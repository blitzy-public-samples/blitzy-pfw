// =================================================================================================
//  Tasks/TaskProxies/SqlQueryTaskProxy.cs - the CALLER-SIDE query proxy.
// =================================================================================================
//  PROVENANCE. The port of the 532-line read-only legacy object
//  ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru, which derives from
//  n_cst_threading_task_sqlbase.sru (222 lines, ported as SqlTaskProxyBase) and is the largest of the
//  folder's four proxies. Its whole surface is the shape contract C-05 persistence.v1.QueryService
//  carries. The legacy tree is read-only behavioural oracle (C-C): it is never edited, moved,
//  deleted or reformatted, and EVERY behavioural claim below carries a ws_objects/** locator that was
//  opened and read rather than inferred.
//
//  THREAD AFFINITY IS A CONTRACT, NOT COMMENTARY (C-K).
//  This object's own marker is [运行在当前线程] - the CALLING thread - at
//  n_cst_threading_task_sqlquery.sru:L2, and its worker counterpart's is [运行在子线程] at
//  n_cst_thread_task_sqlquery.sru:L2. Measured across all fifteen objects of
//  ws_objects/pfw.thread.ext.pbl.src the split is:
//
//      [运行在子线程]     worker thread   6   n_cst_thread_task_sqlbase, _sqlbase_ds_mt,
//                                            _sqlbase_hook, _sqlcommand, _sqlquery, _sqlupdate
//      [运行在主线程]     main thread     1   n_cst_thread_task_sqlbase_ds - EXACTLY ONE
//      [运行在当前线程]   calling thread  4   the four proxies of this folder
//
//  THE PROXY/WORKER PAIR IS NOT COLLAPSED INTO ONE ASYNC METHOD. The .NET expression of the pair is
//  async request/response over a CancellationToken with BOTH TYPES AND THE BOUNDARY PRESERVED. The
//  proof that a single fused method cannot express it is the two-sided reset: this type's Reset()
//  calls the BASE first [:L284], the base delegates to the WORKER [n_cst_threading_task_sqlbase.sru
//  :L61] and ABANDONS on the worker's refusal [:L62] leaving caller-side state untouched, and only
//  then does this type clear its OWN nine fields [:L287-L296]. Three state sets, two delegations and
//  a failure mode in which the outermost state survives - a fused method has one state set and
//  therefore cannot have that failure mode at all, so fusing would silently delete a behaviour.
//
//  THE `ref blob` CALLBACK SHAPE IS HAZARD 1's CONSEQUENCE, NOT A STYLE CHOICE (C-K).
//  docs/PB多线程绕坑提示.md is five lines long and both of them constrain this file:
//    * HAZARD 1 [:L1-L4] A worker thread synchronously calling a MAIN-THREAD object's function or
//      event whose return type is `string` or `blob` can corrupt memory. The stated trigger is a
//      Post message that has not executed for an object which is itself destroyed, and the stated
//      guidance is to return via `ref` instead. That is EXACTLY why the two payload-bearing events
//      this object declares carry their buffer INWARD by reference and answer a numeric code:
//          event type long onchilddatareceived ( string name, ref blob blbdata )          [:L10]
//          event type long ondatachunk ( ref blob blbdata, long count, long current,
//                                        boolean fullstate )                              [:L13]
//      A third site of the same shape exists in the sibling update proxy's update-data setter
//      [n_cst_threading_task_sqlupdate.sru], so the pattern is folder-wide. Large payloads cross
//      this boundary as by-reference buffers and NEVER as return values: OnChildDataReceived and
//      OnDataChunk below take `ref CarrierState?` and return `long`, and nothing in this file
//      returns a payload.
//    * HAZARD 2 [:L5] After a global object is passed to a worker thread the worker must NULL its
//      reference to the main-thread object in its uninit event. The .NET analogue is to clear
//      cross-boundary references EXPLICITLY on teardown and never rely on the collector, which is
//      why both handlers clear the handed-over buffer at [:L139] and [:L245], why Dispose destroys
//      the held carrier deterministically, and why the ownership transfers below leave exactly one
//      owner behind.
//
//  THE NOTIFY CODES STAY IN TWO SEPARATE ENUMERATIONS AND THIS FILE DECLARES NEITHER (C-K).
//  n_cst_threading_task_sqlquery.sru:L28-L33 declares six codes on the QUERY class -
//  NCD_MAXROWS=1, NCD_DATARECEIVED=2, NCD_PAGERECEIVED=3, NCD_DATACHUNK=4, NCD_CHILDRECEIVED=5,
//  NCD_CHILDQUERY=6 - while n_cst_threading_task_sqlupdate.sru:L32 declares NCD_PROGRESS=1 on the
//  UPDATE class. THE VALUE 1 COLLIDES NUMERICALLY ACROSS THE TWO TASK TYPES; in the legacy each set
//  is scoped to its own class so they can never be confused. Buffers/DataWindowBuffers.cs already
//  publishes them as two separate PascalCase enumerations - SqlQueryTaskNotifyCode and
//  SqlUpdateTaskNotifyCode - preserving that collision, and this file CONSUMES SqlQueryTaskNotifyCode
//  rather than declaring, re-numbering or merging anything. A merge would silently alias two
//  unrelated notifications, and a renumbering would invalidate every stored characterization
//  recording, because the numeric values travel in the notification stream.
//
//  CONSTRAINTS. There are NO user rules for this project: review_rules returns exactly one line
//  saying so, and nothing is invented, inferred or back-filled in their place. The binding set is the
//  enterprise-standard baseline plus the named non-rule constraints, and the ruling FOR THIS FILE is:
//    C-A/C-I  Only SqlTaskProxyBase, Tasks/SqlQueryTask.cs, the sibling folders of this project and
//             the four project edges the .csproj already declares are referenced. Nothing here
//             reaches a peer service, PowerFramework.Shared.Eventful or
//             PowerFramework.Shared.Localization, and NO PackageReference is added. The file builds
//             warning-clean under TreatWarningsAsErrors with nullable enabled.
//    C-B      Ten legacy behaviours that read as defects are reproduced AND ANNOTATED AT THE POINT OF
//             REPRODUCTION, never corrected: the full-state-versus-changeset `>1` normalisation in
//             OPPOSITE directions [:L247-L251] plus the child handler's failure-only form [:L141];
//             the `> 1` sentinel-screening length test for filter and sort [:L145-L150]; the
//             never-reset record count and redraw flag in Reset() [:L282-L299] against the prepare
//             hook that DOES reset both [:L503-L513]; the destroy-then-adopt ownership order
//             [:L268-L269]; the data-move handler's MISSING cancellation check [:L268]; the
//             no-receiver branch's MISSING empty-payload arm [:L231-L243]; the array parameter form
//             bypassing its own reset so the has-parameters flag desynchronises [:L369]; the two
//             PUBLIC members carrying the `_of_` private-convention prefix [:L442, :L457]; and the
//             three DEBUG-gated assertions with ZERO un-gated ones [:L275, :L303, :L312].
//    C-C      The legacy tree and docs/PB多线程绕坑提示.md are read-only oracle. Locators, never edits.
//    C-D      No deferred-service coupling. n_scriptinvoker is NOT ported: the legacy's fixed
//             two-to-five parameter arities [:L325-L334] are a PowerScript variadic escape hatch with
//             no C# analogue - `params` expresses all of them - so there is nothing to port and no
//             ScriptBridge edge is created. pfw.utility.regexp is not reached either, so no Documents
//             edge is created.
//    C-E      No connection is opened and no engine is selected. Storage belongs to Data/ and
//             Transactions/, reached by the WORKER and never from here.
//    C-F      No credential literal appears here in any form, including commented out. This file
//             never handles a statement-bearing payload: the four texts that reach ReportError are
//             the codec's own verbatim diagnostics - GetChanges Failed, TransData Failed and their
//             per-column forms [Buffers/ChangesetCodec.cs] - and carry no statement. The database
//             error latch and its SINGLE sanctioned wire projection, which masks the statement
//             unconditionally, are the base's inherited LastDbError and GetLastDbErrorForWire();
//             Errors/DbErrorData.cs and Errors/SqlRedactor.cs are therefore consumed through that
//             inherited surface rather than imported and re-projected here, and TransactionData's
//             write-only credential field is never read, logged, formatted or forwarded.
//    C-G      No listener, no route, no endpoint. This type is reachable only through the authorized
//             C-05 gRPC methods in Grpc/QueryService.cs.
//    C-H      The inherited TimeProvider is the ONLY clock: no DateTime.UtcNow, no DateTime.Now, no
//             Environment.TickCount and no Stopwatch appears anywhere in this file, and the throttled
//             progress notification in Buffers/ is the service's clock consumer and already uses that
//             seam, so no second clock is introduced. Every collaborator is a SUBSTITUTABLE SEAM, so
//             this proxy is testable with NO REAL THREAD AND NO DATABASE; internal visibility reaches
//             PowerFramework.Persistence.Tests through the .csproj's InternalsVisibleTo.
//    C-J      The legacy auto-instance `global n_cst_threading_task_sqlquery
//             n_cst_threading_task_sqlquery` [:L17] is NOT reproduced. This is a DI-registered type
//             with no static mutable state, and it is DISTINCT from Tasks/SqlQueryTask.cs: a
//             composition root must register BOTH, because they are the two halves of one pair.
//    C-K      Every technology-specific and boundary-specific decision above and below is recorded
//             with its evidence rather than asserted.
//    R9       One-based indexing is the refactor's most dangerous mechanical hazard. The four
//             one-based sites in this file - the array parameter loop [:L372], the paged-unique-index
//             validation loop [:L465], the default select index of 1 [:L380, :L383] and the chunk
//             counters carried by OnDataChunk - are each audited individually at the point of use
//             with the conversion named. No reverse iteration exists here to "correct": the inverted
//             traversal lives in Concurrency/IdentityColumnResolver.cs, which collects the Filter
//             buffer BACKWARD because that buffer's order is genuinely inverted relative to its
//             source, and the update proxy applies FORWARD - the pairing is what re-aligns the data,
//             so neither half may be "fixed" on its own.
//    .editorconfig  TaskProxies/ sits OUTSIDE every naming-suppression glob in the repository-root
//             .editorconfig - only Shared.Kernel/RetCode.cs, Shared.Kernel/Enums.cs,
//             Shared.Localization/Categories.cs, six DataServices files, Sql/ClauseModifier.cs,
//             Sql/Paging/IPagingRewriter.cs and Security/Crypto/LegacyDefaults.cs are listed - while
//             TreatWarningsAsErrors is true. Declaring NCD_*, TASK_TYPE, SQL_MS_* or any other
//             SCREAMING_SNAKE identifier here would be a BUILD BREAK rather than a style nit, so the
//             notify codes come from Buffers/, the clause-modification styles from Shared.Kernel's
//             Enums and the generated contract enum, and the return codes from RetCode. Legacy
//             VALUES are preserved exactly; legacy SPELLINGS become PascalCase, per AAP 0.4.5.3.
// =================================================================================================

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PowerFramework.Contracts.Persistence.V1;
using PowerFramework.Persistence.Buffers;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Shared.Diagnostics;

// PowerFramework.Contracts.Common.V1 also declares a RetCode, and the generated contract types are
// imported above, so the kernel's classes are reached through aliases rather than a namespace
// import. This is the identical resolution SqlTaskProxyBase.cs and Tasks/SqlTaskBase.cs use, kept
// character-for-character so all three read the same way.
using Bits = PowerFramework.Shared.Kernel.Bits;
using Enums = PowerFramework.Shared.Kernel.Enums;
using Predicates = PowerFramework.Shared.Kernel.Predicates;
using RetCode = PowerFramework.Shared.Kernel.RetCode;

// The parent namespace PowerFramework.Persistence.Tasks is reached implicitly by namespace nesting,
// which is how SqlQueryTask, SqlTaskBase, ISqlDataStore, ISqlDataStoreFactory, IQueryResultSink and
// IQueryDataWindowRuntime all resolve here without a using directive.
namespace PowerFramework.Persistence.Tasks.TaskProxies;

#region The three-way receiver dispatch, expressed as seams rather than flattened

/// <summary>
/// Which of the two accepted receiver types is installed - the port of the
/// <c>choose case object.TypeOf() case DataWindow!,DataStore!</c> discriminator
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L432-L437</c>].
/// </summary>
/// <remarks>
/// <para>
/// <b>THIS IS NOT DECORATION - THE TWO KINDS BEHAVE DIFFERENTLY (C-B).</b> Every payload-bearing
/// handler in the legacy proxy dispatches three ways: a DataWindow receiver [<c>:L185-L210</c>], a
/// DataStore receiver [<c>:L211-L230</c>], and no receiver at all, in which case the proxy's own held
/// carrier is the target [<c>:L231-L243</c>]. The arms are structurally similar and behaviourally
/// distinct, so flattening them would change behaviour:
/// </para>
/// <list type="bullet">
///   <item>
///     ONLY the DataWindow arm suspends and restores redraw and recalculates groups
///     [<c>:L193-L194</c>, <c>:L200-L204</c>]. The DataStore arm calls neither
///     [<c>:L218-L224</c>], and neither does the held-carrier arm [<c>:L235-L241</c>].
///   </item>
///   <item>
///     ONLY the two receiver arms guard on an empty payload [<c>:L188</c>, <c>:L214</c>]. The
///     held-carrier arm has NO SUCH GUARD and applies unconditionally.
///   </item>
/// </list>
/// <para>
/// The enumeration therefore exists so a reader can see WHICH arm is running, and the handlers below
/// write all three out rather than folding them into one path.
/// </para>
/// </remarks>
internal enum QueryReceiverKind
{
    /// <summary>
    /// <c>_receiver.TypeOf() = DataWindow!</c> [<c>:L185</c>] - the presentational arm, and the only
    /// one that touches redraw or group recalculation.
    /// </summary>
    DataWindow,

    /// <summary>
    /// The <c>else</c> of the same test [<c>:L211</c>], reached for a <c>DataStore!</c> receiver -
    /// structurally identical to <see cref="DataWindow"/> and deliberately free of its two
    /// presentational calls.
    /// </summary>
    DataStore,
}

/// <summary>
/// An installed receiver object - the seam standing in for <c>PowerObject _receiver</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L52</c>] once it has been
/// narrowed to one of the two accepted types.
/// </summary>
/// <remarks>
/// <para>
/// <b>WHY A SEAM RATHER THAN A CONCRETE TYPE (C-K).</b> The legacy receiver is a DataWindow control
/// or a DataStore living in the CALLER's address space, and Persistence is a headless service that
/// owns neither. Declaring the consumed surface as an interface is the same technique
/// <c>Tasks/SqlQueryTask.cs</c> applies to <c>IQueryDataWindowRuntime</c> and
/// <c>Buffers/DataWindowBuffers.cs</c> applies to <c>ICarrierParentTask</c>: name exactly the members
/// the oracle invokes, implement none of them here, and let the composition root supply the
/// implementation. It is also what makes constraint C-H's "testable with no real thread and no
/// database" mechanically true rather than aspirational.
/// </para>
/// <para>
/// <b>THE PRESENTATIONAL PAIR IS DELEGATED, NOT IMPLEMENTED (C-D).</b> Suspending redraw and
/// recalculating groups belong to the deferred DesignSystem capability area, which this phase forbids
/// implementing even as a stub. They appear on this seam because the ORDER AND THE BRANCH
/// RESTRICTION are behaviour that must be reproduced - the flag is raised before the first chunk and
/// the restore is owed after the last, and the finalize hook exists solely to pay that debt when the
/// stream ends early - while the rendering itself is the receiver's own business, on the far side of
/// the boundary. Nothing about a pixel is decided in this file.
/// </para>
/// </remarks>
internal interface IQueryReceiver
{
    /// <summary>
    /// Which accepted type this receiver is - <c>_receiver.TypeOf()</c> [<c>:L185</c>, <c>:L443</c>,
    /// <c>:L524</c>].
    /// </summary>
    QueryReceiverKind Kind { get; }

    /// <summary>
    /// The receiver's definition-and-buffer surface, over which the payload is applied and the
    /// definition is interrogated.
    /// </summary>
    /// <remarks>
    /// The SAME abstraction the held carrier uses, which is what lets one payload-application body
    /// serve all three arms while the arms themselves stay separate. Its
    /// <see cref="ISqlDataStore.Carrier"/> is the buffer half the codecs act on and its
    /// <see cref="ISqlDataStore.Describe"/> is the definition half the create predicate reads.
    /// </remarks>
    ISqlDataStore Store { get; }

    /// <summary>
    /// Suspends or restores repainting - <c>dw.SetRedraw(false)</c> [<c>:L194</c>],
    /// <c>dw.SetRedraw(true)</c> [<c>:L203</c>, <c>:L527</c>].
    /// </summary>
    /// <param name="enable"><see langword="false"/> to suspend, <see langword="true"/> to restore.</param>
    /// <returns>
    /// The DataWindow convention, where <see cref="DataWindowBufferStore.DataStoreSuccess"/> is
    /// success. <b>The legacy discards this result at all three of its call sites</b> and so does this
    /// port; it is surfaced only so an implementation is not forced to swallow its own faults.
    /// </returns>
    /// <remarks>
    /// <b>Invoked ONLY on the <see cref="QueryReceiverKind.DataWindow"/> arm.</b> A
    /// <see cref="QueryReceiverKind.DataStore"/> implementation is never asked and may answer success
    /// without doing anything - the legacy never reaches it either, because the call sites sit inside
    /// the <c>TypeOf() = DataWindow!</c> branch.
    /// </remarks>
    long SetRedraw(bool enable);

    /// <summary>
    /// Recalculates grouping after the last chunk lands - <c>dw.GroupCalc()</c> [<c>:L200</c>].
    /// </summary>
    /// <returns>
    /// The DataWindow convention. <b>The legacy discards it</b>, and so does this port.
    /// </returns>
    /// <remarks>
    /// <b>Invoked ONLY on the <see cref="QueryReceiverKind.DataWindow"/> arm</b>, once, immediately
    /// after <c>ResetUpdate()</c> on the final chunk and before redraw is restored. The DataStore arm
    /// calls <c>ResetUpdate()</c> and stops [<c>:L222-L224</c>], which is the second independent
    /// sighting of the asymmetry and the evidence that group recalculation is presentation.
    /// </remarks>
    long GroupCalc();
}

/// <summary>
/// One column's drop-down child result on the RECEIVE side - the seam standing in for the
/// <c>datawindowchild dwc</c> the legacy obtains through <c>GetChild(name, ref dwc)</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L102</c>, <c>:L115</c>,
/// <c>:L127</c>].
/// </summary>
/// <remarks>
/// <para>
/// <b>WHY THIS IS NOT <see cref="DataWindowBufferStore"/> AND NOT
/// <c>IQueryDataWindowRuntime.TryGetChild</c>'s result (C-K).</b> The SEND side needs only the
/// child's buffers, so <c>Tasks/SqlQueryTask.cs</c>'s runtime seam yields a bare
/// <see cref="DataWindowBufferStore"/>. The RECEIVE side needs three more things that a buffer store
/// does not have and must not grow: it reads the child's own filter and sort expressions back out of
/// its definition [<c>:L145</c>, <c>:L148</c>] and then RE-APPLIES them [<c>:L146</c>, <c>:L149</c>].
/// Those describes are asked of the CHILD and not of its parent, which is why the surface belongs to
/// the child object itself.
/// </para>
/// <para>
/// <b>Re-applying a filter or a sort means EVALUATING A DataWindow EXPRESSION against rows, and no
/// expression evaluator exists in this service</b> - the refactor plan assigns that capability to
/// DataServices, where it is the single largest net-new obligation in the migration. Declaring the
/// two operations on this seam keeps the DECISION here, where the oracle puts it, and leaves the
/// EXECUTION with the object that owns an evaluator. Nothing is silently dropped.
/// </para>
/// </remarks>
internal interface IQueryChildSurface
{
    /// <summary>
    /// The child's buffers, over which the child payload is applied.
    /// </summary>
    DataWindowBufferStore Store { get; }

    /// <summary>
    /// Reads one property of the CHILD's definition - <c>dwc.Describe(property)</c> [<c>:L145</c>,
    /// <c>:L148</c>].
    /// </summary>
    /// <param name="property">
    /// The property name, verbatim. The two this file asks for are
    /// <see cref="SqlQueryTaskProxy.ChildFilterProperty"/> and
    /// <see cref="SqlQueryTaskProxy.ChildSortProperty"/>.
    /// </param>
    /// <returns>
    /// The described value. <b>A ONE-CHARACTER answer is a SENTINEL rather than a value</b> - see
    /// <see cref="SqlQueryTaskProxy.OnChildDataReceived"/>, where the legacy's
    /// <c>Len(...) &gt; 1</c> test is reproduced and explained.
    /// </returns>
    string Describe(string property);

    /// <summary>
    /// Re-applies the child's stored filter expression - <c>dwc.Filter()</c> [<c>:L146</c>].
    /// </summary>
    /// <returns>
    /// The DataWindow convention. <b>The legacy discards it</b>, and so does this port.
    /// </returns>
    long Filter();

    /// <summary>
    /// Re-applies the child's stored sort expression - <c>dwc.Sort()</c> [<c>:L149</c>].
    /// </summary>
    /// <returns>
    /// The DataWindow convention. <b>The legacy discards it</b>, and so does this port.
    /// </returns>
    /// <remarks>
    /// <b>THE ORDER IS FILTER THEN SORT and it is not arbitrary</b> [<c>:L145-L150</c>]: sorting a
    /// set that is about to be filtered would order rows that are then removed.
    /// </remarks>
    long Sort();
}

/// <summary>
/// Resolves a column's drop-down child against a target - the seam standing in for
/// <c>GetChild(name, ref dwc)</c> [<c>:L102</c>, <c>:L115</c>, <c>:L127</c>].
/// </summary>
/// <remarks>
/// The shape deliberately mirrors <c>IQueryDataWindowRuntime.TryGetChild</c> - a target plus a column
/// name, answered as a boolean with an out-parameter - so the send and receive sides read alike. It
/// is a SEPARATE seam because it yields the richer <see cref="IQueryChildSurface"/> the receive side
/// needs, and widening the send side's interface to match would break its existing implementations
/// for a member they can never use.
/// </remarks>
internal interface IQueryChildResolver
{
    /// <summary>
    /// Resolves one column's drop-down child.
    /// </summary>
    /// <param name="target">
    /// The object to resolve against - the receiver's store on the two receiver arms and the held
    /// carrier on the third.
    /// </param>
    /// <param name="columnName">
    /// The column name, exactly as the worker published it in the child payload.
    /// </param>
    /// <param name="child">The resolved child, or <see langword="null"/> when none resolves.</param>
    /// <returns>
    /// <see langword="true"/> when the child resolved, which is the legacy's
    /// <c>GetChild(...) = 1</c>. <b><see langword="false"/> drives the result to
    /// <see cref="DataWindowBufferStore.DataStoreFailure"/></b> [<c>:L110</c>, <c>:L123</c>,
    /// <c>:L135</c>] - unlike the SEND side, which treats an unresolvable child as a column to skip.
    /// </returns>
    bool TryGetChild(
        ISqlDataStore target,
        string columnName,
        out IQueryChildSurface? child);
}

/// <summary>
/// Wraps a carrier the worker has handed over, so it can become this proxy's held result -
/// <c>Data = ds</c> [<c>:L269</c>].
/// </summary>
/// <remarks>
/// <para>
/// <b>WHY THIS SEAM EXISTS AT ALL, STATED PLAINLY (C-K).</b> Every other carrier this proxy holds is
/// built EMPTY by <c>ISqlDataStoreFactory</c> [<c>:L419</c>, <c>:L518</c>], which is the affinity-keyed
/// route through <c>Buffers/DataWindowBuffers.cs</c>'s carrier factory. The hand-over path is
/// different in kind: the carrier arriving at <see cref="SqlQueryTaskProxy.OnDataMove"/> is ALREADY
/// POPULATED and must be adopted rather than created, and no existing seam can wrap one, because the
/// send side never needed to. One member, one legacy line.
/// </para>
/// <para>
/// The definition half of the adopted store is the implementation's business. In the legacy the
/// handed-over object IS a complete DataStore, so a faithful implementation carries whatever
/// definition the worker's carrier was built with rather than synthesising one.
/// </para>
/// </remarks>
internal interface IQueryCarrierAdopter
{
    /// <summary>
    /// Adopts a handed-over carrier as a held result store.
    /// </summary>
    /// <param name="carrier">
    /// The carrier the worker relinquished. The caller owns it from this point, which is what
    /// <c>DataWindowCarrierOwnership.Move</c> guarantees by nulling the sender's reference.
    /// </param>
    /// <returns>The store wrapping it.</returns>
    ISqlDataStore Adopt(DataWindowCarrier carrier);
}

#endregion

#region The proxy

/// <summary>
/// The caller-side control object for an asynchronous SQL retrieval - the port of
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru</c> (532 lines), marked
/// <c>[运行在当前线程]</c> at <c>:L2</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>THIS TYPE IS ONE HALF OF A PAIR AND MUST NOT BE CONFUSED WITH THE OTHER (C-J).</b> The worker
/// half is <see cref="SqlQueryTask"/> in <c>Tasks/SqlQueryTask.cs</c>, marked <c>[运行在子线程]</c>.
/// A composition root registers BOTH: this type is what a caller configures and reads results from,
/// and the worker is what actually runs the retrieval on another thread. Neither is a partial of the
/// other, neither is a base of the other, and fusing them into one async method would delete the
/// two-sided reset behaviour documented in the file header. The legacy auto-instance
/// <c>global n_cst_threading_task_sqlquery n_cst_threading_task_sqlquery</c> [<c>:L17</c>] is
/// deliberately NOT reproduced - there is no static mutable state here.
/// </para>
/// <para>
/// <b>IT IS ALSO THE WORKER'S RESULT SINK, WHICH IS WHAT MAKES THE BOUNDARY VISIBLE.</b> Implementing
/// <see cref="IQueryResultSink"/> is not a convenience: that interface is the already-published port
/// of exactly the six events this legacy object declares [<c>:L10-L15</c>], and
/// <see cref="SqlQueryTask.ExecuteAsync"/> takes it as its only collaborator. The worker therefore
/// drives this proxy across the boundary, and every payload arrives INWARD - three of the six through
/// awaitable members because a chunk now crosses a network edge that its in-process original could
/// not fail on, and the other three synchronously because they cross nothing.
/// </para>
/// <para>
/// <b>The six legacy events, and where each one is.</b>
/// </para>
/// <list type="table">
///   <item>
///     <term><c>onchilddatareceived(string name, ref blob blbdata) returns long</c> [<c>:L10</c>]</term>
///     <description><see cref="OnChildDataReceived"/>, reached from
///     <see cref="SendChildChunkAsync"/></description>
///   </item>
///   <item>
///     <term><c>oncreatedata(readonly string syntax)</c> [<c>:L11</c>]</term>
///     <description><see cref="OnCreateData"/>, reached from <see cref="CreateDataAsync"/></description>
///   </item>
///   <item>
///     <term><c>ondatareceived(long rowcount)</c> [<c>:L12</c>]</term>
///     <description><see cref="OnDataReceived"/></description>
///   </item>
///   <item>
///     <term><c>ondatachunk(ref blob, long count, long current, boolean fullstate) returns long</c>
///     [<c>:L13</c>]</term>
///     <description><see cref="OnDataChunk"/>, reached from <see cref="SendChunkAsync"/> for the
///     changeset arm and from <see cref="SendFullStateChunk"/> for the full-state arm</description>
///   </item>
///   <item>
///     <term><c>onpagereceived(long pagecount, long recordcount)</c> [<c>:L14</c>]</term>
///     <description><see cref="OnPageReceived"/></description>
///   </item>
///   <item>
///     <term><c>ondatamove(datastore ds)</c> [<c>:L15</c>]</term>
///     <description><see cref="OnDataMove"/></description>
///   </item>
/// </list>
/// <para>
/// <b>THE CALLBACK RETURN ALPHABET IS ITS OWN AND IS NOT THE RETURN-CODE ALGEBRA.</b> The two
/// payload-bearing handlers answer <c>{0, 1, -1}</c> - zero for a cancelled early-out [<c>:L96</c>,
/// <c>:L182</c>], one for success and minus one for failure - plus whatever the runtime's own apply
/// answered before normalisation. Those are the DataWindow conventions
/// <see cref="DataWindowBufferStore.DataStoreSuccess"/>,
/// <see cref="DataWindowBufferStore.DataStoreFailure"/> and
/// <see cref="DataWindowBufferStore.EventContinue"/>, NOT <c>RetCode</c> values, and they are
/// deliberately not mapped onto each other: one is a value the PowerBuilder runtime produced and the
/// other is a framework status. The SETTERS, by contrast, answer <c>RetCode</c> throughout.
/// </para>
/// <para>
/// <b>Sealed on purpose.</b> Nine of its members are overrides or interface implementations whose
/// ORDER is contract - the reset that calls its base first, the prepare hook that clears what the
/// reset does not, the finalize hook that pays the redraw debt - and a subclass that reordered any of
/// them would break parity silently.
/// </para>
/// </remarks>
internal sealed class SqlQueryTaskProxy : SqlTaskProxyBase, IQueryResultSink
{
    #region Preserved legacy literals

    /// <summary>
    /// <c>"DataWindow.Type"</c> - the property the create predicate reads [<c>:L446</c>,
    /// <c>:L450</c>, <c>:L453</c>].
    /// </summary>
    /// <remarks>
    /// Declared here because <c>Tasks/SqlTaskBase.cs</c>'s <c>DataWindowProperty</c> catalogue does
    /// not carry it - that catalogue holds the six properties the WORKER reads - and inventing an
    /// entry there for a property only this file asks about would widen a shared type for one caller.
    /// The string is the legacy's, verbatim.
    /// </remarks>
    internal const string TypeProperty = "DataWindow.Type";

    /// <summary>
    /// <c>"datawindow"</c> - the value the create predicate compares against [<c>:L446</c>].
    /// </summary>
    /// <remarks>
    /// <b>The comparison is <c>&lt;&gt;</c>, so ANY other answer - including the describe failure
    /// sentinel - means "this object still has to be built".</b> Reproduced as an ordinal inequality;
    /// see <see cref="NeedsCreatedObject"/>.
    /// </remarks>
    internal const string BuiltObjectTypeValue = "datawindow";

    /// <summary>
    /// <c>"DataWindow.Table.Filter"</c> - the child property read at [<c>:L145</c>].
    /// </summary>
    /// <remarks>
    /// The same string <c>DataWindowProperty.TableFilter</c> carries. It is named again here because
    /// the child seam's describe is asked of the CHILD object, and pointing a reader at the worker's
    /// catalogue would suggest the parent was being interrogated.
    /// </remarks>
    internal const string ChildFilterProperty = "DataWindow.Table.Filter";

    /// <summary>
    /// <c>"DataWindow.Table.Sort"</c> - the child property read at [<c>:L148</c>].
    /// </summary>
    internal const string ChildSortProperty = "DataWindow.Table.Sort";

    /// <summary>
    /// The column separator a paged unique-index column must contain - the <c>"."</c> of
    /// <c>Pos(columns[nIndex],".") = 0</c> [<c>:L466</c>].
    /// </summary>
    internal const char QualifiedColumnSeparator = '.';

    /// <summary>
    /// The length above which a described filter or sort expression counts as substantial - the
    /// <c>&gt; 1</c> of <c>Len(dwc.Describe(...)) &gt; 1</c> [<c>:L145</c>, <c>:L148</c>].
    /// </summary>
    /// <remarks>
    /// <b>THE THRESHOLD IS ONE, NOT ZERO, AND THAT IS DELIBERATE LEGACY BEHAVIOUR (C-B).</b> A
    /// PowerBuilder describe answers the one-character sentinels <c>"?"</c> - not applicable - and
    /// <c>"!"</c> - unreadable - rather than an empty string, so a length of exactly one means "there
    /// is no filter" and NOT "there is a one-character filter". Testing for non-emptiness instead
    /// would re-apply a sentinel as if it were an expression. The identical threshold is encoded on
    /// the send side by <c>ChangesetSourceDefinition.FilterIsSubstantial</c> and
    /// <c>ChangesetSourceDefinition.SortIsSubstantial</c> in <c>Buffers/ChangesetCodec.cs</c>, whose
    /// two sentinel constants name the values being screened.
    /// </remarks>
    internal const int SubstantialExpressionLength = 1;

    /// <summary>
    /// The one-based select-statement index the two-argument clause setters default to -
    /// <c>of_SetOrderByClause(1, ms, clause)</c> [<c>:L380</c>] and
    /// <c>of_SetWhereClause(1, ms, clause)</c> [<c>:L383</c>].
    /// </summary>
    /// <remarks>
    /// <b>R9. ONE-BASED, AND IT IS NOT AN OFF-BY-ONE TO CORRECT.</b> The legacy names the FIRST select
    /// statement of a possibly compound statement, and the worker's clause store scans one-based to
    /// match [<c>Sql/ClauseModifier.cs</c>]. Passing zero here would address nothing; converting to
    /// zero would address the wrong statement in a compound select. The value crosses to the worker
    /// UNCONVERTED, which is the only correct treatment.
    /// </remarks>
    internal const int DefaultSelectIndex = 1;

    /// <summary>
    /// The chunk index that identifies the FIRST chunk of a stream - the <c>1</c> of
    /// <c>if current = 1 then</c> [<c>:L192</c>, <c>:L218</c>, <c>:L235</c>].
    /// </summary>
    /// <remarks>
    /// <b>R9. The chunk counters are ONE-BASED, so the first chunk is 1 and not 0.</b> A zero-based
    /// reading is wrong by one for the WHOLE stream: the first-chunk reset would never fire, so every
    /// chunk after the first would accumulate onto the previous run's rows. The worker documents the
    /// same counter as one-based [<c>persistence.v1.proto</c>, <c>QueryDataChunk.chunk_index</c>], and
    /// <c>ChangesetChunk.IsFirstChunk</c> encodes the identical test on the send side. The value
    /// crosses the boundary unconverted.
    /// <br/>
    /// The corresponding LAST-chunk test needs no constant: it is <c>count = current</c>
    /// [<c>:L198</c>, <c>:L222</c>, <c>:L239</c>], a comparison of the two counters against each other.
    /// </remarks>
    internal const long FirstChunkIndex = 1L;

    #endregion

    #region The injected seams - every collaborator substitutable (C-H)

    private readonly ISqlDataStoreFactory _dataStoreFactory;
    private readonly IQueryDataWindowRuntime _dataWindowRuntime;
    private readonly IQueryChildResolver _childResolver;
    private readonly IQueryCarrierAdopter _carrierAdopter;
    private readonly IChangesetPayloadCodec _payloadCodec;
    private readonly QueryOptions _configuredDefaults;

    #endregion

    #region Caller-side state [:L37-L53]

    /// <summary>
    /// <c>Long _nRowCount</c> [<c>:L46</c>] - the row count of the whole result, latched by
    /// <see cref="OnDataReceived"/>.
    /// </summary>
    private long _rowCount;

    /// <summary>
    /// <c>Long _nPageCount</c> [<c>:L49</c>] - valid only once paging is on, per the legacy's own
    /// comment at <c>:L48</c>.
    /// </summary>
    private long _pageCount;

    /// <summary>
    /// <c>Long _nRecordCount</c> [<c>:L50</c>] - valid only once paging is on.
    /// </summary>
    /// <remarks>
    /// <b>C-B - THIS FIELD IS THE ONE Reset() DOES NOT CLEAR.</b> See <see cref="Reset"/> and
    /// <see cref="OnPrepare"/>, which disagree about it on purpose.
    /// </remarks>
    private long _recordCount;

    /// <summary>
    /// <c>PowerObject _receiver</c> [<c>:L52</c>], narrowed by <see cref="SetReceiver"/> to one of the
    /// two accepted types.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> reproduces both the initial unassigned state and the
    /// <c>SetNull(_receiver)</c> of <see cref="Reset"/> [<c>:L296</c>]. Every payload handler's
    /// three-way dispatch begins by testing it, exactly as <c>if IsValid(_receiver) then</c> does.
    /// </remarks>
    private IQueryReceiver? _receiver;

    /// <summary>
    /// <c>Boolean _bRcvrRedraw</c> [<c>:L53</c>] - whether this proxy owes the receiver a redraw
    /// restore.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Raised with the suspend before the first chunk [<c>:L193-L194</c>] and lowered by whichever of
    /// two places pays the debt first: the last chunk of a completed stream [<c>:L201-L204</c>] or the
    /// finalize hook when the stream ended early [<c>:L521-L530</c>].
    /// </para>
    /// <para>
    /// <b>C-B - Reset() DOES NOT CLEAR IT EITHER.</b> Only <see cref="OnPrepare"/> does
    /// [<c>:L510</c>]. See <see cref="Reset"/>.
    /// </para>
    /// </remarks>
    private bool _receiverRedrawSuspended;

    /// <summary>
    /// Whether <see cref="Dispose(bool)"/> has already run, so teardown is idempotent even after an
    /// ownership transfer has changed which carrier is held.
    /// </summary>
    private bool _disposed;

    #endregion


    #region Construction - `constructor;call super::constructor;Data = Create DataStore` [:L518]

    /// <summary>
    /// Initializes the proxy and creates its held result carrier.
    /// </summary>
    /// <param name="host">
    /// The threading substrate seam the base consumes. Substitutable by design (C-H).
    /// </param>
    /// <param name="logger">
    /// Diagnostics sink. <b>C-F: nothing written through it may carry a statement or a credential</b>,
    /// and nothing in this file writes through it at all - the base logs captured subscriber faults and
    /// this type adds no logging of its own.
    /// </param>
    /// <param name="timeProvider">
    /// The service's single clock seam. <b>C-H: the ONLY clock this file may read, and it reads it
    /// exactly nowhere</b> - it is forwarded to the base and never consulted, because the legacy proxy
    /// calls no <c>CPU()</c>, <c>Now()</c> or <c>Today()</c>.
    /// </param>
    /// <param name="dataStoreFactory">
    /// Builds the held result carrier. <b>This is the affinity-keyed route through
    /// <c>Buffers/DataWindowBuffers.cs</c>'s carrier factory</b>: the concrete
    /// <c>SqlDataStoreFactory.Create</c> answers
    /// <c>new SqlDataObjectStore(DataWindowCarrierFactory.Create(affinity, clock), runtime)</c>, so no
    /// raw carrier is ever constructed here.
    /// </param>
    /// <param name="dataWindowRuntime">
    /// Builds a target from a syntax string - the <c>Create(syntax)</c> of
    /// <see cref="OnCreateData"/>.
    /// </param>
    /// <param name="childResolver">Resolves a column's drop-down child on the receive side.</param>
    /// <param name="carrierAdopter">Adopts a carrier the worker hands over.</param>
    /// <param name="payloadCodec">
    /// Applies a changeset payload to a target - the managed <c>SetChanges</c>. Taken from
    /// <c>Buffers/ChangesetCodec.cs</c> rather than reimplemented, and injected as an interface so a
    /// test can drive the out-of-range results the normalisation exists to coerce.
    /// </param>
    /// <param name="options">
    /// The persistence options, whose <c>Query</c> section seeds the four configurable properties.
    /// </param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>THE CARRIER IS CREATED AT CONSTRUCTION, NOT LAZILY.</b> The legacy constructor's whole body
    /// is <c>Data = Create DataStore</c> [<c>:L518</c>], so the carrier is non-null for the object's
    /// entire lifetime and no handler has to test for its absence. Its counterpart is the destructor's
    /// <c>Destroy Data</c> [<c>:L515</c>], reproduced as <b>deterministic disposal, never
    /// collector-dependent</b> - hazard 2 of <c>docs/PB多线程绕坑提示.md</c>.
    /// </para>
    /// <para>
    /// <b>Why <see cref="CarrierThreadAffinity.MainThread"/>.</b> This object runs on the CALLING
    /// thread [<c>:L2</c>] and the legacy creates a plain <c>DataStore</c> there, not the worker-side
    /// <c>_ds_mt</c> variant. The affinity split exists precisely to distinguish the two - the
    /// hand-over fast path in <c>DataWindowCarrierOwnership.CanMoveWithoutSerialization</c> is
    /// reachable only for a main-thread carrier - so naming the affinity explicitly is what keeps that
    /// path meaningful rather than accidental.
    /// </para>
    /// </remarks>
    internal SqlQueryTaskProxy(
        ISqlTaskProxyHost host,
        ILogger<SqlQueryTaskProxy> logger,
        TimeProvider timeProvider,
        ISqlDataStoreFactory dataStoreFactory,
        IQueryDataWindowRuntime dataWindowRuntime,
        IQueryChildResolver childResolver,
        IQueryCarrierAdopter carrierAdopter,
        IChangesetPayloadCodec payloadCodec,
        IOptions<PersistenceOptions> options)
        : base(host, logger, timeProvider)
    {
        ArgumentNullException.ThrowIfNull(dataStoreFactory);
        ArgumentNullException.ThrowIfNull(dataWindowRuntime);
        ArgumentNullException.ThrowIfNull(childResolver);
        ArgumentNullException.ThrowIfNull(carrierAdopter);
        ArgumentNullException.ThrowIfNull(payloadCodec);
        ArgumentNullException.ThrowIfNull(options);

        _dataStoreFactory = dataStoreFactory;
        _dataWindowRuntime = dataWindowRuntime;
        _childResolver = childResolver;
        _carrierAdopter = carrierAdopter;
        _payloadCodec = payloadCodec;

        // Fail-fast on a structural fault is the legacy posture and is preserved: a proxy configured
        // with a null options body cannot degrade into anything meaningful.
        _configuredDefaults = options.Value?.Query
            ?? throw new ArgumentException(
                "The persistence options carry no Query section. The four configurable query "
                    + "properties are seeded from it, so a proxy cannot be constructed without one.",
                nameof(options));

        // `Data = Create DataStore` [:L518].
        Data = RequireCarrier(
            _dataStoreFactory.Create(CarrierThreadAffinity.MainThread),
            nameof(ISqlDataStoreFactory));

        // The four configured defaults, applied through the SAME body Reset() uses, so a freshly
        // constructed proxy and a freshly reset one are in the same state by construction rather than
        // through two parallel bodies that can drift. The legacy has that property too, because its
        // field initializers [:L37-L40] and its reset [:L287-L290] assign the identical values.
        ApplyConfiguredQueryDefaults();
    }

    #endregion

    #region The identity the base requires

    /// <summary>
    /// <c>string #type = "sqlquery"</c> [<c>:L9</c>], also declared as
    /// <c>constant string TASK_TYPE = "sqlquery"</c> [<c>:L23</c>].
    /// </summary>
    /// <remarks>
    /// <b>The legacy declares the SAME string twice</b> - once as the instance marker the framework
    /// reads and once as a public constant a caller can compare against - and the port expresses both
    /// through this one member. The constant is NOT re-declared under its legacy spelling:
    /// <c>TASK_TYPE</c> is SCREAMING_SNAKE, <c>TaskProxies/</c> sits outside every
    /// naming-suppression glob, and TreatWarningsAsErrors is true, so declaring it would break the
    /// build. The VALUE is preserved exactly, which is the part that is observable.
    /// </remarks>
    protected override string TaskType => "sqlquery";

    /// <summary>
    /// <c>"n_cst_thread_task_sqlquery"</c> - the worker class the framework inserts, returned by
    /// <c>ongettaskclsname</c> [<c>:L500</c>].
    /// </summary>
    /// <remarks>
    /// <para>
    /// The legacy event body is <c>call super::ongettaskclsname;return "n_cst_thread_task_sqlquery"</c>
    /// - it invokes the ancestor and then DISCARDS its answer, overriding unconditionally. Expressing
    /// it as an override of the base's abstract member reproduces that exactly: the base publishes no
    /// value of its own for a derived proxy to fall back to.
    /// </para>
    /// <para>
    /// <b>The legacy CLASS NAME is preserved verbatim rather than renamed to
    /// <see cref="SqlQueryTask"/>.</b> It is the key the framework resolves the worker by, it appears
    /// in log records and characterization recordings, and the base's
    /// <c>ISqlTaskProxyHost.OnInit(string)</c> takes it as its only argument.
    /// </para>
    /// </remarks>
    protected override string WorkerTaskClassName => "n_cst_thread_task_sqlquery";

    #endregion

    #region The four private-write properties [:L37-L40]

    /// <summary>
    /// <c>PrivateWrite Boolean #Paged</c> [<c>:L37</c>] - whether the paging rewrite is on.
    /// </summary>
    /// <remarks>
    /// Public getter, private setter, exactly as <c>PrivateWrite</c> means. It is written by
    /// <see cref="SetPaged"/> and by the reset, and in the former case the value is ALSO forwarded to
    /// the worker - both writes happen, and dropping the local one would leave a caller unable to read
    /// back what it just set.
    /// </remarks>
    internal bool Paged { get; private set; }

    /// <summary>
    /// <c>PrivateWrite Long #PageSize</c> [<c>:L38</c>] - rows per page.
    /// </summary>
    internal long PageSize { get; private set; }

    /// <summary>
    /// <c>PrivateWrite Long #PageIndex</c> [<c>:L39</c>] - the ONE-BASED page ordinal.
    /// </summary>
    /// <remarks>
    /// <b>R9.</b> The worker documents the same field as one-based
    /// [<c>Tasks/SqlQueryTask.cs</c>, <c>n_cst_thread_task_sqlquery.sru:L37</c>], and the value
    /// crosses to it UNCONVERTED. The reset assigns zero [<c>:L287</c>], which is therefore "no page
    /// selected" rather than "the first page" - a distinction that disappears if the value is ever
    /// rebased.
    /// </remarks>
    internal long PageIndex { get; private set; }

    /// <summary>
    /// <c>PrivateWrite Boolean #PageCounting = true</c> [<c>:L40</c>] - whether the total page and
    /// record counts are computed alongside the page.
    /// </summary>
    /// <remarks>
    /// <b>THE ONLY ONE OF THE FOUR WITH A NON-DEFAULT INITIALIZER, AND IT IS <see langword="true"/>.</b>
    /// The reset restores it to <see langword="true"/> as well [<c>:L290</c>] rather than to the
    /// type's default, so counting is opt-OUT and not opt-in. <c>QueryOptions.PageCounting</c> carries
    /// the identical default, which is why seeding from configuration changes nothing observable.
    /// </remarks>
    internal bool PageCounting { get; private set; }

    /// <summary>
    /// <c>PrivateWrite DataStore Data</c> [<c>:L42</c>] - the held result carrier.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Never <see langword="null"/>: it is created in the constructor [<c>:L518</c>], replaced rather
    /// than cleared by <see cref="MoveData"/> [<c>:L419</c>], and replaced rather than cleared by
    /// <see cref="OnDataMove"/> [<c>:L269</c>].
    /// </para>
    /// <para>
    /// It is the target of the THIRD arm of every payload handler's dispatch - the one taken when no
    /// receiver is installed [<c>:L231-L243</c>] - and it is what <see cref="MoveData"/> hands out.
    /// </para>
    /// </remarks>
    internal ISqlDataStore Data { get; private set; }

    #endregion

    #region The three count getters - NO busy guard [:L358-L362, :L401-L402]

    /// <summary>
    /// <c>of_GetPageCount()</c> [<c>:L358-L359</c>] - the total page count, or the legacy's
    /// not-counted value.
    /// </summary>
    /// <returns><c>_nPageCount</c>, verbatim.</returns>
    /// <remarks>
    /// <b>A PLAIN ACCESSOR WITH NO BUSY GUARD, unlike every setter on this type.</b> The legacy body
    /// is a single <c>return</c>, and that asymmetry is deliberate rather than an oversight: reading a
    /// latched count while the retrieval runs is exactly how a caller observes progress, so guarding
    /// it would make the notification stream unreadable. Preserved for all three getters.
    /// </remarks>
    internal long GetPageCount() => _pageCount;

    /// <summary>
    /// <c>of_GetRecordCount()</c> [<c>:L361-L362</c>] - the total record count.
    /// </summary>
    /// <returns><c>_nRecordCount</c>, verbatim.</returns>
    internal long GetRecordCount() => _recordCount;

    /// <summary>
    /// <c>of_GetRowCount()</c> [<c>:L401-L402</c>] - the row count of the whole result.
    /// </summary>
    /// <returns><c>_nRowCount</c>, verbatim.</returns>
    internal long GetRowCount() => _rowCount;

    #endregion

    #region The two PUBLIC members carrying the `_of_` private-convention prefix [:L442, :L457]

    /// <summary>
    /// <c>_of_HasReceiver()</c> [<c>:L457-L458</c>] - whether a receiver object is installed.
    /// </summary>
    /// <value><c>IsValid(_receiver)</c>, verbatim.</value>
    /// <remarks>
    /// <para>
    /// <b>C-B - THE LEGACY DECLARES THIS PUBLIC DESPITE THE <c>_of_</c> PREFIX, AND THE VISIBILITY IS
    /// CARRIED RATHER THAN TIDIED.</b> Throughout this framework a leading underscore marks a private
    /// member; here the prototype at <c>:L85</c> reads <c>public function boolean
    /// _of_hasreceiver ()</c>. The prefix is misleading legacy convention, not an access modifier, and
    /// the proof that public is correct is the WORKER: it reads this member across the boundary at
    /// <c>n_cst_thread_task_sqlquery.sru:L86</c> and <c>:L583</c>, which a private member could not
    /// serve. Narrowing it would break the pair. This is the same anomaly class as
    /// <c>_of_calcitem</c> in the column-expression engine.
    /// </para>
    /// <para>
    /// It is one of the three conditions of
    /// <c>DataWindowCarrierOwnership.CanMoveWithoutSerialization</c>: a receiver means some other
    /// object has to be populated, so the carrier cannot simply change hands.
    /// </para>
    /// </remarks>
    public bool HasReceiver => _receiver is not null;

    /// <summary>
    /// <c>_of_NeedCreate()</c> [<c>:L442-L455</c>] - whether the receiving side still has to build
    /// itself from a transferred syntax.
    /// </summary>
    /// <value>
    /// <see langword="true"/> when the target's described type differs from
    /// <see cref="BuiltObjectTypeValue"/>.
    /// </value>
    /// <remarks>
    /// <para>
    /// <b>C-B - PUBLIC DESPITE THE <c>_of_</c> PREFIX</b> [prototype at <c>:L84</c>], for the same
    /// reason as <see cref="HasReceiver"/>: the worker reads it at
    /// <c>n_cst_thread_task_sqlquery.sru:L103</c>, <c>:L559</c> and <c>:L583</c>.
    /// </para>
    /// <para>
    /// <b>The three-way dispatch appears here too</b> [<c>:L442-L454</c>], and the two receiver arms
    /// are textually identical - the legacy writes the describe twice only because it has to cast to a
    /// different concrete type in each branch. Since both arms reach the same
    /// <see cref="ISqlDataStore"/> surface here, the receiver case is one expression; the held-carrier
    /// case remains separate because it interrogates a DIFFERENT OBJECT.
    /// </para>
    /// <para>
    /// <b>The comparison is an inequality against a literal, so an unreadable answer means "needs
    /// creating".</b> A store with no resolved definition describes as the failure sentinel
    /// [<c>Tasks/SqlTaskBase.cs</c>], which is not <see cref="BuiltObjectTypeValue"/>, so a
    /// freshly-created carrier answers <see langword="true"/> - which is precisely the state the
    /// legacy predicate exists to detect. Ordinal comparison, because this is a protocol token and not
    /// user-facing text.
    /// </para>
    /// </remarks>
    public bool NeedsCreatedObject =>
        !string.Equals(
            (_receiver?.Store ?? Data).Describe(TypeProperty),
            BuiltObjectTypeValue,
            StringComparison.Ordinal);

    #endregion


    #region The three DEBUG-gated setters - and there are ZERO un-gated assertions here [:L273, :L301, :L310]

    /// <summary>
    /// <c>of_SetSQL(readonly string sql)</c> [<c>:L273-L280</c>] - installs the statement.
    /// </summary>
    /// <param name="sql">The statement, verbatim.</param>
    /// <returns>
    /// <c>RetCode.E_BUSY</c> while busy; otherwise the worker's own answer, forwarded UNCHANGED.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>C-B - THE ASSERTION IS DEBUG-GATED, AND ALL THREE OF THIS FILE'S ASSERTIONS ARE.</b> The
    /// legacy wraps it in <c>#IF DEFINED DEBUG THEN ... #END IF</c> [<c>:L275-L277</c>], so a release
    /// build accepts an empty statement here and lets the worker answer. The sibling update proxy is
    /// deliberately INCONSISTENT with this - it carries one un-gated assertion and one gated one - and
    /// the two files are NOT harmonised: each reproduces what its own oracle does.
    /// </para>
    /// <para>
    /// <b>The message string is the legacy's, verbatim</b> - <c>"Len(sql) &lt;= 0"</c>, which states
    /// the FAILING condition rather than the required one. Preserved because it is observable in an
    /// assertion payload and in characterization recordings.
    /// </para>
    /// <para>
    /// The class is <c>Assertions</c> and not <c>Assert</c>: a static class cannot contain a member of
    /// its own name (CS0542), so <c>Shared.Diagnostics/Assert.cs</c> declares
    /// <see cref="Assertions"/> carrying <c>Assert</c>. <c>Debug.Assert</c> is NOT used - the ported
    /// helper carries the framework's seven-field assertion payload and its fail-fast posture.
    /// </para>
    /// </remarks>
    internal long SetSql(string? sql)
    {
        // `if of_IsBusy() then return RetCode.E_BUSY` [:L273].
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

#if DEBUG
        // `Assert(Len(sql) > 0,"Len(sql) <= 0")` [:L276].
        Assertions.Assert(sql is { Length: > 0 }, "Len(sql) <= 0");
#endif

        // `return _of_GetTask().of_SetSQL(sql)` [:L279] - the worker's answer, unexamined.
        return RequireQueryTask().SetSql(sql);
    }

    /// <summary>
    /// <c>of_SetSQLSyntax(readonly string sqlsyntax)</c> [<c>:L301-L308</c>] - installs a
    /// caller-supplied DataWindow syntax instead of deriving one from the statement.
    /// </summary>
    /// <param name="sqlSyntax">The syntax, verbatim.</param>
    /// <returns><c>RetCode.E_BUSY</c> while busy; otherwise the worker's answer.</returns>
    /// <remarks>
    /// Legacy assertion message preserved verbatim: <c>"Len(sqlSyntax) &lt;= 0"</c>. DEBUG-gated
    /// [<c>:L303-L305</c>].
    /// </remarks>
    internal long SetSqlSyntax(string? sqlSyntax)
    {
        // [:L301]
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

#if DEBUG
        // `Assert(Len(sqlSyntax) > 0,"Len(sqlSyntax) <= 0")` [:L304].
        Assertions.Assert(sqlSyntax is { Length: > 0 }, "Len(sqlSyntax) <= 0");
#endif

        // [:L307]
        return RequireQueryTask().SetSqlSyntax(sqlSyntax);
    }

    /// <summary>
    /// <c>of_SetDataObject(readonly string dataobject)</c> [<c>:L310-L317</c>] - names a compiled
    /// DataWindow to load the definition from.
    /// </summary>
    /// <param name="dataObject">The data object name, verbatim.</param>
    /// <returns><c>RetCode.E_BUSY</c> while busy; otherwise the worker's answer.</returns>
    /// <remarks>
    /// Legacy assertion message preserved verbatim: <c>"Len(dataObject) &lt;= 0"</c>. DEBUG-gated
    /// [<c>:L312-L314</c>].
    /// </remarks>
    internal long SetDataObject(string? dataObject)
    {
        // [:L310]
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

#if DEBUG
        // `Assert(Len(dataObject) > 0,"Len(dataObject) <= 0")` [:L313].
        Assertions.Assert(dataObject is { Length: > 0 }, "Len(dataObject) <= 0");
#endif

        // [:L316]
        return RequireQueryTask().SetDataObject(dataObject);
    }

    #endregion

    #region The four setters that write LOCALLY AND forward [:L337, :L344, :L351, :L409]

    /// <summary>
    /// <c>of_SetPaged(readonly boolean paged)</c> [<c>:L337-L342</c>].
    /// </summary>
    /// <param name="paged">Whether to rewrite the statement for paging.</param>
    /// <returns><c>RetCode.E_BUSY</c> while busy; otherwise the worker's answer.</returns>
    /// <remarks>
    /// <b>BOTH WRITES HAPPEN, AND THE LOCAL ONE COMES FIRST</b> [<c>:L339</c> then <c>:L341</c>]. The
    /// local property is what a caller reads back; the forwarded value is what the retrieval acts on.
    /// Note that the local write is NOT rolled back when the worker refuses - the legacy assigns
    /// before it delegates and never revisits the assignment - so a refused set leaves the two halves
    /// disagreeing. That is legacy behaviour and is reproduced (C-B).
    /// </remarks>
    internal long SetPaged(bool paged)
    {
        // [:L337]
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

        // `#Paged = paged` [:L339].
        Paged = paged;

        // [:L341]
        return RequireQueryTask().SetPaged(paged);
    }

    /// <summary>
    /// <c>of_SetPageSize(readonly long pagesize)</c> [<c>:L344-L349</c>].
    /// </summary>
    /// <param name="pageSize">Rows per page.</param>
    /// <returns><c>RetCode.E_BUSY</c> while busy; otherwise the worker's answer.</returns>
    internal long SetPageSize(long pageSize)
    {
        // [:L344]
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

        // `#PageSize = pageSize` [:L346].
        PageSize = pageSize;

        // [:L348]
        return RequireQueryTask().SetPageSize(pageSize);
    }

    /// <summary>
    /// <c>of_SetPageIndex(readonly long pageindex)</c> [<c>:L351-L356</c>].
    /// </summary>
    /// <param name="pageIndex">
    /// The ONE-BASED page ordinal. <b>R9: forwarded unconverted</b>, because the worker's field is
    /// one-based too.
    /// </param>
    /// <returns><c>RetCode.E_BUSY</c> while busy; otherwise the worker's answer.</returns>
    internal long SetPageIndex(long pageIndex)
    {
        // [:L351]
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

        // `#PageIndex = pageIndex` [:L353].
        PageIndex = pageIndex;

        // [:L355]
        return RequireQueryTask().SetPageIndex(pageIndex);
    }

    /// <summary>
    /// <c>of_SetPageCounting(readonly boolean counting)</c> [<c>:L409-L414</c>].
    /// </summary>
    /// <param name="counting">Whether to compute the total page and record counts.</param>
    /// <returns><c>RetCode.E_BUSY</c> while busy; otherwise the worker's answer.</returns>
    /// <remarks>
    /// The fourth of the local-write-and-forward group, and the one whose default is
    /// <see langword="true"/>. Note it sits fifty lines away from its three siblings in the oracle,
    /// among the delegate-only setters - a layout accident of the PowerBuilder editor, not a semantic
    /// grouping.
    /// </remarks>
    internal long SetPageCounting(bool counting)
    {
        // [:L409]
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

        // `#PageCounting = counting` [:L411].
        PageCounting = counting;

        // [:L413]
        return RequireQueryTask().SetPageCounting(counting);
    }

    #endregion

    #region The delegate-only setters - busy guard, then forward, nothing else

    /// <summary>
    /// <c>of_SetChunkSize(readonly long chunksize)</c> [<c>:L404-L407</c>].
    /// </summary>
    /// <param name="chunkSize">Rows per transferred chunk.</param>
    /// <returns>
    /// <c>RetCode.E_BUSY</c> while busy; otherwise the worker's answer, which is
    /// <c>RetCode.E_INVALID_ARGUMENT</c> for any value at or below the legacy's inclusive floor of
    /// 1000 [<c>n_cst_thread_task_sqlquery.sru:L410</c>]. <b>The validation lives with the worker and
    /// is NOT duplicated here</b>, because the legacy proxy duplicates none of it either.
    /// </returns>
    internal long SetChunkSize(long chunkSize)
    {
        // [:L404]
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

        // [:L406]
        return RequireQueryTask().SetChunkSize(chunkSize);
    }

    /// <summary>
    /// <c>of_SetMaxRows(readonly long rows)</c> [<c>:L396-L399</c>].
    /// </summary>
    /// <param name="rows">The row cap, where zero means no limit.</param>
    /// <returns><c>RetCode.E_BUSY</c> while busy; otherwise the worker's answer.</returns>
    internal long SetMaxRows(long rows)
    {
        // [:L396]
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

        // [:L398]
        return RequireQueryTask().SetMaxRows(rows);
    }

    /// <summary>
    /// <c>of_SetPageNative(readonly boolean use_native)</c> [<c>:L424-L427</c>].
    /// </summary>
    /// <param name="useNative">Whether to use the engine's own paging implementation.</param>
    /// <returns><c>RetCode.E_BUSY</c> while busy; otherwise the worker's answer.</returns>
    /// <remarks>
    /// The legacy parameter is spelled <c>use_native</c> - the only underscored parameter name in the
    /// object. Re-cased here, because a parameter name is not observable in any payload, log record or
    /// recording, and an underscored one would break the build under this folder's naming rules.
    /// </remarks>
    internal long SetPageNative(bool useNative)
    {
        // [:L424]
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

        // [:L426]
        return RequireQueryTask().SetPageNative(useNative);
    }

    /// <summary>
    /// <c>of_SetCache(readonly boolean cache)</c> [<c>:L477-L480</c>].
    /// </summary>
    /// <param name="cache">Whether the worker retains its result carrier for reuse.</param>
    /// <returns><c>RetCode.E_BUSY</c> while busy; otherwise the worker's answer.</returns>
    /// <remarks>
    /// Caching is the second of the three conditions of
    /// <c>DataWindowCarrierOwnership.CanMoveWithoutSerialization</c>: a cache means the worker keeps
    /// the carrier, so it cannot give it away and <see cref="OnDataMove"/> will not fire.
    /// </remarks>
    internal long SetCache(bool cache)
    {
        // [:L477]
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

        // [:L479]
        return RequireQueryTask().SetCache(cache);
    }

    /// <summary>
    /// <c>of_SetFilter(readonly string filter)</c> [<c>:L482-L485</c>].
    /// </summary>
    /// <param name="filter">The filter expression to apply after retrieval.</param>
    /// <returns><c>RetCode.E_BUSY</c> while busy; otherwise the worker's answer.</returns>
    internal long SetFilter(string? filter)
    {
        // [:L482]
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

        // [:L484]
        return RequireQueryTask().SetFilter(filter);
    }

    /// <summary>
    /// <c>of_SetSort(readonly string sort)</c> [<c>:L487-L489</c>].
    /// </summary>
    /// <param name="sort">The sort expression to apply after retrieval.</param>
    /// <returns><c>RetCode.E_BUSY</c> while busy; otherwise the worker's answer.</returns>
    internal long SetSort(string? sort)
    {
        // [:L487]
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

        // [:L489]
        return RequireQueryTask().SetSort(sort);
    }

    /// <summary>
    /// <c>of_SetHookClass(readonly string hookcls)</c> [<c>:L472-L475</c>].
    /// </summary>
    /// <param name="hookClass">The retrieval-hook class name.</param>
    /// <returns><c>RetCode.E_BUSY</c> while busy; otherwise the worker's answer.</returns>
    internal long SetHookClass(string? hookClass)
    {
        // [:L472]
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

        // [:L474]
        return RequireQueryTask().SetHookClass(hookClass);
    }

    #endregion

    #region The clause setters - TWO ARITIES EACH, and only the wider one is guarded [:L380-L394]

    /// <summary>
    /// <c>of_SetWhereClause(readonly long ms, readonly string clause)</c> [<c>:L383-L384</c>] - the
    /// two-argument form.
    /// </summary>
    /// <param name="ms">
    /// The modification style - <c>Enums.SQL_MS_REPLACE</c> (1), <c>Enums.SQL_MS_APPEND</c> (2) or
    /// <c>Enums.SQL_MS_PREPEND</c> (3), whose generated wire counterpart is the contracts project's
    /// own <c>SQL_MS_*</c> enumeration. <b>Neither set is re-declared here</b>: a local constant would
    /// be a SCREAMING_SNAKE declaration in a folder outside the naming suppressions, and a duplicate
    /// of a published value besides.
    /// </param>
    /// <param name="clause">The clause text.</param>
    /// <returns>Whatever the three-argument form answers.</returns>
    /// <remarks>
    /// <b>NO BUSY GUARD OF ITS OWN - it borrows the wider form's</b> [<c>:L391</c>]. The legacy body is
    /// a single delegating <c>return</c>, and adding a guard here would double-test the same condition
    /// and change nothing.
    /// <br/>
    /// <b>R9: the default select index is the ONE-BASED literal 1</b>
    /// (<see cref="DefaultSelectIndex"/>), naming the FIRST select statement of a possibly compound
    /// statement. Zero would address nothing.
    /// </remarks>
    internal long SetWhereClause(long ms, string? clause) =>
        SetWhereClause(DefaultSelectIndex, ms, clause);

    /// <summary>
    /// <c>of_SetWhereClause(readonly integer selectindex, readonly long ms, readonly string clause)</c>
    /// [<c>:L391-L394</c>] - the three-argument form, and the one that carries the guard.
    /// </summary>
    /// <param name="selectIndex">
    /// The ONE-BASED select-statement ordinal. <b>R9: forwarded UNCONVERTED</b> - the worker's clause
    /// store scans one-based to match.
    /// </param>
    /// <param name="ms">The modification style.</param>
    /// <param name="clause">The clause text.</param>
    /// <returns><c>RetCode.E_BUSY</c> while busy; otherwise the worker's answer.</returns>
    internal long SetWhereClause(int selectIndex, long ms, string? clause)
    {
        // [:L391]
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

        // [:L393]
        return RequireQueryTask().SetWhereClause(selectIndex, ms, clause);
    }

    /// <summary>
    /// <c>of_SetOrderByClause(readonly long ms, readonly string clause)</c> [<c>:L380-L381</c>] - the
    /// two-argument form.
    /// </summary>
    /// <param name="ms">The modification style.</param>
    /// <param name="clause">The clause text.</param>
    /// <returns>Whatever the three-argument form answers.</returns>
    /// <remarks>
    /// <b>R9: defaults to the one-based select index 1</b>, and carries no busy guard of its own.
    /// </remarks>
    internal long SetOrderByClause(long ms, string? clause) =>
        SetOrderByClause(DefaultSelectIndex, ms, clause);

    /// <summary>
    /// <c>of_SetOrderByClause(readonly integer selectindex, readonly long ms, readonly string
    /// clause)</c> [<c>:L386-L389</c>] - the three-argument form.
    /// </summary>
    /// <param name="selectIndex">The ONE-BASED select-statement ordinal, forwarded unconverted.</param>
    /// <param name="ms">The modification style.</param>
    /// <param name="clause">The clause text.</param>
    /// <returns><c>RetCode.E_BUSY</c> while busy; otherwise the worker's answer.</returns>
    internal long SetOrderByClause(int selectIndex, long ms, string? clause)
    {
        // [:L386]
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

        // [:L388]
        return RequireQueryTask().SetOrderByClause(selectIndex, ms, clause);
    }

    #endregion


    #region The one validating setter, the receiver setter and the carrier hand-out

    /// <summary>
    /// <c>of_SetPagedUniqueIndexColumns(string columns[])</c> [<c>:L460-L470</c>] - names the
    /// unique-index columns the first paging strategy splices into the statement.
    /// </summary>
    /// <param name="columns">
    /// The columns, each of which must be table-qualified. <see langword="null"/> is treated as the
    /// empty array, which is what an unassigned PowerBuilder array is.
    /// </param>
    /// <returns>
    /// <c>RetCode.E_BUSY</c> while busy; <c>RetCode.E_INVALID_ARGUMENT</c> on the FIRST column that
    /// carries no separator; otherwise the worker's answer.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>THE ONLY SETTER ON THIS TYPE THAT VALIDATES ANYTHING ITSELF</b>, and the validation is
    /// exactly one rule: <c>if Pos(columns[nIndex],".") = 0 then return RetCode.E_INVALID_ARGUMENT</c>
    /// [<c>:L466</c>]. A qualified name is required because the generated statement joins the
    /// unique-index columns across two aliased derived tables, where a bare column name would be
    /// ambiguous.
    /// </para>
    /// <para>
    /// <b>R9 - AUDITED ONE-BASED LOOP.</b> The legacy walks <c>for nIndex = 1 to
    /// UpperBound(columns)</c>, where the upper bound is the LAST VALID INDEX rather than a count past
    /// the end, so the loop visits every element inclusively. The C# form iterates the sequence
    /// directly: same elements, same order, no index arithmetic, and therefore no off-by-one to make.
    /// <b>The direction is FORWARD and stays forward.</b> The one genuinely inverted traversal in this
    /// service is <c>Concurrency/IdentityColumnResolver.cs</c>'s Filter-buffer walk, which is backward
    /// because that buffer's order is inverted relative to its source; "correcting" either half of
    /// that pairing would produce wrong data that still passes a row-count assertion.
    /// </para>
    /// <para>
    /// <b>The bail is on the FIRST offender and nothing is forwarded</b> - the legacy returns from
    /// inside the loop, so a later valid column never reaches the worker and the worker's previous
    /// list is left untouched. Reproduced exactly.
    /// </para>
    /// </remarks>
    internal long SetPagedUniqueIndexColumns(IReadOnlyList<string>? columns)
    {
        // `if of_IsBusy() then return RetCode.E_BUSY` [:L462].
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

        // `nCount = UpperBound(columns) : for nIndex = 1 to nCount` [:L464-L465]. R9: the one-based
        // walk becomes a direct enumeration - every element, same order, no arithmetic.
        IReadOnlyList<string> supplied = columns ?? [];

        foreach (string column in supplied)
        {
            // `if Pos(columns[nIndex],".") = 0 then return RetCode.E_INVALID_ARGUMENT` [:L466]. A null
            // element is an unassigned PowerBuilder string, which contains no separator either, so it
            // takes the same arm.
            if (column is null || !column.Contains(QualifiedColumnSeparator, StringComparison.Ordinal))
            {
                return RetCode.E_INVALID_ARGUMENT;
            }
        }

        // `return _of_GetTask().of_SetPagedUniqueIndexColumns(columns)` [:L469].
        return RequireQueryTask().SetPagedUniqueIndexColumns(supplied);
    }

    /// <summary>
    /// <c>of_SetReceiver(readonly powerobject object)</c> [<c>:L429-L440</c>] - installs the object the
    /// result is applied to instead of the held carrier.
    /// </summary>
    /// <param name="receiver">
    /// The receiver. <c>powerobject</c> maps to <see cref="object"/> per the plan's type mapping, so
    /// the type narrowing below is the port of <c>TypeOf()</c>.
    /// </param>
    /// <returns>
    /// <c>RetCode.E_BUSY</c> while busy; <c>RetCode.E_INVALID_OBJECT</c> when the argument is not a
    /// valid object; <c>RetCode.E_INVALID_TYPE</c> when it is valid but is neither accepted type; and
    /// <c>RetCode.OK</c> on success.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>THE THREE OUTCOMES ARE THREE DIFFERENT CODES AND THE ORDER OF THE TESTS IS CONTRACT.</b> The
    /// legacy guards busy first [<c>:L429</c>], then validity [<c>:L430</c>], then type
    /// [<c>:L432-L437</c>]. An invalid object therefore never reaches the type test, which matters
    /// because <c>TypeOf()</c> on an invalid object is not meaningful.
    /// </para>
    /// <para>
    /// <c>Predicates.IsValidObject</c> is used rather than a hand-rolled null test so the ported
    /// semantics of <c>IsValidObject</c> are inherited rather than re-derived.
    /// </para>
    /// <para>
    /// <b>The <c>case else</c> arm is NOT a fallback - it is a refusal</b> [<c>:L435-L436</c>]. The
    /// legacy accepts <c>DataWindow!</c> and <c>DataStore!</c> and rejects everything else, which is
    /// reproduced by requiring the argument to be an <see cref="IQueryReceiver"/>: that interface
    /// exists precisely to name the two accepted kinds, and its
    /// <see cref="IQueryReceiver.Kind"/> distinguishes them for the dispatch.
    /// </para>
    /// <para>
    /// <b>Success does not clear anything.</b> Installing a receiver over an existing one replaces the
    /// reference and leaves the previous receiver untouched - this proxy never owned it, so it has
    /// nothing to destroy. Only <see cref="Reset"/> clears it [<c>:L296</c>].
    /// </para>
    /// </remarks>
    internal long SetReceiver(object? receiver)
    {
        // `if of_IsBusy() then return RetCode.E_BUSY` [:L429].
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

        // `if Not IsValidObject(object) then return RetCode.E_INVALID_OBJECT` [:L430].
        if (!Predicates.IsValidObject(receiver))
        {
            return RetCode.E_INVALID_OBJECT;
        }

        // `choose case object.TypeOf() case DataWindow!,DataStore! ... case else return
        // RetCode.E_INVALID_TYPE end choose` [:L432-L437].
        if (receiver is not IQueryReceiver accepted)
        {
            return RetCode.E_INVALID_TYPE;
        }

        // `_receiver = object` [:L434].
        _receiver = accepted;

        // [:L439]
        return RetCode.OK;
    }

    /// <summary>
    /// <c>of_MoveData()</c> [<c>:L416-L422</c>] - hands the held carrier OUT and immediately takes a
    /// fresh empty one.
    /// </summary>
    /// <returns>
    /// The carrier that was held. <b>The caller owns it from this point and is responsible for
    /// disposing it</b>; this proxy will not.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>THIS IS THE EXACT INVERSE OF <see cref="OnDataMove"/>, AND IT IS EMPHATICALLY NOT A
    /// DESTROY.</b> The legacy body is three lines - <c>ds = Data</c>, <c>Data = Create DataStore</c>,
    /// <c>return ds</c> [<c>:L418-L421</c>] - with no <c>Destroy</c> anywhere in it. Getting this
    /// wrong in either direction is a real fault: destroying the outgoing carrier would hand the caller
    /// a dead object, and failing to create the replacement would leave this proxy holding
    /// <see langword="null"/> for every subsequent handler's third dispatch arm.
    /// </para>
    /// <para>
    /// <b>Ownership after the call is unambiguous and that is the point.</b> Exactly one party holds
    /// the outgoing carrier - the caller - and exactly one holds the incoming one - this proxy - so a
    /// later write through a stale reference is impossible by construction. It is also why
    /// <see cref="Dispose(bool)"/> destroys only the carrier it is holding AT THAT MOMENT: destroying a
    /// carrier this proxy has already handed out would be a double free of an object someone else now
    /// owns.
    /// </para>
    /// <para>
    /// <b>NO BUSY GUARD</b>, matching the oracle. Handing the carrier out mid-retrieval is the caller's
    /// risk to take, and the legacy takes no position on it.
    /// </para>
    /// </remarks>
    internal ISqlDataStore MoveData()
    {
        // `ds = Data` [:L418].
        ISqlDataStore outgoing = Data;

        // `Data = Create DataStore` [:L419] - through the affinity-keyed factory, never a raw
        // construction, and on the same main-thread affinity the constructor chose.
        Data = RequireCarrier(
            _dataStoreFactory.Create(CarrierThreadAffinity.MainThread),
            nameof(ISqlDataStoreFactory));

        // `return ds` [:L421].
        return outgoing;
    }

    #endregion

    #region The six parameter entry points - five convenience arities funnelling into one [:L319-L378]

    /// <summary>
    /// <c>of_SetParam(readonly any param)</c> [<c>:L319-L320</c>] - the single-value form.
    /// </summary>
    /// <param name="param">The one parameter. <c>any</c> maps to <see cref="object"/>.</param>
    /// <returns>Whatever <see cref="SetParams"/> answers.</returns>
    /// <remarks>
    /// <b>It exists as a member of its own rather than as a <c>params</c> call with one element</b>
    /// because the legacy declares it separately [<c>:L61</c>] and because a caller passing a single
    /// array-typed value must not have it splatted into several parameters. Its body is
    /// <c>return of_SetParams({param})</c> - one element, wrapped.
    /// </remarks>
    internal long SetParam(object? param) => SetParams([param]);

    /// <summary>
    /// <c>of_SetParams(readonly any params[])</c> [<c>:L364-L378</c>] - the array form every other
    /// entry point funnels into.
    /// </summary>
    /// <param name="parameters">
    /// The parameters in order. <see langword="null"/> is treated as the empty array, which is what an
    /// unassigned PowerBuilder array is.
    /// </param>
    /// <returns>
    /// <c>RetCode.E_BUSY</c> while busy; the FIRST element's non-<c>RetCode.OK</c> answer; otherwise
    /// <c>RetCode.OK</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>SIX LEGACY ENTRY POINTS BECOME TWO C# MEMBERS.</b> The oracle declares a single-value form
    /// [<c>:L319</c>], fixed arities of two, three, four and five [<c>:L325-L334</c>] and this array
    /// form, and every one of the five convenience forms is a one-line
    /// <c>return of_SetParams({...})</c>. <b>The arity ceiling of five is a LEGACY LIMIT, not a
    /// contract</b>: PowerScript cannot forward an arbitrary-length argument list, so the framework
    /// unrolls the call to a fixed maximum and stops - the same variadic-unroll pattern that appears at
    /// twenty arguments in the worker base and at eight in the transaction object. C# has native
    /// variadic support, so <c>params</c> expresses all six forms and MAY exceed five without any
    /// behavioural regression. <b>This is precisely why no <c>n_scriptinvoker</c> port is needed
    /// (C-D)</b>: the legacy's dynamic-invocation escape hatch exists only to work around the missing
    /// language feature, so there is nothing to port and no ScriptBridge coupling is created.
    /// </para>
    /// <para>
    /// <b>C-B - A PRESERVED DESYNCHRONISATION DEFECT, AND IT IS SUBTLE.</b> Line <c>:L369</c> reads
    /// <c>_of_GetTask().of_ResetParams()</c> - it clears the WORKER's parameter list DIRECTLY, bypassing
    /// the inherited proxy-side reset at <c>n_cst_threading_task_sqlbase.sru:L161-L169</c>, which is the
    /// only thing that clears the caller-side has-parameters flag [<c>:L164</c>]. The consequence:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     With a NON-EMPTY array the loop runs, each successful add raises the flag
    ///     [<c>n_cst_threading_task_sqlbase.sru:L155</c>], and the two halves agree by accident.
    ///   </item>
    ///   <item>
    ///     With an EMPTY array the loop body never runs, so the worker's parameters ARE cleared while
    ///     the caller-side flag KEEPS ITS PREVIOUS VALUE. A caller that asks whether parameters are
    ///     installed is then told yes while the worker holds none.
    ///   </item>
    /// </list>
    /// <para>
    /// Reproduced exactly, and pinned by a test. Routing through the inherited reset instead would
    /// "fix" it and would be a behavioural change dressed as tidying.
    /// </para>
    /// <para>
    /// <b>The bail test is <c>&lt;&gt; RetCode.OK</c>, which is STRICTER than the failure
    /// predicate</b> [<c>:L374</c>]. A prevention (1) reads as a SUCCESS under
    /// <c>Predicates.IsFailed</c> but is NOT <c>RetCode.OK</c>, so it aborts the walk here. Using the
    /// predicate would let a vetoed parameter through and continue with the next one.
    /// </para>
    /// <para>
    /// <b>R9 - AUDITED ONE-BASED LOOP.</b> <c>for index = 1 to UpperBound(params)</c> [<c>:L372</c>]
    /// visits every element inclusively; the direct enumeration below visits the same elements in the
    /// same order with no index arithmetic. <b>Each element is added with an EMPTY name</b>
    /// [<c>:L373</c>] - these are positional parameters, and the named overload exists for a different
    /// caller.
    /// </para>
    /// </remarks>
    internal long SetParams(params object?[]? parameters)
    {
        // `if of_IsBusy() then return RetCode.E_BUSY` [:L367].
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

        // `_of_GetTask().of_ResetParams()` [:L369] - THE WORKER'S RESET, DIRECTLY. See the remarks:
        // this deliberately bypasses the inherited ResetParams(), which is the only thing that clears
        // ParamsInstalled, and the resulting caller/worker desynchronisation on an EMPTY array is a
        // preserved defect (C-B). The result is discarded, exactly as the legacy discards it.
        _ = RequireQueryTask().ResetParams();

        // `nCount = UpperBound(params) : for index = 1 to nCount` [:L371-L372]. R9: audited - direct
        // enumeration over the same elements in the same order.
        object?[] supplied = parameters ?? [];

        foreach (object? parameter in supplied)
        {
            // `rtCode = of_AddParam("",params[index])` [:L373] - the INHERITED add, with an empty name.
            long rtCode = AddParam(string.Empty, parameter);

            // `if rtCode <> RetCode.OK then return rtCode` [:L374] - stricter than IsFailed, on purpose.
            if (rtCode != RetCode.OK)
            {
                return rtCode;
            }
        }

        // `return RetCode.OK` [:L377].
        return RetCode.OK;
    }

    #endregion


    #region The two reset paths that DISAGREE - and the disagreement is the behaviour [:L282, :L503]

    /// <summary>
    /// <c>of_Reset()</c> [<c>:L282-L299</c>] - returns the proxy to its configured state.
    /// </summary>
    /// <returns>
    /// The base's code VERBATIM when the base failed; otherwise <c>RetCode.OK</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>THE BASE IS CALLED FIRST AND ITS FAILURE ABANDONS EVERYTHING BELOW</b> [<c>:L284-L285</c>].
    /// That ordering is contract, not style: the base owns the busy guard [<c>:L57</c>] and the
    /// delegation to the worker [<c>:L61</c>], so a busy proxy or a refusing worker leaves this type's
    /// nine fields COMPLETELY UNTOUCHED. Clearing first and delegating afterwards would lose the caller's
    /// configuration on a refusal.
    /// </para>
    /// <para>
    /// <c>Predicates.IsFailed</c> is used rather than a sign test, so the tri-state boundary is
    /// inherited rather than re-derived: <c>RetCode.CANCELLED</c> is EXPLICITLY EXCLUDED from failure,
    /// so a cancelled worker does NOT take the abandon arm and the clears below still happen.
    /// </para>
    /// <para>
    /// <b>C-B - TWO FIELDS ARE DELIBERATELY NOT CLEARED HERE, AND THIS IS THE SINGLE MOST EASILY
    /// "TIDIED" DEFECT IN THE FILE.</b> The oracle clears nine things [<c>:L287-L296</c>]: page index,
    /// paged, page size, page counting, row count, page count, the carrier's rows, the carrier's data
    /// object and the receiver. It does NOT clear:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <c>_nRecordCount</c> [<c>:L50</c>] - while its immediate neighbour <c>_nPageCount</c>
    ///     [<c>:L49</c>] IS cleared at <c>:L293</c>. Two adjacent fields of the same kind, one cleared
    ///     and one not.
    ///   </item>
    ///   <item>
    ///     <c>_bRcvrRedraw</c> [<c>:L53</c>] - so a reset performed while a redraw suspension is
    ///     outstanding leaves the debt recorded, which is arguably why the finalize hook can still pay
    ///     it.
    ///   </item>
    /// </list>
    /// <para>
    /// <b>THE CONTRAST WITH <see cref="OnPrepare"/> IS SHARP AND IS THE PROOF THAT THE OMISSION IS
    /// REAL.</b> The prepare hook at <c>:L503-L513</c> clears record count [<c>:L507</c>], page count
    /// [<c>:L508</c>], row count [<c>:L509</c>] AND the redraw flag [<c>:L510</c>] - four fields, two of
    /// which this method leaves alone. The same author wrote both, forty lines apart, and chose
    /// differently. Both are reproduced exactly and each cites the other, so a future reader cannot
    /// close the gap by accident. Pinned by tests in both directions.
    /// </para>
    /// <para>
    /// <b>The return is <c>RetCode.OK</c> and NOT the base's code</b> [<c>:L298</c>]. Where the base
    /// answered a non-failing non-zero code - a prevention, say - this method still answers OK.
    /// Reproduced.
    /// </para>
    /// </remarks>
    public override long Reset()
    {
        // `rtCode = super::of_Reset()` [:L284] - the busy guard and the worker delegation live there.
        long rtCode = base.Reset();

        // `if IsFailed(rtCode) then return rtCode` [:L285] - abandon, returning the base's code
        // verbatim and leaving every field below untouched.
        if (Predicates.IsFailed(rtCode))
        {
            return rtCode;
        }

        // `#PageIndex = 0 : #Paged = false : #PageSize = 0 : #PageCounting = true` [:L287-L290],
        // through the shared body the constructor also uses. QueryOptions carries the identical
        // defaults, so at default configuration this is bit-for-bit the four legacy literals.
        ApplyConfiguredQueryDefaults();

        // `_nRowCount = 0` [:L292].
        _rowCount = 0L;

        // `_nPageCount = 0` [:L293].
        _pageCount = 0L;

        // C-B: `_nRecordCount` IS NOT CLEARED HERE, although its neighbour above is and although
        // OnPrepare clears it at [:L507]. NOT AN OVERSIGHT IN THIS PORT - the oracle's reset simply
        // does not name the field. Adding a clear would be a silent behavioural change.
        // C-B: `_bRcvrRedraw` IS NOT CLEARED HERE EITHER, although OnPrepare clears it at [:L510].

        // `Data.Reset()` [:L294] - the carrier's ROWS, not the carrier itself. The result is discarded,
        // exactly as the legacy discards it.
        _ = Data.Carrier.Reset();

        // `Data.DataObject = ""` [:L295] - the carrier's DEFINITION, cleared separately from its rows.
        Data.DataObject = string.Empty;

        // `SetNull(_receiver)` [:L296].
        _receiver = null;

        // `return RetCode.OK` [:L298] - NOT rtCode.
        return RetCode.OK;
    }

    /// <summary>
    /// <c>onprepare</c> [<c>:L503-L513</c>] - the per-run hook the framework raises before the worker
    /// starts.
    /// </summary>
    /// <returns>
    /// The ancestor's value when it was non-zero; otherwise <c>0</c>, the framework's continue value.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>THE ANCESTOR RUNS FIRST AND ITS ANSWER IS A VETO</b> [<c>:L503</c>]. The legacy line is
    /// <c>call super::onprepare;if AncestorReturnValue &lt;&gt; 0 then return AncestorReturnValue</c>,
    /// so a non-zero ancestor answer aborts the run and is passed straight back.
    /// </para>
    /// <para>
    /// <b>A ONE-LINE SPELLING INCONSISTENCY THE ORACLE CARRIES AND THIS PORT PRESERVES:</b> this hook
    /// tests the ancestor's answer against the BARE LITERAL <c>0</c> [<c>:L503</c>], while the base's
    /// init hook tests the identical value against the named <c>RetCode.OK</c>
    /// [<c>n_cst_threading_task_sqlbase.sru:L215</c>]. Same value, two spellings, each kept at the site
    /// that uses it.
    /// </para>
    /// <para>
    /// <b>C-K - MEASURED FINDING: THAT GUARD IS PRESERVED-BUT-UNREACHABLE, AND SAYING SO IS THE POINT.</b>
    /// The immediate ancestor's own prepare hook returns the bare literal <c>0</c> UNCONDITIONALLY and
    /// discards ITS super's answer [<c>n_cst_threading_task_sqlbase.sru:L206-L211</c>], so
    /// <c>AncestorReturnValue</c> here can never be non-zero through the shipped chain - in the oracle
    /// or in this port, where <see cref="SqlTaskProxyBase.OnPrepare"/> reproduces that discard
    /// verbatim. The branch is nonetheless written out rather than dropped, because C-B forbids
    /// removing a live-but-unexercised legacy path: an ancestor that later answered a veto would be
    /// honoured, exactly as the oracle would honour it. A test pins the reachable half - that a non-zero
    /// substrate answer does NOT abort and the four clears still happen.
    /// </para>
    /// <para>
    /// <b>C-B - THIS HOOK CLEARS THE TWO FIELDS <see cref="Reset"/> LEAVES ALONE.</b> Four counters and
    /// flags are zeroed here - record count [<c>:L507</c>], page count [<c>:L508</c>], row count
    /// [<c>:L509</c>] and the redraw flag [<c>:L510</c>] - against the reset's six-plus-three. The two
    /// paths disagree deliberately; see <see cref="Reset"/>, which cites this method back.
    /// </para>
    /// <para>
    /// <b>The carrier is reset here TOO, and in the same two steps</b> - rows [<c>:L505</c>] then
    /// definition [<c>:L506</c>] - so a re-run does not inherit the previous run's result. The receiver
    /// is NOT cleared here, which is correct: a caller installs a receiver once and expects it to
    /// survive successive runs.
    /// </para>
    /// </remarks>
    protected override long OnPrepare()
    {
        // `call super::onprepare` [:L503] - the base clears the latched database error [:L208].
        long ancestorReturnValue = base.OnPrepare();

        // `if AncestorReturnValue <> 0 then return AncestorReturnValue` [:L503]. THE BARE LITERAL,
        // spelled as the oracle spells it - see the remarks on the inconsistency with the base's init.
        if (ancestorReturnValue != 0L)
        {
            return ancestorReturnValue;
        }

        // `Data.Reset()` [:L505] - rows.
        _ = Data.Carrier.Reset();

        // `Data.DataObject = ""` [:L506] - definition.
        Data.DataObject = string.Empty;

        // `_nRecordCount = 0` [:L507] - C-B: CLEARED HERE, and deliberately NOT in Reset().
        _recordCount = 0L;

        // `_nPageCount = 0` [:L508].
        _pageCount = 0L;

        // `_nRowCount = 0` [:L509].
        _rowCount = 0L;

        // `_bRcvrRedraw = false` [:L510] - C-B: CLEARED HERE, and deliberately NOT in Reset(). Note
        // this is a plain clear and NOT a restore: it forgets the debt rather than paying it, which is
        // safe only because a prepare precedes a run rather than following one.
        _receiverRedrawSuspended = false;

        // `return 0 //continue` [:L512].
        return 0L;
    }

    /// <summary>
    /// <c>onfinalize</c> [<c>:L521-L531</c>] - the redraw-leak guard the framework raises when the run
    /// ends, however it ends.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THIS HOOK EXISTS FOR EXACTLY ONE REASON: TO PAY A DEBT THE CHUNK STREAM MAY HAVE LEFT
    /// OUTSTANDING.</b> The DataWindow arm of <see cref="OnDataChunk"/> suspends repainting before the
    /// first chunk [<c>:L193-L194</c>] and restores it after the last [<c>:L201-L204</c>]. If the
    /// stream never reaches its last chunk - cancelled, failed, or aborted mid-transfer - the restore
    /// never runs and the receiver would stay frozen forever. This hook is the only thing that prevents
    /// that.
    /// </para>
    /// <para>
    /// <b>C-B - THE RESTORE IS RESTRICTED TO THE DataWindow BRANCH, AND THE FLAG IS LOWERED
    /// UNCONDITIONALLY.</b> The oracle's order is: test the flag [<c>:L521</c>], LOWER IT
    /// [<c>:L522</c>], then test validity [<c>:L523</c>] and type [<c>:L524</c>] before restoring
    /// [<c>:L527</c>]. So when the receiver has been cleared by a reset, or is a DataStore, the flag is
    /// still lowered and no restore happens - the debt is forgotten rather than paid. Reproduced
    /// exactly, including the order, because lowering after the branch would leave the flag raised on
    /// the DataStore path and make a second finalize behave differently from the first.
    /// </para>
    /// <para>
    /// <b>The base does not port this hook, so there is no ancestor call.</b> The legacy line begins
    /// <c>call super::onfinalize</c>, but the framework parent's <c>onfinalize()</c>
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L22</c>] takes no arguments, returns
    /// nothing and is outside the slice <c>SqlTaskProxyBase</c> consumes. Stated rather than silently
    /// omitted.
    /// </para>
    /// </remarks>
    internal void OnFinalize()
    {
        // `if _bRcvrRedraw then` [:L521].
        if (!_receiverRedrawSuspended)
        {
            return;
        }

        // `_bRcvrRedraw = false` [:L522] - LOWERED FIRST, before the branch that may decline to pay.
        _receiverRedrawSuspended = false;

        // `if IsValid(_receiver) then if _receiver.TypeOf() = DataWindow! then dw.SetRedraw(true)`
        // [:L523-L528]. C-B: the DataStore branch and the no-receiver branch both fall through here
        // having already lowered the flag.
        if (_receiver is { Kind: QueryReceiverKind.DataWindow } dataWindowReceiver)
        {
            // [:L527] - the result is discarded, exactly as the legacy discards it.
            _ = dataWindowReceiver.SetRedraw(true);
        }
    }

    #endregion


    #region The six receive callbacks - payloads INWARD by reference, per hazard 1

    /// <summary>
    /// <c>onchilddatareceived(string name, ref blob blbdata) returns long</c> [<c>:L10</c>, body at
    /// <c>:L93-L155</c>] - applies one column's drop-down child result.
    /// </summary>
    /// <param name="name">
    /// The column whose child this payload belongs to. Carried in the notification's STRING argument
    /// rather than its numeric one, per the legacy's own comment <c>sparam:colname</c> at
    /// <c>:L32</c>.
    /// </param>
    /// <param name="payload">
    /// The child's changeset, carried INWARD BY REFERENCE and <b>CLEARED ON RETURN</b>.
    /// <see langword="null"/> IS the legacy's zero-length blob.
    /// </param>
    /// <returns>
    /// <c>0</c> when cancelled, <see cref="DataWindowBufferStore.DataStoreSuccess"/> on a clean apply,
    /// <see cref="DataWindowBufferStore.DataStoreFailure"/> when the child could not be resolved or the
    /// apply was partial. <b>This alphabet is <c>{0, 1, -1}</c> and is NOT the return-code
    /// algebra.</b>
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>THE <c>ref</c> IS HAZARD 1's CONSEQUENCE, NOT A STYLE CHOICE.</b>
    /// <c>docs/PB多线程绕坑提示.md:L1-L4</c> records that a worker synchronously calling a main-thread
    /// object's member whose return type is <c>string</c> or <c>blob</c> can corrupt memory, and
    /// prescribes returning via <c>ref</c> instead. The legacy signature is that prescription applied.
    /// </para>
    /// <para>
    /// <b>THE PAYLOAD IS CLEARED AFTER HANDOVER</b> - <c>blbData = Blob("")</c> [<c>:L139</c>].
    /// Reproduced as an explicit assignment to <see langword="null"/> through the <c>ref</c>, which is
    /// hazard 2's discipline: cross-boundary references are released EXPLICITLY and never left for the
    /// collector. It happens BEFORE the normalisation and before the notification, so neither can
    /// reach a payload the caller still believes it owns.
    /// </para>
    /// <para>
    /// <b>C-B - THE NORMALISATION HERE IS THE FAILURE FORM ONLY.</b> <c>if rtCode &gt; 1 then rtCode =
    /// -1</c> [<c>:L141</c>]. The child path has NO full-state counterpart at all, so there is no
    /// lenient sibling to confuse it with - which makes this the THIRD data point in the pair of
    /// opposite normalisations documented on <see cref="OnDataChunk"/>. It is written out here rather
    /// than delegated to a shared helper, deliberately: a helper that took the direction as an argument
    /// would hide which direction applies where, and getting the direction backwards is the single
    /// easiest mistake to make in this file.
    /// </para>
    /// <para>
    /// <b>THE THREE-WAY DISPATCH IS REAL BUT ITS ARMS ARE VERIFIED IDENTICAL HERE.</b> The oracle
    /// writes the same eleven lines three times - DataWindow receiver [<c>:L98-L111</c>], DataStore
    /// receiver [<c>:L112-L125</c>], held carrier [<c>:L126-L137</c>] - and the three differ ONLY in
    /// which object they cast to before calling <c>GetChild</c>. There is NO <c>SetRedraw</c> and NO
    /// <c>GroupCalc</c> anywhere in this handler, unlike <see cref="OnDataChunk"/> where the DataWindow
    /// arm genuinely diverges. The three arms are therefore written out as three visible target
    /// selections over one shared body; the body is shared BECAUSE the oracle's three copies are
    /// character-identical, which was checked line by line rather than assumed.
    /// </para>
    /// <para>
    /// <b>THE CHILD IS RESET UNCONDITIONALLY, BEFORE THE PAYLOAD IS EVEN INSPECTED</b>
    /// [<c>:L103</c>, <c>:L116</c>, <c>:L128</c>] - unlike <see cref="OnDataChunk"/>, which resets only
    /// on the first chunk. The reason is structural: a child result transfers WHOLE in one payload, so
    /// there is no sequence for a first-chunk condition to discriminate, and an empty payload therefore
    /// still clears the child.
    /// </para>
    /// </remarks>
    public long OnChildDataReceived(string name, ref CarrierState? payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // `if of_IsCancelled() then return 0` [:L96]. ZERO - the continue value - and not one and not
        // minus one. The notification at :L151 is gated on exactly one, so a cancelled child publishes
        // nothing.
        if (IsCancelled())
        {
            return DataWindowBufferStore.EventContinue;
        }

        long rtCode;
        IQueryChildSurface? child;

        // THE THREE-WAY DISPATCH. `if IsValid(_receiver) then` [:L98].
        if (_receiver is not null)
        {
            // `if _receiver.TypeOf() = DataWindow! then` [:L99] - ARM (a), the DataWindow receiver
            // [:L100-L111]. No presentational call appears in this handler on any arm.
            // `else` [:L112] - ARM (b), the DataStore receiver [:L113-L125], character-identical to (a)
            // in the oracle apart from the cast.
            rtCode = ApplyChildPayload(_receiver.Store, name, ref payload, out child);
        }
        else
        {
            // `else` [:L126] - ARM (c), the held carrier [:L127-L137], again character-identical.
            rtCode = ApplyChildPayload(Data, name, ref payload, out child);
        }

        // `blbData = Blob("")` [:L139] - THE EXPLICIT CLEAR, before the normalisation and before the
        // notification. Hazard 2's discipline, applied to a buffer rather than to an object reference.
        payload = null;

        // `if rtCode > 1 then rtCode = -1` [:L141]. C-B: THE FAILURE FORM, and the child path has only
        // this one. A partial apply - the value the runtime answers when the payload and the target
        // disagree about their definitions - is a FAILURE here. A negative code is left alone, so a
        // failure is never converted into a success.
        if (rtCode > DataWindowBufferStore.DataStoreSuccess)
        {
            rtCode = DataWindowBufferStore.DataStoreFailure;
        }

        // `if rtCode = 1 then` [:L143] - EXACT equality, so neither a failure nor the cancelled zero
        // reaches anything below.
        if (rtCode != DataWindowBufferStore.DataStoreSuccess)
        {
            return rtCode;
        }

        // The child cannot be null on this arm: the only path that answers success resolved it. Stated
        // as an assertion of the control flow rather than a defensive branch, because a defensive
        // branch here would silently swallow a contradiction.
        IQueryChildSurface resolved = child
            ?? throw new InvalidOperationException(
                "The child payload reported success without a resolved child. The success arm is "
                    + "reachable only through a resolved child, so this indicates the resolver "
                    + "answered true with a null out-parameter, which its contract forbids.");

        // `dwc.ResetUpdate()` [:L144] - re-baselines the freshly delivered rows as unmodified. Not
        // presentational, so it is kept on every arm. The result is discarded, as the legacy discards it.
        _ = resolved.Store.ResetUpdate();

        // `if Len(dwc.Describe("DataWindow.Table.Filter")) > 1 then dwc.Filter()` [:L145-L147].
        // C-B: THE THRESHOLD IS ONE, NOT ZERO - a one-character answer is a describe SENTINEL ("?" for
        // not-applicable, "!" for unreadable) and not a one-character expression, so testing for
        // non-emptiness instead would re-apply a sentinel as if it were a filter. Asked of the CHILD's
        // own definition, never the parent's.
        if (resolved.Describe(ChildFilterProperty).Length > SubstantialExpressionLength)
        {
            _ = resolved.Filter();
        }

        // `if Len(dwc.Describe("DataWindow.Table.Sort")) > 1 then dwc.Sort()` [:L148-L150]. THE ORDER
        // IS FILTER THEN SORT and it is not arbitrary: sorting a set that is about to be filtered would
        // order rows that are then removed.
        if (resolved.Describe(ChildSortProperty).Length > SubstantialExpressionLength)
        {
            _ = resolved.Sort();
        }

        // `Event OnNotify(NCD_CHILDRECEIVED,0,name)` [:L151]. The column name travels in the STRING
        // argument and the numeric payload is a literal zero - per the legacy's own comment
        // `sparam:colname` at :L32, this notification carries no packed words at all.
        _ = RaiseNotify((long)SqlQueryTaskNotifyCode.ChildReceived, 0L, name);

        // `return rtCode` [:L154].
        return rtCode;
    }

    /// <summary>
    /// <c>ondatachunk(ref blob blbdata, long count, long current, boolean fullstate) returns long</c>
    /// [<c>:L13</c>, body at <c>:L180-L258</c>] - applies one chunk of the main result.
    /// </summary>
    /// <param name="payload">
    /// The chunk, carried INWARD BY REFERENCE and <b>CLEARED ON RETURN</b>. <see langword="null"/> IS
    /// the legacy's zero-length blob.
    /// </param>
    /// <param name="count">
    /// <c>count</c> - the TOTAL number of chunks. Packed as the LOW word of the notification.
    /// </param>
    /// <param name="current">
    /// <c>current</c> - this chunk's ONE-BASED index (R9). Packed as the HIGH word.
    /// </param>
    /// <param name="fullState">
    /// <c>fullstate</c> - WHICH SERIALIZATION this payload is. <b>It is not a hint</b>: the receiver
    /// cannot decode a full-state image as a changeset or the reverse, and it also selects which
    /// DIRECTION the out-of-range normalisation takes.
    /// </param>
    /// <returns>
    /// <c>0</c> when cancelled, <see cref="DataWindowBufferStore.DataStoreSuccess"/> on success,
    /// <see cref="DataWindowBufferStore.DataStoreFailure"/> on failure. <b><c>{0, 1, -1}</c>, not the
    /// return-code algebra.</b>
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>C-B - THE TWO NORMALISATIONS TAKE THE SAME INPUT TO OPPOSITE SIGNS, AND THIS IS THE SINGLE
    /// EASIEST THING IN THE FILE TO GET BACKWARDS</b> [<c>:L247-L251</c>]:
    /// </para>
    /// <code>
    /// if fullState then
    ///     if rtCode &gt; 1 then rtCode = 1      // [:L248]  -&gt; SUCCESS
    /// else
    ///     if rtCode &gt; 1 then rtCode = -1     // [:L250]  -&gt; FAILURE
    /// end if
    /// </code>
    /// <para>
    /// A partial apply is forgiven on the full-state path and fatal on the changeset path. Both arms are
    /// written out below, inline, at the position the oracle puts them; NO SHARED HELPER hides which
    /// direction applies where. A negative code is left alone by both, so a failure is never converted
    /// into a success. The child handler's failure-only form at <c>:L141</c> is the third data point.
    /// </para>
    /// <para>
    /// <b>THE THREE ARMS GENUINELY DIFFER HERE, SO ALL THREE ARE WRITTEN OUT IN FULL.</b> Two
    /// verified differences, neither cosmetic:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     <b>ONLY the DataWindow arm touches redraw and grouping.</b> It raises the debt flag and
    ///     suspends repainting before the first chunk [<c>:L193-L194</c>], and after the last chunk it
    ///     recalculates groups and restores repainting [<c>:L200</c>, <c>:L201-L204</c>]. The
    ///     structurally identical DataStore arm calls NEITHER [<c>:L218-L224</c>], and neither does the
    ///     held-carrier arm [<c>:L235-L241</c>]. Two independent sightings of that asymmetry are the
    ///     evidence that both operations are presentation (C-D).
    ///   </item>
    ///   <item>
    ///     <b>C-B - ONLY the two receiver arms have an empty-payload arm at all.</b> Both test
    ///     <c>if Len(blbData) &gt; 0</c> and, when it fails, set the result to one and reset the target
    ///     [<c>:L207-L210</c>, <c>:L226-L229</c>]. <b>The held-carrier arm at <c>:L231-L243</c> has NO
    ///     SUCH GUARD</b> - it dispatches straight on <c>fullState</c> and applies unconditionally, so an
    ///     empty payload reaches the apply and answers whatever the apply answers. That is a legacy
    ///     inconsistency, it is observable, and it is reproduced rather than harmonised. Note that
    ///     <c>ChangesetCodec.ApplyChunk</c> deliberately implements only the GUARDED form and records the
    ///     unguarded arm as not reproduced, which is exactly why the third arm is written here instead of
    ///     delegated to it.
    ///   </item>
    /// </list>
    /// </remarks>
    public long OnDataChunk(
        ref CarrierState? payload,
        long count,
        long current,
        bool fullState)
    {
        // `if of_IsCancelled() then return 0` [:L182]. Zero, and the notification at :L253 is gated on
        // exactly one, so a cancelled chunk publishes nothing.
        if (IsCancelled())
        {
            return DataWindowBufferStore.EventContinue;
        }

        long rtCode;

        // THE THREE-WAY DISPATCH. `if IsValid(_receiver) then` [:L184].
        if (_receiver is not null)
        {
            if (_receiver.Kind == QueryReceiverKind.DataWindow)
            {
                // ===== ARM (a): the DataWindow receiver [:L185-L210] - the ONLY arm with redraw and
                // group recalculation.
                ISqlDataStore target = _receiver.Store;

                // `if Len(blbData) > 0 then` [:L188]. An absent state IS the zero-length blob.
                if (payload is not null)
                {
                    if (fullState)
                    {
                        // `rtCode = dw.SetFullState(blbData)` [:L190]. ONE SHOT: no preceding reset, no
                        // chunk index, no chunk count, and no update-reset afterwards - so the item
                        // statuses carried in the image survive verbatim.
                        rtCode = FullStateCodec.Apply(target.Carrier, payload);
                    }
                    else
                    {
                        // `if current = 1 then` [:L192]. R9: ONE-BASED first chunk.
                        if (current == FirstChunkIndex)
                        {
                            // `_bRcvrRedraw = true` [:L193] - the debt is RECORDED BEFORE it is
                            // incurred, so the finalize hook can pay it even if the suspend itself is
                            // the last thing that happens.
                            _receiverRedrawSuspended = true;

                            // `dw.SetRedraw(false)` [:L194] - presentational, delegated to the receiver
                            // seam (C-D). Result discarded, as the legacy discards it.
                            _ = _receiver.SetRedraw(false);

                            // `dw.Reset()` [:L195] - gated on the FIRST chunk, so chunks after it
                            // ACCUMULATE onto the target.
                            _ = target.Carrier.Reset();
                        }

                        // `rtCode = dw.SetChanges(blbData)` [:L197].
                        rtCode = _payloadCodec.TryApply(target.Carrier, payload);

                        // `if count = current then` [:L198] - the LAST chunk.
                        if (count == current)
                        {
                            // `dw.ResetUpdate()` [:L199] - run UNCONDITIONALLY, without consulting the
                            // apply result, exactly as the legacy does not consult it.
                            _ = target.Carrier.ResetUpdate();

                            // `dw.GroupCalc()` [:L200] - presentational, DataWindow arm only.
                            _ = _receiver.GroupCalc();

                            // `if _bRcvrRedraw then _bRcvrRedraw = false : dw.SetRedraw(true)`
                            // [:L201-L204] - the debt is paid here on a completed stream, and by
                            // OnFinalize on an interrupted one.
                            if (_receiverRedrawSuspended)
                            {
                                _receiverRedrawSuspended = false;
                                _ = _receiver.SetRedraw(true);
                            }
                        }
                    }
                }
                else
                {
                    // `rtCode = 1 : dw.Reset()` [:L207-L210]. NOTE THE ORDER: the code is set FIRST and
                    // the reset second, so the result does not depend on what the reset answers.
                    rtCode = DataWindowBufferStore.DataStoreSuccess;
                    _ = target.Carrier.Reset();
                }
            }
            else
            {
                // ===== ARM (b): the DataStore receiver [:L211-L230] - structurally identical to (a)
                // and deliberately free of its two presentational calls.
                ISqlDataStore target = _receiver.Store;

                // `if Len(blbData) > 0 then` [:L214].
                if (payload is not null)
                {
                    if (fullState)
                    {
                        // `rtCode = ds.SetFullState(blbData)` [:L216].
                        rtCode = FullStateCodec.Apply(target.Carrier, payload);
                    }
                    else
                    {
                        // `if current = 1 then ds.Reset()` [:L218-L220] - NO redraw flag and NO
                        // SetRedraw on this arm.
                        if (current == FirstChunkIndex)
                        {
                            _ = target.Carrier.Reset();
                        }

                        // `rtCode = ds.SetChanges(blbData)` [:L221].
                        rtCode = _payloadCodec.TryApply(target.Carrier, payload);

                        // `if count = current then ds.ResetUpdate()` [:L222-L224] - and NOTHING ELSE.
                        // No GroupCalc, no redraw restore.
                        if (count == current)
                        {
                            _ = target.Carrier.ResetUpdate();
                        }
                    }
                }
                else
                {
                    // `rtCode = 1 : ds.Reset()` [:L226-L229].
                    rtCode = DataWindowBufferStore.DataStoreSuccess;
                    _ = target.Carrier.Reset();
                }
            }
        }
        else
        {
            // ===== ARM (c): the held carrier [:L231-L243].
            // C-B: THIS ARM HAS NO EMPTY-PAYLOAD GUARD AT ALL. The oracle dispatches straight on
            // `fullState` at :L232 with no `if Len(blbData) > 0` around it, so an EMPTY payload reaches
            // the apply and answers whatever the apply answers - where both receiver arms would have
            // short-circuited to success and reset the target. Preserved verbatim; harmonising it
            // would silently change an observable result.
            if (fullState)
            {
                // `rtCode = Data.SetFullState(blbData)` [:L233].
                rtCode = FullStateCodec.Apply(Data.Carrier, payload);
            }
            else
            {
                // `if current = 1 then Data.Reset()` [:L235-L237].
                if (current == FirstChunkIndex)
                {
                    _ = Data.Carrier.Reset();
                }

                // `rtCode = Data.SetChanges(blbData)` [:L238].
                rtCode = _payloadCodec.TryApply(Data.Carrier, payload);

                // `if count = current then Data.ResetUpdate()` [:L239-L240].
                if (count == current)
                {
                    _ = Data.Carrier.ResetUpdate();
                }
            }
        }

        // `blbData = Blob("")` [:L245] - THE EXPLICIT CLEAR, after the dispatch and before the
        // normalisation. Hazard 2's discipline.
        payload = null;

        // `if fullState then if rtCode > 1 then rtCode = 1 else if rtCode > 1 then rtCode = -1 end if`
        // [:L247-L251]. C-B: BOTH ARMS, IN OPPOSITE DIRECTIONS, written out at the oracle's own
        // position. Deliberately NOT extracted into a helper that takes the direction as a parameter -
        // such a helper would hide which direction applies where, and that is the mistake this pairing
        // exists to prevent.
        if (fullState)
        {
            // [:L248] -> SUCCESS. A partial apply is FORGIVEN on the full-state path.
            // CONSUMED, not re-derived: FullStateCodec.NormalizeResult publishes exactly this rule, and
            // its own documentation gives the reason it exists as a member at all - the value being
            // normalised comes from the PowerBuilder runtime's SetFullState and this port cannot
            // manufacture every code that runtime could answer, so the rule has to be assertable
            // directly rather than only through whatever the managed apply happens to return.
            rtCode = FullStateCodec.NormalizeResult(rtCode);
        }
        else
        {
            // [:L250] -> FAILURE. The SAME input is FATAL on the changeset path. There is no published
            // counterpart to consume - Buffers/ChangesetCodec.cs bakes this rule into ApplyChunk rather
            // than exposing it - so it is named here, for the same assertability reason.
            rtCode = NormalizeChangesetChunkResult(rtCode);
        }

        // `if rtCode = 1 then Event OnNotify(NCD_DATACHUNK,MakeLong(count,current),"")` [:L253-L255].
        // EXACT equality with one, so neither a failure nor the cancelled zero publishes anything.
        if (rtCode == DataWindowBufferStore.DataStoreSuccess)
        {
            // MakeLong's FIRST argument is the LOW word: `count` low, `current` high, matching the
            // legacy's own comment `lparam:(low word:count,high word:current)` at :L31.
            _ = RaiseNotify(
                (long)SqlQueryTaskNotifyCode.DataChunk,
                PackWords(count, current),
                string.Empty);
        }

        // `return rtCode` [:L257].
        return rtCode;
    }

    /// <summary>
    /// <c>oncreatedata(readonly string syntax)</c> [<c>:L11</c>, body at <c>:L157-L172</c>] - asks the
    /// receiving side to build itself from a transferred DataWindow syntax.
    /// </summary>
    /// <param name="syntax">The syntax the worker derived from its own definition, verbatim.</param>
    /// <exception cref="ArgumentNullException"><paramref name="syntax"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>THE LEGACY EVENT HAS NO RETURN TYPE, SO NOTHING HERE CAN VETO OR REPORT.</b> All three arms
    /// call <c>Create(syntax)</c> and discard its result [<c>:L163</c>, <c>:L167</c>, <c>:L170</c>], and
    /// the worker reacts not to this call's outcome but to the cancellation check it performs
    /// immediately afterwards [<c>n_cst_thread_task_sqlquery.sru:L105</c>]. Reproduced as a
    /// <see langword="void"/> whose one discarded result is annotated rather than silently dropped.
    /// </para>
    /// <para>
    /// The three arms differ ONLY in which object they build - no presentational call and no result
    /// inspection appears on any of them - so the dispatch is three visible target selections over one
    /// shared call, verified line by line against <c>:L159-L171</c>.
    /// </para>
    /// <para>
    /// <c>readonly string</c> maps to <c>in</c> under the plan's type mapping, but a
    /// <see cref="string"/> is already a reference and <c>in string</c> would add an indirection for no
    /// semantic gain, so the parameter is passed by value. The IMMUTABILITY the legacy modifier
    /// promises is what matters and it holds either way.
    /// </para>
    /// </remarks>
    public void OnCreateData(string syntax)
    {
        ArgumentNullException.ThrowIfNull(syntax);

        // `if of_IsCancelled() then return` [:L157]. A void early-out: no value, nothing published.
        if (IsCancelled())
        {
            return;
        }

        // THE THREE-WAY DISPATCH. `if IsValid(_receiver) then` [:L159]; ARM (a) at :L160-L163 and ARM
        // (b) at :L164-L167 differ only in the cast, ARM (c) at :L169-L170 targets the held carrier.
        ISqlDataStore target = _receiver?.Store ?? Data;

        // `dw.Create(syntax)` [:L163] / `ds.Create(syntax)` [:L167] / `Data.Create(syntax)` [:L170].
        // The outcome carries a result and a diagnostic text and BOTH ARE DISCARDED, because the legacy
        // discards them - see the remarks.
        _ = _dataWindowRuntime.CreateFromSyntax(target, syntax);
    }

    /// <summary>
    /// <c>ondatareceived(long rowcount)</c> [<c>:L12</c>, body at <c>:L174-L178</c>] - latches the row
    /// count of the WHOLE result and publishes it.
    /// </summary>
    /// <param name="rowCount">The row count the retrieval reported.</param>
    /// <remarks>
    /// <para>
    /// <b>IT IS THE FIRST THING THE WORKER'S DATA EVENT DOES</b>
    /// [<c>n_cst_thread_task_sqlquery.sru:L83</c>] - before the hand-over predicate, before the codec
    /// selection and before any chunk. A consumer therefore learns the total before it sees a single
    /// row, which is exactly what contract C-05's stream shape publishes as its row-count arm.
    /// </para>
    /// <para>
    /// <b>The notification carries the count RAW, with NO word packing</b> - per the legacy's own
    /// comment <c>lparam:row count</c> at <c>:L29</c>. The legacy publishes the FIELD it has just
    /// assigned rather than the parameter [<c>:L177</c>]; identical in value, and reproduced as written
    /// so the read-after-write is visible.
    /// </para>
    /// </remarks>
    public void OnDataReceived(long rowCount)
    {
        // `if of_IsCancelled() then return` [:L174].
        if (IsCancelled())
        {
            return;
        }

        // `_nRowCount = rowCount` [:L176].
        _rowCount = rowCount;

        // `Event OnNotify(NCD_DATARECEIVED,_nRowCount,"")` [:L177] - the FIELD, and no packing.
        _ = RaiseNotify((long)SqlQueryTaskNotifyCode.DataReceived, _rowCount, string.Empty);
    }

    /// <summary>
    /// <c>onpagereceived(long pagecount, long recordcount)</c> [<c>:L14</c>, body at
    /// <c>:L260-L266</c>] - latches the paging totals and publishes them as one packed value.
    /// </summary>
    /// <param name="pageCount">
    /// Total pages, or the legacy's <c>-1</c> when counting did not happen.
    /// </param>
    /// <param name="recordCount">
    /// Total records, or <c>-1</c> when counting did not happen.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>IT FIRES ON EVERY PATH, COUNTED OR NOT</b> - the worker's raise at
    /// <c>n_cst_thread_task_sqlquery.sru:L865</c> sits OUTSIDE its counting conditional - and the
    /// not-counted state is carried as the real value <c>-1</c> rather than as an absence. A consumer
    /// must treat <c>-1</c> as "not counted" and must not do arithmetic on it.
    /// </para>
    /// <para>
    /// <b>THE TWO TOTALS ARE PACKED INTO ONE NOTIFICATION, page count LOW and record count HIGH</b>
    /// [<c>:L265</c>], per the legacy's own comment <c>lparam:(low word:page count,high word:record
    /// count)</c> at <c>:L30</c>. The packing truncates each to sixteen bits, which is a real legacy
    /// limit rather than a port artefact - see <see cref="PackWords"/>. The UNTRUNCATED values remain
    /// readable through <see cref="GetPageCount"/> and <see cref="GetRecordCount"/>, which is why the
    /// truncation costs a consumer nothing.
    /// </para>
    /// </remarks>
    public void OnPageReceived(long pageCount, long recordCount)
    {
        // `if of_IsCancelled() then return` [:L260].
        if (IsCancelled())
        {
            return;
        }

        // `_nPageCount = pageCount` [:L262].
        _pageCount = pageCount;

        // `_nRecordCount = recordCount` [:L263] - the field Reset() deliberately leaves alone.
        _recordCount = recordCount;

        // `Event OnNotify(NCD_PAGERECEIVED,MakeLong(_nPageCount,_nRecordCount),"")` [:L265] - the
        // FIELDS, not the parameters, and page count is the LOW word.
        _ = RaiseNotify(
            (long)SqlQueryTaskNotifyCode.PageReceived,
            PackWords(_pageCount, _recordCount),
            string.Empty);
    }

    /// <summary>
    /// <c>ondatamove(datastore ds)</c> [<c>:L15</c>, body at <c>:L268-L271</c>] - takes OWNERSHIP of a
    /// carrier the worker has relinquished, replacing the one this proxy holds.
    /// </summary>
    /// <param name="carrier">
    /// The carrier being handed over. This proxy owns it from this point;
    /// <c>DataWindowCarrierOwnership.Move</c> has already nulled the sender's reference, so exactly one
    /// party holds it.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="carrier"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>THIS IS THE MAIN-THREAD FAST PATH AND NO CODEC RUNS ON IT AT ALL.</b> The worker reaches it
    /// only when it is on the main thread, no receiver is installed and caching is off
    /// [<c>DataWindowCarrierOwnership.CanMoveWithoutSerialization</c>], in which case the carrier and
    /// its consumer share an address space and there is nothing to serialize. It is the behavioural
    /// payoff of the two-carrier affinity split.
    /// </para>
    /// <para>
    /// <b>THE ORDER IS CONTRACT AND IT IS DESTROY-THEN-ADOPT</b> [<c>:L268-L269</c>]:
    /// <c>Destroy Data</c> comes FIRST and <c>Data = ds</c> SECOND. Adopting first and destroying
    /// afterwards would destroy the carrier just adopted, because both names would refer to the same
    /// object. Reproduced as deterministic destruction of the outgoing carrier followed by adoption of
    /// the incoming one.
    /// </para>
    /// <para>
    /// <b>C-B - THIS IS THE ONE PAYLOAD HANDLER THAT DOES NOT CHECK CANCELLATION.</b> Every other one
    /// opens with <c>if of_IsCancelled() then return</c> - at <c>:L96</c>, <c>:L157</c>, <c>:L174</c>,
    /// <c>:L182</c> and <c>:L260</c> - and this one has NO SUCH CHECK. It looks like an oversight and
    /// it is behaviour: the carrier has ALREADY been relinquished by the time this runs, so declining
    /// to adopt it would leak it with no owner at all. Reproduced verbatim, and pinned by a test that
    /// signals cancellation and asserts the adoption still happens.
    /// </para>
    /// <para>
    /// <b>C-B - IT REUSES THE DATA-CHUNK NOTIFY CODE RATHER THAN OWNING ONE.</b> The raise at
    /// <c>:L270</c> is <c>NCD_DATACHUNK</c> (4) and not a dedicated code, packing
    /// <c>MakeLong(1,1)</c> - chunk one of one, because the hand-over delivers the whole result at
    /// once. A consumer subscribed to the chunk notification therefore sees a hand-over as a
    /// single-chunk stream, which is precisely what makes the fast path transparent to it. The two
    /// packed values are taken from <c>FullStateCodec</c>'s published single-chunk constants, which the
    /// send side uses for the same reason.
    /// </para>
    /// </remarks>
    public void OnDataMove(DataWindowCarrier carrier)
    {
        ArgumentNullException.ThrowIfNull(carrier);

        // C-B: NO CANCELLATION CHECK HERE, deliberately - see the remarks. Every other payload handler
        // has one.

        // `Destroy Data` [:L268] - FIRST. Deterministic teardown of the outgoing carrier, never left to
        // the collector (hazard 2).
        DestroyCarrier(Data);

        // `Data = ds` [:L269] - SECOND. The already-populated carrier is ADOPTED rather than created,
        // which is the one thing the affinity-keyed factory cannot do.
        Data = RequireCarrier(_carrierAdopter.Adopt(carrier), nameof(IQueryCarrierAdopter));

        // `Event OnNotify(NCD_DATACHUNK,MakeLong(1,1),"")` [:L270] - the CHUNK code, not a dedicated
        // one, carrying chunk one of one.
        _ = RaiseNotify(
            (long)SqlQueryTaskNotifyCode.DataChunk,
            PackWords(FullStateCodec.SingleChunkCount, FullStateCodec.SingleChunkIndex),
            string.Empty);
    }

    #endregion


    #region The worker-facing sink surface - the pair boundary, made explicit

    /// <summary>
    /// <see cref="IChangesetTransferSink.CreateDataAsync"/> - the awaitable projection of
    /// <see cref="OnCreateData"/>.
    /// </summary>
    /// <param name="syntax">The source's generated syntax, verbatim.</param>
    /// <param name="cancellationToken">
    /// The worker's token. <b>Not consulted here</b>: the legacy event tests
    /// <c>of_IsCancelled()</c> against the TASK's own cancellation state [<c>:L157</c>], which the
    /// inherited <see cref="SqlTaskProxyBase.IsCancelled"/> reads from the substrate, and adding a
    /// second source of truth would let the two disagree.
    /// </param>
    /// <returns>
    /// <c>0</c> always. The legacy event is declared with no return type [<c>:L11</c>] so it cannot
    /// answer anything, and the send side ignores the result besides.
    /// </returns>
    /// <remarks>
    /// <b>WHY THE THREE HANDOVER MEMBERS ARE AWAITABLE AND THE OTHERS ARE NOT.</b> These three cross
    /// what used to be an in-process call and is now a network edge, so they can fail in transit in a
    /// way their originals could not and must be awaitable. Nothing is actually awaited HERE - this
    /// side of the boundary is the destination, not the sender - which is why each returns a completed
    /// <see cref="ValueTask{TResult}"/> rather than pretending to do asynchronous work.
    /// </remarks>
    public ValueTask<long> CreateDataAsync(string syntax, CancellationToken cancellationToken)
    {
        OnCreateData(syntax);

        return ValueTask.FromResult(DataWindowBufferStore.EventContinue);
    }

    /// <summary>
    /// <see cref="IChangesetTransferSink.SendChunkAsync"/> - the CHANGESET arm of
    /// <see cref="OnDataChunk"/>.
    /// </summary>
    /// <param name="chunk">
    /// The chunk. Its <c>FullState</c> is fixed at <see langword="false"/> by
    /// <c>ChangesetChunk.ChangesetSelector</c>, so this member can only ever take the changeset arm -
    /// misrouting is prevented by the type system rather than by a runtime check.
    /// </param>
    /// <param name="cancellationToken">The worker's token; see <see cref="CreateDataAsync"/>.</param>
    /// <returns>The handler's <c>{0, 1, -1}</c> answer, unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="chunk"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <b>THE PAYLOAD IS UNWRAPPED INTO A LOCAL AND PASSED BY REFERENCE, WHICH IS THE WHOLE POINT.</b>
    /// The handler clears the buffer on the way out [<c>:L245</c>], and it can only do that through a
    /// <c>ref</c>. Unwrapping here rather than declaring the interface with a <c>ref</c> parameter keeps
    /// the published send-side contract - which is shared with the codec - free of a parameter mode that
    /// only the receive side needs. <b>Nothing returns a buffer anywhere on this path</b>, which is
    /// hazard 1's requirement.
    /// </remarks>
    public ValueTask<long> SendChunkAsync(ChangesetChunk chunk, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        CarrierState? payload = chunk.State;

        long result = OnDataChunk(
            ref payload,
            chunk.ChunkCount,
            chunk.ChunkIndex,
            chunk.FullState);

        return ValueTask.FromResult(result);
    }

    /// <summary>
    /// <see cref="IChangesetTransferSink.SendChildChunkAsync"/> - the awaitable projection of
    /// <see cref="OnChildDataReceived"/>.
    /// </summary>
    /// <param name="payload">The child payload, carrying its column name.</param>
    /// <param name="cancellationToken">The worker's token; see <see cref="CreateDataAsync"/>.</param>
    /// <returns>The handler's <c>{0, 1, -1}</c> answer, unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="payload"/> is <see langword="null"/>.</exception>
    public ValueTask<long> SendChildChunkAsync(
        ChangesetChildPayload payload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);

        CarrierState? state = payload.State;

        long result = OnChildDataReceived(payload.ColumnName, ref state);

        return ValueTask.FromResult(result);
    }

    /// <summary>
    /// <see cref="IQueryResultSink.SendFullStateChunk"/> - the FULL-STATE arm of
    /// <see cref="OnDataChunk"/>.
    /// </summary>
    /// <param name="chunk">
    /// The single chunk, carrying <c>FullStateCodec.SingleChunkCount</c>,
    /// <c>FullStateCodec.SingleChunkIndex</c> and <c>FullState = true</c>.
    /// </param>
    /// <returns>The handler's <c>{0, 1, -1}</c> answer, unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="chunk"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>SYNCHRONOUS, AND SEPARATE FROM <see cref="SendChunkAsync"/>, BECAUSE THE TWO ARMS ARE NOT
    /// INTERCHANGEABLE.</b> The full-state path has no loop, no yield and no cancellation point of its
    /// own, and the receiving side cannot decode a full-state image as a changeset - which is exactly
    /// what the <c>fullstate</c> argument exists to say. The signature matches
    /// <c>FullStateChunkHandover</c> so the sink can be handed straight to <c>FullStateCodec.Send</c>
    /// with no adapter.
    /// </para>
    /// <para>
    /// <b>The chunk's own <c>FullState</c> is forwarded rather than a hard <see langword="true"/></b>,
    /// so the arm this takes is decided by the payload rather than by the call site. A producer that
    /// set the flag wrongly would then get the normalisation direction its flag names - which is the
    /// legacy's behaviour too, since the legacy branches on the argument it was passed.
    /// </para>
    /// </remarks>
    public long SendFullStateChunk(QueryDataChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        CarrierState? payload = chunk.State;

        return OnDataChunk(ref payload, chunk.ChunkCount, chunk.ChunkIndex, chunk.FullState);
    }

    /// <summary>
    /// <see cref="IChangesetTransferSink.ReportError"/> - publishes a transfer fault the way the legacy
    /// publishes one.
    /// </summary>
    /// <param name="returnCode">
    /// The framework return code, which on every path the codecs take is
    /// <c>RetCode.E_INTERNAL_ERROR</c>.
    /// </param>
    /// <param name="errorText">
    /// The error text VERBATIM. <b>C-F: the four texts that reach here carry NO statement and NO
    /// credential</b> - they are <c>GetChanges Failed</c>, <c>TransData Failed</c> and their
    /// per-column forms, produced by <c>Buffers/ChangesetCodec.cs</c> and
    /// <c>Buffers/FullStateCodec.cs</c> - so no redaction applies on this path. The statement-bearing
    /// payload travels the OTHER route entirely: the base latches it as a
    /// <c>DbErrorData</c> and its single sanctioned wire projection masks the statement
    /// unconditionally.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="errorText"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>IT GOES TO THE FRAMEWORK'S ERROR CHANNEL AND NOT TO THE DATABASE-ERROR LATCH.</b> The legacy
    /// raise is <c>Event OnError(RetCode.E_INTERNAL_ERROR, ...)</c> on the task object - a
    /// <c>TNR_ERROR</c> notification - which is a DIFFERENT channel from <c>ondberror</c>, the one that
    /// assigns the latched <c>dberrordata</c> [<c>n_cst_threading_task_sqlbase.sru:L44</c>]. Conflating
    /// them would make a serialization fault look like a database fault and would overwrite a real
    /// database error with a codec diagnostic.
    /// </para>
    /// <para>
    /// The result is discarded and nothing is thrown: the legacy raises the event and ignores whatever
    /// comes back, because the code it is about to return is already decided.
    /// </para>
    /// </remarks>
    public void ReportError(long returnCode, string errorText)
    {
        ArgumentNullException.ThrowIfNull(errorText);

        // `Event OnError(rtCode, text)` - the TNR_ERROR reason, carrying the code and the text and no
        // second numeric argument. The base's dispatcher declines to fire it while cancelled, which is
        // the framework's own behaviour and not this method's business.
        _ = SendNotify(Enums.TNR_ERROR, returnCode, 0L, errorText);
    }

    #endregion

    #region Teardown - `destructor;call super::destructor;Destroy Data` [:L515]

    /// <summary>
    /// Destroys the held carrier and then tears the base down.
    /// </summary>
    /// <param name="disposing">
    /// <see langword="true"/> on an explicit dispose. There is no finalizer, so the
    /// <see langword="false"/> path never runs for this type; the parameter exists because the base
    /// declares the pattern.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>DETERMINISTIC, NEVER COLLECTOR-DEPENDENT - HAZARD 2 APPLIED TO AN OBJECT RATHER THAN A
    /// BUFFER.</b> The legacy destructor's whole body is <c>Destroy Data</c> [<c>:L515</c>], paired with
    /// the constructor's <c>Data = Create DataStore</c> [<c>:L518</c>]. The pairing is what makes the
    /// carrier's lifetime exactly the proxy's, and <c>docs/PB多线程绕坑提示.md:L5</c> is the general
    /// form of the same rule: release a cross-boundary reference explicitly rather than waiting.
    /// </para>
    /// <para>
    /// <b>IT DESTROYS THE CARRIER IT IS HOLDING AT THAT MOMENT, WHICH IS THE ONLY CORRECT CHOICE GIVEN
    /// THE TWO OWNERSHIP TRANSFERS.</b> <see cref="MoveData"/> hands a carrier OUT and leaves a fresh
    /// one behind, and <see cref="OnDataMove"/> destroys the outgoing one and adopts an incoming one.
    /// After either, the carrier this proxy holds is not the one it was constructed with - so
    /// destroying "the original" would be a double free of an object another party now owns, and
    /// destroying nothing would leak the replacement. Exactly one destroy of exactly the current
    /// carrier is what both transfers leave correct.
    /// </para>
    /// <para>
    /// <b>Idempotent by an explicit latch.</b> The base has its own private latch and cannot expose it,
    /// so this type keeps one of its own; a second dispose does nothing, and in particular does not
    /// destroy a second carrier. The receiver reference is dropped too - this proxy never owned the
    /// receiver, so it is released and NOT destroyed, and the distinction is the same one
    /// <see cref="SetReceiver"/> observes.
    /// </para>
    /// </remarks>
    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            // Still forwarded, because the base's own latch is what makes ITS second call harmless and
            // this method must not decide that on its behalf.
            base.Dispose(disposing);
            return;
        }

        if (disposing)
        {
            // `Destroy Data` [:L515] - the carrier held RIGHT NOW, exactly once.
            DestroyCarrier(Data);

            // Hazard 2: the cross-boundary reference this proxy borrowed but never owned is released,
            // not destroyed.
            _receiver = null;
        }

        // Set before the base call, so a re-entrant path through the base observes a fully torn-down
        // derived state rather than a half-torn-down one.
        _disposed = true;

        // `call super::destructor` [:L515] - the ancestor runs AFTER this type's own teardown, which is
        // the order PowerBuilder's destructor chain uses.
        base.Dispose(disposing);
    }

    #endregion

    #region Private helpers - each one a named legacy mechanism, none of them new behaviour

    /// <summary>
    /// <c>_of_GetTask()</c> [<c>:L322-L323</c>] - the worker, narrowed to its concrete type.
    /// </summary>
    /// <returns>The attached <see cref="SqlQueryTask"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// No worker is attached, or the attached worker is not a query task.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>PRIVATE, and that is the legacy's own choice</b> - the prototype at <c>:L62</c> reads
    /// <c>private function n_cst_thread_task_sqlquery _of_gettask ()</c>, so unlike
    /// <see cref="HasReceiver"/> and <see cref="NeedsCreatedObject"/> this member's underscore prefix
    /// AGREES with its visibility. Recorded because the contrast is what shows the other two are
    /// anomalies rather than a house style.
    /// </para>
    /// <para>
    /// <b>It throws rather than answering a code, and that is fail-fast rather than defensiveness.</b>
    /// The legacy body is a bare <c>return _Task</c> over a typed field the framework has already
    /// populated, so a null or wrongly-typed worker is not a runtime condition the oracle has an answer
    /// for - it is a composition fault, and the legacy's posture on a structural fault is to terminate
    /// rather than degrade. The message names the remedy.
    /// </para>
    /// </remarks>
    private SqlQueryTask RequireQueryTask() =>
        GetWorkerTask<SqlQueryTask>()
        ?? throw new InvalidOperationException(
            $"The '{TaskType}' task proxy has no '{WorkerTaskClassName}' worker attached. OnInit must "
            + "succeed before any setter is used, the attached worker must be a SqlQueryTask, and the "
            + "proxy must not be used after disposal.");

    /// <summary>
    /// The body all three arms of <see cref="OnChildDataReceived"/> share, because the oracle's three
    /// copies are character-identical apart from the object they cast to.
    /// </summary>
    /// <param name="target">
    /// The object to resolve the child against - the receiver's store on arms (a) and (b), the held
    /// carrier on arm (c).
    /// </param>
    /// <param name="name">The column name.</param>
    /// <param name="payload">
    /// The child's changeset, by reference for symmetry with the caller. <b>Not cleared here</b>: the
    /// legacy clears it ONCE, after the dispatch [<c>:L139</c>], so clearing inside an arm would move
    /// an observable step.
    /// </param>
    /// <param name="child">The resolved child, or <see langword="null"/> when resolution failed.</param>
    /// <returns>
    /// <see cref="DataWindowBufferStore.DataStoreSuccess"/> on an empty payload,
    /// <see cref="DataWindowBufferStore.DataStoreFailure"/> when the child could not be resolved, and
    /// otherwise whatever the apply answered - UN-NORMALISED, because the normalisation belongs to the
    /// caller's own position at <c>:L141</c>.
    /// </returns>
    private long ApplyChildPayload(
        ISqlDataStore target,
        string name,
        ref CarrierState? payload,
        out IQueryChildSurface? child)
    {
        // `if dw.GetChild(name, ref dwc) = 1 then` [:L102, :L115, :L127].
        if (!_childResolver.TryGetChild(target, name, out child))
        {
            // `else rtCode = -1` [:L109-L110, :L122-L123, :L134-L135]. NOTE THE CONTRAST WITH THE SEND
            // SIDE: there, an unresolvable child is a column to SKIP; here it is a FAILURE.
            return DataWindowBufferStore.DataStoreFailure;
        }

        IQueryChildSurface resolved = child
            ?? throw new InvalidOperationException(
                "The child resolver answered true with a null child. Its contract requires a non-null "
                    + "child on a true answer.");

        // `dwc.Reset()` [:L103, :L116, :L128] - UNCONDITIONAL and BEFORE the payload is inspected, so an
        // empty payload still clears the child. Result discarded, as the legacy discards it.
        _ = resolved.Store.Reset();

        // `if Len(blbData) > 0 then rtCode = dwc.SetChanges(blbData) else rtCode = 1`
        // [:L104-L108, :L117-L121, :L129-L133]. An absent state IS the zero-length blob.
        return payload is null
            ? DataWindowBufferStore.DataStoreSuccess
            : _payloadCodec.TryApply(resolved.Store, payload);
    }

    /// <summary>
    /// The CHANGESET arm of the chunk-result normalisation - <c>if rtCode &gt; 1 then rtCode = -1</c>
    /// [<c>:L250</c>].
    /// </summary>
    /// <param name="resultCode">The raw result of the apply or of the empty-payload arm.</param>
    /// <returns>
    /// <see cref="DataWindowBufferStore.DataStoreFailure"/> when <paramref name="resultCode"/> is above
    /// one; otherwise <paramref name="resultCode"/> unchanged.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>THIS MEMBER ENCODES ONE DIRECTION AND ITS NAME SAYS WHICH.</b> It is deliberately NOT a
    /// shared helper that takes the direction as an argument: such a helper would hide which direction
    /// applies where, and the full-state arm's OPPOSITE rule
    /// (<c>FullStateCodec.NormalizeResult</c>, <c>&gt;1 -&gt; 1</c> at <c>:L248</c>) is the single
    /// easiest thing in this file to get backwards. The two are invoked side by side in the
    /// <c>if fullState ... else ...</c> of <see cref="OnDataChunk"/>, at the position the oracle puts
    /// them, each with its own locator.
    /// </para>
    /// <para>
    /// <b>A NEGATIVE CODE IS LEFT ALONE</b>, so a failure is never converted into a success. Only the
    /// above-one band moves - the value PowerBuilder answers for a partial apply, where the payload and
    /// the target disagree about their definitions. Reporting a definition mismatch as a clean transfer
    /// is exactly the regression no row-count assertion would notice, which is why the rule is named
    /// and asserted directly rather than inferred from what the managed apply currently returns.
    /// </para>
    /// <para>
    /// The child handler's identical rule at <c>:L141</c> is written out INLINE rather than routed here,
    /// on purpose: the child path has no lenient sibling to pair against, so it is a third distinct
    /// site in the oracle and is annotated as one.
    /// </para>
    /// </remarks>
    internal static long NormalizeChangesetChunkResult(long resultCode) =>
        resultCode > DataWindowBufferStore.DataStoreSuccess
            ? DataWindowBufferStore.DataStoreFailure
            : resultCode;

    /// <summary>
    /// <c>MakeLong(low, high)</c> [<c>ws_objects/pfw.common.pbl.src/makelong.srf:L7</c>] - packs two
    /// counters into one notification payload.
    /// </summary>
    /// <param name="low">The value that becomes the LOW word.</param>
    /// <param name="high">The value that becomes the HIGH word.</param>
    /// <returns>The packed value, widened to the notification's <see cref="long"/> payload.</returns>
    /// <remarks>
    /// <para>
    /// <b>THE FIRST ARGUMENT IS THE LOW WORD.</b> The legacy prototype is
    /// <c>global function ulong makelong(readonly uint low, readonly uint high)</c>, and all three call
    /// sites in this file rely on that order: <c>MakeLong(count, current)</c> [<c>:L254</c>],
    /// <c>MakeLong(_nPageCount, _nRecordCount)</c> [<c>:L265</c>] and <c>MakeLong(1, 1)</c>
    /// [<c>:L270</c>]. Getting the order wrong produces notifications that decode to
    /// plausible-but-wrong numbers - precisely the class of regression a unit test catches and a smoke
    /// test does not, which is why all three sites are asserted by round-tripping through the unpack.
    /// </para>
    /// <para>
    /// <b>THE PACKING IS TAKEN FROM <c>Bits</c> AND IS NOT REIMPLEMENTED WITH SHIFTS.</b> Its signature
    /// is deliberately FIXED-WIDTH - <c>MakeLong(ushort low, ushort high)</c> answering
    /// <see cref="uint"/> - because <b>PowerBuilder's <c>uint</c> is SIXTEEN bits and its <c>ulong</c>
    /// is THIRTY-TWO, which are NOT C#'s same-named wider types.</b> Passing a <see cref="long"/> and
    /// letting it widen would silently pack the wrong bits.
    /// </para>
    /// <para>
    /// <b>THE MATCHING UNPACK IS <c>Bits.LoWord</c> AND <c>Bits.HiWord</c> from the same class</b>, so a
    /// consumer reading a chunk notification recovers <c>count</c> with <c>LoWord</c> and
    /// <c>current</c> with <c>HiWord</c>. The pairing is stated here so it is discoverable from either
    /// end.
    /// </para>
    /// </remarks>
    private static long PackWords(long low, long high) =>
        Bits.MakeLong(ToLegacyWord(low), ToLegacyWord(high));

    /// <summary>
    /// Narrows a counter to the sixteen bits PowerBuilder's <c>uint</c> actually holds.
    /// </summary>
    /// <param name="value">The counter.</param>
    /// <returns>The low sixteen bits, as <c>MakeLong</c>'s operand type.</returns>
    /// <remarks>
    /// <para>
    /// <b>THE TRUNCATION IS THE LEGACY'S OWN AND IS NOT INTRODUCED HERE.</b> A PowerBuilder
    /// <c>long</c> is thirty-two bits and a <c>uint</c> is sixteen, so passing a counter above 65535 to
    /// <c>MakeLong</c> has always discarded the high half. The conversion is written explicitly, and
    /// <c>unchecked</c> is stated rather than relied upon as the compiler's default, so a reader can see
    /// that wrapping is intended rather than accidental.
    /// </para>
    /// <para>
    /// <b>A NEGATIVE COUNTER WRAPS RATHER THAN THROWING, WHICH IS ALSO THE LEGACY'S BEHAVIOUR AND IS
    /// REACHABLE IN PRACTICE.</b> The paging totals are <c>-1</c> when counting did not happen
    /// [<c>n_cst_thread_task_sqlquery.sru:L865</c>], and PowerBuilder's assignment of <c>-1</c> into a
    /// sixteen-bit unsigned is the two's-complement low half, 65535 - exactly what this conversion
    /// produces. Rejecting the value instead would fail a notification the legacy publishes
    /// successfully. <b>The untruncated totals stay readable through <see cref="GetPageCount"/> and
    /// <see cref="GetRecordCount"/>, so nothing is lost.</b>
    /// </para>
    /// </remarks>
    private static ushort ToLegacyWord(long value) => unchecked((ushort)value);

    /// <summary>
    /// <c>Destroy Data</c> [<c>:L268</c>, <c>:L515</c>] - deterministic teardown of one carrier.
    /// </summary>
    /// <param name="store">The carrier to destroy.</param>
    /// <remarks>
    /// <para>
    /// Two steps, in this order. The buffers are cleared FIRST, which releases the rows and the parent
    /// references the carrier holds - the direct analogue of hazard 2's
    /// <c>SetNull</c> discipline - and the store is then disposed if it holds anything disposable.
    /// </para>
    /// <para>
    /// <b>The disposal is conditional because <c>ISqlDataStore</c> does not require it.</b> Neither
    /// <c>DataWindowBufferStore</c> nor <c>DataWindowCarrier</c> is
    /// <see cref="IDisposable"/>, so clearing IS the managed equivalent of <c>Destroy</c> for the
    /// stores this service ships; the pattern-matched dispose is what makes an implementation that DOES
    /// hold an unmanaged handle tear down correctly too, without widening a shared interface for one
    /// caller.
    /// </para>
    /// <para>
    /// <b>The parameter is NON-NULLABLE AND CARRIES NO NULL GUARD, deliberately.</b>
    /// <see cref="Data"/> is created in the constructor and only ever replaced by a non-null store, so
    /// a null here is not a state this type can reach - and a guard against an unreachable state is
    /// dead code that advertises a condition the code does not have. The obligation is enforced where
    /// it is real instead: <see cref="RequireCarrier"/> validates every store a seam hands back, at the
    /// moment it hands it back.
    /// </para>
    /// </remarks>
    private static void DestroyCarrier(ISqlDataStore store)
    {
        // Hazard 2: the rows and the parent references go first, explicitly.
        store.ClearState();

        if (store is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    /// <summary>
    /// Validates a result carrier a seam has just handed back, so that a null never reaches
    /// <see cref="Data"/>.
    /// </summary>
    /// <param name="carrier">Whatever the seam answered.</param>
    /// <param name="seam">The seam's name, for the diagnostic.</param>
    /// <returns><paramref name="carrier"/>, guaranteed non-null.</returns>
    /// <exception cref="InvalidOperationException">The seam answered <see langword="null"/>.</exception>
    /// <remarks>
    /// <b>FAIL-FAST ON A STRUCTURAL FAULT, WHICH IS THE LEGACY POSTURE.</b> <c>Create DataStore</c>
    /// [<c>:L419</c>, <c>:L518</c>] cannot fail in PowerBuilder, so there is no legacy code path for a
    /// missing carrier and inventing a graceful one would be softening a fault into a warning. The three
    /// call sites are the constructor, <see cref="MoveData"/> and <see cref="OnDataMove"/> - every place
    /// a seam's answer becomes <see cref="Data"/> - which is what lets <see cref="DestroyCarrier"/> take
    /// a non-nullable argument and carry no dead guard of its own.
    /// </remarks>
    private static ISqlDataStore RequireCarrier(ISqlDataStore? carrier, string seam) =>
        carrier
        ?? throw new InvalidOperationException(
            $"'{seam}' answered no result carrier. The legacy 'Create DataStore' cannot fail, so this "
            + "proxy has no state in which its held carrier is absent and no behaviour to degrade "
            + "into.");

    /// <summary>
    /// The four configurable properties, restored to their configured defaults - the body shared by the
    /// constructor's field initializers [<c>:L37-L40</c>] and the reset's four assignments
    /// [<c>:L287-L290</c>].
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>ONE BODY, TWO CALLERS, WHICH IS A LEGACY PROPERTY AND NOT AN OPTIMISATION.</b> A freshly
    /// constructed proxy and a freshly reset one are in the same state in the oracle too, because its
    /// initializers and its reset assign the identical four values. Two parallel bodies here could
    /// drift and the oracle's could not.
    /// </para>
    /// <para>
    /// <b>THE VALUES COME FROM CONFIGURATION AND THE DEFAULTS ARE BIT-IDENTICAL TO THE LEGACY
    /// LITERALS.</b> The legacy hardcodes <c>false</c>, <c>0</c>, <c>0</c> and <c>true</c>;
    /// <c>QueryOptions</c> declares <c>Paged = false</c>, <c>PageSize = 0</c>, <c>PageIndex = 0</c> and
    /// <c>PageCounting = true</c>. So at default configuration this method assigns exactly what the
    /// oracle assigns, and the AAP's requirement that no value the legacy hardcodes be carried forward
    /// as a literal is discharged structurally. <b>Stated honestly: a NON-default configuration makes
    /// the reset restore the configured value rather than the legacy literal.</b> That is the
    /// unavoidable consequence of having a configuration seam at all - construction would diverge
    /// either way - and it is the same treatment <c>Tasks/SqlQueryTask.cs</c> applies to the worker's
    /// own defaults, so both halves of the pair behave alike.
    /// </para>
    /// <para>
    /// <b>The worker is NOT notified.</b> This assigns local properties only, exactly as
    /// <c>:L287-L290</c> does; the worker's own state was already reset by the base's delegation
    /// [<c>n_cst_threading_task_sqlbase.sru:L61</c>] before this method is reached.
    /// </para>
    /// </remarks>
    private void ApplyConfiguredQueryDefaults()
    {
        // `#PageIndex = 0` [:L287]. R9: zero is "no page selected" in a one-based scheme, not "the
        // first page".
        PageIndex = _configuredDefaults.PageIndex;

        // `#Paged = false` [:L288].
        Paged = _configuredDefaults.Paged;

        // `#PageSize = 0` [:L289].
        PageSize = _configuredDefaults.PageSize;

        // `#PageCounting = true` [:L290] - the one non-default initializer, and counting is opt-OUT.
        PageCounting = _configuredDefaults.PageCounting;
    }

    #endregion
}

#endregion

