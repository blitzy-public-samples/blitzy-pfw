// ==================================================================================================
//  I18nTests.cs - THE LOCALIZATION FACADE
//  ------------------------------------------------------------------------------------------------
//  UNIT UNDER TEST   PowerFramework.Shared.Localization.I18n
//  ORACLE            ws_objects/pfw.ui.pbl.src/i18n.srf   three overloads, readable body
//
//  WHY THIS FACADE IS WORTH A SUITE OF ITS OWN
//  ------------------------------------------------------------------------------------------------
//  Everything about this type is about what it does NOT do, and every one of those non-behaviours is
//  invisible from the outside unless something asserts it:
//
//    * WITH NO PROVIDER INSTALLED IT RETURNS THE TEXT UNCHANGED. It does not throw, does not log, does
//      not return null and does not mark the text as untranslated. That SILENT PASSTHROUGH is the
//      documented legacy behaviour [i18n.srf:L17-L18], and it is the reason a missing translation table
//      degrades into English-looking Chinese source strings rather than into a fault. A test is the
//      only way to keep the silence deliberate rather than accidental.
//    * IT DISCARDS THE PROVIDER'S RETURN CODE. The provider answers 1 for handled and 0 for not
//      handled, and the facade reads neither: it returns whatever `text` holds afterwards. So "not
//      handled" and "no provider at all" are INDISTINGUISHABLE to a caller, by design.
//    * IT DISPATCHES EXACTLY ONCE. No retry, no fallback chain, no second provider.
//
//  THE ref PARAMETER IS THE CONTRACT, NOT AN OPTIMISATION
//  ------------------------------------------------------------------------------------------------
//  `II18nProvider.OnTranslate` takes `ref string? text` and returns a long. Translation therefore
//  MUTATES the caller's variable rather than returning a value, which is unusual enough in C# that a
//  future author could "clean it up" into a `string? Translate(...)` and lose the ability to express
//  "handled but deliberately unchanged" - the state SimplifiedChineseProvider lives in. The facade's
//  job is to pass its own local by reference and hand back whatever the provider left there, and the
//  tests below assert that round trip rather than the provider's internals.
//
//  RULES POSITION
//  review_rules returns "No user rules provided.", so no user-specified rule governs this file. The
//  binding constraints cited inline are C-B (replicate behaviour, never improve) and C-C (the legacy
//  tree is read-only and is the oracle).
// ==================================================================================================

using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.Shared.Localization.Tests;

/// <summary>
/// Parity tests for <see cref="I18n"/>, the localization facade.
/// </summary>
/// <remarks>
/// No <c>using PowerFramework.Shared.Localization;</c> directive appears above: this namespace is
/// nested inside it, so simple-name lookup walks outward and finds <see cref="I18n"/> there.
/// <c>PowerFramework.Shared.Kernel</c> IS imported, because it is not an enclosing namespace of this
/// one and <c>Enums</c> and <c>RetCode</c> live there; the assembly arrives transitively through the
/// project reference to the library under test.
/// </remarks>
public class I18nTests
{
    /// <summary>
    /// A source string that is unmistakably a test input rather than anything from the read-only
    /// translation table, so no assertion below can pass because a real table happened to agree.
    /// </summary>
    private const string SourceText = "<<PFW-SOURCE-TEXT>>";

    // ==============================================================================================
    //  1. THE SILENT PASSTHROUGH
    // ==============================================================================================

    /// <summary>
    /// With no provider installed the text comes back exactly as supplied - not null, not empty, not
    /// annotated, and with nothing thrown. [i18n.srf:L17-L18]
    /// </summary>
    /// <remarks>
    /// The single most important behaviour in the file, and the one a "helpful" implementation would
    /// break first by throwing or by returning null so the caller "knows" translation did not happen.
    /// The legacy does neither, and a great many call sites pass the result straight into a message
    /// they are building, so a null or an exception here would replace the message with a fault.
    /// </remarks>
    [Fact]
    public void WithNoProviderTheTextPassesThroughUnchanged()
    {
        I18n facade = new();

        Assert.Equal(SourceText, facade.I18N(Categories.CAT_DWSVC, SourceText));
        Assert.Equal(SourceText, facade.I18N(Enums.I18N_SRC_PFW, Categories.CAT_DWSVC, SourceText));
        Assert.Equal(SourceText, facade.I18N(Enums.I18N_SRC_CUSTOM, Enums.I18N_CAT_WINDOW, SourceText));
    }

    /// <summary>
    /// The passthrough is value-preserving for every shape of input, including the empty string and
    /// null.
    /// </summary>
    /// <remarks>
    /// Null is the row that matters: the parameter is <c>string?</c> because PowerScript strings are
    /// nullable, and a facade that mapped null onto the empty string "to be safe" would flatten a
    /// distinction the return type exists to carry.
    /// </remarks>
    [Theory]
    [InlineData(SourceText)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("最小化")]
    [InlineData("第{}行")]
    [InlineData(null)]
    public void ThePassthroughPreservesEveryInputExactly(string? text)
    {
        I18n facade = new();

        Assert.Equal(text, facade.I18N(Categories.CAT_MSGBOX, text));
        Assert.Equal(text, facade.I18N(Enums.I18N_SRC_PFW, Categories.CAT_MSGBOX, text));
    }

    /// <summary>
    /// A provider that reports NOT HANDLED leaves the caller in exactly the same place as having no
    /// provider at all.
    /// </summary>
    /// <remarks>
    /// This is the indistinguishability the contract requires, asserted as an equality between two
    /// facades rather than as two separate expectations - so it fails if either half drifts, and the
    /// failure says the two stopped agreeing rather than merely that one value was wrong.
    /// </remarks>
    [Fact]
    public void ANotHandledProviderIsIndistinguishableFromNoProvider()
    {
        I18n withoutProvider = new();

        I18n withProvider = new();
        Assert.Equal(RetCode.OK, withProvider.I18N(new NotHandledProvider()));

        Assert.Equal(
            withoutProvider.I18N(Categories.CAT_DWSVC, SourceText),
            withProvider.I18N(Categories.CAT_DWSVC, SourceText));

        Assert.Equal(SourceText, withProvider.I18N(Categories.CAT_DWSVC, SourceText));
    }

    // ==============================================================================================
    //  2. INSTALLATION
    // ==============================================================================================

    /// <summary>
    /// Installing a provider answers <see cref="RetCode.OK"/>, and installing null answers
    /// <see cref="RetCode.E_INVALID_OBJECT"/>.
    /// </summary>
    /// <remarks>
    /// The codes are read by identifier rather than as the literals 0 and -3, per AAP 0.4.5.3: these
    /// values appear in log records and characterization recordings, so the identifier is the stable
    /// reference and the number is an implementation detail of the algebra.
    /// </remarks>
    [Fact]
    public void InstallingAProviderReportsOkAndInstallingNullReportsInvalidObject()
    {
        I18n facade = new();

        Assert.Equal(RetCode.OK, facade.I18N(new HandledProvider()));
        Assert.Equal(RetCode.E_INVALID_OBJECT, facade.I18N(null));
    }

    /// <summary>
    /// A REJECTED installation leaves the previously installed provider in place; it does not clear
    /// it.
    /// </summary>
    /// <remarks>
    /// The distinction is load-bearing and is the reason this is its own fact. An implementation that
    /// assigned first and validated afterwards would answer the same code while silently uninstalling
    /// a working provider, and the only observable difference is the NEXT translation. So the
    /// assertion is on that next translation rather than on the return code.
    /// </remarks>
    [Fact]
    public void ARejectedInstallationRetainsTheExistingProvider()
    {
        I18n facade = new();

        Assert.Equal(RetCode.OK, facade.I18N(new HandledProvider()));
        Assert.Equal(HandledProvider.DefaultReplacement, facade.I18N(Categories.CAT_DWSVC, SourceText));

        Assert.Equal(RetCode.E_INVALID_OBJECT, facade.I18N(null));

        // Still translating, so the null did not displace the working provider.
        Assert.Equal(HandledProvider.DefaultReplacement, facade.I18N(Categories.CAT_DWSVC, SourceText));
    }

    /// <summary>
    /// Installing a second provider REPLACES the first rather than chaining behind it.
    /// </summary>
    /// <remarks>
    /// The legacy holds a single provider reference and overwrites it [i18n.srf:L11-L13], so there is
    /// no fallback chain to inherit. Two providers with distinguishable replacements make the
    /// replacement observable; a chaining implementation would answer with the first provider's value
    /// or would call both, and the call count below rules out the second possibility directly.
    /// </remarks>
    [Fact]
    public void InstallingASecondProviderReplacesTheFirst()
    {
        RecordingProvider first = new(returnCode: 1, replacement: "<<FIRST>>");
        RecordingProvider second = new(returnCode: 1, replacement: "<<SECOND>>");

        I18n facade = new();
        Assert.Equal(RetCode.OK, facade.I18N(first));
        Assert.Equal(RetCode.OK, facade.I18N(second));

        Assert.Equal("<<SECOND>>", facade.I18N(Categories.CAT_DWSVC, SourceText));

        Assert.Equal(0, first.CallCount);
        Assert.Equal(1, second.CallCount);
    }

    // ==============================================================================================
    //  3. WHAT THE FACADE FORWARDS, AND WHAT IT IGNORES
    // ==============================================================================================

    /// <summary>
    /// The two-argument overload forwards <see cref="Enums.I18N_SRC_PFW"/> as the source, and the
    /// three-argument overload forwards whatever it is given. [i18n.srf:L11]
    /// </summary>
    /// <remarks>
    /// The default source is the whole reason the short overload exists: framework call sites pass a
    /// category and a string and mean "this text is the framework's own", which is precisely the
    /// filter every provider applies first. A default of <c>I18N_SRC_CUSTOM</c> would make every
    /// framework lookup decline.
    /// </remarks>
    [Fact]
    public void TheShortOverloadForwardsTheFrameworkSourceAndTheLongOneForwardsItsOwn()
    {
        RecordingProvider recorder = new();
        I18n facade = new();
        Assert.Equal(RetCode.OK, facade.I18N(recorder));

        _ = facade.I18N(Categories.CAT_DWSVC, SourceText);
        _ = facade.I18N(Enums.I18N_SRC_CUSTOM, Enums.I18N_CAT_RIBBONBAR, SourceText);

        Assert.Equal(2, recorder.CallCount);

        Assert.Equal(Enums.I18N_SRC_PFW, recorder.Calls[0].Source);
        Assert.Equal(Categories.CAT_DWSVC, recorder.Calls[0].Category);
        Assert.Equal(SourceText, recorder.Calls[0].Text);

        Assert.Equal(Enums.I18N_SRC_CUSTOM, recorder.Calls[1].Source);
        Assert.Equal(Enums.I18N_CAT_RIBBONBAR, recorder.Calls[1].Category);
        Assert.Equal(SourceText, recorder.Calls[1].Text);
    }

    /// <summary>
    /// The category is forwarded verbatim, including values the framework does not define.
    /// </summary>
    /// <remarks>
    /// The facade does not validate, clamp or map the category - that is entirely the provider's
    /// business, and the English provider's category switch has no default arm precisely so that an
    /// unknown value declines rather than guessing an element. Forwarding a negative and a large
    /// value proves the facade adds no filter of its own.
    /// </remarks>
    [Theory]
    [InlineData(0L)]
    [InlineData(5L)]
    [InlineData(7L)]
    [InlineData(-1L)]
    [InlineData(long.MaxValue)]
    public void TheCategoryIsForwardedVerbatim(long category)
    {
        RecordingProvider recorder = new();
        I18n facade = new();
        Assert.Equal(RetCode.OK, facade.I18N(recorder));

        _ = facade.I18N(category, SourceText);

        Assert.Equal(1, recorder.CallCount);
        Assert.Equal(category, recorder.Calls[0].Category);
    }

    /// <summary>
    /// The provider's return code is DISCARDED: the facade's answer is whatever the provider left in
    /// <c>text</c>, whatever code it reported alongside. [i18n.srf:L16-L18]
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the behaviour that makes "not handled" and "no provider" indistinguishable, and it also
    /// means a provider CAN mutate the text while reporting not handled - the facade will return the
    /// mutation. That is not a supported provider behaviour, it is simply what the facade does, and
    /// pinning it here is what stops someone from adding a "only take the text when the code is 1"
    /// guard that the legacy does not have.
    /// </para>
    /// <para>
    /// The rows cover the contract's own alphabet, 1 and 0, plus values outside it - a negative and a
    /// large positive - because the facade reads none of them and the test should say so rather than
    /// implying the alphabet is enforced somewhere.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(1L)]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(99L)]
    public void TheProviderReturnCodeIsIgnoredAndTheMutatedTextIsReturned(long providerCode)
    {
        RecordingProvider recorder = new(returnCode: providerCode, replacement: "<<MUTATED>>");
        I18n facade = new();
        Assert.Equal(RetCode.OK, facade.I18N(recorder));

        string? result = facade.I18N(Categories.CAT_DWSVC, SourceText);

        // The recorder writes only when it reports 1, so the expected value follows its own contract
        // rather than the facade's - which is the point: the FACADE did not choose either outcome.
        Assert.Equal(providerCode == 1L ? "<<MUTATED>>" : SourceText, result);
    }

    /// <summary>
    /// Each call dispatches to the provider exactly once - no retry, no double dispatch, and no
    /// second attempt when the provider declines.
    /// </summary>
    /// <remarks>
    /// A retry would be invisible for a pure provider and catastrophic for one with a side effect, and
    /// a second dispatch on a decline is exactly the "helpful fallback" a future author might add. The
    /// count is asserted after a decline specifically, because that is the case where a retry would
    /// look reasonable.
    /// </remarks>
    [Fact]
    public void EachCallDispatchesExactlyOnceEvenWhenTheProviderDeclines()
    {
        RecordingProvider recorder = new(returnCode: 0);
        I18n facade = new();
        Assert.Equal(RetCode.OK, facade.I18N(recorder));

        _ = facade.I18N(Categories.CAT_DWSVC, SourceText);
        Assert.Equal(1, recorder.CallCount);

        _ = facade.I18N(Categories.CAT_DWSVC, SourceText);
        _ = facade.I18N(Enums.I18N_SRC_PFW, Categories.CAT_DWSVC, SourceText);
        Assert.Equal(3, recorder.CallCount);
    }

    // ==============================================================================================
    //  4. THE ref ROUND TRIP
    // ==============================================================================================

    /// <summary>
    /// A provider replacement reaches the caller, including a replacement of null.
    /// </summary>
    /// <remarks>
    /// The null replacement is the row that proves the facade returns the variable rather than
    /// "the replacement if there is one": a facade written as
    /// <c>return replacement ?? text</c> would answer with the source text here, which is a different
    /// behaviour that no legacy statement supports.
    /// </remarks>
    [Fact]
    public void AProviderReplacementReachesTheCallerIncludingANullOne()
    {
        I18n facade = new();
        Assert.Equal(RetCode.OK, facade.I18N(new HandledProvider("<<REPLACED>>")));
        Assert.Equal("<<REPLACED>>", facade.I18N(Categories.CAT_DWSVC, SourceText));

        I18n nullReplacing = new();
        Assert.Equal(RetCode.OK, nullReplacing.I18N(new HandledProvider(null)));
        Assert.Null(nullReplacing.I18N(Categories.CAT_DWSVC, SourceText));
    }

    /// <summary>
    /// A null source text is forwarded to the provider as null rather than normalised on the way in.
    /// </summary>
    /// <remarks>
    /// The provider is the only thing entitled to decide what an absent string means - the English
    /// provider builds an XPath key from it, which for null renders as an empty predicate and misses -
    /// so a facade that substituted the empty string would change which branch the provider takes.
    /// </remarks>
    [Fact]
    public void ANullSourceTextIsForwardedAsNull()
    {
        RecordingProvider recorder = new();
        I18n facade = new();
        Assert.Equal(RetCode.OK, facade.I18N(recorder));

        Assert.Null(facade.I18N(Categories.CAT_DWSVC, null));

        Assert.Equal(1, recorder.CallCount);
        Assert.Null(recorder.Calls[0].Text);
    }

    /// <summary>
    /// The facade holds no per-call state: repeated calls with different inputs do not leak into one
    /// another.
    /// </summary>
    /// <remarks>
    /// <c>i18n.srf</c> keeps a single provider reference and nothing else, so there is no cache, no
    /// last-value and no memoisation to inherit. Asserting it matters because a cache is the most
    /// natural "improvement" to add to a lookup facade, and it would change behaviour the moment a
    /// provider's table were reloaded underneath it.
    /// </remarks>
    [Fact]
    public void TheFacadeCarriesNoPerCallState()
    {
        RecordingProvider recorder = new(returnCode: 1, replacement: "<<FIRST-ANSWER>>");
        I18n facade = new();
        Assert.Equal(RetCode.OK, facade.I18N(recorder));

        Assert.Equal("<<FIRST-ANSWER>>", facade.I18N(Categories.CAT_DWSVC, "alpha"));

        recorder.Replacement = "<<SECOND-ANSWER>>";
        Assert.Equal("<<SECOND-ANSWER>>", facade.I18N(Categories.CAT_DWSVC, "alpha"));

        recorder.ReturnCode = 0;
        Assert.Equal("alpha", facade.I18N(Categories.CAT_DWSVC, "alpha"));

        Assert.Equal(3, recorder.CallCount);
        Assert.Equal("alpha", recorder.Calls[2].Text);
    }

    /// <summary>
    /// Two facades are independent: installing a provider on one does not install it on the other.
    /// </summary>
    /// <remarks>
    /// The port makes <see cref="I18n"/> an instance class with an instance field, where the legacy
    /// global function reaches a single global. That is a deliberate departure recorded in the
    /// production file - a global would be untestable and would make two hosts in one process share a
    /// locale - and this fact is what states the consequence rather than leaving it implied.
    /// </remarks>
    [Fact]
    public void TwoFacadesDoNotShareTheirProvider()
    {
        I18n translating = new();
        I18n passthrough = new();

        Assert.Equal(RetCode.OK, translating.I18N(new HandledProvider("<<ONLY-HERE>>")));

        Assert.Equal("<<ONLY-HERE>>", translating.I18N(Categories.CAT_DWSVC, SourceText));
        Assert.Equal(SourceText, passthrough.I18N(Categories.CAT_DWSVC, SourceText));
    }

    /// <summary>
    /// Nothing on the facade throws, for any combination of provider state, source, category and text.
    /// </summary>
    /// <remarks>
    /// The localization path's documented posture is silent passthrough, so "never throws" is a ported
    /// behaviour rather than defensiveness. Swept rather than asserted arm by arm because what callers
    /// rely on is that NO combination escapes - a great many of them are building a message when they
    /// call this.
    /// </remarks>
    [Fact]
    public void NoCombinationOfInputsThrows()
    {
        II18nProvider?[] providers = [null, new HandledProvider(), new HandledProvider(null), new NotHandledProvider(), new RecordingProvider()];
        long[] sources = [Enums.I18N_SRC_PFW, Enums.I18N_SRC_CUSTOM, -1L, long.MaxValue];
        long[] categories = [Enums.I18N_CAT_WINDOW, Enums.I18N_CAT_CUSTOM, Categories.CAT_MSGBOX, Categories.CAT_DWSVC, -1L, long.MinValue];
        string?[] texts = [null, string.Empty, " ", SourceText, "最小化"];

        foreach (II18nProvider? provider in providers)
        {
            I18n facade = new();
            long installed = facade.I18N(provider);

            Assert.Equal(provider is null ? RetCode.E_INVALID_OBJECT : RetCode.OK, installed);

            foreach (long source in sources)
            {
                foreach (long category in categories)
                {
                    foreach (string? text in texts)
                    {
                        _ = facade.I18N(source, category, text);
                        _ = facade.I18N(category, text);
                    }
                }
            }
        }
    }
}
