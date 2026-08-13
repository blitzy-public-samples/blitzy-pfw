// ==================================================================================================
//  TestDoubles.cs - THE DETERMINISM SEAMS AND SHARED SUBSTITUTES FOR THE PERSISTENCE SUITE
//  ------------------------------------------------------------------------------------------------
//  SUBSTITUTES FOR services/persistence-service/PowerFramework.Persistence/Program.cs
//                      :L318  services.TryAddSingleton(TimeProvider.System) - THE SINGLE clock seam
//                      :L814  services.TryAddSingleton<ISqlRedactor>(_ => SqlRedactor.Instance)
//                  services/persistence-service/PowerFramework.Persistence/Errors/SqlRedactor.cs
//                  services/persistence-service/PowerFramework.Persistence/Errors/DbErrorData.cs
//                  services/persistence-service/PowerFramework.Persistence/Configuration/
//                      PersistenceOptions.cs
//                  services/persistence-service/PowerFramework.Persistence/Transactions/
//                      TransactionData.cs, TransactionPool.cs
//                  services/persistence-service/PowerFramework.Persistence/Buffers/
//                      DataWindowBuffers.cs, ItemStatus.cs, ChangesetCodec.cs, FullStateCodec.cs
//                  shared/PowerFramework.Contracts/Proto/common.v1.proto  (DwBuffer, ItemStatus,
//                      CarrierState)
//
//  ORACLE         ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru        (READ ONLY)
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru             (READ ONLY)
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru   (READ ONLY)
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru(READ ONLY)
//                 ws_objects/pfw.utility.sqlite.pbl.src/sqlitegetitemdouble.srf        (READ ONLY)
//                 ws_objects/pfw.tests.pbl.src/n_test_thread_task.sru                  (READ ONLY)
//                 ws_objects/pfw.tests.pbl.src/n_test_threading_task.sru               (READ ONLY)
//
//  ORACLE STATUS (C-C). Every ws_objects/** path above is READ ONLY. They are the behavioural oracle
//  this migration is verified against and are never an edit target, never reformatted and never moved.
//  Nothing else in this repository can adjudicate the behaviour these doubles stand in for, so EVERY
//  claim below carries a line locator a reader can diff against.
//
//  ==================================================================================================
//  WHY THIS FILE EXISTS AT ALL
//  ==================================================================================================
//  The parity model makes determinism a HARD PREREQUISITE, not a nicety: a characterization comparison
//  is only valid when non-deterministic values are masked from BOTH the recorded master and the
//  candidate, so a suite that reads a real clock produces recordings that differ run to run and
//  therefore prove nothing. Three seams in this service are named sources of that non-determinism,
//  and all three are CLOCK READS of the same monotonic legacy source, CPU():
//
//    1. THE POOL'S IDLE EXPIRY.        `constant long KEEPALIVE_EXPIRE = 30000 //ms`
//                                      [n_cst_thread_trans_pool.sru:L53]; the idle instant is stamped
//                                      on release, `_transactions[refIndex].idleStartTime = CPU()`
//                                      [:L95-L97]; and collection compares
//                                      `CPU() - idleStartTime >= _nKeepAliveExpireTime or force`
//                                      [:L215] - a `>=` boundary WITH a force bypass.
//    2. THE LIVENESS CACHE.            `_nLastConnOK = CPU()` on a successful connect [:L107] and
//                                      `if CPU() - _nLastConnOK < 10000 then return true`
//                                      [n_cst_thread_trans.sru:L198]; a successful probe re-stamps and
//                                      a failed one ZEROES the stamp [:L212].
//    3. THE PROGRESS THROTTLE.         `if CPU() - _nUpdateNotifyTick > 100 or _nUpdateCurrent = 1 or
//                                      _nUpdateCurrent >= _nUpdateTotal then _nUpdateNotifyTick = CPU()`
//                                      [n_cst_thread_task_sqlbase_ds_mt.sru:L37-L39] - a STRICT `> 100`
//                                      threshold with TWO unconditional-fire escapes, first row and
//                                      last row.
//
//  All three are deltas between two reads of ONE monotonic counter, which is why ONE advanceable
//  provider covers all three and no second clock abstraction is introduced. The throttle is the
//  clearest illustration of why this matters: the threshold decides HOW MANY notifications a given
//  update emits, so it is directly observable in the notification stream, and a real clock would make
//  that count vary between the master and the candidate.
//
//  The fourth seam is ORDERING rather than time. Several behaviours are contracts about the ORDER in
//  which describe and modify calls are issued - the crosstab no-user-prompt guard must be written
//  BEFORE the transaction is attached and before the SQL is modified, and the update-prepare reset
//  lines must precede the per-column enables - and an unordered spy cannot express "before". That is
//  what ScriptedCarrierSurface's single ordered call log is for.
//
//  ==================================================================================================
//  DECISION 1 - THE CLOCK DOUBLE IS HAND-ROLLED, AND THAT IS A BUILD-NOT-BUY DECISION (C-I, C-K)
//  ==================================================================================================
//  `Microsoft.Extensions.TimeProvider.Testing`, which supplies the framework's own fake, is ABSENT
//  from the repository-root Directory.Packages.props. Central package management is enabled with
//  CentralPackageVersionOverrideEnabled false, so a versionless reference to a package with no central
//  PackageVersion entry CANNOT RESTORE, and adding the entry would mean editing a repository-root file
//  from inside a single service's test folder. `System.TimeProvider` is a BCL type, so a hand-rolled
//  subclass needs no package at all: the substitute is free and the dependency is not. This is the
//  same build-not-buy reasoning the refactor plan applies to the SQL parser and to pinyin matching.
//
//  ==================================================================================================
//  DECISION 2 - THE CARRIER STAND-IN DRIVES THE REAL BUFFER STORE RATHER THAN REIMPLEMENTING IT (C-B)
//  ==================================================================================================
//  `Buffers/DataWindowBuffers.cs` ALREADY is an in-memory, database-free DataWindow-shaped carrier -
//  three buffers, per-row and per-column item status, current AND original value per cell, one-based
//  row numbers. Reimplementing that surface in a double would create a SECOND definition of the
//  behaviour under test, and the two would drift; worse, every assertion would then be made against
//  the double's opinion rather than the production carrier's. `FakeDataWindowCarrier` therefore DERIVES
//  from `DataWindowBufferStore` and adds only what a test needs and production does not expose:
//  seeding verbs, the two legacy-named transfer verbs, scriptable failure, an ordered record of value
//  reads, and the identity-value surface. `RowsMove`, `RowsDiscard`, `Reset`, `ResetUpdate`,
//  `GetNextModified` and `Processing` are INHERITED UNCHANGED and are deliberately not overridden.
//
//  ==================================================================================================
//  DECISION 3 - WHAT THESE DOUBLES REFUSE TO NORMALISE (C-B)
//  ==================================================================================================
//  A double that tidies up the surface it stands in for makes its suite pass against a fiction. Four
//  legacy shapes are reproduced here precisely because a helpful double would erase them:
//
//    (a) THE INVERTED Filter! ROW ORDER. The identity round trip walks Primary! FORWARD and Filter!
//        BACKWARD, `for nIndex = FilteredCount() to 1 step -1`, because the filter buffer's row order
//        is inverted relative to the source [n_cst_thread_task_sqlupdate.sru:L235-L238]. Risk R9 names
//        this the single most dangerous line in the migration for one-based to zero-based translation:
//        it LOOKS like a bug, it is not one, and "correcting" it yields wrong identity values that a
//        row-count assertion still passes. `FakeDataWindowCarrier` stores Filter! rows in EXACTLY the
//        order it is given them and never reorders, so a backward walk is observably different from a
//        forward one.
//    (b) THE ROW-VERSUS-COLUMN STATUS DISTINCTION. The legacy reads a ROW's status by asking for column
//        index 0 - `GetItemStatus(nRow,0,Primary!)` [:L160] - and a genuine per-COLUMN status two lines
//        away with a real index [:L162]. Column 0 is not a column; it means "the row itself".
//        `Buffers/ItemStatus.cs` refuses a bare 0 where a column number is required, and these doubles
//        expose the two readings through SEPARATE verbs that cannot be confused.
//    (c) CURRENT AND ORIGINAL VALUE PER CELL. `updatewhere=1` is "key and updateable columns" mode, so
//        the generated where clause carries the ORIGINAL value of every marked column, and the legacy
//        original read is the four-argument form with the `org` flag -
//        `ds.GetItemNumber(row,col,buff,org)` [sqlitegetitemdouble.srf]. A double storing one value per
//        cell could not express the concurrency payload at all, so the seeding verbs reproduce the
//        legacy retrieve-then-edit sequence that puts the two values in place.
//    (d) THE THREE DIFFERENT READINGS OF ONE SQLCode FIELD. Exactly `-1` for the defensive override
//        [n_cst_thread_task_sqlupdate.sru:L208], NON-ZERO EXCLUDING 100 for the transaction's own state
//        [n_cst_thread_trans.sru:L233-L235, :L275], and NEGATIVE for the failure predicate [:L337].
//        `ScriptedPooledTransaction` lets a test set the code and the predicate INDEPENDENTLY so the
//        three stay distinguishable instead of collapsing into one condition with three spellings.
//
//  ==================================================================================================
//  DECISION 4 - WHAT IS DELIBERATELY NOT HERE
//  ==================================================================================================
//  NO DATABASE, NO CONNECTION, NO CONTAINER, NO FILE (C-E). Nothing here opens a DbConnection, touches
//  Microsoft.Data.Sqlite, constructs an EF Core context, starts a testcontainer or references a SQL
//  Server or Oracle client. Nothing creates, deletes, probes or writes a filesystem path either -
//  which is also what keeps the paired-capture rule intact, since the `persistence-db` volume state
//  must survive a legacy-side and a target-side capture untouched for a workflow comparison to be
//  valid. The options builder RECORDS a data directory as a string and never goes near it.
//
//  NO KEY, NO CREDENTIAL, NO TOKEN (C-F, C-G). No password, private key, certificate, bearer token,
//  connection string or credential-shaped literal appears anywhere in this file, and there is no
//  `SigningCredentials`, no `SymmetricSecurityKey` and no token-minting helper: Security is the SOLE
//  issuer in this system and Persistence holds verification material only. The one URL-shaped constant
//  is an RFC 2606 `.invalid` authority that cannot resolve and carries no credential; it exists only
//  because `JwtOptions.Authority` and `JwtOptions.Audience` are `[Required(AllowEmptyStrings = false)]`
//  and a bare `new PersistenceOptions()` therefore fails validation. A suite needing an authenticated
//  principal substitutes the authentication handler in its own test host; the material for that is not
//  key bytes and does not belong here. The builder has NO password setter at all, which is a structural
//  refusal rather than a default.
//
//  NO TYPE FROM ANOTHER SERVICE (C-A) AND NOTHING FOR A DEFERRED ONE (C-D). Every substituted contract
//  belongs to PowerFramework.Persistence or to a shared project this service already references. No
//  Gateway, DataServices or Security type is named, and nothing here serves DesignSystem, Documents,
//  Integration or ScriptBridge.
//
//  NO TIMING ASSERTION (AAP 0.8.5). The repository publishes no latency budget, throughput target or
//  availability commitment, so no performance property may be asserted. These doubles exist to REMOVE
//  time from the suite, not to measure it: nothing here calls Thread.Sleep, DateTime.Now,
//  DateTime.UtcNow, Environment.TickCount or Stopwatch, and a real delay is never awaited. Every
//  interval in the suite is crossed by ADVANCING a fake.
//
//  ==================================================================================================
//  THE ORACLE'S OWN PRECEDENT FOR ALL OF THIS
//  ==================================================================================================
//  Test doubles that seed their own non-determinism are not an invention of this port. The legacy ships
//  the same proxy-pair shape it asks production to use: `n_test_threading_task.sru` is the caller-side
//  double whose entire body is `ongettaskclsname` returning the worker class name, and
//  `n_test_thread_task.sru` is the worker-side double that scripts `onprepare`, `onthreadstart`,
//  `onthreadstop` and `ondotask`. Decisively, before the worker double calls `Rand(...)` it calls
//  `Randomize(0)` - THE LEGACY SEEDS ITS OWN TEST RANDOMNESS DETERMINISTICALLY. Doing the same for the
//  clock is continuity with the oracle, not a new practice (C-K).
//
//  ==================================================================================================
//  BINDING CONSTRAINTS AT THIS SITE
//  NAMING (AAP 0.7.2). No SCREAMING_SNAKE identifier is declared here. The constant-spelling exemption
//  of AAP 0.4.5.3 is scoped by .editorconfig to the specific production files that carry legacy
//  constant catalogues, and no test file is inside that scope; legacy constant VALUES are still
//  reproduced exactly, only their C# spellings are conventional.
// ==================================================================================================

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Microsoft.Extensions.Options;

using PowerFramework.Persistence.Concurrency;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Transactions;

namespace PowerFramework.Persistence.Tests;

#region Seam 1 - the clock, the only one, advanceable and shared by all three legacy intervals

/// <summary>
/// The single controllable <see cref="TimeProvider"/> the persistence suite reads time through, so no
/// test depends on a real clock.
/// </summary>
/// <remarks>
/// <para>
/// <b>ONE PROVIDER, THREE LEGACY INTERVALS, AND THAT IS DELIBERATE.</b> The three time-dependent
/// behaviours in this service - the pool's 30,000 ms idle window
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L53</c>], the transaction's
/// 10,000 ms liveness cache
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L198</c>] and the worker carrier's
/// 100 ms progress throttle
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L37</c>] - are all deltas
/// between two reads of ONE monotonic legacy counter, <c>CPU()</c>. They are not three clocks and they
/// must not become three clock types: the service registers exactly one
/// <see cref="TimeProvider"/> [<c>Program.cs:L351</c>], so a suite with three fakes would be testing a
/// composition the host cannot produce.
/// </para>
/// <para>
/// <b>BOTH READINGS MOVE TOGETHER, WHICH IS WHY BOTH ARE OVERRIDDEN.</b> Wall-clock consumers read
/// <see cref="GetUtcNow"/> and elapsed-interval consumers read <see cref="GetTimestamp"/> through
/// <see cref="TimeProvider.GetElapsedTime(long, long)"/>. Overriding only one leaves the other on the
/// real system clock, which is the exact defect this seam exists to prevent, so
/// <see cref="GetTimestamp"/> is derived from the same field <see cref="GetUtcNow"/> returns and
/// <see cref="TimestampFrequency"/> is stated in ticks to match.
/// </para>
/// <para>
/// <b>TIMERS FIRE ON <see cref="Advance(TimeSpan)"/> AND NEVER ON A REAL DELAY.</b> The base class
/// creates a real system timer, so a consumer that schedules work - the pool's idle sweep does - would
/// reintroduce wall-clock dependence through the back door. <see cref="CreateTimer"/> is therefore
/// overridden to hand back a timer this provider drives itself.
/// </para>
/// <para>
/// <b>Thread-safe by construction.</b> Every field read and write is taken under one lock, because a
/// consumer under test may read the clock from a worker continuation while a test advances it.
/// Callbacks are invoked OUTSIDE the lock so a callback that reads the clock cannot deadlock.
/// </para>
/// <para>
/// <b>This type measures nothing (AAP 0.8.5).</b> It exists to remove time from the suite. No
/// assertion built on it may claim a latency, a throughput or any other performance property.
/// </para>
/// </remarks>
internal sealed class FakeTimeProvider : TimeProvider
{
    /// <summary>
    /// The instant an advanceable clock starts at when a test does not care which instant that is.
    /// </summary>
    /// <remarks>
    /// A FIXED, ARBITRARY, UTC INSTANT. It is deliberately not "now": a default of the real current
    /// time would make every recording differ from every other, which is precisely the property the
    /// parity model forbids. Any absolute instant does equally well because every behaviour these
    /// doubles serve compares two reads of the clock rather than reading a calendar.
    /// </remarks>
    internal static readonly DateTimeOffset DefaultStart = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The pool's default idle window - <c>constant long KEEPALIVE_EXPIRE = 30000 //ms</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L53</c>].
    /// </summary>
    /// <remarks>
    /// THE COMPARISON AT THE FAR END IS <c>&gt;=</c>, NOT <c>&gt;</c> -
    /// <c>CPU() - idleStartTime &gt;= _nKeepAliveExpireTime or force</c> [<c>:L215</c>] - so advancing
    /// by EXACTLY this window expires the entry, and the <c>force</c> arm bypasses the comparison
    /// entirely. A test that wants the non-expiring side must advance by strictly less.
    /// </remarks>
    internal static readonly TimeSpan PoolIdleWindow = TimeSpan.FromMilliseconds(30_000);

    /// <summary>
    /// The transaction's liveness-cache window -
    /// <c>if CPU() - _nLastConnOK &lt; 10000 then return true</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L198</c>].
    /// </summary>
    /// <remarks>
    /// THE COMPARISON IS <c>&lt;</c>, so advancing by EXACTLY this window leaves the cache and probes
    /// the connection, while advancing by strictly less answers from the cache without probing. The
    /// mirror image of <see cref="PoolIdleWindow"/>'s boundary, and the two must not be transposed.
    /// </remarks>
    internal static readonly TimeSpan LivenessCacheWindow = TimeSpan.FromMilliseconds(10_000);

    /// <summary>
    /// The worker carrier's progress-notification throttle -
    /// <c>if CPU() - _nUpdateNotifyTick &gt; 100 or _nUpdateCurrent = 1 or
    /// _nUpdateCurrent &gt;= _nUpdateTotal</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L37</c>].
    /// </summary>
    /// <remarks>
    /// THE COMPARISON IS STRICT <c>&gt;</c>, so advancing by exactly this window does NOT release the
    /// throttle - one further tick is required. The two escapes either side of it fire
    /// UNCONDITIONALLY, first row and last row, whatever the clock says, so a test that advances the
    /// clock and counts notifications must account for both.
    /// </remarks>
    internal static readonly TimeSpan ProgressThrottleWindow = TimeSpan.FromMilliseconds(100);

    private readonly Lock _gate = new();
    private readonly List<FakeTimeProviderTimer> _timers = [];
    private DateTimeOffset _start;
    private DateTimeOffset _now;
    private int _advanceCount;

    /// <summary>Creates a clock stopped at <see cref="DefaultStart"/>.</summary>
    internal FakeTimeProvider()
        : this(DefaultStart)
    {
    }

    /// <summary>Creates a clock stopped at a caller-chosen instant.</summary>
    /// <param name="start">The instant both readings begin at.</param>
    internal FakeTimeProvider(DateTimeOffset start)
    {
        _start = start;
        _now = start;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// STATED IN TICKS so that <see cref="GetTimestamp"/> can return <c>UtcTicks</c> directly and
    /// <see cref="TimeProvider.GetElapsedTime(long, long)"/> converts without rounding. A coarser
    /// frequency would quantise the 100 ms throttle boundary and make a strict <c>&gt;</c> comparison
    /// land in the wrong place.
    /// </remarks>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <summary>The instant this clock was started or last reset to.</summary>
    internal DateTimeOffset Start
    {
        get
        {
            lock (_gate)
            {
                return _start;
            }
        }
    }

    /// <summary>How far the clock has been advanced from <see cref="Start"/>.</summary>
    internal TimeSpan Elapsed
    {
        get
        {
            lock (_gate)
            {
                return _now - _start;
            }
        }
    }

    /// <summary>How many times <see cref="Advance(TimeSpan)"/> has been called.</summary>
    /// <remarks>
    /// Recorded so a test can assert that it did NOT need to advance the clock at all - the honest way
    /// to show that a behaviour is time-independent.
    /// </remarks>
    internal int AdvanceCount
    {
        get
        {
            lock (_gate)
            {
                return _advanceCount;
            }
        }
    }

    /// <summary>How many undisposed timers this clock is currently driving.</summary>
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
    public override long GetTimestamp()
    {
        lock (_gate)
        {
            return _now.UtcTicks;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// ARMED AND REGISTERED UNDER ONE ACQUISITION, and that is correctness rather than tidiness.
    /// Registering first and arming afterwards leaves a window in which an <see cref="Advance"/> sees
    /// an unarmed timer, skips it, and the arming then computes its first due instant from the
    /// ALREADY-ADVANCED clock - so the tick a test just asked for is silently lost and the next one is
    /// a whole period further away. That is a test-only race, but it yields a flake instead of a
    /// diagnosis.
    /// </remarks>
    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);

        FakeTimeProviderTimer timer = new(this, callback, state);

        lock (_gate)
        {
            _ = timer.ArmFrom(_now, dueTime, period);
            _timers.Add(timer);
        }

        return timer;
    }

    /// <summary>Moves both readings forward and fires every timer that becomes due.</summary>
    /// <param name="delta">How far to move. Zero is permitted; negative is not.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="delta"/> is negative.
    /// </exception>
    /// <remarks>
    /// A NEGATIVE ADVANCE IS REFUSED because the legacy source is <c>CPU()</c>, a MONOTONIC counter of
    /// elapsed milliseconds. Letting a test wind the clock backwards would permit an interval delta
    /// the legacy could never observe, and any behaviour derived from it would be fiction.
    /// </remarks>
    internal void Advance(TimeSpan delta)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delta, TimeSpan.Zero);

        DateTimeOffset target;
        FakeTimeProviderTimer[] snapshot;

        lock (_gate)
        {
            _now += delta;
            _advanceCount++;
            target = _now;
            snapshot = [.. _timers];
        }

        // OUTSIDE THE LOCK. A callback is free to read the clock, create another timer or dispose
        // itself, and every one of those re-enters this object.
        foreach (FakeTimeProviderTimer timer in snapshot)
        {
            timer.AdvanceTo(target);
        }
    }

    /// <summary>Moves both readings forward by a whole number of milliseconds.</summary>
    /// <param name="milliseconds">How many milliseconds to move. Zero is permitted.</param>
    /// <remarks>
    /// THE MILLISECOND IS THE LEGACY'S OWN UNIT - <c>CPU()</c> counts milliseconds and every threshold
    /// in the oracle is an integer millisecond count - so a test that reasons in the oracle's unit
    /// should not have to construct a <see cref="TimeSpan"/> to say so.
    /// </remarks>
    internal void AdvanceMilliseconds(long milliseconds)
    {
        Advance(TimeSpan.FromMilliseconds(milliseconds));
    }

    /// <summary>Restarts the clock at a new instant, re-arming every live timer from it.</summary>
    /// <param name="start">The instant both readings restart at.</param>
    /// <remarks>
    /// <para>
    /// THE START INSTANT IS SETTABLE, and this is how. It is a method rather than a property setter
    /// because it has a second, non-obvious effect that a property assignment would hide: every live
    /// timer is re-armed from the new instant using the due time and period it was last given.
    /// Without that, a reset would leave timers holding due instants from the abandoned timeline and
    /// they would either fire immediately or never.
    /// </para>
    /// <para>
    /// <see cref="AdvanceCount"/> is cleared too, because it counts advances since the start instant.
    /// </para>
    /// </remarks>
    internal void Reset(DateTimeOffset start)
    {
        FakeTimeProviderTimer[] snapshot;

        lock (_gate)
        {
            _start = start;
            _now = start;
            _advanceCount = 0;
            snapshot = [.. _timers];
        }

        foreach (FakeTimeProviderTimer timer in snapshot)
        {
            timer.RearmFrom(start);
        }
    }

    /// <summary>Re-arms one timer against the current instant, under this clock's lock.</summary>
    /// <param name="timer">The timer being changed.</param>
    /// <param name="dueTime">The new first-fire delay.</param>
    /// <param name="period">The new repeat interval.</param>
    /// <returns><see langword="true"/>, matching <see cref="ITimer.Change"/>'s contract.</returns>
    /// <remarks>
    /// ROUTED THROUGH THE CLOCK RATHER THAN READ FROM IT so that "what time is it" and "arm from that
    /// time" happen under one acquisition. A timer that read the clock and then armed itself could be
    /// advanced past its own due instant in between.
    /// </remarks>
    internal bool Rearm(FakeTimeProviderTimer timer, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(timer);

        lock (_gate)
        {
            return timer.ArmFrom(_now, dueTime, period);
        }
    }

    /// <summary>Stops driving a disposed timer.</summary>
    /// <param name="timer">The timer to forget.</param>
    internal void Forget(FakeTimeProviderTimer timer)
    {
        ArgumentNullException.ThrowIfNull(timer);

        lock (_gate)
        {
            _ = _timers.Remove(timer);
        }
    }
}

/// <summary>
/// A timer driven exclusively by <see cref="FakeTimeProvider.Advance(TimeSpan)"/> and never by a real
/// delay.
/// </summary>
/// <remarks>
/// <para>
/// <b>WHY THIS TYPE EXISTS.</b> <see cref="TimeProvider.CreateTimer"/>'s base implementation returns a
/// timer backed by the real system clock. A consumer that schedules recurring work through the
/// injected provider - the pool's idle sweep is exactly that - would therefore still depend on wall
/// time even though every direct clock read had been faked. A suite that waited for such a timer would
/// be a flake with a stopwatch.
/// </para>
/// <para>
/// <b>A zero or infinite period means one shot.</b> <see cref="Timeout.InfiniteTimeSpan"/> is negative
/// and a zero period would spin forever inside a single advance, so both are treated as
/// non-repeating - which is also what <see cref="System.Threading.Timer"/> means by an infinite
/// period.
/// </para>
/// </remarks>
internal sealed class FakeTimeProviderTimer : ITimer
{
    private readonly Lock _gate = new();
    private readonly FakeTimeProvider _owner;
    private readonly TimerCallback _callback;
    private readonly object? _state;
    private TimeSpan _dueTime = Timeout.InfiniteTimeSpan;
    private TimeSpan _period = Timeout.InfiniteTimeSpan;
    private DateTimeOffset? _nextDue;
    private int _fireCount;
    private bool _disposed;

    /// <summary>Creates an unarmed timer owned by one fake clock.</summary>
    /// <param name="owner">The clock that will drive this timer.</param>
    /// <param name="callback">The callback to invoke when the timer becomes due.</param>
    /// <param name="state">The state handed to <paramref name="callback"/>.</param>
    internal FakeTimeProviderTimer(FakeTimeProvider owner, TimerCallback callback, object? state)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(callback);

        _owner = owner;
        _callback = callback;
        _state = state;
    }

    /// <summary>How many times the callback has been invoked.</summary>
    internal int FireCount
    {
        get
        {
            lock (_gate)
            {
                return _fireCount;
            }
        }
    }

    /// <summary>Whether this timer is armed and will fire on a sufficient advance.</summary>
    internal bool IsArmed
    {
        get
        {
            lock (_gate)
            {
                return !_disposed && _nextDue is not null;
            }
        }
    }

    /// <summary>Whether this timer has been disposed.</summary>
    internal bool IsDisposed
    {
        get
        {
            lock (_gate)
            {
                return _disposed;
            }
        }
    }

    /// <inheritdoc/>
    public bool Change(TimeSpan dueTime, TimeSpan period)
    {
        return _owner.Rearm(this, dueTime, period);
    }

    /// <summary>Arms this timer relative to a stated instant.</summary>
    /// <param name="now">The instant the due time is measured from.</param>
    /// <param name="dueTime">The first-fire delay, or infinite to disarm.</param>
    /// <param name="period">The repeat interval, or infinite or zero for one shot.</param>
    /// <returns><see langword="true"/>, matching <see cref="ITimer.Change"/>'s contract.</returns>
    internal bool ArmFrom(DateTimeOffset now, TimeSpan dueTime, TimeSpan period)
    {
        lock (_gate)
        {
            _dueTime = dueTime;
            _period = period;
            _nextDue = dueTime < TimeSpan.Zero ? null : now + dueTime;
            return true;
        }
    }

    /// <summary>Re-arms this timer from a new start instant using its last-known schedule.</summary>
    /// <param name="now">The instant the recorded due time is re-measured from.</param>
    internal void RearmFrom(DateTimeOffset now)
    {
        lock (_gate)
        {
            _nextDue = _disposed || _dueTime < TimeSpan.Zero ? null : now + _dueTime;
        }
    }

    /// <summary>Fires this timer for every due instant at or before <paramref name="now"/>.</summary>
    /// <param name="now">The instant the owning clock has reached.</param>
    /// <remarks>
    /// THE LOOP MATTERS. One advance may cross several periods, and a periodic timer that fired once
    /// per advance would under-report - so a test asserting "three sweeps happened" would fail against
    /// a correct implementation. The callback is invoked with the lock RELEASED so it may re-enter this
    /// timer or its clock.
    /// </remarks>
    internal void AdvanceTo(DateTimeOffset now)
    {
        while (true)
        {
            TimerCallback callback;
            object? state;

            lock (_gate)
            {
                if (_disposed || _nextDue is not DateTimeOffset due || due > now)
                {
                    return;
                }

                // A ZERO OR INFINITE PERIOD IS ONE SHOT. Rearming on a zero period would spin here
                // until the process died, which is a hang rather than a test failure.
                _nextDue = _period > TimeSpan.Zero ? due + _period : null;
                _fireCount++;
                callback = _callback;
                state = _state;
            }

            callback(state);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

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
}

#endregion

#region Seam 2 - the redaction boundary, recorded so "it went through the redactor" is provable

/// <summary>
/// An <see cref="ISqlRedactor"/> that records every statement handed to it, in call order, and returns
/// an unmistakable masked value.
/// </summary>
/// <remarks>
/// <para>
/// <b>WHAT THIS DOUBLE PROVES, AND WHY A REAL REDACTOR CANNOT PROVE IT (C-F).</b> The legacy
/// <c>sqlSyntax</c> field carries the COMPLETE generated statement including every interpolated literal
/// value, and the legacy logger performs no redaction whatsoever. The port's obligation is therefore
/// not merely "the output looks masked" but "the statement REACHED a diagnostic only by passing through
/// the redaction seam". Asserting that against
/// <c>Errors/SqlRedactor.SqlRedactor</c> is circular: its output is masked whether or not the caller
/// routed through it, because a hand-written masked-looking literal is indistinguishable from a masked
/// result. This double breaks the circularity by making the CALL observable -
/// <see cref="Statements"/> is empty unless <see cref="Redact"/> actually ran - which is what lets a
/// suite show that <c>DbErrorDataExtensions.ToDbError</c> cannot be reached without one.
/// </para>
/// <para>
/// <b>THE MASKED VALUE IS DELIBERATELY NOT <c>SqlRedactor.DefaultPlaceholder</c>.</b> The production
/// placeholder is <c>&lt;redacted&gt;</c> and it appears inside otherwise real statement text. A double
/// that returned the same token would let a test pass against production output it never invoked, so
/// <see cref="MaskedMarker"/> is a value the production scanner can never emit.
/// </para>
/// <para>
/// <b>NULL AND EMPTY ARE RECORDED, NOT SWALLOWED.</b> Both return <see cref="string.Empty"/>, matching
/// the production contract and the legacy scanner's own <c>if nLen &lt;= 0 then return ""</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L111</c>] - but the CALL is
/// still appended, because "the caller routed an empty statement through the seam" and "the caller
/// bypassed the seam" are different facts and only the recording distinguishes them.
/// </para>
/// <para>
/// <b>Idempotent, like the contract requires.</b> <c>Redact(Redact(s))</c> equals <c>Redact(s)</c>:
/// re-presenting <see cref="MaskedMarker"/> returns it unchanged rather than masking a mask. A
/// custom <see cref="Mask"/> is the caller's responsibility to keep idempotent if its suite depends on
/// that.
/// </para>
/// <para>
/// <b>Thread-safe.</b> The diagnostic path is exercised from concurrent request handlers in the
/// service-level suites, so the recording list is guarded.
/// </para>
/// </remarks>
internal sealed class RecordingSqlRedactor : ISqlRedactor
{
    /// <summary>
    /// The value every masked statement is replaced with - chosen so it cannot be confused with
    /// production output.
    /// </summary>
    internal const string MaskedMarker = "<recording-redactor:masked>";

    private readonly Lock _gate = new();
    private readonly List<string> _statements = [];

    /// <summary>
    /// An optional replacement for the default masking, for a suite that needs a distinguishable
    /// result per input.
    /// </summary>
    /// <remarks>
    /// The delegate is handed the non-empty original statement and must return the value to publish.
    /// Leave it <see langword="null"/> to get <see cref="MaskedMarker"/>, which is what almost every
    /// assertion wants.
    /// </remarks>
    internal Func<string, string>? Mask { get; set; }

    /// <summary>Every statement presented to <see cref="Redact"/>, in call order.</summary>
    /// <remarks>
    /// A SNAPSHOT, not the live list, so an assertion cannot be invalidated by a later call and a
    /// concurrent caller cannot fault an enumeration in progress.
    /// </remarks>
    internal IReadOnlyList<string> Statements
    {
        get
        {
            lock (_gate)
            {
                return [.. _statements];
            }
        }
    }

    /// <summary>How many times <see cref="Redact"/> has been called.</summary>
    internal int CallCount
    {
        get
        {
            lock (_gate)
            {
                return _statements.Count;
            }
        }
    }

    /// <summary>
    /// The most recent statement presented, or <see langword="null"/> when the seam has never been
    /// reached.
    /// </summary>
    internal string? LastStatement
    {
        get
        {
            lock (_gate)
            {
                return _statements.Count == 0 ? null : _statements[^1];
            }
        }
    }

    /// <inheritdoc/>
    public string Redact([AllowNull] string statement)
    {
        string recorded = statement ?? string.Empty;

        lock (_gate)
        {
            _statements.Add(recorded);
        }

        if (recorded.Length == 0)
        {
            return string.Empty;
        }

        if (string.Equals(recorded, MaskedMarker, StringComparison.Ordinal))
        {
            return MaskedMarker;
        }

        return Mask is null ? MaskedMarker : Mask(recorded);
    }

    /// <summary>Whether a statement was presented to this seam at least once.</summary>
    /// <param name="statement">The exact statement text expected.</param>
    /// <returns><see langword="true"/> when the seam saw it.</returns>
    internal bool Observed(string statement)
    {
        lock (_gate)
        {
            return _statements.Contains(statement, StringComparer.Ordinal);
        }
    }

    /// <summary>The one-based position of a statement in call order.</summary>
    /// <param name="statement">The exact statement text expected.</param>
    /// <returns>The one-based call position, or <c>0</c> when the statement was never presented.</returns>
    /// <remarks>
    /// ONE-BASED WITH ZERO MEANING "NEVER", matching the legacy's own row and index conventions rather
    /// than a .NET <c>IndexOf</c> returning <c>-1</c>; the surrounding suite reasons in one-based
    /// positions throughout because the oracle does.
    /// </remarks>
    internal int OrdinalOf(string statement)
    {
        lock (_gate)
        {
            for (int index = 0; index < _statements.Count; index++)
            {
                if (string.Equals(_statements[index], statement, StringComparison.Ordinal))
                {
                    return index + 1;
                }
            }

            return 0;
        }
    }

    /// <summary>Forgets every recorded call, for a test that reuses one seam across phases.</summary>
    internal void Clear()
    {
        lock (_gate)
        {
            _statements.Clear();
        }
    }
}

#endregion

#region Seam 3 - the ordered describe and modify recorder, because several contracts are about ORDER

/// <summary>
/// Which describe or modify verb a recorded interaction was.
/// </summary>
/// <remarks>
/// The legacy has ONE object behind all of these - a datastore is at once the describe surface, the
/// modify surface, the sort and filter surface and the value source - so the port's four narrow
/// interfaces are a decomposition of a single legacy surface. Tagging each call with its verb is what
/// lets one ordered log carry all four without losing which contract a given call arrived through.
/// </remarks>
internal enum CarrierCallKind
{
    /// <summary><c>Describe</c> of an arbitrary property.</summary>
    Describe,

    /// <summary><c>Modify</c> of a property or of a multi-line modification script.</summary>
    Modify,

    /// <summary><c>SetSort</c>.</summary>
    SetSort,

    /// <summary><c>SetFilter</c>.</summary>
    SetFilter,

    /// <summary>A column-count describe.</summary>
    GetColumnCount,

    /// <summary>A <c>&lt;name&gt;.Id</c> column-identifier describe.</summary>
    GetColumnId,

    /// <summary>The update-table describe.</summary>
    DescribeUpdateTable,

    /// <summary>The key-in-place describe.</summary>
    DescribeUpdateKeyInPlace,

    /// <summary>A <c>#n.Identity</c> column-identity describe.</summary>
    DescribeColumnIdentity,

    /// <summary>A <c>#n.DBName</c> column-database-name describe.</summary>
    DescribeColumnDbName,
}

/// <summary>One describe or modify interaction, with its position in call order.</summary>
/// <param name="Ordinal">The one-based position of this call among all calls on the surface.</param>
/// <param name="Kind">Which verb was invoked.</param>
/// <param name="Argument">
/// The argument presented - the property name, the modification script, the sort or filter expression.
/// </param>
/// <param name="Result">
/// The value the double answered with, rendered as text so one log can carry both the string-returning
/// and the numeric verbs.
/// </param>
internal readonly record struct RecordedCarrierCall(
    int Ordinal,
    CarrierCallKind Kind,
    string Argument,
    string Result);

/// <summary>
/// The describe, modify, sort and filter surface a DataWindow-shaped consumer talks to, recording every
/// interaction in ONE ordered log and answering from a script.
/// </summary>
/// <remarks>
/// <para>
/// <b>ONE LOG ACROSS FOUR INTERFACES, WHICH IS THE ENTIRE POINT.</b> Several persistence behaviours are
/// contracts about ORDER rather than content, and an unordered spy - or four separate spies - cannot
/// express "before":
/// </para>
/// <list type="bullet">
/// <item>
/// The crosstab no-user-prompt guard must be written BEFORE the transaction is attached and before the
/// SQL is modified. <c>Buffers/FullStateCodec.ApplyNoUserPromptWorkaround</c> writes
/// <c>DataWindow.NoUserPrompt=yes</c>, and its whole purpose is to suppress a dialog that the LATER
/// operations would otherwise raise - so a port that issued it afterwards would still pass a
/// content-only assertion while reproducing none of the behaviour.
/// </item>
/// <item>
/// The update-prepare reset lines must precede the per-column enables. <c>_of_updateprepare</c> first
/// resets update, key and identity to off on EVERY column
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L104-L108</c>] and only then
/// selectively re-enables from the table descriptor. Reversed, the reset would erase the enables and
/// the generated statement would carry no updatable columns at all.
/// </item>
/// </list>
/// <para>
/// <b>THE FOUR INTERFACES ARE ONE OBJECT IN PRODUCTION TOO.</b>
/// <c>Tasks/SqlUpdateCarrier</c> implements <c>IUpdateTarget</c>, <c>IUpdateTargetMetadata</c>,
/// <c>IUpdateTargetModifier</c>, <c>IIdentityColumnMetadata</c> and <c>IIdentityValueSource</c> on a
/// single type, because the legacy has no split. Two of the members are shared outright -
/// <c>GetColumnCount()</c> is declared by both metadata interfaces and <c>Modify(string)</c> by both
/// the modifier and the full-state surface - so implementing them once here is faithful rather than a
/// shortcut.
/// </para>
/// <para>
/// <b>Describe answers are scripted, and the two legacy sentinels are honoured.</b> PowerBuilder's
/// <c>Describe</c> answers <c>?</c> when a property is not applicable and <c>!</c> when it cannot be
/// read [<c>Buffers/ChangesetCodec.ChangesetSourceDefinition</c>], so
/// <see cref="DescribeFallback"/> defaults to <see cref="DescribeUnknown"/> rather than to an empty
/// string: an unscripted property must look unreadable, not blank, or a suite would assert against a
/// state the legacy never produces.
/// </para>
/// <para>
/// <b>Modify reports failure the legacy way - by returning text.</b> <c>Modify</c> returns an ERROR
/// STRING with empty meaning success
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L145</c>], which is why
/// <see cref="ModifyError"/> is a string and not a boolean.
/// </para>
/// </remarks>
internal sealed class ScriptedCarrierSurface
    : IFullStateCarrierSurface, IUpdateTargetMetadata, IUpdateTargetModifier, IIdentityColumnMetadata
{
    /// <summary>
    /// PowerBuilder's "property is not applicable here" describe answer.
    /// </summary>
    internal const string DescribeUnknown = "?";

    /// <summary>
    /// PowerBuilder's "property could not be read" describe answer.
    /// </summary>
    internal const string DescribeError = "!";

    /// <summary>The <c>Modify</c> result that means success - the empty string.</summary>
    internal const string ModifySucceeded = "";

    private readonly Lock _gate = new();
    private readonly List<RecordedCarrierCall> _calls = [];

    /// <summary>Scripted answers for <see cref="Describe"/>, keyed by exact property name.</summary>
    internal Dictionary<string, string> DescribeAnswers { get; } = new(StringComparer.Ordinal);

    /// <summary>The answer <see cref="Describe"/> gives for an unscripted property.</summary>
    internal string DescribeFallback { get; set; } = DescribeUnknown;

    /// <summary>
    /// Per-script <see cref="Modify"/> errors, keyed by the exact modification text, for a suite that
    /// needs one write to fail while the others succeed.
    /// </summary>
    internal Dictionary<string, string> ModifyErrors { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// A blanket <see cref="Modify"/> error applied to every write that has no entry in
    /// <see cref="ModifyErrors"/>. Empty means success.
    /// </summary>
    internal string ModifyError { get; set; } = ModifySucceeded;

    /// <summary>
    /// What <see cref="SetSort"/> answers. Defaults to the datastore success value.
    /// </summary>
    /// <remarks>
    /// SCRIPT A NON-ONE VALUE TO REACH THE FAILURE ARM. <c>FullStateCodec.SynchronizeSortAndFilter</c>
    /// reports <c>SetSort: </c> only when this is not the success value, so the failure branch is
    /// otherwise unreachable without a real DataWindow to break.
    /// </remarks>
    internal long SetSortResult { get; set; } = DataWindowBufferStore.DataStoreSuccess;

    /// <summary>
    /// What <see cref="SetFilter"/> answers. Defaults to the datastore success value.
    /// </summary>
    /// <remarks>
    /// The mirror of <see cref="SetSortResult"/>; a non-one value is what reaches the
    /// <c>SetFilter: </c> failure arm.
    /// </remarks>
    internal long SetFilterResult { get; set; } = DataWindowBufferStore.DataStoreSuccess;

    /// <summary>How many columns <see cref="GetColumnCount"/> reports.</summary>
    internal int ColumnCount { get; set; }

    /// <summary>
    /// Scripted answers for <see cref="GetColumnId"/>, keyed by the exact
    /// <c>&lt;name&gt;.Id</c> property.
    /// </summary>
    internal Dictionary<string, int> ColumnIds { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// What <see cref="GetColumnId"/> answers for an unscripted property.
    /// </summary>
    /// <remarks>
    /// ZERO IS THE LEGACY'S FAILURE VALUE, not a valid identifier: <c>_of_updateprepare</c> fails with
    /// an internal error when a key-column name resolves to a NON-POSITIVE identifier
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L118-L122</c>]. Defaulting
    /// to zero therefore makes an unscripted column name drive the same failure the legacy drives.
    /// </remarks>
    internal int ColumnIdFallback { get; set; }

    /// <summary>
    /// Scripted answers for <see cref="DescribeColumnIdentity"/>, keyed by exact property name.
    /// </summary>
    internal Dictionary<string, string> ColumnIdentityAnswers { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Scripted answers for <see cref="DescribeColumnDbName"/>, keyed by exact property name.
    /// </summary>
    internal Dictionary<string, string> ColumnDbNameAnswers { get; } = new(StringComparer.Ordinal);

    /// <summary>What <see cref="DescribeUpdateTable"/> reports.</summary>
    internal string UpdateTableName { get; set; } = string.Empty;

    /// <summary>
    /// What <see cref="DescribeUpdateKeyInPlace"/> reports. Defaults to <c>no</c>, matching the only
    /// updatable DataWindow in the repository.
    /// </summary>
    /// <remarks>
    /// <c>updatekeyinplace=no</c> [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14</c>] is the
    /// setting that TRIGGERS the legacy's own documented self-assignment workaround, so it is the
    /// default here precisely because that path is exercised by the primary fixture rather than being a
    /// rare branch.
    /// </remarks>
    internal string UpdateKeyInPlaceAnswer { get; set; } = "no";

    /// <summary>Every interaction with this surface, in call order.</summary>
    internal IReadOnlyList<RecordedCarrierCall> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    /// <summary>Every <see cref="Modify"/> argument, in call order.</summary>
    internal IReadOnlyList<string> ModifyScripts
    {
        get
        {
            lock (_gate)
            {
                return
                [
                    .. _calls
                        .Where(static call => call.Kind == CarrierCallKind.Modify)
                        .Select(static call => call.Argument),
                ];
            }
        }
    }

    /// <summary>The one-based line index of the first line of a script containing a fragment.</summary>
    /// <param name="script">The multi-line modification script.</param>
    /// <param name="fragment">The text to look for.</param>
    /// <returns>The one-based line index, or <c>0</c> when no line contains the fragment.</returns>
    /// <remarks>
    /// INTRA-SCRIPT ORDER IS ALSO A CONTRACT. <c>Concurrency/UpdateWhereBuilder</c> assembles ONE
    /// newline-separated script whose reset lines must precede its per-column enables, so ordering
    /// inside a single <see cref="Modify"/> argument matters just as much as ordering between calls.
    /// The line separator is the builder's own <c>LineSeparator</c>.
    /// </remarks>
    internal static int LineOrdinalOf(string script, string fragment)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(fragment);

        string[] lines = script.Split(UpdateWhereBuilder.LineSeparator);

        for (int index = 0; index < lines.Length; index++)
        {
            if (lines[index].Contains(fragment, StringComparison.Ordinal))
            {
                return index + 1;
            }
        }

        return 0;
    }

    /// <summary>Whether one fragment appears on an earlier line of a script than another.</summary>
    /// <param name="script">The multi-line modification script.</param>
    /// <param name="firstFragment">The fragment expected to come first.</param>
    /// <param name="secondFragment">The fragment expected to come second.</param>
    /// <returns>
    /// <see langword="true"/> only when BOTH fragments are present and the first precedes the second.
    /// </returns>
    /// <remarks>
    /// ABSENCE IS NOT ORDER. A missing fragment answers <see langword="false"/> rather than vacuously
    /// true, so a suite cannot pass because the behaviour it asserts about never happened.
    /// </remarks>
    internal static bool LinePrecedes(string script, string firstFragment, string secondFragment)
    {
        int first = LineOrdinalOf(script, firstFragment);
        int second = LineOrdinalOf(script, secondFragment);

        return first > 0 && second > 0 && first < second;
    }

    /// <summary>The one-based call position of an exact verb-and-argument pair.</summary>
    /// <param name="kind">The verb expected.</param>
    /// <param name="argument">The exact argument expected.</param>
    /// <returns>The one-based call position, or <c>0</c> when the call never happened.</returns>
    internal int OrdinalOf(CarrierCallKind kind, string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);

        lock (_gate)
        {
            foreach (RecordedCarrierCall call in _calls)
            {
                if (call.Kind == kind && string.Equals(call.Argument, argument, StringComparison.Ordinal))
                {
                    return call.Ordinal;
                }
            }

            return 0;
        }
    }

    /// <summary>The one-based call position of the first call of a verb containing a fragment.</summary>
    /// <param name="kind">The verb expected.</param>
    /// <param name="fragment">Text the argument must contain.</param>
    /// <returns>The one-based call position, or <c>0</c> when no such call happened.</returns>
    /// <remarks>
    /// The fragment form exists for <see cref="CarrierCallKind.Modify"/>, whose argument is often a
    /// whole assembled script rather than one property assignment.
    /// </remarks>
    internal int FirstOrdinalContaining(CarrierCallKind kind, string fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);

        lock (_gate)
        {
            foreach (RecordedCarrierCall call in _calls)
            {
                if (call.Kind == kind && call.Argument.Contains(fragment, StringComparison.Ordinal))
                {
                    return call.Ordinal;
                }
            }

            return 0;
        }
    }

    /// <summary>Whether one interaction happened before another.</summary>
    /// <param name="firstKind">The verb expected first.</param>
    /// <param name="firstFragment">Text the first call's argument must contain.</param>
    /// <param name="secondKind">The verb expected second.</param>
    /// <param name="secondFragment">Text the second call's argument must contain.</param>
    /// <returns>
    /// <see langword="true"/> only when BOTH calls happened and the first precedes the second.
    /// </returns>
    /// <remarks>
    /// ABSENCE IS NOT ORDER, for the same reason as <see cref="LinePrecedes"/>: a suite asserting that
    /// the crosstab guard precedes the SQL modification must fail, not pass, when the guard was never
    /// written at all.
    /// </remarks>
    internal bool Precedes(
        CarrierCallKind firstKind,
        string firstFragment,
        CarrierCallKind secondKind,
        string secondFragment)
    {
        int first = FirstOrdinalContaining(firstKind, firstFragment);
        int second = FirstOrdinalContaining(secondKind, secondFragment);

        return first > 0 && second > 0 && first < second;
    }

    /// <summary>How many times a verb was invoked.</summary>
    /// <param name="kind">The verb to count.</param>
    /// <returns>The number of recorded calls of that verb.</returns>
    internal int CountOf(CarrierCallKind kind)
    {
        lock (_gate)
        {
            return _calls.Count(call => call.Kind == kind);
        }
    }

    /// <summary>Forgets every recorded interaction, for a test that reuses one surface across phases.</summary>
    internal void Clear()
    {
        lock (_gate)
        {
            _calls.Clear();
        }
    }

    /// <inheritdoc/>
    public string Describe(string property)
    {
        ArgumentNullException.ThrowIfNull(property);

        string answer = DescribeAnswers.TryGetValue(property, out string? scripted)
            ? scripted
            : DescribeFallback;

        return Record(CarrierCallKind.Describe, property, answer);
    }

    /// <inheritdoc/>
    public string Modify(string modifyString)
    {
        ArgumentNullException.ThrowIfNull(modifyString);

        string error = ModifyErrors.TryGetValue(modifyString, out string? scripted)
            ? scripted
            : ModifyError;

        return Record(CarrierCallKind.Modify, modifyString, error);
    }

    /// <inheritdoc/>
    public long SetSort(string sort)
    {
        ArgumentNullException.ThrowIfNull(sort);

        _ = Record(
            CarrierCallKind.SetSort,
            sort,
            SetSortResult.ToString(CultureInfo.InvariantCulture));

        return SetSortResult;
    }

    /// <inheritdoc/>
    public long SetFilter(string filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        _ = Record(
            CarrierCallKind.SetFilter,
            filter,
            SetFilterResult.ToString(CultureInfo.InvariantCulture));

        return SetFilterResult;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// ONE IMPLEMENTATION SATISFIES BOTH METADATA INTERFACES, because
    /// <c>IUpdateTargetMetadata</c> and <c>IIdentityColumnMetadata</c> declare this member
    /// identically - which is itself evidence that the two are a decomposition of one legacy surface.
    /// </remarks>
    public int GetColumnCount()
    {
        _ = Record(
            CarrierCallKind.GetColumnCount,
            UpdateWhereBuilder.ColumnCountProperty,
            ColumnCount.ToString(CultureInfo.InvariantCulture));

        return ColumnCount;
    }

    /// <inheritdoc/>
    public int GetColumnId(string columnIdProperty)
    {
        ArgumentNullException.ThrowIfNull(columnIdProperty);

        int answer = ColumnIds.TryGetValue(columnIdProperty, out int scripted)
            ? scripted
            : ColumnIdFallback;

        _ = Record(
            CarrierCallKind.GetColumnId,
            columnIdProperty,
            answer.ToString(CultureInfo.InvariantCulture));

        return answer;
    }

    /// <inheritdoc/>
    public string DescribeUpdateKeyInPlace()
    {
        return Record(
            CarrierCallKind.DescribeUpdateKeyInPlace,
            UpdateWhereBuilder.UpdateKeyInPlaceProperty,
            UpdateKeyInPlaceAnswer);
    }

    /// <inheritdoc/>
    public string DescribeUpdateTable()
    {
        return Record(
            CarrierCallKind.DescribeUpdateTable,
            UpdateWhereBuilder.UpdateTableProperty,
            UpdateTableName);
    }

    /// <inheritdoc/>
    public string DescribeColumnIdentity(string identityProperty)
    {
        ArgumentNullException.ThrowIfNull(identityProperty);

        string answer = ColumnIdentityAnswers.TryGetValue(identityProperty, out string? scripted)
            ? scripted
            : string.Empty;

        return Record(CarrierCallKind.DescribeColumnIdentity, identityProperty, answer);
    }

    /// <inheritdoc/>
    public string DescribeColumnDbName(string dbNameProperty)
    {
        ArgumentNullException.ThrowIfNull(dbNameProperty);

        string answer = ColumnDbNameAnswers.TryGetValue(dbNameProperty, out string? scripted)
            ? scripted
            : string.Empty;

        return Record(CarrierCallKind.DescribeColumnDbName, dbNameProperty, answer);
    }

    private string Record(CarrierCallKind kind, string argument, string result)
    {
        lock (_gate)
        {
            _calls.Add(new RecordedCarrierCall(_calls.Count + 1, kind, argument, result));
        }

        return result;
    }
}

#endregion

#region Seam 4 - the pooled transaction, whose three readings of one SQLCode must stay distinguishable

/// <summary>Which verb a recorded transaction interaction was.</summary>
internal enum TransactionCallKind
{
    /// <summary><c>Connect</c>.</summary>
    Connect,

    /// <summary><c>Disconnect</c>.</summary>
    Disconnect,

    /// <summary><c>Commit</c>.</summary>
    Commit,

    /// <summary><c>Rollback</c>.</summary>
    Rollback,

    /// <summary><c>Exec</c>.</summary>
    Exec,

    /// <summary><c>AutoCommitCheckpoint</c>.</summary>
    AutoCommitCheckpoint,

    /// <summary><c>ClearState</c>.</summary>
    ClearState,

    /// <summary><c>StampSqlState</c>.</summary>
    StampSqlState,

    /// <summary><c>ApplyTransactionData</c>.</summary>
    ApplyTransactionData,

    /// <summary><c>SetBroken</c>.</summary>
    SetBroken,

    /// <summary>The vetoable before-update hook.</summary>
    BeforeUpdate,

    /// <summary>The after-update hook.</summary>
    AfterUpdate,

    /// <summary><c>Dispose</c>.</summary>
    Dispose,
}

/// <summary>One transaction interaction, with its position in call order.</summary>
/// <param name="Ordinal">The one-based position of this call among all calls on the transaction.</param>
/// <param name="Kind">Which verb was invoked.</param>
/// <param name="Argument">
/// The argument presented, rendered as text - a statement, an observed result code, or empty for a
/// verb that takes none.
/// </param>
internal readonly record struct RecordedTransactionCall(
    int Ordinal,
    TransactionCallKind Kind,
    string Argument);

/// <summary>
/// A pooled transaction whose provider state, dialect and failure predicate are set independently, so
/// the three different legacy readings of one <c>SQLCode</c> field stay distinguishable.
/// </summary>
/// <remarks>
/// <para>
/// <b>WHY INDEPENDENCE IS THE WHOLE REQUIREMENT (C-B).</b> The oracle tests the SAME
/// <c>SQLCode</c> field three different ways at five locators, and
/// <c>Concurrency/ConflictDetector</c>'s header records all five so a future reader does not
/// "harmonize" them:
/// </para>
/// <list type="table">
/// <item>
/// <term>The defensive override</term>
/// <description>
/// EXACTLY <c>-1</c>, together with a claimed success
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L208</c>]. See
/// <see cref="DefensiveOverrideSqlCode"/>.
/// </description>
/// </item>
/// <item>
/// <term>The transaction object's own state test</term>
/// <description>
/// NON-ZERO EXCLUDING <c>100</c> -
/// <c>if SQLCode &lt;&gt; 0 and SQLCode &lt;&gt; 100 then return RetCode.E_DB_ERROR</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L233-L235</c>], and a non-zero code
/// also rewrites a claimed success at <c>[:L275]</c>. See <see cref="NotFoundSqlCode"/>.
/// </description>
/// </item>
/// <item>
/// <term>The failure predicate</term>
/// <description>NEGATIVE [<c>:L337</c>], with the success predicate its complement at <c>[:L340]</c>.</description>
/// </item>
/// </list>
/// <para>
/// A POSITIVE NON-ZERO CODE - a driver warning - therefore rewrites a claimed success under the second
/// reading but NOT under the first, and makes one veto arm report a database error while the other
/// reports a clean cancellation. A double that derived its predicate from its code, or its code from
/// its predicate, would collapse all three into one condition and every suite built on it would agree
/// with itself while disagreeing with the oracle. Hence <see cref="SqlCode"/> is freely settable to
/// <c>100</c>, <c>-1</c>, a positive warning or zero, and <see cref="FailurePredicate"/> is scripted
/// separately.
/// </para>
/// <para>
/// <b>TWO INTERFACES ON ONE OBJECT, DELIBERATELY.</b> <c>IPooledTransaction</c> is the pool's view and
/// <c>IUpdateTransaction</c> is the update path's narrower view of the same legacy object. Their
/// <c>ClearState()</c> members are identical so one implementation serves both, while
/// <c>IsSqlFailed()</c> and <c>IsFailed()</c> are distinct names for the same legacy predicate and are
/// both routed to <see cref="FailurePredicate"/> - which is what keeps the two views consistent the way
/// a single legacy object is.
/// </para>
/// <para>
/// <b>NO ENGINE, NO CONNECTION, NO DATABASE (C-E).</b> <c>ResolveEngine()</c> keeps the interface's
/// default of <see langword="null"/> and <see cref="TryGetEngineCapability"/> answers
/// <see langword="false"/> for everything. Answering the capability probe honestly is the point: a
/// caller that needs the SQLite command source takes its no-capability branch, which is exactly the
/// branch a pooled transaction over a non-SQLite engine drives it down in production.
/// </para>
/// <para>
/// <b>NO CREDENTIAL IS EVER READ OR ECHOED (C-F).</b> <see cref="ApplyTransactionData"/> records that
/// it was called and the descriptor it was handed, and reads no field of it - in particular never
/// <c>LogPass</c>, which is write-only by contract and must never be echoed in a response or a log.
/// </para>
/// </remarks>
internal sealed class ScriptedPooledTransaction : IPooledTransaction, IUpdateTransaction
{
    /// <summary>
    /// The provider code the defensive override tests for, EXACTLY -
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L208</c>].
    /// </summary>
    /// <remarks>
    /// Declared here so a suite can drive the override without restating the magic value, and so the
    /// exactness of the comparison is visible at the point a test sets it. It is the same value as
    /// <c>ConflictDetector.DefensiveOverrideSqlCode</c> and is deliberately NOT read from there: a
    /// double that borrowed the production constant could not detect the production constant changing.
    /// </remarks>
    internal const long DefensiveOverrideSqlCode = -1L;

    /// <summary>
    /// The provider code that means "not found" and READS AS SUCCESS -
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L233-L235</c>].
    /// </summary>
    /// <remarks>
    /// The value that makes the second reading differ from the first and the third: it is non-zero, so
    /// a naive non-zero-means-error port fails it, yet it is not negative and not <c>-1</c>, so neither
    /// the failure predicate nor the defensive override fires on it.
    /// </remarks>
    internal const long NotFoundSqlCode = 100L;

    private readonly Lock _gate = new();
    private readonly List<RecordedTransactionCall> _calls = [];

    /// <inheritdoc cref="IPooledTransaction.SqlCode"/>
    /// <remarks>
    /// SETTABLE, AND SETTABLE INDEPENDENTLY OF <see cref="FailurePredicate"/>. That independence is the
    /// reason this double exists; see the type remarks.
    /// </remarks>
    public long SqlCode { get; set; }

    /// <inheritdoc cref="IPooledTransaction.SqlDbCode"/>
    public long SqlDbCode { get; set; }

    /// <inheritdoc cref="IPooledTransaction.SqlNRows"/>
    public long SqlNRows { get; set; }

    /// <inheritdoc cref="IPooledTransaction.SqlErrText"/>
    public string SqlErrText { get; set; } = string.Empty;

    /// <inheritdoc cref="IPooledTransaction.SqlReturnData"/>
    public string SqlReturnData { get; set; } = string.Empty;

    /// <inheritdoc/>
    public bool AutoCommit { get; set; }

    /// <summary>Moves the auto-commit mode and answers <see cref="RetCode.OK"/>.</summary>
    /// <param name="autoCommit">The mode to put in force.</param>
    /// <returns>Always <see cref="RetCode.OK"/>.</returns>
    /// <remarks>
    /// ROUTED THROUGH THE PROPERTY, so this double moves exactly the state the assignment moves. There is
    /// no engine beneath it whose begin could fail, which is the contract's own nothing-to-do case.
    /// </remarks>
    public long TrySetAutoCommit(bool autoCommit)
    {
        AutoCommit = autoCommit;

        return RetCode.OK;
    }

    /// <summary>
    /// How <see cref="IsSqlFailed"/> and <see cref="IsFailed"/> decide failure. Defaults to the
    /// legacy's NEGATIVE test [<c>n_cst_thread_trans.sru:L337</c>].
    /// </summary>
    /// <remarks>
    /// Script it to make the predicate disagree with the raw code on purpose - which is how a suite
    /// demonstrates that a consumer consulted the PREDICATE rather than re-deriving failure from the
    /// field, a distinction the oracle depends on and a collapsed double would hide.
    /// </remarks>
    internal Func<long, bool> FailurePredicate { get; set; } = static code => code < 0L;

    /// <summary>
    /// How <see cref="IsSqlSucceeded"/> decides success, or <see langword="null"/> to use the
    /// complement of <see cref="FailurePredicate"/>.
    /// </summary>
    /// <remarks>
    /// THE COMPLEMENT IS THE DEFAULT BECAUSE THE ORACLE'S TWO PREDICATES ARE COMPLEMENTARY -
    /// <c>&lt; 0</c> and <c>&gt;= 0</c> [<c>:L337</c>, <c>:L340</c>] - and a separate hook exists only
    /// so a suite can prove a consumer asked the right one of the two.
    /// </remarks>
    internal Func<long, bool>? SuccessPredicate { get; set; }

    /// <summary>The dialect <see cref="GetDbType"/> reports.</summary>
    /// <remarks>
    /// DEFAULTS TO SQL SERVER BECAUSE THE ORACLE DOES: <c>of_getdbtype</c> answers
    /// <c>DBT_ORACLE</c> only when the DBMS string contains <c>ORACLE</c> and otherwise falls back to
    /// <c>DBT_MSSQL</c> [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L356-L361</c>].
    /// The two-value domain is the whole of it - SQLite is not in the legacy enumeration at all - so
    /// this selects a paging rewriter, never a provisioned database (C-E).
    /// </remarks>
    internal DatabaseType DbType { get; set; } = DatabaseType.DbtMssql;

    /// <summary>What <see cref="Connect"/> answers.</summary>
    internal long ConnectResult { get; set; } = RetCode.OK;

    /// <summary>What <see cref="Disconnect"/> answers.</summary>
    internal long DisconnectResult { get; set; } = RetCode.OK;

    /// <summary>What <see cref="Commit(bool)"/> answers.</summary>
    internal long CommitResult { get; set; } = RetCode.OK;

    /// <summary>What <see cref="Rollback"/> answers.</summary>
    internal long RollbackResult { get; set; } = RetCode.OK;

    /// <summary>What every <c>Exec</c> overload answers.</summary>
    internal long ExecResult { get; set; } = RetCode.OK;

    /// <summary>What <see cref="AutoCommitCheckpoint"/> answers.</summary>
    internal long AutoCommitCheckpointResult { get; set; } = RetCode.OK;

    /// <summary>What <see cref="ApplyTransactionData"/> answers.</summary>
    internal long ApplyTransactionDataResult { get; set; } = RetCode.OK;

    /// <summary>
    /// What <see cref="OnBeforeUpdate"/> answers - the vetoable hook.
    /// </summary>
    /// <remarks>
    /// A VETO IS DISCRIMINATED BY THE FAILURE PREDICATE, NOT BY THE HOOK'S VALUE ALONE. A vetoed
    /// before-update yields a DATABASE ERROR when the transaction also reports failure and a CLEAN
    /// CANCELLATION when it does not [<c>n_cst_thread_task_sqlupdate.sru:L196</c>], so a suite covering
    /// both outcomes scripts this together with <see cref="FailurePredicate"/> or
    /// <see cref="SqlCode"/> - which is only expressible because the two are independent here.
    /// </remarks>
    internal long BeforeUpdateResult { get; set; } = RetCode.OK;

    /// <summary>
    /// The result the after-update hook was handed, or <see langword="null"/> when it never fired.
    /// </summary>
    /// <remarks>
    /// RECORDED SEPARATELY FROM THE FINAL OUTCOME BECAUSE THE HOOK OBSERVES THE UNRECONCILED VALUE. The
    /// after-update hook sits at <c>[:L206]</c> and the defensive rewrite at <c>[:L208]</c>, so the hook
    /// sees the value BEFORE the override. A double that reported the reconciled value would make an
    /// order defect invisible.
    /// </remarks>
    internal long? AfterUpdateObserved { get; private set; }

    /// <summary>How many times the after-update hook fired.</summary>
    internal int AfterUpdateCalls { get; private set; }

    /// <summary>How many times the before-update hook fired.</summary>
    internal int BeforeUpdateCalls { get; private set; }

    /// <summary>How many times <see cref="ClearState"/> was called.</summary>
    internal int ClearStateCalls { get; private set; }

    /// <summary>Whether <see cref="Connect"/> has succeeded and <see cref="Disconnect"/> has not run.</summary>
    internal bool Connected { get; private set; }

    /// <summary>Whether this transaction reports itself broken.</summary>
    internal bool Broken { get; set; }

    /// <summary>Whether <see cref="Dispose"/> has run.</summary>
    internal bool Disposed { get; private set; }

    /// <summary>
    /// What <see cref="IsConnected(out bool)"/> reports through its <c>probed</c> out-parameter.
    /// </summary>
    /// <remarks>
    /// FALSE BY DEFAULT AND THAT IS HONEST: this double holds no connection, so it cannot probe one.
    /// The flag exists because the liveness cache's observable consequence is precisely WHETHER a probe
    /// happened - inside the 10,000 ms window the answer comes from the cache without probing
    /// [<c>n_cst_thread_trans.sru:L198</c>] - so a suite advancing <see cref="FakeTimeProvider"/> across
    /// that boundary needs somewhere to state the expected answer.
    /// </remarks>
    internal bool ProbeOnIsConnected { get; set; }

    /// <summary>The last state handed to <see cref="StampSqlState"/>, if any.</summary>
    internal SqlState? StampedState { get; private set; }

    /// <summary>
    /// Whether <see cref="ApplyTransactionData"/> was called, without retaining any field of the
    /// descriptor.
    /// </summary>
    /// <remarks>
    /// THE DESCRIPTOR ITSELF IS NOT RETAINED, DELIBERATELY (C-F). <c>TransactionData.LogPass</c> is
    /// write-only by contract - never echoed in a response, never logged - and a double that stored the
    /// whole descriptor for a test to inspect would be an invitation to assert on it. Recording only
    /// that the call happened, plus the non-credential connection identity below, gives a suite
    /// everything it can legitimately need.
    /// </remarks>
    internal int ApplyTransactionDataCalls { get; private set; }

    /// <summary>
    /// The DBMS name of the last applied descriptor - the one field the dialect decision reads.
    /// </summary>
    /// <remarks>
    /// KEPT BECAUSE <c>of_getdbtype</c> READS EXACTLY THIS AND NOTHING ELSE:
    /// <c>Pos(Upper(DBMS),"ORACLE") &gt; 0</c> [<c>n_cst_thread_trans.sru:L356</c>]. It carries no
    /// credential.
    /// </remarks>
    internal string AppliedDbms { get; private set; } = string.Empty;

    /// <summary>Every interaction with this transaction, in call order.</summary>
    internal IReadOnlyList<RecordedTransactionCall> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    /// <summary>Every statement presented to an <c>Exec</c> overload, in call order.</summary>
    internal IReadOnlyList<string> ExecutedStatements
    {
        get
        {
            lock (_gate)
            {
                return
                [
                    .. _calls
                        .Where(static call => call.Kind == TransactionCallKind.Exec)
                        .Select(static call => call.Argument),
                ];
            }
        }
    }

    /// <inheritdoc/>
    public bool IsDestroyed => Disposed;

    /// <summary>Whether the raw code is non-zero and is not the not-found code.</summary>
    /// <returns>
    /// <see langword="true"/> when <see cref="SqlCode"/> satisfies the transaction object's own state
    /// test.
    /// </returns>
    /// <remarks>
    /// EXPOSED SO A SUITE CAN ASSERT THE SECOND READING WITHOUT RESTATING IT, and named for what it
    /// tests rather than for the consumer that uses it. It is NOT part of either interface: no consumer
    /// calls it, so nothing about production behaviour changes by its presence, and a test that asserts
    /// on it is asserting about the scripted state rather than about the port.
    /// </remarks>
    internal bool IsSqlCodeNonZeroExcludingNotFound()
    {
        return SqlCode != 0L && SqlCode != NotFoundSqlCode;
    }

    /// <summary>Whether the raw code is exactly the defensive-override code.</summary>
    /// <returns><see langword="true"/> when <see cref="SqlCode"/> is exactly <c>-1</c>.</returns>
    /// <remarks>The first reading, exposed for the same reason as the second.</remarks>
    internal bool IsSqlCodeDefensiveOverride()
    {
        return SqlCode == DefensiveOverrideSqlCode;
    }

    /// <inheritdoc/>
    public void StampSqlState(in SqlState state)
    {
        StampedState = state;
        SqlCode = state.SqlCode;
        SqlDbCode = state.SqlDbCode;
        SqlNRows = state.SqlNRows;
        SqlErrText = state.SqlErrText;
        SqlReturnData = state.SqlReturnData;

        Record(TransactionCallKind.StampSqlState, state.SqlCode.ToString(CultureInfo.InvariantCulture));
    }

    /// <inheritdoc/>
    public long Connect(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Record(TransactionCallKind.Connect, string.Empty);

        if (Predicates.IsFailed(ConnectResult))
        {
            return ConnectResult;
        }

        Connected = true;
        return ConnectResult;
    }

    /// <inheritdoc/>
    public long Disconnect()
    {
        Record(TransactionCallKind.Disconnect, string.Empty);
        Connected = false;
        return DisconnectResult;
    }

    /// <inheritdoc/>
    public long Rollback()
    {
        Record(TransactionCallKind.Rollback, string.Empty);
        return RollbackResult;
    }

    /// <inheritdoc/>
    public long Commit(bool autoRollback)
    {
        Record(
            TransactionCallKind.Commit,
            autoRollback.ToString(CultureInfo.InvariantCulture));

        return CommitResult;
    }

    /// <inheritdoc/>
    public long Commit()
    {
        return Commit(true);
    }

    /// <inheritdoc/>
    public long AutoCommitCheckpoint()
    {
        Record(TransactionCallKind.AutoCommitCheckpoint, string.Empty);
        return AutoCommitCheckpointResult;
    }

    /// <inheritdoc/>
    public long Exec(string? sqlCommand, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Record(TransactionCallKind.Exec, sqlCommand ?? string.Empty);
        return ExecResult;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// THE BOUND OVERLOAD RECORDS THE RENDERED TEXT, not the parameterized text, because the rendered
    /// form is what the legacy would have executed and therefore what parity is measured against. The
    /// parameterized form is an implementation improvement the port is permitted precisely because it
    /// is unobservable.
    /// </remarks>
    public long Exec(in SqlCommandText command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Record(TransactionCallKind.Exec, command.RenderedText);
        return ExecResult;
    }

    /// <inheritdoc/>
    public bool IsConnected()
    {
        return Connected;
    }

    /// <inheritdoc/>
    public bool IsConnected(out bool probed)
    {
        probed = ProbeOnIsConnected;
        return Connected;
    }

    /// <inheritdoc/>
    public bool IsBroken()
    {
        return Broken;
    }

    /// <inheritdoc/>
    public long SetBroken()
    {
        Record(TransactionCallKind.SetBroken, string.Empty);
        Broken = true;
        return RetCode.OK;
    }

    /// <inheritdoc/>
    public void ClearState()
    {
        Record(TransactionCallKind.ClearState, string.Empty);
        ClearStateCalls++;
        SqlCode = 0L;
        SqlDbCode = 0L;
        SqlNRows = 0L;
        SqlErrText = string.Empty;
        SqlReturnData = string.Empty;
    }

    /// <inheritdoc/>
    public DatabaseType GetDbType()
    {
        return DbType;
    }

    /// <inheritdoc/>
    public long ApplyTransactionData(in TransactionData descriptor)
    {
        ApplyTransactionDataCalls++;
        AppliedDbms = descriptor.Dbms;
        Record(TransactionCallKind.ApplyTransactionData, descriptor.Dbms);
        return ApplyTransactionDataResult;
    }

    /// <inheritdoc/>
    public bool IsSqlFailed()
    {
        return FailurePredicate(SqlCode);
    }

    /// <inheritdoc/>
    public bool IsSqlSucceeded()
    {
        return SuccessPredicate is null ? !FailurePredicate(SqlCode) : SuccessPredicate(SqlCode);
    }

    /// <inheritdoc cref="IUpdateTransaction.IsFailed"/>
    /// <remarks>
    /// THE SAME PREDICATE AS <see cref="IsSqlFailed"/>, because the two interfaces are two views of ONE
    /// legacy object and the legacy has a single <c>of_issqlfailed</c>. Routing them to different
    /// answers would let a suite pass against a composition that cannot exist.
    /// </remarks>
    public bool IsFailed()
    {
        return FailurePredicate(SqlCode);
    }

    /// <inheritdoc/>
    public long OnBeforeUpdate()
    {
        BeforeUpdateCalls++;
        Record(
            TransactionCallKind.BeforeUpdate,
            BeforeUpdateResult.ToString(CultureInfo.InvariantCulture));

        return BeforeUpdateResult;
    }

    /// <inheritdoc/>
    public void OnAfterUpdate(long result)
    {
        AfterUpdateCalls++;
        AfterUpdateObserved = result;
        Record(
            TransactionCallKind.AfterUpdate,
            result.ToString(CultureInfo.InvariantCulture));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// BUILT FROM THE PROVIDER CODE AND TEXT ONLY, WITH NO STATEMENT. That is the production shape:
    /// <c>DbErrorData.FromTransaction</c> leaves <c>SqlSyntax</c> empty, the buffer at
    /// <c>Primary!</c> and the row at <c>0</c>, mirroring the legacy argument lists which pass exactly
    /// those three. Synthesizing a statement here would invent a legacy behaviour that does not exist
    /// and would put unredacted text on a path that has none (C-F).
    /// </remarks>
    public DbErrorData CaptureError()
    {
        return DbErrorData.FromTransaction(SqlDbCode, SqlErrText);
    }

    /// <inheritdoc/>
    public bool TryGetEngineCapability<TCapability>(
        [NotNullWhen(true)] out TCapability? capability)
        where TCapability : class
    {
        capability = null;
        return false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Disposed)
        {
            return;
        }

        Record(TransactionCallKind.Dispose, string.Empty);
        Disposed = true;
        Connected = false;
    }

    /// <summary>The one-based call position of the first call of a verb.</summary>
    /// <param name="kind">The verb expected.</param>
    /// <returns>The one-based call position, or <c>0</c> when the verb was never invoked.</returns>
    internal int OrdinalOf(TransactionCallKind kind)
    {
        lock (_gate)
        {
            foreach (RecordedTransactionCall call in _calls)
            {
                if (call.Kind == kind)
                {
                    return call.Ordinal;
                }
            }

            return 0;
        }
    }

    /// <summary>Whether one verb was first invoked before another was.</summary>
    /// <param name="first">The verb expected first.</param>
    /// <param name="second">The verb expected second.</param>
    /// <returns>
    /// <see langword="true"/> only when BOTH verbs were invoked and the first precedes the second.
    /// </returns>
    internal bool Precedes(TransactionCallKind first, TransactionCallKind second)
    {
        int firstOrdinal = OrdinalOf(first);
        int secondOrdinal = OrdinalOf(second);

        return firstOrdinal > 0 && secondOrdinal > 0 && firstOrdinal < secondOrdinal;
    }

    /// <summary>How many times a verb was invoked.</summary>
    /// <param name="kind">The verb to count.</param>
    /// <returns>The number of recorded calls of that verb.</returns>
    internal int CountOf(TransactionCallKind kind)
    {
        lock (_gate)
        {
            return _calls.Count(call => call.Kind == kind);
        }
    }

    private void Record(TransactionCallKind kind, string argument)
    {
        lock (_gate)
        {
            _calls.Add(new RecordedTransactionCall(_calls.Count + 1, kind, argument));
        }
    }
}

#endregion

#region Seam 5 - a valid option graph with the documented defaults and no secret of any kind

/// <summary>
/// Builds a valid <see cref="PersistenceOptions"/> graph so an option-consuming unit needs no
/// configuration file, no environment variable and no host.
/// </summary>
/// <remarks>
/// <para>
/// <b>THE DEFAULTS ARE THE APPLICATION'S OWN, NOT A SECOND OPINION (C-B).</b> This builder starts from
/// <c>new PersistenceOptions()</c> and overrides only what it must, so the values a test sees are the
/// values <c>Configuration/PersistenceOptions.cs</c> declares: chunk size <c>10000</c>, page counting
/// on, cache off, max rows <c>0</c> meaning unbounded, journal <c>DELETE</c>, integrity check unset,
/// mode <c>rwc</c>, keep-alive off with a resolved expiry of <c>30000</c> ms, idle sweep every
/// <c>15</c> s. Restating any of them here would create a second definition that could silently drift
/// from the first, and the drift would look like a passing test.
/// </para>
/// <para>
/// <b>EXACTLY THREE VALUES MUST BE OVERRIDDEN, AND EACH REASON IS MECHANICAL.</b>
/// <c>JwtOptions.Authority</c> and <c>JwtOptions.Audience</c> are both
/// <c>[Required(AllowEmptyStrings = false)]</c> and both default to the empty string, and
/// <c>JwtOptions.PermittedCallers</c> carries <c>[MinLength(1)]</c> over an empty list - so a bare
/// <c>new PersistenceOptions()</c> FAILS validation on all three and the host refuses to start on it. A
/// builder that left them alone would hand every consumer an invalid graph, which is the opposite of its
/// purpose. The three defaults are named constants so a reader can see exactly what was supplied and
/// why.
/// </para>
/// <para>
/// <b>THE AUDIENCE AND THE PERMITTED CALLER MATCH THE DEPLOYED VALUES; ONLY THE HOST IS SYNTHETIC.</b>
/// <c>appsettings.json</c> requires the audience <c>powerframework-persistence</c> and permits the
/// single caller <c>powerframework-dataservices</c> - which is the whole of the inbound topology, since
/// DataServices is the only service that calls Persistence. Reproducing both means a suite that mints a
/// test principal for the real audience validates against this graph. The authority is deliberately NOT
/// the deployed <c>https://security-service:5104</c>: it is an RFC 2606 <c>.invalid</c> host that cannot
/// resolve, so no test can accidentally reach a real endpoint through it.
/// </para>
/// <para>
/// <b>NONE OF THE THREE IS A CREDENTIAL, AND NO CREDENTIAL CAN BE SET (C-F, C-G).</b> An authority is a
/// base address, an audience is a name and a permitted caller is a name. There is NO password setter, NO
/// key setter and NO token-minting helper anywhere on this type - not as a defaulted parameter, not as an
/// optional overload. Security is the sole issuer in this system and Persistence holds verification
/// material only, so a builder able to inject signing material would be modelling a composition that
/// must not exist. <c>SqliteOptions.Password</c> is left at its declared <see langword="null"/> and
/// there is no verb to change it.
/// </para>
/// <para>
/// <b>NO FILESYSTEM ACCESS, EVEN FOR THE DATA DIRECTORY (C-E, AAP 0.6.7).</b>
/// <see cref="WithDataDirectory"/> records a string and nothing more: it does not create, delete,
/// probe, enumerate or write a path. That is not fastidiousness - the paired-capture rule requires the
/// <c>persistence-db</c> volume state to survive a legacy-side and a target-side capture UNTOUCHED for
/// a workflow comparison to be valid, so a test double that reached for the filesystem could invalidate
/// a recording without failing anything.
/// </para>
/// <para>
/// <b>Fluent, and each verb returns the same instance.</b> A builder is used once and read once, so
/// there is no cloning and no immutability theatre; <see cref="Build"/> hands back the graph it has been
/// mutating.
/// </para>
/// </remarks>
internal sealed class PersistenceOptionsBuilder
{
    /// <summary>
    /// The token-issuing authority a test graph points at - an RFC 2606 <c>.invalid</c> host that
    /// cannot resolve.
    /// </summary>
    /// <remarks>
    /// DELIBERATELY NOT THE DEPLOYED <c>https://security-service:5104</c>. A reachable-looking authority
    /// invites a test to attempt discovery against it; a reserved host cannot resolve, so the attempt
    /// fails loudly instead of reaching something real.
    /// </remarks>
    internal const string DefaultAuthority = "https://security-service.invalid";

    /// <summary>
    /// The audience a test graph requires in an inbound token - the deployed value from
    /// <c>appsettings.json</c>.
    /// </summary>
    internal const string DefaultAudience = "powerframework-persistence";

    /// <summary>
    /// The one caller a test graph permits - the deployed value, and the whole of the inbound topology,
    /// since DataServices is the only service that calls Persistence.
    /// </summary>
    /// <remarks>
    /// SUPPLIED BECAUSE <c>[MinLength(1)]</c> REQUIRES IT. An empty permitted-caller list fails
    /// validation, so a builder that left the list empty could never produce a graph the host accepts.
    /// </remarks>
    internal const string DefaultPermittedCaller = "powerframework-dataservices";

    private readonly PersistenceOptions _options = new();

    /// <summary>Creates a builder holding a graph that already validates.</summary>
    internal PersistenceOptionsBuilder()
    {
        _options.Jwt.Authority = DefaultAuthority;
        _options.Jwt.Audience = DefaultAudience;
        _options.Jwt.PermittedCallers.Add(DefaultPermittedCaller);
    }

    /// <summary>A valid graph carrying nothing but the application's declared defaults.</summary>
    /// <returns>The built options.</returns>
    internal static PersistenceOptions Default()
    {
        return new PersistenceOptionsBuilder().Build();
    }

    /// <summary>A valid graph wrapped for constructor injection.</summary>
    /// <returns>The built options wrapped in <see cref="IOptions{TOptions}"/>.</returns>
    internal static IOptions<PersistenceOptions> DefaultOptions()
    {
        return Options.Create(Default());
    }

    /// <summary>Sets the retrieval chunk size.</summary>
    /// <param name="chunkSize">The number of rows per chunk.</param>
    /// <returns>This builder.</returns>
    /// <remarks>
    /// THE LEGACY GUARD LIVES AT THE SETTER ON THE TASK, NOT HERE. A configured value at or below
    /// <c>1000</c> is rejected by the query task's own chunk-size setter, so this builder accepts what
    /// configuration binding would accept and leaves the rejection where the oracle puts it.
    /// </remarks>
    internal PersistenceOptionsBuilder WithChunkSize(int chunkSize)
    {
        _options.Query.ChunkSize = chunkSize;
        return this;
    }

    /// <summary>Sets the maximum row count, where <c>0</c> means unbounded.</summary>
    /// <param name="maxRows">The row cap.</param>
    /// <returns>This builder.</returns>
    internal PersistenceOptionsBuilder WithMaxRows(long maxRows)
    {
        _options.Query.MaxRows = maxRows;
        return this;
    }

    /// <summary>Turns page counting on or off.</summary>
    /// <param name="enabled">Whether the row count for paging is computed.</param>
    /// <returns>This builder.</returns>
    internal PersistenceOptionsBuilder WithPageCounting(bool enabled)
    {
        _options.Query.PageCounting = enabled;
        return this;
    }

    /// <summary>Turns result caching on or off.</summary>
    /// <param name="enabled">Whether retrieved results are cached.</param>
    /// <returns>This builder.</returns>
    internal PersistenceOptionsBuilder WithCache(bool enabled)
    {
        _options.Query.Cache = enabled;
        return this;
    }

    /// <summary>Selects a page.</summary>
    /// <param name="pageIndex">The one-based page index.</param>
    /// <param name="pageSize">The page size.</param>
    /// <returns>This builder.</returns>
    internal PersistenceOptionsBuilder WithPaging(int pageIndex, int pageSize)
    {
        _options.Query.Paged = true;
        _options.Query.PageIndex = pageIndex;
        _options.Query.PageSize = pageSize;
        return this;
    }

    /// <summary>Sets the SQLite URI <c>journal</c> parameter.</summary>
    /// <param name="journal">The journal mode.</param>
    /// <returns>This builder.</returns>
    /// <remarks>
    /// The six legal values and the <c>DELETE</c> default are the URI grammar documented beside the
    /// legacy connection call [<c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L452-L455</c>]; the
    /// options validator is what enforces them, and this verb deliberately does not pre-validate so a
    /// suite can drive that rejection.
    /// </remarks>
    internal PersistenceOptionsBuilder WithJournal(string journal)
    {
        _options.Sqlite.Journal = journal;
        return this;
    }

    /// <summary>Sets the SQLite URI <c>mode</c> parameter.</summary>
    /// <param name="mode">The open mode.</param>
    /// <returns>This builder.</returns>
    internal PersistenceOptionsBuilder WithMode(string mode)
    {
        _options.Sqlite.Mode = mode;
        return this;
    }

    /// <summary>Sets or clears the SQLite URI <c>check</c> parameter.</summary>
    /// <param name="mode">The integrity-check mode, or <see langword="null"/> to omit the parameter.</param>
    /// <returns>This builder.</returns>
    internal PersistenceOptionsBuilder WithIntegrityCheck(SqliteIntegrityCheckMode? mode)
    {
        _options.Sqlite.Check = mode;
        return this;
    }

    /// <summary>Records the data directory as a string, without touching the filesystem.</summary>
    /// <param name="directory">The directory path to record.</param>
    /// <returns>This builder.</returns>
    /// <remarks>
    /// NOTHING IS CREATED, PROBED OR DELETED HERE. See the type remarks: the paired-capture rule
    /// depends on the volume state being untouched between a legacy-side and a target-side recording.
    /// </remarks>
    internal PersistenceOptionsBuilder WithDataDirectory(string directory)
    {
        _options.Sqlite.DataDirectory = directory;
        return this;
    }

    /// <summary>Records the database file name.</summary>
    /// <param name="fileName">The file name to record.</param>
    /// <returns>This builder.</returns>
    internal PersistenceOptionsBuilder WithDatabaseFileName(string fileName)
    {
        _options.Sqlite.DatabaseFileName = fileName;
        return this;
    }

    /// <summary>Configures the transaction pool's keep-alive behaviour.</summary>
    /// <param name="enabled">Whether released transactions are retained.</param>
    /// <param name="expireSeconds">
    /// The idle window in seconds; a non-positive value resolves to the legacy default.
    /// </param>
    /// <returns>This builder.</returns>
    /// <remarks>
    /// THE NON-POSITIVE FALLBACK IS THE ORACLE'S, NOT A CONVENIENCE:
    /// <c>if _nKeepAliveExpireTime &lt;= 0 then _nKeepAliveExpireTime = KEEPALIVE_EXPIRE</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru</c>, <c>oninit</c>], where the
    /// configured value arrives in SECONDS and is multiplied by 1000. Leaving
    /// <paramref name="expireSeconds"/> at zero is therefore the way to exercise the 30,000 ms default
    /// rather than an omission.
    /// </remarks>
    internal PersistenceOptionsBuilder WithKeepAlive(bool enabled, double expireSeconds = 0d)
    {
        _options.TransactionPool.KeepAlive = enabled;
        _options.TransactionPool.KeepAliveExpireSeconds = expireSeconds;
        return this;
    }

    /// <summary>Sets the idle-sweep interval, where a non-positive value disables the sweep.</summary>
    /// <param name="seconds">The interval in seconds.</param>
    /// <returns>This builder.</returns>
    internal PersistenceOptionsBuilder WithIdleSweepIntervalSeconds(double seconds)
    {
        _options.TransactionPool.IdleSweepIntervalSeconds = seconds;
        return this;
    }

    /// <summary>Names the transaction class the pool activates.</summary>
    /// <param name="className">The class name, or empty for the default.</param>
    /// <returns>This builder.</returns>
    internal PersistenceOptionsBuilder WithTransactionClassName(string className)
    {
        _options.TransactionPool.TransactionClassName = className;
        return this;
    }

    /// <summary>Overrides the token-issuing authority.</summary>
    /// <param name="authority">The base address of the sole issuer.</param>
    /// <returns>This builder.</returns>
    /// <remarks>
    /// AN ADDRESS, NEVER A KEY. The bearer handler fetches discovery metadata and the published key set
    /// beneath this address; no signing or verification material is ever configured through it.
    /// </remarks>
    internal PersistenceOptionsBuilder WithAuthority(string authority)
    {
        _options.Jwt.Authority = authority;
        return this;
    }

    /// <summary>Overrides the required audience.</summary>
    /// <param name="audience">The audience an inbound token must carry.</param>
    /// <returns>This builder.</returns>
    internal PersistenceOptionsBuilder WithAudience(string audience)
    {
        _options.Jwt.Audience = audience;
        return this;
    }

    /// <summary>Adds a permitted caller identity alongside the default one.</summary>
    /// <param name="caller">The caller identity to permit.</param>
    /// <returns>This builder.</returns>
    /// <remarks>
    /// ADDITIVE, NOT REPLACING, because the list is get-only on the options type and because the default
    /// caller is what keeps the graph valid. A suite that needs the default gone clears
    /// <c>Build().Jwt.PermittedCallers</c> itself, which is a visible act at the call site rather than a
    /// side effect of adding one.
    /// </remarks>
    internal PersistenceOptionsBuilder WithPermittedCaller(string caller)
    {
        _options.Jwt.PermittedCallers.Add(caller);
        return this;
    }

    /// <summary>Adds a data-object definition.</summary>
    /// <param name="dataObject">The definition to add.</param>
    /// <returns>This builder.</returns>
    internal PersistenceOptionsBuilder WithDataObject(DataObjectOptions dataObject)
    {
        ArgumentNullException.ThrowIfNull(dataObject);

        _options.DataObjects.Add(dataObject);
        return this;
    }

    /// <summary>Hands back the built graph.</summary>
    /// <returns>The options.</returns>
    internal PersistenceOptions Build()
    {
        return _options;
    }

    /// <summary>Hands back the built graph wrapped for constructor injection.</summary>
    /// <returns>The options wrapped in <see cref="IOptions{TOptions}"/>.</returns>
    internal IOptions<PersistenceOptions> BuildOptions()
    {
        return Options.Create(_options);
    }

    /// <summary>Runs the application's own validator over the built graph.</summary>
    /// <returns>The validation result.</returns>
    /// <remarks>
    /// THE APPLICATION'S VALIDATOR, NOT A REIMPLEMENTATION. Asserting
    /// <c>Validate().Succeeded</c> is what proves this builder still produces a graph the real host
    /// would accept, so a future tightening of the validator surfaces here rather than at deployment.
    /// </remarks>
    internal ValidateOptionsResult Validate()
    {
        return new PersistenceOptionsValidator().Validate(name: null, _options);
    }
}

#endregion

#region Seam 6 - the in-memory carrier, its parent task and its scriptable changeset codec

/// <summary>One progress or status notification the carrier raised, with its position in call order.</summary>
/// <param name="Ordinal">The one-based position of this notification among all notifications.</param>
/// <param name="NotifyCode">The per-task notify code.</param>
/// <param name="Payload">
/// The packed payload. For a progress notification the legacy packs current and total through
/// <c>MakeLong</c>, whose halves are SIXTEEN-BIT WORDS, so a value above 65535 truncates
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L39</c>]. That defect is
/// preserved by the production carrier and this record carries whatever it produced, unwidened.
/// </param>
/// <param name="Text">The accompanying text, usually empty.</param>
internal readonly record struct RecordedNotification(
    int Ordinal,
    long NotifyCode,
    long Payload,
    string Text);

/// <summary>One database error the carrier forwarded, with its position in call order.</summary>
/// <param name="Ordinal">The one-based position of this error among all forwarded errors.</param>
/// <param name="SqlDbCode">The provider code.</param>
/// <param name="SqlErrText">The provider diagnostic.</param>
/// <param name="SqlSyntax">
/// The offending statement. IT CARRIES INTERPOLATED LITERAL VALUES and is never logged by this double
/// or by the production carrier; redaction is <c>Errors/SqlRedactor</c>'s job and happens on the way
/// out to the wire (C-F).
/// </param>
/// <param name="Buffer">The buffer the offending row is in.</param>
/// <param name="Row">The one-based offending row number, or <c>0</c> when not attributable.</param>
internal readonly record struct RecordedCarrierDbError(
    int Ordinal,
    long SqlDbCode,
    string SqlErrText,
    string SqlSyntax,
    DwBuffer Buffer,
    long Row);

/// <summary>One numeric cell read, recording which of the two legacy overloads was used.</summary>
/// <param name="Ordinal">The one-based position of this read among all reads.</param>
/// <param name="Row">The one-based row number read.</param>
/// <param name="ColumnNumber">The one-based column number read.</param>
/// <param name="Buffer">The buffer read from.</param>
/// <param name="OriginalValue">
/// Whether the ORIGINAL value was asked for - the legacy <c>org</c> flag.
/// </param>
/// <param name="FourArgument">
/// Whether the four-argument overload was used. THE DISTINCTION IS THE POINT:
/// <c>sqlitegetitemdouble.srf</c> is a thin wrapper over <c>GetItemNumber(row,col,buff,org)</c>, and the
/// two-argument form is the buffer-defaulting, current-value-only convenience. A consumer that must read
/// an original value has to use the four-argument form, so recording which one was called proves the
/// concurrency payload was assembled from original values rather than current ones.
/// </param>
internal readonly record struct RecordedValueRead(
    int Ordinal,
    long Row,
    int ColumnNumber,
    DwBuffer Buffer,
    bool OriginalValue,
    bool FourArgument);

/// <summary>
/// The parent task a carrier reports to, recording every notification and database error in call order.
/// </summary>
/// <remarks>
/// <para>
/// <b>THE THREE FLAGS ARE SCRIPTED BECAUSE THEY SELECT WHOLE CODE PATHS.</b>
/// <c>IsMainThread</c> chooses between the no-serialization hand-over and the codec path,
/// <c>IsNCharBinding</c> chooses whether the statement rewriter runs, and <c>IsCancelled</c> is checked
/// at the top of every carrier event
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L28, :L31, :L45, :L60</c>].
/// None of them is derivable from anything a unit test can observe, so all three are set rather than
/// inferred.
/// </para>
/// <para>
/// <b>A NOTIFICATION HANDLER RETURNING 1 DOES NOT ALWAYS MEAN ABORT.</b> In the progress arm it does
/// [<c>:L41-L42</c>], but in the row-cap arm a return of 1 CLEARS THE CAP AND CONTINUES
/// [<c>:L65-L68</c>] - a preserved legacy inconsistency. <see cref="NotifyScript"/> exists so one
/// suite can answer differently per notify code and cover both arms without two doubles.
/// </para>
/// </remarks>
internal sealed class ScriptedCarrierParentTask : ICarrierParentTask
{
    private readonly Lock _gate = new();
    private readonly List<RecordedNotification> _notifications = [];
    private readonly List<RecordedCarrierDbError> _dbErrors = [];

    /// <inheritdoc/>
    /// <remarks>
    /// TRUE BY DEFAULT because the main-thread carrier is the ancestor and the unmarshalled path is the
    /// simpler one to reason about; a suite covering the worker path sets it false explicitly, which
    /// makes the marshalling boundary visible at the call site.
    /// </remarks>
    public bool IsMainThread { get; set; } = true;

    /// <inheritdoc/>
    public bool IsNCharBinding { get; set; }

    /// <inheritdoc/>
    public bool IsCancelled { get; set; }

    /// <summary>What <see cref="OnNotify"/> answers when <see cref="NotifyScript"/> is unset.</summary>
    /// <remarks>
    /// ZERO IS "CONTINUE" - <c>DataWindowBufferStore.EventContinue</c> - matching the legacy default of
    /// an unhandled event.
    /// </remarks>
    internal long NotifyResult { get; set; } = DataWindowBufferStore.EventContinue;

    /// <summary>What <see cref="OnDbError"/> answers.</summary>
    internal long DbErrorResult { get; set; } = DataWindowBufferStore.EventContinue;

    /// <summary>
    /// An optional per-notification answer, handed the notification about to be recorded.
    /// </summary>
    internal Func<RecordedNotification, long>? NotifyScript { get; set; }

    /// <summary>Every notification raised, in call order.</summary>
    internal IReadOnlyList<RecordedNotification> Notifications
    {
        get
        {
            lock (_gate)
            {
                return [.. _notifications];
            }
        }
    }

    /// <summary>Every database error forwarded, in call order.</summary>
    internal IReadOnlyList<RecordedCarrierDbError> DbErrors
    {
        get
        {
            lock (_gate)
            {
                return [.. _dbErrors];
            }
        }
    }

    /// <summary>How many notifications were raised.</summary>
    internal int NotifyCount
    {
        get
        {
            lock (_gate)
            {
                return _notifications.Count;
            }
        }
    }

    /// <inheritdoc/>
    public long OnDbError(
        long sqlDbCode,
        string sqlErrText,
        string sqlSyntax,
        DwBuffer buffer,
        long row)
    {
        lock (_gate)
        {
            _dbErrors.Add(
                new RecordedCarrierDbError(
                    _dbErrors.Count + 1,
                    sqlDbCode,
                    sqlErrText,
                    sqlSyntax,
                    buffer,
                    row));
        }

        return DbErrorResult;
    }

    /// <inheritdoc/>
    public long OnNotify(long notifyCode, long payload, string text)
    {
        RecordedNotification notification;

        lock (_gate)
        {
            notification = new RecordedNotification(
                _notifications.Count + 1,
                notifyCode,
                payload,
                text);

            _notifications.Add(notification);
        }

        return NotifyScript is null ? NotifyResult : NotifyScript(notification);
    }

    /// <summary>How many notifications carried a given notify code.</summary>
    /// <param name="notifyCode">The code to count.</param>
    /// <returns>The number of matching notifications.</returns>
    internal int CountOf(long notifyCode)
    {
        lock (_gate)
        {
            return _notifications.Count(notification => notification.NotifyCode == notifyCode);
        }
    }

    /// <summary>Forgets every recorded notification and error.</summary>
    internal void Clear()
    {
        lock (_gate)
        {
            _notifications.Clear();
            _dbErrors.Clear();
        }
    }
}

/// <summary>
/// A changeset payload codec that delegates to the real one unless a test scripts a return value.
/// </summary>
/// <remarks>
/// <para>
/// <b>DELEGATION RATHER THAN REIMPLEMENTATION (C-B).</b> The real
/// <c>Buffers/ChangesetPayloadCodec</c> already projects and applies buffer segments with no database
/// involved, so a hand-written substitute would be a second encoder whose disagreements with the first
/// would look like passing tests. This type adds exactly one capability the real codec cannot offer:
/// FAILING ON DEMAND.
/// </para>
/// <para>
/// <b>WHY FAILING ON DEMAND IS NECESSARY.</b> <c>ChangesetCodec</c> reports
/// <c>GetChanges Failed</c> when the encode step returns a non-success value, and that arm is otherwise
/// UNREACHABLE from a unit test: the real encoder fails only on a malformed carrier, and a carrier
/// malformed enough to break it cannot be built through the carrier's own public surface. Scripting the
/// return is the only honest way to cover the failure text.
/// </para>
/// <para>
/// <b>A SCRIPTED FAILURE YIELDS NO PAYLOAD.</b> When <see cref="EncodeResult"/> is set, the state comes
/// back <see langword="null"/>, because a legacy <c>GetChanges</c> that fails has not filled its
/// <c>ref blob</c>. Returning a payload alongside a failure code would let a consumer's mistake -
/// using the payload despite the code - pass.
/// </para>
/// </remarks>
internal sealed class ScriptedChangesetCodec : IChangesetPayloadCodec
{
    private readonly IChangesetPayloadCodec _inner;

    /// <summary>Creates a codec that delegates to the real payload codec.</summary>
    /// <param name="inner">
    /// The codec to delegate to, or <see langword="null"/> for a fresh real
    /// <c>ChangesetPayloadCodec</c>.
    /// </param>
    internal ScriptedChangesetCodec(IChangesetPayloadCodec? inner = null)
    {
        _inner = inner ?? new ChangesetPayloadCodec();
    }

    /// <summary>
    /// A forced <see cref="TryEncode"/> result, or <see langword="null"/> to delegate.
    /// </summary>
    /// <remarks>
    /// Set it to <c>DataWindowBufferStore.DataStoreFailure</c> to reach the <c>GetChanges Failed</c>
    /// arm.
    /// </remarks>
    internal long? EncodeResult { get; set; }

    /// <summary>
    /// A forced <see cref="TryApply"/> result, or <see langword="null"/> to delegate.
    /// </summary>
    internal long? ApplyResult { get; set; }

    /// <summary>How many times <see cref="TryEncode"/> was called.</summary>
    internal int EncodeCalls { get; private set; }

    /// <summary>How many times <see cref="TryApply"/> was called.</summary>
    internal int ApplyCalls { get; private set; }

    /// <summary>The most recent payload produced by a delegated encode.</summary>
    internal CarrierState? LastEncoded { get; private set; }

    /// <summary>The most recent payload presented to <see cref="TryApply"/>.</summary>
    internal CarrierState? LastApplied { get; private set; }

    /// <inheritdoc/>
    public long TryEncode(DataWindowBufferStore source, out CarrierState? state)
    {
        ArgumentNullException.ThrowIfNull(source);

        EncodeCalls++;

        if (EncodeResult is long scripted)
        {
            state = null;
            LastEncoded = null;
            return scripted;
        }

        long code = _inner.TryEncode(source, out state);
        LastEncoded = state;
        return code;
    }

    /// <inheritdoc/>
    public long TryApply(
        DataWindowBufferStore target,
        CarrierState? state,
        CarrierBaselineTrust baselineTrust)
    {
        ArgumentNullException.ThrowIfNull(target);

        ApplyCalls++;
        LastApplied = state;

        return ApplyResult ?? _inner.TryApply(target, state, baselineTrust);
    }
}

/// <summary>
/// The DataWindow-shaped result carrier the buffer, codec, concurrency and task suites drive, with no
/// database, no connection and no PowerBuilder runtime behind it.
/// </summary>
/// <remarks>
/// <para>
/// <b>IT DERIVES FROM THE PRODUCTION STORE RATHER THAN REPLACING IT (C-B).</b>
/// <c>Buffers/DataWindowBufferStore</c> already is an in-memory carrier with the three legacy buffers,
/// one-based row numbers, per-row and per-column item status and current AND original value per cell.
/// Reimplementing that here would create a second definition of the behaviour under test, and every
/// assertion would then be made against the double's opinion. So <c>RowsMove</c>,
/// <c>RowsDiscard</c>, <c>Reset</c>, <c>ResetUpdate</c>, <c>GetNextModified</c>,
/// <c>EnumerateModifiedRows</c>, <c>SetSqlPreview</c>, the four counts and <c>Processing</c> are
/// INHERITED UNCHANGED and are deliberately NOT overridden - the double adds seeding verbs, the
/// legacy-named transfer verbs, scriptable failure, an ordered record of numeric reads and the identity
/// value surface, and nothing else.
/// </para>
/// <para>
/// <b>THE IDENTITY SURFACE IS IMPLEMENTED EXPLICITLY, WHICH IS A REQUIREMENT AND NOT A STYLE CHOICE.</b>
/// <c>IIdentityValueSource</c> declares <c>RowCount()</c>, <c>FilteredCount()</c> and
/// <c>GetItemStatus(long, int, DwBuffer)</c>, all of which the base class already provides as
/// <c>internal</c> members. Implementing them implicitly would require declaring PUBLIC members of the
/// same signature, which HIDES the inherited ones - a <c>CS0108</c> warning that
/// <c>TreatWarningsAsErrors</c> turns into a build failure, and, far worse, a shadow through which the
/// double could answer differently from the store it is meant to be. Explicit implementation forwards to
/// the base and cannot shadow it.
/// </para>
/// <para>
/// <b>THE Filter! BUFFER'S ROW ORDER IS PRESERVED EXACTLY AS GIVEN (C-B, risk R9).</b> The identity
/// round trip walks Primary! FORWARD and Filter! BACKWARD, because the filter buffer's order is inverted
/// relative to the source [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L235-L238</c>].
/// This double NEVER sorts, reverses or otherwise normalises a buffer: rows appear in the order they
/// were seeded, so a backward walk over a buffer seeded in inverted order yields SOURCE order and a
/// forward walk does not. That difference is what
/// <c>Concurrency/IdentityColumnResolver.CollectFilterValues</c> is asserted against; a double that
/// helpfully normalised the order would make the backward walk and the forward walk indistinguishable
/// and the suite would pass either way - which is exactly the regression risk R9 warns about, since
/// "correcting" the direction produces wrong identity values that a row-count assertion still accepts.
/// </para>
/// <para>
/// <b>CURRENT AND ORIGINAL VALUES ARE SEEDED THROUGH THE LEGACY'S OWN SEQUENCE.</b>
/// <c>CarrierRow.SetValue</c> records the PREVIOUS value as the original on the first write to a
/// column, so a freshly seeded row would report an original of <see langword="null"/> unless the
/// retrieve baseline is taken. <see cref="SeedRetrievedRow"/> therefore writes the values and then calls
/// the row's <c>Baseline()</c> - which is precisely what a legacy retrieve does - and
/// <see cref="EditCell"/> writes afterwards so the baselined value becomes the original. That ordering
/// is what makes <c>updatewhere=1</c> expressible: the generated where clause carries the ORIGINAL value
/// of every marked column, and all six columns of the only updatable DataWindow in the repository are
/// marked [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14</c>].
/// </para>
/// <para>
/// <b>ROW STATUS AND COLUMN STATUS ARE SEPARATE VERBS THAT CANNOT BE CONFUSED.</b> The legacy reads a
/// ROW's status with column index <c>0</c> - <c>GetItemStatus(nRow,0,Primary!)</c> [<c>:L160</c>] - and a
/// genuine per-COLUMN status two lines away with a real index [<c>:L162</c>]. Column <c>0</c> is not a
/// column; it means "the row itself". <see cref="SetRowStatus"/> addresses the row and
/// <see cref="SetColumnStatus"/> REFUSES a column number of <c>0</c>, mirroring the production store's
/// own refusal, so a caller cannot silently write a row status where it meant a column status.
/// </para>
/// <para>
/// <b>NO STORAGE, NO CLOCK OF ITS OWN, NO SECRET (C-E, C-F).</b> Nothing here opens a connection,
/// touches a file or reads a real clock; the only time source available to a consumer is the injected
/// <see cref="FakeTimeProvider"/>, and no credential-shaped value appears anywhere on this type.
/// </para>
/// </remarks>
internal sealed class FakeDataWindowCarrier : DataWindowBufferStore, IIdentityValueSource
{
    private readonly Lock _gate = new();
    private readonly List<RecordedValueRead> _valueReads = [];

    /// <summary>Creates a carrier with its own scripted surface, codec and parent task.</summary>
    /// <param name="surface">The describe and modify surface, or <see langword="null"/> for a fresh one.</param>
    /// <param name="changeset">The changeset codec, or <see langword="null"/> for a fresh one.</param>
    /// <param name="parentTask">The parent task, or <see langword="null"/> for a fresh one.</param>
    /// <remarks>
    /// ALL THREE ARE INJECTABLE so a suite driving several carriers against one recorded ordering - a
    /// parent-and-child changeset transfer, for instance - can share a single surface and see the calls
    /// interleaved in true order.
    /// </remarks>
    internal FakeDataWindowCarrier(
        ScriptedCarrierSurface? surface = null,
        ScriptedChangesetCodec? changeset = null,
        ScriptedCarrierParentTask? parentTask = null)
    {
        Surface = surface ?? new ScriptedCarrierSurface();
        Changeset = changeset ?? new ScriptedChangesetCodec();
        ParentTask = parentTask ?? new ScriptedCarrierParentTask();
    }

    /// <summary>The describe, modify, sort and filter surface this carrier presents.</summary>
    internal ScriptedCarrierSurface Surface { get; }

    /// <summary>The changeset codec the two changeset verbs route through.</summary>
    internal ScriptedChangesetCodec Changeset { get; }

    /// <summary>The parent task this carrier reports notifications and errors to.</summary>
    internal ScriptedCarrierParentTask ParentTask { get; }

    /// <summary>The inserted-row count reported after an update.</summary>
    /// <remarks>
    /// SET RATHER THAN COMPUTED, because the legacy reads these from the DataWindow engine after the
    /// update statements have run - <c>GetInsertedCount()</c> and its two siblings are engine
    /// counters, not derivations from buffer contents - and this double runs no statements. The counts
    /// are reported through the identity surface in the oracle's own argument order: inserted, updated,
    /// deleted [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L247</c>].
    /// </remarks>
    internal long RowsInserted { get; set; }

    /// <summary>The updated-row count reported after an update.</summary>
    internal long RowsUpdated { get; set; }

    /// <summary>The deleted-row count reported after an update.</summary>
    internal long RowsDeleted { get; set; }

    /// <summary>Every numeric cell read, in call order.</summary>
    internal IReadOnlyList<RecordedValueRead> ValueReads
    {
        get
        {
            lock (_gate)
            {
                return [.. _valueReads];
            }
        }
    }

    /// <summary>How many numeric cell reads asked for the ORIGINAL value.</summary>
    /// <remarks>
    /// The direct measure of whether a consumer assembled its concurrency payload from original values.
    /// A count of zero on an <c>updatewhere=1</c> path means the port is comparing current values, which
    /// silently defeats optimistic concurrency.
    /// </remarks>
    internal int OriginalValueReadCount
    {
        get
        {
            lock (_gate)
            {
                return _valueReads.Count(read => read.OriginalValue);
            }
        }
    }

    /// <summary>Seeds a freshly retrieved, unmodified row and takes the retrieve baseline.</summary>
    /// <param name="buffer">The buffer to append to.</param>
    /// <param name="cells">The column number and value of each populated cell.</param>
    /// <returns>The one-based row number of the appended row.</returns>
    /// <remarks>
    /// AFTER THIS CALL THE CURRENT AND ORIGINAL VALUES ARE EQUAL AND THE ROW READS AS
    /// <c>NotModified!</c>, which is the state a retrieve leaves a row in. Call
    /// <see cref="EditCell"/> next to make the two differ.
    /// </remarks>
    internal long SeedRetrievedRow(
        DwBuffer buffer,
        params (int ColumnNumber, object? Value)[] cells)
    {
        ArgumentNullException.ThrowIfNull(cells);

        long row = AppendRow(buffer, ItemStatus.NotModified);

        foreach ((int columnNumber, object? value) in cells)
        {
            _ = SetItemValue(row, columnNumber, buffer, value);
        }

        // THE RETRIEVE BASELINE. Without it every original value would read as null, because
        // CarrierRow.SetValue records the PREVIOUS value - which is nothing on a first write.
        RowAt(row, buffer).Baseline();

        return row;
    }

    /// <summary>Seeds several freshly retrieved rows into one buffer, in the order given.</summary>
    /// <param name="buffer">The buffer to append to.</param>
    /// <param name="rows">One cell set per row, appended in sequence.</param>
    /// <returns>The one-based row numbers of the appended rows, in the order appended.</returns>
    /// <remarks>
    /// THE ORDER GIVEN IS THE ORDER STORED, and for <c>Filter!</c> that is the whole point: a suite
    /// proving the backward walk seeds the rows in the INVERTED order the legacy buffer actually holds
    /// them in, and this verb must not tidy that up. See the type remarks and risk R9.
    /// </remarks>
    internal IReadOnlyList<long> SeedRetrievedRows(
        DwBuffer buffer,
        params (int ColumnNumber, object? Value)[][] rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        List<long> appended = [];

        foreach ((int ColumnNumber, object? Value)[] cells in rows)
        {
            appended.Add(SeedRetrievedRow(buffer, cells));
        }

        return appended;
    }

    /// <summary>Changes a cell's CURRENT value, leaving its original value and both statuses alone.</summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="columnNumber">The one-based column number.</param>
    /// <param name="buffer">The buffer holding the row.</param>
    /// <param name="value">The new current value.</param>
    /// <remarks>
    /// STATUS IS NOT TOUCHED HERE, DELIBERATELY. A DataWindow engine would set the statuses as a side
    /// effect of an edit, but the port's suites frequently need a value change and a status change to be
    /// independently controllable - a row whose value differs while its status still reads
    /// <c>NotModified!</c> is exactly how a stale-status defect is caught. Use
    /// <see cref="SetRowStatus"/> and <see cref="SetColumnStatus"/>, or
    /// <see cref="SeedEditedRow"/> for the combined case.
    /// </remarks>
    internal void EditCell(long row, int columnNumber, DwBuffer buffer, object? value)
    {
        _ = SetItemValue(row, columnNumber, buffer, value);
    }

    /// <summary>Seeds a row that was retrieved and then edited, so current and original differ.</summary>
    /// <param name="buffer">The buffer to append to.</param>
    /// <param name="rowStatus">The row status to leave the row in.</param>
    /// <param name="retrieved">The values as retrieved - these become the ORIGINAL values.</param>
    /// <param name="edits">The subsequent edits - these become the CURRENT values.</param>
    /// <returns>The one-based row number of the appended row.</returns>
    /// <remarks>
    /// THE CONCURRENCY FIXTURE IN ONE CALL. <c>updatewhere=1</c> is "key and updateable columns" mode, so
    /// the generated where clause carries the original value of every marked column while the SET list
    /// carries the current ones; a row where the two are equal cannot exercise that at all. Each edited
    /// column also receives <paramref name="rowStatus"/> as its per-COLUMN status, which is what the
    /// legacy engine does to an edited column, while the row's own status is set through the
    /// column-zero convention.
    /// </remarks>
    internal long SeedEditedRow(
        DwBuffer buffer,
        ItemStatus rowStatus,
        (int ColumnNumber, object? Value)[] retrieved,
        params (int ColumnNumber, object? Value)[] edits)
    {
        ArgumentNullException.ThrowIfNull(retrieved);
        ArgumentNullException.ThrowIfNull(edits);

        long row = SeedRetrievedRow(buffer, retrieved);

        foreach ((int columnNumber, object? value) in edits)
        {
            EditCell(row, columnNumber, buffer, value);
            SetColumnStatus(row, columnNumber, buffer, rowStatus);
        }

        SetRowStatus(row, buffer, rowStatus);

        return row;
    }

    /// <summary>Sets a ROW's own status, through the legacy column-zero convention.</summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="buffer">The buffer holding the row.</param>
    /// <param name="status">The status to record.</param>
    /// <remarks>
    /// ROUTED THROUGH <c>ItemStatusMachine.RowStatusColumn</c> rather than a literal <c>0</c>, so the
    /// sentinel is named at every use and never mistaken for a column index.
    /// </remarks>
    internal void SetRowStatus(long row, DwBuffer buffer, ItemStatus status)
    {
        _ = SetItemStatus(row, ItemStatusMachine.RowStatusColumn, buffer, status);
    }

    /// <summary>Sets a single COLUMN's status, refusing the row-status sentinel.</summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="columnNumber">The one-based column number; <c>0</c> is refused.</param>
    /// <param name="buffer">The buffer holding the row.</param>
    /// <param name="status">The status to record.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="columnNumber"/> is the row-status sentinel, which addresses the row rather than a
    /// column.
    /// </exception>
    /// <remarks>
    /// THE REFUSAL IS THE POINT (C-B). <c>Buffers/ItemStatus.cs</c> deliberately forbids a bare
    /// <c>0</c> where a column number is required, because the row reading and the column reading are
    /// two different facts carried entirely by whether the index is zero. A double that quietly accepted
    /// <c>0</c> here would let a suite conflate them and still pass.
    /// </remarks>
    internal void SetColumnStatus(long row, int columnNumber, DwBuffer buffer, ItemStatus status)
    {
        if (!ItemStatusMachine.TryGetColumnNumber(columnNumber, out int resolved))
        {
            throw new ArgumentOutOfRangeException(
                nameof(columnNumber),
                columnNumber,
                $"Column number must be {ItemStatusMachine.FirstColumnNumber} or greater. The value "
                    + $"{ItemStatusMachine.RowStatusColumn} is the row-status sentinel, which addresses "
                    + $"the row itself - use {nameof(SetRowStatus)} for that.");
        }

        _ = SetItemStatus(row, resolved, buffer, status);
    }

    /// <summary>Reads one column's values out of a buffer in the buffer's own storage order.</summary>
    /// <param name="buffer">The buffer to read.</param>
    /// <param name="columnNumber">The one-based column number.</param>
    /// <returns>The values, first stored first.</returns>
    /// <remarks>
    /// THE PROOF THAT NOTHING WAS NORMALISED. A suite covering the inverted <c>Filter!</c> order asserts
    /// that this - the storage order - is the INVERTED sequence it seeded, while the resolver's backward
    /// walk answers the source sequence. If the two agreed, the double would have reordered something.
    /// This read does NOT appear in <see cref="ValueReads"/>: it is a test-side inspection, not a
    /// consumer read, and polluting the recording with it would break the assertions that count reads.
    /// </remarks>
    internal IReadOnlyList<object?> ColumnValuesInStorageOrder(DwBuffer buffer, int columnNumber)
    {
        long count = CountOf(buffer);
        List<object?> values = [];

        for (long row = ItemStatusMachine.FirstRowNumber; row <= count; row++)
        {
            values.Add(GetItemValue(row, columnNumber, buffer));
        }

        return values;
    }

    /// <summary>The row count of any one buffer.</summary>
    /// <param name="buffer">The buffer to measure.</param>
    /// <returns>The number of rows the buffer holds.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="buffer"/> is not a legacy buffer.</exception>
    /// <remarks>
    /// THE THREE COUNTS HAVE THREE DIFFERENT NAMES IN THE LEGACY - <c>RowCount()</c>,
    /// <c>DeletedCount()</c> and <c>FilteredCount()</c> - and the port keeps all three, so this verb
    /// exists only to let a seeding loop parameterise over the buffer. It dispatches to the inherited
    /// counts rather than counting anything itself.
    /// </remarks>
    internal long CountOf(DwBuffer buffer)
    {
        return buffer switch
        {
            DwBuffer.Primary => RowCount(),
            DwBuffer.Delete => DeletedCount(),
            DwBuffer.Filter => FilteredCount(),
            _ => throw new ArgumentOutOfRangeException(
                nameof(buffer),
                buffer,
                "Buffer must be one of the three legacy buffers: Primary!, Delete! or Filter!."),
        };
    }

    /// <summary>Projects this carrier's state as a changeset - the legacy <c>GetChanges</c>.</summary>
    /// <param name="state">The projected payload, or <see langword="null"/> when the encode failed.</param>
    /// <returns>
    /// The datastore success value, or a failure value when <see cref="ScriptedChangesetCodec.EncodeResult"/>
    /// scripts one.
    /// </returns>
    /// <remarks>
    /// NAMED FOR THE LEGACY VERB, ROUTED THROUGH THE PORT'S SEAM. The legacy signature is
    /// <c>GetChanges(ref blbData)</c>, a <c>ref</c> out-parameter rather than a return, and the reason is
    /// documented threading hazard 1 of <c>docs/PB多线程绕坑提示.md</c>: a worker thread synchronously
    /// calling a main-thread member that returns a <c>string</c> or a <c>blob</c> can corrupt memory. THE
    /// HAZARD HAS NO .NET ANALOGUE - no message pump, no PowerBuilder object lifetime, no posted-message
    /// queue - so the port returns normally and the payload comes back through a C# <c>out</c>
    /// parameter, which reads the same at the call site.
    /// </remarks>
    internal long GetChanges(out CarrierState? state)
    {
        return Changeset.TryEncode(this, out state);
    }

    /// <summary>Applies a changeset onto this carrier - the legacy <c>SetChanges</c>.</summary>
    /// <param name="state">The payload to apply.</param>
    /// <param name="baselineTrust">
    /// Which baseline rule to apply. Defaults to
    /// <see cref="CarrierBaselineTrust.RequiredOnChangedRows"/> - the reading the production update
    /// carrier uses - so a test that says nothing gets the update path's strictness. A test staging a
    /// TRANSFER, which is the other caller of this codec, passes
    /// <see cref="CarrierBaselineTrust.AsStated"/> explicitly.
    /// </param>
    /// <returns>The datastore success or failure value.</returns>
    /// <remarks>
    /// <para>
    /// <b>A PRESERVED DEFECT GOVERNS HOW THIS MAY BE USED (C-B).</b> The legacy records that
    /// <c>Reset</c> MUST NOT be used to clear data before applying a changeset, because doing so makes
    /// the application fail to apply
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L176</c>], and separately
    /// that changeset application may LOSE ROWS on a sorted DataWindow larger than one block
    /// [<c>:L148</c>], worked around by moving rows through a temporary datastore. Neither defect is
    /// corrected here or in the production codec.
    /// </para>
    /// <para>
    /// THE DEFAULT IS THE STRICTER OF THE TWO READINGS ON PURPOSE. This double stands in for the
    /// carrier the update task applies its payload onto [<c>Tasks/SqlUpdateCarrier.SetChanges</c>],
    /// where the originals become the <c>updatewhere=1</c> predicate, so a test that forgets to say
    /// which reading it wants gets the one that refuses a fabricated baseline rather than the one that
    /// infers it.
    /// </para>
    /// </remarks>
    internal long SetChanges(
        CarrierState? state,
        CarrierBaselineTrust baselineTrust = CarrierBaselineTrust.RequiredOnChangedRows)
    {
        return Changeset.TryApply(this, state, baselineTrust);
    }

    /// <summary>Captures this carrier's full state - the legacy <c>GetFullState</c>.</summary>
    /// <returns>The captured payload.</returns>
    /// <remarks>
    /// <b>TWO PRESERVED DEFECTS TRAVEL WITH THIS VERB (C-B).</b> Full-state transfer requires the sort
    /// and filter conditions to be SYNCHRONIZED, which changeset transfer does not and is faster without
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L563, :L578</c>], and
    /// full-state transfer CRASHES when a crosstab has too many columns [<c>:L673</c>], which the legacy
    /// works around explicitly rather than fixing.
    /// </remarks>
    internal CarrierState? GetFullState()
    {
        return FullStateCodec.Capture(this);
    }

    /// <summary>Applies a full state onto this carrier - the legacy <c>SetFullState</c>.</summary>
    /// <param name="state">The payload to apply.</param>
    /// <returns>The datastore success or failure value.</returns>
    internal long SetFullState(CarrierState? state)
    {
        return FullStateCodec.Apply(this, state);
    }

    /// <summary>Sets the processing kind so the full-state transfer path is selected.</summary>
    /// <param name="processingValue">
    /// The processing value; defaults to the crosstab value. Only <c>4</c> and <c>5</c> select full
    /// state.
    /// </param>
    /// <remarks>
    /// THE TWO SELECTING VALUES ARE CROSSTAB AND COMPOSITE, and they are the only two:
    /// <c>DataWindowProcessing.SelectsFullStateTransfer</c> tests for exactly <c>4</c> or <c>5</c>. A
    /// carrier of any other processing kind takes the changeset path, which is why the codec selector is
    /// a property of the DataWindow rather than a caller's choice.
    /// </remarks>
    internal void SelectFullStateTransfer(long processingValue = DataWindowProcessing.CrosstabValue)
    {
        Processing = new DataWindowProcessing(processingValue);
    }

    /// <summary>Forgets every recorded numeric read.</summary>
    internal void ClearValueReads()
    {
        lock (_gate)
        {
            _valueReads.Clear();
        }
    }

    /// <inheritdoc/>
    long IIdentityValueSource.GetInsertedCount()
    {
        return RowsInserted;
    }

    /// <inheritdoc/>
    long IIdentityValueSource.GetUpdatedCount()
    {
        return RowsUpdated;
    }

    /// <inheritdoc/>
    long IIdentityValueSource.GetDeletedCount()
    {
        return RowsDeleted;
    }

    /// <inheritdoc/>
    long IIdentityValueSource.RowCount()
    {
        return RowCount();
    }

    /// <inheritdoc/>
    long IIdentityValueSource.FilteredCount()
    {
        return FilteredCount();
    }

    /// <inheritdoc/>
    ItemStatus IIdentityValueSource.GetItemStatus(long row, int columnIndex, DwBuffer buffer)
    {
        return GetItemStatus(row, columnIndex, buffer);
    }

    /// <inheritdoc/>
    long? IIdentityValueSource.GetItemNumber(long row, int columnNumber)
    {
        return ReadNumber(row, columnNumber, DwBuffer.Primary, originalValue: false, fourArgument: false);
    }

    /// <inheritdoc/>
    long? IIdentityValueSource.GetItemNumber(
        long row,
        int columnNumber,
        DwBuffer buffer,
        bool originalValue)
    {
        return ReadNumber(row, columnNumber, buffer, originalValue, fourArgument: true);
    }

    private long? ReadNumber(
        long row,
        int columnNumber,
        DwBuffer buffer,
        bool originalValue,
        bool fourArgument)
    {
        lock (_gate)
        {
            _valueReads.Add(
                new RecordedValueRead(
                    _valueReads.Count + 1,
                    row,
                    columnNumber,
                    buffer,
                    originalValue,
                    fourArgument));
        }

        object? value = originalValue
            ? GetItemOriginalValue(row, columnNumber, buffer)
            : GetItemValue(row, columnNumber, buffer);

        return ToNumber(value);
    }

    /// <summary>
    /// The numeric coercion, copied VERBATIM from <c>Tasks/SqlUpdateCarrier.ToNumber</c>.
    /// </summary>
    /// <param name="value">The stored cell value.</param>
    /// <returns>The value as a whole number, or <see langword="null"/> when it is not numeric.</returns>
    /// <remarks>
    /// A VERBATIM COPY, AND THAT IS DELIBERATE (C-B). The production identity surface coerces exactly
    /// this way, including truncating a <see cref="decimal"/> or a <see cref="double"/> toward zero and
    /// answering <see langword="null"/> for anything it does not recognise. A double that coerced more
    /// generously - parsing a hexadecimal string, rounding rather than truncating - would let a suite
    /// pass on a value the production surface reports as absent, which is the precise shape of a false
    /// negative in an identity round trip. The invariant culture is the production choice too: an
    /// identity value is a machine value and must not be read through a locale.
    /// </remarks>
    private static long? ToNumber(object? value) => value switch
    {
        null => null,
        long number => number,
        int number => number,
        short number => number,
        byte number => number,
        decimal number => (long)number,
        double number => (long)number,
        float number => (long)number,
        string text when long.TryParse(text, CultureInfo.InvariantCulture, out long parsed) => parsed,
        _ => null,
    };
}

#endregion

#region Self-checks - a double that lies is worse than no double at all

/// <summary>
/// Proves each double in this file behaves the way the suites built on it assume, so a defect in a
/// double surfaces here rather than as a mystery failure - or, far worse, as a false pass - somewhere
/// else.
/// </summary>
/// <remarks>
/// <para>
/// <b>WHY A DOUBLE NEEDS ITS OWN TESTS.</b> Every other suite in this project asserts about production
/// behaviour THROUGH these doubles. A double that silently normalised a buffer order, collapsed two
/// status readings into one or answered a scripted value it was never given would make those suites
/// agree with themselves while disagreeing with the oracle, and nothing would fail. These checks are the
/// only place the doubles themselves are under test.
/// </para>
/// <para>
/// <b>NOTHING HERE MEASURES TIME (AAP 0.8.5) AND NOTHING WAITS FOR IT.</b> No test in this class calls
/// <c>Thread.Sleep</c>, awaits a real delay, reads <c>DateTime.Now</c>, <c>DateTime.UtcNow</c>,
/// <c>Environment.TickCount</c> or constructs a <c>Stopwatch</c>. Every interval is crossed by advancing
/// <see cref="FakeTimeProvider"/>, which is what makes the whole suite produce identical results on
/// repeated runs - the Golden-Master technique's one hard prerequisite.
/// </para>
/// <para>
/// <b>NOTHING HERE TOUCHES A DATABASE, A FILE OR A NETWORK (C-E).</b> The carrier checks run entirely in
/// memory, which is also what keeps the <c>persistence-db</c> volume state intact between a legacy-side
/// and a target-side characterization capture.
/// </para>
/// </remarks>
public sealed class TestDoublesSanityTests
{
    /// <summary>An arbitrary, fixed, non-default start instant used where one is needed.</summary>
    private static readonly DateTimeOffset AlternateStart =
        new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

    // ==============================================================================================
    //  SEAM 1 - THE CLOCK
    // ==============================================================================================

    /// <summary>
    /// Advancing the clock moves the wall-clock reading and the elapsed-interval reading by the SAME
    /// amount, so a consumer reading either one observes the same interval.
    /// </summary>
    /// <remarks>
    /// THE DEFECT THIS CATCHES IS A HALF-FAKED CLOCK. Overriding <c>GetUtcNow</c> and leaving
    /// <c>GetTimestamp</c> on the real system clock compiles, and every wall-clock assertion passes,
    /// while every elapsed-interval consumer silently keeps depending on wall time.
    /// </remarks>
    [Fact]
    public void AdvancingTheClockMovesBothReadingsByTheSameAmount()
    {
        FakeTimeProvider clock = new();

        DateTimeOffset before = clock.GetUtcNow();
        long stampBefore = clock.GetTimestamp();

        clock.Advance(FakeTimeProvider.ProgressThrottleWindow);

        DateTimeOffset after = clock.GetUtcNow();
        long stampAfter = clock.GetTimestamp();

        Assert.Equal(before + FakeTimeProvider.ProgressThrottleWindow, after);
        Assert.Equal(
            FakeTimeProvider.ProgressThrottleWindow,
            clock.GetElapsedTime(stampBefore, stampAfter));
        Assert.Equal(FakeTimeProvider.ProgressThrottleWindow, clock.Elapsed);
        Assert.Equal(1, clock.AdvanceCount);
    }

    /// <summary>
    /// One clock serves all three legacy intervals, and each interval's boundary comparison lands where
    /// the oracle puts it.
    /// </summary>
    /// <remarks>
    /// THE THREE BOUNDARIES ARE NOT THE SAME SHAPE, which is why they are asserted together: the pool
    /// compares <c>&gt;=</c> so exactly the window expires, the liveness cache compares <c>&lt;</c> so
    /// exactly the window leaves the cache, and the progress throttle compares strict <c>&gt;</c> so
    /// exactly the window does NOT release it. Transposing any two would be invisible to a suite that
    /// only advanced past every boundary generously.
    /// </remarks>
    [Fact]
    public void OneClockCoversAllThreeLegacyIntervalsWithTheirOwnBoundaries()
    {
        Assert.Equal(30_000d, FakeTimeProvider.PoolIdleWindow.TotalMilliseconds);
        Assert.Equal(10_000d, FakeTimeProvider.LivenessCacheWindow.TotalMilliseconds);
        Assert.Equal(100d, FakeTimeProvider.ProgressThrottleWindow.TotalMilliseconds);

        FakeTimeProvider clock = new();
        long start = clock.GetTimestamp();

        clock.Advance(FakeTimeProvider.ProgressThrottleWindow);
        TimeSpan atThrottle = clock.GetElapsedTime(start, clock.GetTimestamp());

        // Strict `> 100` [n_cst_thread_task_sqlbase_ds_mt.sru:L37]: exactly the window does NOT release.
        Assert.False(atThrottle > FakeTimeProvider.ProgressThrottleWindow);

        clock.AdvanceMilliseconds(1);
        Assert.True(
            clock.GetElapsedTime(start, clock.GetTimestamp()) > FakeTimeProvider.ProgressThrottleWindow);

        clock.Reset(FakeTimeProvider.DefaultStart);
        start = clock.GetTimestamp();
        clock.Advance(FakeTimeProvider.LivenessCacheWindow);

        // `< 10000` [n_cst_thread_trans.sru:L198]: exactly the window LEAVES the cache.
        Assert.False(
            clock.GetElapsedTime(start, clock.GetTimestamp()) < FakeTimeProvider.LivenessCacheWindow);

        clock.Reset(FakeTimeProvider.DefaultStart);
        start = clock.GetTimestamp();
        clock.Advance(FakeTimeProvider.PoolIdleWindow);

        // `>= _nKeepAliveExpireTime` [n_cst_thread_trans_pool.sru:L215]: exactly the window EXPIRES.
        Assert.True(
            clock.GetElapsedTime(start, clock.GetTimestamp()) >= FakeTimeProvider.PoolIdleWindow);
    }

    /// <summary>The start instant is settable, at construction and by reset.</summary>
    [Fact]
    public void TheClockStartInstantIsSettable()
    {
        FakeTimeProvider clock = new(AlternateStart);

        Assert.Equal(AlternateStart, clock.Start);
        Assert.Equal(AlternateStart, clock.GetUtcNow());

        clock.AdvanceMilliseconds(500);
        Assert.Equal(TimeSpan.FromMilliseconds(500), clock.Elapsed);

        clock.Reset(FakeTimeProvider.DefaultStart);

        Assert.Equal(FakeTimeProvider.DefaultStart, clock.Start);
        Assert.Equal(FakeTimeProvider.DefaultStart, clock.GetUtcNow());
        Assert.Equal(TimeSpan.Zero, clock.Elapsed);
        Assert.Equal(0, clock.AdvanceCount);
    }

    /// <summary>A negative advance is refused, because the legacy source is a monotonic counter.</summary>
    [Fact]
    public void TheClockRefusesToRunBackwards()
    {
        FakeTimeProvider clock = new();

        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => clock.Advance(TimeSpan.FromMilliseconds(-1)));
    }

    /// <summary>
    /// A timer created through the fake clock fires only when the clock is advanced past its due
    /// instant, and a periodic timer fires once per elapsed period.
    /// </summary>
    /// <remarks>
    /// WITHOUT THIS, A SCHEDULED CONSUMER WOULD STILL DEPEND ON WALL TIME. The base
    /// <see cref="TimeProvider.CreateTimer"/> hands back a real system timer, so a suite for the pool's
    /// idle sweep would either wait for real seconds or observe nothing at all.
    /// </remarks>
    [Fact]
    public void ClockTimersFireOnlyWhenTheClockIsAdvanced()
    {
        FakeTimeProvider clock = new();
        int fires = 0;

        using ITimer timer = clock.CreateTimer(
            _ => fires++,
            state: null,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(100));

        Assert.Equal(1, clock.TimerCount);
        Assert.Equal(0, fires);

        clock.AdvanceMilliseconds(99);
        Assert.Equal(0, fires);

        clock.AdvanceMilliseconds(1);
        Assert.Equal(1, fires);

        // ONE ADVANCE MAY CROSS SEVERAL PERIODS, and a timer that fired once per advance would
        // under-report - so an assertion about how many sweeps happened would fail against a correct
        // implementation.
        clock.AdvanceMilliseconds(300);
        Assert.Equal(4, fires);
    }

    /// <summary>A disposed timer stops firing and stops being driven.</summary>
    [Fact]
    public void ADisposedClockTimerStopsFiring()
    {
        FakeTimeProvider clock = new();
        int fires = 0;

        FakeTimeProviderTimer timer = (FakeTimeProviderTimer)clock.CreateTimer(
            _ => fires++,
            state: null,
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(10));

        Assert.True(timer.IsArmed);

        clock.AdvanceMilliseconds(10);
        Assert.Equal(1, fires);

        timer.Dispose();

        Assert.True(timer.IsDisposed);
        Assert.False(timer.IsArmed);
        Assert.Equal(0, clock.TimerCount);

        clock.AdvanceMilliseconds(1_000);
        Assert.Equal(1, fires);
        Assert.Equal(1, timer.FireCount);
    }

    /// <summary>An infinite due time leaves a timer unarmed however far the clock advances.</summary>
    [Fact]
    public void AnInfiniteDueTimeLeavesAClockTimerUnarmed()
    {
        FakeTimeProvider clock = new();
        int fires = 0;

        using FakeTimeProviderTimer timer = (FakeTimeProviderTimer)clock.CreateTimer(
            _ => fires++,
            state: null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);

        Assert.False(timer.IsArmed);

        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(0, fires);

        Assert.True(timer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan));
        clock.AdvanceMilliseconds(0);

        // A ZERO DUE TIME IS ALREADY DUE, and an infinite period means one shot - so exactly one fire,
        // however much further the clock moves.
        Assert.Equal(1, fires);

        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(1, fires);
    }

    // ==============================================================================================
    //  SEAM 2 - THE REDACTOR
    // ==============================================================================================

    /// <summary>The recording redactor captures every statement, in call order, and masks each one.</summary>
    [Fact]
    public void TheRecordingRedactorCapturesStatementsInCallOrder()
    {
        RecordingSqlRedactor redactor = new();

        Assert.Equal(0, redactor.CallCount);
        Assert.Null(redactor.LastStatement);

        string first = redactor.Redact("SELECT * FROM COMPANY WHERE id = 7");
        string second = redactor.Redact("DELETE FROM COMPANY WHERE id = 8");

        Assert.Equal(RecordingSqlRedactor.MaskedMarker, first);
        Assert.Equal(RecordingSqlRedactor.MaskedMarker, second);
        Assert.Equal(2, redactor.CallCount);
        Assert.Equal(
            ["SELECT * FROM COMPANY WHERE id = 7", "DELETE FROM COMPANY WHERE id = 8"],
            redactor.Statements);
        Assert.Equal(1, redactor.OrdinalOf("SELECT * FROM COMPANY WHERE id = 7"));
        Assert.Equal(2, redactor.OrdinalOf("DELETE FROM COMPANY WHERE id = 8"));
        Assert.Equal(0, redactor.OrdinalOf("never presented"));
        Assert.True(redactor.Observed("DELETE FROM COMPANY WHERE id = 8"));
        Assert.Equal("DELETE FROM COMPANY WHERE id = 8", redactor.LastStatement);

        redactor.Clear();
        Assert.Equal(0, redactor.CallCount);
    }

    /// <summary>
    /// Null and empty are recorded and answered with the empty string, and masking is idempotent.
    /// </summary>
    /// <remarks>
    /// RECORDING AN EMPTY STATEMENT IS NOT PEDANTRY: "the caller routed an empty statement through the
    /// seam" and "the caller bypassed the seam" are different facts, and only the recording tells them
    /// apart.
    /// </remarks>
    [Fact]
    public void TheRecordingRedactorHandlesNullEmptyAndAlreadyMaskedText()
    {
        RecordingSqlRedactor redactor = new();

        Assert.Equal(string.Empty, redactor.Redact(null));
        Assert.Equal(string.Empty, redactor.Redact(string.Empty));
        Assert.Equal(2, redactor.CallCount);

        string masked = redactor.Redact("UPDATE COMPANY SET age = 41");
        Assert.Equal(RecordingSqlRedactor.MaskedMarker, masked);
        Assert.Equal(masked, redactor.Redact(masked));
    }

    /// <summary>A scripted mask replaces the default marker without disturbing the recording.</summary>
    [Fact]
    public void TheRecordingRedactorHonoursAScriptedMask()
    {
        RecordingSqlRedactor redactor = new()
        {
            Mask = static statement => $"masked:{statement.Length}",
        };

        Assert.Equal("masked:6", redactor.Redact("SELECT"));
        Assert.Equal(["SELECT"], redactor.Statements);
    }

    // ==============================================================================================
    //  SEAM 3 - THE ORDERED DESCRIBE AND MODIFY RECORDER
    // ==============================================================================================

    /// <summary>
    /// Describe and modify calls arriving through different interfaces land in ONE ordered log, so
    /// "before" is expressible across contracts.
    /// </summary>
    /// <remarks>
    /// THE CROSSTAB GUARD ORDERING IS THE MOTIVATING CASE. <c>DataWindow.NoUserPrompt=yes</c> must be
    /// written before the later operations, and a content-only assertion would pass whichever order they
    /// happened in.
    /// </remarks>
    [Fact]
    public void TheCarrierSurfaceRecordsEveryVerbInOneOrderedLog()
    {
        ScriptedCarrierSurface surface = new()
        {
            ColumnCount = 6,
            UpdateTableName = "COMPANY",
        };

        surface.DescribeAnswers["DataWindow.NoUserPrompt"] = "no";

        IFullStateCarrierSurface fullState = surface;
        IIdentityColumnMetadata identity = surface;
        IUpdateTargetMetadata metadata = surface;

        Assert.Equal("no", fullState.Describe("DataWindow.NoUserPrompt"));
        Assert.Equal(
            ScriptedCarrierSurface.ModifySucceeded,
            fullState.Modify("DataWindow.NoUserPrompt=yes"));
        Assert.Equal("COMPANY", identity.DescribeUpdateTable());
        Assert.Equal(6, metadata.GetColumnCount());
        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, fullState.SetSort("age A salary A "));

        IReadOnlyList<RecordedCarrierCall> calls = surface.Calls;

        Assert.Equal(5, calls.Count);
        Assert.Equal([1, 2, 3, 4, 5], calls.Select(static call => call.Ordinal));
        Assert.Equal(
            [
                CarrierCallKind.Describe,
                CarrierCallKind.Modify,
                CarrierCallKind.DescribeUpdateTable,
                CarrierCallKind.GetColumnCount,
                CarrierCallKind.SetSort,
            ],
            calls.Select(static call => call.Kind));

        Assert.True(
            surface.Precedes(
                CarrierCallKind.Modify,
                "DataWindow.NoUserPrompt=yes",
                CarrierCallKind.SetSort,
                "age A"));

        Assert.False(
            surface.Precedes(
                CarrierCallKind.SetSort,
                "age A",
                CarrierCallKind.Modify,
                "DataWindow.NoUserPrompt=yes"));

        // ABSENCE IS NOT ORDER: a call that never happened must make the assertion fail, not pass.
        Assert.False(
            surface.Precedes(
                CarrierCallKind.Modify,
                "never written",
                CarrierCallKind.SetSort,
                "age A"));

        Assert.Equal(1, surface.CountOf(CarrierCallKind.Modify));
        Assert.Equal(["DataWindow.NoUserPrompt=yes"], surface.ModifyScripts);
        Assert.Equal(2, surface.OrdinalOf(CarrierCallKind.Modify, "DataWindow.NoUserPrompt=yes"));

        surface.Clear();
        Assert.Empty(surface.Calls);
    }

    /// <summary>Ordering INSIDE one multi-line modification script is expressible too.</summary>
    /// <remarks>
    /// THE UPDATE-PREPARE CASE. The reset lines must precede the per-column enables
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L104-L108</c>]; reversed,
    /// the reset would erase the enables and the generated statement would carry no updatable columns.
    /// </remarks>
    [Fact]
    public void TheCarrierSurfaceExposesOrderWithinOneModificationScript()
    {
        string script = string.Join(
            UpdateWhereBuilder.LineSeparator,
            "#1.Update='no'",
            "#1.Key='no'",
            "#1.Identity='no'",
            "#1.Update='yes'",
            "#1.Key='yes'");

        Assert.Equal(1, ScriptedCarrierSurface.LineOrdinalOf(script, "#1.Update='no'"));
        Assert.Equal(4, ScriptedCarrierSurface.LineOrdinalOf(script, "#1.Update='yes'"));
        Assert.Equal(0, ScriptedCarrierSurface.LineOrdinalOf(script, "#2.Update='yes'"));

        Assert.True(ScriptedCarrierSurface.LinePrecedes(script, "#1.Update='no'", "#1.Update='yes'"));
        Assert.False(ScriptedCarrierSurface.LinePrecedes(script, "#1.Update='yes'", "#1.Update='no'"));
        Assert.False(ScriptedCarrierSurface.LinePrecedes(script, "#1.Update='no'", "#2.Update='yes'"));
    }

    /// <summary>
    /// Describe falls back to the not-applicable sentinel, modify reports failure as text, and sort and
    /// filter results are scriptable to a non-success value.
    /// </summary>
    /// <remarks>
    /// THE FAILURE ARMS ARE OTHERWISE UNREACHABLE. <c>FullStateCodec</c> reports <c>SetSort: </c> and
    /// <c>SetFilter: </c> only when the call answers something other than the datastore success value,
    /// and a real DataWindow cannot be made to fail those on demand without a runtime.
    /// </remarks>
    [Fact]
    public void TheCarrierSurfaceScriptsItsFallbacksAndItsFailures()
    {
        ScriptedCarrierSurface surface = new()
        {
            ModifyError = "syntax error",
            SetSortResult = DataWindowBufferStore.DataStoreFailure,
            SetFilterResult = 0L,
            ColumnIdFallback = 0,
        };

        IFullStateCarrierSurface fullState = surface;
        IUpdateTargetMetadata metadata = surface;

        Assert.Equal(ScriptedCarrierSurface.DescribeUnknown, fullState.Describe("DataWindow.Anything"));

        surface.DescribeFallback = ScriptedCarrierSurface.DescribeError;
        Assert.Equal(ScriptedCarrierSurface.DescribeError, fullState.Describe("DataWindow.Anything"));

        Assert.Equal("syntax error", fullState.Modify("DataWindow.Table.UpdateTable='COMPANY'"));

        surface.ModifyErrors["DataWindow.Table.UpdateTable='OTHER'"] = ScriptedCarrierSurface.ModifySucceeded;
        Assert.Equal(
            ScriptedCarrierSurface.ModifySucceeded,
            fullState.Modify("DataWindow.Table.UpdateTable='OTHER'"));

        Assert.NotEqual(DataWindowBufferStore.DataStoreSuccess, fullState.SetSort("age A "));
        Assert.NotEqual(DataWindowBufferStore.DataStoreSuccess, fullState.SetFilter("age > 40"));

        // ZERO IS THE LEGACY FAILURE VALUE FOR A COLUMN IDENTIFIER, not a valid identifier
        // [n_cst_thread_task_sqlupdate.sru:L118-L122].
        Assert.Equal(0, metadata.GetColumnId("id.Id"));

        surface.ColumnIds["id.Id"] = 1;
        Assert.Equal(1, metadata.GetColumnId("id.Id"));
        Assert.Equal("no", metadata.DescribeUpdateKeyInPlace());
    }

    // ==============================================================================================
    //  SEAM 4 - THE POOLED TRANSACTION
    // ==============================================================================================

    /// <summary>
    /// The three legacy readings of one <c>SQLCode</c> field stay distinguishable, which is the entire
    /// reason this double exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A COLLAPSED DOUBLE WOULD AGREE WITH ITSELF AND DISAGREE WITH THE ORACLE. Two rows of this table
    /// carry the whole argument:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <c>100</c> is NON-ZERO yet the state test does NOT flag it, because the legacy condition is
    /// <c>SQLCode &lt;&gt; 0 and SQLCode &lt;&gt; 100</c> - a not-found reads as SUCCESS. A port that
    /// tested only for non-zero would report an error on every empty result.
    /// </item>
    /// <item>
    /// <c>5</c> - a positive driver warning - flags the state test but fires NEITHER the defensive
    /// override, which demands exactly <c>-1</c>, NOR the failure predicate, which demands negative. One
    /// value, three different answers; that is the distinction this double preserves.
    /// </item>
    /// </list>
    /// </remarks>
    [Theory]
    [InlineData(0L, false, false, false)]
    [InlineData(ScriptedPooledTransaction.NotFoundSqlCode, false, false, false)]
    [InlineData(ScriptedPooledTransaction.DefensiveOverrideSqlCode, true, true, true)]
    [InlineData(5L, true, false, false)]
    [InlineData(-99L, true, false, true)]
    public void TheTransactionKeepsTheThreeSqlCodeReadingsDistinguishable(
        long sqlCode,
        bool expectedStateTestFlagsAnError,
        bool expectedDefensiveOverride,
        bool expectedFailure)
    {
        using ScriptedPooledTransaction transaction = new() { SqlCode = sqlCode };

        Assert.Equal(expectedStateTestFlagsAnError, transaction.IsSqlCodeNonZeroExcludingNotFound());
        Assert.Equal(expectedDefensiveOverride, transaction.IsSqlCodeDefensiveOverride());
        Assert.Equal(expectedFailure, transaction.IsSqlFailed());
        Assert.Equal(expectedFailure, transaction.IsFailed());
        Assert.Equal(!expectedFailure, transaction.IsSqlSucceeded());
    }

    /// <summary>
    /// The failure predicate is settable INDEPENDENTLY of the code, so a suite can prove a consumer
    /// consulted the predicate rather than re-deriving failure from the field.
    /// </summary>
    [Fact]
    public void TheTransactionPredicateIsIndependentOfTheRawCode()
    {
        using ScriptedPooledTransaction transaction = new()
        {
            SqlCode = 0L,
            FailurePredicate = static _ => true,
        };

        Assert.True(transaction.IsSqlFailed());
        Assert.True(transaction.IsFailed());
        Assert.False(transaction.IsSqlSucceeded());
        Assert.False(transaction.IsSqlCodeNonZeroExcludingNotFound());
        Assert.False(transaction.IsSqlCodeDefensiveOverride());

        transaction.SuccessPredicate = static _ => true;

        // BOTH PREDICATES TRUE AT ONCE IS DELIBERATELY REACHABLE. The oracle's two predicates are
        // complementary, so a consumer that asked the WRONG one of the two is only detectable if a
        // suite can script them apart.
        Assert.True(transaction.IsSqlFailed());
        Assert.True(transaction.IsSqlSucceeded());
    }

    /// <summary>
    /// The transaction records its interactions in order, tracks connection state, and reports its
    /// dialect and its captured error without inventing a statement.
    /// </summary>
    [Fact]
    public void TheTransactionRecordsItsInteractionsInOrder()
    {
        using ScriptedPooledTransaction transaction = new()
        {
            SqlDbCode = -803L,
            SqlErrText = "unique constraint violated",
            DbType = DatabaseType.DbtOracle,
        };

        Assert.Equal(RetCode.OK, transaction.Connect(TestContext.Current.CancellationToken));
        Assert.True(transaction.IsConnected());

        Assert.Equal(RetCode.OK, transaction.Exec("UPDATE COMPANY SET age = 41", TestContext.Current.CancellationToken));
        Assert.Equal(RetCode.OK, transaction.Commit());
        Assert.Equal(RetCode.OK, transaction.Rollback());
        Assert.Equal(RetCode.OK, transaction.Disconnect());

        Assert.False(transaction.IsConnected());
        Assert.True(transaction.Precedes(TransactionCallKind.Connect, TransactionCallKind.Exec));
        Assert.True(transaction.Precedes(TransactionCallKind.Exec, TransactionCallKind.Commit));
        Assert.True(transaction.Precedes(TransactionCallKind.Commit, TransactionCallKind.Rollback));
        Assert.False(transaction.Precedes(TransactionCallKind.Rollback, TransactionCallKind.Commit));
        Assert.Equal(["UPDATE COMPANY SET age = 41"], transaction.ExecutedStatements);
        Assert.Equal(1, transaction.CountOf(TransactionCallKind.Exec));
        Assert.Equal(DatabaseType.DbtOracle, transaction.GetDbType());

        DbErrorData captured = transaction.CaptureError();

        Assert.Equal(-803L, captured.SqlDbCode);
        Assert.Equal("unique constraint violated", captured.SqlErrText);

        // NO STATEMENT IS SYNTHESIZED, matching DbErrorData.FromTransaction and the legacy argument
        // lists, all of which pass "", Primary! and 0 in these three positions (C-F).
        Assert.Equal(string.Empty, captured.SqlSyntax);
        Assert.Equal(DwBuffer.Primary, captured.Buffer);
        Assert.Equal(0L, captured.Row);

        // NO ENGINE IS REACHABLE THROUGH A DOUBLE, and answering the probe honestly is the point.
        Assert.False(transaction.TryGetEngineCapability(out IDisposable? capability));
        Assert.Null(capability);
    }

    /// <summary>
    /// The two update hooks are observable separately, so a suite can show the after-update hook saw the
    /// UNRECONCILED result.
    /// </summary>
    /// <remarks>
    /// THE HOOK FIRES BEFORE THE DEFENSIVE OVERRIDE. The hook sits at
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L206</c>] and the rewrite at
    /// [<c>:L208</c>], so a hook that observed the reconciled value would be a behavioural change
    /// dressed as tidiness.
    /// </remarks>
    [Fact]
    public void TheTransactionExposesBothUpdateHooksSeparately()
    {
        using ScriptedPooledTransaction transaction = new() { BeforeUpdateResult = RetCode.PREVENT };

        Assert.Null(transaction.AfterUpdateObserved);

        Assert.Equal(RetCode.PREVENT, transaction.OnBeforeUpdate());

        // THE CLAIMED SUCCESS THE ORACLE HANDS THE HOOK IS 1, NOT THE RETURN-CODE ZERO: the legacy
        // update call reports success as 1 [n_cst_thread_task_sqlupdate.sru:L172-L251].
        transaction.OnAfterUpdate(1L);

        Assert.Equal(1, transaction.BeforeUpdateCalls);
        Assert.Equal(1, transaction.AfterUpdateCalls);
        Assert.Equal(1L, transaction.AfterUpdateObserved);
        Assert.True(transaction.Precedes(TransactionCallKind.BeforeUpdate, TransactionCallKind.AfterUpdate));
    }

    /// <summary>
    /// Stamping provider state overwrites every field, and clearing state resets them without disturbing
    /// the recording.
    /// </summary>
    [Fact]
    public void TheTransactionStampsAndClearsItsProviderState()
    {
        using ScriptedPooledTransaction transaction = new();

        transaction.StampSqlState(new SqlState(-1L, -803L, 0L, "constraint", "returned"));

        Assert.Equal(-1L, transaction.SqlCode);
        Assert.Equal(-803L, transaction.SqlDbCode);
        Assert.Equal("constraint", transaction.SqlErrText);
        Assert.Equal("returned", transaction.SqlReturnData);
        Assert.NotNull(transaction.StampedState);
        Assert.True(transaction.IsSqlCodeDefensiveOverride());

        transaction.ClearState();

        Assert.Equal(0L, transaction.SqlCode);
        Assert.Equal(0L, transaction.SqlDbCode);
        Assert.Equal(string.Empty, transaction.SqlErrText);
        Assert.Equal(1, transaction.ClearStateCalls);
        Assert.True(transaction.Precedes(TransactionCallKind.StampSqlState, TransactionCallKind.ClearState));
    }

    /// <summary>
    /// Applying a descriptor records only that it happened and the non-credential DBMS name, never the
    /// write-only credential.
    /// </summary>
    /// <remarks>
    /// <c>TransactionData.LogPass</c> is write-only by contract - never echoed in a response, never
    /// logged - so this double deliberately retains no way for a test to read one back (C-F).
    /// </remarks>
    [Fact]
    public void TheTransactionRetainsNoCredentialFromAnAppliedDescriptor()
    {
        using ScriptedPooledTransaction transaction = new();

        TransactionData descriptor = new()
        {
            Dbms = "SQLITE",
            Database = "pfw-parity",
        };

        Assert.Equal(RetCode.OK, transaction.ApplyTransactionData(descriptor));
        Assert.Equal(1, transaction.ApplyTransactionDataCalls);
        Assert.Equal("SQLITE", transaction.AppliedDbms);
    }

    /// <summary>Disposal is idempotent and observable, and reports the transaction destroyed.</summary>
    [Fact]
    public void TheTransactionDisposesIdempotently()
    {
        ScriptedPooledTransaction transaction = new();

        Assert.Equal(RetCode.OK, transaction.Connect(TestContext.Current.CancellationToken));
        Assert.False(transaction.IsDestroyed);

        transaction.Dispose();
        transaction.Dispose();

        Assert.True(transaction.Disposed);
        Assert.True(transaction.IsDestroyed);
        Assert.False(transaction.IsConnected());
        Assert.Equal(1, transaction.CountOf(TransactionCallKind.Dispose));
    }

    // ==============================================================================================
    //  SEAM 5 - THE OPTIONS BUILDER
    // ==============================================================================================

    /// <summary>
    /// The builder produces the application's own documented defaults and a graph the real validator
    /// accepts.
    /// </summary>
    /// <remarks>
    /// A BARE <c>new PersistenceOptions()</c> DOES NOT VALIDATE: the authority and the audience are both
    /// required and both default to empty, and the permitted-caller list carries a minimum length over an
    /// empty collection. Asserting the built graph against the APPLICATION's validator is what keeps this
    /// builder honest as the validator tightens - a fourth requirement added tomorrow fails here rather
    /// than at deployment.
    /// </remarks>
    [Fact]
    public void TheOptionsBuilderProducesADocumentedValidGraph()
    {
        PersistenceOptionsBuilder builder = new();
        PersistenceOptions options = builder.Build();

        Assert.Equal(10_000, options.Query.ChunkSize);
        Assert.True(options.Query.PageCounting);
        Assert.False(options.Query.Cache);
        Assert.Equal(0L, options.Query.MaxRows);
        Assert.False(options.Query.Paged);

        Assert.Equal("DELETE", options.Sqlite.Journal);
        Assert.Equal("rwc", options.Sqlite.Mode);
        Assert.Null(options.Sqlite.Check);

        Assert.False(options.TransactionPool.KeepAlive);
        Assert.Equal(
            TransactionPoolOptions.DefaultKeepAliveExpireMilliseconds,
            options.TransactionPool.ResolveKeepAliveExpireMilliseconds());
        Assert.Equal(
            TransactionPoolOptions.DefaultIdleSweepIntervalSeconds,
            options.TransactionPool.IdleSweepIntervalSeconds);

        Assert.Equal(PersistenceOptionsBuilder.DefaultAuthority, options.Jwt.Authority);
        Assert.Equal(PersistenceOptionsBuilder.DefaultAudience, options.Jwt.Audience);
        Assert.Equal(
            PersistenceOptionsBuilder.DefaultPermittedCaller,
            Assert.Single(options.Jwt.PermittedCallers));

        // THE FOUR TOKEN-VALIDATION CHECKS STAY ON. The validator REFUSES a false on any of them, so a
        // graph this builder produces can never model a boundary with a check disabled (C-G).
        Assert.True(options.Jwt.ValidateIssuer);
        Assert.True(options.Jwt.ValidateAudience);
        Assert.True(options.Jwt.ValidateLifetime);
        Assert.True(options.Jwt.ValidateIssuerSigningKey);

        ValidateOptionsResult validation = builder.Validate();

        Assert.True(validation.Succeeded, validation.FailureMessage);
        Assert.Same(options, builder.BuildOptions().Value);
        Assert.True(PersistenceOptionsBuilder.DefaultOptions().Value.Jwt.Audience.Length > 0);
    }

    /// <summary>
    /// The legacy 30,000 ms idle window is what a non-positive configured expiry resolves to, and the
    /// clock's own constant agrees with it.
    /// </summary>
    /// <remarks>
    /// TWO INDEPENDENT STATEMENTS OF ONE ORACLE VALUE, CROSS-CHECKED. The application declares the
    /// default and <see cref="FakeTimeProvider.PoolIdleWindow"/> declares the interval a suite advances
    /// across; if either drifted from <c>KEEPALIVE_EXPIRE = 30000</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L53</c>] the suite would advance
    /// past the wrong boundary and still pass, so they are asserted equal here.
    /// </remarks>
    [Fact]
    public void TheOptionsBuilderAgreesWithTheClockAboutTheIdleWindow()
    {
        PersistenceOptions options = new PersistenceOptionsBuilder()
            .WithKeepAlive(enabled: true)
            .Build();

        Assert.True(options.TransactionPool.KeepAlive);
        Assert.Equal(
            (int)FakeTimeProvider.PoolIdleWindow.TotalMilliseconds,
            options.TransactionPool.ResolveKeepAliveExpireMilliseconds());

        PersistenceOptions configured = new PersistenceOptionsBuilder()
            .WithKeepAlive(enabled: true, expireSeconds: 5d)
            .Build();

        Assert.Equal(5_000, configured.TransactionPool.ResolveKeepAliveExpireMilliseconds());
    }

    /// <summary>The builder never carries a credential, and offers no way to set one.</summary>
    /// <remarks>
    /// THE ABSENCE IS STRUCTURAL, NOT A DEFAULT (C-F, C-G). <c>SqliteOptions.Password</c> stays
    /// <see langword="null"/> because no verb on the builder can change it, and there is no signing-key
    /// or token-minting member on the type at all - Security is the sole issuer and Persistence holds
    /// verification material only.
    /// </remarks>
    [Fact]
    public void TheOptionsBuilderCarriesNoCredential()
    {
        PersistenceOptions options = new PersistenceOptionsBuilder()
            .WithDataDirectory("/tmp/pfw-parity-not-created")
            .WithDatabaseFileName("test.db")
            .Build();

        Assert.Null(options.Sqlite.Password);
        Assert.DoesNotContain("password", options.Jwt.Authority, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".invalid", options.Jwt.Authority, StringComparison.Ordinal);

        // THE RECORDED DIRECTORY IS A STRING AND NOTHING MORE. Nothing above created, probed or wrote a
        // path, which is what keeps a paired characterization capture valid (C-E, AAP 0.6.7).
        //
        // AND THIS ASSERTION DELIBERATELY DOES NOT PROBE THE PATH EITHER. Calling Directory.Exists here
        // would be the only filesystem access in the whole file, in the very test asserting that there is
        // none - and it would also be a flake, since a co-resident clone could have created the path for
        // its own reasons. The guarantee is STRUCTURAL rather than observed: the builder's body is
        // property assignments only, so there is no code path through it that can reach a disk.
        Assert.Equal("/tmp/pfw-parity-not-created", options.Sqlite.DataDirectory);
        Assert.Equal("test.db", options.Sqlite.DatabaseFileName);
    }

    /// <summary>Every fluent verb reaches the option it names.</summary>
    [Fact]
    public void TheOptionsBuilderFluentVerbsReachTheirOptions()
    {
        DataObjectOptions dataObject = new()
        {
            Name = "dw_sqlite",
            SqlSelect = "SELECT * FROM COMPANY",
            UpdateTable = "COMPANY",
            UpdateWhere = 1L,
            UpdateKeyInPlace = false,
        };

        PersistenceOptions options = new PersistenceOptionsBuilder()
            .WithChunkSize(2_000)
            .WithMaxRows(50L)
            .WithPageCounting(false)
            .WithCache(true)
            .WithPaging(pageIndex: 2, pageSize: 25)
            .WithJournal("WAL")
            .WithMode("ro")
            .WithIntegrityCheck(SqliteIntegrityCheckMode.Quick)
            .WithIdleSweepIntervalSeconds(0d)
            .WithTransactionClassName("ScriptedPooledTransaction")
            .WithAuthority("https://issuer.invalid")
            .WithAudience("powerframework-persistence-parity")
            .WithPermittedCaller("powerframework-gateway")
            .WithDataObject(dataObject)
            .Build();

        Assert.Equal(2_000, options.Query.ChunkSize);
        Assert.Equal(50L, options.Query.MaxRows);
        Assert.False(options.Query.PageCounting);
        Assert.True(options.Query.Cache);
        Assert.True(options.Query.Paged);
        Assert.Equal(2, options.Query.PageIndex);
        Assert.Equal(25, options.Query.PageSize);
        Assert.Equal("WAL", options.Sqlite.Journal);
        Assert.Equal("ro", options.Sqlite.Mode);
        Assert.Equal(SqliteIntegrityCheckMode.Quick, options.Sqlite.Check);
        Assert.Null(options.TransactionPool.ResolveIdleSweepInterval());
        Assert.Equal("ScriptedPooledTransaction", options.TransactionPool.TransactionClassName);
        Assert.Equal("https://issuer.invalid", options.Jwt.Authority);
        Assert.Equal("powerframework-persistence-parity", options.Jwt.Audience);

        // ADDITIVE, NOT REPLACING: the default caller is what keeps the graph valid, so a supplied one
        // joins it rather than displacing it.
        Assert.Equal(
            [PersistenceOptionsBuilder.DefaultPermittedCaller, "powerframework-gateway"],
            options.Jwt.PermittedCallers);
        Assert.Same(dataObject, Assert.Single(options.DataObjects));
    }

    // ==============================================================================================
    //  SEAM 6 - THE CARRIER, ITS PARENT TASK AND ITS CODEC
    // ==============================================================================================

    /// <summary>
    /// A retrieved-then-edited row carries a CURRENT and an ORIGINAL value per cell, which is what makes
    /// <c>updatewhere=1</c> expressible at all.
    /// </summary>
    [Fact]
    public void TheCarrierHoldsCurrentAndOriginalValuesPerCell()
    {
        FakeDataWindowCarrier carrier = new();
        carrier.SetColumnNames(["id", "name", "age", "address", "salary", "birth"]);

        long row = carrier.SeedEditedRow(
            DwBuffer.Primary,
            ItemStatus.DataModified,
            [(1, 1L), (2, "PowerFramework"), (3, 40L)],
            (3, 41L));

        Assert.Equal(1L, row);
        Assert.Equal(41L, carrier.GetItemValue(row, 3, DwBuffer.Primary));
        Assert.Equal(40L, carrier.GetItemOriginalValue(row, 3, DwBuffer.Primary));

        // AN UNEDITED COLUMN REPORTS ITS CURRENT VALUE AS ITS ORIGINAL, matching CarrierRow's own
        // fallback - the where clause of an unedited column compares equal values, it does not compare
        // against null.
        Assert.Equal("PowerFramework", carrier.GetItemOriginalValue(row, 2, DwBuffer.Primary));

        // A FRESHLY RETRIEVED ROW HAS EQUAL CURRENT AND ORIGINAL VALUES AND READS AS NotModified!.
        long untouched = carrier.SeedRetrievedRow(DwBuffer.Primary, (1, 2L), (3, 33L));

        Assert.Equal(33L, carrier.GetItemValue(untouched, 3, DwBuffer.Primary));
        Assert.Equal(33L, carrier.GetItemOriginalValue(untouched, 3, DwBuffer.Primary));
        Assert.Equal(
            ItemStatus.NotModified,
            carrier.GetItemStatus(untouched, ItemStatusMachine.RowStatusColumn, DwBuffer.Primary));
    }

    /// <summary>
    /// The ROW status and a COLUMN status are separate facts, and the row-status sentinel is refused
    /// where a column number is required.
    /// </summary>
    /// <remarks>
    /// COLUMN 0 IS NOT A COLUMN. The legacy reads a row's status with index <c>0</c> and a column's with
    /// a real index two lines away, so a double that let the two be written interchangeably would allow a
    /// suite to conflate them and still pass.
    /// </remarks>
    [Fact]
    public void TheCarrierKeepsRowStatusAndColumnStatusDistinct()
    {
        FakeDataWindowCarrier carrier = new();
        long row = carrier.SeedRetrievedRow(DwBuffer.Primary, (1, 1L), (3, 40L));

        carrier.SetRowStatus(row, DwBuffer.Primary, ItemStatus.NewModified);
        carrier.SetColumnStatus(row, 3, DwBuffer.Primary, ItemStatus.DataModified);

        Assert.Equal(
            ItemStatus.NewModified,
            carrier.GetItemStatus(row, ItemStatusMachine.RowStatusColumn, DwBuffer.Primary));
        Assert.Equal(ItemStatus.DataModified, carrier.GetItemStatus(row, 3, DwBuffer.Primary));
        Assert.Equal(ItemStatus.NotModified, carrier.GetItemStatus(row, 1, DwBuffer.Primary));

        ArgumentOutOfRangeException refused = Assert.Throws<ArgumentOutOfRangeException>(
            () => carrier.SetColumnStatus(
                row,
                ItemStatusMachine.RowStatusColumn,
                DwBuffer.Primary,
                ItemStatus.DataModified));

        Assert.Equal("columnNumber", refused.ParamName);
    }

    /// <summary>
    /// The <c>Filter!</c> buffer keeps the order it was seeded in, so the legacy BACKWARD walk yields
    /// source order and a forward walk does not.
    /// </summary>
    /// <remarks>
    /// <b>THIS IS THE ASSERTION RISK R9 EXISTS FOR.</b> The identity round trip walks <c>Primary!</c>
    /// forward and <c>Filter!</c> backward because the filter buffer's row order is inverted relative to
    /// the source
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L235-L238</c>]. If this
    /// double normalised the order, the backward and forward walks would be indistinguishable and a port
    /// that "corrected" the direction would pass - while producing wrong identity values that a
    /// row-count assertion still accepts.
    /// </remarks>
    [Fact]
    public void TheCarrierPreservesTheInvertedFilterBufferOrder()
    {
        FakeDataWindowCarrier carrier = new();

        // SEEDED IN THE INVERTED ORDER THE LEGACY BUFFER ACTUALLY HOLDS: source rows 101, 102, 103
        // appear as 103, 102, 101.
        _ = carrier.SeedRetrievedRows(
            DwBuffer.Filter,
            [(1, 103L)],
            [(1, 102L)],
            [(1, 101L)]);

        for (long row = 1L; row <= carrier.FilteredCount(); row++)
        {
            carrier.SetRowStatus(row, DwBuffer.Filter, ItemStatus.NewModified);
        }

        // STORAGE ORDER IS UNTOUCHED - the double reordered nothing.
        Assert.Equal(
            [103L, 102L, 101L],
            carrier.ColumnValuesInStorageOrder(DwBuffer.Filter, 1).Cast<long>());

        IReadOnlyList<long?> walked = IdentityColumnResolver.CollectFilterValues(carrier, 1);

        // THE BACKWARD WALK YIELDS SOURCE ORDER, which is only true because the storage order is
        // inverted and nothing normalised it.
        Assert.Equal([101L, 102L, 103L], walked.Select(static value => value!.Value));
        Assert.NotEqual(
            carrier.ColumnValuesInStorageOrder(DwBuffer.Filter, 1).Cast<long>(),
            walked.Select(static value => value!.Value));
    }

    /// <summary>
    /// The identity surface reports its counts and records which of the two <c>GetItemNumber</c>
    /// overloads a consumer used.
    /// </summary>
    /// <remarks>
    /// THE OVERLOAD IS THE EVIDENCE. <c>CollectPrimaryValues</c> uses the two-argument form and
    /// <c>CollectFilterValues</c> the four-argument one, and only the four-argument form can ask for an
    /// original value - which is the legacy <c>org</c> flag of
    /// <c>ws_objects/pfw.utility.sqlite.pbl.src/sqlitegetitemdouble.srf</c>. Recording the distinction is
    /// how a suite shows a concurrency payload was built from original values.
    /// </remarks>
    [Fact]
    public void TheCarrierReportsCountsAndRecordsWhichReadOverloadWasUsed()
    {
        FakeDataWindowCarrier carrier = new()
        {
            RowsInserted = 2L,
            RowsUpdated = 3L,
            RowsDeleted = 1L,
        };

        long primary = carrier.SeedRetrievedRow(DwBuffer.Primary, (1, 7L));
        carrier.SetRowStatus(primary, DwBuffer.Primary, ItemStatus.NewModified);

        long filtered = carrier.SeedRetrievedRow(DwBuffer.Filter, (1, 8L));
        carrier.SetRowStatus(filtered, DwBuffer.Filter, ItemStatus.NewModified);

        UpdateRowCounts counts = IdentityColumnResolver.ReadCounts(carrier);

        Assert.Equal(2L, counts.Inserted);
        Assert.Equal(3L, counts.Updated);
        Assert.Equal(1L, counts.Deleted);

        Assert.Equal([7L], IdentityColumnResolver.CollectPrimaryValues(carrier, 1).Select(static v => v!.Value));
        Assert.Equal([8L], IdentityColumnResolver.CollectFilterValues(carrier, 1).Select(static v => v!.Value));

        IReadOnlyList<RecordedValueRead> reads = carrier.ValueReads;

        Assert.Equal(2, reads.Count);
        Assert.False(reads[0].FourArgument);
        Assert.Equal(DwBuffer.Primary, reads[0].Buffer);
        Assert.True(reads[1].FourArgument);
        Assert.Equal(DwBuffer.Filter, reads[1].Buffer);

        // NEITHER COLLECTOR ASKS FOR AN ORIGINAL VALUE - both read current values - so the count is
        // zero here and a non-zero count elsewhere is real evidence rather than noise.
        Assert.False(reads[1].OriginalValue);
        Assert.Equal(0, carrier.OriginalValueReadCount);

        carrier.ClearValueReads();
        Assert.Empty(carrier.ValueReads);
    }

    /// <summary>
    /// The four-argument read reaches the original value, and the coercion matches the production
    /// surface's.
    /// </summary>
    [Fact]
    public void TheCarrierFourArgumentReadReachesTheOriginalValue()
    {
        FakeDataWindowCarrier carrier = new();

        long row = carrier.SeedEditedRow(
            DwBuffer.Primary,
            ItemStatus.DataModified,
            [(1, 40L), (2, 12.75m), (3, "17"), (4, new object())],
            (1, 41L));

        IIdentityValueSource values = carrier;

        Assert.Equal(41L, values.GetItemNumber(row, 1, DwBuffer.Primary, originalValue: false));
        Assert.Equal(40L, values.GetItemNumber(row, 1, DwBuffer.Primary, originalValue: true));
        Assert.Equal(41L, values.GetItemNumber(row, 1));

        // THE COERCION IS THE PRODUCTION ONE, VERBATIM: a decimal truncates toward zero, a numeric
        // string parses under the invariant culture, and anything unrecognised is absent rather than
        // zero.
        Assert.Equal(12L, values.GetItemNumber(row, 2, DwBuffer.Primary, originalValue: false));
        Assert.Equal(17L, values.GetItemNumber(row, 3, DwBuffer.Primary, originalValue: false));
        Assert.Null(values.GetItemNumber(row, 4, DwBuffer.Primary, originalValue: false));
        Assert.Null(values.GetItemNumber(row, 5, DwBuffer.Primary, originalValue: false));

        Assert.Equal(1, carrier.OriginalValueReadCount);
    }

    /// <summary>
    /// The modified-row walk keeps the legacy seed-zero and terminator-zero convention, and it is the
    /// inherited implementation rather than a substitute.
    /// </summary>
    [Fact]
    public void TheCarrierModifiedRowWalkUsesTheLegacySeedAndTerminator()
    {
        FakeDataWindowCarrier carrier = new();

        _ = carrier.SeedRetrievedRows(
            DwBuffer.Primary,
            [(1, 1L)],
            [(1, 2L)],
            [(1, 3L)]);

        carrier.SetRowStatus(2L, DwBuffer.Primary, ItemStatus.DataModified);
        carrier.SetRowStatus(3L, DwBuffer.Primary, ItemStatus.NewModified);

        // SEEDED WITH 0 - "start before the first row" - and terminated by 0, not by -1.
        long first = carrier.GetNextModified(ItemStatusMachine.BeforeFirstRow, DwBuffer.Primary);
        long second = carrier.GetNextModified(first, DwBuffer.Primary);
        long terminator = carrier.GetNextModified(second, DwBuffer.Primary);

        Assert.Equal(2L, first);
        Assert.Equal(3L, second);
        Assert.Equal(ItemStatusMachine.NoMoreModifiedRows, terminator);
        Assert.Equal(2L, carrier.ModifiedCount());
        Assert.Equal([2L, 3L], carrier.EnumerateModifiedRows(DwBuffer.Primary));
        Assert.Equal(3L, carrier.CountOf(DwBuffer.Primary));
    }

    /// <summary>
    /// The four inherited mutators are reachable through the double and behave as the production store
    /// does, because they ARE the production store's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THIS TEST EXISTS TO PROVE AN ABSENCE.</b> <c>RowsMove</c>, <c>RowsDiscard</c>, <c>Reset</c> and
    /// <c>ResetUpdate</c> are deliberately NOT overridden, so a reader has no local implementation to
    /// inspect and no way to tell "inherited unchanged" from "forgotten" without exercising them.
    /// </para>
    /// <para>
    /// <b>RESET AND RESETUPDATE ARE NOT INTERCHANGEABLE, AND THE LEGACY SAYS SO IN BOTH DIRECTIONS.</b>
    /// <c>Reset</c> empties all three buffers; <c>ResetUpdate</c> keeps the rows and re-baselines them, so
    /// current values become original values and the delete buffer is discarded. The legacy additionally
    /// records that <c>Reset</c> MUST NOT be used to clear data before applying a changeset, because doing
    /// so makes the application fail
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L176</c>] - a preserved defect,
    /// which is exactly why a double must not quietly make the two verbs equivalent.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheCarrierInheritsTheProductionMutatorsUnchanged()
    {
        FakeDataWindowCarrier carrier = new();

        _ = carrier.SeedRetrievedRows(
            DwBuffer.Primary,
            [(1, 1L)],
            [(1, 2L)],
            [(1, 3L)]);

        _ = carrier.SeedRetrievedRow(DwBuffer.Delete, (1, 9L));

        // ROWSMOVE - one row from Primary! to Filter!, one-based bounds throughout.
        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            carrier.RowsMove(2L, 2L, DwBuffer.Primary, carrier, 1L, DwBuffer.Filter));
        Assert.Equal(2L, carrier.RowCount());
        Assert.Equal(1L, carrier.FilteredCount());
        Assert.Equal([2L], carrier.ColumnValuesInStorageOrder(DwBuffer.Filter, 1).Cast<long>());

        // ROWSDISCARD - the remaining Primary! rows, and the count follows.
        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            carrier.RowsDiscard(1L, 1L, DwBuffer.Primary));
        Assert.Equal(1L, carrier.RowCount());

        // RESETUPDATE - rows SURVIVE, the delete buffer is discarded, and current values become original
        // values. Asserted through the value, not through a count, because a count cannot see a baseline.
        carrier.EditCell(1L, 1, DwBuffer.Primary, 42L);
        Assert.Equal(3L, carrier.GetItemOriginalValue(1L, 1, DwBuffer.Primary));
        Assert.Equal(1L, carrier.DeletedCount());

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, carrier.ResetUpdate());

        Assert.Equal(1L, carrier.RowCount());
        Assert.Equal(0L, carrier.DeletedCount());
        Assert.Equal(42L, carrier.GetItemValue(1L, 1, DwBuffer.Primary));
        Assert.Equal(42L, carrier.GetItemOriginalValue(1L, 1, DwBuffer.Primary));

        // RESET - a DIFFERENT verb: all three buffers are emptied.
        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, carrier.Reset());

        Assert.Equal(0L, carrier.RowCount());
        Assert.Equal(0L, carrier.FilteredCount());
        Assert.Equal(0L, carrier.DeletedCount());
    }

    /// <summary>
    /// The changeset verbs round-trip a carrier's buffers, and a scripted encode failure yields no
    /// payload.
    /// </summary>
    /// <remarks>
    /// THE FAILURE ARM IS OTHERWISE UNREACHABLE. <c>ChangesetCodec</c> reports <c>GetChanges Failed</c>
    /// only when the encode step answers a non-success value, and the real encoder fails only on a
    /// carrier that cannot be built through the carrier's own surface.
    /// </remarks>
    [Fact]
    public void TheCarrierRoundTripsAChangesetAndCanBeMadeToFail()
    {
        FakeDataWindowCarrier source = new();
        source.SetColumnNames(["id", "age"]);

        long row = source.SeedEditedRow(
            DwBuffer.Primary,
            ItemStatus.DataModified,
            [(1, 1L), (2, 40L)],
            (2, 41L));

        Assert.Equal(1L, row);

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, source.GetChanges(out CarrierState? state));
        Assert.NotNull(state);
        Assert.Equal(1, source.Changeset.EncodeCalls);

        FakeDataWindowCarrier target = new();
        target.SetColumnNames(["id", "age"]);

        // A GetChanges-then-SetChanges round trip IS THE TRANSFER PATH, so it is read as one. The encoder
        // omits an original wherever it equals the current value, which is a positive statement that the
        // column did not move - column 1 above was seeded and never edited, so no original is emitted for
        // it. AsStated honours that encoding.
        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            target.SetChanges(state, CarrierBaselineTrust.AsStated));
        Assert.Equal(1L, target.RowCount());
        Assert.Equal(41L, target.GetItemValue(1L, 2, DwBuffer.Primary));
        Assert.Equal(
            ItemStatus.DataModified,
            target.GetItemStatus(1L, ItemStatusMachine.RowStatusColumn, DwBuffer.Primary));

        // 🔴 AND THE UPDATE READING - this double's DEFAULT - REFUSES A PAYLOAD WITH AN UNSTATED
        // BASELINE, which is what proves the two readings are genuinely different rather than one rule
        // with a spare argument. On the update path an unstated baseline is refused rather than read as
        // the caller's own current value, because it is the value the WHERE clause compares against. This
        // is the arm that answers `E_INVALID_DATA` at `Tasks/SqlUpdateTask` STEP 5
        // [n_cst_thread_task_sqlupdate.sru:L343-L344].
        //
        // THE PAYLOAD IS BUILT BY STRIPPING AN ORIGINAL RATHER THAN BY ROUND-TRIPPING ONE, and that is a
        // consequence of the encoder being correct rather than a weakening of this assertion. THIS
        // codec emits an original for EVERY column, agreeing or not, because AAP 0.6.3.2 admits no
        // exemption - so no state it produces can exercise the strict arm at all. The threat model the
        // arm exists for is a NON-CONFORMING PRODUCER, so the payload is made non-conforming here,
        // exactly as one would arrive: column 1's baseline removed and nothing else touched.
        CarrierState understated = state.Clone();
        DataWindowRow understatedRow = understated.Segments
            .Single(segment => segment.Buffer == DwBuffer.Primary)
            .Rows
            .Single();

        Assert.Equal(2, understatedRow.OriginalValues.Count);

        ColumnValue removed = understatedRow.OriginalValues.Single(value => value.ColumnId == 1);

        Assert.True(understatedRow.OriginalValues.Remove(removed));
        Assert.Equal(2, understatedRow.Columns.Count);

        FakeDataWindowCarrier strict = new();
        strict.SetColumnNames(["id", "age"]);

        Assert.Equal(DataWindowBufferStore.DataStoreFailure, strict.SetChanges(understated));
        Assert.Equal(0L, strict.RowCount());

        // AND THE SAME PAYLOAD IS ACCEPTED UNDER THE RETRIEVE READING, which is what makes the pair a
        // genuine discrimination: `AsStated` reads the absence as "did not move" and the strict default
        // reads it as "no baseline was supplied".
        FakeDataWindowCarrier lenient = new();
        lenient.SetColumnNames(["id", "age"]);

        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            lenient.SetChanges(understated, CarrierBaselineTrust.AsStated));
        Assert.Equal(1L, lenient.RowCount());

        source.Changeset.EncodeResult = DataWindowBufferStore.DataStoreFailure;

        Assert.Equal(DataWindowBufferStore.DataStoreFailure, source.GetChanges(out CarrierState? failed));
        Assert.Null(failed);

        target.Changeset.ApplyResult = DataWindowBufferStore.DataStoreFailure;
        Assert.Equal(DataWindowBufferStore.DataStoreFailure, target.SetChanges(state));
    }

    /// <summary>
    /// A crosstab or composite processing value selects the full-state path, and the full-state verbs
    /// round-trip through it.
    /// </summary>
    /// <remarks>
    /// ONLY 4 AND 5 SELECT FULL STATE, and <c>FullStateCodec.Apply</c> REFUSES a payload whose processing
    /// value does not - so the settable processing value is not a convenience, it is what makes the
    /// full-state path reachable at all.
    /// </remarks>
    [Theory]
    [InlineData(DataWindowProcessing.CrosstabValue)]
    [InlineData(DataWindowProcessing.CompositeValue)]
    public void TheCarrierProcessingValueSelectsTheFullStatePath(long processingValue)
    {
        FakeDataWindowCarrier source = new();
        source.SetColumnNames(["id", "age"]);
        source.SelectFullStateTransfer(processingValue);

        Assert.True(source.RequiresFullStateTransfer);
        Assert.Equal(processingValue, source.Processing.Value);

        _ = source.SeedRetrievedRow(DwBuffer.Primary, (1, 1L), (2, 40L));

        CarrierState? captured = source.GetFullState();

        Assert.NotNull(captured);
        Assert.Equal(processingValue, captured.Processing);

        FakeDataWindowCarrier target = new();
        target.SetColumnNames(["id", "age"]);

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, target.SetFullState(captured));
        Assert.Equal(1L, target.RowCount());
        Assert.Equal(40L, target.GetItemValue(1L, 2, DwBuffer.Primary));
        Assert.True(target.RequiresFullStateTransfer);
    }

    /// <summary>An unassigned processing value does NOT select the full-state path.</summary>
    [Fact]
    public void TheCarrierDefaultsToTheChangesetPath()
    {
        FakeDataWindowCarrier carrier = new();

        Assert.Equal(DataWindowProcessing.Unassigned.Value, carrier.Processing.Value);
        Assert.False(carrier.RequiresFullStateTransfer);
    }

    /// <summary>
    /// The carrier's surface refuses to normalise an unknown buffer, so a caller cannot ask for a fourth
    /// one.
    /// </summary>
    [Fact]
    public void TheCarrierRefusesABufferOutsideTheLegacyThree()
    {
        FakeDataWindowCarrier carrier = new();

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => carrier.CountOf((DwBuffer)99));
    }

    /// <summary>
    /// The parent task records notifications and database errors in call order, and can answer
    /// differently per notify code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A HANDLER RETURNING 1 MEANS ABORT IN THE PROGRESS ARM AND CONTINUE IN THE ROW-CAP ARM
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L41-L42, :L65-L68</c>] -
    /// a preserved legacy inconsistency, which is why one script can answer per code.
    /// </para>
    /// <para>
    /// <b>THE TWO NOTIFY-CODE SETS OVERLAP NUMERICALLY, AND THAT IS NOT A DEFECT.</b> Each is declared on
    /// its OWN task class in the legacy - <c>n_cst_threading_task_sqlquery.NCD_MAXROWS</c> and
    /// <c>n_cst_threading_task_sqlupdate.NCD_PROGRESS</c> - so the class name is part of the identity and
    /// both sets legitimately start at <c>1</c>. The overlap is pinned by the first assertion below,
    /// because a script keyed on the numeric code ALONE cannot tell a query notification from an update
    /// one, and a suite that assumed otherwise would silently answer for the wrong task.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheParentTaskRecordsNotificationsAndErrorsInOrder()
    {
        // THE OVERLAP, PINNED. Both per-task sets start at 1 because each is scoped by its own class.
        Assert.Equal((long)SqlUpdateTaskNotifyCode.Progress, (long)SqlQueryTaskNotifyCode.MaxRows);

        ScriptedCarrierParentTask parentTask = new()
        {
            NotifyScript = static notification =>
                notification.NotifyCode == (long)SqlQueryTaskNotifyCode.MaxRows
                    ? DataWindowBufferStore.EventStop
                    : DataWindowBufferStore.EventContinue,
        };

        ICarrierParentTask task = parentTask;

        Assert.True(task.IsMainThread);
        Assert.False(task.IsNCharBinding);
        Assert.False(task.IsCancelled);

        Assert.Equal(
            DataWindowBufferStore.EventContinue,
            task.OnNotify((long)SqlQueryTaskNotifyCode.DataReceived, 1L, string.Empty));
        Assert.Equal(
            DataWindowBufferStore.EventStop,
            task.OnNotify((long)SqlQueryTaskNotifyCode.MaxRows, 51L, string.Empty));
        Assert.Equal(
            DataWindowBufferStore.EventContinue,
            task.OnDbError(-803L, "constraint", "UPDATE COMPANY SET age = 41", DwBuffer.Primary, 1L));

        IReadOnlyList<RecordedNotification> notifications = parentTask.Notifications;

        Assert.Equal(2, notifications.Count);
        Assert.Equal([1, 2], notifications.Select(static notification => notification.Ordinal));
        Assert.Equal(1L, notifications[0].Payload);
        Assert.Equal(51L, notifications[1].Payload);
        Assert.Equal(1, parentTask.CountOf((long)SqlQueryTaskNotifyCode.MaxRows));

        RecordedCarrierDbError error = Assert.Single(parentTask.DbErrors);

        Assert.Equal(1, error.Ordinal);
        Assert.Equal(-803L, error.SqlDbCode);
        Assert.Equal(1L, error.Row);
        Assert.Equal(DwBuffer.Primary, error.Buffer);

        parentTask.Clear();
        Assert.Equal(0, parentTask.NotifyCount);
        Assert.Empty(parentTask.DbErrors);
    }

    /// <summary>
    /// A carrier, its surface, its codec and its parent task can be shared so several carriers record
    /// into one ordered log.
    /// </summary>
    [Fact]
    public void TheCarrierAcceptsSharedCollaborators()
    {
        ScriptedCarrierSurface surface = new();
        ScriptedChangesetCodec codec = new();
        ScriptedCarrierParentTask parentTask = new();

        FakeDataWindowCarrier parent = new(surface, codec, parentTask);
        FakeDataWindowCarrier child = new(surface, codec, parentTask);

        Assert.Same(surface, parent.Surface);
        Assert.Same(surface, child.Surface);
        Assert.Same(codec, parent.Changeset);
        Assert.Same(parentTask, child.ParentTask);

        _ = parent.GetChanges(out _);
        _ = child.GetChanges(out _);

        Assert.Equal(2, codec.EncodeCalls);
    }
}

#endregion
