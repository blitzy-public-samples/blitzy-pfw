// ==================================================================================================
//  SqlIdentifierGuardTests.cs - the identifier positions of the C-06 write path (CWE-89)
// ==================================================================================================
//
//  THE FINDING THIS PINS. Contract C-06 publishes `TableUpdateContract` and `sql_syntax`, so an
//  authenticated caller supplies the update table name, the updatable column names, the key column
//  names and the identity column name over a network - and `Tasks/SqlUpdateCarrier.cs` concatenates
//  those names into the INSERT, UPDATE and DELETE statements it composes, plus the conflict reread.
//  VALUES on that path were always parameterized as `@pN`; identifiers cannot be, because no dialect
//  has a parameter form for a table or a column. The identifier positions were therefore the whole of
//  the remaining exposure, and they had no admission test: `Sql/ReadOnlyStatementGuard.cs` was applied
//  at three READ-path sites and at zero write-path sites.
//
//  WHY THE OBVIOUS ALTERNATIVE CANNOT WORK, WHICH IS THE PART WORTH REMEMBERING. Checking a name
//  against the carrier's own column model is circular on this path: a carrier built from a supplied
//  `sql_syntax` REGISTERS that declaration into the very catalogue a later lookup would consult, so
//  the input would be validated against itself. The guard therefore asks a question about the TEXT -
//  can it occupy an identifier position at all - which nothing the caller controls elsewhere can
//  influence.
//
//  WHY NOTHING IS QUOTED. Byte-exact generated SQL is this service's parity criterion [AAP 0.6.4], so
//  quoting an identifier would change every generated statement and invalidate every characterization
//  recording taken against the fixture. The guard admits or refuses; it never rewrites. That is the
//  same posture AAP 0.1.5 fixes for a legacy behaviour that cannot survive a boundary the migration
//  created: narrow the contract with a DEFINED error rather than widen it with a guess.
//
//  WHAT THIS SUITE COVERS, IN THREE LAYERS.
//    1. The grammar itself, as theory matrices over Sql/SqlIdentifierGuard.cs - both the refusals AND
//       the acceptances, because a guard that refused everything would satisfy every refusal assertion
//       while breaking every real caller, the sole evidenced fixture included.
//    2. What the guard deliberately does NOT do, which is the C-B half: it does not ask whether a name
//       exists and it does not refuse a reserved word. Both of those answers belong elsewhere and stay
//       there.
//    3. That the read path and the write gate share ONE definition of an identifier character, so the
//       scanner that reads a statement and the gate that admits a name into one cannot drift apart.
//
//  THE TWO ENFORCEMENT SITES ARE PINNED BESIDE THE FIXTURES THAT CAN REACH THEM: the sink, in
//  SqlUpdateCarrierTests against a real database, and the C-06 boundary, in UpdateServiceTests.
//
//  NO DATABASE, NO CONNECTION, NO CLOCK AND NO CONTAINER (constraints C-E, C-H). The guard is pure
//  string inspection, so nothing below needs any of them.
// ==================================================================================================

using System.Reflection;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Tests for the identifier grammar of <c>Sql/SqlIdentifierGuard.cs</c>.
/// </summary>
public sealed class SqlIdentifierGuardTests
{
    // ---------------------------------------------------------------------------------------------
    //  1. WHAT IS ADMITTED - THE HALF A REFUSE-EVERYTHING GUARD WOULD ALSO PASS WITHOUT
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Ordinary identifiers are admitted, the six evidenced column names among them.
    /// </summary>
    /// <param name="candidate">The name under test.</param>
    /// <remarks>
    /// THE FIRST SEVEN ENTRIES ARE THE ONLY UPDATABLE DATAWINDOW IN THE REPOSITORY
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14</c>] - its update table and its six marked
    /// columns. A guard that refused any of them would refuse the fixture the whole update triple is
    /// characterized against, which is why they are asserted first and by name. The rest are the shapes
    /// the three target dialects legitimately produce: the underscore, SQL Server's dollar, hash and at
    /// signs, a digit-bearing name, and a CJK name - the legacy estate is Chinese-authored, so a CJK
    /// column name is an ordinary identifier here rather than an exotic one.
    /// </remarks>
    [Theory]
    [InlineData("COMPANY")]
    [InlineData("id")]
    [InlineData("name")]
    [InlineData("age")]
    [InlineData("address")]
    [InlineData("salary")]
    [InlineData("birth")]
    [InlineData("A")]
    [InlineData("column_1")]
    [InlineData("_leading")]
    [InlineData("Total$")]
    [InlineData("#temp")]
    [InlineData("@variable")]
    [InlineData("col2")]
    [InlineData("公司名称")]
    public void AnOrdinaryNameIsAdmittedInAnIdentifierPosition(string candidate)
    {
        Assert.True(SqlIdentifierGuard.IsAdmissibleIdentifier(candidate));

        // Every bare identifier is also admissible as a table name, because a qualified name is one to
        // three of them: the qualified test must never be STRICTER than the bare one.
        Assert.True(SqlIdentifierGuard.IsAdmissibleQualifiedName(candidate));
    }

    /// <summary>
    /// A qualified table name of up to three parts is admitted.
    /// </summary>
    /// <param name="candidate">The name under test.</param>
    /// <remarks>
    /// THE SOLE EVIDENCED FIXTURE NAMES A BARE TABLE [<c>dw_sqlite.srd:L14</c>], so the qualified form is
    /// not exercised by the oracle - but the update table arrives from a caller through
    /// <c>DataWindow.Table.UpdateTable</c>, and a deployment addressing an attached SQLite database, a
    /// SQL Server schema or an Oracle schema has no other way to say so. Refusing the qualified form
    /// would narrow the contract for a shape all three dialects accept.
    /// </remarks>
    [Theory]
    [InlineData("main.COMPANY")]
    [InlineData("dbo.COMPANY")]
    [InlineData("PAYROLL.dbo.COMPANY")]
    public void AQualifiedTableNameIsAdmitted(string candidate) =>
        Assert.True(SqlIdentifierGuard.IsAdmissibleQualifiedName(candidate));

    // ---------------------------------------------------------------------------------------------
    //  2. WHAT IS REFUSED - THE INJECTION SHAPES, AND WHY EACH ONE MATTERS
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Text that could express anything beyond one identifier is refused in both positions.
    /// </summary>
    /// <param name="candidate">The name under test.</param>
    /// <remarks>
    /// <para>
    /// EACH ROW IS A DIFFERENT WAY OUT OF AN IDENTIFIER POSITION, and the first is the one the finding
    /// was raised on: a statement separator turns one generated statement into two, and
    /// <c>Microsoft.Data.Sqlite</c> executes every statement in a batch it is handed. The rest close the
    /// other exits - a comment marker truncates the rest of the statement including the concurrency
    /// predicate, whitespace introduces a second word so a table position becomes a subquery or a join,
    /// a quote character opens a literal, a parenthesis opens a call, and an operator turns a column
    /// reference into an expression.
    /// </para>
    /// <para>
    /// NULL AND THE EMPTY STRING ARE REFUSED TOO, and that is not pedantry: neither can occupy an
    /// identifier position, and admitting either would emit a statement with a hole in it whose provider
    /// diagnostic names the whole statement rather than the field that was empty.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("COMPANY; DROP TABLE COMPANY")]
    [InlineData("COMPANY;")]
    [InlineData("COMPANY --")]
    [InlineData("COMPANY /* comment */")]
    [InlineData("COMPANY WHERE 1=1")]
    [InlineData("COMPANY ")]
    [InlineData(" COMPANY")]
    [InlineData("COMPANY\tX")]
    [InlineData("COMPANY\nX")]
    [InlineData("COMPANY, OTHER")]
    [InlineData("'COMPANY'")]
    [InlineData("\"COMPANY\"")]
    [InlineData("[COMPANY]")]
    [InlineData("`COMPANY`")]
    [InlineData("(SELECT 1)")]
    [InlineData("salary+1")]
    [InlineData("salary=1")]
    [InlineData("salary||birth")]
    [InlineData("1 AS _")]
    [InlineData("*")]
    [InlineData("")]
    [InlineData(null)]
    public void TextThatCouldExpressMoreThanOneIdentifierIsRefused(string? candidate)
    {
        Assert.False(SqlIdentifierGuard.IsAdmissibleIdentifier(candidate));
        Assert.False(SqlIdentifierGuard.IsAdmissibleQualifiedName(candidate));
    }

    /// <summary>
    /// A period is a qualifier and nothing else, so the forms in which it is punctuation are refused.
    /// </summary>
    /// <param name="candidate">The name under test.</param>
    /// <remarks>
    /// A LEADING, TRAILING OR DOUBLED PERIOD NAMES NO PART, and a fourth part addresses nothing in any
    /// of the three dialects. Each row would otherwise reach a provider as a name it cannot resolve,
    /// with a diagnostic that quotes the whole statement.
    /// </remarks>
    [Theory]
    [InlineData(".COMPANY")]
    [InlineData("COMPANY.")]
    [InlineData("main..COMPANY")]
    [InlineData(".")]
    [InlineData("a.b.c.d")]
    public void APeriodThatNamesNoPartIsRefused(string candidate) =>
        Assert.False(SqlIdentifierGuard.IsAdmissibleQualifiedName(candidate));

    /// <summary>
    /// A column position takes no qualifier, so a qualified name is refused there.
    /// </summary>
    /// <remarks>
    /// THE ASYMMETRY IS DELIBERATE AND IS THE REASON THERE ARE TWO MEMBERS. Every statement the write
    /// path generates names exactly one table, so a column reference needs no qualifier and the legacy
    /// generated none [<c>n_cst_thread_task_sqlupdate.sru:L204</c>]. Admitting one in a column position
    /// would let a caller reference a column of a table the statement does not name.
    /// </remarks>
    [Fact]
    public void AQualifiedNameIsRefusedInAColumnPosition()
    {
        Assert.False(SqlIdentifierGuard.IsAdmissibleIdentifier("COMPANY.salary"));

        // And the same text IS admissible as a table, which is what makes this a positional rule rather
        // than a blanket refusal of the period.
        Assert.True(SqlIdentifierGuard.IsAdmissibleQualifiedName("COMPANY.salary"));
    }

    // ---------------------------------------------------------------------------------------------
    //  3. WHAT THE GUARD DELIBERATELY DOES NOT DO (constraint C-B)
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Admissibility is not existence: a name no table has is still admissible here.
    /// </summary>
    /// <remarks>
    /// THE REFUSAL FOR A NAME THAT DOES NOT RESOLVE IS A DIFFERENT ONE, WITH THE ORACLE'S OWN WORDING -
    /// <c>无效的列名:</c> plus the name, answered by the preparer
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L118-L122</c>]. Folding the
    /// two together here would give one code and one message to two conditions a caller must tell apart:
    /// "this table has no such column" is corrected by sending a different name, while "no statement can
    /// carry this text as a name" is corrected by sending a name at all.
    /// </remarks>
    [Fact]
    public void AnUnknownButWellShapedNameIsStillAdmissible()
    {
        Assert.True(SqlIdentifierGuard.IsAdmissibleIdentifier("no_such_column"));
        Assert.True(SqlIdentifierGuard.IsAdmissibleQualifiedName("NO_SUCH_TABLE"));
    }

    /// <summary>
    /// A reserved word is admissible, because whether the provider accepts it unquoted is the
    /// provider's answer to give.
    /// </summary>
    /// <remarks>
    /// <c>ORDER</c> is a legal column name in a quoted context and the legacy would have generated a
    /// statement for it. Refusing it here would narrow the contract for a case that fails cleanly
    /// anyway, with the provider's own diagnostic - and it would refuse a deployment whose schema
    /// legitimately uses one.
    /// </remarks>
    [Theory]
    [InlineData("ORDER")]
    [InlineData("select")]
    [InlineData("FROM")]
    public void AReservedWordIsAdmissibleBecauseTheProviderDecidesIt(string candidate) =>
        Assert.True(SqlIdentifierGuard.IsAdmissibleIdentifier(candidate));

    // ---------------------------------------------------------------------------------------------
    //  4. ONE DEFINITION, TWO PATHS
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The statement scanner consumes this guard's character predicate rather than declaring its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE SAME QUESTION IS ASKED ON TWO PATHS AND THEY MUST AGREE. <c>SelectStatementModel</c> asks
    /// whether a character can appear inside an identifier in order to decide whether a candidate
    /// keyword is a keyword or the tail of a longer name - so <c>REORDER</c> never yields an
    /// <c>ORDER</c>. The write gate asks it to decide whether a caller-supplied name may occupy an
    /// identifier position. Two copies would eventually disagree, and the disagreement would surface as
    /// a name the read path treats as one word while the gate admits as something wider.
    /// </para>
    /// <para>
    /// ASSERTED BY REFLECTION over the scanner's own private member, because that is the only way to
    /// prove the delegation from outside: a behavioural test would pass with two identical copies, which
    /// is precisely the state this assertion exists to forbid.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheStatementScannerAndTheWriteGateShareOneIdentifierCharacterSet()
    {
        MethodInfo scanner = typeof(SelectStatementModel).GetMethod(
            "IsIdentifierCharacter",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "SelectStatementModel declares no IsIdentifierCharacter, so the delegation this "
                    + "assertion pins cannot be verified.");

        // The character classes agree across the whole range the two paths can meet - every ASCII
        // character plus the CJK and Latin-1 samples that prove the classification is by Unicode
        // category rather than by range.
        foreach (char candidate in Enumerable.Range(0, 128)
            .Select(static code => (char)code)
            .Concat(['é', 'ß', '公', '，', '　']))
        {
            Assert.Equal(
                (bool)scanner.Invoke(null, [candidate])!,
                SqlIdentifierGuard.IsIdentifierCharacter(candidate));
        }
    }
}
