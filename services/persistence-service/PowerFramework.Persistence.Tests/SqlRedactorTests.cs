// ==============================================================================================
//  SqlRedactorTests - the suite that pins PowerFramework.Persistence.Errors.SqlRedactor
//  --------------------------------------------------------------------------------------------
//  SYSTEM UNDER TEST  services/persistence-service/PowerFramework.Persistence/Errors/SqlRedactor.cs
//  BEHAVIOURAL ORACLE ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru
//                         _of_paramtostring :L262-L339, the literal grammar being masked
//                         DisableBind parsing :L128-L129, the second interpolation route
//                     ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru
//                         _of_replacencharliteral :L89-L147, the legacy's OWN forward scanner,
//                         whose escape handling and empty-input convention this suite pins
//                     ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru
//                         the six paging sentinels and the count wrapper :L333-L394, :L830-L834
//                     ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L190
//                         the synthesized Chinese diagnostic that must pass through untouched
//                     ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L463-L469
//                         the sole DDL, whose AGE and SALARY columns are why numerics are masked
//                     all READ ONLY per constraint C-C - read as specification, never edited
//
//  WHAT THIS SUITE IS ACTUALLY PROTECTING
//  --------------------------------------------------------------------------------------------
//  SqlRedactor is the only control standing between a fully interpolated SQL statement and a log
//  record or a network peer. Two of its properties would fail SILENTLY if broken, and they are the
//  centre of this suite rather than incidental coverage:
//
//    1. THE ESCAPE HANDLING. If '' stops being consumed as an escaped quote, the scan
//       DESYNCHRONISES: regions that are inside a literal are treated as outside and vice versa.
//       The output still looks redacted, so nothing draws attention to it - only a reader of a
//       leaked log line would ever find out. EscapedQuote_* below is the guard.
//
//    2. THE IDENTIFIER GUARDS ON NUMERIC MASKING. If either guard is dropped, identifiers that
//       contain digits are corrupted - which includes the paging sentinels a reader uses to tell
//       which strategy generated a statement. Again nothing fails; the diagnostic just quietly
//       stops being readable. Sentinels_* and DigitBearingIdentifiers_* are the guard.
//
//  A third property is asserted structurally rather than behaviourally: there must be NO way to
//  produce a wire DbError carrying an unmasked statement. That is constraint C-F expressed as a
//  compile-time property, and it is stronger than it first appears, because a MANDATORY REDACTOR
//  ARGUMENT WAS NOT ENOUGH. Any implementation of the abstraction satisfied such a parameter,
//  including one that masks nothing, so the boundary was one argument away from being bypassed at
//  every call site. The policy therefore lives INSIDE the projection: ToDbError takes only the
//  payload and applies the sealed SqlRedactor.Instance itself. Three tests hold that line -
//  SqlRedactor_ExposesNoDisableSwitchOfAnyKind (no mode to switch),
//  ToDbError_HasExactlyOneOverload_TakesOnlyThePayload_AndAcceptsNoInjectablePolicy (no seam to
//  inject at), and ToDbError_MasksEvenThoughAPassThroughImplementationExists (the outcome, proven
//  with a pass-through redactor alive in the same assembly).
//
//  C-F: EVERY INPUT IN THIS FILE IS SYNTHETIC. No statement here was captured from a log, and no
//  value from any known hardcoded-secret site appears in any form. The names, ages and salaries are
//  invented for the purpose, and the schema they reference is the repository's own sole DDL.
//
//  No performance property is asserted anywhere in this suite and nothing here is timed.
// ==============================================================================================

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using PowerFramework.Contracts.Common.V1;
using PowerFramework.Persistence.Errors;
using Xunit;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Characterization and contract tests for <see cref="SqlRedactor"/>,
/// <see cref="ISqlRedactor"/> and <see cref="DbErrorDataExtensions.ToDbError"/>.
/// </summary>
public sealed class SqlRedactorTests
{
    /// <summary>The placeholder every expectation below is written against.</summary>
    private const string Mask = SqlRedactor.DefaultPlaceholder;

    /// <summary>A redactor in its default, safe posture.</summary>
    private static SqlRedactor Subject => new();

    // ==========================================================================================
    //  THE MASKING TABLE
    //  ----------------------------------------------------------------------------------------
    //  One table drives both the masking assertions and the idempotence assertions, so the two can
    //  never drift apart: a case added here is automatically also an idempotence case.
    // ==========================================================================================

    /// <summary>
    /// Input statement paired with the exact expected masked output, with a label naming the
    /// property each row exists to pin.
    /// </summary>
    private static readonly (string Label, string Input, string Expected)[] Cases =
    [
        // ---- the string literal grammar [n_cst_thread_task_sqlbase.sru:L318] ----
        (
            "a plain string literal is masked and the surrounding syntax is untouched",
            "SELECT NAME FROM COMPANY WHERE NAME = 'Alice'",
            $"SELECT NAME FROM COMPANY WHERE NAME = '{Mask}'"
        ),
        (
            "an empty string literal is still masked, so its presence remains visible",
            "SELECT NAME FROM COMPANY WHERE ADDRESS = ''",
            $"SELECT NAME FROM COMPANY WHERE ADDRESS = '{Mask}'"
        ),
        (
            "a doubled quote is an escape, so the literal containing it masks as ONE literal "
                + "[oracle: sqlbase.sru:L272/:L318 doubles it, sqlbase_ds.sru:L117-L121 consumes it]",
            "SELECT * FROM COMPANY WHERE NAME = 'O''Brien' AND ADDRESS IS NOT NULL",
            $"SELECT * FROM COMPANY WHERE NAME = '{Mask}' AND ADDRESS IS NOT NULL"
        ),
        (
            "a literal that is nothing but an escaped quote masks as one literal",
            "SELECT * FROM COMPANY WHERE NAME = ''''",
            $"SELECT * FROM COMPANY WHERE NAME = '{Mask}'"
        ),
        (
            "the national-character prefix survives, which the NCharBind rewrite requires "
                + "[sqlbase_ds.sru:L125-L130]",
            "UPDATE COMPANY SET NAME = N'Alice' WHERE ID IS NOT NULL",
            $"UPDATE COMPANY SET NAME = N'{Mask}' WHERE ID IS NOT NULL"
        ),
        (
            "an unterminated literal fails closed: masked to the end, with no invented closing quote",
            "SELECT * FROM COMPANY WHERE NAME = 'Ali",
            $"SELECT * FROM COMPANY WHERE NAME = '{Mask}"
        ),

        // ---- the date and time literal grammar, both dialect arms ----
        (
            "the plain date form is masked [sqlbase.sru:L322-L325 non-Oracle arm]",
            "SELECT * FROM COMPANY WHERE BIRTH = '1970-01-31'",
            $"SELECT * FROM COMPANY WHERE BIRTH = '{Mask}'"
        ),
        (
            "the Oracle to_date form masks both arguments and leaves to_date and the parentheses "
                + "intact [sqlbase.sru:L322-L325 Oracle arm]",
            "SELECT * FROM COMPANY WHERE BIRTH = to_date('1970-01-31','yyyy-mm-dd')",
            $"SELECT * FROM COMPANY WHERE BIRTH = to_date('{Mask}','{Mask}')"
        ),
        (
            "the Oracle datetime form masks both arguments [sqlbase.sru:L328-L331 Oracle arm]",
            "SELECT * FROM COMPANY WHERE BIRTH = "
                + "to_date('1970-01-31 06:05:04','yyyy-mm-dd hh24:mi:ss')",
            $"SELECT * FROM COMPANY WHERE BIRTH = to_date('{Mask}','{Mask}')"
        ),
        (
            "the time form is masked [sqlbase.sru:L320]",
            "SELECT * FROM COMPANY WHERE BIRTH = '06:05:04'",
            $"SELECT * FROM COMPANY WHERE BIRTH = '{Mask}'"
        ),

        // ---- the array form, which becomes an IN list [sqlbase.sru:L271-L312] ----
        (
            "every element of an IN list is masked independently",
            "SELECT * FROM COMPANY WHERE NAME IN ('a','b','c')",
            $"SELECT * FROM COMPANY WHERE NAME IN ('{Mask}','{Mask}','{Mask}')"
        ),
        (
            "a numeric IN list is masked element by element, the commas surviving",
            "SELECT * FROM COMPANY WHERE AGE IN (18,21,65)",
            $"SELECT * FROM COMPANY WHERE AGE IN ({Mask},{Mask},{Mask})"
        ),

        // ---- the numeric grammar, emitted BARE by the legacy [sqlbase.sru:L333-L334] ----
        (
            "a bare integer is masked, because AGE and SALARY are numeric in the sole DDL "
                + "[w_test_sqlite.srw:L463-L469]",
            "SELECT * FROM COMPANY WHERE SALARY > 12345",
            $"SELECT * FROM COMPANY WHERE SALARY > {Mask}"
        ),
        (
            "a decimal is masked as one run, the point included",
            "SELECT * FROM COMPANY WHERE SALARY = 12345.67",
            $"SELECT * FROM COMPANY WHERE SALARY = {Mask}"
        ),
        (
            "an exponent form is masked as one run rather than as a number followed by a name",
            "SELECT * FROM COMPANY WHERE SALARY < 1.5e10",
            $"SELECT * FROM COMPANY WHERE SALARY < {Mask}"
        ),
        (
            "a leading decimal point is part of the literal",
            "SELECT * FROM COMPANY WHERE SALARY > .5",
            $"SELECT * FROM COMPANY WHERE SALARY > {Mask}"
        ),
        (
            "a signed exponent is part of the same run, so the mask covers all of it",
            "SELECT * FROM COMPANY WHERE SALARY < 1.5e-10",
            $"SELECT * FROM COMPANY WHERE SALARY < {Mask}"
        ),
        (
            "a positive signed exponent is treated identically",
            "SELECT * FROM COMPANY WHERE SALARY < 1.5E+10",
            $"SELECT * FROM COMPANY WHERE SALARY < {Mask}"
        ),
        (
            "a digit run that is the HEAD of an identifier is left alone, which is the second "
                + "identifier guard - the run is measured, then rejected because a letter follows it",
            "SELECT * FROM COMPANY WHERE AGE = 30d",
            "SELECT * FROM COMPANY WHERE AGE = 30d"
        ),
        (
            "a trailing exponent marker with no digits is an identifier head, not a literal tail",
            "SELECT * FROM COMPANY WHERE SALARY = 1e",
            "SELECT * FROM COMPANY WHERE SALARY = 1e"
        ),
        (
            "a truncated exponent - marker and sign but no digits, and nothing after it - neither "
                + "masks nor faults, which is the defensive end-of-input arm of the exponent match",
            "SELECT * FROM COMPANY WHERE SALARY = 1e-",
            "SELECT * FROM COMPANY WHERE SALARY = 1e-"
        ),
        (
            "an exponent marker followed by something that is present but is not a digit leaves the "
                + "marker alone and the identifier guard then rejects the run",
            "SELECT * FROM COMPANY WHERE SALARY = 1e ORDER BY ID",
            "SELECT * FROM COMPANY WHERE SALARY = 1e ORDER BY ID"
        ),
        (
            "a statement that is nothing but a signed number still masks: the sign is the first "
                + "non-white-space character, which counts as a unary position",
            "-5000",
            Mask
        ),
        (
            "leading white space before that sign does not change the judgement",
            "   -5000",
            $"   {Mask}"
        ),
        (
            "a unary minus is absorbed, so the redacted copy does not disclose the sign",
            "SELECT * FROM COMPANY WHERE SALARY = -5000",
            $"SELECT * FROM COMPANY WHERE SALARY = {Mask}"
        ),
        (
            "a unary plus is absorbed on the same terms",
            "SELECT * FROM COMPANY WHERE SALARY = +5000",
            $"SELECT * FROM COMPANY WHERE SALARY = {Mask}"
        ),
        (
            "a BINARY minus is NOT absorbed, so the operator stays legible",
            "SELECT * FROM COMPANY WHERE SALARY-1000 > 0",
            $"SELECT * FROM COMPANY WHERE SALARY-{Mask} > {Mask}"
        ),

        // ---- the null form [sqlbase.sru:L315] ----
        (
            "the bare NULL keyword survives: it is unquoted letters and carries no data",
            "UPDATE COMPANY SET ADDRESS = NULL WHERE SALARY IS NOT NULL",
            "UPDATE COMPANY SET ADDRESS = NULL WHERE SALARY IS NOT NULL"
        ),
        (
            "NULL survives even beside a masked literal",
            "UPDATE COMPANY SET ADDRESS = NULL WHERE ID = 7",
            $"UPDATE COMPANY SET ADDRESS = NULL WHERE ID = {Mask}"
        ),

        // ---- identifiers, including every paging sentinel ----
        (
            "identifiers containing digits are not damaged by the numeric rule",
            "SELECT sqlite3_version FROM T1 WHERE T1.COL2 = 7",
            $"SELECT sqlite3_version FROM T1 WHERE T1.COL2 = {Mask}"
        ),
        (
            "a double-quoted identifier is an IDENTIFIER, not a literal, and is left alone "
                + "[the oracle's own scanner toggles on the single quote alone, sqlbase_ds.sru:L114]",
            "SELECT \"first name\" FROM COMPANY WHERE AGE = 30",
            $"SELECT \"first name\" FROM COMPANY WHERE AGE = {Mask}"
        ),
        (
            "the SQL Server row-number strategy keeps all of its sentinels "
                + "[n_cst_thread_task_sqlquery.sru:L355-L356]",
            "SELECT TOP 20 * FROM (SELECT TOP 40 ID,ROW_NUMBER() OVER (ORDER BY age A) AS "
                + "pfwPagedSQL_RN FROM COMPANY) pfwPagedSQL_Tbl WHERE pfwPagedSQL_RN BETWEEN 21 AND 40",
            $"SELECT TOP {Mask} * FROM (SELECT TOP {Mask} ID,ROW_NUMBER() OVER (ORDER BY age A) AS "
                + $"pfwPagedSQL_RN FROM COMPANY) pfwPagedSQL_Tbl WHERE pfwPagedSQL_RN BETWEEN {Mask} "
                + $"AND {Mask}"
        ),
        (
            "the unique-index strategy keeps its outer-table sentinel "
                + "[n_cst_thread_task_sqlquery.sru:L333, :L350]",
            "SELECT * FROM COMPANY INNER JOIN (SELECT ID FROM COMPANY OFFSET 20 ROWS FETCH NEXT 20 "
                + "ROWS ONLY) pfwPagedSQL_OutterTbl ON pfwPagedSQL_OutterTbl.ID = COMPANY.ID",
            $"SELECT * FROM COMPANY INNER JOIN (SELECT ID FROM COMPANY OFFSET {Mask} ROWS FETCH NEXT "
                + $"{Mask} ROWS ONLY) pfwPagedSQL_OutterTbl ON pfwPagedSQL_OutterTbl.ID = COMPANY.ID"
        ),
        (
            "the Oracle triple-nested strategy keeps all three of its sentinels "
                + "[n_cst_thread_task_sqlquery.sru:L394]",
            "SELECT * FROM (SELECT * FROM (SELECT pfwPagedSQL_TblInnerInner.*,ROW_NUMBER() OVER "
                + "(ORDER BY '') AS pfwPagedSQL_RN FROM (SELECT * FROM COMPANY) "
                + "pfwPagedSQL_TblInnerInner) pfwPagedSQL_TblInner WHERE pfwPagedSQL_RN <= 40) "
                + "pfwPagedSQL_TblOuter WHERE pfwPagedSQL_RN BETWEEN 21 AND 40",
            $"SELECT * FROM (SELECT * FROM (SELECT pfwPagedSQL_TblInnerInner.*,ROW_NUMBER() OVER "
                + $"(ORDER BY '{Mask}') AS pfwPagedSQL_RN FROM (SELECT * FROM COMPANY) "
                + $"pfwPagedSQL_TblInnerInner) pfwPagedSQL_TblInner WHERE pfwPagedSQL_RN <= {Mask}) "
                + $"pfwPagedSQL_TblOuter WHERE pfwPagedSQL_RN BETWEEN {Mask} AND {Mask}"
        ),

        // ---- the count wrapper, and the deliberate structural-number consequence ----
        (
            "structural numbers are masked too - COUNT(1) and the \"1 AS _\" column stub - which is "
                + "a documented deliberate consequence, not a defect "
                + "[n_cst_thread_task_sqlquery.sru:L830, :L834]",
            "SELECT COUNT(1) AS CNT FROM (SELECT 1 AS _ FROM COMPANY) pfwPagedSQL_Tbl",
            $"SELECT COUNT({Mask}) AS CNT FROM (SELECT {Mask} AS _ FROM COMPANY) pfwPagedSQL_Tbl"
        ),

        // ---- radix literals: the blob form that used to escape entirely ----
        (
            "a SQL Server / SQLite hexadecimal blob is masked, the 0x marker surviving - THE LEAK "
                + "review found: the numeric measure consumed the 0, saw x as an identifier "
                + "character, declined the run, and every byte of the blob was copied through",
            "UPDATE COMPANY SET ADDRESS = 0xDEADBEEF WHERE ID = 1",
            $"UPDATE COMPANY SET ADDRESS = 0x{Mask} WHERE ID = {Mask}"
        ),
        (
            "the upper-case radix introducer is masked identically and its own case is preserved",
            "UPDATE COMPANY SET ADDRESS = 0XdeadBEEF WHERE ID = 1",
            $"UPDATE COMPANY SET ADDRESS = 0X{Mask} WHERE ID = {Mask}"
        ),
        (
            "a MySQL-style binary literal is masked",
            "SELECT * FROM COMPANY WHERE AGE = 0b0101",
            $"SELECT * FROM COMPANY WHERE AGE = 0b{Mask}"
        ),
        (
            "a binary run that continues into a non-binary digit is a NAME, not a literal, so it is "
                + "left alone - the same guard the numeric rule applies",
            "SELECT * FROM COMPANY WHERE AGE = 0b1z",
            "SELECT * FROM COMPANY WHERE AGE = 0b1z"
        ),
        (
            "a 0x with no hexadecimal digit after it is not a literal and is copied through",
            "SELECT * FROM COMPANY WHERE AGE = 0x",
            "SELECT * FROM COMPANY WHERE AGE = 0x"
        ),
        (
            "an identifier that merely CONTAINS 0x is untouched, because the preceding character is an "
                + "identifier character",
            "SELECT col0xFF FROM COMPANY",
            "SELECT col0xFF FROM COMPANY"
        ),
        (
            "the SQLite quoted blob form needs no branch: the X prefix copies through as an "
                + "identifier character and the quoted body is masked",
            "UPDATE COMPANY SET ADDRESS = X'414243' WHERE ID IS NOT NULL",
            $"UPDATE COMPANY SET ADDRESS = X'{Mask}' WHERE ID IS NOT NULL"
        ),
        (
            "the lower-case quoted blob form behaves identically",
            "UPDATE COMPANY SET ADDRESS = x'414243' WHERE ID IS NOT NULL",
            $"UPDATE COMPANY SET ADDRESS = x'{Mask}' WHERE ID IS NOT NULL"
        ),
        (
            "a bit-string literal behaves identically, for the same reason",
            "UPDATE COMPANY SET AGE = B'0101' WHERE ID IS NOT NULL",
            $"UPDATE COMPANY SET AGE = B'{Mask}' WHERE ID IS NOT NULL"
        ),

        // ---- comment bodies: a caller's clause can carry one, and callers park values in them ----
        (
            "a line comment body is masked while the marker and the line break survive - THE LEAK: "
                + "the numeric rule inspected the first hyphen as a sign, declined, and the body was "
                + "copied out one character at a time",
            "SELECT * FROM COMPANY -- bind NAME = Alice\nWHERE ID IS NOT NULL",
            $"SELECT * FROM COMPANY --{Mask}\nWHERE ID IS NOT NULL"
        ),
        (
            "a CR LF pair is kept intact rather than split",
            "SELECT * FROM COMPANY -- secret\r\nWHERE ID IS NOT NULL",
            $"SELECT * FROM COMPANY --{Mask}\r\nWHERE ID IS NOT NULL"
        ),
        (
            "a line comment with no trailing break consumes the remainder, which is both fail-closed "
                + "and simply correct",
            "SELECT * FROM COMPANY -- password hunter2",
            $"SELECT * FROM COMPANY --{Mask}"
        ),
        (
            "a block comment body is masked and both markers survive",
            "SELECT /* NAME = Alice */ * FROM COMPANY",
            $"SELECT /*{Mask}*/ * FROM COMPANY"
        ),
        (
            "a nested block comment is counted, so the OUTER comment's tail is inside the mask - "
                + "T-SQL nests, and closing at the first terminator would leak that tail",
            "SELECT /* outer /* inner */ still secret */ * FROM COMPANY",
            $"SELECT /*{Mask}*/ * FROM COMPANY"
        ),
        (
            "an unterminated block comment consumes the remainder with no invented terminator",
            "SELECT * FROM COMPANY /* NAME = Alice",
            $"SELECT * FROM COMPANY /*{Mask}"
        ),
        (
            "a literal containing comment markers is still just a literal - the string branch owns it",
            "SELECT * FROM COMPANY WHERE NAME = '-- not a comment /*'",
            $"SELECT * FROM COMPANY WHERE NAME = '{Mask}'"
        ),
        (
            "a subtraction operator is not a comment opener: one hyphen is copied through and the "
                + "operand is masked",
            "SELECT * FROM COMPANY WHERE SALARY-1 > 0",
            $"SELECT * FROM COMPANY WHERE SALARY-{Mask} > {Mask}"
        ),

        // ---- Oracle alternative quoting: the form whose body may hold a BARE apostrophe ----
        (
            "an alternative-quoted literal is masked as ONE literal even though its body contains a "
                + "bare apostrophe - the plain-quote scanner closed at that apostrophe and handed the "
                + "rest of the value back as SQL",
            "SELECT * FROM COMPANY WHERE NAME = q'[O'Brien lives at 12 Mill Lane]'",
            $"SELECT * FROM COMPANY WHERE NAME = q'{Mask}'"
        ),
        (
            "each of the four bracket delimiters is mirrored correctly - a WRONG mirror would find no "
                + "terminator, and the fail-closed arm would then swallow everything after it, so four "
                + "separate masks with the commas intact is exactly what proves the mirroring",
            "SELECT q'(a'b)', q'{c'd}', q'<e'f>', q'[g'h]' FROM COMPANY",
            $"SELECT q'{Mask}', q'{Mask}', q'{Mask}', q'{Mask}' FROM COMPANY"
        ),
        (
            "a non-bracket delimiter closes with itself",
            "SELECT * FROM COMPANY WHERE NAME = q'!O'Brien!'",
            $"SELECT * FROM COMPANY WHERE NAME = q'{Mask}'"
        ),
        (
            "the upper-case introducer behaves identically and its case is preserved",
            "SELECT * FROM COMPANY WHERE NAME = Q'[O'Brien]'",
            $"SELECT * FROM COMPANY WHERE NAME = Q'{Mask}'"
        ),
        (
            "an unterminated alternative-quoted literal fails closed to the end of the input",
            "SELECT * FROM COMPANY WHERE NAME = q'[O'Brien",
            $"SELECT * FROM COMPANY WHERE NAME = q'{Mask}"
        ),
        (
            "the masked form is recognised on a second pass rather than being re-tokenised, which is "
                + "what makes this branch idempotent for ANY placeholder",
            $"SELECT * FROM COMPANY WHERE NAME = q'{Mask}'",
            $"SELECT * FROM COMPANY WHERE NAME = q'{Mask}'"
        ),
        (
            "an identifier ENDING in q is not an introducer: the guard on the preceding character is "
                + "what stops the terminator search running off into the statement",
            "SELECT * FROM COMPANY WHERE seq'Alice' IS NOT NULL",
            $"SELECT * FROM COMPANY WHERE seq'{Mask}' IS NOT NULL"
        ),
        (
            "a white-space delimiter is not legal in Oracle, so this is an ordinary quoted literal "
                + "preceded by a letter",
            "SELECT * FROM COMPANY WHERE NAME = q' Alice '",
            $"SELECT * FROM COMPANY WHERE NAME = q'{Mask}'"
        ),

        // ---- already-masked text, the idempotence surface ----
        (
            "text that has already crossed this seam is returned unchanged",
            $"SELECT * FROM COMPANY WHERE NAME = '{Mask}' AND SALARY > {Mask}",
            $"SELECT * FROM COMPANY WHERE NAME = '{Mask}' AND SALARY > {Mask}"
        ),

        // ---- statements with nothing to mask ----
        (
            "a statement with no literal at all is returned unchanged",
            "SELECT ID,NAME,AGE,ADDRESS,SALARY,BIRTH FROM COMPANY",
            "SELECT ID,NAME,AGE,ADDRESS,SALARY,BIRTH FROM COMPANY"
        ),
        (
            "a dangling sign with no number after it is harmless and is copied through",
            "SELECT * FROM COMPANY WHERE SALARY = -",
            "SELECT * FROM COMPANY WHERE SALARY = -"
        ),
        (
            "a dangling decimal point with no digit after it is copied through",
            "SELECT * FROM COMPANY WHERE SALARY = .",
            "SELECT * FROM COMPANY WHERE SALARY = ."
        ),
    ];

    /// <summary>The masking table, as input and expected-output pairs.</summary>
    /// <returns>One row per case in <see cref="Cases"/>.</returns>
    public static TheoryData<string, string, string> MaskingCases()
    {
        TheoryData<string, string, string> data = [];

        foreach ((string label, string input, string expected) in Cases)
        {
            data.Add(label, input, expected);
        }

        return data;
    }

    /// <summary>Every statement in the masking table, for the idempotence theory.</summary>
    /// <returns>One row per case in <see cref="Cases"/>.</returns>
    public static TheoryData<string, string> AllStatements()
    {
        TheoryData<string, string> data = [];

        foreach ((string label, string input, _) in Cases)
        {
            data.Add(label, input);
        }

        return data;
    }

    // ==========================================================================================
    //  1. THE MASKING TABLE, ASSERTED EXACTLY
    // ==========================================================================================

    /// <summary>
    /// Every case in <see cref="Cases"/> produces exactly the expected text - no more masked and no
    /// less.
    /// </summary>
    /// <param name="label">The property this row pins, echoed into the failure message.</param>
    /// <param name="input">The statement to mask.</param>
    /// <param name="expected">The exact expected output.</param>
    [Theory]
    [MemberData(nameof(MaskingCases))]
    public void Redact_MasksLiteralValuesAndNothingElse(string label, string input, string expected)
    {
        string actual = Subject.Redact(input);

        Assert.Equal(expected, actual);
        Assert.False(string.IsNullOrEmpty(label));
    }

    /// <summary>
    /// Redaction is idempotent for every case in the table: a value that crosses this seam twice -
    /// once into a log record and once into a response - is not masked twice.
    /// </summary>
    /// <param name="label">The property this row pins.</param>
    /// <param name="input">The statement to mask twice.</param>
    [Theory]
    [MemberData(nameof(AllStatements))]
    public void Redact_IsIdempotent(string label, string input)
    {
        SqlRedactor redactor = Subject;

        string once = redactor.Redact(input);
        string twice = redactor.Redact(once);

        Assert.Equal(once, twice);
        Assert.False(string.IsNullOrEmpty(label));
    }

    // ==========================================================================================
    //  2. THE ESCAPE HANDLING - THE FIRST SILENT-FAILURE GUARD
    // ==========================================================================================

    /// <summary>
    /// A doubled quote does not terminate the literal, so exactly ONE literal is produced. A scanner
    /// that mishandled it would emit two, which is the visible symptom of desynchronisation.
    /// </summary>
    [Fact]
    public void EscapedQuote_ProducesExactlyOneLiteral()
    {
        string masked = Subject.Redact("SELECT * FROM COMPANY WHERE NAME = 'O''Brien'");

        Assert.Equal($"SELECT * FROM COMPANY WHERE NAME = '{Mask}'", masked);
        Assert.Equal(1, CountOccurrences(masked, Mask));
        Assert.Equal(2, CountOccurrences(masked, "'"));
    }

    /// <summary>
    /// The scan does not desynchronise across an escaped quote: the whole tail after the literal is
    /// byte-identical, which it could not be if the in-literal state had been left inverted.
    /// </summary>
    [Fact]
    public void EscapedQuote_LeavesTheTailOfTheStatementUntouched()
    {
        const string tail = " AND ADDRESS IS NOT NULL ORDER BY NAME";

        string masked = Subject.Redact("SELECT * FROM COMPANY WHERE NAME = 'O''Brien'" + tail);

        Assert.EndsWith(tail, masked, StringComparison.Ordinal);
    }

    /// <summary>
    /// Several escaped quotes in one literal are all consumed, and a following literal is masked
    /// independently rather than being swallowed by an inverted state.
    /// </summary>
    [Fact]
    public void EscapedQuote_RepeatedDoesNotBleedIntoTheNextLiteral()
    {
        string masked = Subject.Redact(
            "SELECT * FROM COMPANY WHERE NAME = 'a''b''c' AND ADDRESS = 'x'");

        Assert.Equal(
            $"SELECT * FROM COMPANY WHERE NAME = '{Mask}' AND ADDRESS = '{Mask}'",
            masked);
        Assert.Equal(2, CountOccurrences(masked, Mask));
    }

    // ==========================================================================================
    //  3. THE IDENTIFIER GUARDS - THE SECOND SILENT-FAILURE GUARD
    // ==========================================================================================

    /// <summary>
    /// All six paging sentinels survive redaction. The sixth, <c>pfwPagedSQL_Tbl</c>, is the count
    /// wrapper alias at [n_cst_thread_task_sqlquery.sru:L834] and is easy to omit.
    /// </summary>
    /// <param name="sentinel">The sentinel identifier that must survive.</param>
    [Theory]
    [InlineData("pfwPagedSQL_OutterTbl")]
    [InlineData("pfwPagedSQL_RN")]
    [InlineData("pfwPagedSQL_Tbl")]
    [InlineData("pfwPagedSQL_TblInner")]
    [InlineData("pfwPagedSQL_TblInnerInner")]
    [InlineData("pfwPagedSQL_TblOuter")]
    public void Sentinels_SurviveRedaction(string sentinel)
    {
        string masked = Subject.Redact(
            $"SELECT {sentinel}.ID FROM COMPANY {sentinel} WHERE {sentinel}.AGE > 30");

        Assert.Equal(
            $"SELECT {sentinel}.ID FROM COMPANY {sentinel} WHERE {sentinel}.AGE > {Mask}",
            masked);
        Assert.Equal(3, CountOccurrences(masked, sentinel));
    }

    /// <summary>
    /// Identifiers that contain digits are not damaged, whether the digit is in the middle, at the
    /// end, or separated by an underscore.
    /// </summary>
    /// <param name="identifier">The identifier that must survive verbatim.</param>
    [Theory]
    [InlineData("sqlite3")]
    [InlineData("sqlite3_version")]
    [InlineData("T1")]
    [InlineData("COL2")]
    [InlineData("COMPANY_2")]
    [InlineData("X1Y2Z3")]
    [InlineData("_9")]
    public void DigitBearingIdentifiers_AreNotDamaged(string identifier)
    {
        string masked = Subject.Redact($"SELECT {identifier} FROM COMPANY WHERE {identifier} > 1");

        Assert.Equal($"SELECT {identifier} FROM COMPANY WHERE {identifier} > {Mask}", masked);
    }

    /// <summary>
    /// A qualified name whose parts contain digits keeps its dot and both parts.
    /// </summary>
    [Fact]
    public void QualifiedIdentifierWithDigits_KeepsBothPartsAndTheDot()
    {
        string masked = Subject.Redact("SELECT T1.COL2 FROM COMPANY T1 WHERE T1.COL2 = 42");

        Assert.Equal($"SELECT T1.COL2 FROM COMPANY T1 WHERE T1.COL2 = {Mask}", masked);
    }

    // ==========================================================================================
    //  4. THE EMPTY, NULL AND DISABLED CONTRACTS
    // ==========================================================================================

    /// <summary>
    /// A null statement yields the empty string, matching the empty-string convention of
    /// <see cref="DbErrorData.SqlSyntax"/> and the legacy scanner's own
    /// <c>if nLen &lt;= 0 then return ""</c> [n_cst_thread_task_sqlbase_ds.sru:L111].
    /// </summary>
    [Fact]
    public void Redact_NullStatement_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, Subject.Redact(null));
    }

    /// <summary>An empty statement yields the empty string.</summary>
    [Fact]
    public void Redact_EmptyStatement_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, Subject.Redact(string.Empty));
    }

    /// <summary>
    /// THERE IS NO DISABLED MODE TO CONSTRUCT. The type exposes exactly one constructor, its only
    /// parameter is the placeholder token, and it carries no boolean of any kind - so no
    /// configuration value and no call site can put this type into a pass-through state.
    /// </summary>
    /// <remarks>
    /// Asserted structurally rather than behaviourally because that is the only way to state
    /// "the switch does not exist". A behavioural test can only exercise the modes that DO exist.
    /// </remarks>
    [Fact]
    public void SqlRedactor_ExposesNoDisableSwitchOfAnyKind()
    {
        ConstructorInfo[] constructors = typeof(SqlRedactor).GetConstructors();

        ConstructorInfo only = Assert.Single(constructors);
        ParameterInfo parameter = Assert.Single(only.GetParameters());

        Assert.Equal(typeof(string), parameter.ParameterType);
        Assert.Equal("placeholder", parameter.Name);

        // No boolean anywhere on the public or non-public surface: no `enabled` field, no `Enabled`
        // property, no SetEnabled method. A single one of those returning false would reopen the leak.
        const BindingFlags all = BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        Assert.DoesNotContain(typeof(SqlRedactor).GetFields(all), field => field.FieldType == typeof(bool));
        Assert.DoesNotContain(typeof(SqlRedactor).GetProperties(all), property => property.PropertyType == typeof(bool));
        Assert.DoesNotContain(
            typeof(SqlRedactor).GetMethods(all),
            method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(bool)));
    }

    /// <summary>
    /// Masking is unconditional: every construction of the type masks, including the shared
    /// <see cref="SqlRedactor.Instance"/> the outward projection uses.
    /// </summary>
    [Fact]
    public void Redact_MasksUnconditionally_ForEveryConstructionIncludingTheSharedInstance()
    {
        const string statement = "SELECT * FROM COMPANY WHERE NAME = 'Alice' AND SALARY > 12345";
        const string expected = "SELECT * FROM COMPANY WHERE NAME = '" + Mask + "' AND SALARY > " + Mask;

        Assert.Equal(expected, Subject.Redact(statement));
        Assert.Equal(expected, SqlRedactor.Instance.Redact(statement));
        Assert.Equal(expected, new SqlRedactor().Redact(statement));
    }

    /// <summary>
    /// The shared instance is a real, singleton-safe <see cref="SqlRedactor"/> carrying the default
    /// placeholder - it is what the wire projection reaches for, so its identity and posture matter.
    /// </summary>
    [Fact]
    public void Instance_IsASharedRedactorWithTheDefaultPlaceholder()
    {
        Assert.Same(SqlRedactor.Instance, SqlRedactor.Instance);
        Assert.Equal(SqlRedactor.DefaultPlaceholder, SqlRedactor.Instance.Placeholder);
    }

    // ==========================================================================================
    //  5. THE PLACEHOLDER CONTRACT
    // ==========================================================================================

    /// <summary>
    /// The default placeholder satisfies both properties idempotence depends on: no single quote, so
    /// it can sit inside a masked literal, and no digit, so a second pass cannot re-mask it.
    /// </summary>
    [Fact]
    public void DefaultPlaceholder_ContainsNoQuoteAndNoDigit()
    {
        Assert.Equal(SqlRedactor.DefaultPlaceholder, Subject.Placeholder);
        Assert.DoesNotContain('\'', SqlRedactor.DefaultPlaceholder);
        Assert.DoesNotContain(SqlRedactor.DefaultPlaceholder, char.IsAsciiDigit);
    }

    /// <summary>A supplied placeholder is honoured in both the quoted and the numeric position.</summary>
    [Fact]
    public void CustomPlaceholder_IsUsedForBothLiteralKinds()
    {
        SqlRedactor redactor = new(placeholder: "?");

        string masked = redactor.Redact("SELECT * FROM COMPANY WHERE NAME = 'Alice' AND AGE = 30");

        Assert.Equal("?", redactor.Placeholder);
        Assert.Equal("SELECT * FROM COMPANY WHERE NAME = '?' AND AGE = ?", masked);
    }

    /// <summary>
    /// A placeholder that would break quoting or idempotence is rejected at construction rather than
    /// silently corrected - the fault would otherwise surface only as a leaked log line.
    /// </summary>
    /// <param name="placeholder">The invalid placeholder.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("'")]
    [InlineData("<red'acted>")]
    [InlineData("<redacted1>")]
    [InlineData("0")]
    // A LINE BREAK WOULD CLOSE A MASKED LINE COMMENT, so the text after it would escape masking on the
    // next pass. Every control character is refused with it rather than reasoned about one at a time.
    [InlineData("<red\nacted>")]
    [InlineData("<red\racted>")]
    [InlineData("<red\tacted>")]
    // A BLOCK-COMMENT TERMINATOR WOULD CLOSE A MASKED BLOCK COMMENT EARLY, putting the remainder of the
    // masked body back into scanned text.
    [InlineData("<red*/acted>")]
    [InlineData("*/")]
    public void Constructor_RejectsAnInvalidPlaceholder(string placeholder)
    {
        ArgumentException failure =
            Assert.Throws<ArgumentException>(() => new SqlRedactor(placeholder: placeholder));

        Assert.Equal("placeholder", failure.ParamName);
    }

    /// <summary>
    /// Every token form is idempotent under a CUSTOM placeholder too, not only under the default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE DEFAULT PLACEHOLDER HIDES ONE WHOLE CLASS OF INTERACTION, which is why this test exists as
    /// well as the table-driven theory. <c>&lt;redacted&gt;</c> happens to contain a <c>&gt;</c>, so on a
    /// second pass over an alternative-quoted mask the terminator search finds something. A placeholder
    /// such as <c>MASKED</c> contains no second <c>M</c> followed by a quote, so without the
    /// already-masked arm the second pass would read the mask as an UNTERMINATED literal and the
    /// fail-closed arm would swallow the entire remainder of the statement - losing text rather than
    /// merely growing the mask.
    /// </para>
    /// <para>
    /// One statement carrying all six token forms at once, redacted twice, so a regression in any of them
    /// shows up here whatever the placeholder is.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("MASKED")]
    [InlineData("?")]
    [InlineData("<redacted>")]
    [InlineData("[X]")]
    public void EveryTokenFormIsIdempotentUnderACustomPlaceholder(string placeholder)
    {
        SqlRedactor redactor = new(placeholder);

        const string statement =
            "SELECT * FROM COMPANY /* note */ WHERE NAME = q'[O'Brien]' AND ADDRESS = X'4142' "
                + "AND AGE = 0xFF AND SALARY > 12345 -- trailing\nAND ID IS NOT NULL";

        string once = redactor.Redact(statement);
        string twice = redactor.Redact(once);

        Assert.Equal(once, twice);

        // AND THE VALUES REALLY WENT: no fragment of any of the five literals survives either pass.
        foreach (string secret in new[] { "O'Brien", "4142", "0xFF", "12345", "note", "trailing" })
        {
            Assert.DoesNotContain(secret, once, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, twice, StringComparison.Ordinal);
        }

        // AND NOTHING WAS SWALLOWED: the structure after the last masked token is still there, which is
        // what a fail-closed arm firing on an already-masked token would have destroyed.
        Assert.EndsWith("AND ID IS NOT NULL", twice, StringComparison.Ordinal);
    }

    /// <summary>A null placeholder is rejected on the same terms as an empty one.</summary>
    [Fact]
    public void Constructor_RejectsANullPlaceholder()
    {
        ArgumentException failure =
            Assert.Throws<ArgumentException>(() => new SqlRedactor(placeholder: null!));

        Assert.Equal("placeholder", failure.ParamName);
    }

    // ==========================================================================================
    //  6. Redact(in DbErrorData) - ONE MEMBER CHANGES, FOUR DO NOT
    // ==========================================================================================

    /// <summary>
    /// Only <see cref="DbErrorData.SqlSyntax"/> changes. The provider code, the message text, the
    /// buffer and the one-based row ordinal are byte-identical (constraint C-B).
    /// </summary>
    [Fact]
    public void RedactPayload_ChangesOnlyTheStatement()
    {
        DbErrorData original = DbErrorData.FromStatement(
            19,
            "NOT NULL constraint failed: COMPANY.NAME",
            "INSERT INTO COMPANY (NAME,AGE,SALARY) VALUES ('Alice',30,12345.67)",
            DwBuffer.Filter,
            7);

        DbErrorData redacted = Subject.Redact(original);

        Assert.Equal(
            $"INSERT INTO COMPANY (NAME,AGE,SALARY) VALUES ('{Mask}',{Mask},{Mask})",
            redacted.SqlSyntax);
        Assert.Equal(original.SqlDbCode, redacted.SqlDbCode);
        Assert.Equal(original.SqlErrText, redacted.SqlErrText);
        Assert.Equal(original.Buffer, redacted.Buffer);
        Assert.Equal(original.Row, redacted.Row);
    }

    /// <summary>
    /// The framework-synthesized payload passes through completely unchanged: its hardcoded Chinese
    /// diagnostic [n_cst_thread_task_sqlupdate.sru:L190] is not touched, and its already-empty
    /// statement stays empty.
    /// </summary>
    [Fact]
    public void RedactPayload_NoUpdatableTable_PassesThroughUnchanged()
    {
        DbErrorData original = DbErrorData.NoUpdatableTable();

        DbErrorData redacted = Subject.Redact(original);

        Assert.Equal(DbErrorMessages.NoUpdatableTable, redacted.SqlErrText);
        Assert.Equal(string.Empty, redacted.SqlSyntax);
        Assert.Equal(-1, redacted.SqlDbCode);
        Assert.Equal(DwBuffer.Primary, redacted.Buffer);
        Assert.Equal(0, redacted.Row);
        Assert.Equal(original, redacted);
    }

    /// <summary>
    /// The cleared payload survives redaction as the cleared payload, so a "no error was recorded"
    /// check still holds after the value has crossed this seam.
    /// </summary>
    [Fact]
    public void RedactPayload_ClearedState_RemainsEqualToEmpty()
    {
        Assert.Equal(DbErrorData.Empty, Subject.Redact(DbErrorData.Empty));
    }

    /// <summary>
    /// A payload whose statement carries no literal at all survives byte for byte, which is what
    /// makes "unchanged" a meaningful observation elsewhere: the payload changes when there is a
    /// literal to mask, and only then.
    /// </summary>
    [Fact]
    public void RedactPayload_StatementWithNoLiteral_LeavesEveryMemberAlone()
    {
        DbErrorData original = DbErrorData.FromStatement(
            19,
            "NOT NULL constraint failed: COMPANY.NAME",
            "INSERT INTO COMPANY (NAME) SELECT NAME FROM COMPANY_STAGING",
            DwBuffer.Delete,
            3);

        DbErrorData redacted = Subject.Redact(original);

        Assert.Equal(original, redacted);
    }

    // ==========================================================================================
    //  7. ToDbError - THE ONLY SANCTIONED WIRE MAPPING (constraint C-F, STRUCTURALLY ENFORCED)
    // ==========================================================================================

    /// <summary>
    /// All five members are copied onto the wire message and the statement field is masked. The
    /// generated property spellings are protobuf's - <c>Sqldbcode</c>, <c>Sqlerrtext</c>,
    /// <c>Sqlsyntax</c> - because each legacy field name is a single lower-case token.
    /// </summary>
    [Fact]
    public void ToDbError_CopiesAllFiveMembersAndMasksTheStatement()
    {
        DbErrorData error = DbErrorData.FromStatement(
            19,
            "NOT NULL constraint failed: COMPANY.NAME",
            "UPDATE COMPANY SET SALARY = 12345.67 WHERE NAME = 'Alice'",
            DwBuffer.Filter,
            7);

        DbError message = error.ToDbError();

        Assert.Equal(19, message.Sqldbcode);
        Assert.Equal("NOT NULL constraint failed: COMPANY.NAME", message.Sqlerrtext);
        Assert.Equal($"UPDATE COMPANY SET SALARY = {Mask} WHERE NAME = '{Mask}'", message.Sqlsyntax);
        Assert.Equal(DwBuffer.Filter, message.Buffer);
        Assert.Equal(7, message.Row);
    }

    /// <summary>
    /// The cleared payload maps onto a message that equals a default-constructed one, which is only
    /// true because <see cref="DwBuffer.Primary"/> is the published enum's zero member.
    /// </summary>
    [Fact]
    public void ToDbError_ClearedPayload_EqualsADefaultMessage()
    {
        DbError message = DbErrorData.Empty.ToDbError();

        Assert.Equal(new DbError(), message);
        Assert.Equal(string.Empty, message.Sqlsyntax);
    }

    /// <summary>
    /// The synthesized payload's Chinese diagnostic reaches the wire verbatim; only the statement is
    /// ever altered.
    /// </summary>
    [Fact]
    public void ToDbError_NoUpdatableTable_CarriesTheChineseDiagnosticVerbatim()
    {
        DbError message = DbErrorData.NoUpdatableTable().ToDbError();

        Assert.Equal(DbErrorMessages.NoUpdatableTable, message.Sqlerrtext);
        Assert.Equal(-1, message.Sqldbcode);
        Assert.Equal(string.Empty, message.Sqlsyntax);
    }

    /// <summary>
    /// THE STRUCTURAL ASSERTION BEHIND C-F, IN ITS STRONGER FORM: there is exactly ONE
    /// <c>ToDbError</c>, it takes ONLY the payload, and NO overload anywhere accepts an
    /// <see cref="ISqlRedactor"/> or any other delegate or interface that could stand in for the
    /// policy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A mandatory redactor PARAMETER was the earlier shape of this control, and it was not a control
    /// at all: any implementation satisfied it, including one that masked nothing, so the wire boundary
    /// was one argument away from being bypassed at every call site. The policy therefore moved INTO
    /// the projection. What has to be asserted now is an absence - that no seam has been reintroduced -
    /// and an absence can only be asserted structurally.
    /// </para>
    /// </remarks>
    [Fact]
    public void ToDbError_HasExactlyOneOverload_TakesOnlyThePayload_AndAcceptsNoInjectablePolicy()
    {
        MethodInfo[] overloads = typeof(DbErrorDataExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(method => method.Name == "ToDbError")
            .ToArray();

        MethodInfo only = Assert.Single(overloads);
        ParameterInfo receiver = Assert.Single(only.GetParameters());

        // `this in DbErrorData` - the receiver is by-reference because the payload is a readonly
        // record struct, which is the plan's mapping for the legacy `readonly` parameter.
        Assert.Equal(typeof(DbErrorData).MakeByRefType(), receiver.ParameterType);
        Assert.True(receiver.IsIn);

        // NOTHING on this holder accepts a redactor, a delegate or any other substitutable policy.
        Assert.DoesNotContain(
            typeof(DbErrorDataExtensions).GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly),
            method => method.GetParameters().Any(parameter =>
                parameter.ParameterType == typeof(ISqlRedactor)
                || typeof(Delegate).IsAssignableFrom(parameter.ParameterType)));
    }

    /// <summary>
    /// The wire projection masks REGARDLESS of what any <see cref="ISqlRedactor"/> in the process
    /// does. A pass-through implementation exists in this file and is deliberately handed nowhere:
    /// there is no argument through which it could reach the projection.
    /// </summary>
    /// <remarks>
    /// This is the behavioural half of the finding's remedy. The structural test above proves the
    /// parameter is gone; this one proves the outcome - that the statement on the wire is masked even
    /// though a pass-through redactor is available in the same assembly and satisfies the same
    /// interface the log path consumes.
    /// </remarks>
    [Fact]
    public void ToDbError_MasksEvenThoughAPassThroughImplementationExists()
    {
        const string statement = "SELECT * FROM COMPANY WHERE NAME = 'Alice' AND AGE = 30";

        DbErrorData error = DbErrorData.FromStatement(
            1,
            "syntax error",
            statement,
            DwBuffer.Primary,
            0);

        // The pass-through is a legitimate ISqlRedactor. It can serve the log path, and it returns the
        // statement untouched - which is exactly what must NOT be able to reach the wire.
        ISqlRedactor passThrough = new PassThroughRedactor();
        Assert.Equal(statement, passThrough.Redact(statement));

        DbError message = error.ToDbError();

        Assert.Equal($"SELECT * FROM COMPANY WHERE NAME = '{Mask}' AND AGE = {Mask}", message.Sqlsyntax);
        Assert.DoesNotContain("Alice", message.Sqlsyntax, StringComparison.Ordinal);
        Assert.DoesNotContain("30", message.Sqlsyntax, StringComparison.Ordinal);
    }

    /// <summary>
    /// <see cref="SqlRedactor"/> is sealed, so no subclass can weaken the policy the projection
    /// reaches through <see cref="SqlRedactor.Instance"/>, and <see cref="DbErrorDataExtensions"/> is
    /// a static class.
    /// </summary>
    [Fact]
    public void RedactorIsSealed_AndTheMappingHolderIsStatic()
    {
        Assert.True(typeof(SqlRedactor).IsSealed);
        Assert.True(typeof(DbErrorDataExtensions) is { IsAbstract: true, IsSealed: true });
    }

    // ==========================================================================================
    //  8. THE ABSTRACTION'S OWN SHAPE
    // ==========================================================================================

    /// <summary>
    /// <see cref="ISqlRedactor"/> declares exactly one member, so it cannot grow into a
    /// general-purpose payload sanitizer. The <see cref="DbErrorData"/> convenience deliberately
    /// lives on the concrete type instead.
    /// </summary>
    [Fact]
    public void Interface_DeclaresExactlyOneMember()
    {
        MethodInfo[] members = typeof(ISqlRedactor).GetMethods();

        MethodInfo only = Assert.Single(members);

        Assert.Equal(nameof(ISqlRedactor.Redact), only.Name);
        Assert.Equal(typeof(string), only.ReturnType);
        Assert.Equal(typeof(string), Assert.Single(only.GetParameters()).ParameterType);
        Assert.Null(typeof(ISqlRedactor).GetMethod(
            nameof(ISqlRedactor.Redact),
            [typeof(DbErrorData).MakeByRefType()]));
    }

    // ==========================================================================================
    //  HELPERS
    // ==========================================================================================

    /// <summary>Counts non-overlapping occurrences of <paramref name="value"/>.</summary>
    /// <param name="text">The text to search.</param>
    /// <param name="value">The value to count. Must not be empty.</param>
    /// <returns>The number of occurrences.</returns>
    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = text.IndexOf(value, StringComparison.Ordinal);

        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }

    /// <summary>
    /// A redactor that masks nothing, used to prove the mapping is driven by the abstraction rather
    /// than by the concrete implementation.
    /// </summary>
    /// <remarks>
    /// The <see cref="AllowNullAttribute"/> is REQUIRED and is not decoration: the interface declares
    /// the parameter null-accepting, and the compiler enforces that an implementation says so too -
    /// omitting it fails the build with CS8767 under this repository's nullable context and
    /// warnings-as-errors setting. That is the null-tolerance of the contract being enforced rather
    /// than merely documented, so every double written against this interface must repeat it.
    /// </remarks>
    private sealed class PassThroughRedactor : ISqlRedactor
    {
        /// <inheritdoc />
        public string Redact([AllowNull] string statement) => statement ?? string.Empty;
    }
}
