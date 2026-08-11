// ==============================================================================================
//  SqlQueryTaskTests.cs - THE CHUNKED-RETRIEVAL TASK PARITY SUITES
//  --------------------------------------------------------------------------------------------
//  UNDER TEST     services/persistence-service/PowerFramework.Persistence/Tasks/SqlQueryTask.cs
//  ORACLE         ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru      (READ ONLY)
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru   (READ ONLY)
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru              (READ ONLY)
//                 ws_objects/pfw.shared.pbl.src/isfailed.srf                            (READ ONLY)
//
//  WHAT THESE SUITES ARE FOR. SqlQueryTask reproduces eleven legacy behaviours a maintainer would
//  otherwise "fix", and a comment alone cannot stop that. Each is PINNED here by a test whose NAME
//  says the behaviour is deliberate:
//
//    D1   the chunk-size guard rejects 1000 ITSELF, so 1001 is the lowest legal value
//    D2   the private reset does NOT clear the page-native flag            <- the big one
//    D5   the dialect dispatch has two arms and SQLITE ROUTES TO SQL SERVER
//    D6   the defensive override fires on SQL code EXACTLY -1 with a NON-NEGATIVE row count
//    D7   a cancelled count query is reported as a DATABASE ERROR, not as a cancellation
//    D8   an empty sort or filter becomes a SINGLE SPACE
//    D9   the runtime statement substitution does NOT escape embedded quotes
//    D11  the hook is created before the transaction is resolved
//
//  plus the byte-exact count wrapper, the four arms of the page-count short-circuit, the codec
//  selection by processing kind, the chunk arithmetic, and the delegated one-based clause upsert.
//
//  NO DATABASE, NO DataWindow RUNTIME, NO NETWORK AND NO REAL WORKER THREAD IS INVOLVED ANYWHERE IN
//  THIS FILE (C-H). Every collaborator is a hand-written double below. The task's own clock is a fixed
//  TimeProvider that nothing advances, which is exactly the point - the subject reads no clock at all,
//  and the 20 ms inter-chunk yield belongs to the injected changeset codec, which is given the system
//  clock purely so its Task.Delay completes.
//
//  EVERY VALUE HERE IS SYNTHETIC (C-F). No password, account, host or connection string is copied from
//  the legacy tree or from any catalogued in-source secret site.
// ==============================================================================================

using System.Globalization;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Sql.Paging;
using PowerFramework.Persistence.Tasks;
using PowerFramework.Persistence.Transactions;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// The parity suites for <c>Tasks/SqlQueryTask.cs</c>.
/// </summary>
public sealed class SqlQueryTaskTests
{
    /// <summary>A synthetic data-object name. Invented here; not copied from the legacy tree.</summary>
    private const string SomeDataObject = "dw_query_probe";

    /// <summary>A synthetic statement, deliberately simple so clause rewriting is legible.</summary>
    private const string SomeSelect = "SELECT ID, NAME FROM COMPANY";

    /// <summary>
    /// The DataWindow release line the golden-master fixture declares
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L2</c>], used as a stand-in syntax string. It is
    /// never parsed by anything in this file - the create arm is a double.
    /// </summary>
    private const string FixtureSyntax = "release 12.5;";

    /// <summary>A synthetic hook class name. Invented here; the legacy names no hook class anywhere.</summary>
    private const string ProbeHookClass = "n_probe_retrieval_hook";

    /// <summary>A synthetic provider error code. Invented here; not a real SQLSTATE or vendor code.</summary>
    private const long ProbeDbCode = -917L;

    /// <summary>
    /// A synthetic provider diagnostic. Invented here (C-F): no message, account, host or connection
    /// string is copied from the legacy tree or from any catalogued in-source secret site.
    /// </summary>
    private const string ProbeDbErrorText = "unit-test provider diagnostic";

    /// <summary>
    /// Seed for the filter buffer's identifiers, so a filtered row is distinguishable from a primary
    /// one in a failure dump.
    /// </summary>
    private const long FilteredRowSeed = 1000L;

    /// <summary>
    /// Appends <paramref name="rows"/> baselined rows to <c>Primary!</c> directly, bypassing the
    /// retrieve events. Used only where the point is that a carrier ALREADY holds rows before the run -
    /// the retrieve path itself goes through <c>QueryDataStore.Fill</c> so the row cap stays live.
    /// </summary>
    /// <param name="carrier">The carrier to seed.</param>
    /// <param name="rows">The number of rows to append.</param>
    private static void SeedRows(DataWindowBufferStore carrier, long rows)
    {
        // R9: one-based and inclusive at both ends, matching every legacy loop.
        for (long index = 1L; index <= rows; index++)
        {
            long row = carrier.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);
            _ = carrier.SetItemValue(row, 1, DwBuffer.Primary, index);
            carrier.RowAt(row, DwBuffer.Primary).Baseline();
        }
    }

    /// <summary>
    /// Builds a drop-down child carrier holding one row. A child is transferred WHOLE rather than
    /// chunked [<c>:L121-L131</c>], so its size is immaterial - only its existence is.
    /// </summary>
    /// <returns>The child carrier.</returns>
    private static DataWindowBufferStore NewChildCarrier()
    {
        DataWindowBufferStore child = new() { Processing = new DataWindowProcessing(1L) };
        SeedRows(child, 1L);

        return child;
    }

    // ==========================================================================================
    //  SUITE 1 - DEFECT D1: THE CHUNK-SIZE GUARD IS INCLUSIVE  [:L410]
    //  `if chunkSize <= 1000 then return RetCode.E_INVALID_ARGUMENT` - so 1000 is REJECTED, even
    //  though the field's own comment at :L34 claims "min:1000".
    // ==========================================================================================

    [Fact]
    public void SetChunkSize_DefaultsToTenThousand()
    {
        // [:L34, :L256]
        using Harness harness = new();

        Assert.Equal(10000L, harness.Task.ChunkSize);
    }

    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(-1L)]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(999L)]
    [InlineData(1000L)]
    public void SetChunkSize_AtOrBelowOneThousand_IsRejected_PreservedInclusiveBoundary(long chunkSize)
    {
        // [:L410] THE COMPARISON IS `<=`. 1000 is the boundary value and it is REFUSED.
        using Harness harness = new();

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, harness.Task.SetChunkSize(chunkSize));

        // The rejected value is not stored: the guard returns before the assignment at :L412.
        Assert.Equal(10000L, harness.Task.ChunkSize);
    }

    [Theory]
    [InlineData(1001L)]
    [InlineData(10000L)]
    [InlineData(1_000_000L)]
    public void SetChunkSize_AboveOneThousand_IsAccepted(long chunkSize)
    {
        // [:L410-L414] 1001 is the LOWEST legal value.
        using Harness harness = new();

        Assert.Equal(RetCode.OK, harness.Task.SetChunkSize(chunkSize));
        Assert.Equal(chunkSize, harness.Task.ChunkSize);
    }

    // ==========================================================================================
    //  SUITE 2 - THE RESET PAIR  [:L237-L242] and [:L247-L267]
    //  Including DEFECT D2, which is the single most important test in this file.
    // ==========================================================================================

    [Fact]
    public void Reset_RestoresEveryListedDefault()
    {
        using Harness harness = new();

        // Registered FIRST, because an unsanctioned hook class is now refused at the setter rather than
        // stored and silently ignored at retrieval - the activator is an allowlist, so a name it does not
        // carry can never produce a hook.
        _ = harness.HookActivator.Register(ProbeHookClass, static () => new DisposableProbeHook(RetCode.OK));

        // Move every settable field off its default.
        Assert.Equal(RetCode.OK, harness.Task.SetHookClass(ProbeHookClass));

        // And an unsanctioned name is an ARGUMENT error, not a stored value: the caller learns its hook
        // was rejected while it can still act on that, instead of being told the retrieval succeeded.
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, harness.Task.SetHookClass("n_not_registered"));
        Assert.Equal(ProbeHookClass, harness.Task.HookClass);
        Assert.Equal(RetCode.OK, harness.Task.SetSql(SomeSelect));
        Assert.Equal(RetCode.OK, harness.Task.SetSqlSyntax("release 12.5;"));
        Assert.Equal(RetCode.OK, harness.Task.SetDataObject(SomeDataObject));
        Assert.Equal(RetCode.OK, harness.Task.SetSort("name A "));
        Assert.Equal(RetCode.OK, harness.Task.SetFilter("age > 30"));
        Assert.Equal(RetCode.OK, harness.Task.SetChunkSize(4096L));
        Assert.Equal(RetCode.OK, harness.Task.SetPaged(true));
        Assert.Equal(RetCode.OK, harness.Task.SetPageSize(25L));
        Assert.Equal(RetCode.OK, harness.Task.SetPageIndex(3L));
        Assert.Equal(RetCode.OK, harness.Task.SetPageCounting(false));
        Assert.Equal(RetCode.OK, harness.Task.SetMaxRows(500L));
        Assert.Equal(RetCode.OK, harness.Task.SetCache(true));
        Assert.Equal(RetCode.OK, harness.Task.SetWhereClause(1, ClauseModifier.SQL_MS_REPLACE, "ID > 0"));
        Assert.Equal(RetCode.OK, harness.Task.SetOrderByClause(1, ClauseModifier.SQL_MS_REPLACE, "ID"));
        Assert.Equal(RetCode.OK, harness.Task.SetPagedUniqueIndexColumns(["ID"]));

        // [:L237-L241] the public reset chains to the base, whose own reset answers RetCode.OK.
        Assert.Equal(RetCode.OK, harness.Task.Reset());

        // [:L250-L255] the six strings.
        Assert.Equal(string.Empty, harness.Task.HookClass);
        Assert.Equal(string.Empty, harness.Task.Sql);
        Assert.Equal(string.Empty, harness.Task.SqlSyntax);
        Assert.Equal(string.Empty, harness.Task.DataObject);
        Assert.Equal(string.Empty, harness.Task.NewSort);
        Assert.Equal(string.Empty, harness.Task.NewFilter);

        // [:L256-L262] the scalars.
        Assert.Equal(10000L, harness.Task.ChunkSize);
        Assert.Equal(0L, harness.Task.PageIndex);
        Assert.False(harness.Task.Paged);
        Assert.Equal(0L, harness.Task.PageSize);
        Assert.True(harness.Task.PageCounting);
        Assert.Equal(0L, harness.Task.MaxRows);
        Assert.False(harness.Task.Cache);

        // [:L264-L266] the three collections.
        Assert.Empty(harness.Task.Clauses.WhereClauses);
        Assert.Empty(harness.Task.Clauses.OrderByClauses);
        Assert.Empty(harness.Task.PagedUniqueIndexColumns);
    }

    [Fact]
    public void Reset_DoesNotClearPageNative_PreservedLegacyDefect()
    {
        // ======================================================================================
        //  DEFECT D2 [:L247-L267]. `_bPageNative` is the ONE settable field the private reset
        //  omits, and this test exists so that "fixing" the omission breaks a test whose NAME says
        //  the omission is deliberate.
        //
        //  Constraint C-B forbids the fix: the two paging arms emit DIFFERENT byte-exact statements
        //  depending on this flag, so clearing it would change the generated SQL of any workflow
        //  that resets between two paged retrievals.
        // ======================================================================================
        using Harness harness = new();

        Assert.False(harness.Task.PageNative);
        Assert.Equal(RetCode.OK, harness.Task.SetPageNative(true));
        Assert.True(harness.Task.PageNative);

        Assert.Equal(RetCode.OK, harness.Task.Reset());

        // IT SURVIVES. Do not "correct" this.
        Assert.True(harness.Task.PageNative);
    }

    [Fact]
    public async Task Reset_WhileRunning_IsRefusedAsBusy()
    {
        // [:L237] `if #Running then return RetCode.E_BUSY` - the one live #Running guard in the SQL
        // task layer. The gate here is the sink, which blocks inside the row-count callback so the
        // reset lands mid-retrieval.
        using Harness harness = new();
        harness.Task.SetSql(SomeSelect);

        long resetResultWhileRunning = RetCode.OK;

        harness.Sink.OnDataReceivedCallback = () =>
            resetResultWhileRunning = harness.Task.Reset();

        long result = await harness.Task
            .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, result);
        Assert.Equal(RetCode.E_BUSY, resetResultWhileRunning);

        // And once idle it succeeds again.
        Assert.Equal(RetCode.OK, harness.Task.Reset());
    }

    // ==========================================================================================
    //  SUITE 3 - DEFECT D8: THE EMPTY SORT AND FILTER BECOME A SINGLE SPACE  [:L481, :L487]
    // ==========================================================================================

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void SetSort_WithNothing_BecomesASingleSpace_PreservedLegacyDefect(string? sort)
    {
        // [:L486-L487] `if _sNewSort = "" then _sNewSort = " "`.
        using Harness harness = new();

        Assert.Equal(RetCode.OK, harness.Task.SetSort(sort));
        Assert.Equal(" ", harness.Task.NewSort);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void SetFilter_WithNothing_BecomesASingleSpace_PreservedLegacyDefect(string? filter)
    {
        // [:L480-L481] the identical rewrite on the other expression.
        using Harness harness = new();

        Assert.Equal(RetCode.OK, harness.Task.SetFilter(filter));
        Assert.Equal(" ", harness.Task.NewFilter);
    }

    // ==========================================================================================
    //  SUITE 4 - THE REMAINING SETTER GUARDS, AND THE TWO BOUNDARIES THAT DIFFER ON PURPOSE
    // ==========================================================================================

    [Fact]
    public void SetMaxRows_AcceptsZero_AndRejectsNegative()
    {
        // [:L433] `if rows < 0` - EXCLUSIVE of zero, deliberately unlike the chunk size's `<= 1000`.
        // Zero is this field's own legacy default and means "no limit" [:L261].
        using Harness harness = new();

        Assert.Equal(RetCode.OK, harness.Task.SetMaxRows(0L));
        Assert.Equal(0L, harness.Task.MaxRows);

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, harness.Task.SetMaxRows(-1L));
        Assert.Equal(0L, harness.Task.MaxRows);
    }

    [Fact]
    public void SetPageSizeAndPageIndex_RejectZeroAndNegative()
    {
        // [:L450, :L462] both are ONE-BASED magnitudes, so zero is invalid for both.
        using Harness harness = new();

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, harness.Task.SetPageSize(0L));
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, harness.Task.SetPageIndex(0L));
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, harness.Task.SetPageSize(-5L));
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, harness.Task.SetPageIndex(-5L));

        Assert.Equal(RetCode.OK, harness.Task.SetPageSize(1L));
        Assert.Equal(RetCode.OK, harness.Task.SetPageIndex(1L));
    }

    [Fact]
    public void SetDataObjectAndSetSqlSyntax_ClearEachOther_LastWins()
    {
        // [:L423] setting the data object blanks the syntax; [:L475] setting the syntax blanks the
        // data object. The oracle enforces the mutual exclusion by CLEARING, not by refusing.
        using Harness harness = new();

        harness.Task.SetSqlSyntax("release 12.5;");
        Assert.Equal("release 12.5;", harness.Task.SqlSyntax);
        Assert.Equal(string.Empty, harness.Task.DataObject);

        harness.Task.SetDataObject(SomeDataObject);
        Assert.Equal(SomeDataObject, harness.Task.DataObject);
        Assert.Equal(string.Empty, harness.Task.SqlSyntax);

        harness.Task.SetSqlSyntax("release 12.5;");
        Assert.Equal(string.Empty, harness.Task.DataObject);
    }

    [Fact]
    public void SetSql_HasNoEmptinessGuard_TheCheckIsDeferredToRunTime()
    {
        // [:L469] a plain assignment with NO guard, unlike C-07's own statement setter. The emptiness
        // check happens inside the task body instead [:L616-L617], and moving it forward would show a
        // caller a rejection at configuration time that the legacy defers.
        using Harness harness = new();

        Assert.Equal(RetCode.OK, harness.Task.SetSql(string.Empty));
        Assert.Equal(RetCode.OK, harness.Task.SetSql(null));
        Assert.Equal(string.Empty, harness.Task.Sql);
    }

    [Fact]
    public void SetPagedUniqueIndexColumns_ReplacesWholesale_AndNormalisesNulls()
    {
        // [:L406] the oracle assigns the array outright rather than merging into it.
        using Harness harness = new();

        Assert.Equal(RetCode.OK, harness.Task.SetPagedUniqueIndexColumns(["A", "B"]));
        Assert.Equal(["A", "B"], harness.Task.PagedUniqueIndexColumns);

        Assert.Equal(RetCode.OK, harness.Task.SetPagedUniqueIndexColumns(["C"]));
        Assert.Equal(["C"], harness.Task.PagedUniqueIndexColumns);

        Assert.Equal(RetCode.OK, harness.Task.SetPagedUniqueIndexColumns(null));
        Assert.Empty(harness.Task.PagedUniqueIndexColumns);
    }

    // ==========================================================================================
    //  SUITE 5 - THE DELEGATED ONE-BASED CLAUSE UPSERT  [:L269-L301]
    //  The R9 hazard. These assertions are about DELEGATION being correct, not about the scan being
    //  reimplemented here - Sql/ClauseModifier.cs owns the scan and has its own suite.
    // ==========================================================================================

    [Fact]
    public void SetWhereClause_SameSelectIndexTwice_UpdatesInPlace()
    {
        // [:L273-L281] a match leaves the loop variable at the matching position, so the write lands
        // on that entry rather than appending.
        using Harness harness = new();

        Assert.Equal(RetCode.OK, harness.Task.SetWhereClause(1, ClauseModifier.SQL_MS_REPLACE, "ID > 0"));
        Assert.Equal(RetCode.OK, harness.Task.SetWhereClause(1, ClauseModifier.SQL_MS_APPEND, "ID < 9"));

        Assert.Single(harness.Task.Clauses.WhereClauses);
        Assert.Equal("ID < 9", harness.Task.Clauses.WhereClauses[0].Clause);
        Assert.Equal(ClauseModifier.SQL_MS_APPEND, harness.Task.Clauses.WhereClauses[0].ModifyStyle);
    }

    [Fact]
    public void SetWhereClause_NewSelectIndex_AppendsAtEnd()
    {
        // [:L274-L279] no match leaves the loop variable at nCount + 1, so the write appends.
        using Harness harness = new();

        harness.Task.SetWhereClause(1, ClauseModifier.SQL_MS_REPLACE, "ID > 0");
        harness.Task.SetWhereClause(2, ClauseModifier.SQL_MS_REPLACE, "NAME IS NOT NULL");

        Assert.Equal(2, harness.Task.Clauses.WhereClauses.Count);
        Assert.Equal(1, harness.Task.Clauses.WhereClauses[0].SelectIndex);
        Assert.Equal(2, harness.Task.Clauses.WhereClauses[1].SelectIndex);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SetWhereClause_NonPositiveSelectIndex_IsRejected(int selectIndex)
    {
        // [:L271] `if selectIndex <= 0 or clause = "" then return RetCode.E_INVALID_ARGUMENT`. Zero
        // does NOT mean "the first select".
        using Harness harness = new();

        Assert.Equal(
            RetCode.E_INVALID_ARGUMENT,
            harness.Task.SetWhereClause(selectIndex, ClauseModifier.SQL_MS_REPLACE, "ID > 0"));

        Assert.Empty(harness.Task.Clauses.WhereClauses);
    }

    [Fact]
    public void SetOrderByClause_EmptyClause_IsRejected_AndTheTwoCollectionsAreSeparate()
    {
        // [:L288] the same guard on the other clause kind, and setting one never touches the other.
        using Harness harness = new();

        Assert.Equal(
            RetCode.E_INVALID_ARGUMENT,
            harness.Task.SetOrderByClause(1, ClauseModifier.SQL_MS_REPLACE, string.Empty));

        harness.Task.SetWhereClause(ClauseModifier.SQL_MS_REPLACE, "ID > 0");

        Assert.Single(harness.Task.Clauses.WhereClauses);
        Assert.Empty(harness.Task.Clauses.OrderByClauses);
    }

    // ==========================================================================================
    //  SUITE 6 - THE BYTE-EXACT COUNT WRAPPER  [:L826-L834]
    //  Both upstream citations are real and different: :L830 is the "1 AS _" replacement and :L834
    //  is the pfwPagedSQL_Tbl wrap.
    // ==========================================================================================

    [Fact]
    public void BuildCountStatement_EmitsTheExactWrapper_WithTheAliasAndTheSentinel()
    {
        // [:L830] the column clause becomes the literal `1 AS _`; [:L834] the wrap.
        long result = SqlQueryTask.BuildCountStatement(SomeSelect, out string countSql, out string error);

        Assert.Equal(RetCode.OK, result);
        Assert.Equal(string.Empty, error);

        Assert.Equal(
            "SELECT COUNT(1) AS CNT FROM (SELECT 1 AS _ FROM COMPANY) pfwPagedSQL_Tbl",
            countSql);

        // The alias literal and the sentinel are both observable, and the sentinel comes from its
        // single owner rather than from a retyped literal.
        Assert.Contains(SqlQueryTask.CountColumnAlias, countSql, StringComparison.Ordinal);
        Assert.EndsWith(SqlServerPagingRewriter.DerivedTableAlias, countSql, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCountStatement_StripsTheOrderBy_OnlyWhenOneWasPresent()
    {
        // [:L831-L832] the strip is CONDITIONAL on HasOrder(). A statement that never had an ORDER BY
        // must not be given an empty one, because the statement model distinguishes an absent clause
        // from a present-but-empty one when it re-emits.
        Assert.Equal(
            RetCode.OK,
            SqlQueryTask.BuildCountStatement(
                SomeSelect + " ORDER BY NAME",
                out string withOrder,
                out _));

        Assert.DoesNotContain("ORDER BY", withOrder, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(RetCode.OK, SqlQueryTask.BuildCountStatement(SomeSelect, out string withoutOrder, out _));

        // Both arms converge on the same text, which is the point: the conditional exists so the
        // absent-clause case is not rewritten at all.
        Assert.Equal(withOrder, withoutOrder);
    }

    [Fact]
    public void BuildCountStatement_OnAnUnparseableStatement_IsAnInternalError()
    {
        // [:L826-L828]
        long result = SqlQueryTask.BuildCountStatement(
            "DELETE FROM COMPANY",
            out string countSql,
            out string error);

        Assert.Equal(RetCode.E_INTERNAL_ERROR, result);
        Assert.Equal(string.Empty, countSql);
        Assert.Equal(SqlQueryTask.ParseFailedText, error);
    }

    // ==========================================================================================
    //  SUITE 7 - THE PAGE-COUNT SHORT-CIRCUIT  [:L819-L823]
    //  Four arms, all pure, all with no statement issued.
    // ==========================================================================================

    [Fact]
    public void TryInferPageCounts_PartialFinalPage_IsCountedArithmetically()
    {
        // [:L820] first disjunct: some rows, fewer than a full page. [:L821-L822] the inference.
        Assert.True(SqlQueryTask.TryInferPageCounts(7L, 25L, 3L, out long pages, out long records));

        Assert.Equal(3L, pages);
        Assert.Equal(57L, records);
    }

    [Fact]
    public void TryInferPageCounts_EmptyFirstPage_IsCountedAsZeroAndZero()
    {
        // [:L820] second disjunct, and [:L823] the correction that overrides the line above it -
        // without it an empty first page would report ONE page holding NO records.
        Assert.True(SqlQueryTask.TryInferPageCounts(0L, 25L, 1L, out long pages, out long records));

        Assert.Equal(0L, pages);
        Assert.Equal(0L, records);
    }

    [Fact]
    public void TryInferPageCounts_EmptyLaterPage_DoesNotShortCircuit()
    {
        // The `_nPageIndex = 1` conjunct is NOT redundant: an empty page five says nothing about the
        // total, so it must fall through to the counting statement. Dropping the conjunct would
        // silently report zero records for every empty page.
        Assert.False(SqlQueryTask.TryInferPageCounts(0L, 25L, 5L, out _, out _));
    }

    [Fact]
    public void TryInferPageCounts_FullPage_DoesNotShortCircuit()
    {
        // A page that filled completely may or may not be the last one, so it cannot be inferred.
        Assert.False(SqlQueryTask.TryInferPageCounts(25L, 25L, 1L, out _, out _));
    }

    [Theory]
    [InlineData(0L, 25L, 0L)]
    [InlineData(1L, 25L, 1L)]
    [InlineData(25L, 25L, 1L)]
    [InlineData(26L, 25L, 2L)]
    [InlineData(1_000_000_000_000L, 7L, 142_857_142_858L)]
    public void DividePagesCeiling_RoundsUpExactly(long records, long pageSize, long expected)
    {
        // [:L853] `Ceiling(nRecordCount / _nPageSize)`. Decimal division, so a large exact multiple
        // cannot be nudged across the integer by binary floating-point rounding.
        Assert.Equal(expected, SqlQueryTask.DividePagesCeiling(records, pageSize));
    }

    // ==========================================================================================
    //  SUITE 8 - DEFECT D5: THE TWO-ARM DIALECT DISPATCH, AND SQLITE ROUTES TO SQL SERVER
    //  [n_cst_thread_trans.sru:L356-L361] and [:L320-L399]
    // ==========================================================================================

    [Fact]
    public void BuildPagedStatement_ForAnOracleEngine_UsesTheOracleArm()
    {
        using Harness harness = new();
        harness.Task.SetPageSize(10L);
        harness.Task.SetPageIndex(2L);

        PagedStatementOutcome outcome = harness.Task.BuildPagedStatement(
            PagingDialectResolver.Resolve("ORACLE 19c"),
            SomeSelect);

        Assert.Equal(RetCode.OK, outcome.ReturnCode);

        // The Oracle arm's triple-nested row-number sentinels, spelled by their single owner.
        Assert.Contains(OraclePagingRewriter.InnerInnerTableAlias, outcome.PagedSql, StringComparison.Ordinal);
        Assert.Contains(OraclePagingRewriter.OuterTableAlias, outcome.PagedSql, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPagedStatement_ForASqliteEngine_UsesTheSqlServerArm_PreservedLegacyBehaviour()
    {
        // ======================================================================================
        //  DEFECT D5. The resolver is `Pos(Upper(DBMS),"ORACLE") > 0 ? DBT_ORACLE : DBT_MSSQL`
        //  [n_cst_thread_trans.sru:L356-L361], so ANYTHING that does not name Oracle - INCLUDING
        //  SQLITE, the only engine this service provisions - classifies as the SQL Server type.
        //  There is no third arm and none may be added (C-B, C-E): the dialect value selects a PURE
        //  STRING TRANSFORM, never a connection.
        // ======================================================================================
        Assert.Equal(DatabaseType.DbtMssql, PagingDialectResolver.Resolve("SQLITE"));

        using Harness harness = new();
        harness.Task.SetPageSize(10L);
        harness.Task.SetPageIndex(2L);

        PagedStatementOutcome outcome = harness.Task.BuildPagedStatement(
            PagingDialectResolver.Resolve("SQLITE"),
            SomeSelect);

        Assert.Equal(RetCode.OK, outcome.ReturnCode);

        // The SQL Server arm's row-number wrapper, not an Oracle one and not a SQLite one.
        Assert.Contains(SqlServerPagingRewriter.RowNumberAlias, outcome.PagedSql, StringComparison.Ordinal);
        Assert.DoesNotContain(
            OraclePagingRewriter.InnerInnerTableAlias,
            outcome.PagedSql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPagedStatement_ForAnUnrecognisedDialect_IsNotImplementedWithAnEmptyMessage()
    {
        // [:L396-L398] the `case else` arm. A protobuf enum is OPEN, so a value outside {0,1} really
        // can arrive - and the message is DELIBERATELY EMPTY, which is a real value rather than a
        // missing one.
        using Harness harness = new();
        harness.Task.SetPageSize(10L);
        harness.Task.SetPageIndex(1L);

        PagedStatementOutcome outcome = harness.Task.BuildPagedStatement((DatabaseType)99, SomeSelect);

        Assert.Equal(RetCode.E_NO_IMPLEMENTATION, outcome.ReturnCode);
        Assert.Equal(string.Empty, outcome.ErrorText);
        Assert.Equal(string.Empty, outcome.PagedSql);
    }

    [Fact]
    public void BuildPagedStatement_WithNoPagingBounds_IsAnInvalidArgument()
    {
        // [:L307-L310] the run-time re-validation, which is the ONLY guard a caller who never called
        // the setters reaches - the reset installs page size and page index of zero.
        using Harness harness = new();

        PagedStatementOutcome outcome = harness.Task.BuildPagedStatement(DatabaseType.DbtMssql, SomeSelect);

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, outcome.ReturnCode);
        Assert.Equal(PagingRewriteResult.InvalidPagingSettingText, outcome.ErrorText);
    }

    // ==========================================================================================
    //  SUITE 9 - THE STORED CLAUSE APPLICATION  [:L685-L703]
    // ==========================================================================================

    [Fact]
    public void ApplyStoredClauses_AppliesWhereThenOrderBy_InInsertionOrder()
    {
        // [:L689-L702] two separate loops over two separate collections, WHERE first.
        using Harness harness = new();
        harness.Task.SetWhereClause(1, ClauseModifier.SQL_MS_REPLACE, "ID > 0");
        harness.Task.SetOrderByClause(1, ClauseModifier.SQL_MS_REPLACE, "NAME");

        long result = harness.Task.ApplyStoredClauses(SomeSelect, out string modified, out string error);

        Assert.Equal(RetCode.OK, result);
        Assert.Equal(string.Empty, error);
        Assert.Contains("WHERE ID > 0", modified, StringComparison.Ordinal);
        Assert.Contains("ORDER BY NAME", modified, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyStoredClauses_OnAnUnparseableStatement_IsAnInternalError()
    {
        // [:L685-L687]
        using Harness harness = new();
        harness.Task.SetWhereClause(1, ClauseModifier.SQL_MS_REPLACE, "ID > 0");

        long result = harness.Task.ApplyStoredClauses("UPDATE COMPANY SET AGE = 1", out string modified, out string error);

        Assert.Equal(RetCode.E_INTERNAL_ERROR, result);
        Assert.Equal(string.Empty, modified);
        Assert.Equal(SqlQueryTask.ParseFailedText, error);
    }

    [Fact]
    public void ApplyStoredClauses_OnAnUnreachableSelectIndex_ReportsTheWhereFailure()
    {
        // [:L691-L693] the per-clause failure, with its verbatim Chinese diagnostic. A single-select
        // statement has no second select for index 2 to address.
        using Harness harness = new();
        harness.Task.SetWhereClause(2, ClauseModifier.SQL_MS_REPLACE, "ID > 0");

        long result = harness.Task.ApplyStoredClauses(SomeSelect, out _, out string error);

        Assert.Equal(RetCode.E_INTERNAL_ERROR, result);
        Assert.Equal(SqlQueryTask.ModifyWhereFailedText, error);
    }

    [Fact]
    public void ApplyStoredClauses_OnAnUnreachableOrderBySelectIndex_ReportsTheOrderByFailure()
    {
        // [:L698-L700] the other verbatim diagnostic, reached only after every WHERE clause succeeded.
        using Harness harness = new();
        harness.Task.SetOrderByClause(2, ClauseModifier.SQL_MS_REPLACE, "NAME");

        long result = harness.Task.ApplyStoredClauses(SomeSelect, out _, out string error);

        Assert.Equal(RetCode.E_INTERNAL_ERROR, result);
        Assert.Equal(SqlQueryTask.ModifyOrderByFailedText, error);
    }

    // ==========================================================================================
    //  SUITE 10 - THE QUOTE ESCAPE, AND DEFECT D9's ASYMMETRY  [:L709, :L732, :L795] vs [:L635]
    // ==========================================================================================

    [Fact]
    public void EscapeStatementForModify_ReplacesEachQuoteWithTheDataWindowEscape()
    {
        // `ReplaceAll(sql, "~"", "~~~"", true)` - in PowerScript that is: replace each `"` with the
        // two characters `~` and `"`.
        Assert.Equal(
            "SELECT ~\"A~\" FROM T",
            SqlQueryTask.EscapeStatementForModify("SELECT \"A\" FROM T"));

        // A statement with no quote is returned unchanged, which is why the conditional guard at
        // :L795 is net-equivalent to escaping unconditionally.
        Assert.Equal(SomeSelect, SqlQueryTask.EscapeStatementForModify(SomeSelect));
    }

    [Fact]
    public void BuildTableSelectAssignment_DoesNotEscapeOnItsCallersBehalf_PreservingDefectD9()
    {
        // DEFECT D9. The runtime substitution at :L635 does NOT escape while :L709, :L732 and :L795
        // all do, so the escape cannot be folded into the script builder - doing so would silently
        // repair a path the legacy leaves broken.
        Assert.Equal(
            "DataWindow.Table.Select = \"SELECT \"A\" FROM T\"",
            SqlQueryTask.BuildTableSelectAssignment("SELECT \"A\" FROM T"));
    }

    // ==========================================================================================
    //  SUITE 11 - DEFECT D6: THE QUERY-SIDE DEFENSIVE OVERRIDE  [:L771-L774]
    // ==========================================================================================

    [Fact]
    public void ApplyDefensiveRowCountOverride_OnSqlCodeExactlyMinusOne_RewritesAndResets()
    {
        // [:L771-L774] EXACTLY -1 on the code, `>= 0` on the count, AND the carrier is reset - which
        // the update-side override does not do.
        DataWindowBufferStore carrier = new();
        carrier.AppendRow(DwBuffer.Primary, ItemStatus.DataModified);
        Assert.Equal(1L, carrier.RowCount());

        Assert.Equal(-1L, SqlQueryTask.ApplyDefensiveRowCountOverride(-1L, 5L, carrier));
        Assert.Equal(0L, carrier.RowCount());
    }

    [Fact]
    public void ApplyDefensiveRowCountOverride_OnZeroRowsWithSqlCodeMinusOne_StillRewrites()
    {
        // The row-count test is `>= 0`, NOT `= 1` as on the update side - so a retrieval that
        // returned no rows successfully is still rewritten to a failure.
        DataWindowBufferStore carrier = new();

        Assert.Equal(-1L, SqlQueryTask.ApplyDefensiveRowCountOverride(-1L, 0L, carrier));
    }

    [Theory]
    [InlineData(0L, 5L)]
    [InlineData(1L, 5L)]
    [InlineData(100L, 5L)]
    [InlineData(-2L, 5L)]
    [InlineData(-99L, 5L)]
    public void ApplyDefensiveRowCountOverride_OnAnyOtherSqlCode_LeavesEverythingAlone(
        long sqlCode,
        long rowCount)
    {
        // The code test is EXACT EQUALITY. Widening it to `< 0` - which is what the transaction's own
        // failure predicate does - would rewrite a cancellation into a database error.
        DataWindowBufferStore carrier = new();
        carrier.AppendRow(DwBuffer.Primary, ItemStatus.DataModified);

        Assert.Equal(rowCount, SqlQueryTask.ApplyDefensiveRowCountOverride(sqlCode, rowCount, carrier));
        Assert.Equal(1L, carrier.RowCount());
    }

    [Fact]
    public void ApplyDefensiveRowCountOverride_OnAnAlreadyNegativeRowCount_LeavesItAlone()
    {
        // `nRowCnt >= 0` excludes a count that already reported failure, so the value is not
        // re-stamped and the carrier is not reset a second time.
        DataWindowBufferStore carrier = new();
        carrier.AppendRow(DwBuffer.Primary, ItemStatus.DataModified);

        Assert.Equal(-7L, SqlQueryTask.ApplyDefensiveRowCountOverride(-1L, -7L, carrier));
        Assert.Equal(1L, carrier.RowCount());
    }

    // ==========================================================================================
    //  SUITE 12 - THE CODEC DISCRIMINATOR  [:L93-L94]
    //  ------------------------------------------------------------------------------------------
    //  `choose case Long(Data.Describe("DataWindow.Processing")) / case 4,5` selects the FULL-STATE
    //  codec and everything else selects the CHANGESET codec. The task owns the CHOICE; neither codec
    //  is implemented here, so these cases assert on which sibling was driven, not on what it emitted.
    // ==========================================================================================

    [Theory]
    [InlineData(4L)]
    [InlineData(5L)]
    public async Task ProcessingFourAndFive_SelectTheFullStateCodec_AsASingleChunk(long processing)
    {
        // Crosstab (4) and composite (5) are the two kinds the legacy delivers WHOLE, as one
        // full-state chunk with one-based count 1 and index 1 and the selector TRUE [:L95-L101].
        using Harness harness = new();
        harness.Store.Carrier.Processing = new DataWindowProcessing(processing);
        harness.Store.RetrieveRows = 2L;
        _ = harness.Task.SetSql(SomeSelect);

        long result = await harness.Task
            .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, result);

        // ONE chunk, and it is the full-state one.
        (_, long chunkCount, long chunkIndex, bool fullState) = Assert.Single(harness.Sink.Chunks);
        Assert.Equal(1L, chunkCount);
        Assert.Equal(1L, chunkIndex);
        Assert.True(fullState);

        // The full-state arm runs NO create callback and NO child walk - it is a single shot.
        Assert.Empty(harness.Sink.CreatedFrom);
        Assert.Empty(harness.Sink.Children);

        Assert.Equal<string>(["rows:2", "fullstate:1/1", "page:-1/-1"], harness.Sink.Order);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    public async Task ProcessingZeroAndOne_SelectTheChangesetCodec(long processing)
    {
        // `case else` [:L102]. Eleven of the repository's twelve DataWindow definitions declare
        // processing=1 and the twelfth declares 0, so this is the arm the whole legacy corpus uses.
        using Harness harness = new();
        harness.Store.Carrier.Processing = new DataWindowProcessing(processing);
        harness.Store.RetrieveRows = 2L;
        _ = harness.Task.SetSql(SomeSelect);

        long result = await harness.Task
            .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, result);

        (_, long chunkCount, long chunkIndex, bool fullState) = Assert.Single(harness.Sink.Chunks);
        Assert.Equal(1L, chunkCount);
        Assert.Equal(1L, chunkIndex);

        // FALSE. The receiver cannot decode the payload without this flag, so it is not a hint.
        Assert.False(fullState);

        Assert.Equal<string>(["rows:2", "chunk:1/1", "page:-1/-1"], harness.Sink.Order);
    }

    // ==========================================================================================
    //  SUITE 13 - THE CHUNK ARITHMETIC AND THE EMPTY-RESULT ARM  [:L145, :L230]
    // ==========================================================================================

    [Fact]
    public async Task ChunkCountIsCeilingOfPrimaryPlusFilteredOverChunkSize()
    {
        // `nCount = Ceiling((Data.RowCount() + Data.FilteredCount()) / _nChunkSize)` [:L145]. THE
        // FILTERED ROWS COUNT TOWARDS THE ARITHMETIC even though they are not in the primary buffer,
        // which is why a naive RowCount()-only reading under-chunks a filtered result.
        //
        // The chunk size arrives through the CONFIGURED default rather than the setter, because the
        // setter's own guard rejects anything at or below 1000 (D1) and a 1001-row fixture would make
        // this case about allocation rather than about the arithmetic.
        using Harness harness = new(query: new QueryOptions { ChunkSize = 2 });
        harness.Store.RetrieveRows = 3L;
        harness.Store.RetrieveFilteredRows = 2L;
        _ = harness.Task.SetSql(SomeSelect);

        long result = await harness.Task
            .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, result);

        // Ceiling((3 + 2) / 2) = 3, and the indices are ONE-BASED and consecutive.
        Assert.Equal(3, harness.Sink.Chunks.Count);
        Assert.Equal<string>(
            ["rows:3", "chunk:1/3", "chunk:2/3", "chunk:3/3", "page:-1/-1"],
            harness.Sink.Order);
    }

    [Fact]
    public async Task AnEmptyResultEmitsExactlyOneEmptyChunkWithOneBasedCountAndIndex()
    {
        // `else tasking.Event OnDataChunk(ref blbData,1,1,false)` [:L229-L231]. The arithmetic answers
        // ZERO and the legacy still sends ONE chunk, numbered 1 of 1, carrying NOTHING - which is what
        // tells the receiving side to CLEAR rather than that there is nothing to do.
        using Harness harness = new();
        harness.Store.RetrieveRows = 0L;
        _ = harness.Task.SetSql(SomeSelect);

        long result = await harness.Task
            .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, result);

        (CarrierState? state, long chunkCount, long chunkIndex, bool fullState) =
            Assert.Single(harness.Sink.Chunks);

        Assert.Null(state);
        Assert.Equal(1L, chunkCount);
        Assert.Equal(1L, chunkIndex);
        Assert.False(fullState);

        // And the row count handed to the receiver is zero, not absent.
        Assert.Equal<long>([0L], harness.Sink.RowCounts);
    }

    [Fact]
    public async Task WhenTheReceiverNeedsCreatingTheCreateCallbackPrecedesEveryChunk()
    {
        // `if tasking._of_NeedCreate() then tasking.Event OnCreateData(Data.Describe("DataWindow.
        // Syntax"))` [:L105-L107], and then the filter-into-primary self-fold at [:L109]. The ORDER is
        // the contract: a receiver that has not been created cannot accept a chunk.
        using Harness harness = new(needsCreatedObject: true);
        harness.Store.RetrieveRows = 2L;
        _ = harness.Task.SetSql(SomeSelect);

        long result = await harness.Task
            .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, result);
        Assert.Equal<string>([FixtureSyntax], harness.Sink.CreatedFrom);
        Assert.Equal<string>(
            ["rows:2", "create:" + FixtureSyntax, "chunk:1/1", "page:-1/-1"],
            harness.Sink.Order);
    }

    // ==========================================================================================
    //  SUITE 14 - THE MAIN-THREAD HAND-OVER FAST PATH  [:L85-L91]
    //  ------------------------------------------------------------------------------------------
    //  `if of_IsMainThread() and Not tasking._of_HasReceiver() and Not _bCache then tasking.Event
    //  OnDataMove(data) : SetNull(data) : return RetCode.OK`. All THREE conditions, and no codec runs
    //  at all - the carrier is handed over BY REFERENCE and the sender drops its own reference.
    // ==========================================================================================

    [Fact]
    public async Task OnTheMainThreadWithNoReceiverAndNoCacheTheCarrierIsMovedAndNoCodecRuns()
    {
        using Harness harness = new(isMainThread: true, hasReceiver: false);
        harness.Store.RetrieveRows = 4L;
        _ = harness.Task.SetSql(SomeSelect);

        long result = await harness.Task
            .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, result);

        // THE SAME OBJECT, not a copy: this path exists precisely to avoid serializing anything.
        Assert.Same(harness.Store.Carrier, Assert.Single(harness.Sink.Moved));

        // NO codec ran, in either direction.
        Assert.Empty(harness.Sink.Chunks);
        Assert.Empty(harness.Sink.Children);
        Assert.Empty(harness.Sink.CreatedFrom);

        // The moved carrier keeps its rows - nothing reset or discarded them, because the hand-over
        // path deliberately runs no codec and the teardown must not reset a carrier it gave away.
        Assert.Equal(4L, harness.Store.Carrier.RowCount());

        Assert.Equal<string>(["rows:4", "move", "page:-1/-1"], harness.Sink.Order);
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    public async Task TheHandOverPathIsRefusedUnlessAllThreeConditionsHold(
        bool isMainThread,
        bool hasReceiver,
        bool cache)
    {
        // Any one of the three failing sends the result through a codec instead. The worker-thread
        // case is the important one: a worker carrier cannot be handed to the main thread by
        // reference at all, which is hazard 1 of docs/PB多线程绕坑提示.md.
        using Harness harness = new(isMainThread: isMainThread, hasReceiver: hasReceiver);
        harness.Store.RetrieveRows = 2L;
        _ = harness.Task.SetSql(SomeSelect);
        _ = harness.Task.SetCache(cache);

        long result = await harness.Task
            .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, result);
        Assert.Empty(harness.Sink.Moved);
        Assert.Single(harness.Sink.Chunks);
    }

    // ==========================================================================================
    //  SUITE 15 - THE RETRIEVE VETO, AND ITS TWO-WAY DISCRIMINATION  [:L746-L753]
    //  ------------------------------------------------------------------------------------------
    //  `if IsPrevented(TransObject.Event OnBeforeRetrieve(data)) then` splits on the TRANSACTION's own
    //  failure predicate: a clean veto is a CANCELLATION, a veto whose transaction has failed is a
    //  DATABASE ERROR. Collapsing the two - in either direction - loses the distinction the update
    //  path makes the same way.
    // ==========================================================================================

    [Fact]
    public async Task ACleanVetoIsACancellation()
    {
        using Harness harness = new();
        harness.TransactionSurface.BeforeRetrieveResult = RetCode.PREVENT;
        _ = harness.Task.SetSql(SomeSelect);

        long result = await harness.Task
            .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.CANCELLED, result);

        // NOTHING is reported: a cancellation is not a fault, so neither the db-error channel nor the
        // general error channel is touched.
        Assert.Empty(harness.Proxy.Errors);
        Assert.Empty(harness.Host.Errors);

        // And the retrieval never ran.
        Assert.Empty(harness.Store.Retrievals);
        Assert.Empty(harness.Sink.Order);
    }

    [Fact]
    public async Task AVetoWhoseTransactionHasFailedIsADatabaseError()
    {
        // `Event OnDBError(TransObject.SQLDBCode,TransObject.SQLErrText,"",Primary!,0)` then
        // `Event OnError(RetCode.E_DB_ERROR,"")` [:L748-L750]. THE STATEMENT IS EMPTY and THE ROW IS
        // ZERO here - contrast the count site, which passes the statement and row 1.
        using Harness harness = new();
        harness.Activator.SqlFailed = true;
        harness.Activator.SqlDbCode = ProbeDbCode;
        harness.Activator.SqlErrText = ProbeDbErrorText;
        harness.TransactionSurface.BeforeRetrieveResult = RetCode.PREVENT;
        _ = harness.Task.SetSql(SomeSelect);

        long result = await harness.Task
            .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.E_DB_ERROR, result);

        DbErrorData reported = Assert.Single(harness.Proxy.Errors);
        Assert.Equal(ProbeDbCode, reported.SqlDbCode);
        Assert.Equal(ProbeDbErrorText, reported.SqlErrText);
        Assert.Equal(string.Empty, reported.SqlSyntax);
        Assert.Equal(DwBuffer.Primary, reported.Buffer);
        Assert.Equal(0L, reported.Row);

        // The general error carries an EMPTY text on this arm, deliberately: the db-error payload above
        // is the diagnostic, and duplicating it here would double-report one fault.
        Assert.Equal<(long, string)>([(RetCode.E_DB_ERROR, string.Empty)], harness.Host.Errors);
    }

    // ==========================================================================================
    //  SUITE 16 - THE HOOK, AND WHAT "DECLINED" MEANS  [:L755-L767]
    //  ------------------------------------------------------------------------------------------
    //  `if IsNull(nRowCnt) or nRowCnt = RetCode.E_NO_IMPLEMENTATION then` run the default retrieval.
    //  E_NO_IMPLEMENTATION FROM A HOOK IS NOT AN ERROR - it means "not handled, use the default", and
    //  treating it as a failure would break every hook that only handles some data objects.
    // ==========================================================================================

    [Fact]
    public async Task NoHookRunsTheDefaultRetrieval()
    {
        using Harness harness = new();
        harness.Store.RetrieveRows = 3L;
        _ = harness.Task.SetSql(SomeSelect);

        long result = await harness.Task
            .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, result);

        // Plain SQL retrieves with NO parameters - `data.Retrieve()` [:L762] - not through the
        // parameterised path.
        Assert.Empty(Assert.Single(harness.Store.Retrievals));
        Assert.Equal<long>([3L], harness.Sink.RowCounts);
    }

    [Fact]
    public async Task AHookThatDeclinesWithNoImplementationStillRunsTheDefaultRetrieval()
    {
        using Harness harness = new();
        harness.Store.RetrieveRows = 3L;
        DisposableProbeHook hook = new(RetCode.E_NO_IMPLEMENTATION);
        _ = harness.HookActivator.Register(ProbeHookClass, () => hook);
        _ = harness.Task.SetHookClass(ProbeHookClass);
        _ = harness.Task.SetSql(SomeSelect);

        long result = await harness.Task
            .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, result);
        Assert.Equal(1, hook.Invocations);

        // THE DEFAULT RAN ANYWAY. That is the whole contract of the decline sentinel.
        Assert.Single(harness.Store.Retrievals);
        Assert.Equal<long>([3L], harness.Sink.RowCounts);

        // And the hook was released deterministically rather than left to the collector, honouring
        // hazard 2 of docs/PB多线程绕坑提示.md.
        Assert.True(hook.Disposed);
    }

    [Fact]
    public async Task AHookThatHandlesTheRetrievalSuppressesTheDefaultAndOwnsTheRowCount()
    {
        // Any answer that is neither null nor the decline sentinel is the ROW COUNT, and the default
        // retrieval is skipped entirely [:L757-L767].
        using Harness harness = new();
        harness.Store.RetrieveRows = 3L;
        DisposableProbeHook hook = new(7L);
        _ = harness.HookActivator.Register(ProbeHookClass, () => hook);
        _ = harness.Task.SetHookClass(ProbeHookClass);
        _ = harness.Task.SetSql(SomeSelect);

        long result = await harness.Task
            .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, result);
        Assert.Empty(harness.Store.Retrievals);

        // The hook's answer reaches BOTH the after-retrieve notification and the receiver.
        Assert.Equal<long>([7L], harness.TransactionSurface.AfterRetrieveRowCounts);
        Assert.Equal<long>([7L], harness.Sink.RowCounts);
        Assert.True(hook.Disposed);
    }

    // ==========================================================================================
    //  SUITE 17 - DEFECT D11 AND THE TEARDOWN'S CACHED-CARRIER ASYMMETRY  [:L870-L881]
    // ==========================================================================================

    [Fact]
    public async Task WhenTheTransactionCannotBeResolvedTheHookIsStillReleased()
    {
        // ======================================================================================
        //  DEFECT D11, FOUND BY DIRECT SOURCE READING. The hook is created at [:L517], the
        //  invalid-transaction return sits at [:L523], and the `try` does not open until [:L526] -
        //  so `Destroy hook` at [:L873] is NEVER REACHED on that one path and the ORACLE LEAKS THE
        //  HOOK there.
        //
        //  This port releases it anyway, and the deviation is deliberate and documented: a managed
        //  leak has no observable behaviour to preserve, whereas leaving an IDisposable unreleased
        //  is a real defect in a long-lived service process. The behaviour a caller can observe -
        //  the return code, the db-error payload and the general error - is unchanged.
        // ======================================================================================
        using Harness harness = new();
        harness.Activator.ConnectFails = true;
        DisposableProbeHook hook = new(RetCode.E_NO_IMPLEMENTATION);
        _ = harness.HookActivator.Register(ProbeHookClass, () => hook);
        _ = harness.Task.SetHookClass(ProbeHookClass);
        _ = harness.Task.SetSql(SomeSelect);

        long result = await harness.Task
            .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.E_INVALID_TRANSACTION, result);

        // The hook was created - so the leak the oracle has is genuinely reachable - and released.
        Assert.Equal(0, hook.Invocations);
        Assert.True(hook.Disposed);

        // The connect failure's own code and text are carried, with an EMPTY statement and row 0.
        DbErrorData reported = Assert.Single(harness.Proxy.Errors);
        Assert.Equal(FakeQueryTransaction.ConnectFailureCode, reported.SqlDbCode);
        Assert.Equal(FakeQueryTransaction.ConnectFailureText, reported.SqlErrText);
        Assert.Equal(string.Empty, reported.SqlSyntax);
        Assert.Equal(0L, reported.Row);

        Assert.Equal<(long, string)>(
            [(RetCode.E_INVALID_TRANSACTION, string.Empty)],
            harness.Host.Errors);

        // Nothing was retrieved and nothing was published.
        Assert.Empty(harness.Store.Retrievals);
        Assert.Empty(harness.Sink.Order);
    }

    [Fact]
    public async Task ACachedCarrierIsResetOnTeardownAndNeverDropped()
    {
        // `if bCacheDS then data.Reset() else if IsValid(data) then Destroy data` [:L878-L881]. NEVER
        // destroy a cached carrier: the cache the base owns holds a live reference to it, so dropping
        // it would leave a dangling entry that the next retrieval would resolve and then fail on.
        //
        // The veto short-circuits before any codec runs, which is what makes the teardown's own effect
        // observable - a completed changeset transfer empties the source itself.
        using Harness harness = new();
        harness.TransactionSurface.BeforeRetrieveResult = RetCode.PREVENT;
        SeedRows(harness.Store.Carrier, 3L);
        _ = harness.Task.SetDataObject(SomeDataObject);
        _ = harness.Task.SetCache(true);

        long result = await harness.Task
            .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.CANCELLED, result);

        // RESET, not dropped: the rows are gone but the object the cache holds is the same one.
        Assert.Equal(0L, harness.Store.Carrier.RowCount());
        Assert.Same(harness.Store, harness.StoreFactory.Created);
    }

    [Fact]
    public async Task AnUncachedCarrierIsNotResetOnTeardown()
    {
        // The other half of the same `if`. An uncached carrier's reference is DROPPED - which in .NET
        // is simply going out of scope - and it is deliberately NOT reset, because the receiver may
        // still be reading the object the sender handed over.
        using Harness harness = new();
        harness.TransactionSurface.BeforeRetrieveResult = RetCode.PREVENT;
        SeedRows(harness.Store.Carrier, 3L);
        _ = harness.Task.SetSql(SomeSelect);

        long result = await harness.Task
            .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.CANCELLED, result);
        Assert.Equal(3L, harness.Store.Carrier.RowCount());
    }

    // ==========================================================================================
    //  SUITE 18 - PAGE COUNTING END TO END, INCLUDING DEFECT D7  [:L818-L862]
    // ==========================================================================================

    [Fact]
    public async Task ASuccessfulCountQueryEmitsTheByteExactWrapperAndTheCeilingPageCount()
    {
        // The full paged path: the wrapper is built from the PRE-PAGING statement (`sSQLExec`), which
        // is why a count over a paged retrieval counts the whole result set rather than one page.
        using Harness harness = new();
        harness.Store.RetrieveRows = 3L;
        harness.TransactionSurface.CountValue = 7L;
        _ = harness.Task.SetSql(SomeSelect);
        _ = harness.Task.SetPaged(true);
        _ = harness.Task.SetPageSize(2L);
        _ = harness.Task.SetPageIndex(1L);

        long result = await harness.Task
            .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, result);

        Assert.Equal(
            "SELECT COUNT(1) AS CNT FROM (SELECT 1 AS _ FROM COMPANY) pfwPagedSQL_Tbl",
            Assert.Single(harness.TransactionSurface.CountStatements));

        // Ceiling(7 / 2) = 4.
        Assert.Equal<(long, long)>([(4L, 7L)], harness.Sink.PageCounts);
    }

    [Fact]
    public async Task APartialFinalPageIsCountedArithmeticallyWithNoCountQueryAtAll()
    {
        // `if (nRowCnt > 0 and nRowCnt < _nPageSize) ...` [:L819-L823]. THE COUNT QUERY IS SKIPPED
        // ENTIRELY - which is the whole point of the short-circuit, and is observable here as an
        // EMPTY statement list rather than only as an arithmetic result.
        using Harness harness = new();
        harness.Store.RetrieveRows = 3L;
        _ = harness.Task.SetSql(SomeSelect);
        _ = harness.Task.SetPaged(true);
        _ = harness.Task.SetPageSize(10L);
        _ = harness.Task.SetPageIndex(4L);

        long result = await harness.Task
            .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, result);
        Assert.Empty(harness.TransactionSurface.CountStatements);

        // pageCount = pageIndex = 4; recordCount = (4 - 1) * 10 + 3 = 33.
        Assert.Equal<(long, long)>([(4L, 33L)], harness.Sink.PageCounts);
    }

    [Fact]
    public async Task ACancelledCountQueryIsReportedAsADatabaseError_PreservedLegacyDefect()
    {
        // ======================================================================================
        //  DEFECT D7, FOUND BY DIRECT SOURCE READING. `if IsFailed(rtCode) then if rtCode <>
        //  RetCode.CANCELLED then Event OnError(...) : return rtCode` [:L844-L848] has an
        //  UNREACHABLE inner guard, because IsFailed ALREADY excludes CANCELLED
        //  [ws_objects/pfw.shared.pbl.src/isfailed.srf:L11-L13].
        //
        //  The consequence is not cosmetic. A CANCELLED count result does not enter the failure arm
        //  at all: it falls through to `if rtCode = 1` [:L850], fails that too, and lands on the
        //  DATABASE-ERROR arm carrying 检索失败 [:L853-L855]. So a cancellation is reported as a
        //  database fault, and C-B forbids repairing it.
        // ======================================================================================
        using Harness harness = new();
        harness.Store.RetrieveRows = 3L;
        harness.Activator.SqlDbCode = ProbeDbCode;
        harness.Activator.SqlErrText = ProbeDbErrorText;
        harness.TransactionSurface.CountReturnCode = RetCode.CANCELLED;
        _ = harness.Task.SetSql(SomeSelect);
        _ = harness.Task.SetPaged(true);
        _ = harness.Task.SetPageSize(2L);
        _ = harness.Task.SetPageIndex(1L);

        long result = await harness.Task
            .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken);

        // NOT RetCode.CANCELLED. Do not "correct" this.
        Assert.Equal(RetCode.E_DB_ERROR, result);

        Assert.Equal<(long, string)>(
            [(RetCode.E_DB_ERROR, SqlQueryTask.CountRetrieveFailedText)],
            harness.Host.Errors);

        // ROW 1 at this site, not row 0 as at the two transaction-failure sites, and the statement IS
        // carried here.
        DbErrorData reported = Assert.Single(harness.Proxy.Errors);
        Assert.Equal(1L, reported.Row);
        Assert.Contains("pfwPagedSQL_Tbl", reported.SqlSyntax, StringComparison.Ordinal);

        // The chunks were already published before the count ran, so a count failure does not unsend
        // them - and no page notification is issued at all.
        Assert.Single(harness.Sink.Chunks);
        Assert.Empty(harness.Sink.PageCounts);
    }

    [Fact]
    public async Task ACountQueryThatFailsOutrightReportsItsOwnDiagnosticText()
    {
        // The reachable half of the same block: a genuinely failed result DOES enter the IsFailed arm
        // and reports the transaction surface's own text rather than 检索失败.
        using Harness harness = new();
        harness.Store.RetrieveRows = 3L;
        harness.TransactionSurface.CountReturnCode = RetCode.E_DB_ERROR;
        harness.TransactionSurface.CountErrorText = ProbeDbErrorText;
        _ = harness.Task.SetSql(SomeSelect);
        _ = harness.Task.SetPaged(true);
        _ = harness.Task.SetPageSize(2L);
        _ = harness.Task.SetPageIndex(1L);

        long result = await harness.Task
            .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.E_DB_ERROR, result);
        Assert.Equal<(long, string)>([(RetCode.E_DB_ERROR, ProbeDbErrorText)], harness.Host.Errors);

        // NO db-error payload on this arm - only the general error channel is used.
        Assert.Empty(harness.Proxy.Errors);
        Assert.Empty(harness.Sink.PageCounts);
    }

    [Fact]
    public async Task WithPageCountingOffBothValuesAreMinusOne()
    {
        // `else nPageCount = -1 : nRecordCount = -1` [:L861-L862]. A SENTINEL, not a count: -1 means
        // "not counted", and a consumer that treats it as a number reports a negative page count.
        using Harness harness = new();
        harness.Store.RetrieveRows = 3L;
        _ = harness.Task.SetSql(SomeSelect);
        _ = harness.Task.SetPaged(true);
        _ = harness.Task.SetPageSize(2L);
        _ = harness.Task.SetPageIndex(1L);
        _ = harness.Task.SetPageCounting(false);

        long result = await harness.Task
            .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, result);
        Assert.Empty(harness.TransactionSurface.CountStatements);
        Assert.Equal<(long, long)>([(-1L, -1L)], harness.Sink.PageCounts);
    }

    // ==========================================================================================
    //  SUITE 19 - THE MAX-ROWS CAP  [:L743-L744, :L787-L790]
    // ==========================================================================================

    [Fact]
    public async Task ExceedingTheRowCapReportsOutOfRangeWithTheCapInTheMessage()
    {
        // `if data.of_IsRowsExceeded() then Event OnError(RetCode.E_OUT_OF_RANGE,"超出最大允许的行数("
        // + String(_nMaxRows) + ")!")` [:L787-L790]. The cap is installed on the carrier AFTER
        // ClearState [:L743-L744], so a previous run's cap can never leak into this one.
        using Harness harness = new();
        harness.Store.RetrieveRows = 5L;
        _ = harness.Task.SetSql(SomeSelect);
        _ = harness.Task.SetMaxRows(2L);

        long result = await harness.Task
            .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.E_OUT_OF_RANGE, result);
        Assert.Equal<(long, string)>(
            [(RetCode.E_OUT_OF_RANGE, "超出最大允许的行数(2)!")],
            harness.Host.Errors);

        // The cap stopped the retrieve at the row AFTER the last permitted one, so exactly two rows
        // survived, and nothing was published.
        Assert.Equal(2L, harness.Store.Carrier.RowCount());
        Assert.Empty(harness.Sink.Order);
    }

    // ==========================================================================================
    //  SUITE 20 - THE DDDW CHILD WALK AND THE CACHED-PATH DEDUPLICATION  [:L113-L141, :L633-L642]
    // ==========================================================================================

    [Fact]
    public async Task EveryEligibleDropDownChildIsTransferredInColumnOrder()
    {
        // `for nIndex = 1 to nCount` over `#<n>.DDDW.AutoRetrieve` = "yes" whose `#<n>.DDDW.Name` is
        // neither "!" nor "?" [:L113-L120]. THE ORDER IS THE COLUMN ORDER, and it is one-based.
        using Harness harness = new();
        harness.Store.RetrieveRows = 1L;
        harness.Store.DefineDropDownColumn(1, "dept_code", "d_dept");
        harness.Store.DefineDropDownColumn(2, "city_code", "d_city");
        harness.Runtime.DefineChild("dept_code", NewChildCarrier());
        harness.Runtime.DefineChild("city_code", NewChildCarrier());
        _ = harness.Task.SetSql(SomeSelect);

        long result = await harness.Task
            .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, result);
        Assert.Equal<string>(
            ["dept_code", "city_code"],
            [.. harness.Sink.Children.Select(child => child.ColumnName)]);

        // CHILDREN FIRST, THEN THE CHUNKS. A receiver that applied a chunk before its drop-down child
        // existed would resolve every display value against an empty child.
        Assert.Equal<string>(
            ["rows:1", "child:dept_code", "child:city_code", "chunk:1/1", "page:-1/-1"],
            harness.Sink.Order);
    }

    [Theory]
    [InlineData("!")]
    [InlineData("?")]
    public async Task AColumnWhoseDropDownNameIsASentinelIsNotAChild(string dropDownName)
    {
        // `and data.Describe("#" + String(nIndex) + ".DDDW.Name") <> "!" and ... <> "?"` [:L115-L116].
        // "!" is PowerBuilder's unreadable marker and "?" its inconsistent one; neither names a real
        // child, and treating either as one would ask the runtime for an object that does not exist.
        using Harness harness = new();
        harness.Store.RetrieveRows = 1L;
        harness.Store.DefineDropDownColumn(1, "dept_code", dropDownName);
        harness.Runtime.DefineChild("dept_code", NewChildCarrier());
        _ = harness.Task.SetSql(SomeSelect);

        long result = await harness.Task
            .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, result);
        Assert.Empty(harness.Sink.Children);
    }

    [Fact]
    public async Task OnTheCachedPathASharedDropDownSourceIsRetrievedOnceAndTheRestAreSwitchedOff()
    {
        // The deduplication at [:L633-L642] keys on the DROP-DOWN SOURCE NAME, not on the column name,
        // because two columns sharing one source need only one child query. The losers get
        // `AutoRetrieve = no` appended to a single accumulated modify script.
        using Harness harness = new();
        harness.Store.DefineDropDownColumn(1, "ship_city", "d_city");
        harness.Store.DefineDropDownColumn(2, "bill_city", "d_city");
        _ = harness.Task.SetDataObject(SomeDataObject);
        _ = harness.Task.SetCache(true);

        long result = await harness.Task
            .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, result);

        // Exactly one notification, for the FIRST column carrying the shared source.
        (long notifyCode, long payload, string text) = Assert.Single(harness.Host.Notifications);
        Assert.Equal((long)SqlQueryTaskNotifyCode.ChildQuery, notifyCode);
        Assert.Equal(1L, payload);
        Assert.Equal("ship_city", text);

        // And the second column - not the first - was switched off.
        Assert.Contains(
            harness.Store.ModifyScripts,
            script => script.Contains("#2.DDDW.AutoRetrieve = no", StringComparison.Ordinal)
                && !script.Contains("#1.DDDW.AutoRetrieve = no", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OnTheCachedPathAVetoedChildQueryIsSwitchedOffToo()
    {
        // `if #ParentTask.Event OnNotify(...) = 1 then` [:L637]. EXACT EQUALITY WITH 1, not the
        // tri-valued prevention predicate - so PREVENT_DEEP (2) does NOT switch the column off.
        using Harness harness = new();
        harness.Store.DefineDropDownColumn(1, "dept_code", "d_dept");
        harness.Store.DefineDropDownColumn(2, "city_code", "d_city");
        harness.Host.OnNotifyHandler = (_, payload, _) => payload == 1L ? 1L : 2L;
        _ = harness.Task.SetDataObject(SomeDataObject);
        _ = harness.Task.SetCache(true);

        long result = await harness.Task
            .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, result);

        string script = Assert.Single(
            harness.Store.ModifyScripts,
            candidate => candidate.Contains(".DDDW.AutoRetrieve = no", StringComparison.Ordinal));

        // Column 1 answered 1 and is off; column 2 answered 2 - a deep prevention - and is NOT.
        Assert.Contains("#1.DDDW.AutoRetrieve = no", script, StringComparison.Ordinal);
        Assert.DoesNotContain("#2.DDDW.AutoRetrieve = no", script, StringComparison.Ordinal);
    }

    // ==========================================================================================
    //  SUITE 21 - THE STRUCTURAL GUARDS AROUND THE RUN
    // ==========================================================================================

    [Fact]
    public async Task AnUnresolvableDataObjectIsRefusedWithTheLegacyText()
    {
        // `if data.Describe("DataWindow.Units") = "" then Event OnError(RetCode.E_INVALID_ARGUMENT,
        // "无效的DataObject")` [:L555-L560]. An EMPTY describe answer, not "!" - PowerBuilder answers
        // empty for a data object that does not exist at all.
        using Harness harness = new();
        harness.Store.Properties[DataWindowProperty.Units] = string.Empty;
        _ = harness.Task.SetDataObject(SomeDataObject);

        long result = await harness.Task
            .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, result);
        Assert.Equal<(long, string)>(
            [(RetCode.E_INVALID_ARGUMENT, SqlQueryTask.InvalidDataObjectText)],
            harness.Host.Errors);
    }

    [Fact]
    public async Task WithNeitherASyntaxNorAStatementTheRunIsRefusedAsEmptySql()
    {
        // `Event OnError(RetCode.E_INVALID_SQL,"SQL为空!")` [:L621-L623].
        using Harness harness = new();

        long result = await harness.Task
            .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.E_INVALID_SQL, result);
        Assert.Equal<(long, string)>(
            [(RetCode.E_INVALID_SQL, SqlQueryTask.EmptySqlText)],
            harness.Host.Errors);
    }

    [Fact]
    public async Task AFailedSyntaxDerivationCarriesTheProviderTextBehindItsLegacyPrefix()
    {
        // `Event OnError(RetCode.E_INVALID_SQL,"GridSyntaxFromSQL: " + sError)` [:L613-L616].
        using Harness harness = new();
        harness.TransactionSurface.SyntaxError = ProbeDbErrorText;
        _ = harness.Task.SetSql(SomeSelect);

        long result = await harness.Task
            .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.E_INVALID_SQL, result);
        Assert.Equal<(long, string)>(
            [(RetCode.E_INVALID_SQL, SqlQueryTask.GridSyntaxFailurePrefix + ProbeDbErrorText)],
            harness.Host.Errors);
    }

    [Fact]
    public async Task AFailedTransactionAttachmentIsRefusedWithTheLegacyText()
    {
        // `if data.SetTransObject(TransObject) <> 1 then Event OnError(RetCode.E_INVALID_TRANSACTION,
        // "设置事务对象失败!")` [:L680-L683].
        using Harness harness = new();
        harness.Runtime.AttachResult = DataWindowBufferStore.DataStoreFailure;
        _ = harness.Task.SetSql(SomeSelect);

        long result = await harness.Task
            .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.E_INVALID_TRANSACTION, result);
        Assert.Equal<(long, string)>(
            [(RetCode.E_INVALID_TRANSACTION, SqlQueryTask.SetTransObjectFailedText)],
            harness.Host.Errors);
    }

    [Fact]
    public async Task ANegativeRowCountFromAPlainStatementIsADatabaseError()
    {
        // `Event OnError(RetCode.E_DB_ERROR,"检索失败!")` for plain SQL [:L777-L781]. Note the
        // TRAILING EXCLAMATION MARK, which the data-object variant does not have.
        using Harness harness = new();
        harness.Store.RetrieveResult = -1L;
        _ = harness.Task.SetSql(SomeSelect);

        long result = await harness.Task
            .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.E_DB_ERROR, result);
        Assert.Equal<(long, string)>(
            [(RetCode.E_DB_ERROR, SqlQueryTask.PlainSqlRetrieveFailedText)],
            harness.Host.Errors);
    }

    [Fact]
    public async Task ANegativeRowCountFromANamedDataObjectNamesTheDataObject()
    {
        // `Event OnError(RetCode.E_DB_ERROR,data.DataObject + "检索失败,请检查参数传递是否正确!")`
        // [:L783-L785]. The data object's NAME is part of the diagnostic.
        using Harness harness = new();
        harness.Store.RetrieveResult = -1L;
        _ = harness.Task.SetDataObject(SomeDataObject);

        long result = await harness.Task
            .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.E_DB_ERROR, result);
        Assert.Equal<(long, string)>(
            [(RetCode.E_DB_ERROR, SomeDataObject + SqlQueryTask.DataObjectRetrieveFailedSuffix)],
            harness.Host.Errors);
    }

    [Fact]
    public async Task AConcurrentRunIsRefusedAsBusyWithoutDisturbingTheFirst()
    {
        // The `#Running` latch is the one live guard in the SQL task layer, and it protects the run as
        // well as the reset: two concurrent retrievals would share one carrier.
        using Harness harness = new();
        harness.Store.RetrieveRows = 1L;
        long reentrantResult = RetCode.OK;

        harness.Sink.OnDataReceivedCallback = () =>
            reentrantResult = harness.Task
                .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken)
                .GetAwaiter()
                .GetResult();

        _ = harness.Task.SetSql(SomeSelect);

        long result = await harness.Task
            .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, result);
        Assert.Equal(RetCode.E_BUSY, reentrantResult);

        // ONE retrieval happened, not two.
        Assert.Single(harness.Store.Retrievals);
    }

    [Fact]
    public async Task ACancelledHostShortCircuitsBeforeAnythingIsResolved()
    {
        // `if of_IsCancelled() then return RetCode.CANCELLED` [:L528] - the first statement inside the
        // try, so a task cancelled before it starts resolves no data object and opens no carrier.
        using Harness harness = new(isCancelled: true);
        _ = harness.Task.SetSql(SomeSelect);

        long result = await harness.Task
            .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.CANCELLED, result);
        Assert.Empty(harness.Store.Retrievals);
        Assert.Empty(harness.Host.Errors);
    }

    [Fact]
    public async Task ExecuteAsyncRejectsANullSink()
    {
        // Structural, not behavioural: the legacy's `tasking` reference cannot be null because the
        // proxy creates the task, whereas a DI-resolved caller can pass anything.
        using Harness harness = new();

        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            () => harness.Task.ExecuteAsync(null!, TestContext.Current.CancellationToken));
    }


    // ==========================================================================================
    //  THE TEST DOUBLES
    //  ------------------------------------------------------------------------------------------
    //  Nine hand-written doubles, no mocking package, and NOTHING that touches a database, a socket,
    //  a DataWindow runtime, a real thread or the wall clock (C-H). Every one of them is the narrowest
    //  thing that satisfies its interface, because a double with behaviour of its own would move the
    //  assertions off the subject.
    //
    //  The one deliberate exception to "narrowest possible": the store double drives the REAL carrier
    //  through OnRetrieveStart / OnRetrieveRow rather than appending rows behind the carrier's back,
    //  because the row cap of [:L64-L70] is enforced INSIDE the carrier and a double that bypassed it
    //  would make Suite 19 assert against itself.
    // ==========================================================================================

    /// <summary>
    /// Everything the subject needs, wired once. The clock is fixed and nothing advances it, which is
    /// the point: the subject reads no clock at all, and the only real delay in the whole file is the
    /// changeset codec's own inter-chunk yield.
    /// </summary>
    private sealed class Harness : IDisposable
    {
        internal Harness(
            bool isMainThread = false,
            bool isCancelled = false,
            bool hasReceiver = true,
            bool needsCreatedObject = false,
            QueryOptions? query = null,
            ILogger<SqlQueryTask>? logger = null)
        {
            Proxy = new RecordingTaskProxy();

            Host = new FakeQueryHost
            {
                IsMainThread = isMainThread,
                IsCancelled = isCancelled,
                ParentTasking = Proxy,
            };

            Configuration = new PersistenceOptions();
            if (query is not null)
            {
                Configuration.Query = query;
            }

            IOptions<PersistenceOptions> accessor = Options.Create(Configuration);

            Activator = new FakeTransactionActivator();
            Pool = new TransactionPool(accessor, FixedClock.Instance, Activator);

            // The affinity MATCHES the host, exactly as CreateDataStore() derives it [:L554-L556]: a
            // main-thread task gets the base carrier and a worker task gets the `_mt` one.
            Store = new QueryDataStore(
                DataWindowCarrierFactory.CreateForThread(isMainThread, FixedClock.Instance));

            StoreFactory = new SingleStoreFactory(Store);
            HookActivator = new SqlRetrievalHookActivator();
            TransactionSurface = new FakeQueryTransactionSurface();
            Runtime = new FakeQueryRuntime();

            Sink = new RecordingQuerySink
            {
                HasReceiver = hasReceiver,
                NeedsCreatedObject = needsCreatedObject,
            };

            // TimeProvider.System for the CODEC and the fixed clock for the TASK. The codec's yield is
            // a real Task.Delay, so a stopped clock would hang it; the task reads no clock at all, so a
            // stopped one there proves it.
            Task = new SqlQueryTask(
                Host,
                Pool,
                StoreFactory,
                HookActivator,
                FixedClock.Instance,
                logger ?? NullLogger<SqlQueryTask>.Instance,
                TransactionSurface,
                Runtime,
                [new SqlServerPagingRewriter(), new OraclePagingRewriter()],
                new ChangesetCodec(TimeProvider.System),
                SqlRedactor.Instance,
                accessor);
        }

        internal FakeQueryHost Host { get; }

        internal RecordingTaskProxy Proxy { get; }

        internal PersistenceOptions Configuration { get; }

        internal FakeTransactionActivator Activator { get; }

        internal TransactionPool Pool { get; }

        internal QueryDataStore Store { get; }

        internal SingleStoreFactory StoreFactory { get; }

        internal SqlRetrievalHookActivator HookActivator { get; }

        internal FakeQueryTransactionSurface TransactionSurface { get; }

        internal FakeQueryRuntime Runtime { get; }

        internal RecordingQuerySink Sink { get; }

        internal SqlQueryTask Task { get; }

        public void Dispose()
        {
            Task.Dispose();
            Pool.Dispose();
        }
    }

    /// <summary>
    /// A hand-driven threading substrate: no thread, no synchronization primitive, no message pump.
    /// It records the two channels the subject reports through so a test can assert on the exact
    /// diagnostic rather than only on the return code.
    /// </summary>
    private sealed class FakeQueryHost : ISqlTaskHost
    {
        private readonly Dictionary<string, object?> _data = new(StringComparer.Ordinal);

        internal List<(long Code, string Info)> Errors { get; } = [];

        internal List<(long NotifyCode, long Payload, string Text)> Notifications { get; } = [];

        internal int UninitCalls { get; private set; }

        /// <summary>Answer for the notify channel, keyed on the payload so per-column cases are legible.</summary>
        internal Func<long, long, string, long>? OnNotifyHandler { get; set; }

        public bool IsMainThread { get; init; }

        /// <summary>Settable, so a test can cancel a task between two of its own steps.</summary>
        public bool IsCancelled { get; set; }

        public int TaskIndex { get; init; } = 1;

        public ISqlTaskProxy? ParentTasking { get; init; }

        /// <summary>No sibling task is registered: this file exercises one task at a time.</summary>
        public long GetTask(int index, out SqlTaskBase? task)
        {
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

        public long OnPrepare() => DataWindowBufferStore.EventContinue;

        public void OnUninit() => UninitCalls++;

        public long OnError(long errCode, string errInfo)
        {
            Errors.Add((errCode, errInfo));
            return DataWindowBufferStore.EventContinue;
        }

        public long OnNotify(long notifyCode, long payload, string text)
        {
            Notifications.Add((notifyCode, payload, text));

            return OnNotifyHandler?.Invoke(notifyCode, payload, text)
                ?? DataWindowBufferStore.EventContinue;
        }
    }

    /// <summary>The caller-side proxy double: it latches every db-error payload verbatim.</summary>
    private sealed class RecordingTaskProxy : ISqlTaskProxy
    {
        internal List<DbErrorData> Errors { get; } = [];

        public void OnDbError(in DbErrorData error) => Errors.Add(error);
    }

    /// <summary>
    /// Hands out the SAME store on every call, so a test can configure it before the run and inspect
    /// it afterwards. The subject creates a store at most once per run, so sharing one is faithful.
    /// </summary>
    private sealed class SingleStoreFactory : ISqlDataStoreFactory
    {
        private readonly QueryDataStore _store;

        internal SingleStoreFactory(QueryDataStore store) => _store = store;

        internal QueryDataStore Created => _store;

        internal List<CarrierThreadAffinity> RequestedAffinities { get; } = [];

        public ISqlDataStore Create(CarrierThreadAffinity affinity)
        {
            RequestedAffinities.Add(affinity);
            return _store;
        }
    }

    /// <summary>
    /// The DataWindow-shaped store double. It owns a REAL carrier - so buffers, item statuses and the
    /// row cap all behave exactly as production - and answers describes from a dictionary whose default
    /// is PowerBuilder's unreadable marker, which is what a real describe answers for a property the
    /// object does not have.
    /// </summary>
    private sealed class QueryDataStore : ISqlDataStore
    {
        private readonly Dictionary<string, string> _properties = new(StringComparer.Ordinal);

        internal QueryDataStore(DataWindowCarrier carrier)
        {
            Carrier = carrier;

            // "!" for the procedure property, which is what makes bIsProcedure FALSE [:L626]. The sense
            // is INVERTED there - `<> "!"` means IS a procedure - so seeding this wrongly would
            // silently suppress clause modification, paging and page counting all at once.
            _properties[QueryDataWindowProperty.TableProcedure] =
                ChangesetSourceDefinition.DescribeUnreadable;

            _properties[DataWindowProperty.Units] = "0";
            _properties[QueryDataWindowProperty.Syntax] = FixtureSyntax;
            _properties[QueryDataWindowProperty.ColumnCount] = "0";
            _properties[DataWindowProperty.TableSort] = string.Empty;
            _properties[DataWindowProperty.TableFilter] = string.Empty;
            _properties[DataWindowProperty.TableArguments] = string.Empty;
        }

        public DataWindowCarrier Carrier { get; }

        public string DataObject { get; set; } = string.Empty;

        /// <summary>The statement the store reports, and the one a table-select modify rewrites.</summary>
        internal string SqlSelect { get; set; } = SomeSelect;

        /// <summary>Every describe answer, so a test can shape the object without a runtime.</summary>
        internal IDictionary<string, string> Properties => _properties;

        internal List<string> ModifyScripts { get; } = [];

        internal List<string> SortWrites { get; } = [];

        internal List<string> FilterWrites { get; } = [];

        internal List<IReadOnlyList<object?>> Retrievals { get; } = [];

        internal int ClearStateCalls { get; private set; }

        internal int OnInitCalls { get; private set; }

        /// <summary>Rows the next retrieve appends to <c>Primary!</c>.</summary>
        internal long RetrieveRows { get; set; }

        /// <summary>Rows the next retrieve appends to <c>Filter!</c>, which the chunk arithmetic counts.</summary>
        internal long RetrieveFilteredRows { get; set; }

        /// <summary>Overrides the answer the retrieve reports, so the failure arms are reachable.</summary>
        internal long? RetrieveResult { get; set; }

        /// <summary>Answer for every modify, or <see langword="null"/> to apply it.</summary>
        internal string? ModifyFailure { get; set; }

        /// <summary>
        /// The one modification script that should fail, or <see langword="null"/> for none.
        /// </summary>
        /// <remarks>
        /// SCOPED, UNLIKE <see cref="ModifyFailure"/>, AND THAT IS THE POINT. The retrieval issues several
        /// modifications and CAPTURES the result of some of them, so failing all of them would abandon the
        /// retrieval before the site under test was reached. Failing exactly one reproduces the real
        /// condition - one parity workaround this runtime will not accept - while every other modification
        /// behaves normally.
        /// </remarks>
        internal string? ScopedModifyFailureScript { get; set; }

        /// <summary>The error text returned for <see cref="ScopedModifyFailureScript"/>.</summary>
        internal string ScopedModifyFailure { get; set; } = "the runtime refused the modification";

        internal long SortResult { get; set; } = DataWindowBufferStore.DataStoreSuccess;

        internal long FilterResult { get; set; } = DataWindowBufferStore.DataStoreSuccess;

        /// <summary>
        /// Declares a one-based column carrying an auto-retrieving drop-down, and raises the column
        /// count to cover it. The ordinal-prefixed property names are the legacy's own
        /// <c>"#" + String(nIndex)</c> spelling.
        /// </summary>
        /// <param name="ordinal">The ONE-BASED column ordinal.</param>
        /// <param name="columnName">The column's name.</param>
        /// <param name="dropDownName">The drop-down source name, or a describe sentinel.</param>
        internal void DefineDropDownColumn(int ordinal, string columnName, string dropDownName)
        {
            _properties[QueryDataWindowProperty.ForColumn(
                ordinal,
                QueryDataWindowProperty.ColumnDropDownAutoRetrieveSuffix)] = "yes";

            _properties[QueryDataWindowProperty.ForColumn(
                ordinal,
                QueryDataWindowProperty.ColumnDropDownNameSuffix)] = dropDownName;

            _properties[QueryDataWindowProperty.ForColumn(
                ordinal,
                QueryDataWindowProperty.ColumnNameSuffix)] = columnName;

            long declared = long.Parse(
                _properties[QueryDataWindowProperty.ColumnCount],
                CultureInfo.InvariantCulture);

            if (ordinal > declared)
            {
                _properties[QueryDataWindowProperty.ColumnCount] =
                    ordinal.ToString(CultureInfo.InvariantCulture);
            }
        }

        public string GetSqlSelect() => SqlSelect;

        public string Modify(string modificationScript)
        {
            ModifyScripts.Add(modificationScript);

            if (ModifyFailure is not null)
            {
                return ModifyFailure;
            }

            if (ScopedModifyFailureScript is not null
                && string.Equals(modificationScript, ScopedModifyFailureScript, StringComparison.Ordinal))
            {
                return ScopedModifyFailure;
            }

            int separator = modificationScript.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                return "malformed modification script: " + modificationScript;
            }

            string property = modificationScript[..separator].Trim();
            string value = modificationScript[(separator + 1)..].Trim();

            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                value = value[1..^1];
            }

            if (string.Equals(property, DataWindowProperty.TableSelect, StringComparison.Ordinal))
            {
                SqlSelect = value;
                return string.Empty;
            }

            _properties[property] = value;
            return string.Empty;
        }

        public string Describe(string property)
        {
            if (string.Equals(property, DataWindowProperty.TableSelect, StringComparison.Ordinal))
            {
                return SqlSelect;
            }

            // The default is "!", PowerBuilder's unreadable marker - never an empty string, which
            // means something different to the data-object validation at [:L557].
            return _properties.TryGetValue(property, out string? value)
                ? value
                : ChangesetSourceDefinition.DescribeUnreadable;
        }

        public long SetFilter(string? filter)
        {
            FilterWrites.Add(filter ?? string.Empty);
            _properties[DataWindowProperty.TableFilter] = filter ?? string.Empty;
            return FilterResult;
        }

        public long SetSort(string? sort)
        {
            SortWrites.Add(sort ?? string.Empty);
            _properties[DataWindowProperty.TableSort] = sort ?? string.Empty;
            return SortResult;
        }

        public void ClearState()
        {
            ClearStateCalls++;
            Carrier.ClearState();
        }

        public void OnInit(ICarrierParentTask parentTask)
        {
            OnInitCalls++;
            Carrier.OnInit(parentTask);
        }

        public ValueTask<long> RetrieveAsync(
            IReadOnlyList<object?> parameters,
            CancellationToken cancellationToken)
        {
            Retrievals.Add([.. parameters]);

            long appended = Fill();

            return ValueTask.FromResult(RetrieveResult ?? appended);
        }

        /// <summary>
        /// Appends the configured rows THROUGH the carrier's retrieve events, so the row cap of
        /// [:L64-L70] is enforced by the carrier itself rather than simulated here.
        /// </summary>
        /// <returns>The number of rows that survived the cap.</returns>
        private long Fill()
        {
            _ = Carrier.OnRetrieveStart();

            long appended = 0L;

            for (long index = 1L; index <= RetrieveRows; index++)
            {
                long row = Carrier.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);
                _ = Carrier.SetItemValue(row, 1, DwBuffer.Primary, index);
                Carrier.RowAt(row, DwBuffer.Primary).Baseline();

                // `retrieverow` answering 1 means DISCARD THIS ROW AND STOP, which is how the cap and a
                // cancellation both end a retrieve.
                if (Carrier.OnRetrieveRow(row) == DataWindowBufferStore.EventStop)
                {
                    _ = Carrier.RowsDiscard(row, row, DwBuffer.Primary);
                    return appended;
                }

                appended++;
            }

            for (long index = 1L; index <= RetrieveFilteredRows; index++)
            {
                long row = Carrier.AppendRow(DwBuffer.Filter, ItemStatus.NotModified);
                _ = Carrier.SetItemValue(row, 1, DwBuffer.Filter, FilteredRowSeed + index);
                Carrier.RowAt(row, DwBuffer.Filter).Baseline();
            }

            return appended;
        }
    }

    /// <summary>
    /// The four <c>n_cst_thread_trans</c> members that are not on the pooled-transaction contract. It
    /// composes no connection string, opens nothing and parses no SQL - it answers what the test says
    /// and records what it was asked (C-E).
    /// </summary>
    private sealed class FakeQueryTransactionSurface : IQueryTransactionSurface
    {
        internal string SyntaxToReturn { get; set; } = FixtureSyntax;

        internal string SyntaxError { get; set; } = string.Empty;

        /// <summary>Answer for the before-retrieve hook. Zero is the allow value.</summary>
        internal long BeforeRetrieveResult { get; set; } = RetCode.ALLOW;

        internal List<long> AfterRetrieveRowCounts { get; } = [];

        internal List<string> CountStatements { get; } = [];

        /// <summary>The legacy's success value for a counting query is ONE ROW, not zero.</summary>
        internal long CountReturnCode { get; set; } = 1L;

        internal long CountValue { get; set; }

        internal string CountErrorText { get; set; } = string.Empty;

        public GridSyntaxOutcome GridSyntaxFromSql(IPooledTransaction transaction, string sql) =>
            new(SyntaxToReturn, SyntaxError);

        public ValueTask<CountQueryOutcome> Query(
            IPooledTransaction transaction,
            string sql,
            CancellationToken cancellationToken)
        {
            CountStatements.Add(sql);

            DataWindowBufferStore? result = null;

            if (CountReturnCode == 1L)
            {
                result = new DataWindowBufferStore();
                long row = result.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

                // ROW 1, COLUMN 1 - `dsTmp.GetItemNumber(1,1)` [:L851] - both one-based.
                _ = result.SetItemValue(row, 1, DwBuffer.Primary, CountValue);
            }

            return ValueTask.FromResult(new CountQueryOutcome(CountReturnCode, result, CountErrorText));
        }

        public long RaiseBeforeRetrieve(IPooledTransaction transaction, DataWindowCarrier data) =>
            BeforeRetrieveResult;

        public void RaiseAfterRetrieve(
            IPooledTransaction transaction,
            DataWindowCarrier data,
            long rowCount) => AfterRetrieveRowCounts.Add(rowCount);
    }

    /// <summary>
    /// The three DataWindow-runtime operations with no managed analogue. The create arm installs the
    /// statement through the store's own modify path, which is what a real
    /// <c>Create(syntax)</c> followed by a describe would leave behind.
    /// </summary>
    private sealed class FakeQueryRuntime : IQueryDataWindowRuntime
    {
        private readonly Dictionary<string, DataWindowBufferStore> _children =
            new(StringComparer.Ordinal);

        internal long CreateResult { get; set; } = DataWindowBufferStore.DataStoreSuccess;

        internal string CreateError { get; set; } = string.Empty;

        internal long AttachResult { get; set; } = DataWindowBufferStore.DataStoreSuccess;

        internal List<string> CreatedFromSyntax { get; } = [];

        internal int AttachCalls { get; private set; }

        /// <summary>The statement a successful create installs on the store.</summary>
        internal string GeneratedSelect { get; set; } = SomeSelect;

        internal void DefineChild(string columnName, DataWindowBufferStore child) =>
            _children[columnName] = child;

        public CarrierCreateOutcome CreateFromSyntax(ISqlDataStore data, string syntax)
        {
            CreatedFromSyntax.Add(syntax);

            if (CreateResult == DataWindowBufferStore.DataStoreSuccess)
            {
                _ = data.Modify(SqlQueryTask.BuildTableSelectAssignment(GeneratedSelect));
            }

            return new CarrierCreateOutcome(CreateResult, CreateError);
        }

        public bool TryGetChild(ISqlDataStore data, string columnName, out DataWindowBufferStore? child) =>
            _children.TryGetValue(columnName, out child);

        public long AttachTransaction(ISqlDataStore data, IPooledTransaction transaction)
        {
            AttachCalls++;
            return AttachResult;
        }
    }

    /// <summary>
    /// The result sink: the caller-side proxy's six events, recorded verbatim, plus one ORDERED trace.
    /// The order matters as much as the contents - a chunk that arrives before its receiver was created,
    /// or before its drop-down child, is a defect no per-event assertion would catch.
    /// </summary>
    private sealed class RecordingQuerySink : IQueryResultSink
    {
        public bool HasReceiver { get; set; } = true;

        public bool NeedsCreatedObject { get; set; }

        /// <summary>Runs inside the row-count callback, which is the earliest re-entrancy point.</summary>
        internal Action? OnDataReceivedCallback { get; set; }

        internal List<string> Order { get; } = [];

        internal List<long> RowCounts { get; } = [];

        internal List<(long PageCount, long RecordCount)> PageCounts { get; } = [];

        internal List<DataWindowCarrier> Moved { get; } = [];

        internal List<(CarrierState? State, long ChunkCount, long ChunkIndex, bool FullState)> Chunks
        { get; } = [];

        internal List<(string ColumnName, CarrierState? State)> Children { get; } = [];

        internal List<string> CreatedFrom { get; } = [];

        internal List<(long ReturnCode, string ErrorText)> Errors { get; } = [];

        /// <summary>Answer for a changeset chunk handover. Negative selects the failure arm.</summary>
        internal long ChunkResult { get; set; } = DataWindowBufferStore.EventContinue;

        internal long ChildResult { get; set; } = DataWindowBufferStore.EventContinue;

        internal long FullStateResult { get; set; } = DataWindowBufferStore.EventContinue;

        public void OnDataReceived(long rowCount)
        {
            RowCounts.Add(rowCount);
            Order.Add(string.Create(CultureInfo.InvariantCulture, $"rows:{rowCount}"));
            OnDataReceivedCallback?.Invoke();
        }

        public long SendFullStateChunk(QueryDataChunk chunk)
        {
            ArgumentNullException.ThrowIfNull(chunk);

            Chunks.Add((
                chunk.State?.Clone(),
                chunk.ChunkCount,
                chunk.ChunkIndex,
                chunk.FullState));

            Order.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"fullstate:{chunk.ChunkIndex}/{chunk.ChunkCount}"));

            return FullStateResult;
        }

        public void OnPageReceived(long pageCount, long recordCount)
        {
            PageCounts.Add((pageCount, recordCount));
            Order.Add(string.Create(CultureInfo.InvariantCulture, $"page:{pageCount}/{recordCount}"));
        }

        public void OnDataMove(DataWindowCarrier carrier)
        {
            Moved.Add(carrier);
            Order.Add("move");
        }

        public ValueTask<long> CreateDataAsync(string syntax, CancellationToken cancellationToken)
        {
            CreatedFrom.Add(syntax);
            Order.Add("create:" + syntax);

            return ValueTask.FromResult(DataWindowBufferStore.EventContinue);
        }

        public ValueTask<long> SendChunkAsync(ChangesetChunk chunk, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(chunk);

            // Cloned on receipt, because the sender is entitled to drop its reference immediately
            // afterwards - hazard 2 of docs/PB多线程绕坑提示.md.
            Chunks.Add((
                chunk.State?.Clone(),
                chunk.ChunkCount,
                chunk.ChunkIndex,
                chunk.FullState));

            Order.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"chunk:{chunk.ChunkIndex}/{chunk.ChunkCount}"));

            return ValueTask.FromResult(ChunkResult);
        }

        public ValueTask<long> SendChildChunkAsync(
            ChangesetChildPayload payload,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(payload);

            Children.Add((payload.ColumnName, payload.State?.Clone()));
            Order.Add("child:" + payload.ColumnName);

            return ValueTask.FromResult(ChildResult);
        }

        public void ReportError(long returnCode, string errorText)
        {
            Errors.Add((returnCode, errorText));
            Order.Add(string.Create(CultureInfo.InvariantCulture, $"error:{returnCode}"));
        }
    }

    /// <summary>The pool's activator double, so the pool needs no engine and opens no connection.</summary>
    private sealed class FakeTransactionActivator : IPooledTransactionActivator
    {
        internal FakeQueryTransaction? LastCreated { get; private set; }

        /// <summary>The value the query-side defensive override of [:L771-L774] tests for EXACTLY -1.</summary>
        internal long SqlCode { get; set; }

        internal long SqlDbCode { get; set; }

        internal string SqlErrText { get; set; } = string.Empty;

        internal DatabaseType DbType { get; set; } = DatabaseType.DbtMssql;

        /// <summary>Drives the veto discrimination of [:L747].</summary>
        internal bool SqlFailed { get; set; }

        internal bool ConnectFails { get; set; }

        public IPooledTransaction CreateDefault() => Produce();

        public IPooledTransaction Create(string className) => Produce();

        private FakeQueryTransaction Produce()
        {
            LastCreated = new FakeQueryTransaction(this);
            return LastCreated;
        }
    }

    /// <summary>
    /// A pooled transaction that reads its answers live from the activator, so a test configures the
    /// provider state before the run without having to reach the object the pool created.
    /// </summary>
    private sealed class FakeQueryTransaction : IPooledTransaction
    {
        /// <summary>The synthetic provider code a refused connect reports. Invented here.</summary>
        internal const long ConnectFailureCode = -4321L;

        /// <summary>The synthetic diagnostic a refused connect reports. Invented here.</summary>
        internal const string ConnectFailureText = "unit-test connect refused";

        private readonly FakeTransactionActivator _settings;

        internal FakeQueryTransaction(FakeTransactionActivator settings) => _settings = settings;

        internal bool Disposed { get; private set; }

        internal int ClearStateCalls { get; private set; }

        public long SqlCode => _settings.SqlCode;

        public long SqlDbCode => _settings.ConnectFails ? ConnectFailureCode : _settings.SqlDbCode;

        public long SqlNRows => 0L;

        public string SqlErrText => _settings.ConnectFails ? ConnectFailureText : _settings.SqlErrText;

        public string SqlReturnData => string.Empty;

        public bool AutoCommit { get; set; }

        internal bool Connected { get; private set; }

        internal bool Broken { get; private set; }

        public void StampSqlState(in SqlState state)
        {
            // Nothing to record: no case in this file asserts on stamped provider state.
        }

        public long Connect(CancellationToken cancellationToken = default)
        {
            if (_settings.ConnectFails)
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

        public long Rollback() => RetCode.OK;

        public long Commit(bool autoRollback) => RetCode.OK;

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

        public DatabaseType GetDbType() => _settings.DbType;

        public long ApplyTransactionData(in TransactionData descriptor) => RetCode.OK;

        public bool IsSqlFailed() => _settings.SqlFailed;

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


        public bool IsSqlSucceeded() => !_settings.SqlFailed;

        public DbErrorData CaptureError() => DbErrorData.FromTransaction(SqlDbCode, SqlErrText);

        public void Dispose() => Disposed = true;
    }

    /// <summary>
    /// A retrieval hook that answers a configured value and holds a resource, so both the decline
    /// contract of [:L757] and the deterministic release of [:L873] are observable.
    /// </summary>
    private sealed class DisposableProbeHook : ISqlRetrievalHook, IDisposable
    {
        private readonly long _answer;

        internal DisposableProbeHook(long answer) => _answer = answer;

        internal int Invocations { get; private set; }

        internal bool Disposed { get; private set; }

        public long OnRetrieve(SqlTaskBase task, IPooledTransaction transaction, DataWindowCarrier data)
        {
            Invocations++;
            return _answer;
        }

        public void Dispose() => Disposed = true;
    }

    /// <summary>
    /// A clock that never moves, and that is the assertion: the subject reads no clock at all, so a
    /// stopped one cannot change any answer in this file (C-H).
    /// </summary>
    private sealed class FixedClock : TimeProvider
    {
        internal static FixedClock Instance { get; } = new();

        /// <summary>The framework's own release date, so the value is recognisable in a failure dump.</summary>
        public override DateTimeOffset GetUtcNow() => new(2022, 4, 14, 0, 0, 0, TimeSpan.Zero);
    }

    // ==============================================================================================
    //  THE UNAPPLIABLE PARITY WORKAROUND IS AN OBSERVATION ABOUT THE RUNTIME, NOT ABOUT A REQUEST
    // ==============================================================================================

    /// <summary>
    /// A no-user-prompt workaround this runtime will not accept is reported at warning severity at most
    /// once for the process, and is still recorded on every retrieval that meets it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>⚠ THE DEFECT WAS A CHANNEL BEING FLOODED WITH ONE UNCHANGING FACT.</b> Whether that modification
    /// can be applied depends on the DataWindow runtime this service is hosted on and on nothing about a
    /// request - it either always succeeds or always fails - yet it was reported at warning severity on
    /// EVERY query, so six retrievals produced six identical warnings. An operator who learns to filter
    /// this message loses the channel a real per-request fault arrives on, which is why it is worth
    /// correcting even though nothing observable changes.
    /// </para>
    /// <para>
    /// BEHAVIOUR IS UNCHANGED AND THAT IS ASSERTED. The retrieval still SUCCEEDS with the modification
    /// refused, because the oracle discards this result at
    /// <c>[n_cst_thread_task_sqlquery.sru:L675]</c> - unlike the one at <c>:L664</c> which it captures - so
    /// a port that failed here would be STRICTER than the legacy. The count of records is also asserted, so
    /// the demotion cannot become a deletion: the observation must still be made every time, just quietly.
    /// </para>
    /// <para>
    /// <b>THE ASSERTION IS ORDER-INDEPENDENT, BECAUSE THE FLAG IS PROCESS-WIDE.</b> Another test in this
    /// assembly may already have consumed the single warning, so this asserts AT MOST one warning across
    /// two retrievals rather than exactly one. That still discriminates precisely: before the fix each
    /// retrieval warned, so two retrievals gave two warnings and the bound is exceeded however the suite
    /// is ordered. Asserting "exactly one" would have made the test depend on being run first.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheUnappliableNoUserPromptWorkaroundIsReportedOnceForTheProcess()
    {
        LevelRecordingLogger logger = new();

        for (int retrieval = 0; retrieval < 2; retrieval++)
        {
            using Harness harness = new(logger: logger);

            // EXACTLY THE ONE MODIFICATION THE WORKAROUND ISSUES, spelled from the codec's own constant so
            // a change to the script cannot leave this test silently exercising nothing.
            harness.Store.ScopedModifyFailureScript = FullStateCodec.NoUserPromptModifyString;
            harness.Store.RetrieveRows = 1L;
            _ = harness.Task.SetSql(SomeSelect);

            long result = await harness.Task
                .ExecuteAsync(harness.Sink, TestContext.Current.CancellationToken);

            // THE RETRIEVAL SUCCEEDS ANYWAY. The oracle discards this modify's result and so does the port.
            Assert.Equal(RetCode.OK, result);
        }

        (int total, int warnings) = logger.CountMatching("no-user-prompt workaround could not be applied");

        // STILL OBSERVED EVERY TIME: the demotion narrowed the severity, it did not drop the record.
        Assert.Equal(2, total);

        // AT MOST ONE OF THEM IS A WARNING. Before the fix this was 2.
        Assert.True(
            warnings <= 1,
            $"The unappliable workaround produced {warnings} warnings across two retrievals, but it "
                + "describes one unchanging property of the runtime and must reach warning severity at "
                + "most once for the process.");
    }

    /// <summary>
    /// A logger that keeps each record's severity beside its text, which is what the demotion is about.
    /// </summary>
    /// <remarks>
    /// LOCAL TO THIS FILE AND MINIMAL. The sibling recording loggers in this suite keep the rendered text
    /// but not the level, and the level is the entire subject here - a logger that dropped it could not
    /// tell a corrected implementation from the defective one.
    /// </remarks>
    private sealed class LevelRecordingLogger : ILogger<SqlQueryTask>
    {
        private readonly List<(LogLevel Level, string Text)> _records = [];

        /// <inheritdoc/>
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc/>
        /// <remarks>
        /// ALWAYS ENABLED, including at <see cref="LogLevel.Debug"/>. A default filter would discard the
        /// demoted records and the test would then be unable to prove they are still made.
        /// </remarks>
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc/>
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            lock (_records)
            {
                _records.Add((logLevel, formatter(state, exception)));
            }
        }

        /// <summary>Counts records containing a fragment, in total and at warning severity or above.</summary>
        /// <param name="fragment">The text to look for.</param>
        /// <returns>The total count and the count at warning severity or above.</returns>
        internal (int Total, int Warnings) CountMatching(string fragment)
        {
            lock (_records)
            {
                (LogLevel Level, string Text)[] matching = [.. _records
                    .Where(record => record.Text.Contains(fragment, StringComparison.Ordinal))];

                return (matching.Length, matching.Count(record => record.Level >= LogLevel.Warning));
            }
        }
    }

}
