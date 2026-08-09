// ==================================================================================================
//  EnglishProviderTests.cs - THE ENGLISH TRANSLATION PROVIDER
//  ------------------------------------------------------------------------------------------------
//  UNIT UNDER TEST   PowerFramework.Shared.Localization.EnglishProvider
//  ORACLES           ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_en.sru   164 lines
//                    pfw.i18n.xml                                              the translation table
//
//  WHAT THIS PROVIDER ACTUALLY DOES, IN THE ORACLE'S OWN ORDER
//  ------------------------------------------------------------------------------------------------
//      :L24   call super::ontranslate                 reaches an EMPTY handler; nothing to invoke
//      :L32   if source <> I18N_SRC_PFW then return 0 the source filter, BEFORE any category work
//      :L34   choose case category ... end choose     the category-to-element map, NO 'case else'
//      :L49   if sCat <> "" then                      an unmapped category never reaches the table
//      :L51   sTo = _doc.Query(Sprintf(...))          the XPath lookup, key interpolated UNESCAPED
//      :L53   if sTo <> "" then text = sTo : return 1 written and handled ONLY on a hit
//      :L57   return 0                                a miss is a decline, text untouched
//
//  THE TWO QUIRKS THAT ARE NOT BUGS
//  ------------------------------------------------------------------------------------------------
//  QUIRK 1 - WINDOW AND DATAWINDOW SHARE ONE ELEMENT. [:L35-L36] I18N_CAT_WINDOW (0) and
//  I18N_CAT_DATAWINDOW (4) are two distinct categories that both select the 'window' element, because
//  pfw.i18n.xml has no 'datawindow' element at all. The measured consequence is load-bearing rather
//  than cosmetic: a DataWindow-category lookup of a window caption RESOLVES. Inventing a 'datawindow'
//  element would be inventing data into a read-only file (C-C) and would turn every such lookup into
//  a miss.
//
//  QUIRK 2 - I18N_CAT_CUSTOM HAS NO ARM, SO IT NEVER TRANSLATES. [:L34-L47] The switch covers 0, 1, 2,
//  3, 4, 6 and 7 and deliberately NOT 5. I18N_CAT_CUSTOM is the boundary above which a consumer
//  defines its own categories, not a category with content, so the two defined above it have arms and
//  the boundary itself does not. Every value the legacy does not list - 5, anything above 7, and any
//  negative - leaves the element name empty and skips the lookup.
//
//  THE TWO MISTRANSLATIONS, REPRODUCED VERBATIM (C-B)
//  ------------------------------------------------------------------------------------------------
//  pfw.i18n.xml carries two wrong English values, and both must survive:
//      L4    最小化  ->  "Maximize"        the English word for its OPPOSITE (minimize)
//      L49   隐藏    ->  "Htexte details"  a corrupted mixed-language string (hide)
//  n_cst_i18n_en.sru:L58-L153 retains a COMMENTED-OUT pre-XML table containing the CORRECT wording -
//  "Minimize" and "Hide" - and it must not be revived. Reviving it would be exactly the silent
//  correction the constraint forbids, and these two values are the canonical example of it in the
//  whole refactor. The tests below assert the wrong values on purpose and say so at each one.
//
//  RULES POSITION
//  review_rules returns "No user rules provided.". Constraints cited inline: C-B (replicate defects),
//  C-C (the legacy tree and pfw.i18n.xml are read-only), C-K (document boundary decisions).
// ==================================================================================================

using System.Globalization;
using System.IO;
using System.Text;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.Shared.Localization.Tests;

/// <summary>
/// Parity tests for <see cref="EnglishProvider"/>.
/// </summary>
public class EnglishProviderTests
{
    /// <summary>
    /// The message a fixture-guard failure carries, naming the csproj item to restore.
    /// </summary>
    private const string FixtureMissingMessage =
        "pfw.i18n.xml is not in the test working directory, so every lookup below would miss and this "
            + "suite would pass vacuously. Restore the Content item in "
            + "PowerFramework.Shared.Localization.Tests.csproj: "
            + "<Content Include=\"../../pfw.i18n.xml\" Link=\"pfw.i18n.xml\" "
            + "CopyToOutputDirectory=\"PreserveNewest\" />";

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
    /// upstream - cannot silently change what these tests mean. The real resource is used only where
    /// its actual values are the subject, and those tests say so.
    /// </remarks>
    private static void WithSyntheticTable(string documentText, System.Action<EnglishProvider> assertions)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"pfw-i18n-en-tests-{System.Guid.NewGuid():N}");

        Directory.CreateDirectory(directory);

        try
        {
            string path = Path.Combine(directory, I18nResourceReader.DefaultResourceFileName);
            File.WriteAllText(path, documentText, SyntheticEncoding);
            assertions(new EnglishProvider(new I18nResourceReader(path)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // ==============================================================================================
    //  1. CONSTRUCTION
    // ==============================================================================================

    /// <summary>
    /// The parameterless constructor reads the resource from its bare relative name, and the
    /// reader-taking constructor rejects null.
    /// </summary>
    /// <remarks>
    /// The default constructor is what <c>pfw.sra</c>'s open event reaches, so it has to work with no
    /// arguments at all; the reader-taking one exists so tests and future hosts can point it at a
    /// different table. A null reader is a programming error rather than a translation miss, which is
    /// why it throws where everything else in this library stays silent.
    /// </remarks>
    [Fact]
    public void BothConstructorsBehaveAsDeclared()
    {
        RequireResourceFixture();

        EnglishProvider fromDefault = new();
        string? text = "最大化";
        Assert.Equal(1L, fromDefault.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_WINDOW, ref text));
        Assert.Equal("Maximize", text);

        Assert.Throws<System.ArgumentNullException>(() => new EnglishProvider(null!));
    }

    // ==============================================================================================
    //  2. THE SOURCE FILTER  [:L32]
    // ==============================================================================================

    /// <summary>
    /// A source other than <see cref="Enums.I18N_SRC_PFW"/> declines immediately, before any category
    /// work and however well the category and text would have matched. [:L32]
    /// </summary>
    /// <param name="source">The source value.</param>
    /// <remarks>
    /// The order is the point. The filter runs FIRST, so a custom-source caller cannot reach the
    /// framework's own table even by passing a framework category and a key that is in it. The rows
    /// below therefore use a key that DOES resolve for the framework source, which is what makes the
    /// decline a statement about the filter rather than about the key.
    /// </remarks>
    [Theory]
    [InlineData(1L)]
    [InlineData(2L)]
    [InlineData(-1L)]
    [InlineData(long.MaxValue)]
    public void AForeignSourceDeclinesBeforeAnyLookup(long source)
    {
        RequireResourceFixture();

        EnglishProvider provider = new();

        string? text = "最大化";
        Assert.Equal(0L, provider.OnTranslate(source, Enums.I18N_CAT_WINDOW, ref text));
        Assert.Equal("最大化", text);

        // ...and the same key with the framework source DOES resolve, so the row above is the filter
        // rather than a miss.
        string? framework = "最大化";
        Assert.Equal(1L, provider.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_WINDOW, ref framework));
        Assert.Equal("Maximize", framework);
    }

    /// <summary>
    /// A foreign source declines even when the text is null, so the filter runs before anything reads
    /// the text at all.
    /// </summary>
    [Fact]
    public void AForeignSourceDeclinesWithNullTextAndLeavesItNull()
    {
        RequireResourceFixture();

        EnglishProvider provider = new();
        string? text = null;

        Assert.Equal(0L, provider.OnTranslate(Enums.I18N_SRC_CUSTOM, Enums.I18N_CAT_WINDOW, ref text));
        Assert.Null(text);
    }

    // ==============================================================================================
    //  3. THE CATEGORY MAP  [:L34-L47]
    // ==============================================================================================

    /// <summary>
    /// Every mapped category reaches its own element, and each of the six elements is reachable.
    /// [:L35-L46]
    /// </summary>
    /// <param name="category">The category value.</param>
    /// <param name="element">The element it selects.</param>
    /// <remarks>
    /// Driven against a SYNTHETIC table whose every element carries the same key mapped to a
    /// DIFFERENT value, so the answer identifies which element was read. Against the real resource the
    /// same test would only prove that some lookup succeeded.
    /// </remarks>
    [Theory]
    [InlineData(0L, "window")]           // I18N_CAT_WINDOW        :L35-L36
    [InlineData(1L, "tabcontrol")]       // I18N_CAT_TABCONTROL    :L41-L42
    [InlineData(2L, "ribbonbar")]        // I18N_CAT_RIBBONBAR     :L37-L38
    [InlineData(3L, "splitcontainer")]   // I18N_CAT_SPLITCONTAINER :L39-L40
    [InlineData(4L, "window")]           // I18N_CAT_DATAWINDOW - QUIRK 1, shares 'window'  :L35-L36
    [InlineData(6L, "msgbox")]           // Categories.CAT_MSGBOX  :L43-L44
    [InlineData(7L, "dwsvc")]            // Categories.CAT_DWSVC   :L45-L46
    public void EveryMappedCategoryReadsItsOwnElement(long category, string element)
    {
        const string Key = "PROBE-KEY";

        string table =
            "<pfw>"
                + $"<window lang='en'><tr text='{Key}' to='from-window'/></window>"
                + $"<splitcontainer lang='en'><tr text='{Key}' to='from-splitcontainer'/></splitcontainer>"
                + $"<tabcontrol lang='en'><tr text='{Key}' to='from-tabcontrol'/></tabcontrol>"
                + $"<ribbonbar lang='en'><tr text='{Key}' to='from-ribbonbar'/></ribbonbar>"
                + $"<msgbox lang='en'><tr text='{Key}' to='from-msgbox'/></msgbox>"
                + $"<dwsvc lang='en'><tr text='{Key}' to='from-dwsvc'/></dwsvc>"
            + "</pfw>";

        WithSyntheticTable(table, provider =>
        {
            string? text = Key;

            Assert.Equal(1L, provider.OnTranslate(Enums.I18N_SRC_PFW, category, ref text));
            Assert.Equal($"from-{element}", text);
        });
    }

    /// <summary>
    /// QUIRK 1, asserted as an identity rather than as two values: the window and DataWindow categories
    /// are DIFFERENT numbers that answer IDENTICALLY, because both select the <c>window</c> element.
    /// [:L35-L36]
    /// </summary>
    /// <remarks>
    /// Stated as an equality between two lookups so it cannot be satisfied by a coincidence of table
    /// contents, plus the inequality of the two category values themselves so the claim is not
    /// vacuous. The measured consequence the production comment records - that a DataWindow-category
    /// lookup of a window caption RESOLVES - is the second assertion.
    /// </remarks>
    [Fact]
    public void TheWindowAndDataWindowCategoriesAreDistinctValuesThatShareOneElement()
    {
        RequireResourceFixture();

        Assert.NotEqual(Enums.I18N_CAT_WINDOW, Enums.I18N_CAT_DATAWINDOW);

        EnglishProvider provider = new();

        string? asWindow = "窗口列表";
        long windowCode = provider.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_WINDOW, ref asWindow);

        string? asDataWindow = "窗口列表";
        long dataWindowCode = provider.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_DATAWINDOW, ref asDataWindow);

        Assert.Equal(windowCode, dataWindowCode);
        Assert.Equal(asWindow, asDataWindow);

        // And it RESOLVES rather than both missing, which is what makes the shared element observable.
        Assert.Equal(1L, dataWindowCode);
        Assert.Equal("Window List", asDataWindow);
    }

    /// <summary>
    /// QUIRK 2: an unmapped category never reaches the table, so it declines whatever the key is.
    /// [:L34-L47, :L49]
    /// </summary>
    /// <param name="category">An unmapped category value.</param>
    /// <remarks>
    /// <see cref="Enums.I18N_CAT_CUSTOM"/> (5) is the row that matters: it is a real, declared constant
    /// with no arm, so an implementation that added a <c>case else</c> mapping it to any element would
    /// start translating text the legacy never translates. The synthetic table gives EVERY element the
    /// probe key, so a decline here can only come from the category map and not from a missing entry.
    /// </remarks>
    [Theory]
    [InlineData(5L)]                 // Enums.I18N_CAT_CUSTOM - declared, and deliberately unmapped
    [InlineData(8L)]
    [InlineData(100L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    public void AnUnmappedCategoryDeclinesWithoutReachingTheTable(long category)
    {
        const string Key = "PROBE-KEY";

        string table =
            "<pfw>"
                + $"<window lang='en'><tr text='{Key}' to='from-window'/></window>"
                + $"<msgbox lang='en'><tr text='{Key}' to='from-msgbox'/></msgbox>"
                + $"<dwsvc lang='en'><tr text='{Key}' to='from-dwsvc'/></dwsvc>"
            + "</pfw>";

        WithSyntheticTable(table, provider =>
        {
            string? text = Key;

            Assert.Equal(0L, provider.OnTranslate(Enums.I18N_SRC_PFW, category, ref text));
            Assert.Equal(Key, text);
        });
    }

    /// <summary>
    /// The two categories the Localization library declares itself are the ones the provider's own map
    /// reaches, so the two files cannot drift apart.
    /// </summary>
    /// <remarks>
    /// <c>Categories.CAT_MSGBOX</c> and <c>Categories.CAT_DWSVC</c> are declared as offsets from
    /// <c>Enums.I18N_CAT_CUSTOM</c> [ne_cst_i18n.sru:L16-L17], and this provider's switch reads them by
    /// identifier. Asserting the round trip here is what catches a change to either declaration: a
    /// shifted offset would silently unmap the live DataWindow-service category, which is the one on a
    /// real code path in this phase.
    /// </remarks>
    [Fact]
    public void TheLibrarysOwnTwoCategoriesAreBothMapped()
    {
        RequireResourceFixture();

        Assert.Equal(Enums.I18N_CAT_CUSTOM + 1L, Categories.CAT_MSGBOX);
        Assert.Equal(Enums.I18N_CAT_CUSTOM + 2L, Categories.CAT_DWSVC);

        EnglishProvider provider = new();

        string? messageBoxText = "确定";
        Assert.Equal(1L, provider.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_MSGBOX, ref messageBoxText));
        Assert.Equal("OK", messageBoxText);

        string? dataWindowServiceText = "无效的值";
        Assert.Equal(1L, provider.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_DWSVC, ref dataWindowServiceText));
        Assert.Equal("Invalid value", dataWindowServiceText);
    }

    // ==============================================================================================
    //  4. HIT AND MISS  [:L51-L57]
    // ==============================================================================================

    /// <summary>
    /// A hit writes the translation and answers 1; a miss leaves the text untouched and answers 0.
    /// [:L53, :L57]
    /// </summary>
    /// <remarks>
    /// The miss half is the one that has to be exact. Leaving the text alone is what makes a miss
    /// indistinguishable from having no provider at all, which is the silent-passthrough posture the
    /// facade depends on - so a miss that blanked the text would turn "no English translation" into
    /// "no text".
    /// </remarks>
    [Fact]
    public void AHitWritesAndAnswersOneWhileAMissLeavesTheTextAlone()
    {
        RequireResourceFixture();

        EnglishProvider provider = new();

        string? hit = "关闭";
        Assert.Equal(1L, provider.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_WINDOW, ref hit));
        Assert.Equal("Close", hit);

        string? miss = "<<NOT-IN-ANY-TABLE>>";
        Assert.Equal(0L, provider.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_WINDOW, ref miss));
        Assert.Equal("<<NOT-IN-ANY-TABLE>>", miss);
    }

    /// <summary>
    /// A null text is a miss rather than a fault, and comes back null.
    /// </summary>
    /// <remarks>
    /// The provider interpolates the text into an XPath predicate, so null has to render as something.
    /// It renders as nothing, producing <c>tr[@text='']</c>, which matches no entry - so the answer is
    /// a decline. Asserted because the alternative implementations a reader might expect, a throw or an
    /// empty-string substitution, are both observable and both wrong.
    /// </remarks>
    [Fact]
    public void ANullTextIsAMissAndStaysNull()
    {
        RequireResourceFixture();

        EnglishProvider provider = new();
        string? text = null;

        Assert.Equal(0L, provider.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_DWSVC, ref text));
        Assert.Null(text);
    }

    /// <summary>
    /// An entry present under the WRONG element is a miss, so the category boundary is enforced by the
    /// lookup and not merely by the map.
    /// </summary>
    /// <remarks>
    /// The complement of the category-map theory. That one proves each category reads its own element;
    /// this proves reading the wrong element finds nothing, which is what makes the boundary meaningful
    /// rather than advisory.
    /// </remarks>
    [Fact]
    public void AnEntryUnderAnotherElementIsAMiss()
    {
        const string Key = "ONLY-IN-MSGBOX";

        string table = $"<pfw><msgbox lang='en'><tr text='{Key}' to='found'/></msgbox></pfw>";

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
    /// An entry in another LANGUAGE is a miss: this provider reads only <c>lang='en'</c>.
    /// [:L51]
    /// </summary>
    /// <remarks>
    /// The language is baked into the provider's own format literal rather than passed as an argument,
    /// which is why the framework ships one class per locale instead of one parameterised lookup. That
    /// design is only observable through this behaviour, so it is asserted here.
    /// </remarks>
    [Fact]
    public void AnEntryInAnotherLanguageIsAMiss()
    {
        const string Key = "ONLY-IN-CHT";

        string table = $"<pfw><dwsvc lang='cht'><tr text='{Key}' to='傳統中文'/></dwsvc></pfw>";

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
    /// The oracle guards with <c>if sTo &lt;&gt; "" then</c>, so an entry whose <c>to</c> is empty -
    /// and an entry with no <c>to</c> attribute at all, which reads the same way - declines. Without
    /// that guard a malformed table entry would erase the caller's text, and since the facade discards
    /// the return code the caller would have no way to notice.
    /// </remarks>
    [Fact]
    public void AnEmptyOrAbsentTranslationValueIsAMiss()
    {
        string table =
            "<pfw><dwsvc lang='en'>"
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
    /// With no resource table available at all, every lookup declines and nothing throws. [:L51]
    /// </summary>
    /// <remarks>
    /// The legacy does not check <c>LoadFile</c>'s result, so a table that never loaded is a run of
    /// misses rather than a fault - which is why the reader returns a null document and the provider
    /// simply finds nothing. This is the behaviour that makes a deployment shipped without
    /// pfw.i18n.xml degrade into untranslated Chinese source strings instead of failing to start.
    /// </remarks>
    [Fact]
    public void WithNoResourceTableEveryLookupDeclines()
    {
        string absentPath = Path.Combine(
            Path.GetTempPath(),
            $"pfw-i18n-absent-{System.Guid.NewGuid():N}.xml");

        Assert.False(File.Exists(absentPath));

        EnglishProvider provider = new(new I18nResourceReader(absentPath));

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
    //  5. THE TWO MISTRANSLATIONS  (C-B)
    // ==============================================================================================

    /// <summary>
    /// MISTRANSLATION 1, REPRODUCED ON PURPOSE: <c>最小化</c> - minimize - translates to
    /// <c>"Maximize"</c>. [pfw.i18n.xml:L4]
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is not a typo in the test. The read-only translation table maps the Chinese for "minimize"
    /// onto the English word for its opposite, and both the window element's own <c>最大化</c> entry
    /// [L3] and this one answer <c>"Maximize"</c> - so a window menu built from this table offers
    /// "Maximize" twice and never offers "Minimize".
    /// </para>
    /// <para>
    /// <c>n_cst_i18n_en.sru:L58-L153</c> retains a commented-out pre-XML table whose value for this key
    /// is the CORRECT <c>"Minimize"</c>. Reviving it - or "fixing" this assertion - is exactly the
    /// silent correction C-B forbids. The assertion below also states the negative, so an attempt to
    /// correct the behaviour fails on a named row that explains itself rather than on an equality
    /// mismatch someone might read as a broken test.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheMinimizeKeyTranslatesToMaximizeWhichIsThePreservedMistranslation()
    {
        RequireResourceFixture();

        EnglishProvider provider = new();

        string? minimize = "最小化";
        Assert.Equal(1L, provider.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_WINDOW, ref minimize));
        Assert.Equal("Maximize", minimize);

        // NOT the correct wording, which is what the commented-out legacy table holds.
        Assert.NotEqual("Minimize", minimize);

        // The genuine maximize key answers the same way, so the table really does say it twice.
        string? maximize = "最大化";
        Assert.Equal(1L, provider.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_WINDOW, ref maximize));
        Assert.Equal("Maximize", maximize);
        Assert.Equal(minimize, maximize);
    }

    /// <summary>
    /// MISTRANSLATION 2, REPRODUCED ON PURPOSE: <c>隐藏</c> - hide - translates to the corrupted
    /// <c>"Htexte details"</c>. [pfw.i18n.xml:L49]
    /// </summary>
    /// <remarks>
    /// <para>
    /// The value is a corrupted mixed-language string, and the corruption is visible in its
    /// neighbourhood: the entry immediately above it maps <c>查看详情</c> to <c>"Show details"</c>
    /// [L48], and this one reads as <c>"H"</c> plus the word <c>texte</c> plus that neighbour's tail -
    /// the fingerprint of a botched search and replace in the table itself.
    /// </para>
    /// <para>
    /// Reproduced verbatim under C-B, and the correct wording <c>"Hide"</c> from the commented-out
    /// legacy table is asserted ABSENT so a well-meant repair fails loudly here.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheHideKeyTranslatesToACorruptedStringWhichIsThePreservedMistranslation()
    {
        RequireResourceFixture();

        EnglishProvider provider = new();

        string? hide = "隐藏";
        Assert.Equal(1L, provider.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_MSGBOX, ref hide));
        Assert.Equal("Htexte details", hide);

        // NOT the correct wording from the commented-out legacy table.
        Assert.NotEqual("Hide", hide);

        // Its neighbour, which is correct - so the corruption is one entry rather than the element.
        string? showDetails = "查看详情";
        Assert.Equal(1L, provider.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_MSGBOX, ref showDetails));
        Assert.Equal("Show details", showDetails);
    }

    // ==============================================================================================
    //  6. THE SHAPE OF THE ANSWER
    // ==============================================================================================

    /// <summary>
    /// A translated value is returned exactly as the table records it, with no trimming, casing or
    /// culture-dependent transformation applied on the way out.
    /// </summary>
    /// <remarks>
    /// No repair on read (C-C): whatever the attribute says is what the caller gets. The rows include a
    /// value with a placeholder in it - <c>Line {}</c> [pfw.i18n.xml:L52] - because that entry is the
    /// reason the localization chain and Sprintf are coupled, and any trimming or escaping applied here
    /// would break the caller that formats it afterwards.
    /// </remarks>
    [Fact]
    public void ATranslatedValueIsReturnedVerbatim()
    {
        RequireResourceFixture();

        EnglishProvider provider = new();

        string? withPlaceholder = "第{}行";
        Assert.Equal(1L, provider.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_DWSVC, ref withPlaceholder));
        Assert.Equal("Line {}", withPlaceholder);

        // The placeholder survived, which is what the coupled Sprintf call downstream depends on.
        Assert.Contains("{}", withPlaceholder, System.StringComparison.Ordinal);

        string? untrimmed = "双击折叠左侧面板";
        Assert.Equal(1L, provider.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_SPLITCONTAINER, ref untrimmed));
        Assert.Equal("Double-click to collapse the left panel", untrimmed);
    }

    /// <summary>
    /// The lookup answers identically under a culture whose casing rules differ from the invariant
    /// ones, so no step in the path is culture-sensitive.
    /// </summary>
    /// <remarks>
    /// Turkish is the standard probe for this because its dotless-i casing makes an ordinary
    /// <c>ToLower</c> or <c>ToUpper</c> produce a different letter than the invariant culture would.
    /// The element names in this provider - <c>window</c>, <c>msgbox</c>, <c>dwsvc</c> - and the
    /// <c>lang='en'</c> token are all lower-case ASCII with an <c>i</c> in <c>splitcontainer</c>, so a
    /// culture-sensitive casing call anywhere in the XPath construction would silently stop matching
    /// under this culture and nowhere else. Asserted rather than assumed because the failure mode is
    /// invisible on a machine whose culture happens to be invariant-like, which is every CI agent.
    /// </remarks>
    [Fact]
    public void TheLookupIsCultureIndependent()
    {
        RequireResourceFixture();

        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        CultureInfo previousUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo turkish = new("tr-TR");
            CultureInfo.CurrentCulture = turkish;
            CultureInfo.CurrentUICulture = turkish;

            EnglishProvider provider = new();

            string? splitContainer = "展开左侧面板";
            Assert.Equal(
                1L,
                provider.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_SPLITCONTAINER, ref splitContainer));
            Assert.Equal("Expand the left panel", splitContainer);

            string? tabControl = "固定";
            Assert.Equal(
                1L,
                provider.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_TABCONTROL, ref tabControl));
            Assert.Equal("Dock", tabControl);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    /// <summary>
    /// Nothing on the provider throws, for any combination of source, category and text.
    /// </summary>
    /// <remarks>
    /// Includes the apostrophe-bearing key that produces a MALFORMED XPath expression, which is the one
    /// input most likely to escape as an exception - the reader catches it and reports a miss, and this
    /// sweep is what proves the catch is reached through the provider rather than only through the
    /// reader's own tests.
    /// </remarks>
    [Fact]
    public void NoCombinationOfInputsThrows()
    {
        RequireResourceFixture();

        EnglishProvider provider = new();

        long[] sources = [Enums.I18N_SRC_PFW, Enums.I18N_SRC_CUSTOM, long.MinValue, long.MaxValue];
        long[] categories = [0L, 1L, 2L, 3L, 4L, 5L, 6L, 7L, 8L, -1L, long.MaxValue];
        string?[] texts = [null, string.Empty, " ", "关闭", "it's", "x' or '1'='1", "第{}行", "<<ABSENT>>"];

        int handled = 0;

        foreach (long source in sources)
        {
            foreach (long category in categories)
            {
                foreach (string? candidate in texts)
                {
                    string? text = candidate;
                    handled += provider.OnTranslate(source, category, ref text) == 1L ? 1 : 0;
                }
            }
        }

        Assert.True(handled > 0, "At least one row should have translated, or the fixture is not being read.");
    }
}
