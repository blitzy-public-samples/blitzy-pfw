// ==============================================================================================
//  ClauseModifierTests - the characterization suite that pins
//  PowerFramework.Persistence.Sql.ClauseModifier
//  --------------------------------------------------------------------------------------------
//  SYSTEM UNDER TEST  services/persistence-service/PowerFramework.Persistence/Sql/ClauseModifier.cs
//  BEHAVIOURAL ORACLE ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru
//                         :L247-L267   _of_reset
//                         :L269-L284   of_setwhereclause      - the guard is :L271
//                         :L286-L301   of_setorderbyclause    - the guard is :L288
//                         :L684        the pre-loop presence test
//                     ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru
//                         :L380, :L383 the two-arity caller-side proxies
//                     ws_objects/pfw.shared.pbl.src/enums.sru:L715-L722  the three modify styles
//                     all READ ONLY per constraint C-C - read as specification, never edited
//
//  THIS SUITE EXISTS FOR TWO REASONS, AND THE SECOND ONE IS WHY IT IS NEW
//  --------------------------------------------------------------------------------------------
//  1. The upsert semantics are the single most hazardous translation in the file under test. The
//     legacy reads a `for` counter AFTER its loop, and PowerScript does not scope one to its loop,
//     so the no-match value is "one past the end" - which in PowerScript APPENDS. The idiomatic C#
//     transliteration uses a zero-based counter whose no-match value is 0, and 0 in a zero-based
//     list is THE FIRST ELEMENT, so it OVERWRITES where the legacy appends. Both versions leave a
//     list of length one after two sets, so no count assertion separates them; the difference shows
//     up as a silently missing WHERE clause on a compound SELECT, in generated SQL, at run time.
//     The append-versus-overwrite cases below are the only thing standing between that trap and a
//     future reader.
//
//  2. THE FILE UNDER TEST CARRIES THE STRUCTURAL CLAUSE GUARD, and it had no suite at all. Its own
//     doc comment already required that the acceptance of space-only text "must be pinned by an
//     explicit test in this project's suite, so that anyone who tightens this guard is met with a
//     failure rather than a silently narrowed contract" - and no such test existed. The guard is a
//     security boundary published on persistence.v1.SqlClauseSpec.clause, so both halves are pinned
//     here: every enumerated refusal, and every accepted form the refusal must not catch.
//
//  WHAT A GUARD SUITE HAS TO ASSERT, AND WHY THE ACCEPTANCE HALF IS THE LARGER ONE
//  --------------------------------------------------------------------------------------------
//  A guard that refuses everything passes every refusal test. The acceptance matrix is therefore
//  not decoration: it is what makes the refusal matrix meaningful, and it is deliberately populated
//  with the awkward cases rather than the easy ones - a semicolon INSIDE a literal, an apostrophe
//  escaped by doubling, a column named `communion` whose first five letters spell a refused word, a
//  column named `updated_at` whose first seven spell another, and subtraction and division operators
//  whose doubled forms are comment introducers.
//
//  WHERE THE STYLE CONSTANTS COME FROM, AND WHERE THE WIRE ENUM DOES NOT (C-K)
//  --------------------------------------------------------------------------------------------
//  The three legacy modify styles are declared at ws_objects/pfw.shared.pbl.src/enums.sru:L718-L720
//  and nowhere else in the oracle:
//
//      Constant Long SQL_MS_REPLACE  = 1     [enums.sru:L718]
//      Constant Long SQL_MS_APPEND   = 2     [enums.sru:L719]
//      Constant Long SQL_MS_PREPEND  = 3     [enums.sru:L720]
//
//  Those exact line numbers are cited because a nearby locator is easy to get wrong: :L715 opens the
//  `/*--- SQL Parser ---*/` band, :L717 is the `//Modify styles` comment and :L722 closes the band, so
//  only :L718-L720 carry constants. The repository-root .editorconfig cites L717-L719 for the same
//  three in its own comment, which is off by one; this suite deliberately cites the measured range so
//  a reader following either trail arrives at the right lines. That is a comment-only discrepancy in
//  a file this suite does not own, so it is recorded here rather than edited.
//
//  THE CORRESPONDING WIRE ENUM LIVES IN persistence.v1.proto AND NOT IN common.v1.proto. Stated
//  explicitly so nobody searches the wrong file for it:
//
//      shared/PowerFramework.Contracts/Proto/persistence.v1.proto:L244-L255
//          enum SqlModifyStyle { SQL_MS_UNSPECIFIED = 0; SQL_MS_REPLACE = 1;
//                                SQL_MS_APPEND = 2; SQL_MS_PREPEND = 3; }
//
//  common.v1.proto mentions SQL_MS_* only in prose, as the MIRROR IMAGE of its own no-sentinel
//  ruling for RetCode. The enum is not promoted there because the common file's admission test is
//  that BOTH sibling definitions need a shape, and this one has a single consumer - C-05's clause
//  modification - so promoting it would turn the common file into a shared-code back door (C-A).
//  SQL_MS_UNSPECIFIED = 0 is a PROTO3 MECHANICAL ARTIFACT and not a fourth legacy style: the legacy
//  set contains no zero at all, while proto3 requires the first enumerator of every enum to be zero.
//
//  WHY THIS FILE REFERENCES THE PRESERVED SPELLINGS AND NEVER DECLARES ONE
//  --------------------------------------------------------------------------------------------
//  SQL_MS_REPLACE, SQL_MS_APPEND and SQL_MS_PREPEND keep their legacy SCREAMING_SNAKE spelling in C#
//  because those identifiers travel in serialized payloads, in log records and in characterization
//  recordings, so a rename would silently invalidate every stored comparison that mentions one. The
//  repository-root .editorconfig carries CA1707 and IDE1006 suppressions SCOPED BY FILE NAME, and
//  .editorconfig:L692 names services/persistence-service/PowerFramework.Persistence/Sql/ClauseModifier.cs
//  - the file under test. THERE IS NO SECTION FOR THIS TEST PROJECT, so this file sits under the plain
//  [*.cs] band. It therefore only ever REFERENCES those constants; it declares no underscored
//  identifier of its own, because under the inherited TreatWarningsAsErrors a preserved-spelling
//  declaration outside the suppression scope is a BUILD ERROR rather than a style nit. Both halves of
//  that decision are asserted below rather than trusted: the VALUES and the SPELLINGS.
//
//  THIS IS THE DOCUMENTED SQL-INJECTION SITE, AND THIS SUITE DOES NOT PRETEND OTHERWISE (C-F)
//  --------------------------------------------------------------------------------------------
//  The clause arrives as a RAW STRING. The legacy applies exactly one test to it - that it is not the
//  empty string [:L271, :L288] - stores it verbatim, and the query task later splices it into the
//  generated statement. The mechanical root of the legacy exposure is not this object at all: it is
//  the connection parameter string, where DisableBind=1 means the PowerBuilder runtime does NOT use
//  bind variables and interpolates values into the statement text as literals
//  [n_cst_thread_task_sqlbase.sru:L128-L129].
//
//  THE MITIGATION IS DELIBERATELY SOMEWHERE ELSE, AND THAT IS WHY IT IS NOT ASSERTED HERE.
//  Parameterization happens where commands are executed, and redaction of the statement text happens
//  on the diagnostic path in Errors/SqlRedactor.cs - asserted by the sibling SqlRedactorTests.cs, not
//  by this file. INPUT SANITISATION IS NOT THE MITIGATION, so no case below demands that a clause be
//  rewritten, escaped, quoted or trimmed, and none demands that it be refused merely for CONTAINING
//  SQL as though the legacy had refused it. Every accepted clause is asserted to be stored BYTE FOR
//  BYTE, which is the property byte-exact statement parity depends on.
//
//  The structural-refusal matrix further down is TIER 2 and is labelled as such throughout: it
//  implements the refusals ENUMERATED ON persistence.v1.SqlClauseSpec.clause, and that published text
//  is the specification it is checked against. It is not, and must never be read as, a claim about
//  what the legacy did - the legacy did none of it. Keeping those two things apart is what makes this
//  suite compatible with the replicate-do-not-correct constraint (C-B) while still pinning a security
//  boundary. And no clause text is written to any log from this file, at any level.
//
//  WHAT THIS SUITE DELIBERATELY DOES NOT DO
//  --------------------------------------------------------------------------------------------
//  NO DATABASE, AND NOTHING THAT IMPLIES ONE (C-E). There is no DbContext, no connection, no DBMS
//  client, no test container and no schema anywhere below. The system under test is in-memory
//  bookkeeping over strings, and its one collaborator - SelectStatementModel - is a pure string model.
//  No fixture is fabricated because none is needed.
//
//  NO PERFORMANCE ASSERTION OF ANY KIND. The repository publishes no service-level agreement, no
//  latency budget, no throughput target and no availability commitment, so there is nothing to assert
//  against and inventing a number would fabricate a requirement. Nothing below measures elapsed time,
//  allocations or throughput.
//
//  DETERMINISTIC BY CONSTRUCTION. No clock is read, no randomness is drawn, no file is touched and no
//  ambient culture is depended upon - the one case that changes the culture restores it, and the
//  ordered call log is rendered with the invariant culture. Repeatability is the one hard prerequisite
//  of the golden-master technique, so two consecutive runs must produce identical results.
//
//  TIERS, following the sibling SelectStatementModelTests convention:
//    TIER 1 - TRACEABLE:  the guard of :L271 / :L288, the upsert order, the two-arity delegation,
//                         the reset, the presence test, the three style constants and their values,
//                         and the order in which the accumulated clauses drive the statement model
//                         [:L689-L702].
//    TIER 2 - DECIDED:    the structural guard's refused-word set and its scanner rules. The legacy
//                         has no such guard, so nothing measures these - they implement the
//                         published contract text on SqlClauseSpec.clause, and that text is the
//                         specification they are checked against. Also the wire sentinel's numbering,
//                         the absence of a conventionally-cased alias, and the single-declaration
//                         invariant over the persistence assembly.
//    TIER 3 - PORT-LOCAL: null tolerance and the default-struct normalisation, neither of which
//                         PowerScript can express at all.
//
//  LAYOUT, so a reader can find a case without scrolling:
//    1  the legacy guard              a member-data matrix of every refusal and every acceptance
//    2  the upsert                    a member-data matrix of sequences, plus the append/match facts
//    3  the style constants           values, preserved spellings, no alias, declared exactly once
//    4  the wire-to-legacy mapping    the enum's numbering and the single reconciliation point
//    5  driving the statement model   WHERE before ORDER BY, insertion order, one-based index, style
//    6  the structural guard          the refusal half
//    7  the structural guard          the acceptance half, which is deliberately the larger matrix
//
//  NO DATABASE, NO CLOCK, NO I/O. The system under test is in-memory bookkeeping over strings, so
//  every case below is a pure function call. No fixture is fabricated and none is needed.
// ==============================================================================================

using System.Globalization;
using System.Reflection;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// The legacy guard, the upsert semantics, the style constants, the reset, the presence test, and the
/// order in which the accumulated clauses drive the statement model.
/// </summary>
public sealed class ClauseModifierTests
{
    /// <summary>A style value that is legal, so no case below fails for the wrong reason.</summary>
    private const long AnyLegalStyle = ClauseModifier.SQL_MS_REPLACE;

    /// <summary>
    /// A two-block compound statement, so a select index other than <c>1</c> addresses something real.
    /// </summary>
    /// <remarks>
    /// Borrowed from the sibling <c>SelectStatementModelCompoundSelectTests</c> so the two suites agree
    /// on what a compound statement looks like. Two blocks is the minimum that can distinguish "the
    /// one-based index reached the addressed block" from "the index was quietly decremented and hit the
    /// previous one", which is the off-by-one this suite exists to rule out.
    /// </remarks>
    private const string TwoBlockStatement = "SELECT ID FROM COMPANY MINUS SELECT ID FROM RETIRED";

    /// <summary>A single-block statement that already carries a <c>WHERE</c> clause to compose onto.</summary>
    private const string SingleBlockWithWhere = "SELECT ID FROM COMPANY WHERE AGE > 30";

    /// <summary>A single-block statement that already carries an <c>ORDER BY</c> clause.</summary>
    private const string SingleBlockWithOrder = "SELECT ID FROM COMPANY ORDER BY AGE";

    /// <summary>
    /// A distinct, guard-safe clause body for a given select index, so an assertion can tell WHICH set
    /// wrote a given entry.
    /// </summary>
    /// <param name="selectIndex">The select index the clause belongs to.</param>
    /// <returns>An ordinary comparison predicate mentioning the index.</returns>
    /// <remarks>
    /// Rendered with <see cref="CultureInfo.InvariantCulture"/> so the text is identical on every host -
    /// determinism is a required property of this suite, and a culture-sensitive number format would make
    /// the same case produce different text on a different machine. The vocabulary is deliberately inside
    /// what the structural clause guard admits (a bare identifier, a comparison operator and a digit
    /// run), so no case in this suite is ever refused for a reason it was not testing.
    /// </remarks>
    private static string ClauseFor(int selectIndex) =>
        string.Create(CultureInfo.InvariantCulture, $"AGE > {selectIndex}");

    // ==========================================================================================
    //  THE LEGACY GUARD - :L271 and :L288
    // ==========================================================================================

    /// <summary>
    /// The whole guard as one matrix: every refusal it makes and every acceptance it must still make.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>PRESERVED LEGACY BEHAVIOUR (C-B), transcribed from
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L269-L301</c>.</b> The guard
    /// is one line, appearing identically at <c>:L271</c> for <c>of_setwhereclause</c> and at
    /// <c>:L288</c> for <c>of_setorderbyclause</c>:
    /// </para>
    /// <code>
    /// if selectIndex &lt;= 0 or clause = "" then return RetCode.E_INVALID_ARGUMENT
    /// </code>
    /// <para>
    /// Three properties of that line are load-bearing and are all pinned by the cases below.
    /// </para>
    /// <list type="number">
    /// <item>
    /// <b>The operator is <c>&lt;= 0</c>, not <c>&lt; 0</c>.</b> ZERO IS REJECTED and does not mean
    /// "the first select"; the first select is <c>1</c>, which is therefore the LOWEST LEGAL INDEX and
    /// is asserted to succeed.
    /// </item>
    /// <item>
    /// <b>There is no upper bound in the guard at all</b>, so <see cref="int.MaxValue"/> is ACCEPTED
    /// here. An index that addresses no block is refused later and elsewhere, by the statement model
    /// returning <see langword="false"/> - a different layer answering in a different vocabulary. That
    /// split is legacy shape and is not harmonised.
    /// </item>
    /// <item>
    /// <b>"Empty" means exactly the empty string.</b> The comparison is against <c>""</c> and nothing
    /// more, so SPACE-ONLY TEXT IS ACCEPTED. Tightening this to a whitespace test would refuse input
    /// the oracle accepts, which is a behaviour change and not a hardening (C-B).
    /// </item>
    /// </list>
    /// <para>
    /// The two halves are joined by <c>or</c> and yield ONE code: the legacy does not distinguish a
    /// non-positive index from an empty clause, and neither does the port - which is why a case
    /// supplying both faults at once still expects that single code.
    /// </para>
    /// <para>
    /// TIER 1 throughout, except the <see langword="null"/> clause, which is TIER 3: PowerScript
    /// strings are never null, so there is no legacy behaviour for it. Folding null into the guard the
    /// legacy already has narrows nothing and invents nothing.
    /// </para>
    /// </remarks>
    public static TheoryData<string, int, string?, long> GuardArguments =>
        new()
        {
            // ---- REFUSALS -----------------------------------------------------------------------
            // The exact boundary of the `<= 0` operator. Zero is not "the first select".
            { "select index zero, the exact <= 0 boundary", 0, "AGE > 30", RetCode.E_INVALID_ARGUMENT },
            { "a negative select index", -1, "AGE > 30", RetCode.E_INVALID_ARGUMENT },
            { "the most negative select index", int.MinValue, "AGE > 30", RetCode.E_INVALID_ARGUMENT },

            // The emptiness half, at the lowest legal index and at a higher one, so the refusal is
            // attributable to the clause rather than to the index.
            { "an empty clause at the lowest legal index", 1, "", RetCode.E_INVALID_ARGUMENT },
            { "an empty clause at a higher valid index", 7, "", RetCode.E_INVALID_ARGUMENT },

            // TIER 3 - PowerScript has no null, so this is port-local tolerance routed to the same code.
            { "a null clause, which PowerScript cannot express", 1, null, RetCode.E_INVALID_ARGUMENT },

            // Both halves of the OR at once still produce the one code the legacy answers.
            { "both halves of the OR failing together", 0, "", RetCode.E_INVALID_ARGUMENT },

            // ---- ACCEPTANCES --------------------------------------------------------------------
            // ONE is the lowest legal index. This is the other side of the `<= 0` boundary and the case
            // that stops the guard from being satisfied by blanket refusal.
            { "select index ONE, the lowest legal index", 1, "AGE > 30", RetCode.OK },
            { "select index two", 2, "AGE > 30", RetCode.OK },

            // The guard has no upper bound, so an absurd index is still accepted HERE.
            { "int.MaxValue, because the guard has no upper bound", int.MaxValue, "AGE > 30", RetCode.OK },

            // PRESERVED LEGACY BEHAVIOUR: the emptiness test is against "" exactly, so whitespace is
            // not empty. See the third numbered point in the remarks above.
            { "space-only text, since the test is against the empty string exactly", 1, " ", RetCode.OK },
            { "several spaces, for the same reason", 1, "   ", RetCode.OK },
        };

    /// <summary>
    /// TIER 1. The guard admits a positive index carrying non-empty text and refuses everything else,
    /// with the same single code for either fault, and mutates nothing when it refuses.
    /// </summary>
    /// <param name="because">The case, named so a failure message identifies it.</param>
    /// <param name="selectIndex">The select index presented.</param>
    /// <param name="clause">The clause text presented.</param>
    /// <param name="expected">The return code the legacy answers for that pair.</param>
    [Theory]
    [MemberData(nameof(GuardArguments))]
    public void TheGuardAcceptsOnlyAPositiveIndexWithNonEmptyText(
        string because,
        int selectIndex,
        string? clause,
        long expected)
    {
        // Independent instances, so the WHERE result cannot be explained by the ORDER BY call or the
        // other way round. The legacy setters are line-for-line identical apart from the array they
        // address [:L269-L284 versus :L286-L301], so both must answer identically for every case.
        ClauseModifier whereModifier = new();
        ClauseModifier orderModifier = new();

        long whereResult = whereModifier.SetWhereClause(selectIndex, AnyLegalStyle, clause);
        long orderResult = orderModifier.SetOrderByClause(selectIndex, AnyLegalStyle, clause);

        Assert.True(
            expected == whereResult,
            string.Create(
                CultureInfo.InvariantCulture,
                $"SetWhereClause answered {whereResult} rather than {expected} for {because}."));
        Assert.True(
            expected == orderResult,
            string.Create(
                CultureInfo.InvariantCulture,
                $"SetOrderByClause answered {orderResult} rather than {expected} for {because}."));

        if (expected == RetCode.OK)
        {
            // Exactly one entry, carrying the caller's index and the caller's text unchanged.
            SqlClause storedWhere = Assert.Single(whereModifier.WhereClauses);
            SqlClause storedOrder = Assert.Single(orderModifier.OrderByClauses);

            Assert.Equal(selectIndex, storedWhere.SelectIndex);
            Assert.Equal(selectIndex, storedOrder.SelectIndex);
            Assert.Equal(clause, storedWhere.Clause);
            Assert.Equal(clause, storedOrder.Clause);

            // The other collection on each instance is untouched: the two are wholly separate arrays
            // in the legacy [:L43-L44] and wholly separate lists here.
            Assert.Empty(whereModifier.OrderByClauses);
            Assert.Empty(orderModifier.WhereClauses);

            return;
        }

        // A REFUSAL MUTATES NOTHING. The legacy guard runs before any assignment [:L271 precedes
        // :L279-L281], so there is no partial entry to find - not a stored index without text, not an
        // empty-text entry, nothing. Both collections on both instances, because a guard that leaked
        // into the other collection would be worse than one that leaked into its own.
        Assert.Empty(whereModifier.WhereClauses);
        Assert.Empty(whereModifier.OrderByClauses);
        Assert.Empty(orderModifier.WhereClauses);
        Assert.Empty(orderModifier.OrderByClauses);
        Assert.False(whereModifier.HasPendingClauses);
        Assert.False(orderModifier.HasPendingClauses);
    }

    /// <summary>
    /// TIER 1, AND THE PIN THE FILE UNDER TEST ASKS FOR BY NAME. The legacy guard compares against
    /// <c>""</c> and nothing more, so SPACE-ONLY text is stored and reported as success.
    /// </summary>
    /// <remarks>
    /// Anyone who "tightens" the emptiness test to <c>IsNullOrWhiteSpace</c>, or who widens the
    /// structural guard's character rule to refuse a run of spaces, is met with this failure rather
    /// than with a silently narrowed contract (C-B). The structural guard deliberately admits the
    /// SPACE character while refusing every control character, which is what keeps this case passing
    /// and a tab-bearing clause refused.
    /// </remarks>
    [Theory]
    [InlineData(" ")]
    [InlineData("   ")]
    public void SpaceOnlyTextIsStoredBecauseTheLegacyGuardTestsOnlyForTheEmptyString(string clause)
    {
        ClauseModifier modifier = new();

        Assert.Equal(RetCode.OK, modifier.SetWhereClause(AnyLegalStyle, clause));

        Assert.Equal(clause, Assert.Single(modifier.WhereClauses).Clause);
    }

    /// <summary>
    /// TIER 1. The stored triple is the caller's triple, byte for byte, with no normalisation.
    /// </summary>
    [Fact]
    public void AnAcceptedClauseIsStoredVerbatimWithItsIndexAndStyle()
    {
        ClauseModifier modifier = new();

        const string clause = "  age > 30   AND  name LIKE 'A%'  ";

        Assert.Equal(
            RetCode.OK,
            modifier.SetWhereClause(3, ClauseModifier.SQL_MS_APPEND, clause));

        SqlClause stored = Assert.Single(modifier.WhereClauses);

        Assert.Equal(3, stored.SelectIndex);
        Assert.Equal(ClauseModifier.SQL_MS_APPEND, stored.ModifyStyle);

        // NOT trimmed, NOT collapsed, NOT re-cased. The guard refuses or stores; it never rewrites.
        Assert.Equal(clause, stored.Clause);
    }

    /// <summary>
    /// TIER 1. The style is stored WITHOUT validation, matching the legacy, which dispatches on it
    /// later with no default arm.
    /// </summary>
    /// <param name="style">The style to store.</param>
    [Theory]
    [InlineData(0L)]
    [InlineData(4L)]
    [InlineData(99L)]
    [InlineData(-1L)]
    public void AnOutOfDomainStyleIsStoredRatherThanRefusedHere(long style)
    {
        ClauseModifier modifier = new();

        Assert.Equal(RetCode.OK, modifier.SetWhereClause(style, "age > 30"));
        Assert.Equal(style, Assert.Single(modifier.WhereClauses).ModifyStyle);
    }

    /// <summary>
    /// TIER 1. The OTHER HALF of the unvalidated style: it is refused later, by the statement model,
    /// and not here - which is the only reason storing it unchecked is safe.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The legacy stores <c>ms</c> with no test of any kind [<c>:L280</c>, <c>:L297</c>] and the native
    /// parser it eventually feeds simply returns <c>false</c> for a style it does not recognise, which
    /// the apply loop converts into an internal error [<c>:L691-L693</c>, <c>:L698-L700</c>]. So the
    /// validation exists - it just lives one layer down and answers in the parser's boolean vocabulary
    /// rather than in the task's return-code algebra.
    /// </para>
    /// <para>
    /// Asserted here because "the setter accepts any style" is only half a contract, and a reader who
    /// saw the accepting half alone would reasonably conclude a garbage style reaches the generated
    /// SQL. It does not.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnOutOfDomainStyleIsRefusedLaterByTheStatementModelAndNotHere()
    {
        ClauseModifier modifier = new();

        const long unrecognisedStyle = 99L;

        // Accepted by the setter, exactly as the legacy accepts it.
        Assert.Equal(RetCode.OK, modifier.SetWhereClause(1, unrecognisedStyle, "AGE > 30"));

        SqlClause stored = Assert.Single(modifier.WhereClauses);

        Assert.Equal(unrecognisedStyle, stored.ModifyStyle);

        // And refused by the model when the apply loop presents it, changing nothing.
        SelectStatementModel model = new();

        Assert.True(model.Parse(SingleBlockWithWhere));
        Assert.False(model.ModifyWhere(stored.SelectIndex, stored.ModifyStyle, stored.Clause));
        Assert.Equal("AGE > 30", model.GetWhere(1));
        Assert.Equal(SingleBlockWithWhere, model.GetSql());
    }

    // ==========================================================================================
    //  THE UPSERT - THE TRAP THIS SUITE EXISTS FOR
    // ==========================================================================================

    /// <summary>
    /// The upsert as one matrix: a sequence of select indices applied in order, and the entry order the
    /// collection must be left holding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>PRESERVED LEGACY BEHAVIOUR (C-B), and the reason is structural.</b> The oracle body at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L269-L301</c> is:
    /// </para>
    /// <code>
    /// nCount = UpperBound(_whereClauses)          // :L273
    /// for nIndex = 1 to nCount                    // :L274
    ///     if _whereClauses[nIndex].index = selectIndex then
    ///         exit                                // :L276
    ///     end if
    /// next                                        // :L278
    /// _whereClauses[nIndex].index  = selectIndex  // :L279  <-- AFTER the loop
    /// _whereClauses[nIndex].ms     = ms           // :L280
    /// _whereClauses[nIndex].clause = clause       // :L281
    /// </code>
    /// <para>
    /// PowerScript does not scope a <c>for</c> counter to its loop, so the three assignments read the
    /// value the loop left behind, and that value encodes which of three things happens:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <b>no entry matched</b> - the loop ran to completion, so <c>nIndex</c> is <c>nCount + 1</c>: one
    /// past the upper bound. Assigning past the upper bound GROWS a PowerScript array, so the entry is
    /// APPENDED.
    /// </item>
    /// <item><b>an entry matched</b> - <c>nIndex</c> is that entry's position, so it is UPDATED IN PLACE.</item>
    /// <item>
    /// <b>the array was empty</b> - <c>nCount</c> is <c>0</c>, the body never runs and <c>nIndex</c>
    /// stays <c>1</c>, so the entry becomes the first element.
    /// </item>
    /// </list>
    /// <para>
    /// Stated without reference to any loop counter: an UPSERT KEYED ON THE SELECT INDEX THAT PRESERVES
    /// INSERTION ORDER. The trap is that the idiomatic C# transliteration uses a zero-based counter
    /// whose no-match value is <c>0</c>, and <c>0</c> in a zero-based list is THE FIRST ELEMENT - so it
    /// OVERWRITES where the legacy APPENDS. Both versions leave a list of length one after two sets
    /// with different indices, so no count assertion on its own separates them. The
    /// <c>[1, 2, 9]</c> and <c>[4, 1]</c> cases below are the ones that do.
    /// </para>
    /// <para>
    /// TIER 1 throughout. Nothing sorts the collection and nothing may, because the apply loop feeds the
    /// statement model in exactly this order.
    /// </para>
    /// </remarks>
    public static TheoryData<string, int[], int[]> UpsertSequences =>
        new()
        {
            // The empty-collection branch, which is genuinely reachable: _of_reset assigns a fresh empty
            // array over both collections [:L248, :L264-L265], so the next set after a reset lands here.
            { "an empty collection takes the first entry", [5], [5] },

            // THE NAMED APPEND CASE. Index 1 then index 5 must leave TWO entries, not one.
            { "two distinct indices append in insertion order", [1, 5], [1, 5] },

            // THE NAMED MATCH CASE. The same index twice must leave ONE entry.
            { "a repeat on the same index updates in place", [2, 2], [2] },

            // A match sandwiched between appends keeps its ORIGINAL position rather than moving to the end.
            { "a repeat between appends keeps its original position", [3, 1, 3, 2], [3, 1, 2] },

            // Insertion order, NOT select-index order. Descending input stays descending.
            { "descending indices are not sorted", [7, 5, 3], [7, 5, 3] },

            // THE TRAP CASE. A third, unmatched index must not overwrite entry 2 - and must not
            // overwrite entry 1 either, which is what the zero-based no-match value would have done.
            { "an unmatched index does not overwrite an existing entry", [1, 2, 9], [1, 2, 9] },

            // The lowest legal index arriving SECOND still appends, because the key is the index and not
            // its magnitude.
            { "the lowest legal index appends after a higher one", [4, 1], [4, 1] },

            // Every index repeated collapses to one entry each, in first-seen order.
            { "repeats of every index collapse to one entry each", [1, 2, 1, 2], [1, 2] },
        };

    /// <summary>
    /// TIER 1. The upsert keys on the select index, updates a match in place, appends a non-match at the
    /// end, and never reorders - on BOTH collections, which are line-for-line identical in the oracle.
    /// </summary>
    /// <param name="because">The case, named so a failure message identifies it.</param>
    /// <param name="applied">The select indices to set, in order.</param>
    /// <param name="expected">The select indices the collection must be left holding, in order.</param>
    [Theory]
    [MemberData(nameof(UpsertSequences))]
    public void TheUpsertKeysOnSelectIndexAndPreservesInsertionOrder(
        string because,
        int[] applied,
        int[] expected)
    {
        ClauseModifier modifier = new();

        foreach (int selectIndex in applied)
        {
            // The clause text is derived from the index so a later assertion can tell WHICH set wrote a
            // given entry, and stays inside the vocabulary the structural guard admits.
            Assert.Equal(
                RetCode.OK,
                modifier.SetWhereClause(selectIndex, AnyLegalStyle, ClauseFor(selectIndex)));
            Assert.Equal(
                RetCode.OK,
                modifier.SetOrderByClause(selectIndex, AnyLegalStyle, ClauseFor(selectIndex)));
        }

        // Asserting the whole SEQUENCE rather than the count: the sequence implies the count, and a
        // count alone cannot distinguish append-at-the-end from overwrite-the-first.
        Assert.Equal(expected, modifier.WhereClauses.Select(static entry => entry.SelectIndex));
        Assert.Equal(expected, modifier.OrderByClauses.Select(static entry => entry.SelectIndex));

        // Last write wins per index, and the winning text is the one belonging to that index.
        Assert.True(
            modifier.WhereClauses.All(entry => entry.Clause == ClauseFor(entry.SelectIndex)),
            $"A WHERE entry held text belonging to another index for the case of {because}.");
        Assert.True(
            modifier.OrderByClauses.All(entry => entry.Clause == ClauseFor(entry.SelectIndex)),
            $"An ORDER BY entry held text belonging to another index for the case of {because}.");
    }

    /// <summary>
    /// TIER 1. THE TRAP, ASSERTED DIRECTLY: a set on an index that matches nothing leaves every existing
    /// entry byte-identical, including the LAST one, and grows the collection by exactly one.
    /// </summary>
    /// <remarks>
    /// PRESERVED LEGACY BEHAVIOUR (C-B) - the fall-through append of
    /// <c>n_cst_thread_task_sqlquery.sru:L273-L281</c>. This is the assertion the folder requirements
    /// name: an unmatched index lands at upper bound + 1 rather than overwriting the last element. The
    /// zero-based transliteration would fail it by clobbering entry one and leaving the count at three.
    /// </remarks>
    [Fact]
    public void AnUnmatchedIndexDoesNotOverwriteAnyExistingEntry()
    {
        ClauseModifier modifier = new();

        _ = modifier.SetWhereClause(1, ClauseModifier.SQL_MS_REPLACE, "AGE > 30");
        _ = modifier.SetWhereClause(2, ClauseModifier.SQL_MS_APPEND, "SALARY > 100");
        _ = modifier.SetWhereClause(3, ClauseModifier.SQL_MS_PREPEND, "AGE IS NOT NULL");

        SqlClause[] before = [.. modifier.WhereClauses];

        Assert.Equal(RetCode.OK, modifier.SetWhereClause(9, ClauseModifier.SQL_MS_REPLACE, "NAME LIKE 'A%'"));

        // Every pre-existing entry survives unchanged - SqlClause is a record struct, so this compares
        // all three components at once. The LAST one is the interesting member of that set.
        Assert.Equal(before, modifier.WhereClauses.Take(before.Length));
        Assert.Equal([1, 2, 3, 9], modifier.WhereClauses.Select(static entry => entry.SelectIndex));
    }

    /// <summary>
    /// TIER 1. The APPENDED entry carries all three components of the legacy structure - the index, the
    /// style and the clause - and the entry it was appended after is untouched.
    /// </summary>
    /// <remarks>
    /// The oracle assigns all three fields at <c>:L279-L281</c>, so an append that recorded only the
    /// text would be a partial port. The structure itself is <c>integer index / long ms / string
    /// clause</c> at <c>n_cst_thread_task_sqlquery.sru:L10-L14</c>.
    /// </remarks>
    [Fact]
    public void TheAppendedEntryCarriesItsIndexItsStyleAndItsClause()
    {
        ClauseModifier modifier = new();

        Assert.Equal(RetCode.OK, modifier.SetWhereClause(1, ClauseModifier.SQL_MS_REPLACE, "AGE > 30"));
        Assert.Equal(RetCode.OK, modifier.SetWhereClause(5, ClauseModifier.SQL_MS_PREPEND, "SALARY > 100"));

        Assert.Equal([1, 5], modifier.WhereClauses.Select(static entry => entry.SelectIndex));

        SqlClause first = modifier.WhereClauses[0];
        SqlClause appended = modifier.WhereClauses[1];

        // The appended entry, all three components.
        Assert.Equal(5, appended.SelectIndex);
        Assert.Equal(ClauseModifier.SQL_MS_PREPEND, appended.ModifyStyle);
        Assert.Equal("SALARY > 100", appended.Clause);

        // And the entry it landed after, all three components, unchanged.
        Assert.Equal(1, first.SelectIndex);
        Assert.Equal(ClauseModifier.SQL_MS_REPLACE, first.ModifyStyle);
        Assert.Equal("AGE > 30", first.Clause);
    }

    /// <summary>
    /// TIER 1. A MATCH rewrites all three components, not merely the text, because the oracle assigns
    /// index, style and clause together.
    /// </summary>
    /// <remarks>
    /// A port that updated only <c>clause</c> on a match would pass a text-only assertion while leaving
    /// a stale style behind - and a stale style changes the generated SQL, since the style decides
    /// whether the text replaces, appends to or prepends to the existing clause.
    /// </remarks>
    [Fact]
    public void AMatchRewritesAllThreeComponentsAndNotJustTheText()
    {
        ClauseModifier modifier = new();

        _ = modifier.SetWhereClause(4, ClauseModifier.SQL_MS_REPLACE, "AGE > 30");
        Assert.Equal(RetCode.OK, modifier.SetWhereClause(4, ClauseModifier.SQL_MS_APPEND, "SALARY > 100"));

        SqlClause stored = Assert.Single(modifier.WhereClauses);

        Assert.Equal(4, stored.SelectIndex);
        Assert.Equal(ClauseModifier.SQL_MS_APPEND, stored.ModifyStyle);
        Assert.Equal("SALARY > 100", stored.Clause);
    }

    /// <summary>
    /// TIER 3 - PORT-LOCAL. A default-constructed <see cref="SqlClause"/> observes as an EMPTY clause
    /// and never as <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// No code path in <c>ClauseModifier</c> produces one - the setters reject an empty clause before
    /// constructing an entry - but a record struct always has a default form that a caller can write, so
    /// the normalisation is pinned rather than left to be discovered. PowerScript has no null string, so
    /// there is no oracle behaviour here to preserve or contradict.
    /// </remarks>
    [Fact]
    public void ADefaultSqlClauseObservesAsAnEmptyClauseRatherThanNull()
    {
        SqlClause empty = default;

        Assert.Equal(0, empty.SelectIndex);
        Assert.Equal(0L, empty.ModifyStyle);
        Assert.Equal(string.Empty, empty.Clause);

        // And an explicitly null clause is normalised the same way, so the two agree.
        SqlClause fromNull = new(1, ClauseModifier.SQL_MS_REPLACE, null);

        Assert.Equal(string.Empty, fromNull.Clause);
    }

    /// <summary>
    /// TIER 1. A second set on a DIFFERENT index APPENDS. The idiomatic zero-based transliteration
    /// would have overwritten entry one here.
    /// </summary>
    [Fact]
    public void ASetOnANewIndexAppendsRatherThanOverwritingTheFirstEntry()
    {
        ClauseModifier modifier = new();

        Assert.Equal(RetCode.OK, modifier.SetWhereClause(1, AnyLegalStyle, "first"));
        Assert.Equal(RetCode.OK, modifier.SetWhereClause(2, AnyLegalStyle, "second"));
        Assert.Equal(RetCode.OK, modifier.SetWhereClause(7, AnyLegalStyle, "seventh"));

        Assert.Equal([1, 2, 7], modifier.WhereClauses.Select(entry => entry.SelectIndex));
        Assert.Equal(
            ["first", "second", "seventh"],
            modifier.WhereClauses.Select(entry => entry.Clause));
    }

    /// <summary>
    /// TIER 1. A second set on the SAME index updates IN PLACE, keeping its position, so repeated
    /// sets are last-wins rather than accumulative.
    /// </summary>
    [Fact]
    public void ASetOnAnExistingIndexUpdatesInPlaceAndKeepsItsPosition()
    {
        ClauseModifier modifier = new();

        _ = modifier.SetWhereClause(1, ClauseModifier.SQL_MS_REPLACE, "first");
        _ = modifier.SetWhereClause(2, ClauseModifier.SQL_MS_REPLACE, "second");
        _ = modifier.SetWhereClause(1, ClauseModifier.SQL_MS_PREPEND, "rewritten");

        Assert.Equal([1, 2], modifier.WhereClauses.Select(entry => entry.SelectIndex));
        Assert.Equal("rewritten", modifier.WhereClauses[0].Clause);
        Assert.Equal(ClauseModifier.SQL_MS_PREPEND, modifier.WhereClauses[0].ModifyStyle);
        Assert.Equal("second", modifier.WhereClauses[1].Clause);
    }

    /// <summary>
    /// TIER 1. The two collections are independent: a WHERE set never touches ORDER BY and vice
    /// versa, even on the same select index.
    /// </summary>
    [Fact]
    public void TheTwoCollectionsAreIndependentOnTheSameSelectIndex()
    {
        ClauseModifier modifier = new();

        _ = modifier.SetWhereClause(1, AnyLegalStyle, "age > 30");
        _ = modifier.SetOrderByClause(1, AnyLegalStyle, "age A");

        Assert.Equal("age > 30", Assert.Single(modifier.WhereClauses).Clause);
        Assert.Equal("age A", Assert.Single(modifier.OrderByClauses).Clause);
    }

    /// <summary>
    /// TIER 1. The two-arity overloads address select ONE, reproducing bodies that are literally
    /// <c>return of_SetWhereClause(1,ms,clause)</c>.
    /// </summary>
    [Fact]
    public void TheTwoArityOverloadsAddressSelectOne()
    {
        ClauseModifier modifier = new();

        _ = modifier.SetWhereClause(AnyLegalStyle, "age > 30");
        _ = modifier.SetOrderByClause(AnyLegalStyle, "age A");

        Assert.Equal(1, Assert.Single(modifier.WhereClauses).SelectIndex);
        Assert.Equal(1, Assert.Single(modifier.OrderByClauses).SelectIndex);
    }

    /// <summary>
    /// TIER 1. Reset discards BOTH collections, and the next set afterwards lands at position one -
    /// which is the empty-collection branch of the upsert and the reason that branch is reachable.
    /// </summary>
    [Fact]
    public void ResetDiscardsBothCollectionsAndTheNextSetLandsAtPositionOne()
    {
        ClauseModifier modifier = new();

        _ = modifier.SetWhereClause(4, AnyLegalStyle, "age > 30");
        _ = modifier.SetOrderByClause(4, AnyLegalStyle, "age A");

        Assert.True(modifier.HasPendingClauses);

        modifier.Reset();

        Assert.Empty(modifier.WhereClauses);
        Assert.Empty(modifier.OrderByClauses);
        Assert.False(modifier.HasPendingClauses);

        _ = modifier.SetWhereClause(9, AnyLegalStyle, "afterwards");

        SqlClause stored = Assert.Single(modifier.WhereClauses);

        Assert.Equal(9, stored.SelectIndex);
        Assert.Equal("afterwards", stored.Clause);
    }

    /// <summary>
    /// TIER 1. The presence test of <c>:L684</c> is true when EITHER collection holds anything.
    /// </summary>
    [Fact]
    public void ThePresenceTestIsTrueWhenEitherCollectionHoldsAnything()
    {
        ClauseModifier whereOnly = new();
        ClauseModifier orderOnly = new();

        _ = whereOnly.SetWhereClause(AnyLegalStyle, "age > 30");
        _ = orderOnly.SetOrderByClause(AnyLegalStyle, "age A");

        Assert.True(whereOnly.HasPendingClauses);
        Assert.True(orderOnly.HasPendingClauses);
        Assert.False(new ClauseModifier().HasPendingClauses);
    }

    // ==========================================================================================
    //  THE STYLE CONSTANTS - enums.sru:L718-L720
    //  ------------------------------------------------------------------------------------------
    //  TIER 1. Both the VALUES and the SPELLINGS are asserted, and the spellings matter as much as the
    //  values: AAP 0.4.5.3 keeps the SCREAMING_SNAKE identifiers precisely because they appear in
    //  serialized payloads, in log records and in characterization recordings, so a rename would
    //  silently invalidate every stored comparison that mentions one. A value-only suite would let a
    //  rename through.
    //
    //  This file REFERENCES those identifiers and declares none of its own - see the header for why the
    //  .editorconfig suppression that permits the declaration is scoped to ClauseModifier.cs by name and
    //  does not cover this project.
    // ==========================================================================================

    /// <summary>
    /// TIER 1. The three styles carry their legacy numbers, delegate to the one shared numeric source,
    /// are mutually distinct, and none of them is zero.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Transcribed from <c>ws_objects/pfw.shared.pbl.src/enums.sru</c>: <c>SQL_MS_REPLACE = 1</c> at
    /// <c>:L718</c>, <c>SQL_MS_APPEND = 2</c> at <c>:L719</c>, <c>SQL_MS_PREPEND = 3</c> at <c>:L720</c>.
    /// The bare literals are asserted deliberately rather than only the delegation, because asserting
    /// <c>ClauseModifier.SQL_MS_REPLACE == Enums.SQL_MS_REPLACE</c> alone would still pass if BOTH sides
    /// were renumbered together.
    /// </para>
    /// <para>
    /// <b>NONE OF THEM IS ZERO, and that absence is load-bearing.</b> It is the whole reason
    /// <c>persistence.v1.proto</c> had to invent an <c>SQL_MS_UNSPECIFIED = 0</c> sentinel for the wire
    /// form, and the reason that sentinel has no oracle locator.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheStyleConstantsCarryTheirLegacyNumericValues()
    {
        Assert.Equal(1L, ClauseModifier.SQL_MS_REPLACE);
        Assert.Equal(2L, ClauseModifier.SQL_MS_APPEND);
        Assert.Equal(3L, ClauseModifier.SQL_MS_PREPEND);

        // One numeric source of truth: the constants delegate to the shared catalogue rather than
        // re-declaring the numbers.
        Assert.Equal(Enums.SQL_MS_REPLACE, ClauseModifier.SQL_MS_REPLACE);
        Assert.Equal(Enums.SQL_MS_APPEND, ClauseModifier.SQL_MS_APPEND);
        Assert.Equal(Enums.SQL_MS_PREPEND, ClauseModifier.SQL_MS_PREPEND);

        // Mutually distinct, and no zero anywhere in the set.
        Assert.Equal(
            [ClauseModifier.SQL_MS_REPLACE, ClauseModifier.SQL_MS_APPEND, ClauseModifier.SQL_MS_PREPEND],
            new[]
            {
                ClauseModifier.SQL_MS_REPLACE,
                ClauseModifier.SQL_MS_APPEND,
                ClauseModifier.SQL_MS_PREPEND,
            }.Distinct());
        Assert.DoesNotContain(
            0L,
            new[]
            {
                ClauseModifier.SQL_MS_REPLACE,
                ClauseModifier.SQL_MS_APPEND,
                ClauseModifier.SQL_MS_PREPEND,
            });
    }

    /// <summary>
    /// TIER 1. THE SPELLINGS ARE PRESERVED VERBATIM, asserted by reflection so a rename cannot pass.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The public constant surface of <c>ClauseModifier</c> must be EXACTLY the three legacy spellings
    /// with exactly the legacy numbers. An exact-set assertion is chosen over three containment checks
    /// on purpose: it fails both when a preserved spelling is renamed AND when a new public constant is
    /// introduced without review, which is the situation in which a conventionally-named alias would
    /// creep in.
    /// </para>
    /// <para>
    /// Names are read from metadata, so the assertion is about the IDENTIFIER and not about a string
    /// literal that happens to sit beside it. The set is ordered before comparison because reflection
    /// makes no guarantee about field order.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheStyleConstantSpellingsArePreservedVerbatim()
    {
        FieldInfo[] constants = PublicConstantsOf(typeof(ClauseModifier));

        Assert.Equal(
            ["SQL_MS_APPEND", "SQL_MS_PREPEND", "SQL_MS_REPLACE"],
            constants.Select(static field => field.Name).OrderBy(static name => name, StringComparer.Ordinal));

        // Every one is a long, and each carries the legacy number under its legacy spelling.
        Assert.All(constants, static field => Assert.Equal(typeof(long), field.FieldType));
        Assert.Equal(2L, RawLongValueOf(constants, "SQL_MS_APPEND"));
        Assert.Equal(3L, RawLongValueOf(constants, "SQL_MS_PREPEND"));
        Assert.Equal(1L, RawLongValueOf(constants, "SQL_MS_REPLACE"));

        // And the shared catalogue carries the same three spellings, since that is where the numbers
        // actually live [shared/PowerFramework.Shared.Kernel/Enums.cs, from enums.sru:L718-L720].
        foreach (string preserved in (string[])["SQL_MS_REPLACE", "SQL_MS_APPEND", "SQL_MS_PREPEND"])
        {
            Assert.NotNull(typeof(Enums).GetField(preserved, BindingFlags.Public | BindingFlags.Static));
        }
    }

    /// <summary>
    /// TIER 2 - DECIDED. NO CONVENTIONALLY-CASED ALIAS IS PUBLISHED for any of the three spellings.
    /// </summary>
    /// <remarks>
    /// <c>ClauseModifier</c>'s own header forbids offering one, and the reasoning is worth pinning: an
    /// alias is an invitation for a file OUTSIDE the narrow <c>.editorconfig</c> suppression scope to
    /// declare its own copy of the preserved spelling, which under the inherited
    /// <c>TreatWarningsAsErrors</c> would break the build. Asserting the absence keeps a well-meant
    /// "tidy" addition from being made silently.
    /// </remarks>
    [Fact]
    public void NoConventionallyCasedAliasIsPublishedForThePreservedSpellings()
    {
        foreach (string alias in (string[])["SqlMsReplace", "SqlMsAppend", "SqlMsPrepend", "Replace", "Append", "Prepend"])
        {
            Assert.Empty(typeof(ClauseModifier).GetMember(alias));
        }
    }

    /// <summary>
    /// TIER 2 - DECIDED. The preserved spellings are declared EXACTLY ONCE inside the persistence
    /// assembly, and <c>ClauseModifier</c> is the type that declares them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This enforces an audit rather than restating a comment. <c>Tasks/SqlQueryTask</c> and
    /// <c>Grpc/QueryService</c> both CONSUME this vocabulary and the one-based upsert behind it, and both
    /// record in their own documentation that they do not re-declare either. A duplicated constant set is
    /// exactly where two copies drift apart - one gets renumbered, the other does not, and the generated
    /// SQL silently changes - so the single-declaration property is asserted instead of trusted.
    /// </para>
    /// <para>
    /// The scan is deliberately assembly-wide and includes non-public fields, because a private
    /// duplicate would drift just as readily as a public one. The shared kernel legitimately declares the
    /// same three spellings and lives in a different assembly, which is why the scan is scoped to this
    /// one.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePreservedSpellingsAreDeclaredExactlyOnceInThePersistenceAssembly()
    {
        List<string> declarations = [];

        foreach (Type type in typeof(ClauseModifier).Assembly.GetTypes())
        {
            IEnumerable<FieldInfo> fields = type.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly);

            foreach (FieldInfo field in fields)
            {
                if (field.Name.StartsWith("SQL_MS_", StringComparison.Ordinal))
                {
                    declarations.Add($"{type.FullName}.{field.Name}");
                }
            }
        }

        Assert.Equal(
            [
                $"{typeof(ClauseModifier).FullName}.SQL_MS_APPEND",
                $"{typeof(ClauseModifier).FullName}.SQL_MS_PREPEND",
                $"{typeof(ClauseModifier).FullName}.SQL_MS_REPLACE",
            ],
            declarations.OrderBy(static declaration => declaration, StringComparer.Ordinal));
    }

    /// <summary>
    /// Every public constant declared on a type, which for the type under test is the preserved style
    /// vocabulary and nothing else.
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    /// <returns>Its public static literal fields.</returns>
    private static FieldInfo[] PublicConstantsOf(Type type) =>
        [.. type
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(static field => field.IsLiteral)];

    /// <summary>
    /// The compile-time value of a named constant, read from metadata rather than from a reference, so
    /// the assertion cannot be satisfied by a renamed field.
    /// </summary>
    /// <param name="constants">The candidate fields.</param>
    /// <param name="name">The exact preserved spelling to find.</param>
    /// <returns>The declared numeric value.</returns>
    private static long RawLongValueOf(FieldInfo[] constants, string name)
    {
        FieldInfo field = Assert.Single(constants, candidate => candidate.Name == name);

        return Assert.IsType<long>(field.GetRawConstantValue());
    }

    // ==========================================================================================
    //  THE WIRE-TO-LEGACY STYLE MAPPING
    //  ------------------------------------------------------------------------------------------
    //  THE WIRE ENUM IS DECLARED IN persistence.v1.proto:L244-L255 AND NOT IN common.v1.proto (C-K).
    //  Repeated here beside the cases that use it so a reader looking for SqlModifyStyle does not search
    //  the common file, which mentions SQL_MS_* only in prose. The reason it is not promoted is that its
    //  only consumer is C-05's clause modification, and the common file admits only shapes that BOTH
    //  sibling definitions need.
    // ==========================================================================================

    /// <summary>
    /// TIER 1 for the three real styles, TIER 2 for the sentinel. The published enum numbers its three
    /// real members exactly as the oracle does, under the generated PascalCase spelling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The generated C# spellings are <c>SqlMsUnspecified</c>, <c>SqlMsReplace</c>, <c>SqlMsAppend</c>
    /// and <c>SqlMsPrepend</c> - protobuf's own casing transform, not a rename of the preserved
    /// identifiers, which survive verbatim in the <c>.proto</c> text itself.
    /// </para>
    /// <para>
    /// <b><c>SQL_MS_UNSPECIFIED = 0</c> is a proto3 mechanical artifact and not a fourth legacy style.</b>
    /// The legacy set has no zero at all, while proto3 requires the first enumerator of every enum to be
    /// zero, so the sentinel exists for the encoding rather than for the domain. Asserting it is zero
    /// documents that it cannot collide with any real style.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheWireEnumCarriesTheSameThreeNumbersUnderItsGeneratedSpelling()
    {
        Assert.Equal(0, (int)SqlModifyStyle.SqlMsUnspecified);
        Assert.Equal(1, (int)SqlModifyStyle.SqlMsReplace);
        Assert.Equal(2, (int)SqlModifyStyle.SqlMsAppend);
        Assert.Equal(3, (int)SqlModifyStyle.SqlMsPrepend);

        // The two sides agree numerically, which is what makes the conversion below a reconciliation
        // rather than a translation.
        Assert.Equal(ClauseModifier.SQL_MS_REPLACE, (long)SqlModifyStyle.SqlMsReplace);
        Assert.Equal(ClauseModifier.SQL_MS_APPEND, (long)SqlModifyStyle.SqlMsAppend);
        Assert.Equal(ClauseModifier.SQL_MS_PREPEND, (long)SqlModifyStyle.SqlMsPrepend);
    }

    /// <summary>
    /// TIER 1. The three legal styles map onto the three shared constants, which is the one place the
    /// published enum and the shared constant catalogue are reconciled numerically.
    /// </summary>
    [Fact]
    public void TheThreeLegalStylesMapOntoTheThreeSharedConstants()
    {
        Assert.Equal(
            ClauseModifier.SQL_MS_REPLACE,
            ClauseModifier.ToModifyStyle(SqlModifyStyle.SqlMsReplace));
        Assert.Equal(
            ClauseModifier.SQL_MS_APPEND,
            ClauseModifier.ToModifyStyle(SqlModifyStyle.SqlMsAppend));
        Assert.Equal(
            ClauseModifier.SQL_MS_PREPEND,
            ClauseModifier.ToModifyStyle(SqlModifyStyle.SqlMsPrepend));
    }

    /// <summary>
    /// TIER 2. <c>SQL_MS_UNSPECIFIED</c> is a proto3 artifact and passes through as the rejected
    /// value zero, NEVER as replace. An unrecognised open-enum number passes through unchanged too.
    /// </summary>
    /// <param name="wireValue">The raw numeric value arriving on the open enum field.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(99)]
    [InlineData(-7)]
    public void AnOutOfDomainWireStylePassesThroughUnchangedRatherThanBecomingReplace(int wireValue)
    {
        long converted = ClauseModifier.ToModifyStyle((SqlModifyStyle)wireValue);

        Assert.Equal(wireValue, converted);
        Assert.NotEqual(ClauseModifier.SQL_MS_REPLACE, converted);
    }

    // ==========================================================================================
    //  DRIVING THE STATEMENT MODEL, AND THE ORDER OF THE MODIFY CALLS
    //  ------------------------------------------------------------------------------------------
    //  TIER 1. The accumulated clauses are worth nothing until something applies them, and the ORDER in
    //  which they are applied is part of the contract rather than an implementation detail. The oracle
    //  walks its two arrays in two SEPARATE loops at
    //  ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L689-L702 - the WHERE array to
    //  completion first, then the ORDER BY array - calling the parser's three-argument
    //  ModifyWhere/ModifyOrder with the entry's own one-based index, its style and its text, and
    //  abandoning the whole build with RetCode.E_INTERNAL_ERROR on the first false return.
    //
    //  WHERE THAT LOOP LIVES IN THE PORT, AND WHY IT IS MIRRORED HERE RATHER THAN INVOKED.
    //  Tasks/SqlQueryTask.ApplyStoredClauses owns the production loop and is covered by SqlQueryTaskTests
    //  end to end, including its two error texts and its early return. This suite is about the
    //  COLLECTION's contribution to that loop - insertion order, WHERE before ORDER BY, the one-based
    //  index arriving untouched, and the stored style deciding composition - so it reproduces the loop's
    //  shape over a REAL SelectStatementModel instead of constructing a task. That keeps the two suites
    //  from asserting the same thing twice while still pinning the property that belongs here.
    //
    //  ORDER IS ASSERTED AND NOT MERELY CONTENT, which is why the Seam 3 recorder from TestDoubles.cs is
    //  used alongside the model: ScriptedCarrierSurface keeps ONE ordered log of every modify call, so
    //  "the WHERE clauses were applied before the ORDER BY clauses, in insertion order" is expressible.
    //  The model verifies WHAT each call did; the recorder verifies WHEN. A content-only assertion would
    //  pass for a port that applied the ORDER BY entries first, or that sorted by select index.
    // ==========================================================================================

    /// <summary>
    /// One modify call rendered for the ordered log, in the parameter order the statement model declares.
    /// </summary>
    /// <param name="verb">Either <c>ModifyWhere</c> or <c>ModifyOrder</c>.</param>
    /// <param name="clause">The pending entry being applied.</param>
    /// <returns>A stable, culture-independent rendering of the call.</returns>
    /// <remarks>
    /// The one-based SELECT INDEX comes first, then the style, then the text - the order the oracle uses
    /// at <c>:L691</c> and the order <c>n_sql.sru:L43</c> and <c>:L49</c> declare. Rendered with
    /// <see cref="CultureInfo.InvariantCulture"/> because ordering and determinism are required seams: a
    /// culture-sensitive number format would make the recorded log differ between hosts, and a recording
    /// that differs between hosts cannot serve as a master.
    /// </remarks>
    private static string RenderModifyCall(string verb, SqlClause clause) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{verb}({clause.SelectIndex},{clause.ModifyStyle},{clause.Clause})");

    /// <summary>
    /// Applies every pending clause to the model in the oracle's order, recording each call as it goes.
    /// </summary>
    /// <param name="modifier">The accumulated clauses.</param>
    /// <param name="model">The statement being rewritten.</param>
    /// <param name="recorder">The ordered log of modify calls.</param>
    /// <remarks>
    /// Mirrors the shape of <c>Tasks/SqlQueryTask.ApplyStoredClauses</c>, itself the port of
    /// <c>n_cst_thread_task_sqlquery.sru:L689-L702</c>: TWO loops over TWO separate collections, WHERE
    /// first and to completion, then ORDER BY. Each call is asserted to succeed here so that a case which
    /// is really about ordering does not silently rest on a failed modification.
    /// </remarks>
    private static void ApplyPendingClauses(
        ClauseModifier modifier,
        SelectStatementModel model,
        ScriptedCarrierSurface recorder)
    {
        foreach (SqlClause pending in modifier.WhereClauses)
        {
            _ = recorder.Modify(RenderModifyCall("ModifyWhere", pending));

            Assert.True(
                model.ModifyWhere(pending.SelectIndex, pending.ModifyStyle, pending.Clause),
                RenderModifyCall("ModifyWhere", pending) + " was refused by the statement model.");
        }

        foreach (SqlClause pending in modifier.OrderByClauses)
        {
            _ = recorder.Modify(RenderModifyCall("ModifyOrder", pending));

            Assert.True(
                model.ModifyOrder(pending.SelectIndex, pending.ModifyStyle, pending.Clause),
                RenderModifyCall("ModifyOrder", pending) + " was refused by the statement model.");
        }
    }

    /// <summary>
    /// Builds the standing ordering scenario: two <c>WHERE</c> entries added in DESCENDING select-index
    /// order plus one <c>ORDER BY</c> entry, so insertion order and select-index order disagree.
    /// </summary>
    /// <returns>The accumulated clauses.</returns>
    /// <remarks>
    /// Adding select index <c>2</c> BEFORE select index <c>1</c> is the point of the fixture. If anything
    /// sorted the collection, or applied the ORDER BY entry first, the recorded log would differ - so the
    /// scenario can distinguish "preserved insertion order" from "happened to be in order anyway".
    /// </remarks>
    private static ClauseModifier OutOfOrderScenario()
    {
        ClauseModifier modifier = new();

        Assert.Equal(RetCode.OK, modifier.SetWhereClause(2, ClauseModifier.SQL_MS_REPLACE, "AGE > 65"));
        Assert.Equal(RetCode.OK, modifier.SetWhereClause(1, ClauseModifier.SQL_MS_REPLACE, "AGE > 30"));
        Assert.Equal(RetCode.OK, modifier.SetOrderByClause(1, ClauseModifier.SQL_MS_REPLACE, "ID"));

        return modifier;
    }

    /// <summary>
    /// TIER 1. Every <c>WHERE</c> entry is applied before any <c>ORDER BY</c> entry, and within each
    /// collection the entries are applied in INSERTION order - not select-index order.
    /// </summary>
    [Fact]
    public void ThePendingClausesDriveTheModelWhereFirstThenOrderByEachInInsertionOrder()
    {
        ClauseModifier modifier = OutOfOrderScenario();
        SelectStatementModel model = new();
        ScriptedCarrierSurface recorder = new();

        Assert.True(model.Parse(TwoBlockStatement));
        Assert.Equal(2, model.GetSelectCount());

        ApplyPendingClauses(modifier, model, recorder);

        // THE ORDERED LOG. Select index 2 was set first, so it is applied first even though 1 is lower;
        // and both WHERE calls precede the ORDER BY call, which is the two-separate-loops shape of
        // :L689-L702 rather than one interleaved pass.
        Assert.Equal(
            [
                "ModifyWhere(2,1,AGE > 65)",
                "ModifyWhere(1,1,AGE > 30)",
                "ModifyOrder(1,1,ID)",
            ],
            recorder.ModifyScripts);

        // Contiguous ordinals from one, all of the modify verb: nothing else touched the surface, and
        // nothing was applied twice.
        Assert.Equal([1, 2, 3], recorder.Calls.Select(static call => call.Ordinal));
        Assert.All(recorder.Calls, static call => Assert.Equal(CarrierCallKind.Modify, call.Kind));
    }

    /// <summary>
    /// TIER 1. The ONE-BASED select index reaches the block it names. This is the assertion that rules
    /// out the off-by-one a zero-based translation would introduce.
    /// </summary>
    /// <remarks>
    /// A list POSITION and a SELECT INDEX are different quantities, and nothing may convert one into the
    /// other. Here the entry for select index <c>2</c> sits at list position <c>0</c> because it was set
    /// first - so a port that used the position, or that decremented the index, would put its clause on
    /// block <c>1</c>. The emitted statement is asserted in full, because the clause landing on the wrong
    /// block is precisely the kind of defect that leaves every count assertion happy.
    /// </remarks>
    [Fact]
    public void TheOneBasedSelectIndexReachesTheAddressedBlockAndNotThePreviousOne()
    {
        ClauseModifier modifier = OutOfOrderScenario();
        SelectStatementModel model = new();
        ScriptedCarrierSurface recorder = new();

        Assert.True(model.Parse(TwoBlockStatement));

        // The entry naming select index 2 is at list position 0. The two must not be confused.
        Assert.Equal(2, modifier.WhereClauses[0].SelectIndex);

        ApplyPendingClauses(modifier, model, recorder);

        Assert.Equal("AGE > 30", model.GetWhere(1));
        Assert.Equal("AGE > 65", model.GetWhere(2));
        Assert.Equal("ID", model.GetOrder(1));
        Assert.Equal(string.Empty, model.GetOrder(2));
        Assert.False(model.HasOrder(2));

        Assert.Equal(
            "SELECT ID FROM COMPANY WHERE AGE > 30 ORDER BY ID " +
            "MINUS SELECT ID FROM RETIRED WHERE AGE > 65",
            model.GetSql());
    }

    /// <summary>
    /// The three styles and the <c>WHERE</c> body each one produces from
    /// <c>SELECT ID FROM COMPANY WHERE AGE &gt; 30</c>.
    /// </summary>
    /// <remarks>
    /// The expected bodies are measured against the model rather than predicted: a <c>WHERE</c> fragment
    /// is joined with a single SPACE, so an append yields <c>existing + " " + new</c> and a prepend yields
    /// <c>new + " " + existing</c>. That is why the append fragment carries its own leading <c>AND</c> and
    /// the prepend fragment carries a trailing one - composing SQL connectives is the caller's job, and
    /// the model neither adds nor infers them.
    /// </remarks>
    public static TheoryData<string, long, string, string> StyleCompositions =>
        new()
        {
            { "replace substitutes the whole body", ClauseModifier.SQL_MS_REPLACE, "SALARY > 100", "SALARY > 100" },
            { "append composes after the existing body", ClauseModifier.SQL_MS_APPEND, "AND SALARY < 1000", "AGE > 30 AND SALARY < 1000" },
            { "prepend composes before the existing body", ClauseModifier.SQL_MS_PREPEND, "SALARY > 100 AND", "SALARY > 100 AND AGE > 30" },
        };

    /// <summary>
    /// TIER 1. The style the collection STORED is the style that reaches the model, and it decides how the
    /// clause composes with what is already there.
    /// </summary>
    /// <param name="because">The case, named so a failure message identifies it.</param>
    /// <param name="style">The stored modify style.</param>
    /// <param name="fragment">The clause text to set.</param>
    /// <param name="expectedWhere">The <c>WHERE</c> body the model must be left holding.</param>
    [Theory]
    [MemberData(nameof(StyleCompositions))]
    public void TheStoredStyleDecidesHowTheClauseComposes(
        string because,
        long style,
        string fragment,
        string expectedWhere)
    {
        ClauseModifier modifier = new();
        SelectStatementModel model = new();
        ScriptedCarrierSurface recorder = new();

        Assert.True(model.Parse(SingleBlockWithWhere));
        Assert.Equal(RetCode.OK, modifier.SetWhereClause(1, style, fragment));

        ApplyPendingClauses(modifier, model, recorder);

        Assert.True(
            expectedWhere == model.GetWhere(1),
            $"Expected [{expectedWhere}] but the model holds [{model.GetWhere(1)}] when {because}.");

        // The recorded call carries the STORED style, so the log and the outcome agree.
        Assert.Equal(
            [RenderModifyCall("ModifyWhere", Assert.Single(modifier.WhereClauses))],
            recorder.ModifyScripts);
    }

    /// <summary>
    /// TIER 1. An <c>ORDER BY</c> append composes with the LIST connective, which is a bare comma - not
    /// the space a <c>WHERE</c> fragment uses.
    /// </summary>
    /// <remarks>
    /// Asserted separately from the <c>WHERE</c> compositions because the two clause kinds join their
    /// fragments differently, and a port that used one connective for both would still satisfy every
    /// <c>WHERE</c> case above while emitting invalid SQL for an ordering.
    /// </remarks>
    [Fact]
    public void AnOrderByAppendComposesWithTheListConnective()
    {
        ClauseModifier modifier = new();
        SelectStatementModel model = new();
        ScriptedCarrierSurface recorder = new();

        Assert.True(model.Parse(SingleBlockWithOrder));
        Assert.Equal(RetCode.OK, modifier.SetOrderByClause(1, ClauseModifier.SQL_MS_APPEND, "SALARY DESC"));

        ApplyPendingClauses(modifier, model, recorder);

        Assert.Equal("AGE,SALARY DESC", model.GetOrder(1));
        Assert.Equal("SELECT ID FROM COMPANY ORDER BY AGE,SALARY DESC", model.GetSql());
        Assert.Equal(["ModifyOrder(1,2,SALARY DESC)"], recorder.ModifyScripts);
    }

    /// <summary>
    /// TIER 1. With nothing pending, nothing is applied and the statement is emitted untouched.
    /// </summary>
    /// <remarks>
    /// This is the other side of the presence test the oracle performs immediately before its apply loop
    /// [<c>:L684</c>]: when both arrays are empty the loop is skipped entirely, so the statement reaches
    /// the database exactly as it arrived. A port that ran the loop anyway would still emit the same SQL,
    /// which is why the RECORDER is asserted empty rather than only the text.
    /// </remarks>
    [Fact]
    public void NothingIsAppliedWhenNoClauseIsPending()
    {
        ClauseModifier modifier = new();
        SelectStatementModel model = new();
        ScriptedCarrierSurface recorder = new();

        Assert.True(model.Parse(TwoBlockStatement));
        Assert.False(modifier.HasPendingClauses);

        ApplyPendingClauses(modifier, model, recorder);

        Assert.Empty(recorder.ModifyScripts);
        Assert.Empty(recorder.Calls);
        Assert.Equal(TwoBlockStatement, model.GetSql());
        Assert.False(model.HasWhere(1));
        Assert.False(model.HasWhere(2));
    }

    /// <summary>
    /// The ordered log is REPEATABLE - identical across consecutive runs and independent of the ambient
    /// culture.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Repeatability is the golden-master technique's one hard prerequisite, and ordering is one of the
    /// determinism seams this refactor is required to keep. Nothing in the path under test reads a clock,
    /// draws randomness or touches a file, so the same scenario must produce a byte-identical log every
    /// time - and must keep producing it on a host whose ambient culture is not the invariant one.
    /// </para>
    /// <para>
    /// The culture is restored in a <c>finally</c> so no sibling case inherits it.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheOrderedLogIsIdenticalAcrossRunsAndAcrossCultures()
    {
        IReadOnlyList<string> first = CaptureOrderedLog();
        IReadOnlyList<string> second = CaptureOrderedLog();

        Assert.Equal(first, second);

        CultureInfo original = CultureInfo.CurrentCulture;

        try
        {
            foreach (string cultureName in (string[])["de-DE", "tr-TR", "sv-SE"])
            {
                CultureInfo.CurrentCulture = new CultureInfo(cultureName);

                Assert.Equal(first, CaptureOrderedLog());
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>
    /// Runs the standing ordering scenario once and returns the ordered modify-call log.
    /// </summary>
    /// <returns>The rendered modify calls, in call order.</returns>
    private static IReadOnlyList<string> CaptureOrderedLog()
    {
        ClauseModifier modifier = OutOfOrderScenario();
        SelectStatementModel model = new();
        ScriptedCarrierSurface recorder = new();

        Assert.True(model.Parse(TwoBlockStatement));

        ApplyPendingClauses(modifier, model, recorder);

        return recorder.ModifyScripts;
    }

    // ==========================================================================================
    //  THE STRUCTURAL GUARD - THE REFUSAL HALF
    //  ------------------------------------------------------------------------------------------
    //  TIER 2 throughout. The legacy has no such guard, so these do not characterize it - they
    //  implement the refusals enumerated on persistence.v1.SqlClauseSpec.clause, and that published
    //  text is the specification they are checked against. Each case is applied to BOTH setters,
    //  because both reach the same helper and a guard installed on one only would be worse than
    //  none - a caller would simply use the other.
    // ==========================================================================================

    /// <summary>
    /// Every structural form the boundary refuses, one case per named vector.
    /// </summary>
    public static TheoryData<string, string> RefusedClauses =>
        new()
        {
            // A statement terminator, and the multi-statement attack it enables.
            { "a bare statement terminator", "age > 30;" },
            { "a terminated second statement", "age > 30; DROP TABLE COMPANY" },
            { "a terminator with nothing after it but space", "age > 30 ; " },

            // Comment introducers, each of which can comment out the rest of the statement.
            { "a line comment", "age > 30 -- and the rest is gone" },
            { "a line comment with no space", "age>30--gone" },
            { "a block comment opener", "age > 30 /* and the rest" },
            { "a block comment closer", "age > 30 */ inherited from an earlier clause" },
            { "a complete block comment", "age /* sneaky */ > 30" },

            // Nested statements and subqueries.
            { "a scalar subquery", "age > (SELECT MAX(age) FROM COMPANY)" },
            { "a bare SELECT", "SELECT 1" },
            { "an EXISTS subquery", "EXISTS (select 1 from COMPANY)" },
            { "a FROM at clause level", "1=1 FROM COMPANY" },

            // Set operators, including Oracle's spelling.
            { "a UNION", "1=1 UNION SELECT 1" },
            { "a lower-case union", "1=1 union all select 1" },
            { "an INTERSECT", "1=1 INTERSECT SELECT 1" },
            { "an EXCEPT", "1=1 EXCEPT SELECT 1" },
            { "a MINUS", "1=1 MINUS SELECT 1" },

            // DML.
            { "an INSERT", "1=1 INSERT INTO COMPANY VALUES (1)" },
            { "an UPDATE", "1=1 UPDATE COMPANY SET age = 0" },
            { "a DELETE", "1=1 DELETE" },
            { "a MERGE", "1=1 MERGE" },
            { "an INTO", "1=1 INTO OUTFILE" },

            // DDL.
            { "a DROP", "1=1 DROP TABLE COMPANY" },
            { "a mixed-case DrOp", "1=1 DrOp TABLE COMPANY" },
            { "an ALTER", "1=1 ALTER TABLE COMPANY" },
            { "a CREATE", "1=1 CREATE TABLE t (a int)" },
            { "a TRUNCATE", "1=1 TRUNCATE TABLE COMPANY" },
            { "a REINDEX", "1=1 REINDEX" },
            { "a VACUUM", "1=1 VACUUM" },

            // Permissions.
            { "a GRANT", "1=1 GRANT ALL TO PUBLIC" },
            { "a REVOKE", "1=1 REVOKE ALL FROM PUBLIC" },

            // Procedure and batch execution.
            { "an EXEC", "1=1 EXEC master..xp_cmdshell 'dir'" },
            { "an EXECUTE", "1=1 EXECUTE something" },
            { "a bare xp_ procedure", "1=1 AND xp_cmdshell('dir') = 0" },
            { "a bare sp_ procedure", "1=1 AND sp_executesql('x') = 0" },
            { "a WAITFOR time delay", "1=1 WAITFOR DELAY '00:00:05'" },
            { "a SHUTDOWN", "1=1 SHUTDOWN" },
            { "a DECLARE", "1=1 DECLARE @x int" },

            // SQLite statement verbs.
            { "an ATTACH", "1=1 ATTACH DATABASE 'x.db' AS x" },
            { "a DETACH", "1=1 DETACH x" },
            { "a PRAGMA", "1=1 PRAGMA journal_mode = OFF" },

            // Transaction control, which would let a caller commit or abandon the task's own work.
            { "a COMMIT", "1=1 COMMIT" },
            { "a ROLLBACK", "1=1 ROLLBACK" },
            { "a BEGIN", "1=1 BEGIN" },
            { "a SAVEPOINT", "1=1 SAVEPOINT s" },

            // Control characters. A newline is how a line comment truncates a statement and how a
            // property assignment is smuggled into a modification script elsewhere in this service.
            { "a line feed", "age > 30\nAND name = 'a'" },
            { "a carriage return", "age > 30\rAND name = 'a'" },
            { "a tab", "age >\t30" },
            { "a NUL", "age > 30\0" },
            { "a vertical tab", "age > 30\vAND 1=1" },
            { "a form feed", "age > 30\fAND 1=1" },
            { "a delete character", "age > 30\u007f" },
            { "a C1 control character", "age > 30\u0085AND 1=1" },

            // A STRING LITERAL IS EXEMPT FROM THE SQL RULES BUT NOT FROM THE CHARACTER-SET RULE, and a
            // bracketed identifier is exempt on exactly the same terms. Both are refused INSIDE the
            // delimited run rather than after it, which matters: a scanner that only checked control
            // characters outside quotes would let a newline through in a literal and hand the statement a
            // smuggled second line.
            { "a tab inside a string literal", "note = 'a\tb'" },
            { "a line feed inside a string literal", "note = 'a\nb'" },
            { "a tab inside a bracketed identifier", "[a\tb] = 1" },

            // Unbalanced delimiters, each of which desynchronises every later scan.
            { "an unterminated string literal", "name = 'unclosed" },
            { "an unterminated literal ending in an escaped quote", "name = 'a''" },
            { "an unterminated quoted identifier", "\"unclosed = 1" },
            { "an unterminated backtick identifier", "`unclosed` = 1 AND `x" },
            { "an unopened parenthesis", "age > 30)" },
            { "an unclosed parenthesis", "(age > 30" },
            { "a close ahead of its open", "age > 30) AND (name = 'a'" },
            { "an unclosed bracket", "[age > 30" },
            { "an unopened bracket", "age] > 30" },
        };

    /// <param name="because">The vector, named so a failure message identifies the case.</param>
    /// <param name="clause">The refused clause text.</param>
    [Theory]
    [MemberData(nameof(RefusedClauses))]
    public void AStructuralClauseIsRefusedByBothSettersAndStoresNothing(string because, string clause)
    {
        ClauseModifier whereModifier = new();
        ClauseModifier orderModifier = new();

        Assert.True(
            whereModifier.SetWhereClause(AnyLegalStyle, clause) == RetCode.E_INVALID_ARGUMENT,
            $"A WHERE clause carrying {because} was accepted.");
        Assert.True(
            orderModifier.SetOrderByClause(AnyLegalStyle, clause) == RetCode.E_INVALID_ARGUMENT,
            $"An ORDER BY clause carrying {because} was accepted.");

        // NOTHING IS STORED. A refused clause that still landed in the collection would be spliced
        // into the statement by the apply loop, which is the whole exposure.
        Assert.Empty(whereModifier.WhereClauses);
        Assert.Empty(orderModifier.OrderByClauses);
    }

    /// <summary>
    /// A REFUSAL LEAVES AN EARLIER ACCEPTED ENTRY UNTOUCHED, so a caller cannot use a refused set to
    /// disturb what it already stored.
    /// </summary>
    [Fact]
    public void ARefusalLeavesAnAlreadyStoredEntryExactlyAsItWas()
    {
        ClauseModifier modifier = new();

        _ = modifier.SetWhereClause(1, ClauseModifier.SQL_MS_REPLACE, "age > 30");

        Assert.Equal(
            RetCode.E_INVALID_ARGUMENT,
            modifier.SetWhereClause(1, ClauseModifier.SQL_MS_APPEND, "1=1; DROP TABLE COMPANY"));

        SqlClause stored = Assert.Single(modifier.WhereClauses);

        Assert.Equal("age > 30", stored.Clause);
        Assert.Equal(ClauseModifier.SQL_MS_REPLACE, stored.ModifyStyle);
    }

    // ==========================================================================================
    //  THE STRUCTURAL GUARD - THE ACCEPTANCE HALF
    //  ------------------------------------------------------------------------------------------
    //  THE LARGER MATRIX, AND DELIBERATELY SO. A guard that refused everything would satisfy every
    //  case above, so this matrix is what makes those meaningful. It is populated with the AWKWARD
    //  forms rather than the easy ones: refused words embedded inside longer identifiers, refused
    //  characters inside string literals, the doubled-quote escape, and the single-character
    //  operators whose doubled forms introduce comments.
    // ==========================================================================================

    /// <summary>
    /// Every ordinary predicate and ordering expression the refusal must not catch.
    /// </summary>
    public static TheoryData<string, string> AcceptedClauses =>
        new()
        {
            { "a simple comparison", "age > 30" },
            { "the fixture's own sort expression", "age A salary A " },
            { "a qualified column", "c.age > 30" },
            { "a bracketed identifier", "[age] > 30" },
            { "a double-quoted identifier", "\"age\" > 30" },
            { "a backtick identifier", "`age` > 30" },
            { "a conjunction", "age > 30 AND salary < 1000" },
            { "a disjunction with negation", "NOT (age > 30 OR salary IS NULL)" },
            { "an IN list over literals", "age IN (30, 31, 32)" },
            { "an IN list over strings", "name IN ('a', 'b', 'c')" },
            { "a BETWEEN", "age BETWEEN 30 AND 40" },
            { "a LIKE with wildcards", "name LIKE 'A%_'" },
            { "an IS NOT NULL", "birth IS NOT NULL" },
            { "a function call over a column", "UPPER(name) = 'CONTOSO'" },
            { "nested function calls", "LTRIM(RTRIM(SUBSTR(name, 1, 3))) = 'abc'" },
            { "an ordering with both directions", "age DESC, salary ASC" },
            { "a positional parameter", "age > ?" },
            { "two positional parameters", "age > ? AND salary < ?" },
            { "arithmetic subtraction", "age - 1 > 30" },
            { "arithmetic subtraction with no spaces", "age-1>30" },
            { "a negative literal", "salary > -100" },
            { "arithmetic division", "salary / 12 > 100" },
            { "division with no spaces", "salary/12>100" },
            { "a multiplication", "salary * 12 > 1000" },
            { "a not-equal operator", "age <> 30" },
            { "a concatenation operator", "name || address = 'ab'" },
            { "a modulo", "age % 2 = 0" },

            // THE CASES A SUBSTRING SEARCH WOULD GET WRONG. Each of these contains a refused word or
            // a refused character where it does not mean what it spells.
            { "a semicolon inside a string literal", "note = 'a;b'" },
            { "a line-comment sequence inside a literal", "note = 'a--b'" },
            { "a block-comment sequence inside a literal", "note = 'a/*b*/c'" },
            { "the word DROP inside a literal", "note = 'DROP TABLE COMPANY'" },
            { "the word UNION inside a literal", "note = 'union all select'" },
            { "an apostrophe escaped by doubling", "name = 'it''s'" },
            { "two escaped apostrophes", "name = 'a''b''c'" },
            { "a column whose name contains UNION", "communion > 0" },
            { "a column whose name contains UPDATE", "updated_at > '2020-01-01'" },
            { "a column whose name contains SELECT", "preselected = 1" },
            { "a column whose name contains DROP", "raindrops > 0" },
            { "a column whose name contains INTO", "pinto = 1" },
            { "a column whose name contains EXEC", "executive_pay > 0" },
            { "a column whose name ends in a refused word", "my_delete = 0" },
            { "a refused word as a quoted identifier", "\"select\" = 1" },
            { "a refused word as a bracketed identifier", "[union] = 1" },
            { "a refused word as a backtick identifier", "`drop` = 1" },

            // T-SQL ESCAPES A LITERAL CLOSE BRACKET BY DOUBLING IT, so [a]]b] is the SINGLE identifier
            // a]b. A scanner that treated brackets as a nesting depth, or that stopped at the first close,
            // would desynchronise here and then refuse an ordinary predicate.
            { "a bracketed identifier containing an escaped close bracket", "[a]]b] = 1" },
            { "a bracketed identifier whose escaped close ends it", "[a]]] = 1" },
            { "a non-Latin identifier", "\u59d3\u540d = 'a'" },
            { "a non-Latin string literal", "name = '\u591a\u7ebf\u7a0b'" },
            { "space-only text", "   " },
            { "balanced nested parentheses", "((age > 30) AND ((salary < 1000) OR (age < 20)))" },
        };

    /// <param name="because">The form, named so a failure message identifies the case.</param>
    /// <param name="clause">The accepted clause text.</param>
    [Theory]
    [MemberData(nameof(AcceptedClauses))]
    public void AnOrdinaryClauseIsAcceptedByBothSettersAndStoredVerbatim(string because, string clause)
    {
        ClauseModifier whereModifier = new();
        ClauseModifier orderModifier = new();

        Assert.True(
            whereModifier.SetWhereClause(AnyLegalStyle, clause) == RetCode.OK,
            $"A WHERE clause that is {because} was refused.");
        Assert.True(
            orderModifier.SetOrderByClause(AnyLegalStyle, clause) == RetCode.OK,
            $"An ORDER BY clause that is {because} was refused.");

        // STORED VERBATIM. The guard refuses or stores; it never rewrites, so byte-exact statement
        // parity is unaffected for everything it accepts.
        Assert.Equal(clause, Assert.Single(whereModifier.WhereClauses).Clause);
        Assert.Equal(clause, Assert.Single(orderModifier.OrderByClauses).Clause);
    }

    /// <summary>
    /// THE GUARD IS CULTURE-INDEPENDENT. A refused word is refused under every culture, including the
    /// Turkish one, whose dotless-i pair breaks case-insensitive comparison for any code that uses a
    /// culture-sensitive comparer.
    /// </summary>
    /// <remarks>
    /// <c>INSERT</c> and <c>insert</c> compare unequal under <c>tr-TR</c> with a culture-aware
    /// ignore-case comparison, because the upper form of <c>i</c> there is the dotted capital. A
    /// locale-dependent security guard is not a security guard, so the invariance is asserted rather
    /// than assumed - and the ambient culture is restored afterwards so no sibling test inherits it.
    /// </remarks>
    [Fact]
    public void TheRefusedWordSetIsCultureIndependent()
    {
        CultureInfo original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

            ClauseModifier modifier = new();

            Assert.Equal(
                RetCode.E_INVALID_ARGUMENT,
                modifier.SetWhereClause(AnyLegalStyle, "1=1 insert into t values (1)"));
            Assert.Equal(
                RetCode.E_INVALID_ARGUMENT,
                modifier.SetWhereClause(AnyLegalStyle, "1=1 INSERT INTO t VALUES (1)"));

            // And an ordinary clause is still accepted, so the invariance is not just blanket refusal.
            Assert.Equal(RetCode.OK, modifier.SetWhereClause(AnyLegalStyle, "inserted_at > 0"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
