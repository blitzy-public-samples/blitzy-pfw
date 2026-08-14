// ==================================================================================================
//  UncommittedWorkReclaimTests - AN ABANDONED WRITE MUST NOT DENY EVERY OTHER WRITER FOR A QUARTER
//  OF AN HOUR
//  ------------------------------------------------------------------------------------------------
//  WHY THIS FILE EXISTS, STATED AS THE DEFECT IT WOULD HAVE CAUGHT
//
//  A runtime probe of the deployed stack opened a transaction session, executed an INSERT without
//  committing, and abandoned it. A second session on a DIVERGENT transaction descriptor - which the
//  pool answers with a connection of its own rather than the same one - then failed every write with
//
//      SQLite Error 5: 'database is locked'
//
//  from t+31 s to t+826 s, and only succeeded at t+870 s, when the handle reclaim pass reached the
//  abandoned session at the fifteen-minute generic idle bound and tore it down. Reads kept answering
//  throughout and every /health stayed 200, so nothing was down: the availability loss was confined
//  to writers, which is what made it invisible to every assertion in the suite.
//
//  NOTHING ABOUT THE RECOVERY WAS WRONG EXCEPT WHEN IT HAPPENED. The reclaim path already rolls back
//  and releases on its way out - Teardown, pool release, disconnect. What was wrong is that ONE window
//  governed every handle, so a session holding a lock every other writer needs waited exactly as long
//  as one holding nothing at all. The fix is a second, shorter window that applies only while a session
//  is holding uncommitted work.
//
//  WHAT THESE ROWS PIN
//
//    1. THE MARKER IS SET BY A STATEMENT AND CLEARED BY EVERY RELEASE. Set before execution, because a
//       statement that fails part-way can still have taken a lock; cleared by commit, rollback,
//       disconnect, reconnect and either auto-commit transition, because each of those ends the
//       transaction the marker was about. A marker that were never cleared would put a clean session in
//       the shorter window for the rest of the process's life.
//
//    2. NOTHING IS MARKED UNDER AUTO-COMMIT, because each statement commits itself and retains no lock.
//
//    3. THE SHORTER WINDOW REACHES THE PINNING HANDLE, WHICH IS THE WHOLE MECHANISM. The abandoned
//       session was PINNED by the command task that wrote through it, and the session pass never touches
//       a pinned session - so shortening the session window alone would have changed nothing at all.
//       The row in section 2 that walks two passes is the miniature of the reported scenario.
//
//    4. A CLEAN SESSION IS UNTOUCHED. The generic window still governs it, at the same fifteen minutes
//       as before, which is what keeps this a narrowing of one case rather than a global retune.
//
//    5. A RUNNING OPERATION IS EXEMPT. An update against a contended file-backed store can legitimately
//       outrun two minutes, and reclaiming one underneath itself would turn a cleanup into data loss.
//
//    6. THE WINDOW ORDER IS ENFORCED RATHER THAN DOCUMENTED. A shorter window larger than the generic
//       one could never select, and a setting that reads as tuned and does nothing is worse than none.
//
//  ORACLE
//  ------------------------------------------------------------------------------------------------
//  None, and the absence is structural rather than an omission. The legacy pool holds transactions for a
//  single process on one machine [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru], with no
//  server tier, no sessions belonging to remote callers and therefore no notion of one caller's abandoned
//  transaction denying another's. The boundary created the exposure and owns the control (AAP 0.1.4).
//  What IS preserved is the legacy's own release sequence, which the reclaim path already performs and
//  which this change only reaches sooner.
// ==================================================================================================

using System;
using System.Collections.Generic;

using Microsoft.Extensions.Options;

using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Grpc;
using PowerFramework.Persistence.Transactions;

using Xunit;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Conformance for the uncommitted-work marker, the second idle window it selects, and the ordering rule
/// between the two windows.
/// </summary>
/// <remarks>
/// EVERY ROW BUILDS ITS OWN <see cref="LifecycleHarness"/>, whose pool, four registries and reclaimer are
/// the shipped types over a settable clock - so what is exercised here is the production reclaim
/// orchestration and not a re-implementation of it.
/// </remarks>
public sealed class UncommittedWorkReclaimTests
{
    /// <summary>A statement of the shape the reported scenario abandoned.</summary>
    private const string WriteStatement = "INSERT INTO COMPANY (NAME, AGE) VALUES ('a', 1)";

    /// <summary>One second past the shorter window, in seconds.</summary>
    private const int PastUncommittedWorkWindowSeconds =
        HandleLifecycleOptions.DefaultUncommittedWorkIdleExpirySeconds + 1;

    /// <summary>One second past the generic window, in seconds.</summary>
    private const int PastGenericWindowSeconds = HandleLifecycleOptions.DefaultIdleExpirySeconds + 1;

    // ==============================================================================================
    //  SECTION 1 - THE MARKER: WHAT SETS IT, AND EVERYTHING THAT CLEARS IT
    // ==============================================================================================

    /// <summary>
    /// A statement executed inside an explicit transaction marks it as holding uncommitted work.
    /// </summary>
    /// <remarks>
    /// THE STARTING POINT FOR EVERY OTHER ROW, and it is asserted from the OUTSIDE - through the
    /// statement member a caller actually uses - rather than by calling the marker directly, so a
    /// statement path that stopped marking fails here.
    /// </remarks>
    [Fact]
    public void AStatementInsideAnExplicitTransactionMarksUncommittedWork()
    {
        LifecycleHarness harness = new();
        IPooledTransaction transaction = harness.Borrow();

        Assert.False(transaction.HasUncommittedWork);

        _ = transaction.Exec(WriteStatement, TestContext.Current.CancellationToken);

        Assert.True(transaction.HasUncommittedWork);
    }

    /// <summary>
    /// A statement executed under auto-commit marks nothing.
    /// </summary>
    /// <remarks>
    /// THE NEGATIVE THAT KEEPS THE SHORTER WINDOW OFF THE COMMON CASE. Under auto-commit each statement
    /// commits itself and retains no lock, so a session that never opened an explicit transaction must not
    /// be treated as one holding work - it would be reclaimed at two minutes for doing nothing wrong.
    /// </remarks>
    [Fact]
    public void AStatementUnderAutoCommitMarksNothing()
    {
        LifecycleHarness harness = new();
        IPooledTransaction transaction = harness.Borrow();

        Assert.Equal(RetCode.OK, transaction.TrySetAutoCommit(true));

        _ = transaction.Exec(WriteStatement, TestContext.Current.CancellationToken);

        Assert.False(transaction.HasUncommittedWork);
    }

    /// <summary>
    /// Every operation that ends a transaction clears the marker.
    /// </summary>
    /// <param name="release">Which release path the row exercises.</param>
    /// <remarks>
    /// <para>
    /// A THEORY BECAUSE THE PROPERTY IS ABOUT THE SET, NOT ABOUT ANY ONE PATH. Five paths end the
    /// transaction a marker describes, and a fix that cleared on commit alone would leave a rolled-back
    /// session in the shorter window for ever - which is the mirror image of the defect: a clean session
    /// reclaimed early rather than a dirty one reclaimed late.
    /// </para>
    /// <para>
    /// THE RECONNECT CASE IS THE LEAST OBVIOUS AND THE MOST IMPORTANT. A connect pre-emptively disconnects
    /// whatever was open, so the work the marker described no longer exists anywhere; carrying it across
    /// would attribute a previous connection's lock to a fresh one.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(ReleasePath.Commit)]
    [InlineData(ReleasePath.Rollback)]
    [InlineData(ReleasePath.Disconnect)]
    [InlineData(ReleasePath.Reconnect)]
    [InlineData(ReleasePath.AutoCommitOn)]
    [InlineData(ReleasePath.AutoCommitOff)]
    public void EveryReleasePathClearsTheMarker(ReleasePath release)
    {
        LifecycleHarness harness = new();
        IPooledTransaction transaction = harness.Borrow();

        _ = transaction.Exec(WriteStatement, TestContext.Current.CancellationToken);
        Assert.True(transaction.HasUncommittedWork);

        long outcome = release switch
        {
            ReleasePath.Commit => transaction.Commit(),
            ReleasePath.Rollback => transaction.Rollback(),
            ReleasePath.Disconnect => transaction.Disconnect(),
            ReleasePath.Reconnect => transaction.Connect(TestContext.Current.CancellationToken),
            ReleasePath.AutoCommitOn => transaction.TrySetAutoCommit(true),
            ReleasePath.AutoCommitOff => transaction.TrySetAutoCommit(false),
            _ => throw new ArgumentOutOfRangeException(nameof(release)),
        };

        // The release itself must have succeeded, or the row would be asserting the marker is cleared by a
        // path that did not run.
        Assert.Equal(RetCode.OK, outcome);
        Assert.False(transaction.HasUncommittedWork);
    }

    /// <summary>
    /// A second statement after a commit marks the transaction again.
    /// </summary>
    /// <remarks>
    /// THE MARKER IS A STATE, NOT A ONE-SHOT FLAG. A session that commits and then writes again is holding
    /// work exactly as it was the first time, and an implementation that cleared permanently - or that set
    /// only once - would put the second write back in the generic window.
    /// </remarks>
    [Fact]
    public void AStatementAfterACommitMarksTheTransactionAgain()
    {
        LifecycleHarness harness = new();
        IPooledTransaction transaction = harness.Borrow();

        _ = transaction.Exec(WriteStatement, TestContext.Current.CancellationToken);
        Assert.Equal(RetCode.OK, transaction.Commit());
        Assert.False(transaction.HasUncommittedWork);

        _ = transaction.Exec(WriteStatement, TestContext.Current.CancellationToken);
        Assert.True(transaction.HasUncommittedWork);
    }

    // ==============================================================================================
    //  SECTION 2 - THE TWO WINDOWS, AND WHICH HANDLES EACH GOVERNS
    // ==============================================================================================

    /// <summary>
    /// A session holding uncommitted work is reclaimed at the shorter window; one holding nothing is not.
    /// </summary>
    /// <remarks>
    /// THE TWO CASES ARE TAKEN THROUGH THE SAME HARNESS AT THE SAME AGE, which is what makes this an
    /// assertion about the DISTINCTION rather than about a window. A change that shortened the window for
    /// everything would satisfy the first half and fail the second.
    /// </remarks>
    [Fact]
    public void TheShorterWindowGovernsASessionHoldingWorkAndNotOneHoldingNothing()
    {
        LifecycleHarness harness = new();

        // TWO DIVERGENT DESCRIPTORS, WHICH IS THE SHAPE THE PROBE HAD AND IS NOT INCIDENTAL HERE. The pool
        // is keyed by descriptor, so two sessions asking for the SAME descriptor share one transaction -
        // and a shared transaction cannot be dirty for one session and clean for the other. It is also
        // why the reported scenario failed the way it did: the victim's divergent descriptor got a
        // connection of its own and met the file lock rather than joining the holder's transaction.
        TransactionSession dirty = RegisterSessionOn(harness, "holder.db");
        TransactionSession clean = RegisterSessionOn(harness, "bystander.db");

        Assert.NotSame(dirty.Transaction, clean.Transaction);

        _ = dirty.Transaction.Exec(WriteStatement, TestContext.Current.CancellationToken);

        Assert.True(dirty.Transaction.HasUncommittedWork);
        Assert.False(clean.Transaction.HasUncommittedWork);

        harness.Clock.Advance(TimeSpan.FromSeconds(PastUncommittedWorkWindowSeconds));

        // ONE RECLAIMED, NOT TWO. Both sessions are the same age and neither has been touched.
        Assert.Equal(1, harness.Reclaimer.ReclaimOnce());
        Assert.Equal(1, harness.Registry.Count);

        // And the survivor is the clean one, which is the half a count alone cannot establish.
        Assert.True(harness.Registry.TryResolve(
            new SessionHandle { SessionId = clean.SessionId },
            out _));
    }

    /// <summary>
    /// A session holding nothing is still reclaimed at the generic window, unchanged.
    /// </summary>
    /// <remarks>
    /// THE PRE-EXISTING BEHAVIOUR, ASSERTED SO THAT IT CANNOT BE LOST WHILE ADDING THE OTHER. The generic
    /// window is deliberately generous because reclaiming a handle a caller still intends to use is the
    /// worse fault, and that reasoning is untouched for every handle that is holding nothing.
    /// </remarks>
    [Fact]
    public void AnIdleSessionHoldingNothingIsStillReclaimedAtTheGenericWindow()
    {
        LifecycleHarness harness = new();

        _ = harness.RegisterSession();

        harness.Clock.Advance(TimeSpan.FromSeconds(PastUncommittedWorkWindowSeconds));
        Assert.Equal(0, harness.Reclaimer.ReclaimOnce());

        harness.Clock.Advance(
            TimeSpan.FromSeconds(PastGenericWindowSeconds - PastUncommittedWorkWindowSeconds));

        Assert.Equal(1, harness.Reclaimer.ReclaimOnce());
        Assert.Equal(0, harness.Registry.Count);
    }

    /// <summary>
    /// The reported scenario in miniature: an abandoned write pinned by its own task recovers in two
    /// passes rather than waiting for the generic window.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>THIS IS THE ROW THE WHOLE CHANGE TURNS ON.</b> The probe's abandoned session was PINNED by the
    /// task that had written through it, and the session pass never touches a pinned session however idle
    /// or however dirty it is - for good reason, since reclaiming one would hand its transaction back
    /// underneath a working task. So a shorter window applied to the session pass ALONE would have changed
    /// nothing whatsoever: the session would still have waited for its task, and the task would still have
    /// waited fifteen minutes.
    /// </para>
    /// <para>
    /// The task pass therefore applies the same distinction, using the set of session identifiers holding
    /// uncommitted work. Pass one takes the task, which unpins the session; pass two takes the session,
    /// which is the pass that rolls back and releases the lock. Two passes at a sixty-second sweep is the
    /// worst-case recovery the last row of this section states.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnAbandonedWritePinnedByItsOwnTaskRecoversInTwoPasses()
    {
        LifecycleHarness harness = new();

        TransactionSession session = harness.RegisterSession();

        _ = harness.Updates.Register(session.SessionId, new StubUpdateSurface(), out _);
        _ = session.Transaction.Exec(WriteStatement, TestContext.Current.CancellationToken);

        harness.Clock.Advance(TimeSpan.FromSeconds(PastUncommittedWorkWindowSeconds));

        // ONE PASS TAKES BOTH, AND THAT IS THE ORDERING OF THE PASS RATHER THAN LUCK. The reclaimer runs
        // the three task registries FIRST and only then computes the pin set, so a task collected by this
        // pass is not in the set that protects its session - which is what lets the session follow it out
        // in the same sixty-second sweep instead of the next one.
        Assert.Equal(2, harness.Reclaimer.ReclaimOnce());
        Assert.Equal(0, harness.Updates.Count);
        Assert.Equal(0, harness.Registry.Count);

        // AND THE POOL REFERENCE WENT WITH THE SESSION, which is the release that ends the lock. The
        // disconnect is the evidence: it is the last-reference path the legacy performs on release.
        Assert.Equal(0, harness.Pool.UpperBound);
        Assert.Equal(1, harness.Engine.DisconnectCalls);
    }

    /// <summary>
    /// A handle whose operation is running is exempt from the shorter window.
    /// </summary>
    /// <remarks>
    /// THE PROTECTION THAT KEEPS THIS FROM BECOMING DATA LOSS. An update against a contended file-backed
    /// store can legitimately outrun a two-minute window, and its session is dirty by definition while it
    /// is writing - so without the exemption the shorter window would reclaim exactly the writes that are
    /// working. The generic window still governs a running handle, as it did before.
    /// </remarks>
    [Fact]
    public void ARunningHandleIsExemptFromTheShorterWindow()
    {
        LifecycleHarness harness = new();

        TransactionSession session = harness.RegisterSession();

        UpdateTaskEntry entry = Assert.IsType<UpdateTaskEntry>(
            harness.Updates.Register(session.SessionId, new StubUpdateSurface(), out _));

        _ = session.Transaction.Exec(WriteStatement, TestContext.Current.CancellationToken);

        // AN OPERATION IN FLIGHT, taken through the latch every C-06 call goes through.
        Assert.Equal(TaskLatchOutcome.Acquired, entry.TryBeginOperation());
        Assert.True(entry.IsRunning);

        harness.Clock.Advance(TimeSpan.FromSeconds(PastUncommittedWorkWindowSeconds));

        // NEITHER HANDLE MOVES: the task is running, and the session it pins is therefore untouchable.
        Assert.Equal(0, harness.Reclaimer.ReclaimOnce());
        Assert.Equal(1, harness.Updates.Count);
        Assert.Equal(1, harness.Registry.Count);

        // Once the operation ends, the same age is enough - which proves the exemption was the running
        // state and not the age. EndOperation's answer is about whose duty disposal is rather than about
        // whether the operation ended, so what is asserted is the state it leaves.
        _ = entry.EndOperation();
        Assert.False(entry.IsRunning);

        // BOTH HANDLES, IN ONE PASS, for the ordering reason the row above states.
        Assert.Equal(2, harness.Reclaimer.ReclaimOnce());
    }

    /// <summary>
    /// The shipped windows leave worst-case recovery at three minutes rather than fifteen and a half.
    /// </summary>
    /// <remarks>See the two rows above for why one sweep suffices even when the session is pinned.</remarks>
    /// <remarks>
    /// <para>
    /// AN ARITHMETIC ASSERTION OVER THE SHIPPED DEFAULTS, AND IT IS NOT A LATENCY BUDGET. The repository
    /// publishes no performance objective and none is claimed here (AAP 0.8.5): what this row pins is the
    /// BOUND on how long one abandoned caller may deny writes to every other one, which is a correctness
    /// property of the reclaim policy rather than a measurement of anything.
    /// </para>
    /// <para>
    /// The worst case is the window plus one sweep interval, because a handle that expires immediately
    /// after a pass waits for the next. Two passes are needed when the session is pinned, which the row
    /// above walks - so the stated bound is the window plus two sweeps.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheShippedWindowsBoundWorstCaseRecoveryAtThreeMinutes()
    {
        HandleLifecycleOptions shipped = new();

        Assert.Equal(120, shipped.UncommittedWorkIdleExpirySeconds);
        Assert.Equal(60, shipped.SweepIntervalSeconds);
        Assert.Equal(900, shipped.IdleExpirySeconds);

        // ONE SWEEP, NOT TWO: a pass collects the pinning handle and the session it pinned together,
        // because the pin set is computed after the task registries have run. The sweep interval is added
        // because a handle that expires immediately after a pass waits for the next one.
        int worstCase = shipped.UncommittedWorkIdleExpirySeconds + shipped.SweepIntervalSeconds;

        Assert.Equal(180, worstCase);

        // AND IT IS AT LEAST THREE TIMES BETTER THAN THE MEASURED 870 SECONDS, which is the number this
        // change exists to move. Stated as a comparison against the generic bound rather than against the
        // measurement, because the measurement is evidence and the bound is the contract.
        Assert.True(
            worstCase < shipped.IdleExpirySeconds,
            $"The uncommitted-work path's worst case is {worstCase}s against a generic window of "
                + $"{shipped.IdleExpirySeconds}s, so the shorter window buys nothing.");
    }

    // ==============================================================================================
    //  SECTION 3 - THE ORDERING RULE BETWEEN THE TWO WINDOWS
    // ==============================================================================================

    /// <summary>
    /// A shorter window larger than the generic one is refused at startup.
    /// </summary>
    /// <remarks>
    /// AN UNREACHABLE SETTING IS WORSE THAN NO SETTING, which is the same reasoning the per-caller ceiling
    /// rule carries. The shorter window is only ever consulted for a handle the generic window has not
    /// already expired, so a larger value can never select - and an operator who set it would believe an
    /// abandoned write session was being reclaimed sooner while nothing had changed.
    /// </remarks>
    [Fact]
    public void AnUncommittedWorkWindowAboveTheGenericOneIsRefused()
    {
        PersistenceOptions options = new PersistenceOptionsBuilder().Build();
        options.Handles.IdleExpirySeconds = 900;
        options.Handles.UncommittedWorkIdleExpirySeconds = 901;

        ValidateOptionsResult result = new PersistenceOptionsValidator().Validate(name: null, options);

        Assert.True(result.Failed);

        // THE MESSAGE NAMES BOTH KEYS AND THE WAY OUT, because a refusal an operator cannot act on is a
        // startup failure they will work around rather than fix.
        string failure = Assert.Single(result.Failures!);

        Assert.StartsWith("Handles:UncommittedWorkIdleExpirySeconds", failure, StringComparison.Ordinal);
        Assert.Contains("Handles:IdleExpirySeconds", failure, StringComparison.Ordinal);
        Assert.Contains("Set the two equal", failure, StringComparison.Ordinal);
    }

    /// <summary>
    /// Setting the two windows equal is accepted, and is how a deployment opts out.
    /// </summary>
    /// <remarks>
    /// THE OPT-OUT HAS TO BE EXPRESSIBLE, or the rule above would be a policy rather than a bound. Equal
    /// windows restore the pre-existing behaviour exactly: every handle governed by one number, which is a
    /// position a deployment is entitled to hold.
    /// </remarks>
    [Fact]
    public void EqualWindowsAreAcceptedAsTheOptOut()
    {
        PersistenceOptions options = new PersistenceOptionsBuilder().Build();
        options.Handles.IdleExpirySeconds = 900;
        options.Handles.UncommittedWorkIdleExpirySeconds = 900;

        ValidateOptionsResult result = new PersistenceOptionsValidator().Validate(name: null, options);

        Assert.True(
            result.Succeeded,
            $"Equal windows were refused: {string.Join(" | ", result.Failures ?? [])}");
    }

    /// <summary>
    /// A non-positive shorter window is refused by the member's own annotation.
    /// </summary>
    /// <remarks>
    /// ZERO WOULD MEAN "RECLAIM A DIRTY SESSION THE INSTANT IT IS SEEN", which would reclaim a session
    /// between two statements of the same caller's own transaction. The annotation refuses it at the type's
    /// boundary rather than leaving the reclaim pass to interpret it.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANonPositiveUncommittedWorkWindowIsRefused(int seconds)
    {
        PersistenceOptions options = new PersistenceOptionsBuilder().Build();
        options.Handles.UncommittedWorkIdleExpirySeconds = seconds;

        ValidateOptionsResult result = new PersistenceOptionsValidator().Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures!,
            failure => failure.Contains(
                "UncommittedWorkIdleExpirySeconds",
                StringComparison.Ordinal));
    }

    /// <summary>
    /// Registers a session against a descriptor of its own, so it gets a transaction of its own.
    /// </summary>
    /// <param name="harness">The harness whose pool and registry to use.</param>
    /// <param name="database">The descriptor's database, which is what makes it diverge.</param>
    /// <returns>The registered session.</returns>
    /// <remarks>
    /// THE HARNESS'S OWN HELPER USES ONE FIXED DESCRIPTOR, which is right for the rows that only need a
    /// session, and wrong for any row that needs two sessions to differ in what they are holding: the pool
    /// is keyed by descriptor, so those rows would share one transaction and one marker. This mirrors
    /// BeginSession's sequence exactly - reference, borrow, connect, register - with the descriptor as the
    /// only variable.
    /// </remarks>
    private static TransactionSession RegisterSessionOn(LifecycleHarness harness, string database)
    {
        TransactionData descriptor = new() { Dbms = "SQLITE", Database = database };
        PoolLease lease = harness.Pool.AddRefLease(descriptor);

        _ = harness.Pool.Get(lease, out IPooledTransaction? borrowed);

        if (!borrowed!.IsConnected())
        {
            _ = borrowed.Connect(TestContext.Current.CancellationToken);
        }

        return Assert.IsType<TransactionSession>(
            harness.Registry.Register(lease, descriptor, borrowed, out _));
    }

    /// <summary>Which release path a theory row exercises.</summary>
    public enum ReleasePath
    {
        /// <summary>A commit, which ends the transaction and releases what it held.</summary>
        Commit,

        /// <summary>A rollback, which does the same by the other verb.</summary>
        Rollback,

        /// <summary>A disconnect, which ends any open transaction with the connection.</summary>
        Disconnect,

        /// <summary>A reconnect, which pre-emptively disconnects whatever was open first.</summary>
        Reconnect,

        /// <summary>Turning auto-commit on, which commits and releases the open transaction.</summary>
        AutoCommitOn,

        /// <summary>Turning auto-commit off, which opens a fresh transaction that has executed nothing.</summary>
        AutoCommitOff,
    }
}
