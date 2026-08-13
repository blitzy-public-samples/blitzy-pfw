// =================================================================================================
//  Tasks/TaskProxies/ThreadingEventBroker.cs - the caller-side threading specialization of the
//  framework event broker.
// =================================================================================================
//  PROVENANCE. The port of ws_objects/pfw.thread.pbl.src/n_cst_threading_eventful.sru (89 lines),
//  which is declared `global type n_cst_threading_eventful from n_cst_eventful` [:L4, :L10]. It is
//  one of the six objects of ws_objects/pfw.thread.pbl.src, and AAP 0.4.1 assigns that whole library
//  to Persistence in scope. The base it derives from - n_cst_eventful, 1,328 lines of pure
//  PowerScript - is ported in shared/PowerFramework.Shared.Eventful, and that very inheritance is
//  one of the two structural facts AAP 0.4.1 cites as proof that the base belongs in a shared
//  in-scope library. docs/SERVICE_MAPPING.md records object 233 as In scope / Persistence / Ported.
//  Both legacy files are read-only behavioural oracle (C-C) and are never edited or reformatted.
//
//  WHY THIS FILE EXISTS RATHER THAN A LOCAL RE-IMPLEMENTATION.
//  The whole of this type's behaviour is four overridden events and one boolean property. Everything
//  underneath it - the ordered subscription table, the priority and prepend insert, the capture
//  filter, the tri-valued veto and its unwind, the handled latch, the default-return-value
//  substitution and the exception decoration - belongs to the base and is NOT restated here. That is
//  the point: a local stand-in would have had to reproduce all of it, and a reproduction that drifts
//  from the base is a second broker with the same name and different behaviour.
//
//  THE THREE WIN32 SIGNALS BECOME ONE SEAM.
//  The oracle holds three raw event handles [:L29-L31] and pokes them through SetEvent, ResetEvent
//  and WaitForSingleObject [:L17-L19]. A headless Linux service has no Win32 event object, and the
//  caller-side substrate already publishes the same three questions through ISqlTaskProxyHost. They
//  are therefore reached through IThreadingBrokerSignals, which is what makes this type testable with
//  no thread and no synchronization primitive at all (C-H).
//
//  SYNC-SIGNAL POLARITY, MEASURED RATHER THAN ASSUMED. of_IsBusy is
//  `WaitForSingleObject(_hEvtSync,0) <> 0` [n_cst_threading_task.sru:L306], so the signal being SET
//  means NOT BUSY. onnotify sets it before the fan-out and resets it after [:L291, :L295] precisely
//  so that a mutator is legal from inside a subscriber. onexception's
//  `WaitForSingleObject(_hEvtSync,0) = 0` test [:L82] therefore reads "the task is currently free",
//  and that is the condition under which a subscriber's fault is absorbed rather than propagated.
//
//  CONSTRAINTS. There are NO user rules for this project: review_rules returns exactly one line
//  saying so, and nothing is invented, inferred or back-filled in their place. The binding set is the
//  enterprise-standard baseline (AAP 0.7.2) plus the named non-rule constraints (AAP 0.7.3), and the
//  ruling FOR THIS FILE is:
//    C-A/C-I  Only PowerFramework.Shared.Eventful, PowerFramework.Shared.Kernel and siblings in this
//             folder are referenced. Nothing here reaches a peer service and no PackageReference is
//             added. Shared.Eventful depends only on Shared.Kernel and Shared.Diagnostics, both
//             already referenced by this project, so the graph stays acyclic.
//    C-B      Every one of the oracle's behaviours is reproduced, including the two that read oddly:
//             the commented-out null default return value at [:L74-L75] is NOT revived, and the
//             MessageBox at [:L83] becomes a recorded structured diagnostic rather than being dropped
//             or promoted to a throw.
//    C-F      Nothing here logs, formats or carries a statement, a path or a credential. The
//             exception the hook records is handed to the caller unmodified; the redaction of its text
//             is the caller's, in Errors/FaultRecord.cs.
//    .editorconfig  TaskProxies/ sits OUTSIDE every naming-suppression glob while
//             TreatWarningsAsErrors is true, so legacy SCREAMING_SNAKE spellings cannot be declared
//             here. Legacy VALUES are preserved exactly; the SPELLINGS become PascalCase per
//             AAP 0.4.5.3.
// =================================================================================================

using PowerFramework.Shared.Eventful;

// PowerFramework.Contracts.Common.V1 also declares a RetCode, and this project imports it widely, so
// the kernel's constant class is reached through an alias exactly as the sibling files in this folder
// reach it.
using RetCode = PowerFramework.Shared.Kernel.RetCode;

namespace PowerFramework.Persistence.Tasks.TaskProxies;

/// <summary>
/// The three synchronization signals the threading broker reads and raises - the seam that replaces
/// the oracle's three raw Win32 event handles
/// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_eventful.sru:L29-L31</c>] and the three kernel
/// prototypes it pokes them with [<c>:L17-L19</c>].
/// </summary>
/// <remarks>
/// <para>
/// <b>Every member has a legacy locator, and no speculative member is added.</b> The oracle uses
/// exactly three operations on exactly three handles: a zero-timeout poll, a set and a reset. Those
/// six combinations are what this interface publishes and nothing else.
/// </para>
/// <para>
/// <b>Modelling it as an interface is what makes the hooks reachable in a test (C-H).</b> A test
/// supplies a signals double and drives every arm of all four overrides with no thread, no wait
/// handle and no live task; without the seam the cancellation pre-veto and the fault absorption could
/// only be reached by racing a real worker.
/// </para>
/// </remarks>
internal interface IThreadingBrokerSignals
{
    /// <summary>
    /// Whether the cancellation signal is raised - <c>WaitForSingleObject(_hEvtCancelled,0) = 0</c>
    /// [<c>n_cst_threading_eventful.sru:L49, :L61</c>].
    /// </summary>
    /// <value><see langword="true"/> when the task has been cancelled.</value>
    bool IsCancelled { get; }

    /// <summary>
    /// Whether the synchronization signal is raised - <c>WaitForSingleObject(_hEvtSync,0) = 0</c>
    /// [<c>n_cst_threading_eventful.sru:L82</c>].
    /// </summary>
    /// <value>
    /// <see langword="true"/> when the signal is set, which by the polarity recorded in this file's
    /// header means the task is currently <b>not busy</b>.
    /// </value>
    bool IsSyncSignalSet { get; }

    /// <summary>
    /// Raises the synchronization signal - <c>SetEvent(_hEvtSync)</c>
    /// [<c>n_cst_threading_eventful.sru:L63</c>].
    /// </summary>
    void RaiseSyncSignal();

    /// <summary>
    /// Lowers the synchronization signal - <c>ResetEvent(_hEvtSync)</c>
    /// [<c>n_cst_threading_eventful.sru:L70</c>].
    /// </summary>
    void ClearSyncSignal();

    /// <summary>
    /// Raises the cancellation signal - <c>SetEvent(_hEvtCancelled)</c>
    /// [<c>n_cst_threading_eventful.sru:L79</c>].
    /// </summary>
    void RaiseCancellation();

    /// <summary>
    /// Raises the exception signal - <c>SetEvent(_hEvtException)</c>
    /// [<c>n_cst_threading_eventful.sru:L80</c>].
    /// </summary>
    /// <remarks>
    /// <b>A DELIBERATE NON-PORT WITH ITS REASON, recorded rather than silently collapsed.</b> The
    /// caller-side substrate initialises this broker with the PARENT CONTROLLER's cancel event as the
    /// exception handle - <c>Event OnInit(this,_hEvtCancelled,_hEvtSync,#ParentThreading.of_GetCancelEvent())</c>
    /// [<c>n_cst_threading_task.sru:L197</c>] - so in the oracle a subscriber fault cancels the whole
    /// controller and not just the faulting task. The controller itself,
    /// <c>ws_objects/pfw.thread.pbl.src/n_cst_threading.sru</c>, is <b>not modelled in this service at
    /// all</b>: both proxy hosts answer <c>IsControllerBusy</c> with a constant false and record why.
    /// There is therefore no controller-wide cancellation to raise, and the honest expression is that
    /// this signal resolves to the same cancellation as <see cref="RaiseCancellation"/>. It is kept as
    /// a separate member rather than merged into that one so the oracle's two distinct
    /// <c>SetEvent</c> calls remain visible and so a later phase that does model the controller has
    /// exactly one place to widen. Note the controller's own broker passes <c>_hEvtCancelled</c> for
    /// both handles anyway [<c>n_cst_threading.sru:L1009</c>], so the collapsed form is the oracle's
    /// behaviour on that side of the pair.
    /// </remarks>
    void RaiseException();
}

/// <summary>
/// The caller-side threading specialization of the framework event broker - the port of
/// <c>ws_objects/pfw.thread.pbl.src/n_cst_threading_eventful.sru</c>, which derives from
/// <c>n_cst_eventful</c> [<c>:L4, :L10</c>].
/// </summary>
/// <remarks>
/// <para>
/// <b>Four overrides and one property; everything else is the base's.</b> The oracle adds
/// <c>#Silent</c> [<c>:L24</c>], an initialisation event [<c>:L34-L38</c>], and overrides of
/// <c>onprepare</c> [<c>:L48-L58</c>], <c>ontriggering</c> [<c>:L60-L66</c>],
/// <c>ontriggered</c> [<c>:L68-L72</c>] and <c>onexception</c> [<c>:L79-L88</c>], plus a constructor
/// that establishes a default return value [<c>:L74-L77</c>]. Nothing else. The subscription table,
/// the dispatch order, the veto algebra and the exception decoration all live in
/// <see cref="EventBroker"/>.
/// </para>
/// <para>
/// <b>Not thread-safe by design, and that is the oracle's own contract.</b> Every caller-side class
/// in the legacy threading layer is marked <c>[运行在当前线程]</c> - it lives entirely on the calling
/// thread [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L2</c>] - so
/// subscription and dispatch happen on one thread and a lock here would model a synchronization the
/// legacy does not have. The cross-thread hand-off is the worker boundary and is guarded there.
/// </para>
/// <para>
/// <b>Sealed on purpose.</b> The oracle has no further derivation of this type: it is the leaf of the
/// broker chain, and <c>n_cst_threading_task</c> and <c>n_cst_threading</c> both instantiate it
/// directly [<c>n_cst_threading_task.sru:L195</c>, <c>n_cst_threading.sru:L1008</c>]. Sealing records
/// that fact and keeps the base's subclassing latch - which is
/// <c>GetType() != typeof(EventBroker)</c> - answering true for exactly one type here.
/// </para>
/// </remarks>
internal sealed class ThreadingEventBroker : EventBroker
{
    /// <summary>
    /// The faults <see cref="OnException"/> absorbed, in the order they were absorbed.
    /// </summary>
    private readonly List<Exception> _absorbedFaults = [];

    /// <summary>
    /// The object injected as every handler's leading argument - the oracle's <c>_source</c>
    /// [<c>n_cst_threading_eventful.sru:L27</c>], assigned by its initialisation event
    /// [<c>:L34</c>].
    /// </summary>
    private object? _source;

    /// <summary>
    /// The three signals, or <see langword="null"/> until <see cref="Initialize"/> has run - which
    /// reproduces the oracle's own window, since its three handles are zero until <c>oninit</c>
    /// assigns them [<c>:L35-L37</c>] and a zero handle makes every wait fail rather than succeed.
    /// </summary>
    private IThreadingBrokerSignals? _signals;

    /// <summary>
    /// Creates the broker and establishes its default return value.
    /// </summary>
    /// <remarks>
    /// <b>The constructor event, and the commented-out alternative in it is NOT revived (C-B).</b>
    /// <c>event constructor</c> calls its ancestor and then <c>of_SetDefaultReturnValue(0)</c>
    /// [<c>n_cst_threading_eventful.sru:L74-L77</c>]. Two lines directly above that call - a
    /// <c>long nvl</c> declaration and a <c>SetNull(nvl)</c> - are commented out, so the author
    /// considered a null default and chose zero. Zero is what ships.
    /// <para>
    /// That single line has a consequence worth stating because it is easy to lose: the base
    /// substitutes the established default for a non-posted dispatch that produced no value
    /// [<c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L966-L970</c>], so a dispatch
    /// over an unsubscribed name, or over subscribers that all declined to answer, answers <c>0</c>
    /// and never null. The notification path depends on exactly that: its result local starts at the
    /// PowerBuilder default <c>0</c> and its suppression gate tests for zero
    /// [<c>n_cst_threading_task.sru:L325, :L335</c>].
    /// </para>
    /// </remarks>
    internal ThreadingEventBroker()
    {
        // [:L76] of_SetDefaultReturnValue(0). Boxed as long rather than int so the value the base
        // compares against is the same CLR type every handler in this folder returns; a boxed int
        // would make the handled-detection comparison at n_cst_eventful.sru:L916 compare across types.
        _ = SetDefaultReturnValue(0L);
    }

    /// <summary>
    /// Whether the broker suppresses its own guards - the oracle's <c>#Silent</c>
    /// [<c>n_cst_threading_eventful.sru:L24</c>].
    /// </summary>
    /// <value>
    /// <see langword="true"/> while a caller is managing the guards itself. The notification path
    /// saves, sets and restores it around every dispatch
    /// [<c>n_cst_threading_task.sru:L328-L329, :L363</c>].
    /// </value>
    /// <remarks>
    /// <para>
    /// <b>What it actually suppresses, read from the oracle rather than assumed.</b> When the broker
    /// is silent it skips the cancellation pre-veto in <see cref="OnPrepare"/> [<c>:L48-L53</c>] and
    /// returns immediately from both <see cref="OnTriggering"/> and <see cref="OnTriggered"/>
    /// [<c>:L60, :L68</c>], which is where the non-silent path raises and lowers the synchronization
    /// signal [<c>:L63, :L70</c>]. So silence means: apply no cancellation veto of your own, and do
    /// not touch the sync signal. It does <b>not</b> suppress <see cref="OnException"/>, which tests
    /// no such flag [<c>:L79-L88</c>].
    /// </para>
    /// <para>
    /// <b>A MEASURED FINDING, recorded rather than coded around.</b> On the caller side the broker is
    /// triggered from exactly one place - <c>_of_sendnotify</c> - and that function sets this flag
    /// true for its entire body [<c>n_cst_threading_task.sru:L329</c>]. The non-silent branches of
    /// the two triggering hooks are therefore unreachable through the proxy's own publication path.
    /// They are nonetheless implemented here, unlike in the local stand-in this type replaces,
    /// because the oracle has them and because a direct caller - a test, or a future non-notify
    /// dispatch - genuinely reaches them.
    /// </para>
    /// </remarks>
    internal bool Silent { get; set; }

    /// <summary>
    /// The faults <see cref="OnException"/> absorbed rather than propagated, in order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This list is the port of the oracle's dialog, not of a capture list.</b> The oracle shows a
    /// modal <c>MessageBox("Thread Exception", ex.GetMessage(), StopSign!)</c> [<c>:L83</c>] on the
    /// arm where it absorbs the fault, which a headless service cannot do; AAP 0.3.4 turns every such
    /// dialog into a structured, machine-readable result, and this is that result. A fault the hook
    /// does NOT absorb is not recorded here, because the base rethrows it and the exception is then
    /// its own diagnostic.
    /// </para>
    /// <para>
    /// <b>No clearing member is published, and the growth that would need one is measured rather than
    /// assumed.</b> An absorbed fault raises the cancellation signal on the line above it
    /// [<c>:L79</c>], and after that only two dispatch paths still reach subscribers at all: the
    /// catch-all channel, which the notification path triggers with no cancellation screen
    /// [<c>n_cst_threading_task.sru:L331-L333</c>], and the STOP arm, which fires precisely BECAUSE the
    /// task is cancelled [<c>:L343-L347</c>]. Every other arm screens cancellation independently
    /// [<c>:L339, :L349, :L353</c>], and the notify entry point returns before dispatching at all
    /// [<c>:L289</c>]. So the ceiling is a small closed set of remaining lifecycle notifications rather
    /// than a function of how much work the task does, and each recorded fault is logged exactly once
    /// because the caller records where the list stood before its dispatch. Adding a clearing member
    /// would be a lifetime the oracle's dialog does not have.
    /// </para>
    /// </remarks>
    internal IReadOnlyList<Exception> AbsorbedFaults => _absorbedFaults;

    /// <summary>
    /// Establishes the source object and the three signals - the port of
    /// <c>event oninit(source, hEvtCancelled, hEvtSync, hEvtException)</c>
    /// [<c>n_cst_threading_eventful.sru:L34-L38</c>].
    /// </summary>
    /// <param name="source">
    /// The object injected as every handler's leading argument [<c>:L34</c>]. For a task proxy this
    /// is the proxy itself [<c>n_cst_threading_task.sru:L197</c>].
    /// </param>
    /// <param name="signals">The three signals, collapsed onto one seam.</param>
    /// <returns>
    /// <see cref="RetCode.OK"/>, or <see cref="RetCode.E_INVALID_ARGUMENT"/> when either argument is
    /// <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// The oracle's event is three unconditional assignments with no validation and no return value,
    /// because a PowerBuilder event cannot refuse its arguments. The refusal here is the .NET
    /// expression of the same intent: an uninitialised broker would inject a null leading argument
    /// into every handler and poll a null signal seam, which is the state the oracle's zero handles
    /// represent and which nothing in this folder is allowed to reach.
    /// </remarks>
    internal long Initialize(object? source, IThreadingBrokerSignals? signals)
    {
        if (source is null || signals is null)
        {
            return RetCode.E_INVALID_ARGUMENT;
        }

        // [:L34-L37] _source = source; the three handles follow.
        _source = source;
        _signals = signals;

        return RetCode.OK;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The port of <c>event onprepare</c> [<c>n_cst_threading_eventful.sru:L48-L58</c>], statement for
    /// statement. Three things happen in it and the order is contract.
    /// </para>
    /// <para>
    /// <b>First, the cancellation pre-veto, which SILENCE SUPPRESSES</b> [<c>:L48-L53</c>]. When the
    /// task has been cancelled the hook records a veto and answers a prevention, so no subscriber runs
    /// at all. This is exactly why the notification path forces silence for its whole body
    /// [<c>n_cst_threading_task.sru:L328-L329</c>]: the reason-level cancellation screens live there
    /// instead, one per reason [<c>:L339, :L349, :L353</c>], and running both would veto the stop
    /// notification that a cancellation is precisely what needs to publish.
    /// </para>
    /// <para>
    /// <b>Second, the injection guard</b> [<c>:L54</c>]. A handler that declares no argument has no
    /// slot to inject into, and a handler whose target IS the source does not need the injection
    /// because its own receiver is already the source. Either way the hook returns without writing.
    /// </para>
    /// <para>
    /// <b>Third, the injection itself</b> [<c>:L55-L56</c>]: the source goes into the ONE-BASED first
    /// slot and the consumed count becomes one, so the base copies the trigger's own payload from the
    /// second slot onwards. That single pair of statements is why every notification handler in this
    /// folder declares its source as its first parameter.
    /// </para>
    /// </remarks>
    protected override long OnPrepare(string name, object target, EventArgumentContext arguments)
    {
        // [:L48] call super::onprepare. PowerScript executes the ancestor script and DISCARDS its
        // value - the derived event's own return is what the caller sees - so the base's neutral OK is
        // called and ignored rather than propagated.
        _ = base.OnPrepare(name, target, arguments);

        // [:L48-L53] the cancellation pre-veto, suppressed by silence. IsCancelledNow answers false
        // for an uninitialised broker, which is the oracle's zero-handle behaviour.
        if (!Silent && IsCancelledNow())
        {
            // [:L50] of_Prevent() - the tri-valued veto recorded on the base, shallow form.
            _ = Prevent();

            // [:L51] return 1. This is RetCode.PREVENT, the alphabet the base tests with
            // Predicates.IsPrevented - deliberately NOT VetoResult, which shares the numeral.
            return RetCode.PREVENT;
        }

        // [:L54] if argCount < 1 or target = _source then return 0
        if (arguments.DeclaredArgumentCount < 1 || ReferenceEquals(target, _source))
        {
            return RetCode.OK;
        }

        // [:L55] invoker.SetArg(1,_source) - ONE-BASED, and the base owns the conversion.
        _ = arguments.SetArgument(1, _source);

        // [:L56] argPassed = 1
        arguments.ConsumedArgumentCount = 1;

        // [:L57]
        return RetCode.OK;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The port of <c>event ontriggering</c> [<c>n_cst_threading_eventful.sru:L60-L66</c>]. Silence
    /// returns immediately [<c>:L60</c>]; a cancelled task aborts the whole dispatch [<c>:L61</c>];
    /// and a non-posted dispatch raises the synchronization signal [<c>:L62-L64</c>], which by the
    /// polarity recorded in this file's header marks the task NOT BUSY for the duration of the
    /// dispatch and is what makes a mutator legal from inside a subscriber. The base fires this hook
    /// once per dispatch, on the first matching subscription only, so the signal is raised once and
    /// not once per subscriber.
    /// </remarks>
    protected override long OnTriggering(string name, bool isPost)
    {
        // [:L60] call super::ontriggering - value discarded, as above.
        _ = base.OnTriggering(name, isPost);

        // [:L60] if #Silent then return 0
        if (Silent)
        {
            return RetCode.OK;
        }

        // [:L61] if WaitForSingleObject(_hEvtCancelled,0) = 0 then return 1. A prevention here leaves
        // the base's dispatch loop WITHOUT setting the veto state, so nothing survives into an
        // enclosing dispatch - which is the base's own documented asymmetry, not an omission here.
        if (IsCancelledNow())
        {
            return RetCode.PREVENT;
        }

        // [:L62-L64] if Not isPost then SetEvent(_hEvtSync)
        if (!isPost)
        {
            _signals?.RaiseSyncSignal();
        }

        // [:L65]
        return RetCode.OK;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The port of <c>event ontriggered</c> [<c>n_cst_threading_eventful.sru:L68-L72</c>], the
    /// symmetric half of <see cref="OnTriggering"/>: silence returns immediately [<c>:L68</c>] and a
    /// non-posted dispatch lowers the signal its triggering hook raised [<c>:L69-L71</c>]. The base
    /// calls it from the dispatch's outer <see langword="finally"/> under the same "was anything
    /// dispatched" latch that guards the triggering hook, so either both fire or neither does and the
    /// signal cannot be left raised by a fault.
    /// </remarks>
    protected override void OnTriggered(string name, bool isPost)
    {
        // [:L68] call super::ontriggered
        base.OnTriggered(name, isPost);

        // [:L68] if #Silent then return
        if (Silent)
        {
            return;
        }

        // [:L69-L71] if Not isPost then ResetEvent(_hEvtSync)
        if (!isPost)
        {
            _signals?.ClearSyncSignal();
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The port of <c>event onexception</c> [<c>n_cst_threading_eventful.sru:L79-L88</c>], and the one
    /// override whose behaviour a reader is most likely to guess wrongly. <b>It is not a
    /// capture-and-continue.</b>
    /// </para>
    /// <para>
    /// <b>Unconditionally, before any decision:</b> the cancellation signal is raised [<c>:L79</c>]
    /// and the exception signal is raised [<c>:L80</c>]. A subscriber's fault CANCELS THE TASK. That
    /// is the oracle's fail-fast posture and it is preserved rather than softened: the legacy treats a
    /// misbehaving notification subscriber as a structural fault in the work, not as a nuisance to
    /// swallow.
    /// </para>
    /// <para>
    /// <b>Then one test decides the fate of the dispatch</b> [<c>:L82-L85</c>]. When the
    /// synchronization signal is set - the task is free, which is the state the notification fan-out
    /// establishes around itself - the fault is absorbed: the oracle shows a modal dialog and answers
    /// <c>1</c>, which makes the base leave the dispatch loop WITHOUT rethrowing, so the remaining
    /// subscribers of that dispatch do not run. The dialog becomes
    /// <see cref="AbsorbedFaults"/> (AAP 0.3.4). When the signal is clear the hook answers <c>0</c>
    /// [<c>:L87</c>] and the base rethrows the decorated exception, so the fault PROPAGATES to
    /// whoever triggered the dispatch.
    /// </para>
    /// <para>
    /// <b><c>case 2 //Continue</c> is never returned.</b> The base offers it
    /// [<c>n_cst_eventful.sru:L892-L894</c>] and this specialization declines it, so "the fault was
    /// absorbed and the next subscriber ran anyway" is not a state this broker can be in. An
    /// implementation that returned it would look more robust and would be a different broker.
    /// </para>
    /// </remarks>
    protected override long OnException(string name, Exception exception)
    {
        // [:L79] call super::onexception - value discarded, as above.
        _ = base.OnException(name, exception);

        // [:L79] SetEvent(_hEvtCancelled) - the fault cancels the task.
        _signals?.RaiseCancellation();

        // [:L80] SetEvent(_hEvtException) - see IThreadingBrokerSignals.RaiseException for why this
        // resolves to the same cancellation in a service that does not model the controller.
        _signals?.RaiseException();

        // [:L82] if WaitForSingleObject(_hEvtSync,0) = 0 then
        if (_signals is not null && _signals.IsSyncSignalSet)
        {
            // [:L83] MessageBox("Thread Exception", ex.GetMessage(), StopSign!) - recorded rather than
            // shown. Recorded BEFORE the return so a caller reading AbsorbedFaults after the dispatch
            // sees the fault that ended it.
            _absorbedFaults.Add(exception);

            // [:L84] return 1 - Prevent: leave the dispatch loop and do NOT rethrow. This is the
            // base's ExceptionResultPrevent alphabet, the third of the three that share the numeral 1.
            return ExceptionResultPrevent;
        }

        // [:L87] return 0 - the base rethrows the decorated exception.
        return RetCode.OK;
    }

    /// <summary>
    /// Whether the cancellation signal is currently raised, answering <see langword="false"/> for an
    /// uninitialised broker.
    /// </summary>
    /// <returns><see langword="true"/> when the task has been cancelled.</returns>
    /// <remarks>
    /// The oracle polls a raw handle, and a handle that <c>oninit</c> has not yet assigned is zero, on
    /// which <c>WaitForSingleObject</c> fails rather than reporting signalled - so an uninitialised
    /// oracle broker also reads as not cancelled. Collapsing the null seam to false is that behaviour,
    /// not a convenience.
    /// </remarks>
    private bool IsCancelledNow() => _signals is not null && _signals.IsCancelled;
}
