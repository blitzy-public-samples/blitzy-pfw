// =================================================================================================
//  Tasks/TaskProxies/SqlTaskProxyBase.cs - the abstract CALLER-SIDE proxy base.
// =================================================================================================
//  PROVENANCE. The port of ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru (222
//  lines) together with the slice of its framework parent
//  ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru (627 lines) that the SQL proxies actually
//  consume. Both are read-only behavioural oracle (C-C) and are never edited, moved or reformatted.
//  Every behavioural claim in this file carries a ws_objects/** locator that was opened and read.
//
//  WHY THIS FILE EXISTS AT ALL: THREAD AFFINITY IS A CONTRACT, NOT COMMENTARY.
//  The legacy encodes required execution context in $PBExportComments, and across the fifteen
//  objects of ws_objects/pfw.thread.ext.pbl.src the split MEASURES as:
//
//      [运行在子线程]     worker thread   6   n_cst_thread_task_sqlbase, _sqlbase_ds_mt,
//                                            _sqlbase_hook, _sqlcommand, _sqlquery, _sqlupdate
//      [运行在主线程]     main thread     1   n_cst_thread_task_sqlbase_ds - EXACTLY ONE
//      [运行在当前线程]   calling thread  4   the four proxies of this folder
//
//  This file's own marker is [运行在当前线程] at n_cst_threading_task_sqlbase.sru:L2, and its worker
//  counterpart's is [运行在子线程] at n_cst_thread_task_sqlbase.sru:L2.
//
//  THE PROXY/WORKER PAIR IS NOT COLLAPSED INTO ONE ASYNC METHOD, AND THE ORACLE PROVES WHY.
//  The .NET expression of the pair is async request/response with a CancellationToken while
//  PRESERVING BOTH TYPES AND THE BOUNDARY BETWEEN THEM. The proof that one flattened method cannot
//  express this is the two-sided reset at n_cst_threading_task_sqlbase.sru:L53-L68:
//
//      if of_IsBusy() then return RetCode.E_BUSY      guards on its OWN busy state          [:L57]
//      task = _Task                                                                        [:L59]
//      rtCode = task.of_Reset()                      DELEGATES the reset to the WORKER      [:L61]
//      if IsFailed(rtCode) then return rtCode        ABANDONS if the worker refused         [:L62]
//      _bHasParams = false                           only THEN clears its OWN state         [:L64]
//      _lastDBError = emptyData                                                            [:L65]
//      return RetCode.OK                                                                   [:L67]
//
//  Two distinct state sets, a delegation between them, and a failure mode in which the worker's
//  refusal leaves caller-side state UNTOUCHED. A single fused method has one state set and therefore
//  cannot have that failure mode at all, so fusing the pair would silently delete a behaviour.
//  The worker is consequently held as a field of a DISTINCT type - never merged, never a partial of
//  this one - and the `Proxy` suffix is the visible expression of that constraint.
//
//  THE TWO THREADING HAZARDS [docs/PB多线程绕坑提示.md, 5 lines, read-only]. Recorded here because
//  the three derived proxies inherit the patterns they dictate:
//    * HAZARD 1 [:L1-L4] A worker thread synchronously calling a MAIN-THREAD object's function or
//      event whose return type is `string` or `blob` can fault. The stated trigger is a Post message
//      that has not executed for an object which is itself destroyed, and the stated guidance is to
//      return via `ref` instead. That is exactly why payload-bearing callbacks in this folder pass
//      buffers BY REFERENCE rather than returning them - the query proxy's
//      `onchilddatareceived(string name, ref blob blbdata)` and
//      `ondatachunk(ref blob blbdata, ...)` [n_cst_threading_task_sqlquery.sru:L10, :L13], and the
//      update proxy's update-data setter. It is also why the one event THIS type declares carries
//      its payload INWARD as an `in` parameter and returns nothing.
//    * HAZARD 2 [:L5] After a global object is passed to a worker thread, the worker must set the
//      referenced main-thread object to NULL in its uninit event to release the reference. The .NET
//      analogue is to clear cross-boundary references EXPLICITLY on teardown and never rely on the
//      collector, which is what Dispose does here for the retained transaction, the latched error
//      payload, the worker reference and the borrowed commit signal.
//
//  CONSTRAINTS. There are NO user rules for this project: review_rules returns exactly one line
//  saying so, and nothing is invented, inferred or back-filled in their place. The binding set is
//  the enterprise baseline plus the named non-rule constraints, and the ruling FOR THIS FILE is:
//    C-A/C-I  Only the five project edges the .csproj declares are used. Nothing here references a
//             peer service or PowerFramework.Shared.Localization, and no package is added.
//             PowerFramework.Shared.Eventful IS one of those five and IS used: AAP 0.4.1 assigns the
//             whole of ws_objects/pfw.thread.pbl.src to Persistence in scope, one of its six objects
//             is n_cst_threading_eventful.sru, and that object derives from n_cst_eventful - so the
//             notification surface below is a delegate-shaped ADAPTER over the shared broker rather
//             than a second implementation of it. See ThreadingEventBroker.cs beside this file.
//    C-B      Six legacy behaviours that look like defects are reproduced and annotated at the point
//             of reproduction, never corrected: the bare last-error-wins assignment [:L44]; the
//             never-cleared has-transaction-data flag [:L116]; the OPPOSITE orderings of the two
//             resets [:L53-L68 versus :L161-L169]; the dormant commented-out null check [:L147]; the
//             eight-field copy whose AutoCommit the worker erases on arrival [:L83 versus
//             n_cst_thread_task_sqlbase.sru:L119]; and the three convenience overloads that carry no
//             busy guard of their own [:L138, :L195, and n_cst_threading_task.sru:L604/:L611].
//    C-C      The legacy tree is read-only and is the oracle. Locators, never edits.
//    C-D      No deferred-service coupling. n_scriptinvoker is NOT ported because PowerBuilder's
//             variadic escape hatch has no C# analogue, so there is nothing to port and no
//             ScriptBridge edge is created; pfw.utility.regexp is not reached either, because the
//             BCL suffices for the one in-scope use and no Documents edge is created.
//    C-E      No connection is opened, no engine is selected and no SQL Server or Oracle target is
//             named. Storage belongs to Data/ and Transactions/ alone.
//    C-F      No credential literal of any kind appears here, in any form, including commented out.
//             The credential moves through ONE named door and is never readable back out, and the
//             latched error payload leaves the process through ONE sanctioned projection.
//    C-G      No listener, no route, no endpoint. This type is reachable only through the authorized
//             C-05..C-08 gRPC methods under Grpc/.
//    C-H      The injected TimeProvider is the ONLY clock: no DateTime.UtcNow, no DateTime.Now, no
//             Environment.TickCount and no Stopwatch appears anywhere in this file. The worker and
//             the threading substrate are both SUBSTITUTABLE SEAMS, which is mandatory rather than
//             convenient - the worker base's own of_reset ALWAYS succeeds
//             [n_cst_thread_task_sqlbase.sru:L242-L248], so the reset-refusal arm at :L62 is
//             reachable only through a substituted worker and is otherwise untestable.
//    C-J      The legacy auto-instance `global n_cst_threading_task_sqlbase
//             n_cst_threading_task_sqlbase` [:L11] is NOT reproduced. This is a DI-registered type
//             with no static mutable state, and it is DISTINCT from Tasks/SqlTaskBase.cs: a
//             composition root must register BOTH, because they are the two halves of the pair.
//    C-K      Every technology-specific and boundary-specific decision above and below is recorded
//             with the evidence for it rather than asserted.
//    R9       One-based indexing. This file performs NO index arithmetic and NO positional access
//             whatsoever - it deliberately forwards none of the worker's one-based parameter
//             accessors, because the legacy proxy declares none of them either [:L26-L41]. The only
//             one-based value it carries is the task position, forwarded VERBATIM and unconverted by
//             GetIndex(). The reverse-iteration warning is recorded on RaiseNotify for the benefit
//             of a derived-file author reading this base first.
//    .editorconfig  TaskProxies/ sits OUTSIDE every naming-suppression glob while
//             TreatWarningsAsErrors is true, so declaring a SCREAMING_SNAKE identifier here is a
//             BUILD BREAK rather than a style nit. Legacy constant VALUES are preserved exactly;
//             the legacy SPELLINGS become PascalCase, per AAP 0.4.5.3.
// =================================================================================================

using Microsoft.Extensions.Logging;
using PowerFramework.Contracts.Common.V1;
using PowerFramework.Persistence.Errors;
using PowerFramework.Persistence.Transactions;

// PowerFramework.Contracts.Common.V1 also declares a RetCode, so the kernel's constant class is
// reached through an alias rather than a namespace import - the same resolution Tasks/SqlTaskBase.cs
// uses, kept identical so the two halves of the pair read the same way.
using Enums = PowerFramework.Shared.Kernel.Enums;
using Predicates = PowerFramework.Shared.Kernel.Predicates;
using RetCode = PowerFramework.Shared.Kernel.RetCode;

// The parent namespace PowerFramework.Persistence.Tasks is reached implicitly by namespace nesting,
// which is how SqlTaskBase and ISqlTaskProxy resolve without a using directive.
namespace PowerFramework.Persistence.Tasks.TaskProxies;

#region Execution group and channel names - legacy VALUES under PascalCase spellings

/// <summary>
/// The task execution group - the legacy <c>#Group</c> property
/// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L111</c>], whose three legal values are
/// re-exported there [<c>:L67-L69</c>] from the worker substrate
/// [<c>ws_objects/pfw.thread.pbl.src/n_cst_thread_task.sru:L70-L72</c>].
/// </summary>
/// <remarks>
/// <para>
/// The VALUES are the oracle's own and are load-bearing, because the group orders a thread's task
/// list: prepare runs ahead of normal work and post runs behind it, which is why the numbering is
/// signed rather than a plain 0/1/2 sequence.
/// </para>
/// <para>
/// The SPELLINGS are PascalCase and deliberately are not the legacy's. <c>TaskProxies/</c> is outside
/// every <c>.editorconfig</c> naming-suppression glob while warnings are errors, so the legacy
/// SCREAMING_SNAKE identifiers cannot be declared here at all. Preserving the value while respelling
/// the identifier is exactly the split AAP 0.4.5.3 prescribes.
/// </para>
/// </remarks>
internal enum TaskExecutionGroup : long
{
    /// <summary>Runs ahead of ordinary work - <c>GROUP_PREPARE = -1</c> [<c>n_cst_thread_task.sru:L71</c>].</summary>
    Prepare = -1L,

    /// <summary>
    /// Ordinary work - <c>GROUP_NORMAL = 0</c> [<c>n_cst_thread_task.sru:L70</c>], and the legacy
    /// default that <c>#Group</c> is initialised to [<c>n_cst_threading_task.sru:L111</c>].
    /// </summary>
    Normal = 0L,

    /// <summary>Runs behind ordinary work - <c>GROUP_POST = 1</c> [<c>n_cst_thread_task.sru:L72</c>].</summary>
    Post = 1L,
}

/// <summary>
/// The five notification channel names the framework parent publishes for subscription
/// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L71-L105</c>].
/// </summary>
/// <remarks>
/// <para>
/// Every string is the oracle's verbatim value. They are load-bearing because they are the keys a
/// subscription is addressed by, so a "tidied" spelling would silently address nothing - the same
/// reasoning that keeps <c>DataWindowProperty</c>'s strings verbatim in the worker half of the pair.
/// The identifiers are PascalCase for the <c>.editorconfig</c> reason recorded on
/// <see cref="TaskExecutionGroup"/>; only the identifiers change, never the values.
/// </para>
/// </remarks>
internal static class TaskEventName
{
    /// <summary>
    /// <c>EVT_COMMONNOTIFY = "common-notify"</c> [<c>n_cst_threading_task.sru:L71</c>]. Receives
    /// EVERY notification ahead of the per-reason channels, with the reason as its first argument.
    /// </summary>
    internal const string CommonNotify = "common-notify";

    /// <summary>
    /// <c>EVT_START = "start"</c> [<c>n_cst_threading_task.sru:L80</c>]. Its documented return
    /// contract is <c>long (0:continue,1:prevent)</c> [<c>:L83</c>].
    /// </summary>
    internal const string Start = "start";

    /// <summary>
    /// <c>EVT_STOP = "stop"</c> [<c>n_cst_threading_task.sru:L85</c>]. Carries the exit code and the
    /// last error text [<c>:L87-L88</c>], and its documented return contract is <c>none</c>
    /// [<c>:L90</c>].
    /// </summary>
    internal const string Stop = "stop";

    /// <summary>
    /// <c>EVT_NOTIFY = "notify"</c> [<c>n_cst_threading_task.sru:L92</c>]. Carries the three-part
    /// payload [<c>:L94-L96</c>] and is the channel the derived query proxy's progress notifications
    /// reach.
    /// </summary>
    internal const string Notify = "notify";

    /// <summary>
    /// <c>EVT_ERROR = "error"</c> [<c>n_cst_threading_task.sru:L100</c>]. Carries the error code and
    /// the diagnostic text [<c>:L102-L103</c>].
    /// </summary>
    internal const string Error = "error";
}

// The TRI-VALUED veto codes are NOT restated here. They are
// PowerFramework.Shared.Eventful.VetoResult - Continue = 0, PreventOnce = 1, PreventDeep = 2 - the
// port of n_cst_eventful.sru:L111-L112, and that enum is the single authority for the alphabet across
// the whole repository. A local restatement of the same three numerals would be a second authority
// that can drift, which is exactly the duplication this folder's notification surface was rewritten
// to remove. Never flatten the three to a boolean: a prevent-once is consumed by the dispatch that
// raised it while a prevent-deep survives the whole nesting depth [n_cst_eventful.sru:L954-L958], so
// collapsing them would silently convert a deep prevention into a shallow one.

#endregion

#region The notification surface - the shared broker, specialized for threading and adapted to delegates

/// <summary>
/// A subscriber to one of the four per-reason channels - the shape of an <c>of_On</c> subscription to
/// <see cref="TaskEventName.Start"/>, <see cref="TaskEventName.Stop"/>,
/// <see cref="TaskEventName.Notify"/> or <see cref="TaskEventName.Error"/>
/// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L80-L105</c>,
/// <c>:L143, :L373</c>].
/// </summary>
/// <param name="source">
/// The task raising the notification - the <c>n_cst_threading_task source</c> first argument the
/// framework injects into every subscriber [<c>n_cst_threading_task.sru:L81, :L86, :L93, :L101</c>;
/// injected at <c>ws_objects/pfw.thread.pbl.src/n_cst_threading_eventful.sru:L55</c>].
/// </param>
/// <param name="wparam">
/// The first numeric argument. Which legacy argument this is depends on the channel, exactly as it
/// does in the oracle: the exit code for stop [<c>n_cst_threading_task.sru:L87</c>], the error code
/// for error [<c>:L102</c>], the caller's <c>wparam</c> for notify [<c>:L94</c>], and unused for start.
/// </param>
/// <param name="lparam">
/// The second numeric argument. Populated for notify only
/// [<c>n_cst_threading_task.sru:L95</c>]; the oracle passes no second
/// numeric argument on the other three channels. The query proxy packs a current/total pair into it
/// as two 16-bit words [<c>n_cst_threading_task_sqlquery.sru:L254, :L265, :L270</c>].
/// </param>
/// <param name="text">
/// The string argument - the last error text for stop [<c>:L88</c>], the diagnostic for error
/// [<c>:L103</c>], the caller's <c>sparam</c> for notify [<c>:L96</c>], and unused for start.
/// </param>
/// <returns>
/// The subscriber's numeric answer, or <see langword="null"/> when it declines to answer. The
/// documented contract on the start channel is 0 to continue and 1 to prevent [<c>:L83</c>]; the stop
/// channel documents no return at all [<c>:L90</c>] and any value it produces is simply carried.
/// </returns>
/// <remarks>
/// <b>One delegate serves four channels because the oracle's own dispatch does.</b>
/// <c>_of_sendnotify</c> triggers all four through the same variadic broker call with different
/// argument subsets [<c>n_cst_threading_task.sru:L337-L356</c>], so a single three-part payload shape
/// with per-channel documentation is the faithful expression rather than four near-identical
/// delegates. The nullable return preserves PowerBuilder's null-versus-zero distinction, which the
/// dispatch gate at <c>:L335</c> and the <c>SetNull(nVal)</c>/<c>IsNull(nVal)</c> pair at
/// <c>:L336, :L357</c> genuinely depend on - collapsing null to 0 would turn "nobody answered" into
/// "somebody answered continue".
/// </remarks>
internal delegate long? TaskNotificationHandler(
    SqlTaskProxyBase source,
    long wparam,
    long lparam,
    string text);

/// <summary>
/// A subscriber to the catch-all channel - the shape of an <c>of_On</c> subscription to
/// <see cref="TaskEventName.CommonNotify"/>
/// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L71-L78</c>].
/// </summary>
/// <param name="source">The task raising the notification [<c>:L72</c>].</param>
/// <param name="reason">
/// Which kind of notification this is - one of <c>Enums.TNR_START</c>, <c>TNR_STOP</c>,
/// <c>TNR_ERROR</c> or <c>TNR_NOTIFY</c> [<c>:L73</c>;
/// <c>ws_objects/pfw.shared.pbl.src/enums.sru:L581-L584</c>]. This extra leading argument is the ONLY
/// difference from <see cref="TaskNotificationHandler"/>, and it is why the two cannot be one type.
/// </param>
/// <param name="wparam">The first numeric argument [<c>:L74</c>].</param>
/// <param name="lparam">The second numeric argument [<c>:L75</c>].</param>
/// <param name="text">The string argument [<c>:L76</c>].</param>
/// <returns>
/// The subscriber's numeric answer, or <see langword="null"/> when it declines to answer. <b>A
/// non-zero, non-null answer SUPPRESSES the per-reason channels entirely</b>, which is the gate at
/// <c>:L335</c> and is this channel's real power.
/// </returns>
internal delegate long? TaskCommonNotificationHandler(
    SqlTaskProxyBase source,
    long reason,
    long wparam,
    long lparam,
    string text);

/// <summary>
/// The caller-side notification surface - the delegate-shaped adapter over the framework broker
/// <c>_Eventful</c> [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L119</c>, of type
/// <c>ws_objects/pfw.thread.pbl.src/n_cst_threading_eventful.sru</c>].
/// </summary>
/// <remarks>
/// <para>
/// <b>THE BROKER IS THE DISPATCHER. This type only adapts its shape (C-A, C-I, C-K).</b> The framework
/// broker is ported in <c>shared/PowerFramework.Shared.Eventful</c>, its threading specialization is
/// ported in <see cref="ThreadingEventBroker"/> beside this file, and Persistence references the shared
/// project. AAP 0.4.1 requires exactly that: the whole of <c>ws_objects/pfw.thread.pbl.src</c> is
/// assigned to Persistence in scope, one of its six objects IS
/// <c>n_cst_threading_eventful.sru</c>, and that object's inheritance from <c>n_cst_eventful</c> is one
/// of the two structural facts the AAP cites as proof that the base belongs in a shared in-scope
/// library. So the ordered subscription table, the priority and prepend insert, the capture filter, the
/// tri-valued veto and its unwind, the dispatch-depth accounting, the handled latch, the
/// default-return-value substitution, the argument injection and the exception decoration are all the
/// broker's, and NONE of them is restated here.
/// </para>
/// <para>
/// <b>What is left, and why it is adaptation rather than duplication.</b> Two things. First, the broker
/// subscribes an OBJECT and a handler-member NAME, resolving the member reflectively
/// [<c>n_cst_eventful.sru:L398-L403</c>], while this folder's published surface subscribes a .NET
/// delegate; so each subscription is wrapped in a tiny object whose one member forwards to the
/// delegate. Second, <see cref="Off(string?, TaskNotificationHandler?)"/> has to find the wrapper that
/// carries a given delegate, which needs a delegate-to-wrapper index. That index is an identity map,
/// not a dispatch table: nothing reads it to decide who runs, in what order, or whether to stop.
/// </para>
/// <para>
/// <b>Not thread-safe by design, and that is the oracle's own contract.</b> This type is
/// <c>[运行在当前线程]</c> - it lives entirely on the calling thread
/// [<c>n_cst_threading_task_sqlbase.sru:L2</c>] - so subscription and dispatch happen on one thread
/// and a lock here would model a synchronization the legacy does not have. The cross-thread hand-off
/// is the worker boundary, and it is guarded there rather than here.
/// </para>
/// </remarks>
internal sealed class TaskNotificationDispatcher
{
    /// <summary>
    /// The member name every subscription wrapper publishes for the broker to resolve.
    /// </summary>
    /// <remarks>
    /// The broker's second <c>of_On</c> argument pair is an object and the NAME of a member on it
    /// [<c>n_cst_eventful.sru:L296</c>], resolved case-insensitively and stored lower-cased
    /// [<c>:L336, :L398-L403</c>]. One constant rather than a literal at each call site, because the
    /// same name has to appear on the subscribe and the unsubscribe paths and a divergence between them
    /// would leave a subscription that cannot be removed.
    /// </remarks>
    private const string WrapperHandlerName = "Invoke";

    /// <summary>
    /// The <c>"."</c> that separates a subscription's name from its lifetime namespace
    /// [<c>n_cst_eventful.sru:L96</c>, <c>SYMBOL_NS</c>].
    /// </summary>
    private const char SubscriptionNamespaceSeparator = '.';

    /// <summary>
    /// The filter the threading layer removes with - every name, in any namespace except
    /// <c>persistent</c> [<c>n_cst_threading_task.sru:L385, :L391</c>].
    /// </summary>
    /// <remarks>
    /// <b>One symbol is the entire difference between this and "remove everything".</b> The broker's
    /// own parameterless <c>of_off()</c> passes the EMPTY filter and removes every subscription
    /// [<c>n_cst_eventful.sru:L228-L230</c>]; the threading layer passes this one, whose <c>^</c>
    /// negates the namespace and so spares the persistent subscriptions. Reproduced verbatim rather
    /// than simplified to the empty filter, even though this surface creates no persistent
    /// subscription, because the simplification would be indistinguishable from the bug.
    /// </remarks>
    private const string NonPersistentNamespaceFilter = ".^persistent";

    /// <summary>The broker that actually dispatches - the port of <c>_Eventful</c>.</summary>
    private readonly ThreadingEventBroker _broker = new();

    /// <summary>
    /// The delegate-to-wrapper index, keyed by channel name and ordered by subscription.
    /// </summary>
    /// <remarks>
    /// An identity map, NOT a dispatch table. Its only readers are the removal members, which need to
    /// find the wrapper carrying a caller's delegate, and the channel-key tests that reproduce the
    /// legacy removal codes. Dispatch never consults it.
    /// </remarks>
    private readonly Dictionary<string, List<ChannelSubscription>> _wrappers =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes the surface against the object the broker injects as every handler's leading
    /// argument and the signals its hooks read.
    /// </summary>
    /// <param name="source">
    /// The task raising notifications - the oracle's <c>_source</c>, which
    /// <c>n_cst_threading_task.sru:L197</c> initialises with <c>this</c>.
    /// </param>
    /// <param name="signals">The three synchronization signals the broker's hooks read and raise.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="source"/> or <paramref name="signals"/> is <see langword="null"/>. Fail-fast on a
    /// structural fault is the legacy posture and is preserved: a broker with no source would inject a
    /// null leading argument into every subscriber.
    /// </exception>
    internal TaskNotificationDispatcher(SqlTaskProxyBase source, IThreadingBrokerSignals signals)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(signals);

        // [n_cst_threading_task.sru:L195-L197] Create the broker, then raise its init event with the
        // source and the three handles. The refusal code cannot fire here, because both arguments were
        // just null-checked; it is discarded rather than tested for exactly that reason.
        _ = _broker.Initialize(source, signals);
    }

    /// <summary>
    /// Whether the broker suppresses its own guards - the broker's <c>#Silent</c> property
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_eventful.sru:L24</c>].
    /// </summary>
    /// <value>
    /// <see langword="true"/> while a caller is managing the guards itself. The notify path saves,
    /// sets and restores it around every dispatch
    /// [<c>n_cst_threading_task.sru:L328-L329, :L363</c>].
    /// </value>
    /// <remarks>
    /// A pass-through to <see cref="ThreadingEventBroker.Silent"/>, which documents what silence
    /// actually suppresses and the measured finding that the non-silent branch of the two triggering
    /// hooks is unreachable through the proxy's own publication path.
    /// </remarks>
    internal bool Silent
    {
        get => _broker.Silent;
        set => _broker.Silent = value;
    }

    /// <summary>
    /// The dispatch nesting depth this surface currently has open.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the PUMP-TURN BOUNDARY, and that is why it is counted here rather than read off the
    /// broker.</b> The broker's own <c>_nDeep</c> is <c>private:</c> in the oracle
    /// [<c>n_cst_eventful.sru:L77</c> sits under the <c>private:</c> label at <c>:L70</c>], so the
    /// port keeps it private too and a derived broker cannot read it. What this surface genuinely needs
    /// is the moment its OUTERMOST dispatch closes: the broker defers a removal made during a dispatch
    /// to a queued compaction rather than rewriting a table that active levels hold cursors into
    /// [<c>:L1054-L1059, :L1081-L1087</c>], and in PowerBuilder the Win32 message pump turns that
    /// queue. A headless service has no pump, so AAP 0.4.5.4 makes the turn explicit - and the only
    /// safe place to turn it is where no dispatch of this surface is in flight.
    /// </para>
    /// <para>
    /// It is therefore this surface's count of its own open dispatches, which equals the broker's
    /// depth for every dispatch reached through here, and it is <b>not</b> an authority on the broker's
    /// state. <see cref="Prevent(bool)"/> does not consult it: the broker refuses a veto raised outside
    /// a dispatch from its own depth [<c>:L1293</c>], which is the authority.
    /// </para>
    /// </remarks>
    internal int Depth { get; private set; }

    /// <summary>
    /// Faults thrown by subscribers and absorbed rather than propagated, in order.
    /// </summary>
    /// <remarks>
    /// A pass-through to <see cref="ThreadingEventBroker.AbsorbedFaults"/>. Read
    /// <see cref="ThreadingEventBroker.OnException"/> before assuming what lands here: the threading
    /// broker absorbs a fault only when the task is free - the state the notification fan-out
    /// establishes around itself - and otherwise lets the base rethrow it. A fault that propagates is
    /// therefore NOT recorded here, because it is its own diagnostic.
    /// </remarks>
    internal IReadOnlyList<Exception> CapturedExceptions => _broker.AbsorbedFaults;

    /// <summary>
    /// Subscribes to one of the four per-reason channels - <c>of_On(name, object, evtName)</c>
    /// [<c>n_cst_threading_task.sru:L143, :L373</c>].
    /// </summary>
    /// <param name="name">A <see cref="TaskEventName"/> channel name.</param>
    /// <param name="handler">The subscriber.</param>
    /// <returns>
    /// <see cref="RetCode.OK"/> on success, <see cref="RetCode.E_INVALID_ARGUMENT"/> when the channel
    /// name is absent or the handler is <see langword="null"/>, or whatever the broker answered when it
    /// refused the subscription.
    /// </returns>
    /// <remarks>
    /// The argument refusal is tested here rather than left to the broker because the broker's own
    /// refusal is expressed against a topic string and a handler NAME, and this surface's second
    /// argument is a delegate that has no name to be empty. The broker's remaining codes -
    /// <c>E_INVALID_OBJECT</c> for a null target and <c>E_EVENT_NOT_FOUND</c> for an unresolvable
    /// member - are unreachable through this path by construction, and are surfaced unchanged rather
    /// than mapped in case they ever become reachable.
    /// </remarks>
    internal long On(string? name, TaskNotificationHandler? handler)
    {
        if (string.IsNullOrEmpty(name) || handler is null)
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        return Register(name, new PerReasonSubscription(handler));
    }

    /// <summary>
    /// Subscribes to the catch-all channel - <c>of_On(EVT_COMMONNOTIFY, ...)</c>
    /// [<c>n_cst_threading_task.sru:L71, :L143, :L373</c>].
    /// </summary>
    /// <param name="handler">The subscriber.</param>
    /// <returns>
    /// <see cref="RetCode.OK"/> on success, <see cref="RetCode.E_INVALID_ARGUMENT"/> when the handler
    /// is <see langword="null"/>, or whatever the broker answered when it refused the subscription.
    /// </returns>
    internal long OnCommon(TaskCommonNotificationHandler? handler)
    {
        if (handler is null)
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        return Register(TaskEventName.CommonNotify, new CommonSubscription(handler));
    }

    /// <summary>
    /// Removes one subscriber from one channel - the <c>of_Off(name, object, evtName)</c> arity
    /// [<c>n_cst_threading_task.sru:L144, :L376</c>].
    /// </summary>
    /// <param name="name">The channel name.</param>
    /// <param name="handler">The subscriber to remove.</param>
    /// <returns>
    /// <see cref="RetCode.E_INVALID_ARGUMENT"/> when either argument is absent; otherwise
    /// <see cref="RetCode.OK"/>, <b>whether or not anything matched</b>. The legacy's seven
    /// <c>of_Off</c> arities exist to select subscribers by name, by object, by event or by any
    /// combination; only the combinations this folder reaches are published, and no speculative arity
    /// is added.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>The channel KEY survives its last subscriber, and that is behaviour rather than untidiness.</b>
    /// Removing the handler leaves the key present with an empty list, so
    /// <see cref="Off(string?)"/> still answers <see cref="RetCode.OK"/> for that channel afterwards
    /// while <see cref="IsSubscribed"/> answers <see langword="false"/>. Those are genuinely different
    /// questions: one asks whether the channel was ever opened, the other whether anything is listening
    /// on it now.
    /// </para>
    /// <para>
    /// <b>"MATCHED NOTHING" IS NOT A FAILURE HERE, BECAUSE IT IS NOT ONE IN THE ORACLE (C-B).</b> The
    /// legacy removal engine answers <see cref="RetCode.OK"/> unconditionally - its only failure is an
    /// invalid filter [<c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L1021-L1023,
    /// :L1089</c>] - so it does not distinguish "removed something" from "matched nothing", and neither
    /// does this. DISTINGUISHING THEM FROM THIS SURFACE'S OWN INDEX IS THE TEMPTING REFINEMENT, on the
    /// reasoning that a more informative answer costs nothing. It costs the one thing this refactor may
    /// not spend: the answer is observable through the published contract, so a caller written against
    /// the oracle's algebra would read a removal that matched nothing as a failure that never happened.
    /// The index is maintained for a different purpose - <see cref="IsSubscribed"/> is how a caller asks
    /// whether anything is listening - and it deliberately does not reach the return code.
    /// </para>
    /// </remarks>
    internal long Off(string? name, TaskNotificationHandler? handler)
    {
        if (string.IsNullOrEmpty(name) || handler is null)
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        // A CHANNEL THIS SURFACE NEVER OPENED IS THE "MATCHED NOTHING" CASE, AND IT ANSWERS OK.
        if (!_wrappers.TryGetValue(name, out List<ChannelSubscription>? channel))
        {
            return RetCode.OK;
        }

        for (int index = 0; index < channel.Count; index++)
        {
            ChannelSubscription candidate = channel[index];
            if (!candidate.Wraps(handler))
            {
                continue;
            }

            channel.RemoveAt(index);

            // [n_cst_eventful.sru:L1045-L1050] the three-part filter - name, target identity and
            // handler member name - selects exactly this wrapper and no other, because every wrapper is
            // a distinct object.
            _ = _broker.Unsubscribe(name, candidate, WrapperHandlerName);

            return RetCode.OK;
        }

        // The channel exists but carries no wrapper over this delegate. Still OK - see the remarks.
        return RetCode.OK;
    }

    /// <summary>
    /// Removes every subscriber from one channel - the <c>of_Off(name)</c> arity
    /// [<c>n_cst_threading_task.sru:L148, :L387-L392</c>].
    /// </summary>
    /// <param name="name">The channel name.</param>
    /// <returns>
    /// <see cref="RetCode.E_INVALID_ARGUMENT"/> when the name is absent; otherwise
    /// <see cref="RetCode.OK"/>, whether or not the channel had ever been opened.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>The lifetime namespace is the oracle's own, and it is honoured rather than dropped.</b> The
    /// threading layer's one-argument <c>of_Off</c> appends <c>".^persistent"</c> to a name that
    /// carries no <c>"."</c> [<c>n_cst_threading_task.sru:L388-L392</c>], which under the broker's
    /// filter grammar means "this name, in any namespace EXCEPT persistent". The pass-through branch
    /// for a name that already carries a <c>"."</c> is unreachable from here, because none of the five
    /// channel names contains one, and it is reproduced anyway so the two branches stay legible
    /// together. Every subscription this surface makes is namespace-less and therefore not persistent,
    /// so the filter removes all of them - which is why the local index can be cleared unconditionally
    /// alongside it.
    /// </para>
    /// <para>
    /// The unconditional <see cref="RetCode.OK"/> is the oracle's own answer, for the reason recorded on
    /// <see cref="Off(string?, TaskNotificationHandler?)"/>: the legacy engine answers OK whether or not
    /// the filter matched anything [<c>n_cst_eventful.sru:L1089</c>].
    /// </para>
    /// </remarks>
    internal long Off(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        // [:L388-L392] if Pos(name,".") > 0 then of_Off(name) else of_Off(name + ".^persistent")
        _ = _broker.Unsubscribe(
            name.Contains(SubscriptionNamespaceSeparator, StringComparison.Ordinal)
                ? name
                : name + NonPersistentNamespaceFilter);

        // The index is cleared for its own sake; whether a key was present does not change the answer.
        _ = _wrappers.Remove(name);

        return RetCode.OK;
    }

    /// <summary>
    /// Removes every subscriber from every channel - the no-argument <c>of_Off()</c> arity, which in
    /// the oracle clears everything outside the <c>.^persistent</c> namespace
    /// [<c>n_cst_threading_task.sru:L147, :L385</c>].
    /// </summary>
    /// <returns><see cref="RetCode.OK"/> always, matching the oracle's unconditional result.</returns>
    internal long OffAll()
    {
        // [:L385] of_Off(".^persistent") - every name, in any namespace except persistent.
        _ = _broker.Unsubscribe(NonPersistentNamespaceFilter);

        _wrappers.Clear();
        return RetCode.OK;
    }

    /// <summary>
    /// Whether a channel currently has at least one subscriber - <c>of_IsSubscribed(name)</c>
    /// [<c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L747</c>], tested by the notify
    /// path before it triggers the catch-all channel [<c>n_cst_threading_task.sru:L331</c>].
    /// </summary>
    /// <param name="name">The channel name.</param>
    /// <returns><see langword="true"/> when at least one subscriber is registered.</returns>
    /// <remarks>
    /// Answered by the broker rather than from the local index, so there is one authority. Read
    /// <c>EventBroker.IsSubscribed</c>'s own remarks before relying on it as more than a fast
    /// conservative pre-test: it reproduces a legacy quirk whereby the scan skips the first and last
    /// table slots, which the lexical-bound short-circuits above it cover for every table this surface
    /// can produce.
    /// </remarks>
    internal bool IsSubscribed(string? name) =>
        !string.IsNullOrEmpty(name) && _broker.IsSubscribed(name);

    /// <summary>
    /// Records a veto - <c>of_Prevent(deep)</c>
    /// [<c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L1276-L1300</c>].
    /// </summary>
    /// <param name="deep">
    /// <see langword="true"/> for <c>VetoResult.PreventDeep</c>, <see langword="false"/> for
    /// <c>VetoResult.PreventOnce</c> [<c>:L1294-L1298</c>].
    /// </param>
    /// <returns>
    /// <see cref="RetCode.OK"/>, or <see cref="RetCode.FAILED"/> when no dispatch is in progress -
    /// the broker's own <c>if _nDeep &lt;= 0 then return RetCode.FAILED</c> guard [<c>:L1293</c>]. A
    /// veto outside a dispatch has nothing to prevent, so it is refused rather than remembered.
    /// </returns>
    internal long Prevent(bool deep) => _broker.Prevent(deep);

    /// <summary>
    /// Records a shallow veto - the no-argument <c>of_Prevent()</c>, which the oracle defines as
    /// <c>return of_Prevent(false)</c> [<c>n_cst_eventful.sru:L1302</c>].
    /// </summary>
    /// <returns>Whatever <see cref="Prevent(bool)"/> returns for a shallow veto.</returns>
    internal long Prevent() => _broker.Prevent();

    /// <summary>
    /// Dispatches to the catch-all channel [<c>n_cst_threading_task.sru:L332</c>].
    /// </summary>
    /// <param name="reason">The <c>Enums.TNR_*</c> reason.</param>
    /// <param name="wparam">The first numeric argument.</param>
    /// <param name="lparam">The second numeric argument.</param>
    /// <param name="text">The string argument.</param>
    /// <returns>
    /// The last invoked subscriber's answer, or the broker's established default of <c>0</c> when none
    /// produced one. See <see cref="Trigger"/> for why that is never <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// The source object is NOT a parameter: the broker injects it into every subscriber's leading slot
    /// from the value this surface was constructed with
    /// [<c>n_cst_threading_eventful.sru:L55-L56</c>]. Passing it again per dispatch would create a
    /// second source of truth that could disagree with the injected one.
    /// </remarks>
    internal long? TriggerCommon(long reason, long wparam, long lparam, string text) =>
        Dispatch(TaskEventName.CommonNotify, [reason, wparam, lparam, text]);

    /// <summary>
    /// Dispatches to one of the four per-reason channels
    /// [<c>n_cst_threading_task.sru:L337-L356</c>].
    /// </summary>
    /// <param name="name">The channel name.</param>
    /// <param name="wparam">The first numeric argument.</param>
    /// <param name="lparam">The second numeric argument.</param>
    /// <param name="text">The string argument.</param>
    /// <returns>
    /// The last invoked subscriber's answer, or the broker's established default of <c>0</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>ZERO, NOT NULL, AND THAT IS THE ORACLE'S ANSWER.</b> The threading broker's constructor calls
    /// <c>of_SetDefaultReturnValue(0)</c> [<c>n_cst_threading_eventful.sru:L76</c>], and the base
    /// substitutes the established default for any non-posted dispatch that produced no value
    /// [<c>n_cst_eventful.sru:L966-L970</c>]. So an unsubscribed channel, a channel whose subscribers
    /// all declined to answer, and a dispatch stopped by the cancellation pre-veto all answer <c>0</c>.
    /// The return type stays nullable because <see cref="SqlTaskProxyBase.SendNotify"/>'s own
    /// <c>nVal</c> local genuinely holds null when no reason arm runs at all [<c>:L336</c>] - which is
    /// a different fact from a dispatch answering nothing.
    /// </para>
    /// <para>
    /// A null or empty channel name dispatches nothing and answers the same default, which is the
    /// broker's <c>if name = "" then return aDefRetVal</c> [<c>n_cst_eventful.sru:L793</c>]. PowerScript
    /// models the argument as an always-present possibly-empty string, so null and empty arrive at one
    /// outcome.
    /// </para>
    /// <para>
    /// <b>ON A THREADING CHANNEL, ANSWERING IS A CLAIM. Read this before writing a subscriber.</b>
    /// Because the established default is <c>0</c> rather than null, the broker's handled test lands on
    /// its third arm - a value EQUAL to the default is not handled, a value different from it is
    /// [<c>:L912-L917</c>] - and its capture filter then stops offering the event to ordinary
    /// unhandled-only subscriptions [<c>:L831-L833</c>]. So the FIRST subscriber to answer anything other
    /// than <c>0</c> claims the event and every remaining ordinary subscriber on that channel is
    /// skipped, while a subscriber that answers <c>0</c> or declines leaves it unclaimed and the rest
    /// run. That is one line of the oracle's constructor [<c>n_cst_threading_eventful.sru:L76</c>]
    /// deciding the whole channel's dispatch contract, and it is why the notification path's own
    /// suppression gate tests the VALUE rather than the subscription
    /// [<c>n_cst_threading_task.sru:L335</c>].
    /// </para>
    /// </remarks>
    internal long? Trigger(string? name, long wparam, long lparam, string text) =>
        Dispatch(name, [wparam, lparam, text]);

    /// <summary>
    /// Subscribes one wrapper and indexes it - the shared half of <see cref="On"/> and
    /// <see cref="OnCommon"/>.
    /// </summary>
    /// <param name="name">The channel name, already known non-empty.</param>
    /// <param name="subscription">The wrapper to subscribe.</param>
    /// <returns>The broker's code, or <see cref="RetCode.OK"/>.</returns>
    /// <remarks>
    /// The index entry is added only after the broker accepted the subscription, so a refusal cannot
    /// leave an orphan that <see cref="Off(string?, TaskNotificationHandler?)"/> would claim to have
    /// removed.
    /// </remarks>
    private long Register(string name, ChannelSubscription subscription)
    {
        // [n_cst_eventful.sru:L296] of_On(name, object, evtName) - the topic carries no ordering symbol,
        // no priority prefix and no namespace, so every subscription lands at normal priority, at the
        // TAIL of its equal-priority run [:L419], and outside the persistent namespace. That is what
        // makes subscription order the dispatch order, which is the contract the notify path relies on.
        long code = _broker.Subscribe(name, subscription, WrapperHandlerName);
        if (code != RetCode.OK)
        {
            return code;
        }

        if (!_wrappers.TryGetValue(name, out List<ChannelSubscription>? channel))
        {
            channel = [];
            _wrappers[name] = channel;
        }

        channel.Add(subscription);
        return RetCode.OK;
    }

    /// <summary>
    /// Runs one dispatch through the broker and turns the pump afterwards when it was the outermost
    /// one.
    /// </summary>
    /// <param name="name">The channel name; null and empty both dispatch nothing.</param>
    /// <param name="payload">The trigger's own arguments, which the broker copies in after the
    /// injected source.</param>
    /// <returns>The dispatch's answer.</returns>
    /// <remarks>
    /// <para>
    /// The depth is raised around the broker call rather than only after it, because a subscriber may
    /// dispatch again from inside its handler and the pump must not be turned while that outer dispatch
    /// is still in flight - a queued compaction no-ops above depth zero
    /// [<c>n_cst_eventful.sru:L580</c>], so draining it there would consume the owed work without
    /// doing it.
    /// </para>
    /// <para>
    /// The depth is restored in a <see langword="finally"/> so that a fault the threading broker lets
    /// propagate cannot leave this surface permanently believing a dispatch is open. The pump turn is
    /// deliberately NOT in that <see langword="finally"/>: a propagating fault is already unwinding,
    /// and running queued work on the way out would interleave it with the exception's own handling.
    /// </para>
    /// </remarks>
    private long? Dispatch(string? name, object?[] payload)
    {
        Depth++;
        object? answer;
        try
        {
            answer = _broker.Trigger(name ?? string.Empty, payload);
        }
        finally
        {
            Depth--;
        }

        if (Depth == 0 && _broker.PendingPostedContinuationCount > 0)
        {
            // The pump turn. See this member's remarks and Depth's.
            _ = _broker.DrainPostedContinuations();
        }

        // The established default is a boxed long, and every wrapper returns a nullable long, so this
        // unbox succeeds for every value the broker can produce here. `as` rather than a cast so an
        // unexpected shape reads as "nobody answered" instead of raising inside a notification.
        return answer as long?;
    }

    /// <summary>
    /// One subscription, as the broker sees it: an object carrying a member the broker resolves by
    /// name.
    /// </summary>
    /// <remarks>
    /// The base exists for the removal path, which has to ask a wrapper whether it carries a given
    /// delegate without knowing which of the two channel shapes it is. It carries no dispatch
    /// behaviour: the derived types' forwarding members are reached by the broker's own reflection and
    /// never through this type.
    /// </remarks>
    private abstract class ChannelSubscription
    {
        /// <summary>
        /// Whether this wrapper carries a given delegate.
        /// </summary>
        /// <param name="handler">The delegate a caller is trying to remove.</param>
        /// <returns><see langword="true"/> when this wrapper forwards to it.</returns>
        /// <remarks>
        /// Delegate equality, which for two conversions of the same method group over the same target
        /// compares equal - so a caller removes what it subscribed even when it did not keep the
        /// instance it passed.
        /// </remarks>
        internal abstract bool Wraps(Delegate handler);
    }

    /// <summary>
    /// A per-reason subscription - the wrapper the broker invokes for
    /// <see cref="TaskEventName.Start"/>, <see cref="TaskEventName.Stop"/>,
    /// <see cref="TaskEventName.Notify"/> and <see cref="TaskEventName.Error"/>.
    /// </summary>
    /// <param name="handler">The delegate to forward to.</param>
    private sealed class PerReasonSubscription(TaskNotificationHandler handler) : ChannelSubscription
    {
        /// <inheritdoc/>
        internal override bool Wraps(Delegate other) => handler.Equals(other);

        /// <summary>
        /// The member the broker resolves and invokes - four declared arguments, of which the broker's
        /// prepare hook fills the first with the source and the trigger's payload fills the rest.
        /// </summary>
        /// <param name="source">The task raising the notification, injected at
        /// <c>n_cst_threading_eventful.sru:L55</c>.</param>
        /// <param name="wparam">The first numeric argument.</param>
        /// <param name="lparam">The second numeric argument.</param>
        /// <param name="text">The string argument.</param>
        /// <returns>
        /// The subscriber's answer, or <see langword="null"/> when it declined - which the broker reads
        /// as "not handled" [<c>n_cst_eventful.sru:L909</c>] and then substitutes the established
        /// default for at the end of the dispatch.
        /// </returns>
        /// <remarks>
        /// Reached only by the broker's reflection [<c>n_cst_eventful.sru:L398-L403, :L866</c>], which
        /// is why it is private: nothing in this service may call a subscriber directly and bypass the
        /// veto, the capture filter and the exception hook.
        /// </remarks>
        private long? Invoke(SqlTaskProxyBase source, long wparam, long lparam, string text) =>
            handler(source, wparam, lparam, text);
    }

    /// <summary>
    /// A catch-all subscription - the wrapper the broker invokes for
    /// <see cref="TaskEventName.CommonNotify"/>.
    /// </summary>
    /// <param name="handler">The delegate to forward to.</param>
    private sealed class CommonSubscription(TaskCommonNotificationHandler handler) : ChannelSubscription
    {
        /// <inheritdoc/>
        internal override bool Wraps(Delegate other) => handler.Equals(other);

        /// <summary>
        /// The member the broker resolves and invokes - five declared arguments, one more than the
        /// per-reason shape because this channel receives the reason as well
        /// [<c>n_cst_threading_task.sru:L71-L78</c>].
        /// </summary>
        /// <param name="source">The task raising the notification, injected by the prepare hook.</param>
        /// <param name="reason">The <c>Enums.TNR_*</c> reason [<c>:L73</c>].</param>
        /// <param name="wparam">The first numeric argument.</param>
        /// <param name="lparam">The second numeric argument.</param>
        /// <param name="text">The string argument.</param>
        /// <returns>
        /// The subscriber's answer, or <see langword="null"/> when it declined. A non-zero answer here
        /// SUPPRESSES the per-reason channels entirely, which is the gate at <c>:L335</c> and is this
        /// channel's real power.
        /// </returns>
        private long? Invoke(
            SqlTaskProxyBase source,
            long reason,
            long wparam,
            long lparam,
            string text) =>
            handler(source, reason, wparam, lparam, text);
    }
}

#endregion


#region The threading-substrate seam - what this file needs from n_cst_threading_task, and no more

/// <summary>
/// The narrow slice of the caller-side threading substrate
/// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru</c>, 627 lines] that the SQL proxies
/// actually consume.
/// </summary>
/// <remarks>
/// <para>
/// <b>The substrate is deliberately NOT ported wholesale, and the shape here deliberately mirrors
/// <c>ISqlTaskHost</c> in the worker half of the pair.</b> <c>n_cst_threading_task.sru</c> is general
/// controller machinery - a seven-arity init, execution groups, cancellation and synchronization
/// handles, a nine-arity keyed-data surface, seven <c>of_Off</c> subscription arities and the private
/// notification fan-out - and porting it belongs outside this folder's remit. What is modelled is
/// exactly the set of substrate members <c>n_cst_threading_task_sqlbase.sru</c> and its three derived
/// proxies reach for, each with its locator, so the dependency is legible and finite.
/// </para>
/// <para>
/// <b>Modelling it as an interface rather than as a base class is what makes C-H reachable.</b> The
/// whole proxy layer becomes testable with NO REAL THREAD and no synchronization primitive, because a
/// test supplies a host that answers these questions directly. That matters most for the busy guard:
/// <c>of_IsBusy()</c> is a composition of the controller's state with a zero-timeout signal poll
/// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L305-L306</c> - note the file, which is the
/// SUBSTRATE and not the object being ported], and without a substitutable substrate every guarded member
/// in this file and in all three derived proxies would need a live thread to exercise.
/// </para>
/// <para>
/// <b><c>#Running</c> IS modelled here, unlike in the worker's seam, and the contrast is deliberate.</b>
/// Every guard written against <c>#Running</c> in the worker object is COMMENTED OUT in the oracle and
/// is carried across inert, so the worker's seam declares no such member. The guard that IS live is
/// this side's, and it reads <c>#Running</c> directly
/// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L305</c>] before every mutator in
/// <c>n_cst_threading_task_sqlbase.sru</c> [<c>:L57, :L73, :L95, :L110, :L124, :L144, :L162, :L179,
/// :L188</c>]. Declaring it here is therefore reproducing live behaviour, not reviving dead behaviour.
/// </para>
/// </remarks>
internal interface ISqlTaskProxyHost
{
    /// <summary>
    /// Whether the task is currently running - <c>#Running</c>
    /// [<c>n_cst_threading_task.sru:L112</c>], raised at <c>:L254</c> and lowered at <c>:L233</c> and
    /// <c>:L258-L260</c>.
    /// </summary>
    /// <value>
    /// <see langword="false"/> before start and after stop. It is the FIRST term of the busy
    /// composition [<c>:L305</c>].
    /// </value>
    bool IsRunning { get; }

    /// <summary>
    /// Whether the owning controller is busy - <c>#ParentThreading.of_IsBusy()</c>
    /// [<c>n_cst_threading_task.sru:L305</c>].
    /// </summary>
    /// <value>
    /// The answer the busy composition DELEGATES to while this task is not running - a task that has
    /// not started yet is busy exactly when its controller is.
    /// </value>
    bool IsControllerBusy { get; }

    /// <summary>
    /// Whether the task's synchronization signal is currently set - the zero-timeout poll
    /// <c>WaitForSingleObject(_hEvtSync, 0)</c> [<c>n_cst_threading_task.sru:L306</c>], over the handle
    /// declared at <c>:L124</c>.
    /// </summary>
    /// <value>
    /// <see langword="true"/> when the signal is set, which is the <c>WAIT_OBJECT_0</c> answer.
    /// </value>
    /// <remarks>
    /// <b>MIND THE INVERSION - it is the single easiest thing to get backwards in this file.</b> The
    /// oracle answers busy with <c>(WaitForSingleObject(_hEvtSync,0) &lt;&gt; 0)</c>, and a non-zero
    /// result is <c>WAIT_TIMEOUT</c>, meaning NOT SIGNALLED. So a running task is BUSY while this
    /// property is <see langword="false"/>. The signal is raised for the duration of each framework
    /// callback [<c>:L216, :L255, :L291</c>] and cleared again afterwards [<c>:L218, :L263, :L295</c>],
    /// which makes the guard a re-entrancy gate: mutators are permitted from INSIDE a framework
    /// callback and refused from outside one while the task runs.
    /// </remarks>
    bool IsSyncSignalSet { get; }

    /// <summary>
    /// Raises the synchronization signal - <c>SetEvent(_hEvtSync)</c>, as the notification handler does
    /// before it fans out [<c>n_cst_threading_task.sru:L291</c>].
    /// </summary>
    void RaiseSyncSignal();

    /// <summary>
    /// Clears the synchronization signal - <c>ResetEvent(_hEvtSync)</c>, as the notification handler
    /// does after it fans out [<c>n_cst_threading_task.sru:L295</c>].
    /// </summary>
    void ClearSyncSignal();

    /// <summary>
    /// Whether the task has been cancelled - <c>of_IsCancelled()</c>
    /// [<c>n_cst_threading_task.sru:L318-L319</c>].
    /// </summary>
    /// <value>
    /// <see langword="true"/> when the last exit code was <see cref="RetCode.CANCELLED"/>
    /// [<c>:L318</c>] OR the cancellation signal is set [<c>:L319</c>]. Both terms are the oracle's;
    /// the first is what makes a stopped-by-cancellation task keep answering cancelled.
    /// </value>
    bool IsCancelled { get; }

    /// <summary>
    /// The cancellation token - the .NET expression of the cancellation handle
    /// <c>of_GetCancelEvent()</c> returns [<c>n_cst_threading_task.sru:L322</c>], created manual-reset
    /// and initially unsignalled at <c>:L194</c> and signalled by <see cref="Cancel"/>.
    /// </summary>
    /// <value>
    /// A token that becomes cancelled when the legacy handle would become signalled, so an awaiting
    /// caller can honour cancellation cooperatively rather than by polling.
    /// </value>
    /// <remarks>
    /// <b>This is the substitute for the deliberately non-ported handle surface.</b> A raw handle has
    /// no meaning in a Linux container, and the async request/response expression of the proxy/worker
    /// pair is defined in terms of a token, so the token IS the port rather than a convenience beside
    /// it. <see cref="IsCancelled"/> remains available because the oracle's predicate carries the
    /// extra exit-code term that a bare token cannot express.
    /// </remarks>
    CancellationToken Cancellation { get; }

    /// <summary>
    /// The exit code the task stopped with - <c>of_GetLastExitCode()</c>
    /// [<c>n_cst_threading_task.sru:L309</c>], latched at <c>:L234</c> and reset at <c>:L249</c>.
    /// </summary>
    long LastExitCode { get; }

    /// <summary>
    /// The last framework error code - <c>of_GetLastErrorCode()</c>
    /// [<c>n_cst_threading_task.sru:L312</c>], latched at <c>:L213</c> and reset at <c>:L248</c>.
    /// </summary>
    long LastErrorCode { get; }

    /// <summary>
    /// The last framework error text - <c>of_GetLastErrorInfo()</c>
    /// [<c>n_cst_threading_task.sru:L315</c>], latched at <c>:L214</c> and cleared at <c>:L250</c>.
    /// </summary>
    string LastErrorInfo { get; }

    /// <summary>
    /// The task's identifier - <c>of_GetID()</c> [<c>n_cst_threading_task.sru:L398</c>], taken from the
    /// worker at <c>:L201</c>.
    /// </summary>
    ulong TaskId { get; }

    /// <summary>
    /// The task's ONE-BASED position in its controller's task list - <c>of_GetIndex()</c>
    /// [<c>n_cst_threading_task.sru:L401</c>].
    /// </summary>
    /// <value>
    /// A one-based ordinal. <b>It is NOT converted anywhere in this folder</b>: the worker's committed
    /// handler walks DOWN to 1 from it [<c>n_cst_thread_task_sqlbase.sru:L104</c>], so the value only
    /// makes sense in the base the oracle produced it in.
    /// </value>
    int TaskIndex { get; }

    /// <summary>
    /// The worker's registered class name, lower-cased - <c>of_GetTaskClassName()</c>
    /// [<c>n_cst_threading_task.sru:L601</c>], lower-cased when stored at <c>:L191</c>.
    /// </summary>
    string TaskClassName { get; }

    /// <summary>
    /// The worker task this proxy drives - the substrate's <c>_Task</c> property
    /// [<c>n_cst_threading_task.sru:L118</c>], assigned by the ancestor's init through
    /// <c>ParentThread.of_InsertTask(index, ref _Task, clsName)</c> [<c>:L188</c>].
    /// </summary>
    /// <value>
    /// <see langword="null"/> until <see cref="OnInit"/> has succeeded, and <see langword="null"/>
    /// again after the substrate's uninit nulls it [<c>:L273</c>], which is hazard 2 expressed by the
    /// oracle itself.
    /// </value>
    /// <remarks>
    /// <b>The type is <see cref="SqlTaskBase"/> and not a derived worker, deliberately.</b> Each derived
    /// proxy narrows it with its own typed accessor - the legacy's repeated private
    /// <c>_of_gettask()</c> downcast - and the two halves of the pair must stay separately
    /// registrable, so the seam publishes the base and lets the narrowing happen at the point that
    /// needs it.
    /// </remarks>
    SqlTaskBase? Task { get; }

    /// <summary>
    /// The substrate's own init handler - what <c>call super::oninit</c> reaches
    /// [<c>n_cst_threading_task_sqlbase.sru:L213</c>], implemented at
    /// <c>n_cst_threading_task.sru:L179-L206</c>.
    /// </summary>
    /// <param name="workerClassName">
    /// The worker class to insert. The oracle resolves an empty name through
    /// <c>Event OnGetTaskClsName()</c> and asserts the result is non-empty [<c>:L183-L186</c>], which is
    /// why the proxy publishes that name as a required member rather than an optional one.
    /// </param>
    /// <returns>
    /// The ancestor's return value. <see cref="RetCode.OK"/> on success [<c>:L205</c>], or the failing
    /// code from task insertion [<c>:L189</c>].
    /// </returns>
    long OnInit(string workerClassName);

    /// <summary>
    /// The substrate's own prepare handler - what <c>call super::onprepare</c> reaches
    /// [<c>n_cst_threading_task_sqlbase.sru:L206</c>].
    /// </summary>
    /// <returns>
    /// The ancestor's return value. <b>The SQL proxy DISCARDS it</b>, which is recorded at the point of
    /// reproduction rather than here.
    /// </returns>
    long OnPrepare();

    /// <summary>
    /// Requests cancellation - <c>of_Cancel()</c>, which signals the cancellation handle and answers
    /// <see cref="RetCode.OK"/> unconditionally [<c>n_cst_threading_task.sru:L369-L370</c>].
    /// </summary>
    /// <returns><see cref="RetCode.OK"/>.</returns>
    long Cancel();

    /// <summary>
    /// Delays the worker's start - the worker-side half of <c>of_SetDelayFor</c>
    /// [<c>n_cst_threading_task.sru:L608</c> forwarding to
    /// <c>ws_objects/pfw.thread.pbl.src/n_cst_thread_task.sru:L593</c>].
    /// </summary>
    /// <param name="seconds">The delay in seconds.</param>
    /// <returns>The worker's result.</returns>
    /// <remarks>
    /// On the seam rather than on <see cref="SqlTaskBase"/> because it belongs to the worker's
    /// SUBSTRATE half, <c>n_cst_thread_task</c>, which that type deliberately does not model.
    /// </remarks>
    long SetWorkerDelayFor(double seconds);

    /// <summary>
    /// Marks the worker to be skipped - the worker-side half of <c>of_SetSkip</c>
    /// [<c>n_cst_threading_task.sru:L615</c> forwarding to
    /// <c>ws_objects/pfw.thread.pbl.src/n_cst_thread_task.sru:L609</c>].
    /// </summary>
    /// <param name="skip">Whether to skip the task.</param>
    /// <returns>The worker's result.</returns>
    long SetWorkerSkip(bool skip);
}

#endregion

#region The retained-transaction seam - PowerBuilder's `transaction` and its pooled subclass

/// <summary>
/// The connection-settings source a proxy may be handed - the .NET stand-in for PowerBuilder's
/// built-in <c>transaction</c> object, which
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru:L70</c> accepts and
/// <c>:L76-L83</c> copies from.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a SETTINGS-BEARING HANDLE, not a live connection (C-E).</b> Nothing here opens,
/// connects, executes or names an engine. <c>Transactions/TransactionPool.cs</c> owns connections;
/// this proxy owns only what the oracle retained, which is the descriptor-level information plus - when
/// a pooled object was supplied - the reference to that pooled object.
/// </para>
/// <para>
/// <b>All NINE legacy members are exposed even though the copy takes only EIGHT.</b> That is
/// deliberate and load-bearing for review: the built-in object genuinely has a ninth
/// (<see cref="UserParm"/>), and the oracle genuinely declines to copy it [<c>:L76-L83</c>]. Exposing
/// it makes the omission VISIBLE and testable rather than indistinguishable from a member nobody
/// thought of.
/// </para>
/// </remarks>
internal interface ISqlTransactionObject
{
    /// <summary>The DBMS identifier - copied first [<c>n_cst_threading_task_sqlbase.sru:L76</c>].</summary>
    string Dbms { get; }

    /// <summary>The server name - copied second [<c>n_cst_threading_task_sqlbase.sru:L77</c>].</summary>
    string ServerName { get; }

    /// <summary>The database name - copied third [<c>n_cst_threading_task_sqlbase.sru:L78</c>].</summary>
    string Database { get; }

    /// <summary>The login identifier - copied fourth [<c>n_cst_threading_task_sqlbase.sru:L79</c>].</summary>
    string LogId { get; }

    /// <summary>The connection parameter string - copied sixth [<c>n_cst_threading_task_sqlbase.sru:L81</c>].</summary>
    string DbParm { get; }

    /// <summary>The isolation level - copied seventh [<c>n_cst_threading_task_sqlbase.sru:L82</c>].</summary>
    string Lock { get; }

    /// <summary>
    /// The auto-commit flag - copied eighth and last [<c>n_cst_threading_task_sqlbase.sru:L83</c>].
    /// </summary>
    /// <remarks>
    /// <b>The worker ERASES this the instant it arrives</b>, and both halves are behaviour. See the
    /// annotation on the eight-field setter for the evidence and for why the copy is not optimised
    /// away.
    /// </remarks>
    bool AutoCommit { get; }

    /// <summary>
    /// The user-defined parameter - the built-in object's ninth member, and the one the oracle
    /// DELIBERATELY DOES NOT COPY.
    /// </summary>
    /// <value>
    /// Whatever the source holds. It is read by nothing in this file, on purpose: the copy at
    /// <c>n_cst_threading_task_sqlbase.sru:L76-L83</c> has no corresponding line, so a descriptor built
    /// here always leaves its own user parameter at the cleared state a freshly declared
    /// <c>TRANSACTIONDATA</c> has. Reproduced, not completed (C-B).
    /// </value>
    string UserParm { get; }

    /// <summary>
    /// Discloses the credential for transfer into a connection descriptor - the fifth copied member
    /// [<c>n_cst_threading_task_sqlbase.sru:L80</c>].
    /// </summary>
    /// <returns>The password, or <see cref="string.Empty"/> when none was supplied.</returns>
    /// <remarks>
    /// <b>A NAMED DOOR RATHER THAN A PROPERTY, and that is constraint C-F expressed as a type.</b>
    /// AAP 0.4.2.6 makes the credential WRITE-ONLY: never echoed in a response, never logged.
    /// <see cref="TransactionData.LogPass"/> enforces that structurally by having no getter at all, and
    /// reads its own connect-path value back through the equivalently named
    /// <c>RevealLogPassForConnect</c>. This member is the same discipline one level further out: a
    /// plain <c>LogPass</c> property would let any caller ask for the value and interpolate the answer
    /// into a log line, so the only way to obtain it is through a call whose name states at every call
    /// site exactly what is being done and why. There is exactly ONE call site in this file.
    /// </remarks>
    string RevealLogPassForTransfer();
}

/// <summary>
/// A pooled transaction object - the .NET stand-in for <c>n_cst_thread_trans</c>, which the oracle
/// declares as <c>global type n_cst_thread_trans from transaction</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L4, :L8</c>] and which
/// <c>n_cst_threading_task_sqlbase.sru:L122</c> accepts as a distinct overload.
/// </summary>
/// <remarks>
/// <para>
/// <b>The inheritance is the oracle's and is why two overloads exist rather than one.</b> Because the
/// pooled type derives from the built-in one, PowerBuilder resolves
/// <c>of_SetTransObject(n_cst_thread_trans)</c> to the more specific overload [<c>:L122</c>] and
/// <c>of_SetTransObject(transaction)</c> to the general one [<c>:L70</c>]. C# overload resolution
/// selects the more derived interface identically, so the distinction survives without a flag or a
/// type test. It also explains an assignment that otherwise looks ill-typed: the pooled overload
/// stores its argument into a field declared <c>Transaction _Trans</c> [<c>:L16, :L129</c>], which is
/// legal precisely because of this inheritance.
/// </para>
/// <para>
/// The single added member is the one the oracle adds and uses.
/// </para>
/// </remarks>
internal interface IPooledSqlTransactionObject : ISqlTransactionObject
{
    /// <summary>
    /// Reads the pooled object's connection descriptor - <c>of_GetTransData()</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L92, :L394-L400</c>], called by the
    /// pooled setter at <c>n_cst_threading_task_sqlbase.sru:L127</c>.
    /// </summary>
    /// <returns>
    /// The descriptor. <see cref="TransactionData.GetTransactionData(GetTransactionDataHook?)"/> is the
    /// ported accessor and carries the oracle's own quirks - notably that it DISCARDS the result code
    /// of the inner call [<c>n_cst_thread_trans.sru:L397</c>] and returns the descriptor regardless -
    /// so an implementation
    /// should route through it rather than re-deriving the copy.
    /// </returns>
    /// <remarks>
    /// <b>The credential does NOT come back out through here.</b> The ported accessor moves six fields
    /// outbound rather than the oracle's seven, deliberately omitting the password because an outbound
    /// accessor that hands it back IS the echo AAP 0.4.2.6 forbids. That omission is recorded in
    /// <c>Transactions/TransactionData.cs</c> and is not re-litigated here; the consequence for this
    /// file is simply that a pooled setter installs a descriptor whose credential is whatever the
    /// worker already held.
    /// </remarks>
    TransactionData GetTransData();
}

#endregion


#region SqlTaskProxyBase - the abstract caller-side proxy

/// <summary>
/// The abstract CALLER-SIDE proxy for an asynchronous SQL task - the port of
/// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru</c> (222 lines), which the
/// oracle marks <c>[运行在当前线程]</c> at <c>:L2</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>THIS TYPE IS ONE HALF OF A PAIR, AND THE PAIR IS NOT COLLAPSED (C-K).</b> Its counterpart is
/// <see cref="SqlTaskBase"/>, which the oracle marks <c>[运行在子线程]</c>
/// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L2</c>]. Across the fifteen
/// objects of <c>ws_objects/pfw.thread.ext.pbl.src</c> the affinity split MEASURES as six worker-thread
/// objects, EXACTLY ONE main-thread object, and four calling-thread objects - the four proxies of this
/// folder. Thread affinity is therefore a CONTRACT the oracle states explicitly, not commentary about
/// an implementation detail, and the .NET expression of it is async request/response with a
/// <see cref="CancellationToken"/> that PRESERVES BOTH TYPES AND THE BOUNDARY BETWEEN THEM.
/// </para>
/// <para>
/// <b>The two-sided reset is the evidence that one fused method could not express the pair.</b>
/// <see cref="Reset"/> guards on its OWN busy state [<c>:L57</c>], DELEGATES the reset to the worker
/// [<c>:L61</c>], ABANDONS if the worker refused [<c>:L62</c>], and only THEN clears its own
/// caller-side state [<c>:L64-L65</c>]. Two distinct state sets, a delegation between them, and a
/// failure mode in which the worker's refusal leaves caller-side state UNTOUCHED. A single fused
/// method has one state set and therefore cannot have that failure mode at all, so fusing the pair
/// would silently delete a behaviour rather than simplify one.
/// </para>
/// <para>
/// <b>Registration (C-J).</b> The oracle's global auto-instance
/// <c>global n_cst_threading_task_sqlbase n_cst_threading_task_sqlbase</c> [<c>:L11</c>] is NOT
/// reproduced: this is a DI-registered type with no static mutable state. A composition root must
/// register BOTH halves of the pair - this proxy AND <see cref="SqlTaskBase"/>'s concrete derivations -
/// because they are separate types with separate lifetimes and neither substitutes for the other.
/// </para>
/// <para>
/// <b>What <see cref="Reset"/> means for the derived proxies.</b> <c>of_reset</c> does NOT exist on the
/// framework base <c>n_cst_threading_task</c>; it is introduced by THIS object. So the
/// <c>super::of_Reset()</c> in the query proxy [<c>n_cst_threading_task_sqlquery.sru:L284</c>] and in
/// the update proxy [<c>n_cst_threading_task_sqlupdate.sru:L85</c>] resolves to this member. It is a
/// genuine hard prerequisite for both, not a convention they happen to follow.
/// </para>
/// <para>
/// <b>Hazard 1 shapes every signature in this folder</b>
/// [<c>docs/PB多线程绕坑提示.md:L1-L4</c>]. A worker thread synchronously calling a main-thread object's
/// function or event whose return type is <c>string</c> or <c>blob</c> can fault, and the guidance is
/// to return via <c>ref</c> instead. That is why the one event this type declares carries its payload
/// INWARD as an <see langword="in"/> parameter and returns nothing, and why the derived query proxy's
/// payload-bearing callbacks take <c>ref blob</c>
/// [<c>n_cst_threading_task_sqlquery.sru:L10, :L13</c>]. <b>Hazard 2</b> [<c>:L5</c>] is why
/// <see cref="Dispose(bool)"/> clears every cross-boundary reference EXPLICITLY rather than leaving
/// them to the collector.
/// </para>
/// </remarks>
internal abstract class SqlTaskProxyBase : ISqlTaskProxy, IDisposable
{
    private readonly ISqlTaskProxyHost _host;
    private readonly ILogger _logger;

    /// <summary>
    /// The worker's commit signal, BORROWED rather than owned - the port of <c>ulong _hEvtCommitted</c>
    /// [<c>n_cst_threading_task_sqlbase.sru:L22</c>], obtained from the worker during init at
    /// <c>:L218</c>.
    /// </summary>
    /// <remarks>
    /// Never created here. The worker creates it lazily as manual-reset and initially unsignalled
    /// [<c>n_cst_thread_task_sqlbase.sru:L224-L226</c>], and therefore also owns its disposal - which is
    /// exactly why <see cref="Dispose(bool)"/> drops the reference without disposing the object.
    /// </remarks>
    private ManualResetEventSlim? _committedSignal;

    private bool _disposed;

    /// <summary>
    /// Initializes the proxy against a threading substrate and a clock.
    /// </summary>
    /// <param name="host">
    /// The threading substrate seam - the narrow slice of <c>n_cst_threading_task</c> this type
    /// consumes. Substitutable by design, which is what makes the whole proxy layer testable with no
    /// real thread (C-H).
    /// </param>
    /// <param name="logger">
    /// Diagnostics sink. <b>C-F: nothing written through it may carry the latched payload's statement
    /// text or a credential</b>, and nothing in this file does - the only thing logged here is a
    /// captured subscriber fault.
    /// </param>
    /// <param name="timeProvider">
    /// The service's single clock seam. <b>C-H: this is the ONLY clock any proxy in this folder may
    /// read.</b>
    /// </param>
    /// <param name="executionGroup">
    /// The execution group - the oracle's <c>#Group</c>, whose default is <c>GROUP_NORMAL</c>
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L111</c>].
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="host"/>, <paramref name="logger"/> or <paramref name="timeProvider"/> is
    /// <see langword="null"/>. Fail-fast on a structural fault is the legacy posture and is preserved:
    /// a proxy without a substrate cannot degrade gracefully into anything meaningful.
    /// </exception>
    protected SqlTaskProxyBase(
        ISqlTaskProxyHost host,
        ILogger logger,
        TimeProvider timeProvider,
        TaskExecutionGroup executionGroup = TaskExecutionGroup.Normal)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _host = host;
        _logger = logger;
        Clock = timeProvider;
        ExecutionGroup = executionGroup;

        // [n_cst_threading_task.sru:L195-L197] `_Eventful = Create n_cst_threading_eventful` followed
        // immediately by `_Eventful.Event OnInit(this,_hEvtCancelled,_hEvtSync,...)`. The source is
        // `this`, exactly as the oracle passes it, which is what the broker's prepare hook injects as
        // every subscriber's leading argument. Constructed here rather than as a property initialiser
        // precisely because it needs both `this` and the substrate, and the substrate has only just
        // been accepted.
        Notifications = new TaskNotificationDispatcher(this, new HostBrokerSignals(host));

        // `DBERRORDATA _lastDBError` [:L17] - PowerBuilder initialises an unassigned structure to its
        // cleared state, which is what DbErrorData.Empty reproduces. Stated rather than relied upon,
        // because the whole point of the latch is that "no error yet" is distinguishable.
        LastDbError = DbErrorData.Empty;
    }

    /// <summary>
    /// The threading broker's three signals, answered from the substrate seam - the .NET stand-in for
    /// the three raw Win32 handles <c>n_cst_threading_task.sru:L197</c> hands the broker.
    /// </summary>
    /// <param name="host">The substrate seam that owns the signals.</param>
    /// <remarks>
    /// <para>
    /// <b>Why the cancellation term is the COMPOSITE predicate and not the raw signal.</b> The oracle's
    /// broker polls <c>_hEvtCancelled</c> directly [<c>n_cst_threading_eventful.sru:L49, :L61</c>],
    /// while <see cref="ISqlTaskProxyHost.IsCancelled"/> is the substrate's <c>of_iscancelled</c> - the
    /// latched-exit-code term OR the signal term [<c>n_cst_threading_task.sru:L318-L319</c>]. The two
    /// differ only by that first term, and the exit code is latched to
    /// <see cref="RetCode.CANCELLED"/> only by a cancellation, which has already set the signal and
    /// which nothing ever lowers. So the composite is the same predicate with one term that cannot be
    /// true on its own, and reading it keeps this service with ONE cancellation authority instead of
    /// two that could disagree.
    /// </para>
    /// <para>
    /// A separate private type rather than <see cref="SqlTaskProxyBase"/> implementing the interface
    /// itself: the signals are the SUBSTRATE's, not the proxy's, and publishing six more members on
    /// every derived proxy to satisfy the broker would put a cancellation raiser on the public surface
    /// of every SQL task proxy in this folder.
    /// </para>
    /// </remarks>
    private sealed class HostBrokerSignals(ISqlTaskProxyHost host) : IThreadingBrokerSignals
    {
        /// <inheritdoc/>
        public bool IsCancelled => host.IsCancelled;

        /// <inheritdoc/>
        public bool IsSyncSignalSet => host.IsSyncSignalSet;

        /// <inheritdoc/>
        public void RaiseSyncSignal() => host.RaiseSyncSignal();

        /// <inheritdoc/>
        public void ClearSyncSignal() => host.ClearSyncSignal();

        /// <inheritdoc/>
        public void RaiseCancellation() => _ = host.Cancel();

        /// <inheritdoc/>
        public void RaiseException() => _ = host.Cancel();
    }

    #region Caller-side state [:L15-L23]

    /// <summary>
    /// The service's single clock seam, exposed to derived proxies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>C-H: this is the ONLY clock any proxy in this folder may read.</b> Nothing in this file reads
    /// <c>DateTime.UtcNow</c>, <c>DateTime.Now</c>, <c>Environment.TickCount</c> or a
    /// <c>Stopwatch</c>, and a derived proxy must not either - a layer that reads an ambient clock
    /// cannot be characterized, and the paired legacy-versus-target recordings the parity model depends
    /// on would stop being comparable.
    /// </para>
    /// <para>
    /// <b>Recorded honestly: this file itself reads no clock, and neither does any of the three derived
    /// proxies today</b> - a sweep of <c>n_cst_threading_task_sqlcommand.sru</c>,
    /// <c>n_cst_threading_task_sqlquery.sru</c> and <c>n_cst_threading_task_sqlupdate.sru</c> finds no
    /// <c>CPU()</c>, <c>Now()</c> or <c>Today()</c> call. The seam is nonetheless published here rather
    /// than omitted, because that is what makes the prohibition STRUCTURAL for everything written
    /// against this base: a derived proxy that later needs elapsed time has the correct instrument
    /// already in hand and no reason to reach for an ambient one. The shape deliberately matches
    /// <see cref="SqlTaskBase"/>'s own <c>protected TimeProvider Clock</c>, so one convention covers
    /// both halves of the pair.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <b>NO ws_objects LOCATOR, because there is no legacy equivalent to cite.</b> This is a .NET
    /// determinism seam introduced by the migration, not a port of a legacy member - the sentence above
    /// records the ABSENCE of a clock read in the oracle, and an absence has no line number.
    /// </remarks>
    protected TimeProvider Clock { get; }

    /// <summary>
    /// The execution group this task runs in - the oracle's <c>#Group</c>
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L111</c>], assigned from the init
    /// argument at <c>:L203</c>.
    /// </summary>
    public TaskExecutionGroup ExecutionGroup { get; }

    /// <summary>
    /// The notification surface - the port of the framework broker instance <c>_Eventful</c>
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L119</c>], created and initialised at
    /// <c>:L195-L197</c>.
    /// </summary>
    /// <remarks>
    /// Exposed to derived proxies so they can subscribe, unsubscribe and dispatch without reaching
    /// around this type. Dispatch is the shared broker's, specialized for threading by
    /// <see cref="ThreadingEventBroker"/>; see <see cref="TaskNotificationDispatcher"/> for what this
    /// surface adapts and what it deliberately does not restate.
    /// </remarks>
    protected TaskNotificationDispatcher Notifications { get; }

    /// <summary>
    /// The latched database error - the port of <c>DBERRORDATA _lastDBError</c>
    /// [<c>n_cst_threading_task_sqlbase.sru:L17</c>].
    /// </summary>
    /// <value>
    /// The most recent payload the worker pushed, or <see cref="DbErrorData.Empty"/> when none has been
    /// pushed since the last <see cref="Reset"/> or <see cref="OnPrepare"/>.
    /// </value>
    /// <remarks>
    /// <para>
    /// <b>The setter is <see langword="protected"/> because a derived proxy genuinely mutates it.</b>
    /// The update proxy rewrites the row ordinal in place - reading <c>_lastDBError.row</c> and
    /// assigning back to it [<c>n_cst_threading_task_sqlupdate.sru:L315, :L324-L325, :L336-L337</c>] -
    /// which is a real behaviour and the reason this is not a read-only property.
    /// </para>
    /// <para>
    /// <b>C-F.</b> The payload's statement member carries the COMPLETE generated statement including
    /// interpolated literal values, and the legacy logger redacts nothing. Nothing in this file logs
    /// it, interpolates it, or returns it outward except through
    /// <see cref="GetLastDbErrorForWire"/>, which is the one sanctioned projection.
    /// </para>
    /// </remarks>
    protected DbErrorData LastDbError { get; set; }

    /// <summary>
    /// The retained transaction object - the port of <c>Transaction _Trans</c>
    /// [<c>n_cst_threading_task_sqlbase.sru:L16</c>].
    /// </summary>
    /// <value>
    /// The object a successful <see cref="SetTransObject(ISqlTransactionObject)"/> or
    /// <see cref="SetTransObject(IPooledSqlTransactionObject)"/> was handed, or <see langword="null"/>
    /// when neither has succeeded. <b>The two descriptor-only setters never populate it</b>, which is
    /// the oracle's own asymmetry: only the two OBJECT overloads assign it [<c>:L87, :L129</c>].
    /// </value>
    /// <remarks>
    /// <para>
    /// <b>It is a settings-bearing handle, never a live connection (C-E).</b> Nothing here connects,
    /// executes or names an engine; <c>Transactions/TransactionPool.cs</c> owns connections. What is
    /// retained is the descriptor-level information plus, where a pooled object was supplied, the
    /// reference to that pooled object.
    /// </para>
    /// <para>
    /// <b>Measured finding, recorded because it prevents a wrong "improvement".</b> Across the whole of
    /// <c>ws_objects/pfw.thread.ext.pbl.src</c>, this field is written at exactly two sites
    /// [<c>:L87, :L129</c>] and read at exactly one [<c>:L50</c>], and NO object in the library calls the
    /// proxy's <c>of_GetTransObject()</c> at all. It is a pure retention slot with a single accessor -
    /// the identically named <c>of_gettransobject</c> members on the WORKER
    /// [<c>n_cst_thread_task_sqlbase.sru:L148, :L186</c>] are a different member on a different type with
    /// a different signature. Retained faithfully rather than pruned: the accessor is public legacy
    /// surface and a caller outside the library may hold it.
    /// </para>
    /// </remarks>
    protected ISqlTransactionObject? RetainedTransaction { get; private set; }

    /// <summary>
    /// Whether a connection descriptor has been installed on the worker - the port of
    /// <c>Boolean _bHasTransData</c> [<c>n_cst_threading_task_sqlbase.sru:L18</c>].
    /// </summary>
    /// <remarks>
    /// <b>PRESERVED LEGACY DEFECT - THIS FLAG IS NEVER CLEARED (C-B).</b> The oracle sets it true at
    /// <c>:L116</c>, which is the ONLY write to it anywhere in the object, and clears it NOWHERE - not in
    /// <c>of_reset</c> [<c>:L53-L68</c>], which clears the parameter flag and the latched error but not
    /// this one, and not in <c>onprepare</c> [<c>:L206-L211</c>]. Once a descriptor has been installed the
    /// proxy reports so for the rest of its life, even after a reset that the worker accepted. The
    /// asymmetry with <see cref="ParamsInstalled"/> is reproduced deliberately and is NOT tidied: adding
    /// a clear would change what <see cref="HasTransData"/> answers after a reset, which is observable.
    /// </remarks>
    protected bool TransDataInstalled { get; private set; }

    /// <summary>
    /// Whether at least one SQL parameter has been added - the port of <c>Boolean _bHasParams</c>
    /// [<c>n_cst_threading_task_sqlbase.sru:L19</c>].
    /// </summary>
    /// <remarks>
    /// Set true by a successful parameter add [<c>:L155</c>] and cleared by both
    /// <see cref="Reset"/> [<c>:L64</c>] and <see cref="ResetParams"/> [<c>:L164</c>] - which is
    /// precisely the treatment <see cref="TransDataInstalled"/> does NOT receive.
    /// </remarks>
    protected bool ParamsInstalled { get; private set; }

    /// <summary>
    /// The worker task this proxy drives - the port of <c>_Task</c>
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L118</c>], resolved from the substrate
    /// once <see cref="OnInit"/> has succeeded.
    /// </summary>
    /// <value><see langword="null"/> before a successful init and after disposal.</value>
    protected SqlTaskBase? WorkerTask { get; private set; }

    #endregion

    #region The abstract identity each derived proxy supplies

    /// <summary>
    /// The proxy's type marker - the port of <c>#Type</c>
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L110</c>], which each derived proxy
    /// overrides: <c>"sqlcommand"</c> [<c>n_cst_threading_task_sqlcommand.sru:L9</c>],
    /// <c>"sqlquery"</c> [<c>n_cst_threading_task_sqlquery.sru:L9</c>] and <c>"sqlupdate"</c>
    /// [<c>n_cst_threading_task_sqlupdate.sru:L17</c>].
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The marker is a PROXY-ONLY concept, and that is measured rather than assumed.</b> Each derived
    /// proxy carries it twice - once as the overridden property and once as a
    /// <c>constant string TASK_TYPE</c> of the same value
    /// [<c>n_cst_threading_task_sqlcommand.sru:L17</c>, <c>n_cst_threading_task_sqlquery.sru:L23</c>,
    /// <c>n_cst_threading_task_sqlupdate.sru:L27</c>] - while the four WORKER objects carry NEITHER
    /// spelling. So nothing on the worker side has a type marker, and a reader looking for the
    /// counterpart of this member on <see cref="SqlTaskBase"/> will not find one because there is none.
    /// </para>
    /// <para>
    /// The two legacy spellings are one value expressed twice, so the port publishes it once. The value
    /// is preserved; the SCREAMING_SNAKE constant's spelling cannot be, because this folder is outside
    /// the <c>.editorconfig</c> naming suppressions.
    /// </para>
    /// </remarks>
    protected abstract string TaskType { get; }

    /// <summary>
    /// The LEGACY class name of the worker this proxy drives - what the oracle's
    /// <c>ongettaskclsname</c> event answers, and what the substrate inserts by name.
    /// </summary>
    /// <value>
    /// One of the three legacy class-name strings, which are contract rather than description because
    /// the substrate resolves a worker by them: <c>"n_cst_thread_task_sqlcommand"</c>
    /// [<c>n_cst_threading_task_sqlcommand.sru:L56</c>], <c>"n_cst_thread_task_sqlquery"</c>
    /// [<c>n_cst_threading_task_sqlquery.sru:L500</c>] and <c>"n_cst_thread_task_sqlupdate"</c>
    /// [<c>n_cst_threading_task_sqlupdate.sru:L288</c>].
    /// </value>
    /// <remarks>
    /// <para>
    /// <b>Why the strings are preserved verbatim.</b> They are the key the substrate's
    /// <c>of_InsertTask</c> resolves a worker by [<c>n_cst_threading_task.sru:L188</c>], they are stored
    /// lower-cased and published again through <see cref="GetTaskClassName"/> [<c>:L191, :L601</c>], and
    /// they appear in characterization recordings - so a "tidied" name would silently resolve nothing
    /// and would invalidate stored comparisons at the same time.
    /// </para>
    /// <para>
    /// A harmless legacy inconsistency worth one line, since a reader will meet it in all three derived
    /// files: the command and query proxies write <c>call super::ongettaskclsname</c> before returning
    /// their name while the update proxy does not
    /// [<c>n_cst_threading_task_sqlcommand.sru:L56</c>, <c>n_cst_threading_task_sqlquery.sru:L500</c>
    /// versus <c>n_cst_threading_task_sqlupdate.sru:L288</c>]. The ancestor's answer is discarded in both
    /// forms, so the two are observably identical; an overriding property has no ancestor call to make
    /// and the inconsistency simply disappears, which is a difference in spelling and not in behaviour.
    /// </para>
    /// </remarks>
    protected abstract string WorkerTaskClassName { get; }

    #endregion

    #region The busy guard - a COMPOSITION, never a flag [n_cst_threading_task.sru:L305-L307]

    /// <summary>
    /// Whether the task is busy - the port of <c>of_isbusy</c>
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L305-L307</c>].
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when a mutator must be refused with <see cref="RetCode.E_BUSY"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>THIS IS NOT A BOOLEAN FIELD, AND COLLAPSING IT INTO ONE WOULD CHANGE BEHAVIOUR.</b> The oracle
    /// reads, verbatim:
    /// </para>
    /// <code>
    /// if Not #Running then return #ParentThreading.of_IsBusy()   [:L305]
    /// return (WaitForSingleObject(_hEvtSync,0) &lt;&gt; 0)              [:L306]
    /// </code>
    /// <para>
    /// It COMPOSES two independent facts. While the task is not running it DELEGATES entirely to the
    /// owning controller, so an unstarted task is busy exactly when its controller is. Once running it
    /// answers from a ZERO-TIMEOUT, NON-BLOCKING poll of its own synchronization signal.
    /// </para>
    /// <para>
    /// <b>MIND THE INVERSION.</b> The oracle's poll answers busy on <c>&lt;&gt; 0</c>, and a non-zero
    /// <c>WaitForSingleObject</c> result is <c>WAIT_TIMEOUT</c> - NOT signalled. So a running task is
    /// BUSY while the signal is CLEAR, which is why this returns
    /// <c>!<see cref="ISqlTaskProxyHost.IsSyncSignalSet"/></c> and not the property itself. The signal is
    /// raised for the duration of each framework callback and cleared afterwards
    /// [<c>:L216/:L218</c>, <c>:L255/:L263</c>, <c>:L291/:L295</c>], which makes this a re-entrancy gate:
    /// mutators are permitted from INSIDE a framework callback and refused from outside one while the
    /// task runs.
    /// </para>
    /// <para>
    /// <b>It neither blocks nor is asynchronous</b>, and it must stay that way. Every mutator in this file
    /// and in all three derived proxies calls it first and returns <see cref="RetCode.E_BUSY"/> on
    /// <see langword="true"/> [<c>n_cst_threading_task_sqlbase.sru:L57, :L73, :L95, :L110, :L124, :L144,
    /// :L162, :L179, :L188</c>], so a blocking implementation would turn every guard into a stall.
    /// </para>
    /// <para>
    /// <b>Public because the oracle declares it public</b> - <c>public function boolean of_isbusy()</c>
    /// [<c>n_cst_threading_task.sru:L135, :L305</c>] - and a caller genuinely needs to ask before
    /// attempting a mutation rather than interpreting a refusal after the fact.
    /// </para>
    /// </remarks>
    public bool IsBusy()
    {
        // [:L305] Not running: the answer is the controller's, in full.
        if (!_host.IsRunning)
        {
            return _host.IsControllerBusy;
        }

        // [:L306] Running: busy unless the sync signal is currently set. See the inversion note above.
        return !_host.IsSyncSignalSet;
    }

    #endregion

    #region The database-error sink [:L44] and its one sanctioned projection

    /// <summary>
    /// Receives a database error from the worker - the explicit implementation of the pair's boundary
    /// interface, whose single member exists because the oracle's proxy declares exactly one event:
    /// <c>event ondberror ( readonly dberrordata err )</c>
    /// [<c>n_cst_threading_task_sqlbase.sru:L9</c>].
    /// </summary>
    /// <param name="error">The five-member payload.</param>
    /// <remarks>
    /// Implemented EXPLICITLY and forwarded to <see cref="OnDbError"/> so that the overridable hook can be
    /// <see langword="protected"/> <see langword="virtual"/> - which it must be, because the update proxy
    /// overrides it and opens with <c>call super::ondberror</c>
    /// [<c>n_cst_threading_task_sqlupdate.sru:L310</c>]. An implicit implementation would have to be
    /// public, widening the boundary for no reason.
    /// </remarks>
    void ISqlTaskProxy.OnDbError(in DbErrorData error) => OnDbError(in error);

    /// <summary>
    /// Latches a database error - the port of <c>event ondberror</c>
    /// [<c>n_cst_threading_task_sqlbase.sru:L44</c>], whose entire body is the single statement
    /// <c>_lastDBError = err</c>.
    /// </summary>
    /// <param name="error">
    /// The five-member payload. Taken by <see langword="in"/> because the legacy parameter is
    /// <c>readonly</c>, which is this migration's fixed mapping for PowerBuilder's <c>readonly</c>.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>LAST-ERROR-WINS, NEVER ACCUMULATED (C-B).</b> The oracle assigns; it does not append, aggregate,
    /// or protect a first error from being overwritten. There is deliberately no list here, no "first
    /// error is the real one" rule and no count. A second error wholly replaces the first, and a caller
    /// that wants every error must observe each one as it arrives.
    /// </para>
    /// <para>
    /// <b>The asymmetry with the worker's own event is real and worth knowing.</b> The worker declares
    /// <c>ondberror(long sqldbcode, string sqlerrtext, string sqlsyntax, dwbuffer buffer, long row)</c> -
    /// FIVE SCALARS - packs them into a structure, forwards the whole structure to this proxy and returns
    /// the literal 3 [<c>n_cst_thread_task_sqlbase.sru:L85-L98</c>]. So the worker side is
    /// scalar-shaped and this side is record-shaped, which is why <c>Buffers/DataWindowBuffers.cs</c>
    /// declares a five-scalar delegate while this member receives the assembled record.
    /// </para>
    /// <para>
    /// <b>C-F applies directly here.</b> The payload's <see cref="DbErrorData.SqlSyntax"/> carries the
    /// fully generated statement INCLUDING INTERPOLATED LITERALS, and the legacy logger performs no
    /// redaction at all. This method therefore only stores it: it does not log it, does not render it,
    /// and does not emit it. The single sanctioned way it leaves the process is
    /// <see cref="GetLastDbErrorForWire"/>.
    /// </para>
    /// <para>
    /// <b>No busy guard, deliberately.</b> The oracle's event body has none, and adding one would drop
    /// errors precisely when the task is working - which is when they occur.
    /// </para>
    /// </remarks>
    protected virtual void OnDbError(in DbErrorData error)
    {
        // [:L44] `_lastDBError = err` - the whole body. A bare assignment, reproduced as a bare
        // assignment.
        LastDbError = error;
    }

    /// <summary>
    /// Reads the latched database error - the port of <c>of_getlastdberrordata</c>
    /// [<c>n_cst_threading_task_sqlbase.sru:L47</c>].
    /// </summary>
    /// <returns>
    /// The latched payload, or <see cref="DbErrorData.Empty"/> when nothing has been latched since the
    /// last reset or prepare.
    /// </returns>
    /// <remarks>
    /// <b>A plain accessor with NO busy guard, and none may be added.</b> The oracle's body is
    /// <c>return _lastDBError</c> and nothing else. Guarding a read would make the error unreadable
    /// exactly while the task is running, which is when a caller needs it.
    /// </remarks>
    public DbErrorData GetLastDbErrorData() => LastDbError;

    /// <summary>
    /// Projects the latched error onto its wire form - <b>the ONE sanctioned way a database error leaves
    /// this process (C-F)</b>.
    /// </summary>
    /// <returns>
    /// A <see cref="DbError"/> whose statement member has been masked and whose other four members are
    /// carried through unchanged, including the ONE-BASED row ordinal and the buffer.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The projection is <c>DbErrorData.ToDbError()</c> in <c>Errors/SqlRedactor.cs</c>, which applies a
    /// SEALED redaction policy itself rather than accepting one: it has no enabled flag, no configuration
    /// key and no pass-through mode, so there is no argument a caller could pass that would let the
    /// statement text through. This member exists so that the gRPC layer has an obvious correct door and
    /// no reason to reach for <see cref="GetLastDbErrorData"/> and build a message by hand.
    /// </para>
    /// <para>
    /// The row ordinal is NOT rebased. It is one-based legacy contract, and the buffer is copied as-is
    /// including the filter buffer whose row order is inverted relative to the source - neither is an
    /// off-by-one to normalise (R9).
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <b>NO ws_objects LOCATOR, because there is no legacy equivalent to cite.</b> The legacy is a
    /// library with no process boundary, so every wire contract in this migration is a net-new artifact
    /// and this projection has no oracle behind it. What the oracle DOES supply is the payload being
    /// projected - the five-member structure at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/dberrordata.srs:L4-L8</c> - and the field order is preserved
    /// from it.
    /// </remarks>
    public DbError GetLastDbErrorForWire() => LastDbError.ToDbError();

    #endregion

    #region The TWO-SIDED reset [n_cst_threading_task_sqlbase.sru:L53-L68] - why the pair is not fused

    /// <summary>
    /// Resets both sides of the pair - the port of <c>of_reset</c>
    /// [<c>n_cst_threading_task_sqlbase.sru:L53-L68</c>].
    /// </summary>
    /// <returns>
    /// <see cref="RetCode.E_BUSY"/> when the task is busy [<c>:L57</c>]; the WORKER's failing code
    /// verbatim when the worker refuses [<c>:L62</c>]; otherwise <see cref="RetCode.OK"/> [<c>:L67</c>].
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>THE ORDER IS THE CONTRACT, AND THIS METHOD IS THE PROOF THAT THE PAIR CANNOT BE FUSED.</b>
    /// Busy guard, then DELEGATE to the worker, then bail on the worker's refusal, and only THEN clear
    /// caller-side state. Two distinct state sets, a delegation between them, and a failure mode in
    /// which the worker's refusal leaves this side UNTOUCHED - which a single fused method, having one
    /// state set, could not have at all.
    /// </para>
    /// <para>
    /// <b>ORDERING ASYMMETRY TO PRESERVE (C-B).</b> This method DELEGATES THEN CLEARS, whereas
    /// <see cref="ResetParams"/> CLEARS THEN DELEGATES [<c>:L161-L169</c>]. The two are deliberately
    /// opposite in the oracle and the difference is observable: a worker refusal leaves the parameter
    /// flag set here and clears it there. Neither order is tidied to match the other.
    /// </para>
    /// <para>
    /// <b>What it does NOT clear.</b> <see cref="TransDataInstalled"/> is untouched - see that member for
    /// the preserved defect.
    /// </para>
    /// <para>
    /// <b>Why the worker must be a substitutable seam (C-H).</b> The worker BASE's own reset always
    /// succeeds: it resets the commit signal, resets its parameters and returns
    /// <see cref="RetCode.OK"/> unconditionally [<c>n_cst_thread_task_sqlbase.sru:L242-L248</c>]. The
    /// refusal arm at <c>:L62</c> is therefore reachable only through a DERIVED worker's override, each of
    /// which guards on its own running flag. Testing it at all requires substituting the worker, which is
    /// why substitutability here is mandatory rather than convenient.
    /// </para>
    /// <para>
    /// <b>Virtual because both the query and the update proxy override it and chain to it</b>
    /// [<c>n_cst_threading_task_sqlquery.sru:L282, :L284</c>;
    /// <c>n_cst_threading_task_sqlupdate.sru:L82, :L85</c>]. Note that <c>of_reset</c> does not exist on
    /// the framework base at all, so their <c>super::of_Reset()</c> resolves HERE. An override must call
    /// <c>base.Reset()</c> first and must respect its result.
    /// </para>
    /// </remarks>
    public virtual long Reset()
    {
        // [:L57] guards on its OWN busy state.
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

        // [:L59] `task = _Task`
        SqlTaskBase task = RequireWorkerTask();

        // [:L61] DELEGATES the reset to the WORKER.
        long rtCode = task.Reset();

        // [:L62] ABANDONS if the worker refused, returning the worker's code VERBATIM rather than a code
        // of this method's own. Predicates.IsFailed rather than a hand-rolled `< 0`, so the tri-state
        // boundary is inherited rather than re-derived: CANCELLED is explicitly EXCLUDED from failure, so
        // a cancelled worker does NOT take this arm and the clear below still happens.
        if (Predicates.IsFailed(rtCode))
        {
            return rtCode;
        }

        // [:L64] only THEN clears its own caller-side state.
        ParamsInstalled = false;

        // [:L65] `_lastDBError = emptyData`
        LastDbError = DbErrorData.Empty;

        // [:L67] NOT `rtCode`. The oracle returns OK even where the worker answered a non-failing
        // non-zero code such as PREVENT, which IsFailed does not treat as a failure. Reproduced exactly.
        return RetCode.OK;
    }

    #endregion

    #region The FOUR transaction-setting overloads [:L70-L133] - three distinct field counts

    /// <summary>
    /// Installs a connection from a live transaction object, copying EIGHT of its nine members - the port
    /// of <c>of_settransobject(readonly transaction trans)</c>
    /// [<c>n_cst_threading_task_sqlbase.sru:L70-L91</c>].
    /// </summary>
    /// <param name="trans">The transaction object to copy from.</param>
    /// <returns>
    /// <see cref="RetCode.E_BUSY"/> when busy [<c>:L73</c>]; <see cref="RetCode.E_INVALID_OBJECT"/>
    /// when <paramref name="trans"/> is not a valid object [<c>:L74</c>]; otherwise whatever the
    /// whole-descriptor overload answers [<c>:L90</c>].
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>EXACTLY EIGHT FIELDS, IN THE ORACLE'S OWN ORDER</b> [<c>:L76-L83</c>]: DBMS, server, database,
    /// login, password, connection parameters, isolation level, auto-commit.
    /// <see cref="ISqlTransactionObject.UserParm"/> IS DELIBERATELY NOT COPIED - the oracle's copy block
    /// simply has no line for it, so the descriptor built here always leaves its own user parameter at the
    /// cleared state a freshly declared <c>TRANSACTIONDATA</c> local has. Reproduced, not completed (C-B).
    /// </para>
    /// <para>
    /// <b>THE EIGHT-FIELD COPY IS NOT <c>WithConnectionFieldsFrom</c>, AND SUBSTITUTING IT WOULD BE A
    /// BEHAVIOURAL CHANGE.</b> <see cref="TransactionData"/> publishes a SEVEN-field connection transfer
    /// that deliberately excludes BOTH auto-commit and the user parameter, because those are session
    /// state rather than connection identity and the pool keys its entries on identity. That is a
    /// DIFFERENT operation belonging to the pool side. This site has its own count - eight - and the
    /// six-string overload has a third - six. The three are distinct on purpose and are written out
    /// explicitly so a reader can count them.
    /// </para>
    /// <para>
    /// <b>PRESERVED LEGACY ODDITY - THE AUTO-COMMIT FLAG IS COPIED HERE AND ERASED ON ARRIVAL (C-B).</b>
    /// This method hands over auto-commit at <c>:L83</c>, and the worker's <c>of_settransdata</c> assigns
    /// <c>_transData.AutoCommit = false</c> the instant it receives the descriptor, under the comment
    /// 擦除连接目标无关的参数 - "erase parameters irrelevant to the connection target"
    /// [<c>n_cst_thread_task_sqlbase.sru:L118-L119</c>]. So the value travels one hop and is discarded.
    /// <b>The copy is NOT optimised away.</b> It looks like dead work and it is not: a future reader must
    /// be able to see that the legacy copied it and that the legacy discarded it, because deleting either
    /// half would make the pair unintelligible and would silently change what a caller who inspects the
    /// descriptor in between observes.
    /// </para>
    /// <para>
    /// <b>C-F.</b> The credential is read through the named door
    /// <see cref="ISqlTransactionObject.RevealLogPassForTransfer"/> - one of only two sites in this file
    /// that touch it - and is written straight into <see cref="TransactionData.LogPass"/>, which has no
    /// getter at all. It is never logged, never interpolated, never rendered and never returned.
    /// </para>
    /// <para>
    /// <b>The object is retained ONLY on success</b> [<c>:L86-L88</c>], and the test is
    /// <see cref="Predicates.IsSucceeded(long?)"/> rather than an equality against zero - so a PREVENT
    /// answer, which that predicate treats as a success, DOES retain the object. That is the tri-state
    /// boundary being inherited rather than re-derived.
    /// </para>
    /// </remarks>
    public long SetTransObject(ISqlTransactionObject trans)
    {
        // [:L73]
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

        // [:L74] `if Not IsValidObject(trans) then return RetCode.E_INVALID_OBJECT`
        if (!Predicates.IsValidObject(trans))
        {
            return RetCode.E_INVALID_OBJECT;
        }

        // [:L71] `TRANSACTIONDATA transData` - a freshly declared local, so every member this block does
        // not assign stays at its cleared state. That is what leaves UserParm untouched.
        TransactionData transData = new()
        {
            Dbms = trans.Dbms,                              // 1 [:L76]
            ServerName = trans.ServerName,                  // 2 [:L77]
            Database = trans.Database,                      // 3 [:L78]
            LogId = trans.LogId,                            // 4 [:L79]
            LogPass = trans.RevealLogPassForTransfer(),      // 5 [:L80] - the named door, see remarks
            DbParm = trans.DbParm,                          // 6 [:L81]
            Lock = trans.Lock,                              // 7 [:L82]

            // 8 [:L83] - COPIED HERE, ERASED BY THE WORKER ON ARRIVAL
            // [n_cst_thread_task_sqlbase.sru:L118-L119]. Both halves are behaviour; see remarks.
            AutoCommit = trans.AutoCommit,

            // UserParm - NO LINE, because the oracle has no line. Do not add one.
        };

        // [:L85]
        long rtCode = SetTransData(in transData);

        // [:L86-L88]
        if (Predicates.IsSucceeded(rtCode))
        {
            RetainedTransaction = trans;
        }

        // [:L90]
        return rtCode;
    }

    /// <summary>
    /// Installs a connection from SIX connection strings - the port of
    /// <c>of_settransdata(readonly string dbms, readonly string servername, readonly string database,
    /// readonly string logid, readonly string logpass, readonly string dbparm)</c>
    /// [<c>n_cst_threading_task_sqlbase.sru:L93-L105</c>].
    /// </summary>
    /// <param name="dbms">The DBMS identifier [<c>:L97</c>].</param>
    /// <param name="serverName">The server name [<c>:L98</c>].</param>
    /// <param name="database">The database name [<c>:L99</c>].</param>
    /// <param name="logId">The login identifier [<c>:L100</c>].</param>
    /// <param name="logPass">
    /// The credential [<c>:L101</c>]. <b>C-F: WRITE-ONLY.</b> It is passed straight into
    /// <see cref="TransactionData.LogPass"/>, which has no getter, and this method neither logs it,
    /// stores it anywhere else, nor returns it. No default, no placeholder and no example value for it
    /// appears anywhere in this file.
    /// </param>
    /// <param name="dbParm">The connection parameter string [<c>:L102</c>].</param>
    /// <returns>
    /// <see cref="RetCode.E_BUSY"/> when busy [<c>:L95</c>], otherwise whatever the whole-descriptor
    /// overload answers [<c>:L104</c>].
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>EXACTLY SIX FIELDS - the second of this file's three distinct counts.</b> No isolation level, no
    /// auto-commit and no user parameter, so a descriptor installed this way differs from an eight-field
    /// one in more than the two members the strings cannot carry. That is the oracle's own shape
    /// [<c>:L97-L102</c>] and is not harmonised with the eight-field copy.
    /// </para>
    /// <para>
    /// <b>It does NOT retain a transaction object</b>, because there is none to retain - which is why
    /// <see cref="GetTransObject"/> still answers <see langword="null"/> after a successful call here.
    /// </para>
    /// <para>
    /// The parameters accept <see langword="null"/> because <see cref="TransactionData"/>'s string members
    /// do: they read back non-null and accept null on assignment, so a caller may pass an unset
    /// configuration value without pre-coercing it.
    /// </para>
    /// </remarks>
    public long SetTransData(
        string? dbms,
        string? serverName,
        string? database,
        string? logId,
        string? logPass,
        string? dbParm)
    {
        // [:L95]
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

        // [:L93] `TRANSACTIONDATA transData` - a freshly declared local again.
        TransactionData transData = new()
        {
            Dbms = dbms,               // 1 [:L97]
            ServerName = serverName,   // 2 [:L98]
            Database = database,       // 3 [:L99]
            LogId = logId,             // 4 [:L100]
            LogPass = logPass,         // 5 [:L101] - C-F: write-only, see the parameter remarks
            DbParm = dbParm,           // 6 [:L102]

            // Lock, AutoCommit and UserParm - NO LINES. Six, not eight and not nine.
        };

        // [:L104]
        return SetTransData(in transData);
    }

    /// <summary>
    /// Installs a connection descriptor on the worker - the port of
    /// <c>of_settransdata(readonly transactiondata transdata)</c>
    /// [<c>n_cst_threading_task_sqlbase.sru:L107-L120</c>], and the funnel the other three overloads all
    /// pass through.
    /// </summary>
    /// <param name="transData">
    /// The descriptor. Taken by <see langword="in"/> because the legacy parameter is <c>readonly</c>.
    /// </param>
    /// <returns>
    /// <see cref="RetCode.E_BUSY"/> when busy [<c>:L110</c>], otherwise the worker's result verbatim
    /// [<c>:L119</c>].
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>This is the ONLY writer of <see cref="TransDataInstalled"/> anywhere in the object</b>
    /// [<c>:L116</c>], and it never clears it - see that member for the preserved defect.
    /// </para>
    /// <para>
    /// <b>What the worker does on receipt matters to a reader of this call site.</b> It compares the whole
    /// descriptor against the STORED one and answers <see cref="RetCode.OK"/> without doing anything when
    /// they match [<c>n_cst_thread_task_sqlbase.sru:L114</c>]; it then stores it and immediately ERASES the
    /// auto-commit member [<c>:L118-L119</c>]; and a descriptor change drops any pooled reference already
    /// borrowed for the previous one and nulls the transaction reference [<c>:L121-L125</c>]. So a
    /// successful call here can invalidate a connection the worker had already borrowed, which is
    /// deliberate and is why the busy guard above matters.
    /// </para>
    /// <para>
    /// <b>The worker's own busy guard is commented out</b> [<c>:L113</c>] and is carried across inert, so
    /// the live guard for this operation is the one in this method and nowhere else.
    /// </para>
    /// </remarks>
    public long SetTransData(in TransactionData transData)
    {
        // [:L110]
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

        // [:L112] `task = _Task`
        SqlTaskBase task = RequireWorkerTask();

        // [:L114]
        long rtCode = task.SetTransData(in transData);

        // [:L115-L117] The SOLE writer of this flag. IsSucceeded rather than an equality against zero, so
        // a PREVENT answer also sets it - the tri-state boundary inherited, not re-derived.
        if (Predicates.IsSucceeded(rtCode))
        {
            TransDataInstalled = true;
        }

        // [:L119]
        return rtCode;
    }

    /// <summary>
    /// Installs a connection from a POOLED transaction object - the port of
    /// <c>of_settransobject(readonly n_cst_thread_trans trans)</c>
    /// [<c>n_cst_threading_task_sqlbase.sru:L122-L133</c>].
    /// </summary>
    /// <param name="trans">The pooled transaction object to read the descriptor from.</param>
    /// <returns>
    /// <see cref="RetCode.E_BUSY"/> when busy [<c>:L124</c>];
    /// <see cref="RetCode.E_INVALID_OBJECT"/> when <paramref name="trans"/> is not a valid object
    /// [<c>:L125</c>]; otherwise whatever the whole-descriptor overload answers [<c>:L132</c>].
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>A SEPARATE OVERLOAD RATHER THAN A TYPE TEST, because the oracle overloads too.</b>
    /// <c>n_cst_thread_trans</c> derives from the built-in <c>transaction</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L4, :L8</c>], so PowerBuilder resolves
    /// a pooled argument to this member and a plain one to the eight-field member. C# overload resolution
    /// prefers the more derived interface identically, so the distinction survives without a flag.
    /// </para>
    /// <para>
    /// <b>It copies NO fields itself</b> - it delegates the whole descriptor read to the pooled object
    /// [<c>:L127</c>], which is the third of this file's three shapes and the only one with no field list.
    /// The ported accessor moves SIX fields outbound rather than the oracle's seven, deliberately omitting
    /// the credential, so a pooled install leaves whatever password the worker already held in place. That
    /// omission is C-F and is recorded in <c>Transactions/TransactionData.cs</c> rather than re-argued here.
    /// </para>
    /// <para>
    /// <b>The object is retained ONLY on success</b> [<c>:L128-L130</c>]. In the oracle this assignment
    /// stores an <c>n_cst_thread_trans</c> into a field declared <c>Transaction _Trans</c> [<c>:L16</c>],
    /// which is legal only because of the inheritance above - and it is why
    /// <see cref="RetainedTransaction"/> is typed as the BASE abstraction here rather than the pooled one.
    /// </para>
    /// </remarks>
    public long SetTransObject(IPooledSqlTransactionObject trans)
    {
        // [:L124]
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

        // [:L125]
        if (!Predicates.IsValidObject(trans))
        {
            return RetCode.E_INVALID_OBJECT;
        }

        // [:L127] `of_SetTransData(trans.of_GetTransData())` - the descriptor read is the pooled object's,
        // in full, and this method contributes no field of its own.
        TransactionData transData = trans.GetTransData();
        long rtCode = SetTransData(in transData);

        // [:L128-L130]
        if (Predicates.IsSucceeded(rtCode))
        {
            RetainedTransaction = trans;
        }

        // [:L132]
        return rtCode;
    }

    /// <summary>
    /// Reads the retained transaction object - the port of <c>of_gettransobject</c>
    /// [<c>n_cst_threading_task_sqlbase.sru:L50</c>].
    /// </summary>
    /// <returns>
    /// The object a successful object-overload install was handed, or <see langword="null"/> when none
    /// has succeeded.
    /// </returns>
    /// <remarks>
    /// <b>A plain accessor with NO busy guard, and none may be added.</b> The oracle's body is
    /// <c>return _Trans</c> and nothing else.
    /// </remarks>
    public ISqlTransactionObject? GetTransObject() => RetainedTransaction;

    /// <summary>
    /// Whether a connection descriptor has been installed - the port of <c>of_hastransdata</c>
    /// [<c>n_cst_threading_task_sqlbase.sru:L135</c>].
    /// </summary>
    /// <returns><see langword="true"/> once any of the four setters has succeeded.</returns>
    /// <remarks>
    /// <b>A plain accessor with NO busy guard.</b> Its answer is STICKY - see
    /// <see cref="TransDataInstalled"/> for the preserved defect that makes it so.
    /// </remarks>
    public bool HasTransData() => TransDataInstalled;

    #endregion

    #region SQL parameters [:L138-L172] - and the DORMANT check that stays dormant

    /// <summary>
    /// Adds an unnamed SQL parameter - the port of <c>of_addparam(readonly any param)</c>
    /// [<c>n_cst_threading_task_sqlbase.sru:L138</c>], whose whole body is
    /// <c>return of_AddParam("", param)</c>.
    /// </summary>
    /// <param name="param">
    /// The value. <c>any</c> maps to <see cref="object"/>? by this migration's fixed type mapping, so
    /// <see langword="null"/> is representable - and, per the dormant check on the named overload, is
    /// ACCEPTED.
    /// </param>
    /// <returns>Whatever the named overload answers.</returns>
    /// <remarks>
    /// <b>NO BUSY GUARD OF ITS OWN, deliberately (C-B).</b> The oracle's body is a single delegating
    /// statement and it INHERITS the guard through the overload it calls. Adding one here would be
    /// harmless in outcome and wrong in kind: it would put a second guard where the oracle has one, and
    /// the convenience overloads that carry no guard are a pattern this object repeats three times -
    /// here, at the parameterless commit [<c>:L195</c>], and on the framework parent's two
    /// prevent-event arities [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L604, :L611</c>].
    /// </remarks>
    public long AddParam(object? param) => AddParam(string.Empty, param);

    /// <summary>
    /// Adds a named SQL parameter - the port of
    /// <c>of_addparam(readonly string name, readonly any value)</c>
    /// [<c>n_cst_threading_task_sqlbase.sru:L141-L159</c>].
    /// </summary>
    /// <param name="name">
    /// The parameter name, or <see cref="string.Empty"/> for a positional one. The worker lower-cases it
    /// on receipt [<c>n_cst_thread_task_sqlbase.sru:L256</c>], so casing here is not significant.
    /// </param>
    /// <param name="value">The value.</param>
    /// <returns>
    /// <see cref="RetCode.E_BUSY"/> when busy [<c>:L144</c>];
    /// <see cref="RetCode.E_INVALID_ARGUMENT"/> for a rank-2-or-higher array [<c>:L148</c>] or an empty
    /// rank-1 array [<c>:L149</c>]; otherwise the worker's result verbatim [<c>:L158</c>].
    /// </returns>
    /// <remarks>
    /// <para>
    /// The oracle prefaces its validation with 参数只支持简单类型或简单类型的一维数组 [<c>:L146</c>] -
    /// "parameters support only simple types or one-dimensional arrays of simple types" - and then
    /// enforces exactly two of the three things that sentence implies. The element-type half is NOT
    /// enforced anywhere, which is why no element-type check appears here either.
    /// </para>
    /// <para>
    /// <b>A NULL VALUE IS ACCEPTED, AND THAT IS THE POINT OF THE DORMANT CHECK (C-B).</b> Line
    /// <c>:L147</c> carries a COMMENTED-OUT null rejection, immediately above the two live checks. It is a
    /// dormant validation path: carried across inert below, with its locator, exactly as this migration
    /// treats the commented-out byte-length check in the DataWindow service layer. <b>Reviving it would be
    /// the silent correction C-B forbids</b> - it would start rejecting calls the legacy accepts, and a
    /// null SQL parameter is a legitimate value that reaches the worker today.
    /// </para>
    /// <para>
    /// <b>The flag is set only on success</b> [<c>:L154-L156</c>], through
    /// <see cref="Predicates.IsSucceeded(long?)"/>, so a PREVENT answer also sets it.
    /// </para>
    /// </remarks>
    public long AddParam(string? name, object? value)
    {
        // [:L144]
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

        // [:L146] 参数只支持简单类型或简单类型的一维数组
        //          "parameters support only simple types or one-dimensional arrays of simple types"
        //
        // [:L147] DORMANT VALIDATION PATH, CARRIED ACROSS INERT AND NOT TO BE REVIVED (C-B).
        //         The oracle's line reads, commented out:
        //             //if IsNull(value) then return RetCode.E_INVALID_ARGUMENT
        //         so a null value is ACCEPTED and is forwarded to the worker. Reviving this would reject
        //         calls the legacy accepts, which is a silent behavioural change. There is a pinning test
        //         asserting a null value is accepted, precisely so this line cannot be quietly revived.
        //
        //         if (value is null) { return RetCode.E_INVALID_ARGUMENT; }
        if (value is Array array)
        {
            // [:L148] `if UpperBound(value,2) >= 0 then return RetCode.E_INVALID_ARGUMENT`
            // A second dimension exists, so the value is an array of rank 2 or higher. PowerBuilder has no
            // jagged arrays, so rank is the whole of the question.
            if (array.Rank >= 2)
            {
                return RetCode.E_INVALID_ARGUMENT;
            }

            // [:L149] `if UpperBound(value,1) = 0 then return RetCode.E_INVALID_ARGUMENT`
            // A one-based upper bound of 0 is PowerBuilder's empty variable-size array.
            if (array.Length == 0)
            {
                return RetCode.E_INVALID_ARGUMENT;
            }
        }

        // A scalar reaches neither check and is accepted, which is what the oracle's two UpperBound tests
        // do for a non-array value.

        // [:L151] `task = _Task`
        SqlTaskBase task = RequireWorkerTask();

        // [:L153]
        long rtCode = task.AddParam(name, value);

        // [:L154-L156]
        if (Predicates.IsSucceeded(rtCode))
        {
            ParamsInstalled = true;
        }

        // [:L158]
        return rtCode;
    }

    /// <summary>
    /// Clears every SQL parameter - the port of <c>of_resetparams</c>
    /// [<c>n_cst_threading_task_sqlbase.sru:L161-L169</c>].
    /// </summary>
    /// <returns>
    /// <see cref="RetCode.E_BUSY"/> when busy [<c>:L162</c>], otherwise the worker's result verbatim
    /// [<c>:L168</c>].
    /// </returns>
    /// <remarks>
    /// <b>ORDERING ASYMMETRY TO PRESERVE (C-B).</b> This method CLEARS THEN DELEGATES [<c>:L164</c> before
    /// <c>:L168</c>], whereas <see cref="Reset"/> DELEGATES THEN CLEARS [<c>:L61</c> before <c>:L64</c>].
    /// The two are deliberately opposite in the oracle and the difference is OBSERVABLE: if the worker's
    /// delegated call fails, this method has ALREADY cleared the flag and returns the failure with the
    /// flag down, while <see cref="Reset"/> returns the failure with the flag still up. Neither order is
    /// changed to match the other, and there is a test for each direction.
    /// </remarks>
    public long ResetParams()
    {
        // [:L162]
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

        // [:L164] CLEARED FIRST - before the delegation, and therefore even if the delegation then fails.
        // This is the opposite order to Reset; see the remarks.
        ParamsInstalled = false;

        // [:L166] `task = _Task`
        SqlTaskBase task = RequireWorkerTask();

        // [:L168] the worker's result, returned verbatim and not rewritten to OK.
        return task.ResetParams();
    }

    /// <summary>
    /// Whether at least one SQL parameter has been added - the port of <c>of_hasparams</c>
    /// [<c>n_cst_threading_task_sqlbase.sru:L171</c>].
    /// </summary>
    /// <returns><see langword="true"/> when a parameter add has succeeded since the last clear.</returns>
    /// <remarks>
    /// <para>
    /// <b>A plain accessor with NO busy guard.</b>
    /// </para>
    /// <para>
    /// <b>It answers from CALLER-SIDE state, not from the worker's collection</b>, and the distinction is
    /// real: the worker has its own <c>of_hasparams</c> that counts its parameter array
    /// [<c>n_cst_thread_task_sqlbase.sru:L368</c>]. The two can disagree - a worker-side clear that this
    /// proxy did not initiate leaves this flag set - and the oracle keeps both. Reproduced.
    /// </para>
    /// </remarks>
    public bool HasParams() => ParamsInstalled;

    #endregion

    #region Commit and rollback [:L174-L196]

    /// <summary>
    /// Whether the worker's transaction has committed - the port of <c>of_iscommitted</c>
    /// [<c>n_cst_threading_task_sqlbase.sru:L174</c>], whose body is
    /// <c>return (WaitForSingleObject(_hEvtCommitted,0) = 0)</c> with the inline comment
    /// <c>//WAIT_OBJECT_0</c>.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> once the worker has signalled a commit; <see langword="false"/> before that,
    /// and <see langword="false"/> when no signal has been obtained yet.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>A ZERO-TIMEOUT, NON-BLOCKING POLL. It neither blocks nor is asynchronous, and it must stay that
    /// way</b> - the oracle passes a zero timeout precisely so that asking never waits.
    /// <see cref="ManualResetEventSlim.Wait(int)"/> with a zero timeout is the literal equivalent of the
    /// legacy call, which is why it is used in preference to reading
    /// <see cref="ManualResetEventSlim.IsSet"/>.
    /// </para>
    /// <para>
    /// <b>Why a zero-timeout poll is meaningful at all: the signal is MANUAL-RESET.</b> The worker creates
    /// it lazily with <c>CreateEvent(0, true, false, 0)</c> - manual-reset, initially unsignalled -
    /// [<c>n_cst_thread_task_sqlbase.sru:L224-L226</c>]. An AUTO-reset signal would be CONSUMED by the very
    /// poll that observed it, so the first call would answer true and every later call false; manual-reset
    /// makes the answer stable and repeatable, which is what a predicate has to be. There is a test
    /// asserting a second poll still answers true.
    /// </para>
    /// <para>
    /// <b>A missing signal answers <see langword="false"/>, matching the oracle.</b> Before init the legacy
    /// handle is 0 and <c>WaitForSingleObject(0, 0)</c> does not return <c>WAIT_OBJECT_0</c>, so the legacy
    /// answer is false too. <b>No busy guard</b>, and the signal is never created here - the worker owns it.
    /// </para>
    /// </remarks>
    public bool IsCommitted() => _committedSignal?.Wait(0) == true;

    /// <summary>
    /// Rolls the worker's transaction back - the port of <c>of_rollback</c>
    /// [<c>n_cst_threading_task_sqlbase.sru:L177-L184</c>].
    /// </summary>
    /// <returns>
    /// <see cref="RetCode.E_BUSY"/> when busy [<c>:L179</c>], otherwise the worker's result verbatim
    /// [<c>:L183</c>] - which is <see cref="RetCode.E_INVALID_TRANSACTION"/> when no transaction is
    /// attached [<c>n_cst_thread_task_sqlbase.sru:L219</c>].
    /// </returns>
    public long Rollback()
    {
        // [:L179]
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

        // [:L181-L183] `task = _Task` then `return task.of_Rollback()`
        return RequireWorkerTask().Rollback();
    }

    /// <summary>
    /// Commits the worker's transaction - the port of
    /// <c>of_commit(readonly boolean autorollback)</c>
    /// [<c>n_cst_threading_task_sqlbase.sru:L186-L193</c>].
    /// </summary>
    /// <param name="autoRollback">
    /// Whether a failed commit rolls back automatically. Forwarded verbatim; the policy belongs to the
    /// worker and this side takes no view of it.
    /// </param>
    /// <returns>
    /// <see cref="RetCode.E_BUSY"/> when busy [<c>:L188</c>], otherwise the worker's result verbatim
    /// [<c>:L192</c>].
    /// </returns>
    /// <remarks>
    /// A successful worker commit raises the commit signal for this task AND for every preceding one -
    /// see the note on <see cref="RaiseNotify"/> about that handler's reverse loop, which is why
    /// <see cref="IsCommitted"/> can become true without this proxy having called anything.
    /// </remarks>
    public long Commit(bool autoRollback)
    {
        // [:L188]
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

        // [:L190-L192] `task = _Task` then `return task.of_Commit(autoRollback)`
        return RequireWorkerTask().Commit(autoRollback);
    }

    /// <summary>
    /// Commits the worker's transaction with automatic rollback - the port of the parameterless
    /// <c>of_commit</c> [<c>n_cst_threading_task_sqlbase.sru:L195</c>], whose whole body is
    /// <c>return of_Commit(true)</c>.
    /// </summary>
    /// <returns>Whatever <see cref="Commit(bool)"/> answers.</returns>
    /// <remarks>
    /// <b>The flag defaults to <see langword="true"/>, and NO BUSY GUARD OF ITS OWN (C-B).</b> The oracle's
    /// body is a single delegating statement that inherits the guard through the overload it calls - the
    /// same pattern as the unnamed parameter add [<c>:L138</c>].
    /// </remarks>
    public long Commit() => Commit(true);

    #endregion

    #region Lifecycle hooks [:L206-L221] - and the ancestor-guard asymmetry between them

    /// <summary>
    /// Prepares the task for a run - the port of <c>event onprepare</c>
    /// [<c>n_cst_threading_task_sqlbase.sru:L206-L211</c>].
    /// </summary>
    /// <returns>
    /// The literal <c>0</c>, which the oracle annotates <c>//continue</c> [<c>:L210</c>].
    /// </returns>
    /// <remarks>
    /// <para>
    /// The ancestor runs FIRST - <c>call super::onprepare</c> [<c>:L206</c>] - then the latched error is
    /// cleared [<c>:L208</c>], then the continue value is returned. So every run starts with a clean error
    /// latch even without an intervening <see cref="Reset"/>.
    /// </para>
    /// <para>
    /// <b>PRESERVED ASYMMETRY: THE ANCESTOR'S ANSWER IS DISCARDED HERE, AND TESTED IN
    /// <see cref="OnInit"/> (C-B).</b> This handler calls the ancestor and then ignores what it said,
    /// unconditionally returning continue [<c>:L206, :L210</c>], whereas the init handler guards on the
    /// ancestor's answer and returns it verbatim when it is not OK [<c>:L215</c>]. An ancestor prepare that
    /// wanted to prevent the run is therefore OVERRULED at this level. Reproduced, not corrected: adding a
    /// guard here would start refusing runs the legacy performs. The derived query proxy DOES guard in its
    /// own prepare [<c>n_cst_threading_task_sqlquery.sru:L503</c>], so the behaviour differs by level and
    /// that difference is the oracle's.
    /// </para>
    /// <para>
    /// <b>What it does NOT clear.</b> Neither <see cref="TransDataInstalled"/> nor
    /// <see cref="ParamsInstalled"/> is touched - parameters and the installed descriptor deliberately
    /// survive into the run they were set up for.
    /// </para>
    /// <para>
    /// Virtual because the derived proxies override it and chain to it
    /// [<c>n_cst_threading_task_sqlquery.sru:L503</c>, <c>n_cst_threading_task_sqlupdate.sru:L291</c>]. An
    /// override must call <c>base.OnPrepare()</c> first.
    /// </para>
    /// </remarks>
    protected virtual long OnPrepare()
    {
        // [:L206] `call super::onprepare` - and the answer is DELIBERATELY DISCARDED. The discard is the
        // oracle's; see the remarks. It is written as an explicit discard rather than an unassigned call so
        // that a reader can see the result was considered and dropped, not overlooked.
        _ = _host.OnPrepare();

        // [:L208] `_lastDBError = emptyData`
        LastDbError = DbErrorData.Empty;

        // [:L210] `return 0 //continue`
        //
        // The literal 0, spelled as the oracle spells it. Note the SPELLING INCONSISTENCY the oracle
        // carries and this port preserves: OnInit below tests the ancestor's answer against RetCode.OK
        // [:L215] while the derived proxies' prepare handlers test the ancestor's answer against the bare
        // literal 0 [n_cst_threading_task_sqlquery.sru:L503]. Same value, two spellings, and both are kept
        // at the sites that use them.
        return 0L;
    }

    /// <summary>
    /// The composition root's entry point into <see cref="OnPrepare"/> - the port of the substrate
    /// DISPATCHING <c>onprepare</c> on the CALLER side before the worker's body runs.
    /// </summary>
    /// <returns><see cref="OnPrepare"/>'s answer, which is the substrate's continue convention.</returns>
    /// <remarks>
    /// <para>
    /// <b>Why this exists at all.</b> <see cref="OnPrepare"/> is <see langword="protected"/> because the
    /// oracle declares it as an EVENT, and an event is raised by the substrate rather than called by a
    /// peer. This service has no PowerBuilder substrate, so whichever component dispatches the task plays
    /// that part, and this is the seam it raises the caller-side event through. It is the exact mirror of
    /// <c>Tasks/SqlTaskBase.RunPrepare</c>, which raises the WORKER-side event; the oracle raises both on
    /// every dispatch, so a port that reaches only one of them diverges.
    /// </para>
    /// <para>
    /// 🔴 <b>PER DISPATCH, AND WHAT WAS ACTUALLY MISREPORTED WITHOUT IT.</b> The caller-side prepare body
    /// clears the state that belongs to ONE run: the latched database error here in the base
    /// [<c>n_cst_threading_task_sqlbase.sru:L208</c>], and in the update proxy's override the three row
    /// counters and the per-table identity blocks [<c>n_cst_threading_task_sqlupdate.sru:L295-L298</c>].
    /// Those counters ACCUMULATE - <c>_nRowsInserted += inserted</c> [<c>:L66</c>] - because the oracle
    /// fires its count event once per updated TABLE and a total has to survive the loop. So without a
    /// per-dispatch raise a second update on the same task reported the SUM of both runs: one inserted row
    /// answered "2 inserted" and handed back two identity blocks for one table. A caller reading either
    /// number acts on it - the counts are how it learns what its payload did, and the identity blocks are
    /// how it learns which keys the database assigned - so the misreport is a data-integrity fault rather
    /// than a cosmetic one.
    /// </para>
    /// <para>
    /// <b>What it deliberately does NOT disturb, which is why raising it after the setters is safe.</b>
    /// Neither <see cref="TransDataInstalled"/> nor <see cref="ParamsInstalled"/> is touched, and the
    /// update proxy's override leaves the multi-table flag and the retained update object standing - only
    /// <see cref="Reset"/> clears those. Configuration installed for a run therefore survives INTO that
    /// run, which is the divergence between the two paths that <see cref="Reset"/> documents.
    /// </para>
    /// </remarks>
    internal long RunPrepare() => OnPrepare();

    /// <summary>
    /// Attaches the worker and borrows its commit signal - the port of <c>event oninit</c>
    /// [<c>n_cst_threading_task_sqlbase.sru:L213-L221</c>].
    /// </summary>
    /// <returns>
    /// The ancestor's answer verbatim when that answer is not <see cref="RetCode.OK"/> [<c>:L215</c>];
    /// otherwise <see cref="RetCode.OK"/> [<c>:L220</c>].
    /// </returns>
    /// <remarks>
    /// <para>
    /// The ancestor runs FIRST - <c>call super::oninit</c> [<c>:L213</c>] - which is what inserts the worker
    /// task by class name [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L188</c>]. Only then is
    /// the worker read [<c>:L217</c>] and its commit signal borrowed [<c>:L218</c>].
    /// </para>
    /// <para>
    /// <b>THE GUARD IS A LITERAL INEQUALITY, NOT A PREDICATE, AND THE DIFFERENCE IS OBSERVABLE.</b> The
    /// oracle writes <c>if AncestorReturnValue &lt;&gt; RetCode.OK then return AncestorReturnValue</c>
    /// [<c>:L215</c>] - so ANY non-zero answer aborts the init, including <see cref="RetCode.PREVENT"/>,
    /// which <see cref="Predicates.IsSucceeded(long?)"/> would have treated as a success. Using the
    /// predicate here would let a prevented init continue to attach a worker, so the inequality is
    /// reproduced exactly and deliberately is NOT routed through the tri-state predicates.
    /// </para>
    /// <para>
    /// <b>The commit signal is BORROWED, never created here.</b> The worker creates it lazily as
    /// manual-reset and initially unsignalled [<c>n_cst_thread_task_sqlbase.sru:L224-L226</c>] and therefore
    /// owns its disposal, which is why <see cref="Dispose(bool)"/> drops the reference without disposing the
    /// object. Manual-reset is exactly what makes <see cref="IsCommitted"/>'s zero-timeout poll meaningful.
    /// </para>
    /// <para>
    /// Virtual so a derived proxy can extend attachment. An override must call <c>base.OnInit()</c> first
    /// and must respect a non-OK result.
    /// </para>
    /// </remarks>
    protected virtual long OnInit()
    {
        // [:L213] `call super::oninit` - the ancestor inserts the worker by the class name this proxy
        // publishes, which is why WorkerTaskClassName is a required member rather than an optional one.
        long ancestorReturnValue = _host.OnInit(WorkerTaskClassName);

        // [:L215] A LITERAL inequality against RetCode.OK, NOT Predicates.IsSucceeded. PREVENT (1) aborts
        // here even though the predicate would call it a success; see the remarks.
        if (ancestorReturnValue != RetCode.OK)
        {
            return ancestorReturnValue;
        }

        // [:L217] `task = _Task`
        WorkerTask = _host.Task;

        // [:L218] `_hEvtCommitted = task.of_GetCommitEvent()` - BORROWED from the worker, which owns it.
        _committedSignal = RequireWorkerTask().GetCommitEvent();

        // [:L220]
        return RetCode.OK;
    }

    #endregion

    #region The inherited framework surface [n_cst_threading_task.sru] the SQL proxies consume

    /// <summary>
    /// Raises a notification - the port of <c>Event OnNotify(wparam, lparam, sparam)</c>
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L286-L298</c>], which the derived query
    /// proxy raises at <c>n_cst_threading_task_sqlquery.sru:L151, :L177, :L254, :L265</c> and <c>:L270</c>.
    /// </summary>
    /// <param name="wparam">The first numeric argument.</param>
    /// <param name="lparam">
    /// The second numeric argument. The query proxy packs a current/total pair into it as two 16-bit words
    /// [<c>n_cst_threading_task_sqlquery.sru:L254, :L265, :L270</c>].
    /// </param>
    /// <param name="text">The string argument.</param>
    /// <returns>
    /// The subscribers' answer, or <see langword="null"/> when the task is cancelled [<c>:L289</c>] or no
    /// subscriber answered. <b>Null is not zero here</b> - the oracle opens with <c>SetNull(rtCode)</c>
    /// [<c>:L288</c>] and returns that null on the cancelled path, and collapsing it to 0 would turn
    /// "cancelled, nobody was asked" into "somebody answered continue".
    /// </returns>
    /// <remarks>
    /// <para>
    /// The oracle's order is contract: null the result, return early if cancelled, RAISE the sync signal,
    /// fan out, CLEAR the sync signal, return [<c>:L288-L297</c>]. Raising the signal around the fan-out is
    /// what makes a mutator legal from inside a subscriber - see <see cref="IsBusy"/> for why that is a
    /// re-entrancy gate rather than an accident - so the raise and the clear are not optional bookkeeping.
    /// The clear runs in a <see langword="finally"/> so that a fault cannot leave the task permanently
    /// non-busy, which is the one place this port is deliberately stricter than the oracle's straight-line
    /// code: the oracle cannot throw where this can, and leaking a raised signal would disable the guard
    /// for the rest of the task's life.
    /// </para>
    /// <para>
    /// <b>R9, recorded here because a derived-file author reads this base first.</b> A successful worker
    /// commit signals THIS task and every PRECEDING one by walking a REVERSE one-based loop -
    /// <c>for nIndex = of_GetIndex() to 1 step -1</c> [<c>n_cst_thread_task_sqlbase.sru:L104</c>]. It runs
    /// backwards on purpose and <b>reverse iteration is NEVER to be "corrected" anywhere in this
    /// service</b>: the same hazard appears in the update path, where the filter buffer is walked backwards
    /// because its row order is inverted relative to the source, and "fixing" the direction there produces
    /// wrong data that still passes a row-count assertion. This file itself performs no index arithmetic and
    /// no positional access at all, and forwards the one-based task position verbatim through
    /// <see cref="GetIndex"/> without rebasing it.
    /// </para>
    /// </remarks>
    protected long? RaiseNotify(long wparam, long lparam, string text)
    {
        // [:L288] `SetNull(rtCode)` - the result starts NULL, not 0.
        // [:L289] `if of_IsCancelled() then return rtCode` - returns that null.
        if (IsCancelled())
        {
            return null;
        }

        // [:L291] `SetEvent(_hEvtSync)`
        _host.RaiseSyncSignal();
        try
        {
            // [:L293] `rtCode = _of_SendNotify(Enums.TNR_NOTIFY,wparam,lparam,sparam)`
            return SendNotify(Enums.TNR_NOTIFY, wparam, lparam, text);
        }
        finally
        {
            // [:L295] `ResetEvent(_hEvtSync)` - in a finally, see the remarks.
            _host.ClearSyncSignal();
        }
    }

    /// <summary>
    /// Fans a notification out to the local subscribers - the port of the framework parent's private
    /// <c>_of_sendnotify</c>
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L325-L367</c>].
    /// </summary>
    /// <param name="reason">
    /// One of <c>Enums.TNR_START</c>, <c>TNR_STOP</c>, <c>TNR_ERROR</c> or <c>TNR_NOTIFY</c>
    /// [<c>ws_objects/pfw.shared.pbl.src/enums.sru:L581-L584</c>]. An unrecognised reason reaches no
    /// per-reason channel, exactly as the oracle's <c>choose case</c> falls through without an else arm.
    /// </param>
    /// <param name="wparam">The first numeric argument.</param>
    /// <param name="lparam">The second numeric argument.</param>
    /// <param name="text">The string argument.</param>
    /// <returns>
    /// The catch-all channel's answer when that answer suppressed the per-reason channels; otherwise the
    /// per-reason channel's answer when it produced one; otherwise the catch-all's answer, which is
    /// <c>0</c> when nothing was subscribed at all.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The oracle's structure is reproduced statement for statement, and three details in it are easy to
    /// lose. <b>First</b>, the result starts at the PowerBuilder default <c>0</c> and is only overwritten
    /// when the catch-all channel is subscribed [<c>:L331-L333</c>], so an unsubscribed proxy answers 0
    /// rather than null. <b>Second</b>, the gate onto the per-reason channels is
    /// <c>(rtCode = 0 or IsNull(rtCode))</c> [<c>:L335</c>] - a non-zero non-null catch-all answer
    /// SUPPRESSES them entirely. <b>Third</b>, each per-reason arm re-tests cancellation independently
    /// [<c>:L339, :L343, :L349, :L353</c>], and the stop arm is the only one that fires WHEN cancelled -
    /// substituting <see cref="RetCode.CANCELLED"/> for the caller's exit code [<c>:L344</c>] rather than
    /// staying silent.
    /// </para>
    /// <para>
    /// <b>The validity re-tests are the oracle's, not defensiveness of ours.</b> <c>IsValid(this)</c> is
    /// checked at <c>:L335</c> and again at <c>:L362</c> because a subscriber may have destroyed the task
    /// during the fan-out. Disposal is the .NET equivalent of that condition, so both tests read the
    /// disposed flag.
    /// </para>
    /// <para>
    /// <b>Silence is saved, set and restored around the whole body</b> [<c>:L328-L329, :L363</c>]. See
    /// <see cref="TaskNotificationDispatcher.Silent"/> for what it suppresses and for the measured finding
    /// that the non-silent branch is unreachable from this type.
    /// </para>
    /// <para>
    /// <b>A SUBSCRIBER'S FAULT IS NOT ALWAYS ABSORBED, and this member does not catch.</b> The threading
    /// broker cancels the task and raises the exception signal for every subscriber fault, then absorbs
    /// the fault only when the task is currently free - the state <see cref="RaiseNotify"/> establishes
    /// around itself by raising the sync signal - and otherwise lets the base rethrow it
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_eventful.sru:L79-L88</c>]. The oracle's
    /// <c>_of_sendnotify</c> has no <c>catch</c> either, so a propagating fault leaves this member and
    /// reaches whoever published the notification. The silence restore is in a
    /// <see langword="finally"/> so that path cannot leave the surface permanently silent, and the
    /// captured-fault log runs only on the absorbing path because on the other one the exception is its
    /// own diagnostic.
    /// </para>
    /// </remarks>
    protected long? SendNotify(long reason, long wparam, long lparam, string text)
    {
        // [:L328-L329] save, then force silence for the whole body.
        bool wasSilent = Notifications.Silent;
        Notifications.Silent = true;

        // C-F-safe diagnostics: remember where this dispatch's absorbed faults begin so only NEW ones are
        // logged. Nothing logged below carries a statement or a credential.
        int capturedBefore = Notifications.CapturedExceptions.Count;

        // [:L325] `long rtCode` - PowerBuilder initialises it to 0, NOT to null. That default is what makes
        // the gate at :L335 pass for an unsubscribed proxy.
        long? rtCode = 0L;

        try
        {
            // [:L331-L333] the catch-all channel, and only when something is subscribed to it.
            if (Notifications.IsSubscribed(TaskEventName.CommonNotify))
            {
                rtCode = Notifications.TriggerCommon(reason, wparam, lparam, text);
            }

            // [:L335] a non-zero, non-null catch-all answer SUPPRESSES the per-reason channels.
            if ((rtCode is null || rtCode == 0L) && !_disposed)
            {
                // [:L336] `SetNull(nVal)`
                long? nVal = null;

                // [:L337-L356] the per-reason dispatch. No else arm, so an unrecognised reason reaches
                // nothing - reproduced by simply having no default case.
                if (reason == Enums.TNR_START)
                {
                    // [:L339-L341] fires only when NOT cancelled, and takes no arguments.
                    if (!IsCancelled())
                    {
                        nVal = Notifications.Trigger(TaskEventName.Start, 0L, 0L, string.Empty);
                    }
                }
                else if (reason == Enums.TNR_STOP)
                {
                    // [:L343-L347] THE ONLY ARM THAT FIRES WHEN CANCELLED, substituting CANCELLED for the
                    // caller's exit code [:L344] instead of staying silent.
                    nVal = IsCancelled()
                        ? Notifications.Trigger(TaskEventName.Stop, RetCode.CANCELLED, 0L, text)
                        : Notifications.Trigger(TaskEventName.Stop, wparam, 0L, text);
                }
                else if (reason == Enums.TNR_NOTIFY)
                {
                    // [:L349-L351] the only arm that carries the second numeric argument.
                    if (!IsCancelled())
                    {
                        nVal = Notifications.Trigger(TaskEventName.Notify, wparam, lparam, text);
                    }
                }
                else if (reason == Enums.TNR_ERROR)
                {
                    // [:L353-L355] carries the code and the text, and no second numeric argument.
                    if (!IsCancelled())
                    {
                        nVal = Notifications.Trigger(TaskEventName.Error, wparam, 0L, text);
                    }
                }

                // [:L357-L359] `if Not IsNull(nVal) then rtCode = nVal` - a per-reason answer overwrites
                // the catch-all's, and a null one leaves it alone. This is exactly why the dispatcher
                // returns a nullable rather than a sentinel.
                if (nVal is not null)
                {
                    rtCode = nVal;
                }
            }
        }
        finally
        {
            // [:L362-L364] `if IsValid(this) then _Eventful.#Silent = bSilent`. Restored in a finally so a
            // subscriber's fault cannot leave the dispatcher permanently silent.
            if (!_disposed)
            {
                Notifications.Silent = wasSilent;
            }
        }

        LogCapturedNotificationFaults(capturedBefore);

        // [:L366]
        return rtCode;
    }

    /// <summary>
    /// Whether the task has been cancelled - the port of <c>of_iscancelled</c>
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L318-L319</c>].
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the last exit code was <see cref="RetCode.CANCELLED"/> [<c>:L318</c>] OR
    /// the cancellation signal is set [<c>:L319</c>].
    /// </returns>
    /// <remarks>
    /// <b>Both terms are the oracle's, and the first is not redundant.</b> It is what makes a task that has
    /// already STOPPED because it was cancelled keep answering cancelled, after the signal has ceased to be
    /// the interesting fact. A zero-timeout poll again: asking never waits. <see cref="Cancellation"/> is
    /// the token form for a caller that wants to await rather than poll, and it cannot express the
    /// exit-code term, which is why both exist.
    /// </remarks>
    public bool IsCancelled() => _host.IsCancelled;

    /// <summary>
    /// The cancellation token - the .NET expression of <c>of_getcancelevent</c>
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L322</c>].
    /// </summary>
    /// <value>A token that becomes cancelled when the legacy handle would become signalled.</value>
    /// <remarks>
    /// This is the substitute for a raw handle, which has no meaning in a Linux container, and it is the
    /// instrument the async request/response expression of the proxy/worker pair is defined in terms of.
    /// Named <c>Cancellation</c> rather than <c>CancellationToken</c> on purpose: a property whose name
    /// equals its type's name shadows that type inside the declaring class, which would make
    /// <c>CancellationToken.None</c> unresolvable here.
    /// </remarks>
    public CancellationToken Cancellation => _host.Cancellation;

    /// <summary>
    /// Requests cancellation - the port of <c>of_cancel</c>
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L369-L370</c>].
    /// </summary>
    /// <returns><see cref="RetCode.OK"/>, unconditionally, as the oracle answers.</returns>
    /// <remarks>
    /// <b>NO BUSY GUARD, and none may be added.</b> The oracle's body signals the handle and answers OK
    /// [<c>:L369-L370</c>]. Guarding it would make a busy task uncancellable, which is precisely backwards -
    /// a busy task is the only one worth cancelling.
    /// </remarks>
    public long Cancel() => _host.Cancel();

    /// <summary>
    /// The exit code the task stopped with - the port of <c>of_getlastexitcode</c>
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L309</c>].
    /// </summary>
    /// <returns>The latched exit code.</returns>
    /// <remarks>
    /// Read by the update proxy's finalize hook, which opens with
    /// <c>if of_GetLastExitCode() = RetCode.OK then</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru:L303</c>], so this is
    /// consumed surface rather than completeness for its own sake. A stopped-by-cancellation task latches
    /// <see cref="RetCode.CANCELLED"/> here [<c>:L229-L230, :L234</c>], which is the first term of
    /// <see cref="IsCancelled"/>.
    /// </remarks>
    public long GetLastExitCode() => _host.LastExitCode;

    /// <summary>
    /// The last framework error code - the port of <c>of_getlasterrorcode</c>
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L312</c>].
    /// </summary>
    /// <returns>The latched framework error code.</returns>
    /// <remarks>
    /// <b>This is the FRAMEWORK's error, not the database's.</b> It is latched by the substrate's error
    /// handler [<c>:L213</c>] and is a different channel from <see cref="GetLastDbErrorData"/>, which the
    /// worker pushes. Confusing the two is easy and consequential: a failed statement populates the
    /// database payload and leaves this one untouched.
    /// </remarks>
    public long GetLastErrorCode() => _host.LastErrorCode;

    /// <summary>
    /// The last framework error text - the port of <c>of_getlasterrorinfo</c>
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L315</c>].
    /// </summary>
    /// <returns>The latched diagnostic text, or an empty string when none has been latched.</returns>
    public string GetLastErrorInfo() => _host.LastErrorInfo;

    /// <summary>
    /// The task's identifier - the port of <c>of_getid</c>
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L398</c>], taken from the worker during
    /// init [<c>:L201</c>].
    /// </summary>
    /// <returns>The identifier, or 0 before a successful init.</returns>
    public ulong GetId() => _host.TaskId;

    /// <summary>
    /// The task's ONE-BASED position in its controller's task list - the port of <c>of_getindex</c>
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L401</c>].
    /// </summary>
    /// <returns>
    /// A ONE-BASED ordinal, forwarded VERBATIM and deliberately NOT rebased to zero (R9).
    /// </returns>
    /// <remarks>
    /// <b>Rebasing this would be a defect, not a normalisation.</b> The worker's committed handler walks DOWN
    /// TO 1 from this value [<c>n_cst_thread_task_sqlbase.sru:L104</c>], and the ported one-based helper
    /// <c>PowerFramework.Persistence.Concurrency.OneBasedIndex</c> fixes 1 as the first index for exactly
    /// this family of values. Nothing in this file performs index arithmetic on it, so there is no
    /// conversion to route - the audit is that there is nothing to convert.
    /// </remarks>
    public int GetIndex() => _host.TaskIndex;

    /// <summary>
    /// The worker's registered class name, lower-cased - the port of <c>of_gettaskclassname</c>
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L601</c>].
    /// </summary>
    /// <returns>
    /// The name the substrate stored, lower-cased when it was stored [<c>:L191</c>]. It is therefore the
    /// lower-cased form of <see cref="WorkerTaskClassName"/> rather than that member's own spelling, and the
    /// two are deliberately not made to coincide.
    /// </returns>
    public string GetTaskClassName() => _host.TaskClassName;

    /// <summary>
    /// Records a veto for the dispatch in progress - the port of <c>of_preventevent()</c>
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L604</c>].
    /// </summary>
    /// <returns>
    /// <see cref="RetCode.OK"/>, or <see cref="RetCode.FAILED"/> when no dispatch is in progress.
    /// </returns>
    /// <remarks>
    /// <b>NO BUSY GUARD (C-B)</b> - the oracle's body is a single delegating statement to the broker, one of
    /// the unguarded convenience members this object repeats. A shallow veto; see
    /// <see cref="PreventEvent(bool)"/> for the deep form.
    /// </remarks>
    public long PreventEvent() => Notifications.Prevent();

    /// <summary>
    /// Records a shallow or deep veto - the port of <c>of_preventevent(boolean deep)</c>
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L611</c>].
    /// </summary>
    /// <param name="deep">
    /// <see langword="true"/> for <c>VetoResult.PreventDeep</c>, <see langword="false"/> for
    /// <c>VetoResult.PreventOnce</c>.
    /// </param>
    /// <returns>
    /// <see cref="RetCode.OK"/>, or <see cref="RetCode.FAILED"/> when no dispatch is in progress.
    /// </returns>
    /// <remarks>
    /// <b>NO BUSY GUARD (C-B).</b> The veto is TRI-VALUED and is never flattened to a boolean result: a
    /// prevent-once is consumed by the dispatch that raised it while a prevent-deep survives the whole
    /// nesting depth [<c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L954-L958</c>], so
    /// collapsing them would silently convert a deep prevention into a shallow one. The alphabet is
    /// <c>PowerFramework.Shared.Eventful.VetoResult</c> and is not restated in this service.
    /// </remarks>
    public long PreventEvent(bool deep) => Notifications.Prevent(deep);

    /// <summary>
    /// Delays the worker's start - the port of <c>of_setdelayfor</c>
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L607-L608</c>].
    /// </summary>
    /// <param name="seconds">The delay in seconds.</param>
    /// <returns>
    /// <see cref="RetCode.E_BUSY"/> when busy [<c>:L607</c>], otherwise the worker's result [<c>:L608</c>].
    /// </returns>
    /// <remarks>
    /// <b>This one DOES carry a busy guard</b>, unlike the prevent-event members immediately above - the
    /// oracle guards it and does not guard them, and both halves of that are reproduced. The value is
    /// forwarded verbatim and NO CLOCK IS READ here (C-H): a duration is not a point in time, and the
    /// waiting happens on the worker's side.
    /// </remarks>
    public long SetDelayFor(double seconds)
    {
        // [:L607]
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

        // [:L608] `return _Task.of_SetDelayFor(seconds)` - the worker's SUBSTRATE half, which is why this
        // goes through the host seam rather than through SqlTaskBase.
        return _host.SetWorkerDelayFor(seconds);
    }

    /// <summary>
    /// Marks the worker to be skipped - the port of <c>of_setskip</c>
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L614-L615</c>].
    /// </summary>
    /// <param name="skip">Whether to skip the task.</param>
    /// <returns>
    /// <see cref="RetCode.E_BUSY"/> when busy [<c>:L614</c>], otherwise the worker's result [<c>:L615</c>].
    /// </returns>
    public long SetSkip(bool skip)
    {
        // [:L614]
        if (IsBusy())
        {
            return RetCode.E_BUSY;
        }

        // [:L615] `return _Task.of_SetSkip(skip)`
        return _host.SetWorkerSkip(skip);
    }

    #endregion

    #region Worker resolution - the typed downcast the derived proxies repeat

    /// <summary>
    /// Narrows the worker to a derived task type - the support for the private <c>_of_gettask()</c> downcast
    /// accessor each derived proxy declares
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlcommand.sru:L26, :L31</c>;
    /// <c>n_cst_threading_task_sqlquery.sru:L62, :L322</c>;
    /// <c>n_cst_threading_task_sqlupdate.sru:L49, :L79</c>].
    /// </summary>
    /// <typeparam name="TTask">The derived worker type this proxy drives.</typeparam>
    /// <returns>
    /// The worker as <typeparamref name="TTask"/>, or <see langword="null"/> when no worker is attached or
    /// the attached one is of another type.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Each of the three derived proxies repeats the same private accessor whose body is the single
    /// statement <c>return _Task</c>, relying on PowerBuilder's implicit downcast. This member is what lets
    /// each of them keep that one-line accessor in C# while THE TWO TYPES STAY DISTINCT and separately
    /// DI-registrable - the base is deliberately not made generic over the worker type, because a generic
    /// base would put the worker's identity into the proxy's own type identity and make the pair harder to
    /// register, not easier.
    /// </para>
    /// <para>
    /// It answers null rather than throwing on a type mismatch, so a derived accessor can decide for itself
    /// whether a mismatch is a fault. A MISSING worker, by contrast, is a structural fault - see
    /// <see cref="RequireWorkerTask"/>.
    /// </para>
    /// </remarks>
    protected TTask? GetWorkerTask<TTask>()
        where TTask : SqlTaskBase => WorkerTask as TTask;

    /// <summary>
    /// Resolves the attached worker or fails fast - the port of the oracle's bare <c>task = _Task</c> read
    /// [<c>n_cst_threading_task_sqlbase.sru:L59, :L112, :L151, :L166, :L181, :L190, :L217</c>].
    /// </summary>
    /// <returns>The attached worker.</returns>
    /// <exception cref="InvalidOperationException">
    /// No worker is attached, which means <see cref="OnInit"/> has not succeeded or the proxy has been
    /// disposed.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Why this throws rather than returning a code, and why that is NOT a departure from the return-code
    /// algebra.</b> Every one of the oracle's seven call sites reads <c>_Task</c> into a local and
    /// dereferences it immediately, with no null test anywhere. In PowerBuilder that produces a
    /// null-object-reference RUNTIME ERROR, not a return code - and the framework turns such an error into
    /// termination: the application's system-error handler unpacks the payload and executes
    /// <c>HALT CLOSE</c> [<c>ws_objects/pfw.pbl.src/pfw.sra:L111-L144</c>]. So the faithful .NET expression
    /// is an exception, and the fail-fast posture the migration plan requires is preserved rather than
    /// softened into a warning-and-continue.
    /// </para>
    /// <para>
    /// The return-code algebra is untouched by this: no legacy <c>RetCode</c> value is ever converted into a
    /// throw anywhere in this file, and every legacy result - including
    /// <see cref="RetCode.PREVENT"/> and <see cref="RetCode.CANCELLED"/> - is returned as a number through
    /// the tri-state predicates. This exception is for a condition the oracle has no code for.
    /// </para>
    /// </remarks>
    protected SqlTaskBase RequireWorkerTask() =>
        WorkerTask
        ?? throw new InvalidOperationException(
            $"The '{TaskType}' task proxy has no worker task attached. "
            + $"OnInit must succeed - inserting '{WorkerTaskClassName}' - before this proxy is used, "
            + "and the proxy must not be used after disposal.");

    /// <summary>
    /// Logs subscriber faults the dispatcher captured during one fan-out.
    /// </summary>
    /// <param name="firstIndex">
    /// The capture count observed before the fan-out began, so that only NEW faults are logged.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>C-F-SAFE BY CONSTRUCTION IS AN ASSUMPTION ABOUT SUBSCRIBERS, AND IT IS NOT RELIED ON HERE.</b>
    /// A subscriber is code registered against this proxy's notification broker, so its message is whatever
    /// that code chose - and on this service the code near a notification is the SQL task layer, whose
    /// faults carry generated statements. The fault is therefore DESCRIBED rather than attached: attaching
    /// it made every provider render its whole message chain and stack, so the claim above governed the
    /// template and not the record. See <c>Errors/FaultRecord.cs</c>.
    /// </para>
    /// <para>
    /// The dispatcher captures rather than propagates because the legacy broker does, so without this the
    /// fault would be silent - captured and never surfaced - which is worse than either alternative.
    /// </para>
    /// </remarks>
    private void LogCapturedNotificationFaults(int firstIndex)
    {
        IReadOnlyList<Exception> captured = Notifications.CapturedExceptions;

        for (int index = firstIndex; index < captured.Count; index++)
        {
            _logger.LogError(
                "A notification subscriber on the {TaskType} task proxy threw and was captured rather than "
                    + "propagated. FaultTypes={FaultTypes} RedactedMessage={RedactedMessage}",
                TaskType,
                FaultRecord.Types(captured[index]),
                FaultRecord.RedactedMessages(captured[index]));
        }
    }

    #endregion

    #region Teardown - hazard 2 expressed as deterministic clearing

    /// <summary>
    /// Releases the proxy's cross-boundary references.
    /// </summary>
    /// <remarks>
    /// The pattern the sibling worker type uses, kept identical so both halves of the pair tear down the
    /// same way. The derived query proxy has a REAL destructor in the oracle -
    /// <c>event destructor;call super::destructor;Destroy Data</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L515</c>], paired with the
    /// constructor at <c>:L518</c> - so a disposal contract on this base is what that proxy extends rather
    /// than invents. This base itself has only the framework-generated create and destroy pair
    /// [<c>n_cst_threading_task_sqlbase.sru:L198-L204</c>], which is why nothing legacy-specific is
    /// released here beyond the references the uninit path nulls.
    /// </remarks>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the proxy's cross-boundary references.
    /// </summary>
    /// <param name="disposing">
    /// <see langword="true"/> when called from <see cref="Dispose()"/> rather than from a finalizer.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>HAZARD 2, EXPRESSED AS CODE</b> [<c>docs/PB多线程绕坑提示.md:L5</c>]. After a global object has been
    /// passed to a worker thread, the worker must set the referenced main-thread object to NULL in its
    /// uninit event to release the reference. The oracle does exactly that on this side of the boundary too -
    /// <c>SetNull(_Task)</c> in the substrate's uninit
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L273</c>] - so every cross-boundary
    /// reference is cleared EXPLICITLY here rather than left to the collector.
    /// </para>
    /// <para>
    /// <b>The commit signal is DROPPED, NOT DISPOSED, and that distinction is load-bearing.</b> It is
    /// BORROWED: the worker creates it [<c>n_cst_thread_task_sqlbase.sru:L224-L226</c>] and therefore owns
    /// it, and its committed handler signals it for THIS task and every preceding one [<c>:L100-L110</c>].
    /// Disposing it here would break a sibling task's own commit poll - a fault that would surface far from
    /// its cause - so the reference is released and the object is left to its owner.
    /// </para>
    /// <para>
    /// <b>Subscriptions are cleared</b>, because a surviving subscriber holds this proxy as its source
    /// argument and would keep it and everything it references reachable.
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
            // BORROWED, so the reference is dropped and the object is NOT disposed. See the remarks.
            _committedSignal = null;

            // The two cross-boundary references, cleared explicitly - hazard 2.
            WorkerTask = null;
            RetainedTransaction = null;

            // The latched payload carries a statement including interpolated literals (C-F), so it is
            // cleared on teardown rather than left addressable for the object's remaining lifetime.
            LastDbError = DbErrorData.Empty;

            _ = Notifications.OffAll();
        }

        // Set LAST, so that the validity re-tests in SendNotify observe a fully torn-down object rather
        // than a half-torn-down one.
        _disposed = true;
    }

    #endregion
}

#endregion
