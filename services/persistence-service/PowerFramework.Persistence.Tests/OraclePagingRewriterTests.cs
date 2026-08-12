// ==============================================================================================
//  OraclePagingRewriterTests - the parity suite for the Oracle arm of _of_buildpagedsql
// ==============================================================================================
//
//  WHAT IS UNDER TEST
//  ------------------
//  PowerFramework.Persistence.Sql.Paging.OraclePagingRewriter, the managed form of the
//  `case TransObject.DBT_ORACLE` arm at
//  ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L386-L395, together with the
//  two pre-dispatch guards at [:L307-L310] and [:L314-L317] that every arm sits behind.
//
//  Nine lines of PowerScript are the entire specification, and every expectation below was
//  re-derived from them character by character with `cat -A` rather than copied from the managed
//  implementation. That direction matters: an expectation read off the implementation cannot
//  detect a defect in the implementation, it can only agree with one.
//
//
//  NO ORACLE ANYWHERE - CONSTRAINT C-E IN ITS STRONGEST FORM
//  --------------------------------------------------------
//  This suite provisions NO Oracle instance, references NO Oracle client package
//  (no Oracle.ManagedDataAccess, no ODP.NET), opens NO connection, and starts NO container.
//  It touches no file, no socket and no database of any kind.
//
//  That is not a compromise forced by the environment - it is the faithful test. AAP section 0.6.4
//  records that the repository contains no Oracle schema, no Oracle connection string and no
//  Oracle DDL anywhere: the entire Oracle presence in the legacy estate is one discriminator
//  constant (`constant long DBT_ORACLE = 1` at n_cst_thread_task_sqlquery.sru's sibling
//  n_cst_thread_trans.sru:L61) and one statement generator. A statement generator is a pure string
//  transform, so a string-in / string-out theory exercises 100% of what exists. Standing an engine
//  up would add a dependency that could only ever agree or disagree about SQL SEMANTICS, which is
//  not what parity is measured on - byte-exact generated TEXT is.
//
//
//  THE ONE SPACE, AND WHY IT MUST NEVER BE "CLEANED UP" (C-K)
//  ---------------------------------------------------------
//  [:L394] ends with:            ... + ") pfwPagedSQL_TblOuter " + &
//  [:L395] begins with:          "WHERE pfwPagedSQL_RN BETWEEN " + ...
//
//  `&` is PowerScript's LINE CONTINUATION. It contributes no character of its own, so the trailing
//  space that closes the first literal is the ONLY separator between the outer table's alias and
//  the final WHERE. Exactly one space: not two, and not zero.
//
//  This is precisely the detail a re-typed expectation loses - the eye reads a line break and
//  supplies whatever spacing looks tidy - which is why
//  ExactlyOneSpaceSeparatesTheOuterAliasFromTheFinalWhere asserts the doubled and elided forms are
//  ABSENT as well as asserting the single-spaced form is present. A byte-exact parity criterion
//  exists for exactly this class of difference.
//
//  Three sibling spacing facts, each transcribed and none tidied (C-B):
//    * `pfwPagedSQL_TblInnerInner.*,ROW_NUMBER()` - NO space at the comma.
//    * `OVER (ORDER BY ` - one space before the parenthesis, none after it, one after `BY`.
//    * ` <= `, ` BETWEEN ` and ` AND ` - single-spaced on both sides.
//
//
//  WHY THE MODIFY-CALL RECORDER IS AUTHORED HERE RATHER THAN REUSED (C-K)
//  ---------------------------------------------------------------------
//  The requirement for this file asked for "the ordered modify-call recorder from TestDoubles.cs".
//  IT DOES NOT EXIST, AND IT CANNOT. Two findings, both checked:
//
//   1. TestDoubles.cs carries ScriptedCarrierSurface / RecordedCarrierCall / CarrierCallKind.Modify,
//      which is an ordered recorder for a DATAWINDOW carrier's `Modify(string)` script calls -
//      a different surface with a different signature and a different legacy origin. Nothing in the
//      repository records SelectStatementModel clause modification.
//
//   2. Interception is impossible. SelectStatementModel is `internal sealed` with non-virtual
//      members, and IPagingRewriter.Rewrite takes it CONCRETELY rather than behind an interface, so
//      no substitute can be injected and no call can be observed as it happens.
//
//  The only mechanism the sealed model permits is a STATE DIFF, and that is what
//  ClauseMutationRecorder below is: it snapshots all six clause kinds - the six the legacy parser
//  declares at ws_objects/pfw.utility.parser.pbl.src/n_sql.sru:L38-L49 - before and after the arm
//  runs, and reports the mutations it finds in canonical clause order. Authored as a PRIVATE NESTED
//  type so no shared file is edited to serve one suite.
//
//  ITS ONE HONEST LIMIT, stated here rather than discovered later: a replace-with-empty on a clause
//  that is ALREADY absent returns true and changes nothing, so on the no-order-by path "the arm did
//  not call ModifyOrder" is indistinguishable from "the arm called ModifyOrder and it was a no-op".
//  What IS observable there, and what is therefore asserted, is that NO clause changed at all. On
//  the has-order-by path the strip IS observable, and the recorder pins it as the single mutation.
//
//
//  READ-ONLY LEGACY (C-C)
//  ----------------------
//  Every `[:Lnnn]` below locates a line of
//  ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru, which is the behavioural
//  oracle and is never edited, never moved and never reformatted. Locators are cited; source text
//  is not transcribed beyond the short fragments that ARE the expected output.
//
//
//  NO PERFORMANCE CLAIM (AAP section 0.8.5)
//  ----------------------------------------
//  Nothing here asserts, measures or implies that the triple nest is efficient, that one dialect's
//  statement outperforms the other's, or that any rewrite is fast. The repository publishes no
//  latency budget, no throughput target and no SLA, so there is no baseline against which such a
//  claim could be made. Behaviour is the only subject.
//
//
//  NO SCREAMING_SNAKE IDENTIFIER IS DECLARED (AAP section 0.7.2, 0.4.5.3)
//  ---------------------------------------------------------------------
//  .editorconfig scopes its CA1707 / IDE1006 suppressions to the specific files that carry
//  preserved legacy constant spellings, and this file is deliberately not one of them - warnings
//  are errors repository-wide, so an underscore-bearing declaration here would be a build error.
//  Everything of that shape is CONSUMED instead: `Enums.SQL_MS_REPLACE` from the shared kernel,
//  `RetCode.OK`, `RetCode.E_INVALID_ARGUMENT` and `RetCode.E_INTERNAL_ERROR` from the return-code
//  catalogue, and `DatabaseType.DbtOracle` from the generated contract, where protoc renders
//  DBT_ORACLE in PascalCase.
//
//
//  ORDINAL, NEVER NORMALISED
//  -------------------------
//  Every string comparison uses StringComparer.Ordinal or StringComparison.Ordinal. No expectation
//  is trimmed, collapsed, case-folded or whitespace-normalised anywhere, and
//  AWhitespacePerturbedStatementFailsTheOrdinalComparison proves that mechanically rather than
//  claiming it in prose.
//
//  NO USER RULES GOVERN THIS FILE. `review_rules` returns exactly one line - "No user rules
//  provided." - so enterprise-standard best practice applies in their place, per AAP section 0.7.
// ==============================================================================================

// System.Reflection is imported LOCALLY rather than globally, following the rule GlobalUsings.cs
// states for itself: reflection is used by only a few suites, so its type names stay out of the
// ambient scope and each consumer names the dependency. Used here by
// NothingInTheArmsAssemblyGraphReachesAnOracleClient, which is the mechanical enforcement of C-E.
using System.Reflection;
using PowerFramework.Persistence.Sql.Paging;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// The Oracle paging arm of <c>_of_buildpagedsql</c>, asserted as a byte-exact statement matrix
/// plus the structural absences that distinguish it from the other dialect, plus the two guards it
/// sits behind.
/// </summary>
/// <remarks>
/// Ported behaviour lives at
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L303-L404</c>, read only per
/// constraint C-C. The arm itself is <c>[:L386-L395]</c>. See the file header for the derivation
/// method, the constraints this suite discharges, and the two findings behind
/// <see cref="ClauseMutationRecorder"/>.
/// </remarks>
public sealed class OraclePagingRewriterTests
{
    // ------------------------------------------------------------------------------------------
    //  INPUTS
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// THE PRINCIPAL INPUT: the primary fixture's own retrieve, at <c>dw_sqlite.srd:L14</c>.
    /// </summary>
    /// <remarks>
    /// Taken from <see cref="DwSqliteFixture.RetrieveStatement"/> rather than re-spelled, so the
    /// matrix is anchored to the repository's one real updatable statement and cannot drift from
    /// it. It carries NO order-by, which is what reaches the <c>''</c> substitution at
    /// <c>[:L392]</c>.
    /// </remarks>
    private const string FixtureSelect = DwSqliteFixture.RetrieveStatement;

    /// <summary>
    /// The fixture's six columns enumerated, so the innermost derived table's body is
    /// distinguishable rather than a bare star.
    /// </summary>
    /// <remarks>
    /// This is the input that would expose a select-list mutation. The other dialect's arms narrow
    /// the select list and then restore it <c>[:L345, :L348, :L355, :L358, :L381]</c>; this one
    /// never touches it, so an enumerated list must survive into the innermost derived table
    /// unchanged, character for character.
    /// </remarks>
    private const string EnumeratedSelect =
        "SELECT id, name, age, address, salary, birth FROM COMPANY";

    /// <summary>
    /// AN INPUT WHOSE ORDER-BY IS DELIBERATELY UPPER CASE while its select list is lower case.
    /// </summary>
    /// <remarks>
    /// The arm reads the clause body at <c>[:L389]</c> and splices it into the window function
    /// verbatim - it never lowers it, unlike the other dialect's prologue which lowers a copy at
    /// <c>[:L327]</c> for a containment test. With a lower-case order-by the two spellings would be
    /// identical and a case-folding defect would be invisible, so the case difference is the whole
    /// point of this input.
    /// </remarks>
    private const string MixedCaseOrderedSelect = "SELECT id, name FROM COMPANY ORDER BY NAME";

    /// <summary>
    /// An input carrying a <c>WHERE</c> and a two-term order-by with an explicit direction, so the
    /// clause body that reaches <c>OVER (ORDER BY ...)</c> is more than a bare identifier.
    /// </summary>
    /// <remarks>
    /// The <c>WHERE</c> is here to be LEFT ALONE: the arm modifies exactly one clause and this
    /// input gives the recorder a second populated clause to prove that.
    /// </remarks>
    private const string FilteredOrderedSelect =
        "SELECT id, name, age FROM COMPANY WHERE age > 18 ORDER BY age, salary DESC";

    /// <summary>
    /// An input populating five of the six clause kinds at once, so the recorder's
    /// no-other-clause-was-touched assertion has the widest possible surface to fail on.
    /// </summary>
    private const string GroupedOrderedSelect =
        "SELECT age, COUNT(*) FROM COMPANY WHERE age > 18 GROUP BY age HAVING COUNT(*) > 1 "
            + "ORDER BY age";

    /// <summary>
    /// An input whose order-by is spaced UNCONVENTIONALLY inside the clause body.
    /// </summary>
    /// <remarks>
    /// The two spaces around the comma are deliberate and are expected to survive verbatim. The
    /// arm splices the clause BODY, so any re-spacing inside it is a parity break - and one that
    /// would be invisible to a whitespace-normalising assertion, which is exactly why this input
    /// exists.
    /// </remarks>
    private const string OddlySpacedOrderedSelect = "SELECT * FROM T ORDER BY  a  ,  b";

    /// <summary>
    /// A MULTI-BLOCK input: two select blocks joined by a set operator, with a trailing order-by.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS ROW IS NOT A DUPLICATE OF THE ABSENT-ORDER-BY ROWS AND ITS BEHAVIOUR IS SUBTLER.
    /// <c>hasorder()</c> and <c>getorder()</c> in their no-argument form address the FIRST block
    /// only - <c>n_sql.sru:L24</c> and <c>:L36</c>, whose indexed overloads at <c>:L25</c> and
    /// <c>:L37</c> are what reach any other block - and the trailing order-by here belongs to the
    /// SECOND. So the arm takes its <see langword="else"/> branch, substitutes <c>''</c>, and the
    /// caller's own order-by travels INTO the innermost derived table untouched.
    /// </para>
    /// <para>
    /// That is why <see cref="AnExistingOrderByAppearsExactlyOnceInTheGeneratedStatement"/> is
    /// scoped to single-block inputs and this case is asserted separately: here the order-by text
    /// legitimately coexists with the substitution, and conflating the two would either weaken the
    /// exactly-once assertion or make this row look like a defect.
    /// </para>
    /// </remarks>
    private const string UnionOrderedSelect = "SELECT id FROM A UNION SELECT id FROM B ORDER BY id";

    /// <summary>The fixture's key column, <c>dw_sqlite.srd:L8</c>, as a bare identifier.</summary>
    /// <remarks>
    /// Used ONLY as a unique-index column the Oracle arm must ignore. It is a real column of the
    /// real fixture so that the request is well formed and its being ignored is the arm's choice
    /// rather than a consequence of the value being unusable.
    /// </remarks>
    private const string KeyColumn = DwSqliteFixture.KeyColumnName;

    /// <summary>The page size used by every row of the statement matrix.</summary>
    private const long PrincipalPageSize = 10L;

    /// <summary>
    /// The page index used by the statement matrix - TWO RATHER THAN ONE, DELIBERATELY.
    /// </summary>
    /// <remarks>
    /// On page one the <c>BETWEEN</c> lower bound is 1 whether or not the <c>(pageIndex - 1)</c>
    /// term at <c>[:L395]</c> is present, so an arm that dropped it entirely would still emit the
    /// right text. The one-based ordinal is only observable from page two onwards. Page one is
    /// covered separately by <see cref="Arithmetic"/>, where its degenerate values are the subject
    /// rather than a blind spot.
    /// </remarks>
    private const long PrincipalPageIndex = 2L;

    // ------------------------------------------------------------------------------------------
    //  TEXT THAT MUST NEVER APPEAR - spelled here ONLY so its absence is assertable
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The OTHER dialect's empty-order-by substitution <c>[:L370, :L379]</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE PARITY TRAP THIS SUITE EXISTS TO CATCH. Both dialects substitute an expression when the
    /// statement carries no order-by, both legacy comments read <c>原始结果集排序</c> - original
    /// result-set ordering - and the two substitutions are DIFFERENT TEXT. Oracle substitutes two
    /// apostrophes <c>[:L392]</c>; the other arm substitutes this parenthesised scalar subquery.
    /// Sharing one constant between the two rewriters would silently break byte-exact parity while
    /// leaving every assertion looking plausible, which is why the implementation declares them
    /// separately and why this suite asserts this one is ABSENT rather than merely different.
    /// </para>
    /// <para>
    /// Spelled as a local literal rather than referenced from the other arm: this suite must not
    /// depend on that file, and an absence assertion needs the text, not the declaration.
    /// </para>
    /// </remarks>
    private const string OtherDialectAbsentOrderBySubstitution = "(SELECT 0)";

    /// <summary>
    /// The other dialect's <c>INNER JOIN</c> subquery alias <c>[:L333, :L350, :L362]</c>, including
    /// its doubled <c>t</c>.
    /// </summary>
    /// <remarks>
    /// Misspelled in the legacy and left misspelled there (C-B). It appears nowhere in Oracle
    /// output, and <see cref="TheOtherDialectsSentinelAliasesNeverAppear"/> pins that.
    /// </remarks>
    private const string OtherDialectJoinAlias = "pfwPagedSQL_OutterTbl";

    /// <summary>
    /// The other dialect's row-number derived-table alias <c>[:L356, :L382]</c>.
    /// </summary>
    /// <remarks>
    /// A GREP HAZARD, and the reason the absence assertion is written with delimiters: this value
    /// is a strict prefix of all three Oracle aliases, so a bare sub-string search for it would
    /// match <c>pfwPagedSQL_TblInner</c>, <c>pfwPagedSQL_TblInnerInner</c> and
    /// <c>pfwPagedSQL_TblOuter</c> and the absence assertion would fail against correct output.
    /// </remarks>
    private const string OtherDialectRowNumberTableAlias = "pfwPagedSQL_Tbl";

    // ------------------------------------------------------------------------------------------
    //  THE REGISTRY, AND THE SUBSTITUTE OCCUPYING THE OTHER DIALECT'S SLOT
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The rewriter registry this suite hands the dispatcher: the REAL Oracle arm, plus a stand-in
    /// occupying the other dialect's slot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE OTHER DIALECT'S REAL IMPLEMENTATION IS DELIBERATELY NOT REFERENCED HERE, mirroring the
    /// sibling suite's reasoning in the opposite direction. The dispatcher resolves an arm by
    /// dialect, so a registry has to be able to answer for both discriminators
    /// <c>[n_cst_thread_trans.sru:L60-L61]</c> - but nothing in this suite ever dispatches on the
    /// other one, and coupling an Oracle-only suite to the other arm's implementation would mean a
    /// change over there could turn assertions red over here.
    /// </para>
    /// <para>
    /// This also honours the principle the arm's own documentation states: the two dialect arms
    /// have no reason to reference each other, and neither does either arm's test suite.
    /// </para>
    /// </remarks>
    private static IPagingRewriter[] Registry =>
        [new OraclePagingRewriter(), new OtherDialectRewriter()];

    /// <summary>
    /// A fully implemented <see cref="IPagingRewriter"/> occupying the non-Oracle dialect slot, so
    /// the dispatcher's registry can answer for both discriminators.
    /// </summary>
    /// <remarks>
    /// Not a placeholder: every member does real work and none throws. Its
    /// <see cref="Rewrite(in PagingRewriteRequest, SelectStatementModel)"/> returns the statement
    /// unchanged, which is a legitimate total function, and it is never reached by any test here -
    /// <see cref="TheRegistryIsOnlyEverDispatchedOnForOracle"/> asserts that, so the claim is
    /// checked rather than asserted in prose. <see cref="ConsumesPagedUniqueIndexColumns"/> is
    /// <see langword="true"/>, matching the real occupant of this slot, whose arms DO splice
    /// caller-supplied identifiers at <c>[:L331-L336]</c>; that fidelity keeps the dispatcher's
    /// conditional validation step behaving here exactly as it would in production.
    /// </remarks>
    private sealed class OtherDialectRewriter : IPagingRewriter
    {
        /// <inheritdoc/>
        public DatabaseType Dialect => DatabaseType.DbtMssql;

        /// <inheritdoc/>
        public bool ConsumesPagedUniqueIndexColumns => true;

        /// <inheritdoc/>
        public PagingRewriteResult Rewrite(
            in PagingRewriteRequest request,
            SelectStatementModel statement)
        {
            ArgumentNullException.ThrowIfNull(statement);

            return PagingRewriteResult.Ok(statement.GetSql());
        }
    }

    // ------------------------------------------------------------------------------------------
    //  THE ORDERED CLAUSE-MUTATION RECORDER
    //  See the file header for the two findings that make a state diff the only available
    //  mechanism, and for its one honest limit.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// A snapshot of every clause a <see cref="SelectStatementModel"/> exposes, plus the rendered
    /// statement, taken at one instant.
    /// </summary>
    /// <param name="Column">The select-list body - <c>getcolumn()</c>, <c>n_sql.sru:L26</c>.</param>
    /// <param name="Table">The from body - <c>gettable()</c>, <c>n_sql.sru:L28</c>.</param>
    /// <param name="Where">The where body - <c>getwhere()</c>, <c>n_sql.sru:L30</c>.</param>
    /// <param name="Group">The group-by body - <c>getgroup()</c>, <c>n_sql.sru:L32</c>.</param>
    /// <param name="Having">The having body - <c>gethaving()</c>, <c>n_sql.sru:L34</c>.</param>
    /// <param name="Order">The order-by body - <c>getorder()</c>, <c>n_sql.sru:L36</c>.</param>
    /// <param name="OrderPresent">
    /// Whether an order-by clause exists at all - <c>hasorder()</c>, <c>n_sql.sru:L24</c>. Carried
    /// SEPARATELY from <paramref name="Order"/> because an absent clause and a present-but-empty
    /// clause both read back as an empty body, and the strip at <c>[:L390]</c> is a transition
    /// between exactly those two states.
    /// </param>
    /// <param name="Rendered">
    /// The whole statement as the model would emit it - <c>getsql()</c>, <c>n_sql.sru:L12</c>. The
    /// backstop: a mutation that somehow left all six bodies alone but changed the emitted text
    /// still shows up here.
    /// </param>
    private sealed record ClauseSnapshot(
        string Column,
        string Table,
        string Where,
        string Group,
        string Having,
        string Order,
        bool OrderPresent,
        string Rendered);

    /// <summary>
    /// Records, in canonical clause order, which clauses of a statement model changed across an
    /// operation - the only form of "ordered modify-call record" a sealed, non-virtual model admits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order is the one the legacy parser declares its modifiers in -
    /// <c>modifycolumn</c>, <c>modifytable</c>, <c>modifywhere</c>, <c>modifygroup</c>,
    /// <c>modifyhaving</c>, <c>modifyorder</c> at <c>n_sql.sru:L38-L49</c> - so a reader comparing
    /// this record against the parser's own surface sees the same sequence in the same order.
    /// </para>
    /// <para>
    /// SELF-TESTED, so the recorder cannot pass by being blind:
    /// <see cref="TheRecorderDetectsAClauseMutationWhenOneReallyHappens"/> drives a select-list
    /// replacement and an order-by restore through it and asserts both are reported. Without that,
    /// "no column mutation was recorded" would be satisfied by a recorder that records nothing.
    /// </para>
    /// </remarks>
    private static class ClauseMutationRecorder
    {
        /// <summary>The label this recorder reports for a select-list mutation.</summary>
        internal const string ColumnMutation = "column";

        /// <summary>The label this recorder reports for a from-clause mutation.</summary>
        internal const string TableMutation = "table";

        /// <summary>The label this recorder reports for a where-clause mutation.</summary>
        internal const string WhereMutation = "where";

        /// <summary>The label this recorder reports for a group-by mutation.</summary>
        internal const string GroupMutation = "group";

        /// <summary>The label this recorder reports for a having mutation.</summary>
        internal const string HavingMutation = "having";

        /// <summary>The label this recorder reports for an order-by mutation.</summary>
        internal const string OrderMutation = "order";

        /// <summary>Takes a snapshot of every clause the model exposes.</summary>
        /// <param name="statement">The model to read. Never mutated by this call.</param>
        /// <returns>The snapshot.</returns>
        internal static ClauseSnapshot Capture(SelectStatementModel statement)
        {
            ArgumentNullException.ThrowIfNull(statement);

            // Every read here is a pure read - the legacy `get*` and `has*` families are documented
            // as non-mutating and the managed forms are too - so capturing before and after cannot
            // itself perturb what is being measured.
            return new ClauseSnapshot(
                statement.GetColumn(),
                statement.GetTable(),
                statement.GetWhere(),
                statement.GetGroup(),
                statement.GetHaving(),
                statement.GetOrder(),
                statement.HasOrder(),
                statement.GetSql());
        }

        /// <summary>
        /// Reports which clauses differ between two snapshots, in the parser's own declaration
        /// order.
        /// </summary>
        /// <param name="before">The snapshot taken before the operation.</param>
        /// <param name="after">The snapshot taken after it.</param>
        /// <returns>
        /// The mutation labels, in canonical order. Empty when nothing observable changed.
        /// </returns>
        internal static IReadOnlyList<string> Diff(ClauseSnapshot before, ClauseSnapshot after)
        {
            ArgumentNullException.ThrowIfNull(before);
            ArgumentNullException.ThrowIfNull(after);

            List<string> mutations = [];

            // ORDINAL COMPARISON THROUGHOUT. A culture-sensitive comparison could treat two
            // differently spelled clause bodies as equal on some hosts, which would make this
            // recorder silently blind in exactly one region.
            if (!string.Equals(before.Column, after.Column, StringComparison.Ordinal))
            {
                mutations.Add(ColumnMutation);
            }

            if (!string.Equals(before.Table, after.Table, StringComparison.Ordinal))
            {
                mutations.Add(TableMutation);
            }

            if (!string.Equals(before.Where, after.Where, StringComparison.Ordinal))
            {
                mutations.Add(WhereMutation);
            }

            if (!string.Equals(before.Group, after.Group, StringComparison.Ordinal))
            {
                mutations.Add(GroupMutation);
            }

            if (!string.Equals(before.Having, after.Having, StringComparison.Ordinal))
            {
                mutations.Add(HavingMutation);
            }

            // THE ORDER CLAUSE IS THE ONE WITH TWO OBSERVABLE DIMENSIONS: the body can change and
            // the clause can appear or disappear. `hasorder()` is what makes the strip at [:L390]
            // visible when the body was going from text to nothing, so both are tested and either
            // one counts as the same single mutation.
            if (!string.Equals(before.Order, after.Order, StringComparison.Ordinal)
                || before.OrderPresent != after.OrderPresent)
            {
                mutations.Add(OrderMutation);
            }

            return mutations;
        }
    }

    // ------------------------------------------------------------------------------------------
    //  HELPERS
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Dispatches a request on the Oracle discriminator, asserts it succeeded, and returns the
    /// generated statement so a caller can read the text directly.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>The generated statement.</returns>
    /// <remarks>
    /// Goes through <see cref="PagingRewriteDispatcher"/> rather than calling the arm directly, so
    /// the whole path the legacy function walks - the bounds guard <c>[:L307]</c>, the parse
    /// <c>[:L312-L317]</c>, the <c>choose case</c> <c>[:L320]</c> and then the arm - is what is
    /// under test. The few assertions that need the arm in isolation call it directly and say so.
    /// </remarks>
    private static string Rewritten(in PagingRewriteRequest request)
    {
        PagingRewriteResult result =
            PagingRewriteDispatcher.Rewrite(in request, DatabaseType.DbtOracle, Registry);

        Assert.True(result.IsOk, result.ErrorText);

        return result.RewrittenSql;
    }

    /// <summary>
    /// Builds a principal-paged request for a statement and dispatches it.
    /// </summary>
    /// <param name="sql">The statement to rewrite.</param>
    /// <returns>The generated statement.</returns>
    private static string RewrittenPrincipal(string sql) =>
        Rewritten(new PagingRewriteRequest(sql, PrincipalPageSize, PrincipalPageIndex, false));

    /// <summary>
    /// Parses a statement into a model, asserting the parse succeeded.
    /// </summary>
    /// <param name="sql">The statement text.</param>
    /// <returns>The parsed model.</returns>
    /// <remarks>
    /// Uses <see cref="SelectStatementModel.Parse(string)"/> rather than the
    /// <c>ParseSql</c> factory, because the factory faithfully reproduces a legacy site that
    /// DISCARDS the parse result - useful there, useless here, where a silently unparsed model
    /// would make an assertion pass for the wrong reason.
    /// </remarks>
    private static SelectStatementModel ParsedModel(string sql)
    {
        SelectStatementModel model = new();

        Assert.True(model.Parse(sql), $"the suite's own input failed to parse: {sql}");

        return model;
    }

    /// <summary>Counts non-overlapping ordinal occurrences of a value in a statement.</summary>
    /// <param name="haystack">The text to search.</param>
    /// <param name="needle">The value to count. Must not be empty.</param>
    /// <returns>The occurrence count.</returns>
    /// <remarks>
    /// Hand-rolled rather than expressed as a split-and-count so the comparison is explicitly
    /// ordinal at every step and the empty-needle case - which would otherwise loop forever - is
    /// refused outright.
    /// </remarks>
    private static int CountOccurrences(string haystack, string needle)
    {
        ArgumentNullException.ThrowIfNull(haystack);
        ArgumentException.ThrowIfNullOrEmpty(needle);

        int count = 0;
        int index = haystack.IndexOf(needle, StringComparison.Ordinal);

        while (index >= 0)
        {
            count++;
            index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }

    // ==========================================================================================
    //  PHASE 1 - THE ONE STRATEGY, EXACTLY  [:L386-L395]
    // ==========================================================================================

    /// <summary>One row of the statement matrix.</summary>
    /// <param name="Scenario">What the row exercises, with its locator, for failure output.</param>
    /// <param name="Sql">The statement to rewrite.</param>
    /// <param name="Expected">The generated statement, character for character.</param>
    /// <param name="OrderByArgument">
    /// The exact text the window function's <c>ORDER BY</c> receives - the clause body from
    /// <c>[:L389]</c> or the substitution from <c>[:L392]</c> - carried as its own column so the
    /// substitution assertions can read it back instead of re-deriving it.
    /// </param>
    /// <param name="SingleBlock">
    /// Whether the input is one select block. Only single-block inputs can assert that an existing
    /// order-by appears exactly once, because a multi-block input's trailing order-by legitimately
    /// survives inside the innermost derived table - see <see cref="UnionOrderedSelect"/>.
    /// </param>
    /// <remarks>
    /// Held as records rather than only as theory data so the completeness assertion
    /// <see cref="TheMatrixCoversBothSubstitutionPathsAndBothBlockShapes"/> can read the selectors
    /// back and prove the matrix is not missing a path. Projecting one list into both uses is what
    /// keeps that assertion honest: it cannot pass against a matrix the theory does not run.
    /// </remarks>
    private sealed record StatementCase(
        string Scenario,
        string Sql,
        string Expected,
        string OrderByArgument,
        bool SingleBlock);

    /// <summary>A list of <see cref="StatementCase"/> accepting a row as a brace-delimited tuple.</summary>
    /// <remarks>
    /// The same shape <c>TheoryData</c> uses, and for the same reason: a row spelled as five values
    /// reads as a matrix row, whereas a row spelled as a constructor call reads as code. Private,
    /// so nothing outside this suite is exposed to a derived collection type.
    /// </remarks>
    private sealed class StatementCaseCollection : List<StatementCase>
    {
        /// <summary>Adds one row.</summary>
        /// <param name="scenario">What the row exercises, with its locator.</param>
        /// <param name="sql">The statement to rewrite.</param>
        /// <param name="expected">The generated statement, character for character.</param>
        /// <param name="orderByArgument">The window function's order-by argument.</param>
        /// <param name="singleBlock">Whether the input is one select block.</param>
        public void Add(
            string scenario,
            string sql,
            string expected,
            string orderByArgument,
            bool singleBlock) =>
            Add(new StatementCase(scenario, sql, expected, orderByArgument, singleBlock));
    }

    /// <summary>
    /// The matrix rows. Every expectation is a WHOLE STATEMENT spelled out in full, re-derived from
    /// <c>[:L394-L395]</c> fragment by fragment, at <see cref="PrincipalPageSize"/> and
    /// <see cref="PrincipalPageIndex"/> throughout.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SPELLED OUT RATHER THAN COMPOSED BY A HELPER, DELIBERATELY. A helper that assembled these
    /// from the same fragments the implementation uses would agree with the implementation by
    /// construction and could not detect a fragment being mis-spaced, mis-ordered or dropped. The
    /// cost is verbosity; the benefit is that these strings are an INDEPENDENT statement of the
    /// oracle's output and can be diffed against <c>[:L394-L395]</c> directly.
    /// </para>
    /// <para>
    /// The arithmetic is held constant across every row so that a whitespace regression cannot hide
    /// behind an arithmetic one; the bounds are varied separately by <see cref="Arithmetic"/>.
    /// </para>
    /// </remarks>
    private static StatementCaseCollection StatementCases =>
        new()
        {
            {
                "no order-by, the '' substitution, fixture statement [:L392]",
                FixtureSelect,
                "SELECT * FROM (SELECT * FROM (SELECT pfwPagedSQL_TblInnerInner.*,ROW_NUMBER() OVER "
                    + "(ORDER BY '') AS pfwPagedSQL_RN FROM (SELECT * FROM COMPANY) "
                    + "pfwPagedSQL_TblInnerInner) pfwPagedSQL_TblInner WHERE pfwPagedSQL_RN <= 20) "
                    + "pfwPagedSQL_TblOuter WHERE pfwPagedSQL_RN BETWEEN 11 AND 20",
                "''",
                true
            },
            {
                "no order-by, enumerated select list survives into the innermost table unchanged",
                EnumeratedSelect,
                "SELECT * FROM (SELECT * FROM (SELECT pfwPagedSQL_TblInnerInner.*,ROW_NUMBER() OVER "
                    + "(ORDER BY '') AS pfwPagedSQL_RN FROM (SELECT id, name, age, address, salary, "
                    + "birth FROM COMPANY) pfwPagedSQL_TblInnerInner) pfwPagedSQL_TblInner WHERE "
                    + "pfwPagedSQL_RN <= 20) pfwPagedSQL_TblOuter WHERE pfwPagedSQL_RN BETWEEN 11 "
                    + "AND 20",
                "''",
                true
            },
            {
                "order-by lifted into the window function, case preserved [:L389-L390]",
                MixedCaseOrderedSelect,
                // `NAME` KEEPS ITS CASE. The arm splices the clause body read at [:L389] with no
                // case folding of any kind, and the strip at [:L390] means the inner statement
                // carries no ORDER BY at all - so the ordering appears exactly once, in the window.
                "SELECT * FROM (SELECT * FROM (SELECT pfwPagedSQL_TblInnerInner.*,ROW_NUMBER() OVER "
                    + "(ORDER BY NAME) AS pfwPagedSQL_RN FROM (SELECT id, name FROM COMPANY) "
                    + "pfwPagedSQL_TblInnerInner) pfwPagedSQL_TblInner WHERE pfwPagedSQL_RN <= 20) "
                    + "pfwPagedSQL_TblOuter WHERE pfwPagedSQL_RN BETWEEN 11 AND 20",
                "NAME",
                true
            },
            {
                "order-by with a direction, and a WHERE the arm must leave alone [:L389-L390]",
                FilteredOrderedSelect,
                // The two-term body including `DESC` travels verbatim; the WHERE stays inside the
                // innermost derived table exactly where the caller put it.
                "SELECT * FROM (SELECT * FROM (SELECT pfwPagedSQL_TblInnerInner.*,ROW_NUMBER() OVER "
                    + "(ORDER BY age, salary DESC) AS pfwPagedSQL_RN FROM (SELECT id, name, age "
                    + "FROM COMPANY WHERE age > 18) pfwPagedSQL_TblInnerInner) pfwPagedSQL_TblInner "
                    + "WHERE pfwPagedSQL_RN <= 20) pfwPagedSQL_TblOuter WHERE pfwPagedSQL_RN "
                    + "BETWEEN 11 AND 20",
                "age, salary DESC",
                true
            },
            {
                "five clauses populated, only the order-by moves [:L389-L390]",
                GroupedOrderedSelect,
                // GROUP BY and HAVING both survive inside the innermost derived table. This is the
                // widest input in the matrix and the one the clause recorder uses.
                "SELECT * FROM (SELECT * FROM (SELECT pfwPagedSQL_TblInnerInner.*,ROW_NUMBER() OVER "
                    + "(ORDER BY age) AS pfwPagedSQL_RN FROM (SELECT age, COUNT(*) FROM COMPANY "
                    + "WHERE age > 18 GROUP BY age HAVING COUNT(*) > 1) pfwPagedSQL_TblInnerInner) "
                    + "pfwPagedSQL_TblInner WHERE pfwPagedSQL_RN <= 20) pfwPagedSQL_TblOuter WHERE "
                    + "pfwPagedSQL_RN BETWEEN 11 AND 20",
                "age",
                true
            },
            {
                "the order-by body's own odd spacing survives verbatim [:L389]",
                OddlySpacedOrderedSelect,
                // TWO SPACES ON EACH SIDE OF THE COMMA, PRESERVED. The arm splices a clause BODY,
                // so nothing inside it is re-spaced. A whitespace-normalising assertion would pass
                // against a rewriter that tidied this, which is why nothing here is normalised.
                "SELECT * FROM (SELECT * FROM (SELECT pfwPagedSQL_TblInnerInner.*,ROW_NUMBER() OVER "
                    + "(ORDER BY a  ,  b) AS pfwPagedSQL_RN FROM (SELECT * FROM T) "
                    + "pfwPagedSQL_TblInnerInner) pfwPagedSQL_TblInner WHERE pfwPagedSQL_RN <= 20) "
                    + "pfwPagedSQL_TblOuter WHERE pfwPagedSQL_RN BETWEEN 11 AND 20",
                "a  ,  b",
                true
            },
            {
                "multi-block: the trailing order-by is the SECOND block's, so '' is substituted",
                UnionOrderedSelect,
                // NOT A DUPLICATE OF THE FIRST ROW. `hasorder()` addresses block one
                // [n_sql.sru:L24], the order-by here belongs to block two, so the else branch runs
                // AND the caller's own ORDER BY travels into the innermost derived table. Both the
                // substitution and an ORDER BY are therefore present, legitimately.
                "SELECT * FROM (SELECT * FROM (SELECT pfwPagedSQL_TblInnerInner.*,ROW_NUMBER() OVER "
                    + "(ORDER BY '') AS pfwPagedSQL_RN FROM (SELECT id FROM A UNION SELECT id FROM "
                    + "B ORDER BY id) pfwPagedSQL_TblInnerInner) pfwPagedSQL_TblInner WHERE "
                    + "pfwPagedSQL_RN <= 20) pfwPagedSQL_TblOuter WHERE pfwPagedSQL_RN BETWEEN 11 "
                    + "AND 20",
                "''",
                false
            },
        };

    /// <summary>The statement matrix, projected for theory consumption.</summary>
    public static TheoryData<string, string, string, string, bool> Statements
    {
        get
        {
            TheoryData<string, string, string, string, bool> matrix = [];

            foreach (StatementCase statementCase in StatementCases)
            {
                matrix.Add(
                    statementCase.Scenario,
                    statementCase.Sql,
                    statementCase.Expected,
                    statementCase.OrderByArgument,
                    statementCase.SingleBlock);
            }

            return matrix;
        }
    }

    /// <summary>
    /// THE PRINCIPAL ASSERTION: the whole emitted statement, as one exact string, compared
    /// ordinally and never normalised.
    /// </summary>
    /// <param name="scenario">What the row exercises, surfaced in failure output.</param>
    /// <param name="sql">The statement to rewrite.</param>
    /// <param name="expected">The generated statement, character for character.</param>
    /// <param name="orderByArgument">The window function's order-by argument for this row.</param>
    /// <param name="singleBlock">Whether the input is one select block.</param>
    /// <remarks>
    /// Seven rows, well past the three the parity criterion requires, spanning both substitution
    /// paths, both block shapes, a star and an enumerated select list, three different order-by
    /// bodies and four other populated clauses.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Statements))]
    public void EveryStatementIsGeneratedCharacterForCharacter(
        string scenario,
        string sql,
        string expected,
        string orderByArgument,
        bool singleBlock)
    {
        string actual = RewrittenPrincipal(sql);

        Assert.Equal(expected, actual, StringComparer.Ordinal);

        // The row's own selectors, re-asserted against the generated text so the matrix columns
        // cannot drift away from the statements they describe.
        Assert.Contains("(ORDER BY " + orderByArgument + ")", actual, StringComparison.Ordinal);
        Assert.Equal(singleBlock ? 1 : 2, ParsedModel(sql).GetSelectCount());
        Assert.False(string.IsNullOrWhiteSpace(scenario));
    }

    /// <summary>
    /// EXACTLY ONE SPACE separates the outer table's alias from the final <c>WHERE</c> - not two,
    /// and not zero.
    /// </summary>
    /// <param name="scenario">What the row exercises, surfaced in failure output.</param>
    /// <param name="sql">The statement to rewrite.</param>
    /// <param name="expected">The generated statement, used to pin the space in the expectation too.</param>
    /// <param name="orderByArgument">Carried by the shared matrix; unused by this assertion.</param>
    /// <param name="singleBlock">Carried by the shared matrix; unused by this assertion.</param>
    /// <remarks>
    /// <para>
    /// THE DETAIL A RE-TYPED EXPECTATION LOSES. <c>[:L394]</c> ends
    /// <c>... + ") pfwPagedSQL_TblOuter " + &amp;</c> with the space INSIDE the literal, and
    /// <c>[:L395]</c> begins <c>"WHERE ...</c> with none - <c>&amp;</c> is a line continuation and
    /// contributes no character of its own. Explained at length in the file header so a future
    /// reader does not "clean up" the spacing (C-K).
    /// </para>
    /// <para>
    /// Asserted in THREE directions rather than one: the single-spaced form present, the doubled
    /// form absent, the elided form absent. Presence alone would be satisfied by the doubled form
    /// too, because one space is a sub-string of two.
    /// </para>
    /// <para>
    /// And asserted against the EXPECTATION as well as the output, because a doubled space typed
    /// into an expectation would otherwise make the principal assertion fail for a reason nobody
    /// could see.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(Statements))]
    public void ExactlyOneSpaceSeparatesTheOuterAliasFromTheFinalWhere(
        string scenario,
        string sql,
        string expected,
        string orderByArgument,
        bool singleBlock)
    {
        string actual = RewrittenPrincipal(sql);

        foreach (string subject in new[] { actual, expected })
        {
            Assert.Contains(
                OraclePagingRewriter.OuterTableAlias + " WHERE ",
                subject,
                StringComparison.Ordinal);

            Assert.DoesNotContain(
                OraclePagingRewriter.OuterTableAlias + "  WHERE",
                subject,
                StringComparison.Ordinal);

            Assert.DoesNotContain(
                OraclePagingRewriter.OuterTableAlias + "WHERE",
                subject,
                StringComparison.Ordinal);
        }

        Assert.False(string.IsNullOrWhiteSpace(scenario));
        Assert.False(string.IsNullOrEmpty(orderByArgument));
        Assert.Equal(singleBlock, !sql.Contains("UNION", StringComparison.Ordinal));
    }

    /// <summary>
    /// THE THREE DERIVED TABLES NEST INNERMOST OUTWARD in the order
    /// <c>pfwPagedSQL_TblInnerInner</c>, then <c>pfwPagedSQL_TblInner</c>, then
    /// <c>pfwPagedSQL_TblOuter</c>.
    /// </summary>
    /// <param name="scenario">What the row exercises, surfaced in failure output.</param>
    /// <param name="sql">The statement to rewrite.</param>
    /// <param name="expected">Carried by the shared matrix; unused by this assertion.</param>
    /// <param name="orderByArgument">Carried by the shared matrix; unused by this assertion.</param>
    /// <param name="singleBlock">Carried by the shared matrix; unused by this assertion.</param>
    /// <remarks>
    /// <para>
    /// RELATIVE POSITION, NOT MERE PRESENCE. A transposed pair would still contain all three
    /// aliases and every containment assertion would still pass, so the aliases are located and
    /// their indices ordered.
    /// </para>
    /// <para>
    /// THE PREFIX HAZARD IS HANDLED EXPLICITLY. <c>pfwPagedSQL_TblInner</c> is a strict prefix of
    /// <c>pfwPagedSQL_TblInnerInner</c>, so a naive search for the middle alias finds the innermost
    /// one first and the ordering assertion would compare the wrong positions. The middle alias is
    /// therefore located by its ALIAS OCCURRENCE - space-delimited on both sides, which the longer
    /// name cannot satisfy at that position - and the innermost is located by its declaration site.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(Statements))]
    public void TheThreeDerivedTableAliasesNestInnermostOutward(
        string scenario,
        string sql,
        string expected,
        string orderByArgument,
        bool singleBlock)
    {
        string actual = RewrittenPrincipal(sql);

        // The innermost alias where it is DECLARED - `) pfwPagedSQL_TblInnerInner)` - which is the
        // occurrence that closes the caller's own statement and names it.
        int innerInner = actual.IndexOf(
            ") " + OraclePagingRewriter.InnerInnerTableAlias + ")",
            StringComparison.Ordinal);

        // The middle alias, space-delimited on BOTH sides so the longer name cannot match here.
        int inner = actual.IndexOf(
            " " + OraclePagingRewriter.InnerTableAlias + " ",
            StringComparison.Ordinal);

        int outer = actual.IndexOf(
            OraclePagingRewriter.OuterTableAlias,
            StringComparison.Ordinal);

        Assert.True(innerInner >= 0, $"{scenario}: innermost alias declaration not found");
        Assert.True(inner >= 0, $"{scenario}: middle alias not found space-delimited");
        Assert.True(outer >= 0, $"{scenario}: outer alias not found");

        Assert.True(innerInner < inner, $"{scenario}: innermost alias must precede the middle one");
        Assert.True(inner < outer, $"{scenario}: middle alias must precede the outer one");

        // ONE OCCURRENCE EACH for the middle and outer aliases; TWO for the innermost, which is
        // both referenced by the `.*` projection and declared as the alias [:L394].
        Assert.Equal(1, CountOccurrences(actual, " " + OraclePagingRewriter.InnerTableAlias + " "));
        Assert.Equal(1, CountOccurrences(actual, OraclePagingRewriter.OuterTableAlias));
        Assert.Equal(2, CountOccurrences(actual, OraclePagingRewriter.InnerInnerTableAlias));

        Assert.False(string.IsNullOrEmpty(expected));
        Assert.False(string.IsNullOrEmpty(orderByArgument));
        Assert.Equal(singleBlock, !sql.Contains("UNION", StringComparison.Ordinal));
    }

    /// <summary>
    /// The innermost alias is REFERENCED in the innermost select list as well as declared:
    /// <c>SELECT pfwPagedSQL_TblInnerInner.*,ROW_NUMBER()</c>, with NO space at the comma.
    /// </summary>
    /// <param name="scenario">What the row exercises, surfaced in failure output.</param>
    /// <param name="sql">The statement to rewrite.</param>
    /// <param name="expected">Carried by the shared matrix; unused by this assertion.</param>
    /// <param name="orderByArgument">Carried by the shared matrix; unused by this assertion.</param>
    /// <param name="singleBlock">Carried by the shared matrix; unused by this assertion.</param>
    /// <remarks>
    /// The reference must come BEFORE the declaration in the emitted text, which looks backwards
    /// and is correct: the middle level's select list names the derived table that the same level's
    /// <c>FROM</c> goes on to alias. Asserted as an ordering so a rewriter that emitted the
    /// projection unqualified - or qualified it with the wrong alias - fails here.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Statements))]
    public void TheInnermostSelectListReferencesTheInnermostAlias(
        string scenario,
        string sql,
        string expected,
        string orderByArgument,
        bool singleBlock)
    {
        string actual = RewrittenPrincipal(sql);

        string projection =
            "SELECT " + OraclePagingRewriter.InnerInnerTableAlias + ".*,ROW_NUMBER() OVER (ORDER BY ";

        Assert.Contains(projection, actual, StringComparison.Ordinal);

        int reference = actual.IndexOf(projection, StringComparison.Ordinal);
        int declaration = actual.IndexOf(
            ") " + OraclePagingRewriter.InnerInnerTableAlias + ")",
            StringComparison.Ordinal);

        Assert.True(
            reference < declaration,
            $"{scenario}: the qualified projection must precede the alias declaration");

        // NO SPACE AT THE COMMA, and no space inserted after `ROW_NUMBER()` beyond the single one
        // [:L394] carries. Both spellings a tidy-up would produce are asserted absent.
        Assert.DoesNotContain(".*, ROW_NUMBER()", actual, StringComparison.Ordinal);
        Assert.DoesNotContain(".* ,ROW_NUMBER()", actual, StringComparison.Ordinal);
        Assert.DoesNotContain("ROW_NUMBER()  OVER", actual, StringComparison.Ordinal);
        Assert.DoesNotContain("ROW_NUMBER()OVER", actual, StringComparison.Ordinal);
        Assert.DoesNotContain("OVER(ORDER BY", actual, StringComparison.Ordinal);

        Assert.False(string.IsNullOrEmpty(expected));
        Assert.False(string.IsNullOrEmpty(orderByArgument));
        Assert.Equal(singleBlock, !sql.Contains("UNION", StringComparison.Ordinal));
    }

    /// <summary>
    /// The whole statement, exact, across combinations that vary the STATEMENT, the PAGE SIZE and
    /// the PAGE INDEX all at once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY THIS EXISTS ALONGSIDE <see cref="Statements"/>. That matrix varies the statement while
    /// holding the arithmetic fixed, which is what isolates a whitespace regression from an
    /// arithmetic one - but it means every one of its rows shares a single page size and page index.
    /// The parity criterion is stated over the TRIPLE, so these four rows move all three components
    /// together and assert the whole emitted text for each.
    /// </para>
    /// <para>
    /// The four rows are chosen so that no two share a page size, a page index, a substitution path
    /// or a select-list shape. The <c>1</c> by <c>1</c> row is the degenerate extreme where both
    /// bounds collapse to <c>1</c>; the <c>25</c> by <c>4</c> row is far enough out that a dropped
    /// <c>(pageIndex - 1)</c> term is unmissable.
    /// </para>
    /// </remarks>
    public static TheoryData<string, long, long, string> PagedStatements =>
        new()
        {
            {
                FixtureSelect,
                10L,
                1L,
                // PAGE ONE: the cap is one page and the lower bound is 1 [:L394-L395].
                "SELECT * FROM (SELECT * FROM (SELECT pfwPagedSQL_TblInnerInner.*,ROW_NUMBER() OVER "
                    + "(ORDER BY '') AS pfwPagedSQL_RN FROM (SELECT * FROM COMPANY) "
                    + "pfwPagedSQL_TblInnerInner) pfwPagedSQL_TblInner WHERE pfwPagedSQL_RN <= 10) "
                    + "pfwPagedSQL_TblOuter WHERE pfwPagedSQL_RN BETWEEN 1 AND 10"
            },
            {
                MixedCaseOrderedSelect,
                25L,
                4L,
                // A FAR PAGE with a lifted order-by: 25 * 4 = 100 and 25 * 3 + 1 = 76.
                "SELECT * FROM (SELECT * FROM (SELECT pfwPagedSQL_TblInnerInner.*,ROW_NUMBER() OVER "
                    + "(ORDER BY NAME) AS pfwPagedSQL_RN FROM (SELECT id, name FROM COMPANY) "
                    + "pfwPagedSQL_TblInnerInner) pfwPagedSQL_TblInner WHERE pfwPagedSQL_RN <= 100) "
                    + "pfwPagedSQL_TblOuter WHERE pfwPagedSQL_RN BETWEEN 76 AND 100"
            },
            {
                FilteredOrderedSelect,
                5L,
                3L,
                // A THIRD ARITHMETIC SHAPE with a WHERE and a directional order-by: 5 * 3 = 15 and
                // 5 * 2 + 1 = 11.
                "SELECT * FROM (SELECT * FROM (SELECT pfwPagedSQL_TblInnerInner.*,ROW_NUMBER() OVER "
                    + "(ORDER BY age, salary DESC) AS pfwPagedSQL_RN FROM (SELECT id, name, age "
                    + "FROM COMPANY WHERE age > 18) pfwPagedSQL_TblInnerInner) pfwPagedSQL_TblInner "
                    + "WHERE pfwPagedSQL_RN <= 15) pfwPagedSQL_TblOuter WHERE pfwPagedSQL_RN "
                    + "BETWEEN 11 AND 15"
            },
            {
                EnumeratedSelect,
                1L,
                1L,
                // THE DEGENERATE EXTREME: a single-row page one, where the cap and both BETWEEN
                // operands are all 1.
                "SELECT * FROM (SELECT * FROM (SELECT pfwPagedSQL_TblInnerInner.*,ROW_NUMBER() OVER "
                    + "(ORDER BY '') AS pfwPagedSQL_RN FROM (SELECT id, name, age, address, salary, "
                    + "birth FROM COMPANY) pfwPagedSQL_TblInnerInner) pfwPagedSQL_TblInner WHERE "
                    + "pfwPagedSQL_RN <= 1) pfwPagedSQL_TblOuter WHERE pfwPagedSQL_RN BETWEEN 1 AND 1"
            },
        };

    /// <summary>
    /// The whole emitted statement is exact for every (statement, page size, page index) triple.
    /// </summary>
    /// <param name="sql">The statement to rewrite.</param>
    /// <param name="pageSize">Rows per page - <c>_nPageSize</c>.</param>
    /// <param name="pageIndex">The one-based page ordinal - <c>_nPageIndex</c>.</param>
    /// <param name="expected">The generated statement, character for character.</param>
    /// <remarks>
    /// Ordinal, and NOT normalised - the same discipline as every other comparison here. The single
    /// space before the final <c>WHERE</c> is re-asserted on this path too, because these
    /// expectations were typed independently of the other matrix's and could have acquired a
    /// different spacing.
    /// </remarks>
    [Theory]
    [MemberData(nameof(PagedStatements))]
    public void TheWholeStatementIsExactAcrossPageSizeAndPageIndex(
        string sql,
        long pageSize,
        long pageIndex,
        string expected)
    {
        string actual = Rewritten(new PagingRewriteRequest(sql, pageSize, pageIndex, false));

        Assert.Equal(expected, actual, StringComparer.Ordinal);

        Assert.Contains(
            OraclePagingRewriter.OuterTableAlias + " WHERE ",
            actual,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            OraclePagingRewriter.OuterTableAlias + "  WHERE",
            actual,
            StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------
    //  THE TWO ROW-NUMBER BOUNDS  [:L394-L395]
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The arithmetic matrix: page size and page index against the two bounds the oracle computes
    /// from them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both bounds are spelled as LITERAL EXPECTED DIGITS rather than as expressions over the two
    /// inputs. An expectation written as <c>pageSize * pageIndex</c> would restate the formula under
    /// test and would agree with any implementation that shared the same mistake; a literal cannot.
    /// </para>
    /// <para>
    /// <c>pageIndex = 1</c> is covered twice, and it is the row that matters most for the wrong
    /// reason: its lower bound is 1 whether or not the <c>(pageIndex - 1)</c> term at <c>[:L395]</c>
    /// exists, so it can only ever CONFIRM the degenerate case - which is exactly why the middle and
    /// far pages are here alongside it.
    /// </para>
    /// <para>
    /// <c>pageSize = 1</c> with <c>pageIndex = 7</c> is the sharpest row in the matrix: the two
    /// bounds coincide at 7, so a <c>BETWEEN</c> whose operands were transposed still reads
    /// correctly, and the far row at 1000 by 100 is what catches a transposition.
    /// </para>
    /// </remarks>
    public static TheoryData<long, long, string, string> Arithmetic =>
        new()
        {
            // pageSize, pageIndex, expected `<=` cap [:L394], expected BETWEEN low bound [:L395]
            { 10L, 1L, "10", "1" },      // PAGE ONE - the degenerate lower bound
            { 10L, 2L, "20", "11" },     // a middle page - where the one-based term becomes visible
            { 25L, 4L, "100", "76" },    // a later page with a different page size
            { 1L, 1L, "1", "1" },        // the smallest possible page, page one
            { 1L, 7L, "7", "7" },        // both bounds coincide - a transposition still reads right
            { 7L, 3L, "21", "15" },      // a page size that divides nothing evenly
            { 1000L, 100L, "100000", "99001" }, // far page - a transposition is unmissable here
        };

    /// <summary>
    /// The middle level caps with <c>&lt;= pageSize * pageIndex</c> and the outer level narrows with
    /// <c>BETWEEN pageSize * (pageIndex - 1) + 1 AND pageSize * pageIndex</c>.
    /// </summary>
    /// <param name="pageSize">Rows per page - <c>_nPageSize</c>.</param>
    /// <param name="pageIndex">The one-based page ordinal - <c>_nPageIndex</c>.</param>
    /// <param name="expectedCap">The digits expected after <c>&lt;= </c> at <c>[:L394]</c>.</param>
    /// <param name="expectedLowBound">The digits expected after <c>BETWEEN </c> at <c>[:L395]</c>.</param>
    /// <remarks>
    /// <para>
    /// THE CAP AND THE UPPER BETWEEN BOUND ARE THE SAME VALUE, computed twice by the oracle from
    /// operands neither line can have changed. That is asserted rather than assumed, because an
    /// implementation that recomputed the second from a mutated field would emit two different
    /// numbers and still produce syntactically valid SQL.
    /// </para>
    /// <para>
    /// Spacing is pinned here too: <c>&lt;=</c>, <c>BETWEEN</c> and <c>AND</c> are single-spaced on
    /// both sides at <c>[:L394-L395]</c>.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(Arithmetic))]
    public void TheMiddleLevelCapsAndTheOuterLevelNarrowsToTheOneBasedRange(
        long pageSize,
        long pageIndex,
        string expectedCap,
        string expectedLowBound)
    {
        string actual = Rewritten(new PagingRewriteRequest(FixtureSelect, pageSize, pageIndex, false));

        Assert.Contains(
            OraclePagingRewriter.RowNumberColumnAlias + " <= " + expectedCap + ")",
            actual,
            StringComparison.Ordinal);

        Assert.Contains(
            OraclePagingRewriter.RowNumberColumnAlias
                + " BETWEEN " + expectedLowBound + " AND " + expectedCap,
            actual,
            StringComparison.Ordinal);

        // The generated statement ends at the upper bound: no trailing ORDER BY, no OFFSET, no FETCH.
        Assert.EndsWith(
            " BETWEEN " + expectedLowBound + " AND " + expectedCap,
            actual,
            StringComparison.Ordinal);

        // The row-number column is named three times [:L394-L395]: defined by AS, capped, narrowed.
        Assert.Equal(3, CountOccurrences(actual, OraclePagingRewriter.RowNumberColumnAlias));
    }

    // ==========================================================================================
    //  PHASE 2 - THE ORACLE-ONLY EMPTY-ORDER-BY SUBSTITUTION  [:L392]
    // ==========================================================================================

    /// <summary>
    /// With no order-by on the addressed block, the window's <c>ORDER BY</c> argument is
    /// <c>''</c> - two apostrophe characters - and the other dialect's substitution appears nowhere.
    /// </summary>
    /// <param name="sql">A statement whose FIRST block carries no order-by.</param>
    /// <remarks>
    /// <para>
    /// THE FOLDER'S PARITY TRAP, ASSERTED IN BOTH DIRECTIONS. Both dialects substitute an expression
    /// here and both legacy comments read <c>原始结果集排序</c>, so the INTENT is shared while the
    /// TEXT is not: <c>[:L392]</c> substitutes two apostrophes, <c>[:L370]</c> and <c>[:L379]</c>
    /// substitute a parenthesised scalar subquery. A constant shared between the two rewriters would
    /// be a defect that broke parity while every assertion still looked plausible, so the presence
    /// of one and the ABSENCE of the other are both asserted.
    /// </para>
    /// <para>
    /// The two apostrophes are also counted, so a substitution of a single apostrophe - or of an
    /// empty string, which would leave <c>OVER (ORDER BY )</c> - fails distinctly rather than
    /// falling out as a whole-statement mismatch with no diagnosis.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(FixtureSelect)]
    [InlineData(EnumeratedSelect)]
    [InlineData(UnionOrderedSelect)]
    public void AnAbsentOrderByBecomesTwoApostrophesAndNeverTheOtherDialectsSubstitution(string sql)
    {
        string actual = RewrittenPrincipal(sql);

        Assert.Contains("(ORDER BY '') AS ", actual, StringComparison.Ordinal);
        Assert.Equal("''", OraclePagingRewriter.AbsentOrderBySubstitution);
        Assert.Equal(2, CountOccurrences(actual, "'"));

        // NOT the other dialect's text, in any casing or partial form that a shared constant would
        // have produced.
        Assert.DoesNotContain(
            OtherDialectAbsentOrderBySubstitution,
            actual,
            StringComparison.Ordinal);

        Assert.DoesNotContain("SELECT 0", actual, StringComparison.Ordinal);
        Assert.DoesNotContain("(ORDER BY )", actual, StringComparison.Ordinal);
    }

    /// <summary>
    /// An existing order-by is CAPTURED into the window function and REMOVED from the inner
    /// statement, so the ordering appears exactly once in the generated text.
    /// </summary>
    /// <param name="sql">A single-block statement carrying an order-by.</param>
    /// <param name="orderByBody">The clause body <c>[:L389]</c> returns, without its introducer.</param>
    /// <remarks>
    /// <para>
    /// TWO HALVES OF ONE BEHAVIOUR, and asserting either alone would miss a real defect. The capture
    /// alone would pass against a rewriter that forgot the strip at <c>[:L390]</c>, leaving an
    /// <c>ORDER BY</c> inside a derived table where it is meaningless. The strip alone would pass
    /// against a rewriter that discarded the clause instead of lifting it, silently losing the
    /// caller's ordering.
    /// </para>
    /// <para>
    /// SCOPED TO SINGLE-BLOCK INPUTS. On a multi-block statement the trailing order-by belongs to a
    /// block the no-argument <c>hasorder()</c> does not address, so it legitimately survives inside
    /// the inner statement AND the substitution is used - asserted separately by
    /// <see cref="AMultiBlockTrailingOrderBySurvivesInsideTheInnermostTable"/>.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(MixedCaseOrderedSelect, "NAME")]
    [InlineData(FilteredOrderedSelect, "age, salary DESC")]
    [InlineData(GroupedOrderedSelect, "age")]
    [InlineData(OddlySpacedOrderedSelect, "a  ,  b")]
    public void AnExistingOrderByAppearsExactlyOnceInTheGeneratedStatement(
        string sql,
        string orderByBody)
    {
        string actual = RewrittenPrincipal(sql);

        // CAPTURED: the body sits inside the window function, verbatim.
        Assert.Contains("(ORDER BY " + orderByBody + ") AS ", actual, StringComparison.Ordinal);

        // REMOVED: `ORDER BY` introduces the clause exactly once in the whole statement, and that
        // one occurrence is the window function's. A surviving inner clause would make it two.
        Assert.Equal(1, CountOccurrences(actual, "ORDER BY "));

        // And the substitution was NOT used, because the branch was not taken [:L388 vs :L392].
        Assert.DoesNotContain(
            OraclePagingRewriter.AbsentOrderBySubstitution,
            actual,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The strip is performed with <c>SQL_MS_REPLACE</c> and empty text - <c>[:L390]</c> - so the
    /// clause is GONE from the model rather than merely emptied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ASSERTED ON THE MODEL, NOT ONLY ON THE TEXT. An emptied-but-present clause and an absent
    /// clause both render as no <c>ORDER BY</c> at all, so the emitted statement cannot tell them
    /// apart; <c>hasorder()</c> can, and it is the read the arm's own branch at <c>[:L388]</c>
    /// depends on. A rewriter that left an empty clause behind would produce the right text today
    /// and take the wrong branch if the model were ever reused.
    /// </para>
    /// <para>
    /// The modify style is consumed as <c>Enums.SQL_MS_REPLACE</c> - the managed form of
    /// <c>Constant Long SQL_MS_REPLACE = 1</c> at <c>ws_objects/pfw.shared.pbl.src/enums.sru:L718</c>
    /// - and its value is pinned here so a renumbering of the enumeration cannot silently turn the
    /// strip into an append.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheStripLeavesNoOrderClauseOnTheModelAtAll()
    {
        Assert.Equal(1L, Enums.SQL_MS_REPLACE);

        SelectStatementModel model = ParsedModel(FilteredOrderedSelect);

        Assert.True(model.HasOrder());
        Assert.Equal("age, salary DESC", model.GetOrder(), StringComparer.Ordinal);

        PagingRewriteResult result = new OraclePagingRewriter().Rewrite(
            new PagingRewriteRequest(FilteredOrderedSelect, PrincipalPageSize, PrincipalPageIndex, false),
            model);

        Assert.True(result.IsOk);
        Assert.False(model.HasOrder());
        Assert.Equal(string.Empty, model.GetOrder(), StringComparer.Ordinal);

        // The rest of the statement is untouched by the strip: the WHERE is still there and the
        // model renders without any ORDER BY.
        Assert.Equal(
            "SELECT id, name, age FROM COMPANY WHERE age > 18",
            model.GetSql(),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// On a multi-block statement the trailing order-by belongs to a block the arm does not address,
    /// so the substitution IS used and the caller's own ordering survives inside the innermost
    /// derived table.
    /// </summary>
    /// <remarks>
    /// The one place in this suite where the substitution and an <c>ORDER BY</c> coexist. Recorded
    /// as its own assertion, with its own reasoning, rather than folded into the exactly-once
    /// assertion it would otherwise contradict - see the remarks on <see cref="UnionOrderedSelect"/>
    /// for why <c>hasorder()</c> answers <see langword="false"/> here.
    /// </remarks>
    [Fact]
    public void AMultiBlockTrailingOrderBySurvivesInsideTheInnermostTable()
    {
        SelectStatementModel model = ParsedModel(UnionOrderedSelect);

        Assert.Equal(2, model.GetSelectCount());
        Assert.False(model.HasOrder());

        string actual = RewrittenPrincipal(UnionOrderedSelect);

        // The substitution was used, because the addressed block has no order-by.
        Assert.Contains("(ORDER BY '') AS ", actual, StringComparison.Ordinal);

        // And the caller's own trailing clause is still inside the innermost derived table, so
        // `ORDER BY ` appears twice here - once in the window, once as the caller wrote it.
        Assert.Contains(
            "UNION SELECT id FROM B ORDER BY id) " + OraclePagingRewriter.InnerInnerTableAlias,
            actual,
            StringComparison.Ordinal);

        Assert.Equal(2, CountOccurrences(actual, "ORDER BY "));
    }

    /// <summary>
    /// The matrix genuinely covers both substitution paths and both block shapes, so no path can be
    /// dropped without a test disappearing.
    /// </summary>
    /// <remarks>
    /// Reads the SELECTOR columns back off the same rows the theories run, which is what makes this
    /// a coverage assertion rather than a restatement: it cannot pass against a matrix the theories
    /// do not actually execute.
    /// </remarks>
    [Fact]
    public void TheMatrixCoversBothSubstitutionPathsAndBothBlockShapes()
    {
        StatementCaseCollection cases = StatementCases;

        Assert.True(cases.Count >= 3, "the parity criterion requires at least three rows");

        Assert.Contains(
            cases,
            row => string.Equals(
                row.OrderByArgument,
                OraclePagingRewriter.AbsentOrderBySubstitution,
                StringComparison.Ordinal));

        Assert.Contains(
            cases,
            row => !string.Equals(
                row.OrderByArgument,
                OraclePagingRewriter.AbsentOrderBySubstitution,
                StringComparison.Ordinal));

        Assert.Contains(cases, row => row.SingleBlock);
        Assert.Contains(cases, row => !row.SingleBlock);

        // Every row is distinct in BOTH its input and its expectation, so no row is a silent copy of
        // another and the count is not inflated.
        Assert.Distinct(cases.Select(row => row.Sql), StringComparer.Ordinal);
        Assert.Distinct(cases.Select(row => row.Expected), StringComparer.Ordinal);
    }

    // ==========================================================================================
    //  PHASE 3 - THE STRUCTURAL DIFFERENCES FROM THE OTHER DIALECT, ASSERTED AS ABSENCES
    //
    //  These negative assertions are what stop a future refactor from "unifying" the two
    //  rewriters. The other arm has four shapes selected by two flags [:L323, :L343, :L366]; this
    //  one reads neither flag and has one shape. That is a transcribed fact about [:L386-L395] -
    //  `_bPageNative` and `_sPagedUniqueIndexColumns` appear nowhere between those lines - and not
    //  a design choice, so it is pinned rather than trusted.
    // ==========================================================================================

    /// <summary>
    /// NO PAGE-NATIVE VARIANT: the flag makes no difference whatever to Oracle output.
    /// </summary>
    /// <param name="scenario">What the row exercises, surfaced in failure output.</param>
    /// <param name="sql">The statement to rewrite.</param>
    /// <param name="expected">The generated statement - both flag settings must equal it.</param>
    /// <param name="orderByArgument">Carried by the shared matrix; unused by this assertion.</param>
    /// <param name="singleBlock">Carried by the shared matrix; unused by this assertion.</param>
    /// <remarks>
    /// The other arm branches on this flag twice, at <c>[:L343]</c> and <c>[:L366]</c>, and its four
    /// shapes differ materially - one emits <c>OFFSET ... FETCH NEXT</c>, another emits
    /// <c>TOP</c> plus a row number. Asserting the two settings produce IDENTICAL text, and that
    /// both equal the matrix expectation, is stronger than asserting each separately: it pins the
    /// invariance as well as the value.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Statements))]
    public void ThePageNativeFlagMakesNoDifferenceToOracleOutput(
        string scenario,
        string sql,
        string expected,
        string orderByArgument,
        bool singleBlock)
    {
        string withFlag =
            Rewritten(new PagingRewriteRequest(sql, PrincipalPageSize, PrincipalPageIndex, true));

        string withoutFlag =
            Rewritten(new PagingRewriteRequest(sql, PrincipalPageSize, PrincipalPageIndex, false));

        Assert.Equal(withoutFlag, withFlag, StringComparer.Ordinal);
        Assert.Equal(expected, withFlag, StringComparer.Ordinal);

        // NO ENGINE-NATIVE PAGING CONSTRUCT ANYWHERE, under either setting. The other arm's
        // constructs [:L346, :L355, :L372, :L381] have no counterpart in this dialect.
        Assert.DoesNotContain("OFFSET", withFlag, StringComparison.Ordinal);
        Assert.DoesNotContain("FETCH NEXT", withFlag, StringComparison.Ordinal);
        Assert.DoesNotContain("ROWS ONLY", withFlag, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT TOP", withFlag, StringComparison.Ordinal);

        Assert.False(string.IsNullOrWhiteSpace(scenario));
        Assert.False(string.IsNullOrEmpty(orderByArgument));
        Assert.Equal(singleBlock, !sql.Contains("UNION", StringComparison.Ordinal));
    }

    /// <summary>
    /// NO UNIQUE-INDEX VARIANT: supplying unique-index columns makes no difference to Oracle output.
    /// </summary>
    /// <param name="scenario">What the row exercises, surfaced in failure output.</param>
    /// <param name="sql">The statement to rewrite.</param>
    /// <param name="expected">The generated statement - both column settings must equal it.</param>
    /// <param name="orderByArgument">Carried by the shared matrix; unused by this assertion.</param>
    /// <param name="singleBlock">Carried by the shared matrix; unused by this assertion.</param>
    /// <remarks>
    /// <para>
    /// The other arm's whole first branch exists because of this collection - <c>[:L323]</c> tests
    /// it and <c>[:L331-L336]</c> splices its members into three separate fragments - and this arm
    /// never reads it. A real fixture column is supplied so the request is well formed and the
    /// collection's being ignored is the arm's behaviour rather than a consequence of an unusable
    /// value.
    /// </para>
    /// <para>
    /// A SECOND, LARGER LIST IS ALSO PASSED, because a single-element list cannot detect an arm that
    /// read only the first member. Both must produce the same text as no list at all.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(Statements))]
    public void TheUniqueIndexColumnsMakeNoDifferenceToOracleOutput(
        string scenario,
        string sql,
        string expected,
        string orderByArgument,
        bool singleBlock)
    {
        string withoutColumns =
            Rewritten(new PagingRewriteRequest(sql, PrincipalPageSize, PrincipalPageIndex, false));

        string withOneColumn = Rewritten(new PagingRewriteRequest(
            sql,
            PrincipalPageSize,
            PrincipalPageIndex,
            false,
            [KeyColumn]));

        string withTwoColumns = Rewritten(new PagingRewriteRequest(
            sql,
            PrincipalPageSize,
            PrincipalPageIndex,
            false,
            [DwSqliteFixture.UpdateTableName + "." + KeyColumn, DwSqliteFixture.UpdateTableName + ".age"]));

        Assert.Equal(expected, withoutColumns, StringComparer.Ordinal);
        Assert.Equal(expected, withOneColumn, StringComparer.Ordinal);
        Assert.Equal(expected, withTwoColumns, StringComparer.Ordinal);

        Assert.False(string.IsNullOrWhiteSpace(scenario));
        Assert.False(string.IsNullOrEmpty(orderByArgument));
        Assert.Equal(singleBlock, !sql.Contains("UNION", StringComparison.Ordinal));
    }

    /// <summary>
    /// The other dialect's two derived-table aliases appear NOWHERE in Oracle output.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WRITTEN WITH DELIMITERS, WHICH IS WHAT MAKES THE ASSERTION MEANINGFUL RATHER THAN
    /// SELF-DEFEATING. <c>pfwPagedSQL_Tbl</c> is a strict prefix of all three Oracle aliases, so a
    /// bare sub-string search for it matches correct output three times and the naive absence
    /// assertion would fail against a perfectly good statement. It is therefore searched with its
    /// trailing delimiter - the character that follows it wherever the other arm emits it,
    /// <c>[:L356]</c> and <c>[:L382]</c> - which no Oracle alias can satisfy.
    /// </para>
    /// <para>
    /// <c>pfwPagedSQL_OutterTbl</c> needs no such care: its doubled <c>t</c> and reversed word order
    /// make it a prefix of nothing here. Its misspelling is the legacy's and stays the legacy's
    /// (C-B).
    /// </para>
    /// </remarks>
    [Fact]
    public void TheOtherDialectsSentinelAliasesNeverAppear()
    {
        foreach (StatementCase statementCase in StatementCases)
        {
            string actual = RewrittenPrincipal(statementCase.Sql);

            Assert.DoesNotContain(OtherDialectJoinAlias, actual, StringComparison.Ordinal);

            // Delimited on both sides, mirroring the two sites the other arm emits it at.
            Assert.DoesNotContain(
                ") " + OtherDialectRowNumberTableAlias + " ",
                actual,
                StringComparison.Ordinal);

            // And the three Oracle aliases ARE present, so the absence above is a statement about
            // this arm's output rather than about an empty string.
            Assert.Contains(
                OraclePagingRewriter.InnerInnerTableAlias,
                actual,
                StringComparison.Ordinal);

            Assert.Contains(
                " " + OraclePagingRewriter.InnerTableAlias + " ",
                actual,
                StringComparison.Ordinal);

            Assert.Contains(OraclePagingRewriter.OuterTableAlias, actual, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// NO COLUMN CAPTURE AND NO RESTORE STEP: across the whole matrix the arm mutates the order
    /// clause AND NOTHING ELSE, exactly once, and never puts back what it stripped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the assertion the clause recorder exists for. Three of the other dialect's four arms
    /// read the select list and write it back - <c>[:L339, :L345, :L348]</c>,
    /// <c>[:L355, :L358]</c>, <c>[:L381]</c> - and one restores the order-by it saved
    /// <c>[:L360]</c>. This arm does neither: <c>getcolumn</c>, <c>modifycolumn</c>,
    /// <c>modifytable</c> and every restore are absent from <c>[:L386-L395]</c>, and the model is
    /// handed back to the caller carrying the strip.
    /// </para>
    /// <para>
    /// THE ROWS SPLIT INTO THE TWO BRANCHES AND ARE ASSERTED DIFFERENTLY, because the two branches
    /// have different observable content. Where the addressed block HAD an order-by the strip is
    /// visible and the recorder must report exactly one mutation, the order one. Where it had none,
    /// nothing observable happened at all - see the file header for why "no call was made" and "a
    /// no-op call was made" are indistinguishable there, and why "no clause changed" is the honest
    /// assertion.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheArmMutatesTheOrderClauseOnceAndNoOtherClauseEver()
    {
        foreach (StatementCase statementCase in StatementCases)
        {
            SelectStatementModel model = ParsedModel(statementCase.Sql);

            ClauseSnapshot before = ClauseMutationRecorder.Capture(model);

            PagingRewriteResult result = new OraclePagingRewriter().Rewrite(
                new PagingRewriteRequest(
                    statementCase.Sql,
                    PrincipalPageSize,
                    PrincipalPageIndex,
                    false),
                model);

            Assert.True(result.IsOk, statementCase.Scenario);

            ClauseSnapshot after = ClauseMutationRecorder.Capture(model);
            IReadOnlyList<string> mutations = ClauseMutationRecorder.Diff(before, after);

            if (before.OrderPresent)
            {
                // EXACTLY ONE MUTATION AND IT IS THE ORDER CLAUSE [:L390]. Not the select list, not
                // the from clause, not the where, not the group by, not the having.
                Assert.Equal(
                    [ClauseMutationRecorder.OrderMutation],
                    mutations);

                // A STRIP, NOT A REWRITE: the clause is gone rather than replaced with other text.
                Assert.False(after.OrderPresent, statementCase.Scenario);
                Assert.Equal(string.Empty, after.Order, StringComparer.Ordinal);
            }
            else
            {
                // NOTHING OBSERVABLE CHANGED. The else branch at [:L392] assigns a local and calls
                // no modifier.
                Assert.Empty(mutations);
                Assert.Equal(before.Rendered, after.Rendered, StringComparer.Ordinal);
            }

            // NEVER A COLUMN MUTATION, on either branch - the property that stops this arm from
            // being "unified" with the other one's capture-and-restore shape.
            Assert.DoesNotContain(ClauseMutationRecorder.ColumnMutation, mutations);
            Assert.Equal(before.Column, after.Column, StringComparer.Ordinal);
            Assert.Equal(before.Table, after.Table, StringComparer.Ordinal);
            Assert.Equal(before.Where, after.Where, StringComparer.Ordinal);
            Assert.Equal(before.Group, after.Group, StringComparer.Ordinal);
            Assert.Equal(before.Having, after.Having, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// THE RECORDER'S OWN SELF-TEST: it really does detect a select-list mutation and an order-by
    /// restore when one happens.
    /// </summary>
    /// <remarks>
    /// Without this, "no column mutation was recorded" would be satisfied by a recorder that
    /// records nothing at all - the classic way a negative assertion passes for the wrong reason.
    /// Both shapes the other dialect performs are driven through the recorder here: a select-list
    /// replacement <c>[:L345, :L355]</c> and an order-by restore <c>[:L360]</c>.
    /// </remarks>
    [Fact]
    public void TheRecorderDetectsAClauseMutationWhenOneReallyHappens()
    {
        SelectStatementModel model = ParsedModel(FilteredOrderedSelect);

        ClauseSnapshot before = ClauseMutationRecorder.Capture(model);

        // The other dialect's select-list narrowing, performed by hand so the recorder has a real
        // mutation to find. Nothing in this suite asks the Oracle arm to do this.
        Assert.True(model.ModifyColumn(Enums.SQL_MS_REPLACE, KeyColumn));

        IReadOnlyList<string> columnOnly =
            ClauseMutationRecorder.Diff(before, ClauseMutationRecorder.Capture(model));

        Assert.Equal([ClauseMutationRecorder.ColumnMutation], columnOnly);

        // Now the strip and then a RESTORE, which is the step [:L360] performs and this arm does
        // not. The recorder reports the order clause on both transitions.
        ClauseSnapshot beforeStrip = ClauseMutationRecorder.Capture(model);

        Assert.True(model.ModifyOrder(Enums.SQL_MS_REPLACE, string.Empty));

        ClauseSnapshot afterStrip = ClauseMutationRecorder.Capture(model);

        Assert.Equal(
            [ClauseMutationRecorder.OrderMutation],
            ClauseMutationRecorder.Diff(beforeStrip, afterStrip));

        Assert.True(model.ModifyOrder(Enums.SQL_MS_REPLACE, beforeStrip.Order));

        Assert.Equal(
            [ClauseMutationRecorder.OrderMutation],
            ClauseMutationRecorder.Diff(afterStrip, ClauseMutationRecorder.Capture(model)));

        // A snapshot compared with itself yields nothing, so an empty diff genuinely means "no
        // change" rather than "the comparison is broken".
        Assert.Empty(ClauseMutationRecorder.Diff(before, before));
    }

    /// <summary>
    /// THE FINAL STATEMENT IS THE CONCATENATION ITSELF, NOT A RE-READ OF THE MODEL.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The asymmetry with the other dialect is real and worth pinning. Its unique-index arms finish
    /// with <c>sql = sqlParser.GetSQL()</c> at <c>[:L364]</c>, because they mutated the model into
    /// its final shape; this arm builds its result by concatenation at <c>[:L394-L395]</c> and never
    /// re-reads. A re-read here would DISCARD the whole triple nest and return the caller's
    /// stripped input instead - a change that still returns valid SQL and would pass any assertion
    /// that only checked for success.
    /// </para>
    /// <para>
    /// Asserted in three ways: the two texts differ, the model's own render is a strict SUB-STRING
    /// of the returned statement (it is the innermost derived table), and the returned statement is
    /// strictly longer.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheReturnedStatementIsTheConcatenationAndNotAReReadOfTheModel()
    {
        foreach (StatementCase statementCase in StatementCases)
        {
            SelectStatementModel model = ParsedModel(statementCase.Sql);

            PagingRewriteResult result = new OraclePagingRewriter().Rewrite(
                new PagingRewriteRequest(
                    statementCase.Sql,
                    PrincipalPageSize,
                    PrincipalPageIndex,
                    false),
                model);

            Assert.True(result.IsOk, statementCase.Scenario);

            string modelRender = model.GetSql();

            Assert.NotEqual(modelRender, result.RewrittenSql);
            Assert.Contains(modelRender, result.RewrittenSql, StringComparison.Ordinal);
            Assert.True(
                result.RewrittenSql.Length > modelRender.Length,
                statementCase.Scenario);

            // And it equals the row's expectation, so "not a re-read" is not satisfied by returning
            // something else wrong.
            Assert.Equal(statementCase.Expected, result.RewrittenSql, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// The arm's own declared metadata: the Oracle discriminator, and that it does NOT consume the
    /// unique-index columns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ConsumesPagedUniqueIndexColumns</c> being <see langword="false"/> is not cosmetic - the
    /// dispatcher reads it to decide whether to validate caller-supplied identifiers before handing
    /// them on. Because this arm splices none, no validation runs and a malformed identifier reaches
    /// the arm and is ignored, exactly as the legacy ignores it (C-B). Pinning the flag pins that
    /// whole outcome.
    /// </para>
    /// <para>
    /// The discriminator is consumed from the generated contract as <c>DatabaseType.DbtOracle</c> -
    /// protoc's rendering of <c>constant long DBT_ORACLE = 1</c> at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L61</c> - and its numeric value is
    /// asserted, because that value is what travels on the wire.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheArmDeclaresTheOracleDiscriminatorAndIgnoresTheUniqueIndexColumns()
    {
        OraclePagingRewriter arm = new();

        Assert.Equal(DatabaseType.DbtOracle, arm.Dialect);
        Assert.Equal(1, (int)DatabaseType.DbtOracle);
        Assert.False(arm.ConsumesPagedUniqueIndexColumns);
        Assert.True(PagingRewriteDispatcher.IsDialectImplemented(DatabaseType.DbtOracle));

        // A malformed identifier is accepted and ignored rather than refused, because the dispatcher
        // skips validation for an arm that splices nothing.
        string actual = Rewritten(new PagingRewriteRequest(
            FixtureSelect,
            PrincipalPageSize,
            PrincipalPageIndex,
            false,
            ["id; DROP TABLE COMPANY"]));

        Assert.DoesNotContain("DROP TABLE", actual, StringComparison.Ordinal);
        Assert.Equal(StatementCases[0].Expected, actual, StringComparer.Ordinal);
    }

    /// <summary>
    /// NO TEST IN THIS SUITE DISPATCHES ON ANY DIALECT BUT ORACLE.
    /// </summary>
    /// <remarks>
    /// The registry has to contain an occupant for the other slot, and this pins that the occupant
    /// is never actually exercised - so a reader can trust that every generated statement asserted
    /// anywhere above came out of the real Oracle arm. Verified by selecting on both discriminators
    /// and confirming the two are distinct types, so the selection is meaningful rather than
    /// vacuous.
    /// </remarks>
    [Fact]
    public void TheRegistryIsOnlyEverDispatchedOnForOracle()
    {
        IPagingRewriter? selected =
            PagingRewriteDispatcher.SelectRewriter(DatabaseType.DbtOracle, Registry);

        _ = Assert.IsType<OraclePagingRewriter>(selected);

        IPagingRewriter? other =
            PagingRewriteDispatcher.SelectRewriter(DatabaseType.DbtMssql, Registry);

        _ = Assert.IsType<OtherDialectRewriter>(other);
    }

    // ==========================================================================================
    //  PHASE 4 - THE TWO SHARED GUARDS, OBSERVED ON THE ORACLE PATH  [:L307-L317]
    //
    //  Both guards live upstream of the choose case, so they are not the arm's code - but they ARE
    //  reached by every Oracle request, and a caller cannot tell the difference. Asserting them on
    //  this path is therefore asserting the Oracle contract, not duplicating the sibling suite:
    //  the two dialects must answer these identically, and only a per-dialect assertion proves it.
    // ==========================================================================================

    /// <summary>
    /// Non-positive paging settings on the Oracle path: <c>E_INVALID_ARGUMENT</c> with the legacy
    /// diagnostic, and no statement.
    /// </summary>
    /// <param name="pageSize">The page size to submit.</param>
    /// <param name="pageIndex">The page index to submit.</param>
    /// <remarks>
    /// <para>
    /// <c>[:L307-L310]</c> is <c>if _nPageSize &lt;= 0 or _nPageIndex &lt;= 0</c> - an OR, so either
    /// operand alone trips it, and both rows for a single offending operand are here alongside the
    /// row where both offend. Zero and a negative are covered separately because the legacy test is
    /// <c>&lt;= 0</c> rather than <c>= 0</c>.
    /// </para>
    /// <para>
    /// The code is <c>E_INVALID_ARGUMENT</c> at -3 <c>[retcode.sru:L46]</c>. It is asserted through
    /// <c>RetCode.E_INVALID_ARGUMENT</c> AND against the literal -3, because the constant and the
    /// value are two separate things a port can get wrong: consuming the wrong constant, or
    /// renumbering the catalogue.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(0L, 1L)]
    [InlineData(1L, 0L)]
    [InlineData(0L, 0L)]
    [InlineData(-1L, 5L)]
    [InlineData(5L, -1L)]
    [InlineData(-10L, -10L)]
    public void NonPositivePagingSettingsAreRefusedWithTheLegacyCodeAndText(
        long pageSize,
        long pageIndex)
    {
        PagingRewriteRequest request = new(FixtureSelect, pageSize, pageIndex, false);

        PagingRewriteResult result =
            PagingRewriteDispatcher.Rewrite(in request, DatabaseType.DbtOracle, Registry);

        Assert.False(result.IsOk);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, result.ReturnCode);
        Assert.Equal(-3L, result.ReturnCode);

        // BYTE EXACT, INCLUDING THE TRAILING EXCLAMATION MARK [:L308].
        Assert.Equal("无效的分页设置!", result.ErrorText, StringComparer.Ordinal);

        // AND NO STATEMENT IS RENDERED. The guard runs before the parser is even created [:L312],
        // so there is nothing to return and nothing partially built.
        Assert.Equal(string.Empty, result.RewrittenSql, StringComparer.Ordinal);
    }

    /// <summary>
    /// An unparseable statement on the Oracle path: <c>E_INTERNAL_ERROR</c> with the legacy
    /// diagnostic, and no statement.
    /// </summary>
    /// <param name="sql">The statement the parser refuses.</param>
    /// <remarks>
    /// <para>
    /// <c>[:L314-L317]</c> is <c>if Not sqlParser.Parse(origSql)</c>. The code is
    /// <c>E_INTERNAL_ERROR</c> at -27 <c>[retcode.sru:L70]</c> and NOT <c>E_INVALID_SQL</c>, even
    /// though it is the input that is malformed - the oracle's choice, reproduced rather than
    /// improved (C-B).
    /// </para>
    /// <para>
    /// The inputs span every rejection the parser documents that a caller can plausibly submit: a
    /// null or blank statement, a non-<c>SELECT</c> statement, unbalanced parentheses, an
    /// unterminated literal, a fully parenthesised block whose <c>SELECT</c> never appears at depth
    /// zero, and two statements in one string. <see langword="null"/> is included because
    /// <c>PagingRewriteRequest</c> normalises it to the empty string, so the guard - not a
    /// dereference - is what answers.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("UPDATE COMPANY SET age = 1")]
    [InlineData("SELECT * FROM (COMPANY")]
    [InlineData("SELECT * FROM COMPANY WHERE name = 'x")]
    [InlineData("(SELECT * FROM COMPANY)")]
    [InlineData("SELECT * FROM A; SELECT * FROM B")]
    public void AnUnparseableStatementIsRefusedWithTheLegacyCodeAndText(string? sql)
    {
        PagingRewriteRequest request = new(sql, PrincipalPageSize, PrincipalPageIndex, false);

        PagingRewriteResult result =
            PagingRewriteDispatcher.Rewrite(in request, DatabaseType.DbtOracle, Registry);

        Assert.False(result.IsOk);
        Assert.Equal(RetCode.E_INTERNAL_ERROR, result.ReturnCode);
        Assert.Equal(-27L, result.ReturnCode);

        // BYTE EXACT, INCLUDING THE TRAILING EXCLAMATION MARK [:L315].
        Assert.Equal("SQL解析失败!", result.ErrorText, StringComparer.Ordinal);
        Assert.Equal(string.Empty, result.RewrittenSql, StringComparer.Ordinal);
    }

    /// <summary>
    /// The two diagnostics are byte exact down to the code point, and their terminator is the ASCII
    /// exclamation mark rather than its full-width counterpart.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS NOT PEDANTRY, IT IS THE ONE SUBSTITUTION A HUMAN TRANSCRIBER ACTUALLY MAKES. A
    /// Chinese input method emits <c>！</c> at U+FF01 by default, and a message re-typed rather than
    /// copied acquires it silently: the two render almost identically at small sizes, the string
    /// still looks correct in every review, and a characterization recording taken against the
    /// legacy would never match. The oracle's own bytes at <c>[:L308]</c> and <c>[:L315]</c> are the
    /// ASCII form at U+0021, verified with <c>cat -A</c>, so that is what is asserted.
    /// </para>
    /// <para>
    /// Lengths are pinned too, which catches a stray zero-width or combining character that no
    /// visual inspection could see.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheLegacyDiagnosticsAreByteExactDownToTheirTerminator()
    {
        Assert.Equal(
            "无效的分页设置!",
            PagingRewriteResult.InvalidPagingSettingText,
            StringComparer.Ordinal);

        Assert.Equal(
            "SQL解析失败!",
            PagingRewriteResult.ParseFailedText,
            StringComparer.Ordinal);

        foreach (string diagnostic in
            new[] { PagingRewriteResult.InvalidPagingSettingText, PagingRewriteResult.ParseFailedText })
        {
            Assert.Equal(8, diagnostic.Length);
            Assert.EndsWith("!", diagnostic, StringComparison.Ordinal);

            // U+0021, NOT U+FF01. The full-width form is the transcription hazard described above.
            Assert.Equal('\u0021', diagnostic[^1]);
            Assert.DoesNotContain('\uFF01', diagnostic);
        }

        // The unrecognised-dialect arm [:L397] carries an EMPTY diagnostic, which is transcribed
        // rather than filled in. Asserted here so the empty string reads as a decision.
        //
        // Written as an emptiness assertion rather than an equality against string.Empty because
        // both operands would then be compile-time constants, which xUnit2000 rejects - and because
        // "this diagnostic is empty" is the property being stated.
        Assert.Empty(PagingRewriteResult.DialectNotImplementedText);
    }

    /// <summary>
    /// The paging guard is tested BEFORE the parse, so a request that is both badly paged and
    /// unparseable reports the paging fault.
    /// </summary>
    /// <remarks>
    /// Guard ORDER is observable and is therefore contract: <c>[:L307]</c> precedes <c>[:L312]</c>,
    /// so the parser is never even created for a badly paged request. A port that parsed first would
    /// answer <c>E_INTERNAL_ERROR</c> here - a different code, a different diagnostic, and a
    /// different branch for the caller to take.
    /// </remarks>
    [Fact]
    public void ThePagingGuardIsTestedBeforeTheParse()
    {
        PagingRewriteRequest request = new("this is not a select statement at all", 0L, 0L, false);

        PagingRewriteResult result =
            PagingRewriteDispatcher.Rewrite(in request, DatabaseType.DbtOracle, Registry);

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, result.ReturnCode);
        Assert.Equal("无效的分页设置!", result.ErrorText, StringComparer.Ordinal);
    }

    /// <summary>
    /// A paging product too large to represent answers the SAME invalid-paging outcome rather than a
    /// new one, and no statement is rendered from a bound that would wrap.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A GUARD THE ORACLE DOES NOT HAVE, AND THE REASON IT IS NOT A WIDENING. PowerScript
    /// <c>long</c> is 32-bit while these fields are 64-bit, so the wrap point here is not the
    /// legacy's - there is no legacy behaviour at this boundary to be faithful to. An unchecked
    /// product would wrap NEGATIVE, and <c>BETWEEN &lt;negative&gt; AND &lt;negative&gt;</c> is
    /// valid SQL that silently matches no row, which is worse than either a correct page or a
    /// reported error. Reusing the existing outcome keeps the wire free of a code no legacy caller
    /// has an arm for.
    /// </para>
    /// <para>
    /// The boundary is asserted on BOTH sides: the largest representable page size at page one
    /// succeeds, and the same size at page two is refused. And the arm reached directly - bypassing
    /// the dispatcher, which is to say from a test - throws rather than emitting a plausible
    /// statement built on a wrapped bound.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnUnrepresentablePagingProductReusesTheInvalidPagingOutcome()
    {
        PagingRewriteRequest overflowing = new(FixtureSelect, long.MaxValue, 2L, false);

        Assert.True(overflowing.HasValidPagingBounds);
        Assert.False(overflowing.HasRepresentablePagingProducts);

        PagingRewriteResult refused =
            PagingRewriteDispatcher.Rewrite(in overflowing, DatabaseType.DbtOracle, Registry);

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, refused.ReturnCode);
        Assert.Equal("无效的分页设置!", refused.ErrorText, StringComparer.Ordinal);
        Assert.Equal(string.Empty, refused.RewrittenSql, StringComparer.Ordinal);

        // THE OTHER SIDE OF THE BOUNDARY: representable, so it succeeds and the digits appear.
        PagingRewriteRequest representable = new(FixtureSelect, long.MaxValue, 1L, false);

        Assert.True(representable.HasRepresentablePagingProducts);

        string actual = Rewritten(in representable);

        Assert.Contains(" <= 9223372036854775807)", actual, StringComparison.Ordinal);
        Assert.Contains(" BETWEEN 1 AND 9223372036854775807", actual, StringComparison.Ordinal);
        Assert.DoesNotContain("-", actual, StringComparison.Ordinal);

        // The arm reached directly is `checked`, so it faults rather than wrapping.
        Assert.IsType<OverflowException>(
            Record.Exception(() => new OraclePagingRewriter().Rewrite(
                new PagingRewriteRequest(FixtureSelect, long.MaxValue, 3L, false),
                ParsedModel(FixtureSelect))));
    }

    /// <summary>
    /// The arm refuses a null statement reference with a precise exception rather than a
    /// dereference.
    /// </summary>
    /// <remarks>
    /// A MANAGED-ONLY CONTRACT WITH NO LEGACY ANALOGUE, and it changes no outcome the oracle can
    /// produce: PowerScript would fault on a null object reference, and the dispatcher never passes
    /// null. It converts an unavoidable <see cref="NullReferenceException"/> into an actionable one.
    /// Asserted so the guard cannot be removed on the reasoning that nothing reaches it.
    /// </remarks>
    [Fact]
    public void TheArmRefusesANullStatementReference()
    {
        _ = Assert.Throws<ArgumentNullException>(() => new OraclePagingRewriter().Rewrite(
            new PagingRewriteRequest(FixtureSelect, PrincipalPageSize, PrincipalPageIndex, false),
            null!));
    }

    // ==========================================================================================
    //  PURITY, DETERMINISM AND THE ORDINAL DISCIPLINE
    // ==========================================================================================

    /// <summary>
    /// The same request rewritten twice yields the IDENTICAL statement, and a second suite run
    /// cannot differ from the first.
    /// </summary>
    /// <param name="scenario">What the row exercises, surfaced in failure output.</param>
    /// <param name="sql">The statement to rewrite.</param>
    /// <param name="expected">The generated statement, character for character.</param>
    /// <param name="orderByArgument">Carried by the shared matrix; unused by this assertion.</param>
    /// <param name="singleBlock">Carried by the shared matrix; unused by this assertion.</param>
    /// <remarks>
    /// <para>
    /// THE CHARACTERIZATION PREREQUISITE. A golden-master comparison is only valid if the candidate
    /// is repeatable, so this pins that the rewriter reads no clock, no random source, no
    /// environment variable and no ambient culture - the four determinism seams AAP section 0.6.7
    /// enumerates. Ten iterations rather than two, so a value that only varies occasionally has ten
    /// chances to show itself.
    /// </para>
    /// <para>
    /// Each iteration constructs a FRESH request and reaches a FRESH model through the dispatcher,
    /// so this also pins that the arm holds no state between calls - which is what allows the single
    /// registered instance to be shared.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(Statements))]
    public void RewritingTheSameRequestRepeatedlyProducesTheIdenticalStatement(
        string scenario,
        string sql,
        string expected,
        string orderByArgument,
        bool singleBlock)
    {
        string first = RewrittenPrincipal(sql);

        for (int iteration = 0; iteration < 10; iteration++)
        {
            Assert.Equal(first, RewrittenPrincipal(sql), StringComparer.Ordinal);
        }

        Assert.Equal(expected, first, StringComparer.Ordinal);

        Assert.False(string.IsNullOrWhiteSpace(scenario));
        Assert.False(string.IsNullOrEmpty(orderByArgument));
        Assert.Equal(singleBlock, !sql.Contains("UNION", StringComparison.Ordinal));
    }

    /// <summary>
    /// A WHITESPACE-PERTURBED expectation does NOT satisfy the comparisons this suite uses - proving
    /// mechanically that nothing here is whitespace-normalised.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EVERY OTHER ASSERTION IN THIS FILE DEPENDS ON THIS ONE BEING TRUE. "Compared ordinally, never
    /// normalised" is a claim about the assertions, and a claim about assertions can only be checked
    /// by an assertion. So the real statement is perturbed in the four ways a tidy-up would perturb
    /// it - a doubled space, a collapsed space, a space inserted at the comma, a trailing space -
    /// and each perturbation is required to FAIL equality.
    /// </para>
    /// <para>
    /// A suite that used a normalising comparer would fail this test, which is exactly the point.
    /// </para>
    /// </remarks>
    [Fact]
    public void AWhitespacePerturbedStatementFailsTheOrdinalComparison()
    {
        string actual = RewrittenPrincipal(FixtureSelect);

        string[] perturbations =
        [
            // The one space before the final WHERE, doubled - the tidy-up this file warns about.
            actual.Replace(
                OraclePagingRewriter.OuterTableAlias + " WHERE",
                OraclePagingRewriter.OuterTableAlias + "  WHERE",
                StringComparison.Ordinal),

            // The same space removed entirely.
            actual.Replace(
                OraclePagingRewriter.OuterTableAlias + " WHERE",
                OraclePagingRewriter.OuterTableAlias + "WHERE",
                StringComparison.Ordinal),

            // A space inserted at the comma the oracle leaves tight.
            actual.Replace(".*,ROW_NUMBER()", ".*, ROW_NUMBER()", StringComparison.Ordinal),

            // A trailing space, which a trimming comparer would forgive.
            actual + " ",
        ];

        foreach (string perturbed in perturbations)
        {
            Assert.NotEqual(actual, perturbed);
            Assert.False(string.Equals(actual, perturbed, StringComparison.Ordinal));
        }

        // And every perturbation really is a different string, so the loop above is not comparing
        // the statement with itself four times.
        Assert.Distinct(perturbations, StringComparer.Ordinal);
    }

    /// <summary>
    /// A terminator the caller supplied is re-attached at the OUTERMOST end and nowhere else.
    /// </summary>
    /// <remarks>
    /// THE FINDING THIS PINS. The caller's text becomes the INNERMOST derived table, so a semicolon
    /// retained inside it would terminate the whole generated statement inside the first parenthesis
    /// and silently discard the row-number projection, both <c>WHERE</c> clauses and all three
    /// aliases. Asserted as a suffix AND as a single occurrence, because a semicolon in the middle
    /// would still leave one at the end.
    /// </remarks>
    [Fact]
    public void AnInboundTerminatorIsReAttachedOnlyAtTheOutermostEnd()
    {
        foreach (StatementCase statementCase in StatementCases)
        {
            string terminated = Rewritten(new PagingRewriteRequest(
                statementCase.Sql + ";",
                PrincipalPageSize,
                PrincipalPageIndex,
                false));

            Assert.Equal(statementCase.Expected + ";", terminated, StringComparer.Ordinal);
            Assert.EndsWith(";", terminated, StringComparison.Ordinal);
            Assert.Equal(1, CountOccurrences(terminated, ";"));
        }
    }

    /// <summary>
    /// C-E, MECHANICALLY CHECKED: nothing in the Oracle arm's assembly graph reaches an Oracle
    /// client, and the arm itself holds no state that could carry a connection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The claim "no Oracle instance, no client package, no connection" is made in this file's
    /// header and in AAP section 0.6.4, and prose cannot enforce it. The assembly's declared
    /// references are enumerated instead, so a future <c>Oracle.ManagedDataAccess</c> or ODP.NET
    /// reference added anywhere in the persistence service turns this red - which is where such a
    /// reference would first appear.
    /// </para>
    /// <para>
    /// The arm's STORAGE is asserted away as well, which is the structural half of the same
    /// guarantee: it declares no instance field at all, and every static field it declares is a
    /// compile-time literal. A type with no instance storage and no mutable static storage has
    /// nowhere to put a connection, a session, a cache or a clock, so it cannot be anything but a
    /// pure string transform - and that is also what makes one registered instance safe to share
    /// across concurrent calls.
    /// </para>
    /// <para>
    /// The literal test is the precise one: <c>const</c> fields are reflected as static fields, so
    /// an assertion of "no fields whatsoever" would fail against the five sentinel constants that
    /// SHOULD be there. What must not exist is a static field that can be WRITTEN.
    /// </para>
    /// </remarks>
    [Fact]
    public void NothingInTheArmsAssemblyGraphReachesAnOracleClient()
    {
        Type armType = typeof(OraclePagingRewriter);

        Assert.Empty(armType.GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));

        foreach (FieldInfo field in armType.GetFields(
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            Assert.True(
                field.IsLiteral,
                $"{field.Name} is static but not a compile-time constant, so it can hold state");
        }

        foreach (AssemblyName reference in armType.Assembly.GetReferencedAssemblies())
        {
            string name = reference.Name ?? string.Empty;

            Assert.DoesNotContain("Oracle", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ODP", name, StringComparison.OrdinalIgnoreCase);
        }
    }
}
