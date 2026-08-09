// ==================================================================================================
//  SimplifiedChineseProviderTests.cs - THE PROVIDER THAT TRANSLATES NOTHING
//  ------------------------------------------------------------------------------------------------
//  UNIT UNDER TEST   PowerFramework.Shared.Localization.SimplifiedChineseProvider
//  ORACLE            ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_chs.sru   28 lines
//
//  WHY A PROVIDER THAT DOES NOTHING IS STILL A PROVIDER
//  ------------------------------------------------------------------------------------------------
//  Simplified Chinese is the framework's BASE locale: every source string in ws_objects is already
//  written in it, so there is nothing to translate INTO. The oracle is 28 lines of which the entire
//  translate body is COMMENTED OUT [n_cst_i18n_chs.sru:L19-L26], and what remains is the source filter
//  and its two answers. That is not an unfinished object - it is the correct implementation of "the
//  identity translation", and the framework ships it so that pfw.sra's three-way locale switch
//  [pfw.sra:L95-L102] has a class to select for 'chs' exactly as it has one for 'en' and 'cht'. A
//  fourth branch that installed NO provider would look equivalent and is not: this class ANSWERS 1 for
//  a framework string, and the facade's no-provider path answers nothing at all.
//
//  THE OBSERVABLE CONTRACT, WHICH IS TWO ROWS WIDE
//  ------------------------------------------------------------------------------------------------
//      source == I18N_SRC_PFW      ->  return 1  and leave text EXACTLY as it arrived
//      source != I18N_SRC_PFW      ->  return 0  and leave text EXACTLY as it arrived
//
//  The two rows differ ONLY in the return code; neither writes the text. That is the whole class, and
//  the tests below pin both halves plus the one thing a reader is most likely to get wrong - that
//  "handled" here means "handled by doing nothing", NOT "not handled".
//
//  WHY THE HANDLED-BUT-UNCHANGED ANSWER MATTERS TO A CONSUMER
//  ------------------------------------------------------------------------------------------------
//  A chained-provider host would read 1 as "stop, this one owns the string" and 0 as "keep looking".
//  Because this provider claims every framework string, installing it AHEAD of another provider in
//  such a host would suppress that provider entirely. The facade in this phase installs exactly one
//  provider and discards the code, so the distinction is currently unobservable through I18n - which
//  is precisely why it is asserted HERE, directly against the provider, where it is observable.
//
//  RULES POSITION
//  review_rules returns "No user rules provided.". Constraints cited inline: C-B (replicate behaviour
//  including the deliberate no-op), C-K (document boundary decisions).
// ==================================================================================================

using System.Reflection;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.Shared.Localization.Tests;

/// <summary>
/// Parity tests for <see cref="SimplifiedChineseProvider"/>.
/// </summary>
public class SimplifiedChineseProviderTests
{
    // ==============================================================================================
    //  1. THE TWO-ROW CONTRACT
    // ==============================================================================================

    /// <summary>
    /// A framework-sourced string is HANDLED - answer 1 - and comes back byte for byte unchanged.
    /// [:L18, :L27]
    /// </summary>
    /// <param name="text">The text to pass through.</param>
    /// <remarks>
    /// Both halves are load-bearing and they pull in opposite directions, which is why they are
    /// asserted together on every row: an implementation that answered 0 would be indistinguishable
    /// from a decline, and one that wrote to the text would corrupt the base locale it is supposed to
    /// leave alone. The rows deliberately include the empty string, whitespace, a Sprintf placeholder
    /// and an XPath metacharacter, because none of them is special to this provider and the test says
    /// so by treating them all identically.
    /// </remarks>
    [Theory]
    [InlineData("修改数据被拒绝")]
    [InlineData("最小化")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("第{}行")]
    [InlineData("it's")]
    [InlineData("plain ascii")]
    public void AFrameworkSourcedStringIsHandledAndLeftUnchanged(string text)
    {
        SimplifiedChineseProvider provider = new();
        string? subject = text;

        long code = provider.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_WINDOW, ref subject);

        Assert.Equal(1L, code);
        Assert.Equal(text, subject);
    }

    /// <summary>
    /// Any other source DECLINES - answer 0 - and likewise leaves the text unchanged. [:L18]
    /// </summary>
    /// <param name="source">The source value.</param>
    /// <remarks>
    /// The decline is what keeps a consuming application's own strings out of the framework's identity
    /// translation. It has no visible effect on the text either, so the return code is the ONLY thing
    /// separating these rows from the handled ones - and that is the assertion.
    /// </remarks>
    [Theory]
    [InlineData(1L)]
    [InlineData(2L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    public void AnyOtherSourceDeclinesAndLeavesTheTextUnchanged(long source)
    {
        SimplifiedChineseProvider provider = new();
        string? subject = "修改数据被拒绝";

        long code = provider.OnTranslate(source, Enums.I18N_CAT_WINDOW, ref subject);

        Assert.Equal(0L, code);
        Assert.Equal("修改数据被拒绝", subject);
    }

    /// <summary>
    /// The declared custom-source constant is one of the declining values, so the decline is a
    /// statement about a real caller rather than about an arbitrary number.
    /// </summary>
    /// <remarks>
    /// <c>Enums.I18N_SRC_CUSTOM</c> is the value a consuming application passes for its own strings
    /// [enums.sru:L115-L116]. Naming it here ties the theory rows above to the contract they enforce:
    /// the boundary is framework versus application, not zero versus non-zero.
    /// </remarks>
    [Fact]
    public void TheDeclaredCustomSourceIsTheCanonicalDecliningValue()
    {
        Assert.Equal(0L, Enums.I18N_SRC_PFW);
        Assert.Equal(1L, Enums.I18N_SRC_CUSTOM);
        Assert.NotEqual(Enums.I18N_SRC_PFW, Enums.I18N_SRC_CUSTOM);

        SimplifiedChineseProvider provider = new();
        string? subject = "application string";

        Assert.Equal(0L, provider.OnTranslate(Enums.I18N_SRC_CUSTOM, Categories.CAT_DWSVC, ref subject));
        Assert.Equal("application string", subject);
    }

    // ==============================================================================================
    //  2. WHAT THE PROVIDER DOES *NOT* LOOK AT
    // ==============================================================================================

    /// <summary>
    /// The category is not consulted: every category answers identically for a framework source.
    /// [:L18]
    /// </summary>
    /// <param name="category">The category value.</param>
    /// <remarks>
    /// The English provider's answer depends heavily on the category - it selects the element to read -
    /// so a reader coming from that file would reasonably expect a category switch here too. There
    /// isn't one, and the absence is asserted across the whole declared range plus values outside it,
    /// including <c>I18N_CAT_CUSTOM</c> which the English provider deliberately leaves unmapped. A
    /// category-sensitive implementation would fail at least one row.
    /// </remarks>
    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(2L)]
    [InlineData(3L)]
    [InlineData(4L)]
    [InlineData(5L)]
    [InlineData(6L)]
    [InlineData(7L)]
    [InlineData(8L)]
    [InlineData(-1L)]
    [InlineData(long.MaxValue)]
    public void TheCategoryIsNotConsulted(long category)
    {
        SimplifiedChineseProvider provider = new();
        string? subject = "无效的值";

        Assert.Equal(1L, provider.OnTranslate(Enums.I18N_SRC_PFW, category, ref subject));
        Assert.Equal("无效的值", subject);
    }

    /// <summary>
    /// A null text is handled on the framework path and declined on the other, and stays null on both.
    /// </summary>
    /// <remarks>
    /// The provider never dereferences the text, so null is not a special case for it - which is
    /// exactly the claim being made. The English provider, by contrast, has to coalesce null before
    /// interpolating it into an XPath predicate; asserting the difference here documents that only one
    /// of the two providers reads the parameter at all.
    /// </remarks>
    [Fact]
    public void ANullTextIsCarriedThroughBothPathsUnchanged()
    {
        SimplifiedChineseProvider provider = new();

        string? handled = null;
        Assert.Equal(1L, provider.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_WINDOW, ref handled));
        Assert.Null(handled);

        string? declined = null;
        Assert.Equal(0L, provider.OnTranslate(Enums.I18N_SRC_CUSTOM, Enums.I18N_CAT_WINDOW, ref declined));
        Assert.Null(declined);
    }

    /// <summary>
    /// The provider holds no state: repeated calls answer identically, and two instances agree.
    /// </summary>
    /// <remarks>
    /// A 28-line object with no fields cannot accumulate state, and this fact is what lets a host treat
    /// it as a singleton - which <c>pfw.sra</c> does, creating one and installing it for the process
    /// lifetime [pfw.sra:L95-L103]. Asserted so a future change that added caching would have to
    /// declare itself here.
    /// </remarks>
    [Fact]
    public void TheProviderIsStatelessAcrossCallsAndInstances()
    {
        SimplifiedChineseProvider first = new();
        SimplifiedChineseProvider second = new();

        for (int repetition = 0; repetition < 3; repetition++)
        {
            string? viaFirst = "关闭";
            string? viaSecond = "关闭";

            long firstCode = first.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_WINDOW, ref viaFirst);
            long secondCode = second.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_WINDOW, ref viaSecond);

            Assert.Equal(1L, firstCode);
            Assert.Equal(firstCode, secondCode);
            Assert.Equal("关闭", viaFirst);
            Assert.Equal(viaFirst, viaSecond);
        }
    }

    // ==============================================================================================
    //  3. THE NO-OP IS DISTINGUISHABLE FROM A DECLINE AND FROM NO PROVIDER
    // ==============================================================================================

    /// <summary>
    /// The no-op is NOT a decline: it claims the framework string while a declining provider does not,
    /// even though both leave the text alone.
    /// </summary>
    /// <remarks>
    /// This is the single most mistakable property of the class, and it is invisible through the facade
    /// because <c>I18n</c> discards the provider's code - so it is asserted directly. The comparison
    /// against <c>NotHandledProvider</c> makes the claim concrete: identical text outcome, different
    /// answer. A chained-provider host would branch on that difference, which is why the framework
    /// bothers to return 1 from a method that does nothing.
    /// </remarks>
    [Fact]
    public void TheNoOpClaimsTheStringWhereADecliningProviderDoesNot()
    {
        SimplifiedChineseProvider noOp = new();
        NotHandledProvider declining = new();

        string? viaNoOp = "修改数据被拒绝";
        string? viaDeclining = "修改数据被拒绝";

        long noOpCode = noOp.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_DWSVC, ref viaNoOp);
        long decliningCode = declining.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_DWSVC, ref viaDeclining);

        // Same observable text...
        Assert.Equal(viaNoOp, viaDeclining);
        Assert.Equal("修改数据被拒绝", viaNoOp);

        // ...different answer, which is the entire distinction.
        Assert.Equal(1L, noOpCode);
        Assert.Equal(0L, decliningCode);
        Assert.NotEqual(noOpCode, decliningCode);
    }

    /// <summary>
    /// Through the facade, installing this provider is INDISTINGUISHABLE from installing none - the
    /// text passes through either way.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The complement of the fact above, and the reason both are needed. At the provider level the
    /// no-op and a decline differ; at the facade level the no-op and NO PROVIDER AT ALL converge,
    /// because <c>I18n</c> returns the text whatever the provider answered. So the observable effect of
    /// selecting the 'chs' locale is exactly the observable effect of selecting no locale.
    /// </para>
    /// <para>
    /// That convergence is the behaviour, not a shortcoming: the base locale needs no translation, and
    /// the class exists to give the locale switch a target rather than to change any string.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThroughTheFacadeTheNoOpIsIndistinguishableFromNoProvider()
    {
        I18n withoutProvider = new();
        I18n withNoOp = new();

        Assert.Equal(RetCode.OK, withNoOp.I18N(new SimplifiedChineseProvider()));

        foreach (string? candidate in new string?[] { "修改数据被拒绝", "", null, "第{}行" })
        {
            Assert.Equal(
                withoutProvider.I18N(Categories.CAT_DWSVC, candidate),
                withNoOp.I18N(Categories.CAT_DWSVC, candidate));

            Assert.Equal(candidate, withNoOp.I18N(Categories.CAT_DWSVC, candidate));
        }
    }

    // ==============================================================================================
    //  4. SHAPE
    // ==============================================================================================

    /// <summary>
    /// The class implements the provider contract, exposes only the one contract member, and declares
    /// no state.
    /// </summary>
    /// <remarks>
    /// The member count is the machine-checkable form of "28 lines with a commented-out body": anything
    /// added here - a helper, a cache, a table - would be behaviour the oracle does not have. The field
    /// check is the same claim from the other side, and together they are what makes the stateless fact
    /// above a structural guarantee rather than an observation over three repetitions.
    /// </remarks>
    [Fact]
    public void TheClassIsTheContractAndNothingElse()
    {
        Assert.True(typeof(II18nProvider).IsAssignableFrom(typeof(SimplifiedChineseProvider)));

        MethodInfo[] declaredMethods = typeof(SimplifiedChineseProvider)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.Single(declaredMethods);
        Assert.Equal(nameof(II18nProvider.OnTranslate), declaredMethods[0].Name);

        Assert.Empty(typeof(SimplifiedChineseProvider).GetFields(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly));

        Assert.Empty(typeof(SimplifiedChineseProvider).GetProperties(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly));
    }

    /// <summary>
    /// Nothing on the provider throws, for any combination of inputs, and every answer is one of the
    /// two the oracle can produce.
    /// </summary>
    /// <remarks>
    /// A 231-row sweep over sources, categories and texts. It is cheap here because the class reads
    /// only the source, and it is worth having because the alphabet claim - the answer is 0 or 1 and
    /// never a <c>RetCode</c> value - is the one a future maintainer is most likely to break by
    /// "tidying" the literals into named constants from the return-code algebra, where 1 means PREVENT.
    /// </remarks>
    [Fact]
    public void NoCombinationOfInputsThrowsAndEveryAnswerIsZeroOrOne()
    {
        SimplifiedChineseProvider provider = new();

        long[] sources = [Enums.I18N_SRC_PFW, Enums.I18N_SRC_CUSTOM, 7L, -3L, long.MinValue, long.MaxValue];
        long[] categories = [0L, 1L, 2L, 3L, 4L, 5L, 6L, 7L, 99L, -1L, long.MaxValue];
        string?[] texts = [null, string.Empty, " ", "关闭", "第{}行"];

        int handled = 0;
        int declined = 0;

        foreach (long source in sources)
        {
            foreach (long category in categories)
            {
                foreach (string? candidate in texts)
                {
                    string? text = candidate;
                    long code = provider.OnTranslate(source, category, ref text);

                    Assert.True(
                        code is 0L or 1L,
                        $"source={source} category={category} answered {code}, which is outside the "
                            + "two-value alphabet the oracle can produce.");

                    Assert.Equal(candidate, text);

                    if (code == 1L)
                    {
                        handled++;
                    }
                    else
                    {
                        declined++;
                    }
                }
            }
        }

        // Both halves were reached, so neither branch is untested by accident.
        Assert.Equal(categories.Length * texts.Length, handled);
        Assert.Equal((sources.Length - 1) * categories.Length * texts.Length, declined);
    }
}
