// ==================================================================================================
//  TraditionalChineseProviderTests.cs - THE TWIN OF THE ENGLISH PROVIDER, DIFFERING IN ONE TOKEN
//  ------------------------------------------------------------------------------------------------
//  UNIT UNDER TEST   PowerFramework.Shared.Localization.TraditionalChineseProvider
//  ORACLE            ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_cht.sru   68 lines, READ ONLY
//  DATA              pfw.i18n.xml, the lang="cht" half at :L77-L151                        READ ONLY
//
//  THE ORACLE, LINE BY LINE, because every assertion below cites it
//  ------------------------------------------------------------------------------------------------
//      :L33        if source <> Enums.I18N_SRC_PFW then return 0      the filter, FIRST
//      :L35-L48    choose case category -> one of six element names   the map
//      :L50        if sCat <> "" then                                 an unmapped category declines
//      :L52        ...[@lang='cht']/tr[@text='{}']/@to                THE ONE TOKEN
//      :L53-L55    if sTo <> "" then text = sTo : return 1            the hit
//      :L59        return 0                                           the miss
//      :L62-L63    _doc = Create n_xmldoc : _doc.LoadFile(...)        load result NEVER checked
//
//  WHAT THIS SUITE HAS TO PROVE THAT EnglishProviderTests DOES NOT
//  ------------------------------------------------------------------------------------------------
//  The two provider bodies are the same body twice over: diff the oracle scripts
//  (n_cst_i18n_cht.sru:L33-L59 against n_cst_i18n_en.sru:L32-L56) and exactly ONE content line
//  differs - the language token inside the query, 'cht' at cht:L52 where en:L51 has 'en'. The ported
//  C# holds that property too: strip comments from both providers and the only differences are the
//  type name and that token.
//
//  The constant one-line offset between the two locator sets is not a discrepancy: cht declares an
//  EXTRA local at :L31 - the unreferenced XML query-result variable - which pushes every subsequent
//  line down by one. Everything else lines up statement for statement. (The English original then runs
//  on to a dormant commented-out translation table at en:L58-L153 that has no cht counterpart; it is
//  asserted absent by MistranslationParityTests, not here.)
//
//  So the interesting assertions here are the ones that would pass for the WRONG provider if the token
//  were wrong, and those are the ones a copy-paste port gets wrong:
//
//      1. EVERY ELEMENT RESOLVES UNDER cht.  A single misspelt token would make all six elements miss
//         at once and the class would silently translate nothing - which no compile step can catch.
//      2. NOT THE ENGLISH SECTION.  For a key present in BOTH language halves with DIFFERENT values,
//         this provider must answer the Traditional value and never the English one. That is the
//         decisive negative: it is the only assertion class that fails when 'cht' is left as 'en',
//         and leaving it as 'en' is precisely what copying the English provider produces.
//      3. THE TWO PRESERVED MISTRANSLATIONS ARE NOT VISIBLE HERE.  They are lang="en" DATA defects
//         [pfw.i18n.xml:L4, :L49]; the cht counterparts at :L79 and :L124 are correct. Asserting them
//         absent here proves the corruption is data-scoped rather than code-scoped, which is a genuine
//         boundary between the two providers rather than duplicated cover.
//      4. A COINCIDING FORM IS A HIT, NOT A MISS.  Traditional and Simplified Chinese agree on fifteen
//         of the table's cht entries, which therefore map a value to ITSELF. The text is unchanged and
//         the RETURN CODE is the only evidence the lookup succeeded. This is the concrete reason
//         II18nProvider returns long, and it is only observable here - the English half of the table
//         has no self-mapping entry at all.
//
//  TEST SHAPE.  Table-driven parity matrices expressed as theories with member data, per AAP §0.6.7,
//  with every expected value quoted from the read-only resource and carrying its line number so a
//  divergence can be adjudicated against the oracle without re-deriving it.
//
//  WHAT IS DELIBERATELY NOT ASSERTED, so nobody adds it
//  ------------------------------------------------------------------------------------------------
//  There is NO test for a defect on this side, because the cht data has none: all six elements are
//  present and none of their 63 values is corrupted. There is NO test for a dormant translation table,
//  because that block exists only in the English original [n_cst_i18n_en.sru:L58-L153]. There is NO
//  document-level XML scan here - this suite reaches the table only through the substituted reader, and
//  the reader's own structural guards plus the byte-level fidelity of the resource are owned by
//  I18nResourceReaderTests.cs and I18nResourceFidelityTests.cs. And there is no test for the
//  unreferenced local of the deferred XML query-result type declared at cht:L31, because the port
//  deliberately does not reproduce it - an inert declaration has no observable behaviour to assert, and
//  its type belongs to a capability this phase must not implement even partially.
//
//  SCOPE NOTE, recorded because it is an auditable departure from this file's brief.  The brief defers
//  "the exhaustive treatment of the filter and the category map" to a CategoryMappingTests.cs. NO SUCH
//  FILE EXISTS in this repository: the sibling CategoriesTests.cs covers the Categories CONSTANTS -
//  their offsets from I18N_CAT_CUSTOM, their verbatim spelling and their compiled metadata - and
//  asserts nothing about any provider's category map. The provider's own map and filter are therefore
//  covered here, because one test assembly covers one shared library and this suite alone carries
//  TraditionalChineseProvider.cs's measured line coverage. What the brief asks for is honoured where it
//  bites: the source filter is confirmed by a SINGLE row, which is all its single branch can take.
//
// ==================================================================================================

using System.IO;
using System.Text;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.Shared.Localization.Tests;

/// <summary>
/// Parity tests for <see cref="TraditionalChineseProvider"/>, the port of
/// <c>n_cst_i18n_cht.sru</c>.
/// </summary>
public class TraditionalChineseProviderTests
{
    /// <summary>
    /// The oracle's "handled" answer. [ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_cht.sru:L55]
    /// </summary>
    /// <remarks>
    /// Named rather than spelled as a bare <c>1L</c> at every call site, because "handled" and "the
    /// text changed" are different claims and this suite exists in large part to keep them apart.
    /// </remarks>
    private const long Handled = 1L;

    /// <summary>
    /// The oracle's "not handled" answer, returned by the source filter, by an unmapped category and by
    /// a table miss alike. [:L33, :L59]
    /// </summary>
    private const long NotHandled = 0L;

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
    /// The name is read from <see cref="I18nResourceReader.DefaultResourceFileName"/> rather than
    /// restated as a literal, and it is a BARE RELATIVE name resolved against the process working
    /// directory - which is the xunit.v3 host's build output directory. No path arithmetic, no absolute
    /// path, and no read of the repository-root original: the copy in the output directory is what every
    /// deployed consumer sees, so it is what these tests must see too.
    /// <para>
    /// Every hit assertion in this file depends on the resource being there, and every MISS assertion
    /// would pass without it - so the guard is what stops a dropped Content link from turning this
    /// suite green for the wrong reason.
    /// </para>
    /// </remarks>
    private static void RequireResourceFixture()
    {
        Assert.True(File.Exists(I18nResourceReader.DefaultResourceFileName), FixtureMissingMessage);
    }

    /// <summary>
    /// Returns a provider over the real, guarded resource table.
    /// </summary>
    /// <returns>A provider reading <c>pfw.i18n.xml</c> from the test working directory.</returns>
    private static TraditionalChineseProvider RealResourceProvider()
    {
        RequireResourceFixture();
        return new TraditionalChineseProvider();
    }

    /// <summary>
    /// Runs <paramref name="assertions"/> against a provider reading a synthetic table written to a
    /// temporary directory, then deletes it.
    /// </summary>
    /// <param name="documentText">The synthetic table.</param>
    /// <param name="assertions">The assertions to run against a provider over that table.</param>
    /// <remarks>
    /// Synthetic tables are used wherever the assertion is about the LOOKUP MECHANISM rather than about
    /// the real resource's content, so a future upstream edit to pfw.i18n.xml cannot silently change
    /// what those tests mean. The real resource is used wherever its actual values ARE the subject, and
    /// those tests say so and cite the line they read.
    /// <para>
    /// The directory name carries a GUID, so parallel test hosts - including parallel clones of this
    /// repository - cannot collide, and the <c>finally</c> removes exactly the directory this call
    /// created and nothing else.
    /// </para>
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
    /// The default constructor is what <c>ws_objects/pfw.pbl.src/pfw.sra</c>'s open event reaches at
    /// :L101 - the framework application, not the same-named packager object - so it has to work
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
        Assert.Equal(Handled, fromDefault.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_WINDOW, ref text));
        Assert.Equal("關閉", text);

        TraditionalChineseProvider fromReader = new(new I18nResourceReader());
        string? viaReader = "确定";
        Assert.Equal(
            Handled,
            fromReader.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_MSGBOX, ref viaReader));
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
    //  2. THE SOURCE FILTER, CONFIRMED BY ONE ROW  [:L33]
    // ==============================================================================================

    /// <summary>
    /// A source other than <see cref="Enums.I18N_SRC_PFW"/> short-circuits BEFORE the category switch,
    /// however well the category and text would otherwise have matched. [:L33]
    /// </summary>
    /// <remarks>
    /// ONE confirming row, because the filter is ONE branch: <c>if source &lt;&gt; I18N_SRC_PFW then
    /// return 0</c>. Extra source values would exercise the same branch again and pin nothing further.
    /// <para>
    /// THE 0/0 HAZARD, and why the category here is deliberately not the obvious one.
    /// <c>Enums.I18N_SRC_PFW</c> and <c>Enums.I18N_CAT_WINDOW</c> are BOTH <c>0</c>
    /// [ws_objects/pfw.shared.pbl.src/enums.sru:L115-L123, reproduced verbatim at
    /// Enums.cs:L367-L375]. A row built from that pair proves nothing about WHICH argument the
    /// implementation read: an implementation that transposed source and category would still pass it.
    /// This row therefore uses <see cref="Enums.I18N_CAT_DATAWINDOW"/>, which is <c>4</c> and so is
    /// distinguishable from either source value, while still mapping to the SAME <c>window</c> element
    /// [:L36] so the key genuinely would have hit. Under a transposition the control assertion below
    /// reads <c>4</c> as the source, fails the filter, and the test fails - which is the point.
    /// </para>
    /// <para>
    /// The text is asserted unchanged on the declining path, and null is carried through the same
    /// branch, which together show the filter runs before anything reads the text at all.
    /// </para>
    /// </remarks>
    [Fact]
    public void ANonFrameworkSourceShortCircuitsBeforeTheCategorySwitch()
    {
        TraditionalChineseProvider provider = RealResourceProvider();

        // The would-hit key, declined purely because the source is not the framework's own.
        string? declined = "还原";
        Assert.Equal(
            NotHandled,
            provider.OnTranslate(Enums.I18N_SRC_CUSTOM, Enums.I18N_CAT_DATAWINDOW, ref declined));
        Assert.Equal("还原", declined);

        // CONTROL: the identical category and key DO resolve for the framework source, so the decline
        // above is a statement about the source argument and not about the key. [pfw.i18n.xml:L80]
        string? admitted = "还原";
        Assert.Equal(
            Handled,
            provider.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_DATAWINDOW, ref admitted));
        Assert.Equal("還原", admitted);

        // ...and the filter precedes every read of the text, so a null key is not even inspected.
        string? nullText = null;
        Assert.Equal(
            NotHandled,
            provider.OnTranslate(Enums.I18N_SRC_CUSTOM, Enums.I18N_CAT_DATAWINDOW, ref nullText));
        Assert.Null(nullText);
    }

    // ==============================================================================================
    //  3. THE OWN LANGUAGE SECTION - EVERY ELEMENT OF THE cht HALF RESOLVES  [:L35-L48, :L52]
    // ==============================================================================================

    /// <summary>
    /// One or more resolving keys per element of the <c>lang="cht"</c> half of the table, quoted from
    /// the read-only resource with the line each was read from.
    /// </summary>
    /// <returns>Category, source key, the Traditional value the resource records, and its locator.</returns>
    /// <remarks>
    /// Every row was transcribed by direct read of pfw.i18n.xml and then re-verified character by
    /// character, because Simplified and Traditional forms of the same word are VISUALLY SIMILAR and a
    /// value recalled from memory produces an expectation that is silently wrong rather than obviously
    /// wrong. The locator column exists so any future divergence is adjudicated against the oracle
    /// line rather than against someone's reading of it.
    /// <para>
    /// All six switch arms are represented, plus the DataWindow alias that shares the window element
    /// [:L36], so a misspelt element name fails exactly one row while a misspelt language token fails
    /// every row at once.
    /// </para>
    /// </remarks>
    public static TheoryData<long, string, string, string> OwnLanguageRows() =>
        new()
        {
            // window  [element at pfw.i18n.xml:L77]
            { Enums.I18N_CAT_WINDOW, "还原", "還原", "pfw.i18n.xml:L80" },
            { Enums.I18N_CAT_WINDOW, "关闭", "關閉", "pfw.i18n.xml:L81" },
            { Enums.I18N_CAT_WINDOW, "窗口列表", "窗體列表", "pfw.i18n.xml:L82" },

            // window, reached through the DataWindow category - the same element, a different arm [:L36]
            { Enums.I18N_CAT_DATAWINDOW, "还原", "還原", "pfw.i18n.xml:L80" },

            // splitcontainer  [element at pfw.i18n.xml:L90]
            {
                Enums.I18N_CAT_SPLITCONTAINER,
                "双击折叠左侧面板",
                "雙擊折疊左側面板",
                "pfw.i18n.xml:L91"
            },
            { Enums.I18N_CAT_SPLITCONTAINER, "展开左侧面板", "展開左側面板", "pfw.i18n.xml:L99" },

            // tabcontrol  [element at pfw.i18n.xml:L104] - its only entry, and a coinciding one
            { Enums.I18N_CAT_TABCONTROL, "固定", "固定", "pfw.i18n.xml:L105" },

            // ribbonbar  [element at pfw.i18n.xml:L107]
            { Enums.I18N_CAT_RIBBONBAR, "折叠功能区", "折疊功能區", "pfw.i18n.xml:L108" },

            // msgbox  [element at pfw.i18n.xml:L111]
            { Categories.CAT_MSGBOX, "提示", "提示", "pfw.i18n.xml:L112" },
            { Categories.CAT_MSGBOX, "询问", "詢問", "pfw.i18n.xml:L113" },
            { Categories.CAT_MSGBOX, "确定", "確定", "pfw.i18n.xml:L116" },

            // dwsvc  [element at pfw.i18n.xml:L126] - the one element with an in-scope consumer
            { Categories.CAT_DWSVC, "第{}行", "第{}行", "pfw.i18n.xml:L127" },
            { Categories.CAT_DWSVC, "修改数据被拒绝", "修改數據被拒絕", "pfw.i18n.xml:L128" },
            { Categories.CAT_DWSVC, "错误", "錯誤", "pfw.i18n.xml:L131" },
        };

    /// <summary>
    /// Every mapped category reads its own element of the <c>lang="cht"</c> table and answers the exact
    /// Traditional value the resource records, comparing ordinally. [:L35-L48, :L52-L55]
    /// </summary>
    /// <param name="category">The category under test.</param>
    /// <param name="key">A source key the resource records under that category's element.</param>
    /// <param name="expected">The Traditional value the resource records for it.</param>
    /// <param name="locator">The resource line <paramref name="expected"/> was read from.</param>
    /// <remarks>
    /// Asserted against the REAL resource, because the subject is the mapping from category to element
    /// AND the language token together. The comparison is ORDINAL - <c>Assert.Equal</c> on strings and
    /// <see cref="System.StringComparison.Ordinal"/> where a comparison is spelled out - which matters
    /// because these values differ from their Simplified counterparts by single code points that a
    /// culture-aware or normalising comparison could fold together.
    /// </remarks>
    [Theory]
    [MemberData(nameof(OwnLanguageRows))]
    public void EveryMappedCategoryTranslatesFromItsOwnElement(
        long category,
        string key,
        string expected,
        string locator)
    {
        TraditionalChineseProvider provider = RealResourceProvider();
        string? text = key;

        Assert.Equal(Handled, provider.OnTranslate(Enums.I18N_SRC_PFW, category, ref text));
        Assert.NotNull(text);
        Assert.True(
            string.Equals(expected, text, System.StringComparison.Ordinal),
            $"{locator} records '{expected}' for '{key}', but the provider answered '{text}'.");
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
        TraditionalChineseProvider provider = RealResourceProvider();

        Assert.NotEqual(Enums.I18N_CAT_WINDOW, Enums.I18N_CAT_DATAWINDOW);

        string? viaWindow = "关闭";
        Assert.Equal(
            Handled,
            provider.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_WINDOW, ref viaWindow));

        string? viaDataWindow = "关闭";
        Assert.Equal(
            Handled,
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
        TraditionalChineseProvider provider = RealResourceProvider();

        string? text = "关闭";
        Assert.Equal(NotHandled, provider.OnTranslate(Enums.I18N_SRC_PFW, category, ref text));
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
        TraditionalChineseProvider provider = RealResourceProvider();

        string? messageBox = "错误";
        Assert.Equal(
            Handled,
            provider.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_MSGBOX, ref messageBox));
        Assert.Equal("錯誤", messageBox);

        string? dataWindowService = "无效的值";
        Assert.Equal(
            Handled,
            provider.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_DWSVC, ref dataWindowService));
        Assert.Equal("無效的值", dataWindowService);
    }

    // ==============================================================================================
    //  4. NOT THE ENGLISH SECTION - THE DECISIVE NEGATIVE  [:L52]
    // ==============================================================================================

    /// <summary>
    /// Keys that exist in BOTH language halves of the table with DIFFERENT values, together with the
    /// Traditional value this provider must answer and the English value it must never answer.
    /// </summary>
    /// <returns>Category, source key, the Traditional value, the English value, and both locators.</returns>
    /// <remarks>
    /// These rows are the only ones in the project that fail when the language token is wrong, which is
    /// why they are enumerated rather than sampled. The last row is deliberately a COINCIDING entry -
    /// the Traditional value equals the key - because it shows the token check does not depend on the
    /// Traditional and Simplified forms differing: read under <c>en</c> it would come back as
    /// "All columns" and the row would fail just as loudly.
    /// </remarks>
    public static TheoryData<long, string, string, string, string> NotTheEnglishSectionRows() =>
        new()
        {
            { Enums.I18N_CAT_WINDOW, "还原", "還原", "Restore", "cht :L80 / en :L5" },
            { Enums.I18N_CAT_WINDOW, "关闭", "關閉", "Close", "cht :L81 / en :L6" },
            { Enums.I18N_CAT_RIBBONBAR, "折叠功能区", "折疊功能區", "Collapse", "cht :L108 / en :L33" },
            { Categories.CAT_MSGBOX, "询问", "詢問", "Question", "cht :L113 / en :L38" },
            {
                Categories.CAT_DWSVC,
                "修改数据被拒绝",
                "修改數據被拒絕",
                "Modified data is rejected",
                "cht :L128 / en :L53"
            },
            { Categories.CAT_DWSVC, "所有列", "所有列", "All columns", "cht :L143 / en :L68" },
        };

    /// <summary>
    /// A key present in both language halves resolves to the TRADITIONAL value and never to the English
    /// one. [:L52]
    /// </summary>
    /// <param name="category">The category under test.</param>
    /// <param name="key">A key the resource records under both <c>lang="en"</c> and <c>lang="cht"</c>.</param>
    /// <param name="traditional">The value the <c>cht</c> half records.</param>
    /// <param name="english">The value the <c>en</c> half records, which must NOT be returned.</param>
    /// <param name="locators">The two resource lines the values were read from.</param>
    /// <remarks>
    /// THIS IS THE ASSERTION THE WHOLE FILE EXISTS FOR. The two provider bodies are otherwise identical
    /// - diff <c>n_cst_i18n_cht.sru:L33-L59</c> against <c>n_cst_i18n_en.sru:L32-L56</c> and the ONLY
    /// content line that differs is the language token inside the query, <c>'cht'</c> at cht:L52 where
    /// en:L51 has <c>'en'</c>. The ported C# preserves that property, so the one mistake a copy-paste
    /// port makes is leaving the token as <c>'en'</c>, and NOTHING ELSE IN THIS SUITE CATCHES IT: the
    /// return code would still be 1, the text would still change, every category arm would still
    /// resolve, and the whole class would still look like it worked. Only comparing the answer against
    /// the OTHER half's value distinguishes the two.
    /// <para>
    /// Both directions are asserted on every row - equal to the Traditional value, NOT equal to the
    /// English one - because either alone is weaker. Equality alone would be satisfied by a table edit;
    /// inequality alone would be satisfied by a provider that answered anything at all, including
    /// garbage.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(NotTheEnglishSectionRows))]
    public void AKeyInBothHalvesResolvesToTheTraditionalValueAndNeverTheEnglishOne(
        long category,
        string key,
        string traditional,
        string english,
        string locators)
    {
        TraditionalChineseProvider provider = RealResourceProvider();
        string? text = key;

        Assert.Equal(Handled, provider.OnTranslate(Enums.I18N_SRC_PFW, category, ref text));

        Assert.True(
            string.Equals(traditional, text, System.StringComparison.Ordinal),
            $"[{locators}] '{key}' must resolve to the Traditional '{traditional}', not '{text}'.");

        Assert.False(
            string.Equals(english, text, System.StringComparison.Ordinal),
            $"[{locators}] '{key}' resolved to the ENGLISH value '{english}'. The provider is reading "
                + "the lang='en' section: the language token at n_cst_i18n_cht.sru:L52 has been left as "
                + "'en' instead of 'cht'.");

        // The control that keeps the row honest: the English half really does record a DIFFERENT value,
        // so the inequality above is a discrimination rather than a tautology.
        EnglishProvider englishProvider = new();
        string? viaEnglish = key;
        Assert.Equal(Handled, englishProvider.OnTranslate(Enums.I18N_SRC_PFW, category, ref viaEnglish));
        Assert.Equal(english, viaEnglish);
        Assert.NotEqual(viaEnglish, text);
    }

    /// <summary>
    /// The two preserved mistranslations are <c>lang="en"</c> DATA defects and are NOT visible through
    /// this provider. [pfw.i18n.xml:L4 and :L49, against :L79 and :L124]
    /// </summary>
    /// <remarks>
    /// The English half of the table collapses the minimise label onto its opposite - <c>最小化</c> is
    /// recorded as "Maximize" at :L4, the same value :L3 records for <c>最大化</c> - and corrupts the
    /// hide label into the mixed-language string at :L49. AAP §0.8.2 requires both be reproduced rather
    /// than fixed, and <c>EnglishProviderTests</c> plus <c>MistranslationParityTests</c> freeze them.
    /// <para>
    /// WHAT THIS TEST ADDS, and why it is a boundary rather than duplicated cover: the corruption is
    /// scoped to the DATA, not to the code, so the cht path must be uncontaminated by it. Both keys are
    /// therefore driven through THIS provider and asserted to answer their correct Traditional values,
    /// and asserted explicitly NOT to answer either corrupted English string. Driving both providers
    /// over the SAME file is what makes the scoping provable: the same two keys answer correctly here
    /// and incorrectly there, which can only be true if the two are reading different sections.
    /// </para>
    /// <para>
    /// Note also what is NOT here: there is no compensating defect on this side. None was invented to
    /// make the twin symmetrical, and none exists to preserve.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheTwoEnglishMistranslationsAreAbsentFromThisPath()
    {
        TraditionalChineseProvider provider = RealResourceProvider();

        // MISTRANSLATION 1 - the minimise label. cht :L79 records it unchanged; en :L4 says "Maximize".
        string? minimise = "最小化";
        Assert.Equal(
            Handled,
            provider.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_WINDOW, ref minimise));
        Assert.Equal("最小化", minimise);
        Assert.NotEqual("Maximize", minimise);

        // MISTRANSLATION 2 - the hide label. cht :L124 records 隱藏; en :L49 says "Htexte details".
        string? hide = "隐藏";
        Assert.Equal(Handled, provider.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_MSGBOX, ref hide));
        Assert.Equal("隱藏", hide);
        Assert.NotEqual("Htexte details", hide);

        // The same file, the other provider: both defects are present, so they are data-scoped.
        EnglishProvider english = new();

        string? englishMinimise = "最小化";
        Assert.Equal(
            Handled,
            english.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_WINDOW, ref englishMinimise));
        Assert.Equal("Maximize", englishMinimise);

        string? englishMaximise = "最大化";
        Assert.Equal(
            Handled,
            english.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_WINDOW, ref englishMaximise));
        Assert.Equal(englishMaximise, englishMinimise);

        string? englishHide = "隐藏";
        Assert.Equal(
            Handled,
            english.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_MSGBOX, ref englishHide));
        Assert.Equal("Htexte details", englishHide);
    }

    // ==============================================================================================
    //  5. COINCIDING TRADITIONAL FORMS - FIFTEEN HITS THAT LOOK LIKE MISSES  [:L53-L55]
    // ==============================================================================================

    /// <summary>
    /// Every <c>lang="cht"</c> entry whose <c>to</c> value is IDENTICAL to its <c>text</c> value.
    /// Fifteen rows, one per entry, enumerated exhaustively rather than sampled.
    /// </summary>
    /// <returns>Category, the key, and the resource line the entry sits on.</returns>
    /// <remarks>
    /// Traditional and Simplified Chinese agree on these words, so the resource records them unchanged.
    /// This is the complete set: the <c>cht</c> half carries exactly fifteen such entries, and
    /// <see cref="TheCoincidingFormsAreEnumeratedExhaustively"/> pins that count so an entry added or
    /// removed upstream cannot slip past this table.
    /// <para>
    /// ONE ROW IS A DATA QUIRK RATHER THAN A LINGUISTIC COINCIDENCE. At :L139 the paste-column label is
    /// left as SIMPLIFIED text inside the Traditional section - <c>粘贴列</c>, where the neighbouring
    /// entries at :L138 and :L140 are properly converted (<c>複製整列的數據</c>,
    /// <c>拷貝粘帖板數據覆蓋到整列</c>). It is pinned here rather than corrected: AAP §0.7.3 C-B forbids
    /// improving legacy behaviour, and "the Traditional section contains one untranslated Simplified
    /// label" is legacy behaviour that the migration must carry across intact. Observably it behaves
    /// exactly like the fourteen genuine coincidences, which is why it belongs in this table.
    /// </para>
    /// </remarks>
    public static TheoryData<long, string, string> CoincidingTraditionalFormRows() =>
        new()
        {
            // window  [element at pfw.i18n.xml:L77]
            { Enums.I18N_CAT_WINDOW, "最大化", "pfw.i18n.xml:L78" },
            { Enums.I18N_CAT_WINDOW, "最小化", "pfw.i18n.xml:L79" },
            { Enums.I18N_CAT_WINDOW, "水平排列", "pfw.i18n.xml:L85" },
            { Enums.I18N_CAT_WINDOW, "垂直排列", "pfw.i18n.xml:L86" },

            // tabcontrol  [element at pfw.i18n.xml:L104]
            { Enums.I18N_CAT_TABCONTROL, "固定", "pfw.i18n.xml:L105" },

            // msgbox  [element at pfw.i18n.xml:L111]
            { Categories.CAT_MSGBOX, "提示", "pfw.i18n.xml:L112" },
            { Categories.CAT_MSGBOX, "警告", "pfw.i18n.xml:L114" },
            { Categories.CAT_MSGBOX, "取消", "pfw.i18n.xml:L117" },
            { Categories.CAT_MSGBOX, "是", "pfw.i18n.xml:L118" },
            { Categories.CAT_MSGBOX, "否", "pfw.i18n.xml:L119" },
            { Categories.CAT_MSGBOX, "中止", "pfw.i18n.xml:L120" },
            { Categories.CAT_MSGBOX, "忽略", "pfw.i18n.xml:L122" },

            // dwsvc  [element at pfw.i18n.xml:L126]
            { Categories.CAT_DWSVC, "第{}行", "pfw.i18n.xml:L127" },

            // ...and the data quirk: Simplified text left inside the Traditional section. NOT corrected.
            { Categories.CAT_DWSVC, "粘贴列", "pfw.i18n.xml:L139" },

            { Categories.CAT_DWSVC, "所有列", "pfw.i18n.xml:L143" },
        };

    /// <summary>
    /// A coinciding Traditional form is a HANDLED HIT whose observable text is unchanged - both facts
    /// asserted together, on every one of the fifteen rows. [:L53-L55]
    /// </summary>
    /// <param name="category">The category under test.</param>
    /// <param name="key">A key whose <c>cht</c> value is the key itself.</param>
    /// <param name="locator">The resource line the entry sits on.</param>
    /// <remarks>
    /// THE PAIRED ASSERTION IS THE WHOLE POINT, and asserting only half of it would be worse than not
    /// testing these rows at all. Walk the oracle: the query at :L52 returns a NON-EMPTY <c>sTo</c>, so
    /// <c>if sTo &lt;&gt; ""</c> at :L53 is TRUE, so :L54 assigns the same value over the top of itself
    /// and :L55 returns 1. The lookup SUCCEEDED; the text merely did not need to change.
    /// <para>
    /// So "the text is unchanged" is a claim that a BROKEN provider also satisfies - one that never
    /// looked the entry up at all and fell through to <c>return 0</c> at :L59 leaves the text untouched
    /// too, and would pass an unchanged-only assertion on every row here. And "the answer is 1" alone
    /// would be satisfied by a provider that returned 1 while overwriting the text with something else.
    /// Only the two together pin the behaviour, and the return code is the ONLY evidence available for
    /// the first half.
    /// </para>
    /// <para>
    /// This is also why <see cref="II18nProvider.OnTranslate"/> returns <c>long</c> rather than
    /// <c>void</c>, and it is only observable on this provider: the <c>lang="en"</c> half of the table
    /// has no self-mapping entry anywhere, so collapsing the contract to "did the text change" would
    /// pass the English suite and silently report every one of these fifteen as a miss. Per AAP §0.7.3
    /// C-B that is the legacy outcome and not a missed translation to be fixed - none of these entries
    /// may be reported as not-handled, and none may be skipped.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(CoincidingTraditionalFormRows))]
    public void ACoincidingTraditionalFormIsAHandledHitAndNotAMiss(long category, string key, string locator)
    {
        TraditionalChineseProvider provider = RealResourceProvider();
        string? text = key;

        long result = provider.OnTranslate(Enums.I18N_SRC_PFW, category, ref text);

        // HALF ONE - the lookup succeeded. Without this the row is satisfied by a provider that never
        // consulted the table, because that one leaves the text untouched as well.
        Assert.True(
            result == Handled,
            $"{locator} records '{key}' mapped to itself, which is a HIT: the oracle assigns at :L54 "
                + $"and returns 1 at :L55. The provider answered {result}, which is the miss path at "
                + ":L59. A coinciding Traditional form is not a missed translation.");

        // HALF TWO - and the observable text is identical. Without this the row is satisfied by a
        // provider that answered 1 while writing something else into the caller's variable.
        Assert.True(
            string.Equals(key, text, System.StringComparison.Ordinal),
            $"{locator} records '{key}' unchanged, so the text must come back identical, not '{text}'.");
    }

    /// <summary>
    /// The coinciding-form table carries exactly fifteen rows, matching the resource exactly.
    /// </summary>
    /// <remarks>
    /// The count is asserted from the theory data itself rather than by re-scanning the document,
    /// because this suite reaches the table only through the substituted reader (AAP §0.7.3 C-D) and the
    /// document-level scan belongs to <c>I18nResourceReaderTests</c> and <c>I18nResourceFidelityTests</c>.
    /// Fifteen is the number of <c>lang="cht"</c> entries whose <c>to</c> equals its <c>text</c>, read
    /// off the read-only resource: :L78, :L79, :L85, :L86, :L105, :L112, :L114, :L117, :L118, :L119,
    /// :L120, :L122, :L127, :L139 and :L143. Pinning it here means a row silently dropped from the
    /// table above fails a test instead of quietly shrinking the matrix.
    /// </remarks>
    [Fact]
    public void TheCoincidingFormsAreEnumeratedExhaustively()
    {
        Assert.Equal(15, CoincidingTraditionalFormRows().Count());
    }

    // ==============================================================================================
    //  6. THE HIT AND THE MISS  [:L50-L57, :L59]
    // ==============================================================================================

    /// <summary>
    /// A hit writes the translation and answers <c>1</c>; a miss leaves the text alone and answers
    /// <c>0</c>. [:L53-L56, :L59]
    /// </summary>
    /// <remarks>
    /// Asserted over a synthetic table because the subject is the mechanism, not the content: two
    /// entries in one element, one looked up and one not.
    /// </remarks>
    [Fact]
    public void AHitWritesAndAnswersOneWhileAMissLeavesTheTextAlone()
    {
        string table =
            "<pfw><window lang='cht'><tr text='SOURCE' to='TARGET'/></window></pfw>";

        WithSyntheticTable(table, provider =>
        {
            string? hit = "SOURCE";
            Assert.Equal(Handled, provider.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_WINDOW, ref hit));
            Assert.Equal("TARGET", hit);

            string? miss = "NOT-IN-TABLE";
            Assert.Equal(
                NotHandled,
                provider.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_WINDOW, ref miss));
            Assert.Equal("NOT-IN-TABLE", miss);
        });
    }

    /// <summary>
    /// Texts absent from the <c>lang="cht"</c> sections of the REAL resource, with the reason each is
    /// expected to miss.
    /// </summary>
    /// <returns>Category, the key, and why it is not in that element's <c>cht</c> entries.</returns>
    public static TheoryData<long, string, string> MissingTextRows() =>
        new()
        {
            // The lookup is ONE WAY. A Traditional value is not itself a key, so translating twice does
            // not resolve a second time. [the value at pfw.i18n.xml:L80 is not a text attribute anywhere]
            { Enums.I18N_CAT_WINDOW, "還原", "a Traditional value, never a key" },

            // Nor is an English value a key, which is the same property from the other side. [:L5, :L38]
            { Enums.I18N_CAT_WINDOW, "Restore", "an English value, never a key" },
            { Categories.CAT_MSGBOX, "Question", "an English value, never a key" },

            // No trimming: the comparison is ordinal and exact, so a stray space is a different key.
            { Categories.CAT_DWSVC, "修改数据被拒绝 ", "the key with one trailing space" },

            // Present in the table, but under a DIFFERENT element. The category boundary is enforced.
            { Categories.CAT_DWSVC, "关闭", "recorded under window at :L81, not under dwsvc" },

            // The empty key, which the provider forwards as-is; no entry carries an empty text.
            { Enums.I18N_CAT_WINDOW, "", "no entry carries an empty text attribute" },
        };

    /// <summary>
    /// A text absent from the <c>cht</c> sections answers <c>0</c> and leaves the caller's text exactly
    /// as it arrived. [:L59]
    /// </summary>
    /// <param name="category">A mapped category, so the miss is about the text and not the map.</param>
    /// <param name="key">The absent text.</param>
    /// <param name="reason">Why the resource does not record it.</param>
    /// <remarks>
    /// Asserted against the REAL resource, because "absent from the actual table" is the claim. Each row
    /// uses a MAPPED category, so the decline cannot be attributed to the switch falling through at
    /// :L50; the only remaining path is the empty lookup result at :L53 falling to :L59.
    /// </remarks>
    [Theory]
    [MemberData(nameof(MissingTextRows))]
    public void ATextAbsentFromTheTraditionalSectionsIsAMiss(long category, string key, string reason)
    {
        TraditionalChineseProvider provider = RealResourceProvider();
        string? text = key;

        long result = provider.OnTranslate(Enums.I18N_SRC_PFW, category, ref text);

        Assert.True(
            result == NotHandled,
            $"'{key}' is {reason}, so the lookup must miss and answer 0, not {result}.");
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
            Assert.Equal(
                Handled,
                provider.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_MSGBOX, ref found));
            Assert.Equal("found", found);

            string? notFound = Key;
            Assert.Equal(
                NotHandled,
                provider.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_WINDOW, ref notFound));
            Assert.Equal(Key, notFound);
        });
    }

    /// <summary>
    /// An entry in another LANGUAGE is a miss: this provider reads only <c>lang='cht'</c>, and there is
    /// no fallback of any kind. [:L52]
    /// </summary>
    /// <remarks>
    /// The synthetic complement of <see cref="AKeyInBothHalvesResolvesToTheTraditionalValueAndNeverTheEnglishOne"/>:
    /// that theory proves the provider prefers <c>cht</c> when BOTH halves carry the key, and this one
    /// proves it does not reach for another half when <c>cht</c> does NOT carry it. Together they pin the
    /// absence of the three fallbacks AAP §0.7.3 C-B forbids inventing: no <c>en</c> fallback, no
    /// <c>chs</c> fallback, and no Simplified-to-Traditional character conversion.
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
            Assert.Equal(
                NotHandled,
                provider.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_DWSVC, ref text));
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
            Assert.Equal(
                NotHandled,
                provider.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_DWSVC, ref empty));
            Assert.Equal("EMPTY-TO", empty);

            string? absent = "NO-TO";
            Assert.Equal(
                NotHandled,
                provider.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_DWSVC, ref absent));
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
        TraditionalChineseProvider provider = RealResourceProvider();

        foreach (long category in new[]
        {
            Enums.I18N_CAT_WINDOW,
            Categories.CAT_MSGBOX,
            Categories.CAT_DWSVC,
            Enums.I18N_CAT_CUSTOM,
        })
        {
            string? text = null;
            Assert.Equal(NotHandled, provider.OnTranslate(Enums.I18N_SRC_PFW, category, ref text));
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
            Assert.Equal(NotHandled, provider.OnTranslate(Enums.I18N_SRC_PFW, category, ref text));
            Assert.Equal("关闭", text);
        }
    }

    // ==============================================================================================
    //  7. VERBATIM DELIVERY
    // ==============================================================================================

    /// <summary>
    /// Braces inside a translation survive unsubstituted, so the caller's own <c>Sprintf</c> can fill
    /// them afterwards. [pfw.i18n.xml:L127]
    /// </summary>
    /// <remarks>
    /// In-scope call sites in the DataWindow service layer wrap this lookup in their own <c>Sprintf</c>
    /// on exactly this row-number entry, so a provider that consumed, filled or rewrote the braces would
    /// break every one of them. A failure here means Kernel's <c>Formatting.Sprintf</c> is rescanning
    /// substituted output; that would be a Kernel defect to report there, never something to work around
    /// in this provider. Note the entry is also a coinciding form, so this row's return code carries the
    /// same weight it does in section 5.
    /// </remarks>
    [Fact]
    public void BracesInATranslationSurviveUnsubstituted()
    {
        TraditionalChineseProvider provider = RealResourceProvider();
        string? text = "第{}行";

        Assert.Equal(Handled, provider.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_DWSVC, ref text));
        Assert.Equal("第{}行", text);
        Assert.Contains("{}", text, System.StringComparison.Ordinal);

        // ...and the braces are still fillable, which is what those call sites do next.
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
            Assert.Equal(
                Handled,
                provider.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_WINDOW, ref text));
            Assert.Equal("  padded value  ", text);
        });
    }

    // ==============================================================================================
    //  8. THE UNESCAPED KEY  [:L52]
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
    /// measured decision recorded in the reader and in this provider's own decision log.
    /// </remarks>
    [Theory]
    [InlineData("it's")]
    [InlineData("'")]
    [InlineData("x' or '1'='1")]
    [InlineData("x'] | pfw/msgbox[@lang='cht']/tr[@text='确定")]
    [InlineData("关闭' or @text='最大化")]
    public void AKeyCarryingAnApostropheIsAMissAndNeverThrows(string key)
    {
        TraditionalChineseProvider provider = RealResourceProvider();
        string? text = key;

        Assert.Equal(
            NotHandled,
            provider.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_WINDOW, ref text));
        Assert.Equal(key, text);
    }

    /// <summary>
    /// No combination of source, category and text throws, whatever is passed. [:L33-L59]
    /// </summary>
    /// <remarks>
    /// The facade invokes this member as a bare statement and has no handler, so an exception here would
    /// escape to the caller of a lookup the legacy answers by leaving the text alone. Every degenerate
    /// input is therefore a miss or a hit and never a fault: the empty key, a very long key, control
    /// characters, XML metacharacters and a null.
    /// </remarks>
    [Fact]
    public void NoCombinationOfInputsThrows()
    {
        TraditionalChineseProvider provider = RealResourceProvider();

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
                    Assert.True(result == NotHandled || result == Handled);
                }
            }
        }
    }

}
