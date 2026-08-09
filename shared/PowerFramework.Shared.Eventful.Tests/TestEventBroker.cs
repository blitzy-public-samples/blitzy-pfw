// =====================================================================================================
//  TestEventBroker.cs
//  =====================================================================================================
//  SHARED TEST INFRASTRUCTURE: THE DERIVED-BROKER DOUBLE. THIS FILE IS NOT A TEST SUITE.
//
//  It declares no [Fact], no [Theory] and calls no Assert, and its file name deliberately carries no
//  `Tests` suffix so neither a reader scanning the folder nor the runner scanning the assembly mistakes it
//  for a suite. It is the peer of RecordingSubscriber.cs: that file doubles the SUBSCRIBER side of a
//  dispatch, this one doubles the BROKER side.
//
//  WHY THIS FILE HAS TO EXIST - THE SUBCLASSING GATE
//      The legacy broker gates its own four hook events behind a subclassing check made once, in the
//      constructor: `_bSubclassing = (_sThisClsName <> "n_cst_eventful")`
//      (ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L1324-L1327). Every hook site in the
//      dispatch loop is wrapped in `if _bSubclassing then`:
//          :L607  the pre-invocation prepare hook, inside _of_passargs
//          :L846  the triggering veto, on the first matching subscription only
//          :L889  the exception hook, per failing subscriber
//          :L950  the triggered notification, in the unwind path
//      SO A BASE-CLASS INSTANCE NEVER FIRES ITS OWN HOOKS. The port reproduces that exactly - the gate is
//      `_subclassing = GetType() != typeof(EventBroker)` at EventBroker.cs:829, tested at EventBroker.cs
//      :2407, :3295, :2531 and :2670 - which it achieves by making the four hooks `protected virtual`.
//      A plain `EventBroker` therefore cannot observe them either, and without a DERIVED type there is no
//      way to reach them from a test at all. That contrast - four hooks firing through this type and NONE
//      firing through a plain `EventBroker` - is the single property this file exists to make assertable.
//
//  THE SHAPE IS NOT INVENTED: IT MIRRORS THE ONE REAL SUBCLASS IN THE CORPUS
//      ws_objects/pfw.thread.pbl.src/n_cst_threading_eventful.sru is the ONLY subclass of the broker
//      anywhere in the repository, so it is the authority on how the hooks are actually used, and every
//      configurable below traces to one of its lines rather than to a guess:
//          :L24     `public boolean #Silent`                  -> SilentMode
//          :L27     `private powerobject _source`             -> SourceObject
//          :L29-L31 `_hEvtCancelled/_hEvtSync/_hEvtException` -> NOT PORTED; see SUBSTITUTION 2
//          :L34     oninit assigns the source and the handles -> plain settable properties
//          :L48-L58 onprepare  - cancel-and-prevent, then leading-argument injection with a consumed count
//          :L60-L66 ontriggering - silent short circuit, cancellation veto, sync-handle signal
//          :L68-L72 ontriggered  - the mirror, resetting that handle
//          :L74-L77 constructor  - of_SetDefaultReturnValue(0); see THE ONE THING DELIBERATELY NOT MIRRORED
//          :L79-L88 onexception  - arms cancellation, then returns 1 or 0
//      ws_objects/pfw.thread.pbl.src/n_cst_threading.sru:L360-L361 and :L392-L394 show the real caller
//      SAVING #Silent, forcing it true across a dispatch and RESTORING it afterwards, which is why
//      SilentMode is a plain read/write property rather than a constructor argument.
//
//  RULES POSITION, STATED EXPLICITLY
//      `review_rules` returns exactly one line: "No user rules provided." No user-specified rule governs
//      this file, none was invented to fill the gap, and that absence is not licence to lower the bar. The
//      AAP 0.7.2 enterprise baseline and the AAP 0.7.3 binding non-rule constraints govern in their place.
//      The four that bear on this file are named below with what each required here.
//
//  C-B - THE DOUBLE ONLY OBSERVES AND RETURNS. IT CORRECTS, NORMALISES AND COMPLETES NOTHING.
//      Every override records what it was handed and then returns what the suite configured. It does not
//      repair a value, does not clamp a count, does not filter a target and does not tidy an argument.
//      Three specific restraints follow, and each is load bearing rather than stylistic:
//        1. ConsumedArgumentCount is reported to the broker VERBATIM, including a negative or oversized
//           value, because the clamp at n_cst_eventful.sru:L610 and the Min at :L613 are the SUBJECT's
//           behaviour and a double that pre-clamped would be asserting its own.
//        2. The two injection guards are reproduced as CONFIGURABLE behaviour, not hard-coded, so a suite
//           can also observe what the broker does when they are lifted.
//        3. OnException can return each of the legacy's THREE outcomes faithfully - prevent, continue and
//           anything-else-means-rethrow - because the rethrow arm is a real behaviour that must stay
//           reachable, not an error case to be defended against.
//      The only validation anywhere in this file is a null guard on the CONSTRUCTOR, which is hygiene on an
//      API a suite calls directly. Nothing a hook is HANDED is ever validated, defaulted or coerced.
//
//  C-D - NO DEFERRED CAPABILITY APPEARS HERE, NOT EVEN AS A NAME.
//      SUBSTITUTION 1 - THE INVOKER PARAMETER IS GONE AND EventArgumentContext REPLACES IT.
//      The legacy hook signature is
//          onprepare(string name, powerobject target, n_scriptinvoker invoker, integer argcount,
//                    ref integer argpassed)                        (n_cst_eventful.sru:L33)
//      and `n_scriptinvoker` belongs to the DEFERRED ScriptBridge service. The port drops it (its own
//      DECISION 1) in favour of the invoker-free `EventArgumentContext`, which carries the two capabilities
//      the real override actually uses: `argcount` becomes DeclaredArgumentCount, `invoker.SetArg(1, ...)`
//      becomes the one-based SetArgument, and the `ref argpassed` out-parameter becomes
//      ConsumedArgumentCount. THIS FILE BINDS TO THAT CONTEXT. It names no script-invoker type, declares no
//      stand-in for one and reintroduces the concept under no other name. AAP 0.2.1.4 is why that is safe
//      rather than lossy: the invoker was only a variadic-call escape hatch, because PowerScript cannot
//      forward an arbitrary-length argument list, and C# forwards arguments natively.
//
//  C-H - NULLABLE REFERENCE TYPES AND WARNINGS AS ERRORS APPLY HERE AS THEY DO TO SHIPPING CODE.
//      Directory.Build.props sets Nullable enable, TreatWarningsAsErrors true, EnableNETAnalyzers true and
//      AnalysisLevel latest, and this project adds no NoWarn blanket. So there is no `#pragma warning
//      disable` in this file, no `!` null-forgiving operator papering over a real nullability question, no
//      unused field and no unused parameter - every hook parameter is READ, because it is recorded. All
//      four overrides match their base signatures EXACTLY, including nullability: a mismatch would surface
//      as CS0115 and the fix is always to re-read EventBroker.cs, never to loosen the base.
//
//  C-K - THE SUBSTITUTIONS ARE NAMED HERE RATHER THAN LEFT TO BE INFERRED.
//      SUBSTITUTION 1 - the n_scriptinvoker parameter -> EventArgumentContext. Recorded under C-D above and
//          again on OnPrepare.
//      SUBSTITUTION 2 - THE WIN32 EVENT HANDLES HAVE NO ANALOGUE IN A HEADLESS SERVICE.
//          The real subclass reaches kernel32 directly, declaring SetEvent, ResetEvent and
//          WaitForSingleObject as private external prototypes (n_cst_threading_eventful.sru:L17-L19) and
//          using them to signal a synchronisation handle from `ontriggering` (:L63), reset it from
//          `ontriggered` (:L70), arm a cancellation handle from `onexception` (:L79-L80) and poll all of
//          them with a zero timeout (:L49, :L61, :L82). None of that is ported. A .NET service running in a
//          Linux container has no Win32 event object, AAP 0.6.5 lists the message-pump family as a
//          DELIBERATE NON-PORT, and a wait handle would import timing into a suite that must stay
//          deterministic. SO THIS DOUBLE RECORDS THAT THE HOOK FIRED INSTEAD OF SIGNALLING A HANDLE: the
//          ordered DispatchLog row IS the signal, and it carries the `isPost` flag the legacy branched on
//          so a suite can still see which arm would have been taken. The polled cancellation handle becomes
//          the plain settable boolean CancellationSignalled - no handle, no timeout, no clock.
//      SUBSTITUTION 3 - THE DIALOG-FREE OBSERVER. The real `onexception` reports through
//          `MessageBox("Thread Exception", ex.GetMessage(), StopSign!)` (:L83), which no headless test can
//          read. The observation channel is the same DispatchLog, and the message the dialog would have
//          shown is captured as the decorated diagnostic block through the port's own
//          EventBroker.GetDispatchExceptionText - which must be read INSIDE the hook, because the broker
//          rewrites that text after the hook returns (EventBroker.cs:2553-2558).
//
//  THE ONE THING DELIBERATELY NOT MIRRORED, AND WHY
//      The real subclass's constructor calls `of_SetDefaultReturnValue(0)`
//      (n_cst_threading_eventful.sru:L74-L77), which installs a non-null global default and thereby selects
//      a different arm of the broker's handled-detection logic for the whole lifetime of the instance. This
//      double does NOT do that. Under C-B the double must not change what it observes, and a default
//      return value installed behind a suite's back would silently alter every dispatch the suite makes.
//      `SetDefaultReturnValue` is public on the broker, so a suite that wants the threading layer's policy
//      asks for it in one line and the choice stays visible in the test.
//
//  THE NAMING RULING - BUILD BREAKING IF IGNORED
//      The repository-root .editorconfig scopes its naming-analyzer suppressions
//      (dotnet_diagnostic.CA1707.severity = none and IDE1006 none) to TEN NAMED IMPLEMENTATION FILES -
//      RetCode.cs, Enums.cs, Categories.cs, EventGate.cs, ItemChangeProtocol.cs, ColumnExpressionEngine.cs,
//      ExpressionVariableEnvironment.cs, ClauseModifier.cs, IPagingRewriter.cs and LegacyDefaults.cs - and
//      covers NO test file. With TreatWarningsAsErrors true, a SCREAMING_SNAKE or underscored member here
//      is a BUILD ERROR and not a style opinion. Every member declared below is therefore conventional
//      PascalCase, and the legacy spellings - `#Silent`, `_source`, `_hEvtCancelled`, `argPassed`,
//      `of_Prevent`, `onprepare` - appear in documentation only. Where a suite needs one of the ported
//      alphabets it references the subject's own member: RetCode.PREVENT,
//      EventBroker.ExceptionResultContinue, VetoResult.PreventDeep.
//
//  DETERMINISM - THERE IS NO TIMING OF ANY KIND IN THIS FILE
//      No Task.Delay, no Thread.Sleep, no thread creation, no wait handle, no DateTime read, no Guid and no
//      Random. Ordering is expressed solely by the monotonic DispatchRecord.Sequence the shared log assigns
//      on append. The broker's only asynchrony is its post queue, and that is drained explicitly by
//      EventBroker.DrainPostedContinuations - which this file deliberately does NOT wrap or shadow, because
//      a suite must call it on the broker itself and see the real member's behaviour.
//
//  THREE DISTINCT ALPHABETS SHARE THE NUMERALS 1 AND 2 AND ARE NEVER INTERCHANGED HERE
//      1. the prevent state           VetoResult: Continue 0, PreventOnce 1, PreventDeep 2. NO hook
//                                     returns it, and it is NOT used anywhere in this file.
//      2. OnPrepare and OnTriggering  RetCode.OK 0 proceeds, RetCode.PREVENT 1 prevents; the broker tests
//                                     the result with Predicates.IsPrevented.
//      3. OnException                 EventBroker.ExceptionResultPrevent 1 leaves the loop and swallows,
//                                     EventBroker.ExceptionResultContinue 2 clears the latch and carries
//                                     on, ANYTHING ELSE rethrows - for which this file names
//                                     ExceptionResultRethrow, because the broker publishes no constant for
//                                     the third arm and an unnamed literal 0 would read like an oversight.
//
//  TWO VETOES WITH DIFFERENT REACH, AND WHY COLLAPSING THEM WOULD BREAK SUITES
//      OnPrepare's prevention skips ONE SUBSCRIBER and leaves the rest of the dispatch running: it makes
//      _of_passargs return false and the dispatch loop `continue` (n_cst_eventful.sru:L609-L610).
//      OnTriggering's prevention aborts THE WHOLE DISPATCH before any subscriber runs, by leaving the loop
//      outright (:L840-L841). They are configured by two SEPARATE properties here - PrepareResult and
//      TriggeringResult - precisely so a suite can tell a skipped subscriber apart from an aborted
//      dispatch.
// =====================================================================================================

using System;

using PowerFramework.Shared.Kernel;

namespace PowerFramework.Shared.Eventful.Tests;

/// <summary>
/// A derived <see cref="EventBroker"/> that makes the four <see langword="protected"/>
/// <see langword="virtual"/> subclassing hooks observable and configurable from a test.
/// </summary>
/// <remarks>
/// <para>
/// <b>Being derived is the entire point.</b> The broker gates all four hooks on
/// <c>GetType() != typeof(EventBroker)</c>, computed once in its constructor
/// (<c>EventBroker.cs:829</c>, the port of <c>n_cst_eventful.sru:L1324</c>), so a plain
/// <see cref="EventBroker"/> fires none of them. Dispatching through this type instead fires all four, and
/// the difference between those two dispatches is what a suite asserts to prove the gate survived the port.
/// </para>
/// <para>
/// <b>Inert by default.</b> Newly constructed, every configurable holds the value that leaves dispatch
/// exactly as a plain broker would run it: <see cref="PrepareResult"/> and <see cref="TriggeringResult"/>
/// proceed, <see cref="InjectLeadingArgument"/> is off, <see cref="ExceptionResult"/> rethrows as the base
/// hook does, and <see cref="SilentMode"/>, <see cref="CancellationSignalled"/> and
/// <see cref="ArmCancellationOnException"/> are all <see langword="false"/>. A suite therefore opts in to
/// each behaviour it wants to observe, one property at a time, and an instance that is only there to prove
/// the hooks fire needs no configuration at all.
/// </para>
/// <para>
/// <b>Every hook records into a shared <see cref="DispatchLog"/> before it decides anything</b>, so hook
/// rows and <see cref="RecordingSubscriber"/> rows interleave in one ordered sequence under one monotonic
/// <see cref="DispatchRecord.Sequence"/>. That interleaving is what proves the hook ORDER the legacy
/// dispatch loop fixes: <see cref="OnTriggering"/> once before the first subscriber and only after a
/// matching subscription has actually been found (<c>n_cst_eventful.sru:L838-L843</c>),
/// <see cref="OnPrepare"/> once per subscriber immediately before its invocation (<c>:L607</c>),
/// <see cref="OnException"/> per failing subscriber (<c>:L889</c>), and <see cref="OnTriggered"/> once in
/// the unwind path after the last subscriber (<c>:L940-L946</c>).
/// </para>
/// <para>
/// Ordering assertions are best written against <see cref="DispatchLog.HandlerNames"/> and
/// <see cref="DispatchLog.Labels"/> rather than <see cref="DispatchLog.Descriptions"/>, because an
/// <see cref="OnException"/> row carries a live exception and its decorated diagnostic block, both of which
/// render across several lines.
/// </para>
/// <para>
/// <b>Not thread-safe, deliberately</b>, for the same reason <see cref="DispatchLog"/> is not: one broker
/// instance belongs to one logical thread of control, the broker holds unsynchronised per-instance dispatch
/// state, and the legacy threading layer gives every thread its own broker. A lock here would imply a
/// scenario the subject does not support and would add a memory barrier to the ordering evidence.
/// </para>
/// </remarks>
public sealed class TestEventBroker : EventBroker
{
    /// <summary>
    /// The label this double writes into every row it records when no other label was supplied.
    /// </summary>
    /// <remarks>
    /// Lower case and deliberately unlike a subscriber label, so a glance at
    /// <see cref="DispatchLog.Labels"/> separates broker rows from subscriber rows without a lookup.
    /// </remarks>
    public const string DefaultLabel = "broker";

    /// <summary>
    /// The value <see cref="DispatchRecord.HandlerName"/> carries on a row recorded by
    /// <see cref="OnPrepare"/>.
    /// </summary>
    public const string PrepareHookName = nameof(OnPrepare);

    /// <summary>
    /// The value <see cref="DispatchRecord.HandlerName"/> carries on a row recorded by
    /// <see cref="OnTriggering"/>.
    /// </summary>
    public const string TriggeringHookName = nameof(OnTriggering);

    /// <summary>
    /// The value <see cref="DispatchRecord.HandlerName"/> carries on a row recorded by
    /// <see cref="OnTriggered"/>.
    /// </summary>
    public const string TriggeredHookName = nameof(OnTriggered);

    /// <summary>
    /// The value <see cref="DispatchRecord.HandlerName"/> carries on a row recorded by
    /// <see cref="OnException"/>.
    /// </summary>
    public const string ExceptionHookName = nameof(OnException);

    /// <summary>
    /// An <see cref="OnException"/> result that is neither <see cref="EventBroker.ExceptionResultPrevent"/>
    /// nor <see cref="EventBroker.ExceptionResultContinue"/>, and therefore <b>rethrows</b> to the
    /// trigger's caller.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Named here rather than written as a bare <c>0</c> because the third arm of the legacy
    /// <c>choose case</c> is a REAL outcome that a suite selects on purpose, and an unnamed literal would
    /// read like a missing case. The broker publishes constants for the other two arms only
    /// (<c>EventBroker.cs:922</c> and <c>:934</c>), because "anything else" cannot be enumerated - so this
    /// constant is one representative of that arm and not a third member of the broker's alphabet.
    /// </para>
    /// <para>
    /// <c>0</c> is the specific representative chosen because it is the value the base hook returns
    /// (<c>EventBroker.cs:1310-1315</c>) and the value the only real subclass returns for the same purpose,
    /// letting a thread exception propagate (<c>n_cst_threading_eventful.sru:L87</c>).
    /// </para>
    /// </remarks>
    public const long ExceptionResultRethrow = 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestEventBroker"/> class with a private log and the
    /// default label, for a suite that only needs the hook returns.
    /// </summary>
    /// <remarks>
    /// The log it creates is reachable through <see cref="Log"/>, so hook evidence is never lost by taking
    /// this path - it simply is not shared with any subscriber. Use a shared log instead whenever the
    /// assertion is about the ORDER of hooks relative to subscribers, because two separate logs cannot
    /// express a relative order at all.
    /// </remarks>
    public TestEventBroker()
        : this(new DispatchLog(), DefaultLabel)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TestEventBroker"/> class recording into a shared log
    /// under the default label.
    /// </summary>
    /// <param name="log">
    /// The shared invocation log. Pass the same instance the suite's <see cref="RecordingSubscriber"/>
    /// doubles were constructed with so hook rows and subscriber rows interleave.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="log"/> is <see langword="null"/>.</exception>
    public TestEventBroker(DispatchLog log)
        : this(log, DefaultLabel)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TestEventBroker"/> class recording into a shared log
    /// under an explicit label.
    /// </summary>
    /// <param name="log">
    /// The shared invocation log. Pass the same instance the suite's <see cref="RecordingSubscriber"/>
    /// doubles were constructed with so hook rows and subscriber rows interleave.
    /// </param>
    /// <param name="label">
    /// The label written into every row this instance records, so two brokers in one suite are
    /// distinguishable. May be empty; may not be <see langword="null"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="log"/> or <paramref name="label"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// The two guards are hygiene on an API a suite calls directly and are <b>not</b> normalisation of
    /// anything a hook is handed - the distinction C-B turns on. Nothing this double receives from the
    /// broker is ever validated, defaulted or coerced.
    /// </remarks>
    public TestEventBroker(DispatchLog log, string label)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(label);

        Log = log;
        Label = label;
    }

    /// <summary>
    /// Gets the log every hook on this instance records into.
    /// </summary>
    /// <remarks>
    /// The instance handed to the constructor, or the private one the parameterless constructor created.
    /// Shared with the suite's subscriber doubles in the first case, which is what makes a broker-versus-
    /// subscriber ordering assertion expressible.
    /// </remarks>
    public DispatchLog Log { get; }

    /// <summary>
    /// Gets the label written into every row this instance records.
    /// </summary>
    public string Label { get; }

    // =================================================================================================
    //  THE STATE THE REAL SUBCLASS CARRIES - n_cst_threading_eventful.sru:L22-L32, assigned by its
    //  `oninit` event at :L34. Plain read/write properties rather than constructor arguments, because the
    //  real caller MUTATES the silent flag around a single dispatch and restores it afterwards
    //  (n_cst_threading.sru:L360-L361 and :L392-L394).
    // =================================================================================================

    /// <summary>
    /// Gets or sets a value indicating whether the hooks take their silent, do-nothing path: the port of
    /// the legacy <c>public boolean #Silent</c> (<c>n_cst_threading_eventful.sru:L24</c>).
    /// </summary>
    /// <value><see langword="false"/> by default, which is the legacy field's initial value.</value>
    /// <remarks>
    /// <para>
    /// <b>The oracle's gating is ASYMMETRIC and the asymmetry is reproduced rather than tidied</b> - under
    /// C-B, flattening it would be a behaviour change dressed as consistency. Precisely:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="OnTriggering"/> is gated in full - <c>if #Silent then return 0</c> (<c>:L60</c>) - so a
    /// silent instance proceeds and neither <see cref="CancellationSignalled"/> nor
    /// <see cref="TriggeringResult"/> is consulted.
    /// </description></item>
    /// <item><description>
    /// <see cref="OnPrepare"/> is gated only over its CANCELLATION branch - <c>if Not #Silent then</c>
    /// (<c>:L48-L53</c>). The leading-argument injection at <c>:L54-L57</c> sits OUTSIDE the gate and
    /// therefore still happens in silent mode.
    /// </description></item>
    /// <item><description>
    /// <see cref="OnTriggered"/> is gated (<c>:L68</c>), but the only statement the gate guards is the
    /// Win32 handle reset that SUBSTITUTION 2 does not port, so the flag has no observable effect there.
    /// </description></item>
    /// <item><description>
    /// <see cref="OnException"/> is <b>not gated at all</b> (<c>:L79-L88</c> tests no such flag).
    /// </description></item>
    /// </list>
    /// <para>
    /// <b>Recording is never gated by this flag.</b> The log row is SUBSTITUTION 2's stand-in for the
    /// legacy handle signal, not a legacy behaviour, so suppressing it would make a silent hook invisible
    /// and defeat the purpose of the double. A suite proving that a silent hook leaves dispatch untouched
    /// still sees the row that says the hook ran.
    /// </para>
    /// </remarks>
    public bool SilentMode { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the cancellation condition is signalled: <b>the plain
    /// boolean that stands in for the legacy Win32 cancellation event handle</b>.
    /// </summary>
    /// <value><see langword="false"/> by default, meaning not cancelled.</value>
    /// <remarks>
    /// <para>
    /// <b>SUBSTITUTION 2.</b> The legacy holds a <c>ulong</c> handle (<c>_hEvtCancelled</c>,
    /// <c>n_cst_threading_eventful.sru:L29</c>) and polls it with
    /// <c>WaitForSingleObject(_hEvtCancelled, 0) = 0</c> at <c>:L49</c> and <c>:L61</c>. Neither the handle
    /// nor the polling call is ported: a headless .NET service in a Linux container has no Win32 event
    /// object, AAP 0.6.5 records the message-pump and wait-handle family as a deliberate non-port, and a
    /// real wait handle would import timing into a suite that must stay deterministic. Setting this
    /// property to <see langword="true"/> is exactly what an already-signalled handle meant.
    /// </para>
    /// <para>
    /// Its two effects mirror the two poll sites: in <see cref="OnPrepare"/> it prevents the dispatch and
    /// skips the subscriber, and in <see cref="OnTriggering"/> it vetoes the dispatch outright. Both are
    /// suppressed by <see cref="SilentMode"/>, exactly as the oracle suppresses them.
    /// </para>
    /// </remarks>
    public bool CancellationSignalled { get; set; }

    /// <summary>
    /// Gets or sets the designated source object: the port of the legacy <c>private powerobject _source</c>
    /// (<c>n_cst_threading_eventful.sru:L27</c>), which its <c>oninit</c> event assigns (<c>:L34</c>).
    /// </summary>
    /// <value><see langword="null"/> by default, which is the legacy field's state before initialization.</value>
    /// <remarks>
    /// Serves the two purposes it serves in the oracle: it is the target the injection guard compares
    /// against (<c>target = _source</c> at <c>:L54</c>, honoured here by
    /// <see cref="SkipInjectionForSourceTarget"/>), and it is the value the legacy injects into the leading
    /// slot (<c>invoker.SetArg(1,_source)</c> at <c>:L55</c>). This double keeps
    /// <see cref="LeadingArgument"/> separate from it so a suite can inject something OTHER than the source
    /// and still exercise the guard - two behaviours the oracle happens to fuse into one field, and which a
    /// double that fused them could not tell apart.
    /// </remarks>
    public object? SourceObject { get; set; }

    // =================================================================================================
    //  OnPrepare - THE PER-SUBSCRIBER OUTCOME. Reaches ONE subscriber; see the file header.
    // =================================================================================================

    /// <summary>
    /// Gets or sets the value <see cref="OnPrepare"/> returns on its normal path: <see cref="RetCode.OK"/>
    /// to invoke the handler, or <see cref="RetCode.PREVENT"/> to <b>skip this subscriber</b> and move on
    /// to the next.
    /// </summary>
    /// <value><see cref="RetCode.OK"/> by default, so the double is inert until a suite says otherwise.</value>
    /// <remarks>
    /// <para>
    /// Both legacy <c>return 0</c> statements in <c>onprepare</c> (<c>:L54</c> and <c>:L57</c>) come
    /// through here, so it is genuinely the hook's normal-path result rather than one arm of it.
    /// </para>
    /// <para>
    /// <b>This is the SECOND of the three alphabets</b> - the broker tests the result with
    /// <c>Predicates.IsPrevented</c> against <see cref="RetCode.PREVENT"/>, which is <c>1</c>
    /// (<c>n_cst_eventful.sru:L609</c>). It is not <see cref="VetoResult"/>. A prevention here makes
    /// <c>_of_passargs</c> return <see langword="false"/> and the dispatch loop <c>continue</c>
    /// (<c>:L610</c>), which leaves the rest of the dispatch running - use
    /// <see cref="TriggeringResult"/> to abort the whole dispatch instead.
    /// </para>
    /// <para>
    /// <see cref="CancellationSignalled"/> OUTRANKS this value, because the oracle's cancellation branch
    /// returns before reaching either <c>return 0</c>.
    /// </para>
    /// </remarks>
    public long PrepareResult { get; set; } = RetCode.OK;

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="OnPrepare"/> injects
    /// <see cref="LeadingArgument"/> into the leading argument slot and reports a consumed count.
    /// </summary>
    /// <value>
    /// <see langword="false"/> by default. <b>Off is deliberate</b>: injection shifts every argument the
    /// trigger supplied, so a double that did it unasked would silently change what every suite observes.
    /// </value>
    /// <remarks>
    /// Set to <see langword="true"/> to reproduce <c>invoker.SetArg(1,_source)</c> together with
    /// <c>argPassed = 1</c> (<c>n_cst_threading_eventful.sru:L55-L56</c>) - the two statements that
    /// establish the threading layer's <c>Handler(source, ...)</c> calling convention. When it is
    /// <see langword="false"/> the hook writes no slot and reports no consumed count, leaving
    /// <see cref="EventArgumentContext.ConsumedArgumentCount"/> at the zero the broker handed it.
    /// </remarks>
    public bool InjectLeadingArgument { get; set; }

    /// <summary>
    /// Gets or sets the value <see cref="OnPrepare"/> writes into argument slot one when
    /// <see cref="InjectLeadingArgument"/> is <see langword="true"/>.
    /// </summary>
    /// <value>
    /// <see langword="null"/> by default. Null is a legitimate value to inject and is passed through
    /// unchanged rather than treated as "nothing to inject" - <see cref="InjectLeadingArgument"/> is the
    /// only switch.
    /// </value>
    /// <remarks>
    /// The oracle injects its <c>_source</c> field here. A suite reproducing the threading layer exactly
    /// assigns the same object to this and to <see cref="SourceObject"/>; a suite that wants to see the
    /// injection WITHOUT the source-target guard interfering sets them to different objects.
    /// </remarks>
    public object? LeadingArgument { get; set; }

    /// <summary>
    /// Gets or sets the count <see cref="OnPrepare"/> reports through
    /// <see cref="EventArgumentContext.ConsumedArgumentCount"/> after an injection - the port of the legacy
    /// <c>ref integer argpassed</c> out-parameter (<c>n_cst_eventful.sru:L33</c>).
    /// </summary>
    /// <value><c>1</c> by default, which is the value the oracle reports (<c>n_cst_threading_eventful.sru:L56</c>).</value>
    /// <remarks>
    /// <para>
    /// <b>Settable, and reported to the broker VERBATIM - including a negative or oversized value.</b> That
    /// is the point of the property rather than a lapse in validation. <c>_of_passargs</c> applies this
    /// count BEFORE computing how many trigger arguments fit, clamping a non-positive value to zero
    /// (<c>:L610</c>) and taking <c>Min(argcount - consumed, supplied)</c> (<c>:L613</c>), so a suite can
    /// only observe leading-argument injection shifting the caller's arguments - and can only observe the
    /// clamp and the <c>Min</c> at their boundaries - by choosing this number itself. Clamping it here
    /// would assert the double's behaviour instead of the subject's, which C-B forbids.
    /// </para>
    /// <para>
    /// Reported only when an injection actually happened. Both oracle guard paths return before
    /// <c>argPassed = 1</c> is reached, so a suppressed injection reports nothing.
    /// </para>
    /// </remarks>
    public int ConsumedArgumentCount { get; set; } = 1;

    /// <summary>
    /// Gets or sets a value indicating whether injection is suppressed for a handler that declares no
    /// argument slots: the first half of the legacy guard
    /// <c>if argCount &lt; 1 or target = _source then return 0</c>
    /// (<c>n_cst_threading_eventful.sru:L54</c>).
    /// </summary>
    /// <value><see langword="true"/> by default, which is the legacy behaviour.</value>
    /// <remarks>
    /// Configurable rather than hard-coded so a suite can also observe the broker WITHOUT the guard: with
    /// this <see langword="false"/> and a zero-arity handler, the one-based
    /// <see cref="EventArgumentContext.SetArgument"/> has no slot 1 to write and answers
    /// <see cref="RetCode.E_INVALID_ARGUMENT"/>, which <see cref="LeadingArgumentWriteResult"/> reports.
    /// </remarks>
    public bool SkipInjectionWithoutArgumentSlots { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether injection is suppressed when the subscription's target is
    /// <see cref="SourceObject"/>: the second half of the legacy guard at
    /// <c>n_cst_threading_eventful.sru:L54</c>.
    /// </summary>
    /// <value><see langword="true"/> by default, which is the legacy behaviour.</value>
    /// <remarks>
    /// The legacy <c>target = _source</c> compares object identity, so the port compares by reference and
    /// never by <see cref="object.Equals(object?)"/> - an overridden equality on a suite's double would
    /// otherwise change which subscribers get the injection. With <see cref="SourceObject"/> left
    /// <see langword="null"/> the comparison never matches, which is exactly the oracle's state before its
    /// <c>oninit</c> event has run.
    /// </remarks>
    public bool SkipInjectionForSourceTarget { get; set; } = true;

    /// <summary>
    /// Gets the result of the most recent <see cref="EventArgumentContext.SetArgument"/> call
    /// <see cref="OnPrepare"/> made, or <see langword="null"/> when it has not attempted an injection yet.
    /// </summary>
    /// <value>
    /// <see cref="RetCode.OK"/> when the slot was written, <see cref="RetCode.E_INVALID_ARGUMENT"/> when
    /// slot one lay outside the handler's declared range, or <see langword="null"/> when no injection was
    /// attempted - which distinguishes "not tried" from "tried and refused" without a second flag.
    /// </value>
    /// <remarks>
    /// Reports the MOST RECENT attempt. <see cref="OnPrepare"/> fires once per subscriber, so a dispatch
    /// reaching several subscribers overwrites this; the per-occurrence evidence a suite needs for that case
    /// is the ordered run of <see cref="PrepareHookName"/> rows in <see cref="Log"/>, each carrying its own
    /// target and declared argument count.
    /// </remarks>
    public long? LeadingArgumentWriteResult { get; private set; }

    /// <summary>
    /// Gets the result of the most recent <see cref="EventBroker.Prevent()"/> call
    /// <see cref="OnPrepare"/>'s cancellation branch made, or <see langword="null"/> when that branch has
    /// not fired yet.
    /// </summary>
    /// <value>
    /// <see cref="RetCode.OK"/> when the prevent state was set, <see cref="RetCode.FAILED"/> when no
    /// dispatch was in progress, or <see langword="null"/> when the branch has not run.
    /// </value>
    /// <remarks>
    /// The oracle discards this result - <c>of_Prevent()</c> is called as a statement at
    /// <c>n_cst_threading_eventful.sru:L50</c> - but it is captured here because it is the only evidence
    /// that the prevention actually took effect, and because the broker answers
    /// <see cref="RetCode.FAILED"/> outside a dispatch rather than throwing
    /// (<c>EventBroker.cs:3059-3066</c>, the port of <c>n_cst_eventful.sru:L1293</c>).
    /// </remarks>
    public long? CancellationPreventResult { get; private set; }

    // =================================================================================================
    //  OnTriggering - THE WHOLE-DISPATCH OUTCOME. Fires ONCE, and only once a matching subscription has
    //  been found (n_cst_eventful.sru:L838-L843).
    // =================================================================================================

    /// <summary>
    /// Gets or sets the value <see cref="OnTriggering"/> returns on its normal path:
    /// <see cref="RetCode.OK"/> to let the dispatch proceed, or <see cref="RetCode.PREVENT"/> to
    /// <b>abort the whole dispatch before any subscriber runs</b>.
    /// </summary>
    /// <value><see cref="RetCode.OK"/> by default, so the double is inert until a suite says otherwise.</value>
    /// <remarks>
    /// <para>
    /// The same alphabet as <see cref="PrepareResult"/> and a different REACH, which is why the two are
    /// separate properties. A prevention here leaves the dispatch loop outright
    /// (<c>n_cst_eventful.sru:L840-L841</c>) and, because the abort happens before the prevent state is
    /// ever set, nothing survives into an enclosing dispatch.
    /// </para>
    /// <para>
    /// Consulted only when <see cref="SilentMode"/> is <see langword="false"/>, and outranked by
    /// <see cref="CancellationSignalled"/> - the oracle's order is silent, then cancelled, then this
    /// (<c>n_cst_threading_eventful.sru:L60-L65</c>).
    /// </para>
    /// </remarks>
    public long TriggeringResult { get; set; } = RetCode.OK;

    // =================================================================================================
    //  OnException - THE THREE-VALUED OUTCOME. Fires per failing subscriber (n_cst_eventful.sru:L889).
    // =================================================================================================

    /// <summary>
    /// Gets or sets the value <see cref="OnException"/> returns, selecting one of the legacy's three
    /// outcomes.
    /// </summary>
    /// <value>
    /// <see cref="ExceptionResultRethrow"/> by default, matching the base hook
    /// (<c>EventBroker.cs:1310-1315</c>) so the double neither swallows nor diverts an exception unasked.
    /// </value>
    /// <remarks>
    /// <para>
    /// <b>All three arms are independently selectable, and all three are real:</b>
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="EventBroker.ExceptionResultPrevent"/> (<c>1</c>) leaves the dispatch loop and swallows
    /// the exception - the legacy <c>case 1 //Prevent</c> at <c>n_cst_eventful.sru:L890-L891</c>, and what
    /// the real subclass returns after surfacing the error
    /// (<c>n_cst_threading_eventful.sru:L84</c>).
    /// </description></item>
    /// <item><description>
    /// <see cref="EventBroker.ExceptionResultContinue"/> (<c>2</c>) clears the exception latch and carries
    /// on with the next subscriber - <c>case 2 //Continue</c> at <c>:L892-L894</c>. Because it clears the
    /// latch, the NEXT exception in the same dispatch is decorated afresh, so the hook can fire more than
    /// once per dispatch and the log will show it.
    /// </description></item>
    /// <item><description>
    /// <b>Anything else, including <see cref="ExceptionResultRethrow"/>, rethrows to the trigger's
    /// caller</b> - the legacy <c>choose case</c> has no <c>case else</c>, so control falls through to the
    /// throw at <c>:L899-L902</c>. This arm must stay reachable: the framework's posture on a structural
    /// fault is fail-fast rather than graceful degradation (AAP 0.1.4), and softening it into a swallow
    /// would be exactly the behaviour improvement C-B forbids.
    /// </description></item>
    /// </list>
    /// <para>
    /// <b>This is the THIRD of the three alphabets.</b> It is not <see cref="VetoResult"/> and not a return
    /// code: its <c>2</c> means "keep going" while <see cref="VetoResult.PreventDeep"/>'s <c>2</c> means
    /// very nearly the opposite. Never assign one to the other.
    /// </para>
    /// </remarks>
    public long ExceptionResult { get; set; } = ExceptionResultRethrow;

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="OnException"/> arms
    /// <see cref="CancellationSignalled"/>, reproducing the real subclass's
    /// <c>SetEvent(_hEvtCancelled)</c> (<c>n_cst_threading_eventful.sru:L79-L80</c>).
    /// </summary>
    /// <value>
    /// <see langword="false"/> by default. Off is deliberate: arming cancellation is a state change that
    /// would veto every LATER dispatch on this instance, and a double must not do that behind a suite's
    /// back.
    /// </value>
    /// <remarks>
    /// Offered because the coupling is real - in the threading layer a handler exception genuinely cancels
    /// the thread, so a suite proving that a failed dispatch stops the next one needs it - and because
    /// omitting it would lose a behaviour of the one real consumer. The paired
    /// <c>SetEvent(_hEvtException)</c> at <c>:L80</c> has no separate effect to reproduce: under
    /// SUBSTITUTION 2 the exception handle's only purpose was to be observed from outside, and the
    /// <see cref="ExceptionHookName"/> row in <see cref="Log"/> is that observation.
    /// </remarks>
    public bool ArmCancellationOnException { get; set; }

    // =================================================================================================
    //  THE FOUR OVERRIDES
    //
    //  Every one follows the same three-step shape, and the order of the steps is deliberate:
    //      1. CALL THE BASE FIRST. All four real overrides open with `call super::<event>`
    //         (n_cst_threading_eventful.sru:L48, L60, L68, L79), and the port's own header states that an
    //         override calling base.OnX(...) first and then adding its own logic is the intended shape
    //         (EventBroker.cs:1199-1204). The base results are neutral by contract - OK, nothing, 0, OK -
    //         and are DISCARDED rather than propagated, exactly as PowerScript discards the value of a
    //         `call super::` statement. The discard is written explicitly so a reader cannot mistake it for
    //         a forgotten result.
    //      2. RECORD, before anything that can divert or abort. The row is this file's substitute for the
    //         legacy Win32 handle signalling (SUBSTITUTION 2), and it is NEVER gated by SilentMode: a
    //         silent hook that recorded nothing would be indistinguishable from a hook that never fired,
    //         which is the one distinction the double exists to make.
    //      3. WALK THE ORACLE'S DECISION LADDER, in the oracle's order, returning the value the suite
    //         configured on the normal path.
    //
    //  None of the four validates a parameter. The broker is their only caller and passes non-null by
    //  construction; a guard here would be defence against the subject rather than observation of it.
    // =================================================================================================

    /// <summary>
    /// Records the triggering hook and returns the configured whole-dispatch outcome.
    /// </summary>
    /// <param name="name">The event name being dispatched, recorded as the row's topic.</param>
    /// <param name="isPost">
    /// <see langword="true"/> when the dispatch was queued by <see cref="EventBroker.Post"/>, recorded as
    /// the row's single argument.
    /// </param>
    /// <returns>
    /// <see cref="RetCode.OK"/> when <see cref="SilentMode"/> is set; <see cref="RetCode.PREVENT"/> when
    /// <see cref="CancellationSignalled"/> is set; otherwise <see cref="TriggeringResult"/>.
    /// </returns>
    /// <remarks>
    /// The port of <c>ontriggering</c> as the real subclass overrides it
    /// (<c>n_cst_threading_eventful.sru:L60-L66</c>), whose ladder this follows line for line.
    /// </remarks>
    protected override long OnTriggering(string name, bool isPost)
    {
        // :L60 - call super::ontriggering
        _ = base.OnTriggering(name, isPost);

        // SUBSTITUTION 2, first half. :L62-L64 signals the synchronisation handle for a non-posted
        // dispatch - `if Not isPost then SetEvent(_hEvtSync)`. There is no handle to signal in a headless
        // service, so the fact of the hook firing is recorded instead, and isPost travels with it so a
        // suite can still see which arm the legacy would have taken.
        Log.Append(Label, TriggeringHookName, name, [isPost]);

        // :L60 - if #Silent then return 0. Gated in FULL here, unlike OnPrepare: a silent instance
        // proceeds and consults neither the cancellation flag nor the configured outcome.
        if (SilentMode)
        {
            return RetCode.OK;
        }

        // :L61 - if WaitForSingleObject(_hEvtCancelled,0) = 0 then return 1. Cancellation OUTRANKS the
        // configured outcome because the oracle returns here, before its own `return 0` is reached.
        if (CancellationSignalled)
        {
            return RetCode.PREVENT;
        }

        // :L65 - return 0, which is this double's configurable normal path. A prevention from here aborts
        // the WHOLE dispatch (n_cst_eventful.sru:L840-L841), not just one subscriber.
        return TriggeringResult;
    }

    /// <summary>
    /// Records the triggered hook. Returns nothing, exactly as the legacy event does.
    /// </summary>
    /// <param name="name">The event name that was dispatched, recorded as the row's topic.</param>
    /// <param name="isPost">
    /// <see langword="true"/> when the dispatch was queued by <see cref="EventBroker.Post"/>, recorded as
    /// the row's single argument.
    /// </param>
    /// <remarks>
    /// <para>
    /// The port of <c>ontriggered</c> as the real subclass overrides it
    /// (<c>n_cst_threading_eventful.sru:L68-L72</c>). Recording only, by requirement: the hook has no
    /// return value to configure.
    /// </para>
    /// <para>
    /// Its row is what proves the pair is SYMMETRIC. The broker guards this hook with the same "was
    /// anything dispatched" latch that guards <see cref="OnTriggering"/> (<c>n_cst_eventful.sru:L942</c>
    /// and <c>:L838</c>), so either both fire or neither does - and because it runs from the dispatch's
    /// outer <see langword="finally"/> (<c>:L940-L946</c>) it is reached even when the loop ended in an
    /// exception or a prevention. A suite asserting that "the last row is <see cref="TriggeredHookName"/>"
    /// is asserting exactly that.
    /// </para>
    /// </remarks>
    protected override void OnTriggered(string name, bool isPost)
    {
        // :L68 - call super::ontriggered. Void, so there is no result to discard.
        base.OnTriggered(name, isPost);

        // SUBSTITUTION 2, second half. :L69-L71 resets the synchronisation handle for a non-posted
        // dispatch - `if Not isPost then ResetEvent(_hEvtSync)` - and the row stands in for that reset.
        Log.Append(Label, TriggeredHookName, name, [isPost]);

        // :L68 - if #Silent then return. NO BRANCH IS WRITTEN FOR IT, and that is a decision rather than
        // an omission: the only statement the oracle's gate guards is the ResetEvent immediately above,
        // which SUBSTITUTION 2 does not port. With nothing left inside the gate, a silent check here could
        // only suppress the recording - and recording is this file's observation channel, not legacy
        // behaviour, so suppressing it would hide the hook instead of reproducing the oracle. SilentMode is
        // therefore honoured everywhere it has an observable effect and honoured by documentation here,
        // where it has none.
    }

    /// <summary>
    /// Records the exception hook, optionally arms cancellation, and returns the configured one of the
    /// three legacy outcomes.
    /// </summary>
    /// <param name="name">The event name being dispatched, recorded as the row's topic.</param>
    /// <param name="exception">
    /// The exception the handler raised, recorded as the row's first argument - the instance itself, so its
    /// identity survives into the suite's assertions.
    /// </param>
    /// <returns>
    /// <see cref="ExceptionResult"/>, unmodified: <see cref="EventBroker.ExceptionResultPrevent"/>,
    /// <see cref="EventBroker.ExceptionResultContinue"/>, or any other value to rethrow.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The port of <c>onexception</c> as the real subclass overrides it
    /// (<c>n_cst_threading_eventful.sru:L79-L88</c>). <b>Deliberately NOT gated by
    /// <see cref="SilentMode"/></b>, because the oracle tests no such flag here.
    /// </para>
    /// <para>
    /// <b>The row's second argument is the decorated diagnostic block as this hook saw it</b>, read through
    /// <see cref="EventBroker.GetDispatchExceptionText"/> - SUBSTITUTION 3's stand-in for the oracle's
    /// <c>ex.GetMessage()</c> read at <c>:L83</c>. It has to be captured HERE and cannot be recovered
    /// afterwards: the broker records the four-line block before calling this hook
    /// (<c>n_cst_eventful.sru:L886</c> precedes <c>:L889</c>) and then, if this hook does not stop the
    /// exception and the dispatch is the outermost one, REWRITES it with the broker's own class name
    /// prefixed (<c>:L899-L901</c>, ported at <c>EventBroker.cs:2553-2558</c>). Reading it later therefore
    /// yields a different string.
    /// </para>
    /// </remarks>
    protected override long OnException(string name, Exception exception)
    {
        // :L79 - call super::onexception
        _ = base.OnException(name, exception);

        // SUBSTITUTION 3. :L83 reports through MessageBox, which no headless test can read, so the row
        // carries both the live exception and the decorated block the dialog would have shown - captured
        // now because the broker rewrites that text after this hook returns.
        Log.Append(Label, ExceptionHookName, name, [exception, GetDispatchExceptionText(exception)]);

        // :L79-L80 - SetEvent(_hEvtCancelled) and SetEvent(_hEvtException). Opt-in, because arming
        // cancellation changes the outcome of every LATER dispatch on this instance.
        if (ArmCancellationOnException)
        {
            CancellationSignalled = true;
        }

        // :L82-L87 - the oracle picks 1 or 0 from a handle state it polled. Here the outcome is configured
        // outright, which is what makes all THREE arms of the broker's choose case reachable - the oracle
        // itself can never return 2.
        return ExceptionResult;
    }

    /// <summary>
    /// Records the prepare hook, optionally injects a leading argument, and returns the configured
    /// per-subscriber outcome.
    /// </summary>
    /// <param name="name">The event name being dispatched, recorded as the row's topic.</param>
    /// <param name="target">
    /// The subscription's callback target, recorded as the row's first argument and compared against
    /// <see cref="SourceObject"/> by the injection guard.
    /// </param>
    /// <param name="arguments">
    /// The argument buffer for this invocation - <b>the invoker-free substitute for the legacy
    /// <c>n_scriptinvoker</c> parameter</b>. Its
    /// <see cref="EventArgumentContext.DeclaredArgumentCount"/> is recorded as the row's second argument.
    /// </param>
    /// <returns>
    /// <see cref="RetCode.PREVENT"/> when the cancellation branch fires; otherwise
    /// <see cref="PrepareResult"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The port of <c>onprepare</c> as the real subclass overrides it
    /// (<c>n_cst_threading_eventful.sru:L48-L58</c>), whose four-statement body this follows in order.
    /// </para>
    /// <para>
    /// <b>SUBSTITUTION 1, and the reason this override binds to <paramref name="arguments"/>.</b> The legacy
    /// signature carries <c>n_scriptinvoker invoker, integer argcount, ref integer argpassed</c>
    /// (<c>n_cst_eventful.sru:L33</c>), and <c>n_scriptinvoker</c> belongs to the deferred ScriptBridge
    /// service. The port drops it for <see cref="EventArgumentContext"/>, which carries the two capabilities
    /// the real override actually uses. This override therefore reaches the leading slot through
    /// <see cref="EventArgumentContext.SetArgument"/> and reports the consumed count through
    /// <see cref="EventArgumentContext.ConsumedArgumentCount"/>, and it names no script-invoker type and no
    /// stand-in for one.
    /// </para>
    /// <para>
    /// Fires ONCE PER SUBSCRIBER, from inside <c>_of_passargs</c> immediately before the invocation
    /// (<c>:L607</c>), so its rows interleave one-for-one with the subscriber rows that follow each of them.
    /// </para>
    /// </remarks>
    protected override long OnPrepare(string name, object target, EventArgumentContext arguments)
    {
        // :L48 - call super::onprepare
        _ = base.OnPrepare(name, target, arguments);

        // The evidence, before the ladder can divert. DeclaredArgumentCount is the legacy `argcount`, and
        // recording it alongside the target is what lets a suite see WHICH subscriber was prepared and how
        // many slots it had. Those are precisely the two values the injection guard below is computed from,
        // so a suite can reconstruct the guard's decision from the row alone.
        Log.Append(Label, PrepareHookName, name, [target, arguments.DeclaredArgumentCount]);

        // :L48-L53 - the cancellation branch, and the ONLY part of this hook the silent flag gates:
        //     if Not #Silent then
        //         if WaitForSingleObject(_hEvtCancelled,0) = 0 then
        //             of_Prevent()
        //             return 1
        //         end if
        //     end if
        // The oracle does BOTH things - of_Prevent() sets the dispatch-wide prevent state, which the loop
        // tests at the top of its next iteration (n_cst_eventful.sru:L822), and returning a prevention
        // skips this subscriber as well. Both effects are reproduced; collapsing them to one would lose the
        // dispatch-wide half.
        if (!SilentMode && CancellationSignalled)
        {
            // :L50 - of_Prevent(). Its result is captured rather than discarded because it is the only
            // evidence the prevent state was actually set.
            CancellationPreventResult = Prevent();

            // :L51 - return 1
            return RetCode.PREVENT;
        }

        // :L54 - if argCount < 1 or target = _source then return 0. Both halves of the guard are
        // configurable rather than hard-coded, so a suite can observe the broker with either lifted. The
        // source comparison is by REFERENCE because PowerScript's `target = _source` compares object
        // identity; using Equals would let an overridden equality on a suite's double change which
        // subscribers are injected into.
        bool injectionSuppressed =
            (SkipInjectionWithoutArgumentSlots && arguments.DeclaredArgumentCount < 1)
            || (SkipInjectionForSourceTarget && ReferenceEquals(target, SourceObject));

        if (InjectLeadingArgument && !injectionSuppressed)
        {
            // :L55 - invoker.SetArg(1,_source). ONE-BASED, and the conversion to .NET's zero-based buffer
            // happens once inside SetArgument rather than here - AAP 0.4.5.4 names one-based translation
            // the single most dangerous mechanical hazard in this refactor. The result is captured because
            // with SkipInjectionWithoutArgumentSlots lifted it is how a suite sees the refusal.
            LeadingArgumentWriteResult = arguments.SetArgument(1, LeadingArgument);

            // :L56 - argPassed = 1. Reported VERBATIM, negative or oversized values included: the clamp at
            // :L610 and the Min at :L613 belong to the broker, and pre-clamping here would assert this
            // double's behaviour instead of the subject's.
            arguments.ConsumedArgumentCount = ConsumedArgumentCount;
        }

        // :L54 and :L57 - both oracle arms return 0, so both come through the one configurable normal path.
        // A prevention from here skips THIS SUBSCRIBER ONLY and leaves the rest of the dispatch running
        // (n_cst_eventful.sru:L609-L610).
        return PrepareResult;
    }
}
