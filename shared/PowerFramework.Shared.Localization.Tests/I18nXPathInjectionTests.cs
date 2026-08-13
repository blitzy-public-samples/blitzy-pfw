// ==================================================================================================
//  I18nXPathInjectionTests.cs - THE CRAFTED LOOKUP KEY, PINNED OUTCOME BY OUTCOME
//  ------------------------------------------------------------------------------------------------
//  UNITS UNDER TEST  PowerFramework.Shared.Localization.I18nResourceReader.Lookup   (the traversal)
//                    PowerFramework.Shared.Localization.EnglishProvider.OnTranslate (the caller)
//                    PowerFramework.Shared.Localization.I18n.I18N                   (the facade)
//  ORACLES           ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_en.sru:L51
//                    ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_cht.sru:L52
//                    pfw.i18n.xml  (the shipped table, read-only under C-C)
//
//  WHAT THE LEGACY DOES, AND WHY THIS FILE EXISTS
//  ------------------------------------------------------------------------------------------------
//  The legacy providers build their query by UNESCAPED interpolation and evaluate it:
//
//      Sprintf("string(pfw/{}[@lang='en']/tr[@text='{}']/@to)", sCat, text)
//
//  Neither the category nor the source text is quoted or escaped, so a key containing an apostrophe
//  does not produce a failed lookup - it produces a DIFFERENT EXPRESSION. A key can close the literal
//  and add a true disjunct, or close the predicate and append a whole second location path with '|'.
//  The expression then still runs, and the answer is not a miss: it is a DIFFERENT, ATTACKER-CHOSEN
//  translation.
//
//  This file is the exhaustive catalogue of those payloads. It exists because that catalogue is worth
//  keeping whichever way the implementation went: the shapes are the evidence, and an implementation
//  change that altered how any of them resolves must be a deliberate decision with a test behind it
//  rather than a side effect nobody measured.
//
//  THE IMPLEMENTATION IS A TRAVERSAL, SO EVERY PAYLOAD BELOW IS A MISS
//  ------------------------------------------------------------------------------------------------
//  I18nResourceReader.Lookup walks the four steps of the same location path with LINQ to XML and
//  compares the same two attribute values. It never builds a query string, so no argument can
//  contribute SYNTAX - only data to an ordinal comparison. Every crafted key in this file is therefore
//  an ordinary miss: the empty string, which is the legacy's own miss signal because the legacy wraps
//  its expression in string(), and string() of an empty node set is "".
//
//  That divergence from the legacy expression is DELIBERATE, MEASURED, AND NARROW - the reasoning is
//  recorded at I18nResourceReader.Lookup and is summarised here because this is the file whose
//  expectations it changed:
//
//    * ALL 126 <tr> ENTRIES of pfw.i18n.xml - both languages, all six categories - resolve
//      BYTE-IDENTICALLY under the interpolated expression and under the traversal. Zero divergence.
//      So do all three miss kinds and all four degenerate argument shapes.
//    * The equality is not luck. NO text attribute anywhere in the table contains an apostrophe, so no
//      genuine entry can reach the branch where the two spellings differ. That is a property of the
//      DATA, it is asserted below, and it is what makes the change unobservable in the sense AAP
//      0.1.5 requires - "the implementation may be safer than the legacy where the change is
//      unobservable", with parameterized SQL as its own canonical case.
//    * The ONLY measured divergences in the whole comparison are crafted payloads: keys the legacy
//      would answer with a translation the caller never asked for, and this implementation answers
//      with a miss. Each one is recorded below next to the value the legacy produced for it.
//
//  Preserving the interpolation would have preserved a spoofing primitive, not a quirk. This method's
//  output is shown to a user as validation and error text - se_cst_dw.sru:L357 and :L368 route dialog
//  text through this facade - so a caller able to choose WHICH translation comes back can choose what
//  the user is told. Constraint C-G forbids leaving such a sink on a newly published boundary, and
//  C-B's replicate-defects mandate covers behaviour a consumer could depend on, which an
//  attacker-chosen answer to an out-of-vocabulary key is not.
//
//  WHAT IS ASSERTED, IN FOUR PARTS
//  ------------------------------------------------------------------------------------------------
//    1  MALFORMED       - an unbalanced apostrophe. A miss under both spellings, and it is the one
//                         class where the two agree for the same reason a caller would expect.
//    2  WOULD-BE-TAUTOLOGY - the payloads that make the predicate a tautology under an interpolated
//                         path. Each is a miss here, and each carries the entry an interpolated
//                         reader would return instead.
//    3  WOULD-BE-UNION  - the payloads that append a second location path, crossing the category
//                         boundary, the language boundary, or both. Each is a miss here.
//    4  THE BOUNDARY    - the properties that hold regardless of spelling: nothing escapes the table,
//                         no framework-shipped key can escape its own literal, document order still
//                         governs genuine duplicates, and the genuine keys still resolve.
//
// ==================================================================================================

using System;
using System.IO;
using System.Text;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.Shared.Localization.Tests;

/// <summary>
/// Tests pinning the exact outcome of every crafted-lookup-key shape, and the whole-table equivalence
/// that makes the traversal a safe substitute for the legacy's interpolated expression.
/// </summary>
public class I18nXPathInjectionTests
{
    /// <summary>
    /// A key that, interpolated, would yield the tautology <c>@text='x' or '1'='1'</c>.
    /// </summary>
    /// <remarks>
    /// The trailing quote of the legacy template supplies the closing quote of the second literal,
    /// which is what made the expression legal rather than malformed. Written as a constant because it
    /// appears in several tests and a stray character would change its class without changing what the
    /// test looks like. Against the shipped table the legacy answered <c>Maximize</c> for a window
    /// lookup with this key; the traversal answers a miss.
    /// </remarks>
    private const string AlwaysTrueKey = "x' or '1'='1";

    /// <summary>
    /// A second would-be tautology with a different shape - numeric comparison and a dangling empty
    /// literal - used to show the class was never specific to one spelling.
    /// </summary>
    private const string AlternateAlwaysTrueKey = "x' or 1=1 or '";

    /// <summary>
    /// The message a fixture-guard failure carries.
    /// </summary>
    private const string FixtureMissingMessage =
        "pfw.i18n.xml is not in the test working directory, so every case below would answer empty and "
            + "this suite would pass without distinguishing a miss from a hit. Restore the linked "
            + "Content item in PowerFramework.Shared.Localization.csproj, which flows to this project "
            + "through the ProjectReference.";

    /// <summary>
    /// The number of <c>tr</c> entries the shipped table declares, measured.
    /// </summary>
    private const int ShippedEntryCount = 126;

    /// <summary>
    /// The encoding synthetic tables are written with: UTF-8, no byte order mark.
    /// </summary>
    private static readonly UTF8Encoding SyntheticEncoding = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Every crafted key this suite exercises, in one place.
    /// </summary>
    /// <remarks>
    /// Collected as a single array because the containment and whole-surface assertions sweep all of
    /// them across every category and every language, and a payload added to one test but forgotten in
    /// the sweep would be the gap this file exists to close.
    /// </remarks>
    private static readonly string[] CraftedKeys =
    [
        // Would-be tautologies.
        AlwaysTrueKey,
        AlternateAlwaysTrueKey,
        "x' or contains(@to,'o') or '",
        "nosuch' or '1'='1",

        // Would-be chosen-entry selections.
        "nosuch' or @text='关闭",
        "还原' and @to='Restore",

        // Would-be unions: category boundary, language boundary, whole-document descent, dangling.
        "x'] | pfw/msgbox[@lang='en']/tr[@text='确定",
        "x'] | pfw/window[@lang='cht']/tr[@text='关闭",
        "确定'] | pfw/window[@lang='en']/tr[@text='关闭",
        "关闭'] | pfw/msgbox[@lang='en']/tr[@text='确定",
        "x'] | //tr[@text='关闭",
        "x'] | //*[@to",
        "x'] | pfw/nosuchelement[@lang='en']/tr[@text='whatever",

        // Malformed, and inert metacharacters, so the sweep covers all four parts.
        "it's",
        "x'",
        "'",
        "x]",
        "x|y",
        "pfw/msgbox",
        "[@lang='en']",
    ];

    /// <summary>
    /// Asserts the real oracle table is reachable before a test relies on its contents.
    /// </summary>
    private static void RequireResourceFixture()
    {
        Assert.True(File.Exists(I18nResourceReader.DefaultResourceFileName), FixtureMissingMessage);
    }

    /// <summary>
    /// Runs <paramref name="assertions"/> against a reader over a synthetic table.
    /// </summary>
    /// <param name="documentText">The synthetic table.</param>
    /// <param name="assertions">The assertions to run.</param>
    /// <remarks>
    /// Used for the cases whose point is the SHAPE of the outcome rather than the oracle's content -
    /// notably the document-order proof, where a purpose-built table makes the winning entry
    /// unambiguous and independent of any future upstream edit to pfw.i18n.xml.
    /// </remarks>
    private static void WithSyntheticTable(string documentText, Action<I18nResourceReader> assertions)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"pfw-i18n-injection-tests-{Guid.NewGuid():N}");

        Directory.CreateDirectory(directory);

        try
        {
            string path = Path.Combine(directory, I18nResourceReader.DefaultResourceFileName);
            File.WriteAllText(path, documentText, SyntheticEncoding);
            assertions(new I18nResourceReader(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // ==============================================================================================
    //  PART 1 - MALFORMED: THE ONE CLASS BOTH SPELLINGS ANSWER ALIKE FOR THE SAME REASON
    // ==============================================================================================

    /// <summary>
    /// A key with an unbalanced apostrophe misses, and the provider declines rather than propagating
    /// anything.
    /// </summary>
    /// <param name="key">The malformed key.</param>
    /// <remarks>
    /// <para>
    /// Restated first so the classes can be read side by side. Under the legacy this key was safe BY
    /// ACCIDENT - the expression failed to compile, and the reader swallowed the resulting
    /// <c>XPathException</c>. Under the traversal it is safe BY CONSTRUCTION: the apostrophe is one more
    /// character in an ordinal comparison against a table in which no source text contains one.
    /// </para>
    /// <para>
    /// The distinction is worth an assertion because the OUTCOME is identical and the ROBUSTNESS is
    /// not. An ordinary apostrophe in a caller's string used to depend on a catch block to avoid
    /// becoming a crash; now there is nothing to catch.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("it's")]
    [InlineData("x'")]
    [InlineData("'")]
    [InlineData("don't touch")]
    public void AMalformedExpressionIsCaughtAndAnswersAsAMiss(string key)
    {
        RequireResourceFixture();

        I18nResourceReader reader = new();

        Assert.Equal(string.Empty, reader.Lookup("en", "window", key));
        Assert.Equal(string.Empty, reader.Lookup("en", "dwsvc", key));

        // ...and the provider reports the decline rather than propagating anything.
        EnglishProvider provider = new();
        string? text = key;
        Assert.Equal(0L, provider.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_WINDOW, ref text));
        Assert.Equal(key, text);
    }

    /// <summary>
    /// A key with a bracket, a pipe or a slash but no quote is an ordinary miss, and always was.
    /// </summary>
    /// <remarks>
    /// <c>x]</c> would interpolate into <c>@text='x]'</c>, a legal predicate comparing against the
    /// two-character string, and matches nothing. Worth pinning because it separates "contains a
    /// metacharacter" from "escapes the literal": under the legacy only the apostrophe could leave the
    /// string context, so brackets, pipes and slashes on their own were always inert. Under the
    /// traversal nothing leaves it at all, so this class and the crafted classes have converged - and
    /// asserting both is how that convergence stays visible.
    /// </remarks>
    [Theory]
    [InlineData("x]")]
    [InlineData("x|y")]
    [InlineData("pfw/msgbox")]
    [InlineData("[@lang='en']")]
    [InlineData("<<ABSENT>>")]
    public void AKeyWithMetacharactersButNoQuoteIsAnOrdinaryMiss(string key)
    {
        RequireResourceFixture();

        I18nResourceReader reader = new();

        Assert.Equal(string.Empty, reader.Lookup("en", "window", key));
        Assert.Equal(string.Empty, reader.Lookup("en", "msgbox", key));
    }

    // ==============================================================================================
    //  PART 2 - THE WOULD-BE TAUTOLOGIES
    // ==============================================================================================

    /// <summary>
    /// A key that would have made the predicate a tautology no longer selects the addressed element's
    /// first entry - it misses - while that element's own first entry still resolves by its real key.
    /// </summary>
    /// <param name="category">The element addressed.</param>
    /// <param name="genuineFirstKey">The real source text of that element's first entry.</param>
    /// <param name="firstInDocumentOrder">That entry's translation.</param>
    /// <remarks>
    /// <para>
    /// Each row carries BOTH halves of the divergence, which is what makes it evidence rather than an
    /// assertion of the current behaviour. The middle column is the measured legacy answer for
    /// <see cref="AlwaysTrueKey"/> against that element: under the interpolated expression the
    /// predicate stopped discriminating, so every <c>tr</c> in the addressed element matched and
    /// <c>string()</c> took the first in document order. The same injected key therefore yielded
    /// <c>Maximize</c> through a window lookup and <c>Line {}</c> through a DataWindow-service lookup.
    /// </para>
    /// <para>
    /// The third column is asserted to still resolve through its GENUINE key. That is the important
    /// half: it proves the payload now misses because the comparison is literal, not because the
    /// traversal stopped reaching the element. A test that only asserted the miss would pass equally
    /// well against a reader that had stopped working.
    /// </para>
    /// <para>
    /// The genuine keys are the framework's own Simplified Chinese source strings, and the rows double
    /// as an assertion about the oracle's ordering: these are the first <c>tr</c> children of
    /// pfw.i18n.xml lines 3, 30, 33, 16, 37 and 52 respectively.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("window", "最大化", "Maximize")]                                            // :L3
    [InlineData("tabcontrol", "固定", "Dock")]                                              // :L30
    [InlineData("ribbonbar", "折叠功能区", "Collapse")]                                      // :L33
    [InlineData("splitcontainer", "双击折叠左侧面板", "Double-click to collapse the left panel")] // :L16
    [InlineData("msgbox", "提示", "Information")]                                           // :L37
    [InlineData("dwsvc", "第{}行", "Line {}")]                                              // :L52
    public void AWouldBeTautologyMissesWhileTheGenuineFirstEntryStillResolves(
        string category,
        string genuineFirstKey,
        string firstInDocumentOrder)
    {
        RequireResourceFixture();

        I18nResourceReader reader = new();

        // The payload that used to return `firstInDocumentOrder` now returns nothing at all.
        Assert.Equal(string.Empty, reader.Lookup("en", category, AlwaysTrueKey));

        // ...and the element is still perfectly reachable by its real key, which is what proves the
        // miss above is a literal comparison rather than a broken traversal.
        Assert.Equal(firstInDocumentOrder, reader.Lookup("en", category, genuineFirstKey));
    }

    /// <summary>
    /// The class was never tied to one spelling, and neither is the miss: a numeric comparison with a
    /// dangling literal, and a function call in the disjunct, both answer alike.
    /// </summary>
    /// <remarks>
    /// Guards against a change that special-cased one string. The class is "the key closed the literal
    /// and added a true disjunct", and there are unboundedly many ways to write that - so the assertion
    /// is that three structurally different ones agree, including one that calls an XPath function
    /// (<c>contains</c>) rather than comparing constants. A traversal has no function library to reach,
    /// which is the structural reason the whole family collapses to one outcome.
    /// </remarks>
    [Fact]
    public void EveryTautologySpellingAnswersAlike()
    {
        RequireResourceFixture();

        I18nResourceReader reader = new();

        foreach (string key in (string[])
            [AlwaysTrueKey, AlternateAlwaysTrueKey, "x' or contains(@to,'o') or '", "nosuch' or '1'='1"])
        {
            Assert.Equal(string.Empty, reader.Lookup("en", "window", key));
            Assert.Equal(string.Empty, reader.Lookup("en", "dwsvc", key));
            Assert.Equal(string.Empty, reader.Lookup("cht", "window", key));
        }
    }

    /// <summary>
    /// A tautology could never reach another locale by itself, and now reaches nothing - but the two
    /// locales are still independently addressable, and the undeclared third is still a miss.
    /// </summary>
    /// <remarks>
    /// A useful negative kept from the legacy analysis: the injected predicate sat on <c>tr</c>, while
    /// <c>@lang</c> is a predicate on the element above it, so a tautology broadened the entry selection
    /// within one locale's section and no further. Measured under the legacy, <c>en</c> answered
    /// <c>Maximize</c> and <c>cht</c> answered <c>最大化</c> for the same key.
    /// <para>
    /// The language boundary is asserted here in its surviving form: the same genuine key resolves to a
    /// DIFFERENT value per language token, and <c>chs</c> - which the table declares no section for,
    /// because Simplified Chinese is the base locale and its provider is a genuine no-op - misses in
    /// both spellings.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheLanguageTokenStillSelectsAndTheUndeclaredThirdStillMisses()
    {
        RequireResourceFixture();

        I18nResourceReader reader = new();

        Assert.Equal(string.Empty, reader.Lookup("en", "window", AlwaysTrueKey));
        Assert.Equal(string.Empty, reader.Lookup("cht", "window", AlwaysTrueKey));

        // The genuine key still discriminates between the two declared locales.
        Assert.Equal("Maximize", reader.Lookup("en", "window", "最大化"));
        Assert.Equal("最大化", reader.Lookup("cht", "window", "最大化"));

        // And the undeclared language has no section, so it misses whatever the key is.
        Assert.Equal(string.Empty, reader.Lookup("chs", "window", AlwaysTrueKey));
        Assert.Equal(string.Empty, reader.Lookup("chs", "window", "最大化"));
    }

    /// <summary>
    /// Reached through the provider and the facade, a crafted key is reported as a MISS at every layer
    /// and the caller's text comes back untouched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the consequence that matters and the reason the class was worth pinning at all. Under the
    /// legacy the provider could not distinguish an injected match from a genuine one: the reader
    /// returned a non-empty string, so the provider wrote it and answered handled - <c>1</c> - and the
    /// facade then discarded the code and returned the text, so a caller saw a confidently wrong
    /// translation with no signal at any layer. Measured: <c>Maximize</c> through a window lookup and
    /// <c>Line {}</c> through a DataWindow-service lookup.
    /// </para>
    /// <para>
    /// Now every layer reports the miss, and the facade's SILENT PASSTHROUGH returns the source text
    /// unchanged - which is the legacy's own documented fallback behaviour for an untranslated string
    /// (<c>i18n.srf</c>) and is preserved verbatim. So a crafted key is treated exactly like any other
    /// string the table does not carry, which is what it is.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThroughTheProviderAndFacadeACraftedKeyIsAnOrdinaryUntranslatedString()
    {
        RequireResourceFixture();

        EnglishProvider provider = new();

        string? viaWindow = AlwaysTrueKey;
        Assert.Equal(0L, provider.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_WINDOW, ref viaWindow));
        Assert.Equal(AlwaysTrueKey, viaWindow);

        string? viaDataWindowService = AlwaysTrueKey;
        Assert.Equal(
            0L,
            provider.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_DWSVC, ref viaDataWindowService));
        Assert.Equal(AlwaysTrueKey, viaDataWindowService);

        // Through the facade the caller gets its own text back - the silent passthrough - rather than
        // an entry it never asked for.
        I18n facade = new();
        Assert.Equal(RetCode.OK, facade.I18N(new EnglishProvider()));
        Assert.Equal(AlwaysTrueKey, facade.I18N(Enums.I18N_CAT_WINDOW, AlwaysTrueKey));

        // ...while a genuine key still translates through the same facade instance, so the passthrough
        // above is a miss rather than a provider that was never installed.
        Assert.Equal("Maximize", facade.I18N(Enums.I18N_CAT_WINDOW, "最大化"));
    }

    // ==============================================================================================
    //  PART 3 - THE WOULD-BE UNIONS
    // ==============================================================================================

    /// <summary>
    /// A key that would have appended a union operand no longer crosses the category boundary the
    /// caller's argument is supposed to enforce.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The key <c>x'] | pfw/msgbox[@lang='en']/tr[@text='确定</c> expanded to
    /// <c>pfw/window[@lang='en']/tr[@text='x'] | pfw/msgbox[@lang='en']/tr[@text='确定']</c>. The first
    /// operand matched nothing; the second matched the message-box entry. So a WINDOW-category lookup
    /// answered with a MESSAGE-BOX translation - measured <c>OK</c>, and the same <c>OK</c> through the
    /// DataWindow-service and tab-control elements too, because in every case only the injected operand
    /// contributed. The category argument had become decorative.
    /// </para>
    /// <para>
    /// It is decorative no longer, and both halves are asserted: the crafted key misses in every
    /// element, and the message-box entry the payload was reaching for is still reachable through its
    /// own category and nowhere else. That second assertion is the boundary itself - a category
    /// argument that selects nothing would satisfy the first half alone.
    /// </para>
    /// </remarks>
    [Fact]
    public void AWouldBeUnionOperandNoLongerCrossesTheCategoryBoundary()
    {
        RequireResourceFixture();

        const string Key = "x'] | pfw/msgbox[@lang='en']/tr[@text='确定";

        I18nResourceReader reader = new();

        foreach (string category in
            (string[])["window", "dwsvc", "tabcontrol", "ribbonbar", "splitcontainer", "msgbox"])
        {
            Assert.Equal(string.Empty, reader.Lookup("en", category, Key));
        }

        // The genuine entry the payload was reaching for lives in msgbox and is reachable ONLY there.
        Assert.Equal("OK", reader.Lookup("en", "msgbox", "确定"));
        Assert.Equal(string.Empty, reader.Lookup("en", "window", "确定"));
        Assert.Equal(string.Empty, reader.Lookup("en", "dwsvc", "确定"));
    }

    /// <summary>
    /// A key that would have appended a union operand no longer crosses the LANGUAGE boundary either,
    /// so the English provider can no longer be made to answer in Traditional Chinese.
    /// </summary>
    /// <remarks>
    /// The strongest form of the class. <c>@lang</c> sat inside the injected operand's own path, so the
    /// union was not confined to the locale the provider was written for - and the provider is the one
    /// component with no language parameter at all, its token being a private literal. Measured under
    /// the legacy, the English provider answered <c>關閉</c>, the Traditional Chinese value from
    /// pfw.i18n.xml:L81. It now declines, and the English value for the same source string still
    /// resolves - so the locale selection is intact rather than merely unreachable.
    /// </remarks>
    [Fact]
    public void AWouldBeUnionOperandNoLongerCrossesTheLanguageBoundary()
    {
        RequireResourceFixture();

        const string Key = "x'] | pfw/window[@lang='cht']/tr[@text='关闭";

        I18nResourceReader reader = new();
        Assert.Equal(string.Empty, reader.Lookup("en", "window", Key));

        EnglishProvider provider = new();
        string? text = Key;
        Assert.Equal(0L, provider.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_WINDOW, ref text));
        Assert.Equal(Key, text);

        // Each locale still answers with its own value for the genuine source string.
        Assert.Equal("Close", reader.Lookup("en", "window", "关闭"));
        Assert.Equal("關閉", reader.Lookup("cht", "window", "关闭"));
    }

    /// <summary>
    /// A key carrying a whole-document descent reaches nothing, in either operand order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two shapes that made the legacy's document-order rule visible, kept because the reasoning is
    /// the evidence. With category <c>msgbox</c> and key
    /// <c>确定'] | pfw/window[@lang='en']/tr[@text='关闭</c>, operand one matched the message-box
    /// <c>确定</c> entry at pfw.i18n.xml:L41 and operand two matched the window <c>关闭</c> entry at
    /// :L6. The measured legacy answer was <c>Close</c> - the SECOND operand - because line 6 precedes
    /// line 41: union is a set operation, the result is delivered in document order, and operand order
    /// carried no precedence at all. Reversing the operands produced the same winner.
    /// </para>
    /// <para>
    /// Both spellings now miss, and so do the two descent payloads (<c>//tr</c> and <c>//*</c>) whose
    /// whole point was to escape the addressed section. The <c>关闭</c> and <c>确定</c> entries both
    /// still resolve through their own categories, so the miss is literal comparison rather than an
    /// unreachable table.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryUnionAndDescentSpellingMissesInEitherOperandOrder()
    {
        RequireResourceFixture();

        I18nResourceReader reader = new();

        const string MessageBoxFirst = "确定'] | pfw/window[@lang='en']/tr[@text='关闭";
        const string WindowFirst = "关闭'] | pfw/msgbox[@lang='en']/tr[@text='确定";

        Assert.Equal(string.Empty, reader.Lookup("en", "msgbox", MessageBoxFirst));
        Assert.Equal(string.Empty, reader.Lookup("en", "window", WindowFirst));

        // The whole-document descents, which no longer have a path to descend.
        Assert.Equal(string.Empty, reader.Lookup("en", "window", "x'] | //tr[@text='关闭"));
        Assert.Equal(string.Empty, reader.Lookup("en", "window", "x'] | //*[@to"));

        // Both genuine entries the payloads were reaching for still resolve, each in its own category.
        Assert.Equal("Close", reader.Lookup("en", "window", "关闭"));
        Assert.Equal("OK", reader.Lookup("en", "msgbox", "确定"));
    }

    /// <summary>
    /// A key whose genuine prefix is a real source text misses in full, because the whole argument is
    /// the key - it is not a prefix that can be split at an apostrophe.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The complementary case, and the one whose outcome genuinely REVERSED rather than merely
    /// narrowing. Under the legacy, <c>关闭'] | pfw/nosuchelement[...]/tr[@text='whatever</c> stayed
    /// well-formed, the dangling operand contributed nothing, and the caller's genuine key still
    /// resolved to <c>Close</c> - so injection could not turn a hit into a miss.
    /// </para>
    /// <para>
    /// Under the traversal the entire argument is one opaque string, so a payload built on a real key is
    /// simply not that key and misses. That is the correct outcome and it is asserted explicitly: a
    /// caller cannot obtain <c>Close</c> by asking for something that is not <c>关闭</c>. The genuine
    /// key is asserted immediately below it so the pair reads as the whole rule.
    /// </para>
    /// </remarks>
    [Fact]
    public void AKeyBuiltOnAGenuineSourceTextIsStillNotThatSourceText()
    {
        RequireResourceFixture();

        const string Key = "关闭'] | pfw/nosuchelement[@lang='en']/tr[@text='whatever";

        I18nResourceReader reader = new();

        Assert.Equal(string.Empty, reader.Lookup("en", "window", Key));
        Assert.Equal("Close", reader.Lookup("en", "window", "关闭"));
    }

    // ==============================================================================================
    //  PART 4 - THE BOUNDARY OF THE BEHAVIOUR
    // ==============================================================================================

    /// <summary>
    /// Document order still decides among genuine duplicate keys, proven on a synthetic table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The legacy semantic that SURVIVES, and the reason it needs its own test now that the crafted
    /// unions do not. <c>string(node-set)</c> takes the first node in document order, so where a section
    /// declares the same source text twice only the FIRST declaration is ever reachable and the later
    /// one is dead data. The traversal reproduces that with <c>FirstOrDefault</c> over a sequence built
    /// in declaration order: sections in the order the file declares them, each section's entries in the
    /// order it declares them.
    /// </para>
    /// <para>
    /// Asserted on a purpose-built table so the winning entry is unambiguous and independent of any
    /// future upstream edit to pfw.i18n.xml. The table also declares the same key in a SECOND section
    /// and in a second language, so the test simultaneously shows that first-in-document-order is scoped
    /// to the addressed section rather than to the document - which is exactly the boundary the crafted
    /// unions used to cross.
    /// </para>
    /// </remarks>
    [Fact]
    public void DocumentOrderStillDecidesAmongGenuineDuplicatesWithinTheAddressedSection()
    {
        string table =
            "<pfw>"
                // Two entries with the SAME key. Only the first is reachable.
                + "<window lang='en'>"
                    + "<tr text='alpha' to='first-declaration'/>"
                    + "<tr text='alpha' to='second-declaration-unreachable'/>"
                + "</window>"
                // The same key in another section, to show the scope is the addressed section.
                + "<msgbox lang='en'><tr text='alpha' to='other-section'/></msgbox>"
                // ...and in another language, to show the language token still scopes it too.
                + "<window lang='cht'><tr text='alpha' to='other-language'/></window>"
            + "</pfw>";

        WithSyntheticTable(table, reader =>
        {
            Assert.Equal("first-declaration", reader.Lookup("en", "window", "alpha"));

            // Scoped to the addressed section and the addressed language, not to the document.
            Assert.Equal("other-section", reader.Lookup("en", "msgbox", "alpha"));
            Assert.Equal("other-language", reader.Lookup("cht", "window", "alpha"));

            // And a crafted key cannot reach across those scopes on this table either.
            Assert.Equal(string.Empty, reader.Lookup("en", "window", AlwaysTrueKey));
            Assert.Equal(
                string.Empty,
                reader.Lookup("en", "window", "x'] | pfw/msgbox[@lang='en']/tr[@text='alpha"));
        });
    }

    /// <summary>
    /// No crafted key, in any position, returns anything but the empty string - and the containment
    /// holds for the category and language arguments as well as for the source text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole-surface sweep, and the assertion that makes this file a guard rather than a catalogue.
    /// Every payload in <see cref="CraftedKeys"/> is tried as the source text across all six categories
    /// and all three language tokens, and then in the CATEGORY and LANGUAGE positions too - because
    /// those are the other two arguments the legacy interpolated, and a fix that closed only the source
    /// text would leave two thirds of the sink open. Measured under the legacy, a language of
    /// <c>' or '1'='1</c> returned the requested text out of ANY language section, defeating the locale
    /// selection entirely.
    /// </para>
    /// <para>
    /// The assertion is exact equality with the empty string rather than membership of an allow-list of
    /// the table's values. An allow-list bounded the blast radius, which was the right statement while
    /// the sink existed; the stronger statement is available now and is worth making, because "returned
    /// some real translation" is precisely the outcome that is no longer acceptable.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoCraftedArgumentInAnyPositionReturnsAnything()
    {
        RequireResourceFixture();

        string[] categories = ["window", "msgbox", "dwsvc", "tabcontrol", "ribbonbar", "splitcontainer"];
        string[] languages = ["en", "cht", "chs"];

        I18nResourceReader reader = new();

        foreach (string key in CraftedKeys)
        {
            // Position 1 - the source text.
            foreach (string category in categories)
            {
                foreach (string language in languages)
                {
                    Assert.Equal(string.Empty, reader.Lookup(language, category, key));
                }
            }

            // Position 2 - the category, which is the NAME of a location step and so could never have
            // been parameterised in XPath at all. It is an XName here, and an unusable one is a miss.
            Assert.Equal(string.Empty, reader.Lookup("en", key, "关闭"));

            // Position 3 - the language, the position whose legacy payload defeated locale selection.
            Assert.Equal(string.Empty, reader.Lookup(key, "window", "关闭"));
        }

        // The three genuine lookups the sweep brackets, so a reader that answered nothing at all could
        // not pass this test.
        Assert.Equal("Close", reader.Lookup("en", "window", "关闭"));
        Assert.Equal("關閉", reader.Lookup("cht", "window", "关闭"));
        Assert.Equal("OK", reader.Lookup("en", "msgbox", "确定"));
    }

    /// <summary>
    /// Every framework-shipped lookup key is inert, and every one of them still resolves - which
    /// together are what make the traversal an unobservable substitute for the interpolated expression.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the DATA property the whole decision rests on, which is exactly why it needs a test. The
    /// framework's own callers pass Chinese source strings from <c>ws_objects</c>, and no apostrophe,
    /// bracket or pipe appears in any of them - so no genuine entry can reach the branch where the
    /// interpolated expression and the traversal differ, and the two agree byte for byte over the whole
    /// shipped table. Every crafted case in this file therefore requires a key from OUTSIDE the
    /// framework's own vocabulary.
    /// </para>
    /// <para>
    /// If an upstream update ever added a key containing an apostrophe, that key would have been
    /// unresolvable under the legacy - a malformed expression, hence a silent miss - while remaining
    /// perfectly resolvable here. This test is what would catch the divergence appearing, at the point
    /// where the decision is to record it rather than to wonder why one translation behaves differently
    /// from the oracle.
    /// </para>
    /// <para>
    /// The entry count is asserted so that a table which shrank, or a walk that stopped early, fails
    /// here instead of silently narrowing the equivalence claim to whatever it happened to reach.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryFrameworkShippedKeyIsInertAndStillResolves()
    {
        RequireResourceFixture();

        I18nResourceReader reader = new();

        int inspected = 0;

        foreach (System.Xml.Linq.XElement section in
            System.Xml.Linq.XDocument.Load(I18nResourceReader.DefaultResourceFileName).Root!.Elements())
        {
            string category = section.Name.LocalName;
            string language = (string?)section.Attribute("lang") ?? string.Empty;

            System.Collections.Generic.HashSet<string> seen = new(StringComparer.Ordinal);

            foreach (System.Xml.Linq.XElement entry in section.Elements("tr"))
            {
                string key = entry.Attribute("text")?.Value ?? string.Empty;
                string translation = entry.Attribute("to")?.Value ?? string.Empty;

                inspected++;

                // INERT: none of the three characters that could leave the string literal.
                Assert.DoesNotContain("'", key, StringComparison.Ordinal);
                Assert.DoesNotContain("]", key, StringComparison.Ordinal);
                Assert.DoesNotContain("|", key, StringComparison.Ordinal);

                // AND STILL RESOLVES. Only the first declaration of a key is reachable - the
                // document-order rule - so a later duplicate is skipped rather than asserted against.
                if (seen.Add(key))
                {
                    Assert.Equal(translation, reader.Lookup(language, category, key));
                }
            }
        }

        Assert.Equal(ShippedEntryCount, inspected);
    }
}
