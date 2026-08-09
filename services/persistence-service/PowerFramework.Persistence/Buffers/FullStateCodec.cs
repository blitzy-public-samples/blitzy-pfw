// ==============================================================================================
//  FullStateCodec.cs - THE GetFullState / SetFullState BLOB CODEC, AND THE TWO DEFECTS IT OWNS
//  --------------------------------------------------------------------------------------------
//  PORTED FROM    ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru
//                     :L93-L101   the codec selector and the full-state SEND path
//                     :L559-L581  the sort/filter decision - DEFECT 3 at :L563, and the
//                                 changeset arm's exactly opposite note at :L578
//                     :L672-L676  DEFECT 4, the no-user-prompt workaround, sitting BEFORE
//                                 SetTransObject at :L678 and before the SQL modify at :L683
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru
//                     :L180-L258  the RECEIVE path, where the `fullstate` boolean selects this
//                                 codec, and :L247-L248 where a code above 1 becomes 1
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru
//                     the carrier pair this codec operates on
//                 docs/PB多线程绕坑提示.md
//                     the two documented PowerBuilder threading hazards; hazard 1 is why the
//                     legacy signature is `GetFullState(ref blbData)` rather than a blob return
//
//  ORACLE STATUS (C-C). Every ws_objects/** and docs/** path above is READ ONLY. They are the
//  behavioural oracle and are never an edit target, never reformatted and never moved. Nothing else
//  in this repository can adjudicate the behaviour reproduced below, so every behavioural claim here
//  carries a line locator a reader can diff against.
//
//  ==============================================================================================
//  READ THIS FIRST - THERE IS NO BEHAVIOURAL ORACLE FOR THIS CODEC, AND THAT IS NOT A CAVEAT
//  ==============================================================================================
//  ALL TWELVE DataWindow definitions in this repository were scanned for their `processing=` value.
//  ELEVEN ARE `processing=1` AND ONE, ws_objects/pfw.tests.pbl.src/dw_barcode.srd, IS `processing=0`.
//  NOT ONE IS 4 OR 5. Since 4 and 5 - Crosstab and Composite - are the only two values that select
//  this codec [n_cst_thread_task_sqlquery.sru:L94], NO LEGACY FIXTURE REACHES THIS PATH AT ALL, and
//  neither of the two defects below can be characterized against a recording.
//
//  Consequences, stated plainly rather than glossed:
//    * Parity for this file is asserted by UNIT TESTS OVER SYNTHETIC CARRIERS whose declared
//      processing kind is crosstab or composite. Those tests are named so that their unit-level
//      status is evident and cannot be mistaken for characterization coverage.
//    * NO CHARACTERIZATION COVERAGE IS CLAIMED OR IMPLIED for anything in this file.
//    * This is the same posture the migration plan takes elsewhere: it declines to claim a verified
//      container bring-up because no Docker daemon was available where it was authored, and it
//      requires reporting a limitation rather than approximating past it. A documented limitation is
//      the correct deliverable here; a confident-sounding claim is not.
//
//  ==============================================================================================
//  WHICH ARM OF THE SELECTOR THIS FILE IS
//  ==============================================================================================
//  The legacy dispatches on the carrier's processing kind [n_cst_thread_task_sqlquery.sru:L93]:
//
//      choose case Long(Data.Describe("DataWindow.Processing"))
//          case 4,5 //Crosstab or Composite datawindow      <-- THIS FILE
//              Data.GetFullState(ref blbData) ...
//          case else                                        <-- Buffers/ChangesetCodec.cs
//              ... GetChanges ...
//      end choose
//
//  THE CODEC SPLIT IS THE LEGACY'S OWN `choose case`, not a structure imposed on it. The processing
//  kind is read from the carrier as a TYPED property - DataWindowBufferStore.RequiresFullStateTransfer,
//  which delegates to DataWindowProcessing.SelectsFullStateTransfer - and NEVER re-parsed from a
//  describe string here. Two codecs re-parsing the same string is two codecs that can disagree about
//  the answer, and a payload routed to the wrong decoder is not a recoverable error.
//
//  Both defects this file owns are CROSSTAB-SHAPED, which is exactly why they land on this arm and
//  not on the changeset one.
//
//  ==============================================================================================
//  DEFECT 3 AND THE :L563 / :L578 OPPOSITION - BOTH ARMS ARE CONTRACT, NEITHER IS AN OVERSIGHT
//  ==============================================================================================
//  Two notes the legacy wrote against itself, in the two arms of one `choose case` [:L560-L581]:
//
//      case 4,5   //NOTE  [:L562]
//                 //使用SetFullState传递数据的风格需要同步排序和过滤条件  [:L563]
//                 "the SetFullState transfer style REQUIRES sort and filter conditions to be
//                  synchronized"   -> it SETS them
//
//      case else  //NOTE  [:L577]
//                 //使用SetChanges传递数据的风格不需要排序和过滤条件，甚至会影响性能  [:L578]
//                 "the SetChanges transfer style DOES NOT NEED sort and filter conditions, and they
//                  can even hurt performance"   -> it CLEARS them
//
//  THE TWO ARMS ARE EXACT OPPOSITES AND BOTH ARE DELIBERATE. A reader who meets only one of them
//  will conclude the other is a bug and "harmonise" it, so the pairing is documented here and the
//  mechanism behind it is stated rather than left as a rule to memorise:
//
//      A FULL-STATE IMAGE REPLACES THE RECEIVER'S ENTIRE DEFINITION. Whatever sort and filter the
//      task means to apply must therefore already be on the SOURCE at the moment of capture, because
//      after SetFullState the receiver's own sort and filter are gone - overwritten by the image.
//      Hence "requires synchronization".
//
//      A CHANGESET CARRIES ROW DATA ONLY. The receiver keeps its own definition, so setting sort and
//      filter on the source changes nothing the receiver will see and merely costs the source a sort
//      and a filter pass. Hence "does not need them, and even hurts performance".
//
//  This file implements the crosstab/composite arm ONLY. The changeset arm belongs to
//  Buffers/ChangesetCodec.cs and is neither reimplemented nor referenced here (C-A).
//
//  ==============================================================================================
//  WHERE THIS FILE SITS IN THE LEGACY'S ORDER OF OPERATIONS
//  ==============================================================================================
//  The order matters and is preserved, because a workaround applied after the operation it works
//  around would work around nothing:
//
//      1. SynchronizeSortAndFilter          [:L559-L575]  DEFECT 3, during task setup
//      2. ApplyNoUserPromptWorkaround       [:L674-L676]  DEFECT 4, still during setup
//      3. (the task attaches its transaction object)      [:L678]   -- Tasks/, not here
//      4. (the task modifies the SQL statement)           [:L683+]  -- Sql/,   not here
//      5. (the task retrieves)                                      -- Tasks/, not here
//      6. Capture + reset + single-chunk handover          [:L95-L101]
//      7. Receive: ResolveTarget then Apply               [n_cst_threading_task_sqlquery.sru:L184-L248]
//
//  ==============================================================================================
//  DELIBERATE DEPARTURES AND NON-PORTS, EACH RECORDED RATHER THAN LEFT TO BE INFERRED (C-K)
//  ==============================================================================================
//  THE `ref blob` SHAPE IS NOT REPRODUCED. The legacy signature is `GetFullState(ref blbData)`
//  [:L95] rather than a blob return, and docs/PB多线程绕坑提示.md hazard 1 is why: a worker thread
//  synchronously calling a main-thread function or event that returns `string` or `blob` can corrupt
//  memory - the trigger being an undelivered posted message against an object that is then destroyed
//  - and the prescribed avoidance is 可以改为ref传回, "return through ref instead". THAT HAZARD HAS
//  NO .NET ANALOGUE: a headless Linux container has no message pump, no PowerBuilder object lifetime
//  and no posted-message queue. Capture therefore returns its payload normally. The departure is a
//  HAZARD WORKAROUND BECOMING UNNECESSARY, not a style preference, and it is recorded so a reader
//  cannot mistake it for carelessness.
//
//  SetRedraw AND GroupCalc ARE A DOCUMENTED NON-PORT (C-D). They appear beside these very operations
//  in the legacy and ONLY in its DataWindow arm of the CHANGESET path
//  [n_cst_threading_task_sqlquery.sru:L193-L194, L200, L203]; the structurally identical DataStore arm
//  [:L218-L224] calls neither, and NEITHER FULL-STATE ARM CALLS EITHER ONE [:L189-L190, :L215-L216,
//  :L232-L233]. Two independent sightings of that asymmetry are the evidence that both calls are
//  PRESENTATIONAL and belong to the deferred DesignSystem capability area, which this phase forbids
//  implementing even as a stub. They are therefore absent here, and their absence is stated.
//
//  THE ELSE-BRANCH OF THE GATE IS NOT THIS FILE'S. The gate at :L559 has an else-branch at
//  :L582-L597 which also applies sort and filter, under a different condition and WITHOUT dispatching
//  on the processing kind at all. Being processing-agnostic, it is shared task behaviour rather than
//  either codec's, so it belongs to Tasks/SqlQueryTask.cs. Recorded here so that a reader comparing
//  this file against :L556-L597 can see the omission is deliberate and knows where the rest went.
//
//  ==============================================================================================
//  WHAT THIS FILE DELIBERATELY DOES NOT CONTAIN
//  ==============================================================================================
//  NO CHANGESET LOGIC. No GetChanges, no SetChanges, no chunk loop, no chunk-size arithmetic and no
//  inter-chunk yield. The full-state path is SINGLE SHOT by construction [:L97 passes 1, 1] and the
//  two defects the changeset path owns - row loss on a sorted carrier spanning more than one chunk,
//  and the prohibition on Reset between chunks - are Buffers/ChangesetCodec.cs's to preserve.
//
//  NO JSON AND NO XML SERIALIZER (C-D). The full-state payload is an OPAQUE BINARY BLOB and the
//  published contract says so: QueryDataChunk.data is documented as opaque, its format being the
//  codecs' concern on both sides. Reaching for a document serializer would pull in the deferred
//  Documents parser family, and the temptation is real here precisely because "full state" SOUNDS
//  like a document. It is not one. See the payload-format region for the deterministic binary
//  encoding used instead, and why determinism is a parity requirement rather than a nicety.
//
//  NO DwBuffer AND NO ItemStatus DECLARATION. Both are consumed from the generated
//  PowerFramework.Contracts.Common.V1 enums, and every status predicate comes from
//  Buffers/ItemStatus.cs. Re-declaring either would create a second value domain that could drift
//  from the wire one, which AAP 0.4.5.3 forbids outright because the numeric values travel in
//  serialized payloads and characterization recordings.
//
//  NO CROSSTAB RENDERING, GEOMETRY, DPI OR FONT CONCEPT. To this codec a crosstab is a DATA SHAPE
//  and nothing more; presentation is the deferred DesignSystem's.
//
//  NO STORAGE, NO I/O AND NO WALL CLOCK (C-E, C-H). No DbConnection, no EF Core type, no
//  Microsoft.Data.Sqlite type, no file and no socket; no SQL dialect is assumed and no schema is
//  known - and note that no crosstab schema exists anywhere in this repository either, so none may be
//  invented. Nothing here reads a clock: there is no DateTime, DateTimeOffset, Stopwatch or
//  Environment.TickCount, and if a clock need ever arises it goes through the single TimeProvider
//  this service injects. In-memory MemoryStream and BinaryWriter are used to build the opaque blob;
//  those are buffer primitives, not I/O.
//
//  NO SIBLING-FOLDER DEPENDENCY. Nothing here reaches into Concurrency/, Tasks/, Grpc/, Errors/,
//  Sql/, Data/ or Transactions/. The two places the legacy calls out to its task - the error event and
//  the chunk handover - are expressed as DELEGATE SEAMS, which is what keeps this file foundational
//  and independently testable.
//
//  ==============================================================================================
//  WHY EVERY TYPE HERE IS `internal`
//  ==============================================================================================
//  Forced, not chosen. Every type this codec names in a signature - DataWindowBufferStore,
//  CarrierRow, ItemStatusMachine - is internal to this assembly, so a public member mentioning one
//  is CS0051 inconsistent accessibility, and TreatWarningsAsErrors makes that a hard build failure.
//  It is also what C-H asks for: the sibling test project reaches every seam below through the
//  csproj's InternalsVisibleTo item, so the per-service coverage gate is attainable without widening
//  the public API - which matters more here than usual, because with no oracle for this path UNIT
//  COVERAGE IS THE ONLY EVIDENCE AVAILABLE.
//
//  ==============================================================================================
//  R9 - ONE-BASED TO ZERO-BASED, AUDITED INDIVIDUALLY FOR THIS FILE
//  ==============================================================================================
//  AAP 0.4.5.4 permits an individual audit in place of a centralized helper. This is that audit, and
//  each item is commented again at its site:
//
//    (1) THE `1, 1` WIRE PAIR [:L97] IS A ONE-BASED COUNT AND POSITION, NOT AN INDEX. It says "chunk
//        1 of 1", so the first chunk is 1 and a zero-based reading is wrong by one for the whole
//        stream. SingleChunkCount and SingleChunkIndex are both 1 and neither is rebased.
//    (2) ROW NUMBERS ARE ONE-BASED throughout, matching the carrier surface this codec drives:
//        RowAt and SetItemStatus take one-based row numbers and AppendRow RETURNS one. Every loop
//        below counts from ItemStatusMachine.FirstRowNumber to the buffer's own count INCLUSIVE.
//    (3) COLUMN NUMBERS ARE ONE-BASED and are never rebased. They are written to the payload and
//        read back verbatim; ItemStatusMachine.FirstColumnNumber is the floor.
//    (4) THE COLUMN-ZERO STATUS SENTINEL IS NOT AN INDEX. It means "the row itself" and is reached
//        through ItemStatusMachine.RowStatusColumn, never by comparing a bare 0.
//    (5) NO LEGACY LOOP IS PORTED INTO THIS FILE. The full-state path has none - it is single shot -
//        so the loops below are this port's own traversals of the carrier and are bounded by the
//        carrier's own one-based counts.
//
//  ==============================================================================================
//  RULES POSITION, AND THE CONSTRAINTS THAT APPLY IN THEIR PLACE
//  ==============================================================================================
//  review_rules returns exactly one line, "No user rules provided.", so NO user-specified rule
//  governs this file, none is invented here, and the absence is not treated as licence to lower the
//  bar. The enterprise-standard baseline applies instead, and the binding constraints are the
//  migration plan's own non-rule inventory. Those bearing on this file are cited inline where each is
//  discharged: C-A, C-B, C-C, C-D, C-E, C-H, C-I, C-K and risk R9.
//
//  C-B SELF-AUDIT: neither underlying fault is fixed. The workarounds ARE the behaviour. DEFECT 3
//  keeps its gate, its trim, its two verbatim message prefixes and its E_INVALID_ARGUMENT result,
//  including the asymmetry whereby the APPLIED value is trimmed but the REPORTED value is not.
//  DEFECT 4 keeps its guard, so the modify call happens only when the property does not already read
//  "yes". The empty-means-success convention of the modify result is preserved and never inverted.
//
//  C-F / C-G SELF-AUDIT: no key, credential, token, password, connection string or secret-shaped
//  literal appears anywhere in this file. There is no logger here and no statement text is handled,
//  so there is nothing to redact; redaction is Errors/SqlRedactor.cs's job.
//
//  C-I SELF-AUDIT: this file adds no PackageReference. The only non-BCL names it binds are the
//  published contracts and one aliased type from PowerFramework.Shared.Kernel, both of which arrive
//  through ProjectReference items that already exist.
// ==============================================================================================

using System.Globalization;
using System.Text;

using Google.Protobuf;

using PowerFramework.Contracts.Common.V1;
using PowerFramework.Contracts.Persistence.V1;

// COLLISION RESOLUTION (AAP 0.4.5.1). PowerBuilder resolved one flat global namespace by library-list
// order, so this port has no import statements to rewrite - it has namespaces to CREATE, plus the
// collisions a flat namespace tolerated to resolve. This is one of them, and the sibling
// Buffers/DataWindowBuffers.cs resolves it identically: PowerFramework.Contracts.Common.V1 declares a
// protobuf MESSAGE named RetCode while PowerFramework.Shared.Kernel declares the STATIC CLASS of
// return-code constants, also named RetCode. Importing both namespaces would make every unqualified
// mention CS0104 ambiguous, so the one kernel type this file needs is brought in by alias instead. An
// alias declared here is resolved ahead of the namespace imports at the same scope, which is what
// makes it a fix rather than another candidate for the ambiguity.
using RetCode = PowerFramework.Shared.Kernel.RetCode;

namespace PowerFramework.Persistence.Buffers;

#region The two call-outs to the owning task, expressed as delegate seams

/// <summary>
/// Reports a framework return code and its accompanying text, reproducing the legacy
/// <c>Event OnError(code, text)</c> call-out.
/// </summary>
/// <param name="retCode">
/// The return code, drawn from <see cref="RetCode"/>. The full-state path raises exactly two:
/// <see cref="RetCode.E_INTERNAL_ERROR"/> when the chunk handover fails
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L98</c>] and
/// <see cref="RetCode.E_INVALID_ARGUMENT"/> when a sort or filter expression is rejected
/// [<c>:L566, L572</c>].
/// </param>
/// <param name="message">
/// The text verbatim. It is OBSERVABLE - it reaches log records and characterization recordings - so
/// callers must not reformat, localize or wrap it.
/// </param>
/// <remarks>
/// <para>
/// A DELEGATE RATHER THAN A DEPENDENCY, AND THAT IS THE POINT. In the legacy the error event belongs
/// to the SQL task rather than to the carrier, so binding a task type here would couple this codec to
/// <c>Tasks/</c> and make it untestable in isolation - the one thing C-H cannot afford on a path with
/// no behavioural oracle. The seam is a delegate for the same reason the carrier's database-error hook
/// is a callback over raw scalars rather than a reference to <c>Errors/DbErrorData.cs</c>.
/// </para>
/// </remarks>
internal delegate void FullStateErrorReporter(long retCode, string message);

/// <summary>
/// Hands one serialized chunk over to the calling-thread proxy, reproducing
/// <c>tasking.Event OnDataChunk(ref blbData, count, current, fullState)</c>.
/// </summary>
/// <param name="chunk">
/// The chunk to hand over. On this path there is always exactly one, carrying
/// <see cref="FullStateCodec.SingleChunkCount"/>, <see cref="FullStateCodec.SingleChunkIndex"/> and
/// <see cref="QueryDataChunk.FullState"/> set to <see langword="true"/>.
/// </param>
/// <returns>
/// The handover result. A value BELOW ZERO means failure, which is the exact test the legacy applies:
/// <c>if tasking.Event OnDataChunk(ref blbData,1,1,true) &lt; 0 then</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L97</c>]. Zero and one are both
/// success, so the test is a sign test and NOT a comparison against
/// <see cref="DataWindowBufferStore.DataStoreSuccess"/>.
/// </returns>
/// <remarks>
/// The parameter is the published <see cref="QueryDataChunk"/> rather than four loose arguments,
/// because that message IS the wire shape of this call: its <c>data</c>, <c>chunk_count</c>,
/// <c>chunk_index</c> and <c>full_state</c> fields are documented in
/// <c>shared/PowerFramework.Contracts/Proto/persistence.v1.proto</c> as the four legacy arguments,
/// field for field. Inventing a parallel argument list would create a second place for the pairing to
/// drift.
/// </remarks>
internal delegate long FullStateChunkHandover(QueryDataChunk chunk);

#endregion

#region The narrow carrier surface the full-state path needs, and why it is declared here

/// <summary>
/// The four DataWindow-definition operations the full-state transfer path invokes on a result carrier:
/// reading a property, writing a property, and setting the sort and filter expressions.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS INTERFACE EXISTS RATHER THAN FOUR MORE MEMBERS ON THE CARRIER.
/// <see cref="DataWindowBufferStore"/> deliberately publishes only the member set the migration
/// measured across the seven legacy sources it was ported from, and arbitrary
/// <c>Describe</c>/<c>Modify</c> of a DataWindow property is not in that set. Rather than widen a
/// sibling file, this codec declares the NARROWEST contract it actually needs - four members, no more
/// - and the owning task supplies an implementation. That keeps the boundary honest and keeps this
/// codec exercisable against a hand-written double with no carrier, no database and no container.
/// </para>
/// <para>
/// EVERY MEMBER MAPS TO A MEASURED LEGACY CALL. <see cref="Describe"/> and <see cref="Modify"/> are
/// the pair at <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L674-L675</c>;
/// <see cref="SetSort"/> and <see cref="SetFilter"/> are the pair at <c>:L565</c> and <c>:L571</c>.
/// Nothing is added for symmetry.
/// </para>
/// <para>
/// THE TWO RETURN CONVENTIONS ARE DIFFERENT AND MUST NOT BE UNIFIED (C-B).
/// <see cref="Modify"/> answers an ERROR STRING in which EMPTY MEANS SUCCESS - the legacy tests
/// <c>if sError &lt;&gt; "" then</c> at <c>:L665</c> - while <see cref="SetSort"/> and
/// <see cref="SetFilter"/> answer the numeric DataStore convention in which
/// <see cref="DataWindowBufferStore.DataStoreSuccess"/> means success, tested as
/// <c>&lt;&gt; 1</c> at <c>:L565</c> and <c>:L571</c>. Collapsing either into a boolean would discard
/// the diagnostic text in the first case and confuse two coexisting conventions in the second.
/// </para>
/// </remarks>
internal interface IFullStateCarrierSurface
{
    /// <summary>
    /// Reads one DataWindow property, reproducing <c>Describe(property)</c>.
    /// </summary>
    /// <param name="property">
    /// The property expression, for example <see cref="FullStateCodec.NoUserPromptProperty"/>.
    /// </param>
    /// <returns>
    /// The property value verbatim, INCLUDING PowerBuilder's own error markers - <c>"!"</c> when the
    /// property cannot be read and <c>"?"</c> when its value is inconsistent across a selection. Those
    /// markers are values, not exceptions, and the framework screens for them itself
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L562, L564</c>], so an
    /// implementation must pass them through rather than translate them.
    /// </returns>
    string Describe(string property);

    /// <summary>
    /// Writes one or more DataWindow properties, reproducing <c>Modify(modifyString)</c>.
    /// </summary>
    /// <param name="modifyString">
    /// The modify expression, for example <see cref="FullStateCodec.NoUserPromptModifyString"/>.
    /// </param>
    /// <returns>
    /// The error text, or <see cref="string.Empty"/> ON SUCCESS. EMPTY MEANS SUCCESS - this is not
    /// inverted anywhere in this port, because the legacy tests it that way at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L665</c> and the text it
    /// carries on failure is the whole of the diagnostic.
    /// </returns>
    string Modify(string modifyString);

    /// <summary>
    /// Applies a sort expression, reproducing <c>SetSort(sort)</c> as called at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L565</c>.
    /// </summary>
    /// <param name="sort">The sort expression, already trimmed by the caller.</param>
    /// <returns>
    /// <see cref="DataWindowBufferStore.DataStoreSuccess"/> on success. The legacy tests
    /// <c>&lt;&gt; 1</c>, so ANY other value is failure - including
    /// <see cref="DataWindowBufferStore.DataStoreFailure"/> and including zero.
    /// </returns>
    long SetSort(string sort);

    /// <summary>
    /// Applies a filter expression, reproducing <c>SetFilter(filter)</c> as called at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L571</c>.
    /// </summary>
    /// <param name="filter">The filter expression, already trimmed by the caller.</param>
    /// <returns>
    /// <see cref="DataWindowBufferStore.DataStoreSuccess"/> on success; any other value is failure,
    /// matching the <c>&lt;&gt; 1</c> test at <c>:L571</c>.
    /// </returns>
    long SetFilter(string filter);
}

#endregion

#region The DEFECT 3 gate, reproduced as an explicit value rather than an ambient condition

/// <summary>
/// The two thread-and-task facts that decide whether sort and filter synchronization is dispatched by
/// processing kind at all.
/// </summary>
/// <param name="IsMainThread">
/// The answer <c>#ParentThread.of_IsMainThread()</c> would give
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L559</c>].
/// </param>
/// <param name="TaskNeedsCreate">
/// The answer <c>tasking._of_NeedCreate()</c> would give [same line] - whether the calling-thread proxy
/// still has to create its own data object from the transferred syntax.
/// </param>
/// <remarks>
/// <para>
/// Reproduces the guard the whole decision hangs on
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L559</c>]:
/// </para>
/// <code>
/// if Not #ParentThread.of_IsMainThread() and Not tasking._of_NeedCreate() then
///     choose case Long(data.Describe("DataWindow.Processing"))
///         case 4,5    // synchronize   -- THIS FILE
///         case else   // clear         -- Buffers/ChangesetCodec.cs
/// </code>
/// <para>
/// C-B - THE GATE IS NOT OPTIONAL AND SYNCHRONIZING UNCONDITIONALLY WOULD BE A BEHAVIOUR CHANGE. When
/// the gate is closed the legacy takes an ELSE-BRANCH at <c>:L582-L597</c> which applies sort and
/// filter under an entirely different condition -
/// <c>(of_IsMainThread() and (HasReceiver() or _bCache)) or _of_NeedCreate()</c> - and which does NOT
/// dispatch on the processing kind. Being processing-agnostic, that branch is shared task behaviour and
/// belongs to <c>Tasks/SqlQueryTask.cs</c>, not to either codec; <see cref="FullStateCodec"/>
/// consequently reports the gate as closed and applies nothing, rather than guessing at the other
/// branch's condition.
/// </para>
/// <para>
/// A VALUE OBJECT RATHER THAN TWO LOOSE BOOLEANS, because two adjacent booleans of the same type at a
/// call site are transposable without a compiler complaint, and transposing these two inverts the gate
/// on exactly the paths where it matters.
/// </para>
/// </remarks>
internal readonly record struct FullStateTransferGate(bool IsMainThread, bool TaskNeedsCreate)
{
    /// <summary>
    /// Whether sort and filter synchronization is dispatched by processing kind.
    /// </summary>
    /// <value>
    /// <see langword="true"/> only when the caller is NOT on the main thread and the task does NOT
    /// need to create its data object - the double negative of <c>:L559</c>, preserved as written.
    /// </value>
    internal bool SynchronizesByProcessingKind => !IsMainThread && !TaskNeedsCreate;
}

#endregion

#region The three receive targets, and the outcome of the DEFECT 4 workaround

/// <summary>
/// Which of the three things the legacy applies a full-state payload to.
/// </summary>
/// <remarks>
/// <para>
/// Reproduces the receiver dispatch at
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L184-L243</c>:
/// <c>IsValid(_receiver)</c> selects between a receiver and the task's own carrier, and
/// <c>_receiver.TypeOf() = DataWindow!</c> then splits the receiver case in two.
/// </para>
/// <para>
/// THE KIND IS CARRIED EVEN THOUGH THE FULL-STATE OPERATION IS IDENTICAL IN ALL THREE, and that is
/// deliberate. It is identical here [<c>:L190, L216, L233</c>] but NOT on the changeset path, where the
/// DataWindow arm additionally suspends redraw and recalculates groups [<c>:L193-L194, L200, L203</c>]
/// and the DataStore arm does neither [<c>:L218-L224</c>]. Preserving the discriminator keeps that
/// documented asymmetry legible - and keeps the presentational non-port visible as a non-port - instead
/// of erasing the distinction the legacy draws.
/// </para>
/// </remarks>
internal enum FullStateTargetKind
{
    /// <summary>
    /// A DataWindow receiver - the <c>_receiver.TypeOf() = DataWindow!</c> arm
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L185</c>].
    /// </summary>
    DataWindowReceiver,

    /// <summary>
    /// A DataStore receiver - the else arm of the receiver-kind test [<c>:L211-L213</c>].
    /// </summary>
    DataStoreReceiver,

    /// <summary>
    /// No receiver is installed, so the payload lands on the task's own carrier [<c>:L231-L233</c>].
    /// </summary>
    TaskCarrier,
}

/// <summary>
/// The resolved destination of a full-state payload: the store it is applied to, and which of the
/// legacy's three receiver shapes that store is.
/// </summary>
/// <param name="Store">The buffer store the payload is applied to. Never <see langword="null"/>.</param>
/// <param name="Kind">Which of the three legacy shapes <paramref name="Store"/> represents.</param>
/// <remarks>
/// Resolved by <see cref="FullStateCodec.ResolveTarget"/>, which reproduces the dispatch verbatim. The
/// pair travels together because the store alone cannot say which arm produced it, and the kind alone
/// cannot be applied to.
/// </remarks>
internal readonly record struct FullStateTarget(DataWindowBufferStore Store, FullStateTargetKind Kind);

/// <summary>
/// What the guarded no-user-prompt write actually did - DEFECT 4's observable result.
/// </summary>
/// <param name="PropertyAlreadySet">
/// Whether the property already read <see cref="FullStateCodec.NoUserPromptEnabledValue"/>, in which
/// case the legacy performs NO write at all
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L674</c>].
/// </param>
/// <param name="ModifyAttempted">
/// Whether the modify call was issued. Exactly the negation of <paramref name="PropertyAlreadySet"/>,
/// carried explicitly because THE PRESENCE OR ABSENCE OF THE CALL IS THE OBSERVABLE BEHAVIOUR and a
/// test has to be able to assert on it directly.
/// </param>
/// <param name="ModifyError">
/// The modify result verbatim, preserving the legacy convention in which
/// <see cref="string.Empty"/> MEANS SUCCESS [<c>:L665</c> tests <c>if sError &lt;&gt; "" then</c>]. It is
/// also empty when no write was attempted, because a call that did not happen produced no error.
/// </param>
/// <remarks>
/// C-B - WHY THE GUARD IS CARRIED AS RESULT DATA RATHER THAN COLLAPSED AWAY. An unconditional write
/// would emit a modify call the legacy would not have made, and the modify call is OBSERVABLE. The
/// guard is therefore part of the behaviour rather than an optimisation of it, and this type is what
/// lets that be asserted instead of assumed.
/// </remarks>
internal readonly record struct NoUserPromptOutcome(
    bool PropertyAlreadySet,
    bool ModifyAttempted,
    string ModifyError)
{
    /// <summary>
    /// Whether the workaround left the carrier in the intended state.
    /// </summary>
    /// <value>
    /// <see langword="true"/> when <see cref="ModifyError"/> is empty - which covers both a successful
    /// write and the no-write case. EMPTY MEANS SUCCESS, never inverted.
    /// </value>
    internal bool Succeeded => ModifyError.Length == 0;
}

#endregion

#region The opaque payload's value tags

/// <summary>
/// The type tag that precedes every value inside a full-state payload.
/// </summary>
/// <remarks>
/// <para>
/// A carrier column value is <c>object?</c>, because the legacy column type is PowerBuilder's
/// <c>any</c> and AAP 0.4.5.2 maps <c>any</c> to <c>object?</c>. An opaque binary payload therefore
/// needs a discriminator, and this is it. The member set is exactly the AAP 0.4.5.2 type mapping plus
/// the two integral widths PowerBuilder distinguishes, so nothing here is speculative:
/// </para>
/// <list type="bullet">
///   <item><description><c>blob</c> to <c>byte[]</c> - see <see cref="Bytes"/>.</description></item>
///   <item><description><c>integer</c> and <c>long</c> to <c>int</c> and <c>long</c> - see
///     <see cref="Int32"/> and <see cref="Int64"/>.</description></item>
///   <item><description><c>unsignedlong</c> to <c>ulong</c> - see <see cref="UInt64"/>.</description></item>
///   <item><description><c>dec</c> and <c>decimal(n)</c> to <c>decimal</c> - see
///     <see cref="Decimal"/>.</description></item>
///   <item><description><c>datetime</c>, <c>date</c> and <c>time</c> to <c>DateTime</c>,
///     <c>DateOnly</c> and <c>TimeOnly</c> - see <see cref="DateTime"/>, <see cref="DateOnly"/> and
///     <see cref="TimeOnly"/>.</description></item>
/// </list>
/// <para>
/// THE VALUES ARE FROZEN. They are written into the payload byte stream, so renumbering a member
/// silently changes how every previously written payload decodes - the same hazard AAP 0.4.5.3 records
/// for the preserved constant identifiers, and the reason the wire enums carry no synthetic sentinel.
/// A new tag is therefore only ever APPENDED, never inserted.
/// </para>
/// <para>
/// AN UNRECOGNISED CLR TYPE IS A DEFINED ERROR, NEVER A GUESS. The migration rule is that where a
/// legacy behaviour cannot be reproduced the contract is narrowed with a defined error rather than
/// widened with an approximation, so <see cref="FullStateCodec.Capture"/> raises rather than coercing
/// an unknown value to its string form - which would round-trip as the wrong type and read as correct.
/// </para>
/// </remarks>
internal enum FullStateValueTag : byte
{
    /// <summary>
    /// A null value. PowerBuilder has null for value types and the ported return-code algebra depends
    /// on it, so null is A VALUE HERE AND NOT AN ABSENCE - it must survive the round trip distinctly
    /// from zero and from the empty string.
    /// </summary>
    Null = 0,

    /// <summary>A <see cref="bool"/>.</summary>
    Boolean = 1,

    /// <summary>An <see cref="int"/> - PowerBuilder <c>integer</c>.</summary>
    Int32 = 2,

    /// <summary>A <see cref="long"/> - PowerBuilder <c>long</c>.</summary>
    Int64 = 3,

    /// <summary>A <see cref="ulong"/> - PowerBuilder <c>unsignedlong</c>.</summary>
    UInt64 = 4,

    /// <summary>A <see cref="double"/> - PowerBuilder <c>double</c> and <c>real</c>.</summary>
    Double = 5,

    /// <summary>A <see cref="decimal"/> - PowerBuilder <c>dec</c> and <c>decimal(n)</c>.</summary>
    Decimal = 6,

    /// <summary>A <see cref="string"/>.</summary>
    String = 7,

    /// <summary>A <see cref="byte"/> array - PowerBuilder <c>blob</c>.</summary>
    Bytes = 8,

    /// <summary>A <see cref="System.DateTime"/> - PowerBuilder <c>datetime</c>.</summary>
    DateTime = 9,

    /// <summary>A <see cref="System.DateOnly"/> - PowerBuilder <c>date</c>.</summary>
    DateOnly = 10,

    /// <summary>A <see cref="System.TimeOnly"/> - PowerBuilder <c>time</c>.</summary>
    TimeOnly = 11,
}

#endregion

/// <summary>
/// The <c>GetFullState</c> / <c>SetFullState</c> blob codec for the DataWindow result carrier - the
/// crosstab-and-composite arm of the legacy codec selector - together with the two documented legacy
/// defects that arm owns.
/// </summary>
/// <remarks>
/// <para>
/// Ported from <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru</c>: the send path at
/// <c>:L95-L101</c>, DEFECT 3 at <c>:L563</c> and DEFECT 4 at <c>:L673</c>; and from
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L184-L248</c> for the receive
/// path.
/// </para>
/// <para>
/// NO LEGACY FIXTURE REACHES THIS CODE. All twelve DataWindow definitions in the repository declare
/// <c>processing=1</c> or <c>processing=0</c>; none declares 4 or 5, which are the only two values that
/// select this codec [<c>:L94</c>]. Everything here is therefore verified by UNIT TESTS OVER SYNTHETIC
/// CARRIERS and by nothing else, and no characterization coverage is claimed for it. See the file
/// header for the full statement of that limitation.
/// </para>
/// <para>
/// STATIC BY DESIGN. Every operation is a pure transform over a carrier plus its two delegate seams:
/// there is no state to hold, no clock to read, no connection to open and nothing to dispose. That is
/// what makes the whole file exercisable with no database and no container, which C-H requires of every
/// line the per-service coverage gate measures.
/// </para>
/// </remarks>
internal static class FullStateCodec
{
    #region Preserved literals - observable, and therefore frozen

    /// <summary>
    /// The error text raised when the single-chunk handover fails.
    /// </summary>
    /// <remarks>
    /// VERBATIM FROM
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L98</c>:
    /// <c>Event OnError(RetCode.E_INTERNAL_ERROR,"TransData Failed")</c>. It reaches log records and
    /// characterization recordings, so it is not reworded, not localized and not given a period. Note
    /// that it is English while its neighbours on adjacent paths are Chinese - <c>无效的DataObject</c>
    /// at <c>:L554</c>, <c>设置事务对象失败!</c> at <c>:L679</c>, <c>SQL解析失败!</c> at <c>:L686</c> - and
    /// that inconsistency is legacy behaviour reproduced rather than harmonized (C-B).
    /// </remarks>
    internal const string TransDataFailedMessage = "TransData Failed";

    /// <summary>
    /// The prefix of the error text raised when a sort expression is rejected.
    /// </summary>
    /// <remarks>
    /// VERBATIM FROM
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L566</c>:
    /// <c>Event OnError(RetCode.E_INVALID_ARGUMENT,"SetSort: " + _sNewSort)</c>. THE TRAILING SPACE
    /// AFTER THE COLON IS PART OF THE LITERAL and is byte-exact here.
    /// </remarks>
    internal const string SetSortMessagePrefix = "SetSort: ";

    /// <summary>
    /// The prefix of the error text raised when a filter expression is rejected.
    /// </summary>
    /// <remarks>
    /// VERBATIM FROM
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L572</c>:
    /// <c>Event OnError(RetCode.E_INVALID_ARGUMENT,"SetFilter: " + _sNewFilter)</c>. THE TRAILING SPACE
    /// AFTER THE COLON IS PART OF THE LITERAL and is byte-exact here.
    /// </remarks>
    internal const string SetFilterMessagePrefix = "SetFilter: ";

    /// <summary>
    /// The DataWindow property DEFECT 4's workaround reads and writes.
    /// </summary>
    /// <remarks>
    /// From <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L674</c>:
    /// <c>data.Describe("DataWindow.NoUserPrompt")</c>.
    /// </remarks>
    internal const string NoUserPromptProperty = "DataWindow.NoUserPrompt";

    /// <summary>
    /// The value the property must hold for the workaround to be satisfied, and therefore the value the
    /// guard compares against.
    /// </summary>
    /// <remarks>
    /// From the guard at <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L674</c>,
    /// <c>&lt;&gt; "yes"</c>. Compared with <see cref="StringComparison.Ordinal"/>: the legacy
    /// <c>&lt;&gt;</c> on strings is a byte comparison against a machine-generated property value, so a
    /// culture-sensitive or case-insensitive comparison would accept spellings the legacy rejects and
    /// would therefore SKIP a modify call the legacy makes.
    /// </remarks>
    internal const string NoUserPromptEnabledValue = "yes";

    /// <summary>
    /// The modify expression DEFECT 4's workaround writes.
    /// </summary>
    /// <remarks>
    /// VERBATIM FROM
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L675</c>:
    /// <c>data.Modify("DataWindow.NoUserPrompt=yes")</c>. Spelled as one literal rather than composed
    /// from the two constants above, because the legacy writes one literal and the exact text - no
    /// spaces around the equals sign - is what an implementation of
    /// <see cref="IFullStateCarrierSurface.Modify"/> parses.
    /// </remarks>
    internal const string NoUserPromptModifyString = "DataWindow.NoUserPrompt=yes";

    /// <summary>
    /// The total chunk count the full-state path reports: always one.
    /// </summary>
    /// <remarks>
    /// R9 - THIS IS A ONE-BASED COUNT, NOT AN INDEX. From
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L97</c>,
    /// <c>tasking.Event OnDataChunk(ref blbData,1,1,true)</c>: the second argument is <c>count</c>. The
    /// full-state path never chunks, so this is one by construction and there is no arithmetic to
    /// rebase.
    /// </remarks>
    internal const long SingleChunkCount = 1L;

    /// <summary>
    /// The one-based position of the single chunk the full-state path emits: always one.
    /// </summary>
    /// <remarks>
    /// R9 - THIS IS A ONE-BASED POSITION, NOT AN INDEX. The third argument of the same call at
    /// <c>:L97</c> is <c>current</c>, and the published contract states that the first chunk is 1 so a
    /// zero-based reading is wrong by one for the whole stream. It is not rebased here.
    /// </remarks>
    internal const long SingleChunkIndex = 1L;

    #endregion

    #region DEFECT 3 - full state requires sort and filter synchronization [:L563]

    /// <summary>
    /// Synchronizes the task's pending sort and filter expressions onto the carrier before its state is
    /// captured - the crosstab-and-composite arm of the legacy's sort/filter decision.
    /// </summary>
    /// <param name="carrier">
    /// The carrier whose processing kind selects this arm. Only its processing kind is read here; the
    /// expressions are applied through <paramref name="surface"/>.
    /// </param>
    /// <param name="surface">The carrier's sort and filter operations.</param>
    /// <param name="pendingSort">
    /// The task's pending sort expression - the legacy <c>_sNewSort</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L32</c>]. An empty string
    /// means "nothing pending" and is skipped, exactly as <c>if _sNewSort &lt;&gt; ""</c> skips it at
    /// <c>:L564</c>. May be <see langword="null"/>, which is treated as empty because an unassigned
    /// PowerBuilder string is the empty string.
    /// </param>
    /// <param name="pendingFilter">
    /// The task's pending filter expression - the legacy <c>_sNewFilter</c> [<c>:L33</c>] - with the
    /// same empty-and-null handling as <paramref name="pendingSort"/> [<c>:L570</c>].
    /// </param>
    /// <param name="gate">
    /// The thread-and-task gate of <c>:L559</c>. When
    /// <see cref="FullStateTransferGate.SynchronizesByProcessingKind"/> is <see langword="false"/> this
    /// method applies NOTHING and answers <see cref="RetCode.OK"/>.
    /// </param>
    /// <param name="reportError">
    /// The <c>Event OnError</c> seam. Invoked once, immediately before a failure is returned.
    /// </param>
    /// <returns>
    /// <see cref="RetCode.OK"/> when everything applied or nothing needed applying;
    /// <see cref="RetCode.E_INVALID_ARGUMENT"/> when the carrier rejected an expression, matching
    /// <c>return RetCode.E_INVALID_ARGUMENT</c> at <c>:L567</c> and <c>:L573</c>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="carrier"/>, <paramref name="surface"/> or <paramref name="reportError"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="carrier"/>'s processing kind does not select the full-state path, so it belongs
    /// to <c>Buffers/ChangesetCodec.cs</c>. See the remarks.
    /// </exception>
    /// <remarks>
    /// <para>
    /// DEFECT 3, PRESERVED VERBATIM (C-B). The legacy's own note reads, at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L562-L563</c>:
    /// </para>
    /// <code>
    /// //NOTE
    /// //使用SetFullState传递数据的风格需要同步排序和过滤条件
    /// </code>
    /// <para>
    /// - "the SetFullState transfer style REQUIRES sort and filter conditions to be synchronized". The
    /// reason is mechanical rather than arbitrary: a full-state image REPLACES the receiver's entire
    /// definition, so whatever sort and filter the task means to apply must already be on the SOURCE at
    /// the moment of capture, because after the image lands the receiver's own sort and filter are gone.
    /// </para>
    /// <para>
    /// THE EXACTLY OPPOSITE ARM IS ALSO CONTRACT, AND IT IS THE SIBLING CODEC'S. At <c>:L577-L580</c>
    /// the <c>case else</c> arm notes 使用SetChanges传递数据的风格不需要排序和过滤条件，甚至会影响性能 -
    /// "the SetChanges transfer style does not need sort and filter conditions, and they can even hurt
    /// performance" - and therefore CLEARS both with <c>data.SetSort("")</c> and
    /// <c>data.SetFilter("")</c>. A changeset carries row data only and the receiver keeps its own
    /// definition, so setting them on the source changes nothing the receiver sees and merely costs a
    /// sort and a filter pass. THE TWO ARMS ARE DELIBERATE OPPOSITES; neither is an oversight, and
    /// harmonizing them would break one path or the other. This method implements the full-state arm
    /// only, and <c>Buffers/ChangesetCodec.cs</c> implements the clearing arm.
    /// </para>
    /// <para>
    /// THE APPLIED VALUE IS TRIMMED BUT THE REPORTED VALUE IS NOT, and that asymmetry is in the source:
    /// <c>data.SetSort(Trim(_sNewSort))</c> at <c>:L565</c> against
    /// <c>"SetSort: " + _sNewSort</c> at <c>:L566</c>, the second with no <c>Trim</c>. It is reproduced
    /// rather than tidied, because the reported text is observable.
    /// </para>
    /// <para>
    /// SORT IS APPLIED BEFORE FILTER and the first failure returns immediately, so a rejected sort means
    /// the filter is never attempted [<c>:L564-L575</c>].
    /// </para>
    /// </remarks>
    internal static long SynchronizeSortAndFilter(
        DataWindowBufferStore carrier,
        IFullStateCarrierSurface surface,
        string? pendingSort,
        string? pendingFilter,
        FullStateTransferGate gate,
        FullStateErrorReporter reportError)
    {
        ArgumentNullException.ThrowIfNull(carrier);
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(reportError);

        ThrowIfNotFullStateCarrier(carrier, nameof(carrier));

        // `if Not #ParentThread.of_IsMainThread() and Not tasking._of_NeedCreate() then` [:L559].
        // C-B: the gate is preserved rather than flattened. When it is closed the legacy takes the
        // processing-agnostic else-branch at :L582-L597, which belongs to Tasks/SqlQueryTask.cs; this
        // codec applies nothing rather than guessing at that branch's condition.
        if (!gate.SynchronizesByProcessingKind)
        {
            return RetCode.OK;
        }

        // `if _sNewSort <> "" then` [:L564]. An unassigned PowerBuilder string IS the empty string, so
        // null and "" are the same "nothing pending" state here.
        string sort = pendingSort ?? string.Empty;

        if (sort.Length > 0)
        {
            // `if data.SetSort(Trim(_sNewSort)) <> 1 then` [:L565]. The test is against
            // DataStoreSuccess and NOT against RetCode.OK - the two conventions coexist in the legacy
            // and each value is checked against its own.
            if (surface.SetSort(sort.Trim()) != DataWindowBufferStore.DataStoreSuccess)
            {
                // `Event OnError(RetCode.E_INVALID_ARGUMENT,"SetSort: " + _sNewSort)` [:L566]. The
                // REPORTED value is the UNTRIMMED one, unlike the applied value on the line above.
                reportError(RetCode.E_INVALID_ARGUMENT, SetSortMessagePrefix + sort);

                // `return RetCode.E_INVALID_ARGUMENT` [:L567]. Returning here is why a rejected sort
                // means the filter below is never attempted.
                return RetCode.E_INVALID_ARGUMENT;
            }
        }

        // `if _sNewFilter <> "" then` [:L570].
        string filter = pendingFilter ?? string.Empty;

        if (filter.Length > 0)
        {
            // `if data.SetFilter(Trim(_sNewFilter)) <> 1 then` [:L571].
            if (surface.SetFilter(filter.Trim()) != DataWindowBufferStore.DataStoreSuccess)
            {
                // `Event OnError(RetCode.E_INVALID_ARGUMENT,"SetFilter: " + _sNewFilter)` [:L572],
                // again reporting the UNTRIMMED value.
                reportError(RetCode.E_INVALID_ARGUMENT, SetFilterMessagePrefix + filter);

                // `return RetCode.E_INVALID_ARGUMENT` [:L573].
                return RetCode.E_INVALID_ARGUMENT;
            }
        }

        // The legacy arm falls through with no return of its own, so the enclosing function continues
        // and ultimately succeeds. RetCode.OK is that fall-through.
        return RetCode.OK;
    }

    #endregion

    #region DEFECT 4 - full state crashes when a crosstab has too many columns [:L673]

    /// <summary>
    /// Applies the no-user-prompt workaround that keeps a wide crosstab from crashing the full-state
    /// capture: reads the property and writes it ONLY when it does not already read <c>"yes"</c>.
    /// </summary>
    /// <param name="surface">The carrier's property operations.</param>
    /// <returns>
    /// What actually happened - whether the property was already set, whether the modify call was
    /// issued, and the modify result with <see cref="string.Empty"/> MEANING SUCCESS.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// DEFECT 4, PRESERVED VERBATIM (C-B). The legacy's own comment reads, at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L672-L673</c>:
    /// </para>
    /// <code>
    /// //FIXME
    /// //解决交叉表字段过多时可能出现SetFullState崩溃问题
    /// </code>
    /// <para>
    /// - "works around a possible SetFullState CRASH when a crosstab has TOO MANY COLUMNS", followed at
    /// <c>:L674-L676</c> by the workaround itself:
    /// </para>
    /// <code>
    /// if data.Describe("DataWindow.NoUserPrompt") &lt;&gt; "yes" then
    ///     data.Modify("DataWindow.NoUserPrompt=yes")
    /// end if
    /// </code>
    /// <para>
    /// THE UNDERLYING CRASH IS NOT FIXED HERE AND MUST NOT BE. It is a defect of the PowerBuilder
    /// runtime's own full-state serialization, reachable only through the closed native runtime, and
    /// the framework's response to it is this suppression of interactive prompts. THE WORKAROUND IS THE
    /// BEHAVIOUR: it is what the legacy does, so it is what this port does, and it is annotated here so
    /// a reader cannot mistake it for an implementation choice of this migration.
    /// </para>
    /// <para>
    /// THE GUARD IS PART OF THE OBSERVED BEHAVIOUR AND IS NOT COLLAPSED INTO AN UNCONDITIONAL WRITE.
    /// The modify call is observable - an implementation of <see cref="IFullStateCarrierSurface.Modify"/>
    /// may log it, may fail it, and certainly counts it - so writing unconditionally would emit a call
    /// the legacy would not have made. <see cref="NoUserPromptOutcome.ModifyAttempted"/> exists so that
    /// this is assertable rather than assumed.
    /// </para>
    /// <para>
    /// POSITION IS CONTRACT. In the legacy this runs AFTER the drop-down-child modify block
    /// [<c>:L645-L670</c>] and BEFORE the transaction object is attached [<c>:L678</c>] and before the
    /// SQL statement is modified [<c>:L683</c> onward] - and, decisively, before anything retrieves or
    /// captures. A workaround applied after the crashing operation would work around nothing, so callers
    /// must invoke this during task setup and never after <see cref="Capture"/>.
    /// </para>
    /// <para>
    /// THE RESULT IS AN ERROR STRING, NOT A BOOLEAN, AND EMPTY MEANS SUCCESS. That is the legacy
    /// <c>Modify</c> convention, tested as <c>if sError &lt;&gt; "" then</c> at <c>:L665</c>. Inverting
    /// it into a boolean would discard the diagnostic text, which is the whole of what a failed modify
    /// reports. Note that the legacy IGNORES this particular result - <c>:L675</c> discards it, unlike
    /// <c>:L664</c> which captures it - so a caller that treats a failure here as fatal would be
    /// STRICTER THAN THE LEGACY; the result is surfaced for observability, and acting on it is the
    /// caller's decision to make deliberately.
    /// </para>
    /// </remarks>
    internal static NoUserPromptOutcome ApplyNoUserPromptWorkaround(IFullStateCarrierSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        // `if data.Describe("DataWindow.NoUserPrompt") <> "yes" then` [:L674]. THE READ COMES FIRST.
        // Describe may answer "!" or "?" - PowerBuilder's unreadable and inconsistent markers - and
        // neither equals "yes", so both fall into the write arm exactly as the legacy's <> does.
        string current = surface.Describe(NoUserPromptProperty);

        bool alreadySet = string.Equals(current, NoUserPromptEnabledValue, StringComparison.Ordinal);

        if (alreadySet)
        {
            // No write. The legacy issues no modify call on this branch, so neither does this port, and
            // there is no error because there was no call.
            return new NoUserPromptOutcome(
                PropertyAlreadySet: true,
                ModifyAttempted: false,
                ModifyError: string.Empty);
        }

        // `data.Modify("DataWindow.NoUserPrompt=yes")` [:L675].
        string modifyError = surface.Modify(NoUserPromptModifyString) ?? string.Empty;

        return new NoUserPromptOutcome(
            PropertyAlreadySet: false,
            ModifyAttempted: true,
            ModifyError: modifyError);
    }

    #endregion

    #region The send path - single shot, no chunking [:L95-L101]

    /// <summary>
    /// Captures the carrier's complete state, clears the source, and hands the image over as the single
    /// chunk this path emits.
    /// </summary>
    /// <param name="source">
    /// The carrier holding the retrieved result. Its processing kind must select the full-state path.
    /// </param>
    /// <param name="handover">
    /// The <c>tasking.Event OnDataChunk</c> seam. A result BELOW ZERO is failure.
    /// </param>
    /// <param name="reportError">The <c>Event OnError</c> seam.</param>
    /// <returns>
    /// <see cref="RetCode.OK"/> on success; <see cref="RetCode.E_INTERNAL_ERROR"/> when the handover
    /// failed, matching <c>return RetCode.E_INTERNAL_ERROR</c> at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L99</c>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="source"/>, <paramref name="handover"/> or <paramref name="reportError"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="source"/>'s processing kind lands in the legacy's <c>case else</c> arm, so it
    /// belongs to <c>Buffers/ChangesetCodec.cs</c>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Reproduces the whole of the <c>case 4,5</c> arm at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L94-L101</c>:
    /// </para>
    /// <code>
    /// case 4,5 //Crosstab or Composite datawindow
    ///     Data.GetFullState(ref blbData)
    ///     Data.Reset()
    ///     if tasking.Event OnDataChunk(ref blbData,1,1,true) &lt; 0 then
    ///         Event OnError(RetCode.E_INTERNAL_ERROR,"TransData Failed")
    ///         return RetCode.E_INTERNAL_ERROR
    ///     end if
    ///     blbData = Blob("")
    /// </code>
    /// <para>
    /// SINGLE SHOT. Exactly ONE chunk is emitted, carrying <see cref="SingleChunkCount"/>,
    /// <see cref="SingleChunkIndex"/> and <see cref="QueryDataChunk.FullState"/> = <see langword="true"/>.
    /// There is NO chunk loop, NO chunk-size arithmetic and NO inter-chunk yield on this path - all
    /// three belong to the changeset codec, along with the chunk-count formula
    /// <c>ceiling((rowCount + filteredCount) / chunkSize)</c>.
    /// </para>
    /// <para>
    /// THE FULL-STATE FLAG IS THE RECEIVE-SIDE SELECTOR, NOT A HINT. The receiver cannot decode the blob
    /// without it [<c>n_cst_threading_task_sqlquery.sru:L189, L215, L232</c>], so setting it wrongly
    /// routes the payload to <c>Buffers/ChangesetCodec.cs</c> and corrupts the result rather than
    /// failing cleanly.
    /// </para>
    /// <para>
    /// RESETTING THE SOURCE HERE IS LEGAL AND REQUIRED, and it is the exact operation a preserved defect
    /// FORBIDS in the sibling codec. The legacy's <c>不能使用Reset来清除数据，会使SetChanges应用不到行</c>
    /// [<c>:L176-L177</c>] - "Reset must not be used to clear data, it makes SetChanges fail to apply
    /// rows" - governs a MID-LOOP reset between changeset chunks. Here the reset happens AFTER the image
    /// has been captured and there is no loop for it to interfere with. Same operation, opposite
    /// rulings, decided purely by position relative to capture.
    /// </para>
    /// <para>
    /// THE PAYLOAD REFERENCE IS DROPPED AFTER THE HANDOVER, reproducing <c>blbData = Blob("")</c> at
    /// <c>:L101</c>. Its intent is hazard 2 of <c>docs/PB多线程绕坑提示.md</c> - the sender releases what
    /// it has handed over - and the managed equivalent is dropping the reference so the payload is not
    /// reachable from this frame afterwards.
    /// </para>
    /// </remarks>
    internal static long Send(
        DataWindowBufferStore source,
        FullStateChunkHandover handover,
        FullStateErrorReporter reportError)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(handover);
        ArgumentNullException.ThrowIfNull(reportError);

        // `choose case Long(Data.Describe("DataWindow.Processing")) / case 4,5` [:L93-L94], read from the
        // carrier as a TYPED property rather than re-parsed from a describe string, so this codec and
        // its sibling cannot disagree about which arm owns a carrier.
        ThrowIfNotFullStateCarrier(source, nameof(source));

        // `Data.GetFullState(ref blbData)` [:L95]. Returned normally rather than through a ref
        // parameter; see the file header for why that hazard workaround has no .NET analogue.
        byte[] payload = Capture(source);

        // `Data.Reset()` [:L96]. AFTER the capture - see the remarks on why this is legal here and
        // forbidden mid-loop in the changeset codec.
        source.Reset();

        // `if tasking.Event OnDataChunk(ref blbData,1,1,true) < 0 then` [:L97].
        // R9: SingleChunkCount and SingleChunkIndex are a one-based count and position, not indices.
        QueryDataChunk chunk = new()
        {
            Data = ByteString.CopyFrom(payload),
            ChunkCount = SingleChunkCount,
            ChunkIndex = SingleChunkIndex,
            FullState = true,
        };

        long handoverResult = handover(chunk);

        // A SIGN TEST, exactly as the legacy writes it. Zero and one are both success, so this is not a
        // comparison against DataStoreSuccess.
        if (handoverResult < 0L)
        {
            // `Event OnError(RetCode.E_INTERNAL_ERROR,"TransData Failed")` [:L98].
            reportError(RetCode.E_INTERNAL_ERROR, TransDataFailedMessage);

            // `return RetCode.E_INTERNAL_ERROR` [:L99]. Note the legacy does NOT clear the blob on this
            // branch - the clear at :L101 is after the `end if` - and neither does this port.
            return RetCode.E_INTERNAL_ERROR;
        }

        // `blbData = Blob("")` [:L101]. The ownership drop of hazard 2 of docs/PB多线程绕坑提示.md: the
        // sender releases what it has handed over. ByteString.CopyFrom above took a copy, so the chunk
        // is unaffected and this frame no longer reaches the captured image. It is reproduced because
        // the legacy does it rather than because the runtime needs it - a garbage-collected heap makes
        // the release itself unobservable, which is precisely why omitting it silently would leave a
        // reader unable to tell whether the line had been considered.
        payload = [];

        return RetCode.OK;
    }

    #endregion

    #region The receive path - three targets, one shot, no reset, no bookkeeping [:L184-L248]

    /// <summary>
    /// Resolves which of the legacy's three destinations a full-state payload is applied to.
    /// </summary>
    /// <param name="receiver">
    /// The installed receiver, or <see langword="null"/> when none is installed - the managed reading of
    /// <c>IsValid(_receiver)</c> at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L184</c>.
    /// </param>
    /// <param name="receiverIsDataWindow">
    /// The answer <c>_receiver.TypeOf() = DataWindow!</c> would give [<c>:L185</c>]. Read ONLY when
    /// <paramref name="receiver"/> is not <see langword="null"/>, exactly as the legacy reads it only
    /// inside the valid-receiver arm.
    /// </param>
    /// <param name="taskCarrier">
    /// The task's own carrier, used when no receiver is installed [<c>:L233</c>]. Required even when a
    /// receiver IS installed, because a caller must not have to decide whether to pass it.
    /// </param>
    /// <returns>The store to apply to, paired with which legacy shape it represents.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="taskCarrier"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Reproduces the nested dispatch at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L184-L231</c>. THE
    /// FULL-STATE OPERATION IS IDENTICAL IN ALL THREE ARMS [<c>:L190, L216, L233</c>] - which is why
    /// <see cref="Apply"/> takes a store and not a kind - but the kind is still carried, because on the
    /// CHANGESET path the three arms differ and the difference is the evidence for a documented
    /// non-port. See <see cref="FullStateTargetKind"/>.
    /// </para>
    /// </remarks>
    internal static FullStateTarget ResolveTarget(
        DataWindowBufferStore? receiver,
        bool receiverIsDataWindow,
        DataWindowBufferStore taskCarrier)
    {
        ArgumentNullException.ThrowIfNull(taskCarrier);

        // `if IsValid(_receiver) then` [:L184].
        if (receiver is not null)
        {
            // `if _receiver.TypeOf() = DataWindow! then ... else ... end if` [:L185, L211].
            return new FullStateTarget(
                receiver,
                receiverIsDataWindow
                    ? FullStateTargetKind.DataWindowReceiver
                    : FullStateTargetKind.DataStoreReceiver);
        }

        // `else` [:L231] - no receiver, so the payload lands on the task's own carrier [:L233].
        return new FullStateTarget(taskCarrier, FullStateTargetKind.TaskCarrier);
    }

    /// <summary>
    /// Applies a received full-state chunk to its resolved target, reproducing the <c>fullState</c> arm
    /// of the legacy chunk handler in full - including the empty-payload clear and the normalization
    /// that maps any code above one down to one.
    /// </summary>
    /// <param name="target">The resolved destination, from <see cref="ResolveTarget"/>.</param>
    /// <param name="payload">
    /// The serialized image. An EMPTY payload means "clear the target", which is a distinct arm rather
    /// than a degenerate apply.
    /// </param>
    /// <returns>
    /// <see cref="DataWindowBufferStore.DataStoreSuccess"/> when the image was applied or the target was
    /// cleared; <see cref="DataWindowBufferStore.DataStoreFailure"/> when the payload could not be
    /// decoded, which is what <c>SetFullState</c> answers on a payload it rejects.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="payload"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Reproduces
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L188-L248</c>, whose
    /// full-state arm is three lines long in each of its three branches:
    /// </para>
    /// <code>
    /// if Len(blbData) &gt; 0 then
    ///     if fullState then
    ///         rtCode = dw.SetFullState(blbData)      // [:L190], and :L216 / :L233 for the other two
    ///     else
    ///         ... the changeset arm, with its resets and its chunk bookkeeping ...
    ///     end if
    /// else
    ///     rtCode = 1
    ///     dw.Reset()                                 // [:L208-L209], shared with the changeset codec
    /// end if
    /// ...
    /// if fullState then
    ///     if rtCode &gt; 1 then rtCode = 1             // [:L247-L248]
    /// else
    ///     if rtCode &gt; 1 then rtCode = -1            // [:L250], THE OPPOSITE SIGN
    /// end if
    /// </code>
    /// <para>
    /// NO RESET PRECEDES THE APPLY, AND NO CHUNK BOOKKEEPING HAPPENS AT ALL. This is the sharpest
    /// observable difference from the changeset path, which resets when <c>current = 1</c> [<c>:L192-L196,
    /// L218-L220, L235-L237</c>] and calls the update-reset when <c>count = current</c> [<c>:L198-L199,
    /// L222-L223, L239-L240</c>]. NEITHER STEP IS ADDED HERE. The consequence is behavioural rather than
    /// cosmetic: because no update-reset runs, THE ITEM STATUSES CARRIED IN THE IMAGE SURVIVE VERBATIM,
    /// whereas a changeset's rows are re-baselined to unmodified once its last chunk lands.
    /// </para>
    /// <para>
    /// THE IMAGE REPLACES THE TARGET'S CONTENTS, AND THAT IS NOT THE FORBIDDEN RESET. Restoring a
    /// complete image is by definition a replacement, which is exactly WHY the legacy needs no preceding
    /// <c>Reset</c> on this arm - the clearing is intrinsic to the operation rather than a separate step
    /// gated on a chunk index. <see cref="Apply"/> therefore clears the three buffers as the first act of
    /// the restore and is emphatically NOT followed by an update-reset.
    /// </para>
    /// <para>
    /// THE NORMALIZATION AT <c>:L247-L248</c> IS SIGN-OPPOSITE TO THE CHANGESET ARM'S AND IS EASY TO GET
    /// BACKWARDS: on the full-state path a code above one becomes SUCCESS (1), while on the changeset
    /// path it becomes FAILURE (-1). Reproduced exactly.
    /// </para>
    /// <para>
    /// C-D - THE PRESENTATIONAL CALLS ARE ABSENT AND THAT IS DELIBERATE. <c>SetRedraw</c> and
    /// <c>GroupCalc</c> appear on the changeset path's DataWindow arm [<c>:L193-L194, L200, L203</c>] and
    /// in neither its DataStore arm [<c>:L218-L224</c>] nor either full-state arm. Two independent
    /// sightings of that asymmetry are the evidence that both belong to the deferred DesignSystem
    /// capability area, so they are a documented non-port rather than an omission.
    /// </para>
    /// </remarks>
    internal static long Receive(FullStateTarget target, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        DataWindowBufferStore store = target.Store
            ?? throw new ArgumentException(
                "The target carries no store. Resolve it with ResolveTarget rather than constructing "
                    + "a FullStateTarget with a default store, which the legacy dispatch has no arm "
                    + "for.",
                nameof(target));

        long resultCode;

        // `if Len(blbData) > 0 then` [:L188].
        if (payload.Length > 0)
        {
            // `rtCode = dw.SetFullState(blbData)` [:L190] - and the identical line at :L216 and :L233.
            // One shot. No preceding reset, no chunk index, no chunk count.
            resultCode = Apply(store, payload);
        }
        else
        {
            // The EMPTY-PAYLOAD CLEAR ARM [:L207-L209, L226-L228]. It sets the result to 1 FIRST and then
            // resets, so the result does not depend on what Reset answers. THIS ARM IS SHARED WITH
            // Buffers/ChangesetCodec.cs - the legacy reaches it before testing `fullState` at all - so it
            // is reproduced here for the full-state path without any claim to exclusivity over it.
            resultCode = DataWindowBufferStore.DataStoreSuccess;
            store.Reset();
        }

        // `if fullState then if rtCode > 1 then rtCode = 1` [:L247-L248].
        return NormalizeResult(resultCode);
    }

    /// <summary>
    /// Normalizes an apply result the way the full-state arm normalizes it: any code ABOVE one becomes
    /// <see cref="DataWindowBufferStore.DataStoreSuccess"/>.
    /// </summary>
    /// <param name="resultCode">The raw result of the apply or of the empty-payload clear.</param>
    /// <returns>
    /// <paramref name="resultCode"/> unchanged when it is one or below; otherwise
    /// <see cref="DataWindowBufferStore.DataStoreSuccess"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Reproduces <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L247-L251</c>:
    /// </para>
    /// <code>
    /// if fullState then
    ///     if rtCode &gt; 1 then rtCode = 1      // [:L248]  -> SUCCESS
    /// else
    ///     if rtCode &gt; 1 then rtCode = -1     // [:L250]  -> FAILURE
    /// end if
    /// </code>
    /// <para>
    /// THE TWO ARMS TAKE THE SAME INPUT TO OPPOSITE SIGNS, and this is the single easiest thing in the
    /// file to get backwards: on the full-state path an above-one code is coerced to success, while on
    /// the changeset path the identical code is coerced to failure. A NEGATIVE CODE IS LEFT ALONE by
    /// both, so a failure is never converted into a success. It exists as its own member so that the
    /// rule can be asserted directly rather than only through whatever <see cref="Apply"/> happens to
    /// return today - which matters because the legacy value being normalized comes from the
    /// PowerBuilder runtime's own <c>SetFullState</c> and this port cannot manufacture every code that
    /// runtime could answer.
    /// </para>
    /// </remarks>
    internal static long NormalizeResult(long resultCode)
    {
        return resultCode > DataWindowBufferStore.DataStoreSuccess
            ? DataWindowBufferStore.DataStoreSuccess
            : resultCode;
    }

    #endregion

    #region GetFullState and SetFullState - the opaque image, captured and restored

    /// <summary>
    /// Captures a carrier's complete state as an opaque binary image, reproducing
    /// <c>GetFullState(ref blbData)</c>.
    /// </summary>
    /// <param name="source">The carrier to capture.</param>
    /// <returns>
    /// The image. Never <see langword="null"/>; an empty carrier still produces a non-empty image,
    /// because the header alone carries the format marker, the version and the processing kind. That
    /// matters: an EMPTY payload is the legacy's "clear the target" signal
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L207-L209</c>], so a
    /// captured image must never be mistaken for one.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">
    /// A column holds a value whose runtime type has no tag in <see cref="FullStateValueTag"/>. NARROWED
    /// WITH A DEFINED ERROR RATHER THAN WIDENED WITH A GUESS: coercing an unknown value to its string
    /// form would round-trip as the wrong type and read as correct.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Reproduces <c>Data.GetFullState(ref blbData)</c> at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L95</c>. RETURNED NORMALLY
    /// RATHER THAN THROUGH A <c>ref</c> PARAMETER: the legacy shape is a workaround for hazard 1 of
    /// <c>docs/PB多线程绕坑提示.md</c>, where a worker calling a main-thread member that returns
    /// <c>string</c> or <c>blob</c> can corrupt memory, and that hazard has no .NET analogue. See the
    /// file header.
    /// </para>
    /// <para>
    /// KIND-AGNOSTIC ON PURPOSE. The legacy checks the processing kind at the DISPATCH SITE
    /// [<c>:L93</c>] and its <c>GetFullState</c> call is unconditional, so the guard lives on
    /// <see cref="Send"/> rather than here. That also keeps this member usable for a round-trip test of
    /// the encoding itself.
    /// </para>
    /// <para>
    /// THE FORMAT IS OPAQUE, DETERMINISTIC AND HAND-ROLLED, and each of those three is a requirement.
    /// OPAQUE because the published contract says so - <c>QueryDataChunk.data</c> is documented as
    /// carrying a format that is the codecs' concern on both sides and no field describes it.
    /// DETERMINISTIC because the parity model compares recordings byte for byte, so the same carrier
    /// state must always produce the same bytes; that is why the buffers are written in a fixed order
    /// and why columns are taken from <see cref="CarrierRow.AssignedColumnNumbers"/>, which is ordered
    /// deliberately rather than left to a dictionary's unspecified key order. HAND-ROLLED because a JSON
    /// or XML serializer would reach into the deferred Documents parser family, which C-D forbids
    /// outright - and the temptation is real precisely because "full state" sounds like a document.
    /// </para>
    /// <para>
    /// WHAT THE IMAGE CARRIES: the processing kind, then the three buffers in a fixed order, each with
    /// its own row count; per row its item status and its assigned columns in ascending column order;
    /// per column its number, its item status, its current value, and its ORIGINAL value whenever that
    /// differs from the current one. The originals are not optional decoration - <c>updatewhere=1</c>
    /// puts the ORIGINAL value of every marked column into the generated where clause, so an image
    /// carrying only current values could not express optimistic concurrency at all.
    /// </para>
    /// <para>
    /// WHAT THE IMAGE DELIBERATELY DOES NOT CARRY: the sort and filter expressions. They are not part of
    /// the buffer model this codec can observe, and DEFECT 3 is the legacy's own acknowledgement of the
    /// consequence - they must be synchronized onto the source BEFORE capture, which is what
    /// <see cref="SynchronizeSortAndFilter"/> is for.
    /// </para>
    /// <para>
    /// ONE FIDELITY LIMIT, STATED RATHER THAN LEFT TO BE DISCOVERED: a column status stamped on a column
    /// that holds no value is not represented, because the carrier publishes an enumeration of ASSIGNED
    /// columns and none of status-only columns. Every measured legacy stamping site writes the ROW
    /// status through the column-zero sentinel
    /// [<c>n_cst_thread_task_sqlquery.sru:L123, L127, L162, L168, L197, L202</c>] and the one column-level
    /// read addresses a key column, which by definition holds a value
    /// [<c>n_cst_thread_task_sqlupdate.sru:L162</c>], so the unrepresented state is not one the oracle
    /// produces.
    /// </para>
    /// </remarks>
    internal static byte[] Capture(DataWindowBufferStore source)
    {
        ArgumentNullException.ThrowIfNull(source);

        using MemoryStream buffer = new();

        using (BinaryWriter writer = new(buffer, PayloadEncoding, leaveOpen: true))
        {
            writer.Write(PayloadMagic);
            writer.Write(PayloadVersion);
            writer.Write(source.Processing.Value);

            foreach (DwBuffer dwBuffer in PayloadBufferOrder)
            {
                writer.Write((byte)dwBuffer);

                long rowCount = RowCountOf(source, dwBuffer);

                writer.Write(rowCount);

                // R9: rows are ONE-BASED and the bound is INCLUSIVE, matching the carrier surface -
                // RowAt and GetItemStatus take one-based row numbers and AppendRow returns one. There is
                // no minus one and no less-than here.
                for (long row = ItemStatusMachine.FirstRowNumber; row <= rowCount; row++)
                {
                    WriteRow(writer, source, dwBuffer, row);
                }
            }
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Restores a carrier from an opaque binary image, reproducing <c>SetFullState(blbData)</c>.
    /// </summary>
    /// <param name="target">The carrier to restore into. Its existing contents are REPLACED.</param>
    /// <param name="payload">The image, as produced by <see cref="Capture"/>.</param>
    /// <returns>
    /// <see cref="DataWindowBufferStore.DataStoreSuccess"/> when the image was restored;
    /// <see cref="DataWindowBufferStore.DataStoreFailure"/> when it could not be decoded.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="target"/> or <paramref name="payload"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Reproduces the three identical <c>SetFullState</c> calls at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L190</c>, <c>:L216</c> and
    /// <c>:L233</c>. NO PRECEDING RESET STEP AND NO CHUNK BOOKKEEPING - see <see cref="Receive"/> for the
    /// full statement of that difference from the changeset path, and for why replacing the target's
    /// contents as part of the restore is not the step the legacy omits.
    /// </para>
    /// <para>
    /// A MALFORMED PAYLOAD ANSWERS <see cref="DataWindowBufferStore.DataStoreFailure"/> RATHER THAN
    /// THROWING, because that is what the operation it reproduces does: <c>SetFullState</c> returns -1
    /// on a payload it rejects, and the caller at <c>:L190</c> stores that value and carries on. A null
    /// argument still throws, because that is a programming error rather than a rejected payload.
    /// </para>
    /// <para>
    /// WHICH FAULTS COUNT AS MALFORMED IS DECIDED IN ONE PLACE - <see cref="IsPayloadFault(Exception)"/>
    /// - AND THE LIST IS DELIBERATELY NARROW. Truncation, malformed UTF-8, a malformed 7-bit string
    /// length, an unrecognised tag, a count that could not fit and a nonsensical numeric component are
    /// all payload faults. An out-of-memory condition, a cancellation and a null reference are NOT:
    /// they propagate, because the plan requires a structural fault to terminate rather than degrade
    /// into a return code, and because swallowing an out-of-memory condition here would hide the exact
    /// resource-exhaustion problem the count guards are placed before the allocations to prevent.
    /// </para>
    /// <para>
    /// THE RESTORE ORDER PER ROW IS LOAD-BEARING AND IS NOT A STYLE CHOICE. Values are written twice:
    /// first the ORIGINAL values, then the row is re-baselined so that original equals current, and only
    /// then are the current values of modified columns written - which is what makes the carrier capture
    /// each original exactly as it stood. Writing currents first and then trying to fix the originals is
    /// impossible, because the carrier deliberately captures an original only ONCE per baseline period
    /// [<see cref="CarrierRow.SetValue"/>]. Statuses are stamped LAST, because re-baselining clears
    /// them.
    /// </para>
    /// </remarks>
    internal static long Apply(DataWindowBufferStore target, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(payload);

        try
        {
            using MemoryStream buffer = new(payload, writable: false);
            using BinaryReader reader = new(buffer, PayloadEncoding, leaveOpen: true);

            if (reader.ReadUInt32() != PayloadMagic)
            {
                // Not an image this codec wrote. The most likely cause is a changeset payload delivered
                // with the full-state flag set, which is the one way the two codecs can be crossed.
                return DataWindowBufferStore.DataStoreFailure;
            }

            byte version = reader.ReadByte();

            if (version != PayloadVersion)
            {
                return DataWindowBufferStore.DataStoreFailure;
            }

            long processing = reader.ReadInt64();

            // THE REPLACEMENT. Intrinsic to restoring a complete image, which is exactly why the legacy
            // needs no separate Reset on this arm - and NOT the changeset path's `if current = 1 then
            // Reset()` step, which is gated on a chunk index this path does not have. It is also NOT
            // followed by an update-reset, so the statuses restored below survive verbatim.
            target.Reset();

            target.Processing = new DataWindowProcessing(processing);

            for (int bufferIndex = 0; bufferIndex < PayloadBufferOrder.Length; bufferIndex++)
            {
                DwBuffer dwBuffer = (DwBuffer)reader.ReadByte();

                if (dwBuffer != PayloadBufferOrder[bufferIndex])
                {
                    return DataWindowBufferStore.DataStoreFailure;
                }

                long rowCount = reader.ReadInt64();

                // BOUNDED BEFORE IT BECOMES A LOOP BOUND. A negative count, one beyond the protocol
                // ceiling, or one whose rows could not fit in the bytes that remain is a malformed
                // image - see the guard rationale beside MaxRowsPerBuffer.
                if (!IsPlausibleCount(reader, rowCount, MinimumBytesPerRow, MaxRowsPerBuffer))
                {
                    return DataWindowBufferStore.DataStoreFailure;
                }

                // R9: the loop counts rows, not indices. It reads exactly `rowCount` rows and appends
                // each one, so the row numbers AppendRow hands back are 1..rowCount in order.
                for (long row = ItemStatusMachine.FirstRowNumber; row <= rowCount; row++)
                {
                    ReadRow(reader, target, dwBuffer);
                }
            }

            return DataWindowBufferStore.DataStoreSuccess;
        }
        catch (Exception failure) when (IsPayloadFault(failure))
        {
            // EVERY DETERMINISTIC FAULT IN THE BYTES ANSWERS THE CODE, NOT AN EXCEPTION, because that
            // is what the operation this reproduces does: SetFullState returns -1 on a payload it
            // rejects and the caller at [n_cst_threading_task_sqlquery.sru:L190] stores that value and
            // carries on. See IsPayloadFault for which faults qualify and, more importantly, which
            // deliberately do not.
            return DataWindowBufferStore.DataStoreFailure;
        }
    }

    /// <summary>
    /// Whether an exception represents a fault in the IMAGE - which
    /// <see cref="Apply(DataWindowBufferStore, byte[])"/> answers with a code - or a fault in this code
    /// or the host, which it must let escape.
    /// </summary>
    /// <param name="failure">The exception to classify.</param>
    /// <returns><see langword="true"/> when the exception is a payload fault.</returns>
    /// <remarks>
    /// <para>
    /// CENTRALISED BECAUSE AN INCOMPLETE CATCH LIST IS THE FAILURE MODE HERE, and two members of this
    /// list were reachable and unhandled before it existed:
    /// </para>
    /// <para>
    /// <see cref="DecoderFallbackException"/> - <see cref="PayloadEncoding"/> is constructed with
    /// <c>throwOnInvalidBytes: true</c> deliberately, so that a value which cannot round-trip is a
    /// detected fault rather than a silent replacement character. That decision means
    /// <see cref="BinaryReader.ReadString"/> THROWS on malformed UTF-8 rather than substituting, and a
    /// string is reachable from any column through <see cref="FullStateValueTag.String"/>. Malformed
    /// bytes in a string value are the plainest possible example of a bad payload, and they were
    /// escaping.
    /// </para>
    /// <para>
    /// <see cref="FormatException"/> - <see cref="BinaryReader.ReadString"/> reads its length as a
    /// 7-bit encoded integer and raises this when the encoding is malformed, which is exactly what a
    /// corrupted or hostile byte in that position produces.
    /// </para>
    /// <para>
    /// <see cref="EndOfStreamException"/> for a truncated image, <see cref="IOException"/> for a
    /// stream-level fault while reading one, <see cref="InvalidDataException"/> for the structural
    /// rejections this file raises by hand, <see cref="OverflowException"/> and
    /// <see cref="ArgumentException"/> - which covers
    /// <see cref="ArgumentOutOfRangeException"/> - for a nonsensical numeric component that reaches a
    /// constructor, and <see cref="NotSupportedException"/> for a value shape the format cannot
    /// express.
    /// </para>
    /// <para>
    /// DELIBERATELY ABSENT, and each absence is the fail-fast posture the refactor plan requires rather
    /// than an oversight: <see cref="OutOfMemoryException"/>, <see cref="StackOverflowException"/>,
    /// <see cref="OperationCanceledException"/> and <see cref="NullReferenceException"/> all propagate.
    /// A structural fault must terminate rather than degrade into a return code, and an out-of-memory
    /// condition swallowed as "malformed payload" would hide the very resource-exhaustion problem the
    /// count guards above exist to prevent - which is precisely why this list is narrow and why the
    /// guards are placed BEFORE the allocations rather than relying on catching what they cause.
    /// </para>
    /// <para>
    /// The sibling changeset codec classifies the same set for the same reasons. The two lists are
    /// stated independently because the two formats are independent; sharing one would imply a shared
    /// layout that does not exist.
    /// </para>
    /// </remarks>
    private static bool IsPayloadFault(Exception failure)
    {
        // InvalidDataException is named EXPLICITLY and is not covered by IOException: it derives from
        // SystemException, not from IOException, so a list that relied on the latter to catch it would
        // silently let every structural rejection this file raises by hand escape.
        return failure is EndOfStreamException
            or IOException
            or InvalidDataException
            or DecoderFallbackException
            or EncoderFallbackException
            or FormatException
            or OverflowException
            or NotSupportedException
            or ArgumentException;
    }

    #endregion

    #region Payload format internals

    /// <summary>
    /// The four-byte marker every image begins with, spelling <c>PFWF</c> - PowerFramework Full state.
    /// </summary>
    /// <remarks>
    /// Its only job is to let <see cref="Apply"/> reject a payload this codec did not write, which is the
    /// single way the two codecs can be crossed: a changeset blob delivered with the full-state flag set.
    /// Rejecting it beats decoding it as garbage.
    /// </remarks>
    private const uint PayloadMagic = 0x46_57_46_50u;

    /// <summary>
    /// The image format version. Bumped only alongside a decoder that still reads every earlier version,
    /// because stored characterization recordings are compared byte for byte.
    /// </summary>
    private const byte PayloadVersion = 1;

    /// <summary>
    /// The flag byte written for a column whose original value differs from its current one.
    /// </summary>
    private const byte OriginalValuePresent = 1;

    /// <summary>
    /// The flag byte written for a column whose original value equals its current one.
    /// </summary>
    private const byte OriginalValueAbsent = 0;

    /// <summary>
    /// The encoding strings are written with: UTF-8, WITH NO BYTE-ORDER MARK and THROWING on invalid
    /// bytes.
    /// </summary>
    /// <remarks>
    /// Both settings are deliberate. No mark, because the payload is a binary image rather than a text
    /// document and a preamble would be noise inside it. Throwing rather than substituting, because a
    /// silent replacement character is a value corruption that round-trips as if it had succeeded -
    /// exactly the failure shape the parity model cannot detect.
    /// </remarks>
    private static readonly UTF8Encoding PayloadEncoding = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    // ------------------------------------------------------------------------------------------
    //  THE IMAGE IS UNTRUSTED INPUT, AND EVERY COUNT IN IT IS AN ALLOCATION REQUEST
    //  ----------------------------------------------------------------------------------------
    //  An image arrives from a peer, so every length and count it declares is a number chosen by
    //  whoever produced the bytes. Three of them reach an allocation or an iteration bound directly:
    //  the per-buffer row count, the per-row column count - which sizes FIVE arrays before a single
    //  column is read - and a blob length, which BinaryReader.ReadBytes turns into `new byte[count]`
    //  BEFORE it discovers the stream is shorter.
    //
    //  A NON-NEGATIVE CHECK IS NOT A BOUND: eight bytes can say long.MaxValue in a twenty-byte image.
    //  The bound that is always exact is the REMAINING BYTES, because every element costs at least a
    //  known minimum on the wire and a well-formed image always carries the bytes it promised. The
    //  protocol maxima are a coarser gate in front of it, so an absurd count is rejected by name.
    //  The arithmetic is done in `long` throughout so the product cannot overflow into a small
    //  positive number and admit the count it was written to reject.
    //
    //  The sibling changeset codec states the same reasoning at greater length beside its own
    //  constants; the two formats are independent, so each carries its own limits rather than sharing
    //  them - a shared constant would imply a shared layout that does not exist.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The largest row count one buffer of an image may declare: 1,048,576.
    /// </summary>
    /// <remarks>
    /// A coarse sanity gate, far above anything the evidenced fixture produces - the sole updatable
    /// DataWindow in the repository is a six-column <c>COMPANY</c> table
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14</c>]. Declared as
    /// <see cref="long"/> because this codec writes its row counts as <see cref="long"/>, matching the
    /// legacy's own row type.
    /// </remarks>
    private const long MaxRowsPerBuffer = 1L << 20;

    /// <summary>
    /// The largest column count one row of an image may declare: 32,768.
    /// </summary>
    /// <remarks>
    /// This is the count that matters most: <see cref="ReadRow"/> sizes five arrays from it before
    /// reading any column. The legacy's own documented crosstab limitation shows a DataWindow's column
    /// count is bounded in practice [<c>n_cst_thread_task_sqlquery.sru:L673</c>], and this ceiling sits
    /// far above any real one.
    /// </remarks>
    private const long MaxColumnsPerRow = 1L << 15;

    /// <summary>
    /// The fewest bytes one row of an image can occupy: its status ordinal plus its column count.
    /// </summary>
    /// <remarks>
    /// Measured from <see cref="WriteRow"/>: a row writes its status through
    /// <see cref="WriteItemStatus"/> and then an <see cref="int"/> column count. A row with no columns
    /// is legal and costs exactly this, which makes the value a true minimum.
    /// </remarks>
    private const long MinimumBytesPerRow = sizeof(int) + sizeof(int);

    /// <summary>
    /// The fewest bytes one column entry of an image can occupy: its number, its status, its
    /// original-value presence flag, and one value tag.
    /// </summary>
    /// <remarks>
    /// Measured from <see cref="WriteRow"/>'s column loop: an <see cref="int"/> column number, the
    /// status, the one-byte presence flag, then the current value - which is at minimum a single tag
    /// byte, exactly what <see cref="FullStateValueTag.Null"/> costs. The original value is absent
    /// whenever it equals the current one, so it contributes nothing to the minimum.
    /// </remarks>
    private const long MinimumBytesPerColumn = sizeof(int) + sizeof(int) + 1 + 1;

    /// <summary>
    /// Whether <paramref name="count"/> elements of at least <paramref name="minimumBytesPerElement"/>
    /// bytes each could still fit in what remains of <paramref name="reader"/>'s stream, and is within
    /// <paramref name="protocolMaximum"/>.
    /// </summary>
    /// <param name="reader">The reader positioned immediately after the count was read.</param>
    /// <param name="count">The declared count, which may be any value the image chose.</param>
    /// <param name="minimumBytesPerElement">The fewest bytes one element can occupy on the wire.</param>
    /// <param name="protocolMaximum">The coarse ceiling this kind of count is subject to.</param>
    /// <returns>
    /// <see langword="true"/> when the count may be used as a loop bound or an allocation size;
    /// <see langword="false"/> when it must be rejected BEFORE either.
    /// </returns>
    /// <remarks>
    /// A negative count is rejected here as well, so callers need no separate sign test and cannot
    /// forget one. The multiplication is <see cref="long"/> arithmetic and the operands are bounded by
    /// <paramref name="protocolMaximum"/> before it happens, so it cannot overflow.
    /// </remarks>
    private static bool IsPlausibleCount(
        BinaryReader reader,
        long count,
        long minimumBytesPerElement,
        long protocolMaximum)
    {
        if (count < 0L || count > protocolMaximum)
        {
            return false;
        }

        long remaining = reader.BaseStream.Length - reader.BaseStream.Position;

        return count * minimumBytesPerElement <= remaining;
    }

    /// <summary>
    /// The fixed order the three buffers are written and read in.
    /// </summary>
    /// <remarks>
    /// Ascending <see cref="DwBuffer"/> value: <c>Primary!</c>, <c>Delete!</c>, then <c>Filter!</c>. FIXED
    /// RATHER THAN ENUMERATED FROM THE ENUM TYPE, because reflecting over the enum would make the payload
    /// layout depend on member declaration order in a generated file - a dependency no reader of either
    /// side would expect. The order is written into the bytes and verified on read, so a mismatch is
    /// caught rather than silently applied to the wrong buffer.
    /// </remarks>
    private static readonly DwBuffer[] PayloadBufferOrder =
        [DwBuffer.Primary, DwBuffer.Delete, DwBuffer.Filter];

    /// <summary>
    /// Rejects a carrier whose processing kind belongs to the sibling codec's arm.
    /// </summary>
    /// <param name="carrier">The carrier to test.</param>
    /// <param name="parameterName">The caller's parameter name, for the exception message.</param>
    /// <exception cref="InvalidOperationException">
    /// The carrier's processing kind lands in the legacy's <c>case else</c> arm.
    /// </exception>
    /// <remarks>
    /// The processing kind is read from the carrier's own typed property - which delegates to
    /// <see cref="DataWindowProcessing.SelectsFullStateTransfer"/> - and is NEVER re-parsed from a
    /// describe string here. This is what makes the discriminator between the two codecs total: every
    /// carrier belongs to exactly one arm, with no overlap and no gap, and reaching the wrong codec is a
    /// programming error rather than a data condition.
    /// </remarks>
    private static void ThrowIfNotFullStateCarrier(DataWindowBufferStore carrier, string parameterName)
    {
        if (carrier.RequiresFullStateTransfer)
        {
            return;
        }

        throw new InvalidOperationException(
            $"The carrier passed as '{parameterName}' declares processing kind "
                + carrier.Processing.Value.ToString(CultureInfo.InvariantCulture)
                + ", which lands in the legacy's `case else` arm at "
                + "ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L102 and therefore "
                + "belongs to Buffers/ChangesetCodec.cs. FullStateCodec is the `case 4,5` arm - crosstab "
                + "and composite - and only those two processing kinds select it.");
    }

    /// <summary>
    /// Returns the row count of one buffer, using the carrier's own count for that buffer.
    /// </summary>
    /// <param name="store">The carrier.</param>
    /// <param name="buffer">Which buffer to count.</param>
    /// <returns>The number of rows, as a one-based count.</returns>
    /// <exception cref="InvalidDataException"><paramref name="buffer"/> is not a declared buffer.</exception>
    /// <remarks>
    /// The three counts are DIFFERENT MEMBERS in the legacy and are not interchangeable: <c>RowCount()</c>
    /// for <c>Primary!</c>, <c>DeletedCount()</c> for <c>Delete!</c> and <c>FilteredCount()</c> for
    /// <c>Filter!</c>. Note that <c>Filter!</c>'s ROW ORDER IS INVERTED relative to the source - the
    /// legacy iterates it backwards for exactly that reason
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L235-L237</c>] - so this
    /// codec preserves the buffer's order AS THE CARRIER REPORTS IT and never reverses it. Capture and
    /// restore both walk forwards, which round-trips the order the carrier holds; reversing either would
    /// produce wrong data that a row-count assertion could not catch (R9).
    /// <para>
    /// THE DEFAULT ARM IS UNREACHABLE AND IS REQUIRED ANYWAY, WHICH IS WHY IT IS THE ONE THING IN THIS
    /// FILE NO TEST COVERS. This method is private and every call passes a member of
    /// <see cref="PayloadBufferOrder"/>, so no fourth value can arrive; but a switch expression over an
    /// enumeration without a catch-all raises the exhaustiveness warning CS8524, and
    /// <c>TreatWarningsAsErrors</c> makes that a build failure. The arm therefore exists to satisfy the
    /// compiler, throws rather than answering a plausible count, and is recorded here as a deliberate
    /// coverage gap instead of being papered over with a test that reaches it by reflection.
    /// </para>
    /// </remarks>
    private static long RowCountOf(DataWindowBufferStore store, DwBuffer buffer)
    {
        return buffer switch
        {
            DwBuffer.Primary => store.RowCount(),
            DwBuffer.Delete => store.DeletedCount(),
            DwBuffer.Filter => store.FilteredCount(),
            _ => throw new InvalidDataException(
                "The payload names a buffer that is not Primary, Delete or Filter. The legacy model has "
                    + "no fourth buffer and this port declares none."),
        };
    }

    /// <summary>
    /// Writes one row - its item status, then each assigned column in ascending column order.
    /// </summary>
    /// <param name="writer">The payload writer.</param>
    /// <param name="source">The carrier being captured.</param>
    /// <param name="buffer">The buffer the row belongs to.</param>
    /// <param name="row">The one-based row number. R9: never rebased.</param>
    private static void WriteRow(
        BinaryWriter writer,
        DataWindowBufferStore source,
        DwBuffer buffer,
        long row)
    {
        // The column-zero sentinel means "the row itself" and is reached through ItemStatusMachine
        // rather than by writing a bare 0 (R9 item 4).
        ItemStatus rowStatus = source.GetItemStatus(row, ItemStatusMachine.RowStatusColumn, buffer);

        writer.Write((int)rowStatus);

        CarrierRow carrierRow = source.RowAt(row, buffer);

        // Materialised before the count is written, because the count and the sequence that follows it
        // must come from ONE enumeration. AssignedColumnNumbers is ordered ascending deliberately, which
        // is what makes the byte layout reproducible across runs.
        int[] columnNumbers = [.. carrierRow.AssignedColumnNumbers];

        writer.Write(columnNumbers.Length);

        foreach (int columnNumber in columnNumbers)
        {
            writer.Write(columnNumber);
            writer.Write((int)source.GetItemStatus(row, columnNumber, buffer));

            object? current = carrierRow.GetValue(columnNumber);
            object? original = carrierRow.GetOriginalValue(columnNumber);

            // The carrier answers the CURRENT value when no original was captured, so equality here means
            // "not modified since the last baseline" and the original need not travel. Reference equality
            // for a blob makes this conservative rather than wrong: two equal byte arrays are written as
            // distinct, which round-trips correctly and merely costs bytes.
            bool originalDiffers = !Equals(current, original);

            writer.Write(originalDiffers ? OriginalValuePresent : OriginalValueAbsent);

            WriteValue(writer, current);

            if (originalDiffers)
            {
                WriteValue(writer, original);
            }
        }
    }

    /// <summary>
    /// Reads one row and appends it to <paramref name="target"/>, restoring its values, its originals and
    /// its statuses in the order that makes each of the three land correctly.
    /// </summary>
    /// <param name="reader">The payload reader.</param>
    /// <param name="target">The carrier being restored.</param>
    /// <param name="buffer">The buffer the row belongs to.</param>
    /// <exception cref="InvalidDataException">The row is structurally invalid.</exception>
    private static void ReadRow(BinaryReader reader, DataWindowBufferStore target, DwBuffer buffer)
    {
        ItemStatus rowStatus = ReadItemStatus(reader);

        int columnCount = reader.ReadInt32();

        // BOUNDED BEFORE THE FIVE ALLOCATIONS BELOW. They are sized from this number before a single
        // column has been read, so an unbounded count of int.MaxValue would demand gigabytes and raise
        // OutOfMemoryException instead of answering the failure code Apply is contracted to return.
        if (!IsPlausibleCount(reader, columnCount, MinimumBytesPerColumn, MaxColumnsPerRow))
        {
            throw new InvalidDataException(
                "A row declares a column count that is negative, beyond the protocol maximum, or "
                    + "larger than the bytes that remain could hold, so the image is not one this "
                    + "codec wrote.");
        }

        // R9: AppendRow RETURNS the new row's ONE-BASED number - the post-add count, with no minus one -
        // and every subsequent call in this method addresses the row by that number.
        long row = target.AppendRow(buffer, ItemStatus.NotModified);

        CarrierRow carrierRow = target.RowAt(row, buffer);

        int[] columnNumbers = new int[columnCount];
        object?[] currentValues = new object?[columnCount];
        object?[] originalValues = new object?[columnCount];
        ItemStatus[] columnStatuses = new ItemStatus[columnCount];
        bool[] originalDiffers = new bool[columnCount];

        // Zero-based purely because these are CLR array slots for a fixed-size decode buffer, not row or
        // column positions. Every column NUMBER they hold stays one-based (R9 item 3).
        for (int slot = 0; slot < columnCount; slot++)
        {
            int columnNumber = reader.ReadInt32();

            if (columnNumber < ItemStatusMachine.FirstColumnNumber)
            {
                throw new InvalidDataException(
                    "A row names a column number below the one-based floor, so the image is not one this "
                        + "codec wrote. Column numbers are never rebased in this port.");
            }

            columnNumbers[slot] = columnNumber;
            columnStatuses[slot] = ReadItemStatus(reader);
            originalDiffers[slot] = ReadOriginalPresence(reader);
            currentValues[slot] = ReadValue(reader);
            originalValues[slot] = originalDiffers[slot] ? ReadValue(reader) : currentValues[slot];
        }

        // PASS 1 - the ORIGINAL values. Written first so that the baseline below captures the state the
        // database last saw, which is what `updatewhere=1` puts into its generated where clause.
        for (int slot = 0; slot < columnCount; slot++)
        {
            target.SetItemValue(row, columnNumbers[slot], buffer, originalValues[slot]);
        }

        // RE-BASELINE. Clears the captured originals so that original equals current, and clears the
        // statuses - which is why the statuses are stamped afterwards rather than before.
        carrierRow.Baseline();

        // PASS 2 - the CURRENT values, only where they differ. Each write captures the original exactly
        // once, and the value it captures is the one pass 1 established.
        for (int slot = 0; slot < columnCount; slot++)
        {
            if (originalDiffers[slot])
            {
                target.SetItemValue(row, columnNumbers[slot], buffer, currentValues[slot]);
            }
        }

        // STATUSES LAST. Column statuses through their one-based column numbers, then the row's own
        // status through the column-zero sentinel.
        for (int slot = 0; slot < columnCount; slot++)
        {
            target.SetItemStatus(row, columnNumbers[slot], buffer, columnStatuses[slot]);
        }

        target.SetItemStatus(row, ItemStatusMachine.RowStatusColumn, buffer, rowStatus);
    }

    #endregion

    #region Tagged value encoding

    /// <summary>
    /// Writes one column value, tagged with its runtime type.
    /// </summary>
    /// <param name="writer">The payload writer.</param>
    /// <param name="value">
    /// The value. <see langword="null"/> IS A VALUE HERE AND NOT AN ABSENCE - PowerBuilder has null for
    /// value types, the ported tri-state return-code algebra depends on it, and collapsing it to zero
    /// would convert "neither succeeded nor failed" into "succeeded".
    /// </param>
    /// <exception cref="NotSupportedException">
    /// The value's runtime type has no tag. Deliberately fatal: the alternative - writing the value's
    /// string form - produces an image that decodes to the wrong type and reads as correct.
    /// </exception>
    private static void WriteValue(BinaryWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.Write((byte)FullStateValueTag.Null);
                break;

            case bool booleanValue:
                writer.Write((byte)FullStateValueTag.Boolean);
                writer.Write(booleanValue);
                break;

            case int int32Value:
                writer.Write((byte)FullStateValueTag.Int32);
                writer.Write(int32Value);
                break;

            case long int64Value:
                writer.Write((byte)FullStateValueTag.Int64);
                writer.Write(int64Value);
                break;

            case ulong unsignedValue:
                writer.Write((byte)FullStateValueTag.UInt64);
                writer.Write(unsignedValue);
                break;

            case double doubleValue:
                writer.Write((byte)FullStateValueTag.Double);
                writer.Write(doubleValue);
                break;

            case decimal decimalValue:
                writer.Write((byte)FullStateValueTag.Decimal);

                // Written as its four component integers rather than through a numeric conversion, so
                // that SCALE SURVIVES. The fixture's salary column is declared decimal(2) against a REAL
                // database column [ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L463-L469], a mismatch
                // the migration preserves as a defect, and a round trip that silently renormalised 1.50
                // to 1.5 would erase exactly that kind of evidence.
                foreach (int component in decimal.GetBits(decimalValue))
                {
                    writer.Write(component);
                }

                break;

            case string stringValue:
                writer.Write((byte)FullStateValueTag.String);
                writer.Write(stringValue);
                break;

            case byte[] blobValue:
                writer.Write((byte)FullStateValueTag.Bytes);
                writer.Write(blobValue.Length);
                writer.Write(blobValue);
                break;

            case DateTime dateTimeValue:
                writer.Write((byte)FullStateValueTag.DateTime);
                writer.Write(dateTimeValue.Ticks);

                // The kind travels with the ticks. Dropping it would turn an unspecified timestamp into a
                // local or UTC one on the far side, which is a value change disguised as a formatting
                // detail.
                writer.Write((byte)dateTimeValue.Kind);
                break;

            case DateOnly dateValue:
                writer.Write((byte)FullStateValueTag.DateOnly);
                writer.Write(dateValue.DayNumber);
                break;

            case TimeOnly timeValue:
                writer.Write((byte)FullStateValueTag.TimeOnly);
                writer.Write(timeValue.Ticks);
                break;

            default:
                throw new NotSupportedException(
                    "A column holds a value of type '"
                        + value.GetType().FullName
                        + "', which has no tag in FullStateValueTag. The full-state image is narrowed "
                        + "with this defined error rather than widened with a guess: coercing an unknown "
                        + "value to its string form would round-trip as the wrong type and read as "
                        + "correct. Add a tag - APPENDED, never inserted, because the values are written "
                        + "into the payload - together with its writer and reader arms.");
        }
    }

    /// <summary>
    /// Reads one tagged column value.
    /// </summary>
    /// <param name="reader">The payload reader.</param>
    /// <returns>The value, which may legitimately be <see langword="null"/>.</returns>
    /// <exception cref="InvalidDataException">The tag is unrecognised or a length is negative.</exception>
    private static object? ReadValue(BinaryReader reader)
    {
        FullStateValueTag tag = (FullStateValueTag)reader.ReadByte();

        switch (tag)
        {
            case FullStateValueTag.Null:
                return null;

            case FullStateValueTag.Boolean:
                return reader.ReadBoolean();

            case FullStateValueTag.Int32:
                return reader.ReadInt32();

            case FullStateValueTag.Int64:
                return reader.ReadInt64();

            case FullStateValueTag.UInt64:
                return reader.ReadUInt64();

            case FullStateValueTag.Double:
                return reader.ReadDouble();

            case FullStateValueTag.Decimal:
                int[] components =
                [
                    reader.ReadInt32(),
                    reader.ReadInt32(),
                    reader.ReadInt32(),
                    reader.ReadInt32(),
                ];

                try
                {
                    return new decimal(components);
                }
                catch (ArgumentException invalidComponents)
                {
                    // A decimal whose component integers are not a legal encoding. Reported as image
                    // corruption so that Apply answers the failure code rather than escaping.
                    throw new InvalidDataException(
                        "The image carries a decimal whose component integers are not a valid encoding.",
                        invalidComponents);
                }

            case FullStateValueTag.String:
                return reader.ReadString();

            case FullStateValueTag.Bytes:
                int length = reader.ReadInt32();

                // BOUNDED BEFORE THE READ, NOT AFTER IT. ReadBytes allocates `new byte[length]` up
                // front and only then discovers the stream is shorter, so a declared length of
                // int.MaxValue reserves two gigabytes before failing. A blob element is one byte, so
                // the remaining-byte rule is the exact bound here and no invented ceiling is wanted.
                if (!IsPlausibleCount(reader, length, minimumBytesPerElement: 1L, protocolMaximum: int.MaxValue))
                {
                    throw new InvalidDataException(
                        "The image declares a blob length that is negative or longer than the bytes "
                            + "that remain, so it is not one this codec wrote.");
                }

                // ReadBytes may legitimately return fewer bytes than asked for at the end of a stream, so
                // the shortfall is checked rather than assumed away - an unchecked read would silently
                // substitute a truncated blob for the real one.
                byte[] blob = reader.ReadBytes(length);

                if (blob.Length != length)
                {
                    throw new EndOfStreamException(
                        "The image ends inside a blob value, so it is truncated.");
                }

                return blob;

            case FullStateValueTag.DateTime:
                long ticks = reader.ReadInt64();
                byte kind = reader.ReadByte();

                if (!Enum.IsDefined((DateTimeKind)kind))
                {
                    throw new InvalidDataException(
                        "The image carries a timestamp with an undefined DateTimeKind.");
                }

                try
                {
                    return new DateTime(ticks, (DateTimeKind)kind);
                }
                catch (ArgumentOutOfRangeException outOfRange)
                {
                    throw new InvalidDataException(
                        "The image carries a timestamp outside the representable range.",
                        outOfRange);
                }

            case FullStateValueTag.DateOnly:
                int dayNumber = reader.ReadInt32();

                try
                {
                    return DateOnly.FromDayNumber(dayNumber);
                }
                catch (ArgumentOutOfRangeException outOfRange)
                {
                    throw new InvalidDataException(
                        "The image carries a date outside the representable range.",
                        outOfRange);
                }

            case FullStateValueTag.TimeOnly:
                long timeTicks = reader.ReadInt64();

                try
                {
                    return new TimeOnly(timeTicks);
                }
                catch (ArgumentOutOfRangeException outOfRange)
                {
                    throw new InvalidDataException(
                        "The image carries a time of day outside the representable range.",
                        outOfRange);
                }

            default:
                throw new InvalidDataException(
                    "The image carries value tag "
                        + ((byte)tag).ToString(CultureInfo.InvariantCulture)
                        + ", which this codec does not recognise. Tags are only ever appended, so an "
                        + "unknown tag means the image was written by a newer or a different encoder.");
        }
    }

    /// <summary>
    /// Reads an item status, rejecting a value outside the published four-member domain.
    /// </summary>
    /// <param name="reader">The payload reader.</param>
    /// <returns>The status.</returns>
    /// <exception cref="InvalidDataException">The value is not a declared status.</exception>
    /// <remarks>
    /// The domain is the generated <see cref="ItemStatus"/> enum and this port ADDS NO MEMBER TO IT: the
    /// four legal values are <c>NotModified!</c>, <c>DataModified!</c>, <c>New!</c> and
    /// <c>NewModified!</c>. Validating on read rather than casting blindly keeps a corrupt image from
    /// planting a status no legacy path can produce, which would then read as a legitimate state
    /// everywhere downstream.
    /// </remarks>
    private static ItemStatus ReadItemStatus(BinaryReader reader)
    {
        int raw = reader.ReadInt32();

        if (!Enum.IsDefined((ItemStatus)raw))
        {
            throw new InvalidDataException(
                "The image carries item status "
                    + raw.ToString(CultureInfo.InvariantCulture)
                    + ", which is not one of the four published values. This port adds no fifth status.");
        }

        return (ItemStatus)raw;
    }

    /// <summary>
    /// Reads the flag that says whether a column's original value follows its current one.
    /// </summary>
    /// <param name="reader">The payload reader.</param>
    /// <returns>
    /// <see langword="true"/> when an original value follows; <see langword="false"/> when the original
    /// equals the current value and was therefore not written.
    /// </returns>
    /// <exception cref="InvalidDataException">The flag byte is neither of the two legal values.</exception>
    private static bool ReadOriginalPresence(BinaryReader reader)
    {
        byte flag = reader.ReadByte();

        return flag switch
        {
            OriginalValueAbsent => false,
            OriginalValuePresent => true,
            _ => throw new InvalidDataException(
                "The image carries an original-value flag that is neither 0 nor 1, so it is not one this "
                    + "codec wrote."),
        };
    }

    #endregion
}
