// ==============================================================================================
//  SqlTaskBaseTests.cs - THE SQL TASK BASE PARITY SUITES
//  --------------------------------------------------------------------------------------------
//  UNDER TEST     services/persistence-service/PowerFramework.Persistence/Tasks/SqlTaskBase.cs
//  ORACLE         ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru      (READ ONLY)
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_hook.sru (READ ONLY)
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru     (READ ONLY)
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru   (READ ONLY)
//                 docs/PB多线程绕坑提示.md                                              (READ ONLY)
//
//  WHAT THESE SUITES ARE FOR. SqlTaskBase reproduces eight legacy behaviours that a maintainer
//  would otherwise "fix", and a comment alone cannot stop that. Each is PINNED here so that
//  correcting it in the subject breaks a test whose NAME says the behaviour is deliberate:
//
//    1  the one-based SetParam upsert, whose write target is a loop variable that outlives its loop
//    2  the literal generator, byte for byte, in both dialects
//    3  the missing per-element null guard on the ARRAY literal path, which the SCALAR path has
//    4  the zero-length array that falls into the SCALAR branch
//    5  the cache-hit block that restores the SORT by calling SetFilter          <- the big one
//    6  the "!" and "?" describe-sentinel normalisation, applied to SNAPSHOTS ONLY
//    7  the NESTED DBParm parse, which makes NCharBind=1 alone completely inert
//    8  the database-error event's literal 3, which is not a return code at all
//    9  the UNANCHORED DBParm patterns, under which DisableBind=10 reads as SET
//
//  and the two boundaries that are easy to mistake for leaks and are neither: the IN-PROCESS database
//  error payload deliberately still carries the generated statement, because the redaction obligation
//  attaches to the two EXITS - the wire projection and the log record - and masking earlier would
//  destroy what SQL-preview interception and characterization comparison both need.
//
//  plus the two ORDERING contracts - the BACKWARD commit-signal propagation, and OnError's
//  rollback-before-ancestor - and the hook's decline contract, where E_NO_IMPLEMENTATION means
//  "not handled, run the default" rather than "failed".
//
//  AND FOUR STRUCTURAL GUARANTEES that no behavioural assertion can express, each of which is a
//  constraint rather than a preference, so each is pinned by a test that fails if it is traded away:
//
//    A  the DBParm patterns are the ORACLE'S OWN TEXT, case-insensitive, and declared ONCE     (C-B)
//    B  the parse uses the BASE CLASS LIBRARY'S regular expressions and pulls in no deferred
//       capability, so it is not a dependency on the deferred regexp library                   (C-D)
//    C  the literal generator's interpolated output REACHES ISqlRedactor and is masked there    (C-F)
//    D  the worker half and its caller-side proxy remain TWO TYPES - the thread-affinity duality
//       is a contract, not commentary, and flattening it is what AAP 0.4.5.4 forbids            (C-K)
//
//  NO DATABASE, NO REAL THREAD, NO NETWORK AND NO DataWindow RUNTIME IS INVOLVED ANYWHERE IN THIS
//  FILE (C-E, C-H). Every collaborator is a hand-written double in this file: the threading
//  substrate, the caller-side proxy, the pooled transaction, its activator, and the DataWindow
//  runtime. The clock is a fixed TimeProvider that nothing advances, which is exactly the point -
//  the subject reads no clock, and a test that needed one would prove otherwise. Nothing here
//  sleeps, waits on real time, or asserts a duration, so two runs produce identical results
//  (AAP 0.6.7), and nothing here asserts a speed - the datastore cache is pinned by BEHAVIOUR only,
//  never by a performance claim (AAP 0.8.5).
//
//  EVERY VALUE HERE IS SYNTHETIC (C-F). No password, account, host or connection string is copied
//  from the legacy tree or from any of the catalogued in-source secret sites. The one
//  password-shaped constant is spelled so it could not be mistaken for a credential.
//
//  WHY A FEW SIBLING TYPES ARE NAMED HERE, AND WHY THAT IS NOT A WIDENED DEPENDENCY. Four of the
//  guarantees above cannot be stated about the base alone, because the property under test IS the
//  relationship between the base and something else: the rank guard lives on the CALLER-SIDE half,
//  the injected redactor is a dependency of the DERIVED task that composes statements, `Reset`'s
//  base-chain can only be observed through a REAL derived override, and "two types, not one" is a
//  statement about a PAIR. The types involved - SqlTaskProxyBase and the three proxies,
//  PersistenceSqlTaskProxyHost, and SqlQueryTask / SqlUpdateTask / SqlCommandTask - are all
//  `internal` members of the SAME PowerFramework.Persistence assembly this project already
//  references once, so naming them adds no project reference, no package and no edge to the build
//  graph. It is also the established shape in this folder: SqlCommandTaskProxyTests and
//  PersistenceTaskCompositionTests reach across the same pair for the same reason.
// ==============================================================================================

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Tasks;
using PowerFramework.Persistence.Tasks.TaskProxies;
using PowerFramework.Persistence.Transactions;
using PowerFramework.Shared.Containers;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// The parity suites for <c>Tasks/SqlTaskBase.cs</c>.
/// </summary>
public sealed class SqlTaskBaseTests
{
    /// <summary>A synthetic data-object name distinct from the golden-master fixture's.</summary>
    private const string OtherDataObject = "dw_other";

    /// <summary>
    /// The thread-keyed-data key the datastore cache lives under.
    /// </summary>
    /// <remarks>
    /// <b>THE ORACLE'S OWN SPELLING, ASSERTED RATHER THAN ASSUMED</b> -
    /// <c>#ParentThread.of_HasData("$SQL.DataStoreCache")</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L531</c>], with the matching
    /// <c>of_GetData</c> and <c>of_SetData</c> at <c>:L532</c> and <c>:L536</c>. Declared here because
    /// the subject's own copy is private: the key is part of the observable contract between two tasks
    /// sharing one thread, so a drift in its spelling would silently give every task its own cache, and
    /// nothing but a literal comparison would notice.
    /// </remarks>
    private const string DataStoreCacheKey = "$SQL.DataStoreCache";

    /// <summary>A synthetic filter expression. Invented here; not copied from the legacy tree.</summary>
    private const string SomeFilter = "age > 30";

    /// <summary>A synthetic sort expression, distinct from the fixture's.</summary>
    private const string SomeSort = "name A ";

    // ==========================================================================================
    //  SUITE 1 - THE ONE-BASED SetParam UPSERT  [n_cst_thread_task_sqlbase.sru:L341-L357]
    //  R9. The oracle writes at whatever its loop variable holds AFTER the loop, so there are
    //  three outcomes and a C# for-loop can express none of them by accident.
    // ==========================================================================================

    [Fact]
    public void SetParam_OnEmptyCollection_WritesPositionOne()
    {
        // [:L346-L353] nCount is 0, the loop body never runs, and nIndex is therefore 1.
        using Harness harness = new();

        Assert.Equal(RetCode.OK, harness.Task.SetParam("Id", 7L));

        Assert.Equal(1, harness.Task.GetParamCount());
        Assert.Equal("id", harness.Task.GetParamName(1));

        object? value = null;
        Assert.Equal(RetCode.OK, harness.Task.GetParam(1, ref value));
        Assert.Equal(7L, value);
    }

    [Fact]
    public void SetParam_OnMatch_UpdatesInPlaceWithoutAppending()
    {
        // [:L349-L351] the exit leaves nIndex at the matching position.
        using Harness harness = new();
        harness.Task.AddParam("alpha", 1L);
        harness.Task.AddParam("beta", 2L);
        harness.Task.AddParam("gamma", 3L);

        Assert.Equal(RetCode.OK, harness.Task.SetParam("BETA", 99L));

        Assert.Equal(3, harness.Task.GetParamCount());

        object? value = null;
        Assert.Equal(RetCode.OK, harness.Task.GetParam("beta", ref value));
        Assert.Equal(99L, value);

        // The neighbours are untouched, which is what "in place" means.
        Assert.Equal("alpha", harness.Task.GetParamName(1));
        Assert.Equal("beta", harness.Task.GetParamName(2));
        Assert.Equal("gamma", harness.Task.GetParamName(3));
    }

    [Fact]
    public void SetParam_OnNoMatch_AppendsAtEnd()
    {
        // [:L348-L353] the loop runs to completion and leaves nIndex at nCount + 1.
        using Harness harness = new();
        harness.Task.AddParam("alpha", 1L);
        harness.Task.AddParam("beta", 2L);

        Assert.Equal(RetCode.OK, harness.Task.SetParam("delta", 4L));

        Assert.Equal(3, harness.Task.GetParamCount());
        Assert.Equal("delta", harness.Task.GetParamName(3));

        // Entry 1 is NOT the one that was written - which is precisely what a naive port does.
        Assert.Equal("alpha", harness.Task.GetParamName(1));

        object? first = null;
        Assert.Equal(RetCode.OK, harness.Task.GetParam(1, ref first));
        Assert.Equal(1L, first);
    }

    [Fact]
    public void AddParam_AlwaysAppends_EvenForADuplicateName()
    {
        // [:L255] of_addparam appends at UpperBound + 1 unconditionally; it never merges.
        using Harness harness = new();
        harness.Task.AddParam("dup", 1L);
        harness.Task.AddParam("dup", 2L);

        Assert.Equal(2, harness.Task.GetParamCount());

        // [:L502-L506] the name lookup answers the FIRST match.
        object? value = null;
        Assert.Equal(RetCode.OK, harness.Task.GetParam("dup", ref value));
        Assert.Equal(1L, value);
    }

    [Fact]
    public void ParameterSurface_BoundsAndLowerCasing_MatchTheOracle()
    {
        using Harness harness = new();

        // [:L368] of_hasparams is literally UpperBound > 0.
        Assert.False(harness.Task.HasParams());

        // [:L256] the name is lower-cased on entry.
        harness.Task.AddParam("MiXeD", 1L);
        Assert.True(harness.Task.HasParams());
        Assert.Equal("mixed", harness.Task.GetParamName(1));

        // [:L487] the READ bound is UpperBound.
        object? value = null;
        Assert.Equal(RetCode.E_OUT_OF_BOUND, harness.Task.GetParam(0, ref value));
        Assert.Equal(RetCode.E_OUT_OF_BOUND, harness.Task.GetParam(2, ref value));

        // [:L512] an out-of-bound name read answers the EMPTY STRING, not a code.
        Assert.Equal(string.Empty, harness.Task.GetParamName(0));
        Assert.Equal(string.Empty, harness.Task.GetParamName(2));

        // [:L509]
        Assert.Equal(RetCode.E_DATA_NOT_FOUND, harness.Task.GetParam("absent", ref value));

        // [:L517] the WRITE bound is UpperBound + 1 - one wider than the read bound, and writing
        // there GROWS the collection with an empty (positional) name.
        object? appended = 42L;
        Assert.Equal(RetCode.E_OUT_OF_BOUND, harness.Task.SetParam(0, ref appended));
        Assert.Equal(RetCode.E_OUT_OF_BOUND, harness.Task.SetParam(3, ref appended));
        Assert.Equal(RetCode.OK, harness.Task.SetParam(2, ref appended));
        Assert.Equal(2, harness.Task.GetParamCount());
        Assert.Equal(string.Empty, harness.Task.GetParamName(2));

        // [:L363] of_resetparams empties the collection.
        Assert.Equal(RetCode.OK, harness.Task.ResetParams());
        Assert.Equal(0, harness.Task.GetParamCount());
        Assert.False(harness.Task.HasParams());
    }

    [Fact]
    public void ParameterShape_AcceptsAOneDimensionalArray_AndTheRankGuardLivesOnTheProxy()
    {
        // 参数只支持简单类型或简单类型的一维数组 - "parameters support only simple types, or ONE-DIMENSIONAL
        // arrays of simple types" [n_cst_threading_task_sqlbase.sru:L146]. The oracle splits that rule
        // across the PROXY PAIR, and the split is the point:
        //
        //   * the CALLER-SIDE half refuses the shape up front -
        //         if UpperBound(value,2) >= 0 then return RetCode.E_INVALID_ARGUMENT   [:L148]
        //         if UpperBound(value,1) =  0 then return RetCode.E_INVALID_ARGUMENT   [:L149]
        //   * the WORKER-SIDE half - this subject - stores whatever reached it and renders it, so a
        //     rank-1 array takes the ARRAY literal path [n_cst_thread_task_sqlbase.sru:L266-L313].
        using Harness harness = new();

        // ONE-DIMENSIONAL: ACCEPTED by the worker, and the generator takes the ARRAY path for it -
        // the comma-joined list form rather than a scalar rendering.
        long[] ages = [30L, 40L, 50L];
        Assert.Equal(RetCode.OK, harness.Task.AddParam("ages", ages));
        Assert.Equal(1, harness.Task.GetParamCount());

        object? stored = null;
        Assert.Equal(RetCode.OK, harness.Task.GetParam("ages", ref stored));
        Assert.Same(ages, stored);
        Assert.Equal("30,40,50", SqlTaskBase.ParamToString(ages, (long)DatabaseType.DbtMssql));

        // MULTI-DIMENSIONAL: the worker does NOT recognise it as a parameter array, so it can never be
        // spliced into a list form. That is what makes the caller-side refusal safe rather than merely
        // tidy - there is no second path through which a rank-2 array could reach a statement.
        long[,] grid = new long[2, 2];
        Assert.NotEqual(
            "0,0,0,0",
            SqlTaskBase.ParamToString(grid, (long)DatabaseType.DbtMssql));

        // And the documented refusal itself, executed through the caller-side half so the code is
        // observed rather than quoted. The rank test runs BEFORE the worker is even consulted, which is
        // why an unbound host is sufficient here and why no worker, pool or provider is involved.
        using PersistenceSqlTaskProxyHost proxyHost = new("sqlcommand");
        using SqlCommandTaskProxy proxy = new(
            proxyHost,
            NullLogger<SqlCommandTaskProxy>.Instance,
            FixedClock.Instance);

        // [:L148] rank 2 or higher.
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, proxy.AddParam("grid", grid));

        // [:L149] the empty variable-size array, whose one-based upper bound is 0.
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, proxy.AddParam("empty", Array.Empty<long>()));
    }

    // ==========================================================================================
    //  SUITE 2 - THE SQL LITERAL GENERATOR  [n_cst_thread_task_sqlbase.sru:L262-L339]
    //  These strings appear inside generated statements that parity compares byte for byte.
    // ==========================================================================================

    /// <summary>
    /// The scalar renderings, one row per <c>choose case</c> arm and per dialect.
    /// </summary>
    /// <returns>Value, dialect, and the expected literal.</returns>
    public static TheoryData<object, long, string> ScalarLiterals() => new()
    {
        // [:L317-L318] a single-quoted literal with every embedded quote DOUBLED.
        { "plain", (long)DatabaseType.DbtMssql, "'plain'" },
        { "O'Brien", (long)DatabaseType.DbtMssql, "'O''Brien'" },
        { "a'b'c", (long)DatabaseType.DbtOracle, "'a''b''c'" },

        // [:L319-L320] case "time" - identical in both dialects.
        { new TimeOnly(9, 5, 3), (long)DatabaseType.DbtMssql, "'09:05:03'" },
        { new TimeOnly(23, 59, 59), (long)DatabaseType.DbtOracle, "'23:59:59'" },

        // [:L321-L326] case "date" - the dialects DIVERGE here.
        { new DateOnly(2022, 4, 14), (long)DatabaseType.DbtMssql, "'2022-04-14'" },
        {
            new DateOnly(2022, 4, 14),
            (long)DatabaseType.DbtOracle,
            "to_date('2022-04-14','yyyy-mm-dd')"
        },

        // [:L327-L332] case "datetime" - note the Oracle model spells hh24 and mi.
        {
            new DateTime(2022, 4, 14, 7, 8, 9, DateTimeKind.Unspecified),
            (long)DatabaseType.DbtMssql,
            "'2022-04-14 07:08:09'"
        },
        {
            new DateTime(2022, 4, 14, 7, 8, 9, DateTimeKind.Unspecified),
            (long)DatabaseType.DbtOracle,
            "to_date('2022-04-14 07:08:09','yyyy-mm-dd hh24:mi:ss')"
        },

        // [:L333-L334] `case else //Numbers` - UNQUOTED, and the decimal keeps its scale because the
        // golden-master fixture declares salary decimal(2) [dw_sqlite.srd:L12].
        { 1500L, (long)DatabaseType.DbtMssql, "1500" },
        { -7L, (long)DatabaseType.DbtOracle, "-7" },
        { 1500.00m, (long)DatabaseType.DbtMssql, "1500.00" },
        { 2.5d, (long)DatabaseType.DbtMssql, "2.5" },
        { true, (long)DatabaseType.DbtMssql, "true" },
        { false, (long)DatabaseType.DbtMssql, "false" },
    };

    [Theory]
    [MemberData(nameof(ScalarLiterals))]
    public void ParamToString_Scalar_RendersTheOraclesLiteral(object value, long dbType, string expected) =>
        Assert.Equal(expected, SqlTaskBase.ParamToString(value, dbType));

    [Fact]
    public void ParamToString_Array_JoinsWithACommaAndNoSpace()
    {
        // [:L273-L275] `if i <> n then sVal += ","` - a bare comma.
        Assert.Equal("1,2,3", SqlTaskBase.ParamToString(new object?[] { 1L, 2L, 3L }, (long)DatabaseType.DbtMssql));

        Assert.Equal(
            "'a','b'",
            SqlTaskBase.ParamToString(new object?[] { "a", "b" }, (long)DatabaseType.DbtMssql));

        // A single element carries no separator at all.
        Assert.Equal("'a'", SqlTaskBase.ParamToString(new object?[] { "a" }, (long)DatabaseType.DbtMssql));
    }

    [Fact]
    public void ParamToString_ArrayOfDates_UsesTheDialectFormPerElement()
    {
        // [:L284-L294] the Oracle arm is inside the per-element loop, so EVERY element is wrapped.
        object?[] dates = [new DateOnly(2022, 1, 2), new DateOnly(2023, 3, 4)];

        Assert.Equal(
            "to_date('2022-01-02','yyyy-mm-dd'),to_date('2023-03-04','yyyy-mm-dd')",
            SqlTaskBase.ParamToString(dates, (long)DatabaseType.DbtOracle));

        Assert.Equal(
            "'2022-01-02','2023-03-04'",
            SqlTaskBase.ParamToString(dates, (long)DatabaseType.DbtMssql));
    }

    [Fact]
    public void ParamToString_Array_DispatchesOnTheFirstElementTypeOnly()
    {
        // [:L269] `choose case ClassName(paramArray[1])` - ONE dispatch, applied to every element.
        // Element 1 is a string, so element 2 is quoted too even though it is a number.
        Assert.Equal(
            "'x','5'",
            SqlTaskBase.ParamToString(new object?[] { "x", 5L }, (long)DatabaseType.DbtMssql));

        // And with the order reversed, element 2 takes the NUMBER arm and loses its quotes.
        Assert.Equal(
            "5,x",
            SqlTaskBase.ParamToString(new object?[] { 5L, "x" }, (long)DatabaseType.DbtMssql));
    }

    [Fact]
    public void ParamToString_ZeroLengthArray_FallsIntoTheScalarBranch()
    {
        // [:L266-L267] the discrimination is `UpperBound(param) > 0`, and an empty PowerBuilder array
        // has upper bound 0 exactly like a non-array. PRESERVED, not normalised: the behaviour under
        // test is the BRANCH SELECTION, and the scalar branch's number arm answers what PowerBuilder's
        // String() answers for something it cannot convert - the empty string.
        Assert.Equal(string.Empty, SqlTaskBase.ParamToString(Array.Empty<object?>(), (long)DatabaseType.DbtMssql));

        // Decisively NOT the array branch: there is no separator and no quoting.
        Assert.DoesNotContain(",", SqlTaskBase.ParamToString(Array.Empty<object?>(), (long)DatabaseType.DbtOracle));
    }

    [Fact]
    public void ParamToString_ScalarNull_RendersTheNullKeyword()
    {
        // [:L315] `if IsNull(param) then return "NULL"` - present in the SCALAR branch and nowhere else.
        Assert.Equal("NULL", SqlTaskBase.ParamToString(null, (long)DatabaseType.DbtMssql));
        Assert.Equal("NULL", SqlTaskBase.ParamToString(null, (long)DatabaseType.DbtOracle));
    }

    [Fact]
    public void ParamToString_ArrayWithANullElement_IsNotGuarded_PreservedLegacyAsymmetry()
    {
        // PRESERVED LEGACY DEFECT (C-B). The scalar branch guards null at [:L315]; the array branch at
        // [:L266-L313] has NO per-element null test of any kind, and NO GUARD IS ADDED. PowerScript
        // then propagates: any concatenation involving a null string yields null, so one null element
        // makes the accumulator null and the whole rendering answers null.
        //
        // It emphatically does NOT answer "NULL" - that is the scalar branch's behaviour and the
        // asymmetry between the two is the thing being pinned here.
        Assert.Null(SqlTaskBase.ParamToString(new object?[] { "a", null, "c" }, (long)DatabaseType.DbtMssql));
        Assert.Null(SqlTaskBase.ParamToString(new object?[] { null, "a" }, (long)DatabaseType.DbtMssql));
        Assert.Null(SqlTaskBase.ParamToString(new object?[] { 1L, null }, (long)DatabaseType.DbtOracle));

        // The scalar path, for contrast, on the very same run.
        Assert.Equal("NULL", SqlTaskBase.ParamToString(null, (long)DatabaseType.DbtMssql));
    }

    [Fact]
    public void ParamToString_StringIsNeverTreatedAsACharacterSequence()
    {
        // A string is enumerable, but `UpperBound` on one answers 0 in the oracle, so it must take the
        // scalar path rather than being joined character by character.
        Assert.Equal("'abc'", SqlTaskBase.ParamToString("abc", (long)DatabaseType.DbtMssql));
    }

    /// <summary>
    /// The two dialects crossed with the two temporal arms - the ONLY place in the generator where the
    /// database type changes the emitted text.
    /// </summary>
    /// <returns>The value, the dialect discriminator, and the exact literal the oracle emits.</returns>
    /// <remarks>
    /// <para>
    /// A matrix of its own rather than four more rows on <see cref="ScalarLiterals"/>, because the
    /// property under test is DIVERGENCE: every other arm of the <c>choose case</c> emits the same text
    /// in both dialects, and these two do not. Reading them side by side is what makes an accidental
    /// normalisation - one dialect's form quietly used for both - visible in the test output.
    /// </para>
    /// <para>
    /// The two evidenced discriminators are the only ones the oracle declares:
    /// <c>DBT_MSSQL = 0</c> and <c>DBT_ORACLE = 1</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L60-L61</c>]. <b>SQLite is not in
    /// that enumeration at all</b>, and no third arm is invented here (constraint C-E): these
    /// assertions are pure STRING output, which is exactly how AAP 0.6.4 preserves both DBMS
    /// behaviours without provisioning an instance of either.
    /// </para>
    /// <para>
    /// <b>THE FIRST TYPE ARGUMENT IS DELIBERATELY <c>object</c>, MATCHING <see cref="ScalarLiterals"/>.</b>
    /// The subject's signature is <c>ParamToString(object? param, long dbType)</c> - it dispatches on the
    /// RUNTIME type, which is the whole behaviour under test - so a narrower, provably-serializable type
    /// argument would either need one matrix per arm or a stringly-typed stand-in, and both would hide the
    /// dispatch. The analyzer's caution about row enumeration does not bite here: every value below is a
    /// primitive or a BCL date/time type that xunit serializes, and the rows are observably enumerated
    /// individually in the run output.
    /// </para>
    /// </remarks>
    public static TheoryData<object, long, string> DialectTemporalLiterals() => new()
    {
        // [:L321-L326] and [:L355-L360] - `case "date"`. SQL Server takes the plain quoted form ...
        { new DateOnly(2022, 4, 14), (long)DatabaseType.DbtMssql, "'2022-04-14'" },

        // ... and Oracle wraps it in to_date with a MATCHING model string, lower-cased as the oracle
        // spells it.
        {
            new DateOnly(2022, 4, 14),
            (long)DatabaseType.DbtOracle,
            "to_date('2022-04-14','yyyy-mm-dd')"
        },

        // [:L327-L332] - `case "datetime"`. The Oracle model spells hh24 and mi, NOT hh and mm, and the
        // difference is not cosmetic: hh would render a 24-hour value against a 12-hour model and mm
        // would render MINUTES against a MONTH model.
        {
            new DateTime(2022, 4, 14, 7, 8, 9, DateTimeKind.Unspecified),
            (long)DatabaseType.DbtMssql,
            "'2022-04-14 07:08:09'"
        },
        {
            new DateTime(2022, 4, 14, 7, 8, 9, DateTimeKind.Unspecified),
            (long)DatabaseType.DbtOracle,
            "to_date('2022-04-14 07:08:09','yyyy-mm-dd hh24:mi:ss')"
        },

        // A late hour, so a 12-versus-24-hour slip cannot pass. 23:00 rendered against a 12-hour model
        // would read 11:00, and both rows below would still "look like a time".
        {
            new DateTime(2023, 12, 31, 23, 59, 58, DateTimeKind.Unspecified),
            (long)DatabaseType.DbtMssql,
            "'2023-12-31 23:59:58'"
        },
        {
            new DateTime(2023, 12, 31, 23, 59, 58, DateTimeKind.Unspecified),
            (long)DatabaseType.DbtOracle,
            "to_date('2023-12-31 23:59:58','yyyy-mm-dd hh24:mi:ss')"
        },
    };

    [Theory]
    [MemberData(nameof(DialectTemporalLiterals))]
    public void ParamToString_Temporal_DivergesByDialect_ByteForByte(
        object value,
        long dbType,
        string expected)
    {
        string actual = SqlTaskBase.ParamToString(value, dbType)!;

        // ORDINAL, EXPLICITLY. The default equality for two strings is already ordinal, and saying so
        // here is not redundant: these literals are byte-exact parity artefacts that travel inside
        // generated statements, so a culture-sensitive or case-insensitive comparison would let a
        // model-string casing drift - `YYYY-MM-DD` for `yyyy-mm-dd` - pass unnoticed.
        Assert.Equal(expected, actual, StringComparer.Ordinal);

        // The same fact stated as a length-and-content check, which catches a trailing space that an
        // eyeballed comparison misses.
        Assert.Equal(expected.Length, actual.Length);
    }

    [Fact]
    public void ParamToString_Temporal_TheTwoDialectsNeverAgree()
    {
        // The guard against the one mistake this matrix exists to catch: a port that renders both
        // dialects through one branch passes every row above only if the two expected strings are equal,
        // and they are not. Asserted directly so the matrix cannot be defeated by a copy-paste.
        DateOnly date = new(2022, 4, 14);
        DateTime moment = new(2022, 4, 14, 7, 8, 9, DateTimeKind.Unspecified);

        Assert.NotEqual(
            SqlTaskBase.ParamToString(date, (long)DatabaseType.DbtMssql),
            SqlTaskBase.ParamToString(date, (long)DatabaseType.DbtOracle),
            StringComparer.Ordinal);

        Assert.NotEqual(
            SqlTaskBase.ParamToString(moment, (long)DatabaseType.DbtMssql),
            SqlTaskBase.ParamToString(moment, (long)DatabaseType.DbtOracle),
            StringComparer.Ordinal);

        // Whereas the NON-temporal arms are dialect-invariant, which is the other half of the same
        // statement - the divergence is confined to date and datetime and appears nowhere else.
        Assert.Equal(
            SqlTaskBase.ParamToString("O'Brien", (long)DatabaseType.DbtMssql),
            SqlTaskBase.ParamToString("O'Brien", (long)DatabaseType.DbtOracle),
            StringComparer.Ordinal);
        Assert.Equal(
            SqlTaskBase.ParamToString(new TimeOnly(23, 59, 59), (long)DatabaseType.DbtMssql),
            SqlTaskBase.ParamToString(new TimeOnly(23, 59, 59), (long)DatabaseType.DbtOracle),
            StringComparer.Ordinal);
        Assert.Equal(
            SqlTaskBase.ParamToString(1500.00m, (long)DatabaseType.DbtMssql),
            SqlTaskBase.ParamToString(1500.00m, (long)DatabaseType.DbtOracle),
            StringComparer.Ordinal);
    }

    // ==========================================================================================
    //  SUITE 2b - THE GENERATOR'S OUTPUT AND ITS REDACTION OBLIGATION  (constraint C-F)
    //
    //  WHY THIS BELONGS BESIDE THE GENERATOR RATHER THAN IN A REDACTOR SUITE. AAP 0.6.4 traces the
    //  injection exposure to one mechanism: DisableBind=1 means the runtime does not use bind
    //  variables, so VALUES ARE INTERPOLATED INTO STATEMENT TEXT AS LITERALS. The generator above is
    //  where that interpolation happens, so it is also where a caller's data first becomes part of a
    //  string that a database error will later carry in its `sqlsyntax` member - and the legacy logger
    //  performs NO redaction at all. The obligation is therefore the generator's neighbour, and the
    //  assertions below prove the seam is reached rather than merely available.
    //
    //  WHAT IS DELIBERATELY NOT ASSERTED (constraint C-B). Nothing here asserts that the generator
    //  REFUSES hostile input, escapes more than the oracle escapes, or rewrites a suspicious literal.
    //  Preserving the interpolation IS the requirement; the mitigation is internal parameterization
    //  plus redaction, both of which are asserted, and neither of which changes the observable text.
    // ==========================================================================================

    [Fact]
    public void TheGeneratorsInterpolatedOutput_ReachesTheRedactorSeamVerbatim_AndIsMaskedThere()
    {
        using Harness harness = new();

        // Three literals from three different arms of the generator, so no single arm carries the test.
        harness.Task.AddParam("name", "O'Brien");
        harness.Task.AddParam("salary", 1500.00m);
        harness.Task.AddParam("birth", new DateOnly(1990, 1, 2));

        Assert.Equal(
            RetCode.OK,
            harness.Task.CallBindParams(
                "UPDATE COMPANY SET salary = :salary, birth = :birth WHERE name = :name",
                (long)DatabaseType.DbtMssql,
                out SqlBoundStatement bound));

        // The generator really did interpolate - each literal is present in the OBSERVABLE text, in the
        // oracle's own form, doubled quote included.
        Assert.Contains("'O''Brien'", bound.ObservableText, StringComparison.Ordinal);
        Assert.Contains("1500.00", bound.ObservableText, StringComparison.Ordinal);
        Assert.Contains("'1990-01-02'", bound.ObservableText, StringComparison.Ordinal);

        // ... while the text that actually EXECUTES carries provider placeholders and none of the three.
        Assert.DoesNotContain("O''Brien", bound.ParameterizedText, StringComparison.Ordinal);
        Assert.DoesNotContain("1990-01-02", bound.ParameterizedText, StringComparison.Ordinal);
        Assert.Equal(3, bound.Parameters.Count);

        // THE SEAM. RecordingSqlRedactor is the ISqlRedactor double from TestDoubles.cs, and it answers
        // two questions at once: WHAT was presented, and WHAT came back.
        RecordingSqlRedactor seam = new();
        string published = seam.Redact(bound.ObservableText);

        // Presented exactly once, and VERBATIM - a seam that saw a truncated or pre-scrubbed statement
        // would be redacting something other than what the generator produced.
        Assert.Equal(1, seam.CallCount);
        Assert.Equal(bound.ObservableText, seam.LastStatement);
        Assert.True(seam.Observed(bound.ObservableText));
        Assert.Equal(1, seam.OrdinalOf(bound.ObservableText));

        // And nothing the generator interpolated survives the seam.
        Assert.Equal(RecordingSqlRedactor.MaskedMarker, published);
        Assert.DoesNotContain("O''Brien", published, StringComparison.Ordinal);
        Assert.DoesNotContain("1500.00", published, StringComparison.Ordinal);
        Assert.DoesNotContain("1990-01-02", published, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGeneratorsInterpolatedOutput_NeverLeavesTheProcessThroughADatabaseError()
    {
        // The production path, end to end: generate the interpolated statement, hand it to the database
        // error event, and read BOTH copies that leave the process - the wire payload the proxy receives
        // and the log record the sink writes. Neither may carry a literal.
        RecordingTaskLogger log = new();
        using Harness harness = new(logger: log);

        harness.Task.AddParam("name", "O'Brien");
        harness.Task.AddParam("salary", 1500.00m);

        Assert.Equal(
            RetCode.OK,
            harness.Task.CallBindParams(
                "UPDATE COMPANY SET salary = :salary WHERE name = :name",
                (long)DatabaseType.DbtMssql,
                out SqlBoundStatement bound));

        // [:L85-L98] the event's own signature carries the statement, so this is the real hand-off.
        Assert.Equal(
            3L,
            harness.Task.OnDbError(-1L, "unit-test provider text", bound.ObservableText, DwBuffer.Primary, 7L));

        // THE IN-PROCESS PAYLOAD STILL CARRIES THE LITERALS, AND THAT IS THE DESIGN RATHER THAN A LEAK.
        // Masking here would destroy the information the SQL-preview interception and the
        // characterization comparison both need, and this payload has not left the process: it travelled
        // from the worker half to the caller-side proxy, in memory. The obligation attaches to the two
        // EXITS, and both are asserted below.
        DbErrorData inProcess = Assert.Single(harness.Proxy.Errors);
        Assert.Contains("'O''Brien'", inProcess.SqlSyntax, StringComparison.Ordinal);
        Assert.Contains("1500.00", inProcess.SqlSyntax, StringComparison.Ordinal);

        // EXIT 1 - THE WIRE. The only sanctioned projection masks unconditionally and takes no policy
        // argument a call site could weaken, so every literal the generator interpolated is gone while
        // the statement's SHAPE survives and stays diagnosable.
        DbError wire = inProcess.ToDbError();
        Assert.DoesNotContain("O''Brien", wire.Sqlsyntax, StringComparison.Ordinal);
        Assert.DoesNotContain("Brien", wire.Sqlsyntax, StringComparison.Ordinal);
        Assert.DoesNotContain("1500.00", wire.Sqlsyntax, StringComparison.Ordinal);
        Assert.Contains(SqlRedactor.DefaultPlaceholder, wire.Sqlsyntax, StringComparison.Ordinal);
        Assert.StartsWith("UPDATE COMPANY SET salary = ", wire.Sqlsyntax, StringComparison.Ordinal);

        // EXIT 2 - THE LOG RECORD, which is a SECOND, INDEPENDENT copy of the same statement and is the
        // one the legacy wrote with no redaction at all.
        string record = Assert.Single(harness.Log.Records);
        Assert.DoesNotContain("O''Brien", record, StringComparison.Ordinal);
        Assert.DoesNotContain("Brien", record, StringComparison.Ordinal);
        Assert.DoesNotContain("1500.00", record, StringComparison.Ordinal);
        Assert.Contains(SqlRedactor.DefaultPlaceholder, record, StringComparison.Ordinal);

        // An attached exception would be rendered in full by every provider and would republish whatever
        // the formatted text was careful to mask, so there must not be one.
        Assert.Null(Assert.Single(harness.Log.Exceptions));
    }

    [Fact]
    public void TheRedactorIsAConstructorDependencyOfEveryTaskThatCarriesStatementText()
    {
        // C-F, STATED STRUCTURALLY. The base's own error path reaches the seam through the shared
        // singleton, which cannot be swapped out and therefore cannot be forgotten. The derived task
        // that composes statements - and so owns the statement text a caller can observe - takes the
        // seam as a CONSTRUCTOR PARAMETER, which means an instance of it cannot exist without one.
        // Asserted rather than assumed, because an optional-with-a-default spelling would compile
        // identically and silently permit a task with no redaction at all.
        ConstructorInfo[] constructors = typeof(SqlQueryTask)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotEmpty(constructors);
        Assert.All(
            constructors,
            constructor =>
            {
                ParameterInfo redactor = Assert.Single(
                    constructor.GetParameters(),
                    parameter => parameter.ParameterType == typeof(ISqlRedactor));

                // Not optional, so there is no spelling of the call that omits it.
                Assert.False(redactor.IsOptional);
                Assert.False(redactor.HasDefaultValue);
            });

        // And the singleton the base itself uses is a real ISqlRedactor rather than a bare formatter, so
        // the two paths share one contract.
        Assert.IsType<ISqlRedactor>(SqlRedactor.Instance, exactMatch: false);
    }

    // ==========================================================================================
    //  SUITE 3 - THE PER-THREAD DATASTORE CACHE  [n_cst_thread_task_sqlbase.sru:L527-L571]
    //  Including the defect this whole file exists to stop anybody from "fixing".
    // ==========================================================================================

    [Fact]
    public void GetCacheDataStore_OnAMiss_CreatesTheStoreAndSnapshotsItsDefinition()
    {
        using Harness harness = new();

        ISqlDataStore store = harness.Task.CallGetCacheDataStore(DwSqliteFixture.DataObjectName);

        // [:L558] the name is assigned, which is what resolves the definition.
        Assert.Equal(DwSqliteFixture.DataObjectName, store.DataObject);

        // [:L560] the statement snapshot comes from the resolved definition.
        Assert.Equal(DwSqliteFixture.RetrieveStatement, store.GetSqlSelect());

        // [:L568] the init event fires on the miss path too.
        Assert.Same(harness.Task, store.Carrier.ParentTask);

        // The entry really is in the thread's keyed data, under the oracle's own key [:L536].
        OrderedMap cache = Assert.IsType<OrderedMap>(harness.Host.GetData(DataStoreCacheKey));
        Assert.True(cache.Exists(DwSqliteFixture.DataObjectName));
    }

    [Fact]
    public void GetCacheDataStore_OnASecondCall_ReturnsTheSameStoreAndClearsItsState()
    {
        using Harness harness = new();

        ISqlDataStore first = harness.Task.CallGetCacheDataStore(DwSqliteFixture.DataObjectName);
        ISqlDataStore second = harness.Task.CallGetCacheDataStore(DwSqliteFixture.DataObjectName);

        // [:L540-L541] the hit path hands back the cached store itself, not a copy.
        Assert.Same(first, second);

        // The runtime resolved the definition exactly ONCE: the hit path never re-assigns the name.
        Assert.Equal(1, harness.Runtime.ResolutionCount(DwSqliteFixture.DataObjectName));

        // A different name is a separate entry.
        ISqlDataStore other = harness.Task.CallGetCacheDataStore(OtherDataObject);
        Assert.NotSame(first, other);
    }

    [Fact]
    public void GetCacheDataStore_NormalizesTheDescribeSentinelsOnTheSnapshotOnly()
    {
        // [:L562, :L564] `if ... = "!" or ... = "?" then ... = ""`. Both sentinels, on both snapshots.
        Assert.Equal(string.Empty, SqlTaskBase.NormalizeDescribeSentinel("!"));
        Assert.Equal(string.Empty, SqlTaskBase.NormalizeDescribeSentinel("?"));
        Assert.Equal(string.Empty, SqlTaskBase.NormalizeDescribeSentinel(null));

        // Anything else passes through untouched - including a value that merely CONTAINS a sentinel.
        Assert.Equal("age A ", SqlTaskBase.NormalizeDescribeSentinel("age A "));
        Assert.Equal("!!", SqlTaskBase.NormalizeDescribeSentinel("!!"));
        Assert.Equal("a?b", SqlTaskBase.NormalizeDescribeSentinel("a?b"));

        // And end to end: a definition whose sort and filter ARE sentinels caches them as empty, so the
        // hit path's comparison against a live empty describe agrees instead of firing every time.
        using Harness harness = new();
        harness.Runtime.Define(OtherDataObject, sort: "!", filter: "?");

        ISqlDataStore store = harness.Task.CallGetCacheDataStore(OtherDataObject);

        // The STORE still reports the raw sentinel values, because only the SNAPSHOT is normalised.
        Assert.Equal("!", store.Describe("DataWindow.Table.Sort"));
        Assert.Equal("?", store.Describe("DataWindow.Table.Filter"));

        // Second call: the snapshots are "" and the live values are the sentinels, so both restore
        // branches fire and install the EMPTY snapshots - which is the oracle's behaviour exactly.
        harness.Task.CallGetCacheDataStore(OtherDataObject);
        Assert.Equal(string.Empty, store.Describe("DataWindow.Table.Filter"));
    }

    [Fact]
    public void CacheHit_ChangedSort_InstallsSortAsFilter_PreservedLegacyDefect()
    {
        // =====================================================================================
        //  PRESERVED LEGACY DEFECT - n_cst_thread_task_sqlbase.sru:L545-L546
        //
        //      if cacheDS.origSort <> ds.Describe("DataWindow.Table.Sort") then   [:L545]
        //          ds.SetFilter(cacheDS.origSort)                                 [:L546]
        //
        //  :L545 compares the SORT and :L546 restores it with SetFilter - not SetSort, which the
        //  surface plainly has. UNDOCUMENTED in the legacy; found by direct source reading.
        //
        //  DO NOT "FIX" THE SUBJECT TO MAKE THIS TEST READ BETTER. C-B forbids correcting legacy
        //  defects, and this test exists precisely to fail if someone does.
        // =====================================================================================
        using Harness harness = new();

        // Cache it, so the snapshots are the fixture's sort and an empty filter.
        ISqlDataStore store = harness.Task.CallGetCacheDataStore(DwSqliteFixture.DataObjectName);
        Assert.Equal(DwSqliteFixture.SortExpression, store.Describe("DataWindow.Table.Sort"));

        // Now DRIFT the sort, as a previous execution would have.
        Assert.Equal(1L, store.SetSort(SomeSort));
        Assert.Equal(SomeSort, store.Describe("DataWindow.Table.Sort"));

        // Re-acquire: the hit path runs.
        ISqlDataStore again = harness.Task.CallGetCacheDataStore(DwSqliteFixture.DataObjectName);
        Assert.Same(store, again);

        // CONSEQUENCE 1 - THE SORT IS NEVER RESTORED. Nothing on this path calls SetSort at all, so
        // the drifted value survives untouched.
        Assert.Equal(SomeSort, again.Describe("DataWindow.Table.Sort"));
        Assert.NotEqual(DwSqliteFixture.SortExpression, again.Describe("DataWindow.Table.Sort"));

        // CONSEQUENCE 2 - THE FILTER WAS WRITTEN WITH THE SORT VALUE at :L546. Here the snapshot
        // filter is empty and differs from what :L546 installed, so :L549 corrects it afterwards -
        // which is the usual, self-healing case.
        Assert.Equal(string.Empty, again.Describe("DataWindow.Table.Filter"));

        // ... and the write really did happen: SetFilter was called TWICE on this hit, once from
        // :L546 with the SORT SNAPSHOT and once from :L549 with the filter snapshot.
        RecordingDataStore recording = Assert.IsType<RecordingDataStore>(again);
        Assert.Equal([DwSqliteFixture.SortExpression, string.Empty], recording.FilterWrites);

        // ... and SetSort was NEVER called by the subject. The only entry is this test's own drift.
        Assert.Equal([SomeSort], recording.SortWrites);
    }

    [Fact]
    public void CacheHit_WhenTheFilterSnapshotEqualsTheSort_TheFilterKeepsTheSortValue()
    {
        // THE UNMASKED CASE of the same defect [:L545-L549]. When origFilter HAPPENS TO EQUAL the sort
        // string, the comparison at :L548 is false, :L549 never runs, and the filter is LEFT HOLDING
        // THE SORT STRING that :L546 put there. Pinned so the self-healing case above cannot be
        // mistaken for the whole story.
        using Harness harness = new();

        // A definition whose FILTER and SORT are the same text.
        harness.Runtime.Define(OtherDataObject, sort: SomeSort, filter: SomeSort);

        ISqlDataStore store = harness.Task.CallGetCacheDataStore(OtherDataObject);

        // Drift the sort so :L545 fires, and drift the filter to something else so we can see what
        // the hit path leaves behind.
        Assert.Equal(1L, store.SetSort("salary D "));
        Assert.Equal(1L, store.SetFilter(SomeFilter));

        harness.Task.CallGetCacheDataStore(OtherDataObject);

        // :L546 wrote the SORT SNAPSHOT into the filter. :L548 then compared the filter snapshot -
        // which is the same text - against it, found them equal, and did nothing. The filter is left
        // holding a SORT EXPRESSION.
        Assert.Equal(SomeSort, store.Describe("DataWindow.Table.Filter"));

        RecordingDataStore recording = Assert.IsType<RecordingDataStore>(store);
        Assert.Equal([SomeFilter, SomeSort], recording.FilterWrites);
    }

    [Fact]
    public void CacheHit_ChangedStatement_RestoresTheSnapshotThroughModify()
    {
        // [:L542-L543] the statement half of the hit path, which IS correctly paired.
        using Harness harness = new();

        ISqlDataStore store = harness.Task.CallGetCacheDataStore(DwSqliteFixture.DataObjectName);
        Assert.Equal(string.Empty, store.Modify("DataWindow.Table.Select = \"SELECT 1 FROM COMPANY\""));
        Assert.Equal("SELECT 1 FROM COMPANY", store.GetSqlSelect());

        harness.Task.CallGetCacheDataStore(DwSqliteFixture.DataObjectName);

        Assert.Equal(DwSqliteFixture.RetrieveStatement, store.GetSqlSelect());
    }

    [Fact]
    public void CacheHit_ClearsCarrierState_AndTheMissPathDoesNot()
    {
        // [:L551] of_ClearState() is on the HIT path only.
        using Harness harness = new();

        ISqlDataStore store = harness.Task.CallGetCacheDataStore(DwSqliteFixture.DataObjectName);
        RecordingDataStore recording = Assert.IsType<RecordingDataStore>(store);
        Assert.Equal(0, recording.ClearStateCalls);

        harness.Task.CallGetCacheDataStore(DwSqliteFixture.DataObjectName);
        Assert.Equal(1, recording.ClearStateCalls);

        // [:L568] and OnInit fires on BOTH paths, so it has now run twice.
        Assert.Equal(2, recording.OnInitCalls);
    }

    [Fact]
    public void CreateDataStore_SelectsTheAffinityOfTheOwningThread_TheMtTrap()
    {
        // [:L553-L557]. `_mt` does NOT mean "main thread": the MAIN thread creates the plain carrier
        // and a WORKER creates the `_mt` one. A reader who guesses inverts this.
        using Harness main = new(isMainThread: true);
        Assert.Equal(CarrierThreadAffinity.MainThread, main.Task.CallCreateDataStore().Carrier.Affinity);

        using Harness worker = new(isMainThread: false);
        Assert.Equal(CarrierThreadAffinity.WorkerThread, worker.Task.CallCreateDataStore().Carrier.Affinity);
    }

    [Fact]
    public void GetCacheDataStore_EvictsAnEntryThatIsNotACacheRecord()
    {
        // Defensive repair of a state no reachable legacy path produces: without the eviction, Add
        // would refuse the existing key and the cache would stay corrupt for the thread's whole life.
        using Harness harness = new();

        OrderedMap cache = new();
        Assert.True(cache.Add(DwSqliteFixture.DataObjectName, "not a cache record"));
        harness.Host.SetData(DataStoreCacheKey, cache);

        ISqlDataStore store = harness.Task.CallGetCacheDataStore(DwSqliteFixture.DataObjectName);

        Assert.Equal(DwSqliteFixture.RetrieveStatement, store.GetSqlSelect());
        Assert.IsType<CachedDataStore>(cache.Get(DwSqliteFixture.DataObjectName));
    }

    [Fact]
    public void GetCacheDataStore_IsKeyedByDataObjectNameOverAnOrderedMap_InInsertionOrder()
    {
        // [:L527] `public function n_cst_thread_task_sqlbase_ds of_getcacheds(readonly string dataobject)`
        // and [:L536-L539] `mapCache = ... of_GetData("$SQL.DataStoreCache") ... mapCache.Exists(dataObject)`.
        // The KEY IS THE DATA-OBJECT NAME and the container is the ordered map, not a plain dictionary -
        // n_map records INSERTION ORDER and exposes it positionally [n_map.sru:L11-L12], and the port
        // keeps both properties.
        using Harness harness = new();

        ISqlDataStore fixtureStore = harness.Task.CallGetCacheDataStore(DwSqliteFixture.DataObjectName);
        ISqlDataStore otherStore = harness.Task.CallGetCacheDataStore(OtherDataObject);

        Assert.True(harness.Host.HasData(DataStoreCacheKey));
        OrderedMap cache = Assert.IsType<OrderedMap>(harness.Host.GetData(DataStoreCacheKey));

        // Keyed BY NAME - present for the two names asked for, absent for one never asked for.
        Assert.Equal(2UL, cache.Count());
        Assert.True(cache.Exists(DwSqliteFixture.DataObjectName));
        Assert.True(cache.Exists(OtherDataObject));
        Assert.False(cache.Exists("dw_never_requested"));

        // ORDERED, and ONE-BASED, exactly as n_map reports it. A dictionary would satisfy the lookups
        // above and answer nothing here.
        Assert.Equal(DwSqliteFixture.DataObjectName, cache.GetKey(1));
        Assert.Equal(OtherDataObject, cache.GetKey(2));
        Assert.Equal(string.Empty, cache.GetKey(0));
        Assert.Equal(string.Empty, cache.GetKey(3));

        // The positional read reaches the SAME entries as the keyed read.
        Assert.Same(cache.Get(DwSqliteFixture.DataObjectName), cache.Get(1));
        Assert.Same(cache.Get(OtherDataObject), cache.Get(2));

        // [:L561] the entry holds the STORE ITSELF, so the handed-out store and the cached one are one
        // object rather than two views of the same definition.
        CachedDataStore fixtureEntry = Assert.IsType<CachedDataStore>(
            cache.Get(DwSqliteFixture.DataObjectName));
        Assert.Same(fixtureStore, fixtureEntry.DataStore);

        CachedDataStore otherEntry = Assert.IsType<CachedDataStore>(cache.Get(OtherDataObject));
        Assert.Same(otherStore, otherEntry.DataStore);
        Assert.NotSame(fixtureEntry.DataStore, otherEntry.DataStore);

        // [:L560, :L562, :L564] the three snapshots are the definition as it stood at insertion, with the
        // two describe sentinels already normalised.
        Assert.Equal(DwSqliteFixture.RetrieveStatement, fixtureEntry.OrigSql);
        Assert.Equal(DwSqliteFixture.SortExpression, fixtureEntry.OrigSort);
        Assert.Equal(string.Empty, fixtureEntry.OrigFilter);
    }

    [Fact]
    public void GetCacheDataStore_HoldsLiveCarrierReferences_NotCopiesOfCarrierState()
    {
        // WHY THIS IS A SEPARATE ASSERTION FROM "the same store". A cache that handed back an equal but
        // distinct carrier would still pass every Same() check on the STORE while silently giving each
        // caller its own buffers - and the whole reason the legacy caches at all is that the datastore,
        // its buffers and its item statuses are the state being reused [:L540-L551]. The legacy holds a
        // POINTER in `cacheDS.ds`, so the port must hold a live reference too.
        using Harness harness = new();

        ISqlDataStore first = harness.Task.CallGetCacheDataStore(DwSqliteFixture.DataObjectName);
        DataWindowCarrier firstCarrier = first.Carrier;

        OrderedMap cache = Assert.IsType<OrderedMap>(harness.Host.GetData(DataStoreCacheKey));
        CachedDataStore entry = Assert.IsType<CachedDataStore>(
            cache.Get(DwSqliteFixture.DataObjectName));

        // The cached entry's store and the handed-out store share ONE carrier instance.
        Assert.Same(firstCarrier, entry.DataStore.Carrier);

        // And so does the store the HIT path hands back on the next call.
        ISqlDataStore second = harness.Task.CallGetCacheDataStore(DwSqliteFixture.DataObjectName);
        Assert.Same(first, second);
        Assert.Same(firstCarrier, second.Carrier);

        // Mutation through one handle is visible through the other, which is what "live reference"
        // means and what a defensive copy would break. The init event re-runs on every call [:L568], so
        // the carrier's parent association is re-established rather than duplicated.
        Assert.Same(harness.Task, firstCarrier.ParentTask);
        Assert.Same(harness.Task, second.Carrier.ParentTask);

        // A DIFFERENT name gets its own carrier - the sharing is per key, never global.
        ISqlDataStore other = harness.Task.CallGetCacheDataStore(OtherDataObject);
        Assert.NotSame(firstCarrier, other.Carrier);
    }

    // ==========================================================================================
    //  SUITE 4 - THE NESTED DBParm PARSE  [n_cst_thread_task_sqlbase.sru:L127-L132]
    // ==========================================================================================

    [Fact]
    public void SetTransData_NCharBindAlone_DoesNotEnableNCharBinding()
    {
        // THE NESTING IS CONTRACT (C-B). The oracle consults NCharBind ONLY INSIDE the DisableBind=1
        // branch [:L128-L130], so NCharBind=1 on its own is legal and completely INERT. A flat
        // conjunction computes the same answer while hiding that the second flag is subordinate.
        using Harness harness = new();

        Assert.Equal(RetCode.OK, harness.Task.SetTransData(Descriptor("NCharBind=1")));

        Assert.False(harness.Task.IsNCharBindingEnabled());
        Assert.False(harness.Task.IsNCharBinding);
    }

    [Fact]
    public void SetTransData_BothFlags_EnablesNCharBinding()
    {
        // [:L128-L130] the only arm that sets it true.
        using Harness harness = new();

        Assert.Equal(RetCode.OK, harness.Task.SetTransData(Descriptor("DisableBind=1;NCharBind=1")));

        Assert.True(harness.Task.IsNCharBindingEnabled());
        Assert.True(harness.Task.IsNCharBinding);
    }

    [Fact]
    public void SetTransData_DisableBindAlone_LeavesNCharBindingOff()
    {
        // [:L127] the default is established BEFORE any test, so this path leaves it false.
        using Harness harness = new();

        Assert.Equal(RetCode.OK, harness.Task.SetTransData(Descriptor("DisableBind=1")));

        Assert.False(harness.Task.IsNCharBindingEnabled());
    }

    /// <summary>
    /// The complete <c>DBParm</c> flag matrix: the four combinations of the two flags, the spellings the
    /// oracle's own patterns admit, and the values they refuse.
    /// </summary>
    /// <returns>The <c>DBParm</c> string, and the national-character flag it must resolve to.</returns>
    /// <remarks>
    /// <para>
    /// <b>THE ORACLE'S CONDITION IS NESTED, AND THE NESTING IS THE CONTRACT (constraint C-B)</b>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L127-L132</c>]:
    /// </para>
    /// <code>
    /// _bNCharBinding = false                                                              [:L127]
    /// if RegExpFind(_transData.DBParm,"DisableBind\s*=\s*(0|1)",2,true) = "1" then        [:L128]
    ///     if RegExpFind(_transData.DBParm,"NCharBind\s*=\s*(0|1)",2,true) = "1" then      [:L129]
    ///         _bNCharBinding = true                                                       [:L130]
    /// </code>
    /// <para>
    /// The row that matters most is <c>DisableBind=0</c> with <c>NCharBind=1</c>. A port that treats the
    /// two flags as INDEPENDENT answers <see langword="true"/> there and passes every other row in this
    /// matrix, which is precisely why the row is here: <c>NCharBind=1</c> on its own is legal and
    /// COMPLETELY INERT, and honouring it would change the generated statement for every caller who set
    /// it without the first flag.
    /// </para>
    /// <para>
    /// <b>WHY THE FIRST FLAG MATTERS AT ALL, AND IT IS A SECURITY FACT (AAP 0.2.1.4, 0.6.4; C-K).</b>
    /// <c>DisableBind=1</c> means PowerBuilder DOES NOT USE BIND VARIABLES - parameter values are
    /// INTERPOLATED INTO THE STATEMENT TEXT AS LITERALS, which is the mechanical root of the
    /// SQL-injection exposure AAP 0.6.4 analyses and the reason the literal generator exists at all. The
    /// .NET implementation parameterizes INTERNALLY while preserving the observable generated statement
    /// byte for byte, and redacts the statement before any diagnostic leaves the process; this matrix
    /// pins the FLAG PARSE only, and asserts nothing about how the statement is executed. <b>The
    /// exposure is DOCUMENTED, not corrected (constraint C-B).</b>
    /// </para>
    /// <para>
    /// <b>Every string below is synthetic (constraint C-F).</b> None carries a password, an account, a
    /// host or anything copied from the legacy tree or from a catalogued in-source secret site; the
    /// realistic multi-parameter row is built from <c>unit-test-</c> sentinels and deliberately omits any
    /// credential-shaped parameter entirely.
    /// </para>
    /// </remarks>
    public static TheoryData<string, bool> NCharBindingMatrix() => new()
    {
        // --- the four combinations of the nested condition -------------------------------------
        // BOTH set: [:L128] passes, [:L129] passes, [:L130] fires. The ONLY true arm.
        { "DisableBind=1;NCharBind=1", true },

        // Bind disabled but national-character binding off: [:L128] passes, [:L129] fails.
        { "DisableBind=1;NCharBind=0", false },

        // ⚠ THE ROW AN INDEPENDENT-FLAGS PORT GETS WRONG. [:L128] fails, so [:L129] IS NEVER REACHED
        // and NCharBind=1 is inert. A flat `disableBind && nCharBind` computes the same answer here by
        // coincidence; a port that honours NCharBind on its own does not.
        { "DisableBind=0;NCharBind=1", false },

        // NEITHER present: [:L127] alone decides, and it decides false.
        { "", false },

        // --- the spellings the oracle's patterns admit -----------------------------------------
        // The fourth RegExpFind argument is `true` - IGNORE CASE - so every casing matches [:L128-L129].
        { "disablebind=1;ncharbind=1", true },
        { "DISABLEBIND=1;NCHARBIND=1", true },
        { "DiSaBlEbInD=1;nChArBiNd=1", true },

        // `\s*` on BOTH sides of the `=`, so any run of whitespace - including none - is tolerated.
        { "DisableBind = 1;NCharBind = 1", true },
        { "DisableBind  =  1;NCharBind  =  1", true },
        { "disablebind =1;NCharBind= 1", true },

        // A tab is whitespace to `\s*` just as a space is.
        { "DisableBind\t=\t1;NCharBind\t=\t1", true },

        // Casing and whitespace together, which is how a hand-edited connection string actually looks.
        { "  DISABLEBIND   =   1  ;  ncharbind   =   1  ", true },

        // --- a REALISTIC multi-parameter DBParm string, not a flag in isolation ----------------
        // The flags are found by SEARCH rather than by position, so neighbouring parameters, quoted
        // values and a trailing parameter are all irrelevant. Note StaticBind - a DIFFERENT parameter
        // whose name ends in the same word - is not mistaken for either flag.
        {
            "Provider='unit-test-provider';DisableBind=1;StaticBind=0;NCharBind=1;DelimitIdentifier='No'",
            true
        },
        {
            "Provider='unit-test-provider';StaticBind=1;NCharBind=1;DelimitIdentifier='No'",
            false
        },
        {
            "ConnectString='DSN=unit-test-dsn';DisableBind = 1;NCharBind = 0;CommitOnDisconnect='No'",
            false
        },

        // --- only 0 and 1 are recognised, because the capture group is literally `(0|1)` --------
        // `DisableBind=2` produces NO MATCH, so the captured text is not "1" and [:L128] falls through.
        { "DisableBind=2;NCharBind=1", false },
        { "DisableBind=1;NCharBind=2", false },
        { "DisableBind=yes;NCharBind=yes", false },
        { "DisableBind=;NCharBind=", false },

        // ⚠ PRESERVED QUIRK, NOT A DEFECT TO FIX (constraint C-B). The patterns are UNANCHORED on the
        // right, so `=10` matches the LEADING `1` and the flag reads as set - exactly as the oracle's own
        // RegExpFind does with the same pattern. Anchoring the patterns would change the answer for this
        // input, which is a behavioural change dressed as a correction.
        { "DisableBind=10;NCharBind=10", true },
    };

    [Theory]
    [MemberData(nameof(NCharBindingMatrix))]
    public void SetTransData_ResolvesTheNestedFlagPair_AcrossTheWholeMatrix(string dbParm, bool expected)
    {
        using Harness harness = new();

        Assert.Equal(RetCode.OK, harness.Task.SetTransData(Descriptor(dbParm)));

        // The task's two spellings of one backing field [:L524] - both must agree, because the carrier
        // reaches the same fact through the parent-task contract while derived tasks call the function.
        Assert.Equal(expected, harness.Task.IsNCharBindingEnabled());
        Assert.Equal(expected, harness.Task.IsNCharBinding);

        // And the descriptor resolves it identically on its own, which is what proves there is ONE parse
        // rather than two that can drift apart.
        Assert.Equal(expected, harness.Task.GetTransData().IsNCharBindingEnabled);
    }

    [Theory]
    [MemberData(nameof(NCharBindingMatrix))]
    public void TheDescriptorsFlagPairIsResolvedInOnePass_AndTheNestingIsVisibleInIt(
        string dbParm,
        bool expected)
    {
        // The same matrix through the ONE-PASS resolver the task actually calls, so the two out-parameters
        // can be read together. Reading them together is what makes the SUBORDINATION observable: the
        // second is false whenever the first is, no matter what the string says.
        TransactionData descriptor = Descriptor(dbParm);

        descriptor.ResolveDbParmFlags(out bool isBindDisabled, out bool isNCharBindingEnabled);

        Assert.Equal(expected, isNCharBindingEnabled);
        Assert.Equal(descriptor.IsBindDisabled, isBindDisabled);

        // [:L129] is INSIDE [:L128], so the national-character flag can never outrun the bind flag.
        if (isNCharBindingEnabled)
        {
            Assert.True(isBindDisabled);
        }
    }

    [Fact]
    public void TheDbParmFlagsAreParsedOnceByTheDescriptor_NotAgainByTheTask()
    {
        // [:L127-L132] IS ONE SITE IN THE ORACLE, AND IT IS ONE SITE HERE. A second parser - a private
        // regex on the task, say - would compute the same answer today and be free to drift tomorrow,
        // and the flat-conjunction mistake would then only have to be made in one of the two copies.
        // Asserted structurally, because no behavioural test can distinguish one parse from two that
        // currently agree.
        MethodInfo[] taskRegexMembers = [.. typeof(SqlTaskBase)
            .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(static method => method.ReturnType == typeof(Regex))];

        Assert.Empty(taskRegexMembers);

        Assert.DoesNotContain(
            typeof(SqlTaskBase)
                .GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly),
            static field => field.FieldType == typeof(Regex));

        // The descriptor owns exactly TWO patterns - one per flag - and no more.
        MethodInfo[] patterns = [.. typeof(TransactionData)
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(static method => method.ReturnType == typeof(Regex))];

        Assert.Equal(2, patterns.Length);
    }

    [Fact]
    public void TheDbParmPatternsAreTheOraclesOwnTextAndAreCaseInsensitive()
    {
        // BYTE-EXACT PATTERN PARITY with [:L128-L129]. The pattern text is the specification here, so it
        // is compared as text rather than inferred from behaviour: a pattern that had been "tidied" -
        // `\s*=\s*` widened to `\s*=\s*\d`, or the group turned into `([01])` - would still pass the
        // behavioural matrix above for every input in it while accepting inputs the oracle refuses.
        IReadOnlyDictionary<string, string> expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DisableBind"] = @"DisableBind\s*=\s*(0|1)",
            ["NCharBind"] = @"NCharBind\s*=\s*(0|1)",
        };

        MethodInfo[] patterns = [.. typeof(TransactionData)
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(static method => method.ReturnType == typeof(Regex))];

        Assert.Equal(expected.Count, patterns.Length);

        List<string> observed = [];
        foreach (MethodInfo pattern in patterns)
        {
            // exactMatch: false because a source-generated pattern is a SUBCLASS of Regex - the generated
            // type lives in this assembly while the engine it inherits is the base class library's.
            Regex compiled = Assert.IsType<Regex>(pattern.Invoke(null, null), exactMatch: false);

            observed.Add(compiled.ToString());

            // The fourth RegExpFind argument, `true`, is IGNORE CASE - and it is the reason every casing
            // row in the matrix above passes.
            Assert.True(compiled.Options.HasFlag(RegexOptions.IgnoreCase));

            // Exactly one capture group: the `(0|1)` the oracle reads at its index 2.
            Assert.Equal(1, compiled.GetGroupNumbers().Length - 1);
        }

        Assert.Equal(
            expected.Values.Order(StringComparer.Ordinal),
            observed.Order(StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    [Fact]
    public void TheDbParmParseUsesOnlyTheBaseClassLibrarysRegularExpressions()
    {
        // ============================================================================================
        //  CONSTRAINT C-D - AND THIS IS THE FILE'S ONLY DEFERRED-CAPABILITY QUESTION.
        //  AAP 0.2.1.4 records `regexpfind` as NOT a library dependency: its single in-scope use is
        //  this DBParm parse [n_cst_thread_task_sqlbase.sru:L128-L129], and that use is satisfied by
        //  System.Text.RegularExpressions. The deferred `pfw.utility.regexp` library therefore has NO
        //  in-scope consumer, which is why it stays wholly assigned to Documents. Asserted here so a
        //  later "shared regex helper" cannot quietly reintroduce the coupling the mapping removed.
        // ============================================================================================
        Assembly persistence = typeof(SqlTaskBase).Assembly;

        // The regular-expression type itself is the BCL's, from the BCL's own assembly.
        Assert.Equal("System.Text.RegularExpressions", typeof(Regex).Assembly.GetName().Name);

        // The source-generated patterns DERIVE from that BCL type, so the engine doing the matching is
        // the BCL's engine even though the generated subclass lives in this assembly.
        MethodInfo[] patterns = [.. typeof(TransactionData)
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(static method => method.ReturnType == typeof(Regex))];

        Assert.NotEmpty(patterns);
        Assert.All(
            patterns,
            pattern => Assert.IsType<Regex>(pattern.Invoke(null, null), exactMatch: false));

        // NOTHING NAMED FOR A DEFERRED SERVICE IS REFERENCED. The four deferred capability areas are
        // DesignSystem, Documents, Integration and ScriptBridge; none is built in this phase, so no
        // assembly named for one can exist to be referenced, and the regexp library that would have
        // carried this parse belongs to Documents.
        string[] deferred = ["DesignSystem", "Documents", "Integration", "ScriptBridge", "Regexp"];
        IEnumerable<string> referenced = persistence
            .GetReferencedAssemblies()
            .Select(static reference => reference.Name ?? string.Empty);

        Assert.All(
            referenced,
            name => Assert.All(
                deferred,
                capability => Assert.DoesNotContain(capability, name, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void SetTransData_ErasesAutoCommit_AndItsEqualityCheckIsAsymmetric()
    {
        // [:L118-L119] 擦除连接目标无关的参数 - auto-commit is a session behaviour and not part of a
        // connection's identity, so it is erased from the STORED descriptor.
        using Harness harness = new();
        TransactionData incoming = Descriptor("DisableBind=1") with { AutoCommit = true };

        Assert.Equal(RetCode.OK, harness.Task.SetTransData(incoming));
        Assert.False(harness.Task.GetTransData().AutoCommit);

        // Borrow a transaction, so the reference the body drops at [:L121-L125] is observable.
        IPooledTransaction? transaction = null;
        Assert.Equal(RetCode.OK, harness.Task.CallGetTransObject(ref transaction));
        Assert.True(harness.Task.HasTransactionReference);

        // PRESERVED ASYMMETRY [:L114]. The oracle compares the STORED (already erased) descriptor
        // against the INCOMING one, so passing the SAME descriptor again does NOT short-circuit while
        // its auto-commit flag is still set - the two differ by exactly that member, so the body runs
        // and the reference is dropped.
        Assert.Equal(RetCode.OK, harness.Task.SetTransData(incoming));
        Assert.False(harness.Task.HasTransactionReference);

        // Whereas an ALREADY-ERASED descriptor short-circuits at [:L114] and the reference survives.
        Assert.Equal(RetCode.OK, harness.Task.CallGetTransObject(ref transaction));
        Assert.True(harness.Task.HasTransactionReference);
        Assert.Equal(RetCode.OK, harness.Task.SetTransData(incoming with { AutoCommit = false }));
        Assert.True(harness.Task.HasTransactionReference);
    }

    [Fact]
    public void SetTransData_WithADifferentDescriptor_DropsTheReferenceAndReborrows()
    {
        // [:L121-L125] hazard 2: a descriptor change invalidates the borrowed transaction, and the
        // reference is cleared DETERMINISTICALLY rather than left to the collector.
        using Harness harness = new();
        harness.Task.SetTransData(Descriptor("DisableBind=1"));

        IPooledTransaction? transaction = null;
        Assert.Equal(RetCode.OK, harness.Task.CallGetTransObject(ref transaction));
        Assert.True(harness.Task.HasAliveTrans());
        Assert.Equal(1, harness.Activator.CreatedCount);

        harness.Task.SetTransData(Descriptor("DisableBind=0"));

        Assert.False(harness.Task.HasTransactionReference);
        Assert.False(harness.Task.HasAliveTrans());

        // A fresh acquisition for the new descriptor is a NEW pool entry and therefore a new transaction.
        Assert.Equal(RetCode.OK, harness.Task.CallGetTransObject(ref transaction));
        Assert.Equal(2, harness.Activator.CreatedCount);
    }

    // ==========================================================================================
    //  SUITE 5 - THE RETRIEVAL HOOK  [n_cst_thread_task_sqlbase_hook.sru, sqlquery:L755-L761]
    // ==========================================================================================

    [Theory]
    [InlineData(null)]
    [InlineData(RetCode.E_NO_IMPLEMENTATION)]
    public void HookDeclinedRetrieval_NullOrNoImplementation_MeansRunTheDefault(long? hookResult) =>
        // [sqlquery:L761] `if IsNull(nRowCnt) or nRowCnt = RetCode.E_NO_IMPLEMENTATION then` -> DEFAULT.
        // E_NO_IMPLEMENTATION IS NOT A FAILURE HERE, and testing it with IsFailed would say otherwise.
        Assert.True(SqlTaskBase.HookDeclinedRetrieval(hookResult));

    [Theory]
    [InlineData(0L)]
    [InlineData(42L)]
    [InlineData(-1L)]
    [InlineData(RetCode.E_DB_ERROR)]
    public void HookDeclinedRetrieval_AnyOtherAnswer_MeansTheHookHandledIt(long hookResult) =>
        Assert.False(SqlTaskBase.HookDeclinedRetrieval(hookResult));

    [Fact]
    public void HookDeclinedRetrieval_IsNotTheSameQuestionAsIsFailed()
    {
        // The trap this predicate exists to close: a declining hook LOOKS like a failure to the
        // return-code algebra, and is not one.
        Assert.True(Predicates.IsFailed(RetCode.E_NO_IMPLEMENTATION));
        Assert.True(SqlTaskBase.HookDeclinedRetrieval(RetCode.E_NO_IMPLEMENTATION));
    }

    [Fact]
    public void ResolveRetrievalHook_OnlyActivatesARegisteredClassName()
    {
        // C-G. The class name is caller-controlled input republished as QuerySpec.hook_class, so the
        // activator is an ALLOWLIST and an unknown name degrades to "no hook" rather than throwing.
        using Harness harness = new();

        Assert.Null(harness.Task.CallResolveRetrievalHook(null));
        Assert.Null(harness.Task.CallResolveRetrievalHook(string.Empty));
        Assert.Null(harness.Task.CallResolveRetrievalHook("   "));
        Assert.Null(harness.Task.CallResolveRetrievalHook("System.Object"));
        Assert.Null(harness.Task.CallResolveRetrievalHook("n_cst_thread_task_sqlbase_hook"));

        Assert.Equal(RetCode.OK, harness.HookActivator.Register("my_hook", () => new DecliningHook()));
        Assert.True(harness.HookActivator.IsRegistered("my_hook"));
        Assert.NotNull(harness.Task.CallResolveRetrievalHook("my_hook"));

        // Registration is refused rather than overwritten, so a later registration cannot displace an
        // earlier one, and a blank name is rejected outright.
        Assert.Equal(RetCode.E_BUSY, harness.HookActivator.Register("my_hook", () => new DecliningHook()));
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, harness.HookActivator.Register(" ", () => new DecliningHook()));
    }

    [Fact]
    public void ISqlRetrievalHookIsASingleMethodContract_ShapedLikeTheOraclesOneEvent()
    {
        // ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_hook.sru is TWENTY-TWO LINES and
        // declares exactly ONE member:
        //
        //   event type long onretrieve(
        //       n_cst_thread_task_sqlbase task, n_cst_thread_trans transobject, datastore data)
        //
        // The contract's SHAPE is the contract - a hook is an extension point a deployment implements, so
        // any member added here becomes a new obligation on every implementor and any parameter dropped
        // takes information away from all of them. Asserted structurally because no behavioural test can
        // notice a second member that nothing calls yet.
        MethodInfo onRetrieve = Assert.Single(typeof(ISqlRetrievalHook).GetMethods());

        Assert.Equal(nameof(ISqlRetrievalHook.OnRetrieve), onRetrieve.Name);

        // `event type long` - the oracle's return is a long, and it carries a ROW COUNT on the handled
        // arm rather than a return code, which is why it is not narrowed to an enum.
        Assert.Equal(typeof(long), onRetrieve.ReturnType);

        // The three parameters, in the oracle's own order: task, transaction, carrier.
        ParameterInfo[] parameters = onRetrieve.GetParameters();
        Assert.Equal(3, parameters.Length);
        Assert.Equal(typeof(SqlTaskBase), parameters[0].ParameterType);
        Assert.Equal(typeof(IPooledTransaction), parameters[1].ParameterType);
        Assert.Equal(typeof(DataWindowCarrier), parameters[2].ParameterType);

        // None of the three is optional, so no implementor can be handed fewer than the oracle hands.
        Assert.All(parameters, parameter => Assert.False(parameter.IsOptional));

        // ONE member means one METHOD and nothing else - no property, no event, no nested contract.
        Assert.Empty(typeof(ISqlRetrievalHook).GetProperties());
        Assert.Empty(typeof(ISqlRetrievalHook).GetEvents());
        Assert.Empty(typeof(ISqlRetrievalHook).GetInterfaces());
    }

    /// <summary>
    /// Every answer a hook can give, and whether it means "not handled - run the default retrieval".
    /// </summary>
    /// <returns>The hook's answer, and the expected decline verdict.</returns>
    /// <remarks>
    /// <para>
    /// The port of the oracle's two-arm test
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L761</c>]:
    /// </para>
    /// <code>
    /// if IsNull(nRowCnt) or nRowCnt = RetCode.E_NO_IMPLEMENTATION then  ... default retrieval ...
    /// </code>
    /// <para>
    /// <b>EXACTLY TWO VALUES DECLINE, AND EVERY OTHER VALUE - INCLUDING EVERY FAILURE CODE - MEANS THE
    /// HOOK HANDLED IT.</b> The trap the matrix closes is the neighbouring constant:
    /// <see cref="RetCode.E_NO_SUPPORT"/> is <c>-2000</c> and
    /// <see cref="RetCode.E_NO_IMPLEMENTATION"/> is <c>-2001</c>, they read almost identically, and only
    /// the second declines. Treating both as a decline would silently run the default retrieval after a
    /// hook that reported it could not support the request at all.
    /// </para>
    /// </remarks>
    public static TheoryData<long?, bool> HookAnswers() => new()
    {
        // --- the two declining answers -----------------------------------------------------------
        // `IsNull(nRowCnt)` - no hook ran, which is the SetNull arm at [sqlquery:L759].
        { null, true },

        // The one sanctioned decline value.
        { RetCode.E_NO_IMPLEMENTATION, true },

        // --- every other answer means HANDLED ----------------------------------------------------
        // A ROW COUNT, which is what a hook that really retrieved returns. Note there is no row-count
        // row for 0 or 1 separate from the two constants below: 0 IS RetCode.OK and 1 IS RetCode.PREVENT,
        // one value each, and that collision is itself the reason the decline test compares against ONE
        // named constant rather than reasoning about sign or success.
        { 42L, false },

        // Zero rows retrieved - a legitimate result and emphatically not a decline - which is the same
        // value as the success constant.
        { RetCode.OK, false },

        // One row retrieved, which is the same value as the veto constant.
        { RetCode.PREVENT, false },

        // Failure codes: a hook that FAILED still handled the request, so the default must NOT run and
        // silently overwrite its outcome.
        { RetCode.FAILED, false },
        { RetCode.CANCELLED, false },
        { RetCode.E_DB_ERROR, false },
        { RetCode.E_INVALID_TRANSACTION, false },

        // ⚠ THE NEIGHBOURING CONSTANT. -2000, one away from the decline value, and NOT a decline.
        { RetCode.E_NO_SUPPORT, false },
        { RetCode.UNKNOWN, false },
    };

    [Theory]
    [MemberData(nameof(HookAnswers))]
    public void HookDeclinedRetrieval_AnswersTheOraclesTwoArmTest(long? hookResult, bool expectedDecline)
    {
        Assert.Equal(expectedDecline, SqlTaskBase.HookDeclinedRetrieval(hookResult));

        // AND IT IS NOT THE SAME QUESTION AS "did it fail". The return-code algebra answers true for
        // E_NO_IMPLEMENTATION and for every other negative code alike, so a port that reached for
        // IsFailed here would turn every failure into a decline and re-run the retrieval over it.
        if (hookResult is { } answer && Predicates.IsFailed(answer) && answer != RetCode.E_NO_IMPLEMENTATION)
        {
            Assert.False(SqlTaskBase.HookDeclinedRetrieval(answer));
        }
    }

    [Fact]
    public void AnUnresolvableHookClassName_IsRefusedWithADocumentedCode_NeverAnUnstructuredException()
    {
        // ============================================================================================
        //  A HOOK CLASS NAME IS CALLER-CONTROLLED INPUT. The oracle writes `hook = Create Using
        //  _sHookClass` [sqlquery:L516-L517] where the name arrives through the public setter
        //  of_sethookclass [:L428], which contract C-05 republishes as QuerySpec.hook_class. Activating
        //  an arbitrary type from that string would be a remote type-activation primitive, so the port
        //  resolves against an ALLOWLIST and refuses at the SETTER - which is the only place a caller can
        //  still act on the refusal.
        // ============================================================================================
        using Harness harness = new();

        // BLANK IS ADMISSIBLE AND MUST STAY SO (C-B). The oracle's own guard is
        // `if _sHookClass <> "" then` [sqlquery:L516], so blank means "no hook" - the ordinary case, not
        // a name at all - and refusing it would reject every caller that simply does not want one.
        Assert.True(harness.Task.CallIsAdmissibleHookClass(null));
        Assert.True(harness.Task.CallIsAdmissibleHookClass(string.Empty));
        Assert.True(harness.Task.CallIsAdmissibleHookClass("   "));

        // AN UNREGISTERED NAME IS NOT ADMISSIBLE. Without this answer the setter had no way to ask, so an
        // unsanctioned name was stored, ignored at retrieval, and the retrieval then ran with NO hook -
        // which reads to the caller as "your hook ran and did nothing" rather than "your hook was
        // rejected".
        Assert.False(harness.Task.CallIsAdmissibleHookClass("n_cst_thread_task_sqlbase_hook"));
        Assert.False(harness.Task.CallIsAdmissibleHookClass("System.Object"));
        Assert.False(harness.Task.CallIsAdmissibleHookClass("PowerFramework.Persistence.Tasks.SqlQueryTask"));

        // NO UNSTRUCTURED EXCEPTION ON ANY OF THOSE PATHS. Resolution answers null - the legacy state in
        // which IsValid(hook) is false - and the admission test answers false; neither throws, and a
        // thrown type-load or missing-method exception reaching a request path is exactly what an
        // allowlist exists to prevent.
        Assert.Null(Record.Exception(() => harness.Task.CallResolveRetrievalHook("System.Object")));
        Assert.Null(Record.Exception(() => harness.Task.CallResolveRetrievalHook("no.such.type, no.such.assembly")));
        Assert.Null(Record.Exception(() => harness.Task.CallIsAdmissibleHookClass("System.Object")));
        Assert.Null(harness.Task.CallResolveRetrievalHook("System.Object"));

        // THE DOCUMENTED CODE. A name that fails admission is refused with E_INVALID_ARGUMENT at the
        // setter the contract publishes, and the same code answers a blank registration attempt - so the
        // refusal a caller sees is an argument error it can act on.
        Assert.Equal(
            RetCode.E_INVALID_ARGUMENT,
            harness.HookActivator.Register(string.Empty, static () => new DecliningHook()));
        Assert.Equal(
            RetCode.E_INVALID_ARGUMENT,
            harness.HookActivator.Register("   ", static () => new DecliningHook()));

        // ... and once the deployment SANCTIONS the name, the very same string resolves. The caller
        // selects among hooks the deployment already registered; it never names a type directly.
        Assert.Equal(RetCode.OK, harness.HookActivator.Register("sanctioned_hook", static () => new DecliningHook()));
        Assert.True(harness.Task.CallIsAdmissibleHookClass("sanctioned_hook"));
        ISqlRetrievalHook resolved = Assert.IsType<DecliningHook>(
            harness.Task.CallResolveRetrievalHook("sanctioned_hook"));

        // A REGISTERED, RESOLVED HOOK THAT DECLINES STILL MEANS "run the default" - the two halves of the
        // contract meeting, which is the whole point of resolving one.
        Assert.True(
            SqlTaskBase.HookDeclinedRetrieval(
                resolved.OnRetrieve(harness.Task, new FakePooledTransaction(), harness.Task.CallCreateDataStore().Carrier)));

        // The allowlist matches ORDINALLY - a PowerBuilder class name is a fixed token, and a
        // case-insensitive match would widen the allowlist for no behavioural gain.
        Assert.False(harness.Task.CallIsAdmissibleHookClass("SANCTIONED_HOOK"));
        Assert.Null(harness.Task.CallResolveRetrievalHook("Sanctioned_Hook"));
    }

    [Fact]
    public void ReleaseRetrievalHook_DisposesADisposableHookAndClearsTheReference()
    {
        // [sqlquery:L873] `if IsValid(hook) then Destroy hook`, deterministically - hazard 2.
        DisposableHook hook = new();
        ISqlRetrievalHook? reference = hook;

        TestSqlTask.CallReleaseRetrievalHook(ref reference);

        Assert.True(hook.Disposed);
        Assert.Null(reference);

        // A hook with nothing to release is handled just as safely.
        ISqlRetrievalHook? plain = new DecliningHook();
        TestSqlTask.CallReleaseRetrievalHook(ref plain);
        Assert.Null(plain);

        ISqlRetrievalHook? none = null;
        TestSqlTask.CallReleaseRetrievalHook(ref none);
        Assert.Null(none);
    }

    // ==========================================================================================
    //  SUITE 6 - THE COMMIT SIGNAL AND ITS BACKWARD PROPAGATION
    //  [n_cst_thread_task_sqlbase.sru:L100-L111, :L224-L249]
    // ==========================================================================================

    [Fact]
    public void GetCommitEvent_IsManualResetAndInitiallyUnsignalled()
    {
        // [:L225] `CreateEvent(0, true, false, 0)` - argument 3 is bManualReset, argument 4 is
        // bInitialState. Both are contract: manual reset keeps a commit observable until Reset clears
        // it, and the initial state makes "not committed" the starting truth.
        using Harness harness = new();

        Assert.False(harness.Task.IsCommitted());

        ManualResetEventSlim signal = harness.Task.GetCommitEvent();
        Assert.False(signal.IsSet);

        // [:L224] created lazily, and the same instance every time.
        Assert.Same(signal, harness.Task.GetCommitEvent());

        signal.Set();

        // Manual reset: it STAYS set, and a second observer still sees it.
        Assert.True(harness.Task.IsCommitted());
        Assert.True(harness.Task.IsCommitted());

        // [:L242-L244] only Reset clears it.
        Assert.Equal(RetCode.OK, harness.Task.Reset());
        Assert.False(harness.Task.IsCommitted());
    }

    [Fact]
    public void IsCommitted_DoesNotCreateTheSignalItObserves()
    {
        // An observer must not change the state it observes; the legacy proxy reads a handle it was
        // GIVEN [n_cst_threading_task_sqlbase.sru:L218] rather than asking for one.
        using Harness harness = new();

        Assert.False(harness.Task.IsCommitted());
        Assert.False(harness.Task.HasCommitSignal);
    }

    [Fact]
    public void Reset_AlsoResetsTheParameters_AndToleratesAnAbsentSignal()
    {
        // [:L242-L248] two actions in the oracle's order, neither of which creates the signal.
        using Harness harness = new();
        harness.Task.AddParam("a", 1L);

        Assert.Equal(RetCode.OK, harness.Task.Reset());

        Assert.Equal(0, harness.Task.GetParamCount());
        Assert.False(harness.Task.HasCommitSignal);
    }

    [Fact]
    public void Commit_WithoutAnAttachedTransaction_ReportsInvalidTransaction()
    {
        // [:L232] and [:L219] - both guard on validity rather than throwing, which matters because
        // OnError calls Rollback unconditionally.
        using Harness harness = new();

        Assert.Equal(RetCode.E_INVALID_TRANSACTION, harness.Task.Commit(autoRollback: true));
        Assert.Equal(RetCode.E_INVALID_TRANSACTION, harness.Task.Rollback());
        Assert.False(harness.Task.IsCommitted());
    }

    [Fact]
    public void Commit_OnSuccess_SignalsThisTaskAndEveryPrecedingOne_Backward()
    {
        // [:L100-L111] `for nIndex = of_GetIndex() to 1 step -1`, under the oracle's own comment
        // 设置当前以及前面所有任务的提交信号. THE DIRECTION IS DELIBERATE and must not be "tidied".
        using Harness harness = new(taskIndex: 3);

        // Four tasks on the thread; the committing one sits at index 3.
        TestSqlTask first = harness.AddSibling(1);
        TestSqlTask second = harness.AddSibling(2);
        harness.Host.Register(3, harness.Task);
        TestSqlTask fourth = harness.AddSibling(4);

        // Every task has asked for a signal, so none is skipped for want of one.
        _ = first.GetCommitEvent();
        _ = second.GetCommitEvent();
        _ = harness.Task.GetCommitEvent();
        _ = fourth.GetCommitEvent();

        harness.Task.SetTransData(Descriptor("DisableBind=0"));
        IPooledTransaction? transaction = null;
        Assert.Equal(RetCode.OK, harness.Task.CallGetTransObject(ref transaction));

        Assert.Equal(RetCode.OK, harness.Task.Commit(autoRollback: true));

        // The current task and both PRECEDING ones are signalled ...
        Assert.True(first.IsCommitted());
        Assert.True(second.IsCommitted());
        Assert.True(harness.Task.IsCommitted());

        // ... and the FOLLOWING one is not, which is what walking DOWN from the current index means.
        Assert.False(fourth.IsCommitted());

        // The walk really did run from 3 down to 1 inclusive.
        Assert.Equal([3, 2, 1], harness.Host.RequestedTaskIndices);
    }

    [Fact]
    public void Commit_DoesNotCreateASignalForATaskThatNeverAskedForOne()
    {
        // [:L106] `if task._hEvtCommitted <> 0 then SetEvent(...)` - a task with no handle is SKIPPED,
        // never given one. Creating one here would allocate a handle nobody waits on and would make an
        // uninterested task suddenly report itself committed.
        using Harness harness = new(taskIndex: 2);

        TestSqlTask first = harness.AddSibling(1);
        harness.Host.Register(2, harness.Task);

        harness.Task.SetTransData(Descriptor("DisableBind=0"));
        IPooledTransaction? transaction = null;
        Assert.Equal(RetCode.OK, harness.Task.CallGetTransObject(ref transaction));

        Assert.Equal(RetCode.OK, harness.Task.Commit(autoRollback: true));

        Assert.False(first.HasCommitSignal);
        Assert.False(first.IsCommitted());
    }

    [Fact]
    public void Commit_OnFailure_SignalsNothing()
    {
        // [:L235] the notification is gated on IsSucceeded, and the tri-state algebra is preserved: a
        // cancelled commit is neither succeeded nor failed and therefore signals nothing.
        using Harness harness = new(taskIndex: 1);
        harness.Host.Register(1, harness.Task);
        harness.Activator.CommitResult = RetCode.CANCELLED;

        harness.Task.SetTransData(Descriptor("DisableBind=0"));
        IPooledTransaction? transaction = null;
        Assert.Equal(RetCode.OK, harness.Task.CallGetTransObject(ref transaction));
        _ = harness.Task.GetCommitEvent();

        Assert.Equal(RetCode.CANCELLED, harness.Task.Commit(autoRollback: true));

        Assert.False(harness.Task.IsCommitted());
        Assert.False(Predicates.IsSucceeded(RetCode.CANCELLED));
    }

    [Fact]
    public void Commit_OnAPrevention_StillSignals_BecauseAPreventionReadsAsSuccess()
    {
        // The tri-state hole, preserved: PREVENT is 1, IsSucceeded tests >= 0, so a prevention IS a
        // success to this gate [:L235] even though it is not an OK.
        using Harness harness = new(taskIndex: 1);
        harness.Host.Register(1, harness.Task);
        harness.Activator.CommitResult = RetCode.PREVENT;

        harness.Task.SetTransData(Descriptor("DisableBind=0"));
        IPooledTransaction? transaction = null;
        Assert.Equal(RetCode.OK, harness.Task.CallGetTransObject(ref transaction));
        _ = harness.Task.GetCommitEvent();

        Assert.Equal(RetCode.PREVENT, harness.Task.Commit(autoRollback: true));

        Assert.True(harness.Task.IsCommitted());
    }

    // ==========================================================================================
    //  SUITE 7 - LIFECYCLE ORDERING  [n_cst_thread_task_sqlbase.sru:L715-L735]
    // ==========================================================================================

    [Fact]
    public void OnError_RollsBackBeforeTheAncestor_AndReturnsTheAncestorsValue()
    {
        // [:L731-L735] THE ORDERING IS INVERTED relative to every other lifecycle hook, and it is
        // OBSERVABLE. The rollback must happen while the transaction is still attached, because the
        // ancestor's handling may notify, unwind or tear down. Swapping the two lines changes nothing
        // a return-value assertion would notice - hence this test asserts the ORDER.
        using Harness harness = new();
        harness.Host.OnErrorResult = 77L;

        harness.Task.SetTransData(Descriptor("DisableBind=0"));
        IPooledTransaction? transaction = null;
        Assert.Equal(RetCode.OK, harness.Task.CallGetTransObject(ref transaction));

        Assert.Equal(77L, harness.Task.CallOnError(RetCode.E_DB_ERROR, "boom"));

        // ROLLBACK FIRST [:L732], THEN the ancestor [:L733].
        Assert.Equal(["rollback", "ancestor:OnError"], harness.Order);

        // [:L734] and the ANCESTOR's value is what the hook answers - not the rollback's.
        Assert.Equal(RetCode.E_DB_ERROR, harness.Host.LastErrorCode);
        Assert.Equal("boom", harness.Host.LastErrorInfo);
    }

    [Fact]
    public void OnError_WithoutATransaction_StillCallsTheAncestor()
    {
        // [:L732] the rollback's own result is DISCARDED, so a task that never obtained a transaction
        // takes the same path and the ancestor still runs.
        using Harness harness = new();
        harness.Host.OnErrorResult = -5L;

        Assert.Equal(-5L, harness.Task.CallOnError(RetCode.FAILED, "no transaction"));
        Assert.Equal(["ancestor:OnError"], harness.Order);
    }

    [Fact]
    public void OnDbError_MasksBothTextMembersInItsLogAndNeitherInThePayloadItRaises()
    {
        // CONSTRAINT C-F, AND THE DRIVER MESSAGE IS THE HALF THAT IS EASY TO MISS. The statement member
        // obviously carries the whole generated statement - with DisableBind=1 the runtime interpolates
        // values as literals rather than binding them
        // [n_cst_thread_task_sqlbase.sru:L128] - but a driver message echoes offending values too: a
        // uniqueness violation names the duplicate key. Masking one and logging the other raw would put
        // the same material in the same record by a different route.
        //
        // THE PAYLOAD IS UNTOUCHED. The caller-side proxy receives the structure exactly as the oracle
        // builds it [:L86-L95], because that is the in-band channel the contract carries and the wire
        // projection does its own masking on the statement field (constraint C-B). Only the log is
        // narrowed, and the oracle's own logger narrows nothing at all (AAP 0.6.3.8).
        RecordingTaskLogger logger = new();
        using Harness harness = new(logger: logger);

        const string DriverText = "UNIQUE constraint failed: COMPANY.ID (duplicate value 4711)";
        const string Statement = "INSERT INTO COMPANY (ID, NAME) VALUES (4711, 'Zhang Wei')";

        Assert.Equal(
            SqlTaskBase.DbErrorEventResult,
            harness.Task.OnDbError(2627L, DriverText, Statement, DwBuffer.Primary, 3L));

        // IN BAND: byte for byte, both members.
        DbErrorData raised = Assert.Single(harness.Proxy.Errors);
        Assert.Equal(DriverText, raised.SqlErrText);
        Assert.Equal(Statement, raised.SqlSyntax);
        Assert.Equal(2627L, raised.SqlDbCode);
        Assert.Equal(3L, raised.Row);

        // IN THE LOG: no value from either member survives.
        string record = Assert.Single(logger.Records);
        Assert.DoesNotContain("4711", record, StringComparison.Ordinal);
        Assert.DoesNotContain("Zhang Wei", record, StringComparison.Ordinal);

        // ...while everything that locates the fault does: the driver code, the buffer, the row, the
        // shape of the message and the shape of the statement.
        Assert.Contains("2627", record, StringComparison.Ordinal);
        Assert.Contains("Primary", record, StringComparison.Ordinal);
        Assert.Contains("UNIQUE constraint failed", record, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO COMPANY", record, StringComparison.Ordinal);
        // THREE masks, and the arithmetic is per LITERAL rather than per member: the driver message
        // carries one numeric, and the statement carries a numeric and a quoted string. Asserting the
        // count rather than mere presence is what would catch a mask that stopped at the first literal.
        Assert.Equal(3, CountOccurrences(record, SqlRedactor.DefaultPlaceholder));
    }

    /// <summary>Counts non-overlapping occurrences of a marker.</summary>
    /// <param name="text">The text to scan.</param>
    /// <param name="marker">The marker to count.</param>
    /// <returns>The count.</returns>
    private static int CountOccurrences(string text, string marker)
    {
        int count = 0;
        int index = text.IndexOf(marker, StringComparison.Ordinal);

        while (index >= 0)
        {
            count++;
            index = text.IndexOf(marker, index + marker.Length, StringComparison.Ordinal);
        }

        return count;
    }


    [Fact]
    public void OnPrepare_CallsTheAncestorFirst_AndResetsAnExistingSignal()
    {
        // [:L725-L729] the ancestor runs FIRST here - the opposite of OnError - and the body then
        // resets the signal only if one exists, returning the substrate's zero.
        using Harness harness = new();

        Assert.Equal(0L, harness.Task.CallOnPrepare());
        Assert.Equal(["ancestor:OnPrepare"], harness.Order);
        Assert.False(harness.Task.HasCommitSignal);

        harness.Task.GetCommitEvent().Set();
        Assert.True(harness.Task.IsCommitted());

        Assert.Equal(0L, harness.Task.CallOnPrepare());
        Assert.False(harness.Task.IsCommitted());
    }

    [Fact]
    public void OnUninit_CallsTheAncestorFirst_ThenClearsEveryCrossBoundaryReference()
    {
        // [:L715-L723] THIS METHOD IS HAZARD 2 [docs/PB多线程绕坑提示.md:L5]. The ancestor runs first,
        // then the pool reference is dropped, the index zeroed, the transaction reference NULLED and
        // the commit handle closed.
        using Harness harness = new();
        harness.Task.SetTransData(Descriptor("DisableBind=0"));

        IPooledTransaction? transaction = null;
        Assert.Equal(RetCode.OK, harness.Task.CallGetTransObject(ref transaction));
        _ = harness.Task.GetCommitEvent();

        Assert.True(harness.Task.HasTransactionReference);
        Assert.True(harness.Task.HasCommitSignal);

        harness.Task.CallOnUninit();

        Assert.Equal(["ancestor:OnUninit"], harness.Order);
        Assert.False(harness.Task.HasTransactionReference);
        Assert.False(harness.Task.HasCommitSignal);

        // Idempotent: a task that ran uninit and is then disposed does no work twice.
        harness.Task.Dispose();
        Assert.False(harness.Task.HasTransactionReference);
    }

    [Fact]
    public void Dispose_ClearsTheSameReferences_ForATaskThatNeverReachedUninit()
    {
        // The safety net: a task discarded on an exception path never reaches OnUninit, and a reference
        // that outlives its task is the failure mode hazard 2 names.
        using Harness harness = new();
        harness.Task.SetTransData(Descriptor("DisableBind=0"));

        IPooledTransaction? transaction = null;
        Assert.Equal(RetCode.OK, harness.Task.CallGetTransObject(ref transaction));
        _ = harness.Task.GetCommitEvent();

        harness.Task.Dispose();

        Assert.False(harness.Task.HasTransactionReference);
        Assert.False(harness.Task.HasCommitSignal);

        // The task DROPPED its pool reference rather than disposing the transaction itself, and with
        // keep-alive off - the legacy default [n_cst_thread_trans_pool.sru:L76] - losing the last
        // reference is what makes the pool retire its own entry. The distinction matters: the transaction
        // belongs to the pool, so a task that disposed it directly would corrupt a sibling that still
        // held a reference.
        Assert.False(harness.Pool.Exists(Descriptor("DisableBind=0")));

        // Idempotent: a second disposal drops nothing further and does not throw.
        harness.Task.Dispose();
        Assert.False(harness.Task.HasTransactionReference);
    }

    [Fact]
    public void RunPrepare_RaisesThePrepareEvent_SoTheCommitSignalIsReArmedPerDispatch()
    {
        // THE SEAM THE COMPOSITION ROOT RAISES THE EVENT THROUGH. OnPrepare is protected because the
        // oracle declares it as an EVENT, raised by the substrate; this service has no substrate, so the
        // task factory is the substrate and this is how it raises it. While nothing called it, the commit
        // signal was never re-armed between dispatches - so a committed reading survived from one run
        // into the next.
        using Harness harness = new();

        _ = harness.Task.GetCommitEvent();
        harness.Task.GetCommitEvent().Set();
        Assert.True(harness.Task.IsCommitted());

        Assert.Equal(0L, harness.Task.RunPrepare());
        Assert.Equal(["ancestor:OnPrepare"], harness.Order);
        Assert.False(harness.Task.IsCommitted());
    }

    [Fact]
    public void RunUninit_RaisesTheUninitEvent_AndDisposalRaisesItToo()
    {
        // TEARDOWN GOES THROUGH THE HOOK ON EVERY PATH, which is the point: two paths carrying the
        // same three actions side by side let disposal skip the hook entirely - the ancestor's
        // uninit never runs and a derived override never runs either.
        using Harness harness = new();
        harness.Task.SetTransData(Descriptor("DisableBind=0"));

        IPooledTransaction? transaction = null;
        Assert.Equal(RetCode.OK, harness.Task.CallGetTransObject(ref transaction));
        _ = harness.Task.GetCommitEvent();

        harness.Task.RunUninit();

        Assert.Equal(["ancestor:OnUninit"], harness.Order);
        Assert.False(harness.Task.HasTransactionReference);
        Assert.False(harness.Task.HasCommitSignal);

        // Disposal raises it AGAIN and the hook absorbs the repeat rather than double-releasing: the
        // pooled reference is guarded on a positive index and the signal on a non-null reference.
        harness.Task.Dispose();

        Assert.Equal(["ancestor:OnUninit", "ancestor:OnUninit"], harness.Order);
        Assert.False(harness.Task.HasTransactionReference);
    }

    [Fact]
    public void Dispose_RaisesTheUninitEvent_ForATaskThatNeverReachedItExplicitly()
    {
        // The safety net, now expressed as the hook rather than as a copy of its body.
        using Harness harness = new();
        harness.Task.SetTransData(Descriptor("DisableBind=0"));

        IPooledTransaction? transaction = null;
        Assert.Equal(RetCode.OK, harness.Task.CallGetTransObject(ref transaction));
        _ = harness.Task.GetCommitEvent();

        harness.Task.Dispose();

        Assert.Equal(["ancestor:OnUninit"], harness.Order);
        Assert.False(harness.Task.HasTransactionReference);
        Assert.False(harness.Task.HasCommitSignal);
    }

    // ==========================================================================================
    //  SUITE 8 - THE PLACEHOLDER BINDER  [n_cst_thread_task_sqlbase.sru:L371-L482]
    // ==========================================================================================

    [Fact]
    public void BindParams_WithNoParameters_ReportsFailed()
    {
        // [:L400-L401]
        using Harness harness = new();
        string sql = "SELECT * FROM COMPANY WHERE id = :id";

        Assert.Equal(RetCode.FAILED, harness.Task.CallBindParams(ref sql, (long)DatabaseType.DbtMssql));
        Assert.Equal("SELECT * FROM COMPANY WHERE id = :id", sql);
    }

    [Fact]
    public void BindParams_SubstitutesANamedPlaceholder()
    {
        using Harness harness = new();
        harness.Task.AddParam("id", 7L);
        string sql = "SELECT * FROM COMPANY WHERE id = :id";

        Assert.Equal(RetCode.OK, harness.Task.CallBindParams(ref sql, (long)DatabaseType.DbtMssql));
        Assert.Equal("SELECT * FROM COMPANY WHERE id = 7", sql);
    }

    [Fact]
    public void BindParams_DualForm_EmitsTheObservableTextAndAParameterizedOneTogether()
    {
        // BOTH FORMS FROM ONE PASS. The observable text is what the oracle produces and what a
        // SQL-preview hook, a DbError payload and a characterization recording all compare against; the
        // parameterized text is what actually executes, so the interpolated literal never reaches the
        // provider as statement text. Neither can be dropped, and producing them together is what stops
        // them disagreeing.
        using Harness harness = new();
        harness.Task.AddParam("name", "Alice");
        harness.Task.AddParam("age", 30L);

        Assert.Equal(
            RetCode.OK,
            harness.Task.CallBindParams(
                "SELECT * FROM COMPANY WHERE name = :name AND age > :age",
                (long)DatabaseType.DbtMssql,
                out SqlBoundStatement bound));

        Assert.Equal(
            "SELECT * FROM COMPANY WHERE name = 'Alice' AND age > 30",
            bound.ObservableText);
        Assert.Equal(
            "SELECT * FROM COMPANY WHERE name = @p1 AND age > @p2",
            bound.ParameterizedText);

        Assert.Equal(2, bound.Parameters.Count);
        Assert.Equal("@p1", bound.Parameters[0].Name);
        Assert.Equal("Alice", bound.Parameters[0].Value);
        Assert.Equal("@p2", bound.Parameters[1].Name);
        Assert.Equal(30L, bound.Parameters[1].Value);
    }

    [Fact]
    public void BindParams_DualForm_CarriesOneParameterPerNameHoweverManyPlaceholdersShareIt()
    {
        // A NAMED parameter fills every placeholder sharing its name [:L460, :L472], and the
        // parameterized form reproduces that with ONE bound value reused at each occurrence - which is
        // exactly what a named provider parameter means. Two values for one name would be a different
        // statement.
        using Harness harness = new();
        harness.Task.AddParam("id", 7L);

        Assert.Equal(
            RetCode.OK,
            harness.Task.CallBindParams(
                "SELECT * FROM COMPANY WHERE id = :id OR parent = :id",
                (long)DatabaseType.DbtMssql,
                out SqlBoundStatement bound));

        Assert.Equal("SELECT * FROM COMPANY WHERE id = 7 OR parent = 7", bound.ObservableText);
        Assert.Equal("SELECT * FROM COMPANY WHERE id = @p1 OR parent = @p1", bound.ParameterizedText);
        Assert.Equal("@p1", Assert.Single(bound.Parameters).Name);
    }

    [Fact]
    public void BindParams_DualForm_WithNoPlaceholders_LeavesTheTwoFormsIdentical()
    {
        // Nothing to bind means nothing for the two forms to differ about, which is why the
        // overwhelming majority of statements execute exactly as they always did.
        using Harness harness = new();
        harness.Task.AddParam("unused", 1L);

        Assert.Equal(
            RetCode.OK,
            harness.Task.CallBindParams(
                "DELETE FROM COMPANY",
                (long)DatabaseType.DbtMssql,
                out SqlBoundStatement bound));

        Assert.Equal("DELETE FROM COMPANY", bound.ObservableText);
        Assert.Equal("DELETE FROM COMPANY", bound.ParameterizedText);
        Assert.Empty(bound.Parameters);
    }

    [Fact]
    public void BindParams_DualForm_NeverBindsInsideAQuotedRun()
    {
        // The quote tracking is the oracle's [:L425-L434, :L441], and it governs BOTH forms: a colon
        // inside a literal is not a placeholder, so it must not become a bound parameter either.
        using Harness harness = new();
        harness.Task.AddParam("id", 7L);

        Assert.Equal(
            RetCode.OK,
            harness.Task.CallBindParams(
                "SELECT ':id' AS literal, id FROM COMPANY WHERE id = :id",
                (long)DatabaseType.DbtMssql,
                out SqlBoundStatement bound));

        Assert.Equal("SELECT ':id' AS literal, id FROM COMPANY WHERE id = 7", bound.ObservableText);
        Assert.Equal(
            "SELECT ':id' AS literal, id FROM COMPANY WHERE id = @p1",
            bound.ParameterizedText);
        Assert.Equal("@p1", Assert.Single(bound.Parameters).Name);
    }

    [Fact]
    public void BindParams_ShiftsTheRemainingPositionsAfterEachSubstitution()
    {
        // [:L467-L470] a replacement rarely has the placeholder's length, so every LATER placeholder is
        // offset by the difference. Without the shift, everything after the first is corrupted.
        using Harness harness = new();
        harness.Task.AddParam("name", "Alice");
        harness.Task.AddParam("age", 30L);

        string sql = "SELECT * FROM COMPANY WHERE name = :name AND age > :age";

        Assert.Equal(RetCode.OK, harness.Task.CallBindParams(ref sql, (long)DatabaseType.DbtMssql));
        Assert.Equal("SELECT * FROM COMPANY WHERE name = 'Alice' AND age > 30", sql);
    }

    [Fact]
    public void BindParams_NeverSubstitutesInsideAQuotedRun()
    {
        // [:L441] `if bQtUnclose then continue` - a colon inside a literal is ordinary text.
        using Harness harness = new();
        harness.Task.AddParam("id", 7L);

        string sql = "SELECT ':id' AS tag, id FROM COMPANY WHERE id = :id";

        Assert.Equal(RetCode.OK, harness.Task.CallBindParams(ref sql, (long)DatabaseType.DbtMssql));
        Assert.Equal("SELECT ':id' AS tag, id FROM COMPANY WHERE id = 7", sql);
    }

    [Fact]
    public void BindParams_WithAnUnclosedQuote_ReportsInvalidArgument()
    {
        // [:L451]
        using Harness harness = new();
        harness.Task.AddParam("id", 7L);

        string sql = "SELECT * FROM COMPANY WHERE name = 'unterminated";

        Assert.Equal(
            RetCode.E_INVALID_ARGUMENT,
            harness.Task.CallBindParams(ref sql, (long)DatabaseType.DbtMssql));
    }

    [Fact]
    public void BindParams_WithAMixtureOfNamedAndPositionalParameters_ReportsInvalidArgument()
    {
        // [:L407] all named or none named: a positional parameter would race the named ones for the
        // same placeholder.
        using Harness harness = new();
        harness.Task.AddParam("id", 7L);
        harness.Task.AddParam(string.Empty, 8L);

        string sql = "SELECT * FROM COMPANY WHERE id = :id";

        Assert.Equal(
            RetCode.E_INVALID_ARGUMENT,
            harness.Task.CallBindParams(ref sql, (long)DatabaseType.DbtMssql));
    }

    [Fact]
    public void BindParams_WithAPositionalParameter_FillsExactlyOnePlaceholderAndStops()
    {
        // [:L460, :L472] an empty name matches ANY unreplaced placeholder and then exits.
        using Harness harness = new();
        harness.Task.AddParam(string.Empty, 1L);
        harness.Task.AddParam(string.Empty, 2L);

        string sql = "SELECT * FROM COMPANY WHERE id = :a AND age = :b";

        Assert.Equal(RetCode.OK, harness.Task.CallBindParams(ref sql, (long)DatabaseType.DbtMssql));
        Assert.Equal("SELECT * FROM COMPANY WHERE id = 1 AND age = 2", sql);
    }

    [Fact]
    public void BindParams_WithAnUnmatchedPlaceholder_ReportsOutOfBound()
    {
        // [:L477-L479] every placeholder must be substituted; a SURPLUS PARAMETER, by contrast, is
        // tolerated because the guard that would have rejected it at [:L454] is commented out.
        using Harness harness = new();
        harness.Task.AddParam("id", 7L);

        string sql = "SELECT * FROM COMPANY WHERE id = :id AND age = :age";

        Assert.Equal(
            RetCode.E_OUT_OF_BOUND,
            harness.Task.CallBindParams(ref sql, (long)DatabaseType.DbtMssql));
    }

    [Fact]
    public void BindParams_ToleratesMoreParametersThanPlaceholders_BecauseThatGuardIsInert()
    {
        // [:L454] `//if nArgCnt < nCount then return RetCode.E_OUT_OF_BOUND` is COMMENTED OUT in the
        // oracle and is carried across INERT (C-B). Reviving it would reject this statement.
        using Harness harness = new();
        harness.Task.AddParam("id", 7L);
        harness.Task.AddParam("surplus", 9L);

        string sql = "SELECT * FROM COMPANY WHERE id = :id";

        Assert.Equal(RetCode.OK, harness.Task.CallBindParams(ref sql, (long)DatabaseType.DbtMssql));
        Assert.Equal("SELECT * FROM COMPANY WHERE id = 7", sql);
    }

    [Fact]
    public void BindParams_WithADataWindowArgumentList_NamesThePositionalParameters()
    {
        // [:L410-L416]
        using Harness harness = new();
        harness.Task.AddParam(string.Empty, 7L);
        harness.Task.AddParam(string.Empty, "Alice");

        string sql = "SELECT * FROM COMPANY WHERE id = :id AND name = :name";

        Assert.Equal(
            RetCode.OK,
            harness.Task.CallBindParams(ref sql, (long)DatabaseType.DbtMssql, "id\tnumber\nname\tstring"));
        Assert.Equal("SELECT * FROM COMPANY WHERE id = 7 AND name = 'Alice'", sql);
    }

    [Fact]
    public void BindParams_WhenTheArgumentListLengthDisagrees_ReportsOutOfBound()
    {
        // [:L412] the counts must agree EXACTLY.
        using Harness harness = new();
        harness.Task.AddParam(string.Empty, 7L);

        string sql = "SELECT * FROM COMPANY WHERE id = :id";

        Assert.Equal(
            RetCode.E_OUT_OF_BOUND,
            harness.Task.CallBindParams(ref sql, (long)DatabaseType.DbtMssql, "id\tnumber\nname\tstring"));
    }

    [Fact]
    public void BindParams_WithAnUnparseableArgumentList_ReportsInvalidArgument()
    {
        // [:L411] a list that yields no pairs at all.
        using Harness harness = new();
        harness.Task.AddParam(string.Empty, 7L);

        string sql = "SELECT * FROM COMPANY WHERE id = :id";

        Assert.Equal(
            RetCode.E_INVALID_ARGUMENT,
            harness.Task.CallBindParams(ref sql, (long)DatabaseType.DbtMssql, "no-tab-here"));
    }

    [Fact]
    public void BindParams_WithANullArrayElement_ReportsBindFailure_TheOneNarrowing()
    {
        // THE ONE NARROWING (AAP §0.1.5). The oracle splices the null literal in and NULLIFIES THE
        // WHOLE STATEMENT while still answering success; a null statement cannot cross a service
        // boundary, so this port stops with a DEFINED ERROR and leaves the statement untouched.
        using Harness harness = new();
        harness.Task.AddParam("ids", new object?[] { 1L, null });

        string sql = "SELECT * FROM COMPANY WHERE id IN (:ids)";

        Assert.Equal(
            RetCode.E_SQL_BIND_ARG_FAILED,
            harness.Task.CallBindParams(ref sql, (long)DatabaseType.DbtMssql));
        Assert.Equal("SELECT * FROM COMPANY WHERE id IN (:ids)", sql);
    }

    [Fact]
    public void BindParams_DoesNotValidateSqlSyntax()
    {
        // [:L376] 该函数不验证SQL语法有效性 - the oracle's own non-guarantee, preserved. Nonsense in,
        // substituted nonsense out.
        using Harness harness = new();
        harness.Task.AddParam("x", 1L);

        string sql = "NOT SQL AT ALL :x ))(";

        Assert.Equal(RetCode.OK, harness.Task.CallBindParams(ref sql, (long)DatabaseType.DbtMssql));
        Assert.Equal("NOT SQL AT ALL 1 ))(", sql);
    }

    [Fact]
    public void BindParams_TreatsTheColonAsItsOwnDelimiter()
    {
        // The prefix is a member of the delimiter set [:L424], which is what makes `:a:b` TWO
        // placeholders rather than one.
        using Harness harness = new();
        harness.Task.AddParam("a", 1L);
        harness.Task.AddParam("b", 2L);

        string sql = ":a:b";

        Assert.Equal(RetCode.OK, harness.Task.CallBindParams(ref sql, (long)DatabaseType.DbtMssql));
        Assert.Equal("12", sql);
    }

    // ---------------------------------------------------------------------------------------------
    //  C-07's SECOND PLACEHOLDER FORM - THE POSITIONAL QUESTION MARK
    //  --------------------------------------------------------------------------------------------
    //  C-07 is the UNION of two legacy command surfaces: the transaction object's named one, which is
    //  the rest of this region, and the SQLite binding's ANONYMOUS one
    //  [ws_objects/pfw.utility.sqlite.pbl.src/n_sqlite.sru:L32-L43]. AAP 0.4.3 states the verb
    //  "preserves positional `?` binding" and the protocol definition records that "positional `?`
    //  substitution consumes the order". The oracle's own primary SQLite fixture uses that form
    //  [ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L396-L406], so without it that workflow cannot
    //  be replayed at all - which also blocks the paired characterization AAP 0.6.7 requires.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void BindParams_SubstitutesThePositionalQuestionMarkForm_TheOracleFixtureShape()
    {
        // THE LEGACY FIXTURE, VALUE FOR VALUE [w_test_sqlite.srw:L398-L399]. The leading `@` selector is
        // the command task's to strip, so the statement reaches the binder without it.
        using Harness harness = new();
        harness.Task.AddParam(string.Empty, "Paul");
        harness.Task.AddParam(string.Empty, 32L);
        harness.Task.AddParam(string.Empty, "California");
        harness.Task.AddParam(string.Empty, 20000L);
        harness.Task.AddParam(string.Empty, "1999-05-08");

        string sql = "INSERT INTO COMPANY (NAME,AGE,ADDRESS,SALARY,BIRTH) VALUES (?, ?, ?, ?, ?)";

        Assert.Equal(RetCode.OK, harness.Task.CallBindParams(ref sql, (long)DatabaseType.DbtMssql));
        Assert.Equal(
            "INSERT INTO COMPANY (NAME,AGE,ADDRESS,SALARY,BIRTH) "
                + "VALUES ('Paul', 32, 'California', 20000, '1999-05-08')",
            sql);
    }

    [Fact]
    public void BindParams_SubstitutesThePositionalQuestionMarkForm_TheOracleUpdateShape()
    {
        // The second shape the oracle uses [w_test_sqlite.srw:L313] - one marker, at the very end of the
        // statement, which is the position the final-flush arithmetic would get wrong if the marker were
        // opened rather than emitted complete.
        using Harness harness = new();
        harness.Task.AddParam(string.Empty, "Paul");

        string sql = "UPDATE COMPANY SET SALARY = SALARY + 100 WHERE NAME = ?";

        Assert.Equal(RetCode.OK, harness.Task.CallBindParams(ref sql, (long)DatabaseType.DbtMssql));
        Assert.Equal("UPDATE COMPANY SET SALARY = SALARY + 100 WHERE NAME = 'Paul'", sql);
    }

    [Fact]
    public void BindParams_DualForm_RewritesEachQuestionMarkToItsOwnProviderPlaceholder()
    {
        // The two forms diverge exactly as they do for the colon form: the observable text carries the
        // rendered literals a preview hook and a characterization recording compare against, and the
        // parameterized text carries `@pN` so the literal never reaches the provider as statement text.
        using Harness harness = new();
        harness.Task.AddParam(string.Empty, "Alice");
        harness.Task.AddParam(string.Empty, 30L);

        Assert.Equal(
            RetCode.OK,
            harness.Task.CallBindParams(
                "SELECT * FROM COMPANY WHERE name = ? AND age > ?",
                (long)DatabaseType.DbtMssql,
                out SqlBoundStatement bound));

        Assert.Equal("SELECT * FROM COMPANY WHERE name = 'Alice' AND age > 30", bound.ObservableText);
        Assert.Equal("SELECT * FROM COMPANY WHERE name = @p1 AND age > @p2", bound.ParameterizedText);
        Assert.Equal(2, bound.Parameters.Count);
        Assert.Equal("@p1", bound.Parameters[0].Name);
        Assert.Equal("Alice", bound.Parameters[0].Value);
        Assert.Equal("@p2", bound.Parameters[1].Name);
        Assert.Equal(30L, bound.Parameters[1].Value);
    }

    [Fact]
    public void BindParams_ConsumesTheTwoPlaceholderFormsInStatementORDER()
    {
        // ORDER, NOT FORM. A positional parameter matches ANY unreplaced placeholder [:L460], so the
        // first value fills whichever placeholder comes FIRST in the statement whether that is a colon
        // form or a question mark. This is the property "positional `?` substitution consumes the order"
        // names, and it is why the locator must emit the two forms interleaved in statement order.
        using Harness harness = new();
        harness.Task.AddParam(string.Empty, 1L);
        harness.Task.AddParam(string.Empty, 2L);
        harness.Task.AddParam(string.Empty, 3L);

        string sql = "SELECT * FROM T WHERE a = ? AND b = :b AND c = ?";

        Assert.Equal(RetCode.OK, harness.Task.CallBindParams(ref sql, (long)DatabaseType.DbtMssql));
        Assert.Equal("SELECT * FROM T WHERE a = 1 AND b = 2 AND c = 3", sql);
    }

    [Fact]
    public void BindParams_TreatsTwoAdjacentQuestionMarksAsTwoPlaceholders()
    {
        // The marker is emitted COMPLETE, so an adjacent pair cannot collapse into one placeholder the
        // way `:a:b` would if the prefix were not itself a delimiter.
        using Harness harness = new();
        harness.Task.AddParam(string.Empty, 1L);
        harness.Task.AddParam(string.Empty, 2L);

        string sql = "??";

        Assert.Equal(RetCode.OK, harness.Task.CallBindParams(ref sql, (long)DatabaseType.DbtMssql));
        Assert.Equal("12", sql);
    }

    [Fact]
    public void BindParams_ClosesAnOpenColonPlaceholderBeforeEmittingAQuestionMark()
    {
        // ORDERING INSIDE THE LOOP. `:a?` must close `:a` first - taking its length from the marker's own
        // offset - and only then emit the positional one. If the arms were reversed the colon form's
        // length would be measured against the wrong delimiter.
        using Harness harness = new();
        harness.Task.AddParam(string.Empty, 1L);
        harness.Task.AddParam(string.Empty, 2L);

        string sql = ":a?";

        Assert.Equal(RetCode.OK, harness.Task.CallBindParams(ref sql, (long)DatabaseType.DbtMssql));
        Assert.Equal("12", sql);
    }

    [Fact]
    public void BindParams_NeverTreatsAQuestionMarkInsideAQuotedRunAsAPlaceholder()
    {
        // The quote test is the colon arm's, for the same reason [:L441] - and it is load-bearing here
        // because the shipped provider accepts `'what?'` verbatim, so treating it as a placeholder would
        // corrupt a statement that works.
        using Harness harness = new();
        harness.Task.AddParam(string.Empty, 1L);

        string sql = "INSERT INTO T (A,B) VALUES (?, 'what?')";

        Assert.Equal(RetCode.OK, harness.Task.CallBindParams(ref sql, (long)DatabaseType.DbtMssql));
        Assert.Equal("INSERT INTO T (A,B) VALUES (1, 'what?')", sql);
    }

    [Fact]
    public void BindParams_WithFewerParametersThanQuestionMarks_ReportsOutOfBound()
    {
        // The same unmatched-placeholder arm the colon form reaches [:L477-L479], which the command task
        // reports as E_SQL_BIND_ARG_FAILED - the code the contract publishes for a binding failure.
        using Harness harness = new();
        harness.Task.AddParam(string.Empty, 1L);

        string sql = "INSERT INTO T (A,B) VALUES (?, ?)";

        Assert.Equal(
            RetCode.E_OUT_OF_BOUND,
            harness.Task.CallBindParams(ref sql, (long)DatabaseType.DbtMssql));
    }

    [Fact]
    public void BindParams_WithANamedParameter_CannotMatchAQuestionMark()
    {
        // A NAMED parameter's name is non-empty and the comparison is ordinal [:L460], so it can never
        // match the empty name a positional placeholder carries. The marker therefore stays unmatched and
        // the whole bind is refused rather than the value landing in the wrong slot.
        using Harness harness = new();
        harness.Task.AddParam("a", 1L);

        string sql = "SELECT * FROM T WHERE a = :a AND b = ?";

        Assert.Equal(
            RetCode.E_OUT_OF_BOUND,
            harness.Task.CallBindParams(ref sql, (long)DatabaseType.DbtMssql));
    }

    // ---------------------------------------------------------------------------------------------
    //  THE PRE-EXECUTION DETECTOR FOR A STATEMENT WITH PLACEHOLDERS AND NO PARAMETERS AT ALL
    //  --------------------------------------------------------------------------------------------
    //  The binder is gated on the parameter collection being non-empty, so this population was never
    //  scanned and reached the provider with its markers intact - where Microsoft.Data.Sqlite refuses it
    //  with an InvalidOperationException that escaped as an UNHANDLED fault. The detector is what lets
    //  it be refused with the contract's own binding code instead, and it is deliberately more
    //  SQL-literate than the oracle's scan because a false positive here would reject a statement the
    //  provider accepts.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Statements that DO carry a marker nothing would bind.
    /// </summary>
    /// <param name="sql">The statement.</param>
    [Theory]
    [InlineData("INSERT INTO T (A,B) VALUES (?, ?)")]
    [InlineData("UPDATE COMPANY SET SALARY = SALARY + 100 WHERE NAME = ?")]
    [InlineData("SELECT * FROM T WHERE a = :a")]
    [InlineData("SELECT * FROM T WHERE a = :a1")]
    [InlineData("SELECT * FROM T WHERE a = :_a")]
    [InlineData("SELECT * FROM T WHERE b = 'safe?' AND a = ?")]
    [InlineData("SELECT * FROM T -- trailing\n WHERE a = ?")]
    public void ContainsUnboundStatementParameterMarker_FindsTheTwoPublishedForms(string sql) =>
        Assert.True(TestSqlTask.CallContainsUnboundStatementParameterMarker(sql));

    /// <summary>
    /// Statements that carry NO marker, including every construct in which the provider does not see one.
    /// </summary>
    /// <param name="sql">The statement.</param>
    [Theory]
    [InlineData("")]
    [InlineData("SELECT 1")]
    [InlineData("DELETE FROM COMPANY")]
    [InlineData("INSERT INTO T (A,B) VALUES (1, 'what?')")]
    [InlineData("INSERT INTO T (A,B) VALUES (1, 'it''s a ? really')")]
    [InlineData("SELECT \"odd?column\" FROM T")]
    [InlineData("SELECT `odd?column` FROM T")]
    [InlineData("SELECT [odd?column] FROM T")]
    [InlineData("SELECT 1 -- why?")]
    [InlineData("SELECT 1 /* why? */ FROM T")]
    [InlineData("SELECT 1 /* unterminated? ")]
    [InlineData("SELECT 'unterminated? ")]
    [InlineData("SELECT 4 - 1 FROM T")]
    [InlineData("SELECT 4 / 2 FROM T")]
    [InlineData("SELECT a FROM T WHERE b = 1: ")]
    public void ContainsUnboundStatementParameterMarker_FindsNothingWhereTheProviderSeesNothing(
        string sql) =>
        Assert.False(TestSqlTask.CallContainsUnboundStatementParameterMarker(sql));

    [Theory]
    [InlineData("INSERT INTO T (A,B) VALUES (@p1, @p2)")]
    [InlineData("INSERT INTO T (A,B) VALUES ($a, $b)")]
    public void ContainsUnboundStatementParameterMarker_LeavesSQLitesOtherTwoFormsToTheProvider(
        string sql)
    {
        // DELIBERATE, AND DOCUMENTED ON THE DETECTOR. `@name` and `$name` are SQLite parameter forms but
        // not forms C-07 publishes, and matching `@` would misfire on a DOUBLED execution-mode selector
        // whose documented behaviour is that the survivor "reaches the provider and fails there". Both
        // still produce a DEFINED status rather than a fault, through the engine's bind-fault projection.
        Assert.False(TestSqlTask.CallContainsUnboundStatementParameterMarker(sql));
    }

    [Fact]
    public void BindParams_LogsTheSurplusItStillTolerates()
    {
        // THE OUTCOME MATCHES THE ORACLE (C-B): the guard at [:L454] stays commented out, and it is
        // load-bearing because a DataWindow may declare more arguments than its statement references
        // [:L412]. What this adds on top is that the mismatch is not SILENT - and the record carries
        // COUNTS ONLY, because a surplus parameter's value is exactly the live data constraint C-F keeps
        // out of logs.
        using Harness harness = new();
        harness.Task.AddParam("id", 7L);
        harness.Task.AddParam("surplus", "s3cret-value");

        string sql = "SELECT * FROM COMPANY WHERE id = :id";

        Assert.Equal(RetCode.OK, harness.Task.CallBindParams(ref sql, (long)DatabaseType.DbtMssql));
        Assert.Equal("SELECT * FROM COMPANY WHERE id = 7", sql);

        string record = Assert.Single(
            harness.Log.Records,
            entry => entry.Contains("surplus parameters match nothing", StringComparison.Ordinal));

        Assert.Contains("2 parameters", record, StringComparison.Ordinal);
        Assert.Contains("1 placeholders", record, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret-value", record, StringComparison.Ordinal);
        Assert.DoesNotContain("COMPANY", record, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseDataWindowArguments_SplitsPairsAndSkipsMalformedOnesSilently()
    {
        // [:L573-L595]
        Assert.Equal(0, SqlTaskBase.ParseDataWindowArguments(string.Empty, out _, out _));
        Assert.Equal(0, SqlTaskBase.ParseDataWindowArguments(null, out _, out _));

        // [:L583-L586] a pair with no tab, or with an empty half, is SKIPPED rather than reported.
        Assert.Equal(0, SqlTaskBase.ParseDataWindowArguments("lonely", out _, out _));
        Assert.Equal(0, SqlTaskBase.ParseDataWindowArguments("\tnumber", out _, out _));
        Assert.Equal(0, SqlTaskBase.ParseDataWindowArguments("name\t", out _, out _));

        // [:L576] the WHOLE list is lower-cased, names and types together.
        int count = SqlTaskBase.ParseDataWindowArguments(
            "Id\tNumber\nName\tString",
            out IReadOnlyList<string> names,
            out IReadOnlyList<string> types);

        Assert.Equal(2, count);
        Assert.Equal(["id", "name"], names);
        Assert.Equal(["number", "string"], types);

        // A trailing newline does not invent a third pair.
        Assert.Equal(
            2,
            SqlTaskBase.ParseDataWindowArguments("id\tnumber\nname\tstring\n", out _, out _));
    }

    // ==========================================================================================
    //  SUITE 9 - RETRIEVAL WITH MATCHED PARAMETERS  [n_cst_thread_task_sqlbase.sru:L597-L705]
    // ==========================================================================================

    [Fact]
    public async Task RetrieveWithParams_WithNamedParameters_MatchesByName()
    {
        // [:L616-L623] each argument position is filled by NAME, regardless of the parameter order.
        using Harness harness = new();
        harness.Runtime.Define(OtherDataObject, arguments: "id\tnumber\nname\tstring");

        ISqlDataStore store = harness.Task.CallGetCacheDataStore(OtherDataObject);
        harness.Task.AddParam("name", "Alice");
        harness.Task.AddParam("id", 7L);

        Assert.Equal(
            2L,
            await harness.Task.CallRetrieveWithParamsAsync(
                store,
                TestContext.Current.CancellationToken));
        Assert.Equal([7L, "Alice"], harness.Runtime.LastParameters);
    }

    [Fact]
    public async Task RetrieveWithParams_WhenANameDoesNotMatch_FallsBackToTheSamePosition()
    {
        // [:L624-L627] 没有找到按顺序取参. THE SECOND LOOP-VARIABLE-OUTLIVES-ITS-LOOP DEPENDENCY: the
        // oracle detects "no name matched" by testing the INNER loop's counter after it ended, which a
        // C# for-loop cannot express, so a naive port loses this fallback entirely.
        using Harness harness = new();
        harness.Runtime.Define(OtherDataObject, arguments: "id\tnumber\nmissing\tstring");

        ISqlDataStore store = harness.Task.CallGetCacheDataStore(OtherDataObject);
        harness.Task.AddParam("id", 7L);
        harness.Task.AddParam("other", "Alice");

        Assert.Equal(
            2L,
            await harness.Task.CallRetrieveWithParamsAsync(
                store,
                TestContext.Current.CancellationToken));

        // Position 1 matched by name; position 2 found no "missing" and took PARAMETER 2 positionally.
        Assert.Equal([7L, "Alice"], harness.Runtime.LastParameters);
    }

    [Fact]
    public async Task RetrieveWithParams_WithNoNamedParameters_CopiesPositionally()
    {
        // [:L632-L636]
        using Harness harness = new();
        harness.Runtime.Define(OtherDataObject, arguments: "id\tnumber\nname\tstring");

        ISqlDataStore store = harness.Task.CallGetCacheDataStore(OtherDataObject);
        harness.Task.AddParam(string.Empty, 7L);
        harness.Task.AddParam(string.Empty, "Alice");

        Assert.Equal(
            2L,
            await harness.Task.CallRetrieveWithParamsAsync(
                store,
                TestContext.Current.CancellationToken));
        Assert.Equal([7L, "Alice"], harness.Runtime.LastParameters);
    }

    [Fact]
    public async Task RetrieveWithParams_WithFewerParametersThanArguments_FallsBackToWholePositional()
    {
        // [:L615] the by-name block is GATED on `nParmCnt >= nDwArgCnt`, so with fewer parameters than
        // arguments it is skipped entirely and [:L632-L636] copies what there is.
        using Harness harness = new();
        harness.Runtime.Define(OtherDataObject, arguments: "id\tnumber\nname\tstring");

        ISqlDataStore store = harness.Task.CallGetCacheDataStore(OtherDataObject);
        harness.Task.AddParam("id", 7L);

        Assert.Equal(
            1L,
            await harness.Task.CallRetrieveWithParamsAsync(
                store,
                TestContext.Current.CancellationToken));
        Assert.Equal([7L], harness.Runtime.LastParameters);
    }

    [Fact]
    public async Task RetrieveWithParams_WithNoParametersAtAll_RetrievesWithAnEmptyList()
    {
        // [:L651-L652] the zero arm of the unroll - and the whole reason the unroll has nothing to port
        // is that ONE variadic call covers every arity (C-D). The legacy ceiling of 20 is recorded as a
        // legacy limit and is NOT enforced.
        using Harness harness = new();

        ISqlDataStore store = harness.Task.CallGetCacheDataStore(DwSqliteFixture.DataObjectName);

        Assert.Equal(
            0L,
            await harness.Task.CallRetrieveWithParamsAsync(
                store,
                TestContext.Current.CancellationToken));
        Assert.Empty(harness.Runtime.LastParameters);

        // Twenty-five parameters - five past the legacy unroll's ceiling - go through unremarkably.
        for (int index = 1; index <= 25; index++)
        {
            harness.Task.AddParam(string.Empty, (long)index);
        }

        Assert.Equal(
            25L,
            await harness.Task.CallRetrieveWithParamsAsync(
                store,
                TestContext.Current.CancellationToken));
        Assert.Equal(25, harness.Runtime.LastParameters.Count);
    }

    // ==========================================================================================
    //  SUITE 10 - THE DATABASE-ERROR EVENT AND TRANSACTION ACQUISITION
    //  [n_cst_thread_task_sqlbase.sru:L85-L98, :L137-L188]
    // ==========================================================================================

    [Fact]
    public void OnDbError_ForwardsThePayloadToTheProxy_AndReturnsTheLiteralThree()
    {
        // [:L86-L92] the five members in the structure's own order [dberrordata.srs:L4-L8], then
        // [:L94-L95] the forward, then [:L97] `return 3`.
        using Harness harness = new();

        long result = harness.Task.OnDbError(-206L, "table not found", "SELECT * FROM MISSING", DwBuffer.Filter, 4L);

        // PRESERVED (C-B): 3 is DELIBERATELY OUTSIDE the return-code algebra. It is the DataWindow
        // dberror event's own vocabulary and must not be mapped onto a RetCode.
        Assert.Equal(3L, result);
        Assert.Equal(SqlTaskBase.DbErrorEventResult, result);
        Assert.NotEqual(RetCode.OK, result);
        Assert.NotEqual(RetCode.PREVENT, result);

        DbErrorData received = Assert.Single(harness.Proxy.Errors);
        Assert.Equal(-206L, received.SqlDbCode);
        Assert.Equal("table not found", received.SqlErrText);
        Assert.Equal("SELECT * FROM MISSING", received.SqlSyntax);
        Assert.Equal(DwBuffer.Filter, received.Buffer);
        Assert.Equal(4L, received.Row);
    }

    [Fact]
    public void OnDbError_TheWirePayloadHasItsStatementMasked()
    {
        // C-F. The IN-PROCESS payload carries the complete generated statement - which with bind
        // variables disabled contains live row data - and the ONLY sanctioned projection masks it
        // unconditionally, with no policy argument a call site could weaken.
        using Harness harness = new();

        harness.Task.OnDbError(-1L, "failed", "UPDATE COMPANY SET salary = 1500.00 WHERE id = 3", DwBuffer.Primary, 3L);

        DbErrorData received = Assert.Single(harness.Proxy.Errors);

        // The in-process payload still carries the live values - it has not left the process yet.
        Assert.Contains("1500.00", received.SqlSyntax, StringComparison.Ordinal);

        DbError wire = received.ToDbError();

        // Every literal is masked while the statement's SHAPE survives, which is what makes a redacted
        // log record still diagnosable.
        Assert.Equal(
            $"UPDATE COMPANY SET salary = {SqlRedactor.DefaultPlaceholder} WHERE id = "
                + SqlRedactor.DefaultPlaceholder,
            wire.Sqlsyntax);
        Assert.DoesNotContain("1500.00", wire.Sqlsyntax, StringComparison.Ordinal);

        // Everything else is copied through verbatim (C-B).
        Assert.Equal(-1L, wire.Sqldbcode);
        Assert.Equal("failed", wire.Sqlerrtext);
        Assert.Equal(DwBuffer.Primary, wire.Buffer);
        Assert.Equal(3L, wire.Row);
    }

    [Fact]
    public void OnDbError_TheLogRecordMasksBothTheStatementAndTheProviderText()
    {
        // THE PROVIDER'S ERROR TEXT IS THE SECOND ROUTE OUT, AND LEAVING IT OPEN IS THE EASY MISS. Passing
        // the statement through the redactor while the provider's own text goes through nothing at all
        // leaks it anyway - a provider
        // composes that text FROM the statement it was executing, so it quotes the offending value back:
        // this is what a UNIQUE-constraint or type-conversion message really looks like. One record, two
        // fields, one rule.
        using Harness harness = new();

        harness.Task.OnDbError(
            -19L,
            "UNIQUE constraint failed: COMPANY.NAME = 'O''Hara' (salary 1500.00)",
            "UPDATE COMPANY SET salary = 1500.00 WHERE name = 'O''Hara'",
            DwBuffer.Primary,
            3L);

        string record = Assert.Single(harness.Log.Records);

        // Neither literal survives, from either field.
        Assert.DoesNotContain("1500.00", record, StringComparison.Ordinal);
        Assert.DoesNotContain("O''Hara", record, StringComparison.Ordinal);

        // What an operator needs does survive: the provider code, the buffer, the row, the constraint name
        // and the statement's shape.
        Assert.Contains("-19", record, StringComparison.Ordinal);
        Assert.Contains("UNIQUE constraint failed: COMPANY.NAME", record, StringComparison.Ordinal);
        Assert.Contains("UPDATE COMPANY SET salary", record, StringComparison.Ordinal);
        Assert.Contains(SqlRedactor.DefaultPlaceholder, record, StringComparison.Ordinal);

        // And no exception object is attached, which would have rendered both fields unmasked beside it.
        Assert.All(harness.Log.Exceptions, Assert.Null);
    }

    [Fact]
    public void OnDbError_WithNoProxyAttached_StillReturnsThree()
    {
        // [:L94] the oracle reads #ParentTasking into a local; an unattached task has no proxy, and the
        // event must not fault.
        using Harness harness = new(attachProxy: false);

        Assert.Equal(3L, harness.Task.OnDbError(-1L, "e", string.Empty, DwBuffer.Primary, 0L));
    }

    [Fact]
    public void GetTransObject_FirstAcquisition_BorrowsAndConnects()
    {
        // [:L164-L180]
        using Harness harness = new();
        harness.Task.SetTransData(Descriptor("DisableBind=0"));

        IPooledTransaction? transaction = null;
        Assert.Equal(RetCode.OK, harness.Task.CallGetTransObject(ref transaction));

        Assert.NotNull(transaction);
        Assert.Equal(1, harness.Activator.CreatedCount);
        Assert.True(harness.Activator.LastCreated!.Connected);

        // [:L170-L171] the task holds the association afterwards - see AttachTransaction for why the
        // oracle's two statements collapse into one here.
        Assert.True(harness.Task.HasTransactionReference);
        Assert.Same(harness.Activator.LastCreated, transaction);
    }

    [Fact]
    public void GetTransObject_OnASecondCall_ReusesTheHeldTransactionAndClearsItsState()
    {
        // [:L153-L158] reuse when a reference is held, the object is usable, AND it is not broken -
        // note the test is `Not of_IsBroken()`, not `of_IsConnected()`.
        using Harness harness = new();
        harness.Task.SetTransData(Descriptor("DisableBind=0"));

        IPooledTransaction? first = null;
        Assert.Equal(RetCode.OK, harness.Task.CallGetTransObject(ref first));

        IPooledTransaction? second = null;
        Assert.Equal(RetCode.OK, harness.Task.CallGetTransObject(ref second));

        Assert.Same(first, second);
        Assert.Equal(1, harness.Activator.CreatedCount);

        // [:L157] the reuse arm clears state; the acquisition arm does not.
        Assert.Equal(1, harness.Activator.LastCreated!.ClearStateCalls);
    }

    [Fact]
    public void GetTransObject_WhenTheHeldTransactionIsBroken_DropsItAndReacquires()
    {
        // [:L154, :L160-L161]
        using Harness harness = new();
        harness.Task.SetTransData(Descriptor("DisableBind=0"));

        IPooledTransaction? first = null;
        Assert.Equal(RetCode.OK, harness.Task.CallGetTransObject(ref first));
        harness.Activator.LastCreated!.Broken = true;

        IPooledTransaction? second = null;
        Assert.Equal(RetCode.OK, harness.Task.CallGetTransObject(ref second));

        Assert.NotSame(first, second);
        Assert.Equal(2, harness.Activator.CreatedCount);
    }

    [Fact]
    public void GetTransObject_WhenConnectFails_WritesExactlyTwoErrorMembers()
    {
        // [:L173-L178] only SQLDBCode and SQLErrText are written; the statement, buffer and row keep
        // whatever the caller had, because a connect failure has no statement and no row.
        using Harness harness = new();
        harness.Activator.ConnectFails = true;
        harness.Task.SetTransData(Descriptor("DisableBind=0"));

        IPooledTransaction? transaction = null;
        DbErrorData seeded = DbErrorData.FromStatement(0L, "untouched", "SELECT 1", DwBuffer.Delete, 9L);

        Assert.Equal(
            RetCode.E_INVALID_TRANSACTION,
            harness.Task.CallGetTransObject(ref transaction, ref seeded));

        Assert.Equal(FakePooledTransaction.ConnectFailureCode, seeded.SqlDbCode);
        Assert.Equal(FakePooledTransaction.ConnectFailureText, seeded.SqlErrText);

        // The other three are UNTOUCHED.
        Assert.Equal("SELECT 1", seeded.SqlSyntax);
        Assert.Equal(DwBuffer.Delete, seeded.Buffer);
        Assert.Equal(9L, seeded.Row);
    }

    [Fact]
    public void ReleaseTransObject_RefusesAnythingButTheHeldTransaction()
    {
        // [:L137-L139]
        using Harness harness = new();
        harness.Task.SetTransData(Descriptor("DisableBind=0"));

        // [:L137] an unusable argument.
        IPooledTransaction? nothing = null;
        Assert.Equal(RetCode.E_INVALID_OBJECT, harness.Task.CallReleaseTransObject(ref nothing));

        // [:L138] somebody else's transaction.
        IPooledTransaction? stranger = new FakePooledTransaction();
        Assert.Equal(RetCode.E_INVALID_OBJECT, harness.Task.CallReleaseTransObject(ref stranger));

        // [:L139] the held transaction, but with no pool reference - only reachable by releasing twice.
        IPooledTransaction? held = null;
        Assert.Equal(RetCode.OK, harness.Task.CallGetTransObject(ref held));
        IPooledTransaction? sameHeld = held;
        Assert.Equal(RetCode.OK, harness.Task.CallReleaseTransObject(ref held));

        // [:L143] the reference is cleared - hazard 2 - while the INDEX is deliberately left alone, so
        // a later acquisition re-enters through the reacquire path rather than the reuse arm.
        Assert.False(harness.Task.HasTransactionReference);
        Assert.Equal(RetCode.E_INVALID_OBJECT, harness.Task.CallReleaseTransObject(ref sameHeld));
    }

    [Fact]
    public void HasAliveTrans_AnswersFromTheHeldReferenceOrThePool()
    {
        // [:L211-L214]
        using Harness harness = new();
        harness.Task.SetTransData(Descriptor("DisableBind=0"));

        Assert.False(harness.Task.HasAliveTrans());

        IPooledTransaction? transaction = null;
        Assert.Equal(RetCode.OK, harness.Task.CallGetTransObject(ref transaction));

        Assert.True(harness.Task.HasAliveTrans());
    }

    [Fact]
    public void GetTransactionPool_AnswersTheInjectedSingleton()
    {
        // [:L190-L209] the caching, the keyed-data publish-back and the class indirection are all
        // discharged by the container registration; the member exists because callers read it as an
        // action - of_GetTransPool().of_RemoveRef(...).
        using Harness harness = new();

        Assert.Same(harness.Pool, harness.Task.CallGetTransactionPool());
        Assert.Same(harness.Task.CallGetTransactionPool(), harness.Task.CallGetTransactionPool());
    }

    // ==========================================================================================
    //  SUITE 11 - THE POOLED-TRANSACTION SURFACE, AND THE PROXY-PAIR DUALITY
    //  [n_cst_thread_task_sqlbase.sru:L122, :L142, :L165, :L169]  +  AAP 0.4.5.4
    //
    //  The suites above drive the pool THROUGH the task, which is how the task uses it. These drive
    //  the four members DIRECTLY, because their ONE-BASED index convention and their zero sentinel are
    //  a contract the task depends on and cannot state on its own.
    // ==========================================================================================

    [Fact]
    public void ThePoolSurfaceTheTaskDrivesIsTheOraclesFourOneBasedMembers()
    {
        // The oracle reaches the pool at exactly four places, and every one of them passes an INDEX:
        //     of_GetTransPool().of_RemoveRef(_nTransRefIdx)                    [:L122, :L716]
        //     of_GetTransPool().of_Release(_nTransRefIdx, ref transObject)     [:L142]
        //     _nTransRefIdx = transPool.of_AddRef(_transData)                  [:L165]
        //     rtCode = transPool.of_Get(_nTransRefIdx, ref transObject)        [:L169]
        // and every one of them guards with `_nTransRefIdx > 0` [:L122, :L153, :L164], so ZERO IS THE
        // "NO REFERENCE" SENTINEL rather than a valid first position.
        using Harness harness = new();
        TransactionData descriptor = Descriptor("DisableBind=0");

        // AddRef answers an INDEX, NOT A RETURN CODE - which is why its result is assigned rather than
        // tested with IsSucceeded - and the first entry is at ONE.
        int refIndex = harness.Pool.AddRef(descriptor);
        Assert.Equal(1, refIndex);
        Assert.True(harness.Pool.Exists(descriptor));

        // A SECOND descriptor appends at upper bound plus one, so the indices really are positions.
        int secondIndex = harness.Pool.AddRef(Descriptor("DisableBind=1;NCharBind=1"));
        Assert.Equal(2, secondIndex);

        // Get hands out the pooled transaction for that position.
        Assert.Equal(RetCode.OK, harness.Pool.Get(refIndex, out IPooledTransaction? borrowed));
        Assert.NotNull(borrowed);

        // ZERO NAMES NOTHING, on all three guarded members, and the answer is the documented code rather
        // than an exception or a null hand-back.
        Assert.Equal(RetCode.E_OUT_OF_BOUND, harness.Pool.Get(0, out IPooledTransaction? fromZero));
        Assert.Null(fromZero);

        IPooledTransaction? nothing = null;
        Assert.Equal(RetCode.E_OUT_OF_BOUND, harness.Pool.Release(0, ref nothing));
        Assert.Equal(RetCode.E_OUT_OF_BOUND, harness.Pool.RemoveRef(0));

        // And so does a position one past the last.
        Assert.Equal(RetCode.E_OUT_OF_BOUND, harness.Pool.Get(3, out IPooledTransaction? pastTheEnd));
        Assert.Null(pastTheEnd);
        Assert.Equal(RetCode.E_OUT_OF_BOUND, harness.Pool.RemoveRef(3));

        // Release returns the borrowed handle to the pool ...
        Assert.Equal(RetCode.OK, harness.Pool.Release(refIndex, ref borrowed));

        // ... and RemoveRef drops the REFERENCE, which is a different act from returning the handle.
        //
        // ⚠ HIGHEST INDEX FIRST, DELIBERATELY. Dropping an entry RENUMBERS every later position - the
        // legacy rebuilds its array - so removing position 1 first would leave the stored `secondIndex`
        // pointing past the end. That renumbering hazard is a finding of its own and is pinned by
        // TransactionPoolTests.ALeaseSurvivesTheRenumberingThatAStoredPositionDoesNot; it is not
        // re-litigated here, merely respected, and this comment exists so the ordering below reads as
        // intentional rather than arbitrary.
        Assert.Equal(RetCode.OK, harness.Pool.RemoveRef(secondIndex));
        Assert.Equal(RetCode.OK, harness.Pool.RemoveRef(refIndex));
        Assert.False(harness.Pool.Exists(descriptor));

        // The typed lease the port adds alongside the index carries the same sentinel: the default value
        // is deliberately the invalid one, for the same reason the legacy's sentinel is zero.
        Assert.False(PoolLease.None.IsValid);
        Assert.True(new PoolLease(1L).IsValid);
        Assert.False(new PoolLease(0L).IsValid);
    }

    [Fact]
    public void TheWorkerHalfAndItsCallerSideProxyRemainTwoTypes_TheDualityIsNotFlattened()
    {
        // ============================================================================================
        //  AAP 0.4.5.4 - THREAD-AFFINITY ANNOTATIONS ARE CONTRACT, NOT COMMENTARY (constraint C-K).
        //  The legacy encodes required execution context by having every concurrency class exist TWICE:
        //  a caller-side n_cst_threading_task_sql* and a worker-side n_cst_thread_task_sql*, so that no
        //  object is ever touched from two threads. This file's subject is the WORKER half - its own
        //  export comment reads [运行在子线程], "runs on the child thread". Collapsing the pair into one
        //  async method is the specific thing the affinity contract forbids, and it is the kind of
        //  simplification that looks like an improvement, compiles, passes every behavioural test, and
        //  removes the only record of which thread may touch what.
        // ============================================================================================

        // THE WORKER IS NOT A PROXY, IN EITHER DIRECTION. If either assignment held, one type would be
        // standing in for both halves and the duality would already be gone.
        Assert.False(typeof(ISqlTaskProxy).IsAssignableFrom(typeof(SqlTaskBase)));
        Assert.False(typeof(SqlTaskBase).IsAssignableFrom(typeof(SqlTaskProxyBase)));
        Assert.False(typeof(SqlTaskProxyBase).IsAssignableFrom(typeof(SqlTaskBase)));

        // The caller-side base IS the proxy contract, which is what the worker reaches it through.
        Assert.True(typeof(ISqlTaskProxy).IsAssignableFrom(typeof(SqlTaskProxyBase)));

        // THREE PAIRS, SIX DISTINCT TYPES - query, update and command, each with both halves.
        Type[] workers = [typeof(SqlQueryTask), typeof(SqlUpdateTask), typeof(SqlCommandTask)];
        Type[] proxies = [typeof(SqlQueryTaskProxy), typeof(SqlUpdateTaskProxy), typeof(SqlCommandTaskProxy)];

        Assert.All(workers, worker => Assert.True(typeof(SqlTaskBase).IsAssignableFrom(worker)));
        Assert.All(proxies, proxy => Assert.True(typeof(SqlTaskProxyBase).IsAssignableFrom(proxy)));
        Assert.All(workers, worker => Assert.False(typeof(ISqlTaskProxy).IsAssignableFrom(worker)));
        Assert.All(proxies, proxy => Assert.False(typeof(SqlTaskBase).IsAssignableFrom(proxy)));
        Assert.Equal(6, workers.Concat(proxies).Distinct().Count());

        // THE TWO HALVES MEET AT A TYPED SEAM, and it is one-directional: the worker's substrate hands it
        // the proxy as ISqlTaskProxy, so the worker can raise an event on its caller and can reach
        // nothing else of it.
        PropertyInfo? parentTasking = typeof(ISqlTaskHost).GetProperty(nameof(ISqlTaskHost.ParentTasking));
        Assert.NotNull(parentTasking);
        Assert.Equal(typeof(ISqlTaskProxy), parentTasking.PropertyType);

        // ... while the OTHER direction is the worker type itself, which is how the proxy reaches the
        // half that owns the parameters and the transaction.
        PropertyInfo? workerTask = typeof(ISqlTaskProxyHost).GetProperty(nameof(ISqlTaskProxyHost.Task));
        Assert.NotNull(workerTask);
        Assert.Equal(typeof(SqlTaskBase), workerTask.PropertyType);

        // The worker-side base is ABSTRACT, so no instance of it exists that is neither a query, an
        // update nor a command - which is what keeps the pairing exhaustive.
        Assert.True(typeof(SqlTaskBase).IsAbstract);
        Assert.True(typeof(SqlTaskProxyBase).IsAbstract);
    }

    [Fact]
    public void ADerivedTasksResetChainsToTheBase_ClearingTheParametersAndTheCommitSignal()
    {
        // [:L242-L249] of_reset resets the commit signal IF ONE EXISTS and then clears the parameters, and
        // every derived task's own reset must reach it. A derived override that forgot `base.Reset()`
        // would leave a task's parameters and its signalled commit state behind for the NEXT dispatch on
        // the same task - state the caller-side proxy explicitly permits reusing.
        using Harness harness = new();

        // A REAL derived task, not a stand-in: SqlCommandTask takes exactly the base's six collaborators,
        // so the chain can be exercised without a provider, a session or a running thread.
        using SqlCommandTask derived = new(
            harness.Host,
            harness.Pool,
            new RecordingDataStoreFactory(harness.Runtime),
            harness.HookActivator,
            FixedClock.Instance,
            NullLogger<SqlCommandTask>.Instance);

        // Base state, established through the base's own members.
        derived.AddParam("name", "unit-test-name");
        derived.AddParam("age", 30L);
        Assert.Equal(2, derived.GetParamCount());
        Assert.True(derived.HasParams());

        // Ask for the signal so the reset has one to reset, then signal it.
        ManualResetEventSlim signal = derived.GetCommitEvent();
        signal.Set();
        Assert.True(derived.IsCommitted());

        // THE DERIVED OVERRIDE, not the base member - this is the call a caller actually makes.
        MethodInfo? reset = typeof(SqlCommandTask).GetMethod(
            nameof(SqlCommandTask.Reset),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        Assert.NotNull(reset);
        Assert.Equal(typeof(SqlCommandTask), reset.DeclaringType);

        Assert.Equal(RetCode.OK, derived.Reset());

        // BOTH of the base's effects are visible through the derived instance, which is only possible if
        // the override chained.
        Assert.Equal(0, derived.GetParamCount());
        Assert.False(derived.HasParams());
        Assert.False(derived.IsCommitted());
        Assert.False(signal.IsSet);

        // And every derived task in the folder declares the override, so none of them can silently stop
        // chaining while the others keep doing it.
        Assert.All(
            new[] { typeof(SqlQueryTask), typeof(SqlUpdateTask), typeof(SqlCommandTask) },
            worker => Assert.Equal(
                worker,
                worker.GetMethod(
                    nameof(SqlCommandTask.Reset),
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    ?.DeclaringType));
    }

    // ==========================================================================================
    //  THE DOUBLES. Every collaborator the subject has is hand-written here, which is what makes
    //  the "no database, no real thread, no network" guarantee checkable rather than claimed (C-H).
    // ==========================================================================================

    /// <summary>
    /// Builds a synthetic connection descriptor. <b>Every value is invented here (C-F)</b> - nothing is
    /// copied from the legacy tree, from any catalogued secret site, or from any real system.
    /// </summary>
    /// <param name="dbParm">The connection parameter string whose two flags are under test.</param>
    /// <returns>The descriptor.</returns>
    private static TransactionData Descriptor(string dbParm) => new()
    {
        Dbms = "unit-test-dbms",
        ServerName = "unit-test-server",
        Database = "unit-test-database",
        LogId = "unit-test-identity",
        DbParm = dbParm,
    };

    /// <summary>
    /// The whole environment a task suite needs: the substrate, the proxy, the pool with its activator,
    /// the store factory, the DataWindow runtime, the hook allowlist, and one task wired to all of them.
    /// </summary>
    private sealed class Harness : IDisposable
    {
        /// <summary>Builds the environment.</summary>
        /// <param name="isMainThread">Whether the substrate reports the main thread.</param>
        /// <param name="taskIndex">The substrate's task index.</param>
        /// <param name="attachProxy">Whether a caller-side proxy is attached.</param>
        /// <param name="logger">
        /// The recording sink the task writes into, or <see langword="null"/> to have one created.
        /// </param>
        /// <remarks>
        /// THE SUPPLIED SINK IS THE ONE THE TASK GETS, and taking the parameter without honouring it is
        /// worse than not offering it: a caller that passes its own recorder then asserts against a list
        /// the task never wrote to, and an empty list reads as "nothing was logged" rather than as
        /// "you are holding the wrong list". Either spelling works - pass one and assert on it, or pass
        /// none and assert on <see cref="Log"/> - and both name the same object.
        /// </remarks>
        internal Harness(
            bool isMainThread = false,
            int taskIndex = 1,
            bool attachProxy = true,
            RecordingTaskLogger? logger = null)
        {
            Order = [];
            Log = logger ?? new RecordingTaskLogger();
            Proxy = new FakeSqlTaskProxy();
            Host = new FakeSqlTaskHost(Order)
            {
                IsMainThread = isMainThread,
                TaskIndex = taskIndex,
                ParentTasking = attachProxy ? Proxy : null,
            };

            Activator = new FakeActivator(Order);
            Pool = new TransactionPool(
                Options.Create(new PersistenceOptions()),
                FixedClock.Instance,
                Activator);

            Runtime = new FakeDataObjectRuntime();
            Runtime.Define(
                DwSqliteFixture.DataObjectName,
                sqlSelect: DwSqliteFixture.RetrieveStatement,
                sort: DwSqliteFixture.SortExpression);
            Runtime.Define(OtherDataObject, sqlSelect: "SELECT * FROM OTHER");

            HookActivator = new SqlRetrievalHookActivator();

            Task = new TestSqlTask(
                Host,
                Pool,
                new RecordingDataStoreFactory(Runtime),
                HookActivator,
                FixedClock.Instance,
                Log);
        }

        internal List<string> Order { get; }

        /// <summary>
        /// The records the task under test wrote.
        /// </summary>
        /// <remarks>
        /// SUPPLIED BECAUSE A REDACTION CLAIM CANNOT BE MADE ABOUT A NULL LOGGER. The wire payload's masking
        /// was already asserted, and the LOG record - a second, independent copy of the same statement and
        /// the provider's own text - was written to a sink nothing could read.
        /// </remarks>
        internal RecordingTaskLogger Log { get; }

        internal FakeSqlTaskHost Host { get; }

        internal FakeSqlTaskProxy Proxy { get; }

        internal FakeActivator Activator { get; }

        internal TransactionPool Pool { get; }

        internal FakeDataObjectRuntime Runtime { get; }

        internal SqlRetrievalHookActivator HookActivator { get; }

        internal TestSqlTask Task { get; }

        /// <summary>Registers an additional task on the fake thread, at a one-based index.</summary>
        /// <param name="index">The one-based task index.</param>
        /// <returns>The sibling task.</returns>
        internal TestSqlTask AddSibling(int index)
        {
            TestSqlTask sibling = new(
                Host,
                Pool,
                new RecordingDataStoreFactory(Runtime),
                HookActivator,
                FixedClock.Instance);

            Host.Register(index, sibling);
            return sibling;
        }

        public void Dispose()
        {
            Task.Dispose();
            Host.DisposeSiblings();
            Pool.Dispose();
        }
    }

    /// <summary>
    /// Captures the formatted records a task writes, so a redaction claim can be made about the log as well
    /// as about the wire.
    /// </summary>
    /// <remarks>
    /// THE FORMATTED TEXT IS WHAT A SINK WRITES, and it is what the two masked placeholders have to appear in.
    /// The exception argument is captured as well, because an attached exception is rendered in full by every
    /// provider and would republish whatever the formatted text was careful to mask.
    /// </remarks>
    private sealed class RecordingTaskLogger : ILogger<TestSqlTask>
    {
        internal List<string> Records { get; } = [];

        internal List<Exception?> Exceptions { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            Records.Add(formatter(state, exception));
            Exceptions.Add(exception);
        }
    }

    /// <summary>
    /// A concrete <c>SqlTaskBase</c> that exposes the protected surface the suites exercise. It adds no
    /// behaviour of its own, so every assertion above is an assertion about the base.
    /// </summary>
    private sealed class TestSqlTask : SqlTaskBase
    {
        internal TestSqlTask(
            ISqlTaskHost host,
            TransactionPool transactionPool,
            ISqlDataStoreFactory dataStoreFactory,
            ISqlRetrievalHookActivator hookActivator,
            TimeProvider timeProvider,
            ILogger<TestSqlTask>? logger = null)
            : base(
                host,
                transactionPool,
                dataStoreFactory,
                hookActivator,
                timeProvider,
                logger ?? NullLogger<TestSqlTask>.Instance)
        {
        }

        internal ISqlDataStore CallCreateDataStore() => CreateDataStore();

        internal ISqlDataStore CallGetCacheDataStore(string dataObject) => GetCacheDataStore(dataObject);

        internal long CallGetTransObject(ref IPooledTransaction? transaction) =>
            GetTransObject(ref transaction);

        internal long CallGetTransObject(ref IPooledTransaction? transaction, ref DbErrorData dbErrorData) =>
            GetTransObject(ref transaction, ref dbErrorData);

        internal long CallReleaseTransObject(ref IPooledTransaction? transaction) =>
            ReleaseTransObject(ref transaction);

        internal TransactionPool CallGetTransactionPool() => GetTransactionPool();

        internal ISqlRetrievalHook? CallResolveRetrievalHook(string? hookClassName) =>
            ResolveRetrievalHook(hookClassName);

        /// <summary>
        /// Reaches the SETTER'S admission test, which is the member a hook-class setter consults before
        /// storing a caller-supplied name.
        /// </summary>
        /// <param name="hookClassName">The class name, as a caller supplied it.</param>
        /// <returns>Whatever the production member returns.</returns>
        /// <remarks>
        /// Exposed separately from <see cref="CallResolveRetrievalHook"/> because the two answer DIFFERENT
        /// questions at DIFFERENT times: admission runs at the setter, where a refusal can still reach the
        /// caller as an argument error, and resolution runs at retrieval, where the only remaining option
        /// is to proceed with no hook. Testing one through the other would hide that separation, which is
        /// the whole design.
        /// </remarks>
        internal bool CallIsAdmissibleHookClass(string? hookClassName) =>
            IsAdmissibleHookClass(hookClassName);

        /// <summary>
        /// Reaches the pre-execution detector, which is <see langword="static"/> and needs no instance.
        /// </summary>
        /// <param name="sql">The statement to scan.</param>
        /// <returns>Whatever the production member returns.</returns>
        /// <remarks>
        /// Declared on this harness rather than tested through a task because the member is static and
        /// pure: a statement in, a verdict out, with no host, no pool and no provider involved. That is
        /// exactly the property that makes its false-positive matrix cheap to assert exhaustively, which
        /// matters because a false positive REFUSES a statement the provider would have run.
        /// </remarks>
        internal static bool CallContainsUnboundStatementParameterMarker(string sql) =>
            ContainsUnboundStatementParameterMarker(sql);

        internal long CallBindParams(ref string sql, long dbType) => BindParams(ref sql, dbType);

        internal long CallBindParams(ref string sql, long dbType, string dwArgString) =>
            BindParams(ref sql, dbType, dwArgString);

        /// <summary>Reaches the DUAL-FORM binder, which publishes both statement texts at once.</summary>
        /// <param name="sql">The statement to bind.</param>
        /// <param name="dbType">The dialect selecting the literal formats.</param>
        /// <param name="statement">The observable text, the parameterized text and the values.</param>
        /// <returns>Whatever the production overload returns.</returns>
        /// <remarks>
        /// Separate from the <see langword="ref"/> pass-through rather than replacing it, because the two
        /// production overloads are BOTH live: the ref one is the oracle's own progressive-rewrite shape
        /// and the dual-form one is what carries the parameterized text to the provider.
        /// </remarks>
        internal long CallBindParams(string sql, long dbType, out SqlBoundStatement statement) =>
            BindParams(sql, dbType, out statement);

        internal ValueTask<long> CallRetrieveWithParamsAsync(
            ISqlDataStore data,
            CancellationToken cancellationToken) =>
            RetrieveWithParamsAsync(data, cancellationToken);

        internal long CallOnPrepare() => OnPrepare();

        internal void CallOnUninit() => OnUninit();

        internal long CallOnError(long errCode, string errInfo) => OnError(errCode, errInfo);

        internal static void CallReleaseRetrievalHook(ref ISqlRetrievalHook? hook) =>
            ReleaseRetrievalHook(ref hook);
    }

    /// <summary>
    /// A hand-driven threading substrate. It runs no thread, owns no synchronization primitive and
    /// answers every question the subject asks directly.
    /// </summary>
    private sealed class FakeSqlTaskHost : ISqlTaskHost
    {
        private readonly Dictionary<string, object?> _data = new(StringComparer.Ordinal);
        private readonly Dictionary<int, SqlTaskBase> _tasks = [];
        private readonly List<string> _order;

        internal FakeSqlTaskHost(List<string> order) => _order = order;

        internal List<int> RequestedTaskIndices { get; } = [];

        internal long OnErrorResult { get; set; }

        internal long? LastErrorCode { get; private set; }

        internal string? LastErrorInfo { get; private set; }

        public bool IsMainThread { get; init; }

        public bool IsCancelled { get; init; }

        public int TaskIndex { get; init; } = 1;

        public ISqlTaskProxy? ParentTasking { get; init; }

        internal void Register(int index, SqlTaskBase task) => _tasks[index] = task;

        internal void DisposeSiblings()
        {
            foreach (SqlTaskBase task in _tasks.Values)
            {
                task.Dispose();
            }

            _tasks.Clear();
        }

        public long GetTask(int index, out SqlTaskBase? task)
        {
            RequestedTaskIndices.Add(index);

            if (_tasks.TryGetValue(index, out SqlTaskBase? found))
            {
                task = found;
                return RetCode.OK;
            }

            task = null;
            return RetCode.E_OUT_OF_BOUND;
        }

        public bool HasData(string name) => _data.ContainsKey(name);

        public object? GetData(string name) => _data.TryGetValue(name, out object? value) ? value : null;

        public long SetData(string name, object? data)
        {
            _data[name] = data;
            return RetCode.OK;
        }

        public long OnPrepare()
        {
            _order.Add("ancestor:OnPrepare");
            return 0L;
        }

        public void OnUninit() => _order.Add("ancestor:OnUninit");

        public long OnError(long errCode, string errInfo)
        {
            _order.Add("ancestor:OnError");
            LastErrorCode = errCode;
            LastErrorInfo = errInfo;
            return OnErrorResult;
        }

        public long OnNotify(long notifyCode, long payload, string text)
        {
            _order.Add($"ancestor:OnNotify:{notifyCode}:{payload}:{text}");
            return 0L;
        }
    }

    /// <summary>The caller-side proxy double: it latches what it is handed, exactly as the oracle does.</summary>
    private sealed class FakeSqlTaskProxy : ISqlTaskProxy
    {
        internal List<DbErrorData> Errors { get; } = [];

        public void OnDbError(in DbErrorData error) => Errors.Add(error);
    }

    /// <summary>
    /// The DataWindow runtime double. It resolves definitions from a dictionary and "retrieves" by
    /// recording the matched arguments and answering their count - no database, no provider, no SQL.
    /// </summary>
    private sealed class FakeDataObjectRuntime : IDataObjectRuntime
    {
        private readonly Dictionary<string, DataObjectDefinition> _definitions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _resolutions = new(StringComparer.Ordinal);

        internal IReadOnlyList<object?> LastParameters { get; private set; } = [];

        internal void Define(
            string dataObject,
            string sqlSelect = "SELECT * FROM COMPANY",
            string sort = "",
            string filter = "",
            string processing = "1",
            string arguments = "",
            string units = "0")
        {
            _definitions[dataObject] = new DataObjectDefinition(
                dataObject,
                sqlSelect,
                sort,
                filter,
                processing,
                arguments,
                units);
        }

        internal int ResolutionCount(string dataObject) =>
            _resolutions.TryGetValue(dataObject, out int count) ? count : 0;

        public bool TryResolveDefinition(
            string dataObject,
            [NotNullWhen(true)] out DataObjectDefinition? definition)
        {
            _resolutions[dataObject] = ResolutionCount(dataObject) + 1;
            return _definitions.TryGetValue(dataObject, out definition);
        }

        public ValueTask<long> RetrieveAsync(
            ISqlDataStore data,
            IReadOnlyList<object?> parameters,
            CancellationToken cancellationToken)
        {
            LastParameters = [.. parameters];
            return ValueTask.FromResult((long)parameters.Count);
        }
    }

    /// <summary>Hands out <see cref="RecordingDataStore"/> instances over real definition state.</summary>
    private sealed class RecordingDataStoreFactory : ISqlDataStoreFactory
    {
        private readonly IDataObjectRuntime _runtime;

        internal RecordingDataStoreFactory(IDataObjectRuntime runtime) => _runtime = runtime;

        public ISqlDataStore Create(CarrierThreadAffinity affinity) =>
            new RecordingDataStore(
                new SqlDataObjectStore(
                    DataWindowCarrierFactory.Create(affinity, FixedClock.Instance),
                    _runtime));
    }

    /// <summary>
    /// A recording <c>ISqlDataStore</c>. It delegates every question to a real
    /// <c>SqlDataObjectStore</c> - so the definition behaviour under test is the production one - and
    /// records WHICH setter the subject called, which is how the preserved cache-hit defect is pinned.
    /// </summary>
    private sealed class RecordingDataStore : ISqlDataStore
    {
        private readonly SqlDataObjectStore _inner;

        internal RecordingDataStore(SqlDataObjectStore inner) => _inner = inner;

        internal List<string> FilterWrites { get; } = [];

        internal List<string> SortWrites { get; } = [];

        internal int ClearStateCalls { get; private set; }

        internal int OnInitCalls { get; private set; }

        public DataWindowCarrier Carrier => _inner.Carrier;

        public string DataObject
        {
            get => _inner.DataObject;
            set => _inner.DataObject = value;
        }

        public string GetSqlSelect() => _inner.GetSqlSelect();

        public string Modify(string modificationScript) => _inner.Modify(modificationScript);

        public string Describe(string property) => _inner.Describe(property);

        public long SetFilter(string? filter)
        {
            FilterWrites.Add(filter ?? string.Empty);
            return _inner.SetFilter(filter);
        }

        public long SetSort(string? sort)
        {
            SortWrites.Add(sort ?? string.Empty);
            return _inner.SetSort(sort);
        }

        public void ClearState()
        {
            ClearStateCalls++;
            _inner.ClearState();
        }

        public void OnInit(ICarrierParentTask parentTask)
        {
            OnInitCalls++;
            _inner.OnInit(parentTask);
        }

        public ValueTask<long> RetrieveAsync(
            IReadOnlyList<object?> parameters,
            CancellationToken cancellationToken) =>
            _inner.RetrieveAsync(parameters, cancellationToken);
    }

    /// <summary>The pool's activator double, so the pool itself needs no engine and no connection.</summary>
    private sealed class FakeActivator : IPooledTransactionActivator
    {
        private readonly List<string> _order;

        internal FakeActivator(List<string> order) => _order = order;

        internal int CreatedCount { get; private set; }

        internal FakePooledTransaction? LastCreated { get; private set; }

        internal bool ConnectFails { get; set; }

        internal long CommitResult { get; set; } = RetCode.OK;

        public IPooledTransaction CreateDefault() => Produce();

        public IPooledTransaction Create(string className) => Produce();

        private FakePooledTransaction Produce()
        {
            CreatedCount++;
            LastCreated = new FakePooledTransaction(_order)
            {
                ConnectFails = ConnectFails,
                CommitResult = CommitResult,
            };

            return LastCreated;
        }
    }

    /// <summary>
    /// A pooled-transaction double. It opens nothing, composes no connection string and names no
    /// provider (C-E); it records the verb and answers a configured result.
    /// </summary>
    private sealed class FakePooledTransaction : IPooledTransaction
    {
        /// <summary>The synthetic provider code a failed connect reports.</summary>
        internal const long ConnectFailureCode = -1234L;

        /// <summary>The synthetic diagnostic a failed connect reports.</summary>
        internal const string ConnectFailureText = "unit-test connect refused";

        private readonly List<string> _order;

        internal FakePooledTransaction(List<string>? order = null) => _order = order ?? [];

        internal bool ConnectFails { get; init; }

        internal long CommitResult { get; init; } = RetCode.OK;

        internal bool Connected { get; private set; }

        internal bool Broken { get; set; }

        internal int ClearStateCalls { get; private set; }

        internal bool Disposed { get; private set; }

        public long SqlCode => 0L;

        public long SqlDbCode => ConnectFails ? ConnectFailureCode : 0L;

        public long SqlNRows => 0L;

        public string SqlErrText => ConnectFails ? ConnectFailureText : string.Empty;

        public string SqlReturnData => string.Empty;

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

        public void StampSqlState(in SqlState state)
        {
            // Nothing to record: no suite in this file asserts on stamped provider state.
        }

        public long Connect(CancellationToken cancellationToken = default)
        {
            if (ConnectFails)
            {
                return RetCode.E_INVALID_TRANSACTION;
            }

            Connected = true;
            return RetCode.OK;
        }

        public long Disconnect()
        {
            Connected = false;
            return RetCode.OK;
        }

        public long Rollback()
        {
            _order.Add("rollback");
            return RetCode.OK;
        }

        public long Commit(bool autoRollback)
        {
            _order.Add("commit");
            return CommitResult;
        }

        public long Commit() => Commit(true);

        public long AutoCommitCheckpoint() => RetCode.OK;

        public long Exec(string? sqlCommand, CancellationToken cancellationToken = default) => RetCode.OK;

        // The BOUND overload, delegating to the rendered one for the same reason the engine doubles do.
        public long Exec(in SqlCommandText command, CancellationToken cancellationToken = default) =>
            Exec(command.RenderedText, cancellationToken);

        public bool IsConnected() => Connected;

        // The probe-reporting overload. This double never consults a connection, so it never
        // probes - the flag is false on every path, matching the cache/refusal arms of the oracle.
        public bool IsConnected(out bool probed)
        {
            probed = false;
            return Connected;
        }

        public bool IsBroken() => Broken;

        public long SetBroken()
        {
            Broken = true;
            return RetCode.OK;
        }

        public void ClearState() => ClearStateCalls++;

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

        public DbErrorData CaptureError() => DbErrorData.FromTransaction(SqlDbCode, SqlErrText);

        public void Dispose() => Disposed = true;
    }

    /// <summary>A hook that always declines, which is the shape the fallback contract is about.</summary>
    private sealed class DecliningHook : ISqlRetrievalHook
    {
        public long OnRetrieve(SqlTaskBase task, IPooledTransaction transaction, DataWindowCarrier data) =>
            RetCode.E_NO_IMPLEMENTATION;
    }

    /// <summary>A declining hook that also holds a resource, so deterministic release is observable.</summary>
    private sealed class DisposableHook : ISqlRetrievalHook, IDisposable
    {
        internal bool Disposed { get; private set; }

        public long OnRetrieve(SqlTaskBase task, IPooledTransaction transaction, DataWindowCarrier data) =>
            RetCode.E_NO_IMPLEMENTATION;

        public void Dispose() => Disposed = true;
    }

    /// <summary>
    /// A clock that never moves. Eight lines, and it is why no test-clock package is referenced - and
    /// why nothing in these suites can depend on real time (C-H).
    /// </summary>
    private sealed class FixedClock : TimeProvider
    {
        internal static FixedClock Instance { get; } = new();

        public override DateTimeOffset GetUtcNow() => new(2022, 4, 14, 0, 0, 0, TimeSpan.Zero);
    }
}
