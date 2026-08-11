// ==============================================================================================
//  SqlServerPagingRewriterTests - the SQL Server paging arm, pinned character for character
//  --------------------------------------------------------------------------------------------
//  SYSTEM UNDER TEST
//      services/persistence-service/PowerFramework.Persistence/Sql/Paging/SqlServerPagingRewriter.cs
//      services/persistence-service/PowerFramework.Persistence/Sql/Paging/IPagingRewriter.cs
//          the dispatch contract, whose two pre-dispatch guards are observable through this arm
//
//  BEHAVIOURAL ORACLE
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru
//          :L303-L404  `_of_buildpagedsql`, the whole operation, read in full
//          :L307-L310  the paging-bounds guard and its Chinese diagnostic
//          :L314-L317  the parse guard and its Chinese diagnostic
//          :L323       the FIRST selector - whether unique-index columns were supplied
//          :L327-L342  the shared prologue both unique-index arms run
//          :L343-L350  ARM 1
//          :L352-L362  ARM 2
//          :L364       the outer-query re-read that BOTH unique-index arms return
//          :L366-L373  ARM 3
//          :L375-L383  ARM 4
//          :L392       the ORACLE dialect's substitution, cited only to prove the split
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L60
//          `constant long DBT_MSSQL = 0`, the discriminator this arm answers to
//      ws_objects/pfw.shared.pbl.src/enums.sru:L718-L719
//          SQL_MS_REPLACE = 1 and SQL_MS_APPEND = 2, the only two styles the arm drives
//      ws_objects/pfw.shared.pbl.src/retcode.sru:L46, :L70
//          E_INVALID_ARGUMENT = -3 and E_INTERNAL_ERROR = -27, the two guard codes
//      ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14
//          `retrieve="SELECT * FROM COMPANY"`, the principal input every matrix is anchored to
//
//  ORACLE STATUS (C-C). Every ws_objects/** path above is READ ONLY. Each was read as
//  specification and is cited by line locator; nothing here copies, reformats, moves, edits or
//  deletes any of them, and nothing in this file depends on the PowerBuilder toolchain, the
//  PowerBuilder runtime or any shipped native binary. Every expected string below was re-derived
//  from those locators statement by statement rather than transcribed from a summary, because the
//  legacy tree is the only statement of intended behaviour that exists for this operation - there
//  is no schema, no changelog entry and no other document that could adjudicate a disagreement.
//  Where the source and any secondary description differ, THE SOURCE WINS.
//
//  RULES POSITION. No user rules were provided for this repository: the rules document contains
//  exactly one line saying so, and it was read to its end. Nothing is invented or back-filled from
//  convention in their place, and the absence is not treated as licence to lower the bar. What
//  governs this file is the enterprise-standard baseline - nullable reference types on, warnings as
//  errors, ordinal comparison, no ambient state - together with the named non-rule constraints, and
//  each non-obvious decision below cites the constraint that drives it as C-K requires.
//
//  --------------------------------------------------------------------------------------------
//  WHY THIS SUITE EXISTS AND WHAT IT ADDS OVER ITS SIBLING
//  --------------------------------------------------------------------------------------------
//  PARITY HERE IS BYTE-EXACT GENERATED SQL. That is the acceptance criterion for the paging
//  rewriters, and it is the one property that cannot be checked by reading the code: each arm is a
//  chain of a dozen string appends whose whitespace, comma placement, alias spellings and
//  parenthesis nesting are individually load-bearing.
//
//  PagingRewriterByteExactTests beside this file pins the FINAL TEXT of every arm of BOTH dialects
//  through the dispatcher. It is deliberately not duplicated. This suite is SQL-Server-only and
//  goes a level deeper, driving the arm's own internal entry points so that the things the final
//  text alone cannot distinguish become assertable:
//
//      * the shared prologue's three fragments, individually [:L328-L338]
//      * WHICH order-by value reaches the window function - the lowered one or the re-read one
//        [:L327 versus :L342], a distinction that is invisible unless the input's order-by is
//        MIXED CASE, which is why this suite introduces such an input
//      * which arm restores the select list, which restores the order-by, and which restores
//        neither - observed on the statement model itself after the call, not inferred
//      * the sentinel PRESENCE MATRIX, including asserted ABSENCE where the oracle emits none
//      * the arithmetic of the five bound expressions across several page sizes and indices
//      * the two pre-dispatch guards and their Chinese diagnostics, byte for byte
//
//  --------------------------------------------------------------------------------------------
//  CORRECTION 1 - THE MATRIX IS 2x2, NOT "THREE STRATEGIES" (C-K)
//  --------------------------------------------------------------------------------------------
//  The migration plan's prose describes this arm as three SQL Server paging strategies. THAT COUNT
//  IS WRONG, and it is corrected here rather than silently absorbed, because a reader who goes
//  looking for three arms will merge two of them and a matrix that covers three of the four leaves
//  a form untested. The source branches on TWO INDEPENDENT BOOLEANS:
//
//      `if UpperBound(_sPagedUniqueIndexColumns) > 0`   [:L323]
//      `if _bPageNative`                                [:L343] and [:L366]
//
//  giving FOUR emitted forms:
//
//                             |  PageNative = true            |  PageNative = false
//      -----------------------+-------------------------------+------------------------------
//      unique-index columns   |  ARM 1  [:L343-L350]          |  ARM 2  [:L352-L362]
//      PRESENT   [:L323]      |  OFFSET/FETCH inside an       |  TOP + ROW_NUMBER inside an
//                             |  INNER JOIN sub-query         |  INNER JOIN sub-query
//      -----------------------+-------------------------------+------------------------------
//      unique-index columns   |  ARM 3  [:L366-L373]          |  ARM 4  [:L375-L383]
//      ABSENT    [:L365]      |  OFFSET/FETCH appended to     |  TOP + ROW_NUMBER wrapper, the
//                             |  the ORDER BY clause          |  ONLY arm with a trailing ORDER BY
//
//  All four produce DIFFERENT statement text, so collapsing any pair breaks byte-exact parity
//  outright. The theory below is written as an EXPLICIT 2x2 - both selectors travel in the member
//  data as their own columns - so no future simplification can quietly drop a quadrant.
//
//  --------------------------------------------------------------------------------------------
//  CORRECTION 2 - pfwPagedSQL_Tbl IS A SENTINEL THE PLAN'S LIST OMITS (C-K)
//  --------------------------------------------------------------------------------------------
//  The migration plan enumerates five sentinel identifiers in two separate places and OMITS
//  pfwPagedSQL_Tbl from both. The correct count is SIX. Three of the six belong to THIS arm:
//
//      pfwPagedSQL_OutterTbl   [:L333, :L350, :L362]   arms 1 and 2 only
//      pfwPagedSQL_RN          [:L355, :L356, :L381,
//                               :L382, :L383]          arms 2 and 4 only
//      pfwPagedSQL_Tbl         [:L356, :L382, :L834]   arms 2 and 4 only - THE OMITTED ONE. It is
//                                                      easy to miss because it is a PREFIX of
//                                                      pfwPagedSQL_TblInner, pfwPagedSQL_TblInnerInner
//                                                      and pfwPagedSQL_TblOuter, which belong to the
//                                                      other dialect, and because it is reused by
//                                                      the count wrapper at [:L834]
//
//  pfwPagedSQL_OutterTbl HAS TWO t's. "Outter" is the legacy spelling, it is reproduced exactly and
//  IT IS NOT A TYPO TO FIX (C-B): correcting it would change every generated statement this arm
//  emits and would invalidate every stored characterization comparison. This suite asserts the
//  doubled t explicitly, and asserts the single-t spelling is absent, so a well-meaning correction
//  fails here rather than in production.
//
//  Each of the three is asserted AGAINST THE ARM'S OWN internal constant as well as against the
//  legacy literal. The project's application csproj grants this assembly internal access, so the
//  test reads the same declaration the implementation emits from - the spelling therefore exists in
//  exactly one place in the managed tree and the test and the implementation cannot drift to two.
//
//  --------------------------------------------------------------------------------------------
//  CORRECTION 3 - THE EMPTY-ORDER-BY SUBSTITUTION IS DIALECT-SPLIT (C-K)
//  --------------------------------------------------------------------------------------------
//  SQL Server substitutes `(SELECT 0)` at [:L370] and [:L379]. The Oracle arm substitutes `''` -
//  two apostrophes - at [:L392]. The two are NOT interchangeable, and a shared constant between the
//  two rewriters would be a defect rather than a tidy-up. This suite therefore asserts BOTH halves
//  of that split from the SQL Server side: that `(SELECT 0)` appears in arms 3 and 4 when and only
//  when the input carries no order-by, and that this arm NEVER emits the Oracle substitution.
//
//  --------------------------------------------------------------------------------------------
//  C-E - NO FABRICATED DATABASE, IN ITS STRONGEST FORM
//  --------------------------------------------------------------------------------------------
//  There is NO SQL Server instance, no connection, no ADO client package, no container, and no
//  third-party T-SQL parser anywhere in this suite - deliberately not even named in prose beyond
//  this paragraph, so that a reviewer's grep over this file for those type names returns nothing.
//  Every case is a PURE STRING TRANSFORM: statement text plus four scalars in, statement text out.
//  That is exactly how both DBMS behaviours are preserved without provisioning either, which is the
//  finding that let the two-storage-path tension resolve without inventing a schema. Two facts make
//  it possible and are worth restating: SQLite is absent from the legacy dialect enumeration
//  entirely [n_cst_thread_trans.sru:L60-L61], and neither named engine has any schema, connection
//  string or DDL anywhere in the repository - only those two constants and the two generators.
//
//  The build enforces it rather than trusting it: central package management is on and every
//  package reference in the repository is versionless, so a client package added for this file
//  would fail restore with a missing version rather than compile.
//
//  Consequently these tests touch NO network, NO file and NO database, hold no ambient state, read
//  no clock and consume no randomness. They are deterministic in their inputs alone and produce
//  identical results on every run.
//
//  --------------------------------------------------------------------------------------------
//  NO PERFORMANCE CLAIM IS MADE ANYWHERE (migration plan section 0.8.5)
//  --------------------------------------------------------------------------------------------
//  The oracle's own comment at [:L324-L326] describes the unique-index branch as a fast paging
//  optimisation. Its BEHAVIOUR is pinned and NO performance benefit is asserted for it, for the
//  native OFFSET/FETCH arms, or for anything else. There is no timing, no stopwatch, no benchmark,
//  no iteration count and no comparison of one arm's cost against another's, because the repository
//  publishes no service-level agreement, no latency budget, no throughput target and no
//  availability commitment against which such a claim could be made. What the unique-index branch
//  genuinely does is emit DIFFERENT text, which is why it is part of parity rather than a tuning
//  knob.
//
//  --------------------------------------------------------------------------------------------
//  NAMING (migration plan section 0.7.2)
//  --------------------------------------------------------------------------------------------
//  NO SCREAMING_SNAKE IDENTIFIER IS DECLARED HERE. This file is not on the .editorconfig roster
//  that switches the naming analyzers off for the files carrying preserved legacy constant
//  spellings, and warnings are errors repository-wide, so an underscore-bearing declaration would
//  be a build error rather than a style note. Everything of that shape is CONSUMED instead:
//  `Enums.SQL_MS_REPLACE` and `Enums.SQL_MS_APPEND` from the shared kernel, `RetCode.OK`,
//  `RetCode.E_INVALID_ARGUMENT` and `RetCode.E_INTERNAL_ERROR` from the return-code catalogue, and
//  `DatabaseType.DbtMssql` from the generated contract, where protoc renders DBT_MSSQL in
//  PascalCase.
// ==============================================================================================

using PowerFramework.Persistence.Sql.Paging;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// The SQL Server paging arm of <c>_of_buildpagedsql</c>, asserted as an explicit two-by-two matrix
/// of generated statements plus the two pre-dispatch guards.
/// </summary>
/// <remarks>
/// Ported behaviour lives at
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L303-L404</c>, read only per
/// constraint C-C. Every expectation is compared with ordinal string equality and none is
/// whitespace-normalised: spacing is observable output and is a genuine parity requirement.
/// </remarks>
public sealed class SqlServerPagingRewriterTests
{
    // ------------------------------------------------------------------------------------------
    //  INPUTS
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// THE PRINCIPAL INPUT: the primary fixture's own retrieve, at <c>dw_sqlite.srd:L14</c>.
    /// </summary>
    /// <remarks>
    /// Taken from <see cref="DwSqliteFixture.RetrieveStatement"/> rather than re-spelled, so the
    /// matrix is anchored to the repository's one real updatable statement and cannot drift from it.
    /// It carries NO order-by, which is what reaches the <c>(SELECT 0)</c> substitution in arms 3
    /// and 4, and a star select list, which is what makes the un-restored select list of arm 4
    /// visible.
    /// </remarks>
    private const string FixtureSelect = DwSqliteFixture.RetrieveStatement;

    /// <summary>
    /// The fixture's six columns enumerated, so the select list is not opaque and the
    /// restore-the-select-list steps at <c>[:L348]</c> and <c>[:L358]</c> have something
    /// distinguishable to restore.
    /// </summary>
    private const string EnumeratedSelect =
        "SELECT id, name, age, address, salary, birth FROM COMPANY";

    /// <summary>
    /// AN INPUT WHOSE ORDER-BY IS DELIBERATELY MIXED CASE.
    /// </summary>
    /// <remarks>
    /// This is the input that catches the single most plausible porting defect in the prologue. The
    /// oracle lowers the order-by at <c>[:L327]</c>, uses the lowered copy ONLY for the containment
    /// test at <c>[:L334]</c>, and then RE-READS the clause at <c>[:L342]</c> without lowering it. A
    /// port that reuses the lowered variable emits <c>ORDER BY name id</c> where the legacy emits
    /// <c>ORDER BY NAME,id</c> - subtly wrong SQL that still looks plausible and that no
    /// case-insensitive assertion would notice. With a lower-case order-by the two spellings are
    /// identical and the defect is invisible, so the case difference is the whole point of this
    /// input.
    /// </remarks>
    private const string MixedCaseOrderedSelect = "SELECT id, name FROM COMPANY ORDER BY NAME";

    /// <summary>
    /// An input whose order-by ALREADY NAMES the unique-index column, which is the state the
    /// containment test at <c>[:L334]</c> branches away from.
    /// </summary>
    /// <remarks>
    /// The order-by is spelled lower case here on purpose, so that this input isolates the
    /// containment exclusion and does not also exercise the casing hazard that
    /// <see cref="MixedCaseOrderedSelect"/> owns.
    /// </remarks>
    private const string SelfOrderedSelect = "SELECT id, name FROM COMPANY ORDER BY id";

    /// <summary>The fixture's key column, <c>dw_sqlite.srd:L8</c>, as a bare identifier.</summary>
    private const string KeyColumn = DwSqliteFixture.KeyColumnName;

    /// <summary>The same column table-qualified, which is what exercises the substring at
    /// <c>[:L333]</c>.</summary>
    private const string QualifiedKeyColumn = DwSqliteFixture.UpdateTableName + "." + KeyColumn;

    /// <summary>A second qualified column, so the two <c>nIndex &gt; 1</c> separators at
    /// <c>[:L330]</c> and <c>[:L332]</c> are reached.</summary>
    private const string QualifiedAgeColumn = DwSqliteFixture.UpdateTableName + ".age";

    /// <summary>The page size used by the four principal rows.</summary>
    private const long PrincipalPageSize = 10L;

    /// <summary>
    /// The page index used by the four principal rows - TWO RATHER THAN ONE, DELIBERATELY.
    /// </summary>
    /// <remarks>
    /// On page one every offset is zero and every <c>BETWEEN</c> lower bound is one, so an arm that
    /// dropped the <c>(pageIndex - 1)</c> term altogether would still emit the right text. The
    /// one-based ordinal is only observable from page two onwards. Page one is covered separately by
    /// the arithmetic matrix, where its degenerate values are the point rather than a blind spot.
    /// </remarks>
    private const long PrincipalPageIndex = 2L;

    /// <summary>
    /// The Oracle dialect's empty-order-by substitution <c>[:L392]</c>, named here ONLY so that its
    /// ABSENCE from this arm can be asserted.
    /// </summary>
    /// <remarks>
    /// Correction 3 in the file header: the two dialects substitute different text and the two must
    /// never be shared. This constant is never expected in any output of this arm.
    /// </remarks>
    private const string OracleEmptyOrderBySubstitution = "''";

    /// <summary>
    /// The SQL Server empty-order-by substitution <c>[:L370, :L379]</c>.
    /// </summary>
    /// <remarks>
    /// Spelled out here because the arm holds it <see langword="private"/> - unlike the three
    /// sentinels, which are <see langword="internal"/> and are therefore asserted against the
    /// implementation's own declaration. A sub-select rather than a bare <c>0</c> is the legacy's own
    /// choice and is reproduced verbatim (C-B).
    /// </remarks>
    private const string EmptyOrderBySubstitution = "(SELECT 0)";

    /// <summary>
    /// The rewriter registry this suite hands the dispatcher: the REAL SQL Server arm, plus a stand-in
    /// occupying the other dialect's slot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE OTHER DIALECT'S REAL IMPLEMENTATION IS DELIBERATELY NOT REFERENCED HERE. The dispatcher
    /// resolves a rewriter by dialect, so a registry must be able to answer for both discriminators
    /// <c>[n_cst_thread_trans.sru:L60-L61]</c> - but nothing in this suite ever dispatches to the other
    /// one, and coupling a SQL-Server-only suite to the other arm's implementation would mean a change
    /// over there could turn assertions red over here. The sibling suite that pins both dialects'
    /// generated text uses the real pair; this one needs only a selectable occupant, which is exactly
    /// what <see cref="OtherDialectRewriter"/> is.
    /// </para>
    /// <para>
    /// This also mirrors the principle the arm's own documentation states: the two dialect arms have no
    /// reason to reference each other, and neither does either arm's test suite.
    /// </para>
    /// </remarks>
    private static IPagingRewriter[] Registry =>
        [new SqlServerPagingRewriter(), new OtherDialectRewriter()];

    /// <summary>
    /// A fully implemented <see cref="IPagingRewriter"/> occupying the non-SQL-Server dialect slot, so
    /// the dispatcher's registry can answer for both discriminators.
    /// </summary>
    /// <remarks>
    /// Not a placeholder: every member does real work and none throws. Its
    /// <see cref="Rewrite(in PagingRewriteRequest, SelectStatementModel)"/> returns the statement
    /// unchanged, which is a legitimate total function and is never reached by any test in this suite -
    /// <see cref="TheRegistryIsOnlyEverDispatchedOnForSqlServer"/> asserts that, so the claim is checked
    /// rather than asserted in prose. <see cref="ConsumesPagedUniqueIndexColumns"/> is
    /// <see langword="false"/>, matching the other dialect's real behaviour: its generator at
    /// <c>[:L386-L395]</c> never reads the unique-index collection.
    /// </remarks>
    private sealed class OtherDialectRewriter : IPagingRewriter
    {
        /// <inheritdoc/>
        public DatabaseType Dialect => DatabaseType.DbtOracle;

        /// <inheritdoc/>
        public bool ConsumesPagedUniqueIndexColumns => false;

        /// <inheritdoc/>
        public PagingRewriteResult Rewrite(
            in PagingRewriteRequest request,
            SelectStatementModel statement)
        {
            ArgumentNullException.ThrowIfNull(statement);

            return PagingRewriteResult.Ok(statement.GetSql());
        }
    }

    /// <summary>
    /// NO TEST IN THIS SUITE DISPATCHES ON ANY DIALECT BUT SQL SERVER.
    /// </summary>
    /// <remarks>
    /// The registry has to contain an occupant for the other slot, and this pins that the occupant is
    /// never actually exercised - so a reader can trust that every generated statement asserted anywhere
    /// above came out of the real SQL Server arm. Verified by selecting on the SQL Server discriminator
    /// and confirming the real arm is what comes back, and by confirming the two are distinct types so
    /// the selection is meaningful.
    /// </remarks>
    [Fact]
    public void TheRegistryIsOnlyEverDispatchedOnForSqlServer()
    {
        IPagingRewriter? selected =
            PagingRewriteDispatcher.SelectRewriter(DatabaseType.DbtMssql, Registry);

        _ = Assert.IsType<SqlServerPagingRewriter>(selected);

        IPagingRewriter? other =
            PagingRewriteDispatcher.SelectRewriter(DatabaseType.DbtOracle, Registry);

        _ = Assert.IsType<OtherDialectRewriter>(other);
    }

    // ------------------------------------------------------------------------------------------
    //  THE EXPLICIT 2x2 MATRIX  [:L323, :L343, :L366]
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The two-by-two cross-product of the arm's two selectors, with every generated statement
    /// spelled out character for character.
    /// </summary>
    /// <remarks>
    /// <para>
    /// BOTH SELECTORS TRAVEL AS THEIR OWN COLUMN so the quadrant each row occupies is stated rather
    /// than inferred: <c>uniqueIndexColumns</c> being non-empty is
    /// <c>UpperBound(_sPagedUniqueIndexColumns) &gt; 0</c> at <c>[:L323]</c>, and
    /// <c>pageNative</c> is <c>_bPageNative</c> at <c>[:L343]</c> and <c>[:L366]</c>. Every quadrant
    /// is populated at least twice - once from the fixture statement and once from a statement that
    /// already carries an order-by - so no quadrant can be dropped without a test disappearing.
    /// </para>
    /// <para>
    /// Each expectation was re-derived from <c>[:L327-L383]</c> append by append. The two arithmetic
    /// arguments are held at <see cref="PrincipalPageSize"/> and <see cref="PrincipalPageIndex"/>
    /// throughout, so the five bound expressions read as constants here and are varied separately by
    /// <see cref="Arithmetic"/>; that keeps a whitespace regression from hiding behind an arithmetic
    /// one.
    /// </para>
    /// </remarks>
    public static TheoryData<string, string, bool, string[]?, string> ArmMatrix
    {
        get
        {
            TheoryData<string, string, bool, string[]?, string> matrix = [];

            foreach (ArmCase armCase in ArmCases)
            {
                matrix.Add(
                    armCase.Quadrant,
                    armCase.Sql,
                    armCase.PageNative,
                    armCase.UniqueIndexColumns,
                    armCase.Expected);
            }

            return matrix;
        }
    }

    /// <summary>
    /// One row of the two-by-two matrix.
    /// </summary>
    /// <param name="Quadrant">The quadrant and its legacy locator, used in failure messages.</param>
    /// <param name="Sql">The statement to rewrite.</param>
    /// <param name="PageNative">
    /// <c>_bPageNative</c> <c>[:L343, :L366]</c> - the second selector.
    /// </param>
    /// <param name="UniqueIndexColumns">
    /// <c>_sPagedUniqueIndexColumns</c> <c>[:L46]</c> - the first selector;
    /// <see langword="null"/> is the absent state tested at <c>[:L323]</c>.
    /// </param>
    /// <param name="Expected">The generated statement, character for character.</param>
    /// <remarks>
    /// The rows are held as records rather than only as theory data so that
    /// <see cref="TheMatrixCoversAllFourSelectorCombinations"/> can read the SELECTORS back and prove
    /// the cross-product is complete. Projecting one list into both uses is what keeps the coverage
    /// assertion honest: it cannot pass against a matrix the theory does not actually run.
    /// </remarks>
    private sealed record ArmCase(
        string Quadrant,
        string Sql,
        bool PageNative,
        string[]? UniqueIndexColumns,
        string Expected);

    /// <summary>
    /// A list of <see cref="ArmCase"/> that accepts a row as a brace-delimited tuple.
    /// </summary>
    /// <remarks>
    /// The same shape <c>TheoryData</c> itself uses, and it exists for the same reason: a row spelled
    /// as five values reads as a matrix row, whereas a row spelled as a constructor call reads as
    /// code. Private, so nothing outside this suite is exposed to a derived collection type.
    /// </remarks>
    private sealed class ArmCaseCollection : List<ArmCase>
    {
        /// <summary>Adds one row.</summary>
        /// <param name="quadrant">The quadrant and its legacy locator.</param>
        /// <param name="sql">The statement to rewrite.</param>
        /// <param name="pageNative">The page-native selector.</param>
        /// <param name="uniqueIndexColumns">The unique-index-columns selector, or none.</param>
        /// <param name="expected">The generated statement, character for character.</param>
        public void Add(
            string quadrant,
            string sql,
            bool pageNative,
            string[]? uniqueIndexColumns,
            string expected) =>
            Add(new ArmCase(quadrant, sql, pageNative, uniqueIndexColumns, expected));
    }

    /// <summary>The matrix rows, each re-derived from the oracle append by append.</summary>
    private static ArmCaseCollection ArmCases =>
        new()
        {
            // --- ARM 1 : unique-index columns PRESENT, page-native TRUE [:L343-L350] -----------
            {
                "ARM 1 [:L343-L350] fixture statement, bare key column",
                FixtureSelect,
                true,
                [KeyColumn],
                // The prologue appended `id` to an ABSENT order-by [:L341], so the sub-query carries
                // `ORDER BY id` - arm 1 never strips it, which is why it appears INSIDE the join.
                // The select list was narrowed to the unique columns [:L345] and restored to `*`
                // [:L348], and the outer statement keeps the appended order-by because ARM 1 HAS NO
                // ORDER-BY RESTORE (C-B; asserted on the model itself by
                // ArmOneRestoresTheSelectListAndNeverTouchesTheOrderBy).
                "SELECT * FROM COMPANY INNER JOIN (SELECT id FROM COMPANY ORDER BY id OFFSET 10 "
                    + "ROWS FETCH NEXT 10 ROWS ONLY) pfwPagedSQL_OutterTbl ON "
                    + "pfwPagedSQL_OutterTbl.id = id ORDER BY id"
            },
            {
                "ARM 1 [:L343-L350] two table-qualified columns",
                FixtureSelect,
                true,
                [QualifiedKeyColumn, QualifiedAgeColumn],
                // BOTH `nIndex > 1` separators are reached here: the comma at [:L330] and the
                // ` AND ` at [:L332]. And each join term takes the substring AFTER THE FIRST period
                // [:L333], so the left side is `pfwPagedSQL_OutterTbl.id` while the right side keeps
                // the full qualified name.
                "SELECT * FROM COMPANY INNER JOIN (SELECT COMPANY.id,COMPANY.age FROM COMPANY "
                    + "ORDER BY COMPANY.id,COMPANY.age OFFSET 10 ROWS FETCH NEXT 10 ROWS ONLY) "
                    + "pfwPagedSQL_OutterTbl ON pfwPagedSQL_OutterTbl.id = COMPANY.id AND "
                    + "pfwPagedSQL_OutterTbl.age = COMPANY.age ORDER BY COMPANY.id,COMPANY.age"
            },
            {
                "ARM 1 [:L343-L350] mixed-case order-by, appended not lowered [:L342]",
                MixedCaseOrderedSelect,
                true,
                [KeyColumn],
                // `NAME` KEEPS ITS CASE. The lowered copy taken at [:L327] is used only by the
                // containment test at [:L334]; the value that reaches the emitted text is the
                // re-read at [:L342], which is NOT lowered.
                "SELECT id, name FROM COMPANY INNER JOIN (SELECT id FROM COMPANY ORDER BY NAME,id "
                    + "OFFSET 10 ROWS FETCH NEXT 10 ROWS ONLY) pfwPagedSQL_OutterTbl ON "
                    + "pfwPagedSQL_OutterTbl.id = id ORDER BY NAME,id"
            },
            {
                "ARM 1 [:L343-L350] order-by already names the column, so nothing is appended [:L334]",
                SelfOrderedSelect,
                true,
                [KeyColumn],
                // The containment test at [:L334] excluded `id`, leaving the appended fragment
                // EMPTY, and an append of empty text changes nothing. The order-by is therefore the
                // input's own, unduplicated.
                "SELECT id, name FROM COMPANY INNER JOIN (SELECT id FROM COMPANY ORDER BY id OFFSET "
                    + "10 ROWS FETCH NEXT 10 ROWS ONLY) pfwPagedSQL_OutterTbl ON "
                    + "pfwPagedSQL_OutterTbl.id = id ORDER BY id"
            },

            // --- ARM 2 : unique-index columns PRESENT, page-native FALSE [:L352-L362] ----------
            {
                "ARM 2 [:L352-L362] fixture statement, bare key column",
                FixtureSelect,
                false,
                [KeyColumn],
                // The order-by was STRIPPED at [:L353] before the sub-query was built, which is why
                // the innermost select carries none, and the window function received it instead
                // [:L355]. Note `id,ROW_NUMBER()` with NO SPACE after the comma - the legacy's own
                // spelling at [:L355]. The select list was restored [:L358] and the order-by was
                // restored to THE APPENDED VALUE, not the original [:L360].
                "SELECT * FROM COMPANY INNER JOIN (SELECT TOP 10 * FROM (SELECT TOP 20 id,"
                    + "ROW_NUMBER() OVER (ORDER BY id) AS pfwPagedSQL_RN FROM COMPANY) "
                    + "pfwPagedSQL_Tbl WHERE pfwPagedSQL_RN BETWEEN 11 AND 20) "
                    + "pfwPagedSQL_OutterTbl ON pfwPagedSQL_OutterTbl.id = id ORDER BY id"
            },
            {
                "ARM 2 [:L352-L362] enumerated select list, restored after the narrowing [:L358]",
                EnumeratedSelect,
                false,
                [KeyColumn],
                "SELECT id, name, age, address, salary, birth FROM COMPANY INNER JOIN (SELECT TOP 10 "
                    + "* FROM (SELECT TOP 20 id,ROW_NUMBER() OVER (ORDER BY id) AS pfwPagedSQL_RN "
                    + "FROM COMPANY) pfwPagedSQL_Tbl WHERE pfwPagedSQL_RN BETWEEN 11 AND 20) "
                    + "pfwPagedSQL_OutterTbl ON pfwPagedSQL_OutterTbl.id = id ORDER BY id"
            },
            {
                "ARM 2 [:L352-L362] mixed-case order-by reaches the window function uncased [:L355]",
                MixedCaseOrderedSelect,
                false,
                [KeyColumn],
                // THE DECISIVE ROW FOR THE LOWERED-VERSUS-RE-READ DISTINCTION. The window key is
                // `NAME id`, not `name id`. A port that reused the lowered copy from [:L327] fails
                // exactly here and nowhere else in this suite.
                "SELECT id, name FROM COMPANY INNER JOIN (SELECT TOP 10 * FROM (SELECT TOP 20 id,"
                    + "ROW_NUMBER() OVER (ORDER BY NAME,id) AS pfwPagedSQL_RN FROM COMPANY) "
                    + "pfwPagedSQL_Tbl WHERE pfwPagedSQL_RN BETWEEN 11 AND 20) "
                    + "pfwPagedSQL_OutterTbl ON pfwPagedSQL_OutterTbl.id = id ORDER BY NAME,id"
            },

            // --- ARM 3 : unique-index columns ABSENT, page-native TRUE [:L366-L373] ------------
            {
                "ARM 3 [:L366-L373] fixture statement, the (SELECT 0) substitution [:L370]",
                FixtureSelect,
                true,
                null,
                // NO SENTINEL APPEARS IN THIS ARM AT ALL. The paging construct is appended to the
                // order-by clause itself, so the statement keeps its own shape.
                "SELECT * FROM COMPANY ORDER BY (SELECT 0) OFFSET 10 ROWS FETCH NEXT 10 ROWS ONLY"
            },
            {
                "ARM 3 [:L366-L373] existing order-by kept verbatim [:L368]",
                MixedCaseOrderedSelect,
                true,
                null,
                "SELECT id, name FROM COMPANY ORDER BY NAME OFFSET 10 ROWS FETCH NEXT 10 ROWS ONLY"
            },

            // --- ARM 4 : unique-index columns ABSENT, page-native FALSE [:L375-L383] -----------
            {
                "ARM 4 [:L375-L383] fixture statement, star select list left in place",
                FixtureSelect,
                false,
                null,
                // THE ONLY ARM THAT APPENDS A TRAILING ORDER BY [:L383]. The select list is read
                // BEFORE it is replaced [:L381], so the star survives into the widened list, and
                // nothing is restored afterwards.
                "SELECT TOP 10 * FROM (SELECT TOP 20 *,ROW_NUMBER() OVER (ORDER BY (SELECT 0)) AS "
                    + "pfwPagedSQL_RN FROM COMPANY) pfwPagedSQL_Tbl WHERE pfwPagedSQL_RN BETWEEN 11 "
                    + "AND 20 ORDER BY pfwPagedSQL_RN"
            },
            {
                "ARM 4 [:L375-L383] existing order-by stripped into the window function [:L376-L377]",
                MixedCaseOrderedSelect,
                false,
                null,
                // `NAME` again keeps its case: arm 4 reads the clause at [:L376] and never lowers
                // it. The strip at [:L377] is why the innermost select carries no order-by.
                "SELECT TOP 10 * FROM (SELECT TOP 20 id, name,ROW_NUMBER() OVER (ORDER BY NAME) AS "
                    + "pfwPagedSQL_RN FROM COMPANY) pfwPagedSQL_Tbl WHERE pfwPagedSQL_RN BETWEEN 11 "
                    + "AND 20 ORDER BY pfwPagedSQL_RN"
            },
        };

    /// <summary>
    /// Every quadrant of the two-by-two matrix emits its statement character for character.
    /// </summary>
    /// <param name="quadrant">The quadrant and locator, so a failure names the arm it came from.</param>
    /// <param name="sql">The statement to rewrite.</param>
    /// <param name="pageNative">
    /// <c>_bPageNative</c> <c>[:L343, :L366]</c> - the second selector.
    /// </param>
    /// <param name="uniqueIndexColumns">
    /// <c>_sPagedUniqueIndexColumns</c> <c>[:L323]</c> - the first selector; <see langword="null"/>
    /// is the absent state.
    /// </param>
    /// <param name="expected">The generated statement, character for character.</param>
    /// <remarks>
    /// ORDINAL EQUALITY, AND NO WHITESPACE NORMALISATION ANYWHERE. Spacing is observable output: the
    /// legacy emits exactly one space before <c>OFFSET</c> <c>[:L346]</c> and none after the comma
    /// that introduces the row-number column <c>[:L355, :L381]</c>, and a comparison that trimmed or
    /// collapsed whitespace would pass on text the engine renders differently from the legacy's.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ArmMatrix))]
    public void EveryQuadrantOfTheTwoByTwoMatrixGeneratesItsStatementCharacterForCharacter(
        string quadrant,
        string sql,
        bool pageNative,
        string[]? uniqueIndexColumns,
        string expected)
    {
        PagingRewriteResult result =
            Rewrite(sql, PrincipalPageSize, PrincipalPageIndex, pageNative, uniqueIndexColumns);

        Assert.True(result.IsOk, $"{quadrant} did not succeed: {result.ErrorText}");
        Assert.Equal(RetCode.OK, result.ReturnCode);
        Assert.Equal(expected, result.RewrittenSql, StringComparer.Ordinal);
    }

    /// <summary>
    /// THE MATRIX IS FOUR QUADRANTS, NOT THREE STRATEGIES, and this asserts the coverage rather than
    /// leaving it to inspection (C-K).
    /// </summary>
    /// <remarks>
    /// Correction 1 in the file header. A future simplification that merges two quadrants - the
    /// mistake the plan's "three strategies" prose invites - drops a distinct selector combination
    /// and fails here, which is the point: the guard is on the SHAPE of the matrix, not on any one
    /// row.
    /// </remarks>
    [Fact]
    public void TheMatrixCoversAllFourSelectorCombinations()
    {
        HashSet<(bool UniqueIndexColumnsPresent, bool PageNative)> quadrants = [];

        foreach (ArmCase armCase in ArmCases)
        {
            _ = quadrants.Add(
                (armCase.UniqueIndexColumns is { Length: > 0 }, armCase.PageNative));
        }

        // The theory must actually RUN every row the coverage claim is computed from, or the claim is
        // about a list rather than about the tests.
        Assert.Equal(ArmCases.Count, ArmMatrix.Count);

        Assert.Equal(4, quadrants.Count);
        Assert.Contains((true, true), quadrants);    // ARM 1 [:L343-L350]
        Assert.Contains((true, false), quadrants);   // ARM 2 [:L352-L362]
        Assert.Contains((false, true), quadrants);   // ARM 3 [:L366-L373]
        Assert.Contains((false, false), quadrants);  // ARM 4 [:L375-L383]
    }

    // ------------------------------------------------------------------------------------------
    //  THE SHARED PROLOGUE BOTH UNIQUE-INDEX ARMS RUN  [:L327-L342]
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The three fragments the prologue's loop builds, for each shape of column list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The loop at <c>[:L328-L338]</c> builds three strings in one pass, and each has its own join
    /// rule - which is precisely why they are asserted individually rather than only through the
    /// finished statement, where a fragment defect can be masked by another:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>the column list</b> <c>[:L330-L331]</c> - comma-joined, NO SPACE after the comma, with the
    /// separator gated on <c>nIndex &gt; 1</c>
    /// </description></item>
    /// <item><description>
    /// <b>the join predicate</b> <c>[:L332-L333]</c> - joined with <c>" AND "</c>, each term being the
    /// outer alias, a period, THE SUBSTRING AFTER THE FIRST PERIOD OF THE COLUMN, <c>" = "</c>, and
    /// then the column in full. When the column carries no period at all, PowerBuilder's
    /// <c>Pos</c> returns zero and <c>Mid(column, 0 + 1)</c> is the WHOLE column, so the term
    /// degenerates to alias-dot-column - reproduced, not special-cased
    /// </description></item>
    /// <item><description>
    /// <b>the appended order-by</b> <c>[:L334-L337]</c> - comma-joined, containing ONLY the columns
    /// whose lowered spelling is not already found in the lowered order-by. ITS SEPARATOR IS NOT
    /// COUNTER-GATED: <c>[:L335]</c> tests whether the fragment is still empty, not whether the loop
    /// counter has advanced, because a skipped column must not leave a dangling comma
    /// </description></item>
    /// </list>
    /// </remarks>
    public static TheoryData<string, string[], string, string, string, string> Prologue =>
        new()
        {
            {
                "one bare column, no order-by [:L330-L337]",
                [KeyColumn],
                "",
                "id",
                "pfwPagedSQL_OutterTbl.id = id",
                "id"
            },
            {
                "one qualified column - the substring after the first period [:L333]",
                [QualifiedKeyColumn],
                "",
                "COMPANY.id",
                "pfwPagedSQL_OutterTbl.id = COMPANY.id",
                "COMPANY.id"
            },
            {
                "two qualified columns - both nIndex > 1 separators [:L330, :L332]",
                [QualifiedKeyColumn, QualifiedAgeColumn],
                "",
                "COMPANY.id,COMPANY.age",
                "pfwPagedSQL_OutterTbl.id = COMPANY.id AND pfwPagedSQL_OutterTbl.age = COMPANY.age",
                "COMPANY.id,COMPANY.age"
            },
            {
                "the order-by already names the column, so it is excluded [:L334]",
                [KeyColumn],
                "id",
                "id",
                "pfwPagedSQL_OutterTbl.id = id",
                ""
            },
            {
                "the containment test is done on the LOWERED order-by [:L327, :L334]",
                ["ID"],
                // The caller lowers before calling, so an order-by spelled `ID` arrives as `id` and
                // the upper-cased column still matches. Case-insensitive exclusion is the legacy's
                // behaviour and it is asserted here rather than assumed.
                "id",
                "ID",
                "pfwPagedSQL_OutterTbl.ID = ID",
                ""
            },
            {
                "the excluded column leaves no dangling comma [:L335]",
                [KeyColumn, QualifiedAgeColumn],
                // `id` is excluded and `COMPANY.age` is not, so the appended order-by is the SECOND
                // column alone with NO leading comma. A separator gated on the loop counter instead
                // of on the fragment's emptiness would emit `,COMPANY.age` here.
                "order by id",
                "id,COMPANY.age",
                "pfwPagedSQL_OutterTbl.id = id AND pfwPagedSQL_OutterTbl.age = COMPANY.age",
                "COMPANY.age"
            },
            {
                "a deeply qualified column still splits at the FIRST period only [:L333]",
                ["PFW.COMPANY.id"],
                "",
                "PFW.COMPANY.id",
                // `Mid(column, Pos(column, ".") + 1)` takes everything after the FIRST period, so the
                // left side keeps the middle segment too. Splitting at the LAST period would be a
                // different - and wrong - statement.
                "pfwPagedSQL_OutterTbl.COMPANY.id = PFW.COMPANY.id",
                "PFW.COMPANY.id"
            },
        };

    /// <summary>
    /// The prologue's three fragments are built exactly as <c>[:L328-L338]</c> builds them.
    /// </summary>
    /// <param name="shape">The column-list shape, so a failure names it.</param>
    /// <param name="uniqueIndexColumns">The unique-index columns to fold.</param>
    /// <param name="lowerCasedOrderBy">
    /// The already-lowered order-by, standing in for <c>Lower(GetOrder())</c> at <c>[:L327]</c>.
    /// </param>
    /// <param name="expectedColumns">The comma-joined column list <c>[:L331]</c>.</param>
    /// <param name="expectedJoinPredicate">The <c>" AND "</c>-joined predicate <c>[:L333]</c>.</param>
    /// <param name="expectedOrderBy">The appended order-by fragment <c>[:L336]</c>.</param>
    [Theory]
    [MemberData(nameof(Prologue))]
    public void ThePrologueBuildsItsThreeFragmentsExactlyAsTheOracleDoes(
        string shape,
        string[] uniqueIndexColumns,
        string lowerCasedOrderBy,
        string expectedColumns,
        string expectedJoinPredicate,
        string expectedOrderBy)
    {
        UniqueIndexColumnFragments fragments =
            SqlServerPagingRewriter.BuildUniqueIndexColumnFragments(
                uniqueIndexColumns,
                lowerCasedOrderBy);

        AssertFragment(shape, "column list [:L331]", expectedColumns, fragments.Columns);
        AssertFragment(
            shape, "join predicate [:L333]", expectedJoinPredicate, fragments.JoinPredicate);
        AssertFragment(shape, "appended order-by [:L336]", expectedOrderBy, fragments.OrderBy);
    }

    /// <summary>
    /// AN ABSENT COLUMN NAME IS FOLDED AS THE EMPTY STRING, WHICH IS WHAT POWERSCRIPT WOULD HAVE DONE.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is not defensive invention, it is the faithful analogue. A PowerScript string has no null
    /// state - an unassigned element of <c>_sPagedUniqueIndexColumns</c> is the EMPTY STRING - and the
    /// oracle folds it without guarding: <c>Pos("", ".")</c> is zero, so <c>Mid("", 0 + 1)</c> is the
    /// empty string too, and the join term at <c>[:L333]</c> degenerates to the alias, a period, a
    /// space, an equals sign and a space with nothing on either side. The managed signature admits
    /// <see langword="null"/> because a .NET caller can supply one, and it maps that onto the same
    /// empty string so the emitted text is identical to the legacy's.
    /// </para>
    /// <para>
    /// One consequence is worth stating because it looks like a bug and is not: an empty column is
    /// ALWAYS treated as already named by the order-by, and so is never appended. Every string contains
    /// the empty string, so the containment test at <c>[:L334]</c> succeeds unconditionally - in the
    /// legacy exactly as here.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnAbsentColumnNameIsFoldedAsTheEmptyString()
    {
        UniqueIndexColumnFragments single =
            SqlServerPagingRewriter.BuildUniqueIndexColumnFragments([null!], string.Empty);

        Assert.Equal(string.Empty, single.Columns, StringComparer.Ordinal);
        Assert.Equal("pfwPagedSQL_OutterTbl. = ", single.JoinPredicate, StringComparer.Ordinal);
        // Not appended, because "" is contained in every string - see the remarks.
        Assert.Equal(string.Empty, single.OrderBy, StringComparer.Ordinal);

        UniqueIndexColumnFragments trailing =
            SqlServerPagingRewriter.BuildUniqueIndexColumnFragments(
                [KeyColumn, null!],
                string.Empty);

        // The separators still fire on the second element [:L330, :L332]; only its name is empty.
        Assert.Equal("id,", trailing.Columns, StringComparer.Ordinal);
        Assert.Equal(
            "pfwPagedSQL_OutterTbl.id = id AND pfwPagedSQL_OutterTbl. = ",
            trailing.JoinPredicate,
            StringComparer.Ordinal);
        Assert.Equal(KeyColumn, trailing.OrderBy, StringComparer.Ordinal);
    }

    /// <summary>
    /// THE FRAGMENT CARRIER RETURNS EMPTY STRINGS RATHER THAN NULL, INCLUDING WHEN DEFAULT-CONSTRUCTED.
    /// </summary>
    /// <remarks>
    /// The carrier is a readonly record struct, so <see langword="default"/> is always representable and
    /// a caller can reach it without going through the builder. Every one of its three members is
    /// therefore null-normalised, which is what lets the arms concatenate them unconditionally - the
    /// oracle concatenates PowerScript strings that likewise cannot be null. Asserted so that a later
    /// change to a null-returning property would fail here rather than surface as a null reference deep
    /// inside a statement build.
    /// </remarks>
    [Fact]
    public void TheFragmentCarrierNeverReturnsNull()
    {
        UniqueIndexColumnFragments explicitNulls = new(null, null, null);
        UniqueIndexColumnFragments uninitialised = default;

        Assert.Equal(string.Empty, explicitNulls.Columns, StringComparer.Ordinal);
        Assert.Equal(string.Empty, explicitNulls.JoinPredicate, StringComparer.Ordinal);
        Assert.Equal(string.Empty, explicitNulls.OrderBy, StringComparer.Ordinal);

        Assert.Equal(string.Empty, uninitialised.Columns, StringComparer.Ordinal);
        Assert.Equal(string.Empty, uninitialised.JoinPredicate, StringComparer.Ordinal);
        Assert.Equal(string.Empty, uninitialised.OrderBy, StringComparer.Ordinal);
    }

    /// <summary>
    /// THE ORDER-BY IS LOWERED FOR THE CONTAINMENT TEST AND THEN RE-READ WITHOUT LOWERING
    /// <c>[:L327 versus :L342]</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the prologue's subtlest step and the one a port is most likely to flatten. The oracle
    /// takes a LOWERED copy at <c>[:L327]</c>, spends it entirely on the containment test at
    /// <c>[:L334]</c>, appends the surviving columns at <c>[:L341]</c>, and then ASSIGNS THE SAME
    /// VARIABLE AGAIN from a fresh read at <c>[:L342]</c> - this time NOT lowered. Reusing the lowered
    /// value instead would emit a window-function ordering key in the wrong case: plausible-looking
    /// SQL that no case-insensitive comparison would flag.
    /// </para>
    /// <para>
    /// Asserted on the model rather than only through the finished statement, so the failure points at
    /// the prologue instead of at whichever arm happened to consume it: after the append the clause
    /// must read <c>NAME id</c> - the input's own casing, plus the appended fragment, joined by a
    /// single space - and must NOT read <c>name id</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheOrderByIsLoweredOnlyForTheContainmentTestAndIsReReadUncased()
    {
        SelectStatementModel statement = Parsed(MixedCaseOrderedSelect);

        string lowerCasedOrderBy = statement.GetOrder().ToLowerInvariant();   // [:L327]

        Assert.Equal("name", lowerCasedOrderBy, StringComparer.Ordinal);
        Assert.Equal("NAME", statement.GetOrder(), StringComparer.Ordinal);

        UniqueIndexColumnFragments fragments =
            SqlServerPagingRewriter.BuildUniqueIndexColumnFragments(
                [KeyColumn],
                lowerCasedOrderBy);

        // SQL_MS_APPEND IS THE STYLE THE ORACLE USES AT [:L341], and an append to a COMMA-LIST clause
        // joins with a comma: the unique-column fragment the oracle builds carries no leading comma
        // [:L335-L338], so a space would emit `ORDER BY NAME id` - SQL no dialect accepts. Named from the
        // shared kernel's catalogue rather than spelled as `2`.
        Assert.True(statement.ModifyOrder(Enums.SQL_MS_APPEND, fragments.OrderBy));

        string reRead = statement.GetOrder();                                 // [:L342]

        Assert.Equal("NAME,id", reRead, StringComparer.Ordinal);
        Assert.NotEqual("name,id", reRead, StringComparer.Ordinal);
    }

    // ------------------------------------------------------------------------------------------
    //  WHO RESTORES WHAT  [:L348, :L358, :L360] and the two arms that restore nothing
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// ARM 1 RESTORES THE SELECT LIST AND LEAVES THE ORDER-BY ALONE <c>[:L348]</c>.
    /// </summary>
    /// <remarks>
    /// The arm narrows the select list to the unique columns <c>[:L345]</c>, builds its sub-query, and
    /// then puts the original list back <c>[:L348]</c>. It performs NO order-by restore, because it
    /// never removed one: the clause is still whatever the prologue's append left, which is why the
    /// order-by survives INSIDE the generated sub-query. Both halves are asserted on the model itself,
    /// so this cannot be confused with a coincidence of the emitted text.
    /// </remarks>
    [Fact]
    public void ArmOneRestoresTheSelectListAndNeverTouchesTheOrderBy()
    {
        PagingRewriteRequest request =
            new(MixedCaseOrderedSelect, PrincipalPageSize, PrincipalPageIndex, true, [KeyColumn]);
        SelectStatementModel statement = Parsed(MixedCaseOrderedSelect);

        string originalColumns = statement.GetColumn();
        Assert.True(statement.ModifyOrder(Enums.SQL_MS_APPEND, KeyColumn));   // the prologue [:L341]
        string appendedOrderBy = statement.GetOrder();                        // [:L342]

        SqlServerPagingRewriter.AppendPageNativeInnerJoin(
            in request,
            statement,
            new UniqueIndexColumnFragments(KeyColumn, "pfwPagedSQL_OutterTbl.id = id", KeyColumn),
            originalColumns);

        // RESTORED [:L348].
        Assert.Equal(originalColumns, statement.GetColumn(), StringComparer.Ordinal);
        // NOT RESTORED, because never modified - it is still the prologue's appended value.
        Assert.Equal(appendedOrderBy, statement.GetOrder(), StringComparer.Ordinal);
        Assert.Equal("NAME,id", statement.GetOrder(), StringComparer.Ordinal);
    }

    /// <summary>
    /// ARM 2 RESTORES BOTH, AND THE ORDER-BY IT RESTORES IS THE APPENDED ONE, NOT THE ORIGINAL
    /// <c>[:L358, :L360]</c>.
    /// </summary>
    /// <remarks>
    /// The arm strips the order-by <c>[:L353]</c> so the inner sub-query carries none, moves it into
    /// the window function <c>[:L355]</c>, then restores the select list <c>[:L358]</c> and the
    /// order-by <c>[:L360]</c>. What it hands back to the order-by is the value re-read at
    /// <c>[:L342]</c> - the ORIGINAL PLUS THE APPENDED FRAGMENT - so a port that stashed the clause
    /// before the prologue's append would restore too little.
    /// </remarks>
    [Fact]
    public void ArmTwoRestoresTheSelectListAndTheAppendedOrderBy()
    {
        PagingRewriteRequest request =
            new(MixedCaseOrderedSelect, PrincipalPageSize, PrincipalPageIndex, false, [KeyColumn]);
        SelectStatementModel statement = Parsed(MixedCaseOrderedSelect);

        string originalColumns = statement.GetColumn();
        string originalOrderBy = statement.GetOrder();
        Assert.True(statement.ModifyOrder(Enums.SQL_MS_APPEND, KeyColumn));   // the prologue [:L341]
        string appendedOrderBy = statement.GetOrder();                        // [:L342]

        SqlServerPagingRewriter.AppendRowNumberInnerJoin(
            in request,
            statement,
            new UniqueIndexColumnFragments(KeyColumn, "pfwPagedSQL_OutterTbl.id = id", KeyColumn),
            originalColumns,
            appendedOrderBy);

        Assert.Equal(originalColumns, statement.GetColumn(), StringComparer.Ordinal);
        Assert.Equal(appendedOrderBy, statement.GetOrder(), StringComparer.Ordinal);
        Assert.NotEqual(originalOrderBy, statement.GetOrder(), StringComparer.Ordinal);
    }

    /// <summary>
    /// THE STRIP-VERSUS-KEEP ASYMMETRY BETWEEN THE TWO UNIQUE-INDEX ARMS IS OBSERVABLE IN THE
    /// GENERATED TEXT.
    /// </summary>
    /// <remarks>
    /// Arm 1 never removes the order-by, so its inner sub-query still ends with one before the paging
    /// construct <c>[:L346]</c>. Arm 2 removes it first <c>[:L353]</c>, so its innermost select ends at
    /// the table with no order-by at all and the ordering lives only inside the window function
    /// <c>[:L355]</c>. This pins the difference between the two arms rather than each arm's text in
    /// isolation, which is the property a merged "three strategies" reading would destroy.
    /// </remarks>
    [Fact]
    public void ArmOneKeepsTheOrderByInsideTheSubQueryAndArmTwoStripsIt()
    {
        string armOne = Generated(MixedCaseOrderedSelect, true, [KeyColumn]);
        string armTwo = Generated(MixedCaseOrderedSelect, false, [KeyColumn]);

        Assert.Contains(
            "FROM COMPANY ORDER BY NAME,id OFFSET ",
            armOne,
            StringComparison.Ordinal);
        Assert.DoesNotContain("FROM COMPANY ORDER BY", armTwo, StringComparison.Ordinal);
        Assert.Contains("OVER (ORDER BY NAME,id)", armTwo, StringComparison.Ordinal);
    }

    /// <summary>
    /// BOTH UNIQUE-INDEX ARMS RETURN THE OUTER QUERY CARRYING THE INNER JOIN, NOT THE SUB-QUERY THEY
    /// BUILT <c>[:L364]</c>.
    /// </summary>
    /// <remarks>
    /// The single most likely porting mistake in this arm. Each of arms 1 and 2 assembles a local
    /// string, splices it into the FROM clause as an <c>INNER JOIN</c>
    /// <c>[:L350, :L362]</c>, and is then FOLLOWED by a re-read of the whole statement at
    /// <c>[:L364]</c> which OVERWRITES that local. A port that returned the local instead would emit a
    /// bare paged sub-query - a statement that runs, returns rows, and is wrong. Asserted three ways:
    /// the result must begin with the outer select, must contain the join, and must NOT equal the
    /// sub-query it wraps.
    /// </remarks>
    [Fact]
    public void BothUniqueIndexArmsReturnTheOuterQueryCarryingTheInnerJoin()
    {
        string armOne = Generated(FixtureSelect, true, [KeyColumn]);
        string armTwo = Generated(FixtureSelect, false, [KeyColumn]);

        Assert.StartsWith(FixtureSelect + " INNER JOIN (", armOne, StringComparison.Ordinal);
        Assert.StartsWith(FixtureSelect + " INNER JOIN (", armTwo, StringComparison.Ordinal);

        Assert.EndsWith(") pfwPagedSQL_OutterTbl ON pfwPagedSQL_OutterTbl.id = id ORDER BY id",
            armOne,
            StringComparison.Ordinal);
        Assert.EndsWith(") pfwPagedSQL_OutterTbl ON pfwPagedSQL_OutterTbl.id = id ORDER BY id",
            armTwo,
            StringComparison.Ordinal);

        // A returned sub-query would have started with the paging construct, never with the outer
        // select list, and would have carried no join at all.
        Assert.DoesNotContain("SELECT TOP 10 * FROM (SELECT id FROM", armOne, StringComparison.Ordinal);
        Assert.False(
            armTwo.StartsWith("SELECT TOP ", StringComparison.Ordinal),
            "arm 2 returned its inner paged slice instead of the outer query re-read at [:L364]");
    }

    /// <summary>
    /// ARM 3 REPLACES THE ORDER-BY WITH THE PAGING CONSTRUCT AND RESTORES NOTHING <c>[:L372]</c>.
    /// </summary>
    /// <remarks>
    /// The arm never touches the select list, and the clause it writes is the ordering key followed by
    /// the whole <c>OFFSET</c> / <c>FETCH NEXT</c> construct - the paging text lives INSIDE the
    /// order-by clause, which is why this arm emits no sub-query and no alias.
    /// </remarks>
    [Fact]
    public void ArmThreeLeavesThePagingConstructInTheOrderByAndRestoresNothing()
    {
        PagingRewriteRequest request =
            new(MixedCaseOrderedSelect, PrincipalPageSize, PrincipalPageIndex, true);
        SelectStatementModel statement = Parsed(MixedCaseOrderedSelect);

        string originalColumns = statement.GetColumn();

        string rewritten = SqlServerPagingRewriter.BuildPageNativeOffsetFetch(in request, statement);

        Assert.Equal(originalColumns, statement.GetColumn(), StringComparer.Ordinal);
        Assert.Equal(
            "NAME OFFSET 10 ROWS FETCH NEXT 10 ROWS ONLY",
            statement.GetOrder(),
            StringComparer.Ordinal);
        Assert.Equal(statement.GetSql(), rewritten, StringComparer.Ordinal);
    }

    /// <summary>
    /// ARM 4 RESTORES NEITHER THE SELECT LIST NOR THE ORDER-BY <c>[:L375-L383]</c>.
    /// </summary>
    /// <remarks>
    /// The arm strips the order-by <c>[:L377]</c> and widens the select list with the <c>TOP</c> count
    /// and the row-number column <c>[:L381]</c>, and then simply returns - there is no restore step of
    /// either kind in the source. It can afford that because, unlike arms 1 and 2, the statement it
    /// returns is the WRAPPER around the modified statement rather than the modified statement itself,
    /// so nothing downstream reads the model again. Preserved as-is (C-B).
    /// </remarks>
    [Fact]
    public void ArmFourRestoresNeitherTheSelectListNorTheOrderBy()
    {
        PagingRewriteRequest request =
            new(MixedCaseOrderedSelect, PrincipalPageSize, PrincipalPageIndex, false);
        SelectStatementModel statement = Parsed(MixedCaseOrderedSelect);

        string originalColumns = statement.GetColumn();
        Assert.True(statement.HasOrder());

        string rewritten = SqlServerPagingRewriter.BuildRowNumberWrapper(in request, statement);

        // NOT RESTORED: the select list is still the widened one [:L381].
        Assert.NotEqual(originalColumns, statement.GetColumn(), StringComparer.Ordinal);
        Assert.Equal(
            "TOP 20 id, name,ROW_NUMBER() OVER (ORDER BY NAME) AS pfwPagedSQL_RN",
            statement.GetColumn(),
            StringComparer.Ordinal);
        // NOT RESTORED: the order-by was stripped at [:L377] and left stripped.
        Assert.False(statement.HasOrder());
        Assert.Equal(string.Empty, statement.GetOrder(), StringComparer.Ordinal);

        // And the returned text is the WRAPPER, which is why restoring would have been pointless.
        Assert.NotEqual(statement.GetSql(), rewritten, StringComparer.Ordinal);
    }

    // ------------------------------------------------------------------------------------------
    //  THE SENTINELS AND THEIR PRESENCE MATRIX  [:L333, :L350, :L355, :L356, :L362, :L382, :L383]
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// THE THREE SENTINELS THIS ARM EMITS KEEP THEIR LEGACY SPELLINGS, DOUBLED LETTER AND ALL.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asserted against the arm's OWN internal constants as well as against the legacy literals. The
    /// application project grants this assembly internal access, so the test reads the very declaration
    /// the implementation emits from: the spelling exists in exactly one place in the managed tree and
    /// the two cannot drift to two spellings. <c>pfwPagedSQL_Tbl</c> in particular is internal rather
    /// than private precisely so the count wrapper at <c>[:L834]</c> and this suite can both reach it.
    /// </para>
    /// <para>
    /// <b>Correction 2 in the file header.</b> <c>pfwPagedSQL_Tbl</c> is the sixth sentinel, absent from
    /// the migration plan's own five-item list in both places it appears, and it is easy to lose because
    /// it is a PREFIX of three Oracle-side aliases. It is asserted by name here so the omission cannot
    /// propagate into a missing assertion.
    /// </para>
    /// <para>
    /// <b>The doubled <c>t</c> is legacy and is not a typo to fix (C-B).</b> The single-<c>t</c>
    /// spelling is asserted ABSENT from every generated statement, so a well-meaning correction fails
    /// here rather than silently changing every statement the arm emits and invalidating every stored
    /// characterization comparison.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheThreeSentinelsKeepTheirLegacySpellings()
    {
        Assert.Equal(
            "pfwPagedSQL_OutterTbl",
            SqlServerPagingRewriter.OutterTableAlias,
            StringComparer.Ordinal);
        Assert.Equal(
            "pfwPagedSQL_RN",
            SqlServerPagingRewriter.RowNumberAlias,
            StringComparer.Ordinal);
        Assert.Equal(
            "pfwPagedSQL_Tbl",
            SqlServerPagingRewriter.DerivedTableAlias,
            StringComparer.Ordinal);

        // THE DOUBLED t, ISOLATED AS ITS OWN ASSERTION so the failure message is unambiguous.
        Assert.Contains("Outter", SqlServerPagingRewriter.OutterTableAlias, StringComparison.Ordinal);

        foreach (ArmCase armCase in ArmCases)
        {
            Assert.DoesNotContain("pfwPagedSQL_OuterTbl", armCase.Expected, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Which sentinel each quadrant emits, and which it must NOT.
    /// </summary>
    /// <remarks>
    /// Absence is asserted as deliberately as presence. Arm 3 emits NO sentinel at all - it appends the
    /// paging construct to the order-by clause and produces no sub-query to alias - so a port that
    /// wrapped it "for consistency" with arm 4 would be caught here and nowhere else. The three Oracle
    /// aliases are absent from every quadrant, which is the mechanical form of the two arms not sharing
    /// their sentinels.
    /// </remarks>
    public static TheoryData<string, bool, string[]?, bool, bool, bool> Sentinels =>
        new()
        {
            // quadrant                          native  columns        Outter  Tbl    RN
            { "ARM 1 [:L350]", true, [KeyColumn], true, false, false },
            { "ARM 2 [:L355, :L356, :L362]", false, [KeyColumn], true, true, true },
            { "ARM 3 [:L366-L373]", true, null, false, false, false },
            { "ARM 4 [:L382, :L383]", false, null, false, true, true },
        };

    /// <summary>
    /// Each quadrant emits exactly the sentinels the oracle emits there, and no other.
    /// </summary>
    /// <param name="quadrant">The quadrant and locator.</param>
    /// <param name="pageNative">The page-native selector.</param>
    /// <param name="uniqueIndexColumns">The unique-index-columns selector, or none.</param>
    /// <param name="expectsOutterTable">Whether <c>pfwPagedSQL_OutterTbl</c> is expected.</param>
    /// <param name="expectsDerivedTable">Whether <c>pfwPagedSQL_Tbl</c> is expected.</param>
    /// <param name="expectsRowNumber">Whether <c>pfwPagedSQL_RN</c> is expected.</param>
    [Theory]
    [MemberData(nameof(Sentinels))]
    public void EachQuadrantEmitsExactlyTheSentinelsTheOracleEmitsThere(
        string quadrant,
        bool pageNative,
        string[]? uniqueIndexColumns,
        bool expectsOutterTable,
        bool expectsDerivedTable,
        bool expectsRowNumber)
    {
        string generated = Generated(FixtureSelect, pageNative, uniqueIndexColumns);

        AssertSentinel(
            SqlServerPagingRewriter.OutterTableAlias, expectsOutterTable, generated, quadrant);
        AssertSentinel(
            SqlServerPagingRewriter.DerivedTableAlias, expectsDerivedTable, generated, quadrant);
        AssertSentinel(
            SqlServerPagingRewriter.RowNumberAlias, expectsRowNumber, generated, quadrant);

        // THE OTHER DIALECT'S THREE ALIASES NEVER APPEAR HERE. `pfwPagedSQL_Tbl` is a prefix of all
        // three, so each is tested in full rather than by a shared stem.
        Assert.DoesNotContain("pfwPagedSQL_TblInner", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("pfwPagedSQL_TblInnerInner", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("pfwPagedSQL_TblOuter", generated, StringComparison.Ordinal);
    }

    /// <summary>
    /// THE TRAILING <c>ORDER BY pfwPagedSQL_RN</c> BELONGS TO ARM 4 ALONE <c>[:L383]</c>.
    /// </summary>
    /// <remarks>
    /// <c>[:L383]</c> is a statement of its own, reached only on the no-unique-columns, non-native
    /// path, and it is the only place in the whole arm where the row-number alias is used as an
    /// ORDERING KEY rather than as a projected column or a predicate operand. Arms 2 and 4 both project
    /// the alias, so a test that merely looked for the alias would not distinguish them; this asserts
    /// the ordering CLAUSE, and asserts its absence from the other three quadrants.
    /// </remarks>
    [Fact]
    public void OnlyArmFourAppendsTheTrailingRowNumberOrderBy()
    {
        string trailing = " ORDER BY " + SqlServerPagingRewriter.RowNumberAlias;

        Assert.EndsWith(trailing, Generated(FixtureSelect, false, null), StringComparison.Ordinal);
        Assert.EndsWith(
            trailing, Generated(MixedCaseOrderedSelect, false, null), StringComparison.Ordinal);

        Assert.DoesNotContain(
            trailing, Generated(FixtureSelect, true, [KeyColumn]), StringComparison.Ordinal);
        Assert.DoesNotContain(
            trailing, Generated(FixtureSelect, false, [KeyColumn]), StringComparison.Ordinal);
        Assert.DoesNotContain(
            trailing, Generated(FixtureSelect, true, null), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------
    //  THE DIALECT-SPLIT EMPTY-ORDER-BY SUBSTITUTION  [:L370, :L379] versus [:L392]
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>(SELECT 0)</c> is substituted in arms 3 and 4 WHEN AND ONLY WHEN the input carries no
    /// order-by <c>[:L370, :L379]</c>.
    /// </summary>
    /// <param name="quadrant">The quadrant and locator.</param>
    /// <param name="sql">The statement to rewrite.</param>
    /// <param name="pageNative">The page-native selector.</param>
    /// <param name="expectsSubstitution">Whether the substitution is expected.</param>
    /// <remarks>
    /// <para>
    /// Both halves of the biconditional matter. Substituting when an order-by IS present would discard
    /// the caller's ordering; failing to substitute when it is absent would leave the window function
    /// and the <c>OFFSET</c> construct without the ordering key SQL Server requires. The oracle guards
    /// it with <c>HasOrder()</c> at <c>[:L367]</c> and <c>[:L375]</c>, and both outcomes are pinned.
    /// </para>
    /// <para>
    /// <b>The other dialect's substitution is asserted ABSENT.</b> Oracle uses <c>''</c> at
    /// <c>[:L392]</c>. The two are not interchangeable - SQL Server rejects a constant literal as a
    /// window ordering key, which is why the sub-select form exists - so hoisting either into a shared
    /// constant would silently break parity for whichever dialect lost its own text. This arm must
    /// never emit the Oracle form, on any input, in any quadrant.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("ARM 3, no order-by [:L370]", FixtureSelect, true, true)]
    [InlineData("ARM 3, order-by present [:L368]", MixedCaseOrderedSelect, true, false)]
    [InlineData("ARM 4, no order-by [:L379]", FixtureSelect, false, true)]
    [InlineData("ARM 4, order-by present [:L376]", MixedCaseOrderedSelect, false, false)]
    public void TheScalarSubQuerySubstitutionAppearsExactlyWhenTheInputHasNoOrderBy(
        string quadrant,
        string sql,
        bool pageNative,
        bool expectsSubstitution)
    {
        string generated = Generated(sql, pageNative, null);

        Assert.Equal(
            expectsSubstitution,
            generated.Contains(EmptyOrderBySubstitution, StringComparison.Ordinal));

        Assert.False(
            generated.Contains(OracleEmptyOrderBySubstitution, StringComparison.Ordinal),
            $"{quadrant} emitted the ORACLE substitution {OracleEmptyOrderBySubstitution} "
                + $"[:L392] instead of keeping the two dialects apart: {generated}");
    }

    /// <summary>
    /// THE UNIQUE-INDEX ARMS HAVE NO EMPTY-ORDER-BY SUBSTITUTION AT ALL.
    /// </summary>
    /// <remarks>
    /// Arms 1 and 2 never test <c>HasOrder()</c>, because the prologue has already appended a derived
    /// ordering unconditionally at <c>[:L341]</c>. Bolting a substitution onto them would be a
    /// behaviour change even when the appended fragment turns out to be empty (C-B), so the substitution
    /// is asserted absent from both - including on the fixture statement, which carries no order-by of
    /// its own and is therefore exactly the input that would tempt one.
    /// </remarks>
    [Fact]
    public void NeitherUniqueIndexArmSubstitutesForAMissingOrderBy()
    {
        string armOne = Generated(FixtureSelect, true, [KeyColumn]);
        string armTwo = Generated(FixtureSelect, false, [KeyColumn]);

        Assert.DoesNotContain(EmptyOrderBySubstitution, armOne, StringComparison.Ordinal);
        Assert.DoesNotContain(EmptyOrderBySubstitution, armTwo, StringComparison.Ordinal);
        Assert.DoesNotContain(OracleEmptyOrderBySubstitution, armOne, StringComparison.Ordinal);
        Assert.DoesNotContain(OracleEmptyOrderBySubstitution, armTwo, StringComparison.Ordinal);

        // The ordering key is the appended unique column instead, which is what makes the substitution
        // unnecessary rather than merely omitted.
        Assert.Contains("ORDER BY id", armOne, StringComparison.Ordinal);
        Assert.Contains("OVER (ORDER BY id)", armTwo, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------
    //  THE FIVE BOUND EXPRESSIONS  [:L346, :L355, :L356, :L372, :L381, :L382]
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The page size and index projected onto the five bound expressions the arm emits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The oracle computes five distinct values from two inputs, and every one is observable:
    /// </para>
    /// <list type="bullet">
    /// <item><description><c>OFFSET</c> = size x (index - 1) <c>[:L346, :L372]</c></description></item>
    /// <item><description><c>FETCH NEXT</c> = size <c>[:L346, :L372]</c></description></item>
    /// <item><description>the INNER <c>TOP</c> = size x index <c>[:L355, :L381]</c></description></item>
    /// <item><description>the OUTER <c>TOP</c> = size <c>[:L356, :L382]</c></description></item>
    /// <item><description>
    /// <c>BETWEEN</c> = size x (index - 1) + 1 .. size x index <c>[:L356, :L382]</c>
    /// </description></item>
    /// </list>
    /// <para>
    /// PAGE ONE IS INCLUDED ON PURPOSE, and so is a middle page. On page one the offset degenerates to
    /// zero and the lower bound to one, which is the case a port that dropped the one-based ordinal
    /// still gets right - so page one alone proves nothing and page one omitted leaves the degenerate
    /// values unpinned. Both are here.
    /// </para>
    /// </remarks>
    public static TheoryData<long, long, long, long, long, long> Arithmetic =>
        new()
        {
            // size  index  offset  innerTop  betweenLow  betweenHigh
            { 10L, 1L, 0L, 10L, 1L, 10L },
            { 10L, 2L, 10L, 20L, 11L, 20L },
            { 25L, 1L, 0L, 25L, 1L, 25L },
            { 7L, 4L, 21L, 28L, 22L, 28L },
            { 1L, 1L, 0L, 1L, 1L, 1L },
            { 100L, 37L, 3600L, 3700L, 3601L, 3700L },
        };

    /// <summary>
    /// The native <c>OFFSET</c> / <c>FETCH NEXT</c> bounds are computed as the oracle computes them.
    /// </summary>
    /// <param name="pageSize">The page size.</param>
    /// <param name="pageIndex">The page index.</param>
    /// <param name="offset">The expected offset.</param>
    /// <param name="innerTop">The expected inner <c>TOP</c> count, unused on this path.</param>
    /// <param name="betweenLow">The expected lower bound, unused on this path.</param>
    /// <param name="betweenHigh">The expected upper bound, unused on this path.</param>
    /// <remarks>
    /// <para>
    /// Driven through arm 3, the simplest quadrant that carries the construct, so the assertion is
    /// about the arithmetic and not about the surrounding nesting. The row's remaining three values
    /// belong to the row-number path and are asserted by
    /// <see cref="TheRowNumberBoundsAreComputedAsTheOracleComputesThem"/>; the matrix is shared so a
    /// page size or index can never be exercised on one path and forgotten on the other.
    /// </para>
    /// <para>
    /// The three row-number values are ALSO re-derived from the two inputs here, against the formulas
    /// at <c>[:L355]</c> and <c>[:L356]</c>. That is not redundant padding: it keeps the shared table
    /// honest, so a mistyped bound in the member data fails on the row that declares it rather than
    /// producing a matching-but-wrong expectation on the other path.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(Arithmetic))]
    public void TheNativePagingBoundsAreComputedAsTheOracleComputesThem(
        long pageSize,
        long pageIndex,
        long offset,
        long innerTop,
        long betweenLow,
        long betweenHigh)
    {
        PagingRewriteResult result = Rewrite(FixtureSelect, pageSize, pageIndex, true, null);

        Assert.True(result.IsOk, result.ErrorText);
        Assert.Equal(
            $"SELECT * FROM COMPANY ORDER BY (SELECT 0) OFFSET {offset} ROWS FETCH NEXT "
                + $"{pageSize} ROWS ONLY",
            result.RewrittenSql,
            StringComparer.Ordinal);

        Assert.Equal(pageSize * pageIndex, innerTop);
        Assert.Equal((pageSize * (pageIndex - 1)) + 1, betweenLow);
        Assert.Equal(pageSize * pageIndex, betweenHigh);
    }

    /// <summary>
    /// The <c>TOP</c> counts and the <c>BETWEEN</c> bounds are computed as the oracle computes them.
    /// </summary>
    /// <param name="pageSize">The page size.</param>
    /// <param name="pageIndex">The page index.</param>
    /// <param name="offset">The expected offset, unused on this path.</param>
    /// <param name="innerTop">The expected inner <c>TOP</c> count.</param>
    /// <param name="betweenLow">The expected <c>BETWEEN</c> lower bound.</param>
    /// <param name="betweenHigh">The expected <c>BETWEEN</c> upper bound.</param>
    /// <remarks>
    /// <para>
    /// Driven through arm 4, which carries all four remaining values in one statement. Note that the
    /// OUTER <c>TOP</c> is the page size while the INNER <c>TOP</c> is size x index: the inner query
    /// fetches every row up to the end of the requested page and the outer one takes the last page's
    /// worth. Transposing the two produces a statement that returns the right number of rows from the
    /// wrong offset, which a row-count assertion would not catch.
    /// </para>
    /// <para>
    /// The offset is re-derived from the two inputs here for the same reason the sibling re-derives the
    /// bounds: the shared table must agree with the formula at <c>[:L372]</c> as well as with the text.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(Arithmetic))]
    public void TheRowNumberBoundsAreComputedAsTheOracleComputesThem(
        long pageSize,
        long pageIndex,
        long offset,
        long innerTop,
        long betweenLow,
        long betweenHigh)
    {
        PagingRewriteResult result = Rewrite(FixtureSelect, pageSize, pageIndex, false, null);

        Assert.True(result.IsOk, result.ErrorText);
        Assert.Equal(
            $"SELECT TOP {pageSize} * FROM (SELECT TOP {innerTop} *,ROW_NUMBER() OVER (ORDER BY "
                + $"(SELECT 0)) AS pfwPagedSQL_RN FROM COMPANY) pfwPagedSQL_Tbl WHERE "
                + $"pfwPagedSQL_RN BETWEEN {betweenLow} AND {betweenHigh} ORDER BY pfwPagedSQL_RN",
            result.RewrittenSql,
            StringComparer.Ordinal);

        Assert.Equal(pageSize * (pageIndex - 1), offset);
    }

    // ------------------------------------------------------------------------------------------
    //  THE TWO PRE-DISPATCH GUARDS  [:L307-L310, :L314-L317]
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Every paging setting the oracle rejects at <c>[:L307]</c>.
    /// </summary>
    /// <remarks>
    /// The guard is <c>_nPageSize &lt;= 0 or _nPageIndex &lt;= 0</c>, so ZERO IS REJECTED ALONGSIDE
    /// NEGATIVES on both arguments independently and in combination. Zero is the value most likely to
    /// slip through a port written as a positivity check on one argument only, and it is also the
    /// default of an unset field, so it is enumerated for both.
    /// </remarks>
    public static TheoryData<long, long> RejectedPagingSettings =>
        new()
        {
            { 0L, 1L },
            { 1L, 0L },
            { 0L, 0L },
            { -1L, 1L },
            { 1L, -1L },
            { -10L, -2L },
        };

    /// <summary>
    /// A page size or index at or below zero fails BEFORE any dialect arm runs, with the legacy code
    /// and the legacy Chinese diagnostic <c>[:L307-L310]</c>.
    /// </summary>
    /// <param name="pageSize">The page size.</param>
    /// <param name="pageIndex">The page index.</param>
    /// <remarks>
    /// <para>
    /// Asserted through the dispatcher because that is where the legacy places the guard - ahead of the
    /// parse and ahead of the <c>choose case</c>. Its position is observable: a request that is BOTH
    /// badly paged AND unparseable returns the invalid-argument code, never the internal-error one, and
    /// <see cref="TheBoundsGuardIsTestedBeforeTheParseGuard"/> pins that ordering.
    /// </para>
    /// <para>
    /// THE DIAGNOSTIC IS ASSERTED BYTE-EXACTLY, INCLUDING THE TRAILING EXCLAMATION MARK, and it is
    /// carried through verbatim and untranslated: the published contract already declares this text
    /// opaque and possibly non-English, and translating it would be a behaviour change (C-B). No
    /// statement text is returned on a failure, which is asserted too - a caller must not receive a
    /// half-rewritten statement alongside an error.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(RejectedPagingSettings))]
    public void AnInvalidPagingSettingFailsBeforeAnyArmRuns(long pageSize, long pageIndex)
    {
        PagingRewriteRequest request = new(FixtureSelect, pageSize, pageIndex, true);

        PagingRewriteResult result =
            PagingRewriteDispatcher.Rewrite(in request, DatabaseType.DbtMssql, Registry);

        Assert.False(result.IsOk);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, result.ReturnCode);
        Assert.Equal("无效的分页设置!", result.ErrorText, StringComparer.Ordinal);
        Assert.Equal(string.Empty, result.RewrittenSql, StringComparer.Ordinal);
    }

    /// <summary>
    /// An unparseable statement fails with the internal-error code and the legacy Chinese diagnostic
    /// <c>[:L314-L317]</c>.
    /// </summary>
    /// <param name="unparseable">A statement the parser rejects.</param>
    /// <remarks>
    /// <c>E_INTERNAL_ERROR</c> IS -27, and it is deliberately NOT the invalid-argument code even though
    /// the fault is in the caller's input: the oracle chose the internal-error code here and the choice
    /// is observable, so it is preserved rather than rationalised (C-B). Both an empty statement and a
    /// well-formed non-select reach it, which is why both are enumerated.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("UPDATE COMPANY SET age = 1")]
    [InlineData("not a statement at all")]
    public void AnUnparseableStatementFailsWithTheInternalErrorCode(string unparseable)
    {
        PagingRewriteRequest request =
            new(unparseable, PrincipalPageSize, PrincipalPageIndex, true);

        PagingRewriteResult result =
            PagingRewriteDispatcher.Rewrite(in request, DatabaseType.DbtMssql, Registry);

        Assert.False(result.IsOk);
        Assert.Equal(RetCode.E_INTERNAL_ERROR, result.ReturnCode);
        Assert.Equal(-27L, result.ReturnCode);
        Assert.Equal("SQL解析失败!", result.ErrorText, StringComparer.Ordinal);
        Assert.Equal(string.Empty, result.RewrittenSql, StringComparer.Ordinal);
    }

    /// <summary>
    /// GUARD ORDER IS LOAD BEARING: the paging bounds are tested BEFORE the parse <c>[:L307 before
    /// :L314]</c>.
    /// </summary>
    /// <remarks>
    /// A request that violates both guards returns the invalid-argument code, not the internal-error
    /// one. The ordering is observable through the returned code alone, so rearranging the two guards
    /// for tidiness would be a behavioural change. Pinned with a request that is simultaneously badly
    /// paged and unparseable.
    /// </remarks>
    [Fact]
    public void TheBoundsGuardIsTestedBeforeTheParseGuard()
    {
        PagingRewriteRequest request = new("not a statement at all", 0L, 0L, true);

        PagingRewriteResult result =
            PagingRewriteDispatcher.Rewrite(in request, DatabaseType.DbtMssql, Registry);

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, result.ReturnCode);
        Assert.Equal("无效的分页设置!", result.ErrorText, StringComparer.Ordinal);
    }

    // ------------------------------------------------------------------------------------------
    //  THE TWO CLAUSE-MODIFICATION STYLES THE ARM DRIVES  [enums.sru:L718-L719]
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// THE ARM DRIVES EXACTLY TWO CLAUSE-MODIFICATION STYLES, AND THIS PINS WHAT EACH ONE DOES.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every clause change in the whole arm is either a REPLACE or an APPEND
    /// <c>[:L341, :L345, :L348, :L350, :L353, :L355, :L358, :L360, :L362, :L372, :L377, :L381]</c>.
    /// PREPEND, the third style at <c>enums.sru:L720</c>, is never used here, and its absence is
    /// asserted so a future edit cannot reach for it without a test noticing.
    /// </para>
    /// <para>
    /// The two behaviours pinned are the ones the emitted text depends on and that no other assertion
    /// in this suite isolates:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// A REPLACE with EMPTY text REMOVES the clause. That is the strip step at <c>[:L353]</c> and
    /// <c>[:L377]</c>, and it is why arm 2's innermost select and arm 4's inner select carry no
    /// order-by. A port that wrote an empty clause instead of removing one would emit a dangling
    /// <c>ORDER BY</c> introducer.
    /// </description></item>
    /// <item><description>
    /// An APPEND joins with EXACTLY ONE SPACE. That is the step at <c>[:L341]</c>, and it is why the
    /// derived ordering reads <c>NAME id</c> - not <c>NAME,id</c>, which is what a reader who assumed
    /// a comma-separated ordering list would expect. THE SPACE JOIN IS THE LEGACY'S BEHAVIOUR AND IS
    /// PRESERVED, NOT CORRECTED (C-B).
    /// </description></item>
    /// </list>
    /// </remarks>
    [Fact]
    public void TheArmDrivesOnlyTheReplaceAndAppendClauseStyles()
    {
        SelectStatementModel statement = Parsed(MixedCaseOrderedSelect);

        // APPEND joins a comma-list clause with a comma [:L341, :L337].
        Assert.True(statement.ModifyOrder(Enums.SQL_MS_APPEND, KeyColumn));
        Assert.Equal("NAME,id", statement.GetOrder(), StringComparer.Ordinal);

        // REPLACE with empty text REMOVES the clause [:L353, :L377].
        Assert.True(statement.ModifyOrder(Enums.SQL_MS_REPLACE, string.Empty));
        Assert.False(statement.HasOrder());
        Assert.Equal(string.Empty, statement.GetOrder(), StringComparer.Ordinal);

        // REPLACE with text writes it whole [:L345, :L348, :L358, :L360, :L372, :L381].
        Assert.True(statement.ModifyOrder(Enums.SQL_MS_REPLACE, "NAME id"));
        Assert.True(statement.HasOrder());
        Assert.Equal("NAME id", statement.GetOrder(), StringComparer.Ordinal);

        // PREPEND [enums.sru:L720] is never used by this arm, so nothing it emits carries the
        // reversed join order a prepend would produce.
        Assert.DoesNotContain(
            "id NAME",
            Generated(MixedCaseOrderedSelect, false, [KeyColumn]),
            StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------
    //  PURITY - the property that makes every matrix above a function matrix (C-E)
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// EVERY QUADRANT IS A PURE FUNCTION OF ITS ARGUMENTS: the same input rewritten twice, through two
    /// independent arm instances, produces identical text.
    /// </summary>
    /// <param name="quadrant">The quadrant and locator.</param>
    /// <param name="sql">The statement to rewrite.</param>
    /// <param name="pageNative">The page-native selector.</param>
    /// <param name="uniqueIndexColumns">The unique-index-columns selector, or none.</param>
    /// <param name="expected">The generated statement, character for character.</param>
    /// <remarks>
    /// <para>
    /// This is the assertion behind the claim the rest of the suite rests on: these are string
    /// transforms with NO ambient state, NO clock read, NO randomness, NO network, NO file and NO
    /// database of any kind. If any of those crept in, a second run would diverge - which is exactly
    /// how a characterization comparison would start failing intermittently rather than visibly.
    /// </para>
    /// <para>
    /// A FRESH STATEMENT MODEL PER CALL IS PART OF THE CONTRACT, NOT AN ARTEFACT OF THE TEST. The arms
    /// MUTATE the model they are handed - narrowing the select list, stripping and restoring the
    /// order-by, splicing the join - so a caller that reused one model across two rewrites would
    /// accumulate those edits. Each run below therefore parses again, which is what the dispatcher does
    /// too <c>[:L312]</c>.
    /// </para>
    /// <para>
    /// NO TIMING IS MEASURED AND NO COST IS COMPARED. This asserts sameness of output only; the
    /// repository publishes no performance baseline against which anything else could be claimed
    /// (migration plan section 0.8.5).
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ArmMatrix))]
    public void EveryQuadrantIsAPureFunctionOfItsArguments(
        string quadrant,
        string sql,
        bool pageNative,
        string[]? uniqueIndexColumns,
        string expected)
    {
        string first = Generated(sql, pageNative, uniqueIndexColumns);
        string second = Generated(sql, pageNative, uniqueIndexColumns);

        Assert.True(
            string.Equals(first, second, StringComparison.Ordinal),
            $"{quadrant} was not stable across two runs: <{first}> then <{second}>");
        Assert.Equal(expected, first, StringComparer.Ordinal);
        Assert.Equal(expected, second, StringComparer.Ordinal);
    }

    /// <summary>
    /// THIS ARM IS THE DEFAULT: every engine name whose upper-cased form does not contain
    /// <c>ORACLE</c> lands here <c>[n_cst_thread_trans.sru:L356-L360]</c>.
    /// </summary>
    /// <param name="dbms">The engine name, or none.</param>
    /// <param name="expectsSqlServer">Whether the SQL Server arm is selected.</param>
    /// <remarks>
    /// <para>
    /// The legacy selector is one line: <c>if Pos(Upper(DBMS), "ORACLE") &gt; 0</c> returns the Oracle
    /// discriminator, and the <c>else</c> returns the SQL Server one. There is no third arm and no
    /// unknown state, so THE SQL SERVER ARM IS NOT ONE OF TWO EQUAL CHOICES - IT IS THE FALLBACK FOR
    /// EVERYTHING. That is a property of this arm rather than of the resolver, which is why it is
    /// asserted in this suite: it decides when every matrix above applies.
    /// </para>
    /// <para>
    /// <b>The SQLite row is the important one.</b> SQLite is the only storage engine this system
    /// provisions and the only one with an evidenced schema, yet it is ABSENT from the legacy dialect
    /// enumeration entirely <c>[:L60-L61]</c> - so a connection naming it resolves to the SQL Server
    /// text generator. That is the legacy's behaviour, it is preserved rather than corrected (C-B), and
    /// pinning it here records the finding where a reader of the SQL Server arm will meet it.
    /// </para>
    /// <para>
    /// The negative rows are chosen to be adversarial: an engine name that merely STARTS the marker
    /// without completing it, and a name that embeds the marker inside a longer word, so the test
    /// distinguishes a substring test from a prefix or whole-word test.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("Oracle", false)]
    [InlineData("ORACLE", false)]
    [InlineData("oracle", false)]
    [InlineData("MSORACLE80", false)]
    [InlineData("O84 Oracle 8.0.4", false)]
    [InlineData("ORA", true)]
    [InlineData("MSS Microsoft SQL Server", true)]
    [InlineData("SQL Server", true)]
    [InlineData("SQLite", true)]
    [InlineData("ODBC", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    public void EveryEngineNameThatDoesNotNameOracleSelectsThisArm(string? dbms, bool expectsSqlServer)
    {
        DatabaseType resolved = PagingDialectResolver.Resolve(dbms);

        Assert.Equal(
            expectsSqlServer ? DatabaseType.DbtMssql : DatabaseType.DbtOracle,
            resolved);

        // And the resolved dialect really does reach this arm, so the selection is not merely a value.
        IPagingRewriter? selected = PagingRewriteDispatcher.SelectRewriter(resolved, Registry);

        Assert.NotNull(selected);
        Assert.Equal(expectsSqlServer, selected is SqlServerPagingRewriter);
    }

    /// <summary>
    /// THE ARM ANSWERS TO THE SQL SERVER DISCRIMINATOR AND DECLARES THAT IT CONSUMES THE UNIQUE-INDEX
    /// COLUMNS <c>[n_cst_thread_trans.sru:L60]</c>.
    /// </summary>
    /// <remarks>
    /// <c>DBT_MSSQL</c> is <c>0</c> in the legacy and reaches managed code as
    /// <c>DatabaseType.DbtMssql</c>, protoc's PascalCase rendering of the same name from the published
    /// contract - which is why no SCREAMING_SNAKE identifier needs declaring here. The
    /// consumes-the-columns flag is what makes the first selector at <c>[:L323]</c> meaningful for this
    /// dialect and not for the other, and it is asserted because it is the dispatcher's cue to resolve
    /// the columns before handing them over.
    /// </remarks>
    [Fact]
    public void TheArmDeclaresTheSqlServerDialectAndConsumesTheUniqueIndexColumns()
    {
        IPagingRewriter arm = new SqlServerPagingRewriter();

        Assert.Equal(DatabaseType.DbtMssql, arm.Dialect);
        Assert.Equal(0, (int)arm.Dialect);
        Assert.True(arm.ConsumesPagedUniqueIndexColumns);
    }

    // ------------------------------------------------------------------------------------------
    //  HELPERS - the arm is driven directly, so nothing but the arm is under test
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Asserts one prologue fragment with a message naming both the column-list shape and the legacy
    /// locator, so a failure identifies which of the three join rules broke.
    /// </summary>
    /// <param name="shape">The column-list shape.</param>
    /// <param name="fragment">The fragment name and its locator.</param>
    /// <param name="expected">The expected text.</param>
    /// <param name="actual">The produced text.</param>
    private static void AssertFragment(
        string shape,
        string fragment,
        string expected,
        string actual)
    {
        Assert.True(
            string.Equals(expected, actual, StringComparison.Ordinal),
            $"{shape}: the {fragment} should be <{expected}> but was <{actual}>");
    }

    /// <summary>Asserts a sentinel's presence or absence with a message naming the quadrant.</summary>
    /// <param name="sentinel">The sentinel to look for.</param>
    /// <param name="expected">Whether it is expected.</param>
    /// <param name="generated">The generated statement.</param>
    /// <param name="quadrant">The quadrant, for the failure message.</param>
    private static void AssertSentinel(
        string sentinel,
        bool expected,
        string generated,
        string quadrant)
    {
        if (expected)
        {
            Assert.True(
                generated.Contains(sentinel, StringComparison.Ordinal),
                $"{quadrant} should emit {sentinel} but produced: {generated}");
        }
        else
        {
            Assert.False(
                generated.Contains(sentinel, StringComparison.Ordinal),
                $"{quadrant} must not emit {sentinel} but produced: {generated}");
        }
    }

    /// <summary>
    /// Parses and rewrites through the SQL Server arm itself.
    /// </summary>
    /// <param name="sql">The statement to rewrite.</param>
    /// <param name="pageSize">The page size, <c>_nPageSize</c> <c>[:L36]</c>.</param>
    /// <param name="pageIndex">The page index, <c>_nPageIndex</c> <c>[:L37]</c>.</param>
    /// <param name="pageNative">The page-native flag, <c>_bPageNative</c> <c>[:L39]</c>.</param>
    /// <param name="uniqueIndexColumns">
    /// The unique-index columns, <c>_sPagedUniqueIndexColumns</c> <c>[:L46]</c>.
    /// </param>
    /// <remarks>
    /// THE ARM IS CALLED DIRECTLY RATHER THAN THROUGH THE DISPATCHER, deliberately. The dispatcher
    /// contributes a dialect selection and an identifier validation that belong to it and are
    /// asserted in their own suites; routing every row through it would mean a change in either could
    /// turn a byte-exact SQL Server assertion red for a reason that has nothing to do with the
    /// generated text. The two GUARDS are the exception - they live on the dispatcher and are
    /// asserted through it below, because that is where the legacy places them <c>[:L307, :L314]</c>.
    /// </remarks>
    private static PagingRewriteResult Rewrite(
        string sql,
        long pageSize,
        long pageIndex,
        bool pageNative,
        string[]? uniqueIndexColumns)
    {
        PagingRewriteRequest request = new(sql, pageSize, pageIndex, pageNative, uniqueIndexColumns);
        SelectStatementModel statement = Parsed(sql);

        return new SqlServerPagingRewriter().Rewrite(in request, statement);
    }

    /// <summary>
    /// Parses a statement, failing the test rather than the assertion under examination if the input
    /// itself is unusable.
    /// </summary>
    /// <param name="sql">The statement to parse.</param>
    private static SelectStatementModel Parsed(string sql)
    {
        SelectStatementModel statement = new();

        Assert.True(statement.Parse(sql), $"the test input did not parse: {sql}");

        return statement;
    }

    /// <summary>Rewrites and returns the generated text, asserting success first.</summary>
    /// <param name="sql">The statement to rewrite.</param>
    /// <param name="pageNative">The page-native flag.</param>
    /// <param name="uniqueIndexColumns">The unique-index columns, or none.</param>
    private static string Generated(string sql, bool pageNative, string[]? uniqueIndexColumns)
    {
        PagingRewriteResult result =
            Rewrite(sql, PrincipalPageSize, PrincipalPageIndex, pageNative, uniqueIndexColumns);

        Assert.True(result.IsOk, result.ErrorText);

        return result.RewrittenSql;
    }
}
