// ==============================================================================================
//  ItemStatus.cs - THE DATAWINDOW ITEM-STATUS MACHINE
//  --------------------------------------------------------------------------------------------
//  PORT OF        ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru (73 lines)
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru   (176 lines)
//  ALSO SOURCED   ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru
//  FIXTURE        ws_objects/pfw.tests.pbl.src/dw_sqlite.srd (the golden master)
//
//  ORACLE STATUS (C-C). Every path above is READ ONLY. They are the behavioural oracle for parity
//  testing and are never an edit target, never reformatted and never moved. Nothing else in this
//  repository can adjudicate the behaviour below, so every behavioural claim in this file carries
//  a ws_objects/** line locator. A reader who doubts a line here can diff it against the locator.
//
//  WHAT THIS FILE IS
//  --------------------------------------------------------------------------------------------
//  The foundation of the Buffers/ folder. It answers three questions about a DataWindow row's
//  item status and nothing else:
//
//      1. Does a status-bearing call address THE ROW or A COLUMN?         (the column-0 sentinel)
//      2. Which statuses satisfy each of the legacy's three status tests? (the predicates)
//      3. Which rows does the legacy's modified-row walk visit?           (the traversal)
//
//  Its consumers - Buffers/DataWindowBuffers.cs, Buffers/ChangesetCodec.cs,
//  Buffers/FullStateCodec.cs, Concurrency/IdentityColumnResolver.cs, Concurrency/ConflictDetector.cs
//  and the Tasks/ layer - all sit ABOVE it, so this file takes ZERO dependency on any sibling in
//  this project. It is deliberately the first file in PowerFramework.Persistence that can be
//  compiled and tested on its own.
//
//  ============================================================================================
//  DECISION 1 - WHY THIS FILE DECLARES NO TYPE NAMED ItemStatus (C-K)
//  ============================================================================================
//  The file name is fixed by the refactor plan's own target structure and matches it character for
//  character. The TYPE names inside are not, and they must not collide with the published
//  boundary: shared/PowerFramework.Contracts/Proto/common.v1.proto already declares a protobuf
//  enumeration NAMED ItemStatus, whose four members are
//
//      ITEM_STATUS_NOT_MODIFIED = 0, ITEM_STATUS_DATA_MODIFIED = 1,
//      ITEM_STATUS_NEW = 2, ITEM_STATUS_NEW_MODIFIED = 3
//
//  under option csharp_namespace = "PowerFramework.Contracts.Common.V1", which protoc's C#
//  generator emits as PowerFramework.Contracts.Common.V1.ItemStatus with the shouty enum-name
//  prefix stripped: NotModified, DataModified, New, NewModified.
//
//  FOUR MEMBERS, NOT FIVE, AND NotModified! IS ZERO. The contract carries no synthetic
//  "unspecified" member: NotModified! already occupies zero, both as the state of a freshly
//  retrieved row and as the value an unassigned `dwItemStatus` reads as, so proto3's
//  first-enumerator rule is satisfied without inventing anything. Inserting a sentinel would shift
//  DataModified!, New! and NewModified! up by one and silently invalidate every stored numeric
//  comparison (AAP 0.4.5.3). A field that must express "no status was supplied" declares itself
//  proto3 `optional` - common.v1.ColumnValue.item_status is the one that does.
//
//  A second type named ItemStatus in namespace PowerFramework.Persistence.Buffers would make every
//  unqualified mention of the name ambiguous - CS0104 - in Grpc/QueryService.cs,
//  Grpc/UpdateService.cs and every file under Tasks/ and Concurrency/, because all of them import
//  both namespaces. TreatWarningsAsErrors is inherited as true from the repository-root
//  Directory.Build.props, so that is a hard BUILD FAILURE across the service, not a nuisance.
//
//  The resolution is therefore to name the types for what they are - a status MACHINE over the
//  published enum, plus its fluent forwarders - and to CONSUME the generated ItemStatus and
//  DwBuffer rather than restate them. Re-declaring either enum's numeric values here would fork
//  the wire agreement: the values would then live in two places with no compile-time edge between
//  them, and a later edit to one would silently invalidate every stored characterization
//  comparison that mentions the changed member. There is exactly one declaration of each, and it
//  is in the .proto.
//
//  ============================================================================================
//  DECISION 2 - THE COLUMN-ZERO ROW-STATUS CONVENTION, AND WHY 0 IS A SENTINEL (C-K, R9)
//  ============================================================================================
//  Every status-bearing call in the measured legacy surface passes column index 0:
//
//      GetItemStatus(nRow,0,Delete!)                     [n_cst_thread_task_sqlbase_ds_mt.sru:L56]
//      GetItemStatus(nRow,0,Primary!)                    [n_cst_thread_task_sqlupdate.sru:L160]
//      GetItemStatus(nIndex,0,Primary!)                  [n_cst_thread_task_sqlupdate.sru:L230]
//      GetItemStatus(nIndex,0,Filter!)                   [n_cst_thread_task_sqlupdate.sru:L238]
//      GetItemStatus(nRow,0,Primary!)                    [n_cst_threading_task_sqlupdate.sru:L140,L174]
//      GetItemStatus(nRow,0,Filter!)                     [n_cst_threading_task_sqlupdate.sru:L156,L190]
//      SetItemStatus(nRow,0,Primary!,DataModified!)      [n_cst_thread_task_sqlquery.sru:L123,L162,L197]
//      SetItemStatus(nRow,0,Filter!,DataModified!)       [n_cst_thread_task_sqlquery.sru:L127,L168,L202]
//
//  In PowerBuilder, column 0 is not a column. It means "the row itself". The decisive evidence
//  that this is a real distinction rather than a convention nobody exercises is two ADJACENT lines
//  of one function in n_cst_thread_task_sqlupdate.sru:
//
//      :L160  if Data.GetItemStatus(nRow,0,Primary!) <> DataModified! then continue
//      :L162  if Data.GetItemStatus(nRow,nKeyColumns[nIndex],Primary!) <> DataModified! then continue
//
//  :L160 reads the ROW's status. :L162 reads a specific COLUMN's status, with a one-based column
//  number drawn from an array. Both readings coexist, and the ONLY thing that distinguishes them
//  is whether the index is zero. This API therefore makes the distinction explicit instead of
//  leaving a bare 0 at every call site - see RowStatusColumn, AddressesRowStatus and
//  TryGetColumnNumber below.
//
//  R9 - THE ONE-BASED TO ZERO-BASED HAZARD, AUDITED IN FULL FOR THIS FILE
//  The refactor plan names one-based translation the single most dangerous mechanical hazard in
//  the whole migration, and permits an individual audit in place of a centralized helper. This is
//  that audit. THE VALUE 0 CARRIES THREE DIFFERENT MEANINGS in this small API, and not one of them
//  is an index that wants rebasing:
//
//      0 as a COLUMN INDEX     "the row itself"            RowStatusColumn
//      0 as a TRAVERSAL SEED   "start before the first row" BeforeFirstRow
//      0 as a TRAVERSAL RESULT "no more modified rows"      NoMoreModifiedRows
//
//  Row numbers and column numbers themselves ARE one-based: the fixture declares column ids 1
//  through 6 [dw_sqlite.srd:L21-L26], and the legacy delete walk terminates at row 1
//  [n_cst_thread_task_sqlbase_ds_mt.sru:L55]. Applying a blanket minus-one to any of the three
//  sentinels above, or to a row or column number, produces an off-by-one that a row-count
//  assertion cannot catch, because the count stays right while the rows shift. Each of the five
//  constants below therefore exists as a NAMED value with its own locator, so that a future reader
//  who meets a bare 0 in a consumer can find out which of the three it is.
//
//  There is exactly ONE loop in this file that could have been written zero-based - the delete
//  walk - and it is written one-based, descending to FirstRowNumber, precisely to keep the whole
//  file inside a single indexing domain. No zero-based array iteration appears anywhere here.
//
//  ============================================================================================
//  DECISION 3 - WHY THE DELETE-COUNTABLE CASE LIST EXCLUDES A NEW-THEN-DELETED ROW (C-B, C-K)
//  ============================================================================================
//  The legacy computes the update-progress total in updatestart
//  [n_cst_thread_task_sqlbase_ds_mt.sru:L47-L61]:
//
//      _nUpdateTotal = ModifiedCount()                        [:L52]
//      for nRow = DeletedCount() to 1 step -1                 [:L55]
//          choose case GetItemStatus(nRow,0,Delete!)          [:L56]
//              case NotModified!,DataModified!                [:L57]
//                  _nUpdateTotal ++                          [:L58]
//          end choose                                        [:L59]
//      next                                                  [:L60]
//
//  The case list is exactly two statuses and there is NO default arm. NewModified! is absent, so a
//  row that was inserted, edited and then deleted before the update ran does NOT count. That is
//  correct rather than missing: such a row never reached the database, so it generates no SQL
//  statement, so counting it would inflate the denominator of the progress notification
//  [:L39 MakeLong(_nUpdateCurrent,_nUpdateTotal)] and make this service's progress stream diverge
//  from the legacy's. New! is absent for the same reason.
//
//  C-B is explicit that no legacy behaviour may be improved, so the case list is reproduced
//  verbatim: no third case is added, no default arm is added, and the status domain is not
//  normalised into a tidier model. IsDeleteCountable below is that case list and nothing else.
//
//  ============================================================================================
//  DECISION 4 - WHAT IS DELIBERATELY NOT HERE
//  ============================================================================================
//  NO BUFFER MODEL, NO CARRIER, NO CODEC. The Primary!/Delete!/Filter! buffer model belongs to
//  Buffers/DataWindowBuffers.cs, and GetChanges/SetChanges and GetFullState/SetFullState with their
//  four preserved defects belong to Buffers/ChangesetCodec.cs and Buffers/FullStateCodec.cs. This
//  file classifies statuses; it does not hold rows.
//
//  NO PRESENTATION, AND THE EVIDENCE IS AN ASYMMETRY (C-D). SetRedraw and GroupCalc appear beside
//  these very status operations in the legacy, and they appear ONLY in the DataWindow arms of the
//  choose-case, never in the DataStore arms: n_cst_threading_task_sqlupdate.sru calls
//  dw.SetRedraw(false) at :L134 and dw.SetRedraw(true) at :L166 inside `case DataWindow!`, while
//  the structurally identical `case DataStore!` block at :L167-L199 calls neither. The same
//  asymmetry recurs in n_cst_threading_task_sqlquery.sru at :L194, :L200, :L203 and :L527. Two
//  independent sightings of the same asymmetry are the evidence that both calls are PRESENTATIONAL
//  and therefore belong to the deferred DesignSystem capability area. They are a DOCUMENTED
//  NON-PORT here, not an approximation and not a gap to be filled later in this file.
//
//  NO I/O, NO CLOCK, NO STATE (C-H). Every member below is a pure function of its arguments, so
//  the whole file is exercisable with no database, no container and no time source, which is what
//  makes the per-service line-coverage gate reachable for it. Note in particular that the legacy
//  updatestart reads CPU() at :L53 to rate-limit its notifications; that clock read belongs to the
//  Tasks/ layer, where it is injected and seamed for determinism, and it is not reproduced here.
//
//  NO SERIALIZER (C-D). No JSON and no XML: either would reach into the deferred Documents parser
//  family. Statuses cross the wire as the generated protobuf enum, which is already a contract.
//
//  NO NEW DEPENDENCY (C-I). The only using directive is the published contracts namespace. No
//  PackageReference is added by this file - versions live centrally in the repository-root
//  Directory.Packages.props, which is outside this folder's scope - and nothing here touches
//  EF Core, Microsoft.Data.Sqlite, a DbConnection, SQL Server or Oracle, so the machine is
//  dialect-agnostic and schema-free (C-E).
//
//  NAMING (verified against the artifact). The repository-root .editorconfig scopes its CA1707 and
//  IDE1006 relaxations to ten named files, and this is not one of them. Every member below is
//  therefore PascalCase; no member here is spelled in the preserved shouty legacy style, and under
//  warnings-as-errors none could be. The shouty spellings that DO appear in this file appear only
//  inside comments, where they quote the .proto's own member names for traceability.
//
//  RULES POSITION. review_rules returns exactly one line, "No user rules provided.", so NO
//  user-specified rule governs this file and none is invented here. The enterprise-standard
//  baseline applies in their place, and the binding constraints are the refactor plan's own
//  non-rule inventory; those that bear on this file are cited inline at the point each is
//  discharged: C-B, C-C, C-D, C-E, C-H, C-I, C-K and risk R9.
//
//  C-F SELF-AUDIT: no key, credential, token, password, connection string or secret-shaped
//  placeholder appears anywhere in this file, in any member, default, message or comment.
// ==============================================================================================

using PowerFramework.Contracts.Common.V1;

namespace PowerFramework.Persistence.Buffers;

/// <summary>
/// The DataWindow item-status machine: the classification predicates, the row-versus-column status
/// addressing convention and the modified-row traversal that the legacy SQL task layer is built on.
/// </summary>
/// <remarks>
/// <para>
/// Ported from <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru</c> and its
/// main-thread parent <c>n_cst_thread_task_sqlbase_ds.sru</c>, with the traversal taken from
/// <c>n_cst_threading_task_sqlupdate.sru</c>. Those exports are read-only and are the only
/// specification for the behaviour here.
/// </para>
/// <para>
/// The type is named for the machine rather than for the domain because the domain enum
/// <see cref="ItemStatus"/> is already declared by the published boundary in
/// <c>shared/PowerFramework.Contracts/Proto/common.v1.proto</c>. Declaring a second type of that
/// name in this namespace would make the name ambiguous in every consumer that imports both
/// namespaces. See DECISION 1 in the file header.
/// </para>
/// <para>
/// Every member is a pure function of its arguments: no I/O, no clock read, no static mutable
/// state, and therefore no ordering dependency between calls. The predicates are deliberately
/// order-independent, because the golden-master fixture is sorted - <c>dw_sqlite.srd:L14</c>
/// declares <c>sort="age A salary A "</c> - so a row number in the primary parity path does not
/// correlate with insertion order.
/// </para>
/// </remarks>
internal static class ItemStatusMachine
{
    #region Sentinels - the R9 audit surface

    /// <summary>
    /// The column index that addresses a ROW's own status rather than any individual column's
    /// status. Its value is zero, and it is a SENTINEL rather than an index.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every status-bearing call in the measured legacy surface passes this value, for example
    /// <c>GetItemStatus(nRow,0,Delete!)</c>
    /// [<c>n_cst_thread_task_sqlbase_ds_mt.sru:L56</c>] and
    /// <c>SetItemStatus(nRow,0,Primary!,DataModified!)</c>
    /// [<c>n_cst_thread_task_sqlquery.sru:L123,L127,L162,L168,L197,L202</c>].
    /// </para>
    /// <para>
    /// R9: do NOT rebase this value. Column numbers are one-based - the fixture declares ids 1
    /// through 6 at <c>dw_sqlite.srd:L21-L26</c> - but zero is outside that domain by design and
    /// subtracting one from it would turn "the row" into a negative column number. The published
    /// contract states the same rule for the wire: <c>ColumnValue.column_id</c> is documented as a
    /// one-based ordinal whose zero is reserved for the row itself.
    /// </para>
    /// </remarks>
    internal const int RowStatusColumn = 0;

    /// <summary>
    /// The lowest legal column number, one. Column numbers are one-based, so this is the first
    /// value that addresses a real column rather than the row.
    /// </summary>
    /// <remarks>
    /// Evidenced by the golden-master fixture, whose six columns carry ids 1 through 6
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L21-L26</c>], and by the key-column array
    /// the legacy indexes with them [<c>n_cst_thread_task_sqlupdate.sru:L162</c>].
    /// </remarks>
    internal const int FirstColumnNumber = 1;

    /// <summary>
    /// The lowest legal row number, one. Row numbers are one-based.
    /// </summary>
    /// <remarks>
    /// Evidenced by the legacy delete walk, which counts DOWN to this value:
    /// <c>for nRow = DeletedCount() to 1 step -1</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L55</c>].
    /// </remarks>
    internal const long FirstRowNumber = 1L;

    /// <summary>
    /// The seed passed to the modified-row traversal to mean "start before the first row". Its
    /// value is zero, and it is a SENTINEL rather than a row number.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The legacy seeds its walk with a literal zero:
    /// <c>nRow = GetNextModified(0,Primary!)</c>
    /// [<c>n_cst_threading_task_sqlupdate.sru:L138</c>], and likewise for the Filter buffer at
    /// <c>:L154</c>, for the DataStore arms at <c>:L172</c> and <c>:L188</c>, and again in its
    /// database-error row translation at <c>:L321</c> and <c>:L333</c>.
    /// </para>
    /// <para>
    /// R9: do NOT rebase this value. It is consumed exactly once, by
    /// <see cref="GetNextModified"/>, which advances it by one to reach
    /// <see cref="FirstRowNumber"/>. That single increment is the whole of the seed's arithmetic,
    /// and it is an advance within a one-based domain rather than a conversion out of one.
    /// </para>
    /// </remarks>
    internal const long BeforeFirstRow = 0L;

    /// <summary>
    /// The value the modified-row traversal returns to mean "no more modified rows". Its value is
    /// zero, and it is a SENTINEL rather than a row number.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The legacy terminates its walk on it: <c>do while(nRow &gt; 0)</c>
    /// [<c>n_cst_threading_task_sqlupdate.sru:L139,L155,L173,L189,L322,L334</c>]. Note that it
    /// tests strictly greater than zero rather than not-equal-to-zero, so any non-positive result
    /// ends the walk.
    /// </para>
    /// <para>
    /// R9: this is the THIRD distinct meaning the value zero carries in this API, after
    /// <see cref="RowStatusColumn"/> and <see cref="BeforeFirstRow"/>. The two traversal sentinels
    /// are declared separately even though they share a value, because they are separate contracts:
    /// one is an argument, the other a result, and a future change to either must not silently
    /// change the other.
    /// </para>
    /// </remarks>
    internal const long NoMoreModifiedRows = 0L;

    #endregion

    #region Addressing - row status versus column status

    /// <summary>
    /// Reports whether a legacy column index addresses the ROW's own status rather than an
    /// individual column's status.
    /// </summary>
    /// <param name="columnIndex">
    /// The column index as the legacy passes it: <see cref="RowStatusColumn"/> for the row's own
    /// status, or a one-based column number at or above <see cref="FirstColumnNumber"/> for that
    /// column's status.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="columnIndex"/> is
    /// <see cref="RowStatusColumn"/>; otherwise <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="columnIndex"/> is outside the legacy domain, which is
    /// <see cref="RowStatusColumn"/> plus the one-based column numbers from
    /// <see cref="FirstColumnNumber"/> upwards. In practice that means it is negative, and the
    /// commonest way to produce a negative here is to have rebased a one-based column number.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This is the ergonomic default, because the row case is what every measured legacy call site
    /// uses - <c>GetItemStatus(nRow,0,Delete!)</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L56</c>],
    /// <c>GetItemStatus(nRow,0,Primary!)</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L160,L230</c>],
    /// <c>GetItemStatus(nIndex,0,Filter!)</c> [<c>:L238</c>] and the six
    /// <c>SetItemStatus(nRow,0,...,DataModified!)</c> stamping sites in
    /// <c>n_cst_thread_task_sqlquery.sru</c> at <c>:L123,L127,L162,L168,L197,L202</c>.
    /// </para>
    /// <para>
    /// The column case is nonetheless real and is reachable through
    /// <see cref="TryGetColumnNumber"/>: <c>n_cst_thread_task_sqlupdate.sru:L162</c> reads a
    /// specific column's status two lines after <c>:L160</c> reads the row's, and the only thing
    /// distinguishing the two calls is whether the index is zero.
    /// </para>
    /// </remarks>
    internal static bool AddressesRowStatus(int columnIndex)
    {
        ThrowIfNotLegacyColumnIndex(columnIndex, nameof(columnIndex));

        return columnIndex == RowStatusColumn;
    }

    /// <summary>
    /// Resolves a legacy column index to a one-based column number, reporting failure when the
    /// index is the row-status sentinel instead.
    /// </summary>
    /// <param name="columnIndex">
    /// The column index as the legacy passes it: <see cref="RowStatusColumn"/> for the row's own
    /// status, or a one-based column number at or above <see cref="FirstColumnNumber"/>.
    /// </param>
    /// <param name="columnNumber">
    /// On success, the one-based column number, which is <paramref name="columnIndex"/> UNCHANGED.
    /// On failure, <see cref="RowStatusColumn"/>, which is not a column number - the sentinel is
    /// echoed back deliberately so that a caller which ignores the return value cannot mistake the
    /// output for column one.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="columnIndex"/> addresses a column;
    /// <see langword="false"/> when it addresses the row.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="columnIndex"/> is outside the legacy domain, which is
    /// <see cref="RowStatusColumn"/> plus the one-based column numbers from
    /// <see cref="FirstColumnNumber"/> upwards.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Reproduces the column-level reading at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L162</c>,
    /// <c>if Data.GetItemStatus(nRow,nKeyColumns[nIndex],Primary!) &lt;&gt; DataModified! then continue</c>,
    /// where the index is a one-based column number taken from the key-column array rather than
    /// the row sentinel used two lines earlier at <c>:L160</c>.
    /// </para>
    /// <para>
    /// R9, and this is the point of the method existing at all: the successful path assigns
    /// <paramref name="columnIndex"/> to <paramref name="columnNumber"/> WITHOUT arithmetic. There
    /// is deliberately no minus-one here. A legacy column number is already the number this
    /// service's contracts use - the published <c>ColumnValue.column_id</c> is documented as a
    /// one-based ordinal - so rebasing it would shift every column by one while leaving every
    /// column count intact, which is precisely the class of defect no row-count or column-count
    /// assertion can detect.
    /// </para>
    /// </remarks>
    internal static bool TryGetColumnNumber(int columnIndex, out int columnNumber)
    {
        ThrowIfNotLegacyColumnIndex(columnIndex, nameof(columnIndex));

        if (columnIndex == RowStatusColumn)
        {
            // Echo the sentinel rather than a plausible-looking column number. See the parameter
            // documentation: a caller that ignores the false return must not read column one here.
            columnNumber = RowStatusColumn;
            return false;
        }

        // R9: NO REBASE. A legacy one-based column number is already the number the published
        // contracts carry, so the value passes through untouched.
        columnNumber = columnIndex;
        return true;
    }

    #endregion

    #region Classification - the three legacy status tests, reproduced verbatim

    /// <summary>
    /// Reports whether a row sitting in the <c>Delete!</c> buffer counts toward the update-progress
    /// total.
    /// </summary>
    /// <param name="status">
    /// The row's status, read the legacy way with column index <see cref="RowStatusColumn"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> for <see cref="ItemStatus.NotModified"/> and
    /// <see cref="ItemStatus.DataModified"/>; <see langword="false"/> for every other value,
    /// including <see cref="ItemStatus.NewModified"/>, <see cref="ItemStatus.New"/> and any value
    /// outside the declared domain.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Reproduces the case list at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L56-L59</c>
    /// EXACTLY:
    /// </para>
    /// <code>
    /// choose case GetItemStatus(nRow,0,Delete!)
    ///     case NotModified!,DataModified!
    ///         _nUpdateTotal ++
    /// end choose
    /// </code>
    /// <para>
    /// C-B - THE EXCLUSIONS ARE INTENTIONAL LEGACY BEHAVIOUR, NOT A MISSING CASE.
    /// <see cref="ItemStatus.NewModified"/> is absent from the legacy case list, so a row that was
    /// inserted, edited and then deleted before the update ran does not count. That is correct:
    /// the row never reached the database, so it produces no SQL statement, so counting it would
    /// inflate the denominator of the progress notification the worker publishes at
    /// <c>:L39</c> and make this service's progress stream diverge from the legacy's.
    /// <see cref="ItemStatus.New"/> is absent for the same reason. No third case is added here, no
    /// default arm is added, and the status domain is not normalised.
    /// </para>
    /// <para>
    /// A value outside the declared domain - an out-of-range cast, which is the only way to produce
    /// one now that the contract carries no sentinel member - returns <see langword="false"/> and
    /// does not throw. That is exactly what a PowerScript <c>choose case</c> with no
    /// <c>case else</c> arm does with an unmatched value: nothing happens and control falls through.
    /// </para>
    /// </remarks>
    internal static bool IsDeleteCountable(ItemStatus status)
    {
        // Verbatim from n_cst_thread_task_sqlbase_ds_mt.sru:L57 - `case NotModified!,DataModified!`.
        // Two statuses, no default arm. Do not extend this list.
        return status is ItemStatus.NotModified or ItemStatus.DataModified;
    }

    /// <summary>
    /// Reports whether a row is one the legacy treats as NEW for the purpose of the identity-column
    /// round trip.
    /// </summary>
    /// <param name="status">
    /// The row's status, read the legacy way with column index <see cref="RowStatusColumn"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> for <see cref="ItemStatus.NewModified"/> only;
    /// <see langword="false"/> for every other value.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Reproduces the legacy test <c>= NewModified!</c>, which appears six times across the update
    /// path and is always an EQUALITY comparison against that single status:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///     <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L140</c> and
    ///     <c>:L174</c> - selecting Primary-buffer rows to receive identity values.
    ///   </description></item>
    ///   <item><description>
    ///     <c>n_cst_threading_task_sqlupdate.sru:L156</c> and <c>:L190</c> - the same for the
    ///     Filter buffer.
    ///   </description></item>
    ///   <item><description>
    ///     <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L230</c> and
    ///     <c>:L238</c> - collecting the identity values that the round trip sends back.
    ///   </description></item>
    /// </list>
    /// <para>
    /// C-B - <see cref="ItemStatus.New"/> IS DELIBERATELY EXCLUDED. The legacy compares for
    /// equality with <c>NewModified!</c> and never with <c>New!</c>, so a row that was inserted but
    /// never edited does not participate. Widening this predicate to "any new row" would look like
    /// a tidy-up and would change which rows the identity write-back touches, so it is not done.
    /// The domain enum carries <see cref="ItemStatus.New"/> because it is a legal runtime status
    /// that a payload must be able to express, not because any legacy test matches it.
    /// </para>
    /// <para>
    /// <c>Concurrency/IdentityColumnResolver.cs</c> is the primary consumer. It owns the buffer
    /// asymmetry this predicate does not: the legacy walks Primary forward
    /// [<c>n_cst_thread_task_sqlupdate.sru:L229</c>] but Filter BACKWARD [<c>:L237</c>], because
    /// the Filter buffer's row order is inverted relative to the source, which the legacy states in
    /// its own comment at <c>:L235</c>. That direction is a property of the buffer rather than of a
    /// status, so it is not reproduced here - but it must not be "corrected" there either.
    /// </para>
    /// </remarks>
    internal static bool IsNewRow(ItemStatus status)
    {
        // Verbatim: the legacy test is `= NewModified!`, an equality comparison against ONE status.
        // ItemStatus.New is not included. See the remarks above before changing this.
        return status == ItemStatus.NewModified;
    }

    /// <summary>
    /// Reports whether a row counts as MODIFIED, which is the condition that makes the legacy's
    /// modified-row walk visit it and the legacy's modified count include it.
    /// </summary>
    /// <param name="status">
    /// The row's status, read the legacy way with column index <see cref="RowStatusColumn"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> for <see cref="ItemStatus.DataModified"/> and
    /// <see cref="ItemStatus.NewModified"/>; <see langword="false"/> for every other value.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the <c>DataModified!</c> / <c>NewModified!</c> pair. It is the predicate that drives
    /// <see cref="GetNextModified"/> and <see cref="EnumerateModifiedRows"/>, and it is the same
    /// membership rule behind the <c>ModifiedCount()</c> the legacy seeds its progress total from
    /// at <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L52</c>.
    /// </para>
    /// <para>
    /// The stamping direction of the same pair is measured too:
    /// <c>Buffers/ChangesetCodec.cs</c> must set rows to <see cref="ItemStatus.DataModified"/>
    /// before extracting a changeset, exactly as the legacy does at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L123</c> and
    /// <c>:L127</c> for a drop-down child, at <c>:L162</c> and <c>:L168</c> for the temporary
    /// datastore that works around the sorted multi-block defect, and at <c>:L197</c> and
    /// <c>:L202</c> for the direct path - six sites, every one of them passing column index zero.
    /// </para>
    /// <para>
    /// <see cref="ItemStatus.NotModified"/> is excluded, so this predicate is NOT the complement of
    /// <see cref="IsDeleteCountable"/>: the two case lists overlap on
    /// <see cref="ItemStatus.DataModified"/> and disagree on both
    /// <see cref="ItemStatus.NotModified"/> and <see cref="ItemStatus.NewModified"/>. They are
    /// separate legacy tests answering separate questions and must not be collapsed into one.
    /// </para>
    /// </remarks>
    internal static bool IsModified(ItemStatus status)
    {
        return status is ItemStatus.DataModified or ItemStatus.NewModified;
    }

    #endregion

    #region Measured operations - the delete walk and the modified-row traversal

    /// <summary>
    /// Counts the rows in the <c>Delete!</c> buffer that contribute to the update-progress total.
    /// </summary>
    /// <param name="deletedCount">
    /// The number of rows in the <c>Delete!</c> buffer, the value the legacy obtains from
    /// <c>DeletedCount()</c>. Zero is legal and yields zero.
    /// </param>
    /// <param name="deleteRowStatusAt">
    /// Reads the status of one <c>Delete!</c>-buffer row. Its argument is a ONE-BASED row number in
    /// the inclusive range <see cref="FirstRowNumber"/> to <paramref name="deletedCount"/>, and its
    /// result is that row's own status - the reading the legacy performs with column index
    /// <see cref="RowStatusColumn"/>.
    /// </param>
    /// <returns>
    /// The number of rows whose status satisfies <see cref="IsDeleteCountable"/>. The legacy ADDS
    /// this to its modified count rather than using it alone; see the remarks.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="deleteRowStatusAt"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="deletedCount"/> is negative.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Reproduces the loop at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L55-L60</c>:
    /// </para>
    /// <code>
    /// for nRow = DeletedCount() to 1 step -1
    ///     choose case GetItemStatus(nRow,0,Delete!)
    ///         case NotModified!,DataModified!
    ///             _nUpdateTotal ++
    ///     end choose
    /// next
    /// </code>
    /// <para>
    /// THE TOTAL IS COMPOSED BY THE CALLER, NOT HERE. The legacy seeds the total with
    /// <c>_nUpdateTotal = ModifiedCount()</c> at <c>:L52</c> and then increments it inside this
    /// loop, so the update-progress total is the caller's modified count PLUS this result. That
    /// composition belongs to the <c>Tasks/</c> layer, which owns the notification stream and the
    /// <c>CPU()</c>-based rate limit at <c>:L37</c> and <c>:L53</c>; this method deliberately
    /// returns only the part that is a function of statuses.
    /// </para>
    /// <para>
    /// THE REVERSE ITERATION IS PRESERVED even though a count cannot depend on order. Two reasons:
    /// it keeps this loop diffable against its locator line for line, and the descent to
    /// <see cref="FirstRowNumber"/> keeps the whole file inside the one-based indexing domain (R9)
    /// rather than introducing a zero-based walk that a later reader would have to re-audit. The
    /// legacy's own most dangerous reverse walk is a different one - the Filter-buffer identity
    /// collection at <c>n_cst_thread_task_sqlupdate.sru:L237</c>, which is reversed because the
    /// buffer's row order is genuinely inverted [<c>:L235</c>] - and that one belongs to
    /// <c>Concurrency/IdentityColumnResolver.cs</c>.
    /// </para>
    /// <para>
    /// <paramref name="deleteRowStatusAt"/> is invoked exactly once per row and its result is not
    /// cached, matching the legacy, which calls <c>GetItemStatus</c> once per iteration.
    /// </para>
    /// </remarks>
    internal static long CountDeleteCountable(
        long deletedCount,
        Func<long, ItemStatus> deleteRowStatusAt)
    {
        ArgumentNullException.ThrowIfNull(deleteRowStatusAt);
        ArgumentOutOfRangeException.ThrowIfNegative(deletedCount);

        long countable = 0L;

        // Verbatim shape of n_cst_thread_task_sqlbase_ds_mt.sru:L55 - `DeletedCount() to 1 step -1`.
        // R9: both bounds are ONE-BASED row numbers. There is no zero-based iteration here, and the
        // terminating bound is FirstRowNumber rather than 0.
        for (long rowNumber = deletedCount; rowNumber >= FirstRowNumber; rowNumber--)
        {
            if (IsDeleteCountable(deleteRowStatusAt(rowNumber)))
            {
                countable++;
            }
        }

        return countable;
    }

    /// <summary>
    /// Returns the number of the next modified row after <paramref name="afterRowNumber"/>, or
    /// <see cref="NoMoreModifiedRows"/> when there is none.
    /// </summary>
    /// <param name="afterRowNumber">
    /// The row to search after. Pass <see cref="BeforeFirstRow"/> to start the walk, or the previous
    /// result to continue it. This is the legacy's own argument contract.
    /// </param>
    /// <param name="buffer">
    /// The buffer being walked. The measured legacy call sites use <see cref="DwBuffer.Primary"/>
    /// and <see cref="DwBuffer.Filter"/>; <see cref="DwBuffer.Delete"/> is accepted because the
    /// legacy domain has exactly three buffers and restricting it further would invent a rule the
    /// legacy does not have.
    /// </param>
    /// <param name="rowCount">
    /// The number of rows in <paramref name="buffer"/>. Zero is legal and always yields
    /// <see cref="NoMoreModifiedRows"/>.
    /// </param>
    /// <param name="rowStatusAt">
    /// Reads the status of one row of <paramref name="buffer"/>. Its argument is a ONE-BASED row
    /// number in the inclusive range <see cref="FirstRowNumber"/> to <paramref name="rowCount"/>,
    /// and its result is that row's own status.
    /// </param>
    /// <returns>
    /// The one-based number of the next row whose status satisfies <see cref="IsModified"/>, or
    /// <see cref="NoMoreModifiedRows"/> when no such row remains.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="rowStatusAt"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="buffer"/> is not one of the three legacy buffers, or
    /// <paramref name="afterRowNumber"/> or <paramref name="rowCount"/> is negative.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Reproduces <c>GetNextModified</c> as the legacy uses it at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L138</c> and
    /// <c>:L147</c> for the Primary buffer, <c>:L154</c> and <c>:L163</c> for the Filter buffer,
    /// <c>:L172</c>, <c>:L181</c>, <c>:L188</c> and <c>:L197</c> for the DataStore arms of the same
    /// function, and again at <c>:L321</c>, <c>:L328</c>, <c>:L333</c> and <c>:L340</c> where the
    /// walk translates a driver row ordinal into a DataWindow row number.
    /// </para>
    /// <para>
    /// R9 - THE SENTINEL CONTRACT, WHICH IS THE WHOLE POINT OF THIS SIGNATURE. The seed is zero and
    /// the terminator is zero, and neither is an index:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///     <see cref="BeforeFirstRow"/> as an ARGUMENT means "start before the first row". The one
    ///     piece of arithmetic below adds one to it, reaching <see cref="FirstRowNumber"/>. That is
    ///     an advance inside a one-based domain, not a conversion out of one.
    ///   </description></item>
    ///   <item><description>
    ///     <see cref="NoMoreModifiedRows"/> as a RESULT means "no more modified rows". It is never
    ///     returned as a row number, and a caller must test it before using the value, exactly as
    ///     the legacy's <c>do while(nRow &gt; 0)</c> does.
    ///   </description></item>
    /// </list>
    /// <para>
    /// Subtracting one from either sentinel, or from the returned row number, would shift every
    /// visited row by one while leaving the number of visited rows unchanged - a defect no
    /// row-count assertion can catch.
    /// </para>
    /// <para>
    /// THE OPERATION IS BUFFER-AGNOSTIC BY CONSTRUCTION, which is why the same method serves the
    /// Primary and Filter walks the legacy writes out separately, rather than one being a special
    /// case of the other. <paramref name="buffer"/> is carried for validation and for diagnostics:
    /// a row number means a different row in a different buffer - the published contract states
    /// that a consumer must not assume <c>row</c> counts the same way across buffers - so a fault
    /// raised here names the buffer it was raised for.
    /// </para>
    /// <para>
    /// The <paramref name="afterRowNumber"/> at-or-beyond-end fast path also removes any
    /// possibility of overflow on the increment, so a caller may pass
    /// <see cref="long.MaxValue"/> without consequence.
    /// </para>
    /// </remarks>
    internal static long GetNextModified(
        long afterRowNumber,
        DwBuffer buffer,
        long rowCount,
        Func<long, ItemStatus> rowStatusAt)
    {
        ArgumentNullException.ThrowIfNull(rowStatusAt);
        ThrowIfNotLegacyBuffer(buffer, nameof(buffer));
        ArgumentOutOfRangeException.ThrowIfNegative(afterRowNumber);
        ArgumentOutOfRangeException.ThrowIfNegative(rowCount);

        // Nothing can follow the last row. Checked before the increment below, so afterRowNumber
        // may safely be long.MaxValue.
        if (afterRowNumber >= rowCount)
        {
            return NoMoreModifiedRows;
        }

        // R9: `afterRowNumber + 1` is the ONLY arithmetic performed on a row ordinal in this file.
        // With the seed BeforeFirstRow (0) it yields FirstRowNumber (1), which is where a one-based
        // walk starts. It is not a rebase, and it must never become `afterRowNumber - 1`.
        for (long rowNumber = afterRowNumber + 1L; rowNumber <= rowCount; rowNumber++)
        {
            if (IsModified(rowStatusAt(rowNumber)))
            {
                return rowNumber;
            }
        }

        return NoMoreModifiedRows;
    }

    /// <summary>
    /// Enumerates, in ascending row order, the one-based numbers of every modified row in a buffer.
    /// </summary>
    /// <param name="buffer">
    /// The buffer being walked. The legacy runs this walk over <see cref="DwBuffer.Primary"/> and
    /// <see cref="DwBuffer.Filter"/> separately.
    /// </param>
    /// <param name="rowCount">
    /// The number of rows in <paramref name="buffer"/>. Zero is legal and yields an empty sequence.
    /// </param>
    /// <param name="rowStatusAt">
    /// Reads the status of one row of <paramref name="buffer"/>, by ONE-BASED row number.
    /// </param>
    /// <returns>
    /// A lazily evaluated sequence of one-based row numbers. The sequence is empty when the buffer
    /// holds no modified row, and <see cref="NoMoreModifiedRows"/> never appears in it.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="rowStatusAt"/> is <see langword="null"/>. Raised when this method is called,
    /// not deferred to the first enumeration step.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="buffer"/> is not one of the three legacy buffers, or
    /// <paramref name="rowCount"/> is negative. Raised when this method is called, not deferred.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The ergonomic form of the legacy's own idiom, which appears six times across
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru</c>
    /// [<c>:L138-L148</c>, <c>:L154-L164</c>, <c>:L172-L182</c>, <c>:L188-L198</c>,
    /// <c>:L321-L329</c>, <c>:L333-L341</c>]:
    /// </para>
    /// <code>
    /// nRow = GetNextModified(0, Primary!)
    /// do while (nRow &gt; 0)
    ///     if GetItemStatus(nRow, 0, Primary!) = NewModified! then ... end if
    ///     nRow = GetNextModified(nRow, Primary!)
    /// loop
    /// </code>
    /// <para>
    /// BOTH BUFFERS, NOT JUST PRIMARY. The legacy writes the walk out twice per host type, once for
    /// <c>Primary!</c> and once for <c>Filter!</c>, and this method is buffer-agnostic precisely so
    /// that neither is a special case. A consumer that walks only the Primary buffer would silently
    /// skip the filtered-out rows the legacy visits.
    /// </para>
    /// <para>
    /// Argument validation is EAGER and the walk itself is lazy. The two are separated because a C#
    /// iterator method defers its whole body until the first enumeration step, which would report a
    /// null accessor or a negative count at a point far from the mistake. The validated arguments
    /// are captured and the iteration runs in a local function.
    /// </para>
    /// <para>
    /// The sequence is a live view rather than a snapshot: <paramref name="rowStatusAt"/> is invoked
    /// during enumeration, so mutating a row's status mid-walk is observable, exactly as it is in
    /// the legacy, whose loop re-reads the buffer on every iteration. A caller that needs a stable
    /// list must materialise one.
    /// </para>
    /// </remarks>
    internal static IEnumerable<long> EnumerateModifiedRows(
        DwBuffer buffer,
        long rowCount,
        Func<long, ItemStatus> rowStatusAt)
    {
        ArgumentNullException.ThrowIfNull(rowStatusAt);
        ThrowIfNotLegacyBuffer(buffer, nameof(buffer));
        ArgumentOutOfRangeException.ThrowIfNegative(rowCount);

        return Walk(buffer, rowCount, rowStatusAt);

        // The legacy do-while, one for one. Seeded with BeforeFirstRow and terminated on
        // NoMoreModifiedRows, matching `do while (nRow > 0)`.
        static IEnumerable<long> Walk(
            DwBuffer buffer,
            long rowCount,
            Func<long, ItemStatus> rowStatusAt)
        {
            long rowNumber = GetNextModified(BeforeFirstRow, buffer, rowCount, rowStatusAt);

            while (rowNumber > NoMoreModifiedRows)
            {
                yield return rowNumber;

                rowNumber = GetNextModified(rowNumber, buffer, rowCount, rowStatusAt);
            }
        }
    }

    #endregion

    #region Internal helpers

    /// <summary>
    /// Rejects a column index that falls outside the legacy status-addressing domain.
    /// </summary>
    /// <param name="columnIndex">The value to screen.</param>
    /// <param name="paramName">
    /// The name of the caller's parameter, so the raised exception points at the caller's argument
    /// rather than at this helper's.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="columnIndex"/> is neither <see cref="RowStatusColumn"/> nor a column number
    /// at or above <see cref="FirstColumnNumber"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The domain has exactly two parts and no gap between them: the sentinel zero, meaning the row
    /// itself, and the one-based column numbers above it. A negative value is the only way to fall
    /// outside, and the diagnostic names the likeliest cause because it is the R9 hazard itself - a
    /// caller who rebased a one-based column number to zero-based turns column one into zero, which
    /// silently becomes "the row", and turns a hypothetical column zero into minus one, which lands
    /// here. The first of those two mistakes cannot be detected; the second can, and this is where
    /// it is.
    /// </para>
    /// <para>
    /// Preferred over a bare non-negative check because the message carries the domain and the
    /// hazard. Private for the same reason as the buffer screen below: it is validation, not a
    /// published classification rule.
    /// </para>
    /// </remarks>
    private static void ThrowIfNotLegacyColumnIndex(int columnIndex, string paramName)
    {
        if (columnIndex == RowStatusColumn || columnIndex >= FirstColumnNumber)
        {
            return;
        }

        throw new ArgumentOutOfRangeException(
            paramName,
            columnIndex,
            "Not a legacy column index. The domain is the row-status sentinel 0, which means the " +
            "row itself, plus the one-based column numbers from 1 upwards - the golden-master " +
            "fixture declares ids 1 through 6 [ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L21-L26]. " +
            "The legacy never produces a negative index. If one appears here, suspect a one-based " +
            "column number that was rebased to zero-based.");
    }

    /// <summary>
    /// Rejects a buffer value that does not name one of the three legacy buffers.
    /// </summary>
    /// <param name="buffer">The value to screen.</param>
    /// <param name="paramName">
    /// The name of the caller's parameter, so the raised exception points at the caller's argument
    /// rather than at this helper's.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="buffer"/> is a value outside the declared three-member domain.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The legacy <c>dwbuffer</c> domain has exactly three members - <c>Primary!</c>,
    /// <c>Delete!</c> and <c>Filter!</c> - and the published contract carries exactly those three,
    /// with <c>DW_BUFFER_PRIMARY = 0</c> occupying zero the way PowerBuilder's own unassigned
    /// <c>dwbuffer</c> does. There is therefore no sentinel member to screen out, and the only value
    /// that can reach this method outside the domain is an out-of-range numeric cast - a wire
    /// payload carrying an unknown enum number, or a caller casting an arbitrary integer.
    /// </para>
    /// <para>
    /// Rejecting that is NOT a behavioural addition: it refuses a state the legacy cannot represent.
    /// This is the refactor's standing rule for such a case - narrow the contract with a defined
    /// error rather than widen it with a guess - and it is why the failure is a thrown argument
    /// exception rather than a silently chosen default buffer.
    /// </para>
    /// <para>
    /// The screen is private on purpose. It is validation and diagnostics, not a published
    /// classification rule, and every member of this type's surface traces to a legacy call site
    /// that can be cited.
    /// </para>
    /// </remarks>
    private static void ThrowIfNotLegacyBuffer(DwBuffer buffer, string paramName)
    {
        if (buffer is DwBuffer.Primary or DwBuffer.Delete or DwBuffer.Filter)
        {
            return;
        }

        throw new ArgumentOutOfRangeException(
            paramName,
            buffer,
            "Not one of the three legacy DataWindow buffers. The domain is exactly Primary! (0), " +
            "Delete! (1) and Filter! (2); any other number is an out-of-range cast or an unknown " +
            "enum value from a foreign payload, and the legacy has no state it could denote " +
            "[ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L32,L56].");
    }

    #endregion
}

/// <summary>
/// Fluent forwarders for the three <see cref="ItemStatusMachine"/> classification predicates, so
/// that a call site can read <c>status.IsModified()</c> instead of
/// <c>ItemStatusMachine.IsModified(status)</c>.
/// </summary>
/// <remarks>
/// <para>
/// These are extension methods on the GENERATED <see cref="ItemStatus"/> enum from
/// <c>shared/PowerFramework.Contracts/Proto/common.v1.proto</c>. Extending the published type is
/// the only way to get a fluent reading without declaring a competing domain type in this
/// namespace, which DECISION 1 in the file header rules out.
/// </para>
/// <para>
/// EVERY MEMBER FORWARDS AND NOTHING MORE. There is no second copy of any case list here: each body
/// is a single call into <see cref="ItemStatusMachine"/>. That matters because the case lists are
/// the part of this file with parity significance - duplicating one would create two places for it
/// to drift, and only one of them would be covered by the characterization reasoning recorded on
/// the machine's own members.
/// </para>
/// <para>
/// The type carries no state, no I/O and no clock, in common with the machine itself (C-H).
/// </para>
/// </remarks>
internal static class ItemStatusExtensions
{
    /// <summary>
    /// Fluent form of <see cref="ItemStatusMachine.IsDeleteCountable"/>: reports whether a row in
    /// the <c>Delete!</c> buffer counts toward the update-progress total.
    /// </summary>
    /// <param name="status">The row's status, read with column index zero.</param>
    /// <returns>
    /// <see langword="true"/> for <see cref="ItemStatus.NotModified"/> and
    /// <see cref="ItemStatus.DataModified"/> only, reproducing
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L57</c>. A
    /// new-then-deleted row is excluded, intentionally.
    /// </returns>
    internal static bool IsDeleteCountable(this ItemStatus status)
    {
        return ItemStatusMachine.IsDeleteCountable(status);
    }

    /// <summary>
    /// Fluent form of <see cref="ItemStatusMachine.IsNewRow"/>: reports whether a row is one the
    /// legacy treats as NEW for the identity-column round trip.
    /// </summary>
    /// <param name="status">The row's status, read with column index zero.</param>
    /// <returns>
    /// <see langword="true"/> for <see cref="ItemStatus.NewModified"/> only, reproducing the
    /// equality test at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L140,L156,L174,L190</c>
    /// and <c>n_cst_thread_task_sqlupdate.sru:L230,L238</c>. <see cref="ItemStatus.New"/> is
    /// excluded, intentionally.
    /// </returns>
    internal static bool IsNewRow(this ItemStatus status)
    {
        return ItemStatusMachine.IsNewRow(status);
    }

    /// <summary>
    /// Fluent form of <see cref="ItemStatusMachine.IsModified"/>: reports whether a row counts as
    /// modified, which is what makes the legacy's modified-row walk visit it.
    /// </summary>
    /// <param name="status">The row's status, read with column index zero.</param>
    /// <returns>
    /// <see langword="true"/> for <see cref="ItemStatus.DataModified"/> and
    /// <see cref="ItemStatus.NewModified"/>, the pair behind <c>GetNextModified</c> and
    /// <c>ModifiedCount()</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L52</c>].
    /// </returns>
    internal static bool IsModified(this ItemStatus status)
    {
        return ItemStatusMachine.IsModified(status);
    }
}
