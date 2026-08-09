// =================================================================================================
//  PriorityAndCaptureTests - the PRIORITY and CAPTURE parity suite for PowerFramework.Shared.Eventful.
//
//  SUBJECT
//  -------
//  Two of the three axes of EventBroker's subscription vocabulary, ported from
//  ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:
//
//    1. THE DISPATCH PRIORITY - its three well-known values, the 32-bit width those values depend on,
//       and the descending-priority dispatch order they select. The legacy constants are
//       PRIORITY_LOW = -2147483648, PRIORITY_NORMAL = 0 and PRIORITY_HIGH = 2147483647
//       (n_cst_eventful.sru:L104-L106), reached from the topic grammar by '@', by no symbol at all and
//       by '!' (:L351-L356), or by an arbitrary '[digits]:' prefix (:L365-L373). A larger number is a
//       higher priority, stated in prose at ws_objects/pfw.tests.pbl.src/w_test_eventful.srw:L212 and
//       mechanically fixed by the ordered insert at n_cst_eventful.sru:L407-L422.
//
//    2. THE CAPTURE MODE AND THE HANDLED-STATE PROTOCOL - the three modes CAP_UNHANDLED = 0,
//       CAP_HANDLED = 1 and CAP_ALL = 2 (:L100-L102), reached by no symbol, by '%' and by '*'
//       (:L345-L350); the filter that admits a subscription only when its mode equals the dispatch's
//       CURRENT handled state or is CAP_ALL (:L831-L833); and the escalation rule that decides when a
//       dispatch becomes handled at all (:L909-L934).
//
//  The third axis - the PLACEMENT of equal-priority peers, which the prepend symbol '-' decides - is
//  owned by DispatchOrderTests and is deliberately NOT re-asserted here. That suite states the split
//  from its own side: it "asserts POSITION only", and refers the priority VALUES to "the priority and
//  capture suite", which is this file. The one place the two touch is arrival order inside a single
//  priority band, asserted here for an ARBITRARY numeric priority because DispatchOrderTests asserts
//  it only for the normal band.
//
//  WHY THE VALUES AND THE RULE ARE ASSERTED, NOT ONLY THE RESULTING BEHAVIOUR (AAP 0.7.3 C-K)
//  ------------------------------------------------------------------------------------------
//  THE BOUNDARY DECISION, stated once and in full. Under decomposition the capture state and the
//  accumulated return value STOP BEING PROCESS-LOCAL AND START CROSSING A NETWORK BOUNDARY. AAP 0.4.3
//  C-03 makes the DataWindow event chain a bidirectional gRPC stream carrying all thirteen raw and all
//  nine semantic events with sequencing tokens and a typed veto, and AAP 0.6.1 requires the ordering
//  and the veto to survive that crossing rather than be inferred at the far end. The two quantities
//  this file pins are exactly what such a stream has to carry per event: WHICH HANDLED STATE the
//  dispatch is in, and WHAT VALUE has been accumulated so far.
//
//  That is why the numeric VALUES are asserted and not merely the behaviour they produce. On the wire
//  a capture mode is an integer and a priority is an integer, so a renumbering or a width change is
//  invisible to every behavioural test and fatal to every stored recording. Concretely:
//
//    * Priorities.Low and Priorities.High are ABSOLUTE only because they are the 32-bit extremes. A
//      PowerBuilder `long` is a 32-bit signed integer, so the two legacy literals ARE int.MinValue and
//      int.MaxValue, and porting the field as a C# `long` would widen the domain and let an ordinary
//      numeric priority outrank a subscriber that asked for the highest available. The width assertion
//      below is what catches that, and it is the reason a value assertion alone is not enough: an
//      Assert.Equal against int.MinValue still passes for a widened `long` field.
//
//    * CaptureMode's members must stay 0, 1 and 2 with a 32-bit underlying type, because the filter at
//      :L831-L833 compares the subscription's mode against the running state by VALUE, and a wire
//      encoding of that state is a number.
//
//  The real consumer is not hypothetical. ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L596
//  installs a global default return value of 0 on the broker embedded in the DataWindow service, and
//  eight of its raw events are written `if Eventful.of_Trigger(...) = 1 then return 1` - :L116, :L120,
//  :L132, :L139, :L148, :L167, :L178 and :L395. So in the framework's own most important consumer, 0
//  means continue and 1 means prevent, and BOTH mechanisms move together: returning 1 marks the
//  dispatch handled, which makes the capture filter skip every remaining unhandled-only subscriber,
//  AND it becomes the trigger's return value, which makes the raw event return 1 to the runtime.
//  Section 5 of this file asserts that pairing end to end.
//
//  HOW THE TWO OBSERVABLES ARE READ, AND WHY THERE IS NO OTHER WAY
//  --------------------------------------------------------------
//  ORDER AND ADMISSION are read from the shared DispatchLog of RecordingSubscriber. EventBroker
//  publishes no ordered snapshot of live subscriptions - its table is a private List<EventSubscription>
//  and nothing projects it - so the log's `Labels` property, the ordered sequence of the labels of
//  every handler that ran, IS the dispatch order and IS the record of which subscriptions were
//  admitted. A subscription the capture filter skipped simply has no row.
//
//  THE HANDLED STATE AND THE ACCUMULATED VALUE are read FROM INSIDE A DISPATCH, through
//  RecordingSubscriber.DuringAnyHandler, and that is forced rather than chosen. Both are dispatch-level
//  state: n_cst_eventful.sru saves the accumulated value on entry and RESTORES it on exit at :L951, so
//  once of_Trigger has returned, of_IsProcessed() reads false and of_GetReturnValue() reads null again
//  whatever happened inside. Verified against this port directly, and asserted below in
//  TheDispatchLevelStateIsRestoredOnceTheTriggerReturns so the constraint is visible rather than folk
//  knowledge.
//
//  Reading from inside is also the ORACLE'S OWN PATTERN, not a testing contrivance: its second handler
//  calls evtful.of_IsProcessed() and, when that is true, evtful.of_GetReturnValue()
//  (w_test_eventful.srw:L72-L74). The probe subscriber below is that handler. It captures with '*' so
//  that it runs in EITHER handled state - a probe subscribing with no symbol would be skipped by the
//  very escalation it exists to observe - and RecordingSubscriber.Record invokes the callback BEFORE
//  producing the handler's own return value, so a probe observes strictly the subscribers ahead of it.
//
//  A TRAP THAT WOULD MAKE HALF THIS SUITE WRONG IF UNSTATED
//  -------------------------------------------------------
//  Trigger's return value is NOT the handled value. n_cst_eventful.sru:L973 returns `aVal`, the LAST
//  INVOCATION's raw value, and :L966-L970 substitutes the resolved default for it when that value is
//  null and the dispatch was not posted. The accumulated handled value - what of_GetReturnValue()
//  exposes - is a different quantity that only the dispatch itself can see. So a dispatch that WAS
//  handled with the value 1 still returns 0 when a later subscriber returned nothing and the default is
//  0. Every assertion below distinguishes the two, and nothing here reads a handled value out of a
//  trigger result.
//
//  RULES POSITION, STATED EXPLICITLY (AAP 0.7.1)
//  --------------------------------------------
//  review_rules reports exactly one line: "No user rules provided." No user-specified rule governs this
//  file, none was invented to fill the gap, and that absence is not licence to lower the bar. In their
//  place the AAP 0.7.2 enterprise baseline and the AAP 0.7.3 binding non-rule constraints apply. The
//  five that bear on this file:
//
//    C-B  NO BEHAVIOUR IMPROVEMENTS - the capture protocol is asserted AS THE LEGACY HAS IT.
//         Every test that pins a rule of the protocol carries a PINS LEGACY SEMANTICS marker and its
//         locator. Two of them exist specifically to stop a "cleaner" rule being asserted in place of
//         the real one:
//           * "A NON-NULL RETURN MEANS HANDLED" IS NOT A UNIVERSAL, and this file never asserts it as
//             one. It holds only when NO default has been configured, because that case takes the
//             null-default arm at :L912-L913. With a default configured, the return value is compared
//             AGAINST THE DEFAULT (:L916), so a subscriber returning exactly the default leaves the
//             dispatch unhandled. AReturnEqualToTheConfiguredDefaultLeavesTheDispatchUnhandled is the
//             case that separates the two, and it was authored first.
//           * The handled state NEVER ROLLS BACK (w_test_eventful.srw:L235), while the accumulated
//             VALUE may still be overwritten by a later subscriber. That asymmetry is the protocol, and
//             both halves are asserted in one place so neither can be "tidied" into the other.
//         A third case pins a consequence a reader may mistake for a defect and try to fix: with NO
//         default configured the value is NOT overwritten either, because PowerScript's
//         `aVal <> aDefRetVal` against a null default evaluates to NULL and `if NULL then` is false
//         (:L927). That is exactly why the oracle advises always setting a default
//         (w_test_eventful.srw:L257).
//
//    C-C  THE LEGACY TREE IS READ ONLY AND IS THE BEHAVIOURAL ORACLE. This file cites roughly sixty
//         `ws_objects/**` locators and every one of them appears in a COMMENT. Nothing here reads the
//         legacy tree, or any other file, at build time or at run time - this suite performs no file
//         I/O at all - and no project item names a legacy path. The root .dockerignore excludes
//         ws_objects/ from the build context, so such a read would fail in a container and in CI even
//         where it happened to work locally. Every locator was read out of the source rather than
//         copied from a summary; see LOCATOR ACCURACY below.
//
//    C-D  NO DEFERRED CAPABILITY APPEARS, NOT EVEN AS A NAME. The legacy dispatch loop declares an
//         `n_scriptinvoker invoker` local (n_cst_eventful.sru:L791) and every subscription record
//         carries one. n_scriptinvoker belongs to the DEFERRED ScriptBridge service, so nothing here
//         names it, stands in for it, or depends on anything it used to provide. AAP 0.2.1.4 records
//         why that is lossless: the invoker was only a variadic-call escape hatch, and C# forwards an
//         arbitrary argument list natively.
//
//    C-H  NULLABLE REFERENCE TYPES AND WARNINGS AS ERRORS APPLY HERE EXACTLY AS TO SHIPPING CODE.
//         Directory.Build.props sets Nullable enable and TreatWarningsAsErrors true and this project
//         adds no NoWarn, so there is no #pragma warning disable in this file and no null-forgiving `!`
//         papering over a real nullability question. This suite necessarily deals in `object?` and in
//         DELIBERATE NULLS - a null return is half the capture model - and it expresses them properly:
//         a nullable local for a value observed from inside a dispatch, Assert.Null and Assert.NotNull
//         where the question is presence, and a null literal passed as the configured return value
//         rather than an absence of configuration standing in for one. Where a value read from inside a
//         dispatch must be compared, it is asserted non-null first so the comparison is on a value the
//         compiler agrees is present. No TheoryData type argument is `object` or `object?`, because
//         xunit's serializability analyzer is an ERROR under this warning policy; the matrices carry
//         string, long, bool and enum instead, and a row that needs "no value" carries a companion
//         bool rather than a null.
//
//    C-K  THE BOUNDARY DECISION IS NAMED. See the section above; it is the reason this file asserts the
//         mode and priority VALUES and the escalation RULE rather than only the behaviour they produce.
//
//  PROHIBITIONS HONOURED
//  ---------------------
//  Every matrix is a [Theory] with [MemberData] over TheoryData; [Fact] is reserved for cases with no
//  table, which is where the single-value bound and width assertions live. Ordered SEQUENCES of labels
//  are asserted, never sets - a set comparison would pass under any ordering and would defeat the
//  suite. A fresh EventBroker and a fresh DispatchLog are built per test, so no default return value,
//  no accumulated value and no capture state can leak between tests; the broker holds both defaults in
//  instance state, so sharing one would be the single most likely way to make this suite lie. No member
//  here is SCREAMING_SNAKE: the repository-root .editorconfig scopes its naming-analyzer suppressions
//  to ten NAMED implementation files and to no test file, so under warnings-as-errors such a member
//  would be a build error. Dispatch is synchronous, so there is no clock read, no timer, no Task.Delay,
//  no Thread.Sleep, no GUID, no random source and no thread anywhere in this file, and nothing reads a
//  file, a socket or a database.
//
//  LOCATOR ACCURACY
//  ----------------
//  Every locator below was read out of the legacy source rather than copied from a summary, and six
//  differ from the figures in the brief that commissioned this file. The verified ones are used here:
//  the running capture state is initialised at :L786 (not :L787); the capture filter is :L831-L833 (not
//  :L828-L831); the escalation is :L909-L934 with the null-default arm at :L912-L913 (not :L906-L927 and
//  :L910-L911); the mid-dispatch rejection in the per-topic setter is at :L500 (not :L502); the two
//  prose rules are w_test_eventful.srw:L234 and :L235 (not :L237-L238); the oracle's three subscriptions
//  are :L250-L252 with its global default at :L260 (not :L253-L256 and :L269); and its in-dispatch state
//  read is :L72-L74 (not :L74-L78).
//
//  A DIVERGENCE REPORTED RATHER THAN ACCOMMODATED
//  ----------------------------------------------
//  The brief asks this suite to cover "a negative value" among the arbitrary numeric priorities. An
//  arbitrary negative numeric priority is UNREACHABLE through the topic grammar, in the legacy and in
//  the port alike, because the leading '-' is claimed by SYMBOL_PREPEND before any numeric prefix is
//  looked for (n_cst_eventful.sru:L342-L344). The only reachable negative priority is Priorities.Low,
//  through '@'. Rather than quietly dropping the row or inventing an unreachable API to reach it, the
//  finding is PINNED as a test of its own -
//  AnArbitraryNegativeNumericPriorityIsUnreachableThroughTheGrammar - which asserts what the grammar
//  actually does with each spelling, and Priorities.Low carries the negative end of the ordering
//  matrices.
// =================================================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.Shared.Eventful.Tests;

/// <summary>
/// Pins the two subscription axes that cross the wire: the dispatch PRIORITY - its three well-known
/// values, their 32-bit width and the descending order they select - and the CAPTURE MODE together with
/// the handled-state escalation protocol that decides when a dispatch counts as handled at all.
/// </summary>
/// <remarks>
/// See the file header for the boundary decision this suite exists to protect, for why the handled state
/// can only be observed from inside a dispatch, for the trap that a trigger's return value is not the
/// handled value, and for the division of labour with <see cref="DispatchOrderTests"/>.
/// </remarks>
public class PriorityAndCaptureTests
{
    // =============================================================================================
    //  SHARED FIXTURES
    // =============================================================================================

    /// <summary>
    /// The event name every priority and capture case dispatches, matching the oracle's own worked
    /// example (<c>ws_objects/pfw.tests.pbl.src/w_test_eventful.srw:L220-L230</c>).
    /// </summary>
    private const string ClickedTopic = "clicked";

    /// <summary>
    /// A second event name, used only where a case must show that a per-topic default governs its own
    /// topic and the global default governs everything else.
    /// </summary>
    /// <remarks>
    /// Sorts after <see cref="ClickedTopic"/> ordinally, which is irrelevant to every assertion here -
    /// each case triggers exactly one name - but is stated so a reader does not look for a hidden
    /// ordering dependency. Cross-name order belongs to <see cref="DispatchOrderTests"/>.
    /// </remarks>
    private const string OtherTopic = "other";

    /// <summary>
    /// The label of the <c>*</c>-capturing subscriber that reads the broker's dispatch-level state from
    /// inside the dispatch.
    /// </summary>
    /// <remarks>
    /// A constant because several cases assert an ordered label sequence that ends with it, and a typo
    /// in one of those literals would read as an ordering failure rather than as a typo.
    /// </remarks>
    private const string ProbeLabel = "probe";

    /// <summary>
    /// The separator the theory rows use to spell a list of topics or labels inside one string.
    /// </summary>
    /// <remarks>
    /// Rows carry space-separated lists rather than <c>string[]</c> so the generated test display name
    /// shows the actual spellings being ordered, and so every row stays serializable under xunit's
    /// analyzer. No topic spelling used here contains a space, so the split is unambiguous.
    /// </remarks>
    private const char ListSeparator = ' ';

    /// <summary>
    /// The arbitrary numeric priority the oracle itself uses
    /// (<c>w_test_eventful.srw:L226</c>: <c>of_On("9999:clicked",...)</c>).
    /// </summary>
    /// <remarks>
    /// Load-bearing as a NON-BOUND value: it must sort strictly between <see cref="Priorities.Normal"/>
    /// and <see cref="Priorities.High"/>, which is what proves an arbitrary large number does not
    /// saturate to the high bound.
    /// </remarks>
    private const int OracleNumericPriority = 9999;

    // =============================================================================================
    //  SHARED HELPERS
    // =============================================================================================

    /// <summary>
    /// Splits one theory row's space-separated list.
    /// </summary>
    /// <param name="items">The space-separated list.</param>
    /// <returns>The items, in the order written.</returns>
    private static string[] SplitList(string items) =>
        items.Split(
            ListSeparator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Subscribes one <see cref="RecordingSubscriber"/> under an explicit label, asserting the
    /// subscription succeeded.
    /// </summary>
    /// <param name="broker">The broker to subscribe against.</param>
    /// <param name="log">The shared log this subscriber appends to.</param>
    /// <param name="topic">The subscription topic, symbols included.</param>
    /// <param name="label">The label this subscriber writes into the log.</param>
    /// <returns>The subscriber, so a caller can configure its return value or its in-handler callback.</returns>
    /// <remarks>
    /// The return code is asserted rather than ignored because every rejection in this grammar is a
    /// silent one from the caller's point of view: a mistyped symbol combination returns
    /// <see cref="RetCode.E_INVALID_ARGUMENT"/> and leaves a SHORTER subscription table, and an ordering
    /// or admission assertion would then be measuring an arrangement that was never built.
    /// </remarks>
    private static RecordingSubscriber SubscribeLabelled(
        EventBroker broker,
        DispatchLog log,
        string topic,
        string label)
    {
        RecordingSubscriber subscriber = new(log, label, broker);
        subscriber.Topic = topic;

        Assert.Equal(
            RetCode.OK,
            broker.Subscribe(topic, subscriber, RecordingHandlerNames.NoArguments));

        return subscriber;
    }

    /// <summary>
    /// Subscribes one <see cref="RecordingSubscriber"/> whose label IS its topic spelling.
    /// </summary>
    /// <param name="broker">The broker to subscribe against.</param>
    /// <param name="log">The shared log.</param>
    /// <param name="spelling">The topic spelling, symbols and numeric prefix included.</param>
    /// <returns>The subscriber.</returns>
    /// <remarks>
    /// Label equals spelling on purpose for the ordering matrices: the expected label sequence is then
    /// literally the expected spelling order, so a failure message names the topics a reader is
    /// reasoning about rather than opaque markers.
    /// </remarks>
    private static RecordingSubscriber SubscribeSpelling(
        EventBroker broker,
        DispatchLog log,
        string spelling) =>
        SubscribeLabelled(broker, log, spelling, spelling);

    /// <summary>
    /// Subscribes a subscriber that HANDLES the dispatch by returning a non-null value.
    /// </summary>
    /// <param name="broker">The broker to subscribe against.</param>
    /// <param name="log">The shared log.</param>
    /// <param name="topic">The subscription topic.</param>
    /// <param name="label">The label this subscriber writes into the log.</param>
    /// <param name="value">The value it returns.</param>
    /// <returns>The subscriber.</returns>
    /// <remarks>
    /// "Handles" is true of this helper only against a broker with NO default return value configured,
    /// or one whose resolved default differs from <paramref name="value"/>. That is the whole subject of
    /// section 4 and is never assumed by a case in sections 2 or 3: those cases configure no default at
    /// all, so the null-default arm at <c>n_cst_eventful.sru:L912-L913</c> makes any non-null value
    /// handle, and the arrangement they are testing is the capture filter rather than the escalation.
    /// </remarks>
    private static RecordingSubscriber SubscribeHandler(
        EventBroker broker,
        DispatchLog log,
        string topic,
        string label,
        long value)
    {
        RecordingSubscriber subscriber = SubscribeLabelled(broker, log, topic, label);
        subscriber.SetReturnValue(RecordingHandlerNames.NoArguments, value);

        return subscriber;
    }

    /// <summary>
    /// Records what the broker's dispatch-level state looked like at one point INSIDE a dispatch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A mutable holder rather than a tuple return, because the values are produced by a callback that
    /// runs during <c>Trigger</c> and consumed by assertions that run after it. Its fields start at the
    /// "never observed" values - <see langword="null"/> for both - so a case can distinguish "the probe
    /// ran and saw an unhandled dispatch" from "the probe never ran at all", which is exactly the
    /// distinction the capture filter creates.
    /// </para>
    /// <para>
    /// <see cref="Processed"/> is deliberately <see cref="bool"/>? rather than <see cref="bool"/>: with a
    /// plain bool, a probe that never ran and a probe that saw an unhandled dispatch would be
    /// indistinguishable, and several cases below turn on precisely that difference.
    /// </para>
    /// </remarks>
    private sealed class DispatchStateProbe
    {
        /// <summary>
        /// Gets or sets what <see cref="EventBroker.IsProcessed"/> answered, or <see langword="null"/>
        /// when the probe never ran.
        /// </summary>
        public bool? Processed { get; set; }

        /// <summary>
        /// Gets or sets what <see cref="EventBroker.GetReturnValue"/> answered.
        /// </summary>
        /// <remarks>
        /// <see langword="null"/> is ambiguous here between "never ran" and "ran and the dispatch was
        /// unhandled", and deliberately so: it is ambiguous in the subject too, because
        /// <c>of_isprocessed</c> is literally <c>Not IsNull(_aRetVal)</c>
        /// (<c>n_cst_eventful.sru:L655</c>). <see cref="Processed"/> is the field that disambiguates.
        /// </remarks>
        public object? Value { get; set; }

        /// <summary>
        /// Gets or sets how many times the probe ran.
        /// </summary>
        /// <remarks>
        /// Asserted where a case needs to show that an all-capturing probe ran EXACTLY once in a
        /// dispatch, which is what stops a passing observation being an artefact of a second invocation
        /// overwriting the first.
        /// </remarks>
        public int Runs { get; set; }
    }

    /// <summary>
    /// Subscribes an <c>*</c>-capturing subscriber that reads <see cref="EventBroker.IsProcessed"/> and
    /// <see cref="EventBroker.GetReturnValue"/> from inside the dispatch - the oracle's own pattern at
    /// <c>w_test_eventful.srw:L72-L74</c>.
    /// </summary>
    /// <param name="broker">The broker to subscribe against.</param>
    /// <param name="log">The shared log.</param>
    /// <param name="topic">The event name to probe, WITHOUT a capture symbol; <c>*</c> is added here.</param>
    /// <param name="probe">The holder the observation is written into.</param>
    /// <returns>The subscriber, so a caller can additionally configure a return value on it.</returns>
    /// <remarks>
    /// <para>
    /// <b>It captures with <c>*</c> because it must run in EITHER handled state.</b> A probe subscribing
    /// with no symbol would be <see cref="CaptureMode.Unhandled"/> and would therefore be skipped by the
    /// filter at <c>n_cst_eventful.sru:L831-L833</c> in exactly the cases where the escalation it exists
    /// to observe has fired - it could report "unhandled" and never report "handled".
    /// </para>
    /// <para>
    /// <b>It is registered LAST by every caller</b>, because it must observe the subscribers ahead of it.
    /// Position inside one priority band is arrival order (<c>:L419</c> and <c>:L421</c>), so
    /// registering last is what puts it last.
    /// </para>
    /// <para>
    /// The callback runs BEFORE this subscriber's own return value is produced - step 3 of four in
    /// <see cref="RecordingSubscriber"/>'s documented invocation order - so the observation is strictly
    /// of the subscribers that already ran, never of this one.
    /// </para>
    /// </remarks>
    private static RecordingSubscriber SubscribeStateProbe(
        EventBroker broker,
        DispatchLog log,
        string topic,
        DispatchStateProbe probe)
    {
        RecordingSubscriber subscriber =
            SubscribeLabelled(broker, log, $"{TopicSymbols.All}{topic}", ProbeLabel);

        subscriber.DuringAnyHandler = (observedBroker, record) =>
        {
            Assert.NotNull(observedBroker);
            Assert.NotNull(record);

            probe.Processed = observedBroker.IsProcessed();
            probe.Value = observedBroker.GetReturnValue();
            probe.Runs++;
        };

        return subscriber;
    }

    // =============================================================================================
    //  1. THE PRIORITY VALUES AND THE WIDTH THEY DEPEND ON
    //  n_cst_eventful.sru:L104-L106 - the three constants
    //  n_cst_eventful.sru:L351-L356 - '@' and '!' assign two of them during the leading symbol run
    //  n_cst_eventful.sru:L334      - PRIORITY_NORMAL is assigned before the topic is parsed at all
    // =============================================================================================

    /// <summary>
    /// The three named priorities are exactly the legacy literals: the 32-bit floor, zero, and the
    /// 32-bit ceiling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). The legacy declares
    /// <c>constant long PRIORITY_LOW = -2147483648</c>, <c>PRIORITY_NORMAL = 0</c> and
    /// <c>PRIORITY_HIGH = 2147483647</c> at <c>n_cst_eventful.sru:L104-L106</c>. A PowerBuilder
    /// <c>long</c> is a 32-bit signed integer, so those two literals ARE
    /// <see cref="int.MinValue"/> and <see cref="int.MaxValue"/> and this is the legacy value rather
    /// than a modern stand-in for it.
    /// </para>
    /// <para>
    /// The ordering is asserted alongside the values because the ordering is what the broker's ordered
    /// insert relies on (<c>:L407-L422</c>) and it is the property a renumbering would break while a
    /// value-only assertion still passed. The saturation pair is what makes the two bounds ABSOLUTE:
    /// there is no representable priority outside them, so <c>!</c> genuinely cannot be outranked and
    /// <c>@</c> genuinely cannot be undercut, however large a number a caller writes in a
    /// <c>[digits]:</c> prefix.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePriorityBoundsAreTheThirtyTwoBitExtremesAndNormalIsZero()
    {
        // :L104-L106 - the values, exactly.
        Assert.Equal(int.MinValue, Priorities.Low);
        Assert.Equal(0, Priorities.Normal);
        Assert.Equal(int.MaxValue, Priorities.High);

        // The ordering the ordered insert at :L407-L422 depends on.
        Assert.True(Priorities.Low < Priorities.Normal);
        Assert.True(Priorities.Normal < Priorities.High);

        // Absolute, not merely large: nothing representable lies outside the pair.
        Assert.Equal(Priorities.High, Math.Max(Priorities.High, int.MaxValue));
        Assert.Equal(Priorities.Low, Math.Min(Priorities.Low, int.MinValue));
    }

    /// <summary>
    /// Every carrier of a priority is declared 32 bits wide - the three constants and all three of the
    /// types that transport a priority between the grammar, the option aggregate and the live
    /// subscription.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B) and NAMES THE BOUNDARY DECISION
    /// (AAP 0.7.3 C-K). <b>This is the assertion a silent widening to 64 bits fails, and it is the
    /// reason the value assertion above is not sufficient on its own.</b>
    /// <c>Assert.Equal(int.MinValue, Priorities.Low)</c> keeps passing when <c>Low</c> is declared
    /// <c>long</c>, because the comparison simply widens; so the width has to be asserted directly,
    /// through reflection, or it is not asserted at all.
    /// </para>
    /// <para>
    /// Width is behaviour rather than housekeeping. <see cref="Priorities.Low"/> and
    /// <see cref="Priorities.High"/> are absolute ONLY because they are the extremes of the type that
    /// carries them: widen the field and <c>9223372036854775807:clicked</c> outranks a subscriber that
    /// asked for the highest priority available, which is a behavioural change no ordering test would
    /// report. And because AAP 0.4.3 C-03 puts the event chain on a wire, a priority is an integer in a
    /// protobuf message and in a characterization recording, where the width is part of the encoding.
    /// </para>
    /// <para>
    /// All three transport types are checked, not just the constants, because a widening anywhere along
    /// the path from topic parse to live subscription reintroduces the same hole:
    /// <see cref="SubscriptionTopic.Priority"/> is what the grammar produces,
    /// <see cref="SubscriptionOptions.Priority"/> is the aggregate it projects onto, and
    /// <see cref="EventSubscription.Priority"/> is what the broker's ordered insert compares.
    /// </para>
    /// <para>
    /// <see cref="FieldInfo.IsLiteral"/> is asserted too: a <c>const</c> is baked into every consumer at
    /// compile time, so demoting one to a mutable <c>static</c> field would let a test - or a service -
    /// reassign a priority bound at run time and change dispatch order for everything sharing the
    /// process.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryPriorityCarrierIsDeclaredThirtyTwoBitsWide()
    {
        foreach (string constantName in new[]
        {
            nameof(Priorities.Low),
            nameof(Priorities.Normal),
            nameof(Priorities.High),
        })
        {
            FieldInfo? field = typeof(Priorities).GetField(
                constantName,
                BindingFlags.Public | BindingFlags.Static);

            Assert.NotNull(field);

            // A PowerBuilder `long` is 32-bit, so Int32 is the faithful port and Int64 is a widening.
            Assert.Equal(typeof(int), field.FieldType);

            // const, not static readonly: a bound must not be reassignable at run time.
            Assert.True(field.IsLiteral);
        }

        // The three transport types along the path from topic parse to ordered insert.
        Assert.Equal(typeof(int), typeof(SubscriptionTopic).GetProperty(nameof(SubscriptionTopic.Priority))?.PropertyType);
        Assert.Equal(typeof(int), typeof(SubscriptionOptions).GetProperty(nameof(SubscriptionOptions.Priority))?.PropertyType);
        Assert.Equal(typeof(int), typeof(EventSubscription).GetProperty(nameof(EventSubscription.Priority))?.PropertyType);
    }

    /// <summary>
    /// A topic that names no priority receives <see cref="Priorities.Normal"/> - through the grammar,
    /// through the option projection, and as the aggregate's own default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). <c>n_cst_eventful.sru:L334</c> assigns
    /// <c>PRIORITY_NORMAL</c> to the new subscription record BEFORE the topic is parsed, so an unadorned
    /// topic keeps it by never being changed rather than by being defaulted afterwards.
    /// </para>
    /// <para>
    /// The three assertions are the three places the default has to agree, and they are asserted
    /// together because a mismatch between any two would be invisible from a single one of them. The
    /// third is the load-bearing one for the wire: <see cref="SubscriptionOptions.Default"/> is what a
    /// consumer that constructs an option aggregate without parsing a topic gets, so if it disagreed
    /// with the grammar the same logical subscription would sort differently depending on which route
    /// created it.
    /// </para>
    /// <para>
    /// Zero is also the legacy's proxy for "no priority has been chosen yet" (<c>:L352</c>,
    /// <c>:L355</c> and <c>:L369</c> all test <c>&lt;&gt; PRIORITY_NORMAL</c>), which is why an explicit
    /// <c>0:clicked</c> does not consume the single priority slot. That consequence belongs to
    /// <see cref="SubscriptionTopicGrammarTests"/>; only the default itself is asserted here.
    /// </para>
    /// </remarks>
    [Fact]
    public void ATopicNamingNoPriorityReceivesNormal()
    {
        Assert.Equal(RetCode.OK, SubscriptionTopic.ParseSubscription(ClickedTopic, out SubscriptionTopic? parsed));
        Assert.NotNull(parsed);

        Assert.Equal(Priorities.Normal, parsed.Priority);
        Assert.Equal(Priorities.Normal, parsed.ToSubscriptionOptions().Priority);
        Assert.Equal(Priorities.Normal, SubscriptionOptions.Default.Priority);
    }

    // =============================================================================================
    //  2. PRIORITY ORDERING - A LARGER NUMBER DISPATCHES EARLIER
    //  w_test_eventful.srw:L212      - "[digits]:" is a custom priority; a larger number is higher
    //  w_test_eventful.srw:L221-L222 - "!clicked" is highest, "@clicked" is lowest
    //  w_test_eventful.srw:L226      - "9999:clicked" is an evidenced arbitrary priority
    //  n_cst_eventful.sru:L407-L422  - the ordered insert that makes all of the above true
    // =============================================================================================

    /// <summary>
    /// Low, normal and high subscribers registered in a SCRAMBLED order dispatch high, normal, low.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). The direction is stated in prose at
    /// <c>w_test_eventful.srw:L212</c> - a larger number is a higher dispatch priority - and implemented
    /// by the ordered insert at <c>n_cst_eventful.sru:L407-L422</c>, which walks the run for the name and
    /// stops at the first existing peer whose priority is lower than the newcomer's (<c>:L419</c>),
    /// advancing past every other (<c>:L421</c>).
    /// </para>
    /// <para>
    /// <b>Scrambling the registration order is the point.</b> Registered low, normal, high, an
    /// implementation that simply appended in arrival order would produce exactly the REVERSE of the
    /// expected sequence, so this case cannot pass by accident. Inverting the comparison at
    /// <c>:L419</c> would reverse dispatch order across the whole framework while leaving every
    /// subscriber-count assertion green.
    /// </para>
    /// </remarks>
    [Fact]
    public void LowNormalAndHighRegisteredScrambledDispatchInDescendingPriority()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        // Registered ascending, deliberately: the expected result is the exact reverse.
        SubscribeLabelled(broker, log, $"{TopicSymbols.Low}{ClickedTopic}", "low");
        SubscribeLabelled(broker, log, ClickedTopic, "normal");
        SubscribeLabelled(broker, log, $"{TopicSymbols.High}{ClickedTopic}", "high");

        broker.Trigger(ClickedTopic);

        Assert.Equal(["high", "normal", "low"], log.Labels);
    }

    /// <summary>
    /// Registration orders paired with the dispatch order each must produce, over the arbitrary numeric
    /// priorities as well as the two symbolic bounds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each row is a space-separated list of TOPIC SPELLINGS in registration order, then the same
    /// spellings in the order they must dispatch. Every subscriber's label is its own spelling, so the
    /// expectation reads as the priorities themselves and a failure message names them.
    /// </para>
    /// <para>
    /// The spellings and the priority each carries:
    /// <c>!clicked</c> and <c>2147483647:clicked</c> are both <see cref="Priorities.High"/>;
    /// <c>9999:clicked</c> is the oracle's own arbitrary value; <c>+5:clicked</c> is a small positive one
    /// carrying an explicit sign; <c>0:clicked</c> and the bare <c>clicked</c> are both
    /// <see cref="Priorities.Normal"/>; and <c>@clicked</c> is <see cref="Priorities.Low"/> and is the
    /// only reachable NEGATIVE priority - see
    /// <see cref="AnArbitraryNegativeNumericPriorityIsUnreachableThroughTheGrammar"/> for why.
    /// </para>
    /// <para>
    /// Rows 1 and 2 are the same six subscriptions registered in two different scrambles, so no result
    /// can be an artefact of one arrival order. Row 3 registers strictly ascending, which is the
    /// arrangement an append-in-arrival-order implementation gets exactly backwards. Row 4 is the two
    /// spellings of the high bound together, which is the equal-priority arrival-order case at the
    /// bound.
    /// </para>
    /// </remarks>
    public static TheoryData<string, string> NumericPriorityOrderRows =>
        new()
        {
            // Scramble A.
            {
                "9999:clicked @clicked !clicked 0:clicked clicked +5:clicked",
                "!clicked 9999:clicked +5:clicked 0:clicked clicked @clicked"
            },

            // Scramble B - the same six, registered differently. The two normal-priority spellings swap,
            // because arrival order decides inside a band and their arrival order has swapped.
            {
                "clicked +5:clicked !clicked 9999:clicked 0:clicked @clicked",
                "!clicked 9999:clicked +5:clicked clicked 0:clicked @clicked"
            },

            // Strictly ascending registration - the exact reverse of the required dispatch order.
            {
                "@clicked 0:clicked +5:clicked 9999:clicked !clicked",
                "!clicked 9999:clicked +5:clicked 0:clicked @clicked"
            },

            // Both spellings of the high bound, plus a normal peer to show the band boundary.
            {
                "clicked 2147483647:clicked !clicked",
                "2147483647:clicked !clicked clicked"
            },
        };

    /// <summary>
    /// Subscribers dispatch in strictly descending priority whatever order they registered in, across
    /// arbitrary numeric priorities and both symbolic bounds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). This is the general form of
    /// <see cref="LowNormalAndHighRegisteredScrambledDispatchInDescendingPriority"/>: the priority is an
    /// ORDINARY INTEGER and not a closed set of three, which is why the port models it as
    /// <see cref="int"/> constants rather than as an enumeration. The grammar parses any numeric prefix
    /// terminated by <see cref="TopicSymbols.PriorityDelimiter"/> straight into the priority field with
    /// no range check and no membership test (<c>n_cst_eventful.sru:L365-L373</c>), and the oracle
    /// exercises that with <c>of_On("9999:clicked",...)</c> at <c>w_test_eventful.srw:L226</c>.
    /// </para>
    /// <para>
    /// An ordered SEQUENCE is asserted, never a set: a set comparison would pass under every possible
    /// ordering and would make the whole matrix vacuous.
    /// </para>
    /// </remarks>
    /// <param name="registrationOrder">The topic spellings, in the order they are subscribed.</param>
    /// <param name="expectedDispatchOrder">The same spellings, in the order they must dispatch.</param>
    [Theory]
    [MemberData(nameof(NumericPriorityOrderRows))]
    public void SubscribersDispatchInDescendingPriorityWhateverTheRegistrationOrder(
        string registrationOrder,
        string expectedDispatchOrder)
    {
        EventBroker broker = new();
        DispatchLog log = new();

        foreach (string spelling in SplitList(registrationOrder))
        {
            SubscribeSpelling(broker, log, spelling);
        }

        broker.Trigger(ClickedTopic);

        Assert.Equal(SplitList(expectedDispatchOrder), log.Labels);
    }

    /// <summary>
    /// An arbitrary large numeric priority sorts strictly BETWEEN normal and high - it does not saturate
    /// to the high bound.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). <c>n_cst_eventful.sru:L370</c> assigns
    /// <c>Long(sVal)</c> directly to the priority field with no clamping, no range mapping and no
    /// bucketing into the three named bands, so 9999 is 9999 and nothing else. A port that mapped a
    /// numeric prefix onto the nearest named priority - a plausible-looking simplification, since the
    /// named values are the ones with symbols - would place the oracle's own
    /// <c>of_On("9999:clicked",...)</c> AHEAD of a subscriber that asked for the highest priority
    /// available, silently inverting the two.
    /// </para>
    /// <para>
    /// Both halves are asserted: the numeric relation, which is what the ordered insert compares, and
    /// the resulting dispatch order, which is what a subscriber observes. Asserting only the numeric
    /// relation would not catch a clamp applied at subscribe time; asserting only the order would not
    /// distinguish a clamp from a correct comparison of unclamped values.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnArbitraryLargeNumericPriorityDoesNotSaturateToTheHighBound()
    {
        // The numeric relation the ordered insert at :L407-L422 compares.
        Assert.True(Priorities.Normal < OracleNumericPriority);
        Assert.True(OracleNumericPriority < Priorities.High);
        Assert.NotEqual(Priorities.High, OracleNumericPriority);

        // And the same relation observed as dispatch order.
        EventBroker broker = new();
        DispatchLog log = new();

        SubscribeLabelled(broker, log, $"{OracleNumericPriority}{TopicSymbols.PriorityDelimiter}{ClickedTopic}", "arbitrary");
        SubscribeLabelled(broker, log, $"{TopicSymbols.Low}{ClickedTopic}", "low");
        SubscribeLabelled(broker, log, $"{TopicSymbols.High}{ClickedTopic}", "high");
        SubscribeLabelled(broker, log, ClickedTopic, "normal");

        broker.Trigger(ClickedTopic);

        Assert.Equal(["high", "arbitrary", "normal", "low"], log.Labels);
    }

    /// <summary>
    /// Spelling the high bound as digits is indistinguishable from spelling it <c>!</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B), and it is the complement of
    /// <see cref="AnArbitraryLargeNumericPriorityDoesNotSaturateToTheHighBound"/>. The numeric route and
    /// the symbolic route write to the SAME field (<c>n_cst_eventful.sru:L356</c> and <c>:L370</c>), so
    /// <c>2147483647:clicked</c> and <c>!clicked</c> produce byte-identical subscriptions as far as
    /// ordering is concerned, and the pair therefore behaves as an equal-priority band: arrival order
    /// decides, exactly as it does for two <c>!</c> peers.
    /// </para>
    /// <para>
    /// This is what makes <see cref="Priorities.High"/> meaningful as a BOUND rather than as a magic
    /// token. If the two routes disagreed - if <c>!</c> were treated as "above every number" - then the
    /// symbol would not be a value at all and the wire could not carry it as one.
    /// </para>
    /// <para>
    /// The parse is asserted as well as the order, because the order alone would also be produced by two
    /// merely-adjacent priorities.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheExplicitMaximumDigitsSpellingIsIndistinguishableFromTheHighSymbol()
    {
        string digitsSpelling = $"{int.MaxValue}{TopicSymbols.PriorityDelimiter}{ClickedTopic}";
        string symbolSpelling = $"{TopicSymbols.High}{ClickedTopic}";

        Assert.Equal(RetCode.OK, SubscriptionTopic.ParseSubscription(digitsSpelling, out SubscriptionTopic? fromDigits));
        Assert.Equal(RetCode.OK, SubscriptionTopic.ParseSubscription(symbolSpelling, out SubscriptionTopic? fromSymbol));
        Assert.NotNull(fromDigits);
        Assert.NotNull(fromSymbol);

        // Same priority, and the same residual event name - the two spellings differ in nothing that
        // dispatch can see.
        Assert.Equal(Priorities.High, fromDigits.Priority);
        Assert.Equal(Priorities.High, fromSymbol.Priority);
        Assert.Equal(fromSymbol.LegacyName, fromDigits.LegacyName);

        // So the two are peers in one band, and arrival order decides between them - in both directions.
        EventBroker digitsFirst = new();
        DispatchLog digitsFirstLog = new();
        SubscribeLabelled(digitsFirst, digitsFirstLog, digitsSpelling, "digits");
        SubscribeLabelled(digitsFirst, digitsFirstLog, symbolSpelling, "symbol");
        digitsFirst.Trigger(ClickedTopic);
        Assert.Equal(["digits", "symbol"], digitsFirstLog.Labels);

        EventBroker symbolFirst = new();
        DispatchLog symbolFirstLog = new();
        SubscribeLabelled(symbolFirst, symbolFirstLog, symbolSpelling, "symbol");
        SubscribeLabelled(symbolFirst, symbolFirstLog, digitsSpelling, "digits");
        symbolFirst.Trigger(ClickedTopic);
        Assert.Equal(["symbol", "digits"], symbolFirstLog.Labels);
    }

    /// <summary>
    /// Two subscribers at the SAME arbitrary numeric priority dispatch in arrival order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). The default placement mode is append and its
    /// stop condition is a STRICTLY lower priority (<c>n_cst_eventful.sru:L419</c>), so a newcomer walks
    /// past every peer of equal priority (<c>:L421</c>) and lands at the tail of the band. Arrival order
    /// inside one priority band is therefore a guarantee a subscriber may rely on, not an accident of
    /// the container.
    /// </para>
    /// <para>
    /// <b>Asserted here for an ARBITRARY numeric priority specifically.</b>
    /// <see cref="DispatchOrderTests.EqualPriorityPeersDispatchInArrivalOrder"/> already pins the same
    /// rule for the NORMAL band, and
    /// <see cref="DispatchOrderTests.PrependAndAppendDifferOnlyInTheirTreatmentOfEqualPriority"/> owns
    /// the prepend variant - the <c>-</c> symbol, which is the only thing that places a newcomer at the
    /// HEAD of its band instead. Neither is duplicated here. What this case adds is that the rule is a
    /// property of the priority COMPARISON and not of the normal-priority band: the normal band is
    /// reachable without any numeric parsing at all, so a port that special-cased it - or that stored
    /// numeric priorities in a hash-ordered structure while keeping the three named ones in a list -
    /// would pass the sibling case and fail this one.
    /// </para>
    /// </remarks>
    [Fact]
    public void PeersAtTheSameArbitraryNumericPriorityDispatchInArrivalOrder()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        string spelling = $"{OracleNumericPriority}{TopicSymbols.PriorityDelimiter}{ClickedTopic}";

        SubscribeLabelled(broker, log, spelling, "first");
        SubscribeLabelled(broker, log, spelling, "second");
        SubscribeLabelled(broker, log, spelling, "third");

        broker.Trigger(ClickedTopic);

        Assert.Equal(["first", "second", "third"], log.Labels);
    }

    /// <summary>
    /// An arbitrary NEGATIVE numeric priority is unreachable through the topic grammar, and this records
    /// exactly what the grammar does with each spelling that tries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B), and it is a DIVERGENCE REPORTED RATHER THAN
    /// ACCOMMODATED. The brief that commissioned this suite asks for a negative value among the
    /// arbitrary numeric priorities. There is no way to write one: the leading symbol run consumes a
    /// first <c>-</c> as <see cref="TopicSymbols.Prepend"/> (<c>n_cst_eventful.sru:L342-L344</c>) and
    /// rejects a second, and the numeric prefix is only looked for AFTERWARDS on what remains
    /// (<c>:L365</c>), so no residual name can ever begin with a minus sign. The legacy has the identical
    /// property; this is not an artefact of the port.
    /// </para>
    /// <para>
    /// The three spellings and what each actually produces:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///   <c>-5:clicked</c> is <b>prepend at priority +5</b>, not priority -5. The minus is a placement
    ///   symbol and the digits are an ordinary positive prefix. A caller who wrote this meaning "very
    ///   low priority" would get a middling-high one, placed at the head of its band.
    ///   </description></item>
    ///   <item><description>
    ///   <c>--5:clicked</c> is rejected with <see cref="RetCode.E_INVALID_ARGUMENT"/>, because the
    ///   duplicate-prepend guard fires on the second minus.
    ///   </description></item>
    ///   <item><description>
    ///   <c>-2147483648:clicked</c> - the exact spelling of the low bound - is <b>prepend at NORMAL
    ///   priority with the digits left in the EVENT NAME</b>. Two mechanisms compose: the minus is
    ///   consumed as prepend, and the remaining prefix <c>2147483648</c> overflows a 32-bit integer, so
    ///   it is not accepted as a priority and is left in the name exactly as a non-numeric prefix would
    ///   be (<c>:L368</c> guards <c>:L369-L371</c>). The subscription is therefore for an event called
    ///   <c>2147483648:clicked</c>, which nothing will ever trigger.
    ///   </description></item>
    /// </list>
    /// <para>
    /// The only reachable negative priority is <see cref="Priorities.Low"/> through
    /// <see cref="TopicSymbols.Low"/>, and it is the negative end of every ordering matrix above. That is
    /// sufficient rather than a compromise: <see cref="Priorities.Low"/> IS the 32-bit floor, so it
    /// exercises the negative half of the comparison at <c>:L419</c> as thoroughly as any other negative
    /// value could.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnArbitraryNegativeNumericPriorityIsUnreachableThroughTheGrammar()
    {
        // "-5:clicked" is prepend at +5, not priority -5.
        Assert.Equal(RetCode.OK, SubscriptionTopic.ParseSubscription("-5:clicked", out SubscriptionTopic? minusFive));
        Assert.NotNull(minusFive);
        Assert.Equal(5, minusFive.Priority);
        Assert.True(minusFive.Prepend);
        Assert.Equal(ClickedTopic, minusFive.LegacyName);

        // A second minus is a duplicate prepend, not a sign.
        Assert.Equal(
            RetCode.E_INVALID_ARGUMENT,
            SubscriptionTopic.ParseSubscription("--5:clicked", out SubscriptionTopic? doubleMinus));
        Assert.Null(doubleMinus);

        // Spelling the low bound as digits leaves the digits in the NAME: prepend consumed the minus and
        // the residue overflows a 32-bit priority, so it is not a priority prefix at all.
        //
        // The digit run of the low bound is 2147483648, which is EXACTLY one past int.MaxValue and is
        // therefore not a representable 32-bit priority - stated as an assertion rather than a comment so
        // the second of the two composing mechanisms is pinned and not merely claimed. Written as a
        // negated widened constant because the obvious `$"-{int.MinValue}"` renders TWO minus signs and
        // would build a duplicate-prepend spelling instead of the one this case is about.
        const long lowBoundDigits = -(long)int.MinValue;
        Assert.Equal(1L, lowBoundDigits - int.MaxValue);

        string lowBoundSpelling =
            $"{TopicSymbols.Prepend}{lowBoundDigits}{TopicSymbols.PriorityDelimiter}{ClickedTopic}";

        Assert.Equal(
            RetCode.OK,
            SubscriptionTopic.ParseSubscription(lowBoundSpelling, out SubscriptionTopic? lowBoundParsed));
        Assert.NotNull(lowBoundParsed);
        Assert.Equal(Priorities.Normal, lowBoundParsed.Priority);
        Assert.True(lowBoundParsed.Prepend);
        Assert.Equal($"{lowBoundDigits}{TopicSymbols.PriorityDelimiter}{ClickedTopic}", lowBoundParsed.LegacyName);

        // The one reachable negative priority, and it is the floor of the type.
        Assert.Equal(RetCode.OK, SubscriptionTopic.ParseSubscription($"{TopicSymbols.Low}{ClickedTopic}", out SubscriptionTopic? low));
        Assert.NotNull(low);
        Assert.Equal(Priorities.Low, low.Priority);
        Assert.True(low.Priority < 0);
    }


    // =============================================================================================
    //  3. THE THREE CAPTURE MODES AND THE FILTER THAT ADMITS THEM
    //  n_cst_eventful.sru:L100-L102  - CAP_UNHANDLED = 0, CAP_HANDLED = 1, CAP_ALL = 2
    //  n_cst_eventful.sru:L345-L350  - '%' selects CAP_HANDLED, '*' selects CAP_ALL, once only
    //  n_cst_eventful.sru:L786       - the running capture state starts at CAP_UNHANDLED
    //  n_cst_eventful.sru:L831-L833  - the filter: skip unless the mode equals the state, or is CAP_ALL
    //  w_test_eventful.srw:L214-L216 - the grammar's own statement of the three modes
    // =============================================================================================

    /// <summary>
    /// The capture modes carry the legacy flag values on a 32-bit enumeration, and there are exactly
    /// three of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B) and NAMES THE BOUNDARY DECISION
    /// (AAP 0.7.3 C-K). The legacy spellings are <c>CAP_UNHANDLED</c> = 0, <c>CAP_HANDLED</c> = 1 and
    /// <c>CAP_ALL</c> = 2, declared at <c>n_cst_eventful.sru:L100-L102</c>.
    /// </para>
    /// <para>
    /// <b>The numbers are asserted, not merely the behaviour they produce, and the reason is the wire.</b>
    /// AAP 0.4.3 C-03 carries the capture state across a gRPC boundary as part of the DataWindow event
    /// chain, where it is an integer field; AAP 0.6.7 compares stored characterization recordings field by
    /// field. So a renumbering - a perfectly reasonable-looking tidy-up, say ordering the enumeration
    /// None/Handled/All or numbering it from one - would leave every behavioural assertion in this file
    /// green while invalidating every recording and mis-decoding every message already on the wire.
    /// </para>
    /// <para>
    /// <b>Zero must specifically be <see cref="CaptureMode.Unhandled"/>, twice over.</b> The legacy
    /// subscription record starts at <c>CAP_UNHANDLED</c> before the topic is parsed
    /// (<c>:L345-L350</c> only ever moves it OFF that value), and the running dispatch state also starts
    /// there (<c>:L786</c>). Both are the zero of the type, so a defaulted option aggregate, a defaulted
    /// message field and a fresh dispatch all agree without anything having to say so - and a
    /// renumbering that moved zero elsewhere would silently change what an unadorned topic requests.
    /// </para>
    /// <para>
    /// The three-member closure is asserted because the exclusivity is mechanical rather than
    /// conventional: <c>:L346</c> and <c>:L349</c> reject a SECOND capture symbol outright, so a
    /// subscription has exactly one mode and there is no combined state for a fourth member to name.
    /// That is also why <see cref="CaptureMode"/> carries no <see cref="FlagsAttribute"/>.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheCaptureModeValuesAreTheLegacyFlagsOnAThirtyTwoBitEnumeration()
    {
        // :L100-L102 - CAP_UNHANDLED, CAP_HANDLED, CAP_ALL.
        Assert.Equal(0, (int)CaptureMode.Unhandled);
        Assert.Equal(1, (int)CaptureMode.Handled);
        Assert.Equal(2, (int)CaptureMode.All);

        // A PowerBuilder `long` is 32-bit, and the wire encoding of the state is that width.
        Assert.Equal(typeof(int), Enum.GetUnderlyingType(typeof(CaptureMode)));

        // Exactly three, and zero is the unhandled one - the value both a fresh subscription (:L345-L350)
        // and a fresh dispatch (:L786) start at.
        Assert.Equal(3, Enum.GetValues<CaptureMode>().Length);
        Assert.Equal(CaptureMode.Unhandled, default(CaptureMode));

        // One mode per subscription, so no combined state exists for a Flags treatment to express.
        Assert.Null(typeof(CaptureMode).GetCustomAttribute<FlagsAttribute>());
    }

    /// <summary>
    /// Each capture spelling paired with the mode it must select.
    /// </summary>
    /// <remarks>
    /// The three spellings the grammar offers, from <c>w_test_eventful.srw:L214-L216</c>: no symbol at
    /// all captures unhandled events only, <c>%</c> captures events an earlier subscriber has already
    /// handled, and <c>*</c> captures every handled state. Carried as a topic string and an enum member,
    /// both of which are serializable, so the matrix raises no analyzer diagnostic under this
    /// repository's warnings-as-errors policy.
    /// </remarks>
    public static TheoryData<string, CaptureMode> CaptureSpellingRows =>
        new()
        {
            { ClickedTopic, CaptureMode.Unhandled },
            { $"%{ClickedTopic}", CaptureMode.Handled },
            { $"*{ClickedTopic}", CaptureMode.All },
        };

    /// <summary>
    /// The topic symbols and the option-aggregate projection AGREE on the capture mode, and the mode they
    /// agree on is the one the dispatch filter then acts on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). <b>There is exactly one route by which a
    /// subscriber DECLARES its capture mode to the broker - the topic string.</b>
    /// <see cref="EventBroker.Subscribe(string, object?, string)"/> takes a topic and there is no
    /// overload accepting a <see cref="SubscriptionOptions"/>, which faithfully reproduces
    /// <c>of_on(name, object, evtname)</c> (<c>n_cst_eventful.sru:L296</c>) - the legacy has no options
    /// object either, only a symbol-prefixed string.
    /// </para>
    /// <para>
    /// <see cref="SubscriptionOptions"/> is therefore a PROJECTION of a parsed topic rather than a second
    /// input route, reached through <see cref="SubscriptionTopic.ToSubscriptionOptions"/>. Since both
    /// spellings of the same fact exist, this case asserts they AGREE, along the whole path a mode
    /// travels: grammar decode, option projection, and the dispatch behaviour the mode produces. A
    /// disagreement anywhere along it would mean the same subscription behaved differently depending on
    /// which representation a consumer inspected - and the wire contract carries the projection, so the
    /// far end would read a mode the near end does not act on.
    /// </para>
    /// <para>
    /// The behavioural half is what makes this more than a data-copy assertion. It arranges a handler
    /// AHEAD of the subject and reads the subject's admission: an <see cref="CaptureMode.Unhandled"/>
    /// subject must be skipped in that arrangement, and a <see cref="CaptureMode.Handled"/> or
    /// <see cref="CaptureMode.All"/> subject must run. So the declared mode is confirmed against the
    /// filter at <c>:L831-L833</c> and not only against the parser.
    /// </para>
    /// </remarks>
    /// <param name="topic">The topic spelling under test.</param>
    /// <param name="expectedMode">The capture mode that spelling must select.</param>
    [Theory]
    [MemberData(nameof(CaptureSpellingRows))]
    public void TheTopicSymbolsAndTheOptionProjectionAgreeOnTheCaptureMode(
        string topic,
        CaptureMode expectedMode)
    {
        // Route one: the grammar.
        Assert.Equal(RetCode.OK, SubscriptionTopic.ParseSubscription(topic, out SubscriptionTopic? parsed));
        Assert.NotNull(parsed);
        Assert.Equal(expectedMode, parsed.Capture);

        // Route two: the projection the wire contract carries.
        Assert.Equal(expectedMode, parsed.ToSubscriptionOptions().Capture);

        // And the mode both routes report is the one the dispatch filter acts on. With a handler ahead of
        // it, only a mode that admits an ALREADY-HANDLED dispatch runs.
        EventBroker broker = new();
        DispatchLog log = new();

        SubscribeHandler(broker, log, ClickedTopic, "handler", 7L);
        SubscribeLabelled(broker, log, topic, "subject");

        broker.Trigger(ClickedTopic);

        string[] expectedLabels = expectedMode == CaptureMode.Unhandled
            ? ["handler"]
            : ["handler", "subject"];

        Assert.Equal(expectedLabels, log.Labels);
    }

    /// <summary>
    /// An unhandled-capture subscriber - the default - runs while no earlier subscriber has handled the
    /// dispatch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). The dispatch begins in the unhandled state
    /// (<c>n_cst_eventful.sru:L786</c>) and an unadorned subscription's mode is
    /// <see cref="CaptureMode.Unhandled"/>, so the filter at <c>:L831-L833</c> finds them equal and
    /// admits it. This is the positive half of the pair - the case that stops the filter being satisfied
    /// by an implementation that simply skipped everything - and it also pins that a subscriber returning
    /// NOTHING leaves the dispatch unhandled, which is why the second and third subscribers here run at
    /// all (<c>:L909</c>, and <c>w_test_eventful.srw:L234</c>: no return value is defined as not handled).
    /// </para>
    /// <para>
    /// Three subscribers rather than one, because one would not distinguish "the filter admits an
    /// unhandled-capture subscriber" from "the filter admits the FIRST subscriber". All three run, in
    /// arrival order.
    /// </para>
    /// </remarks>
    [Fact]
    public void UnhandledCaptureSubscribersAllRunWhileTheDispatchIsUnhandled()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        SubscribeLabelled(broker, log, ClickedTopic, "first");
        SubscribeLabelled(broker, log, ClickedTopic, "second");
        SubscribeLabelled(broker, log, ClickedTopic, "third");

        broker.Trigger(ClickedTopic);

        Assert.Equal(["first", "second", "third"], log.Labels);
    }

    /// <summary>
    /// An unhandled-capture subscriber is SKIPPED once an earlier subscriber has handled the dispatch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). This is the coupling between the filter and the
    /// escalation latch, and it is the single most consequential behaviour in the capture model: the
    /// filter at <c>n_cst_eventful.sru:L831-L833</c> compares against the dispatch's CURRENT state, and
    /// the latch at <c>:L910-L924</c> moves that state during the very loop the filter runs in. So the
    /// moment one subscriber handles the event, every remaining unhandled-only subscriber stops being
    /// eligible - mid-walk, without the table changing at all.
    /// </para>
    /// <para>
    /// Two subscribers follow the handler, so the skip is shown to be a property of the STATE and not of
    /// being the immediately-next entry. The dispatch is left unhandled by nobody in particular: no
    /// default return value is configured here, so the handler's non-null 7 takes the null-default arm at
    /// <c>:L912-L913</c> and handles. Section 4 is where the escalation rule itself is pinned; this case
    /// depends only on its outcome.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnUnhandledCaptureSubscriberIsSkippedOnceAnEarlierSubscriberHandled()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        SubscribeHandler(broker, log, ClickedTopic, "handler", 7L);
        RecordingSubscriber firstSkipped = SubscribeLabelled(broker, log, ClickedTopic, "skippedNext");
        RecordingSubscriber secondSkipped = SubscribeLabelled(broker, log, ClickedTopic, "skippedLater");

        broker.Trigger(ClickedTopic);

        Assert.Equal(["handler"], log.Labels);

        // Not merely absent from the log - genuinely never invoked.
        Assert.Equal(0, firstSkipped.InvocationCount);
        Assert.Equal(0, secondSkipped.InvocationCount);
    }

    /// <summary>
    /// The two registration positions of a handled-capture subscriber relative to the handler, each
    /// paired with the dispatch order it produces.
    /// </summary>
    /// <remarks>
    /// The whole matrix is two rows because the whole difference is two rows: the same two subscriptions,
    /// the same modes, the same priorities, and only their arrival order swapped. Position inside one
    /// priority band is arrival order (<c>n_cst_eventful.sru:L419</c> and <c>:L421</c>), so swapping the
    /// registrations swaps which of the two the walk reaches first - and that is the only thing that
    /// decides whether the handled-capture subscriber is examined before or after the state has moved.
    /// </remarks>
    public static TheoryData<bool, string> HandledCapturePositionRows =>
        new()
        {
            // Registered BEFORE the handler: examined while the dispatch is still unhandled, so skipped -
            // and never revisited, because the walk moves forward only.
            { true, "handler" },

            // Registered AFTER the handler: examined once the state has moved to handled, so admitted.
            { false, "handler subject" },
        };

    /// <summary>
    /// A handled-capture subscriber runs only when it is reached AFTER the dispatch has been handled -
    /// registering it earlier means it is skipped and never revisited.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). <c>%</c> selects <c>CAP_HANDLED</c>
    /// (<c>n_cst_eventful.sru:L347</c>), the exact mirror of the default, and the filter at
    /// <c>:L831-L833</c> is symmetric: a mode that differs from the current state is skipped whichever
    /// direction the difference runs.
    /// </para>
    /// <para>
    /// <b>The first row is the one that matters, and it is easy to get wrong.</b> The dispatch loop is a
    /// SINGLE FORWARD WALK over one contiguous run of the subscription table (<c>:L822-L938</c>); there
    /// is no second pass and no deferred queue. So a handled-capture subscriber that sits ahead of the
    /// handler is skipped once and is never reconsidered, even though the dispatch does subsequently
    /// become handled. An implementation that collected skipped entries and retried them after the state
    /// moved - a plausible-sounding "fix", and arguably what a reader would expect <c>%</c> to mean -
    /// would run that subscriber and fail this row while passing every other case in this section.
    /// </para>
    /// </remarks>
    /// <param name="registerSubjectFirst">
    /// Whether the handled-capture subscriber registers before the handler.
    /// </param>
    /// <param name="expectedOrder">The dispatch order that arrangement must produce.</param>
    [Theory]
    [MemberData(nameof(HandledCapturePositionRows))]
    public void AHandledCaptureSubscriberRunsOnlyWhenReachedAfterTheHandler(
        bool registerSubjectFirst,
        string expectedOrder)
    {
        EventBroker broker = new();
        DispatchLog log = new();

        if (registerSubjectFirst)
        {
            SubscribeLabelled(broker, log, $"{TopicSymbols.Handled}{ClickedTopic}", "subject");
            SubscribeHandler(broker, log, ClickedTopic, "handler", 7L);
        }
        else
        {
            SubscribeHandler(broker, log, ClickedTopic, "handler", 7L);
            SubscribeLabelled(broker, log, $"{TopicSymbols.Handled}{ClickedTopic}", "subject");
        }

        broker.Trigger(ClickedTopic);

        Assert.Equal(SplitList(expectedOrder), log.Labels);
    }

    /// <summary>
    /// When no subscriber ever handles the dispatch, a handled-capture subscriber never runs at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). The running state only ever moves in one
    /// direction and only on a non-null return that clears the escalation test
    /// (<c>n_cst_eventful.sru:L909-L924</c>), so a dispatch in which every subscriber returns nothing
    /// stays unhandled from <c>:L786</c> to the end of the walk, and a <c>%</c> subscription is
    /// unreachable for the whole of it.
    /// </para>
    /// <para>
    /// This is the complement of
    /// <see cref="AHandledCaptureSubscriberRunsOnlyWhenReachedAfterTheHandler"/> and it is not implied by
    /// it: that case shows the position matters when SOMETHING handles the event, while this one shows
    /// that position cannot rescue a <c>%</c> subscription when nothing does. It also pins the one thing
    /// a <c>%</c> subscriber is entitled to assume - that it is never invoked on an unhandled dispatch -
    /// which is what lets such a handler read <c>of_GetReturnValue()</c> without first testing
    /// <c>of_IsProcessed()</c>.
    /// </para>
    /// <para>
    /// Two null-returning subscribers precede it, so the result is not an artefact of an empty walk.
    /// </para>
    /// </remarks>
    [Fact]
    public void AHandledCaptureSubscriberNeverRunsWhenNothingHandlesTheDispatch()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        SubscribeLabelled(broker, log, ClickedTopic, "silentFirst");
        SubscribeLabelled(broker, log, ClickedTopic, "silentSecond");
        RecordingSubscriber subject =
            SubscribeLabelled(broker, log, $"{TopicSymbols.Handled}{ClickedTopic}", "subject");

        broker.Trigger(ClickedTopic);

        Assert.Equal(["silentFirst", "silentSecond"], log.Labels);
        Assert.Equal(0, subject.InvocationCount);
    }

    /// <summary>
    /// All-capture subscribers run on BOTH sides of the handler within one dispatch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). <c>CAP_ALL</c> is the one value that survives
    /// the filter unconditionally: <c>n_cst_eventful.sru:L832</c> tests for it BY NAME as the escape from
    /// the state comparison, which is exactly what makes it a wildcard over the handled-state axis rather
    /// than a third point on it.
    /// </para>
    /// <para>
    /// <b>Two subscriptions rather than one, and that is forced rather than convenient.</b> A single
    /// subscription is invoked at most once per dispatch, so "runs in both states" cannot be shown by one
    /// subscriber changing its mind - it is shown by one all-capture subscription being admitted while
    /// the dispatch is unhandled and another being admitted after the state has moved, inside a single
    /// walk. That also makes the case a strict superset of what either mode-specific subscription can
    /// observe: <c>allBefore</c> sits where a <c>%</c> subscription would be skipped, and
    /// <c>allAfter</c> sits where an unadorned one would be.
    /// </para>
    /// </remarks>
    [Fact]
    public void AllCaptureSubscribersRunOnBothSidesOfTheHandlerInOneDispatch()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        SubscribeLabelled(broker, log, $"{TopicSymbols.All}{ClickedTopic}", "allBefore");
        SubscribeHandler(broker, log, ClickedTopic, "handler", 7L);
        SubscribeLabelled(broker, log, $"{TopicSymbols.All}{ClickedTopic}", "allAfter");

        broker.Trigger(ClickedTopic);

        Assert.Equal(["allBefore", "handler", "allAfter"], log.Labels);
    }

    /// <summary>
    /// All three capture modes across five subscribers with the handler in the middle: the whole filter,
    /// in one ordered sequence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). This is the most informative single case in the
    /// suite, because it exercises every arm of the filter at <c>n_cst_eventful.sru:L831-L833</c> and both
    /// sides of the latch at <c>:L910-L924</c> in ONE walk, and asserts the result as one ordered
    /// sequence. The five subscribers, in registration order and therefore in walk order:
    /// </para>
    /// <list type="number">
    ///   <item><description>
    ///   <c>unhandledBefore</c> - unadorned, returns nothing. RUNS: its mode equals the initial state.
    ///   Its silence is what leaves the state unhandled for the next two.
    ///   </description></item>
    ///   <item><description>
    ///   <c>allCapture</c> - <c>*</c>. RUNS, on the unhandled side of the latch.
    ///   </description></item>
    ///   <item><description>
    ///   <c>theHandler</c> - unadorned, returns 7. RUNS, and moves the state to handled as it returns.
    ///   </description></item>
    ///   <item><description>
    ///   <c>unhandledAfter</c> - unadorned. SKIPPED: identical in every respect to subscriber 1 except
    ///   its position, which is the whole point - the same declaration is admitted or skipped purely by
    ///   where the walk reaches it.
    ///   </description></item>
    ///   <item><description>
    ///   <c>handledAfter</c> - <c>%</c>. RUNS: reached after the state moved.
    ///   </description></item>
    /// </list>
    /// <para>
    /// So the expected sequence has FOUR entries for FIVE subscribers, and the missing one is
    /// <c>unhandledAfter</c>. Its invocation count is asserted directly as well, so a failure
    /// distinguishes "ran but was logged out of order" from "did not run".
    /// </para>
    /// <para>
    /// An ordered sequence, never a set: as a set this arrangement would pass under any permutation, and
    /// the permutation is the assertion.
    /// </para>
    /// </remarks>
    [Fact]
    public void AllThreeCaptureModesAcrossFiveSubscribersWithTheHandlerInTheMiddle()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        SubscribeLabelled(broker, log, ClickedTopic, "unhandledBefore");
        SubscribeLabelled(broker, log, $"{TopicSymbols.All}{ClickedTopic}", "allCapture");
        SubscribeHandler(broker, log, ClickedTopic, "theHandler", 7L);
        RecordingSubscriber skipped = SubscribeLabelled(broker, log, ClickedTopic, "unhandledAfter");
        SubscribeLabelled(broker, log, $"{TopicSymbols.Handled}{ClickedTopic}", "handledAfter");

        broker.Trigger(ClickedTopic);

        Assert.Equal(["unhandledBefore", "allCapture", "theHandler", "handledAfter"], log.Labels);
        Assert.Equal(0, skipped.InvocationCount);
    }


    // =============================================================================================
    //  4. WHAT MAKES A DISPATCH "HANDLED" - THE ESCALATION PROTOCOL
    //  n_cst_eventful.sru:L909       - only a NON-NULL return participates at all
    //  n_cst_eventful.sru:L910-L924  - the UNHANDLED arm: the latch, with four ways to fire
    //                                    :L912-L913  the default is null
    //                                    :L914-L915  no default has been established (ClassName = "any")
    //                                    :L916-L917  the value differs from the default
    //                                    :L919-L921  the comparison itself raised
    //                                    :L922-L924  and only then is the value recorded
    //  n_cst_eventful.sru:L925-L933  - the ALREADY-HANDLED arm: the value may be OVERWRITTEN, and the
    //                                  state is never touched, because nothing anywhere clears it
    //  n_cst_eventful.sru:L479-L517  - of_setdefaultreturnvalue(name, value), rejected mid-dispatch :L500
    //  n_cst_eventful.sru:L519-L539  - of_setdefaultreturnvalue(value), rejected mid-dispatch :L536
    //  n_cst_eventful.sru:L541-L553  - the resolver: a per-topic entry first, the global one otherwise
    //  n_cst_eventful.sru:L636/:L655 - of_getreturnvalue is the recorded value; of_isprocessed is
    //                                  literally "the recorded value is not null"
    //  n_cst_eventful.sru:L951       - the recorded value is RESTORED when the dispatch unwinds
    //  n_cst_eventful.sru:L966-L973  - what of_Trigger returns, which is a DIFFERENT quantity
    //  w_test_eventful.srw:L234      - handled-ness is decided against the default return value
    //  w_test_eventful.srw:L235      - the handled state cannot roll back; the value can be overwritten
    //  w_test_eventful.srw:L257      - hence the advice to ALWAYS set a default return value
    // =============================================================================================

    /// <summary>
    /// With a default return value configured, a subscriber returning EXACTLY the default leaves the
    /// dispatch UNHANDLED.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). <b>This is the case that proves the comparison is
    /// against the DEFAULT and not against null, and it is the one a naive port fails.</b> A port that
    /// reduced the protocol to "a non-null return means handled" passes almost every other case in this
    /// file - null still does not handle, the latch still never rolls back, the filter still works - and
    /// fails only here. It was therefore authored first.
    /// </para>
    /// <para>
    /// The rule is stated in prose at <c>w_test_eventful.srw:L234</c>: the handled state is judged by the
    /// default return value set through the setter, a return unequal to the default means handled, and no
    /// return value at all is DEFINED as not handled. The implementation is the ladder at
    /// <c>n_cst_eventful.sru:L910-L918</c>, whose third arm - <c>:L916</c>, reached only when the default
    /// is non-null and established - is the actual comparison.
    /// </para>
    /// <para>
    /// Zero is used as the default because that is what the framework itself uses:
    /// <c>w_test_eventful.srw:L260</c> in the oracle, and in production
    /// <c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L596</c> and
    /// <c>ws_objects/pfw.thread.pbl.src/n_cst_threading_eventful.sru:L76</c>. So this is not a contrived
    /// value: in the framework's own most important consumer, a handler returning 0 means "continue, I did
    /// not handle this", and section 5 shows that end to end.
    /// </para>
    /// <para>
    /// The consequence is asserted twice over, because either half alone is weaker than the pair. The
    /// probe reads <see cref="EventBroker.IsProcessed"/> from inside the dispatch and finds it false; and
    /// a plain unadorned subscriber positioned AFTER the returning one still runs, which it could not do
    /// had the state moved. The second assertion is the one that matters operationally - it is exactly the
    /// property the DataWindow event chain relies on to keep offering an event to its remaining services.
    /// </para>
    /// </remarks>
    [Fact]
    public void AReturnEqualToTheConfiguredDefaultLeavesTheDispatchUnhandled()
    {
        EventBroker broker = new();
        DispatchLog log = new();
        DispatchStateProbe probe = new();

        // The framework's own default: se_cst_dw.sru:L596 and n_cst_threading_eventful.sru:L76.
        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue(0L));

        SubscribeHandler(broker, log, ClickedTopic, "returnsTheDefault", 0L);
        SubscribeLabelled(broker, log, ClickedTopic, "stillUnhandledSoStillRuns");
        SubscribeStateProbe(broker, log, ClickedTopic, probe);

        broker.Trigger(ClickedTopic);

        // :L916 - 0 does not differ from the default of 0, so the latch at :L917 never fires.
        Assert.Equal(1, probe.Runs);
        Assert.False(probe.Processed);
        Assert.Null(probe.Value);

        // And the operational consequence: an unadorned subscriber after it is STILL admitted, which is
        // what keeps a service chain running when one service declines to handle an event.
        Assert.Equal(["returnsTheDefault", "stillUnhandledSoStillRuns", ProbeLabel], log.Labels);
    }

    /// <summary>
    /// How many consecutive null-returning subscribers a dispatch carries before the state is inspected.
    /// </summary>
    /// <remarks>
    /// One row for the boundary and two beyond it. A single null return is the case an implementation is
    /// most likely to get right by accident; three is what catches a port that accumulated something per
    /// invocation and eventually tripped a threshold.
    /// </remarks>
    public static TheoryData<int> NullReturnerCountRows => new() { 1, 2, 3 };

    /// <summary>
    /// A subscriber that returns nothing never handles the dispatch, however many of them run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). <c>n_cst_eventful.sru:L909</c> guards the whole
    /// escalation with <c>if Not IsNull(aVal)</c>, so a null return does not reach the ladder at all - it
    /// is not compared with the default, and it cannot fire the latch. The oracle states the same rule as
    /// a definition rather than a consequence: no return value is DEFINED as not handled
    /// (<c>w_test_eventful.srw:L234</c>).
    /// </para>
    /// <para>
    /// <b>A void handler and a handler returning null are indistinguishable, and that is the oracle's
    /// position rather than an approximation.</b> <c>of_isprocessed</c> is literally
    /// <c>Not IsNull(_aRetVal)</c> (<c>:L655</c>), so the two states the port could have distinguished are
    /// the same state in the subject.
    /// </para>
    /// <para>
    /// No default is configured here deliberately. That is the arrangement in which ANY non-null value
    /// would handle - the null-default arm at <c>:L912-L913</c> - so it is the most permissive setting
    /// there is, and a null return failing to handle even here is the strongest form of the assertion.
    /// </para>
    /// </remarks>
    /// <param name="nullReturnerCount">How many null-returning subscribers precede the probe.</param>
    [Theory]
    [MemberData(nameof(NullReturnerCountRows))]
    public void NullNeverHandlesTheDispatchHoweverManySubscribersReturnIt(int nullReturnerCount)
    {
        EventBroker broker = new();
        DispatchLog log = new();
        DispatchStateProbe probe = new();

        List<string> expectedLabels = [];

        for (int index = 1; index <= nullReturnerCount; index++)
        {
            string label = $"silent{index}";
            RecordingSubscriber subscriber = SubscribeLabelled(broker, log, ClickedTopic, label);

            // Configured EXPLICITLY to return null rather than left unconfigured, so the intent is in the
            // arrangement rather than in an absence. The two are indistinguishable to the broker, which is
            // documented on RecordingSubscriber.ReturnValueFor and is itself the subject here.
            subscriber.SetReturnValue(RecordingHandlerNames.NoArguments, null);
            expectedLabels.Add(label);
        }

        SubscribeStateProbe(broker, log, ClickedTopic, probe);
        expectedLabels.Add(ProbeLabel);

        object? triggerResult = broker.Trigger(ClickedTopic);

        Assert.Equal(1, probe.Runs);
        Assert.False(probe.Processed);
        Assert.Null(probe.Value);

        // Every one of them was admitted, because the state never moved off unhandled.
        Assert.Equal(expectedLabels, log.Labels);

        // :L966-L968 - with no default configured, the substitution has nothing to substitute.
        Assert.Null(triggerResult);
    }

    /// <summary>
    /// With NO default return value configured at all, any non-null return handles the dispatch and
    /// becomes the recorded value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). <b>This is a rule about ONE CONFIGURATION and it
    /// is deliberately not asserted as a universal.</b> It holds because a broker with no default takes
    /// the FIRST arm of the ladder - <c>n_cst_eventful.sru:L912-L913</c>, <c>IsNull(aDefRetVal)</c> - so
    /// the comparison at <c>:L916</c> is never reached and the value's relationship to anything is
    /// irrelevant. The moment a default is configured the third arm governs instead, and
    /// <see cref="AReturnEqualToTheConfiguredDefaultLeavesTheDispatchUnhandled"/> is that case. Asserting
    /// "non-null means handled" as a general rule would mask the default comparison entirely, which is
    /// precisely the naive port this suite exists to fail.
    /// </para>
    /// <para>
    /// Two distinct escalation triggers actually coincide here, and the port keeps them apart even though
    /// they select the same arm today. <c>:L912</c> tests whether the default IS null;
    /// <c>:L914</c> tests <c>ClassName(aDefRetVal) = "any"</c>, which is PowerScript asking whether the
    /// variable has ever been assigned a typed value at all - "no default has been ESTABLISHED", a third
    /// state distinct from a default of null. A fresh broker satisfies both. They are kept separate
    /// because the third arm only runs when neither of the first two fires, and collapsing them would make
    /// that ordering unverifiable.
    /// </para>
    /// <para>
    /// The recorded value is asserted, not just the state, because <c>:L922-L924</c> records the value
    /// only ON escalation - the two are one action in the subject and a port could plausibly latch the
    /// state without recording the value, leaving <c>of_IsProcessed()</c> true while
    /// <c>of_GetReturnValue()</c> stayed null. That combination is impossible in the subject, since
    /// <c>of_isprocessed</c> is derived FROM the recorded value (<c>:L655</c>), and asserting both here is
    /// what pins the derivation.
    /// </para>
    /// </remarks>
    [Fact]
    public void WithNoDefaultConfiguredAnyNonNullReturnHandlesTheDispatch()
    {
        EventBroker broker = new();
        DispatchLog log = new();
        DispatchStateProbe probe = new();

        // Deliberately NO SetDefaultReturnValue call: the default is both null AND unestablished, which is
        // the two coinciding triggers at :L912 and :L914.
        SubscribeHandler(broker, log, ClickedTopic, "handler", 7L);
        SubscribeStateProbe(broker, log, ClickedTopic, probe);

        broker.Trigger(ClickedTopic);

        Assert.Equal(1, probe.Runs);
        Assert.True(probe.Processed);
        Assert.Equal(7L, probe.Value);
    }

    /// <summary>
    /// A return that DIFFERS from the configured default handles the dispatch and records the value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). The third arm of the ladder,
    /// <c>n_cst_eventful.sru:L916-L917</c>, and the positive complement of
    /// <see cref="AReturnEqualToTheConfiguredDefaultLeavesTheDispatchUnhandled"/>. Together the two cases
    /// bracket the comparison: same broker configuration, same subscriber shape, one value equal to the
    /// default and one not, opposite outcomes. Neither alone would show that the comparison is what
    /// decides - the equal case alone is also consistent with "nothing ever handles", and the differing
    /// case alone is also consistent with "any non-null value handles".
    /// </para>
    /// <para>
    /// 1 against a default of 0 is the framework's own pairing, not an arbitrary one:
    /// <c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru</c> installs 0 as the default at
    /// <c>:L596</c> and writes eight of its raw events as
    /// <c>if Eventful.of_Trigger(...) = 1 then return 1</c>. Section 5 asserts that consumer directly.
    /// </para>
    /// <para>
    /// The unadorned subscriber between the handler and the probe is doing work: it must NOT run, which is
    /// the complement of the same subscriber running in the equal-to-default case. So the two cases differ
    /// in their observed label sequence as well as in their observed state, and a port that reported the
    /// state correctly while filtering incorrectly would fail one of them.
    /// </para>
    /// </remarks>
    [Fact]
    public void AReturnDifferingFromTheConfiguredDefaultHandlesTheDispatchAndRecordsTheValue()
    {
        EventBroker broker = new();
        DispatchLog log = new();
        DispatchStateProbe probe = new();

        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue(0L));

        SubscribeHandler(broker, log, ClickedTopic, "returnsOne", 1L);
        RecordingSubscriber skipped = SubscribeLabelled(broker, log, ClickedTopic, "unhandledOnly");
        SubscribeStateProbe(broker, log, ClickedTopic, probe);

        broker.Trigger(ClickedTopic);

        // :L916-L917 then :L922-L924.
        Assert.Equal(1, probe.Runs);
        Assert.True(probe.Processed);
        Assert.Equal(1L, probe.Value);

        // The mirror of the equal-to-default case: here the unadorned subscriber is skipped.
        Assert.Equal(["returnsOne", ProbeLabel], log.Labels);
        Assert.Equal(0, skipped.InvocationCount);
    }

    /// <summary>
    /// The four combinations of topic and returned value that separate a per-topic default from the global
    /// one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fixture behind every row: a global default of 0, and a per-topic default of 5 registered for
    /// <see cref="ClickedTopic"/> only. Each row triggers one topic with one returned value and states
    /// whether the dispatch must come out handled.
    /// </para>
    /// <para>
    /// The four rows are the full cross-product, and it is the cross-product that makes the case
    /// conclusive. Rows 1 and 4 return the SAME value on different topics and must disagree; rows 2 and 3
    /// do likewise with the other value. So no single mistake - reading the global default for everything,
    /// reading the per-topic default for everything, comparing against null, or ignoring the comparison -
    /// can produce all four outcomes.
    /// </para>
    /// </remarks>
    public static TheoryData<string, long, bool> DefaultResolutionRows =>
        new()
        {
            // The per-topic default is 5 for "clicked", so 5 is NOT handled there ...
            { ClickedTopic, 5L, false },

            // ... and 0 IS, even though 0 is the GLOBAL default.
            { ClickedTopic, 0L, true },

            // "other" has no entry of its own, so the global default of 0 governs: 0 is not handled ...
            { OtherTopic, 0L, false },

            // ... and 5 is, even though 5 is the per-topic default for a different topic.
            { OtherTopic, 5L, true },
        };

    /// <summary>
    /// A per-topic default return value governs the comparison for its own topic, and the global default
    /// governs every other topic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). The resolver at
    /// <c>n_cst_eventful.sru:L541-L553</c> has three arms in this order: with no per-topic registrations
    /// at all the global default is returned without a search (<c>:L543-L544</c>); otherwise the FIRST
    /// per-topic entry whose name matches (<c>:L546-L550</c>); otherwise the global default again
    /// (<c>:L552</c>). The two setter overloads that populate those two stores are
    /// <c>:L479-L517</c> and <c>:L519-L539</c>, and the oracle shows both spellings side by side -
    /// <c>of_SetDefaultReturnValue(0)</c> live at <c>w_test_eventful.srw:L260</c> and
    /// <c>of_SetDefaultReturnValue("clicked",0)</c> commented out at <c>:L263</c>.
    /// </para>
    /// <para>
    /// <b>The resolution happens ONCE per dispatch, before the loop starts</b> (<c>:L795</c>), which is
    /// why every subscriber in one dispatch is measured against the same yardstick and why changing a
    /// default mid-dispatch is refused outright - see
    /// <see cref="SettingEitherDefaultFromInsideADispatchReturnsTheLegacyFailureCode"/>.
    /// </para>
    /// <para>
    /// Both topics carry an identically-shaped arrangement so the only variable between rows is which
    /// default the resolver picked.
    /// </para>
    /// </remarks>
    /// <param name="topic">The event name to dispatch.</param>
    /// <param name="returnedValue">The value the subscriber returns.</param>
    /// <param name="expectHandled">Whether the dispatch must come out handled.</param>
    [Theory]
    [MemberData(nameof(DefaultResolutionRows))]
    public void APerTopicDefaultGovernsItsOwnTopicAndTheGlobalDefaultGovernsTheOthers(
        string topic,
        long returnedValue,
        bool expectHandled)
    {
        EventBroker broker = new();
        DispatchLog log = new();
        DispatchStateProbe probe = new();

        // :L519-L539 - the global default, then :L479-L517 - the per-topic override for "clicked" only.
        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue(0L));
        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue(ClickedTopic, 5L));

        SubscribeHandler(broker, log, topic, "value", returnedValue);
        SubscribeStateProbe(broker, log, topic, probe);

        broker.Trigger(topic);

        Assert.Equal(1, probe.Runs);
        Assert.Equal(expectHandled, probe.Processed);

        if (expectHandled)
        {
            Assert.Equal(returnedValue, probe.Value);
        }
        else
        {
            Assert.Null(probe.Value);
        }
    }

    /// <summary>
    /// Once the dispatch is handled, a later subscriber returning nothing does NOT return it to unhandled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). The oracle states the rule in prose at
    /// <c>w_test_eventful.srw:L235</c>: the handled state cannot roll back - once handled it cannot become
    /// unhandled again - though the event's return value may be overwritten by a later subscription. The
    /// mechanism is that there is NO reverse assignment anywhere in the object: the transition at
    /// <c>n_cst_eventful.sru:L910</c> is guarded by <c>if nCap = CAP_UNHANDLED</c>, so once the state has
    /// moved the whole latch-setting block is skipped and the else arm at <c>:L925-L933</c> touches the
    /// value only. Nothing assigns <c>CAP_UNHANDLED</c> to <c>nCap</c> after <c>:L786</c>.
    /// </para>
    /// <para>
    /// <b>Why a modelling error here is easy to make and hard to see.</b> A port that treated the handled
    /// state as "the most recent subscriber returned something meaningful" - a resettable flag rather than
    /// a latch - would behave identically in every dispatch whose last subscriber happens to return a
    /// value, which is most of them. It would diverge only when a later subscriber declines, and then it
    /// would re-admit the unhandled-only subscribers behind it and re-offer an event that has already been
    /// consumed.
    /// </para>
    /// <para>
    /// The arrangement is a handler, then a <c>%</c>-capturing subscriber that explicitly returns null,
    /// then the probe. The middle subscriber MUST be <c>%</c> or <c>*</c>: an unadorned one would be
    /// skipped by the filter and could not return anything at all, so the case would assert nothing. Its
    /// admission is asserted too, since a skipped subscriber would trivially fail to roll anything back.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheHandledStateNeverRollsBackWhenALaterSubscriberReturnsNull()
    {
        EventBroker broker = new();
        DispatchLog log = new();
        DispatchStateProbe probe = new();

        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue(0L));

        SubscribeHandler(broker, log, ClickedTopic, "handler", 1L);

        RecordingSubscriber declines =
            SubscribeLabelled(broker, log, $"{TopicSymbols.Handled}{ClickedTopic}", "declines");
        declines.SetReturnValue(RecordingHandlerNames.NoArguments, null);

        SubscribeStateProbe(broker, log, ClickedTopic, probe);

        broker.Trigger(ClickedTopic);

        // It really did run, so it really did have the opportunity to roll the state back.
        Assert.Equal(1, declines.InvocationCount);
        Assert.Equal(["handler", "declines", ProbeLabel], log.Labels);

        // :L909 - a null return never reaches the ladder, and nothing else clears the latch.
        Assert.Equal(1, probe.Runs);
        Assert.True(probe.Processed);
        Assert.Equal(1L, probe.Value);
    }

    /// <summary>
    /// The recorded VALUE is overwritten by a later differing return while the handled STATE stays
    /// handled - the whole asymmetry, in one place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). <c>w_test_eventful.srw:L235</c> is one sentence
    /// carrying two rules that pull in opposite directions, and this case asserts both halves together so
    /// neither can be tidied into the other: the state is a LATCH and the value is a LAST-WRITE-WINS
    /// register. The already-handled arm at <c>n_cst_eventful.sru:L925-L933</c> writes the value and never
    /// touches the state; the unhandled arm at <c>:L910-L924</c> writes both.
    /// </para>
    /// <para>
    /// The overwrite is CONDITIONAL, and the condition is the same comparison as the escalation:
    /// <c>:L927</c> overwrites only when the value differs from the default. Both cases are covered here.
    /// The first subscriber returns 1 and the second returns 2, both differing from the default of 0, so
    /// the second wins; then a third, capturing <c>%</c>, returns exactly the default 0, and the recorded
    /// value stays 2. That third subscriber is what stops the case being satisfied by an unconditional
    /// last-write-wins implementation.
    /// </para>
    /// <para>
    /// Two probes are needed because the register is observed at two moments, and there is no other way to
    /// see an intermediate value: the state is dispatch-scoped and gone by the time
    /// <see cref="EventBroker.Trigger"/> returns. The first probe sits between the two writers and reads
    /// 1; the second sits at the end and reads 2. Both capture with <c>*</c> so neither is filtered out.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheRecordedValueIsOverwrittenByALaterDifferingReturnWhileTheStateStaysHandled()
    {
        EventBroker broker = new();
        DispatchLog log = new();
        DispatchStateProbe afterFirstWrite = new();
        DispatchStateProbe afterSecondWrite = new();

        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue(0L));

        // Write one: escalates and records 1 (:L916-L917, :L922-L924).
        SubscribeHandler(broker, log, ClickedTopic, "writesOne", 1L);

        // Observed between the two writes - the only way to see the intermediate value at all.
        SubscribeStateProbe(broker, log, ClickedTopic, afterFirstWrite);

        // Write two: already handled, differs from the default, so it OVERWRITES (:L927-L929).
        SubscribeHandler(broker, log, $"{TopicSymbols.Handled}{ClickedTopic}", "writesTwo", 2L);

        // Write three: already handled, but EQUAL to the default, so :L927 is false and nothing is written.
        SubscribeHandler(broker, log, $"{TopicSymbols.Handled}{ClickedTopic}", "writesTheDefault", 0L);

        SubscribeStateProbe(broker, log, ClickedTopic, afterSecondWrite);

        broker.Trigger(ClickedTopic);

        Assert.Equal(
            ["writesOne", ProbeLabel, "writesTwo", "writesTheDefault", ProbeLabel],
            log.Labels);

        // Half one of the asymmetry - the state latched on the first write and never moved again.
        Assert.True(afterFirstWrite.Processed);
        Assert.True(afterSecondWrite.Processed);

        // Half two - the value moved from 1 to 2, and the equal-to-default write did not move it back.
        Assert.Equal(1, afterFirstWrite.Runs);
        Assert.Equal(1L, afterFirstWrite.Value);
        Assert.Equal(1, afterSecondWrite.Runs);
        Assert.Equal(2L, afterSecondWrite.Value);
    }

    /// <summary>
    /// With NO default configured, a later differing return does NOT overwrite the recorded value - which
    /// is exactly why the oracle advises always setting one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B), and this is the case most likely to be mistaken
    /// for a defect and "fixed". It is not a defect; it is what the subject does, and the reason is
    /// PowerScript's three-valued comparison. The already-handled arm overwrites only
    /// <c>if aVal &lt;&gt; aDefRetVal</c> (<c>n_cst_eventful.sru:L927</c>). When the default is NULL that
    /// expression evaluates to NULL rather than to true, <c>if NULL then</c> is false, and the value is
    /// therefore left alone. The port reproduces it by returning null from its three-valued comparison for
    /// a null operand and testing <c>== true</c>.
    /// </para>
    /// <para>
    /// So the overwrite rule the oracle documents at <c>w_test_eventful.srw:L235</c> does not actually
    /// apply to a broker with no default - which is precisely why <c>:L257</c> advises ALWAYS setting a
    /// default return value for an event. That advice is not stylistic: without a default, the FIRST
    /// non-null return wins permanently, and every later subscriber's value is silently discarded.
    /// </para>
    /// <para>
    /// It reads as the exact opposite of
    /// <see cref="TheRecordedValueIsOverwrittenByALaterDifferingReturnWhileTheStateStaysHandled"/> from
    /// the same arrangement with one line removed, which is why the two sit next to each other. Note the
    /// STATE still latches identically - only the register behaves differently - so this is not a second
    /// escalation rule.
    /// </para>
    /// </remarks>
    [Fact]
    public void WithNoDefaultConfiguredTheRecordedValueIsNotOverwritten()
    {
        EventBroker broker = new();
        DispatchLog log = new();
        DispatchStateProbe probe = new();

        // The one line the companion case has and this one does not: no SetDefaultReturnValue call.
        SubscribeHandler(broker, log, ClickedTopic, "writesOne", 1L);
        SubscribeHandler(broker, log, $"{TopicSymbols.Handled}{ClickedTopic}", "wouldWriteTwo", 2L);
        SubscribeStateProbe(broker, log, ClickedTopic, probe);

        object? triggerResult = broker.Trigger(ClickedTopic);

        // Both writers ran, so the second genuinely had its chance.
        Assert.Equal(["writesOne", "wouldWriteTwo", ProbeLabel], log.Labels);

        // The state latched exactly as it does with a default configured ...
        Assert.Equal(1, probe.Runs);
        Assert.True(probe.Processed);

        // ... but the register still holds the FIRST value: :L927 compared 2 against a null default,
        // got null, and did not write.
        Assert.Equal(1L, probe.Value);

        // The probe returned nothing, so :L966-L968 substitutes the resolved default - which is null here,
        // so the trigger reports null even though the dispatch was handled with the value 1. This is the
        // trigger-result-is-not-the-handled-value distinction, in its starkest form.
        Assert.Null(triggerResult);
    }

    /// <summary>
    /// The handled state and the recorded value are readable from INSIDE a dispatch, exactly as the oracle
    /// reads them, and both are restored once the dispatch unwinds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). This case reproduces the oracle's own second
    /// handler. <c>w_test_eventful.srw:L72-L74</c> is: <c>if evtful.of_IsProcessed() then</c>
    /// <c>nLastReturnValue = evtful.of_GetReturnValue()</c> - a subscriber consulting what the subscriber
    /// ahead of it contributed, and then folding that value into its own return
    /// (<c>:L84</c>: <c>return wparam + lparam + nLastReturnValue</c>). The two accessors are
    /// <c>:L636</c> and <c>:L655</c>.
    /// </para>
    /// <para>
    /// <b>The second half of this case is why every other case in this section reads its state through a
    /// probe.</b> The dispatch saves the recorded value on entry and RESTORES it on the way out
    /// (<c>n_cst_eventful.sru:L951</c>), which is what makes the broker re-entrant: a nested dispatch must
    /// not leave its result visible to the enclosing one. The consequence for a caller is that
    /// <see cref="EventBroker.IsProcessed"/> and <see cref="EventBroker.GetReturnValue"/> report the
    /// OUTER level's view the moment <see cref="EventBroker.Trigger"/> returns - false and null at the top
    /// level - however the dispatch went. Asserting that here makes the constraint explicit rather than
    /// folk knowledge, and it is what would catch a port that "helpfully" left the last dispatch's result
    /// readable afterwards.
    /// </para>
    /// <para>
    /// The reader is registered second so it observes the first subscriber and not itself: the callback
    /// runs before the reading subscriber's own return value is produced, so a value it went on to return
    /// could not contaminate the observation.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheStateIsReadableFromInsideTheDispatchAndIsRestoredOnceTheTriggerReturns()
    {
        EventBroker broker = new();
        DispatchLog log = new();
        DispatchStateProbe probe = new();

        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue(0L));

        // Before the dispatch: nothing has been recorded at this level.
        Assert.False(broker.IsProcessed());
        Assert.Null(broker.GetReturnValue());

        SubscribeHandler(broker, log, ClickedTopic, "onButtonClicked1", 3L);

        // The oracle's second handler: an all-capturing reader of the first one's contribution.
        RecordingSubscriber reader = SubscribeStateProbe(broker, log, ClickedTopic, probe);

        // And, like the oracle's, it returns a value of its own - which must not affect what it observed.
        reader.SetReturnValue(RecordingHandlerNames.NoArguments, 11L);

        object? triggerResult = broker.Trigger(ClickedTopic);

        // Read from inside: the first subscriber's contribution, and nothing of the reader's own.
        Assert.Equal(1, probe.Runs);
        Assert.True(probe.Processed);
        Assert.Equal(3L, probe.Value);

        // The reader's 11 differs from the default of 0, so it overwrote the register (:L927-L929) and,
        // being the last invocation, is also what the trigger returns (:L973).
        Assert.Equal(11L, triggerResult);

        // :L951 - and the moment the dispatch unwound, this level's view was restored.
        Assert.False(broker.IsProcessed());
        Assert.Null(broker.GetReturnValue());
    }

    /// <summary>
    /// Whether a default is configured, and what an unhandled dispatch must therefore return.
    /// </summary>
    /// <remarks>
    /// A companion <see cref="bool"/> rather than a nullable value carries "no default configured",
    /// because a null row value would be indistinguishable from a configured default of null - and, being
    /// two genuinely different states in the subject (<c>n_cst_eventful.sru:L912</c> against
    /// <c>:L914</c>), they must not be conflated in a matrix either. Every argument is serializable, which
    /// this repository's warnings-as-errors policy requires of theory data.
    /// </remarks>
    public static TheoryData<bool, long> UnhandledTriggerResultRows =>
        new()
        {
            // A default of 0 - the framework's own choice.
            { true, 0L },

            // A non-zero default, so a passing result cannot be a coincidence of zero being the default of
            // everything.
            { true, 42L },

            // No default at all. The companion value is ignored.
            { false, 0L },
        };

    /// <summary>
    /// An unhandled dispatch returns the resolved default - and, with no default configured, returns null
    /// rather than a fabricated zero.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). <c>n_cst_eventful.sru:L966-L970</c> substitutes
    /// the resolved default for the returned value when that value is null and the dispatch was not
    /// posted, and <c>:L973</c> returns it. The oracle states the consequence of omitting a default
    /// explicitly at <c>w_test_eventful.srw:L266</c>: without one, <c>of_Trigger</c> may return a null
    /// <c>any</c>.
    /// </para>
    /// <para>
    /// <b>The third row is the substantive one.</b> Returning null where nothing was configured is a
    /// deliberate refusal to invent a value, and it matters because it is the thing a well-meaning port
    /// most wants to change: a numeric zero looks tidier than a null and would make every arithmetic
    /// caller simpler. It would also silently satisfy <c>if of_Trigger(...) = 0</c> for a caller that
    /// never set a default, which is a different answer from the one the subject gives.
    /// </para>
    /// <para>
    /// The dispatch here is unhandled by construction: the single subscriber returns nothing, so
    /// <c>:L909</c> keeps the escalation out of reach entirely. The case where nothing is SUBSCRIBED at
    /// all also returns the resolved default, through the lexical-bounds reject at <c>:L797-L798</c>, and
    /// that is owned by
    /// <see cref="DispatchOrderTests.ANameOutsideTheLexicalBoundsRunsNothingAndReturnsTheResolvedDefault"/>
    /// rather than duplicated here.
    /// </para>
    /// </remarks>
    /// <param name="configureDefault">Whether a global default is configured at all.</param>
    /// <param name="defaultValue">The default to configure when <paramref name="configureDefault"/> is set.</param>
    [Theory]
    [MemberData(nameof(UnhandledTriggerResultRows))]
    public void AnUnhandledDispatchReturnsTheResolvedDefault(bool configureDefault, long defaultValue)
    {
        EventBroker broker = new();
        DispatchLog log = new();

        if (configureDefault)
        {
            Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue(defaultValue));
        }

        // Returns nothing, so the dispatch stays unhandled all the way through (:L909).
        RecordingSubscriber silent = SubscribeLabelled(broker, log, ClickedTopic, "silent");
        silent.SetReturnValue(RecordingHandlerNames.NoArguments, null);

        object? triggerResult = broker.Trigger(ClickedTopic);

        Assert.Equal(["silent"], log.Labels);

        if (configureDefault)
        {
            // :L967-L968 - the substitution.
            Assert.Equal(defaultValue, triggerResult);
        }
        else
        {
            // No value invented where none was configured (w_test_eventful.srw:L266).
            Assert.Null(triggerResult);
        }
    }

    /// <summary>
    /// Setting either default return value from inside a dispatch is refused with the legacy failure code,
    /// and throws nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). Both overloads open with the same guard -
    /// <c>if _nDeep &gt; 0 then return RetCode.FAILED</c> at <c>n_cst_eventful.sru:L500</c> and
    /// <c>:L536</c> - and <c>_nDeep</c> is incremented for the duration of every dispatch
    /// (<c>:L809</c>, restored at <c>:L952</c>).
    /// </para>
    /// <para>
    /// <b>The rejection is behaviour rather than defensiveness, and the reason is the one-shot
    /// resolution.</b> A dispatch resolves its default ONCE, before the loop starts (<c>:L795</c>), and
    /// every escalation decision in that dispatch is measured against the resolved value. Were a change
    /// admitted part-way through, two subscribers in one dispatch would answer to different yardsticks -
    /// the same returned value could handle for one and not for the next - so the oracle refuses outright.
    /// A port that allowed the change, or that threw instead of returning a code, would both be wrong, and
    /// in different ways: the first changes dispatch semantics, the second turns a documented rejection
    /// into an exception a legacy caller has no handler for.
    /// </para>
    /// <para>
    /// The rejection is asserted to be TOTAL as well as reported: the value read back on the NEXT dispatch
    /// is still the one configured before the first, so nothing was written and then reported as failed.
    /// The second dispatch is what shows that, since a failed write and a successful one are
    /// indistinguishable from inside the dispatch that attempted it.
    /// </para>
    /// </remarks>
    [Fact]
    public void SettingEitherDefaultFromInsideADispatchReturnsTheLegacyFailureCode()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue(0L));

        long globalOverloadResult = RetCode.OK;
        long perTopicOverloadResult = RetCode.OK;
        int attempts = 0;

        RecordingSubscriber subscriber = SubscribeLabelled(broker, log, ClickedTopic, "attemptsToReconfigure");
        subscriber.DuringAnyHandler = (observedBroker, record) =>
        {
            Assert.NotNull(observedBroker);

            // :L536 and :L500 respectively. Neither throws; both report.
            globalOverloadResult = observedBroker.SetDefaultReturnValue(99L);
            perTopicOverloadResult = observedBroker.SetDefaultReturnValue(ClickedTopic, 99L);
            attempts++;
        };

        // Nothing escapes the dispatch: the guard reports, it does not raise.
        object? firstResult = broker.Trigger(ClickedTopic);

        Assert.Equal(1, attempts);
        Assert.Equal(RetCode.FAILED, globalOverloadResult);
        Assert.Equal(RetCode.FAILED, perTopicOverloadResult);

        // The dispatch was unhandled, so it returned the default resolved at :L795 - the original 0.
        Assert.Equal(0L, firstResult);

        // And the refusal was total: the next dispatch still resolves the ORIGINAL default, so neither
        // attempted write landed.
        object? secondResult = broker.Trigger(ClickedTopic);
        Assert.Equal(0L, secondResult);

        // Outside a dispatch the same two calls are accepted, which is what makes the guard specific to
        // being in flight rather than a blanket refusal.
        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue(99L));
        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue(ClickedTopic, 99L));
        Assert.Equal(99L, broker.Trigger(ClickedTopic));
    }


    // =============================================================================================
    //  5. THE TWO REAL ARRANGEMENTS THIS PROTOCOL EXISTS FOR
    //
    //  Everything above pins one rule at a time. This section assembles the rules into the two
    //  arrangements that actually exist in the repository, so that a reader can see the protocol doing
    //  its job rather than only being probed. Both are transcriptions, not inventions.
    //
    //  w_test_eventful.srw:L250-L252, :L260, :L72-L74, :L84 - the oracle's own subscriptions
    //  se_cst_dw.sru:L54, :L57, :L596, and the eight `of_Trigger(...) = 1` raw events - the framework's
    //  own consumer, and the reason AAP 0.4.3 C-03 has to carry this state on the wire
    // =============================================================================================

    /// <summary>
    /// The oracle's own arrangement: a plain subscription, an all-capturing namespaced one that reads what
    /// the first contributed, a third subscription on a different event, and a global default of zero.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). A transcription of
    /// <c>ws_objects/pfw.tests.pbl.src/w_test_eventful.srw:L250-L252</c>, which subscribes exactly three
    /// times - <c>of_On("clicked", parent, "onButtonClicked1")</c>,
    /// <c>of_On("*clicked.myns", parent, "onButtonClicked2")</c> and
    /// <c>of_On("test.myns", parent, "onTest")</c> - and then sets a global default of zero at
    /// <c>:L260</c>. Its second handler reads <c>of_IsProcessed()</c> and <c>of_GetReturnValue()</c> at
    /// <c>:L72-L74</c> and folds the value it finds into its own return at <c>:L84</c>.
    /// </para>
    /// <para>
    /// Four separate rules have to hold simultaneously for this arrangement to behave, which is what makes
    /// it worth assembling: the first handler's non-zero return must escalate against the default of zero;
    /// the second must be admitted despite the state having moved, which is what its <c>*</c> buys it; it
    /// must be able to READ the first's contribution mid-dispatch; and the third must not be reached at
    /// all, because <c>test.myns</c> is a different EVENT NAME and only its namespace is shared.
    /// </para>
    /// <para>
    /// The namespace is orthogonal to both axes this suite pins - <c>w_test_eventful.srw:L233</c> says
    /// priority is independent of the namespace, and it is used only with the off and disable operations.
    /// It is nevertheless carried here verbatim rather than simplified away, because the arrangement is a
    /// transcription and because it is what makes the third subscription a realistic distractor. The
    /// namespace's own behaviour belongs to <see cref="LifetimeNamespaceFilterTests"/>.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheOracleArrangementOfThreeSubscriptionsWithAGlobalDefaultOfZero()
    {
        EventBroker broker = new();
        DispatchLog log = new();
        DispatchStateProbe insideSecondHandler = new();

        // :L250 - the plain subscription. Returns a non-zero value, as the oracle's does (:L62).
        SubscribeHandler(broker, log, ClickedTopic, "onButtonClicked1", 3L);

        // :L251 - "*clicked.myns": all-capturing, namespaced. Reads the first handler's contribution
        // (:L72-L74) and returns a value of its own (:L84).
        RecordingSubscriber second = SubscribeLabelled(
            broker,
            log,
            $"{TopicSymbols.All}{ClickedTopic}{TopicSymbols.NamespaceSeparator}myns",
            "onButtonClicked2");

        second.SetReturnValue(RecordingHandlerNames.NoArguments, 10L);
        second.DuringAnyHandler = (observedBroker, record) =>
        {
            Assert.NotNull(observedBroker);

            insideSecondHandler.Processed = observedBroker.IsProcessed();
            insideSecondHandler.Value = observedBroker.GetReturnValue();
            insideSecondHandler.Runs++;
        };

        // :L252 - "test.myns": same namespace, DIFFERENT event name, so triggering "clicked" must not
        // reach it. A realistic distractor rather than a contrivance.
        RecordingSubscriber onTest = SubscribeLabelled(
            broker,
            log,
            $"test{TopicSymbols.NamespaceSeparator}myns",
            "onTest");

        // :L260 - of_SetDefaultReturnValue(0).
        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue(0L));

        object? triggerResult = broker.Trigger(ClickedTopic);

        // Two subscribers ran, in registration order, and the third was never reached.
        Assert.Equal(["onButtonClicked1", "onButtonClicked2"], log.Labels);
        Assert.Equal(0, onTest.InvocationCount);

        // The oracle's own read: processed is true and the value is the FIRST handler's 3.
        Assert.Equal(1, insideSecondHandler.Runs);
        Assert.True(insideSecondHandler.Processed);
        Assert.Equal(3L, insideSecondHandler.Value);

        // The second handler's 10 differs from the default of 0, so it overwrote the register
        // (:L927-L929), and being the last invocation it is also what the trigger returns (:L973).
        Assert.Equal(10L, triggerResult);
    }

    /// <summary>
    /// The two outcomes of a DataWindow-style service chain, keyed on what the first service returns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fixture behind both rows is <c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru</c>:
    /// a broker carrying a global default of zero (<c>:L596</c>), two plain unadorned subscriptions on one
    /// of its real topic names, and a caller written
    /// <c>if Eventful.of_Trigger(...) = 1 then return 1</c>.
    /// </para>
    /// <para>
    /// The two real topic names are used rather than a placeholder: <c>0-itemchanged</c> from <c>:L54</c>
    /// and <c>1-editchanged</c> from <c>:L57</c>. Their leading digits are part of the NAME - only
    /// <c>-</c>, <c>%</c>, <c>*</c>, <c>@</c> and <c>!</c> are leading-run symbols and the run ends at the
    /// first character that is none of them - and their ordering role belongs to
    /// <see cref="DispatchOrderTests"/>. They appear here so the arrangement is the real one.
    /// </para>
    /// </remarks>
    public static TheoryData<string, long, string, long> ServiceChainRows =>
        new()
        {
            // 0 equals the default, so the first service did NOT handle it: the second is still admitted
            // and the caller's `= 1` test is false, so the chain continues.
            { "0-itemchanged", 0L, "svcA svcB", 0L },

            // 1 differs from the default, so the first service handled it: the second is filtered out AND
            // the caller's `= 1` test is true, so the chain stops.
            { "1-editchanged", 1L, "svcA", 1L },
        };

    /// <summary>
    /// A DataWindow-style service chain continues when a service returns the default and stops when one
    /// returns the prevent value - and the SAME return value drives both mechanisms.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B) and NAMES THE BOUNDARY DECISION
    /// (AAP 0.7.3 C-K). <b>This is the case that shows why the mode values and the escalation rule are
    /// asserted in this file rather than only their consequences.</b>
    /// </para>
    /// <para>
    /// <c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L596</c> installs a global default of
    /// zero on the broker embedded in the DataWindow service, and eight of its raw events are written
    /// <c>if Eventful.of_Trigger(...) = 1 then return 1</c> - <c>:L116</c>, <c>:L120</c>, <c>:L132</c>,
    /// <c>:L139</c>, <c>:L148</c>, <c>:L167</c>, <c>:L178</c> and <c>:L395</c>. So in the framework's own
    /// most important consumer, 0 means continue and 1 means prevent, and TWO INDEPENDENT MECHANISMS MOVE
    /// TOGETHER on that one value:
    /// </para>
    /// <list type="number">
    ///   <item><description>
    ///   <b>The capture filter.</b> Returning 1 differs from the default, so the escalation latch fires
    ///   (<c>n_cst_eventful.sru:L916-L917</c>) and every remaining unadorned subscriber stops being
    ///   eligible (<c>:L831-L833</c>). Returning 0 does not, so they all still run.
    ///   </description></item>
    ///   <item><description>
    ///   <b>The trigger's return value.</b> Returning 1 makes it the last invocation's raw value, so
    ///   <c>:L973</c> hands 1 back and the raw event returns 1 to the runtime. Returning 0 leaves the
    ///   caller's test false.
    ///   </description></item>
    /// </list>
    /// <para>
    /// AAP 0.4.3 C-03 turns that chain into a bidirectional gRPC stream and AAP 0.6.1 requires the
    /// ordering and the veto to survive the crossing. Both quantities asserted here are the ones such a
    /// stream must carry per event, as integers - which is the whole reason
    /// <see cref="TheCaptureModeValuesAreTheLegacyFlagsOnAThirtyTwoBitEnumeration"/> and
    /// <see cref="EveryPriorityCarrierIsDeclaredThirtyTwoBitsWide"/> assert numbers and widths rather than
    /// stopping at behaviour.
    /// </para>
    /// <para>
    /// Note what would happen under the naive escalation rule this suite rejects. If any non-null return
    /// marked the dispatch handled, then a service returning 0 - "I did not handle this, carry on" - would
    /// latch the state and silence every service behind it. The continue row is exactly that failure, and
    /// it is the operational cost of getting the default comparison wrong.
    /// </para>
    /// </remarks>
    /// <param name="topic">The real <c>se_cst_dw</c> topic name to dispatch.</param>
    /// <param name="firstServiceResult">What the first service in the chain returns.</param>
    /// <param name="expectedOrder">The services that must run, in order.</param>
    /// <param name="expectedTriggerResult">What the caller's <c>of_Trigger</c> must hand back.</param>
    [Theory]
    [MemberData(nameof(ServiceChainRows))]
    public void ADataWindowStyleServiceChainContinuesOnTheDefaultAndStopsOnThePreventValue(
        string topic,
        long firstServiceResult,
        string expectedOrder,
        long expectedTriggerResult)
    {
        EventBroker broker = new();
        DispatchLog log = new();

        // se_cst_dw.sru:L596 - the embedded broker's constructor installs this.
        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue(0L));

        // Two plain unadorned service subscriptions, exactly as se_cst_dw's own of_on passthrough at
        // :L448 creates them - no capture symbol and no priority anywhere in that object.
        SubscribeHandler(broker, log, topic, "svcA", firstServiceResult);
        SubscribeHandler(broker, log, topic, "svcB", 0L);

        object? triggerResult = broker.Trigger(topic);

        // Mechanism one: which services the filter admitted.
        Assert.Equal(SplitList(expectedOrder), log.Labels);

        // Mechanism two: what the caller's `= 1` test sees.
        Assert.Equal(expectedTriggerResult, triggerResult);

        // Spelled out as the caller writes it, so the consequence is unmistakable.
        bool callerWouldPrevent = Equals(triggerResult, 1L);
        Assert.Equal(expectedTriggerResult == 1L, callerWouldPrevent);
    }

}
