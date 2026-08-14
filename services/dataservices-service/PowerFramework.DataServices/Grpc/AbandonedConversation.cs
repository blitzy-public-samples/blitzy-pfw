// ==================================================================================================
//  THE UNARY-BOUNDARY TRANSLATION FOR A RACED CONVERSATION TEARDOWN
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS EXISTS FOR, AND WHY IT IS NOT THE FIX FOR THE DEFECT IT GUARDS AGAINST.
//
//  The four headless models are RETAINED PER DATAWINDOW HANDLE and shared between the streaming event
//  chain and the unary read/apply surface, because the legacy shares them: one `se_cst_dw` per
//  DataWindow control holds all five services as instance members [se_cst_dw.sru:L80-L84], so giving
//  the chain its own copies would split state the oracle keeps together. An `EventChain` conversation
//  RE-HOSTS those retained models onto itself for its lifetime - the constructor's `OnInit(this)`
//  fan-out, reproducing [se_cst_dw.sru:L576-L580] - so that a semantic ask raised from inside a model
//  travels to the client that opened the stream.
//
//  THE ROOT-CAUSE FIX IS THE REBIND, NOT THIS FILE. When a conversation ends, the teardown restores the
//  retained models to the DURABLE host - `IDataWindowModelSetProvider.RebindToDurableHost` - so a later
//  unary call raises its semantic events against a host whose default bodies are empty, which is
//  precisely PowerBuilder's "event with no script attached". That removes the deterministic failure.
//
//  WHAT IS LEFT IS A RACE, AND IT IS NARROW BUT REAL. A unary call resolves its model set, and the
//  conversation it was re-hosted onto is torn down before that call reaches the model. The model then
//  reads a host that forwards to a DISPOSED conversation, and `ObjectDisposedException` escapes the
//  handler. gRPC reports an unhandled exception as UNKNOWN with a generic detail, which tells the
//  caller nothing and puts a .NET type name on the wire when detailed errors are enabled.
//
//  SO IT IS TRANSLATED, NOT ANSWERED. FAILED_PRECONDITION with a stable detail: the call arrived while
//  the DataWindow's models were between hosts, and the same call issued again will find them rebound.
//  NOTHING HERE FABRICATES A RESULT - no empty filter, no default menu, no "success" for work that did
//  not happen - because a fabricated answer to an unanswerable semantic ask is the failure mode AAP
//  0.1.5 forbids: the contract is NARROWED WITH A DEFINED ERROR, never widened with a guess.
//
//  ⚠ UNARY ONLY, AND THAT IS THE WHOLE OF THE SCOPE. The duplex `EventChain` and the server-streaming
//  paths are deliberately NOT intercepted. On those paths a question that cannot be answered is a
//  genuine protocol failure - an ordering violation, or an exchange the client abandoned mid-flight -
//  and the drain loop's hard failure is HOW that becomes the call's status. Softening it would convert
//  a broken exchange into a status the client could mistake for a transient condition, and AAP 0.6.1.4
//  assigns the item-change and drop-down-search areas pattern (b), under which an out-of-order arrival
//  is "a hard error, never a reorder opportunity". Adding a streaming override here would contradict
//  that assignment.
//
//  WHY FAILED_PRECONDITION AND NOT ABORTED. ABORTED is spoken for: this service forwards Persistence's
//  update-conflict status as ABORTED and Gateway projects that to HTTP 409, so reusing it here would
//  make a raced teardown indistinguishable from an optimistic-concurrency conflict. RESOURCE_EXHAUSTED
//  is likewise taken, by the ingress bound in GrpcIngressLimit.cs.
//
//  FAILED_PRECONDITION is the status this server ALREADY uses for "the conversation state is not what
//  this call needs" - it is what a message carrying a CLOSED session id is refused with on the streaming
//  path - so this is the CONSISTENT choice rather than an unused one. The two conditions are adjacent by
//  design and are distinguished by their detail, not by their code: one says the session id names nothing
//  live, this one says the models were momentarily between hosts. A caller that treats the whole status
//  as "re-establish and retry" is correct in both cases, which is why sharing it is right.
// ==================================================================================================

using Grpc.Core;
using Grpc.Core.Interceptors;

using Microsoft.Extensions.Logging;

namespace PowerFramework.DataServices.Grpc;

/// <summary>
/// Translates a raced conversation teardown into a defined gRPC status on the UNARY surface.
/// </summary>
/// <remarks>
/// Activated once per call by the gRPC runtime, like every interceptor registered by type. It holds no
/// per-call state, so nothing is carried between calls.
/// </remarks>
internal sealed class AbandonedConversationInterceptor : Interceptor
{
    /// <summary>
    /// The caller-facing detail. STABLE TEXT WITH NOTHING INTERPOLATED FROM THE FAULT.
    /// </summary>
    /// <remarks>
    /// The exception's own <c>ObjectName</c> is a .NET type name and is deliberately absent: it names an
    /// internal type of this service, it tells the caller nothing they can act on, and this service does
    /// not put internal type names on the wire. The METHOD is named instead, because that is the caller's
    /// own vocabulary.
    /// </remarks>
    internal const string Detail =
        "The DataWindow's headless models were between hosts when this call arrived: an event-chain "
        + "conversation for the same DataWindow was being torn down concurrently, and the models are "
        + "restored to their durable host as part of that teardown. Nothing was applied and nothing was "
        + "read. The models are rebound once the teardown completes, so the same call issued again will "
        + "be served.";

    private readonly ILogger<AbandonedConversationInterceptor> _log;

    /// <summary>Creates the interceptor.</summary>
    /// <param name="log">The log the raced call is recorded in.</param>
    /// <exception cref="ArgumentNullException"><paramref name="log"/> is <see langword="null"/>.</exception>
    public AbandonedConversationInterceptor(ILogger<AbandonedConversationInterceptor> log)
    {
        ArgumentNullException.ThrowIfNull(log);

        _log = log;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// THE ONLY OVERRIDE. See this file's banner for why the streaming handlers are left alone.
    /// </remarks>
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(continuation);

        try
        {
            return await continuation(request, context).ConfigureAwait(false);
        }
        catch (ObjectDisposedException disposed)
        {
            // RECORDED AT WARNING, WITH THE EXCEPTION ATTACHED. The race is not an error the operator
            // must act on - the caller can retry and will succeed - but it is not routine either, and a
            // rise in its rate would mean callers are interleaving unary work with a conversation on the
            // same handle far more than expected. The exception is attached rather than interpolated so
            // the type and site stay in the log record while the WIRE detail carries neither.
            _log.LogWarning(
                disposed,
                "A unary call to {Method} raced an event-chain teardown for the same DataWindow and was "
                    + "refused with FAILED_PRECONDITION. Nothing was applied and nothing was read.",
                context.Method);

            throw new RpcException(new Status(StatusCode.FailedPrecondition, Detail));
        }
    }
}
