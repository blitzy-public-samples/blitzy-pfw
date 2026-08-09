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
//  TIERS, following the sibling SelectStatementModelTests convention:
//    TIER 1 - TRACEABLE:  the guard of :L271 / :L288, the upsert order, the two-arity delegation,
//                         the reset, the presence test, the three style constants.
//    TIER 2 - DECIDED:    the structural guard's refused-word set and its scanner rules. The legacy
//                         has no such guard, so nothing measures these - they implement the
//                         published contract text on SqlClauseSpec.clause, and that text is the
//                         specification they are checked against.
//    TIER 3 - PORT-LOCAL: null tolerance, which PowerScript cannot express at all.
//
//  NO DATABASE, NO CLOCK, NO I/O. The system under test is in-memory bookkeeping over strings, so
//  every case below is a pure function call. No fixture is fabricated and none is needed.
// ==============================================================================================

using System.Globalization;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// The legacy guard, the upsert semantics, the reset and the presence test.
/// </summary>
public sealed class ClauseModifierTests
{
    /// <summary>A style value that is legal, so no case below fails for the wrong reason.</summary>
    private const long AnyLegalStyle = ClauseModifier.SQL_MS_REPLACE;

    // ==========================================================================================
    //  THE LEGACY GUARD - :L271 and :L288
    // ==========================================================================================

    /// <summary>
    /// TIER 1. A non-positive select index is refused, and ZERO DOES NOT MEAN "the first select".
    /// </summary>
    /// <param name="selectIndex">The refused index.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void ANonPositiveSelectIndexIsRefusedAndStoresNothing(int selectIndex)
    {
        ClauseModifier modifier = new();

        Assert.Equal(
            RetCode.E_INVALID_ARGUMENT,
            modifier.SetWhereClause(selectIndex, AnyLegalStyle, "age > 30"));
        Assert.Equal(
            RetCode.E_INVALID_ARGUMENT,
            modifier.SetOrderByClause(selectIndex, AnyLegalStyle, "age A"));

        Assert.Empty(modifier.WhereClauses);
        Assert.Empty(modifier.OrderByClauses);
        Assert.False(modifier.HasPendingClauses);
    }

    /// <summary>
    /// TIER 1 for the empty string, TIER 3 for null: both are refused with the one code the legacy
    /// answers, and PowerScript has no null to have had behaviour for.
    /// </summary>
    /// <param name="clause">The refused clause.</param>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void AnEmptyOrNullClauseIsRefusedAndStoresNothing(string? clause)
    {
        ClauseModifier modifier = new();

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, modifier.SetWhereClause(AnyLegalStyle, clause));
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, modifier.SetOrderByClause(AnyLegalStyle, clause));

        Assert.Empty(modifier.WhereClauses);
        Assert.Empty(modifier.OrderByClauses);
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

    // ==========================================================================================
    //  THE UPSERT - THE TRAP THIS SUITE EXISTS FOR
    // ==========================================================================================

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
    //  THE WIRE-TO-LEGACY STYLE MAPPING
    // ==========================================================================================

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
