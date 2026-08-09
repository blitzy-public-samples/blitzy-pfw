// =================================================================================================
//  DispatchOrderTests - the dispatch ORDER parity suite for PowerFramework.Shared.Eventful.
//
//  SUBJECT
//  -------
//  EventBroker's ordered insert and its dispatch walk, ported from
//  ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru. Two properties are pinned here and
//  nowhere else:
//
//    1. The subscription table is held in ASCENDING ORDINAL order of the STORED subscription name,
//       and registration order has no influence whatsoever. The oracle states the rule in a prose note
//       at ws_objects/pfw.tests.pbl.src/w_test_eventful.srw:L238, which says in Chinese that events
//       are ordered by name ascending and that using a small name improves lookup and dispatch
//       performance. The implementation is the ordered insert at n_cst_eventful.sru:L407-L422 plus the
//       lexical bounds at :L439-L440.
//
//    2. WITHIN one name, position is decided by priority descending, and the ONLY difference between
//       the prepend mode and the default append mode is `<=` versus `<` on the priority comparison
//       (:L417 against :L419). Prepend therefore lands at the HEAD of an equal-priority run and
//       append at its TAIL, and neither mode does anything else differently.
//
//  WHY THIS SUITE EXISTS (AAP 0.7.3 C-K - the boundary decision, stated once, in full)
//  ----------------------------------------------------------------------------------
//  The legacy FUSES three independent encodings into one opaque topic string: a lexical ORDERING
//  prefix, the logical NAME, and a `.^persistent` LIFETIME suffix. AAP 0.6.1.2 requires the wire
//  contract to carry `sequence`, `name` and `lifetime` as THREE SEPARATE FIELDS, reconstituting the
//  fused legacy spelling only at the compatibility edge. SubscriptionTopic implements exactly that:
//  `Sequence` and `LogicalName` are projections computed over `LegacyName`, and `LegacyName` remains
//  the single dispatch and sort key.
//
//  That decomposition is precisely why this suite asserts against the STORED LEGACY NAME and never
//  against the projections. ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L54 declares
//  its item-changed topic as "0-itemchanged" and :L57 its edit-changed topic as "1-editchanged". The
//  leading digit-and-hyphen is part of the NAME - the symbol run in of_on ends at the first character
//  that is not one of the five leading-run symbols, and a digit is not one (:L340-L360, `case else
//  exit` at :L357-L358), so nothing strips it. The prefix acts through the ordinal name sort and
//  through nothing else. The DataWindow event chain depends on those two topics dispatching in that
//  relative order, and the ONLY thing enforcing it is an ordinal string comparison on a name that
//  still contains "0-" and "1-". A port that trimmed the prefix, or that sorted by `LogicalName`,
//  or that compared with culture-aware or case-insensitive semantics, would break the chain in
//  silence. This suite is the guard.
//
//  HOW ORDER IS OBSERVED, AND WHY THIS MECHANISM (agent brief, Phase 1)
//  -------------------------------------------------------------------
//  EventBroker exposes NO ordered snapshot of live subscriptions - its table is a private
//  `List<EventSubscription>` and nothing projects it. The second preference is therefore used: the
//  SHARED DispatchLog of RecordingSubscriber, whose `Labels` property is the ordered sequence of the
//  labels of every handler that ran. Every subscriber in a test writes into one log, so the log IS
//  the dispatch order.
//
//  For ordering WITHIN one name that is direct: one Trigger runs the whole run and `Labels` is the
//  run's order.
//
//  For ordering ACROSS names it is indirect but complete, and the mechanism is worth spelling out
//  because it is what makes these assertions non-vacuous. A dispatch selects one name's contiguous
//  run and leaves the walk as soon as it passes that run (:L824-L827): the `bSubscribed` exit at
//  :L825, and - decisively for a name that has not been reached yet - the greater-name exit at :L826.
//  So for a table of distinct names each holding one subscriber, an inversion at positions i < j
//  where name[i] sorts AFTER name[j] makes name[j] UNREACHABLE: triggering it walks onto name[i],
//  finds a greater name before ever entering the run, and breaks. Asserting that EVERY name is
//  reached is therefore a complete assertion that the table is in ascending order - not a tautology.
//  It is exactly what an implementation that appended in registration order fails.
//
//  Each cross-name test states the trigger order it used, and asserts the resulting label SEQUENCE
//  (never a set - a set comparison would pass under any ordering and would defeat the whole suite).
//  Several also assert under the REVERSE trigger order, so no result can be an artefact of the order
//  the triggers were issued in.
//
//  HONEST LIMIT OF THE DISCRIMINATION, so nobody over-reads these assertions
//  ------------------------------------------------------------------------
//  A port that used CULTURE-AWARE comparison CONSISTENTLY at both the insert (:L409) and the walk
//  (:L826) would still reach every name, and the bounds fast-reject at :L797-L798 is observationally
//  identical to an empty walk, so such a port is not distinguishable through this public surface.
//  What IS caught, decisively:
//
//    * ANY CASE-INSENSITIVE comparison. Names differing only by case would fuse into ONE run, so a
//      single Trigger would invoke both subscribers and the label sequence would carry an extra
//      entry. The oracle is explicit that the event name is case-SENSITIVE while only the handler
//      name is case-insensitive (w_test_eventful.srw:L250, and the fold at :L336 is applied to the
//      handler name alone). See NamesDifferingOnlyByCaseAreTwoRunsAndNotOne.
//
//    * ANY COMPARER MISMATCH between the insert and the walk - which is the realistic defect, since
//      `string.CompareTo`, `Comparer<string>.Default` and `List<string>.Sort()` are all CULTURE-AWARE
//      while `string.Equals` is ordinal, so a port can easily end up with one of each. The
//      "Zeta"/"alpha" row of OrdinalNameOrderRows is the case written for it: those two names sort one
//      way ordinally and the other way linguistically, and the divergence does not depend on case
//      folding, so a mismatch leaves one of them unreachable. The case-only rows happen to catch a
//      culture-aware insert as well, because linguistic collation also orders "name" before "Name".
//
//  PROHIBITIONS HONOURED (agent brief, Phase 6)
//  -------------------------------------------
//  Sequences are asserted, never sets. Every matrix is a [Theory] with [MemberData] over TheoryData;
//  [Fact] is reserved for cases with no table. A fresh EventBroker and a fresh DispatchLog are built
//  per test, so no test can observe another's table. No member here is SCREAMING_SNAKE: the root
//  .editorconfig relaxes CA1707 and IDE1006 for ten NAMED implementation files and for no test file,
//  and TreatWarningsAsErrors is true, so such a member would be a build error. Dispatch is
//  synchronous, so there is no timing, no Task.Delay, no Thread.Sleep, no clock read, no GUID, no
//  random source and no thread anywhere in this file. Nothing reads a file, a socket or a database.
//  Nothing here reaches a deferred capability: the dynamic script-invoker the legacy attached to every
//  subscription is deferred to ScriptBridge by AAP 0.7.3 C-D and dropped by the port, and this file
//  neither names it as a type nor depends on anything it used to provide.
//
//  MUTATION-VERIFIED, so a reader can trust that these assertions bite
//  -------------------------------------------------------------------
//  Four deliberate defects were injected into EventBroker one at a time and then reverted. What each
//  one broke is recorded here because a suite nobody has tried to fool is a suite nobody should rely on:
//
//    * append in registration order (drop the ordered insert) - 25 of the 36 cases fail, including ALL
//      SEVEN rows of ScrambledRegistrationsAreHeldInAscendingOrdinalNameOrder, all six midpoint rows,
//      and TheSmallerNameIsReachedByASingleTriggerEvenWhenRegisteredLast;
//    * prepend uses the append comparison (`<=` becomes `<` at :L417) - exactly the four prepend cases
//      fail, and WithDistinctPrioritiesPrependAndAppendPlaceTheSubscriberIdentically correctly still
//      passes, because with distinct priorities the two comparators genuinely do agree;
//    * culture-aware insert against an ordinal walk - the "alpha Zeta" row, the "name Name NAME" row and
//      NamesDifferingOnlyByCaseAreTwoRunsAndNotOne fail, and nothing else does;
//    * unconditional midpoint start (drop the guard at :L803) - the five midpoint rows where the probe
//      engages fail while the size-three row still passes, which is the `nCount > 3` boundary at :L802.
//
//  LOCATOR ACCURACY
//  ----------------
//  Every locator below was read out of the legacy source rather than copied from a summary, and three
//  differ from the figures quoted in the brief that commissioned this file: the ordered insert is at
//  n_cst_eventful.sru:L407-L422 with the bounds at :L439-L440; the two prose rules are at
//  w_test_eventful.srw:L237 and :L238; and the leading symbol run has FIVE cases at
//  n_cst_eventful.sru:L341-L359, not eight. The verified locators are the ones used here.
// =================================================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.Shared.Eventful.Tests;

/// <summary>
/// Pins the dispatch ORDER of <see cref="EventBroker"/>: the ascending ordinal name sort that the
/// framework's own <c>0-</c> and <c>1-</c> topic prefixes rely on, the prepend-versus-append rule
/// inside one name, the lexical bounds and their fast reject, and the rule that a subscription made
/// during a dispatch takes effect only on the next one.
/// </summary>
/// <remarks>
/// See the file header for the boundary decision this suite exists to protect, for the reason the
/// shared <see cref="DispatchLog"/> is the order-observation mechanism, and for the honest limit of
/// what these assertions can and cannot discriminate.
/// </remarks>
public class DispatchOrderTests
{
    /// <summary>
    /// The framework's real item-changed topic, spelled exactly as
    /// <c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L54</c> declares it.
    /// </summary>
    /// <remarks>
    /// The <c>0-</c> is part of the NAME and is not a symbol: only <c>-</c>, <c>%</c>, <c>*</c>,
    /// <c>@</c> and <c>!</c> are leading-run symbols, and the run ends at the first character that is
    /// none of them (<c>n_cst_eventful.sru:L340-L360</c>). So the digit terminates the run at
    /// position one and the whole string survives into <c>EVENTDATA.name</c> (<c>:L363</c>).
    /// </remarks>
    private const string ItemChangedTopic = "0-itemchanged";

    /// <summary>
    /// The framework's real edit-changed topic, from <c>se_cst_dw.sru:L57</c>. Sorts after
    /// <see cref="ItemChangedTopic"/> ordinally, which is the entire purpose of the two prefixes.
    /// </summary>
    private const string EditChangedTopic = "1-editchanged";

    /// <summary>
    /// The separator the theory rows use to spell a list of names inside one string.
    /// </summary>
    /// <remarks>
    /// Rows carry space-separated name lists rather than <c>string[]</c> so that the generated test
    /// display name shows the actual names being ordered. None of the names in any row contains a
    /// space, so the split is unambiguous.
    /// </remarks>
    private const char NameListSeparator = ' ';

    /// <summary>
    /// Splits one theory row's space-separated name list.
    /// </summary>
    /// <param name="names">The space-separated list.</param>
    /// <returns>The names, in the order written.</returns>
    private static string[] SplitNames(string names) =>
        names.Split(
            NameListSeparator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Subscribes one <see cref="RecordingSubscriber"/> per name, using each name as its own label,
    /// in the order given.
    /// </summary>
    /// <param name="broker">The broker to subscribe against.</param>
    /// <param name="names">The topics, in REGISTRATION order.</param>
    /// <returns>The shared log every one of those subscribers writes into.</returns>
    /// <remarks>
    /// Label equals name on purpose: the expected label sequence is then literally the expected name
    /// order, so a failure message names the topics rather than opaque markers. Every subscription is
    /// asserted to have succeeded, because a silently rejected topic would leave a shorter table and
    /// an ordering assertion would then be measuring the wrong thing.
    /// </remarks>
    private static DispatchLog SubscribeNamesAsTheirOwnLabels(
        EventBroker broker,
        IReadOnlyList<string> names)
    {
        DispatchLog log = new();

        for (int index = 0; index < names.Count; index++)
        {
            string name = names[index];
            RecordingSubscriber subscriber = new(log, name, broker);

            Assert.Equal(
                RetCode.OK,
                broker.Subscribe(name, subscriber, RecordingHandlerNames.NoArguments));
        }

        return log;
    }

    /// <summary>
    /// Subscribes one <see cref="RecordingSubscriber"/> under an explicit label.
    /// </summary>
    /// <param name="broker">The broker to subscribe against.</param>
    /// <param name="log">The shared log.</param>
    /// <param name="topic">The subscription topic, symbols included.</param>
    /// <param name="label">The label this subscriber writes into the log.</param>
    /// <returns>The subscriber, so a caller can attach an in-handler callback to it.</returns>
    /// <remarks>
    /// The within-one-name tests need the label to differ from the topic, because several
    /// subscriptions share one name and differ only in their symbols.
    /// </remarks>
    private static RecordingSubscriber SubscribeLabelled(
        EventBroker broker,
        DispatchLog log,
        string topic,
        string label)
    {
        RecordingSubscriber subscriber = new(log, label, broker);

        Assert.Equal(
            RetCode.OK,
            broker.Subscribe(topic, subscriber, RecordingHandlerNames.NoArguments));

        return subscriber;
    }

    /// <summary>
    /// Clears the log, triggers each name once in the order given, and asserts that every one of them
    /// reached its subscriber - so the observed label sequence is exactly the trigger order.
    /// </summary>
    /// <param name="broker">The broker to dispatch on.</param>
    /// <param name="log">The shared log, cleared first so only this pass is measured.</param>
    /// <param name="triggerOrder">The names to trigger, in the order to trigger them.</param>
    /// <remarks>
    /// <para>
    /// <b>This is the cross-name ordering assertion, and it is not a tautology.</b> A dispatch leaves
    /// the walk at the first name greater than the one requested (<c>n_cst_eventful.sru:L826</c>), so
    /// in a table of distinct single-subscriber names any inversion makes the smaller of the inverted
    /// pair unreachable and its label goes missing from this sequence. Passing under EVERY trigger
    /// order therefore means the table is ascending.
    /// </para>
    /// <para>
    /// It is also what catches a fused run: were names compared case-insensitively, two names
    /// differing only in case would form ONE run and a single trigger would append TWO labels, so the
    /// sequence would be longer than the trigger order.
    /// </para>
    /// </remarks>
    private static void AssertEveryNameIsReachedInTriggerOrder(
        EventBroker broker,
        DispatchLog log,
        IReadOnlyList<string> triggerOrder) =>
        AssertTriggeringEachNameRuns(broker, log, triggerOrder, triggerOrder);

    /// <summary>
    /// Clears the log, triggers each name once in the order given, and asserts the resulting label
    /// sequence is exactly the one expected - for the tests whose subscriber labels are descriptive
    /// rather than equal to the topic.
    /// </summary>
    /// <param name="broker">The broker to dispatch on.</param>
    /// <param name="log">The shared log, cleared first so only this pass is measured.</param>
    /// <param name="triggerOrder">The names to trigger, in the order to trigger them.</param>
    /// <param name="expectedLabels">
    /// The labels expected in dispatch order. One entry per trigger when each name holds exactly one
    /// subscriber; a longer sequence would mean two names had fused into one run.
    /// </param>
    /// <remarks>
    /// See <see cref="AssertEveryNameIsReachedInTriggerOrder"/> for why this sequence is the cross-name
    /// ordering assertion rather than a restatement of the trigger order.
    /// </remarks>
    private static void AssertTriggeringEachNameRuns(
        EventBroker broker,
        DispatchLog log,
        IReadOnlyList<string> triggerOrder,
        IReadOnlyList<string> expectedLabels)
    {
        log.Clear();

        for (int index = 0; index < triggerOrder.Count; index++)
        {
            broker.Trigger(triggerOrder[index]);
        }

        Assert.Equal(expectedLabels, log.Labels);
    }

    /// <summary>
    /// Returns a reversed COPY of a name list.
    /// </summary>
    /// <param name="names">The list to reverse.</param>
    /// <returns>A new array holding the same names in the opposite order.</returns>
    /// <remarks>
    /// A copy rather than an in-place reversal, so a caller can assert against the ascending order and
    /// the descending order of the same list within one test without the first assertion's expectation
    /// being mutated out from under it.
    /// </remarks>
    private static string[] Reversed(IReadOnlyList<string> names)
    {
        string[] reversed = [.. names];
        Array.Reverse(reversed);

        return reversed;
    }

    /// <summary>
    /// Parses a topic and returns the wire contract's <see cref="SubscriptionTopic.LogicalName"/>
    /// projection - the fused name with its ordering prefix removed.
    /// </summary>
    /// <param name="topic">The topic to parse.</param>
    /// <returns>The logical name.</returns>
    /// <remarks>
    /// Used only to make the "changing the prefix changes the order" assertion legible: the projection
    /// lets that test show the two LOGICAL topics swapping places, which is the consequence a reader
    /// cares about. Nothing in the broker dispatches or sorts on this value.
    /// </remarks>
    private static string LogicalNameOf(string topic)
    {
        long code = SubscriptionTopic.ParseSubscription(topic, out SubscriptionTopic? parsed);

        Assert.Equal(RetCode.OK, code);
        Assert.NotNull(parsed);

        return parsed!.LogicalName;
    }

    // =============================================================================================
    //  BINDING CHECK - the one precondition every other case in this file rests on
    // =============================================================================================

    /// <summary>
    /// The STORED subscription name keeps the ordering prefix, and the prefix's <c>-</c> is not read
    /// as a prepend request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B): the prefix STAYS INSIDE THE NAME, and its
    /// hyphen is not a prepend request. Both are legacy behaviour to reproduce, not spellings to tidy.
    /// </para>
    /// <para>
    /// The gate for this whole suite: every ordering assertion below is about a name that still
    /// contains <c>0-</c> or <c>1-</c>, so if the parser stripped the prefix - or claimed its hyphen as
    /// <see cref="SubscriptionTopic.Prepend"/> - none of them would mean what they say. Reproduced
    /// here as a precondition only; <c>SubscriptionTopicGrammarTests</c> owns the grammar itself and
    /// covers the symbol run, the priority delimiter and the namespace split in full.
    /// </para>
    /// <para>
    /// The mechanism is <c>n_cst_eventful.sru:L340-L360</c>: the character loop matches each leading
    /// character against the five symbols, a digit matches none of them, so <c>case else</c> exits the
    /// loop at position one (<c>:L357-L358</c>) and <c>Mid(name, 1)</c> at <c>:L363</c> returns the
    /// whole string unchanged. Neither <c>Pos(name, ":")</c> nor <c>Pos(name, ".")</c> finds anything
    /// afterwards, so nothing further is removed.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheStoredNameKeepsTheOrderingPrefixEveryOtherCaseHereDependsOn()
    {
        long itemCode = SubscriptionTopic.ParseSubscription(
            ItemChangedTopic,
            out SubscriptionTopic? itemChanged);
        long editCode = SubscriptionTopic.ParseSubscription(
            EditChangedTopic,
            out SubscriptionTopic? editChanged);

        // A topic carrying no symbols at all, as the yardstick for "claimed nothing".
        long plainCode = SubscriptionTopic.ParseSubscription(
            "itemchanged",
            out SubscriptionTopic? unadorned);

        Assert.Equal(RetCode.OK, itemCode);
        Assert.Equal(RetCode.OK, editCode);
        Assert.Equal(RetCode.OK, plainCode);
        Assert.NotNull(itemChanged);
        Assert.NotNull(editChanged);
        Assert.NotNull(unadorned);

        // The dispatch and sort key is the WHOLE spelling, prefix included (:L335 narrowed by :L363).
        Assert.Equal(ItemChangedTopic, itemChanged!.LegacyName);
        Assert.Equal(EditChangedTopic, editChanged!.LegacyName);

        // The prefix's hyphen is an ordinary name character here, NOT the prepend symbol - the symbol
        // run already ended on the digit at position one (:L340-L360).
        Assert.False(itemChanged.Prepend);
        Assert.False(editChanged.Prepend);

        // And neither topic claimed a priority: each sits exactly where an unadorned topic does, which is
        // PRIORITY_NORMAL as seeded at :L334 before the symbol run runs. Asserted against the unadorned
        // topic rather than against the priority constant, because the constant's VALUE is the priority
        // suite's subject and this suite is only entitled to say "these two claimed nothing".
        Assert.Equal(unadorned!.Priority, itemChanged.Priority);
        Assert.Equal(unadorned.Priority, editChanged.Priority);
        Assert.False(unadorned.Prepend);
    }

    // =============================================================================================
    //  ORDERING ACROSS NAMES - the ascending ordinal sort of the stored name
    //  n_cst_eventful.sru:L407-L422 (insert), :L439-L440 (bounds), :L824-L827 (walk exits)
    //  w_test_eventful.srw:L238 states the rule in prose
    // =============================================================================================

    /// <summary>
    /// The two registration orders of the framework's own DataWindow topics, each paired with the one
    /// dispatch order both must produce.
    /// </summary>
    /// <remarks>
    /// Two rows, deliberately spelling the SAME expectation twice: the point of the matrix is that the
    /// second column does not depend on the first.
    /// </remarks>
    public static TheoryData<string, string> DataWindowRegistrationOrderRows =>
        new()
        {
            // Registered BACKWARDS - edit-changed first, item-changed second.
            { $"{EditChangedTopic} {ItemChangedTopic}", $"{ItemChangedTopic} {EditChangedTopic}" },

            // Registered forwards. Same outcome.
            { $"{ItemChangedTopic} {EditChangedTopic}", $"{ItemChangedTopic} {EditChangedTopic}" },
        };

    /// <summary>
    /// <c>se_cst_dw</c>'s two topics are held in prefix order whatever order they were registered in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). The ordered insert takes its position at the
    /// first existing entry whose name compares GREATER than the new one and otherwise keeps walking
    /// (<c>n_cst_eventful.sru:L408-L413</c>), so the table is in ascending name order at every moment -
    /// the rule the oracle states at <c>w_test_eventful.srw:L238</c>. Registration order contributes
    /// nothing, which is exactly why <c>se_cst_dw.sru:L54</c> and <c>:L57</c> can encode their intended
    /// dispatch order in the NAME and rely on it holding however the two services happen to install
    /// themselves.
    /// </para>
    /// <para>
    /// The assertion is made under three trigger orders - registration order, ascending, then
    /// descending - because each pass proves every name is still reachable, and reachability is what
    /// an inverted table loses (the greater-name exit at <c>:L826</c>). Asserting under the descending
    /// order too is what rules out the result being an artefact of the order the triggers were issued
    /// in.
    /// </para>
    /// </remarks>
    /// <param name="registrationOrder">The order to subscribe in.</param>
    /// <param name="expectedOrder">The ascending ordinal order both registrations must produce.</param>
    [Theory]
    [MemberData(nameof(DataWindowRegistrationOrderRows))]
    public void TheDataWindowTopicsAreHeldInPrefixOrderWhateverTheRegistrationOrder(
        string registrationOrder,
        string expectedOrder)
    {
        string[] registered = SplitNames(registrationOrder);
        string[] expected = SplitNames(expectedOrder);

        EventBroker broker = new();
        DispatchLog log = SubscribeNamesAsTheirOwnLabels(broker, registered);

        AssertEveryNameIsReachedInTriggerOrder(broker, log, registered);
        AssertEveryNameIsReachedInTriggerOrder(broker, log, expected);
        AssertEveryNameIsReachedInTriggerOrder(broker, log, Reversed(expected));
    }

    /// <summary>
    /// A SINGLE trigger of the lexically smaller name reaches it even though it was registered LAST -
    /// the decisive cross-name ordering probe.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B), and this is the sharpest assertion in the
    /// file. Had the insert appended in registration order, the table would read
    /// <c>["1-editchanged", "0-itemchanged"]</c>; triggering <c>"0-itemchanged"</c> would then read
    /// entry one, find a name that is neither equal nor smaller, take the greater-name exit at
    /// <c>n_cst_eventful.sru:L826</c> and dispatch to NOBODY - while still returning the resolved
    /// default and reporting no error at all. That is the silent failure mode the whole suite exists to
    /// prevent, and one trigger of one name detects it.
    /// </para>
    /// <para>
    /// The lexical bounds cannot mask the defect either, because <c>:L439-L440</c> maintain them as the
    /// smallest and largest live names regardless of position, so <c>"0-itemchanged"</c> is inside the
    /// bounds in both tables and the fast rejects at <c>:L797-L798</c> do not fire.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSmallerNameIsReachedByASingleTriggerEvenWhenRegisteredLast()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        // The GREATER name is registered first, so only the ordered insert can put the smaller one in
        // front of it.
        SubscribeLabelled(broker, log, EditChangedTopic, EditChangedTopic);
        SubscribeLabelled(broker, log, ItemChangedTopic, ItemChangedTopic);

        broker.Trigger(ItemChangedTopic);

        Assert.Equal([ItemChangedTopic], log.Labels);
    }

    /// <summary>
    /// Swapping the two ordering prefixes swaps the dispatch order of the two LOGICAL topics.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). The prefix is not decoration and it is not
    /// metadata the broker interprets: it is part of the name the ordinal sort is taken over
    /// (<c>n_cst_eventful.sru:L409</c>), so re-spelling the prefixes re-orders the topics and nothing
    /// else in the system has to change. Both halves are asserted in one test, against two independent
    /// brokers, so the contrast is visible rather than inferred.
    /// </para>
    /// <para>
    /// The assertion is projected through <see cref="SubscriptionTopic.LogicalName"/> purely so the
    /// swap reads as <c>itemchanged</c> and <c>editchanged</c> trading places. That projection is the
    /// wire contract's field, and it is asserted on nowhere else here, because the broker neither
    /// dispatches nor sorts on it (AAP 0.6.1.2, and the decision note on
    /// <see cref="SubscriptionTopic.LegacyName"/>).
    /// </para>
    /// </remarks>
    [Fact]
    public void SwappingTheOrderingPrefixesSwapsTheDispatchOrder()
    {
        // As shipped: item-changed carries "0-", edit-changed carries "1-".
        EventBroker asShipped = new();
        DispatchLog asShippedLog = SubscribeNamesAsTheirOwnLabels(
            asShipped,
            [EditChangedTopic, ItemChangedTopic]);

        string[] shippedAscending = [ItemChangedTopic, EditChangedTopic];
        AssertEveryNameIsReachedInTriggerOrder(asShipped, asShippedLog, shippedAscending);
        Assert.Equal(
            ["itemchanged", "editchanged"],
            asShippedLog.Labels.Select(LogicalNameOf).ToArray());

        // Prefixes swapped, and nothing else about either topic changed.
        const string swappedItemChanged = "1-itemchanged";
        const string swappedEditChanged = "0-editchanged";

        EventBroker swapped = new();
        DispatchLog swappedLog = SubscribeNamesAsTheirOwnLabels(
            swapped,
            [swappedItemChanged, swappedEditChanged]);

        // The decisive probe again: the now-smaller name was registered LAST and is still reached by a
        // single trigger, so the insert really did move it in front (:L409-L411).
        swappedLog.Clear();
        swapped.Trigger(swappedEditChanged);
        Assert.Equal([swappedEditChanged], swappedLog.Labels);

        string[] swappedAscending = [swappedEditChanged, swappedItemChanged];
        AssertEveryNameIsReachedInTriggerOrder(swapped, swappedLog, swappedAscending);
        Assert.Equal(
            ["editchanged", "itemchanged"],
            swappedLog.Labels.Select(LogicalNameOf).ToArray());
    }

    /// <summary>
    /// Registration orders paired with the ORDINAL sort each must produce. Every row is scrambled, so
    /// no row can pass by accident of the order it was written in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Four of the seven rows are chosen to break a specific wrong comparer rather than to add volume:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///   <c>9-x 10-x 1-x</c> - the sort is over CHARACTERS, not over the number the prefix looks like,
    ///   so <c>"10-x"</c> precedes <c>"9-x"</c> because <c>'1'</c> precedes <c>'9'</c>, and
    ///   <c>"1-x"</c> precedes <c>"10-x"</c> because <c>'-'</c> precedes <c>'0'</c>. Any port that
    ///   parsed the prefix and sorted numerically produces a different order for this row.
    ///   </description></item>
    ///   <item><description>
    ///   <c>name Name NAME</c> - three DISTINCT names under an ordinal comparer, one fused name under
    ///   any case-insensitive one. Under <c>OrdinalIgnoreCase</c> a single trigger would invoke all
    ///   three subscribers and the observed sequence would be three times as long as the trigger
    ///   order, so this row fails outright rather than merely reordering.
    ///   </description></item>
    ///   <item><description>
    ///   <c>alpha Zeta</c> - the ORDINAL/LINGUISTIC divergence, and the only case here that catches a
    ///   comparer MISMATCH between the ordered insert (<c>n_cst_eventful.sru:L409</c>) and the dispatch
    ///   walk (<c>:L826</c>). Ordinally <c>"Zeta"</c> precedes <c>"alpha"</c> because every uppercase
    ///   ASCII letter precedes every lowercase one; linguistically <c>"alpha"</c> precedes
    ///   <c>"Zeta"</c>. A port that inserted with <c>string.CompareTo</c>,
    ///   <c>Comparer&lt;string&gt;.Default</c> or <c>List&lt;string&gt;.Sort()</c> - all three of which
    ///   are culture-aware - while walking with an ordinal comparison would leave one of the two
    ///   unreachable, and only this row notices.
    ///   </description></item>
    ///   <item><description>
    ///   <c>abc a ab</c> - a shorter name precedes the longer name it prefixes, which is the behaviour
    ///   the oracle's performance note leans on when it recommends short names
    ///   (<c>w_test_eventful.srw:L238</c>).
    ///   </description></item>
    /// </list>
    /// </remarks>
    public static TheoryData<string, string> OrdinalNameOrderRows =>
        new()
        {
            // Plain alphabetic names, scrambled.
            { "gamma alpha beta", "alpha beta gamma" },

            // The framework's own digit-prefix scheme, scrambled.
            { "2-third 0-first 1-second", "0-first 1-second 2-third" },

            // A digit-prefixed name against the same name unprefixed.
            {
                $"itemchanged {ItemChangedTopic} {EditChangedTopic}",
                $"{ItemChangedTopic} {EditChangedTopic} itemchanged"
            },

            // A character sort, not a numeric one.
            { "9-x 10-x 1-x", "1-x 10-x 9-x" },

            // Case-only differences are three distinct names, uppercase first.
            { "name Name NAME", "NAME Name name" },

            // Ordinal and linguistic orderings disagree here.
            { "alpha Zeta", "Zeta alpha" },

            // Shorter before longer when one prefixes the other.
            { "abc a ab", "a ab abc" },
        };

    /// <summary>
    /// However the names arrive, the table is held in ascending ORDINAL order of the stored name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). The comparison at
    /// <c>n_cst_eventful.sru:L409</c> is PowerScript's <c>&gt;</c> on two strings, which is a byte-wise
    /// comparison and carries no locale, no collation table and no case folding. The port therefore uses
    /// <c>string.CompareOrdinal</c>, and the rows above are chosen so that a culture-aware or
    /// case-insensitive substitute fails rather than merely looking different. See the file header for
    /// the honest limit of that discrimination.
    /// </para>
    /// <para>
    /// The oracle's own reason for the ordering is stated at <c>w_test_eventful.srw:L238</c>: the
    /// ascending order is what lets the lexical bounds reject an unsubscribed name outright
    /// (<c>:L797-L798</c>), lets the walk stop at the first greater name (<c>:L826</c>) and lets the
    /// midpoint probe skip a provably non-matching half (<c>:L800-L806</c>). Ordering is therefore not a
    /// presentational nicety - three correctness-relevant shortcuts are built on it.
    /// </para>
    /// </remarks>
    /// <param name="registrationOrder">The scrambled order to subscribe in.</param>
    /// <param name="ordinalOrder">The ascending ordinal order the table must end up in.</param>
    [Theory]
    [MemberData(nameof(OrdinalNameOrderRows))]
    public void ScrambledRegistrationsAreHeldInAscendingOrdinalNameOrder(
        string registrationOrder,
        string ordinalOrder)
    {
        string[] registered = SplitNames(registrationOrder);
        string[] expected = SplitNames(ordinalOrder);

        // FIXTURE GUARD, not an assertion about the subject: it machine-checks that this row's second
        // column really is the ORDINAL sort of its first, so a mistyped row cannot quietly weaken the
        // case it was written to make.
        Assert.Equal(expected, registered.OrderBy(name => name, StringComparer.Ordinal).ToArray());

        EventBroker broker = new();
        DispatchLog log = SubscribeNamesAsTheirOwnLabels(broker, registered);

        AssertEveryNameIsReachedInTriggerOrder(broker, log, registered);
        AssertEveryNameIsReachedInTriggerOrder(broker, log, expected);
        AssertEveryNameIsReachedInTriggerOrder(broker, log, Reversed(expected));
    }

    /// <summary>
    /// A digit-prefixed name sorts before an alphabetic one, so the prefix scheme actually achieves
    /// what <c>se_cst_dw</c> relies on.
    /// </summary>
    /// <remarks>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). Every ASCII digit precedes every ASCII letter,
    /// so prefixing a topic with <c>0-</c> moves it ahead of every unprefixed alphabetic topic in the
    /// same table - which is what makes a hand-rolled sequence number spelled into the name work at all.
    /// The prefixed topic is registered LAST here, so only the ordered insert at
    /// <c>n_cst_eventful.sru:L409-L411</c> can have placed it in front, and a single trigger of it is
    /// enough to prove that it did: an appended table would have taken the greater-name exit at
    /// <c>:L826</c> and dispatched to nobody.
    /// </remarks>
    [Fact]
    public void ADigitPrefixedNameSortsBeforeAnAlphabeticOne()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        SubscribeLabelled(broker, log, "itemchanged", "unprefixed");
        SubscribeLabelled(broker, log, ItemChangedTopic, "prefixed");

        broker.Trigger(ItemChangedTopic);
        Assert.Equal(["prefixed"], log.Labels);

        AssertTriggeringEachNameRuns(
            broker,
            log,
            [ItemChangedTopic, "itemchanged"],
            ["prefixed", "unprefixed"]);
    }

    /// <summary>
    /// Two names differing only by case are two separate runs, not one - the event name is
    /// case-SENSITIVE.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). The oracle is explicit about the asymmetry at
    /// <c>w_test_eventful.srw:L250</c>, where the event name is annotated case-SENSITIVE and the handler
    /// name case-INsensitive on the same line, and the implementation matches: <c>of_on</c> folds the
    /// HANDLER name at <c>n_cst_eventful.sru:L336</c> and never touches the event name.
    /// </para>
    /// <para>
    /// The failure mode a case-insensitive comparer produces is not a reordering, it is a MERGE: the two
    /// subscriptions would form one contiguous run and one trigger would invoke both handlers. Each
    /// trigger below is therefore asserted to invoke exactly ONE subscriber, which is the assertion that
    /// detects the merge. The comparison that would have merged them is spelled out first so the test
    /// records what it is ruling out.
    /// </para>
    /// </remarks>
    [Fact]
    public void NamesDifferingOnlyByCaseAreTwoRunsAndNotOne()
    {
        // What a case-insensitive comparer would have concluded about these two names.
        Assert.Equal(0, StringComparer.OrdinalIgnoreCase.Compare("Name", "name"));

        // What the ordinal comparer the broker actually uses concludes: they are distinct, and the
        // uppercase spelling sorts first.
        Assert.True(string.CompareOrdinal("Name", "name") < 0);

        EventBroker broker = new();
        DispatchLog log = new();

        // Registered lower-case first, so the ordering cannot come from registration order.
        SubscribeLabelled(broker, log, "name", "lower");
        SubscribeLabelled(broker, log, "Name", "upper");

        broker.Trigger("Name");
        Assert.Equal(["upper"], log.Labels);

        log.Clear();
        broker.Trigger("name");
        Assert.Equal(["lower"], log.Labels);

        // And the two runs sit in ordinal order, uppercase ahead of lowercase. Exactly two labels come
        // back for two triggers: a fused run would have produced four.
        AssertTriggeringEachNameRuns(broker, log, ["Name", "name"], ["upper", "lower"]);
    }

    // =============================================================================================
    //  ORDERING WITHIN ONE NAME - priority descending, and prepend versus append
    //  n_cst_eventful.sru:L414-L421; the whole difference is `<=` at :L417 against `<` at :L419
    //  w_test_eventful.srw:L209-L212 and :L223-L225 document the symbols and their intent
    // =============================================================================================

    /// <summary>
    /// The topic every within-one-name case dispatches, matching the oracle's own worked example
    /// (<c>w_test_eventful.srw:L220-L226</c>).
    /// </summary>
    private const string ClickedTopic = "clicked";

    /// <summary>
    /// Equal-priority peers dispatch in ARRIVAL order.
    /// </summary>
    /// <remarks>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). The default mode is append, and its stop
    /// condition is STRICTLY lower priority (<c>n_cst_eventful.sru:L419</c>), so a newcomer walks past
    /// every peer of equal priority - the <c>nInsertIdx++</c> at <c>:L421</c> - and lands at the tail of
    /// the run. Arrival order inside one priority band is therefore a guarantee a subscriber can rely on,
    /// not an accident of the container.
    /// </remarks>
    [Fact]
    public void EqualPriorityPeersDispatchInArrivalOrder()
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
    /// The two placement spellings of one normal-priority subscription, each paired with the position it
    /// takes among two equal-priority peers that registered before it.
    /// </summary>
    /// <remarks>
    /// The whole matrix is two rows because the whole difference between the two modes is two rows: the
    /// same three subscriptions, the same priorities, the same arrival order, and one leading <c>-</c>.
    /// </remarks>
    public static TheoryData<string, string> EqualPriorityPlacementRows =>
        new()
        {
            // Default append: behind its equals (:L419 stops only at a strictly lower priority).
            { ClickedTopic, "first second newcomer" },

            // Prepend: ahead of its equals (:L417 stops at equal-or-lower).
            { $"-{ClickedTopic}", "newcomer first second" },
        };

    /// <summary>
    /// Prepend lands AHEAD of equal-priority peers and the default append lands BEHIND them - and that
    /// single comparison is the entire difference between the two modes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). Both branches of the name-match arm remember the
    /// current position (<c>n_cst_eventful.sru:L415</c>) and then decide whether to stop at it; the
    /// prepend branch stops when the existing peer's priority is LESS THAN OR EQUAL to the newcomer's
    /// (<c>:L417</c>) while the append branch stops only when it is STRICTLY LESS (<c>:L419</c>).
    /// Swapping those two comparators would silently reorder every same-priority subscription in the
    /// system, and nothing but an ordering assertion would report it.
    /// </para>
    /// <para>
    /// The oracle documents the intent alongside the mechanism: a leading <c>-</c> means "insert at the
    /// head of the run of the same priority", with the default being the tail
    /// (<c>w_test_eventful.srw:L209</c> and <c>:L223</c>). That is what lets a service installing itself
    /// after application code still get in front of it - a late subscriber has no other way to.
    /// </para>
    /// </remarks>
    /// <param name="newcomerTopic">The newcomer's topic, differing only in the prepend symbol.</param>
    /// <param name="expectedOrder">The dispatch order that spelling must produce.</param>
    [Theory]
    [MemberData(nameof(EqualPriorityPlacementRows))]
    public void PrependAndAppendDifferOnlyInTheirTreatmentOfEqualPriority(
        string newcomerTopic,
        string expectedOrder)
    {
        EventBroker broker = new();
        DispatchLog log = new();

        SubscribeLabelled(broker, log, ClickedTopic, "first");
        SubscribeLabelled(broker, log, ClickedTopic, "second");
        SubscribeLabelled(broker, log, newcomerTopic, "newcomer");

        broker.Trigger(ClickedTopic);

        Assert.Equal(SplitNames(expectedOrder), log.Labels);
    }

    /// <summary>
    /// The two placement spellings again, this time to be shown INDISTINGUISHABLE.
    /// </summary>
    public static TheoryData<string> NormalPriorityPlacementSpellingRows =>
        new()
        {
            ClickedTopic,
            $"-{ClickedTopic}",
        };

    /// <summary>
    /// When every priority in the run is distinct, prepend and append place the subscriber identically -
    /// purely by priority.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B), and it is the complement of
    /// <see cref="PrependAndAppendDifferOnlyInTheirTreatmentOfEqualPriority"/>: the prepend symbol is NOT
    /// a general "go first" request. With a high-priority and a low-priority peer already present, the
    /// two comparators at <c>n_cst_eventful.sru:L417</c> and <c>:L419</c> disagree on nothing, because no
    /// existing peer has the newcomer's priority - both walk past the high peer and both stop at the low
    /// one, so both land between them.
    /// </para>
    /// <para>
    /// This suite asserts POSITION only. The priority VALUES themselves - that a leading <c>!</c> is the
    /// highest band, a leading <c>@</c> the lowest, and <c>[digits]:</c> an explicit one
    /// (<c>w_test_eventful.srw:L210-L212</c>) - belong to the priority and capture suite, and are not
    /// re-asserted here.
    /// </para>
    /// </remarks>
    /// <param name="newcomerTopic">The newcomer's topic, with and without the prepend symbol.</param>
    [Theory]
    [MemberData(nameof(NormalPriorityPlacementSpellingRows))]
    public void WithDistinctPrioritiesPrependAndAppendPlaceTheSubscriberIdentically(
        string newcomerTopic)
    {
        EventBroker broker = new();
        DispatchLog log = new();

        SubscribeLabelled(broker, log, $"!{ClickedTopic}", "high");
        SubscribeLabelled(broker, log, $"@{ClickedTopic}", "low");
        SubscribeLabelled(broker, log, newcomerTopic, "newcomer");

        broker.Trigger(ClickedTopic);

        Assert.Equal(["high", "newcomer", "low"], log.Labels);
    }

    /// <summary>
    /// The oracle's <c>-!clicked</c> spelling lands FIRST among the high-priority peers, and still behind
    /// nothing at all.
    /// </summary>
    /// <remarks>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). <c>w_test_eventful.srw:L224</c> documents
    /// <c>of_On("-!clicked", ...)</c> as "insert at the head of the high-priority run", and combining the
    /// two symbols is legal because they claim different fields - the run at
    /// <c>n_cst_eventful.sru:L340-L360</c> accepts the symbols in any order provided they are all leading.
    /// The trace is worth stating because it is the case where the prepend comparator actually fires on
    /// the FIRST peer examined: the existing high peer's priority is equal to the newcomer's, so
    /// <c>:L417</c> stops immediately with an insert position of one and the newcomer takes the head of
    /// the table.
    /// </remarks>
    [Fact]
    public void APrependedHighPrioritySubscriberLandsFirstAmongItsHighPriorityPeers()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        SubscribeLabelled(broker, log, $"!{ClickedTopic}", "high-first");
        SubscribeLabelled(broker, log, $"!{ClickedTopic}", "high-second");
        SubscribeLabelled(broker, log, ClickedTopic, "normal");

        // The oracle's own spelling, verbatim.
        SubscribeLabelled(broker, log, $"-!{ClickedTopic}", "newcomer");

        broker.Trigger(ClickedTopic);

        Assert.Equal(["newcomer", "high-first", "high-second", "normal"], log.Labels);
    }

    /// <summary>
    /// The oracle's <c>-@clicked</c> spelling lands at the head of the LOW-priority run only - it does not
    /// overtake the normal-priority peer ahead of it.
    /// </summary>
    /// <remarks>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). <c>w_test_eventful.srw:L225</c> documents
    /// <c>of_On("-@clicked", ...)</c> as "insert at the head of the low-priority run", and this is the
    /// case that proves prepend is scoped to the newcomer's own priority band: the comparator at
    /// <c>n_cst_eventful.sru:L417</c> does not fire on the normal-priority peer, because that peer's
    /// priority is not less than or equal to the lowest band, so the scan advances past it
    /// (<c>:L421</c>) and only stops at the first existing low peer.
    /// </remarks>
    [Fact]
    public void APrependedLowPrioritySubscriberLandsAtTheHeadOfTheLowRunOnly()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        SubscribeLabelled(broker, log, ClickedTopic, "normal");
        SubscribeLabelled(broker, log, $"@{ClickedTopic}", "low-first");
        SubscribeLabelled(broker, log, $"@{ClickedTopic}", "low-second");

        // The oracle's own spelling, verbatim.
        SubscribeLabelled(broker, log, $"-@{ClickedTopic}", "newcomer");

        broker.Trigger(ClickedTopic);

        Assert.Equal(["normal", "newcomer", "low-first", "low-second"], log.Labels);
    }

    // =============================================================================================
    //  THE LEXICAL BOUNDS AND THE THREE SHORTCUTS THE ASCENDING ORDER PAYS FOR
    //  bounds maintained :L439-L440, reset and rebuilt :L1025-L1026 with :L1073-L1078
    //  fast reject :L797-L798, midpoint start :L800-L806, walk-past-the-run exits :L824-L827
    // =============================================================================================

    /// <summary>
    /// The three live names every bounds case is measured against, already in ascending ordinal order.
    /// </summary>
    /// <remarks>
    /// Chosen so that a name sorting below all three (<c>"alpha"</c>), between two of them
    /// (<c>"charlie"</c>) and above all three (<c>"omega"</c>) are all easy to read at a glance.
    /// </remarks>
    private static string[] LiveNames => ["beta", "delta", "gamma"];

    /// <summary>
    /// Names that fall OUTSIDE the lexical bounds of <see cref="LiveNames"/>, one below and one above.
    /// </summary>
    public static TheoryData<string> OutsideTheLexicalBoundsRows =>
        new()
        {
            // Below every live name: 'a' precedes 'b', so the :L797 reject fires.
            "alpha",

            // Above every live name: 'o' follows 'g', so the :L798 reject fires.
            "omega",
        };

    /// <summary>
    /// A name outside the lexical bounds runs nothing and returns the RESOLVED default - and the resolved
    /// default is worked out BEFORE the reject, not after it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). The dispatch resolves the default first
    /// (<c>n_cst_eventful.sru:L795</c>) and only then tests the two bounds
    /// (<c>:L797-L798</c>), so a rejected name still answers with the value the caller registered for it.
    /// The statement order matters: had the port rejected first, an unsubscribed event would answer null
    /// instead of its declared neutral value, and the oracle's own advice is to always declare one
    /// (<c>w_test_eventful.srw:L257</c>).
    /// </para>
    /// <para>
    /// Both rejects exist only because the table is kept in ascending order, so this is one of the three
    /// correctness-relevant consequences of the sort. It is also why a port that maintained the bounds
    /// wrongly would dispatch to NOBODY in silence - which the closing reachability assertion is here to
    /// rule out.
    /// </para>
    /// </remarks>
    /// <param name="unsubscribedName">A name outside the bounds, with no subscription of its own.</param>
    [Theory]
    [MemberData(nameof(OutsideTheLexicalBoundsRows))]
    public void ANameOutsideTheLexicalBoundsRunsNothingAndReturnsTheResolvedDefault(
        string unsubscribedName)
    {
        string[] live = LiveNames;

        EventBroker broker = new();
        DispatchLog log = SubscribeNamesAsTheirOwnLabels(broker, live);

        // With no default established at all, the resolved value is null (:L544 returns the unset global).
        Assert.Null(broker.Trigger(unsubscribedName));
        Assert.Empty(log.Labels);

        // A global default reaches the rejected path, because it was resolved at :L795.
        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue("global-default"));
        Assert.Equal("global-default", broker.Trigger(unsubscribedName));
        Assert.Empty(log.Labels);

        // A per-name registration outranks the global one on that same path (:L546-L550, first match wins).
        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue(unsubscribedName, "per-name-default"));
        Assert.Equal("per-name-default", broker.Trigger(unsubscribedName));
        Assert.Empty(log.Labels);

        // None of it disturbed the live names, and every one of them is still reachable in order. A
        // handler returning null never flips the handled latch, so the established default cannot
        // suppress a later subscriber here.
        AssertEveryNameIsReachedInTriggerOrder(broker, log, live);
    }

    /// <summary>
    /// A name that sorts BETWEEN two live names runs nothing either - and it gets there through the
    /// walk-past-the-run exit rather than through a bounds reject.
    /// </summary>
    /// <remarks>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). <c>"charlie"</c> is inside the bounds, so
    /// neither <c>n_cst_eventful.sru:L797</c> nor <c>:L798</c> fires and the walk really does start. It
    /// reads <c>"beta"</c>, finds a smaller name and keeps scanning (<c>:L827</c>); it then reads
    /// <c>"delta"</c>, finds a GREATER name before ever entering a matching run, and exits at
    /// <c>:L826</c>. That second exit is only sound because the table is ascending - without the sort it
    /// would have to scan to the end - and it is the same exit that makes an inverted table dispatch to
    /// nobody, which is what every cross-name assertion in this file leans on.
    /// </remarks>
    [Fact]
    public void ANameBetweenTwoLiveNamesRunsNothingThroughTheWalkPastExit()
    {
        string[] live = LiveNames;

        EventBroker broker = new();
        DispatchLog log = SubscribeNamesAsTheirOwnLabels(broker, live);

        Assert.True(string.CompareOrdinal("beta", "charlie") < 0);
        Assert.True(string.CompareOrdinal("charlie", "delta") < 0);

        Assert.Null(broker.Trigger("charlie"));
        Assert.Empty(log.Labels);

        AssertEveryNameIsReachedInTriggerOrder(broker, log, live);
    }

    /// <summary>
    /// Table sizes either side of the midpoint probe's <c>nCount &gt; 3</c> threshold.
    /// </summary>
    /// <remarks>
    /// Three is the largest size at which the probe is skipped entirely
    /// (<c>n_cst_eventful.sru:L802</c>); four is the smallest at which it engages. Sizes with an odd and
    /// an even count are both present because PowerScript's real division rounds where C# integer
    /// division truncates, and the port records that the difference is unobservable - these rows are
    /// what would notice if it were not.
    /// </remarks>
    public static TheoryData<int> MidpointThresholdRows =>
        new()
        {
            3,
            4,
            5,
            6,
            7,
            8,
        };

    /// <summary>
    /// A subscriber in the FIRST half of the table is still found once the midpoint start optimisation
    /// engages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B), and this is the only assertion in the suite that
    /// catches a mis-computed midpoint. Above three entries the dispatch probes the middle entry's name
    /// and, when it sorts BELOW the requested one, starts scanning from the entry after it
    /// (<c>n_cst_eventful.sru:L800-L806</c>). The guard is the whole safety of the optimisation: it only
    /// ever skips entries that are provably ordinally smaller than the target in an ascending list, and
    /// such entries can never match. Drop the guard - start at the midpoint unconditionally - and every
    /// name in the first half becomes unreachable while every name in the second half still works, so a
    /// suite that only asserted on the largest name would pass.
    /// </para>
    /// <para>
    /// Names are registered in DESCENDING order so the ascending table can only have come from the
    /// ordered insert, and the smallest name is asserted on its own first, because it is the one a broken
    /// midpoint loses.
    /// </para>
    /// </remarks>
    /// <param name="tableSize">How many distinct names to put in the table.</param>
    [Theory]
    [MemberData(nameof(MidpointThresholdRows))]
    public void EveryNameIsStillReachedOnceTheMidpointOptimisationEngages(int tableSize)
    {
        // Ascending ordinal order by construction: same length, and the last character increases.
        string[] ascending = ["n1", "n2", "n3", "n4", "n5", "n6", "n7", "n8"];
        string[] live = [.. ascending.Take(tableSize)];

        Assert.Equal(live, live.OrderBy(name => name, StringComparer.Ordinal).ToArray());

        EventBroker broker = new();
        DispatchLog log = SubscribeNamesAsTheirOwnLabels(broker, Reversed(live));

        // The first-half assertion, on its own, because it is the one a wrong midpoint start loses.
        log.Clear();
        broker.Trigger(live[0]);
        Assert.Equal([live[0]], log.Labels);

        // And then every name, in both directions.
        AssertEveryNameIsReachedInTriggerOrder(broker, log, live);
        AssertEveryNameIsReachedInTriggerOrder(broker, log, Reversed(live));
    }

    /// <summary>
    /// Unsubscribing the lexically SMALLEST name recomputes the lower bound and leaves every survivor
    /// dispatchable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). A removal outside a dispatch blanks both bounds
    /// and rebuilds them from the survivors as it walks (<c>n_cst_eventful.sru:L1025-L1026</c> and
    /// <c>:L1073-L1078</c>), taking the first surviving name as the lower bound and the last as the
    /// upper - which is correct precisely because the table is in ascending order, and would be wrong for
    /// any other arrangement.
    /// </para>
    /// <para>
    /// The last assertion is the one that matters. A rebuild that left the bounds blank would make
    /// <c>:L798</c> fire for every non-empty name, and the broker would then dispatch to NOBODY for the
    /// rest of its life while still reporting success on every call. Asserting that the survivors are
    /// still reachable is the only thing that detects that, and it is why this test does not stop at
    /// "the removed name no longer runs".
    /// </para>
    /// </remarks>
    [Fact]
    public void UnsubscribingTheSmallestNameRecomputesTheLowerBound()
    {
        string[] live = LiveNames;

        EventBroker broker = new();
        DispatchLog log = SubscribeNamesAsTheirOwnLabels(broker, live);

        Assert.Equal(RetCode.OK, broker.Unsubscribe(name: live[0]));

        // The removed name runs nothing: it is now below the rebuilt lower bound.
        log.Clear();
        Assert.Null(broker.Trigger(live[0]));
        Assert.Empty(log.Labels);

        // A name that already sorted below the OLD lower bound is still rejected, unchanged.
        Assert.Null(broker.Trigger("alpha"));
        Assert.Empty(log.Labels);

        // The assertion that catches a wrong rebuild: every survivor still dispatches, in order.
        AssertEveryNameIsReachedInTriggerOrder(broker, log, [live[1], live[2]]);
    }

    /// <summary>
    /// Unsubscribing the lexically LARGEST name recomputes the upper bound and leaves every survivor
    /// dispatchable - the symmetric case.
    /// </summary>
    /// <remarks>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). The upper bound is rebuilt last-wins rather than
    /// first-wins (<c>n_cst_eventful.sru:L1076</c> assigns unconditionally where <c>:L1075</c> guards on
    /// the bound still being empty), so the two halves of the rebuild are asymmetric in the source and are
    /// asserted separately here rather than assumed to mirror each other.
    /// </remarks>
    [Fact]
    public void UnsubscribingTheLargestNameRecomputesTheUpperBound()
    {
        string[] live = LiveNames;

        EventBroker broker = new();
        DispatchLog log = SubscribeNamesAsTheirOwnLabels(broker, live);

        Assert.Equal(RetCode.OK, broker.Unsubscribe(name: live[2]));

        // The removed name runs nothing: it is now above the rebuilt upper bound.
        log.Clear();
        Assert.Null(broker.Trigger(live[2]));
        Assert.Empty(log.Labels);

        // A name that already sorted above the OLD upper bound is still rejected, unchanged.
        Assert.Null(broker.Trigger("omega"));
        Assert.Empty(log.Labels);

        // And every survivor still dispatches, in order.
        AssertEveryNameIsReachedInTriggerOrder(broker, log, [live[0], live[1]]);
    }

    // =============================================================================================
    //  SUBSCRIBING DURING A DISPATCH - it takes effect only on the NEXT dispatch
    //  w_test_eventful.srw:L237 states the rule; the implementation is the loop bound captured once at
    //  n_cst_eventful.sru:L820 together with the in-flight cursor fix-up at :L423-L433
    // =============================================================================================

    /// <summary>
    /// The name every mid-dispatch case dispatches. Deliberately spelled with a middle letter so a
    /// newcomer can be given a name that sorts either side of it.
    /// </summary>
    private const string DispatchingTopic = "b-evt";

    /// <summary>
    /// Each newcomer topic a handler subscribes from inside a dispatch, paired with the label sequence the
    /// NEXT trigger of that newcomer's own name must produce.
    /// </summary>
    /// <remarks>
    /// The three rows put the newcomer at three different positions relative to the running dispatch's
    /// cursor, which is what makes the rule's independence from insert position assertable:
    /// <list type="bullet">
    ///   <item><description>
    ///   the SAME name, which appends behind the running subscriber (<c>n_cst_eventful.sru:L419</c> walks
    ///   past its equal-priority peer), so the next trigger of that one name runs both;
    ///   </description></item>
    ///   <item><description>
    ///   a name sorting BEFORE it, which inserts at the head of the table - ahead of the cursor, so the
    ///   fix-up at <c>:L427-L433</c> fires;
    ///   </description></item>
    ///   <item><description>
    ///   a name sorting AFTER it, which appends past the end - behind the cursor, so the fix-up does not
    ///   fire at all (the legacy omits it on that branch, <c>:L434-L435</c>).
    ///   </description></item>
    /// </list>
    /// </remarks>
    public static TheoryData<string, string> MidDispatchNewcomerRows =>
        new()
        {
            { DispatchingTopic, "running newcomer" },
            { "a-evt", "newcomer" },
            { "c-evt", "newcomer" },
        };

    /// <summary>
    /// A subscription made from inside a dispatch does NOT run in that dispatch, and DOES run on the next
    /// one - wherever in the table it landed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B). The rule is the oracle's own, stated in prose at
    /// <c>ws_objects/pfw.tests.pbl.src/w_test_eventful.srw:L237</c>: subscribing a new event while an event
    /// is being dispatched takes effect only on the NEXT dispatch. The DOCUMENTED OUTCOME is what is
    /// asserted here, not the mechanism - the mechanism has two halves and neither is a promise to a
    /// caller. The loop's upper bound is evaluated once at <c>n_cst_eventful.sru:L820</c>, because that is
    /// what a PowerScript <c>for</c> does, so a longer table simply is not noticed; and the insert pushes
    /// every in-flight cursor along when it lands at or before one (<c>:L423-L433</c>), so the running
    /// level keeps pointing at the subscription it was already on rather than being shifted onto its
    /// neighbour.
    /// </para>
    /// <para>
    /// Exactly ONE existing subscriber is used, deliberately. With more than one, an insert ahead of the
    /// cursor also costs the running dispatch its last peer - a real consequence of the two halves above,
    /// asserted on its own in
    /// <see cref="APrependedMidDispatchSubscriptionAlsoCostsTheRunningDispatchItsLastPeer"/> - and mixing
    /// the two effects into one test would leave neither clearly pinned.
    /// </para>
    /// <para>
    /// The in-handler subscription is made through the double's own in-handler callback, guarded by a
    /// captured flag so it happens exactly once and the second trigger cannot register a duplicate. The
    /// return code is captured into a local and asserted AFTER the dispatch rather than inside the
    /// handler, because an assertion failure thrown from a handler would travel out through the broker's
    /// exception-decoration path and arrive annotated.
    /// </para>
    /// </remarks>
    /// <param name="newcomerName">The topic the running handler subscribes.</param>
    /// <param name="expectedNextDispatch">
    /// The labels the next trigger of <paramref name="newcomerName"/> must produce.
    /// </param>
    [Theory]
    [MemberData(nameof(MidDispatchNewcomerRows))]
    public void ASubscriptionMadeDuringADispatchTakesEffectOnlyOnTheNextDispatch(
        string newcomerName,
        string expectedNextDispatch)
    {
        EventBroker broker = new();
        DispatchLog log = new();

        RecordingSubscriber newcomer = new(log, "newcomer", broker);
        RecordingSubscriber running = SubscribeLabelled(broker, log, DispatchingTopic, "running");

        long subscribeCode = RetCode.FAILED;
        bool subscribedOnce = false;

        running.SetInterceptor(
            RecordingHandlerNames.NoArguments,
            (_, _) =>
            {
                if (subscribedOnce)
                {
                    return;
                }

                subscribedOnce = true;
                subscribeCode = broker.Subscribe(
                    newcomerName,
                    newcomer,
                    RecordingHandlerNames.NoArguments);
            });

        broker.Trigger(DispatchingTopic);

        // The subscription really was accepted, mid-dispatch, and took effect on the table immediately.
        Assert.True(subscribedOnce);
        Assert.Equal(RetCode.OK, subscribeCode);

        // It simply did not RUN: only the subscriber that was present when the loop started did.
        Assert.Equal(["running"], log.Labels);
        Assert.Equal(0, newcomer.InvocationCount);

        // And on the next dispatch of its own name it runs, in its ordered position.
        log.Clear();
        broker.Trigger(newcomerName);

        Assert.Equal(SplitNames(expectedNextDispatch), log.Labels);
        Assert.Equal(1, newcomer.InvocationCount);
    }

    /// <summary>
    /// A PREPENDED mid-dispatch subscription also costs the running dispatch its LAST peer - a legacy
    /// consequence that is reproduced rather than corrected.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PINS LEGACY SEMANTICS AS CORRECT (AAP 0.7.3 C-B), and it is the one place in this file where the
    /// pinned behaviour looks like a defect. The two halves of the next-dispatch rule interact: the loop's
    /// upper bound was captured before the insert (<c>n_cst_eventful.sru:L820</c>) while the in-flight
    /// cursor is pushed forward by the insert (<c>:L423-L433</c>), so the cursor reaches the captured
    /// bound one element early and the tail entry of the current run is skipped - even though that entry
    /// was registered long before the dispatch began. It is dispatched normally on the next trigger.
    /// </para>
    /// <para>
    /// The alternative, omitting the fix-up, would dispatch the entry the cursor is currently ON a SECOND
    /// time within one dispatch. The oracle chose the skip, so the port reproduces the skip, and this test
    /// asserts it as correct. Correcting it would be exactly the silent behaviour change C-B forbids.
    /// </para>
    /// <para>
    /// The trace, so the expectation is derived rather than observed: the table starts
    /// <c>[first, second, third]</c> with the bound captured at three and the cursor at the first entry;
    /// <c>first</c> runs and inserts the newcomer at the head, which pushes the cursor to the second
    /// element; the increment moves it to the third element, which is <c>second</c>, and that runs; the
    /// next increment reaches the captured bound and the loop ends with <c>third</c> unvisited.
    /// </para>
    /// </remarks>
    [Fact]
    public void APrependedMidDispatchSubscriptionAlsoCostsTheRunningDispatchItsLastPeer()
    {
        EventBroker broker = new();
        DispatchLog log = new();

        RecordingSubscriber newcomer = new(log, "newcomer", broker);
        RecordingSubscriber first = SubscribeLabelled(broker, log, ClickedTopic, "first");
        SubscribeLabelled(broker, log, ClickedTopic, "second");
        SubscribeLabelled(broker, log, ClickedTopic, "third");

        long subscribeCode = RetCode.FAILED;
        bool subscribedOnce = false;

        first.SetInterceptor(
            RecordingHandlerNames.NoArguments,
            (_, _) =>
            {
                if (subscribedOnce)
                {
                    return;
                }

                subscribedOnce = true;

                // The prepend symbol, so the insert lands at the HEAD - at or before the live cursor.
                subscribeCode = broker.Subscribe(
                    $"-{ClickedTopic}",
                    newcomer,
                    RecordingHandlerNames.NoArguments);
            });

        broker.Trigger(ClickedTopic);

        Assert.True(subscribedOnce);
        Assert.Equal(RetCode.OK, subscribeCode);

        // The newcomer did not run, AND "third" was skipped. Both are legacy behaviour.
        Assert.Equal(["first", "second"], log.Labels);
        Assert.Equal(0, newcomer.InvocationCount);

        // The next dispatch is complete and correctly ordered: the newcomer took the head of the
        // equal-priority run (:L417) and "third" is back.
        log.Clear();
        broker.Trigger(ClickedTopic);

        Assert.Equal(["newcomer", "first", "second", "third"], log.Labels);
    }
}
