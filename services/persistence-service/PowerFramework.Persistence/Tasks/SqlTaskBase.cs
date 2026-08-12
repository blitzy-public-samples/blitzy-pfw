// =================================================================================================
//  Tasks/SqlTaskBase.cs - the shared WORKER-THREAD base of the Persistence SQL task layer.
//
//  MANAGED PORT OF
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru   (736 lines, 23,334 bytes)
//          $PBExportComments: 线程SQL任务对象基类~r~n[运行在子线程]  = WORKER-THREAD AFFINITY
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_hook.sru  (22 lines, whole)
//
//  REFERENCE ONLY - read as specification, never ported here:
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru      main-thread carrier
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru   worker-thread carrier
//      ws_objects/pfw.thread.pbl.src/n_cst_thread_task.sru                     threading substrate
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru                the transaction
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru           the pool
//      ws_objects/pfw.thread.ext.pbl.src/transactiondata.srs, dberrordata.srs  the two structures
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru      the caller-side proxy
//      ws_objects/pfw.utility.container.pbl.src/n_map.sru                      the ordered map
//      docs/PB多线程绕坑提示.md                                                  the two hazards
//
//  This is the FOUNDATIONAL type of the whole Tasks/ folder: SqlQueryTask, SqlUpdateTask and
//  SqlCommandTask all derive from it and all chain to its Reset.
//
// -------------------------------------------------------------------------------------------------
//  THREAD AFFINITY IS A CONTRACT, NOT COMMENTARY  (AAP §0.4.5.4, §0.6.5 - constraint C-K)
// -------------------------------------------------------------------------------------------------
//  Measured census of all 15 objects in ws_objects/pfw.thread.ext.pbl.src, by their own
//  $PBExportComments annotation:
//
//      [运行在子线程]  WORKER thread   = 6   n_cst_thread_task_sqlbase        <- THIS OBJECT
//                                          n_cst_thread_task_sqlbase_ds_mt
//                                          n_cst_thread_task_sqlbase_hook
//                                          n_cst_thread_task_sqlcommand
//                                          n_cst_thread_task_sqlquery
//                                          n_cst_thread_task_sqlupdate
//      [运行在主线程]  MAIN thread     = 1   n_cst_thread_task_sqlbase_ds     <- exactly one
//      [运行在当前线程] CALLING thread  = 4   n_cst_threading_task_sqlbase     <- the proxies
//                                          n_cst_threading_task_sqlcommand
//                                          n_cst_threading_task_sqlquery
//                                          n_cst_threading_task_sqlupdate
//
//  EVERY concurrency class in the legacy exists TWICE - a caller-side n_cst_threading* and a
//  worker-side n_cst_thread* - specifically so that no object is ever touched from two threads.
//  THIS CLASS IS THE WORKER HALF. IT MUST NOT BE MERGED WITH ITS PROXY. The .NET expression of the
//  pair is async request/response with a CancellationToken, preserving BOTH types and the boundary
//  between them; flattening the pair into one async method would erase the boundary the legacy
//  encodes in its own type system.
//
//  ⚠ THE `_mt` NAMING TRAP. `_mt` does NOT mean "main thread":
//        n_cst_thread_task_sqlbase_ds     [运行在主线程] MAIN,   declared `from datastore`
//        n_cst_thread_task_sqlbase_ds_mt  [运行在子线程] WORKER, declared `from ..._ds`
//  The WORKER variant DERIVES FROM the MAIN one. A reader who guesses inverts the whole affinity
//  selection. The trap is restated at the selection site (see GetCacheDataStore).
//
// -------------------------------------------------------------------------------------------------
//  THE TWO DOCUMENTED THREADING HAZARDS  (docs/PB多线程绕坑提示.md, 5 lines - constraint C-K)
// -------------------------------------------------------------------------------------------------
//  HAZARD 1 - string/blob RETURNS across the thread boundary [docs/PB多线程绕坑提示.md:L1-L4].
//  A worker synchronously calling a MAIN-thread object function or event whose return type is
//  `string` or `blob` can fault. The document names the trigger: the main thread holds an unexecuted
//  Post message for an object while that object is itself destroyed. Its guidance is to RETURN VIA
//  `ref` INSTEAD (改为ref传回).
//      => That is why the chunk and child-data callbacks take `ref blob` parameters rather than
//         returning payloads - see onchilddatareceived(string name, ref blob blbdata) and
//         ondatachunk(ref blob blbdata, long count, long current, boolean fullstate) at
//         n_cst_threading_task_sqlquery.sru:L10,L13. Large payloads therefore cross the boundary as
//         streamed / by-reference buffers, NEVER as return values. Every callback surface declared
//         here for derived tasks follows that shape: see ISqlTaskProxy.
//
//  HAZARD 2 - DETERMINISTIC REFERENCE CLEARING [docs/PB多线程绕坑提示.md:L5].
//  After a global object is passed to a worker, the worker must NULL OUT the main-thread reference
//  on uninit (SetNull). Verified in code at three sites: n_cst_thread_task_sqlbase.sru:L124 and
//  :L718 clear the transaction reference, and n_cst_thread_task_sqlquery.sru:L88 clears the carrier
//  after ownership transfer.
//      => The .NET analogue is DETERMINISTIC clearing of cross-boundary references on task teardown,
//         never reliance on the garbage collector. Concretely here: OnUninit releases the pool
//         reference, zeroes the reference index, EXPLICITLY nulls the transaction reference, and
//         disposes the commit signal. Because the datastore cache holds LIVE store references, its
//         lifetime is part of the same contract.
//
// -------------------------------------------------------------------------------------------------
//  RULES POSITION, AND THE CONSTRAINTS THAT STAND IN THEIR PLACE
// -------------------------------------------------------------------------------------------------
//  NO USER RULES WERE PROVIDED. `review_rules` returns exactly one line saying so, and that line is
//  the complete document. Nothing here is inferred or back-filled from convention, and no file
//  enters scope because of a rule. The binding constraints are the enterprise-standard baseline
//  (AAP §0.7.2) plus the twelve named non-rule constraints C-A..C-L (AAP §0.7.3). Their application
//  to THIS file:
//
//  C-A  Only the four permitted shared projects (Contracts, Shared.Kernel, Shared.Diagnostics,
//       Shared.Containers) plus siblings in this service. NOT Shared.Eventful, NOT
//       Shared.Localization, and never a peer service. No PackageReference is added: central
//       package management is in force and nothing beyond the BCL and those four is needed.
//  C-B  Preserved legacy defects, each reproduced verbatim AND annotated where it is reproduced:
//         * the SetFilter-where-SetSort-was-intended cache-hit defect      [:L546]
//         * the NESTED DBParm parse - never a flat conjunction             [:L127-L132]
//         * the SetParam scan whose loop variable outlives its loop        [:L341-L357]
//         * the missing per-element null guard on the array literal path   [:L315 has it, array none]
//         * the literal 3 returned by the database-error event             [:L97]
//         * the BACKWARD iteration of the committed handler                [:L100-L111]
//         * the "!"/"?" describe-sentinel normalisation                    [:L562, :L564]
//         * two inert commented-out blocks carried across, never revived   [:L113, :L640-L647]
//  C-C  The legacy tree is read-only and is the only behavioural oracle. Nothing under ws_objects/**
//       is edited, moved, deleted or reformatted, and EVERY behavioural assertion below carries a
//       ws_objects/** locator so a reader can adjudicate it against the oracle.
//  C-D  The two deferred libraries are NOT pulled in. `n_scriptinvoker` [:L601, :L695-L701] existed
//       only because PowerScript cannot forward a variadic argument list, so the 20-arm unrolled
//       `choose case` at :L649-L693 and its dynamic-invocation fallback have NO analogue to port -
//       C# is natively variadic. `RegExpFind` [:L128-L129] is satisfied by the BCL, and in this file
//       not even that: the flags are CONSUMED from TransactionData. Persistence therefore acquires
//       neither ScriptBridge nor Documents coupling. The legacy arity ceilings - 20 here, 8 in
//       n_cst_thread_trans, 11 in n_sqlite - are RECORDED as legacy limits and DELIBERATELY NOT
//       ENFORCED: exceeding them is not a behavioural regression.
//  C-E  Nothing here opens a connection or selects a database engine. The dialect value is used for
//       one purpose only - choosing between two literal string forms.
//  C-F  No credential literal appears anywhere. TransactionData.LogPass is WRITE-ONLY and is never
//       logged, never echoed and never rendered. Every statement-bearing error payload leaves the
//       process only through DbErrorData.ToDbError(), which masks the statement unconditionally.
//  C-G  No listener, no route, no endpoint. Activation of a retrieval hook from a caller-supplied
//       class NAME is validated against a closed set - see SqlRetrievalHookActivator - and never
//       activates an arbitrary type from untrusted input.
//  C-H  TimeProvider is the ONLY clock. There is no DateTime.UtcNow, no DateTime.Now, no
//       Environment.TickCount and no Stopwatch in this file. Every collaborator is an injectable
//       abstraction so the task layer is unit-testable with NO real thread and NO database, which is
//       what makes the per-service 80% line-coverage gate reachable.
//  C-I  Compiles warning-clean under TreatWarningsAsErrors with nullable enabled, adding no package.
//  C-J  NO static or global instance. The legacy declares
//       `global n_cst_thread_task_sqlbase n_cst_thread_task_sqlbase` [:L37], an auto-instance
//       shadowing its own type name; that is a flat-namespace artefact and is NOT reproduced. This
//       type is constructor-injected and is registered DISTINCTLY from its caller-side proxy.
//  C-K  Documented above and at each decision below: the affinity census, the non-flattening
//       decision, the `_mt` trap, the arity ceilings, both threading hazards, the two similarly
//       named pool configuration keys, and every deviation forced by a sibling's actual surface.
//  C-L  The environment's operational constraints are honoured by the service, not by this file:
//       nothing here binds a port, reads a secret or touches the orchestration layer.
//
//  .editorconfig SCOPE. The repository-root .editorconfig's CA1707/IDE1006 naming suppressions are
//  scoped to the specific files that carry preserved legacy constant spellings (AAP §0.4.5.3).
//  Tasks/ is OUTSIDE those globs, so this file declares ZERO SCREAMING_SNAKE identifiers. Legacy
//  constant VALUES are still preserved exactly - the legacy `constant string ARG_PREFIX = ":"`
//  [:L396] becomes the PascalCase `ArgPrefix` with the identical value ":". References to
//  RetCode.E_INVALID_ARGUMENT and to generated protocol enum members are unaffected: the
//  prohibition is on DECLARING such an identifier here, not on naming one declared elsewhere.
//
//  ACCESSIBILITY. This type is `internal`, not `public`, and that is forced rather than chosen:
//  ICarrierParentTask, IPooledTransaction, TransactionPool, DbErrorData and DataWindowCarrier are
//  all internal to this assembly, so a public member mentioning any of them would be a
//  CS0051/CS0053 inconsistent-accessibility error. The project's
//  InternalsVisibleTo("PowerFramework.Persistence.Tests") is what keeps the whole surface testable,
//  which is the trade the coverage gate needs.
// =================================================================================================

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using PowerFramework.Contracts.Common.V1;
using PowerFramework.Contracts.Persistence.V1;
using PowerFramework.Persistence.Buffers;
using PowerFramework.Persistence.Concurrency;
using PowerFramework.Persistence.Errors;
using PowerFramework.Persistence.Transactions;
using PowerFramework.Shared.Containers;

// The generated contract carries a RetCode MESSAGE and Shared.Kernel carries the RetCode CONSTANT
// CATALOGUE. Both are in scope here, so the alias states which one every bare `RetCode` means -
// the same alias every other file in this project uses.
using Predicates = PowerFramework.Shared.Kernel.Predicates;
using RetCode = PowerFramework.Shared.Kernel.RetCode;

namespace PowerFramework.Persistence.Tasks;

#region The threading-substrate seam - what this file needs from n_cst_thread_task, and no more

/// <summary>
/// The narrow slice of the legacy threading substrate
/// [<c>ws_objects/pfw.thread.pbl.src/n_cst_thread_task.sru</c>] that the SQL task base actually
/// consumes.
/// </summary>
/// <remarks>
/// <para>
/// <b>The substrate is deliberately NOT ported wholesale.</b> <c>n_cst_thread_task.sru</c> is 622
/// lines of general threading machinery - execution groups, cancellation handles, delayed start, an
/// eleven-arity trigger and post surface, message boxes and per-task keyed data - and it belongs
/// outside this folder's remit. What is modelled here is exactly the set of substrate members
/// <c>n_cst_thread_task_sqlbase.sru</c> reaches for, each with its locator, so the dependency is
/// legible and finite.
/// </para>
/// <para>
/// Modelling it as an interface rather than as a base class is what makes constraint C-H reachable:
/// the whole task layer becomes testable with no real thread, because a test supplies a host that
/// answers these questions directly.
/// </para>
/// </remarks>
internal interface ISqlTaskHost
{
    /// <summary>
    /// Whether the owning thread is the main thread - <c>#ParentThread.of_IsMainThread()</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L553</c>,
    /// <c>ws_objects/pfw.thread.pbl.src/n_cst_thread.sru:L725</c>].
    /// </summary>
    /// <remarks>
    /// This single boolean selects the carrier affinity at all three legacy selection sites. See
    /// <see cref="SqlTaskBase.CreateDataStore"/> for the <c>_mt</c> trap that makes reading it
    /// correctly non-obvious.
    /// </remarks>
    bool IsMainThread { get; }

    /// <summary>
    /// Whether the task has been cancelled - <c>of_IsCancelled()</c>
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_thread_task.sru</c>, prototype at its
    /// <c>forward prototypes</c> block].
    /// </summary>
    bool IsCancelled { get; }

    // The substrate's `#Running` property is DELIBERATELY NOT modelled here. Every guard written
    // against it in this object is COMMENTED OUT in the oracle [:L113, :L253, :L343, :L361] and is
    // carried across inert, so nothing in this file may read it; the guard that IS live belongs to the
    // caller-side proxy and is written against ITS own `of_IsBusy()`
    // [n_cst_threading_task_sqlbase.sru:L110, :L128, :L145, :L163]. Declaring an unread member here
    // would invite exactly the revival C-B forbids.

    /// <summary>
    /// The task's ONE-BASED position in its thread's task list - <c>of_GetIndex()</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L104</c>].
    /// </summary>
    /// <remarks>
    /// One-based, because <see cref="GetTask"/> is one-based
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_thread.sru:L366</c>] and the committed handler walks
    /// down to 1 rather than to 0.
    /// </remarks>
    int TaskIndex { get; }

    /// <summary>
    /// Resolves the task at a ONE-BASED position - <c>#ParentThread.of_GetTask(nIndex, ref task)</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L105</c>].
    /// </summary>
    /// <param name="index">The one-based task position.</param>
    /// <param name="task">Receives the task, or <see langword="null"/> when none resolves.</param>
    /// <returns>
    /// <see cref="RetCode.OK"/> on success, or <see cref="RetCode.E_OUT_OF_BOUND"/> for an index
    /// outside <c>1..UpperBound(Tasks)</c>
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_thread.sru:L366</c>].
    /// </returns>
    /// <remarks>
    /// The legacy result is tested with <c>IsSucceeded</c>, so a caller must go through
    /// <see cref="Predicates.IsSucceeded(long?)"/> rather than comparing to zero - a prevention
    /// (value 1) reads as a success in that algebra and the distinction is preserved everywhere.
    /// </remarks>
    long GetTask(int index, out SqlTaskBase? task);

    /// <summary>
    /// Whether the owning thread carries keyed data under <paramref name="name"/> -
    /// <c>#ParentThread.of_HasData(...)</c> [<c>n_cst_thread_task_sqlbase.sru:L194, :L531</c>].
    /// </summary>
    /// <param name="name">The data key.</param>
    /// <returns><see langword="true"/> when a value is stored under that key.</returns>
    bool HasData(string name);

    /// <summary>
    /// Reads the owning thread's keyed data - <c>#ParentThread.of_GetData(...)</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L195, :L532</c>].
    /// </summary>
    /// <param name="name">The data key.</param>
    /// <returns>The stored value, or <see langword="null"/> when the key is absent.</returns>
    object? GetData(string name);

    /// <summary>
    /// Writes the owning thread's keyed data - <c>#ParentThread.of_SetData(...)</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L205, :L536</c>].
    /// </summary>
    /// <param name="name">The data key.</param>
    /// <param name="data">The value to store.</param>
    /// <returns><see cref="RetCode.OK"/> on success.</returns>
    long SetData(string name, object? data);

    /// <summary>
    /// The caller-side proxy this worker task reports to - the substrate's <c>#ParentTasking</c>
    /// property, read at <c>n_cst_thread_task_sqlbase.sru:L94</c>.
    /// </summary>
    /// <remarks>
    /// Nullable because the legacy reads it into a local and the framework tolerates an unattached
    /// task; the database-error event therefore checks validity before forwarding, exactly as
    /// <see cref="Predicates.IsValidObject(object?)"/> does elsewhere in the port.
    /// </remarks>
    ISqlTaskProxy? ParentTasking { get; }

    /// <summary>
    /// The substrate's own prepare handler - what <c>call super::onprepare</c> reaches
    /// [<c>n_cst_thread_task_sqlbase.sru:L725</c>].
    /// </summary>
    /// <returns>The ancestor's return value.</returns>
    long OnPrepare();

    /// <summary>
    /// The substrate's own uninit handler - what <c>call super::onuninit</c> reaches
    /// [<c>n_cst_thread_task_sqlbase.sru:L715</c>].
    /// </summary>
    void OnUninit();

    /// <summary>
    /// The substrate's own error handler - what <c>call super::OnError</c> reaches
    /// [<c>n_cst_thread_task_sqlbase.sru:L733</c>].
    /// </summary>
    /// <param name="errCode">The framework error code.</param>
    /// <param name="errInfo">The diagnostic text.</param>
    /// <returns>
    /// The value the override returns verbatim as <c>AncestorReturnValue</c> [<c>:L734</c>].
    /// </returns>
    long OnError(long errCode, string errInfo);

    /// <summary>
    /// The substrate's notification handler - <c>Event OnNotify(wparam, lparam, sparam)</c>
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_thread_task.sru</c>, events block].
    /// </summary>
    /// <param name="notifyCode">The <c>wparam</c> notification code.</param>
    /// <param name="payload">
    /// The <c>lparam</c> payload. Progress notifications pack current and total into it as two
    /// 16-bit words [<c>n_cst_thread_task_sqlbase_ds_mt.sru:L39</c>].
    /// </param>
    /// <param name="text">The <c>sparam</c> text.</param>
    /// <returns>1 to stop processing, 0 to continue.</returns>
    long OnNotify(long notifyCode, long payload, string text);
}

/// <summary>
/// The narrow slice of the caller-side proxy
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru</c>, marked
/// <c>[运行在当前线程]</c>] that this worker task pushes results into.
/// </summary>
/// <remarks>
/// <para>
/// <b>This interface is the boundary the affinity split exists to protect, and it carries EXACTLY ONE
/// member because the legacy proxy declares exactly one event</b> -
/// <c>event ondberror(readonly dberrordata err)</c>
/// [<c>n_cst_threading_task_sqlbase.sru:L9</c>]. Its derived proxies add their own events, and those
/// belong to the derived task layer rather than here. Everything the proxy DOES with the error -
/// latching it as the last one at <c>:L44</c>, and separately polling the commit handle
/// non-blockingly with <c>WaitForSingleObject(_hEvtCommitted, 0)</c> at <c>:L175</c> - happens on the
/// calling thread and is that type's business, not this one's.
/// </para>
/// <para>
/// <b>Hazard 1 shapes this signature.</b> It returns neither a <see cref="string"/> nor a buffer: the
/// error payload travels INWARD as an <see langword="in"/> parameter. Payload-bearing callbacks that
/// derived tasks add must follow the same rule and pass buffers BY REFERENCE rather than returning
/// them - which is exactly why the query proxy declares
/// <c>onchilddatareceived(string name, ref blob blbdata)</c> and
/// <c>ondatachunk(ref blob blbdata, long count, long current, boolean fullstate)</c>
/// [<c>docs/PB多线程绕坑提示.md:L1-L4</c>;
/// <c>n_cst_threading_task_sqlquery.sru:L10, L13</c>].
/// </para>
/// </remarks>
internal interface ISqlTaskProxy
{
    /// <summary>
    /// Receives a database error - <c>tasking.Event OnDBError(err)</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L95</c>], declared
    /// <c>event ondberror(readonly dberrordata err)</c> on the proxy
    /// [<c>n_cst_threading_task_sqlbase.sru:L9</c>].
    /// </summary>
    /// <param name="error">
    /// The five-member payload. Taken by <see langword="in"/> because the legacy parameter is
    /// <c>readonly</c>, which is the mapping this migration fixes for PowerBuilder's
    /// <c>readonly</c>.
    /// </param>
    /// <remarks>
    /// <b>Constraint C-F.</b> The payload's statement member carries the COMPLETE generated statement
    /// including interpolated literals, and the legacy logger redacts nothing. An implementation must
    /// therefore never log or publish it directly; the only sanctioned projection is
    /// <c>DbErrorData.ToDbError()</c>, which masks that one member unconditionally.
    /// </remarks>
    void OnDbError(in DbErrorData error);
}

#endregion

#region The DataWindow-DEFINITION seam - the surface the datastore cache manipulates

/// <summary>
/// The names of the DataWindow properties this layer reads through <see cref="ISqlDataStore.Describe"/>
/// and writes through <see cref="ISqlDataStore.Modify"/>, spelled exactly as the oracle spells them.
/// </summary>
/// <remarks>
/// <para>
/// Every string here appears verbatim in the legacy source, and the spellings are load-bearing: they
/// are the keys a DataWindow's own property dictionary is addressed by, so a "tidied" spelling would
/// silently address nothing. The set is closed at the six properties the SQL task layer genuinely
/// touches - the port invents no seventh.
/// </para>
/// <para>
/// These are PascalCase C# identifiers carrying legacy string VALUES, which is exactly the split the
/// .editorconfig scoping requires: the value is preserved, the identifier spelling is not the
/// legacy's (AAP §0.4.5.3).
/// </para>
/// </remarks>
internal static class DataWindowProperty
{
    /// <summary>
    /// <c>DataWindow.Table.Select</c> - the retrieval statement, written at
    /// <c>n_cst_thread_task_sqlbase.sru:L543</c> and read through <c>GetSQLSelect()</c> at
    /// <c>:L542, :L560</c>.
    /// </summary>
    internal const string TableSelect = "DataWindow.Table.Select";

    /// <summary>
    /// <c>DataWindow.Table.Sort</c> - described at <c>n_cst_thread_task_sqlbase.sru:L545, :L561</c>.
    /// </summary>
    internal const string TableSort = "DataWindow.Table.Sort";

    /// <summary>
    /// <c>DataWindow.Table.Filter</c> - described at
    /// <c>n_cst_thread_task_sqlbase.sru:L548, :L563</c>.
    /// </summary>
    internal const string TableFilter = "DataWindow.Table.Filter";

    /// <summary>
    /// <c>DataWindow.Table.Arguments</c> - described at
    /// <c>n_cst_thread_task_sqlbase.sru:L603</c>, then split by
    /// <see cref="SqlTaskBase.ParseDataWindowArguments"/>.
    /// </summary>
    internal const string TableArguments = "DataWindow.Table.Arguments";

    /// <summary>
    /// <c>DataWindow.Processing</c> - described at
    /// <c>n_cst_thread_task_sqlquery.sru:L560</c> and again at <c>:L93</c> to select the changeset or
    /// full-state transfer path. Projected onto the carrier's own
    /// <see cref="DataWindowBufferStore.Processing"/> by
    /// <see cref="DataWindowProcessing.FromDescribe"/>.
    /// </summary>
    internal const string Processing = "DataWindow.Processing";

    /// <summary>
    /// <c>DataWindow.Units</c> - probed at <c>n_cst_thread_task_sqlquery.sru:L554</c> purely to test
    /// whether a data object resolved at all: an empty answer there yields
    /// <c>RetCode.E_INVALID_ARGUMENT</c> with the diagnostic 无效的DataObject.
    /// </summary>
    internal const string Units = "DataWindow.Units";
}

/// <summary>
/// One resolved DataWindow definition - the .NET stand-in for a compiled <c>.srd</c> object, which no
/// managed runtime can load.
/// </summary>
/// <param name="DataObject">
/// The data-object name the definition answers to, as assigned at
/// <c>n_cst_thread_task_sqlbase.sru:L558</c>.
/// </param>
/// <param name="SqlSelect">
/// The retrieval statement - the value <c>GetSQLSelect()</c> returns [<c>:L560</c>]. The golden-master
/// fixture declares <c>SELECT * FROM COMPANY</c>
/// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14</c>].
/// </param>
/// <param name="Sort">
/// The value <c>Describe("DataWindow.Table.Sort")</c> answers [<c>:L561</c>]. The fixture declares
/// <c>age A salary A </c> - note the trailing space, which is part of the value
/// [<c>dw_sqlite.srd:L14</c>]. May be a PowerBuilder describe sentinel; see
/// <see cref="SqlTaskBase.NormalizeDescribeSentinel"/>.
/// </param>
/// <param name="Filter">
/// The value <c>Describe("DataWindow.Table.Filter")</c> answers [<c>:L563</c>]. May likewise be a
/// sentinel.
/// </param>
/// <param name="Processing">
/// The value <c>Describe("DataWindow.Processing")</c> answers. The fixture declares
/// <c>processing=1</c> [<c>dw_sqlite.srd:L3</c>], which takes the changeset transfer path rather than
/// the full-state one.
/// </param>
/// <param name="Arguments">
/// The value <c>Describe("DataWindow.Table.Arguments")</c> answers [<c>:L603</c>] - newline-separated
/// <c>name{tab}type</c> pairs, exactly the grammar
/// <see cref="SqlTaskBase.ParseDataWindowArguments"/> parses. Empty when the data object takes no
/// retrieval arguments, which is the fixture's case.
/// </param>
/// <param name="Units">
/// The value <c>Describe("DataWindow.Units")</c> answers. Non-empty for any real definition, because
/// the query task treats an empty answer as proof the data object did not resolve
/// [<c>n_cst_thread_task_sqlquery.sru:L554-L557</c>].
/// </param>
/// <remarks>
/// A record rather than a class so that two definitions of the same data object compare equal by
/// value, which keeps a definition source free to hand out fresh instances without perturbing
/// anything that compares them.
/// </remarks>
internal sealed record DataObjectDefinition(
    string DataObject,
    string SqlSelect,
    string Sort,
    string Filter,
    string Processing,
    string Arguments,
    string Units);

/// <summary>
/// The DataWindow runtime this port does not have - the seam through which a data-object NAME becomes
/// a definition, and through which a retrieval is actually executed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists at all.</b> The legacy assigns a name and the PowerBuilder runtime loads the
/// compiled DataWindow out of a library: <c>ds.DataObject = dataObject</c>
/// [<c>n_cst_thread_task_sqlbase.sru:L558</c>] is a resolution against the target's library list.
/// There is no managed equivalent - the <c>.srd</c> objects live in the read-only legacy tree and no
/// .NET runtime can load them - so resolution becomes an injected collaborator. The published
/// contract already anticipates this: <c>QuerySpec</c> carries <c>data_object</c>,
/// <c>sql_syntax</c>, <c>sql</c>, <c>filter</c> and <c>sort</c> side by side
/// [<c>persistence.v1.proto</c>, fields 2, 3, 4, 6, 7], so a caller may name a definition or supply
/// one outright.
/// </para>
/// <para>
/// <b>Why retrieval lives here too.</b> <c>_of_RetrieveWithParams</c> owns argument MATCHING
/// [<c>:L597-L705</c>] but the final call is <c>Data.Retrieve(...)</c> on the DataWindow itself. That
/// division is preserved exactly: <see cref="SqlTaskBase.RetrieveWithParamsAsync"/> does the matching and
/// this member does the executing, so the matching rules stay unit-testable against a runtime that
/// touches no database (constraint C-H).
/// </para>
/// <para>
/// <b>There is deliberately no default implementation.</b> A no-op runtime would be a stub that
/// silently retrieved nothing, so the composition root must supply a real one - the same discipline
/// <see cref="TransactionPool"/> applies to its activator.
/// </para>
/// </remarks>
internal interface IDataObjectRuntime
{
    /// <summary>
    /// Resolves a data-object name to its definition.
    /// </summary>
    /// <param name="dataObject">The data-object name assigned at <c>:L558</c>.</param>
    /// <param name="definition">Receives the definition, or <see langword="null"/> when unresolved.</param>
    /// <returns>
    /// <see langword="true"/> when the name resolved. <see langword="false"/> reproduces the legacy
    /// state of a DataStore whose data object did not load: every describe then answers the failure
    /// sentinel, which the query task detects through the <c>DataWindow.Units</c> probe
    /// [<c>n_cst_thread_task_sqlquery.sru:L554-L557</c>].
    /// </returns>
    bool TryResolveDefinition(string dataObject, [NotNullWhen(true)] out DataObjectDefinition? definition);

    /// <summary>
    /// Executes the retrieval - the managed stand-in for <c>Data.Retrieve(...)</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L649-L702</c>].
    /// </summary>
    /// <param name="data">The store whose definition and carrier the retrieval fills.</param>
    /// <param name="parameters">
    /// The matched retrieval arguments in ONE-BASED legacy order, already resolved by
    /// <see cref="SqlTaskBase.RetrieveWithParamsAsync"/>. Passed as a single list, which is the whole
    /// reason the legacy's 20-arm unrolled <c>choose case</c> and its dynamic-invocation fallback have
    /// nothing to port (constraint C-D).
    /// </param>
    /// <param name="cancellationToken">
    /// The request's token. <b>THE RETRIEVAL IS THE ONE PROVIDER CALL IN THIS SERVICE THAT IS GENUINELY
    /// ASYNCHRONOUS, AND THAT IS WHY THIS MEMBER IS THE ONE THAT TAKES A TOKEN AND AWAITS.</b> Its whole
    /// call chain above it - the query task's own retrieval body, the runner, and the streaming handler -
    /// is already asynchronous, so nothing had to be restructured to reach it: the only thing missing was
    /// that the last step ignored the caller. The oracle's equivalent poll is <c>of_IsCancelled()</c>,
    /// which its retrieval body checks either side of the call
    /// [<c>n_cst_thread_task_sqlquery.sru:L740, :L785</c>]; a token observed inside the retrieval is
    /// strictly more responsive than a poll that can only fire once the retrieval has returned.
    /// </param>
    /// <returns>
    /// The retrieved row count, or a negative value on failure - the DataWindow convention, NOT the
    /// return-code algebra. The query task tests it as <c>nRowCnt &lt; 0</c>
    /// [<c>n_cst_thread_task_sqlquery.sru:L777</c>].
    /// </returns>
    ValueTask<long> RetrieveAsync(
        ISqlDataStore data,
        IReadOnlyList<object?> parameters,
        CancellationToken cancellationToken);
}

/// <summary>
/// A DataWindow-shaped result carrier: the buffer store the codecs consume, PLUS the definition
/// surface the datastore cache manipulates.
/// </summary>
/// <remarks>
/// <para>
/// <b>The legacy fuses the two and this port cannot.</b> <c>n_cst_thread_task_sqlbase_ds</c> is
/// declared <c>from datastore</c> [<c>n_cst_thread_task_sqlbase_ds.sru:L4</c>], so one object is both
/// the row/buffer/item-status carrier and the DataWindow-definition surface. In the port those halves
/// have different owners: <c>Buffers/DataWindowBuffers.cs</c> owns the carrier - buffers, item
/// statuses, counts, the SQL-preview interception and the N-char literal rewriter - and this interface
/// adds only the definition half, which no sibling owns. The carrier's own documentation says so
/// explicitly, describing its <see cref="DataWindowBufferStore.Processing"/> as assigned "by whichever
/// layer assigns the data object": that layer is this one.
/// </para>
/// <para>
/// <b>Nothing the carrier already provides is redeclared here.</b> Rows, buffers, item statuses,
/// counts and the N-char rewrite are reached through <see cref="Carrier"/> and are never
/// reimplemented - see the ownership cross-check in this file's header.
/// </para>
/// </remarks>
internal interface ISqlDataStore
{
    /// <summary>
    /// The buffer store this result travels in, created by the sibling affinity-keyed factory
    /// <see cref="DataWindowCarrierFactory"/>.
    /// </summary>
    DataWindowCarrier Carrier { get; }

    /// <summary>
    /// The assigned data-object name - <c>ds.DataObject = dataObject</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L558</c>].
    /// </summary>
    /// <remarks>
    /// Assigning it RESOLVES the definition through <see cref="IDataObjectRuntime"/> and projects
    /// <see cref="DataWindowProperty.Processing"/> onto the carrier, which is what the legacy
    /// assignment does implicitly by loading the compiled object.
    /// </remarks>
    string DataObject { get; set; }

    /// <summary>
    /// The current retrieval statement - <c>ds.GetSQLSelect()</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L542, :L560</c>].
    /// </summary>
    /// <returns>The statement text, or the empty string when no definition is resolved.</returns>
    string GetSqlSelect();

    /// <summary>
    /// Applies a DataWindow modification script -
    /// <c>ds.Modify('DataWindow.Table.Select = "..."')</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L543</c>].
    /// </summary>
    /// <param name="modificationScript">A <c>property = "value"</c> script.</param>
    /// <returns>
    /// The empty string on success and an error description otherwise - PowerBuilder's own
    /// <c>Modify</c> convention, matching the sibling seam
    /// <c>IUpdateTargetModifier.Modify</c> in <c>Concurrency/UpdateWhereBuilder.cs</c> and the legacy
    /// check <c>if sError &lt;&gt; "" then</c>
    /// [<c>n_cst_thread_task_sqlupdate.sru:L145</c>].
    /// </returns>
    string Modify(string modificationScript);

    /// <summary>
    /// Reads a DataWindow property - <c>ds.Describe("DataWindow.Table.Sort")</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L545, :L548, :L561, :L563</c>].
    /// </summary>
    /// <param name="property">One of the <see cref="DataWindowProperty"/> names.</param>
    /// <returns>
    /// The property value, or PowerBuilder's failure sentinel <c>"!"</c> for a property this surface
    /// does not carry or for a store with no resolved definition. The sentinel is not an exception
    /// path: the legacy consumes it as data and normalises it at <c>:L562</c> and <c>:L564</c>.
    /// </returns>
    string Describe(string property);

    /// <summary>
    /// Installs a filter expression - <c>ds.SetFilter(...)</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L546, :L549</c>].
    /// </summary>
    /// <param name="filter">The filter expression; <see langword="null"/> clears it.</param>
    /// <returns>
    /// <see cref="DataWindowBufferStore.DataStoreSuccess"/> or
    /// <see cref="DataWindowBufferStore.DataStoreFailure"/> - the DataStore convention of 1 and -1,
    /// which is NOT the return-code algebra.
    /// </returns>
    long SetFilter(string? filter);

    /// <summary>
    /// Installs a sort expression - <c>ds.SetSort(...)</c>, as the query task calls it and tests
    /// against 1 [<c>n_cst_thread_task_sqlquery.sru:L570</c>].
    /// </summary>
    /// <param name="sort">The sort expression; <see langword="null"/> clears it.</param>
    /// <returns>
    /// <see cref="DataWindowBufferStore.DataStoreSuccess"/> or
    /// <see cref="DataWindowBufferStore.DataStoreFailure"/>.
    /// </returns>
    /// <remarks>
    /// <b>This member is what makes the preserved cache-hit defect legible.</b> The oracle HAS this
    /// function and still calls <see cref="SetFilter"/> when restoring the sort
    /// [<c>n_cst_thread_task_sqlbase.sru:L546</c>]. Declaring it here - rather than omitting it
    /// because the cache path never reaches it - is what lets a reader and a test see that the defect
    /// is a wrong call and not a missing capability.
    /// </remarks>
    long SetSort(string? sort);

    /// <summary>
    /// Clears the carrier's accumulated per-execution state - <c>ds.of_ClearState()</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L551</c>], implemented by
    /// <see cref="DataWindowCarrier.ClearState"/>.
    /// </summary>
    void ClearState();

    /// <summary>
    /// Associates the store with its owning task - <c>ds.Event OnInit(this)</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L568</c>], implemented by
    /// <see cref="DataWindowCarrier.OnInit"/>.
    /// </summary>
    /// <param name="parentTask">The owning task.</param>
    void OnInit(ICarrierParentTask parentTask);

    /// <summary>
    /// Executes the retrieval with already-matched arguments, delegating to
    /// <see cref="IDataObjectRuntime.RetrieveAsync"/>.
    /// </summary>
    /// <param name="parameters">The matched arguments in one-based legacy order.</param>
    /// <param name="cancellationToken">The request's token, observed by the runtime.</param>
    /// <returns>The row count, or a negative value on failure.</returns>
    ValueTask<long> RetrieveAsync(
        IReadOnlyList<object?> parameters,
        CancellationToken cancellationToken);
}

/// <summary>
/// Creates <see cref="ISqlDataStore"/> instances at a requested thread affinity.
/// </summary>
/// <remarks>
/// A seam rather than a static call so that a test can hand the task layer stores it fully controls,
/// which is what lets the datastore cache - including its preserved defect - be exercised with no
/// database and no real thread (constraint C-H).
/// </remarks>
internal interface ISqlDataStoreFactory
{
    /// <summary>
    /// Creates a store whose carrier matches <paramref name="affinity"/>.
    /// </summary>
    /// <param name="affinity">The affinity of the thread the store will be used on.</param>
    /// <returns>A store with a freshly created carrier and no definition assigned.</returns>
    ISqlDataStore Create(CarrierThreadAffinity affinity);
}

/// <summary>
/// The default <see cref="ISqlDataStore"/>: a definition surface bolted onto a carrier obtained from
/// the sibling affinity-keyed factory.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is NOT a carrier type and declares no buffer, row or item-status member.</b> It owns a
/// <see cref="DataWindowCarrier"/> created by <see cref="DataWindowCarrierFactory"/> and forwards
/// every carrier concern to it. What it adds is the DataWindow-DEFINITION half that
/// <c>n_cst_thread_task_sqlbase_ds</c> inherits from <c>datastore</c> and that no sibling owns -
/// the data-object name, the retrieval statement, the sort and filter expressions, and the
/// <c>Describe</c>/<c>Modify</c> property access the datastore cache is written in terms of.
/// </para>
/// <para>
/// <b>Mutable definition state is the point, not an oversight.</b> The cache-hit path at
/// <c>n_cst_thread_task_sqlbase.sru:L539-L551</c> exists precisely because a pooled store's
/// statement, sort and filter may have been changed by a previous execution and have to be compared
/// against the snapshot and put back. A store whose definition could not drift would make that whole
/// path - and its preserved defect - unreachable.
/// </para>
/// </remarks>
internal sealed class SqlDataObjectStore : ISqlDataStore
{
    /// <summary>PowerBuilder's <c>Describe</c> failure sentinel for an unrecognised property.</summary>
    /// <remarks>
    /// Normalised away by the snapshot path at <c>n_cst_thread_task_sqlbase.sru:L562</c> and
    /// <c>:L564</c>, which is the only reason this value has to be produced faithfully rather than
    /// replaced by an exception.
    /// </remarks>
    internal const string DescribeFailureSentinel = "!";

    private readonly IDataObjectRuntime _runtime;

    private string _dataObject = string.Empty;
    private DataObjectDefinition? _definition;
    private string _sqlSelect = string.Empty;
    private string _sort = string.Empty;
    private string _filter = string.Empty;

    /// <summary>
    /// Creates a store over an existing carrier.
    /// </summary>
    /// <param name="carrier">
    /// The carrier from <see cref="DataWindowCarrierFactory"/>. Its concrete type - main-thread or
    /// worker-thread - already encodes the affinity, so this type never re-decides it.
    /// </param>
    /// <param name="runtime">The DataWindow runtime that resolves definitions and executes retrievals.</param>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    internal SqlDataObjectStore(DataWindowCarrier carrier, IDataObjectRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(carrier);
        ArgumentNullException.ThrowIfNull(runtime);

        Carrier = carrier;
        _runtime = runtime;
    }

    /// <inheritdoc/>
    public DataWindowCarrier Carrier { get; }

    /// <inheritdoc/>
    public string DataObject
    {
        get => _dataObject;

        set
        {
            // `ds.DataObject = dataObject` [n_cst_thread_task_sqlbase.sru:L558]. In the legacy this
            // single assignment loads a compiled DataWindow out of the target's library list, which
            // is why the very next lines can read a statement, a sort and a filter back out of it.
            _dataObject = value ?? string.Empty;

            if (_dataObject.Length > 0 && _runtime.TryResolveDefinition(_dataObject, out DataObjectDefinition? resolved))
            {
                _definition = resolved;
                _sqlSelect = resolved.SqlSelect;
                _sort = resolved.Sort;
                _filter = resolved.Filter;

                // The carrier's own transfer-path discriminator, which its documentation says is
                // "assigned from Describe(\"DataWindow.Processing\") ... by whichever layer assigns
                // the data object". This is that layer, so the projection happens here rather than
                // being left for a codec to re-parse and possibly disagree about.
                Carrier.Processing = DataWindowProcessing.FromDescribe(resolved.Processing);
                return;
            }

            // An unresolved name leaves the store exactly as PowerBuilder leaves a DataStore whose
            // data object failed to load: no statement, no expressions, and EVERY RECOGNISED PROPERTY
            // DESCRIBING AS EMPTY - which is what the query task's DataWindow.Units probe tests
            // [n_cst_thread_task_sqlquery.sru:L553-L557], and see the remarks on Describe for why the
            // empty string rather than the "!" sentinel is the faithful answer here. The task detects
            // the state through that probe rather than through an exception, so throwing here would
            // replace a legacy code path with one the oracle does not have.
            _definition = null;
            _sqlSelect = string.Empty;
            _sort = string.Empty;
            _filter = string.Empty;
            Carrier.Processing = DataWindowProcessing.Unassigned;
        }
    }

    /// <summary>
    /// Installs a definition that was DERIVED from a syntax string rather than resolved from a name.
    /// </summary>
    /// <param name="definition">The derived definition.</param>
    /// <remarks>
    /// <para>
    /// THE SECOND OF PowerBuilder's TWO WAYS TO GIVE A DataStore A DATA OBJECT. One is
    /// <c>ds.DataObject = name</c>, which loads a compiled object from the library list and is the
    /// <see cref="DataObject"/> setter above. The other is <c>ds.Create(syntax, ref error)</c>
    /// [<c>n_cst_thread_task_sqlquery.sru:L619</c>], which builds one from a syntax string at run time -
    /// and it is the path contract C-05 uses whenever a caller supplies a statement instead of naming a
    /// data object, which is the ordinary case.
    /// </para>
    /// <para>
    /// WHY THIS IS A METHOD ON THE STORE RATHER THAN A CATALOGUE ENTRY. A derived definition belongs to
    /// ONE request: it exists because that request supplied a statement, and it is meaningless to any
    /// other. Registering it in the shared catalogue under a generated name would work and was
    /// considered, and it is worse in two ways that matter - the catalogue would grow for the lifetime of
    /// the process with one entry per distinct statement, and a name a caller never chose would become
    /// resolvable by anyone who guessed it. Holding it here means it is collected with the store.
    /// </para>
    /// <para>
    /// The five assignments are exactly the <see cref="DataObject"/> setter's success arm, less the name
    /// lookup. Keeping them identical is deliberate: a store built either way must describe identically,
    /// or the query task's <c>DataWindow.Units</c> probe
    /// [<c>n_cst_thread_task_sqlquery.sru:L554-L557</c>] would answer differently for the two paths.
    /// </para>
    /// </remarks>
    internal void InstallDerivedDefinition(DataObjectDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        _dataObject = definition.DataObject;
        _definition = definition;
        _sqlSelect = definition.SqlSelect;
        _sort = definition.Sort;
        _filter = definition.Filter;
        Carrier.Processing = DataWindowProcessing.FromDescribe(definition.Processing);
    }

    /// <inheritdoc/>
    public string GetSqlSelect() => _sqlSelect;

    /// <inheritdoc/>
    public string Modify(string modificationScript)
    {
        if (string.IsNullOrEmpty(modificationScript))
        {
            // PowerBuilder answers a description rather than raising, and the caller tests for a
            // non-empty string [n_cst_thread_task_sqlupdate.sru:L145].
            return "empty modification script";
        }

        int separator = modificationScript.IndexOf('=', StringComparison.Ordinal);
        if (separator <= 0)
        {
            return "malformed modification script: " + modificationScript;
        }

        string property = modificationScript[..separator].Trim();
        string rawValue = modificationScript[(separator + 1)..].Trim();

        // The legacy composes the script as `'DataWindow.Table.Select = "' + sql + '"'` [:L543], so
        // the value arrives wrapped in double quotes. Unwrapping exactly one matched pair - and only
        // a matched pair - keeps a statement that legitimately contains quotes intact.
        if (rawValue.Length >= 2 && rawValue[0] == '"' && rawValue[^1] == '"')
        {
            rawValue = rawValue[1..^1];
        }

        if (string.Equals(property, DataWindowProperty.TableSelect, StringComparison.OrdinalIgnoreCase))
        {
            _sqlSelect = rawValue;
            return string.Empty;
        }

        if (string.Equals(property, DataWindowProperty.TableSort, StringComparison.OrdinalIgnoreCase))
        {
            _sort = rawValue;
            return string.Empty;
        }

        if (string.Equals(property, DataWindowProperty.TableFilter, StringComparison.OrdinalIgnoreCase))
        {
            _filter = rawValue;
            return string.Empty;
        }

        return "unsupported property: " + property;
    }

    /// <inheritdoc/>
    public string Describe(string property)
    {
        if (string.IsNullOrEmpty(property))
        {
            return DescribeFailureSentinel;
        }

        // 🔴 THE TWO DESCRIBE FAILURES ARE DIFFERENT ANSWERS, AND COLLAPSING THEM KILLED THE ORACLE'S OWN
        // VALIDITY PROBE. "!" is PowerBuilder's answer for a property it DOES NOT RECOGNISE; the EMPTY
        // STRING is its answer for a property it recognises and has nothing to report for - which is
        // every property of a DataStore whose data object never loaded.
        //
        // The distinction is not a nicety here: the whole validity test in the oracle is
        // `if data.Describe("DataWindow.Units") = "" then ... 无效的DataObject`
        // [n_cst_thread_task_sqlquery.sru:L553-L555], ported at
        // Tasks/SqlQueryTask.ResolveNamedDataObject. Answering "!" for a store with no definition made
        // that comparison FALSE for every unresolvable name, so the arm was dead: the retrieval carried on
        // and failed later as a DATABASE error, which reached the caller as an HTTP 500 for what is
        // entirely a caller mistake. That the arm exists at all - with its own diagnostic - is the proof
        // that the legacy answered empty there, because otherwise it could never have fired in the legacy
        // either.
        //
        // SO A RECOGNISED PROPERTY ANSWERS EMPTY AND AN UNRECOGNISED ONE STILL ANSWERS "!". The three
        // expression properties already hold the empty string in this state - the DataObject setter's
        // unresolved arm writes them - and the three definition-backed ones answer empty because there is
        // no definition to read. Nothing that reads a describe result is disturbed by the change:
        // Tasks/TaskProxies/SqlTaskProxyHost.ReapplyStoredExpression already treats empty and "!" alike,
        // and NormalizeDescribeSentinel maps "!", "?" and null onto empty for every cached snapshot.
        return property switch
        {
            DataWindowProperty.TableSelect => _sqlSelect,
            DataWindowProperty.TableSort => _sort,
            DataWindowProperty.TableFilter => _filter,
            DataWindowProperty.TableArguments => _definition?.Arguments ?? string.Empty,
            DataWindowProperty.Processing => _definition?.Processing ?? string.Empty,
            DataWindowProperty.Units => _definition?.Units ?? string.Empty,

            // PowerBuilder answers "!" for a property it does not recognise. Preserved as data.
            _ => DescribeFailureSentinel,
        };
    }

    /// <inheritdoc/>
    public long SetFilter(string? filter)
    {
        if (_definition is null)
        {
            return DataWindowBufferStore.DataStoreFailure;
        }

        _filter = filter ?? string.Empty;
        return DataWindowBufferStore.DataStoreSuccess;
    }

    /// <inheritdoc/>
    public long SetSort(string? sort)
    {
        if (_definition is null)
        {
            return DataWindowBufferStore.DataStoreFailure;
        }

        _sort = sort ?? string.Empty;
        return DataWindowBufferStore.DataStoreSuccess;
    }

    /// <inheritdoc/>
    public void ClearState() => Carrier.ClearState();

    /// <inheritdoc/>
    public void OnInit(ICarrierParentTask parentTask)
    {
        ArgumentNullException.ThrowIfNull(parentTask);
        Carrier.OnInit(parentTask);
    }

    /// <inheritdoc/>
    public ValueTask<long> RetrieveAsync(
        IReadOnlyList<object?> parameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        return _runtime.RetrieveAsync(this, parameters, cancellationToken);
    }
}

/// <summary>
/// The default <see cref="ISqlDataStoreFactory"/>, which routes every carrier creation through the
/// sibling affinity-keyed factory.
/// </summary>
/// <remarks>
/// <b>Constraint C-K - this is the single encoding of the affinity decision for the whole Tasks/
/// folder.</b> The legacy repeats the same <c>if #ParentThread.of_IsMainThread()</c> branch at THREE
/// sites - <c>n_cst_thread_task_sqlbase.sru:L553-L557</c>,
/// <c>n_cst_thread_task_sqlquery.sru:L541-L548</c> and
/// <c>n_cst_thread_task_sqlupdate.sru:L307-L314</c> - and every one of them is subject to the
/// <c>_mt</c> naming trap. Funnelling all three through
/// <see cref="SqlTaskBase.CreateDataStore"/> and then through here means the trap is encoded once and
/// cannot be got wrong twice.
/// </remarks>
internal sealed class SqlDataStoreFactory : ISqlDataStoreFactory
{
    private readonly IDataObjectRuntime _runtime;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates the factory.
    /// </summary>
    /// <param name="runtime">The DataWindow runtime handed to every store this factory builds.</param>
    /// <param name="timeProvider">
    /// The service's single clock seam, forwarded to the carrier - which needs it for the
    /// notification throttle the worker carrier reproduces from
    /// <c>n_cst_thread_task_sqlbase_ds_mt.sru:L37-L38</c>. Constraint C-H: no ambient clock is read
    /// anywhere in this file or in anything it constructs.
    /// </param>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    internal SqlDataStoreFactory(IDataObjectRuntime runtime, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _runtime = runtime;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public ISqlDataStore Create(CarrierThreadAffinity affinity) =>
        new SqlDataObjectStore(DataWindowCarrierFactory.Create(affinity, _timeProvider), _runtime);
}

#endregion

#region The retrieval hook - one event, and a fallback contract that is load-bearing

/// <summary>
/// The retrieval extension point - the managed port of
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_hook.sru</c>.
/// </summary>
/// <remarks>
/// <para>
/// That object is 22 lines long, is declared <c>from nonvisualobject</c> [<c>:L4</c>], is marked
/// <c>[运行在子线程]</c> - worker-thread affinity - and declares EXACTLY ONE member, an event:
/// </para>
/// <code>
/// event type long onretrieve ( n_cst_thread_task_sqlbase task,
///                              n_cst_thread_trans transobject,
///                              datastore data )                    [:L9]
/// </code>
/// <para>
/// It carries no state, no function and no other event, so this interface carries exactly one method.
/// </para>
/// <para>
/// <b>⚠ THE FALLBACK SEMANTICS ARE LOAD-BEARING, AND THEY INVERT THE OBVIOUS READING.</b> From
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L755-L761</c>:
/// </para>
/// <code>
/// if IsValid(hook) then nRowCnt = hook.Event OnRetrieve(this,TransObject,Data) else SetNull(nRowCnt)
/// if IsNull(nRowCnt) or nRowCnt = RetCode.E_NO_IMPLEMENTATION then
///     ... run the DEFAULT retrieval ...
/// </code>
/// <para>
/// So a hook answering <see cref="RetCode.E_NO_IMPLEMENTATION"/> - or answering nothing at all - means
/// <b>"NOT HANDLED, USE THE DEFAULT"</b>. It is emphatically NOT an error, and treating it as one
/// would turn every unhandled case into a failure. Use
/// <see cref="SqlTaskBase.HookDeclinedRetrieval"/> to test a result rather than open-coding the
/// comparison.
/// </para>
/// <para>
/// A hook that DOES handle the retrieval answers a row count, on the DataWindow convention: negative
/// means failure [<c>:L777</c>], zero or more is the number of rows retrieved.
/// </para>
/// <para>
/// <b>Lifetime.</b> The legacy creates the hook from a class name at <c>:L516-L517</c> and destroys it
/// in the query task's <c>finally</c> at <c>:L873</c>. The .NET analogue is DETERMINISTIC disposal,
/// per hazard 2 of <c>docs/PB多线程绕坑提示.md</c>: a hook that implements
/// <see cref="IDisposable"/> is disposed by
/// <see cref="SqlTaskBase.ReleaseRetrievalHook"/> rather than being left to the collector.
/// </para>
/// </remarks>
internal interface ISqlRetrievalHook
{
    /// <summary>
    /// Performs, or declines to perform, the retrieval.
    /// </summary>
    /// <param name="task">
    /// The owning worker task - the legacy <c>task</c> parameter, passed as <c>this</c>
    /// [<c>n_cst_thread_task_sqlquery.sru:L756</c>].
    /// </param>
    /// <param name="transaction">
    /// The attached pooled transaction - the legacy <c>transobject</c> parameter.
    /// </param>
    /// <param name="data">
    /// The result carrier - the legacy <c>data</c> parameter, declared there as <c>datastore</c>.
    /// </param>
    /// <returns>
    /// A row count when the hook handled the retrieval, or <see cref="RetCode.E_NO_IMPLEMENTATION"/>
    /// to decline and let the default retrieval run.
    /// </returns>
    long OnRetrieve(SqlTaskBase task, IPooledTransaction transaction, DataWindowCarrier data);
}

/// <summary>
/// Turns a caller-supplied hook CLASS NAME into a hook instance, safely.
/// </summary>
/// <remarks>
/// <b>Constraint C-G.</b> The legacy writes <c>hook = Create Using _sHookClass</c>
/// [<c>n_cst_thread_task_sqlquery.sru:L516-L517</c>] where the name arrives through the public setter
/// <c>of_sethookclass</c> [<c>:L428</c>], which contract C-05 republishes as
/// <c>QuerySpec.hook_class</c> [<c>persistence.v1.proto</c>, field 5]. A name reaching that setter is
/// therefore CALLER-CONTROLLED INPUT, and activating an arbitrary type from it would be a remote type
/// activation primitive. Activation is consequently restricted, validated, and fails cleanly rather
/// than throwing into the request path.
/// </remarks>
internal interface ISqlRetrievalHookActivator
{
    /// <summary>
    /// Attempts to create the hook named by <paramref name="className"/>.
    /// </summary>
    /// <param name="className">The class name, as supplied by the caller.</param>
    /// <param name="hook">Receives the hook on success, or <see langword="null"/> on failure.</param>
    /// <returns>
    /// <see langword="true"/> when a hook was created. <see langword="false"/> reproduces the legacy
    /// state in which <c>IsValid(hook)</c> is false, whereupon the default retrieval runs - the same
    /// outcome as a hook that declines.
    /// </returns>
    bool TryCreate(string? className, [NotNullWhen(true)] out ISqlRetrievalHook? hook);

    /// <summary>
    /// Whether <paramref name="className"/> names a SANCTIONED hook.
    /// </summary>
    /// <param name="className">The class name to test.</param>
    /// <returns>
    /// <see langword="true"/> only when the name is non-blank and has been registered.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>THIS IS THE ADMISSION TEST, AND IT EXISTS SO A REFUSAL CAN HAPPEN AT THE SETTER.</b> The
    /// activator is the allowlist, so it is the only component that can answer whether a caller-supplied
    /// name is sanctioned. Without this member the setter had no way to ask, so an unsanctioned name was
    /// stored, silently ignored at retrieval, and the retrieval then ran with NO hook - which reads to
    /// the caller as "your hook ran and did nothing" rather than "your hook was rejected".
    /// </para>
    /// <para>
    /// A blank name answers <see langword="false"/> here while remaining perfectly legal at the setter:
    /// blank means "no hook" [<c>n_cst_thread_task_sqlquery.sru:L516</c>], which is the ordinary case and
    /// not a name at all. Callers must therefore test blankness before consulting this member.
    /// </para>
    /// </remarks>
    bool IsRegistered(string? className);
}

/// <summary>
/// The default <see cref="ISqlRetrievalHookActivator"/>: activation restricted to a registered,
/// closed set of hook types.
/// </summary>
/// <remarks>
/// <para>
/// <b>An allowlist, not a reflective type lookup.</b> Resolving a caller-supplied string through
/// <c>Type.GetType</c> would let a request name any loadable type in the process, so this activator
/// only ever hands back a hook whose name was REGISTERED by the composition root. That inverts the
/// trust direction: the caller selects among hooks the deployment has already sanctioned, which is the
/// legacy capability - a deployment installing its own retrieval implementation - without the legacy's
/// unrestricted activation.
/// </para>
/// <para>
/// Comparison is <see cref="StringComparer.Ordinal"/> because a PowerBuilder class name is a fixed
/// token and case-insensitive matching would widen the allowlist for no behavioural gain.
/// </para>
/// </remarks>
internal sealed class SqlRetrievalHookActivator : ISqlRetrievalHookActivator
{
    private readonly Dictionary<string, Func<ISqlRetrievalHook>> _registrations = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a hook factory under the class name callers will name it by.
    /// </summary>
    /// <param name="className">The class name a caller may supply.</param>
    /// <param name="factory">Creates a fresh hook per activation.</param>
    /// <returns>
    /// <see cref="RetCode.OK"/> on success, <see cref="RetCode.E_INVALID_ARGUMENT"/> for a blank
    /// name, or <see cref="RetCode.E_BUSY"/> when the name is already registered - which is refused
    /// rather than overwritten so that a later registration cannot silently displace an earlier one.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    internal long Register(string? className, Func<ISqlRetrievalHook> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        if (string.IsNullOrWhiteSpace(className))
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        return _registrations.TryAdd(className, factory) ? RetCode.OK : RetCode.E_BUSY;
    }

    /// <summary>
    /// Whether <paramref name="className"/> names a registered hook.
    /// </summary>
    /// <param name="className">The class name to test.</param>
    /// <returns><see langword="true"/> when the name is registered.</returns>
    /// <inheritdoc/>
    public bool IsRegistered(string? className) =>
        !string.IsNullOrWhiteSpace(className) && _registrations.ContainsKey(className);

    /// <inheritdoc/>
    public bool TryCreate(string? className, [NotNullWhen(true)] out ISqlRetrievalHook? hook)
    {
        hook = null;

        // `if _sHookClass <> "" then` [n_cst_thread_task_sqlquery.sru:L516]: an unset class name is
        // the ordinary case and simply means "no hook", never an error.
        if (string.IsNullOrWhiteSpace(className))
        {
            return false;
        }

        if (!_registrations.TryGetValue(className, out Func<ISqlRetrievalHook>? factory))
        {
            return false;
        }

        hook = factory();
        return hook is not null;
    }
}

#endregion

#region The parameter model and the datastore-cache entry

/// <summary>
/// One SQL parameter - the port of the nested structure
/// <c>sqlparam { string name; any value }</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L21-L24</c>].
/// </summary>
/// <param name="Name">
/// The parameter name, ALWAYS LOWER-CASED on entry [<c>:L256, :L345, :L499</c>]. The empty string
/// means a POSITIONAL parameter, and that is not an absence: the binder treats an empty name as
/// "match the next unreplaced placeholder" and stops after the first substitution
/// [<c>:L460, :L472</c>].
/// </param>
/// <param name="Value">
/// The value, the legacy <c>any</c>. May be <see langword="null"/>, which the scalar literal path
/// renders as <c>NULL</c> [<c>:L315</c>], and may be an array, which the array literal path joins with
/// commas [<c>:L266-L313</c>].
/// </param>
/// <remarks>
/// Named <c>SqlTaskParameter</c> rather than <c>SqlParameter</c> deliberately: this project references
/// <c>Microsoft.Data.Sqlite</c>, and a type called <c>SqlParameter</c> sitting beside an ADO provider
/// would read as a database command parameter. It is not one - it is the legacy structure, and it is
/// consumed by the literal generator and by the placeholder binder, neither of which binds anything to
/// a provider.
/// </remarks>
internal readonly record struct SqlTaskParameter(string Name, object? Value);

/// <summary>
/// One placeholder found in a statement - the port of the nested structure
/// <c>sqlarg { string name; long pos; long length; boolean replaced }</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L14-L19</c>].
/// </summary>
/// <remarks>
/// <para>
/// Mutable, and a <see langword="struct"/> held in a list that is written back by index, because every
/// field is mutated in place during binding: <c>length</c> and <c>name</c> are filled in when the
/// placeholder's terminating delimiter is reached [<c>:L436-L437</c>], <c>replaced</c> is latched at
/// [<c>:L466</c>], and <c>pos</c> is SHIFTED for every following placeholder each time a substitution
/// changes the statement's length [<c>:L467-L470</c>].
/// </para>
/// <para>
/// <b><see cref="Position"/> is ONE-BASED</b>, because it is a PowerBuilder string offset: the legacy
/// scan runs <c>for nPos = 1 to Len(sql)</c> [<c>:L421</c>] and the substitution splices with
/// <c>Left(sql, pos - 1)</c> and <c>Mid(sql, pos + length)</c> [<c>:L464</c>]. It is converted to a
/// zero-based index exactly once, at the point of splicing.
/// </para>
/// </remarks>
internal struct SqlPlaceholder
{
    /// <summary>The lower-cased placeholder name, without the leading prefix [<c>:L437</c>].</summary>
    internal string Name;

    /// <summary>The ONE-BASED offset of the prefix character itself [<c>:L443</c>].</summary>
    internal long Position;

    /// <summary>
    /// The placeholder's length INCLUDING the prefix character, computed as
    /// <c>nPos - pos</c> where <c>nPos</c> is the terminating delimiter's offset [<c>:L436, :L448</c>].
    /// </summary>
    /// <remarks>
    /// <b>C-07's POSITIONAL FORM SETS THIS AT EMISSION INSTEAD OF AT CLOSURE.</b> A question-mark
    /// placeholder has no name and no terminating delimiter to measure against - the marker IS the whole
    /// placeholder - so it is emitted already complete with a length of one and is never opened. Everything
    /// downstream reads this member identically for both forms, which is what lets one binder serve both.
    /// </remarks>
    internal long Length;

    /// <summary>Whether a parameter has already been substituted here [<c>:L459, :L466</c>].</summary>
    internal bool Replaced;
}

/// <summary>
/// One datastore-cache entry - the port of the nested structure
/// <c>cacheds { n_cst_thread_task_sqlbase_ds ds; string origsql; string origsort; string origfilter }</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L26-L31</c>].
/// </summary>
/// <param name="DataStore">
/// The cached store - the legacy <c>ds</c> member [<c>:L27</c>]. <b>A LIVE REFERENCE, not a scalar</b>,
/// which is why the cache's lifetime is part of the hazard-2 contract.
/// </param>
/// <param name="OrigSql">
/// The statement snapshot taken at first use - the legacy <c>origsql</c> [<c>:L28</c>], captured from
/// <c>GetSQLSelect()</c> at <c>:L560</c>.
/// </param>
/// <param name="OrigSort">
/// The sort snapshot - the legacy <c>origsort</c> [<c>:L29</c>], captured at <c>:L561</c> and
/// sentinel-normalised at <c>:L562</c>.
/// </param>
/// <param name="OrigFilter">
/// The filter snapshot - the legacy <c>origfilter</c> [<c>:L30</c>], captured at <c>:L563</c> and
/// sentinel-normalised at <c>:L564</c>.
/// </param>
/// <remarks>
/// The four members are declared in the legacy's own order so the correspondence can be checked by
/// reading. Immutable because the legacy never rewrites an entry once added: it only ever compares the
/// snapshots against the live store and pushes the snapshots back into it.
/// </remarks>
internal sealed record CachedDataStore(
    ISqlDataStore DataStore,
    string OrigSql,
    string OrigSort,
    string OrigFilter);

#endregion

#region SqlTaskBase - the worker-thread base of the SQL task layer

/// <summary>
/// The shared WORKER-THREAD base of the Persistence SQL task layer - the managed port of
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru</c>, which derives
/// <c>from n_cst_thread_task</c> and is marked <c>[运行在子线程]</c>.
/// </summary>
/// <remarks>
/// <para>
/// Derived by <c>SqlQueryTask</c>, <c>SqlUpdateTask</c> and <c>SqlCommandTask</c>, each of which chains
/// to <see cref="Reset"/>. It owns five responsibilities, all verified against the oracle:
/// the transaction descriptor and its pooled transaction; the commit signal and its backward
/// propagation; the SQL parameter collection with its literal generator and placeholder binder; the
/// per-thread datastore cache; and the lifecycle hooks whose ORDERING is contract.
/// </para>
/// <para>
/// <b>This is the worker half of a deliberate pair.</b> Its caller-side counterpart is
/// <c>n_cst_threading_task_sqlbase</c> (<c>[运行在当前线程]</c>), modelled here as
/// <see cref="ISqlTaskProxy"/>. The two are registered as DISTINCT types and are never merged
/// (constraints C-J, C-K) - see this file's header for the affinity census and the reason.
/// </para>
/// <para>
/// <b>Everything is injected, nothing is ambient (constraint C-H).</b> The clock is a
/// <see cref="TimeProvider"/>, the pool is the sibling <see cref="TransactionPool"/> with its own
/// activator seam, the store factory and the DataWindow runtime are interfaces, and the threading
/// substrate is <see cref="ISqlTaskHost"/>. Consequently the whole of this type - including the
/// preserved cache defect and the one-based parameter upsert - is exercisable with no real thread and
/// no database.
/// </para>
/// </remarks>
internal abstract class SqlTaskBase : ICarrierParentTask, IDisposable
{
    // ---------------------------------------------------------------------------------------------
    //  Legacy constant VALUES, under PascalCase identifiers.
    //  The .editorconfig naming suppressions are scoped to other files (AAP §0.4.5.3), so this file
    //  preserves each VALUE while spelling the identifier the way C# spells identifiers.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The SQL placeholder prefix - the legacy <c>constant string ARG_PREFIX = ":"</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L396</c>], whose own comment reads SQL参数占位符的前缀.
    /// </summary>
    /// <remarks>
    /// The VALUE is preserved exactly; the legacy SCREAMING_SNAKE SPELLING cannot be, because this file
    /// is outside the .editorconfig globs that suppress the naming analyzers for preserved constants.
    /// A colon also appears in the binder's own delimiter set [<c>:L424</c>], which is what makes a
    /// placeholder terminate the moment another colon is seen.
    /// </remarks>
    private const string ArgPrefix = ":";

    /// <summary>
    /// C-07's SECOND placeholder form - the positional question mark.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THIS CHARACTER IS NOT IN THIS OBJECT'S ORACLE, AND IT IS IN C-07's.</b> The scan this file
    /// ports treats <c>?</c> as a WORD DELIMITER ONLY - it appears in the delimiter list at
    /// <c>n_cst_thread_task_sqlbase.sru:L424</c> and the arm that opens a placeholder tests
    /// <c>if sWord = ARG_PREFIX</c> [<c>:L440</c>], so a bare question mark closes whatever was open and
    /// opens nothing. That is faithful to THIS object, whose whole parameter surface is named
    /// (<c>of_addparam(name, value)</c> [<c>:L67</c>]).
    /// </para>
    /// <para>
    /// <b>C-07 IS THE UNION OF TWO COMMAND SURFACES, AND THE SECOND ONE IS POSITIONAL.</b> The other
    /// half is the SQLite binding's own <c>Exec</c>, overloaded from zero to eleven ANONYMOUS arguments
    /// [<c>ws_objects/pfw.utility.sqlite.pbl.src/n_sqlite.sru:L32-L43</c>], and the legacy's primary
    /// SQLite fixture calls it with question marks - <c>"@INSERT INTO COMPANY (NAME,AGE,ADDRESS,SALARY,
    /// BIRTH) VALUES (?, ?, ?, ?, ?)"</c> with five values, inside a ten-iteration loop
    /// [<c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L396-L406</c>] - and again at <c>:L313</c> for
    /// an update. AAP §0.4.3 states C-07 "preserves positional <c>?</c> binding" and
    /// <c>persistence.v1.proto</c> records that "positional <c>?</c> substitution consumes the order".
    /// Supporting only the colon form left the oracle's own fixture unreplayable, which also blocks the
    /// paired characterization AAP §0.6.7 requires.
    /// </para>
    /// <para>
    /// <b>WHY IT NEEDED NO NEW MATCHING RULE.</b> A question mark carries NO NAME, and the substitution
    /// loop already matches "any unreplaced placeholder" for a parameter whose name is empty and stops
    /// after exactly one [<c>:L460</c>, <c>:L471-L472</c>] - which is precisely positional consumption in
    /// order. So the locator emits a question mark as an ALREADY-COMPLETE placeholder with an empty name,
    /// and every rule after it - the mixed-form refusal, the per-parameter bound name, the two offset
    /// tracks, the unmatched-placeholder check - applies unchanged. A NAMED parameter can never match one,
    /// because its name is non-empty and the comparison is ordinal.
    /// </para>
    /// </remarks>
    private const char PositionalArgMarker = '?';

    /// <summary>
    /// The length of a positional placeholder, in characters: the marker and nothing else.
    /// </summary>
    /// <remarks>
    /// Named rather than written as <c>1</c> at the emission site because
    /// <see cref="SqlPlaceholder.Length"/> is the value BOTH splice tracks subtract from and shift by, so
    /// the number is load-bearing arithmetic rather than a magic constant: the colon form's length is
    /// computed from its terminating delimiter [<c>:L436</c>], and the positional form's is the marker's
    /// own width because the marker IS the whole placeholder.
    /// </remarks>
    private const long PositionalArgMarkerLength = 1L;

    /// <summary>
    /// The per-thread key the datastore cache lives under -
    /// <c>#ParentThread.of_HasData("$SQL.DataStoreCache")</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L531, :L536</c>].
    /// </summary>
    private const string DataStoreCacheKey = "$SQL.DataStoreCache";

    /// <summary>
    /// The per-thread key the transaction pool lives under -
    /// <c>#ParentThread.of_HasData("$SQL.TransPool")</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L194, :L195, :L205</c>].
    /// </summary>
    private const string TransactionPoolKey = "$SQL.TransPool";

    /// <summary>
    /// The configuration key naming the POOL class -
    /// <c>of_GetDataString("$SQL.TransPool.Class")</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L198</c>].
    /// </summary>
    /// <remarks>
    /// <b>⚠ TWO SIMILARLY NAMED KEYS, AND THEY MEAN DIFFERENT THINGS (constraint C-K).</b> This one is
    /// <c>$SQL.TransPool.Class</c> and selects the POOL implementation. The pool object itself reads a
    /// DIFFERENT key, <c>$SQL.TransPool.TransClass</c>, to select the TRANSACTION implementation
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L83</c>]. Conflating them
    /// would make a deployment that customised one silently customise the other.
    /// </remarks>
    private const string TransactionPoolClassKey = "$SQL.TransPool.Class";

    /// <summary>
    /// The separator between one argument pair and the next in a DataWindow argument list
    /// [<c>n_cst_thread_task_sqlbase.sru:L576, :L577, :L588</c>].
    /// </summary>
    private const char ArgumentPairSeparator = '\n';

    /// <summary>
    /// The separator between an argument's name and its type
    /// [<c>n_cst_thread_task_sqlbase.sru:L580</c>].
    /// </summary>
    private const char ArgumentNameTypeSeparator = '\t';

    /// <summary>
    /// The value the database-error event returns - <c>return 3</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L97</c>].
    /// </summary>
    /// <remarks>
    /// <b>Constraint C-B: this is DELIBERATELY OUTSIDE THE RETURN-CODE ALGEBRA and is preserved as the
    /// bare literal it is.</b> Three is not <see cref="RetCode.OK"/>, not
    /// <see cref="RetCode.PREVENT"/> and not any other member of that catalogue - it is the DataWindow
    /// <c>dberror</c> event's own vocabulary, where 3 means "suppress the runtime's own error message
    /// and let the script handle it". Mapping it onto a <see cref="RetCode"/> value would be a silent
    /// behavioural change, and testing it with <see cref="Predicates.IsSucceeded(long?)"/> would be a
    /// category error.
    /// </remarks>
    internal const long DbErrorEventResult = 3L;

    // ---------------------------------------------------------------------------------------------
    //  Injected collaborators. Every one is an abstraction, which is what constraint C-H buys.
    // ---------------------------------------------------------------------------------------------

    private readonly ISqlTaskHost _host;
    private readonly TransactionPool _transactionPool;
    private readonly ISqlDataStoreFactory _dataStoreFactory;
    private readonly ISqlRetrievalHookActivator _hookActivator;
    private readonly ILogger _logger;

    // ---------------------------------------------------------------------------------------------
    //  Instance state, mirroring the legacy private block [n_cst_thread_task_sqlbase.sru:L39-L53].
    //  `n_cst_thread_trans_pool _transPool` [:L41] has no field here: the pool is injected rather
    //  than lazily created, for the reason set out on GetTransactionPool.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Mirrors <c>TRANSACTIONDATA _transData</c> [<c>:L43</c>].</summary>
    private TransactionData _transactionData;

    /// <summary>Mirrors <c>boolean _bNCharBinding</c> [<c>:L45</c>].</summary>
    private bool _nCharBinding;

    /// <summary>
    /// Mirrors <c>SQLPARAM _sqlParams[]</c> [<c>:L47</c>] - a PowerBuilder one-based array, so every
    /// index exposed to a caller is one-based and every access here goes through
    /// <see cref="OneBasedIndex"/>.
    /// </summary>
    private readonly List<SqlTaskParameter> _sqlParams = [];

    /// <summary>
    /// Mirrors <c>int _nTransRefIdx</c> [<c>:L49</c>]; an invalid handle means "no reference held".
    /// </summary>
    /// <remarks>
    /// <b>A STABLE POOL LEASE RATHER THAN THE ONE-BASED POSITION, AND THE REASON IS DECOMPOSITION.</b> The
    /// legacy field IS the position, and the legacy pool renumbers every later position when an entry is
    /// removed [<c>n_cst_thread_trans_pool.sru:L114</c>] - a preserved defect the pool's positional API
    /// still reproduces byte for byte. It was unreachable in the legacy because a task released and zeroed
    /// its index in the same breath [<c>:L123</c>], so it never carried a stale one. A server-held task,
    /// however, is created by one request and retrieved, paged and destroyed by later ones, so an
    /// UNRELATED holder's release now renumbers this task's reference and would silently repoint it at
    /// somebody else's transaction. The lease is monotonic and never reused, so an outlived reference
    /// resolves NOTHING instead of resolving a stranger. Every guard below keeps its shape: the legacy's
    /// <c>&gt; 0</c> becomes <c>IsValid</c> and its <c>= 0</c> becomes <see cref="PoolLease.None"/>.
    /// </remarks>
    private PoolLease _transactionLease;

    /// <summary>
    /// Mirrors <c>n_cst_thread_trans _transObject</c> [<c>:L50</c>]. Nullable because the legacy
    /// explicitly <c>SetNull</c>s it - hazard 2 - at <c>:L124</c>, <c>:L143</c> and <c>:L718</c>.
    /// </summary>
    private IPooledTransaction? _transactionObject;

    /// <summary>
    /// Mirrors <c>ulong _hEvtCommitted</c> [<c>:L52</c>]. A handle value of zero means "not created",
    /// which maps onto <see langword="null"/> here; the legacy tests <c>&lt;&gt; 0</c> at
    /// <c>:L106</c>, <c>:L224</c>, <c>:L242</c>, <c>:L720</c> and <c>:L725</c>.
    /// </summary>
    private ManualResetEventSlim? _committedSignal;

    private bool _disposed;

    /// <summary>
    /// Creates the task.
    /// </summary>
    /// <param name="host">The threading substrate this task runs on.</param>
    /// <param name="transactionPool">
    /// The shared pool. Injected rather than lazily created - see
    /// <see cref="GetTransactionPool"/> for how that discharges <c>of_GetTransPool</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L190-L209</c>].
    /// </param>
    /// <param name="dataStoreFactory">Creates affinity-appropriate result carriers.</param>
    /// <param name="hookActivator">Turns a caller-supplied hook class name into a hook, safely.</param>
    /// <param name="timeProvider">
    /// The service's single clock seam. <b>Constraint C-H: this is the ONLY clock this file may read.</b>
    /// </param>
    /// <param name="logger">
    /// Diagnostics sink. <b>Constraint C-F: nothing written through it may carry an unredacted
    /// statement or the descriptor's write-only password.</b>
    /// </param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    protected SqlTaskBase(
        ISqlTaskHost host,
        TransactionPool transactionPool,
        ISqlDataStoreFactory dataStoreFactory,
        ISqlRetrievalHookActivator hookActivator,
        TimeProvider timeProvider,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(transactionPool);
        ArgumentNullException.ThrowIfNull(dataStoreFactory);
        ArgumentNullException.ThrowIfNull(hookActivator);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _host = host;
        _transactionPool = transactionPool;
        _dataStoreFactory = dataStoreFactory;
        _hookActivator = hookActivator;
        Clock = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// The service's single clock seam, exposed to derived tasks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Constraint C-H: this is the ONLY clock any task in this folder may read.</b> Nothing in this
    /// file reads <c>DateTime.UtcNow</c>, <c>DateTime.Now</c>, <c>Environment.TickCount</c> or a
    /// <c>Stopwatch</c>, and a derived task must not either - a task layer that reads an ambient clock
    /// cannot be characterized, and the paired legacy-versus-target recordings the parity model depends
    /// on would stop being comparable.
    /// </para>
    /// <para>
    /// <b>It is exposed because derived tasks genuinely need it, not speculatively.</b> The legacy reads
    /// <c>CPU()</c> to throttle update-progress notifications
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru:L37-L38, :L53</c>] and
    /// again for the pool's idle expiry [<c>n_cst_thread_trans_pool.sru</c>], and both of those clock
    /// reads sit under this base. The shape deliberately matches the sibling carrier's own
    /// <c>protected TimeProvider Clock</c> in <c>Buffers/DataWindowBuffers.cs</c>, so one convention
    /// covers the whole service.
    /// </para>
    /// </remarks>
    protected TimeProvider Clock { get; }

    #region ICarrierParentTask - what the result carrier is allowed to ask its owning task

    /// <inheritdoc/>
    /// <remarks>
    /// Forwards <c>#ParentThread.of_IsMainThread()</c> so the carrier can record its parent thread's
    /// affinity in <c>OnInit</c> [<c>n_cst_thread_task_sqlbase_ds.sru:L63</c>].
    /// </remarks>
    public bool IsMainThread => _host.IsMainThread;

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The port of <c>of_IsNCharBinding()</c> [<c>n_cst_thread_task_sqlbase.sru:L524</c>], read by the
    /// carrier's SQL-preview interception to decide whether to rewrite string literals into N-char
    /// form [<c>n_cst_thread_task_sqlbase_ds.sru:L169-L170</c>].
    /// </para>
    /// <para>
    /// <b>THE REWRITER ITSELF IS NOT HERE.</b> It belongs to <c>Buffers/DataWindowBuffers.cs</c>,
    /// matching the legacy where <c>_ds</c> declares <c>_of_replacencharliteral</c> privately
    /// [<c>n_cst_thread_task_sqlbase_ds.sru:L59</c>]. This property SUPPLIES the flag; it does not
    /// reimplement the transform.
    /// </para>
    /// </remarks>
    public bool IsNCharBinding => _nCharBinding;

    /// <inheritdoc/>
    public bool IsCancelled => _host.IsCancelled;

    /// <summary>
    /// Whether this task currently holds a reference to a pooled transaction - the direct read of
    /// <c>IsValidObject(_transObject)</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L211</c>].
    /// </summary>
    /// <remarks>
    /// <b>A testability seam, and a narrow one on purpose (constraint C-H).</b> It exists because
    /// hazard 2 is a claim about a reference being CLEARED, and a claim about absence cannot be verified
    /// through <see cref="HasAliveTrans"/>, which also consults the pool and would answer
    /// <see langword="true"/> for a task that had correctly let go. It reports state and changes none.
    /// </remarks>
    internal bool HasTransactionReference => Predicates.IsValidObject(_transactionObject);

    /// <summary>
    /// The pooled transaction this task most recently acquired - the direct read of the oracle's
    /// <c>_transObject</c> field
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L170</c>], or
    /// <see langword="null"/> when no acquisition has succeeded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>AN OBSERVATION SEAM, AND IT REPORTS STATE WITHOUT CHANGING ANY.</b> It exists because a task
    /// body's outcome is only half the story: the oracle's own task body reads the driver's four
    /// accessors off THE INSTANCE IT JUST ACQUIRED - <c>transObject.SQLDBCode</c>,
    /// <c>transObject.SQLErrText</c> [<c>:L110-L111</c>] - and a caller that has to project those onto a
    /// wire response needs the same instance. Nothing else on this type surfaces it:
    /// <see cref="HasTransactionReference"/> answers only whether one is held, and
    /// <see cref="GetTransObject(ref IPooledTransaction?)"/> is <see langword="protected"/> and would
    /// RE-ACQUIRE, which clears the very state the caller is trying to read.
    /// </para>
    /// <para>
    /// <b>WHY THE ACQUIRED INSTANCE AND NOT THE ONE A CALLER ALREADY HOLDS.</b> The pool REPLACES a
    /// broken entry's transaction and DISPOSES the old one
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L164-L172</c>], so a reference
    /// captured before a task ran can be both stale and disposed by the time it returns. This property
    /// always answers whatever the acquisition sequence actually attached, which is the only reading
    /// that matches the oracle's local.
    /// </para>
    /// </remarks>
    internal IPooledTransaction? AttachedTransaction => _transactionObject;

    /// <summary>
    /// Whether the commit signal has been created yet - the direct read of
    /// <c>_hEvtCommitted &lt;&gt; 0</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L106, :L242, :L720</c>].
    /// </summary>
    /// <remarks>
    /// <b>A testability seam (constraint C-H).</b> Three behaviours turn on the signal's mere EXISTENCE
    /// rather than on its state - the committed handler skips a task without one [<c>:L106</c>],
    /// <see cref="Reset"/> and <see cref="OnPrepare"/> tolerate its absence rather than forcing it into
    /// being, and <see cref="OnUninit"/> closes it only if it is there - and none of them is observable
    /// through <see cref="IsCommitted"/>, which answers <see langword="false"/> in both cases.
    /// </remarks>
    internal bool HasCommitSignal => _committedSignal is not null;

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The port of <c>event type long ondberror(...)</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L85-L98</c>], which packs the five scalars into a
    /// <c>DBERRORDATA</c>, forwards it to <c>#ParentTasking</c> and then returns 3.
    /// </para>
    /// <para>
    /// <b>Constraint C-F.</b> The statement reaches the log only through
    /// <see cref="ISqlRedactor.Redact(string)"/>, and the payload reaches the wire only through
    /// <c>DbErrorData.ToDbError()</c>, which masks it unconditionally and takes no policy argument that
    /// a call site could weaken. The descriptor's password is never touched by either path.
    /// </para>
    /// </remarks>
    public long OnDbError(long sqlDbCode, string sqlErrText, string sqlSyntax, DwBuffer buffer, long row)
    {
        // [:L86-L92] the five members are copied in the structure's own declaration order
        // [dberrordata.srs:L4-L8]; the factory writes exactly those five and nothing else.
        DbErrorData error = DbErrorData.FromStatement(sqlDbCode, sqlErrText, sqlSyntax, buffer, row);

        // [:L94-L95] `tasking = #ParentTasking` then `tasking.Event OnDBError(err)`. The legacy assigns
        // into a typed local and triggers unconditionally; an unattached task has no proxy to receive
        // the event, so validity is checked with the same predicate the rest of the port uses.
        ISqlTaskProxy? proxy = _host.ParentTasking;
        if (Predicates.IsValidObject(proxy))
        {
            proxy!.OnDbError(in error);
        }

        // C-F. The redacted projection is what is logged - never error.SqlSyntax itself, which carries
        // the complete generated statement including interpolated literal values whenever the
        // connection disabled bind variables. The legacy logger performs no redaction at all; adding
        // it changes no observable statement, only what leaves the process in a log record.
        //
        // THE PROVIDER'S ERROR TEXT IS MASKED TOO, and it used to go through nothing at all. It reads like
        // a diagnostic rather than a statement, which is exactly why it was trusted - but a provider
        // composes that text FROM the statement it was executing, so it habitually quotes the offending
        // fragment back: a constraint violation names the value that violated it, a type mismatch names
        // the value that would not convert, and a syntax error echoes the surrounding text. Those are the
        // interpolated literals arriving by a second route, so the same rule applies.
        //
        // 🔴 BUT THROUGH THE PROVIDER-ENVELOPE RULE, NOT THE STRICT SCAN, AND THE CLAIM THAT USED TO STAND
        //    HERE - "the code, the constraint name and the column name are not literals and survive" - WAS
        //    MEASURABLY FALSE. Microsoft.Data.Sqlite does not hand back a bare diagnostic: it wraps one as
        //    `SQLite Error 19: '<message>'.`, so the strict scan read the result code as a numeric literal
        //    and the entire diagnosis as a quoted string and masked both. Every constraint failure was
        //    therefore recorded as `SQLite Error <redacted>: '<redacted>'.` - a log line that says a
        //    database error happened and refuses to say which column caused it, which is exactly what an
        //    operator opens the log for.
        //
        //    THE NARROWING IS THE ENVELOPE ONLY. RedactProviderDiagnostic preserves the wrapper and the
        //    result code and puts the interior through the identical scan, so a value the provider quoted
        //    INSIDE its message is still masked; a text that is not that exact envelope falls through to
        //    the strict method unchanged. The STATEMENT below stays strictly masked, because it is the
        //    field that carries interpolated literals by construction.
        _logger.LogError(
            "Database error {SqlDbCode} in buffer {Buffer} at row {Row}: {SqlErrText}. Statement: {SqlSyntax}",
            error.SqlDbCode,
            error.Buffer,
            error.Row,
            SqlRedactor.Instance.RedactProviderDiagnostic(error.SqlErrText),
            SqlRedactor.Instance.Redact(error.SqlSyntax));

        // [:L97] `return 3` - preserved verbatim. See DbErrorEventResult for why it is not a RetCode.
        return DbErrorEventResult;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Forwards the substrate's <c>onnotify</c> so the worker carrier's throttled progress
    /// notifications reach the caller-side proxy
    /// [<c>n_cst_thread_task_sqlbase_ds_mt.sru:L39-L41</c>].
    /// </remarks>
    public long OnNotify(long notifyCode, long payload, string text) =>
        _host.OnNotify(notifyCode, payload, text);

    #endregion

    #region The transaction descriptor, the pool, and the pooled transaction

    /// <summary>
    /// Installs the connection descriptor - the port of <c>of_settransdata</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L113-L135</c>].
    /// </summary>
    /// <param name="transactionData">
    /// The descriptor. Taken by <see langword="in"/> because the legacy parameter is
    /// <c>readonly transactiondata</c>.
    /// </param>
    /// <returns><see cref="RetCode.OK"/> - the legacy has no failing arm here.</returns>
    /// <remarks>
    /// <para>
    /// <b>The whole-structure equality early return is preserved, INCLUDING its asymmetry.</b> The
    /// legacy compares the STORED descriptor - which has already had its auto-commit erased - against
    /// the INCOMING one [<c>:L114</c>]. So passing the same descriptor twice with auto-commit set does
    /// NOT short-circuit the second time: the stored copy reads false while the incoming copy reads
    /// true, they compare unequal, and the whole body runs again. That is legacy behaviour and it is
    /// reproduced rather than "improved" by comparing connection fields only (constraint C-B). Value
    /// equality is available because <see cref="TransactionData"/> is the pool key and canonicalises
    /// every spelling of an empty member.
    /// </para>
    /// <para>
    /// <b>The auto-commit erasure at <c>:L119</c> is deliberate</b>, and the legacy says why in its own
    /// comment: 擦除连接目标无关的参数 - erase parameters irrelevant to the CONNECTION TARGET. Auto-commit
    /// is a session behaviour, not part of a connection's identity, so leaving it in would split one
    /// pooled connection into two entries that differ only by it.
    /// </para>
    /// </remarks>
    internal long SetTransData(in TransactionData transactionData)
    {
        // [:L113] `//if #Running then return RetCode.E_BUSY`
        // CARRIED ACROSS INERT (constraint C-B). The oracle's busy guard is commented out, so this
        // function really does accept a new descriptor mid-run. Reviving it would reject calls the
        // legacy accepts - exactly the silent behavioural change C-B forbids. The busy check that IS
        // live sits on the caller-side proxy [n_cst_threading_task_sqlbase.sru:L110].

        // [:L114] whole-structure equality against the STORED (already erased) descriptor.
        if (_transactionData == transactionData)
        {
            return RetCode.OK;
        }

        // [:L116] store, then [:L118-L119] erase the auto-commit flag. Expressed as one `with` because
        // TransactionData is an immutable record struct; the observable result is identical to the
        // legacy's assign-then-overwrite.
        _transactionData = transactionData with { AutoCommit = false };

        // [:L121-L125] a descriptor change invalidates any transaction already borrowed for the old
        // one: drop the pool reference, zero the index, and NULL THE TRANSACTION REFERENCE.
        // The null is hazard 2 [docs/PB多线程绕坑提示.md:L5] and is deterministic, not a collector hint.
        if (_transactionLease.IsValid)
        {
            GetTransactionPool().RemoveRef(_transactionLease);
            _transactionLease = PoolLease.None;
            _transactionObject = null;
        }

        ApplyConnectionParameterFlags();

        // [:L134]
        return RetCode.OK;
    }

    /// <summary>
    /// Reads the installed descriptor - the port of <c>of_gettransdata</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L216-L217</c>].
    /// </summary>
    /// <returns>
    /// The stored descriptor, whose auto-commit member always reads <see langword="false"/> because
    /// <see cref="SetTransData"/> erased it.
    /// </returns>
    /// <remarks>
    /// <b>Constraint C-F.</b> The returned descriptor carries a password member that is WRITE-ONLY by
    /// contract: it participates in equality and in connecting, and in no rendering, logging or
    /// response path. <see cref="TransactionData.ToString"/> already omits it, so an accidental
    /// interpolation cannot leak it.
    /// </remarks>
    internal TransactionData GetTransData() => _transactionData;

    /// <summary>
    /// Resolves the two connection-parameter flags - the port of
    /// <c>n_cst_thread_task_sqlbase.sru:L127-L132</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE PARSE ITSELF IS NOT DUPLICATED HERE, AND THAT IS THE POINT.</b>
    /// <see cref="TransactionData.ResolveDbParmFlags"/> is the single reproduction of
    /// <c>:L127-L132</c>, and it PRESERVES THE NESTING: the national-character flag is consulted only
    /// INSIDE the bind-disabling branch. This file therefore declares no regular expression and no
    /// second parser - a duplicate would be free to drift from the original, and one of the two copies
    /// would eventually be flattened into a conjunction. Verified against the sibling on integration:
    /// its implementation nests the two tests exactly as <c>:L128-L129</c> does.
    /// </para>
    /// <para>
    /// <b>A flat conjunction is NOT structurally equivalent (constraint C-B).</b> It computes the same
    /// answer while hiding that the second flag is SUBORDINATE to the first, so
    /// <c>NCharBind=1</c> alone is legal and completely inert. Honouring it independently would change
    /// generated statements for every caller who set it without the first.
    /// </para>
    /// <para>
    /// <b>WHERE THE SQL-INJECTION EXPOSURE COMES FROM (constraints C-B, C-F; AAP §0.6.4).</b>
    /// <c>DisableBind=1</c> means PowerBuilder DOES NOT USE BIND VARIABLES - values are INTERPOLATED
    /// INTO THE STATEMENT TEXT AS LITERALS. That is the mechanical root of the exposure, and it is why
    /// <see cref="ParamToString"/> exists at all and why a database error's statement member has to be
    /// redacted before it leaves the process. The .NET implementation uses PARAMETERIZED COMMANDS
    /// internally while preserving the OBSERVABLE generated statement byte for byte: the safer
    /// mechanism is unobservable, so adopting it is permitted, whereas changing the statement text a
    /// caller can see would not be. <b>This is a known legacy defect being DOCUMENTED, NOT
    /// CORRECTED.</b>
    /// </para>
    /// </remarks>
    private void ApplyConnectionParameterFlags()
    {
        // [:L127] the default is established BEFORE any test, and [:L128-L130] the two nested tests are
        // resolved in one pass by the descriptor that owns them.
        _transactionData.ResolveDbParmFlags(out bool isBindDisabled, out bool isNCharBindingEnabled);

        // [:L130] only the nested arm sets it true; [:L127] leaves it false on every other path.
        _nCharBinding = isNCharBindingEnabled;

        if (isBindDisabled)
        {
            _logger.LogWarning(
                "Connection parameters disable bind variables (DisableBind=1), so values are "
                    + "interpolated into statement text as literals. This is preserved legacy "
                    + "behaviour; generated statements are unchanged and error payloads are redacted. "
                    + "National-character binding enabled: {NCharBinding}.",
                isNCharBindingEnabled);
        }
    }

    /// <summary>
    /// Whether a national-character rewrite applies - the port of <c>of_isncharbinding</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L524</c>].
    /// </summary>
    /// <returns><see langword="true"/> only when the connection set BOTH flags.</returns>
    /// <remarks>
    /// Exposed as a function as well as through <see cref="IsNCharBinding"/> because the legacy exposes
    /// it as a function to derived tasks, and the carrier reaches the same fact through
    /// <see cref="ICarrierParentTask"/>. One backing field, two spellings, no second source of truth.
    /// </remarks>
    internal bool IsNCharBindingEnabled() => _nCharBinding;

    /// <summary>
    /// The shared transaction pool - the port of <c>of_gettranspool</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L190-L209</c>].
    /// </summary>
    /// <returns>The injected pool.</returns>
    /// <remarks>
    /// <para>
    /// The legacy performs four things here: it returns a cached pool if it has one [<c>:L192</c>];
    /// otherwise it looks in the owning thread's keyed data under <c>$SQL.TransPool</c>
    /// [<c>:L194-L196</c>]; otherwise it CREATES one, choosing the class named by
    /// <c>$SQL.TransPool.Class</c> and falling back to the default type [<c>:L197-L203</c>]; and it then
    /// fires the pool's init event and publishes it back into the thread's keyed data
    /// [<c>:L204-L205</c>].
    /// </para>
    /// <para>
    /// <b>All four are discharged by injection, and that is a faithful mapping rather than a shortcut.</b>
    /// The container holds exactly one <see cref="TransactionPool"/> for the service, so the caching and
    /// the publish-back are the registration itself; construction and initialisation happen once, in the
    /// container, which is what the init event did; and the POOL-CLASS indirection of
    /// <see cref="TransactionPoolClassKey"/> becomes the registration's choice of implementation, which
    /// is the .NET expression of "configurable pool class". Deliberately NOT invented: an option for the
    /// pool class. <c>PersistenceOptions.TransactionPool</c> declares
    /// <c>TransactionClassName</c>, and that setting answers the pool's OWN key
    /// <c>$SQL.TransPool.TransClass</c> for the TRANSACTION class
    /// [<c>n_cst_thread_trans_pool.sru:L83</c>] - a different key for a different thing. See
    /// <see cref="TransactionPoolClassKey"/>.
    /// </para>
    /// <para>
    /// A method rather than a property because the legacy member is a function, and because callers read
    /// it as an action - <c>of_GetTransPool().of_RemoveRef(...)</c> [<c>:L122, :L716</c>].
    /// </para>
    /// </remarks>
    internal TransactionPool GetTransactionPool() => _transactionPool;

    /// <summary>
    /// Whether a usable transaction exists for the installed descriptor - the port of
    /// <c>of_hasalivetrans</c> [<c>n_cst_thread_task_sqlbase.sru:L211-L214</c>].
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when this task already holds one [<c>:L211</c>], or when the pool has an
    /// entry keyed on an equal descriptor [<c>:L213</c>].
    /// </returns>
    internal bool HasAliveTrans() =>
        Predicates.IsValidObject(_transactionObject) || _transactionPool.Exists(_transactionData);

    /// <summary>
    /// Returns a borrowed transaction to the pool - the port of <c>of_releasetransobject</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L137-L146</c>].
    /// </summary>
    /// <param name="transaction">
    /// The transaction to release. Passed by <see langword="ref"/> because the legacy parameter is
    /// <c>ref</c> and the pool writes through it [<c>:L141</c>].
    /// </param>
    /// <returns>
    /// <see cref="RetCode.E_INVALID_OBJECT"/> when the argument is unusable [<c>:L137</c>] or is not the
    /// transaction this task holds [<c>:L138</c>]; <see cref="RetCode.FAILED"/> when no pool reference
    /// is held [<c>:L139</c>]; otherwise <see cref="RetCode.OK"/>.
    /// </returns>
    /// <remarks>
    /// <b>The reference index is deliberately NOT zeroed here.</b> The legacy nulls
    /// <c>_transObject</c> at <c>:L143</c> and leaves <c>_nTransRefIdx</c> alone, so a later
    /// <see cref="GetTransObject(ref IPooledTransaction?, ref DbErrorData)"/> re-enters through the
    /// <c>_nTransRefIdx &gt; 0</c> arm, finds no valid object, and takes the reacquire path
    /// [<c>:L153-L166</c>]. Zeroing it would be tidier and would change which arm runs.
    /// </remarks>
    protected long ReleaseTransObject(ref IPooledTransaction? transaction)
    {
        // [:L137]
        if (!Predicates.IsValidObject(transaction))
        {
            return RetCode.E_INVALID_OBJECT;
        }

        // [:L138] identity comparison, not equality: releasing someone else's transaction is refused.
        if (!ReferenceEquals(transaction, _transactionObject))
        {
            return RetCode.E_INVALID_OBJECT;
        }

        // [:L139] note FAILED here rather than a more specific code - preserved as the oracle has it.
        if (!_transactionLease.IsValid)
        {
            return RetCode.FAILED;
        }

        // [:L141]
        _transactionPool.Release(_transactionLease, ref transaction);

        // [:L143] hazard 2 again: the reference is cleared deterministically.
        _transactionObject = null;

        // [:L145]
        return RetCode.OK;
    }

    /// <summary>
    /// Borrows or reuses the pooled transaction for the installed descriptor - the port of
    /// <c>of_gettransobject</c> [<c>n_cst_thread_task_sqlbase.sru:L148-L184</c>].
    /// </summary>
    /// <param name="transaction">Receives the transaction. <see langword="ref"/> mirrors the legacy.</param>
    /// <param name="dbErrorData">
    /// Receives connection failure detail. <see langword="ref"/> mirrors the legacy, and only TWO of its
    /// five members are written [<c>:L175-L176</c>] - the rest keep whatever the caller had.
    /// </param>
    /// <returns>
    /// <see cref="RetCode.OK"/> on success; <see cref="RetCode.E_INVALID_TRANSACTION"/> when the
    /// transaction was obtained but could not connect [<c>:L177</c>]; otherwise whatever the pool
    /// answered [<c>:L183</c>].
    /// </returns>
    /// <remarks>
    /// The three arms, in the legacy's order. FIRST, reuse: a held reference index AND a usable object
    /// AND not broken means attach, clear state and return [<c>:L153-L159</c>] - and note the reuse test
    /// is <c>Not of_IsBroken()</c>, not <c>of_IsConnected()</c>, so a connected-but-broken transaction is
    /// discarded while a valid-but-disconnected one is reused and reconnected below. SECOND, a broken
    /// object drops its pool reference and zeroes the index [<c>:L160-L161</c>]. THIRD, acquire: add a
    /// reference if none is held [<c>:L164-L166</c>], get the object [<c>:L168</c>], and on success
    /// attach and connect if not already connected [<c>:L170-L179</c>].
    /// </remarks>
    protected long GetTransObject(ref IPooledTransaction? transaction, ref DbErrorData dbErrorData)
    {
        // [:L151] resolved once, exactly as the legacy hoists it into a local.
        TransactionPool transactionPool = GetTransactionPool();

        // [:L153]
        if (_transactionLease.IsValid && Predicates.IsValidObject(_transactionObject))
        {
            // [:L154]
            if (!_transactionObject!.IsBroken())
            {
                // [:L155-L157]
                transaction = _transactionObject;
                AttachTransaction(transaction);
                transaction.ClearState();

                // [:L158]
                return RetCode.OK;
            }

            // [:L160-L161]
            transactionPool.RemoveRef(_transactionLease);
            _transactionLease = PoolLease.None;
        }

        // [:L164-L166]
        if (!_transactionLease.IsValid)
        {
            _transactionLease = transactionPool.AddRefLease(_transactionData);
        }

        // [:L168]
        long rtCode = transactionPool.Get(_transactionLease, out IPooledTransaction? borrowed);

        // [:L169] IsSucceeded, not `= RetCode.OK`: the tri-state algebra is preserved everywhere.
        if (Predicates.IsSucceeded(rtCode))
        {
            transaction = borrowed;

            // [:L170-L171] `_transObject = transObject` then `_transObject.Event OnAttach(this)`. The
            // two collapse into one call here - see AttachTransaction for why.
            AttachTransaction(borrowed);

            // [:L172] the oracle's own `//Test` comment sits here, above the connect probe.
            // [:L173]
            if (borrowed is not null && !borrowed.IsConnected())
            {
                // [:L174]
                if (Predicates.IsFailed(borrowed.Connect()))
                {
                    // [:L175-L176] exactly two members are written; the buffer, row and statement are
                    // left untouched because a connect failure has no statement and no row.
                    dbErrorData = dbErrorData with
                    {
                        SqlDbCode = borrowed.SqlDbCode,
                        SqlErrText = borrowed.SqlErrText,
                    };

                    // [:L177]
                    return RetCode.E_INVALID_TRANSACTION;
                }
            }

            // [:L180]
            return RetCode.OK;
        }

        // [:L183]
        return rtCode;
    }

    /// <summary>
    /// The convenience overload that discards connection failure detail - the port of the one-argument
    /// <c>of_gettransobject</c> [<c>n_cst_thread_task_sqlbase.sru:L186-L188</c>].
    /// </summary>
    /// <param name="transaction">Receives the transaction.</param>
    /// <returns>Whatever the two-argument overload returns.</returns>
    /// <remarks>
    /// The legacy declares a local <c>DBERRORDATA</c> and throws it away [<c>:L186</c>], which is the
    /// whole content of this overload; reproduced so a derived task that does not care about the detail
    /// reads the same as its original.
    /// </remarks>
    protected long GetTransObject(ref IPooledTransaction? transaction)
    {
        DbErrorData discarded = DbErrorData.Empty;
        return GetTransObject(ref transaction, ref discarded);
    }

    /// <summary>
    /// Associates a borrowed transaction with this task - the port of
    /// <c>transObject.Event OnAttach(this)</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L156, :L171</c>].
    /// </summary>
    /// <param name="transaction">The transaction being attached, or <see langword="null"/>.</param>
    /// <remarks>
    /// <para>
    /// <b>Constraint C-K - this is a documented deviation forced by a sibling's actual surface, and it
    /// COLLAPSES TWO LEGACY STATEMENTS INTO ONE.</b> The legacy attach event assigns two BACK-REFERENCES
    /// onto the TRANSACTION - <c>#ParentTask = parentTask</c> and
    /// <c>#ParentThread = parentTask.#ParentThread</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L103-L105</c>] - and the ported
    /// <see cref="IPooledTransaction"/> deliberately carries NO parent-task member, so there is nothing
    /// on that side to assign. What remains of the pair is the TASK-side association
    /// <c>_transObject = transObject</c> [<c>:L170</c>], which this method performs.
    /// </para>
    /// <para>
    /// A one-directional reference is strictly better for hazard 2: teardown has exactly ONE reference to
    /// clear, and a transaction returned to the pool cannot still be pointing at a task that has
    /// finished. Kept as its own overridable method rather than inlined so that both legacy attach sites
    /// [<c>:L156, :L171</c>] remain visible, and so a derived task can observe the moment of attachment -
    /// which is what the legacy back-reference existed to enable.
    /// </para>
    /// <para>
    /// Idempotent: the reuse arm [<c>:L155-L156</c>] attaches a transaction this task already holds, and
    /// re-recording the same association is exactly what the oracle does there.
    /// </para>
    /// </remarks>
    protected virtual void AttachTransaction(IPooledTransaction? transaction) =>
        _transactionObject = transaction;

    #endregion

    #region Commit, rollback, and the BACKWARD commit-signal propagation

    /// <summary>
    /// The commit signal, created on first use - the port of <c>of_getcommitevent</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L224-L228</c>].
    /// </summary>
    /// <returns>The signal, MANUAL-RESET and INITIALLY UNSIGNALLED.</returns>
    /// <remarks>
    /// <para>
    /// Both properties are contract, and both come straight out of the legacy call
    /// <c>CreateEvent(0, true, false, 0)</c> [<c>:L225</c>]: argument three is
    /// <c>bManualReset = true</c> and argument four is <c>bInitialState = false</c>. MANUAL-RESET means
    /// a commit stays observable until <see cref="Reset"/> clears it, so a consumer that looks late
    /// still sees it; auto-reset would let the first observer consume the signal and hide it from the
    /// rest. INITIALLY UNSIGNALLED means "not committed" is the starting truth.
    /// </para>
    /// <para>
    /// Created lazily, exactly as the legacy tests <c>if _hEvtCommitted = 0</c> first, which is why
    /// <see cref="OnCommitted"/> and <see cref="Reset"/> both have to tolerate its absence rather than
    /// forcing it into existence.
    /// </para>
    /// <para>
    /// The caller-side proxy polls it NON-BLOCKINGLY - <c>WaitForSingleObject(_hEvtCommitted, 0) = 0</c>
    /// [<c>n_cst_threading_task_sqlbase.sru:L175</c>], grabbing the handle once during its own init at
    /// <c>:L218</c>. <see cref="ManualResetEventSlim.IsSet"/> is the equivalent read, and
    /// <see cref="ManualResetEventSlim.Wait(int)"/> with a zero timeout is the literal one.
    /// </para>
    /// </remarks>
    internal ManualResetEventSlim GetCommitEvent()
    {
        // [:L224-L226] `if _hEvtCommitted = 0 then _hEvtCommitted = CreateEvent(0,true,false,0)`
        _committedSignal ??= new ManualResetEventSlim(initialState: false);

        // [:L227]
        return _committedSignal;
    }

    /// <summary>
    /// Whether this task's commit signal is set - the worker-side reading of what the proxy exposes as
    /// <c>of_iscommitted</c> [<c>n_cst_threading_task_sqlbase.sru:L175</c>].
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when no signal has been created, which is the same answer
    /// <c>WaitForSingleObject</c> on a never-signalled handle gives.
    /// </returns>
    /// <remarks>
    /// Deliberately does NOT create the signal: an observer must not change the state it observes, and
    /// the legacy proxy reads a handle it was given rather than asking for one.
    /// </remarks>
    internal bool IsCommitted() => _committedSignal?.IsSet == true;

    /// <summary>
    /// Commits the attached transaction - the port of <c>of_commit</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L230-L240</c>].
    /// </summary>
    /// <param name="autoRollback">
    /// Whether a failed commit rolls back automatically. Forwarded verbatim; the policy belongs to the
    /// transaction [<c>:L234</c>].
    /// </param>
    /// <returns>
    /// <see cref="RetCode.E_INVALID_TRANSACTION"/> when nothing is attached [<c>:L232</c>], otherwise
    /// whatever the transaction answered.
    /// </returns>
    /// <remarks>
    /// The success test is <see cref="Predicates.IsSucceeded(long?)"/> [<c>:L235</c>], not equality with
    /// zero, so the tri-state algebra survives: a prevention still reads as a success and therefore
    /// still fires the notification, while a cancellation reads as neither and does not.
    /// </remarks>
    internal long Commit(bool autoRollback)
    {
        // [:L232]
        if (!Predicates.IsValidObject(_transactionObject))
        {
            return RetCode.E_INVALID_TRANSACTION;
        }

        // [:L234]
        long rtCode = _transactionObject!.Commit(autoRollback);

        // [:L235-L237]
        if (Predicates.IsSucceeded(rtCode))
        {
            OnCommitted();
        }

        // [:L239]
        return rtCode;
    }

    /// <summary>
    /// Rolls the attached transaction back - the port of <c>of_rollback</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L219-L222</c>].
    /// </summary>
    /// <returns>
    /// <see cref="RetCode.E_INVALID_TRANSACTION"/> when nothing is attached [<c>:L219</c>], otherwise
    /// whatever the transaction answered [<c>:L221</c>].
    /// </returns>
    /// <remarks>
    /// Called unconditionally and FIRST by <see cref="OnError"/>, so it must stay safe on a task that
    /// never obtained a transaction - hence the validity guard rather than a throw.
    /// </remarks>
    internal long Rollback()
    {
        // [:L219]
        if (!Predicates.IsValidObject(_transactionObject))
        {
            return RetCode.E_INVALID_TRANSACTION;
        }

        // [:L221]
        return _transactionObject!.Rollback();
    }

    /// <summary>
    /// Signals the commit to this task and to every task before it - the port of
    /// <c>event oncommitted()</c> [<c>n_cst_thread_task_sqlbase.sru:L100-L111</c>].
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>⚠ THE ITERATION RUNS BACKWARD, AND THE DIRECTION IS DELIBERATE (constraints C-B, C-K;
    /// AAP §0.8.6 R9).</b> The oracle is
    /// <c>for nIndex = of_GetIndex() to 1 step -1</c> [<c>:L104</c>], under its own comment
    /// 设置当前以及前面所有任务的提交信号 - "set the commit signal of the current task and of every
    /// preceding task". The reason the direction is not cosmetic: a commit on one connection commits the
    /// work of every task that already ran against it, so the tasks that must be told are the ones at
    /// LOWER indices, and walking down from the current index is what enumerates exactly those and
    /// nothing after them. "Tidying" it into an ascending loop over the same range happens to touch the
    /// same set here, but it is the same class of edit as reversing the inverted filter-buffer traversal
    /// at <c>n_cst_thread_task_sqlupdate.sru:L237</c>, which DOES corrupt data. The direction is
    /// preserved, and the reason is recorded so a future reader does not have to re-derive it.
    /// </para>
    /// <para>
    /// <b>Tasks with no signal created are SKIPPED, not given one.</b> The oracle tests
    /// <c>if task._hEvtCommitted &lt;&gt; 0</c> before signalling [<c>:L106</c>] and never calls the
    /// lazy creator, so a task that never asked for a commit signal is left without one. Creating one
    /// here would allocate a handle nobody waits on and would make a never-interested task suddenly
    /// report itself committed.
    /// </para>
    /// <para>
    /// Reaching another instance's private signal is exactly what the oracle does - PowerScript
    /// <c>private</c> is per-class, and so is C# <c>private</c>, so <c>task._hEvtCommitted</c>
    /// translates member for member.
    /// </para>
    /// </remarks>
    protected virtual void OnCommitted()
    {
        // [:L104] BACKWARD, from this task's own one-based index down to 1 inclusive.
        for (int index = _host.TaskIndex; index >= OneBasedIndex.FirstIndex; index--)
        {
            // [:L105] IsSucceeded rather than an equality test, preserving the tri-state algebra.
            if (!Predicates.IsSucceeded(_host.GetTask(index, out SqlTaskBase? task)))
            {
                continue;
            }

            // [:L106-L108] signal only an ALREADY CREATED handle.
            if (task?._committedSignal is { } signal)
            {
                signal.Set();
            }
        }
    }

    /// <summary>
    /// Clears per-execution state - the port of <c>of_reset</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L242-L249</c>].
    /// </summary>
    /// <returns><see cref="RetCode.OK"/>; the legacy has no failing arm.</returns>
    /// <remarks>
    /// <para>
    /// Two actions in the legacy's order: reset the commit signal IF ONE EXISTS [<c>:L242-L244</c>], then
    /// reset the parameters [<c>:L246</c>]. Resetting the signal is what the manual-reset choice makes
    /// necessary - nothing else clears it - and skipping creation keeps a task that never used one free
    /// of a handle.
    /// </para>
    /// <para>
    /// <b>Virtual because every derived task chains to it.</b> <c>SqlQueryTask</c>,
    /// <c>SqlUpdateTask</c> and <c>SqlCommandTask</c> each override <c>of_reset</c> and call the
    /// ancestor, and the caller-side proxy funnels through <c>task.of_Reset()</c>
    /// [<c>n_cst_threading_task_sqlbase.sru:L61</c>]. An override must call <c>base.Reset()</c> and must
    /// respect its result, which the proxy tests with <c>IsFailed</c> [<c>:L62</c>].
    /// </para>
    /// </remarks>
    internal virtual long Reset()
    {
        // [:L242-L244]
        _committedSignal?.Reset();

        // [:L246]
        ResetParams();

        // [:L248]
        return RetCode.OK;
    }

    #endregion

    #region The SQL parameter collection - one-based throughout

    /// <summary>
    /// The parameter count - the port of <c>of_getparamcount</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L494</c>], which answers <c>UpperBound(_sqlParams)</c> and is
    /// therefore also the highest valid one-based index.
    /// </summary>
    /// <returns>The number of parameters currently held.</returns>
    internal int GetParamCount() => OneBasedIndex.UpperBound(_sqlParams);

    /// <summary>
    /// Whether any parameter is held - the port of <c>of_hasparams</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L368</c>], literally <c>UpperBound(_sqlParams) &gt; 0</c>.
    /// </summary>
    /// <returns><see langword="true"/> when at least one parameter is held.</returns>
    internal bool HasParams() => OneBasedIndex.UpperBound(_sqlParams) > OneBasedIndex.EmptyUpperBound;

    /// <summary>
    /// Discards every parameter - the port of <c>of_resetparams</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L359-L366</c>].
    /// </summary>
    /// <returns><see cref="RetCode.OK"/>.</returns>
    /// <remarks>
    /// The legacy assigns a freshly declared empty array over the field [<c>:L363</c>], which resets the
    /// upper bound to zero. Its busy guard at <c>:L361</c> is COMMENTED OUT and is carried across inert
    /// (constraint C-B) - the live one is on the proxy [<c>n_cst_threading_task_sqlbase.sru:L163</c>].
    /// </remarks>
    internal long ResetParams()
    {
        // [:L361] `//if #Running then return RetCode.E_BUSY` - INERT, carried across, never revived.

        // [:L363]
        _sqlParams.Clear();

        // [:L365]
        return RetCode.OK;
    }

    /// <summary>
    /// Appends a parameter - the port of <c>of_addparam</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L251-L260</c>].
    /// </summary>
    /// <param name="name">
    /// The parameter name, LOWER-CASED before storage [<c>:L256</c>]. The empty string means a
    /// positional parameter, which the proxy's one-argument overload supplies explicitly -
    /// <c>return of_AddParam("", param)</c> [<c>n_cst_threading_task_sqlbase.sru:L138</c>].
    /// </param>
    /// <param name="value">The value; may be <see langword="null"/> or an array.</param>
    /// <returns><see cref="RetCode.OK"/>.</returns>
    /// <remarks>
    /// <para>
    /// Always APPENDS, at <c>UpperBound + 1</c> [<c>:L255</c>] - it never merges with an existing entry
    /// of the same name. That is what distinguishes it from <see cref="SetParam(string, object?)"/>, and
    /// it means duplicate names are representable; the binder then matches the FIRST unreplaced
    /// placeholder for each in turn [<c>:L456-L475</c>].
    /// </para>
    /// <para>
    /// Lower-casing is <see cref="CultureInfo.InvariantCulture"/> because a parameter name is an ASCII
    /// identifier matched against placeholder text that is lower-cased the same way [<c>:L437</c>]; a
    /// culture-sensitive mapping could make the two disagree for the same input.
    /// </para>
    /// </remarks>
    internal long AddParam(string? name, object? value)
    {
        // [:L253] `//if #Running then return RetCode.E_BUSY` - INERT, carried across (constraint C-B).

        // [:L255-L257] append at UpperBound + 1 with the name lower-cased.
        _sqlParams.Add(new SqlTaskParameter(LowerName(name), value));

        // [:L259]
        return RetCode.OK;
    }

    /// <summary>
    /// Updates a parameter by name, appending it when absent - the port of <c>of_setparam</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L341-L357</c>].
    /// </summary>
    /// <param name="name">The parameter name; lower-cased before comparison and storage [<c>:L345</c>].</param>
    /// <param name="value">The value to store.</param>
    /// <returns><see cref="RetCode.OK"/>.</returns>
    /// <remarks>
    /// <para>
    /// <b>⚠ R9 ONE-BASED SCAN SITE (constraints C-B, C-K; AAP §0.8.6 R9). The oracle depends on
    /// POWERSCRIPT LEAVING ITS LOOP VARIABLE IN SCOPE AFTER THE LOOP, and a naive C# port silently
    /// overwrites entry 1.</b> The oracle is:
    /// </para>
    /// <code>
    /// name = Lower(name)                                    [:L345]
    /// nCount = UpperBound(_sqlParams)                       [:L346]
    /// for nIndex = 1 to nCount                              [:L348]
    ///     if _sqlParams[nIndex].name = name then exit       [:L349-L351]
    /// next
    /// _sqlParams[nIndex].name = name    &lt;-- UNCONDITIONAL WRITE AT nIndex   [:L353]
    /// _sqlParams[nIndex].value = value                                       [:L354]
    /// </code>
    /// <para>
    /// The write target is whatever <c>nIndex</c> holds once the loop ends, and there are exactly three
    /// cases. MATCH at position k: the <c>exit</c> leaves <c>nIndex = k</c>, so the entry is updated in
    /// place. NO MATCH: the loop runs to completion and leaves <c>nIndex = nCount + 1</c>, so the
    /// parameter is APPENDED - PowerBuilder grows an array on assignment one past its upper bound.
    /// EMPTY collection: the body never executes at all and <c>nIndex</c> is 1, which is the same
    /// <c>nCount + 1</c> and therefore also an append. All three are reproduced below by computing the
    /// append position FIRST and letting a match overwrite it, using an index variable declared OUTSIDE
    /// the loop. A C# <c>for</c> scopes its counter, so writing to the counter after the loop is not
    /// even expressible - which is precisely why the naive port lands on 1 and corrupts the first entry
    /// without any diagnostic.
    /// </para>
    /// <para>
    /// Its busy guard at <c>:L343</c> is COMMENTED OUT and carried across inert.
    /// </para>
    /// </remarks>
    internal long SetParam(string? name, object? value)
    {
        // [:L343] `//if #Running then return RetCode.E_BUSY` - INERT, carried across (constraint C-B).

        // [:L345]
        string lowered = LowerName(name);

        // [:L346]
        int upperBound = OneBasedIndex.UpperBound(_sqlParams);

        // The value PowerScript's loop variable holds when NO iteration matches - which is also the
        // value it holds when the collection is empty and the body never runs. Computing it up front is
        // what makes the two append cases one case, exactly as the oracle's single write does.
        int targetIndex = OneBasedIndex.AppendIndex(_sqlParams);

        // [:L348-L352] the scan. Ordinal comparison: both sides were lower-cased invariantly, so this
        // is a byte comparison of identifiers and no culture can reinterpret it.
        for (int index = OneBasedIndex.FirstIndex; index <= upperBound; index++)
        {
            if (string.Equals(_sqlParams[index - OneBasedIndex.FirstIndex].Name, lowered, StringComparison.Ordinal))
            {
                // [:L350] `exit` - the loop variable keeps this value for the write below.
                targetIndex = index;
                break;
            }
        }

        // [:L353-L354] the unconditional write at the surviving index. Both members are written, so a
        // matched entry has its name rewritten to the same lower-cased text - harmless, and preserved
        // because the oracle writes both.
        SqlTaskParameter updated = new(lowered, value);
        if (OneBasedIndex.IsWithin(targetIndex, upperBound))
        {
            _sqlParams[targetIndex - OneBasedIndex.FirstIndex] = updated;
        }
        else
        {
            // The append case. targetIndex can only ever be upperBound + 1 here, so a single Add is the
            // exact equivalent of PowerBuilder growing the array by one.
            _sqlParams.Add(updated);
        }

        // [:L356]
        return RetCode.OK;
    }

    /// <summary>
    /// Updates a parameter's VALUE by ONE-BASED position - the port of the index overload of
    /// <c>of_setparam</c> [<c>n_cst_thread_task_sqlbase.sru:L517-L522</c>].
    /// </summary>
    /// <param name="index">The one-based position.</param>
    /// <param name="value">
    /// The value. Passed by <see langword="ref"/> because the legacy parameter is <c>ref any value</c>;
    /// nothing is written back, and the signature is preserved rather than tidied because it is the
    /// published shape a derived task and its proxy are written against.
    /// </param>
    /// <returns>
    /// <see cref="RetCode.E_OUT_OF_BOUND"/> when the index is below 1 or above <c>UpperBound + 1</c>
    /// [<c>:L517</c>], otherwise <see cref="RetCode.OK"/>.
    /// </returns>
    /// <remarks>
    /// <b>The bound really is <c>UpperBound + 1</c>, not <c>UpperBound</c>, and that makes this an
    /// APPENDING setter at exactly one position (constraint C-B).</b> Writing at one past the end grows
    /// the PowerBuilder array, producing an entry whose name is the empty string - a POSITIONAL
    /// parameter - and whose value is the one supplied. The read overload uses the tighter bound
    /// <c>UpperBound</c> [<c>:L487</c>], so the asymmetry between the two is deliberate in the oracle
    /// and is preserved here.
    /// </remarks>
    internal long SetParam(int index, ref object? value)
    {
        int upperBound = OneBasedIndex.UpperBound(_sqlParams);

        // [:L517]
        if (index < OneBasedIndex.FirstIndex || index > upperBound + 1)
        {
            return RetCode.E_OUT_OF_BOUND;
        }

        // [:L519] only the VALUE member is assigned; the name is left as it was.
        if (OneBasedIndex.IsWithin(index, upperBound))
        {
            SqlTaskParameter existing = _sqlParams[index - OneBasedIndex.FirstIndex];
            _sqlParams[index - OneBasedIndex.FirstIndex] = existing with { Value = value };
        }
        else
        {
            // The grow-by-one case: a new entry with an EMPTY name, which the binder treats as
            // positional [:L460].
            _sqlParams.Add(new SqlTaskParameter(string.Empty, value));
        }

        // [:L521]
        return RetCode.OK;
    }

    /// <summary>
    /// Reads a parameter's value by ONE-BASED position - the port of <c>of_getparam</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L487-L492</c>].
    /// </summary>
    /// <param name="index">The one-based position.</param>
    /// <param name="value">Receives the value. <see langword="ref"/> mirrors the legacy signature.</param>
    /// <returns>
    /// <see cref="RetCode.E_OUT_OF_BOUND"/> outside <c>1..UpperBound</c> [<c>:L487</c>], otherwise
    /// <see cref="RetCode.OK"/>.
    /// </returns>
    /// <remarks>
    /// On the out-of-bound arm the legacy leaves the caller's variable untouched, so this does too - it
    /// does not helpfully write <see langword="null"/>.
    /// </remarks>
    internal long GetParam(int index, ref object? value)
    {
        // [:L487] note the tighter bound than the index setter's - see SetParam(int, ref object?).
        if (!OneBasedIndex.IsWithin(index, OneBasedIndex.UpperBound(_sqlParams)))
        {
            return RetCode.E_OUT_OF_BOUND;
        }

        // [:L489]
        value = _sqlParams[index - OneBasedIndex.FirstIndex].Value;

        // [:L491]
        return RetCode.OK;
    }

    /// <summary>
    /// Reads a parameter's value by NAME - the port of the name overload of <c>of_getparam</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L497-L510</c>].
    /// </summary>
    /// <param name="name">The parameter name; lower-cased before comparison [<c>:L499</c>].</param>
    /// <param name="value">Receives the value. <see langword="ref"/> mirrors the legacy signature.</param>
    /// <returns>
    /// <see cref="RetCode.OK"/> on the FIRST match [<c>:L505</c>], or
    /// <see cref="RetCode.E_DATA_NOT_FOUND"/> when no entry matches [<c>:L509</c>].
    /// </returns>
    /// <remarks>
    /// First match wins, which matters because <see cref="AddParam"/> permits duplicate names. The
    /// caller's variable is left untouched when nothing matches.
    /// </remarks>
    internal long GetParam(string? name, ref object? value)
    {
        // [:L499]
        string lowered = LowerName(name);

        // [:L500-L507]
        int upperBound = OneBasedIndex.UpperBound(_sqlParams);
        for (int index = OneBasedIndex.FirstIndex; index <= upperBound; index++)
        {
            SqlTaskParameter parameter = _sqlParams[index - OneBasedIndex.FirstIndex];
            if (string.Equals(parameter.Name, lowered, StringComparison.Ordinal))
            {
                // [:L504-L505]
                value = parameter.Value;
                return RetCode.OK;
            }
        }

        // [:L509]
        return RetCode.E_DATA_NOT_FOUND;
    }

    /// <summary>
    /// Reads a parameter's NAME by ONE-BASED position - the port of <c>of_getparamname</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L512-L515</c>].
    /// </summary>
    /// <param name="index">The one-based position.</param>
    /// <returns>
    /// The lower-cased name, or the EMPTY STRING when the index is out of bounds [<c>:L512</c>].
    /// </returns>
    /// <remarks>
    /// <b>The out-of-bound answer is the empty string, not a return code</b>, because the legacy member
    /// returns <c>string</c> and has nowhere to put one. That collides with the value a POSITIONAL
    /// parameter legitimately carries, so an out-of-bound index and a positional parameter are
    /// indistinguishable through this member alone - a legacy ambiguity, preserved. A caller that needs
    /// to tell them apart tests the index against <see cref="GetParamCount"/> first.
    /// </remarks>
    internal string GetParamName(int index)
    {
        // [:L512]
        if (!OneBasedIndex.IsWithin(index, OneBasedIndex.UpperBound(_sqlParams)))
        {
            return string.Empty;
        }

        // [:L514]
        return _sqlParams[index - OneBasedIndex.FirstIndex].Name;
    }

    /// <summary>
    /// A snapshot of the parameter collection in ONE-BASED order.
    /// </summary>
    /// <returns>A copy, so that a caller enumerating it cannot be perturbed by a concurrent change.</returns>
    /// <remarks>
    /// The port of <c>sqlParams = _sqlParams</c> [<c>n_cst_thread_task_sqlbase.sru:L398</c>], which is a
    /// PowerBuilder array assignment and therefore a COPY. The binder relies on that: it rewrites the
    /// names of its own copy when matching them to DataWindow argument positions [<c>:L414</c>] and the
    /// task's own parameters must not change as a result.
    /// </remarks>
    internal IReadOnlyList<SqlTaskParameter> SnapshotParams() => [.. _sqlParams];

    /// <summary>
    /// Lower-cases a parameter name the way the legacy <c>Lower(...)</c> does, treating
    /// <see langword="null"/> as empty.
    /// </summary>
    /// <param name="name">The name to normalise.</param>
    /// <returns>The invariantly lower-cased name, never <see langword="null"/>.</returns>
    private static string LowerName(string? name) =>
        string.IsNullOrEmpty(name) ? string.Empty : name.ToLowerInvariant();

    #endregion

    #region The SQL literal generator - the interpolation site itself

    /// <summary>
    /// Renders a parameter value as a SQL literal - the port of <c>_of_paramtostring</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L262-L339</c>].
    /// </summary>
    /// <param name="param">
    /// The value. A one-dimensional array takes the ARRAY path, anything else the SCALAR path - see the
    /// discrimination rule below.
    /// </param>
    /// <param name="dbType">
    /// The dialect, compared against <see cref="DatabaseType.DbtOracle"/> - the generated contract enum,
    /// whose <c>DBT_ORACLE = 1</c> is the same value as the legacy
    /// <c>constant long DBT_ORACLE = 1</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L61</c>]. <b>Constraint C-E: the
    /// dialect is used for ONE purpose only - choosing between two literal string forms. Nothing here
    /// opens a connection or selects an engine.</b>
    /// </param>
    /// <returns>
    /// The literal text, or <see langword="null"/> when a null poisons an ARRAY rendering - see the
    /// asymmetry note.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>THIS IS THE INTERPOLATION SITE, and its output must be BYTE-IDENTICAL to the oracle's.</b>
    /// These literals end up inside generated statements that parity tests compare character for
    /// character, so every quote, every comma and every format string below is fixed by the oracle
    /// rather than chosen. See <see cref="ApplyConnectionParameterFlags"/> for why interpolation exists
    /// at all and why it is documented rather than corrected (constraints C-B, C-F).
    /// </para>
    /// <para>
    /// <b>ARRAY-VERSUS-SCALAR DISCRIMINATION IS <c>UpperBound(param) &gt; 0</c> [<c>:L266-L267</c>], SO A
    /// ZERO-LENGTH ARRAY FALLS INTO THE SCALAR BRANCH.</b> That is not an accident to normalise: an empty
    /// PowerBuilder array has upper bound 0, exactly like a non-array, so the two are indistinguishable
    /// at that test and the oracle renders both through the scalar path. Preserved verbatim.
    /// </para>
    /// <para>
    /// <b>THE ARRAY BRANCH DISPATCHES ON THE TYPE OF ELEMENT 1 ONLY</b> -
    /// <c>choose case ClassName(paramArray[1])</c> [<c>:L269</c>] - and then renders EVERY element with
    /// that same formatter. A mixed array is therefore formatted as though it were homogeneous, which is
    /// legacy behaviour and is preserved.
    /// </para>
    /// <para>
    /// <b>⚠ PRESERVED ASYMMETRY: THE SCALAR PATH GUARDS NULL, THE ARRAY PATH DOES NOT (constraint
    /// C-B).</b> <c>if IsNull(param) then return "NULL"</c> appears at <c>:L315</c>, inside the scalar
    /// branch and nowhere else; the array branch at <c>:L266-L313</c> has NO per-element null test of any
    /// kind. <b>No null guard is added to the array path.</b> The consequence follows from PowerScript's
    /// string algebra rather than from a choice: any concatenation involving a null string yields null,
    /// so one null element makes the accumulator null and the whole rendering answers null - it does NOT
    /// answer <c>"NULL"</c>, and it does NOT quietly render an empty literal. That is reproduced here by
    /// answering <see langword="null"/>, which is the only C# shape that neither invents a literal nor
    /// hides the condition. <see cref="BindParams(ref string, long, string)"/> then narrows it to a
    /// DEFINED ERROR rather than splicing a null into a statement, because in the oracle that splice
    /// makes the entire statement null and hands a null statement to the DataWindow - an outcome that
    /// cannot cross a service boundary at all (AAP §0.1.5: narrow with a defined error, never widen with
    /// a guess).
    /// </para>
    /// </remarks>
    internal static string? ParamToString(object? param, long dbType)
    {
        // [:L266] `n = UpperBound(param)`. A non-array answers 0, and so does a zero-length array.
        IReadOnlyList<object?>? elements = AsParameterArray(param);
        int upperBound = elements?.Count ?? OneBasedIndex.EmptyUpperBound;

        // [:L267] `if n > 0 then` - the ARRAY branch.
        if (upperBound > OneBasedIndex.EmptyUpperBound)
        {
            // [:L269] the formatter is chosen ONCE, from element 1, and applied to every element.
            object? first = elements![0];

            StringBuilder builder = new();
            for (int index = OneBasedIndex.FirstIndex; index <= upperBound; index++)
            {
                // [:L272, :L279, :L287, :L298, :L308] - the per-element rendering. NO NULL GUARD HERE.
                string? rendered = RenderLiteral(elements[index - OneBasedIndex.FirstIndex], first, dbType);
                if (rendered is null)
                {
                    // PowerScript null-string propagation: the accumulator is now null and every later
                    // concatenation leaves it null, so the function's answer is null. Returning early is
                    // equivalent and states the reason.
                    return null;
                }

                builder.Append(rendered);

                // [:L273-L275] `if i <> n then sVal += ","` - a comma, and NO SPACE.
                if (index != upperBound)
                {
                    builder.Append(',');
                }
            }

            // [:L338]
            return builder.ToString();
        }

        // [:L315] THE SCALAR BRANCH'S NULL GUARD - present here, absent above.
        if (param is null)
        {
            return "NULL";
        }

        // [:L316-L335] the scalar dispatch, on the value's own type.
        return RenderLiteral(param, param, dbType);
    }

    /// <summary>
    /// Renders one value using the formatter selected by <paramref name="typeExemplar"/>.
    /// </summary>
    /// <param name="value">The value to render.</param>
    /// <param name="typeExemplar">
    /// The value whose type selects the formatter - element 1 on the array path [<c>:L269</c>], and the
    /// value itself on the scalar path [<c>:L316</c>].
    /// </param>
    /// <param name="dbType">The dialect.</param>
    /// <returns>
    /// The rendered literal, or <see langword="null"/> when <paramref name="value"/> is
    /// <see langword="null"/> - the PowerScript null-string propagation described on
    /// <see cref="ParamToString"/>.
    /// </returns>
    /// <remarks>
    /// Factored out so that the array and scalar paths share ONE set of format strings. The oracle
    /// duplicates them, and a duplicate is exactly how one copy comes to differ from the other in a
    /// place where byte-exactness is the acceptance criterion.
    /// </remarks>
    private static string? RenderLiteral(object? value, object? typeExemplar, long dbType)
    {
        if (value is null)
        {
            return null;
        }

        // `dbType = n_cst_thread_trans.DBT_ORACLE` [:L286, :L297, :L322, :L328].
        bool isOracle = dbType == (long)DatabaseType.DbtOracle;

        switch (typeExemplar)
        {
            // [:L270-L276, :L317-L318] case "string": a single-quoted literal with every embedded
            // single quote DOUBLED - `ReplaceAll(value, "'", "''", true)`.
            case string:
                return "'" + LegacyString(value).Replace("'", "''", StringComparison.Ordinal) + "'";

            // [:L277-L283, :L319-L320] case "time": String(value, "hh:mm:ss").
            case TimeOnly:
                return "'" + FormatTemporal(value, "HH:mm:ss") + "'";

            // [:L284-L294, :L321-L326] case "date".
            case DateOnly:
                return isOracle
                    ? "to_date('" + FormatTemporal(value, "yyyy-MM-dd") + "','yyyy-mm-dd')"
                    : "'" + FormatTemporal(value, "yyyy-MM-dd") + "'";

            // [:L295-L305, :L327-L332] case "datetime". Note the Oracle format model spells the hour
            // as hh24 and the minute as mi, which is Oracle's own vocabulary and not PowerBuilder's.
            case DateTime:
                return isOracle
                    ? "to_date('" + FormatTemporal(value, "yyyy-MM-dd HH:mm:ss") + "','yyyy-mm-dd hh24:mi:ss')"
                    : "'" + FormatTemporal(value, "yyyy-MM-dd HH:mm:ss") + "'";

            // [:L306-L312, :L333-L334] `case else //Numbers`: String(value), UNQUOTED.
            default:
                return LegacyString(value);
        }
    }

    /// <summary>
    /// Formats a temporal value with a legacy picture, tolerating a value whose type does not match the
    /// formatter selected by element 1.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <param name="format">The .NET format string equivalent to the legacy picture.</param>
    /// <returns>The formatted text, or the empty string when the value cannot be formatted.</returns>
    /// <remarks>
    /// <para>
    /// The mixed-array case is what this exists for. The array path picks its formatter from element 1
    /// [<c>:L269</c>] and then applies it to every element, so a date formatter can be handed a number.
    /// PowerBuilder's <c>String</c> answers the EMPTY STRING when it cannot convert its argument, and
    /// that is reproduced rather than throwing - the oracle produces a malformed literal and lets the
    /// database reject it, and an exception here would replace a legacy code path with one the oracle
    /// does not have.
    /// </para>
    /// <para>
    /// <see cref="CultureInfo.InvariantCulture"/> throughout: the legacy pictures are fixed patterns
    /// with ASCII separators, so a culture-sensitive calendar or separator would change generated SQL.
    /// The .NET pictures differ in case from the legacy ones - <c>HH</c> and <c>MM</c> rather than
    /// <c>hh</c> and <c>mm</c> - because .NET spells 24-hour hours and months that way, while
    /// PowerBuilder infers both from position. The RENDERED OUTPUT is identical, which is what parity
    /// measures.
    /// </para>
    /// </remarks>
    private static string FormatTemporal(object value, string format) =>
        value switch
        {
            DateOnly date => date.ToString(format, CultureInfo.InvariantCulture),
            TimeOnly time => time.ToString(format, CultureInfo.InvariantCulture),
            DateTime moment => moment.ToString(format, CultureInfo.InvariantCulture),
            DateTimeOffset moment => moment.ToString(format, CultureInfo.InvariantCulture),
            _ => LegacyString(value),
        };

    /// <summary>
    /// The port of PowerBuilder's <c>String(any)</c> conversion for the value domain this contract
    /// carries.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>
    /// The converted text, or the EMPTY STRING for anything outside the domain - which is what
    /// PowerBuilder's own <c>String</c> answers when it cannot convert its argument.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The domain is exactly the CLR type set the contract's <c>AnyValue</c> produces - <c>bool</c>,
    /// <c>long</c>, <c>ulong</c>, <c>double</c>, <c>decimal</c>, <see cref="DateOnly"/>,
    /// <see cref="TimeOnly"/>, <see cref="DateTime"/> and <c>string</c> - so nothing here speculates
    /// about a type that cannot arrive.
    /// </para>
    /// <para>
    /// Two renderings are worth stating. A <c>decimal</c> is rendered with
    /// <see cref="CultureInfo.InvariantCulture"/>, which PRESERVES TRAILING ZEROES, because the
    /// golden-master fixture declares <c>salary decimal(2)</c> and its scale is observable in generated
    /// SQL [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L12</c>]. A <c>bool</c> is rendered as
    /// <c>true</c>/<c>false</c> in lower case, which is what PowerBuilder's <c>String(boolean)</c>
    /// produces - not <c>True</c>/<c>False</c>, which is what .NET's own
    /// <see cref="bool.ToString()"/> would give.
    /// </para>
    /// <para>
    /// A ZERO-LENGTH ARRAY reaches here through the scalar branch, and answers the empty string. The
    /// oracle has no defined rendering for it either: <c>String</c> cannot convert an array, so it
    /// answers empty. The behaviour under test for that input is the BRANCH SELECTION, not the text.
    /// </para>
    /// </remarks>
    private static string LegacyString(object? value) =>
        value switch
        {
            null => string.Empty,
            string text => text,
            bool flag => flag ? "true" : "false",
            long number => number.ToString(CultureInfo.InvariantCulture),
            int number => number.ToString(CultureInfo.InvariantCulture),
            ulong number => number.ToString(CultureInfo.InvariantCulture),
            decimal number => number.ToString(CultureInfo.InvariantCulture),
            double number => number.ToString(CultureInfo.InvariantCulture),
            float number => number.ToString(CultureInfo.InvariantCulture),
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimeOnly time => time.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            DateTime moment => moment.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),

            // PowerBuilder: "String returns the empty string if it cannot convert data."
            _ => string.Empty,
        };

    /// <summary>
    /// Recognises a value as a ONE-DIMENSIONAL parameter array.
    /// </summary>
    /// <param name="param">The value to inspect.</param>
    /// <returns>Its elements, or <see langword="null"/> when the value is not such an array.</returns>
    /// <remarks>
    /// <para>
    /// The legacy admits only 简单类型或简单类型的一维数组 - simple types, or one-dimensional arrays of
    /// simple types - and its proxy enforces the dimensionality up front with
    /// <c>if UpperBound(value, 2) &gt;= 0 then return RetCode.E_INVALID_ARGUMENT</c>
    /// [<c>n_cst_threading_task_sqlbase.sru:L148</c>]. A multi-dimensional array is therefore rejected
    /// BEFORE it can reach the literal generator, and this method accordingly recognises rank-1 arrays
    /// only.
    /// </para>
    /// <para>
    /// A <c>string</c> is deliberately NOT treated as a sequence of characters even though it is
    /// enumerable, and a <c>byte[]</c> blob is not treated as an array of numbers: the legacy's
    /// <c>UpperBound</c> on either answers 0, so both take the scalar path.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<object?>? AsParameterArray(object? param)
    {
        switch (param)
        {
            case null:
            case string:
            case byte[]:
                return null;

            case object?[] objects:
                return objects;

            case Array array when array.Rank == 1:
            {
                object?[] boxed = new object?[array.Length];
                for (int index = 0; index < array.Length; index++)
                {
                    boxed[index] = array.GetValue(index);
                }

                return boxed;
            }

            case IReadOnlyList<object?> list:
                return list;

            default:
                return null;
        }
    }

    #endregion

    #region The named-placeholder binder

    /// <summary>
    /// The word delimiters that terminate - and may begin - a placeholder, exactly the set the oracle
    /// lists at <c>n_cst_thread_task_sqlbase.sru:L424</c> under its own comment
    /// <c>//Word delimiters</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Thirty characters, in the oracle's own order: space, tab, newline, carriage return, single quote,
    /// double quote, then the operator, bracket and punctuation set. <b>The prefix character itself is a
    /// member</b>, which is what makes <c>:a:b</c> two placeholders rather than one.
    /// </para>
    /// <para>
    /// A <see cref="SearchValues{T}"/> rather than a string scan or a switch: the membership test runs
    /// once per character of every statement, the set is closed and known at compile time, and the shape
    /// keeps the set written out exactly once so it can be diffed against the oracle's line.
    /// </para>
    /// </remarks>
    private static readonly SearchValues<char> WordDelimiters = SearchValues.Create(
        " \t\n\r'\"+-*/\\%><=(){}[],.:;?!&#|");

    /// <summary>
    /// The prefix every generated bound-parameter name carries, giving <c>@p1</c>, <c>@p2</c> and so on.
    /// ONE BASED, matching every other bound-parameter name in this service and the one-based ordinals the
    /// legacy counts retrieval arguments and DataWindow columns with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>NOT AN ORACLE VALUE - it names something the oracle does not have.</b> The legacy interpolates
    /// literals and binds nothing, so it has no placeholder vocabulary at all. These names appear ONLY in
    /// the executable form of a bound statement and never in the observable one, so they cannot reach a
    /// SQL-preview hook, a database-error payload or a characterization recording.
    /// </para>
    /// <para>
    /// <c>@</c> rather than <c>$</c> or <c>:</c> because it is the form every provider this service
    /// targets accepts, and because <c>:</c> is the oracle's OWN placeholder prefix - reusing it would
    /// make a generated name indistinguishable from a caller's unsubstituted placeholder.
    /// </para>
    /// </remarks>
    private const string BoundParameterPrefix = "@p";

    /// <summary>
    /// Substitutes every named placeholder in a statement with its parameter's literal - the port of
    /// <c>_of_sqlbindparams</c> [<c>n_cst_thread_task_sqlbase.sru:L371-L482</c>].
    /// </summary>
    /// <param name="sql">
    /// The statement, rewritten in place. <see langword="ref"/> because the legacy parameter is
    /// <c>ref string sql</c> and the oracle rewrites it repeatedly, once per substitution
    /// [<c>:L464</c>].
    /// </param>
    /// <param name="dbType">The dialect, forwarded to <see cref="ParamToString"/>.</param>
    /// <param name="dwArgString">
    /// The DataWindow argument list, used to NAME otherwise positional parameters [<c>:L410-L416</c>].
    /// Empty selects the two-argument behaviour.
    /// </param>
    /// <returns>
    /// <see cref="RetCode.OK"/> on success; <see cref="RetCode.FAILED"/> when no parameter is held
    /// [<c>:L401</c>]; <see cref="RetCode.E_INVALID_ARGUMENT"/> for a partially named parameter set
    /// [<c>:L407</c>], an unparseable argument list [<c>:L411</c>] or an unclosed quote [<c>:L451</c>];
    /// <see cref="RetCode.E_OUT_OF_BOUND"/> when the argument list length disagrees with the parameter
    /// count [<c>:L412</c>] or a placeholder was left unsubstituted [<c>:L478</c>]; and
    /// <see cref="RetCode.E_SQL_BIND_ARG_FAILED"/> for the one narrowed case described below.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>The oracle's own header comment states that this function DOES NOT VALIDATE SQL SYNTAX</b> -
    /// 该函数不验证SQL语法有效性 [<c>:L376</c>] - and that non-guarantee is preserved. It finds
    /// placeholders lexically and substitutes text; it does not parse, and it must not start.
    /// </para>
    /// <para>
    /// <b>Quote tracking is stateful and single-level [<c>:L425-L434</c>].</b> The first quote character
    /// seen opens a quoted run, and only the SAME character closes it, so an apostrophe inside a
    /// double-quoted identifier does not terminate anything. Inside a quoted run the prefix character is
    /// SKIPPED rather than treated as a placeholder [<c>:L441</c>], which is what stops a colon inside a
    /// literal from being substituted. An unclosed quote at the end is refused [<c>:L451</c>].
    /// </para>
    /// <para>
    /// <b>Positions are shifted after every substitution [<c>:L467-L470</c>].</b> A replacement rarely has
    /// the same length as the placeholder it replaces, so every LATER placeholder's recorded position is
    /// offset by the difference. Substituting left to right without that shift corrupts every position
    /// after the first - which is why the offset loop is reproduced rather than replaced by a rebuild.
    /// </para>
    /// <para>
    /// <b>An EMPTY parameter name means positional, and it stops after ONE substitution [<c>:L460,
    /// :L472</c>].</b> A named parameter, by contrast, keeps scanning and fills every placeholder that
    /// shares its name.
    /// </para>
    /// <para>
    /// <b>The one narrowing.</b> When <see cref="ParamToString"/> answers <see langword="null"/> - the
    /// preserved array-path null asymmetry - the oracle splices that null into the statement and makes
    /// the WHOLE statement null, then carries on and returns success. A null statement cannot cross a
    /// service boundary, so this port stops with
    /// <see cref="RetCode.E_SQL_BIND_ARG_FAILED"/> instead: a DEFINED ERROR rather than a guess, which
    /// is the rule for a legacy behaviour that cannot be reproduced across a boundary (AAP §0.1.5). The
    /// statement is left exactly as it was at that point.
    /// </para>
    /// </remarks>
    protected long BindParams(ref string sql, long dbType, string dwArgString)
    {
        long code = BindParams(sql, dbType, dwArgString, out SqlBoundStatement statement);

        // Written back UNCONDITIONALLY, including on a failure arm, because the oracle rewrites its
        // `ref string` progressively - once per substitution [:L464] - and a caller that inspected the
        // statement after a mid-loop refusal would see the partial rewrite. Preserved rather than tidied
        // (C-B); every current caller discards the statement on failure.
        sql = statement.ObservableText;

        return code;
    }

    /// <summary>
    /// Substitutes every named placeholder and emits BOTH the observable statement and a parameterized
    /// one - the same port as <see cref="BindParams(ref string, long, string)"/>, carrying two forms.
    /// </summary>
    /// <param name="sql">The statement to bind.</param>
    /// <param name="dbType">The dialect, forwarded to <see cref="ParamToString"/>.</param>
    /// <param name="dwArgString">
    /// The DataWindow argument list, used to NAME otherwise positional parameters [<c>:L410-L416</c>].
    /// </param>
    /// <param name="statement">
    /// The bound statement: the observable text the oracle would have produced, the parameterized text
    /// with a placeholder per parameter, and the parameter values in placeholder order.
    /// </param>
    /// <returns>Exactly the codes <see cref="BindParams(ref string, long, string)"/> returns.</returns>
    /// <remarks>
    /// <para>
    /// <b>WHY TWO FORMS AND NOT ONE.</b> The oracle interpolates rendered literals into the statement
    /// text, and it does so because PowerBuilder was configured not to bind - a
    /// <c>DisableBind=1</c> connection parameter means the runtime uses no bind variables
    /// [<c>n_cst_thread_task_sqlbase.sru:L128-L129</c>]. That interpolation is the mechanical root of the
    /// injection exposure. It is ALSO observable: the interpolated text is what a SQL-preview hook sees,
    /// what a database-error payload's statement field carries, and what a characterization recording
    /// compares. So neither form can be dropped - one is the behaviour, the other is the safe execution
    /// path - and this method produces both from ONE pass so they cannot disagree.
    /// </para>
    /// <para>
    /// <b>ONE PLACEHOLDER PER PARAMETER, NOT PER OCCURRENCE.</b> A named parameter fills every
    /// placeholder sharing its name [<c>:L460, :L472</c>], and the parameterized form reproduces that by
    /// reusing the same placeholder name at each occurrence with a single bound value - which is exactly
    /// what a named parameter means to the provider. A positional parameter fills one placeholder and
    /// therefore contributes one occurrence.
    /// </para>
    /// <para>
    /// <b>The two texts are spliced with INDEPENDENT offset tracks.</b> A rendered literal and a
    /// placeholder name almost never have the same length, so the shift applied to later positions
    /// [<c>:L467-L470</c>] differs between the two forms. Sharing one track would corrupt whichever form
    /// it was not computed for, so each carries its own - which is the only structural difference between
    /// this method and the oracle's single-form loop.
    /// </para>
    /// </remarks>
    protected long BindParams(
        string sql,
        long dbType,
        string dwArgString,
        out SqlBoundStatement statement)
    {
        ArgumentNullException.ThrowIfNull(sql);
        ArgumentNullException.ThrowIfNull(dwArgString);

        statement = SqlBoundStatement.Unbound(sql);

        // [:L398] `sqlParams = _sqlParams` - a PowerBuilder array assignment, and therefore a COPY. It
        // has to be one: the names below are rewritten from the DataWindow argument list and the task's
        // own parameters must not change as a result.
        List<SqlTaskParameter> sqlParams = [.. SnapshotParams()];

        // [:L400-L401]
        int count = OneBasedIndex.UpperBound(sqlParams);
        if (count == OneBasedIndex.EmptyUpperBound)
        {
            return RetCode.FAILED;
        }

        // [:L402-L406] count the NAMED parameters.
        int namedCount = 0;
        for (int index = OneBasedIndex.FirstIndex; index <= count; index++)
        {
            if (sqlParams[index - OneBasedIndex.FirstIndex].Name.Length > 0)
            {
                namedCount++;
            }
        }

        // [:L407] all named or none named; a MIXTURE is refused, because a positional parameter would
        // race the named ones for the same placeholder.
        if (namedCount > 0 && namedCount != count)
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        // [:L410-L416] name the positional parameters from the DataWindow argument list.
        if (dwArgString.Length > 0 && namedCount == 0)
        {
            // [:L411]
            if (ParseDataWindowArguments(dwArgString, out IReadOnlyList<string> dwArgs, out _) == 0)
            {
                return RetCode.E_INVALID_ARGUMENT;
            }

            // [:L412] the counts must agree EXACTLY - fewer arguments than parameters is as wrong as more.
            if (OneBasedIndex.UpperBound(dwArgs) != count)
            {
                return RetCode.E_OUT_OF_BOUND;
            }

            // [:L413-L415]
            for (int index = OneBasedIndex.FirstIndex; index <= count; index++)
            {
                sqlParams[index - OneBasedIndex.FirstIndex] =
                    sqlParams[index - OneBasedIndex.FirstIndex] with
                    {
                        Name = dwArgs[index - OneBasedIndex.FirstIndex],
                    };
            }
        }

        // [:L418-L451] locate the placeholders. ONE scan, and the positions it produces seed BOTH
        // offset tracks below - so the two forms can never disagree about where a placeholder was.
        if (!TryLocatePlaceholders(sql, out List<SqlPlaceholder> placeholders))
        {
            // [:L451] `if bQtUnclose then return RetCode.E_INVALID_ARGUMENT`
            return RetCode.E_INVALID_ARGUMENT;
        }

        // The observable form the oracle produces, and the executable form beside it. The executable one
        // starts as the same text and diverges only where a placeholder is replaced - by a rendered
        // literal in one, by a provider placeholder name in the other.
        string observable = sql;
        string bound = sql;
        List<SqlPlaceholder> boundPlaceholders = [.. placeholders];
        List<SqlBoundParameter> boundParameters = [];

        // [:L453] `nArgCnt = UpperBound(sqlArgs)`
        // [:L454] `//if nArgCnt < nCount then return RetCode.E_OUT_OF_BOUND` - COMMENTED OUT in the
        //         oracle and carried across INERT (constraint C-B). Leaving it inert is what lets a
        //         statement carry FEWER placeholders than there are parameters; the surplus parameters
        //         simply match nothing, and only an unmatched PLACEHOLDER is an error [:L477-L479].
        int argCount = OneBasedIndex.UpperBound(placeholders);

        // ==========================================================================================
        //  THE TOLERANCE IS PRESERVED, AND IT IS NO LONGER SILENT
        //  ------------------------------------------------------------------------------------------
        //  The guard above stays commented out, because the oracle's author commented it out and
        //  constraint C-B forbids reviving a decision the legacy made. It is also LOAD-BEARING here and
        //  not merely tolerated: the DataWindow-argument path names every parameter from
        //  `DataWindow.Table.Arguments` and requires the ARGUMENT count to match the PARAMETER count
        //  exactly [:L412], while the statement itself is free to reference only some of those arguments -
        //  so a retrieval whose DataWindow declares three arguments and whose SQL mentions two is a
        //  legitimate shape that an active guard would refuse.
        //
        //  WHAT IS FIXED IS THE SILENCE, NOT THE OUTCOME. A caller that supplies more parameters than the
        //  statement has placeholders had no way to learn that the surplus matched nothing: the answer was
        //  a plain success. The condition is now recorded, so the mismatch is diagnosable from the service
        //  log while the accepted outcome is exactly what it was.
        //
        //  C-F: COUNTS ONLY. No parameter value, no parameter name and no statement text appears in this
        //  record - a surplus parameter's VALUE is precisely the kind of live data the redaction policy
        //  exists to keep out of logs, and the counts are sufficient to identify the mismatch.
        // ==========================================================================================
        if (argCount < count && _logger.IsEnabled(LogLevel.Warning))
        {
            _logger.LogWarning(
                "SQL parameter binding received {ParameterCount} parameters for a statement carrying "
                    + "{PlaceholderCount} placeholders. The {SurplusCount} surplus parameters match "
                    + "nothing and are ignored, which is preserved legacy behaviour "
                    + "[n_cst_thread_task_sqlbase.sru:L454, commented out in the oracle].",
                count,
                argCount,
                count - argCount);
        }

        // [:L456-L475] substitute.
        for (int paramIndex = OneBasedIndex.FirstIndex; paramIndex <= count; paramIndex++)
        {
            SqlTaskParameter parameter = sqlParams[paramIndex - OneBasedIndex.FirstIndex];
            bool isPositional = parameter.Name.Length == 0;

            // Per PARAMETER, not per occurrence: the name is minted on first use and the value carried
            // once, so a named parameter filling three placeholders yields ONE bound value.
            string? boundName = null;
            bool boundValueCarried = false;

            for (int argIndex = OneBasedIndex.FirstIndex; argIndex <= argCount; argIndex++)
            {
                SqlPlaceholder placeholder = placeholders[argIndex - OneBasedIndex.FirstIndex];

                // [:L459]
                if (placeholder.Replaced)
                {
                    continue;
                }

                // [:L460] a name match, or ANY unreplaced placeholder for a positional parameter.
                if (!isPositional
                    && !string.Equals(placeholder.Name, parameter.Name, StringComparison.Ordinal))
                {
                    continue;
                }

                // [:L462] render the literal.
                string? replacement = ParamToString(parameter.Value, dbType);
                if (replacement is null)
                {
                    // THE ONE NARROWING - see the remarks. The oracle would splice a null here and
                    // nullify the whole statement while still answering success. The partial rewrite is
                    // published so a caller inspecting the statement sees what the oracle would have.
                    statement = new SqlBoundStatement(observable, bound, boundParameters);

                    return RetCode.E_SQL_BIND_ARG_FAILED;
                }

                // ONE bound name per PARAMETER: a named parameter reuses it at every occurrence, which is
                // exactly what a named provider parameter means, so the value is carried once.
                // ONE-BASED, AND THE OFF-BY-ONE HERE WOULD BE SILENT. The name minted here has to be
                // the name SqlCommandText.BindTo binds, because the bound statement is carried to the
                // engine through that type and the engine binds POSITIONALLY - it re-derives each
                // parameter's name from its ordinal rather than reading the name recorded here. A
                // zero-based name would still produce a statement that LOOKS right and a parameter
                // collection that LOOKS right, and every execution would then fail on an unbound
                // placeholder. Sql/SqlUpdateCarrier's generated statements use the same convention,
                // so there is exactly one placeholder spelling in this service.
                boundName ??= string.Concat(
                    BoundParameterPrefix,
                    (boundParameters.Count + 1).ToString(CultureInfo.InvariantCulture));

                if (!boundValueCarried)
                {
                    boundParameters.Add(new SqlBoundParameter(boundName, parameter.Value));
                    boundValueCarried = true;
                }

                // [:L464] `sql = Left(sql, pos - 1) + sReplace + Mid(sql, pos + length)`.
                // Positions are ONE-BASED PowerBuilder string offsets, converted here and only here.
                int spliceStart = (int)placeholder.Position - OneBasedIndex.FirstIndex;
                int spliceEnd = spliceStart + (int)placeholder.Length;
                observable = string.Concat(
                    observable.AsSpan(0, spliceStart),
                    replacement,
                    observable.AsSpan(spliceEnd));

                // The same splice on the executable form, against ITS OWN position track, because a
                // placeholder name and a rendered literal have different lengths.
                SqlPlaceholder boundPlaceholder = boundPlaceholders[argIndex - OneBasedIndex.FirstIndex];
                int boundStart = (int)boundPlaceholder.Position - OneBasedIndex.FirstIndex;
                int boundEnd = boundStart + (int)boundPlaceholder.Length;
                bound = string.Concat(
                    bound.AsSpan(0, boundStart),
                    boundName,
                    bound.AsSpan(boundEnd));

                // [:L466]
                placeholder.Replaced = true;
                placeholders[argIndex - OneBasedIndex.FirstIndex] = placeholder;

                boundPlaceholder.Replaced = true;
                boundPlaceholders[argIndex - OneBasedIndex.FirstIndex] = boundPlaceholder;

                // [:L467-L470] shift every LATER placeholder by the length difference.
                long offset = replacement.Length - placeholder.Length;
                long boundOffset = boundName.Length - boundPlaceholder.Length;
                for (int laterIndex = argIndex + 1; laterIndex <= argCount; laterIndex++)
                {
                    SqlPlaceholder later = placeholders[laterIndex - OneBasedIndex.FirstIndex];
                    later.Position += offset;
                    placeholders[laterIndex - OneBasedIndex.FirstIndex] = later;

                    SqlPlaceholder laterBound = boundPlaceholders[laterIndex - OneBasedIndex.FirstIndex];
                    laterBound.Position += boundOffset;
                    boundPlaceholders[laterIndex - OneBasedIndex.FirstIndex] = laterBound;
                }

                // [:L471-L472] a positional parameter fills exactly one placeholder and stops; a named
                // one keeps going and fills every placeholder sharing its name.
                if (isPositional)
                {
                    break;
                }
            }
        }

        // Published before the final check, so an unmatched-placeholder refusal still carries the
        // progressive rewrite the oracle would have left behind.
        statement = new SqlBoundStatement(observable, bound, boundParameters);

        // [:L476-L479] every placeholder must have been substituted.
        for (int argIndex = OneBasedIndex.FirstIndex; argIndex <= argCount; argIndex++)
        {
            if (!placeholders[argIndex - OneBasedIndex.FirstIndex].Replaced)
            {
                return RetCode.E_OUT_OF_BOUND;
            }
        }

        // [:L481]
        return RetCode.OK;
    }

    /// <summary>
    /// The two-argument overload - the port of
    /// <c>n_cst_thread_task_sqlbase.sru:L484-L485</c>, which forwards an empty argument list.
    /// </summary>
    /// <param name="sql">The statement, rewritten in place.</param>
    /// <param name="dbType">The dialect.</param>
    /// <returns>Whatever the three-argument overload returns.</returns>
    protected long BindParams(ref string sql, long dbType) => BindParams(ref sql, dbType, string.Empty);

    /// <summary>
    /// The three-argument dual-form overload - an empty DataWindow argument list, both statement forms.
    /// </summary>
    /// <param name="sql">The statement to bind.</param>
    /// <param name="dbType">The dialect.</param>
    /// <param name="statement">The bound statement, in both forms.</param>
    /// <returns>Whatever the four-argument overload returns.</returns>
    protected long BindParams(string sql, long dbType, out SqlBoundStatement statement) =>
        BindParams(sql, dbType, string.Empty, out statement);

    /// <summary>
    /// Locates every placeholder in a statement - the port of the scan at
    /// <c>n_cst_thread_task_sqlbase.sru:L418-L451</c>.
    /// </summary>
    /// <param name="sql">The statement to scan.</param>
    /// <param name="placeholders">
    /// Receives the placeholders in the order they appear, each with its ONE-BASED position, its length
    /// including the prefix, and its lower-cased name.
    /// </param>
    /// <returns>
    /// <see langword="false"/> when the statement ends inside a quoted run [<c>:L451</c>]; otherwise
    /// <see langword="true"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The scan is a single left-to-right pass over characters, holding at most one open placeholder.
    /// Reaching a delimiter CLOSES the open placeholder [<c>:L435-L439</c>] and may immediately OPEN a
    /// new one when that delimiter is the prefix [<c>:L440-L444</c>], which is what makes the prefix's
    /// own membership in the delimiter set load-bearing.
    /// </para>
    /// <para>
    /// <b>IT COLLECTS BOTH OF C-07's PLACEHOLDER FORMS.</b> The colon form is the oracle's, opened and
    /// then closed by the next delimiter. The positional question mark is emitted ALREADY COMPLETE with an
    /// empty name and a length of one, because it has neither a name nor a terminator - see
    /// <see cref="PositionalArgMarker"/> for why that form belongs to C-07 at all and why it needed no new
    /// matching rule. The two interleave freely and appear in statement order, which is what positional
    /// consumption depends on.
    /// </para>
    /// <para>
    /// <b>The final flush at <c>:L447-L450</c> uses <c>nPos</c> AFTER the loop, which PowerScript leaves
    /// at <c>Len(sql) + 1</c></b> - one past the end. That is what gives a placeholder ending at the
    /// statement's last character its correct length, and it is reproduced here explicitly with the same
    /// value rather than being papered over with a special case.
    /// </para>
    /// </remarks>
    private static bool TryLocatePlaceholders(string sql, out List<SqlPlaceholder> placeholders)
    {
        placeholders = [];

        // [:L419] `nIndex = 0` - zero means "no placeholder currently open".
        int openIndex = 0;

        // [:L391-L392] the quote state: whether a run is open, and which character opened it.
        bool quoteUnclosed = false;
        char quoteChar = '\0';

        // [:L420-L421] `nLen = Len(sql)` then `for nPos = 1 to nLen` - ONE-BASED.
        int length = sql.Length;
        int position = OneBasedIndex.FirstIndex;
        for (; position <= length; position++)
        {
            // [:L422]
            char word = sql[position - OneBasedIndex.FirstIndex];

            // [:L423-L424] only a delimiter does anything at all; every other character is passed over.
            if (!WordDelimiters.Contains(word))
            {
                continue;
            }

            // [:L425-L434] quote bookkeeping, for the two quote characters only.
            if (word is '\'' or '"')
            {
                // [:L426] a run is toggled only by the character that opened it, or by any quote when no
                // run is open.
                if (quoteChar == word || quoteChar == '\0')
                {
                    quoteUnclosed = !quoteUnclosed;
                    quoteChar = quoteUnclosed ? word : '\0';
                }
            }

            // [:L435-L439] close the open placeholder: its length reaches up to this delimiter, and its
            // name is the text after the prefix.
            if (openIndex > 0)
            {
                ClosePlaceholder(sql, placeholders, openIndex, position);
                openIndex = 0;
            }

            // [:L440-L444] open a new placeholder - unless we are inside a quoted run, in which case the
            // prefix is ordinary text.
            if (word == ArgPrefix[0])
            {
                // [:L441]
                if (quoteUnclosed)
                {
                    continue;
                }

                // [:L442-L443]
                placeholders.Add(new SqlPlaceholder { Name = string.Empty, Position = position });
                openIndex = OneBasedIndex.UpperBound(placeholders);
            }

            // ==========================================================================================
            //  C-07's POSITIONAL FORM - ADDED HERE, AND ADDED AS A COMPLETE PLACEHOLDER
            //  ------------------------------------------------------------------------------------------
            //  See PositionalArgMarker for why this arm exists at all: C-07 is the union of the
            //  transaction object's named command surface (this file's oracle) and the SQLite binding's
            //  ANONYMOUS one [n_sqlite.sru:L32-L43], and the legacy's own primary fixture uses the
            //  anonymous form [w_test_sqlite.srw:L396-L406].
            //
            //  IT IS EMITTED COMPLETE RATHER THAN OPENED, and that is the whole trick. A colon placeholder
            //  is OPEN when it is seen and only the NEXT delimiter tells it where it ends and what it is
            //  called; a question mark has no name and ends where it begins, so opening it would make the
            //  next delimiter overwrite its length with the distance to that delimiter and give it a name
            //  taken from the intervening text. Emitting it complete - empty name, its own position, width
            //  one, unreplaced - leaves `openIndex` at zero, so the close arm above never touches it.
            //
            //  THE QUOTE TEST IS THE COLON ARM'S, FOR THE SAME REASON [:L441]. Inside a quoted run the
            //  character is ordinary text - `VALUES ('what?')` carries no placeholder - and the run state
            //  is already tracked above because the marker is a delimiter in the oracle's own list [:L424].
            //
            //  AFTER THE CLOSE ARM, NOT BEFORE IT. `":a?"` must close `:a` first and then emit the
            //  positional one, so the two appear in the order they occur in the statement - which is the
            //  order positional consumption depends on.
            // ==========================================================================================
            else if (word == PositionalArgMarker && !quoteUnclosed)
            {
                placeholders.Add(new SqlPlaceholder
                {
                    Name = string.Empty,
                    Position = position,
                    Length = PositionalArgMarkerLength,
                });
            }
        }

        // [:L447-L450] the final flush. `position` is now Len(sql) + 1, exactly as PowerScript leaves
        // its loop variable, which is what makes the length arithmetic come out right.
        if (openIndex > 0)
        {
            ClosePlaceholder(sql, placeholders, openIndex, position);
        }

        // [:L451]
        return !quoteUnclosed;
    }

    /// <summary>
    /// Whether a statement carries a placeholder that NOTHING will bind - the pre-execution diagnostic
    /// for a statement submitted with no parameters at all.
    /// </summary>
    /// <param name="sql">
    /// The statement, with any leading execution-mode selector ALREADY REMOVED - see the remarks.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the statement carries one of the two placeholder forms C-07 publishes,
    /// outside every quoted run and every comment.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="sql"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>WHY THIS IS NOT <see cref="BindParams(string, long, string, out SqlBoundStatement)"/>'s OWN
    /// SCAN.</b> That scan is the oracle's, character for character, and it runs only when parameters
    /// exist - the oracle's collection guard is <c>if nCount = 0 then return RetCode.FAILED</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L400-L401</c>] and the caller gates on
    /// <c>if of_HasParams()</c> [<c>n_cst_thread_task_sqlcommand.sru:L81</c>]. So a statement carrying
    /// placeholders and NO parameters was never scanned, never rewritten, and went to the provider with
    /// its markers intact - where <c>Microsoft.Data.Sqlite</c> refuses it with an
    /// <see cref="InvalidOperationException"/> that used to escape as an UNHANDLED fault. This method is
    /// what lets that population be refused with the contract's own binding code instead, which is the
    /// SAME code the populated-collection path already answers for the same condition (an unmatched
    /// placeholder [<c>:L476-L479</c>]) - so the fix removes an inconsistency rather than adding a rule.
    /// </para>
    /// <para>
    /// <b>IT IS DELIBERATELY MORE LITERATE ABOUT SQL THAN THE ORACLE'S SCAN, AND ONLY BECAUSE IT MUST BE
    /// SAFER.</b> The oracle's scan tracks quoted runs and nothing else, which is fine there: it only
    /// ever decides where to SUBSTITUTE a value the caller supplied. This method decides whether to
    /// REFUSE, so a false positive would reject a statement the provider accepts. It therefore skips
    /// single-quoted strings, double-quoted and backtick-quoted and bracket-quoted identifiers, <c>--</c>
    /// line comments and <c>/* */</c> block comments - the constructs in which SQLite's tokenizer does not
    /// see a parameter either.
    /// </para>
    /// <para>
    /// <b>IT RECOGNISES EXACTLY THE TWO FORMS THE CONTRACT PUBLISHES, AND NOT SQLite's OTHER THREE.</b>
    /// <c>?</c> and <c>:name</c> are C-07's [<c>persistence.v1.proto</c>, <c>n_sqlite.sru:L32-L43</c>].
    /// SQLite additionally accepts <c>@name</c> and <c>$name</c>, and those are deliberately NOT matched
    /// here: <c>@</c> in particular would misfire on a doubled execution-mode selector, whose documented
    /// behaviour is that "a doubled selector is NOT an escape sequence, and the survivor reaches the
    /// provider and fails there". Those forms still fail with a DEFINED status rather than a fault,
    /// through <c>Data/SqliteTransactionEngine</c>'s bind-fault projection - they simply fail at the
    /// provider, exactly as documented, instead of being pre-refused here.
    /// </para>
    /// <para>
    /// <b>THE SELECTOR MUST ALREADY BE GONE.</b> The caller strips a leading execution-mode selector
    /// before this runs, so that a statement that legitimately begins with one is not read as though the
    /// selector were part of its grammar.
    /// </para>
    /// </remarks>
    protected static bool ContainsUnboundStatementParameterMarker(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);

        for (int index = 0; index < sql.Length; index++)
        {
            char current = sql[index];

            switch (current)
            {
                // The three runs SQLite closes with the character that opened them, and inside which a
                // doubled delimiter is an escaped literal rather than a terminator.
                case '\'':
                case '"':
                case '`':
                    index = SkipDelimitedRun(sql, index, current, doubledEscape: true);
                    continue;

                // A bracket-quoted identifier closes with the OTHER bracket and has no doubling rule.
                case '[':
                    index = SkipDelimitedRun(sql, index, ']', doubledEscape: false);
                    continue;

                // `--` to end of line. The guard is what stops a lone minus - an arithmetic operator -
                // being read as the start of a comment.
                case '-' when index + 1 < sql.Length && sql[index + 1] == '-':
                    index = SkipLineComment(sql, index);
                    continue;

                // `/* ... */`, likewise guarded so a division operator is not mistaken for an opener.
                case '/' when index + 1 < sql.Length && sql[index + 1] == '*':
                    index = SkipBlockComment(sql, index);
                    continue;

                // The anonymous form. A bare marker IS a parameter in SQLite - nothing needs to follow it.
                case PositionalArgMarker:
                    return true;

                // The named form. A NAME MUST FOLLOW: a colon with nothing name-shaped after it is not a
                // parameter to SQLite, and refusing on it would misfire on punctuation.
                case ':' when index + 1 < sql.Length && IsParameterNameCharacter(sql[index + 1]):
                    return true;

                default:
                    continue;
            }
        }

        return false;
    }

    /// <summary>
    /// Advances past a delimited run, returning the index of its last character.
    /// </summary>
    /// <param name="sql">The statement being scanned.</param>
    /// <param name="openIndex">The index of the opening delimiter.</param>
    /// <param name="closer">The character that closes the run.</param>
    /// <param name="doubledEscape">
    /// Whether a doubled closing delimiter is an escaped literal that keeps the run open.
    /// </param>
    /// <returns>
    /// The index of the closing delimiter, or the last index of the statement when the run is never
    /// closed.
    /// </returns>
    /// <remarks>
    /// <b>AN UNTERMINATED RUN SWALLOWS THE REMAINDER, WHICH IS THE SAFE DIRECTION HERE.</b> The statement
    /// is malformed either way, and treating the rest as quoted means this method answers "no unbound
    /// marker" and lets the provider report the real syntax error - rather than answering "unbound marker"
    /// and reporting a binding failure for a statement whose actual fault is an unclosed quote.
    /// </remarks>
    private static int SkipDelimitedRun(string sql, int openIndex, char closer, bool doubledEscape)
    {
        for (int index = openIndex + 1; index < sql.Length; index++)
        {
            if (sql[index] != closer)
            {
                continue;
            }

            if (doubledEscape && index + 1 < sql.Length && sql[index + 1] == closer)
            {
                // Consume both halves of the escaped delimiter and stay inside the run. The loop's own
                // increment supplies the second character's advance.
                index++;

                continue;
            }

            return index;
        }

        return sql.Length - 1;
    }

    /// <summary>
    /// Advances past a <c>--</c> line comment, returning the index of its last character.
    /// </summary>
    /// <param name="sql">The statement being scanned.</param>
    /// <param name="openIndex">The index of the first minus.</param>
    /// <returns>
    /// The index of the terminating line break, or the last index of the statement when the comment runs
    /// to the end.
    /// </returns>
    private static int SkipLineComment(string sql, int openIndex)
    {
        int terminator = sql.AsSpan(openIndex).IndexOfAny('\n', '\r');

        return terminator < 0 ? sql.Length - 1 : openIndex + terminator;
    }

    /// <summary>
    /// Advances past a <c>/* */</c> block comment, returning the index of its last character.
    /// </summary>
    /// <param name="sql">The statement being scanned.</param>
    /// <param name="openIndex">The index of the solidus that opens it.</param>
    /// <returns>
    /// The index of the terminator's second character, or the last index of the statement when the
    /// comment is never closed - which SQLite also tolerates by treating it as running to the end.
    /// </returns>
    private static int SkipBlockComment(string sql, int openIndex)
    {
        int terminator = sql.IndexOf("*/", openIndex + 2, StringComparison.Ordinal);

        return terminator < 0 ? sql.Length - 1 : terminator + 1;
    }

    /// <summary>
    /// Whether a character may appear in a SQLite parameter name.
    /// </summary>
    /// <param name="candidate">The character to test.</param>
    /// <returns><see langword="true"/> for an ASCII letter, an ASCII digit or an underscore.</returns>
    /// <remarks>
    /// SQLite's tokenizer accepts the identifier alphabet after a named-parameter introducer. Only the
    /// FIRST character after the introducer is tested by the caller, which is all that is needed to tell a
    /// parameter from punctuation.
    /// </remarks>
    private static bool IsParameterNameCharacter(char candidate) =>
        char.IsAsciiLetterOrDigit(candidate) || candidate == '_';

    /// <summary>
    /// Finalises the placeholder that is currently open - the shared body of
    /// <c>n_cst_thread_task_sqlbase.sru:L436-L437</c> and its identical repeat at <c>:L448-L449</c>.
    /// </summary>
    /// <param name="sql">The statement being scanned.</param>
    /// <param name="placeholders">The placeholders collected so far.</param>
    /// <param name="openIndex">The ONE-BASED position of the open placeholder in that collection.</param>
    /// <param name="terminatorPosition">
    /// The ONE-BASED offset that terminates it - the delimiter's own offset mid-scan, or
    /// <c>Len(sql) + 1</c> at the final flush.
    /// </param>
    /// <remarks>
    /// Factored out because the oracle writes the same two lines twice, once inside the loop and once
    /// after it, and a duplicate is how the two come to disagree. The name excludes the prefix
    /// character: the oracle takes <c>Mid(sql, pos + 1, length - 1)</c>, and since
    /// <see cref="SqlPlaceholder.Position"/> is one-based, <c>pos + 1</c> one-based is <c>pos</c>
    /// zero-based.
    /// </remarks>
    private static void ClosePlaceholder(
        string sql,
        List<SqlPlaceholder> placeholders,
        int openIndex,
        int terminatorPosition)
    {
        SqlPlaceholder open = placeholders[openIndex - OneBasedIndex.FirstIndex];

        // [:L436] `sqlArgs[nIndex].length = nPos - sqlArgs[nIndex].pos`
        open.Length = terminatorPosition - open.Position;

        // [:L437] `sqlArgs[nIndex].name = Lower(Mid(sql, pos + 1, length - 1))`
        open.Name = LowerName(sql.Substring((int)open.Position, (int)open.Length - 1));

        placeholders[openIndex - OneBasedIndex.FirstIndex] = open;
    }

    #endregion

    #region The DataWindow argument list, and retrieval with matched parameters

    /// <summary>
    /// Splits a DataWindow argument list into names and types - the port of <c>_of_parsedwargs</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L573-L595</c>].
    /// </summary>
    /// <param name="argString">
    /// The value <c>Describe("DataWindow.Table.Arguments")</c> answers: newline-separated pairs, each a
    /// name and a type separated by a tab.
    /// </param>
    /// <param name="args">Receives the argument NAMES, lower-cased, in declaration order.</param>
    /// <param name="argTypes">Receives the argument TYPES, lower-cased, in the same order.</param>
    /// <returns>The number of pairs recognised - <c>UpperBound(args)</c> [<c>:L594</c>].</returns>
    /// <remarks>
    /// <para>
    /// <b>The WHOLE string is lower-cased first, names and types together</b> - <c>Lower(argString +
    /// "~n")</c> [<c>:L576</c>] - which is why an argument name compares equal to a parameter name that
    /// <see cref="AddParam"/> also lower-cased. The appended newline is what makes the final pair
    /// terminate like every other one, so the loop needs no special last case.
    /// </para>
    /// <para>
    /// <b>A pair with a missing tab, an empty name or an empty type is SILENTLY SKIPPED</b>
    /// [<c>:L583-L586</c>] rather than reported. With no tab, <c>Left(pair, -1)</c> answers the empty
    /// string in PowerBuilder and the pair fails the name test; the port reproduces the skip directly.
    /// The count returned is therefore the number of ACCEPTED pairs, which is what
    /// <see cref="BindParams(ref string, long, string)"/> compares against its parameter count
    /// [<c>:L412</c>].
    /// </para>
    /// <para>
    /// <see langword="internal"/> rather than private because the binder, the retrieval matcher and the
    /// unit tests all exercise it, and because it is a pure function of its input - no clock, no state,
    /// no I/O.
    /// </para>
    /// </remarks>
    internal static int ParseDataWindowArguments(
        string? argString,
        out IReadOnlyList<string> args,
        out IReadOnlyList<string> argTypes)
    {
        List<string> names = [];
        List<string> types = [];

        // [:L576] lower-case the whole list and append the terminating newline.
        string normalized = LowerName(argString) + ArgumentPairSeparator;

        // [:L577-L589] walk pair by pair. `lastPosition` is the ONE-BASED offset of the previous
        // separator, starting at 0 so the first pair begins at offset 1.
        int lastPosition = 0;
        int separator = normalized.IndexOf(ArgumentPairSeparator, StringComparison.Ordinal) + 1;
        while (separator > 0)
        {
            // [:L579] `Mid(argString, nLastPos + 1, nPos - nLastPos - 1)`
            string pair = normalized.Substring(lastPosition, separator - lastPosition - 1);

            // [:L580-L582] the tab splits name from type; both must be non-empty.
            int tabPosition = pair.IndexOf(ArgumentNameTypeSeparator, StringComparison.Ordinal) + 1;
            string name = tabPosition > 0 ? pair[..(tabPosition - 1)] : string.Empty;
            string type = tabPosition > 0 ? pair[tabPosition..] : pair;

            // [:L583-L586]
            if (name.Length > 0 && type.Length > 0)
            {
                names.Add(name);
                types.Add(type);
            }

            // [:L587-L588]
            lastPosition = separator;
            // `Pos(argString, "~n", nPos + 1)` [:L588]. The char overload is ordinal by definition, so
            // there is no comparison to state; the three-argument StringComparison overload does not
            // exist for a char and would not mean anything if it did.
            separator = separator < normalized.Length
                ? normalized.IndexOf(ArgumentPairSeparator, separator) + 1
                : 0;
        }

        // [:L591-L594]
        args = names;
        argTypes = types;
        return OneBasedIndex.UpperBound(names);
    }

    /// <summary>
    /// Matches the held parameters onto the data object's retrieval arguments and retrieves - the port of
    /// <c>_of_retrievewithparams</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L597-L705</c>].
    /// </summary>
    /// <param name="data">The store to retrieve into. The legacy parameter is <c>readonly datastore</c>.</param>
    /// <returns>
    /// The retrieved row count, or a negative value on failure - the DataWindow convention, not the
    /// return-code algebra.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Three matching strategies, tried in the oracle's order. FIRST, when ANY parameter is named
    /// [<c>:L606-L611</c>] and there are at least as many parameters as arguments [<c>:L615</c>], each
    /// argument position is filled by NAME [<c>:L616-L623</c>], falling back to the parameter at the
    /// SAME POSITION when no name matches [<c>:L625-L627</c>]. SECOND, if that produced nothing - because
    /// no parameter was named, or because there were fewer parameters than arguments - every parameter is
    /// copied POSITIONALLY [<c>:L632-L636</c>]. THIRD, no arguments and no parameters retrieves with an
    /// empty list [<c>:L651-L652</c>].
    /// </para>
    /// <para>
    /// <b>⚠ THE POSITIONAL FALLBACK IS A SECOND LOOP-VARIABLE-OUTLIVES-ITS-LOOP DEPENDENCY.</b>
    /// <c>if nParmIdx &gt; nParmCnt</c> [<c>:L625</c>] tests the INNER loop's counter AFTER that loop
    /// ended: it exceeds the count only when the loop ran to completion without an <c>exit</c>, which is
    /// exactly "no name matched". A C# <c>for</c> scopes its counter, so the test is not expressible and
    /// a naive port loses the fallback silently. Reproduced below with an explicit found flag - the same
    /// hazard, and the same remedy, as <see cref="SetParam(string, object?)"/>.
    /// </para>
    /// <para>
    /// <b>THE 20-ARM UNROLL AND ITS DYNAMIC-INVOCATION FALLBACK HAVE NOTHING TO PORT (constraint
    /// C-D).</b> The oracle spells out <c>Data.Retrieve(...)</c> twenty-one times, once per arity from
    /// zero to twenty [<c>:L649-L693</c>], and past twenty falls back to <c>n_scriptinvoker</c>
    /// [<c>:L694-L702</c>]. Both exist for ONE reason: PowerScript cannot forward an
    /// arbitrary-length argument list. C# can, so the whole construction collapses into a single call
    /// taking the list, and Persistence acquires NO ScriptBridge coupling. <b>The ceiling of 20 is
    /// RECORDED as a legacy limit and DELIBERATELY NOT ENFORCED</b> - as are the sibling ceilings of 8
    /// in <c>n_cst_thread_trans</c> and 11 in <c>n_sqlite</c>. Exceeding them is not a behavioural
    /// regression, because the legacy's own dynamic fallback exceeded them too.
    /// </para>
    /// </remarks>
    protected ValueTask<long> RetrieveWithParamsAsync(
        ISqlDataStore data,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);

        // [:L603]
        int dwArgCount = ParseDataWindowArguments(
            data.Describe(DataWindowProperty.TableArguments),
            out IReadOnlyList<string> dwArgs,
            out _);

        // [:L605]
        int paramCount = OneBasedIndex.UpperBound(_sqlParams);

        // [:L606-L611] `bHasNamedParm` - true as soon as ONE parameter carries a name.
        bool hasNamedParam = false;
        for (int paramIndex = OneBasedIndex.FirstIndex; paramIndex <= paramCount; paramIndex++)
        {
            if (_sqlParams[paramIndex - OneBasedIndex.FirstIndex].Name.Length > 0)
            {
                hasNamedParam = true;

                // [:L609] `exit`
                break;
            }
        }

        List<object?> matched = [];

        // [:L613-L630] the by-name strategy, under its own comment 自动匹配DW参数索引.
        if (hasNamedParam && paramCount >= dwArgCount)
        {
            for (int dwArgIndex = OneBasedIndex.FirstIndex; dwArgIndex <= dwArgCount; dwArgIndex++)
            {
                string dwArgName = dwArgs[dwArgIndex - OneBasedIndex.FirstIndex];

                // [:L617-L623] `//查找参数` - find the parameter of that name.
                bool found = false;
                for (int paramIndex = OneBasedIndex.FirstIndex; paramIndex <= paramCount; paramIndex++)
                {
                    SqlTaskParameter parameter = _sqlParams[paramIndex - OneBasedIndex.FirstIndex];
                    if (string.Equals(parameter.Name, dwArgName, StringComparison.Ordinal))
                    {
                        // [:L620-L621]
                        matched.Add(parameter.Value);
                        found = true;
                        break;
                    }
                }

                // [:L624-L627] `//没有找到按顺序取参` - not found, so take the parameter at the SAME
                // POSITION. Safe because paramCount >= dwArgCount gates the whole block.
                if (!found)
                {
                    matched.Add(_sqlParams[dwArgIndex - OneBasedIndex.FirstIndex].Value);
                }
            }
        }

        // [:L632-L636] nothing matched by name, so copy every parameter positionally.
        if (OneBasedIndex.UpperBound(matched) == OneBasedIndex.EmptyUpperBound)
        {
            for (int paramIndex = OneBasedIndex.FirstIndex; paramIndex <= paramCount; paramIndex++)
            {
                matched.Add(_sqlParams[paramIndex - OneBasedIndex.FirstIndex].Value);
            }
        }

        // [:L637] `nParmCnt = UpperBound(aParams)` - the count is re-read from the MATCHED list, which
        // may be shorter or longer than the parameter list.

        // -----------------------------------------------------------------------------------------
        //  [:L639-L647] CARRIED ACROSS INERT (constraint C-B). The oracle's parameter-type-mismatch
        //  check is COMMENTED OUT, verbatim:
        //
        //      //检查数窗参数类型和传参数据类型是否一致
        //      //if nParmCnt >= nDwArgCnt then
        //      //   for nParmIdx = 1 to nParmCnt
        //      //      if sDwArgTypes[nParmIdx] <> ClassName(aParams[nParmIdx]) then
        //      //         errInfo = Sprintf("参数类型不匹配: 期望 {} 实际 {}", sDwArgTypes[nParmIdx],
        //      //                           ClassName(_sqlParams[nParmIdx].value))
        //      //         return -1
        //      //      next
        //      //end if
        //
        //  IT IS NOT REVIVED. Reviving dormant validation would reject retrievals the oracle performs
        //  happily - a silent behavioural change dressed as robustness, which is precisely what C-B
        //  forbids. The types ARE parsed and ARE available from ParseDataWindowArguments; they are
        //  simply not compared, exactly as here.
        // -----------------------------------------------------------------------------------------

        // [:L649-L702] collapsed to one variadic call - see the remarks. The argument MATCHING above is
        // synchronous and stays that way; only the call itself awaits, which keeps this method's whole
        // parity surface - the matching rules - testable against a runtime that touches no database (C-H).
        return data.RetrieveAsync(matched, cancellationToken);
    }

    #endregion

    #region The per-thread datastore cache, its affinity selection, and its PRESERVED DEFECT

    /// <summary>
    /// Creates a result store at the affinity of the thread this task runs on - the port of the carrier
    /// selection at <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L553-L557</c>.
    /// </summary>
    /// <returns>A store whose carrier matches the current thread's affinity.</returns>
    /// <remarks>
    /// <para>
    /// The oracle:
    /// </para>
    /// <code>
    /// if #ParentThread.of_IsMainThread() then
    ///     ds = Create n_cst_thread_task_sqlbase_ds        // MAIN-thread carrier          [:L554]
    /// else
    ///     ds = Create n_cst_thread_task_sqlbase_ds_mt     // WORKER-thread carrier        [:L556]
    /// end if
    /// </code>
    /// <para>
    /// <b>⚠ THE `_mt` NAMING TRAP, RESTATED HERE BECAUSE THIS IS THE SELECTION SITE. `_mt` DOES NOT MEAN
    /// "MAIN THREAD".</b> Verified from the export headers:
    /// <c>n_cst_thread_task_sqlbase_ds</c> is annotated <c>[运行在主线程]</c> - MAIN thread - and is
    /// declared <c>from datastore</c>; <c>n_cst_thread_task_sqlbase_ds_mt</c> is annotated
    /// <c>[运行在子线程]</c> - WORKER thread - and is declared
    /// <c>from n_cst_thread_task_sqlbase_ds</c>. So the MAIN thread creates the plain one, a WORKER
    /// creates the <c>_mt</c> one, and the WORKER variant DERIVES FROM the MAIN one. A reader who
    /// guesses from the suffix inverts the entire selection, and the inversion is silent: both types
    /// answer every buffer question identically and differ only in cancellation checks, progress
    /// throttling and the row cap.
    /// </para>
    /// <para>
    /// <b>THERE ARE THREE SUCH SELECTION SITES IN THE LEGACY LIBRARY, NOT ONE</b> -
    /// <c>n_cst_thread_task_sqlbase.sru:L553-L557</c>,
    /// <c>n_cst_thread_task_sqlquery.sru:L541-L548</c> and
    /// <c>n_cst_thread_task_sqlupdate.sru:L307-L314</c> - and every one is subject to the trap. All
    /// three route through this single protected helper and then through
    /// <see cref="ISqlDataStoreFactory"/> onto the sibling <see cref="DataWindowCarrierFactory"/>, so the
    /// trap is encoded ONCE. <b>No carrier type is declared in this file</b>; the two carriers belong to
    /// <c>Buffers/DataWindowBuffers.cs</c> and are constructed only there.
    /// </para>
    /// </remarks>
    protected ISqlDataStore CreateDataStore() =>
        _dataStoreFactory.Create(
            _host.IsMainThread
                // [:L554] the MAIN-thread carrier - the base type, NOT the `_mt` one.
                ? CarrierThreadAffinity.MainThread

                // [:L556] the WORKER-thread carrier - the `_mt` type, which derives from the above.
                : CarrierThreadAffinity.WorkerThread);

    /// <summary>
    /// Resolves the shared, per-thread cache of result stores, creating it on first use - the port of the
    /// cache lookup at <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L531-L537</c>.
    /// </summary>
    /// <returns>The cache, keyed by data-object name.</returns>
    /// <remarks>
    /// <para>
    /// The container is the legacy <c>n_map</c>, substituted by
    /// <see cref="OrderedMap"/>. Two of its properties matter here. Its positional accessors are
    /// ONE-BASED with an inclusive <see cref="OrderedMap.Count"/> bound - not used by this path, which
    /// is keyed, but part of the contract if a later path enumerates it. And
    /// <see cref="OrderedMap.Add"/> FAILS on an existing key rather than overwriting, which is exactly
    /// what the oracle wants at <c>:L565</c>: the add happens only on the miss path, so a key can never
    /// already be present.
    /// </para>
    /// <para>
    /// <b>The cache lives on the THREAD, not on the task</b> [<c>:L531-L536</c>], which is what lets
    /// successive tasks on one worker reuse a store instead of rebuilding it. That is also why its
    /// contents are LIVE STORE REFERENCES and why its lifetime is part of the hazard-2 contract
    /// [<c>docs/PB多线程绕坑提示.md:L5</c>].
    /// </para>
    /// </remarks>
    private OrderedMap GetDataStoreCache()
    {
        OrderedMap? cache = null;

        // [:L531-L533]
        if (_host.HasData(DataStoreCacheKey))
        {
            cache = _host.GetData(DataStoreCacheKey) as OrderedMap;
        }

        // [:L534-L537] `if Not IsValidObject(mapCache) then` - note the oracle re-tests validity rather
        // than trusting the presence test, because keyed data can hold a destroyed object.
        if (!Predicates.IsValidObject(cache))
        {
            cache = new OrderedMap();
            _host.SetData(DataStoreCacheKey, cache);
        }

        return cache!;
    }

    /// <summary>
    /// Returns the cached result store for a data object, creating and snapshotting it on first use - the
    /// port of <c>of_getcacheds</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L527-L571</c>].
    /// </summary>
    /// <param name="dataObject">
    /// The data-object name. The legacy parameter is <c>readonly string dataobject</c>.
    /// </param>
    /// <returns>The store, initialised against this task.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dataObject"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Two paths. On a HIT [<c>:L539-L551</c>] the store's statement, sort and filter are compared against
    /// the snapshots taken at first use and put back where they differ, and the carrier's per-execution
    /// state is cleared. On a MISS [<c>:L552-L566</c>] a store is created at the right affinity, the name
    /// is assigned, the three snapshots are taken and sentinel-normalised, and the entry is added.
    /// </para>
    /// <para>
    /// <b>Both paths then converge</b> [<c>:L568</c>]: the store's init event fires on the hit path AND on
    /// the miss path. <b><c>ClearState</c> is HIT-ONLY</b> [<c>:L551</c>] - a freshly created store has no
    /// state to clear, so calling it on the miss path too would be harmless and still wrong, because it
    /// would stop being a signal that the store is a reused one.
    /// </para>
    /// </remarks>
    protected ISqlDataStore GetCacheDataStore(string dataObject)
    {
        ArgumentNullException.ThrowIfNull(dataObject);

        OrderedMap cache = GetDataStoreCache();
        ISqlDataStore store;

        // [:L539-L540] `if mapCache.Exists(dataObject) then cacheDS = mapCache.Get(dataObject)`.
        // The oracle assigns the retrieved value straight into a CACHEDS structure, so a key holding
        // anything else is a state PowerBuilder itself would fault on. Here the type test is explicit,
        // and a key that exists while holding something else is EVICTED so the miss path below can add a
        // sound entry - otherwise Add would refuse the existing key and the cache would stay corrupt for
        // the life of the thread. No reachable legacy path produces that state; this only stops an
        // impossible one from becoming permanent.
        object? cachedEntry = cache.Exists(dataObject) ? cache.Get(dataObject) : null;
        if (cachedEntry is not null && cachedEntry is not CachedDataStore)
        {
            _logger.LogWarning(
                "The datastore cache entry for {DataObject} was not a cache record and has been evicted.",
                dataObject);
            cache.Remove(dataObject);
            cachedEntry = null;
        }

        if (cachedEntry is CachedDataStore cached)
        {
            // [:L540-L541]
            store = cached.DataStore;

            // [:L542-L544] restore the statement when it drifted.
            if (!string.Equals(cached.OrigSql, store.GetSqlSelect(), StringComparison.Ordinal))
            {
                // [:L543] the oracle composes the script by hand, quotes included:
                //     ds.Modify('DataWindow.Table.Select = "' + cacheDS.origSQL + '"')
                store.Modify(DataWindowProperty.TableSelect + " = \"" + cached.OrigSql + "\"");
            }

            // =====================================================================================
            //  🔴 PRESERVED LEGACY DEFECT - n_cst_thread_task_sqlbase.sru:L545-L546
            //  -------------------------------------------------------------------------------------
            //  The oracle reads:
            //
            //      if cacheDS.origSort <> ds.Describe("DataWindow.Table.Sort") then   [:L545]
            //          ds.SetFilter(cacheDS.origSort)                                 [:L546]
            //      end if
            //
            //  Line :L545 compares the SORT. Line :L546 then restores it with SetFilter - NOT SetSort,
            //  which the surface plainly has (ISqlDataStore.SetSort, called by the query task at
            //  n_cst_thread_task_sqlquery.sru:L570). SetSort was clearly what was intended.
            //
            //  THIS DEFECT IS UNDOCUMENTED IN THE LEGACY. It appears nowhere in docs/**, nowhere in
            //  logfile.md and in no source comment; it was found by direct reading of the oracle.
            //
            //  WHAT ACTUALLY HAPPENS, mechanism first. The SORT IS NEVER RESTORED - nothing on this
            //  path ever calls SetSort - and the FILTER IS TRANSIENTLY OVERWRITTEN WITH THE SORT
            //  STRING here. Because :L548 then RE-READS the filter, it will usually now differ from
            //  origFilter and be corrected at :L549. But WHEN origFilter HAPPENS TO EQUAL THE SORT
            //  STRING, :L548 is false and the filter is LEFT HOLDING THE SORT STRING.
            //
            //  DO NOT CORRECT THIS - C-B FORBIDS CORRECTING LEGACY DEFECTS. The four statements below
            //  reproduce the oracle's own sequence in the oracle's own order - compare sort, set
            //  FILTER to the sort value, compare filter, set filter to the filter value, clear state -
            //  so that the behaviour above EMERGES exactly as it does in the legacy rather than being
            //  encoded from a description of it. The pinning test is
            //  CacheHit_ChangedSort_InstallsSortAsFilter_PreservedLegacyDefect.
            // =====================================================================================

            // [:L545] compares the SORT ...
            if (!string.Equals(
                    cached.OrigSort,
                    store.Describe(DataWindowProperty.TableSort),
                    StringComparison.Ordinal))
            {
                // [:L546] ... and restores it with SetFilter. THE DEFECT. Preserved verbatim.
                store.SetFilter(cached.OrigSort);
            }

            // [:L548-L550] the filter comparison and restore, which ARE correctly paired.
            if (!string.Equals(
                    cached.OrigFilter,
                    store.Describe(DataWindowProperty.TableFilter),
                    StringComparison.Ordinal))
            {
                store.SetFilter(cached.OrigFilter);
            }

            // [:L551] HIT PATH ONLY.
            store.ClearState();
        }
        else
        {
            // [:L553-L557] the affinity selection, including the `_mt` trap - see CreateDataStore.
            store = CreateDataStore();

            // [:L558]
            store.DataObject = dataObject;

            // [:L560-L564] take the three snapshots, normalising the two describe results.
            CachedDataStore entry = new(
                store,
                store.GetSqlSelect(),
                NormalizeDescribeSentinel(store.Describe(DataWindowProperty.TableSort)),
                NormalizeDescribeSentinel(store.Describe(DataWindowProperty.TableFilter)));

            // [:L565] Add, not Set: on this path the key cannot already be present.
            cache.Add(dataObject, entry);
        }

        // [:L568] BOTH paths converge here.
        store.OnInit(this);

        // [:L570]
        return store;
    }

    /// <summary>
    /// Normalises a PowerBuilder describe sentinel to the empty string - the port of
    /// <c>n_cst_thread_task_sqlbase.sru:L562</c> and its twin at <c>:L564</c>.
    /// </summary>
    /// <param name="describeResult">The raw describe result.</param>
    /// <returns>
    /// The empty string when the result is <c>"!"</c> or <c>"?"</c>; otherwise the result unchanged.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The oracle, twice and identically:
    /// </para>
    /// <code>
    /// if cacheDS.origSort   = "!" or cacheDS.origSort   = "?" then cacheDS.origSort   = ""   [:L562]
    /// if cacheDS.origFilter = "!" or cacheDS.origFilter = "?" then cacheDS.origFilter = ""   [:L564]
    /// </code>
    /// <para>
    /// <b>Both sentinels are PowerBuilder's, and both are easy to miss (constraint C-B).</b> <c>"!"</c> is
    /// what <c>Describe</c> answers for a property it cannot evaluate, and <c>"?"</c> is what it answers
    /// when the value is not applicable or is inconsistent. Normalising them matters because the SNAPSHOT
    /// is later compared against a LIVE describe on the hit path: a snapshot holding <c>"!"</c> would
    /// never equal a live empty answer, so the restore branch would fire on every single hit and install
    /// the sentinel text as a sort or filter expression.
    /// </para>
    /// <para>
    /// Applied ONLY to the snapshots, exactly as the oracle applies it. The hit path's live describes at
    /// <c>:L545</c> and <c>:L548</c> are NOT normalised, and that asymmetry is preserved.
    /// </para>
    /// </remarks>
    internal static string NormalizeDescribeSentinel(string? describeResult) =>
        describeResult is "!" or "?" ? string.Empty : describeResult ?? string.Empty;

    #endregion

    #region The retrieval hook - resolution, the decline contract, and deterministic release

    /// <summary>
    /// Resolves the retrieval hook named by a caller-supplied class name - the port of
    /// <c>if _sHookClass &lt;&gt; "" then hook = Create Using _sHookClass</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L516-L517</c>].
    /// </summary>
    /// <param name="hookClassName">
    /// The class name, as installed through <c>of_sethookclass</c> [<c>:L428</c>] and republished by
    /// contract C-05 as <c>QuerySpec.hook_class</c>.
    /// </param>
    /// <returns>
    /// The hook, or <see langword="null"/> when none is named or the name is not sanctioned - both of
    /// which reproduce the legacy state in which <c>IsValid(hook)</c> is false and the DEFAULT retrieval
    /// runs [<c>:L755-L761</c>].
    /// </returns>
    /// <remarks>
    /// <b>Constraint C-G.</b> A refused name is NOT an error and NOT an exception: it degrades to the
    /// default retrieval, which is exactly what the legacy does when creation yields an invalid object.
    /// The activator's allowlist is what keeps a caller-supplied string from becoming an arbitrary type
    /// activation - see <see cref="SqlRetrievalHookActivator"/>.
    /// </remarks>
    protected ISqlRetrievalHook? ResolveRetrievalHook(string? hookClassName)
    {
        if (_hookActivator.TryCreate(hookClassName, out ISqlRetrievalHook? hook))
        {
            return hook;
        }

        if (!string.IsNullOrWhiteSpace(hookClassName))
        {
            _logger.LogWarning(
                "Retrieval hook class {HookClass} is not registered; the default retrieval will run.",
                hookClassName);
        }

        return null;
    }

    /// <summary>
    /// Whether a hook class name is ADMISSIBLE - blank, or naming a sanctioned hook.
    /// </summary>
    /// <param name="hookClassName">The caller-supplied name.</param>
    /// <returns>
    /// <see langword="true"/> when the name is blank, meaning "no hook", or names a registered hook.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>THE SETTER'S GUARD, AND THE REASON IT IS AT THE SETTER RATHER THAN AT RETRIEVAL.</b> A hook
    /// class name is caller-controlled input, and the activator is an allowlist, so an unregistered name
    /// can never produce a hook. Storing it anyway and discovering that at retrieval time means the
    /// retrieval RUNS - with no hook, and reporting success - so a caller who asked for a hook is told
    /// its request succeeded while the behaviour it asked for silently did not happen. Refusing at the
    /// setter turns that into an argument error the caller can act on, at the point it can still act.
    /// </para>
    /// <para>
    /// <b>BLANK IS ADMISSIBLE AND MUST STAY SO (C-B).</b> The oracle's own guard is
    /// <c>if _sHookClass &lt;&gt; "" then</c> [<c>n_cst_thread_task_sqlquery.sru:L516</c>] - a blank name
    /// means "no hook", which is the ordinary case, and refusing it would reject every caller that simply
    /// does not want one.
    /// </para>
    /// </remarks>
    protected bool IsAdmissibleHookClass(string? hookClassName) =>
        string.IsNullOrWhiteSpace(hookClassName) || _hookActivator.IsRegistered(hookClassName);

    /// <summary>
    /// Whether a hook result means "NOT HANDLED - run the default retrieval".
    /// </summary>
    /// <param name="hookResult">
    /// The hook's answer, or <see langword="null"/> when no hook ran - which is the legacy
    /// <c>SetNull(nRowCnt)</c> arm [<c>n_cst_thread_task_sqlquery.sru:L759</c>].
    /// </param>
    /// <returns><see langword="true"/> when the default retrieval must run.</returns>
    /// <remarks>
    /// <para>
    /// The port of the test at <c>n_cst_thread_task_sqlquery.sru:L761</c>:
    /// </para>
    /// <code>
    /// if IsNull(nRowCnt) or nRowCnt = RetCode.E_NO_IMPLEMENTATION then  ... default retrieval ...
    /// </code>
    /// <para>
    /// <b>A hook answering <see cref="RetCode.E_NO_IMPLEMENTATION"/> HAS NOT FAILED.</b> The value means
    /// "I did not handle this", and the correct response is to run the default path - not to report an
    /// error, and emphatically not to test it with <see cref="Predicates.IsFailed(long?)"/>, which would
    /// answer true for it and turn every decline into a failure. Exposed as a named predicate so no
    /// derived task has to remember that, and so the test suite can pin it directly.
    /// </para>
    /// </remarks>
    internal static bool HookDeclinedRetrieval(long? hookResult) =>
        hookResult is null || hookResult == RetCode.E_NO_IMPLEMENTATION;

    /// <summary>
    /// Releases a retrieval hook - the port of <c>if IsValid(hook) then Destroy hook</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L873</c>], which the oracle
    /// executes in its cleanup path.
    /// </summary>
    /// <param name="hook">
    /// The hook to release. Passed by <see langword="ref"/> and set to <see langword="null"/> on return,
    /// so a caller cannot keep using a released hook.
    /// </param>
    /// <remarks>
    /// <b>Deterministic, per hazard 2 [<c>docs/PB多线程绕坑提示.md:L5</c>].</b> A hook that implements
    /// <see cref="IDisposable"/> is disposed here and now, rather than being left to the collector, and
    /// the reference is cleared in the same breath. A hook that does not implement it simply has its
    /// reference cleared, which is all <c>Destroy</c> can be said to mean for an object with no
    /// resources.
    /// </remarks>
    protected static void ReleaseRetrievalHook(ref ISqlRetrievalHook? hook)
    {
        if (hook is IDisposable disposable)
        {
            disposable.Dispose();
        }

        hook = null;
    }

    #endregion

    #region Lifecycle - and the one place where the ORDERING is the contract

    /// <summary>
    /// The prepare hook - the port of <c>event onprepare</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L725-L729</c>].
    /// </summary>
    /// <returns>Zero, exactly as the oracle returns [<c>:L728</c>].</returns>
    /// <remarks>
    /// <para>
    /// <b>The ancestor runs FIRST</b> - the oracle is written
    /// <c>event onprepare;call super::onprepare;...</c> [<c>:L725</c>], so the super call precedes the
    /// body. The body then resets the commit signal IF ONE EXISTS [<c>:L725-L727</c>], never creating it.
    /// </para>
    /// <para>
    /// <b>Zero here is the SUBSTRATE's continue convention, not <see cref="RetCode.OK"/></b> - the two
    /// share a value and not a meaning, and the caller-side proxy's own prepare handler returns the same
    /// zero under the comment <c>//continue</c> [<c>n_cst_threading_task_sqlbase.sru:L211</c>]. It is
    /// deliberately not spelled as a return code.
    /// </para>
    /// </remarks>
    protected virtual long OnPrepare()
    {
        // [:L725] `call super::onprepare` - FIRST.
        _host.OnPrepare();

        // [:L725-L727]
        _committedSignal?.Reset();

        // [:L728]
        return 0L;
    }

    /// <summary>
    /// The uninit hook - the port of <c>event onuninit</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L715-L723</c>].
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The ancestor runs FIRST</b> [<c>:L715</c>], then four actions in the oracle's order: drop the
    /// pool reference [<c>:L716</c>], zero the reference index [<c>:L717</c>], NULL THE TRANSACTION
    /// REFERENCE [<c>:L718</c>], and close the commit handle [<c>:L720-L722</c>].
    /// </para>
    /// <para>
    /// <b>This method IS hazard 2 [<c>docs/PB多线程绕坑提示.md:L5</c>].</b> The document requires a worker
    /// that was handed a main-thread object to null its reference in exactly this event, and the oracle
    /// does so with <c>SetNull(_transObject)</c>. The .NET analogue is DETERMINISTIC clearing on teardown
    /// - never a reliance on the collector - and it is not optional tidying: the pool may hand the same
    /// transaction to another task the moment its reference count drops, and a stale reference here would
    /// let a finished task write through it.
    /// </para>
    /// <para>
    /// The commit signal is DISPOSED as well as dropped, because <see cref="ManualResetEventSlim"/> owns
    /// an operating-system wait handle once anyone has waited on it - the same resource
    /// <c>CloseHandle(_hEvtCommitted)</c> releases [<c>:L721</c>].
    /// </para>
    /// </remarks>
    protected virtual void OnUninit()
    {
        // [:L715] `call super::onuninit` - FIRST.
        _host.OnUninit();

        // [:L715-L719]
        if (_transactionLease.IsValid)
        {
            GetTransactionPool().RemoveRef(_transactionLease);
            _transactionLease = PoolLease.None;

            // [:L718] `SetNull(_transObject)` - hazard 2, deterministic.
            _transactionObject = null;
        }

        // [:L720-L722] `if _hEvtCommitted <> 0 then CloseHandle(_hEvtCommitted)`
        if (_committedSignal is not null)
        {
            _committedSignal.Dispose();
            _committedSignal = null;
        }
    }

    /// <summary>
    /// The composition root's entry point into <see cref="OnPrepare"/> - the port of the substrate
    /// DISPATCHING <c>onprepare</c> before it runs the task body.
    /// </summary>
    /// <returns><see cref="OnPrepare"/>'s answer, which is the substrate's continue convention.</returns>
    /// <remarks>
    /// <para>
    /// <b>Why this exists at all.</b> <see cref="OnPrepare"/> is <see langword="protected"/> because the
    /// oracle declares it as an EVENT, and an event is raised by the substrate rather than called by a
    /// peer. This service has no PowerBuilder substrate, so the composition root is the substrate, and
    /// this is the seam it raises the event through. Without it the hook is unreachable and the commit
    /// signal is never re-armed between dispatches - which is precisely the defect this member fixes.
    /// </para>
    /// <para>
    /// <b>Per DISPATCH, not per task.</b> The oracle's prepare event fires once for every run of the
    /// task body, and its body resets the commit signal [<c>:L725-L727</c>] so that an
    /// <see cref="SqlTaskProxyBase.IsCommitted"/> reading cannot survive from one dispatch into the
    /// next. A caller therefore raises this immediately BEFORE each execution, never once per lifetime.
    /// </para>
    /// </remarks>
    internal long RunPrepare() => OnPrepare();

    /// <summary>
    /// The composition root's entry point into <see cref="OnUninit"/> - the port of the substrate
    /// DISPATCHING <c>onuninit</c> as the worker session tears down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Per TASK LIFETIME, not per dispatch - and the asymmetry with <see cref="RunPrepare"/> is
    /// deliberate.</b> The oracle's uninit event releases the pooled transaction reference and CLOSES
    /// the commit handle [<c>:L716-L722</c>]. Raising it between dispatches would destroy the signal the
    /// next dispatch needs, so it belongs on the teardown path only. <see cref="Dispose(bool)"/> routes
    /// through it, which is what makes the hook unconditional: every disposal path - a clean release, a
    /// session end, or a faulted run unwinding through a <see langword="finally"/> - reaches it.
    /// </para>
    /// <para>
    /// Idempotent by construction: the pooled reference is guarded on a positive index and the commit
    /// signal on a non-null reference, so a second raise is a no-op rather than a double release.
    /// </para>
    /// </remarks>
    internal void RunUninit() => OnUninit();

    /// <summary>
    /// The error hook - the port of <c>event onerror</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L731-L735</c>].
    /// </summary>
    /// <param name="errCode">The framework error code.</param>
    /// <param name="errInfo">The diagnostic text.</param>
    /// <returns>The ANCESTOR's return value, verbatim [<c>:L734</c>].</returns>
    /// <remarks>
    /// <para>
    /// <b>⚠ THE ORDERING HERE IS INVERTED RELATIVE TO EVERY OTHER LIFECYCLE HOOK, AND IT IS OBSERVABLE.
    /// DO NOT REORDER.</b> The oracle is:
    /// </para>
    /// <code>
    /// event onerror;//overried
    /// of_Rollback()                 &lt;-- FIRST, before the ancestor            [:L732]
    /// call super::OnError;          &lt;-- THEN the ancestor                     [:L733]
    /// return AncestorReturnValue    &lt;-- and ITS value is the answer           [:L734]
    /// </code>
    /// <para>
    /// <see cref="OnPrepare"/> and <see cref="OnUninit"/> both call the ancestor FIRST; this one calls it
    /// LAST. The reason the difference matters: the rollback must happen while the transaction is still
    /// attached, because the ancestor's error handling may notify, unwind or tear down, and a rollback
    /// attempted afterwards would find nothing to roll back. Swapping the two lines would leave the
    /// transaction open on the error path - a change no compiler and no unit test on the return value
    /// alone would notice, because the RETURN VALUE is unaffected.
    /// </para>
    /// <para>
    /// The rollback's own result is DISCARDED, exactly as the oracle discards it: a task with no
    /// transaction answers <see cref="RetCode.E_INVALID_TRANSACTION"/> from
    /// <see cref="Rollback"/> and that is not an error condition on this path.
    /// </para>
    /// </remarks>
    protected virtual long OnError(long errCode, string errInfo)
    {
        // [:L732] ROLLBACK FIRST. The result is deliberately discarded.
        Rollback();

        // [:L733] THEN the ancestor.
        long ancestorReturnValue = _host.OnError(errCode, errInfo);

        // [:L734] and the ancestor's value is what this hook answers.
        return ancestorReturnValue;
    }

    #endregion

    #region Disposal - the .NET half of the deterministic-clearing contract

    /// <summary>
    /// Releases everything this task holds, deterministically.
    /// </summary>
    /// <remarks>
    /// Not a substitute for <see cref="OnUninit"/> - it is the safety net for a task that is discarded
    /// without its lifecycle running to completion. Both are idempotent, so a task that ran uninit and is
    /// then disposed does no work twice.
    /// </remarks>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the commit signal and clears every cross-boundary reference.
    /// </summary>
    /// <param name="disposing">
    /// <see langword="true"/> when called from <see cref="Dispose()"/> rather than from a finalizer.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>Disposal RAISES <see cref="OnUninit"/> rather than re-implementing it.</b> The two once
    /// carried the same three actions side by side, which left the hook itself unreachable on every
    /// path that only disposed the task - so a derived override never ran, and neither did
    /// <c>_host.OnUninit()</c>. Routing through the hook makes teardown unconditional: a clean release,
    /// a session end, and a faulted run unwinding through a <see langword="finally"/> all reach the same
    /// single teardown, in the oracle's order. The hook is idempotent, so an explicit
    /// <see cref="RunUninit"/> followed by disposal releases once.
    /// </para>
    /// <para>
    /// The reason teardown must happen here at all is hazard 2's: a reference that outlives the task is
    /// the failure mode, and a task discarded on an exception path never reached uninit. The transaction
    /// itself is NOT disposed - it belongs to the pool, and this task only ever borrowed it.
    /// </para>
    /// <para>
    /// <see cref="_transactionPool"/>, <see cref="_dataStoreFactory"/>, <see cref="_hookActivator"/> and
    /// <see cref="_timeProvider"/> are shared, container-owned collaborators and are likewise not
    /// disposed: disposing an injected singleton from a transient consumer would break every other
    /// consumer of it.
    /// </para>
    /// </remarks>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            // THROUGH THE HOOK, NOT ALONGSIDE IT, and the difference is the whole point of this arm.
            // Hand-rolling the three actions here reaches `_host.OnUninit()` on NEITHER path and reaches
            // a derived override on neither either - so a task that was disposed rather than explicitly
            // torn down skipped every observer of its own teardown while still releasing its resources,
            // which is the kind of omission nothing downstream can detect. The hook releases the pooled
            // reference on a valid lease, clears the transaction reference rather than leaving it for the
            // collector (hazard 2 [docs/PB多线程绕坑提示.md:L5]), and closes the commit signal - and every
            // one of those is guarded, so raising it twice absorbs the repeat instead of double-releasing.
            OnUninit();
        }

        _disposed = true;
    }

    #endregion
}

#endregion
