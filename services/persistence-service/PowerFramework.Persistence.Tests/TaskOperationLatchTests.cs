// ==================================================================================================
//  TaskOperationLatchTests.cs - the one mutual-exclusion state machine all three task contracts share
// ==================================================================================================
//
//  SUBJECT  Grpc/TaskOperationLatch.cs, which the query, update and command contracts all hold one of
//           per registered task handle. Its own header carries the disposal-handoff table; this suite
//           is that table asserted row by row, plus the ordering decision it makes deliberately and the
//           two properties that only a concurrent test can establish.
//
//  WHY IT IS TESTED ON ITS OWN RATHER THAN ONLY THROUGH THE THREE SERVICES. The latch is where the
//  atomicity lives, so every one of its interleavings is reachable here directly and cheaply, whereas
//  reaching the same interleavings through an RPC needs a paused operation and a second concurrent
//  request per case. The service suites assert that each RPC CONSULTS the latch; this suite asserts
//  what the latch ANSWERS. Neither substitutes for the other.
//
//  THE ORACLE'S OBSERVABLE ANSWERS ARE UNCHANGED (constraint C-B). A refused acquisition is still
//  E_BUSY at the boundary [n_cst_threading_task_sqlbase.sru:L57 and its eight siblings] and a released
//  handle is still E_INVALID_HANDLE. This type changes only that the answer and the action that follows
//  it are one indivisible step, which is what the in-process oracle got for free from being
//  single-threaded and what a request boundary takes away.
//
//  NO CLOCK, NO DATABASE, NO SERVICE HOST (constraints C-E, C-H). Two tests below start real threads,
//  deliberately: a lease whose mutual exclusion has never been contended has not been shown to exclude
//  anything. They are bounded by a count rather than by a timeout, so neither can hang.
// ==================================================================================================

using PowerFramework.Persistence.Grpc;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Tests for <c>TaskOperationLatch</c> and <c>TaskLatchOutcome</c>.
/// </summary>
public sealed class TaskOperationLatchTests
{
    // ---------------------------------------------------------------------------------------------
    //  1. THE THREE OUTCOMES
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void AFreshLatchIsNeitherRunningNorReleasedAndGrantsTheLease()
    {
        TaskOperationLatch latch = new();

        Assert.False(latch.IsRunning);
        Assert.False(latch.IsReleased);
        Assert.Equal(TaskLatchOutcome.Acquired, latch.TryBegin());
        Assert.True(latch.IsRunning);
    }

    [Fact]
    public void ASecondAcquisitionWhileOneIsInFlightIsBusyAndTheFirstStillOwnsIt()
    {
        // The caller-side `if of_IsBusy() then return RetCode.E_BUSY`, now indivisible with the mutation
        // it guards. BUSY is RETRYABLE advice: the same call unchanged succeeds once the first ends.
        TaskOperationLatch latch = new();

        Assert.Equal(TaskLatchOutcome.Acquired, latch.TryBegin());
        Assert.Equal(TaskLatchOutcome.Busy, latch.TryBegin());
        Assert.Equal(TaskLatchOutcome.Busy, latch.TryBegin());

        // The refusals changed nothing: the lease is still the first caller's and ending it once frees it.
        Assert.False(latch.End());
        Assert.Equal(TaskLatchOutcome.Acquired, latch.TryBegin());
    }

    [Fact]
    public void EveryAcquisitionAfterAReleaseIsGoneAndNotBusy()
    {
        // GONE is NOT retryable, and reporting it as BUSY would send a caller into a loop that can never
        // succeed - which is exactly why the outcome is three-valued rather than a boolean.
        TaskOperationLatch latch = new();

        Assert.True(latch.RequestRelease());
        Assert.True(latch.IsReleased);
        Assert.Equal(TaskLatchOutcome.Gone, latch.TryBegin());
        Assert.Equal(TaskLatchOutcome.Gone, latch.TryBegin());
    }

    [Fact]
    public void EveryAcquisitionAfterADisposalIsGone()
    {
        // The disposed state answers the same way as the released one, so a task torn down without a
        // release recorded - which nothing does today, but the state machine admits - cannot be reacquired.
        TaskOperationLatch latch = new();

        Assert.True(latch.TryClaimDisposal());
        Assert.Equal(TaskLatchOutcome.Gone, latch.TryBegin());

        // And it is NOT reported as released, because no release was requested. The two states are held
        // separately on purpose.
        Assert.False(latch.IsReleased);
    }

    [Fact]
    public void ATaskThatIsBothRunningAndReleasedAnswersGoneRatherThanBusy()
    {
        // THE ORDERING IS OBSERVABLE AND DELIBERATE: the GONE test precedes the BUSY test in TryBegin.
        // The handle is already unregistered by the time a release is recorded, so no retry can succeed,
        // and answering BUSY here would be advice that is not merely unhelpful but false.
        TaskOperationLatch latch = new();

        Assert.Equal(TaskLatchOutcome.Acquired, latch.TryBegin());
        Assert.False(latch.RequestRelease());

        Assert.True(latch.IsRunning);
        Assert.True(latch.IsReleased);
        Assert.Equal(TaskLatchOutcome.Gone, latch.TryBegin());
    }

    // ---------------------------------------------------------------------------------------------
    //  2. THE DISPOSAL-HANDOFF TABLE, ROW BY ROW
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ReleaseWithNothingRunningMakesDisposalTheReleasersDuty()
    {
        // Row 1 of the table in TaskOperationLatch.cs.
        TaskOperationLatch latch = new();

        Assert.True(latch.RequestRelease());
        Assert.True(latch.TryClaimDisposal());
    }

    [Fact]
    public void ReleaseDuringAnOperationHandsDisposalToTheOperationsExit()
    {
        // Row 2, and the one hazard-1 row: the releaser MUST NOT dispose, because a teardown with work
        // still pending against the object is the memory fault docs/PB多线程绕坑提示.md hazard 1 describes.
        TaskOperationLatch latch = new();

        Assert.Equal(TaskLatchOutcome.Acquired, latch.TryBegin());

        // The releaser is refused the duty.
        Assert.False(latch.RequestRelease());

        // The operation's exit takes it.
        Assert.True(latch.End());
        Assert.True(latch.TryClaimDisposal());
    }

    [Fact]
    public void ASecondReleaseNeverTakesTheDutyAgain()
    {
        // Row 3, in both of its shapes: after the first release has disposed, and after the duty has been
        // handed to an operation still in flight.
        TaskOperationLatch disposedAlready = new();

        Assert.True(disposedAlready.RequestRelease());
        Assert.True(disposedAlready.TryClaimDisposal());
        Assert.False(disposedAlready.RequestRelease());
        Assert.False(disposedAlready.TryClaimDisposal());

        TaskOperationLatch handedOff = new();

        Assert.Equal(TaskLatchOutcome.Acquired, handedOff.TryBegin());
        Assert.False(handedOff.RequestRelease());
        Assert.False(handedOff.RequestRelease());

        // Still exactly one duty, and it is still the operation's.
        Assert.True(handedOff.End());
    }

    [Fact]
    public void AnOperationThatEndsWithNoReleasePendingHasNoDuty()
    {
        // Row 4. The ordinary case, and the one that must NOT dispose - the task is still registered and
        // its next call will use it.
        TaskOperationLatch latch = new();

        Assert.Equal(TaskLatchOutcome.Acquired, latch.TryBegin());
        Assert.False(latch.End());
        Assert.False(latch.IsRunning);
    }

    [Fact]
    public void TheDutyIsGrantedExactlyOnceAcrossEveryOrderingOfTheThreeEvents()
    {
        // THE PROPERTY THE WHOLE TABLE EXISTS FOR, asserted as one invariant rather than four cases: for
        // every ordering of "an operation runs", "a release arrives" and "the operation ends", the number
        // of callers told the teardown is theirs is exactly one - never zero, which leaks the task, and
        // never two, which tears it down twice.
        //
        // Enumerated rather than sampled: three orderings is the whole space for one operation and one
        // release, since a release before an acquisition makes that acquisition GONE.
        Assert.Equal(1, CountDuties(static latch =>
        {
            // Release first, nothing in flight.
            bool releaser = latch.RequestRelease();
            return releaser ? 1 : 0;
        }));

        Assert.Equal(1, CountDuties(static latch =>
        {
            // Operation, then release, then end.
            _ = latch.TryBegin();
            int duties = latch.RequestRelease() ? 1 : 0;
            return duties + (latch.End() ? 1 : 0);
        }));

        Assert.Equal(1, CountDuties(static latch =>
        {
            // Operation, then end, then release.
            _ = latch.TryBegin();
            int duties = latch.End() ? 1 : 0;
            return duties + (latch.RequestRelease() ? 1 : 0);
        }));

        static int CountDuties(Func<TaskOperationLatch, int> ordering)
        {
            TaskOperationLatch latch = new();
            int duties = ordering(latch);

            // Whoever was told the duty was theirs must be able to claim the teardown, and nobody else.
            if (duties == 1)
            {
                Assert.True(latch.TryClaimDisposal());
            }

            Assert.False(latch.TryClaimDisposal());

            return duties;
        }
    }

    // ---------------------------------------------------------------------------------------------
    //  3. THE TWO PROPERTIES ONLY CONTENTION CAN ESTABLISH
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void UnderContentionExactlyOneAcquisitionSucceedsPerCompletedOperation()
    {
        // A LEASE THAT HAS NEVER BEEN CONTENDED HAS NOT BEEN SHOWN TO EXCLUDE ANYTHING. Eight threads
        // race the same latch; each records whether it got in. The invariant is that the number of
        // acquisitions equals the number of ends, and that no two acquisitions overlapped - which is
        // measured by a plain counter that only a lease-holder touches.
        const int Threads = 8;
        const int RoundsPerThread = 500;

        TaskOperationLatch latch = new();
        int concurrent = 0;
        int overlaps = 0;
        int acquisitions = 0;

        Thread[] workers = new Thread[Threads];

        for (int index = 0; index < Threads; index++)
        {
            workers[index] = new Thread(() =>
            {
                for (int round = 0; round < RoundsPerThread; round++)
                {
                    if (latch.TryBegin() != TaskLatchOutcome.Acquired)
                    {
                        continue;
                    }

                    _ = Interlocked.Increment(ref acquisitions);

                    // Inside the lease. A second holder here would take the counter above one, which is
                    // the only thing a mutual-exclusion failure could look like.
                    if (Interlocked.Increment(ref concurrent) != 1)
                    {
                        _ = Interlocked.Increment(ref overlaps);
                    }

                    _ = Interlocked.Decrement(ref concurrent);

                    Assert.False(latch.End());
                }
            });
        }

        foreach (Thread worker in workers)
        {
            worker.Start();
        }

        foreach (Thread worker in workers)
        {
            worker.Join();
        }

        Assert.Equal(0, overlaps);

        // Every thread that was refused was refused because another held the lease, so at least one round
        // per thread must have got in - otherwise the test proved nothing about the granting side.
        Assert.True(acquisitions >= Threads, $"only {acquisitions} acquisitions were granted");

        // And the latch is left clean, so the loop's ends balanced its begins.
        Assert.False(latch.IsRunning);
        Assert.Equal(TaskLatchOutcome.Acquired, latch.TryBegin());
    }

    [Fact]
    public void UnderContentionTheTeardownDutyIsStillGrantedExactlyOnce()
    {
        // The disposal handoff, raced. Many rounds, each with one operation and one release arriving from
        // a different thread at an unpredictable point relative to it: the duty must be granted exactly
        // once in every round regardless of how the two interleave.
        // Enough rounds that the two threads' start-up jitter lands the release on both sides of the
        // acquisition many times over, and few enough that the suite stays fast: the race window is at the
        // very start of each round, so extra rounds buy less than they cost.
        const int Rounds = 750;

        for (int round = 0; round < Rounds; round++)
        {
            TaskOperationLatch latch = new();
            int duties = 0;

            Thread releaser = new(() =>
            {
                if (latch.RequestRelease())
                {
                    _ = Interlocked.Increment(ref duties);
                }
            });

            Thread operation = new(() =>
            {
                if (latch.TryBegin() != TaskLatchOutcome.Acquired)
                {
                    // The release won the race, so the operation never ran and owes nothing.
                    return;
                }

                if (latch.End())
                {
                    _ = Interlocked.Increment(ref duties);
                }
            });

            operation.Start();
            releaser.Start();
            operation.Join();
            releaser.Join();

            Assert.Equal(1, Volatile.Read(ref duties));
            Assert.True(latch.TryClaimDisposal());
            Assert.False(latch.TryClaimDisposal());
        }
    }
}
