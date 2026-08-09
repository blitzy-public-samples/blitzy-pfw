// ==================================================================================================
//  EventBrokerTests.cs - THE 1,328-LINE PURE-POWERSCRIPT EVENT BROKER
//  ------------------------------------------------------------------------------------------------
//  UNIT UNDER TEST   PowerFramework.Shared.Eventful.EventBroker (+ EventSubscription,
//                    DefaultReturnValue, EventArgumentContext, IAssertionDetail)
//  ORACLE            ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru   1,328 lines, NOT native
//  CONSUMERS         ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L6-L7,L33  an instance member
//                    ws_objects/pfw.thread.pbl.src/n_cst_threading.sru           inherited by
//                                                                                n_cst_threading_eventful
//
//  WHY THIS OBJECT IS IN THE PHASE-1 SLICE AT ALL
//  ------------------------------------------------------------------------------------------------
//  It is an instance member of se_cst_dw and the ancestor of n_cst_threading_eventful - two independent
//  proofs that both in-scope services need it - and it declares no PBNI binding, so it ports as pure
//  logic with no native substitution problem. The whole DataWindow event chain runs through it.
//
//  THE FIVE BEHAVIOURS A NAIVE PORT GETS WRONG
//  ------------------------------------------------------------------------------------------------
//  1. ARGUMENT VALIDATION ORDER. Subscribe tests the NAME and the HANDLER NAME before it tests the
//     TARGET, so Subscribe("", null, "") answers E_INVALID_ARGUMENT and NOT E_INVALID_OBJECT. A reader
//     who validates the object first - the more usual instinct - changes the code a caller receives.
//
//  2. DISPATCH ORDER IS A SORTED INSERTION, NOT A SORT AT DISPATCH. Subscriptions are kept ordinally by
//     name and, within a name, by DESCENDING priority - and the insertion point is chosen so that equal
//     priorities are appended, unless the topic carried '-' (prepend). The dispatch loop then walks
//     forwards and BREAKS as soon as it leaves the name's run. Getting the insertion wrong reorders
//     handlers silently.
//
//  3. CAPTURE GATING IS A STATE MACHINE, NOT A FLAG. An event starts UNHANDLED. A handler that returns
//     a non-null value differing from the established default flips the state to HANDLED, and from that
//     moment UNHANDLED subscribers are SKIPPED and HANDLED ones start running. '*' subscribers run in
//     both states. So a handler returning a value silently changes which later subscribers execute.
//
//  4. THE RETURN VALUE IS SAVED AND RESTORED AROUND EVERY DISPATCH. _returnValue is pushed on entry and
//     popped in the finally, so GetReturnValue() and IsProcessed() are only meaningful FROM INSIDE a
//     handler. Read from outside they report the pre-dispatch state - which is what makes them safe
//     under nesting, and what makes a test that reads them after Trigger meaningless.
//
//  5. PREVENTION IS TRI-VALUED AND ITS SCOPE IS THE POINT. PreventOnce is cleared as the dispatch
//     unwinds; PreventDeep survives into the enclosing dispatch. Prevent() called OUTSIDE any dispatch
//     answers RetCode.FAILED rather than throwing.
//
//  RULES POSITION
//  review_rules returns "No user rules provided.". Constraints cited inline: C-B (replicate behaviour
//  including defects), AAP 0.6.1 (event ordering is the highest-risk area), C-K (document decisions).
// ==================================================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.Shared.Eventful.Tests;

/// <summary>
/// Characterization tests for <see cref="EventBroker"/>.
/// </summary>
public class EventBrokerTests
{
    // ==============================================================================================
    //  TEST TARGETS
    // ==============================================================================================

    /// <summary>
    /// A handler target that records the order in which its handlers ran and answers a configured value.
    /// </summary>
    private sealed class Recorder
    {
        /// <summary>Initializes a new instance of the <see cref="Recorder"/> class.</summary>
        /// <param name="label">The label this target writes into the shared log.</param>
        /// <param name="log">The shared ordering log.</param>
        /// <param name="answer">The value its handlers return.</param>
        internal Recorder(string label, List<string> log, object? answer = null)
        {
            Label = label;
            Log = log;
            Answer = answer;
        }

        /// <summary>Gets the label written to the log.</summary>
        internal string Label { get; }

        /// <summary>Gets the shared ordering log.</summary>
        internal List<string> Log { get; }

        /// <summary>Gets or sets the value the handlers return.</summary>
        internal object? Answer { get; set; }

        /// <summary>Gets the number of times <see cref="OnEvent"/> ran.</summary>
        internal int CallCount { get; private set; }

        /// <summary>Gets or sets the arguments the last invocation received.</summary>
        internal object?[]? LastArguments { get; private set; }

        /// <summary>A no-argument handler.</summary>
        /// <returns>The configured answer.</returns>
        public object? OnEvent()
        {
            CallCount++;
            LastArguments = [];
            Log.Add(Label);
            return Answer;
        }

        /// <summary>A handler taking two arguments, used to exercise argument passing.</summary>
        /// <param name="first">The first argument.</param>
        /// <param name="second">The second argument.</param>
        /// <returns>The configured answer.</returns>
        public object? OnTwoArguments(long first, string? second)
        {
            LastArguments = [first, second];
            Log.Add($"{Label}({first},{second})");
            return Answer;
        }

        /// <summary>A handler that always throws.</summary>
        /// <returns>Never returns.</returns>
        public object? OnThrowing()
        {
            Log.Add($"{Label}:throw");
            throw new InvalidOperationException("handler blew up");
        }
    }

    /// <summary>
    /// A target whose handler vetoes the dispatch it is running inside.
    /// </summary>
    private sealed class Vetoer
    {
        /// <summary>Initializes a new instance of the <see cref="Vetoer"/> class.</summary>
        /// <param name="label">The label written to the log.</param>
        /// <param name="log">The shared ordering log.</param>
        /// <param name="broker">The broker to veto on.</param>
        /// <param name="deep">Whether the veto is deep.</param>
        internal Vetoer(string label, List<string> log, EventBroker broker, bool deep)
        {
            Label = label;
            Log = log;
            Broker = broker;
            Deep = deep;
        }

        /// <summary>Gets the label written to the log.</summary>
        internal string Label { get; }

        /// <summary>Gets the shared ordering log.</summary>
        internal List<string> Log { get; }

        /// <summary>Gets the broker to veto on.</summary>
        internal EventBroker Broker { get; }

        /// <summary>Gets a value indicating whether the veto is deep.</summary>
        internal bool Deep { get; }

        /// <summary>Gets the code the veto call answered.</summary>
        internal long PreventCode { get; private set; }

        /// <summary>Vetoes the running dispatch.</summary>
        /// <returns>Always null, so the veto does not also mark the event handled.</returns>
        public object? OnEvent()
        {
            Log.Add(Label);
            PreventCode = Broker.Prevent(Deep);
            return null;
        }
    }

    /// <summary>
    /// A target that observes broker state from INSIDE a dispatch, which is the only place it is
    /// meaningful.
    /// </summary>
    private sealed class Observer
    {
        /// <summary>Initializes a new instance of the <see cref="Observer"/> class.</summary>
        /// <param name="broker">The broker to observe.</param>
        internal Observer(EventBroker broker) => Broker = broker;

        /// <summary>Gets the observed broker.</summary>
        internal EventBroker Broker { get; }

        /// <summary>Gets the ambient broker seen during dispatch.</summary>
        internal EventBroker? SeenCurrent { get; private set; }

        /// <summary>Gets the post flag seen during dispatch.</summary>
        internal bool SeenIsPost { get; private set; }

        /// <summary>Gets the processed flag seen during dispatch.</summary>
        internal bool SeenIsProcessed { get; private set; }

        /// <summary>Gets the return value seen during dispatch.</summary>
        internal object? SeenReturnValue { get; private set; }

        /// <summary>Gets the code a veto attempt answered during dispatch.</summary>
        internal long SeenPreventCode { get; private set; }

        /// <summary>Records broker state, then undoes its own veto by reporting only the code.</summary>
        /// <returns>Always null.</returns>
        public object? OnObserve()
        {
            SeenCurrent = EventBroker.Current;
            SeenIsPost = Broker.IsPost();
            SeenIsProcessed = Broker.IsProcessed();
            SeenReturnValue = Broker.GetReturnValue();
            SeenPreventCode = RetCode.OK;
            return null;
        }

        /// <summary>Records only whether a veto is permitted from here.</summary>
        /// <returns>Always null.</returns>
        public object? OnProbeVeto()
        {
            SeenPreventCode = Broker.Prevent(deep: false);

            // Undo it so the probe does not change which later subscribers run: re-entering Continue is
            // not exposed, so the probe is used only in single-subscriber dispatches.
            return null;
        }
    }

    /// <summary>
    /// A target offering two overloads of one handler name, to exercise resolution.
    /// </summary>
    private sealed class Overloaded
    {
        /// <summary>Gets the parameter count of the overload that ran.</summary>
        internal int ChosenArity { get; private set; } = -1;

        /// <summary>The zero-argument overload.</summary>
        /// <returns>Always null.</returns>
        public object? OnEvent()
        {
            ChosenArity = 0;
            return null;
        }

        /// <summary>The two-argument overload.</summary>
        /// <param name="first">The first argument.</param>
        /// <param name="second">The second argument.</param>
        /// <returns>Always null.</returns>
        public object? OnEvent(long first, string? second)
        {
            ChosenArity = 2;
            _ = first;
            _ = second;
            return null;
        }
    }

    /// <summary>
    /// A derived broker, so the four protected hooks become live.
    /// </summary>
    /// <remarks>
    /// The base constructor sets its subclassing flag by comparing <c>GetType()</c> against
    /// <see cref="EventBroker"/>, so the hooks are dead on the base type and live on any derived one.
    /// That is the mechanism a test has to go through to reach them.
    /// </remarks>
    private sealed class HookedBroker : EventBroker
    {
        /// <summary>Gets the names passed to the triggering hook.</summary>
        internal List<string> Triggering { get; } = [];

        /// <summary>Gets the names passed to the triggered hook.</summary>
        internal List<string> Triggered { get; } = [];

        /// <summary>Gets the names passed to the exception hook.</summary>
        internal List<string> Exceptions { get; } = [];

        /// <summary>Gets the names passed to the prepare hook.</summary>
        internal List<string> Prepared { get; } = [];

        /// <summary>Gets or sets the value the triggering hook returns.</summary>
        internal long TriggeringResult { get; set; } = RetCode.OK;

        /// <summary>Gets or sets the value the exception hook returns.</summary>
        internal long ExceptionResult { get; set; }

        /// <summary>Gets or sets the value the prepare hook returns.</summary>
        internal long PrepareResult { get; set; } = RetCode.OK;

        /// <summary>Gets or sets the number of argument slots the prepare hook claims.</summary>
        internal int PrepareConsumes { get; set; }

        /// <summary>Gets or sets a value the prepare hook writes into slot one.</summary>
        internal object? PrepareSlotOneValue { get; set; }

        /// <inheritdoc/>
        protected override long OnTriggering(string name, bool isPost)
        {
            Triggering.Add(name);
            return TriggeringResult;
        }

        /// <inheritdoc/>
        protected override void OnTriggered(string name, bool isPost) => Triggered.Add(name);

        /// <inheritdoc/>
        protected override long OnException(string name, Exception exception)
        {
            Exceptions.Add(name);
            return ExceptionResult;
        }

        /// <inheritdoc/>
        protected override long OnPrepare(string name, object target, EventArgumentContext arguments)
        {
            Prepared.Add($"{name}/{arguments.DeclaredArgumentCount}");

            if (PrepareSlotOneValue is not null)
            {
                arguments.SetArgument(1, PrepareSlotOneValue);
            }

            arguments.ConsumedArgumentCount = PrepareConsumes;
            return PrepareResult;
        }
    }

    // ==============================================================================================
    //  1. CONSTRUCTION AND EMPTY STATE
    // ==============================================================================================

    /// <summary>
    /// A new broker has no subscriptions, no pending continuations, no return value and reports nothing
    /// subscribed.
    /// </summary>
    /// <remarks>
    /// The empty state is reachable and has to be inert: <c>Trigger</c> on an empty broker must answer
    /// without touching anything, because the DataWindow chain triggers topics that frequently have no
    /// subscriber at all [se_cst_dw.sru guards each trigger with a subscription test].
    /// </remarks>
    [Fact]
    public void ANewBrokerIsEmptyAndInert()
    {
        EventBroker broker = new();

        Assert.Equal(0, broker.PendingPostedContinuationCount);
        Assert.Null(broker.GetReturnValue());
        Assert.False(broker.IsProcessed());
        Assert.False(broker.IsPost());
        Assert.False(broker.IsSubscribed("anything"));
        Assert.Null(broker.Trigger("anything"));
    }

    /// <summary>
    /// <see cref="EventBroker.Current"/> is null outside any dispatch.
    /// </summary>
    /// <remarks>
    /// The ambient broker exists so a handler can reach the broker that invoked it without being handed
    /// one. It must be null outside a dispatch, or a handler could not tell whether it is running under
    /// the broker at all - and code that assumed otherwise would use a stale broker from an earlier
    /// dispatch on the same flow.
    /// </remarks>
    [Fact]
    public void TheAmbientBrokerIsNullOutsideADispatch()
    {
        Assert.Null(EventBroker.Current);
    }

    // ==============================================================================================
    //  2. SUBSCRIBE VALIDATION ORDER
    // ==============================================================================================

    /// <summary>
    /// THE ORDER: an empty name or handler name answers <c>E_INVALID_ARGUMENT</c> even when the TARGET is
    /// also invalid.
    /// </summary>
    /// <param name="name">The topic name.</param>
    /// <param name="handlerName">The handler name.</param>
    /// <remarks>
    /// <para>
    /// The rows pass a NULL target deliberately. Both faults are present, and the code that comes back
    /// tells you which check ran first - so this is the only way to pin the order from outside. The
    /// legacy tests the strings first, and a port that validated the object first would answer
    /// <c>E_INVALID_OBJECT</c> for every row here.
    /// </para>
    /// <para>
    /// It matters because a caller branching on the code would misdiagnose the fault: an invalid-object
    /// code sends a developer looking at object lifetime when the real problem is an empty topic string.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("", "OnEvent")]
    [InlineData("name", "")]
    [InlineData("", "")]
    public void AnEmptyNameOrHandlerIsRejectedBeforeTheTargetIsChecked(string name, string handlerName)
    {
        EventBroker broker = new();

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, broker.Subscribe(name, null, handlerName));
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, broker.Subscribe(name, null, handlerName, "signature"));
    }

    /// <summary>
    /// A null target with valid strings answers <c>E_INVALID_OBJECT</c>.
    /// </summary>
    /// <remarks>
    /// The complement of the order test: with the string checks satisfied, the object check is reached and
    /// reports its own code. Together the two tests establish that both checks exist AND which runs first.
    /// </remarks>
    [Fact]
    public void ANullTargetWithValidStringsIsRejectedAsAnInvalidObject()
    {
        EventBroker broker = new();

        Assert.Equal(RetCode.E_INVALID_OBJECT, broker.Subscribe("name", null, "OnEvent"));
    }

    /// <summary>
    /// A malformed topic string propagates the topic parser's own code rather than a generic failure.
    /// </summary>
    /// <param name="topic">A topic the subscription grammar rejects.</param>
    /// <remarks>
    /// The broker forwards the parse result unchanged, so a caller receives the same diagnosis the parser
    /// produced. Worth pinning because collapsing it to a single broker-level code would lose the
    /// distinction between "the object was wrong" and "the topic was wrong", which are fixed in different
    /// places.
    /// </remarks>
    [Theory]
    [InlineData("--name")]
    [InlineData("!100:name")]
    [InlineData("name.")]
    [InlineData("-")]
    public void AMalformedTopicPropagatesTheParserCode(string topic)
    {
        EventBroker broker = new();
        Recorder target = new("t", []);

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, broker.Subscribe(topic, target, nameof(Recorder.OnEvent)));
        Assert.False(broker.IsSubscribed(topic));
    }

    /// <summary>
    /// A handler name that resolves to no method answers <c>E_EVENT_NOT_FOUND</c>.
    /// </summary>
    /// <remarks>
    /// The third distinct failure code, and the one that catches a typo in a handler name. Because
    /// PowerScript resolves handlers by name at runtime, this is the only place such a typo can be
    /// detected - so the code has to be specific enough to point at the handler rather than the topic.
    /// </remarks>
    [Fact]
    public void AnUnresolvableHandlerAnswersEventNotFound()
    {
        EventBroker broker = new();
        Recorder target = new("t", []);

        Assert.Equal(RetCode.E_EVENT_NOT_FOUND, broker.Subscribe("name", target, "NoSuchHandler"));
    }

    /// <summary>
    /// A valid subscription answers <c>OK</c> and becomes visible to
    /// <see cref="EventBroker.IsSubscribed"/>.
    /// </summary>
    /// <remarks>
    /// The success path, and the baseline every later test builds on. Both the code and the visibility are
    /// asserted because they are separate mechanisms - the code comes from the registration, the
    /// visibility from the first-and-last-name bookkeeping the broker maintains alongside the list.
    /// </remarks>
    [Fact]
    public void AValidSubscriptionSucceedsAndBecomesVisible()
    {
        EventBroker broker = new();
        Recorder target = new("t", []);

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(Recorder.OnEvent)));
        Assert.True(broker.IsSubscribed("name"));
        Assert.False(broker.IsSubscribed("other"));
    }

    /// <summary>
    /// A handler name is matched CASE-INSENSITIVELY, matching PowerScript's identifier rules.
    /// </summary>
    /// <param name="handlerName">The handler name as written by the caller.</param>
    /// <remarks>
    /// PowerScript does not distinguish case in identifiers, so a subscription written with any casing
    /// must find the method. The broker folds the name and resolves ignoring case; both halves are needed,
    /// and this test covers the resolution half by subscribing with each casing and then dispatching.
    /// </remarks>
    [Theory]
    [InlineData("OnEvent")]
    [InlineData("onevent")]
    [InlineData("ONEVENT")]
    [InlineData("oNeVeNt")]
    public void AHandlerNameIsMatchedCaseInsensitively(string handlerName)
    {
        List<string> log = [];
        EventBroker broker = new();
        Recorder target = new("t", log);

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, handlerName));

        broker.Trigger("name");

        Assert.Equal(["t"], log);
    }

    /// <summary>
    /// With no signature supplied, the overload with the FEWEST parameters is chosen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The legacy resolves a handler by name alone, so an overload set needs a tie-break, and the rule is
    /// fewest parameters. That is the conservative choice: a zero-argument handler can be invoked whatever
    /// the trigger supplies, whereas a wider one would receive defaulted values for arguments the caller
    /// never sent.
    /// </para>
    /// <para>
    /// The explicit-signature form is the escape hatch, and it is asserted next to the default so the two
    /// read as a pair.
    /// </para>
    /// </remarks>
    [Fact]
    public void WithNoSignatureTheNarrowestOverloadIsChosen()
    {
        EventBroker broker = new();
        Overloaded target = new();

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(Overloaded.OnEvent)));

        broker.Trigger("name", 1L, "two");

        Assert.Equal(0, target.ChosenArity);
    }

    /// <summary>
    /// An explicit handler signature selects a specific overload, and a signature matching none answers
    /// <c>E_EVENT_NOT_FOUND</c>.
    /// </summary>
    /// <remarks>
    /// The four-argument <c>Subscribe</c> exists for exactly this: reaching an overload the narrowest-wins
    /// rule would otherwise hide. Asserting the no-match case too pins that an unmatched signature is a
    /// failure rather than a silent fall back to the narrowest overload - which would be the more
    /// forgiving behaviour and would hide the caller's mistake.
    /// </remarks>
    [Fact]
    public void AnExplicitSignatureSelectsAnOverloadAndAnUnmatchedOneFails()
    {
        EventBroker broker = new();
        Overloaded selected = new();

        Assert.Equal(
            RetCode.OK,
            broker.Subscribe("name", selected, nameof(Overloaded.OnEvent), "long,string"));

        broker.Trigger("name", 7L, "seven");
        Assert.Equal(2, selected.ChosenArity);

        EventBroker other = new();
        Overloaded unmatched = new();

        Assert.Equal(
            RetCode.E_EVENT_NOT_FOUND,
            other.Subscribe("name", unmatched, nameof(Overloaded.OnEvent), "decimal,decimal,decimal"));
    }

    // ==============================================================================================
    //  3. DISPATCH ORDER
    // ==============================================================================================

    /// <summary>
    /// Subscriptions dispatch in ORDINAL name order regardless of the order they were registered in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the mechanism behind the framework's own ordering prefixes: <c>se_cst_dw.sru</c> spells its
    /// item-changed topic <c>"0-..."</c> and its edit-changed topic <c>"1-..."</c> precisely so that a
    /// lexical name sort runs them in that order. Registering them backwards must not change the outcome.
    /// </para>
    /// <para>
    /// Note the two topics are dispatched by separate <c>Trigger</c> calls here - one per name - because
    /// dispatch selects one name's run. The ORDER assertion is therefore about where each subscription
    /// SITS in the list, which the interleaved third registration demonstrates.
    /// </para>
    /// </remarks>
    [Fact]
    public void SubscriptionsAreOrderedOrdinallyByNameNotByRegistrationOrder()
    {
        List<string> log = [];
        EventBroker broker = new();

        // Registered in reverse name order.
        Assert.Equal(RetCode.OK, broker.Subscribe("2-third", new Recorder("third", log), nameof(Recorder.OnEvent)));
        Assert.Equal(RetCode.OK, broker.Subscribe("0-first", new Recorder("first", log), nameof(Recorder.OnEvent)));
        Assert.Equal(RetCode.OK, broker.Subscribe("1-second", new Recorder("second", log), nameof(Recorder.OnEvent)));

        broker.Trigger("0-first");
        broker.Trigger("1-second");
        broker.Trigger("2-third");

        Assert.Equal(["first", "second", "third"], log);
    }

    /// <summary>
    /// Within one name, a HIGHER priority dispatches EARLIER.
    /// </summary>
    /// <remarks>
    /// The direction is the opposite of a natural numeric sort, which is exactly why it needs pinning: an
    /// ascending sort would run the lowest-priority subscriber first and every subscriber would still run,
    /// so nothing but an ordering assertion would notice.
    /// </remarks>
    [Fact]
    public void WithinOneNameHigherPriorityDispatchesEarlier()
    {
        List<string> log = [];
        EventBroker broker = new();

        // Registered lowest-first so the ordering cannot come from registration order.
        Assert.Equal(RetCode.OK, broker.Subscribe("@name", new Recorder("low", log), nameof(Recorder.OnEvent)));
        Assert.Equal(RetCode.OK, broker.Subscribe("name", new Recorder("normal", log), nameof(Recorder.OnEvent)));
        Assert.Equal(RetCode.OK, broker.Subscribe("!name", new Recorder("high", log), nameof(Recorder.OnEvent)));

        broker.Trigger("name");

        Assert.Equal(["high", "normal", "low"], log);
    }

    /// <summary>
    /// Equal priorities dispatch in REGISTRATION order, and the <c>-</c> prepend symbol reverses that for
    /// the subscription carrying it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tie-break, and the reason the prepend symbol exists. Without it a late subscriber could never
    /// get ahead of an equal-priority one that registered first - which a service that installs itself
    /// after application code needs to be able to do.
    /// </para>
    /// <para>
    /// The insertion arithmetic differs by exactly one comparison between the two cases: appending stops
    /// at the first STRICTLY lower priority, prepending stops at the first priority LESS THAN OR EQUAL. So
    /// a prepending subscriber lands before its equals and an appending one after them.
    /// </para>
    /// </remarks>
    [Fact]
    public void EqualPrioritiesKeepRegistrationOrderUnlessPrependIsRequested()
    {
        List<string> appendLog = [];
        EventBroker appending = new();

        Assert.Equal(RetCode.OK, appending.Subscribe("name", new Recorder("first", appendLog), nameof(Recorder.OnEvent)));
        Assert.Equal(RetCode.OK, appending.Subscribe("name", new Recorder("second", appendLog), nameof(Recorder.OnEvent)));
        Assert.Equal(RetCode.OK, appending.Subscribe("name", new Recorder("third", appendLog), nameof(Recorder.OnEvent)));

        appending.Trigger("name");
        Assert.Equal(["first", "second", "third"], appendLog);

        List<string> prependLog = [];
        EventBroker prepending = new();

        Assert.Equal(RetCode.OK, prepending.Subscribe("name", new Recorder("first", prependLog), nameof(Recorder.OnEvent)));
        Assert.Equal(RetCode.OK, prepending.Subscribe("name", new Recorder("second", prependLog), nameof(Recorder.OnEvent)));

        // This one asks to go in front of its equals.
        Assert.Equal(RetCode.OK, prepending.Subscribe("-name", new Recorder("jumped", prependLog), nameof(Recorder.OnEvent)));

        prepending.Trigger("name");
        Assert.Equal(["jumped", "first", "second"], prependLog);
    }

    /// <summary>
    /// A dispatch invokes only the subscriptions matching its name.
    /// </summary>
    /// <remarks>
    /// The complement of the ordering tests, and it exercises the loop's early BREAK: once the walk has
    /// entered a name's run and then left it, it stops rather than scanning the remaining subscriptions.
    /// A missing break would still produce the right invocations here, so the assertion is on the
    /// neighbours NOT running.
    /// </remarks>
    [Fact]
    public void ADispatchInvokesOnlyItsOwnName()
    {
        List<string> log = [];
        EventBroker broker = new();

        Recorder before = new("before", log);
        Recorder matchOne = new("match1", log);
        Recorder matchTwo = new("match2", log);
        Recorder after = new("after", log);

        Assert.Equal(RetCode.OK, broker.Subscribe("a-before", before, nameof(Recorder.OnEvent)));
        Assert.Equal(RetCode.OK, broker.Subscribe("b-match", matchOne, nameof(Recorder.OnEvent)));
        Assert.Equal(RetCode.OK, broker.Subscribe("b-match", matchTwo, nameof(Recorder.OnEvent)));
        Assert.Equal(RetCode.OK, broker.Subscribe("c-after", after, nameof(Recorder.OnEvent)));

        broker.Trigger("b-match");

        Assert.Equal(["match1", "match2"], log);
        Assert.Equal(0, before.CallCount);
        Assert.Equal(0, after.CallCount);
    }

    /// <summary>
    /// A name with no subscription dispatches to nothing and answers the default.
    /// </summary>
    /// <param name="name">A name outside the subscribed range or inside it but unsubscribed.</param>
    /// <remarks>
    /// The rows straddle the broker's own first-and-last-name shortcut: a name ordinally BEFORE the first
    /// or AFTER the last is rejected by that shortcut, while one BETWEEN them reaches the scan and finds
    /// nothing. Both paths must behave the same from outside, and only covering both proves the shortcut
    /// is an optimization rather than a behaviour change.
    /// </remarks>
    [Theory]
    [InlineData("0-before-everything")]
    [InlineData("m-between")]
    [InlineData("z-after-everything")]
    public void AnUnsubscribedNameDispatchesToNothing(string name)
    {
        List<string> log = [];
        EventBroker broker = new();

        Assert.Equal(RetCode.OK, broker.Subscribe("b-first", new Recorder("first", log), nameof(Recorder.OnEvent)));
        Assert.Equal(RetCode.OK, broker.Subscribe("x-last", new Recorder("last", log), nameof(Recorder.OnEvent)));

        Assert.Null(broker.Trigger(name));
        Assert.Empty(log);
    }

    // ==============================================================================================
    //  4. CAPTURE GATING - THE STATE MACHINE
    // ==============================================================================================

    /// <summary>
    /// While an event is UNHANDLED, unhandled-capture subscribers run and handled-capture ones do not.
    /// </summary>
    /// <remarks>
    /// The initial state of the machine. A <c>%</c> subscriber has asked to see the event only after
    /// somebody has dealt with it, so it must be skipped while nobody has - which is what makes the
    /// symbol useful for a fallback-suppressing observer rather than a second handler.
    /// </remarks>
    [Fact]
    public void WhileUnhandledOnlyUnhandledSubscribersRun()
    {
        List<string> log = [];
        EventBroker broker = new();

        // Both return null, so nothing ever flips the state.
        Assert.Equal(RetCode.OK, broker.Subscribe("name", new Recorder("unhandled", log), nameof(Recorder.OnEvent)));
        Assert.Equal(RetCode.OK, broker.Subscribe("%name", new Recorder("handled", log), nameof(Recorder.OnEvent)));

        broker.Trigger("name");

        Assert.Equal(["unhandled"], log);
    }

    /// <summary>
    /// A handler returning a non-null value FLIPS the state to handled, after which unhandled subscribers
    /// are skipped and handled ones run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The transition, and the behaviour most likely to surprise: returning a value from a handler
    /// silently changes which LATER subscribers execute. A handler author who returns a diagnostic value
    /// "just for logging" suppresses every remaining unhandled subscriber on that topic.
    /// </para>
    /// <para>
    /// The ordering here is deliberate - the value-returning subscriber has the highest priority so it
    /// runs first and the transition is observable in the subscribers after it.
    /// </para>
    /// </remarks>
    [Fact]
    public void ANonNullReturnFlipsTheStateAndChangesWhoRunsNext()
    {
        List<string> log = [];
        EventBroker broker = new();

        Assert.Equal(
            RetCode.OK,
            broker.Subscribe("!name", new Recorder("answers", log, answer: "an answer"), nameof(Recorder.OnEvent)));

        Assert.Equal(RetCode.OK, broker.Subscribe("name", new Recorder("unhandled", log), nameof(Recorder.OnEvent)));
        Assert.Equal(RetCode.OK, broker.Subscribe("%name", new Recorder("handled", log), nameof(Recorder.OnEvent)));

        broker.Trigger("name");

        // The value-returner ran, the unhandled subscriber was SKIPPED, the handled one ran.
        Assert.Equal(["answers", "handled"], log);
    }

    /// <summary>
    /// A <c>*</c> subscriber runs in BOTH states.
    /// </summary>
    /// <remarks>
    /// The third capture mode, and the only one immune to the transition - which is what makes it the
    /// right choice for a tracer or an audit hook that must see every dispatch regardless of who handled
    /// it. Asserted by placing one before the value-returner and one after, so both states are covered by
    /// the same mode.
    /// </remarks>
    [Fact]
    public void AnAllCaptureSubscriberRunsInBothStates()
    {
        List<string> log = [];
        EventBroker broker = new();

        Assert.Equal(RetCode.OK, broker.Subscribe("!*name", new Recorder("all-before", log), nameof(Recorder.OnEvent)));
        Assert.Equal(RetCode.OK, broker.Subscribe("name", new Recorder("answers", log, answer: 42L), nameof(Recorder.OnEvent)));
        Assert.Equal(RetCode.OK, broker.Subscribe("@*name", new Recorder("all-after", log), nameof(Recorder.OnEvent)));

        broker.Trigger("name");

        Assert.Equal(["all-before", "answers", "all-after"], log);
    }

    /// <summary>
    /// A handler returning NULL never flips the state, however many run.
    /// </summary>
    /// <remarks>
    /// Null is the "I did not handle this" answer, and it has to be, because a PowerScript event with no
    /// explicit return yields it. Otherwise every handler that simply did its work and returned nothing
    /// would suppress its successors.
    /// </remarks>
    [Fact]
    public void ANullReturnNeverFlipsTheState()
    {
        List<string> log = [];
        EventBroker broker = new();

        for (int index = 0; index < 3; index++)
        {
            Assert.Equal(
                RetCode.OK,
                broker.Subscribe("name", new Recorder($"n{index}", log), nameof(Recorder.OnEvent)));
        }

        Assert.Equal(RetCode.OK, broker.Subscribe("%name", new Recorder("handled", log), nameof(Recorder.OnEvent)));

        broker.Trigger("name");

        Assert.Equal(["n0", "n1", "n2"], log);
    }

    /// <summary>
    /// A handler returning a value EQUAL to the established default does NOT flip the state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The subtlety in the transition. Once a default return value is established for a name, a handler
    /// that returns exactly that value has said nothing new - so the event stays unhandled and later
    /// subscribers still run. Only a value DIFFERING from the default counts as handling.
    /// </para>
    /// <para>
    /// This is what lets a topic declare a neutral answer and have handlers opt in by returning something
    /// else, rather than every handler having to return null to stay out of the way.
    /// </para>
    /// </remarks>
    [Fact]
    public void AValueEqualToTheEstablishedDefaultDoesNotFlipTheState()
    {
        List<string> log = [];
        EventBroker broker = new();

        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue("name", 0L));

        Assert.Equal(
            RetCode.OK,
            broker.Subscribe("!name", new Recorder("returns-default", log, answer: 0L), nameof(Recorder.OnEvent)));

        Assert.Equal(RetCode.OK, broker.Subscribe("name", new Recorder("still-runs", log), nameof(Recorder.OnEvent)));

        broker.Trigger("name");

        Assert.Equal(["returns-default", "still-runs"], log);

        // A DIFFERENT value does flip it, so the comparison is what decided.
        List<string> differing = [];
        EventBroker other = new();

        Assert.Equal(RetCode.OK, other.SetDefaultReturnValue("name", 0L));
        Assert.Equal(
            RetCode.OK,
            other.Subscribe("!name", new Recorder("returns-other", differing, answer: 1L), nameof(Recorder.OnEvent)));
        Assert.Equal(RetCode.OK, other.Subscribe("name", new Recorder("skipped", differing), nameof(Recorder.OnEvent)));

        other.Trigger("name");

        Assert.Equal(["returns-other"], differing);
    }

    /// <summary>
    /// Default-value comparison is by VALUE across numeric types, not by reference or exact type.
    /// </summary>
    /// <remarks>
    /// PowerScript compares an <c>any</c> numerically when both sides are numbers, so a handler returning
    /// an <c>int</c> matches a <c>long</c> default of the same magnitude. Reproduced because a
    /// type-strict comparison would treat every cross-width return as "different" and flip the state,
    /// changing which subscribers run for reasons a handler author could not see.
    /// </remarks>
    [Fact]
    public void DefaultComparisonIsNumericAcrossWidths()
    {
        List<string> log = [];
        EventBroker broker = new();

        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue("name", 5L));

        // An int 5 against a long 5 default: equal, so the state does not flip.
        Assert.Equal(
            RetCode.OK,
            broker.Subscribe("!name", new Recorder("int-five", log, answer: 5), nameof(Recorder.OnEvent)));
        Assert.Equal(RetCode.OK, broker.Subscribe("name", new Recorder("still-runs", log), nameof(Recorder.OnEvent)));

        broker.Trigger("name");

        Assert.Equal(["int-five", "still-runs"], log);
    }

    // ==============================================================================================
    //  5. RETURN VALUES
    // ==============================================================================================

    /// <summary>
    /// <see cref="EventBroker.Trigger"/> returns the LAST invoked handler's value.
    /// </summary>
    /// <remarks>
    /// Not the first, and not the handling one - the last to run. That is what the legacy returns, and it
    /// matters when several subscribers answer: the lowest-priority one wins the returned value even
    /// though the highest-priority one is what flipped the handled state. The two mechanisms are separate
    /// and this test keeps them apart.
    /// </remarks>
    [Fact]
    public void TriggerReturnsTheLastInvokedHandlersValue()
    {
        List<string> log = [];
        EventBroker broker = new();

        Assert.Equal(RetCode.OK, broker.Subscribe("!*name", new Recorder("first", log, answer: "first"), nameof(Recorder.OnEvent)));
        Assert.Equal(RetCode.OK, broker.Subscribe("@*name", new Recorder("last", log, answer: "last"), nameof(Recorder.OnEvent)));

        Assert.Equal("last", broker.Trigger("name"));
        Assert.Equal(["first", "last"], log);
    }

    /// <summary>
    /// With no subscriber, <see cref="EventBroker.Trigger"/> returns the name's default when one is
    /// established, and the global default otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two-level default is how a topic declares a neutral answer for callers that do not check
    /// whether anyone subscribed. The per-name default takes precedence over the global one, which is
    /// asserted here by establishing both and reading the specific one back.
    /// </para>
    /// <para>
    /// The empty-name case returns the GLOBAL default without consulting the per-name table at all, which
    /// the last assertion covers - a detail that matters because an empty name is otherwise rejected
    /// everywhere else in the broker.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheTwoLevelDefaultAppliesWhenNothingRuns()
    {
        EventBroker broker = new();

        Assert.Null(broker.Trigger("name"));

        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue("global fallback"));
        Assert.Equal("global fallback", broker.Trigger("name"));

        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue("name", "specific"));
        Assert.Equal("specific", broker.Trigger("name"));
        Assert.Equal("global fallback", broker.Trigger("other"));

        // An empty name short-circuits to the global default.
        Assert.Equal("global fallback", broker.Trigger(string.Empty));
    }

    /// <summary>
    /// A per-name default can be REPLACED, and the last write wins.
    /// </summary>
    /// <remarks>
    /// The table stores one entry per name rather than accumulating, so a second call for the same name
    /// updates it. Pinned because an accumulating implementation would keep the first value and silently
    /// ignore every later one, which would look like the setter had no effect.
    /// </remarks>
    [Fact]
    public void APerNameDefaultCanBeReplaced()
    {
        EventBroker broker = new();

        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue("name", "first"));
        Assert.Equal("first", broker.Trigger("name"));

        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue("name", "second"));
        Assert.Equal("second", broker.Trigger("name"));

        // Null is a legal default value and is distinguishable from "not established" only through the
        // handled-state machine, so here it simply reads back as null.
        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue("name", null));
        Assert.Null(broker.Trigger("name"));
    }

    /// <summary>
    /// A handler's non-null value is visible through <see cref="EventBroker.GetReturnValue"/> and
    /// <see cref="EventBroker.IsProcessed"/> only FROM INSIDE the dispatch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The save-and-restore behaviour, and the reason it exists. The broker pushes the return value on
    /// dispatch entry and pops it in the finally, so a nested dispatch cannot corrupt its parent's state -
    /// and the price is that both members read as the PRE-dispatch state once <c>Trigger</c> has returned.
    /// </para>
    /// <para>
    /// Asserted from both sides: an observer subscribed AFTER a value-returner sees the value and the
    /// processed flag, while the caller sees neither afterwards. A test that only checked from outside
    /// would conclude the members never work.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheReturnValueIsVisibleOnlyInsideTheDispatch()
    {
        List<string> log = [];
        EventBroker broker = new();
        Observer observer = new(broker);

        Assert.Equal(
            RetCode.OK,
            broker.Subscribe("!name", new Recorder("answers", log, answer: "the answer"), nameof(Recorder.OnEvent)));

        Assert.Equal(RetCode.OK, broker.Subscribe("*name", observer, nameof(Observer.OnObserve)));

        broker.Trigger("name");

        // From inside: the value and the flag are live.
        Assert.Equal("the answer", observer.SeenReturnValue);
        Assert.True(observer.SeenIsProcessed);

        // From outside, afterwards: restored to the pre-dispatch state.
        Assert.Null(broker.GetReturnValue());
        Assert.False(broker.IsProcessed());
    }

    // ==============================================================================================
    //  6. PREVENTION - TRI-VALUED
    // ==============================================================================================

    /// <summary>
    /// <see cref="EventBroker.Prevent(bool)"/> called OUTSIDE any dispatch answers
    /// <c>RetCode.FAILED</c>.
    /// </summary>
    /// <remarks>
    /// The guard is on the dispatch depth, so there is nothing to prevent and the call reports a failure
    /// rather than throwing or silently arming a veto for the next dispatch. Both matter: throwing would
    /// make a misplaced call fatal, and arming would suppress an unrelated later event.
    /// </remarks>
    [Fact]
    public void PreventOutsideADispatchFails()
    {
        EventBroker broker = new();

        Assert.Equal(RetCode.FAILED, broker.Prevent());
        Assert.Equal(RetCode.FAILED, broker.Prevent(deep: false));
        Assert.Equal(RetCode.FAILED, broker.Prevent(deep: true));
    }

    /// <summary>
    /// <see cref="EventBroker.Prevent(bool)"/> inside a dispatch answers <c>OK</c> and stops the
    /// remaining subscribers.
    /// </summary>
    /// <remarks>
    /// The veto's whole purpose: a handler decides the event should go no further. The subscriber AFTER
    /// the vetoer must not run, and the one BEFORE it must already have - so both are asserted, which is
    /// what distinguishes a veto from a cancellation of the whole dispatch.
    /// </remarks>
    [Fact]
    public void PreventInsideADispatchStopsTheRemainingSubscribers()
    {
        List<string> log = [];
        EventBroker broker = new();

        Assert.Equal(RetCode.OK, broker.Subscribe("!name", new Recorder("before", log), nameof(Recorder.OnEvent)));

        Vetoer vetoer = new("vetoer", log, broker, deep: false);
        Assert.Equal(RetCode.OK, broker.Subscribe("name", vetoer, nameof(Vetoer.OnEvent)));

        Assert.Equal(RetCode.OK, broker.Subscribe("@name", new Recorder("after", log), nameof(Recorder.OnEvent)));

        broker.Trigger("name");

        Assert.Equal(["before", "vetoer"], log);
        Assert.Equal(RetCode.OK, vetoer.PreventCode);
    }

    /// <summary>
    /// A DEEP veto also stops the ENCLOSING dispatch, while a shallow one does not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The distinction the enum exists for, demonstrated with a nested trigger. An outer subscriber
    /// triggers an inner event whose handler vetoes; with a shallow veto the outer dispatch continues to
    /// its next subscriber, and with a deep one it stops too.
    /// </para>
    /// <para>
    /// This is the behaviour that a boolean veto would silently lose - both cases would behave as the
    /// shallow one, and the nested suppression the DataWindow item-change chain relies on
    /// [se_cst_dw.sru:L182-L253 fires a nested event from inside itself] would not happen.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADeepVetoStopsTheEnclosingDispatchAndAShallowOneDoesNot(bool deep)
    {
        List<string> log = [];
        EventBroker broker = new();

        Vetoer inner = new("inner-veto", log, broker, deep);
        Assert.Equal(RetCode.OK, broker.Subscribe("inner", inner, nameof(Vetoer.OnEvent)));

        Nester nester = new("outer-nester", log, broker);
        Assert.Equal(RetCode.OK, broker.Subscribe("!outer", nester, nameof(Nester.OnEvent)));

        Assert.Equal(RetCode.OK, broker.Subscribe("@outer", new Recorder("outer-after", log), nameof(Recorder.OnEvent)));

        broker.Trigger("outer");

        if (deep)
        {
            Assert.Equal(["outer-nester", "inner-veto"], log);
        }
        else
        {
            Assert.Equal(["outer-nester", "inner-veto", "outer-after"], log);
        }
    }

    /// <summary>
    /// A target that triggers a nested event from inside its own handler.
    /// </summary>
    private sealed class Nester
    {
        /// <summary>Initializes a new instance of the <see cref="Nester"/> class.</summary>
        /// <param name="label">The label written to the log.</param>
        /// <param name="log">The shared ordering log.</param>
        /// <param name="broker">The broker to trigger the nested event on.</param>
        internal Nester(string label, List<string> log, EventBroker broker)
        {
            Label = label;
            Log = log;
            Broker = broker;
        }

        /// <summary>Gets the label written to the log.</summary>
        internal string Label { get; }

        /// <summary>Gets the shared ordering log.</summary>
        internal List<string> Log { get; }

        /// <summary>Gets the broker used for the nested trigger.</summary>
        internal EventBroker Broker { get; }

        /// <summary>Runs, then triggers a nested event.</summary>
        /// <returns>Always null, so the nesting does not also flip the handled state.</returns>
        public object? OnEvent()
        {
            Log.Add(Label);
            Broker.Trigger("inner");
            return null;
        }
    }

    /// <summary>
    /// A veto does not persist into a LATER, separate dispatch.
    /// </summary>
    /// <remarks>
    /// The unwinding half of the veto's lifecycle. Whatever the depth, the prevention is cleared once the
    /// outermost dispatch completes, so a second trigger of the same event starts clean. Without this a
    /// single veto would permanently disable a topic - and because the veto state is a field rather than a
    /// parameter, that is exactly the failure a missing reset produces.
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AVetoDoesNotPersistIntoALaterDispatch(bool deep)
    {
        List<string> log = [];
        EventBroker broker = new();

        // A subscriber BEFORE the vetoer is what makes non-persistence observable: if the prevention
        // survived the dispatch, the second trigger would break at the top of the loop and this one would
        // not run either - so the log would come back empty rather than merely short.
        Assert.Equal(RetCode.OK, broker.Subscribe("!name", new Recorder("before", log), nameof(Recorder.OnEvent)));

        Vetoer vetoer = new("vetoer", log, broker, deep);
        Assert.Equal(RetCode.OK, broker.Subscribe("name", vetoer, nameof(Vetoer.OnEvent)));

        Assert.Equal(RetCode.OK, broker.Subscribe("@name", new Recorder("after", log), nameof(Recorder.OnEvent)));

        // Pass one: the veto stops the subscriber after it, as it should.
        broker.Trigger("name");
        Assert.Equal(["before", "vetoer"], log);
        Assert.Equal(RetCode.OK, vetoer.PreventCode);

        // Pass two: MEASURED to reproduce pass one exactly. The prevention was cleared as the dispatch
        // unwound, so the topic is not permanently disabled - which is what a missing reset would do,
        // given the veto lives in a field rather than in a parameter.
        log.Clear();
        broker.Trigger("name");
        Assert.Equal(["before", "vetoer"], log);
    }

    /// <summary>
    /// A veto is permitted from inside a dispatch and refused outside it, observed from one target.
    /// </summary>
    /// <remarks>
    /// The two codes from one object, so the difference cannot be attributed to the target. The observer
    /// probes the veto during dispatch and the test probes it afterwards, and the codes differ - which is
    /// the depth guard doing its job.
    /// </remarks>
    [Fact]
    public void TheVetoGuardDependsOnDispatchDepthNotOnTheCaller()
    {
        EventBroker broker = new();
        Observer observer = new(broker);

        Assert.Equal(RetCode.OK, broker.Subscribe("name", observer, nameof(Observer.OnProbeVeto)));

        broker.Trigger("name");

        Assert.Equal(RetCode.OK, observer.SeenPreventCode);
        Assert.Equal(RetCode.FAILED, broker.Prevent());
    }

    // ==============================================================================================
    //  7. POST AND DRAIN
    // ==============================================================================================

    /// <summary>
    /// <see cref="EventBroker.Post"/> does NOT dispatch: it queues, and
    /// <see cref="EventBroker.DrainPostedContinuations"/> runs the queue.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The legacy posts to the Win32 message queue, which a headless Linux container does not have - so
    /// AAP 0.4.5.4 requires the posted call become an explicitly queued continuation. The observable
    /// consequence is that posting is now deferred until something drains, rather than until the message
    /// pump next runs.
    /// </para>
    /// <para>
    /// The pending count is asserted at each step because it is the only way a caller can tell a post
    /// happened at all before the drain.
    /// </para>
    /// </remarks>
    [Fact]
    public void PostQueuesAndDrainRuns()
    {
        List<string> log = [];
        EventBroker broker = new();
        Recorder target = new("posted", log);

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(Recorder.OnEvent)));

        broker.Post("name");

        Assert.Equal(1, broker.PendingPostedContinuationCount);
        Assert.Empty(log);

        Assert.Equal(1, broker.DrainPostedContinuations());

        Assert.Equal(0, broker.PendingPostedContinuationCount);
        Assert.Equal(["posted"], log);
    }

    /// <summary>
    /// Posted continuations drain in POST order, and draining an empty queue is a no-op.
    /// </summary>
    /// <remarks>
    /// Ordering is the point: the legacy message queue is first-in-first-out, so the continuation queue
    /// must be too, or two posted events would arrive in the wrong order relative to each other. The
    /// empty-drain assertion covers the case where a caller drains defensively.
    /// </remarks>
    [Fact]
    public void PostedContinuationsDrainInPostOrder()
    {
        List<string> log = [];
        EventBroker broker = new();

        Assert.Equal(RetCode.OK, broker.Subscribe("a", new Recorder("a", log), nameof(Recorder.OnEvent)));
        Assert.Equal(RetCode.OK, broker.Subscribe("b", new Recorder("b", log), nameof(Recorder.OnEvent)));

        broker.Post("b");
        broker.Post("a");
        broker.Post("b");

        Assert.Equal(3, broker.PendingPostedContinuationCount);
        Assert.Equal(3, broker.DrainPostedContinuations());

        Assert.Equal(["b", "a", "b"], log);
        Assert.Equal(0, broker.DrainPostedContinuations());
    }

    /// <summary>
    /// <see cref="EventBroker.Post"/> SNAPSHOTS its arguments, so a later mutation of the caller's array
    /// does not reach the handler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The clone is what makes a deferred dispatch safe. The legacy posts by value; without the snapshot
    /// the handler would observe whatever the array held at DRAIN time rather than at POST time, which for
    /// a reused buffer is a different value entirely.
    /// </para>
    /// <para>
    /// Demonstrated by mutating the array between the post and the drain and asserting the handler saw the
    /// original.
    /// </para>
    /// </remarks>
    [Fact]
    public void PostSnapshotsItsArguments()
    {
        List<string> log = [];
        EventBroker broker = new();
        Recorder target = new("t", log);

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(Recorder.OnTwoArguments)));

        object?[] arguments = [1L, "original"];
        broker.Post("name", arguments);

        // Mutate the caller's array after posting.
        arguments[0] = 999L;
        arguments[1] = "mutated";

        broker.DrainPostedContinuations();

        Assert.Equal(["t(1,original)"], log);
    }

    /// <summary>
    /// A handler running under <see cref="EventBroker.Post"/> sees
    /// <see cref="EventBroker.IsPost"/> true, and one under <see cref="EventBroker.Trigger"/> sees it
    /// false.
    /// </summary>
    /// <remarks>
    /// The flag lets a handler distinguish a synchronous notification from a deferred one, which matters
    /// because the deferred one runs after the originating call has returned - so state the handler
    /// expects may already have changed. Asserted on both paths from one observer so the difference is
    /// the dispatch and not the target.
    /// </remarks>
    [Fact]
    public void TheIsPostFlagDistinguishesTheTwoDispatchPaths()
    {
        EventBroker broker = new();
        Observer observer = new(broker);

        Assert.Equal(RetCode.OK, broker.Subscribe("name", observer, nameof(Observer.OnObserve)));

        broker.Trigger("name");
        Assert.False(observer.SeenIsPost);

        broker.Post("name");
        broker.DrainPostedContinuations();
        Assert.True(observer.SeenIsPost);

        // Restored afterwards, so the flag does not leak into the next call.
        Assert.False(broker.IsPost());
    }

    // ==============================================================================================
    //  8. DISABLE
    // ==============================================================================================

    /// <summary>
    /// A disabled subscription is skipped, and re-enabling restores it.
    /// </summary>
    /// <remarks>
    /// Disable is reversible where unsubscribe is not, which is why the broker carries both. The
    /// round trip is asserted in one test because a disable that could not be undone would satisfy any
    /// one-way assertion.
    /// </remarks>
    [Fact]
    public void ADisabledSubscriptionIsSkippedAndCanBeReEnabled()
    {
        List<string> log = [];
        EventBroker broker = new();
        Recorder target = new("t", log);

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(Recorder.OnEvent)));

        broker.Trigger("name");
        Assert.Equal(1, target.CallCount);

        Assert.Equal(RetCode.OK, broker.Disable("name", target, disabled: true));
        broker.Trigger("name");
        Assert.Equal(1, target.CallCount);

        Assert.Equal(RetCode.OK, broker.Disable("name", target, disabled: false));
        broker.Trigger("name");
        Assert.Equal(2, target.CallCount);
    }

    /// <summary>
    /// Each disable overload selects the subscriptions its arguments describe, and no more.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Seven overloads exist because the legacy offers seven ways to name a set of subscriptions, and each
    /// is a different selector - by name and target and handler, by name and target, by name alone, by
    /// target and handler, by target alone, everything, and everything EXCLUDING a target. Covering them
    /// one at a time with a fresh broker is what proves each selector is wired to the right filter.
    /// </para>
    /// <para>
    /// The excluding overload is the interesting one: it disables every subscription EXCEPT the named
    /// target's, which is how a component silences everyone else while keeping itself live.
    /// </para>
    /// </remarks>
    [Fact]
    public void EachDisableOverloadSelectsWhatItsArgumentsDescribe()
    {
        // By name, target and handler.
        AssertDisableSelects(
            (broker, first, second) => broker.Disable("a", first, nameof(Recorder.OnEvent), disabled: true),
            expectFirstRan: false,
            expectSecondRan: true);

        // By name and target.
        AssertDisableSelects(
            (broker, first, second) => broker.Disable("a", first, disabled: true),
            expectFirstRan: false,
            expectSecondRan: true);

        // By name alone - both subscriptions are on different names, so only the named one is hit.
        AssertDisableSelects(
            (broker, first, second) => broker.Disable("a", disabled: true),
            expectFirstRan: false,
            expectSecondRan: true);

        // By target and handler.
        AssertDisableSelects(
            (broker, first, second) => broker.Disable(second, nameof(Recorder.OnEvent), disabled: true),
            expectFirstRan: true,
            expectSecondRan: false);

        // By target alone.
        AssertDisableSelects(
            (broker, first, second) => broker.Disable(second, disabled: true),
            expectFirstRan: true,
            expectSecondRan: false);

        // Everything.
        AssertDisableSelects(
            (broker, first, second) => broker.Disable(disabled: true),
            expectFirstRan: false,
            expectSecondRan: false);

        // Everything EXCEPT the named target.
        AssertDisableSelects(
            (broker, first, second) => broker.Disable(first, disabled: true, excluding: true),
            expectFirstRan: true,
            expectSecondRan: false);
    }

    /// <summary>
    /// Sets up two subscriptions on two names, applies a disable selector, and asserts which still run.
    /// </summary>
    /// <param name="applyDisable">The selector to apply.</param>
    /// <param name="expectFirstRan">Whether the first subscription should still run.</param>
    /// <param name="expectSecondRan">Whether the second should still run.</param>
    private static void AssertDisableSelects(
        Func<EventBroker, Recorder, Recorder, long> applyDisable,
        bool expectFirstRan,
        bool expectSecondRan)
    {
        List<string> log = [];
        EventBroker broker = new();
        Recorder first = new("first", log);
        Recorder second = new("second", log);

        Assert.Equal(RetCode.OK, broker.Subscribe("a", first, nameof(Recorder.OnEvent)));
        Assert.Equal(RetCode.OK, broker.Subscribe("b", second, nameof(Recorder.OnEvent)));

        Assert.Equal(RetCode.OK, applyDisable(broker, first, second));

        broker.Trigger("a");
        broker.Trigger("b");

        Assert.Equal(expectFirstRan ? 1 : 0, first.CallCount);
        Assert.Equal(expectSecondRan ? 1 : 0, second.CallCount);
    }

    // ==============================================================================================
    //  9. UNSUBSCRIBE
    // ==============================================================================================

    /// <summary>
    /// Each unsubscribe overload removes the subscriptions its arguments describe, and no more.
    /// </summary>
    /// <remarks>
    /// The same seven-selector structure as disable, and the same reason for covering each: the selectors
    /// are separate code paths and a mis-wired one would silently remove the wrong subscription. Removal
    /// is asserted through dispatch rather than through a count, because the broker exposes no count -
    /// which makes behaviour the only observable.
    /// </remarks>
    [Fact]
    public void EachUnsubscribeOverloadRemovesWhatItsArgumentsDescribe()
    {
        AssertUnsubscribeSelects(
            (broker, first, second) => broker.Unsubscribe("a", first, nameof(Recorder.OnEvent)),
            expectFirstRan: false,
            expectSecondRan: true);

        AssertUnsubscribeSelects(
            (broker, first, second) => broker.Unsubscribe("a", first),
            expectFirstRan: false,
            expectSecondRan: true);

        AssertUnsubscribeSelects(
            (broker, first, second) => broker.Unsubscribe("a"),
            expectFirstRan: false,
            expectSecondRan: true);

        AssertUnsubscribeSelects(
            (broker, first, second) => broker.Unsubscribe(second, nameof(Recorder.OnEvent)),
            expectFirstRan: true,
            expectSecondRan: false);

        AssertUnsubscribeSelects(
            (broker, first, second) => broker.Unsubscribe(second),
            expectFirstRan: true,
            expectSecondRan: false);

        AssertUnsubscribeSelects(
            (broker, first, second) => broker.Unsubscribe(),
            expectFirstRan: false,
            expectSecondRan: false);

        AssertUnsubscribeSelects(
            (broker, first, second) => broker.Unsubscribe(first, excluding: true),
            expectFirstRan: true,
            expectSecondRan: false);
    }

    /// <summary>
    /// Sets up two subscriptions, applies an unsubscribe selector, and asserts which still run.
    /// </summary>
    /// <param name="applyUnsubscribe">The selector to apply.</param>
    /// <param name="expectFirstRan">Whether the first subscription should still run.</param>
    /// <param name="expectSecondRan">Whether the second should still run.</param>
    private static void AssertUnsubscribeSelects(
        Func<EventBroker, Recorder, Recorder, long> applyUnsubscribe,
        bool expectFirstRan,
        bool expectSecondRan)
    {
        List<string> log = [];
        EventBroker broker = new();
        Recorder first = new("first", log);
        Recorder second = new("second", log);

        Assert.Equal(RetCode.OK, broker.Subscribe("a", first, nameof(Recorder.OnEvent)));
        Assert.Equal(RetCode.OK, broker.Subscribe("b", second, nameof(Recorder.OnEvent)));

        Assert.Equal(RetCode.OK, applyUnsubscribe(broker, first, second));

        broker.Trigger("a");
        broker.Trigger("b");

        Assert.Equal(expectFirstRan ? 1 : 0, first.CallCount);
        Assert.Equal(expectSecondRan ? 1 : 0, second.CallCount);
    }

    /// <summary>
    /// Unsubscribing removes the subscription permanently: re-triggering does not resurrect it.
    /// </summary>
    /// <remarks>
    /// The difference from disable, stated directly. A disabled entry stays in the list and can come back;
    /// an unsubscribed one is gone and only a fresh <c>Subscribe</c> restores it. Both halves are asserted
    /// so the two operations cannot be confused.
    /// </remarks>
    [Fact]
    public void UnsubscribingIsPermanentUnlikeDisabling()
    {
        List<string> log = [];
        EventBroker broker = new();
        Recorder target = new("t", log);

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(Recorder.OnEvent)));
        Assert.Equal(RetCode.OK, broker.Unsubscribe("name", target));

        broker.Trigger("name");
        broker.Trigger("name");
        Assert.Equal(0, target.CallCount);

        // Only a fresh subscription brings it back.
        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(Recorder.OnEvent)));
        broker.Trigger("name");
        Assert.Equal(1, target.CallCount);
    }

    // ==============================================================================================
    //  10. IS SUBSCRIBED, AND ITS QUIRK
    // ==============================================================================================

    /// <summary>
    /// <see cref="EventBroker.IsSubscribed"/> rejects an empty name and an unsubscribed one.
    /// </summary>
    /// <remarks>
    /// The predicate is what <c>se_cst_dw.sru</c> guards each of its twelve triggers with, so a false
    /// positive costs a pointless dispatch and a false negative costs a missed event. The empty-name row
    /// is included because an empty name is meaningful elsewhere in the broker - it selects the global
    /// default - and here it must simply answer false.
    /// </remarks>
    [Fact]
    public void IsSubscribedRejectsEmptyAndUnknownNames()
    {
        EventBroker broker = new();
        Recorder target = new("t", []);

        Assert.False(broker.IsSubscribed(string.Empty));
        Assert.False(broker.IsSubscribed("name"));

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(Recorder.OnEvent)));

        Assert.True(broker.IsSubscribed("name"));
        Assert.False(broker.IsSubscribed(string.Empty));
        Assert.False(broker.IsSubscribed("other"));
        Assert.False(broker.IsSubscribed("NAME"));
    }

    /// <summary>
    /// <see cref="EventBroker.IsSubscribed"/> AGREES with dispatch: a disabled subscription reports as
    /// not subscribed, whatever position it holds in the list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is not obvious from the implementation, which is why it is pinned across every list position.
    /// The predicate has two paths: a short-circuit comparing the requested name against the tracked
    /// FIRST and LAST names, which consults no entry and therefore no flag, and a middle scan over
    /// one-based indices 2 through count-1, which does check the disabled and invalid flags. Read alone,
    /// the short-circuit looks as though it would report a disabled first-or-last subscription as
    /// subscribed.
    /// </para>
    /// <para>
    /// MEASURED, it does not - because disabling recomputes the tracked first and last names from the
    /// ENABLED entries only. So a disabled boundary entry is no longer the first or last name, the
    /// short-circuit does not fire for it, and the scan reaches the same answer the middle path would.
    /// The two mechanisms are load-bearing together and neither is sufficient alone.
    /// </para>
    /// <para>
    /// The consistency matters because <c>se_cst_dw.sru</c> guards each of its twelve triggers with this
    /// predicate. A false positive would cost a pointless dispatch; a false negative would drop an event.
    /// The final assertion ties the predicate to dispatch directly, so the two cannot drift.
    /// </para>
    /// </remarks>
    [Fact]
    public void IsSubscribedAgreesWithDispatchAtEveryListPosition()
    {
        List<string> log = [];
        EventBroker broker = new();

        // Five names, so positions 2 through 4 exercise the middle scan and a and e the short-circuit.
        string[] names = ["a", "b", "c", "d", "e"];

        foreach (string name in names)
        {
            Assert.Equal(RetCode.OK, broker.Subscribe(name, new Recorder(name, log), nameof(Recorder.OnEvent)));
            Assert.True(broker.IsSubscribed(name));
        }

        // Disable one at each significant position: first, middle, last.
        foreach (string disabled in new[] { "c", "a", "e" })
        {
            Assert.Equal(RetCode.OK, broker.Disable(disabled, disabled: true));
            Assert.False(broker.IsSubscribed(disabled), $"'{disabled}' is disabled and must report unsubscribed.");
        }

        // The still-enabled interior entries are unaffected.
        Assert.True(broker.IsSubscribed("b"));
        Assert.True(broker.IsSubscribed("d"));

        // The predicate agrees with dispatch for every name.
        foreach (string name in names)
        {
            log.Clear();
            broker.Trigger(name);

            Assert.Equal(
                broker.IsSubscribed(name),
                log.Count > 0);
        }

        // Re-enabling restores both the predicate and the dispatch.
        Assert.Equal(RetCode.OK, broker.Disable(disabled: false));

        foreach (string name in names)
        {
            Assert.True(broker.IsSubscribed(name));
        }
    }

    /// <summary>
    /// Disabling the ONLY subscription reports it unsubscribed, so the boundary bookkeeping handles an
    /// empty enabled set.
    /// </summary>
    /// <remarks>
    /// The degenerate case for the recomputation: with one entry it is simultaneously the first and last
    /// name, so disabling it must clear BOTH tracked names rather than leaving one behind. A recomputation
    /// that only cleared one would leave the short-circuit firing on the other and report the disabled
    /// subscription as live.
    /// </remarks>
    [Fact]
    public void DisablingTheOnlySubscriptionReportsItUnsubscribed()
    {
        List<string> log = [];
        EventBroker broker = new();
        Recorder target = new("only", log);

        Assert.Equal(RetCode.OK, broker.Subscribe("only", target, nameof(Recorder.OnEvent)));
        Assert.True(broker.IsSubscribed("only"));

        Assert.Equal(RetCode.OK, broker.Disable("only", disabled: true));

        Assert.False(broker.IsSubscribed("only"));

        broker.Trigger("only");
        Assert.Empty(log);
    }

    // ==============================================================================================
    //  11. THE AMBIENT BROKER
    // ==============================================================================================

    /// <summary>
    /// <see cref="EventBroker.Current"/> is the dispatching broker during a handler and null again
    /// afterwards.
    /// </summary>
    /// <remarks>
    /// The ambient reference is how a handler reaches the broker to veto on without being passed one -
    /// which is what the legacy's global auto-instance provided. Asserting that it is restored is as
    /// important as asserting it is set: a leaked ambient broker would let code outside any dispatch veto
    /// an event that is not running.
    /// </remarks>
    [Fact]
    public void TheAmbientBrokerIsSetDuringDispatchAndRestoredAfter()
    {
        EventBroker broker = new();
        Observer observer = new(broker);

        Assert.Equal(RetCode.OK, broker.Subscribe("name", observer, nameof(Observer.OnObserve)));

        Assert.Null(EventBroker.Current);

        broker.Trigger("name");

        Assert.Same(broker, observer.SeenCurrent);
        Assert.Null(EventBroker.Current);
    }

    // ==============================================================================================
    //  12. EXCEPTION CAPTURE
    // ==============================================================================================

    /// <summary>
    /// A throwing handler RETHROWS, and the exception carries an annotated dispatch context.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The broker does not swallow: the exception reaches the caller of <c>Trigger</c>. What it adds is
    /// context - the subscription name, the target's class chain and the handler name - stored in the
    /// exception's data under a published key so a diagnostic can read it without parsing a message.
    /// </para>
    /// <para>
    /// Both halves are asserted because they pull in different directions: a broker that annotated but
    /// swallowed would hide the fault, and one that rethrew without annotating would lose which
    /// subscriber failed - and with several subscribers on one topic that is the only useful information.
    /// </para>
    /// </remarks>
    [Fact]
    public void AThrowingHandlerRethrowsWithAnAnnotatedContext()
    {
        List<string> log = [];
        EventBroker broker = new();
        Recorder target = new("boom", log);

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(Recorder.OnThrowing)));

        InvalidOperationException thrown =
            Assert.Throws<InvalidOperationException>(() => broker.Trigger("name"));

        Assert.Equal("handler blew up", thrown.Message);

        string? annotation = EventBroker.GetDispatchExceptionText(thrown);

        Assert.NotNull(annotation);
        Assert.Contains("Subscribe: name", annotation, StringComparison.Ordinal);
        Assert.Contains("Event: onthrowing", annotation, StringComparison.Ordinal);
        Assert.Contains(nameof(Recorder), annotation, StringComparison.Ordinal);
        Assert.Contains("Exception:", annotation, StringComparison.Ordinal);
    }

    /// <summary>
    /// <see cref="EventBroker.GetDispatchExceptionText"/> is null for a null exception and for one that
    /// never passed through a dispatch.
    /// </summary>
    /// <remarks>
    /// The accessor is reached from diagnostic code that may hold any exception, so both degenerate inputs
    /// have to answer rather than throw. Null-for-null in particular matters because a catch handler
    /// reading the annotation cannot always know the exception is present.
    /// </remarks>
    [Fact]
    public void TheAnnotationAccessorIsNullWhenThereIsNothingToRead()
    {
        Assert.Null(EventBroker.GetDispatchExceptionText(null));
        Assert.Null(EventBroker.GetDispatchExceptionText(new InvalidOperationException("never dispatched")));
    }

    /// <summary>
    /// The annotation key is the published constant, so a consumer can read the data slot directly.
    /// </summary>
    /// <remarks>
    /// The key is public precisely so that a consumer outside this assembly - the Gateway's system-error
    /// handler, which unpacks the assert payload - can find the annotation without going through the
    /// accessor. Asserting the round trip through the raw slot is what makes that contract real rather
    /// than incidental.
    /// </remarks>
    [Fact]
    public void TheAnnotationIsStoredUnderThePublishedKey()
    {
        EventBroker broker = new();
        Recorder target = new("boom", []);

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(Recorder.OnThrowing)));

        InvalidOperationException thrown =
            Assert.Throws<InvalidOperationException>(() => broker.Trigger("name"));

        Assert.Equal(
            EventBroker.GetDispatchExceptionText(thrown),
            thrown.Data[EventBroker.DispatchExceptionTextKey] as string);

        Assert.Equal(
            "PowerFramework.Shared.Eventful.EventBroker.DispatchExceptionText",
            EventBroker.DispatchExceptionTextKey);
    }

    /// <summary>
    /// The two exception-hook result constants are the legacy values 1 and 2.
    /// </summary>
    /// <remarks>
    /// They are a distinct alphabet from both the return-code algebra and the veto enum, and they are
    /// numerically identical to the veto's two prevention values - so a derived broker returning the wrong
    /// one compiles and behaves differently. Pinning the values is what makes an override auditable.
    /// </remarks>
    [Fact]
    public void TheExceptionHookResultConstantsAreOneAndTwo()
    {
        Assert.Equal(1L, EventBroker.ExceptionResultPrevent);
        Assert.Equal(2L, EventBroker.ExceptionResultContinue);
        Assert.NotEqual(EventBroker.ExceptionResultPrevent, EventBroker.ExceptionResultContinue);
    }

    // ==============================================================================================
    //  13. THE SUBCLASS HOOKS
    // ==============================================================================================

    /// <summary>
    /// The four protected hooks are DEAD on the base type and LIVE on a derived one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The base constructor decides by comparing <c>GetType()</c> against <see cref="EventBroker"/>, so a
    /// plain broker pays nothing for hooks nobody overrode and a derived one gets all four. That is a
    /// faithful port of the legacy's own check, and it is why every hook test in this file goes through a
    /// derived class.
    /// </para>
    /// <para>
    /// Asserted by dispatching on a derived broker and observing the hook log - the only way to see the
    /// flag, which is private.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheHooksAreLiveOnADerivedBroker()
    {
        List<string> log = [];
        HookedBroker broker = new();
        Recorder target = new("t", log);

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(Recorder.OnEvent)));

        broker.Trigger("name");

        Assert.Equal(["name"], broker.Triggering);
        Assert.Equal(["name"], broker.Triggered);
        Assert.Equal(["name/0"], broker.Prepared);
        Assert.Empty(broker.Exceptions);
        Assert.Equal(["t"], log);
    }

    /// <summary>
    /// A PREVENTING triggering hook stops the dispatch before any handler runs.
    /// </summary>
    /// <remarks>
    /// The hook is the derived broker's chance to veto a whole dispatch, and it runs just before the first
    /// handler would be invoked. Because the prevention is tested with the return-code algebra's
    /// prevention predicate, the value it must return is <c>RetCode.PREVENT</c> - not the veto enum, and
    /// not the exception-hook constants, even though all three use small positive numbers.
    /// </remarks>
    [Fact]
    public void APreventingTriggeringHookStopsTheDispatch()
    {
        List<string> log = [];
        HookedBroker broker = new() { TriggeringResult = RetCode.PREVENT };
        Recorder target = new("t", log);

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(Recorder.OnEvent)));

        broker.Trigger("name");

        Assert.Equal(["name"], broker.Triggering);
        Assert.Empty(log);
        Assert.Equal(0, target.CallCount);

        // The triggered hook did not run either, because no subscriber was ever reached.
        Assert.Empty(broker.Triggered);
    }

    /// <summary>
    /// The exception hook can CONTINUE past a throwing subscriber, or PREVENT the rest of the dispatch.
    /// </summary>
    /// <param name="hookResult">The value the hook returns.</param>
    /// <param name="expectLaterSubscriberRan">Whether the later subscriber should run.</param>
    /// <remarks>
    /// <para>
    /// The two constants asserted through their effect rather than their value. Continue swallows the
    /// exception and moves to the next subscriber - so one broken handler does not stop the others - while
    /// prevent stops the dispatch. Both suppress the rethrow, which is the substantive difference from the
    /// unhooked base broker.
    /// </para>
    /// <para>
    /// This is the mechanism a derived broker uses to make a dispatch resilient, and it is only reachable
    /// through a subclass, so it is untestable without one.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(2L, true)]
    [InlineData(1L, false)]
    public void TheExceptionHookCanContinueOrPreventInsteadOfRethrowing(
        long hookResult,
        bool expectLaterSubscriberRan)
    {
        List<string> log = [];
        HookedBroker broker = new() { ExceptionResult = hookResult };

        Recorder thrower = new("boom", log);
        Recorder later = new("later", log);

        Assert.Equal(RetCode.OK, broker.Subscribe("!name", thrower, nameof(Recorder.OnThrowing)));
        Assert.Equal(RetCode.OK, broker.Subscribe("@name", later, nameof(Recorder.OnEvent)));

        // No throw escapes: the hook decided.
        broker.Trigger("name");

        Assert.Equal(["name"], broker.Exceptions);
        Assert.Equal(expectLaterSubscriberRan ? 1 : 0, later.CallCount);
    }

    /// <summary>
    /// The prepare hook sees the handler's declared argument count and can fill slots the trigger did not
    /// supply.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The hook exists so a derived broker can inject context - the legacy's use is supplying the sender -
    /// and its <c>ConsumedArgumentCount</c> tells the broker how many leading slots it claimed, so the
    /// trigger's own arguments land after them. Asserted by having the hook claim slot one and write a
    /// value into it, then checking the handler received that value first and the trigger's argument
    /// second.
    /// </para>
    /// <para>
    /// The declared count in the hook log is what proves the hook is handed the real slot array rather
    /// than a copy sized to the trigger's arguments.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePrepareHookCanClaimAndFillLeadingArgumentSlots()
    {
        List<string> log = [];
        HookedBroker broker = new()
        {
            PrepareConsumes = 1,
            PrepareSlotOneValue = 77L,
        };

        Recorder target = new("t", log);

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(Recorder.OnTwoArguments)));

        broker.Trigger("name", "supplied");

        Assert.Equal(["name/2"], broker.Prepared);
        Assert.Equal(["t(77,supplied)"], log);
    }

    /// <summary>
    /// A PREVENTING prepare hook skips only its own subscriber, not the whole dispatch.
    /// </summary>
    /// <remarks>
    /// The scope is per-subscriber, which is what makes the hook usable as a filter: a derived broker can
    /// decline to invoke one target while leaving the rest of the dispatch intact. Asserted with two
    /// subscribers so the difference from the triggering hook - which stops everything - is visible.
    /// </remarks>
    [Fact]
    public void APreventingPrepareHookSkipsOnlyItsOwnSubscriber()
    {
        List<string> log = [];
        HookedBroker broker = new() { PrepareResult = RetCode.PREVENT };

        Recorder first = new("first", log);
        Recorder second = new("second", log);

        Assert.Equal(RetCode.OK, broker.Subscribe("!name", first, nameof(Recorder.OnEvent)));
        Assert.Equal(RetCode.OK, broker.Subscribe("@name", second, nameof(Recorder.OnEvent)));

        broker.Trigger("name");

        // The hook ran for BOTH subscribers and prevented both, so neither handler executed - but the
        // dispatch walked the whole run rather than stopping at the first prevention.
        Assert.Equal(2, broker.Prepared.Count);
        Assert.Empty(log);
        Assert.Equal(0, first.CallCount);
        Assert.Equal(0, second.CallCount);
    }

    // ==============================================================================================
    //  14. THE ARGUMENT CONTEXT - ONE-BASED SEAMS
    // ==============================================================================================

    /// <summary>
    /// <see cref="EventArgumentContext"/> slots are ONE-BASED, and index zero is out of range.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The AAP calls one-based translation the refactor's most dangerous mechanical hazard, and this is
    /// the seam a derived broker's prepare hook writes through. Index zero is the trap: it is the valid
    /// first index in C# and an INVALID one in PowerScript, so accepting it would let a zero-based hook
    /// write the wrong slot silently.
    /// </para>
    /// <para>
    /// Out of range returns <c>E_INVALID_ARGUMENT</c> rather than throwing, because the hook runs inside a
    /// dispatch where a throw would become the dispatch's exception - a much worse diagnosis than a code.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheArgumentContextIsOneBasedAndRejectsOutOfRangeSlots()
    {
        List<string> log = [];
        HookedBroker broker = new();
        Recorder target = new("t", log);

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(Recorder.OnTwoArguments)));

        SlotProbe probe = new();
        HookedProbeBroker probeBroker = new(probe);

        Assert.Equal(RetCode.OK, probeBroker.Subscribe("name", target, nameof(Recorder.OnTwoArguments)));

        probeBroker.Trigger("name");

        Assert.Equal(2, probe.DeclaredCount);

        // One-based: 1 and 2 are valid, 0 and 3 are not.
        Assert.Equal(RetCode.OK, probe.SetResults[1]);
        Assert.Equal(RetCode.OK, probe.SetResults[2]);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, probe.SetResults[0]);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, probe.SetResults[3]);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, probe.SetResults[-1]);

        // A read outside the range answers null rather than throwing.
        Assert.Null(probe.ReadOutOfRange);

        // A value written to slot 1 is readable from slot 1.
        Assert.Equal(11L, probe.ReadBackSlotOne);
    }

    /// <summary>
    /// A prepare hook that probes the argument-slot seam and records what it saw.
    /// </summary>
    private sealed class SlotProbe
    {
        /// <summary>Gets the declared argument count the hook was handed.</summary>
        internal int DeclaredCount { get; set; } = -1;

        /// <summary>Gets the codes each attempted slot write answered, keyed by slot.</summary>
        internal Dictionary<int, long> SetResults { get; } = [];

        /// <summary>Gets the value read back from slot one.</summary>
        internal object? ReadBackSlotOne { get; set; }

        /// <summary>Gets the value read from an out-of-range slot.</summary>
        internal object? ReadOutOfRange { get; set; } = "unset";
    }

    /// <summary>
    /// A derived broker whose prepare hook drives a <see cref="SlotProbe"/>.
    /// </summary>
    private sealed class HookedProbeBroker : EventBroker
    {
        private readonly SlotProbe _probe;

        /// <summary>Initializes a new instance of the <see cref="HookedProbeBroker"/> class.</summary>
        /// <param name="probe">The probe to record into.</param>
        internal HookedProbeBroker(SlotProbe probe) => _probe = probe;

        /// <inheritdoc/>
        protected override long OnPrepare(string name, object target, EventArgumentContext arguments)
        {
            _probe.DeclaredCount = arguments.DeclaredArgumentCount;

            foreach (int slot in new[] { -1, 0, 1, 2, 3 })
            {
                _probe.SetResults[slot] = arguments.SetArgument(slot, slot == 1 ? 11L : "written");
            }

            _probe.ReadBackSlotOne = arguments.GetArgument(1);
            _probe.ReadOutOfRange = arguments.GetArgument(0);

            arguments.ConsumedArgumentCount = 0;
            return RetCode.OK;
        }
    }

    // ==============================================================================================
    //  15. THE SUPPORTING TYPES
    // ==============================================================================================

    /// <summary>
    /// <see cref="EventSubscription"/> defaults are inert and its lifetime is a projection of its
    /// namespace.
    /// </summary>
    /// <remarks>
    /// The entry is exposed publicly because the legacy object's state is public, so its defaults are part
    /// of the contract. The lifetime projection is the same derivation the options and topic types use,
    /// and asserting all three agree is what stops one of them drifting.
    /// </remarks>
    [Fact]
    public void TheSubscriptionEntryDefaultsAreInertAndItsLifetimeIsDerived()
    {
        EventSubscription entry = new();

        Assert.Equal(SubscriptionNamespaces.None, entry.Namespace);
        Assert.Equal(string.Empty, entry.Name);
        Assert.Equal(CaptureMode.Unhandled, entry.Capture);
        Assert.Equal(Priorities.Normal, entry.Priority);
        Assert.Null(entry.Target);
        Assert.Equal(string.Empty, entry.ClassChain);
        Assert.Equal(string.Empty, entry.HandlerName);
        Assert.Null(entry.Handler);
        Assert.False(entry.IsInvoking);
        Assert.False(entry.IsDisabled);
        Assert.False(entry.IsInvalid);
        Assert.Equal(SubscriptionLifetime.Transient, entry.Lifetime);

        Assert.Equal(
            SubscriptionLifetime.Persistent,
            new EventSubscription { Namespace = SubscriptionNamespaces.Persistent }.Lifetime);
    }

    /// <summary>
    /// The three mutable flags on a subscription entry are settable, because the broker mutates them in
    /// place.
    /// </summary>
    /// <remarks>
    /// The asymmetry with the init-only members is deliberate and worth pinning: identity is fixed at
    /// registration while STATE changes during dispatch - the invoking flag is set and restored around
    /// every invocation, and the disabled and invalid flags are toggled by the disable and collect paths.
    /// An all-init-only record would make the broker unable to do any of it.
    /// </remarks>
    [Fact]
    public void TheThreeStateFlagsAreMutableWhileIdentityIsInitOnly()
    {
        EventSubscription entry = new()
        {
            Name = "name",
            Namespace = "ns",
            HandlerName = "onevent",
        };

        entry.IsInvoking = true;
        entry.IsDisabled = true;
        entry.IsInvalid = true;

        Assert.True(entry.IsInvoking);
        Assert.True(entry.IsDisabled);
        Assert.True(entry.IsInvalid);

        // Identity has no setter.
        foreach (string identityMember in new[]
        {
            nameof(EventSubscription.Name),
            nameof(EventSubscription.Namespace),
            nameof(EventSubscription.Capture),
            nameof(EventSubscription.Priority),
            nameof(EventSubscription.Target),
            nameof(EventSubscription.ClassChain),
            nameof(EventSubscription.HandlerName),
            nameof(EventSubscription.Handler),
        })
        {
            PropertyInfo property = typeof(EventSubscription).GetProperty(identityMember)!;

            // An init-only accessor is an ordinary setter carrying an IsExternalInit modreq on its
            // return parameter, so the modifier list - not the attribute list - is what identifies it.
            Assert.True(
                property.SetMethod is null || property.SetMethod.ReturnParameter
                    .GetRequiredCustomModifiers()
                    .Any(modifier => modifier == typeof(System.Runtime.CompilerServices.IsExternalInit)),
                $"{identityMember} must be init-only: it is fixed at registration.");
        }
    }

    /// <summary>
    /// <see cref="DefaultReturnValue"/> is a value-equal record with inert defaults.
    /// </summary>
    /// <remarks>
    /// It is the table entry behind the per-name default, and value equality is what lets the broker
    /// replace an entry rather than accumulate. The null-value default matters because null is a LEGAL
    /// default return value, distinct from having no entry at all.
    /// </remarks>
    [Fact]
    public void TheDefaultReturnValueRecordIsValueEqualWithInertDefaults()
    {
        DefaultReturnValue entry = new();

        Assert.Equal(string.Empty, entry.Name);
        Assert.Null(entry.Value);

        DefaultReturnValue named = new() { Name = "name", Value = 42L };

        Assert.Equal(named, new DefaultReturnValue { Name = "name", Value = 42L });
        Assert.NotEqual(named, named with { Name = "other" });
        Assert.NotEqual(named, named with { Value = 43L });
        Assert.Equal(named.GetHashCode(), new DefaultReturnValue { Name = "name", Value = 42L }.GetHashCode());
    }

    /// <summary>
    /// <see cref="EventBroker.BuildPersistentSparingFilter"/> appends a negated persistent namespace
    /// criterion, and leaves an already-qualified name alone.
    /// </summary>
    /// <param name="requested">The requested name.</param>
    /// <param name="expected">The filter it produces.</param>
    /// <remarks>
    /// <para>
    /// The helper builds the filter that clears transient subscriptions while SPARING persistent ones -
    /// which is the sweep the threading layer performs. It works by appending <c>.^persistent</c>, using
    /// the filter grammar's namespace negation.
    /// </para>
    /// <para>
    /// The pass-through case is the substantive one: a name that ALREADY carries a namespace separator is
    /// returned unchanged, because appending a second criterion would produce a namespace of
    /// <c>"ns.^persistent"</c> rather than a negation - silently selecting nothing. So a caller who has
    /// already qualified the name keeps their own criterion, and the sparing is their responsibility.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("itemchanged", "itemchanged.^persistent")]
    [InlineData("", ".^persistent")]
    [InlineData(null, ".^persistent")]
    [InlineData("itemchanged.ns", "itemchanged.ns")]
    [InlineData("itemchanged.", "itemchanged.")]
    [InlineData("itemchanged.^persistent", "itemchanged.^persistent")]
    public void ThePersistentSparingFilterAppendsANegatedNamespaceUnlessAlreadyQualified(
        string? requested,
        string expected)
    {
        Assert.Equal(expected, EventBroker.BuildPersistentSparingFilter(requested));
    }

    /// <summary>
    /// The filter the sparing helper builds parses as a filter that spares persistent subscriptions.
    /// </summary>
    /// <remarks>
    /// The helper's output is only useful if the filter grammar reads it the way the helper intends, so the
    /// two are asserted together. This is the test that would catch a change to either the negation symbol
    /// or the namespace separator - each of which lives in a different file from the helper.
    /// </remarks>
    [Fact]
    public void TheSparingFilterParsesAsANamespaceNegatingFilter()
    {
        string built = EventBroker.BuildPersistentSparingFilter("itemchanged");

        Assert.Equal(RetCode.OK, SubscriptionTopic.ParseFilter(built, out SubscriptionTopic? parsed));
        Assert.NotNull(parsed);

        Assert.Equal("itemchanged", parsed!.LegacyName);
        Assert.True(parsed.NegateNamespace);
        Assert.False(parsed.NegateName);
        Assert.Equal(SubscriptionNamespaces.Persistent, parsed.Namespace);

        // It selects a transient subscription and spares a persistent one.
        Assert.True(parsed.Matches("itemchanged", SubscriptionNamespaces.None));
        Assert.True(parsed.Matches("itemchanged", "other"));
        Assert.False(parsed.Matches("itemchanged", SubscriptionNamespaces.Persistent));
    }

    /// <summary>
    /// <see cref="IAssertionDetail"/> is a public interface, so a foreign exception can supply its own
    /// annotation detail.
    /// </summary>
    /// <remarks>
    /// The broker prefers an exception's own assertion detail over a generic type-name-plus-message
    /// rendering, and the interface is the extension point that makes that possible across an assembly
    /// boundary. Asserted as a shape because the broker's use of it is exercised only when an assertion
    /// exception actually flows through - which the Diagnostics project's own suite covers.
    /// </remarks>
    [Fact]
    public void TheAssertionDetailInterfaceIsPubliclyImplementable()
    {
        Assert.True(typeof(IAssertionDetail).IsInterface);
        Assert.True(typeof(IAssertionDetail).IsPublic);
    }

    // ==============================================================================================
    //  16. ROBUSTNESS
    // ==============================================================================================

    /// <summary>
    /// The broker survives an arbitrary sequence of operations without throwing, and every code it answers
    /// is a declared one.
    /// </summary>
    /// <remarks>
    /// A sweep over the registration and modification surface, driven with degenerate and valid arguments
    /// mixed together. Its value is not any single assertion but the absence of a throw: the broker is
    /// reached from the DataWindow event chain, where an unexpected exception would surface as a failed
    /// user edit rather than as a diagnosable fault.
    /// </remarks>
    [Fact]
    public void TheBrokerSurvivesAnArbitraryOperationSequence()
    {
        List<string> log = [];
        EventBroker broker = new();
        Recorder target = new("t", log);

        long[] codes =
        [
            broker.Subscribe("name", target, nameof(Recorder.OnEvent)),
            broker.Subscribe("name", target, nameof(Recorder.OnEvent)),
            broker.Subscribe("-!*name.persistent", target, nameof(Recorder.OnEvent)),
            broker.Subscribe(string.Empty, target, nameof(Recorder.OnEvent)),
            broker.Subscribe("name", null, nameof(Recorder.OnEvent)),
            broker.Subscribe("name", target, "missing"),
            broker.Disable("name", disabled: true),
            broker.Disable("name", disabled: false),
            broker.Disable(target, disabled: true, excluding: true),
            broker.Disable(disabled: false),
            broker.SetDefaultReturnValue("name", 0L),
            broker.SetDefaultReturnValue(null),
            broker.Unsubscribe("nonexistent"),
            broker.Unsubscribe(target, "missing"),
            broker.Unsubscribe("name", target, nameof(Recorder.OnEvent)),
            broker.Unsubscribe(),
        ];

        long[] declared =
        [
            RetCode.OK,
            RetCode.E_INVALID_ARGUMENT,
            RetCode.E_INVALID_OBJECT,
            RetCode.E_EVENT_NOT_FOUND,
            RetCode.FAILED,
        ];

        foreach (long code in codes)
        {
            Assert.Contains(code, declared);
        }

        // Still usable afterwards.
        Assert.Equal(RetCode.OK, broker.Subscribe("after", target, nameof(Recorder.OnEvent)));
        broker.Trigger("after");
        Assert.True(target.CallCount > 0);
    }

    /// <summary>
    /// Two brokers are independent: a subscription on one is invisible to the other.
    /// </summary>
    /// <remarks>
    /// The legacy object is instantiated per consumer - <c>se_cst_dw</c> holds its own - so state must not
    /// be static. The one genuinely shared piece is the ambient <c>Current</c>, which is a flow-local
    /// pointer rather than shared state, and it is set to whichever broker is dispatching. Asserted because
    /// a mistakenly static subscription list would pass every single-broker test in this file.
    /// </remarks>
    [Fact]
    public void TwoBrokersAreIndependent()
    {
        List<string> firstLog = [];
        List<string> secondLog = [];

        EventBroker first = new();
        EventBroker second = new();

        Assert.Equal(RetCode.OK, first.Subscribe("name", new Recorder("first", firstLog), nameof(Recorder.OnEvent)));

        Assert.True(first.IsSubscribed("name"));
        Assert.False(second.IsSubscribed("name"));

        second.Trigger("name");
        Assert.Empty(firstLog);
        Assert.Empty(secondLog);

        first.Trigger("name");
        Assert.Equal(["first"], firstLog);
    }
}
