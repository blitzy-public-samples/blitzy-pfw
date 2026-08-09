// =====================================================================================================
//  SubscriptionTopicGrammarTests.cs
//  PowerFramework.Shared.Eventful.Tests
// =====================================================================================================
//
//  WHAT THIS SUITE IS
//  The parity matrix for the event broker's EIGHT-SYMBOL TOPIC GRAMMAR and its byte-exact round trip.
//  It is the first suite of the Eventful set by design: every other suite in this folder spells topics
//  using the grammar pinned here, so a defect admitted here would be inherited by all of them. It
//  therefore asserts the grammar itself - what each symbol means, where a symbol stops being a symbol,
//  which spellings are rejected, and that a parsed topic re-emits its own bytes - and deliberately
//  leaves dispatch, subscription storage and veto behaviour to the broker suites.
//
//  THE ONE THING THIS SUITE EXISTS TO PROTECT
//  `SubscriptionTopic.LegacyName` retains the ordering prefix. `se_cst_dw.sru:L54` declares the live
//  DataWindow topic as "0-itemchanged", not "itemchanged" with a sequence beside it. If a future change
//  "tidied" that into a bare logical name and then dispatched or sorted on the tidied value, every
//  ordering guarantee in the DataWindow event chain would shift silently - the subscriptions would still
//  register, nothing would throw, and no other suite in this folder would notice. The decomposition
//  tests below assert the prefix is preserved AND that the triple projected from it fuses back to the
//  identical string.
//
//  THE AUTHORITATIVE SPECIFICATION (read as specification, never edited - AAP 0.7.3 C-C)
//      ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru
//          :L91-L98      the eight grammar symbols, a contiguous constant block and the closed set
//          :L100-L106    the capture flags and the three named priorities
//          :L326-L381    of_on - THE SUBSCRIBE GRAMMAR: symbol run, numeric priority, namespace
//          :L993-L1023   _of_modify - THE FILTER GRAMMAR: a DIFFERENT language over the same symbols
//      ws_objects/pfw.tests.pbl.src/w_test_eventful.srw
//          :L220-L230    the oracle's own worked list of eleven subscribe spellings
//          :L232         the combination rules and "symbol order may be arbitrary but must be leading"
//          :L238         subscriptions are ordered by name ASCENDING - why the prefix orders the chain
//          :L251         of_On("*clicked.myns", ...) as actually called
//          :L297-L339    the thirteen-form filter matrix, each form with its documented meaning
//      ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L54, :L57
//          EVT_ITEMCHANGED = "0-itemchanged" and EVT_EDITCHANGED = "1-editchanged"
//      ws_objects/pfw.thread.pbl.src/n_cst_threading.sru:L544, :L593-L597
//          the ".^persistent" bulk unsubscribe and the caller-side rule that builds it
//
//  A NOTE ON LINE NUMBERS. Every locator below was re-verified against the oracle while this file was
//  written, and three differ from the citations in the task brief. The VERIFIED numbers are used here:
//  the free-symbol-order rule is w_test_eventful.srw:L232 (the brief says :L236, which is the of_Prevent
//  note); of_On("*clicked.myns", ...) is :L251 (the brief says :L254, a default-return-value comment);
//  and the filter matrix spans :L306-:L339 under a doc block at :L297-:L303 (the brief says :L305-:L345).
//
//  RULES POSITION, STATED EXPLICITLY
//  review_rules reports exactly one line: "No user rules provided." No user-specified rule governs this
//  file and none was invented to fill the gap. In their place the AAP 0.7.2 enterprise baseline and the
//  AAP 0.7.3 binding non-rule constraints apply. The four that bear on this suite:
//
//      C-B   BEHAVIOUR IS PINNED AS CORRECT, NEVER CORRECTED. Every assertion here asserts the legacy
//            grammar as RIGHT, and each carries a locator so a later reader cannot mistake a quirk for a
//            bug and "fix" it. The five quirks this suite pins deliberately:
//              1. A leading digit is not a symbol, so the run ends at position one and "0-itemchanged"
//                 keeps its prefix with Prepend FALSE (:L357-L358 reached from :L340).
//              2. The colon prefix is honoured only when numeric; a non-numeric prefix leaves both the
//                 prefix and the colon inside the name, and that is NOT an error (:L365-L373, no else).
//              3. The namespace split takes the FIRST separator, so a second one lands in the namespace
//                 and an event name can never contain one (:L375-L377).
//              4. An empty namespace is REJECTED when subscribing (:L379) and MEANINGFUL when filtering
//                 (:L1002 with :L1042). The two grammars are not harmonised.
//              5. '+' is documented as an append symbol at :L275 but is absent from the constant block
//                 and from the parse loop, so it is an ordinary character. The documentation and the
//                 implementation disagree; the IMPLEMENTATION is preserved and the discrepancy is not
//                 resolved.
//      C-D   NO DEFERRED CAPABILITY APPEARS. The legacy's of_on creates a dynamic script invoker at
//            n_cst_eventful.sru:L398, and the library that type belongs to is assigned to the deferred
//            ScriptBridge service (AAP 0.2.2.2). No CODE in this file references, imports or stands in
//            for it - this note is the only mention, and it exists to record the exclusion rather than
//            to leave it looking like an oversight. Nothing is lost by the exclusion: a topic is a
//            string, and parsing one needs no invoker.
//      C-H   NULLABLE AND WARNINGS AS ERRORS APPLY. Directory.Build.props sets TreatWarningsAsErrors, so
//            this file carries no #pragma warning disable, no null-forgiving operator dodging a real
//            nullability question - the parse helpers use Assert.IsType, which RETURNS a non-null
//            reference - and no member data column that is not asserted.
//      C-K   THE BOUNDARY DECISION IS NAMED. See the reconstitution region, which records why the wire
//            triple exists at all rather than merely testing that it does.
//
//  NAMING. Every member here is PascalCase. The repository-root .editorconfig scopes its naming-analyzer
//  suppressions to the named implementation files on its BAND 3 roster - the single source of truth
//  for that list - and no test file is among them, so a preserved
//  SCREAMING_SNAKE member in this file would be a build error rather than a style debate. The legacy
//  spellings are therefore kept in comments, and the legacy CONSTANTS are referenced through the shared
//  types - RetCode.E_INVALID_ARGUMENT, never the bare -3.
//
//  PURITY. Topic parsing is a total function of one string. This suite touches no file, no socket, no
//  clock, no GUID and no random source, and every comparison is ORDINAL - topic names are case sensitive
//  (only the handler name is folded, at :L336), so a culture-sensitive or case-insensitive comparison
//  would silently pass on wrong input.
// =====================================================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

using PowerFramework.Shared.Kernel;

using Xunit;

namespace PowerFramework.Shared.Eventful.Tests;

/// <summary>
/// Parity matrix for the eight-symbol subscription-topic grammar
/// (<c>n_cst_eventful.sru:L91-L98</c>), both of its parse positions
/// (<c>of_on</c> at <c>:L326-L381</c> and <c>_of_modify</c> at <c>:L993-L1023</c>), and the byte-exact
/// reconstitution of every topic spelling evidenced anywhere in the legacy tree.
/// </summary>
public class SubscriptionTopicGrammarTests
{
    // =================================================================================================
    //  HELPERS
    //  Two parse helpers and one comparison helper, so that several hundred assertions below read as
    //  grammar statements rather than as out-parameter plumbing.
    // =================================================================================================

    /// <summary>
    /// Parses <paramref name="topic"/> under the subscribe grammar, asserting the legacy success code
    /// and returning the decoded topic.
    /// </summary>
    /// <remarks>
    /// <see cref="Assert.IsType{T}(object)"/> rather than <c>Assert.NotNull</c> followed by a
    /// null-forgiving operator: it returns a non-null <see cref="SubscriptionTopic"/>, so the helper
    /// satisfies nullable analysis by construction instead of overriding it (AAP 0.7.3 C-H).
    /// </remarks>
    private static SubscriptionTopic ParseSubscribe(string? topic)
    {
        long code = SubscriptionTopic.ParseSubscription(topic, out SubscriptionTopic? parsed);

        // RetCode.OK is the legacy's own success value (retcode.sru, RetCode.cs:L259), referenced
        // through the constant rather than spelled as 0.
        Assert.Equal(RetCode.OK, code);

        return Assert.IsType<SubscriptionTopic>(parsed);
    }

    /// <summary>
    /// Parses <paramref name="filter"/> under the off/disable filter grammar, asserting the legacy
    /// success code and returning the decoded topic.
    /// </summary>
    private static SubscriptionTopic ParseFilter(string? filter)
    {
        long code = SubscriptionTopic.ParseFilter(filter, out SubscriptionTopic? parsed);

        Assert.Equal(RetCode.OK, code);

        return Assert.IsType<SubscriptionTopic>(parsed);
    }

    /// <summary>
    /// Asserts that <paramref name="topic"/> re-emits <paramref name="expected"/> byte for byte under
    /// ordinal comparison.
    /// </summary>
    /// <remarks>
    /// The round trip is the whole point of the grammar suite, so it is asserted through one helper that
    /// cannot accidentally be written with a culture-sensitive comparison.
    /// <see cref="SubscriptionTopic.ToLegacyString"/> achieves byte-exactness by RETAINING the parsed
    /// string rather than regenerating it, which is the only design that can round-trip a grammar whose
    /// leading symbols may be written in any order (<c>w_test_eventful.srw:L232</c>).
    /// </remarks>
    private static void AssertRoundTripsExactly(string expected, SubscriptionTopic topic)
    {
        Assert.Equal(expected, topic.ToLegacyString(), StringComparer.Ordinal);
    }

    // =================================================================================================
    //  THE LEADING SYMBOL RUN - ONE ROW PER SYMBOL
    //  n_cst_eventful.sru:L339-L360. A character loop over position 1 upward whose `choose case` handles
    //  '-', '%', '*', '@' and '!', and whose `case else` arm EXITS. Order within the run is free; only
    //  the position is fixed, in that the run must be leading (w_test_eventful.srw:L232).
    // =================================================================================================

    /// <summary>
    /// One row per symbol of the leading run, plus the no-symbol baseline: the topic, the residual name
    /// the legacy would store, and the three subscribe options.
    /// </summary>
    public static TheoryData<string, string, bool, CaptureMode, int> LeadingRunSymbolRows =>
        new()
        {
            // w_test_eventful.srw:L220 - of_On("clicked",...) "默认优先级" (default priority). The
            // baseline: no symbol, so every option keeps the default established at :L328-L336.
            { "clicked", "clicked", false, CaptureMode.Unhandled, Priorities.Normal },

            // :L223 - of_On("-clicked",...) inserts at the HEAD of the equal-priority run.
            // n_cst_eventful.sru:L342-L344 sets prepend; the name is otherwise untouched.
            { "-clicked", "clicked", true, CaptureMode.Unhandled, Priorities.Normal },

            // :L222 - of_On("@clicked",...) "低优先级" (low priority). :L351-L353.
            { "@clicked", "clicked", false, CaptureMode.Unhandled, Priorities.Low },

            // :L221 - of_On("!clicked",...) "高优先级" (high priority). :L354-L356.
            { "!clicked", "clicked", false, CaptureMode.Unhandled, Priorities.High },

            // :L230 - of_On("%click",...) "捕获已处理的事件" (capture events already handled by an
            // earlier subscriber). :L345-L347. Note the oracle's own spelling is "click", not "clicked".
            { "%click", "click", false, CaptureMode.Handled, Priorities.Normal },

            // :L229 - of_On("*click",...) "捕获所有事件" (capture every handled-state). :L348-L350.
            { "*click", "click", false, CaptureMode.All, Priorities.Normal }
        };

    /// <summary>
    /// Each symbol of the leading run sets its own field, leaves the residual name otherwise intact, and
    /// the whole topic re-emits byte for byte.
    /// </summary>
    [Theory]
    [MemberData(nameof(LeadingRunSymbolRows))]
    public void EachLeadingRunSymbolSetsItsOwnFieldAndRoundTripsExactly(
        string topic,
        string expectedName,
        bool expectedPrepend,
        CaptureMode expectedCapture,
        int expectedPriority)
    {
        SubscriptionTopic parsed = ParseSubscribe(topic);

        // :L363 - newEvent.name = Mid(newEvent.name,nPos): the run is stripped and what remains is the
        // stored name. Ordinal, because a topic name is case sensitive (only the handler name is folded,
        // at :L336).
        Assert.Equal(expectedName, parsed.LegacyName, StringComparer.Ordinal);

        Assert.Equal(expectedPrepend, parsed.Prepend);
        Assert.Equal(expectedCapture, parsed.Capture);
        Assert.Equal(expectedPriority, parsed.Priority);

        // The subscribe grammar has no negation case, so neither flag can ever be set here
        // (:L339-L359 has no arm for '^').
        Assert.False(parsed.NegateName);
        Assert.False(parsed.NegateNamespace);
        Assert.Equal(TopicGrammar.Subscription, parsed.Grammar);

        // None of these spellings carries a separator, so the namespace is ABSENT rather than empty
        // (:L375, Pos(...) = 0 leaves the field untouched).
        Assert.Null(parsed.NamespaceCriterion);
        Assert.False(parsed.HasNamespaceCriterion);

        AssertRoundTripsExactly(topic, parsed);
    }

    /// <summary>
    /// The three named priorities are the 32-bit extremes and zero, exactly as the legacy declares them.
    /// </summary>
    /// <remarks>
    /// <c>n_cst_eventful.sru:L104-L106</c> declares <c>PRIORITY_LOW = -2147483648</c>,
    /// <c>PRIORITY_NORMAL = 0</c> and <c>PRIORITY_HIGH = 2147483647</c> - the bounds of a PowerScript
    /// <c>long</c>, which is 32 bits wide. The width is asserted as well as the value: widening these to
    /// 64 bits would leave every comparison correct while making the two extremes reachable by ordinary
    /// arithmetic, so a numeric <c>[digits]:</c> priority could then out-rank <c>!</c>. A single scalar
    /// pair of facts with no table of its own, which is why this is a <see cref="FactAttribute"/>.
    /// </remarks>
    [Fact]
    public void TheNamedPrioritiesAreTheThirtyTwoBitExtremes()
    {
        // :L104 - constant long PRIORITY_LOW = -2147483648
        Assert.Equal(int.MinValue, Priorities.Low);

        // :L105 - constant long PRIORITY_NORMAL = 0
        Assert.Equal(0, Priorities.Normal);

        // :L106 - constant long PRIORITY_HIGH = 2147483647
        Assert.Equal(int.MaxValue, Priorities.High);

        // 32 bits, not a widened 64-bit value. Boxing reports the compile-time type of each constant.
        Assert.IsType<int>(Priorities.Low);
        Assert.IsType<int>(Priorities.High);

        // And the property the parser writes is the same width, so no widening can creep in between the
        // constant and the field it lands in.
        Assert.IsType<int>(ParseSubscribe("!clicked").Priority);
    }

    /// <summary>
    /// The symbol pair that carries the low and high priorities maps onto exactly those two constants.
    /// </summary>
    public static TheoryData<string, int> PrioritySymbolRows =>
        new()
        {
            // w_test_eventful.srw:L222 with n_cst_eventful.sru:L353.
            { "@clicked", int.MinValue },

            // :L221 with :L356.
            { "!clicked", int.MaxValue }
        };

    /// <summary>
    /// The <c>@</c> and <c>!</c> symbols resolve to the named priority constants, which are themselves
    /// the 32-bit extremes.
    /// </summary>
    [Theory]
    [MemberData(nameof(PrioritySymbolRows))]
    public void APrioritySymbolResolvesToItsNamedThirtyTwoBitConstant(string topic, int expectedPriority)
    {
        SubscriptionTopic parsed = ParseSubscribe(topic);

        Assert.Equal(expectedPriority, parsed.Priority);

        // Asserted both ways round: against the extreme, and against the named constant, so that a
        // change to either is caught rather than absorbed.
        Assert.True(
            parsed.Priority == Priorities.Low || parsed.Priority == Priorities.High,
            "A priority symbol must resolve to one of the two named priority constants.");

        AssertRoundTripsExactly(topic, parsed);
    }

    // =================================================================================================
    //  THE PRIORITY DELIMITER ':' - HONOURED ONLY WHEN THE PREFIX IS NUMERIC
    //  n_cst_eventful.sru:L365-L373. The FIRST colon is located; the text before it becomes the priority
    //  ONLY IF IsNumber(...) accepts it, in which case both the digits and the colon are stripped. There
    //  is NO else branch, so a non-numeric prefix is silently left alone - the colon stays inside the
    //  event name and this is NOT an error. That asymmetry is quirk 2 in the header and is pinned as
    //  correct under AAP 0.7.3 C-B.
    // =================================================================================================

    /// <summary>
    /// The colon in each of its two readings: consumed as a numeric priority, or retained as an ordinary
    /// character of the event name.
    /// </summary>
    public static TheoryData<string, string, int, bool> PriorityDelimiterRows =>
        new()
        {
            // w_test_eventful.srw:L226 - of_On("9999:clicked",...) "优先级为9999" (priority 9999). The
            // canonical evidenced case: digits AND the delimiter are both stripped (:L371) and a larger
            // number means a higher priority (:L212).
            { "9999:clicked", "clicked", 9999, false },

            // Same rule at a second magnitude, so the assertion cannot pass by coincidence of one value.
            { "12:clicked", "clicked", 12, false },

            // An explicit "0:" is numeric and legal, and lands the priority on Normal. The guard at
            // :L369 is evaluated BEFORE the assignment at :L370 and only tests whether the field has
            // already moved off Normal, so an explicit zero does not claim the slot.
            { "0:clicked", "clicked", Priorities.Normal, false },

            // The legacy applies NO range check to the parsed number (:L370 is a bare Long(sVal)
            // assignment), so an arbitrarily large value inside int range is taken verbatim.
            { "2147483646:clicked", "clicked", 2147483646, false },

            // Reads two ways to a human and one way to the parser: the leading run is scanned BEFORE the
            // colon is looked for, so the '-' is consumed as prepend at :L342-L344 and the numeric prefix
            // that remains is "5". The priority is therefore +5 WITH prepend, never -5. See the dedicated
            // fact below, and the unreachability fact after it.
            { "-5:clicked", "clicked", 5, true },

            // QUIRK 2, the heart of this region. "abc" is not numeric, so :L368 fails, NOTHING happens,
            // and the colon remains part of the stored name. A legal event name may contain a colon.
            { "abc:clicked", "abc:clicked", Priorities.Normal, false },

            // The empty string is not numeric either, so a leading bare colon is kept. This is the
            // IsNumber("") = false case.
            { ":clicked", ":clicked", Priorities.Normal, false },

            // A trailing colon: the prefix "clicked" is not numeric, so the colon survives at the end.
            { "clicked:", "clicked:", Priorities.Normal, false },

            // Only the FIRST colon is examined (:L365 uses Pos, which returns the first occurrence).
            // "9999" is numeric, so it is consumed - and the SECOND colon is simply part of the name.
            { "9999:a:b", "a:b", 9999, false }
        };

    /// <summary>
    /// A numeric prefix before the first colon becomes the priority and is stripped with its delimiter; a
    /// non-numeric prefix leaves both itself and the colon inside the name, and is not an error.
    /// </summary>
    [Theory]
    [MemberData(nameof(PriorityDelimiterRows))]
    public void ThePriorityDelimiterIsHonouredOnlyWhenItsPrefixIsNumeric(
        string topic,
        string expectedName,
        int expectedPriority,
        bool expectedPrepend)
    {
        SubscriptionTopic parsed = ParseSubscribe(topic);

        Assert.Equal(expectedName, parsed.LegacyName, StringComparer.Ordinal);
        Assert.Equal(expectedPriority, parsed.Priority);
        Assert.Equal(expectedPrepend, parsed.Prepend);

        // The delimiter never touches the capture slot; the axes of the string are independent
        // (w_test_eventful.srw:L233 makes the same point about the namespace).
        Assert.Equal(CaptureMode.Unhandled, parsed.Capture);

        AssertRoundTripsExactly(topic, parsed);
    }

    /// <summary>
    /// A leading <c>-</c> immediately before a numeric priority is read as the prepend symbol first, so
    /// the number that follows is positive rather than negative.
    /// </summary>
    /// <remarks>
    /// The one row above that sets <see cref="SubscriptionTopic.Prepend"/> deserves its own statement,
    /// because <c>"-5:clicked"</c> reads two ways to a human and only one way to the parser. The leading
    /// run is scanned BEFORE the colon is looked for (<c>:L339-L360</c> precedes <c>:L365</c>), so the
    /// <c>-</c> is consumed as <see cref="TopicSymbols.Prepend"/> at <c>:L342-L344</c> and the numeric
    /// prefix that remains is <c>"5"</c>. The resulting priority is therefore <b>+5, not -5</b>, and the
    /// subscription is prepended. A parser that looked for the colon first would produce -5 and no
    /// prepend, which is a different subscription in a different queue position.
    /// </remarks>
    [Fact]
    public void ALeadingMinusIsConsumedAsPrependBeforeTheNumericPriorityIsRead()
    {
        SubscriptionTopic parsed = ParseSubscribe("-5:clicked");

        Assert.True(parsed.Prepend);
        Assert.Equal(5, parsed.Priority);
        Assert.Equal("clicked", parsed.LegacyName, StringComparer.Ordinal);
        AssertRoundTripsExactly("-5:clicked", parsed);
    }

    /// <summary>
    /// Every spelling that could express a NEGATIVE numeric priority: none of them does, so a negative
    /// custom priority is unreachable through the subscribe grammar.
    /// </summary>
    public static TheoryData<string> UnreachableNegativePriorityRows =>
        new()
        {
            // The minus is consumed as prepend (:L342-L344) and the prefix left behind is "5", so the
            // priority is positive.
            "-5:clicked",

            // Doubling the minus to protect the sign trips the once-only prepend guard at :L343 instead,
            // so this spelling does not parse at all.
            "--5:clicked",

            // Moving the minus off position one makes the prefix "a-5", which is not numeric, so the
            // colon is simply retained in the name (:L368 fails, no else branch).
            "a-5:clicked"
        };

    /// <summary>
    /// A negative numeric priority cannot be expressed in the subscribe grammar at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pinned as correct under AAP 0.7.3 C-B, and recorded because it is a closure property of the
    /// grammar rather than an accident of one spelling. PowerScript's <c>IsNumber("-5")</c> would accept a
    /// leading minus, so the restriction comes entirely from the ORDER of the parse: the leading symbol
    /// run at <c>:L339-L360</c> is scanned before the colon is located at <c>:L365</c>, so a minus at
    /// position one is always the prepend symbol; a second minus trips the once-only guard at
    /// <c>:L343</c>; and a minus anywhere later makes the prefix non-numeric. Every route is closed.
    /// </para>
    /// <para>
    /// This matters because <see cref="Priorities.Low"/> is <see cref="int.MinValue"/>: if a negative
    /// custom priority WERE reachable, a caller could construct a value below the named low priority and
    /// invert the intended ordering of the <c>@</c> symbol. The grammar forecloses that, and the three
    /// rows record each closed route rather than only the obvious one.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(UnreachableNegativePriorityRows))]
    public void ANegativeNumericPriorityCannotBeSpelled(string topic)
    {
        long code = SubscriptionTopic.ParseSubscription(topic, out SubscriptionTopic? parsed);

        if (code != RetCode.OK)
        {
            // The "--5:clicked" route: rejected outright by the duplicate-prepend guard, and rejected the
            // way the legacy rejects everything - with a code, never an exception.
            Assert.Equal(RetCode.E_INVALID_ARGUMENT, code);
            Assert.Null(parsed);
            return;
        }

        SubscriptionTopic topicValue = Assert.IsType<SubscriptionTopic>(parsed);

        // Whichever surviving route was taken, the resulting priority is never negative.
        Assert.True(
            topicValue.Priority >= Priorities.Normal,
            $"'{topic}' must not yield a negative priority; it yielded {topicValue.Priority}.");
    }

    // =================================================================================================
    //  THE NAMESPACE SEPARATOR '.' - THE FIRST ONE SPLITS, AND THE REST IS NAMESPACE
    //  n_cst_eventful.sru:L375-L380. Pos returns the FIRST occurrence, so everything after it is the
    //  namespace and everything before it is the name. Two consequences are pinned below: a second
    //  separator lands in the NAMESPACE, and an event name can therefore never contain one at all.
    // =================================================================================================

    /// <summary>
    /// Every evidenced namespaced subscribe spelling, plus the multi-separator case that proves the split
    /// takes the first separator.
    /// </summary>
    public static TheoryData<string, string, string> NamespaceSeparatorRows =>
        new()
        {
            // w_test_eventful.srw:L227 - of_On("clicked.myns",...) subscribes under the [.myns] namespace.
            { "clicked.myns", "clicked", "myns" },

            // :L228 - of_On("close.myns",...) with the note that of_Off(".myns") then cancels every
            // subscription in that namespace.
            { "close.myns", "close", "myns" },

            // :L252 - _eventful.of_On("test.myns",parent,"onTest") as actually called.
            { "test.myns", "test", "myns" },

            // QUIRK 3. The split is on the FIRST separator, so the second one belongs to the NAMESPACE,
            // not to the name. An event name consequently can never contain a separator.
            { "clicked.my.ns", "clicked", "my.ns" },

            // The same rule at depth, so the assertion is about the rule and not about one shape.
            { "clicked.a.b.c", "clicked", "a.b.c" }
        };

    /// <summary>
    /// The namespace is split from the name on the first separator, and everything after that separator -
    /// including any further separators - is the namespace.
    /// </summary>
    [Theory]
    [MemberData(nameof(NamespaceSeparatorRows))]
    public void TheNamespaceIsSplitOnTheFirstSeparator(
        string topic,
        string expectedName,
        string expectedNamespace)
    {
        SubscriptionTopic parsed = ParseSubscribe(topic);

        // :L378 - newEvent.name = Left(newEvent.name,nPos - 1)
        Assert.Equal(expectedName, parsed.LegacyName, StringComparer.Ordinal);

        // :L377 - newEvent.ns = Mid(newEvent.name,nPos + 1)
        Assert.Equal(expectedNamespace, parsed.NamespaceCriterion, StringComparer.Ordinal);
        Assert.Equal(expectedNamespace, parsed.Namespace, StringComparer.Ordinal);

        // A separator was present and the namespace is non-empty, so both questions answer true. The
        // subscribe grammar cannot produce a present-but-EMPTY namespace at all, because :L379 rejects
        // it - which is what makes these two properties indistinguishable here and distinct in a filter.
        Assert.True(parsed.HasNamespaceCriterion);
        Assert.True(parsed.HasNamespace);

        // The name never keeps a separator, which is the direct corollary of the first-separator split.
        Assert.DoesNotContain(TopicSymbols.NamespaceSeparator, parsed.LegacyName);

        AssertRoundTripsExactly(topic, parsed);
    }

    // =================================================================================================
    //  THE NEGATION SYMBOL '^' - A FILTER-ONLY SYMBOL, READ INDEPENDENTLY ON EACH PART
    //  n_cst_eventful.sru:L1008-L1015. The name is split from the namespace FIRST (:L1000-L1006), and
    //  only then is a leading '^' tested on each part SEPARATELY. Either, both or neither may be negated.
    //  of_on has no case for '^' at all, so in subscribe position it is an ordinary character - which is
    //  asserted immediately after this, because that contrast is the point.
    // =================================================================================================

    /// <summary>
    /// The negation cross-product from the oracle's filter matrix: neither part negated, the name only,
    /// the namespace only, and both.
    /// </summary>
    public static TheoryData<string, string, string?, bool, bool> FilterNegationRows =>
        new()
        {
            // w_test_eventful.srw:L318 - of_Off("clicked.myns") cancels that event in that namespace.
            // The un-negated baseline of the cross-product.
            { "clicked.myns", "clicked", "myns", false, false },

            // :L309 - of_Off("^clicked") cancels everything EXCEPT that event. Name negated, and no
            // separator at all, so the namespace is not a criterion (hence null).
            { "^clicked", "clicked", null, true, false },

            // :L321 - of_Off("^clicked.myns") cancels everything in that namespace except that event.
            { "^clicked.myns", "clicked", "myns", true, false },

            // :L324 - of_Off("clicked.^myns") cancels that event OUTSIDE that namespace.
            { "clicked.^myns", "clicked", "myns", false, true },

            // :L327 - of_Off("^clicked.^myns") cancels everything except that event and outside that
            // namespace. Both parts negated, which proves the two tests are independent.
            { "^clicked.^myns", "clicked", "myns", true, true },

            // :L315 - of_Off(".^myns") cancels everything outside that namespace. An empty name means
            // "match every name", so a negated namespace can stand alone.
            { ".^myns", "", "myns", false, true },

            // :L312 - of_Off(".myns") cancels everything inside that namespace.
            { ".myns", "", "myns", false, false }
        };

    /// <summary>
    /// A leading negation is read independently on the name and on the namespace, is stripped from
    /// whichever part carried it, and the filter still re-emits byte for byte.
    /// </summary>
    [Theory]
    [MemberData(nameof(FilterNegationRows))]
    public void NegationIsReadIndependentlyOnEachPartOfAFilter(
        string filter,
        string expectedName,
        string? expectedNamespaceCriterion,
        bool expectedNegateName,
        bool expectedNegateNamespace)
    {
        SubscriptionTopic parsed = ParseFilter(filter);

        // :L1010 and :L1014 - the symbol is stripped from whichever part carried it, so neither the
        // stored name nor the stored namespace retains it.
        Assert.Equal(expectedName, parsed.LegacyName, StringComparer.Ordinal);
        Assert.Equal(expectedNamespaceCriterion, parsed.NamespaceCriterion, StringComparer.Ordinal);

        // :L1009 and :L1013 - the two flags, set independently.
        Assert.Equal(expectedNegateName, parsed.NegateName);
        Assert.Equal(expectedNegateNamespace, parsed.NegateNamespace);

        Assert.Equal(TopicGrammar.Filter, parsed.Grammar);

        // A filter never expresses a subscribe option, because _of_modify parses none of them: in filter
        // position '-', '%', '*', '@' and '!' are ordinary characters.
        Assert.Equal(CaptureMode.Unhandled, parsed.Capture);
        Assert.Equal(Priorities.Normal, parsed.Priority);
        Assert.False(parsed.Prepend);

        // :L1017 with :L1030 - an empty name criterion matches every name rather than matching nothing.
        Assert.Equal(expectedName.Length == 0, parsed.MatchesEveryName);

        AssertRoundTripsExactly(filter, parsed);
    }

    /// <summary>
    /// In SUBSCRIBE position the negation symbol is an ordinary character of the event name: it is not a
    /// negation, and it does not even end up stripped.
    /// </summary>
    public static TheoryData<string, string, string?> NegationIsInertWhenSubscribingRows =>
        new()
        {
            // of_on's `choose case` (:L341-L359) has no arm for '^', so the character falls to
            // `case else` and EXITS the run at position one. The '^' is then part of the name.
            { "^clicked", "^clicked", null },

            // The same, with a namespace: the name keeps its '^' and the namespace is read normally.
            { "^clicked.myns", "^clicked", "myns" },

            // A '^' on the namespace side is equally inert when subscribing - it is simply the first
            // character of the namespace's name.
            { "clicked.^myns", "clicked", "^myns" }
        };

    /// <summary>
    /// The negation symbol carries no meaning in subscribe position, which is the load-bearing contrast
    /// with the filter grammar.
    /// </summary>
    /// <remarks>
    /// Pinned as correct under AAP 0.7.3 C-B. The same eight symbols mean different things in the two
    /// positions, and the temptation to unify the two scanners is exactly what this pair of theories
    /// exists to defeat: unify them and <c>of_Off("^clicked")</c> either stops negating or
    /// <c>of_On("^clicked")</c> starts to, and each breaks a different production call site.
    /// </remarks>
    [Theory]
    [MemberData(nameof(NegationIsInertWhenSubscribingRows))]
    public void TheNegationSymbolIsAnOrdinaryCharacterWhenSubscribing(
        string topic,
        string expectedName,
        string? expectedNamespaceCriterion)
    {
        SubscriptionTopic parsed = ParseSubscribe(topic);

        Assert.Equal(expectedName, parsed.LegacyName, StringComparer.Ordinal);
        Assert.Equal(expectedNamespaceCriterion, parsed.NamespaceCriterion, StringComparer.Ordinal);

        // The whole point: no negation was recognised, in either position.
        Assert.False(parsed.NegateName);
        Assert.False(parsed.NegateNamespace);

        AssertRoundTripsExactly(topic, parsed);
    }

    /// <summary>
    /// The identical string means different things in the two grammars, and the two parses differ.
    /// </summary>
    /// <remarks>
    /// A direct comparison rather than two separate assertions, so the asymmetry is recorded as a single
    /// fact about the language: <c>"^clicked"</c> is the name <c>"^clicked"</c> when subscribing
    /// (<c>:L357-L358</c>) and the negated name <c>"clicked"</c> when filtering (<c>:L1008-L1011</c>).
    /// </remarks>
    [Fact]
    public void TheSameStringDecodesDifferentlyUnderTheTwoGrammars()
    {
        SubscriptionTopic asSubscription = ParseSubscribe("^clicked");
        SubscriptionTopic asFilter = ParseFilter("^clicked");

        Assert.Equal("^clicked", asSubscription.LegacyName, StringComparer.Ordinal);
        Assert.False(asSubscription.NegateName);

        Assert.Equal("clicked", asFilter.LegacyName, StringComparer.Ordinal);
        Assert.True(asFilter.NegateName);

        Assert.NotEqual(asSubscription, asFilter);

        // Both nevertheless re-emit the identical original bytes, because retention is grammar-agnostic.
        AssertRoundTripsExactly("^clicked", asSubscription);
        AssertRoundTripsExactly("^clicked", asFilter);
    }

    // =================================================================================================
    //  COMBINATIONS - THE ORACLE'S OWN WORKED CORPUS, ROUND-TRIPPED
    //  w_test_eventful.srw:L220-L230 is the oracle's own list of legal spellings, and :L232 states the
    //  combination rules: '!'/'@' cannot combine with a numeric priority, '*' cannot combine with '%',
    //  and EVERY OTHER COMBINATION IS LEGAL, with symbol order arbitrary but the run leading.
    // =================================================================================================

    /// <summary>
    /// Every subscribe spelling the oracle itself writes down, plus the two evidenced production call
    /// sites, as one round-trip corpus.
    /// </summary>
    /// <remarks>
    /// Held as its own corpus rather than folded into the per-symbol tables because its purpose is
    /// different: the per-symbol tables assert what each symbol MEANS, and this asserts that the exact
    /// strings the legacy is known to pass survive a parse and re-emission unchanged. If the grammar ever
    /// stops accepting one of these, a real call site has broken.
    /// </remarks>
    public static TheoryData<string> OracleWorkedSubscribeCorpus =>
        new()
        {
            "clicked",        // w_test_eventful.srw:L220 - default priority
            "!clicked",       // :L221 - high priority
            "@clicked",       // :L222 - low priority
            "-clicked",       // :L223 - prepend within the default priority
            "-!clicked",      // :L224 - prepend within the HIGH priority
            "-@clicked",      // :L225 - prepend within the LOW priority
            "9999:clicked",   // :L226 - custom numeric priority
            "clicked.myns",   // :L227 - namespaced
            "close.myns",     // :L228 - namespaced, the of_Off(".myns") worked example
            "*click",         // :L229 - capture every handled-state
            "%click",         // :L230 - capture already-handled events
            "*clicked.myns",  // :L251 - AS ACTUALLY CALLED: capture mode combined with a namespace
            "test.myns",      // :L252 - as actually called
            "0-itemchanged",  // se_cst_dw.sru:L54 - a live DataWindow topic
            "1-editchanged",  // se_cst_dw.sru:L57 - a live DataWindow topic

            // n_cst_dwsvc_columnexp.sru:L2429 - of_On("!" + EVT_ITEMCHANGED,this,"onItemChanged").
            // TRACED PRECISELY: that statement sits inside a COMMENTED-OUT block spanning :L2428-:L2432,
            // so it is a DORMANT call site rather than a live subscription - the only occurrence of an
            // of_On with a '!' prefix outside the oracle's own documentation. It is included anyway, and
            // on its own merits rather than as evidence of a live caller: it is the one spelling that
            // combines a leading priority symbol WITH a sequence-prefixed name, and so it proves the
            // symbol run still ends at the digit (:L357-L358) even when a real symbol preceded it.
            "!0-itemchanged"
        };

    /// <summary>
    /// Every spelling in the oracle's worked corpus parses, and re-emits byte for byte.
    /// </summary>
    [Theory]
    [MemberData(nameof(OracleWorkedSubscribeCorpus))]
    public void EveryOracleWorkedSpellingParsesAndRoundTripsExactly(string topic)
    {
        SubscriptionTopic parsed = ParseSubscribe(topic);

        AssertRoundTripsExactly(topic, parsed);

        // A parsed subscription always has a usable name (:L381 guarantees it) and is always tagged with
        // the grammar that produced it.
        Assert.NotEmpty(parsed.LegacyName);
        Assert.Equal(TopicGrammar.Subscription, parsed.Grammar);
        Assert.False(parsed.MatchesEveryName);
    }

    /// <summary>
    /// The oracle's two prepend-plus-priority combinations, and the capture-plus-namespace combination it
    /// actually calls.
    /// </summary>
    public static TheoryData<string, string, bool, CaptureMode, int, string?> CombinedSymbolRows =>
        new()
        {
            // w_test_eventful.srw:L224 - of_On("-!clicked",...) "插入到高优先级头部" (insert at the head of
            // the HIGH priority queue). Two symbols in one run, each claiming a different slot.
            { "-!clicked", "clicked", true, CaptureMode.Unhandled, Priorities.High, null },

            // :L225 - of_On("-@clicked",...) "插入到低优先级头部" (head of the LOW priority queue).
            { "-@clicked", "clicked", true, CaptureMode.Unhandled, Priorities.Low, null },

            // :L251 - the exact spelling the oracle subscribes with: capture mode AND a namespace, which
            // exercises the symbol run and the namespace split in one string.
            { "*clicked.myns", "clicked", false, CaptureMode.All, Priorities.Normal, "myns" },

            // Prepend with a numeric priority: legal, since the numeric prefix conflicts only with '!'
            // and '@' (the guard at :L369 tests the PRIORITY field, which '-' never touches).
            { "-9999:clicked", "clicked", true, CaptureMode.Unhandled, 9999, null },

            // Three symbols at once, all three slots claimed independently, plus a namespace. Legal per
            // :L232's "其他随意组合" (every other combination is free).
            { "-%9999:clicked.myns", "clicked", true, CaptureMode.Handled, 9999, "myns" },

            // THE SHARPEST INTERACTION IN THE GRAMMAR: a priority symbol immediately followed by a
            // SEQUENCE-PREFIXED name. The run consumes the '!' at :L354-L356, then reads '0', matches no
            // symbol case and EXITS at :L357-L358 - so the digits and their hyphen survive into the name
            // even though a real symbol preceded them, and Prepend stays false. Provenance is
            // n_cst_dwsvc_columnexp.sru:L2429, noted as dormant in the corpus above.
            { "!0-itemchanged", "0-itemchanged", false, CaptureMode.Unhandled, Priorities.High, null }
        };

    /// <summary>
    /// Independent symbols combine freely, each claiming its own slot, and the whole spelling round-trips.
    /// </summary>
    [Theory]
    [MemberData(nameof(CombinedSymbolRows))]
    public void IndependentSymbolsCombineFreely(
        string topic,
        string expectedName,
        bool expectedPrepend,
        CaptureMode expectedCapture,
        int expectedPriority,
        string? expectedNamespaceCriterion)
    {
        SubscriptionTopic parsed = ParseSubscribe(topic);

        Assert.Equal(expectedName, parsed.LegacyName, StringComparer.Ordinal);
        Assert.Equal(expectedPrepend, parsed.Prepend);
        Assert.Equal(expectedCapture, parsed.Capture);
        Assert.Equal(expectedPriority, parsed.Priority);
        Assert.Equal(expectedNamespaceCriterion, parsed.NamespaceCriterion, StringComparer.Ordinal);

        AssertRoundTripsExactly(topic, parsed);
    }

    // =================================================================================================
    //  SYMBOL ORDER WITHIN THE RUN IS FREE
    //  w_test_eventful.srw:L232 - "符号的顺序可以任意但必须在最开头": the order of the symbols may be
    //  arbitrary but they must be at the very beginning. The loop at :L339-L360 is a `choose case` inside
    //  a positional scan, so it is genuinely order-independent; only leaving the run ends it.
    // =================================================================================================

    /// <summary>
    /// Pairs of spellings that differ only in the ORDER of their leading symbols.
    /// </summary>
    public static TheoryData<string, string> ReorderedSymbolRunRows =>
        new()
        {
            // The oracle's own "-!clicked" (:L224) against its reversal.
            { "-!clicked", "!-clicked" },

            // The oracle's own "-@clicked" (:L225) against its reversal.
            { "-@clicked", "@-clicked" },

            // Prepend and a capture mode, in both orders.
            { "-*clicked", "*-clicked" },

            // Three symbols, first and last transposed.
            { "-%!clicked", "!%-clicked" }
        };

    /// <summary>
    /// Two spellings that differ only in symbol order decode to the SAME topic, while each still re-emits
    /// its own original bytes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The distinction asserted here is deliberate and is the reason this test exists separately from the
    /// round-trip corpus. <b>Parse equivalence</b> is asserted with
    /// <see cref="SubscriptionTopic.Equals(SubscriptionTopic)"/>, which compares the decoded fields and
    /// deliberately EXCLUDES <see cref="SubscriptionTopic.RawValue"/> - so the two spellings are equal as
    /// topics. <b>String equality is NOT asserted between them</b>, because
    /// <see cref="SubscriptionTopic.ToLegacyString"/> achieves byte-exactness by retaining what it was
    /// given: it cannot know, and must not guess, which of two equally legal orders its caller wrote.
    /// </para>
    /// <para>
    /// So each spelling round-trips to ITSELF, and neither round-trips to the other. A canonical generator
    /// would have to pick one order and would thereby break the round trip for the other, which is why
    /// retention is the correct design and why this test pins both halves of it.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ReorderedSymbolRunRows))]
    public void SymbolOrderWithinTheLeadingRunIsFree(string canonicalSpelling, string reorderedSpelling)
    {
        SubscriptionTopic canonical = ParseSubscribe(canonicalSpelling);
        SubscriptionTopic reordered = ParseSubscribe(reorderedSpelling);

        // PARSE EQUIVALENCE: every decoded field agrees, so the two are the same topic.
        Assert.Equal(canonical, reordered);
        Assert.Equal(canonical.GetHashCode(), reordered.GetHashCode());

        // Field by field as well, so a failure names WHICH field diverged rather than only that one did.
        Assert.Equal(canonical.LegacyName, reordered.LegacyName, StringComparer.Ordinal);
        Assert.Equal(canonical.Prepend, reordered.Prepend);
        Assert.Equal(canonical.Capture, reordered.Capture);
        Assert.Equal(canonical.Priority, reordered.Priority);

        // BYTE-EXACTNESS is per spelling, NOT between spellings: each re-emits what it was given.
        AssertRoundTripsExactly(canonicalSpelling, canonical);
        AssertRoundTripsExactly(reorderedSpelling, reordered);

        // And therefore the two renderings are NOT the same string, even though the topics are equal.
        Assert.NotEqual(canonical.ToLegacyString(), reordered.ToLegacyString(), StringComparer.Ordinal);
    }

    // =================================================================================================
    //  THE RESERVED LIFETIME NAMESPACE - ".^persistent"
    //  ws_objects/pfw.thread.pbl.src/n_cst_threading.sru:L544 passes ".^persistent" to cancel every
    //  subscription EXCEPT those in the persistent namespace, and :L593-L597 is the caller-side rule that
    //  builds it: if the requested name already contains a separator use it as-is (:L594), otherwise
    //  append ".^persistent" (:L596). Read the filter carefully - it SPARES the persistent namespace.
    // =================================================================================================

    /// <summary>
    /// The requested name, the filter the caller-side rule builds from it, and the decoded namespace.
    /// </summary>
    public static TheoryData<string, string, string, bool> PersistentSparingFilterRows =>
        new()
        {
            // n_cst_threading.sru:L544 - of_off() with no name at all builds the bare ".^persistent".
            // The name is empty, so the filter matches EVERY name whose namespace is not "persistent".
            { "", ".^persistent", "", true },

            // :L596 - of_off(name) with a separator-free name appends the same suffix.
            { "clicked", "clicked.^persistent", "clicked", true },

            // The same rule applied to a live DataWindow topic, prefix and all - the sequence prefix is
            // not a separator, so the suffix is appended.
            { "0-itemchanged", "0-itemchanged.^persistent", "0-itemchanged", true },

            // :L593-L594 - a name that ALREADY contains a separator is used untouched, so no negation is
            // introduced and the namespace is matched positively rather than excluded.
            { "clicked.myns", "clicked.myns", "clicked", false }
        };

    /// <summary>
    /// The caller-side bulk-unsubscribe rule builds the reserved-lifetime filter, and that filter decodes
    /// to "every name WHERE the namespace is NOT persistent" - it SPARES persistent subscriptions.
    /// </summary>
    [Theory]
    [MemberData(nameof(PersistentSparingFilterRows))]
    public void TheReservedLifetimeFilterSparesThePersistentNamespace(
        string requestedName,
        string expectedFilter,
        string expectedDecodedName,
        bool expectedNegateNamespace)
    {
        // The port of n_cst_threading.sru:L593-L597, asserted at its real construction site rather than
        // as a bare literal, so the rule and the grammar are pinned together.
        string builtFilter = EventBroker.BuildPersistentSparingFilter(requestedName);
        Assert.Equal(expectedFilter, builtFilter, StringComparer.Ordinal);

        SubscriptionTopic parsed = ParseFilter(builtFilter);

        Assert.Equal(expectedDecodedName, parsed.LegacyName, StringComparer.Ordinal);
        Assert.Equal(expectedNegateNamespace, parsed.NegateNamespace);

        // A namespace criterion is always present, because the rule guarantees a separator either way.
        Assert.True(parsed.HasNamespaceCriterion);

        AssertRoundTripsExactly(builtFilter, parsed);
    }

    /// <summary>
    /// The negation in the reserved-lifetime filter is what spares persistent subscriptions, and dropping
    /// it would delete exactly the subscriptions registered to survive.
    /// </summary>
    /// <remarks>
    /// Asserted through <see cref="SubscriptionTopic.Matches"/> rather than by reading the flags, because
    /// the flags are only half the contract: the meaning is in how they are APPLIED
    /// (<c>n_cst_eventful.sru:L1038-L1043</c>). A transient subscription matches and is therefore
    /// unsubscribed; a persistent one does not match and therefore survives. Nothing about that failure
    /// mode is loud - a broken version still returns success and still throws nothing - so it is pinned
    /// here as a behavioural statement.
    /// </remarks>
    [Fact]
    public void TheReservedLifetimeFilterMatchesTransientSubscriptionsAndSparesPersistentOnes()
    {
        SubscriptionTopic parsed = ParseFilter(EventBroker.BuildPersistentSparingFilter(string.Empty));

        // Every name is in scope (:L1017 with :L1030), so the namespace alone decides.
        Assert.True(parsed.MatchesEveryName);

        // A transient subscription - no namespace at all - MATCHES, so of_off() removes it.
        Assert.True(parsed.Matches("clicked", SubscriptionNamespaces.None));

        // A subscription in some other namespace also matches: the filter excludes only "persistent".
        Assert.True(parsed.Matches("clicked", "myns"));

        // A PERSISTENT subscription does NOT match, so it survives the bulk unsubscribe. This is the
        // assertion that a lifetime-flag-only reimplementation would invert.
        Assert.False(parsed.Matches("clicked", SubscriptionNamespaces.Persistent));

        // And the namespace the filter carries genuinely is the reserved one, projected onto the lifetime.
        Assert.Equal(SubscriptionNamespaces.Persistent, parsed.Namespace, StringComparer.Ordinal);
        Assert.Equal(SubscriptionLifetime.Persistent, parsed.Lifetime);
    }

    // =================================================================================================
    //  THE THREE-STATE NAMESPACE CRITERION - ABSENT versus PRESENT-BUT-EMPTY versus PRESENT
    //  n_cst_eventful.sru:L1000-L1006. The `else bNoNamespace = true` arm is the whole point: the ABSENCE
    //  of a separator is a THIRD state, distinct from a separator with nothing after it. :L1038 then
    //  honours it by skipping the namespace test entirely. Two booleans cannot express this, which is why
    //  NamespaceCriterion is a nullable string, and the four filter forms below are the oracle's own proof
    //  that all three states are reachable and meaningful.
    // =================================================================================================

    /// <summary>
    /// The oracle's four namespace-presence filter forms, with the decoded three-state criterion and the
    /// documented meaning of each.
    /// </summary>
    public static TheoryData<string, string, string?, bool, bool> NamespaceCriterionStateRows =>
        new()
        {
            // w_test_eventful.srw:L306 - of_Off("clicked") cancels every subscription of that event
            // REGARDLESS of namespace. No separator, so the criterion is ABSENT (null) and the namespace
            // test is skipped altogether (:L1004-L1005 with :L1038).
            { "clicked", "clicked", null, false, false },

            // :L330 - of_Off("clicked.") cancels that event's subscriptions that have NO namespace.
            // Separator present, nothing after it, so the criterion is PRESENT AND EMPTY and is compared
            // positively against the empty namespace (:L1002 with :L1042).
            { "clicked.", "clicked", "", true, false },

            // :L333 - of_Off("clicked.^") cancels that event's subscriptions that DO have a namespace.
            // The same present-and-empty criterion, NEGATED (:L1012-L1015) - so "namespace is not empty".
            { "clicked.^", "clicked", "", true, true },

            // :L318 - of_Off("clicked.myns") cancels that event inside that namespace. PRESENT AND
            // NON-EMPTY, the ordinary case, included so the table spans all three states.
            { "clicked.myns", "clicked", "myns", true, false }
        };

    /// <summary>
    /// The namespace criterion is genuinely three-state: absent, present-but-empty, and present.
    /// </summary>
    [Theory]
    [MemberData(nameof(NamespaceCriterionStateRows))]
    public void TheNamespaceCriterionIsThreeStated(
        string filter,
        string expectedName,
        string? expectedCriterion,
        bool expectedHasCriterion,
        bool expectedNegateNamespace)
    {
        SubscriptionTopic parsed = ParseFilter(filter);

        Assert.Equal(expectedName, parsed.LegacyName, StringComparer.Ordinal);
        Assert.Equal(expectedCriterion, parsed.NamespaceCriterion, StringComparer.Ordinal);
        Assert.Equal(expectedHasCriterion, parsed.HasNamespaceCriterion);
        Assert.Equal(expectedNegateNamespace, parsed.NegateNamespace);

        // Namespace flattens the first two states onto the empty string, which is exactly why it cannot be
        // used to tell them apart and HasNamespaceCriterion exists.
        Assert.Equal(expectedCriterion ?? SubscriptionNamespaces.None, parsed.Namespace, StringComparer.Ordinal);

        AssertRoundTripsExactly(filter, parsed);
    }

    /// <summary>
    /// The pair that a two-boolean model would collapse: an absent criterion, a present-but-empty one and
    /// a negated-empty one are three DIFFERENT filters that select three different sets.
    /// </summary>
    /// <remarks>
    /// The sharpest test in this suite of the three-state model, asserted behaviourally through
    /// <see cref="SubscriptionTopic.Matches"/> so that it pins the MEANING rather than the field values.
    /// Each of the three answers the question "does this filter select a subscription that has no
    /// namespace, and one that has one?" differently, and the three answers are the oracle's documented
    /// meanings at <c>w_test_eventful.srw:L306</c>, <c>:L330</c> and <c>:L333</c>.
    /// </remarks>
    [Fact]
    public void AbsentAndPresentButEmptyAndNegatedEmptyAreThreeDifferentFilters()
    {
        SubscriptionTopic absent = ParseFilter("clicked");
        SubscriptionTopic presentEmpty = ParseFilter("clicked.");
        SubscriptionTopic negatedEmpty = ParseFilter("clicked.^");

        // All three are distinct topics, and none is equal to another.
        Assert.NotEqual(absent, presentEmpty);
        Assert.NotEqual(presentEmpty, negatedEmpty);
        Assert.NotEqual(absent, negatedEmpty);

        // :L306 - ABSENT: namespace is not a criterion, so BOTH are selected.
        Assert.True(absent.Matches("clicked", SubscriptionNamespaces.None));
        Assert.True(absent.Matches("clicked", "myns"));

        // :L330 - PRESENT AND EMPTY: only the subscription with NO namespace is selected.
        Assert.True(presentEmpty.Matches("clicked", SubscriptionNamespaces.None));
        Assert.False(presentEmpty.Matches("clicked", "myns"));

        // :L333 - NEGATED EMPTY: only the subscription that HAS a namespace is selected. The exact
        // complement of the previous case, which is what makes the pair a complete partition.
        Assert.False(negatedEmpty.Matches("clicked", SubscriptionNamespaces.None));
        Assert.True(negatedEmpty.Matches("clicked", "myns"));

        // Each still re-emits its own bytes, so the distinction survives a round trip rather than being
        // an artefact of the in-memory model.
        AssertRoundTripsExactly("clicked", absent);
        AssertRoundTripsExactly("clicked.", presentEmpty);
        AssertRoundTripsExactly("clicked.^", negatedEmpty);
    }

    /// <summary>
    /// The unqualified forms of the same pair: a bare separator, and a bare negated separator.
    /// </summary>
    /// <remarks>
    /// <c>w_test_eventful.srw:L336</c> - <c>of_Off(".")</c> cancels every subscription that has no
    /// namespace; <c>:L339</c> - <c>of_Off(".^")</c> cancels every subscription that has one. Both have an
    /// EMPTY name, which under the filter grammar means "match every name" (<c>:L1017</c> with
    /// <c>:L1030</c>) rather than "match nothing" - and note that an empty name is precisely what
    /// <see cref="SubscriptionTopic.ParseSubscription"/> rejects at <c>:L381</c>. The same emptiness is a
    /// feature in one grammar and an error in the other, and that asymmetry is preserved, not harmonised
    /// (AAP 0.7.3 C-B).
    /// </remarks>
    [Fact]
    public void TheUnqualifiedNamespacePresenceFiltersMatchEveryName()
    {
        SubscriptionTopic namespaceless = ParseFilter(".");
        SubscriptionTopic namespaced = ParseFilter(".^");

        // :L1017 - an empty name criterion, in both cases.
        Assert.True(namespaceless.MatchesEveryName);
        Assert.True(namespaced.MatchesEveryName);
        Assert.Empty(namespaceless.LegacyName);
        Assert.Empty(namespaced.LegacyName);

        // Both carry a present-but-empty criterion; only the second negates it.
        Assert.True(namespaceless.HasNamespaceCriterion);
        Assert.True(namespaced.HasNamespaceCriterion);
        Assert.False(namespaceless.NegateNamespace);
        Assert.True(namespaced.NegateNamespace);

        // :L336 - "." selects every subscription without a namespace, whatever its name.
        Assert.True(namespaceless.Matches("clicked", SubscriptionNamespaces.None));
        Assert.True(namespaceless.Matches("anything-at-all", SubscriptionNamespaces.None));
        Assert.False(namespaceless.Matches("clicked", "myns"));

        // :L339 - ".^" selects every subscription WITH a namespace, whatever its name.
        Assert.False(namespaced.Matches("clicked", SubscriptionNamespaces.None));
        Assert.True(namespaced.Matches("clicked", "myns"));
        Assert.True(namespaced.Matches("anything-at-all", SubscriptionNamespaces.Persistent));

        Assert.NotEqual(namespaceless, namespaced);

        AssertRoundTripsExactly(".", namespaceless);
        AssertRoundTripsExactly(".^", namespaced);
    }

    /// <summary>
    /// The oracle's complete thirteen-form filter matrix, as one round-trip corpus.
    /// </summary>
    /// <remarks>
    /// <c>w_test_eventful.srw:L306-L339</c> under the doc block at <c>:L297-L303</c>. Every form the
    /// oracle writes down is here, in its documented order, so that the matrix is pinned as a whole rather
    /// than only in the slices the theories above examine. The fourteenth form the oracle lists,
    /// <c>of_Off()</c> with no argument at all (<c>:L342</c>), is the null/empty filter and is covered by
    /// the empty-filter row below.
    /// </remarks>
    public static TheoryData<string> OracleFilterMatrixCorpus =>
        new()
        {
            "clicked",          // :L306 - every subscription of that event
            "^clicked",         // :L309 - everything EXCEPT that event
            ".myns",            // :L312 - everything in that namespace
            ".^myns",           // :L315 - everything outside that namespace
            "clicked.myns",     // :L318 - that event in that namespace
            "^clicked.myns",    // :L321 - everything in that namespace except that event
            "clicked.^myns",    // :L324 - that event outside that namespace
            "^clicked.^myns",   // :L327 - everything except that event, outside that namespace
            "clicked.",         // :L330 - that event with NO namespace
            "clicked.^",        // :L333 - that event WITH a namespace
            ".",                // :L336 - everything with no namespace
            ".^",               // :L339 - everything with a namespace
            "",                 // :L342 - of_Off() with no argument: the match-everything filter
            ".^persistent"      // n_cst_threading.sru:L544 - the reserved-lifetime sweep
        };

    /// <summary>
    /// Every form in the oracle's filter matrix parses and re-emits byte for byte.
    /// </summary>
    [Theory]
    [MemberData(nameof(OracleFilterMatrixCorpus))]
    public void EveryOracleFilterFormParsesAndRoundTripsExactly(string filter)
    {
        SubscriptionTopic parsed = ParseFilter(filter);

        AssertRoundTripsExactly(filter, parsed);

        Assert.Equal(TopicGrammar.Filter, parsed.Grammar);

        // A filter never carries a subscribe option, whatever it is spelled with: _of_modify parses none.
        Assert.Equal(CaptureMode.Unhandled, parsed.Capture);
        Assert.Equal(Priorities.Normal, parsed.Priority);
        Assert.False(parsed.Prepend);

        // The negation symbol is never left inside either decoded part (:L1010, :L1014).
        Assert.DoesNotContain(TopicSymbols.Negation, parsed.LegacyName);
        Assert.DoesNotContain(TopicSymbols.Negation, parsed.Namespace);
    }

    // =================================================================================================
    //  DECOMPOSITION AND RECONSTITUTION - THE WIRE TRIPLE
    //
    //  WHY THE TRIPLE EXISTS AT ALL (AAP 0.7.3 C-K - the boundary decision, named)
    //  The legacy topic string FUSES THREE INDEPENDENT ENCODINGS into one opaque value: a lexical
    //  ORDERING prefix, the LOGICAL IDENTITY of the event, and the LIFETIME carried by its namespace.
    //  AAP 0.6.1.2 states the consequence precisely - "transmit the string and the ordering becomes
    //  invisible; parse the string at the far end and the contract has an undocumented grammar." So the
    //  cross-service contract carries Sequence, LogicalName and Lifetime as THREE SEPARATE FIELDS, and the
    //  fused legacy form is reconstituted ONLY at the compatibility edge - by ToLegacyString(), for the
    //  three cases that genuinely need it: writing a characterization recording, emitting a log line that
    //  will be diffed against the oracle's, and calling something that still speaks the legacy grammar.
    //
    //  AND WHY DECOMPOSING MUST NOT DISTURB THE NAME (the single thing this suite exists to protect)
    //  se_cst_dw.sru:L54 declares EVT_ITEMCHANGED = "0-itemchanged". The ordering those DataWindow topics
    //  achieve comes ENTIRELY from the ordinal name sort - w_test_eventful.srw:L238 records that
    //  subscriptions are ordered by name ascending - so the digits are a hand-rolled sequence number
    //  SPELLED INTO THE NAME. The decomposition is therefore a PROJECTION over LegacyName, never a
    //  replacement for it. A port that "tidied" the prefix away and then dispatched or sorted on the tidy
    //  name would shift every ordering guarantee in the DataWindow event chain, and would do so silently:
    //  the subscriptions still register, nothing throws, and no other suite in this folder would notice.
    // =================================================================================================

    /// <summary>
    /// Fuses a wire triple back into a legacy topic, which is what a compatibility edge does when it has
    /// received the decomposed fields and must speak to something that still expects the fused spelling.
    /// </summary>
    /// <remarks>
    /// <see cref="SubscriptionTopic.Sequence"/> and <see cref="SubscriptionTopic.LogicalName"/> are
    /// computed from <see cref="SubscriptionTopic.LegacyName"/> rather than stored beside it - which is
    /// what makes the reconstitution invariant unbreakable - so a topic is rebuilt from a triple by fusing
    /// the name, exactly as the legacy's authors did by hand when they typed <c>"0-itemchanged"</c>.
    /// <see cref="CultureInfo.InvariantCulture"/> is explicit: the digits of a sequence number must not
    /// depend on the ambient locale.
    /// </remarks>
    private static SubscriptionTopic BuildFromWireTriple(
        int? sequence,
        string logicalName,
        SubscriptionLifetime lifetime)
    {
        string fusedName = sequence is null
            ? logicalName
            : string.Concat(
                sequence.Value.ToString(CultureInfo.InvariantCulture),
                TopicSymbols.Prepend.ToString(),
                logicalName);

        return new SubscriptionTopic
        {
            Grammar = TopicGrammar.Subscription,
            LegacyName = fusedName,
            NamespaceCriterion = lifetime == SubscriptionLifetime.Persistent
                ? SubscriptionNamespaces.Persistent
                : null
        };
    }

    /// <summary>
    /// The two live DataWindow topics and the sequence-recognition boundary cases around them: the topic,
    /// the sequence it projects, and the logical name it projects.
    /// </summary>
    public static TheoryData<string, int?, string> WireTripleDecompositionRows =>
        new()
        {
            // se_cst_dw.sru:L54 - constant string EVT_ITEMCHANGED = "0-itemchanged". THE case this whole
            // suite exists for.
            { "0-itemchanged", 0, "itemchanged" },

            // se_cst_dw.sru:L57 - constant string EVT_EDITCHANGED = "1-editchanged".
            { "1-editchanged", 1, "editchanged" },

            // No prefix at all: the sequence is ABSENT, not zero. See the dedicated theory below for why
            // that distinction is load bearing.
            { "clicked", null, "clicked" },

            // se_cst_dw.sru:L47 - a real un-prefixed sibling of the two above, so the table contains a
            // production topic on both sides of the distinction.
            { "rowfocuschanging", null, "rowfocuschanging" },

            // A multi-digit sequence: the digit RUN is read, not just one character.
            { "10-itemchanged", 10, "itemchanged" },

            // A bare numeric name is NOT a sequence: the prefix requires the '-' that follows the digits,
            // and there is none here.
            { "0", null, "0" },

            // A redundant leading zero is NOT a sequence either, and this is the guard that keeps the
            // reconstitution invariant true: $"{0}-x" is "0-x", so treating "00-x" as sequence 0 would
            // re-emit a DIFFERENT string and silently rename the event.
            { "00-x", null, "00-x" },

            // A '-' with no digits before it cannot reach this projection anyway - the leading run would
            // have consumed it as prepend - but the projection independently declines it.
            { "-x-y", null, "-x-y" }
        };

    /// <summary>
    /// A topic decomposes into the wire triple without disturbing the name it was spelled into: the
    /// sequence and logical name are projections, and <c>LegacyName</c> keeps the prefix.
    /// </summary>
    [Theory]
    [MemberData(nameof(WireTripleDecompositionRows))]
    public void ATopicDecomposesIntoTheWireTripleWithoutDisturbingItsName(
        string legacyName,
        int? expectedSequence,
        string expectedLogicalName)
    {
        // Built rather than parsed for the two rows a parser cannot produce ("-x-y" would be consumed as
        // prepend), so the projection is asserted on the field itself.
        SubscriptionTopic built = new() { LegacyName = legacyName };

        Assert.Equal(expectedSequence, built.Sequence);
        Assert.Equal(expectedLogicalName, built.LogicalName, StringComparer.Ordinal);

        // THE GUARANTEE. The name is untouched by the projection, prefix and all.
        Assert.Equal(legacyName, built.LegacyName, StringComparer.Ordinal);

        // THE RECONSTITUTION INVARIANT, asserted rather than assumed:
        //   LegacyName == (Sequence is null ? LogicalName : $"{Sequence}-{LogicalName}")
        string refused = built.Sequence is null
            ? built.LogicalName
            : string.Concat(
                built.Sequence.Value.ToString(CultureInfo.InvariantCulture),
                TopicSymbols.Prepend.ToString(),
                built.LogicalName);

        Assert.Equal(legacyName, refused, StringComparer.Ordinal);
    }

    /// <summary>
    /// The two live DataWindow topics, parsed as the framework parses them: the prefix survives, and the
    /// hyphen in it is NOT the prepend symbol.
    /// </summary>
    public static TheoryData<string, int, string> DataWindowTopicRows =>
        new()
        {
            { "0-itemchanged", 0, "itemchanged" },   // se_cst_dw.sru:L54
            { "1-editchanged", 1, "editchanged" }    // se_cst_dw.sru:L57
        };

    /// <summary>
    /// A live DataWindow topic keeps its ordering prefix through a real parse, and its hyphen is an
    /// ordinary character of the event name rather than a prepend request.
    /// </summary>
    /// <remarks>
    /// The trap, traced: the character loop reads position one, finds a digit, matches no symbol case,
    /// falls to <c>case else</c> and EXITS at position one (<c>n_cst_eventful.sru:L357-L358</c>), so
    /// <c>Mid(name, 1)</c> at <c>:L363</c> returns the whole string. Pinned as correct under AAP 0.7.3
    /// C-B: a parser that recognised <c>-</c> at an arbitrary position would corrupt both live DataWindow
    /// topics and silently detach every subscriber.
    /// </remarks>
    [Theory]
    [MemberData(nameof(DataWindowTopicRows))]
    public void ALiveDataWindowTopicKeepsItsOrderingPrefixAndIsNotPrepended(
        string topic,
        int expectedSequence,
        string expectedLogicalName)
    {
        SubscriptionTopic parsed = ParseSubscribe(topic);

        // The name is stored whole, prefix included.
        Assert.Equal(topic, parsed.LegacyName, StringComparer.Ordinal);

        // The hyphen inside the prefix is NOT the prepend symbol - this is the assertion that protects the
        // DataWindow event chain.
        Assert.False(parsed.Prepend);

        // Nothing else was consumed either: the run ended before it began.
        Assert.Equal(CaptureMode.Unhandled, parsed.Capture);
        Assert.Equal(Priorities.Normal, parsed.Priority);
        Assert.Null(parsed.NamespaceCriterion);

        // And the triple projects cleanly over the intact name.
        Assert.Equal(expectedSequence, parsed.Sequence);
        Assert.Equal(expectedLogicalName, parsed.LogicalName, StringComparer.Ordinal);

        AssertRoundTripsExactly(topic, parsed);
    }

    /// <summary>
    /// Triples and the fused legacy string each one must reconstitute to.
    /// </summary>
    public static TheoryData<int?, string, SubscriptionLifetime, string> WireTripleReconstitutionRows =>
        new()
        {
            // se_cst_dw.sru:L54 - the triple a contract would carry for EVT_ITEMCHANGED, refused.
            { 0, "itemchanged", SubscriptionLifetime.Transient, "0-itemchanged" },

            // se_cst_dw.sru:L57 - the same for EVT_EDITCHANGED.
            { 1, "editchanged", SubscriptionLifetime.Transient, "1-editchanged" },

            // The namespaced example: a persistent lifetime is refused as the reserved namespace suffix,
            // which is the third of the three fused encodings.
            { 0, "itemchanged", SubscriptionLifetime.Persistent, "0-itemchanged.persistent" },

            // An unordered topic: no sequence, so nothing is prefixed and no separator is emitted.
            { null, "clicked", SubscriptionLifetime.Transient, "clicked" },

            // An unordered topic with a lifetime, so the two projections are exercised independently.
            { null, "clicked", SubscriptionLifetime.Persistent, "clicked.persistent" },

            // A multi-digit sequence, so the fusion is not tested only at single digits.
            { 10, "itemchanged", SubscriptionLifetime.Transient, "10-itemchanged" }
        };

    /// <summary>
    /// A topic built from the decomposed wire triple reconstitutes to the exact fused legacy string, and
    /// that string parses back to the same topic.
    /// </summary>
    /// <remarks>
    /// This is the compatibility edge in both directions, and the round trip is closed here rather than
    /// merely started: the triple fuses to the legacy spelling, and re-parsing that spelling yields a topic
    /// equal to the built one. Equality excludes
    /// <see cref="SubscriptionTopic.RawValue"/> by design, which is what lets a BUILT topic compare equal
    /// to a PARSED one - the built topic has no retained string to re-emit and renders canonically instead.
    /// See the C-K note at the head of this region for why the triple exists at all.
    /// </remarks>
    [Theory]
    [MemberData(nameof(WireTripleReconstitutionRows))]
    public void AWireTripleReconstitutesToTheFusedLegacyString(
        int? sequence,
        string logicalName,
        SubscriptionLifetime lifetime,
        string expectedFusedTopic)
    {
        SubscriptionTopic built = BuildFromWireTriple(sequence, logicalName, lifetime);

        // The fusion itself: three separate wire fields become one opaque legacy value, and ONLY here.
        Assert.Equal(expectedFusedTopic, built.ToLegacyString(), StringComparer.Ordinal);

        // The triple survives the fusion, so the projection and the fusion agree.
        Assert.Equal(sequence, built.Sequence);
        Assert.Equal(logicalName, built.LogicalName, StringComparer.Ordinal);
        Assert.Equal(lifetime, built.Lifetime);

        // And the loop closes: the fused spelling parses back to the same topic.
        SubscriptionTopic reparsed = ParseSubscribe(expectedFusedTopic);
        Assert.Equal(built, reparsed);
        Assert.Equal(sequence, reparsed.Sequence);
        Assert.Equal(logicalName, reparsed.LogicalName, StringComparer.Ordinal);
        Assert.Equal(lifetime, reparsed.Lifetime);
        AssertRoundTripsExactly(expectedFusedTopic, reparsed);
    }

    /// <summary>
    /// Un-prefixed topics, every one of which must report an ABSENT sequence rather than a zero one.
    /// </summary>
    public static TheoryData<string> AbsentSequenceRows =>
        new()
        {
            "clicked",            // w_test_eventful.srw:L220
            "rowfocuschanging",   // se_cst_dw.sru:L47
            "rowfocuschanged",    // se_cst_dw.sru:L49
            "itemfocuschanged",   // se_cst_dw.sru:L52
            "doubleclicked",      // se_cst_dw.sru:L63
            "getfocus",           // se_cst_dw.sru:L74
            "losefocus"           // se_cst_dw.sru:L76
        };

    /// <summary>
    /// A topic with no ordering prefix reports an ABSENT sequence, never a defaulted zero.
    /// </summary>
    /// <remarks>
    /// The distinction is load bearing, not fastidious. Zero is a REAL sequence - it is the one
    /// <c>se_cst_dw.sru:L54</c> uses - so collapsing "no sequence" onto zero would make every unordered
    /// topic claim the first ordering slot and rank alongside <c>"0-itemchanged"</c>. A nullable sequence
    /// keeps "unordered" and "ordered first" distinguishable, which is exactly the information the fused
    /// legacy string loses and the wire triple is meant to recover.
    /// </remarks>
    [Theory]
    [MemberData(nameof(AbsentSequenceRows))]
    public void AnUnprefixedTopicHasAnAbsentSequenceRatherThanAZeroOne(string topic)
    {
        SubscriptionTopic parsed = ParseSubscribe(topic);

        Assert.Null(parsed.Sequence);

        // Explicitly NOT zero, stated as its own assertion because it is the mistake being guarded against.
        Assert.NotEqual(0, parsed.Sequence);

        // With no prefix to remove, the logical name IS the whole name.
        Assert.Equal(topic, parsed.LogicalName, StringComparer.Ordinal);
        Assert.Equal(topic, parsed.LegacyName, StringComparer.Ordinal);
    }

    /// <summary>
    /// The zero sequence and the absent sequence are different, and only one of them is zero.
    /// </summary>
    [Fact]
    public void AZeroSequenceAndAnAbsentSequenceAreDistinct()
    {
        SubscriptionTopic ordered = ParseSubscribe("0-itemchanged");
        SubscriptionTopic unordered = ParseSubscribe("itemchanged");

        // se_cst_dw.sru:L54 - zero is a real, used sequence value.
        Assert.Equal(0, ordered.Sequence);

        // And absence is not it.
        Assert.Null(unordered.Sequence);

        // The two share a logical name and differ only in the ordering encoding, which is precisely the
        // pair a "tidying" port would conflate.
        Assert.Equal(ordered.LogicalName, unordered.LogicalName, StringComparer.Ordinal);
        Assert.NotEqual(ordered.LegacyName, unordered.LegacyName, StringComparer.Ordinal);
        Assert.NotEqual(ordered, unordered);
    }

    // =================================================================================================
    //  ORDINAL ORDERING OF THE TWO DATAWINDOW TOPICS
    //  w_test_eventful.srw:L238 - "事件是以名称升序排列的": subscriptions are ordered by name ASCENDING.
    //  This is the fact that makes the hand-rolled prefix work at all, and the fact the dispatch-order
    //  suite then relies on. Recorded here because it is a property of the GRAMMAR - of the strings the
    //  prefix produces - rather than of the dispatch loop.
    // =================================================================================================

    /// <summary>
    /// The prefix orders the two live DataWindow topics under ordinal comparison, so item-change is
    /// dispatched before edit-change.
    /// </summary>
    /// <remarks>
    /// Asserted three ways because each could break independently: on the raw strings, through
    /// <see cref="SubscriptionTopic.CompareDispatchOrder"/>, and through a sort with the published
    /// comparer. <see cref="StringComparer.Ordinal"/> throughout - PowerScript's string relational
    /// operators compare code units with no culture involvement, so a culture-sensitive comparison could
    /// reorder these under some locale and would be a defect invisible in the developer's own.
    /// </remarks>
    [Fact]
    public void TheOrderingPrefixSortsTheDataWindowTopicsOrdinally()
    {
        SubscriptionTopic itemChanged = ParseSubscribe("0-itemchanged");
        SubscriptionTopic editChanged = ParseSubscribe("1-editchanged");

        // On the strings themselves: "0-itemchanged" precedes "1-editchanged" because '0' precedes '1'.
        Assert.True(
            StringComparer.Ordinal.Compare(itemChanged.LegacyName, editChanged.LegacyName) < 0,
            "The ordering prefix must sort 0-itemchanged before 1-editchanged.");

        // Note what the prefix is FOR: without it, the logical names sort the other way round, because
        // "editchanged" precedes "itemchanged" alphabetically. The prefix exists to invert exactly that.
        Assert.True(
            StringComparer.Ordinal.Compare(itemChanged.LogicalName, editChanged.LogicalName) > 0,
            "The logical names sort the opposite way, which is why the prefix exists.");

        // Through the published dispatch-order key.
        Assert.True(SubscriptionTopic.CompareDispatchOrder(itemChanged, editChanged) < 0);
        Assert.True(SubscriptionTopic.CompareDispatchOrder(editChanged, itemChanged) > 0);

        // And through an actual sort with the published comparer, which is how the broker consumes it.
        List<SubscriptionTopic> sorted = new List<SubscriptionTopic> { editChanged, itemChanged }
            .OrderBy(topic => topic, SubscriptionTopic.DispatchOrderComparer)
            .ToList();

        Assert.Equal(
            new[] { "0-itemchanged", "1-editchanged" },
            sorted.Select(topic => topic.LegacyName));
    }

    // =================================================================================================
    //  THE DOCUMENTED REJECTIONS - EVERY ONE RETURNS A CODE AND NONE THROWS
    //  The legacy signals every malformed topic with RetCode.E_INVALID_ARGUMENT as an ORDINARY RETURN
    //  VALUE. There are six such returns in of_on between :L343 and :L381, plus the argument check at
    //  :L331, plus exactly one in _of_modify at :L1022. The port returns the identical constant and throws
    //  nothing, so each row below asserts BOTH halves: the code, and the absence of an exception. Turning
    //  a routine caller-handled outcome into an exceptional one would change how every call site is
    //  written, so the no-throw half is a contract and not an implementation detail.
    // =================================================================================================

    /// <summary>
    /// Every spelling the subscribe grammar rejects, one row per documented rejection site.
    /// </summary>
    public static TheoryData<string> SubscribeRejectionRows =>
        new()
        {
            // :L331 - if name = "" ... then return RetCode.E_INVALID_ARGUMENT. The null topic reaches this
            // same test and has its own fact below, where the contrast with filter position can be stated.
            "",

            // :L343 - if bPrepend then return RetCode.E_INVALID_ARGUMENT. The prepend symbol once only.
            "--clicked",

            // :L346 - the capture slot is already claimed. '%' then '*'.
            "%*clicked",

            // :L349 - the same collision in the other order, '*' then '%'. Both orders because the guard
            // is symmetric and a one-sided implementation would pass only one of these rows.
            "*%clicked",

            // :L352 - the priority slot is already claimed. '!' then '@'.
            "!@clicked",

            // :L355 - the same collision in the other order, '@' then '!'.
            "@!clicked",

            // :L369 - a symbol has already moved the priority off Normal, so the numeric prefix collides
            // with it. This is w_test_eventful.srw:L232's "'!'/'@'符号与[数字]+':'不能同时使用".
            "!9999:clicked",

            // :L369 again, with the low-priority symbol.
            "@9999:clicked",

            // :L369 with an explicit zero prefix: still a collision, because the guard tests whether the
            // FIELD has moved rather than what the prefix says.
            "!0:clicked",

            // :L361 - if nPos > nLen then return ...: the run consumed the whole string, so no event name
            // remains. The brief's "name consisting only of symbols".
            "-!",

            // :L361 - a single symbol is the same case.
            "!",

            // :L361 - every leading-run symbol at once and nothing else.
            "-%!",

            // :L379 - an empty namespace is REJECTED WHEN SUBSCRIBING. The identical string is a
            // meaningful filter, which the contrast theory below asserts directly.
            "clicked.",

            // :L379 with a symbol in front, so the rejection is reached after a successful symbol run.
            "!clicked.",

            // :L381 - an empty residual name once the namespace has been split off.
            ".myns",

            // :L379 reached from a bare separator: the namespace is empty, and that is tested before the
            // empty name at :L381, so this is the namespace rejection rather than the name one.
            ".",

            // :L381 - the name is emptied by the split even though the namespace is fine.
            "!.myns"
        };

    /// <summary>
    /// Every documented subscribe rejection returns the legacy invalid-argument code, yields no topic, and
    /// throws nothing.
    /// </summary>
    [Theory]
    [MemberData(nameof(SubscribeRejectionRows))]
    public void EveryDocumentedSubscribeRejectionReturnsInvalidArgumentWithoutThrowing(string topic)
    {
        long code = RetCode.OK;
        SubscriptionTopic? parsed = null;

        // The no-throw half of the contract, asserted rather than assumed: Record.Exception returns null
        // when the delegate completes normally.
        Exception? thrown = Record.Exception(
            () => code = SubscriptionTopic.ParseSubscription(topic, out parsed));

        Assert.Null(thrown);

        // The code half. Referenced through the constant, never as the bare -3, so a change to the legacy
        // value is caught here rather than silently diverging.
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, code);

        // A failure yields no topic at all, so a caller cannot accidentally use a half-built one.
        Assert.Null(parsed);
    }

    /// <summary>
    /// A null topic reaches the same test as an empty one when subscribing, and is the legal
    /// match-everything filter when filtering.
    /// </summary>
    /// <remarks>
    /// PowerScript has no null string argument here - <c>name</c> is an always-present possibly-empty
    /// string - so both parse entry points model a null as the empty string, and each then applies its own
    /// grammar's rule to it: rejected at <c>n_cst_eventful.sru:L331</c> when subscribing, and accepted when
    /// filtering, where an empty name means "match every name" (<c>:L1017</c> with <c>:L1030</c>) and is
    /// exactly what <c>of_Off()</c> with no argument passes (<c>w_test_eventful.srw:L342</c>). A
    /// <see cref="FactAttribute"/> rather than a table row because the two halves are different claims
    /// about the same input, and stating them together is the point.
    /// </remarks>
    [Fact]
    public void ANullTopicIsEmptyToBothGrammarsAndEachAppliesItsOwnRule()
    {
        // Subscribing: rejected, with a code and no exception.
        long subscribeCode = RetCode.OK;
        SubscriptionTopic? asSubscription = null;

        Exception? subscribeThrew = Record.Exception(
            () => subscribeCode = SubscriptionTopic.ParseSubscription(null, out asSubscription));

        Assert.Null(subscribeThrew);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, subscribeCode);
        Assert.Null(asSubscription);

        // Filtering: ACCEPTED, and it is the match-everything filter.
        SubscriptionTopic asFilter = ParseFilter(null);

        Assert.True(asFilter.MatchesEveryName);
        Assert.Empty(asFilter.LegacyName);

        // No separator was supplied, so the namespace is not a criterion either - this filter constrains
        // nothing at all, which is what of_Off() with no argument means.
        Assert.Null(asFilter.NamespaceCriterion);
        Assert.False(asFilter.HasNamespaceCriterion);
        Assert.True(asFilter.Matches("clicked", SubscriptionNamespaces.None));
        Assert.True(asFilter.Matches("anything", "any-namespace"));

        // A null normalises to the empty string on the way out, so re-emission is empty rather than null.
        AssertRoundTripsExactly(string.Empty, asFilter);
    }

    /// <summary>
    /// Every spelling the filter grammar rejects - which is exactly one shape.
    /// </summary>
    public static TheoryData<string> FilterRejectionRows =>
        new()
        {
            // :L1021-L1023 - if bNotName then if bNoName then return RetCode.E_INVALID_ARGUMENT. A
            // negated EMPTY name has no meaning: "every name except no name" selects nothing coherent.
            "^",

            // The same rejection reached after the namespace split: "^.myns" splits into the name "^" and
            // the namespace "myns", and the name is then emptied by stripping the negation.
            "^.myns",

            // And with a negated namespace too, so the rejection is not conditional on the namespace being
            // positive.
            "^.^myns",

            // A bare negated separator pair: the name is "^" again once the split has taken the namespace.
            "^."
        };

    /// <summary>
    /// The filter grammar's single rejection returns the legacy invalid-argument code and throws nothing.
    /// </summary>
    /// <remarks>
    /// Only ONE shape can fail a filter, which is itself a fact worth pinning: every other emptiness is
    /// MEANINGFUL rather than invalid under this grammar, and that asymmetry with
    /// <see cref="SubscriptionTopic.ParseSubscription"/> is preserved rather than harmonised
    /// (AAP 0.7.3 C-B).
    /// </remarks>
    [Theory]
    [MemberData(nameof(FilterRejectionRows))]
    public void TheFilterGrammarsOnlyRejectionIsANegatedEmptyName(string filter)
    {
        long code = RetCode.OK;
        SubscriptionTopic? parsed = null;

        Exception? thrown = Record.Exception(
            () => code = SubscriptionTopic.ParseFilter(filter, out parsed));

        Assert.Null(thrown);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, code);
        Assert.Null(parsed);
    }

    /// <summary>
    /// Spellings that the subscribe grammar rejects and the filter grammar ACCEPTS, with the meaning the
    /// oracle documents for each as a filter.
    /// </summary>
    public static TheoryData<string, string, string> SubscribeRejectedButFilterAcceptedRows =>
        new()
        {
            // Rejected at :L379 when subscribing; w_test_eventful.srw:L330 documents it as "that event's
            // subscriptions that have NO namespace".
            { "clicked.", "clicked", "" },

            // Rejected at :L379; :L336 documents it as "every subscription with no namespace".
            { ".", "", "" },

            // Rejected at :L381 when subscribing (the name is emptied by the split); :L312 documents it as
            // "every subscription in that namespace".
            { ".myns", "", "myns" },

            // Rejected at :L331 when subscribing (empty topic); :L342 documents of_Off() with no argument
            // as the match-everything filter.
            { "", "", "" }
        };

    /// <summary>
    /// The two grammars disagree about emptiness, and that disagreement is the contract.
    /// </summary>
    /// <remarks>
    /// The single most important asymmetry in this file, so it is asserted as a DIRECT CONTRAST on the same
    /// input rather than left to be inferred from two tables that happen to disagree. Each spelling below
    /// is rejected by <c>of_on</c> and accepted by <c>_of_modify</c>, with a documented meaning as a
    /// filter. Harmonising the two - in either direction - would either break every bulk unsubscribe or
    /// admit subscriptions the legacy refuses. Pinned as correct under AAP 0.7.3 C-B.
    /// </remarks>
    [Theory]
    [MemberData(nameof(SubscribeRejectedButFilterAcceptedRows))]
    public void EmptinessIsRejectedWhenSubscribingAndMeaningfulWhenFiltering(
        string spelling,
        string expectedFilterName,
        string expectedFilterNamespace)
    {
        // Rejected as a subscription.
        Assert.Equal(
            RetCode.E_INVALID_ARGUMENT,
            SubscriptionTopic.ParseSubscription(spelling, out SubscriptionTopic? asSubscription));
        Assert.Null(asSubscription);

        // Accepted as a filter, with the meaning the oracle documents.
        SubscriptionTopic asFilter = ParseFilter(spelling);
        Assert.Equal(expectedFilterName, asFilter.LegacyName, StringComparer.Ordinal);
        Assert.Equal(expectedFilterNamespace, asFilter.Namespace, StringComparer.Ordinal);

        AssertRoundTripsExactly(spelling, asFilter);
    }

    /// <summary>
    /// The handler-name half of the legacy's <c>:L331</c> argument check, which the topic parser alone
    /// cannot express.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>n_cst_eventful.sru:L331</c> is a single statement testing TWO arguments -
    /// <c>if name = "" or evtName = "" then return RetCode.E_INVALID_ARGUMENT</c> - and the topic parser is
    /// given only the topic, so the empty-handler half is asserted where it actually lives, on
    /// <see cref="EventBroker.Subscribe(string, object?, string)"/>. Included here rather than deferred so
    /// that the rejection set for <c>:L331</c> is complete in one place.
    /// </para>
    /// <para>
    /// The ORDER of the two guards is part of the contract as well: <c>:L331</c> precedes the
    /// <c>IsValidObject</c> check at <c>:L332</c>, so an empty topic with a null target answers
    /// invalid-argument rather than invalid-object. The last case below pins that precedence.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnEmptyHandlerNameIsRejectedAlongsideAnEmptyTopic()
    {
        EventBroker broker = new();
        object target = new();

        // :L331, handler half - a valid topic with an empty handler name is still invalid.
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, broker.Subscribe("clicked", target, string.Empty));

        // :L331, topic half - reached through the broker, agreeing with the parser-level rows above.
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, broker.Subscribe(string.Empty, target, "onClicked"));

        // Both empty at once: still one code, because it is one test.
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, broker.Subscribe(string.Empty, target, string.Empty));

        // :L331 BEFORE :L332 - the argument check precedes the object check, so an empty topic with a null
        // target reports invalid ARGUMENT, not invalid OBJECT.
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, broker.Subscribe(string.Empty, null, string.Empty));

        // And a malformed TOPIC is surfaced by the broker unchanged, so the parser's codes are not
        // rewritten on their way out (the duplicate-prepend rejection at :L343, seen through Subscribe).
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, broker.Subscribe("--clicked", target, "onClicked"));
    }

    // =================================================================================================
    //  THE GRAMMAR IS CLOSED AT EIGHT SYMBOLS - AND '+' IS NOT THE NINTH
    //
    //  n_cst_eventful.sru:L91-L98 declares eight symbol constants and no more. The three-argument of_on
    //  overload's header documentation, however, describes a NINTH at :L275:
    //      '+'开头表示添加到相同优先级队列的尾部（默认，可选）
    //      ("a leading '+' means append to the tail of the same-priority queue (default, optional)")
    //  That symbol does not exist. It has no constant in the block at :L91-L98, and the parse loop at
    //  :L339-L360 has no `case` for it, so a leading '+' falls to `case else` and EXITS the run
    //  (:L357-L358) - leaving the '+' as an ordinary character of the event name.
    //
    //  The documentation and the implementation therefore disagree, and note the shape of the discrepancy:
    //  the four-argument overload's own documentation (:L302-L312) does NOT mention '+' at all, so the
    //  stale claim survives in exactly one of the two headers. Under AAP 0.7.3 C-B this is PRESERVED, not
    //  resolved: '+' is not implemented, the behaviour it describes (append) is already the DEFAULT that
    //  the absence of '-' selects, and adding the symbol to "honour the documentation" would change the
    //  parse of every event name that happens to begin with a plus.
    // =================================================================================================

    /// <summary>
    /// The grammar declares exactly eight symbols, and they are exactly the eight the legacy declares.
    /// </summary>
    /// <remarks>
    /// Asserted by reflection over the literal <see cref="char"/> fields of <see cref="TopicSymbols"/>
    /// rather than by reading eight named properties, because the claim being made is a CLOSURE claim - not
    /// "these eight exist" but "these eight and NO OTHERS". A ninth symbol added to the type would pass
    /// eight individual equality checks and fail only this one.
    /// </remarks>
    [Fact]
    public void TheGrammarDeclaresExactlyEightSymbolsAndNoMore()
    {
        char[] declared = typeof(TopicSymbols)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(char))
            .Select(field => (char)Convert.ToChar(field.GetRawConstantValue(), CultureInfo.InvariantCulture))
            .OrderBy(symbol => symbol)
            .ToArray();

        // n_cst_eventful.sru:L91-L98, in ordinal order: '!' '%' '*' '-' '.' ':' '@' '^'
        char[] expected =
        [
            TopicSymbols.High,               // :L93 '!'
            TopicSymbols.Handled,            // :L95 '%'
            TopicSymbols.All,                // :L96 '*'
            TopicSymbols.Prepend,            // :L91 '-'
            TopicSymbols.NamespaceSeparator, // :L97 '.'
            TopicSymbols.PriorityDelimiter,  // :L94 ':'
            TopicSymbols.Low,                // :L92 '@'
            TopicSymbols.Negation            // :L98 '^'
        ];

        // EIGHT, exactly - the closure assertion.
        Assert.Equal(8, declared.Length);
        Assert.Equal(expected.OrderBy(symbol => symbol), declared);

        // And each is the character the legacy declares, so the set is right as well as the size.
        Assert.Equal('-', TopicSymbols.Prepend);
        Assert.Equal('@', TopicSymbols.Low);
        Assert.Equal('!', TopicSymbols.High);
        Assert.Equal(':', TopicSymbols.PriorityDelimiter);
        Assert.Equal('%', TopicSymbols.Handled);
        Assert.Equal('*', TopicSymbols.All);
        Assert.Equal('.', TopicSymbols.NamespaceSeparator);
        Assert.Equal('^', TopicSymbols.Negation);

        // '+' IS NOT ONE OF THEM. The documented-but-absent ninth symbol.
        Assert.DoesNotContain('+', declared);
    }

    /// <summary>
    /// Exactly five of the eight symbols belong to the leading run, and <c>+</c> is not one of them.
    /// </summary>
    /// <remarks>
    /// The run scans <c>-</c>, <c>%</c>, <c>*</c>, <c>@</c> and <c>!</c>
    /// (<c>n_cst_eventful.sru:L342</c>, <c>:L345</c>, <c>:L348</c>, <c>:L351</c>, <c>:L354</c>). The other
    /// three symbols are positional rather than leading: <c>:</c> and <c>.</c> are located by search after
    /// the run has ended, and <c>^</c> belongs to the filter grammar only.
    /// </remarks>
    [Fact]
    public void ExactlyFiveSymbolsBelongToTheLeadingRun()
    {
        char[] runSymbols = TopicSymbols.LeadingRunSymbols.ToArray();

        Assert.Equal(5, runSymbols.Length);
        Assert.Equal(
            new[] { '!', '%', '*', '-', '@' },
            runSymbols.OrderBy(symbol => symbol));

        // Each of the five is recognised as a run symbol.
        foreach (char symbol in runSymbols)
        {
            Assert.True(
                TopicSymbols.IsLeadingRunSymbol(symbol),
                $"'{symbol}' is declared in the leading run and must be recognised as one.");
        }

        // The three positional symbols are NOT run symbols: the run would end at each of them.
        Assert.False(TopicSymbols.IsLeadingRunSymbol(TopicSymbols.PriorityDelimiter));
        Assert.False(TopicSymbols.IsLeadingRunSymbol(TopicSymbols.NamespaceSeparator));
        Assert.False(TopicSymbols.IsLeadingRunSymbol(TopicSymbols.Negation));

        // And neither is '+', the documented-but-absent ninth symbol - so it ends the run like any other
        // ordinary character (:L357-L358).
        Assert.False(TopicSymbols.IsLeadingRunSymbol('+'));
    }

    /// <summary>
    /// Spellings led by the documented-but-absent <c>+</c>, and the name each must keep.
    /// </summary>
    public static TheoryData<string, string> PlusIsNotASymbolRows =>
        new()
        {
            // The plus is an ordinary character, so it stays at the head of the event name.
            { "+clicked", "+clicked" },

            // Doubling it changes nothing, because there is no once-only guard to trip - the run never
            // consumed it in the first place. Contrast "--clicked", which IS rejected at :L343.
            { "++clicked", "++clicked" },

            // A plus after a real symbol: the run consumes the '-' and then ends at the '+', which is
            // therefore the first character of the name.
            { "-+clicked", "+clicked" },

            // And a plus with a namespace, so the later parse stages treat it as an ordinary character too.
            { "+clicked.myns", "+clicked" }
        };

    /// <summary>
    /// A leading <c>+</c> is an ordinary character of the event name and is never an append directive.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The negative half of the closure claim, and a C-B assertion: the legacy's own documentation at
    /// <c>n_cst_eventful.sru:L275</c> describes <c>'+'</c> as a leading append symbol, but no such symbol is
    /// declared at <c>:L91-L98</c> and the parse loop at <c>:L339-L360</c> has no case for it. The
    /// discrepancy is PRESERVED rather than resolved.
    /// </para>
    /// <para>
    /// Two consequences are asserted below, and the second is the one that makes preserving the discrepancy
    /// the safe choice rather than the lazy one. First, the character survives into the stored name - so an
    /// event genuinely named <c>"+clicked"</c> is subscribable and dispatchable. Second, the APPEND
    /// behaviour the documentation attributes to <c>'+'</c> is already what the ABSENCE of
    /// <see cref="TopicSymbols.Prepend"/> selects: <see cref="SubscriptionTopic.Prepend"/> is
    /// <see langword="false"/>, which is append, so implementing <c>'+'</c> would add no capability while
    /// silently renaming every event whose name begins with a plus.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(PlusIsNotASymbolRows))]
    public void ALeadingPlusIsPartOfTheNameAndNotAnAppendDirective(string topic, string expectedName)
    {
        SubscriptionTopic parsed = ParseSubscribe(topic);

        // The plus is retained as an ordinary character of the event name.
        Assert.Equal(expectedName, parsed.LegacyName, StringComparer.Ordinal);
        Assert.Contains('+', parsed.LegacyName);

        // It set nothing. In particular it did not set - and could not set - a capture mode or a priority.
        Assert.Equal(CaptureMode.Unhandled, parsed.Capture);
        Assert.Equal(Priorities.Normal, parsed.Priority);

        // Append is already the default that the ABSENCE of the prepend symbol selects, which is why the
        // undeclared '+' would add no capability even if it were implemented. Only the "-+clicked" row
        // carries a real prepend symbol, and there the '-' - not the '+' - is what set the flag.
        Assert.Equal(topic.StartsWith(TopicSymbols.Prepend), parsed.Prepend);

        AssertRoundTripsExactly(topic, parsed);
    }
}
