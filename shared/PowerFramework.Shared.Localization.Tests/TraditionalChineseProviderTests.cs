// ==================================================================================================
//  TraditionalChineseProviderTests.cs - THE TWIN OF THE ENGLISH PROVIDER, DIFFERING IN ONE TOKEN
//  ------------------------------------------------------------------------------------------------
//  UNIT UNDER TEST   PowerFramework.Shared.Localization.TraditionalChineseProvider
//  ORACLE            ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_cht.sru   68 lines
//  DATA              pfw.i18n.xml, the lang="cht" half at :L77-L151   READ ONLY
//
//  WHAT THIS SUITE HAS TO PROVE THAT EnglishProviderTests DOES NOT
//  ------------------------------------------------------------------------------------------------
//  The two provider bodies are the same body twice over: diff the oracle scripts
//  (n_cst_i18n_cht.sru:L33-L59 against n_cst_i18n_en.sru:L32-L56) and exactly ONE content line
//  differs - the language token inside the query, 'cht' at cht:L52 where en:L51 has 'en'. The ported
//  C# holds that property too: strip comments from both providers and the only differences are the
//  type name and that token.
//
//  So the interesting assertions here are the ones that would pass for the WRONG provider if the token
//  were wrong, and those are the ones a copy-paste port gets wrong:
//
//      1. EVERY ELEMENT RESOLVES UNDER cht.  A single misspelt token would make all six elements miss
//         at once and the class would silently translate nothing - which no compile step can catch.
//      2. THE TWO PRESERVED MISTRANSLATIONS ARE NOT VISIBLE HERE.  They are lang="en" DATA defects
//         [pfw.i18n.xml:L4, :L49]; the cht counterparts at :L79 and :L124 are correct. This suite
//         asserts BOTH providers over the SAME file to show the corruption is data-scoped rather than
//         code-scoped, which is also what proves the two are reading different sections.
//      3. AN IDENTITY TRANSLATION IS A HIT, NOT A MISS.  Traditional and Simplified Chinese agree on
//         many characters, so several cht entries map a value to ITSELF [:L78, :L85, :L105, :L112].
//         The text is unchanged and the return code is the ONLY evidence the lookup succeeded. This is
//         the concrete reason II18nProvider returns long, and it is only observable here - the English
//         half of the table has no self-mapping entry.
//
//  WHAT IS DELIBERATELY NOT ASSERTED, so nobody adds it
//  ------------------------------------------------------------------------------------------------
//  There is NO test for a defect on this side, because the cht data has none: all six elements are
//  present and none of their 63 values is corrupted. There is NO test for a dormant translation table,
//  because that block exists only in the English original [n_cst_i18n_en.sru:L58-L153]. And there is no
//  test for an unreferenced local of the deferred XML query-result type declared at cht:L31, because
//  the port deliberately does not reproduce it - an inert declaration has no observable behaviour to
//  assert, and its type belongs to a capability this phase must not implement even partially.
//
//  RULES POSITION
//  review_rules returns "No user rules provided.". Constraints cited inline: C-B (replicate behaviour
//  exactly, including the two mapping quirks, and never add a fallback), C-C (the legacy tree and the
//  resource table are read-only oracles), C-D (the XML read is substituted, not ported), C-H (every
//  arm, both early returns and both the hit and the miss path are covered), C-K (boundary decisions
//  are documented where they are reproduced).
// ==================================================================================================

using System.IO;
using System.Text;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.Shared.Localization.Tests;

/// <summary>
/// Parity tests for <see cref="TraditionalChineseProvider"/>.
/// </summary>
public class TraditionalChineseProviderTests
{
    /// <summary>
    /// The message a fixture-guard failure carries, naming the csproj item to restore.
    /// </summary>
    private const string FixtureMissingMessage =
        "pfw.i18n.xml is not in the test working directory, so every lookup below would miss and this "
            + "suite would pass vacuously. Restore the Content item in "
            + "PowerFramework.Shared.Localization.csproj, which is the project that delivers the table "
            + "to every consumer's output directory.";

    /// <summary>
    /// The encoding synthetic fixtures are written with: UTF-8, and NO byte order mark, matching the
    /// real resource so the reader decodes both the same way.
    /// </summary>
    private static readonly UTF8Encoding SyntheticEncoding = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Asserts the real resource is reachable under its bare relative name before a test relies on it.
    /// </summary>
    /// <remarks>
    /// Every hit assertion in this file depends on the resource being there, and every MISS assertion
    /// would pass without it - so the guard is what stops a dropped Content link from turning this
    /// suite green for the wrong reason.
    /// </remarks>
    private static void RequireResourceFixture()
    {
        Assert.True(File.Exists(I18nResourceReader.DefaultResourceFileName), FixtureMissingMessage);
    }

    /// <summary>
    /// Runs <paramref name="assertions"/> against a provider reading a synthetic table written to a
    /// temporary directory, then deletes it.
    /// </summary>
    /// <param name="documentText">The synthetic table.</param>
    /// <param name="assertions">The assertions to run against a provider over that table.</param>
    /// <remarks>
    /// Synthetic tables are used wherever the assertion is about the LOOKUP rather than about the real
    /// resource's content, so a future edit to pfw.i18n.xml - which is read-only, but could be updated
    /// upstream - cannot silently change what these tests mean. The real resource is used only where its
    /// actual values are the subject, and those tests say so.
    /// </remarks>
    private static void WithSyntheticTable(
        string documentText,
        System.Action<TraditionalChineseProvider> assertions)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "pfw-i18n-cht-tests-" + System.Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        try
        {
            string path = Path.Combine(directory, I18nResourceReader.DefaultResourceFileName);
            File.WriteAllText(path, documentText, SyntheticEncoding);
            assertions(new TraditionalChineseProvider(new I18nResourceReader(path)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // ==============================================================================================
    //  1. CONSTRUCTION AND TYPE SHAPE
    // ==============================================================================================

    /// <summary>
    /// The parameterless constructor reads the resource from its bare relative name, and the
    /// reader-taking constructor rejects null. [:L62-L63]
    /// </summary>
    /// <remarks>
    /// The default constructor is what <c>pfw.sra</c>'s open event reaches at :L101, so it has to work
    /// with no arguments at all; the reader-taking one exists so tests and future hosts can point it at
    /// a different table. A null reader is a programming error rather than a translation miss, which is
    /// why it throws where everything else in this provider stays silent.
    /// </remarks>
    [Fact]
    public void BothConstructorsBehaveAsDeclared()
    {
        RequireResourceFixture();

        TraditionalChineseProvider fromDefault = new();
        string? text = "关闭";
        Assert.Equal(1L, fromDefault.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_WINDOW, ref text));
        Assert.Equal("關閉", text);

        TraditionalChineseProvider fromReader = new(new I18nResourceReader());
        string? viaReader = "确定";
        Assert.Equal(1L, fromReader.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_MSGBOX, ref viaReader));
        Assert.Equal("確定", viaReader);

        Assert.Throws<System.ArgumentNullException>(() => new TraditionalChineseProvider(null!));
    }

    /// <summary>
    /// The type is a sealed leaf implementing the provider contract, with no base class and nothing to
    /// dispose.
    /// </summary>
    /// <remarks>
    /// Sealed because the estate's only reference to <c>n_cst_i18n_cht</c> is the instantiation at
    /// <c>pfw.sra</c>:L101 - nothing derives from it - and not disposable because the legacy destructor
    /// releases a document handle that has no managed analogue [:L66]. Both are asserted so a later
    /// "tidy-up" that opens the type for inheritance or bolts a disposal contract onto it fails loudly.
    /// </remarks>
    [Fact]
    public void TheTypeIsASealedLeafImplementingTheProviderContract()
    {
        System.Type provider = typeof(TraditionalChineseProvider);

        Assert.True(provider.IsSealed);
        Assert.True(provider.IsPublic);
        Assert.Contains(typeof(II18nProvider), provider.GetInterfaces());
        Assert.DoesNotContain(typeof(System.IDisposable), provider.GetInterfaces());
        Assert.Equal(typeof(object), provider.BaseType);
    }

    // ==============================================================================================
    //  2. THE SOURCE FILTER  [:L33]
    // ==============================================================================================

    /// <summary>
    /// A source other than <see cref="Enums.I18N_SRC_PFW"/> declines immediately, before any category
    /// work and however well the category and text would have matched. [:L33]
    /// </summary>
    /// <param name="source">The source value.</param>
    /// <remarks>
    /// The order is the point. The filter runs FIRST, so a custom-source caller cannot reach the
    /// framework's own table even by passing a framework category and a key that is in it. Each row
    /// therefore uses a key that DOES resolve for the framework source, which is what makes the decline
    /// a statement about the filter rather than about the key.
    /// </remarks>
    [Theory]
    [InlineData(Enums.I18N_SRC_CUSTOM)]
    [InlineData(2L)]
    [InlineData(-1L)]
    [InlineData(long.MaxValue)]
    public void AForeignSourceDeclinesBeforeAnyLookup(long source)
    {
        RequireResourceFixture();

        TraditionalChineseProvider provider = new();

        string? text = "关闭";
        Assert.Equal(0L, provider.OnTranslate(source, Enums.I18N_CAT_WINDOW, ref text));
        Assert.Equal("关闭", text);

        string? framework = "关闭";
        Assert.Equal(1L, provider.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_WINDOW, ref framework));
        Assert.Equal("關閉", framework);
    }

    /// <summary>
    /// A foreign source declines even when the text is null, so the filter runs before anything reads
    /// the text at all. [:L33]
    /// </summary>
    [Fact]
    public void AForeignSourceDeclinesWithNullTextAndLeavesItNull()
    {
        TraditionalChineseProvider provider = new();
        string? text = null;

        Assert.Equal(0L, provider.OnTranslate(Enums.I18N_SRC_CUSTOM, Enums.I18N_CAT_WINDOW, ref text));
        Assert.Null(text);
    }

    // ==============================================================================================
    //  3. THE CATEGORY MAP, AND ITS TWO QUIRKS  [:L35-L48]
    // ==============================================================================================

    /// <summary>
    /// One resolving key per element of the <c>cht</c> half of the table, so a single misspelt language
    /// token or element name cannot pass unnoticed.
    /// </summary>
    /// <returns>Category, source key, and the translation the resource records for it.</returns>
    public static TheoryData<long, string, string> MappedCategoryRows() =>
        new()
        {
            { Enums.I18N_CAT_WINDOW, "关闭", "關閉" },                          // pfw.i18n.xml:L81
            { Enums.I18N_CAT_TABCONTROL, "固定", "固定" },                      // :L105
            { Enums.I18N_CAT_RIBBONBAR, "折叠功能区", "折疊功能區" },            // :L108
            { Enums.I18N_CAT_SPLITCONTAINER, "展开左侧面板", "展開左側面板" },    // :L99
            { Categories.CAT_MSGBOX, "确定", "確定" },                          // :L116
            { Categories.CAT_DWSVC, "错误", "錯誤" },                           // :L131
        };

    /// <summary>
    /// Every mapped category reads its own element of the <c>lang="cht"</c> table and translates.
    /// [:L35-L48, :L52]
    /// </summary>
    /// <param name="category">The category under test.</param>
    /// <param name="key">A source key the resource records under that category's element.</param>
    /// <param name="expected">The translation the resource records for it.</param>
    /// <remarks>
    /// Asserted against the REAL resource, because the subject is the mapping from category to element
    /// AND the language token together: with the wrong token every row here misses, and with the wrong
    /// element name exactly one row does. The expected values are quoted from the read-only table with
    /// their line numbers so a divergence can be adjudicated without re-deriving them.
    /// </remarks>
    [Theory]
    [MemberData(nameof(MappedCategoryRows))]
    public void EveryMappedCategoryTranslatesFromItsOwnElement(long category, string key, string expected)
    {
        RequireResourceFixture();

        TraditionalChineseProvider provider = new();
        string? text = key;

        Assert.Equal(1L, provider.OnTranslate(Enums.I18N_SRC_PFW, category, ref text));
        Assert.Equal(expected, text);
    }

    /// <summary>
    /// QUIRK 1 - the window and DataWindow categories are distinct values that share ONE element.
    /// [:L36-L37]
    /// </summary>
    /// <remarks>
    /// <c>Enums.I18N_CAT_WINDOW</c> is 0 and <c>Enums.I18N_CAT_DATAWINDOW</c> is 4, and both select
    /// <c>window</c>. No <c>datawindow</c> element exists in the table, so this is not a shorthand: a
    /// DataWindow-category lookup of a window caption RESOLVES, and it resolves to the window entry.
    /// Splitting the arm would silently turn every such lookup into a miss.
    /// </remarks>
    [Fact]
    public void TheWindowAndDataWindowCategoriesAreDistinctValuesThatShareOneElement()
    {
        RequireResourceFixture();

        Assert.NotEqual(Enums.I18N_CAT_WINDOW, Enums.I18N_CAT_DATAWINDOW);

        TraditionalChineseProvider provider = new();

        string? viaWindow = "关闭";
        Assert.Equal(1L, provider.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_WINDOW, ref viaWindow));

        string? viaDataWindow = "关闭";
        Assert.Equal(
            1L,
            provider.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_DATAWINDOW, ref viaDataWindow));

        Assert.Equal("關閉", viaWindow);
        Assert.Equal(viaWindow, viaDataWindow);
    }

    /// <summary>
    /// QUIRK 2 - an unmapped category declines without reaching the table, including
    /// <see cref="Enums.I18N_CAT_CUSTOM"/> itself. [:L35-L48, :L50]
    /// </summary>
    /// <param name="category">A category value the oracle's switch does not list.</param>
    /// <remarks>
    /// The switch covers 0, 1, 2, 3, 4, 6 and 7 and deliberately NOT 5:
    /// <see cref="Enums.I18N_CAT_CUSTOM"/> is the boundary above which a consumer defines its own
    /// categories, not a category with content. Each row passes a key that DOES exist under the window
    /// element, so the decline is a statement about the category rather than about the key.
    /// </remarks>
    // Enums.I18N_CAT_CUSTOM evaluates to 5, so it is written by identifier here and NOT also as the
    // literal 5: a second row would be the same row (xUnit1025) and would restate a value AAP §0.4.5.3
    // requires be read from its declaration.
    [Theory]
    [InlineData(Enums.I18N_CAT_CUSTOM)]
    [InlineData(8L)]
    [InlineData(-1L)]
    [InlineData(long.MaxValue)]
    public void AnUnmappedCategoryDeclinesWithoutReachingTheTable(long category)
    {
        RequireResourceFixture();

        TraditionalChineseProvider provider = new();

        string? text = "关闭";
        Assert.Equal(0L, provider.OnTranslate(Enums.I18N_SRC_PFW, category, ref text));
        Assert.Equal("关闭", text);
    }

    /// <summary>
    /// Both of the library's own categories are mapped, even though only one has an in-scope consumer
    /// in this phase. [:L44-L47]
    /// </summary>
    /// <remarks>
    /// <c>CAT_DWSVC</c> is reached from the DataWindow service layer; <c>CAT_MSGBOX</c>'s only consumers
    /// are visual objects in the deferred DesignSystem library. The arm is reproduced regardless,
    /// because the map must be faithful rather than pruned by a scheduling accident, and this test is
    /// what stops it being pruned later.
    /// </remarks>
    [Fact]
    public void TheLibrarysOwnTwoCategoriesAreBothMapped()
    {
        RequireResourceFixture();

        TraditionalChineseProvider provider = new();

        string? messageBox = "取消";
        Assert.Equal(
            1L,
            provider.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_MSGBOX, ref messageBox));
        Assert.Equal("取消", messageBox);

        string? dataWindowService = "无效的值";
        Assert.Equal(
            1L,
            provider.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_DWSVC, ref dataWindowService));
        Assert.Equal("無效的值", dataWindowService);
    }

    // ==============================================================================================
    //  4. THE HIT AND THE MISS  [:L50-L57, :L59]
    // ==============================================================================================

    /// <summary>
    /// A hit writes the translation and answers <c>1</c>; a miss leaves the text alone and answers
    /// <c>0</c>. [:L53-L56, :L59]
    /// </summary>
    [Fact]
    public void AHitWritesAndAnswersOneWhileAMissLeavesTheTextAlone()
    {
        string table =
            "<pfw><window lang='cht'><tr text='SOURCE' to='TARGET'/></window></pfw>";

        WithSyntheticTable(table, provider =>
        {
            string? hit = "SOURCE";
            Assert.Equal(1L, provider.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_WINDOW, ref hit));
            Assert.Equal("TARGET", hit);

            string? miss = "NOT-IN-TABLE";
            Assert.Equal(0L, provider.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_WINDOW, ref miss));
            Assert.Equal("NOT-IN-TABLE", miss);
        });
    }

    /// <summary>
    /// An identity translation is a HIT and not a miss: the text is unchanged and the return code is
    /// the only evidence. [:L53-L55]
    /// </summary>
    /// <param name="category">The category under test.</param>
    /// <param name="key">A key whose <c>cht</c> translation is the key itself.</param>
    /// <remarks>
    /// Traditional and Simplified Chinese agree on these characters, so the resource records them
    /// unchanged [pfw.i18n.xml:L78, :L85, :L112, :L105]. This is the property that makes
    /// <c>II18nProvider</c>'s <c>long</c> return load-bearing rather than decorative: without it a
    /// caller could not distinguish "translated, and it happens to be identical" from "not translated",
    /// and collapsing the contract to "did the text change" would report every one of these as a miss.
    /// </remarks>
    [Theory]
    [InlineData(Enums.I18N_CAT_WINDOW, "最大化")]
    [InlineData(Enums.I18N_CAT_WINDOW, "水平排列")]
    [InlineData(Categories.CAT_MSGBOX, "提示")]
    [InlineData(Enums.I18N_CAT_TABCONTROL, "固定")]
    public void AnIdentityTranslationIsAHitAndNotAMiss(long category, string key)
    {
        RequireResourceFixture();

        TraditionalChineseProvider provider = new();
        string? text = key;

        Assert.Equal(1L, provider.OnTranslate(Enums.I18N_SRC_PFW, category, ref text));
        Assert.Equal(key, text);
    }

    /// <summary>
    /// An entry under another element is a miss: the category boundary is enforced, not advisory.
    /// [:L50]
    /// </summary>
    [Fact]
    public void AnEntryUnderAnotherElementIsAMiss()
    {
        const string Key = "ONLY-IN-MSGBOX";

        string table = "<pfw><msgbox lang='cht'><tr text='" + Key + "' to='found'/></msgbox></pfw>";

        WithSyntheticTable(table, provider =>
        {
            string? found = Key;
            Assert.Equal(1L, provider.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_MSGBOX, ref found));
            Assert.Equal("found", found);

            string? notFound = Key;
            Assert.Equal(0L, provider.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_WINDOW, ref notFound));
            Assert.Equal(Key, notFound);
        });
    }

    /// <summary>
    /// An entry in another LANGUAGE is a miss: this provider reads only <c>lang='cht'</c>, and there is
    /// no fallback of any kind. [:L52]
    /// </summary>
    /// <remarks>
    /// The complement of the English provider's equivalent test, and the assertion that pins this
    /// file's whole reason to exist. It also pins the ABSENCE of the three fallbacks constraint C-B
    /// forbids: no <c>en</c> fallback, no <c>chs</c> fallback and no Simplified-to-Traditional
    /// conversion. A key present only under another language must simply MISS.
    /// </remarks>
    [Fact]
    public void AnEntryInAnotherLanguageIsAMissWithNoFallback()
    {
        const string Key = "ONLY-IN-EN";

        string table =
            "<pfw>"
                + "<dwsvc lang='en'><tr text='" + Key + "' to='English'/></dwsvc>"
                + "<dwsvc lang='chs'><tr text='" + Key + "' to='简体'/></dwsvc>"
            + "</pfw>";

        WithSyntheticTable(table, provider =>
        {
            string? text = Key;
            Assert.Equal(0L, provider.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_DWSVC, ref text));
            Assert.Equal(Key, text);
        });
    }

    /// <summary>
    /// An empty translation value is a MISS, not a hit that blanks the text. [:L53]
    /// </summary>
    /// <remarks>
    /// The oracle guards with <c>if sTo &lt;&gt; "" then</c>, so an entry whose <c>to</c> is empty - and
    /// an entry with no <c>to</c> attribute at all, which reads the same way - declines. Without that
    /// guard a malformed table entry would erase the caller's text, and since the facade discards the
    /// return code the caller would have no way to notice.
    /// </remarks>
    [Fact]
    public void AnEmptyOrAbsentTranslationValueIsAMiss()
    {
        string table =
            "<pfw><dwsvc lang='cht'>"
                + "<tr text='EMPTY-TO' to=''/>"
                + "<tr text='NO-TO'/>"
            + "</dwsvc></pfw>";

        WithSyntheticTable(table, provider =>
        {
            string? empty = "EMPTY-TO";
            Assert.Equal(0L, provider.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_DWSVC, ref empty));
            Assert.Equal("EMPTY-TO", empty);

            string? absent = "NO-TO";
            Assert.Equal(0L, provider.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_DWSVC, ref absent));
            Assert.Equal("NO-TO", absent);
        });
    }

    /// <summary>
    /// A null key is a miss and stays null: no throw, and no empty string substituted into the
    /// caller's variable. [:L52]
    /// </summary>
    /// <remarks>
    /// <c>II18nProvider</c> declares the text nullable and obliges an implementor to answer "not
    /// handled" rather than throw. The provider passes the empty string down for the lookup, which is
    /// what the legacy's own <c>Sprintf</c> renders a null argument as, and no entry in the table
    /// carries an empty <c>text</c> attribute - so the miss is faithful rather than defensive.
    /// </remarks>
    [Fact]
    public void ANullKeyIsAMissAndStaysNull()
    {
        RequireResourceFixture();

        TraditionalChineseProvider provider = new();

        foreach (long category in new[]
        {
            Enums.I18N_CAT_WINDOW,
            Categories.CAT_MSGBOX,
            Categories.CAT_DWSVC,
            Enums.I18N_CAT_CUSTOM,
        })
        {
            string? text = null;
            Assert.Equal(0L, provider.OnTranslate(Enums.I18N_SRC_PFW, category, ref text));
            Assert.Null(text);
        }
    }

    /// <summary>
    /// With no resource table available at all, every lookup declines and nothing throws. [:L63]
    /// </summary>
    /// <remarks>
    /// The legacy does not check its load result, so a table that never loaded is a run of misses rather
    /// than a fault. This is the behaviour that makes a deployment shipped without pfw.i18n.xml degrade
    /// into untranslated Simplified Chinese source strings instead of failing to start.
    /// </remarks>
    [Fact]
    public void WithNoResourceTableEveryLookupDeclines()
    {
        string absentPath = Path.Combine(
            Path.GetTempPath(),
            "pfw-i18n-cht-absent-" + System.Guid.NewGuid().ToString("N") + ".xml");

        Assert.False(File.Exists(absentPath));

        TraditionalChineseProvider provider = new(new I18nResourceReader(absentPath));

        foreach (long category in new[]
        {
            Enums.I18N_CAT_WINDOW,
            Enums.I18N_CAT_TABCONTROL,
            Enums.I18N_CAT_RIBBONBAR,
            Enums.I18N_CAT_SPLITCONTAINER,
            Enums.I18N_CAT_DATAWINDOW,
            Categories.CAT_MSGBOX,
            Categories.CAT_DWSVC,
        })
        {
            string? text = "关闭";
            Assert.Equal(0L, provider.OnTranslate(Enums.I18N_SRC_PFW, category, ref text));
            Assert.Equal("关闭", text);
        }
    }

    // ==============================================================================================
    //  5. THE REAL RESOURCE, AND THE DEFECTS THAT ARE NOT ON THIS SIDE
    // ==============================================================================================

    /// <summary>
    /// The two preserved mistranslations are <c>lang="en"</c> DATA defects and are NOT visible through
    /// this provider. [pfw.i18n.xml:L4, :L49 against :L79, :L124]
    /// </summary>
    /// <remarks>
    /// Both providers are driven over the SAME file here, which is what makes the point provable: the
    /// same two keys answer correctly under <c>cht</c> and incorrectly under <c>en</c>. So the
    /// corruption is scoped to the data, the two providers really are reading different sections, and
    /// nothing in this provider repairs anything - there is no defect on this side to preserve, and
    /// none was invented to make the twin symmetrical.
    /// </remarks>
    [Fact]
    public void TheEnglishDataDefectsAreNotVisibleThroughThisProvider()
    {
        RequireResourceFixture();

        TraditionalChineseProvider provider = new();

        string? minimise = "最小化";
        Assert.Equal(1L, provider.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_WINDOW, ref minimise));
        Assert.Equal("最小化", minimise);

        string? hide = "隐藏";
        Assert.Equal(1L, provider.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_MSGBOX, ref hide));
        Assert.Equal("隱藏", hide);

        EnglishProvider english = new();

        // The English half collapses the minimise label onto its opposite, and corrupts the hide label.
        string? englishMinimise = "最小化";
        Assert.Equal(
            1L,
            english.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_WINDOW, ref englishMinimise));

        string? englishMaximise = "最大化";
        Assert.Equal(
            1L,
            english.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_WINDOW, ref englishMaximise));

        Assert.Equal(englishMaximise, englishMinimise);
        Assert.NotEqual(minimise, englishMinimise);
    }

    /// <summary>
    /// Braces inside a translation survive unsubstituted, so the caller's own <c>Sprintf</c> can fill
    /// them afterwards. [pfw.i18n.xml:L127]
    /// </summary>
    /// <remarks>
    /// Eleven in-scope call sites in the DataWindow service layer wrap this lookup in their own
    /// <c>Sprintf</c> on exactly this row-number entry, so a provider that consumed, filled or rewrote
    /// the braces would break all eleven. A failure here means Kernel's <c>Formatting.Sprintf</c> is
    /// rescanning substituted output; that would be a Kernel defect to report there, never something to
    /// work around in this provider.
    /// </remarks>
    [Fact]
    public void BracesInATranslationSurviveUnsubstituted()
    {
        RequireResourceFixture();

        TraditionalChineseProvider provider = new();
        string? text = "第{}行";

        Assert.Equal(1L, provider.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_DWSVC, ref text));
        Assert.Equal("第{}行", text);
        Assert.Contains("{}", text, System.StringComparison.Ordinal);

        // ...and the braces are still fillable, which is what those eleven call sites do next.
        Assert.Equal("第7行", Formatting.Sprintf(text, 7L));
    }

    /// <summary>
    /// A translated value is returned verbatim: no trimming, no casing, no normalisation and no
    /// culture-dependent folding. [:L54]
    /// </summary>
    [Fact]
    public void ATranslatedValueIsReturnedVerbatim()
    {
        string table =
            "<pfw><window lang='cht'><tr text='SPACED' to='  padded value  '/></window></pfw>";

        WithSyntheticTable(table, provider =>
        {
            string? text = "SPACED";
            Assert.Equal(1L, provider.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_WINDOW, ref text));
            Assert.Equal("  padded value  ", text);
        });
    }

    // ==============================================================================================
    //  6. THE UNESCAPED KEY  [:L52]
    // ==============================================================================================

    /// <summary>
    /// A key carrying an apostrophe - including every balanced XPath payload built around one - is an
    /// ordinary MISS and never throws. [:L52]
    /// </summary>
    /// <param name="key">The key under test.</param>
    /// <remarks>
    /// OBSERVED BEHAVIOUR, recorded rather than assumed. The provider neither escapes, quotes,
    /// validates nor rejects the key: it hands it to the reader exactly as the caller supplied it. The
    /// legacy spliced it into an XPath expression unescaped, where an unbalanced payload broke the
    /// expression into a miss and a BALANCED one extended it into a different, caller-unrequested
    /// result. The mandated substitute builds no expression at all and compares the key as DATA, so
    /// every row below - the bare apostrophe, the always-true predicate, the union reaching another
    /// element, and the balanced alternative on the same element - is a miss with the text untouched.
    /// The unbalanced outcome matches the legacy exactly; declining to reproduce the balanced one is the
    /// measured decision recorded in the reader and in this provider's DECISION 5.
    /// </remarks>
    [Theory]
    [InlineData("it's")]
    [InlineData("'")]
    [InlineData("x' or '1'='1")]
    [InlineData("x'] | pfw/msgbox[@lang='cht']/tr[@text='确定")]
    [InlineData("关闭' or @text='最大化")]
    public void AKeyCarryingAnApostropheIsAMissAndNeverThrows(string key)
    {
        RequireResourceFixture();

        TraditionalChineseProvider provider = new();
        string? text = key;

        Assert.Equal(0L, provider.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_WINDOW, ref text));
        Assert.Equal(key, text);
    }

    /// <summary>
    /// No combination of source, category and text throws, whatever is passed. [:L33-L59]
    /// </summary>
    /// <remarks>
    /// The facade invokes this member as a bare statement and has no handler, so an exception here would
    /// escape to the caller of a lookup the legacy answers by leaving the text alone. Every degenerate
    /// input is therefore a miss: the empty key, a very long key, control characters, XML metacharacters
    /// and a null.
    /// </remarks>
    [Fact]
    public void NoCombinationOfInputsThrows()
    {
        RequireResourceFixture();

        TraditionalChineseProvider provider = new();

        long[] sources = [Enums.I18N_SRC_PFW, Enums.I18N_SRC_CUSTOM, -1L, long.MinValue];
        long[] categories =
        [
            Enums.I18N_CAT_WINDOW,
            Enums.I18N_CAT_DATAWINDOW,
            Enums.I18N_CAT_CUSTOM,
            Categories.CAT_DWSVC,
            long.MaxValue,
        ];
        string?[] texts = [null, string.Empty, "关闭", "<&>\"'", new string('x', 4096), "\t\r\n"];

        foreach (long source in sources)
        {
            foreach (long category in categories)
            {
                foreach (string? candidate in texts)
                {
                    string? text = candidate;
                    long result = provider.OnTranslate(source, category, ref text);
                    Assert.True(result == 0L || result == 1L);
                }
            }
        }
    }
}
