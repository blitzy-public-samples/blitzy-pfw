// ==============================================================================================
//  PagedUniqueIndexColumnValidatorTests - THE ONE TRUST BOUNDARY THIS SERVICE ADDS TO THE ORACLE
//  --------------------------------------------------------------------------------------------
//  SYSTEM UNDER TEST  Sql/Paging/IPagingRewriter.cs
//                         PagedUniqueIndexColumnValidator - the lexical and membership rules
//                         PagingRewriteDispatcher.Rewrite  - where the rule is enforced
//                         PagingRewriteResult.InvalidPagedUniqueIndexColumn - the fifth shape
//                     Sql/Paging/SqlServerPagingRewriter.cs   ConsumesPagedUniqueIndexColumns = true
//                     Sql/Paging/OraclePagingRewriter.cs      ConsumesPagedUniqueIndexColumns = false
//
//  BEHAVIOURAL ORACLE ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru
//                         :L303-L310  the paging-bounds guard, which must still fire first
//                         :L312-L317  the parse guard, which must still fire second
//                         :L320-L398  the dialect choose case, which must still fire third
//                         :L328-L338  the loop that CONCATENATES each identifier into three places
//                         :L386-L395  the arm that never reads the collection at all
//                     ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14   `SELECT * FROM COMPANY`
//                     ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L463-L469  the sole DDL
//                     all READ ONLY per constraint C-C.
//
//  WHY THIS SUITE EXISTS
//  --------------------------------------------------------------------------------------------
//  `QuerySpec.paged_unique_index_columns` is `repeated string` on a published contract, and the first
//  dialect arm concatenates each element into the select list, the join predicate and the ORDER BY
//  unquoted and unescaped. An identifier cannot travel as a bind parameter, because the emitted
//  identifier text IS the output byte-exact parity is measured against - so the only available control
//  is to validate before splicing. Every caller here is authenticated, which is precisely why
//  authentication is not the boundary: the exposure is what an authenticated caller can make the
//  statement say.
//
//  TWO PROPERTIES CARRY THE WHOLE SUITE, AND THE SECOND IS THE ONE THAT IS EASY TO LOSE:
//
//    1. A CRAFTED VALUE IS REFUSED. Asserted per attack shape rather than in aggregate, so a
//       regression names the shape it let through.
//    2. A LEGITIMATE VALUE PRODUCES THE SAME STATEMENT, BYTE FOR BYTE. Asserted by comparing the
//       dispatched result against the arm invoked DIRECTLY with the same inputs - so the assertion is
//       "validation changed nothing" rather than a transcription of generated SQL that could drift
//       into agreeing with a bug. If validation ever started normalising, trimming or quoting an
//       identifier, this is what fails.
//
//  And the ORDER of the guards is asserted explicitly, because the finding's fix could easily have
//  been placed first and silently displaced three outcomes the legacy defines.
// ==============================================================================================

using PowerFramework.Persistence.Sql.Paging;

namespace PowerFramework.Persistence.Tests;

public sealed class PagedUniqueIndexColumnValidatorTests
{
    // The statement shape the repository actually evidences: a star select list, which cannot enumerate
    // its columns [dw_sqlite.srd:L14].
    private const string StarSelect = "SELECT * FROM COMPANY";

    // An enumerated select list over the sole DDL's columns [w_test_sqlite.srw:L463-L469].
    private const string EnumeratedSelect = "SELECT ID, NAME, AGE, ADDRESS, SALARY, BIRTH FROM COMPANY";

    private static readonly IPagingRewriter[] BothArms =
    [
        new SqlServerPagingRewriter(),
        new OraclePagingRewriter(),
    ];

    // ==========================================================================================
    //  THE LEXICAL RULE - THE SECURITY CONTROL
    // ==========================================================================================

    [Theory]
    [InlineData("ID")]
    [InlineData("id")]
    [InlineData("_id")]
    [InlineData("ID2")]
    [InlineData("EMP$NO")]
    [InlineData("TEMP#1")]
    [InlineData("COMPANY.ID")]
    [InlineData("DB.COMPANY.ID")]
    public void AWellFormedIdentifierIsAccepted(string identifier)
    {
        // The accepted alphabet is the two engines' own: `$` is legal in an Oracle identifier and `#` in
        // a SQL Server one, and both are admitted so no identifier either engine accepts is refused.
        Assert.True(PagedUniqueIndexColumnValidator.IsLexicallyValid(identifier));
    }

    [Theory]
    // THE ATTACK SHAPES, ONE PER ROW. Each is the minimal edit to a legitimate identifier that would
    // make the spliced text stop being one identifier.
    [InlineData("ID; DROP TABLE COMPANY--")]
    [InlineData("ID, (SELECT password FROM users)")]
    [InlineData("1=1 OR ID")]
    [InlineData("ID) UNION ALL SELECT 1,2,3,4,5,6 FROM COMPANY WHERE (1=1")]
    [InlineData("ID--comment")]
    [InlineData("ID/*comment*/")]
    [InlineData("ID'")]
    [InlineData("'ID'")]
    [InlineData("\"ID\"")]
    [InlineData("[ID]")]
    [InlineData("`ID`")]
    [InlineData("ID ")]
    [InlineData(" ID")]
    [InlineData("MY COLUMN")]
    [InlineData("ID+1")]
    [InlineData("COUNT(ID)")]
    [InlineData("ID\tNAME")]
    [InlineData("ID\nNAME")]
    [InlineData("ID\r\nNAME")]
    // Structural malformations of the dotted form.
    [InlineData(".ID")]
    [InlineData("ID.")]
    [InlineData("A..B")]
    [InlineData("SERVER.DB.SCHEMA.COMPANY.ID")]
    // A digit or a symbol may not START a segment.
    [InlineData("1ID")]
    [InlineData("$ID")]
    [InlineData("#ID")]
    // The empty and whitespace-only cases.
    [InlineData("")]
    [InlineData(" ")]
    public void AMalformedIdentifierIsRefused(string identifier)
    {
        Assert.False(PagedUniqueIndexColumnValidator.IsLexicallyValid(identifier));
    }

    [Fact]
    public void NullAndAnOverlongSegmentAreRefused()
    {
        Assert.False(PagedUniqueIndexColumnValidator.IsLexicallyValid(null));

        string atLimit = "A" + new string('B', PagedUniqueIndexColumnValidator.MaximumSegmentLength - 1);
        string overLimit = atLimit + "C";

        Assert.Equal(PagedUniqueIndexColumnValidator.MaximumSegmentLength, atLimit.Length);
        Assert.True(PagedUniqueIndexColumnValidator.IsLexicallyValid(atLimit));
        Assert.False(PagedUniqueIndexColumnValidator.IsLexicallyValid(overLimit));

        // The bound is per SEGMENT, not per identifier, so three segments at the limit are acceptable.
        Assert.True(
            PagedUniqueIndexColumnValidator.IsLexicallyValid($"{atLimit}.{atLimit}.{atLimit}"));
    }

    // ==========================================================================================
    //  THE MEMBERSHIP RULE - DEFENCE IN DEPTH, ENFORCED ONLY WHERE THE STATEMENT CAN ANSWER IT
    // ==========================================================================================

    [Fact]
    public void AnEnumeratedSelectListRefusesAnIdentifierItDoesNotName()
    {
        SelectStatementModel statement = Parsed(EnumeratedSelect);

        Assert.False(
            PagedUniqueIndexColumnValidator.TryValidate(
                ["SALARY", "NOT_A_COLUMN"],
                statement,
                out string? rejected,
                out string? reason));

        // The offending value and the reason are available to the CALL SITE for a server-side log; they
        // are what the dispatcher deliberately discards rather than returning.
        Assert.Equal("NOT_A_COLUMN", rejected);
        Assert.Equal(PagedUniqueIndexColumnValidator.UnknownColumnReason, reason);
    }

    [Fact]
    public void AnEnumeratedSelectListAcceptsItsOwnColumnsInAnyCaseAndEitherSpelling()
    {
        SelectStatementModel statement = Parsed("SELECT c.ID, c.NAME AS person FROM COMPANY c");

        // Case-insensitive, because an unquoted identifier is case-insensitive in both dialects.
        // Qualified or unqualified, because the arm itself strips the qualifier when it builds the join
        // predicate [:L333]. And the ALIAS is accepted as well as the underlying name, because a caller
        // may reasonably write either.
        Assert.True(
            PagedUniqueIndexColumnValidator.TryValidate(
                ["id", "C.NAME", "person", "c.id"],
                statement,
                out string? rejected,
                out string? reason));

        Assert.Null(rejected);
        Assert.Null(reason);
    }

    [Fact]
    public void AStarSelectListLeavesMembershipUnenforcedAndTheLexicalRuleInForce()
    {
        // THIS IS THE CASE THE PRIMARY FIXTURE ACTUALLY USES [dw_sqlite.srd:L14]. A star list enumerates
        // nothing, so "not in the roster" cannot mean "unknown" - enforcing membership here would refuse
        // every legitimate request against the one statement shape the repository evidences.
        SelectStatementModel statement = Parsed(StarSelect);

        Assert.True(
            PagedUniqueIndexColumnValidator.TryValidate(
                ["ID", "ANY_COLUMN_AT_ALL"],
                statement,
                out string? _,
                out string? _));

        // The security control does NOT relax with it, which is the property that matters.
        Assert.False(
            PagedUniqueIndexColumnValidator.TryValidate(
                ["ID; DROP TABLE COMPANY--"],
                statement,
                out string? rejected,
                out string? reason));

        Assert.Equal("ID; DROP TABLE COMPANY--", rejected);
        Assert.Equal(PagedUniqueIndexColumnValidator.MalformedReason, reason);
    }

    [Theory]
    // Each of these select lists is opaque for a different reason, and each therefore leaves membership
    // unenforceable rather than rejecting.
    [InlineData("SELECT * FROM COMPANY")]
    [InlineData("SELECT c.* FROM COMPANY c")]
    [InlineData("SELECT ID, COUNT(*) FROM COMPANY GROUP BY ID")]
    [InlineData("SELECT ID, SALARY * 2 FROM COMPANY")]
    [InlineData("SELECT ID, [NAME] FROM COMPANY")]
    public void AnOpaqueSelectTermLeavesMembershipUnenforceable(string sql)
    {
        SelectStatementModel statement = Parsed(sql);

        Assert.True(
            PagedUniqueIndexColumnValidator.TryValidate(
                ["SOMETHING_NOT_IN_THE_LIST"],
                statement,
                out string? _,
                out string? _));
    }

    [Fact]
    public void AnEmptyListValidatesTriviallyWhileAListHoldingAnEmptyStringDoesNot()
    {
        SelectStatementModel statement = Parsed(EnumeratedSelect);

        // An empty LIST is the documented "clear" [:L405-L406] and is not an empty IDENTIFIER.
        Assert.True(
            PagedUniqueIndexColumnValidator.TryValidate([], statement, out string? _, out string? _));

        Assert.False(
            PagedUniqueIndexColumnValidator.TryValidate(
                [""],
                statement,
                out string? _,
                out string? reason));

        Assert.Equal(PagedUniqueIndexColumnValidator.MalformedReason, reason);
    }

    [Fact]
    public void ANullElementIsTreatedAsMalformedRatherThanDereferenced()
    {
        // A deserialized wire message can carry a null element despite the non-nullable type argument, so
        // this path is reachable and must not throw.
        SelectStatementModel statement = Parsed(EnumeratedSelect);

        Assert.False(
            PagedUniqueIndexColumnValidator.TryValidate(
                [null!],
                statement,
                out string? rejected,
                out string? reason));

        Assert.Equal(string.Empty, rejected);
        Assert.Equal(PagedUniqueIndexColumnValidator.MalformedReason, reason);
    }

    [Fact]
    public void TheValidatorRefusesNullArguments()
    {
        SelectStatementModel statement = Parsed(EnumeratedSelect);

        Assert.Throws<ArgumentNullException>(
            () => PagedUniqueIndexColumnValidator.TryValidate(null!, statement, out _, out _));
        Assert.Throws<ArgumentNullException>(
            () => PagedUniqueIndexColumnValidator.TryValidate([], null!, out _, out _));
    }

    // ==========================================================================================
    //  ENFORCEMENT AT THE DISPATCHER - AND THE GUARD ORDER THAT MUST NOT MOVE
    // ==========================================================================================

    [Fact]
    public void TheDispatcherRefusesACraftedIdentifierWithEInvalidArgument()
    {
        PagingRewriteRequest request = new(
            StarSelect,
            pageSize: 10L,
            pageIndex: 2L,
            pageNative: false,
            pagedUniqueIndexColumns: ["ID; DROP TABLE COMPANY--"]);

        PagingRewriteResult result =
            PagingRewriteDispatcher.Rewrite(in request, DatabaseType.DbtMssql, BothArms);

        Assert.False(result.IsOk);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, result.ReturnCode);
        Assert.Equal(PagingRewriteResult.InvalidPagedUniqueIndexColumnText, result.ErrorText);

        // NO STATEMENT IS PRODUCED, which is the point: nothing was built from the value.
        Assert.Equal(string.Empty, result.RewrittenSql);

        // AND THE DIAGNOSTIC DOES NOT ECHO THE REJECTED VALUE BACK. The value is attacker-controlled by
        // hypothesis, so a message quoting it would make the refusal a channel of its own.
        Assert.DoesNotContain("DROP TABLE", result.ErrorText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("--", result.ErrorText, StringComparison.Ordinal);

        // It is also distinguishable from the bounds guard in a log, even though the CODE is shared.
        Assert.NotEqual(PagingRewriteResult.InvalidPagingSettingText, result.ErrorText);
    }

    [Fact]
    public void TheDispatcherRefusesAnIdentifierTheEnumeratedStatementDoesNotName()
    {
        PagingRewriteRequest request = new(
            EnumeratedSelect,
            pageSize: 10L,
            pageIndex: 1L,
            pageNative: false,
            pagedUniqueIndexColumns: ["NOT_A_COLUMN"]);

        PagingRewriteResult result =
            PagingRewriteDispatcher.Rewrite(in request, DatabaseType.DbtMssql, BothArms);

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, result.ReturnCode);
        Assert.Equal(PagingRewriteResult.InvalidPagedUniqueIndexColumnText, result.ErrorText);
    }

    [Fact]
    public void TheBoundsGuardStillWinsOverTheIdentifierGuard()
    {
        // GUARD ORDER, ASSERTED. A request that is BOTH badly paged and carries a crafted identifier must
        // still report the paging fault with the oracle's own Chinese diagnostic [:L308-L309]. The two
        // share a return code, so only the text can tell them apart - which is why the text differs.
        PagingRewriteRequest request = new(
            StarSelect,
            pageSize: 0L,
            pageIndex: 0L,
            pageNative: false,
            pagedUniqueIndexColumns: ["ID; DROP TABLE COMPANY--"]);

        PagingRewriteResult result =
            PagingRewriteDispatcher.Rewrite(in request, DatabaseType.DbtMssql, BothArms);

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, result.ReturnCode);
        Assert.Equal(PagingRewriteResult.InvalidPagingSettingText, result.ErrorText);
    }

    [Fact]
    public void TheParseGuardStillWinsOverTheIdentifierGuard()
    {
        PagingRewriteRequest request = new(
            string.Empty,
            pageSize: 10L,
            pageIndex: 1L,
            pageNative: false,
            pagedUniqueIndexColumns: ["ID; DROP TABLE COMPANY--"]);

        PagingRewriteResult result =
            PagingRewriteDispatcher.Rewrite(in request, DatabaseType.DbtMssql, BothArms);

        Assert.Equal(RetCode.E_INTERNAL_ERROR, result.ReturnCode);
        Assert.Equal(PagingRewriteResult.ParseFailedText, result.ErrorText);
    }

    [Fact]
    public void TheDialectGuardStillWinsOverTheIdentifierGuard()
    {
        // A protobuf enum is open, so an unrecognised numeric value reaches here as itself and must still
        // take the `case else` arm [:L396-L398] rather than the new rejection.
        PagingRewriteRequest request = new(
            StarSelect,
            pageSize: 10L,
            pageIndex: 1L,
            pageNative: false,
            pagedUniqueIndexColumns: ["ID; DROP TABLE COMPANY--"]);

        PagingRewriteResult result =
            PagingRewriteDispatcher.Rewrite(in request, (DatabaseType)7, BothArms);

        Assert.Equal(RetCode.E_NO_IMPLEMENTATION, result.ReturnCode);
        Assert.Equal(string.Empty, result.ErrorText);
    }

    [Fact]
    public void TheArmThatIgnoresTheCollectionIsNotSubjectToTheGuard()
    {
        // [:L386-L395] never reads _sPagedUniqueIndexColumns, so there is no concatenation to protect and
        // rejecting would be a narrowing with nothing to justify it (C-B). The request is answered.
        PagingRewriteRequest request = new(
            StarSelect,
            pageSize: 10L,
            pageIndex: 2L,
            pageNative: false,
            pagedUniqueIndexColumns: ["ID; DROP TABLE COMPANY--"]);

        PagingRewriteResult result =
            PagingRewriteDispatcher.Rewrite(in request, DatabaseType.DbtOracle, BothArms);

        Assert.True(result.IsOk);
        Assert.NotEqual(string.Empty, result.RewrittenSql);

        // And the ignored value does not appear in what that arm emitted, which is what "ignores" means.
        Assert.DoesNotContain("DROP TABLE", result.RewrittenSql, StringComparison.OrdinalIgnoreCase);

        Assert.False(new OraclePagingRewriter().ConsumesPagedUniqueIndexColumns);
        Assert.True(new SqlServerPagingRewriter().ConsumesPagedUniqueIndexColumns);
    }

    // ==========================================================================================
    //  BYTE-EXACT PARITY FOR A LEGITIMATE IDENTIFIER - THE PROPERTY THE FIX MUST NOT COST
    // ==========================================================================================

    [Theory]
    [InlineData(StarSelect, "ID")]
    [InlineData(StarSelect, "COMPANY.ID")]
    [InlineData(EnumeratedSelect, "ID")]
    [InlineData(EnumeratedSelect, "SALARY")]
    public void ALegitimateIdentifierProducesExactlyTheStatementTheArmProducesUnvalidated(
        string sql,
        string identifier)
    {
        PagingRewriteRequest request = new(
            sql,
            pageSize: 10L,
            pageIndex: 2L,
            pageNative: false,
            pagedUniqueIndexColumns: [identifier]);

        // Through the dispatcher, which now validates.
        PagingRewriteResult dispatched =
            PagingRewriteDispatcher.Rewrite(in request, DatabaseType.DbtMssql, BothArms);

        // And the arm invoked DIRECTLY with the same inputs, which validates nothing - the faithful port
        // of [:L321-L385] with no boundary in front of it.
        SelectStatementModel statement = Parsed(sql);
        PagingRewriteResult direct = new SqlServerPagingRewriter().Rewrite(in request, statement);

        Assert.True(dispatched.IsOk);
        Assert.True(direct.IsOk);

        // THE ASSERTION IS "VALIDATION CHANGED NOTHING", not a transcription of generated SQL. If the
        // validator ever started trimming, quoting or normalising an identifier, this is what fails.
        Assert.Equal(direct.RewrittenSql, dispatched.RewrittenSql, StringComparer.Ordinal);

        // The identifier reaches the statement verbatim, and the unique-index strategy's own sentinel is
        // there - so this really is the inner-join arm rather than the plain form.
        Assert.Contains(identifier, dispatched.RewrittenSql, StringComparison.Ordinal);
        Assert.Contains("pfwPagedSQL_OutterTbl", dispatched.RewrittenSql, StringComparison.Ordinal);
    }

    [Fact]
    public void ARequestWithNoUniqueIndexColumnsIsUntouchedByTheGuard()
    {
        // The plain form. Nothing to validate, and the strategy the empty collection selects [:L323] must
        // be reached exactly as before.
        PagingRewriteRequest request = new(
            StarSelect,
            pageSize: 10L,
            pageIndex: 2L,
            pageNative: false);

        PagingRewriteResult dispatched =
            PagingRewriteDispatcher.Rewrite(in request, DatabaseType.DbtMssql, BothArms);

        SelectStatementModel statement = Parsed(StarSelect);
        PagingRewriteResult direct = new SqlServerPagingRewriter().Rewrite(in request, statement);

        Assert.True(dispatched.IsOk);
        Assert.Equal(direct.RewrittenSql, dispatched.RewrittenSql, StringComparer.Ordinal);
        Assert.DoesNotContain("pfwPagedSQL_OutterTbl", dispatched.RewrittenSql, StringComparison.Ordinal);
    }

    private static SelectStatementModel Parsed(string sql)
    {
        SelectStatementModel statement = new();

        Assert.True(statement.Parse(sql));

        return statement;
    }
}
