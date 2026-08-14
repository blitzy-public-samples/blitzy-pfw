// ==================================================================================================
//  PersistenceWorkScope - THE MISSING HALF OF THE C-05 AND C-06 HANDLE LIFECYCLE
//  ------------------------------------------------------------------------------------------------
//  WHY THIS TYPE EXISTS, STATED AS A FACT ABOUT THE CODE RATHER THAN A DESIGN OPINION
//
//  `persistence.v1` issues an opaque server-held handle for a transaction session and another for a task,
//  and every operating call names one: `QueryRequest.task` and `UpdateRequest.task` are field 1 of each.
//  Building either request with a DEFAULT handle - a message whose task_id is the empty string - is what
//  happens when nothing creates one, and Persistence rejects a blank handle outright:
//  `Grpc/QueryService.cs:L2971-L2980` and `Grpc/UpdateService.cs:L2692-L2697` both answer
//  `RetCode.E_INVALID_HANDLE`. Every retrieval and every update would then be refused before
//  it reached a statement, with the refusal indistinguishable from a caller error.
//
//  `Clients/PersistenceClient.cs` exposes all six lifecycle operations - BeginSession, EndSession,
//  CreateQueryTask, ReleaseQueryTask, CreateUpdateTask, ReleaseUpdateTask - and a service that CALLS NONE
//  OF THEM has published an unreachable capability. This type is what makes them reachable.
//
//  ============ WHY A SCOPE TYPE RATHER THAN SIX CALLS AT EACH SITE ================================
//  Because the release obligation is the part that is easy to get almost right. A handle is server-held
//  state: an unreleased one occupies a registry entry, holds a pooled-transaction reference and, for an
//  update task, a worker task, for the remaining life of the PROCESS. So release has to happen on the
//  fault path, on the cancellation path, and on the early-return path - not just on the success path. A
//  scope makes that one `await using` instead of three `finally` blocks per call site, and it makes the
//  ordering explicit: the TASK is released before the SESSION, because the session owns the pooled
//  transaction the task borrowed.
//
//  ============ WHAT A RELEASE FAILURE DOES, AND DOES NOT DO ======================================
//  It is recorded and swallowed. A release that failed cannot be retried into success by this frame, and
//  raising it would REPLACE the operation's own result - so a successful retrieval whose cleanup hiccuped
//  would be reported to the caller as a failure, and a failing one would have its real cause replaced by
//  its cleanup's. Neither is honest. The failure is logged with its code so an operator can see a registry
//  leaking, and the operation's own answer stands.
//
//  ============ WHAT THE DESCRIPTOR CARRIES, WHICH IS NOTHING =====================================
//  `BeginSession` requires a non-null `TransactionDescriptor` and this sends an EMPTY one, deliberately.
//  A descriptor's fields are a DBMS name, a server, a database, a login identifier, a PASSWORD and a
//  connection-parameter string - every one of them a Persistence concern. AAP 0.6.6 puts all connection
//  material behind Persistence's own options, and `Configuration/DataServicesOptions.cs` accordingly gives
//  this service nothing but an ADDRESS for Persistence. Sending an empty descriptor is what keeps it that
//  way: this service names no database and holds no credential, and Persistence resolves its own
//  connection from its own configuration. It also means every session this service opens keys to the same
//  pooled transaction, which is what the pool's reference counting is for.
// ==================================================================================================

using System.Diagnostics.CodeAnalysis;
using PowerFramework.Contracts.Persistence.V1;
using RetCode = PowerFramework.Shared.Kernel.RetCode;

namespace PowerFramework.DataServices.Clients;

/// <summary>
/// Which task contract a scope was opened against.
/// </summary>
/// <remarks>
/// THE TWO ARE NOT INTERCHANGEABLE, which is why this exists rather than one release path. A query task
/// and an update task live in SEPARATE registries on the server and are released through separate
/// operations; releasing one through the other's operation would answer "no such task" and leave the real
/// one held.
/// </remarks>
internal enum PersistenceWorkKind
{
    /// <summary>A C-05 query task.</summary>
    Query,

    /// <summary>A C-06 update task.</summary>
    Update,
}

/// <summary>
/// One acquired unit of Persistence work: a transaction session, a task on it, and the obligation to
/// release both.
/// </summary>
/// <remarks>
/// <para>
/// ACQUIRED BY THE CLIENT, RELEASED BY DISPOSAL. A caller opens one with
/// <see cref="PersistenceClient.OpenQueryScopeAsync"/> or
/// <see cref="PersistenceClient.OpenUpdateScopeAsync"/>, tests <see cref="IsAcquired"/>, uses
/// <see cref="Task"/> on its request, and disposes it - on every path, which is what
/// <see langword="await"/> <see langword="using"/> guarantees.
/// </para>
/// <para>
/// A FAILED ACQUISITION IS STILL A SCOPE, and disposing it is still correct. Acquisition is two calls, and
/// the second can fail after the first succeeded - so a scope that has a session but no task must still
/// release the session. That is why <see cref="IsAcquired"/> is a property to test rather than a null
/// result to check: a null would have no session to release and the leak would be silent.
/// </para>
/// <para>
/// DISPOSAL IS IDEMPOTENT AND NEVER THROWS. It is reached from a fault path, so a second disposal or a
/// release failure must not add a second exception to the one already in flight.
/// </para>
/// </remarks>
internal sealed class PersistenceWorkScope : IAsyncDisposable
{
    /// <summary>The client the release calls go back through.</summary>
    private readonly PersistenceClient _client;

    /// <summary>Which release operation applies.</summary>
    private readonly PersistenceWorkKind _kind;

    /// <summary>Whether disposal has already run.</summary>
    private bool _disposed;

    /// <summary>
    /// Creates a scope.
    /// </summary>
    /// <param name="client">The client the release calls go back through.</param>
    /// <param name="kind">Which task contract the task belongs to.</param>
    /// <param name="session">The acquired session, or <see langword="null"/> when none was acquired.</param>
    /// <param name="task">The acquired task, or <see langword="null"/> when none was acquired.</param>
    /// <param name="returnCode">The acquisition's outcome.</param>
    /// <param name="errorText">The acquisition's diagnostic, empty when it succeeded.</param>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    internal PersistenceWorkScope(
        PersistenceClient client,
        PersistenceWorkKind kind,
        SessionHandle? session,
        TaskHandle? task,
        long returnCode,
        string errorText)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
        _kind = kind;
        Session = session;
        Task = task;
        ReturnCode = returnCode;
        ErrorText = errorText ?? string.Empty;
    }

    /// <summary>
    /// Which task contract the acquisition was for.
    /// </summary>
    /// <remarks>
    /// EXPOSED SO A REFUSAL CAN BE DESCRIBED IN THE RIGHT CONTRACT'S TERMS. The kind already governed the
    /// release call; a failed acquisition needs it too, because the settings the refused create call carried
    /// are C-05's on a query and C-06's on an update, and a diagnostic that named the wrong set would send a
    /// caller to the wrong published setter.
    /// </remarks>
    internal PersistenceWorkKind Kind => _kind;

    /// <summary>The acquired session, or <see langword="null"/> when acquisition did not get that far.</summary>
    internal SessionHandle? Session { get; }

    /// <summary>The acquired task, or <see langword="null"/> when acquisition did not get that far.</summary>
    internal TaskHandle? Task { get; }

    /// <summary>The acquisition's outcome code.</summary>
    internal long ReturnCode { get; }

    /// <summary>The acquisition's diagnostic, empty when it succeeded.</summary>
    internal string ErrorText { get; }

    /// <summary>
    /// Whether a usable task handle was acquired.
    /// </summary>
    /// <value>
    /// <see langword="true"/> only when a task handle is present and its identifier is non-blank - which is
    /// the exact condition Persistence tests before it will accept the handle at all.
    /// </value>
    /// <remarks>
    /// THE NULL-STATE PROMISE IS DECLARED SO A CALL SITE DOES NOT HAVE TO SUPPRESS ONE. A caller tests this
    /// and then reads <see cref="Task"/>, and without the annotation the compiler cannot see that the test
    /// established the handle - which would force a null-forgiving operator at every call site, and a
    /// suppression is exactly the thing that survives a later change to this property's condition. The
    /// promise is sound by construction: a non-blank <c>Task?.TaskId</c> is only reachable when
    /// <see cref="Task"/> is non-<see langword="null"/>.
    /// </remarks>
    [MemberNotNullWhen(true, nameof(Task))]
    internal bool IsAcquired => !string.IsNullOrWhiteSpace(Task?.TaskId);

    /// <inheritdoc/>
    /// <remarks>
    /// THE TASK IS RELEASED BEFORE THE SESSION, and the order is not cosmetic: the task borrowed the
    /// pooled transaction the session owns, so ending the session first would leave the task holding a
    /// reference to something already collected.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (Task is not null && !string.IsNullOrWhiteSpace(Task.TaskId))
        {
            await _client.ReleaseWorkTaskAsync(_kind, Task).ConfigureAwait(false);
        }

        if (Session is not null && !string.IsNullOrWhiteSpace(Session.SessionId))
        {
            await _client.EndWorkSessionAsync(Session).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Produces a scope that acquired nothing, carrying the reason.
    /// </summary>
    /// <param name="client">The client, for the disposal path that will find nothing to release.</param>
    /// <param name="kind">Which task contract was being opened.</param>
    /// <param name="returnCode">The refusal code.</param>
    /// <param name="errorText">The refusal's diagnostic.</param>
    /// <returns>The scope.</returns>
    internal static PersistenceWorkScope Refused(
        PersistenceClient client,
        PersistenceWorkKind kind,
        long returnCode,
        string errorText) =>
        new(client, kind, session: null, task: null, returnCode, errorText);

    /// <summary>
    /// Produces a scope that acquired a session but no task.
    /// </summary>
    /// <param name="client">The client the session release goes back through.</param>
    /// <param name="kind">Which task contract was being opened.</param>
    /// <param name="session">The session that WAS acquired and must therefore still be released.</param>
    /// <param name="returnCode">The refusal code.</param>
    /// <param name="errorText">The refusal's diagnostic.</param>
    /// <returns>The scope.</returns>
    /// <remarks>
    /// THIS IS THE CASE A NULL RESULT WOULD HAVE LEAKED. The session is live on the server and holds a
    /// pooled-transaction reference; answering null because the task failed would leave nothing holding the
    /// obligation to end it.
    /// </remarks>
    internal static PersistenceWorkScope SessionOnly(
        PersistenceClient client,
        PersistenceWorkKind kind,
        SessionHandle session,
        long returnCode,
        string errorText) =>
        new(client, kind, session, task: null, returnCode, errorText);

    /// <summary>The code reported when an acquisition answered success without producing a handle.</summary>
    /// <remarks>
    /// A structural impossibility rather than a domain outcome, so it takes the code the legacy pool uses
    /// when it cannot produce an object [<c>n_cst_thread_trans_pool.sru:L174</c>] rather than being
    /// dereferenced.
    /// </remarks>
    internal const long MissingHandleCode = RetCode.E_INVALID_OBJECT;

    /// <summary>The diagnostic for a session acquisition that produced no handle.</summary>
    internal const string MissingSessionHandleText =
        "Persistence reported a successful BeginSession without returning a session handle, so there is "
        + "nothing to create a task on.";

    /// <summary>The diagnostic for a task acquisition that produced no handle.</summary>
    internal const string MissingTaskHandleText =
        "Persistence reported a successful task creation without returning a task handle, so there is "
        + "nothing to name on the operating call.";
}
