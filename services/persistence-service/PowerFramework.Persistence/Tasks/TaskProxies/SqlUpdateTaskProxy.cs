// ==================================================================================================
//  SqlUpdateTaskProxy.cs - THE CALLER-SIDE HALF OF THE UPDATE PROXY/WORKER PAIR
// ==================================================================================================
//
//  The managed port of the 344-line read-only object
//  ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru, which is the CALLER-SIDE
//  control object for an asynchronous DataWindow update. It is the engine behind contract C-06
//  persistence.v1.UpdateService's caller half.
//
//  --------------------------------------------------------------------------------------------
//  THREAD AFFINITY IS A CONTRACT, NOT COMMENTARY (C-K)
//  --------------------------------------------------------------------------------------------
//  The oracle's export comment marks this object [运行在当前线程] - RUNS ON THE CALLING THREAD
//  [n_cst_threading_task_sqlupdate.sru:L2] - while its worker counterpart n_cst_thread_task_sqlupdate
//  is marked [运行在子线程], RUNS ON THE WORKER THREAD [n_cst_thread_task_sqlupdate.sru:L2].
//
//  The split is MEASURED, not assumed. Across all 15 objects of ws_objects/pfw.thread.ext.pbl.src:
//
//        6  worker-thread  [运行在子线程]   n_cst_thread_task_sqlbase, _sqlbase_ds_mt, _sqlbase_hook,
//                                          _sqlcommand, _sqlquery, _sqlupdate
//        1  main-thread    [运行在主线程]   n_cst_thread_task_sqlbase_ds - EXACTLY ONE, and it is the
//                                          result carrier, not a task
//        4  calling-thread [运行在当前线程] n_cst_threading_task_sqlbase, _sqlcommand, _sqlquery,
//                                          _sqlupdate  <- THIS FILE IS ONE OF THESE FOUR
//        4  no affinity marker             dberrordata.srs, transactiondata.srs, n_cst_thread_trans,
//                                          n_cst_thread_trans_pool
//
//  🔴 THE PAIR IS DELIBERATELY NOT FLATTENED INTO ONE ASYNC METHOD (AAP §0.4.5.4). Collapsing the
//  proxy and the worker into a single `async Task<...>` would erase the boundary the affinity
//  annotations describe, and that boundary is where the two halves' responsibilities divide: the
//  worker fires per-invocation reports, THIS side accumulates them. A flattened method has nowhere
//  to hold the accumulation and would have to sum inside the worker, which is a behavioural change
//  (see THE TWO WORK ITEMS below). Async request/response with a CancellationToken is expressed
//  through the base's Cancellation/IsCancelled members; both types remain distinct and separately
//  DI-registrable (C-J).
//
//  --------------------------------------------------------------------------------------------
//  THE TWO WORK ITEMS THE SIBLING FOLDERS EXPLICITLY DELEGATED HERE
//  --------------------------------------------------------------------------------------------
//  1. ACCUMULATION AND APPEND. Concurrency/IdentityColumnResolver.cs states that the write-back
//     "belongs to the caller-side proxy pair under Tasks/TaskProxies/. IT IS NOT IMPLEMENTED IN
//     THIS FILE", and Tasks/SqlUpdateTask.cs states that "ACCUMULATION IS THE PROXY'S JOB, NOT
//     THIS TASK'S". Both are verified against the oracle: on the multi-table path the worker fires
//     OnIdentityColumnDataRetrieved [n_cst_thread_task_sqlupdate.sru:L243] and OnUpdated [:L247]
//     from inside _of_Update, which the loop at [:L364-L369] calls ONCE PER TABLE. The totals
//     therefore exist ONLY on this side.
//
//  2. THE IDENTITY WRITE-BACK. of_refreshidentitydata [:L120-L205] applies the collected values to
//     a caller-supplied object. Its four measured quirks are reproduced below, each annotated at
//     its point of reproduction.
//
//  --------------------------------------------------------------------------------------------
//  🔴 THE MOST DANGEROUS LINE IN THE REFACTOR: COLLECT BACKWARD, APPLY FORWARD (R9)
//  --------------------------------------------------------------------------------------------
//  The COLLECTOR walks the Filter buffer BACKWARD - `for nIndex = nCount to 1 step -1`
//  [n_cst_thread_task_sqlupdate.sru:L237] - and the oracle states why on the line above it:
//  "过滤缓冲区的数据与数据源(调用GetChanges的对象)的过滤缓冲区的数据顺序是相反的" [:L235], i.e. the
//  filter buffer's row order is INVERTED relative to the data source that produced the changeset.
//
//  The APPLIER in this file walks the Filter buffer FORWARD [n_cst_threading_task_sqlupdate.sru:L154,
//  :L163 and :L188, :L197], exactly as it walks Primary [:L138, :L147 and :L172, :L181].
//
//  BACKWARD COLLECTION PAIRED WITH FORWARD APPLICATION IS WHAT RE-ALIGNS THE VALUES. Each half looks
//  like a bug in isolation and neither is. "Correcting" either one produces identity values written
//  to the WRONG ROWS while every row count still matches, so a row-count assertion cannot catch it -
//  which is why the parity test asserts the pairing end to end rather than the counts.
//
//  --------------------------------------------------------------------------------------------
//  NOTIFY CODES: TWO ENUMERATIONS, ONE SHARED VALUE, NEVER MERGED (C-K)
//  --------------------------------------------------------------------------------------------
//  This object declares exactly one notify code: `Constant Long NCD_PROGRESS = 1` with the payload
//  comment `lparam:(low word:current,high word:total)` [n_cst_threading_task_sqlupdate.sru:L32]. The
//  sibling query proxy declares `Constant Long NCD_MAXROWS = 1`
//  [n_cst_threading_task_sqlquery.sru:L28] - THE SAME NUMERIC VALUE for a completely unrelated
//  notification. In PowerBuilder the two never collide because each is a per-class constant; a single
//  merged .NET enumeration WOULD alias them, and a caller switching on the merged value would treat a
//  row-cap breach as an update progress report.
//
//  Buffers/DataWindowBuffers.cs already declares BOTH as separate enumerations preserving the shared
//  value - SqlUpdateTaskNotifyCode.Progress = 1 and SqlQueryTaskNotifyCode.MaxRows = 1 - and already
//  ENCODES the payload at Buffers/DataWindowBuffers.cs:3358 via
//  `Bits.MakeLong((ushort)_updateCurrent,(ushort)_updateTotal)`. This file therefore declares NO enum
//  and NO encoder: it consumes the existing enumeration and owns only the caller-side DECODE, which is
//  what a caller-side constant is for. There is deliberately no identifier named NCD_PROGRESS here.
//
//  --------------------------------------------------------------------------------------------
//  THE TWO THREADING HAZARDS (docs/PB多线程绕坑提示.md, 5 lines, both honoured)
//  --------------------------------------------------------------------------------------------
//  HAZARD 1 [:L1-L4] - a worker thread synchronously calling a main-thread function or event that
//  RETURNS a string or blob can fault, because a pending Post message on an object being destroyed
//  conflicts; the stated remedy is to hand the payload back BY REFERENCE instead of returning it.
//  Three shapes in this file are that remedy, and none of them is a style choice:
//      * of_setupdatedata declares `ref blob blbdata` [:L61, :L236-L239] - reproduced as a `ref`
//        parameter on SetUpdateData.
//      * of_setupdateobject captures through `dw.GetChanges(ref blbData)` [:L255, :L265] - the count
//        is RETURNED and the payload travels OUT, reproduced as GetChanges(out CarrierState?).
//      * onidentitycolumndataretrieved declares TWO `ref long ...[]` out-parameters [:L19, :L71] -
//        reproduced as the inward read-only lists ISqlUpdateTaskProxy publishes, which carry the same
//        no-large-return guarantee without exposing a mutable buffer.
//  HAZARD 2 [:L5] - a worker holding a main-thread object must SetNull it in OnUninit to drop the
//  reference. The mirror of that discipline on this side is that the retained update object is
//  released by of_reset [:L94], and Reset below nulls it for the same reason.
//
//  --------------------------------------------------------------------------------------------
//  WHAT IS DELIBERATELY NOT HERE
//  --------------------------------------------------------------------------------------------
//  NO DUPLICATED TYPE. Every enum and record this file needs already exists in a sibling and is
//  CONSUMED: SqlUpdateTaskNotifyCode and ItemStatusMachine from Buffers/, ResolvedIdentityColumnData
//  and OneBasedIndex from Concurrency/, DbErrorData from Errors/, DwBuffer and ItemStatus from the
//  generated common.v1, RetCode from the shared kernel. In particular there is NO second IdColData -
//  ResolvedIdentityColumnData already carries the oracle's three fields in the oracle's order.
//
//  NO SQL, NO CONNECTION, NO ENGINE (C-E). This file builds no statement, opens no connection and
//  names no database, table or column. The one statement-bearing value it touches is the latched
//  error's SqlSyntax, and it only ever STORES it - see the OnDbError override for the C-F reasoning.
//
//  NO LISTENER, NO ROUTE, NO ENDPOINT (C-G). Reachable only through the authorized C-06 methods.
//
//  NO CLOCK (C-H). Nothing here reads DateTime.UtcNow, DateTime.Now, Environment.TickCount or a
//  Stopwatch. The base publishes the injected TimeProvider as `Clock`; this file needs no elapsed
//  time at all, and a sweep of the oracle confirms the absence is faithful rather than an omission -
//  n_cst_threading_task_sqlupdate.sru contains no CPU(), Now() or Today() call.
//
//  NO GLOBAL (C-J). The oracle declares
//  `global n_cst_threading_task_sqlupdate n_cst_threading_task_sqlupdate` [:L21] - an auto-instance
//  shadowing its own type name. This type has no static mutable state and is registered in the
//  container. 🔴 IT IS A DISTINCT REGISTRATION FROM Tasks/SqlUpdateTask.cs: Program.cs must register
//  BOTH, the proxy as ISqlUpdateTaskProxy for the worker to fire into, and the worker as itself.
//
//  NO SCREAMING_SNAKE DECLARATION. The repository-root .editorconfig scopes its CA1707 and IDE1006
//  relaxations to a named roster of files, and TaskProxies/ IS NOT ON IT - verified against the
//  artifact, where the only persistence-service entries are Sql/ClauseModifier.cs and
//  Sql/Paging/IPagingRewriter.cs. Under TreatWarningsAsErrors an underscored member here would be a
//  BUILD BREAK, not a style nit, so NCD_PROGRESS, TASK_TYPE and IDCOLDATA appear only inside comments
//  quoting the oracle. Values are preserved; identifier spellings are PascalCase (AAP §0.4.5.3).
//
//  --------------------------------------------------------------------------------------------
//  R9 - THE ONE-BASED AUDIT FOR THIS FILE, IN FULL
//  --------------------------------------------------------------------------------------------
//  PowerBuilder arrays are ONE-BASED and UpperBound returns THE LAST VALID INDEX; C# lists are
//  zero-based and Count is ONE PAST THE END. Every one-based site here is audited:
//
//    SITE                                        ORACLE          HOW IT IS HANDLED
//    append at UpperBound(x) + 1                 [:L73]          List.Add, whose landing position IS
//                                                                OneBasedIndex.AppendIndex by
//                                                                definition; pinned by test, NOT by an
//                                                                invented runtime assertion
//    emptiness test UpperBound(_idColDatas) = 0  [:L128-L129]    OneBasedIndex.UpperBound compared
//                                                                with OneBasedIndex.EmptyUpperBound
//    table loop `for nIndex = 1 to nCount`       [:L143, :L159,  explicit one-based int loop from
//                                                :L177, :L193]   OneBasedIndex.FirstIndex, indexed
//                                                                through OneBasedIndex.ToZeroBased
//    value-array bound UpperBound(...[1]....)    [:L136, :L151,  OneBasedIndex.UpperBound - and the
//                                                :L170, :L185]   TABLE-1 READ IS PRESERVED, see the
//                                                                latent defect note at its site
//    value cursor i, tested `if i > n`           [:L141-L142]    one-based int cursor tested with
//                                                                OneBasedIndex.IsWithin
//    identity column id                          [:L144, :L160]  carried as the `long` the resolver
//                                                                produced, NEVER rebased and never
//                                                                narrowed to int
//    GetNextModified seeded 0, terminated at 0   [:L138, :L147]  ItemStatusMachine.BeforeFirstRow and
//                                                                .NoMoreModifiedRows - the ROW domain
//                                                                is `long`, deliberately NOT routed
//                                                                through the int-domain
//                                                                OneBasedIndex.Range
//    ordinal count in the Row rewrite            [:L323-:L325]   explicit long ordinal from
//                                                                ItemStatusMachine.FirstRowNumber's
//                                                                domain, never rebased
//
//  🔴 NO REVERSE ITERATION IS EVER "CORRECTED". See COLLECT BACKWARD, APPLY FORWARD above.
//
//  --------------------------------------------------------------------------------------------
//  RULES POSITION
//  --------------------------------------------------------------------------------------------
//  review_rules returns exactly one line, "No user rules provided." No user-specified rule governs
//  this file and none is invented in its place. The enterprise baseline of AAP §0.7.2 applies
//  instead, and the binding constraints are the plan's own non-rule inventory; those bearing on this
//  file are C-A, C-B, C-C, C-D, C-E, C-F, C-G, C-H, C-I, C-J, C-K and risk R9, each cited inline
//  where it is discharged.
//
// ==================================================================================================

using Microsoft.Extensions.Logging;
using PowerFramework.Contracts.Common.V1;
using PowerFramework.Contracts.Persistence.V1;
using PowerFramework.Persistence.Buffers;
using PowerFramework.Persistence.Concurrency;
using PowerFramework.Persistence.Errors;
using PowerFramework.Shared.Diagnostics;

// `RetCode` is declared TWICE in this solution - PowerFramework.Shared.Kernel.RetCode carries the
// in-process `long` algebra ported from ws_objects/pfw.shared.pbl.src/retcode.sru, while
// PowerFramework.Contracts.Common.V1.RetCode is the generated protobuf wrapper whose nested enum is
// the wire form. Importing both unqualified is CS0104, so the alias below names the one this file
// means, exactly as SqlTaskProxyBase.cs and SqlUpdateTask.cs do.
using Bits = PowerFramework.Shared.Kernel.Bits;
using Predicates = PowerFramework.Shared.Kernel.Predicates;
using RetCode = PowerFramework.Shared.Kernel.RetCode;

namespace PowerFramework.Persistence.Tasks.TaskProxies;

#region The write-back target seam - `readonly powerobject object`, narrowed to what is consumed

/// <summary>
/// Which of the oracle's three <c>TypeOf()</c> arms a write-back target takes.
/// </summary>
/// <remarks>
/// <para>
/// The oracle dispatches on <c>object.TypeOf()</c> in two places - the write-back
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L131</c>] and the
/// update-object setter [<c>:L249</c>] - and BOTH have exactly three arms: <c>DataWindow!</c>,
/// <c>DataStore!</c>, and a <c>case else</c> answering <see cref="RetCode.E_INVALID_TYPE"/>
/// [<c>:L200-L201</c>, <c>:L270-L271</c>].
/// </para>
/// <para>
/// <b><see cref="Other"/> exists so the third arm stays REACHABLE AND TESTABLE.</b> Had the target
/// been typed as bare <see cref="object"/> with a runtime type test, the else arm would mean "not one
/// of our interfaces", which is neither assertable against a substituted double nor meaningful in a
/// headless service. Carrying the discrimination as data keeps all three arms exercisable with no
/// DataWindow and no database (C-H).
/// </para>
/// </remarks>
internal enum IdentityWriteBackTargetKind
{
    /// <summary>
    /// The <c>DataWindow!</c> arm [<c>:L132</c>, <c>:L250</c>] - the arm that brackets its body with
    /// redraw suspension and restoration.
    /// </summary>
    DataWindow,

    /// <summary>
    /// The <c>DataStore!</c> arm [<c>:L167</c>, <c>:L260</c>] - structurally identical to the
    /// <c>DataWindow!</c> arm except that it does NOT bracket its body.
    /// </summary>
    DataStore,

    /// <summary>
    /// Anything else - the <c>case else</c> arm answering <see cref="RetCode.E_INVALID_TYPE"/>
    /// [<c>:L200-L201</c>, <c>:L270-L271</c>].
    /// </summary>
    Other,
}

/// <summary>
/// The caller-supplied DataWindow or DataStore this proxy writes identity values back onto and
/// captures a changeset from - the <c>readonly powerobject object</c> parameter of
/// <c>of_refreshidentitydata</c> [<c>:L54, :L120</c>] and <c>of_setupdateobject</c>
/// [<c>:L62-L63, :L241</c>], narrowed to the members those two functions actually consume.
/// </summary>
/// <remarks>
/// <para>
/// <b>WHY AN INJECTED SURFACE RATHER THAN BARE <see cref="object"/>.</b> AAP §0.4.5.2 maps
/// <c>powerobject</c> onto <see cref="object"/>, and a bare object is what a faithful signature would
/// carry if the body did nothing but store it. This body does far more: it suspends redraw, walks two
/// buffers, reads row statuses and writes items through TWO DIFFERENT ACCESSORS. Declaring exactly
/// those members is the same technique every sibling in this service already applies - <c>IUpdateTarget</c>
/// and <c>IUpdateTargetModifier</c> in <c>Concurrency/</c>, <c>IIdentityValueSource</c> and
/// <c>IIdentityColumnMetadata</c> in <c>Concurrency/</c>, <c>ICarrierParentTask</c> in <c>Buffers/</c> -
/// and it is what makes every branch below reachable with NO DataWindow, NO thread and NO database
/// (C-H). The three-arm dispatch survives intact through <see cref="Kind"/>.
/// </para>
/// <para>
/// <b>NO SIBLING DECLARES THIS SURFACE.</b> <c>Concurrency/IdentityColumnResolver.cs</c> declares the
/// READ side of the round trip (<c>IIdentityValueSource</c>, <c>IIdentityColumnMetadata</c>) and states
/// in its own header that the write-back "IS NOT IMPLEMENTED IN THIS FILE" and belongs here, so the
/// write side is this file's to declare. Nothing below duplicates a sibling type.
/// </para>
/// </remarks>
internal interface IIdentityWriteBackTarget
{
    /// <summary>
    /// Which <c>TypeOf()</c> arm this target takes [<c>:L131</c>, <c>:L249</c>].
    /// </summary>
    IdentityWriteBackTargetKind Kind { get; }

    /// <summary>
    /// Suspends or restores repaint - <c>dw.SetRedraw(false)</c> [<c>:L134</c>] and
    /// <c>dw.SetRedraw(true)</c> [<c>:L166</c>].
    /// </summary>
    /// <param name="redraw">
    /// <see langword="false"/> to suspend, <see langword="true"/> to restore.
    /// </param>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>THE ARM ASYMMETRY IS REPRODUCED; THE RENDERING IS NOT (C-B with C-D).</b> The oracle's
    /// <c>DataWindow!</c> arm brackets its whole body with these two calls while the structurally
    /// identical <c>DataStore!</c> arm [<c>:L167-L199</c>] calls neither, and that difference is
    /// observable to a caller that watches the target. It is therefore preserved as an OBSERVABLE
    /// NOTIFICATION ON THIS SEAM: the write-back invokes it on the <c>DataWindow!</c> arm only, and the
    /// parity test asserts exactly that.
    /// </para>
    /// <para>
    /// <b>Reconciled with the sibling's non-port note, deliberately and in the open.</b>
    /// <c>Concurrency/IdentityColumnResolver.cs</c> records redraw suppression as a C-D non-port on the
    /// grounds that repainting is presentational and belongs to the deferred DesignSystem area. That
    /// reasoning is correct about RENDERING and is honoured here in full: this member performs no Win32
    /// call, no repaint, no invalidation and no geometry, and nothing in this service implements it
    /// against a real window - the actual painting remains a deferred capability surfaced as a Gateway
    /// extension point under <c>/v1/design/**</c>. What is preserved is only the SEQUENCE AND
    /// EXCLUSIVITY of the two calls, which is behaviour rather than presentation, and which a headless
    /// implementation can honour by doing nothing at all. Dropping the calls entirely would have made
    /// the two arms indistinguishable and cost the pinning test that proves the asymmetry was noticed.
    /// </para>
    /// </remarks>
    void SetRedraw(bool redraw);

    /// <summary>
    /// The <c>Primary!</c> row count, bounding that buffer's modified-row walk - <c>RowCount()</c>.
    /// </summary>
    /// <returns>The count, zero or greater.</returns>
    long RowCount();

    /// <summary>
    /// The <c>Filter!</c> row count, bounding that buffer's modified-row walk - <c>FilteredCount()</c>.
    /// </summary>
    /// <returns>The count, zero or greater.</returns>
    long FilteredCount();

    /// <summary>
    /// Reads a row's status - <c>GetItemStatus(nRow, 0, Primary!)</c> [<c>:L140, :L174, :L321, :L333</c>]
    /// and its <c>Filter!</c> form [<c>:L156, :L190</c>].
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="columnIndex">
    /// The column index. The write-back always passes
    /// <see cref="ItemStatusMachine.RowStatusColumn"/>, because column ZERO addresses the ROW's status
    /// rather than any column's - that is the legacy convention and it is why the argument is an index
    /// and not a column number.
    /// </param>
    /// <param name="buffer">The buffer to read from.</param>
    /// <returns>The status.</returns>
    ItemStatus GetItemStatus(long row, int columnIndex, DwBuffer buffer);

    /// <summary>
    /// Writes an identity value into the <c>Primary!</c> buffer -
    /// <c>SetItem(nRow, id, value)</c> [<c>:L144, :L178</c>].
    /// </summary>
    /// <param name="row">The one-based row number.</param>
    /// <param name="columnId">
    /// The identity column's ONE-BASED ordinal, exactly as the resolver produced it. Never rebased.
    /// </param>
    /// <param name="value">The collected value, which may be <see langword="null"/>.</param>
    /// <remarks>
    /// ⚠️ <b>THE TWO WRITE ACCESSORS ARE ASYMMETRIC AND THE ASYMMETRY IS REAL, NOT STYLISTIC.</b> This
    /// one is the item setter; the <c>Filter!</c> buffer is written through
    /// <see cref="SetFilterBufferValue"/> instead, because the legacy item setter has no buffer
    /// parameter and simply CANNOT target that buffer. See that member for the citation.
    /// </remarks>
    void SetItemValue(long row, long columnId, long? value);

    /// <summary>
    /// Writes an identity value into the <c>Filter!</c> buffer through the DIRECT buffer expression -
    /// <c>Object.Data.Filter[nRow, id] = value</c> [<c>:L160, :L194</c>].
    /// </summary>
    /// <param name="row">The one-based row number, in <c>Filter!</c> buffer coordinates.</param>
    /// <param name="columnId">
    /// The identity column's ONE-BASED ordinal, exactly as the resolver produced it. Never rebased.
    /// </param>
    /// <param name="value">The collected value, which may be <see langword="null"/>.</param>
    /// <remarks>
    /// <b>A SEPARATE MEMBER BECAUSE THE ORACLE HAD NO CHOICE, AND MERGING THEM WOULD HIDE THAT.</b>
    /// The oracle's <c>Primary!</c> write is <c>SetItem(nRow, id, value)</c> [<c>:L144</c>] while its
    /// <c>Filter!</c> write drops to the data expression <c>Object.Data.Filter[nRow, id] = value</c>
    /// [<c>:L160</c>] - two different mechanisms for the same logical operation, in the same function,
    /// four lines apart. <c>SetItem</c> takes no buffer argument, so the filtered rows are simply not
    /// addressable through it. <c>Concurrency/IdentityColumnResolver.cs</c> records the mirror-image
    /// asymmetry on the READ side. Collapsing these into one member with a buffer parameter would
    /// present a uniform API the legacy does not have and would erase the reason the second one exists.
    /// </remarks>
    void SetFilterBufferValue(long row, long columnId, long? value);

    /// <summary>
    /// Reads a DataWindow property - <c>Describe(...)</c> [<c>:L254, :L257, :L264, :L267</c>].
    /// </summary>
    /// <param name="property">The property name, spelled as the legacy spells it.</param>
    /// <returns>
    /// The property value, or one of PowerBuilder's own markers - <c>"?"</c> for unknown, <c>"!"</c>
    /// for a read failure - which is why the capture gate below compares against the unknown marker
    /// rather than testing for emptiness.
    /// </returns>
    string Describe(string property);

    /// <summary>
    /// Captures the pending changeset - <c>GetChanges(ref blbData)</c> [<c>:L255, :L265</c>].
    /// </summary>
    /// <param name="changes">
    /// The captured payload, handed back BY REFERENCE rather than returned. This is hazard 1 of
    /// <c>docs/PB多线程绕坑提示.md</c> [<c>:L1-L4</c>] expressed in the signature: the oracle passes
    /// <c>ref blob</c> precisely so a large payload is never a return value, and the shape is preserved
    /// even though a managed reference would not fault.
    /// </param>
    /// <returns>
    /// The number of changed rows. The oracle stores this verbatim and forwards it beside the payload;
    /// a non-positive answer is legal and meaningful downstream.
    /// </returns>
    long GetChanges(out CarrierState? changes);
}

#endregion

#region The proxy

/// <summary>
/// The caller-side control object for an asynchronous DataWindow update - the managed port of
/// <c>n_cst_threading_task_sqlupdate</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru</c>], and the caller half
/// of contract C-06 <c>persistence.v1.UpdateService</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>RUNS ON THE CALLING THREAD</b> - <c>[运行在当前线程]</c> [<c>:L2</c>]. Its worker counterpart
/// <see cref="SqlUpdateTask"/> runs on the worker thread, and the pair is deliberately NOT flattened
/// into one async method. The file header carries the measured affinity census and the full reasoning.
/// </para>
/// <para>
/// <b>🔴 THIS TYPE AND <see cref="SqlUpdateTask"/> ARE TWO DISTINCT DI REGISTRATIONS, AND BOTH ARE
/// REQUIRED (C-J).</b> Register this one additionally as <see cref="ISqlUpdateTaskProxy"/>, which is the
/// channel the worker fires its two per-invocation reports through - a worker constructed with a null
/// proxy silently discards every row count and every identity value, because
/// <see cref="SqlUpdateTask"/> guards its publish with a validity test rather than throwing. Neither
/// type is a global: the oracle's auto-instance at [<c>:L21</c>] is replaced by container registration.
/// </para>
/// <para>
/// <b>WHY THIS TYPE IS MORE THAN A PASS-THROUGH.</b> The worker fires
/// <see cref="OnUpdated"/> and <see cref="OnIdentityColumnDataRetrieved"/> ONCE PER TABLE
/// [<c>n_cst_thread_task_sqlupdate.sru:L243, :L247</c>, from the loop at <c>:L364-L369</c>]. The running
/// totals and the ordered per-table identity blocks exist only here, and the write-back that consumes
/// them [<c>:L120-L205</c>] is this type's largest member.
/// </para>
/// </remarks>
internal sealed class SqlUpdateTaskProxy : SqlTaskProxyBase, ISqlUpdateTaskProxy
{
    /// <summary>
    /// The DataWindow syntax property, read to hand the worker a definition to build its carrier from -
    /// <c>Describe("DataWindow.Syntax")</c> [<c>:L257, :L267</c>].
    /// </summary>
    /// <remarks>
    /// Declared locally rather than imported. The identically valued constant in
    /// <c>Tasks/SqlQueryTask.cs</c> is not on this file's permitted dependency list (C-A), and a const
    /// string is a VALUE rather than a type, so restating it duplicates no type and cannot drift into a
    /// second competing declaration of a shape. The spelling is contract: it is the key a DataWindow's
    /// own property dictionary is addressed by, so a tidied spelling would silently address nothing.
    /// </remarks>
    private const string DataWindowSyntaxProperty = "DataWindow.Syntax";

    /// <summary>
    /// The legacy assertion message for an empty data-object name, preserved verbatim
    /// [<c>:L101</c>].
    /// </summary>
    private const string EmptyDataObjectAssertion = "Len(dataObject) <= 0";

    /// <summary>
    /// The legacy assertion message for an empty syntax string, preserved verbatim [<c>:L114</c>].
    /// </summary>
    private const string EmptySqlSyntaxAssertion = "Len(sqlSyntax) <= 0";

    // ----------------------------------------------------------------------------------------------
    //  Caller-side state - the verbatim shape of [:L36-L45]
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The ordered per-table identity blocks - <c>IDCOLDATA _idColDatas[]</c> [<c>:L45</c>].
    /// </summary>
    /// <remarks>
    /// <para>
    /// Element type is <see cref="ResolvedIdentityColumnData"/>, CONSUMED from
    /// <c>Concurrency/IdentityColumnResolver.cs</c>. The oracle nests its own structure inside this
    /// object - <c>type IdColData from structure within n_cst_threading_task_sqlupdate</c>
    /// [<c>:L6-L7</c>] defined at [<c>:L10-L14</c>] - with three fields in exactly this order: the
    /// identity column id, the <c>Primary!</c> values, the <c>Filter!</c> values. The resolver already
    /// declares that shape, so 🔴 <b>NO COMPETING IdColData IS DECLARED HERE.</b>
    /// </para>
    /// <para>
    /// ORDER IS CONTRACT, NOT CONVENIENCE. Position in this list IS table order, and position within
    /// each block's two arrays IS row correspondence. Nothing here sorts, deduplicates or reorders.
    /// </para>
    /// </remarks>
    private readonly List<ResolvedIdentityColumnData> _idColDatas = [];

    /// <summary>
    /// Accumulated inserted-row count - <c>long _nRowsInserted</c> [<c>:L36</c>].
    /// </summary>
    private long _rowsInserted;

    /// <summary>
    /// Accumulated updated-row count - <c>long _nRowsUpdated</c> [<c>:L37</c>].
    /// </summary>
    private long _rowsUpdated;

    /// <summary>
    /// Accumulated deleted-row count - <c>long _nRowsDeleted</c> [<c>:L38</c>].
    /// </summary>
    private long _rowsDeleted;

    /// <summary>
    /// Whether one DataWindow is being updated against several tables -
    /// <c>boolean _bMultiTableUpdate</c> [<c>:L40</c>].
    /// </summary>
    /// <remarks>
    /// Held on BOTH sides: <see cref="SetMultiTableUpdate"/> writes this copy AND forwards to the
    /// worker [<c>:L209, :L211</c>]. The local copy exists because
    /// <see cref="SetUpdateObject(IIdentityWriteBackTarget, bool)"/> reads it to decide whether to
    /// capture a changeset [<c>:L254</c>], and that decision is taken on this thread.
    /// </remarks>
    private bool _multiTableUpdate;

    /// <summary>
    /// The retained update object - <c>powerobject _updateObject</c> [<c>:L42</c>].
    /// </summary>
    /// <remarks>
    /// Read by three members: the finalize hook's gated auto-invocation [<c>:L304-L305</c>], the
    /// database-error hook's row translation [<c>:L314, :L317</c>], and nothing else. Written by
    /// <see cref="SetUpdateObject(IIdentityWriteBackTarget, bool)"/> [<c>:L251, :L261</c>] and released
    /// by <see cref="Reset"/> [<c>:L94</c>] - but NOT by <see cref="OnPrepare"/>, which is a divergence
    /// the oracle carries and this port preserves.
    /// </remarks>
    private IIdentityWriteBackTarget? _updateObject;

    /// <summary>
    /// Initializes a new proxy.
    /// </summary>
    /// <param name="host">The task-substrate surface the base drives.</param>
    /// <param name="logger">The logger the base uses to report subscriber faults.</param>
    /// <param name="timeProvider">
    /// The service's single clock seam (C-H). This type reads no clock itself; the argument is required
    /// because the base publishes the seam, and passing the injected provider rather than a default keeps
    /// the prohibition structural.
    /// </param>
    /// <param name="executionGroup">The execution group, defaulted as the base defaults it.</param>
    internal SqlUpdateTaskProxy(
        ISqlTaskProxyHost host,
        ILogger<SqlUpdateTaskProxy> logger,
        TimeProvider timeProvider,
        TaskExecutionGroup executionGroup = TaskExecutionGroup.Normal)
        : base(host, logger, timeProvider, executionGroup)
    {
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>"sqlupdate"</c>. The oracle carries this value TWICE - as the instance property
    /// <c>string #type = "sqlupdate"</c> [<c>:L17</c>] and as
    /// <c>constant string TASK_TYPE = "sqlupdate"</c> [<c>:L27</c>] - which is one value expressed
    /// twice, so it is published once. 🔴 No identifier named <c>TASK_TYPE</c> is declared; the shouty
    /// spelling cannot be preserved because this folder is outside the <c>.editorconfig</c> naming
    /// suppressions, and the VALUE is what is observable.
    /// </remarks>
    protected override string TaskType => "sqlupdate";

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <c>"n_cst_thread_task_sqlupdate"</c>, the exact string
    /// <c>event ongettaskclsname</c> answers [<c>:L288</c>]. It is contract rather than description: the
    /// substrate resolves a worker BY this name, it is republished through the base's task-class-name
    /// accessor, and it appears in characterization recordings - so a tidied name would resolve nothing
    /// and invalidate stored comparisons at the same time.
    /// </para>
    /// <para>
    /// ⚠️ <b>A PRESERVED LEGACY INCONSISTENCY (C-B).</b> The sibling proxies write
    /// <c>call super::ongettaskclsname</c> before returning their name -
    /// <c>n_cst_threading_task_sqlquery.sru:L500</c> and <c>n_cst_threading_task_sqlcommand.sru:L56</c> -
    /// and <b>this one does NOT</b> [<c>:L288</c>]. All three locators were opened and confirmed. The
    /// omission is faithfully carried rather than harmonised: an overriding property has no ancestor
    /// call to make, and the ancestor's answer is discarded in the two forms that do make it, so the
    /// three are observably identical and only the spelling differs. Recorded because a reader meeting
    /// all three files WILL notice the difference and must not "complete" it.
    /// </para>
    /// </remarks>
    protected override string WorkerTaskClassName => "n_cst_thread_task_sqlupdate";

    /// <summary>
    /// The accumulated inserted-row count - <c>of_getrowsinserted</c> [<c>:L214-L215</c>].
    /// </summary>
    /// <returns>The running total across every table of the run.</returns>
    /// <remarks>
    /// <b>NO BUSY GUARD, AND NONE MAY BE ADDED (C-B).</b> All three count accessors
    /// [<c>:L214, :L217, :L220</c>] are bare <c>return</c> statements. Guarding them would make the
    /// counts unreadable exactly while the update runs, which is when a progress display wants them.
    /// </remarks>
    internal long GetRowsInserted() => _rowsInserted;

    /// <summary>
    /// The accumulated deleted-row count - <c>of_getrowsdeleted</c> [<c>:L217-L218</c>].
    /// </summary>
    /// <returns>The running total across every table of the run.</returns>
    /// <remarks>
    /// No busy guard - see <see cref="GetRowsInserted"/>. Declared here in the ORACLE'S OWN ORDER,
    /// which puts deleted before updated [<c>:L217</c> before <c>:L220</c>].
    /// </remarks>
    internal long GetRowsDeleted() => _rowsDeleted;

    /// <summary>
    /// The accumulated updated-row count - <c>of_getrowsupdated</c> [<c>:L220-L221</c>].
    /// </summary>
    /// <returns>The running total across every table of the run.</returns>
    /// <remarks>No busy guard - see <see cref="GetRowsInserted"/>.</remarks>
    internal long GetRowsUpdated() => _rowsUpdated;

    /// <summary>
    /// The worker, narrowed to its actual type - the port of the private accessor
    /// <c>_of_gettask()</c> whose whole body is <c>return _Task</c> [<c>:L49, :L79-L80</c>].
    /// </summary>
    /// <returns>The attached worker.</returns>
    /// <exception cref="InvalidOperationException">
    /// No worker is attached, or the attached worker is of another type.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The oracle relies on PowerBuilder's implicit downcast and dereferences the result with no null
    /// test. A missing or mistyped worker there produces a null-object-reference RUNTIME ERROR, which
    /// the framework turns into termination - the application's system-error handler unpacks the payload
    /// and executes <c>HALT CLOSE</c> [<c>ws_objects/pfw.pbl.src/pfw.sra:L111-L144</c>]. The faithful
    /// .NET expression of that is a throw, and it preserves the fail-fast posture the plan requires
    /// instead of softening it into warning-and-continue.
    /// </para>
    /// <para>
    /// <b>The return-code algebra is untouched by this.</b> No legacy <see cref="RetCode"/> value is
    /// ever converted into a throw anywhere in this file; every legacy outcome is returned as a number.
    /// This exception is for a structural condition the oracle has no code for.
    /// </para>
    /// </remarks>
    private SqlUpdateTask GetTask() =>
        GetWorkerTask<SqlUpdateTask>()
        ?? throw new InvalidOperationException(
            $"The '{TaskType}' task proxy has no '{WorkerTaskClassName}' worker attached. "
            + "OnInit must succeed before this proxy is used, and it must not be used after disposal.");

    #region THE TWO PER-TABLE REPORTS - fired by the worker, ACCUMULATED HERE

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The port of <c>event onupdated</c> [<c>:L18, :L66-L69</c>], whose entire body is three
    /// compound additions.
    /// </para>
    /// <para>
    /// 🔴 <b>ACCUMULATION WITH <c>+=</c>, NEVER ASSIGNMENT, AND THIS IS OBSERVABLE.</b> The worker fires
    /// this event once per invocation, and on the multi-table path that is ONCE PER TABLE
    /// [<c>n_cst_thread_task_sqlupdate.sru:L247</c>, inside <c>_of_Update</c>, which the loop at
    /// <c>:L364-L369</c> calls for each descriptor]. With two tables an assignment would silently report
    /// only the LAST table's counts and every total would be wrong while every individual number looked
    /// plausible. The worker sums nothing - <c>Tasks/SqlUpdateTask.cs</c> says so in its own header - so
    /// if this method assigns, the totals simply do not exist anywhere.
    /// </para>
    /// <para>
    /// Fires on EVERY success including a pure update and a pure delete, because the oracle's call sits
    /// OUTSIDE the inserted-count block that gates the identity work [<c>:L215</c> versus <c>:L247</c>].
    /// </para>
    /// <para>
    /// <b>No busy guard</b> - the oracle's event body has none, and adding one would drop reports
    /// precisely while the task is running, which is the only time they arrive.
    /// </para>
    /// </remarks>
    public void OnUpdated(long inserted, long updated, long deleted)
    {
        // [:L66] `_nRowsInserted += inserted`
        _rowsInserted += inserted;

        // [:L67] `_nRowsUpdated += updated`
        _rowsUpdated += updated;

        // [:L68] `_nRowsDeleted += deleted`
        _rowsDeleted += deleted;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The port of <c>event onidentitycolumndataretrieved</c> [<c>:L19, :L71-L77</c>], whose body
    /// computes an append position and copies three fields into it.
    /// </para>
    /// <para>
    /// <b>ONE RECORD APPENDED PER FIRING, WHICH MEANS ONE PER TABLE.</b> The oracle writes
    /// <c>nIndex = UpperBound(_idColDatas) + 1</c> [<c>:L73</c>] and then assigns the id and both value
    /// arrays into that slot [<c>:L74-L76</c>]. Because the worker fires per table
    /// [<c>n_cst_thread_task_sqlupdate.sru:L243</c>], the list grows to one block per table IN TABLE
    /// ORDER, and the write-back's inner loop relies on exactly that.
    /// </para>
    /// <para>
    /// <b>R9 - the append idiom, audited rather than mechanically translated.</b>
    /// <c>x[UpperBound(x) + 1] = v</c> is PowerBuilder's one-based append: it lands at the position one
    /// past the last valid index. <see cref="List{T}.Add"/> lands at exactly that one-based position by
    /// definition, which <see cref="OneBasedIndex.AppendIndex"/> names; the assertion below states the
    /// equivalence at the point of translation instead of leaving it to be trusted.
    /// </para>
    /// <para>
    /// <b>Hazard 1 shapes this signature.</b> The oracle declares two <c>ref long ...[]</c>
    /// out-parameters [<c>:L19</c>] because a worker returning a large payload to the calling thread can
    /// fault [<c>docs/PB多线程绕坑提示.md:L1-L4</c>]. The published interface carries them INWARD as
    /// read-only lists, which is the same by-reference handover with no mutable buffer exposed - never a
    /// returned array. The element type is nullable because a null identity is representable: the
    /// collector's item read answers null for a null item and the oracle stores that null into the array
    /// it hands over.
    /// </para>
    /// <para>
    /// <b>No busy guard</b> - see <see cref="OnUpdated"/>.
    /// </para>
    /// </remarks>
    public void OnIdentityColumnDataRetrieved(
        long identityColumnId,
        IReadOnlyList<long?> primaryValues,
        IReadOnlyList<long?> filterValues)
    {
        ArgumentNullException.ThrowIfNull(primaryValues);
        ArgumentNullException.ThrowIfNull(filterValues);

        // [:L73-L76] `nIndex = UpperBound(_idColDatas) + 1` followed by the three field copies, in the
        // oracle's own field order.
        //
        // R9, audited: the oracle's `x[UpperBound(x) + 1] = v` is PowerBuilder's one-based append, landing
        // one past the last valid index. List.Add lands at exactly that position - it IS
        // OneBasedIndex.AppendIndex BY DEFINITION - so the translation needs no arithmetic and no rebasing.
        // The equivalence is stated here and PINNED BY TEST rather than by a runtime assertion: an
        // assertion would throw, the oracle has none at this site, and inventing a throw on a live
        // notification path would add a failure mode the legacy cannot exhibit (C-B).
        //
        // The record is CONSTRUCTED rather than mutated because ResolvedIdentityColumnData is the
        // resolver's immutable shape; the two arrays are carried through BY REFERENCE exactly as the
        // oracle's array assignment carries them, and neither is copied, reordered, deduplicated, trimmed
        // or merged with the other - position IS row correspondence in both.
        _idColDatas.Add(new ResolvedIdentityColumnData(identityColumnId, primaryValues, filterValues));
    }

    #endregion

    #region THE IDENTITY WRITE-BACK [:L120-L205] - the apply half of the round trip

    /// <summary>
    /// Writes every collected identity value back onto a caller-supplied object - the port of
    /// <c>of_refreshidentitydata</c> [<c>:L54, :L120-L205</c>].
    /// </summary>
    /// <param name="target">The object to write onto.</param>
    /// <returns>
    /// <see cref="RetCode.E_BUSY"/> while the task is busy [<c>:L125</c>];
    /// <see cref="RetCode.E_INVALID_OBJECT"/> for an invalid object [<c>:L126</c>];
    /// <see cref="RetCode.FAILED"/> when NOTHING has been collected [<c>:L129</c>];
    /// <see cref="RetCode.E_INVALID_TYPE"/> for anything that is neither a DataWindow nor a DataStore
    /// [<c>:L201</c>]; <see cref="RetCode.FAILED"/> when the preserved table-1 bound reaches past a
    /// shorter table's array (see the latent-defect note below); otherwise <see cref="RetCode.OK"/>
    /// [<c>:L204</c>].
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>THE EMPTY-COLLECTION ANSWER IS THE GENERIC FAILURE, NOT SUCCESS AND NOT AN ERROR CODE.</b>
    /// <c>if nCount = 0 then return RetCode.FAILED</c> [<c>:L128-L129</c>] - not
    /// <c>RetCode.OK</c>, not a not-found code. It is checked BEFORE the type dispatch, so an
    /// unsupported object with nothing collected answers <see cref="RetCode.FAILED"/> rather than
    /// <see cref="RetCode.E_INVALID_TYPE"/>; the order of the two tests is therefore observable and is
    /// preserved.
    /// </para>
    /// <para>
    /// 🔴 <b>FORWARD IN BOTH BUFFERS - THE SINGLE MOST DANGEROUS DECISION IN THIS FILE (R9).</b> Both
    /// passes advance with <c>GetNextModified(nRow, buffer)</c> [<c>:L147, :L163, :L181, :L197</c>], i.e.
    /// FORWARD, even though <c>Concurrency/IdentityColumnResolver.cs</c> collected the <c>Filter!</c>
    /// values BACKWARD [<c>n_cst_thread_task_sqlupdate.sru:L237</c>]. That is not an inconsistency to
    /// resolve - the filter buffer's row order is genuinely inverted relative to the source that produced
    /// the changeset, stated by the oracle itself at [<c>n_cst_thread_task_sqlupdate.sru:L235</c>], and
    /// backward collection paired with forward application is precisely what re-aligns value to row.
    /// "Correcting" either half writes identity values onto the WRONG ROWS while every row count still
    /// matches, so no count-based assertion can detect it.
    /// </para>
    /// <para>
    /// ⚠️ <b>A LATENT DEFECT PRESERVED, NOT FIXED (C-B).</b> Both passes take their bound from
    /// <c>_idColDatas[1]</c> ONLY - <c>n = UpperBound(_idColDatas[1].primaryValues)</c> [<c>:L136,
    /// :L170</c>] and its Filter twin [<c>:L151, :L185</c>] - and then index EVERY table's array with
    /// that same cursor [<c>:L143-L145, :L159-L161, :L177-L179, :L193-L195</c>]. Multi-table update
    /// appends one block per table [<c>:L71-L77</c>], so a later table whose array is SHORTER than the
    /// first's is read out of range. The table-1 bound is reproduced exactly. What differs is only the
    /// CONSEQUENCE: PowerBuilder raises an array-boundary runtime error, and the .NET port answers
    /// <see cref="RetCode.FAILED"/> instead of throwing, which keeps the member inside the return-code
    /// algebra its callers expect and keeps a partially applied write from being masked by an exception.
    /// The bound itself is NOT widened to the per-table array, because doing so would change which rows
    /// receive values.
    /// </para>
    /// <para>
    /// ⚠️ <b>A COSMETIC ASYMMETRY, NORMALISED VISIBLY.</b> The oracle resets the value cursor
    /// EXPLICITLY before the <c>Filter!</c> pass [<c>:L153, :L187</c>] but relies on declaration-zero
    /// before the <c>Primary!</c> pass [<c>:L121</c> declares <c>i</c>, and [<c>:L141</c>] increments it
    /// with no preceding reset]. Both passes below initialise explicitly, which is observably identical
    /// because a fresh walk always starts from zero, and the legacy asymmetry is recorded here rather
    /// than silently levelled.
    /// </para>
    /// <para>
    /// <b>The redraw bracket is applied on the <c>DataWindow!</c> arm ONLY</b> [<c>:L134, :L166</c>]
    /// while the structurally identical <c>DataStore!</c> arm [<c>:L167-L199</c>] applies neither. See
    /// <see cref="IIdentityWriteBackTarget.SetRedraw"/> for how that preserves the behavioural asymmetry
    /// without porting any rendering.
    /// </para>
    /// </remarks>
    internal long RefreshIdentityData(IIdentityWriteBackTarget target)
    {
        // [:L125] `if of_IsBusy() then return RetCode.E_BUSY`
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

        // [:L126] `if Not IsValidObject(object) then return RetCode.E_INVALID_OBJECT`
        if (!Predicates.IsValidObject(target))
        {
            return RetCode.E_INVALID_OBJECT;
        }

        // [:L128] `nCount = UpperBound(_idColDatas)` - the ONE-BASED table count.
        int tableCount = OneBasedIndex.UpperBound(_idColDatas);

        // [:L129] `if nCount = 0 then return RetCode.FAILED` - see the remarks: FAILED, and BEFORE the
        // type dispatch, so this arm wins over E_INVALID_TYPE when both would apply.
        if (tableCount == OneBasedIndex.EmptyUpperBound)
        {
            return RetCode.FAILED;
        }

        // [:L131] `choose case object.TypeOf()`
        switch (target.Kind)
        {
            case IdentityWriteBackTargetKind.DataWindow:
                // [:L134] `dw.SetRedraw(false)` brackets the WHOLE arm, restored at [:L166]. The restore
                // sits in a finally because the .NET body can leave through the preserved out-of-range
                // failure path, which the oracle has no analogue for; the oracle always falls through to
                // its restore, so a finally is what reproduces "the restore always happens" rather than
                // a stricter or a weaker rule.
                target.SetRedraw(false);
                try
                {
                    // [:L135-L165] the two passes, in the oracle's order.
                    return ApplyBothBuffers(target, tableCount);
                }
                finally
                {
                    // [:L166] `dw.SetRedraw(true)`
                    target.SetRedraw(true);
                }

            case IdentityWriteBackTargetKind.DataStore:
                // [:L167-L199] the SAME two passes with NO bracket at all. Reproduced as a bracket-free
                // call rather than a shared code path with a conditional, so the difference between the
                // two arms stays visible at the dispatch.
                return ApplyBothBuffers(target, tableCount);

            default:
                // [:L200-L201] `case else return RetCode.E_INVALID_TYPE`
                return RetCode.E_INVALID_TYPE;
        }
    }

    /// <summary>
    /// Runs the <c>Primary!</c> pass then the <c>Filter!</c> pass - the shared body of the two
    /// structurally identical arms [<c>:L135-L165</c> and <c>:L169-L199</c>].
    /// </summary>
    /// <param name="target">The object being written onto.</param>
    /// <param name="tableCount">The one-based count of collected blocks.</param>
    /// <returns>
    /// <see cref="RetCode.OK"/> [<c>:L204</c>], or <see cref="RetCode.FAILED"/> when the preserved
    /// table-1 bound reaches past a shorter table's array.
    /// </returns>
    /// <remarks>
    /// The two passes differ in exactly three ways, and each difference is a legacy fact rather than a
    /// parameterisation convenience: the buffer they walk, WHICH ARRAY of each block supplies the values,
    /// and WHICH WRITE ACCESSOR applies them. The third is the important one - <c>Primary!</c> goes
    /// through the item setter [<c>:L144</c>] and <c>Filter!</c> through the direct buffer expression
    /// [<c>:L160</c>], because the item setter cannot address the filtered rows.
    /// </remarks>
    private long ApplyBothBuffers(IIdentityWriteBackTarget target, int tableCount)
    {
        // [:L135-L149] Primary: values from PrimaryValues, applied through the ITEM SETTER.
        long primaryCode = ApplyBuffer(
            target,
            tableCount,
            DwBuffer.Primary,
            target.RowCount(),
            static block => block.PrimaryValues,
            target.SetItemValue);

        if (primaryCode != RetCode.OK)
        {
            return primaryCode;
        }

        // [:L150-L165] Filter: values from FilterValues, applied through the DIRECT BUFFER EXPRESSION.
        return ApplyBuffer(
            target,
            tableCount,
            DwBuffer.Filter,
            target.FilteredCount(),
            static block => block.FilterValues,
            target.SetFilterBufferValue);
    }

    /// <summary>
    /// Walks one buffer's newly-modified rows FORWARD and writes each collected value onto it.
    /// </summary>
    /// <param name="target">The object being written onto.</param>
    /// <param name="tableCount">The one-based count of collected blocks.</param>
    /// <param name="buffer">The buffer to walk.</param>
    /// <param name="rowCount">That buffer's row count, bounding the walk.</param>
    /// <param name="valuesOf">Selects the buffer's value array from a block.</param>
    /// <param name="write">The buffer's write accessor.</param>
    /// <returns>
    /// <see cref="RetCode.OK"/>, or <see cref="RetCode.FAILED"/> for the preserved out-of-range read.
    /// </returns>
    private long ApplyBuffer(
        IIdentityWriteBackTarget target,
        int tableCount,
        DwBuffer buffer,
        long rowCount,
        Func<ResolvedIdentityColumnData, IReadOnlyList<long?>> valuesOf,
        Action<long, long, long?> write)
    {
        // [:L136, :L151] `n = UpperBound(_idColDatas[1].<buffer>Values)` - READ FROM BLOCK ONE ONLY.
        // This is the latent defect, preserved deliberately; see the member remarks. Block one is
        // addressed through the one-based helper so the translation of the literal `[1]` is explicit.
        ResolvedIdentityColumnData firstBlock =
            _idColDatas[OneBasedIndex.ToZeroBased(OneBasedIndex.FirstIndex, tableCount, nameof(tableCount))];

        int bound = OneBasedIndex.UpperBound(valuesOf(firstBlock));

        // [:L137, :L152] `if n > 0 then` - an empty array skips the whole pass, which is the
        // "nothing was collected for this buffer" reading.
        if (bound == OneBasedIndex.EmptyUpperBound)
        {
            return RetCode.OK;
        }

        // The row-status reader the walk and the new-row test both use. Column ZERO addresses the ROW's
        // status rather than a column's [:L140, :L156, :L174, :L190].
        ItemStatus RowStatusAt(long row) =>
            target.GetItemStatus(row, ItemStatusMachine.RowStatusColumn, buffer);

        // The value cursor `i` [:L121]. Initialised EXPLICITLY here for BOTH passes - the oracle only
        // does so before the Filter pass [:L153, :L187]; see the member remarks on that asymmetry.
        int cursor = OneBasedIndex.EmptyUpperBound;

        // [:L138, :L154] `nRow = GetNextModified(0, buffer)` - seeded BEFORE the first row.
        long row = ItemStatusMachine.GetNextModified(
            ItemStatusMachine.BeforeFirstRow,
            buffer,
            rowCount,
            RowStatusAt);

        // [:L139, :L155] `do while(nRow > 0)` - zero terminates the walk.
        while (row > ItemStatusMachine.NoMoreModifiedRows)
        {
            // [:L140, :L156] `if GetItemStatus(nRow,0,buffer) = NewModified! then` - the walk visits
            // every MODIFIED row but only NEWLY-MODIFIED rows receive an identity value, because only an
            // inserted row was assigned one by the database. IsNewRow is an equality test against
            // NewModified! alone; DataModified! rows are visited and skipped.
            if (ItemStatusMachine.IsNewRow(RowStatusAt(row)))
            {
                // [:L141, :L157] `i++` - the cursor advances only for a qualifying row, which is what
                // keeps it in step with the collector's own qualifying-row-only append.
                cursor++;

                // [:L142, :L158] `if i > n then exit` - more qualifying rows than collected values ends
                // the pass rather than failing it. The remaining rows keep whatever they had.
                if (!OneBasedIndex.IsWithin(cursor, bound))
                {
                    break;
                }

                // [:L143-L145, :L159-L161] `for nIndex = 1 to nCount` - EVERY collected block writes its
                // own identity column with its own value at the shared cursor.
                for (int table = OneBasedIndex.FirstIndex; table <= tableCount; table++)
                {
                    ResolvedIdentityColumnData block =
                        _idColDatas[OneBasedIndex.ToZeroBased(table, tableCount, nameof(tableCount))];

                    IReadOnlyList<long?> values = valuesOf(block);
                    int blockBound = OneBasedIndex.UpperBound(values);

                    // The preserved latent defect, CONTAINED rather than corrected: the cursor is bounded
                    // by block one, so a shorter block is reached past. The oracle raises an array-boundary
                    // runtime error here; this answers FAILED and applies nothing further.
                    if (!OneBasedIndex.IsWithin(cursor, blockBound))
                    {
                        return RetCode.FAILED;
                    }

                    long? value = values[OneBasedIndex.ToZeroBased(cursor, blockBound, nameof(cursor))];

                    // [:L144 / :L160] the two asymmetric accessors, supplied by the caller. The identity
                    // column ordinal is passed through UNCHANGED - never rebased, never narrowed.
                    write(row, block.IdentityColumnId, value);
                }
            }

            // [:L147, :L163] `nRow = GetNextModified(nRow, buffer)` - 🔴 FORWARD IN BOTH BUFFERS. See the
            // member remarks before changing this line.
            row = ItemStatusMachine.GetNextModified(row, buffer, rowCount, RowStatusAt);
        }

        return RetCode.OK;
    }

    #endregion

    #region The delegating setters - each busy-guards, then forwards to the worker

    /// <summary>
    /// Names the data object to update through - <c>of_setdataobject</c> [<c>:L51, :L99-L104</c>].
    /// </summary>
    /// <param name="dataObject">The data-object name.</param>
    /// <returns>
    /// <see cref="RetCode.E_BUSY"/> when busy [<c>:L99</c>]; otherwise the worker's answer
    /// [<c>:L103</c>].
    /// </returns>
    /// <remarks>
    /// ⚠️ 🔴 <b>THE ASSERTION HERE IS UN-GATED, AND THAT IS THE PRESERVED INCONSISTENCY (C-B).</b> The
    /// oracle writes <c>Assert(Len(dataObject) &gt; 0,"Len(dataObject) &lt;= 0")</c> at [<c>:L101</c>]
    /// with NO conditional-compilation guard, so it fires in a release build, while the sibling
    /// <see cref="SetSqlSyntax"/> wraps the SAME assertion in <c>#IF DEFINED DEBUG</c>
    /// [<c>:L113-L115</c>] and the sibling command proxy gates its own equivalent too
    /// [<c>n_cst_threading_task_sqlcommand.sru:L36-L38</c>]. All three locators were opened and
    /// confirmed, which makes THIS ONE the outlier. Neither may be harmonised: gating this would remove
    /// a release-build check the oracle performs, and un-gating the other would add one it does not.
    /// The message string is preserved verbatim because it is what a caller sees.
    /// </remarks>
    internal long SetDataObject(string dataObject)
    {
        // [:L99]
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

        // [:L101] UN-GATED - deliberately outside any #if. See the remarks.
        Assertions.Assert(dataObject is { Length: > 0 }, EmptyDataObjectAssertion);

        // [:L103]
        return GetTask().SetDataObject(dataObject);
    }

    /// <summary>
    /// Sets whether the worker commits on success - <c>of_setautocommit</c> [<c>:L52, :L106-L109</c>].
    /// </summary>
    /// <param name="autoCommit">The flag.</param>
    /// <returns>
    /// <see cref="RetCode.E_BUSY"/> when busy [<c>:L106</c>]; otherwise the worker's answer
    /// [<c>:L108</c>].
    /// </returns>
    /// <remarks>
    /// <b>BOOLEAN HERE, AND NOT TO BE CONFLATED WITH THE COMMAND PROXY'S TRI-VALUED SETTING.</b> The
    /// oracle's parameter is <c>readonly boolean autocommit</c> [<c>:L52</c>] - two states, no absence -
    /// so there is no nullable and no enum. No assertion of any kind, gated or otherwise.
    /// </remarks>
    internal long SetAutoCommit(bool autoCommit)
    {
        // [:L106]
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

        // [:L108]
        return GetTask().SetAutoCommit(autoCommit);
    }

    /// <summary>
    /// Sets the DataWindow syntax the worker builds its carrier from - <c>of_setsqlsyntax</c>
    /// [<c>:L53, :L111-L118</c>].
    /// </summary>
    /// <param name="sqlSyntax">The syntax.</param>
    /// <returns>
    /// <see cref="RetCode.E_BUSY"/> when busy [<c>:L111</c>]; otherwise the worker's answer
    /// [<c>:L117</c>].
    /// </returns>
    /// <remarks>
    /// ⚠️ 🔴 <b>THE ASSERTION HERE IS DEBUG-GATED, WHICH IS THE OTHER HALF OF THE PRESERVED
    /// INCONSISTENCY (C-B).</b> The oracle wraps it in <c>#IF DEFINED DEBUG THEN</c> ... <c>#END IF</c>
    /// [<c>:L113-L115</c>], so it does NOT fire in a release build - unlike the identical assertion in
    /// <see cref="SetDataObject"/> at [<c>:L101</c>], which is un-gated. Reproduced with <c>#if DEBUG</c>
    /// so the difference survives into the built artifact rather than being levelled in source.
    /// </remarks>
    internal long SetSqlSyntax(string sqlSyntax)
    {
        // [:L111]
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

#if DEBUG
        // [:L113-L115] `#IF DEFINED DEBUG THEN Assert(Len(sqlSyntax) > 0,...) #END IF`
        Assertions.Assert(sqlSyntax is { Length: > 0 }, EmptySqlSyntaxAssertion);
#endif

        // [:L117]
        return GetTask().SetSqlSyntax(sqlSyntax);
    }

    /// <summary>
    /// Turns multi-table update on or off - <c>of_setmultitableupdate</c> [<c>:L55, :L207-L212</c>].
    /// </summary>
    /// <param name="multiTable">The switch.</param>
    /// <returns>
    /// <see cref="RetCode.E_BUSY"/> when busy [<c>:L207</c>]; otherwise the worker's answer
    /// [<c>:L211</c>].
    /// </returns>
    /// <remarks>
    /// <b>BOTH WRITES HAPPEN, AND BOTH ARE NEEDED.</b> The oracle assigns its own field [<c>:L209</c>]
    /// AND forwards to the worker [<c>:L211</c>]. The local copy is not redundant caching: it is read on
    /// THIS thread by <see cref="SetUpdateObject(IIdentityWriteBackTarget, bool)"/> to decide whether to
    /// capture a changeset [<c>:L254</c>], and the worker's copy decides whether the prepare step runs at
    /// all. Dropping either would break one of the two decisions.
    /// </remarks>
    internal long SetMultiTableUpdate(bool multiTable)
    {
        // [:L207]
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

        // [:L209] the local copy
        _multiTableUpdate = multiTable;

        // [:L211] and the worker's
        return GetTask().SetMultiTableUpdate(multiTable);
    }

    /// <summary>
    /// Hands the changeset payload and its row count to the worker - <c>of_setupdatedata</c>
    /// [<c>:L61, :L236-L239</c>].
    /// </summary>
    /// <param name="updateData">
    /// The payload, passed BY REFERENCE. The oracle's parameter is <c>ref blob blbdata</c> and it
    /// forwards <c>ref</c> straight through [<c>:L238</c>].
    /// </param>
    /// <param name="updateRows">
    /// The row count the sender measured. The oracle's parameter is <c>readonly long updaterows</c>;
    /// a <c>readonly</c> SCALAR is carried by value here, matching the convention the rest of this
    /// folder already uses for scalars, while <c>readonly</c> on a STRUCTURE maps to <c>in</c> as the
    /// base's own error sink shows.
    /// </param>
    /// <returns>
    /// <see cref="RetCode.E_BUSY"/> when busy [<c>:L236</c>]; otherwise the worker's answer
    /// [<c>:L238</c>].
    /// </returns>
    /// <remarks>
    /// <b>THE <c>ref</c> IS HAZARD 1'S REMEDY, NOT DECORATION.</b> This is the third by-reference payload
    /// site in this folder, and the reason is documented in the oracle's own guidance: a worker thread
    /// synchronously exchanging a string or blob with a main-thread object can fault when a Post message
    /// is pending on an object being destroyed, and the stated fix is to pass such payloads
    /// <c>ref</c> instead of returning them [<c>docs/PB多线程绕坑提示.md:L1-L4</c>]. The worker's own
    /// parameter needs no <c>ref</c> because a managed reference already crosses without copying; the
    /// modifier is retained HERE so the declared shape - and the reason for it - survives the migration
    /// rather than being optimised away into an indistinguishable by-value call.
    /// </remarks>
    internal long SetUpdateData(ref CarrierState? updateData, long updateRows)
    {
        // [:L236]
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

        // [:L238] `return _of_GetTask().of_SetUpdateData(ref blbData,updateRows)`
        return GetTask().SetUpdateData(updateData, updateRows);
    }

    #endregion

    #region Add-updatable-table - TWO arities, and the four-argument one leaves BOTH settings ABSENT

    /// <summary>
    /// Appends an updatable-table descriptor, six-argument form - <c>of_addupdatabletable</c>
    /// [<c>:L59, :L223-L225</c>].
    /// </summary>
    /// <param name="name">The update table name.</param>
    /// <param name="updatableColumns">The columns to mark updatable.</param>
    /// <param name="keyColumns">The columns to mark as keys.</param>
    /// <param name="identityColumn">The identity column, or the empty string for none.</param>
    /// <param name="updateWhere">
    /// The concurrency mode, or <see langword="null"/> for ABSENT. <c>1</c> is the "key and updateable
    /// columns" mode the sole evidenced fixture uses
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14</c>].
    /// </param>
    /// <param name="updateKeyInPlace">
    /// The key-in-place setting, or <see langword="null"/> for ABSENT. The fixture sets it to <c>no</c>
    /// [<c>dw_sqlite.srd:L14</c>], so the delete-plus-insert key path is EXERCISED by the golden master
    /// rather than exotic.
    /// </param>
    /// <returns>
    /// <see cref="RetCode.E_BUSY"/> when busy [<c>:L223</c>]; otherwise the worker's answer
    /// [<c>:L224</c>], which is <see cref="RetCode.E_INVALID_ARGUMENT"/> when the descriptor fails the
    /// worker's three-arm admission test.
    /// </returns>
    /// <remarks>
    /// The three-arm admission test - empty name OR empty updatable-column array OR empty key-column
    /// array [<c>n_cst_thread_task_sqlupdate.sru:L84</c>] - belongs to the worker's descriptor collection
    /// and is deliberately NOT re-derived here. An empty identity column stays legal: it is not one of
    /// the three arms.
    /// </remarks>
    internal long AddUpdatableTable(
        string name,
        IEnumerable<string> updatableColumns,
        IEnumerable<string> keyColumns,
        string identityColumn,
        long? updateWhere,
        bool? updateKeyInPlace)
    {
        // [:L223]
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

        // [:L224]
        return GetTask().AddUpdatableTable(
            name,
            updatableColumns,
            keyColumns,
            identityColumn,
            updateWhere,
            updateKeyInPlace);
    }

    /// <summary>
    /// Appends an updatable-table descriptor leaving BOTH optional settings ABSENT - the four-argument
    /// form [<c>:L60, :L227-L234</c>].
    /// </summary>
    /// <param name="name">The update table name.</param>
    /// <param name="updatableColumns">The columns to mark updatable.</param>
    /// <param name="keyColumns">The columns to mark as keys.</param>
    /// <param name="identityColumn">The identity column, or the empty string for none.</param>
    /// <returns>Whatever the six-argument form answers [<c>:L233</c>].</returns>
    /// <remarks>
    /// <para>
    /// 🔴 <b>ABSENT IS NOT THE SAME AS DEFAULTED, AND THE DIFFERENCE IS OBSERVABLE IN THE GENERATED
    /// STATEMENT.</b> The oracle declares two locals and calls <c>SetNull</c> on BOTH before delegating
    /// [<c>:L227-L228, :L230-L231</c>]. The worker's prepare step then tests <c>IsNull(...)</c> on each
    /// and OMITS THE WHOLE MODIFICATION LINE when the value is absent
    /// [<c>n_cst_thread_task_sqlupdate.sru:L131, :L135</c>] - both locators opened and confirmed. So
    /// passing <c>0</c> would emit <c>DataWindow.Table.UpdateWhere = '0'</c> and silently switch the
    /// concurrency mode away from the fixture's own <c>updatewhere=1</c>, and passing
    /// <see langword="false"/> would emit <c>DataWindow.Table.UpdateKeyinPlace = no</c> where the oracle
    /// emits nothing and lets the carrier's own definition govern. That is why
    /// <c>UpdatableTableDescriptor</c> types both members <see cref="long"/>? and <see cref="bool"/>?,
    /// and why C-06's descriptor uses proto3 <c>optional</c> presence.
    /// </para>
    /// <para>
    /// <b>NO BUSY GUARD OF ITS OWN</b> [<c>:L227</c> opens straight into the local declarations]. It
    /// inherits one through the six-argument form, and adding a second would be harmless today but would
    /// diverge from the oracle for no gain.
    /// </para>
    /// </remarks>
    internal long AddUpdatableTable(
        string name,
        IEnumerable<string> updatableColumns,
        IEnumerable<string> keyColumns,
        string identityColumn)
    {
        // [:L227-L228] `Long nUpdateWhere` / `Boolean bUpdateInPlace`, then
        // [:L230-L231] `SetNull(nUpdateWhere)` / `SetNull(bUpdateInPlace)`.
        //
        // Written as two explicitly typed nullable locals rather than as inline `null` arguments, because
        // the oracle's SetNull calls are the whole point of this overload and inlining them would leave a
        // future reader unable to see that ABSENCE was chosen rather than defaulted.
        long? updateWhere = null;
        bool? updateKeyInPlace = null;

        // [:L233] delegation to the six-argument form - which is where the busy guard comes from.
        return AddUpdatableTable(
            name,
            updatableColumns,
            keyColumns,
            identityColumn,
            updateWhere,
            updateKeyInPlace);
    }

    #endregion

    #region Set-update-object - TWO arities, the capture gate, and a swallowed-failure defect

    /// <summary>
    /// Retains the object to update and optionally captures its pending changes -
    /// <c>of_setupdateobject</c> [<c>:L62, :L241-L275</c>].
    /// </summary>
    /// <param name="target">The DataWindow or DataStore to update from.</param>
    /// <param name="applyData">
    /// Whether to capture and forward the changeset now. The one-argument overload passes
    /// <see langword="true"/> [<c>:L277</c>].
    /// </param>
    /// <returns>
    /// <see cref="RetCode.E_BUSY"/> when busy [<c>:L246</c>];
    /// <see cref="RetCode.E_INVALID_OBJECT"/> for an invalid object [<c>:L247</c>];
    /// <see cref="RetCode.E_INVALID_TYPE"/> for anything that is neither a DataWindow nor a DataStore
    /// [<c>:L271</c>]; otherwise <see cref="RetCode.OK"/> [<c>:L274</c>] - <b>unconditionally, even when
    /// a delegated call failed</b>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>THE CAPTURE GATE IS AN OR, NOT AN AND, AND MULTI-TABLE FORCES THE CAPTURE.</b>
    /// <c>if dw.Describe("DataWindow.Table.UpdateTable") &lt;&gt; "?" or _bMultiTableUpdate then</c>
    /// [<c>:L254, :L264</c>]. So the changeset is captured when the target names an update table, AND
    /// ALSO whenever multi-table update is on regardless of what the describe answers - because on that
    /// path the update table is assigned per descriptor by the worker's prepare step and the target's own
    /// definition legitimately has none yet.
    /// </para>
    /// <para>
    /// <b>WHEN THE GATE IS CLOSED THE FORWARD STILL HAPPENS, AND THE WORKER RELIES ON IT.</b> The oracle
    /// declares its payload and count as locals [<c>:L241-L242</c>], leaves them at the empty blob and
    /// zero when the capture is skipped, and calls the update-data setter ANYWAY [<c>:L258, :L268</c>].
    /// That is precisely the shape <c>Tasks/SqlUpdateTask.cs</c> reads as success-with-no-data: a
    /// changeset application answering <c>-1</c> with a zero row count exits OK rather than failing
    /// [<c>n_cst_thread_task_sqlupdate.sru:L339-L342</c>]. Skipping the forward would turn that benign
    /// path into an invalid-data failure.
    /// </para>
    /// <para>
    /// ⚠️ <b>A SWALLOWED-FAILURE DEFECT, PRESERVED (C-B).</b> The oracle discards the return codes of
    /// BOTH delegated calls [<c>:L257-L258, :L267-L268</c>] and answers <see cref="RetCode.OK"/>
    /// regardless [<c>:L274</c>]. A caller therefore cannot learn from this function that the syntax or
    /// the payload was rejected. Reproduced with explicit discards so the discard is visibly deliberate
    /// rather than an overlooked result, and NOT corrected: surfacing the codes would start failing calls
    /// the legacy accepts.
    /// </para>
    /// </remarks>
    internal long SetUpdateObject(IIdentityWriteBackTarget target, bool applyData)
    {
        // [:L246]
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

        // [:L247]
        if (!Predicates.IsValidObject(target))
        {
            return RetCode.E_INVALID_OBJECT;
        }

        // [:L249] `choose case object.TypeOf()`
        switch (target.Kind)
        {
            case IdentityWriteBackTargetKind.DataWindow:
            case IdentityWriteBackTargetKind.DataStore:
                // [:L251, :L261] The two arms are character-for-character identical apart from the local
                // they downcast into, so they are expressed as two labels over one body. Retaining the
                // object happens BEFORE and INDEPENDENTLY of the apply flag, which is what lets a caller
                // register a target for the finalize hook's write-back without handing over data now.
                _updateObject = target;

                // [:L252, :L262] `if applyData then`
                if (applyData)
                {
                    ApplyUpdateObjectData(target);
                }

                break;

            default:
                // [:L270-L271] `case else return RetCode.E_INVALID_TYPE` - note this returns WITHOUT
                // retaining, so an unsupported target leaves any previously retained object in place.
                return RetCode.E_INVALID_TYPE;
        }

        // [:L274] `return RetCode.OK` - unconditionally. See the swallowed-failure note.
        return RetCode.OK;
    }

    /// <summary>
    /// Retains the object to update and captures its pending changes - the one-argument overload
    /// [<c>:L63, :L277-L278</c>].
    /// </summary>
    /// <param name="target">The DataWindow or DataStore to update from.</param>
    /// <returns>Whatever the two-argument overload answers.</returns>
    /// <remarks>
    /// <b>THE APPLY FLAG DEFAULTS TO <see langword="true"/></b> - the oracle's whole body is
    /// <c>return of_SetUpdateObject(object,true)</c> [<c>:L277</c>]. Expressed as a second overload
    /// rather than as an optional parameter, because the oracle declares two functions [<c>:L62-L63</c>]
    /// and an optional parameter would collapse them into one member that no longer matches either
    /// declaration.
    /// </remarks>
    internal long SetUpdateObject(IIdentityWriteBackTarget target) => SetUpdateObject(target, true);

    /// <summary>
    /// Captures the changeset when the gate permits and forwards the definition and payload to the
    /// worker - the <c>applyData</c> body of both arms [<c>:L253-L258</c> and <c>:L263-L268</c>].
    /// </summary>
    /// <param name="target">The retained target.</param>
    private void ApplyUpdateObjectData(IIdentityWriteBackTarget target)
    {
        // [:L241-L242] the two locals, at the cleared state the oracle's declarations give them. When the
        // gate below is closed they STAY here and are forwarded as-is; see the member remarks.
        CarrierState? changes = null;
        long changeRows = 0L;

        // [:L254, :L264] the capture gate - an OR, with multi-table forcing the capture. The property name
        // and the unknown marker are both taken from Concurrency/, which already declares them, rather
        // than restated as local literals.
        if (target.Describe(UpdateWhereBuilder.UpdateTableProperty) != ConflictDetector.DescribeUnknownMarker
            || _multiTableUpdate)
        {
            // [:L255, :L265] `nChangeRows = dw.GetChanges(ref blbData)` - the count returns, the payload
            // travels out by reference.
            changeRows = target.GetChanges(out changes);
        }

        SqlUpdateTask task = GetTask();

        // [:L257, :L267] the definition. RESULT DISCARDED BY THE ORACLE - written as an explicit discard.
        _ = task.SetSqlSyntax(target.Describe(DataWindowSyntaxProperty));

        // [:L258, :L268] the payload and its count. RESULT DISCARDED BY THE ORACLE, and called even when
        // the gate above was closed.
        _ = task.SetUpdateData(changes, changeRows);
    }

    #endregion

    #region Lifecycle - reset, prepare, finalize, and the database-error row rewrite

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The port of <c>of_reset</c> [<c>:L50, :L82-L97</c>].
    /// </para>
    /// <para>
    /// <b>THE BASE RUNS FIRST AND ITS REFUSAL IS FINAL.</b> <c>rtCode = super::of_Reset()</c>
    /// [<c>:L85</c>] then <c>if IsFailed(rtCode) then return rtCode</c> [<c>:L86</c>] - the base's code is
    /// returned VERBATIM rather than replaced, and NOTHING below runs, so a refused reset cannot
    /// half-clear this side's state. <see cref="Predicates.IsFailed(long?)"/> is used rather than a
    /// hand-rolled sign test so the tri-state boundary is inherited: <see cref="RetCode.CANCELLED"/> is
    /// explicitly EXCLUDED from failure, so a cancelled worker does NOT take that arm and the clear below
    /// still happens.
    /// </para>
    /// <para>
    /// <b>NO BUSY GUARD OF ITS OWN, AND NONE MAY BE ADDED.</b> [<c>:L82</c>] opens straight into its
    /// locals. The guard arrives through the base call, whose own port guards first
    /// [<c>n_cst_threading_task_sqlbase.sru:L57</c>].
    /// </para>
    /// <para>
    /// ⚠️ <b>A DIVERGENCE FROM <see cref="OnPrepare"/> THAT MUST BE PRESERVED (C-B).</b> This member
    /// clears FIVE things - the multi-table flag [<c>:L88</c>], the three counters [<c>:L90-L92</c>], the
    /// identity blocks [<c>:L93</c>] and the retained update object [<c>:L94</c>]. The prepare hook clears
    /// only the counters and the blocks [<c>:L295-L298</c>]: it leaves the multi-table flag AND the
    /// retained object standing. The two locators sit fifteen lines apart in the oracle and disagree
    /// deliberately - configuration set up before a run is meant to survive INTO that run, while a full
    /// reset returns the proxy to its constructed state. Levelling them either way would change which
    /// settings survive a re-run.
    /// </para>
    /// <para>
    /// <b>Returns OK, not the base's code</b> [<c>:L96</c>]. Where the base answered a non-failing
    /// non-zero value such as <see cref="RetCode.PREVENT"/> - which
    /// <see cref="Predicates.IsFailed(long?)"/> does not treat as a failure - the oracle still answers OK.
    /// </para>
    /// </remarks>
    public override long Reset()
    {
        // [:L85] the base FIRST.
        long rtCode = base.Reset();

        // [:L86] abandon on failure, returning the base's code verbatim.
        if (Predicates.IsFailed(rtCode))
        {
            return rtCode;
        }

        // [:L88] the multi-table flag - cleared HERE but deliberately NOT in OnPrepare.
        _multiTableUpdate = false;

        // [:L90-L92] the three counters.
        _rowsInserted = 0L;
        _rowsUpdated = 0L;
        _rowsDeleted = 0L;

        // [:L93] `_idColDatas = emptyIdColDatas` - assigning a freshly declared empty array is
        // PowerBuilder's idiom for emptying one; Clear is the managed equivalent and keeps the single
        // list instance, which nothing observes.
        _idColDatas.Clear();

        // [:L94] `SetNull(_updateObject)` - also the mirror of hazard 2's release discipline, and also
        // deliberately NOT done in OnPrepare.
        _updateObject = null;

        // [:L96]
        return RetCode.OK;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The port of <c>event onprepare</c> [<c>:L291-L301</c>].
    /// </para>
    /// <para>
    /// <b>THE ANCESTOR RUNS FIRST AND ITS ANSWER IS TESTED AGAINST A BARE LITERAL.</b>
    /// <c>call super::onprepare</c> [<c>:L291</c>] then
    /// <c>if AncestorReturnValue &lt;&gt; 0 then return AncestorReturnValue</c> [<c>:L293</c>]. The bare
    /// <c>0</c> is the oracle's own spelling HERE, while the base's init handler spells the same value
    /// <c>RetCode.OK</c> [<c>n_cst_threading_task_sqlbase.sru:L215</c>] - one value, two spellings, each
    /// kept at the site that uses it. The test is a literal INEQUALITY and is deliberately not routed
    /// through the tri-state predicates: any non-zero answer aborts, including
    /// <see cref="RetCode.PREVENT"/>, which <see cref="Predicates.IsSucceeded(long?)"/> would have called
    /// a success.
    /// </para>
    /// <para>
    /// ⚠️ <b>WHAT IT DELIBERATELY DOES NOT CLEAR (C-B).</b> The three counters [<c>:L295-L297</c>] and
    /// the identity blocks [<c>:L298</c>] are cleared; the multi-table flag and the retained update
    /// object are NOT. See <see cref="Reset"/>, which clears all five, for why the divergence is
    /// load-bearing.
    /// </para>
    /// </remarks>
    protected override long OnPrepare()
    {
        // [:L291] `call super::onprepare`
        long ancestorReturnValue = base.OnPrepare();

        // [:L293] the BARE LITERAL inequality, spelled as the oracle spells it here.
        if (ancestorReturnValue != 0L)
        {
            return ancestorReturnValue;
        }

        // [:L295-L297] the three counters.
        _rowsInserted = 0L;
        _rowsUpdated = 0L;
        _rowsDeleted = 0L;

        // [:L298] the identity blocks.
        _idColDatas.Clear();

        // NOTE the two absences, and do not "complete" them: _multiTableUpdate and _updateObject are
        // NOT touched here, unlike Reset() [:L88, :L94].

        // [:L300] `return 0 //continue`
        return 0L;
    }

    /// <summary>
    /// Runs the end-of-task epilogue, auto-invoking the write-back when the run succeeded - the port of
    /// <c>event onfinalize</c> [<c>:L303-L308</c>].
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE GATE IS A CONJUNCTION AND BOTH TERMS MATTER.</b> The write-back runs only when the latched
    /// exit code EQUALS <see cref="RetCode.OK"/> [<c>:L303</c>] AND the retained update object is valid
    /// [<c>:L304</c>]. The first term is a literal equality, not a success predicate: a cancelled task
    /// latches <see cref="RetCode.CANCELLED"/> and a prevented one would latch
    /// <see cref="RetCode.PREVENT"/>, and neither equals OK, so neither writes back. Routing this through
    /// <see cref="Predicates.IsSucceeded(long?)"/> would let a PREVENT run write identity values.
    /// </para>
    /// <para>
    /// <b>The result is discarded</b> [<c>:L305</c>] - the oracle calls the write-back as a statement, so
    /// a refusal is invisible to the framework. Reproduced with an explicit discard.
    /// </para>
    /// <para>
    /// <b>ONE SPELLING DIFFERENCE WORTH A LINE.</b> This gate uses <c>IsValid(_updateObject)</c>
    /// [<c>:L304</c>] while the database-error hook [<c>:L314</c>] and both entry guards
    /// [<c>:L126, :L247</c>] use the framework's <c>IsValidObject(...)</c>. The framework wrapper is a
    /// null test followed by <c>IsValid</c> [<c>ws_objects/pfw.shared.pbl.src/isvalidobject.srf</c>], so
    /// for a managed reference - where the only way to fail to denote a live object is to be null - the
    /// two collapse onto the same test. <see cref="Predicates.IsValidObject(object?)"/> is used for both,
    /// and the oracle's inconsistent spelling is recorded rather than reproduced as two mechanisms.
    /// </para>
    /// <para>
    /// <b>NO ANCESTOR CALL IS MADE, AND THAT IS VERIFIED RATHER THAN ASSUMED.</b> The oracle opens with
    /// <c>call super::onfinalize</c> [<c>:L303</c>], but the substrate DECLARES
    /// <c>event onfinalize ( )</c> with no implementation script at all
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L22</c>] and the legacy parent
    /// <c>n_cst_threading_task_sqlbase</c> does not override it either - a sweep of that file finds ZERO
    /// occurrences of the name. The ancestor chain is therefore genuinely empty, which is why this member
    /// is a plain method with nothing to chain to rather than an override, matching the shape
    /// <c>Tasks/SqlUpdateTask.cs</c> already uses for the worker's own finalize hook.
    /// </para>
    /// </remarks>
    internal void OnFinalize()
    {
        // [:L303] `if of_GetLastExitCode() = RetCode.OK then` - a literal EQUALITY, not a predicate.
        if (GetLastExitCode() != RetCode.OK)
        {
            return;
        }

        // [:L304] `if IsValid(_updateObject) then` - see the remarks on the spelling.
        if (!Predicates.IsValidObject(_updateObject))
        {
            return;
        }

        // [:L305] `of_RefreshIdentityData(_updateObject)` - result discarded by the oracle.
        _ = RefreshIdentityData(_updateObject!);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The port of <c>event ondberror</c> [<c>:L310-L343</c>], which extends the base's latch by
    /// translating ONE field of the payload.
    /// </para>
    /// <para>
    /// <b>THE BASE RUNS FIRST, AND THAT ORDER IS CONTRACT.</b> <c>call super::ondberror</c>
    /// [<c>:L310</c>] performs the bare last-error-wins assignment
    /// [<c>n_cst_threading_task_sqlbase.sru:L44</c>], and only then is the LATCHED value inspected and
    /// rewritten. Inspecting the incoming argument instead would read the right values today but would
    /// stop tracking the latch the moment the base's assignment gained any condition - and it would break
    /// the last-error-wins property this override must not disturb: a second error wholly REPLACES the
    /// first, with no accumulation, no first-error-wins rule and no count.
    /// </para>
    /// <para>
    /// <b>WHAT IS BEING TRANSLATED, AND WHY IT IS NOT A CORRECTION.</b> The worker reports the offending
    /// row as an ORDINAL AMONG MODIFIED ROWS, and this side rewrites it into an ACTUAL BUFFER ROW NUMBER
    /// by walking the modified rows and counting [<c>:L321-L328</c>]. The two are different quantities
    /// with the same type, which is exactly why the rewrite is invisible at the type level and must be
    /// annotated. <b>ONLY <see cref="DbErrorData.Row"/> CHANGES</b>; the code, the text, the statement and
    /// the buffer are left exactly as the worker set them, which is why the rewrite is a
    /// <c>with</c>-expression on the immutable record struct rather than a rebuild.
    /// </para>
    /// <para>
    /// ⚠️ <b>TWO PRESERVED GAPS (C-B).</b> First, only the <c>Primary!</c> buffer is walked
    /// [<c>:L320-L321, :L332-L333</c>], so an error reported against <c>Filter!</c> or <c>Delete!</c>
    /// keeps its RAW ORDINAL and a consumer cannot tell translated from untranslated. Second, the
    /// <c>choose case</c> at [<c>:L317</c>] has <b>NO <c>case else</c></b> [the block ends at
    /// <c>:L342</c>], so a retained object that is neither a DataWindow nor a DataStore falls through
    /// SILENTLY - no error, no log, no code - leaving the row untranslated. Both are reproduced and both
    /// are covered by pinning tests.
    /// </para>
    /// <para>
    /// <b>C-F BINDS HARDEST HERE.</b> The payload this override retains and mutates is the one carrying
    /// <see cref="DbErrorData.SqlSyntax"/>, i.e. the fully generated statement INCLUDING INTERPOLATED
    /// LITERALS - the legacy runtime omits bind variables when the connection string carries
    /// <c>DisableBind=1</c> [<c>n_cst_thread_task_sqlbase.sru:L128-L129</c>] and the legacy logger redacts
    /// nothing. This method therefore only STORES it: it does not log it, does not render it, does not put
    /// it in a message and does not emit it. The single sanctioned way it leaves the process is the base's
    /// wire projection, whose redaction is reached directly rather than passed in and so cannot be
    /// weakened from a call site or a container registration.
    /// </para>
    /// </remarks>
    protected override void OnDbError(in DbErrorData error)
    {
        // [:L310] `call super::ondberror` FIRST - the bare last-error-wins assignment.
        base.OnDbError(in error);

        // [:L314] `if Not IsValidObject(_updateObject) then return`
        if (!Predicates.IsValidObject(_updateObject))
        {
            return;
        }

        // Read the LATCHED payload, not the argument. See the remarks on why the order matters.
        DbErrorData latched = LastDbError;

        // [:L315] `if _lastDBError.row <= 0 then return` - a non-positive row is "no row", and the
        // rewrite's ordinal count starts at one, so there is nothing to match.
        if (latched.Row <= 0L)
        {
            return;
        }

        IIdentityWriteBackTarget target = _updateObject!;

        // [:L317] `choose case _updateObject.TypeOf()`
        switch (target.Kind)
        {
            case IdentityWriteBackTargetKind.DataWindow:
            case IdentityWriteBackTargetKind.DataStore:
                // [:L318-L329] and [:L330-L341] are character-for-character identical apart from the
                // local they downcast into, so they are expressed as two labels over one body. Note that
                // NEITHER arm suspends redraw here - unlike the write-back's DataWindow arm - and that
                // asymmetry is the oracle's too.
                TranslateLastDbErrorRow(target, latched);
                break;

            default:
                // [:L342] THE MISSING `case else`. Falling through silently, leaving the row as the raw
                // ordinal the worker reported, is the preserved behaviour. Do not add an error here.
                break;
        }
    }

    /// <summary>
    /// Rewrites the latched error's row from an ordinal among modified rows into an actual
    /// <c>Primary!</c> buffer row - the shared body of both <c>ondberror</c> arms
    /// [<c>:L320-L329</c> and <c>:L332-L341</c>].
    /// </summary>
    /// <param name="target">The retained update object.</param>
    /// <param name="latched">The latched payload, already read from the base's latch.</param>
    private void TranslateLastDbErrorRow(IIdentityWriteBackTarget target, DbErrorData latched)
    {
        long rowCount = target.RowCount();

        ItemStatus RowStatusAt(long candidate) =>
            target.GetItemStatus(candidate, ItemStatusMachine.RowStatusColumn, DwBuffer.Primary);

        // [:L310] `long nRow,nCnt` - the ordinal counter, one-based by increment-before-compare.
        long ordinal = 0L;

        // [:L321, :L333] seeded before the first row. PRIMARY ONLY - see the preserved gap.
        long row = ItemStatusMachine.GetNextModified(
            ItemStatusMachine.BeforeFirstRow,
            DwBuffer.Primary,
            rowCount,
            RowStatusAt);

        // [:L322, :L334] `do while(nRow > 0)`
        while (row > ItemStatusMachine.NoMoreModifiedRows)
        {
            // [:L323, :L335] `nCnt++` - counts EVERY modified row, not only newly-modified ones. That is
            // the difference from the write-back's walk, and it is deliberate: the worker's ordinal counts
            // the same population, so the two must agree or the translation lands on the wrong row.
            ordinal++;

            // [:L324, :L336] `if nCnt = _lastDBError.row then`
            if (ordinal == latched.Row)
            {
                // [:L325, :L337] `_lastDBError.row = nRow` - ONLY the row. Every other member is carried
                // through untouched by the with-expression.
                LastDbError = latched with { Row = row };

                // [:L326, :L338] `return` - the first match wins and the walk stops.
                return;
            }

            // [:L328, :L340] advance.
            row = ItemStatusMachine.GetNextModified(row, DwBuffer.Primary, rowCount, RowStatusAt);
        }

        // Falling out without a match leaves the row as the raw ordinal, which is what the oracle does
        // when the reported ordinal exceeds the number of modified rows [the loop at :L322 simply ends].
    }

    #endregion

    #region The caller-side notify vocabulary - decode only, because Buffers/ owns the encode

    /// <summary>
    /// Decodes an update-progress notification's packed payload - the caller-side counterpart of
    /// <c>Constant Long NCD_PROGRESS = 1</c> and its payload comment
    /// <c>lparam:(low word:current,high word:total)</c> [<c>:L32</c>].
    /// </summary>
    /// <param name="wparam">The notification code, as received.</param>
    /// <param name="lparam">The packed payload, as received.</param>
    /// <param name="current">The progress numerator, from the LOW word.</param>
    /// <param name="total">The progress denominator, from the HIGH word.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="wparam"/> is the progress code and the two words were
    /// unpacked; otherwise <see langword="false"/>, with both outputs zero.
    /// </returns>
    /// <remarks>
    /// <para>
    /// 🔴 <b>THIS FILE DECLARES NO NOTIFY ENUM AND NO ENCODER.</b>
    /// <see cref="SqlUpdateTaskNotifyCode"/> already exists in <c>Buffers/DataWindowBuffers.cs</c>, kept
    /// SEPARATE from <see cref="SqlQueryTaskNotifyCode"/> precisely because both declare a member with the
    /// value 1 - progress here [<c>:L32</c>] versus the query proxy's row cap
    /// [<c>n_cst_threading_task_sqlquery.sru:L28</c>]. In PowerBuilder each is a per-class constant and
    /// they cannot collide; one merged .NET enumeration WOULD alias them and a caller switching on it
    /// would read a row-cap breach as a progress report. The ENCODE direction also already exists, in
    /// <c>Buffers/DataWindowBuffers.cs</c>, so only the caller-side decode is owned here - which is what a
    /// caller-side constant is for in the first place.
    /// </para>
    /// <para>
    /// <b>WORD WIDTHS ARE FIXED AND THE CONVERSION IS EXPLICIT.</b> PowerBuilder's <c>ulong</c> is
    /// 32 bits and its <c>uint</c> is 16 - NOT the wider C# types of the same names - which is why the
    /// packing helpers are typed <see cref="uint"/> and <see cref="ushort"/> and why the native prototype
    /// reads <c>global function ulong makelong(readonly uint low, readonly uint high)</c>
    /// [<c>ws_objects/pfw.common.pbl.src/makelong.srf:L7</c>]. That prototype also fixes the ORDER: the
    /// FIRST argument is the LOW word. The narrowing below is written out rather than hidden, and no shift
    /// or mask is hand-rolled anywhere in this file - <see cref="Bits.LoWord"/> and
    /// <see cref="Bits.HiWord"/> are the exact inverses of the packer.
    /// </para>
    /// <para>
    /// <b>Static and side-effect free</b>, so a caller can decode a notification without a live proxy and
    /// the pack/unpack round trip is assertable with no thread and no database (C-H).
    /// </para>
    /// </remarks>
    internal static bool TryDecodeProgress(long wparam, long lparam, out long current, out long total)
    {
        current = 0L;
        total = 0L;

        // The code is compared as the enumeration rather than as a bare 1, so the comparison names WHICH
        // task's vocabulary it belongs to. A query task's row-cap notification carries the same number and
        // must not be decoded here.
        if (wparam != (long)SqlUpdateTaskNotifyCode.Progress)
        {
            return false;
        }

        // The payload occupies the low 32 bits, matching the `ulong` the packer answers. The conversion is
        // explicit because a 64-bit `long` is wider than the value it transports.
        uint packed = unchecked((uint)lparam);

        // `lparam:(low word:current,high word:total)` [:L32] - low is current, high is total.
        current = Bits.LoWord(packed);
        total = Bits.HiWord(packed);

        return true;
    }

    #endregion
}

#endregion
