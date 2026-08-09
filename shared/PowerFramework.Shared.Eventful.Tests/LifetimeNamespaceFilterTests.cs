// ==================================================================================================
//  LifetimeNamespaceFilterTests.cs - THE LIFETIME FILTER, WHICH A BOOLEAN FLAG CANNOT MODEL
//  ------------------------------------------------------------------------------------------------
//  UNIT UNDER TEST   PowerFramework.Shared.Eventful.EventBroker            the APPLIED filter engine
//                    PowerFramework.Shared.Eventful.SubscriptionTopic      the grammar it applies
//
//  ORACLE            ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru
//                      :L993-L1090   _of_modify, the ONE routine behind all fourteen overloads
//                      :L1000-L1015  the filter grammar, which is NOT the subscribe grammar
//                      :L1021-L1023  the single rejection: a negated EMPTY NAME
//                      :L1030-L1053  the composed match, and the OUTER excluding inversion
//                      :L1054-L1072  what each kind does with a match: remove versus RETAIN
//                      :L1073-L1078  the lexical bounds recomputation
//                      :L747-L783    of_issubscribed, which skips invalid AND disabled entries
//                    ws_objects/pfw.thread.pbl.src/n_cst_threading.sru
//                      :L544         of_off()      -> of_Off(".^persistent")
//                      :L593-L598    of_off(name)  -> as given, or name + ".^persistent"
//                    ws_objects/pfw.thread.pbl.src/n_cst_thread.sru:L552, :L561   the recurrences
//                    ws_objects/pfw.tests.pbl.src/w_test_eventful.srw
//                      :L306-L342    the thirteen documented forms, for UNSUBSCRIBE
//                      :L297-L303    the prose gloss: a leading negation PRESERVES what it names
//                      :L153-L189    the same thirteen, for DISABLE
//
//  WHY THIS FILE IS THE HIGHEST-RISK SUITE IN THE FOLDER
//  ------------------------------------------------------------------------------------------------
//  Because the wrong model of subscription lifetime is a PLAUSIBLE one. A port that stores lifetime as
//  a boolean - "this subscription is persistent, that one is not" - and implements the bulk teardown as
//  "remove everything not flagged persistent" produces exactly the right answer for the framework's own
//  teardown call, passes every naive "unsubscribe removes things" test, and is nonetheless wrong.
//
//  It is wrong because the legacy has no lifetime flag at all. Lifetime is an ordinary NAMESPACE, and
//  the teardown is an ordinary FILTER that happens to negate it: n_cst_threading.sru:L544 passes the
//  string ".^persistent" - an empty name, which matches every name, and a negated namespace criterion,
//  which inverts the namespace comparison. Nothing in the repository ever subscribes INTO `persistent`;
//  it is a reserved namespace whose entire purpose is to survive that one filter. So the "lifetime"
//  concept is a CONSEQUENCE of the filter grammar, not a field, and the grammar is strictly richer than
//  any boolean:
//
//      THERE ARE THREE NAMESPACE STATES, NOT TWO
//        ABSENT                "clicked"    no separator at all, so the namespace criterion is not
//                                           applied and EVERY namespace matches       (:L1004-L1005)
//        PRESENT-BUT-EMPTY     "clicked."   a separator with nothing after it, compared POSITIVELY
//                                           against the empty namespace, so ONLY entries that have no
//                                           namespace match                            (:L1002, :L1042)
//        NEGATED-EMPTY         "clicked.^"  the same empty criterion, NEGATED, so only entries that DO
//                                           have a namespace match                (:L1012-L1015, :L1040)
//
//  A boolean model collapses all three onto one, and it collapses the second half of the idiom with
//  them: n_cst_threading.sru:L593-L598 appends the reserved namespace ONLY when the supplied name
//  carries no separator, and passes a name that already has one through AS GIVEN - which is the one way
//  a persistent subscription can be asked for explicitly and removed. A flag has no way to express
//  "spare unless named directly".
//
//  DECISION (AAP 0.7.3 C-K) - THE BOUNDARY, NAMED
//  ------------------------------------------------------------------------------------------------
//  This is why the cross-service contract carries SEQUENCE, NAME and LIFETIME as three separate fields
//  (AAP 0.6.1.2, 0.3.4). The legacy FUSES all three into one opaque topic string: a lexical ordering
//  prefix, the logical event name, and the lifetime as a namespace suffix. Transmit the fused string and
//  the ordering becomes invisible; parse it at the far end and the contract has an undocumented grammar.
//  Lifetime is therefore a first-class wire field, and the fused legacy spelling is reconstituted ONLY
//  at the compatibility edge. That decomposition is only safe if the SEMANTIC of the suffix is pinned
//  first - which is what this file does. Everything asserted here is the meaning the wire field must
//  carry, and SubscriptionLifetime.Persistent is the decoded form of the very namespace these filters
//  negate.
//
//  WHAT THIS FILE ASSERTS, AND HOW IT OBSERVES IT
//  ------------------------------------------------------------------------------------------------
//  Survival is observed by TRIGGERING the events afterwards and reading the shared DispatchLog, never by
//  reaching into the broker's table. That observes the behaviour a consumer actually depends on, and it
//  pins dispatch ORDER at the same time. IsSubscribed is used only as a secondary signal, and the fact
//  that it answers FALSE for a disabled-but-retained subscription is asserted here as CORRECT rather
//  than worked around - see the disable section.
//
//  RULES POSITION
//  ------------------------------------------------------------------------------------------------
//  review_rules returns "No user rules provided." No user rule governs this file and none is invented;
//  enterprise-standard practice applies and the constraints cited inline are the AAP's own (0.7.3):
//    C-B  Every assertion below treats the negation semantics as CORRECT and carries the locator that
//         makes it legacy behaviour rather than an implementation choice. Three things are asserted as
//         correct specifically because they look like defects: a present-but-empty namespace criterion
//         is NOT the same as an absent one, a disabled subscription is RETAINED rather than removed,
//         and a bulk unsubscribe deliberately SPARES a namespace.
//    C-D  No deferred capability appears. Nothing here references the dynamic script-invoker family
//         (ScriptBridge) or any other deferred service; the broker itself is pure PowerScript in the
//         legacy and carries no native binding, so there is nothing to substitute.
//    C-H  Nullable reference types and TreatWarningsAsErrors apply. This suite passes NULL targets
//         deliberately, and does so through the real object? signatures - there is no suppression, no
//         null-forgiving operator on a broker argument and no NoWarn anywhere in this file.
//    C-K  The boundary decision above is stated rather than left implicit.
//
//  DETERMINISM (AAP 0.6.7)
//  No clock, no GUID, no random, no thread, no file, no network and no database is touched. Every row
//  builds a fresh broker, fresh subscribers and a fresh log, so no row can observe another's state.
//
//  RELATIONSHIP TO ITS SIBLINGS - read together, not instead of
//    SubscriptionTopicGrammarTests   owns the PARSE level: that ".^persistent" DECODES to a negated
//                                    persistent criterion, and that the three namespace states are
//                                    three distinct topics.
//    EventBrokerTests                owns the fourteen-overload enumeration
//                                    (EachUnsubscribeOverloadRemovesWhatItsArgumentsDescribe and
//                                    EachDisableOverloadSelectsWhatItsArgumentsDescribe) and the
//                                    BuildPersistentSparingFilter string shape.
//    THIS FILE                       owns the APPLIED engine: what a populated broker actually does
//                                    when each documented form is handed to it, observed through
//                                    dispatch. It covers the disable kind only insofar as the filter
//                                    grammar is shared, which per :L993 is entirely.
// ==================================================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.Shared.Eventful.Tests;

/// <summary>
/// Characterization tests for the lifetime-and-namespace filter grammar as the broker applies it -
/// <see cref="EventBroker.Unsubscribe(string)"/>, <see cref="EventBroker.Disable(string, bool)"/> and
/// <see cref="EventBroker.BuildPersistentSparingFilter"/> - observed through real dispatch.
/// </summary>
public class LifetimeNamespaceFilterTests
{
    // ==============================================================================================
    //  THE FIXED SPREAD
    //
    //  Five subscriptions, chosen so that all thirteen documented forms select five DIFFERENT sets.
    //  Two names and three namespace states are the minimum that distinguishes them: with only one
    //  name the negated-name forms collapse onto the unqualified ones, and with only one namespace
    //  state the present-but-empty and negated-empty forms collapse onto each other.
    //
    //  The LABEL of each subscriber is its own topic string, so a failure message names the exact
    //  subscription that survived or did not.
    // ==============================================================================================

    /// <summary>The oracle's event name throughout <c>w_test_eventful.srw:L306-L342</c>.</summary>
    private const string TargetName = "clicked";

    /// <summary>A second event name, ordinally BEFORE <see cref="TargetName"/> ('h' &lt; 'l').</summary>
    private const string OtherName = "changed";

    /// <summary>The oracle's namespace throughout the same matrix.</summary>
    private const string TargetNamespace = "myns";

    /// <summary>A third namespace, present so that "not myns" and "has no namespace" differ.</summary>
    private const string ThirdNamespace = "otherns";

    /// <summary>The target name with NO namespace.</summary>
    private const string TargetPlain = "clicked";

    /// <summary>The target name inside the target namespace.</summary>
    private const string TargetInTargetNamespace = "clicked.myns";

    /// <summary>The target name inside a third namespace.</summary>
    private const string TargetInThirdNamespace = "clicked.otherns";

    /// <summary>The other name with NO namespace.</summary>
    private const string OtherPlain = "changed";

    /// <summary>The other name inside the target namespace.</summary>
    private const string OtherInTargetNamespace = "changed.myns";

    // ==============================================================================================
    //  THE RESERVED-LIFETIME SPREAD
    //
    //  Two persistent subscriptions under DIFFERENT names, plus two that are not persistent - one with
    //  no namespace at all and one in an unrelated namespace. The two different names matter: they are
    //  what proves the bulk form spares by NAMESPACE and not by name.
    // ==============================================================================================

    /// <summary>
    /// A transient subscription with no namespace, under the same name as
    /// <see cref="PersistentAlpha"/>.
    /// </summary>
    private const string TransientAlpha = "alpha";

    /// <summary>A persistent subscription. Nothing in the legacy repository subscribes here; the port may.</summary>
    private const string PersistentAlpha = "alpha.persistent";

    /// <summary>A second persistent subscription, under a DIFFERENT name.</summary>
    private const string PersistentBeta = "beta.persistent";

    /// <summary>A transient subscription in an unrelated namespace.</summary>
    private const string TransientGamma = "gamma.other";

    /// <summary>A transient subscription used only by the nothing-is-persistent case.</summary>
    private const string TransientBetaOther = "beta.other";

    /// <summary>A bare transient subscription used only by the nothing-is-persistent case.</summary>
    private const string TransientGammaPlain = "gamma";

    // ==============================================================================================
    //  FILTER SPELLINGS
    //
    //  Written as literals so that a member-data row reads exactly as the oracle's own call does, and
    //  guarded by TheFilterSpellingsAgreeWithTheSharedConstants below - which is what would catch a
    //  change to TopicSymbols.Negation, TopicSymbols.NamespaceSeparator or
    //  SubscriptionNamespaces.Persistent that these literals had silently drifted from. A const string
    //  cannot be composed from a const char in C#, so the guard test replaces the composition.
    // ==============================================================================================

    /// <summary><c>n_cst_threading.sru:L544</c> - the framework's own bulk teardown filter.</summary>
    private const string SparingFilter = ".^persistent";

    /// <summary>The same criterion NOT negated: the negative control for <see cref="SparingFilter"/>.</summary>
    private const string PersistentOnlyFilter = ".persistent";

    /// <summary>
    /// <c>n_cst_threading.sru:L596</c> - the by-name sparing filter for
    /// <see cref="TransientAlpha"/>.
    /// </summary>
    private const string AlphaSparingFilter = "alpha.^persistent";

    // ==============================================================================================
    //  ARRANGEMENT AND OBSERVATION HELPERS
    //
    //  Each returns fresh state. The spread properties allocate a NEW array per read rather than
    //  handing out a shared static one, so no test can mutate what another reads.
    // ==============================================================================================

    /// <summary>The five-way spread the thirteen-form matrix is measured against, in subscription order.</summary>
    private static string[] SpreadTopics =>
    [
        TargetPlain,
        TargetInTargetNamespace,
        TargetInThirdNamespace,
        OtherPlain,
        OtherInTargetNamespace
    ];

    /// <summary>
    /// Every label the spread can produce, in DISPATCH order - which is the ordinal name order
    /// (<c>changed</c> before <c>clicked</c>) and then subscription order within each name.
    /// </summary>
    private static string[] SpreadDispatchOrder =>
    [
        OtherPlain,
        OtherInTargetNamespace,
        TargetPlain,
        TargetInTargetNamespace,
        TargetInThirdNamespace
    ];

    /// <summary>The reserved-lifetime spread, in subscription order.</summary>
    private static string[] LifetimeSpreadTopics =>
    [
        TransientAlpha,
        PersistentAlpha,
        PersistentBeta,
        TransientGamma
    ];

    /// <summary>The event names the reserved-lifetime spread dispatches under, in ordinal order.</summary>
    private static string[] LifetimeEventNames => ["alpha", "beta", "gamma"];

    /// <summary>The event names the five-way spread dispatches under, in ordinal order.</summary>
    private static string[] SpreadEventNames => [OtherName, TargetName];

    /// <summary>
    /// Builds a fresh broker with one fresh <see cref="RecordingSubscriber"/> per topic, each labelled
    /// with its own topic string.
    /// </summary>
    /// <param name="topics">The topics to subscribe, in the order they should be subscribed.</param>
    /// <returns>The broker, its log, and the subscribers positionally matching <paramref name="topics"/>.</returns>
    /// <remarks>
    /// Every subscription is asserted to have succeeded, so a spread that failed to arrange fails as an
    /// arrangement error rather than surfacing later as a mysteriously empty log. The broker is handed to
    /// each subscriber so an in-handler interceptor has it without depending on the ambient
    /// <see cref="EventBroker.Current"/>.
    /// </remarks>
    private static (EventBroker Broker, DispatchLog Log, RecordingSubscriber[] Subscribers) Arrange(
        params string[] topics)
    {
        EventBroker broker = new();
        DispatchLog log = new();
        RecordingSubscriber[] subscribers = new RecordingSubscriber[topics.Length];

        for (int index = 0; index < topics.Length; index++)
        {
            string topic = topics[index];
            RecordingSubscriber subscriber = new(log, topic, broker) { Topic = topic };

            Assert.Equal(
                RetCode.OK,
                broker.Subscribe(topic, subscriber, RecordingHandlerNames.NoArguments));

            subscribers[index] = subscriber;
        }

        return (broker, log, subscribers);
    }

    /// <summary>
    /// Clears the log, triggers each named event in turn, and returns the labels that ran.
    /// </summary>
    /// <param name="broker">The broker to dispatch through.</param>
    /// <param name="log">The log to read.</param>
    /// <param name="eventNames">The event names to trigger, in the order to trigger them.</param>
    /// <returns>The surviving subscribers' labels, in dispatch order.</returns>
    /// <remarks>
    /// A snapshot is taken with <see cref="Enumerable.ToArray{TSource}"/> because
    /// <see cref="DispatchLog.Labels"/> is a live projection, and a caller that captured it before a
    /// further dispatch should keep what it captured.
    /// </remarks>
    private static string[] Observe(EventBroker broker, DispatchLog log, params string[] eventNames)
    {
        log.Clear();

        foreach (string eventName in eventNames)
        {
            broker.Trigger(eventName);
        }

        return [.. log.Labels];
    }

    /// <summary>Observes the five-way spread across both of its event names.</summary>
    /// <param name="broker">The broker to dispatch through.</param>
    /// <param name="log">The log to read.</param>
    /// <returns>The surviving labels, in dispatch order.</returns>
    private static string[] ObserveSpread(EventBroker broker, DispatchLog log) =>
        Observe(broker, log, SpreadEventNames);

    /// <summary>Observes the reserved-lifetime spread across all three of its event names.</summary>
    /// <param name="broker">The broker to dispatch through.</param>
    /// <param name="log">The log to read.</param>
    /// <returns>The surviving labels, in dispatch order.</returns>
    private static string[] ObserveLifetimeSpread(EventBroker broker, DispatchLog log) =>
        Observe(broker, log, LifetimeEventNames);

    /// <summary>Parses a filter under the filter grammar, asserting that it was accepted.</summary>
    /// <param name="filter">The filter spelling.</param>
    /// <returns>The decoded criteria.</returns>
    private static SubscriptionTopic ParseFilter(string filter)
    {
        Assert.Equal(RetCode.OK, SubscriptionTopic.ParseFilter(filter, out SubscriptionTopic? parsed));
        Assert.NotNull(parsed);

        return parsed;
    }

    /// <summary>
    /// Decodes one of this suite's arrangement topics under the SUBSCRIBE grammar, asserting acceptance.
    /// </summary>
    /// <param name="topic">
    /// The topic as it was handed to <see cref="EventBroker.Subscribe(string, object?, string)"/>.
    /// </param>
    /// <returns>The decoded topic, from which the stored name and namespace are read.</returns>
    /// <remarks>
    /// Deliberately <see cref="SubscriptionTopic.ParseSubscription"/> and not
    /// <see cref="SubscriptionTopic.ParseFilter"/>, even though every topic in this suite happens to decode
    /// identically under both. The two grammars genuinely differ - <c>"clicked."</c> is a legal filter and
    /// an illegal subscription - so reading a subscription's stored name and namespace through the filter
    /// grammar would be a category error that only stays invisible while the arrangements remain simple.
    /// </remarks>
    private static SubscriptionTopic ParseSubscriptionTopic(string topic)
    {
        Assert.Equal(
            RetCode.OK,
            SubscriptionTopic.ParseSubscription(topic, out SubscriptionTopic? parsed));
        Assert.NotNull(parsed);

        return parsed;
    }

    // ==============================================================================================
    //  SECTION 1 - THE TWO SHARPEST ROWS, AND THE THREE-STATE PROOF
    //
    //  Authored before anything else in this file, deliberately. These are the assertions a boolean
    //  lifetime model cannot satisfy, and every other assertion in the file is downstream of them: if a
    //  present-but-empty namespace criterion is indistinguishable from an absent one, then the honoured-
    //  as-given branch of the threading idiom has nowhere to live either, and the reserved-lifetime
    //  cases below become coincidences rather than consequences.
    // ==============================================================================================

    /// <summary>
    /// The filter spellings this suite hard-codes still agree with the shared symbol and namespace
    /// constants they were written from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rows below spell their filters as literals so that each reads exactly as the oracle's own call
    /// does - <c>".^persistent"</c> is what <c>n_cst_threading.sru:L544</c> passes, character for
    /// character. C# cannot compose a <see langword="const"/> <see cref="string"/> from a
    /// <see langword="const"/> <see cref="char"/>, so the literals cannot be built from
    /// <see cref="TopicSymbols"/> at compile time; this test is what replaces that composition.
    /// </para>
    /// <para>
    /// It is the test that fails - loudly and in one place - if the negation symbol, the namespace
    /// separator or the reserved namespace name is ever changed. Without it, such a change would leave
    /// every row in this file still passing against a grammar that no longer exists, because the parser
    /// and the literals would have drifted apart in step.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFilterSpellingsAgreeWithTheSharedConstants()
    {
        Assert.Equal('.', TopicSymbols.NamespaceSeparator);
        Assert.Equal('^', TopicSymbols.Negation);
        Assert.Equal("persistent", SubscriptionNamespaces.Persistent, StringComparer.Ordinal);

        // The absent-namespace sentinel is the empty string. Asserted through its length rather than
        // against string.Empty, because two constants either side of Assert.Equal is exactly what
        // xUnit2000 forbids and there is no meaningful "expected" between them.
        Assert.Equal(0, SubscriptionNamespaces.None.Length);

        // The three composed literals, each rebuilt at run time from the constants it was written from.
        // The literal is the EXPECTED side throughout: it is what the oracle passes, and the composition
        // is what the port must still produce.
        Assert.Equal(
            SparingFilter,
            string.Concat(
                TopicSymbols.NamespaceSeparator.ToString(),
                TopicSymbols.Negation.ToString(),
                SubscriptionNamespaces.Persistent),
            StringComparer.Ordinal);

        Assert.Equal(
            PersistentOnlyFilter,
            string.Concat(TopicSymbols.NamespaceSeparator.ToString(), SubscriptionNamespaces.Persistent),
            StringComparer.Ordinal);

        Assert.Equal(
            AlphaSparingFilter,
            string.Concat(TransientAlpha, SparingFilter),
            StringComparer.Ordinal);

        // And the decoded lifetime of the reserved namespace, which is what the wire field carries.
        Assert.Equal(
            SubscriptionLifetime.Persistent,
            SubscriptionNamespaces.ToLifetime(SubscriptionNamespaces.Persistent));

        Assert.Equal(
            SubscriptionLifetime.Transient,
            SubscriptionNamespaces.ToLifetime(SubscriptionNamespaces.None));
    }

    /// <summary>
    /// SHARPEST ROW ONE. A PRESENT-BUT-EMPTY namespace criterion removes ONLY the entries that have no
    /// namespace - it does not behave like an absent criterion.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>w_test_eventful.srw:L330</c> documents <c>of_Off("clicked.")</c> as cancelling the named
    /// event's subscriptions that have NO namespace. Mechanically: <c>:L1000-L1002</c> finds the
    /// separator and takes everything after it as the namespace, which here is the empty string, so
    /// <c>bNoNamespace</c> is never set and the namespace test at <c>:L1038-L1044</c> DOES run - as a
    /// positive comparison against <c>""</c>.
    /// </para>
    /// <para>
    /// <b>PINNED LEGACY SEMANTIC (AAP 0.7.3 C-B).</b> This is correct behaviour, not a quirk to smooth
    /// over. The one entry removed is the one with no namespace; the two in namespaces survive, and so
    /// does everything under the other name.
    /// </para>
    /// </remarks>
    [Fact]
    public void APresentButEmptyNamespaceCriterionRemovesOnlyTheEntriesWithNoNamespace()
    {
        (EventBroker broker, DispatchLog log, _) = Arrange(SpreadTopics);

        // "clicked." - separator present, nothing after it.
        Assert.Equal(RetCode.OK, broker.Unsubscribe("clicked."));

        Assert.Equal(
            new[] { OtherPlain, OtherInTargetNamespace, TargetInTargetNamespace, TargetInThirdNamespace },
            ObserveSpread(broker, log),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// SHARPEST ROW TWO. A NEGATED-EMPTY namespace criterion removes ONLY the entries that DO have a
    /// namespace - the exact opposite selection, from a filter one character longer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>w_test_eventful.srw:L333</c> documents <c>of_Off("clicked.^")</c> as cancelling the named
    /// event's subscriptions that HAVE a namespace. The criterion is the same present-but-empty one,
    /// with <c>:L1012-L1015</c> lifting the leading <c>^</c> off the namespace half and
    /// <c>:L1040</c> turning the comparison into an inequality: "namespace is not empty".
    /// </para>
    /// <para>
    /// <b>PINNED LEGACY SEMANTIC (AAP 0.7.3 C-B).</b> Note that the negation applies to the NAMESPACE
    /// half alone - the name half is still compared positively, which is why the two entries under the
    /// other name survive even though one of them has a namespace.
    /// </para>
    /// </remarks>
    [Fact]
    public void ANegatedEmptyNamespaceCriterionRemovesOnlyTheEntriesThatHaveANamespace()
    {
        (EventBroker broker, DispatchLog log, _) = Arrange(SpreadTopics);

        // "clicked.^" - the same empty criterion, negated.
        Assert.Equal(RetCode.OK, broker.Unsubscribe("clicked.^"));

        Assert.Equal(
            new[] { OtherPlain, OtherInTargetNamespace, TargetPlain },
            ObserveSpread(broker, log),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// The three namespace states select three DIFFERENT sets from one arrangement, asserted in a single
    /// test so that no two of them can be collapsed without this failing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The anchor assertion of the whole file. Its three filters differ only in their trailing
    /// characters, and the oracle gives each a different documented meaning:
    /// <c>w_test_eventful.srw:L306</c> (absent - every namespace), <c>:L330</c> (present-but-empty -
    /// only entries with none) and <c>:L333</c> (negated-empty - only entries with one).
    /// </para>
    /// <para>
    /// The pairwise inequalities are asserted as well as the three expected sets. That is deliberate
    /// redundancy: if a future change made two of the states behave alike, the set assertions would name
    /// which row is wrong and the inequality would name what was lost, and a reader seeing both knows
    /// immediately that a state was collapsed rather than a single expectation mis-typed.
    /// </para>
    /// <para>
    /// <b>Under a boolean lifetime model these three filters cannot be told apart</b>, so this is the
    /// test the mutation check in this file's validation targets first.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheThreeNamespaceStatesSelectThreeDifferentSets()
    {
        (EventBroker absentBroker, DispatchLog absentLog, _) = Arrange(SpreadTopics);
        (EventBroker emptyBroker, DispatchLog emptyLog, _) = Arrange(SpreadTopics);
        (EventBroker negatedBroker, DispatchLog negatedLog, _) = Arrange(SpreadTopics);

        // :L306 - ABSENT. No separator, so the namespace criterion never participates and all three
        // subscriptions under this name go, whatever namespace they are in.
        Assert.Equal(RetCode.OK, absentBroker.Unsubscribe("clicked"));

        // :L330 - PRESENT-BUT-EMPTY. Only the one with no namespace goes.
        Assert.Equal(RetCode.OK, emptyBroker.Unsubscribe("clicked."));

        // :L333 - NEGATED-EMPTY. Only the two that have namespaces go.
        Assert.Equal(RetCode.OK, negatedBroker.Unsubscribe("clicked.^"));

        string[] afterAbsent = ObserveSpread(absentBroker, absentLog);
        string[] afterEmpty = ObserveSpread(emptyBroker, emptyLog);
        string[] afterNegated = ObserveSpread(negatedBroker, negatedLog);

        Assert.Equal(
            new[] { OtherPlain, OtherInTargetNamespace },
            afterAbsent,
            StringComparer.Ordinal);

        Assert.Equal(
            new[] { OtherPlain, OtherInTargetNamespace, TargetInTargetNamespace, TargetInThirdNamespace },
            afterEmpty,
            StringComparer.Ordinal);

        Assert.Equal(
            new[] { OtherPlain, OtherInTargetNamespace, TargetPlain },
            afterNegated,
            StringComparer.Ordinal);

        // No two of the three states may agree. Ordinal throughout, as SubscriptionOptions.NameComparer
        // and NamespaceComparer both are.
        Assert.NotEqual(afterAbsent, afterEmpty, StringComparer.Ordinal);
        Assert.NotEqual(afterEmpty, afterNegated, StringComparer.Ordinal);
        Assert.NotEqual(afterAbsent, afterNegated, StringComparer.Ordinal);

        // The two namespace-qualified states PARTITION what the absent state removes: between them they
        // remove exactly the three entries the absent form removes, and neither removes any other.
        // A model that could not distinguish them would have to break this arithmetic too.
        Assert.Equal(
            SpreadDispatchOrder.Length,
            afterEmpty.Length + afterNegated.Length - afterAbsent.Length);
    }

    /// <summary>
    /// The same three states, applied by <see cref="EventBroker.Disable(string, bool)"/> rather than by
    /// unsubscribe, select the same three sets - because both kinds share one filter engine.
    /// </summary>
    /// <remarks>
    /// <c>:L993</c> is one private routine taking a modification kind, and <c>w_test_eventful.srw</c>
    /// spells the whole matrix out twice for that reason - once for unsubscribe at <c>:L306-L342</c> and
    /// once for disable at <c>:L153-L189</c>. Asserting the sharpest three under the disable kind as well
    /// is what proves the sharing is real rather than incidental, and it is the reason the full matrix
    /// below is run twice off one member-data source.
    /// </remarks>
    [Fact]
    public void TheThreeNamespaceStatesSelectTheSameThreeSetsUnderTheDisableKind()
    {
        (EventBroker absentBroker, DispatchLog absentLog, _) = Arrange(SpreadTopics);
        (EventBroker emptyBroker, DispatchLog emptyLog, _) = Arrange(SpreadTopics);
        (EventBroker negatedBroker, DispatchLog negatedLog, _) = Arrange(SpreadTopics);

        Assert.Equal(RetCode.OK, absentBroker.Disable("clicked", true));
        Assert.Equal(RetCode.OK, emptyBroker.Disable("clicked.", true));
        Assert.Equal(RetCode.OK, negatedBroker.Disable("clicked.^", true));

        Assert.Equal(
            new[] { OtherPlain, OtherInTargetNamespace },
            ObserveSpread(absentBroker, absentLog),
            StringComparer.Ordinal);

        Assert.Equal(
            new[] { OtherPlain, OtherInTargetNamespace, TargetInTargetNamespace, TargetInThirdNamespace },
            ObserveSpread(emptyBroker, emptyLog),
            StringComparer.Ordinal);

        Assert.Equal(
            new[] { OtherPlain, OtherInTargetNamespace, TargetPlain },
            ObserveSpread(negatedBroker, negatedLog),
            StringComparer.Ordinal);
    }

    // ==============================================================================================
    //  SECTION 2 - THE RESERVED-LIFETIME IDIOM
    //
    //  The framework's own bulk teardown, and the one thing the AAP's wire contract calls "lifetime".
    //  Six verified call sites across three legacy objects use exactly two spellings:
    //
    //      of_off()        -> of_Off(".^persistent")                      n_cst_threading.sru:L544
    //                                                                    n_cst_threading_task.sru:L385
    //                                                                    n_cst_thread.sru:L552
    //      of_off(name)    -> if Pos(name,".") > 0 then of_Off(name)      n_cst_threading.sru:L593-L598
    //                         else of_Off(name + ".^persistent")          n_cst_threading_task.sru:L391
    //                                                                    n_cst_thread.sru:L561
    //
    //  Both are composed by EventBroker.BuildPersistentSparingFilter, and both are applied here to a
    //  populated broker so that the SELECTION - not just the string - is pinned.
    // ==============================================================================================

    /// <summary>
    /// The two directions of the reserved-namespace criterion: negated spares it, non-negated selects it.
    /// </summary>
    /// <remarks>
    /// Both rows run against the same arrangement in one theory, which is what proves the negation is
    /// doing the work rather than the persistent subscriptions being special in some other way. Row one
    /// is <c>n_cst_threading.sru:L544</c> verbatim; row two is the negative control - the identical
    /// filter with the negation removed - and its expected set is the exact complement of row one's.
    /// </remarks>
    public static TheoryData<string, string, string[]> ReservedNamespaceDirectionRows =>
        new()
        {
            // n_cst_threading.sru:L544 - the framework's own bulk teardown. Empty name matches every
            // name; the negated namespace criterion selects everything NOT in `persistent`.
            {
                SparingFilter,
                "n_cst_threading.sru:L544 - bulk teardown SPARES the reserved namespace",
                [PersistentAlpha, PersistentBeta]
            },

            // The negative control: the same criterion, NOT negated. Selects exactly what the sparing
            // filter spares, and spares exactly what it selects.
            {
                PersistentOnlyFilter,
                "the negative control - the identical criterion with the negation removed",
                [TransientAlpha, TransientGamma]
            }
        };

    /// <summary>
    /// The reserved-namespace criterion selects opposite sets with and without its negation, and the
    /// negated form is the one the framework's bulk teardown uses.
    /// </summary>
    /// <param name="filter">The filter spelling.</param>
    /// <param name="oracle">The locator and meaning, echoed into the test name for a readable failure.</param>
    /// <param name="expectedSurvivors">The labels that must still dispatch, in dispatch order.</param>
    /// <remarks>
    /// <b>PINNED LEGACY SEMANTIC (AAP 0.7.3 C-B) - a bulk unsubscribe deliberately SPARES a namespace.</b>
    /// That looks like an incomplete teardown and is not: <c>persistent</c> is a reserved namespace whose
    /// only purpose is to survive this call, which is precisely the lifetime concept the cross-service
    /// contract carries as its own field. Nothing in the legacy repository subscribes into it, so the
    /// spared set is empty in the legacy's own usage - and that is exactly why a port could break this
    /// without any legacy workload noticing.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ReservedNamespaceDirectionRows))]
    public void TheReservedNamespaceCriterionSelectsOppositeSetsWithAndWithoutItsNegation(
        string filter,
        string oracle,
        string[] expectedSurvivors)
    {
        Assert.NotEmpty(oracle);

        (EventBroker broker, DispatchLog log, _) = Arrange(LifetimeSpreadTopics);

        Assert.Equal(RetCode.OK, broker.Unsubscribe(filter));

        Assert.Equal(expectedSurvivors, ObserveLifetimeSpread(broker, log), StringComparer.Ordinal);
    }

    /// <summary>
    /// The filter the framework's no-argument bulk teardown builds spares both persistent subscriptions
    /// and removes both transient ones, and it is reached through
    /// <see cref="EventBroker.BuildPersistentSparingFilter"/> exactly as the threading layer reaches it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The end-to-end form of row one above: the filter is COMPOSED by the helper rather than spelled by
    /// the test, so this is the assertion that would catch the helper and the engine disagreeing. The two
    /// persistent subscriptions carry DIFFERENT names, which is what shows the sparing is by namespace
    /// and not by name.
    /// </para>
    /// <para>
    /// <b>PINNED LEGACY SEMANTIC (AAP 0.7.3 C-B), locator <c>n_cst_threading.sru:L544</c>.</b> Also
    /// asserted: passing <see langword="null"/> and passing the empty string produce the same filter,
    /// because the legacy's no-argument overload has no name to pass at all.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFrameworksBulkTeardownSparesEveryPersistentSubscription()
    {
        Assert.Equal(
            SparingFilter,
            EventBroker.BuildPersistentSparingFilter(null),
            StringComparer.Ordinal);

        Assert.Equal(
            SparingFilter,
            EventBroker.BuildPersistentSparingFilter(string.Empty),
            StringComparer.Ordinal);

        (EventBroker broker, DispatchLog log, _) = Arrange(LifetimeSpreadTopics);

        // Everything dispatches before the sweep, so the assertion below is about what the sweep did and
        // not about a spread that never worked.
        Assert.Equal(
            new[] { TransientAlpha, PersistentAlpha, PersistentBeta, TransientGamma },
            ObserveLifetimeSpread(broker, log),
            StringComparer.Ordinal);

        Assert.Equal(
            RetCode.OK,
            broker.Unsubscribe(EventBroker.BuildPersistentSparingFilter(null)));

        // The two persistent subscriptions STILL DISPATCH; the two transient ones are gone.
        Assert.Equal(
            new[] { PersistentAlpha, PersistentBeta },
            ObserveLifetimeSpread(broker, log),
            StringComparer.Ordinal);

        // And the survivors decode to the lifetime the wire contract carries as its own field, which is
        // the whole point of the reserved namespace (AAP 0.6.1.2 - the C-K decision in this file's header).
        Assert.Equal(SubscriptionLifetime.Persistent, ParseSubscriptionTopic(PersistentAlpha).Lifetime);
        Assert.Equal(SubscriptionLifetime.Persistent, ParseSubscriptionTopic(PersistentBeta).Lifetime);
        Assert.Equal(SubscriptionLifetime.Transient, ParseSubscriptionTopic(TransientAlpha).Lifetime);
        Assert.Equal(SubscriptionLifetime.Transient, ParseSubscriptionTopic(TransientGamma).Lifetime);
    }

    /// <summary>
    /// The by-name sparing pair: a name with NO separator has the reserved namespace appended, and a name
    /// that ALREADY carries one is honoured AS GIVEN.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>n_cst_threading.sru:L593-L598</c> is a two-branch function and both branches matter. Row one
    /// takes the append branch: a bare name becomes <c>name + ".^persistent"</c>, so the transient
    /// subscription under that name goes and the persistent one under the SAME name survives. Row two
    /// takes the pass-through branch: a name that already carries a separator is forwarded untouched, so
    /// a persistent subscription CAN be removed - but only when it is asked for explicitly.
    /// </para>
    /// <para>
    /// <b>This pair is the requirement's "honoured as given rather than having the default namespace
    /// appended", and it is the branch a boolean lifetime flag has no way to express at all</b> - a flag
    /// can answer "spare persistent" or "do not", but not "spare persistent unless the caller named the
    /// namespace themselves". Appending a second criterion to an already-qualified name would produce a
    /// namespace of <c>"persistent.^persistent"</c> and select nothing, which is why the legacy branches
    /// rather than always appending.
    /// </para>
    /// </remarks>
    public static TheoryData<string, string, string[]> ByNameSparingRows =>
        new()
        {
            // n_cst_threading.sru:L596 - APPEND branch. Pos(name,".") = 0, so the reserved namespace is
            // appended and the persistent subscription under the same name is spared.
            {
                TransientAlpha,
                AlphaSparingFilter,
                [PersistentAlpha, PersistentBeta, TransientGamma]
            },

            // n_cst_threading.sru:L594 - PASS-THROUGH branch. Pos(name,".") > 0, so the caller's own
            // criterion stands and the persistent subscription IS removed.
            {
                PersistentAlpha,
                PersistentAlpha,
                [TransientAlpha, PersistentBeta, TransientGamma]
            }
        };

    /// <summary>
    /// A requested name without a separator has the reserved namespace appended; one that already has a
    /// separator is honoured as given, so an explicitly named persistent subscription can be removed.
    /// </summary>
    /// <param name="requestedName">The name as the threading layer's caller supplies it.</param>
    /// <param name="expectedFilter">The filter the sparing helper must compose from it.</param>
    /// <param name="expectedSurvivors">The labels that must still dispatch, in dispatch order.</param>
    /// <remarks>
    /// <b>PINNED LEGACY SEMANTIC (AAP 0.7.3 C-B), locator <c>n_cst_threading.sru:L593-L598</c>.</b> The
    /// composed filter is asserted alongside the selection so that a failure says which of the two halves
    /// broke - the composition rule or the engine that reads it.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ByNameSparingRows))]
    public void AByNameSparingFilterAppendsTheReservedNamespaceOnlyWhenTheNameCarriesNoSeparator(
        string requestedName,
        string expectedFilter,
        string[] expectedSurvivors)
    {
        string built = EventBroker.BuildPersistentSparingFilter(requestedName);

        Assert.Equal(expectedFilter, built, StringComparer.Ordinal);

        (EventBroker broker, DispatchLog log, _) = Arrange(LifetimeSpreadTopics);

        Assert.Equal(RetCode.OK, broker.Unsubscribe(built));

        Assert.Equal(expectedSurvivors, ObserveLifetimeSpread(broker, log), StringComparer.Ordinal);
    }

    /// <summary>
    /// The bulk teardown removes EVERYTHING when nothing is in the reserved namespace: the namespace is
    /// not treated as implicitly populated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The case that matters most in practice, because it is the legacy's actual usage - nothing in the
    /// repository subscribes into <c>persistent</c>, so every real <c>of_off()</c> call clears the lot.
    /// A port that special-cased the reserved namespace into existence, or that treated an unqualified
    /// subscription as implicitly persistent, would leave subscriptions alive here and leak handlers
    /// across a teardown.
    /// </para>
    /// <para>
    /// Three transient subscriptions are used, spanning both namespace states - one with no namespace and
    /// one with an unrelated one - so neither state can be the survivor.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheBulkTeardownRemovesEverythingWhenNothingIsInTheReservedNamespace()
    {
        (EventBroker broker, DispatchLog log, _) = Arrange(
            TransientAlpha,
            TransientBetaOther,
            TransientGammaPlain);

        Assert.Equal(
            new[] { TransientAlpha, TransientBetaOther, TransientGammaPlain },
            ObserveLifetimeSpread(broker, log),
            StringComparer.Ordinal);

        Assert.Equal(
            RetCode.OK,
            broker.Unsubscribe(EventBroker.BuildPersistentSparingFilter(null)));

        Assert.Empty(ObserveLifetimeSpread(broker, log));
    }

    /// <summary>
    /// The BROKER's own no-argument unsubscribe removes everything INCLUDING the persistent
    /// subscriptions - the sparing rule belongs to the threading layer, not to the broker.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The distinction is easy to lose and expensive to lose. <c>n_cst_eventful.sru:L228-L230</c> is the
    /// broker's own <c>of_off()</c> and it passes an EMPTY filter with a null object - match everything,
    /// remove everything. The sparing spelling lives one layer up, in
    /// <c>n_cst_threading.sru:L544</c>'s <c>of_off()</c>, which is a DIFFERENT function on a DIFFERENT
    /// object that happens to share a name.
    /// </para>
    /// <para>
    /// So a port that moved the sparing rule down into the broker would silently make every direct
    /// <c>Unsubscribe()</c> spare a namespace it must not spare, and every threading-layer teardown would
    /// still look correct. Asserting both in one place is what keeps the layering honest, and it is why
    /// <see cref="EventBroker.BuildPersistentSparingFilter"/> is a separate composer the caller must
    /// apply rather than behaviour baked into the no-argument overload.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheBrokersOwnNoArgumentUnsubscribeDoesNotSpareTheReservedNamespace()
    {
        (EventBroker broker, DispatchLog log, _) = Arrange(LifetimeSpreadTopics);

        // :L228-L230 - an empty filter and a null object: everything matches, everything goes.
        Assert.Equal(RetCode.OK, broker.Unsubscribe());

        Assert.Empty(ObserveLifetimeSpread(broker, log));

        // Contrast, on an identical arrangement: the threading layer's spelling spares two of the four.
        (EventBroker sparing, DispatchLog sparingLog, _) = Arrange(LifetimeSpreadTopics);

        Assert.Equal(
            RetCode.OK,
            sparing.Unsubscribe(EventBroker.BuildPersistentSparingFilter(null)));

        Assert.Equal(
            new[] { PersistentAlpha, PersistentBeta },
            ObserveLifetimeSpread(sparing, sparingLog),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// The same reserved-namespace sparing applies under the disable kind, suspending the transient
    /// subscriptions while leaving the persistent ones dispatching.
    /// </summary>
    /// <remarks>
    /// The legacy has no <c>of_disable()</c> spelling of the teardown - the threading layer only ever
    /// unsubscribes - but the filter engine is shared at <c>:L993</c>, so the same spelling is a legal
    /// disable filter and must select the same set. Asserted because the reserved namespace is a property
    /// of the GRAMMAR rather than of the removal, and a port that special-cased it inside the removal
    /// path would pass every test above and fail this one.
    /// </remarks>
    [Fact]
    public void TheReservedNamespaceSparingAppliesUnderTheDisableKindToo()
    {
        (EventBroker broker, DispatchLog log, _) = Arrange(LifetimeSpreadTopics);

        Assert.Equal(RetCode.OK, broker.Disable(SparingFilter, true));

        Assert.Equal(
            new[] { PersistentAlpha, PersistentBeta },
            ObserveLifetimeSpread(broker, log),
            StringComparer.Ordinal);

        // RETAINED, not removed: re-enabling with the same filter brings all four back, in order.
        Assert.Equal(RetCode.OK, broker.Disable(SparingFilter, false));

        Assert.Equal(
            new[] { TransientAlpha, PersistentAlpha, PersistentBeta, TransientGamma },
            ObserveLifetimeSpread(broker, log),
            StringComparer.Ordinal);
    }

    // ==============================================================================================
    //  SECTION 3 - THE THIRTEEN DOCUMENTED FORMS
    //
    //  The oracle enumerates them twice, once per kind, in the same order and with the same prose:
    //      unsubscribe   w_test_eventful.srw:L306-L342
    //      disable       w_test_eventful.srw:L153-L189
    //  and prefaces both with the same comment block (:L297-L303 and :L145-L150) stating that a leading
    //  '^' EXCLUDES the named thing, which means the negation forms PRESERVE the subscriptions they name.
    //
    //  One member-data source drives both kinds, because :L993 is one routine and the kind is an
    //  argument to it. Every row spells its own filter and its own expected survivor list; nothing is
    //  computed from another row, so no row can be right for the wrong reason.
    // ==============================================================================================

    /// <summary>
    /// The oracle's thirteen filter forms, each with its locator and the exact set that must still
    /// dispatch from <see cref="SpreadTopics"/> afterwards, in dispatch order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rows are NOT collapsed even where two of them share an expected size, because the point of the
    /// table is that thirteen different spellings mean thirteen different things. The names in play are
    /// <c>clicked</c> and <c>changed</c>; the namespaces are <c>myns</c>, <c>otherns</c> and none.
    /// </para>
    /// <para>
    /// Read the expected lists against the arrangement to see the grammar at work: rows 1 and 2 are
    /// complements, as are 3 and 4, 11 and 12. Rows 5 through 8 are the four sign combinations of a
    /// two-criterion filter and are deliberately NOT complements of one another - a per-criterion
    /// negation flips one conjunct, not the conjunction. Rows 9 and 10 are the three-state pair. Row 13
    /// is the empty filter, which matches everything.
    /// </para>
    /// </remarks>
    private static (string Filter, string Oracle, string[] ExpectedSurvivors)[] OracleFilterForms =>
    [
        // 1. :L306 - every subscription of the named event, in ANY namespace. No separator, so the
        //    namespace criterion is ABSENT and never applied.
        (
            "clicked",
            "w_test_eventful.srw:L306 - the named event, every namespace",
            [OtherPlain, OtherInTargetNamespace]
        ),

        // 2. :L309 - everything EXCEPT the named event. The name is negated; the namespace is still
        //    absent, so it plays no part. PRESERVES the named subscriptions - the oracle's own gloss.
        (
            "^clicked",
            "w_test_eventful.srw:L309 - everything except the named event",
            [TargetPlain, TargetInTargetNamespace, TargetInThirdNamespace]
        ),

        // 3. :L312 - everything in the named namespace, under ANY name. The name half is empty, and an
        //    empty name matches every name (:L1017 with :L1030).
        (
            ".myns",
            "w_test_eventful.srw:L312 - every name, inside the named namespace",
            [OtherPlain, TargetPlain, TargetInThirdNamespace]
        ),

        // 4. :L315 - everything OUTSIDE the named namespace, under any name. The complement of row 3.
        (
            ".^myns",
            "w_test_eventful.srw:L315 - every name, outside the named namespace",
            [OtherInTargetNamespace, TargetInTargetNamespace]
        ),

        // 5. :L318 - the named event inside the named namespace. Both criteria positive, ANDed.
        (
            "clicked.myns",
            "w_test_eventful.srw:L318 - the named event inside the named namespace",
            [OtherPlain, OtherInTargetNamespace, TargetPlain, TargetInThirdNamespace]
        ),

        // 6. :L321 - everything in the named namespace EXCEPT the named event. Name negated, namespace
        //    positive. Removes only `changed.myns`.
        (
            "^clicked.myns",
            "w_test_eventful.srw:L321 - the named namespace, except the named event",
            [OtherPlain, TargetPlain, TargetInTargetNamespace, TargetInThirdNamespace]
        ),

        // 7. :L324 - the named event OUTSIDE the named namespace. Name positive, namespace negated - and
        //    note "outside" includes HAVING NO NAMESPACE, which is why `clicked` goes too.
        (
            "clicked.^myns",
            "w_test_eventful.srw:L324 - the named event, outside the named namespace",
            [OtherPlain, OtherInTargetNamespace, TargetInTargetNamespace]
        ),

        // 8. :L327 - everything outside the named namespace and other than the named event. BOTH
        //    negated, still ANDed, so only `changed` matches. Not the complement of row 5.
        (
            "^clicked.^myns",
            "w_test_eventful.srw:L327 - not the named event, and outside the named namespace",
            [OtherInTargetNamespace, TargetPlain, TargetInTargetNamespace, TargetInThirdNamespace]
        ),

        // 9. :L330 - the named event's subscriptions that have NO namespace. PRESENT-BUT-EMPTY.
        (
            "clicked.",
            "w_test_eventful.srw:L330 - the named event, with NO namespace",
            [OtherPlain, OtherInTargetNamespace, TargetInTargetNamespace, TargetInThirdNamespace]
        ),

        // 10. :L333 - the named event's subscriptions that HAVE a namespace. NEGATED-EMPTY.
        (
            "clicked.^",
            "w_test_eventful.srw:L333 - the named event, WITH a namespace",
            [OtherPlain, OtherInTargetNamespace, TargetPlain]
        ),

        // 11. :L336 - everything with no namespace, under any name. The unqualified present-but-empty
        //     form: empty name AND present-but-empty namespace.
        (
            ".",
            "w_test_eventful.srw:L336 - every name, with NO namespace",
            [OtherInTargetNamespace, TargetInTargetNamespace, TargetInThirdNamespace]
        ),

        // 12. :L339 - everything WITH a namespace, under any name. The complement of row 11.
        (
            ".^",
            "w_test_eventful.srw:L339 - every name, WITH a namespace",
            [OtherPlain, TargetPlain]
        ),

        // 13. :L342 - of_Off() with no argument at all: the empty filter matches everything.
        (
            "",
            "w_test_eventful.srw:L342 - the fully unqualified form removes everything",
            []
        )
    ];

    /// <summary>
    /// The thirteen forms above, projected into the shape xUnit's member data wants.
    /// </summary>
    /// <remarks>
    /// A projection rather than a second literal table, so the thirteen rows exist in exactly ONE place
    /// and the distinctness test below cannot drift from what the theories run. Nothing is collapsed: the
    /// projection is one member-data row per documented form, each carrying its own filter spelling and
    /// its own fully spelled-out expected survivor list.
    /// </remarks>
    public static TheoryData<string, string, string[]> OracleFilterFormRows
    {
        get
        {
            TheoryData<string, string, string[]> rows = new();

            foreach ((string filter, string oracle, string[] survivors) in OracleFilterForms)
            {
                rows.Add(filter, oracle, survivors);
            }

            return rows;
        }
    }

    /// <summary>
    /// Each of the thirteen documented forms removes exactly the set the oracle says it removes.
    /// </summary>
    /// <param name="filter">The filter spelling, exactly as the oracle passes it.</param>
    /// <param name="oracle">The locator and documented meaning for this row.</param>
    /// <param name="expectedSurvivors">The labels that must still dispatch, in dispatch order.</param>
    /// <remarks>
    /// <b>PINNED LEGACY SEMANTIC (AAP 0.7.3 C-B), locators per row.</b> Survival is observed by
    /// triggering both event names and reading the shared log, so each row pins the surviving SET and the
    /// surviving ORDER together. The filter's return code is asserted first: every one of the thirteen is
    /// a legal filter, and a row that started failing validation would otherwise present as an empty
    /// removal rather than as a rejected spelling.
    /// </remarks>
    [Theory]
    [MemberData(nameof(OracleFilterFormRows))]
    public void EachDocumentedFormRemovesExactlyWhatTheOracleSaysItRemoves(
        string filter,
        string oracle,
        string[] expectedSurvivors)
    {
        Assert.NotEmpty(oracle);

        (EventBroker broker, DispatchLog log, _) = Arrange(SpreadTopics);

        // The arrangement dispatches in full before the filter is applied, so an empty expectation below
        // is evidence of a removal rather than of a spread that never ran.
        Assert.Equal(SpreadDispatchOrder, ObserveSpread(broker, log), StringComparer.Ordinal);

        Assert.Equal(RetCode.OK, broker.Unsubscribe(filter));

        Assert.Equal(expectedSurvivors, ObserveSpread(broker, log), StringComparer.Ordinal);

        // Permanence: a removal is not undone by a second dispatch, and re-applying the same filter is a
        // no-op rather than an error.
        Assert.Equal(RetCode.OK, broker.Unsubscribe(filter));
        Assert.Equal(expectedSurvivors, ObserveSpread(broker, log), StringComparer.Ordinal);
    }

    /// <summary>
    /// No two of the thirteen forms select the same set from the spread: each spelling means something
    /// different.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The table above could be satisfied one row at a time by a filter engine that got several rows
    /// right by accident and produced the same set for the rest. This asserts the property the table is
    /// really claiming - that thirteen spellings are thirteen selections - and it is the assertion that
    /// fails first when two states are collapsed, naming the pair that merged.
    /// </para>
    /// <para>
    /// Exactly thirteen rows are also asserted, so a row cannot be quietly dropped to make this pass.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheThirteenFormsSelectThirteenDistinctSets()
    {
        List<string> spellings = [];
        List<string> selections = [];

        foreach ((string filter, _, string[] expectedSurvivors) in OracleFilterForms)
        {
            (EventBroker broker, DispatchLog log, _) = Arrange(SpreadTopics);

            Assert.Equal(RetCode.OK, broker.Unsubscribe(filter));

            string[] survivors = ObserveSpread(broker, log);
            Assert.Equal(expectedSurvivors, survivors, StringComparer.Ordinal);

            spellings.Add(filter);

            // The OBSERVED selection is what is tested for distinctness, not the expectation, so a table
            // whose rows happened to expect the same set would not launder itself past this.
            // U+0001 separates the labels because no label can contain it.
            selections.Add(string.Join('\u0001', survivors));
        }

        Assert.Equal(13, spellings.Count);
        Assert.Equal(13, spellings.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(13, selections.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// A per-criterion negation flips ONE conjunct; it does not complement the whole filter - which is
    /// why the four two-criterion forms are not complements of one another.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The single easiest thing to get wrong when reading <c>:L1029-L1047</c>, because the negation and
    /// the conjunction are written on adjacent lines. <c>"clicked.myns"</c> selects
    /// <c>name = clicked AND namespace = myns</c>; <c>"clicked.^myns"</c> selects
    /// <c>name = clicked AND namespace &lt;&gt; myns</c>. Their selections are disjoint but they do not
    /// cover the arrangement: everything under the other name matches NEITHER, because the name conjunct
    /// is positive in both.
    /// </para>
    /// <para>
    /// An implementation that complemented the whole filter when it saw a negation would pass rows 1
    /// through 4 and rows 9 through 12 of the table above - every single-criterion row - and fail only
    /// here. That is precisely the asymmetry this test exists for, and it is the same asymmetry that
    /// makes the excluding flag in Section 5 a genuinely different operator rather than a synonym.
    /// </para>
    /// </remarks>
    [Fact]
    public void ACriterionNegationFlipsOneConjunctAndNotTheWholeFilter()
    {
        (EventBroker positive, DispatchLog positiveLog, _) = Arrange(SpreadTopics);
        (EventBroker negated, DispatchLog negatedLog, _) = Arrange(SpreadTopics);

        Assert.Equal(RetCode.OK, positive.Unsubscribe("clicked.myns"));
        Assert.Equal(RetCode.OK, negated.Unsubscribe("clicked.^myns"));

        string[] afterPositive = ObserveSpread(positive, positiveLog);
        string[] afterNegated = ObserveSpread(negated, negatedLog);

        HashSet<string> removedByPositive = new(SpreadDispatchOrder, StringComparer.Ordinal);
        removedByPositive.ExceptWith(afterPositive);

        HashSet<string> removedByNegated = new(SpreadDispatchOrder, StringComparer.Ordinal);
        removedByNegated.ExceptWith(afterNegated);

        // Disjoint: no subscription is removed by both.
        Assert.Empty(removedByPositive.Intersect(removedByNegated, StringComparer.Ordinal));

        // But NOT covering: the two entries under the other name survive both, because the NAME conjunct
        // is positive in each. A whole-filter complement would have removed them in one of the two.
        Assert.Contains(OtherPlain, afterPositive, StringComparer.Ordinal);
        Assert.Contains(OtherPlain, afterNegated, StringComparer.Ordinal);
        Assert.Contains(OtherInTargetNamespace, afterPositive, StringComparer.Ordinal);
        Assert.Contains(OtherInTargetNamespace, afterNegated, StringComparer.Ordinal);

        // Three of five removed between them, not five of five.
        Assert.Equal(3, removedByPositive.Count + removedByNegated.Count);
    }

    // ==============================================================================================
    //  SECTION 4 - THE EMPTY-CRITERION RULES, AND THE ONE REJECTION
    //
    //  :L1017-L1019 compute three "criterion absent" flags from three different kinds of emptiness:
    //      bNoName     = (name = "")        an empty NAME matches every name
    //      bNoObject   = IsNull(object)     a NULL target matches every target
    //      bNoEvtName  = (evtName = "")     an empty HANDLER NAME matches every handler
    //  and :L1030, :L1045 and :L1048 then skip the corresponding comparison. Every "all subscriptions"
    //  overload in the legacy is expressed by leaving these empty, so the rules are load-bearing rather
    //  than defensive.
    //
    //  :L1021-L1023 is the ONLY validation in the whole grammar, and it is deliberately narrow: a
    //  negation with an empty NAME is rejected, while a negation with an empty NAMESPACE is perfectly
    //  legal and is in fact forms 10 and 12 of the matrix above.
    // ==============================================================================================

    /// <summary>
    /// An EMPTY name criterion matches every name, so a filter that supplies only a target removes that
    /// target's subscriptions under all of its names.
    /// </summary>
    /// <remarks>
    /// <c>:L1017</c> sets <c>bNoName</c> and <c>:L1030</c> seeds the match with it, so the name comparison
    /// never runs. Two targets are arranged, one subscribed under two names, so the assertion distinguishes
    /// "every name for this target" from "every name for every target".
    /// </remarks>
    [Fact]
    public void AnEmptyNameCriterionMatchesEveryName()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        RecordingSubscriber first = new(log, "first", broker);
        RecordingSubscriber second = new(log, "second", broker);

        Assert.Equal(RetCode.OK, broker.Subscribe(TargetName, first, RecordingHandlerNames.NoArguments));
        Assert.Equal(RetCode.OK, broker.Subscribe(OtherName, first, RecordingHandlerNames.NoArguments));
        Assert.Equal(RetCode.OK, broker.Subscribe(TargetName, second, RecordingHandlerNames.NoArguments));

        // An empty filter plus a target: every name, that target only.
        Assert.Equal(RetCode.OK, broker.Unsubscribe(string.Empty, first));

        Assert.Equal(
            new[] { "second" },
            Observe(broker, log, OtherName, TargetName),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// A NULL target criterion matches every target, so a by-name filter removes the named event's
    /// subscriptions whoever owns them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>:L1018</c> is <c>bNoObject = IsNull(object)</c>, and <c>:L230</c> and <c>:L703</c> are the two
    /// legacy overloads that rely on it - both pass a deliberately null object rather than omitting an
    /// argument, because PowerScript has no optional parameters.
    /// </para>
    /// <para>
    /// <b>C-H NOTE.</b> The null is passed through the real <c>object?</c> parameter, which is what the
    /// signature declares, so no suppression and no null-forgiving operator is needed anywhere here. The
    /// named argument is written out because <c>Unsubscribe(string, object?)</c> would otherwise read as
    /// an accident.
    /// </para>
    /// </remarks>
    [Fact]
    public void ANullTargetCriterionMatchesEveryTarget()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        RecordingSubscriber first = new(log, "first", broker);
        RecordingSubscriber second = new(log, "second", broker);

        Assert.Equal(RetCode.OK, broker.Subscribe(TargetName, first, RecordingHandlerNames.NoArguments));
        Assert.Equal(RetCode.OK, broker.Subscribe(TargetName, second, RecordingHandlerNames.NoArguments));
        Assert.Equal(RetCode.OK, broker.Subscribe(OtherName, first, RecordingHandlerNames.NoArguments));

        Assert.Equal(RetCode.OK, broker.Unsubscribe(TargetName, target: null));

        // Both targets lost their `clicked` subscription; the other name is untouched.
        Assert.Equal(
            new[] { "first" },
            Observe(broker, log, OtherName, TargetName),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// An EMPTY handler-name criterion matches every handler, while a non-empty one narrows to just that
    /// handler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>:L1019</c> is <c>bNoEvtName = (evtName = "")</c>, computed AFTER the fold at <c>:L998</c>, and
    /// <c>:L1048</c> applies it. Both halves are asserted here because only the pair shows the criterion
    /// is genuinely optional: one target subscribes two DIFFERENT handlers to the same event, so removing
    /// by handler name leaves exactly one behind and removing with an empty handler name takes both.
    /// </para>
    /// <para>
    /// The handler names are read from <c>RecordingHandlerNames</c> so they stay compile-checked against
    /// the double's real methods.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnEmptyHandlerNameCriterionMatchesEveryHandlerAndANonEmptyOneNarrows()
    {
        EventBroker narrowing = new();
        DispatchLog narrowingLog = new();
        RecordingSubscriber narrowingTarget = new(narrowingLog, "target", narrowing);

        Assert.Equal(
            RetCode.OK,
            narrowing.Subscribe(TargetName, narrowingTarget, RecordingHandlerNames.NoArguments));
        Assert.Equal(
            RetCode.OK,
            narrowing.Subscribe(TargetName, narrowingTarget, RecordingHandlerNames.OneArgument));

        // Both handlers run before the filter is applied.
        Observe(narrowing, narrowingLog, TargetName);
        Assert.Equal(
            new[] { RecordingHandlerNames.NoArguments, RecordingHandlerNames.OneArgument },
            narrowingLog.HandlerNames,
            StringComparer.Ordinal);

        // A named handler removes only that handler's subscription.
        Assert.Equal(
            RetCode.OK,
            narrowing.Unsubscribe(narrowingTarget, RecordingHandlerNames.NoArguments));

        Observe(narrowing, narrowingLog, TargetName);
        Assert.Equal(
            new[] { RecordingHandlerNames.OneArgument },
            narrowingLog.HandlerNames,
            StringComparer.Ordinal);

        // An empty handler name removes both, from an identical arrangement.
        EventBroker sweeping = new();
        DispatchLog sweepingLog = new();
        RecordingSubscriber sweepingTarget = new(sweepingLog, "target", sweeping);

        Assert.Equal(
            RetCode.OK,
            sweeping.Subscribe(TargetName, sweepingTarget, RecordingHandlerNames.NoArguments));
        Assert.Equal(
            RetCode.OK,
            sweeping.Subscribe(TargetName, sweepingTarget, RecordingHandlerNames.OneArgument));

        Assert.Equal(RetCode.OK, sweeping.Unsubscribe(sweepingTarget, string.Empty));

        Assert.Empty(Observe(sweeping, sweepingLog, TargetName));
    }

    /// <summary>
    /// The four spellings of "a negated name with nothing after the negation", each of which must be
    /// rejected with the legacy invalid-argument code.
    /// </summary>
    /// <remarks>
    /// <c>:L1008-L1011</c> strips the <c>^</c> from the name half, leaving it empty, and
    /// <c>:L1021-L1023</c> then rejects it. The four rows differ only in what follows the name, which is
    /// the point: the rejection is decided by the NAME half alone, whatever namespace criterion accompanies
    /// it - including a namespace criterion that is itself negated.
    /// </remarks>
    public static TheoryData<string> NegatedEmptyNameRejectionRows =>
        new()
        {
            "^",        // negated name, namespace criterion ABSENT
            "^.",       // negated name, present-but-empty namespace
            "^.^",      // negated name, negated-empty namespace
            "^.myns"    // negated name, ordinary namespace
        };

    /// <summary>
    /// A negated EMPTY name is rejected with the legacy invalid-argument code, throws nothing, and leaves
    /// the broker completely untouched - under both modification kinds.
    /// </summary>
    /// <param name="filter">The rejected filter spelling.</param>
    /// <remarks>
    /// <para>
    /// <c>:L1021-L1023</c> returns <c>RetCode.E_INVALID_ARGUMENT</c> as an ordinary value; the legacy has
    /// no exceptions on this path and neither does the port, which is why the code is asserted and
    /// <see cref="Assert.Throws{T}(Func{object})"/> appears nowhere in this file.
    /// </para>
    /// <para>
    /// The untouched-broker half is the substantive assertion. The rejection is returned BEFORE the bounds
    /// are cleared at <c>:L1025-L1026</c>, so a rejected filter cannot blank the lexical bounds - which is
    /// what an implementation that validated after resetting would silently do, leaving
    /// <see cref="EventBroker.IsSubscribed"/> answering false for subscriptions that are still very much
    /// there and still dispatching.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(NegatedEmptyNameRejectionRows))]
    public void ANegatedEmptyNameIsRejectedWithTheLegacyCodeAndChangesNothing(string filter)
    {
        (EventBroker broker, DispatchLog log, _) = Arrange(SpreadTopics);

        Assert.True(broker.IsSubscribed(TargetName));
        Assert.True(broker.IsSubscribed(OtherName));

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, broker.Unsubscribe(filter));
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, broker.Disable(filter, true));
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, broker.Disable(filter, false));

        // The bounds survived the rejection, so IsSubscribed still answers truthfully.
        Assert.True(broker.IsSubscribed(TargetName));
        Assert.True(broker.IsSubscribed(OtherName));

        // And so did every subscription, in its original dispatch order.
        Assert.Equal(SpreadDispatchOrder, ObserveSpread(broker, log), StringComparer.Ordinal);
    }

    /// <summary>
    /// The rejection is scoped to the NAME half: a negated EMPTY NAMESPACE is legal, and is how two of
    /// the thirteen documented forms are spelled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The asymmetry a reader is most likely to "tidy up". <c>:L1021-L1023</c> guards <c>bNotName</c>
    /// against <c>bNoName</c> and there is no corresponding guard for the namespace, so <c>".^"</c> and
    /// <c>"clicked.^"</c> parse and apply while <c>"^"</c> and <c>"^.myns"</c> do not. Harmonising the two
    /// halves - in either direction - would either reject two of the oracle's own documented forms or
    /// accept a filter the oracle rejects.
    /// </para>
    /// <para>
    /// <b>PINNED LEGACY SEMANTIC (AAP 0.7.3 C-B).</b> Asserted here as correct, with the codes for the
    /// legal and the rejected spellings side by side so the asymmetry is visible in one screen.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheRejectionAppliesToTheNameHalfOnlyAndNotToTheNamespaceHalf()
    {
        (EventBroker broker, DispatchLog log, _) = Arrange(SpreadTopics);

        // Legal: a negated EMPTY NAMESPACE. Forms 10 and 12 of the oracle's matrix.
        Assert.Equal(RetCode.OK, SubscriptionTopic.ParseFilter(".^", out SubscriptionTopic? unqualified));
        Assert.Equal(RetCode.OK, SubscriptionTopic.ParseFilter("clicked.^", out SubscriptionTopic? named));
        Assert.NotNull(unqualified);
        Assert.NotNull(named);

        // Rejected: a negated EMPTY NAME, whatever follows it.
        Assert.Equal(
            RetCode.E_INVALID_ARGUMENT,
            SubscriptionTopic.ParseFilter("^", out SubscriptionTopic? bareNegation));
        Assert.Null(bareNegation);

        Assert.Equal(
            RetCode.E_INVALID_ARGUMENT,
            SubscriptionTopic.ParseFilter("^.myns", out SubscriptionTopic? negationWithNamespace));
        Assert.Null(negationWithNamespace);

        // The legal one applies, and selects the entries that HAVE a namespace.
        Assert.Equal(RetCode.OK, broker.Unsubscribe(".^"));

        Assert.Equal(
            new[] { OtherPlain, TargetPlain },
            ObserveSpread(broker, log),
            StringComparer.Ordinal);
    }

    // ==============================================================================================
    //  SECTION 5 - THE EXCLUDING MODE, AND HOW IT COMPOSES WITH A CRITERION NEGATION
    //
    //  :L1051-L1053 is one statement - `if excluding then bMatched = Not bMatched` - applied AFTER every
    //  criterion has been ANDed. It is therefore a DIFFERENT operator from a per-criterion negation: this
    //  one inverts the CONJUNCTION, that one inverts a CONJUNCT.
    //
    //  A SURFACE FACT worth stating, because it bounds what can be asserted through one call. Only the
    //  by-TARGET overloads carry the flag - of_off(object, excluding) at :L573 and
    //  of_disable(object, disabled, excluding) at :L1249 - and BOTH pass an EMPTY name filter. So in the
    //  legacy, and therefore in the port, `excluding` is only ever paired with the target criterion, and
    //  no single public call can spell both an excluding sweep AND a negated filter criterion. The
    //  composition rule is nonetheless real, because both inversions live in the one shared engine, so it
    //  is asserted where it is observable: on SubscriptionTopic.Matches for the criterion negation, and
    //  through the exact-complement property for the outer one.
    // ==============================================================================================

    /// <summary>
    /// An excluding unsubscribe removes every subscription EXCEPT the named target's.
    /// </summary>
    /// <remarks>
    /// <c>:L573</c> passes an empty name filter, so every name matches, and the target is the only active
    /// criterion; <c>:L1051-L1053</c> then inverts it. The target chosen here owns a persistent
    /// subscription, which makes the point that the excluding sweep has no lifetime awareness whatsoever:
    /// it spares by TARGET IDENTITY, and the reserved namespace does not protect the other persistent
    /// subscription from it.
    /// </remarks>
    [Fact]
    public void AnExcludingUnsubscribeRemovesEverythingExceptTheNamedTarget()
    {
        (EventBroker broker, DispatchLog log, RecordingSubscriber[] subscribers) =
            Arrange(LifetimeSpreadTopics);

        // Index 1 is PersistentAlpha in LifetimeSpreadTopics' declaration order.
        RecordingSubscriber spared = subscribers[1];
        Assert.Equal(PersistentAlpha, spared.Label, StringComparer.Ordinal);

        Assert.Equal(RetCode.OK, broker.Unsubscribe(spared, excluding: true));

        Assert.Equal(
            new[] { PersistentAlpha },
            ObserveLifetimeSpread(broker, log),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// An excluding disable suspends every subscription EXCEPT the named target's, and retains them all.
    /// </summary>
    /// <remarks>
    /// <c>:L1249</c> is the disable twin of <c>:L573</c>, differing only in the modification kind and the
    /// value written. Re-enabling with the same excluding call restores everything, which is what shows the
    /// inversion applied to a RETAINING operation rather than a removing one.
    /// </remarks>
    [Fact]
    public void AnExcludingDisableSuspendsEverythingExceptTheNamedTargetAndRetainsThem()
    {
        (EventBroker broker, DispatchLog log, RecordingSubscriber[] subscribers) =
            Arrange(LifetimeSpreadTopics);

        RecordingSubscriber spared = subscribers[1];

        Assert.Equal(RetCode.OK, broker.Disable(spared, disabled: true, excluding: true));

        Assert.Equal(
            new[] { PersistentAlpha },
            ObserveLifetimeSpread(broker, log),
            StringComparer.Ordinal);

        // RETAINED: the same excluding call with the flag cleared brings all four back, in order.
        Assert.Equal(RetCode.OK, broker.Disable(spared, disabled: false, excluding: true));

        Assert.Equal(
            new[] { TransientAlpha, PersistentAlpha, PersistentBeta, TransientGamma },
            ObserveLifetimeSpread(broker, log),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// The excluding and non-excluding by-target forms are EXACT complements: between them they account
    /// for every subscription exactly once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the observable content of "the flag inverts the composed match". Asserted as a partition
    /// rather than as two separate expectations, because an implementation that inverted the wrong thing -
    /// say, only the target comparison rather than the composite - could still satisfy one of the two
    /// directions while breaking the partition.
    /// </para>
    /// <para>
    /// It also matters that the non-excluding direction here removes MORE than one entry: the spared target
    /// owns exactly one subscription, so the two directions split four subscriptions one-and-three rather
    /// than symmetrically, and a partition assertion catches an off-by-one that a count assertion would not.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheExcludingAndNonExcludingByTargetFormsAreExactComplements()
    {
        (EventBroker excludingBroker, DispatchLog excludingLog, RecordingSubscriber[] excludingSubscribers) =
            Arrange(LifetimeSpreadTopics);
        (EventBroker plainBroker, DispatchLog plainLog, RecordingSubscriber[] plainSubscribers) =
            Arrange(LifetimeSpreadTopics);

        Assert.Equal(RetCode.OK, excludingBroker.Unsubscribe(excludingSubscribers[1], excluding: true));
        Assert.Equal(RetCode.OK, plainBroker.Unsubscribe(plainSubscribers[1], excluding: false));

        string[] afterExcluding = ObserveLifetimeSpread(excludingBroker, excludingLog);
        string[] afterPlain = ObserveLifetimeSpread(plainBroker, plainLog);

        // Disjoint.
        Assert.Empty(afterExcluding.Intersect(afterPlain, StringComparer.Ordinal));

        // Covering: together they are the whole arrangement.
        Assert.Equal(
            new[] { TransientAlpha, PersistentAlpha, PersistentBeta, TransientGamma }.Order(StringComparer.Ordinal),
            afterExcluding.Concat(afterPlain).Order(StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// An excluding sweep with NO target criterion removes nothing, because everything matched and the
    /// inversion then unmatched all of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The degenerate case, and the one that proves the inversion is applied to the composite rather than
    /// folded into a criterion. A null target makes <c>bNoObject</c> true at <c>:L1018</c>, the empty name
    /// makes <c>bNoName</c> true at <c>:L1017</c>, so <c>bMatched</c> reaches <c>:L1051</c> as
    /// <see langword="true"/> for every entry - and the inversion turns every one of them into a miss.
    /// </para>
    /// <para>
    /// It also happens to be the safest possible call in the API and the most surprising: a caller who
    /// reads <c>Unsubscribe(null, true)</c> as "unsubscribe everything, definitely" gets the exact
    /// opposite. Pinned so that a well-meaning short-circuit for the null-target case cannot be added.
    /// </para>
    /// <para>
    /// <b>C-H NOTE.</b> The null flows through the declared <c>object?</c> parameter with a named argument
    /// and no suppression.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnExcludingSweepWithNoTargetCriterionRemovesNothing()
    {
        (EventBroker broker, DispatchLog log, _) = Arrange(LifetimeSpreadTopics);

        Assert.Equal(RetCode.OK, broker.Unsubscribe(target: null, excluding: true));

        Assert.Equal(
            new[] { TransientAlpha, PersistentAlpha, PersistentBeta, TransientGamma },
            ObserveLifetimeSpread(broker, log),
            StringComparer.Ordinal);

        // The disable twin behaves the same way: nothing is suspended.
        Assert.Equal(RetCode.OK, broker.Disable(target: null, disabled: true, excluding: true));

        Assert.Equal(
            new[] { TransientAlpha, PersistentAlpha, PersistentBeta, TransientGamma },
            ObserveLifetimeSpread(broker, log),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// The single-criterion filter pairs whose members are exact complements of one another, each spelled
    /// once with and once without its negation.
    /// </summary>
    /// <remarks>
    /// Only SINGLE-criterion pairs appear. A two-criterion filter's negated form is NOT its complement -
    /// <see cref="ACriterionNegationFlipsOneConjunctAndNotTheWholeFilter"/> is the assertion of exactly
    /// that - so including one here would be asserting something false.
    /// </remarks>
    public static TheoryData<string, string> ComplementaryFilterPairRows =>
        new()
        {
            // Name only, namespace criterion absent in both.  w_test_eventful.srw:L306 / :L309
            { "clicked", "^clicked" },

            // Namespace only, name empty in both.             :L312 / :L315
            { ".myns", ".^myns" },

            // The reserved namespace, both directions.        n_cst_threading.sru:L544
            { PersistentOnlyFilter, SparingFilter },

            // The unqualified empty-namespace pair.           :L336 / :L339
            { ".", ".^" }
        };

    /// <summary>
    /// A negated single criterion is the exact complement of its positive form, so applying the excluding
    /// inversion on top of it composes back to the positive match.
    /// </summary>
    /// <param name="positiveFilter">The filter without the negation.</param>
    /// <param name="negatedFilter">The same filter with its one criterion negated.</param>
    /// <remarks>
    /// <para>
    /// TWO INVERSIONS COMPOSE TO NONE, and this is the interaction easiest to implement halfway. The
    /// per-criterion negation at <c>:L1032-L1036</c> and <c>:L1039-L1043</c> and the outer excluding
    /// inversion at <c>:L1051-L1053</c> are independent, so a filter whose one criterion is negated,
    /// applied under an excluding sweep, selects exactly what the NON-negated filter selects on its own.
    /// An implementation that applied only one of the two inversions would produce precisely the
    /// complement of the right answer - a result that is wrong in the most plausible-looking way possible,
    /// since it is still a well-formed selection of the right size in symmetric arrangements.
    /// </para>
    /// <para>
    /// The composition is asserted on <see cref="SubscriptionTopic.Matches"/> per subscription, because
    /// that is where it is reachable: as this section's banner records, the two overloads that carry the
    /// excluding flag both hard-code an empty filter, so no single broker call can present both inversions
    /// at once. The broker-level half of the claim follows immediately after, as the exact-complement
    /// property of the two spellings.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ComplementaryFilterPairRows))]
    public void ANegatedSingleCriterionIsTheExactComplementSoTwoInversionsCompose(
        string positiveFilter,
        string negatedFilter)
    {
        SubscriptionTopic positive = ParseFilter(positiveFilter);
        SubscriptionTopic negated = ParseFilter(negatedFilter);

        // The two spellings differ ONLY in which half is negated, so exactly one negation flag differs.
        Assert.NotEqual(
            positive.NegateName || positive.NegateNamespace,
            negated.NegateName || negated.NegateNamespace);

        foreach (string topic in LifetimeSpreadTopics.Concat(SpreadTopics))
        {
            SubscriptionTopic subscription = ParseSubscriptionTopic(topic);
            string name = subscription.LegacyName;
            string subscriptionNamespace = subscription.Namespace;

            bool positiveMatch = positive.Matches(name, subscriptionNamespace);
            bool negatedMatch = negated.Matches(name, subscriptionNamespace);

            // Inversion one: the criterion negation already complements the positive match.
            Assert.NotEqual(positiveMatch, negatedMatch);

            // Inversion two: the excluding flag inverts whatever the composite produced. Applying it to
            // the negated match therefore recovers the positive match exactly - two inversions, none.
            Assert.Equal(positiveMatch, !negatedMatch);
        }
    }

    /// <summary>
    /// The same complement property, observed at the broker: the positive and negated spellings of one
    /// criterion partition the arrangement between them.
    /// </summary>
    /// <param name="positiveFilter">The filter without the negation.</param>
    /// <param name="negatedFilter">The same filter with its one criterion negated.</param>
    /// <remarks>
    /// The behavioural companion to the assertion above. What one spelling removes, the other spares, and
    /// nothing falls between them - which is what makes the reserved-lifetime idiom of Section 2 a
    /// consequence of the grammar rather than a special case in the engine.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ComplementaryFilterPairRows))]
    public void TheTwoDirectionsOfASingleCriterionPartitionTheArrangement(
        string positiveFilter,
        string negatedFilter)
    {
        (EventBroker positiveBroker, DispatchLog positiveLog, _) = Arrange(LifetimeSpreadTopics);
        (EventBroker negatedBroker, DispatchLog negatedLog, _) = Arrange(LifetimeSpreadTopics);

        Assert.Equal(RetCode.OK, positiveBroker.Unsubscribe(positiveFilter));
        Assert.Equal(RetCode.OK, negatedBroker.Unsubscribe(negatedFilter));

        string[] afterPositive = ObserveLifetimeSpread(positiveBroker, positiveLog);
        string[] afterNegated = ObserveLifetimeSpread(negatedBroker, negatedLog);

        // What one removes, the other spares.
        Assert.Empty(afterPositive.Intersect(afterNegated, StringComparer.Ordinal));

        Assert.Equal(
            LifetimeSpreadTopics.Order(StringComparer.Ordinal),
            afterPositive.Concat(afterNegated).Order(StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    // ==============================================================================================
    //  SECTION 6 - THE SAME GRAMMAR UNDER THE DISABLE KIND
    //
    //  :L1068-L1072 is the whole difference: the value is ASSIGNED into the matched entries and nothing is
    //  ever removed. Two consequences follow, and both are asserted here as CORRECT rather than smoothed:
    //
    //    RETENTION      A disabled entry keeps its slot, so re-enabling restores its exact dispatch
    //                   POSITION without re-subscribing. That is what makes disable usable as a temporary
    //                   suspension and is why the legacy's demo wires it to a checkbox
    //                   (w_test_eventful.srw:L153 passes the box's Checked straight through).
    //
    //    INVISIBILITY   :L1073-L1078 rebuilds the lexical bounds from entries that are neither invalid nor
    //                   disabled, and of_issubscribed at :L776-L777 skips the same two - so a
    //                   disabled-but-retained subscription reports as NOT subscribed while still existing.
    //
    //  This suite covers the disable kind only insofar as the filter grammar is shared, which per :L993 is
    //  entirely. The fourteen-overload enumeration - which arguments each of the seven-plus-seven
    //  overloads feeds into the engine - belongs to EventBrokerTests, in
    //  EachUnsubscribeOverloadRemovesWhatItsArgumentsDescribe and
    //  EachDisableOverloadSelectsWhatItsArgumentsDescribe.
    // ==============================================================================================

    /// <summary>
    /// Each of the thirteen documented forms suspends exactly what the oracle says it selects, and RETAINS
    /// it - re-enabling with the same filter restores the full arrangement in its original order.
    /// </summary>
    /// <param name="filter">The filter spelling, exactly as the oracle passes it.</param>
    /// <param name="oracle">The locator and documented meaning for this row.</param>
    /// <param name="expectedSurvivors">The labels that must still dispatch while disabled, in order.</param>
    /// <remarks>
    /// <para>
    /// The full matrix rather than a representative subset, because the oracle itself spells all thirteen
    /// out twice - <c>w_test_eventful.srw:L306-L342</c> for unsubscribe and <c>:L153-L189</c> for disable -
    /// and one member-data source drives both theories, which is the strongest available statement that
    /// <c>:L993</c> really is one engine taking a kind argument.
    /// </para>
    /// <para>
    /// <b>PINNED LEGACY SEMANTIC (AAP 0.7.3 C-B) - a disabled subscription is RETAINED, not removed.</b>
    /// The round trip in the second half of each row is the assertion: disable, observe the same survivor
    /// set the unsubscribe theory expects, then re-enable with the identical filter and get every
    /// subscription back, in the original dispatch order, with no <c>Subscribe</c> call in between.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(OracleFilterFormRows))]
    public void EachDocumentedFormSuspendsWhatItSelectsAndRetainsIt(
        string filter,
        string oracle,
        string[] expectedSurvivors)
    {
        Assert.NotEmpty(oracle);

        (EventBroker broker, DispatchLog log, _) = Arrange(SpreadTopics);

        Assert.Equal(SpreadDispatchOrder, ObserveSpread(broker, log), StringComparer.Ordinal);

        // The disable kind selects exactly what the removal kind selects: one engine, one grammar.
        Assert.Equal(RetCode.OK, broker.Disable(filter, true));

        Assert.Equal(expectedSurvivors, ObserveSpread(broker, log), StringComparer.Ordinal);

        // RETAINED. The same filter with the value cleared restores every suspended subscription in place.
        Assert.Equal(RetCode.OK, broker.Disable(filter, false));

        Assert.Equal(SpreadDispatchOrder, ObserveSpread(broker, log), StringComparer.Ordinal);
    }

    /// <summary>
    /// A disabled subscription keeps its dispatch POSITION: re-enabling a suspended middle subscriber puts
    /// it back between its neighbours, not at either end.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sharpest consequence of retention, and the one a remove-and-re-add implementation of disable
    /// would break while passing every set-based assertion above. Three subscriptions share one name and
    /// one priority, so their order is purely the insertion order the table holds
    /// (<c>n_cst_eventful.sru:L407-L422</c>); suspending the middle one and restoring it must not disturb
    /// that.
    /// </para>
    /// <para>
    /// The filter used names the target as well as the event, so exactly one of the three is selected -
    /// which is what makes the position observable at all.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADisabledSubscriptionKeepsItsDispatchPosition()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        RecordingSubscriber first = new(log, "first", broker);
        RecordingSubscriber middle = new(log, "middle", broker);
        RecordingSubscriber last = new(log, "last", broker);

        Assert.Equal(RetCode.OK, broker.Subscribe(TargetName, first, RecordingHandlerNames.NoArguments));
        Assert.Equal(RetCode.OK, broker.Subscribe(TargetName, middle, RecordingHandlerNames.NoArguments));
        Assert.Equal(RetCode.OK, broker.Subscribe(TargetName, last, RecordingHandlerNames.NoArguments));

        Assert.Equal(
            new[] { "first", "middle", "last" },
            Observe(broker, log, TargetName),
            StringComparer.Ordinal);

        Assert.Equal(RetCode.OK, broker.Disable(TargetName, middle, true));

        Assert.Equal(
            new[] { "first", "last" },
            Observe(broker, log, TargetName),
            StringComparer.Ordinal);

        Assert.Equal(RetCode.OK, broker.Disable(TargetName, middle, false));

        // Back in the MIDDLE, which a remove-and-re-append implementation could not achieve.
        Assert.Equal(
            new[] { "first", "middle", "last" },
            Observe(broker, log, TargetName),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// <see cref="EventBroker.IsSubscribed"/> reports FALSE for a disabled-but-retained subscription.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>PINNED LEGACY QUIRK, ASSERTED AS CORRECT (AAP 0.7.3 C-B).</b> It follows from two places that
    /// were written independently: <c>:L1073-L1078</c> rebuilds the lexical name bounds only from entries
    /// that are neither invalid nor disabled, and <c>of_issubscribed</c> skips those same two while scanning
    /// (<c>:L776-L777</c>). So once every subscription under a name is disabled, that name falls outside the
    /// bounds, the fast reject at <c>:L769-L770</c> fires, and the answer is false - even though the entries
    /// are all still present and will dispatch again the moment they are re-enabled.
    /// </para>
    /// <para>
    /// The BOUNDS are what decide it here, not the scan. The scan's own loop runs from the second entry to
    /// the count minus one (<c>:L775</c>), skipping the first and last slots entirely, so it could not be
    /// relied on to find them in any case - a separate preserved quirk that
    /// <c>EventBrokerTests</c> owns and this test deliberately does not depend on.
    /// </para>
    /// <para>
    /// This is NOT a defect to fix. It is the observable behaviour a consumer of the legacy sees, and
    /// "correcting" it would change the answer to a published query. The honest reading is that
    /// <c>IsSubscribed</c> asks "would a trigger reach anything under this name", not "does a record
    /// exist" - which is exactly why this suite observes survival through dispatch and uses this member
    /// only as a secondary signal.
    /// </para>
    /// <para>
    /// The other name is asserted at every step as the control: it is never touched, so a change that
    /// broke the bounds wholesale rather than selectively would fail here too.
    /// </para>
    /// </remarks>
    [Fact]
    public void IsSubscribedReportsFalseForADisabledButRetainedSubscription()
    {
        (EventBroker broker, DispatchLog log, _) = Arrange(SpreadTopics);

        Assert.True(broker.IsSubscribed(TargetName));
        Assert.True(broker.IsSubscribed(OtherName));

        // Disable ALL THREE subscriptions under the target name - an absent namespace criterion.
        Assert.Equal(RetCode.OK, broker.Disable(TargetName, true));

        // Reported as not subscribed, and indeed nothing dispatches under that name.
        Assert.False(broker.IsSubscribed(TargetName));
        Assert.True(broker.IsSubscribed(OtherName));

        Assert.Equal(
            new[] { OtherPlain, OtherInTargetNamespace },
            ObserveSpread(broker, log),
            StringComparer.Ordinal);

        // Yet they were RETAINED: re-enabling brings all three back with no re-subscribe, and the report
        // flips back with them.
        Assert.Equal(RetCode.OK, broker.Disable(TargetName, false));

        Assert.True(broker.IsSubscribed(TargetName));
        Assert.Equal(SpreadDispatchOrder, ObserveSpread(broker, log), StringComparer.Ordinal);
    }

    /// <summary>
    /// The disable value is a plain boolean WRITE and not a toggle: disabling twice leaves the entry
    /// disabled, and enabling an already-enabled entry changes nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>:L1070</c> is an assignment - <c>Events[nIndex].disabled = val</c> - so the caller states the
    /// desired state rather than requesting a flip. The legacy's own demo depends on it:
    /// <c>w_test_eventful.srw:L153</c> passes a checkbox's <c>Checked</c> value straight through, and a
    /// toggle would invert on every click regardless of the box.
    /// </para>
    /// <para>
    /// All four transitions are asserted - off to off, off to on, on to on, on to off - because a toggle
    /// implementation agrees with a write on exactly two of them.
    /// </para>
    /// </remarks>
    [Fact]
    public void DisablingIsABooleanWriteAndNotAToggle()
    {
        (EventBroker broker, DispatchLog log, _) = Arrange(SpreadTopics);

        // Enabling something already enabled is harmless.
        Assert.Equal(RetCode.OK, broker.Disable(TargetName, false));
        Assert.Equal(SpreadDispatchOrder, ObserveSpread(broker, log), StringComparer.Ordinal);

        string[] withTargetDisabled = [OtherPlain, OtherInTargetNamespace];

        // Disable once.
        Assert.Equal(RetCode.OK, broker.Disable(TargetName, true));
        Assert.Equal(withTargetDisabled, ObserveSpread(broker, log), StringComparer.Ordinal);

        // Disable again - a toggle would re-enable here. It must stay disabled.
        Assert.Equal(RetCode.OK, broker.Disable(TargetName, true));
        Assert.Equal(withTargetDisabled, ObserveSpread(broker, log), StringComparer.Ordinal);

        // And a third time, for the same reason.
        Assert.Equal(RetCode.OK, broker.Disable(TargetName, true));
        Assert.Equal(withTargetDisabled, ObserveSpread(broker, log), StringComparer.Ordinal);

        // Now enable, twice. The second is a no-op rather than a re-disable.
        Assert.Equal(RetCode.OK, broker.Disable(TargetName, false));
        Assert.Equal(SpreadDispatchOrder, ObserveSpread(broker, log), StringComparer.Ordinal);

        Assert.Equal(RetCode.OK, broker.Disable(TargetName, false));
        Assert.Equal(SpreadDispatchOrder, ObserveSpread(broker, log), StringComparer.Ordinal);
    }

    /// <summary>
    /// A removal is permanent and a suspension is not, even though the two select identically: after an
    /// unsubscribe there is nothing for a re-enable to bring back.
    /// </summary>
    /// <remarks>
    /// The contrast that makes the retention assertions meaningful. The same filter is applied to two
    /// identical arrangements under the two kinds, and then the enabling call is made on BOTH - so the one
    /// that lost its entries stays empty while the one that kept them recovers. Without this, "re-enabling
    /// restores" could be read as "the filter never really did anything".
    /// </remarks>
    [Fact]
    public void ARemovalIsPermanentWhereASuspensionIsReversible()
    {
        (EventBroker removed, DispatchLog removedLog, _) = Arrange(SpreadTopics);
        (EventBroker suspended, DispatchLog suspendedLog, _) = Arrange(SpreadTopics);

        Assert.Equal(RetCode.OK, removed.Unsubscribe(SparingFilter));
        Assert.Equal(RetCode.OK, suspended.Disable(SparingFilter, true));

        // Identical selection: nothing in the five-way spread is persistent, so both lose everything.
        Assert.Empty(ObserveSpread(removed, removedLog));
        Assert.Empty(ObserveSpread(suspended, suspendedLog));

        // The enabling call is made on both. Only the suspended one has anything to restore.
        Assert.Equal(RetCode.OK, removed.Disable(SparingFilter, false));
        Assert.Equal(RetCode.OK, suspended.Disable(SparingFilter, false));

        Assert.Empty(ObserveSpread(removed, removedLog));
        Assert.Equal(SpreadDispatchOrder, ObserveSpread(suspended, suspendedLog), StringComparer.Ordinal);

        // And IsSubscribed cannot tell them apart WHILE suspended, which is the quirk above; it can once
        // the suspension is lifted.
        Assert.False(removed.IsSubscribed(TargetName));
        Assert.True(suspended.IsSubscribed(TargetName));
    }

    // ==============================================================================================
    //  SECTION 7 - FILTERING WHILE A DISPATCH IS IN FLIGHT
    //
    //  :L1054-L1059 splits the removal path on the dispatch depth. Above zero the matched entries are only
    //  TOMBSTONED - `Events[nIndex].invalid = true` - and the compaction is POSTED (:L1083) rather than
    //  performed, because rewriting the table would invalidate the cursor every active dispatch level
    //  holds into it. The dispatch loop then skips tombstoned entries at :L829, so the effect is immediate
    //  even though the storage is not reclaimed until the queued sweep runs.
    //
    //  The port carries the posted sweep as an explicit queued continuation (AAP 0.4.5.4 - a headless
    //  container has no Win32 message pump), so DrainPostedContinuations is where the compaction happens
    //  and PendingPostedContinuationCount is how a test sees that one is owed.
    // ==============================================================================================

    /// <summary>
    /// Unsubscribing a LATER subscriber from inside a dispatch stops it running in that same dispatch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>:L1055-L1059</c> marks the entry invalid immediately, and the dispatch loop's own skip at
    /// <c>:L829</c> honours the tombstone on the very next iteration - so the removal takes effect within
    /// the dispatch that requested it, not after it. The removal is performed from the FIRST subscriber's
    /// handler and targets the THIRD, so the second still runs and the effect cannot be confused with the
    /// dispatch simply stopping.
    /// </para>
    /// <para>
    /// The compaction is owed but not yet done at this point, which is asserted through
    /// <see cref="EventBroker.PendingPostedContinuationCount"/> - one queued sweep, exactly as
    /// <c>:L1083</c> posts one.
    /// </para>
    /// </remarks>
    [Fact]
    public void UnsubscribingALaterSubscriberFromInsideADispatchStopsItRunning()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        RecordingSubscriber first = new(log, "first", broker);
        RecordingSubscriber second = new(log, "second", broker);
        RecordingSubscriber third = new(log, "third", broker);

        Assert.Equal(RetCode.OK, broker.Subscribe(TargetName, first, RecordingHandlerNames.NoArguments));
        Assert.Equal(RetCode.OK, broker.Subscribe(TargetName, second, RecordingHandlerNames.NoArguments));
        Assert.Equal(RetCode.OK, broker.Subscribe(TargetName, third, RecordingHandlerNames.NoArguments));

        Assert.Equal(0, broker.PendingPostedContinuationCount);

        // From inside the FIRST handler, remove the THIRD.
        first.SetInterceptor(
            RecordingHandlerNames.NoArguments,
            (dispatching, _) =>
            {
                Assert.NotNull(dispatching);
                Assert.Equal(RetCode.OK, dispatching.Unsubscribe(TargetName, third));
            });

        Assert.Equal(
            new[] { "first", "second" },
            Observe(broker, log, TargetName),
            StringComparer.Ordinal);

        // One compaction is owed - posted, not performed, because the dispatch held a cursor into the table.
        Assert.Equal(1, broker.PendingPostedContinuationCount);
    }

    /// <summary>
    /// A deferred removal is permanent: once the queued sweep drains, the entry is gone for good and no
    /// later trigger reaches it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The second half of <c>:L1054-L1059</c> and <c>:L1081-L1087</c>. The tombstone alone already keeps
    /// the entry out of every dispatch, so this test is careful to show BOTH: the entry is unreachable
    /// immediately after the dispatch AND the owed compaction really runs and really clears the debt
    /// rather than accumulating one queued sweep per removal forever.
    /// </para>
    /// <para>
    /// The interceptor is cleared before the second dispatch, or it would queue a further sweep and the
    /// pending count would be meaningless.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADeferredRemovalIsPermanentOnceTheQueuedSweepDrains()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        RecordingSubscriber first = new(log, "first", broker);
        RecordingSubscriber second = new(log, "second", broker);

        Assert.Equal(RetCode.OK, broker.Subscribe(TargetName, first, RecordingHandlerNames.NoArguments));
        Assert.Equal(RetCode.OK, broker.Subscribe(TargetName, second, RecordingHandlerNames.NoArguments));

        first.SetInterceptor(
            RecordingHandlerNames.NoArguments,
            (dispatching, _) =>
            {
                Assert.NotNull(dispatching);
                Assert.Equal(RetCode.OK, dispatching.Unsubscribe(TargetName, second));
            });

        Assert.Equal(new[] { "first" }, Observe(broker, log, TargetName), StringComparer.Ordinal);

        // Stop removing, so the counts below are about the sweep and not about further removals.
        first.SetInterceptor(RecordingHandlerNames.NoArguments, null);

        Assert.Equal(1, broker.PendingPostedContinuationCount);

        // Already unreachable on the tombstone alone, BEFORE the sweep runs (:L829).
        Assert.Equal(new[] { "first" }, Observe(broker, log, TargetName), StringComparer.Ordinal);

        // The owed sweep runs exactly once and clears the debt.
        Assert.Equal(1, broker.DrainPostedContinuations());
        Assert.Equal(0, broker.PendingPostedContinuationCount);

        // Still gone, and gone for good.
        Assert.Equal(new[] { "first" }, Observe(broker, log, TargetName), StringComparer.Ordinal);
        Assert.Equal(0, second.InvocationCountFor(RecordingHandlerNames.NoArguments));
    }

    /// <summary>
    /// Unsubscribing from inside a dispatch does not disturb the remaining subscribers' order - which is
    /// the guarantee the deferred-sweep design exists to provide.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reason <c>:L1055-L1059</c> tombstones instead of rebuilding. Five subscribers share one name, so
    /// their order is the insertion order; the third is removed from inside the FIRST one's handler, while
    /// the loop is already positioned inside the run. A rebuild at that moment would shift every later
    /// entry down one slot and the dispatch would skip the fourth, so an implementation that compacted
    /// eagerly would produce first, second, fifth - a plausible-looking result that no set-based assertion
    /// would catch.
    /// </para>
    /// <para>
    /// The order is asserted before the removal, during the dispatch that performs it, and again after the
    /// sweep drains, so all three views must agree.
    /// </para>
    /// </remarks>
    [Fact]
    public void UnsubscribingFromInsideADispatchDoesNotDisturbTheRemainingOrder()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        RecordingSubscriber first = new(log, "first", broker);
        RecordingSubscriber second = new(log, "second", broker);
        RecordingSubscriber third = new(log, "third", broker);
        RecordingSubscriber fourth = new(log, "fourth", broker);
        RecordingSubscriber fifth = new(log, "fifth", broker);

        foreach (RecordingSubscriber subscriber in new[] { first, second, third, fourth, fifth })
        {
            Assert.Equal(
                RetCode.OK,
                broker.Subscribe(TargetName, subscriber, RecordingHandlerNames.NoArguments));
        }

        Assert.Equal(
            new[] { "first", "second", "third", "fourth", "fifth" },
            Observe(broker, log, TargetName),
            StringComparer.Ordinal);

        // Remove a MIDDLE subscriber from the first handler, while the loop is inside the run.
        first.SetInterceptor(
            RecordingHandlerNames.NoArguments,
            (dispatching, _) =>
            {
                Assert.NotNull(dispatching);
                Assert.Equal(RetCode.OK, dispatching.Unsubscribe(TargetName, third));
            });

        // The fourth is NOT skipped: the tombstone left every other slot exactly where it was.
        Assert.Equal(
            new[] { "first", "second", "fourth", "fifth" },
            Observe(broker, log, TargetName),
            StringComparer.Ordinal);

        first.SetInterceptor(RecordingHandlerNames.NoArguments, null);
        broker.DrainPostedContinuations();

        // And the compaction preserved the order it reclaimed around.
        Assert.Equal(
            new[] { "first", "second", "fourth", "fifth" },
            Observe(broker, log, TargetName),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// A namespace-filtered unsubscribe issued from inside a dispatch obeys the same grammar as one issued
    /// outside it, tombstoning exactly the matched entries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two halves of this file joined together: the deferred-removal path at <c>:L1055-L1059</c> shares
    /// the match at <c>:L1030-L1053</c> with the immediate path, so a mid-dispatch sweep spelled
    /// <c>".^persistent"</c> must spare the persistent subscriptions exactly as it does at depth zero. This
    /// is the shape the threading layer actually uses - a teardown triggered from inside a handler is
    /// entirely ordinary - so it is worth pinning rather than inferring.
    /// </para>
    /// <para>
    /// The first subscriber is persistent and issues the sweep, so it also demonstrates that the entry
    /// performing the removal is judged by the same criteria as every other: it survives because its
    /// namespace spares it, not because it is the one that called.
    /// </para>
    /// </remarks>
    [Fact]
    public void AMidDispatchSweepObeysTheSameNamespaceGrammarAsOneIssuedOutsideADispatch()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        RecordingSubscriber persistent = new(log, "persistent", broker);
        RecordingSubscriber transient = new(log, "transient", broker);
        RecordingSubscriber nearMiss = new(log, "near-miss", broker);

        // All three under ONE event name, so a single trigger observes the whole sweep. The third sits in
        // `persistent2`, a namespace that merely STARTS with the reserved one.
        Assert.Equal(
            RetCode.OK,
            broker.Subscribe(PersistentAlpha, persistent, RecordingHandlerNames.NoArguments));
        Assert.Equal(
            RetCode.OK,
            broker.Subscribe(TransientAlpha, transient, RecordingHandlerNames.NoArguments));
        Assert.Equal(
            RetCode.OK,
            broker.Subscribe("alpha.persistent2", nearMiss, RecordingHandlerNames.NoArguments));

        Assert.Equal(
            new[] { "persistent", "transient", "near-miss" },
            Observe(broker, log, "alpha"),
            StringComparer.Ordinal);

        // The persistent subscriber issues the framework's bulk teardown from inside the dispatch.
        persistent.SetInterceptor(
            RecordingHandlerNames.NoArguments,
            (dispatching, _) =>
            {
                Assert.NotNull(dispatching);
                Assert.Equal(
                    RetCode.OK,
                    dispatching.Unsubscribe(EventBroker.BuildPersistentSparingFilter(null)));
            });

        // The transient one is tombstoned mid-dispatch and does not run, and so is the near miss:
        // `persistent2` is NOT the reserved namespace, because :L1039-L1043 compares for EXACT equality
        // and never for a prefix. The subscriber that issued the sweep survives on its namespace alone.
        Assert.Equal(
            new[] { "persistent" },
            Observe(broker, log, "alpha"),
            StringComparer.Ordinal);

        persistent.SetInterceptor(RecordingHandlerNames.NoArguments, null);
        Assert.Equal(1, broker.DrainPostedContinuations());

        // Still dispatching after the sweep is compacted, and the other two are gone for good.
        Assert.Equal(
            new[] { "persistent" },
            Observe(broker, log, "alpha"),
            StringComparer.Ordinal);

        Assert.Equal(1, transient.InvocationCountFor(RecordingHandlerNames.NoArguments));
        Assert.Equal(1, nearMiss.InvocationCountFor(RecordingHandlerNames.NoArguments));
    }
}
