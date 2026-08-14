// ==================================================================================================
//  PersistenceOptionsTests - THE UNIT CONVERSION, THE PRESERVED DEFAULTS AND THE FAIL-FAST VALIDATOR
//  ------------------------------------------------------------------------------------------------
//  SUBJECT   PowerFramework.Persistence.Configuration.PersistenceOptions and its nested groups
//            PowerFramework.Persistence.Configuration.PersistenceOptionsValidator
//            PowerFramework.Persistence.Configuration.SqliteIntegrityCheckMode
//
//  THE TWO THINGS THIS FILE OWNS THAT NOTHING ELSE COVERS
//  ------------------------------------------------------------------------------------------------
//    1. THE SECONDS-TO-MILLISECONDS CONVERSION AND ITS NON-POSITIVE FALLBACK, asserted on the options
//       type itself through ResolveKeepAliveExpireMilliseconds. The legacy reads its keep-alive window
//       as a DOUBLE and MULTIPLIES BY 1000, then falls back to a millisecond constant:
//
//         constant long KEEPALIVE_EXPIRE = 30000 //ms                                        [pool :L53]
//         _nKeepAliveExpireTime = of_GetDataDouble("$SQL.TransPool.KeepAliveExpireTime") * 1000
//                                                                                            [pool :L78]
//         if _nKeepAliveExpireTime <= 0 then _nKeepAliveExpireTime = KEEPALIVE_EXPIRE         [pool :L79]
//
//       where `pool` is ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru. Both halves of
//       that unit mismatch are preserved - seconds in configuration, milliseconds internally - so the
//       conversion is the one thing a reader can get a thousand times wrong without anything failing at
//       startup, and it is pinned here row by row.
//
//    2. THE FAIL-FAST POSTURE OF THE VALIDATOR, which AAP 0.1.4 forbids softening into
//       warning-and-continue. A structural configuration fault must produce a REFUSAL that names every
//       broken rule, never a quietly defaulted value and a healthy start. The framework itself behaves
//       this way for a structural fault: the application object decodes a seven-field assert payload and
//       then executes HALT CLOSE [ws_objects/pfw.pbl.src/pfw.sra:L111-L144].
//
//  ALSO OWNED HERE: THE PRESERVED DEFAULTS AND THE EVIDENCED SHAPE
//  ------------------------------------------------------------------------------------------------
//    * The seven Query defaults are the legacy reset values, one case each
//      [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L256-L262], which the short
//      form `sqlquery` refers to below in the same way `pool` refers to the file above.
//    * The Sqlite group is exactly the URI grammar the estate evidences - three-state integrity check
//      [ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L454], the six journal tokens with DELETE as the
//      default [:L455], and the read-write-create mode with the password in the SECOND-ARGUMENT
//      position rather than in the URI [:L456].
//    * The whole option graph is swept reflectively for anything able to carry signing material.
//
//  WHAT THIS FILE DELIBERATELY LEAVES TO ITS OWNER
//  ------------------------------------------------------------------------------------------------
//  Every boundary below is asserted somewhere; asserting it twice would mean two definitions of one
//  rule, and the second copy is the one that silently stops being checked.
//    * THE <= 1000 CHUNK-SIZE REJECTION belongs to the query suite, because it is the published
//      contract's own call refusing a value [sqlquery :L410]. Here only the DEFAULT is pinned.
//    * POOL BEHAVIOUR - whether a retained transaction is collected, and the legacy field initialiser
//      surviving the unexecuted keep-alive branch [pool :L59] - belongs to TransactionPoolTests.
//      Here only the arithmetic is pinned.
//    * THE FOUR INVARIANT TOKEN SWITCHES and the two key-set refresh floors belong to
//      InvariantTokenValidationTests.
//    * URI COMPOSITION from the Sqlite group belongs to the runtime suite that drives the connection
//      factory. Here only the option shape is pinned.
//    * THE SETTINGS-FILE KEY INVENTORY belongs to the contracts suite's configuration-coherence tests,
//      which read the JSON. This file is the TYPE half of that same bidirectional check: a key spelled
//      differently in either place binds silently to the default, so the member names are asserted here
//      and the file keys are asserted there.
//
//  CONSTRAINTS THIS FILE DISCHARGES
//  ------------------------------------------------------------------------------------------------
//    C-B  REPLICATE, DO NOT CORRECT. The guard reproduced is `<= 0`, NOT `< 0` [pool :L79], so ZERO
//         takes the fallback rather than meaning "expire immediately" - and zero is the SHIPPED value,
//         which makes the fallback the live default path rather than dead code. Thirty seconds is
//         asserted as the expected fallback, not as a value worth improving on.
//    C-E  NO FABRICATED DATABASE. Only the evidenced SQLite shape is asserted. The legacy transaction
//         layer declares exactly two database types and SQLite is not among them
//         [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L60-L61], so this file also asserts
//         that no provider, dialect or second-connection setting has been invented.
//    C-F  NO HARDCODED SECRET. This file asserts the ABSENCE of key material and contains no password,
//         key, certificate, credential, connection string or token literal of any kind. The one
//         credential-shaped member is exercised as null-when-unset and nothing else.
//    C-G  EVERY NEW BOUNDARY AUTHENTICATED, WITH SECURITY AS THE SOLE ISSUER. The reflective sweep is
//         this file's contribution: no member anywhere in the graph can hold signing material, and the
//         verification group is a closed set with no signing authority in it.
//    C-H  Part of the per-service line-coverage gate. PersistenceOptions is a wide type whose accessors
//         and resolver are cheap, high-value coverage.
//    C-K  Every reproduced legacy decision carries its ws_objects locator at the point of reproduction.
//    0.8.5  NO PERFORMANCE ASSERTION. Nothing here claims a lifetime, an interval or a timeout is fast
//         enough, or bounded well, or better than anything. Conversion arithmetic only.
//    0.7.2  Warnings are errors and nullable is on. No SCREAMING_SNAKE identifier is declared: this
//         folder is outside every .editorconfig section that relaxes the naming analyzers. The legacy
//         names KEEPALIVE_EXPIRE and $SQL.TransPool.* are a legacy constant and legacy configuration
//         KEY STRINGS, and they appear in this file only inside documentation.
//
//  NO CLOCK, NO I/O, NO ORDERING DEPENDENCE. Nothing here reads the time, touches the filesystem, opens
//  a connection or shares mutable state between cases, so two consecutive runs are identical.
// ==================================================================================================

using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.Extensions.Options;
using PowerFramework.Persistence.Configuration;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Pins the configuration surface of the Persistence service: the preserved legacy defaults, the
/// keep-alive unit conversion, the evidenced SQLite shape and the fail-fast validator.
/// </summary>
public sealed class PersistenceOptionsTests
{
    // ==============================================================================================
    //  REGION 1 - THE KEEP-ALIVE CONVERSION [pool :L53, :L78-L79]
    // ==============================================================================================

    /// <summary>
    /// Configured seconds paired with the milliseconds the resolver must produce.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EVERY FALLBACK ROW IS EXPRESSED THROUGH THE CONSTANT rather than as a bare <c>30000</c>, so the
    /// table and <see cref="TransactionPoolOptions.DefaultKeepAliveExpireMilliseconds"/> can never drift
    /// apart. The constant's own VALUE is pinned against the legacy literal exactly once, by
    /// <see cref="TheFallbackLifetimeIsTheLegacyThirtyThousandMilliseconds"/>, which is the single place
    /// in this file where writing the number out is the point.
    /// </para>
    /// <para>
    /// The rows cover the five distinct behaviours the resolver has, and the reason each is here rather
    /// than a variation on its neighbour:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///     A WHOLE NUMBER OF SECONDS multiplies cleanly - the ordinary case.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     A FRACTIONAL VALUE proves the conversion is a genuine multiply rather than an
    ///     integer-seconds assumption. The legacy reads a DOUBLE [pool :L78], so half a second is a
    ///     legal configured value and must become five hundred milliseconds, not zero and not one.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     A PRODUCT WITH A FRACTION ABOVE A HALF proves TRUNCATION rather than rounding: 1.8 ms
    ///     becomes 1, where rounding would give 2. The legacy assigns the product into an integral
    ///     field, so truncation is the documented contract.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     ONE MILLISECOND is the smallest configured value that survives the guard at all, and it sits
    ///     immediately beside a sub-millisecond value that does not - which is the boundary the legacy's
    ///     own ORDER creates, because it truncates into its integral field on one line and only then
    ///     compares that field against zero on the next [pool :L78-L79].
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     ZERO AND NEGATIVES take the fallback because the legacy guard is <c>&lt;= 0</c> and not
    ///     <c>&lt; 0</c> [pool :L79]. Zero is also the SHIPPED configured value, so this is the live
    ///     default path.
    ///     </description>
    ///   </item>
    /// </list>
    /// </remarks>
    public static TheoryData<double, int> KeepAliveConversionCases => new()
    {
        // A whole number of seconds - the ordinary case.
        { 45d, 45_000 },
        { 7d, 7_000 },

        // Thirty seconds CONFIGURED reaches the same lifetime the fallback produces. The two are the
        // same effective value stated two ways, which is why an environment file may legitimately carry
        // either.
        { 30d, 30_000 },

        // Fractional seconds - the conversion is a multiply over a double, not an integer scale-up.
        { 0.5d, 500 },
        { 2.75d, 2_750 },
        { 0.9999d, 999 },

        // Truncation, not rounding: 1.8 ms becomes 1 and 1234.5 ms becomes 1234.
        { 0.0018d, 1 },
        { 1.2345d, 1_234 },

        // The smallest configured value that clears the guard.
        { 0.001d, 1 },

        // At or below zero after truncation - the preserved `<= 0` fallback [pool :L79].
        { 0d, TransactionPoolOptions.DefaultKeepAliveExpireMilliseconds },
        { -1d, TransactionPoolOptions.DefaultKeepAliveExpireMilliseconds },
        { -3_600d, TransactionPoolOptions.DefaultKeepAliveExpireMilliseconds },

        // A product strictly between zero and one millisecond truncates to zero FIRST and therefore
        // takes the fallback, rather than becoming an immediate expiry. Testing the double product
        // before truncating would give the opposite answer, which is the easy misreading.
        { 0.0004d, TransactionPoolOptions.DefaultKeepAliveExpireMilliseconds },
    };

    /// <summary>
    /// The configured keep-alive window is converted from seconds to milliseconds, truncated toward
    /// zero, and replaced by the legacy fallback whenever the result is at or below zero.
    /// </summary>
    /// <param name="configuredSeconds">The value a deployment configures, in seconds.</param>
    /// <param name="expectedMilliseconds">The lifetime the pool must be handed, in milliseconds.</param>
    [Theory]
    [MemberData(nameof(KeepAliveConversionCases))]
    public void TheKeepAliveWindowIsConvertedFromSecondsWithTheLegacyNonPositiveFallback(
        double configuredSeconds,
        int expectedMilliseconds)
    {
        TransactionPoolOptions options = new() { KeepAliveExpireSeconds = configuredSeconds };

        Assert.Equal(expectedMilliseconds, options.ResolveKeepAliveExpireMilliseconds());
    }

    /// <summary>
    /// The inputs whose product is not a finite value inside the integral range.
    /// </summary>
    public static TheoryData<double, int> NonFiniteAndOverflowingCases => new()
    {
        { double.NaN, TransactionPoolOptions.DefaultKeepAliveExpireMilliseconds },
        { double.NegativeInfinity, TransactionPoolOptions.DefaultKeepAliveExpireMilliseconds },
        { double.PositiveInfinity, int.MaxValue },
        { double.MaxValue, int.MaxValue },
        { 3_000_000d, int.MaxValue },
    };

    /// <summary>
    /// Values with no finite product resolve without throwing: a not-a-number and a negative infinity
    /// take the fallback, while a positive overflow saturates.
    /// </summary>
    /// <param name="configuredSeconds">The configured value, in seconds.</param>
    /// <param name="expectedMilliseconds">The lifetime the resolver must produce.</param>
    /// <remarks>
    /// <para>
    /// A NOT-A-NUMBER HAS TO BE CAUGHT EXPLICITLY, because every relational comparison against it is
    /// false - so it would slip past a bare <c>&lt;= 0</c> test and be cast into an integer with no
    /// defined result. That is a property of the port rather than of the oracle, and it is asserted
    /// because a deployment can produce it: a configuration source can carry any double.
    /// </para>
    /// <para>
    /// A POSITIVE OVERFLOW SATURATES RATHER THAN THROWING. A checked conversion would raise an overflow
    /// exception from inside a lifetime lookup, turning a misconfigured number into a fault somewhere
    /// unrelated; saturating keeps the observable outcome "a window longer than any process will live".
    /// NOTHING HERE CLAIMS THAT OUTCOME IS DESIRABLE OR FAST - it is the defined answer for an
    /// undefinable input.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(NonFiniteAndOverflowingCases))]
    public void AValueWithNoFiniteProductResolvesWithoutThrowing(
        double configuredSeconds,
        int expectedMilliseconds)
    {
        TransactionPoolOptions options = new() { KeepAliveExpireSeconds = configuredSeconds };

        Assert.Equal(expectedMilliseconds, options.ResolveKeepAliveExpireMilliseconds());
    }

    /// <summary>
    /// The fallback lifetime is thirty thousand MILLISECONDS, which is the legacy constant's value and
    /// the legacy constant's unit.
    /// </summary>
    /// <remarks>
    /// THE ONE PLACE IN THIS FILE WHERE THE NUMBER IS WRITTEN OUT. Every other assertion expresses the
    /// fallback through the constant so the two stay in step; this row is what makes a change to the
    /// constant fail rather than pass silently, and it is the row that ties the constant to
    /// <c>constant long KEEPALIVE_EXPIRE = 30000 //ms</c> [pool :L53]. The unit is stated by the
    /// legacy's own trailing comment, and it is the unit the member name repeats - the configured value
    /// beside it is in SECONDS, and that mismatch is preserved rather than harmonised.
    /// </remarks>
    [Fact]
    public void TheFallbackLifetimeIsTheLegacyThirtyThousandMilliseconds()
    {
        Assert.Equal(30_000, TransactionPoolOptions.DefaultKeepAliveExpireMilliseconds);
    }

    /// <summary>
    /// Keep-alive is off by default and the configured window is zero, so the shipped section resolves
    /// through the fallback path rather than leaving it unreachable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// FALSE IS THE LEGACY DEFAULT RATHER THAN A CAUTIOUS CHOICE: with the key absent the legacy's
    /// boolean read yields false and nothing is retained [pool :L76]. Zero is likewise the legacy's own
    /// "key absent" read of a double, and zero trips the <c>&lt;= 0</c> guard [pool :L79] - so the
    /// fallback is what a stock deployment actually gets.
    /// </para>
    /// <para>
    /// The shipped settings file declares both values explicitly - <c>KeepAlive: false</c> and
    /// <c>KeepAliveExpireSeconds: 0</c> - and they agree with these defaults, which is the bidirectional
    /// key-shape agreement this file owns the type half of.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheShippedKeepAliveSectionIsOffAtZeroSecondsAndResolvesThroughTheFallback()
    {
        TransactionPoolOptions shipped = new PersistenceOptions().TransactionPool;

        Assert.False(shipped.KeepAlive);
        Assert.Equal(0d, shipped.KeepAliveExpireSeconds);
        Assert.Equal(
            TransactionPoolOptions.DefaultKeepAliveExpireMilliseconds,
            shipped.ResolveKeepAliveExpireMilliseconds());
    }

    /// <summary>
    /// The conversion does not consult the keep-alive flag: the same configured window resolves to the
    /// same lifetime whether retention is on or off.
    /// </summary>
    /// <remarks>
    /// WHAT THE FLAG DECIDES IS THE POOL'S BUSINESS, NOT THE CONVERSION'S. The legacy gates RETENTION
    /// and the idle subscription on the flag [pool :L76-L80] while the lifetime field carries its
    /// initialiser regardless [pool :L59], so the flag changes what the pool DOES with the window and
    /// never what the window IS. Pinning that separation here is what lets TransactionPoolTests own the
    /// pool-behaviour half without either suite restating the other's rule.
    /// </remarks>
    [Fact]
    public void TheConversionIsIndependentOfWhetherKeepAliveIsEnabled()
    {
        TransactionPoolOptions retaining = new() { KeepAlive = true, KeepAliveExpireSeconds = 45d };
        TransactionPoolOptions notRetaining = new() { KeepAlive = false, KeepAliveExpireSeconds = 45d };

        Assert.Equal(45_000, retaining.ResolveKeepAliveExpireMilliseconds());
        Assert.Equal(
            retaining.ResolveKeepAliveExpireMilliseconds(),
            notRetaining.ResolveKeepAliveExpireMilliseconds());
    }

    /// <summary>
    /// The non-positive configured windows, including the shipped zero.
    /// </summary>
    public static TheoryData<double> NonPositiveWindows => new() { 0d, -1d, -3_600d, 0.0004d };

    /// <summary>
    /// A non-positive keep-alive window is LEGAL configuration and is never a validation failure.
    /// </summary>
    /// <param name="configuredSeconds">The configured value, in seconds.</param>
    /// <remarks>
    /// REJECTING IT WOULD INVENT A VALIDATION THE LEGACY NEVER HAD (C-B) and would also make the shipped
    /// settings file unstartable, because the shipped value is zero. The legacy falls back rather than
    /// refusing [pool :L79], so the graph must validate cleanly and the fallback must be what resolves.
    /// </remarks>
    [Theory]
    [MemberData(nameof(NonPositiveWindows))]
    public void ANonPositiveKeepAliveWindowValidatesCleanlyRatherThanBeingRejected(
        double configuredSeconds)
    {
        PersistenceOptionsBuilder builder = new PersistenceOptionsBuilder()
            .WithKeepAlive(enabled: true, expireSeconds: configuredSeconds);

        ValidateOptionsResult result = builder.Validate();

        Assert.True(result.Succeeded, Describe(result));
        Assert.Equal(
            TransactionPoolOptions.DefaultKeepAliveExpireMilliseconds,
            builder.Build().TransactionPool.ResolveKeepAliveExpireMilliseconds());
    }

    // ==============================================================================================
    //  REGION 2 - THE QUERY DEFAULTS ARE THE LEGACY RESET VALUES [sqlquery :L256-L262]
    // ==============================================================================================

    /// <summary>
    /// Each retrieval setting paired with the legacy reset value it must default to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ONE CASE PER SETTING, and the member is named through <c>nameof</c> so a rename breaks the BUILD
    /// while the reflected read still proves the member is publicly readable under exactly that
    /// spelling. That spelling is the configuration key, so this table is simultaneously the default
    /// check and the key-shape check: a member spelled differently from its settings-file key does not
    /// error and does not warn, it binds silently to the default.
    /// </para>
    /// <para>
    /// EVERY VALUE IS THE LEGACY <c>_of_reset</c> VALUE, not a judgement - chunk size
    /// <c>10000</c> [sqlquery :L256], page index <c>0</c> [:L257], paging off [:L258], page size
    /// <c>0</c> [:L259], page counting ON [:L260], row cap <c>0</c> meaning unlimited [:L261], caching
    /// off [:L262]. The reset routine IS the specification of what a freshly configured retrieval looks
    /// like, and page counting is the one boolean whose legacy default is true.
    /// </para>
    /// <para>
    /// THE EXPECTED VALUES CARRY THEIR DECLARED WIDTHS deliberately: the row cap is a
    /// <see cref="long"/> and the other three numbers are <see cref="int"/>, so a boxed comparison
    /// would fail if a member's width changed. That is intended - a width change is a contract change.
    /// </para>
    /// </remarks>
    public static TheoryData<string, object> QueryResetDefaults => new()
    {
        { nameof(QueryOptions.ChunkSize), 10_000 },
        { nameof(QueryOptions.PageCounting), true },
        { nameof(QueryOptions.Cache), false },
        { nameof(QueryOptions.MaxRows), 0L },
        { nameof(QueryOptions.PageIndex), 0 },
        { nameof(QueryOptions.PageSize), 0 },
        { nameof(QueryOptions.Paged), false },
    };

    /// <summary>
    /// Every retrieval setting defaults to the value the legacy reset routine establishes.
    /// </summary>
    /// <param name="member">The property name, which is also the configuration key.</param>
    /// <param name="expected">The legacy reset value, at its declared width.</param>
    /// <remarks>
    /// THE <c>&lt;= 1000</c> CHUNK-SIZE REJECTION IS NOT ASSERTED HERE. That boundary is the published
    /// query contract's own call refusing a value [sqlquery :L410] and belongs to the suite that drives
    /// it; this row pins only what an unconfigured service starts with. The two are deliberately
    /// different rules over the same member, and the shipped default sits well clear of the floor.
    /// </remarks>
    [Theory]
    [MemberData(nameof(QueryResetDefaults))]
    public void EveryQueryDefaultIsTheLegacyResetValue(string member, object expected)
    {
        PropertyInfo? property = typeof(QueryOptions).GetProperty(
            member,
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(property);
        Assert.Equal(expected, property!.GetValue(new QueryOptions()));
        Assert.Equal(expected, property.GetValue(new PersistenceOptions().Query));
    }

    /// <summary>
    /// The retrieval group is exactly those seven settings - no eighth has been invented, and none of
    /// the six per-request fields the same reset routine clears has become configuration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A CLOSED SET IS THE ASSERTION, NOT A COUNT. The legacy reset routine also clears the hook class,
    /// the statement text, the statement syntax, the data object, the new sort and the new filter
    /// [sqlquery :L250-L255] plus three clause arrays [:L264-L266], and every one of those is
    /// PER-REQUEST STATE rather than a setting. A member appearing here for one of them would turn a
    /// request parameter into a deployment-wide default, which is a behavioural change no default-value
    /// assertion would catch.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheRetrievalGroupCarriesExactlyTheSevenResetSettings()
    {
        string[] expected =
        [
            nameof(QueryOptions.Cache),
            nameof(QueryOptions.ChunkSize),
            nameof(QueryOptions.MaxRows),
            nameof(QueryOptions.PageCounting),
            nameof(QueryOptions.PageIndex),
            nameof(QueryOptions.PageSize),
            nameof(QueryOptions.Paged),
        ];

        Assert.Equal(expected, PublicMemberNamesOf(typeof(QueryOptions)));
    }

    // ==============================================================================================
    //  REGION 3 - THE SQLITE SHAPE IS THE EVIDENCED URI GRAMMAR [w_test_sqlite.srw:L454-L456]
    // ==============================================================================================

    /// <summary>
    /// The integrity check is unset by default and is genuinely three-state: absent, full or quick.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A BOOLEAN CANNOT EXPRESS THREE STATES, which is why the member is a NULLABLE enumeration. The
    /// legacy extension is documented as <c>check[=quick]</c>
    /// [ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L454], and the three meanings are distinct on
    /// open: the parameter omitted entirely runs no check at all, the parameter present without a value
    /// runs the full check, and <c>check=quick</c> runs the cheaper one. Collapsing the null into
    /// <see langword="false"/> or into an empty string would fuse "run no check" with "run the full
    /// check" and silently change what happens on every open - the migration's wider rule being that a
    /// null is never coerced to a zero.
    /// </para>
    /// <para>
    /// All three states are asserted REPRESENTABLE AND VALID, because a state the validator refuses is
    /// not a state a deployment has. What each state composes into the URI belongs to the connection
    /// factory's own suite.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheIntegrityCheckIsUnsetByDefaultAndIsGenuinelyThreeState()
    {
        Assert.Null(new SqliteOptions().Check);
        Assert.Null(new PersistenceOptions().Sqlite.Check);

        SqliteIntegrityCheckMode?[] states =
        [
            null,
            SqliteIntegrityCheckMode.Full,
            SqliteIntegrityCheckMode.Quick,
        ];

        foreach (SqliteIntegrityCheckMode? state in states)
        {
            PersistenceOptionsBuilder builder = new PersistenceOptionsBuilder().WithIntegrityCheck(state);
            ValidateOptionsResult result = builder.Validate();

            Assert.Equal(state, builder.Build().Sqlite.Check);
            Assert.True(result.Succeeded, Describe(result));
        }
    }

    /// <summary>
    /// The integrity-check enumeration declares exactly the two forms the legacy grammar documents, and
    /// no <c>None</c> member.
    /// </summary>
    /// <remarks>
    /// THERE IS NO <c>None</c> MEMBER ON PURPOSE. The third state is the ABSENCE of a value, carried by
    /// the property being <see langword="null"/>, so a <c>None</c> member would be a second spelling of
    /// one state: the connection factory would have to treat the two identically while a reader could
    /// not tell which one a deployment meant. A third member appearing here would also mean a URI
    /// parameter the legacy grammar does not have.
    /// </remarks>
    [Fact]
    public void TheIntegrityCheckEnumerationDeclaresExactlyTheTwoLegacyForms()
    {
        string[] expected = ["Full", "Quick"];

        Assert.Equal(expected, Enum.GetNames<SqliteIntegrityCheckMode>().Order(StringComparer.Ordinal));
        Assert.True(Enum.IsDefined(SqliteIntegrityCheckMode.Full));
        Assert.True(Enum.IsDefined(SqliteIntegrityCheckMode.Quick));
    }

    /// <summary>
    /// An integrity-check value outside the two declared forms is refused at startup rather than carried
    /// into URI composition.
    /// </summary>
    /// <remarks>
    /// THE CHECK IS NOT REDUNDANT WITH THE ENUMERATION TYPE. Configuration binding will happily produce
    /// an undeclared value from a numeric string, so without this rule the connection factory would be
    /// asked to compose a parameter for a mode that does not exist and the fault would surface later with
    /// no configuration path attached to it.
    /// </remarks>
    [Fact]
    public void AnUndeclaredIntegrityCheckValueIsRefused()
    {
        PersistenceOptions options = new PersistenceOptionsBuilder()
            .WithIntegrityCheck((SqliteIntegrityCheckMode)7)
            .Build();

        ValidateOptionsResult result = new PersistenceOptionsValidator().Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures!,
            failure => failure.StartsWith("Sqlite:Check", StringComparison.Ordinal));
    }

    /// <summary>
    /// The journal mode defaults to <c>DELETE</c>, the legacy default - not to write-ahead logging.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>WAL</c> IS A LEGAL VALUE AND MUST NOT BECOME THE DEFAULT. A modern reader assumes write-ahead
    /// logging, and choosing it because it is "better" would be exactly the silent behavioural
    /// improvement this migration forbids: journal mode changes crash-recovery semantics, file layout and
    /// reader/writer concurrency, all of which are observable. The legacy states its default inline as
    /// <c>DELETE</c> [w_test_sqlite.srw:L455], so the default is <c>DELETE</c> and a deployment that
    /// wants write-ahead logging asks for it explicitly.
    /// </para>
    /// <para>
    /// This assertion carries NO claim about either mode's speed or durability (0.8.5). It records which
    /// token the oracle defaults to, and nothing more.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheJournalModeDefaultsToTheLegacyDeleteRatherThanWriteAheadLogging()
    {
        Assert.Equal("DELETE", new SqliteOptions().Journal, StringComparer.Ordinal);
        Assert.Equal("DELETE", new PersistenceOptions().Sqlite.Journal, StringComparer.Ordinal);
        Assert.NotEqual("WAL", new SqliteOptions().Journal, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The six journal tokens, in the order the legacy comment lists them.
    /// </summary>
    public static TheoryData<string> LegacyJournalModes => new()
    {
        "DELETE",
        "TRUNCATE",
        "PERSIST",
        "MEMORY",
        "WAL",
        "OFF",
    };

    /// <summary>
    /// All six journal tokens the legacy URI extension documents are accepted.
    /// </summary>
    /// <param name="journal">One of the six legacy tokens.</param>
    /// <remarks>
    /// The token set is <c>journal[=DELETE|TRUNCATE|PERSIST|MEMORY|WAL|OFF]</c>
    /// [w_test_sqlite.srw:L455], asserted through the APPLICATION'S OWN VALIDATOR rather than against a
    /// restatement of the list, so a token quietly dropped from the implementation fails here.
    /// </remarks>
    [Theory]
    [MemberData(nameof(LegacyJournalModes))]
    public void EveryLegacyJournalModeIsAccepted(string journal)
    {
        ValidateOptionsResult result = new PersistenceOptionsBuilder().WithJournal(journal).Validate();

        Assert.True(result.Succeeded, Describe(result));
    }

    /// <summary>
    /// Legal tokens in spellings a settings file plausibly carries.
    /// </summary>
    public static TheoryData<string> TolerantJournalSpellings => new()
    {
        "wal",
        " WAL ",
        "delete",
        "Off",
    };

    /// <summary>
    /// A journal token is matched case-insensitively after trimming, because it ends up in a URI
    /// parameter where surrounding whitespace is an authoring artefact rather than meaning.
    /// </summary>
    /// <param name="journal">A legal token in an unusual spelling.</param>
    [Theory]
    [MemberData(nameof(TolerantJournalSpellings))]
    public void AJournalTokenIsMatchedCaseInsensitivelyAfterTrimming(string journal)
    {
        ValidateOptionsResult result = new PersistenceOptionsBuilder().WithJournal(journal).Validate();

        Assert.True(result.Succeeded, Describe(result));
    }

    /// <summary>
    /// Values outside the documented token set, including the empty string.
    /// </summary>
    public static TheoryData<string> RefusedJournalModes => new()
    {
        "NONE",
        "WAL2",
        "ROLLBACK",
        "",
    };

    /// <summary>
    /// A journal token outside the legacy six is refused, and so is an explicitly empty one.
    /// </summary>
    /// <param name="journal">The rejected value.</param>
    /// <remarks>
    /// AN EMPTY VALUE IS REFUSED RATHER THAN PASSED THROUGH, deliberately: accepting it would compose a
    /// URI carrying a meaningless empty journal parameter and mask the misconfiguration behind whatever
    /// the engine then chose. The offending token is a URI keyword rather than a credential, so the
    /// implementation is free to quote it back - and none of the values in this table is credential
    /// shaped (C-F).
    /// </remarks>
    [Theory]
    [MemberData(nameof(RefusedJournalModes))]
    public void AJournalModeOutsideTheLegacySixIsRefused(string journal)
    {
        ValidateOptionsResult result = new PersistenceOptionsBuilder().WithJournal(journal).Validate();

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures!,
            failure => failure.StartsWith("Sqlite:Journal", StringComparison.Ordinal));
    }

    /// <summary>
    /// The three required URI parts default to the evidenced values, and the mode is the legacy
    /// read-write-create token.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>rwc</c> IS FROM <c>mode=rwc</c> IN THE ONE CONNECTION URI IN THE ESTATE
    /// [w_test_sqlite.srw:L456], and <c>test.db</c> IS THE ONLY DATABASE FILE NAME THE REPOSITORY
    /// EVIDENCES [the same line]. The file name reads like a test artefact and is retained anyway,
    /// because stored characterization comparisons resolve against it and a tidier name would invalidate
    /// every one of them.
    /// </para>
    /// <para>
    /// The directory is the one part that is deployment topology - the mount point of the persistence
    /// volume - and it is an absolute path so the database file survives container recreation. That is
    /// not a preference: for one workflow identifier the legacy-side and target-side recordings must be
    /// taken against the SAME volume state, neither recreated nor reseeded between them, or the paired
    /// recordings are not comparable at all. Nothing here touches the filesystem to check it.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheRequiredUriPartsDefaultToTheEvidencedValues()
    {
        SqliteOptions shipped = new PersistenceOptions().Sqlite;

        Assert.Equal("rwc", shipped.Mode, StringComparer.Ordinal);
        Assert.Equal("test.db", shipped.DatabaseFileName, StringComparer.Ordinal);
        Assert.Equal("/var/lib/powerframework", shipped.DataDirectory, StringComparer.Ordinal);
    }

    /// <summary>
    /// The mode is validated for PRESENCE only, so a token the estate never spells is still accepted.
    /// </summary>
    /// <remarks>
    /// NO TOKEN SET IS INVENTED FOR THE MODE (C-B). No legacy evidence constrains the value beyond the
    /// one spelling the estate uses, so a list here would be a rule the oracle does not have - unlike the
    /// journal parameter, whose six tokens the legacy comment enumerates outright. The contrast between
    /// the two members is the point of this row.
    /// </remarks>
    [Fact]
    public void TheModeIsValidatedForPresenceRatherThanAgainstAnInventedTokenSet()
    {
        Assert.True(new PersistenceOptionsBuilder().WithMode("ro").Validate().Succeeded);
        Assert.True(new PersistenceOptionsBuilder().WithMode("memory").Validate().Succeeded);
        Assert.True(new PersistenceOptionsBuilder().WithMode(string.Empty).Validate().Failed);
    }

    /// <summary>
    /// The SQLite group is exactly the six evidenced URI parts - no provider, no dialect and no second
    /// connection has been invented, and the provisioning switch is NOT here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS THE C-E ASSERTION MADE MECHANICAL. The legacy transaction layer declares exactly two
    /// database types, and SQLite is in neither of them
    /// [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L60-L61]; neither of those two engines
    /// has a schema, a connection string or any DDL anywhere in the repository, so a member for either
    /// would provision a database the legacy never had. Their dialect-specific behaviour lives in the
    /// paging rewriters as pure string transforms that need no instance of either.
    /// </para>
    /// <para>
    /// A TIMEOUT OR AUTO-COMMIT MEMBER WOULD ALSO FAIL THIS ROW, correctly: those are runtime calls on
    /// the connection in the legacy binding and have no configuration key anywhere in the estate.
    /// </para>
    /// <para>
    /// THE PROVISIONING SWITCH IS DELIBERATELY ABSENT FROM THIS SET, and its absence is part of the claim.
    /// Every member here is a component of the one evidenced connection URI
    /// [<c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L450-L456</c>]; whether a deployment applies its
    /// pending migrations when the process starts is a RUNTIME decision with no URI component and no
    /// legacy analogue at all, so it lives in its own top-level <c>Schema</c> section - asserted by
    /// <see cref="TheSchemaGroupCarriesExactlyTheProvisioningSwitchAndItIsOffByDefault"/> - rather than
    /// widening this group into a grab bag of storage-adjacent settings.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSqliteGroupCarriesExactlyTheEvidencedUriParts()
    {
        string[] expected =
        [
            nameof(SqliteOptions.Check),
            nameof(SqliteOptions.DataDirectory),
            nameof(SqliteOptions.DatabaseFileName),
            nameof(SqliteOptions.Journal),
            nameof(SqliteOptions.Mode),
            nameof(SqliteOptions.Password),
        ];

        Assert.Equal(expected, PublicMemberNamesOf(typeof(SqliteOptions)));
    }

    /// <summary>
    /// The schema group carries exactly the provisioning switch, and provisioning is OFF by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE FALSE DEFAULT IS THE ASSERTION THAT MATTERS, AND IT PROTECTS THREE THINGS AT ONCE. A
    /// characterization run must be able to rely on the <c>persistence-db</c> volume being untouched
    /// between the legacy-side and target-side captures of one workflow identifier (AAP 0.6.7), so a
    /// service that provisioned unasked would violate the parity rule on every restart. Every existing
    /// deployment, and every service-level test in this project - all of which boot the same composition
    /// root - behaves exactly as it did before this section existed only because the default is off. And a
    /// service that mutates its own storage without being asked is the surprise the fail-fast posture is
    /// meant to preclude. The orchestration manifest turns it ON explicitly, in one place; if this row
    /// ever fails because the default flipped, that is the defect and not this assertion.
    /// </para>
    /// <para>
    /// ONE MEMBER, AND THE SINGLE-MEMBER SET IS PART OF THE CLAIM. A retry count, a timeout or a
    /// "recreate" flag appearing here would each be a policy this section has no evidence for - and the
    /// third would be the destructive capability <c>SchemaProvisioner</c> is forbidden to have. The lock
    /// attempt count is a constructor parameter with a compiled default precisely so it stays off this
    /// surface.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSchemaGroupCarriesExactlyTheProvisioningSwitchAndItIsOffByDefault()
    {
        Assert.Equal(
            [nameof(SchemaOptions.ApplyMigrationsOnStartup)],
            PublicMemberNamesOf(typeof(SchemaOptions)));

        Assert.False(new SchemaOptions().ApplyMigrationsOnStartup);
        Assert.False(new PersistenceOptions().Schema.ApplyMigrationsOnStartup);
        Assert.False(PersistenceOptionsBuilder.Default().Schema.ApplyMigrationsOnStartup);
    }

    /// <summary>
    /// Both values of the provisioning switch validate, and a section bound to null is reported against
    /// its own key path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// NEITHER VALUE IS REFUSED, DELIBERATELY. <see langword="false"/> is the shipped code default and
    /// <see langword="true"/> is what the orchestration manifest sets so the one documented bring-up
    /// command reaches a healthy stack on a fresh volume - a deployment is entitled to either, and a rule
    /// here could only refuse one of the two positions it may legitimately hold. That is the same
    /// treatment <c>TransactionPool</c> receives, and for the same reason.
    /// </para>
    /// <para>
    /// THE NULL SECTION IS STILL REPORTED, WHICH IS NOT THE SAME THING AS BEING UNVALIDATED. Provisioning
    /// reads this section during startup, so a group bound to an explicit null would be a null-reference
    /// exception with no configuration path attached to it rather than a message naming a key. Being
    /// checked for having been BOUND while carrying no rule about its VALUE is exactly the distinction.
    /// </para>
    /// </remarks>
    [Fact]
    public void BothValuesOfTheProvisioningSwitchValidateAndANullSectionIsReported()
    {
        foreach (bool configured in (bool[])[false, true])
        {
            PersistenceOptions options = PersistenceOptionsBuilder.Default();
            options.Schema.ApplyMigrationsOnStartup = configured;

            ValidateOptionsResult result = new PersistenceOptionsValidator().Validate(name: null, options);

            Assert.True(result.Succeeded, Describe(result));
            Assert.Equal(configured, options.Schema.ApplyMigrationsOnStartup);
        }

        PersistenceOptions unbound = PersistenceOptionsBuilder.Default();
        unbound.Schema = null!;

        ValidateOptionsResult refusal = new PersistenceOptionsValidator().Validate(name: null, unbound);

        Assert.True(refusal.Failed);
        Assert.Contains(
            refusal.Failures!,
            failure => failure.StartsWith("Schema was bound to null", StringComparison.Ordinal));
    }

    /// <summary>
    /// The password is null when unset, has no default, and carries no validation attribute in either
    /// direction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// NULL AND EMPTY ARE DIFFERENT STATES AND THE DIFFERENCE IS THE POINT. The member is nullable with
    /// no initialiser precisely so "no password" stays distinguishable from "an empty password"; a
    /// <c>""</c> default in either settings file would destroy that distinction AND would be a
    /// credential-shaped default in a file that carries no secret of any kind (C-F). It is bound from the
    /// environment only.
    /// </para>
    /// <para>
    /// IT IS UNVALIDATED IN EVERY DIRECTION, deliberately. It is optional, its absence is meaningful, and
    /// its content is a credential that may not be echoed into a failure message even to say it is
    /// malformed - so there is nothing for an attribute to assert. This row therefore pins the ABSENCE of
    /// attributes as much as the null.
    /// </para>
    /// <para>
    /// This file supplies no value for it. What the composed connection string does with one - and the
    /// rule that it is passed as the open call's SECOND ARGUMENT rather than appended to the URI
    /// [w_test_sqlite.srw:L456] - belongs to the connection factory's suite.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePasswordIsNullWhenUnsetAndCarriesNoDefaultAndNoValidation()
    {
        Assert.Null(new SqliteOptions().Password);
        Assert.Null(new PersistenceOptions().Sqlite.Password);
        Assert.Null(PersistenceOptionsBuilder.Default().Sqlite.Password);

        PropertyInfo? password = typeof(SqliteOptions).GetProperty(
            nameof(SqliteOptions.Password),
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(password);
        Assert.Empty(password!.GetCustomAttributes<ValidationAttribute>(inherit: true));
        Assert.Equal(
            NullabilityState.Nullable,
            new NullabilityInfoContext().Create(password).ReadState);

        // AND A GRAPH WITH NO PASSWORD IS VALID, which is what makes the unencrypted path the shipped
        // one. Encrypted-database parity is out of scope for this phase: the cipher-enabled library in
        // the repository is materially older than the plain one and its key-derivation and per-page
        // integrity settings are not reachable through any framework API, so there is no evidence of the
        // settings an existing encrypted file was created with.
        Assert.True(new PersistenceOptionsBuilder().Validate().Succeeded);
    }

    // ==============================================================================================
    //  REGION 4A - THE VALIDATOR IS FAIL-FAST AND ACCUMULATES [pfw.sra:L111-L144]
    // ==============================================================================================

    /// <summary>
    /// The validator is registered through the framework's own validation interface, so the host can
    /// enforce it on start.
    /// </summary>
    /// <remarks>
    /// WITHOUT THE INTERFACE THERE IS NO FAIL-FAST AT ALL. Data-annotation validation does not recurse
    /// into nested complex properties, so every annotation on the groups above would be silently ignored
    /// and a service configured with an empty authority or a chunk size of 1000 would start perfectly
    /// happily. This row pins the mechanism that makes the rest of this region reachable from startup
    /// rather than only from a test.
    /// </remarks>
    [Fact]
    public void TheValidatorParticipatesThroughTheFrameworkInterfaceSoStartupCanEnforceIt()
    {
        Assert.IsAssignableFrom<IValidateOptions<PersistenceOptions>>(new PersistenceOptionsValidator());
    }

    /// <summary>
    /// Every group is non-null by default and the data-object roster starts empty, so a partial
    /// configuration binds rather than producing a null to dereference.
    /// </summary>
    /// <remarks>
    /// <para>
    /// AN OMITTED SECTION MUST LEAVE THE PRESERVED LEGACY DEFAULTS IN PLACE. That only works because every
    /// group property is initialised to a fresh instance, which is what lets a deployment configure one
    /// section and inherit the rest - and it is the reason the null-section path below is reachable only
    /// through an explicit null rather than through omission.
    /// </para>
    /// <para>
    /// AN EMPTY DATA-OBJECT ROSTER IS LEGAL, not a missing configuration: a deployment whose callers always
    /// supply their own statement needs no named definition, and refusing to start over an unused
    /// capability would be worse than serving the callers that do not need it.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryGroupIsNonNullByDefaultSoAPartialConfigurationBinds()
    {
        PersistenceOptions options = new();

        PropertyInfo[] groups =
        [
            .. typeof(PersistenceOptions)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(static property => NestedOptionTypeOf(property.PropertyType) is not null),
        ];

        Assert.NotEmpty(groups);
        Assert.All(groups, group => Assert.NotNull(group.GetValue(options)));
        Assert.Empty(options.DataObjects);
    }

    /// <summary>
    /// A valid graph passes with no failure of any kind attached to the result.
    /// </summary>
    /// <remarks>
    /// THE SECOND ASSERTION IS THE ONE WORTH HAVING. A validator that returned a success result while
    /// still populating a failure list would satisfy a bare success check and then hand a startup message
    /// an operator could not act on to whichever consumer read the failures. The two halves of the result
    /// have to agree.
    /// </remarks>
    [Fact]
    public void AValidGraphPassesWithNoFailureAttachedToTheResult()
    {
        ValidateOptionsResult result = new PersistenceOptionsValidator()
            .Validate(name: null, PersistenceOptionsBuilder.Default());

        Assert.True(result.Succeeded, Describe(result));
        Assert.False(result.Failed);
        Assert.Null(result.Failures);
        Assert.Null(result.FailureMessage);
    }

    /// <summary>
    /// A completely unconfigured graph is REFUSED, because the inbound verification settings have no safe
    /// default to fall back on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS THE FAIL-FAST POSTURE IN ITS PUREST FORM. Every other group in the graph carries preserved
    /// legacy defaults and starts happily unconfigured; the verification group cannot, because there is no
    /// defensible default for "which authority mints the tokens I accept" or "which audience proves a
    /// token was minted for me". Inventing one would mean either trusting an issuer nobody chose or
    /// accepting a credential minted for another service - so the type declines to guess and the host
    /// refuses to start (C-G).
    /// </para>
    /// <para>
    /// The shipped settings file is what supplies all three, which is why a deployed service starts and a
    /// bare object does not. Softening this into warn-and-continue would be a behavioural change dressed
    /// up as robustness, and the framework's own posture for a structural fault is the opposite: it
    /// decodes a seven-field assert payload and then halts [ws_objects/pfw.pbl.src/pfw.sra:L111-L144].
    /// </para>
    /// </remarks>
    [Fact]
    public void AnUnconfiguredGraphIsRefusedBecauseVerificationHasNoSafeDefault()
    {
        ValidateOptionsResult result = new PersistenceOptionsValidator()
            .Validate(name: null, new PersistenceOptions());

        Assert.True(result.Failed);
        Assert.NotNull(result.Failures);

        string[] failures = [.. result.Failures!];
        string[] expectedPaths = ["Jwt:Authority", "Jwt:Audience", "Jwt:PermittedCallers"];

        foreach (string path in expectedPaths)
        {
            Assert.Contains(
                failures,
                failure => failure.StartsWith(path, StringComparison.Ordinal));
        }

        // EXACTLY THOSE THREE. A fourth failure would mean one of the preserved legacy defaults had
        // become unstartable - the shipped zero keep-alive window, the reset-state page index and page
        // size, or the empty transaction class name are all legal and none of them may be refused (C-B).
        Assert.Equal(expectedPaths.Length, failures.Length);
    }

    /// <summary>
    /// The five key paths a multiply-invalid graph must name, one per mechanism.
    /// </summary>
    public static TheoryData<string> AccumulatedFaultKeyPaths => new()
    {
        "Sqlite:DataDirectory",
        "Sqlite:Journal",
        "Query:ChunkSize",
        "Jwt:Authority",
        "Handles:MaxPerPrincipal",
    };

    /// <summary>
    /// A graph carrying five independent faults reports every one of them against its own configuration
    /// key path, rather than stopping at the first.
    /// </summary>
    /// <param name="expectedKeyPath">The key path whose failure must be present.</param>
    /// <remarks>
    /// ACCUMULATE-ALL IS WHAT MAKES A FAIL-FAST MESSAGE ACTIONABLE. Startup validation that stopped at
    /// the first fault would make an operator restart five times to find five independent mistakes, and
    /// each restart would reveal exactly one. The five faults chosen here deliberately reach five
    /// DIFFERENT mechanisms - a presence annotation, a token-set check, a numeric range annotation, a
    /// second presence annotation in another group, and a relationship between two members - so a
    /// regression in any one of them surfaces here.
    /// </remarks>
    [Theory]
    [MemberData(nameof(AccumulatedFaultKeyPaths))]
    public void EveryIndependentFaultIsReportedAgainstItsOwnKeyPath(string expectedKeyPath)
    {
        ValidateOptionsResult result = new PersistenceOptionsValidator()
            .Validate(name: null, MultiplyInvalidGraph());

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures!,
            failure => failure.StartsWith(expectedKeyPath, StringComparison.Ordinal));
    }

    /// <summary>
    /// The multiply-invalid graph reports five failures and no more, so nothing is duplicated and nothing
    /// is dropped.
    /// </summary>
    /// <remarks>
    /// THE COUNT IS AS LOAD-BEARING AS THE CONTENT. One fault reported twice trains an operator to skim
    /// the list, which is why the journal check returns early for a blank value that the presence
    /// annotation has already reported. Pinning the count is what keeps that discipline from eroding.
    /// </remarks>
    [Fact]
    public void TheMultiplyInvalidGraphReportsFiveFailuresRatherThanStoppingAtTheFirst()
    {
        ValidateOptionsResult result = new PersistenceOptionsValidator()
            .Validate(name: null, MultiplyInvalidGraph());

        Assert.NotNull(result.Failures);
        Assert.Equal(5, result.Failures!.Count());
    }

    /// <summary>
    /// A structural fault is a refusal, and the offending value is left exactly as configured rather than
    /// quietly repaired.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE SECOND HALF OF THIS ROW IS THE INTERESTING HALF. A validator that "helpfully" wrote the default
    /// back over a broken value would report a failure and leave a startable graph behind it, so whichever
    /// consumer read the value next would see a working service assembled from a configuration nobody
    /// authored. Asserting that the empty directory is STILL empty afterwards is what pins
    /// validate-and-refuse rather than repair-and-continue (AAP 0.1.4).
    /// </para>
    /// <para>
    /// Note also what the validator does NOT do: it never touches the filesystem. "The data directory is
    /// present" means the SETTING is supplied, not that the path exists - checking the filesystem would
    /// make this row environment-dependent and could fail a healthy service for a transient reason, on a
    /// startup path that has no way to retry.
    /// </para>
    /// </remarks>
    [Fact]
    public void AStructuralFaultIsRefusedAndTheOffendingValueIsNotQuietlyRepaired()
    {
        PersistenceOptions options = new PersistenceOptionsBuilder()
            .WithDataDirectory(string.Empty)
            .Build();

        ValidateOptionsResult result = new PersistenceOptionsValidator().Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failures);
        Assert.Single(result.Failures!);
        Assert.Equal(string.Empty, options.Sqlite.DataDirectory);
    }

    /// <summary>
    /// A section bound to an explicit null is reported against its own key path rather than dereferenced,
    /// and the other sections are still validated.
    /// </summary>
    /// <remarks>
    /// EVERY GROUP IS NON-NULLABLE AND INITIALISED, so an ABSENT section leaves the preserved defaults in
    /// place and never reaches this path. It exists for the two shapes that defeat that: a configuration
    /// document declaring a group as an explicit null, and a caller assembling the graph by hand.
    /// Reporting it is strictly better than dereferencing it - the fault becomes an attributable startup
    /// message instead of a null-reference exception with no configuration path attached to it - and the
    /// remaining groups must still be swept, or one null would mask every other mistake in the file.
    /// </remarks>
    [Fact]
    public void ASectionBoundToNullIsReportedAgainstItsKeyPathAndDoesNotMaskTheOthers()
    {
        PersistenceOptions options = new PersistenceOptionsBuilder()
            .WithAuthority(string.Empty)
            .Build();
        options.Query = null!;

        ValidateOptionsResult result = new PersistenceOptionsValidator().Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures!,
            failure => failure.StartsWith("Query was bound to null", StringComparison.Ordinal));
        Assert.Contains(
            result.Failures!,
            failure => failure.StartsWith("Jwt:Authority", StringComparison.Ordinal));
    }

    /// <summary>
    /// A null graph is a programming error rather than a validation failure.
    /// </summary>
    /// <remarks>
    /// THE DISTINCTION MATTERS BECAUSE THE TWO HAVE DIFFERENT AUDIENCES. A validation failure is a
    /// message to an operator about a settings file; a null graph means the binder was never run, which no
    /// settings change can fix. Returning a failure result for it would put an unactionable message in
    /// front of the wrong reader.
    /// </remarks>
    [Fact]
    public void ANullGraphIsAProgrammingErrorRatherThanAValidationFailure()
    {
        Assert.Throws<ArgumentNullException>(
            () => new PersistenceOptionsValidator().Validate(name: null, options: null!));
    }

    /// <summary>
    /// Instance names paired with the key-path prefix each must produce.
    /// </summary>
    public static TheoryData<string?, string> InstanceNamePrefixes => new()
    {
        { null, "" },
        { "", "" },
        { "worker", "[worker]" },
    };

    /// <summary>
    /// A named options instance folds its name into every key path, while the unnamed default produces the
    /// bare paths an operator actually edits.
    /// </summary>
    /// <param name="name">The instance name, or null or empty for the default instance.</param>
    /// <param name="expectedPrefix">The key-path prefix the failure must carry.</param>
    /// <remarks>
    /// AN EMPTY NAME IS THE UNNAMED DEFAULT, NOT AN EMPTY BRACKET. The framework passes
    /// <see cref="Options.DefaultName"/> - the empty string - for the default instance, so treating it as
    /// a name would put a meaningless <c>[]</c> in front of every message a real deployment ever sees.
    /// </remarks>
    [Theory]
    [MemberData(nameof(InstanceNamePrefixes))]
    public void AFailurePathCarriesTheInstanceNameOnlyWhenThereIsOne(string? name, string expectedPrefix)
    {
        PersistenceOptions options = new PersistenceOptionsBuilder().WithChunkSize(1_000).Build();

        ValidateOptionsResult result = new PersistenceOptionsValidator().Validate(name, options);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures!,
            failure => failure.StartsWith(
                string.Concat(expectedPrefix, "Query:ChunkSize"),
                StringComparison.Ordinal));
    }

    // ==============================================================================================
    //  REGION 4B - THE NEGATIVE SECURITY PROPERTY: THIS SERVICE HOLDS NO SIGNING AUTHORITY (C-G)
    // ==============================================================================================

    /// <summary>
    /// The option graph is exactly the ten declared groups, discovered by walking it rather than by
    /// restating a list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS ROW IS WHAT MAKES THE TWO SWEEPS BELOW HONEST. Both of them assert a property over "every
    /// member of the graph", and a walk that silently stopped early would let them pass while covering
    /// nothing. Pinning the discovered TYPE SET means a new group cannot be added without either appearing
    /// here or failing this row - so a future group is swept for key material automatically rather than
    /// on the strength of somebody remembering to extend a list.
    /// </para>
    /// <para>
    /// The walk follows nested groups and the element type of a collection of groups, which is how the two
    /// data-object types are reached: they hang off a list rather than off a property.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheOptionGraphIsExactlyTheTenDeclaredGroups()
    {
        string[] expected =
        [
            nameof(DataObjectColumnOptions),
            nameof(DataObjectOptions),
            nameof(HandleLifecycleOptions),
            nameof(InternalTlsTrustOptions),
            nameof(JwtOptions),
            nameof(PersistenceOptions),
            nameof(QueryOptions),

            // The schema-provisioning switch, added when startup provisioning became configurable so the
            // documented single-command bring-up could reach a healthy stack on a fresh volume. It is its
            // own group rather than a member of SqliteOptions precisely because the row below pins that
            // group to the evidenced connection-URI grammar and nothing else.
            nameof(SchemaOptions),

            nameof(SqliteOptions),
            nameof(TransactionPoolOptions),
        ];

        string[] discovered =
        [
            .. OptionGraphTypes()
                .Select(static type => type.Name)
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal(expected, discovered);
        Assert.All(OptionGraphTypes(), static type => Assert.NotEmpty(
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance)));
    }

    /// <summary>
    /// Every member of the whole graph is a plain setting type that structurally cannot carry key
    /// material.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A CLOSED WHITELIST RATHER THAN A BLACKLIST, and that choice is the whole strength of this row. A
    /// blacklist of key-bearing types has to anticipate what a future author reaches for - a byte array, a
    /// certificate, an asymmetric algorithm, a token-library key type, a wrapper around any of them - and
    /// it fails silently for whatever it did not name. A whitelist fails CLOSED: a member of any type
    /// outside the small set a settings file can express breaks this row and has to be justified.
    /// </para>
    /// <para>
    /// Security is the sole token issuer in this system and exactly one signing secret exists in it. This
    /// service holds VERIFICATION material only and obtains it by fetching the authority's published key
    /// set, so nothing in this graph needs to be able to hold bytes - which is why nothing in it may.
    /// </para>
    /// <para>
    /// Enumerations are permitted as a class because an enumeration cannot carry material either, and one
    /// is genuinely needed: the three-state integrity check.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryOptionMemberIsAPlainSettingTypeThatCannotCarryKeyMaterial()
    {
        IReadOnlyList<PropertyInfo> members = OptionGraphMembers();

        Assert.NotEmpty(members);
        Assert.All(members, static member => Assert.True(
            IsPlainSettingType(member.PropertyType),
            $"{member.DeclaringType?.Name}.{member.Name} is declared as "
                + $"{member.PropertyType.Name}, which is not one of the plain setting types this "
                + "configuration surface is closed over. A member able to hold bytes, a certificate, an "
                + "asymmetric key or a token-library key type would make this service a key holder, and "
                + "Security is the sole issuer - this service verifies and never mints."));
    }

    /// <summary>
    /// The spellings a signing key, a private key or a credential would be given.
    /// </summary>
    /// <remarks>
    /// <c>KeyPath</c> and <c>CertPath</c> are here because mutual TLS is the documented per-pair fallback
    /// rather than this deployment's posture: a client key path appearing on this surface would mean a
    /// client identity had been scaffolded without the pair that needs it being decided. The one
    /// trust-related member on the surface is a path to a PUBLIC certificate authority, which carries no
    /// private material and matches none of these markers.
    /// </remarks>
    public static TheoryData<string> KeyMaterialMarkers => new()
    {
        "SigningKey",
        "SignKey",
        "PrivateKey",
        "SecretKey",
        "SymmetricKey",
        "KeyMaterial",
        "KeyBytes",
        "Secret",
        "Password",
        "Credential",
        "Certificate",
        "Thumbprint",
        "KeyPath",
        "CertPath",
        "Jwks",
        "Pem",
        "Pfx",
    };

    /// <summary>
    /// No member anywhere in the graph is named for signing or key material, save the two documented
    /// exceptions.
    /// </summary>
    /// <param name="marker">A spelling that would indicate signing or credential material.</param>
    /// <remarks>
    /// <para>
    /// THE NAME SWEEP CATCHES WHAT THE TYPE SWEEP CANNOT. A string can hold a key in the same way it holds
    /// an address, so the type whitelist alone would accept a member called <c>SigningKey</c> as long as it
    /// were a string. Names are how an author signals intent, so both sweeps run.
    /// </para>
    /// <para>
    /// EXCEPTION ONE - <c>JwtOptions.ValidateIssuerSigningKey</c>, whose name contains a marker and which
    /// is a BOOLEAN. It is a verification-side switch deciding whether the inbound token's signature is
    /// checked against the published key, which is the opposite of holding a signing key, and a boolean
    /// cannot carry material in any case. Its type is asserted rather than assumed, so the exception stops
    /// applying the moment the member stops being a switch.
    /// </para>
    /// <para>
    /// EXCEPTION TWO - <c>SqliteOptions.Password</c>, the one credential-shaped member that legitimately
    /// exists. It is a DATABASE credential for an encrypted file: it mints nothing, signs nothing and is
    /// accepted by no other service. Naming it individually keeps the sweep sharp - a SECOND
    /// credential-shaped member appearing anywhere still fails this row, which a blanket relaxation of the
    /// marker list would have stopped detecting.
    /// </para>
    /// <para>
    /// THE <c>Jwks</c> MARKER IS THE ABSENCE OF A KEY-SET PATH, AND THAT ABSENCE IS A DECISION RATHER THAN
    /// AN OVERSIGHT. Declaring and validating a key-set path reads as though this service composed its
    /// own address beneath the authority. It does not: the stock bearer handler resolves the key set by
    /// fetching the authority's discovery document and following the address that document publishes, so
    /// no code path would read such a setting - and an operator who overrode it would change nothing while
    /// believing a key-set address had moved, which is strictly worse than having no setting at all. One
    /// metadata flow is advertised and it is the live one.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(KeyMaterialMarkers))]
    public void NoOptionMemberIsNamedForSigningOrKeyMaterial(string marker)
    {
        IReadOnlyList<PropertyInfo> members = OptionGraphMembers();

        Assert.NotEmpty(members);

        foreach (PropertyInfo member in members)
        {
            if (!member.Name.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            bool isTheVerificationSwitch =
                member.DeclaringType == typeof(JwtOptions)
                && string.Equals(
                    member.Name,
                    nameof(JwtOptions.ValidateIssuerSigningKey),
                    StringComparison.Ordinal)
                && member.PropertyType == typeof(bool);

            bool isTheDatabaseCredential =
                member.DeclaringType == typeof(SqliteOptions)
                && string.Equals(
                    member.Name,
                    nameof(SqliteOptions.Password),
                    StringComparison.Ordinal);

            Assert.True(
                isTheVerificationSwitch || isTheDatabaseCredential,
                $"{member.DeclaringType?.Name}.{member.Name} matches the marker \"{marker}\" and is "
                    + "neither of the two documented exceptions. This service holds verification "
                    + "material only; Security is the sole issuer, and exactly one signing secret "
                    + "exists in the whole system.");
        }
    }

    /// <summary>
    /// The two documented exceptions to the name sweep exist, so neither skip above is open-ended.
    /// </summary>
    /// <remarks>
    /// A SKIP THAT MATCHES NOTHING IS A HOLE. If either member were renamed or removed, the corresponding
    /// branch in the sweep would stop matching and would quietly allow a differently named member through
    /// in its place. Asserting that both exist - and that the switch is still a boolean, which is the
    /// entire reason it is allowed to carry the marker - is what keeps the exceptions honest.
    /// </remarks>
    [Fact]
    public void BothDocumentedExceptionsToTheNameSweepStillExistInTheirDeclaredShape()
    {
        PropertyInfo? verificationSwitch = typeof(JwtOptions).GetProperty(
            nameof(JwtOptions.ValidateIssuerSigningKey),
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(verificationSwitch);
        Assert.Equal(typeof(bool), verificationSwitch!.PropertyType);

        PropertyInfo? databaseCredential = typeof(SqliteOptions).GetProperty(
            nameof(SqliteOptions.Password),
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(databaseCredential);
        Assert.Equal(typeof(string), databaseCredential!.PropertyType);

        // AND THE DATABASE CREDENTIAL IS THE ONLY CREDENTIAL-SHAPED MEMBER IN THE WHOLE GRAPH. A second
        // one would be a second thing to rotate, in a service whose only legitimate secret is the one
        // above.
        PropertyInfo[] credentialShaped =
        [
            .. OptionGraphMembers()
                .Where(static member => member.Name.Contains(
                    nameof(SqliteOptions.Password),
                    StringComparison.OrdinalIgnoreCase)),
        ];

        PropertyInfo single = Assert.Single(credentialShaped);
        Assert.Equal(typeof(SqliteOptions), single.DeclaringType);
    }

    /// <summary>
    /// The verification group carries exactly its ten verification settings - an authority, an audience,
    /// the metadata transport flag, the four invariant checks, the two key-set refresh intervals and the
    /// permitted-caller roster - and no eleventh member of any kind.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A CLOSED SET IS THE STRONGEST FORM THIS ASSERTION CAN TAKE. The marker sweep above rejects a member
    /// NAMED like a key; this rejects an eleventh member whatever it is called, which is the shape a
    /// signing authority would most plausibly arrive in - something innocuous beside the settings that
    /// already look like security.
    /// </para>
    /// <para>
    /// The four invariant checks and the two refresh intervals have their own suite, which owns their
    /// defaults, their refusals and their floors. This row asserts only their PRESENCE as members of the
    /// closed set, because a set assertion that omitted them would pass while one of them was deleted.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheVerificationGroupCarriesExactlyItsTenVerificationSettings()
    {
        string[] expected =
        [
            nameof(JwtOptions.Audience),
            nameof(JwtOptions.Authority),
            nameof(JwtOptions.MetadataAutomaticRefreshInterval),
            nameof(JwtOptions.MetadataRefreshInterval),
            nameof(JwtOptions.PermittedCallers),
            nameof(JwtOptions.RequireHttpsMetadata),
            nameof(JwtOptions.ValidateAudience),
            nameof(JwtOptions.ValidateIssuer),
            nameof(JwtOptions.ValidateIssuerSigningKey),
            nameof(JwtOptions.ValidateLifetime),
        ];

        Assert.Equal(expected, PublicMemberNamesOf(typeof(JwtOptions)));
    }

    // ==============================================================================================
    //  HELPERS - NO CLOCK, NO I/O, NO SHARED MUTABLE STATE
    // ==============================================================================================

    /// <summary>
    /// Builds a graph carrying five independent faults, one per validation mechanism.
    /// </summary>
    /// <returns>The invalid graph.</returns>
    /// <remarks>
    /// <para>
    /// Each fault reaches a DIFFERENT mechanism, which is what makes the accumulation assertion meaningful
    /// rather than five variations on one code path:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>an empty data directory - a presence annotation;</description></item>
    ///   <item><description>a journal token outside the legacy six - an explicit token-set check;</description></item>
    ///   <item><description>a chunk size at the inclusive floor - a numeric range annotation;</description></item>
    ///   <item><description>an empty authority - a presence annotation in another group;</description></item>
    ///   <item><description>
    ///   a per-caller handle ceiling above the total - a relationship between two members, which no
    ///   annotation can express.
    ///   </description></item>
    /// </list>
    /// <para>
    /// The journal token is deliberately a non-empty wrong value rather than a blank one, because a blank
    /// would be reported by the presence annotation and the token check returns early for it - so a blank
    /// would test the same mechanism twice and leave the token set unexercised.
    /// </para>
    /// </remarks>
    private static PersistenceOptions MultiplyInvalidGraph()
    {
        PersistenceOptions options = new PersistenceOptionsBuilder()
            .WithDataDirectory(string.Empty)
            .WithJournal("ROLLBACK")
            .WithChunkSize(1_000)
            .WithAuthority(string.Empty)
            .Build();

        options.Handles.MaxPerPrincipal = options.Handles.MaxTotalPerRegistry + 1;

        return options;
    }

    /// <summary>
    /// Renders a validation result for an assertion message.
    /// </summary>
    /// <param name="result">The result to render.</param>
    /// <returns>The accumulated failures, or a note that none was reported.</returns>
    /// <remarks>
    /// USED ONLY ON ROWS THAT EXPECT SUCCESS, so a regression names the rule that broke instead of
    /// reporting a bare false. No option VALUE is interpolated into it - the failures the validator
    /// composes are its own text, and the one credential-shaped member is never validated and so can
    /// never appear in one (C-F).
    /// </remarks>
    private static string Describe(ValidateOptionsResult result)
    {
        return result.Failures is null
            ? result.FailureMessage ?? "no failure was reported"
            : string.Join(" | ", result.Failures);
    }

    /// <summary>
    /// The public instance property names of one option group, ordered so a comparison is deterministic.
    /// </summary>
    /// <param name="type">The group type.</param>
    /// <returns>The ordered member names.</returns>
    /// <remarks>
    /// ORDERED ORDINALLY BECAUSE REFLECTION ORDER IS NOT SPECIFIED. An unordered comparison would be a
    /// test that passes or fails on the runtime's whim, which is worse than no test - and ordinal keeps the
    /// comparison independent of the current culture.
    /// </remarks>
    private static string[] PublicMemberNamesOf(Type type)
    {
        return
        [
            .. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(static property => property.Name)
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Every type reachable from the root of the option graph, including the root.
    /// </summary>
    /// <returns>The discovered group types, in discovery order.</returns>
    /// <remarks>
    /// A BREADTH-FIRST WALK RATHER THAN A HARDCODED LIST, so a group added later is swept by the two
    /// security rows automatically. The list is deduplicated by membership rather than by a set, because
    /// discovery order is useful when a failure has to be read.
    /// </remarks>
    private static IReadOnlyList<Type> OptionGraphTypes()
    {
        List<Type> discovered = [];
        Queue<Type> pending = new();
        pending.Enqueue(typeof(PersistenceOptions));

        while (pending.Count > 0)
        {
            Type type = pending.Dequeue();

            if (discovered.Contains(type))
            {
                continue;
            }

            discovered.Add(type);

            foreach (PropertyInfo property in type.GetProperties(
                BindingFlags.Public | BindingFlags.Instance))
            {
                if (NestedOptionTypeOf(property.PropertyType) is Type nested)
                {
                    pending.Enqueue(nested);
                }
            }
        }

        return discovered;
    }

    /// <summary>
    /// Every public instance property of every type in the graph.
    /// </summary>
    /// <returns>The members to sweep.</returns>
    private static IReadOnlyList<PropertyInfo> OptionGraphMembers()
    {
        return
        [
            .. OptionGraphTypes().SelectMany(static type => type.GetProperties(
                BindingFlags.Public | BindingFlags.Instance)),
        ];
    }

    /// <summary>
    /// The nested option group a member's type refers to, either directly or as the element of a
    /// collection, or <see langword="null"/> when the member is a leaf.
    /// </summary>
    /// <param name="memberType">The declared member type.</param>
    /// <returns>The nested group type, or <see langword="null"/>.</returns>
    /// <remarks>
    /// A GROUP IS IDENTIFIED BY ITS NAMESPACE rather than by a marker interface or a name suffix, because
    /// that is the one property every group in this surface genuinely shares and the one a new group cannot
    /// accidentally fail to have. A string is excluded explicitly - it is a class, and it is a leaf.
    /// </remarks>
    private static Type? NestedOptionTypeOf(Type memberType)
    {
        Type candidate = memberType;

        if (candidate.IsGenericType && candidate.GetGenericArguments() is [Type element])
        {
            candidate = element;
        }

        bool isGroup =
            candidate.IsClass
            && candidate != typeof(string)
            && candidate.Namespace == typeof(PersistenceOptions).Namespace;

        return isGroup ? candidate : null;
    }

    /// <summary>
    /// Whether a member type is one of the plain setting types a configuration file can express and which
    /// cannot carry key material.
    /// </summary>
    /// <param name="memberType">The declared member type.</param>
    /// <returns><see langword="true"/> when the type is permitted on this surface.</returns>
    /// <remarks>
    /// THE SET IS CLOSED ON PURPOSE - see the row that consumes it. A nullable value type is unwrapped
    /// first, because a nullable enumeration is how the one genuine three-state setting is expressed and
    /// the nullability is not what makes a type safe or unsafe here.
    /// </remarks>
    private static bool IsPlainSettingType(Type memberType)
    {
        Type resolved = Nullable.GetUnderlyingType(memberType) ?? memberType;

        if (resolved.IsEnum)
        {
            return true;
        }

        if (resolved == typeof(string)
            || resolved == typeof(bool)
            || resolved == typeof(int)
            || resolved == typeof(long)
            || resolved == typeof(double)
            || resolved == typeof(TimeSpan))
        {
            return true;
        }

        // A nested group, or a collection of nested groups.
        if (NestedOptionTypeOf(memberType) is not null)
        {
            return true;
        }

        // A collection of plain strings - the permitted-caller roster.
        return memberType.IsGenericType
            && memberType.GetGenericArguments() is [Type element]
            && element == typeof(string);
    }
}
