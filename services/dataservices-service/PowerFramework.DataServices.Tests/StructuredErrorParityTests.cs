// =====================================================================================================
//  StructuredErrorParityTests - THE CROSS-CUTTING DIALOG SWEEP FOR AAP 0.2.1.3 CORRECTION 5
// =====================================================================================================
//
//  WHAT THIS FILE IS FOR, AND WHAT IT DELIBERATELY LEAVES TO OTHERS
//  ---------------------------------------------------------------
//  AAP 0.2.1.3 Correction 5 records that dialogs are a REAL presentation dependency sitting inside
//  in-scope DataServices logic, and that each one becomes a structured error result preserving the exact
//  message text, the localization category, the `Sprintf` substitution arguments, the severity, and - for
//  parse failures - the expression text plus caret position. ONLY THE DELIVERY CHANNEL CHANGES.
//
//  The PER-SITE payload details are already asserted by the files that own each site:
//
//      RowSelectServiceTests.cs        n_cst_dwsvc_rowselect.sru:L239, every field
//      ContextMenuModelTests.cs        the ten n_cst_dwsvc_contextmenu.sru sites, per kind and locator
//      ValidationErrorEventTests.cs    se_cst_dw.sru:L357 and its arms
//      ParseErrorFormatterTests.cs     the caret ARITHMETIC and the rendered marker
//
//  This file asserts the two things NONE of them can assert, because each of them sees only its own
//  corner of the estate:
//
//      (1) THE CENSUS IS COMPLETE - every live dialog site in the in-scope DataWindow service layer is
//          accounted for, none is invented, and none is lost.
//      (2) THE LOCALIZATION SPLIT IS PRESERVED - the rowselect, contextmenu and validation-error messages
//          route through I18n; the twenty-eight column-expression messages DO NOT. That split is a
//          DEFECT REPRODUCED ON PURPOSE.
//
//  It therefore does not re-assert what the four files above already pin. Where it touches a payload it
//  touches it as a CENSUS ROW - "this site produces a payload with these fields populated" - not as a
//  behavioural re-derivation of that site's semantics.
//
// -----------------------------------------------------------------------------------------------------
//  THE MEASURED CENSUS - COUNTS TAKEN FROM THE ORACLE, RECORDED SO A FUTURE READER CAN RE-VERIFY
// -----------------------------------------------------------------------------------------------------
//
//  Every count below was MEASURED against the legacy tree rather than copied from the AAP, using a
//  scanner that classifies each `MessageBox` / `MessageBoxEx` occurrence as live or commented by tracking
//  THREE lexical states across the whole file - PowerScript `//` line comments, PowerScript `/* */` BLOCK
//  comments, and double-quoted string literals. `LegacyDialogScanner` in this file is that scanner, so
//  the numbers are not merely documented here, they are RE-MEASURED ON EVERY TEST RUN against the
//  read-only oracle and compared with the hand-written census. If the oracle ever changes, a named row
//  fails.
//
//      LEGACY OBJECT                                            LIVE   COMMENTED   LOCALIZES
//      ws_objects/pfw.datawindow.services.pbl.src/
//        n_cst_dwsvc_rowselect.sru                                 1           0   yes
//        n_cst_dwsvc_contextmenu.sru                              10           0   yes
//        se_cst_dw.sru                                             1           2   yes
//        n_cst_dwsvc_columnexp.sru                                28           0   NO
//        n_cst_dwsvc.sru                                           0           0   n/a
//      ------------------------------------------------------------------------------------
//        TOTAL LIVE DIALOG SITES                                  40
//
//  WHY THE `COMMENTED` COLUMN MATTERS, AND THE ONE TRAP IN IT
//  ---------------------------------------------------------
//  `se_cst_dw.sru:L286` LOOKS like a live dialog - the line itself carries no comment marker at all - but
//  it sits inside a `/* ... */` block that opens at :L280 and closes at :L292. It is the DORMANT
//  byte-length validation path AAP 0.6.1.5 describes, carried across as commented and inert rather than
//  revived. A scanner that only tests whether a LINE STARTS WITH `//` counts it as live and reports 41
//  sites instead of 40. The scanner here tracks block state, so it does not.
//
//  The second commented occurrence, `se_cst_dw.sru:L368`, is PROSE - a comment that mentions MessageBox
//  while explaining why the row-existence guard is defensive. It is not a call at all.
//
//  `n_cst_dwsvc_columnexp.sru` is the file the AAP characterises as "28 live sites and zero commented",
//  and that is exactly what the scanner finds. It is asserted as its own row because the CLAIM of zero
//  commented sites is load-bearing: it is what rules out the reading that some of the twenty-eight were
//  disabled upstream and need no port.
//
//  `n_cst_dwsvc.sru` - 864 lines, the DataWindow service base - contains ZERO dialogs. It is in the
//  census precisely so that a future change which introduces one fails a named row instead of passing
//  silently. A census with no zero rows cannot detect growth.
//
// -----------------------------------------------------------------------------------------------------
//  THE FOUR CONTEXT-MENU SITES THE AAP DID NOT NAME
// -----------------------------------------------------------------------------------------------------
//  AAP 0.2.1.3 Correction 5 names six of the ten context-menu sites - :L795, :L863, :L916, :L1009,
//  :L1018, :L1027 - and directs that the remaining four be located in the source. They are
//  :L1033, :L1039, :L1045 and :L1052, and the scan shows why they were easy to miss: :L1033, :L1039 and
//  :L1045 are three more arms of the SAME type-mismatch message that :L1018 and :L1027 raise, differing
//  only in which column type reached them, and :L1052 is a fourth change-refused site on the paste path
//  rather than on a check-box path. All four are in the census with their locators.
//
// -----------------------------------------------------------------------------------------------------
//  THE DECISIVE CONTRAST, AND WHY IT IS AVAILABLE AT ALL
// -----------------------------------------------------------------------------------------------------
//  A sweep that proved only "these messages differ from those messages" would prove nothing about
//  ROUTING - two sets of different strings can differ for a hundred reasons. The oracle happens to make a
//  far stronger test possible.
//
//  The dialog TITLE at `se_cst_dw.sru:L357` is `I18N(ne_cst_i18n.CAT_DWSVC, "错误")`.
//  The dialog TITLE at all twenty-eight `n_cst_dwsvc_columnexp.sru` sites is the bare literal `"错误"`.
//
//  THE SAME SOURCE TEXT, ONE ROUTED AND ONE NOT. Install a provider that would translate "错误" if it
//  were ever consulted, and the two answers separate: the validation-error title comes back translated,
//  the twenty-eight column-expression titles come back byte-identical to the source. No difference in the
//  strings themselves can explain that, so the only thing it can be measuring is whether the call site
//  consults localization. Phase 3 below is built on exactly that pair.
//
//  The empirical backing for the negative half: the count of `I18N(` occurrences in the entire
//  2,435-line `n_cst_dwsvc_columnexp.sru` is ZERO. `TheColumnExpressionOracleContainsNoLocalizationCall`
//  re-measures that on every run.
//
// -----------------------------------------------------------------------------------------------------
//  GOVERNING CONSTRAINTS
// -----------------------------------------------------------------------------------------------------
//  `review_rules` returns "No user rules provided.", so NO USER RULE GOVERNS THIS FILE and none is
//  invented. AAP 0.7.2's enterprise baseline applies in their place. The AAP 0.7.3 constraints that do
//  govern it:
//
//      C-B / G2    The localization inconsistency is PRESERVED, never harmonized. Harmonizing the
//                  twenty-eight hardcoded messages onto I18n would change observable output text, which
//                  is precisely the silent correction the mandate forbids. Every assertion about them
//                  below expects them UNCHANGED; there is deliberately not one assertion anywhere in this
//                  file that a column-expression message is translated.
//      C-D         No dialog, message-box or other UI type may appear in any error payload.
//                  `NoErrorPayloadCarriesAUiType` enforces that structurally over the payload types
//                  rather than by inspection.
//      C-F         No payload may carry a secret-shaped value.
//      C-K         Every census row cites its locator, and the locator travels IN THE TEST NAME through
//                  the theory's member data, so a CI log names the exact oracle line that failed.
//      Naming      SCREAMING_SNAKE constants are REFERENCED, never DECLARED. `.editorconfig` grants the
//                  CA1707/IDE1006 suppression only to the specific implementation files that carry such
//                  identifiers, and no `*.Tests` file is on that list - so this file declaring one would
//                  break the build under TreatWarningsAsErrors. That is a structural guarantee, not a
//                  convention. `Categories.CAT_DWSVC` is read from `Categories`, never spelled as a
//                  number.
//
// -----------------------------------------------------------------------------------------------------
//  IMPORT PROVENANCE - WHY THIS FILE REACHES THE NAMESPACES IT DOES
// -----------------------------------------------------------------------------------------------------
//  Every namespace below is either declared as a dependency of this file, or is transitively REQUIRED by
//  one that is - by that dependency's own public contract, not by convenience. Recorded explicitly so a
//  reviewer can audit the boundary rather than infer it:
//
//    Declared directly
//      PowerFramework.DataServices.Services        RowSelectService.cs, ContextMenuModel.cs
//      PowerFramework.DataServices.Domain          ValidationSession.cs
//      PowerFramework.DataServices.Expressions     ParseErrorFormatter.cs, ColumnExpressionEngine.cs
//      PowerFramework.Shared.Localization          I18n.cs, II18nProvider.cs, Categories.cs
//      PowerFramework.Shared.Kernel                RetCode.cs, Formatting.cs
//
//    Transitively required by a declared dependency's own signature
//      PowerFramework.DataServices.Configuration   RowSelectService and ContextMenuModel both DEMAND an
//                                                  IOptions<DataServicesOptions> to construct at all.
//      PowerFramework.Shared.Eventful              FakeDataWindowHost's constructor takes an EventBroker.
//      Shared.Kernel's Enums / Predicates          Categories.CAT_DWSVC is DEFINED as
//                                                  Enums.I18N_CAT_CUSTOM + 2, so the catalogue that
//                                                  anchors it is named by the dependency itself.
//
//    Required by the agent brief for THIS file
//      PowerFramework.DataServices.Grpc            The brief requires asserting that these payloads reach
//                                                  a caller "through its ONE SHARED ERROR MAPPING" and
//                                                  that each "projects onto whatever dataservices.v1.proto
//                                                  declares". That mapping is DataWindowWireProjection and
//                                                  ExpressionWireProjection. Hand-rolling a second
//                                                  projection here instead would have asserted THIS FILE's
//                                                  mapping rather than the shipped one - and a mapping that
//                                                  dropped every field would then still pass. The
//                                                  requirement is only satisfiable by calling the real one.
//      PowerFramework.Contracts.DataServices.V1    The contract the payloads are asserted ON, aliased
//                                                  Wire* per this suite's established convention.
//
//  Nothing here reaches across a SERVICE boundary: every namespace is this service's own or a shared
//  library this service already references, so the coupling AAP 0.4.3 permits - the published contracts
//  project - is the only cross-service coupling present.
//
// =====================================================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Expressions;
using PowerFramework.DataServices.Grpc;
using PowerFramework.DataServices.Services;
using PowerFramework.Shared.Eventful;
using PowerFramework.Shared.Kernel;
using PowerFramework.Shared.Localization;

using Xunit;

using WireExpressionError = PowerFramework.Contracts.DataServices.V1.ExpressionError;
using WireSeverity = PowerFramework.Contracts.DataServices.V1.Severity;
using WireStructuredError = PowerFramework.Contracts.DataServices.V1.StructuredError;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// The shape of the payload a census row is expected to produce, which is what decides WHICH fields
/// Phase 2 requires to be populated on it.
/// </summary>
/// <remarks>
/// These are payload SHAPES and not severities or categories: two sites with the same severity can have
/// different shapes, and the shape is what a consumer has to be able to read. They are ordered so the
/// localized shapes come first and the hardcoded ones last, which is the same order the census is written
/// in and the same order the split matrix in Phase 3 reads.
/// </remarks>
public enum DialogPayloadShape
{
    /// <summary>
    /// A row-scoped refusal: a `Sprintf` row template, a detail key, the row as data, `StopSign`, and the
    /// localization category. Raised by <c>n_cst_dwsvc_rowselect.sru</c>:L239 and by the four
    /// change-refused context-menu sites.
    /// </summary>
    LocalizedRowRejection = 1,

    /// <summary>
    /// The row-scoped refusal PLUS the offending value, which the oracle appends after a second line
    /// separator as <c>"!~n" + sVal</c>. Raised by the six value-rejecting context-menu sites.
    /// </summary>
    LocalizedRowDetailWithValue = 2,

    /// <summary>
    /// The validation-error dialog: a localized TITLE, a body that is either the column's own validation
    /// message or a localized fallback, and `StopSign`.
    /// [<c>se_cst_dw.sru</c>:L357, with its fallback composed at :L355.]
    /// </summary>
    LocalizedValidationDialog = 3,

    /// <summary>
    /// A hardcoded column-expression message with NO caret: a title, a body, `StopSign`, and NO
    /// localization category at all. Seventeen of the twenty-eight sites.
    /// </summary>
    HardcodedPlainMessage = 4,

    /// <summary>
    /// A hardcoded column-expression message WITH an expression and a caret position, built through the
    /// oracle's own parse-error builder. Eleven of the twenty-eight sites.
    /// </summary>
    HardcodedCaretParseError = 5,
}

/// <summary>
/// One census row: a live dialog site in the legacy DataWindow service layer, with everything needed to
/// adjudicate it against both the oracle and the implementation.
/// </summary>
/// <remarks>
/// <para>
/// This is the unit AAP 0.6.7 asks the census to be expressed in - table-driven member data, one row per
/// site - so that adding or losing a site fails a NAMED row rather than shifting a total.
/// </para>
/// <para>
/// It is a record rather than a tuple so each field is named at the point of use. The theory's member data
/// projects it down to primitives, because those are what appear in a test name.
/// </para>
/// </remarks>
/// <param name="LegacyFile">
/// The oracle object, as a repository-relative path with forward slashes. Written in full rather than as a
/// bare file name: two distinct files in this repository share the name <c>pfw.sra</c> (AAP 0.8.6 R7), so
/// a bare name is not a locator in this codebase even when it happens to be unambiguous.
/// </param>
/// <param name="LegacyLine">
/// The one-based line the dialog call sits on. This is the locator, and it is the value the scanner
/// checks: the line must hold a LIVE dialog call, not a commented one and not prose about one.
/// </param>
/// <param name="Localizes">
/// Whether the oracle routes this site's text through <c>I18N</c>. TRUE for the twelve rowselect,
/// contextmenu and validation-error sites; FALSE for all twenty-eight column-expression sites. This single
/// flag is the census's record of the preserved defect.
/// </param>
/// <param name="Shape">The payload shape Phase 2 requires this site to produce.</param>
/// <param name="CompanionLocalizationLine">
/// A line OTHER than <paramref name="LegacyLine"/> that participates in composing this site's text through
/// <c>I18N</c>, or <see langword="null"/> when the site composes entirely on its own line. Exactly one
/// census row uses it: <c>se_cst_dw.sru</c>:L357's fallback body is composed at :L355, which is "the
/// associated site in the :L347-L364 region" that Correction 5 pairs with the dialog. Carried as a field
/// so the pairing is a locator rather than a remark.
/// </param>
/// <param name="Description">
/// What the site reports, in English, for a reader of a CI log who does not read PowerScript. Never
/// asserted against - the oracle's own text is what gets asserted - so it can say what a locator cannot.
/// </param>
public sealed record DialogSite(
    string LegacyFile,
    int LegacyLine,
    bool Localizes,
    DialogPayloadShape Shape,
    int? CompanionLocalizationLine,
    string Description)
{
    /// <summary>The locator as it is written in documentation and in commit messages.</summary>
    /// <remarks>
    /// Composed rather than stored so it cannot drift from <see cref="LegacyFile"/> and
    /// <see cref="LegacyLine"/>. Uses the bare file name because that is the form the implementation's own
    /// <c>SourceLocator</c> literals use, and Phase 1 compares the two directly.
    /// </remarks>
    public string ShortLocator =>
        LegacyFile[(LegacyFile.LastIndexOf('/') + 1)..]
            + ":L"
            + LegacyLine.ToString(CultureInfo.InvariantCulture);

    /// <summary>Whether this row is one of the twenty-eight column-expression sites.</summary>
    public bool IsColumnExpressionSite =>
        Shape is DialogPayloadShape.HardcodedPlainMessage
            or DialogPayloadShape.HardcodedCaretParseError;
}

/// <summary>
/// The complete live-dialog census for the in-scope DataWindow service layer, WRITTEN OUT BY HAND.
/// </summary>
/// <remarks>
/// <para>
/// <b>THE CENSUS IS DELIBERATELY INDEPENDENT OF THE IMPLEMENTATION.</b> The twenty-eight column-expression
/// rows could have been generated from <see cref="ExpressionErrorCatalog.All"/> in two lines, and that
/// would have been worse than useless: a census derived from the thing it audits agrees with it by
/// construction and can never report a discrepancy. Correction 5's instruction is explicit - if the
/// implementation produces a site the census does not list, or omits one it does, that is a FINDING to
/// report, not a census to adjust. So the rows are enumerated here and then cross-checked in BOTH
/// directions against the catalogue, and separately against the oracle itself.
/// </para>
/// <para>
/// That makes the census a THREE-WAY agreement:
/// </para>
/// <list type="number">
/// <item><description>this hand-written list,</description></item>
/// <item><description>the read-only legacy source, re-scanned on every run, and</description></item>
/// <item><description>the shipped implementation's own declared site set.</description></item>
/// </list>
/// <para>
/// Any one of the three drifting from the other two fails a named row.
/// </para>
/// </remarks>
public static class DialogCensus
{
    /// <summary>The legacy library the whole in-scope DataWindow service layer is exported from.</summary>
    private const string LibraryPath = "ws_objects/pfw.datawindow.services.pbl.src/";

    /// <summary>
    /// <c>n_cst_dwsvc_rowselect.sru</c> - the row-selection service. One dialog.
    /// </summary>
    public const string RowSelectObject = LibraryPath + "n_cst_dwsvc_rowselect.sru";

    /// <summary>
    /// <c>n_cst_dwsvc_contextmenu.sru</c> - the context-menu service. Ten dialogs, the most of any
    /// localizing object in the layer.
    /// </summary>
    public const string ContextMenuObject = LibraryPath + "n_cst_dwsvc_contextmenu.sru";

    /// <summary>
    /// <c>se_cst_dw.sru</c> - the 22-event service extension. One live dialog, plus the two commented
    /// occurrences the header block describes.
    /// </summary>
    public const string ServiceExtensionObject = LibraryPath + "se_cst_dw.sru";

    /// <summary>
    /// <c>n_cst_dwsvc_columnexp.sru</c> - the 2,435-line column-expression engine. Twenty-eight dialogs,
    /// none of them localized.
    /// </summary>
    /// <remarks>
    /// Deliberately the same string <see cref="ExpressionErrorCatalog.OracleSourcePath"/> holds, and
    /// <c>TheCensusNamesTheSameOracleObjectTheCatalogueNames</c> asserts the two agree. Two independent
    /// spellings of one path is exactly the kind of drift a rename introduces silently.
    /// </remarks>
    public const string ColumnExpressionObject = LibraryPath + "n_cst_dwsvc_columnexp.sru";

    /// <summary>
    /// <c>n_cst_dwsvc.sru</c> - the DataWindow service base. ZERO dialogs, and in the census so that
    /// acquiring one fails a row.
    /// </summary>
    public const string ServiceBaseObject = LibraryPath + "n_cst_dwsvc.sru";

    /// <summary>
    /// Every in-scope object the sweep covers, including the one with no dialogs at all.
    /// </summary>
    public static ImmutableArray<string> Objects { get; } =
    [
        RowSelectObject,
        ContextMenuObject,
        ServiceExtensionObject,
        ColumnExpressionObject,
        ServiceBaseObject,
    ];

    /// <summary>
    /// All forty live dialog sites, in oracle order within each object and in the order the objects are
    /// listed in <see cref="Objects"/>.
    /// </summary>
    public static ImmutableArray<DialogSite> All { get; } = Build();

    /// <summary>
    /// The live-dialog count this census claims for each object, INCLUDING the zero.
    /// </summary>
    /// <remarks>
    /// Derived from <see cref="All"/> rather than written twice, so the per-object counts and the rows can
    /// never disagree with each other. The independence that matters is from the IMPLEMENTATION and from
    /// the ORACLE, and both of those are still audited.
    /// </remarks>
    public static ImmutableDictionary<string, int> LiveCountByObject { get; } =
        Objects.ToImmutableDictionary(
            static path => path,
            static path => All.Count(site =>
                string.Equals(site.LegacyFile, path, StringComparison.Ordinal)),
            StringComparer.Ordinal);

    /// <summary>
    /// The commented-dialog count this census claims for each object.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written out by hand because commented sites are NOT census rows - they produce no payload - yet
    /// their count is load-bearing in two places. In <c>se_cst_dw.sru</c> it pins the dormant byte-length
    /// path at :L286 as dormant, which is what stops a future reader reviving it. In
    /// <c>n_cst_dwsvc_columnexp.sru</c> the value ZERO is the AAP's own characterisation of the object, and
    /// it is what rules out the reading that some of the twenty-eight were already disabled upstream.
    /// </para>
    /// <para>
    /// Both occurrences in <c>se_cst_dw.sru</c> are counted, because the scanner cannot tell a
    /// block-commented CALL from a comment that merely NAMES the function, and pretending it can would be a
    /// weaker assertion dressed as a stronger one. :L286 is the commented call; :L368 is the prose.
    /// </para>
    /// </remarks>
    public static ImmutableDictionary<string, int> CommentedCountByObject { get; } =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [RowSelectObject] = 0,
            [ContextMenuObject] = 0,
            [ServiceExtensionObject] = 2,
            [ColumnExpressionObject] = 0,
            [ServiceBaseObject] = 0,
        }.ToImmutableDictionary(StringComparer.Ordinal);

    /// <summary>
    /// The census projected to primitives, one row per site, for <c>[MemberData]</c>.
    /// </summary>
    /// <returns>The object path, the line, whether it localizes, and the payload shape.</returns>
    /// <remarks>
    /// Primitives and an enum rather than <see cref="DialogSite"/> itself for two reasons that point the
    /// same way. The framework renders these into the TEST NAME, so a CI log names the failing locator
    /// outright - which is what C-K asks of a census. And they are natively serialisable, so no row can
    /// become undiscoverable through a data-serialisation diagnostic. The full row is recovered inside the
    /// body through <see cref="Find"/>.
    /// </remarks>
    public static TheoryData<string, int, bool, DialogPayloadShape> Rows()
    {
        TheoryData<string, int, bool, DialogPayloadShape> rows = [];

        foreach (DialogSite site in All)
        {
            rows.Add(site.LegacyFile, site.LegacyLine, site.Localizes, site.Shape);
        }

        return rows;
    }

    /// <summary>
    /// The census projected to one row per in-scope object, for the per-object count theory.
    /// </summary>
    /// <returns>The object path, its live count, and its commented count.</returns>
    public static TheoryData<string, int, int> ObjectCounts()
    {
        TheoryData<string, int, int> rows = [];

        foreach (string path in Objects)
        {
            rows.Add(path, LiveCountByObject[path], CommentedCountByObject[path]);
        }

        return rows;
    }

    /// <summary>Recovers the full census row for a locator.</summary>
    /// <param name="legacyFile">The object path exactly as the census spells it.</param>
    /// <param name="legacyLine">The one-based line.</param>
    /// <returns>The row.</returns>
    /// <exception cref="InvalidOperationException">
    /// When no such row exists, which can only happen if a theory's member data and
    /// <see cref="All"/> have been allowed to diverge.
    /// </exception>
    public static DialogSite Find(string legacyFile, int legacyLine) =>
        All.SingleOrDefault(site =>
            string.Equals(site.LegacyFile, legacyFile, StringComparison.Ordinal)
            && site.LegacyLine == legacyLine)
        ?? throw new InvalidOperationException(
            "No census row for " + legacyFile + ":L"
                + legacyLine.ToString(CultureInfo.InvariantCulture) + ".");

    /// <summary>Builds the forty rows.</summary>
    /// <returns>The census.</returns>
    private static ImmutableArray<DialogSite> Build()
    {
        ImmutableArray<DialogSite>.Builder census = ImmutableArray.CreateBuilder<DialogSite>(40);

        // ==========================================================================================
        //  n_cst_dwsvc_rowselect.sru - ONE SITE, LOCALIZED THROUGH TWO SEPARATE LOOKUPS
        // ==========================================================================================
        //  :L239 composes `Sprintf(I18N(CAT_DWSVC,"第{}行"), nRow) + "~n" + I18N(CAT_DWSVC,"修改数据被拒绝")
        //  + "!"`. TWO independent category lookups on one line, with `Sprintf` applied to the
        //  TRANSLATION of the first rather than to its key - so a provider whose translation moves the
        //  placeholder still has it filled in the right place. That ordering is the reason the payload
        //  carries the template and the key as data instead of only the rendered string.
        // ==========================================================================================
        census.Add(new DialogSite(
            RowSelectObject,
            239,
            Localizes: true,
            DialogPayloadShape.LocalizedRowRejection,
            CompanionLocalizationLine: null,
            "Range propagation refused by the item-change handler."));

        // ==========================================================================================
        //  n_cst_dwsvc_contextmenu.sru - TEN SITES
        // ==========================================================================================
        //  Four change-refused, six value-rejecting. The four the AAP did not name are :L1033, :L1039,
        //  :L1045 (three further type-mismatch arms) and :L1052 (the paste path's refusal).
        //
        //  All ten localize, and all ten carry `StopSign`. Their shapes differ only in whether the
        //  offending value is appended, which is why the census distinguishes exactly those two shapes
        //  and no more.
        // ==========================================================================================
        census.Add(new DialogSite(
            ContextMenuObject,
            795,
            Localizes: true,
            DialogPayloadShape.LocalizedRowRejection,
            CompanionLocalizationLine: null,
            "Check-column write refused by the item-change handler."));
        census.Add(new DialogSite(
            ContextMenuObject,
            863,
            Localizes: true,
            DialogPayloadShape.LocalizedRowRejection,
            CompanionLocalizationLine: null,
            "Revert-check-column write refused by the item-change handler."));
        census.Add(new DialogSite(
            ContextMenuObject,
            916,
            Localizes: true,
            DialogPayloadShape.LocalizedRowRejection,
            CompanionLocalizationLine: null,
            "Uncheck-column write refused by the item-change handler."));
        census.Add(new DialogSite(
            ContextMenuObject,
            1009,
            Localizes: true,
            DialogPayloadShape.LocalizedRowDetailWithValue,
            CompanionLocalizationLine: null,
            "Paste rejected: the value is not in the column's code table."));
        census.Add(new DialogSite(
            ContextMenuObject,
            1018,
            Localizes: true,
            DialogPayloadShape.LocalizedRowDetailWithValue,
            CompanionLocalizationLine: null,
            "Paste rejected: non-numeric text into an integer column."));
        census.Add(new DialogSite(
            ContextMenuObject,
            1027,
            Localizes: true,
            DialogPayloadShape.LocalizedRowDetailWithValue,
            CompanionLocalizationLine: null,
            "Paste rejected: non-numeric text into a decimal column."));
        census.Add(new DialogSite(
            ContextMenuObject,
            1033,
            Localizes: true,
            DialogPayloadShape.LocalizedRowDetailWithValue,
            CompanionLocalizationLine: null,
            "Paste rejected: unparseable text into a datetime column. NOT NAMED IN THE AAP."));
        census.Add(new DialogSite(
            ContextMenuObject,
            1039,
            Localizes: true,
            DialogPayloadShape.LocalizedRowDetailWithValue,
            CompanionLocalizationLine: null,
            "Paste rejected: non-date text into a date column. NOT NAMED IN THE AAP."));
        census.Add(new DialogSite(
            ContextMenuObject,
            1045,
            Localizes: true,
            DialogPayloadShape.LocalizedRowDetailWithValue,
            CompanionLocalizationLine: null,
            "Paste rejected: non-time text into a time column. NOT NAMED IN THE AAP."));
        census.Add(new DialogSite(
            ContextMenuObject,
            1052,
            Localizes: true,
            DialogPayloadShape.LocalizedRowRejection,
            CompanionLocalizationLine: null,
            "Paste write refused by the item-change handler. NOT NAMED IN THE AAP."));

        // ==========================================================================================
        //  se_cst_dw.sru - ONE LIVE SITE, WITH ITS FALLBACK COMPOSED ON A DIFFERENT LINE
        // ==========================================================================================
        //  :L357 is `MessageBox(I18N(CAT_DWSVC,"错误"), sErrMsg, StopSign!)` - a THREE-argument dialog,
        //  so unlike every other site in the layer it has a TITLE distinct from its body. The body is
        //  either the column's own validation message, stripped of its outer two characters, or - when
        //  that message is empty or the single placeholder "?" - the localized fallback composed at
        //  :L355 as `I18N(CAT_DWSVC,"输入了无效的值") + "!"`.
        //
        //  :L355 is the "associated site in the :L347-L364 region" Correction 5 pairs with the dialog,
        //  and it is recorded as this row's companion line rather than as a row of its own, because it
        //  raises no dialog: it composes a string the dialog then displays.
        // ==========================================================================================
        census.Add(new DialogSite(
            ServiceExtensionObject,
            357,
            Localizes: true,
            DialogPayloadShape.LocalizedValidationDialog,
            CompanionLocalizationLine: 355,
            "Item validation failed; the column's validation message or the localized fallback."));

        // ==========================================================================================
        //  n_cst_dwsvc_columnexp.sru - TWENTY-EIGHT SITES, NONE LOCALIZED
        // ==========================================================================================
        //  Every row below has `Localizes: false`, and that is the whole point of the file. The oracle
        //  makes ZERO `I18N(` calls in this object, so its twenty-eight messages are emitted as the
        //  hardcoded Chinese the author typed - in deliberate contrast with the twelve rows above, which
        //  route identical-looking text through CAT_DWSVC.
        //
        //  ELEVEN are caret-bearing, reported through the oracle's own `_of_BuildParseError(syntax, pos,
        //  errinfo)` builder and therefore carrying an expression and a position. SEVENTEEN are plain
        //  messages with no position at all - and that absence is a real distinction rather than a
        //  missing value, because a plain message has no caret to place.
        // ==========================================================================================
        AddColumnExpressionSite(census, 713, DialogPayloadShape.HardcodedPlainMessage,
            "Compute-object creation for the expression cache failed.");
        AddColumnExpressionSite(census, 739, DialogPayloadShape.HardcodedPlainMessage,
            "Expression cache error carrying a CAUGHT RUNTIME EXCEPTION's text.");
        AddColumnExpressionSite(census, 762, DialogPayloadShape.HardcodedPlainMessage,
            "General expression error.");
        AddColumnExpressionSite(census, 1308, DialogPayloadShape.HardcodedCaretParseError,
            "Function macro references the context, which static expansion cannot carry.");
        AddColumnExpressionSite(census, 1383, DialogPayloadShape.HardcodedCaretParseError,
            "Function macro argument list is invalid.");
        AddColumnExpressionSite(census, 1390, DialogPayloadShape.HardcodedCaretParseError,
            "Function macro argument count wrong, reported by FULL name.");
        AddColumnExpressionSite(census, 1395, DialogPayloadShape.HardcodedCaretParseError,
            "Function macro argument count wrong, reported by SHORT name.");
        AddColumnExpressionSite(census, 1399, DialogPayloadShape.HardcodedCaretParseError,
            "Function macro undefined.");
        AddColumnExpressionSite(census, 1417, DialogPayloadShape.HardcodedCaretParseError,
            "Context variable does not support static expansion.");
        AddColumnExpressionSite(census, 1422, DialogPayloadShape.HardcodedCaretParseError,
            "Variable macro undefined.");
        AddColumnExpressionSite(census, 1426, DialogPayloadShape.HardcodedCaretParseError,
            "Foreign variable does not support static expansion.");
        AddColumnExpressionSite(census, 1431, DialogPayloadShape.HardcodedCaretParseError,
            "Variable macro value invalid.");
        AddColumnExpressionSite(census, 1486, DialogPayloadShape.HardcodedCaretParseError,
            "Variable macro name invalid.");
        AddColumnExpressionSite(census, 1505, DialogPayloadShape.HardcodedCaretParseError,
            "Variable macro quote never closed.");
        AddColumnExpressionSite(census, 1607, DialogPayloadShape.HardcodedPlainMessage,
            "Duplicate variable definition.");
        AddColumnExpressionSite(census, 1671, DialogPayloadShape.HardcodedPlainMessage,
            "Expression cache update failed.");
        AddColumnExpressionSite(census, 1882, DialogPayloadShape.HardcodedPlainMessage,
            "Cache creation refused because macro expansion is unsupported.");
        AddColumnExpressionSite(census, 1888, DialogPayloadShape.HardcodedPlainMessage,
            "Cache creation failed while enabling the cache.");
        AddColumnExpressionSite(census, 2122, DialogPayloadShape.HardcodedPlainMessage,
            "Duplicate FOREIGN variable definition - same text as :L1607, different site.");
        AddColumnExpressionSite(census, 2133, DialogPayloadShape.HardcodedPlainMessage,
            "Foreign variable undefined.");
        AddColumnExpressionSite(census, 2192, DialogPayloadShape.HardcodedPlainMessage,
            "Pre-processing: variable undefined.");
        AddColumnExpressionSite(census, 2208, DialogPayloadShape.HardcodedPlainMessage,
            "Pre-processing: variable value invalid.");
        AddColumnExpressionSite(census, 2232, DialogPayloadShape.HardcodedPlainMessage,
            "Pre-processing: function argument evaluation failed.");
        AddColumnExpressionSite(census, 2242, DialogPayloadShape.HardcodedPlainMessage,
            "Pre-processing: macro variable undefined.");
        AddColumnExpressionSite(census, 2254, DialogPayloadShape.HardcodedPlainMessage,
            "Pre-processing: macro variable value invalid.");
        AddColumnExpressionSite(census, 2282, DialogPayloadShape.HardcodedPlainMessage,
            "Pre-processing: macro return value invalid.");
        AddColumnExpressionSite(census, 2306, DialogPayloadShape.HardcodedPlainMessage,
            "Pre-processing: function return value invalid.");
        AddColumnExpressionSite(census, 2392, DialogPayloadShape.HardcodedPlainMessage,
            "Variable expression error.");

        // n_cst_dwsvc.sru contributes NO rows. Its zero is asserted through the per-object count theory,
        // which is the only place a zero can be asserted at all.
        return census.ToImmutable();
    }

    /// <summary>Adds one non-localized column-expression row.</summary>
    /// <param name="census">The builder.</param>
    /// <param name="line">The oracle line, which is also the site's identity.</param>
    /// <param name="shape">Plain message or caret-bearing parse error.</param>
    /// <param name="description">What the site reports.</param>
    /// <remarks>
    /// A helper only because <c>Localizes: false</c> and the object path are invariant across all
    /// twenty-eight - which is itself the assertion the file exists to make. The LINE is still written out
    /// per row, so no row is generated from a range and losing one is still a named failure.
    /// </remarks>
    private static void AddColumnExpressionSite(
        ImmutableArray<DialogSite>.Builder census,
        int line,
        DialogPayloadShape shape,
        string description) =>
        census.Add(new DialogSite(
            ColumnExpressionObject,
            line,
            Localizes: false,
            shape,
            CompanionLocalizationLine: null,
            description));
}

/// <summary>
/// The result of scanning one legacy object for dialog call sites.
/// </summary>
/// <param name="LiveLines">
/// The one-based lines carrying a dialog call in EXECUTABLE position, ascending.
/// </param>
/// <param name="CommentedLines">
/// The one-based lines where the token appears inside a comment - whether a commented-out call or prose
/// that merely names the function, which are lexically indistinguishable.
/// </param>
/// <param name="LocalizationCallLines">
/// The one-based lines carrying an <c>I18N(</c> call in executable position. This is what measures the
/// localization split against the oracle rather than against the port.
/// </param>
public sealed record LegacyDialogScan(
    ImmutableArray<int> LiveLines,
    ImmutableArray<int> CommentedLines,
    ImmutableArray<int> LocalizationCallLines);

/// <summary>
/// A PowerScript-aware scanner that classifies <c>MessageBox</c> / <c>MessageBoxEx</c> and <c>I18N(</c>
/// occurrences in a legacy object as live or commented.
/// </summary>
/// <remarks>
/// <para>
/// <b>WHY THIS EXISTS RATHER THAN A HARDCODED EXPECTATION.</b> Correction 5's closing instruction is to
/// cross-check the census against the source by counting live, non-commented occurrences per file. Doing
/// that once by hand and writing the answer down leaves the answer to rot. Doing it here means the count is
/// re-derived from the read-only oracle on every test run, so the census cannot silently stop describing
/// the thing it claims to describe.
/// </para>
/// <para>
/// <b>WHY LINE-PREFIX MATCHING IS NOT ENOUGH.</b> Three lexical states have to be tracked, and skipping any
/// one of them changes the answer on this corpus:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>//</c> line comments - the ordinary case, and the only one a prefix test catches.
/// </description></item>
/// <item><description>
/// <c>/* */</c> BLOCK comments - the case that decides <c>se_cst_dw.sru</c>:L286. That line has no comment
/// marker on it whatsoever; it is commented because a block opened six lines earlier. Miss this and the
/// census reports 41 live sites and the dormant byte-length path looks live.
/// </description></item>
/// <item><description>
/// Double-quoted string LITERALS - so a comment marker inside a string cannot open a phantom comment. The
/// column-expression messages are full of <c>"Error:"</c>-style fragments and PowerScript escapes, and one
/// stray <c>/</c> pair inside a literal would silently blank the rest of a 2,435-line file.
/// </description></item>
/// </list>
/// <para>
/// PowerScript's string escape is the doubled quote rather than a backslash, and its block comments do not
/// nest. Both are handled below the way the language defines them, not the way C# would.
/// </para>
/// <para>
/// The scanner READS the legacy tree and never writes to it. AAP 0.2.2.1 makes <c>ws_objects/**</c> the
/// behavioural oracle and forbids editing, moving or reformatting it; reading is exactly what an oracle is
/// for.
/// </para>
/// </remarks>
public static class LegacyDialogScanner
{
    /// <summary>
    /// The marker that identifies the repository root, matching the anchor every other locator in this
    /// suite walks to.
    /// </summary>
    private const string RepositoryRootMarker = "PowerFramework.slnx";

    /// <summary>The dialog functions the legacy layer raises. Both arities, both spellings.</summary>
    /// <remarks>
    /// <c>MessageBoxEx</c> is matched FIRST and the longer token wins, so the two are counted once each
    /// rather than <c>MessageBoxEx</c> being counted as a <c>MessageBox</c> plus a stray suffix.
    /// PowerScript is case-insensitive, so the comparison is too - and the corpus does in fact spell them
    /// consistently, which the scanner does not rely on.
    /// </remarks>
    private static readonly string[] DialogTokens = ["MessageBoxEx", "MessageBox"];

    /// <summary>The localization entry point, with its opening parenthesis to exclude bare mentions.</summary>
    private const string LocalizationToken = "I18N(";

    /// <summary>Scans one legacy object.</summary>
    /// <param name="repositoryRelativePath">The object path, with forward slashes.</param>
    /// <returns>The classified occurrences.</returns>
    public static LegacyDialogScan Scan(string repositoryRelativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRelativePath);

        string absolute = Resolve(repositoryRelativePath);

        Assert.True(
            File.Exists(absolute),
            "The read-only oracle object " + repositoryRelativePath + " is not present at " + absolute
                + ". AAP 0.2.2.1 makes ws_objects/** the behavioural oracle for this layer, so the census "
                + "cannot be adjudicated without it.");

        // ReadAllLines rather than a stream: these objects are at most 2,435 lines, the whole point is to
        // report ONE-BASED LINE NUMBERS, and the file must be read the way the export encodes it. The
        // exports carry a UTF-8 BOM, which the default encoding detection strips - leaving it in would put
        // a zero-width character in front of the first line and change nothing about the counts but
        // everything about a text comparison.
        string[] lines = File.ReadAllLines(absolute);

        ImmutableArray<int>.Builder live = ImmutableArray.CreateBuilder<int>();
        ImmutableArray<int>.Builder commented = ImmutableArray.CreateBuilder<int>();
        ImmutableArray<int>.Builder localization = ImmutableArray.CreateBuilder<int>();

        // THE ONE PIECE OF STATE THAT SPANS LINES, and the reason :L286 classifies correctly. A block
        // comment opened on any earlier line is still open here.
        bool inBlockComment = false;

        for (int index = 0; index < lines.Length; index++)
        {
            ClassifyLine(
                lines[index],
                index + 1,
                ref inBlockComment,
                live,
                commented,
                localization);
        }

        return new LegacyDialogScan(
            live.ToImmutable(),
            commented.ToImmutable(),
            localization.ToImmutable());
    }

    /// <summary>Classifies every token occurrence on one line.</summary>
    /// <param name="line">The raw line.</param>
    /// <param name="lineNumber">Its one-based number.</param>
    /// <param name="inBlockComment">
    /// Whether a block comment is open on entry; updated to whether one is open on exit.
    /// </param>
    /// <param name="live">Collects lines with a live dialog call.</param>
    /// <param name="commented">Collects lines with a commented dialog token.</param>
    /// <param name="localization">Collects lines with a live localization call.</param>
    /// <remarks>
    /// A line is recorded AT MOST ONCE in each collection, because the census is a census of SITES and this
    /// corpus puts one call per line. Recording a line twice would inflate a count without naming a new
    /// site, which is the opposite of what the census is for.
    /// </remarks>
    private static void ClassifyLine(
        string line,
        int lineNumber,
        ref bool inBlockComment,
        ImmutableArray<int>.Builder live,
        ImmutableArray<int>.Builder commented,
        ImmutableArray<int>.Builder localization)
    {
        bool sawLiveDialog = false;
        bool sawCommentedDialog = false;
        bool sawLiveLocalization = false;

        bool inStringLiteral = false;
        int position = 0;

        while (position < line.Length)
        {
            if (inBlockComment)
            {
                // Inside a block, only the closing delimiter is meaningful. A dialog token here is a
                // commented occurrence - which is precisely how se_cst_dw.sru:L286 is reached.
                if (MatchesDialogToken(line, position) is { } blockToken)
                {
                    sawCommentedDialog = true;
                    position += blockToken;
                    continue;
                }

                if (line.AsSpan(position).StartsWith("*/", StringComparison.Ordinal))
                {
                    inBlockComment = false;
                    position += 2;
                    continue;
                }

                position++;
                continue;
            }

            if (inStringLiteral)
            {
                if (line[position] == '"')
                {
                    // PowerScript escapes a quote by DOUBLING it, so a pair re-opens the literal rather
                    // than closing and reopening it. Consuming both characters here is what keeps an
                    // embedded quote from terminating the literal early.
                    if (position + 1 < line.Length && line[position + 1] == '"')
                    {
                        position += 2;
                        continue;
                    }

                    inStringLiteral = false;
                }

                position++;
                continue;
            }

            if (line[position] == '"')
            {
                inStringLiteral = true;
                position++;
                continue;
            }

            // The line comment wins over everything remaining on the line, so any dialog token after it is
            // a commented occurrence - the se_cst_dw.sru:L368 prose case.
            if (line.AsSpan(position).StartsWith("//", StringComparison.Ordinal))
            {
                if (ContainsDialogToken(line, position))
                {
                    sawCommentedDialog = true;
                }

                break;
            }

            if (line.AsSpan(position).StartsWith("/*", StringComparison.Ordinal))
            {
                inBlockComment = true;
                position += 2;
                continue;
            }

            if (MatchesDialogToken(line, position) is { } token)
            {
                sawLiveDialog = true;
                position += token;
                continue;
            }

            if (line.AsSpan(position).StartsWith(LocalizationToken, StringComparison.OrdinalIgnoreCase))
            {
                sawLiveLocalization = true;
                position += LocalizationToken.Length;
                continue;
            }

            position++;
        }

        if (sawLiveDialog)
        {
            live.Add(lineNumber);
        }

        // A line can hold BOTH - a live call and a trailing comment about it - so this is an independent
        // test rather than an else-branch. Nothing in this corpus does, and the scanner does not assume it.
        if (sawCommentedDialog)
        {
            commented.Add(lineNumber);
        }

        if (sawLiveLocalization)
        {
            localization.Add(lineNumber);
        }
    }

    /// <summary>Matches a dialog token at a position, longest first.</summary>
    /// <param name="line">The line.</param>
    /// <param name="position">Where to test.</param>
    /// <returns>The matched token's length, or <see langword="null"/>.</returns>
    private static int? MatchesDialogToken(string line, int position)
    {
        foreach (string token in DialogTokens)
        {
            if (line.AsSpan(position).StartsWith(token, StringComparison.OrdinalIgnoreCase))
            {
                return token.Length;
            }
        }

        return null;
    }

    /// <summary>Whether any dialog token appears from a position to the end of the line.</summary>
    /// <param name="line">The line.</param>
    /// <param name="position">Where to start.</param>
    /// <returns>Whether one appears.</returns>
    private static bool ContainsDialogToken(string line, int position)
    {
        foreach (string token in DialogTokens)
        {
            if (line.IndexOf(token, position, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Resolves a repository-relative oracle path to an absolute one.</summary>
    /// <param name="repositoryRelativePath">The path, with forward slashes.</param>
    /// <returns>The absolute path.</returns>
    private static string Resolve(string repositoryRelativePath)
    {
        string? root = RepositoryRoot();

        Assert.NotNull(root);

        return Path.Combine(
            root,
            repositoryRelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>Walks up from the test binary to the repository root.</summary>
    /// <returns>The root, or <see langword="null"/> when the marker is not found.</returns>
    /// <remarks>
    /// Starts at the embedded anchor the build publishes so the walk still works when the test output sits
    /// outside the checkout, then verifies the marker exactly as every other locator in this suite does.
    /// See <c>TestRepositoryRoot</c>.
    /// </remarks>
    private static string? RepositoryRoot()
    {
        DirectoryInfo? candidate = new(TestRepositoryRoot.SearchStart);

        while (candidate is not null)
        {
            if (File.Exists(Path.Combine(candidate.FullName, RepositoryRootMarker)))
            {
                return candidate.FullName;
            }

            candidate = candidate.Parent;
        }

        return null;
    }
}

/// <summary>
/// The cross-cutting sweep: the dialog census is complete, every converted payload is complete, and the
/// legacy localization split is preserved rather than harmonized.
/// </summary>
public sealed class StructuredErrorParityTests
{
    /// <summary>The census's own claim about how many live dialog sites the layer has.</summary>
    /// <remarks>
    /// Written as a literal rather than as <c>DialogCensus.All.Length</c> on purpose. Comparing the
    /// collection's length to itself is a tautology; comparing it to a number a human wrote down is the
    /// assertion that the census still says what it was reviewed as saying. If a row is added or removed
    /// deliberately, BOTH have to change, and that is the point.
    /// </remarks>
    private const int ExpectedTotalLiveSites = 40;

    /// <summary>How many of the twenty-eight column-expression sites are caret-bearing.</summary>
    private const int ExpectedCaretBearingSites = 11;

    /// <summary>How many of the twenty-eight column-expression sites are plain messages.</summary>
    private const int ExpectedPlainMessageSites = 17;

    /// <summary>How many sites route their text through localization.</summary>
    /// <remarks>
    /// One rowselect, ten contextmenu, one validation-error. The complement is the twenty-eight the AAP
    /// requires be left un-localized.
    /// </remarks>
    private const int ExpectedLocalizingSites = 12;

    // ==============================================================================================
    //  PHASE 1 - THE CENSUS
    // ==============================================================================================

    /// <summary>
    /// Every census row names a line that really does carry a LIVE dialog call in the oracle, and whose
    /// localization behaviour is the one the row claims.
    /// </summary>
    /// <param name="legacyFile">The oracle object.</param>
    /// <param name="legacyLine">The line, which is the locator.</param>
    /// <param name="localizes">Whether the census claims this site routes through <c>I18N</c>.</param>
    /// <param name="shape">The payload shape the census claims.</param>
    /// <remarks>
    /// <para>
    /// THE SINGLE CENSUS THEORY AAP 0.6.7 ASKS FOR. Forty named rows, each adjudicated directly against the
    /// read-only oracle, so losing a site or inventing one fails a row whose NAME is the locator.
    /// </para>
    /// <para>
    /// The localization half is what makes this more than an existence check. For a localizing row the
    /// oracle line must ALSO carry a live <c>I18N(</c> call, or its companion line must; for a
    /// non-localizing row the line must carry NONE. That is measured on the legacy source, so it is
    /// independent of anything the port does - the port could be wrong in both directions and this row would
    /// still report what the oracle actually does.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(DialogCensus.Rows), MemberType = typeof(DialogCensus))]
    public void EveryCensusRowNamesALiveDialogSiteInTheOracle(
        string legacyFile,
        int legacyLine,
        bool localizes,
        DialogPayloadShape shape)
    {
        DialogSite site = DialogCensus.Find(legacyFile, legacyLine);

        // The projected row and the full row must be the same row. Guards against member data and the
        // census drifting apart, which would let a row silently assert something about a different site.
        Assert.Equal(localizes, site.Localizes);
        Assert.Equal(shape, site.Shape);

        LegacyDialogScan scan = LegacyDialogScanner.Scan(legacyFile);

        Assert.Contains(legacyLine, scan.LiveLines);

        Assert.DoesNotContain(legacyLine, scan.CommentedLines);

        // THE LOCALIZATION SPLIT, MEASURED ON THE ORACLE.
        if (localizes)
        {
            bool onItsOwnLine = scan.LocalizationCallLines.Contains(legacyLine);
            bool onItsCompanionLine =
                site.CompanionLocalizationLine is { } companion
                && scan.LocalizationCallLines.Contains(companion);

            Assert.True(
                onItsOwnLine || onItsCompanionLine,
                site.ShortLocator + " is claimed to localize, but the oracle makes no I18N call on that "
                    + "line or on its companion line. Either the census row is wrong or the oracle changed.");
        }
        else
        {
            Assert.DoesNotContain(legacyLine, scan.LocalizationCallLines);
        }
    }

    /// <summary>
    /// Each in-scope object's live and commented dialog counts are exactly what the census claims -
    /// including the two objects whose claim is a ZERO.
    /// </summary>
    /// <param name="legacyFile">The oracle object.</param>
    /// <param name="expectedLive">The live count the census claims.</param>
    /// <param name="expectedCommented">The commented count the census claims.</param>
    /// <remarks>
    /// <para>
    /// The per-object half of Correction 5's "assert the total live count matches the census and that the
    /// counts per file match". A grand total alone cannot detect a site MOVING between objects, and a
    /// per-row check alone cannot detect a site being ADDED - only a count can, because a row that does not
    /// exist has no name to fail under. This theory is the one that turns an unlisted new dialog into a
    /// failure.
    /// </para>
    /// <para>
    /// The commented counts are asserted for the reason the header block gives: <c>se_cst_dw.sru</c>'s TWO
    /// pin the dormant byte-length path at :L286 as dormant, and
    /// <c>n_cst_dwsvc_columnexp.sru</c>'s ZERO is the AAP's own characterisation - it is what rules out the
    /// reading that some of the twenty-eight were disabled upstream and need no port.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(DialogCensus.ObjectCounts), MemberType = typeof(DialogCensus))]
    public void EachObjectsLiveAndCommentedDialogCountsMatchTheCensus(
        string legacyFile,
        int expectedLive,
        int expectedCommented)
    {
        LegacyDialogScan scan = LegacyDialogScanner.Scan(legacyFile);

        Assert.Equal(expectedLive, scan.LiveLines.Length);
        Assert.Equal(expectedCommented, scan.CommentedLines.Length);

        // Every live line the ORACLE reports must be a census row. This is the direction that catches
        // GROWTH: a dialog added to the layer appears here with no row to name it.
        ImmutableArray<int> censusLines =
            [.. DialogCensus.All
                .Where(site => string.Equals(site.LegacyFile, legacyFile, StringComparison.Ordinal))
                .Select(static site => site.LegacyLine)
                .Order()];

        Assert.Equal(censusLines, scan.LiveLines);
    }

    /// <summary>
    /// The whole layer has exactly forty live dialog sites, and the census has exactly forty rows.
    /// </summary>
    [Fact]
    public void TheLayerHasExactlyFortyLiveDialogSites()
    {
        Assert.Equal(ExpectedTotalLiveSites, DialogCensus.All.Length);

        int scanned = DialogCensus.Objects.Sum(path => LegacyDialogScanner.Scan(path).LiveLines.Length);

        Assert.Equal(ExpectedTotalLiveSites, scanned);

        // No locator may appear twice. A duplicated row would inflate the total while describing one site,
        // which would let a genuinely lost site hide behind a copy of its neighbour.
        Assert.Equal(
            ExpectedTotalLiveSites,
            DialogCensus.All.Select(static site => site.ShortLocator)
                .Distinct(StringComparer.Ordinal)
                .Count());
    }

    /// <summary>
    /// Twelve sites localize and twenty-eight do not, and the twenty-eight split eleven caret-bearing to
    /// seventeen plain.
    /// </summary>
    /// <remarks>
    /// The arithmetic of the preserved defect, stated as a total so that "harmonizing" any single site -
    /// which is the change C-B forbids - moves a number here as well as failing a row in Phase 3.
    /// </remarks>
    [Fact]
    public void TheCensusSplitsTwelveLocalizedAgainstTwentyEightHardcoded()
    {
        Assert.Equal(ExpectedLocalizingSites, DialogCensus.All.Count(static site => site.Localizes));

        ImmutableArray<DialogSite> hardcoded =
            [.. DialogCensus.All.Where(static site => !site.Localizes)];

        Assert.Equal(ExpectedCaretBearingSites + ExpectedPlainMessageSites, hardcoded.Length);

        // EVERY non-localizing site is in the column-expression engine, and every column-expression site is
        // non-localizing. The defect is a property of the OBJECT, not a scattering of individual lapses.
        Assert.All(
            hardcoded,
            static site => Assert.Equal(DialogCensus.ColumnExpressionObject, site.LegacyFile));
        Assert.All(
            DialogCensus.All.Where(static site => site.IsColumnExpressionSite),
            static site => Assert.False(site.Localizes));

        Assert.Equal(
            ExpectedCaretBearingSites,
            hardcoded.Count(static site =>
                site.Shape == DialogPayloadShape.HardcodedCaretParseError));
        Assert.Equal(
            ExpectedPlainMessageSites,
            hardcoded.Count(static site => site.Shape == DialogPayloadShape.HardcodedPlainMessage));
    }

    /// <summary>
    /// The column-expression oracle makes NOT ONE localization call in its entire 2,435 lines.
    /// </summary>
    /// <remarks>
    /// The empirical foundation of the whole file, and the strongest single statement of the preserved
    /// defect: the twenty-eight messages do not merely happen to be untranslated, the object contains no
    /// localization call at all, so there is no code path by which any of its text could be translated.
    /// Measured on the oracle rather than asserted about the port.
    /// </remarks>
    [Fact]
    public void TheColumnExpressionOracleContainsNoLocalizationCall()
    {
        LegacyDialogScan scan = LegacyDialogScanner.Scan(DialogCensus.ColumnExpressionObject);

        Assert.Empty(scan.LocalizationCallLines);
    }

    /// <summary>
    /// The implementation's own site catalogue is exactly the twenty-eight the census lists - in both
    /// directions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The third leg of the three-way agreement. The census has already been checked against the oracle; this
    /// checks it against the SHIPPED PORT, and it is the check Correction 5 describes as producing a finding:
    /// a site the implementation reports that the census does not list, or a census row the implementation
    /// cannot report, is a discrepancy in one of the two and not something to paper over.
    /// </para>
    /// <para>
    /// It is adjudicable at all because <c>ExpressionErrorSite</c> encodes each site's ORACLE LINE NUMBER as
    /// its enum value, so <c>descriptor.LegacyLine</c> and a census locator are the same kind of thing and
    /// can be compared without a lookup table that could itself be wrong.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheShippedCatalogueDeclaresExactlyTheCensusedColumnExpressionSites()
    {
        ImmutableArray<int> censused =
            [.. DialogCensus.All
                .Where(static site => site.IsColumnExpressionSite)
                .Select(static site => site.LegacyLine)
                .Order()];

        ImmutableArray<int> declared =
            [.. ExpressionErrorCatalog.All.Select(static descriptor => descriptor.LegacyLine).Order()];

        Assert.Equal(censused, declared);

        // The family split has to agree too, because the family decides which factory a site is reported
        // through and therefore whether its payload carries a caret at all.
        foreach (DialogSite site in DialogCensus.All.Where(static site => site.IsColumnExpressionSite))
        {
            ExpressionErrorSiteDescriptor descriptor =
                ExpressionErrorCatalog.Get((ExpressionErrorSite)site.LegacyLine);

            ExpressionErrorFamily expected = site.Shape == DialogPayloadShape.HardcodedCaretParseError
                ? ExpressionErrorFamily.CaretBearingParseError
                : ExpressionErrorFamily.PlainMessage;

            Assert.Equal(expected, descriptor.Family);
        }
    }

    /// <summary>
    /// The census and the shipped catalogue name the same oracle object by the same path.
    /// </summary>
    /// <remarks>
    /// Two independent spellings of one path is exactly the drift a directory rename introduces silently:
    /// both would still compile, both would still read as documentation, and only one would still resolve.
    /// </remarks>
    [Fact]
    public void TheCensusNamesTheSameOracleObjectTheCatalogueNames() =>
        Assert.Equal(
            DialogCensus.ColumnExpressionObject,
            ExpressionErrorCatalog.OracleSourcePath,
            StringComparer.Ordinal);

    /// <summary>
    /// The ten context-menu locators the implementation emits are exactly the ten the census lists,
    /// including the four the AAP did not name.
    /// </summary>
    /// <remarks>
    /// <c>ContextMenuError</c> carries its oracle locator as a field, which makes the context-menu half of
    /// the census adjudicable against the port in the same way the column-expression half is. The four rows
    /// this pins that Correction 5 left to be located are :L1033, :L1039, :L1045 and :L1052.
    /// </remarks>
    [Fact]
    public void TheContextMenuLocatorsTheCensusListsAreTheOnesTheImplementationEmits()
    {
        ImmutableArray<string> censused =
            [.. DialogCensus.All
                .Where(static site =>
                    string.Equals(
                        site.LegacyFile,
                        DialogCensus.ContextMenuObject,
                        StringComparison.Ordinal))
                .Select(static site => site.ShortLocator)
                .Order(StringComparer.Ordinal)];

        ImmutableArray<string> emitted =
            [.. ContextMenuErrorProbe.EmitEveryLocator().Order(StringComparer.Ordinal)];

        Assert.Equal(censused, emitted);
    }

    /// <summary>
    /// No error payload type carries a dialog, a message box, a window handle or any other user-interface
    /// type: every member is plain data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C-D, enforced STRUCTURALLY rather than by inspection. Correction 5's whole claim is that only the
    /// DELIVERY CHANNEL changed - so a payload that had acquired a dialog handle, an icon resource, a parent
    /// window or a callback would have re-imported the presentation dependency the conversion exists to
    /// remove, and would do it without changing a single message.
    /// </para>
    /// <para>
    /// Expressed as an allow-list of data shapes rather than a deny-list of forbidden names, because a
    /// deny-list can only reject the UI types someone thought to name. Note what the allow-list PERMITS: the
    /// severity enums, including the ones spelled <c>...MessageIcon</c>. The legacy's <c>StopSign!</c> is an
    /// icon CONSTANT, and it survives as a severity VALUE - a number naming how bad the condition is - which
    /// is data. What would violate C-D is a member typed as something that can draw.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoErrorPayloadCarriesAUiType()
    {
        Type[] payloads =
        [
            typeof(RowSelectRejectionError),
            typeof(ContextMenuError),
            typeof(ValidationStructuredError),
            typeof(ExpressionParseError),
        ];

        foreach (Type payload in payloads)
        {
            ImmutableArray<PropertyInfo> members =
                [.. payload
                    .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(static member =>
                        !member.IsDefined(typeof(CompilerGeneratedAttribute), inherit: true))];

            // A payload with no members would pass the loop below vacuously, which would turn this
            // structural guarantee into a no-op the moment a reflection filter was tightened too far.
            Assert.NotEmpty(members);

            foreach (PropertyInfo member in members)
            {
                Assert.True(
                    IsPlainData(member.PropertyType),
                    payload.Name + "." + member.Name + " is typed as " + member.PropertyType.Name
                        + ", which is not plain data. AAP 0.2.1.3 Correction 5 converts every dialog to a "
                        + "structured error in which ONLY THE DELIVERY CHANNEL changed; C-D forbids a "
                        + "dialog, message-box or other UI type appearing in a payload.");
            }
        }
    }

    /// <summary>Whether a payload member's type is plain, transportable data.</summary>
    /// <param name="type">The member type.</param>
    /// <returns>Whether it is plain data.</returns>
    /// <remarks>
    /// Enums qualify because they are numbers with names - which is what the legacy's icon and severity
    /// constants are. <c>ImmutableArray&lt;string&gt;</c> qualifies as the substitution-argument carrier.
    /// Nothing else does: no interface, no delegate, no class beyond the string.
    /// </remarks>
    private static bool IsPlainData(Type type)
    {
        Type unwrapped = Nullable.GetUnderlyingType(type) ?? type;

        if (unwrapped.IsEnum || unwrapped.IsPrimitive)
        {
            return true;
        }

        if (unwrapped == typeof(string) || unwrapped == typeof(decimal))
        {
            return true;
        }

        return unwrapped.IsGenericType
            && unwrapped.GetGenericTypeDefinition() == typeof(ImmutableArray<>)
            && IsPlainData(unwrapped.GetGenericArguments()[0]);
    }

    // ==============================================================================================
    //  PHASE 2 - PAYLOAD COMPLETENESS
    // ==============================================================================================

    /// <summary>
    /// Every census site produces a payload that is complete ON THE CONTRACT: the text, the severity, the
    /// localization category where the legacy used one, the substitution arguments as ARGUMENTS, and - for a
    /// parse failure - the expression and a caret position.
    /// </summary>
    /// <param name="legacyFile">The oracle object.</param>
    /// <param name="legacyLine">The locator.</param>
    /// <param name="localizes">Whether this site routes through <c>I18N</c>.</param>
    /// <param name="shape">The payload shape.</param>
    /// <remarks>
    /// <para>
    /// The forty rows again, this time asserting the CONVERSION rather than the census. Every payload is
    /// projected through the shipped wire mapping before being asserted on, so "the payload is complete" and
    /// "the payload is serialisable over the contract" are the same assertion rather than two - and a mapping
    /// that dropped a field fails here rather than in production.
    /// </para>
    /// <para>
    /// Each site's own per-field expectations belong to the file that owns it. What this asserts is the
    /// property NO owning file can: that the invariant holds across all forty, so a site cannot be converted
    /// into a payload that carries less than its neighbours.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(DialogCensus.Rows), MemberType = typeof(DialogCensus))]
    public void EveryCensusSiteProducesACompletePayloadOnTheContract(
        string legacyFile,
        int legacyLine,
        bool localizes,
        DialogPayloadShape shape)
    {
        DialogSite site = DialogCensus.Find(legacyFile, legacyLine);
        ProducedPayload payload = PayloadProbe.Produce(site);
        WireStructuredError structured = payload.Structured;

        // THE TEXT. Never empty, never trimmed to nothing, and carrying the oracle's own characters.
        Assert.NotEqual(string.Empty, structured.Text);

        // THE SEVERITY. Every one of the forty legacy sites passes StopSign!, and every payload therefore
        // carries the StopSign severity. The icon survives as a severity VALUE, which is what lets the
        // dialog disappear without the urgency disappearing with it.
        Assert.Equal(WireSeverity.StopSign, structured.Severity);

        // THE LOCALIZED FLAG, which is the census's claim about this site restated on the wire so a CONSUMER
        // can tell translatable text from untranslatable text without any message changing.
        Assert.Equal(localizes, structured.Localized);

        // THE LOCALIZATION CATEGORY - present where the legacy used one, and ABSENT where it did not.
        if (localizes)
        {
            // Read from Categories, never spelled as a number: the naming constraint forbids declaring a
            // SCREAMING_SNAKE identifier here, and a literal 7 would silently outlive a change to
            // Enums.I18N_CAT_CUSTOM.
            Assert.Equal(Categories.CAT_DWSVC, structured.Category);
        }
        else
        {
            // NOT MERELY AN UNRESOLVED CATEGORY - no category at all. A payload carrying CAT_DWSVC with
            // Localized false would still be telling a consumer which table to look in, and the oracle
            // names no table for these twenty-eight because it never looks one up.
            Assert.Equal(0L, structured.Category);
        }

        // THE SUBSTITUTION ARGUMENTS, PRESERVED AS ARGUMENTS. The oracle pre-substitutes them into a string
        // it then throws away; carrying them alongside the rendered text is what lets a consumer re-render,
        // re-order or re-translate without re-parsing a message.
        Assert.All(structured.FormatArgs, static argument => Assert.NotNull(argument));

        switch (shape)
        {
            case DialogPayloadShape.LocalizedRowRejection:
            case DialogPayloadShape.LocalizedRowDetailWithValue:
                // The row template, the detail key and the row itself - so the row travels as DATA and not
                // only baked into the rendered text.
                Assert.True(
                    structured.FormatArgs.Count >= 3,
                    site.ShortLocator + " carries " + structured.FormatArgs.Count
                        + " substitution arguments; the row-scoped shapes carry at least the template, the "
                        + "detail key and the row.");
                Assert.Null(payload.Expression);
                break;

            case DialogPayloadShape.LocalizedValidationDialog:
                // The only site in the layer with a title distinct from its body, because it is the only
                // three-argument dialog call.
                Assert.NotEqual(string.Empty, structured.Title);
                Assert.Null(payload.Expression);
                break;

            case DialogPayloadShape.HardcodedPlainMessage:
                Assert.NotNull(payload.Expression);

                // A plain message HAS NO CARET, and that absence is a real distinction rather than a missing
                // value: there is no position in it to point at.
                Assert.Equal(0L, payload.Expression.CaretPosition);
                Assert.Equal(string.Empty, payload.Expression.Expression);
                break;

            case DialogPayloadShape.HardcodedCaretParseError:
                Assert.NotNull(payload.Expression);

                // BOTH fields present and non-default. The caret ARITHMETIC belongs to
                // ParseErrorFormatterTests; what matters here is that neither field is lost in conversion,
                // because a caret with no expression cannot be rendered and an expression with no caret
                // cannot be pointed at.
                Assert.NotEqual(string.Empty, payload.Expression.Expression);
                Assert.True(
                    payload.Expression.CaretPosition > 0L,
                    site.ShortLocator + " is a caret-bearing parse error but its payload carries caret "
                        + "position " + payload.Expression.CaretPosition.ToString(CultureInfo.InvariantCulture)
                        + ", which is not a position.");

                // The oracle's fully rendered marker travels too, for byte-exact characterization against
                // the behavioural oracle.
                Assert.NotEqual(string.Empty, payload.Expression.RenderedMarker);
                break;

            default:
                Assert.Fail("Unhandled payload shape " + shape + " for " + site.ShortLocator + ".");
                break;
        }
    }

    /// <summary>
    /// The twelve localizing sites compose their text byte-for-byte from the oracle's own source strings
    /// when no provider is installed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The byte-exactness half of Correction 5, on the half of the census where a rendered text can be
    /// predicted in full. Every comparison is ORDINAL, so the Chinese characters, the punctuation, the
    /// embedded newline and the trailing <c>"!"</c> are all compared as themselves rather than under a
    /// culture's collation - where, for instance, a full-width and a half-width exclamation mark can compare
    /// equal.
    /// </para>
    /// <para>
    /// The keys are read from the implementation's own constants rather than retyped, so this asserts the
    /// COMPOSITION - Sprintf over the template, the separator, the detail, the suffix - rather than
    /// re-asserting the strings that <c>RowSelectServiceTests</c> and <c>ContextMenuModelTests</c> already
    /// pin character by character.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheLocalizingSitesComposeTheOracleTextExactlyWhenNoProviderIsInstalled()
    {
        string expectedRejection =
            Formatting.Sprintf(RowSelectService.RowNumberMessageKey, 3L)
                + RowSelectService.LineSeparator
                + RowSelectService.RejectionMessageKey
                + RowSelectService.RejectionMessageSuffix;

        ProducedPayload rowSelect = PayloadProbe.Produce(
            DialogCensus.Find(DialogCensus.RowSelectObject, 239));

        Assert.Equal(expectedRejection, rowSelect.Structured.Text, StringComparer.Ordinal);

        // The context menu's four change-refused sites compose the SAME text as row-select's single site,
        // from the same two keys - which is why the census distinguishes them by locator and not by text.
        string expectedMenuRejection =
            Formatting.Sprintf(ContextMenuModel.RowNumberMessageKey, 1L)
                + ContextMenuModel.LineSeparator
                + ContextMenuModel.ChangeRejectedMessageKey
                + ContextMenuModel.DetailSuffix;

        ProducedPayload contextMenu = PayloadProbe.Produce(
            DialogCensus.Find(DialogCensus.ContextMenuObject, 795));

        Assert.Equal(expectedMenuRejection, contextMenu.Structured.Text, StringComparer.Ordinal);

        // The value-rejecting shape appends the offending value after a SECOND separator, which the oracle
        // writes as the single literal "!~n" followed by the value.
        ProducedPayload invalidValue = PayloadProbe.Produce(
            DialogCensus.Find(DialogCensus.ContextMenuObject, 1009));

        Assert.Equal(
            Formatting.Sprintf(ContextMenuModel.RowNumberMessageKey, 1L)
                + ContextMenuModel.LineSeparator
                + ContextMenuModel.InvalidValueMessageKey
                + ContextMenuModel.DetailSuffix
                + ContextMenuModel.LineSeparator
                + "Z",
            invalidValue.Structured.Text,
            StringComparer.Ordinal);

        // The validation-error fallback, and its title, both from the oracle's own source strings.
        ProducedPayload validation = PayloadProbe.Produce(
            DialogCensus.Find(DialogCensus.ServiceExtensionObject, 357));

        Assert.Equal(
            LegacyMessageKeys.ValidationErrorFallbackText
                + LegacyMessageKeys.ValidationErrorFallbackSuffix,
            validation.Structured.Text,
            StringComparer.Ordinal);
        Assert.Equal(
            LegacyMessageKeys.ValidationErrorTitle,
            validation.Structured.Title,
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Every column-expression payload carries the oracle's hardcoded Chinese title and a body rendered from
    /// the oracle's own template, byte for byte.
    /// </summary>
    /// <remarks>
    /// The byte-exactness half on the other twenty-eight. The body is compared against
    /// <c>Formatting.Sprintf</c> over the descriptor's own template, which is the same computation the
    /// factory performs - so what this actually pins is that the CONVERSION and the WIRE PROJECTION together
    /// change not one character between the rendered message and the payload a caller receives.
    /// </remarks>
    [Fact]
    public void EveryColumnExpressionPayloadCarriesTheHardcodedTitleAndRenderedBody()
    {
        foreach (DialogSite site in DialogCensus.All.Where(static site => site.IsColumnExpressionSite))
        {
            ProducedPayload payload = PayloadProbe.Produce(site);

            Assert.Equal(
                ExpressionErrorCatalog.LegacyTitle,
                payload.Structured.Title,
                StringComparer.Ordinal);

            ExpressionErrorSiteDescriptor descriptor =
                ExpressionErrorCatalog.Get((ExpressionErrorSite)site.LegacyLine);

            // The rendered message, from the descriptor's own template and the arguments the payload
            // carries. This is what the oracle would have put in the dialog body.
            string message =
                Formatting.Sprintf(descriptor.FormatTemplate, [.. payload.Structured.FormatArgs]);

            if (descriptor.Family == ExpressionErrorFamily.CaretBearingParseError)
            {
                // A CARET-BEARING SITE'S TEXT IS THE MESSAGE WRAPPED BY THE ORACLE'S OWN BUILDER - message,
                // blank line, expression, underscore run, caret, position - not the bare message. Asserting
                // the bare message here would have been wrong in a way that LOOKED right, because the bare
                // message is a prefix of the wrapped one. Comparing against the builder is what pins the
                // wrapping instead of merely tolerating it.
                Assert.Equal(
                    ParseErrorFormatter.BuildParseError(
                        PayloadProbe.ProbeExpression,
                        PayloadProbe.ProbeCaretPosition,
                        message),
                    payload.Structured.Text,
                    StringComparer.Ordinal);

                // And the two-argument form of the same builder is the marker carried separately, so a
                // consumer can compare byte-exactly without re-deriving it from a character count.
                Assert.Equal(
                    ParseErrorFormatter.BuildParseError(
                        PayloadProbe.ProbeExpression,
                        PayloadProbe.ProbeCaretPosition),
                    payload.Expression?.RenderedMarker,
                    StringComparer.Ordinal);
            }
            else
            {
                Assert.Equal(message, payload.Structured.Text, StringComparer.Ordinal);
            }

            // The arity travels intact: a template needing three arguments must arrive with three, or a
            // consumer re-rendering it produces a message the oracle never emitted.
            Assert.Equal(descriptor.ArgumentCount, payload.Structured.FormatArgs.Count);
        }
    }

    /// <summary>
    /// The substitution arguments are exercised in BOTH of the oracle's placeholder forms - the sequential
    /// empty brace and the one-based index.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Correction 5 requires both forms be exercised, and the two halves of the census happen to use one
    /// each: the row-scoped messages use the SEQUENTIAL form, <c>"第{}行"</c>
    /// [<c>n_cst_dwsvc_rowselect.sru</c>:L239], and every one of the twenty-eight column-expression templates
    /// uses the INDEXED form, <c>{1}</c>, <c>{2}</c>, <c>{3}</c>. The context menu uses the indexed form too,
    /// in the find-expression templates at <c>n_cst_dwsvc_contextmenu.sru</c>:L1183-L1188.
    /// </para>
    /// <para>
    /// The distinction is not cosmetic and this is why it has to be tested: the indexed form can substitute
    /// ONE argument at SEVERAL positions, which the sequential form cannot express at all, and the index is
    /// ONE-based - so a hand-off to a zero-based formatter is off by one on every indexed placeholder in the
    /// corpus while still producing a plausible-looking string.
    /// </para>
    /// </remarks>
    [Fact]
    public void BothSprintfPlaceholderFormsAreExercisedAcrossTheCensus()
    {
        // THE SEQUENTIAL FORM. The template travels as an argument, so the assertion is on the template
        // itself and not on a copy of it.
        ProducedPayload rowSelect = PayloadProbe.Produce(
            DialogCensus.Find(DialogCensus.RowSelectObject, 239));

        Assert.Contains(RowSelectService.RowNumberMessageKey, rowSelect.Structured.FormatArgs);
        Assert.Contains("{}", RowSelectService.RowNumberMessageKey, StringComparison.Ordinal);
        Assert.DoesNotContain("{1}", RowSelectService.RowNumberMessageKey, StringComparison.Ordinal);

        // Substituting through it puts the row where the template says, not where a positional guess would.
        Assert.Equal(
            "第7行",
            Formatting.Sprintf(RowSelectService.RowNumberMessageKey, 7L),
            StringComparer.Ordinal);

        // THE INDEXED FORM, on a column-expression template that takes three arguments.
        ExpressionErrorSiteDescriptor indexed = ExpressionErrorCatalog.All
            .First(static descriptor => descriptor.ArgumentCount >= 3);

        Assert.Contains("{1}", indexed.FormatTemplate, StringComparison.Ordinal);
        Assert.Contains("{2}", indexed.FormatTemplate, StringComparison.Ordinal);

        // ONE-BASED: the first argument answers to {1}. A zero-based formatter would put it at {0}, which
        // appears nowhere in this corpus, and would leave every {1} resolving to the SECOND argument.
        Assert.Equal("first", Formatting.Sprintf("{1}", "first", "second"), StringComparer.Ordinal);
        Assert.Equal("second", Formatting.Sprintf("{2}", "first", "second"), StringComparer.Ordinal);

        // And one argument can reach several positions - the property that makes the indexed form
        // irreplaceable, and the reason the context menu's find-expression templates use it.
        Assert.Equal("a|a|a", Formatting.Sprintf("{1}|{1}|{1}", "a"), StringComparer.Ordinal);
    }

    // ==============================================================================================
    //  PHASE 3 - THE PRESERVED LOCALIZATION INCONSISTENCY
    // ==============================================================================================
    //
    //  THIS IS THE DECISIVE SECTION OF THE FILE, AND IT IS DELIBERATELY ONE MATRIX.
    //
    //  AAP 0.2.1.3 Correction 5 and 0.6.2.5 record that the twenty-eight column-expression messages are
    //  hardcoded Chinese which do NOT route through localization, unlike the dwsvc, rowselect and
    //  contextmenu messages, WHICH DO - and that this inconsistency is LEGACY BEHAVIOUR TO BE REPRODUCED,
    //  not a defect to be repaired. Harmonizing the twenty-eight onto I18n would change observable output
    //  text, and changing observable output text is exactly the silent correction C-B forbids.
    //
    //  So both halves are asserted in ONE theory over all forty rows, and the assertion is a single
    //  comparison in two directions:
    //
    //      a localizing site      ->  installing a translating provider MUST change its text
    //      a non-localizing site  ->  installing the same provider MUST NOT change its text
    //
    //  There is deliberately NOT ONE assertion anywhere in this file that a column-expression message is
    //  translated. If one is ever added, it is the mandate being broken, not a gap being filled.
    // ==============================================================================================

    /// <summary>
    /// A provider that would translate every source string the layer uses, and the facade it is installed
    /// in.
    /// </summary>
    /// <returns>The facade and the provider, so the provider's call log can be inspected.</returns>
    /// <remarks>
    /// <para>
    /// The double is taught a MARKER for every key rather than a plausible translation, because a marker is
    /// unmistakable: a marked string cannot be confused with the source, and an unmarked one cannot be
    /// confused with a translation. Plausible translations would leave every assertion below arguing about
    /// wording.
    /// </para>
    /// <para>
    /// <c>TeachMarkers</c> covers the four keys the shared table lists. The context menu's two detail keys
    /// are not in that table - it is shared with the validation-error and row-select paths - so they are
    /// taught here, and their absence would otherwise let six sites pass the split test by not being
    /// translatable rather than by not being consulted. That distinction is the whole measurement.
    /// </para>
    /// </remarks>
    private static (I18n Facade, ScriptedI18nProvider Provider) TranslatingLocalization()
    {
        ScriptedI18nProvider provider = new ScriptedI18nProvider().TeachMarkers();

        _ = provider.Teach(
            ContextMenuModel.InvalidValueMessageKey,
            LegacyMessageKeys.MarkerFor(ContextMenuModel.InvalidValueMessageKey));
        _ = provider.Teach(
            ContextMenuModel.TypeMismatchMessageKey,
            LegacyMessageKeys.MarkerFor(ContextMenuModel.TypeMismatchMessageKey));

        return (ScriptedLocalization.With(provider), provider);
    }

    /// <summary>
    /// Installing a provider that would translate everything changes the twelve localizing sites' text and
    /// leaves all twenty-eight column-expression sites byte-identical.
    /// </summary>
    /// <param name="legacyFile">The oracle object.</param>
    /// <param name="legacyLine">The locator.</param>
    /// <param name="localizes">Whether the census claims this site routes through <c>I18N</c>.</param>
    /// <param name="shape">The payload shape.</param>
    /// <remarks>
    /// <para>
    /// The preserved inconsistency, as a single readable matrix. Each row is produced TWICE from the same
    /// arrangement - once with no provider and once with a provider that would translate every string the
    /// layer uses - and the only thing asserted is whether the second differs from the first.
    /// </para>
    /// <para>
    /// That framing is what makes it a test of ROUTING rather than of wording. It does not care what any
    /// translation says, only whether one happened; and because both readings come from the same site, the
    /// difference cannot be attributed to anything but whether the call site consults localization.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(DialogCensus.Rows), MemberType = typeof(DialogCensus))]
    public void TheLocalizationSplitIsPreservedAcrossTheWholeCensus(
        string legacyFile,
        int legacyLine,
        bool localizes,
        DialogPayloadShape shape)
    {
        DialogSite site = DialogCensus.Find(legacyFile, legacyLine);

        // The projected row really is this row, and its shape agrees. A theory that silently looked up a
        // DIFFERENT site than the one its name advertises would report a pass against the wrong locator.
        Assert.Equal(shape, site.Shape);

        // THE SHAPE AND THE SPLIT ARE NOT INDEPENDENT, and pinning the correspondence here is what stops a
        // future row being added with a hardcoded shape and a localizing flag - which would be the
        // harmonization C-B forbids, expressed as census data rather than as code.
        Assert.Equal(!site.IsColumnExpressionSite, localizes);

        ProducedPayload untranslated = PayloadProbe.Produce(site);

        (I18n facade, ScriptedI18nProvider provider) = TranslatingLocalization();
        ProducedPayload translated = PayloadProbe.Produce(site, facade);

        if (localizes)
        {
            Assert.NotEqual(
                untranslated.Structured.Text,
                translated.Structured.Text,
                StringComparer.Ordinal);

            // The provider was actually CONSULTED, and consulted under the category the census claims. A
            // site whose text changed for some other reason would pass the comparison above.
            Assert.NotEmpty(provider.Requests);
            Assert.Contains(
                provider.Requests,
                request => request.Category == Categories.CAT_DWSVC);

            Assert.Equal(Categories.CAT_DWSVC, translated.Structured.Category);
            Assert.True(translated.Structured.Localized);
        }
        else
        {
            // BYTE-IDENTICAL. The engine had a fully-armed provider available and did not reach for it,
            // because the oracle it reproduces makes no localization call at all.
            Assert.Equal(
                untranslated.Structured.Text,
                translated.Structured.Text,
                StringComparer.Ordinal);
            Assert.Equal(
                untranslated.Structured.Title,
                translated.Structured.Title,
                StringComparer.Ordinal);

            // NOT CONSULTED AT ALL - not consulted and unresolved, which would leave a request recorded.
            Assert.Empty(provider.Requests);

            // NO CATEGORY AT ALL, rather than an unresolved one. A payload carrying CAT_DWSVC with
            // Localized false would still be telling a consumer which table to look in, and the oracle
            // names no table for these because it never looks one up.
            Assert.Equal(0L, translated.Structured.Category);
            Assert.False(translated.Structured.Localized);
        }
    }

    /// <summary>
    /// The same source string - the dialog title <c>"错误"</c> - is translated at the validation-error site
    /// and left untouched at all twenty-eight column-expression sites.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE SHARPEST STATEMENT OF THE PRESERVED DEFECT AVAILABLE ANYWHERE IN THE ESTATE, and the reason it
    /// is worth its own test. <c>se_cst_dw.sru</c>:L357 wraps its title in
    /// <c>I18N(ne_cst_i18n.CAT_DWSVC, ...)</c>; every one of the twenty-eight column-expression sites passes
    /// the identical literal bare. One provider, one key, one taught translation - and two different
    /// answers.
    /// </para>
    /// <para>
    /// No difference between the strings can account for that, because there is no difference between the
    /// strings. The only variable is whether the call site consults localization, which is precisely the
    /// legacy behaviour C-B requires be preserved rather than harmonized.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSameTitleTextIsTranslatedInOneObjectAndLeftAloneInTheOther()
    {
        // The two titles ARE the same string in the oracle. If this ever stops holding, the contrast below
        // stops being a test of routing and becomes a test of wording, so it is asserted first.
        Assert.Equal(
            LegacyMessageKeys.ValidationErrorTitle,
            ExpressionErrorCatalog.LegacyTitle,
            StringComparer.Ordinal);

        (I18n facade, _) = TranslatingLocalization();
        string marker = LegacyMessageKeys.MarkerFor(LegacyMessageKeys.ValidationErrorTitle);

        // ROUTED: the validation-error dialog's title comes back marked.
        ProducedPayload validation = PayloadProbe.Produce(
            DialogCensus.Find(DialogCensus.ServiceExtensionObject, 357),
            facade);

        Assert.Equal(marker, validation.Structured.Title, StringComparer.Ordinal);

        // NOT ROUTED: all twenty-eight column-expression titles come back as the source literal, with the
        // very same provider installed and the very same key taught.
        foreach (DialogSite site in DialogCensus.All.Where(static site => site.IsColumnExpressionSite))
        {
            ProducedPayload payload = PayloadProbe.Produce(site, facade);

            Assert.Equal(
                ExpressionErrorCatalog.LegacyTitle,
                payload.Structured.Title,
                StringComparer.Ordinal);
            Assert.NotEqual(marker, payload.Structured.Title);
        }
    }

    /// <summary>
    /// The column-expression production path has no way to reach a localization provider at all.
    /// </summary>
    /// <remarks>
    /// The structural half of the preserved defect. The two factories the engine reports through take no
    /// provider and have no means of obtaining one, so the twenty-eight messages are not merely untranslated
    /// in practice - they are untranslatable by construction, exactly as the oracle object is. Expressed as
    /// a call log rather than as a comment: a facade is armed, all twenty-eight payloads are produced, and
    /// the provider records nothing.
    /// </remarks>
    [Fact]
    public void TheColumnExpressionPathNeverConsultsAProviderEvenWhenOneIsArmed()
    {
        (I18n facade, ScriptedI18nProvider provider) = TranslatingLocalization();

        foreach (DialogSite site in DialogCensus.All.Where(static site => site.IsColumnExpressionSite))
        {
            _ = PayloadProbe.Produce(site, facade);
        }

        Assert.Empty(provider.Requests);
    }

    // ==============================================================================================
    //  PHASE 4 - THE SILENT-PASSTHROUGH FALLBACK
    // ==============================================================================================

    /// <summary>
    /// With no provider installed, all three <c>I18n</c> overloads return the source text UNCHANGED -
    /// without throwing, without marking it, and without rebuilding it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The oracle's whole body is <c>if IsValid(n_cst_i18n) then ...OnTranslate(...)</c> followed by
    /// <c>return text</c> [<c>ws_objects/pfw.ui.pbl.src/i18n.srf</c>:L17-L18, :L21-L22]. There is no else
    /// branch, no diagnostic and no sentinel: the fallback is SILENT BY DESIGN. That matters here because
    /// every one of the twelve localizing census sites composes its text through this facade, so a fallback
    /// that threw would turn an untranslated message into a crash, and one that marked would change
    /// observable output on every deployment with no provider installed.
    /// </para>
    /// <para>
    /// <c>Assert.Same</c> rather than <c>Assert.Equal</c> on the reference is deliberate and is the strongest
    /// available statement of "unchanged": the very instance handed in comes back. Nothing was concatenated,
    /// wrapped, normalised, interned into a new string, or annotated. An equal-but-different instance would
    /// pass an equality assertion while proving something weaker.
    /// </para>
    /// </remarks>
    [Fact]
    public void WithNoProviderAllThreeOverloadsReturnTheSourceTextUnchanged()
    {
        I18n facade = ScriptedLocalization.WithoutProvider();

        // A fresh instance, so reference equality cannot succeed by way of string interning.
        string source = new string("输入了无效的值".ToCharArray());

        // OVERLOAD 2 - the category-only translator.
        Assert.Same(source, facade.I18N(Categories.CAT_DWSVC, source));

        // OVERLOAD 3 - the explicit-source translator.
        Assert.Same(source, facade.I18N(Enums.I18N_SRC_PFW, Categories.CAT_DWSVC, source));
        Assert.Same(source, facade.I18N(Enums.I18N_SRC_CUSTOM, Categories.CAT_MSGBOX, source));

        // OVERLOAD 1 - the installer. It is the third overload and it is reached with the invalid input the
        // oracle guards for, which has a defined RESULT rather than an exception: an invalid provider is
        // rejected by return code and nothing is installed.
        Assert.Equal(RetCode.E_INVALID_OBJECT, facade.I18N(null));

        // A rejected install does not arm the facade, so the passthrough still holds afterwards.
        Assert.Same(source, facade.I18N(Categories.CAT_DWSVC, source));

        // Null is carried through as null. AAP 0.4.5.4 forbids collapsing a legacy null, and collapsing this
        // one to an empty string would make an absent message indistinguishable from a blank one.
        Assert.Null(facade.I18N(Categories.CAT_DWSVC, null));
    }

    /// <summary>
    /// A key the installed provider does not know passes through unchanged, rather than becoming empty or a
    /// placeholder.
    /// </summary>
    /// <remarks>
    /// The other half of the silent fallback, and the half that bites in production: a provider IS installed,
    /// so the guard passes and the provider genuinely runs - it simply answers "not mine". The oracle returns
    /// <c>text</c> unconditionally on the line after the call, so a provider's refusal is indistinguishable
    /// from no provider at all. A facade that substituted a placeholder for an unknown key would corrupt
    /// every message any provider had not been taught.
    /// </remarks>
    [Fact]
    public void AnUnknownKeyPassesThroughUnchangedEvenWithAProviderInstalled()
    {
        (I18n facade, ScriptedI18nProvider provider) = TranslatingLocalization();

        string unknown = new string("这个键没有被教过".ToCharArray());

        Assert.Same(unknown, facade.I18N(Categories.CAT_DWSVC, unknown));

        // The provider really was asked - so this is a refusal being honoured, not a lookup being skipped.
        Assert.Contains(unknown, provider.RequestedKeys);

        // A taught key on the same facade DOES change, which is what rules out the reading that the facade
        // is simply inert.
        Assert.Equal(
            LegacyMessageKeys.MarkerFor(LegacyMessageKeys.ValidationErrorTitle),
            facade.I18N(Categories.CAT_DWSVC, LegacyMessageKeys.ValidationErrorTitle),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// A provider that writes nothing leaves the source text exactly as it found it.
    /// </summary>
    /// <remarks>
    /// The provider-side obligation behind the passthrough. <c>OnTranslate</c> delivers its answer by
    /// MUTATING the text, so "not handled" has to mean "did not write" - and a provider that returned zero
    /// after assigning would corrupt the text while claiming not to have touched it. Asserted against the
    /// double that answers zero and deliberately does not even self-assign.
    /// </remarks>
    [Fact]
    public void AProviderThatWritesNothingLeavesTheSourceTextIntact()
    {
        PassthroughI18nProvider provider = new();
        I18n facade = ScriptedLocalization.With(provider);

        string source = new string("修改数据被拒绝".ToCharArray());

        Assert.Same(source, facade.I18N(Categories.CAT_DWSVC, source));
        Assert.Single(provider.Requests);
        Assert.Equal(source, provider.Requests[0].Text, StringComparer.Ordinal);
    }

    /// <summary>
    /// The provider contract is a SINGLE member that mutates a <c>ref string</c> and returns a handled flag -
    /// not a member that returns the translation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The oracle declares exactly one member,
    /// <c>event type long ontranslate ( long source, long category, ref string text )</c>
    /// [<c>ws_objects/pfw.ui.pbl.src/n_cst_i18n.sru</c>:L9], and the direction of data flow is the whole
    /// reason the silent fallback works: the facade returns the variable it passed IN, on the line after the
    /// call, whatever the provider did with it. A contract that RETURNED the translation instead could not
    /// express "not handled" without a sentinel, and the sentinel would then have to be distinguished from a
    /// legitimately empty translation.
    /// </para>
    /// <para>
    /// Asserted by reflection because it is a statement about the SHAPE of the contract rather than about any
    /// behaviour of an implementation, and because the shape is the thing a future refactor would be tempted
    /// to tidy into a return value.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheProviderContractMutatesARefStringRatherThanReturningTheTranslation()
    {
        MethodInfo[] members = typeof(II18nProvider).GetMethods();

        Assert.Single(members);

        MethodInfo onTranslate = members[0];

        // A handled FLAG, not a translation.
        Assert.Equal(typeof(long), onTranslate.ReturnType);

        ParameterInfo[] parameters = onTranslate.GetParameters();
        Assert.Equal(3, parameters.Length);

        ParameterInfo text = parameters[2];

        Assert.True(text.ParameterType.IsByRef, "The text parameter must be passed by reference.");

        // `ref`, not `out`: an out parameter would not carry the LOOKUP KEY in, and the key travelling in on
        // the same parameter the translation travels out on is exactly what makes a refusal a no-op.
        Assert.False(text.IsOut, "The text parameter must be ref rather than out.");
        Assert.Equal(typeof(string), text.ParameterType.GetElementType());

        // Nullable, because PowerBuilder strings can be null and AAP 0.4.5.4 forbids collapsing that null.
        // An implementor therefore cannot dereference it without the compiler demanding a null check, which
        // is the mechanism that keeps a null key answering "not mine" instead of throwing.
        Assert.Null(facadeReturnedFor(null));

        static string? facadeReturnedFor(string? candidate) =>
            ScriptedLocalization.With(new ScriptedI18nProvider().TeachMarkers())
                .I18N(Categories.CAT_DWSVC, candidate);
    }

    /// <summary>
    /// The facade has no logging dependency at all, so the fallback cannot be silent in behaviour but noisy
    /// in a log.
    /// </summary>
    /// <remarks>
    /// "Does not log" is otherwise unfalsifiable by observation - a log write leaves no trace in a return
    /// value. Stated structurally instead: the facade is constructed with no arguments and holds no member
    /// through which a logger could be reached, so there is nowhere for a diagnostic to go. That also keeps
    /// the untranslated-message path free of the CWE-532 exposure a "translation missing" log line would
    /// create for message text that may embed user data.
    /// </remarks>
    [Fact]
    public void TheLocalizationFacadeHasNoLoggingDependency()
    {
        ConstructorInfo[] constructors = typeof(I18n).GetConstructors();

        Assert.Single(constructors);
        Assert.Empty(constructors[0].GetParameters());

        foreach (FieldInfo field in typeof(I18n).GetFields(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            Assert.DoesNotContain("Logger", field.FieldType.Name, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ==============================================================================================
    //  PHASE 5 - THE REST PROJECTION OF THE ERRORS
    // ==============================================================================================

    /// <summary>The projection's group prefix for the DataWindow surface.</summary>
    private const string DataWindowRoute = "/v1/datawindow";

    /// <summary>The projection's group prefix for the column-expression surface.</summary>
    private const string ExpressionRoute = DataWindowRoute + "/expression";

    /// <summary>The DataWindow handle the bound test host answers for.</summary>
    private const string BoundHandle = "company";

    /// <summary>An editable integer column on the bound COMPANY fixture.</summary>
    private const string IntegerColumn = "age";

    /// <summary>
    /// A LOCALIZED census site reaches a Gateway caller over REST with its text, category, substitution
    /// arguments and severity intact.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// End to end through the in-process host: a real service container, a real route table, real
    /// authentication, and the projection's one shared error mapping doing the conversion. The site is
    /// <c>n_cst_dwsvc_contextmenu.sru</c>:L1018 - non-numeric text pasted into an integer column - reached by
    /// driving the very model instance the host will answer from, so the payload the route relays is the
    /// payload the conversion produced rather than one this test built.
    /// </para>
    /// <para>
    /// A translating provider is installed, which is what makes the localized half of the split observable
    /// ON THE BOUNDARY too: the text a caller receives is the TRANSLATED text, so the routing survives the
    /// projection and is not something only the domain layer knows about.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ALocalizedSiteReachesARestCallerWithEveryFieldIntactAsync()
    {
        await using DataServicesTestHostFactory host = new()
        {
            BindsDataWindowHost = true,
            SubstitutesLocalizationProvider = true,
        };

        using HttpClient client = host.CreateAuthenticatedClient();

        // Taught BEFORE the payload is composed. The host's facade holds this very provider, so teaching it
        // here is what arms the translation the model will perform below.
        _ = host.Localization.TeachMarkers();
        _ = host.Localization.Teach(
            ContextMenuModel.TypeMismatchMessageKey,
            LegacyMessageKeys.MarkerFor(ContextMenuModel.TypeMismatchMessageKey));

        DataWindowModelSet set = ArrangeBoundIntegerColumn(host);

        // :L1018 - the integer arm's type guard. "abc" is not numeric, so the paste is refused and the
        // dialog site is reached on the model the route will report from.
        Assert.False(
            Predicates.IsSucceeded(set.ContextMenu.Paste2Column(IntegerColumn, "abc")),
            "The bound arrangement failed to reach the type-mismatch site, so there is no payload to relay.");

        ContextMenuError produced = Assert.IsType<ContextMenuError>(set.ContextMenu.PendingError);
        Assert.Equal("n_cst_dwsvc_contextmenu.sru:L1018", produced.SourceLocator, StringComparer.Ordinal);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(DataWindowRoute + "/context-menu/state", UriKind.Relative),
            new { datawindowHandle = BoundHandle, row = 1L },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        JsonElement error = body.RootElement.GetProperty("error");

        // THE TEXT, byte for byte, and TRANSLATED - so the localization routing survived the boundary.
        Assert.Equal(produced.Text, error.GetProperty("text").GetString(), StringComparer.Ordinal);
        Assert.Contains(
            LegacyMessageKeys.MarkerFor(ContextMenuModel.TypeMismatchMessageKey),
            error.GetProperty("text").GetString(),
            StringComparison.Ordinal);

        // THE CATEGORY, read from Categories rather than spelled as a number. Proto renders a 64-bit integer
        // as a JSON string, which is why it is parsed rather than read as a number.
        Assert.True(error.GetProperty("localized").GetBoolean());
        Assert.Equal(
            Categories.CAT_DWSVC,
            long.Parse(
                error.GetProperty("category").GetString() ?? string.Empty,
                CultureInfo.InvariantCulture));

        // THE SEVERITY - the legacy StopSign!, surviving as a severity value rather than as an icon.
        Assert.Equal(
            "SEVERITY_STOP_SIGN",
            error.GetProperty("severity").GetString(),
            StringComparer.Ordinal);

        // THE SUBSTITUTION ARGUMENTS, still arguments: the row template, the detail key, the row, and the
        // offending value - so a caller can re-render rather than re-parse.
        ImmutableArray<string?> arguments =
            [.. error.GetProperty("formatArgs").EnumerateArray().Select(static a => a.GetString())];

        Assert.Contains(ContextMenuModel.RowNumberMessageKey, arguments);
        Assert.Contains(ContextMenuModel.TypeMismatchMessageKey, arguments);
        Assert.Contains("1", arguments, StringComparer.Ordinal);
        Assert.Contains("abc", arguments, StringComparer.Ordinal);
    }

    /// <summary>
    /// A NON-LOCALIZED census site reaches a Gateway caller over REST with its hardcoded text unchanged and
    /// no localization category.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// The other half of the split, driven the same way and over the same projection. The site is
    /// <c>n_cst_dwsvc_columnexp.sru</c>:L1607 - a duplicate variable definition - reached by adding the same
    /// variable name twice through the published route rather than by calling a factory, so the whole chain
    /// from route to engine to conversion to wire is exercised.
    /// </para>
    /// <para>
    /// It also demonstrates the in-band status projection: a failing return code inside a 200-shaped response
    /// message becomes an HTTP problem document carrying the original response under its <c>response</c>
    /// extension, so the payload is still delivered rather than replaced by a status line. The assertion
    /// accepts either rendering and locates the error in whichever arrived, because WHERE the projection puts
    /// it is the projection's business - that it arrives COMPLETE is this file's.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ANonLocalizedSiteReachesARestCallerUnchangedAndWithoutACategoryAsync()
    {
        await using DataServicesTestHostFactory host = new()
        {
            BindsDataWindowHost = true,
            SubstitutesLocalizationProvider = true,
        };

        using HttpClient client = host.CreateAuthenticatedClient();

        // Fully armed, and about to be ignored: the engine has no way to reach it. This is the preserved
        // defect asserted at the boundary rather than in the domain.
        _ = host.Localization.TeachMarkers();

        using HttpResponseMessage opened = await client.PostAsJsonAsync(
            new Uri(ExpressionRoute + "/sessions", UriKind.Relative),
            new { datawindowHandles = new[] { BoundHandle } },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);

        using JsonDocument session = JsonDocument.Parse(
            await opened.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        string sessionId = session.RootElement.GetProperty("sessionId").GetString()
            ?? throw new InvalidOperationException("The expression session was opened without an id.");

        Assert.NotEqual(string.Empty, sessionId);

        // THE HANDLE THE SESSION REPORTS, NOT THE ONE THAT WAS SENT. A session normalises the handles it
        // registers, so every subsequent operation has to be correlated by the reported form; sending the
        // request's own spelling back resolves to nothing and the site is never reached.
        string handle = session.RootElement.GetProperty("datawindowHandles")
            .EnumerateArray()
            .Select(static element => element.GetString())
            .FirstOrDefault(static candidate => !string.IsNullOrEmpty(candidate))
            ?? throw new InvalidOperationException(
                "The expression session registered no DataWindow handle, so no engine can be reached.");

        var variable = new
        {
            sessionId,
            datawindowHandle = handle,
            name = "duplicated",
            value = new { longValue = "1" },
        };

        using HttpResponseMessage first = await client.PostAsJsonAsync(
            new Uri(ExpressionRoute + "/variables/add", UriKind.Relative),
            variable,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // THE SECOND ADD IS THE CENSUS SITE: :L1607, duplicate variable definition.
        using HttpResponseMessage duplicate = await client.PostAsJsonAsync(
            new Uri(ExpressionRoute + "/variables/add", UriKind.Relative),
            variable,
            TestContext.Current.CancellationToken);

        using JsonDocument body = JsonDocument.Parse(
            await duplicate.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        JsonElement expressionError = LocateRelayedError(body.RootElement);
        JsonElement structured = expressionError.GetProperty("error");

        // THE TEXT, rendered from the oracle's own template and carrying the offending name. Compared against
        // the descriptor's template rather than a retyped string, so this is the CONVERSION being pinned.
        ExpressionErrorSiteDescriptor descriptor =
            ExpressionErrorCatalog.Get(ExpressionErrorSite.DuplicateVariableDefinition);

        Assert.Equal(
            Formatting.Sprintf(
                descriptor.FormatTemplate,
                [.. structured.GetProperty("formatArgs").EnumerateArray()
                    .Select(static a => a.GetString())]),
            structured.GetProperty("text").GetString(),
            StringComparer.Ordinal);

        // THE HARDCODED TITLE, UNCHANGED. The provider was taught this very key and was never asked.
        Assert.Equal(
            ExpressionErrorCatalog.LegacyTitle,
            structured.GetProperty("title").GetString(),
            StringComparer.Ordinal);
        Assert.NotEqual(
            LegacyMessageKeys.MarkerFor(ExpressionErrorCatalog.LegacyTitle),
            structured.GetProperty("title").GetString());

        // NOT LOCALIZED, AND NO CATEGORY AT ALL - not an unresolved one.
        Assert.False(structured.GetProperty("localized").GetBoolean());
        Assert.Equal("0", structured.GetProperty("category").GetString(), StringComparer.Ordinal);

        Assert.Equal(
            "SEVERITY_STOP_SIGN",
            structured.GetProperty("severity").GetString(),
            StringComparer.Ordinal);

        // A PLAIN MESSAGE HAS NO CARET, and the absence travels as an absence rather than as a guess.
        Assert.Equal(
            "0",
            expressionError.GetProperty("caretPosition").GetString(),
            StringComparer.Ordinal);
        Assert.Equal(
            string.Empty,
            expressionError.GetProperty("expression").GetString(),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Neither half of the census carries a database statement, a secret-shaped value, a key, a password or a
    /// connection string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C-F, and the DataServices side of AAP 0.6.6's redaction obligation. <c>common.v1.DbError.sqlsyntax</c>
    /// carries the complete generated statement including interpolated literal values, and the redaction is
    /// owned by the Persistence service that generates it - so what has to be asserted HERE is that
    /// DataServices does not UNDO it, and more broadly that none of the forty converted payloads has become a
    /// channel for statement text or credentials.
    /// </para>
    /// <para>
    /// The census sites are DataWindow-layer dialogs and touch no database at all, so the correct expectation
    /// is that the field never appears in any of their payloads. That is asserted rather than assumed, because
    /// the arguments these payloads DO carry are caller-supplied values - a pasted cell, a variable name, an
    /// expression fragment - and a payload that had started relaying statement text or connection parameters
    /// would look exactly like a payload relaying a pasted cell.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoConvertedPayloadCarriesStatementTextOrASecretShapedValue()
    {
        (I18n facade, _) = TranslatingLocalization();

        foreach (DialogSite site in DialogCensus.All)
        {
            ProducedPayload payload = PayloadProbe.Produce(site, facade);

            AssertCarriesNoSensitiveShape(site, payload.Structured.Text);
            AssertCarriesNoSensitiveShape(site, payload.Structured.Title);

            foreach (string argument in payload.Structured.FormatArgs)
            {
                AssertCarriesNoSensitiveShape(site, argument);
            }

            if (payload.Expression is { } expression)
            {
                AssertCarriesNoSensitiveShape(site, expression.Expression);
                AssertCarriesNoSensitiveShape(site, expression.RenderedMarker);

                // The caught-exception channel is bounded to the ONE legacy site that published such text
                // [:L739] (CWE-209). Every other site leaves it empty, so a managed exception cannot smuggle
                // host-internal detail onto the boundary through a field justified by the legacy.
                if (site.LegacyLine != (int)ExpressionErrorSite.ExpressionCacheError)
                {
                    Assert.Equal(string.Empty, expression.CaughtExceptionText);
                }
            }
        }
    }

    /// <summary>
    /// Every value the census's payloads carry is free of statement text and of credential shapes.
    /// </summary>
    /// <param name="site">The census row, named in the failure so a finding is locatable.</param>
    /// <param name="value">The value.</param>
    /// <remarks>
    /// The statement keywords are tested as WHOLE, SPACE-DELIMITED words, because the Chinese message bodies
    /// legitimately contain Latin fragments - <c>Expression:</c>, <c>Error:</c>, <c>Sub Expression:</c> - and a
    /// naive substring test for a keyword such as <c>update</c> would fire on ordinary prose and have to be
    /// weakened until it detected nothing.
    /// </remarks>
    private static void AssertCarriesNoSensitiveShape(DialogSite site, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        string[] statementKeywords =
            ["select", "insert", "update", "delete", "where", "values"];

        ImmutableArray<string> words =
            [.. value
                .Split([' ', '\t', '\n', '\r', '(', ')', ',', ';', ':'], StringSplitOptions.RemoveEmptyEntries)
                .Select(static word => word.ToLowerInvariant())];

        // A SINGLE keyword is prose; two or more in one value is a statement. "Expression:" plus a column
        // name cannot reach two, and no generated statement can avoid it.
        int matches = statementKeywords.Count(keyword => words.Contains(keyword, StringComparer.Ordinal));

        Assert.True(
            matches < 2,
            site.ShortLocator + " carries a value that reads as a database statement: '" + value
                + "'. AAP 0.6.6 makes the statement field the persistence service's to redact, and this "
                + "service must not become a second channel for it.");

        string[] credentialMarkers =
            ["password=", "pwd=", "logpass", "api_key", "apikey", "secret=", "bearer ", "-----begin"];

        foreach (string marker in credentialMarkers)
        {
            Assert.DoesNotContain(marker, value, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Finds the relayed expression error whether the projection rendered a plain body or an in-band problem
    /// document.
    /// </summary>
    /// <param name="root">The response body's root.</param>
    /// <returns>The <c>ExpressionError</c> element.</returns>
    /// <remarks>
    /// A failing return code inside an otherwise well-formed response message is projected as an HTTP problem
    /// carrying the original message under a <c>response</c> extension, so the payload survives rather than
    /// being replaced. Both renderings are accepted here: WHERE the projection puts the error is the
    /// projection's own contract, asserted by the tests that own it, whereas THAT it arrives complete is what
    /// this file is for.
    /// </remarks>
    private static JsonElement LocateRelayedError(JsonElement root)
    {
        if (root.TryGetProperty("error", out JsonElement direct)
            && direct.ValueKind == JsonValueKind.Object)
        {
            return direct;
        }

        Assert.True(
            root.TryGetProperty("response", out JsonElement relayed),
            "The response carried neither an error member nor a relayed response extension, so the "
                + "converted payload did not reach the caller at all.");

        return relayed.GetProperty("error");
    }

    /// <summary>
    /// Arranges the bound COMPANY fixture so its integer column is editable, visible and populated.
    /// </summary>
    /// <param name="host">The in-process host factory.</param>
    /// <returns>The model set the routes will answer from.</returns>
    /// <remarks>
    /// The model set is resolved from the HOST'S OWN container and is the instance the route reports from -
    /// the provider caches one set per handle - so driving it here is driving the service, not a copy of it.
    /// The fixture reproduces <c>dw_sqlite.srd</c>, which declares update and key metadata but not the
    /// per-row visibility and protection properties every column operation probes; an untaught property reads
    /// as the invalid-expression sentinel, which the oracle treats as NOT visible and skips.
    /// </remarks>
    private static DataWindowModelSet ArrangeBoundIntegerColumn(DataServicesTestHostFactory host)
    {
        DataWindowModelSet set = host.Services
            .GetRequiredService<IDataWindowModelSetProvider>()
            .GetOrCreate(BoundHandle)
            ?? throw new InvalidOperationException(
                "The bound host did not answer for handle '" + BoundHandle + "'.");

        FakeDataWindowHost fake = Assert.IsType<FakeDataWindowHost>(set.Host);

        fake.SetDescribe(IntegerColumn + ".Type", "column");
        fake.SetDescribe(IntegerColumn + ".Band", "detail");
        fake.SetDescribe(IntegerColumn + ".edit.style", "edit");
        fake.SetDescribe(IntegerColumn + ".visible", "1");
        fake.SetDescribe(IntegerColumn + ".protect", "0");

        _ = fake.AddRow(1m, "Ada", 41m, "Marylebone", 1000m, new DateTime(1815, 12, 10));

        return set;
    }
}

/// <summary>
/// Drives every one of the ten context-menu dialog sites and collects the payload each produces.
/// </summary>
/// <remarks>
/// <para>
/// The ten sites are reached through four public operations, and each is reached HONESTLY - by arranging a
/// column whose type and contents make the oracle's own guard fail - rather than by poking
/// <c>PendingError</c>, which has no setter. That matters for the census: a locator collected from a
/// genuinely-executed path proves the site is REACHABLE, whereas a locator read out of a table would only
/// prove someone wrote it down.
/// </para>
/// <para>
/// The type-mismatch arms are reached by column TYPE, which is what selects between :L1018, :L1027, :L1033,
/// :L1039 and :L1045. The prefix-to-category mapping the oracle uses is at
/// <c>ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc.sru</c>:L26-L32.
/// </para>
/// </remarks>
internal static class ContextMenuErrorProbe
{
    /// <summary>The check-box column every check-box arm operates on.</summary>
    private const string FlagColumn = "flag";

    /// <summary>The code-table column the invalid-value arm operates on.</summary>
    private const string GradeColumn = "grade";

    /// <summary>The check-box "on" value.</summary>
    private const string OnValue = "Y";

    /// <summary>The check-box "off" value.</summary>
    private const string OffValue = "N";

    /// <summary>
    /// A value that is not numeric, not a date, not a time and not a datetime - so it fails whichever
    /// type guard the column's type selects.
    /// </summary>
    private const string UntypedText = "abc";

    /// <summary>Drives all ten sites and returns their locators.</summary>
    /// <returns>The ten <c>SourceLocator</c> values, in the order the probe reaches them.</returns>
    public static ImmutableArray<string> EmitEveryLocator() =>
        [.. EmitEveryError().Select(static error => error.SourceLocator)];

    /// <summary>Drives all ten sites and returns the payloads.</summary>
    /// <param name="i18n">
    /// The localization facade the model composes through, or <see langword="null"/> for a bare facade with
    /// no provider - which exercises the silent-passthrough fallback and yields the oracle's own text.
    /// </param>
    /// <returns>The ten payloads.</returns>
    public static ImmutableArray<ContextMenuError> EmitEveryError(I18n? i18n = null)
    {
        ImmutableArray<ContextMenuError>.Builder errors =
            ImmutableArray.CreateBuilder<ContextMenuError>(10);

        // ------------------------------------------------------------------------------------------
        //  THE FOUR CHANGE-REFUSED SITES.
        //  Three check-box operations plus the paste path, each refused by the item-change handler.
        // ------------------------------------------------------------------------------------------
        (FakeDataWindowHost checkHost, ContextMenuModel checkService) = ArrangeCheckBox(i18n);
        checkHost.DoItemChangeHandler = static (_, _, _) => 1L;

        // The seeded cell holds the OFF value, so checking it has work to do. An operation that finds the
        // row already in the requested state SKIPS it and answers OK, and the dialog is never reached.
        errors.Add(Refused(checkService, checkService.CheckColumn(FlagColumn)));

        checkHost.SetItem(1L, FlagColumn, OnValue);
        errors.Add(Refused(checkService, checkService.UncheckColumn(FlagColumn)));

        // Revert always has work to do, whichever way the cell is currently set.
        errors.Add(Refused(checkService, checkService.RevertCheckColumn(FlagColumn)));

        (FakeDataWindowHost pasteHost, ContextMenuModel pasteService) = ArrangeCheckBox(i18n);
        pasteHost.DoItemChangeHandler = static (_, _, _) => 1L;
        errors.Add(Refused(pasteService, pasteService.Paste2Column(FlagColumn, OnValue)));

        // ------------------------------------------------------------------------------------------
        //  THE INVALID-VALUE SITE.
        //  A code-table column rejects a value that is neither a display text nor a data value.
        // ------------------------------------------------------------------------------------------
        (_, ContextMenuModel gradeService) = ArrangeCodeTable(i18n);
        errors.Add(Refused(gradeService, gradeService.Paste2Column(GradeColumn, "Z")));

        // ------------------------------------------------------------------------------------------
        //  THE FIVE TYPE-MISMATCH SITES, one per column-type category.
        // ------------------------------------------------------------------------------------------
        foreach (string colType in TypedColumnTypes)
        {
            (_, ContextMenuModel typedService) = ArrangeTypedColumn(colType, i18n);

            errors.Add(Refused(typedService, typedService.Paste2Column(FlagColumn, UntypedText)));
        }

        return errors.ToImmutable();
    }

    /// <summary>
    /// The five column types that select the five distinct type-mismatch arms, in the order the oracle
    /// tests them.
    /// </summary>
    /// <remarks>
    /// One type per arm, chosen from the oracle's own prefix table [<c>n_cst_dwsvc.sru</c>:L26-L32]:
    /// <c>long</c> is the integer category, <c>decimal(2)</c> the decimal category, and the three temporal
    /// spellings name themselves. The STRING category is deliberately absent - the oracle validates nothing
    /// for it, because any text is a legal string, so it raises no dialog and is not a census site.
    /// </remarks>
    private static readonly string[] TypedColumnTypes =
        ["long", "decimal(2)", "datetime", "date", "time"];

    /// <summary>Asserts an operation actually refused, and returns the payload it produced.</summary>
    /// <param name="service">The model.</param>
    /// <param name="result">The code the operation answered.</param>
    /// <returns>The payload.</returns>
    /// <remarks>
    /// The result code is asserted to be a FAILURE and the payload to be present, so a probe that silently
    /// stopped reaching a site cannot contribute a null or a stale locator to the census. The code itself is
    /// not pinned to a particular value here - the per-arm codes belong to
    /// <c>ContextMenuModelTests</c> - only that the operation did not succeed.
    /// </remarks>
    private static ContextMenuError Refused(ContextMenuModel service, long result)
    {
        Assert.False(
            Predicates.IsSucceeded(result),
            "The arrangement failed to reach a dialog site: the operation answered "
                + result.ToString(CultureInfo.InvariantCulture)
                + ", which is not a failure, so no payload was produced.");

        return Assert.IsType<ContextMenuError>(service.PendingError);
    }

    /// <summary>Arranges a model over a one-row check-box column.</summary>
    /// <param name="i18n">The localization facade, or <see langword="null"/> for a bare one.</param>
    /// <returns>The host and the model.</returns>
    private static (FakeDataWindowHost Host, ContextMenuModel Service) ArrangeCheckBox(I18n? i18n)
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService(i18n);

        FakeDataWindowObjectDefinition column = host.AddColumn(FlagColumn, "char(1)");

        // The fake defaults the tab sequence to "0", which the oracle's editability guard reads as
        // non-editable - so every column operation would walk the buffer and write nothing.
        column.TabSequence = "60";

        host.SetDescribe(FlagColumn + ".Type", "column");
        host.SetDescribe(FlagColumn + ".Band", "detail");
        host.SetDescribe(FlagColumn + ".edit.style", "checkbox");
        host.SetDescribe(FlagColumn + ".checkbox.on", OnValue);
        host.SetDescribe(FlagColumn + ".checkbox.off", OffValue);

        // The per-row visibility and protection probes are property reads, and the oracle skips a row whose
        // cell is invisible or protected. The fake answers the invalid-expression sentinel for an untaught
        // property, which reads as NOT visible.
        host.SetDescribe(FlagColumn + ".visible", "1");
        host.SetDescribe(FlagColumn + ".protect", "0");

        _ = host.AddRow(OffValue);

        return (host, service);
    }

    /// <summary>Arranges a model over a one-row code-table column.</summary>
    /// <param name="i18n">The localization facade, or <see langword="null"/> for a bare one.</param>
    /// <returns>The host and the model.</returns>
    private static (FakeDataWindowHost Host, ContextMenuModel Service) ArrangeCodeTable(I18n? i18n)
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService(i18n);

        FakeDataWindowObjectDefinition grade = host.AddColumn(GradeColumn, "char(1)");
        grade.TabSequence = "80";

        _ = grade.AddCodeTableEntry("优", "A");
        _ = grade.AddCodeTableEntry("良", "B");

        host.SetDescribe(GradeColumn + ".Type", "column");
        host.SetDescribe(GradeColumn + ".Band", "detail");
        host.SetDescribe(GradeColumn + ".edit.style", "radiobuttons");
        host.SetDescribe(GradeColumn + ".visible", "1");
        host.SetDescribe(GradeColumn + ".protect", "0");

        _ = host.AddRow(null);

        return (host, service);
    }

    /// <summary>Arranges a model over a one-row column of the given type.</summary>
    /// <param name="colType">The column type, which selects the type-mismatch arm.</param>
    /// <param name="i18n">The localization facade, or <see langword="null"/> for a bare one.</param>
    /// <returns>The host and the model.</returns>
    private static (FakeDataWindowHost Host, ContextMenuModel Service) ArrangeTypedColumn(
        string colType,
        I18n? i18n)
    {
        (FakeDataWindowHost host, ContextMenuModel service) = NewService(i18n);

        FakeDataWindowObjectDefinition column = host.AddColumn(FlagColumn, colType);
        column.TabSequence = "70";

        host.SetDescribe(FlagColumn + ".Type", "column");
        host.SetDescribe(FlagColumn + ".Band", "detail");
        host.SetDescribe(FlagColumn + ".edit.style", "edit");
        host.SetDescribe(FlagColumn + ".visible", "1");
        host.SetDescribe(FlagColumn + ".protect", "0");

        _ = host.AddRow(null);

        return (host, service);
    }

    /// <summary>Builds a bare model attached to a bare host.</summary>
    /// <param name="i18n">The localization facade, or <see langword="null"/> for a bare one.</param>
    /// <returns>The host and the model.</returns>
    /// <remarks>
    /// A bare <see cref="I18n"/> with no provider installed is the SILENT PASSTHROUGH state, which is what
    /// makes the composed text come back as the oracle's own Chinese source strings. The localization-split
    /// theory passes a provider instead, and that is the only difference between the two readings.
    /// </remarks>
    private static (FakeDataWindowHost Host, ContextMenuModel Service) NewService(I18n? i18n)
    {
        FakeDataWindowHost host = new(new EventBroker())
        {
            Processing = "1",
            ReadOnly = "no",
        };

        ContextMenuModel service = new(
            i18n ?? new I18n(),
            new DataWindowExpressionEvaluator(host),
            Options.Create(new DataServicesOptions()));

        service.OnInit(host);

        return (host, service);
    }
}

/// <summary>
/// One census site's converted payload, AS IT TRAVELS ON THE CONTRACT.
/// </summary>
/// <param name="Structured">
/// The <c>dataservices.v1.StructuredError</c> the site projects onto.
/// </param>
/// <param name="Expression">
/// The enclosing <c>dataservices.v1.ExpressionError</c> for a column-expression site - which is where the
/// expression text and the caret position live - or <see langword="null"/> for the twelve localizing sites,
/// which have neither.
/// </param>
/// <remarks>
/// Deliberately the WIRE form rather than the domain records. Projecting each payload through the real
/// mapping before asserting on it settles two of Correction 5's requirements with one construction: the
/// fields are complete, AND they are complete ON THE BOUNDARY, so a caller receives them rather than the
/// service logging them. A domain-only assertion would pass just as happily against a mapping that dropped
/// every field on the floor.
/// </remarks>
internal sealed record ProducedPayload(WireStructuredError Structured, WireExpressionError? Expression);

/// <summary>
/// Produces the converted payload for any census site, through the shipped wire mapping.
/// </summary>
/// <remarks>
/// The four legacy objects reach their payloads by four different routes, and the probe hides that
/// difference behind one call so the census theories can iterate all forty rows uniformly. What it does NOT
/// hide is the localization facade: every route takes it as a parameter, because passing a translating
/// provider and re-reading the same forty rows is exactly how the split matrix is measured.
/// </remarks>
internal static class PayloadProbe
{
    /// <summary>The check-box column the row-select arrangement operates on.</summary>
    private const string FlagColumn = "flag";

    /// <summary>The check-box "on" value.</summary>
    private const string OnValue = "Y";

    /// <summary>The check-box "off" value.</summary>
    private const string OffValue = "N";

    /// <summary>The column the validation-error arrangement operates on.</summary>
    private const string ValidatedColumn = "name";

    /// <summary>The offending text the validation-error path is driven with.</summary>
    private const string OffendingText = "bad";

    /// <summary>
    /// A synthesized substitution argument. Distinct per position so a mapping that reordered or
    /// duplicated arguments would be visible rather than merely suspected.
    /// </summary>
    private const string ArgumentPrefix = "arg";

    /// <summary>An expression to report a caret-bearing parse error against.</summary>
    /// <remarks>
    /// Contains a wide character on purpose. The oracle measures its caret in BYTES, so an expression that
    /// is pure ASCII would let a character-counting implementation pass. The arithmetic itself belongs to
    /// <c>ParseErrorFormatterTests</c>; this only ensures the value travelling here is not the easy case.
    /// </remarks>
    internal const string ProbeExpression = "if(销售额 > 100, 1, 0)";

    /// <summary>The caret position the caret-bearing sites are reported at.</summary>
    internal const long ProbeCaretPosition = 4L;

    /// <summary>Produces the payload for one census site.</summary>
    /// <param name="site">The census row.</param>
    /// <param name="i18n">
    /// The facade to compose through, or <see langword="null"/> for a bare facade with no provider - the
    /// silent-passthrough state, in which the composed text is the oracle's own source text.
    /// </param>
    /// <returns>The converted payload as it travels on the contract.</returns>
    public static ProducedPayload Produce(DialogSite site, I18n? i18n = null)
    {
        ArgumentNullException.ThrowIfNull(site);

        if (site.IsColumnExpressionSite)
        {
            return ProduceColumnExpression(site);
        }

        if (string.Equals(site.LegacyFile, DialogCensus.RowSelectObject, StringComparison.Ordinal))
        {
            return ProduceRowSelect(i18n);
        }

        if (string.Equals(site.LegacyFile, DialogCensus.ContextMenuObject, StringComparison.Ordinal))
        {
            return ProduceContextMenu(site, i18n);
        }

        if (string.Equals(
                site.LegacyFile,
                DialogCensus.ServiceExtensionObject,
                StringComparison.Ordinal))
        {
            return ProduceValidationError(i18n);
        }

        throw new InvalidOperationException(
            "No probe route for census site " + site.ShortLocator
                + ". Every census row must be producible, or the sweep is asserting over a subset.");
    }

    /// <summary>
    /// Produces a column-expression payload through the oracle's own two factories.
    /// </summary>
    /// <param name="site">The census row.</param>
    /// <returns>The payload.</returns>
    /// <remarks>
    /// <para>
    /// <b>NO LOCALIZATION FACADE IS ACCEPTED HERE, AND THAT IS THE POINT.</b> The factories take no
    /// provider, have no way to reach one, and the oracle object they reproduce makes zero <c>I18N</c>
    /// calls. A parameter would imply a choice the oracle does not offer. This is the preserved defect
    /// expressed as a SIGNATURE rather than as a comment - the split matrix can hand a translating provider
    /// to the other twelve routes and has nowhere to hand one here.
    /// </para>
    /// <para>
    /// The arguments are synthesized from the descriptor's own declared count rather than hardcoded per
    /// site, so a site whose arity changes is reported by the factory instead of silently mis-called.
    /// </para>
    /// </remarks>
    private static ProducedPayload ProduceColumnExpression(DialogSite site)
    {
        ExpressionErrorSite id = (ExpressionErrorSite)site.LegacyLine;
        ExpressionErrorSiteDescriptor descriptor = ExpressionErrorCatalog.Get(id);

        string?[] args =
            [.. Enumerable.Range(1, descriptor.ArgumentCount)
                .Select(static ordinal =>
                    ArgumentPrefix + ordinal.ToString(CultureInfo.InvariantCulture))];

        ExpressionParseError error = descriptor.Family == ExpressionErrorFamily.CaretBearingParseError
            ? ParseErrorFormatter.CreateParseError(id, ProbeExpression, ProbeCaretPosition, args)
            : ParseErrorFormatter.CreatePlainError(id, args);

        WireExpressionError projected = ExpressionWireProjection.ToWire(error)
            ?? throw new InvalidOperationException(
                "The wire mapping dropped the payload for " + site.ShortLocator + ".");

        return new ProducedPayload(projected.Error, projected);
    }

    /// <summary>Produces the row-select refusal payload by driving a refused range propagation.</summary>
    /// <param name="i18n">The facade, or <see langword="null"/> for a bare one.</param>
    /// <returns>The payload.</returns>
    private static ProducedPayload ProduceRowSelect(I18n? i18n)
    {
        FakeDataWindowHost host = new();

        FakeDataWindowObjectDefinition column = host.AddColumn(FlagColumn, "char(1)");
        column.TabSequence = "10";
        column.EditStyle = "checkbox";
        column.CheckBoxOn = OnValue;
        column.CheckBoxOff = OffValue;
        column.SetProperty("visible", "1");

        for (int row = 0; row < 4; row++)
        {
            _ = host.AddRow(OnValue);
        }

        host.CurrentRow = 1L;

        RowSelectService service = new(
            i18n ?? new I18n(),
            Options.Create(new DataServicesOptions
            {
                RowSelect = new RowSelectOptions { Style = RowSelectService.RS_MULTIPLE },
            }));

        service.OnInit(host);

        // The propagation refuses to run unless a MODIFIER GESTURE produced the selection, and the clicked
        // row must itself be selected. A control-click reaches that state the way the oracle reaches it,
        // rather than by writing the flag directly.
        host.ArrangeSelectedRows();
        _ = service.OnLButtonClk(0L, 0L, 1L, FlagObject(host), shiftHeld: false, ctrlHeld: true);
        host.ArrangeSelectedRows(1L, 2L, 3L, 4L);

        // Row 3 refuses, which stops the walk and raises the dialog.
        host.DoItemChangeHandler = static (row, _, _) => row == 3L ? 42L : 0L;

        _ = service.OnLButtonClk(0L, 0L, 1L, FlagObject(host), shiftHeld: false, ctrlHeld: false);

        RowSelectRejectionError error =
            Assert.IsType<RowSelectRejectionError>(service.PendingError);

        WireStructuredError projected = DataWindowWireProjection.ToWireError(error)
            ?? throw new InvalidOperationException(
                "The wire mapping dropped the row-select payload.");

        return new ProducedPayload(projected, null);
    }

    /// <summary>Produces one context-menu payload, selected by locator.</summary>
    /// <param name="site">The census row.</param>
    /// <param name="i18n">The facade, or <see langword="null"/> for a bare one.</param>
    /// <returns>The payload.</returns>
    private static ProducedPayload ProduceContextMenu(DialogSite site, I18n? i18n)
    {
        ContextMenuError error = ContextMenuErrorProbe.EmitEveryError(i18n)
            .Single(candidate =>
                string.Equals(candidate.SourceLocator, site.ShortLocator, StringComparison.Ordinal));

        WireStructuredError projected = DataWindowWireProjection.ToWireError(error)
            ?? throw new InvalidOperationException(
                "The wire mapping dropped the context-menu payload for " + site.ShortLocator + ".");

        return new ProducedPayload(projected, null);
    }

    /// <summary>
    /// Produces the validation-error payload by driving the item-validation event to its dialog arm.
    /// </summary>
    /// <param name="i18n">The facade, or <see langword="null"/> for a bare one.</param>
    /// <returns>The payload.</returns>
    /// <remarks>
    /// The column carries NO validation message, which is what routes the body through the localized
    /// FALLBACK composed at <c>se_cst_dw.sru</c>:L355 rather than through the column's own text. That is the
    /// arm the census row's companion line names, and it is the arm where localization is observable on both
    /// the title and the body at once.
    /// </remarks>
    private static ProducedPayload ProduceValidationError(I18n? i18n)
    {
        FakeDataWindowHost host = new();
        host.AddColumn(ValidatedColumn, FakeColumnType.CharOf(50));
        _ = host.AddRow("original");
        host.SetValidationMessage(ValidatedColumn, string.Empty);

        ValidationSession session = new(
            "structured-error-parity",
            "pfw-structured-error-parity",
            initialDisabledEventMask: 0u,
            lifetime: null,
            i18n,
            timeProvider: null);

        // The handler must answer zero, or the dialog arm is never reached: a non-zero answer is the
        // application handling the error itself.
        host.ItemErrorHandler = static (_, _, _) => 0L;

        ValidationErrorOutcome outcome = session.OnDwnItemValidationError(
            host,
            1L,
            host.DwObject(ValidatedColumn),
            OffendingText);

        ValidationStructuredError error = Assert.IsType<ValidationStructuredError>(outcome.Error);

        WireStructuredError projected = DataWindowWireProjection.ToWireError(error)
            ?? throw new InvalidOperationException(
                "The wire mapping dropped the validation-error payload.");

        return new ProducedPayload(projected, null);
    }

    /// <summary>The column object the row-select clicks are delivered against.</summary>
    /// <param name="host">The host.</param>
    /// <returns>The column object.</returns>
    private static IDataWindowObject FlagObject(FakeDataWindowHost host) =>
        host.GetObjectAttribute(FlagColumn);
}
