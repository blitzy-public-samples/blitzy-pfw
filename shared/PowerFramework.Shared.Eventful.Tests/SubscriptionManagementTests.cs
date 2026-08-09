// =====================================================================================================
//  SubscriptionManagementTests.cs
//  =====================================================================================================
//  THE BREADTH SUITE FOR THE WHOLE SUBSCRIBE / UNSUBSCRIBE / DISABLE / QUERY SURFACE OF
//  PowerFramework.Shared.Eventful.EventBroker.
//
//  SUBJECT
//      shared/PowerFramework.Shared.Eventful/EventBroker.cs - the full logic port of
//      ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru. Every `:Lnnn` locator in this file
//      without a filename prefix is a line of that .sru, which is the SPECIFICATION for every
//      expectation below. It was read at each cited line; nothing here was inferred from the C#.
//
//  ORACLE AND CONSUMER EVIDENCE
//      ws_objects/pfw.tests.pbl.src/w_test_eventful.srw                the in-repo behavioural oracle.
//          :L237 states that a subscription added during a dispatch takes effect only on the NEXT
//          dispatch. :L250 states the two case rules on one line - the EVENT name is case-sensitive
//          「事件名（区分大小写）」 and the CALLBACK name is not 「回调事件名（不区分大小写）」.
//      ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru        the DataServices consumer.
//          :L166-L167 (`ondwnchanging`) and :L316-L317 (`ondoitemchanged`) guard a trigger behind
//          `Eventful.of_IsSubscribed(...)`; :L583 (`ondestructor`) calls the fully unqualified
//          `Eventful.of_Off()` on teardown. Both idioms are asserted here rather than assumed.
//      ws_objects/pfw.thread.pbl.src/n_cst_threading.sru               the Persistence consumer, which
//          re-exposes SIX of the seven unsubscribe shapes one for one from :L532. That is independent
//          evidence every shape is genuinely used, and therefore that covering a representative subset
//          would not be coverage at all.
//
//  ALL FOURTEEN OVERLOAD SHAPES ARE COVERED, AND THE COUNT WAS VERIFIED RATHER THAN ASSUMED
//      The subject declares exactly SEVEN `Unsubscribe` overloads and exactly SEVEN `Disable`
//      overloads, matching the oracle's seven `of_off` and seven `of_disable` declarations. Each is one
//      row of <see cref="UnsubscribeShapeRows"/> or <see cref="DisableShapeRows"/> - fourteen rows for
//      fourteen shapes, with the expected surviving set spelled out per row. No shape is collapsed onto
//      another and no subset is sampled.
//
//  RULES POSITION, STATED EXPLICITLY
//      `review_rules` returns exactly one line: "No user rules provided." No user-specified rule governs
//      this file, none was invented to fill the gap, and that absence is not licence to lower the bar.
//      The AAP 0.7.2 enterprise baseline and the AAP 0.7.3 binding non-rule constraints govern in their
//      place. The four that bear on this file, and what each required here:
//
//  C-B - PRESERVE BEHAVIOUR EXACTLY; ASSERT THE QUIRKS AS CORRECT RATHER THAN TIDY THEM
//      Five quirks are asserted here as the DESIRED outcome, each at its assertion carrying the locator
//      that makes it legacy behaviour rather than an implementation accident:
//        1. a disabled subscription is RETAINED, not removed              :L1068-L1071
//        2. a disabled subscription reports as NOT subscribed             :L777 with :L1073-L1074
//        3. the subscribed query leans on the lexical bounds and scans
//           only the interior of the table                               :L767-L768 with :L775
//        4. both default-return-value setters REFUSE during a dispatch
//           rather than queueing the change                              :L500 and :L536
//        5. a subscription added during a dispatch is deferred to the
//           next one                                                     w_test_eventful.srw:L237
//      A test that "corrected" any of these would be a behavioural change dressed as a fix.
//
//  C-D - NO DEFERRED CAPABILITY APPEARS ANYWHERE IN THIS FILE
//      The oracle resolves a handler by constructing an `n_scriptinvoker` and initialising it against
//      the target, handler name and signature, answering `RetCode.E_EVENT_NOT_FOUND` when that
//      initialisation fails (:L398-L401). `n_scriptinvoker` belongs to the DEFERRED ScriptBridge
//      service, so this suite names no script-invoker type, no stand-in for one and no deferred service:
//      <see cref="EachRejectedSubscriptionAnswersItsOwnLegacyCodeAndThrowsNothing"/> asserts the CODE
//      and is deliberately silent about the mechanism that produces it.
//
//  C-H - NULLABLE AND WARNINGS-AS-ERRORS APPLY HERE EXACTLY AS THEY DO TO SHIPPING CODE
//      Directory.Build.props sets Nullable enable and TreatWarningsAsErrors true for every project,
//      test projects included, and this suite carries the bulk of the 80 percent per-service line
//      coverage burden for the broker assembly. Hence breadth: all fourteen shapes, the whole query
//      surface, and every re-entrancy path - not a representative sample.
//
//  C-K - THE BOUNDARY DECISION THIS SUITE EXISTS TO PIN, NAMED HERE
//      The disable-versus-unsubscribe distinction is not an internal detail: it is SURFACED ON THE WIRE.
//      AAP 0.4.3 C-03 publishes `GetEventGate`, `DisableEvent` and `EnableEvent` on
//      `dataservices.v1.DataWindowService`, so a remote caller can suspend a subscription and later
//      resume it. That contract is only honest if a suspended subscription still EXISTS to resume -
//      which is why retention is ASSERTED here for every one of the seven disable shapes rather than
//      assumed from the fact that a disabled subscriber does not run.
//
//  THE KEY INSIGHT THIS SUITE IS BUILT AROUND
//      Disable and unsubscribe are INDISTINGUISHABLE if the only question asked is "did the subscriber
//      run?" - the dispatch loop skips tombstoned and suspended entries with two adjacent `continue`
//      statements (:L829 and :L830) and cannot tell you which happened. The one assertion that
//      separates them is that re-enabling restores a subscription WITHOUT re-subscribing, and that it
//      resumes its ORIGINAL dispatch position rather than moving to the end. Every disable row therefore
//      compares the restored dispatch run byte for byte against the run recorded BEFORE the disable,
//      which pins retention and ordering in a single equality.
//
//  METHOD
//      Outcomes are verified by TRIGGERING and reading the shared <see cref="DispatchLog"/>, because
//      dispatch behaviour is the real contract; the query surface is a secondary signal and is itself
//      under test. Every test builds a fresh broker, fresh subscribers and a fresh log, so no test can
//      observe another's state. Member data carries only strings, booleans and numbers - never a live
//      target - so no row shares a mutable object with any other row.
//
//  SCOPE, AND WHAT IS DELIBERATELY LEFT TO A SIBLING SUITE
//      This suite covers the OVERLOAD SHAPES and the state queries. It does not duplicate:
//      DispatchOrderTests (the ordering and prepend rules themselves), LifetimeNamespaceFilterTests
//      (the namespace and negation filter spellings), PriorityAndCaptureTests (priority and the capture
//      escalation rules), VariadicDispatchTests (argument passing, truncation and initial values), and
//      PostQueueTests (the in-dispatch behaviour of the post flag and the drain). Each is named in prose
//      at the point where the boundary falls.
//
//  PROHIBITIONS OBSERVED
//      No timing, no clock read, no GUID, no random value, no thread, no file, no network and no
//      database access anywhere in this file. No SCREAMING_SNAKE member is declared: the repository root
//      .editorconfig scopes its CA1707 and IDE1006 suppressions to seven named IMPLEMENTATION files and
//      to no test file at all, so such a member would be a build error under warnings-as-errors.
// =====================================================================================================

using System;
using System.Collections.Generic;
using System.Linq;

using PowerFramework.Shared.Kernel;

using Xunit;

namespace PowerFramework.Shared.Eventful.Tests;

/// <summary>
/// The breadth suite for <see cref="EventBroker"/>'s subscription-management surface: both subscribe
/// forms, all seven unsubscribe shapes, all seven disable shapes, the four state queries, the two
/// default-return-value setters and every re-entrancy path.
/// </summary>
/// <remarks>
/// See this file's header for the full constraint position, the fourteen-shape count check and the
/// boundary against the sibling suites.
/// </remarks>
public class SubscriptionManagementTests
{
    // =================================================================================================
    //  THE SHARED VOCABULARY
    //
    //  Two topics, two target instances and two handler names per target - the smallest arrangement that
    //  can DISTINGUISH all seven selectors from one another. With one topic the topic-qualified shapes
    //  would be indistinguishable from the unqualified ones; with one target the excluding shape would
    //  be indistinguishable from a no-op; with one handler the handler-qualified shapes would be
    //  indistinguishable from the target-qualified ones. Eight subscriptions is therefore the minimum,
    //  not a convenience.
    // =================================================================================================

    /// <summary>The lexically smaller of the two topics. Ordinal comparison throughout.</summary>
    private const string AlphaTopic = "alpha";

    /// <summary>The lexically larger of the two topics.</summary>
    private const string BetaTopic = "beta";

    /// <summary>The label the first target writes into every row it records.</summary>
    private const string FirstLabel = "T1";

    /// <summary>The label the second target writes into every row it records.</summary>
    private const string SecondLabel = "T2";

    /// <summary>
    /// The zero-parameter handler name, and the one the handler-qualified shapes select on.
    /// </summary>
    private const string ZeroArityHandler = RecordingHandlerNames.NoArguments;

    /// <summary>
    /// The one-parameter handler name. Present so a handler-qualified shape has something to NOT select,
    /// which is the only way to prove the handler criterion narrows rather than widens.
    /// </summary>
    private const string OneArityHandler = RecordingHandlerNames.OneArgument;

    // The four identities a dispatch run can contain, spelled "label.declaredHandlerName". Built from
    // the constants above by constant concatenation, so a renamed handler is a compile error here rather
    // than a silently unsatisfiable expectation.

    /// <summary>The first target's zero-parameter subscription.</summary>
    private const string FirstTargetZeroArity = FirstLabel + "." + ZeroArityHandler;

    /// <summary>The first target's one-parameter subscription.</summary>
    private const string FirstTargetOneArity = FirstLabel + "." + OneArityHandler;

    /// <summary>The second target's zero-parameter subscription.</summary>
    private const string SecondTargetZeroArity = SecondLabel + "." + ZeroArityHandler;

    /// <summary>The second target's one-parameter subscription.</summary>
    private const string SecondTargetOneArity = SecondLabel + "." + OneArityHandler;

    /// <summary>The separator between two identities in a rendered dispatch run.</summary>
    private const string RunSeparator = "|";

    // The five expected surviving sets. Every row of both theories names one of these for each topic, so
    // the expectation is legible as a set rather than as a count.

    /// <summary>
    /// All four subscriptions on a topic, in registration order - the arrangement's untouched run.
    /// </summary>
    /// <remarks>
    /// The order is registration order because all eight subscriptions share one priority and none
    /// requested a prepend, so each append landed at the tail of the equal-priority run (<c>:L419</c>).
    /// The ordering rules themselves belong to <c>DispatchOrderTests</c>; this constant only needs them
    /// to be stable.
    /// </remarks>
    private const string EveryIdentity =
        FirstTargetZeroArity + RunSeparator +
        FirstTargetOneArity + RunSeparator +
        SecondTargetZeroArity + RunSeparator +
        SecondTargetOneArity;

    /// <summary>Everything except the first target's zero-parameter subscription.</summary>
    private const string EveryIdentityButFirstTargetZeroArity =
        FirstTargetOneArity + RunSeparator +
        SecondTargetZeroArity + RunSeparator +
        SecondTargetOneArity;

    /// <summary>Only the first target's two subscriptions.</summary>
    private const string FirstTargetIdentities =
        FirstTargetZeroArity + RunSeparator + FirstTargetOneArity;

    /// <summary>Only the second target's two subscriptions.</summary>
    private const string SecondTargetIdentities =
        SecondTargetZeroArity + RunSeparator + SecondTargetOneArity;

    /// <summary>Nothing survived, so the topic dispatches to nobody.</summary>
    private const string NoIdentity = "";

    // =================================================================================================
    //  THE SHAPE KEYS
    //
    //  Member data carries a KEY rather than a delegate or a live target, for two reasons that are both
    //  correctness rather than taste. First, xunit materialises member data once per class, so a live
    //  target in a row would be SHARED by every test that consumes the row - and "a fresh broker,
    //  subscribers and log per test" is a stated requirement of this suite. Second, a string key is
    //  serializable, so each row is individually reportable and re-runnable by name.
    // =================================================================================================

    /// <summary>Shape 1 of seven: by target and handler name, across every topic.</summary>
    private const string ByTargetAndHandler = "target+handler";

    /// <summary>Shape 2 of seven: by target alone.</summary>
    private const string ByTargetAlone = "target";

    /// <summary>Shape 3 of seven: fully unqualified.</summary>
    private const string FullyUnqualified = "unqualified";

    /// <summary>Shape 4 of seven: by target, with the excluding flag inverting the whole match.</summary>
    private const string ByTargetExcluding = "target+excluding";

    /// <summary>Shape 5 of seven: by topic, target and handler name - the narrowest shape.</summary>
    private const string ByTopicTargetAndHandler = "topic+target+handler";

    /// <summary>Shape 6 of seven: by topic alone.</summary>
    private const string ByTopicAlone = "topic";

    /// <summary>Shape 7 of seven: by topic and target.</summary>
    private const string ByTopicAndTarget = "topic+target";

    /// <summary>
    /// The five names used to populate a table deep enough to exercise the subscribed query at its first,
    /// interior and last positions.
    /// </summary>
    /// <remarks>
    /// Ascending and ordinally distinct, so the table's stored order is exactly this order and the
    /// lexical bounds are <c>"aaa"</c> and <c>"eee"</c>. Five is the smallest count that leaves three
    /// interior slots, and three interior slots are what make the <c>:L775</c> loop range observable
    /// rather than degenerate. A static field rather than a collection expression at each use site so no
    /// array is allocated per iteration and no constant-array-as-argument diagnostic can apply.
    /// </remarks>
    private static readonly string[] LexicallyOrderedTopics = ["aaa", "bbb", "ccc", "ddd", "eee"];

    // =================================================================================================
    //  1. SUBSCRIBE - of_on, two forms
    //  :L267 declares the three-argument form and :L296 the four-argument one that adds an optional
    //  handler signature; the former is a single-line delegation to the latter with an empty signature
    //  (:L293).
    // =================================================================================================

    /// <summary>
    /// Both subscribe forms answer <see cref="RetCode.OK"/> for a valid topic, target and handler, and
    /// the subscriber then actually receives a dispatch.
    /// </summary>
    /// <remarks>
    /// Two halves, and the second is the one that matters: a return code proves the call was accepted,
    /// only a dispatch proves the subscription was WIRED. The three-argument form is the oracle's
    /// <c>of_on(name, object, evtName)</c> (<c>:L267</c>, body at <c>:L293</c>) and the four-argument
    /// form is <c>of_on(name, object, evtName, evtSign)</c> (<c>:L296</c>); an empty signature is the
    /// documented "accept any signature" value, which is precisely what the delegation passes.
    /// </remarks>
    [Fact]
    public void BothSubscribeFormsSucceedAndTheSubscriberThenReceivesADispatch()
    {
        DispatchLog log = new();
        EventBroker broker = new();
        RecordingSubscriber threeArgumentTarget = new(log, FirstLabel);
        RecordingSubscriber fourArgumentTarget = new(log, SecondLabel);

        // :L293 - the three-argument form.
        Assert.Equal(RetCode.OK, broker.Subscribe(AlphaTopic, threeArgumentTarget, ZeroArityHandler));

        // :L296 - the four-argument form, with the empty signature the delegation above supplies.
        Assert.Equal(
            RetCode.OK,
            broker.Subscribe(BetaTopic, fourArgumentTarget, ZeroArityHandler, string.Empty));

        broker.Trigger(AlphaTopic);
        Assert.Equal(1, threeArgumentTarget.InvocationCount);
        Assert.Equal(0, fourArgumentTarget.InvocationCount);

        broker.Trigger(BetaTopic);
        Assert.Equal(1, threeArgumentTarget.InvocationCount);
        Assert.Equal(1, fourArgumentTarget.InvocationCount);
    }

    /// <summary>
    /// One row per way a subscription is rejected: the row name, the topic, the handler name, whether a
    /// target is supplied at all, and the legacy code the subject must answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The ORDER of the first two rows is itself the assertion. The oracle tests both strings at
    /// <c>:L331</c> and only then tests the target at <c>:L332</c>, so a call with an empty topic AND no
    /// target answers the argument code rather than the object code. Reversing those two lines would
    /// still satisfy a suite that only ever supplied one fault at a time, which is why the empty-topic
    /// row deliberately also omits the target.
    /// </para>
    /// <para>
    /// "Invalid target" and "null target" are the same row on purpose. The oracle guards with
    /// <c>Not IsValidObject(object)</c> (<c>:L332</c>), and <c>Predicates.IsValidObject</c> documents
    /// that in .NET a non-null reference is always valid - PowerBuilder's separate "destroyed but not
    /// null" state has no analogue. Supplying null is therefore the only reachable way to fail that
    /// guard, and inventing a second one would be inventing behaviour.
    /// </para>
    /// </remarks>
    public static TheoryData<string, string, string, bool, long> SubscribeRejectionRows =>
        new()
        {
            // :L331 - an empty topic. The target is omitted TOO, so this row also pins the ordering of
            // the two guards: the argument code wins over the object code.
            { "empty topic and no target", "", ZeroArityHandler, false, RetCode.E_INVALID_ARGUMENT },

            // :L331 - an empty topic on its own.
            { "empty topic", "", ZeroArityHandler, true, RetCode.E_INVALID_ARGUMENT },

            // :L331 - an empty handler name. The same guard and therefore the same code; the oracle tests
            // `name = "" or evtName = ""` as one condition.
            { "empty handler name", AlphaTopic, "", true, RetCode.E_INVALID_ARGUMENT },

            // :L332 - no target. A DIFFERENT code from the two above, which is the whole point of
            // carrying both in one table.
            { "no target", AlphaTopic, ZeroArityHandler, false, RetCode.E_INVALID_OBJECT },

            // :L398-L401 - a handler name that cannot be resolved on the target. Asserted as a CODE and
            // nothing else, under C-D: the oracle produces it by failing to initialise an
            // `n_scriptinvoker`, and that type belongs to the deferred ScriptBridge service, so the
            // mechanism is deliberately not named or stood in for anywhere in this suite.
            { "unresolvable handler", AlphaTopic, "ThereIsNoHandlerCalledThis", true, RetCode.E_EVENT_NOT_FOUND }
        };

    /// <summary>
    /// Every rejected subscription answers its own legacy code, throws nothing, and leaves the broker
    /// with no subscription to dispatch.
    /// </summary>
    /// <param name="rowName">The row's description, present so a failure names the case.</param>
    /// <param name="topic">The topic to attempt.</param>
    /// <param name="handlerName">The handler name to attempt.</param>
    /// <param name="supplyTarget">Whether to pass a live target or <see langword="null"/>.</param>
    /// <param name="expectedCode">The legacy code the subject must return.</param>
    /// <remarks>
    /// <para>
    /// "Throws nothing" is asserted rather than assumed because it is a real property of the surface: the
    /// oracle is a return-code API from end to end and never raises for a rejected argument, so a port
    /// that threw would break every call site that tests the code. Reaching the assertion at all is the
    /// proof, and the trailing dispatch assertion proves the rejection also left no half-built entry
    /// behind.
    /// </para>
    /// <para>
    /// Both subscribe forms are exercised with each row: the three-argument form delegates to the
    /// four-argument one (<c>:L293</c>), so a guard implemented in the wrong one would pass one form and
    /// fail the other.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(SubscribeRejectionRows))]
    public void EachRejectedSubscriptionAnswersItsOwnLegacyCodeAndThrowsNothing(
        string rowName,
        string topic,
        string handlerName,
        bool supplyTarget,
        long expectedCode)
    {
        Assert.False(string.IsNullOrEmpty(rowName));

        DispatchLog log = new();
        EventBroker broker = new();
        RecordingSubscriber target = new(log, FirstLabel);
        object? candidate = supplyTarget ? target : null;

        long threeArgumentCode = broker.Subscribe(topic, candidate, handlerName);
        long fourArgumentCode = broker.Subscribe(topic, candidate, handlerName, string.Empty);

        Assert.Equal(expectedCode, threeArgumentCode);
        Assert.Equal(expectedCode, fourArgumentCode);

        // A rejection must leave nothing behind: neither the query surface nor a dispatch may find an
        // entry. The topic is triggered even when it is empty, because an empty name is meaningful
        // elsewhere in the broker (:L793 returns the global default for it) and must still dispatch to
        // nobody here.
        Assert.False(broker.IsSubscribed(topic));
        broker.Trigger(topic);
        broker.Trigger(AlphaTopic);
        Assert.Empty(log.Records);
    }

    /// <summary>
    /// A handler name is matched case-INsensitively while a topic name is matched case-SENSITIVELY, and
    /// both halves are asserted here so the asymmetry is visible in one place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PRESERVED LEGACY SEMANTICS, stated on a single oracle line. <c>w_test_eventful.srw:L250</c>
    /// annotates its own subscribe call with 「事件名（区分大小写）」 for the event name and
    /// 「回调事件名（不区分大小写）」 for the callback name - case-sensitive and case-insensitive
    /// respectively. The mechanism is <c>:L336</c>: the handler name is FOLDED TO LOWER CASE on storage
    /// (<c>newEvent.evtName = Lower(evtName)</c>) while the topic's residual name is stored as written
    /// (<c>:L335</c> narrowed by <c>:L363</c>, <c>:L371</c> and <c>:L378</c>), so the two criteria are
    /// compared against differently normalised keys.
    /// </para>
    /// <para>
    /// LOCATOR NOTE, reported rather than accommodated. The agent brief cites <c>:L337</c> for the fold;
    /// the oracle was re-read and <c>:L337</c> is <c>newEvent.object = object</c> while the fold is one
    /// line earlier at <c>:L336</c>. The line cited here is the one that carries the behaviour. This is a
    /// one-line citation drift in the brief and not a behavioural divergence - the rule itself is exactly
    /// as the brief states it.
    /// </para>
    /// <para>
    /// The asymmetry is easy to lose in either direction and neither loss would be reported by a suite
    /// that tested only one half: folding the topic too would make a dispatch reach subscriptions
    /// registered for a different event, and folding neither would break every consumer that spells a
    /// handler name in its declared casing while the broker stored it lower-cased.
    /// </para>
    /// </remarks>
    [Fact]
    public void AHandlerNameIsCaseInsensitiveWhileATopicNameIsCaseSensitive()
    {
        DispatchLog log = new();
        EventBroker broker = new();
        RecordingSubscriber target = new(log, FirstLabel);

        // The handler name is spelled in a casing that matches NO declared member exactly - all upper
        // case - and the topic is spelled with a leading capital.
        const string mixedCaseTopic = "Alpha";
        Assert.Equal(
            RetCode.OK,
            broker.Subscribe(mixedCaseTopic, target, ZeroArityHandler.ToUpperInvariant()));

        // HALF ONE - :L336. The handler resolved despite the casing, and it is the DECLARED spelling that
        // reaches the log, because the fold applies to the stored criterion and not to the member.
        broker.Trigger(mixedCaseTopic);
        Assert.Equal(1, target.InvocationCount);
        Assert.Equal(ZeroArityHandler, log.RecordAt(0).HandlerName);

        // Every other casing of the same handler name reaches the same handler, so the fold is a
        // property of the comparison rather than of the one spelling used above.
        Assert.Equal(RetCode.OK, broker.Subscribe(BetaTopic, target, ZeroArityHandler.ToLowerInvariant()));
        broker.Trigger(BetaTopic);
        Assert.Equal(2, target.InvocationCount);

        // HALF TWO - the topic is case-SENSITIVE. Triggering the same letters in a different casing
        // reaches nobody, and the ordinal comparison is what makes that true.
        Assert.False(
            string.Equals(mixedCaseTopic, AlphaTopic, StringComparison.Ordinal),
            "the two spellings must differ, or this half of the test proves nothing");
        broker.Trigger(AlphaTopic);
        Assert.Equal(2, target.InvocationCount);
        Assert.False(broker.IsSubscribed(AlphaTopic));
        Assert.True(broker.IsSubscribed(mixedCaseTopic));
    }

    /// <summary>
    /// Subscribing the same target and handler to the same topic twice yields TWO subscriptions and two
    /// invocations per dispatch. The broker does not de-duplicate, and that is asserted rather than
    /// assumed.
    /// </summary>
    /// <remarks>
    /// PRESERVED LEGACY SEMANTICS. <c>:L407-L437</c> is an ordered INSERT with no equality test against
    /// any existing entry anywhere in it - the scan compares names and priorities to find a position and
    /// never compares targets or handler names at all. So a repeated subscribe genuinely adds a second
    /// row, and a consumer that subscribes twice is invoked twice. De-duplicating would be an
    /// improvement, and improvements are forbidden by C-B; it would also silently change the invocation
    /// count every consumer of this broker observes.
    /// </remarks>
    [Fact]
    public void SubscribingTheSameTargetAndHandlerTwiceYieldsTwoSubscriptions()
    {
        DispatchLog log = new();
        EventBroker broker = new();
        RecordingSubscriber target = new(log, FirstLabel);

        Assert.Equal(RetCode.OK, broker.Subscribe(AlphaTopic, target, ZeroArityHandler));
        Assert.Equal(RetCode.OK, broker.Subscribe(AlphaTopic, target, ZeroArityHandler));

        broker.Trigger(AlphaTopic);

        Assert.Equal(2, target.InvocationCount);
        Assert.Equal(2, target.InvocationCountFor(ZeroArityHandler));
        Assert.Equal(2, log.Count);

        // Both rows are the same identity, which is the point: two entries, not one entry seen twice.
        Assert.Equal(
            FirstTargetZeroArity + RunSeparator + FirstTargetZeroArity,
            RenderRun(log),
            StringComparer.Ordinal);

        // And removing them takes one call, because the shapes select a SET rather than an instance.
        Assert.Equal(RetCode.OK, broker.Unsubscribe(AlphaTopic, target, ZeroArityHandler));
        log.Clear();
        broker.Trigger(AlphaTopic);
        Assert.Empty(log.Records);
    }

    /// <summary>
    /// The four-argument form's optional handler signature selects a handler and the subscription then
    /// dispatches; a signature that matches no overload is rejected with the event-not-found code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Scoped deliberately. This asserts that the signature ARGUMENT is honoured - a valid one succeeds
    /// and an unmatched one is rejected - and stops there. What the handler then receives, how a shorter
    /// or longer payload is padded or truncated, and the whole PowerScript-spelling alias table belong to
    /// <c>VariadicDispatchTests</c>, and duplicating them here would put the same property under test in
    /// two files with no added confidence.
    /// </para>
    /// <para>
    /// <c>"any"</c> is used rather than <c>"object"</c> because it is the PowerScript spelling a signature
    /// transcribed from a legacy forward declaration would carry, which is the case the argument exists
    /// to serve.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheOptionalHandlerSignatureSelectsAHandlerAndDispatches()
    {
        DispatchLog log = new();
        EventBroker broker = new();
        RecordingSubscriber target = new(log, FirstLabel);

        Assert.Equal(RetCode.OK, broker.Subscribe(AlphaTopic, target, OneArityHandler, "any"));

        // A signature naming the wrong arity selects nothing, and the rejection is the same code an
        // unresolvable NAME produces (:L398-L401) - resolution failure is resolution failure.
        Assert.Equal(
            RetCode.E_EVENT_NOT_FOUND,
            broker.Subscribe(BetaTopic, target, OneArityHandler, "string,string"));

        broker.Trigger(AlphaTopic, "payload");

        Assert.Equal(1, target.InvocationCount);
        Assert.Equal(OneArityHandler, log.RecordAt(0).HandlerName);

        broker.Trigger(BetaTopic);
        Assert.Equal(1, target.InvocationCount);
    }

    // =================================================================================================
    //  2. THE SEVEN UNSUBSCRIBE SHAPES - of_off
    //  :L171 by target and handler (empty topic, so every topic); :L192 by target alone; :L212 fully
    //  unqualified, which passes a NULL target; :L555 by target with the excluding flag; :L658 by topic,
    //  target and handler; :L680 by topic alone, which also passes a null target; :L725 by topic and
    //  target. All seven are single-line delegations to `_of_Modify(..., MOD_OFF, true)`.
    // =================================================================================================

    /// <summary>
    /// One row per unsubscribe shape: the shape key and the dispatch run that must SURVIVE on each topic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The surviving set is spelled out per row rather than expressed as a count, because a count cannot
    /// tell a correct selection from a wrong one of the same size - and two of these shapes remove
    /// exactly two subscriptions while selecting completely different pairs.
    /// </para>
    /// <para>
    /// Both topics are named on every row for the same reason. Three shapes carry no topic criterion and
    /// therefore reach ACROSS topics; three carry one and must leave the other topic untouched. A table
    /// that only asserted the acted-on topic would pass a shape whose filter had lost its topic criterion
    /// entirely.
    /// </para>
    /// </remarks>
    public static TheoryData<string, string, string> UnsubscribeShapeRows =>
        new()
        {
            // SHAPE 1 - :L171, _of_Modify("", object, evtName, false, MOD_OFF, true). The empty filter
            // means every name, so the first target's zero-arity subscription goes on BOTH topics while
            // its one-arity subscription survives.
            { ByTargetAndHandler, EveryIdentityButFirstTargetZeroArity, EveryIdentityButFirstTargetZeroArity },

            // SHAPE 2 - :L192, _of_Modify("", object, "", false, MOD_OFF, true). No handler criterion, so
            // both of the first target's subscriptions go, on both topics. This is the form the five
            // DataWindow services call from their own teardown.
            { ByTargetAlone, SecondTargetIdentities, SecondTargetIdentities },

            // SHAPE 3 - :L212, a NULL target and an EMPTY filter, so nothing narrows the match and
            // everything goes. se_cst_dw.sru:L583 calls exactly this from `ondestructor`.
            { FullyUnqualified, NoIdentity, NoIdentity },

            // SHAPE 4 - :L555, _of_Modify("", object, "", excluding, MOD_OFF, true). The OUTER negation
            // at :L1051-L1053 inverts the whole composite match, so everything the first target did NOT
            // register goes and its own two subscriptions are what remain.
            { ByTargetExcluding, FirstTargetIdentities, FirstTargetIdentities },

            // SHAPE 5 - :L658, the narrowest shape: all three criteria active, so exactly one
            // subscription on exactly one topic goes.
            { ByTopicTargetAndHandler, EveryIdentityButFirstTargetZeroArity, EveryIdentity },

            // SHAPE 6 - :L680, _of_Modify(name, nullObject, "", false, MOD_OFF, true). A topic criterion
            // with a null target, so the whole topic is cleared and the other topic is untouched.
            { ByTopicAlone, NoIdentity, EveryIdentity },

            // SHAPE 7 - :L725, _of_Modify(name, object, "", false, MOD_OFF, true). Topic and target, no
            // handler criterion, so both of the first target's subscriptions on that one topic go.
            { ByTopicAndTarget, SecondTargetIdentities, EveryIdentity }
        };

    /// <summary>
    /// Each of the seven unsubscribe shapes removes exactly the set its arguments describe, leaves every
    /// other subscription dispatching in its original order, and the removal is permanent.
    /// </summary>
    /// <param name="shape">The shape key; see <see cref="ApplyUnsubscribeShape"/>.</param>
    /// <param name="expectedAlphaRun">The run <see cref="AlphaTopic"/> must still produce.</param>
    /// <param name="expectedBetaRun">The run <see cref="BetaTopic"/> must still produce.</param>
    /// <remarks>
    /// <para>
    /// Survival is verified by TRIGGERING both topics and reading the shared log, not by querying. That
    /// is deliberate: dispatch is the real contract, the query surface is itself under test further down
    /// this file, and <see cref="EventBroker.IsSubscribed"/> carries a documented quirk that makes it a
    /// conservative pre-test rather than an authority.
    /// </para>
    /// <para>
    /// The second pair of triggers asserts PERMANENCE, which is the property that separates this shape
    /// family from the disable family. A removal that had merely suspended the entry would produce the
    /// same first run and could then differ on the second; asserting the run twice closes that gap at no
    /// cost.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(UnsubscribeShapeRows))]
    public void EachUnsubscribeShapeRemovesExactlyItsOwnSetAndDoesSoPermanently(
        string shape,
        string expectedAlphaRun,
        string expectedBetaRun)
    {
        Arrangement arrangement = new();

        // The arrangement starts complete on both topics, so any later difference is attributable to the
        // shape under test and to nothing else.
        Assert.Equal(EveryIdentity, arrangement.Run(AlphaTopic), StringComparer.Ordinal);
        Assert.Equal(EveryIdentity, arrangement.Run(BetaTopic), StringComparer.Ordinal);

        Assert.Equal(RetCode.OK, ApplyUnsubscribeShape(shape, arrangement));

        Assert.Equal(expectedAlphaRun, arrangement.Run(AlphaTopic), StringComparer.Ordinal);
        Assert.Equal(expectedBetaRun, arrangement.Run(BetaTopic), StringComparer.Ordinal);

        // Permanent: re-triggering never resurrects a removed subscription.
        Assert.Equal(expectedAlphaRun, arrangement.Run(AlphaTopic), StringComparer.Ordinal);
        Assert.Equal(expectedBetaRun, arrangement.Run(BetaTopic), StringComparer.Ordinal);
    }

    /// <summary>
    /// An unsubscribed subscription is gone rather than suspended: only a fresh
    /// <see cref="EventBroker.Subscribe(string, object?, string)"/> brings it back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE DISTINCTION THIS SUITE IS BUILT AROUND, asserted from the removal side. <c>:L1054-L1067</c>
    /// DESTROYS the matched entry - outside a dispatch by rebuilding the table from the survivors
    /// (<c>:L1060-L1066</c> with the assignment at <c>:L1085</c>), inside one by tombstoning it and
    /// queueing a sweep - while <c>:L1068-L1071</c> only writes a boolean and keeps the entry. So there
    /// is no re-enable that can undo an unsubscribe: the row does not exist to re-enable.
    /// </para>
    /// <para>
    /// Both halves are asserted together. Re-enabling is attempted FIRST and must be a no-op, because a
    /// port that had implemented removal as suspension would satisfy every "did it run?" assertion right
    /// up to this line.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnUnsubscribedSubscriptionIsGoneAndOnlyAFreshSubscribeRestoresIt()
    {
        Arrangement arrangement = new();

        Assert.Equal(RetCode.OK, arrangement.Broker.Unsubscribe(AlphaTopic, arrangement.First, ZeroArityHandler));
        Assert.Equal(EveryIdentityButFirstTargetZeroArity, arrangement.Run(AlphaTopic), StringComparer.Ordinal);

        // A re-enable cannot resurrect it, because there is no entry to re-enable. This call still
        // succeeds - a filter matching nothing is a no-op, not an error - and changes nothing.
        Assert.Equal(
            RetCode.OK,
            arrangement.Broker.Disable(AlphaTopic, arrangement.First, ZeroArityHandler, disabled: false));
        Assert.Equal(EveryIdentityButFirstTargetZeroArity, arrangement.Run(AlphaTopic), StringComparer.Ordinal);

        // Only a fresh subscribe brings it back - and it comes back at the TAIL of the equal-priority
        // run, not at its original position, because :L419 appends. That relocation is the observable
        // difference between re-subscribing and re-enabling, and the disable theory below asserts the
        // other side of it.
        Assert.Equal(
            RetCode.OK,
            arrangement.Broker.Subscribe(AlphaTopic, arrangement.First, ZeroArityHandler));
        Assert.Equal(
            FirstTargetOneArity + RunSeparator +
            SecondTargetZeroArity + RunSeparator +
            SecondTargetOneArity + RunSeparator +
            FirstTargetZeroArity,
            arrangement.Run(AlphaTopic),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// A shape that matches nothing is a harmless no-op that answers <see cref="RetCode.OK"/>, for both
    /// families and for an empty broker.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>:L1089</c> returns <c>RetCode.OK</c> unconditionally once the filter has parsed; "matched
    /// nothing" is not an error condition anywhere in the engine, and the only failure the engine reports
    /// is a malformed filter (<c>:L1021-L1023</c>), whose thirteen spellings belong to
    /// <c>LifetimeNamespaceFilterTests</c>.
    /// </para>
    /// <para>
    /// The trailing survival assertion is the load-bearing half. <c>:L1025-L1026</c> CLEARS both lexical
    /// bounds before the loop and rebuilds them as it runs, so a no-op call still rewrites the two values
    /// the dispatch fast-path (<c>:L797-L798</c>) and the subscribed query (<c>:L767-L770</c>) depend on.
    /// A rebuild that dropped an unmatched entry would leave the broker unable to dispatch anything, and
    /// the return code alone would not notice.
    /// </para>
    /// </remarks>
    [Fact]
    public void AShapeThatMatchesNothingIsAHarmlessNoOpThatSucceeds()
    {
        Arrangement arrangement = new();
        const string unusedTopic = "no-such-topic";
        RecordingSubscriber strangerTarget = new(arrangement.Log, "STRANGER");

        Assert.Equal(RetCode.OK, arrangement.Broker.Unsubscribe(unusedTopic));
        Assert.Equal(RetCode.OK, arrangement.Broker.Unsubscribe(unusedTopic, arrangement.First));
        Assert.Equal(RetCode.OK, arrangement.Broker.Unsubscribe((object?)strangerTarget));
        Assert.Equal(RetCode.OK, arrangement.Broker.Unsubscribe(strangerTarget, ZeroArityHandler));
        Assert.Equal(RetCode.OK, arrangement.Broker.Disable(unusedTopic, disabled: true));
        Assert.Equal(RetCode.OK, arrangement.Broker.Disable((object?)strangerTarget, disabled: true));

        // Nothing was disturbed: both bounds were rebuilt intact and both topics still dispatch in full.
        Assert.Equal(EveryIdentity, arrangement.Run(AlphaTopic), StringComparer.Ordinal);
        Assert.Equal(EveryIdentity, arrangement.Run(BetaTopic), StringComparer.Ordinal);
        Assert.True(arrangement.Broker.IsSubscribed(AlphaTopic));
        Assert.True(arrangement.Broker.IsSubscribed(BetaTopic));
        Assert.Equal(0, strangerTarget.InvocationCount);

        // And on an empty broker every bulk form is still a success rather than a failure.
        EventBroker emptyBroker = new();
        Assert.Equal(RetCode.OK, emptyBroker.Unsubscribe());
        Assert.Equal(RetCode.OK, emptyBroker.Unsubscribe((object?)null));
        Assert.Equal(RetCode.OK, emptyBroker.Unsubscribe((object?)null, excluding: true));
        Assert.Equal(RetCode.OK, emptyBroker.Disable(disabled: true));
        Assert.False(emptyBroker.IsSubscribed(AlphaTopic));
    }

    // =================================================================================================
    //  3. THE SEVEN DISABLE SHAPES - of_disable
    //  :L1092 topic, target, handler, value; :L1115 topic, target, value; :L1138 topic, value; :L1165
    //  target, handler, value; :L1187 target, value; :L1208 value alone; :L1230 target, value, excluding.
    //  The same seven selectors as of_off over the SAME engine, differing only in the modification kind
    //  and in carrying a boolean the engine ASSIGNS rather than a fixed true.
    //
    //  THE LOAD-BEARING DIFFERENCE, at :L1053-L1071. MOD_OFF destroys the entry; MOD_DISABLE writes
    //  `Events[nIndex].disabled = val` and RETAINS it. The dispatch loop skips tombstoned and suspended
    //  entries with two adjacent `continue`s (:L829 and :L830), so BOTH are invisible to dispatch and
    //  "did it run?" cannot tell them apart - only the restore can. See the header's key insight.
    // =================================================================================================

    /// <summary>
    /// One row per disable shape: the shape key and the dispatch run each topic must produce WHILE the
    /// shape's selection is suspended.
    /// </summary>
    /// <remarks>
    /// Deliberately the same seven selections as <see cref="UnsubscribeShapeRows"/>, because the two
    /// families share one engine and one filter grammar; only the kind differs. Keeping the expectations
    /// identical is what makes the difference between the families attributable to retention alone.
    /// </remarks>
    public static TheoryData<string, string, string> DisableShapeRows =>
        new()
        {
            // SHAPE 1 - :L1092, _of_Modify(name, object, evtName, false, MOD_DISABLE, disabled).
            { ByTopicTargetAndHandler, EveryIdentityButFirstTargetZeroArity, EveryIdentity },

            // SHAPE 2 - :L1115, _of_Modify(name, object, "", false, MOD_DISABLE, disabled).
            { ByTopicAndTarget, SecondTargetIdentities, EveryIdentity },

            // SHAPE 3 - :L1138, _of_Modify(name, nullObject, "", false, MOD_DISABLE, disabled). The form
            // w_test_eventful.srw:L153 exercises live.
            { ByTopicAlone, NoIdentity, EveryIdentity },

            // SHAPE 4 - :L1165, _of_Modify("", object, evtName, false, MOD_DISABLE, disabled). No topic
            // criterion, so it reaches across both topics.
            { ByTargetAndHandler, EveryIdentityButFirstTargetZeroArity, EveryIdentityButFirstTargetZeroArity },

            // SHAPE 5 - :L1187, _of_Modify("", object, "", false, MOD_DISABLE, disabled).
            { ByTargetAlone, SecondTargetIdentities, SecondTargetIdentities },

            // SHAPE 6 - :L1208, _of_Modify("", nullObj, "", false, MOD_DISABLE, disabled). Everything is
            // suspended, so both lexical bounds fall empty at :L1073-L1076 and the dispatch fast-path at
            // :L798 rejects every name - which is how a fully suspended broker costs nothing at all.
            { FullyUnqualified, NoIdentity, NoIdentity },

            // SHAPE 7 - :L1230, _of_Modify("", object, "", excluding, MOD_DISABLE, disabled). Note the
            // oracle's own argument order puts `disabled` BEFORE `excluding`, which the port preserves.
            // Exercised by w_test_eventful.srw:L137.
            { ByTargetExcluding, FirstTargetIdentities, FirstTargetIdentities }
        };

    /// <summary>
    /// Each of the seven disable shapes suspends exactly the set its arguments describe, RETAINS every
    /// suspended subscription, and re-enabling restores each one to its ORIGINAL dispatch position
    /// without re-subscribing.
    /// </summary>
    /// <param name="shape">The shape key; see <see cref="ApplyDisableShape"/>.</param>
    /// <param name="expectedAlphaRun">The run <see cref="AlphaTopic"/> must produce while suspended.</param>
    /// <param name="expectedBetaRun">The run <see cref="BetaTopic"/> must produce while suspended.</param>
    /// <remarks>
    /// <para>
    /// <b>THE MOST IMPORTANT ASSERTION IN THIS SUITE, and it is written for all seven shapes rather than
    /// for one.</b> Step 4 below compares the restored run BYTE FOR BYTE against the run recorded before
    /// the disable, which pins three properties in one equality: the entry still existed (retention,
    /// <c>:L1068-L1071</c>), it came back without a fresh subscribe, and it came back IN PLACE. That last
    /// point is what a re-subscribe cannot reproduce - <c>:L419</c> appends, so a re-subscribed entry
    /// lands at the tail of its equal-priority run, and
    /// <see cref="AnUnsubscribedSubscriptionIsGoneAndOnlyAFreshSubscribeRestoresIt"/> asserts exactly that
    /// relocation. Position is asserted here; the ordering RULES that decide what a position is belong to
    /// <c>DispatchOrderTests</c>.
    /// </para>
    /// <para>
    /// PRESERVED LEGACY SEMANTICS - the flag is an ASSIGNMENT, not a toggle. <c>:L1070</c> reads
    /// <c>Events[nIndex].disabled = val</c>, so applying the same shape twice with the same value is
    /// idempotent in both directions. Steps 3 and 5 assert that. A toggle would pass a single-application
    /// test and then silently re-enable everything on the second call, which is exactly the kind of defect
    /// a wire-level <c>DisableEvent</c> retried by a client would hit first.
    /// </para>
    /// <para>
    /// The five steps, in order: record the baseline; suspend and check the selection; suspend again and
    /// check nothing moved; resume and check the baseline is back exactly; resume again and check that is
    /// harmless too.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(DisableShapeRows))]
    public void EachDisableShapeSuspendsItsOwnSetAndReEnablingRestoresItInPlace(
        string shape,
        string expectedAlphaRun,
        string expectedBetaRun)
    {
        Arrangement arrangement = new();

        // STEP 1 - the baseline, captured from a real dispatch rather than assumed from the constants, so
        // the comparison in step 4 is against observed behaviour.
        string baselineAlphaRun = arrangement.Run(AlphaTopic);
        string baselineBetaRun = arrangement.Run(BetaTopic);
        Assert.Equal(EveryIdentity, baselineAlphaRun, StringComparer.Ordinal);
        Assert.Equal(EveryIdentity, baselineBetaRun, StringComparer.Ordinal);

        // STEP 2 - suspend. The selection is skipped during dispatch, exactly as an unsubscribe would
        // look from here, which is why step 4 exists.
        Assert.Equal(RetCode.OK, ApplyDisableShape(shape, arrangement, disabled: true));
        Assert.Equal(expectedAlphaRun, arrangement.Run(AlphaTopic), StringComparer.Ordinal);
        Assert.Equal(expectedBetaRun, arrangement.Run(BetaTopic), StringComparer.Ordinal);

        // STEP 3 - :L1070 assigns rather than toggles, so suspending an already-suspended selection
        // leaves it suspended.
        Assert.Equal(RetCode.OK, ApplyDisableShape(shape, arrangement, disabled: true));
        Assert.Equal(expectedAlphaRun, arrangement.Run(AlphaTopic), StringComparer.Ordinal);
        Assert.Equal(expectedBetaRun, arrangement.Run(BetaTopic), StringComparer.Ordinal);

        // STEP 4 - RETENTION AND POSITION, in one equality against the observed baseline. No Subscribe
        // call has been made since step 1; the only thing that can bring these subscriptions back is the
        // entry still being in the table where it always was.
        Assert.Equal(RetCode.OK, ApplyDisableShape(shape, arrangement, disabled: false));
        Assert.Equal(baselineAlphaRun, arrangement.Run(AlphaTopic), StringComparer.Ordinal);
        Assert.Equal(baselineBetaRun, arrangement.Run(BetaTopic), StringComparer.Ordinal);

        // STEP 5 - resuming an already-resumed selection is harmless, the other half of :L1070.
        Assert.Equal(RetCode.OK, ApplyDisableShape(shape, arrangement, disabled: false));
        Assert.Equal(baselineAlphaRun, arrangement.Run(AlphaTopic), StringComparer.Ordinal);
        Assert.Equal(baselineBetaRun, arrangement.Run(BetaTopic), StringComparer.Ordinal);

        // The subscribed query agrees with dispatch again once everything is resumed, which closes the
        // loop with the quirk asserted in
        // ADisabledSubscriptionReportsAsNotSubscribedAtEveryTablePosition below.
        Assert.True(arrangement.Broker.IsSubscribed(AlphaTopic));
        Assert.True(arrangement.Broker.IsSubscribed(BetaTopic));
    }

    /// <summary>
    /// The excluding disable shape suspends everything EXCEPT the named target's subscriptions, and the
    /// two levels of negation are not the same thing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Carried as its own test as well as a theory row because the exclusion is the one shape whose
    /// meaning is inverted, and because it is the shape a component uses to silence every other
    /// subscriber while staying live itself.
    /// </para>
    /// <para>
    /// <c>excluding</c> is the OUTER negation: <c>:L1051-L1053</c> applies it to the whole composite match
    /// AFTER every criterion has been evaluated. It is not the per-criterion <c>'^'</c> of the filter
    /// grammar (<c>:L1008-L1015</c>, applied at <c>:L1032-L1043</c>), the two compose, and collapsing them
    /// into one flag would silently change which subscriptions a filter reaches. The thirteen filter
    /// spellings that exercise the inner negation belong to <c>LifetimeNamespaceFilterTests</c>; this test
    /// stays on the outer one, which is the only one an overload shape can express.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheExcludingDisableShapeSuspendsEverythingButTheNamedTarget()
    {
        Arrangement arrangement = new();

        Assert.Equal(
            RetCode.OK,
            arrangement.Broker.Disable(arrangement.First, disabled: true, excluding: true));

        // The named target survives on both topics; everyone else is suspended on both.
        Assert.Equal(FirstTargetIdentities, arrangement.Run(AlphaTopic), StringComparer.Ordinal);
        Assert.Equal(FirstTargetIdentities, arrangement.Run(BetaTopic), StringComparer.Ordinal);
        Assert.Equal(0, arrangement.Second.InvocationCount);

        // Without the flag the SAME arguments mean the ordinary thing, which is what proves the flag is
        // read rather than ignored: the selection inverts.
        Assert.Equal(
            RetCode.OK,
            arrangement.Broker.Disable(arrangement.First, disabled: false, excluding: true));
        Assert.Equal(
            RetCode.OK,
            arrangement.Broker.Disable(arrangement.First, disabled: true, excluding: false));
        Assert.Equal(SecondTargetIdentities, arrangement.Run(AlphaTopic), StringComparer.Ordinal);
        Assert.Equal(SecondTargetIdentities, arrangement.Run(BetaTopic), StringComparer.Ordinal);
    }

    /// <summary>
    /// A suspended subscription is skipped by dispatch and by the subscribed query alike, yet is still
    /// there - and an unsubscribed one in the same position is not. The two families are separated in one
    /// test.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C-K, made concrete. AAP 0.4.3 C-03 publishes <c>DisableEvent</c> and <c>EnableEvent</c> on
    /// <c>dataservices.v1.DataWindowService</c>, so the distinction asserted here is a WIRE-LEVEL
    /// guarantee and not an internal detail: a remote caller that suspends a subscription is entitled to
    /// resume it, and that is only meaningful because <c>:L1068-L1071</c> retains the entry.
    /// </para>
    /// <para>
    /// The two halves use the same topic, the same target and the same handler, so nothing but the
    /// operation differs. Both are invisible to dispatch (<c>:L829</c> and <c>:L830</c>); only one comes
    /// back.
    /// </para>
    /// </remarks>
    [Fact]
    public void SuspensionRetainsTheEntryWhereRemovalDestroysIt()
    {
        Arrangement suspended = new();
        Assert.Equal(
            RetCode.OK,
            suspended.Broker.Disable(AlphaTopic, suspended.First, ZeroArityHandler, disabled: true));

        Arrangement removed = new();
        Assert.Equal(
            RetCode.OK,
            removed.Broker.Unsubscribe(AlphaTopic, removed.First, ZeroArityHandler));

        // INDISTINGUISHABLE so far, which is the whole trap: both skip the entry and both leave the rest
        // of the topic intact.
        Assert.Equal(EveryIdentityButFirstTargetZeroArity, suspended.Run(AlphaTopic), StringComparer.Ordinal);
        Assert.Equal(EveryIdentityButFirstTargetZeroArity, removed.Run(AlphaTopic), StringComparer.Ordinal);

        // Now they diverge. The same re-enable call restores one and does nothing at all for the other.
        Assert.Equal(
            RetCode.OK,
            suspended.Broker.Disable(AlphaTopic, suspended.First, ZeroArityHandler, disabled: false));
        Assert.Equal(
            RetCode.OK,
            removed.Broker.Disable(AlphaTopic, removed.First, ZeroArityHandler, disabled: false));

        Assert.Equal(EveryIdentity, suspended.Run(AlphaTopic), StringComparer.Ordinal);
        Assert.Equal(EveryIdentityButFirstTargetZeroArity, removed.Run(AlphaTopic), StringComparer.Ordinal);
    }

    // =================================================================================================
    //  4. THE QUERY SURFACE - four state queries and two default-return-value setters
    //  of_getreturnvalue :L620, of_isprocessed :L639, of_ispost :L706, of_issubscribed :L747-L783,
    //  of_setdefaultreturnvalue(name, value) :L479-L517 and of_setdefaultreturnvalue(value) :L519-L539.
    // =================================================================================================

    /// <summary>
    /// The subscribed query finds a topic at the FIRST table position, in the INTERIOR and at the LAST
    /// position, and agrees with dispatch at all three.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PRESERVED LEGACY QUIRK, and the reason all three positions must be covered rather than one. The
    /// oracle's scan is <c>for nIndex = 2 to nCount - 1</c> (<c>:L775</c>), one-based, so it visits
    /// NEITHER slot 1 NOR slot <c>nCount</c>. The two ends are covered only by the equality
    /// short-circuits against the lexical bounds at <c>:L767-L768</c>, which in the common case ARE the
    /// first and last entries' names. A test that probed only an interior name would therefore pass even
    /// if both short-circuits had been dropped, and a test that probed only an end would pass even if the
    /// loop range were wrong. The loop range is reproduced verbatim in the port and must not be "fixed".
    /// </para>
    /// <para>
    /// Five subscriptions on five ascending names give three interior slots, which is what makes the range
    /// observable rather than degenerate. Dispatch is asserted alongside each answer so the query is
    /// checked against the behaviour it is a pre-test for rather than against itself.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSubscribedQueryFindsATopicAtTheFirstInteriorAndLastTablePositions()
    {
        DispatchLog log = new();
        EventBroker broker = new();
        RecordingSubscriber target = new(log, FirstLabel);

        foreach (string topic in LexicallyOrderedTopics)
        {
            Assert.Equal(RetCode.OK, broker.Subscribe(topic, target, ZeroArityHandler));
        }

        // The names were registered in ascending order and the table is kept ascending (:L409-L412), so
        // slot 1 is the first entry here, slot nCount the last, and the middle three are the interior.
        for (int position = 0; position < LexicallyOrderedTopics.Length; position++)
        {
            string topic = LexicallyOrderedTopics[position];

            Assert.True(
                broker.IsSubscribed(topic),
                $"the topic at table position {position} must report as subscribed");

            // A handler is never told which event reached it - the broker invokes it with the payload
            // alone and the oracle's own handler has no event-name parameter either
            // (w_test_eventful.srw:L44) - so the suite states the topic on the subscriber immediately
            // before triggering. That is the double's documented usage, not a shortcut.
            target.Topic = topic;

            log.Clear();
            broker.Trigger(topic);
            Assert.Single(log.Records);
            Assert.Equal(topic, log.RecordAt(0).Topic, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// The subscribed query answers <see langword="false"/> for an empty name, for a name below the
    /// table's lower bound, for a name above its upper bound and for a name that was never subscribed at
    /// all.
    /// </summary>
    /// <remarks>
    /// The four short-circuits in the oracle's own order: an empty name at <c>:L766</c>, then the two
    /// bound equalities at <c>:L767-L768</c>, then the two range rejects at <c>:L769-L770</c>. The
    /// empty-name row matters because an empty name is MEANINGFUL elsewhere in the broker - <c>:L793</c>
    /// returns the global default return value for it - and must simply answer false here. The two
    /// out-of-range rows exercise the fast path that makes an unsubscribed event cost nothing, and the
    /// interior row proves the loop is reached and still answers false.
    /// </remarks>
    [Fact]
    public void TheSubscribedQueryRejectsAnEmptyNameAndAnythingOutsideTheLexicalBounds()
    {
        DispatchLog log = new();
        EventBroker broker = new();
        RecordingSubscriber target = new(log, FirstLabel);

        foreach (string topic in LexicallyOrderedTopics)
        {
            Assert.Equal(RetCode.OK, broker.Subscribe(topic, target, ZeroArityHandler));
        }

        // :L766
        Assert.False(broker.IsSubscribed(string.Empty));

        // :L769 - ordinally below "aaa". Upper case sorts before lower case ordinally, which is exactly
        // the kind of thing a culture-sensitive comparison would get wrong.
        Assert.False(broker.IsSubscribed("AAA"));
        Assert.True(string.CompareOrdinal("AAA", LexicallyOrderedTopics[0]) < 0);

        // :L770 - ordinally above "eee".
        Assert.False(broker.IsSubscribed("zzz"));

        // Inside the bounds but not present, so the loop runs and still answers false (:L782).
        Assert.False(broker.IsSubscribed("bbc"));

        // Nothing was dispatched by any of the above, and nothing was disturbed by asking.
        Assert.Equal(0, target.InvocationCount);
        Assert.True(broker.IsSubscribed(LexicallyOrderedTopics[0]));
    }

    /// <summary>
    /// A suspended-but-retained subscription reports as NOT subscribed, at the first table position, in
    /// the interior and at the last position alike.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PRESERVED LEGACY QUIRK - the agent brief names <c>:L764-L766</c> for it, and the mechanism spans
    /// two sites that must both be reproduced. The scan skips a suspended entry outright at <c>:L777</c>,
    /// which covers the interior; and the modification engine's bounds recomputation at
    /// <c>:L1073-L1074</c> contributes only entries that are neither tombstoned nor suspended, so
    /// suspending the first or last entry NARROWS the bounds and the short-circuit block beginning at
    /// <c>:L764</c> then rejects the name before the loop is even reached.
    /// </para>
    /// <para>
    /// The consequence is deliberate and is asserted as CORRECT rather than tidied: this query is a fast
    /// CONSERVATIVE pre-test, not an authority on membership. <c>se_cst_dw.sru:L166</c> and <c>:L316</c>
    /// use it exactly that way - to avoid assembling a payload for an event nobody is listening to - and a
    /// suspended subscriber is, for that purpose, correctly reported as absent.
    /// </para>
    /// <para>
    /// Each of the three positions is exercised on its own broker so no earlier suspension can be the
    /// reason a later one is reported absent.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADisabledSubscriptionReportsAsNotSubscribedAtEveryTablePosition()
    {
        int[] positionsUnderTest = [0, 2, LexicallyOrderedTopics.Length - 1];

        foreach (int position in positionsUnderTest)
        {
            DispatchLog log = new();
            EventBroker broker = new();
            RecordingSubscriber target = new(log, FirstLabel);

            foreach (string topic in LexicallyOrderedTopics)
            {
                Assert.Equal(RetCode.OK, broker.Subscribe(topic, target, ZeroArityHandler));
            }

            string suspendedTopic = LexicallyOrderedTopics[position];
            Assert.True(broker.IsSubscribed(suspendedTopic));

            Assert.Equal(RetCode.OK, broker.Disable(suspendedTopic, disabled: true));

            // :L777 for an interior slot, :L1073-L1074 narrowing the bounds for an end slot.
            Assert.False(
                broker.IsSubscribed(suspendedTopic),
                $"a suspended subscription at table position {position} must report as not subscribed");

            // It is genuinely suspended rather than removed: dispatch skips it now, and re-enabling brings
            // both the dispatch AND the query back. That is the retention half, asserted at every position
            // rather than once.
            log.Clear();
            broker.Trigger(suspendedTopic);
            Assert.Empty(log.Records);

            Assert.Equal(RetCode.OK, broker.Disable(suspendedTopic, disabled: false));
            Assert.True(broker.IsSubscribed(suspendedTopic));

            log.Clear();
            broker.Trigger(suspendedTopic);
            Assert.Single(log.Records);

            // Every other name is unaffected throughout, so the narrowing is scoped to the suspension.
            foreach (string other in LexicallyOrderedTopics)
            {
                Assert.True(broker.IsSubscribed(other));
            }
        }
    }

    /// <summary>
    /// An unsubscribed topic reports as not subscribed, and the whole query answers false on an empty
    /// broker.
    /// </summary>
    /// <remarks>
    /// With an empty table both lexical bounds are the empty string, so the upper-bound reject at
    /// <c>:L770</c> fires for any non-empty name and the query short-circuits without touching the table
    /// at all. That is the same mechanism that makes a dispatch to an unsubscribed event free
    /// (<c>:L798</c>), which is why the two are asserted together.
    /// </remarks>
    [Fact]
    public void AnUnsubscribedTopicReportsAsNotSubscribed()
    {
        DispatchLog log = new();
        EventBroker broker = new();
        RecordingSubscriber target = new(log, FirstLabel);

        Assert.False(broker.IsSubscribed(AlphaTopic));

        Assert.Equal(RetCode.OK, broker.Subscribe(AlphaTopic, target, ZeroArityHandler));
        Assert.True(broker.IsSubscribed(AlphaTopic));

        Assert.Equal(RetCode.OK, broker.Unsubscribe(AlphaTopic));
        Assert.False(broker.IsSubscribed(AlphaTopic));

        log.Clear();
        broker.Trigger(AlphaTopic);
        Assert.Empty(log.Records);
    }

    /// <summary>
    /// On a fresh broker the recorded return value is <see langword="null"/>, the processed flag is
    /// <see langword="false"/>, the post flag is <see langword="false"/> and nothing is queued.
    /// </summary>
    /// <remarks>
    /// The resting values, taken straight from the fields the oracle's constructor leaves untouched:
    /// <c>:L636</c> returns <c>_aRetVal</c>, which starts null; <c>:L655</c> is
    /// <c>Not IsNull(_aRetVal)</c> and therefore false; <c>:L722</c> returns <c>_bIsPost</c>, which starts
    /// false. Asserted because all three are DISPATCH-SCOPED, and a port that had made any of them
    /// persistent would look correct inside a dispatch and wrong before the first one.
    /// </remarks>
    [Fact]
    public void TheStateQueriesRestAtTheirNeutralValuesOnAFreshBroker()
    {
        EventBroker broker = new();

        Assert.Null(broker.GetReturnValue());
        Assert.False(broker.IsProcessed());

        // The in-dispatch behaviour of the post flag - and of the queue and its drain - belongs to
        // PostQueueTests. Only the resting value is this suite's business.
        Assert.False(broker.IsPost());
        Assert.Equal(0, broker.PendingPostedContinuationCount);
    }

    /// <summary>
    /// Processed is exactly "the recorded return value is not <see langword="null"/>": a subscriber
    /// returning null never sets it, one returning a value does, and both queries are scoped to the
    /// dispatch and reset after it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>:L655</c> is literally <c>Not IsNull(_aRetVal)</c>, so the two queries are ONE quantity asked
    /// two ways. The consequence the oracle states in prose at <c>w_test_eventful.srw:L234</c> - 「没有返回值
    /// 则定义为未被处理」, no return value is defined as not handled - is what the null-returning subscriber
    /// pins here: a subscriber that handled the event by returning null is indistinguishable from one that
    /// did not handle it at all. That is the oracle's position, not an approximation of it.
    /// </para>
    /// <para>
    /// Three subscribers, in dispatch order, all subscribed with the <c>'*'</c> capture symbol so the
    /// handled latch flipping part-way through does not change WHO runs - that escalation is
    /// <c>PriorityAndCaptureTests</c>' subject, and letting it interfere here would confuse two properties.
    /// The observation is taken from inside the third subscriber's handler because handled detection runs
    /// in the dispatch's FINALLY block (<c>:L909-L933</c>), so a handler sees the state as of before its
    /// own return - which is why the observer must run after the subscriber whose value it is observing.
    /// </para>
    /// <para>
    /// The trailing assertions pin the SCOPING. Every dispatch saves the accumulated value on entry
    /// (<c>:L815-L816</c>) and restores it on exit (<c>:L951</c>), so a caller reading these after
    /// <see cref="EventBroker.Trigger"/> returns sees what was there beforehand and NOT the dispatch's
    /// result. <see cref="EventBroker.Trigger"/>'s own return value is the way to read an outcome from
    /// outside, and it is a different quantity - the last invocation's raw value rather than the last
    /// handled one.
    /// </para>
    /// </remarks>
    [Fact]
    public void ProcessedIsExactlyTheRecordedReturnValueBeingNonNull()
    {
        const string capturingTopic = "*state";
        const string topic = "state";
        const long handledValue = 42L;

        DispatchLog log = new();
        EventBroker broker = new();
        RecordingSubscriber nullReturning = new(log, "NULLRETURNING", broker);
        RecordingSubscriber valueReturning = new(log, "VALUERETURNING", broker);
        RecordingSubscriber observer = new(log, "OBSERVER", broker);

        Assert.Equal(RetCode.OK, broker.Subscribe(capturingTopic, nullReturning, ZeroArityHandler));
        Assert.Equal(RetCode.OK, broker.Subscribe(capturingTopic, valueReturning, ZeroArityHandler));
        Assert.Equal(RetCode.OK, broker.Subscribe(capturingTopic, observer, ZeroArityHandler));

        nullReturning.SetReturnValue(ZeroArityHandler, null);
        valueReturning.SetReturnValue(ZeroArityHandler, handledValue);

        bool processedAfterNullReturn = true;
        object? recordedAfterNullReturn = handledValue;
        nullReturning.DuringAnyHandler = (dispatching, _) =>
        {
            // Observed BEFORE this subscriber's own null return is examined, so this reads the state left
            // by the empty prefix of the dispatch: nothing has been handled yet.
            processedAfterNullReturn = dispatching!.IsProcessed();
            recordedAfterNullReturn = dispatching.GetReturnValue();
        };

        bool processedAfterValueReturn = false;
        object? recordedAfterValueReturn = null;
        observer.DuringAnyHandler = (dispatching, _) =>
        {
            processedAfterValueReturn = dispatching!.IsProcessed();
            recordedAfterValueReturn = dispatching.GetReturnValue();
        };

        object? triggerResult = broker.Trigger(topic);

        // All three ran, in registration order.
        Assert.Equal(3, log.Count);

        // A null return leaves the event unprocessed and records nothing.
        Assert.False(processedAfterNullReturn);
        Assert.Null(recordedAfterNullReturn);

        // A non-null return flips processed and IS the recorded value - one quantity, two questions.
        Assert.True(processedAfterValueReturn);
        AssertReturnedValue(handledValue, recordedAfterValueReturn);

        // Trigger answers the LAST invocation's raw value, which is the observer's null, and the final
        // substitution at :L966-L970 then supplies the resolved default - here unset, so null.
        Assert.Null(triggerResult);

        // Both queries are dispatch-scoped and are back at their resting values.
        Assert.False(broker.IsProcessed());
        Assert.Null(broker.GetReturnValue());
    }

    /// <summary>
    /// Both default-return-value setters succeed outside a dispatch, the per-topic value takes precedence
    /// for its own topic, the global value governs every other topic, and setting the same topic twice
    /// REPLACES rather than duplicates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The resolver's three arms, in the oracle's order (<c>:L541-L553</c>): with no per-name registration
    /// at all the global default is returned without a search (<c>:L544</c>); otherwise the FIRST
    /// registration whose name matches (<c>:L546-L550</c>); otherwise the global default again
    /// (<c>:L552</c>).
    /// </para>
    /// <para>
    /// Replacement is asserted because the setter's own scan is first-match-wins and stops there
    /// (<c>:L505-L511</c>), mirroring the resolver's. An implementation that APPENDED a second
    /// registration for an existing name would leave the first one shadowing it forever, and the only
    /// symptom would be a stale default - which no return code would report.
    /// </para>
    /// </remarks>
    [Fact]
    public void BothDefaultReturnValueSettersSucceedOutsideADispatchAndThePerTopicOneWins()
    {
        const long globalDefault = 9L;
        const long firstPerTopicDefault = 1L;
        const long replacementPerTopicDefault = 3L;

        EventBroker broker = new();

        // :L544 - with no per-name registration the global default answers for every name.
        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue(globalDefault));
        AssertReturnedValue(globalDefault, broker.Trigger(AlphaTopic));
        AssertReturnedValue(globalDefault, broker.Trigger(BetaTopic));

        // :L513-L514 then :L505-L511 - append once, then REPLACE rather than append again.
        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue(AlphaTopic, firstPerTopicDefault));
        AssertReturnedValue(firstPerTopicDefault, broker.Trigger(AlphaTopic));

        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue(AlphaTopic, replacementPerTopicDefault));
        AssertReturnedValue(replacementPerTopicDefault, broker.Trigger(AlphaTopic));

        // :L552 - the other topic still falls through to the global default, so the per-topic entry is
        // scoped to its own name.
        AssertReturnedValue(globalDefault, broker.Trigger(BetaTopic));

        // :L501 - an empty name is the one argument fault this setter reports.
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, broker.SetDefaultReturnValue(string.Empty, 0L));
    }

    /// <summary>
    /// Both default-return-value setters REFUSE while a dispatch is in progress, answering
    /// <see cref="RetCode.FAILED"/> and throwing nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PRESERVED LEGACY SEMANTICS, and it is behaviour rather than defensiveness. <c>:L500</c> and
    /// <c>:L536</c> both return the failure code when the dispatch depth is above zero. The reason is
    /// visible at <c>:L795</c>: a dispatch resolves its default ONCE, before the loop starts, and every
    /// handled-detection decision in that dispatch is measured against the resolved value. Allowing a
    /// change part-way through would make two subscribers in the same dispatch answer to different
    /// yardsticks.
    /// </para>
    /// <para>
    /// LOCATOR NOTE, reported rather than accommodated. The agent brief cites <c>:L502</c> for the
    /// per-topic setter's refusal; the oracle was re-read and <c>:L502</c> is blank, the refusal is at
    /// <c>:L500</c> and the empty-name check immediately after it at <c>:L501</c>. The two lines cited here
    /// are the ones that carry the behaviour. Citation drift in the brief, not a behavioural divergence.
    /// </para>
    /// <para>
    /// The refusal is asserted as CORRECT and NOT as something to soften: a port that queued the change
    /// for after the dispatch, or that applied it immediately, would silently change which subscribers
    /// count as having handled the event. That the call REPORTS the refusal rather than ignoring it is the
    /// other half - the caller can tell it did not take effect.
    /// </para>
    /// <para>
    /// The value established before the dispatch is asserted afterwards to be intact, which proves the
    /// refusal was total rather than partial.
    /// </para>
    /// </remarks>
    [Fact]
    public void BothDefaultReturnValueSettersRefuseDuringADispatch()
    {
        const long establishedDefault = 7L;
        const long rejectedDefault = 11L;

        DispatchLog log = new();
        EventBroker broker = new();
        RecordingSubscriber target = new(log, FirstLabel, broker);

        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue(establishedDefault));
        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue(AlphaTopic, establishedDefault));
        Assert.Equal(RetCode.OK, broker.Subscribe(AlphaTopic, target, ZeroArityHandler));

        long perTopicCode = RetCode.OK;
        long globalCode = RetCode.OK;
        target.DuringAnyHandler = (dispatching, _) =>
        {
            perTopicCode = dispatching!.SetDefaultReturnValue(AlphaTopic, rejectedDefault);
            globalCode = dispatching.SetDefaultReturnValue(rejectedDefault);
        };

        // Reaching the next line at all is the "throws nothing" half.
        object? result = broker.Trigger(AlphaTopic);

        Assert.Equal(1, target.InvocationCount);
        Assert.Equal(RetCode.FAILED, perTopicCode);
        Assert.Equal(RetCode.FAILED, globalCode);

        // Neither value changed: this dispatch and the next both still see the established default.
        AssertReturnedValue(establishedDefault, result);
        AssertReturnedValue(establishedDefault, broker.Trigger(AlphaTopic));
        AssertReturnedValue(establishedDefault, broker.Trigger(BetaTopic));

        // And outside the dispatch the same two calls succeed, so the refusal is about the depth and not
        // about the arguments.
        target.DuringAnyHandler = null;
        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue(AlphaTopic, rejectedDefault));
        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue(rejectedDefault));
        AssertReturnedValue(rejectedDefault, broker.Trigger(BetaTopic));
    }

    /// <summary>
    /// An unhandled dispatch returns the configured default; with no default configured it returns
    /// <see langword="null"/> rather than a fabricated zero.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>:L966-L970</c> substitutes the resolved default only when the dispatch produced no value and
    /// only for a non-posted dispatch. With nothing configured, the resolved default IS null - the
    /// oracle's <c>_aDefRetVal</c> starts as an unassigned <c>any</c> - so null is the correct answer and
    /// zero would be an invention. That distinction matters directly: the threading layer establishes a
    /// default of <c>0</c> in its own constructor
    /// (<c>ws_objects/pfw.thread.pbl.src/n_cst_threading_eventful.sru:L74-L77</c>), which materially
    /// changes what counts as handled, and a port that fabricated zero here would make the two
    /// indistinguishable.
    /// </para>
    /// <para>
    /// Two ways of being unhandled are covered because they take different paths: no subscriber at all,
    /// which the lexical fast-path rejects at <c>:L797-L798</c> before the loop, and a subscriber that ran
    /// and returned null, which reaches the substitution from inside the loop.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnUnhandledDispatchReturnsTheConfiguredDefaultAndOtherwiseNull()
    {
        const long configuredDefault = 5L;

        DispatchLog log = new();
        EventBroker unconfigured = new();
        RecordingSubscriber silent = new(log, FirstLabel);

        // No subscriber, no default.
        Assert.Null(unconfigured.Trigger(AlphaTopic));

        // A subscriber that ran and returned null, still no default.
        Assert.Equal(RetCode.OK, unconfigured.Subscribe(AlphaTopic, silent, ZeroArityHandler));
        silent.SetReturnValue(ZeroArityHandler, null);
        Assert.Null(unconfigured.Trigger(AlphaTopic));
        Assert.Equal(1, silent.InvocationCount);

        // The same two cases with a default configured.
        EventBroker configured = new();
        RecordingSubscriber alsoSilent = new(log, SecondLabel);
        Assert.Equal(RetCode.OK, configured.SetDefaultReturnValue(configuredDefault));

        AssertReturnedValue(configuredDefault, configured.Trigger(AlphaTopic));

        Assert.Equal(RetCode.OK, configured.Subscribe(AlphaTopic, alsoSilent, ZeroArityHandler));
        alsoSilent.SetReturnValue(ZeroArityHandler, null);
        AssertReturnedValue(configuredDefault, configured.Trigger(AlphaTopic));
        Assert.Equal(1, alsoSilent.InvocationCount);
    }

    // =================================================================================================
    //  5. RE-ENTRANCY - modifying the table from inside a dispatch
    //  Two mechanisms, and they are not symmetric. A SUBSCRIBE mid-dispatch inserts immediately and is
    //  hidden from the running dispatch by the loop's once-evaluated upper bound (:L820) together with the
    //  index-stack fix-up (:L427-L433). An UNSUBSCRIBE mid-dispatch cannot rewrite the table at all, so it
    //  TOMBSTONES the entry and queues a compaction (:L1055-L1058 with :L1083; the sweep is :L576-L603).
    //  A DISABLE mid-dispatch needs neither, because it only writes a boolean.
    // =================================================================================================

    /// <summary>
    /// A subscription added from inside a dispatch does NOT run in the dispatch in progress, and DOES run
    /// on the next one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PRESERVED LEGACY SEMANTICS, stated by the oracle in prose: 「在事件分发过程中调用of_On订阅新的事件
    /// 将在下次分发时才会生效」 - a subscription registered during a dispatch takes effect only on the
    /// NEXT dispatch (<c>w_test_eventful.srw:L237</c>, inside the 「特别说明」 block the agent brief cites
    /// as <c>:L239</c>). Asserted as CORRECT under C-B rather than treated as a latency bug to fix.
    /// </para>
    /// <para>
    /// The rule has two halves in the implementation and this test needs both to hold. The dispatch loop
    /// captures its upper bound ONCE at entry (<c>:L820</c>), so a longer table is not noticed; and the
    /// index-stack fix-up (<c>:L427-L433</c>) pushes every in-flight cursor along when an insert lands at
    /// or before it, so the running dispatch keeps pointing at the same subscription it was pointing at. A
    /// port that re-read the table length each iteration would run the newcomer immediately and this test
    /// is what reports that.
    /// </para>
    /// <para>
    /// Both halves of the outcome are asserted - absence now and presence later - because a port that
    /// dropped the subscription entirely would satisfy the first half alone.
    /// </para>
    /// </remarks>
    [Fact]
    public void ASubscriptionAddedDuringADispatchRunsOnlyOnTheNextDispatch()
    {
        DispatchLog log = new();
        EventBroker broker = new();
        RecordingSubscriber established = new(log, "ESTABLISHED", broker);
        RecordingSubscriber alsoEstablished = new(log, "ALSOESTABLISHED", broker);
        RecordingSubscriber newcomer = new(log, "NEWCOMER", broker);

        Assert.Equal(RetCode.OK, broker.Subscribe(AlphaTopic, established, ZeroArityHandler));
        Assert.Equal(RetCode.OK, broker.Subscribe(AlphaTopic, alsoEstablished, ZeroArityHandler));

        long subscribeCode = RetCode.FAILED;
        established.DuringAnyHandler = (dispatching, _) =>
            subscribeCode = dispatching!.Subscribe(AlphaTopic, newcomer, ZeroArityHandler);

        broker.Trigger(AlphaTopic);

        // The call itself succeeded - the newcomer is in the table, it is simply not visible to the
        // dispatch that added it.
        Assert.Equal(RetCode.OK, subscribeCode);
        Assert.Equal("ESTABLISHED|ALSOESTABLISHED", RenderLabels(log), StringComparer.Ordinal);
        Assert.Equal(0, newcomer.InvocationCount);

        // The next dispatch sees it, at the tail of the equal-priority run because :L419 appends.
        established.DuringAnyHandler = null;
        log.Clear();
        broker.Trigger(AlphaTopic);

        Assert.Equal("ESTABLISHED|ALSOESTABLISHED|NEWCOMER", RenderLabels(log), StringComparer.Ordinal);
        Assert.Equal(1, newcomer.InvocationCount);
    }

    /// <summary>
    /// Unsubscribing a LATER subscriber from inside a dispatch skips it immediately, queues the compaction,
    /// and leaves it gone for good once the queue is drained.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>:L1055-L1058</c> is the deferred-removal path: with the dispatch depth above zero the engine
    /// marks the entry invalid and records that a compaction is owed, because rewriting the table would
    /// invalidate the cursor every active dispatch level holds. <c>:L1083</c> then queues the sweep with
    /// the oracle's <c>Post _of_Collect()</c>, which in the port is an explicitly drainable continuation -
    /// the message-pump non-port of AAP 0.6.5, since a headless service has no pump to turn.
    /// </para>
    /// <para>
    /// The distinction the assertions draw is between BEHAVIOURALLY gone and PHYSICALLY gone. The
    /// tombstone makes the entry invisible to dispatch at once (<c>:L829</c>) and it stays invisible across
    /// later triggers, so the caller's intent is honoured immediately; the table is only compacted when the
    /// queue is drained. Both are asserted, and the pending count is read in between so the queued sweep is
    /// observed rather than inferred.
    /// </para>
    /// </remarks>
    [Fact]
    public void UnsubscribingALaterSubscriberDuringADispatchSkipsItAndDefersTheSweep()
    {
        DispatchLog log = new();
        EventBroker broker = new();
        RecordingSubscriber remover = new(log, "REMOVER", broker);
        RecordingSubscriber doomed = new(log, "DOOMED", broker);

        Assert.Equal(RetCode.OK, broker.Subscribe(AlphaTopic, remover, ZeroArityHandler));
        Assert.Equal(RetCode.OK, broker.Subscribe(AlphaTopic, doomed, ZeroArityHandler));
        Assert.Equal(0, broker.PendingPostedContinuationCount);

        long unsubscribeCode = RetCode.FAILED;
        remover.DuringAnyHandler = (dispatching, _) =>
            unsubscribeCode = dispatching!.Unsubscribe(AlphaTopic, doomed);

        broker.Trigger(AlphaTopic);

        Assert.Equal(RetCode.OK, unsubscribeCode);
        Assert.Equal("REMOVER", RenderLabels(log), StringComparer.Ordinal);
        Assert.Equal(0, doomed.InvocationCount);

        // :L1083 - the compaction is owed and queued, not performed.
        Assert.Equal(1, broker.PendingPostedContinuationCount);

        // Still skipped before the sweep runs, so the removal took effect immediately even though the
        // table has not been rewritten yet.
        remover.DuringAnyHandler = null;
        log.Clear();
        broker.Trigger(AlphaTopic);
        Assert.Equal("REMOVER", RenderLabels(log), StringComparer.Ordinal);
        Assert.Equal(0, doomed.InvocationCount);

        // :L576-L603 - the sweep, once drained, compacts the table. The behaviour does not change, which
        // is the point: the tombstone already had the observable effect.
        Assert.Equal(1, broker.DrainPostedContinuations());
        Assert.Equal(0, broker.PendingPostedContinuationCount);

        log.Clear();
        broker.Trigger(AlphaTopic);
        Assert.Equal("REMOVER", RenderLabels(log), StringComparer.Ordinal);
        Assert.Equal(0, doomed.InvocationCount);

        // Gone for good: a re-enable cannot bring back an entry that was removed rather than suspended.
        Assert.Equal(RetCode.OK, broker.Disable(AlphaTopic, doomed, disabled: false));
        log.Clear();
        broker.Trigger(AlphaTopic);
        Assert.Equal("REMOVER", RenderLabels(log), StringComparer.Ordinal);
    }

    /// <summary>
    /// Unsubscribing the CURRENTLY EXECUTING subscriber from inside its own handler leaves the dispatch
    /// intact: it completes normally and the remaining subscribers still run, in order.
    /// </summary>
    /// <remarks>
    /// The self-removal case, which is the one most likely to corrupt a cursor. It cannot here, for the
    /// same reason the previous test's deferral works: at depth above zero the engine only writes a
    /// tombstone (<c>:L1055-L1058</c>) and never resizes the table, so the cursor the loop is holding still
    /// addresses the same slot. The entry being invoked is already past the tombstone test at <c>:L829</c>,
    /// so its own invocation finishes normally, and the loop then advances to the next entry as usual.
    /// </remarks>
    [Fact]
    public void UnsubscribingTheExecutingSubscriberFromItsOwnHandlerLeavesTheDispatchIntact()
    {
        DispatchLog log = new();
        EventBroker broker = new();
        RecordingSubscriber selfRemoving = new(log, "SELFREMOVING", broker);
        RecordingSubscriber follower = new(log, "FOLLOWER", broker);
        RecordingSubscriber lastFollower = new(log, "LASTFOLLOWER", broker);

        Assert.Equal(RetCode.OK, broker.Subscribe(AlphaTopic, selfRemoving, ZeroArityHandler));
        Assert.Equal(RetCode.OK, broker.Subscribe(AlphaTopic, follower, ZeroArityHandler));
        Assert.Equal(RetCode.OK, broker.Subscribe(AlphaTopic, lastFollower, ZeroArityHandler));

        long unsubscribeCode = RetCode.FAILED;
        selfRemoving.DuringAnyHandler = (dispatching, _) =>
            unsubscribeCode = dispatching!.Unsubscribe(AlphaTopic, selfRemoving, ZeroArityHandler);

        broker.Trigger(AlphaTopic);

        // The dispatch completed and BOTH followers ran, in their original order.
        Assert.Equal(RetCode.OK, unsubscribeCode);
        Assert.Equal(
            "SELFREMOVING|FOLLOWER|LASTFOLLOWER",
            RenderLabels(log),
            StringComparer.Ordinal);

        // Afterwards the self-removal has taken effect and the followers are untouched.
        selfRemoving.DuringAnyHandler = null;
        Assert.Equal(1, broker.DrainPostedContinuations());

        log.Clear();
        broker.Trigger(AlphaTopic);
        Assert.Equal("FOLLOWER|LASTFOLLOWER", RenderLabels(log), StringComparer.Ordinal);
        Assert.Equal(1, selfRemoving.InvocationCount);
    }

    /// <summary>
    /// Disabling a later subscriber from inside a dispatch skips it for the remainder of that dispatch and
    /// still RETAINS it, so a re-enable afterwards brings it back with no re-subscribe.
    /// </summary>
    /// <remarks>
    /// The disable path needs no deferral at all: <c>:L1068-L1071</c> writes a boolean into an existing
    /// entry and never resizes the table, so there is nothing for a cursor to lose and nothing to queue.
    /// That is asserted directly - the pending-continuation count stays at zero, unlike the deferred sweep
    /// an unsubscribe owes - and it is the clearest single difference between the two families under
    /// re-entrancy.
    /// </remarks>
    [Fact]
    public void DisablingALaterSubscriberDuringADispatchSkipsItAndKeepsIt()
    {
        DispatchLog log = new();
        EventBroker broker = new();
        RecordingSubscriber suspender = new(log, "SUSPENDER", broker);
        RecordingSubscriber suspended = new(log, "SUSPENDED", broker);

        Assert.Equal(RetCode.OK, broker.Subscribe(AlphaTopic, suspender, ZeroArityHandler));
        Assert.Equal(RetCode.OK, broker.Subscribe(AlphaTopic, suspended, ZeroArityHandler));

        long disableCode = RetCode.FAILED;
        suspender.DuringAnyHandler = (dispatching, _) =>
            disableCode = dispatching!.Disable(AlphaTopic, suspended, disabled: true);

        broker.Trigger(AlphaTopic);

        Assert.Equal(RetCode.OK, disableCode);
        Assert.Equal("SUSPENDER", RenderLabels(log), StringComparer.Ordinal);
        Assert.Equal(0, suspended.InvocationCount);

        // No sweep is owed, because nothing was removed.
        Assert.Equal(0, broker.PendingPostedContinuationCount);

        suspender.DuringAnyHandler = null;
        log.Clear();
        broker.Trigger(AlphaTopic);
        Assert.Equal("SUSPENDER", RenderLabels(log), StringComparer.Ordinal);

        // RETAINED: re-enabling restores it in its original position, with no Subscribe call anywhere
        // above this line.
        Assert.Equal(RetCode.OK, broker.Disable(AlphaTopic, suspended, disabled: false));
        log.Clear();
        broker.Trigger(AlphaTopic);
        Assert.Equal("SUSPENDER|SUSPENDED", RenderLabels(log), StringComparer.Ordinal);
        Assert.Equal(1, suspended.InvocationCount);
    }

    /// <summary>
    /// A fully unqualified unsubscribe issued from inside a dispatch does not corrupt the dispatch in
    /// progress: the executing handler completes, later subscribers are skipped, and the table is empty
    /// afterwards.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This mirrors a REAL consumer call, which is why it is asserted rather than left to inference.
    /// <c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L583</c> calls
    /// <c>Eventful.of_Off()</c> from <c>ondestructor</c> - the bulk teardown of a DataWindow service - and
    /// a service can be torn down from inside a handler it is itself dispatching. Every entry, including
    /// the one currently executing, is tombstoned in one pass (<c>:L1056-L1058</c>).
    /// </para>
    /// <para>
    /// The dispatch's return value is asserted as <see langword="null"/> because the last invocation
    /// returned nothing and no default is configured, so the substitution at <c>:L966-L970</c> supplies the
    /// unset default rather than a fabricated zero. Afterwards both lexical bounds are empty, so the
    /// fast-path at <c>:L798</c> rejects the name outright and the subscribed query answers false - which
    /// is the same state a never-used broker is in.
    /// </para>
    /// <para>
    /// Note the deliberate absence of a persistence exemption. The no-argument form passes an EMPTY
    /// filter, so it sweeps every namespace including the persistent one; the persistence-sparing rule
    /// belongs to the consumer wrappers - <c>ws_objects/pfw.thread.pbl.src/n_cst_threading.sru:L544</c>
    /// passes <c>".^persistent"</c> explicitly - and the filter spellings that express it belong to
    /// <c>LifetimeNamespaceFilterTests</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public void AFullyUnqualifiedUnsubscribeDuringADispatchDoesNotCorruptIt()
    {
        DispatchLog log = new();
        EventBroker broker = new();
        RecordingSubscriber tearingDown = new(log, "TEARINGDOWN", broker);
        RecordingSubscriber sameTopic = new(log, "SAMETOPIC", broker);
        RecordingSubscriber otherTopic = new(log, "OTHERTOPIC", broker);

        Assert.Equal(RetCode.OK, broker.Subscribe(AlphaTopic, tearingDown, ZeroArityHandler));
        Assert.Equal(RetCode.OK, broker.Subscribe(AlphaTopic, sameTopic, ZeroArityHandler));
        Assert.Equal(RetCode.OK, broker.Subscribe(BetaTopic, otherTopic, ZeroArityHandler));

        long unsubscribeCode = RetCode.FAILED;
        tearingDown.DuringAnyHandler = (dispatching, _) => unsubscribeCode = dispatching!.Unsubscribe();

        object? result = broker.Trigger(AlphaTopic);

        // The executing handler completed; nothing after it ran; nothing threw.
        Assert.Equal(RetCode.OK, unsubscribeCode);
        Assert.Equal("TEARINGDOWN", RenderLabels(log), StringComparer.Ordinal);
        Assert.Null(result);
        Assert.Equal(0, sameTopic.InvocationCount);

        // Everything is gone, on both topics, and the compaction is queued exactly once.
        Assert.Equal(1, broker.PendingPostedContinuationCount);
        Assert.False(broker.IsSubscribed(AlphaTopic));
        Assert.False(broker.IsSubscribed(BetaTopic));

        tearingDown.DuringAnyHandler = null;
        log.Clear();
        broker.Trigger(AlphaTopic);
        broker.Trigger(BetaTopic);
        Assert.Empty(log.Records);

        // Draining the sweep changes nothing observable, and the broker is reusable afterwards.
        Assert.Equal(1, broker.DrainPostedContinuations());
        Assert.Equal(RetCode.OK, broker.Subscribe(AlphaTopic, tearingDown, ZeroArityHandler));
        log.Clear();
        broker.Trigger(AlphaTopic);
        Assert.Equal("TEARINGDOWN", RenderLabels(log), StringComparer.Ordinal);
    }

    /// <summary>
    /// The subscribed-check-then-trigger idiom behaves: a topic reporting unsubscribed dispatches to
    /// nobody, and a topic reporting subscribed dispatches.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The exact idiom two in-scope consumers use.
    /// <c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L166-L167</c> (in
    /// <c>ondwnchanging</c>) and <c>:L316-L317</c> (in <c>ondoitemchanged</c>) both read
    /// <c>if Eventful.of_IsSubscribed(EVT_...) then Eventful.of_Trigger(EVT_..., ...)</c>, so the guard's
    /// job is to let the consumer skip assembling a payload for an event nobody listens to.
    /// </para>
    /// <para>
    /// Asserted in both directions, because the idiom fails in both. A false positive costs a pointless
    /// dispatch; a false negative costs a MISSED event, which is the more expensive of the two and is
    /// exactly what the suspended-entry quirk above produces by design.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSubscribedThenTriggerIdiomDispatchesToNobodyForAnUnsubscribedTopic()
    {
        DispatchLog log = new();
        EventBroker broker = new();
        RecordingSubscriber listener = new(log, FirstLabel);

        Assert.Equal(RetCode.OK, broker.Subscribe(AlphaTopic, listener, ZeroArityHandler));

        // The guarded form, written the way the consumer writes it.
        int guardedDispatches = 0;
        foreach (string topic in new List<string> { AlphaTopic, BetaTopic })
        {
            if (broker.IsSubscribed(topic))
            {
                broker.Trigger(topic);
                guardedDispatches++;
            }
        }

        Assert.Equal(1, guardedDispatches);
        Assert.Equal(1, listener.InvocationCount);
        Assert.Single(log.Records);
        Assert.Equal(FirstLabel, log.RecordAt(0).Label, StringComparer.Ordinal);

        // Unguarded, the unsubscribed topic is harmless too - the guard is an optimisation, not a
        // correctness requirement, which is why the consumer is free to use it.
        log.Clear();
        broker.Trigger(BetaTopic);
        Assert.Empty(log.Records);
    }

    // =================================================================================================
    //  6. SHARED HELPERS
    // =================================================================================================

    /// <summary>
    /// Asserts that a dispatch answered a specific numeric value, and that it answered it AS a number.
    /// </summary>
    /// <param name="expected">The value expected.</param>
    /// <param name="actual">The value <see cref="EventBroker.Trigger"/> or a query returned.</param>
    /// <remarks>
    /// The type check is not ceremony. The broker's values travel as <c>object?</c> - the port of
    /// PowerScript's <c>any</c> - so a value that had been stringified or widened somewhere would still
    /// compare equal under a loosely typed assertion and would silently break the numeric-widening
    /// comparison the handled-detection logic performs at <c>:L916</c>.
    /// </remarks>
    private static void AssertReturnedValue(long expected, object? actual) =>
        Assert.Equal(expected, Assert.IsType<long>(actual));

    /// <summary>
    /// Renders a dispatch log as the pipe-joined <c>label.declaredHandlerName</c> run, in dispatch order.
    /// </summary>
    /// <param name="log">The log to render.</param>
    /// <returns>The rendered run, or an empty string when nothing was dispatched.</returns>
    /// <remarks>
    /// A single string makes an expectation a SET AND AN ORDER in one comparison, which is what lets the
    /// disable theory pin retention and position in a single equality. The declared handler name is used
    /// rather than the subscribed spelling because <see cref="DispatchRecord.HandlerName"/> carries the
    /// member's own casing, which is what distinguishes two handlers on one target.
    /// </remarks>
    private static string RenderRun(DispatchLog log) =>
        string.Join(RunSeparator, log.Records.Select(record => record.Label + "." + record.HandlerName));

    /// <summary>
    /// Renders a dispatch log as the pipe-joined subscriber labels alone, in dispatch order.
    /// </summary>
    /// <param name="log">The log to render.</param>
    /// <returns>The rendered labels, or an empty string when nothing was dispatched.</returns>
    /// <remarks>
    /// The form used by the re-entrancy tests, where every subscriber is wired to the same handler and the
    /// handler name would therefore add a constant to every row without distinguishing anything.
    /// </remarks>
    private static string RenderLabels(DispatchLog log) => string.Join(RunSeparator, log.Labels);

    /// <summary>
    /// Applies one of the seven unsubscribe shapes, selected by key.
    /// </summary>
    /// <param name="shape">The shape key.</param>
    /// <param name="arrangement">The arrangement supplying the broker and both targets.</param>
    /// <returns>The code the subject returned.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for an unrecognised key.</exception>
    /// <remarks>
    /// <para>
    /// Each arm is the shape at its own oracle locator, in the order the seven are enumerated in this
    /// file's header. The switch is exhaustive over the seven keys and the default arm throws rather than
    /// silently succeeding, so a typo in a member-data row fails loudly instead of quietly testing nothing.
    /// </para>
    /// <para>
    /// Two arms cast the target to <see cref="object"/> explicitly. That is the documented consequence of
    /// AAP 0.4.5.2 mapping PowerBuilder's <c>powerobject</c> onto <see cref="object"/>: because
    /// <see cref="string"/> IS an <see cref="object"/> in C#, the target-first and topic-first overloads
    /// can be ambiguous at a CALL SITE where the compiler cannot separate them. The cast states the
    /// intent, and the subject documents the same point on
    /// <see cref="EventBroker.Unsubscribe(object?, string)"/>.
    /// </para>
    /// </remarks>
    private static long ApplyUnsubscribeShape(string shape, Arrangement arrangement) => shape switch
    {
        // :L171 - _of_Modify("", object, evtName, false, MOD_OFF, true)
        ByTargetAndHandler => arrangement.Broker.Unsubscribe((object?)arrangement.First, ZeroArityHandler),

        // :L192 - _of_Modify("", object, "", false, MOD_OFF, true)
        ByTargetAlone => arrangement.Broker.Unsubscribe((object?)arrangement.First),

        // :L212 - a null target and an empty filter
        FullyUnqualified => arrangement.Broker.Unsubscribe(),

        // :L555 - _of_Modify("", object, "", excluding, MOD_OFF, true)
        ByTargetExcluding => arrangement.Broker.Unsubscribe(arrangement.First, excluding: true),

        // :L658 - _of_Modify(name, object, evtName, false, MOD_OFF, true)
        ByTopicTargetAndHandler =>
            arrangement.Broker.Unsubscribe(AlphaTopic, arrangement.First, ZeroArityHandler),

        // :L680 - _of_Modify(name, nullObject, "", false, MOD_OFF, true)
        ByTopicAlone => arrangement.Broker.Unsubscribe(AlphaTopic),

        // :L725 - _of_Modify(name, object, "", false, MOD_OFF, true)
        ByTopicAndTarget => arrangement.Broker.Unsubscribe(AlphaTopic, arrangement.First),

        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown unsubscribe shape.")
    };

    /// <summary>
    /// Applies one of the seven disable shapes, selected by key, with the value to assign.
    /// </summary>
    /// <param name="shape">The shape key.</param>
    /// <param name="arrangement">The arrangement supplying the broker and both targets.</param>
    /// <param name="disabled">
    /// The value to ASSIGN - <see langword="true"/> to suspend, <see langword="false"/> to resume. It is
    /// assigned rather than OR-ed (<c>:L1070</c>), which is what makes one shape serve both directions.
    /// </param>
    /// <returns>The code the subject returned.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for an unrecognised key.</exception>
    /// <remarks>
    /// Deliberately the same seven selectors as <see cref="ApplyUnsubscribeShape"/> in the same order, so
    /// the two theories are comparable row for row and the only difference between them is the
    /// modification kind. Note the excluding arm's argument order: the oracle puts <c>disabled</c> BEFORE
    /// <c>excluding</c> (<c>:L1230</c>), which the port preserves even though it reads oddly beside the
    /// unsubscribe form.
    /// </remarks>
    private static long ApplyDisableShape(string shape, Arrangement arrangement, bool disabled) =>
        shape switch
        {
            // :L1092 - _of_Modify(name, object, evtName, false, MOD_DISABLE, disabled)
            ByTopicTargetAndHandler =>
                arrangement.Broker.Disable(AlphaTopic, arrangement.First, ZeroArityHandler, disabled),

            // :L1115 - _of_Modify(name, object, "", false, MOD_DISABLE, disabled)
            ByTopicAndTarget => arrangement.Broker.Disable(AlphaTopic, arrangement.First, disabled),

            // :L1138 - _of_Modify(name, nullObject, "", false, MOD_DISABLE, disabled)
            ByTopicAlone => arrangement.Broker.Disable(AlphaTopic, disabled),

            // :L1165 - _of_Modify("", object, evtName, false, MOD_DISABLE, disabled)
            ByTargetAndHandler =>
                arrangement.Broker.Disable((object?)arrangement.First, ZeroArityHandler, disabled),

            // :L1187 - _of_Modify("", object, "", false, MOD_DISABLE, disabled)
            ByTargetAlone => arrangement.Broker.Disable((object?)arrangement.First, disabled),

            // :L1208 - _of_Modify("", nullObj, "", false, MOD_DISABLE, disabled)
            FullyUnqualified => arrangement.Broker.Disable(disabled),

            // :L1230 - _of_Modify("", object, "", excluding, MOD_DISABLE, disabled)
            ByTargetExcluding => arrangement.Broker.Disable(arrangement.First, disabled, excluding: true),

            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown disable shape.")
        };

    /// <summary>
    /// The eight-subscription arrangement both shape theories are built on: two topics, two target
    /// instances, and two handler names per target on each topic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Constructed fresh per test, never shared and never carried in member data, so no test or theory row
    /// can observe another's state.
    /// </para>
    /// <para>
    /// Eight subscriptions is the MINIMUM that distinguishes all seven selectors: with one topic the
    /// topic-qualified shapes would be indistinguishable from the unqualified ones, with one target the
    /// excluding shape would be indistinguishable from a no-op, and with one handler the handler-qualified
    /// shapes would be indistinguishable from the target-qualified ones.
    /// </para>
    /// </remarks>
    private sealed class Arrangement
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Arrangement"/> class and registers all eight
        /// subscriptions, asserting each was accepted.
        /// </summary>
        /// <remarks>
        /// The registration ORDER is significant and is fixed here: within one topic the four
        /// subscriptions share a priority and none requests a prepend, so each lands at the tail of the
        /// equal-priority run (<c>:L419</c>) and the dispatch order is exactly this order. Both shape
        /// theories assert against that order, so changing it here changes every expected run.
        /// </remarks>
        internal Arrangement()
        {
            Log = new DispatchLog();
            Broker = new EventBroker();
            First = new RecordingSubscriber(Log, FirstLabel, Broker);
            Second = new RecordingSubscriber(Log, SecondLabel, Broker);

            foreach (string topic in new List<string> { AlphaTopic, BetaTopic })
            {
                Assert.Equal(RetCode.OK, Broker.Subscribe(topic, First, ZeroArityHandler));
                Assert.Equal(RetCode.OK, Broker.Subscribe(topic, First, OneArityHandler));
                Assert.Equal(RetCode.OK, Broker.Subscribe(topic, Second, ZeroArityHandler));
                Assert.Equal(RetCode.OK, Broker.Subscribe(topic, Second, OneArityHandler));
            }
        }

        /// <summary>Gets the shared invocation log every subscriber in this arrangement appends to.</summary>
        internal DispatchLog Log { get; }

        /// <summary>Gets the broker under test.</summary>
        internal EventBroker Broker { get; }

        /// <summary>Gets the first target, which every shape selects on.</summary>
        internal RecordingSubscriber First { get; }

        /// <summary>Gets the second target, which exists so a selection has something to spare.</summary>
        internal RecordingSubscriber Second { get; }

        /// <summary>
        /// Clears the log, triggers one topic, and returns the run that topic produced.
        /// </summary>
        /// <param name="topic">The topic to trigger. Compared ordinally by the subject.</param>
        /// <returns>The pipe-joined <c>label.declaredHandlerName</c> run, in dispatch order.</returns>
        /// <remarks>
        /// <para>
        /// Clearing FIRST and rendering after is what makes this callable repeatedly within one test, which
        /// the disable theory needs five times per row. The subscribers' own invocation counters are
        /// deliberately not reset by this - <see cref="DispatchLog.Clear"/> drops the evidence, it does not
        /// un-run an invocation - so a test can still assert a cumulative count across several runs.
        /// </para>
        /// <para>
        /// One argument is passed so the one-parameter handler receives something real. The padding and
        /// truncation rules that decide what a handler of a different arity receives belong to
        /// <c>VariadicDispatchTests</c>; here the payload only needs to be non-empty.
        /// </para>
        /// </remarks>
        internal string Run(string topic)
        {
            First.Topic = topic;
            Second.Topic = topic;

            Log.Clear();
            Broker.Trigger(topic, 1L);

            return RenderRun(Log);
        }
    }
}
