// ==================================================================================================
//  SqlIdentifierGuard.cs - WHAT MAY OCCUPY AN IDENTIFIER POSITION IN A GENERATED STATEMENT
//  ------------------------------------------------------------------------------------------------
//  ORACLE   ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru            (READ ONLY)
//               :L98-L145   _of_updateprepare - resets every per-column flag and re-enables from a
//                           caller's descriptor, resolving each key column through Describe
//               :L204       Update(true, false) - the point the PowerBuilder RUNTIME generated the
//                           INSERT, UPDATE and DELETE statements from the loaded DataWindow
//           ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14                            (READ ONLY)
//               the sole updatable DataWindow: table COMPANY, six columns, every name a bare word
//
//  WHY THIS FILE EXISTS, AND WHY THE ORACLE HAS NO EQUIVALENT. In the legacy the identifiers that
//  reached a generated statement came from a COMPILED DataWindow that shipped inside the application,
//  and the only caller that could name a column or a table was code already running in the same
//  process with the same rights as everything else in it. PowerBuilder's own runtime composed the
//  statement; nothing a remote party sent could reach an identifier position, because there was no
//  remote party. Decomposition created one: contract C-06 publishes `TableUpdateContract` and
//  `sql_syntax`, so an authenticated caller now supplies the update table name, the updatable column
//  names, the key column names and the identity column name over a network, and this service composes
//  DML from them.
//
//  VALUES ARE PARAMETERIZED AND IDENTIFIERS CANNOT BE. Every value in every statement this service
//  generates travels as an `@pN` parameter, which is what makes value-position injection impossible.
//  An identifier has no parameter form in any dialect - `SELECT ... FROM @p1` is not a table
//  reference - so the identifier positions are the ones left, and they are exactly the positions the
//  C-06 write path concatenates. This guard is the admission test that closes them.
//
//  IT IS A SHAPE TEST, AND THAT IS THE WHOLE POINT. The obvious alternative - check the name against
//  the carrier's column model - CANNOT work on this path, because the model is itself derived from the
//  caller's own declaration: a carrier built from a supplied `sql_syntax` registers that declaration
//  into the catalogue that a later lookup would validate against, so the input would be checked
//  against itself. A shape test has no such circularity: it asks whether the text can occupy an
//  identifier position at all, which is a property of the text and of nothing the caller controls
//  elsewhere.
//
//  IT REFUSES, AND REFUSING IS NOT REWRITING. Every method below either answers "admissible" and
//  leaves the caller's text untouched to the byte, or answers "not admissible" so the boundary can
//  refuse with RetCode.E_INVALID_ARGUMENT and the carrier can fail its update before generating
//  anything. There is no escaping, no quoting, no bracketing and no normalisation anywhere in this
//  file. That is deliberate on two counts. Byte-exact generated SQL is the parity criterion for this
//  whole service [AAP 0.6.4], so a guard that quoted an identifier would change every generated
//  statement and invalidate every characterization recording taken against the fixture. And it is the
//  posture AAP 0.1.5 fixes for precisely this situation: where a legacy behaviour cannot be reproduced
//  safely across a boundary that did not previously exist, the contract is NARROWED WITH A DEFINED
//  ERROR, never widened with a guess.
//
//  ONE CHARACTER SET, ONE DEFINITION. Sql/SelectStatementModel.cs already needed to know which
//  characters can appear inside an identifier, to decide whether a candidate keyword is a keyword or
//  the tail of a longer name. That predicate is lifted here and consumed there, so the scanner that
//  READS a statement and the gate that admits an identifier into a WRITTEN one cannot drift apart -
//  a drift that would show up as a name the read path treats as one word and the write path accepts
//  as two.
//
//  WHAT IT DELIBERATELY DOES NOT DO (constraint C-B)
//  ------------------------------------------------------------------------------------------------
//    * IT DOES NOT ASK WHETHER THE IDENTIFIER EXISTS. A table or column this database does not have
//      is admissible here and is refused later, by the provider or by the preparer's own
//      unresolvable-column arm with the oracle's 无效的列名: diagnostic
//      [n_cst_thread_task_sqlupdate.sru:L118-L122]. Admissibility is not existence, exactly as
//      Sql/ReadOnlyStatementGuard.cs records for statements.
//    * IT DOES NOT REFUSE A RESERVED WORD. `ORDER` is a legal column name in a quoted context and the
//      legacy would have generated a statement for it; whether the provider accepts the unquoted form
//      is the provider's answer to give, not this guard's. Refusing it here would narrow the contract
//      for a case that fails cleanly anyway.
//    * IT DOES NOT REFUSE AN EMPTY DESCRIPTOR FIELD ON BEHALF OF ANOTHER RULE. An empty identity
//      column is LEGAL and means "no identity column" [:L127-L129], so callers screen that themselves
//      before asking this guard; the guard's own answer for the empty string is "not admissible",
//      because the empty string cannot occupy an identifier position.
//    * IT DOES NOT TOUCH THE STATEMENT SETTERS. A caller-authored whole statement is
//      ReadOnlyStatementGuard's subject on the read scope, and generating DML is C-06/C-07's entire
//      purpose on the write scope. This guard governs the identifier POSITIONS of statements this
//      service composes itself, which is a different question from what a caller may hand over whole.
//
//  WHAT THIS FILE IS NOT
//  ------------------------------------------------------------------------------------------------
//  Pure in-memory string inspection. It opens no connection, touches no database, names no provider,
//  performs no I/O, reads no clock, consumes no randomness and writes NO LOG - a log line here would
//  record the very text the callers are careful not to disclose (constraint C-F). It holds no mutable
//  state, so every member is static and every call is independent and thread safe.
// ==================================================================================================

namespace PowerFramework.Persistence.Sql;

/// <summary>
/// Decides whether a caller-supplied name may occupy an identifier position in a statement this
/// service generates.
/// </summary>
/// <remarks>
/// <para>
/// <b>ADMISSIBILITY IS NOT EXISTENCE AND NOT VALIDITY.</b> A name this type admits may still name
/// nothing, or be rejected by the provider as a reserved word. The single question answered here is
/// whether the text can occupy an identifier position at all - that is, whether concatenating it into
/// generated DML can express anything other than one identifier.
/// </para>
/// <para>
/// See the file header for why the test is a shape test rather than a catalogue lookup, why it
/// refuses rather than quotes, and what it deliberately leaves alone.
/// </para>
/// </remarks>
internal static class SqlIdentifierGuard
{
    /// <summary>
    /// The separator between the parts of a qualified name.
    /// </summary>
    private const char QualifierSeparator = '.';

    /// <summary>
    /// The most parts a qualified name may carry.
    /// </summary>
    /// <remarks>
    /// THREE, WHICH IS WHAT THE THREE DIALECTS IN THE PLAN CAN ADDRESS. SQLite reaches an attached
    /// database as <c>schema.table</c>; SQL Server addresses <c>database.schema.table</c>; Oracle
    /// addresses <c>schema.table</c>. A fourth part addresses nothing in any of them, so a name
    /// carrying one is refused rather than passed to a provider that cannot resolve it.
    /// </remarks>
    private const int MaximumQualifierParts = 3;

    /// <summary>
    /// Whether a character can appear inside a SQL identifier.
    /// </summary>
    /// <param name="value">The character to classify.</param>
    /// <returns>
    /// <see langword="true"/> when the character is one an identifier may contain.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The set covers the letters and digits every dialect allows plus the four extras the target
    /// dialects add - the underscore, and SQL Server's dollar, hash and at signs, which begin or
    /// appear in temporary-table and variable names.
    /// </para>
    /// <para>
    /// <b>LETTERS ARE CLASSIFIED BY UNICODE CATEGORY, NOT BY ASCII RANGE</b>, which is why the sole
    /// evidenced schema is not the limit of what this admits: the legacy estate is Chinese-authored
    /// and a CJK column name is an ordinary identifier in every dialect here. What the set excludes
    /// is what matters - whitespace, the quote characters, the statement separator, the comment
    /// markers, the parenthesis and every operator - so no admitted name can close an identifier
    /// position and open anything else.
    /// </para>
    /// <para>
    /// CONSUMED BY <c>SelectStatementModel</c>'s KEYWORD SCANNER AS WELL, which is why it lives here
    /// rather than in either caller. See the file header.
    /// </para>
    /// </remarks>
    internal static bool IsIdentifierCharacter(char value) =>
        char.IsLetterOrDigit(value) || value == '_' || value == '$' || value == '#' || value == '@';

    /// <summary>
    /// Whether a name may occupy an identifier position as a single unqualified identifier.
    /// </summary>
    /// <param name="candidate">The name to admit or refuse. May be <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="true"/> when every character is an identifier character and there is at least
    /// one.
    /// </returns>
    /// <remarks>
    /// <para>
    /// USED FOR COLUMN POSITIONS, which are never qualified in any statement this service generates:
    /// the update, insert, delete and conflict-projection statements each name exactly one table, so
    /// a column reference needs no qualifier and the legacy generated none.
    /// </para>
    /// <para>
    /// THE EMPTY STRING IS REFUSED, and null with it. Neither can occupy an identifier position, and
    /// admitting either would emit a statement with an empty name in it - which the provider rejects
    /// with a diagnostic that names the whole statement rather than the field that was empty.
    /// </para>
    /// </remarks>
    internal static bool IsAdmissibleIdentifier(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        foreach (char character in candidate)
        {
            if (!IsIdentifierCharacter(character))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether a name may occupy a table position, either bare or qualified.
    /// </summary>
    /// <param name="candidate">The name to admit or refuse. May be <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="true"/> when the name is one to three identifier parts separated by periods.
    /// </returns>
    /// <remarks>
    /// <para>
    /// SEPARATE FROM <see cref="IsAdmissibleIdentifier"/> BECAUSE A TABLE LEGITIMATELY CARRIES A
    /// QUALIFIER AND A COLUMN DOES NOT HERE. The sole evidenced fixture names a bare <c>COMPANY</c>
    /// [<c>dw_sqlite.srd:L14</c>], so the qualified form is not exercised by the oracle - but the
    /// update table arrives from a caller through <c>DataWindow.Table.UpdateTable</c> and a deployment
    /// addressing an attached database has no other way to say so. Refusing the qualified form would
    /// narrow the contract for a shape the dialects all accept.
    /// </para>
    /// <para>
    /// EVERY PART MUST BE NON-EMPTY, so a leading, trailing or doubled period is refused: those are
    /// the forms in which a period stops being a qualifier and starts being punctuation.
    /// </para>
    /// </remarks>
    internal static bool IsAdmissibleQualifiedName(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        // A SPAN SPLIT RATHER THAN string.Split, so admitting a name allocates nothing: this runs once
        // per identifier on every update, and the answer for the overwhelmingly common case - one bare
        // part - is reached without touching the heap.
        ReadOnlySpan<char> remaining = candidate;
        int parts = 0;

        while (!remaining.IsEmpty)
        {
            parts++;

            if (parts > MaximumQualifierParts)
            {
                return false;
            }

            int separator = remaining.IndexOf(QualifierSeparator);
            ReadOnlySpan<char> part = separator < 0 ? remaining : remaining[..separator];

            if (part.IsEmpty)
            {
                return false;
            }

            foreach (char character in part)
            {
                if (!IsIdentifierCharacter(character))
                {
                    return false;
                }
            }

            if (separator < 0)
            {
                return true;
            }

            remaining = remaining[(separator + 1)..];

            // A TRAILING SEPARATOR LEAVES AN EMPTY REMAINDER, which the loop would otherwise exit on
            // as though the name had ended cleanly. It has not: `COMPANY.` names no second part.
            if (remaining.IsEmpty)
            {
                return false;
            }
        }

        return false;
    }
}
