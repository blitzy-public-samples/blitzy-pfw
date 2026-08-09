// ==================================================================================================
//  SubscriptionTopicTests.cs - THE THREE FUSED ENCODINGS, PULLED APART
//  ------------------------------------------------------------------------------------------------
//  UNIT UNDER TEST   PowerFramework.Shared.Eventful.SubscriptionTopic  (+ TopicGrammar)
//  ORACLES           ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L47-L76  12 live topics
//                    ws_objects/pfw.thread.pbl.src/n_cst_threading.sru:L544,L596       '.^persistent'
//                    ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru         the broker
//
//  THE PROBLEM THIS TYPE SOLVES
//  ------------------------------------------------------------------------------------------------
//  The legacy topic string fuses THREE independent encodings into one opaque value:
//
//      an ORDERING PREFIX      se_cst_dw.sru spells its item-changed topic with a leading "0-" and its
//                              edit-changed topic with a leading "1-", and the broker's dispatch order
//                              derives from a LEXICAL SORT of the subscription name - so the digits are
//                              not decoration, they are the sequencing mechanism
//      a LOGICAL NAME          what the event actually is
//      a LIFETIME NAMESPACE    the '.persistent' suffix seen at n_cst_threading.sru:L544,L596
//
//  AAP 0.6.1.2 is explicit about why that matters across a service boundary: transmit the fused string
//  and the ordering becomes INVISIBLE to the consumer; parse it at the far end and the contract has an
//  undocumented grammar. So the wire contract carries `sequence`, `name` and `lifetime` as three separate
//  fields, and the fused legacy form is reconstituted only at the compatibility edge. This type is that
//  decomposition, and ToLegacyString is that edge.
//
//  TWO GRAMMARS, NOT ONE
//  ------------------------------------------------------------------------------------------------
//  SUBSCRIPTION grammar - what a subscriber registers. Admits a leading run of modifier symbols, an
//  optional numeric priority followed by ':', a name, and an optional '.namespace'. Rejects an empty
//  name, a repeated modifier and an empty namespace.
//
//  FILTER grammar - what an unsubscribe or disable call passes to select existing subscriptions. Admits
//  '^' negation on the name and independently on the namespace, and - unlike the subscription grammar -
//  accepts an EMPTY name, which means "every name". It is far more permissive, and the asymmetry is the
//  point: registering nothing is a programming error, selecting everything is a feature.
//
//  RULES POSITION
//  review_rules returns "No user rules provided.". Constraints cited inline: C-B (replicate behaviour),
//  AAP 0.6.1.2 (the three encodings must be carried separately), C-K (document boundary decisions).
// ==================================================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.Shared.Eventful.Tests;

/// <summary>
/// Characterization tests for <see cref="SubscriptionTopic"/>.
/// </summary>
public class SubscriptionTopicTests
{
    /// <summary>
    /// Parses a subscription topic, asserting success, and returns it.
    /// </summary>
    /// <param name="topic">The topic text.</param>
    /// <returns>The parsed topic.</returns>
    /// <remarks>
    /// Asserts rather than uses the null-forgiving operator, so a parse that unexpectedly fails reports
    /// the return code at the point of failure instead of a null-reference exception several lines later.
    /// </remarks>
    private static SubscriptionTopic ParseSubscriptionOrFail(string topic)
    {
        long code = SubscriptionTopic.ParseSubscription(topic, out SubscriptionTopic? parsed);

        Assert.Equal(RetCode.OK, code);
        Assert.NotNull(parsed);

        return parsed!;
    }

    /// <summary>
    /// Parses a filter, asserting success, and returns it.
    /// </summary>
    /// <param name="filter">The filter text.</param>
    /// <returns>The parsed filter.</returns>
    private static SubscriptionTopic ParseFilterOrFail(string? filter)
    {
        long code = SubscriptionTopic.ParseFilter(filter, out SubscriptionTopic? parsed);

        Assert.Equal(RetCode.OK, code);
        Assert.NotNull(parsed);

        return parsed!;
    }

    // ==============================================================================================
    //  1. THE THREE FUSED ENCODINGS  (AAP 0.6.1.2)
    // ==============================================================================================

    /// <summary>
    /// THE CORE CASE: the framework's own two ordered topics decompose into a sequence and a logical
    /// name.
    /// </summary>
    /// <param name="legacy">The fused legacy topic name.</param>
    /// <param name="sequence">The ordering prefix it carries.</param>
    /// <param name="logical">The logical name underneath it.</param>
    /// <remarks>
    /// <para>
    /// These two rows are the actual topics <c>se_cst_dw.sru</c> registers [:L47-L76], and they are the
    /// reason the decomposition exists: the leading digit is what orders the item-changed handler before
    /// the edit-changed one, via a lexical sort the consumer never sees. Carried as a separate integer
    /// field, the ordering becomes explicit; left fused, a gRPC consumer receives two strings whose
    /// relative dispatch order is undocumented.
    /// </para>
    /// <para>
    /// Note the sequence and the logical name are DERIVED from the legacy name rather than stored beside
    /// it, so the two can never disagree - which is what makes the reconstitution below lossless.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("0-itemchanged", 0, "itemchanged")]
    [InlineData("1-editchanged", 1, "editchanged")]
    [InlineData("2-something", 2, "something")]
    [InlineData("9-last", 9, "last")]
    [InlineData("10-tenth", 10, "tenth")]
    [InlineData("2147483647-max", 2147483647, "max")]
    public void AnOrderedTopicDecomposesIntoASequenceAndALogicalName(
        string legacy,
        int sequence,
        string logical)
    {
        SubscriptionTopic topic = ParseSubscriptionOrFail(legacy);

        Assert.Equal(sequence, topic.Sequence);
        Assert.Equal(logical, topic.LogicalName);
        Assert.Equal(legacy, topic.LegacyName);
    }

    /// <summary>
    /// A topic with no ordering prefix has a NULL sequence, and its logical name is the whole name.
    /// </summary>
    /// <param name="legacy">A topic name carrying no ordering prefix.</param>
    /// <remarks>
    /// <para>
    /// Null rather than zero, and the distinction is load-bearing: zero is a REAL sequence - <c>"0-x"</c>
    /// uses it - so an unsequenced topic must be distinguishable from one explicitly ordered first.
    /// Collapsing the two would make every unordered topic sort as though it had asked to run before
    /// everything else.
    /// </para>
    /// <para>
    /// The rows cover the shapes that look like a prefix and are not: digits with no separator, a
    /// separator with no digits, digits after the separator, a LEADING ZERO on a multi-digit run - which
    /// the reader rejects because <c>"01-x"</c> and <c>"1-x"</c> would otherwise be two spellings of one
    /// sequence - and a run too long to be an integer.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("itemchanged")]
    [InlineData("123")]
    [InlineData("-itemchanged")]
    [InlineData("item-changed")]
    [InlineData("01-x")]
    [InlineData("00-x")]
    [InlineData("0000000001-x")]
    [InlineData("12345678901-x")]
    [InlineData("1_x")]
    public void ATopicWithNoOrderingPrefixHasANullSequence(string legacy)
    {
        SubscriptionTopic topic = ParseSubscriptionOrFail(legacy);

        Assert.Null(topic.Sequence);
        Assert.Equal(topic.LegacyName, topic.LogicalName);
    }

    /// <summary>
    /// A sequence of zero is distinguishable from an ABSENT sequence.
    /// </summary>
    /// <remarks>
    /// The consequence of nullability, asserted directly rather than left to be inferred from the two
    /// theories above. This is the pair a wire contract must keep apart: one asked to be ordered first,
    /// the other expressed no opinion.
    /// </remarks>
    [Fact]
    public void ASequenceOfZeroIsNotAnAbsentSequence()
    {
        SubscriptionTopic ordered = ParseSubscriptionOrFail("0-itemchanged");
        SubscriptionTopic unordered = ParseSubscriptionOrFail("itemchanged");

        Assert.Equal(0, ordered.Sequence);
        Assert.Null(unordered.Sequence);
        Assert.NotEqual(ordered.Sequence, unordered.Sequence);

        // ...and they share a logical name, which is exactly why the sequence must be carried separately.
        Assert.Equal(ordered.LogicalName, unordered.LogicalName);
    }

    /// <summary>
    /// A sequence beyond <see cref="int.MaxValue"/> is not read as a sequence, so no overflow occurs.
    /// </summary>
    /// <remarks>
    /// The accumulator widens to 64 bits and then rejects anything that will not fit, rather than wrapping.
    /// Pinned because a wrap would turn a large ordering prefix into a small one - possibly a NEGATIVE
    /// one - and silently reorder dispatch. Rejecting it means the whole run stays part of the logical
    /// name, which is inert.
    /// </remarks>
    [Fact]
    public void ASequenceBeyondIntMaxIsRejectedRatherThanWrapped()
    {
        SubscriptionTopic tooLarge = ParseSubscriptionOrFail("2147483648-x");

        Assert.Null(tooLarge.Sequence);
        Assert.Equal("2147483648-x", tooLarge.LogicalName);

        // The largest value that DOES fit is read, so the boundary is exact.
        Assert.Equal(int.MaxValue, ParseSubscriptionOrFail("2147483647-x").Sequence);
    }

    /// <summary>
    /// The lifetime namespace is the third encoding, separated from the name and projected into a
    /// lifetime.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All three encodings present at once in the first assertion - an ordering prefix, a logical name,
    /// and the persistent namespace - which is the shape the wire contract has to carry as three fields.
    /// </para>
    /// <para>
    /// The namespace is split off BEFORE the sequence is read, so the two do not interfere: the sequence
    /// reader sees only <c>"0-itemchanged"</c> and never the suffix.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheLifetimeNamespaceIsSplitFromTheNameAndProjectedIntoALifetime()
    {
        SubscriptionTopic all3 = ParseSubscriptionOrFail("0-itemchanged.persistent");

        Assert.Equal(0, all3.Sequence);
        Assert.Equal("itemchanged", all3.LogicalName);
        Assert.Equal("0-itemchanged", all3.LegacyName);
        Assert.Equal("persistent", all3.Namespace);
        Assert.Equal(SubscriptionLifetime.Persistent, all3.Lifetime);
        Assert.True(all3.HasNamespaceCriterion);
        Assert.True(all3.HasNamespace);

        SubscriptionTopic transient = ParseSubscriptionOrFail("0-itemchanged.mynamespace");
        Assert.Equal("mynamespace", transient.Namespace);
        Assert.Equal(SubscriptionLifetime.Transient, transient.Lifetime);
        Assert.True(transient.HasNamespaceCriterion);
    }

    /// <summary>
    /// An absent namespace criterion is distinguishable from an EMPTY namespace.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>NamespaceCriterion</c> is nullable and <c>Namespace</c> coalesces it, so the pair expresses
    /// three states rather than two: no criterion at all, a criterion naming the empty namespace, and a
    /// criterion naming a real one. Only the first and third are reachable through the SUBSCRIPTION
    /// grammar, which rejects an empty namespace outright - the middle state belongs to the filter
    /// grammar, where it means "match subscriptions that have no namespace".
    /// </para>
    /// <para>
    /// Asserted because <c>Namespace</c> alone cannot tell the first two apart, and a consumer that read
    /// only <c>Namespace</c> would treat "unspecified" as "must be empty" - narrowing every namespaceless
    /// filter into one that excludes namespaced subscriptions.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnAbsentNamespaceCriterionIsNotAnEmptyOne()
    {
        SubscriptionTopic noCriterion = ParseSubscriptionOrFail("itemchanged");

        Assert.Null(noCriterion.NamespaceCriterion);
        Assert.False(noCriterion.HasNamespaceCriterion);
        Assert.Equal(SubscriptionNamespaces.None, noCriterion.Namespace);
        Assert.False(noCriterion.HasNamespace);

        // The filter grammar CAN express the empty criterion, and it is a different thing.
        SubscriptionTopic emptyCriterion = ParseFilterOrFail("itemchanged.");

        Assert.NotNull(emptyCriterion.NamespaceCriterion);
        Assert.True(emptyCriterion.HasNamespaceCriterion);
        Assert.Equal(SubscriptionNamespaces.None, emptyCriterion.Namespace);
        Assert.False(emptyCriterion.HasNamespace);
    }

    // ==============================================================================================
    //  2. THE SUBSCRIPTION GRAMMAR
    // ==============================================================================================

    /// <summary>
    /// Each leading modifier symbol sets its own property and is consumed from the name.
    /// </summary>
    /// <param name="topic">The topic text.</param>
    /// <param name="prepend">Expected insertion position.</param>
    /// <param name="capture">Expected capture mode.</param>
    /// <param name="priority">Expected priority.</param>
    /// <remarks>
    /// One row per symbol, then rows combining them, so both the individual mapping and the run-parsing
    /// are covered. The combination rows also pin that ORDER within the run does not matter - the parser
    /// consumes symbols in a loop rather than in a fixed sequence - which is what lets a caller write
    /// <c>"-!name"</c> or <c>"!-name"</c> interchangeably.
    /// </remarks>
    [Theory]
    [InlineData("name", false, CaptureMode.Unhandled, 0)]
    [InlineData("-name", true, CaptureMode.Unhandled, 0)]
    [InlineData("%name", false, CaptureMode.Handled, 0)]
    [InlineData("*name", false, CaptureMode.All, 0)]
    [InlineData("@name", false, CaptureMode.Unhandled, int.MinValue)]
    [InlineData("!name", false, CaptureMode.Unhandled, int.MaxValue)]
    [InlineData("-!name", true, CaptureMode.Unhandled, int.MaxValue)]
    [InlineData("!-name", true, CaptureMode.Unhandled, int.MaxValue)]
    [InlineData("-*!name", true, CaptureMode.All, int.MaxValue)]
    [InlineData("*@-name", true, CaptureMode.All, int.MinValue)]
    public void EachLeadingModifierSetsItsPropertyAndIsConsumed(
        string topic,
        bool prepend,
        CaptureMode capture,
        int priority)
    {
        SubscriptionTopic parsed = ParseSubscriptionOrFail(topic);

        Assert.Equal(prepend, parsed.Prepend);
        Assert.Equal(capture, parsed.Capture);
        Assert.Equal(priority, parsed.Priority);
        Assert.Equal("name", parsed.LegacyName);
    }

    /// <summary>
    /// A numeric priority prefix is read when followed by the delimiter, and removed from the name.
    /// </summary>
    /// <param name="topic">The topic text.</param>
    /// <param name="priority">The expected priority.</param>
    /// <param name="name">The expected remaining name.</param>
    /// <remarks>
    /// The signed rows matter: an explicit negative priority is legal and is how a caller expresses "later
    /// than normal but not last". The magnitude guard is also covered - a value beyond the signed range is
    /// NOT read as a priority, so the text stays part of the name rather than wrapping into a wrong
    /// priority.
    /// </remarks>
    [Theory]
    [InlineData("100:name", 100, "name")]
    [InlineData("0:name", 0, "name")]
    [InlineData("-100:name", 100, "name")]
    [InlineData("+100:name", 100, "name")]
    [InlineData("2147483647:name", 2147483647, "name")]
    public void ANumericPriorityPrefixIsReadAndRemovedFromTheName(string topic, int priority, string name)
    {
        SubscriptionTopic parsed = ParseSubscriptionOrFail(topic);

        Assert.Equal(priority, parsed.Priority);
        Assert.Equal(name, parsed.LegacyName);
    }

    /// <summary>
    /// A leading <c>-</c> is consumed as the PREPEND symbol before any numeric priority is read, so a
    /// negative numeric priority cannot be written with a leading minus.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A genuine and load-bearing ambiguity in the grammar, resolved by the order of the two parsing
    /// stages. <c>"-100:name"</c> does NOT mean priority minus one hundred: the leading <c>-</c> is
    /// consumed by the modifier run as prepend, leaving <c>"100:name"</c>, which parses as priority
    /// POSITIVE one hundred. The row in the theory above records that outcome; this test explains it.
    /// </para>
    /// <para>
    /// Reproduced rather than resolved. A caller wanting a negative priority must place the sign after
    /// another modifier - <c>"%-100:name"</c> still reads the minus as prepend - or accept that the
    /// grammar reaches negative priorities only through <c>@</c>. Pinned because "fixing" the precedence
    /// would change what every existing topic string means.
    /// </para>
    /// </remarks>
    [Fact]
    public void ALeadingMinusIsPrependNotANegativePriority()
    {
        SubscriptionTopic parsed = ParseSubscriptionOrFail("-100:name");

        Assert.True(parsed.Prepend);
        Assert.Equal(100, parsed.Priority);
        Assert.NotEqual(-100, parsed.Priority);
        Assert.Equal("name", parsed.LegacyName);
    }

    /// <summary>
    /// A numeric priority is rejected when the topic ALSO carries a symbolic one, because the two would
    /// disagree.
    /// </summary>
    /// <remarks>
    /// The parser tracks whether a priority has already been set and refuses a second. Without the guard
    /// the last writer would win silently, and <c>"!100:name"</c> would mean either highest priority or
    /// one hundred depending on the implementation's order. Refusing makes the ambiguity a caller error
    /// rather than a coin flip.
    /// </remarks>
    [Fact]
    public void ASymbolicAndNumericPriorityTogetherAreRejected()
    {
        Assert.Equal(
            RetCode.E_INVALID_ARGUMENT,
            SubscriptionTopic.ParseSubscription("!100:name", out SubscriptionTopic? high));
        Assert.Null(high);

        Assert.Equal(
            RetCode.E_INVALID_ARGUMENT,
            SubscriptionTopic.ParseSubscription("@100:name", out SubscriptionTopic? low));
        Assert.Null(low);
    }

    /// <summary>
    /// A repeated modifier is rejected, on every one of the five.
    /// </summary>
    /// <param name="topic">A topic repeating one modifier.</param>
    /// <remarks>
    /// Each guard is separate in the parser - one per symbol - so each needs its own row. A repeated
    /// symbol is not merely redundant: for the capture and priority symbols the second occurrence would
    /// be a CONFLICTING request, since <c>%</c> and <c>*</c> are alternatives and so are <c>@</c> and
    /// <c>!</c>. The cross-symbol rows cover exactly that.
    /// </remarks>
    [Theory]
    [InlineData("--name")]
    [InlineData("%%name")]
    [InlineData("**name")]
    [InlineData("@@name")]
    [InlineData("!!name")]
    [InlineData("%*name")]
    [InlineData("*%name")]
    [InlineData("@!name")]
    [InlineData("!@name")]
    public void ARepeatedOrConflictingModifierIsRejected(string topic)
    {
        Assert.Equal(
            RetCode.E_INVALID_ARGUMENT,
            SubscriptionTopic.ParseSubscription(topic, out SubscriptionTopic? parsed));

        Assert.Null(parsed);
    }

    /// <summary>
    /// A subscription with no name is rejected, in every shape that produces one.
    /// </summary>
    /// <param name="topic">A topic with no usable name.</param>
    /// <remarks>
    /// <para>
    /// The subscription grammar's central restriction, and the asymmetry with the filter grammar. A
    /// subscriber that names no event has asked for nothing, so it is a programming error - whereas a
    /// FILTER with no name means "all", which is useful. The rows cover: nothing at all, only modifiers,
    /// a name consumed entirely by the namespace split, and an empty namespace after a real name.
    /// </para>
    /// <para>
    /// Note the out-parameter is asserted NULL on every failure. A parser that returned a failure code
    /// while also producing a topic would invite a caller to use it, which is the sort of half-failure
    /// that produces a subscription nobody registered.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("!")]
    [InlineData("@")]
    [InlineData("%")]
    [InlineData("*")]
    [InlineData("-!*")]
    [InlineData(".persistent")]
    [InlineData("name.")]
    [InlineData("100:")]
    public void ASubscriptionWithNoUsableNameIsRejected(string topic)
    {
        Assert.Equal(
            RetCode.E_INVALID_ARGUMENT,
            SubscriptionTopic.ParseSubscription(topic, out SubscriptionTopic? parsed));

        Assert.Null(parsed);
    }

    /// <summary>
    /// A null subscription topic is rejected exactly as the empty string is.
    /// </summary>
    /// <remarks>
    /// PowerScript has no distinct null-versus-empty for a string argument in this position, so the port
    /// coalesces and both take the same path. Asserted so a caller cannot rely on a
    /// <see cref="ArgumentNullException"/> that will never be thrown.
    /// </remarks>
    [Fact]
    public void ANullSubscriptionTopicIsRejectedLikeAnEmptyOne()
    {
        Assert.Equal(
            RetCode.E_INVALID_ARGUMENT,
            SubscriptionTopic.ParseSubscription(null, out SubscriptionTopic? fromNull));

        Assert.Null(fromNull);

        Assert.Equal(
            SubscriptionTopic.ParseSubscription(string.Empty, out _),
            SubscriptionTopic.ParseSubscription(null, out _));
    }

    /// <summary>
    /// A parsed subscription reports the SUBSCRIPTION grammar and never the negation flags, which belong
    /// to filters.
    /// </summary>
    /// <remarks>
    /// The grammar tag is what lets a consumer know which rules produced a topic, and the negation flags
    /// being false is what makes a subscription's <c>Matches</c> behave positively. A subscription that
    /// somehow carried a negation would match everything EXCEPT its own name.
    /// </remarks>
    [Fact]
    public void AParsedSubscriptionCarriesTheSubscriptionGrammarAndNoNegation()
    {
        SubscriptionTopic parsed = ParseSubscriptionOrFail("^name");

        Assert.Equal(TopicGrammar.Subscription, parsed.Grammar);
        Assert.False(parsed.NegateName);
        Assert.False(parsed.NegateNamespace);

        // '^' is not a subscription symbol, so it is part of the NAME rather than a negation.
        Assert.Equal("^name", parsed.LegacyName);
    }

    // ==============================================================================================
    //  3. THE FILTER GRAMMAR
    // ==============================================================================================

    /// <summary>
    /// An empty filter matches EVERY name - the asymmetry with the subscription grammar.
    /// </summary>
    /// <param name="filter">An empty or null filter.</param>
    /// <remarks>
    /// The permissiveness that makes an unsubscribe-everything call expressible. Where the subscription
    /// grammar rejects an empty name, the filter grammar accepts it and sets <c>MatchesEveryName</c>, and
    /// <c>Matches</c> then short-circuits to true for any name at all. Both null and empty are covered
    /// because the broker's overloads reach here with either.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void AnEmptyFilterMatchesEveryName(string? filter)
    {
        SubscriptionTopic parsed = ParseFilterOrFail(filter);

        Assert.Equal(TopicGrammar.Filter, parsed.Grammar);
        Assert.True(parsed.MatchesEveryName);
        Assert.Equal(string.Empty, parsed.LegacyName);

        Assert.True(parsed.Matches("anything", null));
        Assert.True(parsed.Matches("something else", "persistent"));
        Assert.True(parsed.Matches(null, null));
    }

    /// <summary>
    /// Name and namespace negation are read independently, so all four combinations are expressible.
    /// </summary>
    /// <param name="filter">The filter text.</param>
    /// <param name="negateName">Expected name negation.</param>
    /// <param name="negateNamespace">Expected namespace negation.</param>
    /// <param name="name">Expected name after the negation symbol is consumed.</param>
    /// <param name="criterion">Expected namespace criterion after its negation symbol is consumed.</param>
    /// <remarks>
    /// Independence is the property being pinned. The two flags are set by separate tests in the parser,
    /// so a filter can negate the name, the namespace, both, or neither - and the both row is the one that
    /// would fail if the implementation shared one flag.
    /// </remarks>
    [Theory]
    [InlineData("name.ns", false, false, "name", "ns")]
    [InlineData("^name.ns", true, false, "name", "ns")]
    [InlineData("name.^ns", false, true, "name", "ns")]
    [InlineData("^name.^ns", true, true, "name", "ns")]
    public void NameAndNamespaceNegationAreReadIndependently(
        string filter,
        bool negateName,
        bool negateNamespace,
        string name,
        string criterion)
    {
        SubscriptionTopic parsed = ParseFilterOrFail(filter);

        Assert.Equal(negateName, parsed.NegateName);
        Assert.Equal(negateNamespace, parsed.NegateNamespace);
        Assert.Equal(name, parsed.LegacyName);
        Assert.Equal(criterion, parsed.NamespaceCriterion);
    }

    /// <summary>
    /// A negation with no name is rejected: negating nothing has no meaning.
    /// </summary>
    /// <remarks>
    /// The filter grammar's ONLY rejection, and it is narrow. An empty name means "every name", so
    /// negating it would mean "no name" - a filter that matches nothing, which is never what a caller
    /// intends and would silently make an unsubscribe call a no-op. Rejecting it surfaces the mistake.
    /// </remarks>
    [Fact]
    public void ANegationWithNoNameIsRejected()
    {
        Assert.Equal(
            RetCode.E_INVALID_ARGUMENT,
            SubscriptionTopic.ParseFilter("^", out SubscriptionTopic? bare));
        Assert.Null(bare);

        Assert.Equal(
            RetCode.E_INVALID_ARGUMENT,
            SubscriptionTopic.ParseFilter("^.ns", out SubscriptionTopic? withNamespace));
        Assert.Null(withNamespace);
    }

    /// <summary>
    /// A filter ignores the subscription grammar's modifier symbols, treating them as part of the name.
    /// </summary>
    /// <param name="filter">A filter beginning with a subscription modifier.</param>
    /// <remarks>
    /// The two grammars are genuinely separate rather than one grammar with optional extras. A filter's
    /// job is to SELECT existing subscriptions by name, and a subscription's stored name never contains
    /// its modifiers - they were consumed at registration. So a filter that stripped them would look for
    /// a name nobody registered, and one that keeps them looks for a name containing the symbol, which is
    /// at least honest about matching nothing.
    /// </remarks>
    [Theory]
    [InlineData("-name")]
    [InlineData("!name")]
    [InlineData("@name")]
    [InlineData("%name")]
    [InlineData("*name")]
    public void AFilterTreatsSubscriptionModifiersAsPartOfTheName(string filter)
    {
        SubscriptionTopic parsed = ParseFilterOrFail(filter);

        Assert.Equal(filter, parsed.LegacyName);
        Assert.Equal(CaptureMode.Unhandled, parsed.Capture);
        Assert.Equal(Priorities.Normal, parsed.Priority);
        Assert.False(parsed.Prepend);
    }

    // ==============================================================================================
    //  4. MATCHING
    // ==============================================================================================

    /// <summary>
    /// A plain filter matches its own name exactly and nothing else.
    /// </summary>
    /// <remarks>
    /// Ordinal comparison, so case matters - the same case sensitivity <c>NameComparer</c> declares. That
    /// consistency is the point: a filter must select the same subscriptions the broker's own name lookup
    /// would find, and a case-insensitive match here would select subscriptions the broker considers
    /// distinct.
    /// </remarks>
    [Fact]
    public void APlainFilterMatchesItsOwnNameExactly()
    {
        SubscriptionTopic filter = ParseFilterOrFail("itemchanged");

        Assert.True(filter.Matches("itemchanged", null));
        Assert.False(filter.Matches("ItemChanged", null));
        Assert.False(filter.Matches("itemchanged2", null));
        Assert.False(filter.Matches("", null));
        Assert.False(filter.Matches(null, null));
    }

    /// <summary>
    /// A negated name filter matches everything EXCEPT its name.
    /// </summary>
    /// <remarks>
    /// The inversion asserted against the same inputs as the positive case, so the two read as
    /// complements. The null and empty rows are included because they are names the negation must ACCEPT -
    /// they are not the excluded name - and an implementation that guarded null before negating would
    /// reject them.
    /// </remarks>
    [Fact]
    public void ANegatedNameFilterMatchesEverythingExceptItsName()
    {
        SubscriptionTopic filter = ParseFilterOrFail("^itemchanged");

        Assert.False(filter.Matches("itemchanged", null));
        Assert.True(filter.Matches("ItemChanged", null));
        Assert.True(filter.Matches("editchanged", null));
        Assert.True(filter.Matches("", null));
        Assert.True(filter.Matches(null, null));
    }

    /// <summary>
    /// The namespace criterion is applied only when present, and only AFTER the name has matched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The conjunction and its order are both asserted. A filter with a namespace requires BOTH to match,
    /// and a filter without one ignores the namespace entirely - so a namespaceless filter selects
    /// subscriptions in every namespace, which is what makes an unqualified unsubscribe reach persistent
    /// subscriptions too.
    /// </para>
    /// <para>
    /// The ordering matters because the namespace test is skipped when the name has already failed. That
    /// is not merely an optimization: it means a negated NAMESPACE cannot rescue a failed name match,
    /// which is the behaviour a caller writing <c>"^name.^ns"</c> needs to understand.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheNamespaceCriterionIsAConjunctionAppliedAfterTheName()
    {
        SubscriptionTopic qualified = ParseFilterOrFail("itemchanged.persistent");

        Assert.True(qualified.Matches("itemchanged", "persistent"));
        Assert.False(qualified.Matches("itemchanged", "other"));
        Assert.False(qualified.Matches("itemchanged", null));
        Assert.False(qualified.Matches("editchanged", "persistent"));

        // Unqualified: the namespace is not consulted at all.
        SubscriptionTopic unqualified = ParseFilterOrFail("itemchanged");

        Assert.True(unqualified.Matches("itemchanged", "persistent"));
        Assert.True(unqualified.Matches("itemchanged", "anything"));
        Assert.True(unqualified.Matches("itemchanged", null));

        // A negated namespace cannot rescue a failed name.
        SubscriptionTopic bothNegated = ParseFilterOrFail("^itemchanged.^persistent");
        Assert.False(bothNegated.Matches("itemchanged", "transient"));
        Assert.True(bothNegated.Matches("editchanged", "transient"));
        Assert.False(bothNegated.Matches("editchanged", "persistent"));
    }

    /// <summary>
    /// An EMPTY namespace criterion selects subscriptions that have no namespace.
    /// </summary>
    /// <remarks>
    /// The use for the absent-versus-empty distinction pinned earlier. <c>"name."</c> is a filter that
    /// requires the namespace to be empty, which is how a caller reaches transient subscriptions while
    /// leaving namespaced ones alone - the exact complement of an unqualified filter.
    /// </remarks>
    [Fact]
    public void AnEmptyNamespaceCriterionSelectsOnlyNamespacelessSubscriptions()
    {
        SubscriptionTopic filter = ParseFilterOrFail("itemchanged.");

        Assert.True(filter.Matches("itemchanged", null));
        Assert.True(filter.Matches("itemchanged", string.Empty));
        Assert.False(filter.Matches("itemchanged", "persistent"));
    }

    /// <summary>
    /// A subscription topic can also be matched against, and it behaves as a positive exact filter.
    /// </summary>
    /// <remarks>
    /// <c>Matches</c> is declared on the type rather than on a filter-only subtype, so a subscription
    /// topic answers it too. Because its negation flags are false and its name is non-empty, it behaves as
    /// an exact positive match - which is the sensible reading and is worth pinning so a caller that
    /// passes the wrong grammar gets a predictable answer rather than an accidental match-everything.
    /// </remarks>
    [Fact]
    public void ASubscriptionTopicMatchesAsAPositiveExactFilter()
    {
        SubscriptionTopic subscription = ParseSubscriptionOrFail("!0-itemchanged.persistent");

        Assert.True(subscription.Matches("0-itemchanged", "persistent"));
        Assert.False(subscription.Matches("0-itemchanged", "other"));
        Assert.False(subscription.Matches("itemchanged", "persistent"));
    }

    // ==============================================================================================
    //  5. RECONSTITUTION AT THE COMPATIBILITY EDGE  (AAP 0.6.1.2)
    // ==============================================================================================

    /// <summary>
    /// <see cref="SubscriptionTopic.ToLegacyString"/> returns the ORIGINAL text verbatim when the topic
    /// came from parsing, so a round trip is lossless.
    /// </summary>
    /// <param name="original">A topic string to round-trip.</param>
    /// <remarks>
    /// <para>
    /// This is the compatibility edge AAP 0.6.1.2 requires: the decomposed form travels over the wire, and
    /// the fused legacy form is rebuilt only when handing back to a legacy consumer. Returning the raw
    /// text rather than a canonical rendering is what makes the round trip EXACT - including for spellings
    /// the canonical builder would normalize, such as a reordered modifier run.
    /// </para>
    /// <para>
    /// The rows deliberately include such spellings. <c>"!-name"</c> canonicalizes to <c>"-!name"</c>,
    /// and preserving the raw value is what stops a round trip through this type from silently rewriting a
    /// caller's topic string.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("itemchanged")]
    [InlineData("0-itemchanged")]
    [InlineData("0-itemchanged.persistent")]
    [InlineData("-!name")]
    [InlineData("!-name")]
    [InlineData("*@-name")]
    [InlineData("100:name")]
    [InlineData("-100:name")]
    [InlineData("+100:name")]
    public void ParsingAndReconstitutingIsLossless(string original)
    {
        Assert.Equal(original, ParseSubscriptionOrFail(original).ToLegacyString());
    }

    /// <summary>
    /// A filter round-trips verbatim too, negations included.
    /// </summary>
    /// <param name="original">A filter string to round-trip.</param>
    /// <remarks>
    /// The same guarantee on the other grammar. The empty row is included because an empty filter is
    /// meaningful - it means every name - so its round trip must produce the empty string rather than
    /// some canonical stand-in.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("itemchanged")]
    [InlineData("^itemchanged")]
    [InlineData("itemchanged.persistent")]
    [InlineData("^itemchanged.^persistent")]
    [InlineData("itemchanged.")]
    public void AFilterRoundTripsVerbatim(string original)
    {
        Assert.Equal(original, ParseFilterOrFail(original).ToLegacyString());
    }

    /// <summary>
    /// A topic BUILT rather than parsed renders canonically, in the documented symbol order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The other half of <c>ToLegacyString</c>, reached when there is no raw value to return - which is
    /// the case for a topic assembled from the wire contract's three separate fields. The canonical order
    /// is prepend, capture, priority, name, namespace, and it is the order the parser accepts, so a
    /// canonical rendering always re-parses to an equal topic.
    /// </para>
    /// <para>
    /// The numeric-priority row exercises the delimiter branch, which the symbolic rows do not: a priority
    /// that is neither of the two extremes nor normal renders as digits followed by <c>:</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public void ABuiltTopicRendersCanonically()
    {
        Assert.Equal(
            "name",
            new SubscriptionTopic { LegacyName = "name" }.ToLegacyString());

        Assert.Equal(
            "-name",
            new SubscriptionTopic { LegacyName = "name", Prepend = true }.ToLegacyString());

        Assert.Equal(
            "*name",
            new SubscriptionTopic { LegacyName = "name", Capture = CaptureMode.All }.ToLegacyString());

        Assert.Equal(
            "%name",
            new SubscriptionTopic { LegacyName = "name", Capture = CaptureMode.Handled }.ToLegacyString());

        Assert.Equal(
            "!name",
            new SubscriptionTopic { LegacyName = "name", Priority = Priorities.High }.ToLegacyString());

        Assert.Equal(
            "@name",
            new SubscriptionTopic { LegacyName = "name", Priority = Priorities.Low }.ToLegacyString());

        // A numeric priority renders with the delimiter.
        Assert.Equal(
            "42:name",
            new SubscriptionTopic { LegacyName = "name", Priority = 42 }.ToLegacyString());

        // The full canonical order: prepend, capture, priority, name, namespace.
        Assert.Equal(
            "-*!name.persistent",
            new SubscriptionTopic
            {
                LegacyName = "name",
                Prepend = true,
                Capture = CaptureMode.All,
                Priority = Priorities.High,
                NamespaceCriterion = SubscriptionNamespaces.Persistent,
            }.ToLegacyString());
    }

    /// <summary>
    /// A canonically rendered subscription re-parses to a topic with the same decomposed fields.
    /// </summary>
    /// <remarks>
    /// The property that makes canonical rendering safe: build from the wire fields, render to legacy,
    /// hand to a legacy consumer, and the consumer's own parse recovers exactly what the wire carried.
    /// Asserted on the DECOMPOSED fields rather than on the string, because the raw values differ by
    /// construction - one is null and the other is the rendered text.
    /// </remarks>
    [Fact]
    public void ACanonicalRenderingReParsesToTheSameFields()
    {
        SubscriptionTopic built = new()
        {
            LegacyName = "0-itemchanged",
            Prepend = true,
            Capture = CaptureMode.All,
            Priority = Priorities.High,
            NamespaceCriterion = SubscriptionNamespaces.Persistent,
        };

        SubscriptionTopic reparsed = ParseSubscriptionOrFail(built.ToLegacyString());

        Assert.Equal(built.LegacyName, reparsed.LegacyName);
        Assert.Equal(built.Sequence, reparsed.Sequence);
        Assert.Equal(built.LogicalName, reparsed.LogicalName);
        Assert.Equal(built.Namespace, reparsed.Namespace);
        Assert.Equal(built.Lifetime, reparsed.Lifetime);
        Assert.Equal(built.Capture, reparsed.Capture);
        Assert.Equal(built.Priority, reparsed.Priority);
        Assert.Equal(built.Prepend, reparsed.Prepend);
    }

    /// <summary>
    /// A canonically rendered FILTER re-parses to the same fields, negations included.
    /// </summary>
    /// <remarks>
    /// The filter half of the same guarantee. The negation symbols are the interesting part: they render
    /// in two different positions - before the name and after the namespace separator - so a builder that
    /// emitted both in one place would produce a string the parser reads differently.
    /// </remarks>
    [Fact]
    public void ACanonicalFilterRenderingReParsesToTheSameFields()
    {
        SubscriptionTopic built = new()
        {
            Grammar = TopicGrammar.Filter,
            LegacyName = "itemchanged",
            NamespaceCriterion = SubscriptionNamespaces.Persistent,
            NegateName = true,
            NegateNamespace = true,
        };

        Assert.Equal("^itemchanged.^persistent", built.ToLegacyString());

        SubscriptionTopic reparsed = ParseFilterOrFail(built.ToLegacyString());

        Assert.Equal(built, reparsed with { RawValue = null });
    }

    /// <summary>
    /// <see cref="SubscriptionTopic.ToSubscriptionOptions"/> projects the four behavioural fields and
    /// drops the rest.
    /// </summary>
    /// <remarks>
    /// The projection is deliberately lossy: options describe HOW a subscription behaves, not WHICH event
    /// it names, so the name, sequence, grammar and negations have no place in them. Asserting the drop as
    /// well as the carry is what documents that the two types are not interchangeable views of one thing.
    /// </remarks>
    [Fact]
    public void TheOptionsProjectionCarriesBehaviourAndDropsIdentity()
    {
        SubscriptionTopic topic = ParseSubscriptionOrFail("-*!0-itemchanged.persistent");

        SubscriptionOptions options = topic.ToSubscriptionOptions();

        Assert.Equal(CaptureMode.All, options.Capture);
        Assert.Equal(Priorities.High, options.Priority);
        Assert.True(options.Prepend);
        Assert.Equal("persistent", options.Namespace);
        Assert.Equal(SubscriptionLifetime.Persistent, options.Lifetime);

        // Identity is not carried: the options type has no name at all, and no handler was set.
        Assert.Equal(string.Empty, options.HandlerName);
    }

    // ==============================================================================================
    //  6. DISPATCH ORDER
    // ==============================================================================================

    /// <summary>
    /// Dispatch order is by name ORDINALLY ascending, then by priority DESCENDING.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both directions are load-bearing and they are opposite, which is the thing to get right. Names sort
    /// ascending because that is what makes the leading digits order the framework's own topics - <c>"0-"</c>
    /// before <c>"1-"</c>. Priorities sort DESCENDING because a HIGHER priority must run EARLIER, which is
    /// the reverse of a natural numeric sort.
    /// </para>
    /// <para>
    /// An implementation that sorted priority ascending would run the lowest-priority subscriber first
    /// while still passing any test that only checked names, which is why the priority direction is
    /// asserted on topics that share a name.
    /// </para>
    /// </remarks>
    [Fact]
    public void DispatchOrderIsNameAscendingThenPriorityDescending()
    {
        SubscriptionTopic first = ParseSubscriptionOrFail("0-itemchanged");
        SubscriptionTopic second = ParseSubscriptionOrFail("1-editchanged");

        Assert.True(SubscriptionTopic.CompareDispatchOrder(first, second) < 0);
        Assert.True(SubscriptionTopic.CompareDispatchOrder(second, first) > 0);

        // Same name, different priority: HIGHER runs EARLIER.
        SubscriptionTopic high = ParseSubscriptionOrFail("!name");
        SubscriptionTopic normal = ParseSubscriptionOrFail("name");
        SubscriptionTopic low = ParseSubscriptionOrFail("@name");

        Assert.True(SubscriptionTopic.CompareDispatchOrder(high, normal) < 0);
        Assert.True(SubscriptionTopic.CompareDispatchOrder(normal, low) < 0);
        Assert.True(SubscriptionTopic.CompareDispatchOrder(high, low) < 0);

        // Identical name and priority compare equal.
        Assert.Equal(0, SubscriptionTopic.CompareDispatchOrder(normal, ParseSubscriptionOrFail("name")));
    }

    /// <summary>
    /// The name comparison DOMINATES the priority comparison.
    /// </summary>
    /// <remarks>
    /// The precedence stated explicitly, because it is what makes the sequencing prefix authoritative: a
    /// low-priority subscriber on <c>"0-x"</c> still runs before a high-priority one on <c>"1-x"</c>.
    /// Priority breaks ties WITHIN a name, it does not compete with the name. Reversing the precedence
    /// would let a priority override the ordering prefix and break the very mechanism the prefix exists
    /// for.
    /// </remarks>
    [Fact]
    public void TheNameComparisonDominatesThePriorityComparison()
    {
        SubscriptionTopic earlyNameLowPriority = ParseSubscriptionOrFail("@0-itemchanged");
        SubscriptionTopic lateNameHighPriority = ParseSubscriptionOrFail("!1-editchanged");

        Assert.True(SubscriptionTopic.CompareDispatchOrder(earlyNameLowPriority, lateNameHighPriority) < 0);
    }

    /// <summary>
    /// Sorting a set of topics with the published comparer produces the dispatch order.
    /// </summary>
    /// <remarks>
    /// The comparer exercised as the broker uses it, rather than one pair at a time. A comparer that were
    /// inconsistent - not a total order - could pass every pairwise test above and still produce an
    /// arbitrary result from a sort, so running an actual sort is what proves it usable.
    /// </remarks>
    [Fact]
    public void SortingWithThePublishedComparerProducesDispatchOrder()
    {
        SubscriptionTopic[] topics =
        [
            ParseSubscriptionOrFail("1-editchanged"),
            ParseSubscriptionOrFail("@0-itemchanged"),
            ParseSubscriptionOrFail("!0-itemchanged"),
            ParseSubscriptionOrFail("0-itemchanged"),
        ];

        SubscriptionTopic[] sorted = [.. topics.OrderBy(topic => topic, SubscriptionTopic.DispatchOrderComparer)];

        Assert.Equal(
            ["0-itemchanged", "0-itemchanged", "0-itemchanged", "1-editchanged"],
            sorted.Select(topic => topic.LegacyName));

        Assert.Equal(
            [Priorities.High, Priorities.Normal, Priorities.Low, Priorities.Normal],
            sorted.Select(topic => topic.Priority));
    }

    /// <summary>
    /// The comparer orders nulls first and treats two nulls as equal.
    /// </summary>
    /// <remarks>
    /// A total order has to answer for null, and answering consistently is what stops a sort from throwing
    /// or producing a nondeterministic arrangement when a collection has a gap. Nulls first rather than
    /// last is arbitrary but must be STABLE, so both directions and the reflexive case are asserted.
    /// </remarks>
    [Fact]
    public void TheComparerOrdersNullsFirstConsistently()
    {
        SubscriptionTopic topic = ParseSubscriptionOrFail("name");

        Assert.Equal(0, SubscriptionTopic.CompareDispatchOrder(null, null));
        Assert.True(SubscriptionTopic.CompareDispatchOrder(null, topic) < 0);
        Assert.True(SubscriptionTopic.CompareDispatchOrder(topic, null) > 0);

        Assert.Equal(0, SubscriptionTopic.DispatchOrderComparer.Compare(null, null));
        Assert.True(SubscriptionTopic.DispatchOrderComparer.Compare(null, topic) < 0);
        Assert.True(SubscriptionTopic.DispatchOrderComparer.Compare(topic, null) > 0);
    }

    /// <summary>
    /// The published comparer agrees with the static comparison method, and is a shared singleton.
    /// </summary>
    /// <remarks>
    /// Two ways to reach one ordering, so they must not diverge. Asserted across a cross-product of
    /// topics rather than on a sample, because a divergence in one arm of the comparison would be enough
    /// to make a sort and a manual comparison disagree.
    /// </remarks>
    [Fact]
    public void ThePublishedComparerAgreesWithTheStaticMethod()
    {
        Assert.Same(SubscriptionTopic.DispatchOrderComparer, SubscriptionTopic.DispatchOrderComparer);

        SubscriptionTopic?[] topics =
        [
            null,
            ParseSubscriptionOrFail("0-itemchanged"),
            ParseSubscriptionOrFail("!0-itemchanged"),
            ParseSubscriptionOrFail("@0-itemchanged"),
            ParseSubscriptionOrFail("1-editchanged"),
            ParseSubscriptionOrFail("name"),
        ];

        foreach (SubscriptionTopic? left in topics)
        {
            foreach (SubscriptionTopic? right in topics)
            {
                Assert.Equal(
                    Math.Sign(SubscriptionTopic.CompareDispatchOrder(left, right)),
                    Math.Sign(SubscriptionTopic.DispatchOrderComparer.Compare(left, right)));
            }
        }
    }

    // ==============================================================================================
    //  7. EQUALITY AND RENDERING
    // ==============================================================================================

    /// <summary>
    /// Equality covers the eight identity-bearing members and deliberately EXCLUDES the raw value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The exclusion is the substantive part. Two topics that mean the same thing must be equal even when
    /// spelled differently - <c>"-!name"</c> and <c>"!-name"</c> are the same subscription - so the raw
    /// text cannot participate. Including it would make a parsed topic unequal to the canonically built
    /// topic it is otherwise identical to, breaking the broker's structural comparisons.
    /// </para>
    /// <para>
    /// Every included member is checked by mutating it and asserting inequality, so a member accidentally
    /// omitted from the hand-written <c>Equals</c> is caught. The sequence and logical name are NOT in the
    /// list because they are derived from the legacy name, which is.
    /// </para>
    /// </remarks>
    [Fact]
    public void EqualityCoversTheIdentityMembersAndExcludesTheRawValue()
    {
        SubscriptionTopic baseline = new()
        {
            Grammar = TopicGrammar.Subscription,
            LegacyName = "0-itemchanged",
            NamespaceCriterion = "persistent",
            Capture = CaptureMode.All,
            Priority = 42,
            Prepend = true,
            NegateName = false,
            NegateNamespace = false,
        };

        // The raw value is excluded: two spellings of one topic are equal.
        Assert.Equal(baseline, baseline with { RawValue = "-*42:0-itemchanged.persistent" });
        Assert.Equal(baseline, baseline with { RawValue = "some other spelling entirely" });
        Assert.Equal(
            baseline.GetHashCode(),
            (baseline with { RawValue = "anything" }).GetHashCode());

        // Every identity member participates.
        Assert.NotEqual(baseline, baseline with { Grammar = TopicGrammar.Filter });
        Assert.NotEqual(baseline, baseline with { LegacyName = "1-editchanged" });
        Assert.NotEqual(baseline, baseline with { NamespaceCriterion = "other" });
        Assert.NotEqual(baseline, baseline with { NamespaceCriterion = null });
        Assert.NotEqual(baseline, baseline with { Capture = CaptureMode.Handled });
        Assert.NotEqual(baseline, baseline with { Priority = 43 });
        Assert.NotEqual(baseline, baseline with { Prepend = false });
        Assert.NotEqual(baseline, baseline with { NegateName = true });
        Assert.NotEqual(baseline, baseline with { NegateNamespace = true });
    }

    /// <summary>
    /// Equality is case-sensitive on the name and on the namespace, matching the comparers.
    /// </summary>
    /// <remarks>
    /// Consistency with <c>SubscriptionOptions.NameComparer</c> and <c>NamespaceComparer</c>, both of
    /// which are ordinal. If equality were case-insensitive while the comparers were not, a set keyed by
    /// topic and a list sorted by the comparer would disagree about how many distinct topics exist.
    /// </remarks>
    [Fact]
    public void EqualityIsCaseSensitiveOnNameAndNamespace()
    {
        SubscriptionTopic lower = new() { LegacyName = "itemchanged", NamespaceCriterion = "persistent" };

        Assert.NotEqual(lower, lower with { LegacyName = "ItemChanged" });
        Assert.NotEqual(lower, lower with { NamespaceCriterion = "Persistent" });
    }

    /// <summary>
    /// A null or unset legacy name is normalized to the empty string rather than retained.
    /// </summary>
    /// <remarks>
    /// The same coalescing initializer pattern as <c>SubscriptionOptions.Namespace</c>, and for the same
    /// reason: every consumer measures, compares and concatenates the name without a null check. Note the
    /// consequence - a null name makes <c>MatchesEveryName</c> true, so an unset name behaves as a
    /// match-everything filter.
    /// </remarks>
    [Fact]
    public void ANullLegacyNameIsNormalizedToEmpty()
    {
        SubscriptionTopic fromNull = new() { LegacyName = null! };

        Assert.Equal(string.Empty, fromNull.LegacyName);
        Assert.Equal(string.Empty, fromNull.LogicalName);
        Assert.Null(fromNull.Sequence);
        Assert.True(fromNull.MatchesEveryName);

        Assert.Equal(string.Empty, new SubscriptionTopic().LegacyName);
    }

    /// <summary>
    /// <see cref="SubscriptionTopic.ToString"/> reports the grammar and the legacy rendering, so a
    /// diagnostic shows which rules produced the topic.
    /// </summary>
    /// <remarks>
    /// The grammar tag is the useful part: the two grammars read the same characters differently, so a log
    /// line carrying only the text would be ambiguous - <c>"^name"</c> is a negated filter or a literal
    /// subscription name depending on which parser produced it.
    /// </remarks>
    [Fact]
    public void ToStringReportsTheGrammarAndTheLegacyRendering()
    {
        Assert.Equal("Subscription[0-itemchanged]", ParseSubscriptionOrFail("0-itemchanged").ToString());
        Assert.Equal("Filter[^itemchanged]", ParseFilterOrFail("^itemchanged").ToString());
        Assert.Equal("Filter[]", ParseFilterOrFail(string.Empty).ToString());
    }

    /// <summary>
    /// Neither parser throws for any input, and every failure is reported as
    /// <c>E_INVALID_ARGUMENT</c> with a null result.
    /// </summary>
    /// <remarks>
    /// The parsers are reached with caller-supplied strings, so a throw would be a denial of service on a
    /// mistyped topic. Asserting that the only failure code is <c>E_INVALID_ARGUMENT</c> also pins that
    /// there is no second failure mode a caller would have to distinguish - the legacy broker reports one
    /// code for a bad topic and so does this.
    /// </remarks>
    [Fact]
    public void NeitherParserThrowsAndFailuresAreAlwaysInvalidArgument()
    {
        string?[] candidates =
        [
            null, "", " ", ".", "..", "^", "^^", "-", "--", "!", "@", "%", "*", ":", "::",
            "-!*@%", "name", ".name", "name.", "name..", "^name", "name.^", "^.^",
            "0-", "-0", "999999999999999999999:name", "-2147483649:name", "+:name", "-:name",
            "name.ns.extra", "0-itemchanged.persistent", "\t", "\n", "多字节", "^多字节.持久",
        ];

        foreach (string? candidate in candidates)
        {
            long subscriptionCode = SubscriptionTopic.ParseSubscription(
                candidate,
                out SubscriptionTopic? subscription);

            Assert.True(
                subscriptionCode == RetCode.OK || subscriptionCode == RetCode.E_INVALID_ARGUMENT,
                $"ParseSubscription({candidate ?? "null"}) answered {subscriptionCode}, which is neither "
                    + "OK nor E_INVALID_ARGUMENT.");

            Assert.Equal(subscriptionCode == RetCode.OK, subscription is not null);

            long filterCode = SubscriptionTopic.ParseFilter(candidate, out SubscriptionTopic? filter);

            Assert.True(
                filterCode == RetCode.OK || filterCode == RetCode.E_INVALID_ARGUMENT,
                $"ParseFilter({candidate ?? "null"}) answered {filterCode}, which is neither OK nor "
                    + "E_INVALID_ARGUMENT.");

            Assert.Equal(filterCode == RetCode.OK, filter is not null);

            // Whatever parsed, every derived member is reachable without throwing.
            foreach (SubscriptionTopic? parsed in new[] { subscription, filter })
            {
                if (parsed is null)
                {
                    continue;
                }

                _ = parsed.Sequence;
                _ = parsed.LogicalName;
                _ = parsed.Namespace;
                _ = parsed.Lifetime;
                _ = parsed.HasNamespace;
                _ = parsed.HasNamespaceCriterion;
                _ = parsed.MatchesEveryName;
                _ = parsed.ToLegacyString();
                _ = parsed.ToSubscriptionOptions();
                _ = parsed.ToString();
                _ = parsed.GetHashCode();
                _ = parsed.Matches(null, null);
                _ = parsed.Matches("name", "persistent");
            }
        }
    }

    /// <summary>
    /// A namespace containing further separators keeps everything after the FIRST one, so a dotted
    /// namespace is possible.
    /// </summary>
    /// <remarks>
    /// The split is on the first separator rather than the last, which means a name cannot contain a dot
    /// but a namespace can. Worth pinning because the opposite choice is equally plausible and would make
    /// <c>"name.ns.extra"</c> a topic named <c>"name.ns"</c> in namespace <c>"extra"</c> - a different
    /// subscription entirely.
    /// </remarks>
    [Fact]
    public void TheNamespaceSplitTakesEverythingAfterTheFirstSeparator()
    {
        SubscriptionTopic parsed = ParseSubscriptionOrFail("name.ns.extra");

        Assert.Equal("name", parsed.LegacyName);
        Assert.Equal("ns.extra", parsed.Namespace);
        Assert.Equal(SubscriptionLifetime.Transient, parsed.Lifetime);
    }
}
