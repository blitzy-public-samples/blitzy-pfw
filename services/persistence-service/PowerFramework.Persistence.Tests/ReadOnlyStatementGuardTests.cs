// ==================================================================================================
//  ReadOnlyStatementGuardTests.cs - the read-scope grammar C-05 enforces on caller-authored SQL
// ==================================================================================================
//
//  THE FINDING THIS PINS. Contract C-05 is published under the `persistence.read` scope, and three of
//  its inputs are caller-authored SQL rather than task-generated: QuerySpec.sql, QuerySpec.sql_syntax
//  (whose `retrieve="..."` clause becomes the registered statement) and SqlClauseSpec.clause. The third
//  had a guard already, in Sql/ClauseModifier.cs. The first two had none, and the justification on
//  record for that was that the legacy retrieval task generates SELECT statements and nothing else -
//  which is true of the TASK and says nothing about the SURFACE. A read-scoped credential could
//  therefore reach INSERT, DELETE, DDL, PRAGMA, ATTACH and - because Microsoft.Data.Sqlite executes
//  every statement in a batch it is handed - whole batches spliced behind a semicolon.
//
//  WHY THE ORACLE HAS NOTHING TO COMPARE AGAINST HERE. PowerFramework is a LIBRARY: `of_setsql` is a
//  plain assignment [n_cst_thread_task_sqlquery.sru:L469-L472] with no guard, and it needed none,
//  because the only caller that could reach it was code already running in the same process with the
//  same rights. Decomposition created the first caller whose rights are NARROWER than the process's, so
//  the guard is a REQUIRED ADDITION in the sense AAP 0.4.2.6 uses for the statement redactor, and it
//  narrows the contract with a DEFINED error rather than widening it with a guess (AAP 0.1.5).
//
//  WHAT THIS SUITE COVERS, IN THREE LAYERS.
//    1. The grammar itself, as a theory matrix over Sql/ReadOnlyStatementGuard.cs - both the refusals
//       and, just as importantly, the ACCEPTANCES, because a guard that refused everything would
//       satisfy every refusal assertion and break every real caller.
//    2. What the guard deliberately does NOT refuse, which is the C-B half: the emptiness check and the
//       malformedness check both stay where the legacy put them, at run time.
//    3. The syntax path, judged by the retrieval its `retrieve="..."` clause declares rather than by
//       the blob, because that clause is what the DataWindow runtime registers and executes.
//
//  WHERE THE OTHER HALF LIVES. This suite covers the GRAMMAR. The two BOUNDARIES that enforce it are
//  pinned beside the fixtures that can reach them: C-05's QuerySpec in QueryServiceLeaseTests region 7,
//  and C-08's GridSyntaxFromSql in TransactionServiceTests. Splitting it that way reuses a real session
//  over a real pooled transaction instead of standing a third composition root up here.
//
//  NO DATABASE, NO CONNECTION, NO CLOCK AND NO CONTAINER (constraints C-E, C-H). The guard is pure
//  string inspection, so nothing below needs any of them.
// ==================================================================================================

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Tests for the read-only statement grammar of <c>Sql/ReadOnlyStatementGuard.cs</c> and for the two
/// boundaries that enforce it.
/// </summary>
public sealed class ReadOnlyStatementGuardTests
{
    // ---------------------------------------------------------------------------------------------
    //  1. WHAT IS ACCEPTED - THE HALF A REFUSE-EVERYTHING GUARD WOULD ALSO PASS WITHOUT
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Ordinary read statements are admitted, including the forms three DBMS dialects need.
    /// </summary>
    /// <param name="sql">The statement under test.</param>
    /// <remarks>
    /// THE FIRST ENTRY IS THE ONLY RETRIEVAL EVIDENCED IN THE REPOSITORY
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14</c>], so a guard that refused it would refuse
    /// the fixture the whole update triple is characterized against. The rest are the shapes the paging
    /// rewriters emit and the shapes a caller legitimately sends.
    /// </remarks>
    [Theory]
    [InlineData("SELECT * FROM COMPANY")]
    [InlineData("select * from COMPANY")]
    [InlineData("SELECT ID, NAME FROM COMPANY WHERE AGE > 30 ORDER BY NAME")]
    [InlineData("SELECT 1")]
    [InlineData("SELECT 1;")]
    [InlineData("SELECT 1 ;   ")]
    [InlineData("  \t\r\n SELECT * FROM COMPANY \r\n WHERE AGE > 1 \r\n")]
    [InlineData("WITH recent AS (SELECT * FROM COMPANY) SELECT * FROM recent")]
    [InlineData("VALUES (1), (2)")]
    [InlineData("SELECT CASE WHEN AGE > 30 THEN 'senior' ELSE 'junior' END FROM COMPANY")]
    [InlineData("SELECT replace(NAME, 'a', 'b') FROM COMPANY")]
    [InlineData("SELECT REPLACE (NAME, 'a', 'b') FROM COMPANY")]
    [InlineData("SELECT * FROM COMPANY WHERE NOTE = 'delete me'")]
    [InlineData("SELECT * FROM COMPANY WHERE NOTE = 'it''s here; drop table x'")]
    [InlineData("SELECT * FROM COMPANY WHERE [delete] = 1")]
    [InlineData("SELECT \"update\" FROM COMPANY")]
    [InlineData("SELECT UPDATED_AT, DELETED_FLAG FROM COMPANY")]
    [InlineData("SELECT COUNT(*) FROM (SELECT * FROM COMPANY) T")]
    [InlineData(
        "SELECT * FROM (SELECT ROW_NUMBER() OVER(ORDER BY ID) AS pfwPagedSQL_RN, * FROM COMPANY) "
        + "pfwPagedSQL_TblOuter WHERE pfwPagedSQL_RN BETWEEN 1 AND 10")]
    [InlineData("SELECT 1 AS _ FROM COMPANY")]
    public void AnOrdinaryReadStatementIsAdmitted(string sql) =>
        Assert.True(
            ReadOnlyStatementGuard.IsAdmissibleStatement(sql),
            $"A legitimate read statement was refused: {sql}");

    // ---------------------------------------------------------------------------------------------
    //  2. WHAT IS REFUSED
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A statement whose leading word is not <c>SELECT</c>, <c>WITH</c> or <c>VALUES</c> is refused.
    /// </summary>
    /// <param name="sql">The statement under test.</param>
    [Theory]
    [InlineData("INSERT INTO COMPANY (NAME) VALUES ('x')")]
    [InlineData("UPDATE COMPANY SET NAME = 'x'")]
    [InlineData("DELETE FROM COMPANY")]
    [InlineData("REPLACE INTO COMPANY (ID, NAME) VALUES (1, 'x')")]
    [InlineData("CREATE TABLE T (ID INTEGER)")]
    [InlineData("DROP TABLE COMPANY")]
    [InlineData("ALTER TABLE COMPANY ADD COLUMN X INTEGER")]
    [InlineData("TRUNCATE TABLE COMPANY")]
    [InlineData("PRAGMA journal_mode = WAL")]
    [InlineData("ATTACH DATABASE 'other.db' AS other")]
    [InlineData("DETACH DATABASE other")]
    [InlineData("VACUUM")]
    [InlineData("REINDEX")]
    [InlineData("ANALYZE")]
    [InlineData("BEGIN")]
    [InlineData("COMMIT")]
    [InlineData("ROLLBACK")]
    [InlineData("SAVEPOINT s1")]
    [InlineData("GRANT SELECT ON COMPANY TO PUBLIC")]
    [InlineData("EXEC sp_who")]
    [InlineData("EXECUTE somethingelse")]
    [InlineData("CALL someprocedure()")]
    [InlineData("DECLARE @x INT")]
    [InlineData("BACKUP DATABASE main TO DISK = 'x'")]
    [InlineData("SHUTDOWN")]
    public void AStatementThatIsNotAReadIsRefused(string sql) =>
        Assert.False(
            ReadOnlyStatementGuard.IsAdmissibleStatement(sql),
            $"A state-changing statement was admitted: {sql}");

    /// <summary>
    /// A second statement behind a semicolon is refused, wherever the semicolon sits.
    /// </summary>
    /// <param name="sql">The statement under test.</param>
    /// <remarks>
    /// THE BATCH IS THE VECTOR THE LEADING-KEYWORD RULE CANNOT SEE. Every entry below begins with an
    /// admissible keyword, and Microsoft.Data.Sqlite executes every statement in a command text it is
    /// handed - so without this rule the leading-keyword test would wave each one through.
    /// </remarks>
    [Theory]
    [InlineData("SELECT 1; DELETE FROM COMPANY")]
    [InlineData("SELECT 1;DROP TABLE COMPANY")]
    [InlineData("SELECT 1 ; PRAGMA journal_mode = WAL")]
    [InlineData("SELECT * FROM COMPANY; SELECT * FROM COMPANY")]
    [InlineData("SELECT 1;;")]
    [InlineData("VALUES (1); DELETE FROM COMPANY")]
    public void ASecondStatementBehindASemicolonIsRefused(string sql) =>
        Assert.False(
            ReadOnlyStatementGuard.IsAdmissibleStatement(sql),
            $"A batch was admitted: {sql}");

    /// <summary>
    /// A mutation reached through a common-table expression is refused.
    /// </summary>
    /// <param name="sql">The statement under test.</param>
    /// <remarks>
    /// <b>THE CASE THAT PROVES THE LEADING-KEYWORD RULE IS INSUFFICIENT ON ITS OWN.</b>
    /// <c>WITH x AS (...) INSERT INTO ...</c> is valid SQLite whose first word is admissible and whose
    /// effect is a write. Only the whole-statement refused-word scan catches it, which is why that scan
    /// exists rather than a cheaper prefix test.
    /// </remarks>
    [Theory]
    [InlineData("WITH x AS (SELECT 1) INSERT INTO COMPANY (NAME) SELECT 'x' FROM x")]
    [InlineData("WITH x AS (SELECT 1) UPDATE COMPANY SET NAME = 'x'")]
    [InlineData("WITH x AS (SELECT 1) DELETE FROM COMPANY")]
    [InlineData("WITH x AS (SELECT 1) REPLACE INTO COMPANY (ID) SELECT 1 FROM x")]
    public void AMutationBehindACommonTableExpressionIsRefused(string sql) =>
        Assert.False(
            ReadOnlyStatementGuard.IsAdmissibleStatement(sql),
            $"A CTE-fronted mutation was admitted: {sql}");

    /// <summary>
    /// The extension loader is refused even though it is a function reachable from a select list.
    /// </summary>
    /// <remarks>
    /// THE ONE REFUSED NAME THAT IS NOT A STATEMENT VERB, and the most consequential: it loads and
    /// executes arbitrary native code. The provider disables extension loading by default, but a guard
    /// that depends on a provider default is a guard that breaks when the default is changed somewhere
    /// else, so the name is refused outright.
    /// </remarks>
    [Theory]
    [InlineData("SELECT load_extension('evil.so')")]
    [InlineData("SELECT LOAD_EXTENSION('evil.so')")]
    [InlineData("SELECT ID FROM COMPANY WHERE load_extension('evil.so') IS NULL")]
    public void TheExtensionLoaderIsRefused(string sql) =>
        Assert.False(
            ReadOnlyStatementGuard.IsAdmissibleStatement(sql),
            $"The extension loader was admitted: {sql}");

    /// <summary>
    /// A comment, an unbalanced parenthesis, an unterminated quoted run and a stray control character
    /// are each refused.
    /// </summary>
    /// <param name="sql">The statement under test.</param>
    /// <remarks>
    /// These are the desynchronisation rules rather than the verb rules, and they matter because a
    /// comment or an unterminated delimiter is how a caller makes the REST of a composed statement into
    /// something other than what it reads as. Rule 6 admits the tab, the carriage return and the line
    /// feed - a whole statement legitimately spans lines - and refuses every other control character.
    /// </remarks>
    [Theory]
    [InlineData("SELECT 1 -- and the rest is a comment")]
    [InlineData("SELECT /* hidden */ 1")]
    [InlineData("SELECT 1 */ trailing")]
    [InlineData("SELECT (1")]
    [InlineData("SELECT 1)")]
    [InlineData("SELECT 'unterminated")]
    [InlineData("SELECT \"unterminated")]
    [InlineData("SELECT [unterminated")]
    [InlineData("SELECT 1]")]
    [InlineData("SELECT\u00001")]
    [InlineData("SELECT 1\u001b[0m")]
    public void ADesynchronisedOrCommentedStatementIsRefused(string sql) =>
        Assert.False(
            ReadOnlyStatementGuard.IsAdmissibleStatement(sql),
            $"A desynchronised statement was admitted: {sql}");

    // ---------------------------------------------------------------------------------------------
    //  3. WHAT THE GUARD DELIBERATELY LEAVES ALONE (constraint C-B)
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// An empty or whitespace-only statement is ADMISSIBLE, because the legacy refuses it at run time.
    /// </summary>
    /// <param name="sql">The statement under test.</param>
    /// <remarks>
    /// <b>THE MOST IMPORTANT NEGATIVE IN THIS SUITE.</b> The oracle's setter is a plain assignment and
    /// the emptiness check happens later, inside the task body, where an empty statement answers
    /// <c>E_INVALID_SQL</c> with <c>SQL为空!</c>
    /// [<c>n_cst_thread_task_sqlquery.sru:L615-L617</c>]. Refusing it here would move a run-time
    /// rejection forward to configuration time, which is exactly the behavioural change C-B forbids -
    /// and the published contract states the same rule, so a caller would see a refusal the contract
    /// promised it would not.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    [InlineData(null)]
    public void AnEmptyStatementIsAdmissibleBecauseTheLegacyRefusesItAtRunTime(string? sql) =>
        Assert.True(
            ReadOnlyStatementGuard.IsAdmissibleStatement(sql),
            "An empty statement was refused at configuration time, which the legacy defers to run time.");

    /// <summary>
    /// A malformed but read-shaped statement is ADMISSIBLE, because the provider is what rejects it.
    /// </summary>
    /// <param name="sql">The statement under test.</param>
    /// <remarks>
    /// The guard answers one question - may a read-scoped caller ask for this at all - and never the
    /// question of whether it will work. Answering the second would change which component reports a
    /// bad statement and with whose diagnostic.
    /// </remarks>
    [Theory]
    [InlineData("SELECT bogus")]
    [InlineData("SELECT * FROM NO_SUCH_TABLE")]
    [InlineData("SELECT FROM")]
    public void AMalformedReadStatementIsAdmissibleBecauseTheProviderRejectsIt(string sql) =>
        Assert.True(
            ReadOnlyStatementGuard.IsAdmissibleStatement(sql),
            $"A malformed statement was refused by the scope guard rather than by the provider: {sql}");

    // ---------------------------------------------------------------------------------------------
    //  4. THE SYNTAX PATH - THE SAME GRAMMAR, APPLIED TO WHAT THE SYNTAX WILL ACTUALLY EXECUTE
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A DataWindow syntax is judged by the retrieval its <c>retrieve="..."</c> clause declares.
    /// </summary>
    /// <remarks>
    /// <b>IT VALIDATES EXACTLY WHAT WILL EXECUTE.</b> The extraction reuses <c>GridSyntax.SelectOf</c> -
    /// the same method the DataWindow runtime itself calls to lift the clause out for registration - so
    /// there is one reading of the blob rather than two, and two readings disagreeing is precisely how a
    /// guard gets bypassed.
    /// </remarks>
    [Fact]
    public void ASyntaxIsJudgedByTheRetrievalItDeclares()
    {
        string admissible = GridSyntax.From("SELECT * FROM COMPANY", ["ID", "NAME"]);
        string inadmissible = GridSyntax.From("DELETE FROM COMPANY", ["ID", "NAME"]);
        string batched = GridSyntax.From("SELECT 1; DROP TABLE COMPANY", ["ID"]);

        Assert.True(ReadOnlyStatementGuard.IsAdmissibleSyntax(admissible));
        Assert.False(ReadOnlyStatementGuard.IsAdmissibleSyntax(inadmissible));
        Assert.False(ReadOnlyStatementGuard.IsAdmissibleSyntax(batched));
    }

    /// <summary>
    /// A syntax that declares no retrieval at all is ADMISSIBLE, because it cannot execute one.
    /// </summary>
    /// <param name="syntax">The syntax blob under test.</param>
    /// <remarks>
    /// NOT A HOLE, AND THE EVIDENCE IS IN THE REPOSITORY'S OWN FIXTURES: <c>release 12.5;</c> is a real
    /// syntax header several suites set, and it carries no retrieve clause. A definition with no
    /// statement cannot execute one - the runtime refuses to build a carrier from it, with its own
    /// diagnostic - so refusing here would move a run-time refusal forward to configuration time, which
    /// is the change C-B forbids. Note that the fixture also contains a semicolon, which the STATEMENT
    /// grammar would refuse: the syntax path judges the extracted clause, not the blob.
    /// </remarks>
    [Theory]
    [InlineData("release 12.5;")]
    [InlineData("table(column=(type=long name=id))")]
    [InlineData("")]
    [InlineData(null)]
    public void ASyntaxDeclaringNoRetrievalIsAdmissible(string? syntax) =>
        Assert.True(
            ReadOnlyStatementGuard.IsAdmissibleSyntax(syntax),
            "A syntax carrying no retrieval clause was refused, which the runtime already refuses later.");

}
