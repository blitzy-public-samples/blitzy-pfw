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
//
//  plus the two ORDERING contracts - the BACKWARD commit-signal propagation, and OnError's
//  rollback-before-ancestor - and the hook's decline contract, where E_NO_IMPLEMENTATION means
//  "not handled, run the default" rather than "failed".
//
//  NO DATABASE, NO REAL THREAD, NO NETWORK AND NO DataWindow RUNTIME IS INVOLVED ANYWHERE IN THIS
//  FILE (C-H). Every collaborator is a hand-written double in this file: the threading substrate,
//  the caller-side proxy, the pooled transaction, its activator, and the DataWindow runtime. The
//  clock is a fixed TimeProvider that nothing advances, which is exactly the point - the subject
//  reads no clock, and a test that needed one would prove otherwise.
//
//  EVERY VALUE HERE IS SYNTHETIC (C-F). No password, account, host or connection string is copied
//  from the legacy tree or from any of the catalogued in-source secret sites. The one
//  password-shaped constant is spelled so it could not be mistaken for a credential.
// ==============================================================================================

using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Tasks;
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
        OrderedMap cache = Assert.IsType<OrderedMap>(harness.Host.GetData("$SQL.DataStoreCache"));
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
        harness.Host.SetData("$SQL.DataStoreCache", cache);

        ISqlDataStore store = harness.Task.CallGetCacheDataStore(DwSqliteFixture.DataObjectName);

        Assert.Equal(DwSqliteFixture.RetrieveStatement, store.GetSqlSelect());
        Assert.IsType<CachedDataStore>(cache.Get(DwSqliteFixture.DataObjectName));
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
        // TEARDOWN GOES THROUGH THE HOOK ON EVERY PATH, which is the point: the two used to carry the
        // same three actions side by side, so disposing a task skipped the hook entirely - the ancestor's
        // uninit never ran and a derived override never ran either.
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
        // THE PROVIDER'S ERROR TEXT WAS THE SECOND ROUTE OUT, AND IT USED TO BE OPEN. The statement went
        // through the redactor and the provider's own text went through nothing at all - and a provider
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
        public long Exec(in SqlCommandText command, CancellationToken cancellationToken = default) => Exec(command.RenderedText);

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
