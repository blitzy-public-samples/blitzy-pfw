// ==================================================================================================
//  MistranslationParityTests.cs - THE TWO PRESERVED MISTRANSLATIONS, THE COLLISION THEY CAUSE,
//                                 AND THE DORMANT TABLE THAT MUST NEVER BE REVIVED
//  ------------------------------------------------------------------------------------------------
//  UNIT UNDER TEST   PowerFramework.Shared.Localization.EnglishProvider
//  ORACLES           pfw.i18n.xml                                              the translation table
//                    ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_en.sru  164 lines, and the
//                                                                              home of the dormant
//                                                                              correct table
//
//  WHY THIS FILE EXISTS AT ALL
//  ------------------------------------------------------------------------------------------------
//  AAP §0.8.2 names the two mistranslations below as legacy text to be preserved VERBATIM, and this
//  suite is the mechanism that stops a future maintainer from "fixing" them. Every assertion here is
//  load-bearing. There is no assertion in this file that may be relaxed, skipped, made
//  case-insensitive, turned into a substring check or wrapped in a conditional - each one is the
//  specification, not a tolerance for a bug.
//
//  The distinction that makes this file different from a normal parity suite: THE PROVIDER LOGIC IS
//  CORRECT AND THE DATA IS WRONG. EnglishProvider does nothing but read pfw.i18n.xml honestly, so
//  preserving these defects requires only that nothing repair them on read. That is why this suite
//  asserts in two layers - behavioural (what the provider answers) and data-level (what the resource
//  records) - because a "fix" can be applied at either layer and each layer catches a different one.
//
//  THE FOUR ENTRIES, TRANSCRIBED FROM THE RESOURCE AND VERIFIED BYTE BY BYTE
//  ------------------------------------------------------------------------------------------------
//  Read out of repository-root pfw.i18n.xml while writing this file, then re-verified with a byte
//  dump. Not recalled from memory, and not copied from any prose description of them:
//
//      L3    <tr text="最大化" to="Maximize" />      LEGITIMATE. Maximize really is "Maximize".
//      L4    <tr text="最小化" to="Maximize"/>       MISTRANSLATION 1. The MINIMIZE label carries
//                                                   the English word for its OPPOSITE, so L3 and L4
//                                                   collapse onto one English word.
//      L48   <tr text="查看详情" to="Show details"/>  LEGITIMATE. The uncorrupted neighbour of L49.
//      L49   <tr text="隐藏" to="Htexte details"/>    MISTRANSLATION 2. The HIDE label is a corrupted
//                                                   mixed string - the residue of a botched
//                                                   search-and-replace frozen into the data. Note
//                                                   the spelling: "Htexte", not "Hide".
//
//  The legitimate neighbours are asserted ALONGSIDE the mistranslations rather than in a separate
//  suite, and that pairing is deliberate. Only the pair shows that L4 is wrong while L3 is right,
//  and only the pair fails if someone "corrects" L4 - a suite that asserted L4 alone would read as
//  an arbitrary expectation with no evidence that anything is amiss.
//
//  THE DORMANT CORRECT TABLE - THE MOST DANGEROUS ARTIFACT IN THE LOCALIZATION SURFACE
//  ------------------------------------------------------------------------------------------------
//  n_cst_i18n_en.sru:L58-L153 is a COMMENTED-OUT pre-resource translation table. Before pfw.i18n.xml
//  existed the provider translated by matching literals in a nested choose case; when the table moved
//  into the resource file the code was commented out rather than deleted. It is unreachable in the
//  legacy and unreachable in the port - and it is CORRECT exactly where the shipped data is WRONG:
//
//      n_cst_i18n_en.sru:L64-L65    case "最小化" / text = "Minimize"       <- the correct wording
//      n_cst_i18n_en.sru:L149-L150  case "隐藏"   / text = "Hide details"   <- the correct wording
//
//  Anyone investigating either defect is therefore looking directly at the "fix", and applying it
//  would be invisible in a diff of the resource file because the resource file would not change.
//  Reviving that block is precisely the silent correction AAP §0.8.2 and constraint C-B forbid, and
//  AAP §0.4.2.3 names it by hand: the commented-out table "must not be revived". "Minimize" and
//  "Hide details" are therefore asserted ABSENT here - the negative half of the requirement, and the
//  half a positive-only suite would miss.
//
//  HOW A "FIX" COULD BE SMUGGLED IN, AND WHICH ASSERTION CATCHES IT
//  ------------------------------------------------------------------------------------------------
//  Each attack was reasoned through and confirmed caught before this file was considered finished:
//
//    an implementation that repairs minimize    -> REGION 1 rows 2 and 3 fail (expected "Maximize")
//    on read, or revives the dormant table         AND REGION 4 fails (must not be "Minimize")
//    an implementation that repairs hide        -> REGION 1 row 5 fails AND REGION 4 fails
//    someone edits the resource's L4 to         -> REGION 5 fails: a `to` value of "Minimize" now
//    to="Minimize" AND updates the expectation     appears in the document. This is the ONLY layer
//    here to match                                 that catches a coordinated edit of code and data,
//                                                  and it is why REGION 5 exists at all.
//    someone deletes entries from the resource  -> REGION 5's non-vacuity control fails: the
//    so the absence assertions pass vacuously      occurrence counts and the 126-entry total break
//    the Content link that delivers the table   -> RequireResourceFixture fails loudly instead of
//    is dropped from the library csproj             every lookup silently missing
//    an implementation returns 0 (not handled)  -> REGION 1's handled-code assertion fails, even
//    while still assigning the right text          though the text assertion would have passed
//    someone loosens Assert.Equal into a        -> REGION 2's explicit StringComparison.Ordinal
//    Contains check or an ignoreCase overload      assertion still fails: "Hide details" is not
//                                                  ordinally equal to "Htexte details", whereas a
//                                                  substring check would wrongly accept it
//
//  CONSTRAINTS THIS FILE IS HELD TO
//  ------------------------------------------------------------------------------------------------
//  review_rules returns exactly "No user rules provided.", so NO user-specified rule governs this
//  file and none is back-filled. The AAP §0.7.3 constraints bind in their place, and four reach here:
//
//    C-B  no behaviour improvement, no correction of any legacy defect. The mistranslations are the
//         expected output, and the correct wordings are asserted absent.
//    C-C  the legacy tree is read-only and IS the behavioural oracle. pfw.i18n.xml is READ and never
//         written: the ONLY two file APIs this file touches are File.Exists and XDocument.Load - both
//         read-only - and NOTHING here writes, moves, renames, re-encodes or reformats that path. The
//         guarantee is therefore structural rather than a matter of reviewer diligence, and it is
//         verifiable by grepping this file for a write API. IF AN ASSERTION HERE EVER FAILS, THE
//         CORRECT RESPONSE IS TO INVESTIGATE THE IMPLEMENTATION - NEVER TO EDIT THE RESOURCE. Every
//         assertion below repeats that in place, because a maintainer who "fixes" to="Maximize" into
//         to="Minimize" turns the build green while breaking parity with the oracle, which is the
//         one failure mode this suite exists to prevent.
//    C-K  every boundary-specific decision is documented. Each assertion names the defect it pins,
//         its pfw.i18n.xml line locator, and the read-only status of that file.
//    C-H  nullable and warnings-as-errors, zero warnings; together with EnglishProviderTests and
//         CategoriesTests this suite carries EnglishProvider's coverage.
//
//  SCOPE - DELIBERATELY NARROW
//  ------------------------------------------------------------------------------------------------
//  The two mistranslations, the collision, and the dormant table. Nothing else. The category-to-
//  element map, the source filter and the miss paths belong to EnglishProviderTests.cs; the category
//  constants to CategoriesTests.cs; the XPath substitution and lookup mechanics to
//  I18nResourceReaderTests.cs; the resource's bytes, encoding and line endings to
//  I18nResourceFidelityTests.cs. This file does not restate any of them, and it must not grow into a
//  full translation matrix - a suite that asserts everything is a suite in which nothing is legible.
// ==================================================================================================

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.Shared.Localization.Tests;

/// <summary>
/// Parity tests pinning the two documented mistranslations in <c>pfw.i18n.xml</c>, the English-word
/// collision the first of them causes, and the absence of the correct wordings that survive only in
/// the dormant commented-out table at <c>n_cst_i18n_en.sru</c>:L58-L153.
/// </summary>
/// <remarks>
/// <para>
/// Every expectation in this class is a preserved legacy defect asserted as the specification
/// [AAP §0.8.2, constraint C-B]. The resource file is read-only [constraint C-C]: this class only
/// ever reads it, and a failure here is a signal to investigate the implementation, never a licence
/// to edit the data.
/// </para>
/// <para>
/// Five regions, each pinning something a different kind of mistake would break. REGION 1 is the
/// positive table. REGION 2 is the ordinal-comparison guard. REGION 3 is the collision. REGION 4 is
/// the behavioural dormant-wording guard. REGION 5 is the data-level dormant-wording guard together
/// with its non-vacuity control.
/// </para>
/// </remarks>
public class MistranslationParityTests
{
    // ==============================================================================================
    //  THE VERIFIED DATA, AS NAMED CONSTANTS
    //  --------------------------------------------------------------------------------------------
    //  Declared once and referenced everywhere below, so a single spelling is under test and no two
    //  assertions can drift apart. Each carries the resource line it was transcribed from; the rows
    //  in REGION 1 additionally quote that line verbatim, so the table can be audited against the
    //  file without leaving this source.
    //
    //  These are PascalCase rather than the SCREAMING_SNAKE the ported legacy constants keep
    //  (AAP §0.4.5.3): they are this suite's own fixtures, not ported legacy identifiers, and this
    //  folder has no CA1707 waiver in .editorconfig precisely because it needs none.
    // ==============================================================================================

    /// <summary>
    /// The maximize label's source text, <c>最大化</c> [<c>pfw.i18n.xml</c>:L3].
    /// </summary>
    private const string MaximizeSourceText = "最大化";

    /// <summary>
    /// The minimize label's source text, <c>最小化</c> [<c>pfw.i18n.xml</c>:L4] - the key that
    /// carries MISTRANSLATION 1.
    /// </summary>
    private const string MinimizeSourceText = "最小化";

    /// <summary>
    /// The show-details label's source text, <c>查看详情</c> [<c>pfw.i18n.xml</c>:L48].
    /// </summary>
    private const string ShowDetailsSourceText = "查看详情";

    /// <summary>
    /// The hide label's source text, <c>隐藏</c> [<c>pfw.i18n.xml</c>:L49] - the key that carries
    /// MISTRANSLATION 2.
    /// </summary>
    private const string HideSourceText = "隐藏";

    /// <summary>
    /// The single English word BOTH the maximize and the minimize label resolve to
    /// [<c>pfw.i18n.xml</c>:L3 and :L4]. Correct for one of them and wrong for the other.
    /// </summary>
    private const string MaximizeTranslation = "Maximize";

    /// <summary>
    /// The show-details translation, exactly as recorded - one word, one space, one word
    /// [<c>pfw.i18n.xml</c>:L48].
    /// </summary>
    private const string ShowDetailsTranslation = "Show details";

    /// <summary>
    /// MISTRANSLATION 2's value, spelled exactly as the resource spells it
    /// [<c>pfw.i18n.xml</c>:L49]. <c>Htexte</c>, not <c>Hide</c> - the corruption is in the first
    /// word and a reader skimming this line will mis-see it.
    /// </summary>
    private const string CorruptedHideTranslation = "Htexte details";

    /// <summary>
    /// The correct minimize wording, which survives ONLY inside the dormant commented-out table
    /// [<c>n_cst_i18n_en.sru</c>:L65] and must never appear in a translation result or in the
    /// resource.
    /// </summary>
    private const string DormantMinimizeWording = "Minimize";

    /// <summary>
    /// The correct hide wording, which survives ONLY inside the dormant commented-out table
    /// [<c>n_cst_i18n_en.sru</c>:L150] and must never appear in a translation result or in the
    /// resource.
    /// </summary>
    private const string DormantHideWording = "Hide details";

    /// <summary>
    /// The value the provider returns when it found a translation and assigned it: <c>1</c>, the
    /// legacy's own <c>返回1代表已处理</c> - "returning 1 means handled"
    /// [<c>n_cst_i18n_en.sru</c>:L28, assigned and returned at :L53-L54].
    /// </summary>
    private const long HandledReturnCode = 1L;

    /// <summary>
    /// The element name every translation entry in the resource uses: <c>tr</c>.
    /// </summary>
    private const string EntryElementName = "tr";

    /// <summary>
    /// The attribute holding an entry's translated value: <c>to</c>.
    /// </summary>
    private const string TranslationAttributeName = "to";

    /// <summary>
    /// The attribute holding an entry's lookup key: <c>text</c>.
    /// </summary>
    private const string SourceTextAttributeName = "text";

    /// <summary>
    /// The number of translation entries the resource carries in total, across both language halves:
    /// 63 under <c>lang="en"</c> and 63 under <c>lang="cht"</c>.
    /// </summary>
    /// <remarks>
    /// Counted from the file, not estimated. It exists solely as a non-vacuity control for REGION 5:
    /// an absence assertion over an empty or truncated document passes for the wrong reason, and this
    /// is what makes that impossible. It is safe to pin an exact number because the resource is
    /// read-only - if this ever fails, the resource was edited, which is itself the finding.
    /// </remarks>
    private const int TotalTranslationEntryCount = 126;

    /// <summary>
    /// The message a fixture-guard failure carries, naming the project item that delivers the table.
    /// </summary>
    /// <remarks>
    /// The item is declared in the LIBRARY project, not in this test project, and flows down the
    /// ProjectReference edge - a deliberate arrangement recorded in
    /// PowerFramework.Shared.Localization.Tests.csproj. These tests are therefore the regression
    /// guard for that delivery, so the message points at where the item actually lives.
    /// </remarks>
    private const string FixtureMissingMessage =
        "pfw.i18n.xml is not in the test working directory, so every lookup in this suite would miss "
            + "and the mistranslation assertions would fail for a reason that has nothing to do with "
            + "the mistranslations. Restore the Content item in "
            + "shared/PowerFramework.Shared.Localization/PowerFramework.Shared.Localization.csproj "
            + "that links the repository-root pfw.i18n.xml into the build output.";

    /// <summary>
    /// Asserts the read-only resource is reachable under its bare relative name before any test
    /// relies on its contents.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The resource is resolved by BARE RELATIVE FILENAME, reproducing the legacy
    /// <c>_doc.LoadFile("pfw.i18n.xml")</c> at <c>n_cst_i18n_en.sru</c>:L159, which probes the
    /// process working directory and nowhere else. No test in this file computes a path to the
    /// repository root or opens the original by absolute path: the copy in the build output is
    /// byte-identical to the oracle, and reading the copy is what keeps the oracle untouched
    /// [constraint C-C].
    /// </para>
    /// <para>
    /// Without this guard a dropped Content link would turn REGION 5 green vacuously - no entry in an
    /// absent document carries a dormant wording - so the guard is what makes the absence assertions
    /// mean anything at all.
    /// </para>
    /// </remarks>
    private static void RequireResourceFixture()
    {
        Assert.True(File.Exists(I18nResourceReader.DefaultResourceFileName), FixtureMissingMessage);
    }

    /// <summary>
    /// Translates <paramref name="sourceText"/> through a provider reading the real resource and
    /// reports both what came back and whether the provider claimed to have handled it.
    /// </summary>
    /// <param name="category">The resource category to look the text up under.</param>
    /// <param name="sourceText">The text to translate, used as the lookup key.</param>
    /// <returns>
    /// The provider's return code, and the text as the provider left it. Both halves matter: a
    /// correct translation reported as not-handled is still a regression.
    /// </returns>
    /// <remarks>
    /// The source is always <see cref="Enums.I18N_SRC_PFW"/>, because anything else is declined at
    /// <c>n_cst_i18n_en.sru</c>:L32 before the table is reached and no assertion in this file would
    /// then be exercising the data. The default constructor is used on purpose: it is the one
    /// <c>ws_objects/pfw.pbl.src/pfw.sra</c>'s open event reaches [:L95-L103] - the framework
    /// application, not the same-named packager object - and it resolves the table exactly as the
    /// legacy does.
    /// </remarks>
    private static (long ReturnCode, string? Text) Translate(long category, string sourceText)
    {
        EnglishProvider provider = new();

        string? text = sourceText;
        long returnCode = provider.OnTranslate(Enums.I18N_SRC_PFW, category, ref text);

        return (returnCode, text);
    }

    /// <summary>
    /// Counts the entries in the read-only resource whose translated value is ordinally equal to
    /// <paramref name="translation"/>.
    /// </summary>
    /// <param name="translation">The translated value to count occurrences of.</param>
    /// <returns>The number of matching entries across both language halves of the document.</returns>
    /// <remarks>
    /// <para>
    /// READ-ONLY, STRUCTURALLY. <see cref="XDocument.Load(string)"/> is the only file access here and
    /// it cannot write; nothing in this class calls <c>Save</c>, <c>WriteAllText</c> or any other
    /// mutating API against this path [constraint C-C].
    /// </para>
    /// <para>
    /// Both language halves are scanned rather than just <c>lang="en"</c>, and deliberately so: the
    /// point of REGION 5 is to catch a dormant wording appearing ANYWHERE in the resource, and an
    /// English string smuggled into the <c>cht</c> half would be exactly the kind of edit a
    /// scoped-to-English scan would miss.
    /// </para>
    /// <para>
    /// The comparison is <see cref="StringComparison.Ordinal"/>, matching the reader and the legacy
    /// XPath predicate, both of which compare exact strings with no culture, casing or normalisation
    /// applied.
    /// </para>
    /// </remarks>
    private static int CountEntriesTranslatedTo(string translation)
    {
        XDocument document = XDocument.Load(I18nResourceReader.DefaultResourceFileName);

        return document
            .Descendants(EntryElementName)
            .Count(entry => string.Equals(
                entry.Attribute(TranslationAttributeName)?.Value,
                translation,
                StringComparison.Ordinal));
    }

    /// <summary>
    /// Describes every entry in the read-only resource whose translated value is ordinally equal to
    /// <paramref name="translation"/>, for use in an assertion message.
    /// </summary>
    /// <param name="translation">The translated value to look for.</param>
    /// <returns>
    /// One human-readable description per matching entry, naming the entry's key, its value and the
    /// element and language it sits under, so a failure points straight at the offending line.
    /// </returns>
    /// <remarks>
    /// Only ever called on the failure path. It exists because "an entry carries the dormant wording"
    /// is useless without saying WHICH entry: the person reading that failure has to be steered to
    /// the implementation or to the edited data, and away from relaxing the assertion.
    /// </remarks>
    private static IReadOnlyList<string> DescribeEntriesTranslatedTo(string translation)
    {
        XDocument document = XDocument.Load(I18nResourceReader.DefaultResourceFileName);

        return document
            .Descendants(EntryElementName)
            .Where(entry => string.Equals(
                entry.Attribute(TranslationAttributeName)?.Value,
                translation,
                StringComparison.Ordinal))
            .Select(entry => string.Concat(
                "<tr text=\"",
                entry.Attribute(SourceTextAttributeName)?.Value,
                "\" to=\"",
                entry.Attribute(TranslationAttributeName)?.Value,
                "\"/> under <",
                entry.Parent?.Name.LocalName,
                " lang=\"",
                entry.Parent?.Attribute("lang")?.Value,
                "\">"))
            .ToList();
    }

    // ==============================================================================================
    //  REGION 1 - THE POSITIVE TABLE. THE MISTRANSLATIONS ARE THE EXPECTED OUTPUT.
    //  --------------------------------------------------------------------------------------------
    //  ONE theory over ONE table, so the two mistranslations and their two legitimate neighbours sit
    //  side by side where a reader sees them together. That layout is the point: split across
    //  separate tests, "minimize translates to Maximize" reads as a typo in the test, whereas next to
    //  "maximize translates to Maximize" it reads as what it is - a defect in the data, pinned on
    //  purpose. AAP §0.6.7 asks for parity matrices expressed as theories with member data, and this
    //  is one.
    // ==============================================================================================

    /// <summary>
    /// The four English entries this suite pins, plus the second category that reaches the first
    /// mistranslation: five rows of category, source text and the exact translation the read-only
    /// resource records for it.
    /// </summary>
    /// <returns>
    /// Rows of the resource category, the lookup key, and the translation transcribed from the cited
    /// line of <c>pfw.i18n.xml</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Every row's expected value was read out of <c>pfw.i18n.xml</c> at the line quoted beside it
    /// and compared character by character before being written here. The resource is read-only
    /// [constraint C-C], so these values are fixed: if a row ever fails, the implementation changed
    /// or the data was edited, and the response is to fix that - never to update the expectation.
    /// </para>
    /// <para>
    /// The two mistranslation rows are marked in place. They are not defects being tolerated; they
    /// are the specification [AAP §0.8.2, constraint C-B].
    /// </para>
    /// </remarks>
    public static TheoryData<long, string, string> PreservedTranslationRows()
    {
        return new TheoryData<long, string, string>
        {
            // ---------------------------------------------------------------------------------
            // ROW 1 - LEGITIMATE, AND THE EVIDENCE THAT ROW 2 IS WRONG.
            // pfw.i18n.xml:L3   <tr text="最大化" to="Maximize" />
            // The maximize label really does translate to "Maximize". Without this row the next
            // one has no context and looks like a mistake in the fixture rather than in the data.
            // The resource is read-only and must not be edited to change this expectation.
            // ---------------------------------------------------------------------------------
            { Enums.I18N_CAT_WINDOW, MaximizeSourceText, MaximizeTranslation },

            // ---------------------------------------------------------------------------------
            // ROW 2 - MISTRANSLATION 1. PINS A LEGACY DEFECT DELIBERATELY.
            // pfw.i18n.xml:L4   <tr text="最小化" to="Maximize"/>
            // The MINIMIZE label resolves to the English word for its OPPOSITE. The correct
            // wording is "Minimize" and it survives only in the dormant table at
            // n_cst_i18n_en.sru:L65, which must not be revived [AAP §0.4.2.3]. Expecting
            // "Maximize" here is required by constraint C-B; the resource is read-only
            // [constraint C-C] and must not be edited to change this expectation.
            // ---------------------------------------------------------------------------------
            { Enums.I18N_CAT_WINDOW, MinimizeSourceText, MaximizeTranslation },

            // ---------------------------------------------------------------------------------
            // ROW 3 - MISTRANSLATION 1 REACHED THROUGH ITS SECOND CATEGORY.
            // pfw.i18n.xml:L4 again, via n_cst_i18n_en.sru:L35-L36.
            // I18N_CAT_WINDOW (0) and I18N_CAT_DATAWINDOW (4) are two distinct categories that
            // both select the 'window' element, because the resource has no 'datawindow' element
            // at all. The defect is therefore reachable through BOTH categories, and both paths
            // matter: a caller passing the DataWindow category gets the mistranslation too. The
            // resource is read-only and must not be edited to change this expectation.
            // ---------------------------------------------------------------------------------
            { Enums.I18N_CAT_DATAWINDOW, MinimizeSourceText, MaximizeTranslation },

            // ---------------------------------------------------------------------------------
            // ROW 4 - LEGITIMATE, AND THE UNCORRUPTED NEIGHBOUR OF ROW 5.
            // pfw.i18n.xml:L48   <tr text="查看详情" to="Show details"/>
            // Reached through Categories.CAT_MSGBOX [n_cst_i18n_en.sru:L43-L44]. One space
            // between the two words, asserted exactly. This row is what shows that the msgbox
            // element is not wholesale corrupted - only the single entry below it is.
            // The resource is read-only and must not be edited to change this expectation.
            // ---------------------------------------------------------------------------------
            { Categories.CAT_MSGBOX, ShowDetailsSourceText, ShowDetailsTranslation },

            // ---------------------------------------------------------------------------------
            // ROW 5 - MISTRANSLATION 2. PINS A LEGACY DEFECT DELIBERATELY.
            // pfw.i18n.xml:L49   <tr text="隐藏" to="Htexte details"/>
            // The HIDE label resolves to a corrupted mixed string. Note the spelling - "Htexte",
            // not "Hide": the corruption is inside the first word, which is exactly why a
            // substring or case-insensitive assertion would wrongly accept the correct wording
            // here. The correct wording is "Hide details" and it survives only in the dormant
            // table at n_cst_i18n_en.sru:L150, which must not be revived. The resource is
            // read-only [constraint C-C] and must not be edited to change this expectation.
            // ---------------------------------------------------------------------------------
            { Categories.CAT_MSGBOX, HideSourceText, CorruptedHideTranslation },
        };
    }

    /// <summary>
    /// Proves the provider returns each of the five pinned entries exactly as the read-only resource
    /// records it, AND reports the request as handled while doing so.
    /// </summary>
    /// <param name="category">The resource category to look the text up under.</param>
    /// <param name="sourceText">The lookup key.</param>
    /// <param name="expectedTranslation">The translation transcribed from the cited resource line.</param>
    /// <remarks>
    /// <para>
    /// THE RETURN CODE IS ASSERTED AS WELL AS THE TEXT, and that is not belt-and-braces. The legacy
    /// assigns the translation and answers <c>1</c> in the same two statements
    /// [<c>n_cst_i18n_en.sru</c>:L53-L54], so an implementation that wrote the right text while
    /// answering <c>0</c> would be reported as not-handled by every caller - the facade would then
    /// treat it as "no provider dealt with this" - and a text-only assertion would sail past it.
    /// </para>
    /// <para>
    /// The comparison is exact: no trimming, no case folding, no normalisation, no substring match.
    /// <see cref="Assert.Equal(string?, string?)"/> compares ordinally by default, and REGION 2
    /// re-asserts that ordinally in a form that cannot be loosened into an <c>ignoreCase</c> overload
    /// without deleting it.
    /// </para>
    /// <para>
    /// If a row here fails, investigate the implementation. The resource is read-only [constraint
    /// C-C] and editing it to satisfy this test would break parity with the behavioural oracle while
    /// turning the build green - the single failure mode this suite exists to prevent.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(PreservedTranslationRows))]
    public void EveryPinnedEntryTranslatesExactlyAsTheResourceRecordsIt(
        long category,
        string sourceText,
        string expectedTranslation)
    {
        RequireResourceFixture();

        (long returnCode, string? translated) = Translate(category, sourceText);

        // n_cst_i18n_en.sru:L54 - a hit assigns the translation and returns 1.
        Assert.Equal(HandledReturnCode, returnCode);

        // The value the read-only resource records, byte for byte. For rows 2 and 5 this is the
        // preserved mistranslation and it is the CORRECT expectation [AAP §0.8.2, constraint C-B].
        Assert.Equal(expectedTranslation, translated);
    }

    // ==============================================================================================
    //  REGION 2 - THE ORDINAL-COMPARISON GUARD.
    //  --------------------------------------------------------------------------------------------
    //  REGION 1 already compares ordinally, because that is what Assert.Equal's string overload
    //  does. This region exists because that fact is INVISIBLE at the call site: xunit also ships an
    //  overload taking ignoreCase, ignoreLineEndingDifferences and ignoreWhiteSpaceDifferences, and a
    //  future maintainer chasing a failure could switch to it in one keystroke and make MISTRANSLATION
    //  2 pass against the correct wording. Spelling StringComparison.Ordinal out means the guarantee
    //  cannot be weakened without deleting an assertion that says why it is there.
    //
    //  It is not theoretical for this data. Under a substring check "Hide details" would be accepted
    //  where "Htexte details" is required, because both end in "details" - the corruption is in the
    //  first word only. Ordinal exact equality is the only comparison that distinguishes them.
    // ==============================================================================================

    /// <summary>
    /// Proves the two mistranslations are matched by exact ordinal equality, and that the correct
    /// wordings are rejected by that comparison rather than merely differing under a looser one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Four assertions per defect, and each one closes a different loophole: exact ordinal equality
    /// to the recorded value; ordinal INequality to the dormant correct wording; a failed
    /// case-insensitive comparison against the dormant wording, which is what rules out an
    /// <c>ignoreCase</c> overload smuggling it through; and a failed <c>Contains</c> check, which is
    /// what rules out a substring assertion doing the same.
    /// </para>
    /// <para>
    /// The resource is read-only [constraint C-C]; this test reads it through the provider and writes
    /// nothing. A failure means the implementation or the data changed.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheMistranslationsAreMatchedOrdinallyAndTheCorrectWordingsAreNot()
    {
        RequireResourceFixture();

        // MISTRANSLATION 1 - pfw.i18n.xml:L4, correct wording dormant at n_cst_i18n_en.sru:L65.
        (long minimizeReturnCode, string? minimize) = Translate(
            Enums.I18N_CAT_WINDOW,
            MinimizeSourceText);

        Assert.Equal(HandledReturnCode, minimizeReturnCode);
        Assert.True(
            string.Equals(MaximizeTranslation, minimize, StringComparison.Ordinal),
            "pfw.i18n.xml:L4 must resolve 最小化 ordinally to \"Maximize\". This is MISTRANSLATION 1 "
                + "and it is preserved on purpose; the resource is read-only, so investigate the "
                + "implementation rather than editing the data.");
        Assert.False(
            string.Equals(DormantMinimizeWording, minimize, StringComparison.Ordinal),
            "The result equals the dormant correct wording from n_cst_i18n_en.sru:L65, so the "
                + "commented-out table has been revived or the resource has been edited.");
        Assert.False(
            string.Equals(DormantMinimizeWording, minimize, StringComparison.OrdinalIgnoreCase),
            "The result matches the dormant wording once case is ignored, which means a "
                + "case-insensitive comparison anywhere on this path would accept the correction.");

        // MISTRANSLATION 2 - pfw.i18n.xml:L49, correct wording dormant at n_cst_i18n_en.sru:L150.
        (long hideReturnCode, string? hide) = Translate(Categories.CAT_MSGBOX, HideSourceText);

        Assert.Equal(HandledReturnCode, hideReturnCode);
        Assert.True(
            string.Equals(CorruptedHideTranslation, hide, StringComparison.Ordinal),
            "pfw.i18n.xml:L49 must resolve 隐藏 ordinally to \"Htexte details\", spelled exactly so. "
                + "This is MISTRANSLATION 2 and it is preserved on purpose; the resource is "
                + "read-only, so investigate the implementation rather than editing the data.");
        Assert.False(
            string.Equals(DormantHideWording, hide, StringComparison.Ordinal),
            "The result equals the dormant correct wording from n_cst_i18n_en.sru:L150, so the "
                + "commented-out table has been revived or the resource has been edited.");

        // THE SUBSTRING LOOPHOLE, CLOSED EXPLICITLY. Both the recorded value and the dormant wording
        // end in "details", so a Contains-style assertion cannot tell them apart. Asserting that the
        // result does NOT contain the dormant wording is what makes the difference load-bearing.
        Assert.NotNull(hide);
        Assert.DoesNotContain(DormantHideWording, hide, StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  REGION 3 - THE COLLISION, ASSERTED AS ITS OWN NAMED TEST.
    //  --------------------------------------------------------------------------------------------
    //  MISTRANSLATION 1 has a second-order consequence the positive table states but does not make
    //  legible: because pfw.i18n.xml:L4 carries the same value as :L3, TWO DISTINCT LABELS COLLAPSE
    //  ONTO ONE ENGLISH WORD. An English-locale user sees "Maximize" on both the maximize and the
    //  minimize affordance, and the framework offers no way to tell them apart.
    //
    //  It gets a dedicated, explicitly named test because in the table alone the repeated expected
    //  value reads like a copy-paste slip in the fixture. Named, it reads as the finding it is - and
    //  it also pins the thing a partial "fix" would break first: correcting only :L4 leaves the
    //  positive table failing on one row and this test failing outright, which is exactly the loud
    //  signal wanted.
    // ==============================================================================================

    /// <summary>
    /// Proves that the maximize and minimize labels - two different source texts - resolve to the
    /// SAME English word, which is the collision <c>pfw.i18n.xml</c>:L4 causes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first assertion is the one that stops this test from being self-fulfilling: it confirms the
    /// two INPUTS genuinely differ. Without it, a copy-paste slip that made both source constants the
    /// same string would produce a test that passes while proving nothing at all - and the two keys
    /// differ by a single Chinese character (大 versus 小), so that slip is entirely plausible.
    /// </para>
    /// <para>
    /// Pins <c>pfw.i18n.xml</c>:L3 and :L4 together [AAP §0.8.2, constraint C-B]. The resource is
    /// read-only [constraint C-C]: if this fails, one of those two lines was edited or the provider
    /// started repairing on read, and the fix belongs there - not in this expectation.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheMaximizeAndMinimizeLabelsCollapseOntoOneEnglishWord()
    {
        RequireResourceFixture();

        // The two keys really are different. If this ever fails the rest of the test is meaningless,
        // which is why it comes first: 最大化 and 最小化 differ only in their middle character.
        Assert.NotEqual(MaximizeSourceText, MinimizeSourceText);

        (long maximizeReturnCode, string? maximize) = Translate(
            Enums.I18N_CAT_WINDOW,
            MaximizeSourceText);

        (long minimizeReturnCode, string? minimize) = Translate(
            Enums.I18N_CAT_WINDOW,
            MinimizeSourceText);

        // Both are hits: the collision is between two entries that BOTH resolve, not between one
        // that resolves and one that falls through untranslated [n_cst_i18n_en.sru:L54].
        Assert.Equal(HandledReturnCode, maximizeReturnCode);
        Assert.Equal(HandledReturnCode, minimizeReturnCode);

        // pfw.i18n.xml:L3 - legitimate.
        Assert.Equal(MaximizeTranslation, maximize);

        // pfw.i18n.xml:L4 - MISTRANSLATION 1, pinned deliberately. The resource is read-only and must
        // not be edited to change this expectation.
        Assert.Equal(MaximizeTranslation, minimize);

        // THE COLLISION ITSELF, stated as a relationship rather than as two separate values. This is
        // what a caller actually experiences, and it is what a partial correction of :L4 breaks.
        Assert.True(
            string.Equals(maximize, minimize, StringComparison.Ordinal),
            "pfw.i18n.xml:L3 and :L4 must resolve to the SAME English word - that collision IS "
                + "MISTRANSLATION 1 and it is preserved on purpose [AAP §0.8.2]. The resource is "
                + "read-only: if these have diverged, the data was edited or the provider now repairs "
                + "the minimize label on read, and that is what needs reverting.");
    }

    // ==============================================================================================
    //  REGION 4 - THE DORMANT-TABLE GUARD, BEHAVIOURAL LAYER.
    //  --------------------------------------------------------------------------------------------
    //  The negative half of constraint C-B, and the half a positive-only suite would miss entirely.
    //  REGION 1 says what the provider MUST answer; this region says what it must NEVER answer.
    //
    //  Those are not the same assertion in the presence of the dormant table. n_cst_i18n_en.sru:L58-
    //  L153 is a fully written, syntactically complete, CORRECT translation table sitting commented
    //  out in the oracle, and someone porting or reviewing this provider will read it while
    //  investigating either defect. Turning it into a fallback, a seed dictionary, a special case or
    //  a post-processing step would be exactly the silent correction AAP §0.4.2.3 forbids by name -
    //  and it would leave pfw.i18n.xml untouched, so it would be invisible in a diff of the data.
    //  These assertions are what make it visible.
    // ==============================================================================================

    /// <summary>
    /// The two dormant correct wordings that must never be observable, each with the source text that
    /// would produce it if the commented-out table were revived and the locator of the line it lives
    /// on.
    /// </summary>
    /// <returns>
    /// Rows of the resource category, the lookup key, the forbidden wording, and the
    /// <c>n_cst_i18n_en.sru</c> locator of the dormant line that carries it.
    /// </returns>
    /// <remarks>
    /// The minimize wording appears twice because it is reachable through two categories: the window
    /// and DataWindow categories share the <c>window</c> element [<c>n_cst_i18n_en.sru</c>:L35-L36],
    /// so a revived table would surface it through both and both paths have to be guarded.
    /// </remarks>
    public static TheoryData<long, string, string, string> DormantWordingRows()
    {
        return new TheoryData<long, string, string, string>
        {
            // The correct minimize wording, dormant at n_cst_i18n_en.sru:L64-L65, reached through the
            // window category. pfw.i18n.xml:L4 records "Maximize" instead and that is what must come
            // back; the resource is read-only and must not be edited to change this expectation.
            {
                Enums.I18N_CAT_WINDOW,
                MinimizeSourceText,
                DormantMinimizeWording,
                "n_cst_i18n_en.sru:L65"
            },

            // The same wording through the DataWindow category, which selects the same element.
            {
                Enums.I18N_CAT_DATAWINDOW,
                MinimizeSourceText,
                DormantMinimizeWording,
                "n_cst_i18n_en.sru:L65"
            },

            // The correct hide wording, dormant at n_cst_i18n_en.sru:L149-L150, reached through the
            // message-box category. pfw.i18n.xml:L49 records the corrupted "Htexte details" instead
            // and that is what must come back; the resource is read-only and must not be edited to
            // change this expectation.
            {
                Categories.CAT_MSGBOX,
                HideSourceText,
                DormantHideWording,
                "n_cst_i18n_en.sru:L150"
            },
        };
    }

    /// <summary>
    /// Proves no lookup ever answers with a wording from the dormant commented-out table.
    /// </summary>
    /// <param name="category">The resource category to look the text up under.</param>
    /// <param name="sourceText">The lookup key that would produce the forbidden wording.</param>
    /// <param name="forbiddenWording">The dormant correct wording that must never be observable.</param>
    /// <param name="dormantLocator">
    /// Where in the oracle that wording lives, carried into the failure message so whoever reads it
    /// is pointed straight at the block that must not be revived.
    /// </param>
    /// <remarks>
    /// <para>
    /// The translation is still asserted to be a HIT. That matters: an implementation could avoid the
    /// forbidden wording simply by failing to translate at all, and this test would pass on a
    /// regression. Requiring the handled code as well means the only way through is to return the
    /// resource's own - wrong - value.
    /// </para>
    /// <para>
    /// The resource is read-only [constraint C-C]. A failure here means the dormant table was revived
    /// or the data was edited; either way the fix is in the implementation or in reverting the data,
    /// never in relaxing this assertion.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(DormantWordingRows))]
    public void NoLookupEverAnswersWithADormantCorrectWording(
        long category,
        string sourceText,
        string forbiddenWording,
        string dormantLocator)
    {
        RequireResourceFixture();

        (long returnCode, string? translated) = Translate(category, sourceText);

        // A hit, not a decline. Without this an implementation that stopped translating altogether
        // would satisfy the inequality below and hide a real regression.
        Assert.Equal(HandledReturnCode, returnCode);

        Assert.False(
            string.Equals(forbiddenWording, translated, StringComparison.Ordinal),
            $"The lookup answered with \"{forbiddenWording}\", the CORRECT wording that exists only "
                + $"inside the dormant commented-out table at {dormantLocator}. Reviving that block - "
                + "as a fallback, a seed table, a special case or a post-processing step - is exactly "
                + "the silent correction of legacy behaviour that AAP §0.4.2.3 and constraint C-B "
                + "forbid. pfw.i18n.xml is read-only and is the behavioural oracle: revert the "
                + "implementation, do not edit the resource and do not relax this assertion.");
    }

    // ==============================================================================================
    //  REGION 5 - THE DORMANT-TABLE GUARD, DATA LAYER. THE DEFENCE OF THE READ-ONLY ORACLE ITSELF.
    //  --------------------------------------------------------------------------------------------
    //  This region asserts against the DOCUMENT, not against the provider, and that is the whole
    //  reason it exists. Every assertion in REGIONS 1 to 4 goes through EnglishProvider, so all of
    //  them share one blind spot: A COORDINATED EDIT PASSES. Someone who "fixes" pfw.i18n.xml:L4 to
    //  to="Minimize" and updates the expectation in REGION 1 to match leaves every behavioural test
    //  green - the provider is still reading the data honestly, and the data is now wrong in the
    //  other direction. Nothing above would notice.
    //
    //  Asserting on the data closes that. The moment a `to` value of "Minimize" or "Hide details"
    //  appears ANYWHERE in the document - in either language half, under any element - this region
    //  fails, and it fails whether or not any expectation elsewhere was updated to agree with it.
    //  That is what it means for pfw.i18n.xml to be READ-ONLY and to be the behavioural oracle
    //  [constraint C-C]: the data is not a test fixture to be adjusted, it is the specification, and
    //  this is the assertion that treats it that way.
    //
    //  Read-only structurally, not by convention: the only file access in this region is
    //  XDocument.Load, through the shared helpers above. Nothing here can write.
    // ==============================================================================================

    /// <summary>
    /// The two dormant correct wordings that must not appear as a translated value anywhere in the
    /// read-only resource, each with the locator of the dormant line it lives on.
    /// </summary>
    /// <returns>Rows of the forbidden wording and its <c>n_cst_i18n_en.sru</c> locator.</returns>
    /// <remarks>
    /// One row per wording rather than one row per lookup: this is a whole-document scan, so the
    /// category and language a wording might have been smuggled into are deliberately not narrowed.
    /// </remarks>
    public static TheoryData<string, string> DormantWordings()
    {
        return new TheoryData<string, string>
        {
            // The correct minimize wording. pfw.i18n.xml:L4 records "Maximize" in its place, and that
            // mistranslation is required to survive [AAP §0.8.2].
            { DormantMinimizeWording, "n_cst_i18n_en.sru:L65" },

            // The correct hide wording. pfw.i18n.xml:L49 records the corrupted "Htexte details" in its
            // place, and that mistranslation is required to survive [AAP §0.8.2].
            { DormantHideWording, "n_cst_i18n_en.sru:L150" },
        };
    }

    /// <summary>
    /// Proves no translation entry anywhere in the read-only resource carries a dormant correct
    /// wording as its value - the guard that catches an edit to the DATA rather than to the code.
    /// </summary>
    /// <param name="forbiddenWording">The dormant wording that must not appear as any entry's value.</param>
    /// <param name="dormantLocator">
    /// Where in the oracle that wording lives, carried into the failure message.
    /// </param>
    /// <remarks>
    /// <para>
    /// Scans BOTH language halves and every element. Narrowing the scan to <c>lang="en"</c> would
    /// leave the obvious hiding place open, and the cost of scanning all 126 entries is nil.
    /// </para>
    /// <para>
    /// If this fails, the resource was edited. That edit is the defect - not this assertion. Revert
    /// <c>pfw.i18n.xml</c> to the oracle at the repository root, which is read-only for exactly this
    /// reason [constraint C-C], and leave the expectation alone.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(DormantWordings))]
    public void NoEntryInTheResourceCarriesADormantCorrectWording(
        string forbiddenWording,
        string dormantLocator)
    {
        RequireResourceFixture();

        int occurrences = CountEntriesTranslatedTo(forbiddenWording);

        if (occurrences != 0)
        {
            IReadOnlyList<string> offending = DescribeEntriesTranslatedTo(forbiddenWording);

            Assert.Fail(
                $"{occurrences} entry(ies) in pfw.i18n.xml translate to \"{forbiddenWording}\", the "
                    + $"CORRECT wording that must exist only inside the dormant commented-out table "
                    + $"at {dormantLocator}. THE RESOURCE HAS BEEN EDITED, AND THAT EDIT IS THE "
                    + "DEFECT: pfw.i18n.xml is read-only and is the behavioural oracle for this "
                    + "refactor [constraint C-C], and the two mistranslations it carries are required "
                    + "to survive verbatim [AAP §0.8.2]. Revert the resource to the repository-root "
                    + "original; do not update any expectation in this suite to agree with the edit. "
                    + $"Offending entries: {string.Join("; ", offending)}");
        }
    }

    /// <summary>
    /// Proves the whole-document scan REGION 5 relies on genuinely reaches the resource's data, so its
    /// absence assertions cannot pass vacuously.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An absence assertion over an empty, truncated or unparsed document passes for entirely the
    /// wrong reason, and that is the one way REGION 5 could go quietly useless. This test is its
    /// control: it asserts the scan finds the entry count the resource actually has, and that it finds
    /// the exact occurrence counts of three values that ARE present - including, at the data layer,
    /// the collision itself.
    /// </para>
    /// <para>
    /// The two-occurrence count for the maximize word is the collision expressed as data: <b>two</b>
    /// distinct entries, <c>pfw.i18n.xml</c>:L3 and :L4, record the same translated value. REGION 3
    /// asserts that through the provider; this asserts it in the file.
    /// </para>
    /// <para>
    /// Exact counts are safe to pin because the resource is read-only [constraint C-C]. If one of them
    /// fails, the resource was edited - which is itself the finding, and the response is to revert the
    /// data rather than to adjust the number.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDocumentScanReachesTheRealDataSoTheAbsenceAssertionsAreNotVacuous()
    {
        RequireResourceFixture();

        XDocument document = XDocument.Load(I18nResourceReader.DefaultResourceFileName);

        // 63 entries under lang="en" and 63 under lang="cht". Counted from the file.
        Assert.Equal(
            TotalTranslationEntryCount,
            document.Descendants(EntryElementName).Count());

        // Every entry carries a translated value, so a null-valued attribute can never be the reason
        // a dormant wording appears to be absent.
        Assert.All(
            document.Descendants(EntryElementName),
            entry => Assert.NotNull(entry.Attribute(TranslationAttributeName)));

        // THE COLLISION, AT THE DATA LAYER. pfw.i18n.xml:L3 and :L4 both record "Maximize", so the
        // count is TWO. One would mean the mistranslation at :L4 had been corrected - which is the
        // silent correction constraint C-B forbids - and the resource is read-only, so the response
        // would be to revert the data, never to change this number.
        Assert.Equal(2, CountEntriesTranslatedTo(MaximizeTranslation));

        // pfw.i18n.xml:L48 - the legitimate neighbour, recorded exactly once.
        Assert.Equal(1, CountEntriesTranslatedTo(ShowDetailsTranslation));

        // pfw.i18n.xml:L49 - MISTRANSLATION 2, recorded exactly once and spelled "Htexte details".
        // This is the row that proves the scan can see the corrupted value at all, which is what
        // gives the "Hide details" absence assertion its meaning.
        Assert.Equal(1, CountEntriesTranslatedTo(CorruptedHideTranslation));
    }
}
