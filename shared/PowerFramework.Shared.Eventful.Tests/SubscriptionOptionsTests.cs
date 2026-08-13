// ==================================================================================================
//  SubscriptionOptionsTests.cs - THE SUBSCRIPTION GRAMMAR'S VOCABULARY
//  ------------------------------------------------------------------------------------------------
//  UNITS UNDER TEST  CaptureMode, Priorities, ModificationKind, TopicSymbols, SubscriptionLifetime,
//                    SubscriptionNamespaces, SubscriptionOptions
//  ORACLES           ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru   the broker
//                    ws_objects/pfw.thread.pbl.src/n_cst_threading.sru:L544,L596 the '.persistent' suffix
//                    ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L47-L76  12 live topics
//
//  WHAT THIS FILE IS ABOUT
//  ------------------------------------------------------------------------------------------------
//  The legacy broker encodes a subscription's ordering, capture mode, priority, insertion position and
//  lifetime as SYMBOLS inside one opaque topic string. These types are that vocabulary, pulled apart into
//  named values so the topic parser has something to produce and the broker has something to switch on.
//  The symbols themselves are therefore data, not implementation detail: every one appears in a legacy
//  topic literal somewhere in ws_objects, so changing a character changes which strings parse.
//
//  THE THREE COMPARERS ARE NOT INTERCHANGEABLE, AND THAT IS THE POINT
//  ------------------------------------------------------------------------------------------------
//  Two of them are ORDINAL and one is ORDINAL-IGNORE-CASE, and the asymmetry is deliberate:
//
//      NameComparer       Ordinal            a topic name is matched exactly; 'ItemChanged' is not
//                                            'itemchanged', because the broker's dispatch order derives
//                                            from a LEXICAL sort of these and case would reorder it
//      HandlerNameComparer OrdinalIgnoreCase PowerScript identifiers are case-insensitive, so a handler
//                                            registered as 'OnItemChanged' must be found by
//                                            'onitemchanged' - and HandlerName is FOLDED on the way in
//      NamespaceComparer  Ordinal            'persistent' is a fixed token, matched exactly
//
//  A single shared comparer would break one of the three. Asserted below in both directions - what each
//  comparer accepts AND what it rejects - because "they are all ordinal" is the mistake that compiles.
//
// ==================================================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace PowerFramework.Shared.Eventful.Tests;

/// <summary>
/// Characterization tests for the subscription vocabulary types.
/// </summary>
public class SubscriptionOptionsTests
{
    // ==============================================================================================
    //  1. CAPTURE MODE
    // ==============================================================================================

    /// <summary>
    /// <see cref="CaptureMode"/> is exactly three values, numbered from zero, with
    /// <see cref="CaptureMode.Unhandled"/> as the default.
    /// </summary>
    /// <remarks>
    /// The default matters more than the numbering. A subscription that names no capture symbol receives
    /// only UNHANDLED dispatches - the broker stops offering an event to further subscribers once one has
    /// handled it, unless they asked otherwise with <c>%</c> or <c>*</c>. So zero must mean "unhandled
    /// only", because that is what an unadorned topic string requests.
    /// </remarks>
    [Fact]
    public void CaptureModeIsThreeValuesDefaultingToUnhandled()
    {
        Assert.Equal(0, (int)CaptureMode.Unhandled);
        Assert.Equal(1, (int)CaptureMode.Handled);
        Assert.Equal(2, (int)CaptureMode.All);

        Assert.Equal(3, Enum.GetValues<CaptureMode>().Length);
        Assert.Equal(CaptureMode.Unhandled, default(CaptureMode));
    }

    // ==============================================================================================
    //  2. PRIORITIES
    // ==============================================================================================

    /// <summary>
    /// The three named priorities are the integer extremes and zero, so no numeric priority can outrank
    /// <see cref="Priorities.High"/> or fall below <see cref="Priorities.Low"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is why the symbolic priorities are the extremes rather than, say, 100 and -100. The topic
    /// grammar admits an explicit NUMERIC priority as well as the <c>!</c> and <c>@</c> symbols, so if
    /// High were any finite value a caller could write a number above it and outrank a subscriber that
    /// asked for the highest priority available. Pinning them at the extremes makes the symbols
    /// genuinely absolute.
    /// </para>
    /// <para>
    /// Asserted as an ordering as well as as values, because the ordering is what the broker relies on
    /// and it is the property a renumbering would break.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePriorityExtremesCannotBeOutrankedByAnyNumericPriority()
    {
        Assert.Equal(int.MinValue, Priorities.Low);
        Assert.Equal(0, Priorities.Normal);
        Assert.Equal(int.MaxValue, Priorities.High);

        Assert.True(Priorities.Low < Priorities.Normal);
        Assert.True(Priorities.Normal < Priorities.High);

        // There is no representable integer outside the pair.
        Assert.Equal(Priorities.High, Math.Max(Priorities.High, int.MaxValue));
        Assert.Equal(Priorities.Low, Math.Min(Priorities.Low, int.MinValue));
    }

    // ==============================================================================================
    //  3. MODIFICATION KIND
    // ==============================================================================================

    /// <summary>
    /// <see cref="ModificationKind"/> is numbered from ONE, so it has no zero member and therefore no
    /// meaningful default.
    /// </summary>
    /// <remarks>
    /// Deliberate, and the opposite convention from <see cref="CaptureMode"/>. The legacy values are 1
    /// and 2 and there is no third state, so a zero would be a value the broker cannot act on. The
    /// consequence is that <c>default(ModificationKind)</c> is NOT a legal value - asserted here so a
    /// caller that relies on field initialization discovers it in a test rather than by having a
    /// modification silently ignored.
    /// </remarks>
    [Fact]
    public void ModificationKindIsNumberedFromOneAndHasNoLegalDefault()
    {
        Assert.Equal(1, (int)ModificationKind.Off);
        Assert.Equal(2, (int)ModificationKind.Disable);

        Assert.Equal(2, Enum.GetValues<ModificationKind>().Length);

        Assert.False(
            Enum.IsDefined(default(ModificationKind)),
            "Zero is not a legal ModificationKind, so an uninitialized field is not a usable value.");
    }

    // ==============================================================================================
    //  4. TOPIC SYMBOLS
    // ==============================================================================================

    /// <summary>
    /// The eight grammar symbols are exactly the legacy characters.
    /// </summary>
    /// <remarks>
    /// Every one of these appears inside a topic literal in <c>ws_objects</c>, so they are data rather
    /// than an internal encoding: changing a character changes which of the framework's own topic strings
    /// parse. They are asserted against literals for the same reason the enum values are - a stored
    /// characterization recording contains the rendered topic string, not a symbolic name.
    /// </remarks>
    [Fact]
    public void TheEightGrammarSymbolsAreTheLegacyCharacters()
    {
        Assert.Equal('-', TopicSymbols.Prepend);
        Assert.Equal('@', TopicSymbols.Low);
        Assert.Equal('!', TopicSymbols.High);
        Assert.Equal(':', TopicSymbols.PriorityDelimiter);
        Assert.Equal('%', TopicSymbols.Handled);
        Assert.Equal('*', TopicSymbols.All);
        Assert.Equal('.', TopicSymbols.NamespaceSeparator);
        Assert.Equal('^', TopicSymbols.Negation);
    }

    /// <summary>
    /// The eight symbols are all DISTINCT, so no character carries two meanings.
    /// </summary>
    /// <remarks>
    /// A collision would make the grammar ambiguous at the point of parsing rather than at the point of
    /// declaration, which is much harder to diagnose. Cheap to assert and it catches a copy-paste error
    /// in the declarations that no individual value assertion would.
    /// </remarks>
    [Fact]
    public void TheEightSymbolsAreAllDistinct()
    {
        char[] symbols =
        [
            TopicSymbols.Prepend,
            TopicSymbols.Low,
            TopicSymbols.High,
            TopicSymbols.PriorityDelimiter,
            TopicSymbols.Handled,
            TopicSymbols.All,
            TopicSymbols.NamespaceSeparator,
            TopicSymbols.Negation,
        ];

        Assert.Equal(symbols.Length, symbols.Distinct().Count());
    }

    /// <summary>
    /// The leading-run set is exactly the five symbols that may appear before a topic's name, and it
    /// excludes the three that may not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The distinction is what terminates the parser's prefix loop. The five in the run modify HOW a
    /// subscription behaves and are consumed one at a time from the front; the three excluded ones are
    /// structural - the priority delimiter separates a numeric priority from the name, the namespace
    /// separator splits the name from its namespace, and the negation symbol belongs to the FILTER
    /// grammar rather than the subscription one.
    /// </para>
    /// <para>
    /// Asserted in both directions. Including a structural symbol in the run would make the parser
    /// swallow it and lose the boundary it marks; excluding one of the five would end the prefix scan
    /// early and treat the rest of the symbols as part of the name.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheLeadingRunIsTheFiveModifiersAndExcludesTheThreeStructuralSymbols()
    {
        char[] run = TopicSymbols.LeadingRunSymbols.ToArray();

        Assert.Equal(
            [TopicSymbols.Prepend, TopicSymbols.Handled, TopicSymbols.All, TopicSymbols.Low, TopicSymbols.High],
            run);

        foreach (char symbol in run)
        {
            Assert.True(TopicSymbols.IsLeadingRunSymbol(symbol), $"'{symbol}' must be a leading-run symbol.");
        }

        foreach (char structural in new[]
        {
            TopicSymbols.PriorityDelimiter,
            TopicSymbols.NamespaceSeparator,
            TopicSymbols.Negation,
        })
        {
            Assert.False(
                TopicSymbols.IsLeadingRunSymbol(structural),
                $"'{structural}' marks a boundary and must NOT be consumed as a leading-run symbol.");
        }
    }

    /// <summary>
    /// <see cref="TopicSymbols.IsLeadingRunSymbol"/> agrees with
    /// <see cref="TopicSymbols.LeadingRunSymbols"/> across the whole ASCII range.
    /// </summary>
    /// <remarks>
    /// The predicate is a hand-written pattern match and the span is a hand-written list, so they can
    /// drift apart - one updated and the other not. Sweeping the full ASCII range is the only way to
    /// prove they agree rather than merely coincide on the values a targeted test happens to try.
    /// </remarks>
    [Fact]
    public void ThePredicateAgreesWithTheSpanAcrossAllOfAscii()
    {
        HashSet<char> run = [.. TopicSymbols.LeadingRunSymbols];

        for (char candidate = (char)0; candidate < (char)128; candidate++)
        {
            Assert.Equal(run.Contains(candidate), TopicSymbols.IsLeadingRunSymbol(candidate));
        }

        // And a non-ASCII character is never a symbol.
        Assert.False(TopicSymbols.IsLeadingRunSymbol('数'));
    }

    // ==============================================================================================
    //  5. LIFETIME AND NAMESPACE
    // ==============================================================================================

    /// <summary>
    /// The persistent namespace token is the exact legacy spelling, and the empty string is the
    /// no-namespace value.
    /// </summary>
    /// <remarks>
    /// The token comes from the <c>.^persistent</c> suffix in <c>n_cst_threading.sru:L544,L596</c>. It is
    /// a fixed literal rather than a convention, so its spelling is part of the contract: a subscription
    /// whose namespace is <c>Persistent</c> with a capital letter is a DIFFERENT, transient namespace,
    /// which the case-sensitivity test below pins.
    /// </remarks>
    [Fact]
    public void TheNamespaceTokensAreTheExactLegacySpellings()
    {
        Assert.Equal("persistent", SubscriptionNamespaces.Persistent);
        Assert.Equal(SubscriptionNamespaces.None, string.Empty);
    }

    /// <summary>
    /// Only the exact token <c>persistent</c> maps to
    /// <see cref="SubscriptionLifetime.Persistent"/>; everything else, including null and case variants,
    /// is transient.
    /// </summary>
    /// <param name="candidate">The namespace text.</param>
    /// <param name="expectPersistent">Whether it should map to the persistent lifetime.</param>
    /// <remarks>
    /// <para>
    /// The case rows are the substantive ones. The comparison is ORDINAL, so <c>Persistent</c> and
    /// <c>PERSISTENT</c> are ordinary user namespaces that happen to look like the framework token - and
    /// a subscription in one of them is NOT protected from a lifetime sweep. That is a sharp edge, and it
    /// is the legacy's edge: PowerScript string comparison is case-sensitive, so the framework's own
    /// suffix matching is too.
    /// </para>
    /// <para>
    /// The null row matters because a namespace is optional throughout: <c>NamespaceCriterion</c> is
    /// nullable and an absent criterion must read as transient rather than throwing.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("persistent", true)]
    [InlineData("Persistent", false)]
    [InlineData("PERSISTENT", false)]
    [InlineData("persistent ", false)]
    [InlineData(" persistent", false)]
    [InlineData("persistents", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("anything", false)]
    public void OnlyTheExactTokenIsPersistent(string? candidate, bool expectPersistent)
    {
        SubscriptionLifetime expected = expectPersistent
            ? SubscriptionLifetime.Persistent
            : SubscriptionLifetime.Transient;

        Assert.Equal(expected, SubscriptionNamespaces.ToLifetime(candidate));
        Assert.Equal(expectPersistent, SubscriptionNamespaces.IsPersistent(candidate));
    }

    /// <summary>
    /// The lifetime enumeration is two values with transient as the default.
    /// </summary>
    /// <remarks>
    /// Transient must be the default because a subscription with no namespace is transient, and no
    /// namespace is the common case. The alternative - persistent by default - would make every ordinary
    /// subscription survive the sweep that is supposed to clear it.
    /// </remarks>
    [Fact]
    public void TheLifetimeEnumerationIsTwoValuesDefaultingToTransient()
    {
        Assert.Equal(0, (int)SubscriptionLifetime.Transient);
        Assert.Equal(1, (int)SubscriptionLifetime.Persistent);

        Assert.Equal(2, Enum.GetValues<SubscriptionLifetime>().Length);
        Assert.Equal(SubscriptionLifetime.Transient, default(SubscriptionLifetime));
    }

    // ==============================================================================================
    //  6. THE OPTIONS RECORD
    // ==============================================================================================

    /// <summary>
    /// A default-constructed options record carries the unadorned-topic behaviour: unhandled capture,
    /// normal priority, append, no namespace, no handler.
    /// </summary>
    /// <remarks>
    /// These are the values a topic string with no symbols at all requests, so the defaults ARE the
    /// grammar's base case. Pinned as a set rather than one at a time because it is the combination that
    /// has to be right: a topic like <c>"itemchanged"</c> must produce exactly this record.
    /// </remarks>
    [Fact]
    public void TheDefaultRecordIsTheUnadornedTopicBehaviour()
    {
        SubscriptionOptions options = new();

        Assert.Equal(CaptureMode.Unhandled, options.Capture);
        Assert.Equal(Priorities.Normal, options.Priority);
        Assert.False(options.Prepend);
        Assert.Equal(SubscriptionNamespaces.None, options.Namespace);
        Assert.Equal(string.Empty, options.HandlerName);
        Assert.Equal(SubscriptionLifetime.Transient, options.Lifetime);
        Assert.False(options.HasNamespace);
    }

    /// <summary>
    /// <see cref="SubscriptionOptions.Default"/> is a shared singleton equal to a freshly constructed
    /// record.
    /// </summary>
    /// <remarks>
    /// Both halves are asserted. It must be the same instance on every read, because a caller may compare
    /// against it by reference; and it must be VALUE-equal to a new record, because a caller may also
    /// compare structurally, and a singleton that had drifted from the constructor's defaults would make
    /// the two comparisons disagree.
    /// </remarks>
    [Fact]
    public void TheSharedDefaultIsOneInstanceEqualToAFreshRecord()
    {
        Assert.Same(SubscriptionOptions.Default, SubscriptionOptions.Default);
        Assert.Equal(new SubscriptionOptions(), SubscriptionOptions.Default);
    }

    /// <summary>
    /// A null namespace is normalized to the empty string rather than retained, so
    /// <see cref="SubscriptionOptions.Namespace"/> is never null.
    /// </summary>
    /// <remarks>
    /// The record's initializer coalesces on the way in. That is what lets every consumer treat the
    /// namespace as a plain string - comparing it, measuring it, concatenating it into a rendered topic -
    /// without a null check at each site. Asserted because the property's declared type does not say so:
    /// it is <c>string</c>, and the guarantee lives in the initializer.
    /// </remarks>
    [Fact]
    public void ANullNamespaceIsNormalizedToEmpty()
    {
        SubscriptionOptions options = new() { Namespace = null! };

        Assert.Equal(string.Empty, options.Namespace);
        Assert.False(options.HasNamespace);
        Assert.Equal(SubscriptionLifetime.Transient, options.Lifetime);
    }

    /// <summary>
    /// The handler name is FOLDED to lower case on assignment, and a null one becomes empty.
    /// </summary>
    /// <param name="assigned">The handler name as assigned.</param>
    /// <param name="expected">The folded value the record stores.</param>
    /// <remarks>
    /// <para>
    /// PowerScript identifiers are case-insensitive, so a handler registered as <c>OnItemChanged</c> must
    /// be found when the broker looks for <c>onitemchanged</c>. Folding at the boundary - rather than
    /// comparing case-insensitively at every lookup - is what makes the stored key canonical, and it is
    /// why the folding is observable: the record returns the folded value, not what was assigned.
    /// </para>
    /// <para>
    /// The fold is INVARIANT rather than culture-sensitive. That is load-bearing: a Turkish culture folds
    /// <c>I</c> to a dotless <c>ı</c>, so a culture-sensitive fold would store a different key on a
    /// Turkish host and fail every lookup for any handler with an uppercase I in its name.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("OnItemChanged", "onitemchanged")]
    [InlineData("onitemchanged", "onitemchanged")]
    [InlineData("ONITEMCHANGED", "onitemchanged")]
    [InlineData("On_Item_Changed", "on_item_changed")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void TheHandlerNameIsFoldedToLowerCaseOnAssignment(string? assigned, string expected)
    {
        SubscriptionOptions options = new() { HandlerName = assigned! };

        Assert.Equal(expected, options.HandlerName);
    }

    /// <summary>
    /// The handler-name fold is INVARIANT, so it produces the same key under a culture with different
    /// casing rules.
    /// </summary>
    /// <remarks>
    /// Turkish is the standard probe: its <c>I</c> folds to a dotless <c>ı</c> rather than <c>i</c>. A
    /// culture-sensitive fold would therefore store <c>onıtemchanged</c> on a Turkish host, and every
    /// lookup for <c>onitemchanged</c> would miss - a defect that appears only in one locale and never in
    /// CI. Asserted by switching the culture rather than by inspecting the implementation.
    /// </remarks>
    [Fact]
    public void TheHandlerNameFoldIsCultureInvariant()
    {
        System.Globalization.CultureInfo previous = System.Globalization.CultureInfo.CurrentCulture;

        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new("tr-TR");

            SubscriptionOptions options = new() { HandlerName = "OnItemChanged" };

            Assert.Equal("onitemchanged", options.HandlerName);
            Assert.DoesNotContain("ı", options.HandlerName, StringComparison.Ordinal);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>
    /// <see cref="SubscriptionOptions.Lifetime"/> and
    /// <see cref="SubscriptionOptions.HasNamespace"/> are projections of the namespace, not independently
    /// settable state.
    /// </summary>
    /// <remarks>
    /// Being derived is what keeps them consistent: there is no way to construct a record that claims to
    /// be persistent while carrying a different namespace. Asserted by setting the namespace and reading
    /// both projections, including the case-variant row that is persistent-looking but transient.
    /// </remarks>
    [Fact]
    public void LifetimeAndHasNamespaceAreProjectionsOfTheNamespace()
    {
        Assert.Equal(
            SubscriptionLifetime.Persistent,
            new SubscriptionOptions { Namespace = SubscriptionNamespaces.Persistent }.Lifetime);

        Assert.True(new SubscriptionOptions { Namespace = SubscriptionNamespaces.Persistent }.HasNamespace);

        SubscriptionOptions lookalike = new() { Namespace = "Persistent" };
        Assert.True(lookalike.HasNamespace);
        Assert.Equal(SubscriptionLifetime.Transient, lookalike.Lifetime);

        SubscriptionOptions none = new() { Namespace = SubscriptionNamespaces.None };
        Assert.False(none.HasNamespace);
        Assert.Equal(SubscriptionLifetime.Transient, none.Lifetime);

        // Neither projection has a setter.
        Assert.Null(typeof(SubscriptionOptions).GetProperty(nameof(SubscriptionOptions.Lifetime))!.SetMethod);
        Assert.Null(typeof(SubscriptionOptions).GetProperty(nameof(SubscriptionOptions.HasNamespace))!.SetMethod);
    }

    /// <summary>
    /// The record has value equality over all five settable members, and the <c>with</c> expression
    /// produces an independent copy.
    /// </summary>
    /// <remarks>
    /// Value equality is why it is a record: the broker compares subscription options structurally when
    /// deciding whether a modification targets an existing subscription. The <c>with</c> half matters
    /// because the folding and coalescing initializers must run on a copy too - a copy that bypassed them
    /// could hold an unfolded handler name, which no lookup would then find.
    /// </remarks>
    [Fact]
    public void TheRecordHasValueEqualityAndWithRunsTheInitializers()
    {
        SubscriptionOptions original = new()
        {
            Capture = CaptureMode.All,
            Priority = 42,
            Prepend = true,
            Namespace = "custom",
            HandlerName = "OnSomething",
        };

        SubscriptionOptions identical = new()
        {
            Capture = CaptureMode.All,
            Priority = 42,
            Prepend = true,
            Namespace = "custom",
            HandlerName = "onsomething",
        };

        Assert.Equal(original, identical);
        Assert.Equal(original.GetHashCode(), identical.GetHashCode());

        // Each member participates: changing any one breaks equality.
        Assert.NotEqual(original, original with { Capture = CaptureMode.Handled });
        Assert.NotEqual(original, original with { Priority = 43 });
        Assert.NotEqual(original, original with { Prepend = false });
        Assert.NotEqual(original, original with { Namespace = "other" });
        Assert.NotEqual(original, original with { HandlerName = "OnOther" });

        // The copy runs the folding initializer.
        SubscriptionOptions copied = original with { HandlerName = "OnRenamed" };
        Assert.Equal("onrenamed", copied.HandlerName);

        // ...and the coalescing one.
        Assert.Equal(string.Empty, (original with { Namespace = null! }).Namespace);
    }

    // ==============================================================================================
    //  7. THE THREE COMPARERS
    // ==============================================================================================

    /// <summary>
    /// The name comparer is CASE-SENSITIVE, so two topic names differing only in case are different
    /// topics.
    /// </summary>
    /// <remarks>
    /// The broker's dispatch order derives from a lexical sort of subscription names, so case-insensitive
    /// comparison would not merely conflate two topics - it would change the ORDER in which subscribers
    /// run, because ordinal and ignore-case sorts interleave differently. That makes this the one comparer
    /// whose case sensitivity has an ordering consequence as well as an identity one.
    /// </remarks>
    [Fact]
    public void TheNameComparerIsCaseSensitive()
    {
        Assert.NotEqual(0, SubscriptionOptions.NameComparer.Compare("ItemChanged", "itemchanged"));
        Assert.False(SubscriptionOptions.NameComparer.Equals("ItemChanged", "itemchanged"));
        Assert.True(SubscriptionOptions.NameComparer.Equals("itemchanged", "itemchanged"));
        Assert.Same(StringComparer.Ordinal, SubscriptionOptions.NameComparer);
    }

    /// <summary>
    /// The handler-name comparer is CASE-INSENSITIVE, matching PowerScript's case-insensitive
    /// identifiers.
    /// </summary>
    /// <remarks>
    /// The one comparer that differs, and the reason the three are separate properties rather than one
    /// shared value. A handler is a PowerScript function name, and the language does not distinguish case
    /// in identifiers - so the broker must find <c>OnItemChanged</c> when asked for
    /// <c>onitemchanged</c>. Note this comparer is belt-and-braces given the record already FOLDS the
    /// name on assignment; it exists for comparing against names that did not come through the record.
    /// </remarks>
    [Fact]
    public void TheHandlerNameComparerIsCaseInsensitive()
    {
        Assert.Equal(0, SubscriptionOptions.HandlerNameComparer.Compare("OnItemChanged", "onitemchanged"));
        Assert.True(SubscriptionOptions.HandlerNameComparer.Equals("OnItemChanged", "onitemchanged"));
        Assert.True(SubscriptionOptions.HandlerNameComparer.Equals("ONITEMCHANGED", "onitemchanged"));
        Assert.False(SubscriptionOptions.HandlerNameComparer.Equals("OnItemChanged", "OnItemChange"));
        Assert.Same(StringComparer.OrdinalIgnoreCase, SubscriptionOptions.HandlerNameComparer);
    }

    /// <summary>
    /// The namespace comparer is CASE-SENSITIVE, which is what makes the persistent token exact.
    /// </summary>
    /// <remarks>
    /// The complement of the persistence theory above, stated at the comparer rather than at the
    /// predicate. Both must agree - a case-insensitive comparer here with a case-sensitive
    /// <c>IsPersistent</c> would let a namespace compare equal to <c>persistent</c> while not being
    /// treated as persistent, which is the kind of inconsistency that produces a leak in a lifetime
    /// sweep.
    /// </remarks>
    [Fact]
    public void TheNamespaceComparerIsCaseSensitiveAndAgreesWithThePersistenceCheck()
    {
        Assert.False(SubscriptionOptions.NamespaceComparer.Equals("Persistent", "persistent"));
        Assert.True(SubscriptionOptions.NamespaceComparer.Equals("persistent", "persistent"));
        Assert.Same(StringComparer.Ordinal, SubscriptionOptions.NamespaceComparer);

        // The comparer and the predicate agree on every case variant.
        foreach (string candidate in new[] { "persistent", "Persistent", "PERSISTENT", "pErSiStEnT" })
        {
            Assert.Equal(
                SubscriptionOptions.NamespaceComparer.Equals(candidate, SubscriptionNamespaces.Persistent),
                SubscriptionNamespaces.IsPersistent(candidate));
        }
    }

    /// <summary>
    /// The three comparers are not all the same object: exactly one of them differs.
    /// </summary>
    /// <remarks>
    /// The asymmetry asserted as a whole, because the individual tests above could all pass while a
    /// refactor had unified them - each one would simply be checking the unified comparer's behaviour
    /// against its own expectations, and two of those expectations would then be wrong. Stating that
    /// exactly two of the three are the same object is the compact form of "the asymmetry is intentional".
    /// </remarks>
    [Fact]
    public void ExactlyOneOfTheThreeComparersDiffers()
    {
        StringComparer[] comparers =
        [
            SubscriptionOptions.NameComparer,
            SubscriptionOptions.HandlerNameComparer,
            SubscriptionOptions.NamespaceComparer,
        ];

        Assert.Equal(2, comparers.Distinct().Count());
        Assert.Same(SubscriptionOptions.NameComparer, SubscriptionOptions.NamespaceComparer);
        Assert.NotSame(SubscriptionOptions.NameComparer, SubscriptionOptions.HandlerNameComparer);
    }
}
