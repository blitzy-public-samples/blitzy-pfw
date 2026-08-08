// =====================================================================================================
//  EventBroker.cs
//  =====================================================================================================
//  THE FULL LOGIC PORT OF THE POWERFRAMEWORK EVENT BROKER.
//
//  SINGLE AUTHORITATIVE SOURCE
//      ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru - 1,328 lines, 53 forward-declared
//      routines (46 public, 7 private) plus 4 events. Verified to contain ZERO occurrences of `native`
//      (`grep -ci native` returns 0) and its `type prototypes` block is empty, so it declares no PBNI
//      class binding and no classic external function either. AAP 0.6.5 states the same conclusion
//      structurally: the shared kernel, the DataWindow service layer and both threading libraries hold
//      zero PBNI objects. There is therefore NO native-substitution decision anywhere in this file -
//      every rule reproduced below is readable in the oracle and independently verifiable, and every
//      one carries its `:Lnnn` locator so a reviewer can check it line by line.
//
//  REFERENCE SOURCES, read as specification and never ported
//      ws_objects/pfw.thread.pbl.src/n_cst_threading_eventful.sru   the real subclass; it dictates the
//          hook shapes. :L48-L58 onprepare, :L60-L66 ontriggering, :L68-L72 ontriggered,
//          :L74-L77 constructor, :L79-L88 onexception.
//      ws_objects/pfw.thread.pbl.src/n_cst_threading.sru            consumer; :L532-L545 and :L593-L601
//          re-expose the unsubscribe surface one for one, and :L544 / :L596 carry the persistence
//          namespace rule.
//      ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru     consumer; :L6-L7 and :L33 hold the
//          nested broker instance, :L47-L76 the twelve topics, :L448-L467 the wrapper surface and
//          :L583 the destructor's bulk unsubscribe.
//      ws_objects/pfw.tests.pbl.src/w_test_eventful.srw             :L207-L249 is the authoritative
//          in-repo behavioural specification. An oracle input under AAP 0.2.2.3: never ported, never
//          edited. Where its prose and the implementation disagree the implementation wins - it already
//          disagrees once, naming a function `of_SetDefaultValue` that is actually
//          `of_SetDefaultReturnValue`.
//      ws_objects/pfw.shared.pbl.src/retcode.sru                    fixes the return-code values:
//          OK 0, PREVENT 1, FAILED -1, E_INVALID_ARGUMENT -3, E_INVALID_OBJECT -5,
//          E_EVENT_NOT_FOUND -19.
//
//  WHY THIS LIVES IN shared/ RATHER THAN IN A SERVICE, verified two independent ways
//      se_cst_dw.sru:L6-L7 declares `type eventful from n_cst_eventful within se_cst_dw` with the
//      instance member at :L33 (consumed by DataServices), and n_cst_threading_eventful.sru:L4 declares
//      `global type n_cst_threading_eventful from n_cst_eventful` (consumed by Persistence). Both in
//      scope services consume the broker, so it belongs to neither.
//
//  RULES POSITION, STATED EXPLICITLY
//      `review_rules` returns exactly one line: "No user rules provided." No user-specified rule governs
//      this file, no rule was invented to fill the gap, and that absence is NOT licence to lower the
//      bar. The AAP 0.7.2 enterprise baseline and the AAP 0.7.3 binding non-rule constraints govern in
//      their place. Every constraint that bears on this file is named below with what it required here.
//
//  C-A - SHARED IN-PROCESS IMPLEMENTATION, NOT A CROSS-SERVICE CHANNEL
//      There is no gRPC type, no HTTP type, no serialization attribute, no [DataContract], no message
//      bus abstraction and no Task-returning transport call anywhere in this file, and none may be
//      added. shared/PowerFramework.Contracts is the only published cross-service coupling in the
//      refactor and this file is not it. DataServices and Persistence each consume this broker IN
//      PROCESS through a ProjectReference; they do not talk to each other through it.
//
//  C-B - PRESERVE BEHAVIOUR EXACTLY, REPLICATE DOCUMENTED DEFECTS, NO IMPROVEMENTS
//      This is the bulk of the work. Reproduced verbatim and annotated at their point of reproduction:
//      the tri-valued veto and its once-versus-deep unwind; the negation-based lifetime filter; the
//      exception capture with its non-rollback latch; the handled state as a latch that cannot roll
//      back while the return value may still be overwritten; the next-dispatch-only visibility of
//      subscriptions added mid-dispatch; the argument truncation and default-initialisation contract;
//      and the `IsSubscribed` loop that deliberately skips the first and last slots. Where the legacy
//      looks wrong it is preserved and annotated, never corrected.
//
//  C-C - THE LEGACY TREE IS READ ONLY AND IS THE BEHAVIOURAL ORACLE
//      Every `ws_objects/**` path above was read as specification. None was edited, moved, deleted or
//      reformatted, and none is a build input of this project.
//
//  C-D - THE FOUR DEFERRED SERVICES ARE NOT IMPLEMENTED, NOT EVEN AS A STUB
//      DECISION 1, and the sharpest constraint on this file. The legacy subscription entry carries an
//      `invoker` field typed to `n_scriptinvoker` (n_cst_eventful.sru:L21) and the `onprepare` event
//      takes one (:L33). `n_scriptinvoker` belongs to the deferred ScriptBridge service, so BOTH ARE
//      DROPPED: this file declares no script-invoker type, no stand-in for one, and no reference to any
//      deferred capability, and it does not report BLOCKED. AAP 0.2.1.4 establishes why that is safe
//      rather than lossy - `n_scriptinvoker` is only a variadic-call escape hatch, because PowerScript
//      cannot forward an arbitrary-length argument list and so the legacy `choose case`-unrolls the
//      call. C# `params object?[]` covers that natively. Eventful therefore acquires NO ScriptBridge
//      coupling. The behaviour-preserving substitute is <see cref="EventArgumentContext"/>, which is
//      mandatory rather than optional: n_cst_threading_eventful.sru:L48-L58 genuinely uses the invoker
//      parameter to INJECT a leading argument and report one consumed slot, and dropping the parameter
//      without a substitute would silently destroy the threading layer's `Handler(source, ...)` calling
//      convention.
//
//  C-K - EVERY TECHNOLOGY-SPECIFIC AND BOUNDARY-SPECIFIC DECISION IS DOCUMENTED HERE, AT ITS POINT OF
//        REPRODUCTION, RATHER THAN IN A SEPARATE DOCUMENT
//      DECISION 1  the n_scriptinvoker non-port and its prepared-argument substitute - above, and on
//                  <see cref="EventArgumentContext"/> and <see cref="OnPrepare"/>.
//      DECISION 2  the message-pump non-port: `Post` becomes an explicitly queued continuation with a
//                  deterministic drain - on <see cref="Post"/> and
//                  <see cref="DrainPostedContinuations"/>.
//      DECISION 3  the arity collapse onto `params object?[]`, with 10 recorded as a LEGACY LIMIT the
//                  .NET contract may exceed without behavioural regression - on <see cref="Trigger"/>.
//      DECISION 4  the assertion-detail decoupling that keeps Kernel as the sole project reference -
//                  on <see cref="IAssertionDetail"/> and <see cref="TryReadAssertionDetail"/>.
//      DECISION 5  the `Message.PowerObjectParm` substitution - on <see cref="Current"/>.
//      DECISION 6  the `IsValid(this)` decision - on <see cref="Dispatch"/>.
//      DECISION 7  the `ex.text = ...` substitution, because `Exception.Message` is immutable in .NET -
//                  on <see cref="DispatchExceptionTextKey"/> and
//                  <see cref="GetDispatchExceptionText"/>.
//      DECISION 8  the numeric-widening value comparison that PowerScript's `any` equality performs -
//                  on <see cref="IsDifferentFrom"/>.
//      DECISION 9  the tri-state default return value that reproduces `ClassName(aDefRetVal) = "any"` -
//                  on <see cref="SetDefaultReturnValue(object?)"/>.
//      DECISION 10 the midpoint-probe rounding difference and why it is unobservable - on
//                  <see cref="Dispatch"/>.
//      DECISION 11 the class-chain walk over nested declaring types - on
//                  <see cref="BuildClassChain"/>.
//      DECISION 12 `Unsubscribe()` keeps the oracle's EMPTY filter, and the `.^persistent` rule is
//                  discharged by <see cref="BuildPersistentSparingFilter"/> - on
//                  <see cref="Unsubscribe()"/>.
//      DECISION 13 the overload ambiguity that `powerobject` mapping to `object` creates - on
//                  <see cref="Unsubscribe(object?, string)"/>.
//      DECISION 14 the disposal omission: the legacy destructor only destroyed invokers - on
//                  <see cref="EventBroker"/>.
//      DECISION 15 handler resolution by reflection as the `mid` substitute - on
//                  <see cref="ResolveHandler"/>.
//
//  THE NAMING RULING - BUILD BREAKING IF IGNORED
//      The repository root .editorconfig scopes its naming-analyzer suppressions to seven named files
//      (RetCode.cs, Enums.cs, VetoResult.cs, Categories.cs, EventGate.cs, ItemChangeProtocol.cs,
//      ClauseModifier.cs and friends). THIS FILE IS NOT IN THAT LIST, and Directory.Build.props sets
//      TreatWarningsAsErrors true. Every member declared here is therefore PascalCase, and every legacy
//      identifier spelling - `of_on`, `_of_trigger`, `EVENTDATA`, `_nDeep`, `_sFirstName` and the rest -
//      appears in documentation only. No widening of that glob list was requested.
//
//  ENTERPRISE BASELINE (AAP 0.7.2)
//      Warning-clean under warnings-as-errors, XML documentation on every public and protected member,
//      no secret in source, and shaped so the sibling shared/PowerFramework.Shared.Eventful.Tests
//      project can reach every behaviour with no I/O and no host. That last point is a second,
//      independent reason `Post` must be drainable on demand rather than fire and forget: an untestable
//      dispatch path cannot be covered to the 80% per-service gate.
//
//  THREE DISTINCT ALPHABETS SHARE THE NUMERALS 1 AND 2 AND MUST NEVER BE INTERCHANGED
//      1. the prevent state          <see cref="VetoResult"/>: Continue 0, PreventOnce 1, PreventDeep 2
//      2. the OnPrepare / OnTriggering return code   tested with Predicates.IsPrevented against
//                                                   RetCode.PREVENT, which is 1
//      3. the OnException result     <see cref="ExceptionResultPrevent"/> 1 = leave the loop,
//                                    <see cref="ExceptionResultContinue"/> 2 = clear the latch and carry
//                                    on, anything else = rethrow
//
//  ORDERING
//      Dispatch order is the ORDINAL sort of the subscription name ascending, then priority descending.
//      StringComparer.Ordinal and string.CompareOrdinal are used everywhere; a culture-sensitive
//      comparison would silently reorder dispatch. This is also why se_cst_dw.sru:L54 and :L57 spell two
//      DataWindow topics "0-itemchanged" and "1-editchanged" - see the trap documented on
//      SubscriptionTopic.LegacyName, where the '-' is part of the NAME and is not the prepend symbol.
// =====================================================================================================

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text;

using PowerFramework.Shared.Kernel;

namespace PowerFramework.Shared.Eventful
{
    /// <summary>
    /// One row of the broker's subscription table: the port of the legacy <c>EVENTDATA</c> structure
    /// (<c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L12-L25</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The legacy structure declares <b>twelve</b> fields. <b>Eleven are ported and exactly one is
    /// dropped</b>: <c>n_scriptinvoker invoker</c> (<c>:L21</c>), under AAP 0.7.3 C-D, because
    /// <c>n_scriptinvoker</c> belongs to the deferred ScriptBridge service. Nothing here stands in for it
    /// as a script invoker; the argument-passing behaviour it carried lives in
    /// <see cref="EventArgumentContext"/> and the method identity it resolved lives in
    /// <see cref="Handler"/>. See DECISION 1 in the file header.
    /// </para>
    /// <para>
    /// Modelled as a mutable class rather than a record because the legacy writes three of its fields
    /// <i>in place</i> during dispatch and during the modification engine: <see cref="IsInvoking"/> at
    /// <c>:L865</c> and <c>:L908</c>, <see cref="IsInvalid"/> at <c>:L1057</c> and
    /// <see cref="IsDisabled"/> at <c>:L1070</c>. A record with value semantics would have made those
    /// writes invisible to the table and broken tombstoning outright.
    /// </para>
    /// </remarks>
    public sealed class EventSubscription
    {
        /// <summary>
        /// The subscription namespace - the port of <c>EVENTDATA.ns</c> (<c>:L13</c>), assigned at
        /// <c>:L377</c>. Empty when the topic carried no namespace. Never <see langword="null"/>.
        /// </summary>
        /// <remarks>
        /// Compared ordinally by the modification engine, so <c>"Persistent"</c> is a different namespace
        /// from <c>"persistent"</c> and a bulk unsubscribe sweeps it. Use
        /// <see cref="SubscriptionOptions.NamespaceComparer"/> when comparing it elsewhere.
        /// </remarks>
        public string Namespace { get; init; } = SubscriptionNamespaces.None;

        /// <summary>
        /// <b>The residual event name, and therefore the dispatch key and the ordinal sort key</b> - the
        /// port of <c>EVENTDATA.name</c> (<c>:L14</c>) as narrowed by <c>:L363</c>, <c>:L371</c> and
        /// <c>:L378</c>. Never <see langword="null"/>.
        /// </summary>
        /// <remarks>
        /// This is <see cref="SubscriptionTopic.LegacyName"/>, <b>not</b>
        /// <see cref="SubscriptionTopic.LogicalName"/>. For the topic <c>"0-itemchanged"</c> it is
        /// <c>"0-itemchanged"</c> with the prefix included, because the leading symbol run ends at the
        /// first character that is not a symbol and <c>'0'</c> ends it. Confusing the two silently
        /// destroys dispatch ordering.
        /// </remarks>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Which handled states this subscription captures - the port of <c>EVENTDATA.capture</c>
        /// (<c>:L15</c>), whose legacy values are <c>CAP_UNHANDLED</c> 0, <c>CAP_HANDLED</c> 1 and
        /// <c>CAP_ALL</c> 2 (<c>:L100-L102</c>).
        /// </summary>
        /// <remarks>
        /// Consumed by the capture filter at <c>:L831-L833</c>: a subscription whose mode differs from
        /// the dispatch's current handled state is skipped <i>unless</i> its mode is
        /// <see cref="CaptureMode.All"/>.
        /// </remarks>
        public CaptureMode Capture { get; init; } = CaptureMode.Unhandled;

        /// <summary>
        /// The dispatch priority within an equal name - the port of <c>EVENTDATA.priority</c>
        /// (<c>:L16</c>). Higher dispatches first.
        /// </summary>
        /// <remarks>
        /// The legacy field is a <c>long</c> holding one of <c>PRIORITY_LOW</c>
        /// (<c>-2147483648</c>), <c>PRIORITY_NORMAL</c> (<c>0</c>) or <c>PRIORITY_HIGH</c>
        /// (<c>2147483647</c>) or a caller-supplied number (<c>:L104-L106</c>). Those bounds are exactly
        /// the 32-bit limits, which is why <see cref="Priorities"/> types them as <see cref="int"/> and
        /// why priority is <i>compared</i> rather than subtracted anywhere it is ordered.
        /// </remarks>
        public int Priority { get; init; } = Priorities.Normal;

        /// <summary>
        /// The callback target - the port of <c>EVENTDATA.object</c> (<c>:L17</c>), whose legacy type is
        /// <c>powerobject</c> and whose target form is <see cref="object"/> per AAP 0.4.5.2.
        /// </summary>
        /// <remarks>
        /// Held as a strong reference, exactly as the legacy holds a pointer. A subscription is only ever
        /// created for a target that passed <c>Predicates.IsValidObject</c> at <c>:L332</c>, so this is
        /// non-null for every entry the table contains; it is nonetheless typed nullable so the validity
        /// re-checks at <c>:L387</c>, <c>:L589</c> and <c>:L834</c> read exactly as the oracle reads.
        /// </remarks>
        public object? Target { get; init; }

        /// <summary>
        /// The target's class chain, <c>parent/.../child</c> - the port of <c>EVENTDATA.clschain</c>
        /// (<c>:L18</c>), built at <c>:L405</c> by <c>_of_getobjectclasschain</c>
        /// (<c>:L976-L991</c>).
        /// </summary>
        /// <remarks>
        /// <b>Its only consumer is the exception text</b> (<c>:L884</c>). It takes no part in dispatch,
        /// in ordering or in matching, and a reader who mistakes it for dispatch logic will look for a
        /// mechanism that is not there. See <see cref="EventBroker.BuildClassChain"/> for how the
        /// containment walk is expressed in .NET.
        /// </remarks>
        public string ClassChain { get; init; } = string.Empty;

        /// <summary>
        /// The callback member name, <b>stored lower-cased</b> - the port of <c>EVENTDATA.evtname</c>
        /// (<c>:L19</c>), folded at <c>:L336</c>.
        /// </summary>
        /// <remarks>
        /// The fold is behaviour, not tidiness: <c>w_test_eventful.srw:L250</c> records that the topic
        /// name is case-SENSITIVE while the handler name is case-INSENSITIVE, and the modification engine
        /// folds its own argument the same way at <c>:L998</c> so the two sides meet.
        /// </remarks>
        public string HandlerName { get; init; } = string.Empty;

        /// <summary>
        /// The resolved handler identity - the port of <c>EVENTDATA.mid</c> (<c>:L20</c>), which the
        /// legacy fills from <c>invoker.Init(object, evtName, evtSign, ScriptEvent!)</c> at
        /// <c>:L399</c>.
        /// </summary>
        /// <remarks>
        /// A <see cref="MethodInfo"/> is the .NET analogue of a PowerBuilder method id: resolved once at
        /// subscribe time, re-checked before each invocation exactly as the legacy re-runs
        /// <c>invoker.Init(object, mid, ScriptEvent!)</c> at <c>:L859</c>. See
        /// <see cref="EventBroker.ResolveHandler"/> (DECISION 15).
        /// </remarks>
        public MethodInfo? Handler { get; init; }

        /// <summary>
        /// The re-entrancy flag - the port of <c>EVENTDATA.invoking</c> (<c>:L22</c>). Set at
        /// <c>:L865</c> and <b>restored to its captured previous value</b>, not to
        /// <see langword="false"/>, at <c>:L908</c>.
        /// </summary>
        /// <remarks>
        /// The restore-to-previous is deliberate and is preserved: a re-entrant invocation must leave the
        /// flag set for the outer invocation that is still on the stack.
        /// </remarks>
        public bool IsInvoking { get; set; }

        /// <summary>
        /// Whether the subscription is suspended - the port of <c>EVENTDATA.disabled</c> (<c>:L23</c>),
        /// assigned by the modification engine at <c>:L1070</c> and honoured at <c>:L830</c>.
        /// </summary>
        /// <remarks>
        /// A disabled subscription is skipped by dispatch and excluded from the lexical bounds, but it is
        /// never removed; re-enabling it restores it in place with its original ordering.
        /// </remarks>
        public bool IsDisabled { get; set; }

        /// <summary>
        /// The deferred-removal tombstone - the port of <c>EVENTDATA.invalid</c> (<c>:L24</c>), set at
        /// <c>:L1057</c> when an unsubscribe lands while a dispatch is in flight and honoured at
        /// <c>:L829</c>.
        /// </summary>
        /// <remarks>
        /// Removing an entry mid-dispatch would invalidate the index stack every active dispatch level
        /// holds a cursor in, so the legacy marks and defers instead. The compaction is queued through
        /// the same mechanism as a posted dispatch (<c>:L1083</c>).
        /// </remarks>
        public bool IsInvalid { get; set; }

        /// <summary>
        /// The lifetime <see cref="Namespace"/> projects onto: whether a <c>".^persistent"</c> bulk
        /// unsubscribe sweeps this subscription away or spares it.
        /// </summary>
        /// <value>
        /// <see cref="SubscriptionLifetime.Persistent"/> when <see cref="Namespace"/> is ordinally equal
        /// to <see cref="SubscriptionNamespaces.Persistent"/>; otherwise
        /// <see cref="SubscriptionLifetime.Transient"/>.
        /// </value>
        /// <remarks>
        /// A projection, and intentionally lossy - <see cref="Namespace"/> stays authoritative. It is
        /// carried because AAP 0.6.1.2 requires lifetime to be a field in its own right rather than a
        /// substring of a fused topic string. Nothing in this file dispatches or matches on it; the
        /// filter grammar works on the namespace itself, which is precisely what makes the negation in
        /// <c>".^persistent"</c> the load-bearing part rather than a lifetime flag.
        /// </remarks>
        public SubscriptionLifetime Lifetime => SubscriptionNamespaces.ToLifetime(Namespace);
    }

    /// <summary>
    /// One registered per-name default return value: the port of the legacy <c>RETVALUEDATA</c> structure
    /// (<c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L27-L30</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two fields, both ported: <see cref="Name"/> and <see cref="Value"/>. A record is right here
    /// because the legacy never mutates the structure through an alias - it either rewrites the array
    /// slot at <c>:L508</c> or appends a fresh one at <c>:L514</c>, and <c>with</c> expresses the first
    /// of those exactly.
    /// </para>
    /// <para>
    /// The default return value is not decoration. It is the yardstick the broker measures "was this
    /// event handled?" against: a subscriber's return value that differs from the default marks the
    /// event handled, and one that equals it does not (<c>w_test_eventful.srw:L234</c>). That is why
    /// <c>n_cst_threading_eventful.sru:L74-L77</c> installs <c>0</c> from its constructor, and why the
    /// legacy documentation advises always setting one (<c>w_test_eventful.srw:L257</c>).
    /// </para>
    /// <para>
    /// <b>Note for readers of the subclass:</b> <c>n_cst_threading_eventful</c> re-declares its own
    /// nested <c>retvaluedata</c> structure (<c>:L6-L7</c>). That is a flat-namespace artifact of
    /// PowerBuilder - a derived object shadowing an inherited structure name - with no C# analogue and no
    /// behaviour attached. It is deliberately <b>not</b> reproduced.
    /// </para>
    /// </remarks>
    public sealed record DefaultReturnValue
    {
        /// <summary>
        /// The event name this default applies to - the port of <c>RETVALUEDATA.name</c>
        /// (<c>:L28</c>). Compared ordinally at <c>:L507</c> and <c>:L547</c>.
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// The default value itself - the port of <c>RETVALUEDATA.value</c> (<c>:L29</c>), whose legacy
        /// type is <c>any</c> and whose target form is <c>object?</c> per AAP 0.4.5.2.
        /// </summary>
        /// <remarks>
        /// <see langword="null"/> is a legal, meaningful value: it selects the first arm of the
        /// handled-detection logic at <c>:L912</c>, under which any non-null return marks the event
        /// handled. See DECISION 9 on <see cref="EventBroker.SetDefaultReturnValue(object?)"/> for the
        /// third state - "no default has been established at all" - which the legacy expresses as
        /// <c>ClassName(aDefRetVal) = "any"</c> and which is tracked separately for the global default.
        /// </remarks>
        public object? Value { get; init; }
    }

    /// <summary>
    /// The argument buffer an <see cref="EventBroker.OnPrepare"/> override may adjust before a handler is
    /// invoked: <b>the invoker-free substitute for the <c>n_scriptinvoker</c> parameter the legacy
    /// <c>onprepare</c> event receives</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>DECISION 1 (AAP 0.7.3 C-D and C-K).</b> The legacy signature is
    /// <c>onprepare(string name, powerobject target, n_scriptinvoker invoker, integer argcount,
    /// ref integer argpassed)</c> (<c>n_cst_eventful.sru:L33</c>). <c>n_scriptinvoker</c> belongs to the
    /// deferred ScriptBridge service, so it is dropped - and dropping it without a substitute would be a
    /// silent behavioural regression, because the real subclass <i>uses</i> it. At
    /// <c>n_cst_threading_eventful.sru:L48-L58</c> the override does:
    /// </para>
    /// <code>
    /// if argCount &lt; 1 or target = _source then return 0
    /// invoker.SetArg(1,_source)
    /// argPassed = 1
    /// return 0
    /// </code>
    /// <para>
    /// - it <b>injects a leading argument</b> (the source object) ahead of the trigger's own arguments
    /// and reports that it consumed one argument slot, which is how the threading layer's
    /// <c>Handler(source, ...)</c> calling convention is established. This type carries exactly those
    /// two capabilities and nothing else: <see cref="DeclaredArgumentCount"/> is the legacy
    /// <c>argcount</c>, <see cref="SetArgument"/> is <c>invoker.SetArg</c> with the same <b>one-based</b>
    /// slot numbering, and <see cref="ConsumedArgumentCount"/> is the legacy <c>ref argpassed</c>. It
    /// carries no script invocation, no dynamic dispatch and no reference to any deferred capability.
    /// </para>
    /// <para>
    /// Instances are created by the broker and handed to the override for the duration of one call only.
    /// The buffer it wraps is the array the handler is invoked with, so a write through
    /// <see cref="SetArgument"/> is immediately effective and no copy-back step exists to forget.
    /// </para>
    /// </remarks>
    public sealed class EventArgumentContext
    {
        private readonly object?[] _slots;

        /// <summary>
        /// Wraps the argument buffer for one invocation.
        /// </summary>
        /// <param name="slots">
        /// The buffer the handler will be invoked with, already pre-filled with each parameter's
        /// PowerScript initial value. Its length is the handler's declared argument count.
        /// </param>
        internal EventArgumentContext(object?[] slots)
        {
            _slots = slots;
        }

        /// <summary>
        /// The number of arguments the resolved handler declares - the port of the legacy
        /// <c>argcount</c> parameter, which <c>_of_passargs</c> reads from
        /// <c>invoker.GetArgCount()</c> (<c>n_cst_eventful.sru:L607</c>).
        /// </summary>
        /// <remarks>
        /// <c>n_cst_threading_eventful.sru:L54</c> guards on it (<c>if argCount &lt; 1 ... then return
        /// 0</c>), so an override that injects a leading argument must be able to see that there is a
        /// slot to inject into.
        /// </remarks>
        public int DeclaredArgumentCount => _slots.Length;

        /// <summary>
        /// How many leading argument slots the override has filled itself, and therefore how many the
        /// broker must skip when it copies the trigger's own arguments in - the port of the legacy
        /// <c>ref integer argpassed</c> out-parameter (<c>:L33</c>, read at <c>:L610</c> and
        /// <c>:L613</c>).
        /// </summary>
        /// <value>
        /// Zero by default. <c>n_cst_threading_eventful.sru:L56</c> sets it to <c>1</c> after injecting
        /// the source object.
        /// </value>
        /// <remarks>
        /// A negative value is <b>clamped to zero</b> by the broker, reproducing <c>:L610</c>
        /// (<c>if nArgIdx &lt;= 0 then nArgIdx = 0</c>) rather than validating it away. A value larger
        /// than <see cref="DeclaredArgumentCount"/> leaves no room for the trigger's arguments and none
        /// are copied, which is <c>:L613</c>'s <c>Min</c> doing its job.
        /// </remarks>
        public int ConsumedArgumentCount { get; set; }

        /// <summary>
        /// Writes one argument slot, addressed <b>one-based</b> exactly as the legacy
        /// <c>invoker.SetArg(1, _source)</c> addresses it
        /// (<c>n_cst_threading_eventful.sru:L55</c>).
        /// </summary>
        /// <param name="slot">The one-based slot index, from 1 to <see cref="DeclaredArgumentCount"/>.</param>
        /// <param name="value">The value to place in that slot. <see langword="null"/> is permitted.</param>
        /// <returns>
        /// <see cref="RetCode.OK"/> when the slot was written, or <see cref="RetCode.E_INVALID_ARGUMENT"/>
        /// when <paramref name="slot"/> lies outside the declared range.
        /// </returns>
        /// <remarks>
        /// One-based numbering is kept rather than "corrected" to zero-based because it is the contract
        /// the only real override in the corpus is written against, and because AAP 0.4.5.4 names
        /// one-based-to-zero-based translation the single most dangerous mechanical hazard in this
        /// refactor. The conversion happens here, once, instead of at every call site.
        /// </remarks>
        public long SetArgument(int slot, object? value)
        {
            if (slot < 1 || slot > _slots.Length)
            {
                return RetCode.E_INVALID_ARGUMENT;
            }

            _slots[slot - 1] = value;
            return RetCode.OK;
        }

        /// <summary>
        /// Reads one argument slot, addressed <b>one-based</b> to match <see cref="SetArgument"/>.
        /// </summary>
        /// <param name="slot">The one-based slot index, from 1 to <see cref="DeclaredArgumentCount"/>.</param>
        /// <returns>
        /// The slot's current value, which before any write is the parameter type's PowerScript initial
        /// value; or <see langword="null"/> when <paramref name="slot"/> lies outside the declared range.
        /// </returns>
        /// <remarks>
        /// Provided so an override can inspect what it is about to displace. The legacy invoker exposes
        /// the same read access, and without it an override could only write blind.
        /// </remarks>
        public object? GetArgument(int slot)
        {
            if (slot < 1 || slot > _slots.Length)
            {
                return null;
            }

            return _slots[slot - 1];
        }
    }

    /// <summary>
    /// The two detail members the broker reads off an assertion failure when it decorates a captured
    /// exception: <b>the substitute for the legacy's dynamic downcast to <c>assertionfailed</c></b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>DECISION 4 (AAP 0.7.3 C-K).</b> The legacy exception path special-cases one exception type. At
    /// <c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L873-L880</c> it tests
    /// <c>ClassName(ex) = "assertionfailed"</c> - a <b>string class-name comparison</b>, not a static
    /// type test - then downcasts and reads two members:
    /// </para>
    /// <code>
    /// if ClassName(ex) = "assertionfailed" then
    ///     assertionfailed f
    ///     f = ex
    ///     sException = f.#Info + "~nStackTrace:~n" + f.#StackTraceInfo
    /// else
    ///     sException = "[" + ClassName(ex) + "]~n" + sException
    /// end if
    /// </code>
    /// <para>
    /// <c>assertionfailed.sru</c> maps to <c>shared/PowerFramework.Shared.Diagnostics/AssertionFailure.cs</c>
    /// and <b>not</b> to Kernel. Adding a <c>ProjectReference</c> to Diagnostics would contradict this
    /// project's measured single dependency - counted over all 1,328 oracle lines, the broker consumes
    /// only <c>RetCode</c>, <c>Predicates</c> and the throw helper, all of which land in Kernel - and
    /// would couple two shared libraries for a coupling the legacy never expressed in its own type
    /// system. So the mechanism is reproduced rather than the dependency: this interface is the static
    /// half, and <see cref="EventBroker.TryReadAssertionDetail"/> adds the late-bound half that mirrors
    /// the legacy's own string class-name test. Kernel remains the sole project reference.
    /// </para>
    /// <para>
    /// Any assertion type can satisfy this contract, which is the point: the broker asks for two strings
    /// and does not care which library supplies them.
    /// </para>
    /// </remarks>
    public interface IAssertionDetail
    {
        /// <summary>
        /// The decoded assertion description - the analogue of the legacy <c>#Info</c> member read at
        /// <c>n_cst_eventful.sru:L876</c>.
        /// </summary>
        string Info { get; }

        /// <summary>
        /// The captured call stack in text form - the analogue of the legacy <c>#StackTraceInfo</c>
        /// member read at <c>n_cst_eventful.sru:L876</c>.
        /// </summary>
        string StackTraceInfo { get; }
    }

    /// <summary>
    /// The PowerFramework publish and subscribe event broker: the full logic port of
    /// <c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Subscriptions are held in one table ordered by name ascending (<b>ordinal</b>) and then by
    /// priority descending. <see cref="Trigger"/> walks that table, invokes every matching subscription
    /// in order, tracks whether the event has been handled, and returns the last handler's value. Four
    /// <see langword="protected"/> <see langword="virtual"/> hooks let a derived broker take part in the
    /// dispatch, and <see cref="Prevent(bool)"/> lets a handler stop it - once, or for the whole nested
    /// chain.
    /// </para>
    /// <para>
    /// <b>Deliberately neither <see langword="sealed"/> nor <see langword="static"/>: it must be
    /// derivable.</b> <c>ws_objects/pfw.thread.pbl.src/n_cst_threading_eventful.sru:L4</c> declares
    /// <c>global type n_cst_threading_eventful from n_cst_eventful</c> and overrides all four hooks, so
    /// sealing this type would make the Persistence threading layer unportable.
    /// </para>
    /// <para>
    /// <b>The four hooks fire only when the instance is of a DERIVED type.</b> The legacy constructor
    /// computes <c>_bSubclassing = (ClassName(this) &lt;&gt; "n_cst_eventful")</c> (<c>:L1324</c>) and
    /// every hook call site is gated on it (<c>:L608</c>, <c>:L839</c>, <c>:L888</c>, <c>:L944</c>). In
    /// C# that becomes <c>GetType() != typeof(EventBroker)</c>. A port that always invoked the hooks
    /// would change base-broker dispatch behaviour, so the gate is reproduced literally.
    /// </para>
    /// <para>
    /// <b>DECISION 14 - no <see cref="IDisposable"/>, and that omission is a decision rather than an
    /// oversight.</b> The legacy destructor (<c>:L1315-L1321</c>) does exactly one thing: it walks the
    /// subscription table destroying each entry's <c>n_scriptinvoker</c>. With the invoker dropped under
    /// C-D there is nothing unmanaged left to release - a <see cref="MethodInfo"/> and an
    /// <see cref="object"/> reference are both collected - so implementing
    /// <see cref="IDisposable"/> purely to mirror a destructor that now has no work would add a
    /// lifecycle contract the legacy never had, and every consumer would have to honour it.
    /// </para>
    /// <para>
    /// <b>Thread affinity.</b> The legacy object is not synchronised and neither is this port: one broker
    /// instance belongs to one logical thread of control, exactly as
    /// <c>ws_objects/pfw.thread.pbl.src/n_cst_threading.sru</c> gives each thread its own broker. The
    /// dispatch depth, the index stack and the accumulated return value are per-instance mutable state,
    /// so concurrent calls on one instance are outside the contract - as they were in PowerBuilder, where
    /// AAP 0.4.5.4 records that the thread-affinity annotations are contract rather than commentary.
    /// </para>
    /// </remarks>
    public class EventBroker
    {
        /// <summary>
        /// The label the exception decoration uses for its fourth line, and the alignment string the
        /// indentation helper is driven by - the literal <c>"Exception: ~n"</c> of
        /// <c>n_cst_eventful.sru:L881</c> and <c>:L886</c>.
        /// </summary>
        /// <remarks>
        /// One constant serves both sites because the oracle uses the same literal at both. Its length is
        /// twelve, so the indentation helper derives six spaces from it. Changing it changes both the
        /// visible label and the indentation width, which is exactly the coupling the legacy has.
        /// </remarks>
        private const string ExceptionLabel = "Exception: \n";

        /// <summary>
        /// The legacy spelling of the assertion-failure class name, as
        /// <c>n_cst_eventful.sru:L873</c> compares it.
        /// </summary>
        private const string LegacyAssertionTypeName = "assertionfailed";

        /// <summary>
        /// The .NET spelling of the same type, which AAP 0.4.2.3 maps
        /// <c>assertionfailed.sru</c> onto: <c>PowerFramework.Shared.Diagnostics.AssertionFailure</c>.
        /// </summary>
        private const string PortedAssertionTypeName = "AssertionFailure";

        /// <summary>
        /// The <c>Info</c> member name the late-bound assertion read looks for - the analogue of the
        /// legacy <c>#Info</c>.
        /// </summary>
        private const string AssertionInfoMemberName = "Info";

        /// <summary>
        /// The <c>StackTraceInfo</c> member name the late-bound assertion read looks for - the analogue
        /// of the legacy <c>#StackTraceInfo</c>.
        /// </summary>
        private const string AssertionStackTraceMemberName = "StackTraceInfo";

        /// <summary>
        /// The ambient "broker currently dispatching" slot - <b>the substitute for the PowerBuilder
        /// runtime global <c>Message.PowerObjectParm</c></b>.
        /// </summary>
        /// <remarks>
        /// See <see cref="Current"/> for DECISION 5 in full.
        /// </remarks>
        private static readonly AsyncLocal<EventBroker?> CurrentBroker = new();

        /// <summary>
        /// The subscription table - the port of <c>EVENTDATA Events[]</c>
        /// (<c>n_cst_eventful.sru:L71</c>). Ordered by name ascending (ordinal) and then priority
        /// descending; <see cref="Subscribe(string, object?, string, string)"/> maintains that order on
        /// insert and nothing ever re-sorts it.
        /// </summary>
        private readonly List<EventSubscription> _subscriptions = [];

        /// <summary>
        /// The per-name default return values - the port of <c>RETVALUEDATA DefRetValues[]</c>
        /// (<c>:L72</c>). Searched linearly and first-match-wins, exactly as <c>:L546-L550</c> searches
        /// it; there is no index and none is needed for the handful of entries the corpus registers.
        /// </summary>
        private readonly List<DefaultReturnValue> _defaultReturnValues = [];

        /// <summary>
        /// The index stack: one dispatch cursor per active dispatch level - the port of
        /// <c>int _nIdxStack[]</c> (<c>:L78</c>).
        /// </summary>
        /// <remarks>
        /// <b>A stack rather than a scalar, because dispatch is re-entrant.</b> The legacy runs its loop
        /// directly on <c>_nIdxStack[_nDeep]</c> (<c>:L820</c>), so each nesting level keeps its own
        /// position, and <c>of_on</c> can reach into every active level to fix a cursor up when it
        /// inserts ahead of it (<c>:L427-L433</c>). Element <c>index</c> holds the cursor for depth
        /// <c>index + 1</c>; the list only ever grows, mirroring PowerScript's array growth, so a nested
        /// dispatch that has unwound leaves its slot behind for the next one to reuse.
        /// </remarks>
        private readonly List<int> _indexStack = [];

        /// <summary>
        /// The queued continuations a <see cref="Post"/> or a deferred compaction has left for the host
        /// to run - <b>the substitute for the Win32 message queue the legacy posts to</b>.
        /// </summary>
        /// <remarks>
        /// See <see cref="Post"/> and <see cref="DrainPostedContinuations"/> for DECISION 2 in full.
        /// </remarks>
        private readonly Queue<Action> _postedContinuations = new();

        /// <summary>
        /// This instance's runtime class name - the port of <c>string _sThisClsName</c> (<c>:L74</c>),
        /// captured in the constructor at <c>:L1323</c> from <c>ClassName(this)</c>.
        /// </summary>
        /// <remarks>
        /// Its only consumer is the outermost-level exception prefix at <c>:L900</c>.
        /// </remarks>
        private readonly string _thisClassName;

        /// <summary>
        /// Whether this instance is of a derived type, and therefore whether the four hooks fire - the
        /// port of <c>boolean _bSubclassing</c> (<c>:L75</c>), computed at <c>:L1324</c>.
        /// </summary>
        private readonly bool _subclassing;

        /// <summary>
        /// The current dispatch depth - the port of <c>long _nDeep</c> (<c>:L77</c>). Zero outside a
        /// dispatch, one inside the outermost, and one more per nesting level.
        /// </summary>
        /// <remarks>
        /// It gates far more than bookkeeping: the garbage sweep at <c>:L385</c>, the compaction at
        /// <c>:L580</c>, both default-value setters at <c>:L500</c> and <c>:L536</c>,
        /// <see cref="Prevent(bool)"/> at <c>:L1293</c>, the tombstone-versus-remove branch at
        /// <c>:L1055</c> and the exception-latch reset at <c>:L959</c> all key on it.
        /// </remarks>
        private long _deep;

        /// <summary>
        /// The global default return value - the port of <c>any _aDefRetVal</c> (<c>:L80</c>), set to
        /// null by the constructor at <c>:L1325</c>.
        /// </summary>
        private object? _globalDefaultReturnValue;

        /// <summary>
        /// Whether a global default return value has ever been <i>established</i>, as distinct from
        /// having been established as <see langword="null"/> - the port of the legacy's
        /// <c>ClassName(aDefRetVal) = "any"</c> test at <c>:L914</c>.
        /// </summary>
        /// <remarks>
        /// See DECISION 9 on <see cref="SetDefaultReturnValue(object?)"/>. It starts
        /// <see langword="false"/> because the constructor's <c>SetNull</c> leaves the PowerScript
        /// <c>any</c> without a datatype, which is exactly the state that test detects.
        /// </remarks>
        private bool _globalDefaultReturnValueEstablished;

        /// <summary>
        /// The accumulated return value of the dispatch in progress - the port of <c>any _aRetVal</c>
        /// (<c>:L81</c>), read by <see cref="GetReturnValue"/> and tested by
        /// <see cref="IsProcessed"/>.
        /// </summary>
        /// <remarks>
        /// Saved and restored around every dispatch (<c>:L815-L816</c> and <c>:L951</c>), so it is scoped
        /// to the level that is running. An enclosing dispatch therefore sees its own value again once an
        /// inner dispatch completes - see the remarks on <see cref="GetReturnValue"/>.
        /// </remarks>
        private object? _returnValue;

        /// <summary>
        /// Whether the dispatch in progress was posted rather than triggered - the port of
        /// <c>boolean _bIsPost</c> (<c>:L83</c>), read by <see cref="IsPost"/>.
        /// </summary>
        private bool _isPost;

        /// <summary>
        /// The exception-decoration latch - the port of <c>boolean _bHasException</c> (<c>:L84</c>).
        /// </summary>
        /// <remarks>
        /// Set the first time an exception is decorated (<c>:L872</c>) so a nested rethrow is not
        /// decorated twice, cleared when an <see cref="OnException"/> override answers
        /// <see cref="ExceptionResultContinue"/> (<c>:L893</c>), and cleared again only once the depth
        /// returns to zero (<c>:L959-L961</c>).
        /// </remarks>
        private bool _hasException;

        /// <summary>
        /// The prevent state of the dispatch in progress - the port of <c>long _nPrevent</c>
        /// (<c>:L85</c>), whose legacy values are <c>PREVENT_ONCE</c> 1 and <c>PREVENT_DEEP</c> 2 with
        /// zero meaning "carry on" (<c>:L111-L112</c>).
        /// </summary>
        /// <remarks>
        /// Typed <see cref="VetoResult"/> and <b>never flattened to a boolean</b>: flattening would
        /// silently convert a deep prevention into a shallow one, which is precisely the distinction the
        /// unwind at <c>:L954-L958</c> exists to keep.
        /// </remarks>
        private VetoResult _prevent;

        /// <summary>
        /// The ordinally smallest subscription name currently enabled - the port of
        /// <c>string _sFirstName</c> (<c>:L87</c>).
        /// </summary>
        /// <remarks>
        /// One half of the lexical bounds. They are a fast reject, and they are the reason the legacy
        /// documentation advises short event names (<c>w_test_eventful.srw:L238</c>): a dispatch whose
        /// name falls outside them returns without scanning the table at all (<c>:L797-L798</c>). While
        /// the table is empty both bounds are the empty string, and a non-empty name is then ordinally
        /// greater than <see cref="_lastName"/>, so the second reject fires and nothing is dispatched.
        /// </remarks>
        private string _firstName = string.Empty;

        /// <summary>
        /// The ordinally largest subscription name currently enabled - the port of
        /// <c>string _sLastName</c> (<c>:L88</c>). See <see cref="_firstName"/>.
        /// </summary>
        private string _lastName = string.Empty;

        /// <summary>
        /// Creates an event broker with an empty subscription table and no default return value.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The port of the legacy <c>constructor</c> event (<c>n_cst_eventful.sru:L1323-L1327</c>), which
        /// does four things:
        /// </para>
        /// <code>
        /// _sThisClsName = ClassName(this)
        /// _bSubclassing = (_sThisClsName &lt;&gt; "n_cst_eventful")
        /// SetNull(_aDefRetVal)
        /// _aRetVal = _aDefRetVal
        /// </code>
        /// <para>
        /// <c>ClassName(this)</c> becomes <c>GetType().Name</c> and the subclassing test becomes
        /// <c>GetType() != typeof(EventBroker)</c> - a type identity comparison rather than a string
        /// comparison, which is stricter in the one way that matters: a derived type that happened to be
        /// named <c>EventBroker</c> in another namespace still counts as derived.
        /// </para>
        /// <para>
        /// <b>Both are computed here and stored, not computed on demand</b>, because
        /// <c>GetType()</c> inside a base constructor already reports the most-derived runtime type. That
        /// is what lets a derived constructor rely on the gate immediately - and
        /// <c>n_cst_threading_eventful.sru:L74-L77</c> does exactly that, calling
        /// <c>of_SetDefaultReturnValue(0)</c> from its own constructor and thereby installing a non-null
        /// global default that selects a different arm of the handled-detection logic for the whole
        /// lifetime of the instance.
        /// </para>
        /// </remarks>
        public EventBroker()
        {
            // :L1323 - _sThisClsName = ClassName(this)
            _thisClassName = GetType().Name;

            // :L1324 - _bSubclassing = (_sThisClsName <> "n_cst_eventful")
            _subclassing = GetType() != typeof(EventBroker);

            // :L1325 - SetNull(_aDefRetVal). The value is null AND no default has been established; the
            // legacy's own ClassName(aDefRetVal) = "any" test at :L914 detects the latter, so both flags
            // start in the state that test implies. See DECISION 9.
            _globalDefaultReturnValue = null;
            _globalDefaultReturnValueEstablished = false;

            // :L1326 - _aRetVal = _aDefRetVal. Seeded from the default rather than assigned null
            // directly, which is the same value and is written this way to match the oracle statement.
            _returnValue = _globalDefaultReturnValue;
        }

        /// <summary>
        /// The broker whose dispatch is currently on the stack, or <see langword="null"/> when no
        /// dispatch is in progress on this execution context - <b>the substitute for the PowerBuilder
        /// runtime global <c>Message.PowerObjectParm</c></b>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>DECISION 5 (AAP 0.7.3 C-K).</b> On the first matching subscription of a dispatch the legacy
        /// snapshots <c>Message.PowerObjectParm</c> and overwrites it with <c>this</c>
        /// (<c>n_cst_eventful.sru:L846-L847</c>), then restores the snapshot once the loop has finished
        /// (<c>:L948</c>). <c>Message</c> is a PowerBuilder runtime global with no .NET analogue: it is
        /// per-thread ambient state that a handler reads to discover which broker called it, most usefully
        /// so the handler can call <see cref="Prevent(bool)"/> or <see cref="GetReturnValue"/> without
        /// having been handed a reference.
        /// </para>
        /// <para>
        /// An <see cref="AsyncLocal{T}"/> is the closest faithful expression: ambient, scoped to the
        /// logical thread of control, flowing into continuations rather than leaking across unrelated
        /// threads, and saved and restored by the dispatch exactly where the oracle saves and restores it.
        /// A plain static field would leak between threads - which matters here, because the threading
        /// layer gives every thread its own broker. Restoration happens in the dispatch's outer
        /// <see langword="finally"/>, so it survives an exception leaving the loop.
        /// </para>
        /// <para>
        /// Note the legacy restores it <i>only when at least one subscription matched</i> (<c>:L942</c>
        /// guards <c>:L948</c>), because it is only overwritten under the same condition. That asymmetry
        /// is preserved.
        /// </para>
        /// </remarks>
        public static EventBroker? Current => CurrentBroker.Value;

        /// <summary>
        /// The <see cref="System.Exception.Data"/> key under which a dispatch records the four-line
        /// diagnostic block it builds for a captured exception.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>DECISION 7 (AAP 0.7.3 C-K) - the substitute for PowerBuilder's assignable
        /// <c>throwable.text</c>.</b> The legacy replaces the caught exception's message outright
        /// (<c>n_cst_eventful.sru:L883-L886</c>) and rethrows the same object. .NET has no equivalent:
        /// <see cref="System.Exception.Message"/> is not settable, and the two nearest alternatives were
        /// both rejected for concrete reasons.
        /// </para>
        /// <list type="bullet">
        ///   <item><description>
        ///   <b>Rejected - call a <c>SetMessage</c> member reflectively.</b>
        ///   <c>PowerFramework.Shared.Kernel.PfwException.SetMessage</c> DECORATES what it is given with
        ///   a <c>"PowerFramework Runtime Error"</c> prefix, which the legacy's plain <c>.text =</c>
        ///   assignment does not add. Using it would inject a line the oracle never emits, and the
        ///   four-line block is required byte for byte.
        ///   </description></item>
        ///   <item><description>
        ///   <b>Rejected - wrap the exception in a new one carrying the text.</b> That changes the runtime
        ///   type an enclosing <see langword="catch"/> matches. The legacy rethrows the same instance and
        ///   never changes its type, so wrapping would be a behavioural change introduced by the port.
        ///   </description></item>
        /// </list>
        /// <para>
        /// So the block is recorded on <see cref="System.Exception.Data"/>, which is always writable and
        /// never affects type or stack, and the ORIGINAL instance is rethrown with a bare
        /// <see langword="throw"/> so its stack trace survives. <see cref="GetDispatchExceptionText"/>
        /// reads it back. An <see cref="OnException"/> override sees the decorated text through that
        /// accessor at the same point in the sequence as the legacy override sees it through
        /// <c>ex.text</c>, because the block is recorded before the hook is called (<c>:L886</c> precedes
        /// <c>:L889</c>).
        /// </para>
        /// </remarks>
        public const string DispatchExceptionTextKey =
            "PowerFramework.Shared.Eventful.EventBroker.DispatchExceptionText";

        /// <summary>
        /// The <see cref="OnException"/> result that stops the dispatch: the legacy
        /// <c>case 1 //Prevent</c> arm at <c>n_cst_eventful.sru:L890-L891</c>.
        /// </summary>
        /// <remarks>
        /// <b>This alphabet is neither <see cref="VetoResult"/> nor a return code.</b> It shares the
        /// numerals 1 and 2 with both and means something different from both, which is why it has its
        /// own named constants. <c>n_cst_threading_eventful.sru:L84</c> returns this value after
        /// surfacing the error to the user.
        /// </remarks>
        public const long ExceptionResultPrevent = 1;

        /// <summary>
        /// The <see cref="OnException"/> result that swallows the exception and carries on with the next
        /// subscriber: the legacy <c>case 2 //Continue</c> arm at
        /// <c>n_cst_eventful.sru:L892-L894</c>, which also clears the exception latch.
        /// </summary>
        /// <remarks>
        /// Any result that is neither this nor <see cref="ExceptionResultPrevent"/> rethrows - including
        /// <c>0</c>, which is what <c>n_cst_threading_eventful.sru:L87</c> returns to let a thread
        /// exception propagate.
        /// </remarks>
        public const long ExceptionResultContinue = 2;

        /// <summary>
        /// How many continuations a <see cref="Post"/> or a deferred compaction has left queued for
        /// <see cref="DrainPostedContinuations"/> to run.
        /// </summary>
        /// <remarks>
        /// Exposed so a caller - or a parity test - can observe that work was queued without running it.
        /// It is the only way to tell the two unsubscribe branches apart from outside: removing at depth
        /// zero rewrites the table immediately and queues nothing, while removing during a dispatch
        /// tombstones and queues one compaction (<c>n_cst_eventful.sru:L1081-L1087</c>).
        /// </remarks>
        public int PendingPostedContinuationCount => _postedContinuations.Count;

        /// <summary>
        /// Reads back the four-line diagnostic block a dispatch recorded on a captured exception, or
        /// <see langword="null"/> when the exception did not pass through a dispatch.
        /// </summary>
        /// <param name="exception">The exception to inspect. May be <see langword="null"/>.</param>
        /// <returns>
        /// The recorded block - <c>"Subscribe: ..."</c>, <c>"Target: ..."</c>, <c>"Event: ..."</c> and
        /// <c>"Exception: "</c> followed by the indented detail - or <see langword="null"/> when there is
        /// none.
        /// </returns>
        /// <remarks>
        /// The read half of DECISION 7; see <see cref="DispatchExceptionTextKey"/> for why the block lives
        /// on <see cref="System.Exception.Data"/> rather than on
        /// <see cref="System.Exception.Message"/>. This is the accessor an <see cref="OnException"/>
        /// override uses in place of the legacy's <c>ex.text</c> read, and the one the parity tests assert
        /// the exact text through.
        /// </remarks>
        public static string? GetDispatchExceptionText(Exception? exception)
        {
            if (exception is null)
            {
                return null;
            }

            return exception.Data[DispatchExceptionTextKey] as string;
        }

        /// <summary>
        /// Builds the filter that a bulk unsubscribe must use to <b>spare</b> subscriptions in the
        /// persistent namespace: the encoding of the caller-side rule at
        /// <c>ws_objects/pfw.thread.pbl.src/n_cst_threading.sru:L544</c> and <c>:L593-L598</c>.
        /// </summary>
        /// <param name="name">
        /// The caller's event name. <see langword="null"/> or empty means "every name", which yields the
        /// bare <c>".^persistent"</c> filter.
        /// </param>
        /// <returns>
        /// <paramref name="name"/> unchanged when it already contains
        /// <see cref="TopicSymbols.NamespaceSeparator"/>, because the caller has then specified a
        /// namespace of its own and it must be honoured as-is; otherwise <paramref name="name"/> with
        /// <c>".^persistent"</c> appended.
        /// </returns>
        /// <remarks>
        /// <para>
        /// <b>The highest-stakes semantic in the modification engine, and the reason it is a named member
        /// rather than a string literal at six call sites.</b> The filter grammar reads
        /// <c>".^persistent"</c> as <i>every name WHERE the namespace is NOT "persistent"</i> - the
        /// <c>'^'</c> is a per-criterion negation (<c>n_cst_eventful.sru:L1012-L1015</c>, applied at
        /// <c>:L1039-L1041</c>). A bulk unsubscribe expressed this way therefore deliberately leaves
        /// persistent subscriptions in place. Under AAP 0.7.3 C-B, expressing the same intent as a
        /// lifetime flag <i>without</i> the negation would delete subscriptions that must survive, which
        /// is why <see cref="SubscriptionLifetime"/> is carried as a projection and the engine matches on
        /// the namespace itself.
        /// </para>
        /// <para>
        /// One expression covers both halves of the legacy rule. With an empty name it produces exactly
        /// the <c>".^persistent"</c> that <c>n_cst_threading.sru:L544</c>,
        /// <c>n_cst_threading_task.sru:L385</c> and <c>n_cst_thread.sru:L552</c> pass for their
        /// no-argument bulk form; with a name that has no separator it produces the
        /// <c>name + ".^persistent"</c> of <c>n_cst_threading.sru:L596</c>,
        /// <c>n_cst_threading_task.sru:L391</c> and <c>n_cst_thread.sru:L561</c>; and with a name that
        /// already carries a separator it returns it untouched, as <c>n_cst_threading.sru:L594</c> does.
        /// Six verified call sites across three objects, one implementation.
        /// </para>
        /// <para>
        /// It is <see langword="static"/> and does no work on the broker: it composes a filter string,
        /// which the caller then passes to <see cref="Unsubscribe(string)"/> or
        /// <see cref="Disable(string, bool)"/>. See DECISION 12 on <see cref="Unsubscribe()"/> for why
        /// the no-argument form does <i>not</i> apply this rule itself.
        /// </para>
        /// </remarks>
        public static string BuildPersistentSparingFilter(string? name)
        {
            string requested = name ?? string.Empty;

            // n_cst_threading.sru:L593 - if Pos(name,".") > 0 then use it as-is. The oracle needs only
            // PRESENCE, not the position, so this is a containment test; Contains(char) is ordinal by
            // definition, matching Pos()'s culture-free search.
            if (requested.Contains(TopicSymbols.NamespaceSeparator))
            {
                return requested;
            }

            // :L596 - otherwise append the negated persistence namespace. Composed from the shared
            // symbol and namespace constants rather than spelled as a literal, so a change to either
            // cannot leave this rule silently out of step with the parser that reads it back.
            return string.Concat(
                requested,
                TopicSymbols.NamespaceSeparator.ToString(),
                TopicSymbols.Negation.ToString(),
                SubscriptionNamespaces.Persistent);
        }

        // =============================================================================================
        //  TRIGGER AND POST - the arity collapse (DECISION 3) and the message-pump non-port (DECISION 2)
        //  of_trigger  n_cst_eventful.sru:L119-L129 declared, :L233-L265 defined
        //  of_post     n_cst_eventful.sru:L132-L142 declared, :L445-L477 defined
        // =============================================================================================

        /// <summary>
        /// Dispatches an event synchronously to every matching subscription, in order, and returns the
        /// last invoked handler's value.
        /// </summary>
        /// <param name="name">
        /// The event name, compared ordinally and <b>case-sensitively</b> against each subscription's
        /// stored name. An empty name dispatches nothing and returns the global default return value
        /// (<c>n_cst_eventful.sru:L793</c>).
        /// </param>
        /// <param name="args">
        /// The event payload, passed positionally to each handler. May be empty and may be
        /// <see langword="null"/>, both of which mean "no payload".
        /// </param>
        /// <returns>
        /// The value returned by the last handler that was actually invoked; or, for a non-posted
        /// dispatch that produced no value, the default return value resolved for
        /// <paramref name="name"/> (<c>:L966-L970</c>). Note this is the <i>last handler's</i> value and
        /// not the accumulated one - <see cref="GetReturnValue"/> exposes that.
        /// </returns>
        /// <remarks>
        /// <para>
        /// <b>DECISION 3 (AAP 0.7.3 C-K) - the arity collapse.</b> The legacy declares <c>of_trigger</c>
        /// in <b>eleven</b> arities, zero through ten payload arguments (<c>:L119-L129</c>), and every one
        /// of them is a single-line delegation to the private <c>_of_Trigger(name, params[], isPost)</c>:
        /// </para>
        /// <code>
        /// public function any of_trigger (readonly string name, readonly any param1)
        ///     return _of_Trigger(name,{param1},false)
        /// </code>
        /// <para>
        /// All eleven collapse onto this one variadic method, and <c>of_post</c>'s eleven collapse onto
        /// <see cref="Post"/>. <b>Ten was a LEGACY LIMIT, not a contract</b>: it existed only because
        /// PowerScript cannot forward an arbitrary-length argument list, so the author had to write out
        /// each arity by hand and stopped at ten. C# <c>params</c> forwards natively, so <b>the .NET
        /// contract may exceed ten arguments without behavioural regression</b> - the same treatment AAP
        /// 0.2.1.4 gives the other legacy arity ceilings (20 in <c>n_cst_thread_task_sqlbase</c>, 8 in
        /// <c>n_cst_thread_trans</c>, 11 in <c>n_sqlite</c>).
        /// </para>
        /// <para>
        /// Eleven explicit overloads are deliberately <b>not</b> also emitted "for compatibility". A
        /// single variadic method is the faithful expression of eleven one-line delegations to one private
        /// routine, and twenty-two near-identical overloads would bury the one real code path.
        /// </para>
        /// <para>
        /// A handler's parameter list need <b>not</b> match the payload length. The order must match and
        /// the types must be compatible; missing parameters receive their type's initial value and
        /// surplus arguments are discarded (<c>w_test_eventful.srw:L240-L247</c>). See
        /// <see cref="PassArguments"/>.
        /// </para>
        /// </remarks>
        public object? Trigger(string name, params object?[]? args)
        {
            // :L233-L265 - each legacy overload builds a fresh array and delegates with isPost false.
            // No copy is taken: the dispatch is synchronous, so the caller cannot mutate the array
            // between here and the last handler returning. Post does take one, and says why.
            return Dispatch(name, args ?? [], isPost: false);
        }

        /// <summary>
        /// Queues an event for asynchronous dispatch and returns immediately. The queued dispatch runs
        /// when the host calls <see cref="DrainPostedContinuations"/>.
        /// </summary>
        /// <param name="name">The event name; see <see cref="Trigger"/>.</param>
        /// <param name="args">
        /// The event payload. <b>A snapshot is taken</b>, because the dispatch happens after this call
        /// returns and the caller must not be able to change the payload in between.
        /// </param>
        /// <remarks>
        /// <para>
        /// <b>DECISION 2 (AAP 0.7.3 C-K) - the message-pump non-port.</b> Every legacy <c>of_post</c>
        /// overload is literally <c>Post _of_Trigger(name, {...}, true)</c> (<c>:L445-L477</c>), which
        /// hands the call to the <b>Win32 message queue</b> so it runs when the message pump next turns.
        /// AAP 0.6.5 marks that pump a <b>deliberate non-port</b>: a headless Linux container has no
        /// message pump, and the same <c>Post</c> idiom appears once more inside the modification engine,
        /// which posts the compaction pass while a dispatch is in flight (<c>:L1083</c>).
        /// </para>
        /// <para>
        /// So a posted dispatch becomes an <b>explicitly queued continuation drained by the host</b>. It
        /// is queued FIFO and executed in that order, and the compaction pass goes through the same queue
        /// so the two keep their relative order exactly as they did in the Win32 queue.
        /// </para>
        /// <para>
        /// <b>Why fire-and-forget would be wrong, stated plainly.</b> A <c>Task.Run</c>, a thread-pool
        /// hand-off or an <c>async void</c> continuation would each reorder events relative to the legacy
        /// - the Win32 queue is strictly ordered and single-threaded, and the broker holds unsynchronised
        /// per-instance dispatch state that a second thread would corrupt. It would also make this path
        /// untestable without a host, and an untestable dispatch path cannot be covered to the 80%
        /// per-service line-coverage gate. Neither is a style preference.
        /// </para>
        /// <para>
        /// A posted dispatch sets the is-post flag, so <see cref="IsPost"/> answers
        /// <see langword="true"/> inside its handlers and the <b>final default substitution is skipped</b>
        /// (<c>:L966</c> guards <c>:L967-L969</c>). The legacy <c>of_post</c> is a <c>subroutine</c>, so
        /// this returns nothing and the dispatch's value is discarded - it is still observable from inside
        /// the dispatch through <see cref="GetReturnValue"/>.
        /// </para>
        /// </remarks>
        public void Post(string name, params object?[]? args)
        {
            // The snapshot is the one deliberate difference from Trigger. The legacy builds its array
            // inline at the Post statement, so the values are fixed at the moment of posting; copying
            // here reproduces that even though the caller now holds the array.
            object?[] payload = args is null || args.Length == 0 ? [] : (object?[])args.Clone();

            // :L445-L477 - Post _of_Trigger(name, {...}, true). The return value is discarded because
            // of_post is a subroutine.
            EnqueueContinuation(() => Dispatch(name, payload, isPost: true));
        }

        /// <summary>
        /// Runs every queued continuation - posted dispatches and deferred compactions - in the order they
        /// were queued, and returns how many ran.
        /// </summary>
        /// <returns>
        /// The number of continuations executed. Zero when nothing was queued.
        /// </returns>
        /// <remarks>
        /// <para>
        /// <b>The drain half of DECISION 2, and the host's replacement for the Win32 message pump.</b>
        /// The legacy relies on the PowerBuilder application's message loop turning; a headless service has
        /// no such loop, so the pump becomes an explicit call the host makes at a point of its own
        /// choosing. That makes the ordering deterministic and the whole <see cref="Post"/> path unit
        /// testable with no host at all, which is what the per-service coverage gate requires.
        /// </para>
        /// <para>
        /// <b>Re-entrancy is permitted and is faithful.</b> A continuation may itself post, and the newly
        /// queued work is drained by the same loop rather than deferred to the next call - a Win32 pump
        /// behaves the same way. A continuation may also call this method, which simply continues the
        /// same FIFO order; nothing is executed twice, because each continuation is dequeued before it
        /// runs. An exception thrown by a continuation propagates to the caller of this method, leaving
        /// the continuations behind it queued, exactly as an exception escaping a message handler leaves
        /// the rest of the queue intact.
        /// </para>
        /// </remarks>
        public int DrainPostedContinuations()
        {
            int executed = 0;

            while (_postedContinuations.Count > 0)
            {
                // Dequeued before it runs, so a continuation that posts cannot cause this one to be
                // executed a second time and a continuation that throws cannot be retried.
                Action continuation = _postedContinuations.Dequeue();
                continuation();
                executed++;
            }

            return executed;
        }

        // =============================================================================================
        //  THE FOUR HOOKS - n_cst_eventful.sru:L33-L36, gated on the subclassing flag at every call site
        //  Every one is protected virtual, is called with the base implementation returning the neutral
        //  value, and may itself call Prevent, SetDefaultReturnValue or any other member of the broker.
        //  The real overrides at n_cst_threading_eventful.sru:L48-L88 all begin with call super::<event>,
        //  so an override calling base.OnX(...) first and then adding its own logic is the intended
        //  shape and is what the derivability check exercises.
        // =============================================================================================

        /// <summary>
        /// Called once per dispatch, on the <b>first matching subscription only</b>, before that
        /// subscription's handler runs. A prevented result aborts the whole dispatch.
        /// </summary>
        /// <param name="name">The event name being dispatched.</param>
        /// <param name="isPost">
        /// <see langword="true"/> when the dispatch was queued by <see cref="Post"/>;
        /// <see langword="false"/> when it came from <see cref="Trigger"/>.
        /// </param>
        /// <returns>
        /// <see cref="RetCode.OK"/> to let the dispatch proceed, or <see cref="RetCode.PREVENT"/> to abort
        /// it. The base implementation returns <see cref="RetCode.OK"/>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The port of the legacy <c>ontriggering</c> event (<c>:L34</c>), called at
        /// <c>:L839-L843</c> and tested with <c>Predicates.IsPrevented</c> - so the value that aborts is
        /// <see cref="RetCode.PREVENT"/>, which is <c>1</c>. <b>This is the second of the three alphabets
        /// that share the numerals 1 and 2</b> and it is not <see cref="VetoResult"/>.
        /// </para>
        /// <para>
        /// "First matching subscription only" is exact and load-bearing: the call sits inside
        /// <c>if Not bSubscribed then</c>, so a dispatch with no matching subscription never fires it at
        /// all, and a dispatch with ten matching subscriptions fires it once. An abort here is not a
        /// prevention in the <see cref="VetoResult"/> sense - it leaves the loop directly (<c>:L841</c>)
        /// without setting the prevent state - so nothing survives into the enclosing dispatch.
        /// </para>
        /// <para>
        /// <c>n_cst_threading_eventful.sru:L60-L66</c> overrides it to abort when the thread has been
        /// cancelled and, for a non-posted dispatch, to signal a synchronisation event.
        /// </para>
        /// </remarks>
        protected virtual long OnTriggering(string name, bool isPost)
        {
            // The neutral value. Returning OK rather than a prevention is what makes the base class safe
            // to call from an override's `call super::ontriggering` equivalent.
            return RetCode.OK;
        }

        /// <summary>
        /// Called once after the dispatch loop, but only when at least one subscription was dispatched.
        /// </summary>
        /// <param name="name">The event name that was dispatched.</param>
        /// <param name="isPost">
        /// <see langword="true"/> when the dispatch was queued by <see cref="Post"/>.
        /// </param>
        /// <remarks>
        /// <para>
        /// The port of the legacy <c>ontriggered</c> event (<c>:L35</c>), which returns nothing, called at
        /// <c>:L944-L946</c> from the dispatch's outer <see langword="finally"/>. It is therefore reached
        /// even when the loop ended in an exception or a prevention - and it is <b>not</b> reached when no
        /// subscription matched, because <c>:L942</c> guards it with the same "was anything dispatched"
        /// latch that guards <see cref="OnTriggering"/>. The pair is symmetric: either both fire or
        /// neither does.
        /// </para>
        /// <para>
        /// It runs <b>before</b> <see cref="Current"/> is restored (<c>:L948</c> follows <c>:L945</c>), so
        /// an override still sees this broker as the ambient one.
        /// </para>
        /// <para>
        /// <c>n_cst_threading_eventful.sru:L68-L72</c> overrides it to reset the synchronisation event its
        /// <c>ontriggering</c> set.
        /// </para>
        /// </remarks>
        protected virtual void OnTriggered(string name, bool isPost)
        {
            // Intentionally empty: the legacy base event has no body either (n_cst_eventful.sru declares
            // `event ontriggered` with no implementation), and the override chain begins at the derived
            // type. This is the neutral behaviour, not an unimplemented one.
        }

        /// <summary>
        /// Called when a handler throws, after the exception has been decorated with the four-line
        /// diagnostic block, to decide whether the dispatch stops, continues, or lets the exception
        /// propagate.
        /// </summary>
        /// <param name="name">The event name being dispatched.</param>
        /// <param name="exception">
        /// The exception the handler raised. Its four-line diagnostic block is already recorded and is
        /// readable through <see cref="GetDispatchExceptionText"/>.
        /// </param>
        /// <returns>
        /// <see cref="ExceptionResultPrevent"/> (<c>1</c>) to leave the dispatch loop and swallow the
        /// exception; <see cref="ExceptionResultContinue"/> (<c>2</c>) to clear the exception latch and
        /// carry on with the next subscriber; <b>anything else - including <c>0</c> - to rethrow</b>. The
        /// base implementation returns <c>0</c> and therefore rethrows.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The port of the legacy <c>onexception</c> event (<c>:L36</c>), dispatched through a
        /// <c>choose case</c> at <c>:L889-L895</c>. <b>This is the THIRD alphabet and it is not
        /// <see cref="VetoResult"/> and not a return code.</b> Reusing <see cref="VetoResult"/> here
        /// would be a category error: its <c>2</c> means "prevent the whole nested chain" while this
        /// <c>2</c> means very nearly the opposite, "keep going". The real override confirms the shape -
        /// <c>n_cst_threading_eventful.sru:L84</c> returns <c>1</c> after surfacing the error and
        /// <c>:L87</c> returns <c>0</c> to let it propagate.
        /// </para>
        /// <para>
        /// The default <c>0</c> is deliberate: an unhandled handler exception must propagate, because the
        /// framework's posture on a structural fault is fail-fast rather than graceful degradation
        /// (AAP 0.1.4). Note that <see cref="ExceptionResultContinue"/> also clears the latch
        /// (<c>:L893</c>), so the <i>next</i> exception in the same dispatch is decorated afresh.
        /// </para>
        /// </remarks>
        protected virtual long OnException(string name, Exception exception)
        {
            // Neither ExceptionResultPrevent nor ExceptionResultContinue, so the caller rethrows. This is
            // the value the real override returns for the same purpose (n_cst_threading_eventful:L87).
            return 0;
        }

        /// <summary>
        /// Called before each handler invocation so a derived broker can <b>inject leading arguments</b>,
        /// or skip this subscriber entirely.
        /// </summary>
        /// <param name="name">The event name being dispatched.</param>
        /// <param name="target">The subscription's callback target.</param>
        /// <param name="arguments">
        /// The argument buffer for this invocation. Write leading slots with
        /// <see cref="EventArgumentContext.SetArgument"/> using <b>one-based</b> indices, then set
        /// <see cref="EventArgumentContext.ConsumedArgumentCount"/> to how many were written.
        /// </param>
        /// <returns>
        /// <see cref="RetCode.OK"/> to invoke the handler, or <see cref="RetCode.PREVENT"/> to skip this
        /// subscriber and move on to the next. The base implementation returns
        /// <see cref="RetCode.OK"/>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The port of the legacy <c>onprepare</c> event (<c>:L33</c>), called from <c>_of_passargs</c> at
        /// <c>:L609</c> and tested with <c>Predicates.IsPrevented</c>, so the value that skips is
        /// <see cref="RetCode.PREVENT"/> - the second of the three alphabets again, not
        /// <see cref="VetoResult"/>. A prevented result makes <c>_of_passargs</c> return
        /// <see langword="false"/> and the dispatch loop <c>continue</c> (<c>:L610</c>), which skips
        /// <i>this</i> subscriber only and leaves the rest of the dispatch running.
        /// </para>
        /// <para>
        /// <b>DECISION 1 (AAP 0.7.3 C-D and C-K) - the invoker parameter is gone and
        /// <paramref name="arguments"/> replaces it.</b> The legacy signature is
        /// <c>(string name, powerobject target, n_scriptinvoker invoker, integer argcount,
        /// ref integer argpassed)</c>. <c>n_scriptinvoker</c> belongs to the deferred ScriptBridge
        /// service, so it is dropped; <see cref="EventArgumentContext"/> carries the two capabilities the
        /// real override actually uses, with <c>argcount</c> as
        /// <see cref="EventArgumentContext.DeclaredArgumentCount"/> and the <c>ref argpassed</c>
        /// out-parameter as <see cref="EventArgumentContext.ConsumedArgumentCount"/>. See that type's
        /// remarks for the four-line override this preserves.
        /// </para>
        /// <para>
        /// An override may call back into the broker from here - <c>n_cst_threading_eventful.sru:L50</c>
        /// calls <c>of_Prevent()</c> from inside <c>onprepare</c> and then returns <c>1</c>, which both
        /// aborts the dispatch through the prevent state and skips this subscriber. Both effects are
        /// preserved: the prevent state is tested at the top of the next loop iteration (<c>:L822</c>).
        /// </para>
        /// </remarks>
        protected virtual long OnPrepare(string name, object target, EventArgumentContext arguments)
        {
            // The neutral value: invoke the handler, consume no leading slots. Matching the legacy base
            // event, which has no body and therefore yields zero for both the result and argpassed.
            return RetCode.OK;
        }

        // =============================================================================================
        //  SUBSCRIBE - of_on, n_cst_eventful.sru:L130-L131 declared, :L293 and :L296-L443 defined
        // =============================================================================================

        /// <summary>
        /// Subscribes a handler to an event topic.
        /// </summary>
        /// <param name="name">
        /// The subscription topic: an optional leading run of the ordering and capture symbols, an
        /// optional numeric priority prefix, the event name, and an optional <c>"."</c> namespace suffix.
        /// See <see cref="SubscriptionTopic.ParseSubscription"/> for the grammar. <b>Case-sensitive.</b>
        /// </param>
        /// <param name="target">The object that owns the handler. Must not be <see langword="null"/>.</param>
        /// <param name="handlerName">
        /// The handler member's name on <paramref name="target"/>. <b>Case-insensitive</b>, and stored
        /// lower-cased.
        /// </param>
        /// <returns>
        /// <see cref="RetCode.OK"/>; <see cref="RetCode.E_INVALID_ARGUMENT"/> when either name is empty or
        /// the topic is malformed; <see cref="RetCode.E_INVALID_OBJECT"/> when
        /// <paramref name="target"/> is <see langword="null"/>; or
        /// <see cref="RetCode.E_EVENT_NOT_FOUND"/> when no handler of that name can be resolved.
        /// </returns>
        /// <remarks>
        /// The port of <c>of_on(name, object, evtName)</c>, which is a single-line delegation to the
        /// four-argument form with an empty signature (<c>n_cst_eventful.sru:L293</c>).
        /// </remarks>
        public long Subscribe(string name, object? target, string handlerName)
        {
            // :L293 - return of_On(name,object,evtName,"")
            return Subscribe(name, target, handlerName, string.Empty);
        }

        /// <summary>
        /// Subscribes a handler to an event topic, disambiguating overloaded handlers by signature.
        /// </summary>
        /// <param name="name">The subscription topic; see <see cref="Subscribe(string, object?, string)"/>.</param>
        /// <param name="target">The object that owns the handler. Must not be <see langword="null"/>.</param>
        /// <param name="handlerName">The handler member's name. Case-insensitive.</param>
        /// <param name="handlerSignature">
        /// A comma-separated list of the handler's parameter types, or empty to accept any signature. Both
        /// CLR type names and the PowerScript spellings of AAP 0.4.5.2 are accepted, so
        /// <c>"string,long"</c> and <c>"String,Int64"</c> select the same handler. See
        /// <see cref="ResolveHandler"/>.
        /// </param>
        /// <returns>See <see cref="Subscribe(string, object?, string)"/>.</returns>
        /// <remarks>
        /// <para>
        /// The port of <c>of_on(name, object, evtName, evtSign)</c> (<c>:L296-L443</c>), and the routine
        /// that establishes the dispatch order for the whole broker. The steps below are in the oracle's
        /// order because several of them are order-dependent.
        /// </para>
        /// <para>
        /// <b>The insert is the whole of the dispatch order, and the prepend-versus-append difference is
        /// exactly <c>&lt;=</c> versus <c>&lt;</c></b> (<c>:L417</c> against <c>:L419</c>). A prepend
        /// stops at the first entry whose priority is less than <i>or equal to</i> the new one, landing at
        /// the HEAD of the equal-priority run; an append stops only at strictly less, landing at its TAIL.
        /// Getting those two comparisons the wrong way round silently reorders every same-priority
        /// subscription in the system, and nothing else would report it.
        /// </para>
        /// <para>
        /// <b>A subscription made during a dispatch takes effect only on the NEXT dispatch</b>
        /// (<c>w_test_eventful.srw:L237</c>). That rule has two halves and both are reproduced here and in
        /// <see cref="Dispatch"/>: the dispatch loop captures its upper bound once at entry, so a longer
        /// table is not noticed; and the index-stack fix-up below pushes every in-flight cursor along when
        /// an insert lands at or before it, so the running dispatch keeps pointing at the same
        /// subscription it was pointing at.
        /// </para>
        /// </remarks>
        public long Subscribe(string name, object? target, string handlerName, string handlerSignature)
        {
            // :L331 - if name = "" or evtName = "" then return RetCode.E_INVALID_ARGUMENT. Tested BEFORE
            // the target, which is why Subscribe("", null, "") answers E_INVALID_ARGUMENT rather than
            // E_INVALID_OBJECT. IsNullOrEmpty rather than Length, so a null is safe and reads as empty
            // exactly as PowerScript's always-present possibly-empty string does.
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(handlerName))
            {
                return RetCode.E_INVALID_ARGUMENT;
            }

            // :L332 - if Not IsValidObject(object) then return RetCode.E_INVALID_OBJECT
            if (!Predicates.IsValidObject(target))
            {
                return RetCode.E_INVALID_OBJECT;
            }

            // :L334-L336 and :L339-L381 - the defaults and the inline topic parse. The character scan,
            // the once-only duplicate guards, the numeric priority prefix and the namespace split all
            // live in SubscriptionTopic.ParseSubscription, which returns the legacy codes unchanged.
            long parseCode = SubscriptionTopic.ParseSubscription(name, out SubscriptionTopic? topic);
            if (parseCode != RetCode.OK)
            {
                // Surfaced unchanged: every failure the parser reports is one of the six
                // E_INVALID_ARGUMENT returns between :L343 and :L381.
                return parseCode;
            }

            if (topic is null)
            {
                // The parser's contract pairs a success code with a non-null topic, so this maps a
                // contract violation onto the same code the oracle uses for an unusable topic rather than
                // dereferencing null.
                return RetCode.E_INVALID_ARGUMENT;
            }

            // :L336 - newEvent.evtName = Lower(evtName). Invariant rather than current culture: the fold
            // must not depend on the ambient locale, or the Turkish dotless i would detach a handler
            // named "onItemChanged" from a subscription registered for it.
            string foldedHandlerName = handlerName.ToLowerInvariant();

            // :L383 - nCount = UpperBound(Events)
            int count = _subscriptions.Count;

            // :L385-L396 - the garbage sweep, and its depth guard is the point. Collecting mid-dispatch
            // would rewrite the table every active level holds a cursor into, so the sweep only runs
            // outside a dispatch; the count is re-read afterwards because the table may have shrunk.
            if (_deep <= 0)
            {
                bool needsCollect = false;

                for (int index = 0; index < count; index++)
                {
                    // :L387 - if Not IsValidObject(Events[nIndex].object) then bCollect = true; exit
                    if (!Predicates.IsValidObject(_subscriptions[index].Target))
                    {
                        needsCollect = true;
                        break;
                    }
                }

                if (needsCollect)
                {
                    // :L393-L394
                    Collect();
                    count = _subscriptions.Count;
                }
            }

            // :L398-L403 - the legacy creates an invoker, initialises it against the target, handler name
            // and signature, and returns E_EVENT_NOT_FOUND when that initialisation fails (tested with
            // Predicates.IsFailed on the returned method id). The OUTCOME is reproduced without an
            // invoker: resolution succeeds or the same code comes back, at the same point in the
            // sequence. :L404's invoker.Release() has no analogue - there is no invoker to release.
            long resolution = ResolveHandler(target!, foldedHandlerName, handlerSignature, out MethodInfo? handler);
            if (Predicates.IsFailed(resolution) || handler is null)
            {
                return RetCode.E_EVENT_NOT_FOUND;
            }

            EventSubscription newEntry = new()
            {
                // :L377 - the namespace, empty when the topic carried none.
                Namespace = topic.Namespace,

                // :L335 narrowed by :L363, :L371 and :L378 - the residual name, which is the dispatch
                // and sort key. LegacyName, never LogicalName.
                Name = topic.LegacyName,

                // :L347 / :L350 - the capture mode claimed by '%' or '*'.
                Capture = topic.Capture,

                // :L334 / :L353 / :L356 / :L370 - the priority claimed by '@', '!' or a numeric prefix.
                Priority = topic.Priority,

                // :L337
                Target = target,

                // :L405 - newEvent.clsChain = _of_GetObjectClassChain(object)
                ClassChain = BuildClassChain(target),

                // :L336
                HandlerName = foldedHandlerName,

                // :L399 - the resolved handler identity, the mid analogue.
                Handler = handler
            };

            // :L407-L422 - the ordered insert. Written with a one-based loop variable so it reads
            // statement for statement against the oracle; the zero-based subscript conversion happens
            // once, on the line that reads the entry. nInsertIdx is one-based too, and zero still means
            // "no position found", which is what :L423 tests.
            int insertIndex = 0;

            for (int index = 1; index <= count; index++)
            {
                EventSubscription existing = _subscriptions[index - 1];

                // :L408 - if Events[nIndex].name <> newEvent.name then
                if (!string.Equals(existing.Name, newEntry.Name, StringComparison.Ordinal))
                {
                    // :L409-L412 - the list is sorted ascending, so the first name greater than the new
                    // one is the insertion point.
                    if (string.CompareOrdinal(existing.Name, newEntry.Name) > 0)
                    {
                        insertIndex = index;
                        break;
                    }

                    // :L413 - otherwise this name is smaller; keep scanning.
                    continue;
                }

                // :L415 - a name match: remember this position, then decide whether to stop here.
                insertIndex = index;

                if (topic.Prepend)
                {
                    // :L417 - PREPEND stops at <= , so it lands at the HEAD of the equal-priority run.
                    if (existing.Priority <= newEntry.Priority)
                    {
                        break;
                    }
                }
                else
                {
                    // :L419 - APPEND stops at < , so it lands at the TAIL of the equal-priority run.
                    if (existing.Priority < newEntry.Priority)
                    {
                        break;
                    }
                }

                // :L421 - advance past this entry and keep looking.
                insertIndex++;
            }

            if (insertIndex > 0)
            {
                // :L424-L426 shift the tail right by one, then :L437 write the new entry into the gap.
                // List<T>.Insert performs both in one operation with identical semantics; the one-based
                // insertIndex converts here. Note that a name-matching final iteration which did not
                // break leaves insertIndex at count + 1, for which the legacy's downward shift loop does
                // not execute at all and this Insert appends - the same outcome by the same arithmetic.
                _subscriptions.Insert(insertIndex - 1, newEntry);

                // :L427-L433 - the index-stack fix-up. Every dispatch level currently in flight holds a
                // cursor; any cursor at or after the insertion point now refers to the entry that was
                // pushed along, so it is advanced to keep referring to the same subscription.
                //
                // THE OFFSET IS DELIBERATE AND IS DERIVED, NOT GUESSED (AAP 0.4.5.4 names one-based to
                // zero-based translation the single most dangerous mechanical hazard in this refactor).
                // The oracle compares a ONE-based cursor against a ONE-based insertIndex, while
                // _indexStack holds ZERO-based cursors:
                //     _nIdxStack[n] >= nInsertIdx   <=>   (_indexStack[level] + 1) >= insertIndex
                //                                   <=>    _indexStack[level] >= insertIndex - 1
                // so `insertIndex - 1` below is that identity and not an off-by-one.
                //
                // PRESERVED LEGACY CONSEQUENCE, verified by tracing the oracle and reproduced rather
                // than corrected (C-B): because the dispatch loop's limit was captured BEFORE this
                // insert, advancing a cursor costs the running dispatch its LAST iteration - so a
                // subscription registered ahead of the cursor mid-dispatch also causes the tail entry of
                // the current run to be skipped, even though that entry was registered before the
                // dispatch began. It is dispatched normally on the next trigger. Without the advance the
                // entry the cursor is currently ON would instead be dispatched TWICE, which is the worse
                // of the two, and the oracle chooses this one.
                if (_deep > 0)
                {
                    int activeLevels = (int)Math.Min(_deep, _indexStack.Count);

                    for (int level = 0; level < activeLevels; level++)
                    {
                        if (_indexStack[level] >= insertIndex - 1)
                        {
                            _indexStack[level]++;
                        }
                    }
                }
            }
            else
            {
                // :L435 - no position was found, so append. No cursor can be affected by an append, which
                // is why the legacy omits the fix-up on this branch.
                insertIndex = count + 1;
                _subscriptions.Insert(insertIndex - 1, newEntry);
            }

            // :L439 - widen the lower bound when the new name sorts before it, or when it is unset. The
            // empty-string test is not redundant with the comparison: an empty bound is ordinally
            // smallest, so without it the first subscription would never establish a lower bound.
            if (string.CompareOrdinal(_firstName, newEntry.Name) > 0 || _firstName.Length == 0)
            {
                _firstName = newEntry.Name;
            }

            // :L440 - widen the upper bound when the new name sorts after it.
            if (string.CompareOrdinal(_lastName, newEntry.Name) < 0)
            {
                _lastName = newEntry.Name;
            }

            // :L442
            return RetCode.OK;
        }

        // =============================================================================================
        //  UNSUBSCRIBE - of_off, seven overloads over one engine
        //  n_cst_eventful.sru:L116-L118, L146, L151-L154 declared; :L171-L231, :L555-L574, :L658-L745
        //  defined. Every one is a single-line delegation to _of_Modify, and each is reproduced argument
        //  for argument. The surface is NOT simplified: both consumers call several of these forms
        //  directly and n_cst_threading.sru:L532-L601 re-exposes them one for one, so collapsing any two
        //  would break a call site in the Persistence or DataServices port.
        // =============================================================================================

        /// <summary>
        /// Removes the subscriptions a given target registered for a given handler, across every event
        /// name.
        /// </summary>
        /// <param name="target">The callback target to match. <see langword="null"/> matches every target.</param>
        /// <param name="handlerName">The handler name to match. Case-insensitive. Empty matches every handler.</param>
        /// <returns><see cref="RetCode.OK"/>, or the filter grammar's failure code.</returns>
        /// <remarks>
        /// <para>
        /// The port of <c>of_off(object, evtName)</c> (<c>n_cst_eventful.sru:L171-L190</c>):
        /// <c>_of_Modify("", object, evtName, false, MOD_OFF, true)</c>.
        /// </para>
        /// <para>
        /// <b>DECISION 13 (AAP 0.7.3 C-K) - a documented overload ambiguity, and it is unavoidable.</b>
        /// AAP 0.4.5.2 maps PowerBuilder's <c>powerobject</c> onto <see cref="object"/>, and
        /// <c>powerobject</c> and <c>string</c> are unrelated types in PowerBuilder, so the oracle can and
        /// does declare both <c>of_off(powerobject, string)</c> and <c>of_off(string, powerobject)</c>
        /// unambiguously. In C# <see cref="string"/> IS an <see cref="object"/>, so a call written as
        /// <c>Unsubscribe("a", "b")</c> matches this overload and
        /// <see cref="Unsubscribe(string, object?)"/> equally well and the compiler reports CS0121 at that
        /// CALL SITE - never at the declaration, so nothing here is broken by it. Disambiguate by casting
        /// the target: <c>Unsubscribe((object?)someTarget, "handler")</c>. The same shape recurs on
        /// <see cref="Disable(object?, string, bool)"/> against
        /// <see cref="Disable(string, object?, bool)"/>. The alternative - renaming one member or dropping
        /// it - would break the mandated seven-overload surface, so the ambiguity is documented rather
        /// than designed away.
        /// </para>
        /// </remarks>
        public long Unsubscribe(object? target, string handlerName)
        {
            // :L189
            return Modify(string.Empty, target, handlerName, excluding: false, ModificationKind.Off, value: true);
        }

        /// <summary>
        /// Removes every subscription a given target registered.
        /// </summary>
        /// <param name="target">The callback target to match. <see langword="null"/> matches every target.</param>
        /// <returns><see cref="RetCode.OK"/>, or the filter grammar's failure code.</returns>
        /// <remarks>
        /// The port of <c>of_off(object)</c> (<c>:L192-L210</c>):
        /// <c>_of_Modify("", object, "", false, MOD_OFF, true)</c>. This is the form the five DataWindow
        /// services call from their own teardown - for example
        /// <c>n_cst_dwsvc_columnexp.sru:L2431</c> and <c>n_cst_dwsvc_rowselect.sru:L281</c>, both spelled
        /// <c>#DataWindow.of_Off(this)</c>.
        /// </remarks>
        public long Unsubscribe(object? target)
        {
            // :L209
            return Modify(string.Empty, target, string.Empty, excluding: false, ModificationKind.Off, value: true);
        }

        /// <summary>
        /// Removes <b>every</b> subscription, including those in the persistent namespace.
        /// </summary>
        /// <returns><see cref="RetCode.OK"/>.</returns>
        /// <remarks>
        /// <para>
        /// The port of <c>of_off()</c> (<c>n_cst_eventful.sru:L212-L231</c>), which passes a null target
        /// and, critically, an <b>EMPTY</b> filter:
        /// </para>
        /// <code>
        /// powerobject nullObj
        /// SetNull(nullObj)
        /// return _of_Modify("",nullObj,"",false,MOD_OFF,true)
        /// </code>
        /// <para>
        /// <b>DECISION 12 (AAP 0.7.3 C-B, C-C and C-K) - this form keeps the oracle's empty filter, and
        /// it deliberately does NOT spare persistent subscriptions.</b> The reasoning, with the evidence,
        /// because the opposite is a tempting mistake:
        /// </para>
        /// <list type="number">
        ///   <item><description>
        ///   The base broker's no-argument form passes <c>""</c>, which matches every name in every
        ///   namespace (<c>:L1017</c> sets <c>bNoName</c>, honoured at <c>:L1030</c>).
        ///   </description></item>
        ///   <item><description>
        ///   <b>An in-scope consumer depends on that.</b>
        ///   <c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L460</c> re-exposes it verbatim
        ///   as <c>return Eventful.of_Off()</c>, and <c>:L583</c> calls it from
        ///   <c>ondestructor</c> - where the intent is unmistakably to detach EVERYTHING as the service is
        ///   torn down. Sparing a namespace there would leak subscriptions to a destroyed DataWindow.
        ///   </description></item>
        ///   <item><description>
        ///   The persistence-sparing behaviour belongs to the CONSUMER wrappers, not to the broker. Six
        ///   verified sites across three objects pass the filter explicitly:
        ///   <c>n_cst_threading.sru:L544</c> and <c>:L596</c>,
        ///   <c>n_cst_threading_task.sru:L385</c> and <c>:L391</c>, and
        ///   <c>n_cst_thread.sru:L552</c> and <c>:L561</c>. Not one of them relies on the broker to
        ///   supply it.
        ///   </description></item>
        /// </list>
        /// <para>
        /// The persistence-sparing semantic is therefore discharged in the two places it actually lives:
        /// the filter grammar implements the <c>'^'</c> negation so <c>Unsubscribe(".^persistent")</c>
        /// genuinely spares those subscriptions, and <see cref="BuildPersistentSparingFilter"/> encodes the
        /// caller-side rule those six sites share. A caller that wants the threading layer's bulk
        /// behaviour writes <c>Unsubscribe(BuildPersistentSparingFilter(null))</c>; a caller that wants the
        /// DataWindow layer's writes <c>Unsubscribe()</c>. Both are reachable, both are correct, and
        /// neither is silently substituted for the other.
        /// </para>
        /// </remarks>
        public long Unsubscribe()
        {
            // :L228-L230 - a null target, an empty filter, and MOD_OFF. The null is what makes the target
            // criterion inactive (:L1018 bNoObject = IsNull(object)).
            return Modify(string.Empty, target: null, string.Empty, excluding: false, ModificationKind.Off, value: true);
        }

        /// <summary>
        /// Removes every subscription <i>except</i> those a given target registered, when
        /// <paramref name="excluding"/> is <see langword="true"/>.
        /// </summary>
        /// <param name="target">The callback target the match is built on.</param>
        /// <param name="excluding">
        /// <see langword="true"/> to invert the whole composite match, removing everything the target did
        /// <i>not</i> register; <see langword="false"/> for the ordinary meaning, which is identical to
        /// <see cref="Unsubscribe(object?)"/>.
        /// </param>
        /// <returns><see cref="RetCode.OK"/>, or the filter grammar's failure code.</returns>
        /// <remarks>
        /// The port of <c>of_off(object, excluding)</c> (<c>:L555-L574</c>):
        /// <c>_of_Modify("", object, "", excluding, MOD_OFF, true)</c>. The exclusion is the
        /// <b>second, outer</b> level of negation in the engine and is applied after every criterion has
        /// been evaluated (<c>:L1051-L1053</c>) - it is not the same thing as the per-criterion
        /// <c>'^'</c>, and the two compose.
        /// </remarks>
        public long Unsubscribe(object? target, bool excluding)
        {
            // :L573
            return Modify(string.Empty, target, string.Empty, excluding, ModificationKind.Off, value: true);
        }

        /// <summary>
        /// Removes one specific subscription: a named event, a target, and a handler.
        /// </summary>
        /// <param name="name">The filter; see <see cref="Unsubscribe(string)"/> for the grammar.</param>
        /// <param name="target">The callback target. <see langword="null"/> matches every target.</param>
        /// <param name="handlerName">The handler name. Case-insensitive. Empty matches every handler.</param>
        /// <returns><see cref="RetCode.OK"/>, or the filter grammar's failure code.</returns>
        /// <remarks>
        /// The port of <c>of_off(name, object, evtName)</c> (<c>:L658-L678</c>):
        /// <c>_of_Modify(name, object, evtName, false, MOD_OFF, true)</c>. Re-exposed by
        /// <c>se_cst_dw.sru:L451</c> and <c>n_cst_threading.sru:L535</c>.
        /// </remarks>
        public long Unsubscribe(string name, object? target, string handlerName)
        {
            // :L677
            return Modify(name, target, handlerName, excluding: false, ModificationKind.Off, value: true);
        }

        /// <summary>
        /// Removes every subscription matching a filter.
        /// </summary>
        /// <param name="name">
        /// The filter, in the <b>filter grammar</b> and not the subscribe grammar. An empty name matches
        /// every name; <c>"^clicked"</c> matches every name except <c>clicked</c>; <c>".myns"</c> matches
        /// every name in the <c>myns</c> namespace; <c>".^persistent"</c> matches every name whose
        /// namespace is not <c>persistent</c>; <c>"clicked."</c> matches <c>clicked</c> only where it has
        /// no namespace. See <see cref="SubscriptionTopic.ParseFilter"/>.
        /// </param>
        /// <returns>
        /// <see cref="RetCode.OK"/>, or <see cref="RetCode.E_INVALID_ARGUMENT"/> for the one invalid
        /// filter - a negation with nothing to negate (<c>n_cst_eventful.sru:L1021-L1023</c>).
        /// </returns>
        /// <remarks>
        /// The port of <c>of_off(name)</c> (<c>:L680-L704</c>):
        /// <c>_of_Modify(name, nullObject, "", false, MOD_OFF, true)</c>. The legacy documents the whole
        /// filter grammar in this routine's own header comment at <c>:L686-L690</c>, and
        /// <c>w_test_eventful.srw:L306-L342</c> exercises fourteen spellings of it.
        /// </remarks>
        public long Unsubscribe(string name)
        {
            // :L701-L703
            return Modify(name, target: null, string.Empty, excluding: false, ModificationKind.Off, value: true);
        }

        /// <summary>
        /// Removes every subscription matching a filter that a given target registered.
        /// </summary>
        /// <param name="name">The filter; see <see cref="Unsubscribe(string)"/>.</param>
        /// <param name="target">The callback target. <see langword="null"/> matches every target.</param>
        /// <returns><see cref="RetCode.OK"/>, or the filter grammar's failure code.</returns>
        /// <remarks>
        /// The port of <c>of_off(name, object)</c> (<c>:L725-L745</c>):
        /// <c>_of_Modify(name, object, "", false, MOD_OFF, true)</c>. Re-exposed by
        /// <c>se_cst_dw.sru:L454</c> and <c>n_cst_threading.sru:L600</c>.
        /// </remarks>
        public long Unsubscribe(string name, object? target)
        {
            // :L744
            return Modify(name, target, string.Empty, excluding: false, ModificationKind.Off, value: true);
        }

        // =============================================================================================
        //  DISABLE - of_disable, seven overloads over the same engine
        //  n_cst_eventful.sru:L159-L165 declared; :L1092-L1250 defined. A disable never removes: it
        //  suspends in place, keeping the subscription's ordering so re-enabling restores it exactly.
        // =============================================================================================

        /// <summary>
        /// Suspends or resumes one specific subscription: a named event, a target, and a handler.
        /// </summary>
        /// <param name="name">The filter; see <see cref="Unsubscribe(string)"/> for the grammar.</param>
        /// <param name="target">The callback target. <see langword="null"/> matches every target.</param>
        /// <param name="handlerName">The handler name. Case-insensitive. Empty matches every handler.</param>
        /// <param name="disabled"><see langword="true"/> to suspend, <see langword="false"/> to resume.</param>
        /// <returns><see cref="RetCode.OK"/>, or the filter grammar's failure code.</returns>
        /// <remarks>
        /// The port of <c>of_disable(name, object, evtName, disabled)</c> (<c>:L1092-L1113</c>):
        /// <c>_of_Modify(name, object, evtName, false, MOD_DISABLE, disabled)</c>.
        /// </remarks>
        public long Disable(string name, object? target, string handlerName, bool disabled)
        {
            // :L1112
            return Modify(name, target, handlerName, excluding: false, ModificationKind.Disable, disabled);
        }

        /// <summary>
        /// Suspends or resumes every subscription matching a filter that a given target registered.
        /// </summary>
        /// <param name="name">The filter; see <see cref="Unsubscribe(string)"/>.</param>
        /// <param name="target">The callback target. <see langword="null"/> matches every target.</param>
        /// <param name="disabled"><see langword="true"/> to suspend, <see langword="false"/> to resume.</param>
        /// <returns><see cref="RetCode.OK"/>, or the filter grammar's failure code.</returns>
        /// <remarks>
        /// The port of <c>of_disable(name, object, disabled)</c> (<c>:L1115-L1136</c>):
        /// <c>_of_Modify(name, object, "", false, MOD_DISABLE, disabled)</c>. See DECISION 13 on
        /// <see cref="Unsubscribe(object?, string)"/> for the call-site ambiguity this shares with
        /// <see cref="Disable(object?, string, bool)"/>.
        /// </remarks>
        public long Disable(string name, object? target, bool disabled)
        {
            // :L1135
            return Modify(name, target, string.Empty, excluding: false, ModificationKind.Disable, disabled);
        }

        /// <summary>
        /// Suspends or resumes every subscription matching a filter.
        /// </summary>
        /// <param name="name">The filter; see <see cref="Unsubscribe(string)"/>.</param>
        /// <param name="disabled"><see langword="true"/> to suspend, <see langword="false"/> to resume.</param>
        /// <returns><see cref="RetCode.OK"/>, or the filter grammar's failure code.</returns>
        /// <remarks>
        /// The port of <c>of_disable(name, disabled)</c> (<c>:L1138-L1163</c>):
        /// <c>_of_Modify(name, nullObject, "", false, MOD_DISABLE, disabled)</c>. This is the form
        /// <c>w_test_eventful.srw:L153</c> exercises live, with thirteen further spellings commented out
        /// beside it at <c>:L156-L189</c> as a specification of the grammar.
        /// </remarks>
        public long Disable(string name, bool disabled)
        {
            // :L1160-L1162
            return Modify(name, target: null, string.Empty, excluding: false, ModificationKind.Disable, disabled);
        }

        /// <summary>
        /// Suspends or resumes the subscriptions a given target registered for a given handler, across
        /// every event name.
        /// </summary>
        /// <param name="target">The callback target. <see langword="null"/> matches every target.</param>
        /// <param name="handlerName">The handler name. Case-insensitive. Empty matches every handler.</param>
        /// <param name="disabled"><see langword="true"/> to suspend, <see langword="false"/> to resume.</param>
        /// <returns><see cref="RetCode.OK"/>, or the filter grammar's failure code.</returns>
        /// <remarks>
        /// The port of <c>of_disable(object, evtName, disabled)</c> (<c>:L1165-L1185</c>):
        /// <c>_of_Modify("", object, evtName, false, MOD_DISABLE, disabled)</c>. See DECISION 13 on
        /// <see cref="Unsubscribe(object?, string)"/> for the ambiguity this shares with
        /// <see cref="Disable(string, object?, bool)"/>.
        /// </remarks>
        public long Disable(object? target, string handlerName, bool disabled)
        {
            // :L1184
            return Modify(string.Empty, target, handlerName, excluding: false, ModificationKind.Disable, disabled);
        }

        /// <summary>
        /// Suspends or resumes every subscription a given target registered.
        /// </summary>
        /// <param name="target">The callback target. <see langword="null"/> matches every target.</param>
        /// <param name="disabled"><see langword="true"/> to suspend, <see langword="false"/> to resume.</param>
        /// <returns><see cref="RetCode.OK"/>, or the filter grammar's failure code.</returns>
        /// <remarks>
        /// The port of <c>of_disable(object, disabled)</c> (<c>:L1187-L1206</c>):
        /// <c>_of_Modify("", object, "", false, MOD_DISABLE, disabled)</c>.
        /// </remarks>
        public long Disable(object? target, bool disabled)
        {
            // :L1205
            return Modify(string.Empty, target, string.Empty, excluding: false, ModificationKind.Disable, disabled);
        }

        /// <summary>
        /// Suspends or resumes <b>every</b> subscription.
        /// </summary>
        /// <param name="disabled"><see langword="true"/> to suspend, <see langword="false"/> to resume.</param>
        /// <returns><see cref="RetCode.OK"/>.</returns>
        /// <remarks>
        /// The port of <c>of_disable(disabled)</c> (<c>:L1208-L1228</c>):
        /// <c>_of_Modify("", nullObj, "", false, MOD_DISABLE, disabled)</c>. Unlike
        /// <see cref="Unsubscribe()"/> this form has no persistence subtlety attached, because nothing in
        /// the corpus disables in bulk with a namespace criterion.
        /// </remarks>
        public long Disable(bool disabled)
        {
            // :L1225-L1227
            return Modify(string.Empty, target: null, string.Empty, excluding: false, ModificationKind.Disable, disabled);
        }

        /// <summary>
        /// Suspends or resumes every subscription <i>except</i> those a given target registered, when
        /// <paramref name="excluding"/> is <see langword="true"/>.
        /// </summary>
        /// <param name="target">The callback target the match is built on.</param>
        /// <param name="disabled"><see langword="true"/> to suspend, <see langword="false"/> to resume.</param>
        /// <param name="excluding"><see langword="true"/> to invert the whole composite match.</param>
        /// <returns><see cref="RetCode.OK"/>, or the filter grammar's failure code.</returns>
        /// <remarks>
        /// The port of <c>of_disable(object, disabled, excluding)</c> (<c>:L1230-L1250</c>):
        /// <c>_of_Modify("", object, "", excluding, MOD_DISABLE, disabled)</c>. Exercised by
        /// <c>w_test_eventful.srw:L137</c>. Note the legacy's own argument order puts
        /// <c>disabled</c> before <c>excluding</c>, which is preserved even though it reads oddly beside
        /// <see cref="Unsubscribe(object?, bool)"/>.
        /// </remarks>
        public long Disable(object? target, bool disabled, bool excluding)
        {
            // :L1249
            return Modify(string.Empty, target, string.Empty, excluding, ModificationKind.Disable, disabled);
        }

        /// <summary>
        /// The single engine behind all fourteen unsubscribe and disable overloads: the port of
        /// <c>_of_modify</c> (<c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L993-L1090</c>).
        /// </summary>
        /// <param name="filter">The filter string, in the filter grammar.</param>
        /// <param name="target">The target criterion, or <see langword="null"/> for "any target".</param>
        /// <param name="handlerName">The handler criterion, or empty for "any handler".</param>
        /// <param name="excluding">Whether to invert the whole composite match.</param>
        /// <param name="kind">Which modification to apply.</param>
        /// <param name="value">The value a <see cref="ModificationKind.Disable"/> assigns.</param>
        /// <returns>
        /// <see cref="RetCode.OK"/>, or <see cref="RetCode.E_INVALID_ARGUMENT"/> when the filter negates a
        /// name it does not have.
        /// </returns>
        /// <remarks>
        /// <para>
        /// <b>Two independent levels of negation, and keeping them distinct is the point.</b> The
        /// per-criterion <c>'^'</c> negates one criterion - a name, or a namespace, independently
        /// (<c>:L1008-L1015</c>, applied at <c>:L1032-L1043</c>) - while
        /// <paramref name="excluding"/> inverts the entire composite result once every criterion has been
        /// evaluated (<c>:L1051-L1053</c>). They compose, and collapsing them into one flag would silently
        /// change which subscriptions a filter reaches.
        /// </para>
        /// <para>
        /// <b>The lexical bounds are recomputed as the loop runs, under a condition that is subtle and is
        /// reproduced literally</b> (<c>:L1073</c>): an entry contributes to the bounds when it did
        /// <i>not</i> match, <b>or</b> when the operation is a disable. For a removal that means the bounds
        /// are rebuilt from the survivors only; for a disable, where nothing is removed, it means they are
        /// rebuilt from every entry - and within that, only entries which are neither tombstoned nor
        /// suspended actually contribute (<c>:L1074</c>), which is what lets a disable narrow the bounds
        /// and a re-enable widen them again.
        /// </para>
        /// </remarks>
        private long Modify(
            string filter,
            object? target,
            string handlerName,
            bool excluding,
            ModificationKind kind,
            bool value)
        {
            // :L998 - evtName = Lower(evtName). Folded before the emptiness test below, exactly as the
            // oracle folds before computing bNoEvtName at :L1019.
            string foldedHandlerName = handlerName.ToLowerInvariant();

            // :L1000-L1023 - the filter grammar: split on the FIRST namespace separator, then test each
            // half independently for a leading negation, then reject the one invalid combination. All of
            // it lives in SubscriptionTopic.ParseFilter, which returns the legacy code unchanged.
            long parseCode = SubscriptionTopic.ParseFilter(filter, out SubscriptionTopic? criteria);
            if (parseCode != RetCode.OK)
            {
                // :L1022 - a negated name with nothing after the '^'. Returned BEFORE the bounds are
                // reset, so a rejected filter leaves the broker completely untouched; resetting first and
                // validating second would blank the bounds on an invalid call.
                return parseCode;
            }

            if (criteria is null)
            {
                // As in Subscribe: the parser pairs a success code with a non-null result, so this maps a
                // contract violation onto the code the oracle uses for an unusable filter.
                return RetCode.E_INVALID_ARGUMENT;
            }

            // :L1018 - bNoObject = IsNull(object). A null target means the target criterion does not
            // participate, which is how every "all subscriptions" overload is expressed.
            bool noTarget = target is null;

            // :L1019 - bNoEvtName = (evtName = "")
            bool noHandlerName = foldedHandlerName.Length == 0;

            // :L1025-L1026 - both bounds are cleared and then rebuilt by the loop below.
            _firstName = string.Empty;
            _lastName = string.Empty;

            // EVENTDATA newEvents[] at :L996 - the survivor list a depth-zero removal rewrites the table
            // from. Left empty and unused on every other path, exactly as the legacy leaves its array.
            List<EventSubscription> survivors = [];
            bool dirty = false;

            // :L1028 - nCount = UpperBound(Events). Captured once; nothing in this loop resizes the table.
            int count = _subscriptions.Count;

            for (int index = 0; index < count; index++)
            {
                EventSubscription entry = _subscriptions[index];

                // :L1030-L1044 - the name and namespace halves of the match, including the empty-name
                // "matches everything" rule, both per-criterion negations, and the three-state namespace
                // criterion whose ABSENT state skips the namespace test altogether.
                bool matched = criteria.Matches(entry.Name, entry.Namespace);

                // :L1045-L1047 - the target narrows the match when one was supplied. Reference identity,
                // because the oracle compares two powerobject pointers with '=' and PowerBuilder object
                // equality is identity.
                if (matched && !noTarget)
                {
                    matched = ReferenceEquals(entry.Target, target);
                }

                // :L1048-L1050 - the handler name narrows it further. Both sides are already folded, so an
                // ordinal comparison here IS the case-insensitive comparison the legacy performs.
                if (matched && !noHandlerName)
                {
                    matched = string.Equals(entry.HandlerName, foldedHandlerName, StringComparison.Ordinal);
                }

                // :L1051-L1053 - the OUTER negation, applied last and to the whole composite.
                if (excluding)
                {
                    matched = !matched;
                }

                if (kind == ModificationKind.Off)
                {
                    // :L1054-L1067
                    if (_deep > 0)
                    {
                        // :L1055-L1059 - a dispatch is in flight, so removal is deferred: mark the
                        // tombstone and remember that a compaction is owed. Removing here would rewrite
                        // the table that every active dispatch level holds a cursor into.
                        if (matched)
                        {
                            entry.IsInvalid = true;
                            dirty = true;
                        }
                    }
                    else
                    {
                        // :L1060-L1066 - no dispatch, so the table is rebuilt from the survivors. The
                        // legacy's Destroy of the matched entry's invoker (:L1062) has no analogue under
                        // C-D: there is no invoker, and dropping the reference is the whole of the
                        // release.
                        if (matched)
                        {
                            dirty = true;
                        }
                        else
                        {
                            survivors.Add(entry);
                        }
                    }
                }
                else if (kind == ModificationKind.Disable)
                {
                    // :L1068-L1072 - assign the flag to matched entries and never remove anything. Note
                    // the value is ASSIGNED rather than OR-ed, so this both disables and re-enables.
                    if (matched)
                    {
                        entry.IsDisabled = value;
                    }
                }

                // :L1073-L1078 - the bounds recomputation, and its condition is reproduced literally
                // because it is what makes the two operations behave differently: a removal rebuilds from
                // survivors, a disable rebuilds from everything.
                if (!matched || kind == ModificationKind.Disable)
                {
                    if (!entry.IsInvalid && !entry.IsDisabled)
                    {
                        // First-wins for the lower bound and last-wins for the upper, which is correct
                        // precisely because the table is kept in ascending name order.
                        if (_firstName.Length == 0)
                        {
                            _firstName = entry.Name;
                        }

                        _lastName = entry.Name;
                    }
                }
            }

            if (dirty)
            {
                // :L1081-L1087
                if (_deep > 0)
                {
                    // :L1083 - Post _of_Collect(). The second and last use of the Win32 message queue in
                    // the oracle, and it goes through the same queue as a posted dispatch so the two keep
                    // their relative order. See DECISION 2 on Post.
                    EnqueueContinuation(Collect);
                }
                else
                {
                    // :L1085 - Events = newEvents
                    ReplaceSubscriptions(survivors);
                }
            }

            // :L1089
            return RetCode.OK;
        }

        // =============================================================================================
        //  THE DISPATCH ENGINE - _of_trigger, n_cst_eventful.sru:L785-L974
        //  Ported statement by statement. Every locator below is exact and every ordering is contract.
        // =============================================================================================

        /// <summary>
        /// Walks the subscription table and invokes every matching subscription in order: the port of
        /// <c>_of_trigger</c> (<c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L785-L974</c>).
        /// </summary>
        /// <param name="name">The event name.</param>
        /// <param name="arguments">The payload, positionally.</param>
        /// <param name="isPost">Whether this dispatch was queued rather than triggered.</param>
        /// <returns>The last invoked handler's value, with the default substitution of <c>:L966-L970</c>.</returns>
        /// <remarks>
        /// <para>
        /// <b>DECISION 6 (AAP 0.7.3 C-K) - the <c>IsValid(this)</c> checks have no .NET analogue, and that
        /// is a decision rather than a silent omission.</b> The oracle re-tests <c>IsValid(this)</c> after
        /// every callback (<c>:L869</c>, <c>:L905</c>, <c>:L943</c> and <c>:L950</c>) and takes the guarded
        /// branch only when it holds, because a PowerBuilder subscriber can execute
        /// <c>Destroy</c> on the broker from inside a handler and the broker must not touch its own state
        /// afterwards. .NET has no such operation: the dispatch frame's <c>this</c> is a live reference
        /// that keeps the object reachable for the whole call, no callback can make it unreachable, and
        /// DECISION 14 deliberately declines to invent an <see cref="IDisposable"/> lifecycle that could
        /// stand in for one. So the legacy's <c>bIsValid</c> is constant <see langword="true"/> on every
        /// path a .NET port can reach, the four guarded branches are taken unconditionally, the two
        /// <c>if Not bIsValid then exit</c> statements at <c>:L821</c> and <c>:L937</c> are unreachable and
        /// are therefore not written, and the observable behaviour is identical. Each of the four sites
        /// carries a note saying so, so a reader comparing against the oracle can see the omission was
        /// deliberate.
        /// </para>
        /// <para>
        /// <b>DECISION 10 (AAP 0.7.3 C-K) - the midpoint probe rounds differently and it is
        /// unobservable.</b> At <c>:L802-L806</c> the oracle probes <c>Events[nCount / 2]</c> and, when
        /// that entry's name sorts before the target, starts scanning at <c>nCount / 2 + 1</c>.
        /// PowerScript's <c>/</c> is real division and the result is rounded when used as a subscript, so
        /// for an odd count PowerBuilder probes one slot further along than C# integer division does. The
        /// difference cannot be observed: the guard only ever advances the start past entries whose names
        /// are <i>provably</i> ordinally smaller than the target in an ascending list, and such entries can
        /// never match, so both roundings skip only non-matching entries and both scan the identical
        /// matching run. C# integer division is used, and the equality is stated here rather than left for
        /// a reader to re-derive.
        /// </para>
        /// </remarks>
        private object? Dispatch(string name, object?[] arguments, bool isPost)
        {
            // :L793 - if name = "" then return _aDefRetVal. Note this returns the GLOBAL default and not
            // the resolved one, because no name means there is nothing to resolve against.
            if (string.IsNullOrEmpty(name))
            {
                return _globalDefaultReturnValue;
            }

            // :L795 - aDefRetVal = _of_GetDefaultReturnValue(name). Resolved FIRST, because the two
            // lexical rejects below return it.
            (bool defaultEstablished, object? defaultReturnValue) = ResolveDefaultReturnValue(name);

            // :L797 - if name < _sFirstName then return aDefRetVal
            if (string.CompareOrdinal(name, _firstName) < 0)
            {
                return defaultReturnValue;
            }

            // :L798 - if name > _sLastName then return aDefRetVal. With an empty table both bounds are the
            // empty string, so any non-empty name trips this one and nothing is dispatched - which is how
            // an unsubscribed event costs nothing at all.
            if (string.CompareOrdinal(name, _lastName) > 0)
            {
                return defaultReturnValue;
            }

            // :L800-L806 - the midpoint start probe. See DECISION 10 above for the rounding note.
            int startIndex = 0;
            int count = _subscriptions.Count;
            if (count > 3)
            {
                // The oracle's one-based midpoint is nCount / 2, so the zero-based subscript is one less;
                // and its one-based start of nCount / 2 + 1 is the zero-based nCount / 2.
                int midpoint = count / 2;
                if (string.CompareOrdinal(_subscriptions[midpoint - 1].Name, name) < 0)
                {
                    startIndex = midpoint;
                }
            }

            // :L808-L817 - save every piece of per-dispatch state, then establish this level's. Saving
            // rather than resetting is what makes the broker re-entrant: a nested dispatch restores the
            // enclosing level's view on the way out.
            long savedDeep = _deep;
            _deep++;
            bool savedIsPost = _isPost;
            _isPost = isPost;
            VetoResult savedPrevent = _prevent;
            _prevent = VetoResult.Continue;
            object? savedReturnValue = _returnValue;
            _returnValue = null;

            // :L817 - aVal = _aRetVal, taken immediately after the line above nulled it, so this starts
            // null. DECLARED OUTSIDE THE LOOP AND NULLED ONLY INSIDE THE INNER TRY, which is behaviour and
            // not tidiness: a subscription skipped by the tombstone, disabled or capture tests at :L829,
            // :L830 and :L832 `continue`s BEFORE that nulling, so it leaves the previous invocation's
            // value in place and that value is what this dispatch returns. Hoisting the nulling to the top
            // of the loop would silently change the returned value.
            object? lastValue = null;

            // :L786-L787 - the loop's own locals.
            CaptureMode handledState = CaptureMode.Unhandled;
            bool subscribed = false;
            bool collect = false;
            EventBroker? savedCurrent = null;

            // :L845 - sThisClsName is captured at the first match. Seeded here instead, which is the same
            // value: the only consumer is the outermost-level exception prefix at :L900, and that site is
            // reachable only after the first-match block has run.
            string dispatchClassName = _thisClassName;

            // The one-based depth this dispatch owns a cursor slot for, matching _nIdxStack[_nDeep].
            int depthSlot = (int)_deep;
            while (_indexStack.Count < depthSlot)
            {
                _indexStack.Add(0);
            }

            try
            {
                // :L820 - for _nIdxStack[_nDeep] = nIdxStart to UpperBound(Events)
                //
                // THE UPPER BOUND IS EVALUATED ONCE, at loop entry, because that is what a PowerScript
                // `for` does - and it is HALF of the documented rule that a subscription made during a
                // dispatch takes effect only on the next dispatch (w_test_eventful.srw:L237). The other
                // half is the index-stack fix-up in Subscribe. Capturing the limit in a local reproduces
                // it exactly; a `< _subscriptions.Count` condition would break the rule silently.
                int limit = _subscriptions.Count;

                // The loop variable IS the shared cursor, so the condition and the increment read and
                // write it rather than a private copy: Subscribe may advance it from inside a handler, and
                // the very next test must see that. C#'s `continue` runs the increment expression, exactly
                // as PowerScript's `continue` advances its `for`, so every `continue` below lands where the
                // oracle's does.
                _indexStack[depthSlot - 1] = startIndex;

                for (; _indexStack[depthSlot - 1] < limit; _indexStack[depthSlot - 1]++)
                {
                    // :L821 - if Not bIsValid then exit. Unreachable in .NET; see DECISION 6.

                    // :L822 - if _nPrevent <> 0 then exit. Tested BEFORE the cursor is read, so a
                    // prevention raised by the previous handler - or from inside OnPrepare - stops the
                    // dispatch here.
                    if (_prevent != VetoResult.Continue)
                    {
                        break;
                    }

                    // :L823 - nIndex = _nIdxStack[_nDeep]
                    int index = _indexStack[depthSlot - 1];
                    EventSubscription entry = _subscriptions[index];

                    // :L824 - if Events[nIndex].name <> name then
                    if (!string.Equals(entry.Name, name, StringComparison.Ordinal))
                    {
                        // :L825 - the matching run is contiguous, so once it has been entered the first
                        // non-matching name ends the dispatch.
                        if (subscribed)
                        {
                            break;
                        }

                        // :L826 - the list is ascending, so a name greater than the target means the
                        // target is not present at all.
                        if (string.CompareOrdinal(entry.Name, name) > 0)
                        {
                            break;
                        }

                        // :L827 - still below the target; keep scanning.
                        continue;
                    }

                    // :L829 - a tombstoned entry is skipped but not removed.
                    if (entry.IsInvalid)
                    {
                        continue;
                    }

                    // :L830 - a suspended entry is skipped.
                    if (entry.IsDisabled)
                    {
                        continue;
                    }

                    // :L831-L833 - the capture filter: skip when the entry's mode differs from the
                    // dispatch's CURRENT handled state, unless the entry captures everything. The
                    // coupling to the handled latch below is the interesting part - as soon as one
                    // subscriber handles the event, the remaining unhandled-only subscribers stop being
                    // eligible and the handled-only ones start.
                    if (entry.Capture != handledState && entry.Capture != CaptureMode.All)
                    {
                        continue;
                    }

                    // :L834-L837 - a target that is no longer usable is skipped and a compaction is owed.
                    // Predicates.IsValidObject documents that a non-null reference always answers true in
                    // .NET, so this branch cannot be taken by a faithful port; it is reproduced because
                    // its absence would leave a reader hunting for the compaction trigger.
                    if (!Predicates.IsValidObject(entry.Target))
                    {
                        collect = true;
                        continue;
                    }

                    // :L838-L848 - the first-match block, which runs exactly once per dispatch.
                    if (!subscribed)
                    {
                        // :L839-L843 - the hook fires once, on the first matching subscription only, and a
                        // prevented result leaves the loop directly WITHOUT setting the prevent state, so
                        // nothing survives into the enclosing dispatch.
                        if (_subclassing && Predicates.IsPrevented(OnTriggering(name, isPost)))
                        {
                            break;
                        }

                        // :L844
                        subscribed = true;

                        // :L845
                        dispatchClassName = _thisClassName;

                        // :L846-L847 - Message.PowerObjectParm is snapshotted and replaced with this
                        // broker. See DECISION 5 on Current.
                        savedCurrent = CurrentBroker.Value;
                        CurrentBroker.Value = this;
                    }

                    // :L849-L851 - captured before the invocation because the catch and finally both read
                    // them and the entry may have been tombstoned by then.
                    string classChain = entry.ClassChain;
                    string handlerName = entry.HandlerName;
                    bool wasInvoking = entry.IsInvoking;

                    // :L852-L856 - the oracle creates a FRESH invoker when this entry is already being
                    // invoked further up the stack, so a re-entrant invocation cannot overwrite the outer
                    // one's argument buffer. That isolation is structural here rather than conditional:
                    // PassArguments allocates the argument array per invocation, so an outer invocation's
                    // arguments live in a different array and there is nothing to clobber. The observable
                    // half of the legacy branch - restoring the flag to its CAPTURED value rather than to
                    // false - is reproduced in the finally at :L908.
                    try
                    {
                        // :L858 - SetNull(aVal). See the note on lastValue's declaration for why this
                        // nulling belongs here and nowhere else.
                        lastValue = null;

                        // :L859 - the oracle re-binds the invoker to the stored method id and raises when
                        // that fails. Re-checked the same way and at the same point: the stored handler
                        // must still be applicable to this target.
                        MethodInfo? handler = ResolveDispatchHandler(entry);
                        if (handler is null)
                        {
                            // :L860 - pfwThrowException("Invalid method"). The message is the oracle's,
                            // byte for byte, and the raise goes through Kernel so the exception type is
                            // the framework's own rather than a locally invented one.
                            Exceptions.PfwThrowException("Invalid method");
                        }

                        // :L862-L864 - _of_PassArgs returns false when an OnPrepare override prevented,
                        // and the loop skips this subscriber only. The `continue` runs the finally below,
                        // where lastValue is null, so this path also resets the returned value - which is
                        // the oracle's behaviour and differs from the skips at :L829, :L830 and :L832.
                        if (!PassArguments(name, entry.Target!, handler, arguments, out object?[] finalArguments))
                        {
                            continue;
                        }

                        // :L865
                        entry.IsInvoking = true;

                        // :L866 - aVal = invoker.Invoke()
                        lastValue = InvokeHandler(handler, entry.Target!, finalArguments);
                    }
                    catch (Exception ex)
                    {
                        // ---------------------------------------------------------------------------
                        //  EXCEPTION CAPTURE - :L867-L902
                        // ---------------------------------------------------------------------------

                        // :L868 - sException = ex.text
                        //
                        // The oracle reads back whatever is CURRENTLY in ex.text, which is the original
                        // message on a first capture and the PREVIOUS dispatch level's four-line block on
                        // a nested rethrow. Under DECISION 7 the block lives on Exception.Data rather than
                        // on the message, so the read has to consult both in that order - reading only
                        // Message would silently discard the inner level's block and collapse the
                        // progressively indented nesting the oracle produces down to a single frame.
                        string detail = GetDispatchExceptionText(ex) ?? ex.Message;

                        // :L869 - bIsValid = IsValid(this). Constant true; see DECISION 6. The whole
                        // block at :L870-L882 is therefore entered unconditionally.

                        // :L871-L880 - THE LATCH. The inner decoration is applied only the FIRST time an
                        // exception is captured in this dispatch chain, so an exception that propagates
                        // out of a nested dispatch is not decorated twice. The outer four-line block
                        // below is applied every time, which is what produces the progressively indented
                        // nesting the oracle emits.
                        if (!_hasException)
                        {
                            _hasException = true;

                            if (TryReadAssertionDetail(ex, out string? assertionDetail))
                            {
                                // :L876 - f.#Info + "~nStackTrace:~n" + f.#StackTraceInfo
                                detail = assertionDetail;
                            }
                            else
                            {
                                // :L878 - "[" + ClassName(ex) + "]~n" + sException
                                detail = string.Concat("[", ex.GetType().Name, "]\n", detail);
                            }
                        }

                        // :L881 - sException = _of_AlignString("Exception: ~n", sException)
                        detail = AlignContinuationLines(ExceptionLabel, detail);

                        // :L883-L886 - the four-line block. EVERY LABEL IS LITERAL AND IS PRESERVED BYTE
                        // FOR BYTE, including the space after each colon and the space before the newline
                        // in ExceptionLabel. Concatenated in the oracle's order with the oracle's
                        // separators.
                        string text = string.Concat(
                            "Subscribe: ", name, "\n",
                            "Target: ", classChain, "\n",
                            "Event: ", handlerName, "\n",
                            ExceptionLabel, detail);

                        // The oracle assigns this to ex.text. .NET has no settable Message, so the block
                        // is recorded on Exception.Data instead and the instance is rethrown unchanged.
                        // See DECISION 7 on DispatchExceptionTextKey for the two alternatives rejected.
                        SetDispatchExceptionText(ex, text);

                        // :L887-L897 - the hook, and its own three-valued alphabet. Reached with the
                        // decorated text already recorded, exactly as the oracle reaches it with ex.text
                        // already assigned.
                        if (_subclassing)
                        {
                            long exceptionResult = OnException(name, ex);

                            // :L890-L891 - case 1 //Prevent
                            if (exceptionResult == ExceptionResultPrevent)
                            {
                                break;
                            }

                            // :L892-L894 - case 2 //Continue, which also clears the latch so the NEXT
                            // exception in this dispatch is decorated afresh.
                            if (exceptionResult == ExceptionResultContinue)
                            {
                                _hasException = false;
                                continue;
                            }
                        }

                        // :L899-L901 - at the outermost level only, the broker's own class name is
                        // prefixed before the exception leaves. nDeep is the SAVED depth, so zero means
                        // this dispatch is the outermost one.
                        if (savedDeep == 0)
                        {
                            SetDispatchExceptionText(
                                ex,
                                string.Concat(dispatchClassName, "\n", GetDispatchExceptionText(ex)));
                        }

                        // :L902 - throw ex. A bare rethrow, which preserves the original stack trace;
                        // `throw ex;` would reset it and PowerBuilder's throw does not.
                        throw;
                    }
                    finally
                    {
                        // :L904 - if bInvoking then Destroy invoker. No invoker exists under C-D, so
                        // there is nothing to destroy.

                        // :L905 - bIsValid = IsValid(this). Constant true; see DECISION 6.

                        // :L907 - if Not bInvoking then invoker.Release(). No analogue, as above.

                        // :L908 - RESTORED TO THE CAPTURED VALUE, NOT TO FALSE. A re-entrant invocation
                        // must leave the flag set for the outer invocation still on the stack.
                        entry.IsInvoking = wasInvoking;

                        // :L909 - only a non-null return participates in handled detection. A void handler
                        // and a handler that returned null are therefore indistinguishable, which is
                        // exactly the oracle's position: "no return value is defined as not handled"
                        // (w_test_eventful.srw:L234).
                        if (lastValue is not null)
                        {
                            if (handledState == CaptureMode.Unhandled)
                            {
                                // :L910-L924 - THE LATCH SET. Three ways to become handled, and a fourth:
                                // a comparison that raises. IsDifferentFrom raises for an incomparable
                                // pair exactly as PowerScript's '<>' does, so the catch arm below is the
                                // reproduction of :L919-L921 and is genuinely reachable.
                                bool handled;

                                try
                                {
                                    handled =
                                        // :L912 - a null default: any value at all is a handled event.
                                        defaultReturnValue is null

                                        // :L914 - ClassName(aDefRetVal) = "any", i.e. NO DEFAULT HAS BEEN
                                        // ESTABLISHED. Distinct from a default of null; see DECISION 9.
                                        || !defaultEstablished

                                        // :L916 - a value that differs from the default. The three-valued
                                        // comparison cannot yield null here, because both operands are
                                        // known non-null by the two tests above.
                                        || IsDifferentFrom(lastValue, defaultReturnValue) == true;
                                }
                                catch (Exception)
                                {
                                    // :L919-L921 - catch(throwable ex3) / nCap = CAP_HANDLED. An
                                    // incomparable pair counts as different, and therefore as handled.
                                    handled = true;
                                }

                                if (handled)
                                {
                                    // :L913 / :L915 / :L917 - the latch, which CANNOT ROLL BACK: nothing
                                    // anywhere sets the handled state back to unhandled
                                    // (w_test_eventful.srw:L235).
                                    handledState = CaptureMode.Handled;

                                    // :L922-L924
                                    _returnValue = lastValue;
                                }
                            }
                            else
                            {
                                // :L925-L933 - ALREADY HANDLED, so the value may still be OVERWRITTEN by
                                // this later subscriber (w_test_eventful.srw:L235) - but only when it
                                // differs from the default.
                                //
                                // PRESERVED LEGACY BEHAVIOUR WORTH READING TWICE: when the default is
                                // NULL, PowerScript's `aVal <> aDefRetVal` evaluates to NULL rather than
                                // to true, `if NULL then` is false, and the value is therefore NOT
                                // overwritten. IsDifferentFrom returns null for that case and `== true`
                                // is false, which reproduces it. This is why the legacy documentation
                                // advises always setting a default return value
                                // (w_test_eventful.srw:L257): without one, the overwrite rule it
                                // documents at :L235 does not actually apply.
                                try
                                {
                                    // :L927-L929
                                    if (IsDifferentFrom(lastValue, defaultReturnValue) == true)
                                    {
                                        _returnValue = lastValue;
                                    }
                                }
                                catch (Exception)
                                {
                                    // :L930-L932 - catch(throwable ex4) / _aRetVal = aVal
                                    _returnValue = lastValue;
                                }
                            }
                        }
                    }

                    // :L937 - if Not bIsValid then exit. Unreachable in .NET; see DECISION 6.
                }
            }
            finally
            {
                // :L939-L940 - the oracle wraps the loop in catch(throwable ex2) / throw ex2, which is a
                // plain rethrow and therefore adds nothing a bare try/finally does not already do. It is
                // deliberately not written out: an explicit `catch { throw; }` in C# is the same
                // behaviour with more code.

                // :L942-L949 - the triggered hook and the ambient restore, both guarded by the SAME "was
                // anything dispatched" latch that guards OnTriggering, so the pair is symmetric.
                if (subscribed)
                {
                    // :L943 - bIsValid; constant true, see DECISION 6.
                    if (_subclassing)
                    {
                        // :L945 - fires from the finally, so it is reached even when the loop ended in a
                        // prevention or an exception.
                        OnTriggered(name, isPost);
                    }

                    // :L948 - restored AFTER the hook, so an override still sees this broker as ambient.
                    CurrentBroker.Value = savedCurrent;
                }

                // :L950 - bIsValid; constant true, see DECISION 6.

                // :L951 - the accumulated value is restored, which is what scopes GetReturnValue to the
                // level that is running. See its remarks.
                _returnValue = savedReturnValue;

                // :L952-L953
                _deep = savedDeep;
                _isPost = savedIsPost;

                // :L954-L958 - THE PREVENT UNWIND, and the reason the veto is tri-valued.
                if (savedPrevent != VetoResult.Continue)
                {
                    // :L955 - an enclosing level was already preventing, so its state is restored and
                    // this level's is discarded.
                    _prevent = savedPrevent;
                }
                else if (_prevent == VetoResult.PreventOnce || _deep == 0)
                {
                    // :L956-L957 - a ONCE prevention is consumed by the level that raised it, and any
                    // prevention is consumed once the depth is back to zero. Note _deep has ALREADY been
                    // restored on the line above, so this reads the enclosing depth.
                    //
                    // Everything else falls through untouched, which is the whole point: a DEEP
                    // prevention at depth greater than zero SURVIVES the unwind and stops the enclosing
                    // dispatch too, at its next :L822 test. A once prevention does not. Flattening the
                    // veto to a boolean would silently turn every deep prevention into a shallow one.
                    _prevent = VetoResult.Continue;
                }

                // :L959-L961 - the exception latch is cleared only at the outermost level, so a nested
                // rethrow stays undecorated all the way out.
                if (_deep == 0)
                {
                    _hasException = false;
                }

                // :L962-L964 - the owed compaction. Collect itself no-ops above depth zero, so at an
                // inner level this call is deliberately inert and the outermost level does the work.
                if (collect)
                {
                    Collect();
                }

                // :L966-L970 - THE FINAL DEFAULT SUBSTITUTION, and it applies to a non-posted dispatch
                // ONLY. A posted dispatch returns its raw value - which nothing reads, because Post is a
                // subroutine - so substituting there would be invisible; not substituting is nonetheless
                // what the oracle does and what is reproduced.
                if (!isPost && lastValue is null)
                {
                    lastValue = defaultReturnValue;
                }
            }

            // :L973 - return aVal. THE LAST INVOCATION'S RAW VALUE, not the accumulated _aRetVal. The two
            // differ whenever a later subscriber returned a value equal to the default, or returned
            // nothing at all: the accumulated value keeps the earlier handled value while this returns the
            // later raw one. GetReturnValue exposes the accumulated value, and only from inside the
            // dispatch.
            return lastValue;
        }

        // =============================================================================================
        //  STATE QUERIES, DEFAULT RETURN VALUES AND THE VETO
        // =============================================================================================

        /// <summary>
        /// The accumulated return value of the dispatch currently in progress.
        /// </summary>
        /// <returns>
        /// The value of the last subscriber that <b>handled</b> the event, or <see langword="null"/> when
        /// none has.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The port of <c>of_getreturnvalue</c> (<c>n_cst_eventful.sru:L620-L637</c>). <b>Meaningful only
        /// from inside a dispatch</b>, which the legacy's own header comment states: "the return value of
        /// the current event dispatch process (most recent, valid when of_IsProcessed returns true)".
        /// </para>
        /// <para>
        /// <b>The value is SCOPED to the dispatch level, and that scoping is preserved.</b> Every dispatch
        /// saves the accumulated value on entry and restores it on exit (<c>:L815-L816</c> and
        /// <c>:L951</c>), so an enclosing dispatch sees its own value again the moment a nested dispatch
        /// completes, and a caller who reads this after <see cref="Trigger"/> has returned sees whatever
        /// was there beforehand rather than the dispatch's result. <see cref="Trigger"/>'s own return value
        /// is the way to read a dispatch's outcome from outside - and note it is a different quantity, the
        /// last invocation's raw value rather than the last handled one.
        /// </para>
        /// </remarks>
        public object? GetReturnValue()
        {
            // :L636 - return _aRetVal
            return _returnValue;
        }

        /// <summary>
        /// Whether the dispatch currently in progress has been handled by a subscriber.
        /// </summary>
        /// <returns><see langword="true"/> when a subscriber has handled the event.</returns>
        /// <remarks>
        /// The port of <c>of_isprocessed</c> (<c>:L639-L656</c>), which is literally
        /// <c>Not IsNull(_aRetVal)</c>. It is therefore the same quantity as
        /// <see cref="GetReturnValue"/> being non-null and carries the same dispatch-level scoping - a
        /// subscriber that handled the event by returning a null value is indistinguishable from one that
        /// did not handle it, which is the oracle's position rather than an approximation.
        /// </remarks>
        public bool IsProcessed()
        {
            // :L655 - return Not IsNull(_aRetVal)
            return _returnValue is not null;
        }

        /// <summary>
        /// Whether the dispatch currently in progress was queued by <see cref="Post"/> rather than
        /// triggered.
        /// </summary>
        /// <returns><see langword="true"/> for a posted dispatch.</returns>
        /// <remarks>
        /// The port of <c>of_ispost</c> (<c>:L706-L723</c>). Like the two members above it is scoped to the
        /// dispatch level, saved at <c>:L810-L811</c> and restored at <c>:L953</c>, so a handler reached
        /// through a nested <see cref="Trigger"/> inside a posted dispatch correctly reports
        /// <see langword="false"/>. It is the same flag <see cref="OnTriggering"/> and
        /// <see cref="OnTriggered"/> receive as their <c>isPost</c> argument, which is how
        /// <c>n_cst_threading_eventful.sru:L62</c> and <c>:L69</c> decide whether to touch their
        /// synchronisation event.
        /// </remarks>
        public bool IsPost()
        {
            // :L722 - return _bIsPost
            return _isPost;
        }

        /// <summary>
        /// Whether any enabled subscription exists for an event name - a cheap pre-test a caller uses to
        /// avoid assembling a payload for an event nobody listens to.
        /// </summary>
        /// <param name="name">The event name, compared ordinally and case-sensitively.</param>
        /// <returns><see langword="true"/> when a subscription for that name appears to exist.</returns>
        /// <remarks>
        /// <para>
        /// The port of <c>of_issubscribed</c> (<c>n_cst_eventful.sru:L747-L783</c>).
        /// <c>se_cst_dw.sru:L166</c> and <c>:L316</c> both call it exactly this way, guarding a
        /// <c>Eventful.of_Trigger(...)</c> behind it.
        /// </para>
        /// <para>
        /// <b>PRESERVED LEGACY QUIRK (AAP 0.7.3 C-B) - the scan deliberately skips the first and last
        /// slots, and it is NOT corrected here.</b> The oracle's loop is <c>for nIndex = 2 to nCount - 1</c>
        /// (<c>:L775</c>), one-based, which visits neither slot 1 nor slot <c>nCount</c>. The reasoning is
        /// visible: the two short-circuits at <c>:L767-L768</c> compare the name against the lexical
        /// bounds, and in the common case those bounds ARE the first and last entries' names, so re-testing
        /// them in the loop would be redundant. The quirk is that the bounds track only entries which are
        /// neither tombstoned nor suspended, while slots 1 and <c>nCount</c> may be either - so a name held
        /// solely by a first or last entry whose neighbours moved the bounds can be missed, and this
        /// answers <see langword="false"/> for a subscription that does exist and would be dispatched.
        /// The loop range is reproduced verbatim, one-based, so the divergence is impossible to introduce
        /// accidentally in either direction, and the consequence is that this is a fast conservative
        /// pre-test rather than an authority.
        /// </para>
        /// </remarks>
        public bool IsSubscribed(string name)
        {
            // :L766 - if name = "" then return false
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            // :L767-L768 - equality with either bound short-circuits true.
            if (string.Equals(name, _firstName, StringComparison.Ordinal))
            {
                return true;
            }

            if (string.Equals(name, _lastName, StringComparison.Ordinal))
            {
                return true;
            }

            // :L769-L770 - outside the bounds there is nothing to find. With an empty table both bounds
            // are empty and the second test fires for any non-empty name.
            if (string.CompareOrdinal(name, _firstName) < 0)
            {
                return false;
            }

            if (string.CompareOrdinal(name, _lastName) > 0)
            {
                return false;
            }

            // :L772-L773
            int count = _subscriptions.Count;
            if (count == 0)
            {
                return false;
            }

            // :L775 - PRESERVED LEGACY QUIRK. One-based bounds, exactly as written: from the SECOND entry
            // to the count minus one, skipping the first and last slots. See this member's remarks. Do not
            // "fix" the range.
            for (int index = 2; index <= count - 1; index++)
            {
                EventSubscription entry = _subscriptions[index - 1];

                // :L776-L777
                if (entry.IsInvalid)
                {
                    continue;
                }

                if (entry.IsDisabled)
                {
                    continue;
                }

                // :L778
                if (string.Equals(entry.Name, name, StringComparison.Ordinal))
                {
                    return true;
                }

                // :L779 - the list is ascending, so a greater name means the target is not present.
                if (string.CompareOrdinal(entry.Name, name) > 0)
                {
                    return false;
                }
            }

            // :L782
            return false;
        }

        /// <summary>
        /// Registers the default return value for one event name, replacing any previous registration for
        /// that name.
        /// </summary>
        /// <param name="name">The event name. Must not be empty.</param>
        /// <param name="value">
        /// The default value. <see langword="null"/> is permitted and is meaningful - see the remarks on
        /// <see cref="DefaultReturnValue.Value"/>.
        /// </param>
        /// <returns>
        /// <see cref="RetCode.OK"/>; <see cref="RetCode.FAILED"/> while a dispatch is in progress; or
        /// <see cref="RetCode.E_INVALID_ARGUMENT"/> when <paramref name="name"/> is empty.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The port of <c>of_setdefaultreturnvalue(name, value)</c>
        /// (<c>n_cst_eventful.sru:L479-L517</c>).
        /// </para>
        /// <para>
        /// <b>The mid-dispatch rejection is behaviour, not defensiveness.</b> A dispatch resolves the
        /// default once, before its loop starts (<c>:L795</c>), and every handled-detection decision in
        /// that dispatch is measured against the resolved value. Allowing a change part-way through would
        /// make two subscribers in the same dispatch answer to different yardsticks, so the oracle refuses
        /// outright at <c>:L500</c> and so does this.
        /// </para>
        /// </remarks>
        public long SetDefaultReturnValue(string name, object? value)
        {
            // :L500
            if (_deep > 0)
            {
                return RetCode.FAILED;
            }

            // :L501
            if (string.IsNullOrEmpty(name))
            {
                return RetCode.E_INVALID_ARGUMENT;
            }

            // :L505-L511 - update in place when the name is already registered. First match wins and the
            // scan stops there, which matches the resolver's own first-match-wins search at :L546-L550.
            for (int index = 0; index < _defaultReturnValues.Count; index++)
            {
                if (string.Equals(_defaultReturnValues[index].Name, name, StringComparison.Ordinal))
                {
                    // :L508 - DefRetValues[nIndex].value = value. Expressed as a `with` because the record
                    // is immutable; the array slot is rewritten exactly as the oracle rewrites its field.
                    _defaultReturnValues[index] = _defaultReturnValues[index] with { Value = value };
                    return RetCode.OK;
                }
            }

            // :L513-L514 - otherwise append.
            _defaultReturnValues.Add(new DefaultReturnValue { Name = name, Value = value });

            // :L516
            return RetCode.OK;
        }

        /// <summary>
        /// Registers the global default return value, used for every event name that has no registration
        /// of its own.
        /// </summary>
        /// <param name="value">The default value. <see langword="null"/> is permitted.</param>
        /// <returns>
        /// <see cref="RetCode.OK"/>, or <see cref="RetCode.FAILED"/> while a dispatch is in progress.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The port of <c>of_setdefaultreturnvalue(value)</c> (<c>n_cst_eventful.sru:L519-L539</c>).
        /// </para>
        /// <para>
        /// <b>DECISION 9 (AAP 0.7.3 C-K) - calling this ESTABLISHES a default, which is a third state
        /// distinct from a default of <see langword="null"/>.</b> The handled-detection logic tests three
        /// things in order (<c>:L912-L917</c>): whether the default is null, whether
        /// <c>ClassName(aDefRetVal) = "any"</c>, and whether the returned value differs from it. That
        /// middle test is PowerScript asking "has this <c>any</c> variable ever been assigned a typed
        /// value at all?" - an unassigned <c>any</c> reports its class as <c>"any"</c>. C# has no such
        /// state for <c>object?</c>, so it is tracked explicitly by a companion flag that starts
        /// <see langword="false"/> (the constructor's <c>SetNull</c> leaves the variable untyped) and
        /// becomes <see langword="true"/> here - <b>even when <paramref name="value"/> is
        /// <see langword="null"/></b>, because assigning null is still an assignment.
        /// </para>
        /// <para>
        /// The two states happen to select the same arm today, since a null default and an unestablished
        /// one both mean "any value counts as handled". They are still kept apart, because the third test
        /// only runs when neither of the first two fires and collapsing them would make that ordering
        /// unverifiable.
        /// </para>
        /// <para>
        /// <b>This is the member a derived broker calls from its own constructor.</b>
        /// <c>ws_objects/pfw.thread.pbl.src/n_cst_threading_eventful.sru:L74-L77</c> calls
        /// <c>of_SetDefaultReturnValue(0)</c>, which installs a non-null established default and thereby
        /// moves the whole threading layer onto the third arm: a handler returning <c>0</c> is then NOT
        /// handled, and only a handler returning something else is. That is a materially different dispatch
        /// contract from the base broker's, established by one line in a constructor, which is why the
        /// constructor computes the subclassing flag eagerly rather than lazily.
        /// </para>
        /// </remarks>
        public long SetDefaultReturnValue(object? value)
        {
            // :L536
            if (_deep > 0)
            {
                return RetCode.FAILED;
            }

            // :L537 - _aDefRetVal = value
            _globalDefaultReturnValue = value;
            _globalDefaultReturnValueEstablished = true;

            // :L538
            return RetCode.OK;
        }

        /// <summary>
        /// Stops the dispatch in progress, optionally for the whole nested dispatch chain.
        /// </summary>
        /// <param name="deep">
        /// <see langword="false"/> to stop only the dispatch level that is running;
        /// <see langword="true"/> to stop the enclosing levels as well.
        /// </param>
        /// <returns>
        /// <see cref="RetCode.OK"/>, or <see cref="RetCode.FAILED"/> when no dispatch is in progress.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The port of <c>of_prevent(deep)</c> (<c>n_cst_eventful.sru:L1276-L1300</c>). Every subscriber
        /// after the caller is skipped (<c>w_test_eventful.srw:L236</c>), because the state is tested at
        /// the top of each loop iteration (<c>:L822</c>).
        /// </para>
        /// <para>
        /// <b>The once-versus-deep distinction is the reason the veto is tri-valued and must never be
        /// flattened to a boolean.</b> The unwind at <c>:L954-L958</c> treats the two differently: a
        /// <see cref="VetoResult.PreventOnce"/> is consumed by the level that raised it, while a
        /// <see cref="VetoResult.PreventDeep"/> raised below depth zero SURVIVES the unwind and stops the
        /// enclosing dispatch at its next iteration too. A boolean cannot carry that, and flattening it
        /// would silently turn every deep prevention into a shallow one.
        /// </para>
        /// <para>
        /// A hook may call this: <c>n_cst_threading_eventful.sru:L50</c> calls <c>of_Prevent()</c> from
        /// inside <c>onprepare</c> when the thread has been cancelled, and then returns <c>1</c> so this
        /// subscriber is skipped as well.
        /// </para>
        /// </remarks>
        public long Prevent(bool deep)
        {
            // :L1293 - outside a dispatch there is nothing to prevent, and the oracle reports that as a
            // failure rather than ignoring it.
            if (_deep <= 0)
            {
                return RetCode.FAILED;
            }

            // :L1294-L1298
            _prevent = deep ? VetoResult.PreventDeep : VetoResult.PreventOnce;

            // :L1299
            return RetCode.OK;
        }

        /// <summary>
        /// Stops the dispatch in progress, for this dispatch level only.
        /// </summary>
        /// <returns>
        /// <see cref="RetCode.OK"/>, or <see cref="RetCode.FAILED"/> when no dispatch is in progress.
        /// </returns>
        /// <remarks>
        /// The port of <c>of_prevent()</c> (<c>n_cst_eventful.sru:L1302-L1303</c>), a single-line
        /// delegation with <c>deep</c> false.
        /// </remarks>
        public long Prevent()
        {
            // :L1302 - return of_Prevent(false)
            return Prevent(deep: false);
        }

        // =============================================================================================
        //  PRIVATE MACHINERY
        // =============================================================================================

        /// <summary>
        /// Resolves the default return value for an event name, and whether one has been established at
        /// all: the port of <c>_of_getdefaultreturnvalue</c>
        /// (<c>n_cst_eventful.sru:L541-L553</c>).
        /// </summary>
        /// <param name="name">The event name to resolve for.</param>
        /// <returns>
        /// A pair carrying whether a default has been established (see DECISION 9 on
        /// <see cref="SetDefaultReturnValue(object?)"/>) and the value itself.
        /// </returns>
        /// <remarks>
        /// Three arms, in the oracle's order: with no per-name registrations at all the global default is
        /// returned without a search (<c>:L544</c>); otherwise the first registration whose name matches
        /// (<c>:L546-L550</c>); otherwise the global default again (<c>:L552</c>). A per-name registration
        /// always counts as established, because registering it was an assignment; the global default
        /// carries its own flag.
        /// </remarks>
        private (bool Established, object? Value) ResolveDefaultReturnValue(string name)
        {
            // :L543-L544 - the fast path, and it is the common one: nothing in the corpus registers a
            // per-name default, so this returns immediately.
            int count = _defaultReturnValues.Count;
            if (count == 0)
            {
                return (_globalDefaultReturnValueEstablished, _globalDefaultReturnValue);
            }

            // :L546-L550 - first match wins, linear, ordinal.
            for (int index = 0; index < count; index++)
            {
                if (string.Equals(_defaultReturnValues[index].Name, name, StringComparison.Ordinal))
                {
                    return (true, _defaultReturnValues[index].Value);
                }
            }

            // :L552
            return (_globalDefaultReturnValueEstablished, _globalDefaultReturnValue);
        }

        /// <summary>
        /// Compacts the subscription table, dropping tombstoned entries and entries whose target is no
        /// longer usable, and rebuilds the lexical bounds: the port of <c>_of_collect</c>
        /// (<c>n_cst_eventful.sru:L576-L603</c>).
        /// </summary>
        /// <remarks>
        /// <b>A no-op while a dispatch is in progress</b> (<c>:L580</c>), and that guard is the whole
        /// reason tombstoning exists: compacting mid-dispatch would rewrite the table every active level
        /// holds a cursor into, and no index fix-up can repair a removal the way the one in
        /// <see cref="Subscribe(string, object?, string, string)"/> repairs an insertion. It is reached
        /// from three places - the garbage sweep in <c>of_on</c> (<c>:L393</c>), the owed compaction at the
        /// end of a dispatch (<c>:L963</c>), and the queued continuation the modification engine leaves
        /// behind (<c>:L1083</c>) - and the guard makes the last two safe to call unconditionally.
        /// </remarks>
        private void Collect()
        {
            // :L580 - if _nDeep > 0 then return
            if (_deep > 0)
            {
                return;
            }

            // :L582-L583 - both bounds are cleared and rebuilt below.
            _firstName = string.Empty;
            _lastName = string.Empty;

            List<EventSubscription> survivors = [];

            // :L585
            int count = _subscriptions.Count;

            for (int index = 0; index < count; index++)
            {
                EventSubscription entry = _subscriptions[index];

                // :L587-L590 - an entry survives when it is neither tombstoned nor pointing at an unusable
                // target. The two tests are sequential rather than combined because the oracle writes them
                // that way, and because the second is the expensive one.
                bool valid = !entry.IsInvalid;
                if (valid)
                {
                    valid = Predicates.IsValidObject(entry.Target);
                }

                if (!valid)
                {
                    // :L591-L592 - the oracle destroys the dropped entry's invoker here. Nothing to do
                    // under C-D: dropping the reference is the whole of the release.
                    continue;
                }

                // :L594
                survivors.Add(entry);

                // :L595-L598 - the bounds are rebuilt from survivors that are neither tombstoned nor
                // suspended. The tombstone test is redundant at this point, because a tombstoned entry did
                // not survive - it is reproduced because the oracle writes it and because removing it would
                // be an editorial change to a condition rather than a translation of one.
                if (!entry.IsInvalid && !entry.IsDisabled)
                {
                    if (_firstName.Length == 0)
                    {
                        _firstName = entry.Name;
                    }

                    _lastName = entry.Name;
                }
            }

            // :L602 - Events = newEvents
            ReplaceSubscriptions(survivors);
        }

        /// <summary>
        /// Replaces the subscription table's contents in place: the port of the oracle's whole-array
        /// assignment <c>Events = newEvents</c> (<c>n_cst_eventful.sru:L602</c> and <c>:L1085</c>).
        /// </summary>
        /// <param name="replacement">The new contents, already in dispatch order.</param>
        /// <remarks>
        /// The list instance is reused rather than swapped so it can stay <see langword="readonly"/>, which
        /// removes any possibility of two code paths holding different tables. Both call sites are reached
        /// only at depth zero - <see cref="Collect"/> guards on it and the modification engine's other
        /// branch tombstones instead - so no cursor can be invalidated by this.
        /// </remarks>
        private void ReplaceSubscriptions(List<EventSubscription> replacement)
        {
            _subscriptions.Clear();
            _subscriptions.AddRange(replacement);
        }

        /// <summary>
        /// Queues a continuation for <see cref="DrainPostedContinuations"/> to run: the port of the
        /// oracle's <c>Post</c> statement (<c>n_cst_eventful.sru:L445-L477</c> and <c>:L1083</c>).
        /// </summary>
        /// <param name="continuation">The work to defer.</param>
        /// <remarks>
        /// A single FIFO queue serves both call sites - a posted dispatch and a deferred compaction - which
        /// is what preserves their relative order. The Win32 message queue the oracle posts to is likewise
        /// one ordered queue, so two mechanisms here would be a divergence. See DECISION 2 on
        /// <see cref="Post"/>.
        /// </remarks>
        private void EnqueueContinuation(Action continuation)
        {
            _postedContinuations.Enqueue(continuation);
        }

        /// <summary>
        /// Builds the argument array for one invocation and gives an <see cref="OnPrepare"/> override its
        /// chance to adjust it: the port of <c>_of_passargs</c>
        /// (<c>n_cst_eventful.sru:L605-L618</c>).
        /// </summary>
        /// <param name="name">The event name being dispatched.</param>
        /// <param name="target">The subscription's callback target.</param>
        /// <param name="handler">The resolved handler.</param>
        /// <param name="arguments">The trigger's payload.</param>
        /// <param name="finalArguments">The array the handler will be invoked with.</param>
        /// <returns>
        /// <see langword="true"/> to invoke the handler, or <see langword="false"/> when an
        /// <see cref="OnPrepare"/> override prevented and this subscriber must be skipped.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The observable contract, documented at <c>w_test_eventful.srw:L240-L247</c> and reproduced
        /// exactly: <b>a handler's parameter count need not match the trigger's argument count</b>, but the
        /// order must match and the types must be compatible; <b>missing parameters receive their type's
        /// initial value</b> - the worked example gives a <c>string arg2</c> the value <c>''</c> and
        /// explicitly not null - and <b>surplus arguments are discarded</b>.
        /// </para>
        /// <para>
        /// The arithmetic is the oracle's, in the oracle's order: read the declared count (<c>:L607</c>);
        /// if subclassed, call the hook and skip this subscriber on a prevented result (<c>:L609</c>);
        /// clamp a negative consumed count to zero rather than rejecting it (<c>:L610</c>); then pass
        /// <c>Min(declared - consumed, payload length)</c> arguments starting at slot
        /// <c>consumed + 1</c> (<c>:L613-L615</c>).
        /// </para>
        /// </remarks>
        private bool PassArguments(
            string name,
            object target,
            MethodInfo handler,
            object?[] arguments,
            out object?[] finalArguments)
        {
            ParameterInfo[] parameters = handler.GetParameters();

            // :L607 - nArgCnt = invoker.GetArgCount()
            int declaredCount = parameters.Length;

            // Pre-filled with each parameter's PowerScript initial value, so a slot the payload does not
            // reach carries '' for a string and 0 for a number rather than null. This is where
            // w_test_eventful.srw:L246 is discharged.
            finalArguments = declaredCount == 0 ? [] : new object?[declaredCount];
            for (int slot = 0; slot < declaredCount; slot++)
            {
                finalArguments[slot] = InitialValueFor(parameters[slot].ParameterType);
            }

            int consumed = 0;

            // :L608 - if _bSubclassing then
            if (_subclassing)
            {
                EventArgumentContext context = new(finalArguments);

                // :L609 - if IsPrevented(Event OnPrepare(...)) then return false
                if (Predicates.IsPrevented(OnPrepare(name, target, context)))
                {
                    return false;
                }

                consumed = context.ConsumedArgumentCount;

                // :L610 - if nArgIdx <= 0 then nArgIdx = 0. A CLAMP, not a validation: a negative
                // consumed count is silently treated as zero rather than reported, and that is preserved.
                if (consumed <= 0)
                {
                    consumed = 0;
                }
            }

            // :L613 - nParmCnt = Min(nArgCnt - nArgIdx, UpperBound(params)). This single expression is
            // both halves of the contract: the Min's first operand truncates the payload to the room the
            // handler has left, and its second stops at the payload's own end.
            int passCount = Math.Min(declaredCount - consumed, arguments.Length);

            // A consumed count larger than the declared count leaves no room at all. PowerScript's
            // SetArgs is a no-op for a non-positive count, so clamping here is that behaviour.
            if (passCount < 0)
            {
                passCount = 0;
            }

            // :L614-L615 - nArgIdx++ then SetArgs(nArgIdx, params, 1, nParmCnt): the payload is written
            // from the slot after the last one the hook consumed. The one-based-to-zero-based conversion
            // is the absence of the ++ , since `consumed` is already the count of filled slots and
            // therefore the zero-based index of the first free one.
            for (int offset = 0; offset < passCount; offset++)
            {
                int slot = consumed + offset;
                if (slot >= declaredCount)
                {
                    break;
                }

                finalArguments[slot] = Coerce(arguments[offset], parameters[slot].ParameterType);
            }

            // :L617
            return true;
        }

        /// <summary>
        /// Invokes a resolved handler: the port of <c>invoker.Invoke()</c>
        /// (<c>n_cst_eventful.sru:L866</c>).
        /// </summary>
        /// <param name="handler">The handler to invoke.</param>
        /// <param name="target">The object to invoke it on.</param>
        /// <param name="arguments">The argument array, already sized and filled.</param>
        /// <returns>
        /// The handler's return value, or <see langword="null"/> for a <see langword="void"/> handler -
        /// which is exactly how the oracle's <c>any</c> reports a handler with no return, and therefore how
        /// "no value was returned, so the event was not handled" arrives at the handled detection.
        /// </returns>
        /// <remarks>
        /// <b><see cref="BindingFlags.DoNotWrapExceptions"/> is load-bearing.</b> Without it reflection
        /// wraps whatever the handler threw in a <see cref="TargetInvocationException"/>, and the dispatch's
        /// exception capture would then decorate the wrapper: <c>ClassName(ex)</c> would read
        /// <c>"TargetInvocationException"</c> instead of the real type, the assertion special case would
        /// never fire, and an <see cref="OnException"/> override would be handed the wrong exception.
        /// PowerBuilder surfaces the handler's own exception directly, so this flag is what makes the port
        /// faithful - and it does it without an unwrap-and-rethrow step, which would have reset the stack
        /// trace or needed <c>ExceptionDispatchInfo</c> to avoid doing so.
        /// </remarks>
        private static object? InvokeHandler(MethodInfo handler, object target, object?[] arguments)
        {
            return handler.Invoke(
                target,
                BindingFlags.DoNotWrapExceptions,
                binder: null,
                parameters: arguments,
                culture: null);
        }

        /// <summary>
        /// Re-checks that a stored handler is still applicable to its target: the port of the dispatch-time
        /// re-bind <c>invoker.Init(Events[nIndex].object, Events[nIndex].mid, ScriptEvent!)</c>
        /// (<c>n_cst_eventful.sru:L859</c>).
        /// </summary>
        /// <param name="entry">The subscription about to be invoked.</param>
        /// <returns>
        /// The handler when it is still applicable; otherwise <see langword="null"/>, which makes the
        /// caller raise <c>"Invalid method"</c> exactly as <c>:L860</c> does.
        /// </returns>
        /// <remarks>
        /// The oracle re-binds before every invocation rather than trusting the stored method id, and the
        /// re-bind cannot fail for a target whose type was fixed at subscribe time - which is equally true
        /// here, so the raise at <c>:L860</c> is a genuine guard on an unreachable state rather than a live
        /// error path. It is reproduced because omitting it would remove the framework's only "invalid
        /// method" report and leave a reader wondering where the stored identity is validated.
        /// </remarks>
        private static MethodInfo? ResolveDispatchHandler(EventSubscription entry)
        {
            MethodInfo? handler = entry.Handler;
            if (handler is null)
            {
                return null;
            }

            Type? declaringType = handler.DeclaringType;
            if (declaringType is not null && !declaringType.IsInstanceOfType(entry.Target))
            {
                return null;
            }

            return handler;
        }

        /// <summary>
        /// Resolves a handler by name and optional signature on a target: the substitute for
        /// <c>invoker.Init(object, evtName, evtSign, ScriptEvent!)</c>
        /// (<c>n_cst_eventful.sru:L399</c>).
        /// </summary>
        /// <param name="target">The object that owns the handler.</param>
        /// <param name="handlerName">The handler name, already folded to lower case. Matched case-insensitively.</param>
        /// <param name="handlerSignature">The optional signature filter; empty accepts any signature.</param>
        /// <param name="handler">The resolved handler, or <see langword="null"/>.</param>
        /// <returns>
        /// <see cref="RetCode.OK"/> on success, or <see cref="RetCode.E_EVENT_NOT_FOUND"/> when nothing
        /// matches. The caller tests the result with <c>Predicates.IsFailed</c>, exactly as <c>:L400</c>
        /// tests the returned method id.
        /// </returns>
        /// <remarks>
        /// <para>
        /// <b>DECISION 15 (AAP 0.7.3 C-K).</b> The oracle resolves through <c>n_scriptinvoker</c>, which is
        /// dropped under C-D, so resolution is done with reflection instead - <b>the return code and its
        /// timing are the contract, and the mechanism is an implementation detail</b>. Resolution happens
        /// once, at subscribe time, and its <see cref="MethodInfo"/> is stored as the method-id analogue.
        /// </para>
        /// <para>
        /// The candidate walk goes up the type hierarchy one declaring type at a time rather than using
        /// <see cref="BindingFlags.FlattenHierarchy"/>, because that flag does not reach inherited
        /// non-public members and a PowerBuilder event is closer to a protected member than to a public
        /// one. Most-derived types are visited first, and a method whose parameter list a nearer type has
        /// already contributed is skipped, so an override is collected once and at its most-derived
        /// declaration. Property accessors and other compiler-generated members are excluded by
        /// <see cref="MethodBase.IsSpecialName"/>, and open generic methods are excluded because there is
        /// no type argument to supply.
        /// </para>
        /// <para>
        /// With no signature the resolution must still be deterministic, so among candidates the one with
        /// the FEWEST parameters wins and, within an equal count, the most-derived - the stable sort over
        /// a most-derived-first list gives that for free. Fewest-parameters is the right tie-break because
        /// of the argument contract: surplus payload arguments are discarded, so the shortest overload is
        /// callable from every trigger that any of them is callable from.
        /// </para>
        /// <para>
        /// With a signature, the parts are matched against each parameter's CLR type name AND against the
        /// PowerScript spellings of AAP 0.4.5.2, so a signature copied from ported PowerScript
        /// (<c>"string,long"</c>) and one written in CLR terms (<c>"String,Int64"</c>) select the same
        /// handler. An empty part is a wildcard for that position, and a <c>ref</c> or <c>out</c> prefix is
        /// accepted and ignored.
        /// </para>
        /// </remarks>
        private static long ResolveHandler(
            object target,
            string handlerName,
            string handlerSignature,
            out MethodInfo? handler)
        {
            handler = null;

            const BindingFlags Flags =
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly;

            List<MethodInfo> candidates = [];

            for (Type? type = target.GetType(); type is not null; type = type.BaseType)
            {
                foreach (MethodInfo method in type.GetMethods(Flags))
                {
                    if (method.IsSpecialName || method.IsAbstract || method.ContainsGenericParameters)
                    {
                        continue;
                    }

                    if (!string.Equals(method.Name, handlerName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    bool alreadySeen = false;
                    foreach (MethodInfo collected in candidates)
                    {
                        if (HaveSameParameterTypes(collected, method))
                        {
                            // A nearer declaration of the same signature has already been collected, so
                            // this is the base of an override chain.
                            alreadySeen = true;
                            break;
                        }
                    }

                    if (!alreadySeen)
                    {
                        candidates.Add(method);
                    }
                }
            }

            if (candidates.Count == 0)
            {
                // :L400-L402 - IsFailed(mid) then return RetCode.E_EVENT_NOT_FOUND
                return RetCode.E_EVENT_NOT_FOUND;
            }

            if (handlerSignature.Length > 0)
            {
                foreach (MethodInfo candidate in candidates)
                {
                    if (MatchesSignature(candidate, handlerSignature))
                    {
                        handler = candidate;
                        return RetCode.OK;
                    }
                }

                return RetCode.E_EVENT_NOT_FOUND;
            }

            MethodInfo chosen = candidates[0];
            foreach (MethodInfo candidate in candidates)
            {
                // Strictly fewer only, so the first - and therefore most-derived - candidate wins a tie.
                if (candidate.GetParameters().Length < chosen.GetParameters().Length)
                {
                    chosen = candidate;
                }
            }

            handler = chosen;
            return RetCode.OK;
        }

        /// <summary>
        /// Whether two methods declare the same parameter types in the same order, and are therefore two
        /// declarations of one overridable member.
        /// </summary>
        /// <param name="left">The method already collected, from a nearer type.</param>
        /// <param name="right">The candidate from a base type.</param>
        /// <returns><see langword="true"/> when the parameter lists are identical.</returns>
        private static bool HaveSameParameterTypes(MethodInfo left, MethodInfo right)
        {
            ParameterInfo[] leftParameters = left.GetParameters();
            ParameterInfo[] rightParameters = right.GetParameters();

            if (leftParameters.Length != rightParameters.Length)
            {
                return false;
            }

            for (int index = 0; index < leftParameters.Length; index++)
            {
                if (leftParameters[index].ParameterType != rightParameters[index].ParameterType)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Whether a candidate handler's parameter list satisfies a signature filter.
        /// </summary>
        /// <param name="candidate">The candidate handler.</param>
        /// <param name="signature">
        /// A comma-separated list of parameter type names. An empty part is a wildcard for that position;
        /// a <c>ref</c> or <c>out</c> prefix is accepted and ignored.
        /// </param>
        /// <returns><see langword="true"/> when the arity and every named position match.</returns>
        /// <remarks>
        /// Both CLR names and the PowerScript spellings of AAP 0.4.5.2 are accepted, matched
        /// case-insensitively against the parameter type's simple name and its full name. A by-reference
        /// parameter's type name carries a trailing <c>'&amp;'</c> in reflection, which is stripped before
        /// comparison so <c>"ref string"</c>, <c>"string"</c> and <c>"String"</c> all match a
        /// <c>ref string</c> parameter - the shape <c>se_cst_dw.sru:L13</c>'s
        /// <c>onddsgetfilter(..., ref string filter)</c> event has.
        /// </remarks>
        private static bool MatchesSignature(MethodInfo candidate, string signature)
        {
            string[] parts = signature.Split(',');
            ParameterInfo[] parameters = candidate.GetParameters();

            if (parts.Length != parameters.Length)
            {
                return false;
            }

            for (int index = 0; index < parts.Length; index++)
            {
                string part = NormalizeSignaturePart(parts[index]);

                if (part.Length == 0)
                {
                    // An empty part is a wildcard: the position exists but its type is unconstrained.
                    continue;
                }

                Type parameterType = parameters[index].ParameterType;
                if (parameterType.IsByRef)
                {
                    parameterType = parameterType.GetElementType() ?? parameterType;
                }

                if (string.Equals(part, parameterType.Name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (parameterType.FullName is string fullName
                    && string.Equals(part, fullName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (PowerScriptTypeAliases.TryGetValue(part, out string? clrName)
                    && string.Equals(clrName, parameterType.Name, StringComparison.Ordinal))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        /// <summary>
        /// Trims a signature part and removes a leading <c>ref</c>, <c>out</c> or <c>readonly</c> modifier.
        /// </summary>
        /// <param name="part">One comma-separated piece of a signature.</param>
        /// <returns>The bare type name.</returns>
        /// <remarks>
        /// <c>readonly</c> is accepted because it is the modifier the oracle's own declarations carry -
        /// almost every legacy parameter is <c>readonly</c>, which AAP 0.4.5.2 maps onto C#'s <c>in</c> -
        /// so a signature transcribed straight from a <c>.sru</c> forward declaration resolves without
        /// editing.
        /// </remarks>
        private static string NormalizeSignaturePart(string part)
        {
            string trimmed = part.Trim();

            foreach (string modifier in SignatureModifiers)
            {
                if (trimmed.StartsWith(modifier, StringComparison.OrdinalIgnoreCase))
                {
                    trimmed = trimmed[modifier.Length..].Trim();
                    break;
                }
            }

            return trimmed;
        }

        /// <summary>
        /// The parameter modifiers a signature part may carry, each with its trailing space so a type whose
        /// name merely begins with one of these words is not truncated.
        /// </summary>
        private static readonly string[] SignatureModifiers = ["ref ", "out ", "readonly ", "in "];

        /// <summary>
        /// The PowerScript-to-CLR type-name map of AAP 0.4.5.2, so a handler signature transcribed from
        /// legacy PowerScript resolves without translation.
        /// </summary>
        /// <remarks>
        /// Ordinal-ignore-case keyed, because PowerScript type names are case-insensitive. The values are
        /// simple CLR type names, matched against <see cref="MemberInfo.Name"/>. This is the same mapping
        /// table the AAP publishes, including <c>any</c> to <see cref="object"/>, <c>blob</c> to
        /// <c>Byte[]</c>, <c>date</c> to <see cref="DateOnly"/>, <c>time</c> to <see cref="TimeOnly"/> and
        /// <c>powerobject</c> to <see cref="object"/>.
        /// </remarks>
        private static readonly Dictionary<string, string> PowerScriptTypeAliases =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["string"] = "String",
                ["char"] = "Char",
                ["boolean"] = "Boolean",
                ["byte"] = "Byte",
                ["int"] = "Int32",
                ["integer"] = "Int32",
                ["uint"] = "UInt32",
                ["unsignedinteger"] = "UInt32",
                ["long"] = "Int64",
                ["ulong"] = "UInt64",
                ["unsignedlong"] = "UInt64",
                ["longlong"] = "Int64",
                ["real"] = "Single",
                ["double"] = "Double",
                ["dec"] = "Decimal",
                ["decimal"] = "Decimal",
                ["datetime"] = "DateTime",
                ["date"] = "DateOnly",
                ["time"] = "TimeOnly",
                ["any"] = "Object",
                ["powerobject"] = "Object",
                ["blob"] = "Byte[]"
            };

        /// <summary>
        /// The PowerScript initial value of a type, used to fill argument slots the payload does not reach.
        /// </summary>
        /// <param name="parameterType">The parameter's declared type.</param>
        /// <returns>
        /// <see cref="string.Empty"/> for a string, the zero value for a value type, and
        /// <see langword="null"/> for any other reference type.
        /// </returns>
        /// <remarks>
        /// This is where <c>w_test_eventful.srw:L246</c> is discharged: its worked example states that a
        /// handler declared <c>ontest(string arg1, string arg2)</c> and triggered with one argument receives
        /// <c>''</c> in <c>arg2</c> - "the type's initial value" - and explicitly not null. Every
        /// PowerScript type has such a value: <c>0</c> for a number, <c>false</c> for a boolean, the empty
        /// string for a string, and null only for an object reference.
        /// </remarks>
        private static object? InitialValueFor(Type parameterType)
        {
            Type type = parameterType;

            if (type.IsByRef)
            {
                type = type.GetElementType() ?? type;
            }

            if (type == typeof(string))
            {
                return string.Empty;
            }

            if (type.IsValueType && !type.IsPointer && !type.ContainsGenericParameters)
            {
                // Nullable<T> is a value type whose zero value is null, which Activator returns, and which
                // is the correct initial value for it.
                return Activator.CreateInstance(type);
            }

            return null;
        }

        /// <summary>
        /// Widens or narrows a payload value to the parameter type it is being passed to, reproducing the
        /// implicit numeric conversions PowerScript performs when it moves an <c>any</c> into a typed
        /// argument slot.
        /// </summary>
        /// <param name="value">The payload value.</param>
        /// <param name="parameterType">The parameter's declared type.</param>
        /// <returns>The value, converted when a conversion is both possible and appropriate.</returns>
        /// <remarks>
        /// <para>
        /// The contract at <c>w_test_eventful.srw:L240</c> is that the argument order must match and the
        /// types must be <b>compatible</b> - not identical. A trigger that passes an <see cref="int"/> to a
        /// handler declaring <c>long</c> works in PowerBuilder, and would fail in .NET without this step,
        /// because reflection requires an exactly assignable boxed value.
        /// </para>
        /// <para>
        /// The conversion is deliberately narrow. Numeric, boolean and character values convert to a
        /// primitive or <see cref="decimal"/> parameter, and an integral value converts to an enum
        /// parameter. <b>Conversion TO <see cref="string"/> is deliberately excluded</b>: PowerScript does
        /// not implicitly stringify a number into a string argument, so allowing it would make the port
        /// accept a call the legacy rejects. Anything else is passed through untouched, and if reflection
        /// then rejects it the resulting exception travels the dispatch's own capture path - which is
        /// exactly where a legacy type mismatch surfaced too.
        /// </para>
        /// </remarks>
        private static object? Coerce(object? value, Type parameterType)
        {
            if (value is null)
            {
                // An explicitly passed null stays null. It is NOT replaced with the initial value: that
                // substitution belongs to slots the payload never reached.
                return null;
            }

            Type target = parameterType;

            if (target.IsByRef)
            {
                target = target.GetElementType() ?? target;
            }

            if (Nullable.GetUnderlyingType(target) is Type underlying)
            {
                // Reflection accepts a boxed T for a Nullable<T> parameter, so the conversion target is T.
                target = underlying;
            }

            if (target == typeof(object) || target.IsInstanceOfType(value))
            {
                return value;
            }

            if (target.IsEnum && IsNumeric(value))
            {
                try
                {
                    return Enum.ToObject(target, value);
                }
                catch (ArgumentException)
                {
                    return value;
                }
                catch (OverflowException)
                {
                    return value;
                }
            }

            bool convertibleSource = IsNumeric(value) || value is bool || value is char;
            bool convertibleTarget = (target.IsPrimitive || target == typeof(decimal)) && target != typeof(nint) && target != typeof(nuint);

            if (convertibleSource && convertibleTarget)
            {
                try
                {
                    return Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
                }
                catch (InvalidCastException)
                {
                    return value;
                }
                catch (OverflowException)
                {
                    return value;
                }
                catch (FormatException)
                {
                    return value;
                }
            }

            return value;
        }

        /// <summary>
        /// Builds a target's class chain, <c>outer/.../inner</c>: the port of
        /// <c>_of_getobjectclasschain</c> (<c>n_cst_eventful.sru:L976-L991</c>).
        /// </summary>
        /// <param name="target">The callback target.</param>
        /// <returns>
        /// The chain, or <see cref="string.Empty"/> when <paramref name="target"/> is unusable
        /// (<c>:L980</c>).
        /// </returns>
        /// <remarks>
        /// <para>
        /// <b>DECISION 11 (AAP 0.7.3 C-K).</b> The oracle walks <c>GetParent()</c> upward, prepending each
        /// ancestor's class name and a <c>'/'</c>. In PowerBuilder <c>GetParent()</c> is
        /// <b>containment</b>, not inheritance: it answers "which object is this one declared inside", and
        /// the broker's own instance in the DataWindow service is declared exactly that way -
        /// <c>type eventful from n_cst_eventful WITHIN se_cst_dw</c>
        /// (<c>se_cst_dw.sru:L6-L7</c>), for which the chain is <c>se_cst_dw/eventful</c>.
        /// </para>
        /// <para>
        /// C#'s containment analogue is the nested-type relationship, so the walk goes up
        /// <see cref="Type.DeclaringType"/> and produces the identical shape - a nested handler class
        /// declared inside its owner yields <c>Owner/Handler</c>, and a top-level class yields its own name.
        /// Walking the BASE-type chain instead was rejected: it would express inheritance rather than
        /// containment and would emit <c>Object/</c> on the front of everything.
        /// </para>
        /// <para>
        /// <b>Its only consumer is the exception text</b> (<c>:L884</c>). It takes no part in dispatch,
        /// ordering or matching, which is worth stating because a chain-shaped string in an event broker
        /// looks like routing.
        /// </para>
        /// </remarks>
        private static string BuildClassChain(object? target)
        {
            // :L980 - if Not IsValidObject(object) then return ""
            if (!Predicates.IsValidObject(target))
            {
                return string.Empty;
            }

            Type type = target!.GetType();

            // :L982 - sChain = ClassName(object)
            StringBuilder chain = new(type.Name);

            // :L983-L988 - walk the containment chain, prepending each ancestor.
            for (Type? declaring = type.DeclaringType; declaring is not null; declaring = declaring.DeclaringType)
            {
                chain.Insert(0, '/').Insert(0, declaring.Name);
            }

            // :L990
            return chain.ToString();
        }

        /// <summary>
        /// Reads the two assertion detail members off a captured exception, when it is an assertion
        /// failure: the port of the oracle's dynamic downcast at
        /// <c>n_cst_eventful.sru:L873-L877</c>.
        /// </summary>
        /// <param name="exception">The captured exception.</param>
        /// <param name="detail">
        /// The composed detail, <c>Info</c> followed by <c>"\nStackTrace:\n"</c> and
        /// <c>StackTraceInfo</c>, byte for byte as <c>:L876</c> composes it.
        /// </param>
        /// <returns><see langword="true"/> when the exception carried assertion detail.</returns>
        /// <remarks>
        /// <para>
        /// <b>The late-bound half of DECISION 4.</b> Two routes are tried, in order:
        /// </para>
        /// <list type="number">
        ///   <item><description>
        ///   <b>The static route</b> - the exception implements <see cref="IAssertionDetail"/>. Any
        ///   assertion type can, and a test double does so trivially.
        ///   </description></item>
        ///   <item><description>
        ///   <b>The late-bound route</b>, which is what the oracle itself does: its test is
        ///   <c>ClassName(ex) = "assertionfailed"</c>, a STRING class-name comparison followed by an
        ///   untyped assignment. The same shape is reproduced here - match the runtime type's name against
        ///   the legacy spelling or the ported one, then read the two members by name. This is what makes
        ///   the arm reachable for
        ///   <c>PowerFramework.Shared.Diagnostics.AssertionFailure</c>, which exposes both members
        ///   publicly, <b>without this project taking a second ProjectReference</b>. Kernel stays the sole
        ///   dependency, exactly as the measured coupling requires.
        ///   </description></item>
        /// </list>
        /// <para>
        /// Both members must be present and readable, or the caller falls back to the
        /// <c>"[TypeName]"</c> form at <c>:L878</c> - so a type that merely shares the name but not the
        /// shape degrades to the general case rather than throwing from inside an exception handler.
        /// </para>
        /// </remarks>
        private static bool TryReadAssertionDetail(Exception exception, [NotNullWhen(true)] out string? detail)
        {
            // Route 1 - the static contract this file declares.
            if (exception is IAssertionDetail carrier)
            {
                detail = string.Concat(carrier.Info, "\nStackTrace:\n", carrier.StackTraceInfo);
                return true;
            }

            detail = null;

            // Route 2 - :L873's string class-name test, in both the legacy and the ported spelling.
            Type type = exception.GetType();
            bool nameMatches =
                string.Equals(type.Name, LegacyAssertionTypeName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(type.Name, PortedAssertionTypeName, StringComparison.Ordinal);

            if (!nameMatches)
            {
                return false;
            }

            string? info = ReadStringMember(exception, type, AssertionInfoMemberName);
            string? stackTraceInfo = ReadStringMember(exception, type, AssertionStackTraceMemberName);

            if (info is null || stackTraceInfo is null)
            {
                return false;
            }

            // :L876 - f.#Info + "~nStackTrace:~n" + f.#StackTraceInfo
            detail = string.Concat(info, "\nStackTrace:\n", stackTraceInfo);
            return true;
        }

        /// <summary>
        /// Reads a public instance string property by name, or returns <see langword="null"/> when it is
        /// absent or unreadable.
        /// </summary>
        /// <param name="instance">The object to read from.</param>
        /// <param name="type">Its runtime type.</param>
        /// <param name="memberName">The property name.</param>
        /// <returns>The value, or <see langword="null"/>.</returns>
        /// <remarks>
        /// Deliberately total: it is called from inside an exception handler, where throwing would replace
        /// the exception being reported with one from the reporting machinery. A property that throws from
        /// its getter therefore degrades to the general <c>"[TypeName]"</c> detail form rather than
        /// escaping.
        /// </remarks>
        private static string? ReadStringMember(object instance, Type type, string memberName)
        {
            PropertyInfo? property = type.GetProperty(
                memberName,
                BindingFlags.Instance | BindingFlags.Public);

            if (property is null || property.PropertyType != typeof(string) || !property.CanRead)
            {
                return null;
            }

            try
            {
                return property.GetValue(instance) as string;
            }
            catch (MethodAccessException)
            {
                return null;
            }
            catch (TargetInvocationException)
            {
                return null;
            }
        }

        /// <summary>
        /// Indents the continuation lines of a block of text under a label: the port of
        /// <c>_of_alignstring</c> (<c>n_cst_eventful.sru:L1252-L1274</c>).
        /// </summary>
        /// <param name="align">
        /// The label the text sits under. Its length fixes the indent - half of it, rounded - and a
        /// trailing newline on it makes the FIRST line indented too.
        /// </param>
        /// <param name="text">The text to indent.</param>
        /// <returns>The indented text.</returns>
        /// <remarks>
        /// <para>
        /// <b>This is a message-formatting concern and NOT the ordering mechanism.</b> Its name in the
        /// oracle is <c>_of_alignstring</c>, which sits alphabetically among the dispatch routines and
        /// reads like one; it aligns TEXT, and nothing in it touches the subscription table, the priorities
        /// or the dispatch order. Mistaking one for the other would send a reader looking for an ordering
        /// bug in a string builder.
        /// </para>
        /// <para>
        /// The indent width is <c>Len(align) / 2</c> (<c>:L1255</c>). PowerScript's division is real and
        /// <c>Fill</c> rounds its count, so the half-away-from-zero rounding is reproduced rather than
        /// truncated - it makes no difference at the single call site, where the label is twelve characters
        /// and the indent is exactly six, and it makes the helper correct for any label. Each iteration
        /// keeps the newline it split on (<c>:L1266</c> uses <c>Left(str, nPos)</c>, inclusive) and the
        /// indent is appended only when text remains (<c>:L1268</c>), so no trailing indent is emitted.
        /// </para>
        /// </remarks>
        private static string AlignContinuationLines(string align, string text)
        {
            // :L1255 - sSpaces = Fill(" ", Len(align) / 2)
            int indentWidth = (int)Math.Round(align.Length / 2.0, MidpointRounding.AwayFromZero);
            string indent = indentWidth > 0 ? new string(' ', indentWidth) : string.Empty;

            StringBuilder result = new();

            // :L1256-L1258 - a label that ends in a newline puts the first line on its own line, so that
            // line needs the indent too.
            if (align.EndsWith('\n'))
            {
                result.Append(indent);
            }

            // :L1260-L1271 - the do/loop while(true).
            string remaining = text;
            while (true)
            {
                // :L1261 - nPos = Pos(str,"~n"). The char overload is ordinal by definition.
                int position = remaining.IndexOf('\n');

                if (position < 0)
                {
                    // :L1262-L1265 - no newline left, so the remainder is the last line.
                    result.Append(remaining);
                    break;
                }

                // :L1266 - sRet += Left(str,nPos), which INCLUDES the newline.
                result.Append(remaining, 0, position + 1);

                // :L1267
                remaining = remaining[(position + 1)..];

                // :L1268-L1270 - indent the next line, but only if there is one.
                if (remaining.Length > 0)
                {
                    result.Append(indent);
                }
            }

            // :L1273
            return result.ToString();
        }

        /// <summary>
        /// Records the four-line diagnostic block on a captured exception: the substitute for the oracle's
        /// <c>ex.text = ...</c> assignment (<c>n_cst_eventful.sru:L883-L886</c> and
        /// <c>:L900</c>).
        /// </summary>
        /// <param name="exception">The captured exception.</param>
        /// <param name="text">The block to record.</param>
        /// <remarks>
        /// The write half of DECISION 7; see <see cref="DispatchExceptionTextKey"/> for the two rejected
        /// alternatives and why <see cref="System.Exception.Data"/> is the right carrier. Writing is total:
        /// a custom exception whose <see cref="System.Exception.Data"/> is a fixed or read-only dictionary
        /// would otherwise throw from inside an exception handler and replace the exception being reported
        /// with one from the reporting machinery. The block is then simply unavailable through
        /// <see cref="GetDispatchExceptionText"/>, and the original exception still propagates with its
        /// type, message and stack intact - a strictly better failure than losing it.
        /// </remarks>
        private static void SetDispatchExceptionText(Exception exception, string text)
        {
            try
            {
                exception.Data[DispatchExceptionTextKey] = text;
            }
            catch (NotSupportedException)
            {
                // A fixed-size or read-only Data dictionary. Nothing to record, nothing to report.
            }
        }

        /// <summary>
        /// PowerScript's <c>&lt;&gt;</c> on two <c>any</c> values, three-valued and numerically widening:
        /// the comparison the handled detection performs at <c>n_cst_eventful.sru:L916</c> and
        /// <c>:L927</c>.
        /// </summary>
        /// <param name="left">The subscriber's return value. Known non-null at both call sites.</param>
        /// <param name="right">The resolved default return value.</param>
        /// <returns>
        /// <see langword="true"/> when the two differ, <see langword="false"/> when they are equal, and
        /// <see langword="null"/> when either operand is null - because a PowerScript comparison involving
        /// NULL yields NULL, and <c>if NULL then</c> does not take its branch.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// The two values are of incompatible scalar kinds and PowerScript would raise a runtime error
        /// comparing them. Both call sites catch it, reproducing <c>:L919-L921</c> and
        /// <c>:L930-L932</c>.
        /// </exception>
        /// <remarks>
        /// <para>
        /// <b>DECISION 8 (AAP 0.7.3 C-K) - why <see cref="object.Equals(object?, object?)"/> is not
        /// enough, with the concrete case.</b> <c>ws_objects/pfw.thread.pbl.src/n_cst_threading_eventful.sru:L76</c>
        /// installs <c>0</c> as the global default, and its handlers return <c>0</c> to mean "not handled".
        /// PowerScript compares those two as equal whatever their integer widths. <c>Equals(0L, 0)</c> in
        /// C# is <see langword="false"/>, because a boxed <see cref="long"/> and a boxed <see cref="int"/>
        /// are different types - so a handler returning <c>0</c> would be scored as HAVING handled the
        /// event, the capture filter would then skip every remaining unhandled-only subscriber, and the
        /// whole threading layer's dispatch would quietly change shape. Numeric widening is therefore
        /// behaviour, not politeness.
        /// </para>
        /// <para>
        /// <b>And it must be able to RAISE.</b> The oracle wraps both comparisons in
        /// <see langword="try"/>/<see langword="catch"/> precisely because PowerScript's <c>&lt;&gt;</c>
        /// raises on an incomparable pair, and those catch arms are behaviour: they score the pair as
        /// different, and therefore as handled. Swallowing the mismatch here and returning
        /// <see langword="true"/> would produce the same answer today but would make the catch arms dead
        /// code, so the raise is kept where the oracle has it.
        /// </para>
        /// <para>
        /// The rules, in order: either operand null yields null; reference identity yields equal; two
        /// numerics compare as <see cref="double"/> when either is floating point and as
        /// <see cref="decimal"/> otherwise, which covers every integral width including
        /// <see cref="ulong"/> without loss; two strings compare ordinally; two booleans and two characters
        /// compare directly; two values of the same runtime type defer to that type's own equality; two
        /// non-scalar references compare by identity, which is what PowerBuilder does for two
        /// <c>powerobject</c> values; and any remaining mixed-scalar pair raises.
        /// </para>
        /// </remarks>
        private static bool? IsDifferentFrom(object? left, object? right)
        {
            // PowerScript NULL propagation. At :L916 neither operand can be null - the two preceding tests
            // have already excluded that - but at :L927 the default may well be, and returning null there
            // is what stops a later subscriber overwriting the return value when no default was set.
            if (left is null || right is null)
            {
                return null;
            }

            if (ReferenceEquals(left, right))
            {
                return false;
            }

            if (IsNumeric(left) && IsNumeric(right))
            {
                if (left is float or double || right is float or double)
                {
                    double leftDouble = Convert.ToDouble(left, CultureInfo.InvariantCulture);
                    double rightDouble = Convert.ToDouble(right, CultureInfo.InvariantCulture);
                    return !leftDouble.Equals(rightDouble);
                }

                decimal leftDecimal = Convert.ToDecimal(left, CultureInfo.InvariantCulture);
                decimal rightDecimal = Convert.ToDecimal(right, CultureInfo.InvariantCulture);
                return leftDecimal != rightDecimal;
            }

            if (left is string leftString && right is string rightString)
            {
                return !string.Equals(leftString, rightString, StringComparison.Ordinal);
            }

            if (left is bool leftBool && right is bool rightBool)
            {
                return leftBool != rightBool;
            }

            if (left is char leftChar && right is char rightChar)
            {
                return leftChar != rightChar;
            }

            if (left.GetType() == right.GetType())
            {
                return !left.Equals(right);
            }

            if (!IsScalar(left) && !IsScalar(right))
            {
                // Two unrelated object references. PowerBuilder compares powerobject values by identity
                // and does not raise, so neither does this.
                return true;
            }

            // A mixed scalar pair - a string against a number, an enum against an integer. PowerScript
            // raises here, and the callers' catch arms score the pair as different. See the remarks.
            throw new ArgumentException(
                "PowerScript cannot compare a value of type '"
                + left.GetType().Name
                + "' with a value of type '"
                + right.GetType().Name
                + "'.",
                nameof(left));
        }

        /// <summary>
        /// Whether a boxed value is one of the CLR numeric types PowerScript's numeric tower covers.
        /// </summary>
        /// <param name="value">The value to classify.</param>
        /// <returns><see langword="true"/> for an integral, floating-point or decimal value.</returns>
        /// <remarks>
        /// <see cref="char"/> and <see cref="bool"/> are deliberately excluded even though both are
        /// convertible to a number: PowerScript treats them as their own kinds, and folding them in here
        /// would make <c>'0'</c> compare equal to <c>48</c>.
        /// </remarks>
        private static bool IsNumeric(object value)
        {
            return value is sbyte or byte or short or ushort or int or uint or long or ulong
                or float or double or decimal;
        }

        /// <summary>
        /// Whether a boxed value is a scalar - a value PowerScript compares by VALUE rather than by
        /// reference.
        /// </summary>
        /// <param name="value">The value to classify.</param>
        /// <returns><see langword="true"/> for a scalar.</returns>
        /// <remarks>
        /// Used only by <see cref="IsDifferentFrom"/>, to tell a comparison PowerScript would raise on
        /// (a scalar against something of another kind) from one it performs by identity (two object
        /// references).
        /// </remarks>
        private static bool IsScalar(object value)
        {
            return IsNumeric(value)
                || value is string
                || value is bool
                || value is char
                || value is Enum
                || value is DateTime
                || value is DateOnly
                || value is TimeOnly
                || value is TimeSpan
                || value is Guid;
        }
    }
}
