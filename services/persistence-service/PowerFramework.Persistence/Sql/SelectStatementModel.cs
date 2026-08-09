// ==============================================================================================
//  SelectStatementModel - the managed SQL SELECT statement model
//  --------------------------------------------------------------------------------------------
//  REIMPLEMENTATION OF  ws_objects/pfw.utility.parser.pbl.src/n_sql.sru (60 lines)
//                       ws_objects/pfw.utility.parser.pbl.src/parsesql.srf (16 lines)
//  ORACLE STATUS        Those two .srf/.sru exports plus their single measured consumer,
//                       ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru, are the
//                       ONLY specification for this type, and all of them are READ ONLY. They are
//                       the behavioural oracle for parity testing, never an edit target. Every
//                       member and every behavioural decision below cites the legacy line it came
//                       from, so the two can be diffed against each other by a later reader.
//
//  WHY THIS IS A REIMPLEMENTATION RATHER THAN A BINDING OR A SUBSTITUTION
//  --------------------------------------------------------------------------------------------
//  n_sql.sru:L8 declares
//
//      global type n_sql from nonvisualobject native "pfw.dll"
//
//  and the file contains ZERO function or subroutine bodies: the 41 prototypes at L9-L49 are the
//  whole of it. Every line of behaviour lives inside the closed-source pfw.dll, for which no C++
//  source exists anywhere in this repository, so there is no PowerScript to transliterate and no
//  binary to bind to on Linux. The native-substitution matrix classifies this one object as
//  REIMPLEMENT IN REPO rather than SUBSTITUTE, and it is the only in-scope object with that
//  classification.
//
//  BUILD-NOT-BUY: Microsoft.SqlServer.TransactSql.ScriptDom WAS EVALUATED AND REJECTED
//  --------------------------------------------------------------------------------------------
//  The obvious purchase is a general-purpose T-SQL parser. It was rejected for two independent
//  reasons, either of which is sufficient:
//
//    1. ScriptDom is SQL-Server-dialect-specific. Persistence needs the SAME clause model to
//       serve BOTH paging rewriters, and the Oracle rewriter emits a triple-nested ROW_NUMBER
//       form (n_cst_thread_task_sqlquery.sru:L392-L395) that a T-SQL-only parser cannot round
//       trip. One model that serves one of two required dialects is not a model.
//    2. The acceptance criterion here is byte-exact parity with n_sql's CLAUSE-LEVEL STRING
//       output, including the sentinel identifiers the rewriters splice in - pfwPagedSQL_RN,
//       pfwPagedSQL_OutterTbl, pfwPagedSQL_Tbl, pfwPagedSQL_TblInner, pfwPagedSQL_TblInnerInner,
//       pfwPagedSQL_TblOuter - and the count-wrapper alias "1 AS _" at :L830. A parser that
//       normalises a statement into an abstract syntax tree and regenerates it cannot make that
//       guarantee; a focused six-clause model that retains the input's own text can, and is far
//       easier to verify. No SQL parser exists in the base class library either.
//
//  NO NUGET PACKAGE IS ADDED BY THIS FILE, and that is enforced by the build rather than merely
//  asserted here: central package management is switched on at the repository root, every
//  PackageReference in the repository is versionless, and Directory.Packages.props contains no
//  ScriptDom, no SQL Server client and no Oracle client, so a package reference added here would
//  fail restore with a missing PackageVersion. The only using directives below are System.Text,
//  for StringBuilder, and PowerFramework.Shared.Kernel, for the three modify-style constants.
//
//  THE WHOLE OF THE LEGACY SURFACE, TRANSCRIBED IN DECLARATION ORDER
//  --------------------------------------------------------------------------------------------
//  Transcribed from n_sql.sru so the surface below can be diffed against the source top to
//  bottom. Every member in this file is either one of these prototypes or the one annotated
//  addition; there is no third category.
//
//      L9   string  copyright()                                  NOT PORTED  see below
//      L10  string  getversion()                                 NOT PORTED  see below
//      L11  boolean parse(readonly string sql)                   Parse
//      L12  string  getsql()                                     GetSql
//      L13  int     getselectcount()                             GetSelectCount
//      L14  boolean hascolumn()                                  HasColumn()
//      L15  boolean hascolumn(readonly int nselectindex)          HasColumn(int)
//      L16  boolean hastable()                                   HasTable()
//      L17  boolean hastable(readonly int nselectindex)           HasTable(int)
//      L18  boolean haswhere()                                   HasWhere()
//      L19  boolean haswhere(readonly int nselectindex)           HasWhere(int)
//      L20  boolean hasgroup()                                   HasGroup()
//      L21  boolean hasgroup(readonly int nselectindex)           HasGroup(int)
//      L22  boolean hashaving()                                  HasHaving()
//      L23  boolean hashaving(readonly int nselectindex)          HasHaving(int)
//      L24  boolean hasorder()                                   HasOrder()
//      L25  boolean hasorder(readonly int nselectindex)           HasOrder(int)
//      L26  string  getcolumn()                                  GetColumn()
//      L27  string  getcolumn(readonly int nselectindex)          GetColumn(int)
//      L28  string  gettable()                                   GetTable()
//      L29  string  gettable(readonly int nselectindex)           GetTable(int)
//      L30  string  getwhere()                                   GetWhere()
//      L31  string  getwhere(readonly int nselectindex)           GetWhere(int)
//      L32  string  getgroup()                                   GetGroup()
//      L33  string  getgroup(readonly int nselectindex)           GetGroup(int)
//      L34  string  gethaving()                                  GetHaving()
//      L35  string  gethaving(readonly int nselectindex)          GetHaving(int)
//      L36  string  getorder()                                   GetOrder()
//      L37  string  getorder(readonly int nselectindex)           GetOrder(int)
//      L38  boolean modifycolumn(ms, newcolumn)                   ModifyColumn(long, string)
//      L39  boolean modifycolumn(nselectindex, ms, newcolumn)     ModifyColumn(int, long, string)
//      L40  boolean modifytable(ms, newtable)                     ModifyTable(long, string)
//      L41  boolean modifytable(nselectindex, ms, newtable)       ModifyTable(int, long, string)
//      L42  boolean modifywhere(ms, newwhere)                     ModifyWhere(long, string)
//      L43  boolean modifywhere(nselectindex, ms, newwhere)       ModifyWhere(int, long, string)
//      L44  boolean modifygroup(ms, newgroup)                     ModifyGroup(long, string)
//      L45  boolean modifygroup(nselectindex, ms, newgroup)       ModifyGroup(int, long, string)
//      L46  boolean modifyhaving(ms, newhaving)                   ModifyHaving(long, string)
//      L47  boolean modifyhaving(nselectindex, ms, newhaving)     ModifyHaving(int, long, string)
//      L48  boolean modifyorder(ms, neworder)                     ModifyOrder(long, string)
//      L49  boolean modifyorder(nselectindex, ms, neworder)       ModifyOrder(int, long, string)
//
//      parsesql.srf:L10-L14  n_sql parsesql(readonly string sql)  ParseSql (static factory)
//
//  The arithmetic, stated so nobody miscounts it later: 41 prototypes are declared, 2 are not
//  ported, so 39 legacy members ship, plus the one static factory that reproduces parsesql.srf.
//
//  THE TWO MEMBERS DELIBERATELY NOT PORTED
//  --------------------------------------------------------------------------------------------
//  copyright() at L9 and getversion() at L10 are framework-metadata boilerplate present on every
//  PBNI class in this estate; they report the vendor string and the pfw.dll build number and are
//  not part of the functional contract of a SQL statement model. Porting them would fabricate a
//  version number for an assembly whose version already comes from Directory.Build.props.
//
//  DEFERRED-SERVICE ISOLATION, WHICH IS THE WHOLE REASON THIS FILE EXISTS
//  --------------------------------------------------------------------------------------------
//  The legacy library pfw.utility.parser holds 13 objects. Exactly TWO of them - n_sql.sru and
//  parsesql.srf - are in scope for Persistence. The remaining 11 are the JSON and XML object
//  family, and they are assigned to a deferred capability area that receives no code, no test and
//  no container in this phase. Reimplementing the statement model in this repository, instead of
//  reaching for anything that library also offers, is precisely what keeps Persistence free of
//  any coupling to that deferred area. Nothing below references, imports or borrows from any of
//  those 11 objects, in code or in comment.
//
//  WHAT THIS FILE IS NOT
//  --------------------------------------------------------------------------------------------
//  It is a string-in / string-out model and nothing else. It opens no connection, touches no
//  database, references no DBMS client, performs no I/O, reads no clock, consumes no randomness
//  and writes no log. It holds no static mutable state, no singleton and no cached parser, so
//  instances are independent and a fresh one per operation is the intended usage - matching the
//  legacy, which creates a new n_sql at n_cst_thread_task_sqlquery.sru:L312 and again at :L531
//  and destroys it at :L401 and :L874. The legacy also declares a global auto-instance shadowing
//  its own type name at n_sql.sru:L51; the collision is resolved by giving the TYPE this
//  descriptive .NET name and making the INSTANCE an injected dependency, so no global exists here.
//
//  NAMING: NO SCREAMING_SNAKE IDENTIFIER IS DECLARED IN THIS FILE
//  --------------------------------------------------------------------------------------------
//  The repository-root .editorconfig switches CA1707 and IDE1006 off in the individually named files
//  on its BAND 3 roster - the single source of truth for that list, cited here rather than recounted
//  - each of which carries preserved legacy constant spellings. This file is deliberately NOT one
//  of them, and TreatWarningsAsErrors is on repository-wide, so an underscore-bearing identifier
//  declared here would be a compile error rather than a style note. The three modify-style
//  constants are therefore CONSUMED from PowerFramework.Shared.Kernel.Enums, where they are
//  declared inside the .editorconfig's suppression scope, and none is redeclared here.
// ==============================================================================================

using System.Text;

using PowerFramework.Shared.Kernel;

namespace PowerFramework.Persistence.Sql;

/// <summary>
/// Managed model of a SQL <c>SELECT</c> statement, reproducing the clause-level surface of the
/// closed-source PBNI class <c>n_sql</c> declared at
/// <c>ws_objects/pfw.utility.parser.pbl.src/n_sql.sru:L8</c>.
/// </summary>
/// <remarks>
/// <para>
/// The model recognises the six clause kinds the legacy surface exposes - <c>Column</c>,
/// <c>Table</c>, <c>Where</c>, <c>Group</c>, <c>Having</c> and <c>Order</c> - and offers, for
/// each, a presence test, a text accessor and a three-style modifier, in both the short arity and
/// the select-index arity that <c>n_sql.sru:L14-L49</c> declares.
/// </para>
/// <para>
/// <b>Round-trip invariant.</b> For any statement this model parses successfully, calling
/// <see cref="Parse(string)"/> and then <see cref="GetSql()"/> with no intervening modification
/// returns the input <em>unchanged</em>, byte for byte, including its own whitespace, keyword
/// casing, comments and any leading common-table expression. That is achieved structurally rather
/// than by normalising and regenerating: the parse records the exact source geometry of every
/// clause, and an unmodified block re-emits its original text verbatim. It is the strongest parity
/// guarantee available from this repository, because the native's own reassembly lives inside
/// pfw.dll and cannot be observed here.
/// </para>
/// <para>
/// <b>Residual uncertainty, recorded rather than glossed over.</b> Once a clause has actually been
/// modified, the exact keyword casing and inter-clause spacing that the native <c>GetSQL()</c>
/// emits is <em>not</em> determinable from this repository. This implementation minimises the
/// exposure by splicing the new clause text into the statement's original geometry - the
/// introducer keyword, the gap before it and the gap after it are all reused exactly as the input
/// spelled them - and by falling back to an upper-case canonical introducer only for a clause that
/// was absent from the input altogether. Exact native emission for a modified statement is a
/// characterization item to be settled against the behavioural oracle. Nothing here claims a
/// byte-exact match against the native for that case.
/// </para>
/// <para>
/// This type is deliberately <see langword="internal"/>: no consumer outside the
/// <c>PowerFramework.Persistence</c> assembly needs it, and the application project already grants
/// the sibling test project access, so visibility never has to be widened for testability.
/// </para>
/// </remarks>
internal sealed class SelectStatementModel
{
    // ------------------------------------------------------------------------------------------
    //  Clause identity
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The six clause kinds of <c>n_sql</c>, numbered by their canonical position in a
    /// <c>SELECT</c> statement so that ordering comparisons and array indexing are the same thing.
    /// </summary>
    /// <remarks>
    /// The numeric order is load-bearing in two places: the parse rejects a statement whose
    /// depth-zero clause introducers do not appear in strictly increasing order, and emission
    /// walks the kinds in this order to rebuild the statement.
    /// </remarks>
    private enum ClauseKind
    {
        /// <summary>The select list, between <c>SELECT</c> and <c>FROM</c>.</summary>
        Column = 0,

        /// <summary>The whole <c>FROM</c> body, joins included.</summary>
        Table = 1,

        /// <summary>The <c>WHERE</c> body.</summary>
        Where = 2,

        /// <summary>The <c>GROUP BY</c> body.</summary>
        Group = 3,

        /// <summary>The <c>HAVING</c> body.</summary>
        Having = 4,

        /// <summary>The <c>ORDER BY</c> body.</summary>
        Order = 5,
    }

    /// <summary>Number of members of <see cref="ClauseKind"/>.</summary>
    private const int ClauseKindCount = 6;

    /// <summary>
    /// Upper-case canonical introducer per <see cref="ClauseKind"/>, indexed by its numeric value.
    /// </summary>
    /// <remarks>
    /// Used only when emitting a clause that was <em>absent</em> from the parsed input and has
    /// therefore no source geometry to reuse - for example an <c>ORDER BY</c> appended to a
    /// statement that had none. Upper case is chosen because every SQL fragment the legacy paging
    /// routine composes is upper case, from the <c>ROW_NUMBER() OVER (ORDER BY ...)</c> column at
    /// <c>n_cst_thread_task_sqlquery.sru:L355</c> to the <c>INNER JOIN</c> table fragment at
    /// <c>:L350</c>. That is a documented interpretation of an unobservable native detail, not a
    /// measurement.
    /// </remarks>
    private static readonly string[] CanonicalIntroducers =
    [
        "SELECT",
        "FROM",
        "WHERE",
        "GROUP BY",
        "HAVING",
        "ORDER BY",
    ];

    // ------------------------------------------------------------------------------------------
    //  Internal model - one slot per clause, one block per SELECT
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// One clause of one select block, holding both its current text and the exact source geometry
    /// the parse observed for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Presence and text are independent. A clause that is present but empty is a distinct state
    /// from a clause that is absent, which is why <see cref="Present"/> is a field of its own
    /// rather than being inferred from <see cref="Text"/> being empty. Only a degenerate input such
    /// as <c>SELECT a FROM t WHERE</c> produces present-and-empty, but the distinction has to exist
    /// for <c>Has*</c> to answer honestly.
    /// </para>
    /// <para>
    /// <see cref="Introducer"/> doubles as the marker for "this clause has source geometry". It is
    /// non-empty exactly when the clause was found in the parsed input, and it survives the clause
    /// being cleared, so a clear-then-restore round trip - which is literally what the legacy paging
    /// routine does at <c>n_cst_thread_task_sqlquery.sru:L377</c> then <c>:L360</c> - puts the
    /// clause back in the input's own spelling and spacing rather than a canonical approximation.
    /// </para>
    /// </remarks>
    private sealed class ClauseSlot
    {
        /// <summary>
        /// Whether the clause is currently part of the statement. Set by the parse when the
        /// introducer is found, cleared by a replace with empty text, set again by any modification
        /// that supplies text.
        /// </summary>
        public bool Present { get; set; }

        /// <summary>
        /// The clause body with leading and trailing whitespace removed. This is the value
        /// <c>Get*</c> returns and the value <c>Modify*</c> transforms; the surrounding whitespace
        /// lives in <see cref="LeadGap"/> and in the following slot's <see cref="PreGap"/> so that
        /// no part of the input is lost.
        /// </summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// The exact source text between the end of the previous clause's body and the start of
        /// this clause's introducer - whitespace, and any comment that sits there. Empty for the
        /// first clause of a block, because a block's recorded text begins at its <c>SELECT</c>.
        /// </summary>
        public string PreGap { get; set; } = string.Empty;

        /// <summary>
        /// The introducer exactly as the input spelled it, preserving its casing and any internal
        /// whitespace - so a source <c>group  by</c> re-emits as <c>group  by</c>. Empty means the
        /// clause was absent from the input and has no source geometry to reuse.
        /// </summary>
        public string Introducer { get; set; } = string.Empty;

        /// <summary>
        /// The exact source whitespace between the introducer and the first character of the body.
        /// </summary>
        public string LeadGap { get; set; } = string.Empty;

        /// <summary>
        /// Whether this clause carries source geometry captured by the parse, as opposed to having
        /// been introduced by a later modification.
        /// </summary>
        public bool HasSourceGeometry => Introducer.Length > 0;
    }

    /// <summary>
    /// One <c>SELECT</c> block of the statement: the unit that <c>getselectcount()</c>
    /// (<c>n_sql.sru:L13</c>) counts and that the one-based select index of every clause accessor
    /// addresses.
    /// </summary>
    /// <remarks>
    /// <see cref="Prefix"/> plus <see cref="Raw"/> is the exact, contiguous slice of the input this
    /// block occupies, so concatenating those two over every block in order reproduces the input
    /// byte for byte. That is the mechanism behind the round-trip invariant.
    /// </remarks>
    private sealed class SelectBlock
    {
        /// <summary>
        /// Everything in the input between the end of the previous block and this block's
        /// <c>SELECT</c> keyword: for the first block any leading whitespace and any leading
        /// common-table expression, and for a later block the set operator that introduced it
        /// together with the whitespace on both sides of it. Re-emitted verbatim and never parsed.
        /// </summary>
        public string Prefix { get; init; } = string.Empty;

        /// <summary>
        /// This block's own source text, from its <c>SELECT</c> keyword to the end of the block.
        /// Re-emitted verbatim while the block is unmodified.
        /// </summary>
        public string Raw { get; init; } = string.Empty;

        /// <summary>
        /// The whitespace that followed the last clause body of the block in the input. Always
        /// re-emitted, so trailing layout survives a round trip.
        /// </summary>
        public string Trailing { get; set; } = string.Empty;

        /// <summary>
        /// Whether any clause of this block has been modified since the parse. While false,
        /// <see cref="Emit"/> returns <see cref="Raw"/> unchanged.
        /// </summary>
        public bool Modified { get; set; }

        /// <summary>The six clause slots, indexed by the numeric value of <see cref="ClauseKind"/>.</summary>
        public ClauseSlot[] Clauses { get; } = CreateSlots();

        /// <summary>Creates the fixed set of six empty clause slots.</summary>
        private static ClauseSlot[] CreateSlots()
        {
            ClauseSlot[] slots = new ClauseSlot[ClauseKindCount];
            for (int index = 0; index < ClauseKindCount; index++)
            {
                slots[index] = new ClauseSlot();
            }

            return slots;
        }

        /// <summary>
        /// Renders this block: verbatim while unmodified, otherwise spliced clause by clause in
        /// canonical order.
        /// </summary>
        /// <returns>The block's SQL text.</returns>
        /// <remarks>
        /// <para>
        /// The splice reuses each surviving clause's own source geometry, so every region of the
        /// statement that was not modified comes out byte-identical to the input. Three cases:
        /// </para>
        /// <list type="bullet">
        /// <item>
        /// present with source geometry - emit its captured pre-gap, its captured introducer, its
        /// captured lead gap and its current text. When the clause is unmodified that is exactly the
        /// input's own characters.
        /// </item>
        /// <item>
        /// absent - emit nothing at all, which is how a replace with empty text removes a clause and
        /// takes its pre-gap with it, leaving no dangling keyword and no double space.
        /// </item>
        /// <item>
        /// present without source geometry - a clause introduced by a modification where the input
        /// had none. Emit the upper-case canonical introducer, separated from what precedes it by a
        /// single space, followed by the clause text.
        /// </item>
        /// </list>
        /// </remarks>
        public string Emit()
        {
            if (!Modified)
            {
                return Raw;
            }

            StringBuilder builder = new(Raw.Length + 64);

            for (int kind = 0; kind < ClauseKindCount; kind++)
            {
                ClauseSlot slot = Clauses[kind];
                if (!slot.Present)
                {
                    continue;
                }

                if (slot.HasSourceGeometry)
                {
                    builder.Append(slot.PreGap);
                    builder.Append(slot.Introducer);

                    if (slot.Text.Length > 0)
                    {
                        // The captured lead gap is reused as-is. It is empty only when the input put
                        // nothing at all between the introducer and the body, as in "SELECT(A)"; a
                        // single space is then substituted, because the body may since have been
                        // replaced with text that would otherwise fuse onto the keyword.
                        builder.Append(slot.LeadGap.Length > 0 ? slot.LeadGap : " ");
                        builder.Append(slot.Text);
                    }

                    // Nothing further is emitted when the body is empty - a degenerate input such as
                    // "SELECT A FROM T WHERE   ORDER BY B". The whitespace that followed the
                    // introducer was handed to the next clause as its pre-gap by the parse, so the
                    // bare keyword is the whole of this clause and re-emitting the lead gap here would
                    // duplicate that whitespace.
                }
                else
                {
                    // A clause introduced by a modification where the input had none. It only becomes
                    // present by being given text, so the text is never empty on this path.
                    AppendSeparated(builder, CanonicalIntroducers[kind]);
                    builder.Append(' ');
                    builder.Append(slot.Text);
                }
            }

            builder.Append(Trailing);

            return builder.ToString();
        }

        /// <summary>
        /// Appends <paramref name="value"/>, inserting a single space first when the buffer is not
        /// empty.
        /// </summary>
        /// <remarks>
        /// No whitespace test is needed on what precedes: every clause emission ends either with a
        /// trimmed body or with an introducer keyword, so the buffer never ends in whitespace at this
        /// point. The empty case is reachable, and only in the degenerate situation where every
        /// earlier clause has been cleared outright.
        /// </remarks>
        private static void AppendSeparated(StringBuilder builder, string value)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(value);
        }
    }

    // ------------------------------------------------------------------------------------------
    //  Statement state
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The parsed select blocks, in source order. Empty until a successful
    /// <see cref="Parse(string)"/>, and emptied again by a failed one.
    /// </summary>
    /// <remarks>
    /// This is the only mutable state in the type, and it is per-instance. Nothing here is static
    /// and mutable, so two instances never interfere - which matters because the legacy SQL task
    /// layer is worker-thread-affine and shared parser state would be a concurrency defect the
    /// legacy does not have.
    /// </remarks>
    private readonly List<SelectBlock> _blocks = [];

    // ------------------------------------------------------------------------------------------
    //  Scanner tokens
    // ------------------------------------------------------------------------------------------

    /// <summary>A clause introducer found at parenthesis depth zero.</summary>
    /// <param name="Kind">Which clause the introducer opens.</param>
    /// <param name="Start">Index of the first character of the introducer.</param>
    /// <param name="End">Index one past the last character of the introducer.</param>
    private readonly record struct ClauseToken(ClauseKind Kind, int Start, int End);

    /// <summary>A set operator found at parenthesis depth zero, which separates two select blocks.</summary>
    /// <param name="Start">Index of the first character of the operator.</param>
    /// <param name="End">Index one past the last character of the operator.</param>
    private readonly record struct SetOperatorToken(int Start, int End);

    // ------------------------------------------------------------------------------------------
    //  Parse, emit, count - n_sql.sru:L11-L13
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Parses a <c>SELECT</c> statement, replacing anything this instance held before.
    /// Reproduces <c>parse(readonly string sql)</c> at <c>n_sql.sru:L11</c>.
    /// </summary>
    /// <param name="sql">The statement text.</param>
    /// <returns>
    /// <see langword="true"/> when the statement was understood; <see langword="false"/> when it was
    /// not, in which case the instance is left empty - <see cref="GetSelectCount"/> returns zero,
    /// every <c>Has*</c> returns <see langword="false"/>, every <c>Get*</c> returns an empty string
    /// and every <c>Modify*</c> returns <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The legacy consumer treats a false return as fatal:
    /// <c>n_cst_thread_task_sqlquery.sru:L314</c> raises an internal-error event and abandons the
    /// paging build. That fail-fast posture is the caller's to keep; this method simply reports the
    /// outcome and never throws.
    /// </para>
    /// <para>
    /// A statement is rejected when it is null, empty or entirely whitespace; when its parentheses
    /// are unbalanced; when a quoted literal, a quoted or bracketed identifier or a block comment is
    /// left unterminated; when any select block contains no depth-zero clause introducer at all;
    /// when a block's first depth-zero introducer is not <c>SELECT</c> - which is what rejects a
    /// non-<c>SELECT</c> statement, and also a block wrapped entirely in parentheses, whose
    /// <c>SELECT</c> is never at depth zero; or when a block's introducers do not appear in strictly
    /// increasing canonical order, which is what rejects a duplicated clause.
    /// </para>
    /// <para>
    /// Text before the first block's <c>SELECT</c> is retained verbatim as that block's prefix and
    /// is never interpreted, so a leading common-table expression - whose own <c>SELECT</c> sits
    /// inside parentheses and is therefore invisible to the depth-zero scan - survives a round trip
    /// and is carried along by every modification.
    /// </para>
    /// </remarks>
    public bool Parse(string sql)
    {
        _blocks.Clear();

        if (string.IsNullOrWhiteSpace(sql))
        {
            return false;
        }

        List<ClauseToken> clauseTokens = [];
        List<SetOperatorToken> setOperatorTokens = [];

        if (!TryScanTokens(sql, clauseTokens, setOperatorTokens))
        {
            return false;
        }

        if (clauseTokens.Count == 0)
        {
            return false;
        }

        int segmentCount = setOperatorTokens.Count + 1;
        List<SelectBlock> blocks = new(segmentCount);
        int tokenCursor = 0;
        int prefixStart = 0;

        for (int segment = 0; segment < segmentCount; segment++)
        {
            int segmentEnd = segment < setOperatorTokens.Count
                ? setOperatorTokens[segment].Start
                : sql.Length;

            int firstToken = tokenCursor;
            while (tokenCursor < clauseTokens.Count && clauseTokens[tokenCursor].Start < segmentEnd)
            {
                tokenCursor++;
            }

            int tokenEnd = tokenCursor;

            // A select block with no clause introducer at all is not a select block.
            if (tokenEnd == firstToken)
            {
                _blocks.Clear();
                return false;
            }

            // The block must open with SELECT. This is what rejects an UPDATE, a DELETE or a block
            // whose SELECT is nested inside parentheses.
            if (clauseTokens[firstToken].Kind != ClauseKind.Column)
            {
                _blocks.Clear();
                return false;
            }

            // Introducers must appear in strictly increasing canonical order, which rejects both a
            // duplicated clause and an out-of-order one.
            for (int token = firstToken + 1; token < tokenEnd; token++)
            {
                if (clauseTokens[token].Kind <= clauseTokens[token - 1].Kind)
                {
                    _blocks.Clear();
                    return false;
                }
            }

            blocks.Add(BuildBlock(sql, prefixStart, segmentEnd, clauseTokens, firstToken, tokenEnd));

            // The next block's prefix begins at the set operator, so the operator's own text is
            // carried verbatim and never reconstructed.
            prefixStart = segmentEnd;
        }

        _blocks.AddRange(blocks);

        return true;
    }

    /// <summary>
    /// Renders the whole statement. Reproduces <c>getsql()</c> at <c>n_sql.sru:L12</c>.
    /// </summary>
    /// <returns>
    /// The statement text, or an empty string when no statement has been parsed successfully.
    /// </returns>
    /// <remarks>
    /// With no intervening modification this returns the parsed input unchanged, byte for byte.
    /// After a modification, each modified block is spliced and every other region of the statement
    /// is still emitted verbatim. The legacy calls this repeatedly during one paging build - at
    /// <c>n_cst_thread_task_sqlquery.sru:L346</c>, <c>:L356</c>, <c>:L364</c>, <c>:L373</c>,
    /// <c>:L382</c>, <c>:L394</c>, <c>:L703</c> and <c>:L834</c> - so it is a pure read that must
    /// never disturb the model.
    /// </remarks>
    public string GetSql()
    {
        if (_blocks.Count == 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new(EstimateLength());

        foreach (SelectBlock block in _blocks)
        {
            builder.Append(block.Prefix);
            builder.Append(block.Emit());
        }

        return builder.ToString();
    }

    /// <summary>
    /// The number of <c>SELECT</c> blocks in the parsed statement. Reproduces
    /// <c>getselectcount()</c> at <c>n_sql.sru:L13</c>.
    /// </summary>
    /// <returns>The block count, or zero when no statement has been parsed successfully.</returns>
    /// <remarks>
    /// Zero is also the channel by which a caller detects that a parse failed - which is exactly
    /// what the factory at <see cref="ParseSql(string)"/> forces a caller to do, because the legacy
    /// factory discards the parse result.
    /// </remarks>
    public int GetSelectCount() => _blocks.Count;

    /// <summary>Sums the recorded source lengths, to size the emission buffer once.</summary>
    private int EstimateLength()
    {
        int total = 0;

        foreach (SelectBlock block in _blocks)
        {
            total += block.Prefix.Length + block.Raw.Length;
        }

        return total + 64;
    }

    // ------------------------------------------------------------------------------------------
    //  One-based select index resolution - the single conversion point
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Resolves a one-based select index to its block, or <see langword="null"/> when the index
    /// addresses nothing.
    /// </summary>
    /// <param name="oneBasedSelectIndex">
    /// The select index exactly as a caller supplies it: <c>1</c> is the first block, matching the
    /// legacy convention and the literal <c>1</c> that the caller-side proxy passes at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L380</c> and
    /// <c>:L383</c>.
    /// </param>
    /// <returns>The addressed block, or <see langword="null"/>.</returns>
    /// <remarks>
    /// <para>
    /// This is the ONLY place in this file where a one-based index becomes a zero-based list
    /// position. Scattering that arithmetic is the single most dangerous mechanical hazard in this
    /// whole migration, because an off-by-one there is indistinguishable from a behavioural
    /// regression; keeping it in one named method makes it auditable in one place.
    /// </para>
    /// <para>
    /// A non-positive or out-of-range index resolves to nothing rather than throwing. The native
    /// returns a value in every case and its callers never guard a call - the paging routine invokes
    /// <c>GetOrder()</c> and <c>ModifyOrder(...)</c> bare at
    /// <c>n_cst_thread_task_sqlquery.sru:L327</c> and <c>:L341</c> - so an exception here would be a
    /// new failure mode the legacy does not have. It does matter that the outcome is reportable,
    /// though: <c>:L691</c> and <c>:L698</c> treat a false return from a three-argument modify as an
    /// error, which is how an index that addresses no block surfaces to the caller.
    /// </para>
    /// </remarks>
    private SelectBlock? ResolveBlock(int oneBasedSelectIndex)
    {
        if (oneBasedSelectIndex <= 0 || oneBasedSelectIndex > _blocks.Count)
        {
            return null;
        }

        return _blocks[oneBasedSelectIndex - 1];
    }

    /// <summary>
    /// The select index that the short arity of every clause accessor targets.
    /// </summary>
    /// <remarks>
    /// Measured, not assumed. The caller-side proxy's two-argument overloads have the literal bodies
    /// <c>return of_SetOrderByClause(1,ms,clause)</c> and
    /// <c>return of_SetWhereClause(1,ms,clause)</c> at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L380</c> and
    /// <c>:L383</c>, so the short arity means "the first block", never "all blocks".
    /// </remarks>
    private const int DefaultSelectIndex = 1;

    // ------------------------------------------------------------------------------------------
    //  Block construction - capturing the exact source geometry of every clause
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Builds one select block from the clause tokens that fall inside one segment of the input.
    /// </summary>
    /// <param name="sql">The whole statement text.</param>
    /// <param name="prefixStart">Index at which this block's verbatim prefix begins.</param>
    /// <param name="segmentEnd">Index one past the last character belonging to this block.</param>
    /// <param name="tokens">All clause tokens of the statement, in source order.</param>
    /// <param name="firstToken">Index of this block's first clause token.</param>
    /// <param name="tokenEnd">Index one past this block's last clause token.</param>
    /// <returns>The populated block.</returns>
    /// <remarks>
    /// <para>
    /// Each clause owns the gap that PRECEDES its introducer rather than the gap that follows it.
    /// That choice is what makes removing a clause clean: dropping the clause drops its leading gap
    /// with it, so clearing the <c>ORDER BY</c> of <c>SELECT a FROM t WHERE x=1 ORDER BY a</c> yields
    /// <c>SELECT a FROM t WHERE x=1</c> with no trailing space, which is the shape the legacy needs
    /// at <c>n_cst_thread_task_sqlquery.sru:L377</c> before it wraps the result in a derived table at
    /// <c>:L382</c>.
    /// </para>
    /// <para>
    /// The gap that follows the LAST clause body becomes the block's trailing text, which is always
    /// re-emitted so the round trip stays byte-exact.
    /// </para>
    /// </remarks>
    private static SelectBlock BuildBlock(
        string sql,
        int prefixStart,
        int segmentEnd,
        List<ClauseToken> tokens,
        int firstToken,
        int tokenEnd)
    {
        int selectStart = tokens[firstToken].Start;

        SelectBlock block = new()
        {
            Prefix = sql[prefixStart..selectStart],
            Raw = sql[selectStart..segmentEnd],
        };

        // Carried from one clause to the next: the whitespace that trailed the previous body
        // becomes the pre-gap of the clause that follows it.
        string carriedGap = string.Empty;

        for (int token = firstToken; token < tokenEnd; token++)
        {
            ClauseToken current = tokens[token];

            int bodyStart = current.End;
            int bodyEnd = token + 1 < tokenEnd ? tokens[token + 1].Start : segmentEnd;

            int textStart = bodyStart;
            while (textStart < bodyEnd && char.IsWhiteSpace(sql[textStart]))
            {
                textStart++;
            }

            int textEnd = bodyEnd;
            while (textEnd > textStart && char.IsWhiteSpace(sql[textEnd - 1]))
            {
                textEnd--;
            }

            ClauseSlot slot = block.Clauses[(int)current.Kind];
            slot.Present = true;
            slot.PreGap = carriedGap;
            slot.Introducer = sql[current.Start..current.End];

            if (textStart < textEnd)
            {
                slot.LeadGap = sql[bodyStart..textStart];
                slot.Text = sql[textStart..textEnd];
                carriedGap = sql[textEnd..bodyEnd];
            }
            else
            {
                // The body is empty or entirely whitespace - a degenerate input such as
                // "SELECT a FROM t WHERE   ORDER BY b". Hand the whole run of whitespace to the
                // NEXT clause as its pre-gap rather than keeping it as a lead gap, so that the
                // round trip stays byte-exact and no separator has to be invented.
                slot.LeadGap = string.Empty;
                slot.Text = string.Empty;
                carriedGap = sql[bodyStart..bodyEnd];
            }
        }

        block.Trailing = carriedGap;

        return block;
    }

    // ------------------------------------------------------------------------------------------
    //  Scanner
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Walks the statement once, collecting the clause introducers and set operators that sit at
    /// parenthesis depth zero outside every literal, quoted identifier and comment.
    /// </summary>
    /// <param name="sql">The statement text.</param>
    /// <param name="clauses">Receives the clause introducers, in source order.</param>
    /// <param name="setOperators">Receives the set operators, in source order.</param>
    /// <returns>
    /// <see langword="true"/> when the text scanned cleanly; <see langword="false"/> when the
    /// parentheses are unbalanced or a literal, quoted identifier or block comment is unterminated.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Depth awareness and literal awareness are both load-bearing, not defensive polish, because
    /// both cases are live in this codebase. The paging routine splices
    /// <c>ROW_NUMBER() OVER (ORDER BY ...)</c> into the COLUMN clause at
    /// <c>n_cst_thread_task_sqlquery.sru:L355</c> and <c>:L381</c>, and splices
    /// <c>INNER JOIN (&lt;a whole SELECT&gt;) pfwPagedSQL_OutterTbl ON ...</c> into the TABLE clause
    /// at <c>:L350</c> and <c>:L362</c>. A scan that merely searched for the text
    /// <c>ORDER BY</c> would split inside the window function, and one that searched for
    /// <c>SELECT</c> would split inside the injected join, corrupting the statement in both cases.
    /// A predicate comparing against a literal that happens to contain the words is the third case,
    /// and quoted-literal awareness is what covers it.
    /// </para>
    /// <para>
    /// Recognised opaque regions: single-quoted literals with the doubled-quote escape, double-quoted
    /// identifiers with the doubled-quote escape, bracketed identifiers with the doubled-bracket
    /// escape, line comments introduced by a double hyphen, and block comments. Block comments are
    /// treated as non-nesting, which matches the SQL standard; T-SQL's nesting extension is not
    /// modelled, and a nested opener would simply end the comment early rather than corrupt the
    /// depth count.
    /// </para>
    /// </remarks>
    private static bool TryScanTokens(
        string sql,
        List<ClauseToken> clauses,
        List<SetOperatorToken> setOperators)
    {
        int depth = 0;
        int index = 0;

        while (index < sql.Length)
        {
            char current = sql[index];

            switch (current)
            {
                // A single-quoted literal or a double-quoted identifier: opaque, and closed by its
                // own delimiter.
                case '\'':
                case '"':
                    if (!TrySkipDelimited(sql, ref index, current))
                    {
                        return false;
                    }

                    continue;

                // A bracketed identifier, which is how SQL Server quotes a name that collides with
                // a keyword - so the scan must never see the keyword inside it.
                case '[':
                    if (!TrySkipDelimited(sql, ref index, ']'))
                    {
                        return false;
                    }

                    continue;

                case '(':
                    depth++;
                    index++;
                    continue;

                case ')':
                    depth--;
                    if (depth < 0)
                    {
                        return false;
                    }

                    index++;
                    continue;

                default:
                    break;
            }

            if (current == '-' && index + 1 < sql.Length && sql[index + 1] == '-')
            {
                SkipLineComment(sql, ref index);
                continue;
            }

            if (current == '/' && index + 1 < sql.Length && sql[index + 1] == '*')
            {
                if (!TrySkipBlockComment(sql, ref index))
                {
                    return false;
                }

                continue;
            }

            if (depth == 0 && char.IsAsciiLetter(current) && IsWordBoundaryBefore(sql, index))
            {
                if (TryMatchSetOperator(sql, index, out int setOperatorEnd))
                {
                    setOperators.Add(new SetOperatorToken(index, setOperatorEnd));
                    index = setOperatorEnd;
                    continue;
                }

                if (TryMatchClauseIntroducer(sql, index, out ClauseKind kind, out int clauseEnd))
                {
                    clauses.Add(new ClauseToken(kind, index, clauseEnd));
                    index = clauseEnd;
                    continue;
                }
            }

            index++;
        }

        return depth == 0;
    }

    /// <summary>
    /// Advances <paramref name="index"/> past a delimited region that starts at
    /// <paramref name="index"/>, honouring the doubled-closer escape.
    /// </summary>
    /// <param name="sql">The statement text.</param>
    /// <param name="index">On entry, the opener's index; on success, one past the closer.</param>
    /// <param name="closer">The closing delimiter, doubled to escape itself.</param>
    /// <returns><see langword="true"/> when the region was closed; otherwise <see langword="false"/>.</returns>
    private static bool TrySkipDelimited(string sql, ref int index, char closer)
    {
        // The opening delimiter is consumed by position: whatever character stood at index started
        // the region, so the scan begins one past it.
        int scan = index + 1;

        while (scan < sql.Length)
        {
            if (sql[scan] == closer)
            {
                if (scan + 1 < sql.Length && sql[scan + 1] == closer)
                {
                    scan += 2;
                    continue;
                }

                index = scan + 1;
                return true;
            }

            scan++;
        }

        index = sql.Length;
        return false;
    }

    /// <summary>
    /// Advances <paramref name="index"/> past a line comment, which ends at the first carriage
    /// return or line feed, or at the end of the text.
    /// </summary>
    private static void SkipLineComment(string sql, ref int index)
    {
        int scan = index + 2;

        while (scan < sql.Length && sql[scan] != '\n' && sql[scan] != '\r')
        {
            scan++;
        }

        index = scan;
    }

    /// <summary>
    /// Advances <paramref name="index"/> past a block comment.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the comment was closed; <see langword="false"/> when it ran to the
    /// end of the text unterminated.
    /// </returns>
    private static bool TrySkipBlockComment(string sql, ref int index)
    {
        int scan = index + 2;

        while (scan + 1 < sql.Length)
        {
            if (sql[scan] == '*' && sql[scan + 1] == '/')
            {
                index = scan + 2;
                return true;
            }

            scan++;
        }

        index = sql.Length;
        return false;
    }

    // ------------------------------------------------------------------------------------------
    //  Keyword matching
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Whether a character can appear inside a SQL identifier, and therefore cannot sit adjacent to
    /// a keyword that is genuinely a keyword.
    /// </summary>
    /// <remarks>
    /// The set covers the letters and digits every dialect allows plus the four extras the two
    /// target dialects add - the underscore, and SQL Server's dollar, hash and at signs, which begin
    /// or appear in temporary-table and variable names.
    /// </remarks>
    private static bool IsIdentifierCharacter(char value) =>
        char.IsLetterOrDigit(value) || value == '_' || value == '$' || value == '#' || value == '@';

    /// <summary>
    /// Whether the character before <paramref name="index"/> lets a keyword start there.
    /// </summary>
    /// <remarks>
    /// A preceding identifier character means the candidate is the tail of a longer name, so
    /// <c>REORDER</c> never yields an <c>ORDER</c>. A preceding period means the candidate is the
    /// unqualified half of a qualified name, so a column written <c>t.from</c> is not read as a
    /// <c>FROM</c> clause.
    /// </remarks>
    private static bool IsWordBoundaryBefore(string sql, int index)
    {
        if (index == 0)
        {
            return true;
        }

        char previous = sql[index - 1];

        return !IsIdentifierCharacter(previous) && previous != '.';
    }

    /// <summary>
    /// Matches one whole word at <paramref name="index"/>, case-insensitively and ordinally.
    /// </summary>
    /// <param name="sql">The statement text.</param>
    /// <param name="index">Where the word must start.</param>
    /// <param name="word">The word to match.</param>
    /// <param name="end">On success, one past the last matched character; otherwise <paramref name="index"/>.</param>
    /// <returns><see langword="true"/> when the whole word matched and is not glued to an identifier.</returns>
    /// <remarks>
    /// The comparison is deliberately ordinal-ignore-case rather than culture-sensitive: SQL keywords
    /// are ASCII, and a culture-sensitive comparison would make keyword recognition depend on the
    /// host's locale, which is a defect no legacy behaviour asks for.
    /// </remarks>
    private static bool TryMatchWord(string sql, int index, string word, out int end)
    {
        end = index;

        if (index + word.Length > sql.Length)
        {
            return false;
        }

        if (string.Compare(sql, index, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) != 0)
        {
            return false;
        }

        int after = index + word.Length;

        if (after < sql.Length && IsIdentifierCharacter(sql[after]))
        {
            return false;
        }

        end = after;
        return true;
    }

    /// <summary>
    /// Matches two whole words separated by at least one whitespace character, tolerating any amount
    /// of whitespace between them.
    /// </summary>
    /// <returns><see langword="true"/> when both words matched.</returns>
    /// <remarks>
    /// This is what lets <c>GROUP BY</c>, <c>ORDER BY</c> and <c>UNION ALL</c> be recognised however
    /// the input spaced or line-broke them. Whitespace only: a comment interposed between the two
    /// words is not treated as a separator, which is a documented interpretation rather than a
    /// measurement, since the native's tolerance there is not observable from this repository.
    /// </remarks>
    private static bool TryMatchWordPair(string sql, int index, string first, string second, out int end)
    {
        end = index;

        if (!TryMatchWord(sql, index, first, out int afterFirst))
        {
            return false;
        }

        int scan = afterFirst;
        while (scan < sql.Length && char.IsWhiteSpace(sql[scan]))
        {
            scan++;
        }

        if (scan == afterFirst)
        {
            return false;
        }

        if (!TryMatchWord(sql, scan, second, out int afterSecond))
        {
            return false;
        }

        end = afterSecond;
        return true;
    }

    /// <summary>
    /// Matches a clause introducer at <paramref name="index"/>.
    /// </summary>
    /// <returns><see langword="true"/> when an introducer matched.</returns>
    private static bool TryMatchClauseIntroducer(string sql, int index, out ClauseKind kind, out int end)
    {
        if (TryMatchWord(sql, index, "SELECT", out end))
        {
            kind = ClauseKind.Column;
            return true;
        }

        if (TryMatchWord(sql, index, "FROM", out end))
        {
            kind = ClauseKind.Table;
            return true;
        }

        if (TryMatchWord(sql, index, "WHERE", out end))
        {
            kind = ClauseKind.Where;
            return true;
        }

        if (TryMatchWord(sql, index, "HAVING", out end))
        {
            kind = ClauseKind.Having;
            return true;
        }

        if (TryMatchWordPair(sql, index, "GROUP", "BY", out end))
        {
            kind = ClauseKind.Group;
            return true;
        }

        if (TryMatchWordPair(sql, index, "ORDER", "BY", out end))
        {
            kind = ClauseKind.Order;
            return true;
        }

        kind = ClauseKind.Column;
        end = index;
        return false;
    }

    /// <summary>
    /// Matches a set operator at <paramref name="index"/>, preferring the two-word
    /// <c>UNION ALL</c> over the one-word <c>UNION</c>.
    /// </summary>
    /// <returns><see langword="true"/> when a set operator matched.</returns>
    /// <remarks>
    /// <para>
    /// <b>That the statement splits into blocks at all is a documented design decision, not a
    /// measurement.</b> The splitting itself is proved by <c>getselectcount()</c> at
    /// <c>n_sql.sru:L13</c> and by the select index on every clause accessor, but the native's own
    /// splitting rule lives inside pfw.dll and is not observable here. Splitting on the set operators
    /// below at parenthesis depth zero is the reading that makes the counted surface meaningful.
    /// Worth keeping in proportion: every measured call site addresses block <c>1</c>, either
    /// explicitly at <c>n_cst_thread_task_sqlquery.sru:L691</c> and <c>:L698</c> by way of the
    /// caller-side proxy's literal <c>1</c>, or implicitly through the short arity - so single-block
    /// behaviour is the only path with direct evidence behind it.
    /// </para>
    /// <para>
    /// <b>WHICH OPERATORS, AND WHY ORACLE'S <c>MINUS</c> IS AMONG THEM RATHER THAN AN OMISSION.</b>
    /// The recognised set is <c>UNION</c>, <c>UNION ALL</c>, <c>INTERSECT</c>, <c>EXCEPT</c> and
    /// <c>MINUS</c> - five, because THIS ONE PARSER SERVES BOTH TARGET DIALECTS. The legacy dispatches
    /// its paging rewrite on a two-valued database type, <c>DBT_MSSQL = 0</c> and
    /// <c>DBT_ORACLE = 1</c> [<c>n_cst_thread_trans.sru:L60-L61</c>, resolved at <c>:L357-L359</c>],
    /// and both arms drive this same clause model - the SQL Server strategies at
    /// <c>n_cst_thread_task_sqlquery.sru:L323-L381</c> and the Oracle triple-nested row-number
    /// strategy at <c>:L392-L395</c>. <c>MINUS</c> is Oracle's spelling of set difference, for which
    /// <c>EXCEPT</c> is the SQL Server and standard spelling; recognising only the latter would leave
    /// the parser able to serve one dialect and not the other, which is not a choice this file is
    /// entitled to make.
    /// </para>
    /// <para>
    /// <b>The failure mode omitting it produces is silent and total, which is why it is called out.</b>
    /// An unrecognised operator is not skipped - it simply never becomes a block boundary, so both
    /// <c>SELECT</c> tokens land in ONE segment. The strict increasing-clause-order check in
    /// <see cref="Parse"/> then sees a second Column introducer after a Table introducer, rejects the
    /// whole statement, and <see cref="Parse"/> returns <see langword="false"/>. A valid Oracle
    /// compound query would therefore have been reported as unparseable rather than mis-parsed, and
    /// <c>parsesql.srf</c>'s factory discards that result [see the factory's Defect 1], so the caller
    /// would have received a live-but-empty model with no error anywhere.
    /// </para>
    /// <para>
    /// Each operator is matched as a WHOLE WORD through <see cref="TryMatchWord"/> and only at
    /// parenthesis depth zero outside every quoted or commented region, so a column, alias or
    /// bracketed identifier that merely spells one of these words is not a boundary. That protection
    /// is not specific to <c>MINUS</c>; it is the same protection <c>EXCEPT</c> and <c>INTERSECT</c>
    /// have always relied on.
    /// </para>
    /// </remarks>
    private static bool TryMatchSetOperator(string sql, int index, out int end)
    {
        if (TryMatchWord(sql, index, "UNION", out int afterUnion))
        {
            end = afterUnion;

            int scan = afterUnion;
            while (scan < sql.Length && char.IsWhiteSpace(sql[scan]))
            {
                scan++;
            }

            if (scan > afterUnion && TryMatchWord(sql, scan, "ALL", out int afterAll))
            {
                end = afterAll;
            }

            return true;
        }

        if (TryMatchWord(sql, index, "INTERSECT", out end))
        {
            return true;
        }

        if (TryMatchWord(sql, index, "EXCEPT", out end))
        {
            return true;
        }

        // Oracle's set-difference operator, the dialect counterpart of EXCEPT above. Required because
        // one parser serves both DBT_MSSQL and DBT_ORACLE - see the remarks. Matched last only because
        // the order of these independent single-word tests is immaterial; it is not a fallback.
        if (TryMatchWord(sql, index, "MINUS", out end))
        {
            return true;
        }

        end = index;
        return false;
    }

    // ------------------------------------------------------------------------------------------
    //  Clause accessor cores - the single implementation behind all 36 clause members
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Whether the addressed block currently carries the given clause.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the index addresses no block, when nothing has been parsed, or
    /// when the clause is absent. Never throws.
    /// </returns>
    private bool HasClause(int selectIndex, ClauseKind kind)
    {
        SelectBlock? block = ResolveBlock(selectIndex);

        return block is not null && block.Clauses[(int)kind].Present;
    }

    /// <summary>
    /// The current text of the given clause, with surrounding whitespace removed.
    /// </summary>
    /// <returns>
    /// An empty string when the index addresses no block, when nothing has been parsed, or when the
    /// clause is absent. Never throws.
    /// </returns>
    /// <remarks>
    /// The legacy round-trips a clause through this accessor and back through <c>Modify*</c> - it
    /// saves the column list at <c>n_cst_thread_task_sqlquery.sru:L339</c> and restores it at
    /// <c>:L348</c>, and saves the order-by at <c>:L376</c> and restores it at <c>:L360</c> - so what
    /// comes out here has to be exactly what goes back in.
    /// </remarks>
    private string GetClause(int selectIndex, ClauseKind kind)
    {
        SelectBlock? block = ResolveBlock(selectIndex);

        if (block is null)
        {
            return string.Empty;
        }

        ClauseSlot slot = block.Clauses[(int)kind];

        return slot.Present ? slot.Text : string.Empty;
    }

    /// <summary>
    /// Applies a modify style to the given clause of the addressed block.
    /// </summary>
    /// <param name="selectIndex">One-based select index.</param>
    /// <param name="ms">
    /// The modify style: <see cref="Enums.SQL_MS_REPLACE"/>, <see cref="Enums.SQL_MS_APPEND"/> or
    /// <see cref="Enums.SQL_MS_PREPEND"/>, measured at
    /// <c>ws_objects/pfw.shared.pbl.src/enums.sru:L718-L720</c>.
    /// </param>
    /// <param name="newValue">The clause text to apply. Treated as opaque and never re-parsed.</param>
    /// <param name="kind">Which clause to modify.</param>
    /// <returns>
    /// <see langword="true"/> when the call was honoured, <see langword="false"/> when the index
    /// addresses no block or the style is unrecognised. On <see langword="false"/> nothing is mutated.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>The style transformation happens here, deliberately.</b> In the legacy it is the native
    /// <c>n_sql</c> that applies the style, so this is the file that owns the text transformation.
    /// The sibling clause-collection type owns the style constants' pending-clause bookkeeping and
    /// the return-code algebra; neither side duplicates the other.
    /// </para>
    /// <para>
    /// <b>Replace with empty text REMOVES the clause.</b> Measured from three independent locators
    /// that all annotate the call as stripping the clause -
    /// <c>n_cst_thread_task_sqlquery.sru:L377</c>, <c>:L390</c> and <c>:L832</c> - and corroborated
    /// structurally twice over: <c>:L831</c> guards the call with a presence test first, and
    /// <c>:L356</c>, <c>:L382</c> and <c>:L834</c> all embed the result in a derived table, where a
    /// dangling <c>ORDER BY</c> would be rejected outright by SQL Server. The complementary restore
    /// at <c>:L360</c> then puts the clause back. Applying the rule uniformly across all six clause
    /// kinds is the faithful generalisation of a single native implementation; clearing the column or
    /// table clause consequently yields a statement that is no longer a well-formed <c>SELECT</c>,
    /// which is the caller's responsibility exactly as it is in the legacy, since the native carries
    /// no guard against it either.
    /// </para>
    /// <para>
    /// <b>Append or prepend of empty text is a no-op that reports success.</b> This is a measured
    /// requirement, not a convenience: <c>n_cst_thread_task_sqlquery.sru:L341</c> appends
    /// <c>sUniqueColumnsOrderBy</c> to the order-by clause, and that string is legitimately empty
    /// whenever every unique-index column already appears in the existing order-by, which the loop at
    /// <c>:L335-L338</c> only adds to it when it does not. The native's behaviour for that case is not
    /// observable, so treating it as "nothing to add, nothing went wrong" is a documented
    /// interpretation. What matters is what must NOT happen here: this file deliberately does not
    /// apply the non-positive-index-or-empty-clause rejection that the caller applies at
    /// <c>:L269</c> and <c>:L286</c>. That guard belongs to the layer above, and reproducing it here
    /// would turn <c>:L341</c> into a failure.
    /// </para>
    /// <para>
    /// <b>Combining separator.</b> Append and prepend join the new fragment to the existing text with
    /// a single space; when the clause is absent or its text is empty both reduce to setting the new
    /// text with no separator at all. The single space is the reasonable reading of an unobservable
    /// native detail - it is the minimum that keeps two SQL fragments lexically apart.
    /// </para>
    /// </remarks>
    private bool ModifyClause(int selectIndex, long ms, string newValue, ClauseKind kind)
    {
        SelectBlock? block = ResolveBlock(selectIndex);

        if (block is null)
        {
            return false;
        }

        // Defensive rather than expected: the parameter is non-nullable, and PowerScript strings are
        // never null, so a null here can only arrive from a nullable-oblivious caller. Coercing it to
        // empty keeps the model from ever throwing, which is the contract the legacy call sites rely
        // on - none of them guards a modify call.
        string value = string.IsNullOrEmpty(newValue) ? string.Empty : newValue;

        ClauseSlot slot = block.Clauses[(int)kind];
        string existing = slot.Present ? slot.Text : string.Empty;

        switch (ms)
        {
            case Enums.SQL_MS_REPLACE:
                if (value.Length == 0)
                {
                    // Remove the clause outright. The source geometry is retained on the slot so a
                    // later restore re-emits the clause in the input's own spelling and spacing.
                    slot.Present = false;
                    slot.Text = string.Empty;
                }
                else
                {
                    slot.Present = true;
                    slot.Text = value;
                }

                block.Modified = true;
                return true;

            case Enums.SQL_MS_APPEND:
                if (value.Length == 0)
                {
                    // Measured no-op. See the remarks: this is n_cst_thread_task_sqlquery.sru:L341.
                    return true;
                }

                slot.Text = existing.Length == 0 ? value : existing + " " + value;
                slot.Present = true;
                block.Modified = true;
                return true;

            case Enums.SQL_MS_PREPEND:
                if (value.Length == 0)
                {
                    return true;
                }

                slot.Text = existing.Length == 0 ? value : value + " " + existing;
                slot.Present = true;
                block.Modified = true;
                return true;

            default:
                // An unrecognised style changes nothing and reports failure. The legacy caller treats
                // a false return as an error at n_cst_thread_task_sqlquery.sru:L691 and :L698, which
                // is how a bad style reaches the caller.
                return false;
        }
    }

    // ==========================================================================================
    //  Presence tests - n_sql.sru:L14-L25
    //  ------------------------------------------------------------------------------------------
    //  Twelve members: six clause kinds, each in the short arity and the select-index arity. Every
    //  short arity delegates to the indexed one with the first block, which is measured rather than
    //  assumed - see DefaultSelectIndex.
    // ==========================================================================================

    /// <summary>Whether the first block carries a select list. Reproduces <c>hascolumn()</c> at <c>n_sql.sru:L14</c>.</summary>
    public bool HasColumn() => HasClause(DefaultSelectIndex, ClauseKind.Column);

    /// <summary>Whether the addressed block carries a select list. Reproduces <c>hascolumn(int)</c> at <c>n_sql.sru:L15</c>.</summary>
    /// <param name="selectIndex">One-based select index.</param>
    public bool HasColumn(int selectIndex) => HasClause(selectIndex, ClauseKind.Column);

    /// <summary>Whether the first block carries a <c>FROM</c> clause. Reproduces <c>hastable()</c> at <c>n_sql.sru:L16</c>.</summary>
    public bool HasTable() => HasClause(DefaultSelectIndex, ClauseKind.Table);

    /// <summary>Whether the addressed block carries a <c>FROM</c> clause. Reproduces <c>hastable(int)</c> at <c>n_sql.sru:L17</c>.</summary>
    /// <param name="selectIndex">One-based select index.</param>
    public bool HasTable(int selectIndex) => HasClause(selectIndex, ClauseKind.Table);

    /// <summary>Whether the first block carries a <c>WHERE</c> clause. Reproduces <c>haswhere()</c> at <c>n_sql.sru:L18</c>.</summary>
    public bool HasWhere() => HasClause(DefaultSelectIndex, ClauseKind.Where);

    /// <summary>Whether the addressed block carries a <c>WHERE</c> clause. Reproduces <c>haswhere(int)</c> at <c>n_sql.sru:L19</c>.</summary>
    /// <param name="selectIndex">One-based select index.</param>
    public bool HasWhere(int selectIndex) => HasClause(selectIndex, ClauseKind.Where);

    /// <summary>Whether the first block carries a <c>GROUP BY</c> clause. Reproduces <c>hasgroup()</c> at <c>n_sql.sru:L20</c>.</summary>
    public bool HasGroup() => HasClause(DefaultSelectIndex, ClauseKind.Group);

    /// <summary>Whether the addressed block carries a <c>GROUP BY</c> clause. Reproduces <c>hasgroup(int)</c> at <c>n_sql.sru:L21</c>.</summary>
    /// <param name="selectIndex">One-based select index.</param>
    public bool HasGroup(int selectIndex) => HasClause(selectIndex, ClauseKind.Group);

    /// <summary>Whether the first block carries a <c>HAVING</c> clause. Reproduces <c>hashaving()</c> at <c>n_sql.sru:L22</c>.</summary>
    public bool HasHaving() => HasClause(DefaultSelectIndex, ClauseKind.Having);

    /// <summary>Whether the addressed block carries a <c>HAVING</c> clause. Reproduces <c>hashaving(int)</c> at <c>n_sql.sru:L23</c>.</summary>
    /// <param name="selectIndex">One-based select index.</param>
    public bool HasHaving(int selectIndex) => HasClause(selectIndex, ClauseKind.Having);

    /// <summary>
    /// Whether the first block carries an <c>ORDER BY</c> clause. Reproduces <c>hasorder()</c> at
    /// <c>n_sql.sru:L24</c>.
    /// </summary>
    /// <remarks>
    /// The most heavily exercised presence test in the legacy: the paging routine branches on it at
    /// <c>n_cst_thread_task_sqlquery.sru:L367</c>, <c>:L375</c>, <c>:L388</c> and <c>:L831</c>,
    /// substituting a synthetic sort key when there is no order-by to preserve.
    /// </remarks>
    public bool HasOrder() => HasClause(DefaultSelectIndex, ClauseKind.Order);

    /// <summary>Whether the addressed block carries an <c>ORDER BY</c> clause. Reproduces <c>hasorder(int)</c> at <c>n_sql.sru:L25</c>.</summary>
    /// <param name="selectIndex">One-based select index.</param>
    public bool HasOrder(int selectIndex) => HasClause(selectIndex, ClauseKind.Order);

    // ==========================================================================================
    //  Clause text accessors - n_sql.sru:L26-L37
    // ==========================================================================================

    /// <summary>
    /// The first block's select list - everything between <c>SELECT</c> and <c>FROM</c>, so a
    /// <c>TOP n</c> or <c>DISTINCT</c> prefix is part of it. Reproduces <c>getcolumn()</c> at
    /// <c>n_sql.sru:L26</c>.
    /// </summary>
    /// <remarks>
    /// That the row-limiting prefix belongs to this clause is measured, not inferred: the legacy
    /// replaces the whole clause with <c>"TOP " + n + " " + columns + ",ROW_NUMBER() OVER (...)"</c>
    /// at <c>n_cst_thread_task_sqlquery.sru:L355</c> and <c>:L381</c>.
    /// </remarks>
    public string GetColumn() => GetClause(DefaultSelectIndex, ClauseKind.Column);

    /// <summary>The addressed block's select list. Reproduces <c>getcolumn(int)</c> at <c>n_sql.sru:L27</c>.</summary>
    /// <param name="selectIndex">One-based select index.</param>
    public string GetColumn(int selectIndex) => GetClause(selectIndex, ClauseKind.Column);

    /// <summary>
    /// The first block's <c>FROM</c> body, joins included. Reproduces <c>gettable()</c> at
    /// <c>n_sql.sru:L28</c>.
    /// </summary>
    /// <remarks>
    /// The clause deliberately spans the ENTIRE from-body rather than just a table name, because the
    /// legacy appends a whole join into it - <c>INNER JOIN (&lt;subquery&gt;) pfwPagedSQL_OutterTbl ON
    /// ...</c> at <c>n_cst_thread_task_sqlquery.sru:L350</c> and <c>:L362</c> - and the result has to
    /// re-emit correctly.
    /// </remarks>
    public string GetTable() => GetClause(DefaultSelectIndex, ClauseKind.Table);

    /// <summary>The addressed block's <c>FROM</c> body. Reproduces <c>gettable(int)</c> at <c>n_sql.sru:L29</c>.</summary>
    /// <param name="selectIndex">One-based select index.</param>
    public string GetTable(int selectIndex) => GetClause(selectIndex, ClauseKind.Table);

    /// <summary>The first block's <c>WHERE</c> body. Reproduces <c>getwhere()</c> at <c>n_sql.sru:L30</c>.</summary>
    public string GetWhere() => GetClause(DefaultSelectIndex, ClauseKind.Where);

    /// <summary>The addressed block's <c>WHERE</c> body. Reproduces <c>getwhere(int)</c> at <c>n_sql.sru:L31</c>.</summary>
    /// <param name="selectIndex">One-based select index.</param>
    public string GetWhere(int selectIndex) => GetClause(selectIndex, ClauseKind.Where);

    /// <summary>The first block's <c>GROUP BY</c> body. Reproduces <c>getgroup()</c> at <c>n_sql.sru:L32</c>.</summary>
    public string GetGroup() => GetClause(DefaultSelectIndex, ClauseKind.Group);

    /// <summary>The addressed block's <c>GROUP BY</c> body. Reproduces <c>getgroup(int)</c> at <c>n_sql.sru:L33</c>.</summary>
    /// <param name="selectIndex">One-based select index.</param>
    public string GetGroup(int selectIndex) => GetClause(selectIndex, ClauseKind.Group);

    /// <summary>The first block's <c>HAVING</c> body. Reproduces <c>gethaving()</c> at <c>n_sql.sru:L34</c>.</summary>
    public string GetHaving() => GetClause(DefaultSelectIndex, ClauseKind.Having);

    /// <summary>The addressed block's <c>HAVING</c> body. Reproduces <c>gethaving(int)</c> at <c>n_sql.sru:L35</c>.</summary>
    /// <param name="selectIndex">One-based select index.</param>
    public string GetHaving(int selectIndex) => GetClause(selectIndex, ClauseKind.Having);

    /// <summary>
    /// The first block's <c>ORDER BY</c> body. Reproduces <c>getorder()</c> at <c>n_sql.sru:L36</c>.
    /// </summary>
    /// <remarks>
    /// Read four times in one paging build, at <c>n_cst_thread_task_sqlquery.sru:L327</c>,
    /// <c>:L342</c>, <c>:L368</c> and <c>:L376</c>, twice to be lower-cased for a containment test and
    /// twice to be saved for restoration after the clause has been stripped.
    /// </remarks>
    public string GetOrder() => GetClause(DefaultSelectIndex, ClauseKind.Order);

    /// <summary>The addressed block's <c>ORDER BY</c> body. Reproduces <c>getorder(int)</c> at <c>n_sql.sru:L37</c>.</summary>
    /// <param name="selectIndex">One-based select index.</param>
    public string GetOrder(int selectIndex) => GetClause(selectIndex, ClauseKind.Order);

    // ==========================================================================================
    //  Clause modifiers - n_sql.sru:L38-L49
    //  ------------------------------------------------------------------------------------------
    //  The select index comes FIRST in every three-argument overload, then the style, then the text -
    //  the legacy order in modifycolumn(readonly int nselectindex, readonly long ms,
    //  readonly string newcolumn) at n_sql.sru:L39. The parameters are plain by-value: the legacy
    //  `readonly` keyword maps to C# `in` only where the type makes that meaningful, and on a long and
    //  a string reference it would add indirection without adding meaning.
    //
    //  These return bool, not the framework return code, because the native surface has no return-code
    //  algebra at all while the task layer that drives it returns one. That asymmetry is legacy
    //  behaviour and is preserved rather than harmonised.
    // ==========================================================================================

    /// <summary>
    /// Modifies the first block's select list. Reproduces <c>modifycolumn(ms, newcolumn)</c> at
    /// <c>n_sql.sru:L38</c>.
    /// </summary>
    /// <param name="ms">The modify style.</param>
    /// <param name="newColumn">The clause text, stored opaquely and never re-parsed.</param>
    /// <returns><see langword="true"/> when the call was honoured.</returns>
    /// <remarks>
    /// Text supplied here is opaque by contract. The legacy passes
    /// <c>"TOP 20 *,ROW_NUMBER() OVER (ORDER BY (SELECT 0)) AS pfwPagedSQL_RN"</c>-shaped fragments at
    /// <c>n_cst_thread_task_sqlquery.sru:L355</c> and the count-wrapper <c>"1 AS _"</c> at
    /// <c>:L830</c>; the nested parentheses and the inner <c>SELECT</c> and <c>ORDER BY</c> must
    /// survive untouched into <see cref="GetSql()"/>, which they do because modification text is
    /// stored and re-emitted verbatim.
    /// </remarks>
    public bool ModifyColumn(long ms, string newColumn) =>
        ModifyClause(DefaultSelectIndex, ms, newColumn, ClauseKind.Column);

    /// <summary>
    /// Modifies the addressed block's select list. Reproduces
    /// <c>modifycolumn(nselectindex, ms, newcolumn)</c> at <c>n_sql.sru:L39</c>.
    /// </summary>
    /// <param name="selectIndex">One-based select index.</param>
    /// <param name="ms">The modify style.</param>
    /// <param name="newColumn">The clause text.</param>
    /// <returns><see langword="true"/> when the call was honoured.</returns>
    public bool ModifyColumn(int selectIndex, long ms, string newColumn) =>
        ModifyClause(selectIndex, ms, newColumn, ClauseKind.Column);

    /// <summary>
    /// Modifies the first block's <c>FROM</c> body. Reproduces <c>modifytable(ms, newtable)</c> at
    /// <c>n_sql.sru:L40</c>.
    /// </summary>
    /// <param name="ms">The modify style.</param>
    /// <param name="newTable">The clause text.</param>
    /// <returns><see langword="true"/> when the call was honoured.</returns>
    /// <remarks>
    /// The legacy appends a complete join, subquery and all, at
    /// <c>n_cst_thread_task_sqlquery.sru:L350</c> and <c>:L362</c>.
    /// </remarks>
    public bool ModifyTable(long ms, string newTable) =>
        ModifyClause(DefaultSelectIndex, ms, newTable, ClauseKind.Table);

    /// <summary>
    /// Modifies the addressed block's <c>FROM</c> body. Reproduces
    /// <c>modifytable(nselectindex, ms, newtable)</c> at <c>n_sql.sru:L41</c>.
    /// </summary>
    /// <param name="selectIndex">One-based select index.</param>
    /// <param name="ms">The modify style.</param>
    /// <param name="newTable">The clause text.</param>
    /// <returns><see langword="true"/> when the call was honoured.</returns>
    public bool ModifyTable(int selectIndex, long ms, string newTable) =>
        ModifyClause(selectIndex, ms, newTable, ClauseKind.Table);

    /// <summary>
    /// Modifies the first block's <c>WHERE</c> body. Reproduces <c>modifywhere(ms, newwhere)</c> at
    /// <c>n_sql.sru:L42</c>.
    /// </summary>
    /// <param name="ms">The modify style.</param>
    /// <param name="newWhere">The clause text.</param>
    /// <returns><see langword="true"/> when the call was honoured.</returns>
    public bool ModifyWhere(long ms, string newWhere) =>
        ModifyClause(DefaultSelectIndex, ms, newWhere, ClauseKind.Where);

    /// <summary>
    /// Modifies the addressed block's <c>WHERE</c> body. Reproduces
    /// <c>modifywhere(nselectindex, ms, newwhere)</c> at <c>n_sql.sru:L43</c>.
    /// </summary>
    /// <param name="selectIndex">One-based select index.</param>
    /// <param name="ms">The modify style.</param>
    /// <param name="newWhere">The clause text.</param>
    /// <returns><see langword="true"/> when the call was honoured.</returns>
    /// <remarks>
    /// This exact three-argument shape is what the legacy drives from its pending-clause collection at
    /// <c>n_cst_thread_task_sqlquery.sru:L691</c>, where a <see langword="false"/> return abandons the
    /// query build.
    /// </remarks>
    public bool ModifyWhere(int selectIndex, long ms, string newWhere) =>
        ModifyClause(selectIndex, ms, newWhere, ClauseKind.Where);

    /// <summary>
    /// Modifies the first block's <c>GROUP BY</c> body. Reproduces <c>modifygroup(ms, newgroup)</c>
    /// at <c>n_sql.sru:L44</c>.
    /// </summary>
    /// <param name="ms">The modify style.</param>
    /// <param name="newGroup">The clause text.</param>
    /// <returns><see langword="true"/> when the call was honoured.</returns>
    public bool ModifyGroup(long ms, string newGroup) =>
        ModifyClause(DefaultSelectIndex, ms, newGroup, ClauseKind.Group);

    /// <summary>
    /// Modifies the addressed block's <c>GROUP BY</c> body. Reproduces
    /// <c>modifygroup(nselectindex, ms, newgroup)</c> at <c>n_sql.sru:L45</c>.
    /// </summary>
    /// <param name="selectIndex">One-based select index.</param>
    /// <param name="ms">The modify style.</param>
    /// <param name="newGroup">The clause text.</param>
    /// <returns><see langword="true"/> when the call was honoured.</returns>
    public bool ModifyGroup(int selectIndex, long ms, string newGroup) =>
        ModifyClause(selectIndex, ms, newGroup, ClauseKind.Group);

    /// <summary>
    /// Modifies the first block's <c>HAVING</c> body. Reproduces <c>modifyhaving(ms, newhaving)</c>
    /// at <c>n_sql.sru:L46</c>.
    /// </summary>
    /// <param name="ms">The modify style.</param>
    /// <param name="newHaving">The clause text.</param>
    /// <returns><see langword="true"/> when the call was honoured.</returns>
    public bool ModifyHaving(long ms, string newHaving) =>
        ModifyClause(DefaultSelectIndex, ms, newHaving, ClauseKind.Having);

    /// <summary>
    /// Modifies the addressed block's <c>HAVING</c> body. Reproduces
    /// <c>modifyhaving(nselectindex, ms, newhaving)</c> at <c>n_sql.sru:L47</c>.
    /// </summary>
    /// <param name="selectIndex">One-based select index.</param>
    /// <param name="ms">The modify style.</param>
    /// <param name="newHaving">The clause text.</param>
    /// <returns><see langword="true"/> when the call was honoured.</returns>
    public bool ModifyHaving(int selectIndex, long ms, string newHaving) =>
        ModifyClause(selectIndex, ms, newHaving, ClauseKind.Having);

    /// <summary>
    /// Modifies the first block's <c>ORDER BY</c> body. Reproduces <c>modifyorder(ms, neworder)</c>
    /// at <c>n_sql.sru:L48</c>.
    /// </summary>
    /// <param name="ms">The modify style.</param>
    /// <param name="newOrder">The clause text.</param>
    /// <returns><see langword="true"/> when the call was honoured.</returns>
    /// <remarks>
    /// The busiest modifier in the legacy, and the one carrying both preserved edge cases: a replace
    /// with empty text strips the clause at <c>n_cst_thread_task_sqlquery.sru:L377</c>, <c>:L390</c>
    /// and <c>:L832</c>, a replace with the saved text restores it at <c>:L360</c>, and an append of a
    /// legitimately empty fragment at <c>:L341</c> is a no-op that must still report success.
    /// </remarks>
    public bool ModifyOrder(long ms, string newOrder) =>
        ModifyClause(DefaultSelectIndex, ms, newOrder, ClauseKind.Order);

    /// <summary>
    /// Modifies the addressed block's <c>ORDER BY</c> body. Reproduces
    /// <c>modifyorder(nselectindex, ms, neworder)</c> at <c>n_sql.sru:L49</c>.
    /// </summary>
    /// <param name="selectIndex">One-based select index.</param>
    /// <param name="ms">The modify style.</param>
    /// <param name="newOrder">The clause text.</param>
    /// <returns><see langword="true"/> when the call was honoured.</returns>
    /// <remarks>
    /// Driven from the pending-clause collection at
    /// <c>n_cst_thread_task_sqlquery.sru:L698</c>, where a <see langword="false"/> return abandons the
    /// query build.
    /// </remarks>
    public bool ModifyOrder(int selectIndex, long ms, string newOrder) =>
        ModifyClause(selectIndex, ms, newOrder, ClauseKind.Order);

    // ==========================================================================================
    //  Factory - parsesql.srf:L10-L14
    // ==========================================================================================

    /// <summary>
    /// Creates a model and parses <paramref name="sql"/> into it, reproducing the global function
    /// <c>parsesql</c> at <c>ws_objects/pfw.utility.parser.pbl.src/parsesql.srf:L10-L14</c>.
    /// </summary>
    /// <param name="sql">The statement text.</param>
    /// <returns>
    /// A model instance, ALWAYS non-null - including when the statement could not be parsed. Ask the
    /// returned model whether it parsed: <see cref="GetSelectCount"/> is zero exactly when the parse
    /// failed.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>PRESERVED LEGACY DEFECT - the discarded parse result.</b> The legacy factory is three lines:
    /// it creates the parser, calls <c>Parse(sql)</c>, and returns the instance - <em>throwing the
    /// boolean away</em>. A malformed statement therefore yields a live but unparsed object rather
    /// than a null or an exception, and the caller cannot tell from the returned reference whether
    /// parsing succeeded; it has to interrogate the model. That is reproduced here exactly, by
    /// discarding the result explicitly so no reader mistakes it for an oversight. It is deliberately
    /// NOT corrected: this method does not throw, does not return null, and is not reshaped into a
    /// try-pattern, because every one of those would be a behaviour change dressed up as robustness.
    /// </para>
    /// <para>
    /// <b>PRESERVED LEGACY DEFECT WITH NO MANAGED ANALOGUE - the leaked instance.</b> The legacy
    /// factory creates the object and never destroys it, so ownership transfers silently to the caller,
    /// who must <c>Destroy</c> it - which the one measured consumer does remember to do, at
    /// <c>n_cst_thread_task_sqlquery.sru:L401</c> and <c>:L874</c>. In .NET the lifetime is the garbage
    /// collector's and the container's, so there is simply nothing to reproduce. Note the asymmetry
    /// deliberately: the ignored-result behaviour above IS observable and IS reproduced, whereas this
    /// one is not observable and is not. <see cref="IDisposable"/> is deliberately not implemented to
    /// "mirror" the destroy - this type owns no unmanaged resource, and inventing a disposal contract
    /// for it would be an invented requirement.
    /// </para>
    /// </remarks>
    public static SelectStatementModel ParseSql(string sql)
    {
        SelectStatementModel model = new();

        // The discard is the defect, made explicit. See the remarks above.
        _ = model.Parse(sql);

        return model;
    }
}
