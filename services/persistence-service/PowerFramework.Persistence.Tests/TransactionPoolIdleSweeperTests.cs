// ==================================================================================================
//  TransactionPoolIdleSweeperTests.cs - THE SUITE THAT PROVES THE IDLE SWEEP IS ACTUALLY SCHEDULED
//  ------------------------------------------------------------------------------------------------
//  UNDER TEST     services/persistence-service/PowerFramework.Persistence/Transactions/
//                     TransactionPoolIdleSweeper.cs
//  ORACLE         ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru        (READ ONLY)
//
//  WHY THIS FILE EXISTS. The pool's collection logic was already covered - its expiry comparison, its
//  unconditional disconnect, its keep-alive gate - but NOTHING drove it. In the oracle the sweep is
//  driven by a timer the pool subscribes to inside its keep-alive branch [pool :L80], so a retained
//  transaction is eventually collected without anybody asking. A port that reproduces the collection
//  and drops the driver retains every idle connection for the life of the process, and every existing
//  assertion still passes because each one calls the collection directly. A covered mechanism with no
//  caller is exactly the shape of gap a unit suite cannot see, so the SCHEDULE is what this file
//  asserts, not the collection.
//
//  NO DATABASE, NO CONNECTION, NO NETWORK, NO CONTAINER AND NO REAL DELAY (C-H). The sweeper's timer is
//  created through the injected TimeProvider, so a tick is produced by ADVANCING A FAKE rather than by
//  waiting; a suite that slept for its interval would be a flake with a stopwatch. AAP 0.8.5 forbids
//  asserting any performance property and nothing here does: the assertions are that a tick happens and
//  that a failing sweep does not take the host down, never how quickly either occurs.
//
//  THE CLOCK IS HAND-ROLLED, AND IT OVERRIDES CreateTimer AS WELL AS THE TWO READINGS.
//  Microsoft.Extensions.TimeProvider.Testing - which supplies the framework's own FakeTimeProvider - is
//  NOT among the repository's central package versions, and adding a package the refactor's dependency
//  inventory does not list would be scope creep on a test file; TransactionPoolTests.cs settled the same
//  question the same way. The extra override matters more here than there: the base TimeProvider answers
//  CreateTimer with a REAL System.Threading.Timer, so a fake that overrode only the readings would leave
//  the PeriodicTimer running on wall time and every assertion below would become a race against it.
//
//  EVERY VALUE IN THIS FILE IS SYNTHETIC AND IS INVENTED HERE (C-F). No password, account name, host
//  name or connection string is copied from the legacy tree or from any catalogued secret site.
//
//  RULES POSITION. review_rules returns exactly one line, "No user rules provided.", so the
//  enterprise-standard baseline applies and the binding constraints are the plan's own non-rule
//  inventory - C-B, C-E, C-F and C-H bite here and are cited where each applies.
// ==================================================================================================

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Transactions;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// The suite for <c>TransactionPoolIdleSweeper</c> - the hosted service that drives the pool's idle
/// collection.
/// </summary>
public sealed class TransactionPoolIdleSweeperTests
{
    /// <summary>A distinctive non-zero instant, so no stamp collides with the zero sentinel.</summary>
    private static readonly DateTimeOffset ClockStart = new(2026, 5, 6, 7, 8, 9, TimeSpan.Zero);

    /// <summary>The synthetic descriptor. The pool matches by value and never reads a field.</summary>
    private static TransactionData Descriptor => new()
    {
        Dbms = "SQLITE",
        Database = "pfw-sweeper-parity",
    };

    // ==============================================================================================
    //  SUITE 1 - WHETHER A SWEEP IS SCHEDULED AT ALL
    // ==============================================================================================

    /// <summary>
    /// A positive configured interval schedules the sweep.
    /// </summary>
    [Fact]
    public void APositiveIntervalSchedulesTheSweep()
    {
        using Fixture fixture = new(idleSweepIntervalSeconds: 15d);

        Assert.True(fixture.Sweeper.IsScheduled);
    }

    /// <summary>
    /// A NON-POSITIVE interval disables the sweep rather than failing or falling back to a default, and
    /// the disabled loop performs no sweep at all however far the clock is advanced.
    /// </summary>
    /// <param name="intervalSeconds">The configured interval.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    public async Task ANonPositiveIntervalDisablesTheSweepEntirely(double intervalSeconds)
    {
        using Fixture fixture = new(idleSweepIntervalSeconds: intervalSeconds);

        Assert.False(fixture.Sweeper.IsScheduled);

        await fixture.Sweeper.StartAsync(TestContext.Current.CancellationToken);

        // A DISABLED LOOP CREATES NO TIMER AT ALL, so advancing the clock by an hour produces nothing.
        // The advance is what makes this an assertion rather than a coincidence of timing.
        fixture.Clock.Advance(TimeSpan.FromHours(1));

        await fixture.Sweeper.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, fixture.Clock.TimerCount);
        Assert.Equal(0, fixture.Sweeper.SweepCount);
    }

    // ==============================================================================================
    //  SUITE 2 - THE SCHEDULE ITSELF
    // ==============================================================================================

    /// <summary>
    /// THE FINDING ITSELF. Advancing the clock past the interval drives the pool's collection, so a
    /// retained transaction is collected and disconnected without anybody asking for it.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task AdvancingPastTheIntervalCollectsARetainedTransaction()
    {
        // Keep-alive ON with a one-second idle lifetime, which is the only configuration in which the
        // pool retains anything to collect [pool :L95-L99].
        using Fixture fixture = new(
            idleSweepIntervalSeconds: 15d,
            keepAlive: true,
            keepAliveExpireSeconds: 1d);

        // Take a reference, borrow the transaction so one exists, then release the reference: the entry is
        // RETAINED with an idle stamp rather than dropped.
        PoolLease lease = fixture.Pool.AddRefLease(Descriptor);
        Assert.Equal(RetCode.OK, fixture.Pool.Get(lease, out IPooledTransaction? borrowed));
        Assert.NotNull(borrowed);

        // CONNECTED DELIBERATELY. The pool never connects [pool :L154-L178] and a disconnect on an
        // unconnected transaction is a no-op, so without this the sweep's disconnect would be
        // unobservable and the assertion below would be vacuous.
        Assert.Equal(RetCode.OK, borrowed!.Connect(TestContext.Current.CancellationToken));

        Assert.Equal(RetCode.OK, fixture.Pool.RemoveRef(lease));
        Assert.True(fixture.Pool.Exists(Descriptor));

        await fixture.Sweeper.StartAsync(TestContext.Current.CancellationToken);
        await WaitForTimer(fixture);

        // Past the idle lifetime AND past the sweep interval, so the tick fires and the entry expires.
        fixture.Clock.Advance(TimeSpan.FromSeconds(20d));

        await WaitForSweeps(fixture, atLeast: 1);
        await fixture.Sweeper.StopAsync(TestContext.Current.CancellationToken);

        Assert.False(fixture.Pool.Exists(Descriptor));
        Assert.Equal(1, fixture.Engine.DisconnectCalls);
    }

    /// <summary>
    /// The loop is periodic rather than one-shot: each further interval produces a further sweep.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task TheSweepRepeatsOnEveryInterval()
    {
        using Fixture fixture = new(idleSweepIntervalSeconds: 5d);

        await fixture.Sweeper.StartAsync(TestContext.Current.CancellationToken);
        await WaitForTimer(fixture);

        fixture.Clock.Advance(TimeSpan.FromSeconds(5d));
        await WaitForSweeps(fixture, atLeast: 1);

        fixture.Clock.Advance(TimeSpan.FromSeconds(5d));
        await WaitForSweeps(fixture, atLeast: 2);

        await fixture.Sweeper.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Stopping the host ends the loop cleanly - the cancellation is swallowed rather than surfaced as a
    /// faulted background task - and no further sweep runs afterwards.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task StoppingEndsTheLoopCleanlyAndNoFurtherSweepRuns()
    {
        using Fixture fixture = new(idleSweepIntervalSeconds: 5d);

        await fixture.Sweeper.StartAsync(TestContext.Current.CancellationToken);
        await WaitForTimer(fixture);

        await fixture.Sweeper.StopAsync(TestContext.Current.CancellationToken);

        int afterStop = fixture.Sweeper.SweepCount;

        // The timer was disposed with the loop, so a further advance cannot produce a tick.
        Assert.Equal(0, fixture.Clock.TimerCount);

        fixture.Clock.Advance(TimeSpan.FromSeconds(50d));

        Assert.Equal(afterStop, fixture.Sweeper.SweepCount);

        // A CLEAN SHUTDOWN IS NOT A FAULT. The hosted service's own task completed rather than faulting,
        // which is what the cancellation filter in the loop exists to guarantee.
        Assert.NotNull(fixture.Sweeper.ExecuteTask);
        Assert.True(fixture.Sweeper.ExecuteTask!.IsCompleted);
        Assert.False(fixture.Sweeper.ExecuteTask!.IsFaulted);
    }

    // ==============================================================================================
    //  SUITE 3 - A FAILING SWEEP MUST NOT TAKE THE HOST DOWN
    // ==============================================================================================

    /// <summary>
    /// A sweep against a DISPOSED pool is a shutdown race, not a fault: it is swallowed silently and does
    /// not count as a completed sweep.
    /// </summary>
    [Fact]
    public void ASweepAgainstADisposedPoolIsSwallowed()
    {
        using Fixture fixture = new(idleSweepIntervalSeconds: 5d);

        fixture.Pool.Dispose();

        // No throw, and no recorded sweep - the call did not complete its work, so counting it would
        // overstate what happened.
        fixture.Sweeper.SweepOnce();

        Assert.Equal(0, fixture.Sweeper.SweepCount);
    }

    /// <summary>
    /// A healthy sweep records itself, which is what makes the swallowed case above distinguishable from
    /// a sweep that never ran at all.
    /// </summary>
    [Fact]
    public void AHealthySweepRecordsItself()
    {
        using Fixture fixture = new(idleSweepIntervalSeconds: 5d);

        fixture.Sweeper.SweepOnce();
        fixture.Sweeper.SweepOnce();

        Assert.Equal(2, fixture.Sweeper.SweepCount);
    }

    // ==============================================================================================
    //  HELPERS
    // ==============================================================================================

    /// <summary>
    /// Waits until the loop has created its timer, so an advance cannot be issued before there is
    /// anything to advance.
    /// </summary>
    /// <param name="fixture">The fixture under test.</param>
    /// <returns>A task that completes once the timer exists.</returns>
    /// <remarks>
    /// <para>
    /// <c>StartAsync</c> returns at the loop's first await, which is BEFORE the timer is constructed on
    /// some scheduling orders, so an advance issued immediately afterwards can find nothing to advance.
    /// </para>
    /// <para>
    /// A SIGNAL, NOT A POLL. This used to be a bounded retry - up to two hundred five-millisecond delays -
    /// which made the outcome a function of scheduler and host load: under a loaded agent the bound could
    /// expire while the loop was merely slow to reach its first await, and the test then failed for a
    /// reason that says nothing about the sweeper. The clock now completes a task the instant it registers
    /// its first timer, so this awaits the event itself. There is no interval, no attempt count and
    /// nothing to tune. The only bound is the test runner's own timeout, which is the correct owner of a
    /// liveness bound; no timing is asserted anywhere here (AAP 0.8.5).
    /// </para>
    /// </remarks>
    private static async Task WaitForTimer(Fixture fixture)
    {
        await fixture.Clock.FirstTimerCreated.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, fixture.Clock.TimerCount);
    }

    /// <summary>
    /// Waits until the sweeper has recorded at least <paramref name="atLeast"/> sweeps.
    /// </summary>
    /// <param name="fixture">The fixture under test.</param>
    /// <param name="atLeast">The sweep count to wait for.</param>
    /// <returns>A task that completes once the count is reached.</returns>
    /// <remarks>
    /// <para>
    /// WHY A WAIT IS NEEDED AT ALL. The tick is delivered SYNCHRONOUSLY inside
    /// <see cref="ManualTimeProvider.Advance"/> - the callback has already run by the time the advance
    /// returns - but the loop it releases resumes from <c>PeriodicTimer.WaitForNextTickAsync</c> on a
    /// continuation, so the sweep itself happens just after. What is being awaited is therefore a
    /// continuation the scheduler has ALREADY been handed, not an event that may or may not occur.
    /// </para>
    /// <para>
    /// TOKEN-DRIVEN AND UNBOUNDED, WHICH IS THE POINT. This used to be a bounded retry of up to two
    /// hundred five-millisecond delays, and a bound like that is a timing assumption wearing a
    /// convenience's clothes: it can expire because the agent is busy, and the failure then accuses the
    /// sweeper of not sweeping when the truth is that a thread was not scheduled within one second. The
    /// loop now has no attempt count and no delay - it yields until the count appears, and the ONLY thing
    /// that can end it early is the test's own cancellation token. A sweep that genuinely never happens
    /// is therefore reported by the runner's timeout, which is the separate liveness bound that belongs
    /// outside the assertion. Nothing here asserts a duration (AAP 0.8.5).
    /// </para>
    /// </remarks>
    private static async Task WaitForSweeps(Fixture fixture, int atLeast)
    {
        while (fixture.Sweeper.SweepCount < atLeast)
        {
            TestContext.Current.CancellationToken.ThrowIfCancellationRequested();

            // Hands the scheduler the continuation carrying the sweep. No duration, so nothing here can
            // expire; a run that never sweeps is ended by the runner rather than by this loop.
            await Task.Yield();
        }

        Assert.True(
            fixture.Sweeper.SweepCount >= atLeast,
            $"the sweeper recorded {fixture.Sweeper.SweepCount} sweeps, expected at least {atLeast}");
    }

    /// <summary>
    /// The pool, the sweeper, the engine double and the clock that drives all three.
    /// </summary>
    private sealed class Fixture : IDisposable
    {
        /// <summary>Builds the fixture.</summary>
        /// <param name="idleSweepIntervalSeconds">The configured sweep interval.</param>
        /// <param name="keepAlive">Whether released entries are retained.</param>
        /// <param name="keepAliveExpireSeconds">The idle lifetime of a retained entry.</param>
        internal Fixture(
            double idleSweepIntervalSeconds,
            bool keepAlive = false,
            double keepAliveExpireSeconds = 0d)
        {
            Clock = new ManualTimeProvider(ClockStart);
            Engine = new FakeEngine();

            PersistenceOptions options = new()
            {
                TransactionPool = new TransactionPoolOptions
                {
                    KeepAlive = keepAlive,
                    KeepAliveExpireSeconds = keepAliveExpireSeconds,
                    IdleSweepIntervalSeconds = idleSweepIntervalSeconds,
                },
            };

            Pool = new TransactionPool(
                Options.Create(options),
                Clock,
                new PooledTransactionActivator(() => Engine, Clock));

            Sweeper = new TransactionPoolIdleSweeper(
                Pool,
                Options.Create(options),
                Clock,
                NullLogger<TransactionPoolIdleSweeper>.Instance);
        }

        internal ManualTimeProvider Clock { get; }

        internal FakeEngine Engine { get; }

        internal TransactionPool Pool { get; }

        internal TransactionPoolIdleSweeper Sweeper { get; }

        /// <inheritdoc/>
        public void Dispose()
        {
            Sweeper.Dispose();
            Pool.Dispose();
        }
    }

    /// <summary>
    /// A hand-driven clock that also owns the timers created through it, so a tick is produced by
    /// advancing rather than by waiting.
    /// </summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly Lock _gate = new();

        private readonly List<ManualTimer> _timers = [];

        /// <summary>
        /// Completed the instant this clock registers its FIRST timer.
        /// </summary>
        /// <remarks>
        /// The signal that replaced a polling loop in <c>WaitForTimer</c>. Run continuations
        /// asynchronously, so completing the source cannot execute a waiter's continuation inline while
        /// this clock still holds its own lock - which would be a deadlock rather than a flake.
        /// </remarks>
        private readonly TaskCompletionSource _firstTimerCreated =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private DateTimeOffset _now;

        /// <summary>Positions the clock.</summary>
        /// <param name="start">The starting instant.</param>
        internal ManualTimeProvider(DateTimeOffset start) => _now = start;

        /// <summary>
        /// A task that completes when this clock has registered its first timer.
        /// </summary>
        /// <remarks>
        /// Exposed so a test can await the EVENT rather than poll for its consequence. It never
        /// transitions back: a clock registers a first timer once.
        /// </remarks>
        internal Task FirstTimerCreated => _firstTimerCreated.Task;

        /// <inheritdoc/>
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        /// <summary>The number of live timers, so a test can wait for one rather than assume it.</summary>
        internal int TimerCount
        {
            get
            {
                lock (_gate)
                {
                    return _timers.Count;
                }
            }
        }

        /// <inheritdoc/>
        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate)
            {
                return _now;
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Answered from the same field <see cref="GetUtcNow"/> reads, at tick resolution, so a component
        /// measuring MONOTONIC elapsed time is driven by this clock rather than escaping to
        /// <c>Stopwatch</c>.
        /// </remarks>
        public override long GetTimestamp()
        {
            lock (_gate)
            {
                return _now.UtcTicks;
            }
        }

        /// <inheritdoc/>
        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            ArgumentNullException.ThrowIfNull(callback);

            ManualTimer timer = new(this, callback, state);

            // ARMED AND REGISTERED UNDER ONE ACQUISITION, and that is not tidiness. Registering first and
            // arming afterwards leaves a window in which an Advance sees an unarmed timer, skips it, and
            // then the arming computes its first due instant from the ALREADY-ADVANCED clock - so the tick
            // the test just asked for is lost and the next one is a whole interval further away. That is a
            // test-only race but it produces a flake in the suite, not a diagnosis.
            lock (_gate)
            {
                _ = timer.ArmFrom(_now, dueTime, period);
                _timers.Add(timer);
            }

            // OUTSIDE THE LOCK, and after the timer is registered. Completing it inside would run a
            // waiter's continuation while this clock still held its gate on a source configured otherwise;
            // completing it before registration would let a waiter observe a clock with no timer.
            _ = _firstTimerCreated.TrySetResult();

            return timer;
        }

        /// <summary>
        /// Moves the clock forward and fires every timer that becomes due.
        /// </summary>
        /// <param name="delta">How far to move.</param>
        /// <remarks>
        /// The callbacks run OUTSIDE <see cref="_gate"/>, because a timer callback may create or dispose a
        /// timer and holding the gate across it would deadlock on the re-entry.
        /// </remarks>
        internal void Advance(TimeSpan delta)
        {
            DateTimeOffset target;
            ManualTimer[] snapshot;

            lock (_gate)
            {
                _now += delta;
                target = _now;
                snapshot = [.. _timers];
            }

            foreach (ManualTimer timer in snapshot)
            {
                timer.AdvanceTo(target);
            }
        }

        /// <summary>Forgets a disposed timer.</summary>
        /// <param name="timer">The timer.</param>
        internal void Forget(ManualTimer timer)
        {
            lock (_gate)
            {
                _ = _timers.Remove(timer);
            }
        }
    }

    /// <summary>
    /// A timer whose only source of time is <see cref="ManualTimeProvider.Advance"/>.
    /// </summary>
    private sealed class ManualTimer : ITimer
    {
        private readonly ManualTimeProvider _owner;

        private readonly TimerCallback _callback;

        private readonly object? _state;

        private readonly Lock _gate = new();

        private DateTimeOffset? _nextDue;

        private TimeSpan _period = Timeout.InfiniteTimeSpan;

        private bool _disposed;

        /// <summary>Creates the timer.</summary>
        /// <param name="owner">The clock that drives it.</param>
        /// <param name="callback">The callback to fire.</param>
        /// <param name="state">The callback's state.</param>
        internal ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
        }

        /// <inheritdoc/>
        public bool Change(TimeSpan dueTime, TimeSpan period) =>
            ArmFrom(_owner.GetUtcNow(), dueTime, period);

        /// <summary>
        /// Arms the timer relative to a CALLER-SUPPLIED instant.
        /// </summary>
        /// <param name="now">The instant to measure <paramref name="dueTime"/> from.</param>
        /// <param name="dueTime">When the first tick is due, or infinite to disarm.</param>
        /// <param name="period">The repeat interval, or infinite for one-shot.</param>
        /// <returns><see langword="true"/> unless the timer is already disposed.</returns>
        /// <remarks>
        /// Taking the instant as a parameter is what lets creation arm and register atomically: the
        /// provider already holds its own gate at that point and must not re-read its clock through a
        /// member that would take it again.
        /// </remarks>
        internal bool ArmFrom(DateTimeOffset now, TimeSpan dueTime, TimeSpan period)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return false;
                }

                _period = period;
                _nextDue = dueTime == Timeout.InfiniteTimeSpan ? null : now + dueTime;

                return true;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                _nextDue = null;
            }

            _owner.Forget(this);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            Dispose();

            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Fires the callback for every due instant at or before <paramref name="target"/>.
        /// </summary>
        /// <param name="target">The instant the clock has reached.</param>
        internal void AdvanceTo(DateTimeOffset target)
        {
            while (true)
            {
                lock (_gate)
                {
                    if (_disposed || _nextDue is not { } due || due > target)
                    {
                        return;
                    }

                    // A non-repeating timer is armed once; a repeating one is re-armed BEFORE the callback
                    // so a callback that disposes the timer still wins.
                    _nextDue = _period == Timeout.InfiniteTimeSpan || _period <= TimeSpan.Zero
                        ? null
                        : due + _period;
                }

                _callback(_state);
            }
        }
    }

    /// <summary>
    /// A database-verb engine that opens nothing (C-E). The sweep's observable effect is the disconnect,
    /// so that is the verb this double exists to record.
    /// </summary>
    private sealed class FakeEngine : ITransactionEngine
    {
        /// <summary>How many times the sweep disconnected this transaction.</summary>
        internal int DisconnectCalls { get; private set; }

        /// <inheritdoc/>
        public int DbHandle { get; private set; }

        /// <inheritdoc/>
        public string Dbms { get; private set; } = "SQLITE";

        /// <inheritdoc/>
        public bool AutoCommit { get; set; }

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

        /// <inheritdoc/>
        public SqlState Execute(in SqlCommandText command, CancellationToken cancellationToken = default) => Execute(command.RenderedText);

        /// <inheritdoc/>
        public void Dispose()
        {
        }
    }
}
