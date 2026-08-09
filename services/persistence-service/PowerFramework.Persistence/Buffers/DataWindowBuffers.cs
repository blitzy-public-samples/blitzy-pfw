// ==============================================================================================
//  DataWindowBuffers.cs - THE DATAWINDOW BUFFER ANTI-CORRUPTION LAYER AND THE _ds / _ds_mt PAIR
//  --------------------------------------------------------------------------------------------
//  PORTED FROM    ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru    (176 L)
//                     the MAIN-THREAD result carrier. Header comment
//                     `$PBExportComments$线程SQL任务对象Datastore~r~n[运行在主线程]`, whose bracketed
//                     annotation reads "runs on the main thread". Declared
//                     `global type n_cst_thread_task_sqlbase_ds from datastore`.
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru  (73 L)
//                     the WORKER carrier, `from n_cst_thread_task_sqlbase_ds`, header annotation
//                     `[运行在子线程]` - "runs on a child (worker) thread".
//  ALSO SOURCED   ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru
//                     :L553-L557 the carrier-selection site, :L527-L570 of_getcacheds,
//                     :L45/:L127-L132/:L524 the NChar-binding derivation and its accessor
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru
//                     :L72-L110 ondatareceived - the main-thread no-serialization fast path at
//                     :L84-L90 and the codec selector at :L93
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L28-L33
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L32
//                     the two per-task notify-code declarations
//                 docs/PB多线程绕坑提示.md
//                     the two documented PowerBuilder threading hazards
//  FIXTURE        ws_objects/pfw.tests.pbl.src/dw_sqlite.srd
//                     the golden master: processing=1, sort="age A salary A ", six columns all
//                     `update=yes updatewhereclause=yes`, `updatewhere=1 updatekeyinplace=no`
//
//  ORACLE STATUS (C-C). Every ws_objects/** and docs/** path above is READ ONLY. They are the
//  behavioural oracle this migration is verified against and are never an edit target, never
//  reformatted and never moved. Nothing else in this repository can adjudicate the behaviour
//  reproduced below - logfile.md stops at framework 3.0.7.2062 while the history runs years later,
//  and the two PowerBuilder project objects contradict each other and both name a library that does
//  not exist - so EVERY behavioural claim in this file carries a line locator a reader can diff
//  against.
//
//  ============================================================================================
//  WHY THIS LAYER IS REQUIRED RATHER THAN STYLISTIC (AAP 0.3.4)
//  ============================================================================================
//  `n_cst_thread_task_sqlbase_ds` DERIVES FROM `datastore`. The legacy result carrier therefore IS
//  a DataWindow - complete with the Primary!/Delete!/Filter! buffers, per-row and per-column item
//  statuses, and original values per column - and not a flat result set. A naive rowset would
//  silently discard exactly the state the C-06 update contract depends on: `updatewhere=1` puts the
//  ORIGINAL value of every marked column into the generated where clause, so a payload that carries
//  only current values cannot express optimistic concurrency at all. This file is what preserves
//  that state across the network boundary decomposition creates.
//
//  THE INHERITANCE CHAIN IS REPRODUCED ONE FOR ONE, and the three C# types map onto it exactly:
//
//      PowerBuilder                            C# in this file
//      ----------------------------------      --------------------------------------------
//      datastore                          ->   DataWindowBufferStore
//      n_cst_thread_task_sqlbase_ds       ->   DataWindowCarrier          (main-thread affinity)
//      n_cst_thread_task_sqlbase_ds_mt    ->   WorkerDataWindowCarrier    (worker affinity)
//
//  DataWindowBufferStore stands in for the PowerBuilder `datastore` ancestor: it carries the
//  members `_ds` and `_ds_mt` INHERIT and call - the three buffers, the counts, item-status get and
//  set, the modified-row walk, RowsMove, RowsDiscard, Reset, ResetUpdate, SetSqlPreview and the
//  processing kind - which is why none of those appear in `_ds.sru`'s own prototype list.
//
//  ============================================================================================
//  DECISION 1 - TWO CARRIER TYPES, NEVER ONE, BECAUSE AFFINITY IS CONTRACT (C-K, AAP 0.4.5.4)
//  ============================================================================================
//  The refactor plan states that the legacy's thread-affinity annotations are CONTRACT, NOT
//  COMMENTARY, and the selection site proves it. n_cst_thread_task_sqlbase.sru:L553-L557:
//
//      if #ParentThread.of_IsMainThread() then
//          ds = Create n_cst_thread_task_sqlbase_ds        // main-thread carrier
//      else
//          ds = Create n_cst_thread_task_sqlbase_ds_mt     // worker carrier
//      end if
//
//  Collapsing the pair would ERASE REAL BEHAVIOUR, not merely a name. On the main thread, with no
//  receiver installed and caching off, n_cst_thread_task_sqlquery.sru:L84-L90 hands the carrier over
//  BY REFERENCE and runs NO CODEC, NO BLOB and NO SERIALIZATION AT ALL:
//
//      if #ParentThread.of_IsMainThread() then
//          if Not tasking._of_HasReceiver() and Not _bCache then
//              tasking.Event OnDataMove(data)
//              SetNull(data)
//              return RetCode.OK
//          end if
//      end if
//
//  That path exists only because the carrier and its consumer share an address space, which is
//  precisely what the affinity records. It is reproduced here as an explicit ownership transfer -
//  see DataWindowCarrierOwnership - in which the sender hands the carrier over and then DROPS ITS
//  OWN REFERENCE, the managed equivalent of `SetNull(data)` and the same discipline as hazard 2 of
//  docs/PB多线程绕坑提示.md. Affinity is a first-class inspectable property of the carrier so the
//  marshalling boundary is visible in the type system rather than implied by a comment.
//
//  ============================================================================================
//  DECISION 2 - THE FIVE PRESERVED DEFECTS (C-B). EACH IS ANNOTATED WHERE IT IS REPRODUCED.
//  ============================================================================================
//  A competent implementer would "fix" every one of these and thereby break parity:
//
//    1. DELETE-THEN-INSERT IS NOT COUNTED.  n_cst_thread_task_sqlbase_ds_mt.sru:L32-L35 returns the
//       ancestor value WITHOUT incrementing the progress counter when the statement is a delete
//       preview against Primary! or Filter!. It looks like a missing increment; it is the delete
//       half of the `updatekeyinplace=no` delete-then-insert pair, and the fixture sets
//       `updatekeyinplace=no` [dw_sqlite.srd:L14], so this arm is EXERCISED, not hypothetical.
//    2. THE DELETED-ROW LOOP RUNS BACKWARD.  :L55 iterates `DeletedCount() to 1 step -1`. R9 warns
//       that reversing such a loop yields wrong data that still passes a row-count assertion.
//    3. CAP LIFTING - A HANDLER RETURNING 1 CONTINUES.  :L65-L68 clears the row cap and returns 0
//       when the notification handler returns 1. Everywhere else in this codebase a handler
//       returning 1 means abort; here it means keep going with the cap lifted.
//    4. THE PROGRESS PAYLOAD TRUNCATES ABOVE 65535.  :L39 packs current and total through
//       MakeLong, whose halves are 16-bit words. Reachable, because the chunk size defaults to
//       10000 and a result may hold far more rows than 65535. Not widened.
//    5. THE NCHAR REWRITER RETURNS ITS INPUT UNCHANGED.  n_cst_thread_task_sqlbase_ds.sru:L125-L128
//       returns `sql` itself the moment it meets a literal already prefixed with N. That is an
//       early return, not an optimisation, and it must not become an idempotent rewrite that
//       produces an equal-but-not-identical string.
//
//  ============================================================================================
//  DECISION 3 - WHY EVERY TYPE HERE IS `internal`
//  ============================================================================================
//  Two independent reasons. First, every consumer - Buffers/ChangesetCodec.cs,
//  Buffers/FullStateCodec.cs, Concurrency/*, Tasks/* and Grpc/* - lives in this same assembly, so
//  public exposure is unnecessary and C-H asks for internal seams where it is. Second, and
//  decisively, the sibling Buffers/ItemStatus.cs declares `internal static class ItemStatusMachine`;
//  a public member of this file that named it in a signature would be CS0051 inconsistent
//  accessibility, and TreatWarningsAsErrors makes that a hard build failure. The test project
//  reaches everything here through the csproj's InternalsVisibleTo item, which is what makes the
//  per-service coverage gate attainable without widening the API.
//
//  ============================================================================================
//  DECISION 4 - THE `ref blob` SHAPE IS DELIBERATELY NOT REPRODUCED (C-K)
//  ============================================================================================
//  Every legacy codec signature is `GetChanges(ref blbData)` rather than a blob return, and
//  docs/PB多线程绕坑提示.md hazard 1 is why: a worker thread synchronously calling a main-thread
//  member that returns `string` or `blob` can corrupt memory, the trigger being an undelivered
//  posted message against an object that is then destroyed, and the prescribed avoidance is to
//  return through a `ref` parameter instead. THE HAZARD HAS NO .NET ANALOGUE - there is no message
//  pump, no PowerBuilder object lifetime and no posted-message queue in a headless container - so
//  managed members here return values normally. The departure is recorded rather than left for a
//  reader to mistake for carelessness.
//
//  ============================================================================================
//  DECISION 5 - WHAT IS DELIBERATELY NOT HERE
//  ============================================================================================
//  NO DATA-OBJECT CACHE. of_getcacheds [n_cst_thread_task_sqlbase.sru:L527-L570] keeps its cache on
//  the PARENT THREAD under the key `$SQL.DataStoreCache`, so it belongs to Tasks/SqlTaskBase.cs,
//  which may use OrderedMap from PowerFramework.Shared.Containers for it. This file exposes only
//  the two members that path calls on the carrier: the clear-state step it invokes on a cache hit
//  [:L546] and the initialization step it invokes on every path [:L566].
//
//  NO SIBLING-FOLDER DEPENDENCY. Nothing here reaches into Concurrency/, Tasks/, Grpc/, Errors/,
//  Sql/, Data/ or Transactions/. This file is foundational: it consumes Buffers/ItemStatus.cs, the
//  published contracts and PowerFramework.Shared.Kernel, and nothing else. That is why the database
//  error hook is a callback over five raw scalars rather than a dependency on Errors/DbErrorData.cs
//  - and it also matches the legacy, where the PARENT TASK assembles the `dberrordata` structure and
//  the carrier merely forwards the scalars [n_cst_thread_task_sqlbase_ds.sru:L159].
//
//  NO PRESENTATION, AND THE EVIDENCE IS AN ASYMMETRY (C-D). SetRedraw and GroupCalc appear beside
//  these very operations in the legacy, and ONLY in its DataWindow arms, never in its DataStore
//  arms: n_cst_threading_task_sqlquery.sru:L185-L208 calls dw.SetRedraw(false), dw.ResetUpdate(),
//  dw.GroupCalc() and dw.SetRedraw(true) inside `case DataWindow!`, while the structurally identical
//  DataStore arm at :L212-L228 calls Reset, SetChanges and ResetUpdate and NEITHER of the other two.
//  The same asymmetry recurs in n_cst_threading_task_sqlupdate.sru. Two independent sightings are
//  the evidence that both calls are PRESENTATIONAL and belong to the deferred DesignSystem
//  capability area; they are a DOCUMENTED NON-PORT. ResetUpdate is NOT presentational - it appears
//  in both arms - and is kept.
//
//  NO STORAGE, NO I/O, NO WALL CLOCK (C-E, C-H). No DbConnection, no EF Core type, no
//  Microsoft.Data.Sqlite type, no file and no socket. No SQL dialect is assumed and no schema is
//  known. Every time read goes through an injected TimeProvider; see DECISION 6.
//
//  NO SERIALIZER (C-D). No JSON and no XML - either would reach into the deferred Documents parser
//  family. Serialization is the codecs' concern and it crosses the wire as protobuf, which is
//  already a contract.
//
//  ============================================================================================
//  DECISION 6 - THE CLOCK IS INJECTED, AND THAT IS MANDATORY (C-H, AAP 0.6.7)
//  ============================================================================================
//  The worker carrier rate-limits its progress notifications on a 100-unit tick delta
//  [n_cst_thread_task_sqlbase_ds_mt.sru:L37, stamped at :L38 and :L53]. THAT THRESHOLD IS DIRECTLY
//  OBSERVABLE IN THE NOTIFICATION STREAM - it decides how many notifications a given update emits -
//  and the Golden-Master technique's one hard prerequisite is repeatability with non-deterministic
//  values masked from BOTH master and candidate. A real clock would make the notification count vary
//  run to run and the parity comparison meaningless. Every read therefore goes through the single
//  TimeProvider this service registers, taken by constructor injection; no second clock abstraction
//  is introduced, and DateTime, DateTimeOffset, Stopwatch and Environment.TickCount appear nowhere.
//
//  The legacy reads CPU(), a MONOTONIC counter of milliseconds, so the comparison is a DELTA between
//  two reads of one monotonic source and not a wall-clock timestamp. The mapping is therefore
//  TimeProvider.GetTimestamp for the stamp and TimeProvider.GetElapsedTime for the delta, truncated
//  to whole milliseconds so that the strict `> 100` boundary lands exactly where integer CPU()
//  arithmetic puts it.
//
//  ============================================================================================
//  R9 - ONE-BASED TO ZERO-BASED, AUDITED INDIVIDUALLY FOR THIS FILE
//  ============================================================================================
//  The refactor plan names this the single most dangerous mechanical hazard in the migration and
//  permits an individual audit in place of a centralized helper. This is that audit; each item is
//  also commented at its site:
//
//    (1) THE BACKWARD DELETED-ROW LOOP, `DeletedCount() to 1 step -1`
//        [n_cst_thread_task_sqlbase_ds_mt.sru:L55]. Both bounds are one-based row numbers and the
//        descent terminates at 1, not 0. The direction is deliberate - see defect 2.
//    (2) THE NCHAR SCAN, `for nPos = 1 to nLen` with a `Mid(sql,nPos - 1,1)` LOOK-BEHIND
//        [:L125] and an in-loop `nPos ++` SKIP [:L119]. All three are one-based and each is
//        translated individually below. PowerBuilder's Mid returns "" for an out-of-range start, so
//        the look-behind at position 1 must yield empty rather than throw.
//    (3) EVERY ROW NUMBER on this surface is ONE-BASED, matching the legacy: the fixture declares
//        column ids 1 through 6 [dw_sqlite.srd:L21-L26] and every measured loop bounds itself by a
//        buffer's own count. No row number is rebased anywhere in this file.
//    (4) THE COLUMN-ZERO STATUS SENTINEL IS NOT AN INDEX. It means "the row itself", and it is
//        resolved through ItemStatusMachine rather than compared to a bare 0 here.
//
//  ============================================================================================
//  RULES POSITION
//  ============================================================================================
//  review_rules returns exactly one line, "No user rules provided.", so NO user-specified rule
//  governs this file and none is invented here; the absence is not treated as licence to lower the
//  bar. The enterprise-standard baseline applies in their place and the binding constraints are the
//  refactor plan's own non-rule inventory. Those bearing on this file are cited inline where each is
//  discharged: C-A, C-B, C-C, C-D, C-E, C-F, C-G, C-H, C-I, C-K and risk R9.
//
//  C-F / C-G SELF-AUDIT: no key, credential, token, password, connection string or secret-shaped
//  literal appears anywhere in this file. The database-error hook forwards `sqlSyntax`, which per
//  AAP 0.6.3.8 carries the COMPLETE generated statement including interpolated literal values, and
//  THIS FILE NEVER LOGS IT - there is no logger here at all. Redaction is Errors/SqlRedactor.cs's
//  job.
//
//  C-I SELF-AUDIT: no PackageReference is added by this file. The only non-BCL names it binds are
//  the published contracts and two aliased types from PowerFramework.Shared.Kernel, both of which
//  arrive through ProjectReference items that already exist.
// ==============================================================================================

using System.Globalization;
using System.Text;
using PowerFramework.Contracts.Common.V1;

// COLLISION RESOLUTION (AAP 0.4.5.1). PowerBuilder resolved one flat global namespace by library
// list order, so the port has no import statements to rewrite - it has namespaces to CREATE, plus
// the collisions a flat namespace tolerated to resolve. This is one of them:
// PowerFramework.Contracts.Common.V1 declares a protobuf MESSAGE named RetCode while
// PowerFramework.Shared.Kernel declares the STATIC CLASS of return-code constants, also named
// RetCode. Importing both namespaces would make every unqualified mention CS0104 ambiguous, so the
// two kernel types this file needs are brought in by alias instead. An alias declared here takes
// precedence over a namespace import, which resolves the collision without hiding it.
using Bits = PowerFramework.Shared.Kernel.Bits;
using RetCode = PowerFramework.Shared.Kernel.RetCode;

namespace PowerFramework.Persistence.Buffers;

#region Thread affinity - the marshalling boundary made visible in the type system

/// <summary>
/// Which thread a result carrier is built for, and therefore which of the two legacy carrier types
/// it reproduces.
/// </summary>
/// <remarks>
/// <para>
/// Reproduces the branch at
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L553-L557</c>, which asks
/// <c>#ParentThread.of_IsMainThread()</c> and creates
/// <c>n_cst_thread_task_sqlbase_ds</c> or <c>n_cst_thread_task_sqlbase_ds_mt</c> accordingly.
/// </para>
/// <para>
/// C-K. This exists as a named type rather than a <see langword="bool"/> because the legacy encodes
/// the required execution context in the carrier's own header comment - <c>[运行在主线程]</c> on
/// <c>n_cst_thread_task_sqlbase_ds.sru:L2</c> and <c>[运行在子线程]</c> on
/// <c>n_cst_thread_task_sqlbase_ds_mt.sru:L2</c> - and the refactor plan treats those annotations as
/// contract rather than commentary. A boolean named for the main thread would make the worker case
/// the negation of something, which is the wrong shape for a two-sided marshalling boundary.
/// </para>
/// </remarks>
internal enum CarrierThreadAffinity
{
    /// <summary>
    /// The carrier runs on the main thread and reproduces <c>n_cst_thread_task_sqlbase_ds</c>. This
    /// is the only affinity on which the no-serialization handover of
    /// <c>n_cst_thread_task_sqlquery.sru:L84-L90</c> is available.
    /// </summary>
    MainThread,

    /// <summary>
    /// The carrier runs on a worker thread and reproduces
    /// <c>n_cst_thread_task_sqlbase_ds_mt</c>, adding the four cancellation-aware event overrides at
    /// <c>n_cst_thread_task_sqlbase_ds_mt.sru:L27</c>, <c>:L30</c>, <c>:L47</c> and <c>:L63</c>.
    /// </summary>
    WorkerThread,
}

#endregion

#region The SQL-preview statement kind

/// <summary>
/// The kind of statement a SQL preview is reporting, reproducing the PowerBuilder
/// <c>dwsqlpreviewtype</c> values the legacy carriers actually compare against.
/// </summary>
/// <remarks>
/// <para>
/// Exactly four members, because exactly four are named in the two carrier sources and no fifth is
/// invented: <c>PreviewUpdate!</c> and <c>PreviewInsert!</c> at
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L168</c>,
/// <c>PreviewSelect!</c> at <c>n_cst_thread_task_sqlbase_ds_mt.sru:L31</c> and
/// <c>PreviewDelete!</c> at <c>:L32</c>.
/// </para>
/// <para>
/// C-K. NO NUMERIC ENCODING IS ASSERTED. The legacy compares these symbolically at every one of
/// those four sites, no value of this kind crosses a service boundary, none is written to a log and
/// none appears in a characterization recording, so there is nothing here for a numeric value to be
/// faithful to. That is the opposite of the constant sets the refactor plan requires be preserved
/// digit for digit - those are observable, and these are not - and it is why this enumeration
/// carries no explicit values while the notify-code enumerations below carry theirs.
/// </para>
/// </remarks>
internal enum SqlPreviewType
{
    /// <summary>
    /// A retrieval statement. The worker override short-circuits its entire progress-counting block
    /// on this kind [<c>n_cst_thread_task_sqlbase_ds_mt.sru:L31</c>], and the main-thread override
    /// performs no N-char rewrite for it [<c>n_cst_thread_task_sqlbase_ds.sru:L168</c>].
    /// </summary>
    Select,

    /// <summary>
    /// An insert statement. One of the two kinds the N-char rewrite applies to
    /// [<c>n_cst_thread_task_sqlbase_ds.sru:L168</c>].
    /// </summary>
    Insert,

    /// <summary>
    /// An update statement. The other kind the N-char rewrite applies to
    /// [<c>n_cst_thread_task_sqlbase_ds.sru:L168</c>].
    /// </summary>
    Update,

    /// <summary>
    /// A delete statement. Combined with a <c>Primary!</c> or <c>Filter!</c> buffer this selects the
    /// uncounted delete-then-insert arm [<c>n_cst_thread_task_sqlbase_ds_mt.sru:L32-L35</c>].
    /// </summary>
    Delete,
}

#endregion

#region The codec discriminator - DataWindow.Processing

/// <summary>
/// The carrier's processing kind, which is the discriminator that chooses between the full-state and
/// changeset serialization paths.
/// </summary>
/// <param name="Value">
/// The numeric processing kind, as <c>Long(Describe("DataWindow.Processing"))</c> yields it.
/// </param>
/// <remarks>
/// <para>
/// Reproduces the dispatch at
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L93</c>:
/// </para>
/// <code>
/// choose case Long(Data.Describe("DataWindow.Processing"))
///     case 4,5 //Crosstab or Composite datawindow
///         ... GetFullState ...
///     case else
///         ... GetChanges ...
/// end choose
/// </code>
/// <para>
/// The identical dispatch recurs at <c>:L560</c>, where it decides whether the sort and filter
/// conditions must be synchronized before transfer, so the two arms below are the same two paths
/// <c>Buffers/FullStateCodec.cs</c> and <c>Buffers/ChangesetCodec.cs</c> implement. The discriminator
/// lives here, on the carrier, because it is a QUERY AGAINST THE CARRIER; exposing it as a named
/// typed value is what stops each codec from re-parsing a describe string and disagreeing with the
/// other about the answer.
/// </para>
/// <para>
/// C-K - WHY THIS IS A WRAPPED VALUE AND NOT AN ENUMERATION OF ALL PROCESSING KINDS. The legacy
/// names exactly two of them, in its own inline comment at <c>:L94</c>, and says nothing whatever
/// about the rest. An enumeration listing every PowerBuilder processing kind would be a claim this
/// repository cannot substantiate, and a wrong member name in it would be indistinguishable from a
/// correct one. A value object with the two named constants and an explicit "everything else"
/// predicate asserts only what the oracle asserts.
/// </para>
/// </remarks>
internal readonly record struct DataWindowProcessing(long Value)
{
    /// <summary>
    /// The crosstab processing kind, <c>4</c>, named by the legacy's own comment at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L94</c>.
    /// </summary>
    internal const long CrosstabValue = 4L;

    /// <summary>
    /// The composite processing kind, <c>5</c>, named by the same comment at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L94</c>.
    /// </summary>
    internal const long CompositeValue = 5L;

    /// <summary>
    /// A crosstab carrier, which selects the full-state transfer path.
    /// </summary>
    internal static DataWindowProcessing Crosstab => new(CrosstabValue);

    /// <summary>
    /// A composite carrier, which selects the full-state transfer path.
    /// </summary>
    internal static DataWindowProcessing Composite => new(CompositeValue);

    /// <summary>
    /// The processing kind of a carrier whose data object has not been assigned, which is what
    /// <c>Describe</c> reports before one is.
    /// </summary>
    /// <remarks>
    /// Zero rather than a synthetic sentinel, because zero already lands in the legacy's
    /// <c>case else</c> arm and therefore already means "use the changeset path" - which is exactly
    /// what an unassigned carrier must do. Inventing a distinguishable "unknown" member would create
    /// a state the legacy dispatch has no arm for.
    /// </remarks>
    internal static DataWindowProcessing Unassigned => new(0L);

    /// <summary>
    /// Whether this processing kind selects the full-state transfer path rather than the changeset
    /// path.
    /// </summary>
    /// <value>
    /// <see langword="true"/> for <see cref="CrosstabValue"/> and <see cref="CompositeValue"/> only;
    /// <see langword="false"/> for every other value, reproducing the legacy's <c>case else</c> arm.
    /// </value>
    /// <remarks>
    /// <para>
    /// The full-state path carries two of the four preserved cross-thread transfer defects and the
    /// changeset path carries the other two, which is precisely why this predicate has to be exact.
    /// Full state requires the sort and filter conditions to be synchronized
    /// [<c>n_cst_thread_task_sqlquery.sru:L562-L563</c>] and crashes when a crosstab has too many
    /// columns [<c>:L672-L674</c>]; a changeset needs neither condition and is faster without them
    /// [<c>:L577-L578</c>] but may lose rows on a sorted carrier spanning more than one chunk
    /// [<c>:L147-L149</c>], and <c>Reset</c> must not be used to clear data between chunks because
    /// doing so makes the changeset fail to apply [<c>:L176-L177</c>].
    /// </para>
    /// </remarks>
    internal bool SelectsFullStateTransfer => Value is CrosstabValue or CompositeValue;

    /// <summary>
    /// Converts the raw result of <c>Describe("DataWindow.Processing")</c> into a processing kind.
    /// </summary>
    /// <param name="describeResult">
    /// The describe result verbatim, including PowerBuilder's own error markers. May be
    /// <see langword="null"/>.
    /// </param>
    /// <returns>The processing kind, or <see cref="Unassigned"/> when the value is not a number.</returns>
    /// <remarks>
    /// <para>
    /// Reproduces <c>Long(Data.Describe("DataWindow.Processing"))</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L93</c>]. PowerBuilder's
    /// <c>Describe</c> answers <c>"!"</c> when the property cannot be read and <c>"?"</c> when the
    /// value is inconsistent across a selection - the framework itself screens for exactly those two
    /// markers elsewhere [<c>n_cst_thread_task_sqlbase.sru:L562, L564</c>] - and neither is a number,
    /// so both fall through the numeric conversion and reach the legacy's <c>case else</c> arm. This
    /// method therefore answers <see cref="Unassigned"/> for them, which lands in the same arm.
    /// </para>
    /// <para>
    /// Parsed with <see cref="CultureInfo.InvariantCulture"/> and
    /// <see cref="NumberStyles.Integer"/>: the input is a machine-generated property value, never a
    /// user-facing or localized number, so culture-sensitive parsing would introduce a dependency on
    /// ambient state that the legacy conversion does not have.
    /// </para>
    /// </remarks>
    internal static DataWindowProcessing FromDescribe(string? describeResult)
    {
        if (long.TryParse(
                describeResult,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long parsed))
        {
            return new DataWindowProcessing(parsed);
        }

        return Unassigned;
    }
}

#endregion

#region The two per-task notify codes - separate namespaces, never merged

/// <summary>
/// The notification codes a SQL QUERY task publishes as the first argument of its notify callback.
/// </summary>
/// <remarks>
/// <para>
/// Ported from <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L28-L33</c>,
/// where they are declared as <c>Constant Long NCD_*</c> members of the query tasking class.
/// </para>
/// <para>
/// C-K - THE VALUE 1 MEANS TWO DIFFERENT THINGS IN TWO DIFFERENT TASK TYPES, which is why there are
/// two enumerations here and not one. In the legacy each set is scoped to its own class, so
/// <c>n_cst_threading_task_sqlquery.NCD_MAXROWS</c> and
/// <c>n_cst_threading_task_sqlupdate.NCD_PROGRESS</c> are both 1 and can never be confused; the
/// worker carrier reaches each one through its owning class name at
/// <c>n_cst_thread_task_sqlbase_ds_mt.sru:L39</c> and <c>:L65</c>. Two separate enumerations
/// reproduce that scoping and make conflating them structurally impossible. THEY ARE NEVER MERGED
/// AND NEVER RENUMBERED: the numeric values travel in the notification stream, so a renumbering
/// would silently invalidate every stored characterization recording.
/// </para>
/// <para>
/// C-K - THE IDENTIFIERS ARE RE-CASED AND THE VALUES ARE NOT. The legacy spellings are
/// <c>NCD_MAXROWS</c>, <c>NCD_DATARECEIVED</c>, <c>NCD_PAGERECEIVED</c>, <c>NCD_DATACHUNK</c>,
/// <c>NCD_CHILDRECEIVED</c> and <c>NCD_CHILDQUERY</c>. The repository-root <c>.editorconfig</c>
/// scopes its CA1707 and IDE1006 relaxations to a named list of files that carry preserved shouty
/// identifiers, and this file is deliberately not on it, so an underscored member here would be a
/// build error under warnings-as-errors. Re-casing the identifier changes nothing observable; the
/// numeric values, which are observable, are preserved exactly.
/// </para>
/// </remarks>
internal enum SqlQueryTaskNotifyCode : long
{
    /// <summary>
    /// <c>NCD_MAXROWS = 1</c>. The retrieve has passed the configured row cap. The payload is the
    /// offending ROW NUMBER, passed straight through with no word packing
    /// [<c>n_cst_thread_task_sqlbase_ds_mt.sru:L65</c>].
    /// </summary>
    MaxRows = 1L,

    /// <summary>
    /// <c>NCD_DATARECEIVED = 2</c>. The payload is the row count of the whole result, per the
    /// legacy's own comment <c>lparam:row count</c>.
    /// </summary>
    DataReceived = 2L,

    /// <summary>
    /// <c>NCD_PAGERECEIVED = 3</c>. The payload packs two words, per the legacy's own comment
    /// <c>lparam:(low word:page count,high word:record count)</c>.
    /// </summary>
    PageReceived = 3L,

    /// <summary>
    /// <c>NCD_DATACHUNK = 4</c>. The payload packs two words, per the legacy's own comment
    /// <c>lparam:(low word:count,high word:current)</c>, matching the
    /// <c>MakeLong(count,current)</c> call at <c>n_cst_threading_task_sqlquery.sru:L254</c>.
    /// </summary>
    DataChunk = 4L,

    /// <summary>
    /// <c>NCD_CHILDRECEIVED = 5</c>. Carries the column name in the string argument rather than in
    /// the numeric payload, per the legacy's own comment <c>sparam:colname</c>.
    /// </summary>
    ChildReceived = 5L,

    /// <summary>
    /// <c>NCD_CHILDQUERY = 6</c>. Carries a column id in the numeric payload and the column name in
    /// the string argument, per the legacy's own comment <c>lparam:colid,sparam:colname</c>.
    /// </summary>
    ChildQuery = 6L,
}

/// <summary>
/// The notification codes a SQL UPDATE task publishes as the first argument of its notify callback.
/// </summary>
/// <remarks>
/// <para>
/// Ported from <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L32</c>, the
/// single member <c>Constant Long NCD_PROGRESS = 1</c>. Deliberately a SEPARATE enumeration from
/// <see cref="SqlQueryTaskNotifyCode"/> - see the remarks there for why the shared value 1 makes
/// merging them unsafe.
/// </para>
/// </remarks>
internal enum SqlUpdateTaskNotifyCode : long
{
    /// <summary>
    /// <c>NCD_PROGRESS = 1</c>. Update progress. The payload packs two words, per the legacy's own
    /// comment <c>lparam:(low word:current,high word:total)</c>, matching the
    /// <c>MakeLong(_nUpdateCurrent,_nUpdateTotal)</c> call at
    /// <c>n_cst_thread_task_sqlbase_ds_mt.sru:L39</c>.
    /// </summary>
    Progress = 1L,
}

#endregion

#region The parent-task callback surface - #ParentTask and #ParentThread, narrowed to what is used

/// <summary>
/// The parent SQL task, narrowed to the members a result carrier actually consumes from it.
/// </summary>
/// <remarks>
/// <para>
/// Stands in for the two <c>privatewrite</c> references the carrier holds -
/// <c>#ParentTask</c> of type <c>n_cst_thread_task_sqlbase</c> and <c>#ParentThread</c> of type
/// <c>n_cst_thread</c> - declared at
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L39-L40</c> and captured
/// together by <c>oninit</c> at <c>:L62-L63</c>, where <c>#ParentThread</c> is derived from
/// <c>parentTask.#ParentThread</c>.
/// </para>
/// <para>
/// C-K - WHY AN ABSTRACTION RATHER THAN A CONCRETE TYPE. <c>n_cst_thread_task_sqlbase</c> maps onto
/// <c>Tasks/SqlTaskBase.cs</c>, a SIBLING FOLDER this file must not depend on: the folder's
/// foundational position requires that it consume only <c>Buffers/ItemStatus.cs</c>, the published
/// contracts and the shared kernel. Declaring the consumed surface as an interface is the same
/// technique the refactor plan applies to the DataServices inheritance edge, where a service defines
/// its own abstract host contract carrying only the members it actually uses from a parent it may
/// not port. Five members is the whole of it, and each is traced to its call site below.
/// </para>
/// <para>
/// It is also what keeps the database-error hook free of <c>Errors/DbErrorData.cs</c>. In the legacy
/// the carrier forwards five raw scalars and the PARENT TASK assembles the <c>dberrordata</c>
/// structure from them [<c>n_cst_thread_task_sqlbase_ds.sru:L159</c> forwarding into
/// <c>n_cst_thread_task_sqlbase.sru:L85</c>], so a callback over those same five scalars is both the
/// dependency-free shape and the faithful one.
/// </para>
/// </remarks>
internal interface ICarrierParentTask
{
    /// <summary>
    /// Whether the parent task's thread is the main thread.
    /// </summary>
    /// <remarks>
    /// Reproduces <c>#ParentThread.of_IsMainThread()</c>, reached through the parent task exactly as
    /// <c>oninit</c> derives <c>#ParentThread</c> from <c>parentTask.#ParentThread</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L63</c>]. It is the
    /// predicate the carrier-selection branch asks
    /// [<c>n_cst_thread_task_sqlbase.sru:L553</c>] and the one that gates the no-serialization
    /// handover [<c>n_cst_thread_task_sqlquery.sru:L84</c>].
    /// </remarks>
    bool IsMainThread { get; }

    /// <summary>
    /// Whether the connection is configured for N-char binding, which is the only condition under
    /// which the carrier rewrites a previewed statement.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reproduces <c>of_IsNCharBinding()</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L524</c>], consumed by
    /// the carrier at <c>n_cst_thread_task_sqlbase_ds.sru:L169</c>.
    /// </para>
    /// <para>
    /// C-K - CONTEXT WORTH KNOWING, AND NOT THIS FILE'S TO FIX. The flag is set true only when BOTH
    /// <c>DisableBind=1</c> AND <c>NCharBind=1</c> appear in the connection parameter string, a
    /// nested condition at <c>n_cst_thread_task_sqlbase.sru:L127-L132</c>. <c>DisableBind=1</c> means
    /// the runtime does not use bind variables - it interpolates literals - which the refactor plan
    /// identifies as the MECHANICAL ROOT of the legacy SQL-injection exposure. The N-char rewriter is
    /// therefore reachable ONLY on the interpolating path. The exposure is documented as a known
    /// legacy defect and remediated where the statements are built, not here.
    /// </para>
    /// </remarks>
    bool IsNCharBinding { get; }

    /// <summary>
    /// Whether the parent task has been cancelled.
    /// </summary>
    /// <remarks>
    /// Reproduces <c>of_IsCancelled()</c>, which every one of the worker carrier's four overrides
    /// tests immediately after calling its ancestor
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L27, L30, L49,
    /// L63</c>].
    /// </remarks>
    bool IsCancelled { get; }

    /// <summary>
    /// Forwards a database error to the parent task, which is what assembles the structured payload
    /// from these five scalars.
    /// </summary>
    /// <param name="sqlDbCode">The database's own error code.</param>
    /// <param name="sqlErrText">The database's error text.</param>
    /// <param name="sqlSyntax">
    /// The statement that failed. IT CARRIES INTERPOLATED LITERAL VALUES on the interpolating path
    /// and is therefore forwarded, never logged, by any member of this file.
    /// </param>
    /// <param name="buffer">The buffer the offending row sits in.</param>
    /// <param name="row">The offending row number, one-based.</param>
    /// <returns>
    /// The value the carrier's own database-error event returns. Zero lets the runtime report the
    /// error in its default way; one suppresses that reporting.
    /// </returns>
    /// <remarks>
    /// Reproduces
    /// <c>return #ParentTask.Event OnDBError(sqldbcode,sqlerrtext,sqlsyntax,buffer,row)</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L159</c>], argument for
    /// argument and in the same order.
    /// </remarks>
    long OnDbError(long sqlDbCode, string sqlErrText, string sqlSyntax, DwBuffer buffer, long row);

    /// <summary>
    /// Raises a task notification on the parent task.
    /// </summary>
    /// <param name="notifyCode">
    /// The per-task notification code. A query task's codes are
    /// <see cref="SqlQueryTaskNotifyCode"/> and an update task's are
    /// <see cref="SqlUpdateTaskNotifyCode"/>; the two sets overlap numerically and are never merged.
    /// </param>
    /// <param name="payload">
    /// The numeric payload. Either a plain value, as the row number at
    /// <c>n_cst_thread_task_sqlbase_ds_mt.sru:L65</c> is, or two 16-bit words packed by
    /// <c>MakeLong</c>, as the progress pair at <c>:L39</c> is.
    /// </param>
    /// <param name="text">
    /// The string argument. Both carrier call sites pass the empty string; the query task's own
    /// child-column notifications are the ones that use it.
    /// </param>
    /// <returns>
    /// One to ask the caller to stop, anything else to continue - EXCEPT on the row-cap
    /// notification, where one means the opposite. See the row-cap override for that preserved
    /// inversion.
    /// </returns>
    /// <remarks>
    /// Reproduces <c>#ParentTask.Event OnNotify(wparam,lparam,sparam)</c> as called at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L39</c> and
    /// <c>:L65</c>. The payload is typed <see cref="long"/> rather than <see cref="uint"/> because
    /// the legacy passes a plain row number through the same argument at <c>:L65</c> that it passes
    /// a packed pair through at <c>:L39</c>; widening the parameter loses nothing, because the
    /// packing itself - and its truncation - happens before the call.
    /// </remarks>
    long OnNotify(long notifyCode, long payload, string text);
}

#endregion

#region One row of a carrier buffer

/// <summary>
/// One row held in a carrier buffer: its item status, its per-column item statuses, its current
/// column values and the original values the optimistic-concurrency where clause compares against.
/// </summary>
/// <remarks>
/// <para>
/// This is the unit of state a naive rowset would discard. <c>updatewhere=1</c> on the golden-master
/// fixture [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14</c>] combined with
/// <c>updatewhereclause=yes</c> on all six columns [<c>:L8-L13</c>] means the generated update
/// statement's where clause carries the key column PLUS THE ORIGINAL VALUE OF EVERY UPDATEABLE
/// COLUMN, so a row that remembers only its current values cannot express the concurrency check at
/// all.
/// </para>
/// <para>
/// C-K - EXPLICIT ORIGINAL-VALUE AND STATUS TRACKING IS MANDATED, NOT CHOSEN. The legacy relies on
/// PowerBuilder's hidden per-item state, and exploits it: at
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L155-L167</c> it ASSIGNS A
/// KEY COLUMN TO ITSELF, gated on the runtime value of the key-in-place setting, purely to flip that
/// hidden state so the delete-and-insert pair is generated. The accompanying comment at
/// <c>:L151-L154</c> documents the whole thing as a fix for the case where the requested key field is
/// not marked as a key. There is no .NET analogue for a self-assignment that mutates hidden state, so
/// the refactor plan requires the port model original-value and status tracking EXPLICITLY. That is
/// what this type is. Consequently <see cref="SetValue"/> deliberately does NOT touch any status:
/// every status change in this port is an explicit call, which is what makes the workaround
/// expressible at all.
/// </para>
/// <para>
/// Column values are typed <see cref="object"/> because the legacy carrier reaches them through the
/// PowerBuilder <c>GetItemString</c>/<c>Number</c>/<c>Date</c>/<c>DateTime</c>/<c>Time</c>/
/// <c>Decimal</c> family, whose union the refactor plan maps onto <c>object?</c>. Collapsing the
/// family into one object-typed pair keeps this file dialect-agnostic and schema-free (C-E): nothing
/// here knows the fixture's six columns, their types, or that a database exists.
/// </para>
/// <para>
/// Storage is sparse on purpose. A carrier row may be materialised by a codec that decoded only some
/// columns, and the absent ones must read as absent rather than as a fabricated default.
/// </para>
/// </remarks>
internal sealed class CarrierRow
{
    private readonly Dictionary<int, object?> _values = [];
    private readonly Dictionary<int, object?> _originalValues = [];
    private readonly Dictionary<int, ItemStatus> _columnStatuses = [];

    /// <summary>
    /// Creates a row carrying the supplied item status and no column values.
    /// </summary>
    /// <param name="status">
    /// The row's initial item status. A retrieved row is
    /// <see cref="ItemStatus.NotModified"/>; a row a codec materialises for transfer is stamped
    /// <see cref="ItemStatus.DataModified"/>, which is what the legacy does at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L123, L127, L162, L168,
    /// L197, L202</c>.
    /// </param>
    internal CarrierRow(ItemStatus status)
    {
        Status = status;
    }

    /// <summary>
    /// The row's own item status - the value a status call addressed with the column-zero sentinel
    /// reads and writes.
    /// </summary>
    /// <remarks>
    /// Read the legacy way at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L56</c>
    /// (<c>GetItemStatus(nRow,0,Delete!)</c>) and written the legacy way at
    /// <c>n_cst_thread_task_sqlquery.sru:L123</c>
    /// (<c>SetItemStatus(nRow,0,Primary!,DataModified!)</c>). The four legal values are the published
    /// <see cref="ItemStatus"/> members and this port adds none.
    /// </remarks>
    internal ItemStatus Status { get; set; }

    /// <summary>
    /// The one-based numbers of the columns this row has a value for, in ascending order.
    /// </summary>
    /// <remarks>
    /// Ordered deliberately. A dictionary's key order is unspecified, and a codec that serialized
    /// columns in enumeration order would then produce a payload whose byte layout varied between
    /// runs - which would break the characterization comparison that the whole parity model rests on
    /// (AAP 0.6.7). Ascending column number is the fixture's own order
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L21-L26</c> declares ids 1 through 6].
    /// </remarks>
    internal IEnumerable<int> AssignedColumnNumbers => _values.Keys.Order();

    /// <summary>
    /// Reads a column's current value.
    /// </summary>
    /// <param name="columnNumber">The one-based column number. R9: never rebased.</param>
    /// <returns>The current value, or <see langword="null"/> when the column has none.</returns>
    internal object? GetValue(int columnNumber)
    {
        return _values.TryGetValue(columnNumber, out object? value) ? value : null;
    }

    /// <summary>
    /// Reads the column value the optimistic-concurrency where clause must compare against.
    /// </summary>
    /// <param name="columnNumber">The one-based column number. R9: never rebased.</param>
    /// <returns>
    /// The value the column held at the last baseline, or its current value when it has not been
    /// modified since - which is the same answer, because an unmodified column's original value IS
    /// its current value.
    /// </returns>
    /// <remarks>
    /// This is the managed equivalent of passing the PowerBuilder <c>originalvalue</c> flag to a
    /// <c>GetItem*</c> call, and it is the value <c>Concurrency/UpdateWhereBuilder.cs</c> needs for
    /// every column marked <c>updatewhereclause=yes</c>
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L13</c>].
    /// </remarks>
    internal object? GetOriginalValue(int columnNumber)
    {
        return _originalValues.TryGetValue(columnNumber, out object? original)
            ? original
            : GetValue(columnNumber);
    }

    /// <summary>
    /// Writes a column's current value, capturing its original on the first write since the last
    /// baseline.
    /// </summary>
    /// <param name="columnNumber">The one-based column number. R9: never rebased.</param>
    /// <param name="value">The new value. <see langword="null"/> is a legal value, not an absence.</param>
    /// <remarks>
    /// DELIBERATELY DOES NOT MUTATE ANY ITEM STATUS. See the type-level remarks: the legacy exploits
    /// PowerBuilder's implicit status flip with a self-assignment, and the refactor plan requires the
    /// port express status changes explicitly instead, so a status change here is always an explicit
    /// <see cref="DataWindowBufferStore.SetItemStatus"/> call.
    /// </remarks>
    internal void SetValue(int columnNumber, object? value)
    {
        // Capture the original exactly once per baseline period. A second write must not overwrite
        // the captured original, or the concurrency check would compare against an intermediate
        // value the database never saw.
        if (!_originalValues.ContainsKey(columnNumber))
        {
            _originalValues[columnNumber] = GetValue(columnNumber);
        }

        _values[columnNumber] = value;
    }

    /// <summary>
    /// Reads one column's item status.
    /// </summary>
    /// <param name="columnNumber">The one-based column number. R9: never rebased.</param>
    /// <returns>
    /// The column's status, or <see cref="ItemStatus.NotModified"/> when none has been stamped.
    /// </returns>
    /// <remarks>
    /// The only measured column-level status read in the legacy is
    /// <c>if Data.GetItemStatus(nRow,nKeyColumns[nIndex],Primary!) &lt;&gt; DataModified! then
    /// continue</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L162</c>], which asks
    /// only whether a key column was modified. <see cref="ItemStatus.NotModified"/> is therefore the
    /// right default: it is the status a freshly retrieved column carries, and it is the answer that
    /// makes that comparison behave as it does on a row nobody has edited.
    /// </remarks>
    internal ItemStatus GetColumnStatus(int columnNumber)
    {
        return _columnStatuses.TryGetValue(columnNumber, out ItemStatus status)
            ? status
            : ItemStatus.NotModified;
    }

    /// <summary>
    /// Stamps one column's item status.
    /// </summary>
    /// <param name="columnNumber">The one-based column number. R9: never rebased.</param>
    /// <param name="status">The status to stamp.</param>
    /// <remarks>
    /// NO TRANSITION TABLE IS REPRODUCED. PowerBuilder restricts which status changes
    /// <c>SetItemStatus</c> accepts, but this repository contains no statement of those rules and
    /// every measured call site stamps the single value <c>DataModified!</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L123, L127, L162, L168,
    /// L197, L202</c>]. Inventing a table would add a failure mode the oracle does not have, which
    /// C-B forbids; the omission is recorded here rather than left to be discovered.
    /// </remarks>
    internal void SetColumnStatus(int columnNumber, ItemStatus status)
    {
        _columnStatuses[columnNumber] = status;
    }

    /// <summary>
    /// Re-baselines the row: its original values become its current values, its per-column statuses
    /// are cleared and its own status becomes <see cref="ItemStatus.NotModified"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the row-level half of <see cref="DataWindowBufferStore.ResetUpdate"/>, and the legacy
    /// invokes that operation immediately after data lands - <c>Data.ResetUpdate()</c> at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L240</c>, and
    /// <c>ds.ResetUpdate()</c> at <c>:L223</c> once the last chunk has been applied - which is
    /// precisely what makes freshly delivered rows read as unmodified with their originals equal to
    /// their currents.
    /// </para>
    /// <para>
    /// Clearing the captured originals rather than copying the current values into them is
    /// deliberate: <see cref="GetOriginalValue"/> already falls back to the current value, so an
    /// empty capture set MEANS "original equals current" and cannot drift out of step with it.
    /// </para>
    /// </remarks>
    internal void Baseline()
    {
        _originalValues.Clear();
        _columnStatuses.Clear();
        Status = ItemStatus.NotModified;
    }
}

#endregion

#region The `datastore` ancestor - the three-buffer model the carriers inherit

/// <summary>
/// The three-buffer DataWindow model the legacy result carriers inherit: <c>Primary!</c>,
/// <c>Delete!</c> and <c>Filter!</c>, their item statuses, their counts, the modified-row walk, row
/// movement and discard, the two distinct reset operations, SQL-preview interception and the
/// processing kind that chooses a serialization path.
/// </summary>
/// <remarks>
/// <para>
/// Stands in for the PowerBuilder <c>datastore</c> ancestor of
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L8</c>, which declares
/// <c>global type n_cst_thread_task_sqlbase_ds from datastore</c>. Everything here is a member the
/// two carriers INHERIT and call rather than declare, which is exactly why none of it appears in
/// <c>_ds.sru</c>'s own prototype list at <c>:L52-L60</c>.
/// </para>
/// <para>
/// THE MEMBER SET IS THE MEASURED ONE. Each member below is called by name somewhere in the seven
/// legacy sources this file was ported from, and the surface the refactor plan enumerates for the
/// cross-thread transfer paths - the three buffers, item-status get and set including the
/// column-zero row-status convention, the modified, deleted and filtered counts, row movement and
/// discard, next-modified traversal and SQL-preview interception - is exactly this set. Nothing is
/// added for symmetry.
/// </para>
/// <para>
/// C-D. No presentational member appears. <c>SetRedraw</c> and <c>GroupCalc</c> sit beside these very
/// operations in the legacy and ONLY in its DataWindow arms
/// [<c>n_cst_threading_task_sqlquery.sru:L185-L208</c>], never in its structurally identical DataStore
/// arms [<c>:L212-L228</c>]; that asymmetry is the evidence they are presentational and belong to the
/// deferred DesignSystem area. <see cref="ResetUpdate"/> appears in BOTH arms and is therefore kept.
/// </para>
/// <para>
/// C-H. No storage, no connection, no clock and no I/O of any kind, so the whole buffer model is
/// exercisable with no database and no container - which is what makes the per-service line-coverage
/// gate attainable for it.
/// </para>
/// </remarks>
internal class DataWindowBufferStore
{
    /// <summary>
    /// The value PowerBuilder's DataStore functions return on success.
    /// </summary>
    /// <remarks>
    /// C-K - THIS IS DELIBERATELY NOT <c>RetCode.OK</c>, AND CONFUSING THE TWO WOULD BE A SILENT
    /// DEFECT. The framework's own return-code algebra puts SUCCESS AT ZERO
    /// [<c>ws_objects/pfw.shared.pbl.src/retcode.sru</c>], while the PowerBuilder DataStore functions
    /// this region reproduces - <c>Reset</c>, <c>ResetUpdate</c>, <c>RowsMove</c>,
    /// <c>RowsDiscard</c>, <c>SetItemStatus</c>, <c>SetSqlPreview</c> - answer 1 for success and -1
    /// for failure. The legacy keeps both conventions in play simultaneously and checks each against
    /// its own: <c>n_cst_thread_task_sqlquery.sru:L570</c> tests <c>SetSort(...) &lt;&gt; 1</c> and then
    /// returns <c>RetCode.E_INVALID_ARGUMENT</c> in the same breath. Naming the DataStore convention
    /// separately is what stops a reader - or a later edit - from testing one against the other.
    /// </remarks>
    internal const long DataStoreSuccess = 1L;

    /// <summary>
    /// The value PowerBuilder's DataStore functions return on failure. See
    /// <see cref="DataStoreSuccess"/> for why this is not <c>RetCode.FAILED</c>, which happens to
    /// share the value but not the convention.
    /// </summary>
    internal const long DataStoreFailure = -1L;

    /// <summary>
    /// The value a carrier event returns to let processing continue.
    /// </summary>
    /// <remarks>
    /// Zero, which is also what a PowerBuilder event script with no <c>return</c> statement yields,
    /// and what <c>_ds.sqlpreview</c> returns unconditionally at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L173</c>.
    /// </remarks>
    internal const long EventContinue = 0L;

    /// <summary>
    /// The value a carrier event returns to stop processing.
    /// </summary>
    /// <remarks>
    /// One, as returned by every cancellation check in the worker carrier
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L27, L30, L49,
    /// L63</c>] and by the row-cap enforcement arm at <c>:L70</c>. NOTE THE ONE PLACE WHERE A
    /// HANDLER'S 1 MEANS THE OPPOSITE: see <see cref="WorkerDataWindowCarrier.OnRetrieveRow"/>.
    /// </remarks>
    internal const long EventStop = 1L;

    private readonly List<CarrierRow> _primaryBuffer = [];
    private readonly List<CarrierRow> _deleteBuffer = [];
    private readonly List<CarrierRow> _filterBuffer = [];

    /// <summary>
    /// The carrier's processing kind, which selects the full-state or changeset serialization path.
    /// </summary>
    /// <remarks>
    /// Assigned from <c>Describe("DataWindow.Processing")</c> through
    /// <see cref="DataWindowProcessing.FromDescribe"/> by whichever layer assigns the data object.
    /// The golden-master fixture declares <c>processing=1</c>
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L3</c>], so the fixture takes the CHANGESET
    /// path and the full-state path is the exceptional one.
    /// </remarks>
    internal DataWindowProcessing Processing { get; set; } = DataWindowProcessing.Unassigned;

    /// <summary>
    /// Whether this carrier must be transferred as a full-state image rather than as a changeset.
    /// </summary>
    /// <remarks>
    /// THIS IS THE CODEC DISCRIMINATOR, exposed here so that
    /// <c>Buffers/ChangesetCodec.cs</c> and <c>Buffers/FullStateCodec.cs</c> both ask the carrier
    /// rather than each re-parsing a describe string and risking disagreement. It reproduces the
    /// <c>case 4,5</c> arm of <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L93-L94</c>.
    /// </remarks>
    internal bool RequiresFullStateTransfer => Processing.SelectsFullStateTransfer;

    /// <summary>
    /// The replacement statement most recently installed by <see cref="SetSqlPreview"/>, or
    /// <see langword="null"/> when none has been.
    /// </summary>
    /// <remarks>
    /// This is where the N-char rewrite lands. <c>SetSqlPreview</c> is the PowerBuilder function that
    /// substitutes the statement about to be executed, and the carrier's SQL-preview event calls it
    /// with the rewritten text at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L170</c>. Exposing the
    /// installed value is what makes that rewrite observable without a database.
    /// </remarks>
    internal string? SqlPreviewStatement { get; private set; }

    #region Counts

    /// <summary>
    /// The number of rows in the <c>Primary!</c> buffer.
    /// </summary>
    /// <returns>The primary row count, never negative.</returns>
    /// <remarks>
    /// Reproduces <c>RowCount()</c> as called at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L109, L145, L159, L195</c>
    /// and <c>n_cst_thread_task_sqlupdate.sru:L158, L228</c>. Kept as a METHOD rather than turned into
    /// a property so that the four counts read at a call site exactly as their legacy originals do,
    /// which is what lets a reader diff a ported loop against its locator line for line.
    /// </remarks>
    internal long RowCount()
    {
        return _primaryBuffer.Count;
    }

    /// <summary>
    /// The number of rows in the <c>Filter!</c> buffer.
    /// </summary>
    /// <returns>The filtered row count, never negative.</returns>
    /// <remarks>
    /// Reproduces <c>FilteredCount()</c> as called at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L109, L145, L164, L165,
    /// L199, L200</c> and <c>n_cst_thread_task_sqlupdate.sru:L236</c>. Note that the chunk arithmetic
    /// at <c>:L145</c> SUMS this with <see cref="RowCount"/>, so a heavily filtered result produces
    /// more chunks than its row count alone would suggest - which is observable in the chunk indices
    /// the query contract publishes.
    /// </remarks>
    internal long FilteredCount()
    {
        return _filterBuffer.Count;
    }

    /// <summary>
    /// The number of rows in the <c>Delete!</c> buffer.
    /// </summary>
    /// <returns>The deleted row count, never negative.</returns>
    /// <remarks>
    /// Reproduces <c>DeletedCount()</c>, which the worker carrier uses as the UPPER bound of its
    /// backward walk at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L55</c>.
    /// </remarks>
    internal long DeletedCount()
    {
        return _deleteBuffer.Count;
    }

    /// <summary>
    /// The number of modified rows awaiting an update.
    /// </summary>
    /// <returns>The modified row count, never negative.</returns>
    /// <remarks>
    /// <para>
    /// Reproduces <c>ModifiedCount()</c>, whose single measured use is the seed of the update-progress
    /// total: <c>_nUpdateTotal = ModifiedCount()</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L52</c>].
    /// </para>
    /// <para>
    /// C-K - WHY THIS COUNTS THE PRIMARY AND FILTER BUFFERS AND NOT THE DELETE BUFFER, DERIVED FROM
    /// THE REPOSITORY RATHER THAN ASSUMED. The update walk visits modified rows in BOTH the primary
    /// and the filter buffer - <c>GetNextModified(0,Primary!)</c> at
    /// <c>n_cst_threading_task_sqlupdate.sru:L172</c> and <c>GetNextModified(0,Filter!)</c> at
    /// <c>:L188</c> for the DataStore arm, mirrored at <c>:L138</c> and <c>:L154</c> for the
    /// DataWindow arm - so rows in either buffer produce statements and both belong in the
    /// denominator. Deleted rows are the ones that do NOT: the worker carrier adds their contribution
    /// SEPARATELY, in the backward walk at <c>_ds_mt.sru:L55-L60</c>, which would be redundant if this
    /// count already covered them. The separate addition is therefore the evidence that this count
    /// excludes the delete buffer.
    /// </para>
    /// <para>
    /// A row counts when <see cref="ItemStatusMachine.IsModified"/> says so, delegated rather than
    /// re-derived so that the modified-status definition lives in exactly one place.
    /// </para>
    /// </remarks>
    internal long ModifiedCount()
    {
        long modified = 0L;

        foreach (CarrierRow row in _primaryBuffer)
        {
            if (ItemStatusMachine.IsModified(row.Status))
            {
                modified++;
            }
        }

        foreach (CarrierRow row in _filterBuffer)
        {
            if (ItemStatusMachine.IsModified(row.Status))
            {
                modified++;
            }
        }

        return modified;
    }

    #endregion

    #region Item status - the column-zero row-status convention

    /// <summary>
    /// Reads an item status, addressing either the row itself or one of its columns.
    /// </summary>
    /// <param name="row">The one-based row number within <paramref name="buffer"/>.</param>
    /// <param name="columnIndex">
    /// <see cref="ItemStatusMachine.RowStatusColumn"/> to read the ROW's status, or a one-based
    /// column number to read that COLUMN's status.
    /// </param>
    /// <param name="buffer">The buffer to read from.</param>
    /// <returns>The item status.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="row"/> is outside 1 through the buffer's row count, <paramref name="buffer"/>
    /// is not a declared buffer, or <paramref name="columnIndex"/> is outside the legacy
    /// status-addressing domain.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Reproduces <c>GetItemStatus(row, column, buffer)</c>. The row-versus-column decision is
    /// delegated to <see cref="ItemStatusMachine.TryGetColumnNumber"/> rather than made here with a
    /// bare comparison against zero, because R9 item 4 is that THE ZERO IS A SENTINEL AND NOT AN
    /// INDEX: the decisive evidence is two adjacent lines of one legacy function, where
    /// <c>n_cst_thread_task_sqlupdate.sru:L160</c> reads the row's status with 0 and <c>:L162</c>
    /// reads a specific column's status with a one-based number from an array.
    /// </para>
    /// <para>
    /// An out-of-range row THROWS rather than answering a null status. Every measured legacy call
    /// site bounds itself by the buffer's own count -
    /// <c>_ds_mt.sru:L55</c>, <c>n_cst_thread_task_sqlupdate.sru:L158, L228, L236</c>,
    /// <c>n_cst_threading_task_sqlupdate.sru:L138-L197</c> - so an out-of-range row is a structural
    /// fault in the caller, and the refactor plan requires structural faults FAIL FAST rather than
    /// degrade.
    /// </para>
    /// </remarks>
    internal ItemStatus GetItemStatus(long row, int columnIndex, DwBuffer buffer)
    {
        CarrierRow target = RowAt(row, buffer);

        return ItemStatusMachine.TryGetColumnNumber(columnIndex, out int columnNumber)
            ? target.GetColumnStatus(columnNumber)
            : target.Status;
    }

    /// <summary>
    /// Stamps an item status, addressing either the row itself or one of its columns.
    /// </summary>
    /// <param name="row">The one-based row number within <paramref name="buffer"/>.</param>
    /// <param name="columnIndex">
    /// <see cref="ItemStatusMachine.RowStatusColumn"/> to stamp the ROW's status, or a one-based
    /// column number to stamp that COLUMN's status.
    /// </param>
    /// <param name="buffer">The buffer to write to.</param>
    /// <param name="status">The status to stamp.</param>
    /// <returns><see cref="DataStoreSuccess"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="row"/> is outside 1 through the buffer's row count, <paramref name="buffer"/>
    /// is not a declared buffer, or <paramref name="columnIndex"/> is outside the legacy
    /// status-addressing domain.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Reproduces <c>SetItemStatus(row, column, buffer, status)</c> as called at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L123, L127, L162, L168,
    /// L197, L202</c> - six sites, all of them stamping the ROW's status with <c>DataModified!</c> so
    /// that a codec will treat the row as one to transfer.
    /// </para>
    /// <para>
    /// C-B. No status-transition table is enforced and no status value is rejected; see
    /// <see cref="CarrierRow.SetColumnStatus"/> for why inventing either would add a failure mode
    /// the oracle does not have.
    /// </para>
    /// </remarks>
    internal long SetItemStatus(long row, int columnIndex, DwBuffer buffer, ItemStatus status)
    {
        CarrierRow target = RowAt(row, buffer);

        if (ItemStatusMachine.TryGetColumnNumber(columnIndex, out int columnNumber))
        {
            target.SetColumnStatus(columnNumber, status);
        }
        else
        {
            target.Status = status;
        }

        return DataStoreSuccess;
    }

    /// <summary>
    /// Returns the number of the next modified row after <paramref name="afterRow"/> in
    /// <paramref name="buffer"/>.
    /// </summary>
    /// <param name="afterRow">
    /// The row to search after. Pass <see cref="ItemStatusMachine.BeforeFirstRow"/> to start the walk,
    /// or the previous result to continue it.
    /// </param>
    /// <param name="buffer">The buffer to walk.</param>
    /// <returns>
    /// The one-based row number, or <see cref="ItemStatusMachine.NoMoreModifiedRows"/> when there is
    /// none.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="buffer"/> is not a declared buffer or <paramref name="afterRow"/> is negative.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Reproduces <c>GetNextModified(row, buffer)</c> and its <c>do while (nRow &gt; 0)</c> idiom, whose
    /// twelve measured call sites are at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L138, L147, L154, L163,
    /// L172, L181, L188, L197, L321, L328, L333, L340</c>.
    /// </para>
    /// <para>
    /// The walk itself is DELEGATED to <see cref="ItemStatusMachine.GetNextModified"/> rather than
    /// re-implemented, so the seed and terminator sentinels - both of which are zero and neither of
    /// which is an index - are audited in exactly one place (R9).
    /// </para>
    /// </remarks>
    internal long GetNextModified(long afterRow, DwBuffer buffer)
    {
        List<CarrierRow> rows = BufferOf(buffer);

        return ItemStatusMachine.GetNextModified(
            afterRow,
            buffer,
            rows.Count,
            rowNumber => RowAt(rowNumber, buffer).Status);
    }

    /// <summary>
    /// Enumerates the modified rows of <paramref name="buffer"/> in ascending row order.
    /// </summary>
    /// <param name="buffer">The buffer to walk.</param>
    /// <returns>The one-based row numbers of the modified rows.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="buffer"/> is not a declared buffer.
    /// </exception>
    /// <remarks>
    /// The same walk as <see cref="GetNextModified"/>, expressed as the loop the legacy writes out by
    /// hand at every one of its twelve call sites. Delegated to
    /// <see cref="ItemStatusMachine.EnumerateModifiedRows"/>, which is a LIVE VIEW rather than a
    /// snapshot - exactly as the legacy loop is, since it re-reads the buffer on every iteration.
    /// </remarks>
    internal IEnumerable<long> EnumerateModifiedRows(DwBuffer buffer)
    {
        List<CarrierRow> rows = BufferOf(buffer);

        return ItemStatusMachine.EnumerateModifiedRows(
            buffer,
            rows.Count,
            rowNumber => RowAt(rowNumber, buffer).Status);
    }

    #endregion

    #region Column values, current and original

    /// <summary>
    /// Reads a column's current value.
    /// </summary>
    /// <param name="row">The one-based row number within <paramref name="buffer"/>.</param>
    /// <param name="columnNumber">The one-based column number. R9: never rebased.</param>
    /// <param name="buffer">The buffer to read from.</param>
    /// <returns>The current value, or <see langword="null"/> when the column has none.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="row"/> is outside 1 through the buffer's row count,
    /// <paramref name="buffer"/> is not a declared buffer, or <paramref name="columnNumber"/> is not
    /// a column number - the row-status sentinel is rejected here, because a value is never addressed
    /// by it.
    /// </exception>
    internal object? GetItemValue(long row, int columnNumber, DwBuffer buffer)
    {
        CarrierRow target = RowAt(row, buffer);

        return target.GetValue(RequireColumnNumber(columnNumber));
    }

    /// <summary>
    /// Reads the column value the optimistic-concurrency where clause compares against.
    /// </summary>
    /// <param name="row">The one-based row number within <paramref name="buffer"/>.</param>
    /// <param name="columnNumber">The one-based column number. R9: never rebased.</param>
    /// <param name="buffer">The buffer to read from.</param>
    /// <returns>The value the column held at the last baseline.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// As <see cref="GetItemValue"/>.
    /// </exception>
    /// <remarks>
    /// The managed equivalent of the PowerBuilder <c>originalvalue</c> flag on a <c>GetItem*</c> call.
    /// This is the value <c>updatewhere=1</c> requires for every column marked
    /// <c>updatewhereclause=yes</c>, which on the golden-master fixture is ALL SIX
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14</c>].
    /// </remarks>
    internal object? GetItemOriginalValue(long row, int columnNumber, DwBuffer buffer)
    {
        CarrierRow target = RowAt(row, buffer);

        return target.GetOriginalValue(RequireColumnNumber(columnNumber));
    }

    /// <summary>
    /// Writes a column's current value, capturing its original on the first write since the last
    /// baseline.
    /// </summary>
    /// <param name="row">The one-based row number within <paramref name="buffer"/>.</param>
    /// <param name="columnNumber">The one-based column number. R9: never rebased.</param>
    /// <param name="buffer">The buffer to write to.</param>
    /// <param name="value">The new value. <see langword="null"/> is a legal value.</param>
    /// <returns><see cref="DataStoreSuccess"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// As <see cref="GetItemValue"/>.
    /// </exception>
    /// <remarks>
    /// Does NOT change any item status - see <see cref="CarrierRow.SetValue"/> for why that is
    /// mandated rather than chosen.
    /// </remarks>
    internal long SetItemValue(long row, int columnNumber, DwBuffer buffer, object? value)
    {
        CarrierRow target = RowAt(row, buffer);

        target.SetValue(RequireColumnNumber(columnNumber), value);

        return DataStoreSuccess;
    }

    #endregion

    #region Row movement, discard and admission

    /// <summary>
    /// Moves a contiguous range of rows from one buffer into a buffer of another - or the same -
    /// carrier.
    /// </summary>
    /// <param name="startRow">The one-based first row to move.</param>
    /// <param name="endRow">The one-based last row to move, inclusive.</param>
    /// <param name="moveBuffer">The buffer to move the rows out of.</param>
    /// <param name="target">The carrier to move the rows into. May be this same instance.</param>
    /// <param name="beforeRow">
    /// The one-based row in the target buffer to insert before. A value past the end appends, which
    /// is how the legacy uses it.
    /// </param>
    /// <param name="targetBuffer">The buffer to move the rows into.</param>
    /// <returns>
    /// <see cref="DataStoreSuccess"/>, or <see cref="DataStoreFailure"/> when the requested range or
    /// insertion point is not addressable.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="moveBuffer"/> or <paramref name="targetBuffer"/> is not a declared buffer.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Reproduces <c>RowsMove(startrow, endrow, movebuffer, targetdw, beforerow, targetbuffer)</c>,
    /// whose three measured call sites are
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L109</c>, which folds the
    /// whole filter buffer onto the end of the primary buffer OF THE SAME CARRIER, and <c>:L160</c>
    /// and <c>:L166</c>, which move a chunk into a temporary carrier. All three pass a range bounded
    /// by the source buffer's own count, and none checks the return value.
    /// </para>
    /// <para>
    /// AN EMPTY RANGE ANSWERS FAILURE AND MOVES NOTHING, deliberately. <c>:L109</c> passes
    /// <c>1, FilteredCount()</c>, so when nothing is filtered the range is <c>1, 0</c> - end before
    /// start. PowerBuilder answers -1 for that and moves nothing, the legacy ignores the answer, and
    /// so this returns <see cref="DataStoreFailure"/> rather than inventing a silent no-op success
    /// (C-B).
    /// </para>
    /// <para>
    /// R9. <paramref name="startRow"/>, <paramref name="endRow"/> and <paramref name="beforeRow"/> are
    /// all ONE-BASED row numbers and none is rebased here; the single conversion to a zero-based list
    /// index happens in <see cref="ZeroBasedIndexOf"/>, which is the only such site in this file.
    /// </para>
    /// </remarks>
    internal long RowsMove(
        long startRow,
        long endRow,
        DwBuffer moveBuffer,
        DataWindowBufferStore target,
        long beforeRow,
        DwBuffer targetBuffer)
    {
        ArgumentNullException.ThrowIfNull(target);

        List<CarrierRow> source = BufferOf(moveBuffer);
        List<CarrierRow> destination = target.BufferOf(targetBuffer);

        if (startRow < ItemStatusMachine.FirstRowNumber
            || endRow < startRow
            || endRow > source.Count
            || beforeRow < ItemStatusMachine.FirstRowNumber)
        {
            return DataStoreFailure;
        }

        int firstIndex = ZeroBasedIndexOf(startRow);
        int moveCount = (int)(endRow - startRow + 1L);

        // Extract before inserting. The legacy's own most important call site moves rows between two
        // buffers of ONE carrier [n_cst_thread_task_sqlquery.sru:L109], so source and destination may
        // be the same list; taking the range out first makes the insertion point below well defined
        // in every combination.
        List<CarrierRow> moved = source.GetRange(firstIndex, moveCount);
        source.RemoveRange(firstIndex, moveCount);

        // A beforeRow past the end appends, which is exactly how :L109 uses it: it passes
        // RowCount() + 1, one past the last primary row.
        int insertIndex = beforeRow > destination.Count
            ? destination.Count
            : ZeroBasedIndexOf(beforeRow);

        destination.InsertRange(insertIndex, moved);

        return DataStoreSuccess;
    }

    /// <summary>
    /// Removes a contiguous range of rows from a buffer without placing them in the
    /// <c>Delete!</c> buffer.
    /// </summary>
    /// <param name="startRow">The one-based first row to discard.</param>
    /// <param name="endRow">The one-based last row to discard, inclusive.</param>
    /// <param name="buffer">The buffer to discard from.</param>
    /// <returns>
    /// <see cref="DataStoreSuccess"/>, or <see cref="DataStoreFailure"/> when the requested range is
    /// not addressable.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="buffer"/> is not a declared buffer.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Reproduces <c>RowsDiscard(startrow, endrow, buffer)</c>, whose four measured call sites are
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L178, L181, L211, L214</c>.
    /// </para>
    /// <para>
    /// C-B - WHY THIS OPERATION EXISTS AT ALL RATHER THAN A <c>Reset</c> CALL. Those four sites are
    /// the workaround for a documented legacy defect. The comment immediately above the first pair
    /// reads <c>不能使用Reset来清除数据，会使SetChanges应用不到行</c>
    /// [<c>:L176-L177</c>] - "Reset must not be used to clear data, it makes SetChanges fail to apply
    /// to rows" - so the legacy discards the rows instead. <see cref="Reset"/> and this method are
    /// therefore NOT interchangeable, and conflating them would destroy the defect's meaning.
    /// </para>
    /// </remarks>
    internal long RowsDiscard(long startRow, long endRow, DwBuffer buffer)
    {
        List<CarrierRow> rows = BufferOf(buffer);

        if (startRow < ItemStatusMachine.FirstRowNumber
            || endRow < startRow
            || endRow > rows.Count)
        {
            return DataStoreFailure;
        }

        rows.RemoveRange(ZeroBasedIndexOf(startRow), (int)(endRow - startRow + 1L));

        return DataStoreSuccess;
    }

    /// <summary>
    /// Admits one row to the end of a buffer.
    /// </summary>
    /// <param name="buffer">The buffer to admit the row into.</param>
    /// <param name="status">The row's initial item status.</param>
    /// <returns>The new row's one-based row number within that buffer.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="buffer"/> is not a declared buffer.
    /// </exception>
    /// <remarks>
    /// <para>
    /// NOT A LEGACY MEMBER, AND LABELLED AS SUCH DELIBERATELY. In the legacy, rows enter a carrier by
    /// exactly two routes: a database retrieve, and <c>SetChanges</c> or <c>SetFullState</c> applying
    /// a decoded payload. Neither lives in this file - the first belongs to <c>Tasks/</c> and the
    /// second to the two codecs - and there is no <c>InsertRow</c> or <c>DeleteRow</c> call anywhere
    /// in the legacy SQL task layer, so reproducing one would be an invention.
    /// </para>
    /// <para>
    /// What this seam is for: it lets a codec materialise a decoded row into the right buffer, and it
    /// lets the buffer model be exercised with NO DATABASE, which C-H requires of every line this
    /// service's coverage gate measures. Appending rather than inserting at an arbitrary position is
    /// the whole of it, because appending is the only admission order either route produces.
    /// </para>
    /// </remarks>
    internal long AppendRow(DwBuffer buffer, ItemStatus status)
    {
        List<CarrierRow> rows = BufferOf(buffer);

        rows.Add(new CarrierRow(status));

        // R9: the new row's ONE-BASED number is the post-add count, not the zero-based index the list
        // would report. There is no minus one here.
        return rows.Count;
    }

    /// <summary>
    /// Returns one row of a buffer.
    /// </summary>
    /// <param name="row">The one-based row number within <paramref name="buffer"/>.</param>
    /// <param name="buffer">The buffer to read from.</param>
    /// <returns>The row.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="row"/> is outside 1 through the buffer's row count, or
    /// <paramref name="buffer"/> is not a declared buffer.
    /// </exception>
    /// <remarks>
    /// <para>
    /// R9 - THIS IS THE ONLY PLACE IN THIS FILE WHERE A ONE-BASED ROW NUMBER BECOMES A ZERO-BASED
    /// INDEX. Every other member reaches a row through this one, so the conversion has exactly one
    /// line to audit instead of one per accessor. The refactor plan permits either a centralized
    /// helper or an individual audit; row access takes the centralized route, and the remaining
    /// one-based translations - the backward delete walk and the N-char scan - take the individual
    /// route because each is a loop whose SHAPE has to stay diffable against its locator.
    /// </para>
    /// </remarks>
    internal CarrierRow RowAt(long row, DwBuffer buffer)
    {
        List<CarrierRow> rows = BufferOf(buffer);

        if (row < ItemStatusMachine.FirstRowNumber || row > rows.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(row),
                row,
                $"Row number must be between {ItemStatusMachine.FirstRowNumber} and the {buffer} "
                    + $"buffer's row count of {rows.Count}, inclusive. Row numbers on this surface "
                    + "are one-based, matching the legacy; a zero or negative value usually means a "
                    + "one-based row number was rebased by mistake.");
        }

        return rows[ZeroBasedIndexOf(row)];
    }

    #endregion

    #region The two reset operations - distinct, and not interchangeable

    /// <summary>
    /// Removes every row from all three buffers.
    /// </summary>
    /// <returns><see cref="DataStoreSuccess"/>.</returns>
    /// <remarks>
    /// <para>
    /// Reproduces <c>Reset()</c>, called on a carrier at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L96</c> - immediately after
    /// a full-state image has been taken, on the crosstab-or-composite path - and at <c>:L217</c>,
    /// <c>:L773</c> and <c>:L877</c>.
    /// </para>
    /// <para>
    /// C-B - THIS IS THE OPERATION ONE PRESERVED DEFECT FORBIDS. Between changeset chunks the legacy
    /// must NOT use it: <c>不能使用Reset来清除数据，会使SetChanges应用不到行</c> [<c>:L176-L177</c>]. It
    /// uses <see cref="RowsDiscard"/> there instead [<c>:L178, L181</c>]. Both operations exist here
    /// precisely so <c>Buffers/ChangesetCodec.cs</c> can honour that prohibition, and
    /// <see cref="ResetUpdate"/> is a third, different operation again.
    /// </para>
    /// </remarks>
    internal long Reset()
    {
        _primaryBuffer.Clear();
        _deleteBuffer.Clear();
        _filterBuffer.Clear();

        return DataStoreSuccess;
    }

    /// <summary>
    /// Clears the update flags: every remaining row becomes unmodified with its original values
    /// re-baselined, and the <c>Delete!</c> buffer is emptied. Rows are NOT removed.
    /// </summary>
    /// <returns><see cref="DataStoreSuccess"/>.</returns>
    /// <remarks>
    /// <para>
    /// Reproduces <c>ResetUpdate()</c>, called at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L144, L199, L223,
    /// L240</c> - and note WHERE: at <c>:L223</c> and <c>:L240</c> it runs once the last chunk has been
    /// applied, which is what makes freshly delivered rows read as unmodified with their originals
    /// equal to their currents.
    /// </para>
    /// <para>
    /// C-B, AND WHY <see cref="Reset"/> IS NOT A SUBSTITUTE IN EITHER DIRECTION. <see cref="Reset"/>
    /// removes rows and would discard the data that has just arrived; this operation keeps every row
    /// and only clears the flags. The legacy uses BOTH, at different points, and it is one of the two
    /// that a preserved defect specifically forbids between chunks. Conflating them destroys that
    /// distinction.
    /// </para>
    /// <para>
    /// It appears in BOTH the DataWindow and the DataStore arm of the receiver dispatch
    /// [<c>:L199</c> and <c>:L223</c>], which is the evidence that - unlike the <c>GroupCalc</c> and
    /// <c>SetRedraw</c> calls beside it at <c>:L200</c> and <c>:L203</c> - it is NOT presentational and
    /// must be kept (C-D).
    /// </para>
    /// </remarks>
    internal long ResetUpdate()
    {
        foreach (CarrierRow row in _primaryBuffer)
        {
            row.Baseline();
        }

        foreach (CarrierRow row in _filterBuffer)
        {
            row.Baseline();
        }

        // Emptying the delete buffer is half of what clearing the update flags MEANS: a deleted row
        // whose deletion has been applied must generate no further statement.
        _deleteBuffer.Clear();

        return DataStoreSuccess;
    }

    #endregion

    #region SQL-preview interception

    /// <summary>
    /// Installs the statement to be executed in place of the one being previewed.
    /// </summary>
    /// <param name="sqlSyntax">The replacement statement.</param>
    /// <returns><see cref="DataStoreSuccess"/>.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="sqlSyntax"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Reproduces <c>SetSqlPreview(...)</c>, whose single measured call site is
    /// <c>SetSqlPreview(_of_ReplaceNCharLiteral(sqlSyntax))</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L170</c>]. The installed
    /// value is readable through <see cref="SqlPreviewStatement"/>.
    /// </para>
    /// <para>
    /// C-B - NO SCOPE GUARD IS REPRODUCED. PowerBuilder documents this function as callable only from
    /// within the SQL-preview event, but the legacy's one call site is inside that event, no legacy
    /// code checks the return value, and nothing in this repository states what happens elsewhere.
    /// Inventing a guard would add a failure mode the oracle does not have.
    /// </para>
    /// <para>
    /// C-F / C-G. The statement is STORED AND NEVER LOGGED. On the interpolating path it carries
    /// literal values verbatim, and there is no logger anywhere in this file; redaction belongs to
    /// <c>Errors/SqlRedactor.cs</c>.
    /// </para>
    /// </remarks>
    internal long SetSqlPreview(string sqlSyntax)
    {
        ArgumentNullException.ThrowIfNull(sqlSyntax);

        SqlPreviewStatement = sqlSyntax;

        return DataStoreSuccess;
    }

    #endregion

    #region The inherited event surface - default scripts, overridden by the two carriers

    /// <summary>
    /// Raised as a retrieve begins.
    /// </summary>
    /// <returns>
    /// <see cref="EventContinue"/> to proceed, <see cref="EventStop"/> to abort the retrieve.
    /// </returns>
    /// <remarks>
    /// The ancestor script the worker carrier extends at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L27</c> with
    /// <c>call super::retrievestart</c>. Neither the PowerBuilder <c>datastore</c> nor
    /// <c>n_cst_thread_task_sqlbase_ds</c> scripts it, so the default answer is
    /// <see cref="EventContinue"/>.
    /// </remarks>
    internal virtual long OnRetrieveStart()
    {
        return EventContinue;
    }

    /// <summary>
    /// Raised as each row of a retrieve arrives.
    /// </summary>
    /// <param name="row">The one-based number of the row that has just arrived.</param>
    /// <returns>
    /// <see cref="EventContinue"/> to proceed, <see cref="EventStop"/> to stop the retrieve.
    /// </returns>
    /// <remarks>
    /// The ancestor script the worker carrier extends at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L63</c>. Unscripted
    /// above that, so the default answer is <see cref="EventContinue"/>.
    /// </remarks>
    internal virtual long OnRetrieveRow(long row)
    {
        return EventContinue;
    }

    /// <summary>
    /// Raised as an update begins.
    /// </summary>
    /// <returns>
    /// <see cref="EventContinue"/> to proceed, <see cref="EventStop"/> to abandon the update.
    /// </returns>
    /// <remarks>
    /// The ancestor script the worker carrier extends at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L47</c>. Unscripted
    /// above that, so the default answer is <see cref="EventContinue"/>.
    /// </remarks>
    internal virtual long OnUpdateStart()
    {
        return EventContinue;
    }

    /// <summary>
    /// Raised once an update has completed, carrying the three row counts.
    /// </summary>
    /// <param name="rowsInserted">The number of rows inserted.</param>
    /// <param name="rowsUpdated">The number of rows updated.</param>
    /// <param name="rowsDeleted">The number of rows deleted.</param>
    /// <remarks>
    /// Returns nothing, matching the legacy event, which has no <c>return</c> and no return type
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L162-L166</c>].
    /// Unscripted on the ancestor.
    /// </remarks>
    internal virtual void OnUpdateEnd(long rowsInserted, long rowsUpdated, long rowsDeleted)
    {
        // The PowerBuilder datastore ancestor has no script for this event. The main-thread carrier
        // overrides it to capture the three counts [_ds.sru:L162-L166]; there is nothing to do here.
    }

    /// <summary>
    /// Raised when the database reports an error.
    /// </summary>
    /// <param name="sqlDbCode">The database's own error code.</param>
    /// <param name="sqlErrText">The database's error text.</param>
    /// <param name="sqlSyntax">
    /// The statement that failed. Forwarded, never logged - see <see cref="ICarrierParentTask.OnDbError"/>.
    /// </param>
    /// <param name="buffer">The buffer the offending row sits in.</param>
    /// <param name="row">The offending row number, one-based.</param>
    /// <returns>
    /// <see cref="EventContinue"/> to let the runtime report the error in its default way,
    /// <see cref="EventStop"/> to suppress that reporting.
    /// </returns>
    /// <remarks>
    /// The ancestor script the main-thread carrier replaces at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L159</c>. Unscripted
    /// above that.
    /// </remarks>
    internal virtual long OnDbError(
        long sqlDbCode,
        string sqlErrText,
        string sqlSyntax,
        DwBuffer buffer,
        long row)
    {
        return EventContinue;
    }

    /// <summary>
    /// Raised before each statement is executed, giving the carrier the chance to replace it.
    /// </summary>
    /// <param name="sqlType">The kind of statement being previewed.</param>
    /// <param name="sqlSyntax">The statement as generated.</param>
    /// <param name="buffer">The buffer whose row the statement was generated for.</param>
    /// <returns>
    /// <see cref="EventContinue"/> to proceed, <see cref="EventStop"/> to stop processing.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The ancestor script the main-thread carrier replaces at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L168-L174</c> and the
    /// worker carrier then extends at <c>n_cst_thread_task_sqlbase_ds_mt.sru:L30-L45</c>. Unscripted
    /// above that.
    /// </para>
    /// <para>
    /// C-K - TWO ARGUMENTS OF THE PowerBuilder EVENT ARE DELIBERATELY ABSENT. Its full signature also
    /// carries the requesting function and the row number, and NEITHER carrier body reads either one:
    /// <c>_ds</c> reads <c>sqlType</c> and <c>sqlSyntax</c> [<c>:L168, L170</c>] and <c>_ds_mt</c> reads
    /// <c>sqlType</c> and <c>buffer</c> [<c>_ds_mt.sru:L31-L32</c>]. Carrying an argument no ported line
    /// consumes would be inventing surface, so the omission is recorded here instead.
    /// </para>
    /// </remarks>
    internal virtual long OnSqlPreview(SqlPreviewType sqlType, string sqlSyntax, DwBuffer buffer)
    {
        return EventContinue;
    }

    #endregion

    #region Private helpers

    /// <summary>
    /// Resolves a buffer discriminator to the list that holds it.
    /// </summary>
    /// <param name="buffer">The buffer to resolve.</param>
    /// <returns>The backing list.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="buffer"/> is not one of the three declared buffers.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The three buffers are the published <see cref="DwBuffer"/> members and this port declares no
    /// fourth. A value outside them can only arise from an out-of-range cast, which is a structural
    /// fault, so it throws rather than answering an empty buffer - the refactor plan's fail-fast
    /// posture, which must never soften into graceful degradation.
    /// </para>
    /// <para>
    /// THE FILTER BUFFER'S ROW ORDER IS INVERTED RELATIVE TO THE SOURCE, which the published contract
    /// records and the legacy relies on: <c>n_cst_thread_task_sqlupdate.sru:L237</c> walks it BACKWARD
    /// for exactly that reason [<c>:L235</c>]. This method hands back the list as it is and performs no
    /// reordering, because normalising the order here would silently break that walk.
    /// </para>
    /// </remarks>
    private List<CarrierRow> BufferOf(DwBuffer buffer)
    {
        return buffer switch
        {
            DwBuffer.Primary => _primaryBuffer,
            DwBuffer.Delete => _deleteBuffer,
            DwBuffer.Filter => _filterBuffer,
            _ => throw new ArgumentOutOfRangeException(
                nameof(buffer),
                buffer,
                "Buffer must be one of the three declared DataWindow buffers: Primary, Delete or "
                    + "Filter. The legacy model has no fourth buffer and this port declares none."),
        };
    }

    /// <summary>
    /// Converts a one-based legacy row number into a zero-based list index.
    /// </summary>
    /// <param name="rowNumber">The one-based row number, already range-checked by the caller.</param>
    /// <returns>The zero-based index.</returns>
    /// <remarks>
    /// R9 - THE SINGLE REBASE SITE FOR ROW ACCESS IN THIS FILE, and the only minus-one applied to a
    /// row ordinal anywhere in it. Centralised on purpose: the refactor plan calls one-based
    /// translation the most dangerous mechanical hazard in the migration, and an off-by-one here is
    /// indistinguishable from a behavioural regression because the row COUNT stays correct while the
    /// rows shift. The cast is safe because the caller has already bounded the value by a
    /// <see cref="List{T}"/> count, which is itself an <see cref="int"/>.
    /// </remarks>
    private static int ZeroBasedIndexOf(long rowNumber)
    {
        return (int)(rowNumber - 1L);
    }

    /// <summary>
    /// Screens a column number, rejecting the row-status sentinel.
    /// </summary>
    /// <param name="columnNumber">The candidate one-based column number.</param>
    /// <returns><paramref name="columnNumber"/> UNCHANGED. R9: no rebase.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="columnNumber"/> is not a column number.
    /// </exception>
    /// <remarks>
    /// A VALUE is never addressed by the column-zero sentinel - only a STATUS is - so the sentinel is
    /// a caller fault here rather than an alternative meaning. The classification itself is delegated
    /// to <see cref="ItemStatusMachine.TryGetColumnNumber"/> so that the sentinel's definition lives
    /// in one place (R9 item 4).
    /// </remarks>
    private static int RequireColumnNumber(int columnNumber)
    {
        if (ItemStatusMachine.TryGetColumnNumber(columnNumber, out int resolved))
        {
            return resolved;
        }

        throw new ArgumentOutOfRangeException(
            nameof(columnNumber),
            columnNumber,
            $"Column number must be {ItemStatusMachine.FirstColumnNumber} or greater. The value "
                + $"{ItemStatusMachine.RowStatusColumn} is the row-status sentinel, which addresses "
                + "the row itself and never a column value.");
    }

    #endregion
}

#endregion

#region The main-thread carrier - n_cst_thread_task_sqlbase_ds

/// <summary>
/// The MAIN-THREAD result carrier: the row-cap state, the three update counts, the database-error
/// forwarding hook and the N-char statement rewrite.
/// </summary>
/// <remarks>
/// <para>
/// Ported from <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru</c> (176 lines),
/// whose header comment <c>$PBExportComments$线程SQL任务对象Datastore~r~n[运行在主线程]</c> annotates it
/// as running on the main thread, and which is declared
/// <c>global type n_cst_thread_task_sqlbase_ds from datastore</c> [<c>:L8</c>].
/// </para>
/// <para>
/// THE MEMBER SET IS THE LEGACY'S OWN, ITEM FOR ITEM: five protected state fields
/// [<c>:L42-L48</c>], six public members and one private rewriter [<c>:L52-L60</c>], the
/// initialization event [<c>:L62-L64</c>] and three event scripts - the database error at
/// <c>:L159</c>, the update completion at <c>:L162-L166</c> and the SQL preview at
/// <c>:L168-L174</c>. Nothing is added and nothing is left out.
/// </para>
/// <para>
/// This type is CONCRETE, not abstract, because the legacy instantiates it directly:
/// <c>ds = Create n_cst_thread_task_sqlbase_ds</c>
/// [<c>n_cst_thread_task_sqlbase.sru:L554</c>]. Use
/// <see cref="DataWindowCarrierFactory"/> to reproduce that selection rather than choosing a type by
/// hand.
/// </para>
/// </remarks>
internal class DataWindowCarrier : DataWindowBufferStore
{
    /// <summary>
    /// Creates a main-thread carrier.
    /// </summary>
    /// <param name="timeProvider">
    /// The service's single clock seam. Required on the whole hierarchy even though only the worker
    /// carrier reads it, so that there is one clock and not two.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="timeProvider"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// C-H. The clock arrives by constructor injection and never from
    /// <c>DateTime</c>, <c>DateTimeOffset</c>, <c>Stopwatch</c> or <c>Environment.TickCount</c>; see
    /// DECISION 6 in the file header for why that is mandatory rather than tidy, and
    /// <see cref="WorkerDataWindowCarrier"/> for the notification throttle it makes reproducible.
    /// </remarks>
    internal DataWindowCarrier(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        Clock = timeProvider;
    }

    /// <summary>
    /// Which thread this carrier type is built for.
    /// </summary>
    /// <remarks>
    /// <see cref="CarrierThreadAffinity.MainThread"/>, from the header annotation
    /// <c>[运行在主线程]</c> at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L2</c>. Overridden by the
    /// worker carrier. THIS IS A PROPERTY OF THE TYPE, and it is deliberately distinct from
    /// <see cref="ParentThreadAffinity"/>, which is a property of the task the carrier was
    /// initialized from: the factory makes the two agree by construction, exactly as
    /// <c>n_cst_thread_task_sqlbase.sru:L553-L557</c> does, but they are two different facts and
    /// collapsing them would hide which one a given call site actually depends on.
    /// </remarks>
    internal virtual CarrierThreadAffinity Affinity => CarrierThreadAffinity.MainThread;

    /// <summary>
    /// The parent task this carrier was initialized from, or <see langword="null"/> before
    /// <see cref="OnInit"/> has run.
    /// </summary>
    /// <remarks>
    /// Reproduces <c>privatewrite n_cst_thread_task_sqlbase #ParentTask</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L39</c>]: readable by
    /// anyone, writable only from inside the carrier, which a get-only property with a private setter
    /// expresses exactly.
    /// </remarks>
    internal ICarrierParentTask? ParentTask { get; private set; }

    /// <summary>
    /// The affinity of the parent task's thread, captured at initialization, or
    /// <see langword="null"/> before <see cref="OnInit"/> has run.
    /// </summary>
    /// <remarks>
    /// Reproduces <c>privatewrite n_cst_thread #ParentThread</c> [<c>:L40</c>] and its derivation
    /// <c>#ParentThread = parentTask.#ParentThread</c> [<c>:L63</c>], narrowed to the single fact any
    /// ported line asks of it. Nullable because the legacy reference is genuinely unset until
    /// <c>oninit</c> runs.
    /// </remarks>
    internal CarrierThreadAffinity? ParentThreadAffinity { get; private set; }

    /// <summary>
    /// The service's clock, for the worker carrier's notification throttle.
    /// </summary>
    /// <remarks>
    /// Protected so the derived worker carrier reads it the way <c>_ds_mt</c> reads its inherited
    /// state, and exposed as the ONLY time source in this file.
    /// </remarks>
    protected TimeProvider Clock { get; }

    #region The five protected state fields of _ds.sru:L42-L48

    /// <summary>
    /// The maximum number of rows a retrieve may deliver, or zero for no limit.
    /// </summary>
    /// <remarks>
    /// Reproduces <c>protected long _nMaxRows</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L43</c>]. Protected
    /// because <c>_ds_mt</c> both READS it and WRITES it - it clears the cap to zero at
    /// <c>n_cst_thread_task_sqlbase_ds_mt.sru:L66</c> - so a read-only projection would not do.
    /// </remarks>
    protected long MaxRows { get; set; }

    /// <summary>
    /// Whether a retrieve was stopped because it passed the row cap.
    /// </summary>
    /// <remarks>
    /// Reproduces <c>protected boolean _bRowsExceeded</c> [<c>:L44</c>], set by the worker carrier at
    /// <c>n_cst_thread_task_sqlbase_ds_mt.sru:L69</c> and published by
    /// <see cref="IsRowsExceeded"/>.
    /// </remarks>
    protected bool RowsExceeded { get; set; }

    /// <summary>
    /// The number of rows the last update inserted.
    /// </summary>
    /// <remarks>
    /// Reproduces <c>protected long _nRowsInserted</c> [<c>:L46</c>], captured by
    /// <see cref="OnUpdateEnd"/> and published by <see cref="GetInsertedCount"/>.
    /// </remarks>
    protected long RowsInserted { get; set; }

    /// <summary>
    /// The number of rows the last update updated.
    /// </summary>
    /// <remarks>
    /// Reproduces <c>protected long _nRowsUpdated</c> [<c>:L47</c>].
    /// </remarks>
    protected long RowsUpdated { get; set; }

    /// <summary>
    /// The number of rows the last update deleted.
    /// </summary>
    /// <remarks>
    /// Reproduces <c>protected long _nRowsDeleted</c> [<c>:L48</c>]. NOT THE SAME THING AS
    /// <see cref="DataWindowBufferStore.DeletedCount"/> - see <see cref="GetDeletedCount"/>.
    /// </remarks>
    protected long RowsDeleted { get; set; }

    #endregion

    #region The six public members of _ds.sru:L52-L58

    /// <summary>
    /// Whether a retrieve was stopped because it passed the row cap.
    /// </summary>
    /// <returns><see langword="true"/> when the cap was reached and enforced.</returns>
    /// <remarks>
    /// Reproduces <c>of_isrowsexceeded()</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L66</c>], a bare
    /// <c>return _bRowsExceeded</c>. Note that the flag is set ONLY on the enforcement arm of the
    /// row-cap override; when the notification handler asks for the cap to be lifted, the retrieve
    /// continues and this stays <see langword="false"/> - see
    /// <see cref="WorkerDataWindowCarrier.OnRetrieveRow"/>.
    /// </remarks>
    internal bool IsRowsExceeded()
    {
        return RowsExceeded;
    }

    /// <summary>
    /// Sets the maximum number of rows a retrieve may deliver.
    /// </summary>
    /// <param name="maxRows">The cap. Zero means no limit, and negative values are not rejected.</param>
    /// <returns><c>RetCode.OK</c>.</returns>
    /// <remarks>
    /// <para>
    /// Reproduces <c>of_setmaxrows(readonly long maxrows)</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L69-L71</c>], which
    /// assigns and then <c>return RetCode.OK</c>. That is the FRAMEWORK convention, where success is
    /// zero, not the DataStore convention where success is one - see
    /// <see cref="DataWindowBufferStore.DataStoreSuccess"/> for why the two are named separately.
    /// </para>
    /// <para>
    /// C-B. NO VALIDATION IS ADDED. These two lines are the whole legacy body: there is no guard on
    /// the argument, so a negative cap is stored as given and simply never satisfies the
    /// <c>_nMaxRows &gt; 0</c> test at <c>n_cst_thread_task_sqlbase_ds_mt.sru:L64</c>, which makes it
    /// behave as no limit. The task-level setter is where a guard does exist - the query task rejects
    /// <c>rows &lt; 0</c> - and reproducing it here as well would add a failure mode the carrier does
    /// not have. The return type is kept even though the value is constant, because the legacy
    /// prototype returns <c>long</c> and a caller may test it.
    /// </para>
    /// </remarks>
    internal long SetMaxRows(long maxRows)
    {
        MaxRows = maxRows;

        return RetCode.OK;
    }

    /// <summary>
    /// The number of rows the last update inserted.
    /// </summary>
    /// <returns>The inserted row count.</returns>
    /// <remarks>
    /// Reproduces <c>of_getinsertedcount()</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L73-L74</c>]. Read
    /// together with its two siblings at <c>n_cst_thread_task_sqlupdate.sru:L247</c>, which forwards
    /// all three to the update-completed callback; those three values are what the update contract's
    /// counts message carries back to the caller.
    /// </remarks>
    internal long GetInsertedCount()
    {
        return RowsInserted;
    }

    /// <summary>
    /// The number of rows the last update updated.
    /// </summary>
    /// <returns>The updated row count.</returns>
    /// <remarks>
    /// Reproduces <c>of_getupdatedcount()</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L76-L77</c>].
    /// </remarks>
    internal long GetUpdatedCount()
    {
        return RowsUpdated;
    }

    /// <summary>
    /// The number of rows the last update DELETED FROM THE DATABASE.
    /// </summary>
    /// <returns>The deleted row count reported by the update.</returns>
    /// <remarks>
    /// <para>
    /// Reproduces <c>of_getdeletedcount()</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L79-L80</c>].
    /// </para>
    /// <para>
    /// READ THIS BEFORE USING EITHER: THIS IS NOT
    /// <see cref="DataWindowBufferStore.DeletedCount"/>. Both members exist on this one object in the
    /// legacy too, and they answer different questions.
    /// <see cref="DataWindowBufferStore.DeletedCount"/> is the inherited DataStore function reporting
    /// HOW MANY ROWS SIT IN THE <c>Delete!</c> BUFFER awaiting an update - the worker carrier uses it
    /// as a loop bound at <c>n_cst_thread_task_sqlbase_ds_mt.sru:L55</c>. This member reports HOW MANY
    /// ROWS THE COMPLETED UPDATE ACTUALLY DELETED, captured from the update-completion event at
    /// <c>_ds.sru:L164</c> and forwarded at <c>n_cst_thread_task_sqlupdate.sru:L247</c>. Before an
    /// update runs the first is positive and this is zero; after one succeeds the first is zero,
    /// because clearing the update flags empties that buffer, and this is positive. The near-identical
    /// names are the legacy's, and renaming either would break the correspondence a reader needs to
    /// diff this port against its oracle.
    /// </para>
    /// </remarks>
    internal long GetDeletedCount()
    {
        return RowsDeleted;
    }

    /// <summary>
    /// Zeroes all five pieces of carrier state: the row cap, the rows-exceeded flag and the three
    /// update counts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reproduces <c>of_clearstate()</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L82-L87</c>], which
    /// assigns all five in sequence and returns nothing.
    /// </para>
    /// <para>
    /// THIS IS THE CACHE-REUSE RESET. It is what <c>of_getcacheds</c> calls on a CACHE HIT
    /// [<c>n_cst_thread_task_sqlbase.sru:L546</c>], after restoring the cached carrier's original
    /// select, sort and filter, and immediately before handing the carrier back for another task to
    /// use. It deliberately touches NEITHER the rows nor the buffers - the cache path restores those
    /// separately - which is why it is not <see cref="DataWindowBufferStore.Reset"/> and not
    /// <see cref="DataWindowBufferStore.ResetUpdate"/>.
    /// </para>
    /// </remarks>
    internal void ClearState()
    {
        MaxRows = 0L;
        RowsExceeded = false;
        RowsInserted = 0L;
        RowsUpdated = 0L;
        RowsDeleted = 0L;
    }

    #endregion

    #region Initialization - _ds.sru:L62-L64

    /// <summary>
    /// Captures the parent task and derives the parent thread's affinity from it.
    /// </summary>
    /// <param name="parentTask">The task that owns this carrier.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="parentTask"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Reproduces the <c>oninit</c> event
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L62-L64</c>]:
    /// </para>
    /// <code>
    /// #ParentTask = parentTask
    /// #ParentThread = parentTask.#ParentThread
    /// </code>
    /// <para>
    /// The legacy raises it on EVERY path out of the carrier factory, cache hit and cache miss alike
    /// [<c>n_cst_thread_task_sqlbase.sru:L566</c>], so a cached carrier is re-pointed at whichever
    /// task has just borrowed it. Not virtual, because <c>_ds_mt</c> does not override it.
    /// </para>
    /// </remarks>
    internal void OnInit(ICarrierParentTask parentTask)
    {
        ArgumentNullException.ThrowIfNull(parentTask);

        ParentTask = parentTask;

        // `#ParentThread = parentTask.#ParentThread` [:L63], narrowed to the one fact about that
        // thread any ported line asks: whether it is the main one.
        ParentThreadAffinity = parentTask.IsMainThread
            ? CarrierThreadAffinity.MainThread
            : CarrierThreadAffinity.WorkerThread;
    }

    #endregion

    #region The three event scripts of _ds.sru

    /// <inheritdoc/>
    /// <remarks>
    /// Reproduces the <c>updateend</c> event
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L162-L166</c>], which
    /// copies the event's three arguments into the three protected count fields and does nothing else.
    /// These are the values the update contract's counts message returns, forwarded at
    /// <c>n_cst_thread_task_sqlupdate.sru:L247</c>.
    /// </remarks>
    internal override void OnUpdateEnd(long rowsInserted, long rowsUpdated, long rowsDeleted)
    {
        base.OnUpdateEnd(rowsInserted, rowsUpdated, rowsDeleted);

        RowsInserted = rowsInserted;
        RowsUpdated = rowsUpdated;
        RowsDeleted = rowsDeleted;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Reproduces the <c>dberror</c> event
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L159</c>], a single line:
    /// <c>return #ParentTask.Event OnDBError(sqldbcode,sqlerrtext,sqlsyntax,buffer,row)</c>. The
    /// carrier forwards the five raw scalars and PROPAGATES THE HOOK'S RETURN VALUE as the event
    /// result; it is the parent task that assembles the structured payload from them
    /// [<c>n_cst_thread_task_sqlbase.sru:L85</c>], which is why this file needs no dependency on
    /// <c>Errors/DbErrorData.cs</c>.
    /// </para>
    /// <para>
    /// C-F / C-G. <paramref name="sqlSyntax"/> carries the complete generated statement INCLUDING
    /// INTERPOLATED LITERAL VALUES on the interpolating path. It is passed straight through and NEVER
    /// LOGGED; there is no logger in this file at all. Redaction is <c>Errors/SqlRedactor.cs</c>'s job.
    /// </para>
    /// </remarks>
    internal override long OnDbError(
        long sqlDbCode,
        string sqlErrText,
        string sqlSyntax,
        DwBuffer buffer,
        long row)
    {
        base.OnDbError(sqlDbCode, sqlErrText, sqlSyntax, buffer, row);

        return RequireParentTask().OnDbError(sqlDbCode, sqlErrText, sqlSyntax, buffer, row);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Reproduces the <c>sqlpreview</c> event
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L168-L174</c>]:
    /// </para>
    /// <code>
    /// if sqlType = PreviewUpdate! or sqlType = PreviewInsert! then
    ///     if #ParentTask.of_IsNCharBinding() then
    ///         SetSqlPreview(_of_ReplaceNCharLiteral(sqlSyntax))
    ///     end if
    /// end if
    /// return 0
    /// </code>
    /// <para>
    /// Both conditions must hold, and the answer is <see cref="DataWindowBufferStore.EventContinue"/>
    /// UNCONDITIONALLY - on the rewrite path and off it alike. A retrieval or delete preview is left
    /// alone here; the worker carrier is what does something with a delete preview, and it does so
    /// AFTER this ancestor has run.
    /// </para>
    /// </remarks>
    internal override long OnSqlPreview(SqlPreviewType sqlType, string sqlSyntax, DwBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(sqlSyntax);

        base.OnSqlPreview(sqlType, sqlSyntax, buffer);

        // `if sqlType = PreviewUpdate! or sqlType = PreviewInsert!` [:L168] - and note the nesting:
        // the binding check is INSIDE this one, so a select or delete preview never asks it.
        if (sqlType is SqlPreviewType.Update or SqlPreviewType.Insert)
        {
            // `if #ParentTask.of_IsNCharBinding()` [:L169]. Reachable only when the connection has
            // BOTH DisableBind=1 AND NCharBind=1 [n_cst_thread_task_sqlbase.sru:L127-L132], which is
            // the literal-interpolating path - see ICarrierParentTask.IsNCharBinding.
            if (RequireParentTask().IsNCharBinding)
            {
                SetSqlPreview(ReplaceNCharLiteral(sqlSyntax));
            }
        }

        // `return 0` [:L173], unconditional.
        return EventContinue;
    }

    #endregion

    #region The N-char literal rewriter - _ds.sru:L89-L147

    /// <summary>
    /// Rewrites every string literal in a statement into PowerBuilder N-char syntax by prefixing it
    /// with <c>N</c>, and returns the statement UNCHANGED the moment it finds one already prefixed.
    /// </summary>
    /// <param name="sql">The statement to rewrite.</param>
    /// <returns>
    /// The rewritten statement; the empty string when <paramref name="sql"/> is empty; or
    /// <paramref name="sql"/> ITSELF, by reference, when a literal already carries the prefix.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Ported from <c>_of_replacencharliteral</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L89-L147</c>], a single
    /// forward scan that tracks whether it is inside a quoted literal and accumulates the statement in
    /// segments. Private, as the legacy prototype is [<c>:L59</c>]; it is reachable for testing through
    /// <see cref="OnSqlPreview"/>, which is also the only way the legacy reaches it.
    /// </para>
    /// <para>
    /// C-B - DEFECT 5, THE UNCHANGED EARLY RETURN. When an opening quote is preceded by <c>N</c> the
    /// legacy answers <c>return sql</c> [<c>:L127</c>] and abandons everything accumulated so far. That
    /// is BEHAVIOUR, not an optimisation, and there are two things not to do with it. It must not
    /// become an idempotent rewrite that produces an equal-but-not-identical string - this method
    /// returns the SAME REFERENCE, which is observably different from the rewritten path even when the
    /// text matches. And the test is per STATEMENT, not per literal: one already-prefixed literal
    /// leaves EVERY OTHER literal in the statement unprefixed, including any the scan had already
    /// rewritten into its buffer, because that buffer is discarded. A statement mixing the two forms
    /// therefore comes back wholly unrewritten.
    /// </para>
    /// <para>
    /// R9 - THREE ONE-BASED CONSTRUCTS, EACH TRANSLATED INDIVIDUALLY, none by blanket arithmetic:
    /// </para>
    /// <list type="number">
    ///   <item><description>
    ///     THE SCAN ITSELF is <c>for nPos = 1 to nLen</c> [<c>:L113</c>], so the loop below counts
    ///     from 1 to the length INCLUSIVE. It is not rewritten as a zero-based walk, because the two
    ///     look-arounds below have to stay diffable against their locators.
    ///   </description></item>
    ///   <item><description>
    ///     THE LOOK-BEHIND is <c>Mid(sql,nPos - 1,1)</c> [<c>:L125</c>]. At position 1 that asks for
    ///     character 0, and PowerBuilder's <c>Mid</c> answers the EMPTY STRING for an out-of-range
    ///     start rather than failing. So a statement that opens with a quote must compare unequal to
    ///     <c>N</c> and carry on - never throw an exception the legacy could not raise. See
    ///     <see cref="CharacterEquals"/>.
    ///   </description></item>
    ///   <item><description>
    ///     THE IN-LOOP SKIP is <c>nPos ++</c> followed by <c>continue</c> [<c>:L119-L120</c>].
    ///     PowerScript's <c>continue</c> jumps to <c>next</c>, which increments again, so the pair
    ///     advances TWO positions and consumes both halves of an escaped quote. A C# <c>for</c> runs
    ///     its increment on <c>continue</c> too, so the same two lines produce the same two-position
    ///     advance - but only because the increment lives in the <c>for</c> header. Moving it into the
    ///     body would silently halve the skip.
    ///   </description></item>
    /// </list>
    /// <para>
    /// The scan's other subtlety is worth stating because it looks like dead code: the CLOSING-quote
    /// branch sets the pending segment start when it is unset [<c>:L134-L136</c>], under the comment
    /// <c>覆盖空白字符串</c> - "covers the empty string". That is what keeps the closing quote of an
    /// EMPTY literal, whose opening quote reset the pending start to zero one position earlier.
    /// Without it, <c>''</c> would come back as <c>N'</c>.
    /// </para>
    /// </remarks>
    private static string ReplaceNCharLiteral(string sql)
    {
        const char quote = '\'';
        const char ncharPrefix = 'N';
        const string ncharLiteralOpening = "N'";

        // `nLen = Len(sql)` [:L110] and `if nLen <= 0 then return ""` [:L111]. The empty answer is the
        // legacy's, and it is NOT the input: an empty input yields an empty result either way, so the
        // distinction only matters for keeping this line diffable.
        long length = sql.Length;
        if (length <= 0L)
        {
            return string.Empty;
        }

        StringBuilder rewritten = new();

        // `nStmtBegin` - the one-based start of the segment not yet copied out, or 0 for "none
        // pending". Zero is a SENTINEL here and not an index (R9), which is why it is compared to
        // exactly 0 below and never decremented.
        long segmentStart = 0L;
        bool inLiteral = false;

        for (long position = 1L; position <= length; position++)
        {
            if (!CharacterEquals(sql, position, quote))
            {
                // `else ... if nStmtBegin = 0 then nStmtBegin = nPos` [:L137-L138]. An ordinary
                // character opens a pending segment if none is open.
                if (segmentStart == 0L)
                {
                    segmentStart = position;
                }

                continue;
            }

            // `if bQuoted then ... if Mid(sql,nPos + 1,1) = "'"` [:L115-L117]. An escaped quote is
            // only escaped INSIDE a literal; outside one, a doubled quote is an empty literal
            // followed by another, and the two branches below handle it as such.
            if (inLiteral && CharacterEquals(sql, position + 1L, quote))
            {
                // `if nStmtBegin = 0 then nStmtBegin = nPos` [:L118].
                if (segmentStart == 0L)
                {
                    segmentStart = position;
                }

                // R9 item 3: `nPos ++` [:L119] then `continue` [:L120]. Together with the increment in
                // the `for` header this advances two positions, consuming both halves of the pair.
                position++;
                continue;
            }

            // `bQuoted = Not bQuoted` [:L123].
            inLiteral = !inLiteral;

            if (inLiteral)
            {
                // C-B DEFECT 5. `if Mid(sql,nPos - 1,1) = "N" then return sql` [:L125-L127], under the
                // comment 已经是NChar语法，不再做处理 - "already N-char syntax, do no further
                // processing". Returns the ARGUMENT ITSELF, discarding `rewritten` entirely.
                if (CharacterEquals(sql, position - 1L, ncharPrefix))
                {
                    return sql;
                }

                // `if nStmtBegin < nPos then sNewSql += Mid(sql,nStmtBegin,nPos - nStmtBegin) + "N'"`
                // [:L129-L131]. Note this holds when segmentStart is still the 0 sentinel and the
                // statement opens with a quote: the segment is then empty - PowerBuilder's Mid answers
                // "" for a start below 1 - and only the "N'" is appended.
                if (segmentStart < position)
                {
                    rewritten
                        .Append(Segment(sql, segmentStart, position - segmentStart))
                        .Append(ncharLiteralOpening);

                    segmentStart = 0L;
                }
            }
            else
            {
                // `else ... if nStmtBegin = 0 then nStmtBegin = nPos` [:L133-L136], the
                // 覆盖空白字符串 arm. This is what preserves the closing quote of an empty literal.
                if (segmentStart == 0L)
                {
                    segmentStart = position;
                }
            }
        }

        // `if nStmtBegin > 0 and nStmtBegin <= nLen then sNewSql += Mid(sql,nStmtBegin,nLen -
        // nStmtBegin + 1)` [:L142-L144]. The `+ 1` is inclusive-length arithmetic in a one-based
        // domain, not an off-by-one (R9).
        if (segmentStart > 0L && segmentStart <= length)
        {
            rewritten.Append(Segment(sql, segmentStart, length - segmentStart + 1L));
        }

        // `return sNewSql` [:L146].
        return rewritten.ToString();
    }

    /// <summary>
    /// Reproduces <c>Mid(text, oneBasedPosition, 1) = expected</c>, including PowerBuilder's answer
    /// for an out-of-range position.
    /// </summary>
    /// <param name="text">The text to inspect.</param>
    /// <param name="oneBasedPosition">The one-based character position. R9: never rebased by callers.</param>
    /// <param name="expected">The character to compare against.</param>
    /// <returns>
    /// <see langword="true"/> only when the position is addressable AND holds
    /// <paramref name="expected"/>.
    /// </returns>
    /// <remarks>
    /// PowerBuilder's <c>Mid</c> answers the EMPTY STRING when the start is below 1 or past the end,
    /// and the empty string never equals a one-character literal, so an out-of-range position answers
    /// <see langword="false"/> here. That is what makes the look-behind at position 1
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L125</c>] and the
    /// look-ahead at the last position [<c>:L117</c>] safe without either becoming an exception the
    /// legacy never raised. Comparing characters rather than materialising a one-character string also
    /// keeps the scan allocation-free, which matters because it runs once per previewed statement.
    /// </remarks>
    private static bool CharacterEquals(string text, long oneBasedPosition, char expected)
    {
        if (oneBasedPosition < 1L || oneBasedPosition > text.Length)
        {
            return false;
        }

        // R9: the one and only rebase in this scan, applied to a value already proven addressable.
        return text[(int)(oneBasedPosition - 1L)] == expected;
    }

    /// <summary>
    /// Reproduces <c>Mid(text, oneBasedStart, length)</c>, including PowerBuilder's answers for an
    /// out-of-range start and for a length that runs past the end.
    /// </summary>
    /// <param name="text">The text to take from.</param>
    /// <param name="oneBasedStart">
    /// The one-based start position. Values below 1 - including the pending-segment sentinel 0 -
    /// answer the empty string, which is what PowerBuilder does and what the rewriter relies on for a
    /// statement that opens with a quote.
    /// </param>
    /// <param name="length">The number of characters to take.</param>
    /// <returns>The segment, truncated at the end of <paramref name="text"/>, or the empty string.</returns>
    private static string Segment(string text, long oneBasedStart, long length)
    {
        if (oneBasedStart < 1L || oneBasedStart > text.Length || length <= 0L)
        {
            return string.Empty;
        }

        long available = text.Length - oneBasedStart + 1L;
        long take = Math.Min(length, available);

        // R9: rebase applied once, to a start already proven addressable.
        return text.Substring((int)(oneBasedStart - 1L), (int)take);
    }

    #endregion

    #region Private helpers

    /// <summary>
    /// Returns the parent task, failing fast when the carrier was never initialized.
    /// </summary>
    /// <returns>The parent task.</returns>
    /// <exception cref="InvalidOperationException">
    /// <see cref="OnInit"/> has not been called.
    /// </exception>
    /// <remarks>
    /// The legacy dereferences <c>#ParentTask</c> unconditionally at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru:L159</c> and <c>:L169</c>,
    /// and on an uninitialized carrier that is a null-object runtime error which terminates the
    /// operation. THE FAIL-FAST POSTURE IS PRESERVED AS FAIL-FAST: this throws rather than inventing a
    /// benign default, because the refactor plan is explicit that softening a structural fault into
    /// warn-and-continue would be a behavioural change dressed as robustness. The legacy raises it on
    /// every factory path [<c>n_cst_thread_task_sqlbase.sru:L566</c>], so reaching this exception means
    /// a caller bypassed the factory.
    /// </remarks>
    protected ICarrierParentTask RequireParentTask()
    {
        return ParentTask
            ?? throw new InvalidOperationException(
                "The carrier has not been initialized. Call OnInit with the owning task before "
                    + "raising any carrier event, exactly as the legacy factory does on every path "
                    + "out of of_getcacheds.");
    }

    #endregion
}

#endregion

#region The worker carrier - n_cst_thread_task_sqlbase_ds_mt

/// <summary>
/// The WORKER result carrier: the main-thread carrier plus cancellation checks on four events, a
/// throttled update-progress notification, and row-cap enforcement.
/// </summary>
/// <remarks>
/// <para>
/// Ported from <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru</c> (73
/// lines), whose header comment <c>$PBExportComments$线程SQL任务对象Datastore~r~n[运行在子线程]</c>
/// annotates it as running on a child thread, and which is declared
/// <c>global type n_cst_thread_task_sqlbase_ds_mt from n_cst_thread_task_sqlbase_ds</c>
/// [<c>:L8</c>].
/// </para>
/// <para>
/// DERIVATION, NOT COMPOSITION, AND EVERY OVERRIDE CALLS ITS ANCESTOR FIRST. All four legacy scripts
/// open with <c>call super::</c> - <c>retrievestart</c> at <c>:L27</c>, <c>sqlpreview</c> at
/// <c>:L30</c>, <c>updatestart</c> at <c>:L47</c> and <c>retrieverow</c> at <c>:L63</c> - and the
/// ancestor call is load-bearing in at least one of them: the SQL-preview ancestor is what performs
/// the N-char rewrite, so flattening the pair would move that rewrite after the progress accounting
/// instead of before it.
/// </para>
/// <para>
/// THE MEMBER SET IS THE LEGACY'S OWN: three private counters [<c>:L13-L17</c>] and four event
/// overrides. It declares no state beyond those three and no function at all.
/// </para>
/// <para>
/// C-B. Three of the five preserved defects live in this type: the uncounted delete-then-insert arm,
/// the backward deleted-row walk and the row-cap lifting. Each is annotated where it is reproduced.
/// </para>
/// </remarks>
internal sealed class WorkerDataWindowCarrier : DataWindowCarrier
{
    /// <summary>
    /// The tick delta, in milliseconds, above which a progress notification is due.
    /// </summary>
    /// <remarks>
    /// <para>
    /// From <c>CPU() - _nUpdateNotifyTick &gt; 100</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L37</c>]. STRICTLY
    /// GREATER THAN, so a delta of exactly 100 does NOT notify. <c>CPU()</c> counts whole
    /// milliseconds, so the comparison below truncates the elapsed time to whole milliseconds before
    /// applying it; comparing a fractional value would fire one millisecond early and change the
    /// notification count, which is observable.
    /// </para>
    /// </remarks>
    private const long NotifyThrottleMilliseconds = 100L;

    /// <summary>
    /// The number of statements previewed so far in the current update.
    /// </summary>
    /// <remarks>
    /// Reproduces <c>private long _nUpdateCurrent</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L14</c>].
    /// </remarks>
    private long _updateCurrent;

    /// <summary>
    /// The number of statements the current update is expected to produce.
    /// </summary>
    /// <remarks>
    /// Reproduces <c>private long _nUpdateTotal</c> [<c>:L15</c>], seeded from the modified count and
    /// then incremented in place by the backward deleted-row walk.
    /// </remarks>
    private long _updateTotal;

    /// <summary>
    /// The monotonic timestamp of the most recent progress notification.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reproduces <c>private long _nUpdateNotifyTick</c> [<c>:L16</c>], which the legacy stamps from
    /// <c>CPU()</c> at <c>:L38</c> and <c>:L53</c>.
    /// </para>
    /// <para>
    /// C-K - THE MAPPING FROM <c>CPU()</c>. <c>CPU()</c> is a MONOTONIC counter, and the legacy only
    /// ever uses it as a DELTA between two reads of that one counter - never as a wall-clock instant.
    /// The managed equivalent is therefore <see cref="TimeProvider.GetTimestamp"/> for the stamp and
    /// <see cref="TimeProvider.GetElapsedTime(long)"/> for the delta, which preserves the delta
    /// semantics exactly. A raw timestamp is stored rather than a converted millisecond value so that
    /// no precision is discarded before the comparison, and the conversion happens once, at the
    /// comparison, where the truncation to whole milliseconds is deliberate.
    /// </para>
    /// </remarks>
    private long _updateNotifyTimestamp;

    /// <summary>
    /// Creates a worker carrier.
    /// </summary>
    /// <param name="timeProvider">The service's single clock seam.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="timeProvider"/> is <see langword="null"/>.
    /// </exception>
    internal WorkerDataWindowCarrier(TimeProvider timeProvider)
        : base(timeProvider)
    {
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <see cref="CarrierThreadAffinity.WorkerThread"/>, from the header annotation
    /// <c>[运行在子线程]</c> at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L2</c>.
    /// </remarks>
    internal override CarrierThreadAffinity Affinity => CarrierThreadAffinity.WorkerThread;

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Reproduces <c>retrievestart</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L27</c>], the whole
    /// of which is <c>call super::retrievestart;if #ParentTask.of_IsCancelled() then return 1</c>.
    /// </para>
    /// <para>
    /// The script has no trailing <c>return</c>, so an uncancelled retrieve answers zero - the default
    /// for a <c>long</c> event script that falls off its end. THAT IS NOT THE SAME AS RETURNING THE
    /// ANCESTOR'S VALUE: <c>sqlpreview</c> at <c>:L44</c> writes <c>return AncestorReturnValue</c>
    /// explicitly precisely because falling off the end does NOT do it. The ancestor's answer is
    /// therefore discarded here, deliberately and visibly.
    /// </para>
    /// </remarks>
    internal override long OnRetrieveStart()
    {
        // `call super::retrievestart` [:L27] - first, always.
        _ = base.OnRetrieveStart();

        // `if #ParentTask.of_IsCancelled() then return 1` [:L27].
        if (RequireParentTask().IsCancelled)
        {
            return EventStop;
        }

        // The legacy script ends here with no return. See the remarks: zero, not the ancestor value.
        return EventContinue;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Reproduces <c>sqlpreview</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L30-L45</c>], the most
    /// intricate of the four overrides. The ancestor call comes first and is load-bearing: it is what
    /// performs the N-char rewrite [<c>n_cst_thread_task_sqlbase_ds.sru:L168-L174</c>]. The final answer
    /// is the ANCESTOR'S value, written explicitly at <c>:L44</c>.
    /// </para>
    /// <para>
    /// C-B DEFECT 1 - THE DELETE-THEN-INSERT ARM DOES NOT COUNT. See the inline annotation.
    /// </para>
    /// <para>
    /// C-B DEFECT 4 - THE PAYLOAD TRUNCATES ABOVE 65535. See the inline annotation.
    /// </para>
    /// </remarks>
    internal override long OnSqlPreview(SqlPreviewType sqlType, string sqlSyntax, DwBuffer buffer)
    {
        // `call super::sqlpreview` [:L30]. This is the call that rewrites the statement when the
        // connection uses N-char binding, so it MUST run before the accounting below.
        long ancestorReturnValue = base.OnSqlPreview(sqlType, sqlSyntax, buffer);

        // `if #ParentTask.of_IsCancelled() then return 1` [:L30].
        if (RequireParentTask().IsCancelled)
        {
            return EventStop;
        }

        // `if sqlType <> PreviewSelect! then` [:L31]. A retrieval preview is never progress.
        if (sqlType != SqlPreviewType.Select)
        {
            // ==================================================================================
            // C-B DEFECT 1. `if sqlType = PreviewDelete! and (buffer = Primary! or buffer = Filter!)
            // then return AncestorReturnValue` [:L32-L35], under the legacy's own one-word comment
            // //Delete-Then-Insert.
            //
            // THE MISSING INCREMENT IS THE BEHAVIOUR, NOT AN OVERSIGHT. A key change on a carrier
            // whose definition says updatekeyinplace=no is performed as a DELETE PLUS AN INSERT, and
            // the pair is one logical statement for progress purposes: counting the delete half as
            // well would double-count it. The delete half is identifiable exactly as this arm
            // identifies it - a delete preview against a row that is still in the Primary! or
            // Filter! buffer rather than in the Delete! buffer, because the row was never deleted by
            // the user.
            //
            // THIS ARM IS EXERCISED, NOT HYPOTHETICAL. The golden-master fixture declares
            // updatekeyinplace=no [ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14], and its `id`
            // column is both key and identity [:L8], so any key edit takes this path.
            //
            // Note also that it returns the ANCESTOR's value and not zero - it is a short circuit out
            // of the accounting, not an abort.
            // ==================================================================================
            if (sqlType == SqlPreviewType.Delete
                && (buffer is DwBuffer.Primary or DwBuffer.Filter))
            {
                return ancestorReturnValue;
            }

            // `_nUpdateCurrent ++` [:L36].
            _updateCurrent++;

            // `if CPU() - _nUpdateNotifyTick > 100 or _nUpdateCurrent = 1 or
            //     _nUpdateCurrent >= _nUpdateTotal then` [:L37] - three conditions, ANY of which
            // fires: the throttle has elapsed, this is the first statement, or the count has reached
            // the total. The first and last exist so that an update always emits at least a start and
            // a finish however fast it runs.
            if (IsProgressNotificationDue())
            {
                // `_nUpdateNotifyTick = CPU()` [:L38] - stamped BEFORE the notification, so the
                // handler's own duration does not extend the next interval. The order is preserved.
                _updateNotifyTimestamp = Clock.GetTimestamp();

                // ==============================================================================
                // C-B DEFECT 4. `MakeLong(_nUpdateCurrent,_nUpdateTotal)` [:L39], current in the LOW
                // word and total in the HIGH word - the order the legacy's own comment states at
                // n_cst_threading_task_sqlupdate.sru:L32,
                // `lparam:(low word:current,high word:total)`, and the same order used at
                // n_cst_threading_task_sqlquery.sru:L254 and :L268.
                //
                // THE HALVES ARE 16 BITS, SO ANY VALUE ABOVE 65535 TRUNCATES. PowerBuilder's `ulong`
                // is 32 bits and its `uint` is 16, so Bits.MakeLong takes two ushort operands by
                // design, faithfully. This is REACHABLE: the query task's chunk size defaults to
                // 10000 and a result may hold far more rows than 65535, so a large update's progress
                // stream genuinely wraps.
                //
                // IT IS OBSERVABLE LEGACY BEHAVIOUR IN THE NOTIFICATION STREAM AND IS THEREFORE
                // PRESERVED, NOT WIDENED. The narrowing casts are explicit, and `unchecked` states
                // that the wrap is intended rather than relying on the project's default arithmetic
                // mode - so enabling overflow checking later cannot silently turn a preserved
                // behaviour into an exception.
                // ==============================================================================
                long payload = unchecked(
                    (long)Bits.MakeLong((ushort)_updateCurrent, (ushort)_updateTotal));

                // `if #ParentTask.Event OnNotify(n_cst_threading_task_sqlupdate.NCD_PROGRESS, ...,"")
                //     = 1 then return 1` [:L39-L41]. Here a handler answering 1 means STOP, which is
                // the ordinary convention - contrast OnRetrieveRow, where it is inverted.
                long handlerAnswer = RequireParentTask().OnNotify(
                    (long)SqlUpdateTaskNotifyCode.Progress,
                    payload,
                    string.Empty);

                if (handlerAnswer == EventStop)
                {
                    return EventStop;
                }
            }
        }

        // `return AncestorReturnValue` [:L44], written explicitly by the legacy.
        return ancestorReturnValue;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Reproduces <c>updatestart</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L47-L61</c>]: the
    /// ancestor call, the cancellation check, then the progress accounting reset and the deleted-row
    /// contribution to the total.
    /// </para>
    /// <para>
    /// C-B DEFECT 2 AND RISK R9 - THE BACKWARD WALK. See the inline annotation; it is the most
    /// dangerous loop in this file to "correct".
    /// </para>
    /// </remarks>
    internal override long OnUpdateStart()
    {
        // `call super::updatestart` [:L47] - first, always. Its answer is discarded because the
        // legacy script falls off its end rather than returning it.
        _ = base.OnUpdateStart();

        // `if #ParentTask.of_IsCancelled() then return 1` [:L49].
        if (RequireParentTask().IsCancelled)
        {
            return EventStop;
        }

        // `_nUpdateCurrent = 0` [:L51], `_nUpdateTotal = ModifiedCount()` [:L52],
        // `_nUpdateNotifyTick = CPU()` [:L53].
        _updateCurrent = 0L;
        _updateTotal = ModifiedCount();
        _updateNotifyTimestamp = Clock.GetTimestamp();

        // ======================================================================================
        // C-B DEFECT 2 / R9 ITEM 1. Verbatim shape of [:L55-L60]:
        //
        //     for nRow = DeletedCount() to 1 step -1
        //         choose case GetItemStatus(nRow,0,Delete!)
        //             case NotModified!,DataModified!
        //                 _nUpdateTotal ++
        //         end choose
        //     next
        //
        // THE DIRECTION IS DELIBERATE AND MUST NOT BE "CORRECTED". Reversing such a loop is the
        // canonical way to produce wrong data that still passes a row-count assertion, because the
        // count stays right while the rows shift; the sibling precedent is the Filter! buffer walk at
        // n_cst_thread_task_sqlupdate.sru:L237, which runs backward because that buffer's row order
        // is documented as inverted relative to the source [:L235].
        //
        // BOTH BOUNDS ARE ONE-BASED ROW NUMBERS. The descent terminates at FirstRowNumber, not at 0,
        // and the loop variable is never rebased.
        //
        // THE STATUS TEST IS DELEGATED, NOT RE-DERIVED. ItemStatusMachine.IsDeleteCountable is the
        // legacy's two-status case list with no default arm, so a row that was inserted, edited and
        // then deleted generates no SQL and is correctly excluded from the denominator.
        //
        // WHY THE LOOP IS WRITTEN OUT HERE RATHER THAN CALLING
        // ItemStatusMachine.CountDeleteCountable, which expresses the same walk: the legacy
        // increments THE SAME FIELD the modified count seeded, in place. Computing a separate subtotal
        // and adding it would be arithmetically identical but would no longer diff against its
        // locator, and this is the one loop in the file where that correspondence is worth the most.
        // ======================================================================================
        for (long rowNumber = DeletedCount();
            rowNumber >= ItemStatusMachine.FirstRowNumber;
            rowNumber--)
        {
            ItemStatus rowStatus = GetItemStatus(
                rowNumber,
                ItemStatusMachine.RowStatusColumn,
                DwBuffer.Delete);

            if (ItemStatusMachine.IsDeleteCountable(rowStatus))
            {
                _updateTotal++;
            }
        }

        // The legacy script ends here with no return, so the answer is zero.
        return EventContinue;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Reproduces <c>retrieverow</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L63-L72</c>]: the
    /// ancestor call, the cancellation check, then row-cap handling.
    /// </para>
    /// <para>
    /// C-B DEFECT 3 - A HANDLER ANSWERING 1 MEANS "KEEP GOING" HERE. See the inline annotation; this
    /// is the one place in the file where that answer is inverted.
    /// </para>
    /// </remarks>
    internal override long OnRetrieveRow(long row)
    {
        // `call super::retrieverow` [:L63] - first, always. Answer discarded; the legacy script falls
        // off its end.
        _ = base.OnRetrieveRow(row);

        // `if #ParentTask.of_IsCancelled() then return 1` [:L63].
        if (RequireParentTask().IsCancelled)
        {
            return EventStop;
        }

        // `if _nMaxRows > 0 and row > _nMaxRows then` [:L64]. A cap of zero - or, as SetMaxRows notes,
        // a negative one - means no limit, and `row` is the one-based number of the row that has just
        // arrived, so the cap is exceeded only on the row AFTER the last permitted one.
        if (MaxRows > 0L && row > MaxRows)
        {
            // `#ParentTask.Event OnNotify(n_cst_threading_task_sqlquery.NCD_MAXROWS,row,"")` [:L65].
            // NOTE THE PAYLOAD: the row number passed STRAIGHT THROUGH, with no word packing and
            // therefore no truncation. Contrast the progress payload in OnSqlPreview, which packs two
            // 16-bit words and does truncate. Note also the notify code: it belongs to the QUERY
            // task's set, whose value 1 means the row cap, while the UPDATE task's value 1 means
            // progress - the exact collision the two separate enumerations exist to make
            // unconfusable.
            long handlerAnswer = RequireParentTask().OnNotify(
                (long)SqlQueryTaskNotifyCode.MaxRows,
                row,
                string.Empty);

            // ==================================================================================
            // C-B DEFECT 3 - THE COUNTER-INTUITIVE ARM. `if ... = 1 then _nMaxRows = 0; return 0`
            // [:L65-L68]. EVERYWHERE ELSE IN THIS CODEBASE A HANDLER ANSWERING 1 MEANS ABORT. HERE IT
            // MEANS THE OPPOSITE: the cap is LIFTED and the retrieve CONTINUES, for the whole
            // remainder of the retrieve, because clearing the cap to zero makes the guard above
            // permanently unsatisfiable.
            // The rows-exceeded flag is deliberately NOT set on this arm, so of_IsRowsExceeded stays
            // false and a caller cannot tell the cap was ever reached. Reproduced exactly; do not
            // "fix" the polarity and do not set the flag here.
            // ==================================================================================
            if (handlerAnswer == EventStop)
            {
                MaxRows = 0L;

                return EventContinue;
            }

            // `_bRowsExceeded = true` then `return 1` [:L69-L70] - the enforcement arm, which is the
            // one that stops the retrieve and the only one that raises the flag.
            RowsExceeded = true;

            return EventStop;
        }

        // The legacy script ends here with no return, so the answer is zero.
        return EventContinue;
    }

    /// <summary>
    /// Whether a progress notification is due, by any of the legacy's three conditions.
    /// </summary>
    /// <returns><see langword="true"/> when the notification should be raised.</returns>
    /// <remarks>
    /// <para>
    /// Reproduces the composite condition at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L37</c>:
    /// <c>CPU() - _nUpdateNotifyTick &gt; 100 or _nUpdateCurrent = 1 or
    /// _nUpdateCurrent &gt;= _nUpdateTotal</c>. The order of the three disjuncts is preserved, which
    /// matters only in that a reader can diff it; the result is order-independent.
    /// </para>
    /// <para>
    /// C-H. THE ELAPSED TIME IS TRUNCATED TO WHOLE MILLISECONDS BEFORE THE COMPARISON, because
    /// <c>CPU()</c> yields whole milliseconds and the comparison is STRICTLY greater than. Comparing a
    /// fractional value would make a 100.4 millisecond gap notify where the legacy's integer
    /// arithmetic would not, and the notification count is exactly what the characterization
    /// comparison measures.
    /// </para>
    /// <para>
    /// The third disjunct uses <c>&gt;=</c> rather than <c>=</c>, so a total that undercounts - which
    /// the modified count can do, since it is seeded before the delete-then-insert pairs are known -
    /// still terminates the notification stream rather than suppressing its final notification.
    /// </para>
    /// </remarks>
    private bool IsProgressNotificationDue()
    {
        long elapsedMilliseconds = (long)Clock.GetElapsedTime(_updateNotifyTimestamp).TotalMilliseconds;

        return elapsedMilliseconds > NotifyThrottleMilliseconds
            || _updateCurrent == 1L
            || _updateCurrent >= _updateTotal;
    }
}

#endregion

#region The carrier factory - n_cst_thread_task_sqlbase.sru:L553-L557

/// <summary>
/// Creates the result carrier whose thread affinity matches the thread it will be used on.
/// </summary>
/// <remarks>
/// <para>
/// Reproduces the selection at
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L553-L557</c>:
/// </para>
/// <code>
/// if #ParentThread.of_IsMainThread() then
///     ds = Create n_cst_thread_task_sqlbase_ds
/// else
///     ds = Create n_cst_thread_task_sqlbase_ds_mt
/// end if
/// </code>
/// <para>
/// The selection is a factory rather than a constructor choice at each call site because it is the
/// point at which the marshalling boundary is decided, and there is exactly one such point in the
/// legacy. Note what the legacy does immediately afterwards and what this factory therefore does NOT
/// do: it assigns the data object and registers the carrier in the parent thread's cache
/// [<c>:L558-L565</c>], and it raises the initialization event [<c>:L566</c>]. Both belong to
/// <c>Tasks/SqlTaskBase.cs</c>, which owns the cache; a caller here must call
/// <see cref="DataWindowCarrier.OnInit"/> itself, and the carrier fails fast if it does not.
/// </para>
/// </remarks>
internal static class DataWindowCarrierFactory
{
    /// <summary>
    /// Creates a carrier for the requested thread affinity.
    /// </summary>
    /// <param name="affinity">Which thread the carrier will be used on.</param>
    /// <param name="timeProvider">The service's single clock seam.</param>
    /// <returns>
    /// A <see cref="DataWindowCarrier"/> for <see cref="CarrierThreadAffinity.MainThread"/> or a
    /// <see cref="WorkerDataWindowCarrier"/> for <see cref="CarrierThreadAffinity.WorkerThread"/>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="affinity"/> is not a declared affinity.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="timeProvider"/> is <see langword="null"/>.
    /// </exception>
    internal static DataWindowCarrier Create(
        CarrierThreadAffinity affinity,
        TimeProvider timeProvider)
    {
        return affinity switch
        {
            // `ds = Create n_cst_thread_task_sqlbase_ds` [:L554].
            CarrierThreadAffinity.MainThread => new DataWindowCarrier(timeProvider),

            // `ds = Create n_cst_thread_task_sqlbase_ds_mt` [:L556].
            CarrierThreadAffinity.WorkerThread => new WorkerDataWindowCarrier(timeProvider),

            _ => throw new ArgumentOutOfRangeException(
                nameof(affinity),
                affinity,
                "Affinity must be MainThread or WorkerThread. The legacy has exactly two carrier "
                    + "types and this port declares no third."),
        };
    }

    /// <summary>
    /// Creates a carrier for a thread described the way the legacy describes it.
    /// </summary>
    /// <param name="isMainThread">
    /// The answer <c>#ParentThread.of_IsMainThread()</c> would give
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L553</c>].
    /// </param>
    /// <param name="timeProvider">The service's single clock seam.</param>
    /// <returns>The carrier whose affinity matches that thread.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="timeProvider"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// The boolean-shaped overload exists so that the one call site which genuinely has a boolean -
    /// the legacy branch itself - reads as its original does, while every other caller states the
    /// affinity it means.
    /// </remarks>
    internal static DataWindowCarrier CreateForThread(bool isMainThread, TimeProvider timeProvider)
    {
        return Create(
            isMainThread ? CarrierThreadAffinity.MainThread : CarrierThreadAffinity.WorkerThread,
            timeProvider);
    }
}

#endregion

#region Ownership transfer - the main-thread path that runs no codec at all

/// <summary>
/// The main-thread handover in which a carrier changes owner BY REFERENCE, with no codec, no blob and
/// no serialization.
/// </summary>
/// <remarks>
/// <para>
/// Reproduces <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L84-L90</c>, which
/// runs BEFORE the codec selector at <c>:L93</c> and short-circuits it entirely:
/// </para>
/// <code>
/// if #ParentThread.of_IsMainThread() then
///     if Not tasking._of_HasReceiver() and Not _bCache then
///         tasking.Event OnDataMove(data)
///         SetNull(data)
///         return RetCode.OK
///     end if
/// end if
/// </code>
/// <para>
/// THIS IS THE BEHAVIOURAL PAYOFF OF THE TWO-CARRIER SPLIT. The path exists only because the carrier
/// and its consumer share an address space, which is precisely what the affinity records - so
/// collapsing <c>_ds</c> and <c>_ds_mt</c> into one type would erase a real execution path rather
/// than merely a name. The receiving side takes the carrier itself: <c>ondatamove</c> at
/// <c>n_cst_threading_task_sqlquery.sru</c> destroys whatever it held and adopts the argument.
/// </para>
/// <para>
/// C-K. The <c>SetNull(data)</c> is REPRODUCED, not dropped as a garbage-collection nicety. It is the
/// same discipline as hazard 2 of <c>docs/PB多线程绕坑提示.md</c>, which requires a worker to null its
/// reference to a main-thread object once it is done with it, and its purpose here is to make
/// OWNERSHIP UNAMBIGUOUS: after the handover exactly one party holds the carrier, so a later write
/// through a stale reference is impossible by construction rather than by convention.
/// </para>
/// </remarks>
internal static class DataWindowCarrierOwnership
{
    /// <summary>
    /// Whether a carrier can be handed over by reference instead of being serialized.
    /// </summary>
    /// <param name="affinity">
    /// The affinity of the thread publishing the result -
    /// <c>#ParentThread.of_IsMainThread()</c> [<c>:L84</c>].
    /// </param>
    /// <param name="hasReceiver">
    /// Whether the tasking object has a receiver installed -
    /// <c>tasking._of_HasReceiver()</c> [<c>:L85</c>]. A receiver means some other object must be
    /// populated, so the carrier cannot simply change hands.
    /// </param>
    /// <param name="cacheEnabled">
    /// Whether the task caches its result - <c>_bCache</c> [<c>:L85</c>]. A cache means the task keeps
    /// the carrier, so it cannot give it away.
    /// </param>
    /// <returns>
    /// <see langword="true"/> only when the publisher is on the main thread AND there is no receiver
    /// AND caching is off.
    /// </returns>
    /// <remarks>
    /// A pure predicate, so the decision is testable with no task, no thread and no database (C-H).
    /// All three conditions are required: the legacy nests the second inside the first and conjoins
    /// the two negations, and any one of them failing sends the result down a codec path instead.
    /// </remarks>
    internal static bool CanMoveWithoutSerialization(
        CarrierThreadAffinity affinity,
        bool hasReceiver,
        bool cacheEnabled)
    {
        return affinity == CarrierThreadAffinity.MainThread && !hasReceiver && !cacheEnabled;
    }

    /// <summary>
    /// Hands a carrier over to a receiver and drops the sender's own reference to it.
    /// </summary>
    /// <param name="carrier">
    /// The carrier to hand over. SET TO <see langword="null"/> ON RETURN, reproducing
    /// <c>SetNull(data)</c>.
    /// </param>
    /// <param name="receive">
    /// The receiving action, reproducing <c>tasking.Event OnDataMove(data)</c>.
    /// </param>
    /// <returns><c>RetCode.OK</c>, as the legacy returns at <c>:L89</c>.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="receive"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="carrier"/> is already <see langword="null"/>, which means the handover has
    /// already happened or never had anything to hand over.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The order is the legacy's: the receiver takes the carrier FIRST [<c>:L87</c>] and only then is
    /// the sender's reference dropped [<c>:L88</c>]. Dropping first would leave the carrier
    /// unreachable at the moment the receiver needed it.
    /// </para>
    /// <para>
    /// Handing over a carrier that is already <see langword="null"/> throws rather than answering a
    /// benign code, which is the fail-fast posture again: a double handover means two parties believe
    /// they own the same result, and that is precisely the class of fault the <c>SetNull</c> exists to
    /// prevent. NO CODEC RUNS ON THIS PATH - neither <c>Buffers/ChangesetCodec.cs</c> nor
    /// <c>Buffers/FullStateCodec.cs</c> is involved, and no blob is produced.
    /// </para>
    /// </remarks>
    internal static long Move(ref DataWindowCarrier? carrier, Action<DataWindowCarrier> receive)
    {
        ArgumentNullException.ThrowIfNull(receive);

        DataWindowCarrier moving = carrier
            ?? throw new InvalidOperationException(
                "There is no carrier to hand over. A carrier may be moved once; after the move the "
                    + "sender's reference is null by design, reproducing SetNull(data).");

        // `tasking.Event OnDataMove(data)` [:L87] - the receiver adopts the carrier itself.
        receive(moving);

        // `SetNull(data)` [:L88] - the sender relinquishes its reference so that ownership is
        // unambiguous afterwards.
        carrier = null;

        // `return RetCode.OK` [:L89] - the framework convention, where success is zero.
        return RetCode.OK;
    }
}

#endregion
