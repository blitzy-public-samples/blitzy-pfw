// =================================================================================================
//  Tasks/TaskProxies/SqlCommandTaskProxy.cs - the caller-side COMMAND proxy.
// =================================================================================================
//  PROVENANCE. The port of ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlcommand.sru
//  (58 lines), the SMALLEST of the four objects in this folder. Its worker counterpart
//  ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlcommand.sru (116 lines) is ported
//  SEPARATELY as Tasks/SqlCommandTask.cs - see the affinity note below for why that separation is a
//  contract and not a layout preference. Both objects, and every other ws_objects/** path and
//  docs/PB多线程绕坑提示.md cited here, are READ-ONLY behavioural oracle (C-C): never edited, never
//  moved, never reformatted. Every behavioural claim below carries a locator that was opened and
//  read against the file it names.
//
//  WHY THIS FILE IS SEPARATE FROM ITS WORKER: THREAD AFFINITY IS A CONTRACT, NOT COMMENTARY.
//  The legacy encodes required execution context in $PBExportComments, and across the FIFTEEN
//  objects of ws_objects/pfw.thread.ext.pbl.src the split MEASURES as:
//
//      [运行在子线程]     worker thread   6   n_cst_thread_task_sqlbase, _sqlbase_ds_mt,
//                                            _sqlbase_hook, _sqlcommand, _sqlquery, _sqlupdate
//      [运行在主线程]     main thread     1   n_cst_thread_task_sqlbase_ds - EXACTLY ONE
//      [运行在当前线程]   calling thread  4   the four proxies of this folder
//
//  This file's own marker is [运行在当前线程] - the CALLING thread - at
//  n_cst_threading_task_sqlcommand.sru:L2, and its worker's is [运行在子线程] - the CHILD thread - at
//  n_cst_thread_task_sqlcommand.sru:L2. Every concurrency class in the legacy exists TWICE, once per
//  side, precisely so that no object is ever touched from two threads.
//
//  THE PROXY/WORKER PAIR IS NOT COLLAPSED INTO ONE ASYNC METHOD.
//  The .NET expression of the pair is async request/response with a CancellationToken while
//  PRESERVING BOTH TYPES AND THE BOUNDARY BETWEEN THEM. The proof that a single fused method cannot
//  express this is the TWO-SIDED RESET, which lives on the base and is inherited unchanged here:
//  n_cst_threading_task_sqlbase.sru:L53-L68 guards on its OWN busy state [:L57], DELEGATES the reset
//  to the worker [:L61], ABANDONS on the worker's refusal while returning the worker's code verbatim
//  [:L62], and only THEN clears its own caller-side state [:L64-L65]. That failure mode - worker
//  refused, caller-side state untouched - requires two distinct state sets, so a fused method could
//  not have it at all. Tasks/TaskProxies/SqlTaskProxyBase.cs carries the full argument; this file
//  simply must not undo it. Concretely: SqlCommandTaskProxy and Tasks/SqlCommandTask.cs are two
//  DISTINCT types, and a composition root registers BOTH (C-J).
//
//  THE TWO THREADING HAZARDS [docs/PB多线程绕坑提示.md, 5 lines, read-only], acknowledged because
//  they shape this folder even where this particular file does not exercise them:
//    * HAZARD 1 [:L1-L4] A worker thread synchronously calling a MAIN-THREAD object's function or
//      event whose return type is `string` or `blob` can fault - the stated trigger being a Post
//      message that has not executed for an object which is itself already destroyed - and the
//      stated guidance is to return via `ref` instead. THIS PROXY DECLARES NO PAYLOAD-BEARING
//      CALLBACK OF ITS OWN: the oracle's whole event surface here is the single inherited
//      `ondberror` and the class-name answer at :L56, and its two functions take a string and a
//      number INWARD and return a numeric code. The hazard is nonetheless why the folder's LARGE
//      payloads travel by reference rather than as return values - the query proxy's
//      `onchilddatareceived(string name, ref blob blbdata)` and `ondatachunk(ref blob blbdata, ...)`
//      [n_cst_threading_task_sqlquery.sru:L10, :L13] - and a command carries no result set, which is
//      exactly why this file has no such member to write.
//    * HAZARD 2 [:L5] After a global object is passed to a worker thread, the worker must set the
//      referenced main-thread object to NULL in its uninit event to release the reference. The .NET
//      analogue is to clear cross-boundary references EXPLICITLY on teardown and never rely on the
//      collector. This type adds NO cross-boundary reference of its own - it holds no field
//      whatsoever - so the base's Dispose contract is inherited unchanged and correctly covers
//      everything there is to clear. Adding a Dispose override here would be empty ceremony.
//
//  CONSTRAINTS. There are NO user rules for this project: review_rules returns exactly one line
//  saying "No user rules provided.", and nothing is invented, inferred or back-filled in their
//  place. The binding set is the enterprise-standard baseline plus the named non-rule constraints,
//  and the ruling FOR THIS FILE is:
//    C-A/C-I  Only SqlTaskProxyBase, Tasks/SqlCommandTask.cs and the five project edges the .csproj
//             declares are referenced. No peer service, no PowerFramework.Shared.Localization, and NO
//             new PackageReference. PowerFramework.Shared.Eventful is reached only through the
//             notification surface inherited from the base, whose header records why that edge exists.
//             Builds clean under TreatWarningsAsErrors with nullable enabled, in Debug and in Release.
//    C-B      Two legacy asymmetries are reproduced and annotated at the point of reproduction,
//             never harmonised: the DEBUG-GATED assertion on the statement setter [:L36-L38]
//             standing beside the WHOLLY UNVALIDATED auto-commit setter [:L43-L46]; and the fact
//             that the WORKER rejects an empty statement at runtime with E_INVALID_SQL
//             [n_cst_thread_task_sqlcommand.sru:L45] while this proxy only asserts in DEBUG - so a
//             Debug build and a Release build genuinely differ in what an empty statement does.
//    C-C      The legacy tree is read-only and is the oracle. Locators, never edits.
//    C-D      No deferred-service coupling. n_scriptinvoker is NOT ported - PowerBuilder's variadic
//             escape hatch has no C# analogue, so there is nothing to port and no ScriptBridge edge
//             is created - and pfw.utility.regexp is not reached either, so no Documents edge is
//             created. Neither appears anywhere in this file.
//    C-E      No connection is opened and no engine is selected. The command STATEMENT passes
//             through as an opaque string; executing it belongs to the worker over Transactions/.
//    C-F      No credential literal appears here in any form, including commented out. This file
//             never touches TransactionData.LogPass and never reads, renders, logs or returns the
//             latched error payload - so the fully bound statement text the command path's failure
//             arm carries cannot escape through it. The one sanctioned exit for that payload remains
//             the base's GetLastDbErrorForWire(), whose projection applies the sealed mask in
//             Errors/SqlRedactor.cs.
//    C-G      No listener, no route, no endpoint. This type is reachable only through the authorized
//             C-07 gRPC methods under Grpc/.
//    C-H      The injected TimeProvider - reachable as the base's Clock - is the ONLY clock any
//             proxy here may read, and THIS FILE READS NO CLOCK AT ALL: no DateTime.UtcNow, no
//             DateTime.Now, no Environment.TickCount and no Stopwatch appears anywhere in it, which
//             matches the oracle, where a sweep of n_cst_threading_task_sqlcommand.sru finds no
//             CPU(), Now() or Today() call. Both collaborators - the threading substrate and the
//             worker - are substitutable seams, so every behaviour below is testable with NO REAL
//             THREAD AND NO DATABASE.
//    C-J      The legacy auto-instance `global n_cst_threading_task_sqlcommand
//             n_cst_threading_task_sqlcommand` [:L11] is NOT reproduced. This is a DI-registered
//             type with no static mutable state.
//    C-K      Every technology-specific and boundary-specific decision is recorded with its
//             evidence rather than asserted - the affinity census above, the non-flattening
//             decision, the auto-commit values with their translated legacy semantics, both
//             threading hazards, and the accessibility and declaration-site choices below.
//    R9       One-based indexing is the refactor's most dangerous mechanical hazard. THIS FILE
//             PERFORMS NO INDEX ARITHMETIC AND NO POSITIONAL ACCESS WHATSOEVER, and forwards none:
//             the oracle's proxy declares no parameter accessor and no row or column ordinal
//             [:L26-L28], so there is nothing here to rebase. Reverse iteration is never to be
//             "corrected" anywhere in this service - see the note on the base's RaiseNotify.
//    .editorconfig  TaskProxies/ sits OUTSIDE every naming-suppression glob - those exist only for
//             the handful of files listed there by path, of which only RetCode.cs and Enums.cs are
//             in Shared.Kernel - while TreatWarningsAsErrors is true. So declaring TASK_TYPE,
//             AC_OFF, AC_ON, AC_NATIVE or any other SCREAMING_SNAKE identifier here would be a
//             BUILD BREAK rather than a style nit. Legacy constant VALUES are preserved exactly;
//             the legacy SPELLINGS become PascalCase, per AAP 0.4.5.3.
//
//  WHERE THE AUTO-COMMIT TRIPLE WENT (C-K).
//  The oracle re-exports it here as three aliases of the worker's own constants [:L19-L21]:
//
//      constant long AC_OFF    = n_cst_thread_task_sqlcommand.AC_OFF      value 0
//      constant long AC_ON     = n_cst_thread_task_sqlcommand.AC_ON       value 1
//      constant long AC_NATIVE = n_cst_thread_task_sqlcommand.AC_NATIVE   value 2
//
//  and the values live on the worker at n_cst_thread_task_sqlcommand.sru:L16-L18, where AC_ON
//  carries the source comment 开启事务并自动提交 - "begin a transaction and auto-commit" - and
//  AC_NATIVE carries 不开启事务 - "do NOT begin a transaction". NEITHER THE CONSTANTS NOR AN
//  EQUIVALENT ENUM IS DECLARED IN THIS FILE. They are declared ONCE, in the published protocol
//  definition, as `enum AutoCommitMode { AC_OFF = 0; AC_ON = 1; AC_NATIVE = 2; }`
//  [shared/PowerFramework.Contracts/Proto/persistence.v1.proto:L1841-L1849], and this file
//  REFERENCES the generated enum - exactly as Tasks/SqlCommandTask.cs does. That is a FAITHFUL
//  expression of the legacy alias relationship rather than a departure from it: the oracle's point
//  was that both sides name ONE set of values, and both sides now name one generated enum. THE
//  VALUES ARE PRESERVED (0/1/2); ONLY THE DECLARATION SITE MOVES, which the .editorconfig ruling
//  above makes mandatory rather than optional.
// =================================================================================================

using Microsoft.Extensions.Logging;
using PowerFramework.Contracts.Persistence.V1;

// PowerFramework.Contracts.Common.V1 also declares a RetCode, so the kernel's constant class is
// reached through an alias rather than a namespace import - the same resolution
// Tasks/TaskProxies/SqlTaskProxyBase.cs and Tasks/SqlCommandTask.cs both use, kept identical so all
// three files read the same way. PowerFramework.Shared.Kernel.Predicates is deliberately NOT
// imported: this file makes no sign test of its own - it returns the worker's code verbatim - and an
// unused import is not permitted here.
using RetCode = PowerFramework.Shared.Kernel.RetCode;

#if DEBUG
// A DEBUG-ONLY import, scoped exactly as narrowly as the single caller it serves. The assertion at
// :L36-L38 sits inside the oracle's own `#IF DEFINED DEBUG` block, so in a Release build this file
// has no use for the diagnostics project at all and the import would be dead. Aliasing rather than
// importing the namespace also states the correct spelling once: the type is `Assertions`, NOT
// `Assert` - a static class cannot contain a member of its own name (CS0542), so
// shared/PowerFramework.Shared.Diagnostics/Assert.cs declares `public static class Assertions` and
// callers write Assertions.Assert(...).
using Assertions = PowerFramework.Shared.Diagnostics.Assertions;
#endif

// The parent namespace PowerFramework.Persistence.Tasks is reached implicitly by namespace nesting,
// which is how SqlCommandTask resolves without a using directive.
namespace PowerFramework.Persistence.Tasks.TaskProxies;

/// <summary>
/// The caller-side proxy for an asynchronous SQL COMMAND task - the port of
/// <c>n_cst_threading_task_sqlcommand</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlcommand.sru</c>, 58 lines].
/// </summary>
/// <remarks>
/// <para>
/// <b>THIS TYPE IS DISTINCT FROM <see cref="SqlCommandTask"/>, AND A COMPOSITION ROOT MUST REGISTER
/// BOTH (C-J).</b> They are the two halves of one legacy pair: this half runs on the CALLING thread
/// [<c>:L2</c>], the other on the CHILD thread
/// [<c>n_cst_thread_task_sqlcommand.sru:L2</c>]. Neither substitutes for the other, and they are
/// never merged - the file banner records the measured affinity census and the two-sided-reset
/// evidence that makes the separation a contract rather than a preference. The legacy's global
/// auto-instance <c>global n_cst_threading_task_sqlcommand n_cst_threading_task_sqlcommand</c>
/// [<c>:L11</c>] is deliberately not reproduced: there is no static, no singleton and no ambient
/// instance here, and every collaborator arrives through the constructor.
/// </para>
/// <para>
/// <b>It carries no state of its own beyond its identity, and adds only two functions.</b> The
/// oracle's entire own surface is the type marker [<c>:L9</c>, <c>:L17</c>], the auto-commit
/// re-export [<c>:L19-L21</c>], a private typed downcast [<c>:L26</c>, <c>:L31</c>], the two setters
/// [<c>:L34-L46</c>] and the worker class name [<c>:L56</c>]. Everything else a caller needs -
/// the composed non-blocking busy predicate, the two-sided reset, the last-error-wins error sink,
/// all four transaction-setting overloads, the parameter surface with its rank and empty-array
/// rejections, parameter reset, rollback, both commit overloads, the non-blocking commit poll and
/// the notify, cancellation and exit-code surface - is INHERITED from
/// <see cref="SqlTaskProxyBase"/> unchanged, because the oracle declares none of it here
/// [<c>:L25-L29</c> lists exactly three prototypes]. In particular <b>there is no reset override</b>:
/// the oracle declares none, so <see cref="SqlTaskProxyBase.Reset"/> is inherited as-is.
/// </para>
/// <para>
/// <b>Return codes are numbers, not exceptions.</b> Both setters answer a
/// <c>PowerFramework.Shared.Kernel.RetCode</c> value, and the worker's answer is returned
/// <i>verbatim</i> - never re-interpreted, never widened, never narrowed. The one condition that
/// throws is a structurally absent or wrongly typed worker, which the oracle has no return code for
/// at all; see <see cref="RequireCommandTask"/>.
/// </para>
/// <para>
/// <b>Thread safety.</b> This type is no more thread-safe than the oracle it ports, and deliberately
/// so: the legacy object is documented to run on the CALLING thread and its members are entered from
/// that thread alone. The busy predicate it guards with is a re-entrancy gate, not a lock - see
/// <see cref="SqlTaskProxyBase.IsBusy"/> - so callers must not drive one proxy instance from two
/// threads concurrently. Adding a lock here would change the observable refusal behaviour of every
/// mutator.
/// </para>
/// </remarks>
internal sealed class SqlCommandTaskProxy : SqlTaskProxyBase
{
    #region Identity - the legacy's two spellings of one value, declared once each

    /// <summary>
    /// The proxy's type marker, <c>"sqlcommand"</c> - the value the oracle spells TWICE, as the
    /// instance attribute <c>string #type = "sqlcommand"</c> [<c>:L9</c>] and as
    /// <c>constant string TASK_TYPE = "sqlcommand"</c> [<c>:L17</c>].
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One value, so one declaration.</b> The two legacy spellings are the same string - the
    /// attribute overrides the framework's <c>ProtectedWrite String #Type</c>
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L110</c>] while the constant
    /// republishes it for callers - so the port declares it here once and surfaces it through
    /// <see cref="TaskType"/>. The VALUE is preserved exactly; the constant's SCREAMING_SNAKE
    /// spelling cannot be, because <c>TaskProxies/</c> is outside the <c>.editorconfig</c> naming
    /// suppressions while warnings are errors - see the file banner.
    /// </para>
    /// <para>
    /// <b><see langword="internal"/> rather than private, as a narrow verification seam (C-H).</b>
    /// <see cref="TaskType"/> is <see langword="protected"/> because the base declares it so, which
    /// leaves the marker unreadable from outside a <see langword="sealed"/> type; and the marker is
    /// contract - it identifies the task kind - so it has to be assertable without inferring it from
    /// a diagnostic message. <c>InternalsVisibleTo</c> to the test project makes that possible
    /// without widening the type's real surface.
    /// </para>
    /// </remarks>
    internal const string TaskTypeMarker = "sqlcommand";

    /// <summary>
    /// The LEGACY class name of the worker this proxy drives, <c>"n_cst_thread_task_sqlcommand"</c> -
    /// the answer the oracle's <c>ongettaskclsname</c> event returns [<c>:L56</c>].
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Preserved verbatim, because it is a key and not a description.</b> The substrate resolves a
    /// worker BY this string - <c>ParentThread.of_InsertTask(index, ref _Task, clsName)</c>
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L188</c>] - and it also appears in
    /// characterization recordings, so a "tidied" .NET-style name would resolve nothing AND
    /// invalidate stored comparisons at the same time.
    /// </para>
    /// <para>
    /// <b>The oracle's event calls its ancestor first, and that is observably immaterial.</b>
    /// <c>:L56</c> reads <c>event ongettaskclsname;call super::ongettaskclsname;return
    /// "n_cst_thread_task_sqlcommand"</c> - the ancestor runs and its answer is then DISCARDED by the
    /// unconditional return. The query proxy is written the same way
    /// [<c>n_cst_threading_task_sqlquery.sru:L500</c>] while the update proxy omits the ancestor call
    /// [<c>n_cst_threading_task_sqlupdate.sru:L288</c>]; that inconsistency is documented where it
    /// occurs and is recorded here only so a reader meeting both forms knows neither is the anomaly.
    /// An overriding property has no ancestor call to make, so the difference disappears in the port
    /// - a difference in spelling, not in behaviour.
    /// </para>
    /// <para>
    /// <see langword="internal"/> for the same seam reason as <see cref="TaskTypeMarker"/>: the base
    /// declares <see cref="WorkerTaskClassName"/> <see langword="protected"/>, and the string is
    /// contract that must be assertable directly.
    /// </para>
    /// </remarks>
    internal const string LegacyWorkerClassName = "n_cst_thread_task_sqlcommand";

    #endregion

    #region Construction - C-J, everything arrives through here

    /// <summary>
    /// Initializes a command proxy against a threading substrate, a logger and the service clock.
    /// </summary>
    /// <param name="host">
    /// The threading substrate seam. Substitutable by design, which is what makes this type testable
    /// with no real thread (C-H).
    /// </param>
    /// <param name="logger">
    /// Diagnostics sink, forwarded to the base. <b>C-F: nothing this type writes through it carries a
    /// statement or a credential - it writes nothing at all.</b>
    /// </param>
    /// <param name="timeProvider">
    /// The service's single clock seam. <b>C-H: the only clock any proxy may read.</b> This type
    /// reads no clock, and the seam reaches it only through the base's
    /// <see cref="SqlTaskProxyBase.Clock"/>.
    /// </param>
    /// <param name="executionGroup">
    /// The execution group - the oracle's <c>#Group</c>, whose default is <c>GROUP_NORMAL</c>
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L111</c>].
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="host"/>, <paramref name="logger"/> or <paramref name="timeProvider"/> is
    /// <see langword="null"/>. The base validates and throws; fail-fast on a structural fault is the
    /// legacy posture and is preserved rather than softened.
    /// </exception>
    /// <remarks>
    /// The body is deliberately empty. The oracle's constructor is the framework-generated
    /// <c>on n_cst_threading_task_sqlcommand.create; call super::create</c> pair [<c>:L48-L50</c>],
    /// which adds nothing of its own - this type holds no field to initialize - so a constructor that
    /// did anything beyond chaining would be inventing behaviour the oracle does not have.
    /// </remarks>
    internal SqlCommandTaskProxy(
        ISqlTaskProxyHost host,
        ILogger<SqlCommandTaskProxy> logger,
        TimeProvider timeProvider,
        TaskExecutionGroup executionGroup = TaskExecutionGroup.Normal)
        : base(host, logger, timeProvider, executionGroup)
    {
    }

    #endregion

    #region The abstract identity the base requires

    /// <inheritdoc/>
    /// <remarks>
    /// <c>"sqlcommand"</c> [<c>:L9</c>, <c>:L17</c>]. Surfaces <see cref="TaskTypeMarker"/> rather
    /// than repeating the literal, so the value has exactly one declaration site in this file.
    /// </remarks>
    protected override string TaskType => TaskTypeMarker;

    /// <inheritdoc/>
    /// <remarks>
    /// <c>"n_cst_thread_task_sqlcommand"</c> [<c>:L56</c>]. Surfaces
    /// <see cref="LegacyWorkerClassName"/> for the same reason.
    /// </remarks>
    protected override string WorkerTaskClassName => LegacyWorkerClassName;

    #endregion

    #region Lifecycle - the reachable door onto an EXTERNALLY TRIGGERED legacy event

    /// <summary>
    /// Brings the proxy up, inserting and attaching the worker named by
    /// <see cref="LegacyWorkerClassName"/> - the reachable realization of the oracle's
    /// <c>oninit</c> event
    /// [<c>n_cst_threading_task_sqlbase.sru:L213-L221</c>, chaining
    /// <c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L179-L206</c>].
    /// </summary>
    /// <returns>
    /// <see cref="RetCode.OK"/> once the worker is attached and its commit signal borrowed; otherwise
    /// the substrate's own answer, returned verbatim. Note the base tests that answer with a
    /// <b>literal inequality against <see cref="RetCode.OK"/></b> rather than through the tri-state
    /// predicates, so <see cref="RetCode.PREVENT"/> ABORTS attachment here even though
    /// <c>Predicates.IsSucceeded</c> would call it a success - that is the oracle's own test at
    /// <c>n_cst_threading_task_sqlbase.sru:L215</c> and it is not routed through the predicates.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The substrate reported success but produced no worker, which is a structural fault in the
    /// substrate rather than a condition the oracle has a code for.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Why this member exists, since it is the one addition this file makes to the inherited
    /// surface (C-K).</b> In PowerBuilder <c>oninit</c> is an EVENT - <c>event type long oninit(...)</c>
    /// [<c>n_cst_threading_task.sru:L16</c>] - and events are triggered from OUTSIDE the object. The
    /// controller does exactly that on the concrete proxy it has just created:
    /// <c>rtCode = newTasking.Event OnInit(this,_Thread,index,_hEvtSync,taskClsName,group)</c>
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading.sru:L921</c>]. A C# <see langword="protected"/>
    /// method is NOT externally invocable the way a PowerBuilder event is, and
    /// <see cref="SqlTaskProxyBase.WorkerTask"/> has a private setter, so without a reachable door on
    /// the concrete type there would be no way for a composition root - or a test - to attach a worker
    /// at all. This is therefore the faithful expression of an externally triggered event, not a new
    /// capability: it adds no logic whatsoever and every behavioural decision stays on the base.
    /// </para>
    /// <para>
    /// <b>Why it is <see langword="internal"/> and not <see langword="public"/>.</b> The two ported
    /// <c>of_*</c> functions below are <see langword="public"/> because the oracle declares them
    /// <c>public function</c> [<c>:L27-L28</c>]. This one is a lifecycle door for the composition root
    /// and the test project inside this assembly, and the enclosing type is
    /// <see langword="internal"/> in any case, so the narrower spelling states the intent accurately.
    /// </para>
    /// <para>
    /// <b>No idempotence guard, deliberately (C-B).</b> The oracle's event has none - the controller
    /// triggers it once, from <c>of_InsertTask</c> - so calling this twice re-runs the substrate's
    /// insert exactly as re-triggering the legacy event would. Adding a "already initialized" refusal
    /// would be inventing a return code the oracle does not have.
    /// </para>
    /// </remarks>
    internal long Initialize() => OnInit();

    #endregion

    #region The private typed downcast - the port of _of_gettask() [:L26, :L31]

    /// <summary>
    /// Narrows the attached worker to <see cref="SqlCommandTask"/> - the port of
    /// <c>private function n_cst_thread_task_sqlcommand _of_gettask ();return _Task</c>
    /// [<c>:L26</c>, <c>:L31</c>].
    /// </summary>
    /// <returns>The attached worker, as the command task type.</returns>
    /// <exception cref="InvalidOperationException">
    /// No worker is attached - <see cref="Initialize"/> has not succeeded, or the proxy has been
    /// disposed - or the attached worker is of some other task type.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>The oracle's accessor tests nothing, and that is exactly why this one fails fast.</b> Its
    /// body is the single statement <c>return _Task</c>, relying on PowerBuilder's implicit downcast,
    /// and both call sites dereference the result immediately with no null test
    /// [<c>:L40</c>, <c>:L45</c>]. In PowerBuilder a null or incompatible reference there produces a
    /// RUNTIME ERROR rather than a return code, and the framework turns such an error into
    /// termination - the application's system-error handler unpacks the payload and executes
    /// <c>HALT CLOSE</c> [<c>ws_objects/pfw.pbl.src/pfw.sra:L111-L144</c>]. So an exception is the
    /// faithful .NET expression, and the fail-fast posture is preserved rather than softened into a
    /// warning-and-continue. No legacy <see cref="RetCode"/> value is converted into a throw anywhere
    /// in this file; this covers a condition the oracle has no code for.
    /// </para>
    /// <para>
    /// <b>Two distinct faults, deliberately separated.</b> An ABSENT worker is diagnosed by the base's
    /// <see cref="SqlTaskProxyBase.RequireWorkerTask"/>, which already names the task type and the
    /// worker class it expected. A worker of the WRONG type means the substrate inserted a class other
    /// than <see cref="LegacyWorkerClassName"/>, which is a different mistake and gets its own
    /// message - <see cref="SqlTaskProxyBase.GetWorkerTask{TTask}"/> answers <see langword="null"/> on
    /// a type mismatch precisely so that a derived accessor can decide, and this one decides that a
    /// mismatch is a fault.
    /// </para>
    /// <para>
    /// <b>The shape generalizes, which is a requirement rather than an accident.</b> All three derived
    /// proxies repeat this accessor - query at
    /// [<c>n_cst_threading_task_sqlquery.sru:L62</c>, <c>:L322</c>] and update at
    /// [<c>n_cst_threading_task_sqlupdate.sru:L49</c>, <c>:L79</c>] - so the mechanism used here is
    /// the base's generic narrowing over a single <c>protected SqlTaskBase</c> field, which works
    /// unchanged for both. The base is deliberately NOT generic over the worker type: that would fold
    /// the worker's identity into the proxy's own type identity and make the pair harder to register,
    /// and <b>the proxy and worker types must remain distinct and separately DI-registrable</b> (C-J).
    /// </para>
    /// </remarks>
    private SqlCommandTask RequireCommandTask()
    {
        // An absent worker is the structural fault the base already diagnoses, and it must be
        // diagnosed FIRST so that "nothing attached" never surfaces as "wrong type attached".
        SqlTaskBase task = RequireWorkerTask();

        // [:L31] `return _Task` - the typed narrowing the oracle gets from its implicit downcast.
        return GetWorkerTask<SqlCommandTask>()
            ?? throw new InvalidOperationException(
                $"The '{TaskTypeMarker}' task proxy is attached to a "
                + $"'{task.GetType().Name}' worker, but it drives only "
                + $"'{nameof(SqlCommandTask)}' - the substrate must insert "
                + $"'{LegacyWorkerClassName}'.");
    }

    #endregion

    #region The two ported mutators [:L34-L46] - and the asymmetry between them

    /// <summary>
    /// Installs the statement the worker will execute - the port of
    /// <c>of_setsql(readonly string sql)</c> [<c>:L27</c>, <c>:L34-L41</c>].
    /// </summary>
    /// <param name="sql">
    /// The statement, forwarded to the worker <b>completely unchanged</b> - not trimmed, not
    /// normalized, not validated and not rejected, <see langword="null"/> included. See the remarks
    /// for what the two builds do differently with an empty one.
    /// </param>
    /// <returns>
    /// <see cref="RetCode.E_BUSY"/> while the task is busy [<c>:L34</c>]; otherwise <b>whatever the
    /// worker answers, verbatim</b> [<c>:L40</c>] - which is <see cref="RetCode.E_INVALID_SQL"/> for
    /// the empty string and <see cref="RetCode.OK"/> for anything else, <see langword="null"/>
    /// included [<c>n_cst_thread_task_sqlcommand.sru:L45-L49</c>].
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// No worker is attached, or one of the wrong type is. See <see cref="RequireCommandTask"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>THE ORDER OF THE THREE STEPS IS CONTRACT.</b> The busy guard comes FIRST and returns before
    /// anything else happens [<c>:L34</c>], so a refused call neither asserts nor reaches the worker -
    /// which matters, because a busy proxy asked to install an empty statement answers
    /// <see cref="RetCode.E_BUSY"/> and not <see cref="RetCode.E_INVALID_SQL"/>, in Debug and Release
    /// alike. The assertion is second [<c>:L36-L38</c>] and the delegation last [<c>:L40</c>].
    /// </para>
    /// <para>
    /// <b>⚠ C-B - THE DEBUG GATING IS PRESERVED, SO A DEBUG BUILD AND A RELEASE BUILD GENUINELY
    /// DIFFER.</b> The oracle wraps the assertion in its own
    /// <c>#IF DEFINED DEBUG THEN ... #END IF</c> block, and the <c>#if DEBUG</c> below reproduces
    /// that faithfully. The consequence must not be tidied away in either direction:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// In a <b>Debug</b> build an empty or <see langword="null"/> statement raises
    /// <c>PowerFramework.Shared.Diagnostics.AssertionFailure</c> here and never reaches the worker.
    /// </description></item>
    /// <item><description>
    /// In a <b>Release</b> build the same statement <b>passes this proxy untouched</b> and is rejected
    /// by the worker with <see cref="RetCode.E_INVALID_SQL"/> [
    /// <c>n_cst_thread_task_sqlcommand.sru:L45</c>], which is a RETURN CODE and not a throw.
    /// </description></item>
    /// </list>
    /// <para>
    /// So the proxy's check is a development-time tripwire and the worker's is the real guard. <b>The
    /// assertion is neither promoted to a runtime guard nor removed</b>: promoting it would replace a
    /// documented return code with an exception on the Release path, and removing it would delete a
    /// diagnostic the oracle deliberately ships.
    /// </para>
    /// <para>
    /// <b>Why <c>Assertions.Assert</c> and never <c>Debug.Assert</c>.</b> The ported assertion is a
    /// port of <c>assert.srf</c>: it builds the legacy seven-field payload from the current call stack
    /// and raises <c>AssertionFailure</c>, which is the behaviour under test, whereas
    /// <c>Debug.Assert</c> would trip a listener instead. That family guards on
    /// <c>Predicates.IsSucceeded</c> and <c>Predicates.IsValidObject</c> in its numeric and object
    /// overloads and therefore inherits the tri-state hole - <see cref="RetCode.PREVENT"/> reads as a
    /// success, <see cref="RetCode.CANCELLED"/> and <see langword="null"/> as neither - which is
    /// immaterial for the boolean overload used here but is the reason the substitution must be the
    /// ported family rather than the framework's.
    /// </para>
    /// <para>
    /// <b>The condition, translated exactly.</b> The oracle asserts <c>Len(sql) &gt; 0</c>. In
    /// PowerScript <c>Len</c> of a NULL string is NULL, <c>NULL &gt; 0</c> is NULL, and a NULL
    /// condition takes the FALSE branch - so a null statement FAILS the assertion just as the empty
    /// string does. Mapping a null length to <c>0</c> below collapses that intermediate value, but the
    /// BRANCH TAKEN is identical for both inputs and the branch is all that is observable.
    /// </para>
    /// </remarks>
    public long SetSql(string? sql)
    {
        // [:L34] `if of_IsBusy() then return RetCode.E_BUSY` - the composed, non-blocking predicate on
        // the base. First, before the assertion and before the worker is touched.
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

#if DEBUG
        // [:L36-L38] `#IF DEFINED DEBUG THEN Assert(Len(sql) > 0,"Len(sql) <= 0") #END IF`. The
        // message text is the oracle's own, verbatim, because it is what a failing build reports.
        Assertions.Assert((sql?.Length ?? 0) > 0, "Len(sql) <= 0");
#endif

        // [:L40] `return _of_GetTask().of_SetSQL(sql)` - the argument forwarded unchanged and the
        // worker's code returned verbatim.
        return RequireCommandTask().SetSql(sql);
    }

    /// <summary>
    /// Installs the auto-commit mode the worker will run under - the port of
    /// <c>of_setautocommit(readonly long autocommit)</c> [<c>:L28</c>, <c>:L43-L46</c>].
    /// </summary>
    /// <param name="autoCommit">
    /// The mode. <b>Any value is accepted, including one outside the three declared enumerators</b> -
    /// see the remarks.
    /// </param>
    /// <returns>
    /// <see cref="RetCode.E_BUSY"/> while the task is busy [<c>:L43</c>]; otherwise whatever the
    /// worker answers, verbatim [<c>:L45</c>], which is <see cref="RetCode.OK"/> unconditionally
    /// [<c>n_cst_thread_task_sqlcommand.sru:L40-L43</c>].
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// No worker is attached, or one of the wrong type is. See <see cref="RequireCommandTask"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>⚠ C-B - THIS SETTER VALIDATES NOTHING, AND ITS ASYMMETRY WITH <see cref="SetSql"/> IS THE
    /// LEGACY BEHAVIOUR TO PRESERVE.</b> The oracle's body is two statements - busy guard, delegate -
    /// with no assertion, no range check, no membership test and no diagnostic of any kind
    /// [<c>:L43-L46</c>]. Set that beside the statement setter three lines earlier, which DOES carry a
    /// DEBUG-gated assertion [<c>:L34-L41</c>], and the inconsistency is plain: the argument that gets
    /// checked is a free-form string, while the argument that gets no check at all is a THREE-VALUED
    /// enumeration for which an out-of-range value is meaningless. <b>Both are reproduced exactly as
    /// written and neither is harmonised with the other.</b>
    /// </para>
    /// <para>
    /// <b>The two halves of the pair agree, and that agreement is the evidence.</b>
    /// <see cref="SqlCommandTask.SetAutoCommit"/> records that it "deliberately validates nothing"
    /// on the worker side [<c>n_cst_thread_task_sqlcommand.sru:L40-L43</c>], and this side adds only
    /// the busy guard the worker's own setter deliberately lacks. So an unmatched value is stored
    /// happily by both halves and then <b>silently behaves as <see cref="AutoCommitMode.AcOff"/></b>,
    /// because none of the arms in the worker's task body matches it: the success epilogue is an
    /// <c>if</c>/<c>elseif</c> with NO <c>else</c> [<c>:L94-L103</c>] so nothing is committed, and the
    /// failure epilogue's <c>else</c> arm rolls back [<c>:L107-L109</c>] exactly as it would for
    /// <c>AC_OFF</c>. Adding an argument check on either side would be correcting a legacy defect,
    /// which the refactor forbids.
    /// </para>
    /// <para>
    /// <b>Why the parameter is the generated enum and not the oracle's <c>long</c>.</b> The oracle
    /// signature is <c>readonly long autocommit</c> [<c>:L28</c>], and nothing is lost by naming
    /// <see cref="AutoCommitMode"/> instead: a proto3 enum is an OPEN <see cref="int"/>-backed enum in
    /// C#, so <c>SetAutoCommit((AutoCommitMode)7)</c> reproduces the unvalidated <c>long</c> path
    /// exactly, while an ordinary call site gains a type that cannot be confused with an unrelated
    /// number. It is also the same type the worker's setter takes, so the delegation needs no
    /// conversion. The values are the oracle's own - <c>AC_OFF = 0</c>, <c>AC_ON = 1</c>,
    /// <c>AC_NATIVE = 2</c> [<c>n_cst_thread_task_sqlcommand.sru:L16-L18</c>] - and only the
    /// declaration site moved; the file banner records why it had to.
    /// </para>
    /// <para>
    /// <b>Taken by value rather than by <see langword="in"/>.</b> This migration maps a PowerBuilder
    /// <c>readonly</c> parameter to <see langword="in"/>, and that mapping is honoured for the
    /// structures it exists for - the base takes its transaction descriptor as
    /// <see langword="in"/>. For a four-byte enum, by-value already guarantees the caller's copy is
    /// untouched, an <see langword="in"/> reference would be a pessimization, and the base treats
    /// every ported <c>readonly</c> SCALAR the same way. The same reasoning applies to
    /// <see cref="SetSql"/>'s string, which is immutable.
    /// </para>
    /// </remarks>
    public long SetAutoCommit(AutoCommitMode autoCommit)
    {
        // [:L43] `if of_IsBusy() then return RetCode.E_BUSY`. This is the ONLY guard in the method,
        // and the only one the oracle has - see the C-B note above before adding another.
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

        // [:L45] `return _of_GetTask().of_SetAutoCommit(autoCommit)` - delegated with no validation of
        // any kind and the worker's code returned verbatim.
        return RequireCommandTask().SetAutoCommit(autoCommit);
    }

    #endregion
}
