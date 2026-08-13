// ==================================================================================================
//  PowerFramework.DataServices.Clients.OutboundCallPolicy
//  ------------------------------------------------------------------------------------------------
//  The retry-admission, circuit-breaking and deadline policy for every call DataServices makes
//  outward: the four gRPC channels to Persistence (contracts C-05 through C-08) and the REST channel
//  to Security (contracts C-01 and C-02).
//
//  THIS FILE HAS NO LEGACY SOURCE, AND THAT IS STRUCTURAL RATHER THAN AN OMISSION. The legacy
//  PowerFramework is a library loaded in-process, so every call in it is a synchronous method
//  invocation that cannot fail in transit: there is no legacy retry policy, no legacy deadline and no
//  legacy idempotency classification to port. This policy exists because decomposition created the
//  failure mode, and handling a failure mode the transition itself introduces is required BY the
//  transition rather than layered on top of it (AAP 0.5.3).
//
//  WHY A GENERIC HTTP RESILIENCE PIPELINE IS NOT SUFFICIENT ON A gRPC CHANNEL:
//
//    1. A gRPC call the SERVER refused arrives as HTTP 200, with the refusal in `grpc-status` metadata
//       - in the response headers for a trailers-only answer, in the trailers otherwise. An
//       HTTP-level predicate therefore reads "success" for a server-declared Unavailable and never
//       retries it.
//    2. A TRANSPORT fault arrives as an exception, which every stock transient predicate treats as
//       retryable - and every gRPC call is a POST, so a method-based gate is all-or-nothing. A stock
//       predicate therefore replays an update, an Exec, a commit or a session begin.
//
//  The second is the serious one on THIS edge, because this is the edge that carries the update
//  contract. C-06's whole purpose is that a concurrency conflict is answered definitively rather than
//  overwritten silently, and a transport-level replay of an update after an unknown outcome is
//  precisely the silent double-apply that contract exists to prevent.
//
//  THE CLASSIFICATION RULE, DELIBERATELY CONSERVATIVE. A transport replay is INVISIBLE to the caller:
//  it happens below the client member, so the caller cannot observe it, compensate for it, or decline
//  it. EXACTLY ONE family is admitted - a pure READ, whose repetition changes nothing and is
//  unobservable - and everything else is attempted exactly once.
//
//  🔴 A SECOND FAMILY USED TO BE ADMITTED AND HAS BEEN WITHDRAWN: the "idempotent teardown", meaning a
//  task release. The reasoning was that a lost release response leaks a server-held task against
//  Persistence's quota, so repeating the release is useful rather than merely harmless. The premise was
//  false. The published contract freezes the opposite - "Releasing an already-released handle is
//  E_INVALID_HANDLE, not a silent success" - and all three upstream implementations enforce it, so a
//  replay after a lost response reports a FAILURE for a release that had in fact succeeded. Making
//  release idempotent instead would have been a contract change made to accommodate a retry policy,
//  and it would cost the answer that lets two concurrent releases be resolved. See
//  BuildReplaySafePaths for the full record.
//
//  Note also what the rule excludes that a naive reading would admit: the clause setters.
//  SetWhereClause and SetOrderByClause carry a MODIFICATION STYLE, and a setter carrying the append
//  style applied twice appends twice, so the second attempt of a "failed" call silently produces a
//  different statement.
//
//  NO DURATION HERE IS A LATENCY TARGET, AND NONE IS INVENTED. AAP 0.8.5 forbids asserting a
//  performance objective, because the repository publishes no SLA, latency budget or throughput
//  target to derive one from. Every duration is a CORRECTNESS bound whose derivation is named at the
//  point of use, and each derives from a value already present in this repository.
// ==================================================================================================

using System.Collections.Frozen;
using System.Globalization;
using System.Net.Http.Headers;
using Google.Protobuf.Reflection;
using Grpc.Net.Client.Configuration;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using PowerFramework.Contracts.Persistence.V1;

// ALIASED BECAUSE THE NAME IS TAKEN. Implicit usings bring in
// Microsoft.Extensions.DependencyInjection, whose ServiceDescriptor describes a container registration
// rather than a gRPC contract. The alias says which one is meant at every use site.
using ContractDescriptor = Google.Protobuf.Reflection.ServiceDescriptor;

namespace PowerFramework.DataServices.Clients;

/// <summary>
/// Which class of call a set of <see cref="global::Grpc.Core.CallOptions"/> is being built for, and therefore
/// which deadline bounds it.
/// </summary>
/// <remarks>
/// A unary call is bounded by the point at which the CALLER abandons it; a stream is legitimately
/// long-lived and is bounded instead by the point at which the UPSTREAM would have reclaimed the
/// handle behind it. Collapsing the two would either cut retrievals off mid-flight or leave unary
/// calls effectively unbounded.
/// </remarks>
public enum OutboundCallClass
{
    /// <summary>
    /// A single request and a single response. Bounded by the caller's total request timeout.
    /// </summary>
    Unary = 0,

    /// <summary>
    /// A server-streaming call whose lifetime is the server-held task it reads from.
    /// </summary>
    Stream = 1,
}

/// <summary>
/// The two deadlines DataServices applies to its outbound Persistence calls, and the clock they are
/// measured on.
/// </summary>
/// <remarks>
/// <para>
/// WHY A DEADLINE IS A CORRECTNESS CONCERN AND NOT A PERFORMANCE ONE. Without one, Persistence keeps
/// working - and keeps the query, update, command or transaction handle behind that work alive - for
/// as long as the transport appears open, including after the caller has gone away in a way the
/// transport has not yet noticed. Those handles are bounded per principal and globally, so an
/// abandoned call that is never told to stop consumes admission capacity that a live caller then
/// cannot get. The deadline is how the bound the caller is honouring reaches the server, where it can
/// be acted on.
/// </para>
/// <para>
/// A gRPC deadline is enforced by the CLIENT as a TOTAL bound across every HTTP attempt, because the
/// resilience pipeline sits beneath the gRPC call and its retries are invisible to it. The unary
/// deadline is therefore the pipeline's total request timeout rather than its per-attempt timeout: a
/// shorter value would pre-empt the retry budget the same pipeline was configured with.
/// </para>
/// <para>
/// THE CLOCK IS SUBSTITUTABLE FOR ASSERTION, NOT FOR BEHAVIOUR. gRPC measures the deadline it is given
/// on the system clock, so a test supplying a fake clock observes the value this type computes without
/// changing when a real call expires.
/// </para>
/// </remarks>
public sealed class OutboundDeadlines
{
    private readonly TimeProvider _clock;

    /// <summary>
    /// Creates the deadline pair.
    /// </summary>
    /// <param name="unary">The total bound on a unary call.</param>
    /// <param name="stream">The total bound on a streaming call.</param>
    /// <param name="clock">The clock the absolute deadline is computed from. Defaults to the system clock.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Either duration is zero or negative. A non-positive deadline expires before the request reaches
    /// the upstream, turning every call into a deadline failure.
    /// </exception>
    public OutboundDeadlines(TimeSpan unary, TimeSpan stream, TimeProvider? clock = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(unary, TimeSpan.Zero, nameof(unary));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(stream, TimeSpan.Zero, nameof(stream));

        Unary = unary;
        Stream = stream;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// The pair a client falls back to when no configured instance was supplied.
    /// </summary>
    /// <remarks>
    /// THIS EXISTS SO THAT NO CONSTRUCTION PATH PRODUCES AN UNBOUNDED CALL. A hand-constructed client -
    /// in a test, or in a host that has not bound the configuration section - still sets a deadline,
    /// because "no deadline" is the defect this type was added to remove and leaving it reachable
    /// through an omitted argument would preserve it. The values match the defaults on
    /// <see cref="Configuration.ClientResilienceOptions"/> so the fallback and a configured instance
    /// cannot disagree about the shipped posture.
    /// </remarks>
    public static OutboundDeadlines Default { get; } = new(
        Configuration.ClientResilienceOptions.DefaultRequestTimeout,
        Configuration.ClientResilienceOptions.DefaultStreamDeadline);

    /// <summary>The total bound on a unary call.</summary>
    public TimeSpan Unary { get; }

    /// <summary>The total bound on a streaming call.</summary>
    public TimeSpan Stream { get; }

    /// <summary>
    /// Computes the absolute UTC instant a call of the given class must complete by.
    /// </summary>
    /// <param name="callClass">The class of call.</param>
    /// <returns>The deadline, in UTC, as gRPC requires.</returns>
    /// <remarks>
    /// An unrecognised member is treated as <see cref="OutboundCallClass.Unary"/> rather than left
    /// unbounded, because the safe answer to "which bound applies" is the tighter one.
    /// </remarks>
    public DateTime DeadlineFor(OutboundCallClass callClass)
    {
        TimeSpan budget = callClass is OutboundCallClass.Stream ? Stream : Unary;

        return _clock.GetUtcNow().UtcDateTime + budget;
    }
}

/// <summary>
/// Decides, per outbound operation and per observed outcome, whether an attempt may be replayed and
/// whether it counts against the circuit breaker.
/// </summary>
/// <remarks>
/// <para>
/// A PER-SERVICE COPY BY DESIGN. Gateway carries its own equivalent for its own outbound edges. The
/// classification is a statement about THIS service's callees and their contracts, so sharing one
/// implementation across a service boundary would make behaviour cross a boundary the architecture
/// reserves for published contracts (AAP 0.7.2) and would couple two services' retry postures
/// together. <c>SecurityClient</c> and <c>InternalTlsTrust</c> exist twice for the same reason.
/// </para>
/// <para>
/// THE PATH TABLE IS BUILT FROM THE CONTRACT DESCRIPTORS RATHER THAN FROM STRING LITERALS, so a
/// renamed or removed method is a startup failure instead of an operation that silently falls back to
/// "not replay-safe" and quietly loses its retry. The build also refuses to admit a streaming method,
/// so the two rules cannot drift apart.
/// </para>
/// </remarks>
internal static class OutboundCallPolicy
{
    /// <summary>
    /// The metadata key carrying a gRPC status. Lower-case because gRPC metadata keys are
    /// case-insensitive by specification and are normalised to lower case on the wire.
    /// </summary>
    private const string GrpcStatusHeaderName = "grpc-status";

    /// <summary>
    /// The gRPC status codes an attempt may be replayed for, when - and only when - the operation is
    /// itself replay-safe.
    /// </summary>
    /// <remarks>
    /// <para>
    /// UNAVAILABLE is the canonical "this upstream cannot serve right now" answer: a container still
    /// starting, a listener draining, a connection the platform reset. RESOURCE EXHAUSTED is
    /// Persistence's own admission refusal when a handle registry is at its per-principal or global
    /// ceiling, and it is retryable in the same narrow sense because the condition it reports clears
    /// as handles are released.
    /// </para>
    /// <para>
    /// EVERY OTHER STATUS IS EXCLUDED, and the exclusions carry the policy:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// ABORTED is the optimistic-concurrency answer C-06 is built on - a definitive statement that the
    /// row moved under the caller, whose only correct response is to re-read and rebase. Retrying it is
    /// the silent-overwrite failure mode this system forbids, so it is named rather than merely omitted.
    /// </item>
    /// <item>
    /// CANCELLED means somebody asked for the call to stop; replaying it defeats the request.
    /// </item>
    /// <item>
    /// UNAUTHENTICATED and PERMISSION_DENIED are decisions, not faults: a replay presents the same
    /// credential to the same policy and is refused identically.
    /// </item>
    /// <item>
    /// INVALID_ARGUMENT, FAILED_PRECONDITION and OUT_OF_RANGE are answers about the request, which does
    /// not change between attempts - the chunk-size guard that refuses a value at or below one thousand
    /// is exactly this shape.
    /// </item>
    /// <item>
    /// ALREADY_EXISTS and NOT_FOUND are answers about state; DEADLINE_EXCEEDED means the caller's own
    /// bound has passed so there is no budget left to spend; UNIMPLEMENTED is the deferred-capability
    /// answer; INTERNAL, UNKNOWN and DATA_LOSS are faults whose repetition is not known to be safe.
    /// </item>
    /// </list>
    /// </remarks>
    private static readonly FrozenSet<int> RetryableGrpcStatuses = FrozenSet.ToFrozenSet(
    [
        (int)global::Grpc.Core.StatusCode.Unavailable,
        (int)global::Grpc.Core.StatusCode.ResourceExhausted,
    ]);

    /// <summary>
    /// The gRPC status codes that count as a failure for circuit-breaking purposes.
    /// </summary>
    /// <remarks>
    /// NARROWER THAN THE RETRY SET, DELIBERATELY. A breaker that opens stops every call on the channel,
    /// including the reads that would still have succeeded. RESOURCE_EXHAUSTED is excluded even though
    /// it is retryable: it is a healthy, deliberate refusal aimed at one caller's quota, so breaking the
    /// channel on it would let one caller's over-use deny the whole service. ABORTED, the authorization
    /// statuses and the validation statuses are excluded for the stronger reason that they are correct
    /// answers which happen to be refusals - counting them would let ordinary contested updates trip the
    /// breaker and take the read path down with them.
    /// </remarks>
    private static readonly FrozenSet<int> CircuitBreakingGrpcStatuses = FrozenSet.ToFrozenSet(
    [
        (int)global::Grpc.Core.StatusCode.Unavailable,
    ]);

    /// <summary>The replay-safe methods of C-05, by name on its descriptor.</summary>
    /// <remarks>
    /// <para>
    /// HOISTED SO ONE DEFINITION FEEDS BOTH RETRY LAYERS. The HTTP-level policy classifies by request
    /// path and the gRPC-level policy classifies by method name; if each kept its own roster they would
    /// drift, and the drift would show up as an operation being retried at one layer and not the other -
    /// which for a non-idempotent method is a double-apply. See <see cref="BuildRetryServiceConfig"/>.
    /// </para>
    /// <para>
    /// DECLARED ABOVE <see cref="ReplaySafePaths"/> BECAUSE STATIC INITIALIZERS RUN IN DECLARATION
    /// ORDER. That field is initialised by <see cref="BuildReplaySafePaths"/>, which reads all four
    /// rosters; declared after it, each would still be <see langword="null"/> when the set is built and
    /// class initialization would fail with a <see cref="NullReferenceException"/> at the first outbound
    /// call. Keep them here.
    /// </para>
    /// <para>
    /// 🔴 <b>THE ADMISSION TEST IS THAT A REPLAY GIVES THE CALLER THE SAME ANSWER, NOT MERELY THAT IT
    /// LEAVES THE SERVER IN THE SAME STATE.</b> Only pure reads satisfy it. An operation that CONSUMES
    /// something the server was holding - a task registration, a pending commit - answers differently the
    /// second time even where the end state is identical, and the caller has no way to tell that second
    /// answer apart from a genuine one. See <see cref="BuildReplaySafePaths"/> for the four operations
    /// that were admitted on the weaker test and are not any more.
    /// </para>
    /// </remarks>
    private static readonly string[] ReplaySafeQueryMethods = ["Count"];

    /// <summary>
    /// The replay-safe methods of C-06. EMPTY, and that is a statement rather than an omission - see
    /// <see cref="BuildReplaySafePaths"/> for why the task release is not among them.
    /// </summary>
    private static readonly string[] ReplaySafeUpdateMethods = [];

    /// <summary>
    /// The replay-safe methods of C-07. EMPTY, on the same terms as
    /// <see cref="ReplaySafeUpdateMethods"/>.
    /// </summary>
    private static readonly string[] ReplaySafeCommandMethods = [];

    /// <summary>The replay-safe methods of C-08, by name on its descriptor.</summary>
    /// <remarks>
    /// Five pure reads. <c>AutoCommit</c> was here and is not any more: it COMMITS or ROLLS BACK
    /// [<c>Grpc/TransactionService.AutoCommit</c>], which is the plainest possible non-idempotent
    /// operation and was admitted only because its name reads like a settings query.
    /// </remarks>
    private static readonly string[] ReplaySafeTransactionMethods =
    [
        "GetTransactionData",
        "IsConnected",
        "GetDatabaseType",
        "GetSessionState",
        "GridSyntaxFromSql",
    ];

    /// <summary>
    /// Absolute request paths whose attempts may be replayed at the transport level.
    /// </summary>
    private static readonly FrozenSet<string> ReplaySafePaths = BuildReplaySafePaths();

    /// <summary>
    /// Decides whether a failed attempt may be retried.
    /// </summary>
    /// <param name="arguments">The attempt's outcome and its resilience context.</param>
    /// <returns><see langword="true"/> when the attempt may be replayed.</returns>
    /// <remarks>
    /// <para>
    /// THE ORDER OF THE THREE TESTS IS THE POLICY. The operation is checked FIRST, so an unsafe or
    /// unrecognised operation is refused before any outcome is examined - which means a method added to
    /// a contract without being classified defaults to "attempted exactly once" rather than to
    /// "replayed". Then a readable gRPC status decides alone, because for a gRPC response the HTTP
    /// status is 200 and consulting it would answer "success" for a call the server refused. Only when
    /// no gRPC status is readable - a genuine transport fault, a proxy's own 5xx, or a REST response -
    /// does the package's transient definition apply.
    /// </para>
    /// <para>
    /// A REQUEST THAT CANNOT BE RECOVERED FROM THE CONTEXT IS REFUSED, because with no path there is no
    /// classification and guessing would replay whatever the unknown call happened to be.
    /// </para>
    /// </remarks>
    internal static ValueTask<bool> ShouldRetryAsync(
        RetryPredicateArguments<HttpResponseMessage> arguments)
    {
        if (!IsReplaySafe(ReadRequest(arguments)))
        {
            return ValueTask.FromResult(false);
        }

        if (arguments.Outcome.Result is { } response
            && TryReadGrpcStatus(response, out int grpcStatus))
        {
            return ValueTask.FromResult(RetryableGrpcStatuses.Contains(grpcStatus));
        }

        return ValueTask.FromResult(HttpClientResiliencePredicates.IsTransient(arguments.Outcome));
    }

    /// <summary>
    /// The retry predicate installed on a gRPC channel's HTTP pipeline: it never retries.
    /// </summary>
    /// <param name="arguments">The attempt's outcome. Not read.</param>
    /// <returns>Always <see langword="false"/>.</returns>
    /// <remarks>
    /// <para>
    /// NOT A DISABLED FEATURE - A DELIBERATE DIVISION OF LABOUR. Retry for a gRPC channel belongs to the
    /// gRPC layer, which is strictly better informed: it sees a CONNECT failure, which this pipeline
    /// never does because the balancer establishes the connection outside it, and it also sees a status
    /// delivered in trailers, which is the only failure this pipeline could ever have acted on. Retrying
    /// here as well adds no reachable failure mode and MULTIPLIES the attempt count - four attempts at
    /// each layer makes sixteen calls against an upstream that is by definition already struggling.
    /// </para>
    /// <para>
    /// EXPRESSED AS A PREDICATE RATHER THAN AS ZERO ATTEMPTS because the resilience package's own
    /// validator requires <c>MaxRetryAttempts</c> to be at least one, so "no retries" is not expressible
    /// as a count. A named method rather than an inline lambda so a test can assert the identity of what
    /// is installed instead of inferring it from behaviour.
    /// </para>
    /// <para>
    /// IT IS INSTALLED ON THE FOUR PERSISTENCE CHANNELS ONLY. The Security REST client is not a gRPC
    /// channel, has no service config, and keeps <see cref="ShouldRetryAsync"/> as its only retry
    /// mechanism - which is why that predicate and its thirteen crypto path classifications remain live.
    /// </para>
    /// </remarks>
    internal static ValueTask<bool> NeverRetryAtTheHttpLayerAsync(
        RetryPredicateArguments<HttpResponseMessage> arguments) => ValueTask.FromResult(false);

    /// <summary>
    /// Decides whether an outcome counts against the circuit breaker.
    /// </summary>
    /// <param name="arguments">The outcome and its resilience context.</param>
    /// <returns><see langword="true"/> when the outcome is evidence the upstream is unhealthy.</returns>
    /// <remarks>
    /// UNLIKE RETRY, THIS IS NOT GATED ON THE OPERATION. Whether the upstream is healthy is a property
    /// of the upstream rather than of the call that discovered it, so a server-declared Unavailable on
    /// an update is exactly as much evidence as one on a read. What IS filtered is the meaning of the
    /// answer: a status the server chose deliberately and correctly is not a failure of the server.
    /// </remarks>
    internal static ValueTask<bool> ShouldBreakAsync(
        CircuitBreakerPredicateArguments<HttpResponseMessage> arguments)
    {
        if (arguments.Outcome.Result is { } response
            && TryReadGrpcStatus(response, out int grpcStatus))
        {
            return ValueTask.FromResult(CircuitBreakingGrpcStatuses.Contains(grpcStatus));
        }

        return ValueTask.FromResult(HttpClientResiliencePredicates.IsTransient(arguments.Outcome));
    }

    /// <summary>
    /// Forces the classification table to be built, so a contract mismatch is a startup failure.
    /// </summary>
    /// <returns>The number of classified replay-safe operations.</returns>
    /// <remarks>
    /// The table is built in a static initializer, which would otherwise run at the first outbound
    /// failure - the worst possible moment to discover that a method name no longer resolves. The
    /// composition root calls this while building the host, for the same reason it eagerly resolves the
    /// trust anchor and the client identity there.
    /// </remarks>
    internal static int Verify() => ReplaySafePaths.Count;

    /// <summary>
    /// Answers whether the operation behind this attempt may be replayed.
    /// </summary>
    /// <param name="context">The resilience context the pipeline is executing under.</param>
    /// <returns><see langword="true"/> when the path is classified replay-safe.</returns>
    private static bool IsReplaySafe(HttpRequestMessage? request)
    {
        if (request is null)
        {
            return false;
        }

        string? path = ReadPath(request.RequestUri);

        if (path is null)
        {
            return false;
        }

        return ReplaySafePaths.Contains(path) || IsSafeMethod(request.Method);
    }

    /// <summary>
    /// Recovers the request an attempt was made for, from whichever of the two places carries it.
    /// </summary>
    /// <param name="arguments">The attempt's outcome and its resilience context.</param>
    /// <returns>The request, or <see langword="null"/> when neither place carries one.</returns>
    /// <remarks>
    /// TWO PLACES, ONE OBJECT, AND BOTH ARE POPULATED IN THE DEPLOYED PIPELINE. The resilience handler
    /// puts the outgoing request on the resilience context before it executes the strategy, and the
    /// message handler beneath it sets <see cref="HttpResponseMessage.RequestMessage"/> on the response it
    /// returns, so in production these are the same request and reading either answers identically. The
    /// context is preferred because it is present even when there is no response at all - the
    /// transport-fault case this policy exists to classify. The fallback keeps the predicate answering the
    /// same question the same way when it is interrogated directly rather than driven through a pipeline.
    /// </remarks>
    private static HttpRequestMessage? ReadRequest(
        RetryPredicateArguments<HttpResponseMessage> arguments) =>
        arguments.Context.GetRequestMessage() ?? arguments.Outcome.Result?.RequestMessage;

    /// <summary>
    /// Answers whether a request method is safe in the sense RFC 9110 defines.
    /// </summary>
    /// <param name="method">The request method.</param>
    /// <returns><see langword="true"/> when the method's semantics are read-only.</returns>
    /// <remarks>
    /// <para>
    /// THE SECOND ADMISSION, AND IT EXISTS BECAUSE A PATH TABLE CANNOT SEE A REST GET. The table above is
    /// an enumeration of the operations this service NAMES, and it is the only admission that can apply on
    /// a gRPC channel, where every call is an HTTP POST and the operation is knowable only from the path.
    /// On the Security REST channel the transport itself carries the distinction: RFC 9110 section 9.2.1
    /// DEFINES a safe method as read-only, which is the same property the table's entries were selected
    /// for, established by the protocol rather than argued from a list. So the verification-material fetch
    /// stays retryable while token issuance on the same channel - a POST, and the one call whose replay
    /// would record an issuance of a credential that was never delivered - does not.
    /// </para>
    /// <para>
    /// AND IT CHANGES NOTHING ON A gRPC CHANNEL, which is what keeps the conservative classification
    /// intact where it matters: gRPC transports every call as a POST, so this arm is unreachable on the
    /// four Persistence contracts and an unclassified RPC is still attempted exactly once.
    /// </para>
    /// <para>
    /// EXACTLY THE FOUR SAFE METHODS AND NO OTHERS. PUT and DELETE are idempotent but not safe - repeating
    /// one is defined to leave the same end state, which is a statement about the state rather than about
    /// the work - and neither belongs in a set whose members are admitted because their repetition changes
    /// nothing.
    /// </para>
    /// </remarks>
    private static bool IsSafeMethod(HttpMethod method) =>
        method == HttpMethod.Get
            || method == HttpMethod.Head
            || method == HttpMethod.Options
            || method == HttpMethod.Trace
            || string.Equals(method.Method, "GET", StringComparison.OrdinalIgnoreCase)
            || string.Equals(method.Method, "HEAD", StringComparison.OrdinalIgnoreCase)
            || string.Equals(method.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase)
            || string.Equals(method.Method, "TRACE", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads the request path from a request URI of either form.
    /// </summary>
    /// <param name="requestUri">The URI, which may be absent, absolute or relative.</param>
    /// <returns>The path, or <see langword="null"/> when there is none to read.</returns>
    /// <remarks>
    /// A PREDICATE MUST NEVER THROW, WHICH IS THE ONLY REASON THIS IS NOT ONE PROPERTY ACCESS.
    /// <see cref="Uri.AbsolutePath"/> throws for a RELATIVE URI, and an exception raised inside a
    /// resilience predicate does not fail politely: it replaces the failure the pipeline was deciding
    /// about, so the caller is told about a policy fault instead of about its own upstream. In the
    /// deployed shape the URI is always absolute, because the client factory resolves it against the
    /// configured base address before the handler pipeline runs - so this arm is unreachable in
    /// production and is here because "unreachable" is not a property worth betting a thrown exception
    /// on. The relative form is truncated at a query or fragment so that both forms yield the same
    /// value for the same operation.
    /// </remarks>
    private static string? ReadPath(Uri? requestUri)
    {
        if (requestUri is null)
        {
            return null;
        }

        if (requestUri.IsAbsoluteUri)
        {
            return requestUri.AbsolutePath;
        }

        string original = requestUri.OriginalString;
        int boundary = original.AsSpan().IndexOfAny('?', '#');

        return boundary < 0 ? original : original[..boundary];
    }

    /// <summary>
    /// Reads the gRPC status from a response, when the response carries one where a message handler can
    /// still see it.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <param name="status">Receives the status value.</param>
    /// <returns><see langword="true"/> when a status was read.</returns>
    /// <remarks>
    /// <para>
    /// BOTH PLACES ARE CHECKED, AND ONE OF THEM IS USUALLY EMPTY. A server that failed before writing
    /// any message answers trailers-only and the status is then in the response HEADERS - which is where
    /// every authorization refusal, every handle-quota refusal and every not-ready answer in this system
    /// appears. A server that had already begun writing puts the status in the TRAILERS, which are not
    /// populated until the body has been read to completion, so at the moment a message handler observes
    /// the response they are ordinarily absent. Reading both is the difference between classifying the
    /// statuses that are observable and pretending none are.
    /// </para>
    /// <para>
    /// An unparseable value is treated as no value rather than as a status, so a malformed header falls
    /// through to the transport-level definition instead of silently becoming status zero, which would
    /// read as success.
    /// </para>
    /// </remarks>
    private static bool TryReadGrpcStatus(HttpResponseMessage response, out int status)
    {
        if (TryReadGrpcStatus(response.Headers, out status))
        {
            return true;
        }

        return TryReadGrpcStatus(response.TrailingHeaders, out status);
    }

    /// <summary>
    /// Reads the gRPC status from one header collection.
    /// </summary>
    /// <param name="headers">The collection to read.</param>
    /// <param name="status">Receives the status value.</param>
    /// <returns><see langword="true"/> when a status was read.</returns>
    private static bool TryReadGrpcStatus(HttpHeaders headers, out int status)
    {
        status = 0;

        if (!headers.TryGetValues(GrpcStatusHeaderName, out IEnumerable<string>? values))
        {
            return false;
        }

        foreach (string value in values)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out status))
            {
                return true;
            }
        }

        status = 0;

        return false;
    }

    /// <summary>
    /// Builds the replay-safe path table from the contract descriptors.
    /// </summary>
    /// <returns>The set of absolute request paths whose attempts may be replayed.</returns>
    /// <exception cref="InvalidOperationException">
    /// A classified method does not exist on its contract, or exists but is a streaming method.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE C-05 ENTRY IS <c>Count</c> AND NOTHING ELSE, because it is a read.
    /// <c>CreateQueryTask</c> is absent for the obvious reason: a replayed create SUCCEEDS and leaves a
    /// second server-held task nobody holds a handle to.
    /// </para>
    /// <para>
    /// 🔴 <b>THE THREE TASK RELEASES ARE ABSENT, AND THEY USED TO BE HERE ON A PREMISE THE CONTRACT
    /// CONTRADICTS.</b> They were admitted because a release of an already-released handle was believed to
    /// report success, so a replay after a lost response would cost nothing and would stop a task being
    /// orphaned against the registry's quota. The published contract says the opposite and says it as a
    /// frozen rule - "Releasing an already-released handle is <c>E_INVALID_HANDLE</c>, not a silent
    /// success" [<c>docs/CONTRACTS.md</c>, C-05 method table] - and all three upstream implementations
    /// enforce it: the removal from the handle registry is what decides the outcome, so the second caller
    /// is told the handle is unknown [<c>Persistence/Grpc/QueryService.cs</c>,
    /// <c>UpdateService.cs</c>, <c>CommandService.cs</c>]. A replay therefore turns a release that HAD
    /// SUCCEEDED into a reported failure: the first attempt released the task, its response was lost in
    /// transit, and the retry answers <c>E_INVALID_HANDLE</c> - a spurious failure on a cleanup path,
    /// which is the shape that gets read as a leak and investigated as one.
    /// </para>
    /// <para>
    /// The alternative was to make release idempotent, and it is the wrong one: the answer distinguishing
    /// "this call released it" from "it was already gone" is what makes two concurrent releases resolvable,
    /// and softening it would be a contract change to accommodate a retry policy. So the policy changes
    /// instead, and a lost release response is resolved by the caller's own cleanup path rather than by a
    /// blind replay.
    /// </para>
    /// <para>
    /// <b>C-06 AND C-07 THEREFORE CONTRIBUTE NOTHING AT ALL</b>, at either layer. <c>Update</c> and
    /// <c>Exec</c> were already absent because neither is idempotent and both document themselves as
    /// unsafe to replay blindly - a changeset re-sent after an unknown outcome may apply twice, and a
    /// statement may be neither idempotent nor transactional, the native autocommit mode having
    /// deliberately run outside a transaction - and with their releases gone the two contracts have no
    /// replay-safe operation left. An empty roster is a statement of that, not an oversight.
    /// </para>
    /// <para>
    /// THE C-08 ENTRIES are six reads. <c>BeginSession</c> is absent because each success begins a
    /// SEPARATE reference-counted session that must be ended; <c>EndSession</c> is absent for a sharper
    /// reason still - the legacy pool is REFERENCE COUNTED, so a repeated end DECREMENTS twice and can
    /// tear down a session another borrower still holds, which is worse than the spurious failure a
    /// replayed task release produces.
    /// <c>Commit</c> is absent because a commit whose outcome is unknown must be resolved by reading
    /// state, never by committing again; <c>Rollback</c>, <c>ClearState</c>, <c>SetBroken</c> and
    /// <c>SetAutoCommit</c> are absent because they mutate session state and gain nothing from a replay.
    /// </para>
    /// <para>
    /// THE CLAUSE SETTERS AND THE OTHER QUERY SETTINGS ARE ALL ABSENT, and this is the least obvious
    /// exclusion. <c>SetWhereClause</c> and <c>SetOrderByClause</c> carry a MODIFICATION STYLE: applied
    /// with the append style, a second attempt appends a second copy, so a replay of a call whose
    /// response was lost changes which statement runs. <c>Reset</c>, <c>SetChunkSize</c>,
    /// <c>SetMaxRows</c>, <c>SetPaging</c>, <c>SetPagedUniqueIndexColumns</c> and <c>PrepareUpdate</c>
    /// are excluded as a family rather than argued one at a time, because call ORDER is observable on
    /// this contract - naming the source object clears the statement syntax - and a replay reorders
    /// nothing but does re-apply, which is enough to make the family unsafe to admit wholesale.
    /// </para>
    /// <para>
    /// THE C-01 AND C-02 ENTRIES ARE REST PATHS, so they are literals: there is no descriptor to resolve
    /// them from, and <c>SecurityClient</c> declares the same paths. The thirteen crypto operations
    /// admitted are PURE FUNCTIONS of their request - hashing, keyed hashing, symmetric and RSA
    /// transforms, encoding conversions - so a replay computes the same answer from the same input.
    /// Three siblings are excluded: RSA key generation, because it RETAINS the generated key against a
    /// bounded capacity so a replay consumes that capacity twice, and the three random generators,
    /// because each attempt yields different material by definition. Token issuance is excluded too:
    /// every mint is separately recorded as an issuance, so a replay leaves an audit record of a
    /// credential that was never delivered, and the credential cache keeps issuance off the hot path for
    /// as long as a token is held.
    /// </para>
    /// </remarks>
    private static FrozenSet<string> BuildReplaySafePaths()
    {
        HashSet<string> paths = new(StringComparer.Ordinal);

        // THE ROSTERS ARE THE HOISTED FIELDS, NOT LITERALS REPEATED HERE, so the path table this builds
        // and the gRPC service config BuildRetryServiceConfig builds cannot classify one method
        // differently. They used to be inline collection expressions at these four call sites, which is
        // exactly the arrangement that lets a later edit admit a method at one layer only.
        AddContractMethods(paths, QueryService.Descriptor, ReplaySafeQueryMethods);
        AddContractMethods(paths, UpdateService.Descriptor, ReplaySafeUpdateMethods);
        AddContractMethods(paths, CommandService.Descriptor, ReplaySafeCommandMethods);
        AddContractMethods(paths, TransactionService.Descriptor, ReplaySafeTransactionMethods);

        foreach (string path in
            (string[])
            [
                "/v1/crypto/hash",
                "/v1/crypto/hmac",
                "/v1/crypto/hash-file",
                "/v1/crypto/hmac-file",
                "/v1/crypto/symmetric/encrypt",
                "/v1/crypto/symmetric/decrypt",
                "/v1/crypto/rsa/encrypt",
                "/v1/crypto/rsa/decrypt",
                "/v1/crypto/rsa/sign",
                "/v1/crypto/rsa/verify",
                "/v1/crypto/encoding/string-to-blob",
                "/v1/crypto/encoding/blob-to-string",
                "/v1/crypto/encoding/blob-reverse",
            ])
        {
            paths.Add(path);
        }

        return FrozenSet.ToFrozenSet(paths, StringComparer.Ordinal);
    }

    /// <summary>
    /// Builds the gRPC-level retry configuration for the replay-safe methods of the four contracts.
    /// </summary>
    /// <param name="maxAttempts">The total attempts allowed for one call, INCLUDING the first.</param>
    /// <param name="initialBackoff">The delay before the second attempt.</param>
    /// <returns>A service configuration carrying one method configuration per replay-safe method.</returns>
    /// <remarks>
    /// <para>
    /// 🔴 THIS EXISTS BECAUSE THE HTTP-LEVEL RETRY POLICY CANNOT SEE THE FAILURE THAT MATTERS MOST.
    /// </para>
    /// <para>
    /// The Polly pipeline is attached to the HttpClient's message handler. Grpc.Net establishes its
    /// connection in the BALANCER's subchannel transport, which runs outside that handler - so a call
    /// against a Persistence that is down fails in
    /// <c>SocketConnectivitySubchannelTransport.TryConnectAsync</c> and is never presented to
    /// <see cref="ShouldRetryAsync"/> at all. That was measured on the sibling edge rather than inferred:
    /// with the upstream stopped and a freshly started process, a replay-safe read answered in a quarter
    /// of a second against three configured attempts, and the log recorded
    /// <c>Unavailable / "Error connecting to subchannel"</c> with a <c>SocketException</c> raised inside
    /// the balancer while the pipeline was demonstrably alive for a REST client in the same log. So
    /// "upstream unreachable" - the single most likely transient fault in a decomposed system, and the
    /// one the resilience dependency was taken for - was the one case never retried.
    /// </para>
    /// <para>
    /// gRPC-level retry runs INSIDE the gRPC client, sees connect failures, and is METHOD-SCOPED, which
    /// is why it fits here: this policy is already operation-scoped, so the two layers agree by
    /// construction rather than by coincidence. Both are kept - the HTTP one still owns the circuit
    /// breaker, the per-attempt timeout and the total request timeout - but only one of them retries.
    /// </para>
    /// <para>
    /// ONLY REPLAY-SAFE METHODS ARE NAMED, and a method absent from a service config's method list is
    /// simply not retried. So <c>Query</c>, <c>Update</c>, <c>Exec</c>, <c>Commit</c>, <c>BeginSession</c>,
    /// <c>EndSession</c> and every clause setter remain single-attempt exactly as before: replaying one
    /// would apply an update twice, append a clause twice, or decrement a reference-counted session
    /// another borrower still holds. The exclusions are argued method by method on
    /// <see cref="BuildReplaySafePaths"/> and are not restated here, because there is only one roster.
    /// </para>
    /// <para>
    /// THE RETRYABLE STATUS SET IS <see cref="RetryableGrpcStatuses"/> ITSELF, not a second list, for the
    /// same anti-drift reason the method rosters are hoisted.
    /// </para>
    /// </remarks>
    internal static ServiceConfig BuildRetryServiceConfig(int maxAttempts, TimeSpan initialBackoff)
    {
        ServiceConfig config = new();

        foreach ((ContractDescriptor descriptor, string[] methods) in
            ((ContractDescriptor, string[])[])
            [
                (QueryService.Descriptor, ReplaySafeQueryMethods),
                (UpdateService.Descriptor, ReplaySafeUpdateMethods),
                (CommandService.Descriptor, ReplaySafeCommandMethods),
                (TransactionService.Descriptor, ReplaySafeTransactionMethods),
            ])
        {
            foreach (string method in methods)
            {
                MethodConfig methodConfig = new()
                {
                    RetryPolicy = new RetryPolicy
                    {
                        MaxAttempts = maxAttempts,
                        InitialBackoff = initialBackoff,

                        // Bounded so a long outage cannot grow the delay without limit inside the
                        // caller's total budget, which the attempt and total timeouts still enforce
                        // above this.
                        MaxBackoff = initialBackoff * 8,
                        BackoffMultiplier = 2,
                    },
                };

                foreach (int status in RetryableGrpcStatuses)
                {
                    methodConfig.RetryPolicy.RetryableStatusCodes.Add(
                        (global::Grpc.Core.StatusCode)status);
                }

                methodConfig.Names.Add(new MethodName
                {
                    Service = descriptor.FullName,
                    Method = method,
                });

                config.MethodConfigs.Add(methodConfig);
            }
        }

        return config;
    }

    /// <summary>
    /// Resolves each named method on a contract and adds its absolute request path.
    /// </summary>
    /// <param name="paths">The set being built.</param>
    /// <param name="service">The contract descriptor.</param>
    /// <param name="methodNames">The methods to classify as replay-safe.</param>
    /// <exception cref="InvalidOperationException">
    /// A named method does not exist on the contract, or is a streaming method.
    /// </exception>
    private static void AddContractMethods(
        HashSet<string> paths,
        ContractDescriptor service,
        string[] methodNames)
    {
        foreach (string methodName in methodNames)
        {
            MethodDescriptor? method = service.FindMethodByName(methodName);

            if (method is null)
            {
                throw new InvalidOperationException(
                    $"Contract '{service.FullName}' declares no method '{methodName}', so the outbound "
                        + "retry classification names an operation that no longer exists. Either the "
                        + "contract was changed without updating the classification, or the "
                        + "classification carries a typo - in both cases the operation would otherwise "
                        + "fall through to 'attempted exactly once' silently.");
            }

            if (method.IsClientStreaming || method.IsServerStreaming)
            {
                throw new InvalidOperationException(
                    $"'{service.FullName}/{methodName}' is a streaming method and cannot be classified "
                        + "replay-safe. Replaying a stream re-establishes it from the beginning, which "
                        + "delivers a chunk sequence that never existed - with chunk indices that do "
                        + "not agree with what the consumer already saw - to a consumer that has "
                        + "already read part of the original.");
            }

            paths.Add(string.Concat("/", service.FullName, "/", method.Name));
        }
    }
}
