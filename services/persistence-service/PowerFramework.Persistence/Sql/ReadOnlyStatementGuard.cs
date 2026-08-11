// ==================================================================================================
//  ReadOnlyStatementGuard.cs - WHAT A READ-SCOPED CALLER MAY ASK CONTRACT C-05 TO RUN
//  ------------------------------------------------------------------------------------------------
//  ORACLE   ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru        (READ ONLY)
//               :L469-L472  of_setsql      - a plain assignment, NO guard of any kind
//               :L474-L478  of_setsqlsyntax - a plain assignment, NO guard of any kind
//               :L599-L621  the run-time resolution: a supplied SYNTAX wins over a statement, and an
//                           absent statement is refused HERE rather than at the setter
//           ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14                          (READ ONLY)
//               retrieve="SELECT * FROM COMPANY" - the one evidenced retrieval in the repository
//
//  WHY THIS FILE EXISTS, AND WHY THE ORACLE HAS NO EQUIVALENT. PowerFramework is a LIBRARY. It opens
//  no socket, registers no route and receives no unsolicited request, so the only caller that could
//  ever reach `of_setsql` was code already running inside the same process with the same rights as
//  everything else in it. There was nothing to authorize and therefore nothing to guard. Decomposition
//  created the system's first ingress, and with it the first caller whose rights are NARROWER than the
//  process's: contract C-05 is published under the READ scope, and its QuerySpec now carries a
//  caller-authored statement and a caller-authored DataWindow syntax over a network.
//
//  A read-scoped credential that can reach an arbitrary statement is not a read scope. Microsoft.Data
//  .Sqlite executes every statement in a batch it is handed, so a single spliced semicolon turns a
//  retrieval into a retrieval plus a DELETE; and even without a batch, SQLite accepts
//  `WITH x AS (...) INSERT INTO ...`, whose leading keyword is not a mutation at all. So the guard is
//  a REQUIRED ADDITION in exactly the sense AAP 0.4.2.6 uses for Errors/SqlRedactor.cs - a property the
//  legacy could not have needed, made necessary by the boundary the migration introduced - and it is
//  the same posture AAP 0.1.5 fixes for this situation: where a legacy behaviour cannot be reproduced
//  safely across a boundary that did not previously exist, the contract is NARROWED WITH A DEFINED
//  ERROR, never widened with a guess.
//
//  IT REFUSES, AND REFUSING IS NOT REWRITING. Every method below either answers "admissible" and leaves
//  the caller's text untouched to the byte, or answers "not admissible" so the boundary can report
//  RetCode.E_INVALID_SQL and store nothing. There is no escaping, no quoting, no sanitising and no
//  rewriting anywhere in this file, because byte-exact statement parity is the acceptance criterion and
//  a guard that altered a character would break it. The refused and the still-accepted grammar are both
//  enumerated on persistence.v1.QuerySpec.sql and .sql_syntax; this file is the implementation of that
//  published text and the two must not drift.
//
//  IT SITS AT THE BOUNDARY, NOT IN THE TASK. Tasks/SqlQueryTask.cs is a faithful port of an object whose
//  setters have no guard, and adding one there would make the port unfaithful and would refuse callers
//  the legacy accepts on paths that never cross a scope boundary. The scope lives on the gRPC adapter -
//  the class that carries [Authorize(Policy = Read)] - so the enforcement lives there too, exactly as
//  the statement redaction lives at the egress rather than in the fault recorder.
//
//  WHAT IT DELIBERATELY DOES NOT DO (constraint C-B)
//  ------------------------------------------------------------------------------------------------
//    * IT DOES NOT REFUSE AN EMPTY STATEMENT. The oracle defers that check to run time, where an empty
//      statement answers E_INVALID_SQL with `SQL为空!` [:L615-L617], and contract C-05 forbids moving it
//      forward to configuration time. An empty or whitespace-only value is therefore ADMISSIBLE here and
//      is refused later, unchanged, by the code path that always refused it.
//    * IT DOES NOT REFUSE A MALFORMED STATEMENT. `SELECT bogus` is admissible; the provider rejects it,
//      with the provider's own diagnostic, exactly as before. This guard answers one question - may a
//      read-scoped caller ask for this at all - and never the question of whether it will work.
//    * IT DOES NOT TOUCH THE CLAUSE SETTERS. SqlClauseSpec.clause has its own guard, with its own
//      grammar, in Sql/ClauseModifier.cs. The two are siblings and are deliberately not merged: a clause
//      body is a predicate FRAGMENT spliced into a parsed statement, so it admits no statement keyword at
//      all and refuses every control character, whereas a whole statement legitimately begins with a
//      keyword and legitimately spans lines.
//    * IT DOES NOT GUARD C-06 OR C-07, AND THAT IS A SCOPE DECISION RATHER THAN AN OVERSIGHT. Both are
//      published under the WRITE scope, and generating and executing INSERT, UPDATE and DELETE is their
//      entire purpose [n_cst_thread_task_sqlupdate.sru:L204]. Refusing a DELETE on C-07's
//      SetCommandSql/Exec, or an update carrier's syntax on C-06's SetUpdateSyntax, would refuse the
//      contract. The least-privilege property those two rely on is the SCOPE SEPARATION itself: a
//      credential minted for `persistence.read` cannot reach either of them at all, which is what makes
//      gating the read surface the whole of the fix rather than half of it.
//
//  WHAT THIS FILE IS NOT
//  ------------------------------------------------------------------------------------------------
//  Pure in-memory string inspection. It opens no connection, touches no database, names no DBMS client,
//  performs no I/O, reads no clock, consumes no randomness and writes NO LOG - a log line here would
//  record the interpolated literals that Errors/SqlRedactor.cs exists to remove. It holds no mutable
//  state, so every member is static and every call is independent and thread safe.
//
//  RULES POSITION. review_rules returns exactly one line, "No user rules provided.", so the
//  enterprise-standard baseline applies. The constraints that bite here are C-G (least privilege: a
//  credential minted for one contract must not reach another contract's capability), C-B (no legacy
//  behaviour is corrected, which is why the emptiness and malformedness checks stay where they were),
//  C-F (no caller value is echoed, logged or quoted) and C-E (no database is fabricated or reached).
// ==================================================================================================

using PowerFramework.Persistence.Data;

namespace PowerFramework.Persistence.Sql;

/// <summary>
/// Decides whether a caller-supplied retrieval statement, or a caller-supplied DataWindow syntax
/// carrying one, is admissible on the read-scoped C-05 surface.
/// </summary>
/// <remarks>
/// <para>
/// <b>ADMISSIBILITY IS NOT VALIDITY.</b> A statement this type admits may still be rejected by the
/// provider, by the DataWindow runtime or by the task's own run-time emptiness check, all of which are
/// unchanged. The single question answered here is whether a caller holding only
/// <c>persistence.read</c> is entitled to ask for it.
/// </para>
/// <para>
/// See the file header for why the guard exists at all, why it lives on the boundary rather than in the
/// task, and what it deliberately leaves alone.
/// </para>
/// </remarks>
internal static class ReadOnlyStatementGuard
{
    /// <summary>The sentinel <see cref="string.IndexOf(char)"/> and this file's scanner use for "none".</summary>
    private const int NotFound = -1;

    /// <summary>
    /// The SQL string-literal delimiter, named so no method below carries an escaped quote literal.
    /// </summary>
    private const char SingleQuote = '\u0027';

    /// <summary>
    /// The bare words a whole statement may begin with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THREE, AND EACH ONE EARNS ITS PLACE. <c>SELECT</c> is the retrieval the contract exists for.
    /// <c>WITH</c> introduces a common-table expression, which SQLite, SQL Server and Oracle all accept
    /// ahead of a <c>SELECT</c> - refusing it would refuse an ordinary read. <c>VALUES</c> is a
    /// standalone row-source statement in SQLite and is a legitimate, side-effect-free read.
    /// </para>
    /// <para>
    /// <b><c>WITH</c> IS THE ONE THAT NEEDS THE REST OF THE SCAN.</b> `WITH x AS (...) INSERT INTO ...`
    /// is valid SQLite: the leading keyword says nothing about what the statement DOES. That is why the
    /// leading-keyword test alone is insufficient and the refused-word scan runs over the whole text.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> AdmissibleLeadingWords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT",
            "WITH",
            "VALUES",
        };

    /// <summary>
    /// The bare words no read-scoped statement may contain outside a quoted context.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A DENY LIST OF STATEMENT VERBS, NOT AN ALLOW LIST OF EXPRESSION GRAMMAR.</b> An allow list
    /// would have to enumerate every function, operator and dialect keyword three DBMS families accept,
    /// and would refuse ordinary reads the moment one was missed. What actually needs refusing is small
    /// and closed: the verbs that change state, the verbs that change transaction boundaries, and the
    /// two procedure families whose whole purpose is to execute something else.
    /// </para>
    /// <para>
    /// <b>WHAT IS DELIBERATELY ABSENT MATTERS AS MUCH AS WHAT IS PRESENT.</b>
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///   <c>END</c> is NOT refused: it closes every <c>CASE</c> expression, which is ordinary in a
    ///   select list. Refusing it would refuse a large class of legitimate reads to block nothing - a
    ///   bare <c>END</c> cannot begin a statement here, because the leading word must be one of three
    ///   and no second statement can exist.
    ///   </description></item>
    ///   <item><description>
    ///   <c>TABLE</c>, <c>VIEW</c>, <c>INDEX</c>, <c>TRIGGER</c>, <c>TEMP</c> and <c>TEMPORARY</c> are
    ///   NOT refused: they are the OBJECTS of <c>CREATE</c>, <c>ALTER</c> and <c>DROP</c>, all three of
    ///   which are refused, and each of them is also a legitimate identifier or table-function name.
    ///   Refusing the object as well as the verb buys nothing and costs real reads.
    ///   </description></item>
    ///   <item><description>
    ///   <c>REPLACE</c> is handled SEPARATELY rather than listed here, because it is both a SQLite DML
    ///   verb and a standard scalar function. See <see cref="IsRefusedWordAt"/>.
    ///   </description></item>
    /// </list>
    /// <para>
    /// <b><c>LOAD_EXTENSION</c> IS THE ONE ENTRY THAT IS NOT A STATEMENT VERB</b>, and it is the most
    /// important. It is a scalar FUNCTION, so it is reachable from inside an ordinary select list, and
    /// what it does is load and execute arbitrary native code. The provider disables extension loading
    /// by default, but a guard that depends on a provider default is a guard that breaks when the
    /// default is changed somewhere else, so the name is refused outright.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> RefusedStatementWords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Data manipulation.
            "INSERT",
            "UPDATE",
            "DELETE",
            "UPSERT",
            "MERGE",

            // The target of a SELECT ... INTO, which materialises a table rather than returning rows.
            "INTO",

            // Data definition.
            "CREATE",
            "ALTER",
            "DROP",
            "TRUNCATE",
            "RENAME",

            // Maintenance, all of which write.
            "REINDEX",
            "VACUUM",
            "ANALYZE",

            // Database and pragma control. Each of these can only be a statement, and each of them
            // reaches storage or engine configuration.
            "ATTACH",
            "DETACH",
            "PRAGMA",

            // Transaction control, which would let a caller commit or abandon the task's own work.
            "BEGIN",
            "COMMIT",
            "ROLLBACK",
            "SAVEPOINT",

            // Permissions.
            "GRANT",
            "REVOKE",
            "DENY",

            // Executing something other than this statement.
            "EXEC",
            "EXECUTE",
            "CALL",
            "DECLARE",
            "WAITFOR",
            "SHUTDOWN",
            "RECONFIGURE",
            "BACKUP",
            "RESTORE",
            "OPENROWSET",
            "OPENQUERY",
            "OPENDATASOURCE",

            // A function, not a verb - and the only one here. See the remarks above.
            "LOAD_EXTENSION",
        };

    /// <summary>
    /// The two stored-procedure name prefixes that name whole families rather than single words.
    /// </summary>
    /// <remarks>
    /// The same two <c>Sql/ClauseModifier.cs</c> refuses, for the same reason: an extended or system
    /// procedure name is not enumerable, and every member of both families executes something.
    /// </remarks>
    private static readonly string[] RefusedWordPrefixes = ["xp_", "sp_"];

    /// <summary>
    /// Whether a caller-supplied retrieval statement may be accepted on the read-scoped surface.
    /// </summary>
    /// <param name="sql">
    /// The statement as the caller sent it. <see langword="null"/>, empty and whitespace-only are all
    /// ADMISSIBLE - see the file header for why the emptiness check stays at run time.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a read-scoped caller is entitled to ask for this statement;
    /// <see langword="false"/> when the boundary must answer <c>RetCode.E_INVALID_SQL</c> and store
    /// nothing.
    /// </returns>
    /// <remarks>
    /// It answers a decision and never throws, because every caller is a handler that reports a return
    /// code and the whole point is to turn a hostile input into that code rather than into an exception a
    /// gRPC handler would surface as an unhelpful internal failure.
    /// </remarks>
    internal static bool IsAdmissibleStatement(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            // Nothing to run means nothing to refuse. The run-time check owns this case (C-B).
            return true;
        }

        return HasAdmissibleLeadingWord(sql) && HasNoRefusedStructure(sql);
    }

    /// <summary>
    /// Whether a caller-supplied DataWindow syntax may be accepted on the read-scoped surface.
    /// </summary>
    /// <param name="syntax">The syntax blob as the caller sent it.</param>
    /// <returns>
    /// <see langword="true"/> when the retrieval the syntax declares is admissible, or when it declares
    /// none at all; <see langword="false"/> otherwise.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>IT VALIDATES EXACTLY WHAT WILL EXECUTE, AND THAT IS WHY IT REUSES THE RUNTIME'S OWN READER.</b>
    /// <see cref="GridSyntax.SelectOf"/> is the method the DataWindow runtime itself
    /// calls to lift a syntax's retrieval clause out for registration, so whatever it returns IS the text
    /// this service will hand to the provider. Re-implementing the extraction here would create a second
    /// reading of the same blob, and the two readings disagreeing is precisely how a guard gets bypassed.
    /// </para>
    /// <para>
    /// <b>A SYNTAX THAT DECLARES NO RETRIEVAL IS ADMISSIBLE, AND THAT IS NOT A HOLE.</b> The extraction
    /// answers the empty string when the blob carries no <c>retrieve="..."</c> clause, and a definition
    /// with no statement cannot execute one - the runtime refuses to build a carrier from it, with its own
    /// diagnostic, exactly as it did before this guard existed. Refusing here instead would move a
    /// run-time refusal forward to configuration time, which is the change C-B forbids.
    /// </para>
    /// </remarks>
    internal static bool IsAdmissibleSyntax(string? syntax) =>
        string.IsNullOrWhiteSpace(syntax)
        || IsAdmissibleStatement(GridSyntax.SelectOf(syntax));

    /// <summary>
    /// Whether the first bare word of a statement is one this contract admits.
    /// </summary>
    /// <param name="sql">The statement, known to carry at least one non-whitespace character.</param>
    /// <remarks>
    /// <b>THE SCAN SKIPS LEADING WHITESPACE AND NOTHING ELSE.</b> It deliberately does not skip a leading
    /// comment or a leading open parenthesis: `/* */ SELECT ...` and `(SELECT ...)` are both refused,
    /// the first because comments are refused outright and the second because admitting a parenthesised
    /// statement would mean this method could no longer find the leading keyword at all. Neither form is
    /// produced by anything in this repository, and the evidenced retrieval is the bare
    /// <c>SELECT * FROM COMPANY</c> [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14</c>].
    /// </remarks>
    private static bool HasAdmissibleLeadingWord(string sql)
    {
        int index = 0;

        while (index < sql.Length && char.IsWhiteSpace(sql[index]))
        {
            index++;
        }

        int start = index;

        while (index < sql.Length && IsWordCharacter(sql[index]))
        {
            index++;
        }

        return index > start && AdmissibleLeadingWords.Contains(sql[start..index]);
    }

    /// <summary>
    /// Whether a statement is free of the structure a read-scoped caller may not send.
    /// </summary>
    /// <param name="sql">The statement to scan.</param>
    /// <remarks>
    /// <para>
    /// <b>ONE LEFT-TO-RIGHT PASS, AND THE ORDER OF THE RULES IS WHAT MAKES THEM SOUND.</b> Each rule is
    /// applied only in the state where it means what it says: a semicolon, a comment introducer or a
    /// refused word inside a string literal or a quoted identifier is DATA, not structure, so the scan
    /// tracks quoting state rather than searching for substrings. A substring search would refuse
    /// <c>WHERE note = 'delete me'</c> and would still miss <c>[delete]</c>.
    /// </para>
    /// <para>
    /// <b>THE WHITESPACE RULE DIFFERS FROM THE CLAUSE GUARD'S, DELIBERATELY.</b>
    /// <c>Sql/ClauseModifier.cs</c> admits the space and refuses every other control character, because a
    /// predicate fragment has no reason to span lines. A whole statement routinely does - the evidenced
    /// paging rewriters emit multi-line SQL - so the tab, the carriage return and the line feed are
    /// admitted here and every other control character is still refused.
    /// </para>
    /// <para>
    /// <b>A TRAILING SEMICOLON IS ADMITTED; ANY OTHER SEMICOLON IS NOT.</b> `SELECT 1;` is one statement
    /// written with its terminator, which callers and tools both do, and refusing it would be a
    /// gratuitous rejection. A semicolon with anything but whitespace after it introduces a SECOND
    /// statement, which the provider would execute, and that is the batch this guard exists to stop.
    /// </para>
    /// </remarks>
    private static bool HasNoRefusedStructure(string sql)
    {
        int parenthesisDepth = 0;
        int wordStart = NotFound;

        for (int index = 0; index < sql.Length; index++)
        {
            char current = sql[index];

            // RULE 1, first so that no later rule has to reason about a control character. The four
            // whitespace forms a statement legitimately contains are admitted; every other control
            // character - the C0 range, the C1 range and the delete character - is refused.
            if (char.IsControl(current) && !IsAdmissibleWhitespace(current))
            {
                return false;
            }

            // A word ends at the first non-word character and is tested THERE, so a refused word at the
            // very end of the text is still tested by the flush after the loop.
            if (IsWordCharacter(current))
            {
                if (wordStart == NotFound)
                {
                    wordStart = index;
                }

                continue;
            }

            if (wordStart != NotFound)
            {
                if (IsRefusedWordAt(sql, wordStart, index))
                {
                    return false;
                }

                wordStart = NotFound;
            }

            switch (current)
            {
                case SingleQuote:
                    // A string literal. Its CONTENT is exempt from every other rule.
                    if (!TrySkipQuoted(sql, ref index, SingleQuote))
                    {
                        return false;
                    }

                    break;

                case '"':
                    // A quoted identifier, doubled-quote escaped, exempt for the same reason. It is also
                    // how a DataWindow syntax quotes its retrieval clause, so a statement lifted out of
                    // one can legitimately contain doubled quotes.
                    if (!TrySkipQuoted(sql, ref index, '"'))
                    {
                        return false;
                    }

                    break;

                case '`':
                    // MySQL-style quoting. Admitted as a delimiter so its content is exempt and its
                    // termination is checked, rather than left to be scanned as bare text.
                    if (!TrySkipQuoted(sql, ref index, '`'))
                    {
                        return false;
                    }

                    break;

                case '[':
                    // A BRACKETED IDENTIFIER IS A QUOTING CONTEXT, NOT A NESTING DEPTH. T-SQL delimits an
                    // identifier with brackets and escapes a literal close bracket by DOUBLING it;
                    // brackets do not nest, so counting depth would both mis-handle `[a]]b]` and leave
                    // the identifier's content exposed to the refused-word rule.
                    if (!TrySkipBracketed(sql, ref index))
                    {
                        return false;
                    }

                    break;

                case ']':
                    // A close with no open: a legitimate one is always consumed by the skip above, so
                    // reaching here means the text is desynchronised.
                    return false;

                case '(':
                    parenthesisDepth++;
                    break;

                case ')':
                    parenthesisDepth--;

                    if (parenthesisDepth < 0)
                    {
                        // A close ahead of its open. Refused on sight rather than at the end, because
                        // from here on the depth no longer describes the text.
                        return false;
                    }

                    break;

                case ';':
                    // RULE 2. One trailing terminator is a statement's own punctuation; anything after it
                    // is a second statement.
                    if (!IsTrailing(sql, index))
                    {
                        return false;
                    }

                    break;

                case '-':
                    // RULE 3. A single minus is subtraction; two adjacent ones start a line comment.
                    if (index + 1 < sql.Length && sql[index + 1] == '-')
                    {
                        return false;
                    }

                    break;

                case '/':
                    // RULE 3. A single solidus is division.
                    if (index + 1 < sql.Length && sql[index + 1] == '*')
                    {
                        return false;
                    }

                    break;

                case '*':
                    // RULE 3, the closing half. Refused symmetrically so that a statement cannot close a
                    // block comment the guard never saw opened.
                    if (index + 1 < sql.Length && sql[index + 1] == '/')
                    {
                        return false;
                    }

                    break;

                default:
                    // Every other printable character is an operator, a separator or punctuation, all of
                    // which ordinary retrievals need across three DBMS dialects.
                    break;
            }
        }

        // The trailing word, for a statement that ends on one - `ORDER BY name` and `... OR DROP` both
        // end without a delimiter, and only one of them may be accepted.
        if (wordStart != NotFound && IsRefusedWordAt(sql, wordStart, sql.Length))
        {
            return false;
        }

        // RULE 4's balance half, tested at the end because that is the only place it is knowable.
        // Brackets need no counterpart: a legitimate bracketed identifier is consumed whole by
        // TrySkipBracketed, and a stray close is refused on sight.
        return parenthesisDepth == 0;
    }

    /// <summary>
    /// Whether a semicolon at <paramref name="index"/> is followed by whitespace only.
    /// </summary>
    /// <param name="sql">The statement being scanned.</param>
    /// <param name="index">The position of the semicolon.</param>
    private static bool IsTrailing(string sql, int index)
    {
        for (int rest = index + 1; rest < sql.Length; rest++)
        {
            if (!char.IsWhiteSpace(sql[rest]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The four whitespace characters a whole statement may legitimately contain.
    /// </summary>
    /// <param name="candidate">The character to classify.</param>
    /// <remarks>
    /// Enumerated rather than delegated to <see cref="char.IsWhiteSpace(char)"/>, because that predicate
    /// also admits the vertical tab and the form feed - which no statement generator emits and which are
    /// exactly the kind of character that makes one text render as another.
    /// </remarks>
    private static bool IsAdmissibleWhitespace(char candidate) =>
        candidate is ' ' or '\t' or '\r' or '\n';

    /// <summary>
    /// Whether a character belongs to a bare word for the purposes of the refused-word test.
    /// </summary>
    /// <param name="candidate">The character to classify.</param>
    /// <remarks>
    /// The underscore and the digits are INCLUDED, and that inclusion is what protects ordinary
    /// identifiers: without it <c>updated_at</c> would scan as the two words <c>updated</c> and
    /// <c>at</c>, <c>load_extension</c> would never be seen at all, and <c>sp_</c> would never be seen as
    /// a prefix of anything. Letters are classified by <see cref="char.IsLetter(char)"/> rather than by
    /// an ASCII range, so a non-Latin identifier scans as one word instead of a run of delimiters.
    /// </remarks>
    private static bool IsWordCharacter(char candidate) =>
        char.IsLetterOrDigit(candidate) || candidate == '_';

    /// <summary>
    /// Whether the bare word spanning <c>[start, end)</c> is refused.
    /// </summary>
    /// <param name="sql">The statement being scanned.</param>
    /// <param name="start">The inclusive start of the word.</param>
    /// <param name="end">The exclusive end of the word.</param>
    /// <remarks>
    /// <para>
    /// <b><c>REPLACE</c> IS THE ONE WORD WHOSE VERDICT DEPENDS ON WHAT FOLLOWS IT, AND THE ASYMMETRY IS
    /// REAL RATHER THAN CONVENIENT.</b> <c>replace(name, 'a', 'b')</c> is a standard scalar function and
    /// belongs in a select list; <c>REPLACE INTO t ...</c> is SQLite DML, and `WITH x AS (...) REPLACE
    /// INTO ...` would reach it with an admissible leading keyword. A function call is always followed by
    /// an open parenthesis and the DML verb never is, so that single character separates them exactly.
    /// </para>
    /// <para>
    /// The look-ahead skips whitespace, because <c>replace (a, 'b', 'c')</c> is the same call written
    /// differently and the two must not be answered differently.
    /// </para>
    /// </remarks>
    private static bool IsRefusedWordAt(string sql, int start, int end)
    {
        ReadOnlySpan<char> word = sql.AsSpan(start, end - start);

        foreach (string prefix in RefusedWordPrefixes)
        {
            if (word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (word.Equals("REPLACE", StringComparison.OrdinalIgnoreCase))
        {
            return !IsFollowedByCall(sql, end);
        }

        return RefusedStatementWords.Contains(sql[start..end]);
    }

    /// <summary>
    /// Whether the next non-whitespace character after <paramref name="index"/> opens an argument list.
    /// </summary>
    /// <param name="sql">The statement being scanned.</param>
    /// <param name="index">The position just past the word.</param>
    private static bool IsFollowedByCall(string sql, int index)
    {
        for (int rest = index; rest < sql.Length; rest++)
        {
            if (char.IsWhiteSpace(sql[rest]))
            {
                continue;
            }

            return sql[rest] == '(';
        }

        return false;
    }

    /// <summary>
    /// Advances <paramref name="index"/> past a delimited run, honouring the doubled-delimiter escape.
    /// </summary>
    /// <param name="sql">The statement being scanned.</param>
    /// <param name="index">
    /// On entry, the position of the OPENING delimiter. On a successful return, the position of the
    /// closing one, so the caller's own increment resumes after it.
    /// </param>
    /// <param name="delimiter">The delimiter character.</param>
    /// <returns><see langword="false"/> when the run is never closed.</returns>
    /// <remarks>
    /// AN UNTERMINATED RUN IS REFUSED RATHER THAN TREATED AS EXTENDING TO THE END, because an unbalanced
    /// delimiter is how a caller makes the REST of a composed statement into literal text - or makes
    /// literal text into structure. It is a desynchronised input either way, and the only safe answer is
    /// to decline it.
    /// </remarks>
    private static bool TrySkipQuoted(string sql, ref int index, char delimiter)
    {
        for (int scan = index + 1; scan < sql.Length; scan++)
        {
            if (sql[scan] != delimiter)
            {
                continue;
            }

            // A DOUBLED DELIMITER IS AN ESCAPE, NOT A CLOSE. `'it''s'` is one literal, and treating the
            // middle pair as a close followed by an open would leave the scan reading `s` as bare text.
            if (scan + 1 < sql.Length && sql[scan + 1] == delimiter)
            {
                scan++;

                continue;
            }

            index = scan;

            return true;
        }

        return false;
    }

    /// <summary>
    /// Advances <paramref name="index"/> past a bracketed identifier.
    /// </summary>
    /// <param name="sql">The statement being scanned.</param>
    /// <param name="index">
    /// On entry, the position of the opening bracket. On a successful return, the position of the closing
    /// one.
    /// </param>
    /// <returns><see langword="false"/> when the identifier is never closed.</returns>
    /// <remarks>
    /// T-SQL escapes a literal close bracket by DOUBLING it, so <c>[a]]b]</c> is the single identifier
    /// <c>a]b</c>. Brackets do not nest inside an identifier, which is why this is a delimited skip and
    /// not a depth count.
    /// </remarks>
    private static bool TrySkipBracketed(string sql, ref int index)
    {
        for (int scan = index + 1; scan < sql.Length; scan++)
        {
            if (sql[scan] != ']')
            {
                continue;
            }

            if (scan + 1 < sql.Length && sql[scan + 1] == ']')
            {
                scan++;

                continue;
            }

            index = scan;

            return true;
        }

        return false;
    }
}
