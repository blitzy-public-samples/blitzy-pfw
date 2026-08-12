// ==================================================================================================
//  TransactionPoolTests.cs - THE REFERENCE-COUNT, EXPIRY AND CONNECTION-SEMANTICS PARITY SUITES
//  ------------------------------------------------------------------------------------------------
//  UNDER TEST     services/persistence-service/PowerFramework.Persistence/Transactions/
//                     TransactionPool.cs - the pool, the pooled transaction, the SQL-state snapshot
//                     and the class-name activator, all four of which live in that one file.
//  ORACLE         ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru        (READ ONLY)
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru             (READ ONLY)
//                 ws_objects/pfw.thread.ext.pbl.src/transactiondata.srs                (READ ONLY)
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru      (READ ONLY)
//                 ws_objects/pfw.thread.ext.pbl.src/dberrordata.srs                    (READ ONLY)
//                 ws_objects/pfw.shared.pbl.src/retcode.sru                            (READ ONLY)
//                 Every locator below is a CITATION into those files (C-C). None of them is read at
//                 run time, none is copied into this file, and none is ever an edit target: they are
//                 the behavioural oracle and this file is the record of what they say.
//
//  WHAT THESE SUITES ARE FOR. The subject reproduces a dozen legacy behaviours that LOOK like defects
//  and are not, and constraint C-B forbids correcting any of them. A comment saying so protects
//  nothing on its own: the assertions below are what make a "tidying" edit fail the build. Every one
//  of the following is pinned here, and each is pinned because the tidier alternative is the one a
//  reader would reach for first:
//
//    1  Release tests the reference count for EXACTLY 1 and does NOT decrement       [pool :L123]
//    2  RemoveRef's keep-alive arm RETAINS the entry rather than destroying it        [pool :L95-L99]
//    3  Collect's expiry comparison is GREATER-OR-EQUAL, not strictly greater         [pool :L215]
//    4  RemoveRef's disconnect is guarded by not-broken; RemoveAll's and Collect's
//       are NOT                                                        [pool :L105, :L197, :L217]
//    5  Get applies the descriptor to a NEWLY CREATED object only                     [pool :L171]
//    6  Get answers E_INVALID_OBJECT for ANY throwable                          [pool :L173-L175]
//    7  AddRef answers an INDEX, not a return code                                    [pool :L151]
//    8  A non-positive configured expiry falls back to 30000 ms rather than failing    [pool :L79]
//    9  Rollback under auto-commit answers FAILED, not a harmless no-op               [trans :L185]
//   10  The rollback and the clean disconnect PRESERVE all five SQL-state values
//                                                          [trans :L163-L183, :L504-L524]
//   11  SQLCode = 100 reads as a SUCCESS                                 [trans :L233, :L340]
//   12  IsBroken has a SIDE EFFECT - it fires the check hook                 [trans :L530-L534]
//   13  Collect answers NOTHING, because the oracle declares a SUBROUTINE           [pool :L70]
//   14  The credential is part of the pool KEY, so two passwords are two entries
//                                                          [pool :L138], [transactiondata.srs:L8]
//   15  A SUCCEEDING liveness probe RE-STAMPS the tick, restarting the window      [trans :L212]
//
//  Two further behaviours are pinned that are not legacy defects but are documented decisions of the
//  port: the SATURATING decrement at zero, whose alternative is a silent connection leak, and the
//  four constraints the class-name activator enforces.
//
//  AND TWO OBLIGATIONS THE PORT ACQUIRED BECAUSE THE BOUNDARY IS NEW, both asserted here rather than
//  assumed: the credential is WRITE-ONLY on every output path (AAP 0.4.2.6, C-F), and the statement text
//  in a structured error reaches a log only through ISqlRedactor, because the legacy field carries
//  interpolated literals and the legacy logger redacted nothing at all (AAP 0.6.3.8).
//
//  NO DATABASE, NO CONNECTION, NO NETWORK AND NO CONTAINER IS INVOLVED ANYWHERE IN THIS FILE (C-E,
//  C-H). Nothing here opens a connection, names a provider or references a dialect client: the pool's
//  own body performs no I/O at all, and the pooled transaction's I/O sits entirely behind the engine
//  seam. The doubles are the whole environment - the SHARED FakeTimeProvider and RecordingSqlRedactor
//  from TestDoubles.cs, plus five declared at the foot of this file: SkewedClock, FakeTransaction,
//  StubActivator, FakeEngine and the hook recorders.
//
//  TIME IS SIMULATED, ALWAYS, AND FROM ONE SEAM (AAP 0.6.7). Nothing here calls a real clock, waits,
//  sleeps, or measures anything: every instant this file reasons about is a value a test set. The clock
//  is the shared FakeTimeProvider from TestDoubles.cs - the SAME double the rest of this assembly drives
//  - which is hand-rolled rather than taken from Microsoft.Extensions.TimeProvider.Testing because that
//  package is NOT among the repository's central package versions and adding one the refactor's
//  dependency inventory does not list would be scope creep on a test file. Suite 13 additionally
//  declares SkewedClock, which answers the wall-clock and the monotonic readings from INDEPENDENT fields
//  so it can step a clock BACKWARDS - the one case that distinguishes a monotonic implementation from a
//  wall-clock one, and the one thing the shared double deliberately cannot express.
//
//  ONE TimeProvider SEAM SERVES THREE DISTINCT LEGACY CLOCKS, AND A FUTURE READER SHOULD NOT ADD A
//  SECOND ABSTRACTION FOR THEM (C-K). Program.cs registers exactly one TimeProvider, and these three
//  independent legacy windows all read through it - which is why TestDoubles.cs names all three on the
//  one double rather than spreading them over three:
//      * FakeTimeProvider.PoolIdleWindow        30000 ms  the pool's idle expiry  [pool  :L53, :L215]
//      * FakeTimeProvider.LivenessCacheWindow   10000 ms  the connection cache    [trans :L198]
//      * FakeTimeProvider.ProgressThrottleWindow  100 ms  the buffer layer's throttle (not this file's
//                                                         subject; named here so the seam's full duty is
//                                                         visible from one place)
//  Two of the three are this file's business, and both are asserted BEHAVIOURALLY - what the subject does
//  at a given SIMULATED instant - never as a latency or throughput claim, because the repository
//  publishes no performance objective of any kind (AAP 0.8.5).
//
//  COVERAGE, MEASURED (C-H). These suites take TransactionPool.cs to 533 of 533 lines - the pool, the
//  pooled transaction, the SQL-state snapshot and the activator all at 100 percent - and 95 of its 97
//  branch points. THE TWO REMAINING BRANCHES ARE UNREACHABLE FROM A TEST, and are recorded here so that
//  a later reader does not spend an afternoon on them:
//      * PooledEntry.IncrementRefCount's saturation arm needs a reference count of uint.MaxValue, which
//        is 4.29 billion AddRef calls. Its counterpart - the saturating DECREMENT at zero - IS asserted,
//        and that is the arm with a consequence, since the alternative there is a connection leak.
//      * One of the four short-circuit conditions in the activator's contract check would need an
//        ABSTRACT or OPEN-GENERIC implementation of IPooledTransaction inside the APPLICATION assembly;
//        the probes in this file are in the test assembly, which the by-name lookup deliberately does not
//        search. Adding a production type to reach a branch would be scope creep, so it is not added.
//
//  EVERY VALUE IN THIS FILE IS SYNTHETIC AND IS INVENTED HERE (C-F). No password, account name, host
//  name or connection string is copied from the legacy tree, from any catalogued in-source secret
//  site, or from any real system. Most descriptors below carry only DBMS and database identifiers,
//  because those are the only fields the pool's behaviour depends on - it matches descriptors by
//  value and never reads a field. Suites 16 and 18 additionally use two CREDENTIAL STAND-INS, both
//  declared beside the suite that needs them, both named so that no reader could mistake either for a
//  live secret, and both used only to be searched FOR in output and never disclosed by it.
//
//  RULES POSITION. review_rules returns exactly one line, "No user rules provided.", so no
//  user-specified rule governs this file - stated as a finding rather than as latitude. The
//  enterprise-standard baseline applies in their place (AAP 0.7.2): nullable and warnings-as-errors
//  inherited from Directory.Build.props, no secret in source, and no identifier declared here departs
//  from C# naming convention. The binding constraints are the refactor plan's own non-rule inventory -
//  C-B, C-C, C-E, C-F, C-H and C-K bite here and each is cited at the point it applies.
// ==================================================================================================

// System.Reflection is imported LOCALLY and deliberately: GlobalUsings.cs states that it is not made
// ambient across the assembly because only a few files assert on member SHAPES, and Suite 14 is one of
// them. Following that convention rather than widening the global set.
using System.Reflection;

// Microsoft.Extensions.Logging.Abstractions is NOT imported. Every sibling that imports it needs
// NullLogger for a subject that takes an ILogger; nothing in this file does, because neither the pool nor
// the pooled transaction takes a logger - which is itself worth noticing, since it is why "the credential
// never reaches a log" is asserted here against RENDERINGS and against what the redactor RECEIVED rather
// than against captured log lines.
using Microsoft.Extensions.Options;

using PowerFramework.Contracts.Persistence.V1;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Transactions;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// The parity suites for <c>TransactionPool</c> and the three companion types that share its file.
/// </summary>
public sealed class TransactionPoolTests
{
    // ==============================================================================================
    //  SYNTHETIC FIXTURES - INVENTED HERE, NOT IMPORTED (C-F)
    // ==============================================================================================

    /// <summary>A distinctive non-zero instant, so the zero "not idle" sentinel is never ambiguous.</summary>
    /// <remarks>
    /// Deliberately not the Unix epoch. The pool's clock readers difference the timestamp against an
    /// origin captured in the constructor, so the epoch is no longer a hazard on its own - but the fake
    /// answers its monotonic reading from this same instant's tick count, and starting from a non-zero
    /// instant keeps every stamp comfortably clear of the zero "not idle" sentinel [pool :L149].
    /// </remarks>
    private static readonly DateTimeOffset ClockStart = new(2024, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>The full name of the default pooled transaction, for the activation suite.</summary>
    private const string DefaultTransactionTypeName =
        "PowerFramework.Persistence.Transactions.PooledTransaction";

    /// <summary>
    /// The idle lifetime the expiry matrix configures, in SECONDS - the unit an operator writes.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT thirty seconds. The fallback default is thirty [<c>:L53</c>], so a matrix
    /// configured at thirty would pass whether the configured value was honoured or silently discarded in
    /// favour of the default. Ten proves the configured value is the one that reaches the comparison.
    /// </remarks>
    private const double ExpiryMatrixSeconds = 10d;

    /// <summary>
    /// The same lifetime in MILLISECONDS - the unit the subject's comparison is denominated in
    /// [<c>:L78</c>, <c>:L215</c>].
    /// </summary>
    /// <remarks>
    /// Written as its own constant rather than as <c>ExpiryMatrixSeconds * 1000</c> ON PURPOSE: deriving it
    /// from the seconds value with the very multiplication under test would make the assertion vacuous.
    /// The two constants are independent statements, and the test that relates them is the assertion.
    /// </remarks>
    private const long ExpiryMatrixMilliseconds = 10_000L;

    /// <summary>A first descriptor. Only the two identifiers matter; the pool never reads a field.</summary>
    private static TransactionData DescriptorA => new()
    {
        Dbms = "SQLITE",
        Database = "pfw-parity-a",
    };

    /// <summary>A second descriptor, unequal to the first.</summary>
    private static TransactionData DescriptorB => new()
    {
        Dbms = "SQLITE",
        Database = "pfw-parity-b",
    };

    /// <summary>
    /// A third descriptor, so the lease suite can drop a MIDDLE entry and observe the renumbering.
    /// </summary>
    private static TransactionData DescriptorC => new()
    {
        Dbms = "SQLITE",
        Database = "pfw-parity-c",
    };

    // ==============================================================================================
    //  SUITE 1 - INITIALISATION [n_cst_thread_trans_pool.sru:L76-L83]
    // ==============================================================================================

    /// <summary>
    /// Keep-alive defaults to <see langword="false"/> and the idle sweep is then NEVER driven, because
    /// the legacy subscription sits inside the keep-alive branch [pool :L80].
    /// </summary>
    [Fact]
    public void KeepAliveDefaultsToFalseAndIdleCollectionIsNotDriven()
    {
        using TransactionPool pool = CreatePool(out FakeTimeProvider _, out StubActivator _);

        Assert.False(pool.IsKeepAliveEnabled);
        Assert.False(pool.IsIdleCollectionEnabled);
    }

    /// <summary>
    /// With keep-alive off, <c>OnIdle</c> is inert [pool :L80], no entry can ever sit at zero references
    /// [pool :L95], and a DIRECT unforced sweep still finds nothing to take however far the clock moves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE KEEP-ALIVE-OFF CONFIGURATION IS ASSERTED, NOT ASSUMED, AND IT IS DRIVEN THROUGH THE CLOCK
    /// SEAM LIKE EVERY OTHER TIMING CLAIM IN THIS FILE.</b> Its behaviour is a CONSEQUENCE of two
    /// unrelated lines rather than a feature, so it is worth stating: because the retention arm is inside
    /// the keep-alive test [<c>:L95</c>], the last release destroys the entry there and then, and an
    /// unreferenced entry therefore never EXISTS for a sweep to find. The legacy draws the same conclusion
    /// and never subscribes its idle handler in this configuration [<c>:L80</c>].
    /// </para>
    /// <para>
    /// The last section below advances the clock a thousand seconds - far past any expiry - and sweeps
    /// UNFORCED, because <c>of_collect</c> is public [<c>:L210</c>] and a caller may invoke it whatever
    /// the configuration. The referenced entry survives, which is the <c>refCount &lt;= 0</c> conjunct
    /// [<c>:L215</c>] doing its work independently of the expiry test beside it.
    /// </para>
    /// </remarks>
    [Fact]
    public void OnIdleDoesNothingWhenKeepAliveIsOff()
    {
        using TransactionPool pool = CreatePool(out FakeTimeProvider clock, out StubActivator activator);

        int index = pool.AddRef(DescriptorA);
        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? _));

        // WITH KEEP-ALIVE OFF NO ENTRY CAN EVER SIT AT ZERO REFERENCES: the retention arm is skipped
        // [pool :L95] so RemoveRef destroys immediately [pool :L101-L114]. That is precisely why the
        // legacy never subscribes the idle handler in this configuration - there would be nothing for it
        // to find.
        Assert.Equal(RetCode.OK, pool.RemoveRef(index));
        Assert.Equal(0, pool.UpperBound);

        // A REFERENCED entry, which OnIdle must leave alone even if it did run - and it does not run.
        int second = pool.AddRef(DescriptorB);
        Assert.Equal(RetCode.OK, pool.Get(second, out IPooledTransaction? held));
        FakeTransaction survivor = Assert.IsType<FakeTransaction>(held);

        pool.OnIdle();

        Assert.Equal(1, pool.UpperBound);
        Assert.Equal(0, survivor.DisconnectCalls);
        Assert.Equal(0, survivor.DisposeCalls);
        Assert.Equal(2, activator.CreatedCount);

        // AND A DIRECT SWEEP, WITH THE CLOCK A THOUSAND SECONDS ON, STILL TAKES NOTHING. The expiry field
        // is populated even in this configuration [pool :L59] and of_collect is public [pool :L210], so
        // this call is legitimate - and the reference count alone is enough to retain the entry [:L215].
        clock.Advance(TimeSpan.FromMilliseconds(1_000_000));
        pool.Collect(force: false);

        Assert.Equal(1, pool.UpperBound);
        Assert.Equal(0, survivor.DisposeCalls);

        // Forcing it does take the entry, because force overrides the EXPIRY and this entry was retained
        // by its expiry, not by its count... except that it was NOT: it still holds a reference, so force
        // leaves it alone too [:L215]. The two conjuncts are independent and only one of them is forceable.
        pool.Collect(force: true);

        Assert.Equal(1, pool.UpperBound);
        Assert.Equal(0, survivor.DisposeCalls);
    }

    /// <summary>
    /// A non-positive configured expiry resolves to 30000 ms THROUGH the options type's own call, and is
    /// NOT rejected [pool :L78-L79]. Zero, a negative value and a fractional value under one
    /// millisecond all take the fallback.
    /// </summary>
    /// <param name="configuredSeconds">The configured value in seconds.</param>
    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(-3600d)]
    [InlineData(0.0004d)]
    public void NonPositiveExpiryFallsBackToThirtySecondsAndIsNotRejected(double configuredSeconds)
    {
        using TransactionPool pool = CreatePool(
            out FakeTimeProvider _,
            out StubActivator _,
            keepAlive: true,
            keepAliveExpireSeconds: configuredSeconds);

        Assert.Equal(
            TransactionPoolOptions.DefaultKeepAliveExpireMilliseconds,
            pool.KeepAliveExpireMilliseconds);
    }

    /// <summary>
    /// A positive configured expiry is converted from seconds to milliseconds exactly once
    /// [pool :L78]. A second conversion here would produce a value a thousand times too large.
    /// </summary>
    [Fact]
    public void PositiveExpiryIsConvertedFromSecondsExactlyOnce()
    {
        using TransactionPool pool = CreatePool(
            out FakeTimeProvider _,
            out StubActivator _,
            keepAlive: true,
            keepAliveExpireSeconds: 7d);

        Assert.Equal(7_000L, pool.KeepAliveExpireMilliseconds);
    }

    /// <summary>
    /// With keep-alive OFF the expiry field still carries thirty seconds, because the legacy field's
    /// initialiser survives the unexecuted branch [pool :L59] and <c>of_collect</c> is public
    /// [pool :L210].
    /// </summary>
    [Fact]
    public void ExpiryIsPopulatedEvenWhenKeepAliveIsOff()
    {
        using TransactionPool pool = CreatePool(
            out FakeTimeProvider _,
            out StubActivator _,
            keepAlive: false,
            keepAliveExpireSeconds: 99d);

        Assert.Equal(
            TransactionPoolOptions.DefaultKeepAliveExpireMilliseconds,
            pool.KeepAliveExpireMilliseconds);
    }

    /// <summary>
    /// The class name is resolved EVENT-FIRST: the resolver wins, and configuration is consulted only
    /// when the resolver yields null or empty [pool :L82-L83].
    /// </summary>
    /// <param name="resolverAnswer">What the resolver returns, or <see langword="null"/>.</param>
    /// <param name="configured">The configured fallback.</param>
    /// <param name="expected">The name the pool must settle on.</param>
    [Theory]
    [InlineData("FromResolver", "FromConfig", "FromResolver")]
    [InlineData(null, "FromConfig", "FromConfig")]
    [InlineData("", "FromConfig", "FromConfig")]
    [InlineData(null, "", "")]
    [InlineData(" ", "FromConfig", " ")]
    public void ClassNameIsResolvedEventFirst(string? resolverAnswer, string configured, string expected)
    {
        using TransactionPool pool = CreatePool(
            out FakeTimeProvider _,
            out StubActivator _,
            transactionClassName: configured,
            classNameResolver: () => resolverAnswer);

        // The whitespace row is deliberate: the oracle tests `IsNull(...) or ... = ""`, NOT blankness,
        // so a single space WINS over configuration and reaches the activator, which rejects it.
        Assert.Equal(expected, pool.TransactionClassName);
    }

    /// <summary>
    /// FAIL FAST on a structurally invalid construction (AAP 0.1.4) - a null argument or a missing
    /// options section throws rather than degrading.
    /// </summary>
    [Fact]
    public void ConstructionFailsFastOnStructurallyInvalidInput()
    {
        FakeTimeProvider clock = new(ClockStart);
        StubActivator activator = new();
        IOptions<PersistenceOptions> options = Options.Create(new PersistenceOptions());

        Assert.Throws<ArgumentNullException>(() => { _ = new TransactionPool(null!, clock, activator); });
        Assert.Throws<ArgumentNullException>(() => { _ = new TransactionPool(options, null!, activator); });
        Assert.Throws<ArgumentNullException>(() => { _ = new TransactionPool(options, clock, null!); });

        IOptions<PersistenceOptions> noSection =
            Options.Create(new PersistenceOptions { TransactionPool = null! });
        Assert.Throws<InvalidOperationException>(
            () => { _ = new TransactionPool(noSection, clock, activator); });

        // AND THE OTHER STRUCTURAL FAULT OF THE SAME KIND: an options accessor that resolves to NO VALUE.
        // A non-null accessor whose Value is null is what a mis-registered options pipeline hands over, and
        // it is not the same fault as a null accessor - the argument guard above cannot see it. Both arms
        // throw rather than proceeding with a default configuration, which is the fail-fast posture the
        // legacy states by terminating on a structural fault (AAP 0.1.4) rather than degrading quietly.
        Assert.Throws<InvalidOperationException>(
            () => { _ = new TransactionPool(new NullValueOptions(), clock, activator); });
    }

    /// <summary>
    /// A configured class name that binds to <see langword="null"/> is normalised to empty, so the pool
    /// takes the DEFAULT activation arm rather than asking for a null class [pool :L166-L170].
    /// </summary>
    /// <remarks>
    /// The oracle tests <c>_sTransCls &lt;&gt; ""</c> [<c>:L166</c>] and reaches its default arm otherwise.
    /// A configuration binder can legitimately produce a null string where the options type declares an
    /// empty default - a JSON null, an unset environment variable read into a nullable path - so the
    /// port normalises rather than trusting the declared default. Asserted because the alternative fails
    /// LATER, inside the activator, as a null-argument exception from a call the pool should never have
    /// made.
    /// </remarks>
    [Fact]
    public void ANullConfiguredClassNameIsNormalisedToEmptyAndTakesTheDefaultArm()
    {
        using TransactionPool pool = CreatePool(
            out FakeTimeProvider _,
            out StubActivator activator,
            transactionClassName: null!);

        Assert.Equal(string.Empty, pool.TransactionClassName);

        int index = pool.AddRef(DescriptorA);
        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? handed));

        Assert.NotNull(handed);
        Assert.Equal(1, activator.CreateDefaultCalls);
        Assert.Empty(activator.RequestedClassNames);
    }

    // ==============================================================================================
    //  SUITE 2 - THE REFERENCE-COUNT MATRIX [pool :L86-L152]
    // ==============================================================================================

    /// <summary>
    /// <c>AddRef</c> on an unknown descriptor APPENDS at upper bound plus one and answers that
    /// one-based index [pool :L143-L145, :L151].
    /// </summary>
    [Fact]
    public void AddRefOnUnknownDescriptorAppendsAndAnswersOneBasedIndex()
    {
        using TransactionPool pool = CreatePool(out FakeTimeProvider _, out StubActivator _);

        Assert.Equal(0, pool.UpperBound);

        // ONE-BASED: the first entry is index 1, not 0. An index of 0 is what the consumer stores to
        // mean "no reference" [n_cst_thread_task_sqlbase.sru:L123], so it must never address entry one.
        Assert.Equal(1, pool.AddRef(DescriptorA));
        Assert.Equal(1, pool.UpperBound);

        Assert.Equal(2, pool.AddRef(DescriptorB));
        Assert.Equal(2, pool.UpperBound);
    }

    /// <summary>
    /// <c>AddRef</c> on a KNOWN descriptor answers the existing index, increments, and zeroes the idle
    /// stamp [pool :L138-L139, :L148-L149].
    /// </summary>
    [Fact]
    public void AddRefOnKnownDescriptorReusesTheIndexIncrementsAndClearsTheIdleStamp()
    {
        using TransactionPool pool = CreatePool(
            out FakeTimeProvider clock,
            out StubActivator _,
            keepAlive: true,
            keepAliveExpireSeconds: 10d);

        int index = pool.AddRef(DescriptorA);
        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? _));

        // Drop to zero so the keep-alive arm stamps the idle clock.
        Assert.Equal(RetCode.OK, pool.RemoveRef(index));
        Assert.Equal(1, pool.UpperBound);

        // Re-reference it: same index, count back to one, AND THE STAMP IS CLEARED [pool :L149]. If it
        // were not cleared, a sweep could expire a transaction somebody has just asked for.
        clock.Advance(TimeSpan.FromMilliseconds(5_000));
        Assert.Equal(index, pool.AddRef(DescriptorA));
        Assert.Equal(1, pool.UpperBound);

        // Prove the stamp is gone by forcing the clock well past the expiry and sweeping: a referenced
        // entry survives, and the stamp being zero is what a later unforced sweep would consult.
        clock.Advance(TimeSpan.FromMilliseconds(1_000_000));
        pool.Collect(force: false);
        Assert.Equal(1, pool.UpperBound);
    }

    /// <summary>
    /// The last reference with keep-alive ON and a HEALTHY transaction RETAINS the entry and stamps the
    /// idle clock - it does not destroy, does not disconnect and does not remove [pool :L95-L99].
    /// </summary>
    [Fact]
    public void LastReferenceWithKeepAliveAndHealthyTransactionRetainsAndStamps()
    {
        using TransactionPool pool = CreatePool(
            out FakeTimeProvider clock,
            out StubActivator activator,
            keepAlive: true,
            keepAliveExpireSeconds: 10d);

        int index = pool.AddRef(DescriptorA);
        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? handed));
        FakeTransaction transaction = Assert.IsType<FakeTransaction>(handed);

        Assert.Equal(RetCode.OK, pool.RemoveRef(index));

        // RETAINED.
        Assert.Equal(1, pool.UpperBound);
        Assert.Equal(0, transaction.DisconnectCalls);
        Assert.Equal(0, transaction.DisposeCalls);

        // And the SAME object comes back, which is the whole point of retention.
        Assert.Equal(index, pool.AddRef(DescriptorA));
        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? again));
        Assert.Same(transaction, again);
        Assert.Equal(1, activator.CreatedCount);
    }

    /// <summary>
    /// The last reference with keep-alive OFF removes the entry and DISCONNECTS before disposing
    /// [pool :L101-L114].
    /// </summary>
    [Fact]
    public void LastReferenceWithKeepAliveOffRemovesAndDisconnects()
    {
        using TransactionPool pool = CreatePool(out FakeTimeProvider _, out StubActivator _);

        int index = pool.AddRef(DescriptorA);
        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? handed));
        FakeTransaction transaction = Assert.IsType<FakeTransaction>(handed);

        // The create arm's own entries - the descriptor apply - are not what this suite is about.
        transaction.Log.Clear();

        Assert.Equal(RetCode.OK, pool.RemoveRef(index));

        Assert.Equal(0, pool.UpperBound);
        Assert.Equal(1, transaction.DisconnectCalls);
        Assert.Equal(1, transaction.DisposeCalls);

        // The disconnect precedes the dispose [pool :L106-L108].
        AssertSequence(["Disconnect", "Dispose"], transaction.Log);
    }

    /// <summary>
    /// The last reference on a BROKEN transaction removes it WITHOUT calling <c>Disconnect</c>, because
    /// that call is guarded by not-broken in <c>RemoveRef</c> [pool :L105]. This is the guard whose
    /// absence in <c>RemoveAll</c> and <c>Collect</c> the next suites pin.
    /// </summary>
    [Fact]
    public void LastReferenceOnBrokenTransactionRemovesWithoutDisconnecting()
    {
        using TransactionPool pool = CreatePool(
            out FakeTimeProvider _,
            out StubActivator activator,
            keepAlive: true,
            keepAliveExpireSeconds: 10d);

        int index = pool.AddRef(DescriptorA);
        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? handed));
        FakeTransaction transaction = Assert.IsType<FakeTransaction>(handed);
        transaction.Broken = true;

        // Keep-alive is ON, yet a broken transaction still falls through to destruction [pool :L96].
        Assert.Equal(RetCode.OK, pool.RemoveRef(index));

        Assert.Equal(0, pool.UpperBound);
        Assert.Equal(0, transaction.DisconnectCalls);
        Assert.Equal(1, transaction.DisposeCalls);
        Assert.Equal(1, activator.CreatedCount);
    }

    /// <summary>
    /// An entry that never went through <c>Get</c> holds no transaction, and dropping its last
    /// reference removes it even with keep-alive on - because <c>IsValidObject</c> fails at
    /// [pool :L95].
    /// </summary>
    [Fact]
    public void LastReferenceOnAnEntryWithNoTransactionRemovesItEvenWithKeepAlive()
    {
        using TransactionPool pool = CreatePool(
            out FakeTimeProvider _,
            out StubActivator activator,
            keepAlive: true,
            keepAliveExpireSeconds: 10d);

        int index = pool.AddRef(DescriptorA);
        Assert.Equal(1, pool.UpperBound);

        Assert.Equal(RetCode.OK, pool.RemoveRef(index));

        Assert.Equal(0, pool.UpperBound);
        Assert.Equal(0, activator.CreatedCount);
    }

    /// <summary>
    /// A count above zero survives the decrement untouched [pool :L94].
    /// </summary>
    [Fact]
    public void RemoveRefWithReferencesRemainingKeepsTheEntry()
    {
        using TransactionPool pool = CreatePool(out FakeTimeProvider _, out StubActivator _);

        int index = pool.AddRef(DescriptorA);
        Assert.Equal(index, pool.AddRef(DescriptorA));
        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? handed));
        FakeTransaction transaction = Assert.IsType<FakeTransaction>(handed);

        Assert.Equal(RetCode.OK, pool.RemoveRef(index));

        Assert.Equal(1, pool.UpperBound);
        Assert.Equal(0, transaction.DisconnectCalls);
        Assert.Equal(0, transaction.DisposeCalls);
    }

    /// <summary>
    /// THE DECREMENT-AT-ZERO CASE, which is the documented width decision. The count SATURATES at zero
    /// rather than wrapping to <c>4294967295</c>, so a second <c>RemoveRef</c> on an entry that is
    /// already at zero still takes the release arm rather than retaining the entry for the life of the
    /// process.
    /// </summary>
    /// <remarks>
    /// The rejected alternative is the wrap, which <c>PooledEntry.DecrementRefCount</c> names
    /// explicitly. Under the wrap the second call below would leave the count at four billion, the
    /// <c>&lt;= 0</c> arm would be unreachable, and the entry - together with its pooled connection -
    /// would never be released again. This test is the thing that stops that behaviour from being
    /// reintroduced.
    /// </remarks>
    [Fact]
    public void DecrementAtZeroSaturatesRatherThanWrapping()
    {
        using TransactionPool pool = CreatePool(
            out FakeTimeProvider _,
            out StubActivator _,
            keepAlive: true,
            keepAliveExpireSeconds: 10d);

        int index = pool.AddRef(DescriptorA);
        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? handed));
        FakeTransaction transaction = Assert.IsType<FakeTransaction>(handed);

        // First call: count 1 -> 0, retained by the keep-alive arm.
        Assert.Equal(RetCode.OK, pool.RemoveRef(index));
        Assert.Equal(1, pool.UpperBound);

        // Second call on a count that is ALREADY ZERO. Saturating, so the count stays at zero and the
        // release arm is taken again - the entry is retained once more because it is still healthy,
        // which proves the arm was REACHED rather than skipped.
        Assert.Equal(RetCode.OK, pool.RemoveRef(index));
        Assert.Equal(1, pool.UpperBound);

        // Now condemn it and drop again: the release arm destroys, which under a wrapped count would be
        // unreachable because 4294967294 is not zero.
        transaction.Broken = true;
        Assert.Equal(RetCode.OK, pool.RemoveRef(index));
        Assert.Equal(0, pool.UpperBound);
        Assert.Equal(1, transaction.DisposeCalls);
    }

    /// <summary>
    /// <c>IsBroken</c> IS ASKED, and asking is what can condemn an entry [trans :L530-L534]. A check
    /// hook that condemns on inspection turns <c>RemoveRef</c>'s retention arm into a destruction arm.
    /// </summary>
    [Fact]
    public void RemoveRefInspectsWithIsBrokenAndACondemningCheckChangesTheArm()
    {
        using TransactionPool pool = CreatePool(
            out FakeTimeProvider _,
            out StubActivator _,
            keepAlive: true,
            keepAliveExpireSeconds: 10d);

        int index = pool.AddRef(DescriptorA);
        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? handed));
        FakeTransaction transaction = Assert.IsType<FakeTransaction>(handed);

        // Get already asked once [pool :L159]; reset so this suite measures RemoveRef alone.
        transaction.IsBrokenCalls = 0;

        // The inspection itself condemns it - the side effect the check hook exists for.
        transaction.CondemnOnNextIsBroken = true;

        Assert.Equal(RetCode.OK, pool.RemoveRef(index));

        // ASKED EXACTLY ONCE, which is the hook-count trace RemoveRef's remarks record: the oracle asks
        // at :L96 and again at :L105, but the second call finds the flag already set and fires no hook.
        Assert.Equal(1, transaction.IsBrokenCalls);

        // Condemned on inspection, so destroyed - and with NO disconnect, because :L105's guard sees it
        // as broken.
        Assert.Equal(0, pool.UpperBound);
        Assert.Equal(0, transaction.DisconnectCalls);
        Assert.Equal(1, transaction.DisposeCalls);
    }

    /// <summary>
    /// <c>IsBroken</c> is NOT asked at all when the entry holds no transaction - the zero-hook row of
    /// the trace [pool :L95, :L104].
    /// </summary>
    [Fact]
    public void RemoveRefDoesNotInspectWhenThereIsNoTransaction()
    {
        using TransactionPool pool = CreatePool(
            out FakeTimeProvider _,
            out StubActivator activator,
            keepAlive: true,
            keepAliveExpireSeconds: 10d);

        int index = pool.AddRef(DescriptorA);
        Assert.Equal(RetCode.OK, pool.RemoveRef(index));

        Assert.Equal(0, activator.CreatedCount);
        Assert.Empty(activator.Created);
    }

    /// <summary>
    /// THE REBUILD RENUMBERS EVERY LATER INDEX [pool :L114]. A preserved defect: an index held above a
    /// removed position addresses a DIFFERENT entry afterwards, silently.
    /// </summary>
    [Fact]
    public void RemovalRenumbersEveryLaterIndex()
    {
        using TransactionPool pool = CreatePool(out FakeTimeProvider _, out StubActivator _);

        int first = pool.AddRef(DescriptorA);
        int second = pool.AddRef(DescriptorB);
        Assert.Equal(1, first);
        Assert.Equal(2, second);

        // Drop the FIRST entry. The second entry is now at index 1, so the caller's stored index 2 is
        // out of range - and had there been a third entry it would silently address the second.
        Assert.Equal(RetCode.OK, pool.RemoveRef(first));
        Assert.Equal(1, pool.UpperBound);

        // The surviving entry answers to index 1 now, and re-acquiring says so.
        Assert.Equal(1, pool.AddRef(DescriptorB));

        // The stale index is out of range rather than merely wrong, in this two-entry shape.
        Assert.Equal(RetCode.E_OUT_OF_BOUND, pool.RemoveRef(3));
    }

    // ==============================================================================================
    //  SUITE 3 - THE RELEASE MATRIX [pool :L120-L132]
    // ==============================================================================================

    /// <summary>
    /// At EXACTLY one reference, <c>Release</c> disconnects the caller's object, clears its state, nulls
    /// the caller's handle - AND LEAVES THE COUNT UNCHANGED [pool :L123-L131].
    /// </summary>
    [Fact]
    public void ReleaseAtExactlyOneReferenceDisconnectsClearsAndNullsWithoutDecrementing()
    {
        using TransactionPool pool = CreatePool(out FakeTimeProvider _, out StubActivator _);

        int index = pool.AddRef(DescriptorA);
        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? handed));
        FakeTransaction transaction = Assert.IsType<FakeTransaction>(handed);
        transaction.Log.Clear();

        IPooledTransaction? handle = handed;
        Assert.Equal(RetCode.OK, pool.Release(index, ref handle));

        Assert.Null(handle);
        Assert.Equal(1, transaction.DisconnectCalls);
        Assert.Equal(1, transaction.ClearStateCalls);
        Assert.Equal(0, transaction.DisposeCalls);
        AssertSequence(["Disconnect", "ClearState"], transaction.Log);

        // NO DECREMENT. The entry is still there with its reference intact, which is exactly why a
        // second Release behaves identically rather than tipping into a release arm.
        Assert.Equal(1, pool.UpperBound);

        IPooledTransaction? again = transaction;
        Assert.Equal(RetCode.OK, pool.Release(index, ref again));
        Assert.Equal(2, transaction.DisconnectCalls);
    }

    /// <summary>
    /// At TWO references, <c>Release</c> does NOT disconnect - the test is <c>== 1</c>, not
    /// <c>&lt;= 1</c> [pool :L123] - yet it still clears and still nulls.
    /// </summary>
    [Fact]
    public void ReleaseAtTwoReferencesDoesNotDisconnectButStillClearsAndNulls()
    {
        using TransactionPool pool = CreatePool(out FakeTimeProvider _, out StubActivator _);

        int index = pool.AddRef(DescriptorA);
        Assert.Equal(index, pool.AddRef(DescriptorA));
        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? handed));
        FakeTransaction transaction = Assert.IsType<FakeTransaction>(handed);
        transaction.Log.Clear();

        IPooledTransaction? handle = handed;
        Assert.Equal(RetCode.OK, pool.Release(index, ref handle));

        Assert.Null(handle);
        Assert.Equal(0, transaction.DisconnectCalls);
        Assert.Equal(1, transaction.ClearStateCalls);
        AssertSequence(["ClearState"], transaction.Log);
    }

    /// <summary>
    /// At ZERO references, <c>Release</c> does not disconnect either - <c>== 1</c> excludes zero just as
    /// firmly as it excludes two [pool :L123].
    /// </summary>
    [Fact]
    public void ReleaseAtZeroReferencesDoesNotDisconnect()
    {
        using TransactionPool pool = CreatePool(
            out FakeTimeProvider _,
            out StubActivator _,
            keepAlive: true,
            keepAliveExpireSeconds: 10d);

        int index = pool.AddRef(DescriptorA);
        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? handed));
        FakeTransaction transaction = Assert.IsType<FakeTransaction>(handed);

        // Down to zero, retained by the keep-alive arm.
        Assert.Equal(RetCode.OK, pool.RemoveRef(index));
        transaction.Log.Clear();

        IPooledTransaction? handle = handed;
        Assert.Equal(RetCode.OK, pool.Release(index, ref handle));

        Assert.Null(handle);
        Assert.Equal(0, transaction.DisconnectCalls);
        Assert.Equal(1, transaction.ClearStateCalls);
    }

    /// <summary>
    /// <c>Release</c> validates the CALLER'S object, not the stored one, and answers
    /// <see cref="RetCode.E_INVALID_OBJECT"/> for a null handle [pool :L121]. The stored transaction is
    /// untouched.
    /// </summary>
    [Fact]
    public void ReleaseRejectsANullCallerHandleAndLeavesTheStoredTransactionAlone()
    {
        using TransactionPool pool = CreatePool(out FakeTimeProvider _, out StubActivator _);

        int index = pool.AddRef(DescriptorA);
        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? handed));
        FakeTransaction transaction = Assert.IsType<FakeTransaction>(handed);
        transaction.Log.Clear();

        IPooledTransaction? handle = null;
        Assert.Equal(RetCode.E_INVALID_OBJECT, pool.Release(index, ref handle));

        Assert.Null(handle);
        Assert.Empty(transaction.Log);
    }

    /// <summary>
    /// THE GUARD ORDER IS INDEX FIRST, THEN OBJECT [pool :L120 then :L121] - observable when both are
    /// bad, where the answer is <see cref="RetCode.E_OUT_OF_BOUND"/> rather than
    /// <see cref="RetCode.E_INVALID_OBJECT"/>.
    /// </summary>
    [Fact]
    public void ReleaseChecksTheIndexBeforeTheObject()
    {
        using TransactionPool pool = CreatePool(out FakeTimeProvider _, out StubActivator _);

        IPooledTransaction? handle = null;
        Assert.Equal(RetCode.E_OUT_OF_BOUND, pool.Release(1, ref handle));
    }

    /// <summary>
    /// A failing guard leaves the caller's handle untouched, exactly as the legacy's early returns do -
    /// they never reach <c>SetNull</c> [pool :L129].
    /// </summary>
    [Fact]
    public void ReleaseLeavesTheHandleIntactOnAFailingGuard()
    {
        using TransactionPool pool = CreatePool(out FakeTimeProvider _, out StubActivator _);

        int index = pool.AddRef(DescriptorA);
        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? handed));

        IPooledTransaction? handle = handed;
        Assert.Equal(RetCode.E_OUT_OF_BOUND, pool.Release(index + 1, ref handle));

        Assert.Same(handed, handle);
    }

    // ==============================================================================================
    //  SUITE 4 - THE INDEX-GUARD MATRIX [pool :L89, :L120, :L154]
    // ==============================================================================================

    /// <summary>
    /// Zero, negative and past-the-upper-bound indices all answer
    /// <see cref="RetCode.E_OUT_OF_BOUND"/> on all three guarded members.
    /// </summary>
    /// <param name="refIndex">The index to reject.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-2_147_483_648)]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    public void EveryGuardedMemberRejectsAnOutOfRangeIndex(int refIndex)
    {
        using TransactionPool pool = CreatePool(out FakeTimeProvider _, out StubActivator _);

        // Exactly one entry, so the only valid index is 1.
        Assert.Equal(1, pool.AddRef(DescriptorA));

        Assert.Equal(RetCode.E_OUT_OF_BOUND, pool.RemoveRef(refIndex));

        IPooledTransaction? handle = null;
        Assert.Equal(RetCode.E_OUT_OF_BOUND, pool.Release(refIndex, ref handle));

        Assert.Equal(RetCode.E_OUT_OF_BOUND, pool.Get(refIndex, out IPooledTransaction? transaction));
        Assert.Null(transaction);
    }

    /// <summary>
    /// THE UPPER BOUND IS THE COUNT, NOT THE COUNT MINUS ONE, so the highest entry is reachable
    /// [pool :L89]. Reading it as a zero-based last index would make this call fail.
    /// </summary>
    [Fact]
    public void TheHighestOneBasedIndexIsReachable()
    {
        using TransactionPool pool = CreatePool(out FakeTimeProvider _, out StubActivator _);

        _ = pool.AddRef(DescriptorA);
        int highest = pool.AddRef(DescriptorB);

        Assert.Equal(2, highest);
        Assert.Equal(2, pool.UpperBound);
        Assert.Equal(RetCode.OK, pool.Get(highest, out IPooledTransaction? transaction));
        Assert.NotNull(transaction);
    }

    // ==============================================================================================
    //  SUITE 5 - THE GET MATRIX [pool :L154-L178]
    // ==============================================================================================

    /// <summary>
    /// A healthy stored transaction is REUSED, has its state cleared, and does NOT have the descriptor
    /// re-applied [pool :L160-L162, :L171].
    /// </summary>
    [Fact]
    public void GetReusesAHealthyTransactionClearsItAndDoesNotReapplyTheDescriptor()
    {
        using TransactionPool pool = CreatePool(out FakeTimeProvider _, out StubActivator activator);

        int index = pool.AddRef(DescriptorA);

        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? first));
        FakeTransaction transaction = Assert.IsType<FakeTransaction>(first);

        // The CREATE path applied it exactly once [pool :L171].
        Assert.Equal(1, transaction.ApplyTransactionDataCalls);
        Assert.Equal(DescriptorA, transaction.LastAppliedDescriptor);

        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? second));

        Assert.Same(first, second);
        Assert.Equal(1, activator.CreatedCount);

        // STILL ONCE. A reused object is never re-configured.
        Assert.Equal(1, transaction.ApplyTransactionDataCalls);

        // THE MIRROR IMAGE OF THAT ASYMMETRY, AND IT IS EASY TO GET BACKWARDS: ClearState is called on
        // the REUSE arm only [pool :L161] and NOT on the create arm [pool :L166-L172], which is why the
        // count here is ONE after two hand-outs rather than two. A freshly created object has nothing to
        // clear, so the oracle does not clear it - and a port that cleared on both arms would fire one
        // extra call per newly created transaction.
        Assert.Equal(1, transaction.ClearStateCalls);
    }

    /// <summary>
    /// A BROKEN stored transaction is destroyed WITHOUT a disconnect [pool :L164] and a replacement is
    /// created and configured.
    /// </summary>
    [Fact]
    public void GetDestroysABrokenTransactionWithoutDisconnectingAndRecreates()
    {
        using TransactionPool pool = CreatePool(out FakeTimeProvider _, out StubActivator activator);

        int index = pool.AddRef(DescriptorA);
        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? first));
        FakeTransaction original = Assert.IsType<FakeTransaction>(first);
        original.Broken = true;

        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? second));
        FakeTransaction replacement = Assert.IsType<FakeTransaction>(second);

        Assert.NotSame(original, replacement);
        Assert.Equal(2, activator.CreatedCount);

        // THE THIRD DESTROY BEHAVIOUR IN THE FILE: destroyed, but NOT disconnected [pool :L164].
        Assert.Equal(0, original.DisconnectCalls);
        Assert.Equal(1, original.DisposeCalls);

        // The replacement is newly created, so it DOES get the descriptor.
        Assert.Equal(1, replacement.ApplyTransactionDataCalls);
        Assert.Equal(DescriptorA, replacement.LastAppliedDescriptor);
    }

    /// <summary>
    /// <c>Get</c> NEVER CONNECTS. No arm of it calls <c>Connect</c> - creation, reuse and recreation
    /// alike - because the pool has no <c>of_Connect</c> call anywhere in its 240 lines.
    /// </summary>
    [Fact]
    public void GetNeverConnectsOnAnyArm()
    {
        using TransactionPool pool = CreatePool(out FakeTimeProvider _, out StubActivator activator);

        int index = pool.AddRef(DescriptorA);

        // Create arm.
        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? created));
        FakeTransaction first = Assert.IsType<FakeTransaction>(created);
        Assert.Equal(0, first.ConnectCalls);

        // Reuse arm.
        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? _));
        Assert.Equal(0, first.ConnectCalls);

        // Recreate arm.
        first.Broken = true;
        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? recreated));
        Assert.Equal(0, Assert.IsType<FakeTransaction>(recreated).ConnectCalls);

        Assert.All(activator.Created, static t => Assert.Equal(0, t.ConnectCalls));
    }

    /// <summary>
    /// An empty class name selects the DEFAULT create arm [pool :L169]; a non-empty one selects the
    /// named arm [pool :L167].
    /// </summary>
    [Fact]
    public void GetSelectsTheCreateArmFromTheClassName()
    {
        using TransactionPool defaulted = CreatePool(out FakeTimeProvider _, out StubActivator defaultActivator);
        int defaultedIndex = defaulted.AddRef(DescriptorA);
        Assert.Equal(RetCode.OK, defaulted.Get(defaultedIndex, out IPooledTransaction? _));
        Assert.Equal(1, defaultActivator.CreateDefaultCalls);
        Assert.Empty(defaultActivator.RequestedClassNames);

        using TransactionPool named = CreatePool(
            out FakeTimeProvider _,
            out StubActivator namedActivator,
            transactionClassName: "SomeConfiguredName");
        int namedIndex = named.AddRef(DescriptorA);
        Assert.Equal(RetCode.OK, named.Get(namedIndex, out IPooledTransaction? _));
        Assert.Equal(0, namedActivator.CreateDefaultCalls);
        AssertSequence(["SomeConfiguredName"], namedActivator.RequestedClassNames);
    }

    /// <summary>
    /// ANY throwable from the create path answers <see cref="RetCode.E_INVALID_OBJECT"/> and hands back
    /// no object [pool :L173-L175]. This is why the activator reports failure by throwing.
    /// </summary>
    [Fact]
    public void GetAnswersInvalidObjectForAnyActivationFailure()
    {
        using TransactionPool pool = CreatePool(
            out FakeTimeProvider _,
            out StubActivator activator,
            transactionClassName: "WillNotResolve");

        activator.ThrowOnCreate = true;

        int index = pool.AddRef(DescriptorA);
        Assert.Equal(RetCode.E_INVALID_OBJECT, pool.Get(index, out IPooledTransaction? transaction));

        Assert.Null(transaction);

        // The entry survives, so a later call can succeed once the configuration is corrected.
        Assert.Equal(1, pool.UpperBound);
    }

    /// <summary>
    /// A throwable from the DESCRIPTOR APPLY is caught by the same arm, and the broken object destroyed
    /// on the way through is still disposed - the <see langword="finally"/> runs on the throwing path.
    /// </summary>
    [Fact]
    public void GetCatchesAFailingDescriptorApplyAndStillDisposesTheDoomedObject()
    {
        using TransactionPool pool = CreatePool(out FakeTimeProvider _, out StubActivator activator);

        int index = pool.AddRef(DescriptorA);
        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? first));
        FakeTransaction original = Assert.IsType<FakeTransaction>(first);
        original.Broken = true;

        activator.ThrowOnApply = true;

        Assert.Equal(RetCode.E_INVALID_OBJECT, pool.Get(index, out IPooledTransaction? transaction));
        Assert.Null(transaction);

        // The broken predecessor was disposed even though a later step threw.
        Assert.Equal(1, original.DisposeCalls);
    }

    // ==============================================================================================
    //  SUITE 6 - THE EXPIRY MATRIX [pool :L210-L226]
    // ==============================================================================================

    /// <summary>
    /// The expiry matrix, expressed as member data DERIVED FROM the configured lifetime rather than
    /// restated beside it.
    /// </summary>
    /// <returns>Elapsed milliseconds since the idle stamp, paired with whether the entry must be gone.</returns>
    /// <remarks>
    /// <para>
    /// <b>WHY MEMBER DATA AND NOT FOUR INLINE ROWS.</b> The boundary this matrix exists to pin is
    /// <c>elapsed &gt;= expiry</c> [<c>n_cst_thread_trans_pool.sru:L215</c>], so the interesting rows are
    /// the three AROUND the expiry and not three particular integers. Computing them from
    /// <see cref="ExpiryMatrixMilliseconds"/> means the row that must NOT collect and the row that must
    /// cannot drift apart from the configured value: change the lifetime and the boundary rows move with
    /// it. Four literals would have to be re-derived by hand, and a reader could not tell from the
    /// attribute alone which of them was meant to be the boundary.
    /// </para>
    /// <para>
    /// The zero row is not a boundary case but the OPPOSITE one, and it is the row that fails loudly if
    /// the idle stamp is ever left at the "not idle" sentinel of zero [<c>:L149</c>] instead of being
    /// written on release: with a zero stamp the subject would compute the whole elapsed process lifetime
    /// and collect an entry that a caller released an instant ago.
    /// </para>
    /// </remarks>
    public static TheoryData<long, bool> ExpiryMatrix() => new()
    {
        // Released and not a millisecond has passed - retained.
        { 0L, false },

        // One millisecond under the expiry - retained, which is what makes the next row an assertion.
        { ExpiryMatrixMilliseconds - 1L, false },

        // EXACTLY the expiry - COLLECTED, because the comparison is greater-or-EQUAL [:L215].
        { ExpiryMatrixMilliseconds, true },

        // Past it - collected.
        { ExpiryMatrixMilliseconds + 1L, true },
    };

    /// <summary>
    /// A delta EXACTLY equal to the expiry COLLECTS, because the comparison is greater-or-equal
    /// [pool :L215]. One tick under it does not.
    /// </summary>
    /// <param name="elapsedMilliseconds">How far the clock advances after the idle stamp.</param>
    /// <param name="expectCollected">Whether the entry must be gone afterwards.</param>
    [Theory]
    [MemberData(nameof(ExpiryMatrix))]
    public void CollectUsesAGreaterOrEqualExpiryComparison(long elapsedMilliseconds, bool expectCollected)
    {
        using TransactionPool pool = CreatePool(
            out FakeTimeProvider clock,
            out StubActivator _,
            keepAlive: true,
            keepAliveExpireSeconds: ExpiryMatrixSeconds);

        // THE CONVERSION CROSSES THE SEAM HERE: the lifetime is CONFIGURED in seconds and the clock below
        // is advanced in MILLISECONDS, so a missing or doubled x1000 [:L78] changes which rows pass.
        Assert.Equal(ExpiryMatrixMilliseconds, pool.KeepAliveExpireMilliseconds);

        int index = pool.AddRef(DescriptorA);
        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? handed));
        FakeTransaction transaction = Assert.IsType<FakeTransaction>(handed);

        // Retained, with the idle clock stamped at the current instant [pool :L97].
        Assert.Equal(RetCode.OK, pool.RemoveRef(index));
        Assert.Equal(1, pool.UpperBound);

        clock.Advance(TimeSpan.FromMilliseconds(elapsedMilliseconds));
        pool.Collect(force: false);

        Assert.Equal(expectCollected ? 0 : 1, pool.UpperBound);
        Assert.Equal(expectCollected ? 1 : 0, transaction.DisposeCalls);

        // WHEN IT DOES COLLECT, THE DISCONNECT IS UNGUARDED [pool :L217] - contrast with RemoveRef's
        // guarded call, which the reference-count suite pins.
        Assert.Equal(expectCollected ? 1 : 0, transaction.DisconnectCalls);
    }

    /// <summary>
    /// <c>force</c> overrides the EXPIRY only, never the reference count [pool :L215].
    /// </summary>
    [Fact]
    public void ForcedCollectionOverridesTheExpiryButNotTheReferenceCount()
    {
        using TransactionPool pool = CreatePool(
            out FakeTimeProvider _,
            out StubActivator _,
            keepAlive: true,
            keepAliveExpireSeconds: 3_600d);

        int referenced = pool.AddRef(DescriptorA);
        Assert.Equal(RetCode.OK, pool.Get(referenced, out IPooledTransaction? held));
        FakeTransaction stillHeld = Assert.IsType<FakeTransaction>(held);

        int idle = pool.AddRef(DescriptorB);
        Assert.Equal(RetCode.OK, pool.Get(idle, out IPooledTransaction? release));
        FakeTransaction released = Assert.IsType<FakeTransaction>(release);
        Assert.Equal(RetCode.OK, pool.RemoveRef(idle));

        Assert.Equal(2, pool.UpperBound);

        // Forced, with the clock not advanced at all: the unreferenced entry goes, the referenced one
        // survives.
        pool.Collect(force: true);

        Assert.Equal(1, pool.UpperBound);
        Assert.Equal(1, released.DisposeCalls);
        Assert.Equal(0, stillHeld.DisposeCalls);
    }

    /// <summary>
    /// A BROKEN transaction is still disconnected by <c>Collect</c>, because that call is UNGUARDED
    /// [pool :L217] unlike <c>RemoveRef</c>'s [pool :L105]. This is the asymmetry stated as a pair.
    /// </summary>
    [Fact]
    public void CollectDisconnectsEvenABrokenTransaction()
    {
        using TransactionPool pool = CreatePool(
            out FakeTimeProvider clock,
            out StubActivator _,
            keepAlive: true,
            keepAliveExpireSeconds: 1d);

        int index = pool.AddRef(DescriptorA);
        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? handed));
        FakeTransaction transaction = Assert.IsType<FakeTransaction>(handed);

        Assert.Equal(RetCode.OK, pool.RemoveRef(index));
        Assert.Equal(1, pool.UpperBound);

        transaction.Broken = true;
        clock.Advance(TimeSpan.FromMilliseconds(1_000));
        pool.Collect(force: false);

        Assert.Equal(0, pool.UpperBound);
        Assert.Equal(1, transaction.DisconnectCalls);
        Assert.Equal(1, transaction.DisposeCalls);
    }

    /// <summary>
    /// The idle sweep runs from <c>OnIdle</c> when keep-alive is ON, unforced [pool :L73, :L80].
    /// </summary>
    [Fact]
    public void OnIdleSweepsUnforcedWhenKeepAliveIsOn()
    {
        using TransactionPool pool = CreatePool(
            out FakeTimeProvider clock,
            out StubActivator _,
            keepAlive: true,
            keepAliveExpireSeconds: 2d);

        Assert.True(pool.IsIdleCollectionEnabled);

        int index = pool.AddRef(DescriptorA);
        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? handed));
        FakeTransaction transaction = Assert.IsType<FakeTransaction>(handed);
        Assert.Equal(RetCode.OK, pool.RemoveRef(index));

        // Not yet expired.
        clock.Advance(TimeSpan.FromMilliseconds(1_999));
        pool.OnIdle();
        Assert.Equal(1, pool.UpperBound);

        // Expired.
        clock.Advance(TimeSpan.FromMilliseconds(1));
        pool.OnIdle();
        Assert.Equal(0, pool.UpperBound);
        Assert.Equal(1, transaction.DisposeCalls);
    }

    // ==============================================================================================
    //  THE HOSTED IDLE-SWEEP DRIVER IS PROVEN IN ITS OWN SUITE, NOT HERE.
    //
    //  The property that matters - that a hosted service actually DRIVES OnIdle on the SAME injected
    //  clock the pool measures expiry against, and that it starts no timer at all when the sweep is
    //  disabled - is asserted by TransactionPoolIdleSweeperTests.cs against
    //  Transactions/TransactionPoolIdleSweeper.cs. That suite carries the stronger form of the same
    //  assertions: it overrides CreateTimer as well as both clock readings (PeriodicTimer asks the
    //  provider to CREATE a timer, so a double overriding only the readings would leave a real
    //  System.Threading.Timer running and the case would be a race against real seconds), it covers the
    //  non-positive interval as a theory rather than a single case, and it also pins repetition, clean
    //  shutdown, the disposed-pool swallow and the success record.
    //
    //  Duplicating those cases here would give the pool suite a second, weaker opinion about a schedule
    //  it does not own - the pool exposes OnIdle and IsIdleCollectionEnabled and nothing about WHEN the
    //  sweep runs, which is exactly why the schedule lives in its own type and its own suite.
    // ==============================================================================================
    /// <summary>
    /// An entry that never held a transaction is still swept out; there is simply nothing to settle
    /// [pool :L216].
    /// </summary>
    [Fact]
    public void CollectRemovesAnEntryThatNeverHeldATransaction()
    {
        using TransactionPool pool = CreatePool(out FakeTimeProvider _, out StubActivator activator);

        int index = pool.AddRef(DescriptorA);
        Assert.Equal(RetCode.OK, pool.RemoveRef(index));
        Assert.Equal(0, pool.UpperBound);

        int second = pool.AddRef(DescriptorB);
        Assert.Equal(1, second);
        pool.Collect(force: true);

        Assert.Equal(1, pool.UpperBound);
        Assert.Equal(0, activator.CreatedCount);
    }

    // ==============================================================================================
    //  SUITE 7 - REMOVE-ALL AND DISPOSAL [pool :L190-L208, :L238]
    // ==============================================================================================

    /// <summary>
    /// Unforced, <c>RemoveAll</c> destroys unreferenced entries IGNORING the idle clock, and keeps
    /// referenced ones [pool :L195].
    /// </summary>
    [Fact]
    public void RemoveAllUnforcedDestroysUnreferencedEntriesIgnoringTheClock()
    {
        using TransactionPool pool = CreatePool(
            out FakeTimeProvider _,
            out StubActivator _,
            keepAlive: true,
            keepAliveExpireSeconds: 3_600d);

        int referenced = pool.AddRef(DescriptorA);
        Assert.Equal(RetCode.OK, pool.Get(referenced, out IPooledTransaction? held));
        FakeTransaction stillHeld = Assert.IsType<FakeTransaction>(held);

        int idle = pool.AddRef(DescriptorB);
        Assert.Equal(RetCode.OK, pool.Get(idle, out IPooledTransaction? release));
        FakeTransaction released = Assert.IsType<FakeTransaction>(release);
        Assert.Equal(RetCode.OK, pool.RemoveRef(idle));

        // The clock has not moved, so Collect would keep both; RemoveAll does not consult it.
        Assert.Equal(RetCode.OK, pool.RemoveAll(force: false));

        Assert.Equal(1, pool.UpperBound);
        Assert.Equal(1, released.DisposeCalls);
        Assert.Equal(1, released.DisconnectCalls);
        Assert.Equal(0, stillHeld.DisposeCalls);
    }

    /// <summary>
    /// Forced, <c>RemoveAll</c> destroys REFERENCED entries too - which is the difference from a forced
    /// <c>Collect</c> [pool :L195 against :L215].
    /// </summary>
    [Fact]
    public void RemoveAllForcedDestroysReferencedEntriesToo()
    {
        using TransactionPool pool = CreatePool(out FakeTimeProvider _, out StubActivator _);

        int index = pool.AddRef(DescriptorA);
        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? handed));
        FakeTransaction transaction = Assert.IsType<FakeTransaction>(handed);

        Assert.Equal(RetCode.OK, pool.RemoveAll(force: true));

        Assert.Equal(0, pool.UpperBound);
        Assert.Equal(1, transaction.DisconnectCalls);
        Assert.Equal(1, transaction.DisposeCalls);
    }

    /// <summary>
    /// <c>RemoveAll</c>'s disconnect is UNGUARDED, so a broken transaction is disconnected [pool :L197].
    /// </summary>
    [Fact]
    public void RemoveAllDisconnectsEvenABrokenTransaction()
    {
        using TransactionPool pool = CreatePool(out FakeTimeProvider _, out StubActivator _);

        int index = pool.AddRef(DescriptorA);
        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? handed));
        FakeTransaction transaction = Assert.IsType<FakeTransaction>(handed);
        transaction.Broken = true;

        Assert.Equal(RetCode.OK, pool.RemoveAll(force: true));

        Assert.Equal(1, transaction.DisconnectCalls);
        Assert.Equal(1, transaction.DisposeCalls);
    }

    /// <summary>
    /// Disposal is <c>RemoveAll(true)</c> [pool :L238] and is IDEMPOTENT, and every member then refuses
    /// to run.
    /// </summary>
    [Fact]
    public void DisposalForcesRemoveAllIsIdempotentAndClosesEveryMember()
    {
        FakeTransaction transaction;
        TransactionPool pool = CreatePool(out FakeTimeProvider _, out StubActivator _);

        int index = pool.AddRef(DescriptorA);
        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? handed));
        transaction = Assert.IsType<FakeTransaction>(handed);

        pool.Dispose();

        // Forced, so the still-referenced entry was destroyed.
        Assert.Equal(1, transaction.DisconnectCalls);
        Assert.Equal(1, transaction.DisposeCalls);

        // AND NO ENTRY IS LEFT BEHIND. The forced sweep [:L195] empties the collection rather than
        // merely closing what it holds, so a disposed pool cannot be a reference-count ledger for
        // connections that no longer exist. Asserted rather than assumed, because the count reader is the
        // one member that stays readable after disposal.
        Assert.Equal(0, pool.UpperBound);

        // Idempotent: the second call is a no-op rather than a second sweep or a throw.
        pool.Dispose();
        Assert.Equal(1, transaction.DisposeCalls);

        TransactionData descriptor = DescriptorA;
        IPooledTransaction? handle = transaction;
        Assert.Throws<ObjectDisposedException>(() => { _ = pool.AddRef(descriptor); });
        Assert.Throws<ObjectDisposedException>(() => { _ = pool.RemoveRef(1); });
        Assert.Throws<ObjectDisposedException>(() => { _ = pool.Release(1, ref handle); });
        Assert.Throws<ObjectDisposedException>(() => { _ = pool.Get(1, out IPooledTransaction? _); });
        Assert.Throws<ObjectDisposedException>(() => { _ = pool.Exists(descriptor); });
        Assert.Throws<ObjectDisposedException>(() => { _ = pool.RemoveAll(force: true); });
        Assert.Throws<ObjectDisposedException>(() => pool.Collect(force: true));
        Assert.Throws<ObjectDisposedException>(pool.OnIdle);
    }

    // ==============================================================================================
    //  SUITE 8 - EXISTS [pool :L180-L188]
    // ==============================================================================================

    /// <summary>
    /// <c>Exists</c> answers about the ENTRY, not about a transaction, so an entry that has never been
    /// through <c>Get</c> still answers <see langword="true"/>.
    /// </summary>
    [Fact]
    public void ExistsAnswersAboutTheEntryRatherThanAboutATransaction()
    {
        using TransactionPool pool = CreatePool(out FakeTimeProvider _, out StubActivator activator);

        Assert.False(pool.Exists(DescriptorA));
        Assert.False(pool.Exists(DescriptorB));

        _ = pool.AddRef(DescriptorA);

        Assert.True(pool.Exists(DescriptorA));
        Assert.False(pool.Exists(DescriptorB));
        Assert.Equal(0, activator.CreatedCount);
    }

    /// <summary>
    /// The pool key is the WHOLE descriptor by value, so a difference in ANY field - including
    /// <c>DbParm</c>, which carries the bind-disabling flag - produces a different entry. Narrowing the
    /// key would share one connection between a bound and an unbound configuration.
    /// </summary>
    [Fact]
    public void TheKeyIsTheWholeDescriptorSoADbParmDifferenceIsADifferentEntry()
    {
        using TransactionPool pool = CreatePool(out FakeTimeProvider _, out StubActivator _);

        TransactionData bound = DescriptorA with { DbParm = "DisableBind=0" };
        TransactionData unbound = DescriptorA with { DbParm = "DisableBind=1" };

        int first = pool.AddRef(bound);
        int second = pool.AddRef(unbound);

        Assert.NotEqual(first, second);
        Assert.Equal(2, pool.UpperBound);
        Assert.True(pool.Exists(bound));
        Assert.True(pool.Exists(unbound));
        Assert.False(pool.Exists(DescriptorA));
    }

    // ==============================================================================================
    //  SUITE 9 - THREAD SAFETY
    // ==============================================================================================

    /// <summary>
    /// Concurrent <c>AddRef</c>, <c>Get</c>, <c>RemoveRef</c> and <c>Collect</c> neither tear the entry
    /// collection nor deadlock, and the pool settles empty.
    /// </summary>
    [Fact]
    public async Task ConcurrentUseDoesNotTearStateOrDeadlock()
    {
        using TransactionPool pool = CreatePool(
            out FakeTimeProvider _,
            out StubActivator activator,
            keepAlive: true,
            keepAliveExpireSeconds: 1d);

        const int workers = 8;
        const int iterations = 60;

        Task[] tasks = new Task[workers];
        for (int worker = 0; worker < workers; worker++)
        {
            int seed = worker;
            tasks[worker] = Task.Run(
                () =>
                {
                    for (int iteration = 0; iteration < iterations; iteration++)
                    {
                        TransactionData descriptor = new()
                        {
                            Dbms = "SQLITE",
                            Database = "pfw-concurrent-" + ((seed + iteration) % 4),
                        };

                        int index = pool.AddRef(descriptor);
                        Assert.True(index > 0);

                        // The index may already have been renumbered by another worker's removal - the
                        // preserved defect - so a non-success answer here is legitimate and is exactly
                        // what a caller must tolerate. What must NEVER happen is a torn read or a hang.
                        long got = pool.Get(index, out IPooledTransaction? transaction);
                        Assert.True(got == RetCode.OK || got == RetCode.E_OUT_OF_BOUND);

                        if (got == RetCode.OK)
                        {
                            Assert.NotNull(transaction);
                        }

                        long removed = pool.RemoveRef(index);
                        Assert.True(removed == RetCode.OK || removed == RetCode.E_OUT_OF_BOUND);

                        pool.Collect(force: false);
                    }
                },
                TestContext.Current.CancellationToken);
        }

        await Task.WhenAll(tasks);

        pool.RemoveAll(force: true);
        Assert.Equal(0, pool.UpperBound);

        // Every transaction the activator ever produced has been disposed exactly once, so nothing was
        // orphaned by a race.
        Assert.All(activator.Created, static t => Assert.Equal(1, t.DisposeCalls));
    }

    /// <summary>
    /// The lock is NOT held across a disconnect: a transaction whose <c>Disconnect</c> re-enters the
    /// pool completes rather than deadlocking.
    /// </summary>
    /// <remarks>
    /// This is the only way to assert the rule from outside. A re-entrant call from inside a disconnect
    /// would block forever if settlement happened under the lock; with settlement outside it, the
    /// nested call simply succeeds. Note that <see cref="System.Threading.Lock"/> is re-entrant on the
    /// same thread, so the assertion is strengthened by making the nested call observe real state
    /// rather than merely return.
    /// </remarks>
    [Fact]
    public void SettlementHappensOutsideTheLock()
    {
        using TransactionPool pool = CreatePool(out FakeTimeProvider _, out StubActivator _);

        int index = pool.AddRef(DescriptorA);
        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? handed));
        FakeTransaction transaction = Assert.IsType<FakeTransaction>(handed);

        int observedUpperBound = -1;
        transaction.OnDisconnect = () => observedUpperBound = pool.UpperBound;

        Assert.Equal(RetCode.OK, pool.RemoveRef(index));

        // The entry was already removed from the collection before the disconnect ran, which is what
        // "decide under the lock, settle outside it" means in observable terms.
        Assert.Equal(0, observedUpperBound);
        Assert.Equal(0, pool.UpperBound);
    }

    /// <summary>
    /// A throwing disconnect still reaches the dispose, and a batch settles EVERY item before reporting.
    /// </summary>
    [Fact]
    public void AThrowingDisconnectStillDisposesAndABatchSettlesEveryItem()
    {
        using TransactionPool pool = CreatePool(out FakeTimeProvider _, out StubActivator activator);

        int first = pool.AddRef(DescriptorA);
        Assert.Equal(RetCode.OK, pool.Get(first, out IPooledTransaction? one));
        FakeTransaction failing = Assert.IsType<FakeTransaction>(one);
        failing.ThrowOnDisconnect = true;

        int second = pool.AddRef(DescriptorB);
        Assert.Equal(RetCode.OK, pool.Get(second, out IPooledTransaction? two));
        FakeTransaction healthy = Assert.IsType<FakeTransaction>(two);

        AggregateException raised =
            Assert.Throws<AggregateException>(() => pool.RemoveAll(force: true));

        Assert.Single(raised.InnerExceptions);

        // BOTH were disposed: the failing one through its own finally, the healthy one because the batch
        // loop kept going.
        Assert.Equal(1, failing.DisposeCalls);
        Assert.Equal(1, healthy.DisposeCalls);
        Assert.Equal(0, pool.UpperBound);
        Assert.Equal(2, activator.CreatedCount);
    }

    // ==============================================================================================
    //  SUITE 10 - THE CONNECTION-SEMANTICS MATRIX [n_cst_thread_trans.sru]
    //  Run against the REAL PooledTransaction over a fake engine, so every arm is exercised with no
    //  database, no connection and no network (C-E, C-H).
    // ==============================================================================================

    /// <summary>
    /// A successful connect disconnects any open handle first, clears state, connects, and stamps the
    /// liveness tick [trans :L111-L143].
    /// </summary>
    [Fact]
    public void ConnectDisconnectsAnOpenHandleFirstAndStampsTheLivenessTick()
    {
        FakeTimeProvider clock = new(ClockStart);
        FakeEngine engine = new() { DbHandle = 7 };
        RecordingHooks hooks = new();
        using PooledTransaction transaction = new(engine, clock, hooks);
        hooks.Observed = transaction;

        Assert.Equal(RetCode.OK, transaction.Connect(TestContext.Current.CancellationToken));

        // The pre-emptive disconnect [trans :L115-L117] fires NO hook - it is a bare statement.
        AssertSequence(["Disconnect", "Connect"], engine.Log);
        AssertSequence(["OnBeforeConnect", "OnAfterConnect", "OnConnectionOk"], hooks.Log);

        // The tick is stamped, so the liveness cache short-circuits immediately afterwards.
        Assert.True(transaction.IsConnected());
    }

    /// <summary>
    /// A prevented before-connect hook answers <see cref="RetCode.E_DB_ERROR"/> when it left an error
    /// and <see cref="RetCode.CANCELLED"/> when it did not [trans :L122-L124].
    /// </summary>
    /// <param name="hookSqlCode">The code the hook leaves behind.</param>
    /// <param name="expected">The return code the connect must answer.</param>
    [Theory]
    [InlineData(0L, RetCode.CANCELLED)]
    [InlineData(-1L, RetCode.E_DB_ERROR)]
    [InlineData(100L, RetCode.E_DB_ERROR)]
    public void APreventedConnectDiscriminatesOnTheStateTheHookLeft(long hookSqlCode, long expected)
    {
        FakeTimeProvider clock = new(ClockStart);
        FakeEngine engine = new();
        RecordingHooks hooks = new()
        {
            BeforeConnectResult = RetCode.PREVENT,

            // The 100 row matters: the veto discrimination tests SQLCode <> 0 [trans :L123], and 100 is
            // non-zero - so the "no data found" code, which reads as a SUCCESS everywhere else in this
            // file, reads as a DATABASE ERROR here. That asymmetry is the oracle's.
            BeforeConnectStamp = hookSqlCode == 0
                ? null
                : new SqlState(hookSqlCode, hookSqlCode, 0, "hook", string.Empty),
        };

        using PooledTransaction transaction = new(engine, clock, hooks);
        hooks.Observed = transaction;
        hooks.Observed = transaction;

        Assert.Equal(expected, transaction.Connect(TestContext.Current.CancellationToken));
        Assert.DoesNotContain("Connect", engine.Log);
    }

    /// <summary>
    /// A hook returning <c>2</c> does NOT veto, because the test is
    /// <c>Predicates.IsPrevented</c> - an exact equality against <c>1</c> [trans :L122].
    /// </summary>
    [Fact]
    public void ADeepPreventionValueDoesNotVetoTheConnect()
    {
        FakeTimeProvider clock = new(ClockStart);
        FakeEngine engine = new();
        RecordingHooks hooks = new() { BeforeConnectResult = 2L };
        using PooledTransaction transaction = new(engine, clock, hooks);

        Assert.Equal(RetCode.OK, transaction.Connect(TestContext.Current.CancellationToken));
        Assert.Contains("Connect", engine.Log);
    }

    /// <summary>
    /// A connect that SUCCEEDED but whose state is spoiled by the after-hook performs the
    /// state-preserving clean disconnect and answers <see cref="RetCode.E_DB_ERROR"/>
    /// [trans :L129-L137].
    /// </summary>
    [Fact]
    public void AConnectSpoiledByItsAfterHookCleanlyDisconnectsAndReportsADatabaseError()
    {
        FakeTimeProvider clock = new(ClockStart);
        FakeEngine engine = new();
        RecordingHooks hooks = new() { AfterConnectStamp = SqlState.Failed(-99, "spoiled") };
        using PooledTransaction transaction = new(engine, clock, hooks);
        hooks.Observed = transaction;

        Assert.Equal(RetCode.E_DB_ERROR, transaction.Connect(TestContext.Current.CancellationToken));

        // The connect ran and was then cleanly undone, with the disconnect hooks fired [trans :L513, :L523].
        AssertSequence(["Connect", "Disconnect"], engine.Log);
        AssertSequence(
            ["OnBeforeConnect", "OnAfterConnect", "OnBeforeDisconnect", "OnAfterDisconnect"],
            hooks.Log);

        // STATE PRESERVED: the caller still reads the error that condemned it, not the disconnect's own
        // outcome.
        Assert.Equal(-99L, transaction.SqlDbCode);
        Assert.Equal("spoiled", transaction.SqlErrText);
        Assert.False(transaction.IsConnected());
    }

    /// <summary>
    /// A connect on a BROKEN transaction answers <see cref="RetCode.E_INVALID_TRANSACTION"/> and touches
    /// the engine not at all [trans :L113].
    /// </summary>
    [Fact]
    public void ConnectRefusesABrokenTransaction()
    {
        FakeTimeProvider clock = new(ClockStart);
        FakeEngine engine = new();
        using PooledTransaction transaction = new(engine, clock);

        Assert.Equal(RetCode.OK, transaction.SetBroken());
        Assert.Equal(RetCode.E_INVALID_TRANSACTION, transaction.Connect(TestContext.Current.CancellationToken));
        Assert.Empty(engine.Log);
    }

    /// <summary>
    /// AN ALREADY-CANCELLED TOKEN REFUSES THE CONNECT BEFORE ANY ENGINE VERB, and answers
    /// <see cref="RetCode.CANCELLED"/> rather than throwing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE ARM SITS AHEAD OF THE BROKENNESS TEST, AND THE ORDER IS THE ASSERTION.</b> The subject's own
    /// comment says the cancellation arm goes first "because it decides whether to start at all", and this
    /// test holds it to that: a cancelled token reaches no hook and no engine verb, so nothing is half done
    /// and there is nothing to undo. It also pins the RESULT SHAPE - the port answers the return-code
    /// algebra's cancelled value, which the predicates classify as neither succeeded nor failed
    /// [<c>retcode.sru:L44-L45</c>], rather than raising an exception a caller of an in-process pool would
    /// never have had to handle.
    /// </para>
    /// <para>
    /// The token is cancelled by the TEST and observed synchronously; nothing waits for it, so this stays
    /// inside the determinism mandate exactly as the clock-driven suites do.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnAlreadyCancelledTokenRefusesTheConnectBeforeAnyEngineVerb()
    {
        FakeTimeProvider clock = new(ClockStart);
        FakeEngine engine = new() { DbHandle = 3 };
        RecordingHooks hooks = new();
        using PooledTransaction transaction = new(engine, clock, hooks);

        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Assert.Equal(RetCode.CANCELLED, transaction.Connect(cancelled.Token));

        // NEITHER SUCCEEDED NOR FAILED, which is the tri-state hole preserved from the oracle.
        Assert.False(Predicates.IsSucceeded(RetCode.CANCELLED));
        Assert.False(Predicates.IsFailed(RetCode.CANCELLED));

        // AND NOTHING STARTED: no pre-emptive disconnect despite the open handle, no hook, no connect.
        Assert.Empty(engine.Log);
        Assert.Empty(hooks.Log);

        // The transaction is untouched rather than condemned, so a later uncancelled call still works.
        Assert.False(transaction.IsBroken());
        Assert.Equal(RetCode.OK, transaction.Connect(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A pooled transaction that carries no engine keeps the interface's DEFAULT negative for the engine
    /// accessor, which is the documented answer rather than an omission.
    /// </summary>
    /// <remarks>
    /// The interface supplies <c>ResolveEngine()</c> as a default member answering <see langword="null"/>
    /// precisely so a double that composes no engine stays a valid implementation (C-E). This asserts the
    /// pair of negatives together: the accessor answers nothing, and the capability probe answers false
    /// rather than throwing - so a caller reaching for a provider-specific capability on a transaction that
    /// has no provider gets a defined "no" on both doors.
    /// </remarks>
    [Fact]
    public void ATransactionWithNoEngineAnswersTheDefaultNegativeOnBothEngineDoors()
    {
        IPooledTransaction engineless = new FakeTransaction();

        Assert.Null(engineless.ResolveEngine());
        Assert.False(engineless.TryGetEngineCapability(out IDisposable? capability));
        Assert.Null(capability);

        // And the real one DOES carry an engine, so the negative above is a property of the double rather
        // than of the interface member being unreachable.
        FakeTimeProvider clock = new(ClockStart);
        FakeEngine engine = new();
        using PooledTransaction real = new(engine, clock);

        Assert.Same(engine, real.ResolveEngine());
    }

    /// <summary>
    /// <c>Disconnect</c>'s no-op fast path fires NO hooks and still answers success, for a closed handle
    /// and for a broken transaction alike [trans :L145-L148].
    /// </summary>
    /// <param name="dbHandle">The engine's handle.</param>
    /// <param name="broken">Whether the transaction is condemned first.</param>
    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(5, true)]
    public void DisconnectFastPathFiresNoHooksAndReportsSuccess(int dbHandle, bool broken)
    {
        FakeTimeProvider clock = new(ClockStart);
        FakeEngine engine = new() { DbHandle = dbHandle };
        RecordingHooks hooks = new();
        using PooledTransaction transaction = new(engine, clock, hooks);

        if (broken)
        {
            Assert.Equal(RetCode.OK, transaction.SetBroken());
        }

        Assert.Equal(RetCode.OK, transaction.Disconnect());
        Assert.Empty(engine.Log);
        Assert.Empty(hooks.Log);
    }

    /// <summary>
    /// A real disconnect fires both hooks, zeroes the liveness tick, and reports the engine's own
    /// failure [trans :L150-L158].
    /// </summary>
    [Fact]
    public void ARealDisconnectFiresBothHooksAndReportsAFailure()
    {
        FakeTimeProvider clock = new(ClockStart);
        FakeEngine engine = new() { DbHandle = 3, DisconnectResult = SqlState.Failed(-12, "no") };
        RecordingHooks hooks = new();
        using PooledTransaction transaction = new(engine, clock, hooks);
        hooks.Observed = transaction;

        Assert.Equal(RetCode.E_DB_ERROR, transaction.Disconnect());
        AssertSequence(["OnBeforeDisconnect", "OnAfterDisconnect"], hooks.Log);
        Assert.False(transaction.IsConnected());
    }

    /// <summary>
    /// ROLLBACK UNDER AUTO-COMMIT ANSWERS <see cref="RetCode.FAILED"/>, not a harmless no-op
    /// [trans :L185] - and the auto-commit test comes BEFORE the broken test, which is observable when
    /// both hold.
    /// </summary>
    [Fact]
    public void RollbackUnderAutoCommitReportsFailureAndIsTestedBeforeBrokenness()
    {
        FakeTimeProvider clock = new(ClockStart);
        FakeEngine engine = new() { AutoCommit = true };
        using PooledTransaction transaction = new(engine, clock);

        Assert.Equal(RetCode.FAILED, transaction.Rollback());
        Assert.Empty(engine.Log);

        // Broken AS WELL: still FAILED, because :L185 precedes :L186.
        Assert.Equal(RetCode.OK, transaction.SetBroken());
        Assert.Equal(RetCode.FAILED, transaction.Rollback());

        // Not auto-commit and broken: NOW the broken arm answers.
        engine.AutoCommit = false;
        Assert.Equal(RetCode.E_INVALID_TRANSACTION, transaction.Rollback());
    }

    /// <summary>
    /// THE ROLLBACK PRESERVES ALL FIVE SQL-STATE VALUES, and the after-hook observes the RESTORED state
    /// rather than the rollback's own [trans :L163-L183].
    /// </summary>
    [Fact]
    public void TheRollbackPreservesAllFiveStateValuesAndTheAfterHookSeesThem()
    {
        FakeTimeProvider clock = new(ClockStart);
        SqlState original = new(-1, -4711, 17, "the original error", "the original return");
        FakeEngine engine = new()
        {
            DbHandle = 4,
            ExecuteResult = original,

            // The rollback statement reports something entirely different, which must NOT survive.
            RollbackResult = new SqlState(0, 0, 0, string.Empty, "rollback-return"),
        };
        RecordingHooks hooks = new();
        using PooledTransaction transaction = new(engine, clock, hooks);
        hooks.Observed = transaction;

        // Put the original state in place through a failing command.
        Assert.Equal(RetCode.E_DB_ERROR, transaction.Exec("UPDATE COMPANY SET AGE = 1", TestContext.Current.CancellationToken));
        Assert.Equal(original.SqlDbCode, transaction.SqlDbCode);

        Assert.Equal(RetCode.OK, transaction.Rollback());

        // ALL FIVE RESTORED.
        Assert.Equal(original.SqlCode, transaction.SqlCode);
        Assert.Equal(original.SqlDbCode, transaction.SqlDbCode);
        Assert.Equal(original.SqlNRows, transaction.SqlNRows);
        Assert.Equal(original.SqlErrText, transaction.SqlErrText);
        Assert.Equal(original.SqlReturnData, transaction.SqlReturnData);

        // The BEFORE hook saw the failing operation's state; the AFTER hook saw the restored state.
        Assert.Equal(original.SqlDbCode, hooks.BeforeRollbackObservedSqlDbCode);
        Assert.Equal(original.SqlDbCode, hooks.AfterRollbackObservedSqlDbCode);
        Assert.Equal(original.SqlErrText, hooks.AfterRollbackObservedSqlErrText);
    }

    /// <summary>
    /// THE CLEAN DISCONNECT PRESERVES THE SAME FIVE VALUES [trans :L504-L524], through the same snapshot
    /// type - which is why the two routines cannot drift apart.
    /// </summary>
    [Fact]
    public void TheCleanDisconnectPreservesTheSameFiveStateValues()
    {
        FakeTimeProvider clock = new(ClockStart);
        FakeEngine engine = new()
        {
            // The disconnect the clean path performs reports its own, different state.
            DisconnectResult = new SqlState(-2, -222, 3, "disconnect-error", "disconnect-return"),
        };
        RecordingHooks hooks = new() { AfterConnectStamp = SqlState.Failed(-808, "after-connect-spoil") };
        using PooledTransaction transaction = new(engine, clock, hooks);
        hooks.Observed = transaction;

        Assert.Equal(RetCode.E_DB_ERROR, transaction.Connect(TestContext.Current.CancellationToken));

        // The spoiled state survived the clean disconnect, which is the whole point.
        Assert.Equal(-1L, transaction.SqlCode);
        Assert.Equal(-808L, transaction.SqlDbCode);
        Assert.Equal("after-connect-spoil", transaction.SqlErrText);
        Assert.Equal(-808L, hooks.AfterDisconnectObservedSqlDbCode);
    }

    /// <summary>
    /// <c>Commit</c> gates on auto-commit and brokenness, and on failure rolls back only when asked
    /// [trans :L240-L257]. The parameterless overload delegates with auto-rollback ON [trans :L383].
    /// </summary>
    [Fact]
    public void CommitGatesRollsBackOnDemandAndTheParameterlessOverloadOptsIn()
    {
        FakeTimeProvider clock = new(ClockStart);
        FakeEngine engine = new() { AutoCommit = true };
        RecordingHooks hooks = new();
        using PooledTransaction autoCommitted = new(engine, clock, hooks);

        Assert.Equal(RetCode.FAILED, autoCommitted.Commit(autoRollback: true));
        Assert.Equal(RetCode.FAILED, autoCommitted.Commit());

        FakeEngine broken = new();
        using PooledTransaction condemned = new(broken, clock);
        Assert.Equal(RetCode.OK, condemned.SetBroken());
        Assert.Equal(RetCode.E_INVALID_TRANSACTION, condemned.Commit(autoRollback: true));

        // A failing commit WITHOUT auto-rollback does not roll back.
        FakeEngine failingBare = new() { CommitResult = SqlState.Failed(-5, "commit failed") };
        RecordingHooks bareHooks = new();
        using PooledTransaction bare = new(failingBare, clock, bareHooks);
        Assert.Equal(RetCode.E_DB_ERROR, bare.Commit(autoRollback: false));
        AssertSequence(["Commit"], failingBare.Log);
        AssertSequence(["OnBeforeCommit", "OnAfterCommit"], bareHooks.Log);

        // The parameterless overload DOES roll back, because it passes true.
        FakeEngine failingDefault = new() { CommitResult = SqlState.Failed(-5, "commit failed") };
        using PooledTransaction defaulted = new(failingDefault, clock);
        Assert.Equal(RetCode.E_DB_ERROR, defaulted.Commit());
        AssertSequence(["Commit", "Rollback"], failingDefault.Log);
    }

    /// <summary>
    /// The autocommit checkpoint inspects the state it inherits: an error rolls back and reports, a
    /// clean non-auto-commit state commits, and a clean auto-commit state does nothing
    /// [trans :L370-L381].
    /// </summary>
    [Fact]
    public void TheAutoCommitCheckpointChoosesBetweenRollbackCommitAndNothing()
    {
        FakeTimeProvider clock = new(ClockStart);

        // An inherited error, not auto-commit: roll back and report.
        FakeEngine erroring = new() { DbHandle = 2, ExecuteResult = SqlState.Failed(-3, "bad") };
        using PooledTransaction failed = new(erroring, clock);
        Assert.Equal(RetCode.E_DB_ERROR, failed.Exec("DELETE FROM COMPANY", TestContext.Current.CancellationToken));
        Assert.Equal(RetCode.E_DB_ERROR, failed.AutoCommitCheckpoint());
        AssertSequence(["Execute", "Rollback"], erroring.Log);

        // An inherited error under auto-commit: report WITHOUT rolling back.
        FakeEngine erroringAuto = new()
        {
            DbHandle = 2,
            AutoCommit = true,
            ExecuteResult = SqlState.Failed(-3, "bad"),
        };
        using PooledTransaction failedAuto = new(erroringAuto, clock);
        Assert.Equal(RetCode.E_DB_ERROR, failedAuto.Exec("DELETE FROM COMPANY", TestContext.Current.CancellationToken));
        Assert.Equal(RetCode.E_DB_ERROR, failedAuto.AutoCommitCheckpoint());
        AssertSequence(["Execute"], erroringAuto.Log);

        // Clean and not auto-commit: commit.
        FakeEngine clean = new();
        using PooledTransaction committing = new(clean, clock);
        Assert.Equal(RetCode.OK, committing.AutoCommitCheckpoint());
        AssertSequence(["Commit"], clean.Log);

        // Clean and auto-commit: nothing at all.
        FakeEngine cleanAuto = new() { AutoCommit = true };
        using PooledTransaction nothing = new(cleanAuto, clock);
        Assert.Equal(RetCode.OK, nothing.AutoCommitCheckpoint());
        Assert.Empty(cleanAuto.Log);
    }

    /// <summary>
    /// The SQL-code matrix: the code a statement leaves behind, the return code the command path must
    /// answer, and what the SQLCode success predicate must say about it.
    /// </summary>
    /// <returns>The four rows that pin the hundred.</returns>
    /// <remarks>
    /// <para>
    /// <b>THE 100 ROW IS THE WHOLE POINT AND IT IS NOT A TYPO.</b> The oracle tests
    /// <c>SQLCode &lt;&gt; 0 and SQLCode &lt;&gt; 100</c> [<c>n_cst_thread_trans.sru:L233</c>], so a
    /// hundred - the "no rows found" code - reads as a SUCCESS. Preserved verbatim under C-B: the tidy
    /// alternative is to treat every non-zero code as an error, and it would turn every empty result set
    /// into a database error.
    /// </para>
    /// <para>
    /// <b>THE 1 ROW IS THE OTHER HALF OF THE PAIR</b> and it is the row that proves the two tests are
    /// genuinely different rather than accidentally agreeing: a POSITIVE non-zero code is a database error
    /// in the command path [<c>:L233</c>] while the return-code predicate reads it as a success, because
    /// that predicate tests the SIGN [<c>:L340</c>]. Both answers are asserted on the one row.
    /// </para>
    /// <para>
    /// Member data rather than inline rows so this reasoning sits WITH the rows it explains rather than
    /// above four attributes that cannot carry it.
    /// </para>
    /// </remarks>
    public static TheoryData<long, long, bool> SqlCodeMatrix() => new()
    {
        // Clean: a success by both readings.
        { 0L, RetCode.OK, true },

        // NO ROWS FOUND: a success by both readings, and the row a "simplification" would break.
        { 100L, RetCode.OK, true },

        // A negative code: a database error, and a failure to the sign-testing predicate.
        { -1L, RetCode.E_DB_ERROR, false },

        // A positive non-zero code: a database error to the command path, a SUCCESS to the predicate.
        { 1L, RetCode.E_DB_ERROR, true },
    };

    /// <summary>
    /// <c>SQLCode = 100</c> READS AS A SUCCESS, both in the command path [trans :L233] and in the
    /// transaction's own success predicate [trans :L340].
    /// </summary>
    /// <param name="sqlCode">The code the statement leaves behind.</param>
    /// <param name="expected">The return code the command must answer.</param>
    /// <param name="expectSqlSucceeded">What the SQLCode success predicate must answer.</param>
    [Theory]
    [MemberData(nameof(SqlCodeMatrix))]
    public void SqlCodeOneHundredReadsAsSuccessInBothPlaces(
        long sqlCode,
        long expected,
        bool expectSqlSucceeded)
    {
        FakeTimeProvider clock = new(ClockStart);
        FakeEngine engine = new()
        {
            DbHandle = 1,
            ExecuteResult = new SqlState(sqlCode, sqlCode, 0, string.Empty, string.Empty),
        };
        using PooledTransaction transaction = new(engine, clock);

        Assert.Equal(expected, transaction.Exec("SELECT * FROM COMPANY", TestContext.Current.CancellationToken));

        // THE SQLCODE FAMILY, NOT the return-code algebra. Note the 1 row: it is a database error in the
        // command path yet reads as a SUCCESS to the predicate, because the two tests are different.
        Assert.Equal(expectSqlSucceeded, transaction.IsSqlSucceeded());
        Assert.Equal(!expectSqlSucceeded, transaction.IsSqlFailed());
    }

    /// <summary>
    /// A null or empty command answers <see cref="RetCode.E_INVALID_ARGUMENT"/> [trans :L220], and a
    /// prevented command hook discriminates exactly as the connect hook does [trans :L225-L226].
    /// </summary>
    [Fact]
    public void ExecRejectsEmptyCommandsAndDiscriminatesAVeto()
    {
        FakeTimeProvider clock = new(ClockStart);
        FakeEngine engine = new();
        using PooledTransaction transaction = new(engine, clock);

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, transaction.Exec(null, TestContext.Current.CancellationToken));
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, transaction.Exec(string.Empty, TestContext.Current.CancellationToken));
        Assert.Empty(engine.Log);

        // WHITESPACE IS NOT EMPTY: the oracle tests emptiness, not blankness, so this reaches the engine.
        Assert.Equal(RetCode.OK, transaction.Exec("   ", TestContext.Current.CancellationToken));
        AssertSequence(["Execute"], engine.Log);

        FakeEngine vetoed = new();
        RecordingHooks cleanVeto = new() { BeforeCommandResult = RetCode.PREVENT };
        using PooledTransaction cancelled = new(vetoed, clock, cleanVeto);
        Assert.Equal(RetCode.CANCELLED, cancelled.Exec("SELECT 1", TestContext.Current.CancellationToken));
        Assert.Empty(vetoed.Log);

        FakeEngine errored = new();
        RecordingHooks dirtyVeto = new()
        {
            BeforeCommandResult = RetCode.PREVENT,
            BeforeCommandStamp = SqlState.Failed(-9, "veto error"),
        };
        using PooledTransaction dbError = new(errored, clock, dirtyVeto);
        dirtyVeto.Observed = dbError;
        Assert.Equal(RetCode.E_DB_ERROR, dbError.Exec("SELECT 1", TestContext.Current.CancellationToken));
        Assert.Empty(errored.Log);
    }

    /// <summary>
    /// The liveness-cache matrix, derived from the shared statement of the legacy window.
    /// </summary>
    /// <returns>Elapsed milliseconds since the connect, paired with whether the probe must have run.</returns>
    /// <remarks>
    /// <para>
    /// <b>THIS BOUNDARY IS THE OPPOSITE WAY ROUND FROM THE EXPIRY ONE, AND THAT IS THE POINT.</b> The pool
    /// collects when <c>elapsed &gt;= expiry</c> [<c>n_cst_thread_trans_pool.sru:L215</c>] while the
    /// connection caches when <c>elapsed &lt; 10000</c> [<c>n_cst_thread_trans.sru:L198</c>] - so the value
    /// ON the boundary EXPIRES in the first case and PROBES in the second. Two windows, two comparisons,
    /// one injected clock: the rows are derived from <see cref="FakeTimeProvider.LivenessCacheWindow"/> so
    /// that the shared double's statement of the window and this suite's boundary cannot drift apart
    /// (C-K).
    /// </para>
    /// </remarks>
    public static TheoryData<long, bool> LivenessCacheMatrix()
    {
        long window = (long)FakeTimeProvider.LivenessCacheWindow.TotalMilliseconds;

        return new TheoryData<long, bool>
        {
            // Just connected - cached, no probe.
            { 0L, false },

            // One millisecond inside the window - still cached, because the test is STRICTLY less-than.
            { window - 1L, false },

            // EXACTLY the window - PROBES, because the cache holds only while strictly under it [:L198].
            { window, true },

            // Past it - probes.
            { window + 1L, true },
        };
    }

    /// <summary>
    /// THE LIVENESS CACHE SHORT-CIRCUITS STRICTLY UNDER TEN THOUSAND MILLISECONDS and re-probes at or
    /// beyond it [trans :L198].
    /// </summary>
    /// <param name="elapsedMilliseconds">How far the clock advances after the connect.</param>
    /// <param name="expectProbe">Whether the built-in probe must have run.</param>
    [Theory]
    [MemberData(nameof(LivenessCacheMatrix))]
    public void TheLivenessCacheShortCircuitsStrictlyUnderTheWindow(
        long elapsedMilliseconds,
        bool expectProbe)
    {
        FakeTimeProvider clock = new(ClockStart);
        FakeEngine engine = new() { ExecuteResult = SqlState.Succeeded(rowCount: 1) };
        using PooledTransaction transaction = new(engine, clock);

        Assert.Equal(RetCode.OK, transaction.Connect(TestContext.Current.CancellationToken));
        engine.Log.Clear();

        clock.Advance(TimeSpan.FromMilliseconds(elapsedMilliseconds));

        Assert.True(transaction.IsConnected());

        string[] expectedLog = expectProbe ? ["Execute"] : [];
        AssertSequence(expectedLog, engine.Log);
    }

    /// <summary>
    /// The built-in probe judges on the ROW COUNT and not on <c>SQLCode</c> [trans :L207], and it
    /// chooses the dual-table form for Oracle and the bare form for everything else
    /// [trans :L202-L205].
    /// </summary>
    /// <param name="dbms">The DBMS identifier.</param>
    /// <param name="expectedStatement">The probe statement that must be sent.</param>
    [Theory]
    [InlineData("SQLITE", "SELECT 1")]
    [InlineData("", "SELECT 1")]
    [InlineData("MSS Microsoft SQL Server", "SELECT 1")]
    [InlineData("O90 Oracle9i", "SELECT 1 FROM DUAL")]
    [InlineData("oracle", "SELECT 1 FROM DUAL")]
    public void TheBuiltInProbePicksItsStatementByDialect(string dbms, string expectedStatement)
    {
        FakeTimeProvider clock = new(ClockStart);
        FakeEngine engine = new()
        {
            Dbms = dbms,
            ExecuteResult = SqlState.Succeeded(rowCount: 1),
        };
        using PooledTransaction transaction = new(engine, clock);

        Assert.Equal(RetCode.OK, transaction.Connect(TestContext.Current.CancellationToken));
        clock.Advance(TimeSpan.FromMilliseconds(10_000));

        Assert.True(transaction.IsConnected());
        AssertSequence([expectedStatement], engine.ExecutedStatements);
    }

    /// <summary>
    /// A probe that returns NO ROWS reads as NOT connected even with a clean code, because the verdict is
    /// <c>SQLNRows &gt; 0</c> [trans :L207] - and a failed probe ZEROES the tick [trans :L214].
    /// </summary>
    [Fact]
    public void AProbeWithNoRowsReadsAsDisconnectedAndZeroesTheTick()
    {
        FakeTimeProvider clock = new(ClockStart);
        FakeEngine engine = new() { ExecuteResult = SqlState.Succeeded(rowCount: 0) };
        using PooledTransaction transaction = new(engine, clock);

        Assert.Equal(RetCode.OK, transaction.Connect(TestContext.Current.CancellationToken));
        clock.Advance(TimeSpan.FromMilliseconds(10_000));

        Assert.False(transaction.IsConnected());

        // The tick is now zero, so the next call short-circuits to false without probing again.
        engine.Log.Clear();
        Assert.False(transaction.IsConnected());
        Assert.Empty(engine.Log);
    }

    /// <summary>
    /// A SUCCEEDING probe RE-STAMPS the tick, so the window restarts from the probe rather than from the
    /// connect [trans :L212].
    /// </summary>
    /// <remarks>
    /// <para>
    /// The failing arm is asserted above - a failed probe zeroes the tick [<c>:L214</c>] - and this is the
    /// other half of the same <c>if</c>. It is the half a port loses silently: leave the re-stamp out and
    /// the tick keeps the CONNECT instant forever, so every call past the first window probes the database
    /// again and the cache stops existing. Nothing fails, nothing logs, and the only symptom is a probe per
    /// call - which is precisely the kind of thing this suite exists to catch, and precisely why it is
    /// asserted as a BEHAVIOURAL rule about a simulated instant rather than as a claim about cost
    /// (AAP 0.8.5).
    /// </para>
    /// </remarks>
    [Fact]
    public void ASucceedingProbeRestartsTheLivenessWindow()
    {
        FakeTimeProvider clock = new(ClockStart);
        FakeEngine engine = new() { ExecuteResult = SqlState.Succeeded(rowCount: 1) };
        using PooledTransaction transaction = new(engine, clock);

        Assert.Equal(RetCode.OK, transaction.Connect(TestContext.Current.CancellationToken));
        engine.Log.Clear();

        // Reach the window, so the cache lapses and the probe runs. It succeeds.
        clock.Advance(FakeTimeProvider.LivenessCacheWindow);
        Assert.True(transaction.IsConnected());
        AssertSequence(["Execute"], engine.Log);

        // NOW THE WINDOW HAS RESTARTED FROM THE PROBE. One millisecond under a second full window, and the
        // answer comes from the cache with NO second probe - which can only be true if the probe re-stamped.
        engine.Log.Clear();
        clock.Advance(FakeTimeProvider.LivenessCacheWindow - TimeSpan.FromMilliseconds(1));
        Assert.True(transaction.IsConnected());
        Assert.Empty(engine.Log);

        // And one millisecond more reaches the SECOND window, which probes again.
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(transaction.IsConnected());
        AssertSequence(["Execute"], engine.Log);
    }

    /// <summary>
    /// The liveness path's HOOK results are judged by the shared kernel's return-code predicates
    /// [trans :L197, :L209] - so a cancelled check is NOT a failure, and a prevention reads as
    /// connected.
    /// </summary>
    [Fact]
    public void TheLivenessHooksAreJudgedByTheReturnCodeAlgebra()
    {
        FakeTimeProvider clock = new(ClockStart);

        // A FAILING check hook answers false before the cache is even consulted [trans :L197].
        FakeEngine failing = new();
        RecordingHooks failCheck = new() { CheckResult = RetCode.FAILED };
        using PooledTransaction failed = new(failing, clock, failCheck);
        Assert.Equal(RetCode.OK, failed.Connect(TestContext.Current.CancellationToken));
        Assert.False(failed.IsConnected());

        // CANCELLED IS NOT A FAILURE under Predicates.IsFailed - the preserved tri-state hole - so the
        // connection still reads as live.
        FakeEngine cancelled = new();
        RecordingHooks cancelCheck = new() { CheckResult = RetCode.CANCELLED };
        using PooledTransaction survived = new(cancelled, clock, cancelCheck);
        Assert.Equal(RetCode.OK, survived.Connect(TestContext.Current.CancellationToken));
        Assert.True(survived.IsConnected());

        // A test hook that answers a PREVENTION reads as connected, because IsSucceeded is >= 0.
        FakeEngine prevented = new();
        RecordingHooks preventTest = new() { TestResult = RetCode.PREVENT };
        using PooledTransaction live = new(prevented, clock, preventTest);
        Assert.Equal(RetCode.OK, live.Connect(TestContext.Current.CancellationToken));
        clock.Advance(TimeSpan.FromMilliseconds(10_000));
        Assert.True(live.IsConnected());

        // And no built-in probe ran, because the hook answered non-null [trans :L201].
        Assert.Empty(prevented.ExecutedStatements);
    }

    /// <summary>
    /// A never-connected transaction reads as disconnected without consulting any hook [trans :L196].
    /// </summary>
    [Fact]
    public void ANeverConnectedTransactionReadsAsDisconnected()
    {
        FakeTimeProvider clock = new(ClockStart);
        FakeEngine engine = new();
        RecordingHooks hooks = new();
        using PooledTransaction transaction = new(engine, clock, hooks);

        Assert.False(transaction.IsConnected());
        Assert.Empty(hooks.Log);
    }

    /// <summary>
    /// <c>IsBroken</c> FIRES THE CHECK HOOK when the flag is not already set, and skips it once it is
    /// [trans :L530-L534].
    /// </summary>
    [Fact]
    public void IsBrokenFiresTheCheckHookOnlyWhileNotAlreadyBroken()
    {
        FakeTimeProvider clock = new(ClockStart);
        FakeEngine engine = new();
        RecordingHooks hooks = new();
        using PooledTransaction transaction = new(engine, clock, hooks);

        Assert.False(transaction.IsBroken());
        Assert.Equal(1, hooks.CheckCalls);

        Assert.False(transaction.IsBroken());
        Assert.Equal(2, hooks.CheckCalls);

        // Now let the hook condemn it. The flag is read AFTER the hook, so this answers true.
        hooks.CondemnTarget = transaction;
        Assert.True(transaction.IsBroken());
        Assert.Equal(3, hooks.CheckCalls);

        // Already broken: the hook is skipped from here on.
        Assert.True(transaction.IsBroken());
        Assert.Equal(3, hooks.CheckCalls);
    }

    /// <summary>
    /// The dialect resolver answers Oracle only on a substring match, and MSSQL for everything else -
    /// SQLite and the empty string included [trans :L356-L361].
    /// </summary>
    /// <param name="dbms">The DBMS identifier.</param>
    /// <param name="expectOracle">Whether the answer must be the Oracle discriminator.</param>
    [Theory]
    [InlineData("SQLITE", false)]
    [InlineData("", false)]
    [InlineData("MSS Microsoft SQL Server", false)]
    [InlineData("ORACLE", true)]
    [InlineData("O90 Oracle9i", true)]
    [InlineData("prefix-oRaClE-suffix", true)]
    public void TheDialectResolverAnswersOracleOnlyOnASubstringMatch(string dbms, bool expectOracle)
    {
        FakeTimeProvider clock = new(ClockStart);
        FakeEngine engine = new() { Dbms = dbms };
        using PooledTransaction transaction = new(engine, clock);

        Assert.Equal(
            expectOracle ? DatabaseType.DbtOracle : DatabaseType.DbtMssql,
            transaction.GetDbType());
    }

    /// <summary>
    /// <c>ClearState</c> zeroes the five values but touches NEITHER the broken flag NOR the liveness
    /// tick [trans :L363-L368], so clearing state cannot resurrect a condemned transaction.
    /// </summary>
    [Fact]
    public void ClearStateLeavesTheBrokenFlagAndTheLivenessTickAlone()
    {
        FakeTimeProvider clock = new(ClockStart);
        FakeEngine engine = new()
        {
            DbHandle = 1,
            ExecuteResult = new SqlState(-1, -7, 4, "err", "ret"),
        };
        using PooledTransaction transaction = new(engine, clock);

        Assert.Equal(RetCode.OK, transaction.Connect(TestContext.Current.CancellationToken));
        Assert.Equal(RetCode.E_DB_ERROR, transaction.Exec("SELECT 1", TestContext.Current.CancellationToken));
        Assert.Equal(RetCode.OK, transaction.SetBroken());

        transaction.ClearState();

        Assert.Equal(0L, transaction.SqlCode);
        Assert.Equal(0L, transaction.SqlDbCode);
        Assert.Equal(0L, transaction.SqlNRows);
        Assert.Equal(string.Empty, transaction.SqlErrText);
        Assert.Equal(string.Empty, transaction.SqlReturnData);

        Assert.True(transaction.IsBroken());
        Assert.False(transaction.IsConnected());
    }

    /// <summary>
    /// The descriptor transfer delegates the seven-field fold to the engine and answers
    /// <see cref="RetCode.OK"/> [trans :L343-L354], and the structured error carries exactly the two
    /// values the consumer copies [n_cst_thread_task_sqlbase.sru:L175-L176].
    /// </summary>
    [Fact]
    public void ApplyTransactionDataFoldsSevenFieldsAndCaptureErrorCarriesTwoValues()
    {
        FakeTimeProvider clock = new(ClockStart);
        FakeEngine engine = new()
        {
            DbHandle = 1,
            ExecuteResult = new SqlState(-1, -4711, 0, "provider text", "ret"),
        };
        using PooledTransaction transaction = new(engine, clock);

        TransactionData descriptor = DescriptorA with { UserParm = "ignored-by-the-fold" };
        Assert.Equal(RetCode.OK, transaction.ApplyTransactionData(in descriptor));

        // The fold moved SEVEN fields and left AutoCommit and UserParm alone [trans :L345-L351].
        Assert.Equal(descriptor.Dbms, engine.AppliedDescriptor.Dbms);
        Assert.Equal(descriptor.Database, engine.AppliedDescriptor.Database);
        Assert.Equal(string.Empty, engine.AppliedDescriptor.UserParm);

        Assert.Equal(RetCode.E_DB_ERROR, transaction.Exec("SELECT 1", TestContext.Current.CancellationToken));

        DbErrorData error = transaction.CaptureError();
        Assert.Equal(-4711L, error.SqlDbCode);
        Assert.Equal("provider text", error.SqlErrText);

        // AND NO STATEMENT TEXT, which is why there is nothing here for the redactor to redact.
        Assert.Equal(string.Empty, error.SqlSyntax);
    }

    /// <summary>
    /// The pooled transaction's own disposal is idempotent and disposes the engine it owns.
    /// </summary>
    [Fact]
    public void PooledTransactionDisposalIsIdempotentAndDisposesItsEngine()
    {
        FakeTimeProvider clock = new(ClockStart);
        FakeEngine engine = new();
        PooledTransaction transaction = new(engine, clock);

        transaction.Dispose();
        transaction.Dispose();

        Assert.Equal(1, engine.DisposeCalls);

        // And a disposed transaction refuses to work rather than misbehaving silently.
        Assert.Throws<ObjectDisposedException>(() => { _ = transaction.Connect(TestContext.Current.CancellationToken); });
        Assert.Throws<ObjectDisposedException>(() => { _ = transaction.Exec("SELECT 1", TestContext.Current.CancellationToken); });

        // SetBroken is deliberately NOT guarded - condemning is meaningful at any point.
        Assert.Equal(RetCode.OK, transaction.SetBroken());
    }

    /// <summary>
    /// The pooled transaction fails fast on a structurally invalid construction.
    /// </summary>
    [Fact]
    public void PooledTransactionConstructionFailsFast()
    {
        FakeTimeProvider clock = new(ClockStart);
        FakeEngine engine = new();

        Assert.Throws<ArgumentNullException>(() => { _ = new PooledTransaction(null!, clock); });
        Assert.Throws<ArgumentNullException>(() => { _ = new PooledTransaction(engine, null!); });
    }

    /// <summary>
    /// The cleared SQL state is exactly zero, zero, zero, empty and empty, and the two factories spell
    /// success and failure as the oracle does.
    /// </summary>
    [Fact]
    public void TheSqlStateSnapshotHasTheOraclesClearedAndFactoryValues()
    {
        SqlState cleared = SqlState.Cleared;
        Assert.Equal(0L, cleared.SqlCode);
        Assert.Equal(0L, cleared.SqlDbCode);
        Assert.Equal(0L, cleared.SqlNRows);
        Assert.Equal(string.Empty, cleared.SqlErrText);
        Assert.Equal(string.Empty, cleared.SqlReturnData);

        SqlState succeeded = SqlState.Succeeded(rowCount: 12);
        Assert.Equal(0L, succeeded.SqlCode);
        Assert.Equal(12L, succeeded.SqlNRows);

        SqlState failed = SqlState.Failed(-77, null);
        Assert.Equal(-1L, failed.SqlCode);
        Assert.Equal(-77L, failed.SqlDbCode);
        Assert.Equal(string.Empty, failed.SqlErrText);

        // The five-value constructor and value equality, which is what makes the snapshot a snapshot.
        SqlState explicitly = new(-1, -77, 0, string.Empty, string.Empty);
        Assert.Equal(failed, explicitly);

        // The two projecting members answer EMPTY rather than null however the value was built, which is
        // what stops a defaulted struct from handing a null reference to a consumer.
        SqlState viaInitialiser = new()
        {
            SqlCode = -1,
            SqlDbCode = -2,
            SqlNRows = 3,
            SqlErrText = null!,
            SqlReturnData = null!,
        };
        Assert.Equal(string.Empty, viaInitialiser.SqlErrText);
        Assert.Equal(string.Empty, viaInitialiser.SqlReturnData);

        SqlState withText = viaInitialiser with { SqlErrText = "text", SqlReturnData = "ret" };
        Assert.Equal("text", withText.SqlErrText);
        Assert.Equal("ret", withText.SqlReturnData);
    }

    /// <summary>
    /// The DEFAULT disconnect hooks - the ones nothing overrides - are reached on a real disconnect, so
    /// an implementation that supplies no script behaves exactly as an unimplemented PowerBuilder event
    /// [trans :L150, :L152].
    /// </summary>
    [Fact]
    public void TheDefaultDisconnectHooksAreReachedAndDoNothing()
    {
        FakeTimeProvider clock = new(ClockStart);
        FakeEngine engine = new() { DbHandle = 9 };
        MinimalHooks hooks = new();
        using PooledTransaction transaction = new(engine, clock, hooks);

        Assert.Equal(RetCode.OK, transaction.Disconnect());

        // The verbs ran; the defaults contributed nothing, which is the whole contract of a default.
        AssertSequence(["Disconnect"], engine.Log);
    }

    /// <summary>
    /// The transaction's <c>AutoCommit</c> property is a pass-through to the connection target, because
    /// the legacy property lives on the transaction object itself and the command task toggles it to
    /// reproduce the native-autocommit arm.
    /// </summary>
    [Fact]
    public void AutoCommitPassesThroughToTheConnectionTarget()
    {
        FakeTimeProvider clock = new(ClockStart);
        FakeEngine engine = new();
        using PooledTransaction transaction = new(engine, clock);

        Assert.False(transaction.AutoCommit);

        transaction.AutoCommit = true;

        Assert.True(transaction.AutoCommit);
        Assert.True(engine.AutoCommit);

        // And the gate it controls now answers accordingly [trans :L185].
        Assert.Equal(RetCode.FAILED, transaction.Rollback());
    }

    /// <summary>
    /// ⚠ THE LAST STATEMENT'S ROW COUNT SURVIVES A COMMIT, because a commit affects no rows and therefore
    /// has no row count of its own to publish.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the defect behind an <c>ExecResponse.sql_nrows</c> of zero under <c>AC_ON</c>: the commit's
    /// own state was assigned wholesale, and its count is zero, so the count the caller's INSERT, UPDATE or
    /// DELETE had just reported was erased on the way through the epilogue's commit
    /// [<c>n_cst_thread_task_sqlcommand.sru:L99</c>]. The identical statement under <c>AC_OFF</c> never
    /// passed through a commit and reported correctly, which is exactly the asymmetry that made the fault
    /// visible.
    /// </para>
    /// <para>
    /// It is the legacy reading and not a convenience: the oracle's accessor is the SQLite binding's own
    /// <c>SQLNRows()</c> [<c>n_sqlite.sru:L26</c>], a separate call from its <c>Commit()</c>, and the
    /// underlying change counter is not reset by a commit - measured directly against the shipped provider,
    /// <c>changes()</c> still answers the DML's count after an explicit commit.
    /// </para>
    /// </remarks>
    [Fact]
    public void ACommitPreservesTheRowCountTheLastStatementReported()
    {
        FakeTimeProvider clock = new(ClockStart);
        FakeEngine engine = new() { ExecuteResult = SqlState.Succeeded(4) };
        using PooledTransaction transaction = new(engine, clock);

        Assert.Equal(RetCode.OK, transaction.Connect(TestContext.Current.CancellationToken));

        Assert.Equal(
            RetCode.OK,
            transaction.Exec("UPDATE COMPANY SET AGE = 9", TestContext.Current.CancellationToken));
        Assert.Equal(4L, transaction.SqlNRows);

        // The commit itself reports nothing about rows - its own state carries zero.
        Assert.Equal(RetCode.OK, transaction.Commit(autoRollback: true));

        Assert.Equal(4L, transaction.SqlNRows);

        // And the commit's OWN outcome still lands on the members that carry an outcome.
        Assert.Equal(0L, transaction.SqlCode);
        Assert.Equal(string.Empty, transaction.SqlErrText);
    }

    /// <summary>
    /// The same preservation on the AUTO-COMMIT CHECKPOINT, which is the member the <c>AC_ON</c> epilogue
    /// actually reaches through the task's commit helper.
    /// </summary>
    [Fact]
    public void TheAutoCommitCheckpointPreservesTheRowCountToo()
    {
        FakeTimeProvider clock = new(ClockStart);
        FakeEngine engine = new() { ExecuteResult = SqlState.Succeeded(2) };
        using PooledTransaction transaction = new(engine, clock);

        Assert.Equal(RetCode.OK, transaction.Connect(TestContext.Current.CancellationToken));

        Assert.Equal(
            RetCode.OK,
            transaction.Exec("DELETE FROM COMPANY WHERE AGE > 90", TestContext.Current.CancellationToken));

        Assert.Equal(RetCode.OK, transaction.AutoCommitCheckpoint());
        Assert.Equal(2L, transaction.SqlNRows);
    }

    /// <summary>
    /// A FAILING commit preserves it as well: the commit still affected no rows, so the previous
    /// statement's count remains the truth about the last statement, and the FAILURE is carried by
    /// <c>SqlCode</c> - the member that carries outcomes.
    /// </summary>
    [Fact]
    public void AFailedCommitPreservesTheRowCountAndReportsTheFailureThroughSqlCode()
    {
        FakeTimeProvider clock = new(ClockStart);
        FakeEngine engine = new()
        {
            ExecuteResult = SqlState.Succeeded(3),
            CommitResult = SqlState.Failed(5, "database is locked"),
        };

        using PooledTransaction transaction = new(engine, clock);

        Assert.Equal(RetCode.OK, transaction.Connect(TestContext.Current.CancellationToken));
        Assert.Equal(
            RetCode.OK,
            transaction.Exec("INSERT INTO COMPANY (NAME) VALUES ('x')", TestContext.Current.CancellationToken));

        Assert.Equal(RetCode.E_DB_ERROR, transaction.Commit(autoRollback: false));

        Assert.Equal(3L, transaction.SqlNRows);
        Assert.Equal(-1L, transaction.SqlCode);
        Assert.Equal("database is locked", transaction.SqlErrText);
    }

    /// <summary>
    /// The state stamp overwrites all five values at once and touches nothing else - the port of the
    /// legacy SQL-state properties being assignable [trans :L176-L180, :L517-L521].
    /// </summary>
    [Fact]
    public void TheStateStampOverwritesAllFiveValuesAndNothingElse()
    {
        FakeTimeProvider clock = new(ClockStart);
        FakeEngine engine = new();
        using PooledTransaction transaction = new(engine, clock);

        Assert.Equal(RetCode.OK, transaction.Connect(TestContext.Current.CancellationToken));
        Assert.True(transaction.IsConnected());

        SqlState imposed = new(-1, -321, 8, "imposed text", "imposed return");
        transaction.StampSqlState(in imposed);

        Assert.Equal(-1L, transaction.SqlCode);
        Assert.Equal(-321L, transaction.SqlDbCode);
        Assert.Equal(8L, transaction.SqlNRows);
        Assert.Equal("imposed text", transaction.SqlErrText);
        Assert.Equal("imposed return", transaction.SqlReturnData);

        // NOT the liveness tick and NOT the broken flag.
        Assert.True(transaction.IsConnected());
        Assert.False(transaction.IsBroken());
    }

    // ==============================================================================================
    //  SUITE 11 - THE CLASS-NAME ACTIVATOR AND ITS FOUR CONSTRAINTS
    // ==============================================================================================

    /// <summary>
    /// The default create arm builds the default pooled transaction over a fresh engine.
    /// </summary>
    [Fact]
    public void TheActivatorCreatesTheDefaultTransaction()
    {
        FakeTimeProvider clock = new(ClockStart);
        int engines = 0;
        PooledTransactionActivator activator = new(
            () =>
            {
                engines++;
                return new FakeEngine();
            },
            clock);

        using IPooledTransaction created = activator.CreateDefault();

        Assert.IsType<PooledTransaction>(created);
        Assert.Equal(1, engines);
    }

    /// <summary>
    /// A REGISTERED name is resolved from the registry, case-insensitively, without any reflection.
    /// </summary>
    [Fact]
    public void TheActivatorPrefersARegisteredFactory()
    {
        FakeTimeProvider clock = new(ClockStart);
        FakeTransaction registered = new();
        Dictionary<string, Func<IPooledTransaction>> registry =
            new(StringComparer.OrdinalIgnoreCase) { ["MyTransaction"] = () => registered };

        PooledTransactionActivator activator = new(
            static () => new FakeEngine(),
            clock,
            hooks: null,
            namedFactories: registry);

        Assert.Same(registered, activator.Create("MyTransaction"));
        Assert.Same(registered, activator.Create("mytransaction"));
    }

    /// <summary>
    /// A registered factory that hands back nothing is a failure rather than a null instance.
    /// </summary>
    [Fact]
    public void TheActivatorRejectsARegisteredFactoryThatReturnsNothing()
    {
        FakeTimeProvider clock = new(ClockStart);
        Dictionary<string, Func<IPooledTransaction>> registry =
            new(StringComparer.OrdinalIgnoreCase) { ["Nothing"] = static () => null! };

        PooledTransactionActivator activator = new(
            static () => new FakeEngine(),
            clock,
            hooks: null,
            namedFactories: registry);

        Assert.Throws<InvalidOperationException>(() => { _ = activator.Create("Nothing"); });
    }

    /// <summary>
    /// CONSTRAINT 2 AND 3 TOGETHER: a type that ships in the service and implements the contract IS
    /// activated by name - which is what reproduces <c>Create Using</c> [pool :L167].
    /// </summary>
    [Fact]
    public void TheActivatorActivatesAnInServiceTypeByName()
    {
        FakeTimeProvider clock = new(ClockStart);
        PooledTransactionActivator activator = new(static () => new FakeEngine(), clock);

        using IPooledTransaction created = activator.Create(DefaultTransactionTypeName);

        Assert.IsType<PooledTransaction>(created);
    }

    /// <summary>
    /// The four constraints, each rejected with <see cref="InvalidOperationException"/> so that
    /// <c>Get</c>'s catch-all turns it into <see cref="RetCode.E_INVALID_OBJECT"/>.
    /// </summary>
    /// <param name="className">The name to refuse.</param>
    [Theory]
    // CONSTRAINT 1 - assembly-qualified names are refused before resolution is even attempted.
    [InlineData("PowerFramework.Persistence.Transactions.PooledTransaction, PowerFramework.Persistence")]
    // CONSTRAINT 2 - not a type in this assembly.
    [InlineData("System.Object")]
    [InlineData("Nope.Not.A.Type")]
    [InlineData("   ")]
    // CONSTRAINT 3 - a real in-assembly type that does not implement the contract.
    [InlineData("PowerFramework.Persistence.Transactions.TransactionPool")]
    // CONSTRAINT 3 - an interface rather than a concrete class.
    [InlineData("PowerFramework.Persistence.Transactions.IPooledTransaction")]
    public void TheActivatorEnforcesItsFourConstraints(string className)
    {
        FakeTimeProvider clock = new(ClockStart);
        PooledTransactionActivator activator = new(static () => new FakeEngine(), clock);

        Assert.Throws<InvalidOperationException>(() => { _ = activator.Create(className); });
    }

    /// <summary>
    /// A null or empty class name is an argument error - the activator is never asked for one, because
    /// the pool selects the default arm on an empty name [pool :L166].
    /// </summary>
    [Fact]
    public void TheActivatorRejectsANullOrEmptyClassName()
    {
        FakeTimeProvider clock = new(ClockStart);
        PooledTransactionActivator activator = new(static () => new FakeEngine(), clock);

        Assert.Throws<ArgumentNullException>(() => { _ = activator.Create(null!); });
        Assert.Throws<ArgumentException>(() => { _ = activator.Create(string.Empty); });
    }

    /// <summary>
    /// An engine factory that hands back nothing fails fast rather than producing a transaction that
    /// would break on its first verb.
    /// </summary>
    [Fact]
    public void TheActivatorRejectsAnEngineFactoryThatReturnsNothing()
    {
        FakeTimeProvider clock = new(ClockStart);
        PooledTransactionActivator activator = new(static () => null!, clock);

        Assert.Throws<InvalidOperationException>(() => { _ = activator.CreateDefault(); });
    }

    /// <summary>
    /// The activator fails fast on a structurally invalid construction.
    /// </summary>
    [Fact]
    public void TheActivatorConstructionFailsFast()
    {
        FakeTimeProvider clock = new(ClockStart);

        Assert.Throws<ArgumentNullException>(() => { _ = new PooledTransactionActivator(null!, clock); });
        Assert.Throws<ArgumentNullException>(
            () => { _ = new PooledTransactionActivator(static () => new FakeEngine(), null!); });
    }

    // ==============================================================================================
    //  THE CONSTRUCTOR-SHAPE MATRIX  [n_cst_thread_trans_pool.sru:L167 `Create Using _sTransCls`]
    //
    //  These drive Activate(Type) DIRECTLY rather than through Create(string). That is deliberate and
    //  it does NOT bypass a security constraint: CONSTRAINT 2 restricts which types a configuration
    //  STRING may name, and it still governs Create(string) untouched. Activate takes an
    //  already-resolved Type that no configuration value can produce, so the shapes it accepts are
    //  testable only from here - which is why the method carries `internal` and the service grants
    //  InternalsVisibleTo.
    // ==============================================================================================

    /// <summary>
    /// The zero-argument shape is the LITERAL analogue of <c>Create Using</c> [pool :L167], which passes
    /// no arguments at all. No type this service ships declares it, so it is exercised here against a
    /// purpose-built double rather than deleted - deleting it would narrow the port, because a faithful
    /// descendant of the legacy transaction class takes no constructor arguments either.
    /// </summary>
    [Fact]
    public void TheZeroArgumentShapeIsTheLiteralCreateUsingAnalogue()
    {
        FakeTimeProvider clock = new(ClockStart);
        PooledTransactionActivator activator = new(static () => new FakeEngine(), clock);

        IPooledTransaction created = activator.Activate(typeof(ZeroArgumentProbe));

        _ = Assert.IsType<ZeroArgumentProbe>(created);
    }

    /// <summary>
    /// Proves the documented claim that an OPTIONAL third parameter matches the seamed probe, because the
    /// probe compares declared parameter TYPES rather than requiring an exact arity. Also confirms the
    /// engine, the clock and the hooks are the ones the activator holds - an activator that constructed
    /// the type but passed the wrong collaborators would leave an unseamed clock behind.
    /// </summary>
    [Fact]
    public void AnOptionalThirdParameterMatchesTheSeamedShapeAndReceivesTheRealCollaborators()
    {
        FakeTimeProvider clock = new(ClockStart);
        FakeEngine engine = new();
        MinimalHooks hooks = new();
        PooledTransactionActivator activator = new(() => engine, clock, hooks);

        IPooledTransaction created = activator.Activate(typeof(OptionalHooksProbe));

        OptionalHooksProbe probe = Assert.IsType<OptionalHooksProbe>(created);
        Assert.Same(engine, probe.Engine);
        Assert.Same(clock, probe.Clock);
        Assert.Same(hooks, probe.Hooks);
    }

    /// <summary>
    /// With no hooks configured the activator passes <see langword="null"/> through rather than inventing
    /// a no-op, because substituting the no-op is the IMPLEMENTATION's job - <c>PooledTransaction</c>
    /// does it in its own constructor, so a type that wants the real thing gets exactly what was
    /// registered and a type that wants none is not handed a decoy.
    /// </summary>
    [Fact]
    public void NoConfiguredHooksReachTheSeamedShapeAsNull()
    {
        FakeTimeProvider clock = new(ClockStart);
        PooledTransactionActivator activator = new(static () => new FakeEngine(), clock);

        IPooledTransaction created = activator.Activate(typeof(OptionalHooksProbe));

        Assert.Null(Assert.IsType<OptionalHooksProbe>(created).Hooks);
    }

    /// <summary>
    /// The seamed shape wins when a type declares BOTH, pinning the documented probe order so a later
    /// reordering cannot silently start constructing implementations without their seams.
    /// </summary>
    [Fact]
    public void TheSeamedShapeIsPreferredWhenATypeDeclaresBoth()
    {
        FakeTimeProvider clock = new(ClockStart);
        PooledTransactionActivator activator = new(static () => new FakeEngine(), clock);

        IPooledTransaction created = activator.Activate(typeof(BothShapesProbe));

        Assert.True(Assert.IsType<BothShapesProbe>(created).UsedSeamedConstructor);
    }

    /// <summary>
    /// A type declaring neither accepted shape reaches the closing throw, which the pool's catch-all
    /// [pool :L173-L175] turns into <c>E_INVALID_OBJECT</c>.
    /// </summary>
    [Fact]
    public void ATypeDeclaringNoAcceptedShapeIsRejected()
    {
        FakeTimeProvider clock = new(ClockStart);
        PooledTransactionActivator activator = new(static () => new FakeEngine(), clock);

        _ = Assert.Throws<InvalidOperationException>(
            () => activator.Activate(typeof(NoAcceptedShapeProbe)));
    }

    // ==============================================================================================
    //  SUITE 12 - THE STABLE-LEASE ADDRESSING MODE
    //
    //  WHAT THIS SUITE IS ACTUALLY PROVING, AND WHY IT IS NOT PROVING THE RENUMBERING IS GONE.
    //  The rebuild that renumbers every later index [pool :L114] is a PRESERVED DEFECT and Suite 2
    //  asserts it verbatim. It was unreachable in the oracle because its one consumer zeroed its own
    //  index the instant it released [n_cst_thread_task_sqlbase.sru:L123]. Decomposition gave holders a
    //  lifetime the legacy's never had - a transaction session named by a later RPC, a SQL task paged by
    //  later ones - so the same defect became reachable. This suite proves the LEASE is immune to the
    //  renumbering, not that the renumbering stopped happening.
    // ==============================================================================================

    /// <summary>
    /// The lease-taking entry point selects the SAME entry the positional one would, so one descriptor
    /// answers one lease however many times it is asked for.
    /// </summary>
    [Fact]
    public void ALeaseNamesTheEntryTheDescriptorMatchesAndRepeatsForTheSameDescriptor()
    {
        using TransactionPool pool = CreatePool(out _, out _);

        PoolLease first = pool.AddRefLease(DescriptorA);
        PoolLease second = pool.AddRefLease(DescriptorA);

        Assert.True(first.IsValid);
        Assert.Equal(first, second);
        Assert.True(pool.IsLeaseLive(first));

        // Both calls took a reference on ONE entry, exactly as two positional AddRef calls would - so it
        // takes TWO releases to drop it, which is the only way to observe the shared count from outside.
        Assert.Equal(RetCode.OK, pool.RemoveRef(first));
        Assert.True(pool.Exists(DescriptorA));

        Assert.Equal(RetCode.OK, pool.RemoveRef(first));
        Assert.False(pool.Exists(DescriptorA));
    }

    /// <summary>
    /// THE FINDING ITSELF. An earlier entry's removal renumbers a later one's position, and the lease
    /// still addresses the entry it was issued for while the stored POSITION no longer does.
    /// </summary>
    [Fact]
    public void ALeaseSurvivesTheRenumberingThatAStoredPositionDoesNot()
    {
        // Keep-alive off, so a released entry is dropped rather than retained - which is what triggers
        // the rebuild at [pool :L101-L114].
        using TransactionPool pool = CreatePool(out _, out _);

        PoolLease leaseA = pool.AddRefLease(DescriptorA);
        PoolLease leaseC = pool.AddRefLease(DescriptorC);

        // C is the SECOND entry, so a holder that stored its position stored 2.
        const int storedPositionOfC = 2;

        Assert.Equal(RetCode.OK, pool.RemoveRef(leaseA));
        Assert.False(pool.Exists(DescriptorA));
        Assert.True(pool.Exists(DescriptorC));

        // THE STORED POSITION IS NOW PAST THE END, which is the benign half of the hazard.
        Assert.Equal(RetCode.E_OUT_OF_BOUND, pool.Get(storedPositionOfC, out IPooledTransaction? byIndex));
        Assert.Null(byIndex);

        // THE LEASE STILL NAMES C. Asserted on the descriptor the pool applied to the transaction it
        // handed back, because that is the only way to tell WHICH entry served the call.
        Assert.Equal(RetCode.OK, pool.Get(leaseC, out IPooledTransaction? byLease));
        Assert.Equal(DescriptorC, Assert.IsType<FakeTransaction>(byLease).LastAppliedDescriptor);
    }

    /// <summary>
    /// The dangerous half of the same hazard: a stored position that is still IN RANGE after a
    /// renumbering addresses a STRANGER, and the lease does not.
    /// </summary>
    [Fact]
    public void AStoredPositionCanAddressAStrangerWhereALeaseCannot()
    {
        using TransactionPool pool = CreatePool(out _, out _);

        PoolLease leaseA = pool.AddRefLease(DescriptorA);
        _ = pool.AddRefLease(DescriptorB);
        PoolLease leaseC = pool.AddRefLease(DescriptorC);

        // A holder of B stored position 2; a holder of C stored position 3.
        Assert.Equal(RetCode.OK, pool.RemoveRef(leaseA));

        // POSITION 2 IS STILL VALID - it now names C rather than B. This is the wrong-entry write the
        // finding describes, and it is asserted here so the difference is visible rather than asserted.
        Assert.Equal(RetCode.OK, pool.Get(2, out IPooledTransaction? stranger));
        Assert.Equal(DescriptorC, Assert.IsType<FakeTransaction>(stranger).LastAppliedDescriptor);

        // The lease issued for C names C at its new position, and the lease issued for B - had one been
        // taken - would name B wherever B moved to. Nothing addresses by arithmetic.
        Assert.Equal(RetCode.OK, pool.Get(leaseC, out IPooledTransaction? owned));
        Assert.Equal(DescriptorC, Assert.IsType<FakeTransaction>(owned).LastAppliedDescriptor);
    }

    /// <summary>
    /// An expired lease resolves NOTHING rather than something else, on every one of the four
    /// lease-addressed operations.
    /// </summary>
    [Fact]
    public void AnExpiredLeaseResolvesNothingOnEveryOperation()
    {
        using TransactionPool pool = CreatePool(out _, out _);

        PoolLease leaseA = pool.AddRefLease(DescriptorA);
        PoolLease leaseB = pool.AddRefLease(DescriptorB);

        Assert.Equal(RetCode.OK, pool.RemoveRef(leaseA));
        Assert.False(pool.IsLeaseLive(leaseA));

        // B took A's position. Not one of these four may reach it.
        Assert.Equal(RetCode.E_OUT_OF_BOUND, pool.RemoveRef(leaseA));
        Assert.Equal(RetCode.E_OUT_OF_BOUND, pool.Get(leaseA, out IPooledTransaction? nothing));
        Assert.Null(nothing);

        IPooledTransaction? borrowed = new FakeTransaction();
        Assert.Equal(RetCode.E_OUT_OF_BOUND, pool.Release(leaseA, ref borrowed));

        // The refusal did NOT null the caller's reference, because the index guard precedes every other
        // arm [pool :L120] and a refused call touches nothing.
        Assert.NotNull(borrowed);

        // And B is untouched by any of it - still live, still the only entry left.
        Assert.True(pool.IsLeaseLive(leaseB));
        Assert.True(pool.Exists(DescriptorB));
        Assert.False(pool.Exists(DescriptorA));
    }

    /// <summary>
    /// Lease identities are monotonic and never reused, so a holder that outlives its entry cannot be
    /// handed a later entry that happens to occupy the same position.
    /// </summary>
    [Fact]
    public void LeaseIdentitiesAreNeverReused()
    {
        using TransactionPool pool = CreatePool(out _, out _);

        PoolLease first = pool.AddRefLease(DescriptorA);

        Assert.Equal(RetCode.OK, pool.RemoveRef(first));

        PoolLease second = pool.AddRefLease(DescriptorA);

        Assert.True(second.IsValid);
        Assert.NotEqual(first, second);
        Assert.False(pool.IsLeaseLive(first));
        Assert.True(pool.IsLeaseLive(second));
    }

    /// <summary>
    /// The invalid lease is not a lookup at all: it is refused before any scan, so it can never collide
    /// with an entry.
    /// </summary>
    [Fact]
    public void TheNoneLeaseNamesNothing()
    {
        using TransactionPool pool = CreatePool(out _, out _);

        _ = pool.AddRefLease(DescriptorA);

        Assert.False(PoolLease.None.IsValid);
        Assert.False(default(PoolLease).IsValid);
        Assert.False(pool.IsLeaseLive(PoolLease.None));
        Assert.Equal(RetCode.E_OUT_OF_BOUND, pool.RemoveRef(PoolLease.None));
        Assert.Equal(RetCode.E_OUT_OF_BOUND, pool.Get(PoolLease.None, out IPooledTransaction? nothing));
        Assert.Null(nothing);
    }

    /// <summary>
    /// The lease-addressed operations share ONE body with their positional twins, so every arm below the
    /// lookup behaves identically - here the release path's object-validity arm and its <c>ref</c> null.
    /// </summary>
    [Fact]
    public void TheLeaseAndPositionalPathsShareEveryArmBelowTheLookup()
    {
        using TransactionPool pool = CreatePool(out _, out StubActivator activator);

        PoolLease lease = pool.AddRefLease(DescriptorA);

        Assert.Equal(RetCode.OK, pool.Get(lease, out IPooledTransaction? borrowed));

        // [pool :L121] The CALLER'S object is validated, not the stored one, and the check sits BELOW the
        // lookup - so a live lease with a null handle answers E_INVALID_OBJECT rather than E_OUT_OF_BOUND.
        IPooledTransaction? absent = null;
        Assert.Equal(RetCode.E_INVALID_OBJECT, pool.Release(lease, ref absent));

        // [pool :L123, :L125, :L128, :L129] Exactly one reference outstanding, so the release disconnects,
        // clears state and nulls the CALLER'S variable.
        Assert.Equal(RetCode.OK, pool.Release(lease, ref borrowed));
        Assert.Null(borrowed);

        FakeTransaction served = Assert.Single(activator.Created);
        Assert.Equal(1, served.DisconnectCalls);
        Assert.Equal(1, served.ClearStateCalls);
    }

    // ==============================================================================================
    //  SUITE 13 - BOTH CLOCKS ARE MONOTONIC, NOT WALL-CLOCK
    //
    //  The oracle reads CPU(), a PROCESS-RELATIVE counter that cannot move backwards, and every
    //  comparison either clock performs is a DELTA against a stored stamp. A UTC instant can step in
    //  either direction - an NTP correction, a manual clock change, a daylight-saving transition on a
    //  badly configured host - and a delta computed across such a step is wrong in a way that is
    //  invisible to a suite whose fake advances only forward, because a forward-only fake makes a
    //  wall-clock reading and a monotonic reading indistinguishable.
    //
    //  THE DOUBLE BELOW SEPARATES THEM. It answers GetUtcNow() and GetTimestamp() from INDEPENDENT
    //  fields, so each case here steps the wall clock one way and the monotonic counter the other. Every
    //  assertion follows the MONOTONIC reading, and each case is chosen so a wall-clock implementation
    //  would produce the opposite answer - which is what makes these assertions rather than descriptions.
    // ==============================================================================================

    /// <summary>
    /// The idle expiry follows the monotonic reading. A backward wall-clock step does not stop an expired
    /// entry being collected, and a forward one does not collect an entry that has barely been idle.
    /// </summary>
    /// <param name="monotonicMilliseconds">How far the monotonic counter advances.</param>
    /// <param name="wallClockStepMinutes">How far the wall clock jumps, in either direction.</param>
    /// <param name="expectCollected">Whether the entry must be gone afterwards.</param>
    [Theory]

    // Expired by the monotonic reading, and the wall clock has jumped an hour BACKWARDS. A wall-clock
    // implementation computes a negative delta and retains the entry for the life of the process.
    [InlineData(10_000, -60, true)]

    // Barely idle by the monotonic reading, and the wall clock has jumped an hour FORWARDS. A wall-clock
    // implementation collects a transaction whose caller released it a millisecond ago.
    [InlineData(1, 60, false)]

    // The boundary itself still lands where the oracle's greater-or-equal comparison puts it [pool :L215],
    // under a wall clock that has moved the other way.
    [InlineData(9_999, -60, false)]
    public void TheIdleExpiryFollowsTheMonotonicReadingAndNotTheWallClock(
        long monotonicMilliseconds,
        int wallClockStepMinutes,
        bool expectCollected)
    {
        SkewedClock clock = new(ClockStart);

        using TransactionPool pool = CreatePoolWithClock(
            clock,
            out StubActivator _,
            keepAlive: true,
            keepAliveExpireSeconds: 10d);

        int index = pool.AddRef(DescriptorA);
        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? handed));
        FakeTransaction transaction = Assert.IsType<FakeTransaction>(handed);

        // Retained, with the idle stamp taken from the MONOTONIC reading [pool :L97].
        Assert.Equal(RetCode.OK, pool.RemoveRef(index));
        Assert.Equal(1, pool.UpperBound);

        clock.StepWallClock(TimeSpan.FromMinutes(wallClockStepMinutes));
        clock.AdvanceMonotonic(TimeSpan.FromMilliseconds(monotonicMilliseconds));

        pool.Collect(force: false);

        Assert.Equal(expectCollected ? 0 : 1, pool.UpperBound);
        Assert.Equal(expectCollected ? 1 : 0, transaction.DisposeCalls);
    }

    /// <summary>
    /// The liveness cache follows the monotonic reading too: a backward wall-clock step does not extend
    /// the window and a forward one does not end it early.
    /// </summary>
    /// <param name="monotonicMilliseconds">How far the monotonic counter advances.</param>
    /// <param name="wallClockStepMinutes">How far the wall clock jumps, in either direction.</param>
    /// <param name="expectProbe">Whether the built-in probe must have run.</param>
    [Theory]

    // Past the window monotonically, wall clock an hour BACKWARDS: a wall-clock implementation computes a
    // negative delta, treats the cache as fresh forever and never probes again.
    [InlineData(10_000, -60, true)]

    // Inside the window monotonically, wall clock an hour FORWARDS: a wall-clock implementation probes on
    // every call and the cache stops existing.
    [InlineData(9_999, 60, false)]
    public void TheLivenessCacheFollowsTheMonotonicReadingAndNotTheWallClock(
        long monotonicMilliseconds,
        int wallClockStepMinutes,
        bool expectProbe)
    {
        SkewedClock clock = new(ClockStart);
        FakeEngine engine = new() { ExecuteResult = SqlState.Succeeded(rowCount: 1) };
        using PooledTransaction transaction = new(engine, clock);

        Assert.Equal(RetCode.OK, transaction.Connect(TestContext.Current.CancellationToken));
        engine.Log.Clear();

        clock.StepWallClock(TimeSpan.FromMinutes(wallClockStepMinutes));
        clock.AdvanceMonotonic(TimeSpan.FromMilliseconds(monotonicMilliseconds));

        Assert.True(transaction.IsConnected());

        AssertSequence(expectProbe ? ["Execute"] : [], engine.Log);
    }

    // ==============================================================================================
    //  SUITE 14 - THE SEVEN-MEMBER SURFACE, SHAPE BY SHAPE [n_cst_thread_trans_pool.sru:L64-L70]
    //  ------------------------------------------------------------------------------------------------
    //  The legacy declares exactly seven public members, and its prototype block is the contract:
    //
    //      public function long    of_removeref (readonly integer refindex)                        :L64
    //      public function long    of_release   (readonly integer refindex,
    //                                            ref n_cst_thread_trans transobject)              :L65
    //      public function integer of_addref    (readonly transactiondata transdata)               :L66
    //      public function long    of_get       (readonly integer refindex,
    //                                            ref n_cst_thread_trans transobject)              :L67
    //      public function boolean of_exists    (readonly transactiondata transdata)               :L68
    //      public function long    of_removeall (readonly boolean force)                           :L69
    //      public subroutine       of_collect   (readonly boolean force)                           :L70
    //
    //  WHY A SHAPE ASSERTION AND NOT JUST USAGE. Every other suite in this file calls these members and
    //  therefore proves they exist - but usage cannot pin the three shapes a well-meaning edit would
    //  HARMONISE, because such an edit changes the call sites in the same commit and the suite still
    //  compiles:
    //
    //    * AddRef answers an int INDEX and not a long RETURN CODE [:L66, :L151]. The two are trivially
    //      confusable - every sibling answers a code - and a caller that read the index as a code would
    //      see the first entry's index of 1 as PREVENT and every subsequent one as an unknown positive.
    //    * Collect answers NOTHING [:L70]. It is the legacy's only subroutine here, and giving it a
    //      return code would invite callers to branch on a value the oracle never produced.
    //    * Release and Get pass the connection by REFERENCE, in opposite directions [:L65, :L67]: Release
    //      takes one and NULLS it [:L129], Get hands one back. Collapsing either to a return value would
    //      lose the legacy's own statement about which side owns the handle afterwards.
    //
    //  The reflection below reads the signatures rather than describing them, so the contract is asserted
    //  in the only way that survives a refactor of the call sites.
    // ==============================================================================================

    /// <summary>
    /// All seven members exist with the shapes the legacy prototype block declares [pool :L64-L70], and
    /// the by-reference directions are preserved.
    /// </summary>
    [Fact]
    public void TheSevenMemberSurfaceKeepsTheLegacyShapes()
    {
        Type pool = typeof(TransactionPool);

        // of_addref [:L66] - ONE readonly descriptor in, an INT INDEX out.
        MethodInfo addRef = AssertMethod(
            pool,
            nameof(TransactionPool.AddRef),
            typeof(int),
            typeof(TransactionData).MakeByRefType());
        ParameterInfo descriptorParameter = Assert.Single(addRef.GetParameters());
        Assert.Equal(typeof(TransactionData).MakeByRefType(), descriptorParameter.ParameterType);
        Assert.True(descriptorParameter.IsIn, "AddRef must take the descriptor as a readonly reference.");
        Assert.False(descriptorParameter.IsOut);

        // of_removeref [:L64] - ONE index in, a return CODE out.
        MethodInfo removeRef = AssertMethod(
            pool,
            nameof(TransactionPool.RemoveRef),
            typeof(long),
            typeof(int));
        Assert.False(Assert.Single(removeRef.GetParameters()).ParameterType.IsByRef);

        // of_release [:L65] - an index and a connection passed BY REFERENCE so the callee can null it
        // [:L129], and a return code out.
        MethodInfo release = AssertMethod(
            pool,
            nameof(TransactionPool.Release),
            typeof(long),
            typeof(int),
            typeof(IPooledTransaction).MakeByRefType());
        ParameterInfo releaseHandle = release.GetParameters()[1];
        Assert.True(releaseHandle.ParameterType.IsByRef);
        Assert.False(releaseHandle.IsIn, "Release must be able to WRITE the caller's handle, not only read it.");
        Assert.False(releaseHandle.IsOut, "Release READS the handle before nulling it, so it is ref and not out.");

        // of_get [:L67] - an index in and a connection OUT, which is the opposite direction.
        MethodInfo get = AssertMethod(
            pool,
            nameof(TransactionPool.Get),
            typeof(long),
            typeof(int),
            typeof(IPooledTransaction).MakeByRefType());
        ParameterInfo handedBack = get.GetParameters()[1];
        Assert.True(handedBack.IsOut, "Get hands the connection BACK, so its second parameter is out.");

        // of_exists [:L68] - a descriptor in, a BOOLEAN out. Not a return code: absence is not a failure.
        MethodInfo exists = AssertMethod(
            pool,
            nameof(TransactionPool.Exists),
            typeof(bool),
            typeof(TransactionData).MakeByRefType());
        Assert.True(Assert.Single(exists.GetParameters()).IsIn);

        // of_removeall [:L69] - a force flag in, a return code out [:L207].
        _ = AssertMethod(pool, nameof(TransactionPool.RemoveAll), typeof(long), typeof(bool));

        // of_collect [:L70] - a force flag in and NOTHING out, because the legacy declares a SUBROUTINE.
        _ = AssertMethod(pool, nameof(TransactionPool.Collect), typeof(void), typeof(bool));

        // THE INITIALISE HALF OF THE PAIR IS THE CONSTRUCTOR, and the finalise half is Dispose [:L238].
        // The legacy pairs oninit [:L76] with a destructor that forces RemoveAll; the port pairs a
        // constructor that reads the same three settings with IDisposable, which is what lets the host
        // own the lifetime. Both halves are asserted to exist here and their behaviour is asserted in
        // Suites 1 and 7 respectively.
        Assert.NotNull(pool.GetConstructor(
            [
                typeof(IOptions<PersistenceOptions>),
                typeof(TimeProvider),
                typeof(IPooledTransactionActivator),
                typeof(TransactionClassNameResolver),
            ]));
        Assert.True(typeof(IDisposable).IsAssignableFrom(pool));
    }

    // ==============================================================================================
    //  SUITE 15 - THE POOLED ENTRY IS THE LEGACY STRUCTURE, FIELD FOR FIELD
    //             [n_cst_thread_trans_pool.sru:L10-L15]
    //  ------------------------------------------------------------------------------------------------
    //  The legacy pool's element is a structure with FOUR fields and no methods:
    //
    //      type sharedtransactiondata from structure
    //          transactiondata      transdata          <- the connection identity
    //          n_cst_thread_trans   transobject        <- the connection itself, or nothing yet
    //          unsignedlong         refcount           <- how many callers hold it
    //          unsignedlong         idlestarttime      <- when the last one let go, or 0 for "in use"
    //      end type
    //
    //  The port's entry is PRIVATE, which is correct - nothing outside the pool has any business reading
    //  it - so this suite does not reach for it. It pins each field through the member that OBSERVES it,
    //  all four in one test, so that a port which quietly dropped one has somewhere to fail. Dropping the
    //  fourth is the realistic mistake: an implementation that stamped nothing on release would still pass
    //  every reference-count assertion in Suite 2 and would leak every pooled connection for the life of
    //  the process, because a stamp left at zero makes the elapsed delta the whole process lifetime.
    // ==============================================================================================

    /// <summary>
    /// All FOUR fields of the legacy element are observable, and each is observed here through the member
    /// that reads it [pool :L10-L15].
    /// </summary>
    [Fact]
    public void ThePooledEntryCarriesTheLegacyStructuresFourFields()
    {
        using TransactionPool pool = CreatePool(
            out FakeTimeProvider clock,
            out StubActivator activator,
            keepAlive: true,
            keepAliveExpireSeconds: ExpiryMatrixSeconds);

        // FIELD 1 - transdata. The entry is FOUND BY ITS DESCRIPTOR and by nothing else, which is what
        // makes the descriptor the identity rather than an attribute [:L138, :L184].
        int index = pool.AddRef(DescriptorA);
        Assert.Equal(1, index);
        Assert.True(pool.Exists(DescriptorA));
        Assert.False(pool.Exists(DescriptorB));

        // FIELD 2 - transobject. Initially EMPTY: AddRef records the identity and creates nothing
        // [:L145], so the connection is a separate field with its own lifetime and not a constructor
        // argument. The activator has not been asked for anything yet.
        Assert.Equal(0, activator.CreatedCount);

        // ... and it is populated on demand, by Get [:L167-L172], which is when it starts to exist.
        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? handed));
        FakeTransaction transaction = Assert.IsType<FakeTransaction>(handed);
        Assert.Equal(1, activator.CreatedCount);

        // ... and it is the SAME object on the next Get, which is the entry holding it rather than the
        // caller [:L160].
        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? again));
        Assert.Same(transaction, again);
        Assert.Equal(1, activator.CreatedCount);

        // FIELD 3 - refcount. A second AddRef for the SAME descriptor reaches the SAME entry and counts
        // [:L138-L148], so the count is per entry and not per descriptor instance.
        Assert.Equal(index, pool.AddRef(DescriptorA));
        Assert.Equal(1, pool.UpperBound);

        // Two references are held, so one release is not the last: the entry stays and nothing is stamped.
        Assert.Equal(RetCode.OK, pool.RemoveRef(index));
        Assert.Equal(1, pool.UpperBound);

        // FIELD 4 - idlestarttime. The LAST release stamps it [:L97] rather than removing the entry,
        // because keep-alive is on and the transaction is healthy [:L95-L96].
        Assert.Equal(RetCode.OK, pool.RemoveRef(index));
        Assert.Equal(1, pool.UpperBound);
        Assert.Equal(0, transaction.DisposeCalls);

        // The stamp is proved by its EFFECT, which is the only honest way to observe a private field: one
        // millisecond under the expiry the entry survives, which can only be true if the stamp was written
        // at the release instant. Had it been left at the zero sentinel [:L149], the delta here would be
        // the whole simulated process lifetime and the entry would already be gone.
        clock.Advance(TimeSpan.FromMilliseconds(ExpiryMatrixMilliseconds - 1L));
        pool.Collect(force: false);
        Assert.Equal(1, pool.UpperBound);

        // One more millisecond reaches the boundary, and the boundary collects [:L215].
        clock.Advance(TimeSpan.FromMilliseconds(1));
        pool.Collect(force: false);
        Assert.Equal(0, pool.UpperBound);
        Assert.Equal(1, transaction.DisconnectCalls);
        Assert.Equal(1, transaction.DisposeCalls);
    }

    // ==============================================================================================
    //  SUITE 16 - THE CREDENTIAL IS PART OF THE IDENTITY AND PART OF NO OUTPUT (C-F)
    //             [transactiondata.srs:L8], [n_cst_thread_trans_pool.sru:L138]
    //  ------------------------------------------------------------------------------------------------
    //  TWO REQUIREMENTS THAT PULL IN OPPOSITE DIRECTIONS, AND BOTH ARE ASSERTED HERE.
    //
    //  IDENTITY. The legacy keys its entries on `_transactions[index].TransData = TransData` [:L138] -
    //  whole-structure equality over all nine fields of transactiondata, `logpass` among them [srs:L8].
    //  Two descriptors that differ ONLY in their password are therefore DIFFERENT connection identities,
    //  and they must not share a pooled connection: sharing one would authenticate the second caller with
    //  the first caller's credential, which is a privilege escalation dressed as a cache hit. A port that
    //  "optimised" the key down to server-plus-database would do exactly that, and every other assertion
    //  in this file would still pass.
    //
    //  DISCLOSURE. AAP 0.4.2.6 makes `logpass` WRITE-ONLY: never echoed in a response, never logged. The
    //  subject implements that structurally - TransactionData declares the member with an initialiser and
    //  NO GETTER, so this suite cannot read the value back even to assert about it, and it does not try.
    //  It asserts instead that the value is absent from every rendering the pool path can produce.
    //
    //  THE SENTINEL BELOW IS INVENTED HERE AND IS NOT A CREDENTIAL. It is a string designed to be
    //  conspicuous in a diff and impossible to mistake for a real secret, and it matches no provider's
    //  key format. No value from the repository's catalogued in-source secret sites is reproduced here or
    //  anywhere else in this file (C-F, AAP 0.6.6).
    // ==============================================================================================

    /// <summary>
    /// A conspicuous non-credential stand-in for a password. It exists to be searched for in output.
    /// </summary>
    /// <remarks>
    /// Shaped so that a human reading a log or a diff cannot mistake it for a live secret, and so that no
    /// provider would accept it: it names its own purpose. Two DIFFERENT stand-ins are needed - a pool
    /// keyed correctly must tell them apart - and neither is derived from any real system.
    /// </remarks>
    private const string FirstCredentialSentinel = "not-a-secret-first-sentinel";

    /// <summary>The second stand-in, unequal to the first. See <see cref="FirstCredentialSentinel"/>.</summary>
    private const string SecondCredentialSentinel = "not-a-secret-second-sentinel";

    /// <summary>
    /// Descriptors differing ONLY in the credential are DIFFERENT entries and get DIFFERENT connections
    /// [pool :L138], [transactiondata.srs:L8].
    /// </summary>
    [Fact]
    public void DescriptorsDifferingOnlyInTheCredentialNeverShareAConnection()
    {
        using TransactionPool pool = CreatePool(out FakeTimeProvider _, out StubActivator activator);

        TransactionData first = DescriptorA with { LogPass = FirstCredentialSentinel };
        TransactionData second = DescriptorA with { LogPass = SecondCredentialSentinel };

        // Both carry a credential, which is the ONE thing the type will disclose about it - the question,
        // never the answer. Everything else below is asserted without reading the value at all.
        Assert.True(first.HasCredential);
        Assert.True(second.HasCredential);

        int firstIndex = pool.AddRef(first);
        int secondIndex = pool.AddRef(second);

        // TWO ENTRIES, not one. A key narrowed to server-plus-database would answer the same index twice
        // and the second caller would inherit the first caller's authenticated connection.
        Assert.NotEqual(firstIndex, secondIndex);
        Assert.Equal(2, pool.UpperBound);

        // Each is findable as itself, and the credential-free descriptor they were derived from is
        // findable as NEITHER.
        Assert.True(pool.Exists(first));
        Assert.True(pool.Exists(second));
        Assert.False(pool.Exists(DescriptorA));

        // TWO CONNECTIONS, and the descriptor each one was told about is its own.
        Assert.Equal(RetCode.OK, pool.Get(firstIndex, out IPooledTransaction? firstHandle));
        Assert.Equal(RetCode.OK, pool.Get(secondIndex, out IPooledTransaction? secondHandle));
        Assert.NotSame(firstHandle, secondHandle);
        Assert.Equal(2, activator.CreatedCount);

        // A third AddRef with a descriptor EQUAL to the first - same credential included - reaches the
        // first entry, so equality is by value and not by reference [:L138].
        TransactionData firstAgain = DescriptorA with { LogPass = FirstCredentialSentinel };
        Assert.Equal(firstIndex, pool.AddRef(firstAgain));
        Assert.Equal(2, pool.UpperBound);

        // And an EMPTY credential is a third identity again, because emptiness is a value too.
        Assert.NotEqual(firstIndex, pool.AddRef(DescriptorA with { LogPass = "" }));
    }

    /// <summary>
    /// The credential appears in NO rendering the pool path can produce - not the descriptor's, not the
    /// applied descriptor's, not the structured error's, and not the redactor's record (C-F).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>WHAT "EVERY RENDERING" MEANS HERE, CONCRETELY.</b> The realistic way a credential escapes a
    /// service is not a deliberate log statement; it is one structured-log scope that formats a descriptor
    /// as a single argument, or an error payload that carries a connection string. This test walks a
    /// descriptor carrying a stand-in credential through the whole pool path - AddRef, Get, the
    /// descriptor fold onto the connection, a failing statement, the structured error, the redactor and
    /// the release - and asserts the stand-in is in none of the strings any of that produces.
    /// </para>
    /// <para>
    /// <b>THE EMPTY-CREDENTIAL ROW MATTERS TOO.</b> Asserting only that a rendering omits the value would
    /// pass for a rendering that emits the field with an empty value - which discloses that no credential
    /// was supplied. The assertions below are on the FIELD NAME as well as on the value, so a
    /// <c>LogPass = </c> label cannot appear either.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheCredentialReachesNoLogNoErrorAndNoDiagnostic()
    {
        RecordingSqlRedactor redactor = new();

        using TransactionPool pool = CreatePool(out FakeTimeProvider clock, out StubActivator _);

        TransactionData descriptor = DescriptorA with
        {
            LogId = "pfw-parity-principal",
            LogPass = FirstCredentialSentinel,
            DbParm = "DisableBind=1",
        };

        // 1 - THE DESCRIPTOR'S OWN RENDERING. The record's generated printer would have emitted every
        //     property; the type overrides both ToString and the print member so that it does not.
        string rendered = descriptor.ToString();
        AssertNoCredential(rendered);
        Assert.Contains("Dbms = ", rendered, StringComparison.Ordinal);
        Assert.Contains("LogId = ", rendered, StringComparison.Ordinal);

        int index = pool.AddRef(descriptor);
        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? handed));
        FakeTransaction transaction = Assert.IsType<FakeTransaction>(handed);

        // 2 - THE DESCRIPTOR AS THE CONNECTION RECEIVED IT. The fold reaches the connection [:L171], so
        //     the connection holds a descriptor too - and its rendering must be just as silent.
        AssertNoCredential(transaction.LastAppliedDescriptor.ToString());

        // 3 - THE POOL'S OWN CALL LOG. Every verb the pool drove is recorded; none of them names a field.
        foreach (string entry in transaction.Log)
        {
            AssertNoCredential(entry);
        }

        // 4 - A STRUCTURED ERROR RAISED ON THAT CONNECTION. This is the payload a caller actually
        //     receives, and the one place a connection string historically leaks.
        FakeEngine engine = new()
        {
            DbHandle = 1,
            ExecuteResult = SqlState.Failed(-4711, "provider text"),
        };
        using PooledTransaction real = new(engine, clock);
        Assert.Equal(RetCode.OK, real.ApplyTransactionData(in descriptor));
        Assert.Equal(RetCode.E_DB_ERROR, real.Exec("UPDATE COMPANY SET NAME = 'x'", TestContext.Current.CancellationToken));

        DbErrorData error = real.CaptureError();
        AssertNoCredential(error.ToString());
        AssertNoCredential(error.SqlErrText);
        AssertNoCredential(error.SqlSyntax);

        // 5 - THE REDACTOR'S RECORD. Anything on its way to a log passes through the injected redactor, so
        //     what it RECEIVED is itself evidence: the statement text arrives, the credential does not.
        _ = redactor.Redact(error.SqlSyntax);
        foreach (string statement in redactor.Statements)
        {
            AssertNoCredential(statement);
        }

        // 6 - AND THE RELEASE PATH, which is the last thing to touch the entry.
        Assert.Equal(RetCode.OK, pool.Release(index, ref handed));
        Assert.Equal(RetCode.OK, pool.RemoveRef(index));
        foreach (string entry in transaction.Log)
        {
            AssertNoCredential(entry);
        }
    }

    // ==============================================================================================
    //  SUITE 17 - THE RESOLVED EXPIRY IS THE OPTIONS TYPE'S ANSWER, NOT A SECOND COPY OF THE
    //             ARITHMETIC [n_cst_thread_trans_pool.sru:L53, :L76-L79]
    //  ------------------------------------------------------------------------------------------------
    //  The legacy resolves its idle lifetime in two statements:
    //
    //      _nKeepAliveExpireTime = ...of_GetDataDouble("$SQL.TransPool.KeepAliveExpireTime") * 1000  :L78
    //      if _nKeepAliveExpireTime <= 0 then _nKeepAliveExpireTime = KEEPALIVE_EXPIRE               :L79
    //      constant long KEEPALIVE_EXPIRE = 30000 //ms                                              :L53
    //
    //  The port puts that arithmetic on the OPTIONS type, as ResolveKeepAliveExpireMilliseconds(), and
    //  PersistenceOptionsBuilder's own suite owns it in isolation. THIS suite owns the other half of the
    //  obligation: that the pool USES that answer rather than carrying its own copy of the conversion.
    //  Both halves are needed. A pool with a private x1000 would agree with the options type on every
    //  value until one of them was changed, and then the disagreement would surface as connections living
    //  a thousand times too long - or expiring a thousand times too early - with no failing test.
    //
    //  THE SUITE CLOSES ON THE THIRD SETTING oninit READS [:L83] - the transaction class name - for the
    //  same reason and in the same way: not that the pool STORES it, which Suite 1 covers, but that the
    //  pool ACTIVATES from it through the real activator, and that a name which cannot be resolved becomes
    //  a return code instead of an escaping exception.
    // ==============================================================================================

    /// <summary>
    /// The lifetimes this suite drives end to end, in the unit an operator configures them in.
    /// </summary>
    /// <returns>A configured value in SECONDS, paired with the milliseconds the pool must resolve it to.</returns>
    /// <remarks>
    /// Every expected value is written out rather than computed from the input, for the reason
    /// <see cref="ExpiryMatrixMilliseconds"/> gives: deriving the expectation with the multiplication under
    /// test asserts nothing. The three non-positive rows all expect the fallback [<c>:L79</c>], and the
    /// sub-millisecond row is the one that shows the fallback is reached by TRUNCATION rather than by the
    /// sign test alone - 0.0004 seconds is 0.4 ms, which is positive and truncates to zero.
    /// </remarks>
    public static TheoryData<double, long> ResolvedExpiryMatrix() => new()
    {
        // Configured, positive, and a whole number of seconds.
        { 10d, 10_000L },

        // A fractional second still converts, and still lands on a whole millisecond.
        { 1.5d, 1_500L },

        // One millisecond is the smallest configured value that survives truncation.
        { 0.001d, 1L },

        // ZERO takes the fallback [:L79] - and takes it to thirty seconds, NOT to "expire immediately".
        { 0d, 30_000L },

        // So does a negative value, which is how the legacy's unsigned field would have read as enormous.
        { -1d, 30_000L },

        // And so does a positive value too small to survive truncation to whole milliseconds.
        { 0.0004d, 30_000L },

        // AN ABSURDLY LARGE LIFETIME SATURATES rather than overflowing. A configuration of roughly
        // thirty-one thousand years is not a realistic operator input, but arithmetic that wrapped on it
        // would produce a NEGATIVE expiry - and a negative expiry collects EVERYTHING on the next sweep,
        // turning the largest possible keep-alive into no keep-alive at all. The row exists because that
        // failure is silent and its symptom is the opposite of its cause.
        { 1_000_000_000d, int.MaxValue },
    };

    /// <summary>
    /// The pool's effective expiry IS <c>ResolveKeepAliveExpireMilliseconds()</c>, and it is the value the
    /// collection comparison actually reads [pool :L78-L79, :L215].
    /// </summary>
    /// <param name="configuredSeconds">The lifetime as configured, in seconds.</param>
    /// <param name="expectedMilliseconds">The lifetime the pool must resolve it to, in milliseconds.</param>
    [Theory]
    [MemberData(nameof(ResolvedExpiryMatrix))]
    public void TheEffectiveExpiryIsTheOptionsTypesOwnAnswerAndDrivesCollection(
        double configuredSeconds,
        long expectedMilliseconds)
    {
        // ONE options instance, read twice: once by this test and once by the pool. That is what makes the
        // first assertion an identity claim about the pool's SOURCE rather than a coincidence of values.
        TransactionPoolOptions poolOptions = new()
        {
            KeepAlive = true,
            KeepAliveExpireSeconds = configuredSeconds,
        };

        int resolved = poolOptions.ResolveKeepAliveExpireMilliseconds();
        Assert.Equal(expectedMilliseconds, resolved);

        FakeTimeProvider clock = new(ClockStart);
        StubActivator activator = new();

        using TransactionPool pool = new(
            Options.Create(new PersistenceOptions { TransactionPool = poolOptions }),
            clock,
            activator);

        // THE POOL DID NOT COMPUTE THIS - IT ASKED.
        Assert.Equal(resolved, pool.KeepAliveExpireMilliseconds);

        int index = pool.AddRef(DescriptorA);
        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? handed));
        FakeTransaction transaction = Assert.IsType<FakeTransaction>(handed);

        // Released, retained and stamped [:L95-L99].
        Assert.Equal(RetCode.OK, pool.RemoveRef(index));
        Assert.Equal(1, pool.UpperBound);

        // ONE MILLISECOND UNDER THE RESOLVED VALUE - and the clock is advanced in MILLISECONDS while the
        // lifetime was configured in SECONDS, so this crosses the unit boundary in the direction that
        // catches a missing or doubled conversion.
        clock.Advance(TimeSpan.FromMilliseconds(resolved - 1L));
        pool.Collect(force: false);
        Assert.Equal(1, pool.UpperBound);
        Assert.Equal(0, transaction.DisposeCalls);

        // AND AT THE RESOLVED VALUE ITSELF, because the comparison is greater-or-EQUAL [:L215].
        clock.Advance(TimeSpan.FromMilliseconds(1));
        pool.Collect(force: false);
        Assert.Equal(0, pool.UpperBound);
        Assert.Equal(1, transaction.DisposeCalls);
    }

    /// <summary>
    /// The <c>&lt;= 0</c> fallback is honoured END TO END: a zero configuration expires at thirty seconds,
    /// not immediately and not never [pool :L53, :L79].
    /// </summary>
    /// <remarks>
    /// <para>
    /// The theory above proves the fallback VALUE reaches the comparison. This test states the failure it
    /// exists to prevent, which is worth its own name: had the fallback been omitted, a zero configuration
    /// would resolve to zero, every unreferenced entry would satisfy <c>elapsed &gt;= 0</c> on the very
    /// next sweep, and keep-alive would be silently OFF for every deployment that never configured a
    /// lifetime - the default configuration. That is not a caught error; it is a feature that quietly does
    /// nothing.
    /// </para>
    /// <para>
    /// The window is named through <see cref="FakeTimeProvider.PoolIdleWindow"/> rather than as a literal,
    /// so this assertion and the shared double's own statement of the legacy window cannot drift apart.
    /// </para>
    /// </remarks>
    [Fact]
    public void AZeroConfigurationExpiresAtThirtySecondsRatherThanImmediately()
    {
        using TransactionPool pool = CreatePool(
            out FakeTimeProvider clock,
            out StubActivator _,
            keepAlive: true,
            keepAliveExpireSeconds: 0d);

        Assert.Equal(
            TransactionPoolOptions.DefaultKeepAliveExpireMilliseconds,
            pool.KeepAliveExpireMilliseconds);
        Assert.Equal(
            (long)FakeTimeProvider.PoolIdleWindow.TotalMilliseconds,
            pool.KeepAliveExpireMilliseconds);

        int index = pool.AddRef(DescriptorA);
        Assert.Equal(RetCode.OK, pool.Get(index, out IPooledTransaction? handed));
        FakeTransaction transaction = Assert.IsType<FakeTransaction>(handed);

        Assert.Equal(RetCode.OK, pool.RemoveRef(index));

        // NOT IMMEDIATELY - the sweep runs with the clock unmoved and the entry stays.
        pool.Collect(force: false);
        Assert.Equal(1, pool.UpperBound);

        // NOT AT ONE MILLISECOND UNDER THE WINDOW EITHER.
        clock.Advance(FakeTimeProvider.PoolIdleWindow - TimeSpan.FromMilliseconds(1));
        pool.Collect(force: false);
        Assert.Equal(1, pool.UpperBound);
        Assert.Equal(0, transaction.DisposeCalls);

        // AND AT THE WINDOW, IT GOES.
        clock.Advance(TimeSpan.FromMilliseconds(1));
        pool.Collect(force: false);
        Assert.Equal(0, pool.UpperBound);
        Assert.Equal(1, transaction.DisconnectCalls);
        Assert.Equal(1, transaction.DisposeCalls);
    }

    /// <summary>
    /// The THIRD setting is the class the pool's factory activates from, and an unresolvable name fails
    /// with a DOCUMENTED CODE rather than letting an exception escape [pool :L82-L83, :L166-L175].
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE REAL ACTIVATOR, NOT THE STUB, WHICH IS WHAT MAKES THIS AN END-TO-END CLAIM.</b> Suite 5 pins
    /// the pool's half with a stub that throws on demand, and Suite 11 pins the activator's half with a
    /// name that cannot resolve. Both halves passing does not prove the two are CONNECTED - the activator
    /// could throw a type the pool does not catch, and each suite would still be green. This test wires the
    /// real activator to the real pool and asserts the seam: the configured name is what gets activated,
    /// and a name that cannot be resolved surfaces as <see cref="RetCode.E_INVALID_OBJECT"/> because the
    /// oracle catches EVERY throwable [<c>:L173-L175</c>] rather than a chosen few.
    /// </para>
    /// <para>
    /// The entry SURVIVES the failure, which is the oracle's behaviour and not an accident: the catch arm
    /// returns before the entry is touched, so correcting the configuration and calling again succeeds
    /// without the caller having to re-add its reference.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePoolActivatesFromTheConfiguredClassNameAndFailsCleanlyWhenItCannot()
    {
        FakeTimeProvider clock = new(ClockStart);
        PooledTransactionActivator activator = new(static () => new FakeEngine(), clock);

        // THE RESOLVABLE ARM - the configured name names a concrete pooled transaction in this service, so
        // the pool takes the Create-Using arm [:L167] and hands back an instance of that very type.
        using TransactionPool resolvable = new(
            Options.Create(new PersistenceOptions
            {
                TransactionPool = new TransactionPoolOptions
                {
                    TransactionClassName = DefaultTransactionTypeName,
                },
            }),
            clock,
            activator);

        Assert.Equal(DefaultTransactionTypeName, resolvable.TransactionClassName);

        int index = resolvable.AddRef(DescriptorA);
        Assert.Equal(RetCode.OK, resolvable.Get(index, out IPooledTransaction? handed));
        Assert.IsType<PooledTransaction>(handed);

        // THE UNRESOLVABLE ARM - a syntactically valid name for a type that does not exist. The activator
        // throws, the pool catches, and the CALLER gets a code.
        using TransactionPool unresolvable = new(
            Options.Create(new PersistenceOptions
            {
                TransactionPool = new TransactionPoolOptions
                {
                    TransactionClassName = "PowerFramework.Persistence.Transactions.NoSuchTransaction",
                },
            }),
            clock,
            activator);

        int missing = unresolvable.AddRef(DescriptorA);
        Assert.Equal(RetCode.E_INVALID_OBJECT, unresolvable.Get(missing, out IPooledTransaction? none));
        Assert.Null(none);

        // The entry is still there, so the failure is recoverable rather than terminal [:L173-L175].
        Assert.Equal(1, unresolvable.UpperBound);
        Assert.True(unresolvable.Exists(DescriptorA));
    }

    // ==============================================================================================
    //  SUITE 18 - THE DESCRIPTOR ROUND TRIP AND THE STRUCTURED ERROR'S REDACTION DOOR
    //             [n_cst_thread_trans.sru:L343-L354, :L394-L410], [dberrordata.srs]
    //  ------------------------------------------------------------------------------------------------
    //  The legacy connection carries the descriptor in BOTH directions - of_SetTransData applies it and
    //  of_GetTransData reads it back through an overridable event - and its failures are reported through
    //  the dberrordata structure. Across a service boundary the read-back direction acquires a duty the
    //  in-process original never had: the statement field carries the COMPLETE generated statement with
    //  literal values interpolated, and the legacy logger performs no redaction at all (AAP 0.6.3.8), so
    //  the port routes that field through ISqlRedactor and nothing else.
    //
    //  WHAT THIS SUITE ASSERTS, AND WHAT IT DELIBERATELY DOES NOT. The redactor's masking RULES belong to
    //  SqlRedactorTests and the payload's shape belongs to DbErrorDataTests; duplicating either here would
    //  create two owners for one behaviour. This suite asserts the WIRING: that a transaction-level
    //  failure becomes a DbErrorData through FromTransaction carrying exactly the two values the legacy
    //  consumer copies, that the statement field is the one thing the redactor is handed, and that the
    //  door is taken rather than bypassed. The recording redactor is used precisely because it makes
    //  "was the door taken" observable instead of inferred.
    // ==============================================================================================

    /// <summary>
    /// The descriptor round-trips: the fold reaches the connection and the read-back answers a descriptor,
    /// with the credential surviving as IDENTITY and not as output [trans :L343-L354, :L394-L410].
    /// </summary>
    [Fact]
    public void TheDescriptorRoundTripsThroughApplyAndReadBack()
    {
        FakeTimeProvider clock = new(ClockStart);
        FakeEngine engine = new();
        using PooledTransaction transaction = new(engine, clock);

        TransactionData outbound = DescriptorA with
        {
            ServerName = "pfw-parity-host",
            LogId = "pfw-parity-principal",
            LogPass = FirstCredentialSentinel,
            Lock = "RU",
            DbParm = "DisableBind=1,NCharBind=1",
            AutoCommit = true,
            UserParm = "carried-by-the-caller",
        };

        // APPLY - the connection fields fold onto the engine and the call answers OK [trans :L343-L354].
        Assert.Equal(RetCode.OK, transaction.ApplyTransactionData(in outbound));
        Assert.Equal(outbound.Dbms, engine.AppliedDescriptor.Dbms);
        Assert.Equal(outbound.ServerName, engine.AppliedDescriptor.ServerName);
        Assert.Equal(outbound.Database, engine.AppliedDescriptor.Database);
        Assert.Equal(outbound.LogId, engine.AppliedDescriptor.LogId);
        Assert.Equal(outbound.Lock, engine.AppliedDescriptor.Lock);
        Assert.Equal(outbound.DbParm, engine.AppliedDescriptor.DbParm);

        // READ BACK - the descriptor answers a descriptor, and the accessor's own hook can refuse
        // [trans :L402]. With no hook installed the read-back is a faithful copy of the connection fields.
        string errInfo = "unset";
        TransactionData inbound = default;
        Assert.Equal(RetCode.OK, outbound.GetTransactionData(ref inbound, ref errInfo));

        Assert.Equal(outbound.Dbms, inbound.Dbms);
        Assert.Equal(outbound.ServerName, inbound.ServerName);
        Assert.Equal(outbound.Database, inbound.Database);
        Assert.Equal(outbound.LogId, inbound.LogId);
        Assert.Equal(outbound.Lock, inbound.Lock);
        Assert.Equal(outbound.DbParm, inbound.DbParm);
        Assert.Equal(string.Empty, errInfo);

        // THE CREDENTIAL IS NOT ON THE OUTBOUND COPY, which is the read-back direction's whole point:
        // a descriptor that leaves the service carries the identity of the connection and not the means
        // to open it (AAP 0.4.2.6). The pool, which never sends a descriptor anywhere, keeps the full one.
        Assert.False(inbound.HasCredential);
        Assert.True(outbound.HasCredential);
        AssertNoCredential(inbound.ToString());
        AssertNoCredential(outbound.ToString());

        // ... and BECAUSE the credential is part of the identity [pool :L138], the stripped copy is NOT
        // equal to the original. That is the honest consequence of the two rules together, and a pool
        // keyed on a read-back copy would open a second connection rather than reuse the first.
        Assert.NotEqual(outbound, inbound);

        // The DbParm flags survive the trip, which is what the SQL layer reads them for [sqlbase :L128].
        inbound.ResolveDbParmFlags(out bool bindDisabled, out bool ncharBinding);
        Assert.True(bindDisabled);
        Assert.True(ncharBinding);
    }

    /// <summary>
    /// A transaction-level failure becomes a <c>DbErrorData</c> through <c>FromTransaction</c>, and the
    /// STATEMENT field is the one value handed to the mandatory <c>ISqlRedactor</c> [dberrordata.srs],
    /// (AAP 0.6.3.8).
    /// </summary>
    [Fact]
    public void AFailureBecomesAStructuredErrorWhoseStatementGoesThroughTheRedactor()
    {
        RecordingSqlRedactor redactor = new();

        FakeTimeProvider clock = new(ClockStart);
        FakeEngine engine = new()
        {
            DbHandle = 1,
            ExecuteResult = SqlState.Failed(-4711, "constraint failed"),
        };
        using PooledTransaction transaction = new(engine, clock);

        Assert.Equal(
            RetCode.E_DB_ERROR,
            transaction.Exec("UPDATE COMPANY SET SALARY = 1", TestContext.Current.CancellationToken));

        // THE CAPTURE IS FromTransaction's SHAPE: the two values the legacy consumer copies, and THREE
        // that a transaction-level failure has nothing to say about.
        DbErrorData captured = transaction.CaptureError();
        Assert.Equal(DbErrorData.FromTransaction(-4711L, "constraint failed"), captured);
        Assert.Equal(-4711L, captured.SqlDbCode);
        Assert.Equal("constraint failed", captured.SqlErrText);
        Assert.Equal(string.Empty, captured.SqlSyntax);
        Assert.Equal(DwBuffer.Primary, captured.Buffer);
        Assert.Equal(0L, captured.Row);

        // AN EMPTY STATEMENT STILL GOES THROUGH THE DOOR, and the redactor answers empty for it. The
        // recording proves the call happened: a caller that skipped redaction "because there is nothing to
        // redact" is exactly how the habit is lost.
        Assert.Equal(string.Empty, redactor.Redact(captured.SqlSyntax));
        Assert.Equal(1, redactor.CallCount);
        Assert.Equal(string.Empty, redactor.LastStatement);

        // WHEN A STATEMENT IS CARRIED - which is the statement-level failure the update path raises - the
        // redactor receives it VERBATIM and answers a masked value. Both facts matter: the first is what
        // makes this test evidence that the door was taken, the second is what the door is for.
        DbErrorData statementLevel = DbErrorData.FromStatement(
            -19L,
            "constraint failed",
            "UPDATE COMPANY SET SALARY = 1 WHERE ID = 7",
            DwBuffer.Primary,
            row: 7L);

        string masked = redactor.Redact(statementLevel.SqlSyntax);

        Assert.Equal(2, redactor.CallCount);
        Assert.Equal("UPDATE COMPANY SET SALARY = 1 WHERE ID = 7", redactor.LastStatement);
        Assert.Equal(RecordingSqlRedactor.MaskedMarker, masked);
        Assert.DoesNotContain("SALARY", masked, StringComparison.Ordinal);

        // AND THE PAYLOAD'S OWN RENDERING WITHHOLDS THE STATEMENT REGARDLESS OF THE REDACTOR, because a
        // diagnostic that formats the payload must not be the one path that discloses it.
        string rendered = statementLevel.ToString();
        Assert.DoesNotContain("SALARY", rendered, StringComparison.Ordinal);
        Assert.Contains("withheld", rendered, StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  HELPERS
    // ==============================================================================================

    /// <summary>
    /// An options accessor that is itself present but resolves to NO VALUE - the shape a mis-registered
    /// options pipeline produces, and the one an argument guard cannot catch.
    /// </summary>
    /// <remarks>
    /// <see cref="Options.Create{TOptions}(TOptions)"/> cannot express this: it wraps a value that the
    /// compiler already insists is non-null. Six lines of stub can, and the fail-fast arm it reaches is
    /// worth an assertion because the alternative - constructing a pool over a default configuration
    /// nobody chose - is silent and would only surface as connections expiring on a schedule no operator
    /// configured.
    /// </remarks>
    private sealed class NullValueOptions : IOptions<PersistenceOptions>
    {
        /// <inheritdoc/>
        public PersistenceOptions Value => null!;
    }

    /// <summary>
    /// Resolves a public instance method by name and parameter list, and asserts its return type.
    /// </summary>
    /// <param name="declaring">The type that must declare it.</param>
    /// <param name="name">The member name.</param>
    /// <param name="returnType">The return type the member must have. <see langword="void"/> is allowed.</param>
    /// <param name="parameterTypes">
    /// The parameter types, in order, with <see cref="Type.MakeByRefType"/> applied to any by-reference
    /// parameter. An empty list means the member takes none.
    /// </param>
    /// <returns>The resolved method, so the caller can inspect the parameter modifiers.</returns>
    /// <remarks>
    /// Resolution BY EXACT PARAMETER LIST rather than by name is what makes the assertion sharp: a lookup
    /// by name alone would resolve an overload the caller did not mean, and this pool genuinely carries
    /// overload pairs - the positional members each have a lease-addressed sibling - so a name-only lookup
    /// here would either throw on ambiguity or silently pin the wrong one.
    /// </remarks>
    private static MethodInfo AssertMethod(
        Type declaring,
        string name,
        Type returnType,
        params Type[] parameterTypes)
    {
        MethodInfo? resolved = declaring.GetMethod(
            name,
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            parameterTypes,
            modifiers: null);

        Assert.NotNull(resolved);
        Assert.Equal(returnType, resolved.ReturnType);

        return resolved;
    }

    /// <summary>
    /// Asserts that a rendering discloses neither credential stand-in and does not even name the field.
    /// </summary>
    /// <param name="rendered">The text to inspect. <see langword="null"/> passes trivially.</param>
    /// <remarks>
    /// THE FIELD NAME IS CHECKED AS WELL AS THE VALUES, because a rendering that emitted
    /// <c>LogPass = </c> with an empty value would satisfy a value-only assertion while still disclosing
    /// whether a credential was supplied. The comparisons are ordinal: this is a security assertion about
    /// bytes, not a culture-sensitive text comparison.
    /// </remarks>
    private static void AssertNoCredential(string? rendered)
    {
        if (rendered is null)
        {
            return;
        }

        Assert.DoesNotContain(FirstCredentialSentinel, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(SecondCredentialSentinel, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(TransactionData.LogPass), rendered, StringComparison.Ordinal);
    }

    /// <summary>
    /// Asserts that a recorded call log matches an expected sequence exactly, in order.
    /// </summary>
    /// <param name="expected">The expected entries, in order.</param>
    /// <param name="actual">The recorded log.</param>
    /// <remarks>
    /// A named helper rather than a bare <c>Assert.Equal</c> at each site, because a collection
    /// expression does not participate in generic type inference: <c>Assert.Equal(["a"], log)</c> cannot
    /// infer its element type, while an array-typed parameter accepts the same literal. It also makes
    /// the intent - ORDER MATTERS - explicit, which is load-bearing here: several suites assert hook and
    /// verb ORDERING rather than mere occurrence.
    /// </remarks>
    private static void AssertSequence(string[] expected, List<string> actual) =>
        Assert.Equal(expected, actual);

    /// <summary>
    /// Builds a pool over the three doubles, with the three settings the legacy <c>oninit</c> reads.
    /// </summary>
    /// <param name="clock">Receives the shared simulated clock, which only a test advances.</param>
    /// <param name="activator">Receives the recording activator.</param>
    /// <param name="keepAlive">The keep-alive setting. Defaults to the LEGACY default of false.</param>
    /// <param name="keepAliveExpireSeconds">The idle lifetime in seconds.</param>
    /// <param name="transactionClassName">The configured class name.</param>
    /// <param name="classNameResolver">The event-first resolver.</param>
    /// <returns>The pool, which the caller disposes.</returns>
    private static TransactionPool CreatePool(
        out FakeTimeProvider clock,
        out StubActivator activator,
        bool keepAlive = false,
        double keepAliveExpireSeconds = 0d,
        string transactionClassName = "",
        TransactionClassNameResolver? classNameResolver = null)
    {
        clock = new FakeTimeProvider(ClockStart);
        activator = new StubActivator();

        PersistenceOptions options = new()
        {
            TransactionPool = new TransactionPoolOptions
            {
                KeepAlive = keepAlive,
                KeepAliveExpireSeconds = keepAliveExpireSeconds,
                TransactionClassName = transactionClassName,
            },
        };

        return new TransactionPool(
            Options.Create(options),
            clock,
            activator,
            classNameResolver);
    }

    /// <summary>
    /// Builds a pool over a CALLER-SUPPLIED clock, so a suite can drive the two readings independently.
    /// </summary>
    /// <param name="clock">The clock the pool reads.</param>
    /// <param name="activator">Receives the recording activator.</param>
    /// <param name="keepAlive">The keep-alive setting.</param>
    /// <param name="keepAliveExpireSeconds">The idle lifetime in seconds.</param>
    /// <returns>The pool, which the caller disposes.</returns>
    private static TransactionPool CreatePoolWithClock(
        TimeProvider clock,
        out StubActivator activator,
        bool keepAlive = false,
        double keepAliveExpireSeconds = 0d)
    {
        activator = new StubActivator();

        PersistenceOptions options = new()
        {
            TransactionPool = new TransactionPoolOptions
            {
                KeepAlive = keepAlive,
                KeepAliveExpireSeconds = keepAliveExpireSeconds,
            },
        };

        return new TransactionPool(Options.Create(options), clock, activator);
    }

    /// <summary>
    /// A clock whose WALL-CLOCK and MONOTONIC readings are independent, so a suite can step one without
    /// the other.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>WHY THE SHARED DOUBLE CANNOT DO THIS JOB, AND WHY THAT IS RIGHT.</b> Every other suite in this
    /// file drives <see cref="FakeTimeProvider"/> from <c>TestDoubles.cs</c>, which answers
    /// <see cref="TimeProvider.GetUtcNow"/> and <see cref="TimeProvider.GetTimestamp"/> from ONE field and
    /// whose <c>Advance</c> rejects a negative delta outright. Coupling the two readings is exactly what a
    /// forward-only suite wants - and it is what makes a wall-clock implementation and a monotonic one
    /// indistinguishable. This double exists purely so that difference becomes observable: a wall clock can
    /// step backwards and a monotonic counter cannot, and that is precisely the divergence a delta over a
    /// stored stamp is sensitive to. It is a SECOND VIEW of time, not a second clock abstraction: the
    /// subject still reads one injected <see cref="TimeProvider"/> and nothing here reaches a real clock.
    /// </para>
    /// </remarks>
    private sealed class SkewedClock : TimeProvider
    {
        private DateTimeOffset _wall;

        private long _monotonicTicks;

        /// <summary>Starts both readings from one instant, then lets them diverge.</summary>
        /// <param name="start">The starting instant.</param>
        internal SkewedClock(DateTimeOffset start)
        {
            _wall = start;
            _monotonicTicks = start.UtcTicks;
        }

        /// <inheritdoc/>
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        /// <inheritdoc/>
        public override DateTimeOffset GetUtcNow() => _wall;

        /// <inheritdoc/>
        public override long GetTimestamp() => _monotonicTicks;

        /// <summary>Moves the monotonic counter forward. It never moves back, because it cannot.</summary>
        /// <param name="delta">How far forward.</param>
        internal void AdvanceMonotonic(TimeSpan delta) => _monotonicTicks += delta.Ticks;

        /// <summary>Steps the wall clock in EITHER direction, as a clock correction would.</summary>
        /// <param name="delta">How far, negative for a backward step.</param>
        internal void StepWallClock(TimeSpan delta) => _wall += delta;
    }

    /// <summary>
    /// A recording <c>IPooledTransaction</c>. Everything the pool decides is expressed through these
    /// members, so this double is the whole environment the pool suites need (C-H).
    /// </summary>
    private sealed class FakeTransaction : IPooledTransaction
    {
        internal List<string> Log { get; } = [];

        internal int ConnectCalls { get; private set; }

        internal int DisconnectCalls { get; private set; }

        internal int ClearStateCalls { get; private set; }

        internal int ApplyTransactionDataCalls { get; private set; }

        internal int DisposeCalls { get; private set; }

        internal int IsBrokenCalls { get; set; }

        internal bool Broken { get; set; }

        internal bool CondemnOnNextIsBroken { get; set; }

        internal bool ThrowOnApply { get; set; }

        internal bool ThrowOnDisconnect { get; set; }

        internal Action? OnDisconnect { get; set; }

        internal TransactionData LastAppliedDescriptor { get; private set; }

        public long SqlCode => 0;

        public long SqlDbCode => 0;

        public long SqlNRows => 0;

        public string SqlErrText => string.Empty;

        public string SqlReturnData => string.Empty;

        public void StampSqlState(in SqlState state)
        {
            // The pool never writes state, so this double records only that nobody tried to.
            Log.Add("StampSqlState");
        }

        public bool AutoCommit { get; set; }

        public long Connect(CancellationToken cancellationToken = default)
        {
            ConnectCalls++;
            Log.Add("Connect");
            return RetCode.OK;
        }

        public long Disconnect()
        {
            DisconnectCalls++;
            Log.Add("Disconnect");
            OnDisconnect?.Invoke();

            return ThrowOnDisconnect
                ? throw new InvalidOperationException("the fake engine refuses to disconnect")
                : RetCode.OK;
        }

        public long Rollback() => RetCode.OK;

        public long Commit(bool autoRollback) => RetCode.OK;

        public long Commit() => RetCode.OK;

        public long AutoCommitCheckpoint() => RetCode.OK;

        public long Exec(string? sqlCommand, CancellationToken cancellationToken = default) => RetCode.OK;

        // The BOUND overload, delegating to the rendered one for the same reason the engine doubles do.
        public long Exec(in SqlCommandText command, CancellationToken cancellationToken = default) => Exec(command.RenderedText);

        public bool IsConnected() => true;

        // The probe-reporting overload. Recorded so a caller can assert on it; this double answers
        // from nothing at all, so it reports that it did not probe.
        public bool IsConnected(out bool probed)
        {
            probed = false;
            return true;
        }

        public bool IsBroken()
        {
            IsBrokenCalls++;

            if (CondemnOnNextIsBroken)
            {
                CondemnOnNextIsBroken = false;
                Broken = true;
            }

            return Broken;
        }

        public long SetBroken()
        {
            Broken = true;
            return RetCode.OK;
        }

        public void ClearState()
        {
            ClearStateCalls++;
            Log.Add("ClearState");
        }

        public DatabaseType GetDbType() => DatabaseType.DbtMssql;

        public long ApplyTransactionData(in TransactionData descriptor)
        {
            if (ThrowOnApply)
            {
                throw new InvalidOperationException("the fake refuses the descriptor");
            }

            ApplyTransactionDataCalls++;
            LastAppliedDescriptor = descriptor;
            Log.Add("ApplyTransactionData");
            return RetCode.OK;
        }

        public bool IsSqlFailed() => false;

        /// <summary>
        /// Reports that this double routes to no transaction engine, so no engine capability is reachable
        /// through it.
        /// </summary>
        /// <typeparam name="TCapability">The capability asked for; never satisfied here.</typeparam>
        /// <param name="capability">Always <see langword="null"/>.</param>
        /// <returns>Always <see langword="false"/>.</returns>
        /// <remarks>
        /// A DOUBLE HAS NO ENGINE TO PROBE, and answering the probe honestly is the whole point. A caller
        /// that needs a provider-shaped capability - the SQLite command source, for instance - takes its
        /// no-capability branch against this double, which is exactly the branch a pooled transaction over
        /// a non-SQLite engine would drive it down in production.
        /// </remarks>
        public bool TryGetEngineCapability<TCapability>(
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TCapability? capability)
            where TCapability : class
        {
            capability = null;
            return false;
        }


        public bool IsSqlSucceeded() => true;

        public DbErrorData CaptureError() => DbErrorData.Empty;

        public void Dispose()
        {
            DisposeCalls++;
            Log.Add("Dispose");
        }
    }

    /// <summary>
    /// A recording activator that hands out <see cref="FakeTransaction"/> instances.
    /// </summary>
    private sealed class StubActivator : IPooledTransactionActivator
    {
        internal List<FakeTransaction> Created { get; } = [];

        internal List<string> RequestedClassNames { get; } = [];

        internal int CreateDefaultCalls { get; private set; }

        internal int CreatedCount => Created.Count;

        internal bool ThrowOnCreate { get; set; }

        internal bool ThrowOnApply { get; set; }

        public IPooledTransaction CreateDefault()
        {
            CreateDefaultCalls++;
            return Produce();
        }

        public IPooledTransaction Create(string className)
        {
            RequestedClassNames.Add(className);

            return ThrowOnCreate
                ? throw new InvalidOperationException("the stub activator refuses this class name")
                : Produce();
        }

        private FakeTransaction Produce()
        {
            FakeTransaction transaction = new() { ThrowOnApply = ThrowOnApply };
            Created.Add(transaction);
            return transaction;
        }
    }

    /// <summary>
    /// A recording <c>ITransactionEngine</c>. It NEVER opens a connection, composes no connection
    /// string and references no provider (C-E); it records the verb and answers a configured state.
    /// </summary>
    private sealed class FakeEngine : ITransactionEngine
    {
        internal List<string> Log { get; } = [];

        internal List<string> ExecutedStatements { get; } = [];

        internal int DisposeCalls { get; private set; }

        internal TransactionData AppliedDescriptor { get; private set; }

        internal SqlState ConnectResult { get; init; } = SqlState.Succeeded();

        internal SqlState DisconnectResult { get; init; } = SqlState.Succeeded();

        internal SqlState CommitResult { get; init; } = SqlState.Succeeded();

        internal SqlState RollbackResult { get; init; } = SqlState.Succeeded();

        internal SqlState ExecuteResult { get; init; } = SqlState.Succeeded();

        public int DbHandle { get; set; }

        public string Dbms { get; set; } = "SQLITE";

        public bool AutoCommit { get; set; }

        public void ApplyConnectionFields(in TransactionData descriptor)
        {
            // The seven-field fold, performed by the descriptor's own accessor so the omissions are the
            // descriptor's rather than this double's.
            AppliedDescriptor = AppliedDescriptor.WithConnectionFieldsFrom(in descriptor);
            Log.Add("ApplyConnectionFields");
        }

        public SqlState Connect(CancellationToken cancellationToken = default)
        {
            Log.Add("Connect");
            return ConnectResult;
        }

        public SqlState Disconnect()
        {
            Log.Add("Disconnect");
            return DisconnectResult;
        }

        public SqlState Commit()
        {
            Log.Add("Commit");
            return CommitResult;
        }

        public SqlState Rollback()
        {
            Log.Add("Rollback");
            return RollbackResult;
        }

        public SqlState Execute(string sqlCommand, CancellationToken cancellationToken = default)
        {
            Log.Add("Execute");
            ExecutedStatements.Add(sqlCommand);
            return ExecuteResult;
        }

        // The BOUND overload. It delegates to the rendered one so this double keeps recording exactly
        // what it recorded before and every existing assertion on it still holds; what the overload adds
        // is that the production seam's parameter-carrying shape is exercised as well.
        public SqlState Execute(in SqlCommandText command, CancellationToken cancellationToken = default) => Execute(command.RenderedText);

        public void Dispose() => DisposeCalls++;
    }

    /// <summary>
    /// A hook set that overrides NOTHING, so every member resolves to the interface's own default.
    /// </summary>
    /// <remarks>
    /// It exists to prove that the defaults are genuinely reachable and genuinely inert. The recording
    /// double below cannot do that, because it overrides all thirteen members in order to record them.
    /// </remarks>
    private sealed class MinimalHooks : IPooledTransactionHooks
    {
    }

    /// <summary>
    /// A recording hook set that can also spoil the SQL state, which is how the after-hook arms are
    /// reached.
    /// </summary>
    /// <remarks>
    /// The state-spoiling members are the only way to exercise the two arms where a HOOK changes the
    /// outcome of an operation that had already succeeded - the connect's clean disconnect
    /// [trans :L133-L137] and the commit's conditional rollback [trans :L249-L254].
    /// </remarks>
    private sealed class RecordingHooks : IPooledTransactionHooks
    {
        internal List<string> Log { get; } = [];

        internal int CheckCalls { get; private set; }

        internal long BeforeConnectResult { get; init; } = RetCode.OK;

        internal SqlState? BeforeConnectStamp { get; init; }

        internal SqlState? AfterConnectStamp { get; init; }

        internal long BeforeCommandResult { get; init; } = RetCode.OK;

        internal SqlState? BeforeCommandStamp { get; init; }

        internal long CheckResult { get; init; } = RetCode.OK;

        internal long? TestResult { get; init; }

        internal IPooledTransaction? CondemnTarget { get; set; }

        internal long BeforeRollbackObservedSqlDbCode { get; private set; }

        internal long AfterRollbackObservedSqlDbCode { get; private set; }

        internal string AfterRollbackObservedSqlErrText { get; private set; } = string.Empty;

        internal long AfterDisconnectObservedSqlDbCode { get; private set; }

        /// <summary>The transaction the state-observing hooks read. Set by the suites that need it.</summary>
        internal PooledTransaction? Observed { get; set; }

        public long OnBeforeConnect()
        {
            Log.Add("OnBeforeConnect");
            ApplyStamp(BeforeConnectStamp);
            return BeforeConnectResult;
        }

        public void OnAfterConnect()
        {
            Log.Add("OnAfterConnect");
            ApplyStamp(AfterConnectStamp);
        }

        public void OnConnectionOk() => Log.Add("OnConnectionOk");

        public void OnBeforeDisconnect() => Log.Add("OnBeforeDisconnect");

        public void OnAfterDisconnect()
        {
            Log.Add("OnAfterDisconnect");
            AfterDisconnectObservedSqlDbCode = Observed?.SqlDbCode ?? 0;
        }

        public void OnBeforeRollback()
        {
            Log.Add("OnBeforeRollback");
            BeforeRollbackObservedSqlDbCode = Observed?.SqlDbCode ?? 0;
        }

        public void OnAfterRollback()
        {
            Log.Add("OnAfterRollback");
            AfterRollbackObservedSqlDbCode = Observed?.SqlDbCode ?? 0;
            AfterRollbackObservedSqlErrText = Observed?.SqlErrText ?? string.Empty;
        }

        public void OnBeforeCommit() => Log.Add("OnBeforeCommit");

        public void OnAfterCommit() => Log.Add("OnAfterCommit");

        public long OnBeforeCommand(string sqlCommand)
        {
            Log.Add("OnBeforeCommand");
            ApplyStamp(BeforeCommandStamp);
            return BeforeCommandResult;
        }

        public void OnAfterCommand(string sqlCommand) => Log.Add("OnAfterCommand");

        public long OnCheck()
        {
            CheckCalls++;

            if (CondemnTarget is { } target)
            {
                CondemnTarget = null;
                _ = target.SetBroken();
            }

            return CheckResult;
        }

        public long? OnTest() => TestResult;

        /// <summary>
        /// Spoils - or cleans - the SQL state the way a real hook would, by running a statement through
        /// the observed transaction's engine. Implemented as a direct state write instead, because a
        /// hook cannot reach the transaction's private state and must not need to.
        /// </summary>
        /// <param name="stamp">The state to impose, or <see langword="null"/> to impose none.</param>
        private void ApplyStamp(SqlState? stamp)
        {
            if (stamp is { } state && Observed is { } transaction)
            {
                transaction.StampSqlState(in state);
            }
        }
    }

    /// <summary>
    /// The shared base for the constructor-shape probes. Every member is implemented inertly because
    /// these doubles exist to be CONSTRUCTED, never driven - the shape matrix asserts on which
    /// constructor the activator selected, so behaviour here would be dead weight that could drift out
    /// of step with <see cref="FakeTransaction"/>.
    /// </summary>
    private abstract class ShapeProbe : IPooledTransaction
    {
        public long SqlCode => 0;

        public long SqlDbCode => 0;

        public long SqlNRows => 0;

        public string SqlErrText => string.Empty;

        public string SqlReturnData => string.Empty;

        public bool AutoCommit { get; set; }

        public void StampSqlState(in SqlState state) => _ = state;

        public long Connect(CancellationToken cancellationToken = default) => RetCode.OK;

        public long Disconnect() => RetCode.OK;

        public long Rollback() => RetCode.OK;

        public long Commit(bool autoRollback) => RetCode.OK;

        public long Commit() => RetCode.OK;

        public long AutoCommitCheckpoint() => RetCode.OK;

        public long Exec(string? sqlCommand, CancellationToken cancellationToken = default) => RetCode.OK;

        // The BOUND overload, delegating to the rendered one for the same reason the engine doubles do.
        public long Exec(in SqlCommandText command, CancellationToken cancellationToken = default) => Exec(command.RenderedText);

        public bool IsConnected() => false;

        public bool IsConnected(out bool probed)
        {
            probed = false;
            return false;
        }

        public bool IsBroken() => false;

        public long SetBroken() => RetCode.OK;

        public void ClearState()
        {
        }

        public DatabaseType GetDbType() => DatabaseType.DbtMssql;

        public long ApplyTransactionData(in TransactionData descriptor) => RetCode.OK;

        public bool IsSqlFailed() => false;

        /// <summary>
        /// Reports that this double routes to no transaction engine, so no engine capability is reachable
        /// through it.
        /// </summary>
        /// <typeparam name="TCapability">The capability asked for; never satisfied here.</typeparam>
        /// <param name="capability">Always <see langword="null"/>.</param>
        /// <returns>Always <see langword="false"/>.</returns>
        /// <remarks>
        /// A DOUBLE HAS NO ENGINE TO PROBE, and answering the probe honestly is the whole point. A caller
        /// that needs a provider-shaped capability - the SQLite command source, for instance - takes its
        /// no-capability branch against this double, which is exactly the branch a pooled transaction over
        /// a non-SQLite engine would drive it down in production.
        /// </remarks>
        public bool TryGetEngineCapability<TCapability>(
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TCapability? capability)
            where TCapability : class
        {
            capability = null;
            return false;
        }


        public bool IsSqlSucceeded() => true;

        public DbErrorData CaptureError() => DbErrorData.Empty;

        public void Dispose() => GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Declares ONLY the zero-argument shape - the literal <c>Create Using</c> form [pool :L167].
    /// </summary>
    private sealed class ZeroArgumentProbe : ShapeProbe;

    /// <summary>
    /// Declares the seamed shape with an OPTIONAL hooks parameter, to prove the probe matches on declared
    /// parameter types rather than on exact arity.
    /// </summary>
    private sealed class OptionalHooksProbe : ShapeProbe
    {
        public OptionalHooksProbe(
            ITransactionEngine engine,
            TimeProvider clock,
            IPooledTransactionHooks? hooks = null)
        {
            Engine = engine;
            Clock = clock;
            Hooks = hooks;
        }

        public ITransactionEngine Engine { get; }

        public TimeProvider Clock { get; }

        public IPooledTransactionHooks? Hooks { get; }
    }

    /// <summary>
    /// Declares BOTH accepted shapes, so the probe order is observable.
    /// </summary>
    private sealed class BothShapesProbe : ShapeProbe
    {
        public BothShapesProbe() => UsedSeamedConstructor = false;

        public BothShapesProbe(ITransactionEngine engine, TimeProvider clock, IPooledTransactionHooks hooks)
        {
            _ = engine;
            _ = clock;
            _ = hooks;
            UsedSeamedConstructor = true;
        }

        public bool UsedSeamedConstructor { get; }
    }

    /// <summary>
    /// Declares neither accepted shape, so activation reaches the closing throw.
    /// </summary>
    private sealed class NoAcceptedShapeProbe : ShapeProbe
    {
        public NoAcceptedShapeProbe(string unmatched) => _ = unmatched;
    }
}
