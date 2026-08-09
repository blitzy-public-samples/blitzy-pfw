// ==============================================================================================
//  SelectStatementModelTests - the characterization suite that pins
//  PowerFramework.Persistence.Sql.SelectStatementModel
//  --------------------------------------------------------------------------------------------
//  SYSTEM UNDER TEST  services/persistence-service/PowerFramework.Persistence/Sql/SelectStatementModel.cs
//  BEHAVIOURAL ORACLE ws_objects/pfw.utility.parser.pbl.src/n_sql.sru        (60 lines, PROTOTYPES ONLY)
//                     ws_objects/pfw.utility.parser.pbl.src/parsesql.srf     (16 lines)
//                     ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru
//                     ws_objects/pfw.shared.pbl.src/enums.sru:L718-L720      the three modify styles
//                     ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14         the fixture's retrieve=
//                     all READ ONLY per constraint C-C - read as specification, never edited
//
//  WHAT THIS SUITE CAN AND CANNOT BE EVIDENCE OF (finding DP-7, stated first because it governs
//  every assertion below)
//  --------------------------------------------------------------------------------------------
//  n_sql.sru:L8 declares `global type n_sql from nonvisualobject native "pfw.dll"` and the file
//  contains ZERO function bodies - the 41 prototypes at L9-L49 are the whole of it. Every line of
//  behaviour lives inside a closed binary for which no C++ source exists anywhere in this
//  repository. This suite is therefore a TARGET CHARACTERIZATION of the in-repository
//  reimplementation, NOT a Golden-Master comparison against the native. It records what the .NET
//  model does so that a change to it is visible; it cannot and does not certify agreement with
//  pfw.dll.
//
//  Three tiers of assertion appear below, and each case says which tier it is in:
//
//    TIER 1 - TRACEABLE. The member census, the one-based select index, the three modify-style
//             constants and the removal-on-empty-replace rule are all evidenced by a locator in the
//             oracle exports. These would survive a paired recording unchanged.
//    TIER 2 - DECIDED, NOT MEASURED. The set-operator splitting rule, the single-space join for
//             append and prepend, the upper-case canonical introducer for a clause the input never
//             had, and the treat-append-of-empty-as-success rule are documented DESIGN DECISIONS in
//             the production file. The oracle would settle each; until it is captured, these
//             assertions pin the decision so that it cannot drift silently.
//    TIER 3 - STRUCTURAL. The byte-exact round trip and the geometry preservation are properties of
//             THIS implementation's design - retain the input's own text and splice - rather than
//             observations of the native. They are the strongest parity guarantee available from
//             this repository precisely because the native's reassembly is unobservable.
//
//  THE PROPERTY MOST WORTH PROTECTING
//  --------------------------------------------------------------------------------------------
//  The acceptance criterion for this file is BYTE-EXACT clause-level output, because the paging
//  rewriters splice sentinel identifiers into the result and a characterization recording compares
//  the generated statement character for character. A model that normalised a statement into a tree
//  and regenerated it would pass a "does it still parse" test and fail parity on whitespace,
//  keyword casing and comment placement. That is why so many cases below assert an exact string
//  rather than a parsed shape, and why the clear-then-restore case - which is literally the legacy
//  paging idiom [n_cst_thread_task_sqlquery.sru:L376 then :L360] - asserts identity with the input.
//
//  C-F SELF-AUDIT: every statement here is synthetic and names invented tables and columns. No
//  credential, connection string, password or captured production statement appears. This matters
//  in this file specifically: the where-clause setter is the SQL-injection site analysed in the
//  refactor plan, so the injection-shaped cases below use obviously synthetic payloads and assert
//  only that the model treats a clause as OPAQUE TEXT - which is the preserved legacy behaviour,
//  not an endorsement of it.
//
//  RULES POSITION: review_rules returns exactly one line, "No user rules provided.", so no
//  user-specified rule governs this file and none is invented. The enterprise-standard baseline
//  applies instead: deterministic, no I/O, no clock, no database, no shared mutable state, every
//  test independent. No performance property is asserted, because the repository publishes none.
// ==============================================================================================

using System.Reflection;

using PowerFramework.Persistence.Sql;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Characterization tests for the managed SQL <c>SELECT</c> statement model: what it accepts, what
/// it extracts, what the three modify styles do, and the source geometry it preserves.
/// </summary>
public sealed class SelectStatementModelTests
{
    /// <summary>The statement the golden-master fixture retrieves [<c>dw_sqlite.srd:L14</c>].</summary>
    private const string FixtureRetrieve = "SELECT * FROM COMPANY";

    /// <summary>A parsed model over <paramref name="sql"/>, with the parse asserted to have succeeded.</summary>
    private static SelectStatementModel Parsed(string sql)
    {
        SelectStatementModel model = new();

        Assert.True(model.Parse(sql), $"Expected to parse: {sql}");

        return model;
    }

    // ==========================================================================================
    //  The member census - n_sql.sru:L9-L49
    // ==========================================================================================

    /// <summary>
    /// The model publishes exactly the surface the native prototypes declare: 39 ported members plus
    /// the one static factory, with the two metadata members deliberately absent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TIER 1 - TRACEABLE. <c>n_sql.sru</c> declares 41 prototypes at <c>L9-L49</c>. Two are not
    /// ported - <c>copyright()</c> at <c>L9</c> and <c>getversion()</c> at <c>L10</c>, which report a
    /// vendor string and the pfw.dll build number and are framework-metadata boilerplate present on
    /// every PBNI class in the estate. Porting them would fabricate a version number for an assembly
    /// whose version already comes from <c>Directory.Build.props</c>. So 39 members ship, plus
    /// <c>ParseSql</c> reproducing <c>parsesql.srf:L10-L14</c>.
    /// </para>
    /// <para>
    /// The arithmetic is asserted rather than described because a member quietly added or dropped is
    /// invisible in review: 6 clause kinds x 3 operations x 2 arities is 36, plus
    /// <c>Parse</c>, <c>GetSql</c> and <c>GetSelectCount</c> is 39.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheModel_PublishesExactlyTheThirtyNinePortedMembersPlusTheFactory()
    {
        string[] clauses = ["Column", "Table", "Where", "Group", "Having", "Order"];

        MethodInfo[] instanceMembers = typeof(SelectStatementModel)
            .GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance)
            .Where(method => !method.IsSpecialName)
            .ToArray();

        Assert.Equal(39, instanceMembers.Length);

        foreach (string clause in clauses)
        {
            // Presence test, text accessor and modifier, each in both arities.
            Assert.Single(instanceMembers, m => m.Name == "Has" + clause && m.GetParameters().Length == 0);
            Assert.Single(instanceMembers, m => m.Name == "Has" + clause && m.GetParameters().Length == 1);
            Assert.Single(instanceMembers, m => m.Name == "Get" + clause && m.GetParameters().Length == 0);
            Assert.Single(instanceMembers, m => m.Name == "Get" + clause && m.GetParameters().Length == 1);
            Assert.Single(instanceMembers, m => m.Name == "Modify" + clause && m.GetParameters().Length == 2);
            Assert.Single(instanceMembers, m => m.Name == "Modify" + clause && m.GetParameters().Length == 3);
        }

        Assert.Single(instanceMembers, m => m.Name == "Parse");
        Assert.Single(instanceMembers, m => m.Name == "GetSql");
        Assert.Single(instanceMembers, m => m.Name == "GetSelectCount");

        // The two metadata prototypes are NOT ported.
        Assert.DoesNotContain("Copyright", instanceMembers.Select(m => m.Name));
        Assert.DoesNotContain("GetVersion", instanceMembers.Select(m => m.Name));

        // The factory, and nothing else, is static.
        MethodInfo[] staticMembers = typeof(SelectStatementModel)
            .GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Static)
            .ToArray();
        Assert.Single(staticMembers);
        Assert.Equal("ParseSql", staticMembers[0].Name);
    }

    /// <summary>
    /// The three modify styles are the shared kernel's constants, with the values the legacy declares.
    /// </summary>
    /// <remarks>
    /// TIER 1 - TRACEABLE. <c>enums.sru:L718-L720</c> declares replace as 1, append as 2 and prepend
    /// as 3. The constants are CONSUMED from <c>PowerFramework.Shared.Kernel.Enums</c> rather than
    /// redeclared here, because the repository-root <c>.editorconfig</c> scopes its CA1707 and IDE1006
    /// relaxations to ten named files and neither the model nor this suite is among them - a
    /// SCREAMING_SNAKE identifier declared locally would be a build error under warnings-as-errors.
    /// Asserted against literals rather than against the constants themselves, since comparing a
    /// constant to itself would pass for any value.
    /// </remarks>
    [Fact]
    public void TheThreeModifyStyles_CarryTheValuesTheLegacyConstantBlockDeclares()
    {
        Assert.Equal(1L, Enums.SQL_MS_REPLACE);
        Assert.Equal(2L, Enums.SQL_MS_APPEND);
        Assert.Equal(3L, Enums.SQL_MS_PREPEND);
    }

    // ==========================================================================================
    //  Parse acceptance
    // ==========================================================================================

    /// <summary>
    /// Every accepted statement round-trips BYTE FOR BYTE through parse and emit, whitespace,
    /// casing, comments and all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TIER 3 - STRUCTURAL, and the single most valuable property in this file. It is achieved by
    /// design rather than by normalising: the parse records the exact source geometry of every clause
    /// and an unmodified block re-emits its original text. The cases are chosen so that a
    /// regenerating implementation would fail at least one of them - irregular internal spacing,
    /// lower-case keywords, a doubled space inside <c>group  by</c>, a line comment whose terminator
    /// is a newline, a block comment containing a clause keyword, a trailing semicolon, and
    /// surrounding whitespace on both ends.
    /// </para>
    /// <para>
    /// Note the CTE case. A leading common-table expression's own <c>SELECT</c> sits inside
    /// parentheses and is therefore invisible to the depth-zero scan, so the whole expression is
    /// retained verbatim as the first block's prefix and never interpreted.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(FixtureRetrieve)]
    [InlineData("select id, name from company where age > 30 group by id having count(*) > 1 order by name")]
    [InlineData("  SELECT a FROM t  ")]
    [InlineData("SELECT   a  ,   b   FROM    t")]
    [InlineData("SeLeCt a FrOm t WhErE b=1")]
    [InlineData("SELECT a FROM t group  by a")]
    [InlineData("SELECT a FROM t ORDER BY a, b DESC")]
    [InlineData("SELECT a FROM t\nWHERE b = 1\nORDER BY a")]
    [InlineData("SELECT a FROM t;")]
    [InlineData("SELECT(A)FROM t")]
    [InlineData("SELECT a FROM (SELECT b FROM u WHERE b > 1) x")]
    [InlineData("WITH c AS (SELECT 1 AS n FROM dual) SELECT n FROM c")]
    [InlineData("SELECT 'from t where x' AS lit FROM t")]
    [InlineData("SELECT [from] FROM [where]")]
    [InlineData("SELECT \"order by\" FROM t")]
    [InlineData("SELECT a /* from u */ FROM t")]
    [InlineData("SELECT a -- from u\n FROM t")]
    [InlineData("SELECT a FROM t UNION SELECT b FROM u")]
    [InlineData("SELECT a FROM t UNION ALL SELECT b FROM u")]
    [InlineData("SELECT a FROM t UNION  ALL  SELECT b FROM u")]
    [InlineData("SELECT a FROM t INTERSECT SELECT b FROM u")]
    [InlineData("SELECT a FROM t EXCEPT SELECT b FROM u")]
    [InlineData("SELECT")]
    [InlineData("SELECT a")]
    public void AnAcceptedStatement_RoundTripsByteForByte(string sql)
    {
        SelectStatementModel model = Parsed(sql);

        Assert.Equal(sql, model.GetSql());
    }

    /// <summary>
    /// A statement whose parentheses, quotes, brackets or block comment are unbalanced is rejected,
    /// and rejection leaves the model EMPTY.
    /// </summary>
    /// <remarks>
    /// TIER 1 - TRACEABLE as a failure channel: the legacy consumer treats a false return as fatal,
    /// raising an internal-error event and abandoning the paging build
    /// [<c>n_cst_thread_task_sqlquery.sru:L314</c>]. The emptied state is what makes a caller that
    /// ignores the boolean still safe: the count is zero, every presence test is false, every accessor
    /// returns empty and every modifier reports failure. The method never throws.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SELECT a FROM (t")]
    [InlineData("SELECT a FROM t)")]
    [InlineData("SELECT 'unterminated FROM t")]
    [InlineData("SELECT a /* unterminated FROM t")]
    [InlineData("SELECT [unterminated FROM t")]
    [InlineData("SELECT \"unterminated FROM t")]
    public void AMalformedStatement_IsRejectedAndLeavesTheModelEmpty(string sql)
    {
        SelectStatementModel model = new();

        Assert.False(model.Parse(sql));
        Assert.Equal(0, model.GetSelectCount());
        Assert.Equal(string.Empty, model.GetSql());
        Assert.False(model.HasColumn());
        Assert.Equal(string.Empty, model.GetColumn());
        Assert.False(model.ModifyWhere(Enums.SQL_MS_REPLACE, "x=1"));
    }

    /// <summary>
    /// A statement whose first depth-zero introducer is not <c>SELECT</c>, or which has no introducer
    /// at all, is rejected - and so is a whole statement wrapped in parentheses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TIER 1 - TRACEABLE. <c>DELETE FROM t</c> is rejected because its first depth-zero introducer is
    /// <c>FROM</c>; <c>UPDATE</c> and <c>INSERT</c> are rejected because they contain no introducer at
    /// all; <c>(SELECT a FROM t)</c> is rejected because its <c>SELECT</c> is never at depth zero.
    /// </para>
    /// <para>
    /// A duplicated or out-of-order clause is rejected by the same rule that orders the six kinds: the
    /// introducers must appear in strictly increasing canonical order, which is what makes
    /// <c>WHERE ... WHERE</c> and <c>ORDER BY ... WHERE</c> both malformed.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("DELETE FROM t")]
    [InlineData("UPDATE t SET a = 1")]
    [InlineData("INSERT INTO t VALUES (1)")]
    [InlineData("FROM t")]
    [InlineData("(SELECT a FROM t)")]
    [InlineData("SELECT a FROM t WHERE b = 1 WHERE c = 2")]
    [InlineData("SELECT a FROM t ORDER BY a WHERE b = 1")]
    [InlineData("SELECT a FROM t GROUP BY a FROM u")]
    public void AStatementThatIsNotAWellOrderedSelect_IsRejected(string sql)
    {
        SelectStatementModel model = new();

        Assert.False(model.Parse(sql));
        Assert.Equal(0, model.GetSelectCount());
    }

    /// <summary>
    /// MEASURED, AND COUNTER-INTUITIVE: text before the first depth-zero <c>SELECT</c> is retained
    /// verbatim as a prefix and never interpreted, so a statement carrying arbitrary leading text is
    /// ACCEPTED.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TIER 3 - STRUCTURAL, and recorded here because it is the fact most likely to be mistaken for a
    /// defect. The mechanism exists for the common-table expression: a leading <c>WITH</c> clause's own
    /// <c>SELECT</c> is inside parentheses and therefore invisible to the depth-zero scan, so the whole
    /// expression must be carried along untouched by every modification or the paging rewrite would
    /// destroy it. That mechanism does not - and cannot - distinguish a CTE from any other leading text,
    /// so <c>"NOT A SELECT"</c> parses with <c>"NOT A "</c> as its prefix and an empty select list.
    /// </para>
    /// <para>
    /// THE PARSE IS THEREFORE NOT A VALIDATOR, and no caller should use it as one. Stated as an
    /// assertion rather than a comment so that a future "tighten the parse" change has to confront the
    /// CTE case it would break. The CTE case below is the one this behaviour exists for, and it is
    /// asserted in the same test so the two cannot be separated.
    /// </para>
    /// </remarks>
    [Fact]
    public void TextBeforeTheFirstSelect_IsCarriedVerbatimThroughEveryModification()
    {
        // The case this mechanism exists for.
        SelectStatementModel cte = Parsed("WITH c AS (SELECT 1 AS n FROM dual) SELECT n FROM c");
        Assert.Equal("n", cte.GetColumn());
        Assert.Equal("c", cte.GetTable());
        Assert.True(cte.ModifyColumn(Enums.SQL_MS_REPLACE, "Z"));
        Assert.Equal("WITH c AS (SELECT 1 AS n FROM dual) SELECT Z FROM c", cte.GetSql());

        // The same mechanism, applied to text that is not a CTE at all.
        SelectStatementModel prefixed = Parsed("garbage SELECT a FROM t");
        Assert.Equal("a", prefixed.GetColumn());
        Assert.True(prefixed.ModifyColumn(Enums.SQL_MS_REPLACE, "Z"));
        Assert.Equal("garbage SELECT Z FROM t", prefixed.GetSql());

        // And to a statement that is not a SELECT in any meaningful sense.
        SelectStatementModel notASelect = Parsed("NOT A SELECT");
        Assert.Equal(1, notASelect.GetSelectCount());
        Assert.True(notASelect.HasColumn());
        Assert.Equal(string.Empty, notASelect.GetColumn());
        Assert.False(notASelect.HasTable());
    }

    /// <summary>
    /// A null statement is rejected without throwing.
    /// </summary>
    /// <remarks>
    /// PowerScript strings are never null, so a null can only arrive from a nullable-oblivious caller.
    /// Reporting failure rather than throwing keeps the contract the legacy call sites rely on - none
    /// of them guards a parse call - and the factory behaves the same way.
    /// </remarks>
    [Fact]
    public void ANullStatement_IsRejectedWithoutThrowing()
    {
        SelectStatementModel model = new();

        Assert.False(model.Parse(null!));
        Assert.Equal(0, model.GetSelectCount());
        Assert.Equal(0, SelectStatementModel.ParseSql(null!).GetSelectCount());
    }

    /// <summary>
    /// A failed parse discards whatever the instance held before it.
    /// </summary>
    /// <remarks>
    /// The instance is not left holding a stale statement that a caller ignoring the boolean would then
    /// modify and emit. This is the behaviour that makes the fatal-on-false posture at
    /// <c>n_cst_thread_task_sqlquery.sru:L314</c> safe.
    /// </remarks>
    [Fact]
    public void AFailedReparse_DiscardsThePreviouslyParsedStatement()
    {
        SelectStatementModel model = Parsed("SELECT a FROM t WHERE b=1");

        Assert.False(model.Parse("UPDATE t SET a=1"));
        Assert.Equal(0, model.GetSelectCount());
        Assert.Equal(string.Empty, model.GetSql());
        Assert.False(model.HasWhere());
    }

    // ==========================================================================================
    //  Clause extraction
    // ==========================================================================================

    /// <summary>
    /// The six accessors extract the six clause bodies, trimmed at the edges and otherwise verbatim.
    /// </summary>
    /// <remarks>
    /// TIER 1 - TRACEABLE as a requirement: the legacy round-trips a clause through the accessor and
    /// back through the modifier - it saves the column list at
    /// <c>n_cst_thread_task_sqlquery.sru:L339</c> and restores it at <c>:L348</c>, and saves the
    /// order-by at <c>:L376</c> and restores it at <c>:L360</c> - so what comes out has to be exactly
    /// what goes back in. Internal whitespace is therefore preserved (<c>id ,  name</c> keeps both
    /// gaps) while the surrounding whitespace, which belongs to the statement's geometry rather than
    /// to the clause, is not returned.
    /// </remarks>
    [Fact]
    public void TheSixAccessors_ReturnTheClauseBodiesTrimmedAtTheEdgesOnly()
    {
        SelectStatementModel model = Parsed(
            "select id ,  name  from  company  where age > 30  group  by id  having count(*) > 1  order by name ");

        Assert.True(model.HasColumn());
        Assert.Equal("id ,  name", model.GetColumn());
        Assert.True(model.HasTable());
        Assert.Equal("company", model.GetTable());
        Assert.True(model.HasWhere());
        Assert.Equal("age > 30", model.GetWhere());
        Assert.True(model.HasGroup());
        Assert.Equal("id", model.GetGroup());
        Assert.True(model.HasHaving());
        Assert.Equal("count(*) > 1", model.GetHaving());
        Assert.True(model.HasOrder());
        Assert.Equal("name", model.GetOrder());
    }

    /// <summary>
    /// An ABSENT clause reports false and returns an empty string; a clause that is PRESENT BUT EMPTY
    /// reports true and returns an empty string.
    /// </summary>
    /// <remarks>
    /// The distinction the production <c>Present</c> flag exists for, and the reason presence is not
    /// inferred from the text being empty. Only a degenerate input produces present-and-empty -
    /// <c>SELECT A FROM T WHERE   ORDER BY B</c> has a bare <c>WHERE</c> keyword with no body - but the
    /// state has to exist for the presence tests to answer honestly, and a model that collapsed the two
    /// would emit a statement without the keyword and change the text.
    /// </remarks>
    [Fact]
    public void PresenceAndEmptiness_AreTwoDifferentStates()
    {
        SelectStatementModel bareKeyword = Parsed("SELECT A FROM T WHERE   ORDER BY B");

        Assert.True(bareKeyword.HasWhere());
        Assert.Equal(string.Empty, bareKeyword.GetWhere());
        Assert.False(bareKeyword.HasGroup());
        Assert.Equal(string.Empty, bareKeyword.GetGroup());

        // Present-and-empty survives the round trip WITH its keyword.
        Assert.Equal("SELECT A FROM T WHERE   ORDER BY B", bareKeyword.GetSql());

        // A select list is present even when the statement is nothing but the keyword.
        SelectStatementModel keywordOnly = Parsed("SELECT");
        Assert.True(keywordOnly.HasColumn());
        Assert.Equal(string.Empty, keywordOnly.GetColumn());
        Assert.False(keywordOnly.HasTable());
    }

    /// <summary>
    /// A clause keyword INSIDE parentheses, a quoted literal, a quoted or bracketed identifier or a
    /// comment is invisible to the scan.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The property that makes the model usable on real statements, and the one with the most ways to
    /// go wrong. Each case below puts a clause keyword somewhere it must NOT be recognised:
    /// </para>
    /// <list type="bullet">
    /// <item>inside a correlated subquery in the select list, and inside one in the WHERE clause;</item>
    /// <item>inside a string literal, where <c>from</c> and <c>where</c> are just characters;</item>
    /// <item>inside a bracketed and a double-quoted identifier;</item>
    /// <item>inside a block comment and after a line comment marker.</item>
    /// </list>
    /// <para>
    /// The subquery cases are the sharpest: the inner <c>ORDER BY</c> must not become the outer
    /// statement's order-by clause, and the outer one must still be found after it.
    /// </para>
    /// </remarks>
    [Fact]
    public void ClauseKeywordsInsideParenthesesLiteralsIdentifiersAndComments_AreNotRecognised()
    {
        SelectStatementModel correlated =
            Parsed("SELECT (SELECT MAX(b) FROM u WHERE u.k = t.k) AS m FROM t WHERE a = 1");
        Assert.Equal("(SELECT MAX(b) FROM u WHERE u.k = t.k) AS m", correlated.GetColumn());
        Assert.Equal("t", correlated.GetTable());
        Assert.Equal("a = 1", correlated.GetWhere());
        Assert.Equal(1, correlated.GetSelectCount());

        SelectStatementModel nested =
            Parsed("SELECT a FROM t WHERE a IN (SELECT b FROM u ORDER BY b) ORDER BY a");
        Assert.Equal("a IN (SELECT b FROM u ORDER BY b)", nested.GetWhere());
        Assert.Equal("a", nested.GetOrder());

        SelectStatementModel grouped =
            Parsed("SELECT a FROM t WHERE a IN (SELECT b FROM u GROUP BY b HAVING COUNT(*) > 1)");
        Assert.Equal("a IN (SELECT b FROM u GROUP BY b HAVING COUNT(*) > 1)", grouped.GetWhere());
        Assert.False(grouped.HasGroup());
        Assert.False(grouped.HasHaving());

        SelectStatementModel literal = Parsed("SELECT 'from t where x' AS lit FROM t");
        Assert.Equal("'from t where x' AS lit", literal.GetColumn());
        Assert.Equal("t", literal.GetTable());

        SelectStatementModel bracketed = Parsed("SELECT [from] FROM [where]");
        Assert.Equal("[from]", bracketed.GetColumn());
        Assert.Equal("[where]", bracketed.GetTable());

        SelectStatementModel quoted = Parsed("SELECT \"order by\" FROM t");
        Assert.Equal("\"order by\"", quoted.GetColumn());
        Assert.False(quoted.HasOrder());

        SelectStatementModel blockComment = Parsed("SELECT a /* from u */ FROM t");
        Assert.Equal("a /* from u */", blockComment.GetColumn());
        Assert.Equal("t", blockComment.GetTable());

        SelectStatementModel lineComment = Parsed("SELECT a -- from u\n FROM t");
        Assert.Equal("a -- from u", lineComment.GetColumn());
        Assert.Equal("t", lineComment.GetTable());

        SelectStatementModel doubled = Parsed("SELECT a FROM ((t))");
        Assert.Equal("((t))", doubled.GetTable());
    }

    // ==========================================================================================
    //  The one-based select index
    // ==========================================================================================

    /// <summary>
    /// The select index is ONE-BASED, the short arity addresses block one, and an index outside the
    /// range answers negatively rather than throwing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TIER 1 for the one-based index: the caller-side proxy passes a literal <c>1</c> at
    /// <c>n_cst_thread_task_sqlquery.sru:L691</c> and <c>:L698</c>. R9 applies - index 1 must address
    /// the FIRST block, and a rebase would make it address the second while leaving
    /// <c>GetSelectCount</c> unchanged.
    /// </para>
    /// <para>
    /// TIER 2 for the SPLITTING rule that produces more than one block: that the statement splits at
    /// all is proved by <c>getselectcount()</c> [<c>n_sql.sru:L13</c>] and by the index on every
    /// accessor, but the native's own splitting rule is inside pfw.dll. Splitting on the four set
    /// operators at depth zero is the documented reading. Worth keeping in proportion: every measured
    /// call site addresses block 1, so single-block behaviour is the only path with direct evidence.
    /// </para>
    /// <para>
    /// Zero and negative indices answer as "no such block" rather than throwing, which keeps the model
    /// from ever throwing at a legacy call site that carries no guard.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSelectIndex_IsOneBasedAndOutOfRangeIndicesAnswerNegatively()
    {
        SelectStatementModel model =
            Parsed("SELECT a FROM t WHERE a=1 UNION ALL SELECT b FROM u ORDER BY b");

        Assert.Equal(2, model.GetSelectCount());

        // Block one.
        Assert.True(model.HasColumn(1));
        Assert.Equal("a", model.GetColumn(1));
        Assert.Equal("t", model.GetTable(1));
        Assert.Equal("a=1", model.GetWhere(1));
        Assert.False(model.HasOrder(1));

        // Block two - a DIFFERENT block, which is what proves the index is not ignored.
        Assert.Equal("b", model.GetColumn(2));
        Assert.Equal("u", model.GetTable(2));
        Assert.False(model.HasWhere(2));
        Assert.Equal("b", model.GetOrder(2));

        // The short arity is block one.
        Assert.Equal(model.GetColumn(1), model.GetColumn());
        Assert.Equal(model.GetTable(1), model.GetTable());
        Assert.Equal(model.GetWhere(1), model.GetWhere());
        Assert.Equal(model.HasOrder(1), model.HasOrder());

        // Out of range in both directions, and never an exception.
        foreach (int index in new[] { -1, 0, 3, int.MaxValue, int.MinValue })
        {
            Assert.False(model.HasColumn(index));
            Assert.Equal(string.Empty, model.GetColumn(index));
            Assert.False(model.ModifyColumn(index, Enums.SQL_MS_REPLACE, "z"));
        }
    }

    /// <summary>
    /// Set operators split a statement into blocks, and <c>UNION ALL</c> is taken as one two-word
    /// operator rather than as <c>UNION</c> followed by a stray <c>ALL</c>. Oracle's <c>MINUS</c>
    /// is the fifth recognised operator and is covered, with the rest of the dialect matrix, by
    /// SelectStatementModelCompoundSelectTests.
    /// </summary>
    /// <remarks>
    /// TIER 2 - DECIDED. The two-word preference matters for the round trip: consuming only
    /// <c>UNION</c> would leave <c>ALL</c> at the head of the second block, where it would be emitted
    /// in a different place once that block was modified.
    /// </remarks>
    [Theory]
    [InlineData("SELECT a FROM t UNION SELECT b FROM u", 2)]
    [InlineData("SELECT a FROM t UNION ALL SELECT b FROM u", 2)]
    [InlineData("SELECT a FROM t INTERSECT SELECT b FROM u", 2)]
    [InlineData("SELECT a FROM t EXCEPT SELECT b FROM u", 2)]
    [InlineData("SELECT a FROM t UNION SELECT b FROM u UNION ALL SELECT c FROM v", 3)]
    [InlineData("SELECT a FROM t WHERE a IN (SELECT b FROM u UNION SELECT c FROM v)", 1)]
    [InlineData(FixtureRetrieve, 1)]
    public void TheSetOperators_SplitAtDepthZeroOnly(string sql, int expectedBlocks)
    {
        SelectStatementModel model = Parsed(sql);

        Assert.Equal(expectedBlocks, model.GetSelectCount());
        Assert.Equal(sql, model.GetSql());
    }

    /// <summary>
    /// Modifying one block of a multi-block statement leaves every other block byte-identical.
    /// </summary>
    /// <remarks>
    /// TIER 3 - STRUCTURAL. Each block emits verbatim while unmodified, so the blast radius of a
    /// modification is exactly one block. Asserted on the SECOND block specifically, because an
    /// implementation that resolved every index to the first block would still pass a test that only
    /// modified block one.
    /// </remarks>
    [Fact]
    public void ModifyingOneBlock_LeavesTheOtherBlocksUntouched()
    {
        SelectStatementModel model =
            Parsed("SELECT a FROM t WHERE a=1 UNION ALL SELECT b FROM u WHERE b=2");

        Assert.True(model.ModifyWhere(2, Enums.SQL_MS_APPEND, "AND c=3"));

        Assert.Equal(
            "SELECT a FROM t WHERE a=1 UNION ALL SELECT b FROM u WHERE b=2 AND c=3", model.GetSql());
        Assert.Equal("a=1", model.GetWhere(1));
    }

    // ==========================================================================================
    //  The three modify styles
    // ==========================================================================================

    /// <summary>
    /// Replace substitutes the clause text; append and prepend join to it with a single space.
    /// </summary>
    /// <remarks>
    /// TIER 2 for the SEPARATOR. The single space is the reasonable reading of an unobservable native
    /// detail - it is the minimum that keeps two SQL fragments lexically apart. The ORDER of the join
    /// is the part that must not be got backwards: append puts the new fragment after the existing
    /// text and prepend puts it before, and both readings produce valid SQL from the fragments the
    /// legacy passes, so only an explicit assertion distinguishes them.
    /// </remarks>
    [Theory]
    [InlineData(1L, "y=2", "SELECT a FROM t WHERE y=2 ORDER BY a")]
    [InlineData(2L, "AND y=2", "SELECT a FROM t WHERE x=1 AND y=2 ORDER BY a")]
    [InlineData(3L, "y=2 AND", "SELECT a FROM t WHERE y=2 AND x=1 ORDER BY a")]
    public void TheThreeStyles_ReplaceAppendAndPrependTheClauseText(
        long style,
        string fragment,
        string expected)
    {
        SelectStatementModel model = Parsed("SELECT a FROM t WHERE x=1 ORDER BY a");

        Assert.True(model.ModifyWhere(style, fragment));
        Assert.Equal(expected, model.GetSql());
    }

    /// <summary>
    /// REPLACE WITH EMPTY TEXT REMOVES the clause outright, taking its keyword and its preceding gap
    /// with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TIER 1 - TRACEABLE, from three independent locators that all annotate the call as stripping the
    /// clause [<c>n_cst_thread_task_sqlquery.sru:L377</c>, <c>:L390</c>, <c>:L832</c>], corroborated
    /// structurally twice over: <c>:L831</c> guards the call with a presence test first, and
    /// <c>:L356</c>, <c>:L382</c> and <c>:L834</c> all embed the result in a derived table, where a
    /// dangling <c>ORDER BY</c> would be rejected outright by SQL Server.
    /// </para>
    /// <para>
    /// No dangling keyword and no double space is the specific property asserted, because an
    /// implementation that cleared only the TEXT would leave <c>SELECT a FROM t ORDER BY a</c> as
    /// <c>SELECT a FROM t WHERE ORDER BY a</c> - still parseable by this model, and invalid SQL.
    /// </para>
    /// </remarks>
    [Fact]
    public void ReplacingAClauseWithEmptyText_RemovesTheClauseAndItsKeyword()
    {
        SelectStatementModel model = Parsed("SELECT a FROM t WHERE x=1 ORDER BY a");

        Assert.True(model.ModifyWhere(Enums.SQL_MS_REPLACE, string.Empty));

        Assert.Equal("SELECT a FROM t ORDER BY a", model.GetSql());
        Assert.False(model.HasWhere());
        Assert.Equal(string.Empty, model.GetWhere());
        Assert.DoesNotContain("WHERE", model.GetSql(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The removal rule applies uniformly to all six clause kinds, including the two whose removal
    /// leaves the statement invalid.
    /// </summary>
    /// <remarks>
    /// The faithful generalisation of a single native implementation: clearing the column or table
    /// clause yields text that is no longer a well-formed <c>SELECT</c>, and that is the CALLER's
    /// responsibility exactly as it is in the legacy, whose native carries no guard against it either.
    /// Adding a guard here would be a behaviour change dressed up as robustness, so the degenerate
    /// results are asserted rather than prevented.
    /// </remarks>
    [Fact]
    public void TheRemovalRule_AppliesToAllSixClauseKindsIncludingTheDegenerateOnes()
    {
        const string source = "SELECT a FROM t WHERE b=1 GROUP BY a HAVING c>1 ORDER BY a";

        SelectStatementModel order = Parsed(source);
        Assert.True(order.ModifyOrder(Enums.SQL_MS_REPLACE, string.Empty));
        Assert.Equal("SELECT a FROM t WHERE b=1 GROUP BY a HAVING c>1", order.GetSql());

        SelectStatementModel having = Parsed(source);
        Assert.True(having.ModifyHaving(Enums.SQL_MS_REPLACE, string.Empty));
        Assert.Equal("SELECT a FROM t WHERE b=1 GROUP BY a ORDER BY a", having.GetSql());

        SelectStatementModel group = Parsed(source);
        Assert.True(group.ModifyGroup(Enums.SQL_MS_REPLACE, string.Empty));
        Assert.Equal("SELECT a FROM t WHERE b=1 HAVING c>1 ORDER BY a", group.GetSql());

        // The two degenerate ones. Not prevented, and not tidied.
        SelectStatementModel table = Parsed("SELECT a FROM t WHERE b=1");
        Assert.True(table.ModifyTable(Enums.SQL_MS_REPLACE, string.Empty));
        Assert.Equal("SELECT a WHERE b=1", table.GetSql());

        SelectStatementModel column = Parsed("SELECT a FROM t WHERE b=1");
        Assert.True(column.ModifyColumn(Enums.SQL_MS_REPLACE, string.Empty));
        Assert.Equal(" FROM t WHERE b=1", column.GetSql());
    }

    /// <summary>
    /// APPEND OR PREPEND OF EMPTY TEXT is a no-op that reports SUCCESS.
    /// </summary>
    /// <remarks>
    /// TIER 2, but a MEASURED REQUIREMENT rather than a convenience.
    /// <c>n_cst_thread_task_sqlquery.sru:L341</c> appends <c>sUniqueColumnsOrderBy</c> to the order-by
    /// clause, and that string is legitimately empty whenever every unique-index column already appears
    /// in the existing order-by - the loop at <c>:L335-L338</c> only adds to it when one does not. So
    /// "nothing to add, nothing went wrong" is the reading that keeps that call site working. What must
    /// NOT happen here is the non-positive-index-or-empty-clause rejection the CALLER applies at
    /// <c>:L269</c> and <c>:L286</c>: that guard belongs to the layer above, and reproducing it here
    /// would turn <c>:L341</c> into a failure.
    /// </remarks>
    [Theory]
    [InlineData(2L)]
    [InlineData(3L)]
    public void AppendingOrPrependingEmptyText_ChangesNothingAndReportsSuccess(long style)
    {
        const string source = "SELECT a FROM t WHERE x=1 ORDER BY a";
        SelectStatementModel model = Parsed(source);

        Assert.True(model.ModifyOrder(style, string.Empty));

        Assert.Equal(source, model.GetSql());
        Assert.True(model.HasOrder());
        Assert.Equal("a", model.GetOrder());
    }

    /// <summary>
    /// Appending or prepending to an ABSENT or EMPTY clause sets the text with no separator at all.
    /// </summary>
    /// <remarks>
    /// A leading or trailing space would otherwise appear in the emitted clause and in every
    /// characterization comparison of it. The absent case additionally has to introduce the clause,
    /// which is what makes append indistinguishable from replace when there is nothing to join to.
    /// </remarks>
    [Fact]
    public void AppendingToAnAbsentOrEmptyClause_SetsTheTextWithNoSeparator()
    {
        SelectStatementModel absent = Parsed("SELECT a FROM t");
        Assert.True(absent.ModifyOrder(Enums.SQL_MS_APPEND, "a"));
        Assert.Equal("a", absent.GetOrder());
        Assert.Equal("SELECT a FROM t ORDER BY a", absent.GetSql());

        SelectStatementModel alsoAbsent = Parsed("SELECT a FROM t");
        Assert.True(alsoAbsent.ModifyOrder(Enums.SQL_MS_PREPEND, "a"));
        Assert.Equal("a", alsoAbsent.GetOrder());

        SelectStatementModel bare = Parsed("SELECT A FROM T WHERE   ORDER BY B");
        Assert.True(bare.ModifyWhere(Enums.SQL_MS_APPEND, "x=1"));
        Assert.Equal("x=1", bare.GetWhere());
    }

    /// <summary>
    /// An UNRECOGNISED style changes nothing and reports failure.
    /// </summary>
    /// <remarks>
    /// TIER 1 as a failure channel: the legacy caller treats a false return as an error at
    /// <c>n_cst_thread_task_sqlquery.sru:L691</c> and <c>:L698</c>. Zero is included among the cases
    /// deliberately - it is not one of the three declared styles, and it is the value an
    /// uninitialised variable would carry.
    /// </remarks>
    [Theory]
    [InlineData(0L)]
    [InlineData(4L)]
    [InlineData(99L)]
    [InlineData(-1L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void AnUnrecognisedStyle_ChangesNothingAndReportsFailure(long style)
    {
        const string source = "SELECT a FROM t WHERE x=1 ORDER BY a";
        SelectStatementModel model = Parsed(source);

        Assert.False(model.ModifyWhere(style, "y=2"));

        Assert.Equal(source, model.GetSql());
        Assert.Equal("x=1", model.GetWhere());
    }

    /// <summary>
    /// A null clause value is coerced to empty, so replace REMOVES the clause and append does nothing -
    /// and neither throws.
    /// </summary>
    /// <remarks>
    /// Defensive rather than expected: the parameter is non-nullable and PowerScript strings are never
    /// null, so a null can only arrive from a nullable-oblivious caller. Coercing keeps the model from
    /// ever throwing, which is the contract the legacy call sites rely on - none of them guards a
    /// modify call. The CONSEQUENCE is worth knowing and is therefore asserted: passing null to
    /// replace deletes the clause rather than leaving it alone.
    /// </remarks>
    [Fact]
    public void ANullClauseValue_IsCoercedToEmptyRatherThanThrowing()
    {
        SelectStatementModel replacing = Parsed("SELECT a FROM t WHERE x=1");
        Assert.True(replacing.ModifyWhere(Enums.SQL_MS_REPLACE, null!));
        Assert.Equal("SELECT a FROM t", replacing.GetSql());
        Assert.False(replacing.HasWhere());

        SelectStatementModel appending = Parsed("SELECT a FROM t WHERE x=1");
        Assert.True(appending.ModifyWhere(Enums.SQL_MS_APPEND, null!));
        Assert.Equal("SELECT a FROM t WHERE x=1", appending.GetSql());
    }

    /// <summary>
    /// All six clause kinds accept all three styles through both arities, and the indexed arity agrees
    /// with the short one.
    /// </summary>
    /// <remarks>
    /// The 36-member surface is generated from one core, so the risk is a single member wired to the
    /// wrong clause kind - a copy-and-paste slip that no compiler catches because every signature is
    /// identical. Each kind is therefore modified in isolation and the OTHER five are asserted
    /// unchanged, which is what localises such a slip.
    /// </remarks>
    [Fact]
    public void EveryClauseKind_IsWiredToItsOwnSlotInBothArities()
    {
        const string source = "SELECT a FROM t WHERE b=1 GROUP BY c HAVING d>1 ORDER BY e";

        (string Kind, Func<SelectStatementModel, bool> Short, Func<SelectStatementModel, bool> Indexed,
            Func<SelectStatementModel, string> Read)[] cases =
        [
            ("Column", m => m.ModifyColumn(Enums.SQL_MS_REPLACE, "Z"),
                m => m.ModifyColumn(1, Enums.SQL_MS_REPLACE, "Z"), m => m.GetColumn()),
            ("Table", m => m.ModifyTable(Enums.SQL_MS_REPLACE, "Z"),
                m => m.ModifyTable(1, Enums.SQL_MS_REPLACE, "Z"), m => m.GetTable()),
            ("Where", m => m.ModifyWhere(Enums.SQL_MS_REPLACE, "Z"),
                m => m.ModifyWhere(1, Enums.SQL_MS_REPLACE, "Z"), m => m.GetWhere()),
            ("Group", m => m.ModifyGroup(Enums.SQL_MS_REPLACE, "Z"),
                m => m.ModifyGroup(1, Enums.SQL_MS_REPLACE, "Z"), m => m.GetGroup()),
            ("Having", m => m.ModifyHaving(Enums.SQL_MS_REPLACE, "Z"),
                m => m.ModifyHaving(1, Enums.SQL_MS_REPLACE, "Z"), m => m.GetHaving()),
            ("Order", m => m.ModifyOrder(Enums.SQL_MS_REPLACE, "Z"),
                m => m.ModifyOrder(1, Enums.SQL_MS_REPLACE, "Z"), m => m.GetOrder()),
        ];

        foreach ((string kind, var shortArity, var indexedArity, var read) in cases)
        {
            SelectStatementModel viaShort = Parsed(source);
            Assert.True(shortArity(viaShort), kind);
            Assert.Equal("Z", read(viaShort));

            SelectStatementModel viaIndexed = Parsed(source);
            Assert.True(indexedArity(viaIndexed), kind);
            Assert.Equal("Z", read(viaIndexed));

            // Both arities produce the identical statement.
            Assert.Equal(viaShort.GetSql(), viaIndexed.GetSql());

            // Exactly one clause changed.
            int changed = cases.Count(other => read == other.Read || other.Read(viaShort) == "Z");
            Assert.Equal(1, changed);
        }
    }

    // ==========================================================================================
    //  Source geometry - the reason parity is achievable at all
    // ==========================================================================================

    /// <summary>
    /// A modified clause is spliced into the statement's OWN geometry: the introducer keeps its source
    /// spelling and both surrounding gaps are reused exactly.
    /// </summary>
    /// <remarks>
    /// TIER 3 - STRUCTURAL. A lower-case <c>where</c> stays lower case and a doubled space inside
    /// <c>group  by</c> stays doubled, because the introducer is stored as the input spelled it rather
    /// than canonicalised. An implementation that emitted the canonical upper-case keyword for a clause
    /// it had modified would produce valid SQL that differs from the legacy's byte for byte.
    /// </remarks>
    [Fact]
    public void AModifiedClause_KeepsTheIntroducerSpellingAndSpacingTheInputUsed()
    {
        SelectStatementModel lowerCase =
            Parsed("select a from t where b=1 group  by a having c>1 order   by a");
        Assert.True(lowerCase.ModifyWhere(Enums.SQL_MS_REPLACE, "b=2"));
        Assert.Equal(
            "select a from t where b=2 group  by a having c>1 order   by a", lowerCase.GetSql());

        SelectStatementModel doubledSpace = Parsed("select a from t where b=1 group  by a");
        Assert.True(doubledSpace.ModifyGroup(Enums.SQL_MS_REPLACE, "z"));
        Assert.Equal("select a from t where b=1 group  by z", doubledSpace.GetSql());
    }

    /// <summary>
    /// A clause the input never had is introduced with the UPPER-CASE canonical keyword, in canonical
    /// position.
    /// </summary>
    /// <remarks>
    /// TIER 2 - DECIDED. There is no source geometry to reuse, so a keyword has to be chosen; upper
    /// case is chosen because every SQL fragment the legacy paging routine composes is upper case, from
    /// the <c>ROW_NUMBER() OVER (ORDER BY ...)</c> column at
    /// <c>n_cst_thread_task_sqlquery.sru:L355</c> to the <c>INNER JOIN</c> table fragment at
    /// <c>:L350</c>. That is a documented interpretation of an unobservable native detail, not a
    /// measurement. CANONICAL POSITION is the part that is not a matter of taste: a
    /// <c>GROUP BY</c> introduced into a statement that already has an <c>ORDER BY</c> must land
    /// BEFORE it or the result is invalid SQL.
    /// </remarks>
    [Fact]
    public void AClauseTheInputNeverHad_IsIntroducedInCanonicalPositionWithAnUpperCaseKeyword()
    {
        SelectStatementModel model = Parsed("select a from t where x=1 order by a");

        Assert.True(model.ModifyGroup(Enums.SQL_MS_REPLACE, "a"));
        Assert.Equal("select a from t where x=1 GROUP BY a order by a", model.GetSql());

        Assert.True(model.ModifyHaving(Enums.SQL_MS_REPLACE, "c>1"));
        Assert.Equal("select a from t where x=1 GROUP BY a HAVING c>1 order by a", model.GetSql());
    }

    /// <summary>
    /// THE PAGING IDIOM: saving a clause, clearing it and restoring it returns the statement to the
    /// input byte for byte.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TIER 3 - STRUCTURAL, and the single case that most directly protects the production workload.
    /// The legacy does exactly this during a paging build: it saves the order-by at
    /// <c>n_cst_thread_task_sqlquery.sru:L376</c>, clears it so the statement can be embedded in a
    /// derived table, and restores it at <c>:L360</c>.
    /// </para>
    /// <para>
    /// The source geometry is retained ON THE SLOT even while the clause is absent, which is what makes
    /// the restore exact rather than approximate. The fixture here is deliberately awkward - a
    /// lower-case doubled-space <c>order  by</c>, irregular gaps and a trailing space - because a
    /// canonicalising implementation round-trips a tidy statement perfectly and fails this one.
    /// </para>
    /// </remarks>
    [Fact]
    public void SavingClearingAndRestoringAClause_ReturnsTheStatementToTheInputExactly()
    {
        const string source = "SELECT a  FROM t   order  by  a DESC ";
        SelectStatementModel model = Parsed(source);

        string saved = model.GetOrder();
        Assert.Equal("a DESC", saved);

        Assert.True(model.ModifyOrder(Enums.SQL_MS_REPLACE, string.Empty));
        Assert.Equal("SELECT a  FROM t ", model.GetSql());

        Assert.True(model.ModifyOrder(Enums.SQL_MS_REPLACE, saved));
        Assert.Equal(source, model.GetSql());
    }

    /// <summary>
    /// A clause removed and then re-added is re-introduced in the input's own spelling, not the
    /// canonical one.
    /// </summary>
    /// <remarks>
    /// The distinction between "absent because it was removed" and "absent because the input never had
    /// it": the first retains its geometry and the second has none. Both are asserted in one test
    /// because the difference between them is exactly what a simplification would erase.
    /// </remarks>
    [Fact]
    public void AClauseRemovedAndReAdded_ReusesItsOriginalSpellingWhileANewOneDoesNot()
    {
        SelectStatementModel removed = Parsed("select a from t order  by a");
        Assert.True(removed.ModifyOrder(Enums.SQL_MS_REPLACE, string.Empty));
        Assert.Equal("select a from t", removed.GetSql());
        Assert.True(removed.ModifyOrder(Enums.SQL_MS_APPEND, "b"));
        Assert.Equal("select a from t order  by b", removed.GetSql());

        SelectStatementModel neverHad = Parsed("select a from t");
        Assert.True(neverHad.ModifyOrder(Enums.SQL_MS_APPEND, "b"));
        Assert.Equal("select a from t ORDER BY b", neverHad.GetSql());
    }

    /// <summary>
    /// Clearing EVERY clause leaves an empty statement while the block count stays at one, so an empty
    /// emission does not mean "not parsed".
    /// </summary>
    /// <remarks>
    /// MEASURED, and worth pinning because the two zero-ish states are easy to conflate.
    /// <c>GetSelectCount</c> is zero exactly when the PARSE failed; an empty <c>GetSql</c> can also
    /// arise from a caller having cleared everything. A caller detecting parse failure must therefore
    /// use the count, not the text - which is what the factory's documentation says.
    /// </remarks>
    [Fact]
    public void ClearingEveryClause_EmptiesTheTextWithoutResettingTheBlockCount()
    {
        SelectStatementModel model = Parsed("SELECT a FROM t WHERE b=1 GROUP BY a HAVING c>1 ORDER BY a");

        Assert.True(model.ModifyColumn(Enums.SQL_MS_REPLACE, string.Empty));
        Assert.True(model.ModifyTable(Enums.SQL_MS_REPLACE, string.Empty));
        Assert.True(model.ModifyWhere(Enums.SQL_MS_REPLACE, string.Empty));
        Assert.True(model.ModifyGroup(Enums.SQL_MS_REPLACE, string.Empty));
        Assert.True(model.ModifyHaving(Enums.SQL_MS_REPLACE, string.Empty));
        Assert.True(model.ModifyOrder(Enums.SQL_MS_REPLACE, string.Empty));

        Assert.Equal(string.Empty, model.GetSql());
        Assert.Equal(1, model.GetSelectCount());

        // And it can be rebuilt from nothing.
        Assert.True(model.ModifyColumn(Enums.SQL_MS_REPLACE, "z"));
        Assert.Equal("SELECT z", model.GetSql());
    }

    /// <summary>
    /// Emission is a PURE READ: repeated calls agree and none disturbs the model.
    /// </summary>
    /// <remarks>
    /// The legacy calls it eight times during one paging build
    /// [<c>n_cst_thread_task_sqlquery.sru:L346,L356,L364,L373,L382,L394,L703,L834</c>], so an emission
    /// with a side effect would corrupt the statement partway through that sequence.
    /// </remarks>
    [Fact]
    public void Emission_IsIdempotentAndDoesNotDisturbTheModel()
    {
        SelectStatementModel model = Parsed("SELECT a FROM t WHERE b=1");

        Assert.Equal(model.GetSql(), model.GetSql());

        string before = model.GetSql();
        Assert.True(model.ModifyWhere(Enums.SQL_MS_APPEND, "AND c=2"));
        string after = model.GetSql();

        Assert.Equal(after, model.GetSql());
        Assert.NotEqual(before, after);
        Assert.Equal("b=1 AND c=2", model.GetWhere());
        Assert.Equal("b=1 AND c=2", model.GetWhere());
    }

    // ==========================================================================================
    //  The factory - parsesql.srf, defects included
    // ==========================================================================================

    /// <summary>
    /// PRESERVED LEGACY DEFECT: the factory DISCARDS the parse result, so it always returns a live
    /// instance and a caller has to interrogate the model to learn whether parsing succeeded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The legacy factory is three lines - create, parse, return - throwing the boolean away
    /// [<c>parsesql.srf:L10-L14</c>]. A malformed statement therefore yields a live but UNPARSED
    /// object rather than a null or an exception. Reproduced exactly, and deliberately NOT corrected:
    /// the method does not throw, does not return null and is not reshaped into a try-pattern, because
    /// every one of those would be a behaviour change dressed up as robustness.
    /// </para>
    /// <para>
    /// The companion defect has NO managed analogue and is deliberately not reproduced: the legacy
    /// factory never destroys the object, so ownership transfers silently to the caller, who must
    /// <c>Destroy</c> it - which the one measured consumer does remember to do
    /// [<c>n_cst_thread_task_sqlquery.sru:L401</c>, <c>:L874</c>]. In .NET the lifetime belongs to the
    /// collector, so there is nothing to reproduce, and <see cref="IDisposable"/> is deliberately NOT
    /// implemented to "mirror" the destroy. The asymmetry is the point: the observable defect is
    /// preserved and the unobservable one is not.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFactory_DiscardsTheParseResultAndAlwaysReturnsALiveInstance()
    {
        SelectStatementModel malformed = SelectStatementModel.ParseSql("SELECT a FROM (t");

        Assert.Equal(0, malformed.GetSelectCount());
        Assert.Equal(string.Empty, malformed.GetSql());
        Assert.False(malformed.HasColumn());

        SelectStatementModel wellFormed = SelectStatementModel.ParseSql(FixtureRetrieve);

        Assert.Equal(1, wellFormed.GetSelectCount());
        Assert.Equal(FixtureRetrieve, wellFormed.GetSql());

        // No disposal contract is invented for a type that owns no unmanaged resource.
        Assert.DoesNotContain(typeof(IDisposable), typeof(SelectStatementModel).GetInterfaces());
        Assert.DoesNotContain(typeof(IAsyncDisposable), typeof(SelectStatementModel).GetInterfaces());
    }

    /// <summary>
    /// The factory produces a fresh, independent instance on every call.
    /// </summary>
    /// <remarks>
    /// Matching the legacy, which creates a new <c>n_sql</c> at
    /// <c>n_cst_thread_task_sqlquery.sru:L312</c> and again at <c>:L531</c>. The model holds no static
    /// mutable state, no singleton and no cached parser, which matters because the legacy SQL task
    /// layer is worker-thread-affine and shared parser state would be a concurrency defect the legacy
    /// does not have.
    /// </remarks>
    [Fact]
    public void EveryInstance_IsIndependentOfEveryOther()
    {
        SelectStatementModel first = SelectStatementModel.ParseSql("SELECT a FROM t WHERE x=1");
        SelectStatementModel second = SelectStatementModel.ParseSql("SELECT b FROM u");
        SelectStatementModel untouched = new();

        Assert.NotSame(first, second);
        Assert.True(first.ModifyWhere(Enums.SQL_MS_REPLACE, "y=2"));

        Assert.Equal("SELECT a FROM t WHERE y=2", first.GetSql());
        Assert.Equal("SELECT b FROM u", second.GetSql());
        Assert.Equal(0, untouched.GetSelectCount());

        FieldInfo[] mutableStatics = typeof(SelectStatementModel)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => !field.IsLiteral && !field.IsInitOnly)
            .ToArray();
        Assert.Empty(mutableStatics);
    }

    // ==========================================================================================
    //  The fixture, and the shapes the paging layer builds on top of this model
    // ==========================================================================================

    /// <summary>
    /// The golden-master fixture's own retrieve statement parses, extracts and rewrites as the paging
    /// layer needs.
    /// </summary>
    /// <remarks>
    /// <c>retrieve="SELECT * FROM COMPANY"</c> [<c>dw_sqlite.srd:L14</c>] is the statement every
    /// characterization recording of the retrieve / validate / update triple starts from. The
    /// count-wrapper shape is the one the legacy builds at
    /// <c>n_cst_thread_task_sqlquery.sru:L830</c>, where the select list is replaced with the literal
    /// alias <c>1 AS _</c> - asserted verbatim because the alias is one of the observable sentinels
    /// parity is measured on.
    /// </remarks>
    [Fact]
    public void TheFixtureRetrieveStatement_SupportsTheShapesThePagingLayerBuilds()
    {
        SelectStatementModel model = Parsed(FixtureRetrieve);

        Assert.Equal("*", model.GetColumn());
        Assert.Equal("COMPANY", model.GetTable());
        Assert.False(model.HasWhere());
        Assert.False(model.HasOrder());

        // The count wrapper - n_cst_thread_task_sqlquery.sru:L830.
        Assert.True(model.ModifyColumn(Enums.SQL_MS_REPLACE, "1 AS _"));
        Assert.Equal("SELECT 1 AS _ FROM COMPANY", model.GetSql());

        // A filtered, sorted retrieve built from the same statement.
        SelectStatementModel filtered = Parsed(FixtureRetrieve);
        Assert.True(filtered.ModifyWhere(Enums.SQL_MS_REPLACE, "age > 30"));
        Assert.True(filtered.ModifyOrder(Enums.SQL_MS_REPLACE, "age ASC"));
        Assert.Equal("SELECT * FROM COMPANY WHERE age > 30 ORDER BY age ASC", filtered.GetSql());
    }

    /// <summary>
    /// A clause is treated as OPAQUE TEXT and is never re-parsed, escaped or validated - which is the
    /// mechanical root of the legacy's SQL-injection exposure, preserved deliberately.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C-B. The where-clause setter takes a RAW clause string and splices it in
    /// [<c>n_cst_thread_task_sqlquery.sru:L269-L284</c>, applied at <c>:L691</c>], and the exposure is
    /// mechanical rather than theoretical: <c>DisableBind=1</c> in the connection parameters means the
    /// runtime does not use bind variables at all
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L128-L129</c>], so clause
    /// text is interpolated literally.
    /// </para>
    /// <para>
    /// This model deliberately does NOT escape, quote, reject or normalise a clause, because the
    /// observable generated statement must match the legacy's byte for byte. The remediation lives
    /// where it can be applied without changing that output - parameterized commands inside the
    /// execution layer - and the defect is documented at each site rather than silently fixed here.
    /// The payloads below are obviously synthetic, and what is asserted is only that the text passes
    /// through UNCHANGED.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("1=1 OR 'a'='a'")]
    [InlineData("x = 'O''Brien'")]
    [InlineData("x = 1 -- trailing comment")]
    [InlineData("x IN (SELECT y FROM other)")]
    public void AClauseIsOpaqueText_NeitherEscapedNorValidated(string clause)
    {
        SelectStatementModel model = Parsed(FixtureRetrieve);

        Assert.True(model.ModifyWhere(Enums.SQL_MS_REPLACE, clause));

        Assert.Equal(clause, model.GetWhere());
        Assert.Equal("SELECT * FROM COMPANY WHERE " + clause, model.GetSql());
    }

    // ==========================================================================================
    //  Scanner and emission edge cases
    // ==========================================================================================

    /// <summary>
    /// A set operator followed by a segment carrying no depth-zero clause introducer is rejected, and
    /// so is one that ends the statement.
    /// </summary>
    /// <remarks>
    /// The complement of the block-splitting rule: splitting produces segments, and every segment has
    /// to be a select block in its own right. A trailing operator, an operator followed by a bare
    /// identifier, and an operator followed by a PARENTHESISED select - whose own <c>SELECT</c> is
    /// never at depth zero - all fail. The last is the same rule that rejects a whole statement wrapped
    /// in parentheses, applied one segment in.
    /// </remarks>
    [Theory]
    [InlineData("SELECT a FROM t UNION x")]
    [InlineData("SELECT a FROM t UNION ")]
    [InlineData("SELECT a FROM t UNION")]
    [InlineData("SELECT a FROM t UNION(SELECT b FROM u)")]
    [InlineData("SELECT a FROM t UNION (SELECT b FROM u)")]
    [InlineData("SELECT a FROM t INTERSECT")]
    [InlineData("SELECT a FROM t EXCEPT ")]
    public void ASegmentWithNoClauseIntroducer_IsRejected(string sql)
    {
        SelectStatementModel model = new();

        Assert.False(model.Parse(sql));
        Assert.Equal(0, model.GetSelectCount());
    }

    /// <summary>
    /// A DOUBLED closer inside a quoted literal or a quoted or bracketed identifier is an ESCAPE, not a
    /// terminator.
    /// </summary>
    /// <remarks>
    /// The standard SQL escape in all three quoting styles: <c>''</c> inside a string,
    /// <c>]]</c> inside a bracketed identifier and <c>""</c> inside a quoted one. Treating the first
    /// closer as terminal would end the literal early and expose its remainder to the introducer scan -
    /// so a name like <c>O'Brien</c> would silently split a clause. Each case is also asserted to round
    /// trip, which is what proves the scan resumed in the right place rather than merely surviving.
    /// </remarks>
    [Theory]
    [InlineData("SELECT 'O''Brien' FROM t", "'O''Brien'")]
    [InlineData("SELECT 'a''' FROM t", "'a'''")]
    [InlineData("SELECT [a]]b] FROM t", "[a]]b]")]
    [InlineData("SELECT \"a\"\"b\" FROM t", "\"a\"\"b\"")]
    [InlineData("SELECT 'from t' , 'where x' FROM t", "'from t' , 'where x'")]
    public void ADoubledCloser_IsAnEscapeRatherThanATerminator(string sql, string expectedColumn)
    {
        SelectStatementModel model = Parsed(sql);

        Assert.Equal(expectedColumn, model.GetColumn());
        Assert.Equal("t", model.GetTable());
        Assert.Equal(sql, model.GetSql());
    }

    /// <summary>
    /// A clause keyword that runs on into an identifier is NOT an introducer, so the longer word is
    /// absorbed into the preceding clause.
    /// </summary>
    /// <remarks>
    /// Word-boundary matching, without which <c>FROMAGE</c> would introduce a <c>FROM</c> clause and
    /// <c>WHEREVER</c> a <c>WHERE</c> clause. The consequence is asserted rather than just the negative:
    /// the run-on word ends up inside whichever clause was open, which is the faithful reading - the
    /// model does not know it is a word rather than a keyword, only that it is not the keyword.
    /// </remarks>
    [Theory]
    [InlineData("SELECT a FROMAGE b", "a FROMAGE b", "")]
    [InlineData("SELECT a FROM t WHEREVER x", "a", "t WHEREVER x")]
    [InlineData("SELECT a FROM t GROUPS BY a", "a", "t GROUPS BY a")]
    [InlineData("SELECT a FROM t ORDERLY BY a", "a", "t ORDERLY BY a")]
    [InlineData("SELECT a FROM t HAVINGS c", "a", "t HAVINGS c")]
    public void AKeywordRunningOnIntoAnIdentifier_IsNotAnIntroducer(
        string sql,
        string expectedColumn,
        string expectedTable)
    {
        SelectStatementModel model = Parsed(sql);

        Assert.Equal(expectedColumn, model.GetColumn());
        Assert.Equal(expectedTable, model.GetTable());
        Assert.False(model.HasWhere());
        Assert.False(model.HasGroup());
        Assert.False(model.HasHaving());
        Assert.False(model.HasOrder());
        Assert.Equal(sql, model.GetSql());
    }

    /// <summary>
    /// A two-word introducer requires white space BETWEEN its words and requires its second word to be
    /// present and correct.
    /// </summary>
    /// <remarks>
    /// <c>GROUP BY</c> and <c>ORDER BY</c> are the only two-word introducers, and three ways of getting
    /// them wrong are distinguished: no gap at all (<c>GROUP,BY</c>), a gap followed by the wrong word
    /// (<c>GROUP X</c>, <c>GROUP BYE</c>), and the first word at the very end of the statement. None
    /// introduces a clause, and each leaves the text inside the preceding clause - so the statement
    /// still parses and still round-trips, which is the behaviour a caller would see.
    /// </remarks>
    [Theory]
    [InlineData("SELECT a FROM t GROUP,BY a", "t GROUP,BY a")]
    [InlineData("SELECT a FROM t ORDER,BY a", "t ORDER,BY a")]
    [InlineData("SELECT a FROM t GROUP X", "t GROUP X")]
    [InlineData("SELECT a FROM t ORDER X", "t ORDER X")]
    [InlineData("SELECT a FROM t GROUP", "t GROUP")]
    [InlineData("SELECT a FROM t ORDER", "t ORDER")]
    [InlineData("SELECT a FROM t GROUP BYE a", "t GROUP BYE a")]
    public void ATwoWordIntroducer_NeedsAGapAndTheRightSecondWord(string sql, string expectedTable)
    {
        SelectStatementModel model = Parsed(sql);

        Assert.Equal(expectedTable, model.GetTable());
        Assert.False(model.HasGroup());
        Assert.False(model.HasOrder());
        Assert.Equal(sql, model.GetSql());
    }

    /// <summary>
    /// A line comment ends at a carriage return, at a line feed, or at the end of the statement.
    /// </summary>
    /// <remarks>
    /// All three terminators matter for the round trip. A comment terminated only by a carriage return -
    /// a legitimately old-fashioned but perfectly legal line ending - must not swallow the rest of the
    /// statement, and a comment running to the very end of the text must not run off it.
    /// </remarks>
    [Theory]
    [InlineData("SELECT a -- c\r FROM t", "a -- c", "t")]
    [InlineData("SELECT a -- c\r\n FROM t", "a -- c", "t")]
    [InlineData("SELECT a -- c\n FROM t", "a -- c", "t")]
    [InlineData("SELECT a FROM t -- trailing", "a", "t -- trailing")]
    public void ALineComment_EndsAtEitherLineEndingOrAtTheEndOfTheStatement(
        string sql,
        string expectedColumn,
        string expectedTable)
    {
        SelectStatementModel model = Parsed(sql);

        Assert.Equal(expectedColumn, model.GetColumn());
        Assert.Equal(expectedTable, model.GetTable());
        Assert.Equal(sql, model.GetSql());
    }

    /// <summary>
    /// A present-but-EMPTY clause survives a modification to a DIFFERENT clause, keeping its bare
    /// keyword and the whitespace that followed it.
    /// </summary>
    /// <remarks>
    /// The emission path for present-and-empty, which only runs once a block has been modified - before
    /// that the block emits verbatim and the state is never exercised. The whitespace that followed the
    /// bare keyword was handed to the NEXT clause as its pre-gap by the parse, so re-emitting a lead gap
    /// here would duplicate it. Three spaces in and three spaces out is the assertion.
    /// </remarks>
    [Fact]
    public void APresentButEmptyClause_SurvivesAModificationToAnotherClause()
    {
        SelectStatementModel viaOrder = Parsed("SELECT A FROM T WHERE   ORDER BY B");
        Assert.True(viaOrder.ModifyOrder(Enums.SQL_MS_REPLACE, "C"));
        Assert.Equal("SELECT A FROM T WHERE   ORDER BY C", viaOrder.GetSql());
        Assert.True(viaOrder.HasWhere());
        Assert.Equal(string.Empty, viaOrder.GetWhere());

        SelectStatementModel viaColumn = Parsed("SELECT A FROM T WHERE   ORDER BY B");
        Assert.True(viaColumn.ModifyColumn(Enums.SQL_MS_REPLACE, "Z"));
        Assert.Equal("SELECT Z FROM T WHERE   ORDER BY B", viaColumn.GetSql());
    }

    /// <summary>
    /// A clause whose input had NO gap between the keyword and the body gets a single substituted space
    /// when it is re-emitted - and the absent gaps around the OTHER clauses stay absent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>SELECT(A)FROM t</c> is legal SQL with no whitespace anywhere around its keywords. When the
    /// block is re-emitted, the captured lead gap for the select list is empty, so a single space is
    /// substituted - otherwise a replacement body could fuse onto the keyword and change the meaning.
    /// </para>
    /// <para>
    /// THE SECOND CASE IS THE DEGENERATE ONE, and it is recorded rather than prevented: replacing the
    /// select list of that statement yields <c>SELECT BFROM t</c>, because the FROM clause's captured
    /// pre-gap is genuinely empty and reusing the input's own geometry is the whole mechanism. This
    /// preserves byte-exactness for every well-spaced statement, which is the property parity is
    /// measured on, at the cost of a caller who both writes gapless SQL and then rewrites its select
    /// list. Inserting a space that the input did not have would break the round trip everywhere else.
    /// </para>
    /// </remarks>
    [Fact]
    public void AClauseWithNoLeadGap_GetsASubstitutedSpaceWhenItIsReEmitted()
    {
        SelectStatementModel viaTable = Parsed("SELECT(A)FROM t");
        Assert.True(viaTable.ModifyTable(Enums.SQL_MS_REPLACE, "u"));
        Assert.Equal("SELECT (A)FROM u", viaTable.GetSql());

        // The degenerate consequence, preserved deliberately.
        SelectStatementModel viaColumn = Parsed("SELECT(A)FROM t");
        Assert.True(viaColumn.ModifyColumn(Enums.SQL_MS_REPLACE, "B"));
        Assert.Equal("SELECT BFROM t", viaColumn.GetSql());
    }

    /// <summary>
    /// A clause introduced into a statement whose earlier clauses have all been cleared is emitted with
    /// no leading space.
    /// </summary>
    /// <remarks>
    /// The empty-buffer arm of the separator logic. Reachable only through the degenerate sequence the
    /// production comment names - every earlier clause cleared outright - and asserted because the
    /// alternative, an unconditional separator, would put a leading space on the statement that no
    /// caller asked for and that every characterization comparison would then carry.
    /// </remarks>
    [Fact]
    public void AGeometryLessClauseEmittedFirst_CarriesNoLeadingSpace()
    {
        SelectStatementModel model = Parsed("SELECT a FROM t");

        Assert.True(model.ModifyColumn(Enums.SQL_MS_REPLACE, string.Empty));
        Assert.True(model.ModifyTable(Enums.SQL_MS_REPLACE, string.Empty));
        Assert.Equal(string.Empty, model.GetSql());

        Assert.True(model.ModifyGroup(Enums.SQL_MS_REPLACE, "x"));
        Assert.Equal("GROUP BY x", model.GetSql());

        Assert.True(model.ModifyOrder(Enums.SQL_MS_REPLACE, "y"));
        Assert.Equal("GROUP BY x ORDER BY y", model.GetSql());
    }

    /// <summary>
    /// Every one of the six clause kinds is reachable through BOTH the presence test and the text
    /// accessor in the indexed arity.
    /// </summary>
    /// <remarks>
    /// The 36-member surface is generated from two cores, so a member wired to the wrong core - a
    /// presence test that returned text, or an accessor that answered presence - would not compile, but
    /// one wired to the wrong CLAUSE would. This exercises all twelve indexed read members against a
    /// statement where every clause has a DISTINCT body, so a mis-wiring shows up as the wrong value
    /// rather than as a coincidence.
    /// </remarks>
    [Fact]
    public void AllTwelveIndexedReadMembers_AddressTheirOwnClause()
    {
        SelectStatementModel model =
            Parsed("SELECT c1 FROM t2 WHERE w3 GROUP BY g4 HAVING h5 ORDER BY o6");

        Assert.True(model.HasColumn(1));
        Assert.True(model.HasTable(1));
        Assert.True(model.HasWhere(1));
        Assert.True(model.HasGroup(1));
        Assert.True(model.HasHaving(1));
        Assert.True(model.HasOrder(1));

        Assert.Equal("c1", model.GetColumn(1));
        Assert.Equal("t2", model.GetTable(1));
        Assert.Equal("w3", model.GetWhere(1));
        Assert.Equal("g4", model.GetGroup(1));
        Assert.Equal("h5", model.GetHaving(1));
        Assert.Equal("o6", model.GetOrder(1));

        // The short arity agrees with the indexed one for all six.
        Assert.Equal(model.GetColumn(1), model.GetColumn());
        Assert.Equal(model.GetTable(1), model.GetTable());
        Assert.Equal(model.GetWhere(1), model.GetWhere());
        Assert.Equal(model.GetGroup(1), model.GetGroup());
        Assert.Equal(model.GetHaving(1), model.GetHaving());
        Assert.Equal(model.GetOrder(1), model.GetOrder());

        // And an index addressing no block answers negatively for all twelve.
        Assert.False(model.HasColumn(2));
        Assert.False(model.HasTable(2));
        Assert.False(model.HasWhere(2));
        Assert.False(model.HasGroup(2));
        Assert.False(model.HasHaving(2));
        Assert.False(model.HasOrder(2));

        Assert.Equal(string.Empty, model.GetColumn(2));
        Assert.Equal(string.Empty, model.GetTable(2));
        Assert.Equal(string.Empty, model.GetWhere(2));
        Assert.Equal(string.Empty, model.GetGroup(2));
        Assert.Equal(string.Empty, model.GetHaving(2));
        Assert.Equal(string.Empty, model.GetOrder(2));
    }

    /// <summary>
    /// The type is <see langword="internal"/> and sealed, reachable from this suite only through the
    /// application project's own friend grant.
    /// </summary>
    /// <remarks>
    /// No consumer outside the <c>PowerFramework.Persistence</c> assembly needs the model, and the
    /// application project already grants this test project access, so visibility never has to be
    /// widened for testability. Asserted so that a later "make it public for testing" change - which
    /// would put an unversioned type on the assembly's public surface - fails a test.
    /// </remarks>
    [Fact]
    public void TheModel_StaysInternalToItsAssembly()
    {
        Type type = typeof(SelectStatementModel);

        Assert.False(type.IsPublic);
        Assert.True(type.IsSealed);
        Assert.Equal("PowerFramework.Persistence.Sql", type.Namespace);
        Assert.Equal("PowerFramework.Persistence", type.Assembly.GetName().Name);
    }
}
