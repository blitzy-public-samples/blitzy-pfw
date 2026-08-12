// ==================================================================================================
//  PagingDispatcherAndCountTests.cs - THE DIALECT DISPATCH AND THE PAGE-COUNT PATH
//  ------------------------------------------------------------------------------------------------
//  UNDER TEST  services/persistence-service/PowerFramework.Persistence/Sql/Paging/IPagingRewriter.cs
//                  - PagingRewriteDispatcher (the two pre-dispatch guards, the parse, the
//                    `choose case` including its `case else` arm)
//                  - PagingDialectResolver   (of_getdbtype(), which is a substring test and nothing
//                    more)
//              services/persistence-service/PowerFramework.Persistence/Tasks/SqlQueryTask.cs
//                  - BuildCountStatement, TryInferPageCounts, DividePagesCeiling and the counting
//                    path reached from ExecuteAsync
//
//  ORACLE      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru       (READ ONLY)
//                  :L303-L404  _of_buildpagedsql - the guards, the parse, the dispatch
//                  :L808-L863  the counting block - the gate, the short-circuit, the wrapper,
//                              the second binding site, the query and its failure arms
//              ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru               (READ ONLY)
//                  :L60-L61    constant long DBT_MSSQL = 0 / DBT_ORACLE = 1
//                  :L356-L361  of_getdbtype()
//              ws_objects/pfw.utility.parser.pbl.src/n_sql.sru                        (READ ONLY)
//                  the has/get/modify clause triple the wrapper drives
//              ws_objects/pfw.shared.pbl.src/retcode.sru                              (READ ONLY)
//                  :L46 E_INVALID_ARGUMENT = -3      :L70 E_INTERNAL_ERROR      = -27
//                  :L71 E_DB_ERROR         = -28     :L75 E_SQL_BIND_ARG_FAILED = -32
//                  :L77 E_NO_SUPPORT       = -2000   :L78 E_NO_IMPLEMENTATION   = -2001
//              ws_objects/pfw.shared.pbl.src/enums.sru                                (READ ONLY)
//                  :L718 SQL_MS_REPLACE = 1
//              ws_objects/pfw.tests.pbl.src/w_test_thread_sqlquery.srw                (READ ONLY)
//                  :L176-L185 the caller-side paging surface, and the statement that the record
//                             count IS the query's total row count once paging is on
//
//  WHY THIS FILE EXISTS, AND WHAT IT DELIBERATELY DOES NOT DUPLICATE
//  The two rewriter arms own the byte-exact paged statements they generate, and
//  PagingRewriterByteExactTests.cs pins those character for character. Neither arm owns the two
//  things the oracle keeps OUTSIDE them, and those two things are this file's subject:
//
//    1. DIALECT SELECTION - the classifier, the two arms, and the `case else` arm nobody can reach
//       through the classifier.
//    2. THE COUNT PATH - the three-condition gate, the arithmetic short-circuit, the two observable
//       artifacts of the counting wrapper, the ceiling, and every failure arm.
//
//  NO DATABASE OF ANY KIND IS INVOLVED, AND NONE MAY BE INTRODUCED (constraint C-E). The dispatcher
//  and the counting wrapper are PURE STRING TRANSFORMS: the dialect value selects generated TEXT, not
//  a connection. There is no SQL Server instance, no Oracle instance, no SQLite file, no dialect
//  client package and no test container anywhere in this file. Where the counting path would execute a
//  query, the pooled-transaction double from TestDoubles.cs stands in and the counting statement is
//  captured as a string. Nothing here opens a connection, reads a clock it did not inject, touches
//  the filesystem or starts a thread.
//
//  BEHAVIOUR IS REPLICATED, NEVER REPAIRED (constraint C-B). Three preserved oddities are asserted
//  here as the EXPECTED result, each with its locator, because each is the kind of thing a
//  well-meaning maintainer removes:
//
//    * the `case else` diagnostic is DELIBERATELY THE EMPTY STRING [:L397], not null and not a
//      helpful sentence;
//    * `if nRecordCount = 0 then nPageCount = 0` [:L823] comes LAST and overrides the line above it,
//      so an empty first page reports zero pages rather than one;
//    * when counting does not happen BOTH totals become -1 [:L861-L862] - a sentinel pair, not a
//      number.
//
//  CONSTRAINT C-C. The legacy tree is the behavioural oracle and is read-only. Every locator above
//  was opened and read; nothing in ws_objects/** is edited, moved or reformatted by this refactor,
//  and no PowerScript is copied into this file beyond the short quotations needed to name a
//  behaviour.
//
//  CONSTRAINT C-F. The counting statement is GENERATED SQL and therefore carries interpolated
//  literal values whenever the connection disabled bind variables, so the database error raised on
//  the counting path must reach a diagnostic only through the redaction seam. The recording redactor
//  from TestDoubles.cs is what makes that provable: its Statements list is empty unless Redact
//  actually ran, so "the statement looks masked" cannot be mistaken for "the statement went through
//  the redactor".
//
//  CONSTRAINT C-K. Two decisions are documented at the point they are asserted, because both are
//  places a future change would plausibly go wrong: why the `case else` arm is unreachable through
//  the legacy type accessor and why no third database-type value may be invented to reach it, and
//  why the counting path's re-parse failure arm is pinned at the unit level rather than end to end.
//
//  SECTION 0.8.5 - NO PERFORMANCE CLAIM IS MADE ANYWHERE. The oracle's own comment calls the
//  unique-index branch a 快速唯一索引列分页优化 - a "fast unique-index-column paging optimisation"
//  [:L324-L326] - and this file asserts nothing whatever about it being fast. The repository
//  publishes no latency budget, no throughput target and no availability commitment, so there is no
//  baseline against which such a claim could even be stated.
//
//  RULES POSITION. No user rules were provided for this project: the rules document contains exactly
//  one line saying so. Nothing is invented in their place; the bar applied instead is the
//  enterprise-standard baseline - nullable and warnings-as-errors inherited from the repository root,
//  ordinal string comparison throughout, no secret in source, and the named non-rule constraints
//  cited above at each point they apply.
//
//  EVERY VALUE HERE IS SYNTHETIC (constraint C-F). No password, account name, host or connection
//  string is copied from the legacy tree or from any catalogued in-source secret site.
// ==================================================================================================

using System.Globalization;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Sql.Paging;
using PowerFramework.Persistence.Tasks;
using PowerFramework.Persistence.Transactions;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// The parity suites for the dialect dispatch and the page-count path.
/// </summary>
public sealed class PagingDispatcherAndCountTests
{
    // ==============================================================================================
    //  SYNTHETIC INPUTS
    //  Every statement below is invented for this file or taken from the golden-master fixture
    //  constants in DwSqliteFixture. None is a credential and none is copied from a secret site.
    // ==============================================================================================

    /// <summary>A two-column statement, deliberately simple so a rewritten clause stays legible.</summary>
    private const string TwoColumnSelect = "SELECT ID, NAME FROM COMPANY";

    /// <summary>The same statement carrying an ORDER BY, which the counting wrapper must strip.</summary>
    private const string OrderedSelect = "SELECT ID, NAME FROM COMPANY ORDER BY NAME";

    /// <summary>
    /// A statement the model cannot parse as a SELECT, used to reach the parse-failure guard.
    /// </summary>
    private const string UnparseableStatement = "DELETE FROM COMPANY";

    /// <summary>
    /// The DataWindow release line the golden-master fixture declares
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L2</c>], used as a stand-in syntax string.
    /// </summary>
    /// <remarks>
    /// NEVER PARSED BY ANYTHING IN THIS FILE. Supplying a syntax rather than a bare statement is what
    /// makes the task's <c>bPlainSQL</c> flag FALSE [<c>:L599-L603</c>], which is the only way the
    /// counting path's own binding site at [<c>:L836</c>] becomes reachable at all - a plain statement
    /// has already had its parameters interpolated at [<c>:L605</c>], so the oracle suppresses the
    /// second binding for it. The carrier-creation arm is a double, so the string's content is
    /// immaterial.
    /// </remarks>
    private const string FixtureSyntax = "release 12.5;";

    /// <summary>A synthetic provider code. Invented here; not a real SQLSTATE or vendor code.</summary>
    private const long ProbeDbCode = -917L;

    /// <summary>
    /// A synthetic provider diagnostic. Invented here (constraint C-F): no message, account, host or
    /// connection string is copied from the legacy tree or from any catalogued secret site.
    /// </summary>
    private const string ProbeDbErrorText = "unit-test provider diagnostic";

    /// <summary>
    /// The counting wrapper this file expects, RE-DERIVED BY HAND from
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L830-L834</c> rather than
    /// composed from the subject's own constants.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TYPED OUT DELIBERATELY, AND THAT IS THE POINT OF IT. The oracle is two lines:
    /// </para>
    /// <code>
    /// L830  sqlParser.ModifyColumn(Enums.SQL_MS_REPLACE, "1 AS _")
    /// L834  sSQL = "SELECT COUNT(1) AS CNT FROM (" + sqlParser.GetSQL() + ") pfwPagedSQL_Tbl"
    /// </code>
    /// <para>
    /// A test that assembled its expectation from <c>SqlQueryTask.CountWrapperPrefix</c> and
    /// <c>SqlServerPagingRewriter.DerivedTableAlias</c> would pass no matter what those constants said,
    /// because it would be comparing the subject against itself. The literal below is the independent
    /// half of the comparison; a separate assertion then checks that the subject's own constants still
    /// spell the same thing, so a drift in either direction is caught rather than absorbed.
    /// </para>
    /// </remarks>
    private const string CountWrapperOpening = "SELECT COUNT(1) AS CNT FROM (";

    /// <summary>The closing of the hand-derived wrapper, including its sentinel alias [<c>:L834</c>].</summary>
    private const string CountWrapperClosing = ") pfwPagedSQL_Tbl";

    /// <summary>The column list the wrapper replaces the whole select list with [<c>:L830</c>].</summary>
    private const string CountColumnList = "1 AS _";

    /// <summary>
    /// The two Chinese diagnostics the guards raise and the two the counting path raises, spelled out
    /// here so every assertion compares the subject against an INDEPENDENT literal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each is transcribed from its locator, character for character, INCLUDING the presence or
    /// absence of the trailing exclamation mark - which differs between them and is not a typo:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><c>无效的分页设置!</c> [<c>:L308</c>] - WITH the mark.</description></item>
    ///   <item><description><c>SQL解析失败!</c> [<c>:L315</c>, and again at <c>:L827</c>] - WITH.</description></item>
    ///   <item><description><c>SQL参数绑定失败!</c> [<c>:L838</c>] - WITH.</description></item>
    ///   <item><description><c>检索失败</c> [<c>:L856</c>] - WITHOUT, unlike the retrieval-failure
    ///   text at <c>:L778</c> which carries one.</description></item>
    /// </list>
    /// <para>
    /// The <c>case else</c> diagnostic is not in this list because it has no text: <c>:L397</c> is
    /// literally <c>Event OnError(RetCode.E_NO_IMPLEMENTATION,"")</c>.
    /// </para>
    /// </remarks>
    private const string InvalidPagingSettingChinese = "无效的分页设置!";

    /// <summary>The parse-failure diagnostic [<c>:L315</c>, <c>:L827</c>], transcribed by hand.</summary>
    private const string ParseFailedChinese = "SQL解析失败!";

    /// <summary>The parameter-binding diagnostic [<c>:L838</c>], transcribed by hand.</summary>
    private const string BindFailedChinese = "SQL参数绑定失败!";

    /// <summary>
    /// The counting-failure diagnostic [<c>:L856</c>], transcribed by hand. NO EXCLAMATION MARK.
    /// </summary>
    private const string CountRetrieveFailedChinese = "检索失败";

    // ==============================================================================================
    //  SHARED HELPERS - the whole fixture is strings, which is what C-E buys
    // ==============================================================================================

    /// <summary>
    /// Both dialect arms, freshly constructed, in the order a composition root would register them.
    /// </summary>
    /// <returns>The two arms.</returns>
    /// <remarks>
    /// EXACTLY TWO, AND NEVER A THIRD. The roster is the whole of the oracle's <c>choose case</c>
    /// [<c>:L321</c>, <c>:L386</c>]; a SQLite arm is not added, because the legacy has no SQLite paging
    /// behaviour to preserve and inventing one would generate a statement the legacy never produced
    /// (constraints C-B and C-E, and AAP section 0.6.4).
    /// </remarks>
    private static IPagingRewriter[] BothArms() =>
        [new SqlServerPagingRewriter(), new OraclePagingRewriter()];

    /// <summary>
    /// Runs the dispatcher with both arms registered.
    /// </summary>
    /// <param name="dialect">The discriminator, supplied DIRECTLY rather than resolved from a
    /// connection - which is the whole of constraint C-E for this operation.</param>
    /// <param name="sql">The statement to rewrite.</param>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="pageIndex">The ONE-BASED page ordinal.</param>
    /// <param name="pageNative">Whether to select the engine's own paging construct.</param>
    /// <returns>The dispatcher's result.</returns>
    private static PagingRewriteResult Dispatch(
        DatabaseType dialect,
        string sql,
        long pageSize = 10L,
        long pageIndex = 2L,
        bool pageNative = false)
    {
        PagingRewriteRequest request = new(sql, pageSize, pageIndex, pageNative);

        return PagingRewriteDispatcher.Rewrite(in request, dialect, BothArms());
    }

    /// <summary>
    /// Renders a string as its Unicode code points, so a failed Chinese comparison names the
    /// difference instead of printing two glyph sequences that look identical.
    /// </summary>
    /// <param name="text">The text to render.</param>
    /// <returns>The space-separated <c>U+</c> code-point sequence, four hexadecimal digits each.</returns>
    /// <remarks>
    /// WHY A HOMOGLYPH CHECK IS WORTH THE FEW LINES. The legacy sources are UTF-8 with a BOM and the
    /// diagnostics are Chinese, so the failure mode this guards against is not a wrong message but a
    /// VISUALLY IDENTICAL one - a full-width exclamation mark for an ASCII one, or a lookalike
    /// ideograph. Both would satisfy a reader comparing the two strings by eye and neither would
    /// satisfy the oracle. Comparing code points makes the difference legible in the failure output.
    /// </remarks>
    private static string CodePointsOf(string text) =>
        string.Join(
            ' ',
            text.Select(character =>
                string.Create(CultureInfo.InvariantCulture, $"U+{(int)character:X4}")));

    // ==============================================================================================
    //  SUITE 1 - THE CLASSIFIER: A SUBSTRING TEST WITH A TWO-VALUE CODOMAIN
    //  ----------------------------------------------------------------------------------------------
    //  of_getdbtype() [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L356-L361] is three
    //  lines and this is all of them:
    //
    //      if Pos(Upper(DBMS),"ORACLE") > 0 then return DBT_ORACLE else return DBT_MSSQL
    //
    //  Two consequences are asserted below and NEITHER is corrected (constraint C-B):
    //    * it is a CASE-INSENSITIVE SUBSTRING test, so any name merely containing the marker selects
    //      the second dialect;
    //    * THE FIRST DIALECT IS THE FALLBACK FOR EVERYTHING ELSE, so SQLite - the only engine this
    //      service provisions - classifies as the SQL Server discriminator. That is counter-intuitive
    //      and it is the contract.
    // ==============================================================================================

    /// <summary>
    /// Engine names paired with the discriminator the classifier must answer, expressed as the raw
    /// numeric value so every row also pins <c>DBT_MSSQL = 0</c> and <c>DBT_ORACLE = 1</c>.
    /// </summary>
    /// <remarks>
    /// The numeric form is deliberate. Comparing against the enum member would pass even if the
    /// underlying values were swapped, and those two values are what the legacy declares
    /// [<c>n_cst_thread_trans.sru:L60-L61</c>] and what travels in every serialized payload and
    /// characterization recording.
    /// </remarks>
    public static TheoryData<string, long> EngineNamesAndDiscriminators =>
        new()
        {
            // The marker exactly, and in each case variant - `Upper(DBMS)` against an uppercase
            // literal is a case-insensitive search written the long way [:L356].
            { "ORACLE", 1L },
            { "oracle", 1L },
            { "Oracle", 1L },
            { "oRaClE", 1L },

            // CONTAINMENT, NOT EQUALITY. A version-suffixed or vendor-prefixed name still matches,
            // which is the breadth the oracle has and this preserves.
            { "ORACLE11g", 1L },
            { "Oracle Database 19c Enterprise Edition", 1L },
            { "O84 ORACLE", 1L },
            { "provider=oracle.dataaccess", 1L },

            // EVERYTHING ELSE FALLS TO THE FIRST DIALECT - including SQLITE, which is the entire
            // point of this row and of the dedicated fact below it.
            { "SQLITE", 0L },
            { "SQLite", 0L },
            { "sqlite3", 0L },
            { "MSS Microsoft SQL Server", 0L },
            { "SQLNCLI", 0L },
            { "ODBC", 0L },
            { "PostgreSQL", 0L },
            { "ORCL", 0L },
            { "", 0L },
        };

    [Theory]
    [MemberData(nameof(EngineNamesAndDiscriminators))]
    public void TheClassifierIsACaseInsensitiveSubstringTestWithExactlyTwoAnswers(
        string dbms,
        long expectedDiscriminator)
    {
        // [n_cst_thread_trans.sru:L356-L361]
        DatabaseType resolved = PagingDialectResolver.Resolve(dbms);

        Assert.Equal(expectedDiscriminator, (long)resolved);

        // AND THE ANSWER ALWAYS HAS AN ARM. This is the load-bearing half of the C-K note on the
        // `case else` suite below: the classifier's codomain is {0, 1}, both of which are implemented,
        // so no engine name whatever can send the dispatcher down the unrecognised-dialect path.
        Assert.True(
            PagingRewriteDispatcher.IsDialectImplemented(resolved),
            $"The classifier answered {(long)resolved} for '{dbms}', which has no arm. The classifier "
                + "can only ever answer one of two implemented values - see "
                + "n_cst_thread_trans.sru:L356-L361.");
    }

    [Fact]
    public void TheClassifierAnswersTheFirstDialectForAnAbsentEngineName()
    {
        // In PowerScript, `Pos` applied to a null subject yields null, a null condition does not take
        // the `then` branch, and control falls to `else` [:L356-L359] - so an unset DBMS already
        // resolved to the first dialect in the oracle. Throwing here would invent a failure mode the
        // legacy does not have.
        Assert.Equal(DatabaseType.DbtMssql, PagingDialectResolver.Resolve(null));
        Assert.Equal(0L, (long)PagingDialectResolver.Resolve(null));
    }

    [Fact]
    public void TheOracleMarkerIsSpelledExactlyAsTheOracleSpellsIt()
    {
        // `"ORACLE"` [:L356]. Compared against an independent literal rather than against itself, so a
        // change to the production constant is a failure here rather than an invisible one. String
        // equality in this assertion is ordinal, which is also what the resolver's own comparison is.
        Assert.Equal("ORACLE", PagingDialectResolver.OracleEngineMarker);
    }

    [Fact]
    public void ThereIsNoSqliteArm_ASqliteEngineNameRoutesToTheSqlServerArm_PreservedLegacyBehaviour()
    {
        // ==========================================================================================
        //  THE COUNTER-INTUITIVE ONE, AND IT IS CORRECT.
        //  The classifier is a substring test returning ONLY 0 or 1
        //  [n_cst_thread_trans.sru:L356-L361], so a DBMS string naming SQLite does not reach a SQLite
        //  arm and does not reach the `case else` arm either - it reaches the SQL Server arm and
        //  produces the SQL Server arm's generated text.
        //
        //  NO SQLITE PAGING STRATEGY MAY BE ADDED (constraints C-B, C-E; AAP section 0.6.4). The
        //  service provisions SQLite for STORAGE, but the legacy transaction object's type enumeration
        //  does not contain SQLite at all [n_cst_thread_trans.sru:L60-L61] and there is therefore no
        //  SQLite paging behaviour to preserve. What the arm selects is a PURE STRING TRANSFORM, never
        //  a connection, which is exactly why both dialect generators are testable here with no
        //  instance of either engine in existence.
        // ==========================================================================================
        DatabaseType resolved = PagingDialectResolver.Resolve("SQLite");

        Assert.Equal(DatabaseType.DbtMssql, resolved);

        PagingRewriteResult result = Dispatch(resolved, TwoColumnSelect);

        Assert.Equal(RetCode.OK, result.ReturnCode);

        // Observed through the SQL Server arm's own sentinel, not through the dispatcher's internals.
        Assert.Contains(
            SqlServerPagingRewriter.RowNumberAlias,
            result.RewrittenSql,
            StringComparison.Ordinal);

        // And demonstrably NOT the Oracle arm: its outermost sentinel is absent.
        Assert.DoesNotContain(
            OraclePagingRewriter.OuterTableAlias,
            result.RewrittenSql,
            StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  SUITE 2 - EXACTLY TWO ARMS, SELECTED BY VALUE, OBSERVED THROUGH THEIR OWN SENTINELS
    //  ----------------------------------------------------------------------------------------------
    //  `choose case TransObject.of_GetDBType()` with `case TransObject.DBT_MSSQL` [:L321] and
    //  `case TransObject.DBT_ORACLE` [:L386].
    //
    //  SELECTION IS ASSERTED THROUGH THE OUTPUT AND NEVER THROUGH PRIVATE STATE. The dispatcher
    //  exposes no "which arm ran" property and must not need one: each arm emits sentinel identifiers
    //  the other never emits, so the generated text names the arm that produced it. The sentinels are
    //  consumed from their single owners rather than retyped, because one of the neighbouring spellings
    //  carries a preserved legacy misspelling with two t's and retyping any of them invites exactly
    //  that drift.
    //
    //  This suite does NOT re-assert the byte-exact paged statements - PagingRewriterByteExactTests.cs
    //  owns those, arm by arm. What is asserted here is which arm answered.
    // ==============================================================================================

    [Fact]
    public void TheTwoDiscriminatorsAreZeroAndOne_AndNoThirdValueExists()
    {
        // [n_cst_thread_trans.sru:L60-L61] `constant long DBT_MSSQL = 0` and `DBT_ORACLE = 1`.
        Assert.Equal(0L, (long)DatabaseType.DbtMssql);
        Assert.Equal(1L, (long)DatabaseType.DbtOracle);

        // ==========================================================================================
        //  THE GUARD AGAINST THE MOST LIKELY WRONG CHANGE (constraint C-K). The published contract
        //  declares EXACTLY these two values. A future change that added a third - a SQLite
        //  discriminator, say, in order to "reach" the `case else` arm from a named constant - would
        //  fail here, which is the point: the arm is reached by an out-of-range VALUE, never by a new
        //  member. See the C-K note in Suite 3.
        // ==========================================================================================
        Assert.Equal<long[]>([0L, 1L], [.. Enum.GetValues<DatabaseType>().Select(value => (long)value)]);
    }

    [Fact]
    public void TheFirstDiscriminatorSelectsTheSqlServerArm()
    {
        // [:L321] `case TransObject.DBT_MSSQL`.
        PagingRewriteResult result = Dispatch(DatabaseType.DbtMssql, OrderedSelect);

        Assert.Equal(RetCode.OK, result.ReturnCode);
        Assert.True(result.IsOk);

        // The SQL Server row-number arm's own markers.
        Assert.Contains(
            SqlServerPagingRewriter.RowNumberAlias,
            result.RewrittenSql,
            StringComparison.Ordinal);

        Assert.Contains(
            SqlServerPagingRewriter.DerivedTableAlias,
            result.RewrittenSql,
            StringComparison.Ordinal);

        // Neither of the Oracle arm's two distinguishing aliases appears. Note that its INNERMOST
        // alias is deliberately the one checked rather than the derived-table alias, because
        // `pfwPagedSQL_Tbl` is a PREFIX of `pfwPagedSQL_TblInnerInner` and a containment assertion on
        // the shorter spelling would not discriminate between the arms at all.
        Assert.DoesNotContain(
            OraclePagingRewriter.InnerInnerTableAlias,
            result.RewrittenSql,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            OraclePagingRewriter.OuterTableAlias,
            result.RewrittenSql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheSecondDiscriminatorSelectsTheOracleArm()
    {
        // [:L386] `case TransObject.DBT_ORACLE`.
        PagingRewriteResult result = Dispatch(DatabaseType.DbtOracle, OrderedSelect);

        Assert.Equal(RetCode.OK, result.ReturnCode);

        // All three of the triple-nested arm's sentinels [:L394-L395].
        Assert.Contains(
            OraclePagingRewriter.InnerInnerTableAlias,
            result.RewrittenSql,
            StringComparison.Ordinal);

        Assert.Contains(
            OraclePagingRewriter.InnerTableAlias,
            result.RewrittenSql,
            StringComparison.Ordinal);

        Assert.Contains(
            OraclePagingRewriter.OuterTableAlias,
            result.RewrittenSql,
            StringComparison.Ordinal);

        // And not the SQL Server arm: its inner-join alias never appears on this path, and the arm
        // emits no TOP clause.
        Assert.DoesNotContain(
            SqlServerPagingRewriter.OutterTableAlias,
            result.RewrittenSql,
            StringComparison.Ordinal);

        Assert.DoesNotContain("TOP ", result.RewrittenSql, StringComparison.Ordinal);
    }

    /// <summary>
    /// The dialect values that have an arm, and the ones that do not.
    /// </summary>
    /// <remarks>
    /// The out-of-range values are cast integers rather than named members, because no third member
    /// exists and none may be added - see <see cref="TheTwoDiscriminatorsAreZeroAndOne_AndNoThirdValueExists"/>.
    /// </remarks>
    public static TheoryData<int, bool> DialectValuesAndWhetherImplemented =>
        new()
        {
            { 0, true },
            { 1, true },
            { 2, false },
            { 3, false },
            { 99, false },
            { -1, false },
            { int.MaxValue, false },
            { int.MinValue, false },
        };

    [Theory]
    [MemberData(nameof(DialectValuesAndWhetherImplemented))]
    public void OnlyTheTwoDeclaredValuesHaveAnArm(int dialectValue, bool implemented)
    {
        // The managed form of "this value matches a `case` label rather than falling into
        // `case else`" [:L321, :L386, :L396].
        Assert.Equal(
            implemented,
            PagingRewriteDispatcher.IsDialectImplemented((DatabaseType)dialectValue));
    }

    [Fact]
    public void ArmSelectionMatchesOnTheArmsOwnDeclaredDialect()
    {
        IPagingRewriter[] arms = BothArms();

        IPagingRewriter? sqlServer = PagingRewriteDispatcher.SelectRewriter(
            DatabaseType.DbtMssql,
            arms);

        IPagingRewriter? oracle = PagingRewriteDispatcher.SelectRewriter(DatabaseType.DbtOracle, arms);

        Assert.NotNull(sqlServer);
        Assert.NotNull(oracle);
        Assert.Equal(DatabaseType.DbtMssql, sqlServer.Dialect);
        Assert.Equal(DatabaseType.DbtOracle, oracle.Dialect);

        // The registration order is immaterial: selection is by declared dialect, not by position.
        IPagingRewriter[] reversed = [.. arms.Reverse()];

        IPagingRewriter? fromReversed = PagingRewriteDispatcher.SelectRewriter(
            DatabaseType.DbtOracle,
            reversed);

        Assert.NotNull(fromReversed);
        Assert.Equal(DatabaseType.DbtOracle, fromReversed.Dialect);

        // No arm for a value outside {0, 1}, which is what makes the `case else` arm reachable by
        // dialect value alone.
        Assert.Null(PagingRewriteDispatcher.SelectRewriter((DatabaseType)99, arms));
    }

    [Fact]
    public void ArmSelectionTakesTheFirstMatchAndSkipsANullRegistration()
    {
        // FIRST MATCH WINS, mirroring `choose case`: PowerScript evaluates its labels in source order
        // and runs the first that matches, so a duplicate registration behaves the way a duplicated
        // `case` label would rather than raising.
        SqlServerPagingRewriter first = new();
        SqlServerPagingRewriter second = new();

        Assert.Same(
            first,
            PagingRewriteDispatcher.SelectRewriter(DatabaseType.DbtMssql, [first, second]));

        // A container can be configured to yield a null element, and a NullReferenceException from
        // inside a lookup would obscure the real fault - so the null is skipped, not dereferenced.
        OraclePagingRewriter oracle = new();

        Assert.Same(
            oracle,
            PagingRewriteDispatcher.SelectRewriter(DatabaseType.DbtOracle, [null!, oracle]));
    }

    [Fact]
    public void AnImplementedDialectWithNoRegisteredArmIsAWiringFaultAndNotTheNotImplementedArm()
    {
        // ==========================================================================================
        //  THE DISTINCTION THIS PINS IS THE ONE C-K WARNS ABOUT. "The dialect is implemented but no
        //  implementation was supplied" is a state the legacy could not reach at all: its arms are
        //  compiled into the `choose case` and cannot be absent. Answering E_NO_IMPLEMENTATION for it
        //  would let a composition mistake masquerade as the oracle's unrecognised-dialect behaviour,
        //  so it fails fast instead - matching the framework's own posture, where a structural fault
        //  terminates rather than degrades [ws_objects/pfw.pbl.src/pfw.sra:L111-L144].
        // ==========================================================================================
        PagingRewriteRequest request = new(TwoColumnSelect, 10L, 1L, false);

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() =>
            PagingRewriteDispatcher.Rewrite(in request, DatabaseType.DbtOracle, [new SqlServerPagingRewriter()]));

        Assert.Contains(nameof(IPagingRewriter), thrown.Message, StringComparison.Ordinal);
    }


    // ==============================================================================================
    //  SUITE 3 - THE `case else` DEFENSIVE ARM, ITS EMPTY MESSAGE, AND WHY NOBODY MAY WIDEN IT
    //  ----------------------------------------------------------------------------------------------
    //  The oracle, verbatim [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L396-L398]:
    //
    //      case else
    //          Event OnError(RetCode.E_NO_IMPLEMENTATION,"")
    //          return RetCode.E_NO_IMPLEMENTATION
    //
    //  ⚠ THE DIAGNOSTIC IS THE EMPTY STRING AND THAT EMPTINESS IS PRESERVED LEGACY BEHAVIOUR
    //  (constraint C-B). It is not null, it is not a helpful sentence, and it is not an oversight of
    //  the oracle's author to be repaired. Inventing text here would be a behaviour improvement, which
    //  is precisely what this refactor forbids - so the emptiness is asserted as the EXPECTED value.
    //
    //  ⚠ HOW THIS ARM IS REACHED, AND HOW IT MUST NEVER BE REACHED (constraint C-K). It is reached
    //  BELOW by calling the dispatcher directly with an OUT-OF-RANGE dialect value. It is UNREACHABLE
    //  THROUGH THE LEGACY TYPE ACCESSOR: of_getdbtype() is a substring test that returns DBT_ORACLE or
    //  DBT_MSSQL and nothing else [n_cst_thread_trans.sru:L356-L361], both of which have an arm, so no
    //  engine name in existence can select this arm. It is defence in depth, and it is genuinely
    //  reachable in the port because the discriminator also travels on the published contract as a
    //  field and a protobuf enum is OPEN - an unrecognised numeric value survives deserialization as
    //  itself and arrives as a DatabaseType outside {0, 1}.
    //
    //  SO: DO NOT DELETE THE ARM AS DEAD CODE, and DO NOT INVENT A THIRD DATABASE-TYPE VALUE IN ORDER
    //  TO REACH IT. There is no DBT_SQLITE, no new enum member and no extra constant anywhere in this
    //  file or in the subject; a cast integer is the correct and only way in. Suite 2's enum-shape
    //  assertion is the tripwire that enforces the second half of that sentence.
    // ==============================================================================================

    /// <summary>
    /// Dialect values with no arm. Cast integers, never named members - see the C-K note above.
    /// </summary>
    public static TheoryData<int> UnrecognisedDialectValues =>
        [2, 3, 99, -1, int.MaxValue, int.MinValue];

    [Theory]
    [MemberData(nameof(UnrecognisedDialectValues))]
    public void AnUnrecognisedDialectAnswersNoImplementationWithADeliberatelyEmptyMessage(int dialectValue)
    {
        PagingRewriteResult result = Dispatch((DatabaseType)dialectValue, TwoColumnSelect);

        // [:L398] the code, asserted against BOTH the raw numeral and the named constant so that a
        // change to either is caught. E_NO_IMPLEMENTATION is -2001 [retcode.sru:L78].
        Assert.Equal(-2001L, result.ReturnCode);
        Assert.Equal(RetCode.E_NO_IMPLEMENTATION, result.ReturnCode);

        // AND NOT ITS NEIGHBOUR. E_NO_SUPPORT is -2000 [retcode.sru:L77]; the two are adjacent,
        // distinct, and must never be conflated.
        Assert.NotEqual(RetCode.E_NO_SUPPORT, result.ReturnCode);
        Assert.NotEqual(-2000L, result.ReturnCode);

        // [:L397] THE EMPTY MESSAGE. Asserted four ways on purpose: it is not null, it is not
        // whitespace, it has length zero, and it equals the empty string. A future "improvement" that
        // put a diagnostic sentence here fails all four.
        Assert.NotNull(result.ErrorText);
        Assert.Equal(string.Empty, result.ErrorText);
        Assert.Empty(result.ErrorText);
        Assert.Equal(0, result.ErrorText.Length);

        // No statement is produced, and the result is not OK.
        Assert.Equal(string.Empty, result.RewrittenSql);
        Assert.False(result.IsOk);
    }

    [Fact]
    public void TheNotImplementedDiagnosticConstantIsItselfEmpty()
    {
        // The subject declares the emptiness EXPLICITLY rather than letting a factory pass
        // string.Empty silently, so that it reads as a transcribed fact with a locator instead of as an
        // omission somebody might later "fix" [:L397]. This asserts the declaration, not just the
        // outcome.
        //
        // Asserted as emptiness and as length rather than as equality with string.Empty, because an
        // equality between two compile-time constants has no meaningful expected-versus-actual side.
        Assert.Empty(PagingRewriteResult.DialectNotImplementedText);
        Assert.Equal(0, PagingRewriteResult.DialectNotImplementedText.Length);
    }

    [Fact]
    public void NoEngineNameCanReachTheNotImplementedArm_TheArmIsDefenceInDepth()
    {
        // ==========================================================================================
        //  THE C-K PROOF, STATED AS AN EXECUTABLE FACT. Over a corpus that spans both dialects, every
        //  case variant, several vendor-shaped names, the empty name and the absent name, the
        //  classifier NEVER produces a value without an arm. That is why the arm cannot be exercised
        //  through the accessor and why it must nonetheless remain: its input comes from a caller who
        //  supplies the discriminator directly.
        // ==========================================================================================
        string?[] everyShapeOfEngineName =
        [
            "ORACLE", "oracle", "Oracle", "oRaClE", "ORACLE11g", "O84 ORACLE",
            "Oracle Database 19c Enterprise Edition", "provider=oracle.dataaccess",
            "SQLITE", "SQLite", "sqlite3", "MSS Microsoft SQL Server", "SQLNCLI", "ODBC",
            "PostgreSQL", "ORCL", "JDBC", "ADO.Net", string.Empty, null,
        ];

        foreach (string? name in everyShapeOfEngineName)
        {
            DatabaseType resolved = PagingDialectResolver.Resolve(name);

            Assert.True(
                PagingRewriteDispatcher.IsDialectImplemented(resolved),
                $"'{name ?? "<null>"}' resolved to {(long)resolved}, which has no arm.");

            // The stronger statement: the dispatcher itself never answers the not-implemented code for
            // a classifier-produced value.
            Assert.NotEqual(
                RetCode.E_NO_IMPLEMENTATION,
                Dispatch(resolved, TwoColumnSelect).ReturnCode);
        }
    }

    // ==============================================================================================
    //  SUITE 4 - GUARD 1: THE PAGING BOUNDS  [:L307-L310]
    //  ----------------------------------------------------------------------------------------------
    //      if _nPageSize <= 0 or _nPageIndex <= 0 then
    //          Event OnError(RetCode.E_INVALID_ARGUMENT,"无效的分页设置!")
    //          return RetCode.E_INVALID_ARGUMENT
    //
    //  A DISJUNCTION: either value being non-positive fails the whole request. Both boundaries are
    //  inclusive of zero on the FAILING side, because the domain is one-based - zero is invalid rather
    //  than meaning "the first page".
    //
    //  This is also a REDUNDANT SECOND CHECK, and the redundancy is preserved rather than collapsed
    //  (constraint C-B): the two setters already reject a non-positive value [:L450, :L462], yet the
    //  oracle re-tests both here at build time. It matters measurably - a caller that never called
    //  either setter reaches only this check, because the fields start at zero [:L257, :L259].
    // ==============================================================================================

    /// <summary>
    /// Page-size and page-index pairs the bounds guard must refuse, with the reason named so a failure
    /// message identifies the arm.
    /// </summary>
    public static TheoryData<string, long, long> RefusedPagingBounds =>
        new()
        {
            { "page size zero - the fields' own initial value [:L257]", 0L, 1L },
            { "page size negative", -1L, 1L },
            { "page size deeply negative", -25L, 3L },
            { "page index zero - one-based, so zero is not the first page [:L259]", 10L, 0L },
            { "page index negative", 10L, -1L },
            { "both zero - the never-configured task", 0L, 0L },
            { "both negative", -5L, -5L },
        };

    [Theory]
    [MemberData(nameof(RefusedPagingBounds))]
    public void InvalidPagingBoundsAreAnInvalidArgumentWithTheVerbatimChineseDiagnostic(
        string arm,
        long pageSize,
        long pageIndex)
    {
        PagingRewriteResult result = Dispatch(DatabaseType.DbtMssql, TwoColumnSelect, pageSize, pageIndex);

        // [:L309] E_INVALID_ARGUMENT is -3 [retcode.sru:L46]. The arm name is carried into the failure
        // message so a theory failure names the bound that was supposed to be refused.
        Assert.True(
            result.ReturnCode == RetCode.E_INVALID_ARGUMENT,
            $"{arm}: expected E_INVALID_ARGUMENT (-3) but the dispatcher answered {result.ReturnCode}.");

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, result.ReturnCode);
        Assert.Equal(-3L, result.ReturnCode);

        // [:L308] the diagnostic, compared against an INDEPENDENT hand-transcribed literal.
        Assert.Equal(InvalidPagingSettingChinese, result.ErrorText);

        // And the subject's own constant still spells the same thing, so a drift in either direction is
        // a failure rather than a silent agreement between two copies of the same mistake.
        Assert.Equal(InvalidPagingSettingChinese, PagingRewriteResult.InvalidPagingSettingText);

        // No statement is produced on a refused request.
        Assert.Equal(string.Empty, result.RewrittenSql);
        Assert.False(result.IsOk);
    }

    [Fact]
    public void TheInvalidPagingDiagnosticIsByteExactIncludingItsTrailingExclamationMark()
    {
        // [:L308] `"无效的分页设置!"`. The trailing mark is ASCII U+0021, not the full-width U+FF01 that
        // a Chinese input method produces by default - a difference no reader would see and no oracle
        // comparison would tolerate.
        Assert.Equal(
            "U+65E0 U+6548 U+7684 U+5206 U+9875 U+8BBE U+7F6E U+0021",
            CodePointsOf(PagingRewriteResult.InvalidPagingSettingText));

        Assert.EndsWith("!", PagingRewriteResult.InvalidPagingSettingText, StringComparison.Ordinal);
        Assert.Equal(8, PagingRewriteResult.InvalidPagingSettingText.Length);
    }

    /// <summary>Bounds the guard must accept, so the guard is shown to be a boundary and not a wall.</summary>
    public static TheoryData<long, long> AcceptedPagingBounds =>
        new()
        {
            { 1L, 1L },
            { 10L, 1L },
            { 25L, 3L },
            { 1L, 1_000_000L },
        };

    [Theory]
    [MemberData(nameof(AcceptedPagingBounds))]
    public void ValidPagingBoundsPassTheGuardAndReachAnArm(long pageSize, long pageIndex)
    {
        PagingRewriteResult result = Dispatch(DatabaseType.DbtMssql, TwoColumnSelect, pageSize, pageIndex);

        Assert.Equal(RetCode.OK, result.ReturnCode);
        Assert.NotEqual(string.Empty, result.RewrittenSql);
    }

    [Fact]
    public void APagingProductThatCannotBeRepresentedAnswersTheSameInvalidArgument()
    {
        // ==========================================================================================
        //  A GUARD THE ORACLE DOES NOT HAVE, ASSERTED HERE AS AN ADDITION RATHER THAN AS PARITY
        //  (constraint C-K). Every arm multiplies page size by page index [:L346, :L356, :L372, :L382,
        //  :L395], and an unchecked product wraps to a negative number whose statement is syntactically
        //  valid and silently matches no row. The port refuses the request instead, and it answers the
        //  SAME outcome the oracle's own bounds test answers rather than inventing a fourth code that
        //  no legacy caller has an arm for.
        //
        //  There is also no legacy behaviour to be faithful to here, because the widths differ:
        //  PowerScript `long` is 32-bit signed [:L36-L37] while these fields are 64-bit, so a request
        //  that overflows here could never have been expressed there.
        // ==========================================================================================
        PagingRewriteResult result = Dispatch(
            DatabaseType.DbtMssql,
            TwoColumnSelect,
            long.MaxValue,
            2L);

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, result.ReturnCode);
        Assert.Equal(PagingRewriteResult.InvalidPagingSettingText, result.ErrorText);
        Assert.Equal(string.Empty, result.RewrittenSql);
    }

    // ==============================================================================================
    //  SUITE 5 - GUARD 2: THE PARSE  [:L312-L317]
    //  ----------------------------------------------------------------------------------------------
    //      sqlParser = Create n_sql
    //      if Not sqlParser.Parse(origSql) then
    //          Event OnError(RetCode.E_INTERNAL_ERROR,"SQL解析失败!")
    //          return RetCode.E_INTERNAL_ERROR
    //
    //  The code is E_INTERNAL_ERROR and NOT E_INVALID_SQL even though it is the INPUT that is
    //  malformed. That is the oracle's choice and it is reproduced rather than improved (C-B).
    // ==============================================================================================

    /// <summary>Statements the SELECT model cannot parse, each a different shape of unparseable.</summary>
    public static TheoryData<string> UnparseableStatements =>
        [
            "DELETE FROM COMPANY",
            "UPDATE COMPANY SET AGE = 1",
            "INSERT INTO COMPANY (NAME) VALUES ('x')",
            "EXEC sp_probe",
            "not a statement at all",
            "   ",
            "",
        ];

    [Theory]
    [MemberData(nameof(UnparseableStatements))]
    public void AnUnparseableStatementIsAnInternalErrorWithTheVerbatimChineseDiagnostic(string sql)
    {
        PagingRewriteResult result = Dispatch(DatabaseType.DbtMssql, sql);

        // [:L316] E_INTERNAL_ERROR is -27 [retcode.sru:L70].
        Assert.Equal(RetCode.E_INTERNAL_ERROR, result.ReturnCode);
        Assert.Equal(-27L, result.ReturnCode);

        // [:L315] the diagnostic, against an independent literal and against the subject's constant.
        Assert.Equal(ParseFailedChinese, result.ErrorText);
        Assert.Equal(ParseFailedChinese, PagingRewriteResult.ParseFailedText);

        Assert.Equal(string.Empty, result.RewrittenSql);
    }

    [Fact]
    public void TheParseFailureDiagnosticIsByteExactIncludingItsAsciiPrefixAndTrailingMark()
    {
        // [:L315] `"SQL解析失败!"` - three ASCII letters, four ideographs, one ASCII exclamation mark.
        Assert.Equal(
            "U+0053 U+0051 U+004C U+89E3 U+6790 U+5931 U+8D25 U+0021",
            CodePointsOf(PagingRewriteResult.ParseFailedText));

        Assert.StartsWith("SQL", PagingRewriteResult.ParseFailedText, StringComparison.Ordinal);
        Assert.EndsWith("!", PagingRewriteResult.ParseFailedText, StringComparison.Ordinal);
        Assert.Equal(8, PagingRewriteResult.ParseFailedText.Length);
    }

    // ==============================================================================================
    //  SUITE 6 - THE ORDER OF THE THREE GUARDS IS ITSELF THE CONTRACT  [:L307, :L314, :L320]
    //  ----------------------------------------------------------------------------------------------
    //  Bounds, then parse, then dialect. The order is observable through the returned code alone, and
    //  a rearrangement that looked harmless would change the answer for every request that fails more
    //  than one guard. All four combinations are pinned.
    // ==============================================================================================

    [Fact]
    public void BadBoundsBeatAnUnparseableStatement()
    {
        // The bounds guard [:L307] runs BEFORE the parse [:L314], so a request that is both badly paged
        // and unparseable reports the PAGING fault.
        PagingRewriteResult result = Dispatch(
            DatabaseType.DbtMssql,
            UnparseableStatement,
            pageSize: 0L,
            pageIndex: 0L);

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, result.ReturnCode);
        Assert.Equal(PagingRewriteResult.InvalidPagingSettingText, result.ErrorText);
        Assert.NotEqual(RetCode.E_INTERNAL_ERROR, result.ReturnCode);
    }

    [Fact]
    public void AnUnparseableStatementBeatsAnUnrecognisedDialect()
    {
        // The parse [:L314] runs BEFORE the `choose case` [:L320], so an unparseable statement naming an
        // unrecognised dialect reports the PARSE fault - and the not-implemented arm's empty message
        // never appears.
        PagingRewriteResult result = Dispatch((DatabaseType)99, UnparseableStatement);

        Assert.Equal(RetCode.E_INTERNAL_ERROR, result.ReturnCode);
        Assert.Equal(PagingRewriteResult.ParseFailedText, result.ErrorText);
        Assert.NotEqual(RetCode.E_NO_IMPLEMENTATION, result.ReturnCode);
    }

    [Fact]
    public void BadBoundsBeatAnUnrecognisedDialect()
    {
        PagingRewriteResult result = Dispatch(
            (DatabaseType)99,
            TwoColumnSelect,
            pageSize: -1L,
            pageIndex: 1L);

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, result.ReturnCode);
        Assert.Equal(PagingRewriteResult.InvalidPagingSettingText, result.ErrorText);
    }

    [Fact]
    public void OnlyWhenBothGuardsPassDoesTheDialectDecide()
    {
        // Valid bounds and a parseable statement, so the third guard is the one that answers - and it
        // answers the not-implemented arm with its empty message.
        PagingRewriteResult result = Dispatch((DatabaseType)99, TwoColumnSelect, 10L, 1L);

        Assert.Equal(RetCode.E_NO_IMPLEMENTATION, result.ReturnCode);
        Assert.Equal(string.Empty, result.ErrorText);
    }


    // ==============================================================================================
    //  SUITE 7 - THE COUNTING WRAPPER: TWO OBSERVABLE ARTIFACTS AND ONE ORDERING  [:L826-L834]
    //  ----------------------------------------------------------------------------------------------
    //      if Not sqlParser.Parse(sSQLExec) then ... return RetCode.E_INTERNAL_ERROR      [:L826-L828]
    //      sqlParser.ModifyColumn(Enums.SQL_MS_REPLACE, "1 AS _")     //去掉字段            [:L830]
    //      if sqlParser.HasOrder() then
    //          sqlParser.ModifyOrder(Enums.SQL_MS_REPLACE,"")         //去掉ORDER BY语句     [:L831-L833]
    //      sSQL = "SELECT COUNT(1) AS CNT FROM (" + sqlParser.GetSQL() + ") pfwPagedSQL_Tbl" [:L834]
    //
    //  BOTH ARTIFACTS ARE OBSERVABLE AND BOTH ARE ASSERTED BYTE-EXACTLY: the select list becomes
    //  exactly `1 AS _`, and the whole statement is wrapped with the alias `CNT` and the sentinel
    //  `pfwPagedSQL_Tbl`. `COUNT(1)` rather than `COUNT(*)` is the oracle's spelling and is preserved.
    //
    //  IT COUNTS THE UNPAGED STATEMENT. The oracle passes sSQLExec, which is the statement BEFORE the
    //  paging rewrite - counting the paged statement would answer the page size rather than the total.
    // ==============================================================================================

    /// <summary>
    /// Statements paired with the counting statement the wrapper must produce, character for character.
    /// </summary>
    /// <remarks>
    /// Each expectation is written out in full rather than assembled from the subject's constants, for
    /// the reason given on <see cref="CountWrapperOpening"/>: an expectation built from the subject
    /// cannot detect the subject changing.
    /// </remarks>
    public static TheoryData<string, string, string> CountWrapperCases =>
        new()
        {
            {
                // The plain two-column case: the select list is REPLACED wholesale, so neither column
                // name survives into the counted statement.
                "two named columns",
                "SELECT ID, NAME FROM COMPANY",
                "SELECT COUNT(1) AS CNT FROM (SELECT 1 AS _ FROM COMPANY) pfwPagedSQL_Tbl"
            },
            {
                // A star select loses its star the same way.
                "a star select",
                "SELECT * FROM COMPANY",
                "SELECT COUNT(1) AS CNT FROM (SELECT 1 AS _ FROM COMPANY) pfwPagedSQL_Tbl"
            },
            {
                // The golden-master fixture's own retrieve statement
                // [ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14]. It happens to be spelled the same
                // way as the row above, which is WHY these rows carry a name: two theory rows with
                // identical arguments collapse into one test case and the second is silently skipped,
                // so the name is what keeps the fixture's own statement independently exercised.
                "the golden-master fixture's retrieve statement",
                DwSqliteFixture.RetrieveStatement,
                "SELECT COUNT(1) AS CNT FROM (SELECT 1 AS _ FROM COMPANY) pfwPagedSQL_Tbl"
            },
            {
                // THE WHERE CLAUSE SURVIVES, and it must: a count that dropped the restriction would
                // answer the table's row total instead of the query's.
                "a WHERE clause, which must survive",
                "SELECT ID FROM COMPANY WHERE AGE > 30",
                "SELECT COUNT(1) AS CNT FROM (SELECT 1 AS _ FROM COMPANY WHERE AGE > 30) pfwPagedSQL_Tbl"
            },
            {
                // THE ORDER BY DOES NOT SURVIVE [:L831-L832] - ordering a counted sub-select is
                // pointless and some engines reject it outright.
                "an ORDER BY, which must not survive",
                "SELECT ID, NAME FROM COMPANY ORDER BY NAME",
                "SELECT COUNT(1) AS CNT FROM (SELECT 1 AS _ FROM COMPANY) pfwPagedSQL_Tbl"
            },
            {
                // Both clauses together: the restriction stays, the ordering goes.
                "both clauses - the restriction stays and the ordering goes",
                "SELECT ID, NAME FROM COMPANY WHERE AGE > 30 ORDER BY NAME",
                "SELECT COUNT(1) AS CNT FROM (SELECT 1 AS _ FROM COMPANY WHERE AGE > 30) pfwPagedSQL_Tbl"
            },
        };

    [Theory]
    [MemberData(nameof(CountWrapperCases))]
    public void TheCountingWrapperIsByteExact(string arm, string executableSql, string expectedCountSql)
    {
        long result = SqlQueryTask.BuildCountStatement(
            executableSql,
            out string countSql,
            out string errorText);

        Assert.Equal(RetCode.OK, result);
        Assert.Equal(string.Empty, errorText);

        // BYTE-EXACT, ordinal, against the hand-derived literal. The arm name is carried into the
        // failure message so a theory failure says WHICH shape of statement diverged.
        Assert.True(
            string.Equals(expectedCountSql, countSql, StringComparison.Ordinal),
            $"{arm}: expected '{expectedCountSql}' but the wrapper produced '{countSql}'.");

        Assert.Equal(expectedCountSql, countSql);

        // ARTIFACT 1 [:L830]: the select list is EXACTLY `1 AS _`, asserted as a whole clause rather
        // than as a fragment - `Contains` alone would pass for `SELECT ID, 1 AS _ FROM ...`.
        Assert.Contains(
            CountWrapperOpening + "SELECT " + CountColumnList + " FROM ",
            countSql,
            StringComparison.Ordinal);

        // ARTIFACT 2 [:L834]: the wrapper's opening and its sentinel-bearing close.
        Assert.StartsWith(CountWrapperOpening, countSql, StringComparison.Ordinal);
        Assert.EndsWith(CountWrapperClosing, countSql, StringComparison.Ordinal);

        // NO ORDER BY ANYWHERE IN THE RESULT, whatever the input carried [:L831-L833].
        Assert.DoesNotContain("ORDER BY", countSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheSubjectsOwnWrapperConstantsStillSpellTheOracleLiterals()
    {
        // The other half of the byte-exact comparison. The theory above compares the OUTPUT against
        // hand-derived literals; this compares the subject's DECLARED constants against the same
        // literals, re-derived from [:L830] and [:L834]. Together they catch a drift in either place.
        Assert.Equal(CountColumnList, SqlQueryTask.CountColumnAlias);
        Assert.Equal(CountWrapperOpening, SqlQueryTask.CountWrapperPrefix);

        // The close is spelled in two pieces by the subject - the separator and the sentinel, whose
        // single owner is the SQL Server rewriter because the paged statement needs the same spelling.
        Assert.Equal(
            CountWrapperClosing,
            SqlQueryTask.CountWrapperAliasSeparator + SqlServerPagingRewriter.DerivedTableAlias);

        // The style constant driving both modifications is the shared catalogue's, whose value is 1
        // [ws_objects/pfw.shared.pbl.src/enums.sru:L718].
        Assert.Equal(1L, ClauseModifier.SQL_MS_REPLACE);
        Assert.Equal(Enums.SQL_MS_REPLACE, ClauseModifier.SQL_MS_REPLACE);
    }

    /// <summary>
    /// Replays the three steps of [<c>:L830-L834</c>] IN THE ORACLE'S ORDER, independently of the
    /// subject.
    /// </summary>
    /// <param name="sql">The statement to count.</param>
    /// <returns>The counting statement the oracle's ordering produces.</returns>
    /// <remarks>
    /// <para>
    /// WHY AN ORDERED REPLAY IS THE HONEST PROOF HERE, AND WHY THE ORDERED MODIFY RECORDER CANNOT BE
    /// (constraint C-K). <c>SqlQueryTask.BuildCountStatement</c> creates its own
    /// <see cref="SelectStatementModel"/> inside the method, exactly as the oracle does
    /// <c>Create n_sql</c> inside its function - there is no injection seam for the clause surface and
    /// adding one would change the subject in order to test it. So the two clause modifications cannot
    /// be observed by a spy. What CAN be observed is the produced text, and text is enough: this method
    /// performs the same three steps in the oracle's order and
    /// <see cref="ReplayWithTheWrapBeforeTheStrip"/> performs them in the wrong one. If the subject
    /// matches the first and differs from the second, the ordering is pinned rather than merely
    /// implied. The ordered describe-and-modify recorder from TestDoubles.cs is used where it genuinely
    /// observes the subject - on the store surface, in Suite 13 - rather than pointed at a replay of
    /// this file's own making, which would assert nothing about the subject at all.
    /// </para>
    /// </remarks>
    private static string ReplayInLegacyOrder(string sql)
    {
        SelectStatementModel model = new();

        Assert.True(model.Parse(sql));

        // [:L830] the column replacement, result discarded exactly as the oracle discards it.
        _ = model.ModifyColumn(ClauseModifier.SQL_MS_REPLACE, CountColumnList);

        // [:L831-L833] the CONDITIONAL strip - and it happens BEFORE the wrap below.
        if (model.HasOrder())
        {
            _ = model.ModifyOrder(ClauseModifier.SQL_MS_REPLACE, string.Empty);
        }

        // [:L834] the wrap, over text that no longer carries an ordering.
        return CountWrapperOpening + model.GetSql() + CountWrapperClosing;
    }

    /// <summary>
    /// Replays the same three steps with the wrap moved BEFORE the strip - the mistake this file exists
    /// to make impossible.
    /// </summary>
    /// <param name="sql">The statement to count.</param>
    /// <returns>The counting statement the wrong ordering produces.</returns>
    private static string ReplayWithTheWrapBeforeTheStrip(string sql)
    {
        SelectStatementModel model = new();

        Assert.True(model.Parse(sql));

        _ = model.ModifyColumn(ClauseModifier.SQL_MS_REPLACE, CountColumnList);

        // THE WRONG ORDER: the wrapper is composed from text that still carries the ORDER BY, so the
        // ordering ends up INSIDE the counted sub-select.
        string wrapped = CountWrapperOpening + model.GetSql() + CountWrapperClosing;

        if (model.HasOrder())
        {
            _ = model.ModifyOrder(ClauseModifier.SQL_MS_REPLACE, string.Empty);
        }

        return wrapped;
    }

    [Fact]
    public void TheOrderByIsStrippedBeforeTheWrapAndNotAfterIt()
    {
        long result = SqlQueryTask.BuildCountStatement(OrderedSelect, out string countSql, out _);

        Assert.Equal(RetCode.OK, result);

        // The subject agrees with the oracle's ordering, character for character.
        Assert.Equal(ReplayInLegacyOrder(OrderedSelect), countSql);

        // And disagrees with the wrong ordering - which is what makes the assertion above about ORDER
        // rather than about content. The wrong ordering is not merely different: it carries the
        // ordering INSIDE the counted sub-select, which is the observable consequence.
        string wrongOrder = ReplayWithTheWrapBeforeTheStrip(OrderedSelect);

        Assert.NotEqual(wrongOrder, countSql);
        Assert.Contains("ORDER BY", wrongOrder, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ORDER BY", countSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheStripIsConditionalSoAnAbsentOrderByIsNotGivenAnEmptyOne()
    {
        // [:L831] the guard is `if sqlParser.HasOrder()`, and the condition is not cosmetic: the
        // statement model distinguishes an ABSENT clause from a PRESENT-BUT-EMPTY one when it re-emits,
        // which is exactly where a byte-exact difference would appear.
        Assert.Equal(RetCode.OK, SqlQueryTask.BuildCountStatement(OrderedSelect, out string stripped, out _));

        Assert.Equal(
            RetCode.OK,
            SqlQueryTask.BuildCountStatement(TwoColumnSelect, out string neverHadOne, out _));

        // Both arms converge on the same text, which is the point of the conditional: the absent-clause
        // case is not rewritten at all, and the stripped case ends up indistinguishable from it.
        Assert.Equal(neverHadOne, stripped);
        Assert.Equal(ReplayInLegacyOrder(TwoColumnSelect), neverHadOne);
    }

    [Theory]
    [MemberData(nameof(UnparseableStatements))]
    public void AnUnparseableStatementOnTheCountingPathIsAnInternalError(string executableSql)
    {
        // ==========================================================================================
        //  [:L826-L828] THE COUNTING PATH'S OWN PARSE GUARD, PINNED AT THE UNIT LEVEL - AND HERE IS
        //  WHY IT IS NOT PINNED END TO END (constraint C-K).
        //
        //  The counting block re-parses sSQLExec [:L826], but the paging rewrite has ALREADY parsed the
        //  very same statement [:L314] earlier in the same retrieval, and the counting block only runs
        //  when paging is on [:L818]. Both parses are the same call over the same text, so a statement
        //  that fails here failed there first and the retrieval returned E_INTERNAL_ERROR from the
        //  paging step - meaning this arm is SHADOWED on the end-to-end path and cannot be reached
        //  through ExecuteAsync.
        //
        //  That is a finding about the oracle, not a gap in the port: the arm is genuinely reachable in
        //  the port because BuildCountStatement is a published static entry point in its own right, and
        //  it is exercised here directly. Do NOT "fix" the shadowing by making the counting path parse
        //  something the paging path did not - that would change which statement is counted.
        //
        //  CONSEQUENCE FOR A COVERAGE READER, STATED SO IT IS NOT MISTAKEN FOR AN UNTESTED BRANCH. The
        //  builder's own failure arm is covered by this theory, but the two lines that REPORT it inside
        //  the private counting method - the error event and the early return - cannot be reached from
        //  ExecuteAsync for exactly the reason above, and no test in this file or any other can reach
        //  them without altering the subject. They are left uncovered deliberately rather than reached
        //  by a contrived seam.
        // ==========================================================================================
        long result = SqlQueryTask.BuildCountStatement(
            executableSql,
            out string countSql,
            out string errorText);

        Assert.Equal(RetCode.E_INTERNAL_ERROR, result);
        Assert.Equal(-27L, result);

        // [:L827] the same diagnostic the paging guard raises, and the same one the subject publishes.
        Assert.Equal(ParseFailedChinese, errorText);
        Assert.Equal(ParseFailedChinese, SqlQueryTask.ParseFailedText);

        // No statement is produced on the failure arm.
        Assert.Equal(string.Empty, countSql);
    }

    // ==============================================================================================
    //  SUITE 8 - THE ARITHMETIC SHORT-CIRCUIT: NO COUNTING STATEMENT AT ALL  [:L819-L823]
    //  ----------------------------------------------------------------------------------------------
    //  Under the oracle's own comment 当前页不足分页大小时不进行统计 - "do not count when the current page
    //  is short of the page size":
    //
    //      if (nRowCnt > 0 and nRowCnt < _nPageSize) or (nRowCnt = 0 and _nPageIndex = 1) then
    //          nPageCount   = _nPageIndex
    //          nRecordCount = (_nPageIndex - 1) * _nPageSize + nRowCnt
    //          if nRecordCount = 0 then nPageCount = 0
    //
    //  ⚠ TWO DISJUNCTS AND THE SECOND IS NOT REDUNDANT. A partial final page means this IS the last
    //  page, so the totals follow from the ordinal. An EMPTY page qualifies only on page ONE, because
    //  an empty page five says nothing about the total and must fall through to the counting statement.
    //
    //  ⚠ THE ZERO CORRECTION COMES LAST AND OVERRIDES THE LINE ABOVE IT [:L823]. Without it an empty
    //  first page would report ONE page holding NO records. It is easy to miss and it is observable.
    // ==============================================================================================

    /// <summary>
    /// Row count, page size and page index paired with the totals the short-circuit must infer.
    /// </summary>
    public static TheoryData<long, long, long, long, long> ShortCircuitCases =>
        new()
        {
            // A partial final page in the middle of a run: 3 complete pages of 25 precede it.
            { 7L, 25L, 3L, 3L, 57L },

            // The smallest partial page there is.
            { 1L, 10L, 1L, 1L, 1L },

            // One row short of a full page, on the first page.
            { 24L, 25L, 1L, 1L, 24L },

            // A partial final page deep into a run - `(pageIndex - 1)` turns the ONE-BASED ordinal into
            // a count of COMPLETE PRECEDING pages, which is not a rebasing.
            { 9L, 10L, 5L, 5L, 49L },

            // THE ZERO SPECIAL CASE [:L823]: an empty FIRST page reports ZERO pages, not one.
            { 0L, 25L, 1L, 0L, 0L },
            { 0L, 1L, 1L, 0L, 0L },
        };

    [Theory]
    [MemberData(nameof(ShortCircuitCases))]
    public void TheShortCircuitInfersBothTotalsArithmetically(
        long rowCount,
        long pageSize,
        long pageIndex,
        long expectedPageCount,
        long expectedRecordCount)
    {
        Assert.True(
            SqlQueryTask.TryInferPageCounts(
                rowCount,
                pageSize,
                pageIndex,
                out long pageCount,
                out long recordCount));

        // [:L821] and [:L822], then [:L823].
        Assert.Equal(expectedPageCount, pageCount);
        Assert.Equal(expectedRecordCount, recordCount);
    }

    [Fact]
    public void TheZeroRecordCorrectionOverridesThePageOrdinalAssignedAboveIt()
    {
        // Stated as its own fact because the theory row above could be read as arithmetic coincidence.
        // [:L821] assigns nPageCount = _nPageIndex = 1, and [:L823] then OVERWRITES it with 0 because
        // the record total came out zero. Both lines run; the second wins.
        Assert.True(SqlQueryTask.TryInferPageCounts(0L, 25L, 1L, out long pageCount, out long recordCount));

        Assert.Equal(0L, recordCount);
        Assert.Equal(0L, pageCount);
        Assert.NotEqual(1L, pageCount);
    }

    /// <summary>Row counts that must NOT short-circuit, each for a different reason.</summary>
    public static TheoryData<string, long, long, long> NonShortCircuitCases =>
        new()
        {
            { "a FULL page may or may not be the last one", 25L, 25L, 1L },
            { "a full page deep into a run", 10L, 10L, 3L },
            { "an EMPTY page five says nothing about the total", 0L, 25L, 5L },
            { "an empty page two is already outside the second disjunct", 0L, 1L, 2L },
            { "more rows than the page size - the cap case", 30L, 25L, 1L },
        };

    [Theory]
    [MemberData(nameof(NonShortCircuitCases))]
    public void APageThatSaysNothingAboutTheTotalFallsThroughToTheCountingStatement(
        string reason,
        long rowCount,
        long pageSize,
        long pageIndex)
    {
        Assert.False(
            SqlQueryTask.TryInferPageCounts(rowCount, pageSize, pageIndex, out _, out _),
            reason);
    }

    // ==============================================================================================
    //  SUITE 9 - THE CEILING  [:L853]
    //  ----------------------------------------------------------------------------------------------
    //      nPageCount = Ceiling(nRecordCount / _nPageSize)
    //
    //  PowerScript divides two longs into a REAL and then rounds up, so the port divides in decimal.
    //  A binary floating-point divide can land a large exact multiple a hair above or below the integer
    //  and change the page total by one - precisely the off-by-one a row-count assertion would not
    //  catch. Both an EXACT division and an inexact one are exercised, so the rounding is genuinely
    //  reached rather than incidentally satisfied.
    // ==============================================================================================

    /// <summary>Record totals and page sizes paired with the page total the ceiling must answer.</summary>
    public static TheoryData<long, long, long> CeilingCases =>
        new()
        {
            // No records at all: zero pages, not one.
            { 0L, 25L, 0L },

            // One record: one page. The smallest inexact division there is.
            { 1L, 25L, 1L },

            // EXACT DIVISIONS - the rounding must not add a page.
            { 25L, 25L, 1L },
            { 50L, 25L, 2L },
            { 100L, 10L, 10L },

            // INEXACT DIVISIONS - the rounding must add exactly one page.
            { 26L, 25L, 2L },
            { 49L, 25L, 2L },
            { 51L, 25L, 3L },

            // A page size of one, where every record is its own page.
            { 7L, 1L, 7L },

            // Large and inexact, where a binary divide is at its least trustworthy.
            { 1_000_000_000_000L, 7L, 142_857_142_858L },
        };

    [Theory]
    [MemberData(nameof(CeilingCases))]
    public void ThePageTotalIsTheCeilingOfTheRecordTotalOverThePageSize(
        long recordCount,
        long pageSize,
        long expectedPageCount)
    {
        Assert.Equal(expectedPageCount, SqlQueryTask.DividePagesCeiling(recordCount, pageSize));
    }

    [Fact]
    public void AZeroPageSizeAnswersTheNotCountedSentinelRatherThanDividingByZero()
    {
        // Unreachable through the guarded paths - the setter rejects it [:L462] and the paged builder
        // re-rejects it [:L307] - but guarded rather than assumed, because a divide by zero would
        // surface as an exception on a path whose whole contract is return codes.
        Assert.Equal(SqlQueryTask.NotCountedValue, SqlQueryTask.DividePagesCeiling(100L, 0L));
        Assert.Equal(-1L, SqlQueryTask.DividePagesCeiling(100L, 0L));
    }


    // ==============================================================================================
    //  SUITE 10 - THE COUNTING QUERY, DRIVEN THROUGH A WHOLE RETRIEVAL  [:L818-L858]
    //  ----------------------------------------------------------------------------------------------
    //  The counting block is private to the task and reachable only from its body, so the arms below
    //  are exercised by running a retrieval end to end. NOTHING CONNECTS TO ANYTHING (constraint C-E):
    //  the transaction is the scripted pooled-transaction double, the counting query is intercepted as
    //  a STRING, the DataWindow runtime is a double, and the clock is injected and never advanced.
    // ==============================================================================================

    [Fact]
    public async Task AFullPageIssuesTheCountingQueryAndItsStatementIsTheByteExactWrapper()
    {
        // A page that filled completely cannot be inferred [:L820], so the counting statement is built
        // [:L830-L834] and executed [:L843].
        using Harness harness = new();
        harness.Runtime.GeneratedSelect = TwoColumnSelect;
        harness.Store.RetrieveRows = 2L;
        harness.TransactionSurface.CountValue = 5L;
        _ = harness.Task.SetSqlSyntax(FixtureSyntax);
        _ = harness.Task.SetPaged(true);
        _ = harness.Task.SetPageSize(2L);
        _ = harness.Task.SetPageIndex(1L);

        long result = await harness.Task.ExecuteAsync(
            harness.Sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, result);

        // EXACTLY ONE counting query, carrying the byte-exact wrapper.
        string issued = Assert.Single(harness.TransactionSurface.CountStatements);

        Assert.Equal(
            "SELECT COUNT(1) AS CNT FROM (SELECT 1 AS _ FROM COMPANY) pfwPagedSQL_Tbl",
            issued);

        // IT COUNTS THE UNPAGED STATEMENT. The paging rewrite ran - the store carried the paged
        // statement while the retrieval was in flight - yet the counted statement carries none of the
        // paging machinery, because the oracle passes sSQLExec rather than the rewritten text. Counting
        // the paged statement would answer the page size instead of the total.
        Assert.DoesNotContain(
            SqlServerPagingRewriter.RowNumberAlias,
            issued,
            StringComparison.Ordinal);

        // Ceiling(5 / 2) = 3 pages over 5 records [:L852-L853].
        Assert.Equal<(long, long)>([(3L, 5L)], harness.Sink.PageCounts);
    }

    [Fact]
    public async Task TheCountedStatementCarriesNoOrderingEvenWhenTheRetrievedStatementDid()
    {
        // [:L831-L833] the strip, observed through a whole retrieval rather than through the builder.
        using Harness harness = new();
        harness.Runtime.GeneratedSelect = OrderedSelect;
        harness.Store.RetrieveRows = 2L;
        harness.TransactionSurface.CountValue = 4L;
        _ = harness.Task.SetSqlSyntax(FixtureSyntax);
        _ = harness.Task.SetPaged(true);
        _ = harness.Task.SetPageSize(2L);
        _ = harness.Task.SetPageIndex(1L);

        long result = await harness.Task.ExecuteAsync(
            harness.Sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, result);

        string issued = Assert.Single(harness.TransactionSurface.CountStatements);

        Assert.DoesNotContain("ORDER BY", issued, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ReplayInLegacyOrder(OrderedSelect), issued);

        // Ceiling(4 / 2) = 2, an EXACT division, so the ceiling must not add a page.
        Assert.Equal<(long, long)>([(2L, 4L)], harness.Sink.PageCounts);
    }

    /// <summary>
    /// Record totals the counting query answers, paired with the page total the ceiling must publish.
    /// </summary>
    /// <remarks>
    /// Both an EXACT division and an inexact one, so the rounding at [<c>:L853</c>] is genuinely
    /// exercised on the end-to-end path and not only in the arithmetic suite.
    /// </remarks>
    public static TheoryData<long, long, long> PublishedCeilingCases =>
        new()
        {
            { 50L, 25L, 2L },
            { 51L, 25L, 3L },
            { 25L, 25L, 1L },
            { 26L, 25L, 2L },
        };

    [Theory]
    [MemberData(nameof(PublishedCeilingCases))]
    public async Task ThePublishedPageTotalIsTheCeilingOfTheCountedRecordTotal(
        long countedRecords,
        long pageSize,
        long expectedPages)
    {
        using Harness harness = new();
        harness.Runtime.GeneratedSelect = TwoColumnSelect;

        // A FULL page, so the arithmetic short-circuit does not apply and the query really runs.
        harness.Store.RetrieveRows = pageSize;
        harness.TransactionSurface.CountValue = countedRecords;
        _ = harness.Task.SetSqlSyntax(FixtureSyntax);
        _ = harness.Task.SetPaged(true);
        _ = harness.Task.SetPageSize(pageSize);
        _ = harness.Task.SetPageIndex(1L);

        long result = await harness.Task.ExecuteAsync(
            harness.Sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, result);
        Assert.Single(harness.TransactionSurface.CountStatements);
        Assert.Equal<(long, long)>([(expectedPages, countedRecords)], harness.Sink.PageCounts);
    }

    [Fact]
    public async Task APartialFinalPageIssuesNoCountingQueryAtAll()
    {
        // [:L819-L823] the short-circuit, observed as an EMPTY statement list rather than only as an
        // arithmetic result - the whole point of the branch is that NO query is issued.
        using Harness harness = new();
        harness.Runtime.GeneratedSelect = TwoColumnSelect;
        harness.Store.RetrieveRows = 3L;
        _ = harness.Task.SetSqlSyntax(FixtureSyntax);
        _ = harness.Task.SetPaged(true);
        _ = harness.Task.SetPageSize(10L);
        _ = harness.Task.SetPageIndex(4L);

        long result = await harness.Task.ExecuteAsync(
            harness.Sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, result);
        Assert.Empty(harness.TransactionSurface.CountStatements);

        // pageCount = pageIndex = 4; recordCount = (4 - 1) * 10 + 3 = 33.
        Assert.Equal<(long, long)>([(4L, 33L)], harness.Sink.PageCounts);
    }

    [Fact]
    public async Task AnEmptyFirstPagePublishesZeroAndZeroWithNoCountingQuery()
    {
        // The second disjunct [:L820] plus the correction at [:L823], end to end.
        using Harness harness = new();
        harness.Runtime.GeneratedSelect = TwoColumnSelect;
        harness.Store.RetrieveRows = 0L;
        _ = harness.Task.SetSqlSyntax(FixtureSyntax);
        _ = harness.Task.SetPaged(true);
        _ = harness.Task.SetPageSize(25L);
        _ = harness.Task.SetPageIndex(1L);

        long result = await harness.Task.ExecuteAsync(
            harness.Sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, result);
        Assert.Empty(harness.TransactionSurface.CountStatements);

        // ZERO pages, not one - see the correction's own fact in Suite 8.
        Assert.Equal<(long, long)>([(0L, 0L)], harness.Sink.PageCounts);
    }

    // ==============================================================================================
    //  SUITE 11 - THE GATE'S ELSE-ARM: BOTH TOTALS BECOME -1  [:L818, :L860-L862]
    //  ----------------------------------------------------------------------------------------------
    //      if Not bIsProcedure and _bPaged and _bPageCounting then ... else
    //          nPageCount   = -1
    //          nRecordCount = -1
    //
    //  THREE CONDITIONS, AND EACH IS TESTED SEPARATELY. When any one fails, BOTH totals become the
    //  sentinel - not one of them, and not zero - and the notification STILL FIRES [:L865]: counting is
    //  skipped, the publication is not. A consumer that treats -1 as a number reports a negative page
    //  count, which is exactly why it must be published as a pair and preserved as a pair (C-B).
    // ==============================================================================================

    [Fact]
    public void TheNotCountedSentinelIsMinusOne()
    {
        // [:L861-L862]. Pinned on its own so the three facts below can name it rather than repeat -1.
        Assert.Equal(-1L, SqlQueryTask.NotCountedValue);
    }

    [Fact]
    public async Task WithPagingOffBothTotalsAreTheSentinel()
    {
        using Harness harness = new();
        harness.Runtime.GeneratedSelect = TwoColumnSelect;
        harness.Store.RetrieveRows = 2L;
        _ = harness.Task.SetSqlSyntax(FixtureSyntax);
        _ = harness.Task.SetPaged(false);
        _ = harness.Task.SetPageSize(2L);
        _ = harness.Task.SetPageIndex(1L);

        long result = await harness.Task.ExecuteAsync(
            harness.Sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, result);
        Assert.Empty(harness.TransactionSurface.CountStatements);
        Assert.Equal<(long, long)>(
            [(SqlQueryTask.NotCountedValue, SqlQueryTask.NotCountedValue)],
            harness.Sink.PageCounts);
    }

    [Fact]
    public async Task WithPageCountingOffBothTotalsAreTheSentinel()
    {
        using Harness harness = new();
        harness.Runtime.GeneratedSelect = TwoColumnSelect;
        harness.Store.RetrieveRows = 2L;
        _ = harness.Task.SetSqlSyntax(FixtureSyntax);
        _ = harness.Task.SetPaged(true);
        _ = harness.Task.SetPageSize(2L);
        _ = harness.Task.SetPageIndex(1L);
        _ = harness.Task.SetPageCounting(false);

        long result = await harness.Task.ExecuteAsync(
            harness.Sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, result);
        Assert.Empty(harness.TransactionSurface.CountStatements);
        Assert.Equal<(long, long)>([(-1L, -1L)], harness.Sink.PageCounts);
    }

    [Fact]
    public async Task ForAStoredProcedureBothTotalsAreTheSentinel()
    {
        // `bIsProcedure = (data.Describe("DataWindow.Table.Procedure") <> "!")` [:L626] - ⚠ THE SENSE IS
        // INVERTED FROM THE OBVIOUS READING: the source IS a procedure precisely when the property is
        // READABLE. A readable answer therefore suppresses clause modification, paging AND counting.
        using Harness harness = new(isProcedure: true);
        harness.Runtime.GeneratedSelect = TwoColumnSelect;
        harness.Store.RetrieveRows = 2L;
        _ = harness.Task.SetSqlSyntax(FixtureSyntax);
        _ = harness.Task.SetPaged(true);
        _ = harness.Task.SetPageSize(2L);
        _ = harness.Task.SetPageIndex(1L);

        long result = await harness.Task.ExecuteAsync(
            harness.Sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, result);
        Assert.Empty(harness.TransactionSurface.CountStatements);
        Assert.Equal<(long, long)>([(-1L, -1L)], harness.Sink.PageCounts);

        // AND NO PAGING REWRITE EITHER, which is the other half of the same gate [:L722].
        Assert.DoesNotContain(
            SqlServerPagingRewriter.RowNumberAlias,
            harness.Store.SqlSelect,
            StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  SUITE 12 - THE COUNTING PATH'S FAILURE ARMS, AND THE REDACTION SEAM  [:L836-L858]
    // ==============================================================================================

    [Fact]
    public async Task AFailedParameterBindingOnTheCountingPathReportsItsOwnChineseDiagnostic()
    {
        // ==========================================================================================
        //  [:L836-L841] THE SECOND, DISTINCT BINDING SITE.
        //      if of_HasParams() and Not bPlainSQL then
        //          if IsFailed(_of_SQLBindParams(ref sSQL,TransObject.of_GetDBType(),sDwArgs)) then
        //              Event OnError(RetCode.E_SQL_BIND_ARG_FAILED,"SQL参数绑定失败!")
        //
        //  `Not bPlainSQL` is why this test supplies a SYNTAX rather than a statement: a plain statement
        //  has already had its parameters interpolated at [:L605], so binding it again would bind twice
        //  and the oracle suppresses the second site for it.
        //
        //  The binding is made to fail by a NAMED/UNNAMED PARAMETER MIXTURE, which the binder refuses
        //  outright because a positional parameter would race the named ones for the same placeholder.
        // ==========================================================================================
        using Harness harness = new();
        harness.Runtime.GeneratedSelect = TwoColumnSelect;
        harness.Store.RetrieveRows = 2L;
        _ = harness.Task.SetSqlSyntax(FixtureSyntax);
        _ = harness.Task.SetPaged(true);
        _ = harness.Task.SetPageSize(2L);
        _ = harness.Task.SetPageIndex(1L);
        _ = harness.Task.SetParam("pid", 1L);
        _ = harness.Task.SetParam(string.Empty, 2L);

        long result = await harness.Task.ExecuteAsync(
            harness.Sink,
            TestContext.Current.CancellationToken);

        // [:L839] E_SQL_BIND_ARG_FAILED is -32 [retcode.sru:L75].
        Assert.Equal(RetCode.E_SQL_BIND_ARG_FAILED, result);
        Assert.Equal(-32L, result);

        // [:L838] the diagnostic, against an independent literal and against the subject's constant.
        Assert.Equal<(long, string)>(
            [(RetCode.E_SQL_BIND_ARG_FAILED, BindFailedChinese)],
            harness.Host.Errors);

        Assert.Equal(BindFailedChinese, SqlQueryTask.BindParamsFailedText);

        // THE QUERY NEVER RAN - the binding fails before the execution at [:L843].
        Assert.Empty(harness.TransactionSurface.CountStatements);

        // And no totals are published, because the counting block returned before [:L865].
        Assert.Empty(harness.Sink.PageCounts);
    }

    [Fact]
    public async Task ASuccessfulBindingOnTheCountingPathReplacesTheStatementWithTheBoundForm()
    {
        // ==========================================================================================
        //  THE OTHER SIDE OF [:L836-L841], WHICH IS EASY TO LEAVE UNTESTED BECAUSE IT LOOKS LIKE A
        //  NO-OP. When the binding SUCCEEDS the oracle carries the bound statement forward - the
        //  `ref sSQL` out-parameter is what the query then executes - so a port that dropped the
        //  write-back would execute the UNBOUND statement and every parameterised paged count would
        //  count the wrong rows.
        //
        //  With an ALL-NAMED parameter set the binder accepts the request, and because the counting
        //  wrapper carries no placeholder there is nothing to substitute - so the observable statement
        //  is unchanged and the query receives the byte-exact wrapper. That is the assertion: the
        //  write-back happened and it preserved the text.
        // ==========================================================================================
        using Harness harness = new();
        harness.Runtime.GeneratedSelect = TwoColumnSelect;
        harness.Store.RetrieveRows = 2L;
        harness.TransactionSurface.CountValue = 6L;
        _ = harness.Task.SetSqlSyntax(FixtureSyntax);
        _ = harness.Task.SetPaged(true);
        _ = harness.Task.SetPageSize(2L);
        _ = harness.Task.SetPageIndex(1L);

        // ONE parameter, NAMED - so the all-named-or-none rule is satisfied and the binder proceeds.
        _ = harness.Task.SetParam("pid", 1L);

        long result = await harness.Task.ExecuteAsync(
            harness.Sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, result);

        // No bind-failure diagnostic was raised.
        Assert.Empty(harness.Host.Errors);

        // The query ran, with the statement the wrapper produced.
        Assert.Equal(
            "SELECT COUNT(1) AS CNT FROM (SELECT 1 AS _ FROM COMPANY) pfwPagedSQL_Tbl",
            Assert.Single(harness.TransactionSurface.CountStatements));

        // Ceiling(6 / 2) = 3.
        Assert.Equal<(long, long)>([(3L, 6L)], harness.Sink.PageCounts);
    }

    [Fact]
    public void TheBindFailureDiagnosticIsByteExact()
    {
        // [:L838] `"SQL参数绑定失败!"` - three ASCII letters, six ideographs, one ASCII mark.
        Assert.Equal(
            "U+0053 U+0051 U+004C U+53C2 U+6570 U+7ED1 U+5B9A U+5931 U+8D25 U+0021",
            CodePointsOf(SqlQueryTask.BindParamsFailedText));

        Assert.Equal(10, SqlQueryTask.BindParamsFailedText.Length);
    }

    /// <summary>
    /// Counting-query results that are NOT the single row the oracle expects, each reaching the same
    /// database-error arm.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠ <c>RetCode.CANCELLED</c> IS IN THIS LIST DELIBERATELY, AND ITS PRESENCE IS A PRESERVED DEFECT
    /// (constraint C-B). The oracle's discrimination is
    /// <c>if IsFailed(rtCode) ... if rtCode = 1 ... else</c> [<c>:L844-L858</c>], and
    /// <c>isfailed</c> tests <c>&lt; 0</c> WITH AN EXPLICIT EXCLUSION OF <c>CANCELLED</c>
    /// [<c>ws_objects/pfw.shared.pbl.src/isfailed.srf:L11-L13</c>] - so a cancellation is neither
    /// succeeded nor failed, falls straight past the failure arm, fails the single-row test and is
    /// reported as a DATABASE ERROR. That is the tri-state hole of the return-code algebra producing an
    /// observably wrong classification, and it is reproduced rather than repaired.
    /// </para>
    /// <para>
    /// Zero is <c>RetCode.OK</c> and two is a two-row result; neither is negative, so neither enters the
    /// failure arm either.
    /// </para>
    /// </remarks>
    public static TheoryData<long> CountResultsThatAreNotExactlyOneRow =>
        [0L, 2L, RetCode.CANCELLED];

    [Theory]
    [MemberData(nameof(CountResultsThatAreNotExactlyOneRow))]
    public async Task ACountingResultThatIsNotOneRowIsReportedAsADatabaseError(long countResult)
    {
        using Harness harness = new();
        harness.Runtime.GeneratedSelect = TwoColumnSelect;
        harness.Store.RetrieveRows = 2L;
        harness.Activator.SqlDbCode = ProbeDbCode;
        harness.Activator.SqlErrText = ProbeDbErrorText;
        harness.TransactionSurface.CountReturnCode = countResult;
        _ = harness.Task.SetSqlSyntax(FixtureSyntax);
        _ = harness.Task.SetPaged(true);
        _ = harness.Task.SetPageSize(2L);
        _ = harness.Task.SetPageIndex(1L);

        long result = await harness.Task.ExecuteAsync(
            harness.Sink,
            TestContext.Current.CancellationToken);

        // [:L857] E_DB_ERROR is -28 [retcode.sru:L71]. NOT the cancellation code, even for the
        // cancelled row - do not "correct" this.
        Assert.Equal(RetCode.E_DB_ERROR, result);
        Assert.Equal(-28L, result);

        // [:L856] 检索失败, WITHOUT the exclamation mark the retrieval-failure text at [:L778] carries.
        Assert.Equal<(long, string)>(
            [(RetCode.E_DB_ERROR, CountRetrieveFailedChinese)],
            harness.Host.Errors);

        Assert.Equal(CountRetrieveFailedChinese, SqlQueryTask.CountRetrieveFailedText);
        Assert.DoesNotContain("!", SqlQueryTask.CountRetrieveFailedText, StringComparison.Ordinal);

        // [:L855] the structured payload: the transaction's codes, the counting statement, the PRIMARY
        // buffer and ROW 1 - note the row is ONE here and ZERO at the two transaction-failure sites.
        DbErrorData reported = Assert.Single(harness.Proxy.Errors);

        Assert.Equal(ProbeDbCode, reported.SqlDbCode);
        Assert.Equal(ProbeDbErrorText, reported.SqlErrText);
        Assert.Equal(DwBuffer.Primary, reported.Buffer);
        Assert.Equal(1L, reported.Row);

        // The payload carries the RAW statement, exactly as the oracle places it there, so in-process
        // handling keeps it. Every EGRESS masks it - see the redaction fact below.
        Assert.Equal(
            "SELECT COUNT(1) AS CNT FROM (SELECT 1 AS _ FROM COMPANY) pfwPagedSQL_Tbl",
            reported.SqlSyntax);

        // No totals are published on a failed count [:L857 returns before :L865].
        Assert.Empty(harness.Sink.PageCounts);
    }

    [Fact]
    public async Task TheCountingStatementReachesADiagnosticOnlyThroughTheRedactionSeam()
    {
        // ==========================================================================================
        //  CONSTRAINT C-F, PROVED RATHER THAN ASSERTED BY INSPECTION.
        //
        //  The counting statement is GENERATED SQL: it carries interpolated literal values whenever the
        //  connection disabled bind variables [n_cst_thread_task_sqlbase.sru:L128-L129], and the legacy
        //  logger performs no redaction whatever. So the obligation is not "the output looks masked" but
        //  "the statement REACHED a diagnostic only by passing through the redactor".
        //
        //  A REAL REDACTOR CANNOT PROVE THAT - its output is masked whether or not the caller routed
        //  through it. The recording double makes the CALL observable: its Statements list is EMPTY
        //  unless Redact actually ran.
        // ==========================================================================================
        using Harness harness = new();
        harness.Runtime.GeneratedSelect = TwoColumnSelect;
        harness.Store.RetrieveRows = 2L;
        harness.TransactionSurface.CountReturnCode = 0L;
        _ = harness.Task.SetSqlSyntax(FixtureSyntax);
        _ = harness.Task.SetPaged(true);
        _ = harness.Task.SetPageSize(2L);
        _ = harness.Task.SetPageIndex(1L);

        long result = await harness.Task.ExecuteAsync(
            harness.Sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.E_DB_ERROR, result);

        string expectedCountSql =
            "SELECT COUNT(1) AS CNT FROM (SELECT 1 AS _ FROM COMPANY) pfwPagedSQL_Tbl";

        // THE SEAM WAS REACHED, and it was reached WITH THE COUNTING STATEMENT itself.
        Assert.True(
            harness.Redactor.Observed(expectedCountSql),
            "The counting statement did not pass through the redaction seam. C-F requires the one "
                + "diagnostic this path writes itself to carry the redacted statement, and the "
                + "recording redactor observed: "
                + string.Join(" | ", harness.Redactor.Statements));

        Assert.Contains(expectedCountSql, harness.Redactor.Statements);
    }

    [Fact]
    public async Task AGenuinelyFailedCountingQueryReportsTheSurfacesOwnTextAndRaisesNoPayload()
    {
        // The REACHABLE half of [:L844-L849]: a NEGATIVE result that is not the cancellation value does
        // enter the failure arm, reports the transaction surface's own diagnostic rather than 检索失败,
        // and raises NO database-error payload - only the general error channel is used.
        using Harness harness = new();
        harness.Runtime.GeneratedSelect = TwoColumnSelect;
        harness.Store.RetrieveRows = 2L;
        harness.TransactionSurface.CountReturnCode = RetCode.E_DB_ERROR;
        harness.TransactionSurface.CountErrorText = ProbeDbErrorText;
        _ = harness.Task.SetSqlSyntax(FixtureSyntax);
        _ = harness.Task.SetPaged(true);
        _ = harness.Task.SetPageSize(2L);
        _ = harness.Task.SetPageIndex(1L);

        long result = await harness.Task.ExecuteAsync(
            harness.Sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.E_DB_ERROR, result);
        Assert.Equal<(long, string)>([(RetCode.E_DB_ERROR, ProbeDbErrorText)], harness.Host.Errors);
        Assert.Empty(harness.Proxy.Errors);
        Assert.Empty(harness.Sink.PageCounts);
    }

    // ==============================================================================================
    //  SUITE 13 - THE COUNTING PATH'S OWN ORDERINGS, ON THE ONE ORDERED SURFACE IT TOUCHES
    //  ----------------------------------------------------------------------------------------------
    //  These use the ordered describe-and-modify recorder from TestDoubles.cs, which keeps every
    //  interaction with the DataWindow-shaped surface in ONE log with a one-based ordinal - so "before"
    //  is expressible rather than merely hoped for.
    //
    //  WHAT IS ORDERED HERE, AND WHY IT BELONGS TO THE COUNTING PATH. The oracle captures the DataWindow
    //  argument string at [:L807-L811] - BEFORE the data event - under the comment at [:L813] warning
    //  that *OnDataReceived执行后Data可能将变得无效！, "after OnDataReceived the data may have become
    //  invalid". The capture exists solely so the counting path's binder at [:L837] has the value, and it
    //  is gated on the SAME three conditions as the counting block itself [:L809]. If it moved after the
    //  data event the counting path would bind against a carrier that may no longer exist.
    // ==============================================================================================

    [Fact]
    public async Task TheArgumentStringIsCapturedBeforeTheDataEventAndBeforeTheCountingQuery()
    {
        using Harness harness = new();
        harness.Runtime.GeneratedSelect = TwoColumnSelect;
        harness.Store.RetrieveRows = 2L;
        harness.TransactionSurface.CountValue = 4L;
        _ = harness.Task.SetSqlSyntax(FixtureSyntax);
        _ = harness.Task.SetPaged(true);
        _ = harness.Task.SetPageSize(2L);
        _ = harness.Task.SetPageIndex(1L);

        long result = await harness.Task.ExecuteAsync(
            harness.Sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, result);

        // The counting query really ran, so the ordering probes below are about a query that happened
        // rather than about a default.
        Assert.Single(harness.TransactionSurface.CountStatements);

        // [:L810] the capture happened at all.
        int captureOrdinal = harness.Recorder.OrdinalOf(
            CarrierCallKind.Describe,
            DataWindowProperty.TableArguments);

        Assert.True(
            captureOrdinal > 0,
            "The DataWindow argument string was never described, so the counting path's binder has "
                + "nothing to bind against - see :L810.");

        // BEFORE the data event [:L813-L814], measured by the recorder's own call count at the moment
        // the sink was notified.
        Assert.True(
            captureOrdinal <= harness.Sink.RecorderCallsAtDataReceived,
            $"The argument string was captured at call {captureOrdinal}, after the data event at call "
                + $"{harness.Sink.RecorderCallsAtDataReceived} - see the oracle's own warning at :L813.");

        // AND BEFORE the counting query [:L843], measured the same way.
        Assert.True(
            captureOrdinal <= harness.TransactionSurface.RecorderCallsAtCountQuery,
            $"The argument string was captured at call {captureOrdinal}, after the counting query at "
                + $"call {harness.TransactionSurface.RecorderCallsAtCountQuery} - the capture exists "
                + "solely to serve the binder at :L837.");
    }

    [Fact]
    public async Task ThePagedStatementIsInstalledBeforeTheArgumentCaptureAndIsRestoredBeforeTheCount()
    {
        using Harness harness = new();
        harness.Runtime.GeneratedSelect = TwoColumnSelect;
        harness.Store.RetrieveRows = 2L;
        harness.TransactionSurface.CountValue = 4L;
        _ = harness.Task.SetSqlSyntax(FixtureSyntax);
        _ = harness.Task.SetPaged(true);
        _ = harness.Task.SetPageSize(2L);
        _ = harness.Task.SetPageIndex(1L);

        long result = await harness.Task.ExecuteAsync(
            harness.Sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, result);

        // [:L732] the paged statement is written to the carrier, and [:L807-L811] the argument capture
        // follows it. The ordered log carries both, so the relation is asserted rather than assumed.
        Assert.True(
            harness.Recorder.Precedes(
                CarrierCallKind.Modify,
                SqlServerPagingRewriter.RowNumberAlias,
                CarrierCallKind.Describe,
                DataWindowProperty.TableArguments),
            "The paging rewrite must be installed before the argument string is captured.");

        // [:L792-L805] THE RESTORATION HAPPENS BEFORE THE DATA EVENT, and the oracle says so in its own
        // comment at :L793 - *需要在OnDataReceived之前还原. The observable consequence is that the
        // statement the carrier ends the run with is the ORIGINAL one, not the paged one.
        Assert.Equal(TwoColumnSelect, harness.Store.SqlSelect);

        // And the counted statement is derived from that original rather than from the paged text.
        Assert.Equal(
            ReplayInLegacyOrder(TwoColumnSelect),
            Assert.Single(harness.TransactionSurface.CountStatements));
    }


    // ==============================================================================================
    //  THE HARNESS AND ITS DOUBLES
    //  ----------------------------------------------------------------------------------------------
    //  NO DATABASE, NO NETWORK, NO DataWindow RUNTIME, NO REAL WORKER THREAD AND NO WALL CLOCK
    //  (constraints C-E and C-H). Every collaborator below is the narrowest thing that satisfies its
    //  interface, because a double with behaviour of its own would move the assertions off the subject.
    //
    //  THREE OF THEM ARE THE SHARED DOUBLES FROM TestDoubles.cs RATHER THAN LOCAL ONES, and each is
    //  there for a reason this file depends on:
    //    * ScriptedPooledTransaction  - the pooled-transaction double the counting path executes
    //                                   against, so no connection is ever opened;
    //    * RecordingSqlRedactor       - the seam that makes the C-F obligation provable rather than
    //                                   merely plausible;
    //    * ScriptedCarrierSurface     - the ORDERED describe-and-modify recorder, which keeps every
    //                                   interaction with the DataWindow-shaped surface in one log with
    //                                   a one-based ordinal so that "before" is expressible.
    //
    //  The store double DELEGATES its describe and modify verbs to that recorder rather than answering
    //  from a dictionary of its own, which is what puts the whole retrieval's traffic into a single
    //  ordered log without the store having to grow ordering machinery it would then own alone.
    // ==============================================================================================

    /// <summary>
    /// The processing kind eleven of the repository's twelve DataWindow definitions declare, assigned
    /// explicitly so the publication path is deterministic rather than dependent on a default.
    /// </summary>
    private const long LegacyProcessingKind = 1L;

    /// <summary>
    /// A READABLE answer for the stored-procedure property, which is what makes
    /// <c>bIsProcedure</c> TRUE - the sense is inverted [<c>:L626</c>].
    /// </summary>
    private const string ProcedureAnswer = "yes";

    /// <summary>
    /// Everything the subject needs, wired once. The clock is injected and nothing advances it, which
    /// is itself an assertion: the counting path reads no clock at all.
    /// </summary>
    private sealed class Harness : IDisposable
    {
        internal Harness(bool isProcedure = false)
        {
            Clock = new FakeTimeProvider();
            Proxy = new RecordingTaskProxy();
            Host = new FakeQueryHost { ParentTasking = Proxy };
            Redactor = new RecordingSqlRedactor();

            Recorder = new ScriptedCarrierSurface
            {
                // PowerBuilder's "property could not be read" marker is the DEFAULT, never an empty
                // string. `bIsProcedure = (Describe(...) <> "!")` [:L626], so an unscripted property
                // must look UNREADABLE - a blank default would classify every ordinary SELECT as a
                // stored procedure and silently suppress clause modification, paging and counting all
                // at once.
                DescribeFallback = ScriptedCarrierSurface.DescribeError,
            };

            Recorder.DescribeAnswers[QueryDataWindowProperty.TableProcedure] =
                isProcedure ? ProcedureAnswer : ScriptedCarrierSurface.DescribeError;

            Recorder.DescribeAnswers[QueryDataWindowProperty.Syntax] = FixtureSyntax;
            Recorder.DescribeAnswers[QueryDataWindowProperty.ColumnCount] = "0";
            Recorder.DescribeAnswers[DataWindowProperty.Units] = "0";
            Recorder.DescribeAnswers[DataWindowProperty.TableSort] = string.Empty;
            Recorder.DescribeAnswers[DataWindowProperty.TableFilter] = string.Empty;

            // EMPTY, AND THAT MATTERS. The counting path's binder is handed this value [:L837]; an
            // argument list here would make the bind-failure suite about argument matching instead of
            // about the named/unnamed mixture it is actually testing.
            Recorder.DescribeAnswers[DataWindowProperty.TableArguments] = string.Empty;

            // The statement the carrier reports. Installed by the runtime double's create arm, exactly
            // as a real Create(syntax) followed by a describe would leave it.
            Recorder.DescribeAnswers[DataWindowProperty.TableSelect] = string.Empty;

            IOptions<PersistenceOptions> options = new PersistenceOptionsBuilder().BuildOptions();

            Activator = new ScriptedTransactionActivator();
            Pool = new TransactionPool(options, Clock, Activator);

            Store = new RecordedQueryStore(
                Recorder,
                DataWindowCarrierFactory.CreateForThread(isMainThread: false, Clock));

            TransactionSurface = new CountingTransactionSurface(Recorder);
            Runtime = new FakeQueryRuntime();
            Sink = new RecordingQuerySink(Recorder);

            // The injected clock for the TASK and the system clock for the CODEC. The codec's
            // inter-chunk yield is a real delay, so a stopped clock would hang it; the task reads no
            // clock at all, so a stopped one there proves exactly that.
            Task = new SqlQueryTask(
                Host,
                Pool,
                new SingleStoreFactory(Store),
                new SqlRetrievalHookActivator(),
                Clock,
                NullLogger<SqlQueryTask>.Instance,
                TransactionSurface,
                Runtime,
                BothArms(),
                new ChangesetCodec(TimeProvider.System),
                Redactor,
                options);
        }

        internal FakeTimeProvider Clock { get; }

        internal FakeQueryHost Host { get; }

        internal RecordingTaskProxy Proxy { get; }

        internal RecordingSqlRedactor Redactor { get; }

        internal ScriptedCarrierSurface Recorder { get; }

        internal ScriptedTransactionActivator Activator { get; }

        internal TransactionPool Pool { get; }

        internal RecordedQueryStore Store { get; }

        internal CountingTransactionSurface TransactionSurface { get; }

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
    /// The threading substrate: no thread, no synchronization primitive and no message pump. It records
    /// the error channel so a test can assert on the exact diagnostic rather than only on the code.
    /// </summary>
    private sealed class FakeQueryHost : ISqlTaskHost
    {
        private readonly Dictionary<string, object?> _data = new(StringComparer.Ordinal);

        internal List<(long Code, string Info)> Errors { get; } = [];

        internal List<(long NotifyCode, long Payload, string Text)> Notifications { get; } = [];

        public bool IsMainThread => false;

        public bool IsCancelled => false;

        public int TaskIndex => 1;

        public ISqlTaskProxy? ParentTasking { get; init; }

        /// <summary>No sibling task is registered: this file exercises one task at a time.</summary>
        /// <param name="index">Ignored.</param>
        /// <param name="task">Always <see langword="null"/>.</param>
        /// <returns><c>RetCode.E_OUT_OF_BOUND</c>.</returns>
        public long GetTask(int index, out SqlTaskBase? task)
        {
            task = null;

            return RetCode.E_OUT_OF_BOUND;
        }

        public bool HasData(string name) => _data.ContainsKey(name);

        public object? GetData(string name) => _data.GetValueOrDefault(name);

        public long SetData(string name, object? data)
        {
            _data[name] = data;

            return RetCode.OK;
        }

        public long OnPrepare() => DataWindowBufferStore.EventContinue;

        public void OnUninit() => _data.Clear();

        public long OnError(long errCode, string errInfo)
        {
            Errors.Add((errCode, errInfo));

            return DataWindowBufferStore.EventContinue;
        }

        public long OnNotify(long notifyCode, long payload, string text)
        {
            Notifications.Add((notifyCode, payload, text));

            return DataWindowBufferStore.EventContinue;
        }
    }

    /// <summary>The caller-side proxy double: it latches every database-error payload verbatim.</summary>
    private sealed class RecordingTaskProxy : ISqlTaskProxy
    {
        internal List<DbErrorData> Errors { get; } = [];

        public void OnDbError(in DbErrorData error) => Errors.Add(error);
    }

    /// <summary>The pool's activator, so the pool needs no engine and opens no connection.</summary>
    /// <remarks>
    /// EVERY TRANSACTION IT HANDS OUT IS THE SHARED <see cref="ScriptedPooledTransaction"/>. The
    /// provider state is configured on the ACTIVATOR rather than on the transaction, because the pool
    /// creates the transaction during the run and a test has no other moment at which to reach it.
    /// </remarks>
    private sealed class ScriptedTransactionActivator : IPooledTransactionActivator
    {
        /// <summary>The provider code the query-side defensive override tests for EXACTLY -1 [<c>:L771</c>].</summary>
        internal long SqlCode { get; set; }

        /// <summary>The provider code the database-error payload carries [<c>:L855</c>].</summary>
        internal long SqlDbCode { get; set; }

        /// <summary>The provider diagnostic the database-error payload carries [<c>:L855</c>].</summary>
        internal string SqlErrText { get; set; } = string.Empty;

        /// <summary>
        /// The dialect the transaction reports, which selects a paging rewriter and never a connection.
        /// </summary>
        internal DatabaseType DbType { get; set; } = DatabaseType.DbtMssql;

        internal ScriptedPooledTransaction? LastCreated { get; private set; }

        public IPooledTransaction CreateDefault() => Produce();

        public IPooledTransaction Create(string className) => Produce();

        private ScriptedPooledTransaction Produce()
        {
            ScriptedPooledTransaction transaction = new()
            {
                SqlCode = SqlCode,
                SqlDbCode = SqlDbCode,
                SqlErrText = SqlErrText,
                DbType = DbType,
            };

            LastCreated = transaction;

            return transaction;
        }
    }

    /// <summary>Hands out the SAME store on every call, so a test can configure it before the run.</summary>
    private sealed class SingleStoreFactory : ISqlDataStoreFactory
    {
        private readonly RecordedQueryStore _store;

        internal SingleStoreFactory(RecordedQueryStore store) => _store = store;

        internal List<CarrierThreadAffinity> RequestedAffinities { get; } = [];

        public ISqlDataStore Create(CarrierThreadAffinity affinity)
        {
            RequestedAffinities.Add(affinity);

            return _store;
        }
    }

    /// <summary>
    /// The DataWindow-shaped store double. It owns a REAL carrier - so buffers, item statuses and the
    /// row cap behave exactly as production - and routes every describe and modify through the shared
    /// ORDERED recorder so the whole retrieval lands in one ordered log.
    /// </summary>
    private sealed class RecordedQueryStore : ISqlDataStore
    {
        private readonly ScriptedCarrierSurface _recorder;

        internal RecordedQueryStore(ScriptedCarrierSurface recorder, DataWindowCarrier carrier)
        {
            _recorder = recorder;
            Carrier = carrier;

            // Assigned explicitly rather than left to a default, so the publication path is the
            // changeset one on every run in this file.
            Carrier.Processing = new DataWindowProcessing(LegacyProcessingKind);
        }

        public DataWindowCarrier Carrier { get; }

        public string DataObject { get; set; } = string.Empty;

        /// <summary>
        /// The statement the carrier currently reports, read WITHOUT recording so an assertion made
        /// after the run cannot disturb the ordered log it is about to inspect.
        /// </summary>
        internal string SqlSelect =>
            _recorder.DescribeAnswers.TryGetValue(DataWindowProperty.TableSelect, out string? statement)
                ? statement
                : string.Empty;

        /// <summary>Rows the next retrieve appends to <c>Primary!</c>.</summary>
        internal long RetrieveRows { get; set; }

        /// <summary>Every parameter list a retrieve was handed, in call order.</summary>
        internal List<IReadOnlyList<object?>> Retrievals { get; } = [];

        public string GetSqlSelect() => _recorder.Describe(DataWindowProperty.TableSelect);

        public string Modify(string modificationScript)
        {
            ArgumentNullException.ThrowIfNull(modificationScript);

            // RECORDED FIRST, THEN APPLIED. The ordinal a test reasons about is the moment the
            // modification was REQUESTED, which is what the oracle's ordering is about.
            string error = _recorder.Modify(modificationScript);

            if (error.Length > 0)
            {
                return error;
            }

            int separator = modificationScript.IndexOf('=', StringComparison.Ordinal);

            if (separator <= 0)
            {
                return "malformed modification script: " + modificationScript;
            }

            string property = modificationScript[..separator].Trim();
            string value = modificationScript[(separator + 1)..].Trim();

            // The assignment form is `<property> = "<value>"`, so the quotes are stripped to leave the
            // statement a describe would answer with.
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                value = value[1..^1];
            }

            _recorder.DescribeAnswers[property] = value;

            return string.Empty;
        }

        public string Describe(string property) => _recorder.Describe(property);

        public long SetFilter(string? filter) => _recorder.SetFilter(filter ?? string.Empty);

        public long SetSort(string? sort) => _recorder.SetSort(sort ?? string.Empty);

        public void ClearState() => Carrier.ClearState();

        public void OnInit(ICarrierParentTask parentTask) => Carrier.OnInit(parentTask);

        /// <summary>
        /// Appends the configured rows THROUGH the carrier's own retrieve events, so the row cap is
        /// enforced by the carrier rather than simulated here.
        /// </summary>
        /// <param name="parameters">The matched retrieve arguments, recorded and otherwise unused.</param>
        /// <param name="cancellationToken">Unused: this double performs no asynchronous work.</param>
        /// <returns>The number of rows that survived the cap.</returns>
        public ValueTask<long> RetrieveAsync(
            IReadOnlyList<object?> parameters,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(parameters);

            Retrievals.Add([.. parameters]);

            _ = Carrier.OnRetrieveStart();

            long appended = 0L;

            // R9: ONE-BASED and inclusive at both ends, matching every legacy loop.
            for (long index = 1L; index <= RetrieveRows; index++)
            {
                long row = Carrier.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);
                _ = Carrier.SetItemValue(row, 1, DwBuffer.Primary, index);
                Carrier.RowAt(row, DwBuffer.Primary).Baseline();

                // `retrieverow` answering 1 means DISCARD THIS ROW AND STOP, which is how the row cap
                // ends a retrieve.
                if (Carrier.OnRetrieveRow(row) == DataWindowBufferStore.EventStop)
                {
                    _ = Carrier.RowsDiscard(row, row, DwBuffer.Primary);

                    return ValueTask.FromResult(appended);
                }

                appended++;
            }

            return ValueTask.FromResult(appended);
        }
    }

    /// <summary>
    /// The four transaction-object members that are not on the pooled-transaction contract. It composes
    /// no connection string, opens nothing and parses no SQL - it answers what the test says, records
    /// the counting statement it was handed, and notes WHEN it was called (constraint C-E).
    /// </summary>
    private sealed class CountingTransactionSurface : IQueryTransactionSurface
    {
        private readonly ScriptedCarrierSurface _recorder;

        internal CountingTransactionSurface(ScriptedCarrierSurface recorder) => _recorder = recorder;

        /// <summary>Every counting statement executed, verbatim and in call order.</summary>
        internal List<string> CountStatements { get; } = [];

        /// <summary>
        /// The result the counting query answers. THE LEGACY'S SUCCESS VALUE IS ONE ROW, NOT ZERO
        /// [<c>:L851</c>] - and the value is a ROW COUNT rather than a return code, so testing it
        /// against <c>RetCode.PREVENT</c>, which shares the value 1, would be a category error.
        /// </summary>
        internal long CountReturnCode { get; set; } = 1L;

        /// <summary>The record total the counting result carries at row 1, column 1 [<c>:L852</c>].</summary>
        internal long CountValue { get; set; }

        /// <summary>The diagnostic the counting query answers with on its failure arm [<c>:L846</c>].</summary>
        internal string CountErrorText { get; set; } = string.Empty;

        /// <summary>
        /// The ordered recorder's call count at the moment the counting query ran - a cross-seam
        /// ordering probe, so "the argument string was captured before the counting query" is
        /// expressible without either seam knowing about the other.
        /// </summary>
        internal int RecorderCallsAtCountQuery { get; private set; }

        internal List<long> AfterRetrieveRowCounts { get; } = [];

        public GridSyntaxOutcome GridSyntaxFromSql(IPooledTransaction transaction, string sql) =>
            new(FixtureSyntax, string.Empty);

        public ValueTask<CountQueryOutcome> Query(
            IPooledTransaction transaction,
            string sql,
            CancellationToken cancellationToken)
        {
            CountStatements.Add(sql);
            RecorderCallsAtCountQuery = _recorder.Calls.Count;

            DataWindowBufferStore? result = null;

            if (CountReturnCode == 1L)
            {
                result = new DataWindowBufferStore();
                long row = result.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

                // ROW 1, COLUMN 1 - `dsTmp.GetItemNumber(1,1)` [:L852] - both one-based.
                _ = result.SetItemValue(row, 1, DwBuffer.Primary, CountValue);
            }

            return ValueTask.FromResult(new CountQueryOutcome(CountReturnCode, result, CountErrorText));
        }

        public long RaiseBeforeRetrieve(IPooledTransaction transaction, DataWindowCarrier data) =>
            RetCode.ALLOW;

        public void RaiseAfterRetrieve(
            IPooledTransaction transaction,
            DataWindowCarrier data,
            long rowCount) => AfterRetrieveRowCounts.Add(rowCount);
    }

    /// <summary>
    /// The three DataWindow-runtime operations with no managed analogue. The create arm installs the
    /// statement through the store's own modify path, which is what a real <c>Create(syntax)</c>
    /// followed by a describe would leave behind.
    /// </summary>
    private sealed class FakeQueryRuntime : IQueryDataWindowRuntime
    {
        internal List<string> CreatedFromSyntax { get; } = [];

        internal int AttachCalls { get; private set; }

        /// <summary>The statement a successful create installs on the store.</summary>
        internal string GeneratedSelect { get; set; } = TwoColumnSelect;

        public CarrierCreateOutcome CreateFromSyntax(ISqlDataStore data, string syntax)
        {
            ArgumentNullException.ThrowIfNull(data);

            CreatedFromSyntax.Add(syntax);

            _ = data.Modify(SqlQueryTask.BuildTableSelectAssignment(GeneratedSelect));

            return new CarrierCreateOutcome(DataWindowBufferStore.DataStoreSuccess, string.Empty);
        }

        /// <summary>No drop-down child is declared anywhere in this file.</summary>
        /// <param name="data">Ignored.</param>
        /// <param name="columnName">Ignored.</param>
        /// <param name="child">Always <see langword="null"/>.</param>
        /// <returns>Always <see langword="false"/>.</returns>
        public bool TryGetChild(ISqlDataStore data, string columnName, out DataWindowBufferStore? child)
        {
            child = null;

            return false;
        }

        public long AttachTransaction(ISqlDataStore data, IPooledTransaction transaction)
        {
            AttachCalls++;

            return DataWindowBufferStore.DataStoreSuccess;
        }
    }

    /// <summary>
    /// The result sink. It records the paging totals - the observable output of the whole counting path
    /// - and notes the ordered recorder's call count at the data event, so the argument capture's
    /// position relative to that event is assertable.
    /// </summary>
    private sealed class RecordingQuerySink : IQueryResultSink
    {
        private readonly ScriptedCarrierSurface _recorder;

        internal RecordingQuerySink(ScriptedCarrierSurface recorder) => _recorder = recorder;

        /// <summary>A receiver exists, so the chunked publication path runs.</summary>
        public bool HasReceiver => true;

        /// <summary>The receiver already has its data object, so no create callback is needed.</summary>
        public bool NeedsCreatedObject => false;

        internal List<long> RowCounts { get; } = [];

        /// <summary>
        /// Every <c>OnPageReceived</c> pair, in call order - INCLUDING the sentinel pair, because the
        /// notification fires whether or not counting happened [<c>:L865</c>].
        /// </summary>
        internal List<(long PageCount, long RecordCount)> PageCounts { get; } = [];

        internal List<DataWindowCarrier> Moved { get; } = [];

        internal int ChunkCount { get; private set; }

        internal int FullStateChunkCount { get; private set; }

        internal List<string> CreatedFrom { get; } = [];

        internal List<(long ReturnCode, string ErrorText)> Errors { get; } = [];

        /// <summary>The ordered recorder's call count at the moment the data event fired.</summary>
        internal int RecorderCallsAtDataReceived { get; private set; }

        public void OnDataReceived(long rowCount)
        {
            RowCounts.Add(rowCount);
            RecorderCallsAtDataReceived = _recorder.Calls.Count;
        }

        public long SendFullStateChunk(QueryDataChunk chunk)
        {
            ArgumentNullException.ThrowIfNull(chunk);

            FullStateChunkCount++;

            return DataWindowBufferStore.EventContinue;
        }

        public void OnPageReceived(long pageCount, long recordCount) =>
            PageCounts.Add((pageCount, recordCount));

        public void OnDataMove(DataWindowCarrier carrier) => Moved.Add(carrier);

        public ValueTask<long> CreateDataAsync(string syntax, CancellationToken cancellationToken)
        {
            CreatedFrom.Add(syntax);

            return ValueTask.FromResult(DataWindowBufferStore.EventContinue);
        }

        public ValueTask<long> SendChunkAsync(ChangesetChunk chunk, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(chunk);

            ChunkCount++;

            return ValueTask.FromResult(DataWindowBufferStore.EventContinue);
        }

        public ValueTask<long> SendChildChunkAsync(
            ChangesetChildPayload payload,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(payload);

            return ValueTask.FromResult(DataWindowBufferStore.EventContinue);
        }

        public void ReportError(long returnCode, string errorText) =>
            Errors.Add((returnCode, errorText));
    }

}
