// ==================================================================================================
//  ReservedRouteMetadataTests - THE AUDITABLE CONTROL BEHIND CONSTRAINT C-D
//  ------------------------------------------------------------------------------------------------
//  SUBJECT     OpenApi/gateway.v1.yaml and OpenApi/security.v1.yaml, plus all three protocol
//              definitions (Proto/common.v1.proto, Proto/dataservices.v1.proto,
//              Proto/persistence.v1.proto) reached through their generated descriptors.
//  AUTHORITY   Agent Action Plan 0.2.2.2 (the prohibition, stated in implementation terms),
//              0.4.4 (the permitted form), 0.7.3 constraint C-D, docs/CONTRACTS.md 13 / 13.1,
//              docs/DEFERRED.md (the authoritative deferred roster).
//
//  READ THIS FILE AS EVIDENCE, NOT AS DOCUMENTATION
//  ------------------------------------------------------------------------------------------------
//  C-D is the hardest prohibition in this refactor and the one most easily satisfied on paper while
//  being broken in fact. It says: DO NOT IMPLEMENT THE FOUR DEFERRED SERVICES - DesignSystem,
//  Documents, Integration, ScriptBridge - NOT EVEN PARTIALLY, NOT EVEN TO "STUB THEM OUT". AAP 0.2.2.2
//  spells that out as: no `services/design-service/`, no `PowerFramework.Documents.csproj`, no
//  placeholder `Dockerfile`, no empty test project, and no `NotImplementedException`-throwing class
//  for any of the four.
//
//  AAP 0.4.4 then states the ONE permitted representation: each deferred service appears as a named
//  Gateway route returning `501 Not Implemented` with a machine-readable body naming the deferred
//  service it will eventually reach and the marker `reserved for Phase 2`, declared in Gateway's
//  routing and contract metadata ONLY. The plan closes the loophole in terms: "a routing declaration
//  is not a stub of the deferred service."
//
//  SO THIS SUITE ASSERTS BOTH HALVES, AND THE SECOND HALF IS THE ONE THAT MAKES IT EVIDENCE.
//
//      POSITIVE HALF   The four routes exist, and they exist in the PERMITTED form: a 501, no `2xx`,
//                      no request schema, a machine-readable body that names ITS OWN deferred
//                      service, the Phase-2 marker, and four declarations of identical shape.
//      NEGATIVE HALF   NOTHING BEYOND THEM EXISTS. No service, method, message, enum, enum value,
//                      field or oneof in any protocol definition, and no path, operation identifier,
//                      schema name or property name in either OpenAPI document, names a deferred
//                      capability.
//
//  A file that asserted only the routes would be documentation. Asserting the ABSENCE is what makes
//  it a control, because "stubbing out" is precisely the failure that a positive-only test cannot see.
//
//  THE THREE THINGS THAT DISTINGUISH A PERMITTED DECLARATION FROM A FORBIDDEN STUB
//  ------------------------------------------------------------------------------------------------
//  Stated plainly, because these are the three the positive half exists to check:
//      1. NO `2xx` OF ANY KIND. A success status would tell a consumer the route sometimes works.
//      2. NO REQUEST SCHEMA. Modelling an input is modelling the capability.
//      3. NOTHING BEHIND IT. No projected RPC, no request or response message, no implementation
//         target of any sort - which is what the negative half establishes document-wide.
//
//  WHAT IS SWEPT, AND WHY THAT LINE IS WHERE IT IS  (constraint C-K)
//  ------------------------------------------------------------------------------------------------
//  IDENTIFIERS ARE SWEPT. STRING DATA IS NOT. That is a principled line rather than a convenient one.
//
//  An identifier is where MODELLING shows up: a message called `SciterSession`, a field called
//  `fontMetrics`, an rpc called `SendMqtt`, a schema called `ZipArchive`. Any of those would be a
//  partial definition of a forbidden target even if no handler existed yet, because a consumer could
//  generate code against it.
//
//  String data is different, and AAP 0.4.4 is explicit that NAMING a deferred capability area is
//  PERMITTED metadata - it is the whole reason the routes are declared, since it is what makes the
//  shape of the eventual system legible from the gateway's contract. Three places in gateway.v1.yaml
//  legitimately carry a deferred service's name as an enum VALUE, and none of them models anything:
//      * `ReservedRouteBody.deferredService`   the four names, as the 501 body's destination member.
//      * `Capability.phaseOneDestination`      which Phase-1 destination each legacy capability bit
//                                              maps to - `DesignSystem`, `ScriptBridge` among them.
//      * `Capability.name` / `Capability.value` the eight PRESERVED legacy bit identifiers from
//                                              `ws_objects/pfw.shared.pbl.src/enums.sru:L41-L49`,
//                                              which include `INIT_FLAG_ENABLE_SCITER`, `_BLINK`,
//                                              `_BLINKFAST`, `_WEBVIEW` and `_DPIAWARE`. AAP 0.4.5.3
//                                              requires those spellings verbatim, and AAP 0.1.4 reads
//                                              the bitmask as the legacy framework's OWN decomposition
//                                              intent - mapping it onto the service roster is
//                                              corroboration that the Phase-1 slice is drawn right.
//  Those three sets are asserted EXACTLY, by value, in GatewayContractTests; sweeping them here would
//  duplicate that while forcing a sixteen-entry blanket into the exemption ledger below, which is the
//  opposite of what a narrow ledger is for. Instead, the enum-value door is closed BY LOCATION - only
//  two named schemas may carry a deferred service's name as enum data - which is a far tighter
//  statement than an exemption list. See
//  NoSchemaOutsideTheTwoPermittedCataloguesCarriesADeferredServiceNameAsEnumData.
//
//  PROTO ENUM VALUE NAMES *ARE* SWEPT, and that is the same rule rather than an exception to it:
//  `PY_LIKE_IGNORE_CASE` is an identifier the wire agrees on, not data carried over it.
//
//  THE EXEMPTION LEDGER IS THE MOST DANGEROUS PART OF THIS FILE
//  ------------------------------------------------------------------------------------------------
//  Every exemption is a hole in the control. So each one below is (a) IDENTITY-BASED - an exact
//  fully-qualified name, or one named type and its members - never a loose pattern; (b) carries the
//  AAP citation that justifies it; and (c) is LIVENESS-CHECKED by
//  `EveryExemptionInTheLedgerStillResolvesToARealContractSymbol`, so an entry that stops matching
//  anything fails the build and has to be pruned instead of quietly widening over time.
//
//  NEVER widen an exemption to silence a hit you cannot explain. If a sweep fails, the finding is
//  about the CONTRACT until proven otherwise.
//
//  DELIBERATELY NOT SWEPT, AND WHY - the traps, each with the in-scope symbol that proves it
//  ------------------------------------------------------------------------------------------------
//  These five words look like deferred capabilities and are not. Using any of them as a vocabulary
//  term would produce false failures, and a later agent would "fix" them by weakening the control -
//  so they are recorded here as measured findings rather than left to be rediscovered:
//      "menu"      matches `ContextMenuItem`, `ContextMenuModel`, `GetContextMenuModel`. The context
//                  menu's ITEM MODEL is the IN-SCOPE headless half (AAP 0.2.1.3 Correction 4); only
//                  DPI conversion, font measurement and rendering are deferred. Narrowed to
//                  "popupmenu", which names the deferred `n_cst_popupmenu` rendering surface.
//      "invoke"    matches `InvokeMethodChannel`, `ColumnExpInvokeMethodEvent`, `func_invoke` - C-04
//                  macro invocation, in scope (AAP 0.4.3 C-04). Narrowed to "scriptinvoker" and
//                  "objectinvoker", the deferred `pfw.utility.invoker` natives.
//      "script"    matches INSIDE `TransactionDeSCRIPTor` and `TOPIC_GRAMMAR_SUBSCRIPTION`. Narrowed
//                  to "powerscript", "scriptinvoker", "objectinvoker", "sciter".
//      "log"       matches `logical_text_width` (the in-scope computed menu-item width),
//                  `logid` and `logpass` (the transaction descriptor). Narrowed to "logger", the
//                  deferred `n_logger`.
//      "globalvar" matches `GlobalVarData`, `VAR_LOCAL`, `VAR_FOREIGN` - the column-expression
//                  `globalvardata` structure (AAP 0.6.2.1), in scope. ScriptBridge's global-variable
//                  access is instead covered by its engine and invoker terms.
//  Two capabilities that LOOK deferred and are genuinely IN SCOPE are likewise not terms - and note
//  that NOT MAKING THEM TERMS is why neither needs an exemption, which is strictly better than
//  exempting them:
//      PINYIN      `pinyinfirstletterlike.srf` is IN SCOPE (AAP 0.4.1), invoked from a DataWindow
//                  filter expression at `n_cst_dwsvc_dropdownsearch.sru:L323`, and its `PY_LIKE_*`
//                  flags are declared at `enums.sru:L1146-L1149`. `dataservices.v1.PinyinLike` is
//                  therefore legitimate, not a Documents leak. Exempting it in the ledger is the
//                  tempting response, and the ledger's own necessity check rejects such an entry,
//                  correctly, because no term matches it - the worked example is recorded at the ledger.
//                  (Also note that "nyi" would match pi-NYI-n, which is why the placeholder
//                  vocabulary does not contain it.)
//      LOCALIZATION The i18n categories are a shared LIBRARY, not a deferred service
//                  (AAP 0.8.3 Deviation 1), so "i18n" and "localization" are not deferred terms.
//  One deferred term is deliberately listed WITHOUT its obvious companion:
//      "compiler"  is a ScriptBridge term (`pfw.utility.compiler`), but "evaluator" is NOT, because
//                  AAP 0.4.2.5 places an in-scope DataWindow expression evaluator in DataServices.
//
//  DISCIPLINE
//  ------------------------------------------------------------------------------------------------
//  * TABLE-DRIVEN. One theory row per reserved route for the positive half; one row per vocabulary
//    term per document for the negative half. A failure names the row, so it says which prohibition
//    was crossed without a debugger.
//  * SHAPE ONLY (C-A). No route is invoked, no host is started, no HTTP is spoken. This suite reads
//    two authored documents and a descriptor graph, and nothing else.
//  * NO BEHAVIOUR IS ASSERTED FOR A RESERVED ROUTE (C-B). They are metadata; there is no
//    implementation to characterise, and asserting runtime behaviour would presuppose one.
//  * PURE. No clock, no randomness, no environment variable, no network, no sleep. Repeatability is
//    the hard prerequisite of the Golden-Master approach this repository adopts (AAP 0.6.7).
//  * NO SECRET LITERAL of any kind, in any form (C-F).
//  * NOT VACUOUS. `BothContractDocumentsAreLoadedAndDeclareRoutesBeforeAnySweepRuns` is the guard:
//    the fixture is specified to THROW rather than hand over an empty document, and this suite is the
//    reason that matters. The vocabulary is the load-bearing input to the negative half - empty it
//    and the negative half passes trivially - so
//    `TheDeferredCapabilityVocabularyIsTheLoadBearingInputAndIsNotEmpty` asserts its size and
//    coverage. ANY EDIT TO THE VOCABULARY IS A CHANGE TO A COMPLIANCE CONTROL.
//
// ==================================================================================================

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Google.Protobuf.Reflection;
using Microsoft.OpenApi;
using Xunit;
using Xunit.Sdk;

namespace PowerFramework.Contracts.Tests;

/// <summary>
/// The auditable control behind constraint C-D: proves that the four deferred services appear in the
/// published contract surface ONLY as Gateway's four reserved <c>501</c> routes, and that nothing
/// anywhere in that surface implements, models or names a deferred capability.
/// </summary>
/// <remarks>
/// <para>
/// The fixture is supplied by xunit.v3 through this constructor parameter. It is registered once for
/// the whole assembly by <c>[assembly: AssemblyFixture(typeof(OpenApiContractDocuments))]</c> at the
/// top of <c>ContractTestContext.cs</c>, which is the single convention for this folder - this class
/// deliberately adds no second registration, no collection definition and no class fixture.
/// </para>
/// <para>
/// Both halves of the suite are described at length in the file banner above. In short: the positive
/// half holds the four reserved routes to the form AAP 0.4.4 permits, and the negative half sweeps a
/// deferred-capability vocabulary across every identifier in all three protocol definitions and both
/// OpenAPI documents to prove nothing beyond those routes exists.
/// </para>
/// </remarks>
/// <param name="documents">
/// The two parsed REST contract documents, loaded once per test assembly. The fixture throws rather
/// than yielding an empty document when a file cannot be located or parsed, which is what stops this
/// suite from ever passing vacuously.
/// </param>
public sealed class ReservedRouteMetadataTests(OpenApiContractDocuments documents)
{
    // ==============================================================================================
    //  THE FOUR RESERVED ROUTES, AS THE DOCUMENT ACTUALLY SPELLS THEM
    //
    //  AAP 0.4.4 writes each reserved extension point as `/v1/<area>/**`. OPENAPI HAS NO `**`
    //  WILDCARD, so the document expresses the catch-all as a TEMPLATED PATH - `/v1/design/{path}` -
    //  with one required path parameter carrying the remainder. That form was read off the document
    //  rather than assumed (`gateway.v1.yaml` paths section), and it is what makes each entry a route
    //  FAMILY rather than a single URL, so a Phase-2 consumer's eventual URL shape is already legible.
    //
    //  The literals below are therefore the document's own path templates, written out in full. They
    //  are deliberately NOT composed from a prefix plus a suffix constant: a consumer hardcodes the
    //  whole template, so this suite should break if any part of it changes, and composition would
    //  hide exactly the half that changed.
    // ==============================================================================================

    private const string DesignRoutePath = "/v1/design/{path}";
    private const string DocumentsRoutePath = "/v1/documents/{path}";
    private const string IntegrationRoutePath = "/v1/integration/{path}";
    private const string ScriptingRoutePath = "/v1/scripting/{path}";

    /// <summary>The four deferred service names, exactly as AAP 0.2.2.2 and docs/DEFERRED.md spell them.</summary>
    private const string DesignSystemService = "DesignSystem";
    private const string DocumentsService = "Documents";
    private const string IntegrationService = "Integration";
    private const string ScriptBridgeService = "ScriptBridge";

    /// <summary>The only outcome a reserved route's handler produces.</summary>
    private const string NotImplementedStatus = "501";

    /// <summary>
    /// The pre-handler refusal an unauthenticated caller receives, declared beside
    /// <see cref="NotImplementedStatus"/> on every reserved operation.
    /// </summary>
    /// <remarks>
    /// It comes from the authentication middleware rather than from any code behind the route, which is
    /// exactly why it was once left undeclared - and why leaving it undeclared was wrong. The reserved
    /// operations require a token, so this status occurs in practice, and a response set that omits it
    /// tells a generated client it cannot.
    /// </remarks>
    private const string UnauthorizedStatus = "401";

    /// <summary>The marker AAP 0.4.4 specifies verbatim.</summary>
    private const string PhaseTwoMarker = "reserved for Phase 2";

    /// <summary>The extension carrying each reserved operation's destination service.</summary>
    private const string DeferredServiceExtension = "x-deferred-service";

    /// <summary>The extension carrying the Phase-2 reservation marker.</summary>
    private const string ReservedMarkerExtension = "x-reserved-marker";

    /// <summary>The media type a machine-readable body must be published under.</summary>
    private const string MachineReadableMediaType = "application/json";

    /// <summary>Friendly document names, used only in failure messages.</summary>
    private const string GatewayDocumentName = "gateway.v1.yaml";
    private const string SecurityDocumentName = "security.v1.yaml";

    /// <summary>
    /// The two schemas that are PERMITTED to carry a deferred service's name as enum data, addressed
    /// by identity. See the file banner: naming a capability area is permitted metadata (AAP 0.4.4);
    /// modelling one is not.
    /// </summary>
    private static readonly string[] SchemasPermittedToNameADeferredService =
    [
        // The 501 body's destination member. Its whole content is the name, which is the point.
        "ReservedRouteBody",

        // The capability projection's Phase-1 destination column, which is the legacy's own
        // decomposition intent read forward onto the service roster (AAP 0.1.4).
        "Capability",
    ];

    // ==============================================================================================
    //  MEMBER DATA - THE POSITIVE HALF, ONE ROW PER RESERVED ROUTE
    // ==============================================================================================

    /// <summary>
    /// The four reserved routes paired with the deferred service each one names, one theory row apiece.
    /// </summary>
    /// <remarks>
    /// THE PAIRING IS THE POINT OF THE TABLE. Asserting only that the four names appear SOMEWHERE in
    /// the document would pass a cross-wired document in which `/v1/design/**` named `Documents` -
    /// a plausible copy-and-paste error, since the four declarations are structurally identical by
    /// design. Each row therefore carries the route and its OWN expected service, and the assertions
    /// check the pairing in both directions: this route names this service, and it names none of the
    /// other three.
    /// </remarks>
    public static TheoryData<string, string> ReservedRoutes =>
        new()
        {
            { DesignRoutePath, DesignSystemService },
            { DocumentsRoutePath, DocumentsService },
            { IntegrationRoutePath, IntegrationService },
            { ScriptingRoutePath, ScriptBridgeService },
        };

    /// <summary>Both published REST contract documents, one theory row apiece, for the sweeps.</summary>
    public static TheoryData<string> ContractDocumentNames =>
        new()
        {
            GatewayDocumentName,
            SecurityDocumentName,
        };

    // ==============================================================================================
    //  THE DEFERRED-CAPABILITY VOCABULARY - THE LOAD-BEARING INPUT TO THE NEGATIVE HALF
    //
    //  Taken from AAP 0.2.2.2 (the deferred library assignments) and AAP 0.4.4 (the capabilities each
    //  reserved route will eventually reach). EVERY TERM IS COMMENTED WITH THE DEFERRED SERVICE THAT
    //  OWNS IT, so a reader can audit the coverage against those two sections line by line, and so a
    //  failure message can name the owning service rather than just the offending word.
    //
    //  MATCHING IS CASE-INSENSITIVE AND BY SUBSTRING, so "font" catches `FontMetrics` and `fontName`
    //  alike, and "blink" subsumes "blinkfast".
    //
    //  TERM SELECTION IS EVIDENCE-DRIVEN, NOT ASPIRATIONAL. Each term below was measured against all
    //  1,456 protobuf identifiers and all identifiers of both OpenAPI documents before being added.
    //  A term that matched an IN-SCOPE symbol was narrowed rather than exempted - the five narrowings
    //  and the two in-scope capabilities that are deliberately not terms are recorded in the file
    //  banner with the symbol that proves each. Narrowing a term is always better than exempting a
    //  symbol, because a narrowed term keeps guarding everything else.
    //
    //  IF YOU EDIT THIS TABLE YOU ARE EDITING A COMPLIANCE CONTROL. Adding a term is safe and
    //  welcome. REMOVING one weakens C-D enforcement and needs the same scrutiny as removing an
    //  assertion, because the negative half passes trivially against an empty vocabulary - which
    //  `TheDeferredCapabilityVocabularyIsTheLoadBearingInputAndIsNotEmpty` exists to prevent.
    // ==============================================================================================

    /// <summary>
    /// One deferred-capability term: the word to sweep for, the deferred service that owns it, and the
    /// legacy library or object it stands for.
    /// </summary>
    /// <param name="Term">The substring to match, case-insensitively, against every identifier.</param>
    /// <param name="DeferredService">
    /// The owning deferred service, so a failure message can name which prohibition was crossed rather
    /// than only which word was found.
    /// </param>
    /// <param name="LegacyCapability">
    /// The legacy anchor - the library or object the term stands for - so a reader can trace the term
    /// back to the full-estate mapping in AAP 0.4.1 rather than take it on trust.
    /// </param>
    private sealed record DeferredTerm(string Term, string DeferredService, string LegacyCapability);

    /// <summary>
    /// The vocabulary itself. Held as a typed array rather than only as theory data so that the
    /// meta-tests guarding it can reason over the rows directly.
    /// </summary>
    private static readonly DeferredTerm[] DeferredTerms =
    [
            // ---------------------------------------------------------------------------------------
            //  DesignSystem - `/v1/design/**`. UI, theming, geometry, colour, DPI, canvas, painter,
            //  font, image, image list, popup menu, tooltip, tray icon, timer, win32 interop, the logo
            //  control, and the presentational halves of ColumnSort, ContextMenu and DropDownSearch.
            //  Legacy: pfw.ui (56 remaining), pfw.ui.controls (28), pfw.ui.controls.ext (38 remaining),
            //  pfw.ui.objects (64), pfw.base::u_logo.sru (1). Capability bits INIT_FLAG_ENABLE_UI (1)
            //  and INIT_FLAG_ENABLE_DPIAWARE (1024) [enums.sru:L41,L47].
            // ---------------------------------------------------------------------------------------
            new("theme", DesignSystemService, "theming; the legacy disables its own with themename = \"Do Not Use Themes\" [ws_objects/pfw.pbl.src/pfw.sra:L25]"),
            new("geometry", DesignSystemService, "the geometry structures of pfw.ui"),
            new("colour", DesignSystemService, "colour functions - British spelling, as AAP 0.4.4 writes it"),
            new("color", DesignSystemService, "colour functions - American spelling, as code would spell it"),
            new("dpi", DesignSystemService, "the DPI conversion family; INIT_FLAG_ENABLE_DPIAWARE = 1024 [enums.sru:L47]"),
            new("px2mm", DesignSystemService, "PX2MMX/PX2MMY, the deferred half of ContextMenu [n_cst_dwsvc_contextmenu.sru:L1241]"),
            new("d2px", DesignSystemService, "D2PX, the deferred half of ContextMenu [n_cst_dwsvc_contextmenu.sru:L1241]"),
            new("canvas", DesignSystemService, "the canvas surface of pfw.ui"),
            new("painter", DesignSystemService, "the painter surface of pfw.ui"),
            new("font", DesignSystemService, "font measurement, n_cst_font [n_cst_dwsvc_contextmenu.sru:L1092]; the in-scope headless half carries logical_text_width instead"),
            new("image", DesignSystemService, "image and image list; subsumes \"imagelist\". Resolving a name to a picture and drawing it is the deferred rendering half"),
            new("popupmenu", DesignSystemService, "n_cst_popupmenu rendering [n_cst_dwsvc_contextmenu.sru:L18]; deliberately narrower than \"menu\", whose item model is in scope"),
            new("tooltip", DesignSystemService, "the tooltip surface of pfw.ui"),
            new("trayicon", DesignSystemService, "the tray icon surface of pfw.ui"),
            new("timer", DesignSystemService, "the timer surface of pfw.ui"),
            new("win32", DesignSystemService, "win32 interop - ShowWindow/GetWindowRect/SetWindowPos [n_cst_dwsvc_dropdownsearch.sru:L256,L474,L489]"),
            new("logo", DesignSystemService, "the logo control, ws_objects/pfw.base.pbl.src/u_logo.sru - the one object split out of an otherwise in-scope library (AAP 0.4.1)"),

            // ---------------------------------------------------------------------------------------
            //  Documents - `/v1/documents/**`. JSON and its four helpers, the XML object family, ZIP,
            //  barcode and QR, file scanning, logging, the date/number conversion set, regular
            //  expressions, device info.
            //  Legacy: pfw.utility.parser (11 remaining), pfw.utility.zip (4), pfw.utility.barcode (2),
            //  pfw.utility (11 remaining), pfw.utility.container::n_list.sru (1),
            //  pfw.utility.regexp (5), pfw.utility.devinfo (2).
            // ---------------------------------------------------------------------------------------
            new("json", DocumentsService, "the JSON object family of pfw.utility.parser - NOT the RFC 7517 JSON Web Key set, which is C-01 verification material and is exempt by identity"),
            new("xml", DocumentsService, "the XML object family, n_xmldoc / n_xmlqueryresult of pfw.utility.parser - NOT the preserved XML_* return codes, which are exempt by identity"),
            new("zip", DocumentsService, "archive handling, pfw.utility.zip"),
            new("barcode", DocumentsService, "barcode generation, pfw.utility.barcode"),
            new("qrcode", DocumentsService, "QR generation, pfw.utility.barcode; spelled in full because bare \"qr\" is two characters and would match noise"),
            new("filescan", DocumentsService, "file scanning, n_filescanner of pfw.utility"),
            new("logger", DocumentsService, "logging, n_logger of pfw.utility; deliberately narrower than \"log\", which matches in-scope logical_text_width, logid and logpass"),
            new("regexp", DocumentsService, "regular expressions, pfw.utility.regexp; the one in-scope use is satisfied by System.Text.RegularExpressions (AAP 0.2.1.4)"),
            new("devinfo", DocumentsService, "device and environment information, pfw.utility.devinfo"),
            new("deviceinfo", DocumentsService, "device and environment information, spelled out"),

            // ---------------------------------------------------------------------------------------
            //  Integration - `/v1/integration/**`. Outbound HTTP and its extensions, FTP, WebSocket,
            //  MQTT, and the pfwx.* transports.
            //  Legacy: pfw.net.http (22), pfw.net.http.ext (4), pfw.net.ftp (3),
            //  pfw.net.websocket (2), pfwx.net.http (7), pfwx.net.mqtt (3), pfwx.base (1),
            //  pfwx.utility.parser (1), pfwx (1).
            // ---------------------------------------------------------------------------------------
            new("http", IntegrationService, "the outbound HTTP client of pfw.net.http - NOT the preserved E_HTTP_ERROR / E_WINHTTP_ERROR return codes, which are exempt by identity"),
            new("ftp", IntegrationService, "FTP, pfw.net.ftp"),
            new("websocket", IntegrationService, "WebSocket, pfw.net.websocket"),
            new("mqtt", IntegrationService, "MQTT, pfwx.net.mqtt - also the location of five of the eight in-source secret sites (AAP 0.6.6.1)"),
            new("pfwx", IntegrationService, "the second-generation pfwx.* transports and the pfwx target itself"),

            // ---------------------------------------------------------------------------------------
            //  ScriptBridge - `/v1/scripting/**`. Sciter and its extensions, MiniBlink, WebView
            //  embedding, the PowerScript compiler and evaluator, dynamic object and script
            //  invocation, and global-variable access.
            //  Legacy: pfw.ui.sciter (15), pfw.ui.sciter.ext (4), pfw.ui.blink (15),
            //  pfw.ui.webview (11), pfw.utility.compiler (2), pfw.utility.invoker (7 remaining).
            //  Capability bits INIT_FLAG_ENABLE_SCITER (2), _BLINK (4), _BLINKFAST (8) and
            //  _WEBVIEW (2048) [enums.sru:L42-L44,L48].
            // ---------------------------------------------------------------------------------------
            new("sciter", ScriptBridgeService, "Sciter engine embedding, pfw.ui.sciter; INIT_FLAG_ENABLE_SCITER = 2 [enums.sru:L42]"),
            new("blink", ScriptBridgeService, "MiniBlink engine embedding, pfw.ui.blink; subsumes both \"blinkfast\" - the alternative build of the same engine [enums.sru:L43-L44] - and \"miniblink\", its brand spelling, so neither is listed separately"),
            new("webview", ScriptBridgeService, "WebView embedding, pfw.ui.webview; INIT_FLAG_ENABLE_WEBVIEW = 2048 [enums.sru:L48]"),
            new("powerscript", ScriptBridgeService, "runtime PowerScript compilation and evaluation, pfw.utility.compiler; deliberately narrower than \"script\", which matches inside TransactionDescriptor and TOPIC_GRAMMAR_SUBSCRIPTION"),
            new("compiler", ScriptBridgeService, "the runtime compiler of pfw.utility.compiler. Its companion \"evaluator\" is deliberately NOT a term: AAP 0.4.2.5 places an IN-SCOPE DataWindow expression evaluator in DataServices, so the word is ambiguous in this codebase"),
            new("scriptinvoker", ScriptBridgeService, "script invocation, n_scriptinvoker of pfw.utility.invoker; not a real in-scope dependency (AAP 0.2.1.4) because C# has native variadic support"),
            new("objectinvoker", ScriptBridgeService, "dynamic object invocation, pfw.utility.invoker; deliberately narrower than \"invoke\", which matches in-scope C-04 macro invocation"),

            // ---------------------------------------------------------------------------------------
            //  THE DEFERRED SERVICES' OWN NAMES AND ROUTE SEGMENTS.
            //
            //  Every row above sweeps for a CAPABILITY. These five sweep for the four services
            //  THEMSELVES, and they are what make the reserved-route entries in the exemption ledger
            //  LOAD-BEARING rather than decorative: without them, `/v1/design/{path}` and
            //  `reservedDesignSystem` would match no term at all, and excluding them by identity
            //  would be a no-op that merely looked like a control.
            //
            //  What they catch is the most direct violation available - a schema called
            //  `DocumentsRequest`, an operation called `getDesignTokens`, a path `/v1/scripting/eval`,
            //  an rpc called `Integration`. AAP 0.4.4 permits the four names to appear as the reserved
            //  routes' OWN identifiers and as enum DATA naming a destination. Anywhere else in an
            //  identifier, a deferred service's name is that service being modelled.
            //
            //  Note the deliberate subsumption and the deliberate pair: "design" already covers
            //  "designsystem", so only the shorter form is listed; "scripting" (the route segment) and
            //  "scriptbridge" (the service name) share no substring, so both are needed.
            // ---------------------------------------------------------------------------------------
            new("design", DesignSystemService, "the deferred service's own name and its /v1/design/** route segment; subsumes \"designsystem\""),
            new("documents", DocumentsService, "the deferred service's own name and its /v1/documents/** route segment"),
            new("integration", IntegrationService, "the deferred service's own name and its /v1/integration/** route segment"),
            new("scripting", ScriptBridgeService, "the /v1/scripting/** route segment of the deferred ScriptBridge service"),
            new("scriptbridge", ScriptBridgeService, "the deferred service's own name, which its route segment does not contain"),
    ];

    /// <summary>The vocabulary as theory data: one row per term for both negative-half sweeps.</summary>
    public static TheoryData<string, string, string> DeferredCapabilityVocabulary
    {
        get
        {
            TheoryData<string, string, string> rows = [];

            foreach (DeferredTerm term in DeferredTerms)
            {
                rows.Add(term.Term, term.DeferredService, term.LegacyCapability);
            }

            return rows;
        }
    }

    // ==============================================================================================
    //  THE EXEMPTION LEDGER
    //
    //  Every entry is a HOLE IN THE CONTROL, so every entry is (a) identity-based, (b) cited, and
    //  (c) liveness-checked by EveryExemptionInTheLedgerStillResolvesToARealContractSymbol so it
    //  cannot go stale and quietly widen.
    //
    //  There are three families and no fourth. Read each and ask the question the AAP asks: could
    //  this hide a genuine deferred implementation? For each one the answer is no, and the reason is
    //  written next to it.
    // ==============================================================================================

    /// <summary>
    /// Protobuf types whose ENTIRE contents are exempt, addressed by exact fully-qualified type name.
    /// A descriptor is covered when its full name is the entry, or begins with the entry plus a dot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A whole-type exemption is used ONLY where the type's every member belongs to the same cited
    /// justification, which is true of both entries: one is a preserved status-code alphabet and the
    /// other is a single in-scope capability with its flag set. Neither can host an implementation,
    /// because an implementation needs an rpc or a service and a type subtree can carry neither.
    /// </para>
    /// </remarks>
    private static readonly string[] ExemptProtoTypeSubtrees =
    [
        // ---------------------------------------------------------------------------------------
        //  `common.v1.XmlParseStatus` - the message, its nested `Value` enum, and all 17 `XML_*`
        //  values. THIS IS THE TRAP IN THE NEGATIVE SWEEP and it is exempted deliberately.
        //
        //  These are PRESERVED RETURN CODES from the read-only behavioural oracle
        //  `ws_objects/pfw.shared.pbl.src/retcode.sru:L84-L100`, which AAP 0.4.2.3 requires ported
        //  IN FULL - it names "the XML_* set" explicitly. They are a numbering scheme and a scope
        //  for it: nothing reads XML, nothing parses XML, and no consumer of this contract can
        //  obtain an XML capability from them. The XML OBJECT FAMILY ITSELF STAYS DEFERRED to
        //  Documents (AAP 0.4.1), and the term "xml" keeps guarding every other identifier.
        //
        //  Exempting the word "xml" everywhere instead would be the harmful move: it would let a
        //  real `XmlDocumentService` through. Conversely, failing on these names would push a later
        //  agent toward DELETING legitimate constants from the preserved catalogue, which is the
        //  exact failure mode AAP 0.4.5.3 warns about.
        // ---------------------------------------------------------------------------------------
        "common.v1.XmlParseStatus",

        // ---------------------------------------------------------------------------------------
        //  NOT LISTED, AND THE REASON IS WORTH RECORDING: `dataservices.v1.PinyinLike`.
        //
        //  Pinyin looks like a Documents capability and is IN SCOPE. AAP 0.4.1 places
        //  `pinyinfirstletterlike.srf` in scope as CONTRIBUTED BEHAVIOUR - it is invoked from inside
        //  a DataWindow filter expression [`n_cst_dwsvc_dropdownsearch.sru:L323`, flags = 7] - even
        //  though its library `pfw.utility` is otherwise deferred to Documents, and its flag
        //  meanings are declared in the read-only oracle at `enums.sru:L1146-L1149`.
        //
        //  EXEMPTING THE TYPE HERE IS THE TEMPTING RESPONSE, AND THE NECESSITY CHECK IN
        //  EveryExemptionInTheLedgerStillResolvesToARealContractSymbol REJECTS IT, correctly:
        //  "pinyin" is not a vocabulary term, so nothing would ever report the type, and an
        //  exemption for something no sweep reports is how a ledger starts reading as a general
        //  allow-list. The right control is the one in place - the term is not in the vocabulary, and
        //  the reason is recorded in the file banner. Left here as the worked example of how
        //  to answer "does this need an exemption?": if no term matches it, the answer is no.
        // ---------------------------------------------------------------------------------------
    ];

    /// <summary>
    /// Individual protobuf descriptors exempt by EXACT fully-qualified name. No prefix matching, no
    /// pattern: a name either is one of these or it is a finding.
    /// </summary>
    private static readonly string[] ExemptProtoFullNames =
    [
        // ---------------------------------------------------------------------------------------
        //  FOUR PRESERVED RETURN CODES from `retcode.sru`, which AAP 0.4.2.3 requires ported in
        //  full. Each happens to contain a deferred term, and each is a RESULT CODE rather than a
        //  capability: a caller can learn that something failed, and can do nothing else with it.
        //  Their in-process twins carry the same spellings in
        //  `PowerFramework.Shared.Kernel/RetCode.cs`, which is why the spellings cannot be changed
        //  to dodge this sweep (AAP 0.4.5.3 - they appear in serialized payloads, log records and
        //  characterization recordings).
        // ---------------------------------------------------------------------------------------
        "common.v1.RetCode.Value.E_INVALID_IMAGE",   // matches "image"  - DesignSystem term
        "common.v1.RetCode.Value.E_WIN32_ERROR",     // matches "win32"  - DesignSystem term
        "common.v1.RetCode.Value.E_HTTP_ERROR",      // matches "http"   - Integration term
        "common.v1.RetCode.Value.E_WINHTTP_ERROR",   // matches "http"   - Integration term

        // ---------------------------------------------------------------------------------------
        //  `dataservices.v1.ContextMenuItem.image` - matches the DesignSystem term "image".
        //
        //  The context menu's ITEM MODEL is the IN-SCOPE headless half (AAP 0.2.1.3 Correction 4):
        //  DataServices ships labels, ids, enabled and split flags and computed logical text widths,
        //  while window geometry, DPI conversion, font measurement and rendering are deferred. This
        //  field carries the image resource NAME AND NOTHING MORE - no bytes, no dimensions, no
        //  scaling factor, no DPI variant - and the proto says so at its declaration. Resolving the
        //  name to a picture and drawing it is precisely the deferred half, and no part of that is
        //  reachable from a string.
        // ---------------------------------------------------------------------------------------
        "dataservices.v1.ContextMenuItem.image",
    ];

    /// <summary>
    /// OpenAPI identifiers exempt by EXACT spelling, together with the document that declares each.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two families. The first is the four reserved routes and their eight operation identifiers -
    /// permitted BY CONSTRUCTION under AAP 0.4.4, and excluded by identity rather than by a pattern
    /// such as "anything starting with reserved", which a real stub could hide behind.
    /// </para>
    /// <para>
    /// The second is the JSON Web Key surface of the Security contract. "JSON Web Key" is the RFC 7517
    /// term for the VERIFICATION MATERIAL that contract C-01 requires Security to publish (AAP 0.4.3
    /// C-01), so that the other three services can validate tokens with the stock bearer handler and
    /// zero bespoke code. It is not the deferred Documents JSON parser, and it carries no JSON
    /// capability: it publishes public key parameters. Note how tight the exemption is - the property
    /// names of both JWK schemas (<c>alg</c>, <c>e</c>, <c>key_ops</c>, <c>kid</c>, <c>kty</c>,
    /// <c>n</c>, <c>use</c>, <c>keys</c>) contain no vocabulary term at all, so matching SIMPLE
    /// identifiers rather than dotted paths keeps this family to four entries instead of a subtree.
    /// </para>
    /// </remarks>
    private static readonly string[] ExemptOpenApiIdentifiers =
    [
        // -------- The four reserved routes: permitted by AAP 0.4.4, excluded by identity. --------
        DesignRoutePath,
        DocumentsRoutePath,
        IntegrationRoutePath,
        ScriptingRoutePath,
        "reservedDesignSystem",
        "reservedDesignSystemPost",
        "reservedDocuments",
        "reservedDocumentsPost",
        "reservedIntegration",
        "reservedIntegrationPost",
        "reservedScriptBridge",
        "reservedScriptBridgePost",

        // -------- C-01 verification material: RFC 7517, not the deferred JSON parser. --------
        "/.well-known/jwks.json",
        "getJsonWebKeySet",
        "JsonWebKey",
        "JsonWebKeySet",

        // -------- The context menu's image resource NAME: the in-scope headless half. --------
        //
        // The REST projection of `dataservices.v1.ContextMenuItem.image`, which is already exempt by
        // exact descriptor name in ExemptProtoFullNames above - read that entry for the full AAP 0.2.1.3
        // Correction 4 position. In short: the item model is the IN-SCOPE headless half, this member
        // carries the image resource NAME and nothing more, and resolving that name to a picture and
        // drawing it is the deferred half.
        //
        // THIS ENTRY IS ONLY REACHABLE BECAUSE THE PAYLOAD SCHEMAS ARE CONCRETE, and that is the point
        // rather than an inconvenience. A document that delegated every projected body to one open
        // `ProtoPayload` schema would give this sweep no property names to inspect on the DataWindow
        // surface at all, so it would pass because there was nothing there - the silent failure mode
        // the file banner's guard exists to catch. The Tier 3 schemas put every projected member in
        // front of it, and this is the one it reports.
        "image",
    ];

    // ==============================================================================================
    //  PLACEHOLDER VOCABULARY - the contract-level analogue of the forbidden throwing class
    //
    //  AAP 0.2.2.2 forbids "no NotImplementedException-throwing class for any of the four". A contract
    //  cannot throw, so the analogue is a DECLARATION that models "the deferred service exists but is
    //  unimplemented" - a message, an enum value, a schema or a documented error that reserves a place
    //  for a capability rather than naming a destination.
    //
    //  These terms are deliberately narrow. "deferred" and "reserved" are NOT among them:
    //  `deferred_accept_pending` and `deferred_accept_queued` are the IN-SCOPE posted deferred-accept
    //  continuation of `se_cst_dw.sru:L387-L393` (AAP 0.4.5.4 - a posted call becomes an explicitly
    //  queued continuation, because a headless container has no Win32 message pump), and `Reserved`
    //  is the tag and component prefix of the four permitted declarations themselves. "nyi" is not
    //  among them either, because it matches pi-NYI-n.
    // ==============================================================================================

    private static readonly string[] PlaceholderVocabulary =
    [
        "notimplement",   // NotImplemented, NotImplementation, notImplementedYet
        "unimplement",    // Unimplemented
        "placeholder",    // the word AAP 0.2.2.2 itself uses for the forbidden artifacts
        "stub",           // the loophole AAP 0.8.1 closes by name: "even to 'stub them out'"
        "comingsoon",     // ComingSoon
        "tobedone",       // ToBeDone / TBD
        "phase2",         // an identifier reserving a slot rather than naming a destination
        "notsupported",   // NotSupported as a modelled outcome; E_NO_SUPPORT is a return code, not this
    ];

    // ==============================================================================================
    //  GUARD - RUN THIS BEFORE BELIEVING ANYTHING ELSE IN THIS FILE
    //
    //  A compliance control that passes because it had nothing to inspect is worse than no control at
    //  all: it reports success and it is wrong. Every sweep below is an assertion of ABSENCE, and an
    //  absence is trivially true of an empty document - so the failure mode is silent by construction.
    //
    //  OpenApiContractDocuments is specified to THROW rather than hand over an empty document when a
    //  file cannot be located or parsed, and this suite is the reason that specification matters. This
    //  test states the dependency explicitly rather than relying on it: if the fixture ever changed to
    //  degrade gracefully, this row would fail while every sweep in the file would still pass.
    // ==============================================================================================

    [Fact]
    public void BothContractDocumentsAreLoadedAndDeclareRoutesBeforeAnySweepRuns()
    {
        OpenApiDocument gateway = documents.Gateway;
        OpenApiDocument security = documents.Security;

        Assert.NotNull(gateway.Paths);
        Assert.NotNull(security.Paths);

        // NON-EMPTY, WHICH IS THE HALF THAT MATTERS. Both documents are large - the gateway contract
        // alone declares dozens of routes - so a count of zero means the fixture handed over a shell.
        Assert.NotEmpty(gateway.Paths);
        Assert.NotEmpty(security.Paths);

        // AND THE COMPONENT SECTIONS THE POSITIVE HALF READS THROUGH ARE PRESENT. The 501 body reaches
        // the reserved routes as a `$ref` into components, so an absent components section would make
        // every body assertion below unreachable rather than false.
        Assert.NotNull(gateway.Components);
        Assert.NotNull(gateway.Components.Schemas);
        Assert.NotNull(gateway.Components.Responses);

        // THE DESCRIPTOR GRAPH IS THE OTHER SWEEP SUBSTRATE, and it is loaded by the runtime rather
        // than by the fixture, so it is checked here too. Three files, and at least one service, so a
        // generation misconfiguration cannot present as a clean sweep.
        Assert.Equal(3, ContractDescriptors.All.Count);
        Assert.NotEmpty(ContractDescriptors.AllServices());
        Assert.NotEmpty(ContractDescriptors.AllDescriptors());
    }

    // ==============================================================================================
    //  POSITIVE HALF - THE FOUR ROUTES EXIST, AND THEY EXIST IN THE PERMITTED FORM
    // ==============================================================================================

    [Theory]
    [MemberData(nameof(ReservedRoutes))]
    public void EachReservedRouteIsDeclaredAsATemplatedCatchAllFamily(string route, string deferredService)
    {
        // C-D CLAUSE DISCHARGED: AAP 0.4.4 requires the four extension points to be DECLARED in
        // Gateway's routing and contract metadata. This is the "declared" half; every other row in the
        // positive half constrains the form of that declaration.
        IOpenApiPathItem pathItem = RequirePath(route);

        // A ROUTE FAMILY, NOT A SINGLE URL.
        //
        // AAP 0.4.4 writes each extension point as `/v1/<area>/**`. OpenAPI has no `**` wildcard, so
        // the document uses a TEMPLATED CATCH-ALL - one required path parameter carrying the remainder.
        // The form was read off the document rather than assumed. Reserving one fixed URL instead would
        // reserve nothing useful and would tell a reader nothing about the eventual surface, which is
        // the only reason the routes are declared at all.
        Assert.NotNull(pathItem.Parameters);
        IOpenApiParameter parameter = Assert.Single(pathItem.Parameters);
        Assert.Equal("path", parameter.Name);
        Assert.Equal(ParameterLocation.Path, parameter.In);
        Assert.True(
            parameter.Required,
            $"Reserved route {route} declares its catch-all parameter as optional. A path template "
                + "parameter is required by definition, and an optional one would make the template "
                + "ambiguous against a sibling route.");

        // BOTH METHODS ARE DECLARED, SO "NOTHING IS IMPLEMENTED HERE" COVERS MORE THAN ONE VERB.
        //
        // The document declares `get` and `post` explicitly and states that every OTHER method answers
        // the same 501. Declaring exactly these two - rather than one, or all nine - is the shape the
        // four share, and `TheFourReservedDeclarationsAreStructurallyUniform` holds them to it.
        Assert.NotNull(pathItem.Operations);
        Assert.Equal(
            [HttpMethod.Get, HttpMethod.Post],
            pathItem.Operations.Keys.OrderBy(static method => method.Method, StringComparer.Ordinal).ToArray());

        // AND THE ROUTE IS TAGGED AS RESERVED, so a generated client groups the eight operations away
        // from the projected surface instead of interleaving them with operations that work.
        foreach ((HttpMethod method, OpenApiOperation operation) in pathItem.Operations)
        {
            Assert.NotNull(operation.Tags);
            OpenApiTagReference tag = Assert.Single(operation.Tags);
            Assert.Equal(
                "Reserved",
                tag.Name,
                StringComparer.Ordinal);

            Assert.False(
                string.IsNullOrWhiteSpace(operation.OperationId),
                $"Reserved operation {method} {route} for {deferredService} declares no operationId. "
                    + "A generated client needs one, and its absence would make the reserved surface "
                    + "unnameable from a consumer's code.");
        }
    }

    [Theory]
    [MemberData(nameof(ReservedRoutes))]
    public void EachReservedRouteDeclaresNotImplementedAndNoSuccessStatusAtAll(string route, string deferredService)
    {
        foreach ((HttpMethod method, OpenApiOperation operation) in OperationsOf(route))
        {
            Assert.NotNull(operation.Responses);

            // C-D CLAUSE DISCHARGED: AAP 0.4.4 - "a named route returning 501 Not Implemented".
            Assert.True(
                operation.Responses.ContainsKey(NotImplementedStatus),
                $"Reserved route {method} {route} for deferred service {deferredService} declares no "
                    + $"{NotImplementedStatus} response. {NotImplementedStatus} is the one outcome AAP "
                    + "0.4.4 permits a reserved route to declare.");

            // NO `2xx` OF ANY KIND - THE SHARPEST AVAILABLE SIGNAL THAT NOTHING IS IMPLEMENTED.
            //
            // A success response, even one described as a placeholder, tells a consumer the route
            // sometimes works, and a code path must exist for it to work. That is exactly the
            // "stub them out" outcome AAP 0.8.1 forbids by name.
            foreach (string status in operation.Responses.Keys)
            {
                Assert.False(
                    status.StartsWith('2'),
                    $"Reserved route {method} {route} declares success status {status}. Nothing is "
                        + $"implemented behind a reserved route, so it can never succeed - and a 2xx "
                        + $"here would mean part of deferred service {deferredService} had been built, "
                        + "which constraint C-D forbids outright.");
            }

            // AND 501 IS THE ONLY HANDLER RESULT, WHICH IS THE STATEMENT THIS CONTROL ACTUALLY NEEDS
            // TO MAKE.
            //
            // A reserved route answers unconditionally, for every method and every path remainder, so
            // any status describing an EVALUATED outcome - a 400, a 404, a 409 - would say the route
            // examines the request before answering, and any 2xx would say part of a deferred service
            // had been built. Neither may appear.
            //
            // 401 IS THE ONE PERMITTED COMPANION, AND EXCLUDING IT IS THE DEFECT THIS ASSERTION EXISTS
            // TO CATCH. Asserting the set is EXACTLY {501} is the tempting reading, on the reasoning that
            // the 401 comes from the authentication middleware and is therefore not a response the
            // ROUTE produces. That is true about where the refusal originates and wrong about what the
            // contract owes a consumer: these operations DO require a token - Endpoints/
            // DeferredCapabilityEndpoints.cs calls RequireAuthorization on every one of them under a
            // document-level bearer requirement - so an unauthenticated caller receives 401 in
            // practice, and a response set that omits it tells a generated client and a conformance
            // tool that the status cannot occur. Declaring it costs nothing about
            // unconditionality: 501 remains the only outcome any handler produces.
            string[] declared = [.. operation.Responses.Keys.Order(StringComparer.Ordinal)];

            Assert.Equal([UnauthorizedStatus, NotImplementedStatus], declared);
        }
    }

    [Fact]
    public void TheUnauthenticatedOutcomeIsDeclaredOnceAsACrossCuttingConventionRatherThanLeftUnstated()
    {
        // THE OTHER HALF OF THE ROW ABOVE, AND WITHOUT IT THAT ROW WOULD BE ENFORCING A HALF-TRUTH.
        //
        // The reserved operations declare exactly one status, and that is correct: the route evaluates
        // nothing, so a second declared status would say otherwise. But the endpoint layer requires
        // authorization on every one of them, so an anonymous caller observably receives 401 and never
        // reaches the 501 - which the end-to-end suite asserts from the outside. A contract that
        // asserted a single-status result set with nothing anywhere to reconcile it against that 401
        // would leave a consumer entitled to conclude the status was impossible on these paths.
        //
        // The reconciliation is declared ONCE, machine-readably, at document level - not restated on
        // forty-six operations, and specifically not on these eight where it would misattribute a
        // scheme outcome to a route. This row pins that the declaration exists and says the four things
        // a consumer needs: which status, that the scheme produces it, that it is evaluated before the
        // handler, and that it is not enumerated on every operation.
        Assert.NotNull(documents.Gateway.Extensions);

        Assert.True(
            documents.Gateway.Extensions!.TryGetValue("x-cross-cutting-responses", out var declaration),
            "gateway.v1.yaml declares no document-level 'x-cross-cutting-responses'. The reserved "
                + "operations declare exactly one response each, so without this declaration the "
                + "contract states a single-status result set for paths that observably answer 401 "
                + "first, and nothing anywhere reconciles the two.");

        // The reader materializes an unrecognised extension as a JSON node wrapper, so the node itself
        // is what carries the declaration - `ToString()` on the wrapper yields its type name.
        JsonNodeExtension node = Assert.IsType<JsonNodeExtension>(declaration);
        string serialized = node.Node.ToJsonString();

        foreach (string required in (string[])
            ["401", "bearerAuth", "route-handler", "declaredPerOperation"])
        {
            Assert.Contains(required, serialized, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheIngressLimiterStatusIsReconciledAtDocumentLevelRatherThanAddedToAReservedResponseSet()
    {
        // THE SECOND CROSS-CUTTING STATUS, AND THE ROW THAT KEEPS ITS OMISSION HONEST.
        //
        // Gateway now bounds its ingress - it is this system's first-ever listener, so a caller able to
        // exhaust it is a failure mode the decomposition itself created rather than a legacy behaviour
        // (AAP 0.1.4). The limiter is GLOBAL middleware, not a per-path policy, so it runs before every
        // route handler in the document except the exempt `/health`, and a reserved path can therefore
        // observably answer 429.
        //
        // WHY IT IS NOT DECLARED ON THE EIGHT RESERVED OPERATIONS, WHICH IS THE JUDGEMENT THIS ROW
        // RECORDS. The two rows above pin their response set closed at exactly {401, 501}, and
        // gateway.v1.yaml states the rule they enforce: any 4xx there other than that 401 would say the
        // route inspects the request before answering. A reserved family inspects nothing. The 401 earns
        // its place because the OPERATION carries `.RequireAuthorization()`, the requirement that
        // produces it; the limiter carries no per-operation declaration to earn the same standing. C-D
        // is a hard AAP exclusion and outranks a publication preference, so the status is reconciled
        // machine-readably at document level - which is exactly what the extension exists for - instead
        // of growing a set whose closedness IS the deferred-service compliance position.
        //
        // Without this row the omission would be indistinguishable from having forgotten to publish the
        // limiter at all, which is the SEC-05 finding itself.
        Assert.NotNull(documents.Gateway.Extensions);

        Assert.True(
            documents.Gateway.Extensions!.TryGetValue("x-cross-cutting-responses", out var declaration),
            "gateway.v1.yaml declares no document-level 'x-cross-cutting-responses', so the ingress "
                + "limiter's 429 is reconciled nowhere. The four reserved families deliberately omit it "
                + "from their closed response sets, and this declaration is the only thing that "
                + "distinguishes that omission from never having published the limit.");

        JsonNodeExtension node = Assert.IsType<JsonNodeExtension>(declaration);
        string serialized = node.Node.ToJsonString();

        // THE FOUR THINGS A CONSUMER OF A RESERVED PATH NEEDS: which status, what produces it, that it
        // is evaluated before the handler, and that it is NOT enumerated on the reserved families.
        foreach (string required in (string[])
            ["429", "ingress-rate-limiter", "route-handler", "reserved"])
        {
            Assert.Contains(required, serialized, StringComparison.Ordinal);
        }

        // AND THE EXEMPTION IS NAMED, because `/health` is the one path that genuinely cannot answer
        // 429 - the limiter skips it so a saturated service still reports its own readiness, which is
        // what the compose health gate depends on.
        Assert.Contains("health", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(ReservedRoutes))]
    public void NoReservedRouteAcceptsARequestBodyOrAnyInputBeyondItsPathTemplate(string route, string deferredService)
    {
        foreach ((HttpMethod method, OpenApiOperation operation) in OperationsOf(route))
        {
            // C-D CLAUSE DISCHARGED, AND THIS IS THE SUBTLEST OF THE THREE.
            //
            // A request schema IS a model of the deferred capability: it says what the service would
            // accept, which is design work on a forbidden target and is generatable by a consumer even
            // with no handler anywhere. AAP describes these four as "structurally identical 501 Not
            // Implemented declarations with no request schema, no 2xx response", and the absence is the
            // assertion. `null` rather than "present but empty" is what is required, because an empty
            // request body object would still appear in a generated client's signature.
            Assert.Null(operation.RequestBody);

            // NO OPERATION-LEVEL PARAMETERS EITHER.
            //
            // The single catch-all parameter is declared once at PATH level and shared by both
            // methods - which is why it is asserted there rather than here. An operation-level
            // parameter would be input modelling by another route: a query filter or a header would
            // describe how the deferred capability is to be addressed. Null and empty are both
            // acceptable, since either means nothing was modelled.
            Assert.True(
                operation.Parameters is null || operation.Parameters.Count == 0,
                $"Reserved route {method} {route} declares {operation.Parameters?.Count} "
                    + $"operation-level parameter(s). The only permitted input is the path template's "
                    + $"catch-all, declared once at path level; anything further models how deferred "
                    + $"service {deferredService} would be addressed, which constraint C-D forbids.");
        }
    }

    [Theory]
    [MemberData(nameof(ReservedRoutes))]
    public void TheNotImplementedBodyIsMachineReadableRatherThanProse(string route, string deferredService)
    {
        foreach ((HttpMethod method, OpenApiOperation operation) in OperationsOf(route))
        {
            IOpenApiResponse response = operation.Responses![NotImplementedStatus];

            // MACHINE-READABLE IS A REQUIREMENT, NOT A COURTESY.
            //
            // AAP 0.4.4 requires "a machine-readable body naming the deferred service it will
            // eventually reach and the marker 'reserved for Phase 2'". A description alone would
            // satisfy neither half of the reason the routes exist: a client could not branch on it, and
            // a reader would have to guess which service was meant. So a JSON media type carrying a
            // resolvable SCHEMA is asserted - not merely a non-empty `description`.
            Assert.NotNull(response.Content);
            Assert.True(
                response.Content.ContainsKey(MachineReadableMediaType),
                $"The {NotImplementedStatus} response of {method} {route} publishes no "
                    + $"'{MachineReadableMediaType}' media type. AAP 0.4.4 requires the body naming "
                    + $"deferred service {deferredService} to be machine-readable.");

            IOpenApiSchema? schema = response.Content[MachineReadableMediaType].Schema;
            Assert.NotNull(schema);

            // THE STRUCTURED MEMBERS A CLIENT BRANCHES ON. Named individually rather than counted, so
            // that removing one fails here instead of shifting a number nobody reads.
            Assert.NotNull(schema.Properties);
            // `service` AND `deferredService` ARE BOTH REQUIRED, AND BOTH ARE ASSERTED. They carry the
            // identical value. `service` is the member v1 published first; `deferredService` is the
            // unambiguous spelling added later, when `service` was also removed - which silently broke
            // every v1 consumer reading it under a version number promising nothing had changed. Naming
            // both here is what stops that happening again, in either direction.
            foreach (string member in (string[])["status", "service", "deferredService", "marker", "route", "retCode"])
            {
                Assert.True(
                    schema.Properties.ContainsKey(member),
                    $"The {NotImplementedStatus} body of {route} declares no '{member}' member. Without "
                        + "it the body is not machine-readable in the sense AAP 0.4.4 requires.");

                Assert.NotNull(schema.Required);
                Assert.Contains(member, schema.Required);
            }

            // CLOSED, so an unrecognised field in a 501 body is a detectable error rather than data a
            // client silently ignores. An open body would also be the natural place for a future agent
            // to smuggle capability detail in without touching the schema.
            Assert.False(
                schema.AdditionalPropertiesAllowed,
                $"The {NotImplementedStatus} body of {route} is open to unknown members. A reserved "
                    + "route has exactly one outcome with exactly five members, so an open body can "
                    + "only invite capability detail that constraint C-D forbids.");

            // THE THREE CONSTANTS. A reserved route answers unconditionally, so each of these is a
            // constant rather than an open value - which is what lets a conformance test and a client
            // both match on them. `-2001` is the legacy framework's own `E_NO_IMPLEMENTATION`
            // [ws_objects/pfw.shared.pbl.src/retcode.sru:L78] rather than a vocabulary invented for
            // this document, so a client that already branches on the return-code algebra needs no new
            // case. Microsoft.OpenApi 2.11.0 models `const` as a string, hence the string comparands.
            Assert.Equal(NotImplementedStatus, schema.Properties["status"].Const);
            Assert.Equal(PhaseTwoMarker, schema.Properties["marker"].Const);
            Assert.Equal("-2001", schema.Properties["retCode"].Const);
        }
    }

    [Theory]
    [MemberData(nameof(ReservedRoutes))]
    public void EachReservedRouteNamesItsOwnDeferredServiceAndNamesNoOther(string route, string deferredService)
    {
        // C-D CLAUSE DISCHARGED: AAP 0.4.4 - the body must name "the deferred service IT WILL
        // EVENTUALLY REACH". The pairing, not merely the presence of four names somewhere.
        //
        // WHY THE PAIRING NEEDS ITS OWN ASSERTION. The four declarations are structurally identical by
        // design, which makes them a natural copy-and-paste, which makes a cross-wire - `/v1/design/**`
        // announcing `Documents` - a real and plausible error rather than a hypothetical one. A document
        // in that state would satisfy every other row in this file: four routes, four 501s, four
        // machine-readable bodies, all four names present. It would still be wrong, and it would send a
        // Phase-2 consumer to the wrong service.
        //
        // WHERE THE PAIRING LIVES. The 501 body schema is SHARED by all four routes - one
        // `ReservedRouteBody` reached by `$ref` - and its `deferredService` member therefore enumerates
        // all four names by construction. That is correct and is asserted exactly elsewhere. The
        // PER-ROUTE pairing is consequently carried by the routing metadata: the `x-deferred-service`
        // extension, which the body's member name deliberately mirrors so the wire member and the
        // routing metadata read the same.
        string[] otherDeferredServices =
            ((string[])[DesignSystemService, DocumentsService, IntegrationService, ScriptBridgeService])
                .Where(candidate => !string.Equals(candidate, deferredService, StringComparison.Ordinal))
                .ToArray();

        foreach ((HttpMethod method, OpenApiOperation operation) in OperationsOf(route))
        {
            Assert.Equal(deferredService, StringExtension(operation, DeferredServiceExtension));

            // THE HUMAN-READABLE HALF HAS TO AGREE WITH THE MACHINE-READABLE HALF.
            //
            // A consumer reads the summary; a generator reads the extension. If they disagreed, one of
            // the two audiences would be misled, and the disagreement would be invisible to a test that
            // only checked one of them.
            Assert.Contains(deferredService, operation.Summary ?? string.Empty, StringComparison.Ordinal);
            Assert.Contains(deferredService, operation.Description ?? string.Empty, StringComparison.Ordinal);

            // AND IT NAMES NONE OF THE OTHER THREE, which is the half that actually catches a
            // cross-wire. Checked over summary and description together, because that is where a stray
            // name would survive a review: an extension is one word and gets noticed, a paragraph does
            // not.
            string prose = (operation.Summary ?? string.Empty) + "\n" + (operation.Description ?? string.Empty);

            foreach (string other in otherDeferredServices)
            {
                Assert.False(
                    prose.Contains(other, StringComparison.Ordinal),
                    $"Reserved route {method} {route} is declared for deferred service "
                        + $"{deferredService} but its prose also names {other}. Each reserved route "
                        + "reaches exactly one deferred service (AAP 0.4.4); naming a second one either "
                        + "cross-wires the destination or begins to describe a capability boundary that "
                        + "constraint C-D forbids modelling.");
            }

            // NOTHING BEHIND THE ROUTE, STATED AS AN ASSERTION.
            //
            // Every projected operation in this document names the gRPC method it projects and the two
            // protobuf messages that define its payload. A reserved route names NONE of the three,
            // because there is nothing to name: it has no handler beyond the constant response, it
            // calls nothing, and it can reach nothing. If any of these extensions ever appeared here,
            // something behind the route would have been built - which is the whole of what C-D forbids.
            Assert.Null(StringExtension(operation, "x-grpc-method"));
            Assert.Null(StringExtension(operation, "x-proto-request"));
            Assert.Null(StringExtension(operation, "x-proto-response"));
        }
    }

    [Theory]
    [MemberData(nameof(ReservedRoutes))]
    public void EachReservedRouteCarriesThePhaseTwoReservationMarker(string route, string deferredService)
    {
        foreach ((HttpMethod method, OpenApiOperation operation) in OperationsOf(route))
        {
            // C-D CLAUSE DISCHARGED: AAP 0.4.4 - "and the marker 'reserved for Phase 2'".
            //
            // EXACT ON THE MACHINE-READABLE SIDE. The extension is compared ordinally against the
            // literal the plan specifies, because a client and a conformance test both match on it and
            // a paraphrase would break both. The same literal is a `const` on the body's `marker`
            // member, asserted in TheNotImplementedBodyIsMachineReadableRatherThanProse, so the marker
            // appears identically in the routing metadata and on the wire.
            Assert.Equal(PhaseTwoMarker, StringExtension(operation, ReservedMarkerExtension));

            // CASE-INSENSITIVE ON THE PROSE SIDE, AND DELIBERATELY SO.
            //
            // AAP 0.4.4 permits the marker to be carried "in the response schema, its example, or the
            // operation description", and prose legitimately varies its capitalisation - the document
            // opens each description with "**Reserved for Phase 2. Not implemented.**", capital R,
            // while the constant is lower-case. Matching the RESERVATION VOCABULARY rather than the
            // exact string is therefore correct here and only here: the two tokens are asserted
            // separately so that "reserved" alone, or "Phase 2" alone, is not enough.
            string prose = (operation.Summary ?? string.Empty) + "\n" + (operation.Description ?? string.Empty);

            Assert.Contains("reserved", prose, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("phase 2", prose, StringComparison.OrdinalIgnoreCase);

            // AND THE PROSE SAYS OUT LOUD THAT IT IS NOT IMPLEMENTED.
            //
            // This is the statement a human reader needs, and it is the one a partial implementation
            // would have to delete in order to become plausible - which makes its presence a useful
            // tripwire rather than decoration.
            Assert.Contains("not implemented", prose, StringComparison.OrdinalIgnoreCase);

            Assert.False(
                string.IsNullOrWhiteSpace(operation.Description),
                $"Reserved route {method} {route} for {deferredService} carries no description. The "
                    + "description is where AAP 0.4.4's reservation marker and the statement that "
                    + "nothing exists behind the route are addressed to a human reader.");
        }
    }

    [Fact]
    public void TheFourReservedDeclarationsAreStructurallyUniform()
    {
        // C-D CLAUSE DISCHARGED, AND THIS IS THE ROW THAT CATCHES CREEP EARLIEST.
        //
        // AAP describes the four as STRUCTURALLY IDENTICAL declarations, and gateway.v1.yaml states in
        // its own comments that the symmetry is part of the compliance position rather than a tidiness
        // preference: the four differ ONLY in the path segment and in the deferred service they name.
        //
        // The consequence is that ASYMMETRY IS EVIDENCE. If one of the four ever acquires more shape
        // than its siblings - a request schema, a second status, an extra parameter, a third method,
        // another extension - that difference IS capability modelling starting, and it will show up
        // here before any single-route assertion notices, because each single-route row only knows what
        // it was told to look for while this row compares the four against each other.
        //
        // The comparison is done by reducing each route to a canonical SHAPE SIGNATURE with the
        // route-specific parts removed, then asserting all four signatures are the same string. A
        // string comparison is used on purpose: the failure message prints both shapes, so the reader
        // sees exactly which structural element diverged.
        string[] signatures = ReservedRoutePaths
            .Select(route => BuildShapeSignature(route))
            .ToArray();

        Assert.Equal(4, signatures.Length);

        string reference = signatures[0];

        for (int index = 1; index < signatures.Length; index++)
        {
            Assert.Equal(reference, signatures[index]);
        }

        // AND ALL FOUR SHARE ONE BODY SCHEMA RATHER THAN FOUR COPIES OF IT.
        //
        // One shared `ReservedForPhaseTwo` response means the four bodies cannot drift apart at all,
        // which is a stronger guarantee than four identical declarations that happen to agree today.
        // It also means a change to the reserved body is a single, reviewable edit.
        IOpenApiResponse[] bodies = ReservedRoutePaths
            .SelectMany(route => OperationsOf(route))
            .Select(entry => entry.Operation.Responses![NotImplementedStatus])
            .ToArray();

        Assert.Equal(8, bodies.Length);

        Assert.Single(
            bodies
                .Select(static body => body.Description)
                .Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public void TheContractsOpenApiFolderHoldsExactlyTheTwoInScopeDocumentsAndNoDeferredOne()
    {
        // C-D CLAUSE DISCHARGED: the four deferred services receive NO CONTRACT of their own. A
        // published document for one of them would be a partial implementation of exactly the kind AAP
        // 0.2.2.2 forbids, because a consumer could generate a working client against it - the document
        // would be the design work, whether or not a service ever answered.
        //
        // The folder actually resolved by the fixture is inspected, rather than a path composed here,
        // so this row cannot pass by looking somewhere else. Two REST contracts exist and only two:
        // C-01/C-02 on Security and C-09/C-10 on Gateway. The other eight contracts are gRPC and live
        // in the protocol definitions. A third YAML in this folder is therefore either an undocumented
        // REST contract or a stray file, and both are findings.
        string openApiFolder = Path.GetDirectoryName(documents.GatewayDocumentPath)!;

        Assert.True(
            Directory.Exists(openApiFolder),
            $"The folder the fixture resolved the gateway document from does not exist: "
                + $"'{openApiFolder}'.");

        // Read-only inspection only: this suite never creates, writes, moves or deletes anything, and
        // it never reaches outside shared/PowerFramework.Contracts/OpenApi (constraint C-C).
        string[] documentFileNames = Directory.GetFiles(openApiFolder)
            .Select(static path => Path.GetFileName(path))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [OpenApiContractDocuments.GatewayDocumentFileName, OpenApiContractDocuments.SecurityDocumentFileName],
            documentFileNames);

        // NO SUBFOLDER EITHER, which is where a per-service document would most plausibly be filed.
        Assert.Empty(Directory.GetDirectories(openApiFolder));
    }

    [Fact]
    public void TheReservedRoutesAreTheOnlyPlaceEitherDocumentDeclaresNotImplemented()
    {
        // C-D CLAUSE DISCHARGED - THE CONTRACT-LEVEL ANALOGUE OF THE FORBIDDEN THROWING CLASS.
        //
        // AAP 0.2.2.2 forbids a `NotImplementedException`-throwing placeholder class. A contract cannot
        // throw, so the analogue is a DECLARED not-implemented outcome: any operation that publishes a
        // 501 is saying "this exists but does nothing yet", and that is a stub in contract form.
        //
        // Exactly eight operations may say it - the four reserved routes' `get` and `post`. If an
        // IN-SCOPE operation ever declared a 501, some in-scope surface would have become a placeholder,
        // which is the same prohibition read from the other direction.
        (string Document, string Route, string Method, string? OperationId)[] declarers =
        [
            .. AllOperations(documents.Gateway, GatewayDocumentName)
                .Concat(AllOperations(documents.Security, SecurityDocumentName))
                .Where(static entry => entry.Operation.Responses?.ContainsKey(NotImplementedStatus) == true)
                .Select(static entry => (entry.Document, entry.Route, entry.Method.Method, entry.Operation.OperationId)),
        ];

        Assert.Equal(8, declarers.Length);

        foreach ((string document, string route, string method, string? operationId) in declarers)
        {
            Assert.True(
                ReservedRoutePaths.Contains(route, StringComparer.Ordinal),
                $"{document} declares a {NotImplementedStatus} response on {method} {route} "
                    + $"(operationId '{operationId}'), which is not one of the four reserved routes. A "
                    + "declared not-implemented outcome on an in-scope operation is a stub in contract "
                    + "form, and AAP 0.2.2.2 forbids a placeholder for a deferred capability in any "
                    + "form.");

            Assert.Equal(GatewayDocumentName, document);
        }
    }

    // ==============================================================================================
    //  NEGATIVE HALF - NOTHING BEYOND THE FOUR ROUTES EXISTS
    //
    //  This is the half that makes the file evidence rather than documentation. The positive half proves
    //  the permitted representation is present and correctly shaped; only this half can prove that the
    //  permitted representation is the ONLY representation.
    // ==============================================================================================

    [Theory]
    [MemberData(nameof(ContractDocumentNames))]
    public void EachContractDocumentContributesIdentifiersToTheSweep(string documentName)
    {
        // THE PER-DOCUMENT HALF OF THE ANTI-VACUOUS GUARANTEE, AND IT CLOSES A REAL GAP.
        //
        // The OpenAPI sweep walks BOTH documents inside a single theory row per term, which reads well
        // and reports a cross-document leak once instead of twice. It also means that if the identifier
        // walk over ONE document silently yielded nothing - a components section that moved, an
        // enumerator that stopped early on a shape it did not expect - that document's half of the sweep
        // would enforce nothing while every row stayed green, and the assembly-wide guard would not
        // notice because the other document still produced findings-free output for the right reason.
        //
        // So each document is asserted to contribute a substantial identifier set, in all four swept
        // kinds. The floors are deliberately far below the measured counts: the assertion is "this walk
        // is working", not "this document has exactly N names", and a floor that tracked reality would
        // have to be edited every time a contract legitimately grew.
        OpenApiDocument document = DocumentNamed(documentName);

        ContractIdentifier[] identifiers = [.. AllIdentifiers(document, documentName)];

        Assert.True(
            identifiers.Length >= 100,
            $"The identifier walk over {documentName} yielded only {identifiers.Length} identifiers. "
                + "Both C-D sweeps assert an ABSENCE, so a walk that returns little or nothing makes "
                + "this document's half of the compliance control pass without enforcing anything.");

        // ALL FOUR SWEPT KINDS ARE PRESENT. A walk that produced only paths would still clear the count
        // floor above while never looking at a single schema or property name - which is where a
        // deferred payload type would actually be modelled.
        string[] kinds = [.. identifiers.Select(static identifier => identifier.Kind).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

        Assert.Equal(["operationId", "path", "property", "schema"], kinds);
    }

    [Theory]
    [MemberData(nameof(DeferredCapabilityVocabulary))]
    public void NoPublishedProtobufIdentifierNamesADeferredCapability(
        string term,
        string deferredService,
        string legacyCapability)
    {
        // C-D CLAUSE DISCHARGED: "no service definition, message or method implements any deferred
        // capability". The sweep covers every KIND of identifier, not just message names, because a
        // FIELD or a ONEOF named after a deferred capability would be just as much a partial definition
        // of it - and it would be far easier to miss in review.
        //
        // ContractDescriptors.FindDescriptorsMentioning is the documented C-D compliance instrument:
        // it walks every message (including messages nested at any depth), every field, every oneof,
        // every enum, every enum value, every service and every method across all three protocol
        // definitions, and matches case-insensitively against each descriptor's FULL name - so a
        // package or an enclosing message carrying the term is caught too.
        //
        // MATCHING THE FULL NAME IS WHY WHOLE-TYPE EXEMPTIONS ARE THE RIGHT SHAPE for the two entries
        // in ExemptProtoTypeSubtrees: a nested member of an exempt type inherits the type's name and
        // would otherwise be reported once per member.
        IDescriptor[] findings = ContractDescriptors.FindDescriptorsMentioning(term)
            .Where(static descriptor => !IsExemptProtoDescriptor(descriptor))
            .ToArray();

        Assert.True(
            findings.Length == 0,
            BuildProtoSweepFailureMessage(term, deferredService, legacyCapability, findings));
    }

    [Theory]
    [MemberData(nameof(DeferredCapabilityVocabulary))]
    public void NoProtobufIdentifierModelsADeferredCapabilityAsPresentButUnimplemented(
        string term,
        string deferredService,
        string legacyCapability)
    {
        // The placeholder sweep, run once per vocabulary row so that a hit can be attributed. It looks
        // for the CONTRACT-LEVEL analogue of the forbidden `NotImplementedException` class: a
        // descriptor whose name pairs a deferred capability with a not-implemented word - a
        // `SciterStub`, an `UnimplementedWebViewCall`, a `HTTP_NOT_IMPLEMENTED`.
        //
        // Either half alone is legitimate somewhere in this contract set, which is why the pairing is
        // the subject: `E_NO_IMPLEMENTATION` is a preserved return code and carries no capability, and
        // the exempt symbols named in the ledger carry a capability word without any placeholder sense.
        // Only the two TOGETHER describe a deferred service that exists but does nothing.
        string[] findings = ContractDescriptors.FindDescriptorsMentioning(term)
            .Where(static descriptor => !IsExemptProtoDescriptor(descriptor))
            .Select(static descriptor => descriptor.FullName)
            .Where(static fullName => PlaceholderVocabulary.Any(
                placeholder => Normalise(fullName).Contains(placeholder, StringComparison.Ordinal)))
            .ToArray();

        Assert.True(
            findings.Length == 0,
            $"The published protocol definitions declare {findings.Length} identifier(s) that pair "
                + $"deferred capability '{term}' (owned by {deferredService} - {legacyCapability}) with "
                + $"a not-implemented marker: {string.Join(", ", findings)}. AAP 0.2.2.2 forbids a "
                + "placeholder for a deferred service in any form; the four reserved Gateway routes' "
                + $"own {NotImplementedStatus} is the only permitted not-implemented declaration in the "
                + "system.");
    }

    [Theory]
    [MemberData(nameof(DeferredCapabilityVocabulary))]
    public void NoOpenApiIdentifierNamesADeferredCapabilityInEitherDocument(
        string term,
        string deferredService,
        string legacyCapability)
    {
        // C-D CLAUSE DISCHARGED, on the REST half of the boundary. Both documents are swept in one row
        // per term rather than one row per term per document, so a term that leaks into both is reported
        // once with both locations - which reads better than two half-findings.
        //
        // FOUR IDENTIFIER KINDS ARE SWEPT: paths, operation identifiers, schema names and property
        // names. Those are the four places where MODELLING appears. Enum VALUES are string data and are
        // deliberately out of scope here - see the file banner for the principle, and
        // NoSchemaOutsideTheTwoPermittedCataloguesCarriesADeferredServiceNameAsEnumData for the control
        // that closes that door by LOCATION instead of by exemption.
        //
        // MATCHING IS ON THE SIMPLE IDENTIFIER, NOT ON A DOTTED PATH. That is deliberate and it is what
        // keeps the exemption ledger to four entries for the whole JSON Web Key surface: a property of
        // an exempt schema is judged on its own name, so `JsonWebKey.alg` is not reported merely
        // because its container is called JsonWebKey - while a property genuinely named `sciterVersion`
        // inside that same schema would still be caught.
        ContractIdentifier[] findings =
        [
            .. AllIdentifiers(documents.Gateway, GatewayDocumentName)
                .Concat(AllIdentifiers(documents.Security, SecurityDocumentName))
                .Where(identifier => identifier.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                .Where(static identifier => !ExemptOpenApiIdentifiers.Contains(identifier.Name, StringComparer.Ordinal))
                .DistinctBy(static identifier => identifier.ToString(), StringComparer.Ordinal),
        ];

        Assert.True(
            findings.Length == 0,
            BuildOpenApiSweepFailureMessage(term, deferredService, legacyCapability, findings));
    }

    [Fact]
    public void NoSchemaOutsideTheTwoPermittedCataloguesCarriesADeferredServiceNameAsEnumData()
    {
        // THE ENUM-VALUE DOOR, CLOSED BY LOCATION RATHER THAN BY EXEMPTION.
        //
        // AAP 0.4.4 makes naming a deferred capability area PERMITTED metadata - it is what makes the
        // eventual system legible from Gateway's contract, and it is the only reason the reserved routes
        // exist. So a deferred service NAME appearing as enum data is not itself a violation. What would
        // be a violation is that name appearing somewhere that models a capability: an operation's
        // request enum, a payload discriminator, a target selector.
        //
        // Rather than exempt the permitted occurrences term by term - which would put a sixteen-entry
        // blanket in the ledger - this row states the far tighter property: exactly two schemas in the
        // whole contract surface may carry a deferred service's name as enum data, addressed BY
        // IDENTITY, and both are catalogues rather than capability models.
        //
        //   ReservedRouteBody   the 501 body's `service` and `deferredService` members, which carry the
        //                       identical value under the originally published name and the unambiguous
        //                       one. Their entire content is the name; AAP 0.4.4 requires it.
        //   Capability          the `/v1/capabilities` projection's `phaseOneDestination` column, which
        //                       records which Phase-1 destination each preserved legacy capability bit
        //                       maps to. AAP 0.1.4 reads that bitmask as the legacy framework's own
        //                       decomposition intent, and mapping it onto the roster is corroboration
        //                       that the Phase-1 slice is drawn correctly.
        string[] deferredServiceNames =
            [DesignSystemService, DocumentsService, IntegrationService, ScriptBridgeService];

        List<string> findings = [];

        foreach ((string documentName, OpenApiDocument document) in
            (ValueTuple<string, OpenApiDocument>[])
            [
                (GatewayDocumentName, documents.Gateway),
                (SecurityDocumentName, documents.Security),
            ])
        {
            foreach ((string schemaName, IOpenApiSchema schema) in document.Components?.Schemas
                ?? new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal))
            {
                if (SchemasPermittedToNameADeferredService.Contains(schemaName, StringComparer.Ordinal))
                {
                    continue;
                }

                foreach ((string context, IOpenApiSchema inlineSchema) in InlineSchemas(schema, schemaName))
                {
                    string[] named = (inlineSchema.Enum ?? [])
                        .Select(static node => node?.ToString() ?? string.Empty)
                        .Where(value => deferredServiceNames.Contains(value, StringComparer.Ordinal))
                        .ToArray();

                    if (named.Length != 0)
                    {
                        findings.Add($"{documentName} {context} -> {string.Join("/", named)}");
                    }
                }
            }
        }

        Assert.True(
            findings.Count == 0,
            $"{findings.Count} schema location(s) outside the two permitted catalogues "
                + $"({string.Join(", ", SchemasPermittedToNameADeferredService)}) carry a deferred "
                + $"service name as enum data: {string.Join("; ", findings)}. Naming a deferred "
                + "capability area is permitted metadata (AAP 0.4.4), but only where it names a "
                + "DESTINATION; an enum elsewhere selects or discriminates, which begins to model the "
                + "capability that constraint C-D forbids.");
    }

    // ==============================================================================================
    //  THE CONTROLS ON THE CONTROL - what stops this suite from decaying into a no-op
    // ==============================================================================================

    [Fact]
    public void TheDeferredCapabilityVocabularyIsTheLoadBearingInputAndIsNotEmpty()
    {
        // WHY THIS ROW EXISTS.
        //
        // Both sweeps assert an ABSENCE, and an absence is trivially true of an empty vocabulary. Empty
        // the table and every negative row above goes green while enforcing nothing at all - a failure
        // mode that is silent by construction and would survive review, because the tests would still
        // be there and would still pass.
        //
        // THE VOCABULARY IS THEREFORE THE LOAD-BEARING INPUT TO THIS COMPLIANCE CONTROL, and any edit
        // to it is an edit to the control. Adding a term is safe. Removing one needs the same scrutiny
        // as deleting an assertion, and shrinking the table below its current size will fail here.
        //
        // The theory data is projected from DeferredTerms, so the projection is checked too - a theory
        // that silently produced fewer rows than the table declares would be the same failure by
        // another route.
        Assert.Equal(DeferredTerms.Length, DeferredCapabilityVocabulary.Count);

        // A FLOOR RATHER THAN AN EXACT COUNT, so that adding a term never requires editing this number
        // - the direction of change that strengthens the control must never be the inconvenient one.
        Assert.True(
            DeferredTerms.Length >= 44,
            $"The deferred-capability vocabulary has shrunk to {DeferredTerms.Length} terms. Both sweeps "
                + "assert an absence, so they pass trivially against a smaller table: removing a term "
                + "silently weakens C-D enforcement. Restore it, or justify the removal the way you "
                + "would justify deleting an assertion.");

        // ALL FOUR DEFERRED SERVICES ARE COVERED, WITH REAL DEPTH EACH.
        //
        // A total count alone could be satisfied by thirty-nine DesignSystem terms and nothing for the
        // other three, which would leave three quarters of the prohibition unenforced. So coverage is
        // asserted per service, and each is required to carry several terms rather than a token one.
        Dictionary<string, int> termsPerService = DeferredTerms
            .GroupBy(static term => term.DeferredService, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);

        foreach (string service in
            (string[])[DesignSystemService, DocumentsService, IntegrationService, ScriptBridgeService])
        {
            Assert.True(
                termsPerService.TryGetValue(service, out int count) && count >= 5,
                $"Deferred service {service} is covered by only "
                    + $"{(termsPerService.TryGetValue(service, out int found) ? found : 0)} vocabulary "
                    + "term(s). All four deferred services must be swept with real depth; AAP 0.8.1 is "
                    + "explicit that discovery rigor is not to be relaxed on the deferred majority, and "
                    + "the same applies to the control that enforces the deferral.");
        }

        // EVERY ROW IS FULLY POPULATED. A term with no owning service could not produce an attributable
        // failure message, and a term with no legacy anchor could not be audited back to AAP 0.4.1.
        Assert.All(
            DeferredTerms,
            static term =>
            {
                Assert.False(string.IsNullOrWhiteSpace(term.Term));
                Assert.False(string.IsNullOrWhiteSpace(term.DeferredService));
                Assert.False(string.IsNullOrWhiteSpace(term.LegacyCapability));
            });

        // NO DUPLICATES AND NO REDUNDANT SUBSUMPTION. A term that contains another term matches
        // strictly less than the shorter one and adds nothing but a second identical failure - and the
        // deliberate subsumptions ("image" over "imagelist", "blink" over "blinkfast") are handled by
        // NOT listing the longer form, which this keeps honest.
        string[] terms = [.. DeferredTerms.Select(static term => term.Term)];

        Assert.Equal(terms.Length, terms.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (string term in terms)
        {
            string[] subsumed = terms
                .Where(other => !string.Equals(other, term, StringComparison.OrdinalIgnoreCase)
                    && other.Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            Assert.True(
                subsumed.Length == 0,
                $"Vocabulary term '{term}' subsumes {string.Join(", ", subsumed)}, which therefore add "
                    + "nothing: a substring match on the shorter term already covers them. Drop the "
                    + "longer form, or narrow the shorter one if it is matching too much.");
        }
    }

    [Fact]
    public void EveryExemptionInTheLedgerStillResolvesToARealContractSymbol()
    {
        // THE LEDGER IS THE MOST DANGEROUS PART OF THIS FILE, SO IT IS ITSELF UNDER TEST.
        //
        // Every exemption is a hole in the control. The specific way an exemption list decays is that a
        // symbol is renamed or removed while its exemption stays behind - and a stale exemption is not
        // merely dead weight, it is a hole with nothing visible in it, which is exactly where a future
        // symbol can land without anybody deciding to allow it.
        //
        // So each entry must still match something in the contract TODAY. When an entry stops resolving,
        // this row fails and forces the ledger to be PRUNED rather than left to widen. That is what
        // keeps "narrow" a property of the ledger over time instead of a property of the day it was
        // written.
        foreach (string subtreeRoot in ExemptProtoTypeSubtrees)
        {
            Assert.True(
                ContractDescriptors.AllDescriptors().Any(descriptor =>
                    string.Equals(descriptor.FullName, subtreeRoot, StringComparison.Ordinal)),
                $"Exempt protobuf type '{subtreeRoot}' no longer exists in the published protocol "
                    + "definitions. A stale exemption is a hole in the C-D control with nothing visible "
                    + "in it: prune the entry rather than leaving it to cover something it was never "
                    + "reviewed for.");

            // AND AN EXEMPT SUBTREE MAY NOT GROW A CAPABILITY INSIDE ITSELF.
            //
            // This closes the one residual hole in a whole-type exemption, and it is worth spelling out
            // because the hole is not obvious. Members nested inside an exempt type inherit the
            // exemption, so a `message XmlParseStatus { message ParseRequest { string document = 1; } }`
            // would be swept and then excluded - a deferred capability modelled in the one place nothing
            // is looking.
            //
            // The exempt type is therefore held to the shape its justification depends on: a PURE CODE
            // SPACE. Zero fields, so it carries no data and can appear in no payload; zero nested
            // messages, so no request or response type can be filed under it; only nested enums, which
            // is what a preserved status alphabet is made of. Growing it past that fails here, which
            // forces the addition to be reviewed on its own merits instead of inheriting a clearance
            // granted to something else.
            MessageDescriptor? exemptMessage = ContractDescriptors.AllMessages()
                .FirstOrDefault(message => string.Equals(message.FullName, subtreeRoot, StringComparison.Ordinal));

            if (exemptMessage is null)
            {
                continue;
            }

            Assert.True(
                exemptMessage.Fields.InDeclarationOrder().Count == 0,
                $"Exempt protobuf type '{subtreeRoot}' has acquired "
                    + $"{exemptMessage.Fields.InDeclarationOrder().Count} field(s). A whole-type "
                    + "exemption is justified only for a pure code space that carries no data; a field "
                    + "makes the type appear in a payload while inheriting an exemption granted to a "
                    + "status alphabet.");

            Assert.True(
                exemptMessage.NestedTypes.Count == 0,
                $"Exempt protobuf type '{subtreeRoot}' has acquired "
                    + $"{exemptMessage.NestedTypes.Count} nested message(s), which would inherit its "
                    + "exemption. Declare them outside the exempt type so the C-D sweep can see them.");

            Assert.NotEmpty(exemptMessage.EnumTypes);
        }

        foreach (string fullName in ExemptProtoFullNames)
        {
            Assert.True(
                ContractDescriptors.AllDescriptors().Any(descriptor =>
                    string.Equals(descriptor.FullName, fullName, StringComparison.Ordinal)),
                $"Exempt protobuf descriptor '{fullName}' no longer exists. Prune the ledger entry.");
        }

        ContractIdentifier[] openApiIdentifiers =
        [
            .. AllIdentifiers(documents.Gateway, GatewayDocumentName)
                .Concat(AllIdentifiers(documents.Security, SecurityDocumentName)),
        ];

        foreach (string identifier in ExemptOpenApiIdentifiers)
        {
            Assert.True(
                openApiIdentifiers.Any(candidate =>
                    string.Equals(candidate.Name, identifier, StringComparison.Ordinal)),
                $"Exempt OpenAPI identifier '{identifier}' no longer appears in either contract "
                    + "document. Prune the ledger entry.");
        }

        // AND EVERY EXEMPTION IS STILL NECESSARY - it must be matched by a vocabulary term, otherwise
        // it is exempting something nothing was going to report. An unnecessary exemption is how a
        // ledger starts to read as a general allow-list rather than as a list of explained collisions.
        string[] terms = [.. DeferredTerms.Select(static term => term.Term)];

        foreach (string entry in ExemptProtoTypeSubtrees.Concat(ExemptProtoFullNames).Concat(ExemptOpenApiIdentifiers))
        {
            Assert.True(
                terms.Any(term => entry.Contains(term, StringComparison.OrdinalIgnoreCase)),
                $"Ledger entry '{entry}' is matched by no vocabulary term, so it exempts something no "
                    + "sweep would have reported. Remove it: an exemption that is not needed reads as a "
                    + "general allow-list and invites the next one to be added without a reason.");
        }
    }

    // ==============================================================================================
    //  PRIVATE - the sweep substrate, the exemption predicate, and the failure messages
    //
    //  The failure messages are a deliberate part of the product here. This suite exists to be read by
    //  someone auditing a compliance position, and a bare "Assert.Empty() Failure" would tell that
    //  reader nothing about which prohibition was crossed or where.
    // ==============================================================================================

    /// <summary>One identifier found in an OpenAPI document, with enough context to be actionable.</summary>
    /// <param name="Document">Which of the two contract documents declares it.</param>
    /// <param name="Kind">What kind of identifier it is: path, operationId, schema or property.</param>
    /// <param name="Name">
    /// The SIMPLE identifier, which is what the vocabulary is matched against - a property is judged on
    /// its own name rather than on its container's, so an exempt container cannot shelter a
    /// capability-named member inside it.
    /// </param>
    /// <param name="Context">Where it sits, for the reader. Never matched against.</param>
    private sealed record ContractIdentifier(string Document, string Kind, string Name, string Context)
    {
        /// <inheritdoc/>
        public override string ToString() =>
            string.Create(
                CultureInfo.InvariantCulture,
                $"{Document} {Kind} '{Name}' at {Context}");
    }

    /// <summary>The four reserved path templates, in the order the theory table declares them.</summary>
    private static string[] ReservedRoutePaths =>
        [DesignRoutePath, DocumentsRoutePath, IntegrationRoutePath, ScriptingRoutePath];

    /// <summary>Resolves one of the two contract documents from the friendly name a theory row carries.</summary>
    /// <remarks>
    /// Theory rows carry strings rather than <see cref="OpenApiDocument"/> instances because xunit.v3
    /// prints a row's arguments in the test name, and a document's ToString would make every row
    /// unreadable. An unrecognised name is a defect in the calling row, not a finding about a contract,
    /// so it fails immediately with both accepted spellings named.
    /// </remarks>
    private OpenApiDocument DocumentNamed(string documentName) => documentName switch
    {
        GatewayDocumentName => documents.Gateway,
        SecurityDocumentName => documents.Security,
        _ => throw FailException.ForFailure(
            $"'{documentName}' is not one of the two published REST contract documents. Expected "
                + $"'{GatewayDocumentName}' or '{SecurityDocumentName}'."),
    };

    /// <summary>
    /// Fetches a reserved path item, failing with a message that lists the declared routes when the
    /// route is absent.
    /// </summary>
    /// <remarks>
    /// A missing reserved route is a C-D finding in its own right - the four extension points are
    /// required to be declared (AAP 0.4.4) - so it is reported as an assertion failure that names what
    /// was sought, rather than surfacing as a <see cref="KeyNotFoundException"/> from an indexer.
    /// </remarks>
    private IOpenApiPathItem RequirePath(string route)
    {
        OpenApiDocument document = documents.Gateway;

        Assert.NotNull(document.Paths);

        if (document.Paths.TryGetValue(route, out IOpenApiPathItem? pathItem))
        {
            return pathItem;
        }

        // Thrown rather than reported through Assert.Fail so the compiler can see that control flow
        // ends here and the method needs no unreachable trailing statement. FailException is the same
        // exception Assert.Fail raises, so this still surfaces as an ASSERTION FAILURE - a finding about
        // the contract - rather than as an unexpected error, which is the distinction
        // ContractTestContext.cs draws for the same reason.
        throw FailException.ForFailure(
            $"The gateway contract declares no route '{route}'. AAP 0.4.4 requires all four reserved "
                + "extension points to be declared as routing metadata, because that is what makes the "
                + "shape of the eventual system legible from this contract. Declared routes under "
                + $"'/v1/': {string.Join(", ", document.Paths.Keys.Where(static declared => declared.StartsWith("/v1/", StringComparison.Ordinal)).OrderBy(static declared => declared, StringComparer.Ordinal))}.");
    }

    /// <summary>Every (method, operation) pair declared on one reserved route.</summary>
    private IEnumerable<(HttpMethod Method, OpenApiOperation Operation)> OperationsOf(string route)
    {
        IOpenApiPathItem pathItem = RequirePath(route);

        Assert.NotNull(pathItem.Operations);
        Assert.NotEmpty(pathItem.Operations);

        // Ordered so that a failure quotes the same operation first on every run - determinism in the
        // report matters as much as determinism in the result (AAP 0.6.7).
        return pathItem.Operations
            .OrderBy(static entry => entry.Key.Method, StringComparer.Ordinal)
            .Select(static entry => (entry.Key, entry.Value));
    }

    /// <summary>Every (route, method, operation) triple in a document, flattened and ordered.</summary>
    private static IEnumerable<(string Document, string Route, HttpMethod Method, OpenApiOperation Operation)> AllOperations(
        OpenApiDocument document,
        string documentName)
    {
        if (document.Paths is null)
        {
            yield break;
        }

        foreach ((string route, IOpenApiPathItem pathItem) in document.Paths
            .OrderBy(static entry => entry.Key, StringComparer.Ordinal))
        {
            if (pathItem.Operations is null)
            {
                continue;
            }

            foreach ((HttpMethod method, OpenApiOperation operation) in pathItem.Operations
                .OrderBy(static entry => entry.Key.Method, StringComparer.Ordinal))
            {
                yield return (documentName, route, method, operation);
            }
        }
    }

    /// <summary>
    /// Every path, operation identifier, component schema name and property name in a document - the
    /// four identifier kinds the C-D sweep covers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why these four and no more.</b> They are the places where a deferred capability would have to
    /// be MODELLED to be usable by a consumer: a URL to call, a name to call it by, a payload type, and
    /// a member of that payload. Enum values, descriptions, examples and media types carry string data
    /// rather than structure, and AAP 0.4.4 explicitly permits a deferred capability area to be NAMED -
    /// see the file banner, and see
    /// <see cref="NoSchemaOutsideTheTwoPermittedCataloguesCarriesADeferredServiceNameAsEnumData"/> for
    /// the control that governs enum data by location.
    /// </para>
    /// <para>
    /// <b>Reference proxies terminate the walk.</b> Microsoft.OpenApi resolves a <c>$ref</c> into a
    /// proxy that transparently exposes its target's members, so walking through one would revisit
    /// component schemas that are enumerated in their own right and could cycle on a self-referential
    /// schema. Inline structure only is walked, and a visited set guards the recursion regardless.
    /// </para>
    /// </remarks>
    private static IEnumerable<ContractIdentifier> AllIdentifiers(OpenApiDocument document, string documentName)
    {
        foreach ((string _, string route, HttpMethod method, OpenApiOperation operation) in
            AllOperations(document, documentName))
        {
            yield return new ContractIdentifier(documentName, "path", route, route);

            if (!string.IsNullOrEmpty(operation.OperationId))
            {
                yield return new ContractIdentifier(
                    documentName,
                    "operationId",
                    operation.OperationId,
                    $"{method.Method} {route}");
            }
        }

        if (document.Components?.Schemas is null)
        {
            yield break;
        }

        foreach ((string schemaName, IOpenApiSchema schema) in document.Components.Schemas
            .OrderBy(static entry => entry.Key, StringComparer.Ordinal))
        {
            yield return new ContractIdentifier(
                documentName,
                "schema",
                schemaName,
                $"components.schemas.{schemaName}");

            foreach ((string context, IOpenApiSchema inlineSchema) in InlineSchemas(schema, schemaName))
            {
                if (inlineSchema.Properties is null)
                {
                    continue;
                }

                foreach ((string propertyName, IOpenApiSchema _) in inlineSchema.Properties
                    .OrderBy(static entry => entry.Key, StringComparer.Ordinal))
                {
                    yield return new ContractIdentifier(
                        documentName,
                        "property",
                        propertyName,
                        $"{context}.{propertyName}");
                }
            }
        }
    }

    /// <summary>
    /// A schema and every schema inlined within it - properties, array items, additional properties and
    /// the composition keywords - depth-first, stopping at <c>$ref</c> proxies.
    /// </summary>
    private static IEnumerable<(string Context, IOpenApiSchema Schema)> InlineSchemas(
        IOpenApiSchema root,
        string rootContext)
    {
        HashSet<object> visited = new(ReferenceEqualityComparer.Instance);
        Stack<(string Context, IOpenApiSchema Schema)> pending = new();

        pending.Push((rootContext, root));

        while (pending.Count != 0)
        {
            (string context, IOpenApiSchema schema) = pending.Pop();

            // A reference proxy re-exposes a component that is enumerated on its own account, so
            // following it would duplicate findings and could cycle. Recorded as a deliberate stop
            // rather than an omission.
            if (schema is OpenApiSchemaReference || !visited.Add(schema))
            {
                continue;
            }

            yield return (context, schema);

            if (schema.Properties is not null)
            {
                foreach ((string propertyName, IOpenApiSchema property) in schema.Properties)
                {
                    pending.Push(($"{context}.{propertyName}", property));
                }
            }

            if (schema.Items is not null)
            {
                pending.Push(($"{context}[]", schema.Items));
            }

            if (schema.AdditionalProperties is not null)
            {
                pending.Push(($"{context}{{}}", schema.AdditionalProperties));
            }

            foreach ((string keyword, IList<IOpenApiSchema>? composed) in
                (ValueTuple<string, IList<IOpenApiSchema>?>[])
                [
                    ("allOf", schema.AllOf),
                    ("anyOf", schema.AnyOf),
                    ("oneOf", schema.OneOf),
                ])
            {
                if (composed is null)
                {
                    continue;
                }

                for (int index = 0; index < composed.Count; index++)
                {
                    pending.Push(($"{context}.{keyword}[{index}]", composed[index]));
                }
            }
        }
    }

    /// <summary>
    /// Reduces one reserved route to a canonical shape signature with its route-specific parts removed,
    /// so the four can be compared against each other as strings.
    /// </summary>
    /// <remarks>
    /// Everything structural goes in; nothing route-specific does. The deferred service name, the
    /// operation identifiers, the path segment, the summary and the description are all excluded,
    /// because those are the parts AAP permits the four to differ in. What remains - methods, response
    /// statuses, request-body presence, parameter shape, extension keys, tags, media types and body
    /// members - is precisely the surface that must be identical, so any divergence in it prints as a
    /// string diff a reader can act on.
    /// </remarks>
    private string BuildShapeSignature(string route)
    {
        IOpenApiPathItem pathItem = RequirePath(route);
        StringBuilder signature = new();

        signature.Append(CultureInfo.InvariantCulture, $"pathParameters={pathItem.Parameters?.Count ?? 0}");

        foreach (IOpenApiParameter parameter in pathItem.Parameters ?? [])
        {
            signature.Append(CultureInfo.InvariantCulture, $"|param:{parameter.Name}/{parameter.In}/required={parameter.Required}");
        }

        foreach ((HttpMethod method, OpenApiOperation operation) in OperationsOf(route))
        {
            signature.Append(CultureInfo.InvariantCulture, $"|{method.Method}");
            signature.Append(CultureInfo.InvariantCulture, $":statuses={string.Join(",", ((IEnumerable<string>?)operation.Responses?.Keys ?? []).OrderBy(static status => status, StringComparer.Ordinal))}");
            signature.Append(CultureInfo.InvariantCulture, $":requestBody={operation.RequestBody is not null}");
            signature.Append(CultureInfo.InvariantCulture, $":operationParameters={operation.Parameters?.Count ?? 0}");
            signature.Append(CultureInfo.InvariantCulture, $":security={(operation.Security is null ? "inherited" : operation.Security.Count.ToString(CultureInfo.InvariantCulture))}");
            signature.Append(CultureInfo.InvariantCulture, $":extensions={string.Join(",", ((IEnumerable<string>?)operation.Extensions?.Keys ?? []).OrderBy(static key => key, StringComparer.Ordinal))}");
            signature.Append(CultureInfo.InvariantCulture, $":tags={string.Join(",", ((IEnumerable<OpenApiTagReference>?)operation.Tags ?? []).Select(static tag => tag.Name).OrderBy(static name => name, StringComparer.Ordinal))}");

            IOpenApiResponse response = operation.Responses![NotImplementedStatus];
            signature.Append(CultureInfo.InvariantCulture, $":mediaTypes={string.Join(",", ((IEnumerable<string>?)response.Content?.Keys ?? []).OrderBy(static key => key, StringComparer.Ordinal))}");

            IOpenApiSchema? body = response.Content?[MachineReadableMediaType].Schema;
            signature.Append(CultureInfo.InvariantCulture, $":bodyMembers={string.Join(",", ((IEnumerable<string>?)body?.Properties?.Keys ?? []).OrderBy(static key => key, StringComparer.Ordinal))}");
            signature.Append(CultureInfo.InvariantCulture, $":bodyClosed={body?.AdditionalPropertiesAllowed == false}");
        }

        return signature.ToString();
    }

    /// <summary>
    /// Reads a string-valued specification extension, returning <see langword="null"/> when it is absent.
    /// </summary>
    /// <remarks>
    /// Microsoft.OpenApi 2.x models an unrecognised extension as a <see cref="JsonNodeExtension"/>
    /// wrapping a <see cref="JsonNode"/>, so the value is reached through the node rather than off a
    /// typed property. A non-string node yields <see langword="null"/> rather than throwing, because a
    /// wrongly-typed extension is a finding for the assertion to report, not an error for the helper to
    /// raise.
    /// </remarks>
    private static string? StringExtension(OpenApiOperation operation, string name)
    {
        if (operation.Extensions is null
            || !operation.Extensions.TryGetValue(name, out IOpenApiExtension? extension)
            || extension is not JsonNodeExtension node
            || node.Node is not JsonValue value)
        {
            return null;
        }

        return value.TryGetValue(out string? text) ? text : null;
    }

    /// <summary>
    /// Whether a protobuf descriptor is covered by the exemption ledger, by exact full name or as a
    /// member of an exempt type.
    /// </summary>
    /// <remarks>
    /// Both forms are identity-based. The subtree form matches the type itself or a name that continues
    /// past it at a DOT BOUNDARY, so <c>common.v1.XmlParseStatusExtras</c> would not be covered by an
    /// exemption for <c>common.v1.XmlParseStatus</c> - a prefix test without the dot would have quietly
    /// admitted exactly the kind of neighbour a stub would be filed under.
    /// </remarks>
    private static bool IsExemptProtoDescriptor(IDescriptor descriptor)
    {
        string fullName = descriptor.FullName;

        if (ExemptProtoFullNames.Contains(fullName, StringComparer.Ordinal))
        {
            return true;
        }

        foreach (string subtreeRoot in ExemptProtoTypeSubtrees)
        {
            if (string.Equals(fullName, subtreeRoot, StringComparison.Ordinal)
                || fullName.StartsWith(subtreeRoot + ".", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Lower-cases a name and strips separators, so word-boundary spellings all compare alike.</summary>
    /// <remarks>
    /// The three protocol definitions spell field and enum-value names in <c>snake_case</c> while
    /// messages, services and methods use <c>PascalCase</c>, so <c>NOT_IMPLEMENTED</c>,
    /// <c>not_implemented</c> and <c>NotImplemented</c> must all match the single placeholder term
    /// <c>notimplement</c>. Invariant lower-casing is used because these are ASCII protocol identifiers,
    /// not user text, and a culture-sensitive fold would make the result depend on the machine.
    /// </remarks>
    private static string Normalise(string name) =>
        name.ToLowerInvariant().Replace("_", string.Empty, StringComparison.Ordinal);

    /// <summary>Builds the failure text for a protobuf vocabulary hit.</summary>
    private static string BuildProtoSweepFailureMessage(
        string term,
        string deferredService,
        string legacyCapability,
        IReadOnlyList<IDescriptor> findings)
    {
        StringBuilder text = new();

        text.Append(CultureInfo.InvariantCulture, $"The published protocol definitions declare {findings.Count} identifier(s) naming deferred capability '{term}'.");
        text.Append(CultureInfo.InvariantCulture, $" That capability belongs to the DEFERRED service {deferredService} ({legacyCapability}).");
        text.Append(" Constraint C-D (AAP 0.2.2.2, 0.7.3) forbids implementing the four deferred services even partially and even to \"stub them out\", and AAP 0.4.4 permits exactly one representation of them anywhere in the system: Gateway's four reserved routes returning ");
        text.Append(CultureInfo.InvariantCulture, $"{NotImplementedStatus}.");
        text.Append(" A message, field, oneof, enum, enum value, service or method named after a deferred capability is a partial DEFINITION of a forbidden target even with no handler behind it, because a consumer can generate code against it.");
        text.Append(" Offending symbol(s), by full name:");

        foreach (IDescriptor descriptor in findings)
        {
            text.Append(CultureInfo.InvariantCulture, $" [{descriptor.GetType().Name} {descriptor.FullName}]");
        }

        text.Append(" If one of these is genuinely in scope, add an identity-based entry to the exemption ledger in this file WITH the AAP citation that justifies it - never widen a term or delete an assertion to silence a hit you cannot explain.");

        return text.ToString();
    }

    /// <summary>Builds the failure text for an OpenAPI vocabulary hit.</summary>
    private static string BuildOpenApiSweepFailureMessage(
        string term,
        string deferredService,
        string legacyCapability,
        IReadOnlyList<ContractIdentifier> findings)
    {
        StringBuilder text = new();

        text.Append(CultureInfo.InvariantCulture, $"The published REST contract documents declare {findings.Count} identifier(s) naming deferred capability '{term}'.");
        text.Append(CultureInfo.InvariantCulture, $" That capability belongs to the DEFERRED service {deferredService} ({legacyCapability}).");
        text.Append(" Constraint C-D (AAP 0.2.2.2, 0.7.3) forbids implementing the four deferred services even partially, and AAP 0.4.4 permits only Gateway's four reserved ");
        text.Append(CultureInfo.InvariantCulture, $"{NotImplementedStatus} routes - which are excluded from this sweep by identity, not by pattern.");
        text.Append(" Offending identifier(s):");

        foreach (ContractIdentifier finding in findings)
        {
            text.Append(CultureInfo.InvariantCulture, $" [{finding}]");
        }

        text.Append(" A routing declaration is not a stub, and neither is a schema: if any of these is genuinely in scope, add an identity-based entry to the exemption ledger in this file with its AAP citation.");

        return text.ToString();
    }
}
