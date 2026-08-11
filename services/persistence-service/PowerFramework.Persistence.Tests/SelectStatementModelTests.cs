// ==============================================================================================
//  SelectStatementModelTests - the characterization suite that pins
//  PowerFramework.Persistence.Sql.SelectStatementModel
//  --------------------------------------------------------------------------------------------
//  SYSTEM UNDER TEST  services/persistence-service/PowerFramework.Persistence/Sql/SelectStatementModel.cs
//  BEHAVIOURAL ORACLE ws_objects/pfw.utility.parser.pbl.src/n_sql.sru        (62 lines, PROTOTYPES ONLY)
//                     ws_objects/pfw.utility.parser.pbl.src/parsesql.srf     (15 lines)
//                     ws_objects/pfw.tests.pbl.src/w_test_sqlparser.srw      (752 lines, the driver)
//                     ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru
//                     ws_objects/pfw.shared.pbl.src/enums.sru:L718-L720      the three modify styles
//                     ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14         the fixture's retrieve=
//                     all READ ONLY per constraint C-C - read as specification, never edited,
//                     never copied, cited by locator at every point of use.
//
//  C-K: WHY THE SUBJECT IS A REIMPLEMENTATION AND WHAT THAT MAKES THE ACCEPTANCE CRITERION
//  --------------------------------------------------------------------------------------------
//  AAP 0.3.3 records the decision this suite exists to protect. A managed SQL-parser substitution
//  for n_sql was evaluated and REJECTED: Microsoft.SqlServer.TransactSql.ScriptDom is
//  SQL-Server-dialect-specific and therefore cannot serve the Oracle rewriter, and no SQL parser
//  exists in the base class library at all. Since the acceptance criterion is BYTE-EXACT parity
//  with n_sql's CLAUSE-LEVEL STRING output - including the sentinel identifiers the rewriters
//  splice in, pfwPagedSQL_OutterTbl, pfwPagedSQL_RN, pfwPagedSQL_Tbl, pfwPagedSQL_TblInner,
//  pfwPagedSQL_TblInnerInner and pfwPagedSQL_TblOuter, and the count-wrapper alias "1 AS _" at
//  n_cst_thread_task_sqlquery.sru:L830 - a focused in-repository six-clause model is both
//  sufficient and far more verifiable than a parser that normalises to a tree and regenerates.
//
//  That criterion is the reason so many cases below assert an EXACT STRING rather than a parsed
//  shape, and the reason every one of those comparisons is made with StringComparison.Ordinal
//  explicitly rather than left to a default. Whitespace is observable here: the Oracle rewriter's
//  single space before WHERE [n_cst_thread_task_sqlquery.sru:L394-L395] is a genuine downstream
//  parity requirement, so a normalised or whitespace-insensitive comparison would let exactly the
//  defect this file is meant to catch through. If GetSql or any Get<Clause> is off by one
//  character, all four MSSQL paging arms and the Oracle arm fail downstream.
//
//  WHAT THIS SUITE CAN AND CANNOT BE EVIDENCE OF (stated early, because it governs every assertion)
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
//  THE ORACLE DRIVER WINDOW, MINED RATHER THAN INVENTED
//  --------------------------------------------------------------------------------------------
//  w_test_sqlparser.srw is the closest thing to an oracle this unit has, and two of its own
//  defaults are used below in preference to invented input:
//
//    * its statement box ships with "SELECT * FROM DUAL" [w_test_sqlparser.srw:L716], so that is
//      the statement the driver itself parses on open;
//    * its select-index box ships with "1" [w_test_sqlparser.srw:L749], which is direct evidence
//      that index 1 addresses the FIRST select block - the one-based convention, corroborated by
//      the caller-side guard `if selectIndex <= 0 ... return RetCode.E_INVALID_ARGUMENT` at
//      n_cst_thread_task_sqlquery.sru:L269 and :L286.
//
//  Its twelve modify buttons are also the only measured call sites for the THREE-ARGUMENT modify
//  overloads, and every one of them passes the select index FIRST
//  [w_test_sqlparser.srw:L380,L399,L418,L437,L456,L475,L511,L530,L549,L568,L587,L699], matching the
//  prototypes at n_sql.sru:L39,L41,L43,L45,L47,L49. Worth recording what the driver does NOT
//  exercise: it drives REPLACE and APPEND only, never PREPEND, so the prepend arm has no measured
//  call site anywhere in the estate and its assertions below are Tier 2 by necessity.
//
//  C-E: NO DATABASE, AND NOT BY OMISSION
//  --------------------------------------------------------------------------------------------
//  Every case in this file is string in, string out. There is no connection, no SQLite file, no
//  SQL Server or Oracle client, no test container and no fixture with state. That is constraint
//  C-E - no fabricated database - discharged structurally: the subject is a pure text transform, so
//  both DBMS behaviours the legacy supports are testable without an instance of either, which is
//  exactly the property AAP 0.6.4 relies on. Consequently no test here is order-dependent, none
//  reads a clock and none consumes randomness, so the suite is repeatable by construction - the one
//  hard prerequisite of the Golden-Master technique.
//
//  C-B: THE DEFECTS THAT ARE ASSERTED AS EXPECTED
//  --------------------------------------------------------------------------------------------
//  Three preserved legacy behaviours are pinned as CORRECT rather than corrected, because C-B
//  forbids improving them:
//
//    1. The factory DISCARDS Parse's boolean [parsesql.srf:L12] and returns a live but UNPARSED
//       instance for an unparseable statement. Not reshaped into a try-pattern, not made to return
//       null, not made to throw.
//    2. A clause is OPAQUE TEXT - never escaped, quoted, validated or re-parsed - which is the
//       mechanical root of the legacy SQL-injection exposure. Remediation belongs to the execution
//       layer, where it does not change the observable generated statement.
//    3. Removal of the column or table clause yields text that is no longer a well-formed SELECT.
//       The native carries no guard against it, so neither does the port.
//
//  C-F SELF-AUDIT: every statement here is synthetic and names invented tables and columns. No
//  credential, connection string, password, token, key or captured production statement appears.
//  This matters in this file specifically because of item 2 above: the injection-shaped cases use
//  obviously synthetic payloads and assert only that the model passes the text through UNCHANGED,
//  which is a record of the preserved behaviour and not an endorsement of it.
//
//  RULES POSITION: review_rules returns exactly one line, "No user rules provided.", so no
//  user-specified rule governs this file, none is invented, and no file enters scope because of
//  one. The enterprise-standard baseline of AAP 0.7.2 applies instead: nullable and
//  warnings-as-errors inherited from the repository root and kept clean, no SCREAMING_SNAKE
//  identifier DECLARED here (the three modify styles are CONSUMED from
//  PowerFramework.Shared.Kernel.Enums, which is inside .editorconfig's naming-suppression scope
//  while no test file is), deterministic and independent tests, and no secret in source. Per AAP
//  0.8.5 NO PERFORMANCE PROPERTY is asserted anywhere below - not a timing, not a bound, not a
//  comparison against anything - because the repository publishes no latency, throughput or
//  availability commitment to assert one against. Note that the oracle driver window does time
//  itself with CPU() [w_test_sqlparser.srw:L727,L731]; that is a developer convenience in a
//  read-only demo and is deliberately NOT carried across.
// ==============================================================================================

using System.Reflection;

using PowerFramework.Persistence.Sql;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Characterization tests for the managed SQL <c>SELECT</c> statement model: the 39-member surface
/// it publishes, what it accepts, what it extracts, what the three modify styles do, and the source
/// geometry it preserves so that emission stays byte-exact.
/// </summary>
public sealed class SelectStatementModelTests
{
    /// <summary>
    /// The statement the golden-master fixture retrieves
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14</c>].
    /// </summary>
    /// <remarks>
    /// The principal case throughout this file. Every characterization recording of the
    /// retrieve / validate / update triple starts from this statement, so proving the model against
    /// it - rather than only against synthetic input - is what ties this suite to real evidence.
    /// </remarks>
    private const string FixtureRetrieve = "SELECT * FROM COMPANY";

    /// <summary>
    /// The statement the oracle driver window ships with in its own statement box
    /// [<c>ws_objects/pfw.tests.pbl.src/w_test_sqlparser.srw:L716</c>].
    /// </summary>
    private const string OracleDriverDefault = "SELECT * FROM DUAL";

    /// <summary>
    /// The select index the oracle driver window ships with
    /// [<c>ws_objects/pfw.tests.pbl.src/w_test_sqlparser.srw:L749</c>], and therefore the index that
    /// addresses the FIRST select block.
    /// </summary>
    private const int FirstSelectIndex = 1;

    /// <summary>The six clause kinds, in the order <c>n_sql.sru:L14-L49</c> declares them.</summary>
    private static readonly string[] ClauseKinds =
        ["Column", "Table", "Where", "Group", "Having", "Order"];

    /// <summary>A parsed model over <paramref name="sql"/>, with the parse asserted to have succeeded.</summary>
    private static SelectStatementModel Parsed(string sql)
    {
        SelectStatementModel model = new();

        Assert.True(model.Parse(sql), $"Expected to parse: {sql}");

        return model;
    }

    /// <summary>
    /// Asserts byte-exact string equality with <see cref="StringComparison.Ordinal"/> stated
    /// explicitly, so that the comparison can never be relaxed to a culture-aware,
    /// case-insensitive or whitespace-normalising one by a later edit.
    /// </summary>
    /// <param name="expected">The exact expected text.</param>
    /// <param name="actual">The text produced by the model.</param>
    /// <param name="because">What the comparison is evidence of, for the failure message.</param>
    private static void AssertByteExact(string expected, string actual, string because) =>
        Assert.True(
            string.Equals(expected, actual, StringComparison.Ordinal),
            $"{because}\n  expected: <{expected}>\n  actual:   <{actual}>");

    // ==========================================================================================
    //  THE MEMBER CENSUS - n_sql.sru:L9-L49
    // ==========================================================================================

    /// <summary>
    /// The model publishes exactly the surface the native prototypes declare, plus one member the port
    /// needs and the native does not, plus the one static factory - with the two metadata members
    /// deliberately absent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TIER 1 - TRACEABLE for the 39. <c>n_sql.sru</c> declares 41 prototypes at <c>L9-L49</c>. Two are
    /// not ported - <c>copyright()</c> at <c>L9</c> and <c>getversion()</c> at <c>L10</c>, which report a
    /// vendor string and the pfw.dll build number and are framework-metadata boilerplate present on
    /// every PBNI class in the estate. Porting them would fabricate a version number for an assembly
    /// whose version already comes from <c>Directory.Build.props</c>. So 39 ported members ship, plus
    /// <c>ParseSql</c> reproducing <c>parsesql.srf:L10-L14</c>.
    /// </para>
    /// <para>
    /// The arithmetic is asserted rather than described because a member quietly added or dropped is
    /// invisible in review: 6 clause kinds x 3 operations x 2 arities is 36, plus
    /// <c>Parse</c>, <c>GetSql</c> and <c>GetSelectCount</c> is 39. Per-member SIGNATURES - names,
    /// parameter types, parameter ORDER and return types - are pinned separately by
    /// <see cref="EveryClauseMember_HasExactlyTheSignatureItsLegacyPrototypeDeclares"/>, because a
    /// count alone would accept a member whose parameters were in the wrong order.
    /// </para>
    /// <para>
    /// TIER 3 - PORT-LOCAL for the fortieth. <c>GetSqlWithoutTerminator</c> has no native counterpart
    /// and exists because a review found that the paging rewriters embed this statement's text inside a
    /// larger one - <c>SELECT TOP n * FROM (</c> + text + <c>) pfwPagedSQL_Tbl WHERE ...</c> - so a
    /// trailing <c>;</c> retained in that text terminated the generated statement in the middle and
    /// silently discarded the paging. The native has no such member because the native's own callers
    /// have the same defect; adding one here is what lets <c>GetSql</c> stay byte-for-byte faithful to
    /// its input while the rewriters read a body with no terminator in it. The sibling
    /// <c>StatementTerminator</c> is a PROPERTY, so its accessor is special-named and does not appear in
    /// this census - it is asserted separately below.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheModel_PublishesTheThirtyNinePortedMembersPlusTheTerminatorReadAndTheFactory()
    {
        MethodInfo[] instanceMembers = typeof(SelectStatementModel)
            .GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance)
            .Where(method => !method.IsSpecialName)
            .ToArray();

        Assert.Equal(40, instanceMembers.Length);

        foreach (string clause in ClauseKinds)
        {
            // Presence test, text accessor and modifier, each in both arities.
            Assert.Single(instanceMembers, m => m.Name == "Has" + clause && m.GetParameters().Length == 0);
            Assert.Single(instanceMembers, m => m.Name == "Has" + clause && m.GetParameters().Length == 1);
            Assert.Single(instanceMembers, m => m.Name == "Get" + clause && m.GetParameters().Length == 0);
            Assert.Single(instanceMembers, m => m.Name == "Get" + clause && m.GetParameters().Length == 1);
            Assert.Single(instanceMembers, m => m.Name == "Modify" + clause && m.GetParameters().Length == 2);
            Assert.Single(instanceMembers, m => m.Name == "Modify" + clause && m.GetParameters().Length == 3);
        }

        // The three statement-level members - n_sql.sru:L11, L12, L13.
        Assert.Single(instanceMembers, m => m.Name == "Parse");
        Assert.Single(instanceMembers, m => m.Name == "GetSql");
        Assert.Single(instanceMembers, m => m.Name == "GetSelectCount");

        // The one port-local member, and the property beside it. Named explicitly so that the count
        // above cannot be satisfied by some other accidental addition.
        Assert.Single(instanceMembers, m => m.Name == "GetSqlWithoutTerminator");
        Assert.NotNull(typeof(SelectStatementModel).GetProperty("StatementTerminator"));

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

    /// <summary>The six clause kinds, as theory data.</summary>
    public static TheoryData<string> AllClauseKinds =>
        new() { "Column", "Table", "Where", "Group", "Having", "Order" };

    /// <summary>
    /// Every one of the 36 clause members carries EXACTLY the signature its legacy prototype declares -
    /// name, parameter types, parameter ORDER and return type - and in particular the SELECT INDEX IS
    /// THE FIRST PARAMETER of the three-argument modifier, never the last.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TIER 1 - TRACEABLE, and the assertion that a member census by arity alone cannot make.
    /// <c>n_sql.sru:L39</c> declares
    /// <c>modifycolumn(readonly int nselectindex, readonly long ms, readonly string newcolumn)</c>, and
    /// <c>L41</c>, <c>L43</c>, <c>L45</c>, <c>L47</c> and <c>L49</c> declare the other five the same way:
    /// index, then style, then text. Every measured call site agrees - the twelve modify buttons of the
    /// oracle driver window all pass the index first
    /// [<c>w_test_sqlparser.srw:L380,L399,L418,L437,L456,L475,L511,L530,L549,L568,L587,L699</c>], and so
    /// do the two production call sites [<c>n_cst_thread_task_sqlquery.sru:L691</c>, <c>:L698</c>].
    /// </para>
    /// <para>
    /// This matters because <c>Modify&lt;K&gt;(long, string)</c> and a hypothetical
    /// <c>Modify&lt;K&gt;(long, string, int)</c> would both satisfy an arity check while the second would
    /// silently break every caller. The order is therefore asserted positionally, as is the STYLE's
    /// type: <c>ms</c> is a <c>long</c> [<c>n_sql.sru:L38</c>] because the constants it carries are
    /// declared <c>Constant Long</c> [<c>enums.sru:L718-L720</c>], and narrowing it to <c>int</c> would
    /// be a silent contract change.
    /// </para>
    /// </remarks>
    /// <param name="clause">The clause kind under inspection.</param>
    [Theory]
    [MemberData(nameof(AllClauseKinds))]
    public void EveryClauseMember_HasExactlyTheSignatureItsLegacyPrototypeDeclares(string clause)
    {
        Type model = typeof(SelectStatementModel);

        // has<clause>() -> boolean            n_sql.sru:L14,L16,L18,L20,L22,L24
        MethodInfo? shortHas = model.GetMethod("Has" + clause, Type.EmptyTypes);
        Assert.NotNull(shortHas);
        Assert.Equal(typeof(bool), shortHas.ReturnType);

        // has<clause>(readonly int nselectindex) -> boolean   n_sql.sru:L15,L17,L19,L21,L23,L25
        MethodInfo? indexedHas = model.GetMethod("Has" + clause, [typeof(int)]);
        Assert.NotNull(indexedHas);
        Assert.Equal(typeof(bool), indexedHas.ReturnType);

        // get<clause>() -> string             n_sql.sru:L26,L28,L30,L32,L34,L36
        MethodInfo? shortGet = model.GetMethod("Get" + clause, Type.EmptyTypes);
        Assert.NotNull(shortGet);
        Assert.Equal(typeof(string), shortGet.ReturnType);

        // get<clause>(readonly int nselectindex) -> string    n_sql.sru:L27,L29,L31,L33,L35,L37
        MethodInfo? indexedGet = model.GetMethod("Get" + clause, [typeof(int)]);
        Assert.NotNull(indexedGet);
        Assert.Equal(typeof(string), indexedGet.ReturnType);

        // modify<clause>(readonly long ms, readonly string new) -> boolean
        //                                     n_sql.sru:L38,L40,L42,L44,L46,L48
        MethodInfo? shortModify = model.GetMethod("Modify" + clause, [typeof(long), typeof(string)]);
        Assert.NotNull(shortModify);
        Assert.Equal(typeof(bool), shortModify.ReturnType);

        ParameterInfo[] shortParameters = shortModify.GetParameters();
        Assert.Equal(typeof(long), shortParameters[0].ParameterType);
        Assert.Equal(typeof(string), shortParameters[1].ParameterType);

        // modify<clause>(readonly int nselectindex, readonly long ms, readonly string new) -> boolean
        //                                     n_sql.sru:L39,L41,L43,L45,L47,L49
        MethodInfo? indexedModify =
            model.GetMethod("Modify" + clause, [typeof(int), typeof(long), typeof(string)]);
        Assert.NotNull(indexedModify);
        Assert.Equal(typeof(bool), indexedModify.ReturnType);

        // THE ORDER, POSITIONALLY. Index FIRST, style SECOND, text THIRD.
        ParameterInfo[] indexedParameters = indexedModify.GetParameters();
        Assert.Equal(3, indexedParameters.Length);
        Assert.Equal(typeof(int), indexedParameters[0].ParameterType);
        Assert.Equal(typeof(long), indexedParameters[1].ParameterType);
        Assert.Equal(typeof(string), indexedParameters[2].ParameterType);

        // And the mirror image is ABSENT: no overload takes the index last.
        Assert.Null(model.GetMethod("Modify" + clause, [typeof(long), typeof(string), typeof(int)]));
    }

    /// <summary>
    /// The three statement-level members carry the return types their prototypes declare.
    /// </summary>
    /// <remarks>
    /// TIER 1 - TRACEABLE. <c>n_sql.sru:L11</c> returns <c>boolean</c>, <c>L12</c> returns
    /// <c>string</c> and <c>L13</c> returns <c>int</c> - not <c>long</c>, which is worth pinning
    /// because every other numeric on this surface is a <c>long</c> and widening the count would be
    /// an easy, invisible drift.
    /// </remarks>
    [Fact]
    public void TheThreeStatementMembers_CarryTheReturnTypesTheirPrototypesDeclare()
    {
        Type model = typeof(SelectStatementModel);

        MethodInfo? parse = model.GetMethod("Parse", [typeof(string)]);
        Assert.NotNull(parse);
        Assert.Equal(typeof(bool), parse.ReturnType);

        MethodInfo? getSql = model.GetMethod("GetSql", Type.EmptyTypes);
        Assert.NotNull(getSql);
        Assert.Equal(typeof(string), getSql.ReturnType);

        MethodInfo? getSelectCount = model.GetMethod("GetSelectCount", Type.EmptyTypes);
        Assert.NotNull(getSelectCount);
        Assert.Equal(typeof(int), getSelectCount.ReturnType);
    }

    /// <summary>
    /// The three modify styles are the shared kernel's constants, with the values the legacy declares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TIER 1 - TRACEABLE. <c>enums.sru:L718-L720</c> declares replace as 1, append as 2 and prepend
    /// as 3. Asserted against LITERALS rather than against the constants themselves, since comparing a
    /// constant to itself would pass for any value.
    /// </para>
    /// <para>
    /// The constants are CONSUMED from <c>PowerFramework.Shared.Kernel.Enums</c> and are deliberately
    /// not redeclared here. The reason is a scoping decision, and it is worth stating PRECISELY because
    /// the imprecise version of it is tempting and wrong. The repository-root <c>.editorconfig</c>
    /// switches <c>CA1707</c> and <c>IDE1006</c> to <c>none</c> for the INDIVIDUALLY NAMED files on its
    /// BAND 3 roster - the single source of truth for that list, and the reason no count is restated
    /// here: a number copied into a source comment is a second place for the roster to be wrong, and
    /// every copy of it in this repository had silently drifted to a different value as files were
    /// added. Neither the model nor this suite is on that roster. It is where a preserved
    /// SCREAMING_SNAKE spelling is sanctioned; a
    /// file outside it declaring one is outside the sanctioned scope, and the roster's deliberate
    /// narrowness is what makes the exception auditable.
    /// </para>
    /// <para>
    /// What this is NOT: it is not a hard build failure in the current configuration, and claiming that
    /// it were would mislead the next reader in a specific way - they would try it, watch it compile,
    /// and conclude the convention does not apply. Verified by building one: an underscore-bearing
    /// constant added to this project compiles with zero warnings, whether declared
    /// <see langword="internal"/> or <see langword="public"/>. The mechanism is that
    /// <c>Directory.Build.props</c> sets <c>TreatWarningsAsErrors</c>, <c>EnableNETAnalyzers</c> and
    /// <c>AnalysisLevel</c> but pointedly does NOT set <c>EnforceCodeStyleInBuild</c>, so the IDE
    /// family never reaches the build at all, and <c>CA1707</c> is not among the rules the SDK's
    /// default analysis mode enables. So warnings-as-errors is real and is kept clean, but it is not
    /// what holds this particular line - the roster is, and it holds it by convention rather than by
    /// the compiler.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheThreeModifyStyles_CarryTheValuesTheLegacyConstantBlockDeclares()
    {
        Assert.Equal(1L, Enums.SQL_MS_REPLACE);
        Assert.Equal(2L, Enums.SQL_MS_APPEND);
        Assert.Equal(3L, Enums.SQL_MS_PREPEND);
    }

    // ==========================================================================================
    //  PARSE ACCEPTANCE AND THE BYTE-EXACT ROUND TRIP
    // ==========================================================================================

    /// <summary>
    /// Statements the model accepts, each of which must survive parse-then-emit unchanged.
    /// </summary>
    /// <remarks>
    /// Chosen so that a REGENERATING implementation would fail at least one: irregular internal
    /// spacing, lower-case keywords, a doubled space inside <c>group  by</c>, a line comment whose
    /// terminator is a newline, a block comment containing a clause keyword, a trailing semicolon,
    /// surrounding whitespace on both ends, and a leading common-table expression. The first two
    /// entries are the two statements the repository itself supplies - the fixture's
    /// <c>retrieve=</c> [<c>dw_sqlite.srd:L14</c>] and the oracle driver's own default
    /// [<c>w_test_sqlparser.srw:L716</c>] - so the theory is anchored in real evidence before it
    /// reaches synthetic input.
    /// </remarks>
    public static TheoryData<string> AcceptedStatements =>
        new()
        {
            FixtureRetrieve,
            OracleDriverDefault,
            "select id, name from company where age > 30 group by id having count(*) > 1 order by name",
            "  SELECT a FROM t  ",
            "SELECT   a  ,   b   FROM    t",
            "SeLeCt a FrOm t WhErE b=1",
            "SELECT a FROM t group  by a",
            "SELECT a FROM t ORDER BY a, b DESC",
            "SELECT a FROM t\nWHERE b = 1\nORDER BY a",
            "SELECT a FROM t;",
            "SELECT(A)FROM t",
            "SELECT a FROM (SELECT b FROM u WHERE b > 1) x",
            "WITH c AS (SELECT 1 AS n FROM dual) SELECT n FROM c",
            "SELECT 'from t where x' AS lit FROM t",
            "SELECT [from] FROM [where]",
            "SELECT \"order by\" FROM t",
            "SELECT a /* from u */ FROM t",
            "SELECT a -- from u\n FROM t",
            "SELECT a FROM t UNION SELECT b FROM u",
            "SELECT a FROM t UNION ALL SELECT b FROM u",
            "SELECT a FROM t UNION  ALL  SELECT b FROM u",
            "SELECT a FROM t INTERSECT SELECT b FROM u",
            "SELECT a FROM t EXCEPT SELECT b FROM u",
            "SELECT",
            "SELECT a",
        };

    /// <summary>
    /// Every accepted statement round-trips BYTE FOR BYTE through parse and emit - whitespace,
    /// casing, comments and all - compared ordinally.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TIER 3 - STRUCTURAL, and the single most valuable property in this file. It is achieved by
    /// design rather than by normalising: the parse records the exact source geometry of every clause
    /// and an unmodified block re-emits its original text.
    /// </para>
    /// <para>
    /// Note the CTE case. A leading common-table expression's own <c>SELECT</c> sits inside
    /// parentheses and is therefore invisible to the depth-zero scan, so the whole expression is
    /// retained verbatim as the first block's prefix and never interpreted.
    /// </para>
    /// </remarks>
    /// <param name="sql">The statement to round-trip.</param>
    [Theory]
    [MemberData(nameof(AcceptedStatements))]
    public void AnAcceptedStatement_RoundTripsByteForByte(string sql)
    {
        SelectStatementModel model = Parsed(sql);

        AssertByteExact(sql, model.GetSql(), "An unmodified statement must emit its input verbatim.");
    }


    // ==========================================================================================
    //  THE STATEMENT TERMINATOR - SEPARATED AT PARSE, RE-ATTACHED AT THE OUTERMOST END
    //  ------------------------------------------------------------------------------------------
    //  TIER 3 - PORT-LOCAL, and it exists because a review found a defect the round-trip theory
    //  above could not see. `SELECT a FROM t;` round-trips byte for byte whether the semicolon is
    //  held separately or kept inside the body, so that theory passes either way. What it cannot see
    //  is that the paging rewriters EMBED this text inside a larger statement, and a semicolon inside
    //  the body then sits in the MIDDLE of the generated SQL - so everything the rewriter appends
    //  after it is unreachable and the paging silently disappears.
    //
    //  The cases below therefore pin all three halves of the resolution: the body carries no
    //  terminator, GetSql re-attaches it so the round trip stays byte-exact, and multi-statement
    //  input is refused outright rather than guessed at.
    // ==========================================================================================

    /// <summary>Statement, the body the rewriters must see, and the terminator held apart from it.</summary>
    public static TheoryData<string, string, string> TerminatorSeparations =>
        new()
        {
            { "SELECT a FROM t;", "SELECT a FROM t", ";" },
            { "SELECT a FROM t ;", "SELECT a FROM t ", ";" },
            { "SELECT a FROM t;  ", "SELECT a FROM t", ";  " },
            { "SELECT a FROM t;\n", "SELECT a FROM t", ";\n" },
            { "  SELECT a FROM t;  ", "  SELECT a FROM t", ";  " },
            { "SELECT a FROM t", "SELECT a FROM t", "" },
            { FixtureRetrieve + ";", FixtureRetrieve, ";" },
            {
                "SELECT a FROM t UNION SELECT b FROM u;",
                "SELECT a FROM t UNION SELECT b FROM u",
                ";"
            },
        };

    /// <summary>
    /// A trailing terminator is held apart from the body, so the two reads differ by exactly it.
    /// </summary>
    /// <param name="sql">The statement as supplied.</param>
    /// <param name="expectedBody">The body the rewriters must see.</param>
    /// <param name="expectedTerminator">The terminator, with the whitespace that followed it.</param>
    [Theory]
    [MemberData(nameof(TerminatorSeparations))]
    public void ATrailingTerminatorIsHeldApartFromTheBody(
        string sql,
        string expectedBody,
        string expectedTerminator)
    {
        SelectStatementModel model = Parsed(sql);

        AssertByteExact(
            expectedBody,
            model.GetSqlWithoutTerminator(),
            "The embeddable body must carry no terminator.");
        AssertByteExact(
            expectedTerminator,
            model.StatementTerminator,
            "The terminator must be held verbatim, trailing whitespace included.");

        // AND THE FAITHFUL READ IS STILL BYTE-EXACT, which is the obligation the separation must not
        // break: a dropped terminator is a diff in a recording comparison.
        AssertByteExact(sql, model.GetSql(), "Separating the terminator must not disturb the round trip.");
    }

    /// <summary>Statements whose semicolon is DATA rather than structure.</summary>
    public static TheoryData<string> SemicolonsThatAreNotTerminators =>
        new()
        {
            "SELECT 'a;b' AS lit FROM t",
            "SELECT \"a;b\" FROM t",
            "SELECT [a;b] FROM t",
            "SELECT a /* ; */ FROM t",
            "SELECT a -- ;\n FROM t",
        };

    /// <summary>
    /// A semicolon that is DATA rather than structure is not a terminator, so nothing is separated.
    /// </summary>
    /// <param name="sql">The statement whose semicolon sits inside a literal, an identifier or a comment.</param>
    [Theory]
    [MemberData(nameof(SemicolonsThatAreNotTerminators))]
    public void ASemicolonInsideALiteralOrACommentIsNotATerminator(string sql)
    {
        SelectStatementModel model = Parsed(sql);

        Assert.Equal(string.Empty, model.StatementTerminator);
        AssertByteExact(sql, model.GetSqlWithoutTerminator(), "No terminator may be split off.");
        AssertByteExact(sql, model.GetSql(), "The statement must round-trip unchanged.");
    }

    /// <summary>Inputs carrying more than one statement, all of which are refused.</summary>
    /// <remarks>
    /// Note the trailing-comment cases: a semicolon followed by anything but whitespace is a
    /// separator, and deciding which trailing text is inert would require an oracle this repository
    /// does not have.
    /// </remarks>
    public static TheoryData<string> MultiStatementInputs =>
        new()
        {
            "SELECT a FROM t; SELECT b FROM u",
            "SELECT a FROM t; DELETE FROM t",
            "SELECT a FROM t;;",
            "SELECT a FROM t; ;",
            "SELECT a FROM t; -- done",
            "SELECT a FROM t; /* done */",
            "SELECT a FROM (SELECT b; FROM u) x",
            ";",
            "; SELECT a FROM t",
            ";SELECT a FROM t",
        };

    /// <summary>
    /// MULTI-STATEMENT INPUT IS REFUSED, which is a narrowing with a defined error rather than a
    /// widening with a guess (AAP 0.1.5).
    /// </summary>
    /// <remarks>
    /// The legacy parser is a closed binary whose behaviour on two statements cannot be observed from
    /// this repository, and every consumer of a rewritten statement splices the result into a
    /// DataWindow's select property [<c>n_cst_thread_task_sqlquery.sru:L709</c>] - which expects ONE
    /// statement. Refusing routes it to the caller's own fail-fast arm [<c>:L314</c>].
    /// </remarks>
    /// <param name="sql">The multi-statement input.</param>
    [Theory]
    [MemberData(nameof(MultiStatementInputs))]
    public void MultiStatementInputIsRefusedAndLeavesTheModelEmpty(string sql)
    {
        SelectStatementModel model = new();

        Assert.False(model.Parse(sql));
        Assert.Equal(0, model.GetSelectCount());
        Assert.Equal(string.Empty, model.GetSql());
        Assert.Equal(string.Empty, model.GetSqlWithoutTerminator());
        Assert.Equal(string.Empty, model.StatementTerminator);
    }

    /// <summary>
    /// A FAILED PARSE CLEARS A TERMINATOR THE PREVIOUS ONE HELD, so no state survives into a model the
    /// caller has been told is empty.
    /// </summary>
    [Fact]
    public void AFailedParseClearsTheTerminatorTheEarlierParseHeld()
    {
        SelectStatementModel model = new();

        Assert.True(model.Parse("SELECT a FROM t;"));
        Assert.Equal(";", model.StatementTerminator);

        Assert.False(model.Parse("SELECT a FROM (t"));
        Assert.Equal(string.Empty, model.StatementTerminator);
        Assert.Equal(string.Empty, model.GetSql());
    }

    /// <summary>
    /// A MODIFICATION LANDS INSIDE THE BODY, ahead of the terminator, which is the property that makes
    /// the separation useful rather than merely tidy.
    /// </summary>
    [Fact]
    public void AModifiedStatementKeepsItsTerminatorAtTheVeryEnd()
    {
        SelectStatementModel model = Parsed("SELECT a FROM t;");

        Assert.True(model.ModifyWhere(Enums.SQL_MS_REPLACE, "b = 1"));

        AssertByteExact(
            "SELECT a FROM t WHERE b = 1",
            model.GetSqlWithoutTerminator(),
            "The modification must land inside the body.");
        AssertByteExact(
            "SELECT a FROM t WHERE b = 1;",
            model.GetSql(),
            "The terminator must stay at the very end.");
    }

    /// <summary>Statements whose delimiters are unbalanced, all of which are rejected.</summary>
    public static TheoryData<string> MalformedStatements =>
        new()
        {
            "",
            "   ",
            "SELECT a FROM (t",
            "SELECT a FROM t)",
            "SELECT 'unterminated FROM t",
            "SELECT a /* unterminated FROM t",
            "SELECT [unterminated FROM t",
            "SELECT \"unterminated FROM t",
        };

    /// <summary>
    /// A statement whose parentheses, quotes, brackets or block comment are unbalanced is rejected,
    /// and rejection leaves the model EMPTY.
    /// </summary>
    /// <remarks>
    /// TIER 1 - TRACEABLE as a failure channel: the legacy consumer treats a false return as fatal,
    /// raising an internal-error event and abandoning the paging build
    /// [<c>n_cst_thread_task_sqlquery.sru:L314-L316</c>]. The emptied state is what makes a caller that
    /// ignores the boolean still safe: the count is zero, every presence test is false, every accessor
    /// returns empty and every modifier reports failure. The method never throws.
    /// </remarks>
    /// <param name="sql">The malformed statement.</param>
    [Theory]
    [MemberData(nameof(MalformedStatements))]
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

    /// <summary>Inputs that are not a well-ordered depth-zero <c>SELECT</c>.</summary>
    public static TheoryData<string> StatementsThatAreNotWellOrderedSelects =>
        new()
        {
            "DELETE FROM t",
            "UPDATE t SET a = 1",
            "INSERT INTO t VALUES (1)",
            "FROM t",
            "(SELECT a FROM t)",
            "SELECT a FROM t WHERE b = 1 WHERE c = 2",
            "SELECT a FROM t ORDER BY a WHERE b = 1",
            "SELECT a FROM t GROUP BY a FROM u",
        };

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
    /// <param name="sql">The rejected input.</param>
    [Theory]
    [MemberData(nameof(StatementsThatAreNotWellOrderedSelects))]
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
        AssertByteExact(
            "WITH c AS (SELECT 1 AS n FROM dual) SELECT Z FROM c",
            cte.GetSql(),
            "A leading CTE must survive a modification untouched.");

        // The same mechanism, applied to text that is not a CTE at all.
        SelectStatementModel prefixed = Parsed("garbage SELECT a FROM t");
        Assert.Equal("a", prefixed.GetColumn());
        Assert.True(prefixed.ModifyColumn(Enums.SQL_MS_REPLACE, "Z"));
        AssertByteExact(
            "garbage SELECT Z FROM t",
            prefixed.GetSql(),
            "Arbitrary leading text is carried verbatim, exactly as a CTE is.");

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
    /// <c>n_cst_thread_task_sqlquery.sru:L314-L316</c> safe.
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
    //  CLAUSE EXTRACTION
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
    /// to the clause, is not returned. The all-six read is itself the oracle driver's own
    /// "get every clause" button [<c>w_test_sqlparser.srw:L249-L254</c>].
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
    /// AN ABSENT CLAUSE returns THE EMPTY STRING, not a sentinel - and every one of the six kinds
    /// answers the same way, in both arities.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The absent-clause return value is a decision that must be READ OFF the legacy rather than
    /// chosen, and the legacy settles it. <c>n_sql.sru:L26-L37</c> types every accessor as
    /// <c>string</c>, and PowerScript has no null string - an unset string is the empty string - so no
    /// sentinel is even representable through that signature. The oracle driver corroborates it by
    /// consuming the results with string concatenation straight into a display field
    /// [<c>w_test_sqlparser.srw:L249-L254</c>]: a sentinel would be rendered to the user, and a null
    /// would be impossible. The production consumer relies on the same thing at
    /// <c>n_cst_thread_task_sqlquery.sru:L327</c>, where <c>Lower(sqlParser.GetOrder())</c> is called
    /// with no presence check first and its result then searched with <c>Pos</c>.
    /// </para>
    /// <para>
    /// So: empty string, not <c>null</c>, not <c>"?"</c>, not a marker of any kind. Asserted for all
    /// six kinds because a per-kind divergence is exactly the sort of thing a generated surface hides.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnAbsentClause_ReturnsTheEmptyStringRatherThanASentinel()
    {
        // The fixture's own statement has four of the six clauses absent.
        SelectStatementModel model = Parsed(FixtureRetrieve);

        Assert.True(model.HasColumn());
        Assert.True(model.HasTable());

        foreach (bool present in new[] { model.HasWhere(), model.HasGroup(), model.HasHaving(), model.HasOrder() })
        {
            Assert.False(present);
        }

        foreach (string absent in new[] { model.GetWhere(), model.GetGroup(), model.GetHaving(), model.GetOrder() })
        {
            Assert.NotNull(absent);
            Assert.Equal(string.Empty, absent);
        }

        // Identical through the indexed arity.
        foreach (string absent in new[]
        {
            model.GetWhere(FirstSelectIndex),
            model.GetGroup(FirstSelectIndex),
            model.GetHaving(FirstSelectIndex),
            model.GetOrder(FirstSelectIndex),
        })
        {
            Assert.Equal(string.Empty, absent);
        }

        // And the two clauses that CAN be absent from a degenerate statement answer the same way.
        SelectStatementModel keywordOnly = Parsed("SELECT");
        Assert.False(keywordOnly.HasTable());
        Assert.Equal(string.Empty, keywordOnly.GetTable());
        Assert.Equal(string.Empty, keywordOnly.GetTable(FirstSelectIndex));
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
        AssertByteExact(
            "SELECT A FROM T WHERE   ORDER BY B",
            bareKeyword.GetSql(),
            "A bare keyword must not be tidied away.");

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
    //  THE ONE-BASED SELECT INDEX, AND THE SHORT-ARITY EQUIVALENCE
    // ==========================================================================================

    /// <summary>
    /// The select index is ONE-BASED, the short arity addresses block one, and an index outside the
    /// range answers negatively rather than throwing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TIER 1 for the one-based index, from three independent locators. The oracle driver window ships
    /// its select-index box with the literal <c>"1"</c> [<c>w_test_sqlparser.srw:L749</c>] and feeds it
    /// straight into every indexed read and every indexed modify. The production caller-side setters
    /// reject a non-positive index outright - <c>if selectIndex &lt;= 0 ... return
    /// RetCode.E_INVALID_ARGUMENT</c> [<c>n_cst_thread_task_sqlquery.sru:L269</c>, <c>:L286</c>] - so
    /// zero is not a legal position, which only makes sense if 1 is the first. And the stored clauses
    /// are then applied with that same index at <c>:L691</c> and <c>:L698</c>.
    /// </para>
    /// <para>
    /// Translation hazard R9 applies directly here: PowerBuilder arrays are one-based, C# arrays are
    /// zero-based, and a rebase would make index 1 address the SECOND block while leaving
    /// <c>GetSelectCount</c> unchanged - a silent off-by-one indistinguishable from a behavioural
    /// regression. That is why the assertion below reads block two and requires a DIFFERENT value.
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

        // Block one - the index the oracle driver ships with.
        Assert.True(model.HasColumn(FirstSelectIndex));
        Assert.Equal("a", model.GetColumn(FirstSelectIndex));
        Assert.Equal("t", model.GetTable(FirstSelectIndex));
        Assert.Equal("a=1", model.GetWhere(FirstSelectIndex));
        Assert.False(model.HasOrder(FirstSelectIndex));

        // Block two - a DIFFERENT block, which is what proves the index is not ignored and not rebased.
        Assert.Equal("b", model.GetColumn(2));
        Assert.Equal("u", model.GetTable(2));
        Assert.False(model.HasWhere(2));
        Assert.Equal("b", model.GetOrder(2));

        // Out of range in both directions, and never an exception. Zero is included deliberately: the
        // caller-side guard at :L269 rejects it, so no legal call ever passes it, and a model that
        // treated it as an alias for the first block would mask that guard's removal.
        foreach (int index in new[] { -1, 0, 3, int.MaxValue, int.MinValue })
        {
            Assert.False(model.HasColumn(index));
            Assert.Equal(string.Empty, model.GetColumn(index));
            Assert.False(model.ModifyColumn(index, Enums.SQL_MS_REPLACE, "z"));
        }
    }

    /// <summary>
    /// Clause kind, the replacement text and the statement it produces - one row per kind, used to
    /// prove the two arities are interchangeable at select index 1.
    /// </summary>
    /// <remarks>
    /// Column, Where and Order lead the table because they are the three the requirement names
    /// explicitly; the other three follow so the equivalence is proved for the whole surface rather
    /// than sampled.
    /// </remarks>
    public static TheoryData<string, string, string> ShortArityEquivalenceCases =>
        new()
        {
            { "Column", "z1", "SELECT z1 FROM t WHERE b=1 GROUP BY c HAVING d>1 ORDER BY e" },
            { "Where", "z2", "SELECT a FROM t WHERE z2 GROUP BY c HAVING d>1 ORDER BY e" },
            { "Order", "z3", "SELECT a FROM t WHERE b=1 GROUP BY c HAVING d>1 ORDER BY z3" },
            { "Table", "z4", "SELECT a FROM z4 WHERE b=1 GROUP BY c HAVING d>1 ORDER BY e" },
            { "Group", "z5", "SELECT a FROM t WHERE b=1 GROUP BY z5 HAVING d>1 ORDER BY e" },
            { "Having", "z6", "SELECT a FROM t WHERE b=1 GROUP BY c HAVING z6 ORDER BY e" },
        };

    /// <summary>
    /// THE SHORT ARITY IS EXACTLY THE INDEXED ARITY AT SELECT INDEX 1, for the presence test, the text
    /// accessor and all three modify styles.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TIER 1 - TRACEABLE. <c>n_sql.sru</c> declares each clause member twice, once without an index
    /// and once with, and gives no other way to distinguish them; the oracle driver only ever drives
    /// the indexed form and only ever with <c>"1"</c> [<c>w_test_sqlparser.srw:L749</c>], while the
    /// production code drives both forms - the short one at
    /// <c>n_cst_thread_task_sqlquery.sru:L341</c>, <c>:L345</c>, <c>:L353</c>, <c>:L830</c> and the
    /// indexed one at <c>:L691</c> and <c>:L698</c> - against the same statement. The two must
    /// therefore be interchangeable when the index is 1, or the paging build and the clause-application
    /// loop would disagree about which block they are editing.
    /// </para>
    /// <para>
    /// Invoked through reflection rather than through twelve hand-written call pairs, because the point
    /// is that EVERY member behaves this way: a hand-written list would be the very thing a
    /// copy-and-paste slip hides in. Each style is exercised, including PREPEND, which no measured call
    /// site drives at all - the oracle window offers only replace and append buttons - so its
    /// equivalence is Tier 2 and is asserted precisely because nothing else covers it.
    /// </para>
    /// </remarks>
    /// <param name="clause">The clause kind.</param>
    /// <param name="replacement">The text to write into it.</param>
    /// <param name="expected">The statement a replace must produce, byte for byte.</param>
    [Theory]
    [MemberData(nameof(ShortArityEquivalenceCases))]
    public void TheShortArity_IsExactlyTheIndexedArityAtSelectIndexOne(
        string clause,
        string replacement,
        string expected)
    {
        const string source = "SELECT a FROM t WHERE b=1 GROUP BY c HAVING d>1 ORDER BY e";

        Type type = typeof(SelectStatementModel);
        MethodInfo shortHas = type.GetMethod("Has" + clause, Type.EmptyTypes)!;
        MethodInfo indexedHas = type.GetMethod("Has" + clause, [typeof(int)])!;
        MethodInfo shortGet = type.GetMethod("Get" + clause, Type.EmptyTypes)!;
        MethodInfo indexedGet = type.GetMethod("Get" + clause, [typeof(int)])!;
        MethodInfo shortModify = type.GetMethod("Modify" + clause, [typeof(long), typeof(string)])!;
        MethodInfo indexedModify =
            type.GetMethod("Modify" + clause, [typeof(int), typeof(long), typeof(string)])!;

        // 1. THE PRESENCE TEST AND THE TEXT ACCESSOR AGREE ACROSS THE TWO ARITIES.
        SelectStatementModel reader = Parsed(source);

        Assert.Equal(
            shortHas.Invoke(reader, null),
            indexedHas.Invoke(reader, [FirstSelectIndex]));
        AssertByteExact(
            (string)shortGet.Invoke(reader, null)!,
            (string)indexedGet.Invoke(reader, [FirstSelectIndex])!,
            $"Get{clause}() must equal Get{clause}(1).");

        // 2. ALL THREE MODIFY STYLES AGREE ACROSS THE TWO ARITIES, both in the boolean they report and
        //    in the statement they leave behind.
        foreach (long style in new[] { Enums.SQL_MS_REPLACE, Enums.SQL_MS_APPEND, Enums.SQL_MS_PREPEND })
        {
            SelectStatementModel viaShort = Parsed(source);
            SelectStatementModel viaIndexed = Parsed(source);

            object? shortResult = shortModify.Invoke(viaShort, [style, replacement]);
            object? indexedResult = indexedModify.Invoke(viaIndexed, [FirstSelectIndex, style, replacement]);

            Assert.Equal(shortResult, indexedResult);
            Assert.True((bool)shortResult!, $"Modify{clause} reported failure for style {style}.");

            AssertByteExact(
                viaShort.GetSql(),
                viaIndexed.GetSql(),
                $"Modify{clause}({style}, ...) must equal Modify{clause}(1, {style}, ...).");
        }

        // 3. AND THE REPLACE ARM PRODUCES THE EXACT STATEMENT EXPECTED, so the equivalence above cannot
        //    be satisfied by two members that are equally wrong.
        SelectStatementModel replaced = Parsed(source);
        Assert.True((bool)shortModify.Invoke(replaced, [Enums.SQL_MS_REPLACE, replacement])!);
        AssertByteExact(expected, replaced.GetSql(), $"Replacing {clause} must produce the expected text.");
    }


    /// <summary>Statement and the number of select blocks it splits into.</summary>
    public static TheoryData<string, int> SetOperatorSplits =>
        new()
        {
            { FixtureRetrieve, 1 },
            { OracleDriverDefault, 1 },
            { "SELECT a FROM t UNION SELECT b FROM u", 2 },
            { "SELECT a FROM t UNION ALL SELECT b FROM u", 2 },
            { "SELECT a FROM t INTERSECT SELECT b FROM u", 2 },
            { "SELECT a FROM t EXCEPT SELECT b FROM u", 2 },
            { "SELECT a FROM t UNION SELECT b FROM u UNION ALL SELECT c FROM v", 3 },
            { "SELECT a FROM t WHERE a IN (SELECT b FROM u UNION SELECT c FROM v)", 1 },
        };

    /// <summary>
    /// <c>GetSelectCount</c> answers 1 for a single statement and the block count for a compound one,
    /// splitting on set operators at depth zero only - and <c>UNION ALL</c> is taken as one two-word
    /// operator rather than as <c>UNION</c> followed by a stray <c>ALL</c>.
    /// </summary>
    /// <remarks>
    /// TIER 2 - DECIDED. The two-word preference matters for the round trip: consuming only
    /// <c>UNION</c> would leave <c>ALL</c> at the head of the second block, where it would be emitted
    /// in a different place once that block was modified. Oracle's <c>MINUS</c> is the fifth recognised
    /// operator and is covered, with the rest of the dialect matrix, by
    /// <c>SelectStatementModelCompoundSelectTests</c>; it is not duplicated here.
    /// </remarks>
    /// <param name="sql">The statement.</param>
    /// <param name="expectedBlocks">The expected block count.</param>
    [Theory]
    [MemberData(nameof(SetOperatorSplits))]
    public void TheSetOperators_SplitAtDepthZeroOnly(string sql, int expectedBlocks)
    {
        SelectStatementModel model = Parsed(sql);

        Assert.Equal(expectedBlocks, model.GetSelectCount());
        AssertByteExact(sql, model.GetSql(), "Splitting must not disturb the round trip.");
    }

    /// <summary>
    /// A per-block accessor reads from the RIGHT block, and modifying one block leaves every other
    /// block byte-identical.
    /// </summary>
    /// <remarks>
    /// TIER 3 - STRUCTURAL. Each block emits verbatim while unmodified, so the blast radius of a
    /// modification is exactly one block. Asserted on the SECOND block specifically, because an
    /// implementation that resolved every index to the first block would still pass a test that only
    /// modified block one - and because that is the failure mode a one-based-to-zero-based rebase
    /// produces (hazard R9).
    /// </remarks>
    [Fact]
    public void ModifyingOneBlock_LeavesTheOtherBlocksUntouched()
    {
        SelectStatementModel model =
            Parsed("SELECT a FROM t WHERE a=1 UNION ALL SELECT b FROM u WHERE b=2");

        // The per-block read first, so the modification below is aimed at a block already proved
        // distinct.
        Assert.Equal("a=1", model.GetWhere(FirstSelectIndex));
        Assert.Equal("b=2", model.GetWhere(2));

        Assert.True(model.ModifyWhere(2, Enums.SQL_MS_APPEND, "AND c=3"));

        AssertByteExact(
            "SELECT a FROM t WHERE a=1 UNION ALL SELECT b FROM u WHERE b=2 AND c=3",
            model.GetSql(),
            "Only the addressed block may change.");
        Assert.Equal("a=1", model.GetWhere(FirstSelectIndex));
    }

    // ==========================================================================================
    //  THE THREE MODIFY STYLES - THIS FILE'S OWN SUBJECT
    //  ------------------------------------------------------------------------------------------
    //  Division of responsibility, stated so neither file grows into the other: ClauseModifierTests
    //  owns the SQL_MS_* UPSERT COLLECTION - the one-clause-per-select-index accumulation and the
    //  caller-side non-positive-index-or-empty-clause guard at :L269 and :L286. THIS file owns
    //  Modify<K>'s own TEXT TRANSFORMATION: what each style does to the clause and to the emitted
    //  statement.
    // ==========================================================================================

    /// <summary>Style, the fragment supplied and the statement it must produce.</summary>
    public static TheoryData<long, string, string> ModifyStyleTransformations =>
        new()
        {
            { Enums.SQL_MS_REPLACE, "y=2", "SELECT a FROM t WHERE y=2 ORDER BY a" },
            { Enums.SQL_MS_APPEND, "AND y=2", "SELECT a FROM t WHERE x=1 AND y=2 ORDER BY a" },
            { Enums.SQL_MS_PREPEND, "y=2 AND", "SELECT a FROM t WHERE y=2 AND x=1 ORDER BY a" },
        };

    /// <summary>
    /// Replace substitutes the clause text; append and prepend join to it with a single space - and the
    /// emitted statement reflects the change byte for byte.
    /// </summary>
    /// <remarks>
    /// TIER 2 for the SEPARATOR. The single space is the reasonable reading of an unobservable native
    /// detail - it is the minimum that keeps two SQL fragments lexically apart. The ORDER of the join
    /// is the part that must not be got backwards: append puts the new fragment after the existing
    /// text and prepend puts it before, and both readings produce valid SQL from the fragments the
    /// legacy passes, so only an explicit assertion distinguishes them. The append case is the shape
    /// the production code actually builds at <c>n_cst_thread_task_sqlquery.sru:L341</c>.
    /// </remarks>
    /// <param name="style">The modify style.</param>
    /// <param name="fragment">The clause fragment.</param>
    /// <param name="expected">The statement the style must produce.</param>
    [Theory]
    [MemberData(nameof(ModifyStyleTransformations))]
    public void TheThreeStyles_ReplaceAppendAndPrependTheClauseText(
        long style,
        string fragment,
        string expected)
    {
        SelectStatementModel model = Parsed("SELECT a FROM t WHERE x=1 ORDER BY a");

        Assert.True(model.ModifyWhere(style, fragment));
        AssertByteExact(expected, model.GetSql(), $"Style {style} must transform the clause exactly.");
    }

    /// <summary>
    /// REPLACE WITH EMPTY TEXT REMOVES the clause outright, taking its keyword and its preceding gap
    /// with it - the exact call three of the five paging arms use to strip the ordering.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TIER 1 - TRACEABLE, from three independent locators that all annotate the call as stripping the
    /// clause. <c>ModifyOrder(Enums.SQL_MS_REPLACE,"")</c> appears at
    /// <c>n_cst_thread_task_sqlquery.sru:L353</c> (the SQL Server unique-index row-number arm),
    /// <c>:L377</c> (the SQL Server plain row-number arm) and <c>:L390</c> (the Oracle arm), the last
    /// two carrying the inline comment that says so outright. It is corroborated structurally twice
    /// over: <c>:L831</c> guards the count path's equivalent call with a presence test first, and
    /// <c>:L356</c>, <c>:L382</c> and <c>:L394</c> all embed the result in a derived table, where a
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
        // The WHERE clause, which is what makes the "keyword and its gap go too" property visible.
        SelectStatementModel model = Parsed("SELECT a FROM t WHERE x=1 ORDER BY a");

        Assert.True(model.ModifyWhere(Enums.SQL_MS_REPLACE, string.Empty));

        AssertByteExact("SELECT a FROM t ORDER BY a", model.GetSql(), "The clause and its keyword go.");
        Assert.False(model.HasWhere());
        Assert.Equal(string.Empty, model.GetWhere());
        Assert.DoesNotContain("WHERE", model.GetSql(), StringComparison.OrdinalIgnoreCase);

        // THE PAGING CALL ITSELF: ModifyOrder(SQL_MS_REPLACE, "") at :L353, :L377 and :L390.
        SelectStatementModel stripped = Parsed("SELECT * FROM COMPANY WHERE age > 30 ORDER BY age ASC");

        Assert.True(stripped.HasOrder());
        Assert.True(stripped.ModifyOrder(Enums.SQL_MS_REPLACE, string.Empty));

        Assert.False(stripped.HasOrder());
        Assert.Equal(string.Empty, stripped.GetOrder());
        AssertByteExact(
            "SELECT * FROM COMPANY WHERE age > 30",
            stripped.GetSql(),
            "The stripped statement must be embeddable in a derived table.");
        Assert.DoesNotContain("ORDER", stripped.GetSql(), StringComparison.OrdinalIgnoreCase);

        // The WHERE clause it did NOT touch is untouched, so stripping is not a reset.
        Assert.True(stripped.HasWhere());
        Assert.Equal("age > 30", stripped.GetWhere());
    }


    /// <summary>
    /// The removal rule applies uniformly to all six clause kinds, including the two whose removal
    /// leaves the statement invalid.
    /// </summary>
    /// <remarks>
    /// The faithful generalisation of a single native implementation: clearing the column or table
    /// clause yields text that is no longer a well-formed <c>SELECT</c>, and that is the CALLER's
    /// responsibility exactly as it is in the legacy, whose native carries no guard against it either.
    /// Adding a guard here would be a behaviour change dressed up as robustness (C-B), so the
    /// degenerate results are asserted rather than prevented.
    /// </remarks>
    [Fact]
    public void TheRemovalRule_AppliesToAllSixClauseKindsIncludingTheDegenerateOnes()
    {
        const string source = "SELECT a FROM t WHERE b=1 GROUP BY a HAVING c>1 ORDER BY a";

        SelectStatementModel order = Parsed(source);
        Assert.True(order.ModifyOrder(Enums.SQL_MS_REPLACE, string.Empty));
        AssertByteExact("SELECT a FROM t WHERE b=1 GROUP BY a HAVING c>1", order.GetSql(), "ORDER BY removal.");

        SelectStatementModel having = Parsed(source);
        Assert.True(having.ModifyHaving(Enums.SQL_MS_REPLACE, string.Empty));
        AssertByteExact("SELECT a FROM t WHERE b=1 GROUP BY a ORDER BY a", having.GetSql(), "HAVING removal.");

        SelectStatementModel group = Parsed(source);
        Assert.True(group.ModifyGroup(Enums.SQL_MS_REPLACE, string.Empty));
        AssertByteExact("SELECT a FROM t WHERE b=1 HAVING c>1 ORDER BY a", group.GetSql(), "GROUP BY removal.");

        SelectStatementModel where = Parsed(source);
        Assert.True(where.ModifyWhere(Enums.SQL_MS_REPLACE, string.Empty));
        AssertByteExact("SELECT a FROM t GROUP BY a HAVING c>1 ORDER BY a", where.GetSql(), "WHERE removal.");

        // The two degenerate ones. Not prevented, and not tidied.
        SelectStatementModel table = Parsed("SELECT a FROM t WHERE b=1");
        Assert.True(table.ModifyTable(Enums.SQL_MS_REPLACE, string.Empty));
        AssertByteExact("SELECT a WHERE b=1", table.GetSql(), "FROM removal is not prevented.");

        SelectStatementModel column = Parsed("SELECT a FROM t WHERE b=1");
        Assert.True(column.ModifyColumn(Enums.SQL_MS_REPLACE, string.Empty));
        AssertByteExact(" FROM t WHERE b=1", column.GetSql(), "Select-list removal is not prevented.");
    }

    /// <summary>The two joining styles, which both treat empty text as a successful no-op.</summary>
    public static TheoryData<long> JoiningStyles =>
        new() { Enums.SQL_MS_APPEND, Enums.SQL_MS_PREPEND };

    /// <summary>
    /// APPEND OR PREPEND OF EMPTY TEXT is a no-op that reports SUCCESS.
    /// </summary>
    /// <remarks>
    /// TIER 2, but a MEASURED REQUIREMENT rather than a convenience.
    /// <c>n_cst_thread_task_sqlquery.sru:L341</c> appends <c>sUniqueColumnsOrderBy</c> to the order-by
    /// clause, and that string is legitimately empty whenever every unique-index column already appears
    /// in the existing order-by - the loop at <c>:L334-L337</c> only adds to it when one does not. So
    /// "nothing to add, nothing went wrong" is the reading that keeps that call site working. What must
    /// NOT happen here is the non-positive-index-or-empty-clause rejection the CALLER applies at
    /// <c>:L269</c> and <c>:L286</c>: that guard belongs to the layer above and is owned by
    /// <c>ClauseModifierTests</c>, and reproducing it here would turn <c>:L341</c> into a failure.
    /// </remarks>
    /// <param name="style">The joining style.</param>
    [Theory]
    [MemberData(nameof(JoiningStyles))]
    public void AppendingOrPrependingEmptyText_ChangesNothingAndReportsSuccess(long style)
    {
        const string source = "SELECT a FROM t WHERE x=1 ORDER BY a";
        SelectStatementModel model = Parsed(source);

        Assert.True(model.ModifyOrder(style, string.Empty));

        AssertByteExact(source, model.GetSql(), "An empty join must change nothing at all.");
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
        AssertByteExact("SELECT a FROM t ORDER BY a", absent.GetSql(), "No separator before the new text.");

        SelectStatementModel alsoAbsent = Parsed("SELECT a FROM t");
        Assert.True(alsoAbsent.ModifyOrder(Enums.SQL_MS_PREPEND, "a"));
        Assert.Equal("a", alsoAbsent.GetOrder());

        SelectStatementModel bare = Parsed("SELECT A FROM T WHERE   ORDER BY B");
        Assert.True(bare.ModifyWhere(Enums.SQL_MS_APPEND, "x=1"));
        Assert.Equal("x=1", bare.GetWhere());
    }

    /// <summary>Style values that are not one of the three the legacy declares.</summary>
    /// <remarks>
    /// Zero is included deliberately - it is not one of the three declared styles, and it is the value
    /// an uninitialised PowerScript <c>long</c> would carry.
    /// </remarks>
    public static TheoryData<long> UnrecognisedStyles =>
        new() { 0L, 4L, 99L, -1L, long.MaxValue, long.MinValue };

    /// <summary>
    /// An UNRECOGNISED style changes nothing and reports failure.
    /// </summary>
    /// <remarks>
    /// TIER 1 as a failure channel: the legacy caller treats a false return as an error at
    /// <c>n_cst_thread_task_sqlquery.sru:L691</c> and <c>:L698</c>, raising an internal error and
    /// abandoning the retrieve.
    /// </remarks>
    /// <param name="style">The unrecognised style.</param>
    [Theory]
    [MemberData(nameof(UnrecognisedStyles))]
    public void AnUnrecognisedStyle_ChangesNothingAndReportsFailure(long style)
    {
        const string source = "SELECT a FROM t WHERE x=1 ORDER BY a";
        SelectStatementModel model = Parsed(source);

        Assert.False(model.ModifyWhere(style, "y=2"));

        AssertByteExact(source, model.GetSql(), "A refused modification must leave the text alone.");
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
        AssertByteExact("SELECT a FROM t", replacing.GetSql(), "Null replaces as empty, so it removes.");
        Assert.False(replacing.HasWhere());

        SelectStatementModel appending = Parsed("SELECT a FROM t WHERE x=1");
        Assert.True(appending.ModifyWhere(Enums.SQL_MS_APPEND, null!));
        AssertByteExact("SELECT a FROM t WHERE x=1", appending.GetSql(), "Null appends as nothing.");
    }

    /// <summary>
    /// All six clause kinds are wired to their OWN slot in both arities: modifying one changes exactly
    /// one.
    /// </summary>
    /// <remarks>
    /// The 36-member surface is generated from one core, so the risk is a single member wired to the
    /// wrong clause kind - a copy-and-paste slip that no compiler catches because every signature is
    /// identical. Each kind is therefore modified in isolation against a statement where every clause
    /// has a DISTINCT body, and the OTHER five are asserted unchanged, which is what localises such a
    /// slip.
    /// </remarks>
    [Fact]
    public void EveryClauseKind_IsWiredToItsOwnSlotInBothArities()
    {
        const string source = "SELECT c1 FROM t2 WHERE w3 GROUP BY g4 HAVING h5 ORDER BY o6";
        string[] originals = ["c1", "t2", "w3", "g4", "h5", "o6"];

        Type type = typeof(SelectStatementModel);

        for (int target = 0; target < ClauseKinds.Length; target++)
        {
            string clause = ClauseKinds[target];
            MethodInfo shortModify = type.GetMethod("Modify" + clause, [typeof(long), typeof(string)])!;
            MethodInfo indexedModify =
                type.GetMethod("Modify" + clause, [typeof(int), typeof(long), typeof(string)])!;

            SelectStatementModel viaShort = Parsed(source);
            SelectStatementModel viaIndexed = Parsed(source);

            Assert.True((bool)shortModify.Invoke(viaShort, [Enums.SQL_MS_REPLACE, "Z"])!, clause);
            Assert.True(
                (bool)indexedModify.Invoke(viaIndexed, [FirstSelectIndex, Enums.SQL_MS_REPLACE, "Z"])!,
                clause);

            // Both arities produce the identical statement.
            AssertByteExact(viaShort.GetSql(), viaIndexed.GetSql(), $"{clause}: arities must agree.");

            // EXACTLY ONE clause changed, and it is the one addressed.
            for (int other = 0; other < ClauseKinds.Length; other++)
            {
                MethodInfo read = type.GetMethod("Get" + ClauseKinds[other], Type.EmptyTypes)!;
                string actual = (string)read.Invoke(viaShort, null)!;
                string expected = other == target ? "Z" : originals[other];

                AssertByteExact(
                    expected,
                    actual,
                    $"Modifying {clause} must not disturb {ClauseKinds[other]}.");
            }
        }
    }


    // ==========================================================================================
    //  SOURCE GEOMETRY - THE REASON BYTE-EXACT PARITY IS ACHIEVABLE AT ALL
    // ==========================================================================================

    /// <summary>
    /// A modified clause is spliced into the statement's OWN geometry: the introducer keeps its source
    /// spelling and both surrounding gaps are reused exactly.
    /// </summary>
    /// <remarks>
    /// TIER 3 - STRUCTURAL. A lower-case <c>where</c> stays lower case and a doubled space inside
    /// <c>group  by</c> stays doubled, because the introducer is stored as the input spelled it rather
    /// than canonicalised. An implementation that emitted the canonical upper-case keyword for a clause
    /// it had modified would produce valid SQL that differs from the legacy's byte for byte - which is
    /// the entire failure mode the acceptance criterion exists to catch.
    /// </remarks>
    [Fact]
    public void AModifiedClause_KeepsTheIntroducerSpellingAndSpacingTheInputUsed()
    {
        SelectStatementModel lowerCase =
            Parsed("select a from t where b=1 group  by a having c>1 order   by a");
        Assert.True(lowerCase.ModifyWhere(Enums.SQL_MS_REPLACE, "b=2"));
        AssertByteExact(
            "select a from t where b=2 group  by a having c>1 order   by a",
            lowerCase.GetSql(),
            "Keyword casing and every gap must survive a modification.");

        SelectStatementModel doubledSpace = Parsed("select a from t where b=1 group  by a");
        Assert.True(doubledSpace.ModifyGroup(Enums.SQL_MS_REPLACE, "z"));
        AssertByteExact(
            "select a from t where b=1 group  by z",
            doubledSpace.GetSql(),
            "A doubled space inside a two-word introducer stays doubled.");
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
        AssertByteExact(
            "select a from t where x=1 GROUP BY a order by a",
            model.GetSql(),
            "A new GROUP BY must land before the existing ORDER BY.");

        Assert.True(model.ModifyHaving(Enums.SQL_MS_REPLACE, "c>1"));
        AssertByteExact(
            "select a from t where x=1 GROUP BY a HAVING c>1 order by a",
            model.GetSql(),
            "A new HAVING must land between GROUP BY and ORDER BY.");
    }

    /// <summary>
    /// THE PAGING IDIOM: saving a clause, clearing it and restoring it returns the statement to the
    /// input byte for byte.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TIER 3 - STRUCTURAL, and the single case that most directly protects the production workload.
    /// The legacy does exactly this during a paging build: it saves the order-by at
    /// <c>n_cst_thread_task_sqlquery.sru:L376</c>, clears it at <c>:L377</c> so the statement can be
    /// embedded in a derived table, and restores it at <c>:L360</c>. The column list gets the same
    /// treatment - saved at <c>:L339</c>, replaced at <c>:L345</c> and <c>:L355</c>, restored at
    /// <c>:L348</c> and <c>:L358</c>.
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
        AssertByteExact("SELECT a  FROM t ", model.GetSql(), "The cleared form keeps its own geometry.");

        Assert.True(model.ModifyOrder(Enums.SQL_MS_REPLACE, saved));
        AssertByteExact(source, model.GetSql(), "Save, clear and restore must be exactly the identity.");

        // The same round trip on the column list, which the unique-index arms save and restore.
        SelectStatementModel columns = Parsed(FixtureRetrieve);
        string savedColumns = columns.GetColumn();

        Assert.True(columns.ModifyColumn(Enums.SQL_MS_REPLACE, "ID,ROW_NUMBER() OVER (ORDER BY ID) AS pfwPagedSQL_RN"));
        AssertByteExact(
            "SELECT ID,ROW_NUMBER() OVER (ORDER BY ID) AS pfwPagedSQL_RN FROM COMPANY",
            columns.GetSql(),
            "The row-number column list is spliced in verbatim, sentinel included.");

        Assert.True(columns.ModifyColumn(Enums.SQL_MS_REPLACE, savedColumns));
        AssertByteExact(FixtureRetrieve, columns.GetSql(), "Restoring the column list is the identity.");
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
        AssertByteExact("select a from t", removed.GetSql(), "Removal strips the keyword.");
        Assert.True(removed.ModifyOrder(Enums.SQL_MS_APPEND, "b"));
        AssertByteExact("select a from t order  by b", removed.GetSql(), "Re-adding reuses the spelling.");

        SelectStatementModel neverHad = Parsed("select a from t");
        Assert.True(neverHad.ModifyOrder(Enums.SQL_MS_APPEND, "b"));
        AssertByteExact("select a from t ORDER BY b", neverHad.GetSql(), "A never-present clause is canonical.");
    }

    /// <summary>
    /// Clearing EVERY clause leaves an empty statement while the block count stays at one, so an empty
    /// emission does not mean "not parsed".
    /// </summary>
    /// <remarks>
    /// MEASURED, and worth pinning because the two zero-ish states are easy to conflate.
    /// <c>GetSelectCount</c> is zero exactly when the PARSE failed; an empty <c>GetSql</c> can also
    /// arise from a caller having cleared everything. A caller detecting parse failure must therefore
    /// use the count, not the text - which is what the factory's documentation says and what the
    /// factory test below relies on.
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
        AssertByteExact("SELECT z", model.GetSql(), "A statement can be rebuilt from an empty one.");
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

        AssertByteExact(model.GetSql(), model.GetSql(), "Two reads of an untouched model must agree.");

        string before = model.GetSql();
        Assert.True(model.ModifyWhere(Enums.SQL_MS_APPEND, "AND c=2"));
        string after = model.GetSql();

        AssertByteExact(after, model.GetSql(), "Two reads of a modified model must agree.");
        Assert.NotEqual(before, after);
        Assert.Equal("b=1 AND c=2", model.GetWhere());
        Assert.Equal("b=1 AND c=2", model.GetWhere());
    }


    // ==========================================================================================
    //  THE FACTORY - parsesql.srf, DEFECTS INCLUDED
    // ==========================================================================================

    /// <summary>
    /// PRESERVED LEGACY DEFECT (C-B): the factory DISCARDS the parse result, so it always returns a
    /// LIVE BUT UNPARSED instance for a statement it could not parse, and a caller has to interrogate
    /// the model to learn that parsing failed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The legacy factory is three statements - create, parse, return - and it throws the boolean away
    /// [<c>parsesql.srf:L11-L13</c>]:
    /// <c>sqlParser = Create n_sql</c>, then <c>sqlParser.Parse(sql)</c> with the result assigned to
    /// nothing at all, then <c>return sqlParser</c>. A malformed statement therefore yields a live but
    /// unparsed object rather than a null or an exception.
    /// </para>
    /// <para>
    /// REPRODUCED EXACTLY, AND DELIBERATELY NOT CORRECTED. The method does not throw, does not return
    /// null and is not reshaped into a try-pattern, because every one of those would be a behaviour
    /// change dressed up as robustness - precisely what constraint C-B forbids. What the caller CAN do
    /// is exactly what a legacy caller could: read <c>GetSelectCount()</c>, which is zero if and only
    /// if the parse failed. That is asserted below so the detection route is pinned alongside the
    /// defect.
    /// </para>
    /// <para>
    /// THE COMPANION DEFECT IS A LEAK, IT IS WORSE THAN IT LOOKS, AND IT IS DELIBERATELY NOT
    /// REPRODUCED. <c>parsesql.srf</c> never destroys the object, so ownership transfers silently to
    /// the caller, who must <c>Destroy</c> it. The one measured consumer does destroy its own directly
    /// created instances - <c>Destroy sqlParser</c> at
    /// <c>n_cst_thread_task_sqlquery.sru:L401</c> and <c>if IsValid(sqlParser) then Destroy sqlParser</c>
    /// at <c>:L874</c> - but that destroy is placed where TWO EARLY RETURNS BYPASS IT:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <c>:L314-L316</c> - the parse-failed arm returns <c>RetCode.E_INTERNAL_ERROR</c> immediately
    /// after the instance was created at <c>:L312</c>, so the instance is leaked;
    /// </item>
    /// <item>
    /// <c>:L396-L398</c> - the <c>case else</c> arm, reached for any database type that is neither
    /// SQL Server nor Oracle, returns <c>RetCode.E_NO_IMPLEMENTATION</c> from INSIDE the
    /// <c>choose case</c>, and is likewise leaked.
    /// </item>
    /// </list>
    /// <para>
    /// Both leak for the same structural reason: <c>end choose</c> is at <c>:L399</c> and
    /// <c>Destroy sqlParser</c> sits at <c>:L401</c>, AFTER it, so only the fall-through path reaches
    /// the destroy. In .NET the lifetime belongs to the garbage collector, so there is nothing to
    /// reproduce and nothing to leak; <see cref="IDisposable"/> is deliberately NOT implemented to
    /// "mirror" the destroy either, because the model owns no unmanaged resource and inventing a
    /// disposal contract would put an obligation on every caller that the subject does not need. The
    /// asymmetry is the point: the OBSERVABLE defect (the discarded boolean) is preserved, and the
    /// UNOBSERVABLE one (the leak) is documented here and not carried across.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFactory_DiscardsTheParseResultAndAlwaysReturnsALiveInstance()
    {
        // A statement the model cannot parse. The factory still hands back a live object.
        SelectStatementModel malformed = SelectStatementModel.ParseSql("SELECT a FROM (t");

        Assert.NotNull(malformed);
        Assert.Equal(0, malformed.GetSelectCount());
        Assert.Equal(string.Empty, malformed.GetSql());
        Assert.Equal(string.Empty, malformed.GetSqlWithoutTerminator());
        Assert.False(malformed.HasColumn());
        Assert.Equal(string.Empty, malformed.GetColumn());

        // AND IT IS STILL USABLE RATHER THAN POISONED: every modifier reports failure, and none throws.
        Assert.False(malformed.ModifyWhere(Enums.SQL_MS_REPLACE, "x=1"));
        Assert.False(malformed.ModifyColumn(FirstSelectIndex, Enums.SQL_MS_REPLACE, "x"));

        // It can be re-parsed into a working state, which is what makes returning it defensible at all.
        Assert.True(malformed.Parse(FixtureRetrieve));
        Assert.Equal(1, malformed.GetSelectCount());

        // The well-formed arm, so the assertion above is not satisfied by a factory that always fails.
        SelectStatementModel wellFormed = SelectStatementModel.ParseSql(FixtureRetrieve);
        Assert.Equal(1, wellFormed.GetSelectCount());
        AssertByteExact(FixtureRetrieve, wellFormed.GetSql(), "The factory parses a good statement.");

        // The oracle driver's own default statement, through the factory.
        SelectStatementModel oracleDefault = SelectStatementModel.ParseSql(OracleDriverDefault);
        Assert.Equal(1, oracleDefault.GetSelectCount());
        AssertByteExact(OracleDriverDefault, oracleDefault.GetSql(), "w_test_sqlparser.srw:L716.");

        // NO DISPOSAL CONTRACT IS INVENTED for a type that owns no unmanaged resource - see the leak
        // discussion above.
        Assert.DoesNotContain(typeof(IDisposable), typeof(SelectStatementModel).GetInterfaces());
        Assert.DoesNotContain(typeof(IAsyncDisposable), typeof(SelectStatementModel).GetInterfaces());
    }

    /// <summary>
    /// THE LEGACY GLOBAL AUTO-INSTANCE IS NOT REPRODUCED: the port exposes no static or global instance
    /// of the model, so the only way to obtain one is to construct it or to call the factory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>n_sql.sru:L51</c> declares <c>global n_sql n_sql</c> - a global auto-instantiated variable
    /// whose NAME IS ITS OWN TYPE NAME. That is legal in PowerScript because there are no namespaces:
    /// every global object lives in one flat namespace whose symbol resolution is decided by the
    /// ordering of the target's library list, and a name may therefore denote both a type and a
    /// variable at once.
    /// </para>
    /// <para>
    /// AAP 0.4.5.1 and 0.5.4.2 fix the collision-resolution rule for exactly this case, and it is not
    /// a matter of taste: the TYPE keeps the descriptive .NET name - <c>SelectStatementModel</c> - and
    /// the INSTANCE becomes an injected dependency rather than a global. So there is no
    /// <c>SelectStatementModel.Instance</c>, no <c>Default</c>, no <c>Current</c>, no lazily cached
    /// singleton and no static field of its own type anywhere on the surface. This is asserted rather
    /// than merely documented because a "convenient" static instance is the single most likely
    /// regression here, and it would be a genuine behavioural hazard rather than a style lapse: the
    /// legacy SQL task layer is worker-thread-affine, the model carries per-statement mutable state,
    /// and a shared instance would introduce a concurrency defect the legacy does not have.
    /// </para>
    /// <para>
    /// The one static member the type does declare is the factory itself, which is a FUNCTION and not
    /// an instance - it reproduces <c>parsesql.srf</c>, a global FUNCTION object, and it returns a fresh
    /// object on every call.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoStaticOrGlobalInstanceOfTheModelExists()
    {
        Type type = typeof(SelectStatementModel);
        const BindingFlags anyStatic =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        // NO static field, public or private, holds an instance of the model.
        Assert.DoesNotContain(type.GetFields(anyStatic), field => field.FieldType == type);

        // NO static property exposes one either - the `Instance`/`Default`/`Current` shapes.
        Assert.DoesNotContain(type.GetProperties(anyStatic), property => property.PropertyType == type);

        // NO mutable static state of ANY type, so two instances cannot influence one another.
        Assert.DoesNotContain(
            type.GetFields(anyStatic),
            field => !field.IsLiteral && !field.IsInitOnly);

        // The only static METHOD is the factory, and it hands back a new object each time.
        MethodInfo[] staticMethods = type
            .GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Static)
            .ToArray();
        Assert.Single(staticMethods);
        Assert.Equal("ParseSql", staticMethods[0].Name);

        Assert.NotSame(
            SelectStatementModel.ParseSql(FixtureRetrieve),
            SelectStatementModel.ParseSql(FixtureRetrieve));
    }

    /// <summary>
    /// The factory produces a fresh, independent instance on every call.
    /// </summary>
    /// <remarks>
    /// Matching the legacy, which creates a new <c>n_sql</c> at
    /// <c>n_cst_thread_task_sqlquery.sru:L312</c> and again at <c>:L531</c> rather than reusing one.
    /// </remarks>
    [Fact]
    public void EveryInstance_IsIndependentOfEveryOther()
    {
        SelectStatementModel first = SelectStatementModel.ParseSql("SELECT a FROM t WHERE x=1");
        SelectStatementModel second = SelectStatementModel.ParseSql("SELECT b FROM u");
        SelectStatementModel untouched = new();

        Assert.NotSame(first, second);
        Assert.True(first.ModifyWhere(Enums.SQL_MS_REPLACE, "y=2"));

        AssertByteExact("SELECT a FROM t WHERE y=2", first.GetSql(), "The modified instance changed.");
        AssertByteExact("SELECT b FROM u", second.GetSql(), "Its sibling did not.");
        Assert.Equal(0, untouched.GetSelectCount());
    }


    // ==========================================================================================
    //  THE FIXTURE, AND THE THREE ARTEFACTS THE PAGING LAYER BUILDS ON TOP OF THIS MODEL
    // ==========================================================================================

    /// <summary>
    /// The golden-master fixture's own retrieve statement parses, extracts and rewrites as the paging
    /// layer needs - including the count-wrapper's <c>1 AS _</c> alias.
    /// </summary>
    /// <remarks>
    /// <c>retrieve="SELECT * FROM COMPANY"</c> [<c>dw_sqlite.srd:L14</c>] is the statement every
    /// characterization recording of the retrieve / validate / update triple starts from. The
    /// count-wrapper shape is the one the legacy builds at
    /// <c>n_cst_thread_task_sqlquery.sru:L830</c>, where the select list is replaced with the literal
    /// alias <c>1 AS _</c> - asserted verbatim because the alias is one of the observable sentinels
    /// parity is measured on. <c>:L831-L832</c> then strips the order-by, guarded by a presence test,
    /// which is the second half of the same shape and is asserted here too.
    /// </remarks>
    [Fact]
    public void TheFixtureRetrieveStatement_SupportsTheShapesThePagingLayerBuilds()
    {
        SelectStatementModel model = Parsed(FixtureRetrieve);

        Assert.Equal("*", model.GetColumn());
        Assert.Equal("COMPANY", model.GetTable());
        Assert.False(model.HasWhere());
        Assert.False(model.HasOrder());

        // THE COUNT WRAPPER'S FIRST ARTEFACT - n_cst_thread_task_sqlquery.sru:L830.
        Assert.True(model.ModifyColumn(Enums.SQL_MS_REPLACE, "1 AS _"));
        AssertByteExact("SELECT 1 AS _ FROM COMPANY", model.GetSql(), "The count alias is spliced verbatim.");

        // THE COUNT WRAPPER'S SECOND ARTEFACT - :L831-L832, presence-guarded order-by strip.
        SelectStatementModel counted = Parsed("SELECT * FROM COMPANY ORDER BY age ASC");
        Assert.True(counted.ModifyColumn(Enums.SQL_MS_REPLACE, "1 AS _"));
        Assert.True(counted.HasOrder());
        Assert.True(counted.ModifyOrder(Enums.SQL_MS_REPLACE, string.Empty));
        AssertByteExact("SELECT 1 AS _ FROM COMPANY", counted.GetSql(), "Count path: alias in, ordering out.");

        // A filtered, sorted retrieve built from the same statement, which is the clause-application
        // loop's own output shape [:L691, :L698, :L703].
        SelectStatementModel filtered = Parsed(FixtureRetrieve);
        Assert.True(filtered.ModifyWhere(Enums.SQL_MS_REPLACE, "age > 30"));
        Assert.True(filtered.ModifyOrder(Enums.SQL_MS_REPLACE, "age ASC"));
        AssertByteExact(
            "SELECT * FROM COMPANY WHERE age > 30 ORDER BY age ASC",
            filtered.GetSql(),
            "The applied where and order-by clauses land in canonical position.");
    }

    /// <summary>
    /// <c>ModifyTable</c> with APPEND grafts an <c>INNER JOIN</c> onto the FROM clause - the mechanism
    /// BOTH unique-index SQL Server paging arms use to wrap the outer query.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TIER 1 - TRACEABLE, and this is the single most load-bearing use of APPEND in the estate.
    /// <c>n_cst_thread_task_sqlquery.sru:L350</c> and <c>:L362</c> both call
    /// <c>sqlParser.ModifyTable(Enums.SQL_MS_APPEND, "INNER JOIN (" + sql + ") pfwPagedSQL_OutterTbl ON "
    /// + sUniqueColumnsWhere)</c> - <c>:L350</c> in the engine-paging arm and <c>:L362</c> in the
    /// row-number arm - where <c>sql</c> is a complete inner SELECT built moments earlier and
    /// <c>sUniqueColumnsWhere</c> is the join predicate accumulated at <c>:L332-L333</c>.
    /// </para>
    /// <para>
    /// Three properties have to hold together, and each is asserted: the join text is appended AFTER the
    /// existing table text rather than before it, it is joined with exactly one space, and the ORDER BY
    /// that follows the FROM clause stays where it is - because the appended fragment is a whole
    /// parenthesised subquery containing its own <c>ORDER BY</c> and its own <c>WHERE</c>, none of which
    /// may be mistaken for a depth-zero clause of the outer statement. That last point is why the
    /// payload here is a realistic one lifted from the arm's own shape rather than a token string: a
    /// model that re-scanned the appended text would relocate the outer ordering and produce SQL that
    /// still parses and is wrong.
    /// </para>
    /// <para>
    /// The sentinel <c>pfwPagedSQL_OutterTbl</c> is spelled exactly as the legacy spells it, doubled
    /// <c>t</c> included. It is one of the observable identifiers parity is measured on, so it is
    /// carried verbatim and never corrected.
    /// </para>
    /// </remarks>
    [Fact]
    public void ModifyTableWithAppend_GraftsAnInnerJoinOntoTheFromClause()
    {
        const string innerJoin =
            "INNER JOIN (SELECT ID FROM COMPANY ORDER BY ID OFFSET 10 ROWS FETCH NEXT 10 ROWS ONLY) "
            + "pfwPagedSQL_OutterTbl ON pfwPagedSQL_OutterTbl.ID = ID";

        // The shape of :L350 - append onto a statement that already carries an ORDER BY.
        SelectStatementModel ordered = Parsed("SELECT ID, NAME FROM COMPANY ORDER BY ID");

        Assert.True(ordered.ModifyTable(Enums.SQL_MS_APPEND, innerJoin));

        AssertByteExact(
            "COMPANY " + innerJoin,
            ordered.GetTable(),
            "The join must be appended after the table, joined by one space.");
        AssertByteExact(
            "SELECT ID, NAME FROM COMPANY " + innerJoin + " ORDER BY ID",
            ordered.GetSql(),
            "The outer ORDER BY must stay after the enlarged FROM clause.");

        // The appended subquery's OWN clauses are not mistaken for the outer statement's.
        Assert.False(ordered.HasWhere());
        Assert.Equal("ID", ordered.GetOrder());
        Assert.Equal(1, ordered.GetSelectCount());

        // And the same graft onto the fixture's own retrieve statement, which has no ORDER BY at all.
        SelectStatementModel fixture = Parsed(FixtureRetrieve);
        Assert.True(fixture.ModifyTable(Enums.SQL_MS_APPEND, innerJoin));
        AssertByteExact(
            "SELECT * FROM COMPANY " + innerJoin,
            fixture.GetSql(),
            "The graft works on the evidenced retrieve statement too.");
        Assert.Contains("pfwPagedSQL_OutterTbl", fixture.GetSql(), StringComparison.Ordinal);
    }

    /// <summary>Clause payloads that must pass through the model unchanged.</summary>
    /// <remarks>
    /// Obviously synthetic, per the C-F self-audit in the file header. The point of each is that the
    /// model does not react to it: an injection-shaped disjunction, an escaped apostrophe, a trailing
    /// line comment and a nested SELECT.
    /// </remarks>
    public static TheoryData<string> OpaqueClausePayloads =>
        new()
        {
            "1=1 OR 'a'='a'",
            "x = 'O''Brien'",
            "x = 1 -- trailing comment",
            "x IN (SELECT y FROM other)",
        };

    /// <summary>
    /// A clause is treated as OPAQUE TEXT and is never re-parsed, escaped or validated - which is the
    /// mechanical root of the legacy's SQL-injection exposure, preserved deliberately (C-B).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The where-clause setter takes a RAW clause string and splices it in
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
    /// What is asserted is only that the text passes through UNCHANGED.
    /// </para>
    /// </remarks>
    /// <param name="clause">The opaque clause payload.</param>
    [Theory]
    [MemberData(nameof(OpaqueClausePayloads))]
    public void AClauseIsOpaqueText_NeitherEscapedNorValidated(string clause)
    {
        SelectStatementModel model = Parsed(FixtureRetrieve);

        Assert.True(model.ModifyWhere(Enums.SQL_MS_REPLACE, clause));

        AssertByteExact(clause, model.GetWhere(), "The clause is stored verbatim.");
        AssertByteExact(
            "SELECT * FROM COMPANY WHERE " + clause,
            model.GetSql(),
            "The clause is emitted verbatim.");
    }


    // ==========================================================================================
    //  SCANNER AND EMISSION EDGE CASES
    // ==========================================================================================

    /// <summary>Compound statements whose segments are not select blocks in their own right.</summary>
    public static TheoryData<string> SegmentsWithoutAClauseIntroducer =>
        new()
        {
            "SELECT a FROM t UNION x",
            "SELECT a FROM t UNION ",
            "SELECT a FROM t UNION",
            "SELECT a FROM t UNION(SELECT b FROM u)",
            "SELECT a FROM t UNION (SELECT b FROM u)",
            "SELECT a FROM t INTERSECT",
            "SELECT a FROM t EXCEPT ",
        };

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
    /// <param name="sql">The rejected compound statement.</param>
    [Theory]
    [MemberData(nameof(SegmentsWithoutAClauseIntroducer))]
    public void ASegmentWithNoClauseIntroducer_IsRejected(string sql)
    {
        SelectStatementModel model = new();

        Assert.False(model.Parse(sql));
        Assert.Equal(0, model.GetSelectCount());
    }

    /// <summary>Statement and the select list its doubled closer must yield.</summary>
    public static TheoryData<string, string> DoubledClosers =>
        new()
        {
            { "SELECT 'O''Brien' FROM t", "'O''Brien'" },
            { "SELECT 'a''' FROM t", "'a'''" },
            { "SELECT [a]]b] FROM t", "[a]]b]" },
            { "SELECT \"a\"\"b\" FROM t", "\"a\"\"b\"" },
            { "SELECT 'from t' , 'where x' FROM t", "'from t' , 'where x'" },
        };

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
    /// <param name="sql">The statement.</param>
    /// <param name="expectedColumn">The select list it must yield.</param>
    [Theory]
    [MemberData(nameof(DoubledClosers))]
    public void ADoubledCloser_IsAnEscapeRatherThanATerminator(string sql, string expectedColumn)
    {
        SelectStatementModel model = Parsed(sql);

        AssertByteExact(expectedColumn, model.GetColumn(), "The escaped closer stays inside the literal.");
        Assert.Equal("t", model.GetTable());
        AssertByteExact(sql, model.GetSql(), "The scan resumed in the right place.");
    }

    /// <summary>Statement, its select list and its table clause when a keyword runs on.</summary>
    public static TheoryData<string, string, string> RunOnKeywords =>
        new()
        {
            { "SELECT a FROMAGE b", "a FROMAGE b", "" },
            { "SELECT a FROM t WHEREVER x", "a", "t WHEREVER x" },
            { "SELECT a FROM t GROUPS BY a", "a", "t GROUPS BY a" },
            { "SELECT a FROM t ORDERLY BY a", "a", "t ORDERLY BY a" },
            { "SELECT a FROM t HAVINGS c", "a", "t HAVINGS c" },
        };

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
    /// <param name="sql">The statement.</param>
    /// <param name="expectedColumn">The select list.</param>
    /// <param name="expectedTable">The table clause.</param>
    [Theory]
    [MemberData(nameof(RunOnKeywords))]
    public void AKeywordRunningOnIntoAnIdentifier_IsNotAnIntroducer(
        string sql,
        string expectedColumn,
        string expectedTable)
    {
        SelectStatementModel model = Parsed(sql);

        AssertByteExact(expectedColumn, model.GetColumn(), "The select list absorbs the run-on word.");
        AssertByteExact(expectedTable, model.GetTable(), "So does the table clause when it is open.");
        Assert.False(model.HasWhere());
        Assert.False(model.HasGroup());
        Assert.False(model.HasHaving());
        Assert.False(model.HasOrder());
        AssertByteExact(sql, model.GetSql(), "And the statement still round-trips.");
    }

    /// <summary>Statement and its table clause when a two-word introducer is malformed.</summary>
    public static TheoryData<string, string> MalformedTwoWordIntroducers =>
        new()
        {
            { "SELECT a FROM t GROUP,BY a", "t GROUP,BY a" },
            { "SELECT a FROM t ORDER,BY a", "t ORDER,BY a" },
            { "SELECT a FROM t GROUP X", "t GROUP X" },
            { "SELECT a FROM t ORDER X", "t ORDER X" },
            { "SELECT a FROM t GROUP", "t GROUP" },
            { "SELECT a FROM t ORDER", "t ORDER" },
            { "SELECT a FROM t GROUP BYE a", "t GROUP BYE a" },
        };

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
    /// <param name="sql">The statement.</param>
    /// <param name="expectedTable">The table clause that absorbs the malformed introducer.</param>
    [Theory]
    [MemberData(nameof(MalformedTwoWordIntroducers))]
    public void ATwoWordIntroducer_NeedsAGapAndTheRightSecondWord(string sql, string expectedTable)
    {
        SelectStatementModel model = Parsed(sql);

        AssertByteExact(expectedTable, model.GetTable(), "The malformed introducer stays in the table clause.");
        Assert.False(model.HasGroup());
        Assert.False(model.HasOrder());
        AssertByteExact(sql, model.GetSql(), "And the statement still round-trips.");
    }

    /// <summary>Statement, its select list and its table clause across the three line-comment endings.</summary>
    public static TheoryData<string, string, string> LineCommentTerminators =>
        new()
        {
            { "SELECT a -- c\r FROM t", "a -- c", "t" },
            { "SELECT a -- c\r\n FROM t", "a -- c", "t" },
            { "SELECT a -- c\n FROM t", "a -- c", "t" },
            { "SELECT a FROM t -- trailing", "a", "t -- trailing" },
        };

    /// <summary>
    /// A line comment ends at a carriage return, at a line feed, or at the end of the statement.
    /// </summary>
    /// <remarks>
    /// All three terminators matter for the round trip. A comment terminated only by a carriage return -
    /// a legitimately old-fashioned but perfectly legal line ending - must not swallow the rest of the
    /// statement, and a comment running to the very end of the text must not run off it.
    /// </remarks>
    /// <param name="sql">The statement.</param>
    /// <param name="expectedColumn">The select list.</param>
    /// <param name="expectedTable">The table clause.</param>
    [Theory]
    [MemberData(nameof(LineCommentTerminators))]
    public void ALineComment_EndsAtEitherLineEndingOrAtTheEndOfTheStatement(
        string sql,
        string expectedColumn,
        string expectedTable)
    {
        SelectStatementModel model = Parsed(sql);

        AssertByteExact(expectedColumn, model.GetColumn(), "The comment stays inside the open clause.");
        AssertByteExact(expectedTable, model.GetTable(), "And the next clause is still found.");
        AssertByteExact(sql, model.GetSql(), "And the statement round-trips.");
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
        AssertByteExact(
            "SELECT A FROM T WHERE   ORDER BY C",
            viaOrder.GetSql(),
            "The bare keyword and its three spaces survive.");
        Assert.True(viaOrder.HasWhere());
        Assert.Equal(string.Empty, viaOrder.GetWhere());

        SelectStatementModel viaColumn = Parsed("SELECT A FROM T WHERE   ORDER BY B");
        Assert.True(viaColumn.ModifyColumn(Enums.SQL_MS_REPLACE, "Z"));
        AssertByteExact(
            "SELECT Z FROM T WHERE   ORDER BY B",
            viaColumn.GetSql(),
            "A modification upstream of it does not disturb it either.");
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
        AssertByteExact("SELECT (A)FROM u", viaTable.GetSql(), "A single space is substituted.");

        // The degenerate consequence, preserved deliberately.
        SelectStatementModel viaColumn = Parsed("SELECT(A)FROM t");
        Assert.True(viaColumn.ModifyColumn(Enums.SQL_MS_REPLACE, "B"));
        AssertByteExact("SELECT BFROM t", viaColumn.GetSql(), "The absent pre-gap stays absent.");
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
        AssertByteExact("GROUP BY x", model.GetSql(), "No leading space on an empty buffer.");

        Assert.True(model.ModifyOrder(Enums.SQL_MS_REPLACE, "y"));
        AssertByteExact("GROUP BY x ORDER BY y", model.GetSql(), "And one space between the two.");
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
    /// rather than as a coincidence. It is the read-side counterpart of
    /// <see cref="EveryClauseKind_IsWiredToItsOwnSlotInBothArities"/>, and it mirrors the oracle
    /// driver's own all-six read at <c>w_test_sqlparser.srw:L249-L254</c>.
    /// </remarks>
    [Fact]
    public void AllTwelveIndexedReadMembers_AddressTheirOwnClause()
    {
        SelectStatementModel model =
            Parsed("SELECT c1 FROM t2 WHERE w3 GROUP BY g4 HAVING h5 ORDER BY o6");

        Assert.True(model.HasColumn(FirstSelectIndex));
        Assert.True(model.HasTable(FirstSelectIndex));
        Assert.True(model.HasWhere(FirstSelectIndex));
        Assert.True(model.HasGroup(FirstSelectIndex));
        Assert.True(model.HasHaving(FirstSelectIndex));
        Assert.True(model.HasOrder(FirstSelectIndex));

        Assert.Equal("c1", model.GetColumn(FirstSelectIndex));
        Assert.Equal("t2", model.GetTable(FirstSelectIndex));
        Assert.Equal("w3", model.GetWhere(FirstSelectIndex));
        Assert.Equal("g4", model.GetGroup(FirstSelectIndex));
        Assert.Equal("h5", model.GetHaving(FirstSelectIndex));
        Assert.Equal("o6", model.GetOrder(FirstSelectIndex));

        // The short arity agrees with the indexed one for all six.
        Assert.Equal(model.GetColumn(FirstSelectIndex), model.GetColumn());
        Assert.Equal(model.GetTable(FirstSelectIndex), model.GetTable());
        Assert.Equal(model.GetWhere(FirstSelectIndex), model.GetWhere());
        Assert.Equal(model.GetGroup(FirstSelectIndex), model.GetGroup());
        Assert.Equal(model.GetHaving(FirstSelectIndex), model.GetHaving());
        Assert.Equal(model.GetOrder(FirstSelectIndex), model.GetOrder());

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
    /// would put an unversioned type on the assembly's public surface, and so on a boundary that AAP
    /// 0.4.3 reserves for the published contracts alone - fails a test.
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
