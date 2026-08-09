// ==============================================================================================
//  ClauseModifier - the clause-modification style vocabulary and the pending-clause upsert
//  --------------------------------------------------------------------------------------------
//  PORT OF          ws_objects/pfw.shared.pbl.src/enums.sru:L715-L722          the three styles
//                   ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru
//                       :L6, :L10-L14   type sqlclause from structure
//                       :L43-L44        SQLCLAUSE _whereClauses[] / _orderByClauses[]
//                       :L247-L267      _of_reset
//                       :L269-L284      of_setwhereclause
//                       :L286-L301      of_setorderbyclause
//                       :L684           the pre-loop presence test
//                       :L689-L702      the two apply loops - NOT ported here, see below
//                   ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru
//                       :L380, :L383    the two-arity caller-side proxies
//                   ws_objects/pfw.utility.parser.pbl.src/n_sql.sru:L42-L43, :L48-L49
//
//  ORACLE STATUS    All five paths above are READ ONLY. They are the behavioural oracle this
//                   migration is verified against and are never edited, moved, reformatted or
//                   deleted. Every behavioural decision below cites the legacy line it came from
//                   so the two sides can be diffed against each other by a later reader.
//
//  WHAT THIS FILE OWNS, AND WHAT IT DELIBERATELY DOES NOT
//  --------------------------------------------------------------------------------------------
//  The legacy splits clause modification across TWO objects, and the .NET side keeps the seam in
//  the same place rather than tidying it into one type:
//
//      n_cst_thread_task_sqlquery   accumulates pending clauses in two arrays, validates the
//                                   caller's arguments, and returns a RETURN CODE
//                                   -> this file: ClauseModifier
//      n_sql (native, pfw.dll)      performs the actual replace / append / prepend TEXT
//                                   transformation, and returns a BOOLEAN
//                                   -> the sibling file: SelectStatementModel.Modify*
//
//  So this file owns the style VOCABULARY and the pending-clause COLLECTION. It owns no text
//  transformation whatsoever: there is no string concatenation of clause bodies anywhere below,
//  and there must never be one, because duplicating the transformation would give the repository
//  two implementations of it to keep in agreement. SelectStatementModel states the same boundary
//  from its own side, in the remarks on its private ModifyClause, and the two statements are meant
//  to be read together.
//
//  THE APPLY LOOP IS NOT HERE EITHER, AND THAT IS THE SAME DECISION SEEN FROM THE OTHER END.
//  The legacy walks its two arrays at :L689-L702, calling ModifyWhere and ModifyOrder in their
//  three-argument form and abandoning the whole operation with RetCode.E_INTERNAL_ERROR on the
//  first false return. That loop belongs to Tasks/SqlQueryTask.cs, which is the object that also
//  owns the parse, the statement read-back at :L703 and the splice at :L709. This file exists to
//  make that loop writable - it exposes the two collections in order, plus the presence test the
//  legacy performs immediately before the loop at :L684 - and nothing more.
//
//  WHY THE SCREAMING_SNAKE CONSTANT SPELLINGS ARE PRESERVED
//  --------------------------------------------------------------------------------------------
//  SQL_MS_REPLACE, SQL_MS_APPEND and SQL_MS_PREPEND keep their legacy spelling, which departs from
//  C# naming convention on purpose. The reason is not sentiment: these identifiers travel in
//  serialized payloads, in log records and in characterization recordings, so renaming one would
//  silently invalidate every stored comparison in the parity suite that mentions it. The
//  repository-root .editorconfig carries a suppression for CA1707 and IDE1006 scoped to THIS FILE
//  BY NAME, and that narrow scoping is the design: because Directory.Build.props sets
//  TreatWarningsAsErrors for every project, a preserved-spelling identifier declared in a file
//  outside that scope would be a build error rather than a style nit. Within the Sql folder this
//  is the only file inside the scope. Consequently:
//
//    * do not rename these three constants;
//    * do not export a conventionally-named alias for them, because an alias is an invitation for
//      a file outside the suppression scope to declare its own copy and break the build;
//    * do not declare any other underscored identifier here - the exception is for the legacy
//      constant catalogue, not for this file in general.
//
//  THE NUMERIC VALUES COME FROM ONE PLACE
//  --------------------------------------------------------------------------------------------
//  The three constants below are defined in terms of Enums.SQL_MS_REPLACE, Enums.SQL_MS_APPEND and
//  Enums.SQL_MS_PREPEND in PowerFramework.Shared.Kernel rather than as bare literals 1, 2 and 3.
//  That keeps a single numeric source of truth for a value that also appears on the published
//  query contract, and the edge is mutually declared: the shared constant catalogue names this
//  file as the consumer of those three constants in its own SQL Parser section.
//
//  THE WIRE ENUM IS NOT DUPLICATED HERE
//  --------------------------------------------------------------------------------------------
//  shared/PowerFramework.Contracts/Proto/persistence.v1.proto declares
//
//      enum SqlModifyStyle { SQL_MS_UNSPECIFIED = 0; SQL_MS_REPLACE = 1;
//                            SQL_MS_APPEND = 2; SQL_MS_PREPEND = 3; }
//
//  in persistence.v1.proto and NOT in common.v1.proto, because its only consumer is the query
//  service's clause modification and the common file admits only shapes that two sibling
//  definitions both need. No local copy of that enum appears below; the single conversion between
//  it and the legacy numeric style lives in ToModifyStyle, and there is exactly one such member.
//
//  SQL_MS_UNSPECIFIED IS A PROTO3 MECHANICAL ARTIFACT, NOT A FOURTH LEGACY STYLE. The legacy set
//  has no zero value at all - a whole-file search of the constant catalogue returns exactly the
//  three lines cited above - while proto3 requires the first enumerator of every enum to be zero.
//  ToModifyStyle therefore maps it to a value the statement model REJECTS and never to
//  SQL_MS_REPLACE, because the standing posture where a legacy behaviour cannot be reproduced
//  across a network boundary is to narrow the contract with a defined error rather than widen it
//  with a guess.
//
//  THIS IS THE SQL-INJECTION SITE. IT IS DOCUMENTED, NOT SILENTLY REPAIRED
//  --------------------------------------------------------------------------------------------
//  This file is the boundary at which a caller-supplied RAW CLAUSE STRING enters the SQL layer.
//  The clause arrives unvalidated - the only test applied to it, at :L271 and :L288, is that it is
//  not the empty string - it is stored verbatim, and Tasks/SqlQueryTask.cs later splices it into
//  the generated statement through the statement model's modify path, reads the rewritten text
//  back at :L703 and writes it into the DataWindow's select property at :L709.
//
//  The mechanical root of the legacy exposure is the connection parameter string, not this file:
//  n_cst_thread_task_sqlbase.sru:L128-L129 parses DBParm for DisableBind and NCharBind, and
//  DisableBind=1 MEANS THE POWERBUILDER RUNTIME DOES NOT USE BIND VARIABLES - values are
//  interpolated into the statement text as literals.
//
//  THE MANDATED RESOLUTION. The implementation uses parameterized commands internally while keeping
//  the OBSERVABLE GENERATED STATEMENT unchanged, and the site is recorded as a known legacy defect.
//  Parameterization happens where commands are actually executed, which is not this file. So this
//  file adds NO escaping, NO quoting, NO sanitisation and NO REWRITING of any kind: every one of
//  those would alter the observable generated SQL, and byte-exact statement parity is the acceptance
//  criterion. Nothing below changes a single character of an accepted clause.
//
//  A BIND PARAMETER CANNOT PROTECT SQL STRUCTURE, WHICH IS WHY THE PARAGRAPH ABOVE IS NOT THE WHOLE
//  ANSWER. This file previously concluded from it that no validation belonged here at all. That
//  conclusion was wrong, and a review found it: parameterisation substitutes VALUES, whereas a
//  clause is spliced in as SYNTAX. The clause reaches n_sql's ModifyWhere / ModifyOrder entry points
//  and then the DataWindow's select property with NO grammar standing between it and execution, so
//  there is nothing downstream for this file to defer to. And the exposure is NEW rather than
//  inherited: the legacy is a library with no listener, so no caller could reach this setter from
//  outside the process at all, whereas C-05's SqlClauseSpec.clause now carries it over a network.
//
//  SO THE BOUNDARY REFUSES STRUCTURAL SQL, AND REFUSING IS NOT REWRITING. ValidateClauseBody below
//  either stores the caller's clause verbatim or stores nothing and answers
//  RetCode.E_INVALID_ARGUMENT - the SAME code the legacy's own guard already answers for a
//  non-positive index or an empty clause [:L271, :L288]. That is the posture AAP 0.1.5 fixes for
//  exactly this situation: a legacy behaviour that cannot be reproduced safely across a boundary
//  that did not previously exist is NARROWED WITH A DEFINED ERROR, never widened with a guess. The
//  refused grammar and the still-accepted grammar are both enumerated on
//  persistence.v1.SqlClauseSpec.clause, and this file is the implementation of that published text -
//  the two are meant to be read together and must not drift.
//
//  AND THE CLAUSE TEXT IS NEVER LOGGED FROM HERE, at any level. There is no logger field, no
//  logger parameter and no logging call anywhere below. Statement text is redacted by
//  Errors/SqlRedactor.cs on the error path; an unredacted log line added here would leak exactly
//  the interpolated literals that redaction exists to remove.
//
//  WHAT THIS FILE IS NOT
//  --------------------------------------------------------------------------------------------
//  Pure in-memory bookkeeping over strings and nothing else. It opens no connection, touches no
//  database, references no DBMS client, performs no I/O, reads no clock, consumes no randomness
//  and writes no log. No database is fabricated by it, directly or by implication: the two clause
//  collections are lists, and the only types it names beyond the base class library are the shared
//  constant catalogue, the shared return-code catalogue and the generated wire enum.
//
//  It holds no static mutable state, so instances are independent and each SQL query task owns
//  one - matching the legacy, where the two arrays are instance fields of the task object at
//  :L43-L44 rather than shared variables. Instance members are consequently NOT thread safe, which
//  is correct rather than an omission: the legacy setters are called on the CALLING thread through
//  the proxy at n_cst_threading_task_sqlquery.sru:L386-L393, which refuses the call outright with
//  RetCode.E_BUSY while the task is running, so the accumulation phase and the worker phase never
//  overlap. That E_BUSY guard belongs to the proxy layer under Tasks/, not here.
//
//  Visibility is internal, and it stays internal. The three consumers - Tasks/SqlQueryTask.cs,
//  Tasks/SqlTaskBase.cs and Grpc/QueryService.cs - are all inside this assembly, and the project
//  file already grants PowerFramework.Persistence.Tests access through InternalsVisibleTo, so
//  nothing needs widening for testability.
//
//  RULES POSITION
//  --------------------------------------------------------------------------------------------
//  No user rules were provided for this project: the rules document contains exactly one line
//  saying so. The binding constraints are therefore the enterprise-standard baseline plus the
//  named non-rule constraints, and each non-obvious decision above and below cites the one that
//  drives it. No rule is inferred, invented or back-filled from convention.
// ==============================================================================================

using PowerFramework.Contracts.Persistence.V1;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Persistence.Sql;

/// <summary>
/// One pending clause modification: which <c>SELECT</c> it addresses, how it combines with what is
/// already there, and the clause text itself.
/// </summary>
/// <remarks>
/// <para>
/// Reproduces the structure declared inside the legacy query task at
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L10-L14</c>:
/// </para>
/// <code>
/// type sqlclause from structure
///     integer     index
///     long        ms
///     string      clause
/// end type
/// </code>
/// <para>
/// <b>It is a private implementation detail of the task, not part of the statement parser's public
/// surface.</b> The structure is declared <i>within</i> the task object [<c>:L6</c>], exists only to
/// give the two accumulation arrays an element type [<c>:L43-L44</c>], and never crosses any
/// boundary. That is why the triple lives here beside the collection that holds it rather than in
/// its own file or on the statement model, and it is also why the published wire shape
/// <c>persistence.v1.SqlClauseSpec</c> mirrors the public setter signature instead of this
/// structure. The two are related but distinct, and this one is deliberately the local form.
/// </para>
/// <para>
/// A PowerBuilder structure maps to a <see langword="readonly record struct"/>: it is a plain field
/// triple with value semantics and no behaviour, which is exactly what a record struct is. Value
/// equality is synthesized, so two entries carrying the same index, style and text compare equal -
/// useful in the parity matrices, which assert on whole collections.
/// </para>
/// <para>
/// <b>Field spellings are anglicised, and only these.</b> <c>index</c> becomes
/// <see cref="SelectIndex"/> because the legacy setter's own parameter is named
/// <c>selectindex</c> [<c>:L269</c>] and because a member called <c>Index</c> beside a list
/// position invites precisely the confusion the remarks on
/// <see cref="ClauseModifier.WhereClauses"/> warn about; <c>ms</c> becomes
/// <see cref="ModifyStyle"/>, since <c>ms</c> is an abbreviation of "modify style" and carries no
/// serialized meaning of its own. Neither is a preserved identifier: the constant VALUES travel in
/// recordings, the structure's field names do not.
/// </para>
/// </remarks>
internal readonly record struct SqlClause
{
    /// <summary>
    /// Normalised clause text. Holds <see langword="null"/> for "no text", so that
    /// <see cref="Clause"/> can observe as <see cref="string.Empty"/> without ever being
    /// <see langword="null"/> - including for <c>default(SqlClause)</c>, which no code path in this
    /// file produces but which a caller can always write.
    /// </summary>
    private readonly string? _clause;

    /// <summary>
    /// Creates a pending clause modification.
    /// </summary>
    /// <param name="selectIndex">
    /// The one-based select index this clause addresses. Reproduces <c>integer index</c>. Not
    /// validated here: validation belongs to
    /// <see cref="ClauseModifier.SetWhereClause(int, long, string?)"/> and its siblings, which
    /// reject a non-positive value before ever constructing one of these.
    /// </param>
    /// <param name="modifyStyle">
    /// How the clause combines with the existing one. Reproduces <c>long ms</c>. Expected to be one
    /// of <see cref="ClauseModifier.SQL_MS_REPLACE"/>,
    /// <see cref="ClauseModifier.SQL_MS_APPEND"/> or
    /// <see cref="ClauseModifier.SQL_MS_PREPEND"/>, though the legacy stores it entirely unchecked
    /// - see the remarks on <see cref="ModifyStyle"/>.
    /// </param>
    /// <param name="clause">
    /// The clause body without its leading keyword. Reproduces <c>string clause</c>. A
    /// <see langword="null"/> value is normalised to empty so <see cref="Clause"/> never observes as
    /// <see langword="null"/>.
    /// </param>
    public SqlClause(int selectIndex, long modifyStyle, string? clause)
    {
        SelectIndex = selectIndex;
        ModifyStyle = modifyStyle;
        _clause = string.IsNullOrEmpty(clause) ? null : clause;
    }

    /// <summary>
    /// The one-based select index this clause addresses, reproducing <c>integer index</c> at
    /// <c>n_cst_thread_task_sqlquery.sru:L11</c>.
    /// </summary>
    /// <remarks>
    /// <b>One-based, and it is a key rather than a position.</b> The first <c>SELECT</c> in a
    /// statement is 1, not 0, which is why zero is rejected by the setters instead of meaning "the
    /// first one". Widened from the legacy 16-bit <c>integer</c> to <see langword="int"/>, matching
    /// both the statement model's own select-index parameters and the <c>int32 select_index</c>
    /// field on the published wire shape.
    /// </remarks>
    public int SelectIndex { get; }

    /// <summary>
    /// How this clause combines with the one already present, reproducing <c>long ms</c> at
    /// <c>n_cst_thread_task_sqlquery.sru:L12</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Stored unvalidated, deliberately.</b> The legacy setters guard the index and the emptiness
    /// of the clause and nothing else [<c>:L271</c>, <c>:L288</c>]; the style is written straight
    /// into the array slot with no test of any kind. Reproducing that faithfully means accepting any
    /// <see langword="long"/> here. An unrecognised value is not silently absorbed, though: the
    /// statement model's modify path has a default arm that changes nothing and reports failure, and
    /// the legacy apply loop treats that failure as <c>RetCode.E_INTERNAL_ERROR</c>
    /// [<c>:L689-L702</c>], so a bad style surfaces there rather than here.
    /// </para>
    /// <para>
    /// Typed as <see langword="long"/> rather than as the generated wire enum on purpose: this is
    /// the legacy numeric form, and the single conversion from the wire form is
    /// <see cref="ClauseModifier.ToModifyStyle(SqlModifyStyle)"/>.
    /// </para>
    /// </remarks>
    public long ModifyStyle { get; }

    /// <summary>
    /// The clause body without its leading keyword, reproducing <c>string clause</c> at
    /// <c>n_cst_thread_task_sqlquery.sru:L13</c>. Never <see langword="null"/>; reads as
    /// <see cref="string.Empty"/> when no text was supplied.
    /// </summary>
    /// <remarks>
    /// <b>Raw and opaque.</b> The text is whatever the caller supplied, byte for byte. It is neither
    /// escaped, quoted, sanitised, allow-listed nor parsed here - see the injection-site note in
    /// this file's header for why adding any of those would be a behavioural change rather than a
    /// hardening. Every instance reachable from <see cref="ClauseModifier"/> carries a non-empty
    /// value, because the setters reject an empty clause before constructing one.
    /// </remarks>
    public string Clause => _clause ?? string.Empty;
}

/// <summary>
/// Accumulates the pending <c>WHERE</c> and <c>ORDER BY</c> clause modifications of one SQL query
/// task, keyed on select index, and publishes the three legacy modify-style constants.
/// </summary>
/// <remarks>
/// <para>
/// Ports the clause-accumulation half of
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru</c>: the two arrays at
/// <c>:L43-L44</c>, the two setters at <c>:L269-L284</c> and <c>:L286-L301</c>, the two-arity
/// caller-side overloads at <c>n_cst_threading_task_sqlquery.sru:L380</c> and <c>:L383</c>, and the
/// array clearing inside <c>_of_reset</c> at <c>:L247-L267</c>.
/// </para>
/// <para>
/// <b>It performs no text transformation and runs no apply loop.</b> Both belong elsewhere by
/// design; the file header states the seam in full and explains why duplicating either would be a
/// defect rather than a convenience.
/// </para>
/// <para>
/// Not thread safe, which matches the legacy: the setters are called on the calling thread through
/// a proxy that refuses them with <c>RetCode.E_BUSY</c> while the task is running, so accumulation
/// and execution never overlap.
/// </para>
/// </remarks>
internal sealed class ClauseModifier
{
    // ------------------------------------------------------------------------------------------
    //  The modify styles - enums.sru:L718-L720
    //  ------------------------------------------------------------------------------------------
    //  Three values, and there is no fourth. A whole-file search of the legacy constant catalogue
    //  for SQL_MS_ returns exactly these three declarations, so there is also no legacy "no style"
    //  and no legacy "unspecified".
    //
    //  SPELLING PRESERVED, VALUE DELEGATED. The identifiers keep their SCREAMING_SNAKE form because
    //  they travel in serialized payloads, log records and characterization recordings; the numbers
    //  come from the shared constant catalogue so exactly one place in the repository decides what
    //  1, 2 and 3 mean. The header explains why no conventionally-named alias is offered.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Replace the clause outright. Value <c>1</c> [<c>enums.sru:L718</c>].
    /// </summary>
    /// <remarks>
    /// Replacing with empty text removes the clause, which is the statement model's behaviour rather
    /// than this file's - see <see cref="SelectStatementModel.ModifyWhere(int, long, string)"/>.
    /// Note that the public setters here refuse an empty clause outright, so that removal path is
    /// reachable only from the statement model's internal callers and never through
    /// <see cref="SetWhereClause(int, long, string?)"/>. That asymmetry is legacy behaviour: the
    /// paged-statement builder strips an <c>ORDER BY</c> by calling the parser directly, bypassing
    /// the public setter and therefore its guard.
    /// </remarks>
    public const long SQL_MS_REPLACE = Enums.SQL_MS_REPLACE;

    /// <summary>
    /// Append to the existing clause. Value <c>2</c> [<c>enums.sru:L719</c>].
    /// </summary>
    /// <remarks>
    /// Appending composes clause TEXT within a single select index. It is not a mechanism for
    /// accumulating separate entries: entries are upserted on the select index, so sending the same
    /// index twice replaces the earlier entry rather than adding to it. See
    /// <see cref="WhereClauses"/>.
    /// </remarks>
    public const long SQL_MS_APPEND = Enums.SQL_MS_APPEND;

    /// <summary>
    /// Prepend to the existing clause. Value <c>3</c> [<c>enums.sru:L720</c>].
    /// </summary>
    public const long SQL_MS_PREPEND = Enums.SQL_MS_PREPEND;

    // ------------------------------------------------------------------------------------------
    //  Internal invariants
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The select index the two-arity overloads supply, measured rather than assumed.
    /// </summary>
    /// <remarks>
    /// <c>n_cst_threading_task_sqlquery.sru:L380</c> is literally
    /// <c>return of_SetOrderByClause(1,ms,clause)</c> and <c>:L383</c> is
    /// <c>return of_SetWhereClause(1,ms,clause)</c>. <b>The short form addresses the FIRST select,
    /// not every select.</b> Reading it as "all blocks" would silently broaden a single-block
    /// modification into a statement-wide one on any compound <c>SELECT</c>. The same constant, at
    /// the same value and for the same reason, appears on
    /// <see cref="SelectStatementModel"/>'s own short arities.
    /// </remarks>
    private const int DefaultSelectIndex = 1;

    /// <summary>
    /// The not-found sentinel for the upsert scan.
    /// </summary>
    /// <remarks>
    /// <b>Minus one, and never zero.</b> Zero is a valid zero-based list position, so using it as a
    /// not-found marker would make "no entry matched" indistinguishable from "the first entry
    /// matched" - which is precisely the defect the remarks on
    /// <see cref="UpsertClause(List{SqlClause}, int, long, string?)"/> describe. No other value in
    /// this file is used as a not-found marker.
    /// </remarks>
    private const int NotFound = -1;

    // ------------------------------------------------------------------------------------------
    //  The two collections - n_cst_thread_task_sqlquery.sru:L43-L44
    //  ------------------------------------------------------------------------------------------
    //  Two separate arrays in the legacy, two separate lists here. They are never merged and never
    //  cross-consulted: the apply loop walks the WHERE array to completion and only then walks the
    //  ORDER BY array [:L689-L702], and a select index present in one carries no implication for
    //  the other. Field names are kept close to the legacy spellings so the two can be read side by
    //  side.
    // ------------------------------------------------------------------------------------------

    private readonly List<SqlClause> _whereClauses = [];

    private readonly List<SqlClause> _orderByClauses = [];

    /// <summary>
    /// The pending <c>WHERE</c> clause modifications, in insertion order. Reproduces
    /// <c>_whereClauses[]</c> at <c>n_cst_thread_task_sqlquery.sru:L43</c>.
    /// </summary>
    /// <value>
    /// <para>
    /// <b>Insertion order, not select-index order.</b> The legacy appends a new entry at the end of
    /// its array and overwrites a matching entry where it already sits, so indices added as 3, 1, 2
    /// are walked as 3, 1, 2. Nothing sorts this collection, and nothing may: the apply loop feeds
    /// the statement model in exactly this order.
    /// </para>
    /// <para>
    /// <b>A list POSITION here is not a SELECT INDEX.</b> They are different quantities and the
    /// distinction is load-bearing. The select index is a one-based key chosen by the caller and
    /// carried on <see cref="SqlClause.SelectIndex"/>; the position is a zero-based ordinal that
    /// exists only because this happens to be a list. One is never derived from the other - no code
    /// in this file subtracts one from a select index to reach a position, or treats a position as
    /// an index - and a consumer must read the index from the entry rather than infer it from where
    /// the entry sits.
    /// </para>
    /// <para>
    /// <b>Entries are upserted, keyed on select index.</b> Setting the same index twice replaces the
    /// earlier entry in place, leaving the count and the position unchanged;
    /// <see cref="SQL_MS_APPEND"/> composes clause text within one index and does not accumulate
    /// entries.
    /// </para>
    /// <para>
    /// A live read-only view rather than a copy, so a consumer sees later additions. Enumerating it
    /// while calling a setter is therefore not supported, on the same terms as any other list.
    /// </para>
    /// </value>
    public IReadOnlyList<SqlClause> WhereClauses => _whereClauses;

    /// <summary>
    /// The pending <c>ORDER BY</c> clause modifications, in insertion order. Reproduces
    /// <c>_orderByClauses[]</c> at <c>n_cst_thread_task_sqlquery.sru:L44</c>.
    /// </summary>
    /// <value>
    /// Identical semantics to <see cref="WhereClauses"/> on a wholly separate collection: same
    /// insertion ordering, same one-based-key-versus-zero-based-position distinction, same upsert
    /// rule. Setting a <c>WHERE</c> clause never touches this collection and setting an
    /// <c>ORDER BY</c> clause never touches that one.
    /// </value>
    public IReadOnlyList<SqlClause> OrderByClauses => _orderByClauses;

    /// <summary>
    /// Whether either collection holds anything, so a caller can skip statement rewriting entirely.
    /// </summary>
    /// <value>
    /// <see langword="true"/> when at least one <c>WHERE</c> or <c>ORDER BY</c> modification is
    /// pending.
    /// </value>
    /// <remarks>
    /// Reproduces the composite test the legacy performs immediately before its apply loop -
    /// <c>UpperBound(_whereClauses) &gt; 0 or UpperBound(_orderByClauses) &gt; 0</c> at
    /// <c>n_cst_thread_task_sqlquery.sru:L684</c> - and it lives here because it is a statement
    /// about these two collections and nothing else. The other half of that legacy condition, the
    /// stored-procedure test, is a property of the task rather than of the clauses and stays with
    /// the caller in <c>Tasks/</c>, as does the loop itself.
    /// </remarks>
    public bool HasPendingClauses => _whereClauses.Count > 0 || _orderByClauses.Count > 0;

    // ==========================================================================================
    //  The clause setters - n_cst_thread_task_sqlquery.sru:L269-L301
    //  ------------------------------------------------------------------------------------------
    //  Four members: two clause kinds, each in the two-arity caller-side form and the explicit
    //  select-index form. The two-arity pair reproduces n_cst_threading_task_sqlquery.sru:L380 and
    //  :L383, both of which delegate with a literal 1.
    //
    //  THEY RETURN A RETURN CODE, NOT A BOOLEAN, AND THAT ASYMMETRY WITH THE STATEMENT MODEL IS
    //  PRESERVED RATHER THAN HARMONISED. The legacy declares these as
    //  `public function long of_setwhereclause(...)` returning RetCode.OK or
    //  RetCode.E_INVALID_ARGUMENT [:L269, :L271, :L283], whereas the native statement parser it
    //  eventually feeds declares `public function boolean modifywhere(...)`
    //  [n_sql.sru:L42-L43]. The parser has no return-code algebra; the task layer does. Two layers
    //  reporting failure in two different vocabularies is what the oracle does, so unifying them
    //  would be a behavioural change dressed up as consistency.
    // ==========================================================================================

    /// <summary>
    /// Sets the <c>WHERE</c> clause modification for the FIRST select. Reproduces
    /// <c>of_setwhereclause(readonly long ms, readonly string clause)</c> at
    /// <c>n_cst_threading_task_sqlquery.sru:L383</c>.
    /// </summary>
    /// <param name="ms">
    /// The modify style: <see cref="SQL_MS_REPLACE"/>, <see cref="SQL_MS_APPEND"/> or
    /// <see cref="SQL_MS_PREPEND"/>. Not validated, matching the legacy.
    /// </param>
    /// <param name="clause">The clause body without its leading keyword.</param>
    /// <returns>
    /// <c>RetCode.OK</c> on success, or <c>RetCode.E_INVALID_ARGUMENT</c> when
    /// <paramref name="clause"/> is empty.
    /// </returns>
    /// <remarks>
    /// The legacy body is exactly <c>return of_SetWhereClause(1,ms,clause)</c>, so this addresses the
    /// first select and not every select - see <see cref="DefaultSelectIndex"/>. Because the index it
    /// supplies is 1, this overload can only ever fail on the clause half of the guard.
    /// </remarks>
    public long SetWhereClause(long ms, string? clause)
    {
        return SetWhereClause(DefaultSelectIndex, ms, clause);
    }

    /// <summary>
    /// Sets the <c>WHERE</c> clause modification for the addressed select. Reproduces
    /// <c>of_setwhereclause(readonly integer selectindex, readonly long ms, readonly string clause)</c>
    /// at <c>n_cst_thread_task_sqlquery.sru:L269-L284</c>.
    /// </summary>
    /// <param name="selectIndex">
    /// The ONE-BASED select index. Zero and negatives are rejected; zero does not mean "the first
    /// select".
    /// </param>
    /// <param name="ms">
    /// The modify style: <see cref="SQL_MS_REPLACE"/>, <see cref="SQL_MS_APPEND"/> or
    /// <see cref="SQL_MS_PREPEND"/>. Not validated, matching the legacy - see
    /// <see cref="SqlClause.ModifyStyle"/>.
    /// </param>
    /// <param name="clause">
    /// The clause body without its leading keyword. Empty is rejected;
    /// <see langword="null"/> is treated as empty and therefore rejected too.
    /// </param>
    /// <returns>
    /// <c>RetCode.OK</c> on success [<c>:L283</c>], or <c>RetCode.E_INVALID_ARGUMENT</c> when the
    /// guard at <c>:L271</c> rejects the arguments.
    /// </returns>
    /// <remarks>
    /// Upserts on <paramref name="selectIndex"/> into <see cref="WhereClauses"/>: an existing entry
    /// for that index is replaced in place, otherwise a new entry is appended at the end. See
    /// <see cref="UpsertClause(List{SqlClause}, int, long, string?)"/> for why that distinction
    /// matters more than it looks.
    /// </remarks>
    public long SetWhereClause(int selectIndex, long ms, string? clause)
    {
        return UpsertClause(_whereClauses, selectIndex, ms, clause);
    }

    /// <summary>
    /// Sets the <c>ORDER BY</c> clause modification for the FIRST select. Reproduces
    /// <c>of_setorderbyclause(readonly long ms, readonly string clause)</c> at
    /// <c>n_cst_threading_task_sqlquery.sru:L380</c>.
    /// </summary>
    /// <param name="ms">
    /// The modify style: <see cref="SQL_MS_REPLACE"/>, <see cref="SQL_MS_APPEND"/> or
    /// <see cref="SQL_MS_PREPEND"/>. Not validated, matching the legacy.
    /// </param>
    /// <param name="clause">The clause body without its leading keyword.</param>
    /// <returns>
    /// <c>RetCode.OK</c> on success, or <c>RetCode.E_INVALID_ARGUMENT</c> when
    /// <paramref name="clause"/> is empty.
    /// </returns>
    /// <remarks>
    /// The legacy body is exactly <c>return of_SetOrderByClause(1,ms,clause)</c>, so this addresses
    /// the first select and not every select - see <see cref="DefaultSelectIndex"/>.
    /// </remarks>
    public long SetOrderByClause(long ms, string? clause)
    {
        return SetOrderByClause(DefaultSelectIndex, ms, clause);
    }

    /// <summary>
    /// Sets the <c>ORDER BY</c> clause modification for the addressed select. Reproduces
    /// <c>of_setorderbyclause(readonly integer selectindex, readonly long ms, readonly string clause)</c>
    /// at <c>n_cst_thread_task_sqlquery.sru:L286-L301</c>.
    /// </summary>
    /// <param name="selectIndex">
    /// The ONE-BASED select index. Zero and negatives are rejected.
    /// </param>
    /// <param name="ms">
    /// The modify style: <see cref="SQL_MS_REPLACE"/>, <see cref="SQL_MS_APPEND"/> or
    /// <see cref="SQL_MS_PREPEND"/>. Not validated, matching the legacy.
    /// </param>
    /// <param name="clause">
    /// The clause body without its leading keyword. Empty is rejected;
    /// <see langword="null"/> is treated as empty and therefore rejected too.
    /// </param>
    /// <returns>
    /// <c>RetCode.OK</c> on success [<c>:L300</c>], or <c>RetCode.E_INVALID_ARGUMENT</c> when the
    /// guard at <c>:L288</c> rejects the arguments.
    /// </returns>
    /// <remarks>
    /// The legacy <c>of_setorderbyclause</c> is line-for-line identical to
    /// <c>of_setwhereclause</c> apart from the array it addresses, which is why both delegate to one
    /// shared helper here instead of carrying two copies of the same body. Upserts into
    /// <see cref="OrderByClauses"/>, leaving <see cref="WhereClauses"/> untouched.
    /// </remarks>
    public long SetOrderByClause(int selectIndex, long ms, string? clause)
    {
        return UpsertClause(_orderByClauses, selectIndex, ms, clause);
    }

    // ==========================================================================================
    //  THE ONE-BASED UPSERT - n_cst_thread_task_sqlquery.sru:L271-L283 and :L288-L300
    //  ------------------------------------------------------------------------------------------
    //  This is the single most hazardous translation in this file, so the legacy body is transcribed
    //  in full and the hazard is spelled out rather than left to be rediscovered.
    //
    //      if selectIndex <= 0 or clause = "" then return RetCode.E_INVALID_ARGUMENT
    //
    //      nCount = UpperBound(_whereClauses)
    //      for nIndex = 1 to nCount
    //          if _whereClauses[nIndex].index = selectIndex then
    //              exit
    //          end if
    //      next
    //      _whereClauses[nIndex].index  = selectIndex        <-- AFTER the loop has finished
    //      _whereClauses[nIndex].ms     = ms
    //      _whereClauses[nIndex].clause = clause
    //
    //      return RetCode.OK
    //
    //  WHY THAT WORKS IN POWERSCRIPT. PowerScript does not scope a `for` counter to its loop, so the
    //  three assignments read a variable the loop left behind, and its value encodes which of three
    //  things should happen:
    //
    //      no entry matched     nIndex is nCount + 1, one past the end -> the assignment APPENDS,
    //                           because assigning past the upper bound grows a PowerScript array
    //      an entry matched     nIndex is that entry's position       -> the assignment UPDATES IN PLACE
    //      the array was empty  nCount is 0, the body never runs, nIndex stays 1 -> the assignment
    //                           INSERTS THE FIRST ELEMENT
    //
    //  The empty-array branch is genuinely reachable rather than theoretical: _of_reset [:L247-L267]
    //  clears both arrays by assigning a freshly declared empty SQLCLAUSE emptyClauses[] [:L248],
    //  and Reset below reproduces that, so the very next set after a reset takes this branch.
    //
    //  THE OBSERVABLE SEMANTICS TO REPRODUCE, stated without reference to any loop counter: an
    //  UPSERT KEYED ON THE SELECT INDEX THAT PRESERVES INSERTION ORDER - update in place on a
    //  match, append at the end on no match.
    //
    //  THE TRAP, WRITTEN OUT SO IT CANNOT BE WALKED INTO AGAIN. A C# `for` DOES scope its counter,
    //  so the idiomatic-looking transliteration is:
    //
    //      int idx = 0;                                              // zero-based "not found"
    //      for (int i = 0; i < list.Count; i++) { if (match) { idx = i; break; } }
    //      list[idx] = spec;                                         // BUG
    //
    //  On no match idx stays 0, and 0 in a zero-based list is THE FIRST ELEMENT - so that code
    //  silently OVERWRITES entry one where the legacy would have appended. The PowerScript no-match
    //  value means append; the C# no-match value means overwrite the first. Those are opposite
    //  behaviours, and the difference does not show up in a row-count assertion, because both
    //  versions leave a list of length one after the second call with a fresh index. It shows up as
    //  a silently missing WHERE clause on a compound SELECT, at run time, in generated SQL.
    //
    //  THE FIX USED HERE: an explicit not-found sentinel of -1 and an explicit two-way branch. No
    //  loop counter is read after its loop, and 0 is not used as a not-found marker anywhere in this
    //  file. See NotFound.
    //
    //  AND THE TWO INDEX SPACES ARE KEPT APART. The select index is a ONE-BASED KEY that arrives
    //  from the caller - over the wire, on the query contract's clause setters. The list position is
    //  an INTERNAL ZERO-BASED ORDINAL. Nothing below converts one into the other: there is no
    //  `selectIndex - 1`, and no position is ever stored as an index. Conflating them would be a
    //  second defect of the same family as the trap above.
    // ==========================================================================================

    /// <summary>
    /// Validates the arguments and upserts one clause modification into the supplied collection,
    /// keyed on the select index and preserving insertion order.
    /// </summary>
    /// <param name="clauses">
    /// The collection to upsert into: either the <c>WHERE</c> or the <c>ORDER BY</c> collection. The
    /// legacy setters are line-for-line identical apart from which array they address, so both call
    /// this one helper.
    /// </param>
    /// <param name="selectIndex">The one-based select index acting as the upsert key.</param>
    /// <param name="ms">The modify style, stored without validation.</param>
    /// <param name="clause">The raw clause text.</param>
    /// <returns>
    /// <c>RetCode.OK</c> when the entry was stored, or <c>RetCode.E_INVALID_ARGUMENT</c> when the
    /// guard rejected the arguments, in which case the collection is left completely untouched.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>The guard, reproduced exactly.</b> The legacy test is
    /// <c>if selectIndex &lt;= 0 or clause = "" then return RetCode.E_INVALID_ARGUMENT</c> at both
    /// <c>:L271</c> and <c>:L288</c>: two conditions joined by OR, evaluated before any mutation.
    /// Both halves are preserved, including the fact that <b>a non-positive index and an empty
    /// clause produce the same single return code</b> - the legacy does not distinguish them and
    /// neither does this.
    /// </para>
    /// <para>
    /// <b>"Empty" means exactly the empty string, so whitespace-only text is ACCEPTED.</b> The
    /// legacy compares against <c>""</c>, nothing more, so a clause of three spaces is stored and
    /// reported as success. <see cref="string.IsNullOrEmpty(string?)"/> is precisely that test plus
    /// null tolerance; <c>IsNullOrWhiteSpace</c> would reject input the legacy accepts, which is a
    /// behavioural change and not a hardening. The acceptance of whitespace-only text must be pinned
    /// by an explicit test in this project's suite, so that anyone who "tightens" this guard is met
    /// with a failure rather than a silently narrowed contract.
    /// </para>
    /// <para>
    /// <b>Null is folded into the same rejection.</b> PowerScript strings are never null, so no
    /// legacy behaviour exists for it; treating null as empty routes it to the guard the legacy
    /// already has, which narrows nothing and invents nothing. The parameter is declared nullable so
    /// that the tolerance is visible in the signature rather than being a surprise.
    /// </para>
    /// <para>
    /// <b>The index scan runs forward, and that direction is not incidental.</b> It reproduces
    /// <c>for nIndex = 1 to nCount</c>, so on the pathological input of two entries carrying the
    /// same select index - which this method can never itself produce - the FIRST is the one
    /// updated, exactly as the legacy <c>exit</c> would have left it.
    /// </para>
    /// </remarks>
    private static long UpsertClause(List<SqlClause> clauses, int selectIndex, long ms, string? clause)
    {
        // The guard of :L271 and :L288, before any mutation. Note the OR: either half alone is
        // enough to reject, and both together still yield this one code.
        if (selectIndex <= 0 || string.IsNullOrEmpty(clause))
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        // THE STRUCTURAL GUARD, which the legacy does not have and which this boundary must. It runs
        // AFTER the legacy guard so that the legacy's own two rejections keep their exact reachability,
        // and BEFORE any mutation so that a refused clause leaves the collection untouched - the same
        // ordering, and the same return code, as the guard above.
        if (!ValidateClauseBody(clause))
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        // Locate the existing entry for this select index, if there is one. `position` is a
        // ZERO-BASED LIST ORDINAL and `selectIndex` is a ONE-BASED KEY; they are never converted
        // into one another. The sentinel is -1 because 0 is a real position - see NotFound.
        int position = NotFound;

        for (int candidate = 0; candidate < clauses.Count; candidate++)
        {
            if (clauses[candidate].SelectIndex == selectIndex)
            {
                position = candidate;
                break;
            }
        }

        SqlClause specification = new(selectIndex, ms, clause);

        if (position >= 0)
        {
            // Matched: update in place. The count does not change and the entry keeps its position,
            // which is what makes repeated sets last-wins rather than accumulative.
            clauses[position] = specification;
        }
        else
        {
            // No match: APPEND at the end. This is the branch the legacy reaches by falling off the
            // end of its loop with the counter one past the upper bound, and it is deliberate legacy
            // semantics rather than a convenience - see the banner above. The same branch also
            // handles the empty collection, which the legacy reaches with its counter left at 1.
            clauses.Add(specification);
        }

        return RetCode.OK;
    }

    // ==========================================================================================
    //  THE STRUCTURAL CLAUSE GUARD
    //  ------------------------------------------------------------------------------------------
    //  A SCANNER RATHER THAN A REGULAR EXPRESSION OR A SUBSTRING SEARCH, and the reason is that the
    //  three things worth refusing all hide inside the two things that must still be accepted. A
    //  bare `Contains(";")` refuses `WHERE note = 'a;b'`, which is an ordinary predicate over an
    //  ordinary literal; a bare `Contains("union")` refuses a column named `communion`. So the text
    //  is walked ONCE, left to right, tracking exactly three pieces of state - whether the cursor is
    //  inside a string literal, inside a quoted identifier, and how deep the parentheses are - and
    //  each rule is applied only where it means what it says.
    //
    //  WHAT IS REFUSED, matching persistence.v1.SqlClauseSpec.clause clause for clause:
    //    1. a control character, INCLUDING a newline or a tab. A newline is how `--` truncates the
    //       rest of a statement and how a property assignment is smuggled into a modification script
    //       elsewhere in this service, and no legitimate WHERE or ORDER BY body needs one.
    //    2. a statement terminator `;` outside a literal - the multi-statement vector.
    //    3. a comment introducer `--`, `/*` or `*/` outside a literal - each can comment out the
    //       remainder of the generated statement, which changes what executes without changing what
    //       is visible at the top of it.
    //    4. an unterminated string literal or quoted identifier, and an unbalanced parenthesis or
    //       bracket. Each desynchronises every later scan, so refusing them is what makes rules 2, 3
    //       and 5 sound rather than best-effort.
    //    5. any refused BARE WORD - a nested-statement or set-operator introducer, or any DDL, DML or
    //       permission verb. Bare means: not inside a string literal and not inside a quoted
    //       identifier, and delimited on both sides by a non-word character, so `communion` and
    //       `updated_at` are untouched while `UNION` and `UPDATE` are not.
    //
    //  WHAT IS STILL ACCEPTED, because the refusal is a narrowing and not a rewrite: every ordinary
    //  predicate and ordering expression - comparisons, AND/OR/NOT, IN over a literal list, BETWEEN,
    //  LIKE, IS NULL, qualified and quoted column names, string and numeric literals, ASC/DESC,
    //  function calls over columns, positional `?` parameters - and, faithfully, SPACE-ONLY TEXT,
    //  because the legacy guard compares against `""` and nothing more (C-B).
    //
    //  THE SCANNER IS ORDINAL AND CULTURE-INDEPENDENT throughout. A culture-sensitive comparison
    //  would make the refused-word set depend on the server's locale, and the Turkish dotless-i pair
    //  alone is enough to make `INSERT` and `insert` compare unequal under some cultures - a
    //  locale-dependent security guard is not a security guard.
    // ==========================================================================================

    /// <summary>
    /// The bare words a clause body may not contain: nested-statement and set-operator introducers,
    /// and every DDL, DML and permission verb.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>SELECT</c> and <c>FROM</c> are in the set, which is what refuses a subquery.</b> A
    /// correlated subquery is the single most expressive vector available through this field - it can
    /// read any table the connection can reach - and neither word has any legitimate place in a clause
    /// BODY, because the body is spliced in after the keyword the parser already owns.
    /// </para>
    /// <para>
    /// <b>Ordinal-ignore-case, and the comparer is stated rather than defaulted.</b>
    /// <see cref="StringComparer.OrdinalIgnoreCase"/> is locale-invariant; the default comparer for a
    /// <see cref="HashSet{T}"/> of strings is ordinal but case-SENSITIVE, which would let <c>DrOp</c>
    /// through, and a culture-aware comparer would make the guard's behaviour depend on the server's
    /// locale.
    /// </para>
    /// <para>
    /// <b>The two procedure prefixes are handled separately</b>, in
    /// <see cref="IsRefusedWord(ReadOnlySpan{char})"/>, because <c>xp_</c> and <c>sp_</c> name whole
    /// families - <c>xp_cmdshell</c>, <c>sp_executesql</c> and every sibling - rather than single
    /// words, so no finite set can enumerate them.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> RefusedClauseWords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Nested statements and subqueries.
            "SELECT",
            "FROM",

            // Set operators, including Oracle's spelling of EXCEPT.
            "UNION",
            "INTERSECT",
            "EXCEPT",
            "MINUS",

            // DML.
            "INSERT",
            "UPDATE",
            "DELETE",
            "MERGE",
            "UPSERT",
            "INTO",

            // DDL.
            "CREATE",
            "ALTER",
            "DROP",
            "TRUNCATE",
            "RENAME",
            "REINDEX",
            "VACUUM",
            "ANALYZE",

            // Permissions.
            "GRANT",
            "REVOKE",
            "DENY",

            // Procedure and batch execution.
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

            // SQLite statement verbs reachable through a spliced clause.
            "ATTACH",
            "DETACH",
            "PRAGMA",

            // Transaction control, which would let a caller commit or abandon the task's own work.
            "BEGIN",
            "COMMIT",
            "ROLLBACK",
            "SAVEPOINT",
        };

    /// <summary>
    /// The two stored-procedure name prefixes that name whole families rather than single words.
    /// </summary>
    private static readonly string[] RefusedWordPrefixes = ["xp_", "sp_"];

    /// <summary>
    /// The SQL string-literal delimiter, named so the scanner below carries no escaped quote literal.
    /// </summary>
    private const char SingleQuote = '\u0027';

    /// <summary>
    /// Decides whether a clause body may be stored, without altering a single character of it.
    /// </summary>
    /// <param name="clause">
    /// The clause body, already known to be non-empty by the legacy guard that runs first.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the body is an ordinary predicate or ordering expression;
    /// <see langword="false"/> when it carries statement structure, in which case the caller answers
    /// <c>RetCode.E_INVALID_ARGUMENT</c> and stores nothing.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>It answers a decision and never throws.</b> Every caller is a setter that reports a return
    /// code, and the whole point of the guard is to convert a hostile input into that code rather than
    /// into an exception a gRPC handler would surface as a 500.
    /// </para>
    /// <para>
    /// <b>The single left-to-right pass is what makes the rules sound.</b> See the region banner for
    /// why each rule is applied only in the state where it means what it says, and why the balance
    /// checks are a precondition of the rest rather than a nicety.
    /// </para>
    /// </remarks>
    private static bool ValidateClauseBody(string clause)
    {
        int parenthesisDepth = 0;
        int wordStart = NotFound;

        for (int index = 0; index < clause.Length; index++)
        {
            char current = clause[index];

            // RULE 1, applied before anything else so that no later rule has to reason about a
            // control character. SPACE is the one whitespace this grammar admits; a tab, a newline
            // and a carriage return are refused along with every other control character, and so are
            // the C1 range and the delete character.
            if (char.IsControl(current))
            {
                return false;
            }

            // A word ends at the first non-word character, and it is tested THERE rather than at the
            // next word's start, so that a refused word at the very end of the clause is still tested
            // - see the flush after the loop for the other half of that.
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
                if (IsRefusedWord(clause.AsSpan(wordStart, index - wordStart)))
                {
                    return false;
                }

                wordStart = NotFound;
            }

            switch (current)
            {
                case SingleQuote:
                    // A string literal. Its CONTENT is exempt from every other rule, which is the
                    // whole reason the scan tracks state rather than searching for substrings: a
                    // semicolon, a comment introducer or the word UNION inside a literal is data.
                    if (!TrySkipQuoted(clause, ref index, SingleQuote))
                    {
                        return false;
                    }

                    break;

                case '"':
                    // A quoted identifier, doubled-quote escaped, exempt for the same reason.
                    if (!TrySkipQuoted(clause, ref index, '"'))
                    {
                        return false;
                    }

                    break;

                case '`':
                    // MySQL-style quoting. Admitted as a delimiter so that its content is exempt and
                    // its termination is checked, rather than left to be scanned as bare text.
                    if (!TrySkipQuoted(clause, ref index, '`'))
                    {
                        return false;
                    }

                    break;

                case '[':
                    // A BRACKETED IDENTIFIER IS A QUOTING CONTEXT, NOT A NESTING DEPTH, and the
                    // distinction is what lets `[union] = 1` through. T-SQL delimits an identifier
                    // with brackets and escapes a literal close bracket by DOUBLING it; brackets do
                    // not nest in an identifier at all, so counting depth would both mis-handle
                    // `[a]]b]` and leave the identifier's content exposed to the refused-word rule.
                    if (!TrySkipBracketed(clause, ref index))
                    {
                        return false;
                    }

                    break;

                case ']':
                    // A close with no open. A legitimate one is always consumed by the skip above, so
                    // reaching here means the text is desynchronised.
                    return false;

                case '(':
                    parenthesisDepth++;
                    break;

                case ')':
                    parenthesisDepth--;

                    if (parenthesisDepth < 0)
                    {
                        // A close ahead of its open. Refused on sight rather than at the end,
                        // because from here on the depth no longer describes the text.
                        return false;
                    }

                    break;

                case ';':
                    // RULE 2.
                    return false;

                case '-':
                    // RULE 3. A single minus is subtraction and is fine; two adjacent ones start a
                    // line comment.
                    if (index + 1 < clause.Length && clause[index + 1] == '-')
                    {
                        return false;
                    }

                    break;

                case '/':
                    // RULE 3. A single solidus is division.
                    if (index + 1 < clause.Length && clause[index + 1] == '*')
                    {
                        return false;
                    }

                    break;

                case '*':
                    // RULE 3, the closing half. A lone `*/` cannot open a comment, but it CAN close
                    // one the caller opened in an earlier clause on the same statement, so it is
                    // refused symmetrically rather than only when it follows a `/*` this scan saw.
                    if (index + 1 < clause.Length && clause[index + 1] == '/')
                    {
                        return false;
                    }

                    break;

                default:
                    // Every other printable character is an operator, a separator or punctuation, all
                    // of which ordinary predicates need. Enumerating an allow-list of them would
                    // refuse dialect operators the legacy accepts without refusing any vector that
                    // rules 1 to 5 do not already cover.
                    break;
            }
        }

        // The trailing word, for a clause that ends on one - `ORDER BY name` and `x = 1 OR DROP`
        // both end without a delimiter, and only one of them may be stored.
        if (wordStart != NotFound && IsRefusedWord(clause.AsSpan(wordStart)))
        {
            return false;
        }

        // RULE 4's balance half, tested at the end because that is the only place it is knowable.
        // Brackets need no counterpart here: every legitimate bracketed identifier is consumed whole
        // by TrySkipBracketed, and a stray close is refused on sight.
        return parenthesisDepth == 0;
    }

    /// <summary>
    /// Whether a character belongs to a bare word for the purposes of the refused-word test.
    /// </summary>
    /// <param name="candidate">The character to classify.</param>
    /// <remarks>
    /// The underscore and the digits are INCLUDED, and that inclusion is what protects ordinary
    /// column names: without it, <c>updated_at</c> would be scanned as the two words <c>updated</c>
    /// and <c>at</c>, and <c>sp_</c> would never be seen as a prefix of anything. Letters are
    /// classified by <see cref="char.IsLetter(char)"/> rather than by an ASCII range so that a
    /// non-Latin identifier is scanned as one word instead of as a run of delimiters.
    /// </remarks>
    private static bool IsWordCharacter(char candidate) =>
        char.IsLetterOrDigit(candidate) || candidate == '_';

    /// <summary>
    /// Whether a bare word is refused, by exact membership or by procedure-family prefix.
    /// </summary>
    /// <param name="word">The word, delimited on both sides by non-word characters.</param>
    private static bool IsRefusedWord(ReadOnlySpan<char> word)
    {
        foreach (string prefix in RefusedWordPrefixes)
        {
            if (word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return RefusedClauseWords.Contains(word.ToString());
    }

    /// <summary>
    /// Advances <paramref name="index"/> past a delimited run, honouring the doubled-delimiter escape.
    /// </summary>
    /// <param name="clause">The clause being scanned.</param>
    /// <param name="index">
    /// On entry, the position of the OPENING delimiter. On a successful return, the position of the
    /// closing one, so that the caller's own increment resumes after it.
    /// </param>
    /// <param name="delimiter">The delimiter character.</param>
    /// <returns><see langword="false"/> when the run is never closed.</returns>
    /// <remarks>
    /// <b>The doubled-delimiter escape is honoured, which is not optional.</b> <c>'it''s'</c> is one
    /// literal containing an apostrophe, and a scanner that stopped at the second quote would resume
    /// scanning <c>s'</c> as bare text - and then report an unterminated literal for a perfectly
    /// ordinary predicate. A control character inside the run is still refused by the caller's rule 1
    /// on the next iteration only if the run ends, so it is tested HERE as well: a literal is exempt
    /// from the SQL rules, not from the character-set rule.
    /// </remarks>
    private static bool TrySkipQuoted(string clause, ref int index, char delimiter)
    {
        for (int cursor = index + 1; cursor < clause.Length; cursor++)
        {
            char current = clause[cursor];

            if (char.IsControl(current))
            {
                return false;
            }

            if (current != delimiter)
            {
                continue;
            }

            if (cursor + 1 < clause.Length && clause[cursor + 1] == delimiter)
            {
                // A doubled delimiter is an escaped one: consume both and keep going.
                cursor++;

                continue;
            }

            index = cursor;

            return true;
        }

        // Fell off the end still inside the run.
        return false;
    }

    /// <summary>
    /// Advances <paramref name="index"/> past a bracket-delimited identifier, honouring the
    /// doubled-close escape.
    /// </summary>
    /// <param name="clause">The clause being scanned.</param>
    /// <param name="index">
    /// On entry, the position of the opening <c>[</c>. On a successful return, the position of the
    /// closing <c>]</c>, so that the caller's own increment resumes after it.
    /// </param>
    /// <returns><see langword="false"/> when the identifier is never closed.</returns>
    /// <remarks>
    /// SEPARATE FROM <see cref="TrySkipQuoted(string, ref int, char)"/> because the opener and the
    /// closer are DIFFERENT characters, which is the one delimiter form in SQL where that is true. The
    /// escape rule is the same in spirit and different in shape: <c>[a]]b]</c> is the single identifier
    /// <c>a]b</c>, so a doubled CLOSE is consumed rather than treated as the end.
    /// </remarks>
    private static bool TrySkipBracketed(string clause, ref int index)
    {
        for (int cursor = index + 1; cursor < clause.Length; cursor++)
        {
            char current = clause[cursor];

            if (char.IsControl(current))
            {
                return false;
            }

            if (current != ']')
            {
                continue;
            }

            if (cursor + 1 < clause.Length && clause[cursor + 1] == ']')
            {
                cursor++;

                continue;
            }

            index = cursor;

            return true;
        }

        return false;
    }

    // ==========================================================================================
    //  Reset - n_cst_thread_task_sqlquery.sru:L247-L267
    // ==========================================================================================

    /// <summary>
    /// Discards every pending clause modification in BOTH collections, returning this instance to
    /// its freshly-constructed state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reproduces the clause-clearing half of <c>_of_reset</c>, which declares a fresh empty
    /// <c>SQLCLAUSE emptyClauses[]</c> at <c>:L248</c> and assigns it over both arrays at
    /// <c>:L264-L265</c>. Assigning an empty array is a complete discard, not a truncation, so both
    /// collections are cleared outright and neither retains capacity semantics that a caller could
    /// observe.
    /// </para>
    /// <para>
    /// <b>Both collections, always.</b> The legacy clears them together in one subroutine and offers
    /// no way to clear one alone, so neither does this. After a reset the next set lands at position
    /// one, which is the empty-collection branch of
    /// <see cref="UpsertClause(List{SqlClause}, int, long, string?)"/> and the reason that branch is
    /// reachable at all.
    /// </para>
    /// <para>
    /// The rest of <c>_of_reset</c> - the hook class, the statement text, the chunk size, the paging
    /// settings, the cache flag and the paged unique-index columns - belongs to the query task that
    /// owns those fields, and stays in <c>Tasks/</c>.
    /// </para>
    /// </remarks>
    public void Reset()
    {
        _whereClauses.Clear();
        _orderByClauses.Clear();
    }

    // ==========================================================================================
    //  The wire-to-legacy style mapping
    //  ------------------------------------------------------------------------------------------
    //  ONE member, deliberately. It is the only place in this service that converts between the
    //  generated SqlModifyStyle and the legacy numeric style, so the agreement between the published
    //  enum and the shared constant catalogue is asserted in exactly one location and can be tested
    //  in exactly one location.
    // ==========================================================================================

    /// <summary>
    /// Converts a published <see cref="SqlModifyStyle"/> into the legacy numeric modify style that
    /// <see cref="SetWhereClause(int, long, string?)"/> and its siblings take.
    /// </summary>
    /// <param name="wireStyle">The style as it arrived on the query contract.</param>
    /// <returns>
    /// <see cref="SQL_MS_REPLACE"/>, <see cref="SQL_MS_APPEND"/> or <see cref="SQL_MS_PREPEND"/> for
    /// the three legal values; otherwise the raw numeric value unchanged, which is a style the
    /// statement model rejects.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>The three legal values are enumerated rather than cast, and that is the point of the
    /// member.</b> The published enum happens to carry the legacy numbering today, so a bare cast
    /// would compile and appear to work - which is exactly why it is not used. Naming each value and
    /// returning the corresponding shared constant makes this method the single place where the two
    /// sides are reconciled, so a future renumbering on either side is caught by a test over this one
    /// member instead of silently producing a wrong statement. Nothing else in the repository asserts
    /// that the published enum and the shared constant catalogue agree numerically, which is why the
    /// reconciliation is written out here rather than delegated to a cast.
    /// </para>
    /// <para>
    /// <b><c>SQL_MS_UNSPECIFIED</c> maps to a rejected value and NEVER to
    /// <see cref="SQL_MS_REPLACE"/>.</b> It is a proto3 mechanical artifact rather than a fourth
    /// legacy style: the legacy set has no zero at all, while proto3 requires a zero first
    /// enumerator. Defaulting it to replace would widen the contract with a guess, so it is instead
    /// passed through as <c>0</c>, which is none of the three styles the statement model's modify
    /// path recognises and therefore lands on that path's default arm, changing nothing and
    /// reporting failure. The legacy offers no behaviour to copy here, because it never validates
    /// its style argument at all - which is precisely why the handling has to be defined rather than
    /// inherited.
    /// </para>
    /// <para>
    /// <b>An unrecognised number is passed through too, for the same reason.</b> A proto3 enum field
    /// is OPEN, not closed: an unknown numeric value is neither rejected by the parser nor folded to
    /// zero, so a request can legitimately arrive carrying <c>99</c>. Preserving that number rather
    /// than collapsing it to a sentinel keeps it visible to the caller's own validation and to
    /// diagnostics, and it cannot be mistaken for a legal style since the three legal styles are
    /// enumerated above it.
    /// </para>
    /// <para>
    /// <b>This method does not itself reject anything.</b> Refusing an out-of-domain style with
    /// <c>E_INVALID_ARGUMENT</c> and the matching gRPC status, before any clause text is applied, is
    /// the service boundary's duty in <c>Grpc/QueryService.cs</c>; a caller compares the result
    /// against the three constants published on this class. Splitting it that way keeps the
    /// conversion total - it has no failure mode of its own - and keeps the status mapping with the
    /// code that owns the transport.
    /// </para>
    /// </remarks>
    public static long ToModifyStyle(SqlModifyStyle wireStyle)
    {
        switch (wireStyle)
        {
            case SqlModifyStyle.SqlMsReplace:
                return SQL_MS_REPLACE;

            case SqlModifyStyle.SqlMsAppend:
                return SQL_MS_APPEND;

            case SqlModifyStyle.SqlMsPrepend:
                return SQL_MS_PREPEND;

            case SqlModifyStyle.SqlMsUnspecified:
            default:
                // Pass the number through unchanged. For SqlMsUnspecified that is 0, and for an
                // unrecognised open-enum value it is whatever arrived. Neither is one of the three
                // legal styles, so both are refused downstream rather than laundered into one.
                return (long)wireStyle;
        }
    }
}
