// ==================================================================================================
//  TaskOperationLatch.cs - THE ONE MUTUAL-EXCLUSION STATE MACHINE ALL THREE TASK CONTRACTS SHARE
//  ------------------------------------------------------------------------------------------------
//  ORACLE   ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru     (READ ONLY)
//               :L57, :L73, :L95, :L110, :L124, :L144, :L162, :L179, :L188  - the caller-side
//               `if of_IsBusy() then return RetCode.E_BUSY` guard, written out NINE times, once per
//               mutator.
//           ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru        (READ ONLY)
//               :L237 - the worker's own `#Running` guard, which stands BEHIND the caller-side one.
//           ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru:L87-L89 - the release
//               discipline: the adopter takes the payload FIRST and only then does the sender drop
//               its reference.
//           docs/PB多线程绕坑提示.md, hazard 1 (READ ONLY) - destroying an object with work still
//               pending against it raises a memory exception.
//
//  WHY THIS TYPE EXISTS AT ALL, STATED PLAINLY. The legacy's busy guard is a READ of a flag that the
//  same thread had already set, because in process the caller and the object it configures are the
//  same thread of control: `if of_IsBusy() then return E_BUSY` cannot be raced, so the oracle needs
//  no lease. Across a service boundary the caller is a request and requests are concurrent, so the
//  identical two-step - ask whether the task is busy, then mutate it - becomes a CHECK-THEN-ACT: two
//  Exec calls, or a setter and a retrieval, both read "not busy" and both proceed, and the second
//  request's statement, parameters or descriptor array lands on the task the first one is already
//  executing. Nothing about the oracle changed; the concurrency the decomposition introduced is what
//  makes the guard insufficient in its original form.
//
//  SO THE GUARD BECOMES A LEASE, AND THE OBSERVABLE ANSWER DOES NOT CHANGE. A refused acquisition
//  still answers E_BUSY, a released task still answers E_INVALID_HANDLE, and a task that is neither
//  still proceeds. What changes is only that the answer and the action are now indivisible.
//
//  THREE STATES, AND WHY THE THIRD IS NOT REDUNDANT.
//    RUNNING   an operation owns the task right now. Anything else is refused with E_BUSY, which is a
//              retryable answer: the same call unchanged succeeds once the operation finishes.
//    RELEASED  a release has been requested. Anything else is refused with E_INVALID_HANDLE, which is
//              NOT retryable, and telling a caller BUSY here would send it into a loop that can never
//              succeed. The handle is unregistered before the release is recorded, so in the ordinary
//              case a released task is simply unresolvable and this state is never consulted; it
//              exists for the interleaving where a caller resolved the handle a moment before another
//              call released it.
//    DISPOSED  the teardown has run. Kept distinct from RELEASED because the two are separated in time
//              whenever a release arrives while an operation is in flight: the release cannot dispose
//              - that is hazard 1, a teardown with work still pending - so disposal is handed to
//              whichever of the two paths finishes last, and exactly one of them must perform it.
//
//  THE DISPOSAL HANDOFF IS THE SUBTLE PART AND IT IS WRITTEN ONCE, HERE. Both End() and
//  RequestRelease() answer the same question - "is disposal now MY duty?" - and the answer is true for
//  exactly one caller across every interleaving:
//
//      release arrives with nothing running   RequestRelease -> true    (releaser disposes)
//      release arrives during an operation    RequestRelease -> false   (cannot: hazard 1)
//                                             End            -> true    (operation disposes on exit)
//      second release                         RequestRelease -> false   (already disposed or handed off)
//      operation ends with no release         End            -> false   (nothing to dispose)
//
//  Writing that table out three times - once per task contract - would give three places for one of
//  its four rows to be subtly wrong, and a wrong row is either a leaked task or a double teardown.
//
//  NO CLOCK, NO I/O, NO DATABASE AND NO AWAIT (C-E, C-H). This type holds three booleans behind one
//  lock. It never calls a collaborator, so it can never be the outer half of a lock-ordering cycle,
//  and it deliberately exposes no member that runs caller-supplied code while the lock is held.
//
//  RULES POSITION. review_rules returns exactly one line, "No user rules provided.", so the
//  enterprise-standard baseline applies; C-B (the observable codes are the oracle's and do not change)
//  and C-H (no database, no thread, no container) bite here.
// ==================================================================================================

namespace PowerFramework.Persistence.Grpc;

/// <summary>
/// Why an attempt to take a task's operation lease was refused, or that it succeeded.
/// </summary>
/// <remarks>
/// THREE OUTCOMES RATHER THAN A BOOLEAN, because the two refusals carry different advice to the caller
/// and are reported with different codes. A boolean would force the call site to read the state again
/// to find out which - and that second read is itself a race, which is precisely the class of defect
/// this type exists to remove.
/// </remarks>
internal enum TaskLatchOutcome
{
    /// <summary>The caller owns the task until it calls <see cref="TaskOperationLatch.End"/>.</summary>
    Acquired,

    /// <summary>
    /// Another operation owns the task. Reported as <c>RetCode.E_BUSY</c> - RETRYABLE, because the same
    /// call unchanged succeeds once that operation finishes.
    /// </summary>
    Busy,

    /// <summary>
    /// The task has been released or disposed. Reported as <c>RetCode.E_INVALID_HANDLE</c> - NOT
    /// retryable, and reporting it as busy would invite an unbounded retry loop.
    /// </summary>
    Gone,
}

/// <summary>
/// The mutual-exclusion and lifetime state machine for one registered task handle.
/// </summary>
/// <remarks>
/// One instance per registered task. Every mutation, every execution and the release itself pass
/// through it, so no two of them can overlap on one task and no teardown can run while an operation is
/// still using the task. See this file's header for the disposal-handoff table.
/// </remarks>
internal sealed class TaskOperationLatch
{
    private readonly Lock _gate = new();

    private bool _running;

    private bool _released;

    private bool _disposed;

    /// <summary>
    /// Whether an operation currently owns the task.
    /// </summary>
    /// <remarks>
    /// The managed reading of the caller-side <c>of_IsBusy()</c>
    /// [<c>n_cst_threading_task_sqlbase.sru:L57</c>]. <b>A DIAGNOSTIC READ ONLY.</b> No guard may be
    /// built on it, because reading it and then acting is the check-then-act this type replaces - use
    /// <see cref="TryBegin"/>, whose answer and whose effect are one step.
    /// </remarks>
    internal bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _running;
            }
        }
    }

    /// <summary>
    /// Whether a release has been requested.
    /// </summary>
    /// <remarks>
    /// A DIAGNOSTIC READ ONLY, for the same reason as <see cref="IsRunning"/>. It is also what lets a
    /// resolve step answer <c>E_INVALID_HANDLE</c> for a handle that is registered but retiring, which is
    /// a cheap early-out rather than a guard: the authoritative refusal is <see cref="TryBegin"/>'s.
    /// </remarks>
    internal bool IsReleased
    {
        get
        {
            lock (_gate)
            {
                return _released;
            }
        }
    }

    /// <summary>
    /// Takes the operation lease.
    /// </summary>
    /// <returns>
    /// <see cref="TaskLatchOutcome.Acquired"/> when the caller now owns the task and must call
    /// <see cref="End"/>; <see cref="TaskLatchOutcome.Gone"/> when it has been released or disposed;
    /// <see cref="TaskLatchOutcome.Busy"/> when another operation owns it.
    /// </returns>
    /// <remarks>
    /// THE GONE TEST PRECEDES THE BUSY TEST, and the order is observable: a task that is both running and
    /// released must answer GONE, because its handle is already unregistered and no retry can ever
    /// succeed. Answering BUSY there would be advice that is not merely unhelpful but false.
    /// </remarks>
    internal TaskLatchOutcome TryBegin()
    {
        lock (_gate)
        {
            if (_released || _disposed)
            {
                return TaskLatchOutcome.Gone;
            }

            if (_running)
            {
                return TaskLatchOutcome.Busy;
            }

            _running = true;

            return TaskLatchOutcome.Acquired;
        }
    }

    /// <summary>
    /// Gives the operation lease back.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when a release arrived while this operation was in flight, so DISPOSAL IS
    /// NOW THIS CALLER'S DUTY; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// Call from a <see langword="finally"/>, always. An operation that returns without releasing the
    /// lease pins the task for the life of the process, and the observable symptom is every later call on
    /// that handle answering <c>E_BUSY</c> for ever.
    /// </remarks>
    internal bool End()
    {
        lock (_gate)
        {
            _running = false;

            return _released && !_disposed;
        }
    }

    /// <summary>
    /// Records that the task has been released.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when disposal is the RELEASER'S duty - nothing was in flight and nothing
    /// has disposed yet; <see langword="false"/> when it belongs to an operation still in flight, or has
    /// already happened.
    /// </returns>
    internal bool RequestRelease()
    {
        lock (_gate)
        {
            _released = true;

            return !_running && !_disposed;
        }
    }

    /// <summary>
    /// Claims the right to perform the teardown, exactly once.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> for the first caller; <see langword="false"/> for every later one.
    /// </returns>
    /// <remarks>
    /// The claim is separated from the teardown itself so the owner can run its disposal OUTSIDE this
    /// lock: a task's teardown is arbitrary work, and holding a lock that every reader also takes across
    /// it would be the one lock-ordering hazard this type is otherwise free of.
    /// </remarks>
    internal bool TryClaimDisposal()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            _disposed = true;

            return true;
        }
    }
}
