// ==================================================================================================
//  SqlTaskProxyHost.cs - THE CALLER-SIDE THREADING SUBSTRATE, AND THE TWO RECEIVE-SIDE SEAMS
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS FILE SUPPLIES
//  SqlTaskProxyBase.cs declares ISqlTaskProxyHost - the narrow slice of
//  ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru that the three SQL proxies consume - and
//  SqlQueryTaskProxy.cs declares IQueryChildResolver and IQueryCarrierAdopter for its receive side.
//  All three had no implementation and no registration, which made every caller/worker proxy pair in
//  this service UNCONSTRUCTIBLE: the update and command factories could not build their halves, and
//  the query proxy could not accept a child payload or adopt a handed-over carrier. This file is the
//  shipped implementation of all three.
//
//  WHY IT IS ITS OWN FILE RATHER THAN PART OF THE COMPOSITION ROOT
//  These are not wiring types. The host LATCHES state - the running flag, the synchronization signal,
//  the exit code, the two error latches - and the semantics of each latch come from a specific line of
//  the legacy substrate. Putting them beside the proxies that read them keeps each latch next to the
//  locator that authorises it, and keeps Program.cs to registration.
//
//  THE PAIR STAYS A PAIR (constraint C-J). The legacy encodes execution context as a CONTRACT by
//  shipping every concurrency class twice: six SQL objects are annotated worker-thread affine, exactly
//  one main-thread affine and four calling-thread proxies [the $PBExportComments$ headers of
//  ws_objects/pfw.thread.ext.pbl.src/*.sru]. This host is the CALLER side. It is deliberately a
//  different type from Program.cs's PersistenceSqlTaskHost, which is the WORKER side, and neither is a
//  base or a partial of the other - fusing them would erase the one piece of information the affinity
//  annotations exist to state.
//
//  ONE HOST PER TASK, NEVER ONE PER PROCESS. Every latch below is per task, so a shared instance would
//  report one task's exit code to another. The factories construct one host per task and the container
//  never hands the same instance to two.
//
//  C-F. No credential, connection string, descriptor field or statement text is read, stored, logged or
//  published anywhere in this file.
//  C-H. No ambient clock: nothing here reads DateTime, DateTimeOffset, Stopwatch or Environment.TickCount.
// ==================================================================================================

using PowerFramework.Contracts.Common.V1;
using PowerFramework.Persistence.Buffers;
using PowerFramework.Persistence.Data;
using RetCode = PowerFramework.Shared.Kernel.RetCode;

namespace PowerFramework.Persistence.Tasks.TaskProxies
{
    /// <summary>
    /// The shipped <see cref="ISqlTaskProxyHost"/>: the caller-side threading substrate for exactly one
    /// SQL task.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE SYNCHRONIZATION SIGNAL IS MANUAL AND ITS SENSE IS INVERTED - GET THIS BACKWARDS AND EVERY
    /// GUARDED MUTATOR MISBEHAVES.</b> The oracle answers busy with
    /// <c>(WaitForSingleObject(_hEvtSync,0) &lt;&gt; 0)</c>
    /// [<c>n_cst_threading_task.sru:L306</c>], and a NON-ZERO result is <c>WAIT_TIMEOUT</c> - meaning NOT
    /// SIGNALLED. So a running task is BUSY while <see cref="IsSyncSignalSet"/> is
    /// <see langword="false"/>. The signal is raised for the duration of each framework callback
    /// [<c>:L216, :L255, :L291</c>] and cleared afterwards [<c>:L218, :L263, :L295</c>], which makes the
    /// guard a re-entrancy gate: a mutator is permitted from INSIDE a callback and refused from outside
    /// one while the task runs.
    /// </para>
    /// <para>
    /// <b>THE CANCELLATION HANDLE BECOMES A TOKEN, WHICH IS THE PORT RATHER THAN A CONVENIENCE.</b> A raw
    /// Win32 handle has no meaning in a Linux container, and the asynchronous request/response expression
    /// of the proxy/worker pair is defined in terms of a token.
    /// <see cref="IsCancelled"/> keeps BOTH of the oracle's terms - the latched exit code
    /// [<c>:L318</c>] and the signal [<c>:L319</c>] - because a token alone cannot express the first,
    /// and the first is what makes a stopped-by-cancellation task keep answering cancelled.
    /// </para>
    /// </remarks>
    internal sealed class SqlTaskProxyHost : ISqlTaskProxyHost, IDisposable
    {
        /// <summary>
        /// The one-based position this host reports for its single task.
        /// </summary>
        /// <remarks>
        /// ONE, BECAUSE THE LEGACY INDEX IS ONE-BASED [<c>n_cst_threading_task.sru:L401</c>] and this
        /// host owns exactly one task. Named rather than written as a bare literal, because one-based
        /// indexing is the single most dangerous mechanical hazard in this port (AAP §0.8.6 R9).
        /// </remarks>
        internal const int SoleTaskIndex = 1;

        /// <summary>The next task identifier to hand out.</summary>
        /// <remarks>
        /// The port of <c>of_GetID()</c> [<c>:L398</c>], whose value the substrate takes from the worker
        /// at <c>:L201</c>. An interlocked counter rather than a random or clock-derived value: identifiers
        /// appear in log records that a characterization run compares, so a value that varied between a
        /// master and a candidate would break the paired comparison (constraint C-H).
        /// </remarks>
        private static ulong _nextTaskId;

        private readonly CancellationTokenSource _cancellation = new();
        private readonly ILogger<SqlTaskProxyHost> _logger;

        private SqlTaskBase? _worker;
        private string _taskClassName = string.Empty;
        private bool _disposed;

        /// <summary>
        /// Initializes a host that has bound no worker and started nothing.
        /// </summary>
        /// <param name="logger">Records the delay and skip requests the worker substrate does not model.</param>
        /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
        internal SqlTaskProxyHost(ILogger<SqlTaskProxyHost> logger)
        {
            ArgumentNullException.ThrowIfNull(logger);

            _logger = logger;
            TaskId = Interlocked.Increment(ref _nextTaskId);
        }

        /// <inheritdoc/>
        public bool IsRunning { get; private set; }

        /// <inheritdoc/>
        /// <remarks>
        /// <see langword="false"/>, and that is the accurate answer rather than a placeholder. The
        /// legacy term delegates to the owning CONTROLLER's own busy predicate - the answer for a task
        /// that has not started yet [<c>:L305</c>] - and in this service one controller owns exactly one
        /// task, so a controller is busy precisely when its task is. Reporting <see langword="true"/>
        /// would refuse every mutator on a freshly created, not-yet-started task.
        /// </remarks>
        public bool IsControllerBusy => false;

        /// <inheritdoc/>
        public bool IsSyncSignalSet { get; private set; }

        /// <inheritdoc/>
        public bool IsCancelled =>
            LastExitCode == RetCode.CANCELLED || _cancellation.IsCancellationRequested;

        /// <inheritdoc/>
        public CancellationToken Cancellation => _cancellation.Token;

        /// <inheritdoc/>
        public long LastExitCode { get; private set; }

        /// <inheritdoc/>
        public long LastErrorCode { get; private set; }

        /// <inheritdoc/>
        public string LastErrorInfo { get; private set; } = string.Empty;

        /// <inheritdoc/>
        public ulong TaskId { get; }

        /// <inheritdoc/>
        public int TaskIndex => SoleTaskIndex;

        /// <inheritdoc/>
        public string TaskClassName => _taskClassName;

        /// <inheritdoc/>
        public SqlTaskBase? Task => _worker;

        /// <summary>
        /// The number of seconds the worker's start was delayed by, or zero when none was requested.
        /// </summary>
        /// <remarks>
        /// Recorded rather than acted on, because the member it ports belongs to the WORKER's substrate
        /// half - <c>n_cst_thread_task.sru:L593</c> - which <see cref="SqlTaskBase"/> deliberately does
        /// not model. Publishing what was asked for keeps the request observable instead of silently
        /// discarded.
        /// </remarks>
        internal double WorkerDelaySeconds { get; private set; }

        /// <summary>Whether the worker was marked to be skipped.</summary>
        /// <remarks>Recorded for the same reason as <see cref="WorkerDelaySeconds"/>.</remarks>
        internal bool WorkerSkipped { get; private set; }

        /// <summary>
        /// Binds the worker this host serves.
        /// </summary>
        /// <param name="worker">The worker-side task.</param>
        /// <exception cref="ArgumentNullException"><paramref name="worker"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// SEPARATE FROM THE CONSTRUCTOR BECAUSE THE ORDER CANNOT BE OTHERWISE. A proxy requires its
        /// host at construction and a worker requires its own worker-side host, so the factory builds
        /// this host first, then the worker, then binds. That is the same shape the oracle has: the
        /// substrate's init inserts the worker into the thread and only then assigns <c>_Task</c>
        /// [<c>n_cst_threading_task.sru:L188</c>].
        /// </remarks>
        internal void BindWorker(SqlTaskBase worker)
        {
            ArgumentNullException.ThrowIfNull(worker);

            _worker = worker;
        }

        /// <summary>
        /// Marks the task running and clears the previous run's latches - the port of the substrate's
        /// start [<c>n_cst_threading_task.sru:L248-L254</c>].
        /// </summary>
        /// <remarks>
        /// The oracle resets the exit code [<c>:L249</c>], the error code [<c>:L248</c>] and the error
        /// text [<c>:L250</c>] BEFORE it raises the running flag [<c>:L254</c>], so a second run never
        /// reports the first run's failure. The order is preserved.
        /// </remarks>
        internal void MarkRunning()
        {
            LastExitCode = RetCode.OK;
            LastErrorCode = RetCode.OK;
            LastErrorInfo = string.Empty;

            IsRunning = true;
        }

        /// <summary>
        /// Marks the task stopped and latches its exit code - the port of the substrate's stop
        /// [<c>n_cst_threading_task.sru:L233-L234, :L258-L260</c>].
        /// </summary>
        /// <param name="exitCode">The code the run ended with.</param>
        /// <remarks>
        /// The exit code is latched BEFORE the flag is lowered, matching <c>:L233-L234</c>, so an
        /// observer that sees the task stopped always sees the code that stopped it.
        /// </remarks>
        internal void MarkStopped(long exitCode)
        {
            LastExitCode = exitCode;

            IsRunning = false;
        }

        /// <summary>
        /// Latches a framework error - the port of <c>:L213-L214</c>.
        /// </summary>
        /// <param name="errorCode">The framework error code.</param>
        /// <param name="errorInfo">The diagnostic text.</param>
        internal void LatchError(long errorCode, string? errorInfo)
        {
            LastErrorCode = errorCode;
            LastErrorInfo = errorInfo ?? string.Empty;
        }

        /// <inheritdoc/>
        public void RaiseSyncSignal() => IsSyncSignalSet = true;

        /// <inheritdoc/>
        public void ClearSyncSignal() => IsSyncSignalSet = false;

        /// <inheritdoc/>
        /// <remarks>
        /// The port of <c>call super::oninit</c> [<c>n_cst_threading_task.sru:L179-L206</c>], narrowed to
        /// the two things the SQL proxies depend on: the worker class name is required and non-empty -
        /// the oracle resolves an empty one through its own event and then ASSERTS the result
        /// [<c>:L183-L186</c>] - and the name is stored LOWER-CASED [<c>:L191</c>], which is how
        /// <see cref="TaskClassName"/> republishes it.
        /// </remarks>
        public long OnInit(string workerClassName)
        {
            if (string.IsNullOrWhiteSpace(workerClassName))
            {
                return RetCode.E_INVALID_ARGUMENT;
            }

            // [:L188-L189] Insertion fails when there is no worker to insert.
            if (_worker is null)
            {
                return RetCode.E_INVALID_OBJECT;
            }

            // [:L191] Lower-cased when stored.
            _taskClassName = workerClassName.ToLowerInvariant();

            // [:L205]
            return RetCode.OK;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Zero is the substrate's CONTINUE convention rather than <see cref="RetCode.OK"/> - the two
        /// share a value and not a meaning, and the SQL proxy's own prepare handler returns the same zero
        /// under the comment <c>//continue</c> [<c>n_cst_threading_task_sqlbase.sru:L211</c>]. Nothing
        /// needs preparing on a host that holds latches.
        /// </remarks>
        public long OnPrepare() => DataWindowBufferStore.EventContinue;

        /// <inheritdoc/>
        /// <remarks>
        /// The port of <c>of_Cancel()</c>, which signals the cancellation handle and answers
        /// <see cref="RetCode.OK"/> UNCONDITIONALLY [<c>n_cst_threading_task.sru:L369-L370</c>] - there is
        /// no arm in which it refuses, including a second cancel of an already-cancelled task.
        /// </remarks>
        public long Cancel()
        {
            if (!_cancellation.IsCancellationRequested)
            {
                _cancellation.Cancel();
            }

            // [:L318] The exit-code term of the cancellation predicate, latched so that a task stopped by
            // cancellation keeps answering cancelled after the signal has been observed.
            LastExitCode = RetCode.CANCELLED;

            return RetCode.OK;
        }

        /// <inheritdoc/>
        public long SetWorkerDelayFor(double seconds)
        {
            WorkerDelaySeconds = seconds;

            _logger.LogDebug(
                "A SQL task proxy requested a start delay of {DelaySeconds} seconds. The worker-side "
                + "substrate member this forwards to is a deliberate non-port, so the request is "
                + "recorded rather than scheduled.",
                seconds);

            return RetCode.OK;
        }

        /// <inheritdoc/>
        public long SetWorkerSkip(bool skip)
        {
            WorkerSkipped = skip;

            _logger.LogDebug(
                "A SQL task proxy set the worker skip flag to {Skip}. The worker-side substrate member "
                + "this forwards to is a deliberate non-port, so the request is recorded rather than "
                + "applied.",
                skip);

            return RetCode.OK;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            _cancellation.Dispose();
        }
    }

    /// <summary>
    /// The shipped <see cref="IQueryChildResolver"/>: it resolves the drop-down child a definition
    /// declares, on the RECEIVE side of the child payload path.
    /// </summary>
    /// <remarks>
    /// <b>THE SEND AND RECEIVE SIDES ANSWER THE SAME QUESTION AND DISAGREE ABOUT THE CONSEQUENCE.</b>
    /// On the send side an unresolvable child is a column to SKIP - the oracle answers <c>continue</c>
    /// [<c>n_cst_thread_task_sqlquery.sru:L120</c>]. On the receive side it drives the result to the
    /// datastore failure value [<c>n_cst_threading_task_sqlquery.sru:L110, :L123, :L135</c>]. That is why
    /// the two are separate seams even though they take the same arguments, and this resolver
    /// deliberately applies the same declaration test as
    /// <see cref="QueryDataWindowRuntime.TryGetChild"/> so the two cannot disagree about WHETHER a child
    /// exists while disagreeing about what to do when it does not.
    /// </remarks>
    internal sealed class QueryChildResolver : IQueryChildResolver
    {
        private readonly IQueryDataWindowRuntime _runtime;

        /// <summary>
        /// Initializes the resolver.
        /// </summary>
        /// <param name="runtime">
        /// The send-side runtime whose child declaration test this resolver reuses.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="runtime"/> is <see langword="null"/>.</exception>
        internal QueryChildResolver(IQueryDataWindowRuntime runtime)
        {
            ArgumentNullException.ThrowIfNull(runtime);

            _runtime = runtime;
        }

        /// <inheritdoc/>
        public bool TryGetChild(
            ISqlDataStore target,
            string columnName,
            out IQueryChildSurface? child)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(columnName);

            child = null;

            if (!_runtime.TryGetChild(target, columnName, out DataWindowBufferStore? store)
                || store is null)
            {
                return false;
            }

            child = new QueryChildSurface(store, target);

            return true;
        }
    }

    /// <summary>
    /// One resolved drop-down child, as the receive side drives it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE DESCRIBE ANSWERS COME FROM THE PARENT'S DEFINITION, WHICH IS WHERE POWERBUILDER KEEPS
    /// THEM.</b> A child DataWindow's own filter and sort are properties of the child object, and the
    /// receive side reads them purely to decide whether to re-apply either
    /// [<c>n_cst_threading_task_sqlquery.sru:L145, :L148</c>]. Delegating the describe to the parent
    /// store keeps one definition surface in play instead of synthesising a second one that nothing
    /// wrote.
    /// </para>
    /// <para>
    /// <b>THE RE-APPLICATION IS A CAPABILITY BOUNDARY AND IT IS DRAWN BY THE PLAN, NOT BY PREFERENCE.</b>
    /// Re-applying a stored filter means EVALUATING a DataWindow filter expression, and expression
    /// evaluation is DataServices' - <c>Expressions/DataWindowExpressionEvaluator.cs</c> - not
    /// Persistence's; AAP §0.4.2.6 gives this service no evaluator, and building one here would put the
    /// same behaviour in two services. So an EMPTY expression re-applies successfully because there is
    /// genuinely nothing to apply, and a non-empty one answers the datastore failure value. The oracle
    /// calls neither member for an empty expression - both are guarded on
    /// <c>Len(Describe(...)) &gt; 1</c> - and DISCARDS the result of both [<c>:L146, :L149</c>], so this
    /// boundary changes no observable outcome on the child path.
    /// </para>
    /// </remarks>
    internal sealed class QueryChildSurface : IQueryChildSurface
    {
        private readonly ISqlDataStore _parent;

        /// <summary>
        /// Initializes the surface.
        /// </summary>
        /// <param name="store">The child's own buffer store.</param>
        /// <param name="parent">The parent store whose definition the describe answers come from.</param>
        /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
        internal QueryChildSurface(DataWindowBufferStore store, ISqlDataStore parent)
        {
            ArgumentNullException.ThrowIfNull(store);
            ArgumentNullException.ThrowIfNull(parent);

            Store = store;
            _parent = parent;
        }

        /// <inheritdoc/>
        public DataWindowBufferStore Store { get; }

        /// <inheritdoc/>
        public string Describe(string property)
        {
            ArgumentNullException.ThrowIfNull(property);

            return _parent.Describe(property);
        }

        /// <inheritdoc/>
        public long Filter() => ReapplyStoredExpression(DataWindowProperty.TableFilter);

        /// <inheritdoc/>
        public long Sort() => ReapplyStoredExpression(DataWindowProperty.TableSort);

        /// <summary>
        /// Re-applies one stored expression, or reports that it cannot be evaluated here.
        /// </summary>
        /// <param name="property">The describe key of the expression to re-apply.</param>
        /// <returns>
        /// <see cref="DataWindowBufferStore.DataStoreSuccess"/> when there is nothing to apply;
        /// <see cref="DataWindowBufferStore.DataStoreFailure"/> otherwise.
        /// </returns>
        private long ReapplyStoredExpression(string property)
        {
            string expression = _parent.Describe(property);

            bool empty = expression.Length == 0
                || string.Equals(
                    expression,
                    SqlDataObjectStore.DescribeFailureSentinel,
                    StringComparison.Ordinal);

            return empty
                ? DataWindowBufferStore.DataStoreSuccess
                : DataWindowBufferStore.DataStoreFailure;
        }
    }

    /// <summary>
    /// The shipped <see cref="IQueryCarrierAdopter"/>: it wraps a carrier the worker relinquished so it
    /// can become the caller-side proxy's held result.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>ADOPTED, NOT CREATED - AND THE DISTINCTION IS THE WHOLE REASON THIS SEAM EXISTS.</b> Every
    /// other carrier the proxy holds is built EMPTY by <see cref="ISqlDataStoreFactory"/>; the carrier
    /// arriving at the hand-over is ALREADY POPULATED and must be wrapped rather than replaced
    /// [<c>n_cst_threading_task_sqlquery.sru:L269</c>]. Creating a fresh store and copying into it would
    /// discard the item statuses and original-value shadows the update contract compares against.
    /// </para>
    /// <para>
    /// The adopted store carries the definition the worker's carrier arrived with, which in the legacy is
    /// automatic because the handed-over object IS a complete DataStore. Here the definition travels with
    /// the carrier's assigned data object, so a caller that reads <c>GetSQLSelect</c> off the adopted
    /// store sees what the worker was retrieving rather than an empty statement.
    /// </para>
    /// </remarks>
    internal sealed class QueryCarrierAdopter : IQueryCarrierAdopter
    {
        private readonly IDataObjectRuntime _runtime;

        /// <summary>
        /// Initializes the adopter.
        /// </summary>
        /// <param name="runtime">
        /// The runtime the adopted store resolves its definition through - the same instance the send
        /// side uses, so an adopted store and the store it came from resolve identically.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="runtime"/> is <see langword="null"/>.</exception>
        internal QueryCarrierAdopter(IDataObjectRuntime runtime)
        {
            ArgumentNullException.ThrowIfNull(runtime);

            _runtime = runtime;
        }

        /// <inheritdoc/>
        public ISqlDataStore Adopt(DataWindowCarrier carrier)
        {
            ArgumentNullException.ThrowIfNull(carrier);

            return new SqlDataObjectStore(carrier, _runtime);
        }
    }
}
