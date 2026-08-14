// ==================================================================================================
//  HandleLifecycleTests - THE BOUNDS ON SERVER-HELD WORK HANDLES
//  ------------------------------------------------------------------------------------------------
//  WHAT THESE ROWS EXIST TO CATCH
//
//  All four of this service's handle tables were unbounded `ConcurrentDictionary` singletons with no
//  ceiling, no idle expiry and no shutdown drain. Every entry pins real resources - a session pins a pool
//  reference and therefore an open connection, and a command or update task pins a worker task - so a
//  caller that crashed, timed out or forgot to release left all of it alive for the life of the PROCESS,
//  and nothing would ever notice.
//
//  Not one pre-existing row could see that, because a leak is invisible to every assertion about an
//  operation's own answer: the retrieval still streamed, the update still updated, and the handle was
//  still there afterwards. So these rows assert the four properties nothing else does.
//
//    1. THE CEILING REFUSES RATHER THAN EVICTS, and the refusal is atomic with the test - a
//       count-then-add pair admits one handle past the ceiling for every caller that races another.
//    2. THE REFUSAL COSTS NOTHING. A refused create returns the pool reference and disposes the task it
//       built, or the ceiling would leak the very resource it exists to protect.
//    3. AN ABANDONED HANDLE IS RECLAIMED AND TORN DOWN, on the same path a release takes - and a session
//       a live task still names is never reclaimed, because that would hand a transaction back underneath
//       a working task.
//    4. SHUTDOWN DRAINS, TASKS BEFORE SESSIONS, because a task borrows the transaction its session owns.
//
//  ============ WHERE EACH IS ASSERTED ============================================================
//  The quota is exercised directly, because its atomicity is a property of the type rather than of any
//  registry. The session and update registries are exercised end to end against a real transaction pool
//  and a real update surface, because their teardown is the part that has to be right. The reclaimer is
//  exercised over all four registries, because the pinning rule and the drain ORDER are properties of the
//  orchestration and of nothing smaller. Two service-level rows then prove the refusal reaches the wire as
//  E_BUSY, which is what DataServices projects onto HTTP 429.
// ==================================================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Options;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Data;
using PowerFramework.Persistence.Grpc;
using PowerFramework.Persistence.Runtime;
using PowerFramework.Persistence.Tasks;
using PowerFramework.Persistence.Transactions;
using TransactionService = PowerFramework.Persistence.Grpc.TransactionService;
using UpdateService = PowerFramework.Persistence.Grpc.UpdateService;

namespace PowerFramework.Persistence.Tests;

// =====================================================================================================
//  1. THE QUOTA - the ceiling itself
// =====================================================================================================
public sealed class HandleQuotaTests
{
    [Fact]
    public void APerCallerCeilingRefusesTheCallerThatReachedItAndNobodyElse()
    {
        HandleQuota quota = new("query task", maxTotal: 10, maxPerPrincipal: 2);

        Assert.True(quota.TryReserve("gateway", out _));
        Assert.True(quota.TryReserve("gateway", out _));

        // THE THIRD IS REFUSED, AND THE DIAGNOSTIC NAMES THE PER-CALLER KEY rather than the total: an
        // operator told "the service is full" when one caller is at its own ceiling would tune the wrong
        // number. The order of the two tests is therefore observable and is contract.
        Assert.False(quota.TryReserve("gateway", out string diagnostic));
        Assert.Contains("MaxPerPrincipal", diagnostic, StringComparison.Ordinal);
        Assert.Contains("gateway", diagnostic, StringComparison.Ordinal);

        // ANOTHER CALLER IS UNAFFECTED, which is the whole reason the per-caller bound exists.
        Assert.True(quota.TryReserve("other", out _));
        Assert.Equal(3, quota.Reserved);
    }

    [Fact]
    public void TheTotalCeilingRefusesEvenACallerWellBelowItsOwnShare()
    {
        HandleQuota quota = new("session", maxTotal: 2, maxPerPrincipal: 2);

        Assert.True(quota.TryReserve("a", out _));
        Assert.True(quota.TryReserve("b", out _));

        Assert.False(quota.TryReserve("c", out string diagnostic));
        Assert.Contains("MaxTotalPerRegistry", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void AReleaseFreesTheCallersShareAndTheTotalTogether()
    {
        HandleQuota quota = new("update task", maxTotal: 1, maxPerPrincipal: 1);

        Assert.True(quota.TryReserve("gateway", out _));
        Assert.False(quota.TryReserve("gateway", out _));

        quota.Release("gateway");

        Assert.Equal(0, quota.Reserved);
        Assert.True(quota.TryReserve("gateway", out _));
    }

    [Fact]
    public void AReleaseThatMatchesNoReservationChangesNothing()
    {
        // REACHED FROM TEARDOWN PATHS, INCLUDING THE RECLAIM PASS AND THE DRAIN, so it must not throw: a
        // throw there would abandon the rest of the pass. It must also not go NEGATIVE, or an unmatched
        // release would silently raise the effective ceiling.
        HandleQuota quota = new("command task", maxTotal: 1, maxPerPrincipal: 1);

        quota.Release("never-reserved");
        quota.Release(string.Empty);

        Assert.Equal(0, quota.Reserved);
        Assert.True(quota.TryReserve("gateway", out _));
        Assert.False(quota.TryReserve("gateway", out _));
    }

    [Fact]
    public async Task SixteenThreadsReleasedTogetherAdmitExactlyTheCeiling()
    {
        // THE ROW THAT WOULD HAVE CAUGHT A COUNT-THEN-ADD CEILING, and it is built to actually race rather
        // than to look like it races. A barrier releases every thread at the same instant and each thread is
        // its own long-running thread rather than a pool work item, so sixteen reservations genuinely
        // overlap; a plain parallel loop over a synchronous body runs on a handful of threads and
        // interleaves so rarely that a check-then-act pair passes it. Eight rounds, because one round of a
        // concurrency assertion is an anecdote.
        //
        // A ceiling that can be exceeded is not a ceiling, and this is the same defect class as the
        // retained-key capacity check elsewhere in this system: test, yield, then add.
        const int Ceiling = 4;
        const int Racers = 16;
        const int Rounds = 8;

        for (int round = 0; round < Rounds; round++)
        {
            HandleQuota quota = new("session", maxTotal: Ceiling, maxPerPrincipal: Ceiling);
            using Barrier gate = new(Racers);
            int admitted = 0;

            Task[] attempts = [.. Enumerable.Range(0, Racers).Select(_ => Task.Factory.StartNew(
                () =>
                {
                    gate.SignalAndWait(TestContext.Current.CancellationToken);

                    // `out string _` rather than `out _`: the enclosing lambda's parameter is also named
                    // `_`, so a bare discard would bind to that int instead of discarding a string.
                    if (quota.TryReserve("gateway", out string _))
                    {
                        _ = Interlocked.Increment(ref admitted);
                    }
                },
                TestContext.Current.CancellationToken,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default))];

            await Task.WhenAll(attempts);

            Assert.Equal(Ceiling, Volatile.Read(ref admitted));
            Assert.Equal(Ceiling, quota.Reserved);
        }
    }

    [Fact]
    public void ANonPositiveCeilingIsRefusedAtTheTypesOwnBoundary()
    {
        // The options validator already refuses these, but this type is constructible without options, so
        // the guarantee holds at its own boundary rather than depending on how it was reached.
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new HandleQuota("k", 0, 1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new HandleQuota("k", 1, 0));
        _ = Assert.Throws<ArgumentException>(() => new HandleQuota(" ", 1, 1));
    }
}

// =====================================================================================================
//  2. THE SESSION REGISTRY - against a real pool, so the teardown is the real teardown
// =====================================================================================================
public sealed class TransactionSessionLifecycleTests
{
    [Fact]
    public void TheCeilingRefusesAFurtherSessionAndTakesNoReservationForIt()
    {
        LifecycleHarness harness = new(maxTotal: 1, maxPerPrincipal: 1);

        Assert.NotNull(harness.RegisterSession());

        Assert.Null(harness.Registry.Register(
            lease: new PoolLease(1L),
            descriptor: new TransactionData { Dbms = "SQLITE" },
            transaction: harness.Borrow(),
            out string diagnostic));

        Assert.NotEmpty(diagnostic);
        Assert.Equal(1, harness.Registry.Count);
    }

    [Fact]
    public void ReleasingASessionFreesItsShareOfTheCeiling()
    {
        LifecycleHarness harness = new(maxTotal: 1, maxPerPrincipal: 1);

        TransactionSession first = harness.RegisterSession();

        Assert.True(harness.Registry.TryRemove(first.SessionId));
        _ = harness.Registry.Teardown(first);

        // THE RESERVATION WENT BACK WITH THE HANDLE. Without that, a service would refuse every session
        // after its first N for the life of the process even though nothing was held.
        Assert.NotNull(harness.RegisterSession());
    }

    [Fact]
    public void ASecondRemovalOfTheSameHandleAnswersFalseSoTeardownRunsOnce()
    {
        // IDEMPOTENT RELEASE, AND THE CONTRACT'S OWN READING OF IT: the removal is what decides, so two
        // concurrent releases produce exactly one teardown and the loser is answered E_INVALID_HANDLE
        // rather than a silent success. This row is what stops a double release from returning the same
        // pool reference twice.
        LifecycleHarness harness = new();

        TransactionSession session = harness.RegisterSession();

        Assert.True(harness.Registry.TryRemove(session.SessionId));
        Assert.False(harness.Registry.TryRemove(session.SessionId));
    }

    [Fact]
    public void AnIdleSessionIsReclaimedAndItsPoolReferenceIsReturned()
    {
        LifecycleHarness harness = new();

        _ = harness.RegisterSession();
        Assert.Equal(1, harness.Pool.UpperBound);

        harness.Clock.Advance(TimeSpan.FromSeconds(HandleLifecycleOptions.DefaultIdleExpirySeconds + 1));

        Assert.Equal(1, harness.Registry.ReclaimIdle(harness.Clock.GetUtcNow(), harness.Window, NothingPinned));
        Assert.Equal(0, harness.Registry.Count);

        // THE POOL ENTRY IS GONE, WHICH IS THE POINT OF THE WHOLE MECHANISM. Removing the table row while
        // leaving the reference taken would have converted an abandoned handle into a pinned connection -
        // exactly the leak, with a cleanup pass on top of it.
        Assert.Equal(0, harness.Pool.UpperBound);
        Assert.Equal(1, harness.Engine.DisconnectCalls);
    }

    [Fact]
    public void ASessionTouchedWithinTheWindowIsNotReclaimed()
    {
        LifecycleHarness harness = new();

        TransactionSession session = harness.RegisterSession();

        harness.Clock.Advance(TimeSpan.FromSeconds(HandleLifecycleOptions.DefaultIdleExpirySeconds - 1));

        // A resolve is what every call on this session does, and it refreshes the stamp.
        Assert.True(harness.Registry.TryResolve(
            new SessionHandle { SessionId = session.SessionId },
            out _));

        harness.Clock.Advance(TimeSpan.FromSeconds(HandleLifecycleOptions.DefaultIdleExpirySeconds - 1));

        Assert.Equal(0, harness.Registry.ReclaimIdle(harness.Clock.GetUtcNow(), harness.Window, NothingPinned));
        Assert.Equal(1, harness.Registry.Count);
    }

    [Fact]
    public void APinnedSessionSurvivesHoweverIdleItIs()
    {
        // A TASK WORKING AGAINST A SESSION IS USING THAT SESSION, and the fact that no C-08 call has named
        // it recently says nothing about whether it is abandoned. Reclaiming one would hand its transaction
        // back underneath the task.
        LifecycleHarness harness = new();

        TransactionSession session = harness.RegisterSession();

        harness.Clock.Advance(TimeSpan.FromDays(1));

        HashSet<string> pinned = [session.SessionId];

        Assert.Equal(0, harness.Registry.ReclaimIdle(harness.Clock.GetUtcNow(), harness.Window, pinned));
        Assert.Equal(1, harness.Registry.Count);

        // And once nothing names it, the very next pass takes it.
        Assert.Equal(1, harness.Registry.ReclaimIdle(harness.Clock.GetUtcNow(), harness.Window, NothingPinned));
    }

    [Fact]
    public void TheDrainReleasesEveryLiveSessionWhateverItsAge()
    {
        LifecycleHarness harness = new();

        _ = harness.RegisterSession();
        _ = harness.RegisterSession();

        Assert.Equal(2, harness.Registry.Count);
        Assert.Equal(2, harness.Registry.Drain());
        Assert.Equal(0, harness.Registry.Count);
        Assert.Equal(0, harness.Pool.UpperBound);
    }

    [Fact]
    public void TeardownRefusesALeaseThePoolNoLongerHolds()
    {
        // THE LEGACY HAZARD, AND THE HANDLE THAT REMOVES IT. The oracle keys a session on the pool's
        // ARRAY INDEX, and the pool COMPACTS that array on removal - so a live session's index could
        // fall out of range, or worse come to name a STRANGER'S transaction, because ANOTHER session
        // departed. The lease replaces the index with an id that is monotonic and never reissued, which
        // is what makes the second failure impossible rather than merely unlikely.
        //
        // The guard itself is still the oracle's, at three of its own entry points, and it still answers
        // E_OUT_OF_BOUND: a handle the pool does not hold is refused rather than acted on.
        LifecycleHarness harness = new();

        TransactionSession forged = Assert.IsType<TransactionSession>(harness.Registry.Register(
            lease: new PoolLease(harness.Pool.UpperBound + 7L),
            descriptor: new TransactionData { Dbms = "SQLITE" },
            transaction: harness.Borrow(),
            out _));

        Assert.Equal(RetCode.E_OUT_OF_BOUND, harness.Registry.Teardown(forged));
    }

    /// <summary>Nothing is pinned. Named so the argument reads as a statement rather than a literal.</summary>
    private static IReadOnlySet<string> NothingPinned => new HashSet<string>(StringComparer.Ordinal);
}

// =====================================================================================================
//  3. THE UPDATE REGISTRY - the teardown that disposes a worker task
// =====================================================================================================
public sealed class UpdateTaskLifecycleTests
{
    [Fact]
    public void TheCeilingRefusesAFurtherTaskAndNamesWhy()
    {
        UpdateTaskRegistry registry = Build(maxTotal: 1, maxPerPrincipal: 1, out _);

        Assert.NotNull(registry.Register("session-1", new StubUpdateSurface(), out _));
        Assert.Null(registry.Register("session-1", new StubUpdateSurface(), out string diagnostic));
        Assert.NotEmpty(diagnostic);
    }

    [Fact]
    public void AnIdleTaskIsReclaimedAndItsSurfaceIsDisposed()
    {
        UpdateTaskRegistry registry = Build(out LifecycleClock clock);
        StubUpdateSurface surface = new();

        _ = registry.Register("session-1", surface, out _);

        clock.Advance(TimeSpan.FromSeconds(HandleLifecycleOptions.DefaultIdleExpirySeconds + 1));

        Assert.Equal(1, registry.ReclaimIdle(clock.GetUtcNow(), Window));
        Assert.Equal(0, registry.Count);

        // DISPOSED, NOT MERELY FORGOTTEN. Removing the table row without disposing would leave the worker
        // task and its pool reference alive - the leak with a cleanup pass on top of it.
        Assert.True(surface.Disposed);
    }

    [Fact]
    public void AReclaimedTasksShareOfTheCeilingIsReturned()
    {
        UpdateTaskRegistry registry = Build(maxTotal: 1, maxPerPrincipal: 1, out LifecycleClock clock);

        _ = registry.Register("session-1", new StubUpdateSurface(), out _);

        clock.Advance(TimeSpan.FromSeconds(HandleLifecycleOptions.DefaultIdleExpirySeconds + 1));

        Assert.Equal(1, registry.ReclaimIdle(clock.GetUtcNow(), Window));
        Assert.NotNull(registry.Register("session-1", new StubUpdateSurface(), out _));
    }

    [Fact]
    public void ATaskTouchedWithinTheWindowIsNotReclaimed()
    {
        UpdateTaskRegistry registry = Build(out LifecycleClock clock);

        UpdateTaskEntry entry = Assert.IsType<UpdateTaskEntry>(
            registry.Register("session-1", new StubUpdateSurface(), out _));

        clock.Advance(TimeSpan.FromSeconds(HandleLifecycleOptions.DefaultIdleExpirySeconds - 1));

        Assert.True(registry.TryResolve(
            new TaskHandle { TaskId = entry.TaskId },
            out _));

        clock.Advance(TimeSpan.FromSeconds(HandleLifecycleOptions.DefaultIdleExpirySeconds - 1));

        Assert.Equal(0, registry.ReclaimIdle(clock.GetUtcNow(), Window));
    }

    [Fact]
    public void ASecondRemovalOfTheSameHandleAnswersFalse()
    {
        UpdateTaskRegistry registry = Build(out _);

        UpdateTaskEntry entry = Assert.IsType<UpdateTaskEntry>(
            registry.Register("session-1", new StubUpdateSurface(), out _));

        Assert.True(registry.TryRemove(entry.TaskId, out _));
        Assert.False(registry.TryRemove(entry.TaskId, out _));
    }

    [Fact]
    public void TheDrainDisposesEveryLiveTask()
    {
        UpdateTaskRegistry registry = Build(out _);
        StubUpdateSurface first = new();
        StubUpdateSurface second = new();

        _ = registry.Register("session-1", first, out _);
        _ = registry.Register("session-2", second, out _);

        Assert.Equal(2, registry.Drain());
        Assert.Equal(0, registry.Count);
        Assert.True(first.Disposed);
        Assert.True(second.Disposed);
    }

    [Fact]
    public void LiveSessionIdsNamesEverySessionATaskIsWorkingAgainst()
    {
        // WHAT THE SESSION REGISTRY PINS ON. A missing identity here would let the reclaimer take a session
        // out from under a working task.
        UpdateTaskRegistry registry = Build(out _);

        _ = registry.Register("session-1", new StubUpdateSurface(), out _);
        _ = registry.Register("session-2", new StubUpdateSurface(), out _);

        Assert.Equal(["session-1", "session-2"], registry.LiveSessionIds.Order(StringComparer.Ordinal));
    }

    /// <summary>The default idle window, as the shipped settings define it.</summary>
    private static TimeSpan Window =>
        TimeSpan.FromSeconds(HandleLifecycleOptions.DefaultIdleExpirySeconds);

    private static UpdateTaskRegistry Build(out LifecycleClock clock) =>
        Build(
            HandleLifecycleOptions.DefaultMaxTotalPerRegistry,
            HandleLifecycleOptions.DefaultMaxPerPrincipal,
            out clock);

    private static UpdateTaskRegistry Build(int maxTotal, int maxPerPrincipal, out LifecycleClock clock)
    {
        PersistenceOptions options = new();
        options.Handles.MaxTotalPerRegistry = maxTotal;
        options.Handles.MaxPerPrincipal = maxPerPrincipal;

        clock = new LifecycleClock();

        return new UpdateTaskRegistry(Options.Create(options), clock);
    }
}

// =====================================================================================================
//  4. THE RECLAIMER - the orchestration, where the pinning rule and the drain order live
// =====================================================================================================
public sealed class HandleReclaimerTests
{
    [Fact]
    public void OnePassReclaimsAnAbandonedTaskAndTheSessionItLeavesBehind()
    {
        // THE PINNED SET IS COMPUTED FROM WHAT SURVIVED THE TASK SWEEP, WHICH IS THE ORDER THAT MATTERS. A
        // task abandoned alongside its session is gone by the time the session pass runs, so nothing pins
        // that session and the pair is reclaimed together - one pass, not two. What the pinning rule
        // protects is a session whose task is STILL THERE, whether because the task was touched recently or
        // because it is provably streaming; that task remains in the table and pins its session however
        // idle the session looks.
        LifecycleHarness harness = new();

        TransactionSession session = harness.RegisterSession();
        StubUpdateSurface surface = new();
        _ = harness.Updates.Register(session.SessionId, surface, out _);

        harness.Clock.Advance(TimeSpan.FromSeconds(HandleLifecycleOptions.DefaultIdleExpirySeconds + 1));

        Assert.Equal(2, harness.Reclaimer.ReclaimOnce());

        // The task was torn down, not merely forgotten, and the session's pool reference went back with it.
        Assert.True(surface.Disposed);
        Assert.Equal(0, harness.Updates.Count);
        Assert.Equal(0, harness.Registry.Count);
        Assert.Equal(0, harness.Pool.UpperBound);

        // A second pass finds nothing, which is what makes the first one complete rather than partial.
        Assert.Equal(0, harness.Reclaimer.ReclaimOnce());
    }

    [Fact]
    public void ASessionWhoseTaskIsStillLiveSurvivesThePassHoweverIdleItLooks()
    {
        // THE PINNING RULE, ASSERTED THROUGH THE ORCHESTRATION RATHER THAN THE REGISTRY. The task is kept
        // young by a resolve - which is what every call naming it does - while the session is left
        // untouched past its window. Without the pin, the session's transaction would be handed back
        // underneath a task that is still working against it.
        LifecycleHarness harness = new();

        TransactionSession session = harness.RegisterSession();
        StubUpdateSurface surface = new();
        UpdateTaskEntry entry = Assert.IsType<UpdateTaskEntry>(
            harness.Updates.Register(session.SessionId, surface, out _));

        harness.Clock.Advance(TimeSpan.FromSeconds(HandleLifecycleOptions.DefaultIdleExpirySeconds - 1));
        Assert.True(harness.Updates.TryResolve(new TaskHandle { TaskId = entry.TaskId }, out _));
        harness.Clock.Advance(TimeSpan.FromSeconds(HandleLifecycleOptions.DefaultIdleExpirySeconds - 1));

        Assert.Equal(0, harness.Reclaimer.ReclaimOnce());
        Assert.False(surface.Disposed);
        Assert.Equal(1, harness.Registry.Count);
        Assert.Equal(1, harness.Updates.Count);
    }

    [Fact]
    public void APassWithNothingIdleReclaimsNothingFromAnyOfTheFourTables()
    {
        LifecycleHarness harness = new();

        TransactionSession session = harness.RegisterSession();
        _ = harness.Updates.Register(session.SessionId, new StubUpdateSurface(), out _);

        Assert.Equal(0, harness.Reclaimer.ReclaimOnce());
        Assert.Equal(1, harness.Registry.Count);
        Assert.Equal(1, harness.Updates.Count);
        Assert.Equal(0, harness.Queries.Count);
        Assert.Equal(0, harness.Commands.Count);
    }

    [Fact]
    public void TheDrainReleasesTheTaskBeforeTheSessionItBorrowedFrom()
    {
        // THE ORDER IS LOAD-BEARING AND IS OBSERVED HERE RATHER THAN ASSUMED. The stub records what the pool
        // looked like at the instant it was disposed: an upper bound of one means the session was STILL
        // LIVE, which is the only order in which a task can be torn down without holding a returned
        // reference. Reversing the two lines in DrainAll fails this row.
        LifecycleHarness harness = new();

        TransactionSession session = harness.RegisterSession();
        StubUpdateSurface surface = new(() => harness.Pool.UpperBound);
        _ = harness.Updates.Register(session.SessionId, surface, out _);

        Assert.Equal(2, harness.Reclaimer.DrainAll());

        Assert.True(surface.Disposed);
        Assert.Equal(1, surface.PoolUpperBoundAtDispose);
        Assert.Equal(0, harness.Pool.UpperBound);
        Assert.Equal(0, harness.Registry.Count);
        Assert.Equal(0, harness.Updates.Count);
    }

    [Fact]
    public void TheDrainOfAnEmptyServiceReleasesNothingAndSaysSo()
    {
        LifecycleHarness harness = new();

        Assert.Equal(0, harness.Reclaimer.DrainAll());
    }
}

// =====================================================================================================
//  5. THE REFUSAL AT THE WIRE - what a caller actually receives
// =====================================================================================================
public sealed class HandleCeilingWireTests
{
    [Fact]
    public async Task BeginSessionAnswersBusyAtTheCeilingAndReturnsThePoolReferenceItTook()
    {
        LifecycleHarness harness = new(maxTotal: 1, maxPerPrincipal: 1);

        BeginSessionResponse first =
            await harness.Transactions.BeginSession(BeginRequest(), harness.CallContext);

        Assert.Equal(WireOk, first.Status.RetCode);

        BeginSessionResponse refused =
            await harness.Transactions.BeginSession(BeginRequest(), harness.CallContext);

        // E_BUSY - the oracle's own "not now" - which DataServices projects onto ResourceExhausted and
        // therefore onto HTTP 429. A new code here would enter every v1 consumer's branch set.
        Assert.Equal(
            (WireRetCode)(int)RetCode.E_BUSY,
            refused.Status.RetCode);
        Assert.NotEmpty(refused.Status.ErrorText);
        Assert.Null(refused.Session);

        // THE REFERENCE THE REFUSED CALL TOOK WAS HANDED BACK. The pool keys on whole-descriptor equality,
        // so both calls addressed ONE entry: if the refusal had kept its reference the entry's count would
        // be two, and ending the first session would leave a connection open with no handle naming it.
        // Ending it here therefore has to empty the pool completely.
        Assert.True(harness.Registry.TryResolve(first.Session, out TransactionSession? live));
        Assert.True(harness.Registry.TryRemove(live!.SessionId));
        _ = harness.Registry.Teardown(live);

        Assert.Equal(0, harness.Pool.UpperBound);
    }

    [Fact]
    public async Task CreateUpdateTaskAnswersBusyAtTheCeilingAndDisposesTheSurfaceItBuilt()
    {
        LifecycleHarness harness = new(maxTotal: 1, maxPerPrincipal: 1);
        RecordingUpdateFactory factory = new();
        UpdateService service = new(factory, harness.Updates, new DataObjectDefinitionCatalogue());

        TransactionSession session = harness.RegisterSession();

        CreateUpdateTaskResponse first =
            await service.CreateUpdateTask(CreateUpdateRequest(session.SessionId), harness.CallContext);

        Assert.Equal(WireOk, first.Status.RetCode);

        CreateUpdateTaskResponse refused =
            await service.CreateUpdateTask(CreateUpdateRequest(session.SessionId), harness.CallContext);

        Assert.Equal(
            (WireRetCode)(int)RetCode.E_BUSY,
            refused.Status.RetCode);
        Assert.Null(refused.Task);

        // THE SURFACE THE REFUSED CALL BUILT WAS DISPOSED, on the same terms as every other refusal arm in
        // that method. Leaving it would pin its pool reference for the life of the process - the leak the
        // ceiling exists to bound, produced by the ceiling itself.
        Assert.Equal(2, factory.Created.Count);
        Assert.False(factory.Created[0].Disposed);
        Assert.True(factory.Created[1].Disposed);
    }

    private static BeginSessionRequest BeginRequest() => new()
    {
        Descriptor_ = new TransactionDescriptor
        {
            Dbms = "SQLITE",
        },
    };

    private static CreateUpdateTaskRequest CreateUpdateRequest(
        string sessionId) => new()
        {
            Session = new SessionHandle { SessionId = sessionId },
        };

    private static WireRetCode WireOk => WireRetCode.Ok;
}

// =====================================================================================================
//  6. THE DOUBLES - one harness, one clock, one engine, one surface
// =====================================================================================================

/// <summary>
/// A settable clock, so idle expiry is driven rather than waited for.
/// </summary>
/// <remarks>
/// THE ONE CLOCK THE WHOLE MECHANISM READS. Every stamp, every comparison and the sweep timer itself come
/// from the injected <see cref="TimeProvider"/>, which is what makes a fifteen-minute window assertable in
/// a millisecond and what keeps paired characterization captures comparable.
/// </remarks>
internal sealed class LifecycleClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Moves the clock forward.</summary>
    /// <param name="delta">How far.</param>
    internal void Advance(TimeSpan delta) => _now += delta;
}

/// <summary>
/// A transaction engine that connects, disconnects and does nothing else.
/// </summary>
/// <remarks>
/// The pool's teardown is what these rows are about, so the engine only has to record that it was
/// DISCONNECTED - which is what proves a reclaimed session's reference reached the last-reference path
/// rather than merely leaving the table.
/// </remarks>
internal sealed class LifecycleEngine : ITransactionEngine
{
    /// <summary>How many times the pool disconnected this engine.</summary>
    internal int DisconnectCalls { get; private set; }

    /// <inheritdoc/>
    public int DbHandle { get; private set; }

    /// <inheritdoc/>
    public string Dbms { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public bool AutoCommit { get; set; }

    /// <summary>Moves the auto-commit mode and answers success, because this double opens no transaction.</summary>
    /// <param name="autoCommit">The mode to put in force.</param>
    /// <returns>Always a succeeded state.</returns>
    /// <remarks>
    /// ROUTED THROUGH THE PROPERTY so whatever the property records still records. A double with no
    /// provider behind it has nothing the transition can fail on, which is the contract's own
    /// nothing-to-do case.
    /// </remarks>
    public SqlState TrySetAutoCommit(bool autoCommit)
    {
        AutoCommit = autoCommit;

        return SqlState.Succeeded();
    }

    /// <inheritdoc/>
    public void ApplyConnectionFields(in TransactionData descriptor) => Dbms = descriptor.Dbms;

    /// <inheritdoc/>
    public SqlState Connect(CancellationToken cancellationToken = default)
    {
        DbHandle = 1;

        return SqlState.Succeeded();
    }

    /// <inheritdoc/>
    public SqlState Disconnect()
    {
        DisconnectCalls++;
        DbHandle = 0;

        return SqlState.Succeeded();
    }

    /// <inheritdoc/>
    public SqlState Commit() => SqlState.Succeeded();

    /// <inheritdoc/>
    public SqlState Rollback() => SqlState.Succeeded();

    /// <inheritdoc/>
    public SqlState Execute(string sqlCommand, CancellationToken cancellationToken = default)
    {
        _ = sqlCommand;

        return SqlState.Succeeded(1);
    }

    /// <summary>The bound overload, forwarding to the text form this double already records.</summary>
    public SqlState Execute(in SqlCommandText command, CancellationToken cancellationToken = default) =>
        Execute(command.CanonicalText, cancellationToken);

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}

/// <summary>
/// An update surface that records only whether - and in what surrounding state - it was disposed.
/// </summary>
/// <param name="poolUpperBoundAtDispose">
/// Read at the instant of disposal, so a drain's ORDER is observable: a non-zero value means the session
/// this task borrowed from was still live when the task was torn down, which is the only correct order.
/// </param>
internal sealed class StubUpdateSurface(Func<int>? poolUpperBoundAtDispose = null) : IUpdateTaskSurface
{
    private readonly Func<int>? _poolUpperBound = poolUpperBoundAtDispose;

    /// <summary>Whether this surface was disposed.</summary>
    internal bool Disposed { get; private set; }

    /// <summary>What the pool's upper bound was when this surface was disposed.</summary>
    internal int PoolUpperBoundAtDispose { get; private set; } = -1;

    /// <inheritdoc/>
    public long Reset() => RetCode.OK;

    /// <inheritdoc/>
    /// <remarks>
    /// True, because these cases are about a handle's lifetime rather than about a prepare's admission: a
    /// surface that claimed no source would make every prepare in them refuse for an unrelated reason.
    /// </remarks>
    public bool HasUpdateSource => true;

    /// <inheritdoc/>
    /// <remarks>Empty, because these cases install no data object and assert nothing about one.</remarks>
    public string DataObject => string.Empty;

    /// <inheritdoc/>
    public long ResetUpdatableTables() => RetCode.OK;

    /// <inheritdoc/>
    public long SetMultiTableUpdate(bool multiTable)
    {
        _ = multiTable;

        return RetCode.OK;
    }

    /// <inheritdoc/>
    public long AddUpdatableTable(
        string name,
        IEnumerable<string> updatableColumns,
        IEnumerable<string> keyColumns,
        string identityColumn,
        long? updateWhere,
        bool? updateKeyInPlace) => RetCode.OK;

    /// <inheritdoc/>
    public long AddUpdatableTable(
        string name,
        IEnumerable<string> updatableColumns,
        IEnumerable<string> keyColumns,
        string identityColumn) => RetCode.OK;

    /// <inheritdoc/>
    public long SetDataObject(string dataObject) => RetCode.OK;

    /// <inheritdoc/>
    public long SetSqlSyntax(string sqlSyntax) => RetCode.OK;

    /// <inheritdoc/>
    public long SetUpdateData(CarrierState? updateData, long updateRows) => RetCode.OK;

    /// <inheritdoc/>
    public long SetAutoCommit(bool autoCommit) => RetCode.OK;

    /// <inheritdoc/>
    public UpdateRunResult Execute(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return new UpdateRunResult { Code = RetCode.OK };
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Disposed)
        {
            return;
        }

        Disposed = true;
        PoolUpperBoundAtDispose = _poolUpperBound?.Invoke() ?? -1;
    }
}

/// <summary>An update-task factory that hands out <see cref="StubUpdateSurface"/> instances and keeps them.</summary>
internal sealed class RecordingUpdateFactory : IUpdateTaskFactory
{
    /// <summary>Every surface this factory produced, in creation order.</summary>
    internal List<StubUpdateSurface> Created { get; } = [];

    /// <inheritdoc/>
    public long TryCreate(string sessionId, out IUpdateTaskSurface? task)
    {
        _ = sessionId;

        StubUpdateSurface surface = new();
        Created.Add(surface);
        task = surface;

        return RetCode.OK;
    }
}

/// <summary>A minimal call context. No principal, so every handle is attributed to one bucket.</summary>
internal sealed class LifecycleCallContext : ServerCallContext
{
    /// <inheritdoc/>
    protected override string MethodCore => "/persistence.v1.TransactionService/BeginSession";

    /// <inheritdoc/>
    protected override string HostCore => "localhost:5101";

    /// <inheritdoc/>
    protected override string PeerCore => "ipv4:127.0.0.1:0";

    /// <inheritdoc/>
    protected override DateTime DeadlineCore => DateTime.MaxValue;

    /// <inheritdoc/>
    protected override Metadata RequestHeadersCore { get; } = [];

    /// <inheritdoc/>
    protected override CancellationToken CancellationTokenCore => CancellationToken.None;

    /// <inheritdoc/>
    protected override Metadata ResponseTrailersCore { get; } = [];

    /// <inheritdoc/>
    protected override Status StatusCore { get; set; }

    /// <inheritdoc/>
    protected override WriteOptions? WriteOptionsCore { get; set; }

    /// <inheritdoc/>
    protected override AuthContext AuthContextCore { get; } =
        new("lifecycle", new Dictionary<string, List<AuthProperty>>(StringComparer.Ordinal));

    /// <inheritdoc/>
    protected override ContextPropagationToken CreatePropagationTokenCore(
        ContextPropagationOptions? options) => throw new NotSupportedException();

    /// <inheritdoc/>
    protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
}

/// <summary>
/// One real pool, one real session registry, the three real task registries and the real reclaimer over
/// them.
/// </summary>
/// <remarks>
/// EVERYTHING EXCEPT THE ENGINE AND THE UPDATE SURFACE IS THE SHIPPED TYPE. The pool's reference counting
/// and compaction, the registries' tables and quotas and the reclaimer's orchestration are all production
/// code here, because the properties under test - a returned reference, a disposed worker, a pinned
/// session, a drain order - are properties of exactly those.
/// </remarks>
internal sealed class LifecycleHarness
{
    /// <summary>Creates the harness with the shipped ceilings.</summary>
    internal LifecycleHarness()
        : this(
            HandleLifecycleOptions.DefaultMaxTotalPerRegistry,
            HandleLifecycleOptions.DefaultMaxPerPrincipal)
    {
    }

    /// <summary>Creates the harness with explicit ceilings.</summary>
    /// <param name="maxTotal">The total ceiling per registry.</param>
    /// <param name="maxPerPrincipal">The per-caller ceiling.</param>
    internal LifecycleHarness(int maxTotal, int maxPerPrincipal)
    {
        PersistenceOptions options = new();
        options.Handles.MaxTotalPerRegistry = maxTotal;
        options.Handles.MaxPerPrincipal = maxPerPrincipal;

        IOptions<PersistenceOptions> bound = Options.Create(options);

        Clock = new LifecycleClock();
        Engine = new LifecycleEngine();
        Pool = new TransactionPool(
            bound,
            Clock,
            new PooledTransactionActivator(() => Engine, Clock));

        Registry = new TransactionSessionRegistry(bound, Clock, Pool);
        Queries = new QueryTaskRegistry(bound, Clock);
        Updates = new UpdateTaskRegistry(bound, Clock);
        Commands = new CommandTaskRegistry(bound, Clock);

        Reclaimer = new HandleReclaimer(Registry, Queries, Updates, Commands, bound, Clock);
        // THE THREE TASK TABLES ARE SUPPLIED, because EndSession PURGES them: a task cannot outlive the
        // session it was created against. They are the harness's own real tables rather than doubles, so
        // a session ended through this adapter genuinely retires the tasks these rows created.
        Transactions = new TransactionService(
            Pool,
            Registry,
            new UnreachedQuerySurface(),
            Queries,
            Updates,
            Commands);
        Window = options.Handles.IdleExpiry;
    }

    /// <summary>The settable clock every stamp and comparison reads.</summary>
    internal LifecycleClock Clock { get; }

    /// <summary>The engine behind every pooled transaction.</summary>
    internal LifecycleEngine Engine { get; }

    /// <summary>The real reference-counted pool.</summary>
    internal TransactionPool Pool { get; }

    /// <summary>The real session registry.</summary>
    internal TransactionSessionRegistry Registry { get; }

    /// <summary>The real query-task registry. Empty in these rows.</summary>
    internal QueryTaskRegistry Queries { get; }

    /// <summary>The real update-task registry.</summary>
    internal UpdateTaskRegistry Updates { get; }

    /// <summary>The real command-task registry. Empty in these rows.</summary>
    internal CommandTaskRegistry Commands { get; }

    /// <summary>The real reclaimer over all four.</summary>
    internal HandleReclaimer Reclaimer { get; }

    /// <summary>The real C-08 adapter, for the wire-level rows.</summary>
    internal TransactionService Transactions { get; }

    /// <summary>The configured idle window.</summary>
    internal TimeSpan Window { get; }

    /// <summary>A call context carrying no principal.</summary>
    internal ServerCallContext CallContext { get; } = new LifecycleCallContext();

    /// <summary>Takes a pool reference and borrows its transaction, as BeginSession does.</summary>
    /// <returns>The borrowed transaction.</returns>
    internal IPooledTransaction Borrow()
    {
        PoolLease lease = Pool.AddRefLease(new TransactionData { Dbms = "SQLITE" });

        _ = Pool.Get(lease, out IPooledTransaction? borrowed);

        // CONNECTED, BECAUSE BeginSession CONNECTS. of_Release disconnects only on the last reference, so a
        // borrowed-but-never-connected transaction would have nothing to disconnect and a teardown
        // assertion would pass for the wrong reason.
        if (!borrowed!.IsConnected())
        {
            _ = borrowed.Connect();
        }

        return borrowed;
    }

    /// <summary>Takes a reference and registers a session over it, as BeginSession does.</summary>
    /// <returns>The registered session.</returns>
    internal TransactionSession RegisterSession()
    {
        TransactionData descriptor = new() { Dbms = "SQLITE" };
        PoolLease lease = Pool.AddRefLease(descriptor);

        _ = Pool.Get(lease, out IPooledTransaction? borrowed);

        if (!borrowed!.IsConnected())
        {
            _ = borrowed.Connect();
        }

        return Assert.IsType<TransactionSession>(
            Registry.Register(lease, descriptor, borrowed, out _));
    }
}

/// <summary>A query surface no row in this file reaches. Every member states that rather than answering.</summary>
/// <remarks>
/// C-08 CONSUMES ONLY ONE MEMBER OF THIS SEAM - the syntax derivation - and no row here reaches even that,
/// because nothing in this file retrieves anything. Answering a plausible value would let a row that
/// wandered into a retrieval path pass silently, which is the opposite of what a double is for.
/// </remarks>
internal sealed class UnreachedQuerySurface : IQueryTransactionSurface
{
    /// <inheritdoc/>
    public GridSyntaxOutcome GridSyntaxFromSql(IPooledTransaction transaction, string sql) =>
        throw new NotSupportedException(Reason);

    /// <inheritdoc/>
    public ValueTask<CountQueryOutcome> Query(
        IPooledTransaction transaction,
        string sql,
        CancellationToken cancellationToken) => throw new NotSupportedException(Reason);

    /// <inheritdoc/>
    public long RaiseBeforeRetrieve(IPooledTransaction transaction, DataWindowCarrier data) =>
        throw new NotSupportedException(Reason);

    /// <inheritdoc/>
    public void RaiseAfterRetrieve(
        IPooledTransaction transaction,
        DataWindowCarrier data,
        long rowCount) => throw new NotSupportedException(Reason);

    /// <summary>Why a call here is a test defect rather than a behaviour.</summary>
    private const string Reason =
        "No row in HandleLifecycleTests retrieves anything, so a call here means a row reached a code path "
        + "it was not written for. Answering a value instead would let that pass silently.";
}
