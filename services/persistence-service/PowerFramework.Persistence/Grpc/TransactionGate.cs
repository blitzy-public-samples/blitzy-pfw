// ==================================================================================================
//  TransactionGate.cs - THE PER-TRANSACTION MUTUAL-EXCLUSION GATE, HOLDABLE ACROSS AN AWAIT
//  ------------------------------------------------------------------------------------------------
//  ROLE
//  One gate per POOLED TRANSACTION OBJECT. Every operation that touches that object - a query, an
//  update, a command, an autocommit change, a commit, a rollback, a session teardown - holds this
//  gate for the whole of its critical section, so no two of them can ever be inside one transaction
//  object at the same time.
//
//  WHY IT EXISTS AT ALL: THREAD AFFINITY IS CONTRACT, NOT COMMENTARY
//  The legacy's concurrency design is a PROXY PAIR in which every concurrency class exists twice - a
//  caller-side `n_cst_threading*` and a worker-side `n_cst_thread*` - specifically so that NO OBJECT
//  IS EVER TOUCHED FROM TWO THREADS, and the required execution context is recorded in the objects'
//  own source comments. The migration plan states that those annotations are a contract rather than an
//  implementation detail and must be reproduced as an EXPLICIT MARSHALLING BOUNDARY rather than
//  flattened (AAP 0.4.5.4). A gRPC server is concurrent by nature, so without a gate two simultaneous
//  calls would touch one transaction object from two threads - something the oracle structurally
//  prevents.
//
//  WHY IT IS KEYED ON THE TRANSACTION AND NOT ON THE SESSION
//  The pool is REFERENCE COUNTED and keys its entries on WHOLE-descriptor equality
//  [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L136-L146], so two sessions opened
//  with equal descriptors are handed the SAME transaction object [:L158-L172]. A per-session gate
//  would leave those two sessions racing on one object while APPEARING to be synchronized. The oracle
//  has no such race because each pool lives on ONE worker thread - stored per-thread under a named
//  framework datum [n_cst_thread_task_sqlbase.sru:L194-L206] - so every referrer of an entry is on
//  that same thread by construction. A process-wide pool has to reproduce that guarantee explicitly.
//
//  ============ WHY THIS IS NOT `System.Threading.Lock`, WHICH IS WHAT IT REPLACED ================
//  A `Lock` CANNOT BE HELD ACROSS AN AWAIT, and one of the operations that must hold it is
//  asynchronous: C-05's `Query` streams a result, so its SQL execution and its result capture span
//  continuations. With a `Lock` the streaming query simply could not take the gate, which is exactly
//  how it came to run UNGATED while its siblings were gated - and an operation outside the gate makes
//  the gate meaningless for every operation inside it, because the object it protects is shared.
//
//  A gate that some operations can hold and others cannot is not a weaker guarantee; it is no
//  guarantee. So the primitive is a `SemaphoreSlim` of one, which both a synchronous and an
//  asynchronous caller can hold, and every operation on one transaction object now takes THE SAME
//  gate whichever kind it is.
//
//  IT IS NOT REENTRANT, AND THAT IS DELIBERATE RATHER THAN A LIMITATION. No critical section in this
//  service acquires this gate a second time - each handler takes it once, at the top of its critical
//  section - and keeping it non-reentrant means a future edit that nested one acquisition inside
//  another would DEADLOCK VISIBLY at the first test run rather than silently defeat the exclusion the
//  way a reentrant lock would.
//
//  FAIRNESS AND CANCELLATION
//  `SemaphoreSlim` grants in arrival order under contention, so a long streaming retrieval cannot
//  starve a commit indefinitely. The asynchronous acquisition honours the caller's cancellation token,
//  so a client that goes away while queued behind another operation stops waiting rather than holding
//  a request thread; the synchronous acquisition does not take one, because every synchronous critical
//  section in this service is short and bounded.
//
//  NO PERFORMANCE CLAIM IS MADE OR IMPLIED. The repository publishes no SLA, latency budget or
//  throughput target anywhere (AAP 0.8.5), and serializing operations on one transaction object is a
//  CORRECTNESS requirement inherited from the legacy's thread affinity rather than a tuning choice.
//
//  WHAT THIS FILE DELIBERATELY DOES NOT DO
//    * It holds no transaction, no session and no handle. It is exclusion and nothing else.
//    * It reads no clock, so it introduces no non-determinism into paired characterization captures.
//    * It logs nothing: a gate that logged would record the timing of every database operation.
//    * It never times out. A bounded wait would let a second operation into a transaction object the
//      first is still inside, which is the exact hazard this removes.
// ==================================================================================================

namespace PowerFramework.Persistence.Grpc;

/// <summary>
/// Serializes every operation on one pooled transaction object, and can be held across an
/// <see langword="await"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>ONE INSTANCE PER TRANSACTION OBJECT, SHARED BY EVERY SESSION THAT BORROWED IT.</b> See the
/// file header for why the granularity is the transaction rather than the session, and why a
/// reference index would be the wrong key.
/// </para>
/// <para>
/// <b>ALWAYS TAKEN WITH A <see langword="using"/> DECLARATION.</b> Both acquisitions answer a
/// scope whose disposal releases the gate, so an early return, a thrown exception and a normal
/// fall-through all release it. A hand-released gate would be one <c>return</c> away from pinning
/// a transaction object for the life of the process.
/// </para>
/// <para>
/// NOT DISPOSED, AND NOT DISPOSABLE. Its lifetime is the transaction object's, it holds no
/// unmanaged resource of its own beyond the semaphore's own wait handle - which is created lazily
/// and only by the wait-handle-based APIs this type never uses - and making it disposable would
/// invite a caller to dispose a gate that another session is still holding.
/// </para>
/// </remarks>
internal sealed class TransactionGate
{
    /// <summary>The exclusion primitive: one permit, so exactly one holder at a time.</summary>
    /// <remarks>
    /// A COUNT OF ONE AND A MAXIMUM OF ONE. The maximum is stated so that a stray extra release -
    /// which a hand-written release path could produce - throws immediately instead of quietly
    /// admitting two holders.
    /// </remarks>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Takes the gate, blocking until it is free.
    /// </summary>
    /// <returns>A scope whose disposal releases the gate.</returns>
    /// <remarks>
    /// FOR SYNCHRONOUS CRITICAL SECTIONS. It takes no cancellation token deliberately: every
    /// synchronous critical section in this service is a short, bounded sequence of collaborator
    /// calls, and abandoning a wait midway through a transaction lifecycle operation would leave
    /// the caller unable to tell whether it had run.
    /// </remarks>
    internal TransactionGateScope Enter()
    {
        _gate.Wait();

        return new TransactionGateScope(_gate);
    }

    /// <summary>
    /// Takes the gate asynchronously, so it can be held across an <see langword="await"/>.
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancels the WAIT, never the holder. A caller that goes away while queued stops waiting; a
    /// caller that already holds the gate is unaffected.
    /// </param>
    /// <returns>A scope whose disposal releases the gate.</returns>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was signalled before the gate was taken.
    /// </exception>
    /// <remarks>
    /// FOR THE STREAMING OPERATIONS, which are the reason this type exists rather than a
    /// <see cref="Lock"/>. The gate is taken BEFORE the first statement and released only after the
    /// last result has been captured, so a retrieval's execution and its result capture are one
    /// critical section even though they span continuations.
    /// </remarks>
    internal async ValueTask<TransactionGateScope> EnterAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        return new TransactionGateScope(_gate);
    }
}

/// <summary>
/// One holder's tenure on a <see cref="TransactionGate"/>. Disposing it releases the gate.
/// </summary>
/// <remarks>
/// <para>
/// A STRUCT SO THAT TAKING THE GATE ALLOCATES NOTHING, and <see langword="readonly"/> so a copy
/// cannot be mutated into releasing twice.
/// </para>
/// <para>
/// <b>DISPOSAL IS IDEMPOTENT-BY-CONSTRUCTION RATHER THAN BY A FLAG.</b> The semaphore's maximum
/// count is one, so a second release throws
/// <see cref="SemaphoreFullException"/> - which is the correct outcome: it names a real defect in a
/// caller's release path at the moment it happens, rather than silently admitting two holders into
/// one transaction object. The <see langword="using"/> pattern every call site uses disposes
/// exactly once.
/// </para>
/// </remarks>
/// <param name="gate">The semaphore to release. Never <see langword="null"/> in practice.</param>
internal readonly struct TransactionGateScope(SemaphoreSlim gate) : IDisposable
{
    /// <inheritdoc/>
    public void Dispose() => gate?.Release();
}
