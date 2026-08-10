// ==================================================================================================
//  Grpc/CommandService.cs - CONTRACT C-07, persistence.v1.CommandService
//  ------------------------------------------------------------------------------------------------
//  ROLE: A THIN ADAPTER, AND NOTHING MORE.
//  Validate, translate, delegate to Tasks/, map the result. Every behaviour this file appears to have
//  actually lives in a sibling folder, and the four behaviours the contract names for Exec are all in
//  that category - they are WIRED THROUGH here, never re-implemented:
//
//      positional parameter binding                 ->  Tasks/SqlTaskBase.cs (BindParams, ParamToString)
//      the leading-`@` execution mode               ->  Transactions/TransactionPool.cs
//                                                       (IPooledTransaction / PooledTransaction)
//      multi-statement batch execution              ->  Transactions/TransactionPool.cs
//      error-text retrieval                         ->  Transactions/TransactionPool.cs
//      the THREE-MODE autocommit epilogue           ->  Tasks/SqlCommandTask.cs (OnDoTask)
//      the caller-side busy guards and the
//        parameter-shape rejections                 ->  Tasks/TaskProxies/SqlCommandTaskProxy.cs
//                                                       and SqlTaskProxyBase.cs
//      the driver-error payload and its redaction   ->  Errors/DbErrorData.cs, Errors/SqlRedactor.cs
//
//  What is genuinely THIS file's own is the boundary itself: the task-handle correlation the legacy
//  had no need for, the translation of a wire parameter list into the oracle's append-ordered
//  collection, and the projection of one execution onto ExecResponse's six fields.
//
//  WHY THIS SERVICE IS gRPC (constraint C-K)
//  The legacy surface being ported is ACTION-ORIENTED AND THEREFORE RPC-SHAPED, NOT
//  RESOURCE-ORIENTED. Read the oracle's own prototypes and the shape is unmistakable - of_setsql,
//  of_setautocommit, of_reset [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlcommand.sru:
//  L27-L29] over a transaction surface of of_exec, of_commit, of_rollback and an error-text accessor
//  [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L78-L84]. Those are verbs, not
//  representations: there is no collection to GET and no entity to PUT. Three further properties push
//  the same way. The database-error structure requires a STRUCTURED payload rather than a message
//  string [ws_objects/pfw.thread.ext.pbl.src/dberrordata.srs:L4-L8]. The thirteen SQL task classes
//  carry MANDATORY THREAD AFFINITY in their own source comments - this contract's worker is labelled
//  [运行在子线程] "runs on the worker thread" [n_cst_thread_task_sqlcommand.sru:L2] against its
//  caller-side twin's [运行在当前线程] "runs on the calling thread"
//  [n_cst_threading_task_sqlcommand.sru:L2] - and that duality is contract rather than commentary.
//  And the outcome model has to carry a tri-state algebra plus a FOUR-VALUE autocommit domain rather
//  than a boolean. Protocol buffers over gRPC carry all of it with compile-time contract enforcement;
//  JSON over REST would carry none of it well.
//
//  WHAT THIS FILE DELIBERATELY DOES NOT DO
//    * It declares NO route, NO health endpoint and NO ping endpoint. /health is this service's only
//      anonymous surface and both routes live in Endpoints/, on port 5101 (constraint C-L).
//    * It adds NO <Protobuf> item to the project. shared/PowerFramework.Contracts owns the single
//      GrpcServices="Both" item; a second would compile the messages twice (constraint C-A).
//    * It references NO peer service project and exposes NO shared behaviour across the boundary.
//      The published contracts project is the only permitted cross-service coupling (constraint C-A).
//    * It mints NO token and holds NO signing key. Security is the sole issuer; this service holds
//      verification material only and validates against Security's published key set
//      (constraints C-F, C-G).
//    * It opens NO connection to SQL Server or Oracle. The two DatabaseType values select PURE STRING
//      TRANSFORMS under Sql/Paging/ and literal formats for parameter interpolation, and nothing else
//      (constraint C-E).
//    * It declares NO route, handler, options type or placeholder for DesignSystem, Documents,
//      Integration or ScriptBridge, and throws NO NotImplementedException. Those four reserved routes
//      are declarations on GATEWAY's routing table alone (constraint C-D).
//    * It DECLARES no SCREAMING_SNAKE constant. This folder is outside every naming-suppression glob
//      in the repository .editorconfig while TreatWarningsAsErrors is true, so the preserved legacy
//      identifiers are CONSUMED - the autocommit triple and the database-type values from the
//      generated contract enums, the return codes from PowerFramework.Shared.Kernel - and never
//      redeclared here. No type below shadows a generated contract type either.
//    * It re-implements NO status mapping and throws NO RpcException. persistence.v1.OperationStatus
//      is "the uniform outcome of every unary call in this file" by the contract's own words, so every
//      outcome - success, guard refusal, driver failure - is reported in ret_code. NOTHING IN C-07
//      PRODUCES Aborted; that is C-06's concern, and its conflict detail has no analogue here because
//      a command carries no row state to conflict over.
//    * It ENFORCES NO ARGUMENT-COUNT CEILING. See the note on Exec: the legacy ceilings are recorded
//      and deliberately not reproduced as validation (constraint C-K).
//
//  ============ THIS FILE IS C-07'S CONCRETE REDACTION SITE (constraint C-F) ======================
//  The failure epilogue of the oracle's task body passes THE FULLY BOUND, FULLY INTERPOLATED STATEMENT
//  into the database-error event -
//      Event OnDBError(transObject.SQLDBCode, transObject.SQLErrText, sSQL, Primary!, 0)
//  [n_cst_thread_task_sqlcommand.sru:L110] - and the legacy logger performs no redaction at all. With
//  bind variables disabled the runtime interpolates values as literals rather than binding them
//  [n_cst_thread_task_sqlbase.sru:L128], so that text can carry live row data. EVERY wire DbError this
//  file emits is produced by the sanctioned projection in Errors/SqlRedactor.cs, reached through the
//  single Status overload that takes a payload; DbError.Sqlsyntax is NEVER assigned anywhere below,
//  and no statement text is ever interpolated into a log record or an exception message.
//  ==========================================================================================
//
//  LEGACY SPECIFICATION (READ ONLY - never edited, never moved, never reformatted: constraint C-C)
//  Every behavioural claim in the comments below carries a ws_objects/** locator, because nothing else
//  in this repository can adjudicate it - the changelog stops at framework 3.0.7.2062 while the commit
//  history runs years later, and neither PowerBuilder project object would build as written.
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlcommand.sru      the worker, 116 lines
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlcommand.sru   its caller-side twin
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru      the caller-side base
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru         the worker-side base
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru                the transaction surface
//      ws_objects/pfw.thread.ext.pbl.src/dberrordata.srs                      the driver-error record
//      ws_objects/pfw.utility.sqlite.pbl.src/n_sqlite.sru                      the arity ceiling
//      ws_objects/pfw.shared.pbl.src/retcode.sru                              the return-code algebra
//  Unqualified [:Lnnn] references inside a member's comments name
//  n_cst_thread_task_sqlcommand.sru, which is this contract's primary oracle; every reference to any
//  other file is spelled in full.
//
//  PRESERVED SEMANTICS, EACH ANNOTATED AT ITS POINT OF REPRODUCTION (constraint C-B)
//      AC_OFF as the default, and an unset wire field meaning AC_OFF     SetAutoCommit, Exec
//      the autocommit setter validating NOTHING                          SetAutoCommit
//      AC_NATIVE toggling the flag instead of committing, and its
//        failure arm performing NO rollback of its own                   Exec (delegated + annotated)
//      an empty statement answering E_INVALID_SQL, not E_INVALID_ARGUMENT  SetSql, Exec
//      the DOUBLE empty-statement guard, kept as two guards              SetSql, Exec
//      the ordered failure arms and their exact codes                    Exec
//      SQLCode = 100 reading as SUCCESS                                  Exec (sql_code projection)
//      the SQLCode-based predicates, distinct from Predicates.*          Exec
//      the vetoed before-command discrimination                          Exec (delegated + annotated)
//      E_BUSY on every mutator while a command is in flight              Reset, SetAutoCommit, SetSql, Exec
//      a positional parameter being the empty-NAMED case                 Exec
//      the 2-D-array and empty-array parameter rejections                Exec (delegated + annotated)
//      the dormant commented-out null check, carried across inert        Exec
//      of_iscommitted as a strictly NON-BLOCKING poll                    Exec (committed projection)
//      the 11 / 20 / 8 argument ceilings, RECORDED and NOT ENFORCED      Exec
//
//  NO RULES GOVERN THIS FILE. review_rules reports that no user rules were provided, which is not
//  latitude: the enterprise-standard baseline applies in their place - nullable reference types with
//  warnings as errors, no secret in source or settings, structured logging with the statement field
//  redacted, parameterized execution beneath an observably unchanged generated statement, and
//  versioned contracts as the only cross-service coupling.
// ==================================================================================================

// IMPORT PROVENANCE, RECORDED BECAUSE THIS FILE STRADDLES SEVERAL LAYERS AND ONE IMPORT DESERVES ITS
// REASON IN WRITING.
//   * Contracts.Persistence.V1 is the PUBLISHED BOUNDARY and the only cross-service coupling (C-A).
//   * Persistence.Tasks and .TaskProxies supply the ORACLE'S PROXY PAIR, whose duality this adapter
//     preserves rather than flattens.
//   * Persistence.Transactions supplies the pooled-connection surface the accessors are read from.
//   * Persistence.Errors supplies the driver payload and its sanctioned wire projection.
//   * Persistence.Buffers supplies ONE member - CarrierValue.TryFromWire - which is this assembly's
//     sanctioned reader for a wire AnyValue. It is CONSUMED RATHER THAN REIMPLEMENTED on purpose: the
//     type is a discriminated union over eleven arms including three composite ones, and a second
//     reader in the same assembly would be a second place for the null-versus-absent distinction and
//     the date and decimal parsing to drift. Same assembly, so no new coupling of any kind is created.
//   * PowerFramework.Contracts.Common.V1 is deliberately ABSENT; see the alias note below.
//   * Persistence.Configuration is deliberately ABSENT too: this adapter reads no option. Storage and
//     pool settings belong to the collaborators that own them, and an unused import would imply
//     otherwise.
using System.Collections.Concurrent;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using PowerFramework.Contracts.Persistence.V1;
using PowerFramework.Persistence.Buffers;
using PowerFramework.Persistence.Errors;
using PowerFramework.Persistence.Tasks;
using PowerFramework.Persistence.Tasks.TaskProxies;
using PowerFramework.Persistence.Transactions;

// The generated C-07 service base, reached through an alias for two reasons. First, the mandated class
// name below is the contract's own service name, so the bare name has to resolve to exactly one type
// and the alias is what guarantees which - without it, `CommandService` inside this namespace would be
// ambiguous between the generated static wrapper class and the type declared here. Second, an alias
// makes the derivation visible at the class declaration instead of hiding a fully qualified name in
// it. This is the same shape the sibling adapter uses at Grpc/TransactionService.cs.
using GeneratedCommandServiceBase =
    global::PowerFramework.Contracts.Persistence.V1.CommandService.CommandServiceBase;

// RetCode names the IN-PROCESS algebra - PowerFramework.Shared.Kernel.RetCode, a static class of
// `long` constants transcribed from ws_objects/pfw.shared.pbl.src/retcode.sru. It is what every
// collaborator below returns and what every guard tests. The WIRE form is the generated proto enum
// PowerFramework.Contracts.Common.V1.RetCode.Types.Value, and this file never names it: every
// conversion goes through TransactionWireCodes, for the review-invariant reason recorded on Status
// below. That is also why PowerFramework.Contracts.Common.V1 is deliberately NOT imported here - the
// wrapper message in it is also called RetCode, and not importing it keeps the bare name unambiguous.
using Predicates = PowerFramework.Shared.Kernel.Predicates;
using RetCode = PowerFramework.Shared.Kernel.RetCode;

namespace PowerFramework.Persistence.Grpc;

// --------------------------------------------------------------------------------------------------
//  PART 1 OF 3 - THE COMPOSITION SEAM FOR THE ORACLE'S PROXY PAIR
// --------------------------------------------------------------------------------------------------

/// <summary>
/// The two halves of the legacy proxy pair for one command task, handed over together.
/// </summary>
/// <remarks>
/// <para>
/// <b>THE PAIR IS DELIBERATELY NOT FUSED INTO ONE OBJECT, AND THAT IS THE WHOLE POINT.</b> The oracle's
/// concurrency design is a proxy pair in which every SQL class exists TWICE - a caller-side
/// <c>n_cst_threading_task_sqlcommand</c> documented [运行在当前线程] "runs on the calling thread"
/// [<c>n_cst_threading_task_sqlcommand.sru:L2</c>] and a worker-side
/// <c>n_cst_thread_task_sqlcommand</c> documented [运行在子线程] "runs on the worker thread"
/// [<c>n_cst_thread_task_sqlcommand.sru:L2</c>] - specifically so that no object is ever touched from
/// two threads. The migration plan names that thread affinity as CONTRACT rather than commentary and
/// forbids flattening the pair into a single async method, so the adapter holds both halves and drives
/// each on its own side of the boundary: the mutators go to the proxy, the task body to the worker.
/// </para>
/// <para>
/// A class rather than a <see langword="record"/> <see langword="struct"/>, so that "no components"
/// is expressible as a genuine <see langword="null"/> rather than as a default instance whose two
/// non-nullable members are silently null - a nullability hole the compiler does not report for
/// structs.
/// </para>
/// </remarks>
internal sealed class CommandTaskComponents
{
    /// <summary>
    /// Pairs a caller-side proxy with the worker it drives.
    /// </summary>
    /// <param name="proxy">
    /// The caller-side half. Must already have been initialized, so that it has borrowed the worker's
    /// commit signal - see <see cref="ICommandTaskFactory.Create"/>.
    /// </param>
    /// <param name="worker">The worker-side half, reachable through this proxy's substrate seam.</param>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    internal CommandTaskComponents(SqlCommandTaskProxy proxy, SqlCommandTask worker)
    {
        Proxy = proxy ?? throw new ArgumentNullException(nameof(proxy));
        Worker = worker ?? throw new ArgumentNullException(nameof(worker));
    }

    /// <summary>
    /// The caller-side proxy - the port of <c>n_cst_threading_task_sqlcommand</c>.
    /// </summary>
    /// <value>
    /// Every mutator this service exposes is routed here, because every one of them is declared on the
    /// CALLER-SIDE object in the oracle and opens with that object's busy guard.
    /// </value>
    internal SqlCommandTaskProxy Proxy { get; }

    /// <summary>
    /// The worker - the port of <c>n_cst_thread_task_sqlcommand</c>.
    /// </summary>
    /// <value>
    /// Driven for exactly one purpose: running the task body, which is the port of
    /// <c>event ondotask</c> [<c>:L60-L115</c>] and owns the whole three-mode epilogue.
    /// </value>
    internal SqlCommandTask Worker { get; }
}

/// <summary>
/// Creates one command-task proxy pair. The composition seam this adapter is built behind.
/// </summary>
/// <remarks>
/// <para>
/// <b>AN INJECTED SEAM RATHER THAN A DIRECT CONSTRUCTION, FOR THE SAME REASON THE SIBLING ADAPTER
/// INJECTS ITS TRANSACTION SURFACE (constraint C-H).</b> Building the pair requires the threading
/// substrate on both sides - <c>ISqlTaskHost</c> for the worker and <c>ISqlTaskProxyHost</c> for the
/// proxy - together with the pool, the carrier factory, the retrieval-hook activator and the clock.
/// Reaching for those here would make this adapter untestable without a real thread and would put a
/// composition decision inside a boundary translator. Behind this interface the adapter is exercisable
/// with no thread, no pool and no database, which is what keeps the 80%-per-service coverage gate
/// reachable.
/// </para>
/// <para>
/// <b>THE FACTORY OWNS ONE ORDERING OBLIGATION, AND IT IS OBSERVABLE.</b> The implementation MUST have
/// initialized the proxy before returning it, because initialization is what borrows the worker's
/// commit signal - <c>_hEvtCommitted = task.of_GetCommitEvent()</c>
/// [<c>n_cst_threading_task_sqlbase.sru:L218</c>] - and the worker's committed notification signals
/// only an ALREADY CREATED handle [<c>n_cst_thread_task_sqlbase.sru:L100-L111</c>]. A pair returned
/// uninitialized would therefore run correctly and report <c>ExecResponse.committed</c> as
/// <see langword="false"/> on a commit that genuinely happened, which is precisely the understated
/// durability the contract warns against.
/// </para>
/// </remarks>
internal interface ICommandTaskFactory
{
    /// <summary>
    /// Builds an initialized command-task proxy pair.
    /// </summary>
    /// <param name="components">
    /// The pair on success; <see langword="null"/> on any non-succeeding outcome.
    /// </param>
    /// <returns>
    /// <see cref="RetCode.OK"/> on success, or the substrate's own code. <b>Tested with
    /// <c>Predicates.IsSucceeded</c> by the caller, so the tri-state algebra applies</b>: an
    /// implementation answering <see cref="RetCode.PREVENT"/> is reporting a SUCCESS and must therefore
    /// still produce the pair. Note this differs from the proxy's own initialization, which tests a
    /// LITERAL inequality against <see cref="RetCode.OK"/> and so aborts on a prevention
    /// [<c>n_cst_threading_task_sqlbase.sru:L215</c>]; the difference is the oracle's and is not
    /// harmonized here.
    /// </returns>
    long Create(out CommandTaskComponents? components);
}

// --------------------------------------------------------------------------------------------------
//  PART 2 OF 3 - THE SERVER-HELD TASK AND ITS HANDLE TABLE
// --------------------------------------------------------------------------------------------------

/// <summary>
/// One server-held command task: its opaque wire handle, the session it runs against, and the proxy
/// pair that carries it.
/// </summary>
/// <remarks>
/// <para>
/// <b>THE LEGACY HAD NO EQUIVALENT, WHICH IS WHY THIS TYPE IS NEW.</b> In the oracle a command task IS
/// an object - created, configured through its setters and then run, all in one address space - so
/// object identity was the handle. Across a network boundary there is no pointer to pass, so the
/// contract issues an opaque <c>TaskHandle</c> and the caller names it on every subsequent call. That
/// is the whole of what this type adds; every behaviour still lives on the pair it holds.
/// </para>
/// <para>
/// <b>CORRELATION IS EXPLICIT AND NEVER AMBIENT.</b> There is no thread-local, no asynchronous-local
/// and no per-connection implicit state anywhere in this service: a caller names its task on every
/// request, and this object is the only thing that maps that name onto a proxy pair. Nothing here is
/// portable across service instances, and the contract says so - a handle is server-issued,
/// unparseable, and NOT A CREDENTIAL, authorizing nothing. Authentication on this edge is the bearer
/// configuration installed by <c>Program.cs</c> (constraints C-F, C-G).
/// </para>
/// <para>
/// <b>The session is captured at creation and never re-resolved.</b> The oracle installs a transaction
/// DESCRIPTOR on the task once - <c>of_settransdata</c>
/// [<c>n_cst_threading_task_sqlbase.sru:L107-L120</c>] - and the task then resolves it to a pooled
/// connection on each run [<c>n_cst_thread_task_sqlbase.sru:L148-L184</c>]. Binding the task to one
/// session reproduces that: the descriptor cannot change under a live task, so a caller cannot move a
/// half-configured command onto a different connection.
/// </para>
/// </remarks>
internal sealed class CommandTask : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// Creates a task record.
    /// </summary>
    /// <param name="taskId">The opaque, server-issued wire identity.</param>
    /// <param name="session">The session this task runs against.</param>
    /// <param name="components">The initialized proxy pair.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    internal CommandTask(string taskId, TransactionSession session, CommandTaskComponents components)
    {
        TaskId = taskId ?? throw new ArgumentNullException(nameof(taskId));
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Components = components ?? throw new ArgumentNullException(nameof(components));
    }

    /// <summary>
    /// The opaque wire identity of this task.
    /// </summary>
    internal string TaskId { get; }

    /// <summary>
    /// The session this task runs against, and therefore the source of both the descriptor it was
    /// configured with and the gate every execution is serialized on.
    /// </summary>
    internal TransactionSession Session { get; }

    /// <summary>
    /// The proxy pair. Both halves are driven, each on its own side of the boundary.
    /// </summary>
    internal CommandTaskComponents Components { get; }

    /// <summary>
    /// The caller-side half, for readability at the call sites that only need it.
    /// </summary>
    internal SqlCommandTaskProxy Proxy => Components.Proxy;

    /// <summary>
    /// The worker, for readability at the one call site that needs it.
    /// </summary>
    internal SqlCommandTask Worker => Components.Worker;

    /// <summary>
    /// Releases both halves of the pair - the wire form of the legacy <c>Destroy</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE ORDER IS LOAD-BEARING: PROXY FIRST, WORKER SECOND.</b> The proxy BORROWS the worker's
    /// commit signal rather than owning it [<c>n_cst_threading_task_sqlbase.sru:L218</c>], and the
    /// worker's own disposal is what disposes that signal. Releasing the worker first would leave the
    /// proxy holding a disposed handle, so a stray <c>IsCommitted</c> read would fault instead of
    /// answering <see langword="false"/>. The sibling proxy test harness disposes in exactly this
    /// order for exactly this reason.
    /// </para>
    /// <para>
    /// The worker's disposal is also what returns its pool reference
    /// [<c>n_cst_thread_task_sqlbase.sru:L160</c>, <c>:L165</c> take it], so failing to release a task
    /// would pin its connection open. Idempotent, so a duplicate release is harmless.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Components.Proxy.Dispose();
        Components.Worker.Dispose();
    }
}

/// <summary>
/// The process-wide table mapping opaque task handles onto <see cref="CommandTask"/> records.
/// Registered as a singleton; injected, never reached statically.
/// </summary>
/// <remarks>
/// <para>
/// <b>A SINGLETON IS REQUIRED RATHER THAN CONVENIENT.</b> gRPC service instances are resolved per call,
/// so a table held by the service itself would be discarded between <c>CreateCommandTask</c> and the
/// first <c>SetSql</c>, and every handle would be unknown on arrival. Holding it here, injected, keeps
/// the lifetime correct while leaving the correlation explicit and leaving the service substitutable in
/// a test (constraint C-H).
/// </para>
/// <para>
/// <b>Ordinal comparison, deliberately.</b> A handle is an opaque server-issued token, so it is matched
/// byte for byte; culture-sensitive or case-insensitive matching would let two distinct tokens collide
/// on some hosts and not others.
/// </para>
/// <para>
/// <b>This table holds no behaviour and duplicates none.</b> Task state - the statement, the autocommit
/// mode, the parameter collection, the running flag and the commit signal - lives entirely on the pair,
/// which is where the oracle keeps it. The table adds only the handle-to-object mapping that a network
/// boundary needs and an in-process library did not.
/// </para>
/// </remarks>
internal sealed class CommandTaskRegistry
{
    private readonly ConcurrentDictionary<string, CommandTask> _tasks = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a proxy pair under a freshly issued handle.
    /// </summary>
    /// <param name="session">The session the task runs against.</param>
    /// <param name="components">The initialized proxy pair.</param>
    /// <returns>The registered record, carrying the handle callers must quote thereafter.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>The identity is an opaque GUID with no structure a consumer could parse.</b> The contract
    /// requires exactly that, and it is also why a characterization comparison must mask the field on
    /// BOTH sides rather than expect it to match.
    /// </para>
    /// <para>
    /// The insertion cannot collide in practice and is not permitted to overwrite in principle: a
    /// failure to add would mean a repeated GUID, which is a structural fault rather than a recoverable
    /// one, and the loop simply re-issues rather than silently replacing a live task some other caller
    /// still holds.
    /// </para>
    /// </remarks>
    internal CommandTask Register(TransactionSession session, CommandTaskComponents components)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(components);

        while (true)
        {
            CommandTask task = new(Guid.NewGuid().ToString("N"), session, components);

            if (_tasks.TryAdd(task.TaskId, task))
            {
                return task;
            }
        }
    }

    /// <summary>
    /// Resolves a wire handle onto its task.
    /// </summary>
    /// <param name="handle">
    /// The handle from the request. <see langword="null"/>, an empty identity and an unknown identity
    /// are all treated identically - see the remarks.
    /// </param>
    /// <param name="task">The resolved task, or <see langword="null"/> when unresolved.</param>
    /// <returns><see langword="true"/> when <paramref name="handle"/> names a live task.</returns>
    /// <remarks>
    /// <b>An absent handle, a blank handle and a stale handle are ONE outcome, not three.</b> The
    /// contract fixes the code for a stale, foreign or unknown task handle at
    /// <see cref="RetCode.E_INVALID_HANDLE"/>. Distinguishing the three would tell a caller which of
    /// its guesses was closer to a live handle, and it would answer a question the oracle - where a
    /// task handle was a pointer - has no answer for.
    /// </remarks>
    internal bool TryResolve(TaskHandle? handle, out CommandTask? task)
    {
        string? taskId = handle?.TaskId;

        if (string.IsNullOrEmpty(taskId))
        {
            task = null;
            return false;
        }

        return _tasks.TryGetValue(taskId, out task);
    }

    /// <summary>
    /// Removes a task from the table, so that its handle is single-use.
    /// </summary>
    /// <param name="taskId">The identity to remove.</param>
    /// <param name="task">The removed record, or <see langword="null"/> when nothing was removed.</param>
    /// <returns>
    /// <see langword="true"/> when this call is the one that removed it. Concurrent callers releasing
    /// the same task therefore produce exactly one removal - and therefore exactly one disposal - and
    /// the loser sees the same unknown-handle outcome as any other stale handle.
    /// </returns>
    internal bool TryRemove(string taskId, out CommandTask? task) => _tasks.TryRemove(taskId, out task);
}

// --------------------------------------------------------------------------------------------------
//  PART 3 OF 3 - THE ADAPTER
// --------------------------------------------------------------------------------------------------

/// <summary>
/// Serves contract C-07, <c>persistence.v1.CommandService</c>: a statement that returns no result set,
/// executed under the legacy's THREE-VALUED autocommit policy.
/// </summary>
/// <remarks>
/// <para>
/// <b>EVERY METHOD IS AUTHORIZED, AND THE ENFORCEMENT IS DELIBERATELY NOT HERE (constraint C-G).</b>
/// <c>Program.cs</c> installs a fallback authorization policy requiring an authenticated principal, so
/// an endpoint with no authorization metadata of its own is a CLOSED door rather than an open one. This
/// class therefore carries no <c>[Authorize]</c> attribute - it would be redundant - and, more to the
/// point, carries no <c>[AllowAnonymous]</c> anywhere: <c>/health</c> is this service's only anonymous
/// surface and it lives in <c>Endpoints/</c>. This service VALIDATES tokens against Security's
/// published key set and NEVER MINTS one; Security is the sole issuer in the system.
/// </para>
/// <para>
/// <b>NO HANDLER THROWS <c>RpcException</c>, AND NONE MAPS A TRANSPORT STATUS.</b> The contract defines
/// <c>OperationStatus</c> as the uniform outcome of every unary call, so a guard refusal and a driver
/// failure are both reported in <c>ret_code</c> rather than as a transport fault. Transport-status
/// mapping is the composition root's concern and is consumed, not re-implemented, here. C-07 has no
/// <c>Aborted</c> path at all - that belongs to C-06's optimistic-concurrency contract, and a command
/// carries no row state to conflict over.
/// </para>
/// <para>
/// <b>THE ONE ARGUMENT SHAPE THAT DOES THROW.</b> A <see langword="null"/> request message is a
/// structural fault in the transport rather than a domain outcome, and the oracle has no return code
/// for it, so it is answered with <see cref="ArgumentNullException"/> exactly as the sibling adapter
/// answers it. Fail fast on a structural fault is the framework's own posture
/// [<c>ws_objects/pfw.pbl.src/pfw.sra:L111-L144</c>] and is preserved rather than softened.
/// </para>
/// <para>
/// <b>EXECUTION IS SERIALIZED ON THE SESSION'S GATE, WHICH REPRODUCES THE ORACLE'S THREAD AFFINITY.</b>
/// A gRPC server is concurrent by nature, whereas the oracle structurally prevents two threads from
/// touching one transaction: its pool lives on ONE worker thread, stored per-thread under a named
/// framework datum [<c>n_cst_thread_task_sqlbase.sru:L194-L206</c>], so every referrer of an entry is
/// on that thread by construction. The gate is owned by <c>TransactionSession</c> and keyed on the
/// BORROWED TRANSACTION rather than on the session, because the pool hands equal descriptors the same
/// connection - so two command tasks on two sessions over one connection share one gate. Holding it
/// across the task body is what makes reading the driver's four numeric accessors afterwards a read of
/// THIS execution's outcome rather than of a racing one.
/// </para>
/// </remarks>
internal sealed class CommandService : GeneratedCommandServiceBase
{
    /// <summary>
    /// The diagnostic for a task handle that names nothing live. Quotes no value, so a caller cannot
    /// learn anything about the handle space from it (constraint C-F).
    /// </summary>
    private const string UnknownTaskDiagnostic =
        "The task handle does not name a live command task on this service instance. A handle is "
        + "server-issued, single-use once released, and not portable across instances.";

    /// <summary>
    /// The diagnostic for a session handle that names nothing live.
    /// </summary>
    private const string UnknownSessionDiagnostic =
        "The session handle does not name a live transaction session on this service instance. Open one "
        + "through the transaction contract's session lifecycle before creating a command task.";

    /// <summary>
    /// The diagnostic for a parameter whose wire value carries no arm at all.
    /// </summary>
    private const string ParameterValueMissingDiagnostic =
        "A positional parameter carries no value arm. An explicitly null value is stated by setting the "
        + "null arm; a message with no arm set is an incomplete payload and is refused rather than read "
        + "as null.";

    private readonly TransactionSessionRegistry _sessions;
    private readonly CommandTaskRegistry _tasks;
    private readonly ICommandTaskFactory _factory;
    private readonly ILogger<CommandService>? _logger;

    /// <summary>
    /// Creates the adapter over its three collaborators.
    /// </summary>
    /// <param name="sessions">
    /// The handle-to-session table owned by the transaction contract. Must be the process singleton.
    /// <para>
    /// <b>REUSED RATHER THAN DUPLICATED, DELIBERATELY (constraint C-K).</b> A command task runs against
    /// a session opened by C-08, and this table is the ONLY thing in the system that maps a session
    /// handle onto its descriptor, its borrowed connection and its gate. Declaring a second session
    /// table here would give the container two registrations to keep in step and would create two
    /// places for the mapping to drift - and a command executed against the wrong one of the two would
    /// run on a connection nobody was serializing.
    /// </para>
    /// </param>
    /// <param name="tasks">The handle-to-task table. Must be the process singleton.</param>
    /// <param name="factory">
    /// Builds the proxy pair. Substitutable by design, which is what makes this whole type testable
    /// with no thread, no pool and no database (constraint C-H).
    /// </param>
    /// <param name="logger">
    /// Optional structured logger. Optional rather than required so a unit test can construct the
    /// service with nothing but its behavioural collaborators. <b>Nothing written through it carries a
    /// statement except through the sanctioned redactor, and nothing carries a credential at all.</b>
    /// </param>
    /// <exception cref="ArgumentNullException">A required collaborator is <see langword="null"/>.</exception>
    /// <remarks>
    /// <b>NO <c>ISqlRedactor</c> IS INJECTED, AND THAT IS THE STRONGER CHOICE (constraint C-F).</b> The
    /// sanctioned projection in <c>Errors/SqlRedactor.cs</c> reaches its redaction policy directly
    /// rather than accepting one, precisely so the masking of the statement field cannot be weakened
    /// from a call site or from a container registration. Injecting a redactor here would make the
    /// policy replaceable, which is the opposite of the guarantee.
    /// </remarks>
    public CommandService(
        TransactionSessionRegistry sessions,
        CommandTaskRegistry tasks,
        ICommandTaskFactory factory,
        ILogger<CommandService>? logger = null)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _logger = logger;
    }

    // ----------------------------------------------------------------------------------------------
    //  PRIVATE HELPERS
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Clears the caller-side driver-error sink, so that a payload read after the task body can only be
    /// one this execution produced.
    /// </summary>
    /// <param name="proxy">The caller-side proxy whose sink is cleared.</param>
    /// <remarks>
    /// <para>
    /// <b>THIS IS THE ORACLE'S OWN CLEARING WRITE, NOT AN INVENTED ONE.</b> The sink is a single field
    /// on the caller-side object whose ONLY writer is the database-error event, whose entire body is the
    /// bare assignment <c>_lastDBError = err</c> [<c>n_cst_threading_task_sqlbase.sru:L44</c>]; and the
    /// oracle clears it by writing an empty structure into that same field -
    /// <c>_lastDBError = emptyData</c> [<c>:L65</c>], where <c>emptyData</c> is an uninitialized local
    /// [<c>:L55</c>]. Writing <see cref="DbErrorData.Empty"/> through the event entry point is
    /// therefore the same write the oracle's reset performs, reached the only way the field can be
    /// reached from outside the object.
    /// </para>
    /// <para>
    /// <b>Why the caller-side reset is NOT used for this.</b> <c>of_reset</c> would clear the sink, but
    /// it also delegates to the worker's reset, which restores autocommit to <c>AC_OFF</c> and clears
    /// the statement [<c>:L34-L35</c>], and it resets the parameter collection and the commit signal
    /// [<c>n_cst_thread_task_sqlbase.sru:L242-L246</c>]. Calling it inside an execution would therefore
    /// discard the very configuration the caller had just installed through <c>SetSql</c> and
    /// <c>SetAutoCommit</c>. The narrow write is the only one that clears the sink and nothing else.
    /// </para>
    /// <para>
    /// The cast is required because the entry point is implemented EXPLICITLY on the caller-side base
    /// so that the overridable hook stays <see langword="protected"/>. No override exists on the
    /// command proxy, so the effect here is exactly the bare field assignment.
    /// </para>
    /// </remarks>
    private static void ClearCapturedDbError(SqlCommandTaskProxy proxy)
    {
        DbErrorData empty = DbErrorData.Empty;

        ((ISqlTaskProxy)proxy).OnDbError(in empty);
    }

    /// <summary>
    /// Installs the request's parameter list on the task, replacing whatever was there.
    /// </summary>
    /// <param name="proxy">The caller-side proxy that owns the collection.</param>
    /// <param name="parameters">The wire parameter list, in significant order.</param>
    /// <returns>
    /// <see cref="RetCode.OK"/> when every parameter was installed;
    /// <see cref="RetCode.E_INVALID_ARGUMENT"/> for a parameter whose value carries no arm; otherwise
    /// the caller-side code that refused - <see cref="RetCode.E_BUSY"/> while a command is in flight,
    /// or <see cref="RetCode.E_INVALID_ARGUMENT"/> for a rejected parameter SHAPE.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>REPLACE RATHER THAN APPEND, AND THAT IS A BOUNDARY DECISION WITH A REASON (constraint C-K).</b>
    /// The oracle accumulates parameters across successive calls to <c>of_addparam</c>, which APPENDS at
    /// <c>UpperBound + 1</c> [<c>n_cst_thread_task_sqlbase.sru:L251-L259</c>], and it clears them
    /// through a separate <c>of_resetparams</c>. C-07 publishes no per-parameter method, so the repeated
    /// field IS the collection for this execution and fully specifies it. Resetting first is the only
    /// coherent reading: appending instead would make a second <c>Exec</c> on one task bind the first
    /// call's values as well, which no caller could express any intent to do.
    /// </para>
    /// <para>
    /// <b>A POSITIONAL PARAMETER IS LITERALLY THE EMPTY-NAMED CASE, AND THE TWO ARE ONE OPERATION.</b>
    /// The oracle's positional overload is defined as the named one with an empty name -
    /// <c>public function long of_addparam(readonly any param); return of_AddParam("", param)</c>
    /// [<c>n_cst_threading_task_sqlbase.sru:L138</c>] - and the binder then treats an empty name as
    /// "match the next unreplaced placeholder" rather than as a name to look up
    /// [<c>n_cst_thread_task_sqlbase.sru:L460</c>, <c>:L472</c>]. So an unset wire name is not a missing
    /// field to defend against; it is the positional form, and it is passed through unchanged.
    /// </para>
    /// <para>
    /// <b>THE SHAPE REJECTIONS ARE DELEGATED, NOT RE-IMPLEMENTED HERE (constraint C-B).</b> The
    /// caller-side base refuses a parameter of rank two or higher and an empty array, each with
    /// <see cref="RetCode.E_INVALID_ARGUMENT"/> [<c>n_cst_threading_task_sqlbase.sru:L148-L149</c>],
    /// under the source comment recording that parameters support only simple types or one-dimensional
    /// arrays of simple types [<c>:L146</c>]. Those guards are already ported and this method's job is
    /// to surface their answer, not to duplicate it.
    /// </para>
    /// <para>
    /// <b>THE DORMANT NULL CHECK IS CARRIED ACROSS INERT AND MUST NEVER BE REVIVED (constraint C-B).</b>
    /// The oracle's line, commented out, reads:
    /// </para>
    /// <code>
    ///   //if IsNull(value) then return RetCode.E_INVALID_ARGUMENT     [n_cst_threading_task_sqlbase.sru:L147]
    /// </code>
    /// <para>
    /// so an explicitly null VALUE is ACCEPTED and forwarded. A second dormant guard sits on the
    /// worker-side append for the same reason - <c>//if #Running then return RetCode.E_BUSY</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L253</c>]. Reviving either would refuse calls the legacy
    /// accepts. Note the distinction this method DOES enforce is a different one: a wire value with no
    /// arm set at all is not a null value, it is a producer that said nothing, and the contract
    /// separates the two explicitly.
    /// </para>
    /// <para>
    /// <b>NO ARITY CEILING IS APPLIED. SEE <see cref="Exec"/>.</b> This loop is bounded only by the
    /// list the caller sent.
    /// </para>
    /// </remarks>
    private static long ApplyParameters(
        SqlCommandTaskProxy proxy,
        IReadOnlyList<PositionalParameter> parameters)
    {
        // The oracle's own clearing entry point, which also lowers the caller-side "has parameters"
        // flag that gates binding [n_cst_threading_task_sqlbase.sru:L161-L169]. It carries its own busy
        // guard at [:L162], so a command in flight is refused here rather than half-reconfigured.
        long rtCode = proxy.ResetParams();
        if (Predicates.IsFailed(rtCode))
        {
            return rtCode;
        }

        for (int index = 0; index < parameters.Count; index++)
        {
            PositionalParameter parameter = parameters[index];

            // The sanctioned wire-value reader. It converts the explicit null arm to a null value - the
            // case the dormant guard above would have rejected - and refuses a message with no arm,
            // which is the narrowing the contract defines rather than a guess.
            if (!CarrierValue.TryFromWire(parameter.Value, out object? value))
            {
                return RetCode.E_INVALID_ARGUMENT;
            }

            // The name is forwarded exactly as sent, empty included. Lowercasing is the oracle's and
            // happens on the worker-side append [n_cst_thread_task_sqlbase.sru:L256]; doing it here as
            // well would be a second normalization of one value.
            rtCode = proxy.AddParam(parameter.Name, value);
            if (Predicates.IsFailed(rtCode))
            {
                return rtCode;
            }
        }

        return RetCode.OK;
    }

    /// <summary>
    /// Projects one execution's outcome onto the diagnostic the legacy's general-error event would have
    /// carried, arm by arm.
    /// </summary>
    /// <param name="rtCode">The outcome the task body answered.</param>
    /// <param name="driverText">
    /// The driver's own message for this execution - the raised payload's text when one was raised,
    /// otherwise the connection's.
    /// </param>
    /// <returns>The message argument, verbatim, or an empty string where no event fires.</returns>
    /// <remarks>
    /// <para>
    /// <b>THE ORACLE DOES NOT USE ONE MESSAGE FOR ALL FAILURES, AND THE DIFFERENCE IS OBSERVABLE
    /// (constraint C-B).</b> The task body raises its general-error event five times with three
    /// different sources of text, and the contract defines <c>error_text</c> as that argument verbatim:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// an empty statement carries the SYNTHESIZED Chinese diagnostic "无效的SQL!" [<c>:L66</c>];
    /// </item>
    /// <item>
    /// a binding failure carries the SYNTHESIZED Chinese diagnostic "SQL参数绑定失败!" [<c>:L83</c>];
    /// </item>
    /// <item>
    /// a failed acquisition carries the ACQUIRED PAYLOAD's text rather than a synthesized one
    /// [<c>:L72</c>];
    /// </item>
    /// <item>
    /// a failed commit on the <c>AC_ON</c> arm, and a failed statement, both carry the TRANSACTION's own
    /// text [<c>:L101</c>, <c>:L111</c>];
    /// </item>
    /// <item>
    /// cancellation raises NO EVENT AT ALL and returns silently [<c>:L76-L78</c>], and a succeeding
    /// statement raises none either [<c>:L94-L103</c>] - both are therefore empty rather than
    /// "successful".
    /// </item>
    /// </list>
    /// <para>
    /// The two synthesized messages are CONSUMED from the worker, where they are already declared
    /// verbatim, rather than re-declared here - one literal, one declaration site. They are Chinese and
    /// stay Chinese: the contract requires this member to be treated as opaque display text that
    /// consumers must not parse, and translating it would be a behavioural change dressed as a courtesy.
    /// </para>
    /// <para>
    /// The success test is <c>Predicates.IsSucceeded</c> rather than an equality against
    /// <see cref="RetCode.OK"/>, so a PREVENTION is classified as a success here exactly as the oracle
    /// classifies it - and CANCELLED, which that predicate calls NEITHER succeeded nor failed, is handled
    /// by its own arm above rather than falling through to the driver text.
    /// </para>
    /// </remarks>
    private static string ProjectErrorText(long rtCode, string driverText)
    {
        // [:L76-L78] No event, so no message - and this arm must precede the success test, because
        // CANCELLED is neither succeeded nor failed under the tri-state algebra.
        if (rtCode == RetCode.CANCELLED)
        {
            return string.Empty;
        }

        // [:L66] and [:L83] - the two arms that synthesize their own diagnostic instead of quoting the
        // driver.
        if (rtCode == RetCode.E_INVALID_SQL)
        {
            return SqlCommandTask.InvalidSqlMessage;
        }

        if (rtCode == RetCode.E_SQL_BIND_ARG_FAILED)
        {
            return SqlCommandTask.BindArgumentFailureMessage;
        }

        // [:L94-L103] the success epilogue raises no error event on any mode.
        if (Predicates.IsSucceeded(rtCode))
        {
            return string.Empty;
        }

        // [:L72], [:L101], [:L111] - every remaining arm quotes the driver's own text.
        return driverText;
    }

    // ----------------------------------------------------------------------------------------------
    //  THE SIX RPCs C-07 DECLARES - NONE MISSING, NONE EXTRA
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Creates a server-held command task against an open session - the wire form of the legacy object
    /// creation.
    /// </summary>
    /// <param name="request">The session the task will run against.</param>
    /// <param name="context">The call context. Unread: nothing here depends on transport metadata.</param>
    /// <returns>
    /// The task handle on success. <see cref="RetCode.E_INVALID_TRANSACTION"/> when the session handle
    /// names nothing live, matching the code the oracle answers when asked to act on a transaction it
    /// cannot use; otherwise the factory's or the descriptor installer's own code.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>The descriptor is installed HERE and never again</b>, which is the port of
    /// <c>of_settransdata(readonly transactiondata)</c>
    /// [<c>n_cst_threading_task_sqlbase.sru:L107-L120</c>]. The task resolves it to a pooled connection
    /// on each run [<c>n_cst_thread_task_sqlbase.sru:L148-L184</c>], and because the pool keys its
    /// entries on WHOLE-descriptor equality the task is handed the SAME connection the session borrowed
    /// - which is what makes the session's gate the right gate and the session's accessors the right
    /// accessors after an execution.
    /// </para>
    /// <para>
    /// <b>A pair that cannot be configured is released rather than registered.</b> Leaving a
    /// half-configured task in the table would hand the caller a handle whose every later call failed
    /// for a reason it could not see, and would pin the worker's pool reference until process exit.
    /// </para>
    /// </remarks>
    public override Task<CreateCommandTaskResponse> CreateCommandTask(
        CreateCommandTaskRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_sessions.TryResolve(request.Session, out TransactionSession? session) || session is null)
        {
            return Task.FromResult(new CreateCommandTaskResponse
            {
                Status = TransactionWireCodes.Status(
                    RetCode.E_INVALID_TRANSACTION,
                    UnknownSessionDiagnostic),
            });
        }

        // IsSucceeded rather than an equality against OK, so the tri-state algebra is inherited rather
        // than re-derived: a PREVENT answer reads as a success
        // [ws_objects/pfw.shared.pbl.src/issucceeded.srf:L11-L13], and the seam's contract requires the
        // pair to be produced in that case.
        long rtCode = _factory.Create(out CommandTaskComponents? components);
        if (!Predicates.IsSucceeded(rtCode) || components is null)
        {
            return Task.FromResult(new CreateCommandTaskResponse
            {
                Status = TransactionWireCodes.Status(rtCode),
            });
        }

        // A local, because the installer takes its argument by readonly reference and a property is not
        // addressable. The descriptor carries the write-only credential field and is neither rendered,
        // logged nor projected onto the response (constraint C-F).
        TransactionData descriptor = session.Descriptor;

        rtCode = components.Proxy.SetTransData(in descriptor);
        if (Predicates.IsFailed(rtCode))
        {
            CommandTask discarded = new(string.Empty, session, components);
            discarded.Dispose();

            return Task.FromResult(new CreateCommandTaskResponse
            {
                Status = TransactionWireCodes.Status(rtCode),
            });
        }

        CommandTask task = _tasks.Register(session, components);

        _logger?.LogDebug(
            "Command task {TaskId} created against session {SessionId}.",
            task.TaskId,
            session.SessionId);

        return Task.FromResult(new CreateCommandTaskResponse
        {
            Status = TransactionWireCodes.Status(RetCode.OK),
            Task = new TaskHandle { TaskId = task.TaskId },
        });
    }

    /// <summary>
    /// Destroys a server-held command task - the wire form of the legacy <c>Destroy</c>.
    /// </summary>
    /// <param name="request">The task to release.</param>
    /// <param name="context">The call context. Unread.</param>
    /// <returns>
    /// <see cref="RetCode.OK"/> once released; <see cref="RetCode.E_INVALID_HANDLE"/> when the handle
    /// names nothing live, which is also what a second release of the same handle answers.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <b>Removal and disposal are one atomic step, and the table is what makes it atomic.</b> Only the
    /// caller that wins the removal disposes, so two concurrent releases dispose exactly once and the
    /// loser gets the ordinary unknown-handle answer. Disposal returns the worker's pool reference
    /// [<c>n_cst_thread_task_sqlbase.sru:L160</c>], so a task that is never released pins its connection
    /// - which is why this method exists at all rather than relying on a garbage collector the oracle
    /// did not have either.
    /// </remarks>
    public override Task<ReleaseCommandTaskResponse> ReleaseCommandTask(
        ReleaseCommandTaskRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? taskId = request.Task?.TaskId;

        if (string.IsNullOrEmpty(taskId)
            || !_tasks.TryRemove(taskId, out CommandTask? task)
            || task is null)
        {
            return Task.FromResult(new ReleaseCommandTaskResponse
            {
                Status = TransactionWireCodes.Status(RetCode.E_INVALID_HANDLE, UnknownTaskDiagnostic),
            });
        }

        task.Dispose();

        _logger?.LogDebug("Command task {TaskId} released.", taskId);

        return Task.FromResult(new ReleaseCommandTaskResponse
        {
            Status = TransactionWireCodes.Status(RetCode.OK),
        });
    }

    /// <summary>
    /// Resets a command task - the port of <c>of_reset</c> [<c>:L27</c>, <c>:L32-L38</c>] reached through
    /// its caller-side twin [<c>n_cst_threading_task_sqlbase.sru:L53-L68</c>].
    /// </summary>
    /// <param name="request">The task to reset.</param>
    /// <param name="context">The call context. Unread.</param>
    /// <returns>
    /// <see cref="RetCode.OK"/> on success, <see cref="RetCode.E_BUSY"/> while a command is in flight,
    /// or <see cref="RetCode.E_INVALID_HANDLE"/> for an unknown handle.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>BOTH HALVES OF THE RESET ARE CONTRACT, AND BOTH ARE DELEGATED.</b> The worker restores
    /// autocommit to <c>AC_OFF</c> and clears the statement [<c>:L34-L35</c>], and the caller-side twin
    /// then clears its own parameter flag and its driver-error sink
    /// [<c>n_cst_threading_task_sqlbase.sru:L64-L65</c>]. A caller that reset a task and then ran it
    /// without setting autocommit again therefore gets <c>AC_OFF</c>, not whatever was set before.
    /// </para>
    /// <para>
    /// <b>THE BUSY REFUSAL IS DOUBLE, AND BOTH GUARDS ARE PRESERVED.</b> The caller-side twin refuses
    /// first [<c>:L57</c>] and the worker refuses again with <c>if #Running then return RetCode.E_BUSY</c>
    /// [<c>:L32</c>]. Two guards on one condition is defence in depth in the oracle and is not collapsed
    /// here (constraint C-B).
    /// </para>
    /// <para>
    /// <b>The answer is the caller-side one, and it is NOT always the worker's.</b> The twin abandons on
    /// a FAILED worker answer and returns it verbatim [<c>:L62</c>], but otherwise returns
    /// <see cref="RetCode.OK"/> rather than the worker's code [<c>:L67</c>] - so a non-failing non-zero
    /// answer such as <see cref="RetCode.PREVENT"/> is reported as OK. Reproduced exactly by delegating.
    /// </para>
    /// </remarks>
    public override Task<ResetCommandTaskResponse> Reset(
        ResetCommandTaskRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_tasks.TryResolve(request.Task, out CommandTask? task) || task is null)
        {
            return Task.FromResult(new ResetCommandTaskResponse
            {
                Status = TransactionWireCodes.Status(RetCode.E_INVALID_HANDLE, UnknownTaskDiagnostic),
            });
        }

        return Task.FromResult(new ResetCommandTaskResponse
        {
            Status = TransactionWireCodes.Status(task.Proxy.Reset()),
        });
    }

    /// <summary>
    /// Installs the THREE-VALUED autocommit mode - the port of <c>of_setautocommit</c> [<c>:L28</c>,
    /// <c>:L40-L43</c>] reached through its caller-side twin
    /// [<c>n_cst_threading_task_sqlcommand.sru:L43-L46</c>].
    /// </summary>
    /// <param name="request">The task and the mode.</param>
    /// <param name="context">The call context. Unread.</param>
    /// <returns>
    /// <see cref="RetCode.OK"/> - the oracle has no failing arm here - <see cref="RetCode.E_BUSY"/>
    /// while a command is in flight, or <see cref="RetCode.E_INVALID_HANDLE"/> for an unknown handle.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>⚠ THE SETTER VALIDATES NOTHING, AND THAT IS PRESERVED ON PURPOSE (constraint C-B).</b> The
    /// oracle's body is two statements - a bare assignment and <c>return RetCode.OK</c>
    /// [<c>:L40-L42</c>] - with no range check, no domain check and no diagnostic. A proto3 enum field is
    /// OPEN rather than closed, so a value outside the three enumerators arrives intact as its raw
    /// number, and it is accepted here exactly as the oracle accepts it. NO RANGE CHECK MAY BE ADDED:
    /// the task body's epilogue tests for the two named modes and treats everything else as
    /// <c>AC_OFF</c>, so an unrecognised value has DEFINED behaviour rather than undefined behaviour,
    /// and rejecting it would refuse a call the legacy accepts. This is the deliberate contrast with the
    /// clause-style selector on the query contract, where the legacy offers no validation to copy and the
    /// contract therefore DEFINES a rejection instead.
    /// </para>
    /// <para>
    /// <b>⚠ THIS ENUM IS NOT THE UPDATE CONTRACT'S BOOLEAN. DO NOT UNIFY THEM.</b> C-07's autocommit is
    /// three-valued <c>AutoCommitMode</c>; C-06's is a plain <see cref="bool"/>. The asymmetry is in the
    /// source at both sites - <c>of_setautocommit(readonly long autocommit)</c> here at [<c>:L28</c>]
    /// against <c>of_setautocommit(readonly boolean autocommit)</c> on the update task
    /// [<c>n_cst_thread_task_sqlupdate.sru:L49</c>] - and it is annotated at both precisely because a
    /// reader who meets only one site will unify them. The update task has no native arm anywhere in its
    /// body, so offering it one would be offering a value the server could only reject.
    /// </para>
    /// <para>
    /// <b>The default is <c>AC_OFF</c>, and the wire agrees mechanically.</b> The oracle initialises the
    /// field to <c>AC_OFF</c> [<c>:L21</c>] and its reset restores that [<c>:L34</c>]; because
    /// <c>AC_OFF</c> genuinely occupies zero, an unset field arrives as <c>AC_OFF</c> and no synthetic
    /// unspecified sentinel was needed in the contract.
    /// </para>
    /// </remarks>
    public override Task<SetCommandAutoCommitResponse> SetAutoCommit(
        SetCommandAutoCommitRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_tasks.TryResolve(request.Task, out CommandTask? task) || task is null)
        {
            return Task.FromResult(new SetCommandAutoCommitResponse
            {
                Status = TransactionWireCodes.Status(RetCode.E_INVALID_HANDLE, UnknownTaskDiagnostic),
            });
        }

        // Forwarded unchanged, including a value outside the three enumerators. The caller-side twin's
        // busy guard [n_cst_threading_task_sqlcommand.sru:L43] is the ONLY guard on this path.
        return Task.FromResult(new SetCommandAutoCommitResponse
        {
            Status = TransactionWireCodes.Status(task.Proxy.SetAutoCommit(request.Autocommit)),
        });
    }

    /// <summary>
    /// Installs the statement - the port of <c>of_setsql</c> [<c>:L29</c>, <c>:L45-L50</c>] reached
    /// through its caller-side twin [<c>n_cst_threading_task_sqlcommand.sru:L34-L41</c>].
    /// </summary>
    /// <param name="request">The task and the statement.</param>
    /// <param name="context">The call context. Unread.</param>
    /// <returns>
    /// <see cref="RetCode.OK"/> on success; <see cref="RetCode.E_INVALID_SQL"/> for an empty statement;
    /// <see cref="RetCode.E_BUSY"/> while a command is in flight;
    /// <see cref="RetCode.E_INVALID_HANDLE"/> for an unknown handle.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>⚠ AN EMPTY STATEMENT IS <see cref="RetCode.E_INVALID_SQL"/>, NOT
    /// <see cref="RetCode.E_INVALID_ARGUMENT"/> (constraint C-B).</b> The guard is
    /// <c>if sql = "" then return RetCode.E_INVALID_SQL</c> [<c>:L45</c>] - an empty statement is
    /// classified as a BAD STATEMENT rather than as a bad argument, unlike most other guards in the SQL
    /// layer, and the distinction is preserved rather than harmonized.
    /// </para>
    /// <para>
    /// <b>⚠ THE LEGACY CHECKS THIS TWICE AND BOTH GUARDS SURVIVE.</b> The task body tests the same
    /// condition again and answers the same code with a verbatim Chinese diagnostic, "无效的SQL!"
    /// [<c>:L65-L68</c>], where this setter's arm carries no message at all. Two guards, one condition,
    /// DIFFERENT observable messages - and the second is the only one a caller who never calls this
    /// method can reach, which is exactly the case of an <see cref="Exec"/> that omits its statement
    /// field. Collapsing them would remove an observable difference.
    /// </para>
    /// <para>
    /// <b>THE LEADING-<c>@</c> EXECUTION MODE TRAVELS INSIDE THIS STRING, AND ITS BEHAVIOUR IS THE
    /// TRANSACTION'S.</b> A statement whose first character is <c>@</c> is not a statement at all: the
    /// remainder NAMES A DATAWINDOW OBJECT rather than SQL, which the transaction surface implements as
    /// <c>if Left(sql,1) = "@" then ds.DataObject = Mid(sql,2)</c>
    /// [<c>n_cst_thread_trans.sru:L309-L310</c>]. The prefix is deliberately NOT hoisted into a separate
    /// boolean field, because it is the statement text that selects the mode and splitting it would
    /// create two sources of truth for one decision. Nothing in this file inspects the first character;
    /// the text is forwarded verbatim.
    /// </para>
    /// <para>
    /// <b>MULTI-STATEMENT BATCH EXECUTION is likewise carried in this one string</b> and is likewise the
    /// transaction's behaviour: a batch is submitted as a single command and rolled back as a unit on
    /// failure, which is what makes the autocommit mode meaningful on a batch and not only on a single
    /// statement. This file neither splits nor counts statements.
    /// </para>
    /// <para>
    /// A DEBUG-only assertion also sits on the caller-side twin [<c>:L36-L38</c>]; it is delegated with
    /// everything else and is not re-declared here.
    /// </para>
    /// </remarks>
    public override Task<SetCommandSqlResponse> SetSql(
        SetCommandSqlRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_tasks.TryResolve(request.Task, out CommandTask? task) || task is null)
        {
            return Task.FromResult(new SetCommandSqlResponse
            {
                Status = TransactionWireCodes.Status(RetCode.E_INVALID_HANDLE, UnknownTaskDiagnostic),
            });
        }

        // C-F: the statement is forwarded and never logged from here. The only record that may ever
        // carry it is produced through the sanctioned redactor, deeper in the stack.
        return Task.FromResult(new SetCommandSqlResponse
        {
            Status = TransactionWireCodes.Status(task.Proxy.SetSql(request.Sql)),
        });
    }

    /// <summary>
    /// Executes the installed statement under the installed autocommit mode - the boundary form of
    /// <c>event ondotask</c> [<c>:L60-L115</c>].
    /// </summary>
    /// <param name="request">The task, an optional statement and mode override, and the parameter list.</param>
    /// <param name="context">The call context. Unread.</param>
    /// <returns>
    /// The outcome plus the four driver accessors and the durability flag. See the remarks for the
    /// ordered failure arms and for what each of the six response fields is defined from.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>THE ORDERED FAILURE ARMS, IN THE ORACLE'S OWN ORDER, AND THE ORDER IS LOAD-BEARING.</b> All
    /// four live in the task body and are surfaced by this method rather than re-derived by it:
    /// </para>
    /// <list type="number">
    /// <item>
    /// an empty statement answers <see cref="RetCode.E_INVALID_SQL"/> [<c>:L65-L68</c>] - and it does so
    /// BEFORE the transaction is acquired, so a task with no statement never takes a pool reference;
    /// </item>
    /// <item>
    /// a failed acquisition raises the DATABASE-ERROR event and answers
    /// <see cref="RetCode.E_INVALID_TRANSACTION"/> [<c>:L70-L74</c>] - both, not either, and note the
    /// statement it raises with is the EMPTY STRING because an acquisition failure has no statement;
    /// </item>
    /// <item>
    /// cancellation answers <see cref="RetCode.CANCELLED"/> silently, with no event at all
    /// [<c>:L76-L78</c>] - and BEFORE binding, so a cancelled task never interpolates a caller literal;
    /// </item>
    /// <item>
    /// a binding failure answers <see cref="RetCode.E_SQL_BIND_ARG_FAILED"/> [<c>:L82-L85</c>].
    /// </item>
    /// </list>
    /// <para>
    /// <b>⚠ <c>SQLCode = 100</c> IS SUCCESS, NOT AN ERROR (constraint C-B).</b> The transaction's command
    /// path tests <c>if SQLCode &lt;&gt; 0 and SQLCode &lt;&gt; 100 then return RetCode.E_DB_ERROR</c>
    /// [<c>n_cst_thread_trans.sru:L233</c>], so a "nothing found" outcome is EXPLICITLY not a failure and
    /// this call reports <c>ret_code</c> OK with <c>sql_code</c> 100. An implementation that treated any
    /// non-zero code as failure would invent errors the legacy does not report. The carve-out is applied
    /// where the oracle applies it - inside the transaction - and this method simply must not undo it by
    /// re-deriving an outcome from <c>sql_code</c>.
    /// </para>
    /// <para>
    /// <b>⚠ THREE CODE SPACES MEET IN THIS RESPONSE AND NONE IS COMPARABLE TO ANOTHER.</b>
    /// <c>status.ret_code</c> is the FRAMEWORK's algebra; <c>sql_code</c> is the RUNTIME's statement
    /// status where 0 is success and 100 is also success; <c>sql_db_code</c> is the DRIVER's own number.
    /// Relatedly, the transaction object publishes its own predicates over <c>SQLCode</c> rather than over
    /// a return code - <c>of_issucceeded</c> is <c>SQLCode &gt;= 0</c>
    /// [<c>n_cst_thread_trans.sru:L340</c>] and <c>of_isfailed</c> is <c>SQLCode &lt; 0</c> [<c>:L337</c>]
    /// - and those must NOT be conflated with <c>Predicates.IsSucceeded</c> and
    /// <c>Predicates.IsFailed</c>, whose semantics differ: the framework predicates classify a PREVENTION
    /// as a success and leave CANCELLED as NEITHER succeeded nor failed
    /// [<c>ws_objects/pfw.shared.pbl.src/issucceeded.srf:L11-L13</c>,
    /// <c>isfailed.srf:L11-L13</c>]. Every guard in this file uses the FRAMEWORK predicates, on framework
    /// codes; the SQLCode predicates are never called here, and the two are named apart deliberately.
    /// </para>
    /// <para>
    /// <b>THE THREE-MODE EPILOGUE IS DELEGATED IN FULL, AND ITS ASYMMETRY IS REAL.</b> The task body owns
    /// it and this method must not second-guess any arm:
    /// <c>AC_OFF</c> commits NOTHING - the success epilogue is an if / else-if with no else
    /// [<c>:L94-L103</c>] - because the caller's session owns the transaction;
    /// <c>AC_ON</c> commits explicitly with auto-rollback true and lets the COMMIT'S code REPLACE the
    /// statement's, surfacing a commit failure through the error event with the transaction's own text
    /// [<c>:L98-L103</c>];
    /// <c>AC_NATIVE</c> switches the connection's own autocommit ON BEFORE the statement [<c>:L88-L90</c>]
    /// and OFF again afterwards on BOTH the success and the failure path [<c>:L96</c>, <c>:L106</c>],
    /// raises the committed notification INSTEAD of committing [<c>:L97</c>], and on failure performs
    /// <b>NO ROLLBACK OF ITS OWN</b> - it skips the else arm's rollback [<c>:L108</c>] because there was
    /// no explicit transaction to roll back. <b>That last point looks like an omission and is not</b>;
    /// annotating it is the whole reason this paragraph exists.
    /// </para>
    /// <para>
    /// <b>THE VETOED BEFORE-COMMAND DISCRIMINATION IS ALSO DELEGATED AND ALSO PRESERVED.</b> When the
    /// transaction's before-command hook prevents, the outcome is <see cref="RetCode.E_DB_ERROR"/> if
    /// <c>SQLCode</c> is non-zero and <see cref="RetCode.CANCELLED"/> otherwise
    /// [<c>n_cst_thread_trans.sru:L224-L227</c>] - a veto and a driver failure are told apart by the
    /// driver's state rather than by the veto itself.
    /// </para>
    /// <para>
    /// <b>⚠ THE LEGACY ARGUMENT CEILINGS ARE RECORDED HERE AND DELIBERATELY NOT ENFORCED (constraint
    /// C-K).</b> The SQLite binding's execute and query surfaces are overloaded from zero arguments up to
    /// ELEVEN and then simply stop [<c>ws_objects/pfw.utility.sqlite.pbl.src/n_sqlite.sru:L42-L43</c>,
    /// <c>:L54-L55</c>, <c>:L67-L68</c>], the transaction's retrieval unrolls to EIGHT before falling back
    /// to dynamic invocation [<c>n_cst_thread_trans.sru:L447-L467</c>], and the query task's own unrolled
    /// maximum is TWENTY. Every one of those ceilings exists because PowerScript CANNOT FORWARD AN
    /// ARBITRARY-LENGTH ARGUMENT LIST, so the surface had to be written out by hand one arity at a time.
    /// They are statements about the SOURCE LANGUAGE, not about what the database accepts and not about
    /// what any behaviour depends on. C# is natively variadic and a repeated field has no cap, so
    /// EXCEEDING ELEVEN ARGUMENTS IS NOT A BEHAVIOURAL REGRESSION - the twelfth simply had no overload to
    /// call. <b>NO VALIDATION REJECTING A TWELFTH ARGUMENT MAY BE ADDED HERE.</b> The ceilings are
    /// recorded because they explain an otherwise-baffling shape in the oracle, and because a parity
    /// recording will never contain more than eleven, so a reviewer comparing the two should not read the
    /// difference as a divergence.
    /// </para>
    /// <para>
    /// <b>WHAT EACH RESPONSE FIELD IS DEFINED FROM.</b> The four numeric and textual accessors are read
    /// from the session's borrowed connection inside the gate, which is the same connection the task
    /// resolved from the pool - the pool keys on whole-descriptor equality, and the task was configured
    /// with this session's descriptor. They correspond to the accessors the legacy publishes for exactly
    /// this purpose and that the task body itself reads when reporting a failure [<c>:L110-L111</c>].
    /// <c>committed</c> is the NON-BLOCKING poll of the commit signal - <c>of_iscommitted</c> is
    /// <c>WaitForSingleObject(_hEvtCommitted, 0) = 0</c>
    /// [<c>n_cst_threading_task_sqlbase.sru:L174</c>], a zero-timeout probe and never a wait - so it is
    /// read here as a STATUS READ and is never awaited. It is TRUE exactly when the legacy would have
    /// raised its committed notification, which has two reachable raise sites and not one:
    /// <c>AC_NATIVE</c> on success raises it inline [<c>:L97</c>], and <c>AC_ON</c> on success raises it
    /// through the commit helper but ONLY IF THE COMMIT ITSELF SUCCEEDED
    /// [<c>n_cst_thread_task_sqlbase.sru:L235-L237</c>]; <c>AC_OFF</c> never raises it, and no failing
    /// statement raises it on any mode.
    /// </para>
    /// <para>
    /// <b>THE DRIVER PAYLOAD IS ATTACHED ONLY WHEN THE LEGACY WOULD ALSO HAVE RAISED ITS DATABASE-ERROR
    /// EVENT</b>, which the contract states as the presence rule for that member. The sink is cleared
    /// immediately before the task body and read immediately after, so a payload found there was produced
    /// by THIS execution and never by a previous one. One boundary case is worth naming rather than
    /// hiding: a raised payload carrying a zero driver code, no text and no statement is
    /// indistinguishable from an unraised one, and it is unobservable either way, because such a payload
    /// projects onto an all-default wire message. <b>Its statement field is masked by the projection, not
    /// by this file, and <c>DbError.Sqlsyntax</c> is never assigned anywhere below (constraint C-F).</b>
    /// </para>
    /// </remarks>
    public override Task<ExecResponse> Exec(ExecRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_tasks.TryResolve(request.Task, out CommandTask? task) || task is null)
        {
            return Task.FromResult(new ExecResponse
            {
                Status = TransactionWireCodes.Status(RetCode.E_INVALID_HANDLE, UnknownTaskDiagnostic),
            });
        }

        // -------------------------------------------------------------------------------------------
        // STEP 1 - the OPTIONAL statement, applied in the contract's own field order.
        // -------------------------------------------------------------------------------------------
        // Unset leaves the task's statement alone, and the task body's own emptiness guard then applies
        // to whatever the task holds - which is how a caller who never called SetSql reaches the second
        // of the two preserved guards. Set-but-empty is refused HERE, by the setter's guard, with
        // E_INVALID_SQL and no message [:L45].
        //
        // Every mutator below carries the caller-side busy guard, so a command already in flight for
        // this task is refused with E_BUSY rather than reconfigured underneath itself
        // [n_cst_threading_task_sqlbase.sru:L57, :L73, :L95, :L110, :L124, :L144, :L162, :L179, :L188].
        if (request.HasSql)
        {
            long statementCode = task.Proxy.SetSql(request.Sql);
            if (Predicates.IsFailed(statementCode))
            {
                return Task.FromResult(new ExecResponse
                {
                    Status = TransactionWireCodes.Status(statementCode),
                });
            }
        }

        // -------------------------------------------------------------------------------------------
        // STEP 2 - the parameter list. Positional is the empty-NAMED case; shape rejections and the
        // dormant null check are the caller-side base's. See ApplyParameters.
        // -------------------------------------------------------------------------------------------
        long parameterCode = ApplyParameters(task.Proxy, request.Parameters);
        if (Predicates.IsFailed(parameterCode))
        {
            return Task.FromResult(new ExecResponse
            {
                Status = parameterCode == RetCode.E_INVALID_ARGUMENT
                    ? TransactionWireCodes.Status(parameterCode, ParameterValueMissingDiagnostic)
                    : TransactionWireCodes.Status(parameterCode),
            });
        }

        // -------------------------------------------------------------------------------------------
        // STEP 3 - the OPTIONAL autocommit override. Unset uses the task's setting, whose default is
        // AC_OFF [:L21]. Validates nothing, exactly as the oracle validates nothing [:L40-L43].
        // -------------------------------------------------------------------------------------------
        if (request.HasAutocommit)
        {
            long autoCommitCode = task.Proxy.SetAutoCommit(request.Autocommit);
            if (Predicates.IsFailed(autoCommitCode))
            {
                return Task.FromResult(new ExecResponse
                {
                    Status = TransactionWireCodes.Status(autoCommitCode),
                });
            }
        }

        // -------------------------------------------------------------------------------------------
        // STEP 4 - clear the driver-error sink, so that what is read after the body belongs to this
        // execution. The oracle's own clearing write; see ClearCapturedDbError.
        // -------------------------------------------------------------------------------------------
        ClearCapturedDbError(task.Proxy);

        long rtCode;
        long sqlNRows;
        long sqlCode;
        long sqlDbCode;
        string sqlErrText;
        bool committed;
        DbErrorData captured;

        // -------------------------------------------------------------------------------------------
        // STEP 5 - the task body, and the outcome read, both inside the session's gate. The gate is what
        // reproduces the oracle's thread affinity across a concurrent server; see the type remarks.
        // -------------------------------------------------------------------------------------------
        lock (task.Session.Gate)
        {
            // [:L60-L115] in full - the acquisition, the cancellation check, the binding, the inversion,
            // the execution and BOTH epilogues. This single call is the whole of the delegation.
            rtCode = task.Worker.OnDoTask();

            // ⚠ READ FROM THE INSTANCE THE WORKER ACQUIRED, NEVER FROM THE ONE THE SESSION HOLDS.
            // The oracle's task body reads its four accessors off the transaction IT just acquired -
            // `transObject.SQLDBCode`, `transObject.SQLErrText` [:L110-L111] - and across this boundary
            // the two are not always the same object: the pool REPLACES a broken entry's transaction and
            // DISPOSES the old one
            // [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L164-L172], so the
            // reference the session captured when it opened can be both stale and disposed by the time a
            // command runs. Reading it would report the state of a connection that never executed this
            // statement. Null only when the acquisition arm above already returned, in which case the
            // driver detail travels in the raised payload instead and these four are genuinely absent.
            IPooledTransaction? transaction = task.Worker.AttachedTransaction;

            sqlNRows = transaction?.SqlNRows ?? 0L;
            sqlCode = transaction?.SqlCode ?? 0L;
            sqlDbCode = transaction?.SqlDbCode ?? 0L;
            sqlErrText = transaction?.SqlErrText ?? string.Empty;

            // A zero-timeout probe, never a wait [n_cst_threading_task_sqlbase.sru:L174].
            committed = task.Proxy.IsCommitted();

            captured = task.Proxy.GetLastDbErrorData();
        }

        bool driverErrorRaised = captured != DbErrorData.Empty;

        // The driver payload reaches the wire ONLY through the projection in Errors/SqlRedactor.cs, which
        // masks the statement field unconditionally and takes no policy argument a call site could
        // weaken. This file never assigns DbError.Sqlsyntax (constraint C-F). The re-typing of the
        // framework code onto the wire enum goes through the single named helper, for the review-invariant
        // reason recorded on that helper.
        OperationStatus status = driverErrorRaised
            ? TransactionWireCodes.Status(rtCode, in captured)
            : TransactionWireCodes.Status(rtCode);

        // Assigned after composition because the helper has no three-member overload, and adding one
        // would mean editing the file that owns it. The legacy fires BOTH events on a driver failure, so
        // a payload and a message coexist rather than excluding one another. On the acquisition arm the
        // message is the ACQUIRED payload's text [:L72], which is why the raised payload is preferred as
        // the source when one exists.
        status.ErrorText = ProjectErrorText(
            rtCode,
            driverErrorRaised ? captured.SqlErrText : sqlErrText);

        if (_logger?.IsEnabled(LogLevel.Debug) == true)
        {
            // C-F: no statement and no credential in this record. The parameter COUNT is safe to state;
            // the parameter VALUES and the statement text are not, and neither appears.
            _logger.LogDebug(
                "Command task {TaskId} executed with {ResultCode}: sql_code {SqlCode}, "
                + "sql_db_code {SqlDbCode}, rows {SqlNRows}, committed {Committed}, "
                + "parameters {ParameterCount}.",
                task.TaskId,
                rtCode,
                sqlCode,
                sqlDbCode,
                sqlNRows,
                committed,
                request.Parameters.Count);
        }

        return Task.FromResult(new ExecResponse
        {
            Status = status,

            SqlNrows = sqlNRows,

            // Carried verbatim. 100 is a SUCCESS value here; see the remarks.
            SqlCode = sqlCode,

            SqlDbCode = sqlDbCode,

            // Opaque display text, possibly non-English. Consumers classify on ret_code, never by
            // parsing this.
            SqlErrText = sqlErrText,

            Committed = committed,
        });
    }
}
