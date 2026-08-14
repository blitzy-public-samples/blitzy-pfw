// ==================================================================================================
//  II18nProviderTests.cs - THE PROVIDER CONTRACT ITSELF
//  ------------------------------------------------------------------------------------------------
//  UNIT UNDER TEST   PowerFramework.Shared.Localization.II18nProvider
//  ORACLE            ws_objects/pfw.ui.pbl.src/n_cst_i18n.sru   the event DECLARATION at :L9, with
//                                                               no handler script of any kind
//
//  WHY AN INTERFACE WITH ONE MEMBER NEEDS A SUITE
//  ------------------------------------------------------------------------------------------------
//  Two reasons, and neither is about the interface's own code, because it has none.
//
//  FIRST, THE SHAPE IS UNUSUAL ENOUGH TO BE "TIDIED" AWAY. `long OnTranslate(long source, long
//  category, ref string? text)` returns a code and mutates its third argument, where idiomatic C#
//  would return the translated string. The `ref` is not an optimisation: it is the only way to express
//  the three distinct outcomes the legacy has - translated, HANDLED BUT DELIBERATELY UNCHANGED, and
//  not handled - because the middle one is exactly what SimplifiedChineseProvider reports and a
//  `string? Translate(...)` signature cannot say it. Refactoring the interface would silently collapse
//  that state into "returned the input", which is what "not handled" means.
//
//  SECOND, THE CONTRACT IS SATISFIED BY THREE SHIPPED IMPLEMENTATIONS AND THE TEST DOUBLES, AND THE
//  FACADE MUST BE UNABLE TO TELL THEM APART except by what they do to `text`. The tests below drive
//  the real contract through every shipped implementation rather than through the doubles alone -
//  which is the gap the review found, since the doubles compiled against the interface but nothing
//  ever invoked the real thing through it.
//
//  WHAT THE ORACLE PROVIDES, AND WHAT IT DOES NOT
//  ------------------------------------------------------------------------------------------------
//  n_cst_i18n.sru DECLARES the ontranslate event and implements NO script for it. ne_cst_i18n.sru,
//  which derives from it, declares two constants and no event script either. So there is no base
//  behaviour to inherit and nothing to invoke - which is why the port is a pure interface rather than
//  an abstract class, and why the concrete providers' `call super::ontranslate` reproduces as a
//  comment at the point it would have run rather than as an invented base call. This suite asserts the
//  consequence: the interface has exactly one member, no default implementation, and nothing to
//  dispose.
//
// ==================================================================================================

using System.Linq;
using System.Reflection;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.Shared.Localization.Tests;

/// <summary>
/// Contract tests for <see cref="II18nProvider"/>, driven through every shipped implementation and
/// through the facade.
/// </summary>
public class II18nProviderTests
{
    /// <summary>
    /// An unmistakably synthetic source string, so nothing here can pass because the real translation
    /// table happened to contain the same key.
    /// </summary>
    private const string SourceText = "<<PFW-CONTRACT-PROBE>>";

    /// <summary>
    /// Every shipped implementation of the contract, plus the two purpose-built doubles.
    /// </summary>
    /// <returns>One row per implementation.</returns>
    public static TheoryData<string, II18nProvider> AllImplementations()
    {
        TheoryData<string, II18nProvider> rows = [];

        rows.Add(nameof(SimplifiedChineseProvider), new SimplifiedChineseProvider());
        rows.Add(nameof(EnglishProvider), new EnglishProvider());
        rows.Add(nameof(TraditionalChineseProvider), new TraditionalChineseProvider());
        rows.Add(nameof(HandledProvider), new HandledProvider());
        rows.Add(nameof(NotHandledProvider), new NotHandledProvider());
        rows.Add(nameof(RecordingProvider), new RecordingProvider());

        return rows;
    }

    // ==============================================================================================
    //  1. THE SHAPE OF THE CONTRACT
    // ==============================================================================================

    /// <summary>
    /// The interface declares exactly one member, with the exact signature the legacy event has, and
    /// carries no default implementation. [n_cst_i18n.sru:L9]
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asserted by reflection rather than by inspection because every element of it is load-bearing.
    /// The RETURN TYPE is <c>long</c> because the legacy event returns a code and the facade must be
    /// able to ignore it. The THIRD PARAMETER is <c>ref string?</c> because translation mutates rather
    /// than returns, and because the string is nullable in PowerScript. And there is NO DEFAULT
    /// IMPLEMENTATION, because a default would be the invented base behaviour the oracle does not have
    /// - <c>n_cst_i18n.sru</c> declares the event and implements nothing.
    /// </para>
    /// <para>
    /// A second member added here would also break the "the facade cannot tell implementations apart"
    /// property, since it would give it something else to call.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheContractIsOneMemberWithTheLegacyEventSignature()
    {
        System.Type contract = typeof(II18nProvider);

        Assert.True(contract.IsInterface);

        MethodInfo[] declared = contract.GetMethods();
        MethodInfo member = Assert.Single(declared);

        Assert.Equal(nameof(II18nProvider.OnTranslate), member.Name);
        Assert.Equal(typeof(long), member.ReturnType);
        Assert.True(member.IsAbstract, "The contract must declare no default implementation.");

        ParameterInfo[] parameters = member.GetParameters();
        Assert.Equal(3, parameters.Length);

        Assert.Equal(typeof(long), parameters[0].ParameterType);
        Assert.False(parameters[0].ParameterType.IsByRef);

        Assert.Equal(typeof(long), parameters[1].ParameterType);
        Assert.False(parameters[1].ParameterType.IsByRef);

        // The one that matters: by reference, and to a string.
        Assert.True(parameters[2].ParameterType.IsByRef);
        Assert.Equal(typeof(string), parameters[2].ParameterType.GetElementType());
        Assert.False(parameters[2].IsOut, "It is `ref`, not `out`: an incoming value is read.");

        // No properties, no events, and nothing to dispose - the contract is behaviour only.
        Assert.Empty(contract.GetProperties());
        Assert.Empty(contract.GetEvents());
        Assert.DoesNotContain(typeof(System.IDisposable), contract.GetInterfaces());
    }

    /// <summary>
    /// Every provider the library ships implements the contract, and the facade accepts each one.
    /// </summary>
    /// <param name="name">The implementation's own name, so a failure says which one.</param>
    /// <param name="provider">The implementation.</param>
    /// <remarks>
    /// The three-provider shape is reproduced from the legacy deliberately - one class per locale,
    /// including the Simplified Chinese one whose body is a no-op because that is the base locale - so
    /// this row set is also an audit that none of the three was dropped as "redundant".
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllImplementations))]
    public void EveryImplementationSatisfiesTheContractAndInstallsOnTheFacade(string name, II18nProvider provider)
    {
        Assert.NotNull(name);
        Assert.IsAssignableFrom<II18nProvider>(provider);

        I18n facade = new();
        Assert.Equal(RetCode.OK, facade.I18N(provider));
    }

    /// <summary>
    /// All three shipped locale providers exist and are distinct types, with the Simplified Chinese one
    /// present despite having nothing to do.
    /// </summary>
    /// <remarks>
    /// C-B. <c>n_cst_i18n_chs.sru</c> is 28 lines whose translate body is entirely commented out,
    /// because Simplified Chinese IS the base locale and needs no table. Dropping it would be the
    /// tidy-up the constraint forbids: <c>ws_objects/pfw.pbl.src/pfw.sra</c> - the framework
    /// application, not the same-named packager object - selects one of three provider classes from the
    /// locale string, so a two-provider port would leave one locale unmappable.
    /// </remarks>
    [Fact]
    public void AllThreeLocaleProvidersShipIncludingTheNoOpOne()
    {
        II18nProvider[] locales =
            [new EnglishProvider(), new SimplifiedChineseProvider(), new TraditionalChineseProvider()];

        Assert.Equal(3, locales.Select(provider => provider.GetType()).Distinct().Count());
    }

    // ==============================================================================================
    //  2. THE ref DISCIPLINE, ACROSS EVERY IMPLEMENTATION
    // ==============================================================================================

    /// <summary>
    /// No implementation ever leaves <c>text</c> in a state its return code does not license: a
    /// provider reporting NOT HANDLED must leave the caller's variable exactly as it found it.
    /// </summary>
    /// <param name="name">The implementation's own name, so a failure says which one.</param>
    /// <param name="provider">The implementation.</param>
    /// <remarks>
    /// <para>
    /// This is the invariant that makes the facade's silent passthrough work. If a declining provider
    /// blanked the text, "no translation available" would become "the text disappeared", and the
    /// facade - which reads the code and discards it - would have no way to recover.
    /// </para>
    /// <para>
    /// Note what is NOT asserted: that a HANDLED provider changed the text. Reporting handled and
    /// leaving the text alone is a legitimate outcome and is precisely what
    /// <see cref="SimplifiedChineseProvider"/> does for framework text, so the assertion is one-sided
    /// on purpose.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllImplementations))]
    public void ADecliningImplementationLeavesTheTextUntouched(string name, II18nProvider provider)
    {
        long[] sources = [Enums.I18N_SRC_PFW, Enums.I18N_SRC_CUSTOM, -1L];
        long[] categories = [Enums.I18N_CAT_WINDOW, Enums.I18N_CAT_CUSTOM, Categories.CAT_MSGBOX, Categories.CAT_DWSVC, -1L];

        foreach (long source in sources)
        {
            foreach (long category in categories)
            {
                string? text = SourceText;
                long code = provider.OnTranslate(source, category, ref text);

                if (code == 0L)
                {
                    Assert.Equal(
                        SourceText,
                        text ?? $"{name} blanked the text while reporting not handled");
                }
            }
        }
    }

    /// <summary>
    /// Every implementation answers with the contract's own alphabet: 1 for handled, 0 for not
    /// handled, and nothing else.
    /// </summary>
    /// <param name="name">The implementation's own name, so a failure says which one.</param>
    /// <param name="provider">The implementation.</param>
    /// <remarks>
    /// The alphabet is two values wide, and it is NOT the return-code algebra - 1 is
    /// <c>RetCode.PREVENT</c> in that algebra and means nothing of the kind here. Keeping the two apart
    /// is why the facade discards the code rather than passing it to <c>Predicates.IsSucceeded</c>,
    /// which would read a decline as a failure and a handled translation as a prevention.
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllImplementations))]
    public void EveryImplementationAnswersWithinTheTwoValueAlphabet(string name, II18nProvider provider)
    {
        long[] sources = [Enums.I18N_SRC_PFW, Enums.I18N_SRC_CUSTOM, 42L];
        long[] categories = [Enums.I18N_CAT_WINDOW, Enums.I18N_CAT_DATAWINDOW, Categories.CAT_DWSVC, 99L];

        foreach (long source in sources)
        {
            foreach (long category in categories)
            {
                string? text = SourceText;
                long code = provider.OnTranslate(source, category, ref text);

                Assert.True(
                    code is 0L or 1L,
                    $"{name} answered {code}, which is outside the handled/not-handled alphabet.");
            }
        }
    }

    /// <summary>
    /// No implementation throws for any combination of source, category and text - including null text.
    /// </summary>
    /// <param name="name">The implementation's own name, so a failure says which one.</param>
    /// <param name="provider">The implementation.</param>
    /// <remarks>
    /// The localization path's posture is silent passthrough, and the facade has no catch of its own,
    /// so an implementation that threw would propagate straight into whatever message the caller was
    /// building. The null-text row is the one most likely to be missed: the English provider
    /// interpolates the text into an XPath expression, so null has to render as something rather than
    /// dereference.
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllImplementations))]
    public void NoImplementationThrowsForAnyInput(string name, II18nProvider provider)
    {
        long[] sources = [Enums.I18N_SRC_PFW, Enums.I18N_SRC_CUSTOM, long.MinValue, long.MaxValue];
        long[] categories = [Enums.I18N_CAT_WINDOW, Enums.I18N_CAT_CUSTOM, Categories.CAT_MSGBOX, Categories.CAT_DWSVC, long.MinValue, long.MaxValue];
        string?[] texts = [null, string.Empty, " ", SourceText, "最小化", "it's", "第{}行"];

        int answered = 0;

        foreach (long source in sources)
        {
            foreach (long category in categories)
            {
                foreach (string? candidate in texts)
                {
                    string? text = candidate;
                    answered += provider.OnTranslate(source, category, ref text) == 1L ? 1 : 0;
                }
            }
        }

        Assert.True(answered >= 0, $"unreachable: {name} returned from every call rather than throwing");
    }

    // ==============================================================================================
    //  3. THE CONTRACT REACHED THROUGH THE FACADE
    // ==============================================================================================

    /// <summary>
    /// Driving each implementation through <see cref="I18n"/> gives the same answer as calling it
    /// directly, so the facade adds nothing and loses nothing.
    /// </summary>
    /// <param name="name">The implementation's own name, so a failure says which one.</param>
    /// <param name="provider">The implementation.</param>
    /// <remarks>
    /// This is the fact the review was asking for: the real contract invoked THROUGH the facade rather
    /// than only compiled against by a double. It is expressed as an equivalence rather than as a
    /// value, so it holds for every implementation without restating each one's behaviour - and it
    /// fails if the facade ever starts inspecting the code, caching, or normalising the text.
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllImplementations))]
    public void TheFacadeIsTransparentOverEveryImplementation(string name, II18nProvider provider)
    {
        long[] sources = [Enums.I18N_SRC_PFW, Enums.I18N_SRC_CUSTOM];
        long[] categories = [Enums.I18N_CAT_WINDOW, Categories.CAT_MSGBOX, Categories.CAT_DWSVC, Enums.I18N_CAT_CUSTOM];
        string?[] texts = [null, string.Empty, SourceText, "最小化"];

        I18n facade = new();
        Assert.Equal(RetCode.OK, facade.I18N(provider));

        foreach (long source in sources)
        {
            foreach (long category in categories)
            {
                foreach (string? candidate in texts)
                {
                    string? direct = candidate;
                    _ = provider.OnTranslate(source, category, ref direct);

                    string? throughFacade = facade.I18N(source, category, candidate);

                    Assert.True(
                        string.Equals(direct, throughFacade, System.StringComparison.Ordinal),
                        $"{name}: calling it directly gave \"{direct}\" while the facade gave "
                            + $"\"{throughFacade}\", so the facade is no longer transparent.");
                }
            }
        }
    }
}
