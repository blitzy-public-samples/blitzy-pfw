// ==================================================================================================
//  PersistenceSqlTaskProxyHost.cs - THE CALLER-SIDE SUBSTRATE, ONE PER PROXY
//  ------------------------------------------------------------------------------------------------
//  ROLE
//  The caller-side half of the legacy proxy pair's substrate. `n_cst_threading_task` is the class every
//  caller-side proxy inherits from, and it owns the running flag, the synchronization signal, the
//  cancellation handle, the latched exit and error state, the task identity and the worker reference.
//  `SqlTaskProxyBase` already ports the BEHAVIOUR written against that substrate; this type is the
//  substrate itself, so the two halves of every pair stay distinct types with a marshalling boundary
//  between them rather than being flattened into one async method.
//
//  WHY IT IS ONE PER PROXY AND NOT REGISTERED IN THE CONTAINER
//  Every piece of state below is per-task: the running flag, the signal, the token source, the latched
//  exit code, the task identity. A shared host would let one task's cancellation cancel another's and
//  one task's error text be read as another's. The task factories construct it, which is also where the
//  worker and the proxy are joined - see `Program.cs`.
//
//  THE SIGNAL INVERSION, RESTATED HERE BECAUSE THIS IS WHERE THE SIGNAL LIVES
//  The oracle answers busy with `(WaitForSingleObject(_hEvtSync,0) <> 0)`, and a non-zero result is
//  WAIT_TIMEOUT, meaning NOT SIGNALLED. So a running task is BUSY while the signal is CLEAR. The signal
//  is raised for the duration of each framework callback and cleared afterwards, which makes the guard a
//  re-entrancy gate: mutators are permitted from inside a callback and refused from outside one while
//  the task runs. This type only holds the flag; `SqlTaskProxyBase` composes the guard from it.
//
//  LEGACY REFERENCE (read only - never edited, never built, never shipped: constraint C-C)
//      ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru
//          :L112       #Running, raised at :L254 and lowered at :L233 and :L258-L260
//          :L118       _Task, assigned by init and nulled by uninit at :L273
//          :L124       _hEvtSync, the synchronization handle
//          :L179-L206  oninit, which inserts the worker and answers OK at :L205
//          :L191       the class name, LOWER-CASED when stored
//          :L194       the cancellation handle, manual reset and initially unsignalled
//          :L201       the task identity, taken from the worker
//          :L309-:L322 the four latched accessors and the cancellation handle accessor
//          :L318-L319  of_IsCancelled - the exit-code term OR the signal term
//          :L369-L370  of_Cancel, which signals and answers OK unconditionally
//          :L398, :L401, :L601  of_GetID, of_GetIndex, of_GetTaskClassName
//      ws_objects/pfw.thread.pbl.src/n_cst_thread_task.sru:L593, :L609  the worker-side delay and skip
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru:L213  call super::oninit
//
//  NOTHING IN THIS FILE READS THE LEGACY TREE. Every ws_objects/** path above appears in a comment.
//
//  WHAT THIS FILE DELIBERATELY DOES NOT DO
//    * It creates no thread and starts no worker. The legacy substrate marshals to a real worker thread;
//      in this service a task runs on the caller's own async continuation, and the affinity CONTRACT is
//      preserved by keeping the two halves distinct rather than by spawning a thread whose only purpose
//      would be to reproduce a marshalling hop that a headless service does not need.
//    * It reproduces no Win32 handle surface. A raw handle has no meaning in a Linux container, and the
//      migration records the handle surface as a deliberate non-port whose substitute is the token.
//    * It reads no clock and logs no statement.
// ==================================================================================================

using PowerFramework.Shared.Kernel;

namespace PowerFramework.Persistence.Tasks.TaskProxies
{
    /// <summary>
    /// The provisioned <see cref="ISqlTaskProxyHost"/>: one caller-side substrate for one proxy.
    /// </summary>
    /// <remarks>
    /// NOT THREAD SAFE, AND THAT IS THE AFFINITY CONTRACT RATHER THAN AN OVERSIGHT. The legacy annotates
    /// its caller-side classes <c>[运行在当前线程]</c> - "runs on the calling thread" - and a lock here
    /// would hide a caller that had violated that. The one exception is the cancellation token source,
    /// which is thread safe by construction because cancellation is precisely the signal that legitimately
    /// arrives from another thread.
    /// </remarks>
    internal sealed class PersistenceSqlTaskProxyHost : ISqlTaskProxyHost, IDisposable
    {
        /// <summary>
        /// The one-based position this host reports.
        /// </summary>
        /// <remarks>
        /// ONE, BECAUSE THE LEGACY INDEX IS ONE-BASED and this host owns exactly one task. The worker's
        /// committed handler walks DOWN to 1 from this value
        /// [<c>n_cst_thread_task_sqlbase.sru:L104</c>], so the base it is produced in is the base it must
        /// stay in - it is NOT converted anywhere.
        /// </remarks>
        internal const int SoleTaskIndex = 1;

        /// <summary>Assigns each host a distinct task identity.</summary>
        /// <remarks>
        /// AN INTERLOCKED COUNTER, WHICH IS THE MIGRATION'S RECORDED SUBSTITUTE for the legacy's native
        /// thread-identifier generator. The value is opaque: it identifies a task within a process and is
        /// never a credential, never parsed and never attached meaning to by a caller.
        /// </remarks>
        private static ulong _nextTaskId;

        /// <summary>Signals cancellation to the worker cooperatively.</summary>
        private readonly CancellationTokenSource _cancellation = new();

        /// <summary>The worker's registered class name, lower-cased as the oracle stores it.</summary>
        private readonly string _taskClassName;

        /// <summary>Whether this host has been disposed.</summary>
        private bool _disposed;

        /// <summary>Initializes the host.</summary>
        /// <param name="taskClassName">
        /// The worker's registered class name. LOWER-CASED on the way in, because the oracle lower-cases
        /// it when it stores it [<c>n_cst_threading_task.sru:L191</c>] and the substrate resolves a worker
        /// BY this name - so the casing is contract rather than presentation.
        /// </param>
        /// <exception cref="ArgumentException"><paramref name="taskClassName"/> is blank.</exception>
        internal PersistenceSqlTaskProxyHost(string taskClassName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(taskClassName);

            _taskClassName = taskClassName.ToLowerInvariant();
            TaskId = Interlocked.Increment(ref _nextTaskId);
        }

        /// <summary>The worker this host's proxy drives, joined after construction.</summary>
        /// <remarks>
        /// ASSIGNED RATHER THAN CONSTRUCTOR-INJECTED, AND THE ORDER IS THE ORACLE'S. The worker is
        /// constructed around its own worker-side host, which is constructed around this proxy's fault
        /// sink - so the pair cannot be built in one expression. The legacy has the same shape: the
        /// substrate's init inserts the worker into the thread's task list and only then assigns
        /// <c>_Task</c> [<c>:L188</c>]. Null until then, and null again after uninit, which is the
        /// oracle's own hazard rather than a defect of this port.
        /// </remarks>
        internal SqlTaskBase? Worker { get; set; }

        /// <inheritdoc/>
        /// <remarks>
        /// Settable by the proxy's own lifecycle, which is what the base's start and stop paths drive.
        /// It is the FIRST term of the busy composition, and a task that has not started yet delegates
        /// that question to its controller through <see cref="IsControllerBusy"/>.
        /// </remarks>
        public bool IsRunning { get; internal set; }

        /// <inheritdoc/>
        /// <remarks>
        /// FALSE, AND THAT IS ACCURATE RATHER THAN A STAND-IN. The legacy controller is a thread object
        /// owning a LIST of tasks, and its busy answer is "some task of mine is running". This service's
        /// controller is the gRPC handle registry, which owns one task per handle and runs them
        /// independently - so a task that has not started is never blocked by a sibling, and reporting
        /// otherwise would refuse a mutator the caller is entitled to make.
        /// </remarks>
        public bool IsControllerBusy => false;

        /// <inheritdoc/>
        public bool IsSyncSignalSet { get; private set; }

        /// <inheritdoc/>
        /// <remarks>
        /// BOTH OF THE ORACLE'S TERMS, and the first is what makes a stopped-by-cancellation task keep
        /// answering cancelled after its token has been disposed of by the caller.
        /// </remarks>
        public bool IsCancelled => LastExitCode == RetCode.CANCELLED || _cancellation.IsCancellationRequested;

        /// <inheritdoc/>
        public CancellationToken Cancellation =>
            _disposed ? CancellationToken.None : _cancellation.Token;

        /// <inheritdoc/>
        public long LastExitCode { get; internal set; }

        /// <inheritdoc/>
        public long LastErrorCode { get; internal set; }

        /// <inheritdoc/>
        public string LastErrorInfo { get; internal set; } = string.Empty;

        /// <inheritdoc/>
        public ulong TaskId { get; }

        /// <inheritdoc/>
        public int TaskIndex => SoleTaskIndex;

        /// <inheritdoc/>
        public string TaskClassName => _taskClassName;

        /// <inheritdoc/>
        public SqlTaskBase? Task => Worker;

        /// <inheritdoc/>
        public void RaiseSyncSignal() => IsSyncSignalSet = true;

        /// <inheritdoc/>
        public void ClearSyncSignal() => IsSyncSignalSet = false;

        /// <inheritdoc/>
        /// <remarks>
        /// <para>
        /// The oracle's init inserts the worker into the owning thread's task list and answers
        /// <see cref="RetCode.OK"/> [<c>:L205</c>], or the failing code from that insertion
        /// [<c>:L189</c>]. Here the worker is already joined by the factory that built the pair, so the
        /// insertion is the JOIN being verified rather than performed: a host with no worker cannot serve
        /// a proxy, and reporting that as an invalid-argument failure is what the oracle's own
        /// non-empty-name assertion does one step earlier.
        /// </para>
        /// <para>
        /// The supplied name is checked against the registered one rather than ignored. The oracle
        /// resolves an empty name through its own event and then asserts the result is non-empty
        /// [<c>:L183-L186</c>], so a name that disagrees with the registered worker is a caller defect the
        /// oracle would have caught at that assertion.
        /// </para>
        /// </remarks>
        public long OnInit(string workerClassName)
        {
            ArgumentNullException.ThrowIfNull(workerClassName);

            if (Worker is null)
            {
                return RetCode.E_INVALID_OBJECT;
            }

            if (workerClassName.Length > 0
                && !string.Equals(workerClassName, _taskClassName, StringComparison.OrdinalIgnoreCase))
            {
                return RetCode.E_INVALID_ARGUMENT;
            }

            return RetCode.OK;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The substrate's prepare handler has nothing to prepare on a host that holds flags, and the SQL
        /// proxy DISCARDS this value in any case - which is recorded at the point of reproduction in
        /// <c>SqlTaskProxyBase</c> rather than relied upon here.
        /// </remarks>
        public long OnPrepare() => RetCode.OK;

        /// <inheritdoc/>
        /// <remarks>
        /// SIGNALS AND ANSWERS SUCCESS UNCONDITIONALLY, exactly as the oracle does [<c>:L369-L370</c>] -
        /// including when cancellation has already been requested, because a second cancel is not a
        /// failure. A disposed host answers success too: the task it stood for is already gone, so
        /// reporting a failure would make every teardown path report an error it cannot act on.
        /// </remarks>
        public long Cancel()
        {
            if (!_disposed)
            {
                _cancellation.Cancel();
            }

            return RetCode.OK;
        }

        /// <summary>The worker's start delay in seconds, as last requested.</summary>
        /// <remarks>
        /// HELD HERE BECAUSE THE ORACLE HOLDS IT ON THE SUBSTRATE, NOT ON THE TASK. The proxy's
        /// <c>of_SetDelayFor</c> forwards to the WORKER-SIDE substrate <c>n_cst_thread_task</c>
        /// [<c>:L593</c>], which <see cref="SqlTaskBase"/> deliberately does not model - so the substrate
        /// half of the pair is where the value lives, and this host is that half's port. It is readable
        /// so a characterization run can observe what a caller requested.
        /// </remarks>
        internal double WorkerDelaySeconds { get; private set; }

        /// <summary>Whether the worker is marked to be skipped, as last requested.</summary>
        /// <remarks>Held for the same reason as <see cref="WorkerDelaySeconds"/> [<c>:L609</c>].</remarks>
        internal bool WorkerSkipped { get; private set; }

        /// <inheritdoc/>
        /// <remarks>
        /// <para>
        /// RECORDED RATHER THAN ENACTED, AND THE DIFFERENCE IS HONEST. In the legacy the delay defers a
        /// worker whose start this substrate controls; in this service a task runs on the caller's own
        /// continuation the moment the caller asks for it, so there is no start for a delay to defer. The
        /// value is therefore retained and readable - which is what a caller reading its own setting back
        /// observes - and no timer is created.
        /// </para>
        /// <para>
        /// A NEGATIVE DELAY IS REFUSED. Every other value is accepted, because the oracle applies no upper
        /// bound and inventing one would add a failure mode it does not have.
        /// </para>
        /// </remarks>
        public long SetWorkerDelayFor(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0d)
            {
                return RetCode.E_INVALID_ARGUMENT;
            }

            WorkerDelaySeconds = seconds;

            return RetCode.OK;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Recorded on the same terms as the delay. A skipped task is one the legacy's thread loop passes
        /// over; this service's caller drives its task directly, so the flag is retained and readable
        /// rather than enacted by a loop that does not exist.
        /// </remarks>
        public long SetWorkerSkip(bool skip)
        {
            WorkerSkipped = skip;

            return RetCode.OK;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Releases the cancellation source, which is the only disposable this host owns - the worker
        /// belongs to the factory that built the pair and is disposed with it. Idempotent, because a proxy
        /// and a registry may both release the pair on a teardown path.
        /// </remarks>
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
}
