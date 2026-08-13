// ==============================================================================================
//  ChangesetCodec.cs - THE GetChanges / SetChanges BLOB CODEC, AND THE THREE DEFECTS IT OWNS
//  --------------------------------------------------------------------------------------------
//  PORTED FROM    ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru
//                     the SEND side. :L93 the codec selector; :L103-L110 the filter fold and its
//                     NOTE; :L112-L141 the drop-down child loop; :L145-L151 the chunk arithmetic
//                     and the sorted guard; :L143-L191 the temporary-carrier branch carrying
//                     DEFECT 1 [:L148] and DEFECT 2 [:L176]; :L192-L228 the in-place branch;
//                     :L229-L231 the empty-result arm; :L410 the chunk-size guard and :L34 the
//                     10000 default; :L550-L597 the sort and filter decision, whose `case else`
//                     arm at :L576-L580 CLEARS both.
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru
//                     the RECEIVE side. :L180-L258 ondatachunk, including the reset-at-first-chunk
//                     rule and the strict `rtCode > 1` coercion at :L250; :L93-L155
//                     onchilddatareceived, including its reset-BEFORE-apply and its two
//                     re-application predicates at :L145-L150.
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru
//                     the carrier this codec operates on, reproduced by Buffers/DataWindowBuffers.cs.
//                 docs/PB多线程绕坑提示.md
//                     the two threading hazards. Hazard 1 is the whole reason the legacy signature
//                     is `GetChanges(ref blbData)`; see DECISION 5.
//  FIXTURE        ws_objects/pfw.tests.pbl.src/dw_sqlite.srd
//                     processing=1 [:L3] so the fixture takes THIS codec's arm, and
//                     sort="age A salary A " [:L14] so it is SORTED - which makes DEFECT 1 a MAIN
//                     PATH rather than an edge case. Six columns, all update=yes
//                     updatewhereclause=yes [:L8-L13], with updatewhere=1 updatekeyinplace=no [:L14].
//
//  ORACLE STATUS (C-C). Every ws_objects/** and docs/** path above is READ ONLY: never edited,
//  never reformatted, never moved. Nothing else in this repository can adjudicate the behaviour
//  below - logfile.md stops at framework 3.0.7.2062 while the history runs years later, and the two
//  PowerBuilder project objects contradict each other and both name a library that does not exist -
//  so EVERY behavioural claim here carries a line locator a reader can diff against.
//
//  ============================================================================================
//  WHICH ARM OF THE CODEC SELECTOR THIS IS
//  ============================================================================================
//  n_cst_thread_task_sqlquery.sru:L93 dispatches on the carrier's processing kind:
//
//      choose case Long(Data.Describe("DataWindow.Processing"))
//          case 4,5 //Crosstab or Composite datawindow
//              ... GetFullState ...                     <- Buffers/FullStateCodec.cs
//          case else
//              ... GetChanges ...                       <- THIS FILE
//      end choose
//
//  THIS FILE IS THE `case else` ARM: every processing kind except crosstab and composite. The
//  decision is taken by asking the carrier's own typed property - see
//  ChangesetCodec.HandlesCarrier - and NEVER by re-parsing a describe string, because
//  Buffers/DataWindowBuffers.cs already owns that parse and two independent parses could disagree.
//
//  ELEVEN OF THE REPOSITORY'S TWELVE DataWindow DEFINITIONS DECLARE processing=1, so this codec is
//  the only one the legacy corpus exercises at all. The full-state codec has no fixture. The parity
//  weight for the changeset path therefore rests entirely on this file and its tests.
//
//  ============================================================================================
//  DECISION 1 - DEFECT 1, THE SORTED MULTI-CHUNK ROW LOSS, IS REPRODUCED WITH ITS WORKAROUND
//  ============================================================================================
//  The legacy wrote a FIXME against itself at n_cst_thread_task_sqlquery.sru:L147-L149:
//
//      //FIXME
//      //带排序的DW大于1块时使用SetChanges可能会丢失行
//      //采用临时的DS来绕开此问题
//
//  "When a sorted DataWindow spans more than one block, using SetChanges may lose rows; a temporary
//  DataStore is used to work around it." THE WORKAROUND *IS* THE BEHAVIOUR (C-B). The underlying
//  loss is not fixed here, is not detected here, and is not reported here - it is routed around
//  exactly as the legacy routes around it, by staging each chunk through a temporary carrier so that
//  the rows a chunk carries are the ONLY rows that carrier ever holds. See
//  ChangesetCodec.RequiresTemporaryCarrier and the temporary-carrier branch of TransferAsync.
//
//  ============================================================================================
//  DECISION 2 - DEFECT 2, THE RESET PROHIBITION, IS POINT-SPECIFIC AND NOT GLOBAL
//  ============================================================================================
//  The second FIXME, at n_cst_thread_task_sqlquery.sru:L175-L176:
//
//      //FIXME
//      //不能使用Reset来清除数据，会使SetChanges应用不到行
//
//  "Reset must not be used to clear the data; doing so makes SetChanges fail to apply to rows." THE
//  PROHIBITION ITSELF IS THE BEHAVIOUR. It is also NARROW, and a codec that banned Reset outright
//  would be as wrong as one that reset freely. There are FOUR measured positions and they do not
//  agree with one another:
//
//    (a) TEMPORARY-CARRIER BRANCH, MID-LOOP CLEAR - RESET IS FORBIDDEN. The legacy discards rows
//        individually instead [:L177-L182]. Reproduced in TransferAsync's temporary branch, which
//        calls RowsDiscard and never Reset.
//    (b) IN-PLACE BRANCH, FINAL CHUNK - RESET IS USED, and legitimately: `if nChunkIdx < nChunkCnt
//        then` discard, `else Data.Reset()` [:L209-L218]. It is safe because it runs AFTER the
//        changeset blob has already been captured at :L205, so there is no later apply for it to
//        break. Reproduced verbatim.
//    (c) CHILD COLUMN, AFTER CAPTURE - RESET IS USED. `dwcSrc.Reset()` at :L133 sits between the
//        capture at :L129 and the handover at :L134, so again nothing remains to apply.
//    (d) RECEIVE SIDE, BEFORE THE FIRST CHUNK - RESET IS USED. `if current = 1 then ... Reset()`
//        [:L192-L196, :L218-L220, :L235-L237], and the child receiver resets unconditionally before
//        applying [:L103, :L116, :L128]. Safe because it precedes the apply rather than interleaving
//        with it.
//
//  The distinction that makes all four consistent: RESET IS UNSAFE ONLY BETWEEN A CAPTURE AND A
//  LATER CAPTURE FROM THE SAME CARRIER. Every position above is annotated at its site.
//
//  ============================================================================================
//  DECISION 3 - A THIRD, LATENT QUIRK: THE FILTERED COUNT IS NEVER RESET PER ITERATION
//  ============================================================================================
//  n_cst_thread_task_sqlquery.sru:L75 declares `long nRow,nRowCnt,nFilterCnt` ONCE, OUTSIDE the
//  chunk loop. `nRowCnt` is reassigned at the top of every iteration [:L159, :L195], but
//  `nFilterCnt` is assigned ONLY inside the guard `if nRowCnt < _nChunkSize and
//  Data.FilteredCount() > 0` [:L164, :L199]. On an iteration where that guard is false the later
//  `if nFilterCnt > 0 then ... RowsDiscard(1,nFilterCnt,Filter!)` [:L180-L182, :L213-L215]
//  therefore runs with a STALE COUNT CARRIED OVER FROM THE PREVIOUS ITERATION, against a Filter
//  buffer that has already been emptied.
//
//  REPRODUCED, NOT HELPFULLY ZEROED (C-B). The filtered count below is declared before the loop and
//  is never cleared inside it. Its observable effect is nil for exactly one reason: THE LEGACY
//  DISCARDS THE RETURN VALUE, so an out-of-range discard is a benign no-op. That is precisely why
//  DataWindowBufferStore.RowsDiscard answers DataStoreFailure for an unaddressable range instead of
//  throwing - an exception here would be a regression the legacy could not have had.
//
//  ONE FURTHER OBSERVATION, RECORDED FOR HONESTY RATHER THAN CLAIMED AS BEHAVIOUR. Under the
//  measured arithmetic each chunk consumes exactly min(chunkSize, remaining) rows and the PRIMARY
//  buffer always drains before the FILTER buffer is touched, so `rowCount == chunkSize` cannot
//  follow an iteration that set the filtered count, and the stale discard stays LATENT through an
//  undisturbed loop. It becomes reachable the moment the handover sink mutates the source between
//  chunks, which it can: the handover at :L183 and :L219 runs while the source is still live.
//
//  ============================================================================================
//  DECISION 4 - SORT AND FILTER ARE CLEARED HERE, WHICH IS THE MIRROR IMAGE OF FULL STATE
//  ============================================================================================
//  n_cst_thread_task_sqlquery.sru:L577-L578 says the SetChanges transfer style does not need sort
//  and filter conditions and that they can even hurt performance, so the `case else` arm sets both
//  to empty [:L579-L580]. The `case 4,5` arm immediately above SYNCHRONIZES them instead
//  [:L562-L575]. The two arms are opposites and BOTH are contract.
//
//  THE GATE IS PRESERVED AND IS NOT UNCONDITIONAL. The legacy reaches that dispatch only when a
//  data object has been assigned [:L550] AND execution is off the main thread AND the receiving side
//  does not need a freshly created object [:L559]. Otherwise it takes the else-arm at :L582-L597 and
//  SYNCHRONIZES. See ChangesetCodec.ShouldClearSortAndFilter, which is a pure predicate precisely so
//  the gate is testable without a thread, a task or a database.
//
//  ============================================================================================
//  DECISION 5 - THE `ref blob` SHAPE IS DELIBERATELY NOT REPRODUCED (C-K)
//  ============================================================================================
//  Every legacy changeset signature is `GetChanges(ref blbData)` rather than a blob return, and
//  docs/PB多线程绕坑提示.md line 1 is why: a worker thread synchronously calling a main-thread
//  member that returns `string` or `blob` can corrupt memory, the trigger being an undelivered
//  posted message against an object that is then destroyed, and the prescribed avoidance - line 4,
//  `可以改为ref传回` - is to return through a `ref` parameter instead. THAT IS A HAZARD WORKAROUND,
//  NOT A STYLE CHOICE, AND THE HAZARD HAS NO .NET ANALOGUE: a headless container has no message
//  pump, no PowerBuilder object lifetime and no posted-message queue. Managed members here therefore
//  return their payload normally, through `out` where a status code accompanies it.
//
//  Hazard 2 of the same document - null the reference once you are done with it - grounds the blob
//  clear that follows every handover. See DECISION 6.
//
//  ============================================================================================
//  DECISION 6 - THE BLOB IS CLEARED AFTER EVERY HANDOVER, AND THAT IS OBSERVABLE
//  ============================================================================================
//  `blbData = Blob("")` appears at :L138, :L187 and :L223 - once per handover, on all three paths.
//  It bounds peak memory across a chunk sequence, and it is the same ownership discipline as
//  hazard 2: after the payload changes hands exactly one party holds it. Reproduced by assigning
//  the empty payload to the local immediately after each handover, and surfaced through
//  ChangesetTransferOutcome.PayloadCleared so a test can assert it rather than infer it.
//
//  ============================================================================================
//  DECISION 7 - WHAT IS DELIBERATELY NOT HERE
//  ============================================================================================
//  NO FULL-STATE LOGIC. GetFullState, SetFullState and the two defects that travel with them - full
//  state requires the sort and filter conditions to be synchronized [:L562-L563] and it crashes when
//  a crosstab has too many columns [:L672-L674] - belong to Buffers/FullStateCodec.cs. This file
//  refuses a full-state payload outright rather than half-handling it; see ApplyChunk.
//
//  NO DwBuffer AND NO ItemStatus RE-DECLARATION. Both come from the published contracts,
//  PowerFramework.Contracts.Common.V1, and every status predicate comes from Buffers/ItemStatus.cs.
//  Declaring a second spelling of either would be the wire-compatibility defect that
//  common.v1.proto's own enum notes warn about.
//
//  NO JSON AND NO XML (C-D). The changeset payload is the PUBLISHED persistence.v1.CarrierState
//  message, and re-expressing it as document serialization would reach into the deferred Documents
//  parser family, which is forbidden outright. An opaque binary blob written with BinaryWriter is the
//  tempting shape and it is a defect: a format owned privately here cannot be
//  implemented by a consumer that references only the contracts project. Protobuf's own canonical
//  encoding is deterministic byte-for-byte for equal input, which is what a golden-master comparison
//  requires, and it is a published format rather than a private one.
//
//  NO PRESENTATION, AND THE EVIDENCE IS AN ASYMMETRY (C-D). The DataWindow receiver arm suspends
//  redraw before the first chunk and calls a group recalculation plus a redraw restore after the last
//  [n_cst_threading_task_sqlquery.sru:L194, :L200, :L203]; the structurally identical DataStore arm
//  [:L212-L228] calls NEITHER. Two independent sightings of that asymmetry - the same pattern recurs
//  in n_cst_threading_task_sqlupdate.sru - are the evidence that both calls are PRESENTATIONAL and
//  belong to the deferred DesignSystem capability area. They are a DOCUMENTED NON-PORT. ResetUpdate
//  is NOT presentational, appears in both arms, and is KEPT.
//
//  NO STORAGE, NO I/O, NO WALL CLOCK (C-E, C-H). No DbConnection, no EF Core type, no
//  Microsoft.Data.Sqlite type, no file, no socket, no SQL dialect and no schema knowledge. The only
//  time-dependent operation is the inter-chunk yield, and it goes through the INJECTED TimeProvider;
//  DateTime, DateTimeOffset, Stopwatch and Environment.TickCount appear nowhere.
//
//  NO SIBLING-FOLDER DEPENDENCY. Nothing here reaches into Concurrency/, Tasks/, Grpc/, Errors/,
//  Sql/, Data/ or Transactions/. The only names bound are Buffers/DataWindowBuffers.cs,
//  Buffers/ItemStatus.cs, the published contracts, PowerFramework.Shared.Kernel.RetCode and the BCL.
//
//  ============================================================================================
//  R9 - ONE-BASED TO ZERO-BASED, AUDITED INDIVIDUALLY FOR THIS FILE
//  ============================================================================================
//  The refactor plan names this the single most dangerous mechanical hazard in the migration and
//  permits an individual audit in place of a centralized helper. This is that audit; the six sites
//  the assignment enumerates are each also commented where they occur:
//
//    (1) Ceiling((RowCount() + FilteredCount()) / chunkSize) [:L145]. COUNTS, NOT INDICES - no
//        rebasing applies. Computed with exact integer arithmetic; see ComputeChunkCount.
//    (2) `for nChunkIdx = 1 to nChunkCnt` [:L158, :L194] and the `count` / `current` arguments of
//        the handover [:L183, :L219, :L230]. THE WIRE VALUES ARE ONE-BASED. The first chunk is 1,
//        never 0, so a zero-based reading is wrong by one for the whole stream, and the receive side
//        tests `current = 1` and `count = current` in that same domain.
//    (3) `for nRow = 1 to nRowCnt` and `for nRow = 1 to nFilterCnt` [:L161-L163, :L167-L169,
//        :L196-L198, :L201-L203]. ONE-BASED ROW NUMBERS passed straight to SetItemStatus, whose own
//        surface is one-based. Loops are written `for (long row = FirstRowNumber; row <= count;
//        row++)` so the shape stays diffable against the locator.
//    (4) RowsMove(1, FilteredCount(), Filter!, Data, RowCount() + 1, Primary!) [:L109]. THE APPEND
//        IDIOM. `RowCount() + 1` is one past the last valid row in a one-based domain; the
//        equivalent zero-based insertion point is the count ITSELF, not the count plus one. The
//        conversion is NOT performed here - the one-based value is passed through to RowsMove, which
//        owns the single conversion site - and the decision is commented at
//        FoldFilterBufferIntoPrimary.
//    (5) RowsMove(1, n, buffer, temp, 1, buffer) [:L160, :L166]. DESTINATION ROW 1 means "before the
//        first row", the one-based head of the buffer. It is not a zero-based offset and is passed
//        through unchanged.
//    (6) RowsDiscard(1, n, buffer) [:L178, :L181, :L211, :L214]. AN INCLUSIVE ONE-BASED RANGE whose
//        end is the last valid row, not one past it. Passed through unchanged.
//
//  ============================================================================================
//  BINDING CONSTRAINTS AT THIS SITE
//  C-F / C-G SELF-AUDIT: no key, credential, token, password, connection string, statement text or
//  secret-shaped literal appears anywhere in this file. The payload is opaque application data and
//  is never logged - there is no logger here at all.
//
//  C-I SELF-AUDIT: no PackageReference is added. Every non-BCL name bound below arrives through a
//  ProjectReference that already exists.
//
//  C-A / C-J / C-L ARE STRUCTURAL AND OPERATIONAL RATHER THAN LINE-LEVEL, AND ARE RECORDED HERE SO
//  THE LEDGER IS COMPLETE INSTEAD OF PARTIAL. C-A - decomposition, never a collapsed solution - is
//  discharged by where this file LIVES: inside one service's own project, naming no other service
//  and no other service's project, so the four services stay separately restorable and buildable.
//  C-J - independently deployable and independently scalable - follows from the same fact plus the
//  no-storage, no-socket, no-ambient-clock position above: this codec adds nothing that would pin
//  the service to a host, a volume or a peer. C-L - the attached environment's instructions are
//  binding - is discharged by validating through the environment's OWN documented per-service
//  command shape - restore, then build in Release, then test with XPlat Code Coverage collection,
//  run from the service directory - rather than through a bespoke one; the resulting Cobertura
//  report is the exact artifact the per-service coverage gate is measured from. None of the three
//  is claimed at a specific line below, because inventing a line-level citation for a structural
//  constraint would make the ledger less auditable, not more.
// ==============================================================================================

using PowerFramework.Contracts.Common.V1;
using PowerFramework.Contracts.Persistence.V1;

// COLLISION RESOLUTION (AAP 0.4.5.1). PowerBuilder resolved one flat global namespace by library
// list order, so this port has no import statements to rewrite - it has namespaces to CREATE plus
// the collisions a flat namespace tolerated to resolve. This is one of them:
// PowerFramework.Contracts.Common.V1 declares a protobuf MESSAGE named RetCode while
// PowerFramework.Shared.Kernel declares the STATIC CLASS of return-code constants, also named
// RetCode. Importing both namespaces would make every unqualified mention CS0104 ambiguous, so the
// kernel type is brought in by alias instead; an alias declared here takes precedence over a
// namespace import, which resolves the collision without hiding it. Buffers/DataWindowBuffers.cs
// binds the same direction, and the two files must not disagree.
using RetCode = PowerFramework.Shared.Kernel.RetCode;

namespace PowerFramework.Persistence.Buffers;

#region The Describe substitute - the carrier definition properties this codec reads

/// <summary>
/// The carrier definition properties the changeset path reads and writes: the data object name, the
/// generated syntax, and the table sort and filter expressions.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS TYPE EXISTS RATHER THAN MEMBERS ON THE CARRIER. The legacy reaches all four of these
/// through PowerBuilder's <c>Describe</c> and the <c>DataObject</c> property -
/// <c>Data.Describe("DataWindow.Table.Sort")</c> at
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L150</c>,
/// <c>Data.DataObject</c> at <c>:L153</c>, <c>Data.Describe("DataWindow.Syntax")</c> at
/// <c>:L104</c> and <c>:L156</c>, and the filter expression at
/// <c>n_cst_threading_task_sqlquery.sru:L145</c>. <c>Describe</c> is a stringly-typed reflection
/// surface over a DataWindow definition and it is NOT part of the buffer model that
/// <see cref="DataWindowBufferStore"/> reproduces, so these values arrive as an explicit,
/// strongly-typed record instead. That keeps the definition and the DATA cleanly separated, which is
/// what lets every operation in this file be exercised with no database and no data object at all
/// (C-H).
/// </para>
/// <para>
/// C-K - A RECORD WITH INIT-ONLY MEMBERS, SO CLEARING PRODUCES A NEW VALUE. The legacy mutates the
/// live carrier with <c>data.SetSort("")</c> and <c>data.SetFilter("")</c> at
/// <c>n_cst_thread_task_sqlquery.sru:L579-L580</c>. Modelling the definition as an immutable value
/// and returning a cleared copy makes the transition auditable - the caller can hold both the before
/// and the after - without changing what is observable, because the only observable consequence of
/// those two calls is that the transferred carrier carries no sort and no filter.
/// </para>
/// </remarks>
internal sealed record ChangesetSourceDefinition
{
    /// <summary>
    /// The result PowerBuilder's <c>Describe</c> answers when a property is not applicable, or when
    /// its value is inconsistent across the selection it was asked about.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A SINGLE QUESTION MARK, AND IT IS NOT THE SAME AS AN EMPTY STRING. The guard at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L151</c> tests BOTH
    /// separately - <c>sProp &lt;&gt; "?" and sProp &lt;&gt; ""</c> - so the legacy itself treats
    /// them as two distinct states that happen to lead to the same branch here. The framework
    /// screens for the same marker elsewhere, at <c>:L118</c> and at
    /// <c>n_cst_thread_task_sqlbase.sru:L562</c>, which is independent confirmation that it is a
    /// real sentinel rather than a stray literal.
    /// </para>
    /// </remarks>
    internal const string DescribeNotApplicable = "?";

    /// <summary>
    /// The result PowerBuilder's <c>Describe</c> answers when a property name cannot be read at all.
    /// </summary>
    /// <remarks>
    /// Screened for at <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L118</c>,
    /// alongside <see cref="DescribeNotApplicable"/>, when deciding whether a column has a usable
    /// drop-down child name.
    /// </remarks>
    internal const string DescribeUnreadable = "!";

    /// <summary>
    /// The carrier's data object name, reproducing <c>Data.DataObject</c> as read at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L153</c>. Empty when no
    /// data object has been assigned, which is the state the legacy tests for.
    /// </summary>
    internal string DataObjectName { get; init; } = string.Empty;

    /// <summary>
    /// The carrier's generated syntax, reproducing <c>Data.Describe("DataWindow.Syntax")</c> as read
    /// at <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L104</c> for the
    /// create-data handover and at <c>:L156</c> as the temporary carrier's fallback shape.
    /// </summary>
    internal string Syntax { get; init; } = string.Empty;

    /// <summary>
    /// The table sort expression, reproducing <c>Data.Describe("DataWindow.Table.Sort")</c> as read
    /// at <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L150</c>.
    /// </summary>
    /// <remarks>
    /// The golden-master fixture declares <c>sort="age A salary A "</c>
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14</c>] - trailing space included, because
    /// the describe result is the definition text verbatim and is never trimmed here.
    /// </remarks>
    internal string SortExpression { get; init; } = string.Empty;

    /// <summary>
    /// The table filter expression, reproducing <c>Describe("DataWindow.Table.Filter")</c> as read at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L145</c>.
    /// </summary>
    internal string FilterExpression { get; init; } = string.Empty;

    /// <summary>
    /// Whether the sort expression counts as ABSENT for the purposes of the sorted-carrier guard.
    /// </summary>
    /// <value>
    /// <see langword="true"/> when the expression is <see cref="DescribeNotApplicable"/> or empty;
    /// <see langword="false"/> for any other text.
    /// </value>
    /// <remarks>
    /// <para>
    /// Reproduces two of the three conditions of
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L151</c>. The comparison
    /// is ORDINAL: the describe result is a machine-generated property value, never a localized or
    /// user-facing string, so a culture-sensitive comparison would introduce a dependency on ambient
    /// state that the legacy comparison does not have.
    /// </para>
    /// </remarks>
    internal bool SortIsAbsent =>
        string.IsNullOrEmpty(SortExpression)
        || string.Equals(SortExpression, DescribeNotApplicable, StringComparison.Ordinal);

    /// <summary>
    /// Whether a data object has been assigned, which is the outer gate on the sort and filter
    /// decision.
    /// </summary>
    /// <remarks>
    /// Reproduces <c>if _sDataObject &lt;&gt; "" then</c> at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L550</c>. When no data
    /// object is assigned the legacy takes an entirely different path that builds one from SQL or
    /// syntax [<c>:L598-L610</c>], and neither sort nor filter is touched on it.
    /// </remarks>
    internal bool HasDataObject => DataObjectName is { Length: > 0 };

    /// <summary>
    /// Whether a filter expression is substantial enough for the receive side to re-apply it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reproduces <c>if Len(dwc.Describe("DataWindow.Table.Filter")) &gt; 1 then dwc.Filter()</c> at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L145-L147</c>. NOTE THE
    /// THRESHOLD: it is length GREATER THAN ONE, not greater than zero, and the reason is
    /// <see cref="DescribeNotApplicable"/> - a single question mark has length 1, so testing for a
    /// non-empty string would treat "not applicable" as a real expression. That is a deliberate
    /// legacy idiom and it is preserved rather than normalised into a null check.
    /// </para>
    /// </remarks>
    internal bool FilterIsSubstantial => FilterExpression is { Length: > 1 };

    /// <summary>
    /// Whether a sort expression is substantial enough for the receive side to re-apply it.
    /// </summary>
    /// <remarks>
    /// Reproduces <c>if Len(dwc.Describe("DataWindow.Table.Sort")) &gt; 1 then dwc.Sort()</c> at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L148-L150</c>. See
    /// <see cref="FilterIsSubstantial"/> for why the threshold is one rather than zero.
    /// </remarks>
    internal bool SortIsSubstantial => SortExpression is { Length: > 1 };
}

#endregion

#region The wire shapes - one chunk, and one drop-down child payload

/// <summary>
/// One serialized chunk of a carrier's changeset, together with the two one-based chunk counters and
/// the serialization selector the receiving side needs in order to decode it.
/// </summary>
/// <remarks>
/// <para>
/// Reproduces the four arguments of
/// <c>tasking.Event OnDataChunk(ref blbData, nChunkCnt, nChunkIdx, false)</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L183</c>, <c>:L219</c>] and
/// its empty-result form <c>OnDataChunk(ref blbData, 1, 1, false)</c> [<c>:L230</c>], and it maps
/// field for field onto the published <c>persistence.v1.QueryDataChunk</c> message.
/// </para>
/// <para>
/// C-K - A CLASS RATHER THAN A RECORD, DELIBERATELY. A record would synthesize value equality over
/// <see cref="State"/>, and a generated protobuf message's equality is a deep field comparison whose
/// cost and whose treatment of an absent state are both surprising at a call site that only wanted to
/// know whether two chunks were the same delivery. Since no behaviour here needs chunk equality, the
/// safer shape is one that does not offer it.
/// </para>
/// </remarks>
internal sealed class ChangesetChunk
{
    /// <summary>
    /// The value this codec stamps on the serialization selector of every chunk it emits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ALWAYS FALSE. The legacy passes <see langword="true"/> on exactly one path - a crosstab or
    /// composite carrier delivered whole as a single full-state chunk
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L97</c>] - and
    /// <see langword="false"/> on all three changeset handovers [<c>:L183</c>, <c>:L219</c>,
    /// <c>:L230</c>].
    /// </para>
    /// <para>
    /// GETTING IT WRONG ROUTES THE PAYLOAD TO THE WRONG CODEC, because the receiving side selects
    /// its decoder from this flag alone [<c>n_cst_threading_task_sqlquery.sru:L189</c>,
    /// <c>:L215</c>, <c>:L232</c>]. It is a constant rather than a constructor argument precisely so
    /// that no call site can supply the other value by accident.
    /// </para>
    /// </remarks>
    internal const bool ChangesetSelector = false;

    /// <summary>
    /// Creates a chunk.
    /// </summary>
    /// <param name="state">The carrier state travelling with the payload, or <see langword="null"/> when none is carried.</param>
    /// <param name="chunkCount">The total number of chunks in this sequence. One-based domain.</param>
    /// <param name="chunkIndex">This chunk's ONE-BASED index; the first chunk is 1, never 0.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="chunkCount"/> is not positive, or <paramref name="chunkIndex"/> is outside 1
    /// through <paramref name="chunkCount"/>.
    /// </exception>
    /// <remarks>
    /// R9 item 2. Both counters are validated in the ONE-BASED domain and neither is rebased. A
    /// zero index is rejected rather than tolerated, because the receive side's
    /// <c>if current = 1</c> reset test [<c>:L192</c>, <c>:L218</c>, <c>:L235</c>] would silently
    /// never fire for a zero-based stream and the target would accumulate rows across sequences.
    /// </remarks>
    internal ChangesetChunk(CarrierState? state, long chunkCount, long chunkIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkIndex, 1L);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(chunkIndex, chunkCount);

        State = state;
        ChunkCount = chunkCount;
        ChunkIndex = chunkIndex;
    }

    /// <summary>
    /// The changeset this chunk carries, as the PUBLISHED typed carrier state, or
    /// <see langword="null"/> for the legacy's zero-length blob. Legacy <c>ref blob blbdata</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TYPED RATHER THAN OPAQUE BYTES, AND THE CHANGE IS NOT INTERNAL TIDYING. The published
    /// <c>persistence.v1.QueryDataChunk.state</c> is deliberately not a <c>bytes</c> field whose format is
    /// the codecs' private concern on both sides: that shape leaves DataServices - which references
    /// only the contracts project - unable to decode it without duplicating this file or taking a
    /// project reference on Persistence internals. Section 2b of <c>persistence.v1.proto</c> records
    /// the full reasoning.
    /// </para>
    /// <para>
    /// <see langword="null"/> IS THE ZERO-LENGTH BLOB, and the mapping is exact rather than a
    /// convention invented here: <c>state</c> is a MESSAGE field, so protobuf tracks its presence
    /// natively, and an absent state is what the receive side's <c>if Len(blbData) &gt; 0</c> test
    /// [<c>n_cst_threading_task_sqlquery.sru:L188</c>] reads as false. It is DISTINCT from a state
    /// carrying three empty segments, which is a legitimately empty result rather than no payload.
    /// </para>
    /// </remarks>
    internal CarrierState? State { get; }

    /// <summary>
    /// The total number of chunks in this sequence. Legacy <c>count</c>, argument 2 of the handover.
    /// </summary>
    internal long ChunkCount { get; }

    /// <summary>
    /// This chunk's one-based index. Legacy <c>current</c>, argument 3 of the handover.
    /// </summary>
    internal long ChunkIndex { get; }

    /// <summary>
    /// The serialization selector, always <see cref="ChangesetSelector"/>. Legacy <c>fullstate</c>,
    /// argument 4 of the handover.
    /// </summary>
    internal bool FullState => ChangesetSelector;

    /// <summary>
    /// Whether this is the first chunk of the sequence, which is the condition on which the receiving
    /// side clears its target before applying.
    /// </summary>
    /// <remarks>
    /// Reproduces <c>if current = 1 then</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L192</c>, <c>:L218</c>,
    /// <c>:L235</c>]. See DECISION 2 position (d): a reset HERE is safe because it precedes the apply.
    /// </remarks>
    internal bool IsFirstChunk => ChunkIndex == 1L;

    /// <summary>
    /// Whether this is the last chunk of the sequence, which is the condition on which the receiving
    /// side clears its update flags.
    /// </summary>
    /// <remarks>
    /// Reproduces <c>if count = current then</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L198</c>, <c>:L222</c>,
    /// <c>:L239</c>]. Written as an equality between the two counters, exactly as the legacy writes
    /// it, rather than as a comparison against a separately derived total.
    /// </remarks>
    internal bool IsLastChunk => ChunkCount == ChunkIndex;

    /// <summary>
    /// Whether the payload is empty, which on the receiving side MEANS "clear the target" rather than
    /// "there is nothing to do".
    /// </summary>
    /// <remarks>
    /// Reproduces <c>if Len(blbData) &gt; 0 then ... else rtCode = 1 : Reset()</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L188-L210</c>]. The
    /// send side produces this shape on its empty-result arm at <c>:L230</c>.
    /// </remarks>
    internal bool IsEmptyPayload => State is null;
}

/// <summary>
/// One drop-down child result, delivered alongside the main result and keyed by the column whose
/// child it is.
/// </summary>
/// <remarks>
/// <para>
/// Reproduces the two arguments of
/// <c>tasking.Event OnChildDataReceived(sColName, ref blbData)</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L134</c>], and maps onto the
/// published <c>persistence.v1.QueryChildDataChunk</c> message.
/// </para>
/// <para>
/// A class rather than a record for the same reason as <see cref="ChangesetChunk"/>.
/// </para>
/// </remarks>
internal sealed class ChangesetChildPayload
{
    /// <summary>
    /// Creates a child payload.
    /// </summary>
    /// <param name="columnName">
    /// The column name, resolved by the legacy from the column's own Name property at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L119</c>.
    /// </param>
    /// <param name="state">
    /// The child changeset as published typed carrier state, or <see langword="null"/> for the
    /// legacy's zero-length blob, which means "clear the child".
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="columnName"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="columnName"/> is <see langword="null"/>.</exception>
    internal ChangesetChildPayload(string columnName, CarrierState? state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);

        ColumnName = columnName;
        State = state;
    }

    /// <summary>
    /// The column whose drop-down child this payload carries. Legacy <c>string name</c>.
    /// </summary>
    internal string ColumnName { get; }

    /// <summary>
    /// The child changeset as published typed carrier state, or <see langword="null"/> for the legacy's
    /// zero-length blob. Legacy <c>ref blob blbdata</c>. See <see cref="ChangesetChunk.State"/> for why
    /// this is typed and why absence rather than emptiness carries the zero-length reading.
    /// </summary>
    internal CarrierState? State { get; }

    /// <summary>
    /// Whether the payload is empty, which the child receiver treats as success without applying
    /// anything.
    /// </summary>
    /// <remarks>
    /// Reproduces <c>if Len(blbData) &gt; 0 then ... else rtCode = 1</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L104-L108</c>]. Note
    /// that the child receiver has ALREADY reset its target by this point [<c>:L103</c>], so an empty
    /// payload still clears the child - it just does so unconditionally rather than on this branch.
    /// </remarks>
    internal bool IsEmptyPayload => State is null;
}

/// <summary>
/// One drop-down child carrier on the SEND side, paired with the column it belongs to.
/// </summary>
/// <param name="ColumnName">
/// The column name, as the legacy resolves it at
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L119</c> and then embeds in
/// both of its error texts at <c>:L130</c> and <c>:L135</c>.
/// </param>
/// <param name="Child">
/// The child carrier, standing in for the <c>datawindowchild</c> the legacy obtains through
/// <c>Data.GetChild(sColName, ref dwcSrc)</c> at <c>:L120</c>.
/// </param>
/// <remarks>
/// <para>
/// WHY ELIGIBILITY IS DECIDED BY THE CALLER AND NOT HERE. The legacy discovers its children by
/// walking the column count and interrogating two describe properties per column
/// [<c>:L113-L118</c>], which is definition reflection rather than buffer work. This codec therefore
/// receives an already-filtered list, and exposes
/// <see cref="ChangesetCodec.IsChildEligible(string, string)"/> so the FILTER RULE itself is still
/// reproduced, documented and testable in one place.
/// </para>
/// <para>
/// A record here is safe: both members are reference-equality-friendly and no payload is involved.
/// </para>
/// </remarks>
internal sealed record ChangesetChildSource(string ColumnName, DataWindowBufferStore Child);

#endregion

#region Where the temporary carrier got its shape from

/// <summary>
/// Which of the two routes supplied the temporary carrier's shape in the sorted multi-chunk branch.
/// </summary>
/// <remarks>
/// <para>
/// Reproduces the choice at
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L153-L157</c>:
/// </para>
/// <code>
/// if Data.DataObject &lt;&gt; "" then
///     dsTmp.DataObject = Data.DataObject
/// else
///     dsTmp.Create(Data.Describe("DataWindow.Syntax"))
/// end if
/// </code>
/// <para>
/// C-K. This is surfaced as a named result rather than kept internal because it is the only
/// observable difference between two otherwise identical temporary carriers, and DEFECT 1's
/// workaround is only faithful if the carrier it stages through has the SOURCE's shape. A test can
/// assert which route ran; without this it could only assert that the transfer succeeded.
/// </para>
/// </remarks>
internal enum TemporaryCarrierProvenance
{
    /// <summary>
    /// The source's data object name was non-empty and was copied, reproducing
    /// <c>dsTmp.DataObject = Data.DataObject</c> [<c>:L154</c>].
    /// </summary>
    DataObjectName,

    /// <summary>
    /// The source had no data object name, so the temporary carrier was constructed from the source's
    /// generated syntax, reproducing <c>dsTmp.Create(Data.Describe("DataWindow.Syntax"))</c>
    /// [<c>:L156</c>].
    /// </summary>
    Syntax,
}

#endregion

#region The handover surface - the four legacy events this codec raises

/// <summary>
/// The four events the changeset send path raises, expressed as one narrow callback surface.
/// </summary>
/// <remarks>
/// <para>
/// Each member reproduces one legacy event and nothing else, so the mapping is one for one and
/// auditable:
/// </para>
/// <list type="table">
/// <item>
/// <term><see cref="CreateDataAsync"/></term>
/// <description>
/// <c>tasking.Event OnCreateData(Data.Describe("DataWindow.Syntax"))</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L104</c>].
/// </description>
/// </item>
/// <item>
/// <term><see cref="SendChunkAsync"/></term>
/// <description>
/// <c>tasking.Event OnDataChunk(...)</c> [<c>:L183</c>, <c>:L219</c>, <c>:L230</c>].
/// </description>
/// </item>
/// <item>
/// <term><see cref="SendChildChunkAsync"/></term>
/// <description>
/// <c>tasking.Event OnChildDataReceived(sColName, ref blbData)</c> [<c>:L134</c>].
/// </description>
/// </item>
/// <item>
/// <term><see cref="ReportError"/></term>
/// <description>
/// <c>Event OnError(RetCode.E_INTERNAL_ERROR, ...)</c> [<c>:L130</c>, <c>:L135</c>, <c>:L172</c>,
/// <c>:L184</c>, <c>:L206</c>, <c>:L220</c>].
/// </description>
/// </item>
/// </list>
/// <para>
/// C-K - WHY THE HANDOVERS ARE ASYNCHRONOUS AND THE ERROR REPORT IS NOT. The three handovers cross
/// what used to be an in-process call and is now a network edge: a chunk leaves this service over
/// gRPC, so the operation can fail in transit in a way its in-process original could not, and it must
/// be awaitable. The error report does not cross anything - the legacy raises it on the task object
/// in the same thread and ignores any result - so it stays synchronous and returns nothing, which is
/// also what keeps the failure paths below free of a second thing that can fail.
/// </para>
/// <para>
/// C-B - A NEGATIVE RESULT FROM A HANDOVER MEANS FAILURE, and the legacy tests for exactly that:
/// <c>if tasking.Event OnDataChunk(...) &lt; 0 then</c>. Zero and positive both mean success. The
/// codec never interprets the magnitude.
/// </para>
/// </remarks>
internal interface IChangesetTransferSink
{
    /// <summary>
    /// Asks the receiving side to build itself from the source's generated syntax.
    /// </summary>
    /// <param name="syntax">The source's syntax, verbatim.</param>
    /// <param name="cancellationToken">The task's cancellation token.</param>
    /// <returns>A negative value on failure; zero or positive on success.</returns>
    /// <remarks>
    /// Reproduces <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L104</c>. THE
    /// LEGACY IGNORES THIS RESULT - the event is declared without a return type - and so does the
    /// codec; it is surfaced here only so an implementation is not forced to swallow its own faults
    /// silently, and the immediately following cancellation check at <c>:L105</c> is what the legacy
    /// actually reacts to.
    /// </remarks>
    ValueTask<long> CreateDataAsync(string syntax, CancellationToken cancellationToken);

    /// <summary>
    /// Hands one chunk of the main result over to the receiving side.
    /// </summary>
    /// <param name="chunk">The chunk. Its counters are one-based.</param>
    /// <param name="cancellationToken">The task's cancellation token.</param>
    /// <returns>A negative value on failure; zero or positive on success.</returns>
    /// <remarks>
    /// Reproduces <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L183</c> and
    /// <c>:L219</c>. On the empty-result arm at <c>:L230</c> the legacy DOES NOT TEST THE RESULT, and
    /// the codec reproduces that too.
    /// </remarks>
    ValueTask<long> SendChunkAsync(ChangesetChunk chunk, CancellationToken cancellationToken);

    /// <summary>
    /// Hands one drop-down child result over to the receiving side.
    /// </summary>
    /// <param name="payload">The child payload, carrying its column name.</param>
    /// <param name="cancellationToken">The task's cancellation token.</param>
    /// <returns>A negative value on failure; zero or positive on success.</returns>
    /// <remarks>
    /// Reproduces <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L134</c>.
    /// </remarks>
    ValueTask<long> SendChildChunkAsync(
        ChangesetChildPayload payload,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reports a fault the way the legacy reports one, before the codec returns its failure code.
    /// </summary>
    /// <param name="returnCode">
    /// The framework return code, always <c>RetCode.E_INTERNAL_ERROR</c> on this path.
    /// </param>
    /// <param name="errorText">
    /// The error text VERBATIM. The four texts this codec produces are
    /// <c>GetChanges Failed</c>, <c>TransData Failed</c>, <c>[column] GetChanges Failed</c> and
    /// <c>[column] TransData Failed</c>, and they are preserved exactly because they are observable in
    /// log records and in characterization recordings.
    /// </param>
    void ReportError(long returnCode, string errorText);
}

#endregion

#region The opaque payload format - GetChanges and SetChanges

/// <summary>
/// How much a payload's ORIGINAL values may be trusted, which is a property of the CALLER rather than of
/// the payload and therefore has to be stated at every apply.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>THE CONCURRENCY BASELINE IS THE ONE THING A CHANGESET CANNOT BE ALLOWED TO GUESS.</b> An
/// <c>updatewhere=1</c> predicate carries the key column plus the ORIGINAL value of every marked column
/// (AAP 0.6.3.2), so the originals in a payload are not decoration - they ARE the optimistic-concurrency
/// check. This codec used to substitute the caller's CURRENT value wherever an original was missing,
/// which produced a predicate built from the values the caller was writing: an ordinary update then
/// compared a row against itself and could not detect a lost race, and a caller that changed a KEY
/// without stating its old value had its statement aimed at the row named by the NEW key - a write to a
/// different row than the one it read, reported as success. Neither is a hypothetical; both follow
/// mechanically from a fabricated baseline.
/// </para>
/// <para>
/// <b>WHY THIS IS A MODE AND NOT ONE RULE FOR EVERYONE.</b> Two callers apply changesets and they are
/// not equally trustworthy, so one rule would have to be wrong for one of them:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///     The RETRIEVE path - <c>Tasks/TaskProxies/SqlQueryTaskProxy</c> moving chunks between carriers -
///     applies a payload THIS SERVICE PRODUCED, whose encoding is defined by
///     <see cref="ChangesetPayloadCodec.TryEncode"/>: an original is emitted only where it DIFFERS from
///     the current value, because the carrier answers the current value when no original was captured.
///     Absence there is a positive statement of equality, so requiring one would refuse this service's
///     own conforming output - and it would break the one shape that MUST survive the wire, a column
///     stamped <c>DataModified!</c> whose value did not move, which is how the legacy's self-assignment
///     workaround at <c>n_cst_thread_task_sqlupdate.sru:L155-L167</c> crosses the boundary at all.
///     </description>
///   </item>
///   <item>
///     <description>
///     The UPDATE path - <c>Tasks/SqlUpdateCarrier.SetChanges</c> - applies a payload composed by an
///     untrusted CALLER, and every statement generated from it carries a predicate read out of these
///     originals. There the contract's own requirement applies in full: a changed row states both halves
///     of every column it carries, or it is refused.
///     </description>
///   </item>
/// </list>
/// <para>
/// <b>AND THE STRICT MODE IS SCOPED TO THE ROWS WHOSE BASELINE IS ACTUALLY READ, which is narrower than
/// "every row" and deliberately so.</b> Requiring a baseline from a row no statement is generated for
/// would refuse conforming payloads while preventing nothing. The two arms that read originals are every
/// <c>Delete!</c> buffer row - membership in that buffer IS the pending delete, so a <c>DELETE ... WHERE</c>
/// is generated whatever its status - and every <see cref="ItemStatus.DataModified"/> row in a modifiable
/// buffer, which becomes an <c>UPDATE</c> or, when the key moved under <c>updatekeyinplace=no</c>, a
/// <c>DELETE</c> plus an <c>INSERT</c> [<c>Tasks/SqlUpdateCarrier.ApplyUpdate</c>]. A row whose status is
/// <see cref="ItemStatus.New"/> or <see cref="ItemStatus.NewModified"/> in a modifiable buffer generates an
/// <c>INSERT</c>, which has no where clause and no prior state to describe; a
/// <see cref="ItemStatus.NotModified"/> row there generates nothing at all.
/// </para>
/// </remarks>
internal enum CarrierBaselineTrust
{
    /// <summary>
    /// The producer's own encoding is honoured: a column with no stated original is UNCHANGED, so its
    /// baseline is its current value because the two are the same value.
    /// </summary>
    /// <remarks>
    /// For payloads this service produced. It is the transfer-path reading and it is exact rather than
    /// lenient - see the type-level remarks.
    /// </remarks>
    AsStated = 0,

    /// <summary>
    /// Every column of every row whose baseline becomes a where clause - a <c>Delete!</c> buffer row, or a
    /// <see cref="ItemStatus.DataModified"/> row in a modifiable buffer - must state its original
    /// explicitly, and a payload that omits one is refused rather than read against a fabricated baseline.
    /// </summary>
    /// <remarks>
    /// For the update path, where the originals become an optimistic-concurrency predicate. This is
    /// literally what AAP 0.6.3.2 requires a payload to transmit, so the requirement narrows nothing a
    /// conforming caller was permitted to omit. Rows that generate an <c>INSERT</c> or no statement at all
    /// are outside it - see the type-level remarks for why the scope is the predicate rather than the row
    /// shape.
    /// </remarks>
    RequiredOnChangedRows = 1,
}

/// <summary>
/// Encodes a carrier's changed rows into an opaque binary payload and applies such a payload back
/// onto a carrier: the managed stand-in for PowerBuilder's <c>GetChanges</c> and <c>SetChanges</c>.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS IS AN INTERFACE. Three reasons, and the third is decisive. First, the payload format is a
/// genuinely separable concern from the chunking algorithm, and separating them is what keeps
/// <see cref="ChangesetCodec"/> readable against its locators. Second, it is the seam at which a
/// future format version can be introduced without touching the chunk loop. Third, and decisively,
/// TWO OF THE LEGACY'S OWN BEHAVIOURS ARE ONLY REACHABLE THROUGH A SUBSTITUTABLE ENCODER: the
/// <c>if ... GetChanges(ref blbData) &lt; 0 then</c> failure arm
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L129</c>, <c>:L171</c>,
/// <c>:L205</c>], and the <c>if rtCode &gt; 1 then rtCode = -1</c> coercion of a PARTIAL apply
/// [<c>n_cst_threading_task_sqlquery.sru:L141</c>, <c>:L250</c>]. A managed encoder over an in-memory
/// buffer never fails and never partially succeeds, so without this seam both arms would be dead code
/// and the per-service coverage gate (C-H) would be unreachable for them.
/// </para>
/// <para>
/// THE RETURN CONVENTION IS PowerBuilder's, NOT THE FRAMEWORK'S. These members answer
/// <see cref="DataWindowBufferStore.DataStoreSuccess"/> for success and
/// <see cref="DataWindowBufferStore.DataStoreFailure"/> for failure, and an apply may answer a value
/// GREATER than one to mean "applied, but the payload and the target disagree about their
/// definitions". That is the convention the legacy's own tests are written against, and mixing it
/// with the framework's zero-is-success algebra would be a silent defect - see the remarks on
/// <see cref="DataWindowBufferStore.DataStoreSuccess"/>.
/// </para>
/// </remarks>
internal interface IChangesetPayloadCodec
{
    /// <summary>
    /// Encodes the changed rows of <paramref name="source"/> into a payload.
    /// </summary>
    /// <param name="source">The carrier to read. Not modified.</param>
    /// <param name="state">
    /// The projected carrier state, or <see langword="null"/> when the result is not
    /// <see cref="DataWindowBufferStore.DataStoreSuccess"/>.
    /// </param>
    /// <returns>
    /// <see cref="DataWindowBufferStore.DataStoreSuccess"/> or
    /// <see cref="DataWindowBufferStore.DataStoreFailure"/>.
    /// </returns>
    /// <remarks>
    /// Reproduces <c>GetChanges(ref blbData)</c>. See DECISION 5 for why the managed shape returns the
    /// payload through <see langword="out"/> rather than through a <c>ref</c> parameter.
    /// </remarks>
    long TryEncode(DataWindowBufferStore source, out CarrierState? state);

    /// <summary>
    /// Applies a payload onto <paramref name="target"/>, merging its rows into the target's buffers.
    /// </summary>
    /// <param name="target">The carrier to write.</param>
    /// <param name="state">The carrier state to apply. <see langword="null"/> is malformed input.</param>
    /// <param name="baselineTrust">
    /// How much the payload's ORIGINAL values may be trusted, which depends on who composed the payload.
    /// See <see cref="CarrierBaselineTrust"/>: the transfer path passes
    /// <see cref="CarrierBaselineTrust.AsStated"/> and the update path passes
    /// <see cref="CarrierBaselineTrust.RequiredOnChangedRows"/>.
    /// </param>
    /// <returns>
    /// <see cref="DataWindowBufferStore.DataStoreSuccess"/> on a clean apply,
    /// <see cref="DataWindowBufferStore.DataStoreFailure"/> on a malformed payload, or a value greater
    /// than one for a partial apply.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Reproduces <c>SetChanges(blbData)</c>. A MALFORMED PAYLOAD MUST ANSWER A CODE AND MUST NOT
    /// THROW: the legacy reacts to a code, and an exception escaping here would be a failure mode the
    /// oracle does not have (C-B).
    /// </para>
    /// <para>
    /// THE TRUST MODE IS A REQUIRED ARGUMENT RATHER THAN A DEFAULT, deliberately. A default would let a
    /// future caller inherit the lenient reading silently, and the one caller that must not is the one
    /// whose payload becomes an optimistic-concurrency predicate. Stating it at the call site is what
    /// makes the choice reviewable.
    /// </para>
    /// </remarks>
    long TryApply(
        DataWindowBufferStore target,
        CarrierState? state,
        CarrierBaselineTrust baselineTrust);
}

/// <summary>
/// The default carrier-state projection: for each of the three buffers, every changed row with its
/// item status, its per-column item statuses, and both the current and the ORIGINAL value of every
/// column it has one for - expressed as the PUBLISHED <see cref="CarrierState"/> message.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS IS A PROJECTION AND NOT A SERIALIZER. Writing an opaque
/// little-endian byte format of its own invention here, with the published contract describing the
/// resulting field as "opaque - its internal format is the changeset and full-state codecs' concern on
/// both sides", is not implementable across the boundary this type sits on: DataServices references ONLY
/// <c>shared/PowerFramework.Contracts</c> (constraint C-A), so a format owned privately here leaves the
/// consumer with two choices, both forbidden - duplicate this file, or take a project reference on
/// Persistence internals. Section 2b of <c>persistence.v1.proto</c> records the reasoning and the
/// resolution; this type is the send-and-receive half of it.
/// </para>
/// <para>
/// WHY BOTH VALUES PER COLUMN, AND WHY THAT IS NOT OPTIONAL. The golden-master fixture declares
/// <c>updatewhere=1</c> [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14</c>] with
/// <c>updatewhereclause=yes</c> on all six columns [<c>:L8-L13</c>], so the generated update
/// statement's where clause carries the key column PLUS THE ORIGINAL VALUE OF EVERY UPDATEABLE
/// COLUMN. A payload that carried only current values could not express optimistic concurrency at all,
/// and the row loss would be invisible until a conflict silently failed to be detected. That is why
/// <see cref="DataWindowRow.OriginalValues"/> is populated here and not treated as a retrieve-path
/// nicety - <b>for every column projected, including the ones whose original still equals their
/// current value.</b> Skipping the agreeing ones is an inference this codec's receive half can make and
/// a consumer of the CONTRACT cannot be required to: it would leave a freshly retrieved row carrying no
/// baseline at all, and AAP 0.6.3.2 states the obligation with no exemption.
/// </para>
/// <para>
/// NO JSON AND NO XML (C-D), AND NO HAND-ROLLED BINARY EITHER. Document serialization would reach into
/// the deferred Documents parser family; a private binary format is what the review found
/// unimplementable. Protobuf's own canonical encoding is deterministic for the shapes used here - the
/// generated writer emits fields in ascending number and this projection fills every repeated field in
/// a fixed order - so equal input yields byte-identical output, which is the repeatability the
/// Golden-Master technique requires of both master and candidate.
/// </para>
/// <para>
/// DETERMINISM DETAILS THAT MATTER, AND THEY ARE THE SAME ONES AS BEFORE. Columns are emitted in
/// ASCENDING COLUMN NUMBER, taken from <see cref="CarrierRow.AssignedColumnNumbers"/>, which is ordered
/// for exactly this reason - a dictionary's enumeration order is unspecified and would make the payload
/// vary between runs. Segments are emitted in the fixed canonical order primary, delete, filter. A
/// decimal travels as canonical invariant TEXT through <see cref="CarrierValue"/>, which preserves the
/// declared scale, so the fixture's <c>salary decimal(2)</c> round-trips as a two-place decimal rather
/// than being normalised.
/// </para>
/// <para>
/// THE VALUE DOMAIN IS THE PUBLISHED ONE. <see cref="CarrierValue"/> is the single mapping onto
/// <c>common.v1.AnyValue</c>'s eleven arms, and it WIDENS on the way out per the contract's own rule -
/// a <see cref="short"/> reads back as a <see cref="long"/>. A value outside those arms, of which
/// <see cref="TimeSpan"/> is the reachable case, is answered with
/// <see cref="DataWindowBufferStore.DataStoreFailure"/> rather than encoded: that is the legacy's own
/// <c>if ... GetChanges(ref blbData) &lt; 0 then</c> arm, and the reasoning is on
/// <see cref="CarrierValue"/> itself.
/// </para>
/// <para>
/// WHICH ROWS COUNT AS CHANGED. For <c>Primary!</c> and <c>Filter!</c>, a row is included when
/// <see cref="ItemStatusMachine.IsModified"/> holds OR its status is <see cref="ItemStatus.New"/>.
/// The predicate comes from <c>Buffers/ItemStatus.cs</c> and the one addition is deliberate:
/// <see cref="ItemStatusMachine.IsModified"/> reproduces the legacy's MODIFIED-ROW WALK, which by
/// measurement covers <see cref="ItemStatus.DataModified"/> and
/// <see cref="ItemStatus.NewModified"/> only, whereas a PAYLOAD must also be able to carry a row that
/// has been inserted and not yet edited. <c>common.v1.proto</c> reaches the same conclusion for the
/// same reason when it explains why it carries all four statuses: a payload that cannot represent a
/// row's actual state is a lossy payload. Every row of <c>Delete!</c> is included regardless of
/// status, because a row's presence in that buffer IS the change.
/// </para>
/// <para>
/// C-H. Nothing here touches storage, a clock, a socket or a file, so the whole projection is
/// exercisable with no database and no container.
/// </para>
/// </remarks>
internal sealed class ChangesetPayloadCodec : IChangesetPayloadCodec
{
    /// <summary>
    /// The buffers projected, in the fixed canonical order they are projected in.
    /// </summary>
    /// <remarks>
    /// THE ORDER IS PART OF THE CONTRACT, not an implementation detail: <c>persistence.v1.CarrierState</c>
    /// requires exactly one segment per buffer IN THIS ORDER, and <see cref="TryApply"/> rejects any
    /// other arrangement. Varying it would also change the serialized bytes for identical data and break
    /// the golden-master comparison.
    /// </remarks>
    internal static readonly DwBuffer[] SerializedBuffers =
    [
        DwBuffer.Primary,
        DwBuffer.Delete,
        DwBuffer.Filter,
    ];

    /// <summary>
    /// The row-status sentinel column, restated locally so no literal zero appears below.
    /// </summary>
    private const int RowStatusColumn = ItemStatusMachine.RowStatusColumn;

    /// <inheritdoc/>
    public long TryEncode(DataWindowBufferStore source, out CarrierState? state)
    {
        ArgumentNullException.ThrowIfNull(source);

        state = null;

        CarrierState projected = new()
        {
            // The codec discriminator travels so that an apply cannot silently move a carrier onto the
            // other codec's arm. The raw legacy value, as `Long(Describe("DataWindow.Processing"))`
            // yields it.
            Processing = source.Processing.Value,
        };

        foreach (DwBuffer dwBuffer in SerializedBuffers)
        {
            if (!TryProjectBufferSegment(source, dwBuffer, out CarrierBufferSegment? segment))
            {
                // A value this boundary cannot express. `GetChanges` answers a code, never an
                // exception, so this becomes the -1 the legacy's `if ... GetChanges(ref blbData) < 0
                // then` arm tests for (C-B). The partially built state is discarded rather than sent.
                return DataWindowBufferStore.DataStoreFailure;
            }

            projected.Segments.Add(segment);
        }

        state = projected;

        return DataWindowBufferStore.DataStoreSuccess;
    }

    /// <inheritdoc/>
    public long TryApply(
        DataWindowBufferStore target,
        CarrierState? state,
        CarrierBaselineTrust baselineTrust)
    {
        ArgumentNullException.ThrowIfNull(target);

        // =========================================================================================
        //  EVERY STRUCTURAL CHECK RUNS BEFORE THE FIRST MUTATION, AND THAT ORDERING IS THE POINT.
        //  -------------------------------------------------------------------------------------
        //  This apply MERGES rows into a live target - it does not replace it - so a fault detected
        //  half way through would leave the target holding some of a payload it then reports as
        //  rejected, and the caller has no way to unwind that. The legacy has the same shape and the
        //  same exposure, which is why the checks are hoisted rather than interleaved.
        //
        //  WHAT IS CHECKED, AND WHY COUNTING THE SEGMENTS IS NOT ENOUGH. `persistence.v1.CarrierState`
        //  requires EXACTLY ONE segment per buffer in canonical order. A check that only counted three
        //  would accept three segments all tagged Primary - which loads the Delete! and Filter! rows
        //  into the primary buffer and returns a row count nothing disagrees with. It would equally
        //  accept a Filter! row filed inside the Primary segment, and the Filter buffer's row order is
        //  INVERTED relative to the source [n_cst_thread_task_sqlupdate.sru:L235-L238], so a mis-filed
        //  row is read in the wrong direction and pairs with the wrong data.
        //
        //  AND THE PROCESSING KIND IS RECONCILED RATHER THAN ADOPTED. Reading and discarding it under the
        //  reading that "detection is the caller's business" is the tempting shortcut. It is not the
        //  caller's business: the two sides build
        //  their carriers from their own definitions, so a disagreement means they disagree about which
        //  serialization is even applicable, and merging a crosstab image into a tabular carrier is the
        //  outcome of trusting the sender. An UNASSIGNED target - a carrier whose data object has not
        //  been set - adopts the payload's kind instead, because that is not a disagreement.
        // =========================================================================================
        if (state is null)
        {
            // No payload at all. The caller's own empty-payload arm handles the legacy's zero-length
            // blob; reaching here with null means a producer sent a chunk with neither.
            return DataWindowBufferStore.DataStoreFailure;
        }

        if (!TryValidateSegments(state, target, out DwBuffer[]? order) || order is null)
        {
            return DataWindowBufferStore.DataStoreFailure;
        }

        // A pre-flight read of every value, so that an unrepresentable one is refused before any row is
        // admitted. The rows are materialised in the same pass, in canonical buffer order.
        List<(DwBuffer Buffer, DataWindowRow Row, List<AdmittedColumn> Columns)> admitted = [];

        for (int index = 0; index < order.Length; index++)
        {
            CarrierBufferSegment segment = state.Segments[index];

            foreach (DataWindowRow row in segment.Rows)
            {
                if (!TryReadRow(row, baselineTrust, out List<AdmittedColumn>? columns)
                    || columns is null)
                {
                    return DataWindowBufferStore.DataStoreFailure;
                }

                admitted.Add((order[index], row, columns));
            }
        }

        // Adopt the payload's kind only when the target has none of its own - see the banner.
        if (target.Processing.Value == DataWindowProcessing.Unassigned.Value)
        {
            target.Processing = new DataWindowProcessing(state.Processing);
        }

        foreach ((DwBuffer dwBuffer, DataWindowRow row, List<AdmittedColumn> columns) in admitted)
        {
            Admit(target, dwBuffer, row, columns);
        }

        return DataWindowBufferStore.DataStoreSuccess;
    }

    /// <summary>
    /// One column of an inbound row, already read into carrier values.
    /// </summary>
    /// <param name="ColumnNumber">The one-based column number. R9: never rebased.</param>
    /// <param name="Status">That column's own item status.</param>
    /// <param name="Current">The current value.</param>
    /// <param name="Original">
    /// The value at the last baseline. Equal to <paramref name="Current"/> where the payload omitted an
    /// original, which the contract defines as "unchanged since the baseline" - and which is why such a
    /// column also reads <see cref="ItemStatus.NotModified"/> unless its row is insert-shaped. On the
    /// update path an omitted original is refused before it reaches here
    /// [<see cref="CarrierBaselineTrust.RequiredOnChangedRows"/>].
    /// </param>
    private readonly record struct AdmittedColumn(
        int ColumnNumber,
        ItemStatus Status,
        object? Current,
        object? Original);

    /// <summary>
    /// Projects one buffer's eligible rows onto a segment.
    /// </summary>
    /// <param name="source">The carrier to read.</param>
    /// <param name="dwBuffer">The buffer to project.</param>
    /// <param name="segment">The segment, or <see langword="null"/> when a value is unrepresentable.</param>
    /// <returns><see langword="false"/> when a value is outside the published value domain.</returns>
    /// <remarks>
    /// AN EMPTY SEGMENT IS STILL EMITTED, because the contract requires one segment per buffer whether
    /// or not it has rows - on a freshly retrieved carrier the delete segment is normally empty, and
    /// omitting it would make a conforming payload indistinguishable from a truncated one.
    /// </remarks>
    private static bool TryProjectBufferSegment(
        DataWindowBufferStore source,
        DwBuffer dwBuffer,
        out CarrierBufferSegment? segment)
    {
        segment = null;

        CarrierBufferSegment projected = new() { Buffer = dwBuffer };
        long rowCount = RowCountOf(source, dwBuffer);

        // R9: rows are ONE-BASED and the bound is INCLUSIVE, matching the carrier surface. There is no
        // minus one and no less-than here.
        for (long rowNumber = ItemStatusMachine.FirstRowNumber; rowNumber <= rowCount; rowNumber++)
        {
            ItemStatus rowStatus = source.GetItemStatus(rowNumber, RowStatusColumn, dwBuffer);

            if (!IsPayloadRow(dwBuffer, rowStatus))
            {
                continue;
            }

            if (!TryProjectRow(source, dwBuffer, rowNumber, rowStatus, out DataWindowRow? row))
            {
                return false;
            }

            projected.Rows.Add(row);
        }

        segment = projected;

        return true;
    }

    /// <summary>
    /// Projects one row onto the published row message.
    /// </summary>
    /// <param name="source">The carrier to read.</param>
    /// <param name="dwBuffer">The buffer the row is in.</param>
    /// <param name="rowNumber">The one-based row number.</param>
    /// <param name="rowStatus">The row's own status, already read through the column-zero sentinel.</param>
    /// <param name="row">The projection, or <see langword="null"/> when a value is unrepresentable.</param>
    /// <returns><see langword="false"/> when a value is outside the published value domain.</returns>
    /// <remarks>
    /// AN ORIGINAL IS EMITTED FOR EVERY COLUMN THE ROW CARRIES, agreeing with the current value or not.
    /// An earlier form emitted one only where it DIFFERED, which the contract then defined as the encoding
    /// of "unchanged since the last baseline"; AAP 0.6.3.2 leaves no room for that reading, because on a
    /// freshly retrieved row every column agrees and the row would carry no baseline at all. The value
    /// emitted is the one the carrier CAPTURED at its last baseline, which for a <c>blob</c> is a content
    /// snapshot rather than an alias - the carrier hands out defensive copies, so the emitted original must
    /// be read from the baseline store and never from the live array.
    /// </remarks>
    private static bool TryProjectRow(
        DataWindowBufferStore source,
        DwBuffer dwBuffer,
        long rowNumber,
        ItemStatus rowStatus,
        out DataWindowRow? row)
    {
        row = null;

        CarrierRow carrierRow = source.RowAt(rowNumber, dwBuffer);

        DataWindowRow projected = new()
        {
            Buffer = dwBuffer,
            Row = rowNumber,
            ItemStatus = rowStatus,
        };

        // Materialised once: the ordered enumeration is walked once here, and ascending order is what
        // makes the payload reproducible across runs.
        foreach (int columnNumber in carrierRow.AssignedColumnNumbers)
        {
            object? current = carrierRow.GetValue(columnNumber);

            if (!CarrierValue.TryToWire(current, out AnyValue? currentValue))
            {
                return false;
            }

            projected.Columns.Add(new ColumnValue
            {
                // BOTH IDENTIFIERS, WHEN THE CARRIER KNOWS BOTH. The legacy blob carries ordinals and no
                // names, so the name comes from the carrier's own recorded column order rather than from
                // the blob - see DataWindowBufferStore.ColumnNames. It is the EMPTY STRING when the
                // carrier was never told the names, which is a carrier built from a caller-supplied
                // changeset; a fabricated name would be worse than an absent one. The contract requires
                // both wherever both are known [common.v1.ColumnValue].
                ColumnName = source.ColumnNameOf(columnNumber),
                ColumnId = columnNumber,
                Value = currentValue,
                ItemStatus = source.GetItemStatus(rowNumber, columnNumber, dwBuffer),
            });

            object? original = carrierRow.GetOriginalValue(columnNumber);

            // 🔴 ONE ORIGINAL FOR EVERY COLUMN PROJECTED, INCLUDING THE ONES THAT DID NOT MOVE. An earlier
            // form skipped a column whose original equalled its current value, on the reasoning that a
            // consumer could infer the omitted baseline from the current value - which is true of THIS
            // codec's own receive half and false of the contract. AAP 0.6.3.2 states the obligation without
            // an exemption: the payload must transmit, per row, BOTH the current and the original value of
            // EVERY MARKED COLUMN, because `updatewhere=1` over six columns each marked
            // `updatewhereclause=yes` [ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14] builds a WHERE
            // clause from all six originals. Omitting the agreeing ones made a FRESHLY RETRIEVED row carry
            // no baseline at all, so a consumer holding only the payload had to RECONSTRUCT the concurrency
            // predicate from an absence - and a consumer that read the absence as "no baseline exists"
            // rather than as "equal to current" would either fabricate one or drop the predicate, which is
            // the silent overwrite AAP 0.6.3.8 forbids outright.
            //
            // AN EMPTY LIST NOW MEANS EXACTLY ONE THING - the row has NO PRIOR STATE, which is an
            // insert-shaped row - instead of meaning that plus "every column happens to agree".
            if (!CarrierValue.TryToWire(original, out AnyValue? originalValue))
            {
                return false;
            }

            projected.OriginalValues.Add(new ColumnValue
            {
                ColumnName = source.ColumnNameOf(columnNumber),
                ColumnId = columnNumber,
                Value = originalValue,
            });
        }

        row = projected;

        return true;
    }

    /// <summary>
    /// Validates the segment set against the contract's exactly-one-per-buffer-in-canonical-order rule.
    /// </summary>
    /// <param name="state">The inbound state.</param>
    /// <param name="target">The carrier being applied onto, for the processing reconciliation.</param>
    /// <param name="order">The canonical buffer order, positionally aligned with the segments.</param>
    /// <returns><see langword="false"/> when the state is structurally invalid.</returns>
    private static bool TryValidateSegments(
        CarrierState state,
        DataWindowBufferStore target,
        out DwBuffer[]? order)
    {
        order = null;

        if (state.Segments.Count != SerializedBuffers.Length)
        {
            return false;
        }

        for (int index = 0; index < SerializedBuffers.Length; index++)
        {
            CarrierBufferSegment segment = state.Segments[index];

            // POSITIONAL EQUALITY, which covers duplication, omission, reordering and an undeclared
            // value in one test: segment n must be buffer n of the canonical order, and the generated
            // enum property returns the raw number for a value outside the domain, so an invented
            // buffer cannot equal any canonical one.
            if (segment.Buffer != SerializedBuffers[index])
            {
                return false;
            }

            foreach (DataWindowRow row in segment.Rows)
            {
                // A row whose own tag disagrees with its segment. See the banner in TryApply for what
                // accepting one costs.
                if (row.Buffer != segment.Buffer)
                {
                    return false;
                }

                if (row.Row < ItemStatusMachine.FirstRowNumber || !Enum.IsDefined(row.ItemStatus))
                {
                    return false;
                }
            }
        }

        // THE PROCESSING RECONCILIATION. An unassigned target adopts; a disagreeing one refuses.
        if (target.Processing.Value != DataWindowProcessing.Unassigned.Value
            && target.Processing.Value != state.Processing)
        {
            return false;
        }

        order = SerializedBuffers;

        return true;
    }

    /// <summary>
    /// Reads one inbound row's columns into carrier values, rejecting a malformed one.
    /// </summary>
    /// <param name="row">The inbound row.</param>
    /// <param name="baselineTrust">Whether the baseline values carried by the row may be trusted.</param>
    /// <param name="columns">The columns, in payload order.</param>
    /// <returns><see langword="false"/> when the row is structurally invalid.</returns>
    /// <remarks>
    /// <para>
    /// AN ORIGINAL NAMING A COLUMN THE ROW DOES NOT CARRY IS REJECTED, and a DUPLICATED column number is
    /// rejected in either list. Both would leave the restore below reading a column that is not there or
    /// applying one twice, and the second write would capture the wrong original.
    /// </para>
    /// <para>
    /// 🔴 <b>A ROW THAT SUPPLIED NO PER-COLUMN STATUS AT ALL IS READ THROUGH THE LEGACY ROW-STATUS
    /// CONVENTION, AND WITHOUT THAT EVERY CONTRACT-CONFORMANT UPDATE WAS REFUSED AS A CONCURRENCY
    /// CONFLICT.</b> <c>common.v1.ColumnValue.item_status</c> is declared proto3 <c>optional</c> and its
    /// own contract says absence is the NORMAL case, so a caller that sends a row stamped
    /// <c>DataModified!</c> with its six current values and its six originals - exactly the payload the
    /// published contract describes, and exactly what a retrieval hands back - carried no per-column
    /// status. Reading every one of those as <c>NotModified!</c> made
    /// <c>Tasks/SqlUpdateCarrier.ApplyUpdate</c> skip every column, generate no SET list, and report zero
    /// affected rows - which <see cref="Concurrency.ConflictDetector.IsConcurrencyMismatch"/> then
    /// classified as an optimistic-concurrency mismatch. The caller was told another writer had changed a
    /// row that nothing had touched, and the only way to make an update apply was to send a field the
    /// contract calls optional.
    /// </para>
    /// <para>
    /// THE CONVENTION IS THE ORACLE'S OWN, NOT AN INVENTION HERE. PowerBuilder reads A ROW's status with
    /// column index 0 - <c>GetItemStatus(nRow, 0, Primary!)</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L160</c>], the convention
    /// <c>common.v1.ItemStatus</c> documents - and a row is <c>DataModified!</c> precisely BECAUSE a
    /// column of it was modified. In-process the two levels cannot diverge, because the runtime maintains
    /// both; across this boundary a producer may state one and omit the other, so the row's own statement
    /// is what governs the columns it supplied.
    /// </para>
    /// <para>
    /// 🔴 <b>AND IT GOVERNS ONLY THE COLUMNS WHOSE VALUE ACTUALLY MOVED, WHICH IS WHAT MAKES THE
    /// RECONSTRUCTION FAITHFUL RATHER THAN MERELY PERMISSIVE.</b> In process, the per-column statuses of
    /// a <c>DataModified!</c> row are NOT all modified - the runtime flips a column's status when
    /// <c>SetItem</c> changes it and leaves the rest alone, which is exactly why PowerBuilder's generated
    /// SET list names the changed columns and not the updatable set
    /// [<c>Tasks/SqlUpdateCarrier.ApplyUpdate</c> reproduces that selection]. A payload carries both
    /// halves of every marked column - <c>updatewhere=1</c> requires the originals for the predicate
    /// (AAP 0.6.3.2) - so the pair itself says which columns moved, and reading it reconstructs precisely
    /// the state the in-process runtime would hold. Adopting the row's status for EVERY supplied column
    /// instead was observably wrong in two ways: it wrote a SET list wider than the oracle's, and it
    /// stamped the KEY column modified, which sent an ordinary update whose key had not changed down the
    /// <c>updatekeyinplace=no</c> DELETE-plus-INSERT path [<c>SqlUpdateCarrier.HasKeyChange</c>] and
    /// answered <c>rowsUpdated = 0</c> beside a delete and an insert the caller never asked for.
    /// </para>
    /// <para>
    /// THE VALUE TEST IS <see cref="CarrierValue.AreEquivalent"/>, NOT <c>Equals</c>, because
    /// <c>AnyValue</c> gives one number several faithful spellings: a retrieval answers a legacy
    /// <c>number</c> column through <c>double_value</c> and a caller re-sending it as <c>int64_value</c>
    /// is equally within the contract, so <c>28.0d</c> beside <c>28L</c> must read as "did not move" -
    /// which is also how the storage engine compares them. See that method for the full reasoning.
    /// </para>
    /// <para>
    /// 🔴 <b>AND WHERE NO ORIGINAL WAS STATED THE COLUMN READS UNCHANGED, UNLESS THE ROW IS
    /// INSERT-SHAPED.</b> Letting the row's statement stand for such a column is the tempting reading, on
    /// the grounds that a column with no baseline cannot be measured against one. The premise is true and
    /// the conclusion is backwards for an update: the column is stamped modified, so it enters the
    /// generated SET list, while the predicate beside it compares that column against the value being
    /// written - so a caller
    /// that changed a value and omitted its original wrote to whichever row held the NEW value, and a
    /// caller that changed a KEY that way bypassed the <c>updatekeyinplace=no</c> DELETE-plus-INSERT path
    /// entirely. Absence of an original is the contract's own encoding of "unchanged since the baseline",
    /// and that is what it reads as.
    /// </para>
    /// <para>
    /// THE INSERT-SHAPED ARM KEEPS THE ONE CASE WHERE ADOPTING IS RIGHT. A row whose own status is
    /// <c>New!</c> or <c>NewModified!</c> carries no originals at all because it has no prior state, and
    /// resolving its columns to <c>NotModified!</c> would not be the state the in-process runtime holds
    /// for a new row. No predicate is generated for an insert, so nothing here can be aimed at the wrong
    /// row.
    /// </para>
    /// <para>
    /// 🔴 <b>AND ON THE UPDATE PATH AN UNSTATED BASELINE IS REFUSED OUTRIGHT RATHER THAN INFERRED.</b>
    /// <see cref="CarrierBaselineTrust.RequiredOnChangedRows"/> requires every column of every row that
    /// is not insert-shaped to state its original, because those originals become the
    /// optimistic-concurrency predicate and a caller is not a trustworthy source of a value it is
    /// simultaneously overwriting. The transfer path passes
    /// <see cref="CarrierBaselineTrust.AsStated"/> instead, under which absence IS a statement of
    /// equality - see <see cref="CarrierBaselineTrust"/> for why the two callers cannot share one rule.
    /// </para>
    /// <para>
    /// A ROW WHOSE EVERY SUPPLIED COLUMN THEREFORE READS <c>NotModified!</c> - a row stamped modified in
    /// which nothing moved - is a payload CONTRADICTION and is answered as one further down the path,
    /// with the oracle's own invalid-update-data code rather than as a concurrency conflict
    /// [<see cref="Concurrency.ConflictDetector"/>]. It is deliberately not refused here: this codec
    /// admits a changeset for several callers and only the update walk knows which columns are updatable.
    /// </para>
    /// <para>
    /// <b>IT APPLIES ONLY WHEN NOT ONE COLUMN CARRIES EXPLICIT PRESENCE, WHICH IS WHAT KEEPS A PARTIAL
    /// UPDATE EXACT.</b> A caller that stamps the one column it changed is being explicit about all of
    /// them, and every column it left unstamped stays <c>NotModified!</c> - so a two-column payload that
    /// marks one column still writes exactly one column, and no unmodified value can clobber a concurrent
    /// writer's. Presence, not value, decides WHICH RULE APPLIES: an explicit
    /// <c>ITEM_STATUS_NOT_MODIFIED</c> is a statement and is honoured as one, and an explicit
    /// <c>ITEM_STATUS_DATA_MODIFIED</c> is honoured even on a column whose value did not move - which is
    /// how the legacy's self-assignment workaround at
    /// <c>n_cst_thread_task_sqlupdate.sru:L155-L167</c> remains expressible across this boundary.
    /// </para>
    /// <para>
    /// AND IT CANNOT REACH THE RETRIEVE PATH. <see cref="TryProjectBufferSegment"/> assigns
    /// <c>ItemStatus</c> on EVERY projected column, and assigning a proto3 <c>optional</c> field sets its
    /// presence bit whatever the value - so a payload this service produced always has explicit presence
    /// on every column and takes the honour-them-exactly branch. The inference is reachable only from a
    /// caller-composed payload, which is the update path.
    /// </para>
    /// </remarks>
    private static bool TryReadRow(
        DataWindowRow row,
        CarrierBaselineTrust baselineTrust,
        out List<AdmittedColumn>? columns)
    {
        columns = null;

        // AN INSERT-SHAPED ROW HAS NO PRIOR STATE, so it states no originals at all. The row-status
        // inference below may therefore adopt a modified status for a column with no baseline ONLY here -
        // resolving a new row's columns to NotModified! would not be the state the in-process runtime
        // holds for one, and would leave its INSERT with nothing marked.
        bool insertShaped = row.ItemStatus is ItemStatus.New or ItemStatus.NewModified;

        // 🔴 THE ROWS WHOSE BASELINE ACTUALLY BECOMES A PREDICATE, which is what the update path's
        // requirement is scoped to. Requiring a baseline from any row that merely is not insert-shaped
        // would be strictly broader than the hazard and would refuse conforming payloads: a NotModified!
        // row in a modifiable buffer generates NO STATEMENT AT ALL, so nothing ever reads its originals
        // [Tasks/SqlUpdateCarrier.ApplyUpdate, the final comment of the row walk]. The two arms that DO
        // read them are:
        //
        //   * EVERY Delete! buffer row, whatever its status - membership in that buffer IS the pending
        //     delete, so the walk generates a DELETE for each one and builds its where clause from the
        //     originals [ApplyUpdate, the delete walk; ItemStatusMachine.IsDeleteCountable];
        //   * every DataModified! row in a modifiable buffer, which becomes either an UPDATE or, when the
        //     key moved under updatekeyinplace=no, a DELETE plus an INSERT - and both statements of that
        //     pair are aimed by the originals.
        //
        // New!/NewModified! in a modifiable buffer generate an INSERT, which has no where clause at all.
        bool baselineBecomesAPredicate =
            row.Buffer == DwBuffer.Delete || row.ItemStatus == ItemStatus.DataModified;

        // THE ROW'S OWN STATUS GOVERNS ONLY WHEN THE PAYLOAD SAID NOTHING PER COLUMN - see the remarks.
        // Computed before the walk because it is a property of the WHOLE row: one explicitly stamped
        // column makes the producer explicit about every column, including the ones it left alone.
        bool anyColumnStatedItsStatus = false;

        foreach (ColumnValue candidate in row.Columns)
        {
            if (candidate.HasItemStatus)
            {
                anyColumnStatedItsStatus = true;

                break;
            }
        }

        // A row status this codec does not recognise is refused by TryValidateSegments before this method
        // runs, so the value adopted below is always a defined member.
        bool adoptRowStatus =
            !anyColumnStatedItsStatus && ItemStatusMachine.IsModified(row.ItemStatus);

        Dictionary<int, object?> originals = [];

        foreach (ColumnValue original in row.OriginalValues)
        {
            if (!IsColumnNumber(original.ColumnId)
                || !CarrierValue.TryFromWire(original.Value, out object? originalValue)
                || !originals.TryAdd((int)original.ColumnId, originalValue))
            {
                return false;
            }
        }

        List<AdmittedColumn> read = [];
        HashSet<int> seen = [];

        foreach (ColumnValue column in row.Columns)
        {
            if (!IsColumnNumber(column.ColumnId)
                || !seen.Add((int)column.ColumnId)
                || !CarrierValue.TryFromWire(column.Value, out object? currentValue))
            {
                return false;
            }

            int columnNumber = (int)column.ColumnId;

            // WHETHER AN ORIGINAL WAS STATED IS KEPT SEPARATELY FROM ITS VALUE, because both rules below
            // turn on presence rather than on the value: the update path REQUIRES presence, and the
            // row-status inference distinguishes "stated, and equal" from "never stated".
            bool originalWasSupplied = originals.TryGetValue(columnNumber, out object? supplied);

            // 🔴 THE UPDATE PATH REFUSES A CHANGED ROW THAT LEFT A BASELINE UNSTATED, rather than reading
            // the caller's own current value as the baseline. Everything the generated statement's WHERE
            // clause compares comes out of these originals, so a fabricated one is a predicate aimed at
            // the values being written: it matches the row itself and cannot detect a lost race, and for
            // a changed KEY it addresses the row named by the NEW key. Refusing is the fail-closed
            // answer AAP 0.1.5 requires - narrow with a defined error, never widen with a guess - and it
            // asks a caller for nothing the contract did not already require it to send (AAP 0.6.3.2).
            // Scoped to the rows whose baseline is actually read - see the note above this walk - so a
            // row that generates no statement, or one that generates only an INSERT, is not asked for a
            // baseline it has no use for.
            if (baselineTrust == CarrierBaselineTrust.RequiredOnChangedRows
                && baselineBecomesAPredicate
                && !originalWasSupplied)
            {
                return false;
            }

            // THE STATED ORIGINAL, OR THE CURRENT VALUE WHERE NONE WAS STATED - which is EXACT rather
            // than a substitution, because in every case that reaches this line the two are the same
            // value. Under AsStated the producer omits an original precisely when it equals the current
            // value; under RequiredOnChangedRows the only rows that reach here with an unstated original
            // are insert-shaped, for which no baseline exists and none is ever read.
            object? originalValue = originalWasSupplied ? supplied : currentValue;

            // An omitted per-column status is the contract's "no status was supplied". When the row
            // itself said nothing modified either, that means the column is not modified in its own
            // right; when the ROW is stamped modified and NOT ONE column stated a status, the row's own
            // statement governs every column it supplied EXCEPT the ones whose stated original proves
            // the value did not move - the legacy row-status convention, see the remarks on this method
            // for why, for why the value test is part of it, and for why the retrieve path cannot reach
            // it.
            //
            // 🔴 AND A COLUMN WITH NO STATED ORIGINAL READS UNCHANGED UNLESS THE ROW IS INSERT-SHAPED.
            // It used to adopt the row's modified status on the reasoning that a column with no baseline
            // cannot be measured - true, but the conclusion was backwards for an UPDATE: the column was
            // then written into the SET list while the predicate compared it against the value being
            // written, so a caller that changed a value and omitted its original had its statement aimed
            // at the wrong row. Absence of an original means UNCHANGED, which is the contract's own
            // encoding of it. The insert-shaped arm keeps the one case where adopting is right: a new
            // row's columns carry no originals because there is no prior state, and reading them as
            // NotModified! would not be the state the in-process runtime holds for a new row.
            ItemStatus columnStatus = column.HasItemStatus
                ? column.ItemStatus
                : adoptRowStatus
                    && (insertShaped
                        || (originalWasSupplied
                            && !CarrierValue.AreEquivalent(currentValue, originalValue)))
                    ? row.ItemStatus
                    : ItemStatus.NotModified;

            if (!Enum.IsDefined(columnStatus))
            {
                return false;
            }

            read.Add(new AdmittedColumn(columnNumber, columnStatus, currentValue, originalValue));
        }

        // Every original must have named a column the row actually carries.
        foreach (int columnNumber in originals.Keys)
        {
            if (!seen.Contains(columnNumber))
            {
                return false;
            }
        }

        columns = read;

        return true;
    }

    /// <summary>
    /// Whether a wire column identifier names a real column.
    /// </summary>
    /// <remarks>
    /// R9: ONE-BASED. Zero is the row-status sentinel and never addresses a value, and anything past
    /// <see cref="int.MaxValue"/> cannot be a column ordinal on a carrier whose columns are ints.
    /// </remarks>
    private static bool IsColumnNumber(long columnId) =>
        columnId >= ItemStatusMachine.FirstColumnNumber && columnId <= int.MaxValue;

    /// <summary>
    /// Admits one already-validated row into the target carrier.
    /// </summary>
    /// <param name="target">The carrier to write.</param>
    /// <param name="dwBuffer">The buffer to admit into.</param>
    /// <param name="row">The inbound row, for its own status.</param>
    /// <param name="columns">The columns read from it.</param>
    /// <remarks>
    /// <para>
    /// THE THREE-STEP RESTORE, AND WHY IT IS NOT ONE ASSIGNMENT.
    /// <see cref="CarrierRow.SetValue"/> deliberately captures a column's ORIGINAL exactly once per
    /// baseline period and deliberately does not touch any item status - see the remarks on
    /// <see cref="CarrierRow"/> for why, and note that the same explicitness is what makes the legacy's
    /// self-assignment workaround at <c>n_cst_thread_task_sqlupdate.sru:L155-L167</c> expressible at
    /// all. So restoring a column whose original and current differ takes three steps: write the
    /// ORIGINAL, re-baseline the row so that value becomes the original, then write the CURRENT so the
    /// baseline is captured behind it. Writing the current first would record an original of
    /// <see langword="null"/> and the concurrency check would compare against a value the database
    /// never saw.
    /// </para>
    /// <para>
    /// The row's own status and its per-column statuses are stamped LAST, because
    /// <see cref="CarrierRow.Baseline"/> clears both.
    /// </para>
    /// </remarks>
    private static void Admit(
        DataWindowBufferStore target,
        DwBuffer dwBuffer,
        DataWindowRow row,
        List<AdmittedColumn> columns)
    {
        // R9: AppendRow answers the new row's ONE-BASED number, which is the post-add count and not a
        // zero-based index. It is handed straight to RowAt, whose surface is one-based too.
        long appendedRowNumber = target.AppendRow(dwBuffer, ItemStatus.NotModified);
        CarrierRow appended = target.RowAt(appendedRowNumber, dwBuffer);

        foreach (AdmittedColumn column in columns)
        {
            appended.SetValue(column.ColumnNumber, column.Original);
        }

        appended.Baseline();

        foreach (AdmittedColumn column in columns)
        {
            appended.SetValue(column.ColumnNumber, column.Current);
            appended.SetColumnStatus(column.ColumnNumber, column.Status);
        }

        appended.Status = row.ItemStatus;
    }

    /// <summary>
    /// Whether a row belongs in the payload.
    /// </summary>
    /// <param name="dwBuffer">The buffer the row is in.</param>
    /// <param name="status">The row's own item status.</param>
    /// <returns><see langword="true"/> when the row is a change this payload must carry.</returns>
    /// <remarks>
    /// <para>
    /// See the type-level remarks for the reasoning: the measured modified-row predicate from
    /// <c>Buffers/ItemStatus.cs</c> plus <see cref="ItemStatus.New"/> for the two live buffers, and
    /// every row of <c>Delete!</c> unconditionally because presence in that buffer IS the change.
    /// </para>
    /// <para>
    /// C-K - TWO CONSEQUENCES OF INCLUDING <c>Delete!</c> ARE RECORDED HERE RATHER THAN ENGINEERED
    /// AROUND, BECAUSE BOTH ARE THE LEGACY'S OWN SHAPE AND BOTH ARE UNREACHABLE ON THIS PATH. First, the
    /// in-place branch discards only <c>Primary!</c> and <c>Filter!</c> between chunks
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L210-L215</c>] and never
    /// <c>Delete!</c>, so a populated delete buffer would be re-extracted on every chunk. Second, the
    /// temporary-carrier branch moves rows only between the primary and filter buffers [<c>:L160</c>,
    /// <c>:L166</c>], so a populated delete buffer would not be transferred AT ALL on that branch. The
    /// two branches would therefore disagree - and neither is corrected, for the same reason: THIS CODEC
    /// SERVES THE RETRIEVE PATH, whose event is <c>ondatareceived</c>, and a freshly retrieved carrier's
    /// delete buffer is empty. Rows enter it only through a deletion, which belongs to the update path.
    /// The membership rule stays lossless so that a payload can still express a deleted row, and the
    /// asymmetry is documented so a later reader does not mistake it for an oversight.
    /// </para>
    /// </remarks>
    private static bool IsPayloadRow(DwBuffer dwBuffer, ItemStatus status)
    {
        if (dwBuffer == DwBuffer.Delete)
        {
            return true;
        }

        return ItemStatusMachine.IsModified(status) || status == ItemStatus.New;
    }

    /// <summary>
    /// Reads a buffer's row count without duplicating the carrier's own accessors.
    /// </summary>
    /// <param name="source">The carrier to read.</param>
    /// <param name="dwBuffer">The buffer to count.</param>
    /// <returns>The row count.</returns>
    /// <exception cref="NotSupportedException">
    /// <paramref name="dwBuffer"/> is not one of the three declared buffers.
    /// </exception>
    private static long RowCountOf(DataWindowBufferStore source, DwBuffer dwBuffer)
    {
        return dwBuffer switch
        {
            DwBuffer.Primary => source.RowCount(),
            DwBuffer.Delete => source.DeletedCount(),
            DwBuffer.Filter => source.FilteredCount(),
            _ => throw new NotSupportedException(
                $"The buffer {dwBuffer} is not one of the three PowerBuilder buffers this payload "
                    + "format carries. The legacy declares exactly Primary!, Delete! and Filter!."),
        };
    }
}

#endregion

#region The transfer request and the three outcome shapes

/// <summary>
/// Everything the changeset send path needs: the carrier, its definition, the chunk size, whether the
/// receiving side needs a freshly created object, and the inter-chunk yield.
/// </summary>
/// <remarks>
/// The five members correspond to the five inputs the legacy reads from its own state at
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru</c>: the <c>data</c> argument
/// of <c>ondatareceived</c> [<c>:L21</c>], its describe properties [<c>:L150</c>, <c>:L153</c>,
/// <c>:L156</c>], the private <c>_nChunkSize</c> [<c>:L34</c>], <c>tasking._of_NeedCreate()</c>
/// [<c>:L103</c>] and the <c>of_Wait(0.02)</c> interval [<c>:L189</c>, <c>:L225</c>].
/// </remarks>
internal sealed record ChangesetTransferRequest
{
    /// <summary>
    /// The carrier whose changes are being transferred. Legacy <c>ref n_cst_thread_task_sqlbase_ds
    /// data</c>.
    /// </summary>
    /// <remarks>
    /// MUTATED BY THE TRANSFER, on both branches and deliberately. The in-place branch stamps its rows
    /// and discards or resets them [<c>:L196-L218</c>]; the temporary-carrier branch MOVES its rows out
    /// [<c>:L160</c>, <c>:L166</c>], so the source is drained either way. The legacy does the same, and
    /// a caller that needs the data afterwards must not be on this path at all - see
    /// <see cref="DataWindowCarrierOwnership"/> for the path that keeps it.
    /// </remarks>
    internal required DataWindowBufferStore Source { get; init; }

    /// <summary>
    /// The source's definition properties, standing in for its <c>Describe</c> surface.
    /// </summary>
    internal required ChangesetSourceDefinition Definition { get; init; }

    /// <summary>
    /// The maximum number of rows one chunk carries. Legacy <c>_nChunkSize</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Defaults to <see cref="ChangesetCodec.LegacyDefaultChunkSize"/>, the legacy's own initializer at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L34</c>.
    /// </para>
    /// <para>
    /// THE 1000 FLOOR IS NOT ENFORCED HERE, DELIBERATELY. <c>of_setchunksize</c> answers
    /// <c>E_INVALID_ARGUMENT</c> for any value at or below 1000 [<c>:L410</c>], and that guard belongs
    /// to the query task which owns the setter - contract C-05's <c>SetChunkSize</c>. Reproducing it
    /// here would put the same rule in two places, and the codec must not ASSUME a value above 1000
    /// either: every chunk-arithmetic path below is correct for any positive size, which is also what
    /// lets a test drive a multi-chunk sequence over a handful of rows.
    /// </para>
    /// </remarks>
    internal long ChunkSize { get; init; } = ChangesetCodec.LegacyDefaultChunkSize;

    /// <summary>
    /// Whether the receiving side needs to build itself from the source's syntax before any chunk
    /// arrives. Legacy <c>tasking._of_NeedCreate()</c>.
    /// </summary>
    /// <remarks>
    /// When set, the transfer raises the create-data handover and then FOLDS the filter buffer into the
    /// tail of the primary buffer [<c>:L103-L110</c>], which changes the counts the chunk arithmetic
    /// then sees. It is also one of the two conditions that suppress the sort and filter clearing
    /// [<c>:L559</c>].
    /// </remarks>
    internal bool ReceiverNeedsCreatedObject { get; init; }

    /// <summary>
    /// How long to yield between chunks. Legacy <c>of_Wait(0.02)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Defaults to <see cref="ChangesetCodec.LegacyInterChunkYield"/>, which is the legacy's own 0.02
    /// seconds. It is a request member rather than a constant so that a test can drive a many-chunk
    /// sequence without waiting, WITHOUT changing the production default - the observable legacy value
    /// stays the default, and only a caller that says otherwise gets something else.
    /// </para>
    /// <para>
    /// A non-positive value still yields control once, so cancellation is still observed between
    /// chunks. See <c>ChangesetCodec</c>'s yield helper.
    /// </para>
    /// </remarks>
    internal TimeSpan InterChunkYield { get; init; } = ChangesetCodec.LegacyInterChunkYield;
}

/// <summary>
/// What a changeset send did: its framework return code, the chunk arithmetic it computed, how many
/// chunks it actually handed over, which branch it took, and whether it folded the filter buffer.
/// </summary>
/// <param name="ReturnCode">
/// The framework return code, in the zero-is-success algebra: <c>RetCode.OK</c>,
/// <c>RetCode.CANCELLED</c> or <c>RetCode.E_INTERNAL_ERROR</c>. These are the only three the legacy
/// returns from this event [<c>:L99</c>, <c>:L105</c>, <c>:L131</c>, <c>:L136</c>, <c>:L139</c>,
/// <c>:L142</c>, <c>:L173</c>, <c>:L185</c>, <c>:L189</c>, <c>:L207</c>, <c>:L221</c>, <c>:L225</c>,
/// <c>:L234</c>].
/// </param>
/// <param name="ChunkCount">
/// The chunk count the arithmetic produced. One for the empty-result arm, which hands over a single
/// empty chunk [<c>:L230</c>].
/// </param>
/// <param name="ChunksSent">
/// How many chunks were handed over before the sequence ended, which is lower than
/// <paramref name="ChunkCount"/> when the transfer was cancelled or failed mid-sequence.
/// </param>
/// <param name="UsedTemporaryCarrier">
/// Whether the sorted multi-chunk workaround of DEFECT 1 ran. This is the single most important thing
/// a parity test can assert about a send, because the fixture is sorted and the workaround is
/// therefore a main path.
/// </param>
/// <param name="TemporaryCarrierShape">
/// Where the temporary carrier got its shape, or <see langword="null"/> when no temporary carrier was
/// created.
/// </param>
/// <param name="FoldedFilterBuffer">
/// Whether the filter buffer was folded into the tail of the primary buffer before the chunk
/// arithmetic ran [<c>:L109</c>].
/// </param>
/// <param name="PayloadCleared">
/// Whether the codec was holding no payload when it returned, which reproduces
/// <c>blbData = Blob("")</c> [<c>:L138</c>, <c>:L187</c>, <c>:L223</c>]. False only when the transfer
/// ended at a failed handover, where the legacy also returns while the blob is still populated.
/// </param>
/// <param name="ErrorText">
/// The verbatim error text reported, or <see langword="null"/> when none was. One of
/// <c>GetChanges Failed</c> or <c>TransData Failed</c>.
/// </param>
internal sealed record ChangesetTransferOutcome(
    long ReturnCode,
    long ChunkCount,
    long ChunksSent,
    bool UsedTemporaryCarrier,
    TemporaryCarrierProvenance? TemporaryCarrierShape,
    bool FoldedFilterBuffer,
    bool PayloadCleared,
    string? ErrorText);

/// <summary>
/// What a drop-down child transfer did.
/// </summary>
/// <param name="ReturnCode">
/// The framework return code: <c>RetCode.OK</c>, <c>RetCode.CANCELLED</c> or
/// <c>RetCode.E_INTERNAL_ERROR</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L131</c>, <c>:L136</c>,
/// <c>:L139</c>, <c>:L142</c>].
/// </param>
/// <param name="ChildrenSent">How many children were handed over before the walk ended.</param>
/// <param name="PayloadCleared">
/// Whether the codec was holding no payload when it returned, reproducing <c>blbData = Blob("")</c>
/// [<c>:L138</c>].
/// </param>
/// <param name="ErrorText">
/// The verbatim error text reported, or <see langword="null"/> when none was. Either
/// <c>[column] GetChanges Failed</c> or <c>[column] TransData Failed</c>.
/// </param>
internal sealed record ChangesetChildTransferOutcome(
    long ReturnCode,
    int ChildrenSent,
    bool PayloadCleared,
    string? ErrorText);

/// <summary>
/// What applying one main-result chunk did.
/// </summary>
/// <param name="Result">
/// The result in PowerBuilder's convention, AFTER the strict coercion: one for success, minus one for
/// failure, and zero when the apply was skipped because the task was already cancelled
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L182</c>].
/// </param>
/// <param name="TargetCleared">
/// Whether the target was reset. True both on the empty-payload arm [<c>:L207-L210</c>] and on the
/// first chunk of a sequence [<c>:L192-L196</c>].
/// </param>
/// <param name="UpdateFlagsReset">
/// Whether the update flags were cleared, which the legacy does once the LAST chunk has been applied
/// [<c>:L198-L199</c>, <c>:L222-L223</c>, <c>:L239-L240</c>].
/// </param>
/// <param name="PartialApplyCoerced">
/// Whether an apply result greater than one was rewritten to failure. THE CHANGESET PATH IS STRICTER
/// THAN THE FULL-STATE PATH about exactly this: <c>if rtCode &gt; 1 then rtCode = -1</c> on the
/// changeset arm [<c>:L250</c>] against <c>if rtCode &gt; 1 then rtCode = 1</c> on the full-state arm
/// [<c>:L248</c>]. A partial apply is a FAILURE here and a SUCCESS there.
/// </param>
/// <param name="Notified">
/// Whether the chunk notification would be published, which the legacy does only when the result is
/// exactly one [<c>:L253-L255</c>].
/// </param>
internal sealed record ChangesetApplyOutcome(
    long Result,
    bool TargetCleared,
    bool UpdateFlagsReset,
    bool PartialApplyCoerced,
    bool Notified);

/// <summary>
/// What applying one drop-down child payload did.
/// </summary>
/// <param name="Result">
/// The result in PowerBuilder's convention after coercion: one, minus one, or zero when the apply was
/// skipped because the task was already cancelled
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L96</c>].
/// </param>
/// <param name="TargetCleared">
/// Always true on a completed apply: the child receiver resets its target UNCONDITIONALLY before
/// looking at the payload [<c>:L103</c>, <c>:L116</c>, <c>:L128</c>], unlike the main receiver which
/// resets only on the first chunk.
/// </param>
/// <param name="UpdateFlagsReset">
/// Whether the child's update flags were cleared [<c>:L144</c>].
/// </param>
/// <param name="PartialApplyCoerced">
/// Whether an apply result greater than one was rewritten to failure [<c>:L141</c>]. The child path
/// uses the same STRICT coercion as the main changeset path.
/// </param>
/// <param name="RequiresFilterReapplication">
/// Whether the child's filter must be re-applied [<c>:L145-L147</c>]. See
/// <see cref="ChangesetSourceDefinition.FilterIsSubstantial"/> for the threshold, and the remarks on
/// <see cref="ChangesetCodec.ApplyChildPayload"/> for why the DECISION is reproduced here while the
/// execution is not.
/// </param>
/// <param name="RequiresSortReapplication">
/// Whether the child's sort must be re-applied [<c>:L148-L150</c>].
/// </param>
/// <param name="Notified">
/// Whether the child notification would be published, which the legacy does only when the result is
/// exactly one [<c>:L151</c>].
/// </param>
internal sealed record ChangesetChildApplyOutcome(
    long Result,
    bool TargetCleared,
    bool UpdateFlagsReset,
    bool PartialApplyCoerced,
    bool RequiresFilterReapplication,
    bool RequiresSortReapplication,
    bool Notified);

#endregion

#region The codec

/// <summary>
/// The changeset half of the cross-thread transfer: the <c>case else</c> arm of the codec selector,
/// carrying the chunk arithmetic, the sorted multi-chunk workaround of DEFECT 1, the point-specific
/// reset prohibition of DEFECT 2, the never-reset filtered count, the filter fold, the sort and filter
/// clearing, the drop-down child loop and the receive-side apply.
/// </summary>
/// <remarks>
/// <para>
/// Ported from <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L102-L231</c> (send)
/// and <c>n_cst_threading_task_sqlquery.sru:L93-L155, L180-L258</c> (receive). See the file banner for
/// the seven decisions this type embodies and for the individual R9 audit.
/// </para>
/// <para>
/// C-H. Every member is a pure transform over a carrier, a definition and a chunk size, so the whole
/// type is exercisable with no database, no container and no thread. The one time-dependent operation
/// is the inter-chunk yield and it goes through the injected <see cref="TimeProvider"/>.
/// </para>
/// </remarks>
internal sealed class ChangesetCodec
{
    /// <summary>
    /// The legacy's own default chunk size.
    /// </summary>
    /// <remarks>
    /// <c>long _nChunkSize = 10000 //Chunk row count,min:1000</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L34</c>]. The inline
    /// comment names the floor; the setter enforces it at <c>:L410</c>, and that enforcement lives in
    /// the query task rather than here - see
    /// <see cref="ChangesetTransferRequest.ChunkSize"/>.
    /// </remarks>
    internal const long LegacyDefaultChunkSize = 10000L;

    /// <summary>
    /// The verbatim error text a failed changeset extraction reports.
    /// </summary>
    /// <remarks>
    /// <c>Event OnError(RetCode.E_INTERNAL_ERROR,"GetChanges Failed")</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L172</c>, <c>:L206</c>].
    /// PRESERVED CHARACTER FOR CHARACTER, including the capitalisation and the single space, because it
    /// appears in log records and in characterization recordings where a reworded message would
    /// invalidate every stored comparison.
    /// </remarks>
    internal const string GetChangesFailedText = "GetChanges Failed";

    /// <summary>
    /// The verbatim error text a failed handover reports.
    /// </summary>
    /// <remarks>
    /// <c>Event OnError(RetCode.E_INTERNAL_ERROR,"TransData Failed")</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L184</c>, <c>:L220</c>].
    /// Preserved character for character, for the same reason as
    /// <see cref="GetChangesFailedText"/>.
    /// </remarks>
    internal const string TransDataFailedText = "TransData Failed";

    /// <summary>
    /// The describe result that marks a drop-down child column as auto-retrieving.
    /// </summary>
    /// <remarks>
    /// <c>if sProp &lt;&gt; "yes" then continue</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L116</c>]. The test is for
    /// EXACT EQUALITY with the lower-case word; anything else, including an empty result or a describe
    /// error marker, skips the column.
    /// </remarks>
    internal const string AutoRetrieveEnabled = "yes";

    /// <summary>
    /// The legacy's own inter-chunk yield, 0.02 seconds.
    /// </summary>
    /// <remarks>
    /// <c>if Not of_Wait(0.02) then return RetCode.CANCELLED</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L189</c>, <c>:L225</c>].
    /// Expressed in seconds rather than milliseconds so the value is diffable against the locator.
    /// </remarks>
    internal static readonly TimeSpan LegacyInterChunkYield = TimeSpan.FromSeconds(0.02);

    private readonly TimeProvider _timeProvider;
    private readonly IChangesetPayloadCodec _payloadCodec;

    /// <summary>
    /// Creates a codec.
    /// </summary>
    /// <param name="timeProvider">
    /// The service's single clock seam, used ONLY to schedule the inter-chunk yield.
    /// </param>
    /// <param name="payloadCodec">
    /// The payload format, or <see langword="null"/> for <see cref="ChangesetPayloadCodec"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="timeProvider"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// C-H. THE CLOCK IS INJECTED AND NEVER READ AMBIENTLY. The Golden-Master technique's one hard
    /// prerequisite is repeatability with non-deterministic values masked from BOTH master and
    /// candidate, and the inter-chunk yield is the only thing here that could vary run to run.
    /// <see cref="DateTime"/>, <see cref="DateTimeOffset"/>, <c>Stopwatch</c> and
    /// <c>Environment.TickCount</c> appear nowhere in this file.
    /// </para>
    /// <para>
    /// The payload format is optional so that production wiring stays a single constructor argument
    /// while a test can substitute an encoder that fails or that partially applies - the only way the
    /// legacy's <c>GetChanges Failed</c> arm and its <c>rtCode &gt; 1</c> coercion are reachable at all.
    /// </para>
    /// </remarks>
    internal ChangesetCodec(TimeProvider timeProvider, IChangesetPayloadCodec? payloadCodec = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        _timeProvider = timeProvider;
        _payloadCodec = payloadCodec ?? new ChangesetPayloadCodec();
    }

    #region Pure decisions - each testable with no carrier, no thread and no database

    /// <summary>
    /// Whether a carrier belongs to THIS codec rather than to the full-state codec.
    /// </summary>
    /// <param name="carrier">The carrier to classify.</param>
    /// <returns>
    /// <see langword="true"/> for every processing kind except crosstab and composite, which is the
    /// legacy's <c>case else</c> arm.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="carrier"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Reproduces the selector at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L93-L94</c> by ASKING THE
    /// CARRIER'S OWN TYPED PROPERTY. It does not re-parse a describe string:
    /// <c>Buffers/DataWindowBuffers.cs</c> already owns that parse through
    /// <see cref="DataWindowProcessing.FromDescribe"/>, and two independent parses of the same property
    /// could disagree about which codec owns a carrier - which is the one disagreement that would send
    /// a payload to the wrong decoder.
    /// </para>
    /// <para>
    /// The fixture declares <c>processing=1</c>
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L3</c>], so it answers <see langword="true"/>.
    /// </para>
    /// </remarks>
    internal static bool HandlesCarrier(DataWindowBufferStore carrier)
    {
        ArgumentNullException.ThrowIfNull(carrier);

        return !carrier.RequiresFullStateTransfer;
    }

    /// <summary>
    /// Computes how many chunks a carrier's contents divide into.
    /// </summary>
    /// <param name="rowCount">The <c>Primary!</c> row count.</param>
    /// <param name="filteredCount">The <c>Filter!</c> row count.</param>
    /// <param name="chunkSize">The maximum rows per chunk. Must be positive.</param>
    /// <returns>The chunk count, which is zero only when both counts are zero.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="rowCount"/> or <paramref name="filteredCount"/> is negative, or
    /// <paramref name="chunkSize"/> is not positive.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Reproduces
    /// <c>nChunkCnt = Ceiling((Data.RowCount() + Data.FilteredCount()) / _nChunkSize)</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L145</c>].
    /// </para>
    /// <para>
    /// THE SUM SPANS BOTH BUFFERS, WHICH IS THE WHOLE POINT AND IS EASY TO GET WRONG. A heavily
    /// filtered result therefore produces MORE chunks than its primary row count alone would suggest,
    /// and the published <c>persistence.v1.QueryDataChunk</c> message records the same formula as
    /// contract for exactly that reason. Counting the primary buffer alone would under-chunk and drop
    /// the tail of the filter buffer entirely.
    /// </para>
    /// <para>
    /// R9 item 1 - COUNTS, NOT INDICES, so no rebasing applies. PowerBuilder's division of two integers
    /// yields a real number and <c>Ceiling</c> rounds it up; the managed form uses exact integer
    /// arithmetic instead of a floating-point divide, because a double divide can round a large exact
    /// multiple to just above or just below the integer and change the chunk count by one. Both operands
    /// are non-negative, so the standard non-negative ceiling identity holds.
    /// </para>
    /// </remarks>
    internal static long ComputeChunkCount(long rowCount, long filteredCount, long chunkSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rowCount);
        ArgumentOutOfRangeException.ThrowIfNegative(filteredCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkSize);

        long total = rowCount + filteredCount;

        if (total <= 0L)
        {
            // `if nChunkCnt > 0 then ... else` [:L146, :L229] - the empty-result arm, which still hands
            // over a single empty chunk.
            return 0L;
        }

        return ((total - 1L) / chunkSize) + 1L;
    }

    /// <summary>
    /// Whether the sorted multi-chunk workaround of DEFECT 1 applies.
    /// </summary>
    /// <param name="chunkCount">The chunk count from <see cref="ComputeChunkCount"/>.</param>
    /// <param name="definition">The source's definition, read for its sort expression.</param>
    /// <returns>
    /// <see langword="true"/> when the carrier spans more than one chunk AND carries a real sort
    /// expression.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// DEFECT 1, PRESERVED (C-B). Reproduces
    /// <c>if nChunkCnt &gt; 1 and sProp &lt;&gt; "?" and sProp &lt;&gt; "" then</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L151</c>], guarding the
    /// workaround the legacy documented against itself one line earlier at <c>:L148</c>:
    /// <c>带排序的DW大于1块时使用SetChanges可能会丢失行</c> - "when a sorted DataWindow spans more than
    /// one block, using SetChanges may lose rows" - and <c>:L149</c>
    /// <c>采用临时的DS来绕开此问题</c> - "a temporary DataStore is used to work around it".
    /// </para>
    /// <para>
    /// ALL THREE CONDITIONS ARE REQUIRED AND THE TWO SORT TESTS ARE NOT ONE TEST. A single question
    /// mark is PowerBuilder's not-applicable describe result and an empty string is a real answer
    /// meaning "no sort"; the legacy compares against both separately and this port keeps that
    /// distinction in <see cref="ChangesetSourceDefinition.SortIsAbsent"/> rather than collapsing them
    /// into a null-or-empty test that would lose the "?" case's identity.
    /// </para>
    /// <para>
    /// THIS IS A MAIN PATH, NOT AN EDGE CASE. The golden-master fixture is
    /// <c>processing=1</c> - so it takes this codec's arm - AND
    /// <c>sort="age A salary A "</c> - so it is sorted
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L3, L14</c>]. Whenever its row count plus
    /// filtered count exceeds the chunk size, the workaround runs.
    /// </para>
    /// </remarks>
    internal static bool RequiresTemporaryCarrier(
        long chunkCount,
        ChangesetSourceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return chunkCount > 1L && !definition.SortIsAbsent;
    }

    /// <summary>
    /// Whether the changeset path clears the sort and filter conditions rather than synchronizing them.
    /// </summary>
    /// <param name="definition">The source's definition, read for whether a data object is assigned.</param>
    /// <param name="isMainThread">
    /// The answer <c>#ParentThread.of_IsMainThread()</c> would give
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L559</c>].
    /// </param>
    /// <param name="receiverNeedsCreatedObject">
    /// The answer <c>tasking._of_NeedCreate()</c> would give [<c>:L559</c>].
    /// </param>
    /// <param name="requiresFullStateTransfer">
    /// The carrier's own codec discriminator, <see cref="DataWindowBufferStore.RequiresFullStateTransfer"/>
    /// [<c>:L560</c>].
    /// </param>
    /// <returns><see langword="true"/> when both conditions must be cleared.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// DECISION 4. Reproduces the three nested gates that guard
    /// <c>data.SetSort("") : data.SetFilter("")</c> at <c>:L579-L580</c>: a data object must be
    /// assigned [<c>:L550</c>], execution must be OFF the main thread and the receiving side must NOT
    /// need a freshly created object [<c>:L559</c>], and the carrier must not be on the full-state arm
    /// [<c>:L560</c>].
    /// </para>
    /// <para>
    /// THE MIRROR IMAGE IS ALSO CONTRACT. The legacy's note at <c>:L577-L578</c> says the SetChanges
    /// transfer style does not need sort and filter conditions and can even be hurt by them; the arm
    /// immediately above SYNCHRONIZES them for full state instead [<c>:L562-L575</c>], and the
    /// else-branch synchronizes them when the gate fails [<c>:L582-L597</c>]. Clearing them
    /// unconditionally would break the other two paths, which is why this is a predicate rather than an
    /// unconditional step inside the transfer.
    /// </para>
    /// </remarks>
    internal static bool ShouldClearSortAndFilter(
        ChangesetSourceDefinition definition,
        bool isMainThread,
        bool receiverNeedsCreatedObject,
        bool requiresFullStateTransfer)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return definition.HasDataObject
            && !isMainThread
            && !receiverNeedsCreatedObject
            && !requiresFullStateTransfer;
    }

    /// <summary>
    /// Produces the definition the transferred carrier carries once its sort and filter have been
    /// cleared.
    /// </summary>
    /// <param name="definition">The definition to clear.</param>
    /// <returns>A copy with both expressions empty and everything else unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Reproduces <c>data.SetSort("")</c> and <c>data.SetFilter("")</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L579-L580</c>]. BOTH ARE
    /// SET TO EMPTY, not to the not-applicable marker: an empty expression is a real answer meaning
    /// "no condition", which is exactly what the legacy installs.
    /// </para>
    /// <para>
    /// C-B - THE RESULTS ARE NOT CHECKED, AND THAT IS THE LEGACY BEHAVIOUR. Every other
    /// <c>SetSort</c> and <c>SetFilter</c> call in the same function tests
    /// <c>&lt;&gt; 1</c> and reports <c>E_INVALID_ARGUMENT</c> on failure [<c>:L565-L568</c>,
    /// <c>:L571-L574</c>, <c>:L585-L588</c>, <c>:L591-L594</c>]; these two, alone, are unchecked.
    /// Adding a check would be an improvement, and C-B forbids improvements.
    /// </para>
    /// </remarks>
    internal static ChangesetSourceDefinition ClearSortAndFilter(
        ChangesetSourceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return definition with
        {
            SortExpression = string.Empty,
            FilterExpression = string.Empty,
        };
    }

    /// <summary>
    /// Whether a column's drop-down child must be transferred alongside the main result.
    /// </summary>
    /// <param name="autoRetrieve">
    /// The result of <c>Describe("#n.DDDW.AutoRetrieve")</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L115</c>].
    /// </param>
    /// <param name="dropDownName">
    /// The result of <c>Describe("#n.DDDW.Name")</c> [<c>:L117</c>].
    /// </param>
    /// <returns><see langword="true"/> when the column has a transferable child.</returns>
    /// <remarks>
    /// <para>
    /// Reproduces both filters of the child loop: <c>if sProp &lt;&gt; "yes" then continue</c>
    /// [<c>:L116</c>], and <c>if sProp &lt;&gt; "!" and sProp &lt;&gt; "?" then</c> [<c>:L118</c>].
    /// </para>
    /// <para>
    /// C-K - THE RULE LIVES HERE EVEN THOUGH THE WALK DOES NOT. The legacy discovers its children by
    /// iterating the column count and interrogating two describe properties per column, which is
    /// DEFINITION REFLECTION and belongs to the layer that owns the definition. The FILTER RULE,
    /// however, is behaviour: both markers are screened, the auto-retrieve test is an exact match
    /// against the lower-case word rather than a truthiness test, and an unreadable or
    /// not-applicable name skips the column instead of failing the transfer. Keeping the rule here
    /// keeps it reproduced, documented and testable in one place.
    /// </para>
    /// <para>
    /// C-B - THE TEST IS EXACTLY THE LEGACY'S TWO-MARKER TEST AND IS NOT WIDENED. An EMPTY name passes,
    /// because <c>"" &lt;&gt; "!"</c> and <c>"" &lt;&gt; "?"</c> both hold, and that is not an oversight
    /// in the legacy either: such a column is filtered one line later, where
    /// <c>Data.GetChild(sColName, ref dwcSrc) = -1</c> answers -1 and the loop continues [<c>:L120</c>].
    /// That lookup is a definition lookup and therefore belongs to the caller too, which is why this
    /// predicate deliberately stops where the legacy's own <c>if</c> stops. A NULL name likewise passes,
    /// and has NO LEGACY COUNTERPART AT ALL: PowerBuilder's <c>Describe</c> answers a string in every
    /// case - a value, <c>"!"</c>, <c>"?"</c> or <c>""</c> - so nothing in the oracle says what a null
    /// should do, and inventing a rejection here would add a failure mode the oracle does not have.
    /// </para>
    /// </remarks>
    internal static bool IsChildEligible(string? autoRetrieve, string? dropDownName)
    {
        if (!string.Equals(autoRetrieve, AutoRetrieveEnabled, StringComparison.Ordinal))
        {
            return false;
        }

        return !string.Equals(
                dropDownName,
                ChangesetSourceDefinition.DescribeUnreadable,
                StringComparison.Ordinal)
            && !string.Equals(
                dropDownName,
                ChangesetSourceDefinition.DescribeNotApplicable,
                StringComparison.Ordinal);
    }

    /// <summary>
    /// Folds the whole <c>Filter!</c> buffer onto the tail of the <c>Primary!</c> buffer of the SAME
    /// carrier.
    /// </summary>
    /// <param name="source">The carrier to fold. Both of its buffers are modified.</param>
    /// <returns>
    /// <see cref="DataWindowBufferStore.DataStoreSuccess"/>, or
    /// <see cref="DataWindowBufferStore.DataStoreFailure"/> when nothing was filtered - see the
    /// remarks.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Reproduces
    /// <c>Data.RowsMove(1, Data.FilteredCount(), Filter!, Data, Data.RowCount() + 1, Primary!)</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L109</c>], together with
    /// its NOTE at <c>:L106-L108</c>: applying a changeset to an object built from the current syntax
    /// handles filtering automatically, which avoids the cost of copying the filter buffer separately.
    /// </para>
    /// <para>
    /// A SELF-MOVE. The source and the destination are the SAME carrier, which is unusual enough to be
    /// worth stating: this is one carrier rearranging its own two buffers, not a transfer between two.
    /// <see cref="DataWindowBufferStore.RowsMove"/> extracts the range before inserting it for exactly
    /// this reason.
    /// </para>
    /// <para>
    /// R9 ITEM 4 - THE APPEND IDIOM, AND THE TRANSLATION DECISION IN FULL. The destination is
    /// <c>RowCount() + 1</c>, one past the last valid row in a one-based domain, which PowerBuilder
    /// reads as "after every existing row". IN A ZERO-BASED COLLECTION THE EQUIVALENT INSERTION POINT
    /// IS THE COUNT ITSELF, NOT THE COUNT PLUS ONE, and blindly carrying the plus one into a
    /// zero-based insert would be an off-by-one that no row-count assertion could catch, because the
    /// count would still be right. THE CONVERSION IS DELIBERATELY NOT PERFORMED HERE: the one-based
    /// value is passed through unchanged to <see cref="DataWindowBufferStore.RowsMove"/>, which owns
    /// the single one-based-to-zero-based conversion site in the buffer model and already treats an
    /// insertion point past the end as an append. One conversion, in one place, audited once.
    /// </para>
    /// <para>
    /// C-B - AN EMPTY FILTER BUFFER ANSWERS FAILURE AND MOVES NOTHING. When nothing is filtered the
    /// range is 1 through 0 - end before start - which PowerBuilder answers with -1. The legacy
    /// IGNORES the result, so the fold is simply a no-op on that carrier; the code is returned rather
    /// than swallowed so a caller that wants to know can ask.
    /// </para>
    /// <para>
    /// ORDER MATTERS. The fold runs BEFORE the chunk arithmetic [<c>:L109</c> precedes <c>:L145</c>],
    /// so on this path <see cref="ComputeChunkCount"/> sees the POST-FOLD counts: the primary count has
    /// grown by the filtered count and the filtered count is zero.
    /// </para>
    /// </remarks>
    internal static long FoldFilterBufferIntoPrimary(DataWindowBufferStore source)
    {
        ArgumentNullException.ThrowIfNull(source);

        long filteredCount = source.FilteredCount();

        // R9 item 4. `Data.RowCount() + 1` verbatim: a ONE-BASED insertion point one past the last
        // row. Not rebased here; RowsMove owns the conversion.
        long appendBeforeRow = source.RowCount() + 1L;

        return source.RowsMove(
            ItemStatusMachine.FirstRowNumber,
            filteredCount,
            DwBuffer.Filter,
            source,
            appendBeforeRow,
            DwBuffer.Primary);
    }

    /// <summary>
    /// Creates the temporary carrier the sorted multi-chunk workaround stages each chunk through.
    /// </summary>
    /// <param name="definition">The source's definition, read for its data object name and syntax.</param>
    /// <param name="processing">
    /// The source's processing kind, copied so the temporary carrier answers the codec selector
    /// identically.
    /// </param>
    /// <param name="provenance">Which of the two routes supplied the shape.</param>
    /// <returns>An empty carrier with the source's shape.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// DEFECT 1's WORKAROUND, PART ONE. Reproduces
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L152-L157</c>: create a
    /// plain datastore, then copy the source's data object name when it is non-empty, otherwise build
    /// from the source's generated syntax.
    /// </para>
    /// <para>
    /// A PLAIN <see cref="DataWindowBufferStore"/> AND NOT A CARRIER. The legacy writes
    /// <c>dsTmp = Create datastore</c>, not <c>Create n_cst_thread_task_sqlbase_ds</c>, so the
    /// temporary object has NONE of the carrier behaviour - no parent task, no progress notification,
    /// no row cap, no N-char rewrite and no thread affinity. Instantiating a
    /// <see cref="DataWindowCarrier"/> here would add all of that to a staging buffer whose only job is
    /// to hold one chunk, and its notification stream would then differ from the legacy's.
    /// </para>
    /// <para>
    /// <c>Destroy dsTmp</c> at <c>:L192</c> HAS NO COUNTERPART and needs none: the temporary carrier
    /// becomes unreachable when the transfer returns and the runtime reclaims it. It holds no unmanaged
    /// resource, no handle and no connection, so there is nothing to dispose and
    /// <see cref="IDisposable"/> would be ceremony rather than correctness.
    /// </para>
    /// </remarks>
    internal static DataWindowBufferStore CreateTemporaryCarrier(
        ChangesetSourceDefinition definition,
        DataWindowProcessing processing,
        out TemporaryCarrierProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(definition);

        // `dsTmp = Create datastore` [:L152].
        DataWindowBufferStore temporary = new()
        {
            // The processing kind travels with the shape. Without it the staging buffer could answer
            // the codec selector differently from the carrier it stages for, which is the one
            // disagreement that would route a payload to the wrong decoder.
            Processing = processing,
        };

        // `if Data.DataObject <> "" then dsTmp.DataObject = ... else dsTmp.Create(...)` [:L153-L157].
        // The shape is carried by the definition; the buffers themselves are always empty at creation,
        // exactly as a freshly created datastore's are.
        provenance = definition.HasDataObject
            ? TemporaryCarrierProvenance.DataObjectName
            : TemporaryCarrierProvenance.Syntax;

        return temporary;
    }

    #endregion

    #region The send path - n_cst_thread_task_sqlquery.sru:L102-L231

    /// <summary>
    /// Transfers a carrier's changes as a sequence of chunks: the optional create-data handover and
    /// filter fold, the chunk arithmetic, then either the sorted multi-chunk workaround or the in-place
    /// loop, with a cooperative yield and a cancellation re-check between chunks.
    /// </summary>
    /// <param name="request">The transfer inputs.</param>
    /// <param name="sink">Where the chunks and the errors go.</param>
    /// <param name="cancellationToken">
    /// The task's cancellation token, standing in for <c>of_IsCancelled()</c> and for the false answer
    /// of <c>of_Wait</c>.
    /// </param>
    /// <returns>The outcome, whose return code is the value the legacy event returns.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="request"/> or <paramref name="sink"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="ChangesetTransferRequest.ChunkSize"/> is not positive.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Reproduces the <c>case else</c> arm of
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L102-L231</c>, step for step.
    /// The drop-down child loop that sits inside that arm at <c>:L112-L141</c> is a SEPARATE entry
    /// point - see <see cref="TransferChildrenAsync"/> - because its column discovery is definition
    /// reflection rather than buffer work; a caller reproduces the legacy order by calling that method
    /// between the fold and this one.
    /// </para>
    /// <para>
    /// CANCELLATION IS NEVER SWALLOWED INTO SUCCESS. It is checked after the create-data handover
    /// [<c>:L105</c>], before the chunk sequence [<c>:L142</c>] and between every pair of chunks
    /// [<c>:L188-L190</c>, <c>:L224-L226</c>], and each check answers <c>RetCode.CANCELLED</c>. No
    /// <see cref="OperationCanceledException"/> escapes: the legacy reacts to a return code and a caller
    /// written against it would not catch one.
    /// </para>
    /// <para>
    /// THE SOURCE IS DRAINED EITHER WAY. See the remarks on
    /// <see cref="ChangesetTransferRequest.Source"/>.
    /// </para>
    /// </remarks>
    internal async Task<ChangesetTransferOutcome> TransferAsync(
        ChangesetTransferRequest request,
        IChangesetTransferSink sink,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.ChunkSize);

        DataWindowBufferStore source = request.Source;
        bool foldedFilterBuffer = false;

        // `if tasking._of_NeedCreate() then` [:L103].
        if (request.ReceiverNeedsCreatedObject)
        {
            // `tasking.Event OnCreateData(Data.Describe("DataWindow.Syntax"))` [:L104]. The legacy
            // event is declared WITHOUT a return type, so its result is not testable there and is not
            // tested here either (C-B).
            _ = await sink.CreateDataAsync(request.Definition.Syntax, cancellationToken)
                .ConfigureAwait(false);

            // `if of_IsCancelled() then return RetCode.CANCELLED` [:L105]. Note the position: BEFORE
            // the fold, so a cancellation here leaves the buffers untouched.
            if (cancellationToken.IsCancellationRequested)
            {
                return new ChangesetTransferOutcome(
                    RetCode.CANCELLED,
                    ChunkCount: 0L,
                    ChunksSent: 0L,
                    UsedTemporaryCarrier: false,
                    TemporaryCarrierShape: null,
                    FoldedFilterBuffer: false,
                    PayloadCleared: true,
                    ErrorText: null);
            }

            // The fold [:L109], whose NOTE at :L106-L108 explains why: applying a changeset to an
            // object built from the current syntax handles filtering automatically, so copying the
            // filter buffer separately would be pure cost.
            _ = FoldFilterBufferIntoPrimary(source);
            foldedFilterBuffer = true;
        }

        // `if of_IsCancelled() then return RetCode.CANCELLED` [:L142] - the guard that stands between
        // the child loop and the chunk sequence.
        if (cancellationToken.IsCancellationRequested)
        {
            return new ChangesetTransferOutcome(
                RetCode.CANCELLED,
                ChunkCount: 0L,
                ChunksSent: 0L,
                UsedTemporaryCarrier: false,
                TemporaryCarrierShape: null,
                FoldedFilterBuffer: foldedFilterBuffer,
                PayloadCleared: true,
                ErrorText: null);
        }

        // `nChunkCnt = Ceiling((Data.RowCount() + Data.FilteredCount()) / _nChunkSize)` [:L145]. On the
        // folded path these are the POST-FOLD counts, which is why the fold above precedes this line
        // exactly as it does in the legacy.
        long chunkCount = ComputeChunkCount(
            source.RowCount(),
            source.FilteredCount(),
            request.ChunkSize);

        // `if nChunkCnt > 0 then ... else` [:L146, :L229].
        if (chunkCount <= 0L)
        {
            // `tasking.Event OnDataChunk(ref blbData,1,1,false)` [:L230]. THE RESULT IS NOT TESTED
            // here, unlike at :L183 and :L219 - the legacy omits the check on this arm alone, and the
            // omission is reproduced. The counters are 1 and 1 even though the arithmetic answered
            // zero, and the payload is empty, which is what tells the receiving side to CLEAR.
            _ = await sink
                .SendChunkAsync(
                    new ChangesetChunk(state: null, 1L, 1L),
                    cancellationToken)
                .ConfigureAwait(false);

            return new ChangesetTransferOutcome(
                RetCode.OK,
                ChunkCount: 1L,
                ChunksSent: 1L,
                UsedTemporaryCarrier: false,
                TemporaryCarrierShape: null,
                FoldedFilterBuffer: foldedFilterBuffer,
                PayloadCleared: true,
                ErrorText: null);
        }

        // `sProp = Data.Describe("DataWindow.Table.Sort")` [:L150] and the three-condition guard at
        // :L151. DEFECT 1's branch point.
        bool useTemporaryCarrier = RequiresTemporaryCarrier(chunkCount, request.Definition);
        TemporaryCarrierProvenance? temporaryCarrierShape = null;
        ChunkLoopResult loop;

        if (useTemporaryCarrier)
        {
            DataWindowBufferStore temporary = CreateTemporaryCarrier(
                request.Definition,
                source.Processing,
                out TemporaryCarrierProvenance shape);

            temporaryCarrierShape = shape;

            loop = await TransferThroughTemporaryCarrierAsync(
                    request,
                    sink,
                    temporary,
                    chunkCount,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            loop = await TransferInPlaceAsync(request, sink, chunkCount, cancellationToken)
                .ConfigureAwait(false);
        }

        return new ChangesetTransferOutcome(
            loop.ReturnCode,
            chunkCount,
            loop.ChunksSent,
            useTemporaryCarrier,
            temporaryCarrierShape,
            foldedFilterBuffer,
            loop.PayloadCleared,
            loop.ErrorText);
    }

    /// <summary>
    /// The sorted multi-chunk branch: each chunk is staged through a temporary carrier so that the rows
    /// the chunk carries are the only rows that carrier ever holds.
    /// </summary>
    /// <param name="request">The transfer inputs.</param>
    /// <param name="sink">Where the chunks and the errors go.</param>
    /// <param name="temporary">The staging carrier from <see cref="CreateTemporaryCarrier"/>.</param>
    /// <param name="chunkCount">The chunk count, which is greater than one on this branch.</param>
    /// <param name="cancellationToken">The task's cancellation token.</param>
    /// <returns>The loop result.</returns>
    /// <remarks>
    /// <para>
    /// DEFECT 1's WORKAROUND, PART TWO, REPRODUCED VERBATIM (C-B). Reproduces
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L158-L191</c>. The underlying
    /// row loss the legacy warned about at <c>:L148</c> is neither fixed nor detected here - THE
    /// WORKAROUND IS THE BEHAVIOUR - and the blob is taken from the TEMPORARY carrier at <c>:L171</c>,
    /// never from the source.
    /// </para>
    /// <para>
    /// DEFECT 2, POSITION (a): RESET IS FORBIDDEN AS THE MID-LOOP CLEAR. The legacy discards the moved
    /// rows individually [<c>:L177-L182</c>] and its comment at <c>:L176</c> says why -
    /// <c>不能使用Reset来清除数据，会使SetChanges应用不到行</c>, "Reset must not be used to clear the
    /// data; doing so makes SetChanges fail to apply to rows". There is NO Reset call anywhere in this
    /// method.
    /// </para>
    /// <para>
    /// THE ORDER IS CONTRACT: capture, then clear, then hand over, then clear the payload, then yield.
    /// The clear happens BEFORE the handover on both branches [<c>:L177-L183</c>, <c>:L209-L219</c>], so
    /// the staging carrier is already empty by the time the chunk leaves.
    /// </para>
    /// </remarks>
    private async Task<ChunkLoopResult> TransferThroughTemporaryCarrierAsync(
        ChangesetTransferRequest request,
        IChangesetTransferSink sink,
        DataWindowBufferStore temporary,
        long chunkCount,
        CancellationToken cancellationToken)
    {
        DataWindowBufferStore source = request.Source;
        long chunkSize = request.ChunkSize;
        long chunksSent = 0L;
        CarrierState? payloadInFlight = null;

        // DECISION 3 - THE THIRD, LATENT QUIRK. `long nRow,nRowCnt,nFilterCnt` is declared ONCE,
        // OUTSIDE the chunk loop [:L75], and nFilterCnt is assigned ONLY inside the guard at :L164.
        // This declaration is therefore deliberately hoisted out of the loop and deliberately NOT
        // cleared at the top of an iteration: on an iteration where that guard is false, the discard
        // at :L181 runs with the PREVIOUS iteration's count against an already-emptied Filter buffer.
        // Zeroing it here would be a correction, and C-B forbids corrections.
        long filteredMovedCount = 0L;

        // `for nChunkIdx = 1 to nChunkCnt` [:L158]. R9 item 2: the index is ONE-BASED and travels to
        // the wire as `current`, where the receiving side tests it against 1.
        for (long chunkIndex = 1L; chunkIndex <= chunkCount; chunkIndex++)
        {
            // `nRowCnt = Min(_nChunkSize,Data.RowCount())` [:L159]. Re-read every iteration, because
            // the source shrinks as rows move out.
            long movedCount = Math.Min(chunkSize, source.RowCount());

            // `Data.RowsMove(1,nRowCnt,Primary!,dsTmp,1,Primary!)` [:L160]. R9 item 5: the destination
            // row is 1, the one-based head of the buffer, not a zero-based offset. The result is
            // IGNORED exactly as the legacy ignores it - an empty range answers failure and moves
            // nothing, which is the correct outcome when there is nothing to move.
            _ = source.RowsMove(
                ItemStatusMachine.FirstRowNumber,
                movedCount,
                DwBuffer.Primary,
                temporary,
                ItemStatusMachine.FirstRowNumber,
                DwBuffer.Primary);

            // `for nRow = 1 to nRowCnt : dsTmp.SetItemStatus(nRow,0,Primary!,DataModified!)`
            // [:L161-L163].
            StampRowsAsModified(temporary, DwBuffer.Primary, movedCount);

            // `if nRowCnt < _nChunkSize and Data.FilteredCount() > 0 then` [:L164]. The chunk is
            // topped up from the filter buffer ONLY when the primary buffer could not fill it, which
            // is why the primary buffer always drains first.
            if (movedCount < chunkSize && source.FilteredCount() > 0L)
            {
                // `nFilterCnt = Min(_nChunkSize - nRowCnt,Data.FilteredCount())` [:L165].
                filteredMovedCount = Math.Min(chunkSize - movedCount, source.FilteredCount());

                // `Data.RowsMove(1,nFilterCnt,Filter!,dsTmp,1,Filter!)` [:L166]. R9 item 5 again.
                _ = source.RowsMove(
                    ItemStatusMachine.FirstRowNumber,
                    filteredMovedCount,
                    DwBuffer.Filter,
                    temporary,
                    ItemStatusMachine.FirstRowNumber,
                    DwBuffer.Filter);

                // `for nRow = 1 to nFilterCnt : dsTmp.SetItemStatus(nRow,0,Filter!,DataModified!)`
                // [:L167-L169].
                StampRowsAsModified(temporary, DwBuffer.Filter, filteredMovedCount);
            }

            // `if dsTmp.GetChanges(ref blbData) < 0 then` [:L171]. FROM THE TEMPORARY CARRIER, which
            // is the whole of DEFECT 1's workaround.
            if (_payloadCodec.TryEncode(temporary, out payloadInFlight)
                < DataWindowBufferStore.DataStoreSuccess)
            {
                // `Event OnError(RetCode.E_INTERNAL_ERROR,"GetChanges Failed")` [:L172] then
                // `return RetCode.E_INTERNAL_ERROR` [:L173].
                sink.ReportError(RetCode.E_INTERNAL_ERROR, GetChangesFailedText);

                return new ChunkLoopResult(
                    RetCode.E_INTERNAL_ERROR,
                    chunksSent,
                    payloadInFlight is null,
                    GetChangesFailedText);
            }

            // DEFECT 2, POSITION (a). `if nRowCnt > 0 then dsTmp.RowsDiscard(1,nRowCnt,Primary!)`
            // [:L177-L179]. R9 item 6: an INCLUSIVE one-based range whose end is the last valid row.
            if (movedCount > 0L)
            {
                _ = temporary.RowsDiscard(
                    ItemStatusMachine.FirstRowNumber,
                    movedCount,
                    DwBuffer.Primary);
            }

            // `if nFilterCnt > 0 then dsTmp.RowsDiscard(1,nFilterCnt,Filter!)` [:L180-L182]. THIS IS
            // THE SITE OF THE LATENT QUIRK: the count may be the previous iteration's. The result is
            // discarded exactly as the legacy discards it, which is precisely why RowsDiscard answers
            // a failure code for an unaddressable range instead of throwing - an exception here would
            // be a regression the legacy could not have had.
            if (filteredMovedCount > 0L)
            {
                _ = temporary.RowsDiscard(
                    ItemStatusMachine.FirstRowNumber,
                    filteredMovedCount,
                    DwBuffer.Filter);
            }

            // `if tasking.Event OnDataChunk(ref blbData,nChunkCnt,nChunkIdx,false) < 0 then` [:L183].
            if (await sink
                    .SendChunkAsync(
                        new ChangesetChunk(payloadInFlight, chunkCount, chunkIndex),
                        cancellationToken)
                    .ConfigureAwait(false)
                < 0L)
            {
                // `Event OnError(RetCode.E_INTERNAL_ERROR,"TransData Failed")` [:L184] then
                // `return RetCode.E_INTERNAL_ERROR` [:L185]. NOTE that the legacy returns here WITHOUT
                // clearing the blob - the clear at :L187 is never reached - so PayloadCleared is
                // reported as the actual state rather than asserted to be true.
                sink.ReportError(RetCode.E_INTERNAL_ERROR, TransDataFailedText);

                return new ChunkLoopResult(
                    RetCode.E_INTERNAL_ERROR,
                    chunksSent,
                    payloadInFlight is null,
                    TransDataFailedText);
            }

            chunksSent++;

            // `blbData = Blob("")` [:L187]. DECISION 6 - the ownership handover is complete, so the
            // sender drops the payload.
            payloadInFlight = null;

            // `if nChunkIdx < nChunkCnt then if Not of_Wait(0.02) then return RetCode.CANCELLED`
            // [:L188-L190]. No yield after the LAST chunk, which is why the condition is strict.
            if (chunkIndex < chunkCount
                && !await WaitAsync(request.InterChunkYield, cancellationToken).ConfigureAwait(false))
            {
                return new ChunkLoopResult(
                    RetCode.CANCELLED,
                    chunksSent,
                    payloadInFlight is null,
                    null);
            }
        }

        // `Destroy dsTmp` [:L192]. No counterpart is needed or wanted - see CreateTemporaryCarrier.
        return new ChunkLoopResult(RetCode.OK, chunksSent, payloadInFlight is null, null);
    }

    /// <summary>
    /// The unsorted or single-chunk branch: each chunk is captured from the source in place.
    /// </summary>
    /// <param name="request">The transfer inputs.</param>
    /// <param name="sink">Where the chunks and the errors go.</param>
    /// <param name="chunkCount">The chunk count.</param>
    /// <param name="cancellationToken">The task's cancellation token.</param>
    /// <returns>The loop result.</returns>
    /// <remarks>
    /// <para>
    /// Reproduces <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L194-L227</c>.
    /// The rows are stamped ON THE SOURCE and the blob is taken FROM THE SOURCE, with no staging
    /// carrier: chunking works because the rows a chunk carried are removed before the next one is
    /// stamped.
    /// </para>
    /// <para>
    /// DEFECT 2, POSITION (b): RESET IS USED HERE, AND ONLY ON THE FINAL CHUNK.
    /// <c>if nChunkIdx &lt; nChunkCnt then</c> discard both buffers <c>else Data.Reset()</c>
    /// [<c>:L209-L218</c>]. It is safe for one specific reason - it runs AFTER the changeset has
    /// already been captured at <c>:L205</c>, so there is no later capture for it to break - and that
    /// is exactly the distinction position (a) turns on. A codec that banned Reset outright would be as
    /// wrong as one that reset freely.
    /// </para>
    /// </remarks>
    private async Task<ChunkLoopResult> TransferInPlaceAsync(
        ChangesetTransferRequest request,
        IChangesetTransferSink sink,
        long chunkCount,
        CancellationToken cancellationToken)
    {
        DataWindowBufferStore source = request.Source;
        long chunkSize = request.ChunkSize;
        long chunksSent = 0L;
        CarrierState? payloadInFlight = null;

        // DECISION 3 again: the same hoisted, never-cleared filtered count [:L75], whose stale value
        // reaches the discard at :L214 on an iteration where the guard at :L199 is false.
        long filteredStampedCount = 0L;

        // `for nChunkIdx = 1 to nChunkCnt` [:L194]. R9 item 2.
        for (long chunkIndex = 1L; chunkIndex <= chunkCount; chunkIndex++)
        {
            // `nRowCnt = Min(_nChunkSize,Data.RowCount())` [:L195].
            long stampedCount = Math.Min(chunkSize, source.RowCount());

            // `for nRow = 1 to nRowCnt : Data.SetItemStatus(nRow,0,Primary!,DataModified!)`
            // [:L196-L198]. Stamping is what makes the rows visible to the changeset extraction, so
            // exactly this chunk's rows are the ones that get captured.
            StampRowsAsModified(source, DwBuffer.Primary, stampedCount);

            // `if nRowCnt < _nChunkSize and Data.FilteredCount() > 0 then` [:L199].
            if (stampedCount < chunkSize && source.FilteredCount() > 0L)
            {
                // `nFilterCnt = Min(_nChunkSize - nRowCnt,Data.FilteredCount())` [:L200].
                filteredStampedCount = Math.Min(chunkSize - stampedCount, source.FilteredCount());

                // `for nRow = 1 to nFilterCnt : Data.SetItemStatus(nRow,0,Filter!,DataModified!)`
                // [:L201-L203].
                StampRowsAsModified(source, DwBuffer.Filter, filteredStampedCount);
            }

            // `if Data.GetChanges(ref blbData) < 0 then` [:L205]. FROM THE SOURCE on this branch.
            if (_payloadCodec.TryEncode(source, out payloadInFlight)
                < DataWindowBufferStore.DataStoreSuccess)
            {
                // `Event OnError(RetCode.E_INTERNAL_ERROR,"GetChanges Failed")` [:L206] then
                // `return RetCode.E_INTERNAL_ERROR` [:L207].
                sink.ReportError(RetCode.E_INTERNAL_ERROR, GetChangesFailedText);

                return new ChunkLoopResult(
                    RetCode.E_INTERNAL_ERROR,
                    chunksSent,
                    payloadInFlight is null,
                    GetChangesFailedText);
            }

            // DEFECT 2, POSITION (b). `if nChunkIdx < nChunkCnt then` [:L209].
            if (chunkIndex < chunkCount)
            {
                // `if nRowCnt > 0 then Data.RowsDiscard(1,nRowCnt,Primary!)` [:L210-L212]. R9 item 6.
                if (stampedCount > 0L)
                {
                    _ = source.RowsDiscard(
                        ItemStatusMachine.FirstRowNumber,
                        stampedCount,
                        DwBuffer.Primary);
                }

                // `if nFilterCnt > 0 then Data.RowsDiscard(1,nFilterCnt,Filter!)` [:L213-L215]. The
                // second site of the latent quirk of DECISION 3; the result is discarded, as it is in
                // the legacy.
                if (filteredStampedCount > 0L)
                {
                    _ = source.RowsDiscard(
                        ItemStatusMachine.FirstRowNumber,
                        filteredStampedCount,
                        DwBuffer.Filter);
                }
            }
            else
            {
                // `Data.Reset()` [:L217]. LEGAL HERE, and only here on the send side: the capture at
                // :L205 has already happened, so there is nothing left for a later apply to miss.
                _ = source.Reset();
            }

            // `if tasking.Event OnDataChunk(ref blbData,nChunkCnt,nChunkIdx,false) < 0 then` [:L219].
            if (await sink
                    .SendChunkAsync(
                        new ChangesetChunk(payloadInFlight, chunkCount, chunkIndex),
                        cancellationToken)
                    .ConfigureAwait(false)
                < 0L)
            {
                // `Event OnError(...,"TransData Failed")` [:L220] then return [:L221].
                sink.ReportError(RetCode.E_INTERNAL_ERROR, TransDataFailedText);

                return new ChunkLoopResult(
                    RetCode.E_INTERNAL_ERROR,
                    chunksSent,
                    payloadInFlight is null,
                    TransDataFailedText);
            }

            chunksSent++;

            // `blbData = Blob("")` [:L223]. DECISION 6.
            payloadInFlight = null;

            // `if nChunkIdx < nChunkCnt then if Not of_Wait(0.02) then return RetCode.CANCELLED`
            // [:L224-L226].
            if (chunkIndex < chunkCount
                && !await WaitAsync(request.InterChunkYield, cancellationToken).ConfigureAwait(false))
            {
                return new ChunkLoopResult(
                    RetCode.CANCELLED,
                    chunksSent,
                    payloadInFlight is null,
                    null);
            }
        }

        return new ChunkLoopResult(RetCode.OK, chunksSent, payloadInFlight is null, null);
    }

    /// <summary>
    /// Transfers the drop-down child results that travel alongside the main result.
    /// </summary>
    /// <param name="children">
    /// The ELIGIBLE children, in column order. Eligibility is decided by
    /// <see cref="IsChildEligible"/> at the layer that owns the definition; see the remarks on
    /// <see cref="ChangesetChildSource"/>.
    /// </param>
    /// <param name="sink">Where the child payloads and the errors go.</param>
    /// <param name="cancellationToken">The task's cancellation token.</param>
    /// <returns>The outcome, whose return code is the value the legacy event returns.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="children"/> or <paramref name="sink"/> is <see langword="null"/>, or a member of
    /// <paramref name="children"/> is.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Reproduces <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L112-L142</c>.
    /// Per child: stamp every <c>Primary!</c> row and then every <c>Filter!</c> row
    /// [<c>:L121-L128</c>], extract [<c>:L129</c>], reset [<c>:L133</c>], hand over [<c>:L134</c>],
    /// clear the payload [<c>:L138</c>] and re-check cancellation [<c>:L139</c>].
    /// </para>
    /// <para>
    /// DEFECT 2, POSITION (c): RESET IS USED, between the capture and the handover [<c>:L133</c>]. Safe
    /// for the same reason as position (b) - nothing re-captures from this child afterwards.
    /// </para>
    /// <para>
    /// THE ERROR TEXTS CARRY THE COLUMN NAME IN SQUARE BRACKETS, and both are preserved verbatim:
    /// <c>"[" + sColName + "] GetChanges Failed"</c> [<c>:L130</c>] and
    /// <c>"[" + sColName + "] TransData Failed"</c> [<c>:L135</c>]. The bracket, the space after the
    /// closing bracket and the capitalisation are all part of what a characterization recording holds.
    /// </para>
    /// <para>
    /// A child whose <c>GetChild</c> lookup fails is SKIPPED rather than treated as an error
    /// [<c>:L120</c>], and that skip belongs to the caller for the same reason the eligibility filter
    /// does: it is a lookup against the definition, not against the buffers.
    /// </para>
    /// </remarks>
    internal async Task<ChangesetChildTransferOutcome> TransferChildrenAsync(
        IReadOnlyList<ChangesetChildSource> children,
        IChangesetTransferSink sink,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(children);
        ArgumentNullException.ThrowIfNull(sink);

        int childrenSent = 0;
        CarrierState? payloadInFlight = null;

        // `for nIndex = 1 to nCount` [:L114], narrowed to the columns that passed both filters. The
        // list order IS the legacy's column order, so the handover sequence is unchanged.
        foreach (ChangesetChildSource child in children)
        {
            ArgumentNullException.ThrowIfNull(child);

            // `nRowCnt = dwcSrc.RowCount() : for nRow = 1 to nRowCnt : SetItemStatus(nRow,0,Primary!,
            // DataModified!)` [:L121-L124]. EVERY row, not a chunk of them: a child result is
            // transferred whole.
            StampRowsAsModified(child.Child, DwBuffer.Primary, child.Child.RowCount());

            // `nRowCnt = dwcSrc.FilteredCount() : for nRow = 1 to nRowCnt : SetItemStatus(nRow,0,
            // Filter!,DataModified!)` [:L125-L128]. Note that the legacy REUSES nRowCnt here, so the
            // primary count is gone by this point; nothing downstream reads it, and the reuse is
            // reproduced by simply reading each count at its point of use.
            StampRowsAsModified(child.Child, DwBuffer.Filter, child.Child.FilteredCount());

            // `if dwcSrc.GetChanges(ref blbData) < 0 then` [:L129].
            if (_payloadCodec.TryEncode(child.Child, out payloadInFlight)
                < DataWindowBufferStore.DataStoreSuccess)
            {
                // `Event OnError(RetCode.E_INTERNAL_ERROR,"[" + sColName + "] GetChanges Failed")`
                // [:L130] then return [:L131].
                string errorText = ChildErrorText(child.ColumnName, GetChangesFailedText);

                sink.ReportError(RetCode.E_INTERNAL_ERROR, errorText);

                return new ChangesetChildTransferOutcome(
                    RetCode.E_INTERNAL_ERROR,
                    childrenSent,
                    payloadInFlight is null,
                    errorText);
            }

            // DEFECT 2, POSITION (c). `dwcSrc.Reset()` [:L133].
            _ = child.Child.Reset();

            // `if tasking.Event OnChildDataReceived(sColName,ref blbData) < 0 then` [:L134].
            if (await sink
                    .SendChildChunkAsync(
                        new ChangesetChildPayload(child.ColumnName, payloadInFlight),
                        cancellationToken)
                    .ConfigureAwait(false)
                < 0L)
            {
                // `Event OnError(RetCode.E_INTERNAL_ERROR,"[" + sColName + "] TransData Failed")`
                // [:L135] then return [:L136].
                string errorText = ChildErrorText(child.ColumnName, TransDataFailedText);

                sink.ReportError(RetCode.E_INTERNAL_ERROR, errorText);

                return new ChangesetChildTransferOutcome(
                    RetCode.E_INTERNAL_ERROR,
                    childrenSent,
                    payloadInFlight is null,
                    errorText);
            }

            childrenSent++;

            // `blbData = Blob("")` [:L138].
            payloadInFlight = null;

            // `if of_IsCancelled() then return RetCode.CANCELLED` [:L139] - INSIDE the loop, so a
            // cancellation stops the walk rather than merely being noticed after it.
            if (cancellationToken.IsCancellationRequested)
            {
                return new ChangesetChildTransferOutcome(
                    RetCode.CANCELLED,
                    childrenSent,
                    payloadInFlight is null,
                    null);
            }
        }

        // `if of_IsCancelled() then return RetCode.CANCELLED` [:L142] - the check that follows the
        // loop. It is a SECOND check, not a duplicate of the one inside: this one also fires when the
        // list was empty.
        if (cancellationToken.IsCancellationRequested)
        {
            return new ChangesetChildTransferOutcome(
                RetCode.CANCELLED,
                childrenSent,
                payloadInFlight is null,
                null);
        }

        return new ChangesetChildTransferOutcome(
            RetCode.OK,
            childrenSent,
            payloadInFlight is null,
            null);
    }

    #endregion

    #region Send-path helpers

    /// <summary>
    /// Stamps the first <paramref name="rowCount"/> rows of a buffer as data-modified, which is what
    /// makes exactly those rows the ones a changeset extraction captures.
    /// </summary>
    /// <param name="carrier">The carrier to stamp.</param>
    /// <param name="dwBuffer">The buffer to stamp.</param>
    /// <param name="rowCount">How many rows from the head of the buffer. Zero stamps nothing.</param>
    /// <remarks>
    /// <para>
    /// Reproduces all SIX stamping loops of
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru</c> - <c>:L122-L124</c> and
    /// <c>:L126-L128</c> for a drop-down child, <c>:L161-L163</c> and <c>:L167-L169</c> for the
    /// temporary carrier, <c>:L196-L198</c> and <c>:L201-L203</c> for the in-place branch - every one of
    /// which passes column index zero and the status <c>DataModified!</c>.
    /// </para>
    /// <para>
    /// R9 ITEM 3. <c>for nRow = 1 to nRowCnt</c> is reproduced as
    /// <c>for (row = FirstRowNumber; row &lt;= rowCount; row++)</c>: ONE-BASED, INCLUSIVE AT BOTH ENDS,
    /// and the bound is a COUNT that doubles as the last valid row number in that domain. The loop shape
    /// is kept deliberately close to the original so it stays diffable against its locator.
    /// </para>
    /// <para>
    /// The COLUMN-ZERO SENTINEL comes from <see cref="ItemStatusMachine.RowStatusColumn"/> rather than a
    /// bare literal, because zero here means "the row itself" and is not a column index at all (R9 item
    /// 4 of <c>Buffers/DataWindowBuffers.cs</c>). The status write itself is delegated to the carrier,
    /// which routes it through the status machine.
    /// </para>
    /// </remarks>
    private static void StampRowsAsModified(
        DataWindowBufferStore carrier,
        DwBuffer dwBuffer,
        long rowCount)
    {
        for (long rowNumber = ItemStatusMachine.FirstRowNumber;
            rowNumber <= rowCount;
            rowNumber++)
        {
            // The legacy ignores this result at all six sites, and so does this.
            _ = carrier.SetItemStatus(
                rowNumber,
                ItemStatusMachine.RowStatusColumn,
                dwBuffer,
                ItemStatus.DataModified);
        }
    }

    /// <summary>
    /// Builds the bracketed per-column error text.
    /// </summary>
    /// <param name="columnName">The column name.</param>
    /// <param name="text">Either <see cref="GetChangesFailedText"/> or <see cref="TransDataFailedText"/>.</param>
    /// <returns>The text, verbatim in the legacy's own shape.</returns>
    /// <remarks>
    /// Reproduces <c>"[" + sColName + "] GetChanges Failed"</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L130</c>] and
    /// <c>"[" + sColName + "] TransData Failed"</c> [<c>:L135</c>] - one opening bracket, the column
    /// name, one closing bracket, one space, then the text. Both operands are strings, so no format
    /// provider is involved and none could change the result.
    /// </remarks>
    private static string ChildErrorText(string columnName, string text)
    {
        return string.Concat("[", columnName, "] ", text);
    }

    /// <summary>
    /// Yields cooperatively between chunks and reports whether the sequence may continue.
    /// </summary>
    /// <param name="interval">The yield interval. A non-positive value still yields once.</param>
    /// <param name="cancellationToken">The task's cancellation token.</param>
    /// <returns>
    /// <see langword="false"/> when cancellation was requested before, during or after the yield, which
    /// is the answer the legacy reacts to.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Reproduces <c>if Not of_Wait(0.02) then return RetCode.CANCELLED</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L189</c>, <c>:L225</c>]. The
    /// legacy call does two things at once - it lets the worker breathe between chunks and it observes
    /// cancellation - and both are preserved: control is yielded, and the token is checked on both
    /// sides of the yield.
    /// </para>
    /// <para>
    /// THE CANCELLATION EXCEPTION IS CAUGHT AND CONVERTED, NOT PROPAGATED, and that is required rather
    /// than defensive: the legacy answers a boolean that becomes a return CODE, so a caller written
    /// against this contract tests a value and would not catch an
    /// <see cref="OperationCanceledException"/>. Converting it is what keeps
    /// <c>RetCode.CANCELLED</c> reachable. Cancellation is never silently turned into success - every
    /// caller of this helper returns <c>RetCode.CANCELLED</c> on a false answer.
    /// </para>
    /// <para>
    /// C-H. The delay is scheduled through the INJECTED <see cref="TimeProvider"/>, so a test can drive
    /// a many-chunk sequence with a zero interval and still exercise the yield and the cancellation
    /// check. No ambient clock is read.
    /// </para>
    /// </remarks>
    private async ValueTask<bool> WaitAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        // A negative interval is clamped rather than rejected: the legacy interval is a literal and
        // could never be negative, so there is no legacy behaviour for a negative one to preserve, and
        // failing a whole transfer over a configuration typo would be worse than yielding once.
        TimeSpan yieldFor = interval < TimeSpan.Zero ? TimeSpan.Zero : interval;

        try
        {
            await Task.Delay(yieldFor, _timeProvider, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        return !cancellationToken.IsCancellationRequested;
    }

    /// <summary>
    /// What one chunk loop produced, so that both branches can report the same four facts to
    /// <see cref="TransferAsync"/>.
    /// </summary>
    /// <param name="ReturnCode">The framework return code the loop ended with.</param>
    /// <param name="ChunksSent">How many chunks were handed over.</param>
    /// <param name="PayloadCleared">Whether the loop was holding no payload when it ended.</param>
    /// <param name="ErrorText">The verbatim error text, or <see langword="null"/>.</param>
    private sealed record ChunkLoopResult(
        long ReturnCode,
        long ChunksSent,
        bool PayloadCleared,
        string? ErrorText);

    #endregion

    #region The receive path - n_cst_threading_task_sqlquery.sru:L93-L155, L180-L258

    /// <summary>
    /// Applies one main-result chunk to a target carrier: clear on the first chunk, apply, clear the
    /// update flags on the last, then coerce a partial apply to failure.
    /// </summary>
    /// <param name="target">The carrier to populate.</param>
    /// <param name="chunk">The chunk to apply.</param>
    /// <param name="cancellationToken">
    /// The task's cancellation token, standing in for <c>of_IsCancelled()</c>.
    /// </param>
    /// <returns>The outcome, whose result is in PowerBuilder's convention.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="target"/> or <paramref name="chunk"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Reproduces <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L180-L258</c>,
    /// specifically its changeset arm. The legacy writes the same logic THREE TIMES, once per receiver
    /// kind - a DataWindow receiver [<c>:L185-L210</c>], a DataStore receiver [<c>:L211-L230</c>] and
    /// its own internal carrier [<c>:L231-L243</c>] - and this is that logic once.
    /// </para>
    /// <para>
    /// C-D - THE THREE ARMS DIFFER IN EXACTLY ONE WAY AND IT IS PRESENTATIONAL. The DataWindow arm also
    /// calls <c>dw.SetRedraw(false)</c> before the first chunk [<c>:L194</c>] and
    /// <c>dw.GroupCalc()</c> plus <c>dw.SetRedraw(true)</c> after the last [<c>:L200</c>, <c>:L203</c>];
    /// the structurally identical DataStore arm calls NEITHER. Two independent sightings of that
    /// asymmetry - the same pattern recurs in <c>n_cst_threading_task_sqlupdate.sru</c> - are the
    /// evidence that suspending redraw and recalculating groups are PRESENTATION and belong to the
    /// deferred DesignSystem capability area. THEY ARE A DOCUMENTED NON-PORT: omitted here, named here,
    /// and reachable later through the reserved gateway route. <c>ResetUpdate</c> appears in ALL THREE
    /// arms and is therefore NOT presentational; it is kept.
    /// </para>
    /// <para>
    /// ONE LEGACY ASYMMETRY IS DELIBERATELY NOT REPRODUCED, AND IS RECORDED INSTEAD. The two receiver
    /// arms guard on <c>if Len(blbData) &gt; 0</c> and clear the target when the payload is empty
    /// [<c>:L188-L210</c>, <c>:L214-L229</c>], whereas the internal-carrier arm has NO SUCH GUARD and
    /// hands an empty payload straight to the apply [<c>:L231-L243</c>]. The guarded form is the one
    /// implemented, because it is the behaviour the send side's empty-result arm at <c>:L230</c> exists
    /// to trigger and because an unguarded apply of an empty payload would answer failure where the
    /// legacy answers success. The unguarded arm is a legacy inconsistency, not a third behaviour.
    /// </para>
    /// <para>
    /// A FULL-STATE CHUNK IS REFUSED, NOT HALF-HANDLED. <see cref="ChangesetChunk"/> cannot carry the
    /// other selector value, so misrouting is prevented by the type system on this path; a caller
    /// reading the wire flag must dispatch on it before constructing the chunk. See
    /// <see cref="ChangesetChunk.ChangesetSelector"/>.
    /// </para>
    /// </remarks>
    internal ChangesetApplyOutcome ApplyChunk(
        DataWindowBufferStore target,
        ChangesetChunk chunk,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(chunk);

        // `if of_IsCancelled() then return 0` [:L182]. ZERO - the continue value - and not one and not
        // minus one. The consequence matters: the notification at :L253 is gated on exactly one, so a
        // cancelled chunk publishes nothing.
        if (cancellationToken.IsCancellationRequested)
        {
            return new ChangesetApplyOutcome(
                DataWindowBufferStore.EventContinue,
                TargetCleared: false,
                UpdateFlagsReset: false,
                PartialApplyCoerced: false,
                Notified: false);
        }

        long result;
        bool targetCleared = false;
        bool updateFlagsReset = false;

        if (chunk.IsEmptyPayload)
        {
            // `rtCode = 1 : dw.Reset()` [:L207-L210] and `rtCode = 1 : ds.Reset()` [:L226-L229]. AN
            // EMPTY PAYLOAD MEANS CLEAR, not "nothing to do", and it is exactly the shape the send
            // side's empty-result arm produces at :L230. Note the ORDER: the legacy sets the code
            // first and resets second, and the reset is unconditional on this arm.
            result = DataWindowBufferStore.DataStoreSuccess;

            _ = target.Reset();
            targetCleared = true;
        }
        else
        {
            // DEFECT 2, POSITION (d). `if current = 1 then ... Reset()` [:L192-L196, :L218-L220,
            // :L235-L237]. RESET IS LEGAL HERE because it PRECEDES the apply rather than interleaving
            // with a later capture - which is the distinction that makes position (a)'s prohibition and
            // this call consistent with each other.
            if (chunk.IsFirstChunk)
            {
                _ = target.Reset();
                targetCleared = true;
            }

            // `rtCode = dw.SetChanges(blbData)` [:L197, :L221, :L238]. Chunks after the first ACCUMULATE
            // onto the target, which is why the reset above is gated on the first chunk alone.
            // THE TRANSFER READING: this payload was produced by TryEncode on the sending side, whose
            // encoding omits an original exactly where it equals the current value.
            result = _payloadCodec.TryApply(target, chunk.State, CarrierBaselineTrust.AsStated);

            // `if count = current then dw.ResetUpdate()` [:L198-L199, :L222-L223, :L239-L240]. Run
            // UNCONDITIONALLY on the last chunk, without consulting the apply result - the legacy does
            // not consult it either, and this is what makes freshly delivered rows read as unmodified
            // with their originals equal to their currents. The GroupCalc and SetRedraw calls that sit
            // beside it in the DataWindow arm are the documented non-port; ResetUpdate is not.
            if (chunk.IsLastChunk)
            {
                _ = target.ResetUpdate();
                updateFlagsReset = true;
            }
        }

        // `blbData = Blob("")` [:L245]. Nothing to reproduce beyond not retaining the payload: the chunk
        // belongs to the caller and this method keeps no reference to it after returning.

        // THE STRICT COERCION. `if rtCode > 1 then rtCode = -1` on the CHANGESET arm [:L250], against
        // `if rtCode > 1 then rtCode = 1` on the full-state arm [:L248]. A partial apply - the value
        // PowerBuilder answers when the payload and the target disagree about their definitions - is a
        // FAILURE here and a SUCCESS there. Getting the direction backwards would report a definition
        // mismatch as a clean transfer, and no row-count assertion would notice.
        bool partialApplyCoerced = result > DataWindowBufferStore.DataStoreSuccess;

        if (partialApplyCoerced)
        {
            result = DataWindowBufferStore.DataStoreFailure;
        }

        // `if rtCode = 1 then Event OnNotify(NCD_DATACHUNK,MakeLong(count,current),"")` [:L253-L255].
        // EXACT EQUALITY with one, so neither a failure nor the cancelled zero publishes anything. The
        // notification itself belongs to the layer that owns the stream; what is reproduced here is the
        // DECISION, which is the part that is behaviour.
        bool notified = result == DataWindowBufferStore.DataStoreSuccess;

        return new ChangesetApplyOutcome(
            result,
            targetCleared,
            updateFlagsReset,
            partialApplyCoerced,
            notified);
    }

    /// <summary>
    /// Applies one drop-down child payload to a child carrier: reset unconditionally, apply, coerce a
    /// partial apply to failure, then clear the update flags and decide whether the child's filter and
    /// sort must be re-applied.
    /// </summary>
    /// <param name="childTarget">The child carrier to populate.</param>
    /// <param name="payload">The payload to apply.</param>
    /// <param name="childDefinition">
    /// The CHILD's own definition, read for its filter and sort expressions. This is the child's
    /// definition and not the parent's: the legacy asks <c>dwc.Describe(...)</c>, on the child object
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L145</c>, <c>:L148</c>].
    /// </param>
    /// <param name="cancellationToken">The task's cancellation token.</param>
    /// <returns>The outcome, whose result is in PowerBuilder's convention.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="childTarget"/>, <paramref name="payload"/> or
    /// <paramref name="childDefinition"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Reproduces <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L93-L155</c>,
    /// again collapsing three structurally identical receiver arms [<c>:L98-L111</c>, <c>:L112-L125</c>,
    /// <c>:L126-L137</c>] into one.
    /// </para>
    /// <para>
    /// THE RESET IS UNCONDITIONAL HERE, WHICH IS DIFFERENT FROM THE MAIN RECEIVER. <c>dwc.Reset()</c>
    /// runs before the payload is even inspected [<c>:L103</c>, <c>:L116</c>, <c>:L128</c>], whereas
    /// <see cref="ApplyChunk"/> resets only on the first chunk. The reason is structural: a child result
    /// is transferred WHOLE in a single payload, so there is no sequence for a first-chunk condition to
    /// discriminate, and an empty payload therefore still clears the child - it just does so on the way
    /// in rather than on a branch.
    /// </para>
    /// <para>
    /// THE TWO RE-APPLICATION FLAGS ARE A DECISION REPRODUCED, WITH ITS EXECUTION DELEGATED, AND THE
    /// SPLIT IS DELIBERATE (C-K). The legacy calls <c>dwc.Filter()</c> and <c>dwc.Sort()</c> when the
    /// respective expression is longer than one character [<c>:L145-L150</c>]. THE PREDICATE IS
    /// BEHAVIOUR and is reproduced exactly, threshold included - see
    /// <see cref="ChangesetSourceDefinition.FilterIsSubstantial"/> for why it is length greater than one
    /// rather than a non-empty test. EXECUTING those two operations, however, means EVALUATING A
    /// DataWindow EXPRESSION against the rows, and no expression evaluator exists in this service: the
    /// refactor plan assigns that capability to DataServices, where it is the single largest net-new
    /// obligation in the migration. Surfacing the decision as an outcome flag is therefore the faithful
    /// choice - nothing is silently dropped, the caller that owns an evaluator can act on it, and this
    /// file does not grow a dependency it has no business having.
    /// </para>
    /// </remarks>
    internal ChangesetChildApplyOutcome ApplyChildPayload(
        DataWindowBufferStore childTarget,
        ChangesetChildPayload payload,
        ChangesetSourceDefinition childDefinition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(childTarget);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(childDefinition);

        // `if of_IsCancelled() then return 0` [:L96]. Zero again, and the notification at :L151 is
        // gated on exactly one, so nothing is published.
        if (cancellationToken.IsCancellationRequested)
        {
            return new ChangesetChildApplyOutcome(
                DataWindowBufferStore.EventContinue,
                TargetCleared: false,
                UpdateFlagsReset: false,
                PartialApplyCoerced: false,
                RequiresFilterReapplication: false,
                RequiresSortReapplication: false,
                Notified: false);
        }

        // `dwc.Reset()` [:L103, :L116, :L128] - UNCONDITIONAL and BEFORE the apply. DEFECT 2's
        // prohibition does not reach here for the same reason it does not reach position (d): this reset
        // precedes the apply instead of interleaving with a later capture.
        _ = childTarget.Reset();

        // `if Len(blbData) > 0 then rtCode = dwc.SetChanges(blbData) else rtCode = 1`
        // [:L104-L108, :L117-L121, :L129-L133].
        long result = payload.IsEmptyPayload
            ? DataWindowBufferStore.DataStoreSuccess
            : _payloadCodec.TryApply(childTarget, payload.State, CarrierBaselineTrust.AsStated);

        // `blbData = Blob("")` [:L139] - the payload belongs to the caller and is not retained.

        // `if rtCode > 1 then rtCode = -1` [:L141]. THE SAME STRICT COERCION as the main changeset
        // path, and note that the child path has no full-state counterpart at all, so there is no
        // lenient sibling here to confuse it with.
        bool partialApplyCoerced = result > DataWindowBufferStore.DataStoreSuccess;

        if (partialApplyCoerced)
        {
            result = DataWindowBufferStore.DataStoreFailure;
        }

        bool updateFlagsReset = false;
        bool requiresFilterReapplication = false;
        bool requiresSortReapplication = false;

        // `if rtCode = 1 then` [:L143] - everything below happens ONLY on a clean apply, unlike the main
        // receiver which clears the update flags on the last chunk regardless of the result.
        if (result == DataWindowBufferStore.DataStoreSuccess)
        {
            // `dwc.ResetUpdate()` [:L144]. Not presentational; kept.
            _ = childTarget.ResetUpdate();
            updateFlagsReset = true;

            // `if Len(dwc.Describe("DataWindow.Table.Filter")) > 1 then dwc.Filter()` [:L145-L147].
            requiresFilterReapplication = childDefinition.FilterIsSubstantial;

            // `if Len(dwc.Describe("DataWindow.Table.Sort")) > 1 then dwc.Sort()` [:L148-L150]. THE
            // ORDER IS FILTER THEN SORT, which is the legacy's order and is not arbitrary - sorting a
            // set that is about to be filtered would order rows that are then removed.
            requiresSortReapplication = childDefinition.SortIsSubstantial;
        }

        // `Event OnNotify(NCD_CHILDRECEIVED,0,name)` [:L151] - inside the same clean-apply branch, so
        // the notification decision and the two re-application decisions share one condition.
        bool notified = result == DataWindowBufferStore.DataStoreSuccess;

        return new ChangesetChildApplyOutcome(
            result,
            TargetCleared: true,
            updateFlagsReset,
            partialApplyCoerced,
            requiresFilterReapplication,
            requiresSortReapplication,
            notified);
    }

    #endregion
}

#endregion
