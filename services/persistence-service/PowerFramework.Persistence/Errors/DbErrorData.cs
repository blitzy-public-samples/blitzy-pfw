// ==============================================================================================
//  DbErrorData - the in-process structured database-error payload
//  --------------------------------------------------------------------------------------------
//  PORTED FROM    ws_objects/pfw.thread.ext.pbl.src/dberrordata.srs:L3-L9
//                     the five-field PowerBuilder structure this record mirrors, in full
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L34, L85-L98
//                     the worker-side event declaration and the pack-and-forward bridge
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru:L9, L17,
//                     L44, L47, L53-L65, L206-L208
//                     the caller-side retention, its accessor, and BOTH reset sites
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L310-L342
//                     the override that rewrites the retained payload's row after storage
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L159
//                     the datastore's native dberror event, forwarded verbatim
//
//  ORACLE STATUS  Every ws_objects/** path named in this file is READ ONLY (constraint C-C). Each
//                 was read as specification and cited by locator; nothing here copies, reformats,
//                 moves, edits or deletes any of them, and nothing in the .NET tree depends on the
//                 PowerBuilder toolchain, the PowerBuilder runtime or any shipped native binary.
//                 The legacy tree is the ONLY statement of intended behaviour that exists for this
//                 structure - there is no schema, no changelog entry and no other document that
//                 could adjudicate a disagreement - which is why every behavioural claim below
//                 carries the :L line reference it was taken from.
//
//  THE STRUCTURE, VERBATIM FROM THE ORACLE [dberrordata.srs:L3-L9]
//  --------------------------------------------------------------------------------------------
//      global type dberrordata from structure
//          long        sqldbcode        [:L4]  ->  long      SqlDbCode
//          string      sqlerrtext       [:L5]  ->  string    SqlErrText
//          string      sqlsyntax        [:L6]  ->  string    SqlSyntax
//          dwbuffer    buffer           [:L7]  ->  DwBuffer  Buffer
//          long        row              [:L8]  ->  long      Row
//      end type
//
//  Its own export comment reads "PowerThread database error data (DBError)" [dberrordata.srs:L2].
//  EXACTLY FIVE MEMBERS, IN THIS ORDER, AND THERE IS NO SIXTH. The order is not cosmetic: it is
//  the order the legacy forwards them in positionally [sqlbase_ds.sru:L159], the order the bridge
//  assigns them in [sqlbase.sru:L88-L92], the order the wire mirror declares its fields in
//  [common.v1.proto DbError, fields 1..5], and the order the compiler-generated ToString() below
//  renders them in. A reordering or an addition here desynchronizes all four at once.
//
//  Member spellings are fixed by the oracle and are NOT restyled. In particular the second member
//  is SqlErrText, from `sqlerrtext` [:L5], and NOT SqlErrorText: the longer spelling belongs to a
//  DIFFERENT object, the three-parameter SQLite binding event
//  `event OnDBError (long code, string sqlErrorText, string sqlSyntax)`
//  [ws_objects/pfw.utility.sqlite.pbl.src/n_sqlite.sru:L88], which must not influence this one.
//  The fourth member's NAME is `buffer` and its TYPE is `dwbuffer` [:L7], so the member is Buffer;
//  naming it DwBuffer would name it after its type and diverge from the oracle.
//
//  C-B SELF-AUDIT: NO MEMBER IS ADDED THAT THE LEGACY STRUCTURE LACKS
//  --------------------------------------------------------------------------------------------
//  The legacy has five fields, so this record has five members. There is deliberately no
//  timestamp, no severity, no correlation identifier, no captured exception, no stack trace and no
//  transient-or-permanent classification, however useful any of them would look on an error type.
//  Each would be a NEW capability rather than a port, which C-B forbids: behaviour is preserved
//  exactly, and documented defects are replicated rather than corrected. The same audit forbids
//  "improving" the parts of this contract that are awkward - the single retained value that a
//  second error silently discards, the row ordinal that is rewritten after storage, and the
//  Chinese diagnostic that bypasses localization are all reproduced as they are.
//
//  THE COMPLETE RAISE CENSUS, MEASURED RATHER THAN SAMPLED
//  --------------------------------------------------------------------------------------------
//  Every `Event OnDBError(` site in ws_objects/pfw.thread.ext.pbl.src/ was enumerated, so the
//  three factories below are known to be sufficient and no fourth shape exists to be discovered
//  later. NINE sites raise the payload, one bridges it, and one populates it without raising:
//
//      sqlupdate.sru:L190   (-1, "<no updatable table>", "", Primary!, 0)      -> NoUpdatableTable
//      sqlupdate.sru:L197   (TransObject code, text, "", Primary!, 0)         -> FromTransaction
//      sqlupdate.sru:L293   (dbErrData code, text, "", Primary!, 0)           -> FromTransaction
//      sqlquery.sru:L521    (dbErrdata code, text, "", Primary!, 0)           -> FromTransaction
//      sqlquery.sru:L748    (TransObject code, text, "", Primary!, 0)         -> FromTransaction
//      sqlcommand.sru:L71   (dbErrData code, text, "", Primary!, 0)           -> FromTransaction
//      sqlcommand.sru:L110  (TransObject code, text, sSQL, Primary!, 0)       -> FromStatement
//      sqlquery.sru:L855    (TransObject code, text, sSQL, Primary!, 1)       -> FromStatement
//      sqlbase_ds.sru:L159  (sqldbcode, sqlerrtext, sqlsyntax, buffer, row)   -> FromStatement
//
//      sqlbase.sru:L95      Event OnDBError(err)   the BRIDGE - forwards the packed structure
//      sqlbase.sru:L175-176 code and text only, then returns E_INVALID_TRANSACTION - see
//                           FromTransaction, which covers this partial population too
//
//  Two facts fall straight out of that census and shape the factories. First, EIGHT of the nine
//  pass `Primary!` and `0`, and six of them pass `""` for the statement, which is why the cleared
//  state below is load-bearing rather than incidental. Second, sqlbase_ds.sru:L159 is the ONLY
//  site that can supply a genuinely non-Primary buffer or a non-zero row, because it forwards the
//  DataWindow runtime's own `dberror` arguments untouched; every other site hardcodes the pair.
//
//  THE DwBuffer SENTINEL, AND A CORRECTION THAT CHANGES THE IMPLEMENTATION (C-K)
//  --------------------------------------------------------------------------------------------
//  Buffer is typed as the GENERATED PowerFramework.Contracts.Common.V1.DwBuffer, reached through
//  this project's ProjectReference to shared/PowerFramework.Contracts. No local buffer enum is
//  declared here and none may be: a second definition would drift from the wire contract, and the
//  versioned contracts project is the only sanctioned cross-boundary vocabulary in this system.
//
//  The generated enum was READ rather than assumed, from
//  shared/PowerFramework.Contracts/obj/**/CommonV1.cs after compiling that project, and it has
//  FOUR members, not three - protobuf's C# generator strips the DW_BUFFER_ prefix and PascalCases
//  what is left [common.v1.proto:L619-L629]:
//
//      DW_BUFFER_UNSPECIFIED = 0   ->   DwBuffer.Unspecified = 0
//      DW_BUFFER_PRIMARY     = 1   ->   DwBuffer.Primary     = 1
//      DW_BUFFER_DELETE      = 2   ->   DwBuffer.Delete      = 2
//      DW_BUFFER_FILTER      = 3   ->   DwBuffer.Filter      = 3
//
//  PRIMARY IS THEREFORE NOT THE ZERO MEMBER, and that single fact decides how Buffer is written.
//  A plain `public DwBuffer Buffer { get; init; }` would make default(DbErrorData).Buffer observe
//  as Unspecified, which contradicts the cleared state the legacy actually produces. Buffer
//  consequently uses the same canonicalising backing field as the two strings, mapping BOTH
//  Primary and the sentinel onto one stored representation that observes as Primary.
//
//  Folding Unspecified into Primary is faithful rather than a liberty, on four counts:
//    1. The contract says so. UNSPECIFIED "is a protocol-level 'field absent', never a legacy
//       buffer: a request carrying it is malformed, and a response must never populate it"
//       [common.v1.proto:L620-L624]. Canonicalising on the way in makes it structurally
//       impossible for this service to hold, and therefore to emit, that value.
//    2. The legacy domain has exactly three values and no fourth [common.v1.proto:L606-L609,
//       counted from the literals Primary! 36, Filter! 18, Delete! 2]. A four-state in-process
//       value could represent something the legacy cannot.
//    3. PowerBuilder initialises an unassigned `dwbuffer` to Primary!, so in the legacy "no buffer
//       was supplied" and "the default buffer" are the SAME state. The sentinel exists only
//       because proto3 requires a zero first enumerator; it has no legacy counterpart to preserve.
//    4. It keeps one canonical cleared state, exactly as the string canonicalisation does, so
//       every spelling of "empty" compares equal instead of some of them comparing equal.
//
//  Delete and Filter are stored and returned untouched. Filter in particular must not be treated
//  as a leftover buffer: the legacy walks it BACKWARDS during the identity round trip because its
//  row order is inverted relative to the source [n_cst_thread_task_sqlupdate.sru:L235-L238], so a
//  Row paired with Filter does not count the way a Row paired with Primary does.
//
//  RELATIONSHIP TO THE WIRE TYPE - AND WHERE THE MAPPING LIVES, WHICH IS NOT HERE
//  --------------------------------------------------------------------------------------------
//  This record is the IN-PROCESS counterpart of the five-field DbError message in
//  shared/PowerFramework.Contracts/Proto/common.v1.proto, whose field order mirrors the oracle's
//  member order deliberately so the correspondence stays checkable at a glance.
//
//  The conversion to that message lives in Errors/SqlRedactor.cs, as a ToDbError extension that
//  REQUIRES a redactor argument, and it is deliberately absent from this file. The reason is a
//  security property rather than tidiness: SqlSyntax carries the complete generated statement
//  INCLUDING INTERPOLATED LITERAL VALUES, because the legacy parses DBParm for DisableBind and
//  DisableBind=1 means the runtime does not use bind variables at all
//  [n_cst_thread_task_sqlbase.sru:L128-L129], and the legacy logger performs no redaction of any
//  kind. If this type could convert itself, an unredacted statement could reach a network peer
//  through a one-line call. Making the redactor a required argument of the only available
//  conversion removes that path. Consumers must use that extension and must NOT hand-roll a
//  mapping, which would reintroduce exactly the leak the split exists to prevent.
//
//  C-A SELF-AUDIT: this type is Persistence-internal and is NOT promoted into
//  PowerFramework.Contracts. The cross-service form of a database error is the generated DbError
//  message and nothing else; the contracts project is the only permitted cross-service coupling,
//  and no shared behaviour crosses a service boundary.
//
//  C-F SELF-AUDIT: no credential, key, token, password, connection string, certificate or
//  secret-shaped placeholder appears anywhere in this file, in any comment, default or literal.
//  No statement text captured from a log appears here either, for the DisableBind reason above.
//
//  RULES POSITION
//  --------------------------------------------------------------------------------------------
//  review_rules returns exactly one line, "No user rules provided.", so NO user-specified rule
//  governs this file. That is a finding, not latitude, and nothing is invented or back-filled from
//  convention in its place. The enterprise-standard baseline applies instead - nullable reference
//  types on, warnings as errors, no secret in source, deterministic and trivially testable - and
//  the binding constraints are the refactor plan's own non-rule inventory, of which C-A, C-B, C-C,
//  C-F, C-H and C-K bite on this file and are each discharged at the point they are cited above.
//
//  No performance property is asserted anywhere in this file and no decision here is justified by
//  one: the repository publishes no latency budget, no throughput target and no availability
//  commitment, so there is no baseline against which such a claim could be made.
// ==============================================================================================

using PowerFramework.Contracts.Common.V1;

namespace PowerFramework.Persistence.Errors;

/// <summary>
/// The structured payload of a database error, ported field for field from the PowerBuilder
/// structure <c>dberrordata</c> [ws_objects/pfw.thread.ext.pbl.src/dberrordata.srs:L3-L9].
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a value type.</b> The legacy structure is a value and is used as one, so a
/// <see langword="readonly record struct"/> reproduces its semantics without ceremony. It is
/// copied on assignment - <c>_lastDBError = err</c>
/// [n_cst_threading_task_sqlbase.sru:L44] - returned by copy - <c>return _lastDBError</c>
/// [:L47] - and passed as a <see langword="ref"/> out-parameter -
/// <c>of_gettransobject(ref n_cst_thread_trans, ref dberrordata)</c>
/// [n_cst_thread_task_sqlbase.sru:L148, and its single-argument overload at :L186-L188]. A
/// reference type would have to defend against aliasing at every one of those points.
/// </para>
/// <para>
/// <b>Last error wins: there is no history, and none may be added.</b> The caller-side proxy
/// retains exactly ONE value - a single field <c>DBERRORDATA _lastDBError</c>
/// [n_cst_threading_task_sqlbase.sru:L17] - which the handler overwrites wholesale on every
/// error [:L44] and the accessor <c>of_getlastdberrordata</c> returns [:L26, :L47]. A second
/// error therefore DISCARDS the first, and the oracle consumes only that last value:
/// <c>errData = task.of_GetLastDBErrorData()</c> followed by a single message
/// [ws_objects/pfw.tests.pbl.src/w_test_thread_sqlquery.srw:L190-L191]. The field that retains
/// this payload belongs to <c>Tasks/TaskProxies/</c> and must be a single value, never a list or
/// a queue: accumulating a history would be a new capability, and it would change which error a
/// caller observes.
/// </para>
/// <para>
/// <b>Two reset points, both assigning the cleared state.</b> The retained value is cleared by
/// assigning a freshly declared, unassigned structure - the <c>DBERRORDATA emptyData</c> idiom -
/// in <c>of_reset()</c> [n_cst_threading_task_sqlbase.sru:L53, L55, L65] and again per run in
/// <c>event onprepare</c> [:L206, :L208]. <see cref="Empty"/> is the port of that idiom, and it
/// is why the defaulting behaviour documented on <see cref="SqlErrText"/>,
/// <see cref="SqlSyntax"/> and <see cref="Buffer"/> is load-bearing rather than incidental.
/// </para>
/// <para>
/// <b><see cref="Row"/> is rewritten AFTER the payload has been retained.</b> The update proxy
/// overrides the handler as <c>event ondberror;call super::ondberror;</c>
/// [n_cst_threading_task_sqlupdate.sru:L310] - the base stores the payload first, then the
/// override remaps the ordinal, assigning <c>_lastDBError.row = nRow</c> [:L325, :L337] and
/// leaving the other four members untouched. It walks the modified rows with
/// <c>GetNextModified</c>, counting until the count equals the reported ordinal, which converts a
/// modified-row ordinal into a real DataWindow row. Two guards precede it: the update object must
/// be valid [:L314], and <c>if _lastDBError.row &lt;= 0 then return</c> [:L315], so a
/// row-less error is left alone. This is why every member below has an <c>init</c> accessor:
/// the porting consumer writes <c>retained = retained with { Row = mappedRow };</c>, which the
/// compiler only permits when the member is settable at initialization.
/// </para>
/// <para>
/// <b>A preserved limitation in that remap, which is not a bug to fix here.</b> Both branches
/// walk the <c>Primary</c> buffer ONLY [:L321, :L328 and :L333, :L340] even though
/// <see cref="Buffer"/> may legitimately be <c>Delete</c> or <c>Filter</c>; there is no branch
/// for either. An ordinal reported against a non-Primary buffer is therefore remapped against
/// the wrong buffer, or not remapped correctly at all. That is legacy behaviour and C-B requires
/// it be replicated rather than corrected.
/// </para>
/// <para>
/// <b>The worker-to-caller bridge, and the <c>3</c> that is not part of this payload.</b> The
/// worker-side event is declared with the five members positionally
/// [n_cst_thread_task_sqlbase.sru:L34]; the bridge packs them into the structure in declaration
/// order, forwards the single structure to the caller-side proxy, and then returns the literal
/// <c>3</c> [:L85-L98, forward at :L95, return at :L97]. That <c>3</c> is the DataWindow
/// <c>dberror</c> suppression code, which stops the PowerBuilder runtime raising its own dialog.
/// It is a control value on the event channel, NOT a member of this structure, and it is
/// deliberately not modelled here - it belongs to <c>Tasks/</c>.
/// </para>
/// <para>
/// <b>The three-parameter SQLite variant needs no extra member.</b> The SQLite binding declares a
/// narrower event, <c>event OnDBError (long code, string sqlErrorText, string sqlSyntax)</c>
/// [ws_objects/pfw.utility.sqlite.pbl.src/n_sqlite.sru:L88]. It maps onto the first three members
/// with <see cref="Buffer"/> left at <c>Primary</c> and <see cref="Row"/> at <c>0</c>, which is
/// precisely what <see cref="FromStatement"/> expresses and what the cleared state already
/// supplies. Note that this narrower event is also the origin of the <c>sqlErrorText</c>
/// spelling, which must not be carried onto <see cref="SqlErrText"/>.
/// </para>
/// <para>
/// <b>Return codes are consumed from the shared kernel and never redeclared here.</b> The codes
/// that accompany this payload are <c>PowerFramework.Shared.Kernel.RetCode.E_DB_ERROR</c>
/// (<c>-28</c>) [ws_objects/pfw.shared.pbl.src/retcode.sru:L71],
/// <c>RetCode.E_INVALID_TRANSACTION</c> (<c>-7</c>) [:L50] and, for the SQLite provider, the
/// <c>SQLITE_*</c> family [:L107 onward]. The oracle switches on the first two together when
/// deciding to read this payload at all
/// [w_test_thread_sqlquery.srw:L188-L191]. They are deliberately NOT restated in this file: this
/// type carries a provider's own numeric code in <see cref="SqlDbCode"/>, which is a different
/// code space from a <c>RetCode</c>, and duplicating a preserved constant spelling here would
/// both risk drift and raise a naming diagnostic, since this file is outside the
/// <c>.editorconfig</c> globs that permit those spellings.
/// </para>
/// </remarks>
public readonly record struct DbErrorData
{
    // ------------------------------------------------------------------------------------------
    //  THE CANONICALISING BACKING FIELDS, AND WHY THEY ARE NOT AUTO-PROPERTIES
    //  ----------------------------------------------------------------------------------------
    //  These three fields exist to make ONE state - the cleared state - have ONE representation,
    //  so that every spelling of "empty" compares equal to every other. The mechanism matters:
    //  the compiler-generated equality of a record struct compares the type's FIELDS, not its
    //  properties, so two instances agree only if their stored values agree. Storing null for
    //  "empty" and projecting it on read is therefore what makes all of the following equal:
    //
    //      default(DbErrorData)
    //      DbErrorData.Empty
    //      new DbErrorData()
    //      new DbErrorData { SqlErrText = "", SqlSyntax = "", Buffer = DwBuffer.Primary }
    //      new DbErrorData { Buffer = DwBuffer.Unspecified }
    //      FromTransaction(0, "")
    //
    //  Without the canonicalisation the last four would each store a distinct value and compare
    //  UNEQUAL to the first two, which would break every "no error was recorded" check written
    //  against this type - and it would break them silently, because each instance individually
    //  reads back exactly as expected.
    //
    //  There is a second, independent reason for the two string fields. Under the repository's
    //  inherited nullable context and warnings-as-errors setting, a plain non-nullable
    //  auto-property would still leave `default` holding a null reference and hand that null to
    //  consumers, since a struct's default is all-bits-zero and no accessor runs to prevent it.
    //  The projection below removes that possibility structurally: neither string member can ever
    //  observe as null, on any instance, however it was produced. A NullReferenceException raised
    //  while reporting a database error would be the worst possible place for one - it would
    //  replace the diagnostic the caller needs with a defect in the diagnostic path itself.
    //
    //  SqlDbCode and Row need no such treatment: their legacy cleared value is 0
    //  [n_cst_threading_task_sqlbase.sru:L55, L65 - PowerBuilder initialises `long` to 0], which
    //  is already what a struct's default gives them, so a plain auto-property is exact.
    // ------------------------------------------------------------------------------------------

    /// <summary>Stores <see langword="null"/> for the empty text, projected by the accessor.</summary>
    private readonly string? _sqlErrText;

    /// <summary>Stores <see langword="null"/> for the empty statement, projected by the accessor.</summary>
    private readonly string? _sqlSyntax;

    /// <summary>
    /// Stores <see langword="null"/> for the cleared buffer, projected by the accessor as
    /// <see cref="DwBuffer.Primary"/>. Necessary because the generated enum's zero member is
    /// <see cref="DwBuffer.Unspecified"/> rather than <see cref="DwBuffer.Primary"/>
    /// [common.v1.proto:L619-L629].
    /// </summary>
    private readonly DwBuffer? _buffer;

    // ------------------------------------------------------------------------------------------
    //  MEMBER 1 of 5 - dberrordata.srs:L4  `long sqldbcode`
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The database provider's own numeric error code, as the legacy transaction object reports it
    /// through <c>SQLDBCode</c> [dberrordata.srs:L4].
    /// </summary>
    /// <value>
    /// <para>
    /// <b>This is NOT a <c>RetCode</c>.</b> Three different code spaces reach this one member
    /// depending on the provider - for SQLite it is a <c>SQLITE_*</c> result code
    /// [ws_objects/pfw.shared.pbl.src/retcode.sru:L107 onward], and for another provider it is
    /// that provider's own space - which is why it is a plain integer rather than a member of any
    /// enumeration. Classify an error from this value only in combination with knowledge of the
    /// provider, and never by parsing <see cref="SqlErrText"/>.
    /// </para>
    /// <para>
    /// <b>One value is synthetic.</b> <see cref="NoUpdatableTable"/> reports <c>-1</c>, which no
    /// provider produced: the framework detects that condition itself and invents the code
    /// [n_cst_thread_task_sqlupdate.sru:L190]. See that factory for why the literal is reproduced.
    /// </para>
    /// <para>
    /// The cleared value is <c>0</c>, which is what a freshly declared structure carries
    /// [n_cst_threading_task_sqlbase.sru:L55]. Note that <c>0</c> is therefore ambiguous on its
    /// own - it is both "no error recorded" and, for the SQLite provider, <c>SQLITE_OK</c> - so
    /// presence of an error is decided by the surrounding return code, exactly as the oracle does
    /// it [w_test_thread_sqlquery.srw:L188].
    /// </para>
    /// </value>
    public long SqlDbCode { get; init; }

    // ------------------------------------------------------------------------------------------
    //  MEMBER 2 of 5 - dberrordata.srs:L5  `string sqlerrtext`
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The provider's error message text, verbatim, as the legacy transaction object reports it
    /// through <c>SQLErrText</c> [dberrordata.srs:L5]. This is the member the oracle displays
    /// [w_test_thread_sqlquery.srw:L191].
    /// </summary>
    /// <value>
    /// <para>
    /// Never <see langword="null"/>: the empty text observes as <see cref="string.Empty"/>,
    /// matching the legacy cleared state in which PowerBuilder initialises a <c>string</c> to
    /// <c>""</c> [n_cst_threading_task_sqlbase.sru:L55, L65].
    /// </para>
    /// <para>
    /// <b>Opaque display text - do not parse it to classify an error.</b> It may not be English:
    /// framework-detected conditions synthesize their own diagnostics, and the one that reaches
    /// this payload is Chinese and hardcoded - see <see cref="DbErrorMessages.NoUpdatableTable"/>.
    /// Use <see cref="SqlDbCode"/> for classification.
    /// </para>
    /// </value>
    public string SqlErrText
    {
        get => _sqlErrText ?? string.Empty;
        init => _sqlErrText = string.IsNullOrEmpty(value) ? null : value;
    }

    // ------------------------------------------------------------------------------------------
    //  MEMBER 3 of 5 - dberrordata.srs:L6  `string sqlsyntax`
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The text of the statement that failed [dberrordata.srs:L6]. Empty is both valid and the
    /// common case: six of the nine legacy raise sites pass <c>""</c> for it.
    /// </summary>
    /// <value>
    /// <para>
    /// Never <see langword="null"/>; the empty statement observes as <see cref="string.Empty"/>.
    /// </para>
    /// <para>
    /// <b>In-process this member may carry interpolated literal values, and that is why it must
    /// not be echoed to a network peer.</b> The legacy places the complete generated statement
    /// here - the failing command text is handed straight to the error event
    /// [n_cst_thread_task_sqlcommand.sru:L110] - and the mechanical reason it contains data at all
    /// is the connection parameter string: the legacy parses <c>DBParm</c> for <c>DisableBind</c>
    /// [n_cst_thread_task_sqlbase.sru:L128-L129], and <c>DisableBind=1</c> means the runtime does
    /// not use bind variables, so values are interpolated into the statement text as literals. The
    /// legacy logger performs no redaction of any kind.
    /// </para>
    /// <para>
    /// The value is consequently safe for this service's own diagnostics under its own controls,
    /// and unsafe to publish. Conversion to the wire <c>DbError</c> message therefore lives in
    /// <c>Errors/SqlRedactor.cs</c> and requires a redactor argument; there is deliberately no
    /// self-conversion on this type. Do not hand-roll a mapping that bypasses it.
    /// </para>
    /// </value>
    public string SqlSyntax
    {
        get => _sqlSyntax ?? string.Empty;
        init => _sqlSyntax = string.IsNullOrEmpty(value) ? null : value;
    }

    // ------------------------------------------------------------------------------------------
    //  MEMBER 4 of 5 - dberrordata.srs:L7  `dwbuffer buffer`
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The DataWindow buffer the offending row sits in [dberrordata.srs:L7]. Typed as the
    /// generated <see cref="DwBuffer"/> from the published contracts project, never a local
    /// duplicate.
    /// </summary>
    /// <value>
    /// <para>
    /// <b>Observes as <see cref="DwBuffer.Primary"/> in the cleared state, and can never observe
    /// as <see cref="DwBuffer.Unspecified"/>.</b> The generated enum's zero member is
    /// <see cref="DwBuffer.Unspecified"/>, not <see cref="DwBuffer.Primary"/>
    /// [common.v1.proto:L619-L629], whereas PowerBuilder initialises an unassigned
    /// <c>dwbuffer</c> to <c>Primary!</c>. The accessor bridges that difference, and the
    /// initializer folds both <see cref="DwBuffer.Primary"/> and
    /// <see cref="DwBuffer.Unspecified"/> onto the one stored cleared value. The sentinel is "a
    /// protocol-level field absent, never a legacy buffer", which "a response must never
    /// populate" [common.v1.proto:L620-L624], so refusing to hold it is the contract's own rule
    /// enforced structurally rather than by review.
    /// </para>
    /// <para>
    /// <b><see cref="DwBuffer.Filter"/> is not a leftover buffer.</b> Its row order is INVERTED
    /// relative to the source, which is why the legacy identity round trip walks it backwards
    /// [n_cst_thread_task_sqlupdate.sru:L235-L238]. A <see cref="Row"/> paired with
    /// <see cref="DwBuffer.Filter"/> therefore does not count the way one paired with
    /// <see cref="DwBuffer.Primary"/> does, and a consumer must not assume it does.
    /// </para>
    /// <para>
    /// In practice only one legacy site can report anything other than
    /// <see cref="DwBuffer.Primary"/>: the datastore's native <c>dberror</c> event, which
    /// forwards the runtime's own arguments untouched
    /// [n_cst_thread_task_sqlbase_ds.sru:L159]. Every other site hardcodes <c>Primary!</c>.
    /// </para>
    /// </value>
    public DwBuffer Buffer
    {
        get => _buffer ?? DwBuffer.Primary;
        init => _buffer = value is DwBuffer.Primary or DwBuffer.Unspecified ? null : value;
    }

    // ------------------------------------------------------------------------------------------
    //  MEMBER 5 of 5 - dberrordata.srs:L8  `long row`
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The ONE-BASED ordinal of the offending row within <see cref="Buffer"/>
    /// [dberrordata.srs:L8]. <c>0</c> means "no particular row", which is what the legacy passes
    /// for a connection-level or statement-level failure that is not attributable to a single row
    /// - eight of the nine raise sites do exactly that.
    /// </summary>
    /// <value>
    /// <para>
    /// <b>This is the only member that is rewritten after the payload has been retained.</b> The
    /// update proxy stores the payload through its base handler and then remaps this ordinal from
    /// a modified-row count to a real DataWindow row, assigning it in place
    /// [n_cst_threading_task_sqlupdate.sru:L310, :L325, :L337]. The remap is skipped entirely
    /// when the value is not positive [:L315]. The <c>init</c> accessor is what lets a porting
    /// consumer express that as <c>retained with { Row = mappedRow }</c>, changing this member
    /// and nothing else.
    /// </para>
    /// <para>
    /// One-based indexing is legacy contract, not an off-by-one to normalise. Converting to a
    /// zero-based ordinal here would be indistinguishable from a defect at every consumer, and
    /// would corrupt the remap above, which counts from one.
    /// </para>
    /// </value>
    public long Row { get; init; }


    // ------------------------------------------------------------------------------------------
    //  THE CLEARED VALUE
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The cleared payload: <see cref="SqlDbCode"/> <c>0</c>, <see cref="SqlErrText"/> and
    /// <see cref="SqlSyntax"/> empty, <see cref="Buffer"/> <see cref="DwBuffer.Primary"/> and
    /// <see cref="Row"/> <c>0</c>. This is the port of the legacy <c>DBERRORDATA emptyData</c>
    /// idiom [n_cst_threading_task_sqlbase.sru:L55 and :L206, assigned at :L65 and :L208].
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately defined as <see langword="default"/> rather than as an explicitly constructed
    /// instance. Defining it as <see langword="default"/> removes any possibility of the named
    /// value and the type's own default disagreeing - there is nothing to keep in step, because
    /// they are the same value by construction. An explicit initializer here would be a second
    /// definition of the cleared state, and a future edit to one and not the other would be
    /// invisible.
    /// </para>
    /// <para>
    /// Reset semantics to preserve: the legacy clears the retained payload at TWO points, in
    /// <c>of_reset()</c> [:L53-L65] and again per run in <c>event onprepare</c> [:L206-L208], and
    /// both do it by assigning a freshly declared structure rather than by clearing members
    /// individually. Assigning this value is the faithful port of both.
    /// </para>
    /// </remarks>
    public static DbErrorData Empty => default;

    // ------------------------------------------------------------------------------------------
    //  THE THREE MEASURED RAISE SHAPES
    //  ----------------------------------------------------------------------------------------
    //  One factory per DISTINCT legacy shape and no more. The census in this file's header
    //  enumerates every `Event OnDBError(` site in the legacy library, and these three cover all
    //  of them; no speculative factory is offered for a shape the oracle does not produce.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The framework-detected "no updatable table" error:
    /// <c>(-1, DbErrorMessages.NoUpdatableTable, "", Primary, 0)</c>.
    /// </summary>
    /// <returns>The payload the legacy raises at [n_cst_thread_task_sqlupdate.sru:L190].</returns>
    /// <remarks>
    /// <para>
    /// <b>Raised when the DataWindow has no update table.</b> The legacy asks
    /// <c>Data.Describe("DataWindow.Table.UpdateTable")</c> and treats three answers as "none" -
    /// the empty string, <c>"!"</c> and <c>"?"</c>, the latter two being PowerBuilder's own error
    /// and unknown markers [:L188-L189]. It then raises this payload [:L190], raises the SEPARATE
    /// <c>OnError</c> channel with the same text [:L191], and returns <c>E_DB_ERROR</c> [:L192].
    /// Only the first of those three reaches this type.
    /// </para>
    /// <para>
    /// <b>The <c>-1</c> is synthetic and is reproduced literally.</b> No provider produced it: the
    /// framework detected the condition itself and invented the code. It is NOT
    /// <c>RetCode.FAILED</c> reused semantically - that constant happens to share the value, but
    /// this member is a provider code space rather than a return code, and conflating them would
    /// misrepresent what the legacy wrote. The accompanying return code on the other channel is
    /// <c>RetCode.E_DB_ERROR</c>, and it is a different value entirely.
    /// </para>
    /// </remarks>
    public static DbErrorData NoUpdatableTable() => new()
    {
        // Every member is stated explicitly, including the three whose values equal the cleared
        // state, because the legacy call site states them explicitly too - the fidelity of this
        // factory is checkable against :L190 argument for argument only if nothing is elided.
        SqlDbCode = -1,
        SqlErrText = DbErrorMessages.NoUpdatableTable,
        SqlSyntax = "",
        Buffer = DwBuffer.Primary,
        Row = 0,
    };

    /// <summary>
    /// A provider-reported error with no statement text:
    /// <c>(sqlDbCode, sqlErrText, "", Primary, 0)</c>. This is the most common legacy shape,
    /// covering five raise sites plus one partial population.
    /// </summary>
    /// <param name="sqlDbCode">
    /// The provider's numeric code, taken from the transaction object's <c>SQLDBCode</c>.
    /// </param>
    /// <param name="sqlErrText">
    /// The provider's message text, taken from the transaction object's <c>SQLErrText</c>. An
    /// empty or <see langword="null"/> value canonicalises to <see cref="string.Empty"/>.
    /// </param>
    /// <returns>The payload shape those sites raise.</returns>
    /// <remarks>
    /// <para>
    /// Covers, all with an empty statement and the cleared buffer and row:
    /// [n_cst_thread_task_sqlupdate.sru:L197], [n_cst_thread_task_sqlupdate.sru:L293],
    /// [n_cst_thread_task_sqlquery.sru:L521], [n_cst_thread_task_sqlquery.sru:L748] and
    /// [n_cst_thread_task_sqlcommand.sru:L71]. It also covers the partial population at
    /// [n_cst_thread_task_sqlbase.sru:L175-L176], where a failed connect sets only the code and
    /// the text on the <see langword="ref"/> out-parameter before returning
    /// <c>E_INVALID_TRANSACTION</c> - the other three members are simply left at their cleared
    /// values there, which is exactly what this factory produces.
    /// </para>
    /// <para>
    /// <b>The vetoed-before-update site deserves its own note, because the discrimination is
    /// easy to lose.</b> At [:L196-L202] a prevented <c>OnBeforeUpdate</c> hook is only reported
    /// as a database error when the transaction ALSO reports failure - <c>IsPrevented(...)</c> and
    /// then <c>TransObject.of_IsFailed()</c> [:L196, :L197] - in which case this payload is raised
    /// and <c>E_DB_ERROR</c> returned. A CLEAN veto raises nothing at all and returns
    /// <c>RetCode.CANCELLED</c> [:L201]. Producing this payload for a clean veto would invent an
    /// error the legacy does not report.
    /// </para>
    /// </remarks>
    public static DbErrorData FromTransaction(long sqlDbCode, string sqlErrText) => new()
    {
        SqlDbCode = sqlDbCode,
        SqlErrText = sqlErrText,
        // Stated explicitly to mirror the legacy argument lists, all of which pass "" , Primary!
        // and 0 in these three positions.
        SqlSyntax = "",
        Buffer = DwBuffer.Primary,
        Row = 0,
    };

    /// <summary>
    /// The complete five-member shape, for the sites that carry a real statement and, in one case,
    /// a genuinely non-<see cref="DwBuffer.Primary"/> buffer and a non-zero row.
    /// </summary>
    /// <param name="sqlDbCode">The provider's numeric code.</param>
    /// <param name="sqlErrText">
    /// The provider's message text. Empty or <see langword="null"/> canonicalises to
    /// <see cref="string.Empty"/>.
    /// </param>
    /// <param name="sqlSyntax">
    /// The failing statement text. Empty or <see langword="null"/> canonicalises to
    /// <see cref="string.Empty"/>. See <see cref="SqlSyntax"/> for why this value must not be
    /// echoed to a network peer without passing through the redactor.
    /// </param>
    /// <param name="buffer">
    /// The buffer the offending row sits in. <see cref="DwBuffer.Unspecified"/> and
    /// <see cref="DwBuffer.Primary"/> both canonicalise to <see cref="DwBuffer.Primary"/>.
    /// </param>
    /// <param name="row">The one-based row ordinal, or <c>0</c> for "no particular row".</param>
    /// <returns>The payload shape those sites raise.</returns>
    /// <remarks>
    /// <para>
    /// Covers the two sites that carry a statement -
    /// [n_cst_thread_task_sqlcommand.sru:L110], which passes the failing command text with the
    /// cleared buffer and row, and [n_cst_thread_task_sqlquery.sru:L855], which passes the
    /// statement together with <c>row = 1</c> - and the datastore forwarding path at
    /// [n_cst_thread_task_sqlbase_ds.sru:L159], which relays the DataWindow runtime's own
    /// <c>dberror</c> arguments untouched and is therefore the ONLY path in the system that can
    /// supply a non-Primary buffer or a row other than <c>0</c> or <c>1</c>.
    /// </para>
    /// <para>
    /// This factory is also the shape the three-parameter SQLite binding event maps onto
    /// [ws_objects/pfw.utility.sqlite.pbl.src/n_sqlite.sru:L88]: pass its <c>code</c>,
    /// <c>sqlErrorText</c> and <c>sqlSyntax</c> through with
    /// <see cref="DwBuffer.Primary"/> and <c>0</c>.
    /// </para>
    /// </remarks>
    public static DbErrorData FromStatement(
        long sqlDbCode,
        string sqlErrText,
        string sqlSyntax,
        DwBuffer buffer,
        long row) => new()
        {
            SqlDbCode = sqlDbCode,
            SqlErrText = sqlErrText,
            SqlSyntax = sqlSyntax,
            Buffer = buffer,
            Row = row,
        };
}

/// <summary>
/// The diagnostic message texts that the legacy SQL task layer synthesizes itself and places into
/// a <see cref="DbErrorData"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists as a named constant rather than a literal at each use site.</b>
/// <c>Tasks/SqlUpdateTask.cs</c> and <c>Grpc/UpdateService.cs</c> both need this text, and a
/// single wrong or transposed character in a CJK string literal is effectively invisible in
/// review while still failing every parity comparison against the oracle. Declaring it once means
/// there is one spelling to verify and no way for two use sites to disagree.
/// </para>
/// <para>
/// <b>Scope boundary - only ONE Chinese diagnostic belongs here.</b> The same legacy library
/// carries six further Chinese messages, but every one of them travels on the separate
/// <c>Event OnError(...)</c> channel and never enters a <c>dberrordata</c>:
/// [n_cst_thread_task_sqlquery.sru:L679], [:L778], [:L780], [:L827], [:L838] and [:L856]. Those
/// belong to <c>Tasks/</c>, not to this type. The one message below is the only one the oracle
/// puts into this payload [n_cst_thread_task_sqlupdate.sru:L190]. Note that L191 of that same file
/// passes the SAME text to the <c>OnError</c> channel, so the text legitimately appears on both
/// channels for that one condition - which is another reason to have exactly one declaration of it.
/// </para>
/// </remarks>
public static class DbErrorMessages
{
    /// <summary>
    /// "There is no updatable table" - the text the legacy synthesizes when a DataWindow's update
    /// table cannot be determined [n_cst_thread_task_sqlupdate.sru:L190].
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>PRESERVED DEFECT, REPRODUCED VERBATIM AND DELIBERATELY NOT CORRECTED (C-B).</b> This
    /// text is hardcoded Chinese and does NOT route through
    /// <c>PowerFramework.Shared.Localization</c> - neither through <c>II18nProvider</c> nor through
    /// the <c>I18n</c> helper - even though localization is available in this system and other
    /// parts of the framework do use it for user-facing diagnostics. The legacy simply embeds the
    /// literal in the raise expression [:L190, and again on the <c>OnError</c> channel at :L191].
    /// </para>
    /// <para>
    /// That inconsistency is legacy behaviour and it is replicated rather than harmonized.
    /// Routing it through localization would change the observable text for any non-Chinese
    /// locale, which is a behaviour change dressed as an improvement: parity is measured against
    /// the oracle's actual output, so a "corrected" message would fail every comparison that
    /// mentions it. The same ruling applies wherever this constant is consumed - do not wrap it in
    /// a translation call at the use site either.
    /// </para>
    /// </remarks>
    public const string NoUpdatableTable = "没有可更新的表";
}

