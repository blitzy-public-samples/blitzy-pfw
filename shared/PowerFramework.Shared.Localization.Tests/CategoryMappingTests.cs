// ==================================================================================================
//  CategoryMappingTests.cs - THE SIX-ARM CATEGORY SWITCH, ITS TWO DELIBERATE HOLES, AND THE FILTER
//                            THAT RUNS IN FRONT OF IT
//  ------------------------------------------------------------------------------------------------
//  UNITS UNDER TEST  PowerFramework.Shared.Localization.EnglishProvider
//                    PowerFramework.Shared.Localization.TraditionalChineseProvider
//                    - both XPath providers, driven through ONE matrix, because they share the
//                      switch SHAPE and a divergence between them is a copy-paste error rather than
//                      a design difference. Neither of the per-provider suites can catch that: each
//                      one sees only its own provider.
//
//  ORACLES           ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_en.sru      164 lines
//                    ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_cht.sru      68 lines
//                    ws_objects/pfw.ui.controls.ext.pbl.src/ne_cst_i18n.sru         28 lines
//                    ws_objects/pfw.shared.pbl.src/enums.sru                        the I18N block
//                    pfw.i18n.xml                                                   the table
//                    All five are READ ONLY (constraint C-C). Every locator below was verified
//                    against the file on disk, and every expected value in every row was read out
//                    of pfw.i18n.xml rather than derived, remembered or copied from another suite.
//
//  THE ORACLE, TRANSCRIBED RATHER THAN SUMMARISED  [n_cst_i18n_en.sru:L30-L56]
//  ------------------------------------------------------------------------------------------------
//      L30   string sCat,sTo                                  <- sCat starts as the EMPTY string
//      L32   if source <> Enums.I18N_SRC_PFW then return 0     <- THE FILTER, ahead of everything
//      L34   choose case category
//      L35     case Enums.I18N_CAT_WINDOW,Enums.I18N_CAT_DATAWINDOW
//      L36        sCat = "window"                              <- TWO categories, ONE element
//      L37     case Enums.I18N_CAT_RIBBONBAR
//      L38        sCat = "ribbonbar"
//      L39     case Enums.I18N_CAT_SPLITCONTAINER
//      L40        sCat = "splitcontainer"
//      L41     case Enums.I18N_CAT_TABCONTROL
//      L42        sCat = "tabcontrol"
//      L43     case CAT_MSGBOX
//      L44        sCat = "msgbox"
//      L45     case CAT_DWSVC
//      L46        sCat = "dwsvc"
//      L47   end choose            <- NO arm for Enums.I18N_CAT_CUSTOM, and NO 'case else'
//      L49   if sCat <> "" then    <- THE GATE: the whole lookup hangs off a non-empty element name
//      L51      sTo = _doc.Query(Sprintf("string(pfw/{}[@lang='en']/tr[@text='{}']/@to)",sCat,text))...
//      L52      if sTo <> "" then text = sTo : return 1
//      L56   end if
//      L155  return 0
//
//  n_cst_i18n_cht.sru is the SAME code shifted by one line - the filter at :L33, the switch at
//  :L35-L48, the gate at :L50, the query at :L52 with lang='cht' - so one matrix over both providers
//  is a faithful reading of the estate and not a convenience.
//
//  THE THREE FACTS THIS SUITE EXISTS TO PIN
//  ------------------------------------------------------------------------------------------------
//  (1) TWO CATEGORIES SHARE ONE ELEMENT. Enums.I18N_CAT_WINDOW (0) and Enums.I18N_CAT_DATAWINDOW (4)
//      are distinct values that both select 'window' [:L35-L36]. This is legacy DESIGN, not a bug to
//      be split (constraint C-B): pfw.i18n.xml carries no 'datawindow' element at all, so splitting
//      the arm would turn every DataWindow-category lookup into a miss. The data premise is asserted
//      at the foot of this file, because until now it lived only in comments.
//
//  (2) Enums.I18N_CAT_CUSTOM MAPS TO NOTHING, SO IT NEVER TRANSLATES. The switch covers 0, 1, 2, 3,
//      4, 6 and 7 and deliberately NOT 5 [:L34-L47]. With no arm, sCat stays empty, the gate at :L49
//      fails and THE LOOKUP IS SKIPPED ENTIRELY - the table is never consulted, however perfectly
//      the text would have matched. This suite asserts that as INTENDED behaviour. It does not
//      assert that the value "should" fall back to a default element, and it treats the missing arm
//      as the port's fidelity rather than its omission (constraint C-B).
//
//      THE EASY THING TO GET BACKWARDS, stated once so it stays clear: the two categories that DO
//      have arms are DERIVED FROM the one that does not. ne_cst_i18n.sru:L16-L17 declares
//      CAT_MSGBOX = Enums.I18N_CAT_CUSTOM + 1 and CAT_DWSVC = Enums.I18N_CAT_CUSTOM + 2, so 5 is the
//      BOUNDARY above which a consumer defines its own categories - a base for arithmetic, never a
//      category with content of its own. 6 and 7 translate; the 5 they are measured from does not.
//
//  (3) THE SOURCE FILTER RUNS BEFORE THE SWITCH. [:L32] A non-framework source returns 0 without
//      reaching the mapping or the table, so no combination of category and text can translate for
//      it. Proving that needs a row that WOULD have translated - which is why the filter rows below
//      use pairs measured to be hits and then assert the same pair DOES translate under the
//      framework source.
//
//  THE ZERO COLLISION - THE HAZARD THAT SHAPES EVERY ROW IN THIS FILE
//  ------------------------------------------------------------------------------------------------
//  enums.sru's I18N block declares Enums.I18N_SRC_PFW = 0 AND Enums.I18N_CAT_WINDOW = 0. The two
//  arguments of OnTranslate therefore have the SAME neutral value, and a call that passes 0 for both
//  cannot distinguish which argument the implementation read: a provider that swapped them, or read
//  one twice, answers identically. Two rules follow, and they are enforced by an executable
//  assertion in this file rather than left to reviewer diligence:
//
//      * EVERY source-filter row uses a NON-ZERO category.
//      * EVERY category-mapping row spells the source Enums.I18N_SRC_PFW, never a bare literal 0.
//
//  Quirk (1) is what makes the first rule cost nothing. Because Enums.I18N_CAT_DATAWINDOW (4) reaches
//  the SAME 'window' element as Enums.I18N_CAT_WINDOW (0), the window element is reachable through a
//  NON-ZERO category - so every claim about it can be proved twice, once through the ambiguous value
//  and once through an unambiguous one.
//
//  HOW THIS FILE DIVIDES FROM ITS SIBLINGS  (deliberate overlap, stated so it reads as a decision)
//  ------------------------------------------------------------------------------------------------
//  EnglishProviderTests.cs        one provider, and mostly SYNTHETIC tables: it proves which element
//                                 was read by giving every element the same probe key. Its filter
//                                 test pairs a foreign source with Enums.I18N_CAT_WINDOW, so it does
//                                 not close the zero collision above.
//  TraditionalChineseProviderTests.cs  one provider, the cht language token, and its own map rows.
//  THIS FILE                      BOTH providers in ONE matrix against the REAL table; the filter
//                                 rows on non-zero categories with a known-hit control; the zero
//                                 collision asserted executably; arm coverage asserted as a set; and
//                                 the data premise behind both holes.
//  MistranslationParityTests.cs   the two mistranslated VALUES, the collision they cause, and the
//                                 dormant table. NOT restated here. pfw.i18n.xml:L49 is used below as
//                                 a known HIT - deliberately, it is the strongest one available - but
//                                 its corrupted value is never asserted, only named in a comment.
//  the rendered XPath key         NOT asserted here at all. Its textual shape belongs to
//                                 LookupKeyFormatTests.cs and its assembly to I18nResourceReaderTests.cs;
//                                 this file only ever observes what a lookup ANSWERS.
//  I18nResourceReaderTests.cs     the reader's mechanics and the XPath substitution. Not asserted here.
//  I18nResourceFidelityTests.cs   the resource's bytes, encoding and line endings. Not asserted here.
//  SimplifiedChineseProviderTests.cs  the base-locale no-op, which has NO category switch at all and
//                                 is therefore deliberately absent from every matrix below.
//
//  WHY THERE IS NO RECORDING READER DOUBLE, DECIDED RATHER THAN OVERLOOKED
//  ------------------------------------------------------------------------------------------------
//  A reader double that counted lookups would turn "the table was never consulted" from an inference
//  into a direct observation. It is not possible without changing the system under test, and changing
//  the system under test to make a test possible is out of bounds: I18nResourceReader is a SEALED
//  class, both providers depend on that concrete type rather than on an interface, and the library is
//  not this file's to edit. So the strongest AVAILABLE evidence is used instead - a (category, text)
//  pair measured to translate under the framework source, asserted not to translate under a foreign
//  one - which is an observable statement of the same fact and needs no seam at all.
//
//  RULES POSITION
//  ------------------------------------------------------------------------------------------------
//  review_rules returns exactly "No user rules provided.", confirmed for this file. AAP §0.7.3's
//  binding constraints stand in their place, and the three that govern here are cited at every
//  assertion that turns on them: C-B (replicate behaviour, including deliberate holes, and improve
//  nothing), C-C (the legacy tree and pfw.i18n.xml are read-only - this file's only file APIs are
//  File.Exists and XDocument.Load, both of which read), C-H (nullable, warnings-as-errors, and the
//  per-project coverage gate).
//
//  IF AN ASSERTION HERE FAILS, INVESTIGATE THE IMPLEMENTATION - NEVER EDIT THE RESOURCE. Every
//  expected value below is quoted from a read-only file with its line number so a disagreement can be
//  adjudicated against the oracle instead of re-derived.
// ==================================================================================================

// System.IO for the fixture guard's File.Exists, System.Linq for the set-level coverage assertions,
// System.Xml.Linq for the one data-premise assertion. All three ship inside the
// Microsoft.NETCore.App shared framework, so this file adds no package reference - which matters,
// because an XML PACKAGE here would be the first thread of coupling to the deferred Documents
// capability that this phase must not implement even partially (constraint C-D). System.Collections.
// Generic is deliberately absent: nothing here needs it, and an unused directive in a suite this size
// invites the next reader to assume a dependency that does not exist.
using System.IO;
using System.Linq;
using System.Xml.Linq;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.Shared.Localization.Tests;

/// <summary>
/// Category-map, category-hole and source-filter parity tests, run across both XPath providers.
/// </summary>
/// <remarks>
/// Every test in this class drives the REAL <c>pfw.i18n.xml</c>. That is the point: the subject is the
/// mapping from a category value to one element OF THAT TABLE, so a synthetic document would prove
/// only that some lookup happened. The cost is that each test depends on the fixture reaching the test
/// working directory, and the mandatory guard is what stops a dropped delivery from turning the whole
/// class green for the wrong reason.
/// </remarks>
public class CategoryMappingTests
{
    // ==============================================================================================
    //  0. THE ALPHABET, THE LANGUAGES, AND THE ELEMENT NAMES
    // ==============================================================================================

    /// <summary>
    /// The handled answer. The legacy states the alphabet in its own doc block as
    /// <c>返回1代表已处理</c>, "returning 1 means handled" [<c>n_cst_i18n_en.sru</c>:L28].
    /// </summary>
    /// <remarks>
    /// THIS IS NOT THE RETURN-CODE ALGEBRA, and conflating the two would be a category error rather
    /// than a naming quibble: <c>RetCode.PREVENT</c> is also 1 and <c>RetCode.OK</c> is also 0, so
    /// reading a translate answer through <c>RetCode</c> would report a successful translation as a
    /// prevention and a decline as a success. The provider's contract is a two-value handled flag and
    /// nothing more, which is why it is declared locally here.
    /// </remarks>
    private const long Handled = 1L;

    /// <summary>
    /// The not-handled answer, returned by the source filter [:L32], by the unmapped-category gate
    /// [:L49] and by an ordinary lookup miss [:L155] alike.
    /// </summary>
    private const long NotHandled = 0L;

    /// <summary>The English provider's discriminator, matching its <c>lang="en"</c> table half.</summary>
    private const string English = "en";

    /// <summary>
    /// The Traditional Chinese provider's discriminator, matching its <c>lang="cht"</c> table half.
    /// </summary>
    /// <remarks>
    /// Simplified Chinese is deliberately absent from every matrix in this file. Its provider has no
    /// category switch to test - <c>n_cst_i18n_chs.sru</c> is a genuine no-op because Simplified
    /// Chinese is the base locale and the table declares no <c>lang="chs"</c> section - so a row for
    /// it would assert nothing about a category map.
    /// </remarks>
    private const string TraditionalChinese = "cht";

    // The six element names the switch can produce, spelled exactly as the legacy assigns them at
    // :L36, :L38, :L40, :L42, :L44 and :L46. They are DATA, not identifiers - lower case in the
    // resource file - so they are ordinary PascalCase constants carrying a lower-case value, and this
    // file declares no underscored identifier of its own (the .editorconfig CA1707 and IDE1006
    // suppressions are scoped to the files that DECLARE preserved identifiers, and this is not one of
    // them; it only CONSUMES Enums.I18N_* and Categories.CAT_*).
    private const string WindowElement = "window";
    private const string RibbonBarElement = "ribbonbar";
    private const string SplitContainerElement = "splitcontainer";
    private const string TabControlElement = "tabcontrol";
    private const string MessageBoxElement = "msgbox";
    private const string DataWindowServiceElement = "dwsvc";

    /// <summary>
    /// The two element names the resource does NOT contain, which is the data premise behind both
    /// deliberate holes: there is nothing for a <c>datawindow</c> arm or a
    /// <c>Enums.I18N_CAT_CUSTOM</c> arm to read.
    /// </summary>
    private static readonly string[] AbsentElementNames = ["datawindow", "custom"];

    /// <summary>
    /// Every element name the switch can produce, used to assert that the matrix exercises all six
    /// rather than however many a future edit happens to leave behind.
    /// </summary>
    private static readonly string[] MappedElementNames =
    [
        WindowElement,
        RibbonBarElement,
        SplitContainerElement,
        TabControlElement,
        MessageBoxElement,
        DataWindowServiceElement,
    ];

    /// <summary>
    /// Every category value the switch maps, in the legacy's own arm order: window and DataWindow
    /// share the first arm, then ribbon bar, split container, tab control, message box and the
    /// DataWindow service. <c>Enums.I18N_CAT_CUSTOM</c> (5) is absent BY DESIGN [:L34-L47].
    /// </summary>
    private static readonly long[] MappedCategories =
    [
        Enums.I18N_CAT_WINDOW,
        Enums.I18N_CAT_DATAWINDOW,
        Enums.I18N_CAT_RIBBONBAR,
        Enums.I18N_CAT_SPLITCONTAINER,
        Enums.I18N_CAT_TABCONTROL,
        Categories.CAT_MSGBOX,
        Categories.CAT_DWSVC,
    ];

    // ==============================================================================================
    //  1. THE FIXTURE GUARD - MANDATORY, AND THE SHARPEST FAILURE MODE IN THIS FILE
    // ==============================================================================================

    /// <summary>
    /// The message a fixture-guard failure carries, naming the item that delivers the table.
    /// </summary>
    /// <remarks>
    /// It names <c>PowerFramework.Shared.Localization.csproj</c> and not this test project, because
    /// that is where the single <c>Content</c> item lives: the library is the only thing in the tree
    /// that READS the table, so it owns delivering it, and it flows down the project-reference edge
    /// into every consumer's output directory.
    /// </remarks>
    private const string FixtureMissingMessage =
        "pfw.i18n.xml is not in the test working directory. EVERY negative assertion in "
            + "CategoryMappingTests would then pass VACUOUSLY - a decline is exactly what a missing "
            + "table produces - so this guard fails first and loudly instead. Restore the Content item "
            + "in PowerFramework.Shared.Localization.csproj, which is the project that delivers the "
            + "table to every consumer's output directory; it is declared there and deliberately "
            + "nowhere else.";

    /// <summary>
    /// Asserts the real resource is reachable under its bare relative name before a test relies on it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The bare relative name is the contract, not a convenience: the providers reproduce the legacy
    /// <c>_doc.LoadFile("pfw.i18n.xml")</c> [<c>n_cst_i18n_en.sru</c>:L159,
    /// <c>n_cst_i18n_cht.sru</c>:L63], which resolves against the process working directory and probes
    /// nowhere else. The xunit.v3 host runs with the build output directory as its working directory,
    /// so the table has to be sitting there.
    /// </para>
    /// <para>
    /// This guard is why the class can safely assert declines. Roughly half the rows below expect
    /// <see cref="NotHandled"/> and an untouched string, which is ALSO what an absent table produces -
    /// the providers ignore the load result exactly as the legacy does - so without the guard a
    /// dropped delivery would leave those rows green while proving nothing at all.
    /// </para>
    /// </remarks>
    private static void RequireResourceFixture()
    {
        Assert.True(File.Exists(I18nResourceReader.DefaultResourceFileName), FixtureMissingMessage);
    }

    // ==============================================================================================
    //  2. THE PROVIDER FACTORY AND THE TWO ASSERTION HELPERS
    // ==============================================================================================

    /// <summary>
    /// Builds the provider a row names, over the real resource table.
    /// </summary>
    /// <param name="language">
    /// <see cref="English"/> or <see cref="TraditionalChinese"/>.
    /// </param>
    /// <returns>The provider for that language, as the contract both implement.</returns>
    /// <remarks>
    /// A row carries the LANGUAGE TOKEN rather than the provider itself because theory arguments must
    /// serialize, and a provider does not. The token is also what appears in the test-runner output,
    /// which makes a failing row self-identifying. The return type is the interface deliberately: the
    /// matrix asserts the shared contract, so nothing below may reach for a member only one of the two
    /// concrete types has.
    /// </remarks>
    private static II18nProvider CreateProvider(string language)
    {
        return language switch
        {
            English => new EnglishProvider(),
            TraditionalChinese => new TraditionalChineseProvider(),

            // Unreachable from any row in this file. It exists so that a mistyped token in a future
            // row fails as an explicit, named error instead of silently selecting a default provider
            // and reporting a translation mismatch that would send a reader hunting in the wrong file.
            _ => throw new ArgumentOutOfRangeException(
                nameof(language),
                language,
                "Unknown provider discriminator. Use CategoryMappingTests.English or "
                    + "CategoryMappingTests.TraditionalChinese."),
        };
    }

    /// <summary>
    /// Asserts a translate call reported <see cref="Handled"/> and wrote the expected translation.
    /// </summary>
    /// <param name="code">The answer the provider returned.</param>
    /// <param name="expected">The translation the read-only resource records.</param>
    /// <param name="observed">The text after the call.</param>
    /// <param name="because">What the row proves, quoted into any failure message.</param>
    /// <remarks>
    /// Both assertions carry a message, which is why they are written as <c>Assert.True</c>
    /// rather than <c>Assert.Equal</c>: this suite's whole value is that a failure names the legacy
    /// behaviour that broke, and a bare value diff cannot say "the window and DataWindow arms have been
    /// split". Each message embeds both values, so no diagnostic detail is lost. The comparison is
    /// <see cref="StringComparison.Ordinal"/>, matching the XPath predicate the lookup is built on,
    /// which compares exact strings with no culture, casing or normalisation applied.
    /// </remarks>
    private static void AssertHandledWithTranslation(
        long code,
        string expected,
        string? observed,
        string because)
    {
        // The observed text is quoted into BOTH messages, including this one about the answer. The two
        // assertions run in order, so when the answer is wrong the second never executes - and without
        // the text here the failure would report only a number, sending the reader back to re-run the
        // case by hand to find out what the provider actually produced.
        Assert.True(
            code == Handled,
            $"Expected {Handled} (handled) but the provider answered {code}; the text is now "
                + $"'{observed}'. {because}");

        Assert.True(
            string.Equals(expected, observed, StringComparison.Ordinal),
            $"Expected the text to be translated to '{expected}' but observed '{observed}'. {because}");
    }

    /// <summary>
    /// Asserts a translate call declined and left the text ordinally untouched.
    /// </summary>
    /// <param name="code">The answer the provider returned.</param>
    /// <param name="original">The text as it was passed in.</param>
    /// <param name="observed">The text after the call.</param>
    /// <param name="because">What the row proves, quoted into any failure message.</param>
    /// <remarks>
    /// Both halves matter and neither implies the other. A provider could answer 0 having already
    /// overwritten the string, and a provider could leave the string alone while answering 1 - each is
    /// a distinct divergence from an oracle whose only mutation is the single assignment at
    /// <c>n_cst_i18n_en.sru</c>:L53, performed only on a hit.
    /// </remarks>
    private static void AssertDeclinedWithTextUntouched(
        long code,
        string original,
        string? observed,
        string because)
    {
        // The text goes into this message too, and here it is load-bearing rather than merely helpful:
        // when a decline turns into a hit, WHAT the provider substituted is the evidence that names the
        // element it reached. Remove the source filter and this exact assertion reports the message-box
        // translation of 隐藏 appearing where the caller's own string should still be - which is the
        // observable proof that a lookup took effect when none should have run at all.
        Assert.True(
            code == NotHandled,
            $"Expected {NotHandled} (not handled) but the provider answered {code}; the text is now "
                + $"'{observed}'. {because}");

        Assert.True(
            string.Equals(original, observed, StringComparison.Ordinal),
            $"Expected the text to be left ordinally unchanged as '{original}' but observed "
                + $"'{observed}'. {because}");
    }

    // ==============================================================================================
    //  3. THE ROW TABLES
    // ==============================================================================================
    //
    //  READ THIS BEFORE ADDING OR EDITING A ROW - THE ZERO COLLISION
    //  ----------------------------------------------------------------------------------------------
    //  enums.sru's I18N block declares Enums.I18N_SRC_PFW = 0 AND Enums.I18N_CAT_WINDOW = 0. Both
    //  arguments of OnTranslate therefore share one neutral value, so A ROW THAT PASSES 0 FOR BOTH
    //  PROVES NOTHING ABOUT WHICH ARGUMENT THE IMPLEMENTATION READ - a provider that swapped them, or
    //  read one of them twice, answers such a row identically to a correct one. Two rules, and
    //  TheZeroCollisionIsRealAndEveryRowTableRespectsIt below enforces both as assertions so this
    //  warning cannot rot into a comment nobody honours:
    //
    //      RULE 1  Every source-filter row uses a NON-ZERO category.
    //      RULE 2  Every category-mapping row spells the source Enums.I18N_SRC_PFW, never a literal 0.
    //
    //  Rule 1 costs nothing because of quirk (1): Enums.I18N_CAT_DATAWINDOW (4) reaches the SAME
    //  'window' element as Enums.I18N_CAT_WINDOW (0), so the window element is always reachable
    //  through an unambiguous, non-zero category. Where a claim is made about the window element, the
    //  tables below deliberately make it twice - once through 0 and once through 4.
    //
    //  Each row's Locator is the pfw.i18n.xml line the expectation was read from. It is carried as
    //  DATA rather than as a comment so it reaches the failure message, which is what lets a
    //  disagreement be adjudicated against the read-only oracle instead of re-derived from scratch.
    //  ----------------------------------------------------------------------------------------------

    /// <summary>
    /// One verified translation: which provider, which category, which element that category selects,
    /// the key, the translation the resource records for it, and the resource line it came from.
    /// </summary>
    private sealed record CategoryHitRow(
        string Language,
        long Category,
        string ElementName,
        string Key,
        string Expected,
        string Locator);

    /// <summary>
    /// One key that exists under a DIFFERENT element from the category being asked, plus the category
    /// under which it DOES resolve so the row cannot be satisfied by the key simply being absent.
    /// </summary>
    private sealed record CrossArmMissRow(
        string Language,
        long Category,
        string Key,
        long HomeCategory,
        string HomeLocator);

    /// <summary>
    /// One (source, category, key) triple that is a measured HIT under the framework source, used to
    /// prove the source filter runs before the lookup rather than after it.
    /// </summary>
    /// <param name="Language">Which provider the row drives.</param>
    /// <param name="Source">The foreign source value.</param>
    /// <param name="Category">The category, always NON-ZERO - see RULE 1 above.</param>
    /// <param name="Key">A key the resource records under that category's element.</param>
    /// <param name="HomeLocator">The resource line the entry lives on.</param>
    /// <param name="HomeTranslationDiffersFromKey">
    /// Whether the translation the resource records for <paramref name="Key"/> is a DIFFERENT string
    /// from the key itself. It is <see langword="true"/> for every row but one: Traditional Chinese
    /// renders <c>固定</c> as <c>固定</c> [pfw.i18n.xml:L105], an identity translation that is still a
    /// hit. Carrying the fact as data lets the control assert the outcome in BOTH directions - changed
    /// where the table changes it, ordinally identical where the table does not - instead of skipping
    /// the assertion for the awkward row or dropping it for all of them.
    /// </param>
    private sealed record ForeignSourceRow(
        string Language,
        long Source,
        long Category,
        string Key,
        string HomeLocator,
        bool HomeTranslationDiffersFromKey);

    /// <summary>
    /// The verified per-arm translations: all seven mapped category values for BOTH providers, fourteen
    /// rows, every expectation quoted from the read-only table with its line number.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each key was checked to appear under EXACTLY ONE element of the resource, which is what makes a
    /// hit here evidence about the CATEGORY MAP rather than merely evidence that some lookup succeeded:
    /// an implementation reading the wrong element finds nothing and declines.
    /// </para>
    /// <para>
    /// THE ONE IDENTITY ROW. Traditional Chinese translates <c>固定</c> to itself
    /// [pfw.i18n.xml:L105], so for that row alone the text is unchanged on a HIT and only the answer
    /// <see cref="Handled"/> proves the tab-control arm was reached. It is kept rather than dropped
    /// precisely because it is the row a "did the text change?" style of assertion would silently fail
    /// to cover, and it is why the per-arm theory asserts the exact expected value instead.
    /// </para>
    /// </remarks>
    private static readonly CategoryHitRow[] CategoryHits =
    [
        // ---- the shared arm, reached through the ambiguous zero value [:L35-L36] ----
        new(English, Enums.I18N_CAT_WINDOW, WindowElement, "还原", "Restore", "pfw.i18n.xml:L5"),
        new(TraditionalChinese, Enums.I18N_CAT_WINDOW, WindowElement, "还原", "還原", "pfw.i18n.xml:L80"),

        // ---- the same arm, reached through the NON-ZERO twin: quirk (1) made useful [:L35-L36] ----
        new(English, Enums.I18N_CAT_DATAWINDOW, WindowElement, "还原", "Restore", "pfw.i18n.xml:L5"),
        new(
            TraditionalChinese,
            Enums.I18N_CAT_DATAWINDOW,
            WindowElement,
            "还原",
            "還原",
            "pfw.i18n.xml:L80"),

        // ---- ribbon bar [:L37-L38] ----
        new(
            English,
            Enums.I18N_CAT_RIBBONBAR,
            RibbonBarElement,
            "折叠功能区",
            "Collapse",
            "pfw.i18n.xml:L33"),
        new(
            TraditionalChinese,
            Enums.I18N_CAT_RIBBONBAR,
            RibbonBarElement,
            "折叠功能区",
            "折疊功能區",
            "pfw.i18n.xml:L108"),

        // ---- split container [:L39-L40]. The two providers use DIFFERENT keys, and deliberately so:
        //      each was chosen from its own language half of the table rather than assumed symmetric.
        new(
            English,
            Enums.I18N_CAT_SPLITCONTAINER,
            SplitContainerElement,
            "展开左侧面板",
            "Expand the left panel",
            "pfw.i18n.xml:L24"),
        new(
            TraditionalChinese,
            Enums.I18N_CAT_SPLITCONTAINER,
            SplitContainerElement,
            "双击折叠左侧面板",
            "雙擊折疊左側面板",
            "pfw.i18n.xml:L91"),

        // ---- tab control [:L41-L42]. The cht row is the IDENTITY translation described above.
        new(English, Enums.I18N_CAT_TABCONTROL, TabControlElement, "固定", "Dock", "pfw.i18n.xml:L30"),
        new(
            TraditionalChinese,
            Enums.I18N_CAT_TABCONTROL,
            TabControlElement,
            "固定",
            "固定",
            "pfw.i18n.xml:L105"),

        // ---- message box [:L43-L44], the arm keyed on Categories.CAT_MSGBOX (I18N_CAT_CUSTOM + 1) ----
        new(English, Categories.CAT_MSGBOX, MessageBoxElement, "确定", "OK", "pfw.i18n.xml:L41"),
        new(
            TraditionalChinese,
            Categories.CAT_MSGBOX,
            MessageBoxElement,
            "询问",
            "詢問",
            "pfw.i18n.xml:L113"),

        // ---- DataWindow service [:L45-L46], keyed on Categories.CAT_DWSVC (I18N_CAT_CUSTOM + 2).
        //      This is the live one: the DataWindow service layer reaches it from the item-validation
        //      path, so a break here breaks an in-scope code path rather than a hypothetical one.
        new(
            English,
            Categories.CAT_DWSVC,
            DataWindowServiceElement,
            "修改数据被拒绝",
            "Modified data is rejected",
            "pfw.i18n.xml:L53"),
        new(
            TraditionalChinese,
            Categories.CAT_DWSVC,
            DataWindowServiceElement,
            "修改数据被拒绝",
            "修改數據被拒絕",
            "pfw.i18n.xml:L128"),
    ];

    /// <summary>
    /// Keys that exist under one element of the table, asked for under a different category.
    /// </summary>
    /// <remarks>
    /// This is the table that proves the arms are ARMS and not a single search across the whole
    /// document. Every row names the category under which the key DOES resolve, and the theory asserts
    /// that too - so a failure can never be explained away as a key that simply is not in the table.
    /// The window-element rows appear twice, once through <c>Enums.I18N_CAT_WINDOW</c> (0) and once
    /// through <c>Enums.I18N_CAT_DATAWINDOW</c> (4), so the claim survives the zero collision.
    /// </remarks>
    private static readonly CrossArmMissRow[] CrossArmMisses =
    [
        // 确定 lives ONLY in msgbox [en :L41, cht :L116]. Asked for as a window caption it must miss.
        new(English, Enums.I18N_CAT_WINDOW, "确定", Categories.CAT_MSGBOX, "pfw.i18n.xml:L41"),
        new(English, Enums.I18N_CAT_DATAWINDOW, "确定", Categories.CAT_MSGBOX, "pfw.i18n.xml:L41"),
        new(TraditionalChinese, Enums.I18N_CAT_WINDOW, "确定", Categories.CAT_MSGBOX, "pfw.i18n.xml:L116"),
        new(
            TraditionalChinese,
            Enums.I18N_CAT_DATAWINDOW,
            "确定",
            Categories.CAT_MSGBOX,
            "pfw.i18n.xml:L116"),

        // 还原 lives ONLY in window [en :L5, cht :L80]. The home category is the NON-ZERO twin.
        new(English, Categories.CAT_MSGBOX, "还原", Enums.I18N_CAT_DATAWINDOW, "pfw.i18n.xml:L5"),
        new(
            TraditionalChinese,
            Categories.CAT_MSGBOX,
            "还原",
            Enums.I18N_CAT_DATAWINDOW,
            "pfw.i18n.xml:L80"),

        // 固定 lives ONLY in tabcontrol [en :L30, cht :L105]. Asked for as a ribbon-bar caption it misses.
        new(English, Enums.I18N_CAT_RIBBONBAR, "固定", Enums.I18N_CAT_TABCONTROL, "pfw.i18n.xml:L30"),
        new(
            TraditionalChinese,
            Enums.I18N_CAT_RIBBONBAR,
            "固定",
            Enums.I18N_CAT_TABCONTROL,
            "pfw.i18n.xml:L105"),

        // 折叠功能区 lives ONLY in ribbonbar [en :L33, cht :L108]. The reverse of the pair above.
        new(
            English,
            Enums.I18N_CAT_TABCONTROL,
            "折叠功能区",
            Enums.I18N_CAT_RIBBONBAR,
            "pfw.i18n.xml:L33"),
        new(
            TraditionalChinese,
            Enums.I18N_CAT_TABCONTROL,
            "折叠功能区",
            Enums.I18N_CAT_RIBBONBAR,
            "pfw.i18n.xml:L108"),

        // 隐藏 lives ONLY in msgbox [en :L49, cht :L124]. Asked for under the DataWindow-service
        // category it misses - the two neighbouring custom categories are NOT one shared element.
        new(English, Categories.CAT_DWSVC, "隐藏", Categories.CAT_MSGBOX, "pfw.i18n.xml:L49"),
        new(
            TraditionalChinese,
            Categories.CAT_DWSVC,
            "隐藏",
            Categories.CAT_MSGBOX,
            "pfw.i18n.xml:L124"),

        // And a msgbox key asked for under the split container, so the miss is not only ever adjacent.
        new(
            English,
            Enums.I18N_CAT_SPLITCONTAINER,
            "确定",
            Categories.CAT_MSGBOX,
            "pfw.i18n.xml:L41"),
        new(
            TraditionalChinese,
            Enums.I18N_CAT_SPLITCONTAINER,
            "确定",
            Categories.CAT_MSGBOX,
            "pfw.i18n.xml:L116"),
    ];

    /// <summary>
    /// Foreign-source rows, every one of them a measured HIT under <c>Enums.I18N_SRC_PFW</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EVERY CATEGORY HERE IS NON-ZERO, which is RULE 1 of the zero collision: with a non-zero category
    /// and a non-framework source, no pair of these two arguments can be confused for the other, so a
    /// decline can only be the filter at <c>n_cst_i18n_en.sru</c>:L32.
    /// </para>
    /// <para>
    /// The strongest row is the first: <c>Categories.CAT_MSGBOX</c> with <c>隐藏</c>, a confirmed entry
    /// at pfw.i18n.xml:L49. HAD THE FILTER BEEN OMITTED, that exact row would have answered
    /// <see cref="Handled"/> and overwritten the text with <c>Htexte details</c> - the corrupted
    /// mixed-language value that entry carries - which is the observable proof that no lookup took
    /// effect. That was not reasoned about but MEASURED: removing the filter from
    /// <see cref="EnglishProvider"/> makes this very row fail with "the provider answered 1; the text is
    /// now 'Htexte details'".
    /// </para>
    /// <para>
    /// The value is named here as EVIDENCE and is deliberately NOT asserted - a comment quoting the
    /// read-only oracle pins nothing, whereas an assertion would duplicate a parity claim this file does
    /// not own. Pinning it is MistranslationParityTests.cs's job; this file needs only the fact that the
    /// pair translates, which the theory asserts directly. Using a mistranslated entry as a known HIT
    /// and asserting what it says are two different uses of one row, and only the first belongs here.
    /// </para>
    /// </remarks>
    private static readonly ForeignSourceRow[] ForeignSources =
    [
        // The declared foreign source, on the strongest known-hit pair in the table.
        new(English, Enums.I18N_SRC_CUSTOM, Categories.CAT_MSGBOX, "隐藏", "pfw.i18n.xml:L49", true),
        new(
            TraditionalChinese,
            Enums.I18N_SRC_CUSTOM,
            Categories.CAT_MSGBOX,
            "隐藏",
            "pfw.i18n.xml:L124",
            true),

        // An arbitrary positive source the enum does not declare at all, on the live category.
        new(English, 2L, Categories.CAT_DWSVC, "修改数据被拒绝", "pfw.i18n.xml:L53", true),
        new(TraditionalChinese, 2L, Categories.CAT_DWSVC, "修改数据被拒绝", "pfw.i18n.xml:L128", true),

        // A negative source. The legacy compares for inequality, so sign is irrelevant - asserted
        // rather than assumed.
        new(English, -1L, Enums.I18N_CAT_RIBBONBAR, "折叠功能区", "pfw.i18n.xml:L33", true),
        new(TraditionalChinese, -1L, Enums.I18N_CAT_RIBBONBAR, "折叠功能区", "pfw.i18n.xml:L108", true),

        // The extremes, which catch a filter narrowed to a range check instead of an inequality. The
        // cht tab-control row is THE IDENTITY ROW: 固定 translates to 固定 [pfw.i18n.xml:L105], a hit
        // whose result equals its input, which is why the control asserts the outcome in both
        // directions rather than asserting the text changed.
        new(English, long.MaxValue, Enums.I18N_CAT_TABCONTROL, "固定", "pfw.i18n.xml:L30", true),
        new(
            TraditionalChinese,
            long.MaxValue,
            Enums.I18N_CAT_TABCONTROL,
            "固定",
            "pfw.i18n.xml:L105",
            false),
        new(English, long.MinValue, Enums.I18N_CAT_DATAWINDOW, "还原", "pfw.i18n.xml:L5", true),
        new(
            TraditionalChinese,
            long.MinValue,
            Enums.I18N_CAT_DATAWINDOW,
            "还原",
            "pfw.i18n.xml:L80",
            true),

        // And the split container, so every non-zero mapped arm is represented across this table.
        new(
            English,
            Enums.I18N_SRC_CUSTOM,
            Enums.I18N_CAT_SPLITCONTAINER,
            "展开左侧面板",
            "pfw.i18n.xml:L24",
            true),
        new(
            TraditionalChinese,
            Enums.I18N_SRC_CUSTOM,
            Enums.I18N_CAT_SPLITCONTAINER,
            "双击折叠左侧面板",
            "pfw.i18n.xml:L91",
            true),
    ];

    // ==============================================================================================
    //  4. THE MEMBER DATA PROVIDERS
    // ==============================================================================================
    //  Each one projects a strongly typed row table above into the loosely typed shape a theory
    //  consumes. The tables stay the single source of truth, so the meta-assertions in section 6 can
    //  iterate the very same rows the theories run - a coverage claim about a copy of the data would be
    //  worth nothing.
    // ----------------------------------------------------------------------------------------------

    /// <summary>Projects <see cref="CategoryHits"/> for the per-arm hit theory.</summary>
    /// <returns>One row per (provider, category): language, category, key, expected, locator.</returns>
    public static TheoryData<string, long, string, string, string> MappedCategoryHitRows()
    {
        TheoryData<string, long, string, string, string> rows = [];

        foreach (CategoryHitRow row in CategoryHits)
        {
            rows.Add(row.Language, row.Category, row.Key, row.Expected, row.Locator);
        }

        return rows;
    }

    /// <summary>Projects <see cref="CrossArmMisses"/> for the cross-arm negative theory.</summary>
    /// <returns>One row per miss: language, category asked, key, home category, home locator.</returns>
    public static TheoryData<string, long, string, long, string> CrossArmMissRows()
    {
        TheoryData<string, long, string, long, string> rows = [];

        foreach (CrossArmMissRow row in CrossArmMisses)
        {
            rows.Add(row.Language, row.Category, row.Key, row.HomeCategory, row.HomeLocator);
        }

        return rows;
    }

    /// <summary>Projects <see cref="ForeignSources"/> for the source-filter theory.</summary>
    /// <returns>
    /// One row per foreign source: language, source, category, key, home locator, and whether the
    /// recorded translation differs from the key.
    /// </returns>
    public static TheoryData<string, long, long, string, string, bool> ForeignSourceHitRows()
    {
        TheoryData<string, long, long, string, string, bool> rows = [];

        foreach (ForeignSourceRow row in ForeignSources)
        {
            rows.Add(
                row.Language,
                row.Source,
                row.Category,
                row.Key,
                row.HomeLocator,
                row.HomeTranslationDiffersFromKey);
        }

        return rows;
    }

    /// <summary>
    /// The rows for the <c>Enums.I18N_CAT_CUSTOM</c> theory: a provider, a key that REALLY EXISTS in
    /// the table, and the NON-ZERO category under which it does resolve.
    /// </summary>
    /// <returns>Language, home category, key, home locator.</returns>
    /// <remarks>
    /// <para>
    /// The keys are real entries on purpose. Asking for a key that is absent would prove nothing about
    /// the missing arm - it would miss under every category - so each row here is a pair the provider
    /// is KNOWN to translate, and the theory asserts that too. The decline is then attributable to the
    /// category value and to nothing else.
    /// </para>
    /// <para>
    /// Every home category is non-zero, so the control call inside the theory never passes 0 for both
    /// arguments. <c>Enums.I18N_CAT_DATAWINDOW</c> (4) stands in for the window element wherever a
    /// window caption is used, which is quirk (1) paying for itself again.
    /// </para>
    /// </remarks>
    public static TheoryData<string, long, string, string> CustomCategoryRows() =>
        new()
        {
            { English, Categories.CAT_MSGBOX, "确定", "pfw.i18n.xml:L41" },
            { English, Categories.CAT_MSGBOX, "隐藏", "pfw.i18n.xml:L49" },
            { English, Enums.I18N_CAT_DATAWINDOW, "还原", "pfw.i18n.xml:L5" },
            { English, Categories.CAT_DWSVC, "修改数据被拒绝", "pfw.i18n.xml:L53" },
            { TraditionalChinese, Categories.CAT_MSGBOX, "确定", "pfw.i18n.xml:L116" },
            { TraditionalChinese, Categories.CAT_MSGBOX, "隐藏", "pfw.i18n.xml:L124" },
            { TraditionalChinese, Enums.I18N_CAT_DATAWINDOW, "还原", "pfw.i18n.xml:L80" },
            { TraditionalChinese, Categories.CAT_DWSVC, "修改数据被拒绝", "pfw.i18n.xml:L128" },
        };

    /// <summary>
    /// Category values from outside the whole known set - neither mapped nor even declared - for the
    /// theory that pins the absence of a <c>case else</c>.
    /// </summary>
    /// <returns>Language and an unmapped category value.</returns>
    /// <remarks>
    /// <c>Enums.I18N_CAT_CUSTOM</c> (5) is deliberately NOT in this table. It is a declared contract
    /// value with a hole of its own and it gets its own named theory, so that a failure reads as "the
    /// custom category started translating" rather than as one anonymous row among six. The values here
    /// are the ones no reading of the legacy could account for: just past the last mapped value, well
    /// past it, negative, and both extremes of the type.
    /// </remarks>
    public static TheoryData<string, long> UnmappedCategoryRows() =>
        new()
        {
            { English, 8L },
            { English, 9L },
            { English, 100L },
            { English, -1L },
            { English, long.MinValue },
            { English, long.MaxValue },
            { TraditionalChinese, 8L },
            { TraditionalChinese, 9L },
            { TraditionalChinese, 100L },
            { TraditionalChinese, -1L },
            { TraditionalChinese, long.MinValue },
            { TraditionalChinese, long.MaxValue },
        };

    /// <summary>
    /// The shared-element rows: one key that exists in the <c>window</c> element, per provider.
    /// </summary>
    /// <returns>Language, key, the translation the resource records, and its locator.</returns>
    public static TheoryData<string, string, string, string> SharedWindowElementRows() =>
        new()
        {
            { English, "还原", "Restore", "pfw.i18n.xml:L5" },
            { TraditionalChinese, "还原", "還原", "pfw.i18n.xml:L80" },
        };

    // ==============================================================================================
    //  5. THE SWITCH
    // ==============================================================================================

    /// <summary>
    /// FACT (1) - the window and DataWindow categories are two distinct values that reach ONE shared
    /// element, so the same key translates identically under both. [<c>n_cst_i18n_en.sru</c>:L35-L36,
    /// <c>n_cst_i18n_cht.sru</c>:L36-L37]
    /// </summary>
    /// <param name="language">The provider under test.</param>
    /// <param name="key">A key the <c>window</c> element records.</param>
    /// <param name="expected">The translation the read-only resource records for it.</param>
    /// <param name="locator">Where that expectation was read from.</param>
    /// <remarks>
    /// <para>
    /// THIS IS LEGACY DESIGN AND NOT A BUG TO BE SPLIT (constraint C-B). The single arm at :L35 lists
    /// both category values, and the reason is in the data: <c>pfw.i18n.xml</c> contains no
    /// <c>datawindow</c> element at all, a premise asserted directly by
    /// <see cref="TheResourceHasNoElementForEitherDeliberateHole"/>. Giving the two categories separate
    /// elements would turn every DataWindow-category lookup into a miss, so the shared arm is
    /// load-bearing rather than cosmetic.
    /// </para>
    /// <para>
    /// Four things are asserted, and each rules out a different way of passing by accident: the two
    /// category VALUES differ, so the claim is not vacuous; both calls report
    /// <see cref="Handled"/> with the expected translation, so the element really was read; the two
    /// answers are equal to EACH OTHER, so the shared element cannot be simulated by a coincidence of
    /// table contents; and the result differs from the input, so "unchanged" can never be mistaken for
    /// a translation. That last one is why <c>还原</c> is the key rather than a Traditional Chinese
    /// identity entry - it changes under both providers.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(SharedWindowElementRows))]
    public void TheWindowAndDataWindowCategoriesReachOneSharedElement(
        string language,
        string key,
        string expected,
        string locator)
    {
        RequireResourceFixture();

        // Captured into locals first. Comparing the two constants directly would be folded by the
        // compiler and would read as a tautology; through locals this is a genuine assertion that the
        // two published contract values have not converged.
        long windowCategory = Enums.I18N_CAT_WINDOW;
        long dataWindowCategory = Enums.I18N_CAT_DATAWINDOW;

        Assert.NotEqual(windowCategory, dataWindowCategory);

        II18nProvider provider = CreateProvider(language);

        string? viaWindow = key;
        long viaWindowCode = provider.OnTranslate(Enums.I18N_SRC_PFW, windowCategory, ref viaWindow);

        string? viaDataWindow = key;
        long viaDataWindowCode = provider.OnTranslate(
            Enums.I18N_SRC_PFW,
            dataWindowCategory,
            ref viaDataWindow);

        string because =
            $"{language}: Enums.I18N_CAT_WINDOW ({windowCategory}) and Enums.I18N_CAT_DATAWINDOW "
                + $"({dataWindowCategory}) share ONE arm at n_cst_i18n_en.sru:L35-L36, so '{key}' must "
                + $"translate identically under both [{locator}]. Splitting them - or inventing a "
                + "'datawindow' element the read-only resource does not have - is the break this row "
                + "catches.";

        AssertHandledWithTranslation(viaWindowCode, expected, viaWindow, because);
        AssertHandledWithTranslation(viaDataWindowCode, expected, viaDataWindow, because);

        // The two categories answer as ONE element, not merely as two lookups that happen to agree.
        Assert.Equal(viaWindowCode, viaDataWindowCode);
        Assert.Equal(viaWindow, viaDataWindow);

        // And the answer is a translation rather than the input handed back, so no row above can be
        // satisfied by a provider that reports Handled while leaving the text alone.
        Assert.NotEqual(key, viaDataWindow);
    }

    /// <summary>
    /// Every mapped category reaches its OWN element, for BOTH providers: fourteen verified
    /// translations read out of the read-only table. [:L35-L46, :L51-L54]
    /// </summary>
    /// <param name="language">The provider under test.</param>
    /// <param name="category">The category value.</param>
    /// <param name="key">A key that appears under EXACTLY ONE element of the table.</param>
    /// <param name="expected">The translation the resource records for it.</param>
    /// <param name="locator">Where that expectation was read from.</param>
    /// <remarks>
    /// <para>
    /// Driven against the REAL resource, and each key was checked to be unique to one element, so a hit
    /// identifies WHICH element was read: an implementation that mapped a category to the wrong element
    /// finds nothing there and declines. Both providers run the same matrix because the switch is the
    /// same code in two files [<c>n_cst_i18n_en.sru</c>:L34-L47, <c>n_cst_i18n_cht.sru</c>:L35-L48], so
    /// an en/cht divergence is a copy-paste error - and neither per-provider suite can see it.
    /// </para>
    /// <para>
    /// The exact expected VALUE is asserted rather than "the text changed", because one row's
    /// translation is its own input: Traditional Chinese renders <c>固定</c> as <c>固定</c>
    /// [pfw.i18n.xml:L105]. A changed-text assertion would fail on a correct implementation there,
    /// whereas the answer <see cref="Handled"/> plus an exact value is right for every row.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(MappedCategoryHitRows))]
    public void EveryMappedCategoryTranslatesFromItsOwnElement(
        string language,
        long category,
        string key,
        string expected,
        string locator)
    {
        RequireResourceFixture();

        II18nProvider provider = CreateProvider(language);
        string? text = key;

        // The source is spelled Enums.I18N_SRC_PFW and never a literal 0 - RULE 2 of the zero
        // collision, so the row states which argument it means even though the value is also 0.
        long code = provider.OnTranslate(Enums.I18N_SRC_PFW, category, ref text);

        AssertHandledWithTranslation(
            code,
            expected,
            text,
            $"{language}: category {category} must reach its own element and translate '{key}' to the "
                + $"value the read-only resource records at {locator}. A miss here means the arm was "
                + "removed, renamed, or pointed at a different element.");
    }

    /// <summary>
    /// A key that exists under a DIFFERENT element is a miss, which is what proves the arms are arms
    /// rather than one search across the whole document. [:L49-L51]
    /// </summary>
    /// <param name="language">The provider under test.</param>
    /// <param name="category">The category asked for.</param>
    /// <param name="key">A key that lives under a different element.</param>
    /// <param name="homeCategory">The category under which the key DOES resolve.</param>
    /// <param name="homeLocator">Where the key's own entry lives.</param>
    /// <remarks>
    /// <para>
    /// The second half of each row is what makes the first half mean anything: the same key is asked
    /// for again under its home category and must be handled there. Without that control, a decline
    /// would be indistinguishable from a key that is simply not in the table - and a decline is also
    /// what a missing resource file produces, which the fixture guard separately rules out.
    /// </para>
    /// <para>
    /// The break this catches is an implementation that searches every element regardless of category -
    /// including the XPath union that <see cref="EnglishProvider"/>'s own header records as a live
    /// injection outcome. Under such an implementation each first half here would return a translation
    /// for a category that does not name it.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(CrossArmMissRows))]
    public void AKeyBelongingToAnotherElementIsAMissUnderThisCategory(
        string language,
        long category,
        string key,
        long homeCategory,
        string homeLocator)
    {
        RequireResourceFixture();

        II18nProvider provider = CreateProvider(language);

        string? asked = key;
        long askedCode = provider.OnTranslate(Enums.I18N_SRC_PFW, category, ref asked);

        AssertDeclinedWithTextUntouched(
            askedCode,
            key,
            asked,
            $"{language}: '{key}' is recorded at {homeLocator}, under the element category "
                + $"{homeCategory} selects - NOT the one category {category} selects. A hit here means "
                + "the category arms have been collapsed into a search across every element.");

        // The control: the very same key, asked for under its own category, IS handled. Only the answer
        // is asserted and not the value - some entries translate to themselves, and pinning a value
        // here would duplicate the per-arm matrix for no extra information.
        string? atHome = key;
        long homeCode = provider.OnTranslate(Enums.I18N_SRC_PFW, homeCategory, ref atHome);

        Assert.True(
            homeCode == Handled,
            $"{language}: control failed. '{key}' must translate under category {homeCategory} "
                + $"[{homeLocator}], otherwise the decline above says nothing about the category map - "
                + "it would only say the key is absent from the table.");
    }

    /// <summary>
    /// FACT (2) - <c>Enums.I18N_CAT_CUSTOM</c> maps to NOTHING, so it never translates, not even text
    /// the table really contains. [:L34-L47, :L49]
    /// </summary>
    /// <param name="language">The provider under test.</param>
    /// <param name="homeCategory">The non-zero category under which the key DOES resolve.</param>
    /// <param name="key">A key that really exists in the read-only table.</param>
    /// <param name="homeLocator">Where that entry lives.</param>
    /// <remarks>
    /// <para>
    /// THIS IS INTENDED BEHAVIOUR, ASSERTED AS SUCH (constraint C-B). The <c>choose case</c> at
    /// :L34-L47 covers 0, 1, 2, 3, 4, 6 and 7 and deliberately omits 5. With no arm, the legacy's
    /// <c>sCat</c> keeps the empty string it was declared with at :L30, the gate at :L49 fails, and THE
    /// TABLE IS NEVER CONSULTED. Nothing here asserts that the value "should" fall back to a default
    /// element, and the missing arm is not treated as an omission in the port.
    /// </para>
    /// <para>
    /// THE THING THAT IS EASY TO GET BACKWARDS. The two categories that DO have arms are derived FROM
    /// this one: <c>ne_cst_i18n.sru</c>:L16-L17 declares <c>CAT_MSGBOX = Enums.I18N_CAT_CUSTOM + 1</c>
    /// and <c>CAT_DWSVC = Enums.I18N_CAT_CUSTOM + 2</c>. So 5 is the BOUNDARY a consumer counts its own
    /// categories from - a base for arithmetic, never a category with content - and 6 and 7, measured
    /// from it, are the ones the switch answers. Every row below asserts the boundary declines and the
    /// derived category translates, in that order, on the same key.
    /// </para>
    /// <para>
    /// The keys really exist in the table, which is the whole point: an absent key would decline under
    /// every category and prove nothing. Because the decline and the control differ ONLY in the
    /// category argument, the decline is attributable to that argument alone.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(CustomCategoryRows))]
    public void TheCustomCategoryBoundaryNeverTranslatesEvenTextTheTableContains(
        string language,
        long homeCategory,
        string key,
        string homeLocator)
    {
        RequireResourceFixture();

        II18nProvider provider = CreateProvider(language);

        string? viaCustom = key;
        long customCode = provider.OnTranslate(Enums.I18N_SRC_PFW, Enums.I18N_CAT_CUSTOM, ref viaCustom);

        AssertDeclinedWithTextUntouched(
            customCode,
            key,
            viaCustom,
            $"{language}: Enums.I18N_CAT_CUSTOM ({Enums.I18N_CAT_CUSTOM}) has NO arm in the switch at "
                + $"n_cst_i18n_en.sru:L34-L47, so it can never translate - not even '{key}', which the "
                + $"read-only resource really does record at {homeLocator}. A hit here means an arm or "
                + "a 'case else' was added for it, which would be a silent behavioural improvement.");

        // The control, and the distinction the doc block above warns about: the DERIVED category
        // translates the same key that the BOUNDARY it is derived from declines.
        string? viaDerived = key;
        long derivedCode = provider.OnTranslate(Enums.I18N_SRC_PFW, homeCategory, ref viaDerived);

        Assert.True(
            derivedCode == Handled,
            $"{language}: control failed. '{key}' must translate under category {homeCategory} "
                + $"[{homeLocator}]. Without that, the decline above would only say the key is absent, "
                + "not that the custom-category boundary is unmapped.");

        Assert.NotEqual(key, viaDerived);
    }

    /// <summary>
    /// Any category the legacy does not list behaves exactly as <c>Enums.I18N_CAT_CUSTOM</c> does,
    /// because there is no <c>case else</c> to catch it. [:L47, :L49]
    /// </summary>
    /// <param name="language">The provider under test.</param>
    /// <param name="category">A category value from outside the whole known set.</param>
    /// <remarks>
    /// <para>
    /// The <c>choose case</c> ends at :L47 with no default arm, so an unrecognised value simply leaves
    /// the element name empty and the gate at :L49 skips the lookup. The break this row catches is an
    /// implementation that adds a default element "for robustness": it would begin translating text
    /// under category values the legacy answers with a flat decline.
    /// </para>
    /// <para>
    /// One fixed key is used for every row - <c>确定</c>, a real message-box entry - and the control
    /// asserts it still translates under <see cref="Categories.CAT_MSGBOX"/>. Holding the key constant
    /// while varying only the category is what makes the category the subject of the theory. The
    /// control's category is non-zero, so no call in this test passes 0 for both arguments.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(UnmappedCategoryRows))]
    public void AnArbitraryUnmappedCategoryDeclinesBecauseThereIsNoDefaultArm(
        string language,
        long category)
    {
        RequireResourceFixture();

        // A real entry: en pfw.i18n.xml:L41 (确定 -> OK), cht :L116 (确定 -> 確定). Its home category is
        // Categories.CAT_MSGBOX, which is 6 and therefore never confusable with a source value of 0.
        const string Key = "确定";

        II18nProvider provider = CreateProvider(language);

        string? text = Key;
        long code = provider.OnTranslate(Enums.I18N_SRC_PFW, category, ref text);

        AssertDeclinedWithTextUntouched(
            code,
            Key,
            text,
            $"{language}: category {category} appears in no arm of the switch at "
                + "n_cst_i18n_en.sru:L34-L47, and there is no 'case else' at :L47, so the element name "
                + "stays empty and the gate at :L49 skips the lookup entirely. A hit here means a "
                + "default element was invented.");

        string? atHome = Key;
        long homeCode = provider.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_MSGBOX, ref atHome);

        Assert.True(
            homeCode == Handled,
            $"{language}: control failed. '{Key}' must still translate under Categories.CAT_MSGBOX "
                + $"({Categories.CAT_MSGBOX}), otherwise the decline above is about the key rather than "
                + "about the category.");
    }

    // ==============================================================================================
    //  6. THE FILTER THAT RUNS IN FRONT OF THE SWITCH
    // ==============================================================================================

    /// <summary>
    /// FACT (3) - the source filter runs BEFORE the category switch, so a foreign source cannot reach a
    /// lookup even with a category and a key that are a known hit.
    /// [<c>n_cst_i18n_en.sru</c>:L32, <c>n_cst_i18n_cht.sru</c>:L33]
    /// </summary>
    /// <param name="language">The provider under test.</param>
    /// <param name="source">A source value other than <c>Enums.I18N_SRC_PFW</c>.</param>
    /// <param name="category">A NON-ZERO mapped category - RULE 1 of the zero collision.</param>
    /// <param name="key">A key the resource records under that category's element.</param>
    /// <param name="homeLocator">Where that entry lives.</param>
    /// <param name="homeTranslationDiffersFromKey">
    /// Whether the recorded translation is a different string from the key. False for exactly one row -
    /// the Traditional Chinese identity translation of <c>固定</c> [pfw.i18n.xml:L105].
    /// </param>
    /// <remarks>
    /// <para>
    /// ORDER IS THE WHOLE CLAIM. The legacy filter is the first executable statement of the event,
    /// ahead of the switch at :L34 and the gate at :L49, so it returns before any element name is
    /// chosen and before the table is touched. A filter moved AFTER the lookup would still decline -
    /// which is exactly why a row using a key that does not exist proves nothing. Every row here is a
    /// MEASURED HIT, and the second half of the test asserts that: the identical (category, key) pair
    /// is asked again with <c>Enums.I18N_SRC_PFW</c> and must be handled.
    /// </para>
    /// <para>
    /// THE OBSERVABLE PROOF THAT NO LOOKUP TOOK EFFECT. Take the strongest row: English,
    /// <see cref="Categories.CAT_MSGBOX"/>, <c>隐藏</c> - a confirmed entry at pfw.i18n.xml:L49. Had the
    /// filter been omitted, that exact row would have answered <see cref="Handled"/> and overwritten
    /// the text with the mistranslated value that entry carries. It answers <see cref="NotHandled"/>
    /// with the text ordinally untouched instead, and the control proves the pair was reachable. The
    /// mistranslated value itself is deliberately NOT asserted here - MistranslationParityTests.cs owns
    /// it - and using the entry as a known HIT is a different use of the same row.
    /// </para>
    /// <para>
    /// EVERY CATEGORY IN THIS TABLE IS NON-ZERO. Because <c>Enums.I18N_SRC_PFW</c> and
    /// <c>Enums.I18N_CAT_WINDOW</c> are both 0, a filter row on the window category could be satisfied
    /// by an implementation that read the wrong argument. Where a window-element row is wanted,
    /// <c>Enums.I18N_CAT_DATAWINDOW</c> (4) is used instead - the same element through an unambiguous
    /// value. <see cref="TheZeroCollisionIsRealAndEveryRowTableRespectsIt"/> enforces that mechanically.
    /// </para>
    /// <para>
    /// No recording double is used, and that is a decision rather than an omission:
    /// <see cref="I18nResourceReader"/> is sealed and both providers depend on the concrete type, so a
    /// counting seam cannot be introduced without editing the library - which a test may not do. The
    /// known-hit-plus-control pair is the observable form of the same statement.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ForeignSourceHitRows))]
    public void AForeignSourceDeclinesBeforeTheSwitchIsEvenReached(
        string language,
        long source,
        long category,
        string key,
        string homeLocator,
        bool homeTranslationDiffersFromKey)
    {
        RequireResourceFixture();

        II18nProvider provider = CreateProvider(language);

        string? viaForeignSource = key;
        long foreignCode = provider.OnTranslate(source, category, ref viaForeignSource);

        AssertDeclinedWithTextUntouched(
            foreignCode,
            key,
            viaForeignSource,
            $"{language}: source {source} is not Enums.I18N_SRC_PFW ({Enums.I18N_SRC_PFW}), so the "
                + $"filter at n_cst_i18n_en.sru:L32 must return before the switch - even though "
                + $"category {category} and '{key}' ARE a hit [{homeLocator}]. A translation here means "
                + "the filter was removed, weakened, or moved after the lookup.");

        // The control that gives the decline its meaning: the SAME category and the SAME key, with only
        // the source changed, must translate.
        string? viaFrameworkSource = key;
        long frameworkCode = provider.OnTranslate(
            Enums.I18N_SRC_PFW,
            category,
            ref viaFrameworkSource);

        Assert.True(
            frameworkCode == Handled,
            $"{language}: control failed. Category {category} with '{key}' must translate under "
                + $"Enums.I18N_SRC_PFW [{homeLocator}]. If it does not, the row above is an ordinary "
                + "miss and says nothing about the source filter at all.");

        // ...and the control wrote exactly what the table records, asserted in BOTH directions rather
        // than as "the text changed". Every row but one changes the text, so a one-directional
        // assertion would fail on a CORRECT implementation for the Traditional Chinese identity
        // translation of 固定 [pfw.i18n.xml:L105] - a hit whose result equals its input. Carrying the
        // fact as a row field turns that awkward case into an extra assertion instead of an exception:
        // where the table changes the text it must change, and where it does not it must NOT.
        bool controlChangedTheText = !string.Equals(key, viaFrameworkSource, StringComparison.Ordinal);

        Assert.True(
            controlChangedTheText == homeTranslationDiffersFromKey,
            homeTranslationDiffersFromKey
                ? $"{language}: control failed. The entry at {homeLocator} records a translation that "
                    + $"DIFFERS from '{key}', so the control call had to change the text; it produced "
                    + $"'{viaFrameworkSource}'."
                : $"{language}: control failed. The entry at {homeLocator} records '{key}' as its own "
                    + $"translation - an identity entry - so the control call had to leave the text "
                    + $"ordinally identical; it produced '{viaFrameworkSource}'.");
    }

    /// <summary>
    /// A foreign source declines with a null text too, so the filter runs before anything reads the
    /// text - the third argument is never even dereferenced. [:L32]
    /// </summary>
    /// <param name="language">The provider under test.</param>
    /// <remarks>
    /// The category is <see cref="Categories.CAT_DWSVC"/> (7) rather than the window category, so this
    /// row also respects RULE 1 of the zero collision. It is the one filter assertion that cannot use a
    /// known hit - there is no entry whose key is null - so it stands on the absence of a throw and the
    /// preserved null instead, which is the contract the provider's own documentation states.
    /// </remarks>
    [Theory]
    [InlineData(English)]
    [InlineData(TraditionalChinese)]
    public void AForeignSourceDeclinesWithANullTextAndLeavesItNull(string language)
    {
        RequireResourceFixture();

        II18nProvider provider = CreateProvider(language);
        string? text = null;

        long code = provider.OnTranslate(
            Enums.I18N_SRC_CUSTOM,
            Categories.CAT_DWSVC,
            ref text);

        Assert.Equal(NotHandled, code);
        Assert.Null(text);
    }

    // ==============================================================================================
    //  7. THE META-ASSERTIONS - THE ROW TABLES AND THE RESOURCE, ASSERTED ABOUT THEMSELVES
    // ==============================================================================================

    /// <summary>
    /// The zero collision is REAL, and every row table above respects the two rules it forces.
    /// [<c>enums.sru</c>, the I18N block: <c>I18N_SRC_PFW = 0</c> and <c>I18N_CAT_WINDOW = 0</c>]
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the warning at the head of section 3 made executable. A comment asking future rows to
    /// avoid a hazard decays; an assertion does not. Three things are pinned:
    /// </para>
    /// <para>
    /// (a) THE COLLISION EXISTS. <c>Enums.I18N_SRC_PFW</c> and <c>Enums.I18N_CAT_WINDOW</c> are the
    /// same value, so the two arguments of <c>OnTranslate</c> cannot be told apart by a call that
    /// passes 0 for both. If a future edit ever separates them, this assertion fails and the rules
    /// below can be relaxed deliberately rather than drifted into.
    /// </para>
    /// <para>
    /// (b) RULE 1 - every foreign-source row uses a non-zero category, and a source that really is not
    /// the framework source.
    /// </para>
    /// <para>
    /// (c) THE WINDOW ELEMENT IS ALSO PROVED WITHOUT THE AMBIGUOUS VALUE. Quirk (1) makes
    /// <c>Enums.I18N_CAT_DATAWINDOW</c> (4) a non-zero route to the same element, and the hit table
    /// carries such a row FOR BOTH PROVIDERS - so every claim about that element survives the collision.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheZeroCollisionIsRealAndEveryRowTableRespectsIt()
    {
        // (a) Through locals, so this is a runtime comparison of two published values rather than a
        // constant the compiler folds away.
        long frameworkSource = Enums.I18N_SRC_PFW;
        long windowCategory = Enums.I18N_CAT_WINDOW;

        Assert.Equal(frameworkSource, windowCategory);

        // (b) RULE 1, over the actual rows the filter theory runs.
        Assert.NotEmpty(ForeignSources);

        foreach (ForeignSourceRow row in ForeignSources)
        {
            Assert.True(
                row.Category != windowCategory,
                $"Foreign-source row ({row.Language}, source {row.Source}, category {row.Category}, "
                    + $"'{row.Key}') uses the category value that COLLIDES with Enums.I18N_SRC_PFW. "
                    + "Such a row cannot show which argument the implementation read. Use a non-zero "
                    + "category - Enums.I18N_CAT_DATAWINDOW (4) reaches the same 'window' element.");

            Assert.True(
                row.Source != frameworkSource,
                $"Foreign-source row ({row.Language}, category {row.Category}, '{row.Key}') passes the "
                    + "framework source, so it is not a foreign-source row at all.");
        }

        // (c) The non-ambiguous route to the shared window element, present for both providers.
        foreach (string language in new[] { English, TraditionalChinese })
        {
            Assert.Contains(
                CategoryHits,
                row => string.Equals(row.Language, language, StringComparison.Ordinal)
                    && row.Category == Enums.I18N_CAT_DATAWINDOW
                    && string.Equals(row.ElementName, WindowElement, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// The hit matrix covers EVERY mapped category and EVERY element the switch can produce, for BOTH
    /// providers - so a future edit cannot quietly shrink the matrix while leaving it green.
    /// [:L34-L47]
    /// </summary>
    /// <remarks>
    /// Coverage of a switch is not something a per-row assertion can state. This one reads the same
    /// table the theory runs, so the claim is about the tests that actually execute: seven mapped
    /// category values per provider, fourteen rows in total, and all six element names exercised.
    /// <c>Enums.I18N_CAT_CUSTOM</c> must NOT appear, which is the other half of the statement - it has
    /// no element to reach, and a row claiming one would contradict the hole this suite pins.
    /// </remarks>
    [Fact]
    public void TheHitMatrixCoversEveryMappedCategoryAndEveryElementForBothProviders()
    {
        foreach (string language in new[] { English, TraditionalChinese })
        {
            long[] covered = CategoryHits
                .Where(row => string.Equals(row.Language, language, StringComparison.Ordinal))
                .Select(row => row.Category)
                .Distinct()
                .Order()
                .ToArray();

            Assert.Equal(MappedCategories.Order().ToArray(), covered);

            Assert.DoesNotContain(Enums.I18N_CAT_CUSTOM, covered);
        }

        string[] elementsCovered = CategoryHits
            .Select(row => row.ElementName)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(MappedElementNames.Order(StringComparer.Ordinal).ToArray(), elementsCovered);
    }

    /// <summary>
    /// THE DATA PREMISE BEHIND THE HIT MATRIX: every key it uses appears under EXACTLY ONE element of
    /// its own language half, and that element is the one the row claims.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the assumption the whole matrix rests on, and it was previously only verified by hand
    /// while the rows were being chosen. <see cref="EveryMappedCategoryTranslatesFromItsOwnElement"/>
    /// treats a hit as evidence about WHICH element the switch selected - but that inference is only
    /// sound while each key is unique to one element. Were a key to appear under two, the row would keep
    /// passing with an arm pointed at either of them, and the matrix would quietly stop testing the
    /// mapping at all.
    /// </para>
    /// <para>
    /// Both halves of the claim are asserted: the count of elements carrying the key is exactly one, and
    /// the name of that element is what the row declares. The second half also cross-checks the row
    /// table itself against the resource, so a locator or an element name mistyped into a row is caught
    /// here rather than surviving as plausible-looking documentation.
    /// </para>
    /// <para>
    /// Scoped to one language half at a time, because the two halves DO share keys by design - the
    /// source text is the same Simplified Chinese in both, only the translation differs. A
    /// document-wide uniqueness claim would therefore be false, and asserting it would be asserting the
    /// wrong thing about a read-only file. Read-only throughout: <c>XDocument.Load</c> and nothing else
    /// (constraint C-C).
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryMatrixKeyIsUniqueToTheElementItsRowNames()
    {
        RequireResourceFixture();

        XElement? root = XDocument.Load(I18nResourceReader.DefaultResourceFileName).Root;

        Assert.NotNull(root);

        foreach (CategoryHitRow row in CategoryHits)
        {
            string[] carriers = root!
                .Elements()
                .Where(element => string.Equals(
                    (string?)element.Attribute("lang"),
                    row.Language,
                    StringComparison.Ordinal))
                .Where(element => element
                    .Elements("tr")
                    .Any(entry => string.Equals(
                        (string?)entry.Attribute("text"),
                        row.Key,
                        StringComparison.Ordinal)))
                .Select(element => element.Name.LocalName)
                .ToArray();

            Assert.True(
                carriers.Length == 1,
                $"'{row.Key}' must appear under exactly ONE {row.Language} element for the hit at "
                    + $"{row.Locator} to identify which element the switch selected, but it appears "
                    + $"under {carriers.Length}: [{string.Join(", ", carriers)}]. Choose a key unique "
                    + "to one element, or the row stops testing the category map.");

            Assert.Equal(row.ElementName, carriers[0]);
        }
    }

    /// <summary>
    /// THE DATA PREMISE BEHIND BOTH DELIBERATE HOLES: the read-only resource contains no
    /// <c>datawindow</c> element and no <c>custom</c> element, and its category elements are EXACTLY the
    /// six the switch can name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything else in this file asserts BEHAVIOUR. This one assertion drops to the DATA, because
    /// the two holes are only defensible in terms of it: the window and DataWindow categories share one
    /// arm because there is no <c>datawindow</c> element to give the second one, and
    /// <c>Enums.I18N_CAT_CUSTOM</c> has no arm because there is no <c>custom</c> element for it to read.
    /// Until now that premise lived only in comments - in this library's own source and in the sibling
    /// suites - so nothing would have caught a resource that grew one. A coordinated edit that added a
    /// <c>datawindow</c> element AND split the arm would leave every behavioural row above green while
    /// silently diverging from the oracle; this is the assertion that fails.
    /// </para>
    /// <para>
    /// READ-ONLY, STRUCTURALLY (constraint C-C). <c>XDocument.Load</c> is the only file API here and it
    /// cannot write; nothing in this class calls <c>Save</c>, <c>WriteAllText</c> or any other mutating
    /// API, and no synthetic table is written anywhere in this file. If this assertion ever fails, the
    /// resource is NOT the thing to change - investigate the implementation, or record a deliberate,
    /// justified change to the oracle.
    /// </para>
    /// <para>
    /// SCOPE. This is the data premise for the category map and nothing more. The resource's bytes,
    /// encoding and line endings belong to I18nResourceFidelityTests.cs, its entry values to
    /// MistranslationParityTests.cs, and the reader's own mechanics to I18nResourceReaderTests.cs. No
    /// XML package is referenced for this: <c>System.Xml.Linq</c> ships in the shared framework.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheResourceHasNoElementForEitherDeliberateHole()
    {
        RequireResourceFixture();

        XDocument document = XDocument.Load(I18nResourceReader.DefaultResourceFileName);
        XElement? root = document.Root;

        Assert.NotNull(root);

        // The absent names are searched over the WHOLE document rather than only its top level, so a
        // nested element smuggled in under a language half is caught too.
        foreach (string absent in AbsentElementNames)
        {
            Assert.Empty(document.Descendants(absent));
        }

        // And the category elements are exactly the six the switch can name - no more, no fewer. Both
        // language halves declare the same six, so the distinct set is six names rather than twelve.
        string[] declared = root!
            .Elements()
            .Select(element => element.Name.LocalName)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(MappedElementNames.Order(StringComparer.Ordinal).ToArray(), declared);
    }
}
