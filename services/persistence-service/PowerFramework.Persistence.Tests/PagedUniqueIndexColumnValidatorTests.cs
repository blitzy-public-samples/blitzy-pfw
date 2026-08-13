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
                out string? reason,
                out IReadOnlyList<string>? resolved));

        // The offending value and the reason are available to the CALL SITE for a server-side log; they
        // are what the dispatcher deliberately discards rather than returning.
        Assert.Equal("NOT_A_COLUMN", rejected);
        Assert.Equal(PagedUniqueIndexColumnValidator.UnknownColumnReason, reason);

        // NOTHING IS RESOLVED ON A REFUSAL, so a caller that ignored the boolean cannot splice a
        // partially resolved list.
        Assert.Null(resolved);
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
                out string? reason,
                out IReadOnlyList<string>? resolved));

        Assert.Null(rejected);
        Assert.Null(reason);

        // THE RESOLUTION, WHICH IS THE HALF A REVIEW FOUND MISSING. Three of the four spellings name a
        // SOURCE column and resolve to THEMSELVES, character for character, so byte-exact statement
        // parity is untouched for every request that was already correct. The fourth - `person` - names
        // only the statement's OUTPUT ALIAS, and resolves to the source term `c.NAME`, because the arm
        // splices these identifiers into the sub-query's select list [:L331, :L345] where an alias names
        // no column at all.
        Assert.Equal(["id", "C.NAME", "c.NAME", "c.id"], resolved);
    }

    [Fact]
    public void AnOutputAliasResolvesToItsSourceTermRatherThanBeingSplicedAsItself()
    {
        // THE DEFECT THIS TEST EXISTS FOR, stated on its own rather than folded into the case above. A
        // validator that adds every whitespace-delimited token of `c.id AS ident` to one flat set admits
        // `ident` as though it named a source column - and the arm then emits
        // `SELECT ident FROM COMPANY c`, projecting a column the table does not have, plus a join
        // predicate on the same non-existent name. Admitted by the guard whose job is to prevent it.
        SelectStatementModel statement = Parsed("SELECT c.id AS ident, c.NAME nm FROM COMPANY c");

        Assert.True(
            PagedUniqueIndexColumnValidator.TryValidate(
                ["ident", "nm"],
                statement,
                out string? _,
                out string? _,
                out IReadOnlyList<string>? resolved));

        // BOTH ALIAS FORMS RESOLVE - the explicit `AS` form and the implicit two-token form.
        Assert.Equal(["c.id", "c.NAME"], resolved);
    }

    [Fact]
    public void ASourceSpellingWinsOverAnAliasThatCollidesWithIt()
    {
        // `SELECT a AS b, b FROM t` makes `b` both an alias of `a` and a real column. The SOURCE meaning
        // must win: it is the meaning the sub-query's own FROM clause has for the name, and it is also the
        // behaviour that existed before resolution was introduced, so nothing correct changes.
        SelectStatementModel statement = Parsed("SELECT a AS b, b FROM t");

        Assert.True(
            PagedUniqueIndexColumnValidator.TryValidate(
                ["b"],
                statement,
                out string? _,
                out string? _,
                out IReadOnlyList<string>? resolved));

        Assert.Equal(["b"], resolved);
    }

    [Fact]
    public void AQualifiedSpellingOfAnUnqualifiedColumnIsSplicedAsTheCallerWroteIt()
    {
        // A caller who writes `c.ID` against a statement that selects a bare `ID` has `c.ID`
        // spliced, and that statement is valid - the qualifier names a table the sub-query's FROM clause
        // still carries. Resolution must confirm membership by trailing segment WITHOUT replacing the
        // caller's spelling, or it would change output that is already correct.
        SelectStatementModel statement = Parsed(EnumeratedSelect);

        Assert.True(
            PagedUniqueIndexColumnValidator.TryValidate(
                ["c.ID"],
                statement,
                out string? _,
                out string? _,
                out IReadOnlyList<string>? resolved));

        Assert.Equal(["c.ID"], resolved);
    }

    [Fact]
    public void AnAliasThatIsNotAnIdentifierIsNotAdmittedAtAll()
    {
        // A quoted alias cannot be spliced, so recording it would only re-create the defect. The term is
        // opaque, which leaves the whole map unenforceable and the lexical rule as the enforced bound -
        // and the quoted alias itself is therefore neither resolved nor refused by membership.
        SelectStatementModel statement = Parsed("SELECT c.id AS \"the id\" FROM COMPANY c");

        Assert.True(
            PagedUniqueIndexColumnValidator.TryValidate(
                ["anything_at_all"],
                statement,
                out string? _,
                out string? _,
                out IReadOnlyList<string>? resolved));

        // Unenforceable, so the caller's own spelling passes through unchanged.
        Assert.Equal(["anything_at_all"], resolved);
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
                out string? _,
                out IReadOnlyList<string>? resolved));

        // UNENFORCEABLE MEANS UNRESOLVED TOO: the caller's own spellings pass through unchanged, because
        // there is nothing to resolve against and inventing a resolution would be worse than leaving the
        // lexical check as the enforced bound.
        Assert.Equal(["ID", "ANY_COLUMN_AT_ALL"], resolved);

        // The security control does NOT relax with it, which is the property that matters.
        Assert.False(
            PagedUniqueIndexColumnValidator.TryValidate(
                ["ID; DROP TABLE COMPANY--"],
                statement,
                out string? rejected,
                out string? reason,
                out IReadOnlyList<string>? _));

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
                out string? _,
                out IReadOnlyList<string>? _));
    }

    [Fact]
    public void AnEmptyListValidatesTriviallyWhileAListHoldingAnEmptyStringDoesNot()
    {
        SelectStatementModel statement = Parsed(EnumeratedSelect);

        // An empty LIST is the documented "clear" [:L405-L406] and is not an empty IDENTIFIER.
        Assert.True(
            PagedUniqueIndexColumnValidator.TryValidate(
                [],
                statement,
                out string? _,
                out string? _,
                out IReadOnlyList<string>? _));

        Assert.False(
            PagedUniqueIndexColumnValidator.TryValidate(
                [""],
                statement,
                out string? _,
                out string? reason,
                out IReadOnlyList<string>? _));

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
                out string? reason,
                out IReadOnlyList<string>? _));

        Assert.Equal(string.Empty, rejected);
        Assert.Equal(PagedUniqueIndexColumnValidator.MalformedReason, reason);
    }

    [Fact]
    public void TheValidatorRefusesNullArguments()
    {
        SelectStatementModel statement = Parsed(EnumeratedSelect);

        Assert.Throws<ArgumentNullException>(
            () => PagedUniqueIndexColumnValidator.TryValidate(null!, statement, out _, out _, out _));
        Assert.Throws<ArgumentNullException>(
            () => PagedUniqueIndexColumnValidator.TryValidate([], null!, out _, out _, out _));
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

    // ==========================================================================================
    //  THE ALIAS RESOLUTION, ASSERTED ON THE GENERATED SQL
    // ==========================================================================================

    [Fact]
    public void AnAliasedUniqueIndexColumnProducesASubQueryOverTheSourceColumn()
    {
        // THE END-TO-END FORM OF THE DEFECT. Before resolution, `ident` reached the arm unchanged and the
        // arm emitted `SELECT ident, ROW_NUMBER() ...` over COMPANY - a projection of a column no table
        // has - and joined on `pfwPagedSQL_OutterTbl.ident = ident`. Admitted by the guard whose job was
        // to prevent exactly that class of statement.
        const string sql = "SELECT c.ID AS ident, c.NAME FROM COMPANY c";

        PagingRewriteRequest request = new(
            sql,
            pageSize: 10L,
            pageIndex: 2L,
            pageNative: false,
            pagedUniqueIndexColumns: ["ident"]);

        PagingRewriteResult result =
            PagingRewriteDispatcher.Rewrite(in request, DatabaseType.DbtMssql, BothArms);

        Assert.True(result.IsOk);

        // The SOURCE column is what the sub-query projects and what the join predicate names.
        Assert.Contains("pfwPagedSQL_OutterTbl.ID = c.ID", result.RewrittenSql, StringComparison.Ordinal);

        // AND THE ALIAS IS NOWHERE IN THE GENERATED TEXT except where the statement's own select list
        // legitimately carries it - so the assertion is made on the join predicate, which is the fragment
        // the caller's identifier feeds.
        Assert.DoesNotContain(
            "pfwPagedSQL_OutterTbl.ident",
            result.RewrittenSql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ASourceSpellingStillReachesTheArmCharacterForCharacter()
    {
        // The other half of the same property: a caller who names the source column - in ANY casing -
        // gets that casing spliced, not the select list's. A resolution that normalised casing would
        // change generated SQL for a request that was already correct.
        const string sql = "SELECT c.ID AS ident, c.NAME FROM COMPANY c";

        PagingRewriteRequest request = new(
            sql,
            pageSize: 10L,
            pageIndex: 2L,
            pageNative: false,
            pagedUniqueIndexColumns: ["id"]);

        PagingRewriteResult result =
            PagingRewriteDispatcher.Rewrite(in request, DatabaseType.DbtMssql, BothArms);

        Assert.True(result.IsOk);
        Assert.Contains("pfwPagedSQL_OutterTbl.id = id", result.RewrittenSql, StringComparison.Ordinal);
    }

    // ==========================================================================================
    //  THE OVERFLOW GUARD - A WRAPPED BOUND IS WORSE THAN AN ERROR
    //  ------------------------------------------------------------------------------------------
    //  Every arm multiplies the page size by the page index [:L346, :L356, :L372, :L382, :L395]. An
    //  unchecked product wraps to a NEGATIVE number, and `TOP -N`, `OFFSET -N` and a `BETWEEN` over
    //  negative bounds are all syntactically fine and silently match no row - so the caller receives
    //  an empty page with no indication that anything went wrong. That is the one outcome worse than
    //  either a correct page or a reported fault.
    // ==========================================================================================

    /// <param name="pageSize">The page size.</param>
    /// <param name="pageIndex">The one-based page ordinal.</param>
    [Theory]
    [InlineData(long.MaxValue, 2L)]
    [InlineData(2L, long.MaxValue)]
    [InlineData(long.MaxValue, long.MaxValue)]
    [InlineData(3_037_000_500L, 3_037_000_500L)]
    public void AProductThatCannotBeRepresentedIsRefusedBeforeAnySqlIsRendered(
        long pageSize,
        long pageIndex)
    {
        PagingRewriteRequest request = new(
            StarSelect,
            pageSize,
            pageIndex,
            pageNative: false);

        Assert.True(request.HasValidPagingBounds, "The oracle's own bounds test must still pass.");
        Assert.False(request.HasRepresentablePagingProducts);

        foreach (DatabaseType dialect in new[] { DatabaseType.DbtMssql, DatabaseType.DbtOracle })
        {
            PagingRewriteResult result =
                PagingRewriteDispatcher.Rewrite(in request, dialect, BothArms);

            // THE SAME OUTCOME AS THE ORACLE'S OWN BOUNDS TEST, not a fourth one: it is the same class of
            // fault, and a new code would put something on the wire no legacy caller has an arm for.
            Assert.Equal(RetCode.E_INVALID_ARGUMENT, result.ReturnCode);
            Assert.Equal(PagingRewriteResult.InvalidPagingSettingText, result.ErrorText);

            // NOTHING WAS RENDERED. This is the assertion that separates "refused" from "rendered a
            // negative bound and then reported it".
            Assert.Equal(string.Empty, result.RewrittenSql);
        }
    }

    /// <param name="pageSize">The page size.</param>
    /// <param name="pageIndex">The one-based page ordinal.</param>
    [Theory]
    [InlineData(1L, 1L)]
    [InlineData(10L, 2L)]
    [InlineData(long.MaxValue, 1L)]
    [InlineData(1L, long.MaxValue)]
    [InlineData(3_037_000_499L, 3_037_000_499L)]
    public void AProductAtOrBelowTheLimitIsStillAccepted(long pageSize, long pageIndex)
    {
        // THE BOUNDARY IS EXERCISED FROM BOTH SIDES, because a guard that refused one page too many would
        // be a narrowing nobody asked for. 3037000499 squared is the largest square below long.MaxValue.
        PagingRewriteRequest request = new(
            StarSelect,
            pageSize,
            pageIndex,
            pageNative: false);

        Assert.True(request.HasRepresentablePagingProducts);

        PagingRewriteResult result =
            PagingRewriteDispatcher.Rewrite(in request, DatabaseType.DbtMssql, BothArms);

        Assert.True(result.IsOk);
        Assert.NotEqual(string.Empty, result.RewrittenSql);
    }

    [Fact]
    public void TheBoundsGuardStillWinsOverTheOverflowGuard()
    {
        // A non-positive value fails BOTH tests, and the oracle's own arm must be the one that answers -
        // which it is, because HasRepresentablePagingProducts includes the bounds test rather than
        // replacing it. The two share a code and a text here, so this asserts the ordering does not
        // divide by zero rather than a distinguishable message.
        PagingRewriteRequest request = new(StarSelect, pageSize: 10L, pageIndex: 0L, pageNative: false);

        Assert.False(request.HasValidPagingBounds);
        Assert.False(request.HasRepresentablePagingProducts);

        PagingRewriteResult result =
            PagingRewriteDispatcher.Rewrite(in request, DatabaseType.DbtMssql, BothArms);

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, result.ReturnCode);
        Assert.Equal(PagingRewriteResult.InvalidPagingSettingText, result.ErrorText);
    }

    [Fact]
    public void AnArmReachedDirectlyWithAnUnrepresentableProductThrowsRatherThanRenderingIt()
    {
        // THE BACKSTOP. An arm reached directly - which is to say from a test - bypasses the dispatcher's
        // guard, and `checked` at the render sites is what makes that a loud failure instead of a
        // plausible-looking statement built on a negative bound.
        PagingRewriteRequest request = new(
            StarSelect,
            pageSize: long.MaxValue,
            pageIndex: 2L,
            pageNative: false);

        Assert.Throws<OverflowException>(
            () => new SqlServerPagingRewriter().Rewrite(in request, Parsed(StarSelect)));
        Assert.Throws<OverflowException>(
            () => new OraclePagingRewriter().Rewrite(in request, Parsed(StarSelect)));
    }

    // ==========================================================================================
    //  THE TERMINATOR, THROUGH THE REWRITERS
    // ==========================================================================================

    /// <param name="dialect">The dialect whose arm to drive.</param>
    /// <param name="pageNative">Which of the dialect's arms to select.</param>
    [Theory]
    [InlineData(DatabaseType.DbtMssql, false)]
    [InlineData(DatabaseType.DbtMssql, true)]
    [InlineData(DatabaseType.DbtOracle, false)]
    public void ATerminatedStatementIsRewrittenWithTheTerminatorOnlyAtTheVeryEnd(
        DatabaseType dialect,
        bool pageNative)
    {
        // THE DEFECT, END TO END. Every rewriter embeds the parsed statement inside a larger one, so a
        // semicolon retained in the body landed in the MIDDLE of the generated SQL and everything after
        // it - the OFFSET, the BETWEEN, the aliases, the trailing ORDER BY - became unreachable.
        PagingRewriteRequest terminated = new(
            StarSelect + ";",
            pageSize: 10L,
            pageIndex: 2L,
            pageNative);

        PagingRewriteResult result =
            PagingRewriteDispatcher.Rewrite(in terminated, dialect, BothArms);

        Assert.True(result.IsOk);

        // EXACTLY ONE terminator, and it is the LAST character of the statement.
        Assert.Equal(1, result.RewrittenSql.Count(character => character == ';'));
        Assert.EndsWith(";", result.RewrittenSql, StringComparison.Ordinal);

        // AND THE REST IS BYTE-IDENTICAL to the same request without a terminator, which is what proves
        // the separation changed nothing else.
        PagingRewriteRequest bare = new(StarSelect, pageSize: 10L, pageIndex: 2L, pageNative);

        PagingRewriteResult expected = PagingRewriteDispatcher.Rewrite(in bare, dialect, BothArms);

        Assert.True(expected.IsOk);
        Assert.Equal(expected.RewrittenSql + ";", result.RewrittenSql, StringComparer.Ordinal);
    }

    [Fact]
    public void ATerminatedStatementIsRewrittenCorrectlyOnTheUniqueIndexArmToo()
    {
        // The fourth arm, which the theory above cannot reach because it needs a unique-index column.
        PagingRewriteRequest terminated = new(
            EnumeratedSelect + ";",
            pageSize: 10L,
            pageIndex: 2L,
            pageNative: false,
            pagedUniqueIndexColumns: ["ID"]);

        PagingRewriteResult result =
            PagingRewriteDispatcher.Rewrite(in terminated, DatabaseType.DbtMssql, BothArms);

        Assert.True(result.IsOk);
        Assert.Equal(1, result.RewrittenSql.Count(character => character == ';'));
        Assert.EndsWith(";", result.RewrittenSql, StringComparison.Ordinal);

        // The inner sub-query the arm folded into the INNER JOIN carries no terminator, which is the
        // specific position the defect used to put one in.
        Assert.DoesNotContain(";)", result.RewrittenSql, StringComparison.Ordinal);
        Assert.Contains("pfwPagedSQL_OutterTbl", result.RewrittenSql, StringComparison.Ordinal);
    }

    [Fact]
    public void AMultiStatementRequestIsRefusedByTheParseGuard()
    {
        // The narrowing, seen from the dispatcher: multi-statement input reaches the parse guard and takes
        // the oracle's own fail-fast arm [:L314-L317] rather than being rewritten into two statements.
        PagingRewriteRequest request = new(
            StarSelect + "; DELETE FROM COMPANY",
            pageSize: 10L,
            pageIndex: 1L,
            pageNative: false);

        PagingRewriteResult result =
            PagingRewriteDispatcher.Rewrite(in request, DatabaseType.DbtMssql, BothArms);

        Assert.Equal(RetCode.E_INTERNAL_ERROR, result.ReturnCode);
        Assert.Equal(PagingRewriteResult.ParseFailedText, result.ErrorText);
        Assert.Equal(string.Empty, result.RewrittenSql);
    }

    private static SelectStatementModel Parsed(string sql)
    {
        SelectStatementModel statement = new();

        Assert.True(statement.Parse(sql));

        return statement;
    }
}
