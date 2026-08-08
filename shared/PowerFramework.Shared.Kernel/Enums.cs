// ==============================================================================================
//  Enums - the PowerFramework framework-wide constant catalogue
//  --------------------------------------------------------------------------------------------
//  PORTED FROM    ws_objects/pfw.shared.pbl.src/enums.sru (1,162 lines, 723 constants)
//  ORACLE STATUS  That .sru is the ONLY specification for these values, and it is READ ONLY: it
//                 is the behavioural oracle for parity testing, never an edit target. Every
//                 region below therefore carries the enums.sru line range it was taken from, so
//                 any single value can be adjudicated against the source without guesswork.
//
//  WHAT THE LEGACY OBJECT IS, ENUMERATED
//  --------------------------------------------------------------------------------------------
//      enums.sru:L4, L8       global type enums from nonvisualobject
//      enums.sru:L10          global enums enums          <- a global auto-instance whose name
//                                                            shadows its own type name
//      enums.sru:L12-L1152    type variables ... end variables - ONE block, holding all 723
//      enums.sru:L38          Public:                     - the sole access specifier
//      enums.sru:L1153-L1161  on create / on destroy      - TriggerEvent only, no state to port
//
//  That inventory is exhaustive. There is no method, no event script, no instance variable and no
//  native binding anywhere in the object, so this file is a pure data transcription and every
//  member below is either one of the 723 constants or an annotated comment. There is no third
//  category.
//
//  COUNTS, VERIFIED RATHER THAN ASSUMED
//  --------------------------------------------------------------------------------------------
//  723 constants, in the type distribution the source declares: 505 PB Long, 179 PB Ulong,
//  34 PB Uint, 5 PB String. A case-SENSITIVE grep of the source for '^Constant ' returns 720
//  because the three Pinyin declarations at enums.sru:L1147-L1149 are written in lowercase
//  ('constant long'); the correct figure is the case-insensitive 723. Each region banner states
//  its own count, so a transcription slip localises to one region instead of hiding in a total.
//
//  TYPE MAPPING - PB INTEGER WIDTHS ARE NOT C# INTEGER WIDTHS
//  --------------------------------------------------------------------------------------------
//      PB Long   (32-bit SIGNED)    -> C# long     widening, always safe, and it is the mapping
//                                                  the transformation plan's type table specifies
//      PB Ulong  (32-bit UNSIGNED)  -> C# uint     EXACT width, deliberately not long
//      PB Uint   (16-bit UNSIGNED)  -> C# ushort   EXACT width, deliberately not uint
//      PB String                    -> C# string
//
//  The two unsigned mappings are exact rather than uniformly widened for a reason that is
//  load-bearing, not stylistic. The STATE_* and REGEXP_* families are PB Ulong bitmasks consumed
//  through the Bits.* helpers in this same assembly, and those helpers are authored against
//  32-bit uint; if the catalogue widened them to long, every mask test would either fail to
//  compile or need a cast at each call site. Two range checks confirm the mapping is lossless in
//  both directions: LOG_LEVEL_ALL = 4294967295 [enums.sru:L1033] is 0xFFFFFFFF, which fits uint
//  exactly and does not fit a signed 32-bit type at all; and the largest PB Uint value in the
//  file is CRYPTO_RSA_BITS_4096 = 4096 [enums.sru:L967], comfortably inside ushort.
//
//  DECISION 1 - const, NEVER enum
//  --------------------------------------------------------------------------------------------
//  The source declares 'Constant <Type>', which maps to C# 'const'. An enum was considered and
//  rejected on three independent grounds, any one of which is sufficient:
//
//      ADDITIVE BITMASKS      STATE_*, SC_STATE_*, XML_PARSE_*, REGEXP_*, LOG_LEVEL_* and the
//                             INIT_FLAG_ENABLE_* set are combined with bitwise operators, and
//                             several members are themselves sums of other members.
//      DUPLICATE VALUES       HTTP_ENCODING_UTF16 == HTTP_ENCODING_UTF16LE == 2, GB2312 == GBK,
//                             ISO88591 == LATIN1, WS_E_CLOSE_HANDSHAKE_TIMEOUT ==
//                             WS_E_INVALID_PORT == 23, and the deliberately overlapped
//                             BARCODE_OPT_* slots. A C# enum cannot carry two names for one value
//                             without losing the value-to-name round trip.
//      MIXED WIDTHS           one enum cannot span long, uint, ushort and string.
//
//  DECISION 2 - THE GLOBAL AUTO-INSTANCE AT enums.sru:L10 IS NOT REPRODUCED
//  --------------------------------------------------------------------------------------------
//  'global enums enums' declares an auto-instantiated global variable whose name shadows its own
//  type name. That is legal only because PowerBuilder has a single flat global namespace with no
//  import statements, where symbol resolution follows the ordering of the library list in the
//  target file [pfw.pbt:LibList]. It is the same collision the transformation plan records for
//  'global n_sql n_sql', and the resolution is the same one PfwException.cs applied to its own
//  auto-instance: the TYPE keeps the descriptive name and the shared global instance is NOT
//  recreated. Concretely, this class is 'static', so it cannot be instantiated, has no
//  constructor, and exposes no singleton, no Instance and no Current. Callers reach every value
//  as 'Enums.NAME'.
//
//  DECISION 3 - IDENTIFIER SPELLINGS ARE PRESERVED VERBATIM
//  --------------------------------------------------------------------------------------------
//  Every constant keeps its original SCREAMING_SNAKE spelling, which deliberately departs from
//  C# naming convention. The reason is not deference to the original author: these exact
//  spellings travel in serialized payloads, in log records and in characterization recordings,
//  where a rename would silently invalidate every stored comparison.
//
//  The mechanism that makes this legal is FILE-GLOB SCOPED, and it matters that it stays scoped.
//  The repository-root .editorconfig carries a section for this exact path setting CA1707 and
//  IDE1006 to 'none', and this file plus RetCode.cs are the only two files in this project inside
//  that scope. Directory.Build.props sets TreatWarningsAsErrors, so the same identifier spelling
//  in any other file of this project would be a build ERROR - which is the intended behaviour.
//  There is deliberately NO '#pragma warning disable' and NO project-wide NoWarn here: either
//  would widen the exemption past the two files that have earned it and would hide genuinely
//  badly named new code elsewhere.
//
//  DECISION 4 - LEGACY SECTION ORDER IS PRESERVED, AND HERE IT IS ALSO A COMPILE REQUIREMENT
//  --------------------------------------------------------------------------------------------
//  The 24 regions appear in exactly the order the source declares them. That is fidelity, but it
//  is also mechanically necessary: eight WS_CERT_* constants [enums.sru:L833-L836, L839-L842] are
//  defined AS the corresponding HTTP_CERT_* constant, and a C# 'const' may only reference a
//  'const' already declared. Reordering the Http and WebSocket regions would not merely lose
//  information, it would fail to compile. No region may be moved, merged, trimmed or relocated to
//  another file.
//
//  DECISION 5 - ALIAS AND SUM EXPRESSIONS STAY WRITTEN AS EXPRESSIONS
//  --------------------------------------------------------------------------------------------
//  31 of the 723 constants have a non-literal right-hand side, and every one is transcribed as the
//  expression the source writes. They break down as:
//
//      7 SUMS OF OTHER CONSTANTS   INIT_FLAG_ENABLE_ALL, XML_PARSE_DEFAULT, XML_PARSE_FULL,
//                                  CRYPTO_RNDSTRING_DEFAULT, CRYPTO_GUID_DEFAULT,
//                                  LOG_LEVEL_DEFAULT, LOG_CLEAR_DEFAULT
//      23 SINGLE-TERM ALIASES      the eight WS_CERT_*, the three WS_MQTT_QOS_AT_*,
//                                  CRYPTO_SYMCRYPT_MODE_DEFAULT, CRYPTO_RSA_PADDING_DEFAULT,
//                                  XML_FORMAT_DEFAULT, JSON_FORMAT_DEFAULT, ZIP_FORMAT_DEFAULT,
//                                  BARCODE_UNIT/SVG/DATA_DEFAULT and
//                                  QRCODE_ECC/UNIT/SVG/DATA_DEFAULT
//      1 SUM OF TWO LITERALS       SC_HANDLE_INVOKE_METHOD = 1024 + 512
//
//  Pre-computing any of them would erase the relationship that IS the documentation - most visibly
//  the deliberate omission of BLINKFAST from INIT_FLAG_ENABLE_ALL, which a literal 3847 would hide
//  completely. The count matters as an audit figure, so it is stated exactly: 31, of which 30
//  reference another constant by name and one sums two literals.
//
//  DECISION 6 - DEFERRED-CAPABILITY REGIONS SHIP AS CONSTANTS AND NOTHING ELSE
//  --------------------------------------------------------------------------------------------
//  Most of this catalogue names capabilities belonging to the four services this phase does NOT
//  implement: Sciter, Blink, WebView and Compiler (ScriptBridge); Http, WebSocket and Ftp
//  (Integration); XML Parser, JSON Parser, Zip, Barcode, QR Code, Logger, File Scanner, Device
//  Info and Regular Expression (Documents); UI (DesignSystem). Every one of them is transcribed
//  in full, because transcribing an integer is not implementing a service - there is no client,
//  no transport, no type and no behaviour behind any of these names - and because the legacy
//  object they come from is assigned in full to this shared library. Equally, NOTHING is added
//  behind them: no interface, no wrapper, no helper, no mapping and no exception-throwing
//  placeholder. Trimming a region, commenting one out or moving one to a 'deferred' file would
//  all breach the same constraint from the other direction. Each region names its eventual
//  consumer so the boundary is auditable.
//
//  DECISION 7 - WHAT IS DELIBERATELY *NOT* CARRIED ACROSS
//  --------------------------------------------------------------------------------------------
//      enums.sru:L13-L34   The BSD 2-Clause notice and its four-condition Chinese restatement.
//                          Not duplicated here: the text lives in the repository-root LICENSE and
//                          NOTICE, and the assembly-level Copyright in Directory.Build.props
//                          points at both. Duplicating a licence into one source file of sixteen
//                          projects would create a second place for it to drift.
//      enums.sru:L1-L2     The PowerBuilder export header and $PBExportComments$ line, which are
//                          toolchain metadata with no meaning outside the PBL exporter.
//      enums.sru:L38       'Public:' as a standalone specifier. C# has no section-scoped access
//                          modifier, so it becomes 'public' on each of the 723 members.
//
//  The source's own comments ARE carried across, including the ones that read as mistakes, and
//  including two commented-out declarations. Chinese comments are translated to English with the
//  fact that they were Chinese noted at the point of translation; the meaning is never changed.
//
//  DEFECTS AND ANOMALIES PRESERVED, EACH ANNOTATED WHERE IT OCCURS
//  --------------------------------------------------------------------------------------------
//      enums.sru:L999   the Regular Expression region's closing banner reads 'End Crypto'
//      enums.sru:L881   WS_E_CLOSE_HANDSHAKE_TIMEOUT and WS_E_INVALID_PORT are both 23
//      enums.sru:L49    INIT_FLAG_ENABLE_ALL omits BLINKFAST (correct, and easy to misread)
//      enums.sru:L172   the Chinese notes write SC_SW_* where the declared flags are SC_WS_*
//      enums.sru:L505   the Blink notes reference the SCITER flag names, not the Blink ones
//      enums.sru:L1147  the three Pinyin constants are the file's only lowercase declarations
//
//  A NOTE ON STYLE, SINCE IT DIVERGES FROM ONE .editorconfig PREFERENCE
//  --------------------------------------------------------------------------------------------
//  This file uses a FILE-SCOPED namespace. The root .editorconfig expresses
//  'csharp_style_namespace_declarations = block_scoped' at severity 'silent', which by that
//  file's own stated design can never become a build error; the sibling PfwException.cs in this
//  same project is file-scoped, and file-scoped saves a level of indentation across roughly two
//  thousand lines. Matching the sibling was judged the more useful consistency. Comments are
//  plain '//' rather than '///' XML documentation throughout, because the source comments contain
//  raw '<video>', '<node/>', '<![CDATA[...]]>' and '&' text that is not well-formed XML; quoting
//  it into doc comments would either mangle the legacy text or break the build.
// ==============================================================================================

namespace PowerFramework.Shared.Kernel;

/// <summary>
/// The PowerFramework framework-wide constant catalogue: 723 named values transcribed from
/// the legacy PowerScript object at ws_objects/pfw.shared.pbl.src/enums.sru.
/// </summary>
/// <remarks>
/// Organised into 24 regions in the exact order the legacy source declares them, each region
/// carrying its source line range and its constant count. Identifier spellings are preserved
/// verbatim in SCREAMING_SNAKE because they travel in serialized payloads, log records and
/// characterization recordings. The class is static and cannot be instantiated: the legacy
/// global auto-instance is deliberately not reproduced. Regions naming capabilities that
/// belong to a service deferred out of this phase carry constants only - there is no client,
/// no transport, no type and no behaviour behind any of those names.
/// </remarks>
public static class Enums
{
    // ==========================================================================================
    //  SECTION 01 of 24 - PowerFramework enums
    //  ORACLE    ws_objects/pfw.shared.pbl.src/enums.sru:L37-L49
    //  CONSTANTS 9
    //  CONSUMER  Gateway (IN SCOPE) - Composition/CapabilityFlags.cs and
    //            Endpoints/CapabilityEndpoints.cs
    //  NOTE      the source opens this section but never closes it with an
    //            'End' banner; it runs on until the next opening banner.
    // ==========================================================================================
    #region PowerFramework enums - enums.sru:L37-L49 (9 constants)

    // enums.sru:L38 declares 'Public:' exactly once, and it governs every one of the
    // 723 declarations that follow. C# has no section-scoped access specifier, so it is
    // re-expressed as 'public' on each member individually.

    // Initialize flags (pfwInitialize:[flags])
    // ------------------------------------------------------------------------------------------
    // THE EIGHT CAPABILITY BITS - enums.sru:L40-L48
    // These are the legacy framework's OWN decomposition intent, expressed as a module-gating bitmask
    // passed to pfwInitialize. A module that is not explicitly enabled is unusable AND its DLL need
    // not ship [docs/README.md, section on initialisation]. Mapping the bits onto the Phase 1 service
    // roster is independent corroboration that the four-service slice is drawn correctly:
    //
    //     INIT_FLAG_ENABLE_UI        deferred DesignSystem
    //     INIT_FLAG_ENABLE_SCITER    deferred ScriptBridge
    //     INIT_FLAG_ENABLE_BLINK     deferred ScriptBridge
    //     INIT_FLAG_ENABLE_BLINKFAST deferred ScriptBridge
    //     INIT_FLAG_ENABLE_ORCA      PowerBuilder packaging tooling, not a service at all
    //     INIT_FLAG_ENABLE_SQLITE    Persistence  <-- THE ONLY BIT WITH AN IN-SCOPE CONSUMER IN PHASE 1
    //     INIT_FLAG_ENABLE_DPIAWARE  deferred DesignSystem
    //     INIT_FLAG_ENABLE_WEBVIEW   deferred ScriptBridge
    //
    // Gateway's Composition/CapabilityFlags.cs and Endpoints/CapabilityEndpoints.cs project this
    // bitmask as configuration. Every other bit therefore ships as a NAME AND A VALUE and nothing
    // else: no client, no wrapper, no placeholder type sits behind any of them (C-D).
    //
    // THE BIT LAYOUT IS SPARSE ON PURPOSE. The values run 1, 2, 4, 8 then jump straight to 256 -
    // bits 4 through 7 (values 16, 32, 64, 128) are unused. Do NOT fill the gap: a new bit assigned
    // there would collide with whatever the legacy author reserved the range for, and the gap is
    // itself observable through INIT_FLAG_ENABLE_ALL.
    // ------------------------------------------------------------------------------------------
    public const long INIT_FLAG_ENABLE_UI = 1;
    public const long INIT_FLAG_ENABLE_SCITER = 2;
    public const long INIT_FLAG_ENABLE_BLINK = 4;
    public const long INIT_FLAG_ENABLE_BLINKFAST = 8;
    public const long INIT_FLAG_ENABLE_ORCA = 256;
    public const long INIT_FLAG_ENABLE_SQLITE = 512;
    public const long INIT_FLAG_ENABLE_DPIAWARE = 1024;
    public const long INIT_FLAG_ENABLE_WEBVIEW = 2048;
    // ------------------------------------------------------------------------------------------
    // INIT_FLAG_ENABLE_ALL IS WRITTEN AS A SEVEN-TERM SUM, NOT AS THE LITERAL 3847 - enums.sru:L49
    // It evaluates to 3847, and a unit test asserts exactly that. It is nevertheless transcribed as
    // the sum the source writes, because the sum is what makes the OMISSION legible:
    //
    //     INIT_FLAG_ENABLE_BLINKFAST (8) IS DELIBERATELY ABSENT.
    //
    // blink.dll and blinkfast.dll are two alternative builds of ONE engine, so asking for both at
    // once is meaningless. Collapsing the sum to 3847 would hide that reasoning behind a number, and
    // a later reader would have no way to tell a deliberate omission from an arithmetic slip. This is
    // legacy behaviour, correct as it stands, and NOT a defect to fix (C-B).
    //
    // Consequence worth asserting in a test: (INIT_FLAG_ENABLE_ALL & INIT_FLAG_ENABLE_BLINKFAST) == 0.
    // ------------------------------------------------------------------------------------------
    public const long INIT_FLAG_ENABLE_ALL = INIT_FLAG_ENABLE_UI
                                           + INIT_FLAG_ENABLE_SCITER
                                           + INIT_FLAG_ENABLE_BLINK
                                           + INIT_FLAG_ENABLE_ORCA
                                           + INIT_FLAG_ENABLE_SQLITE
                                           + INIT_FLAG_ENABLE_DPIAWARE
                                           + INIT_FLAG_ENABLE_WEBVIEW;

    #endregion

    // ==========================================================================================
    //  SECTION 02 of 24 - UI
    //  ORACLE    ws_objects/pfw.shared.pbl.src/enums.sru:L51-L111
    //  CONSTANTS 49
    //  CONSUMER  deferred DesignSystem - constants only, nothing behind them (C-D)
    //  NOTE      the source opens this section but never closes it with an
    //            'End' banner; it runs on until the next opening banner.
    // ==========================================================================================
    #region UI - enums.sru:L51-L111 (49 constants)

    // Orientation
    public const ushort HORZ = 0;  // Horizontal
    public const ushort VERT = 1;  // Vertical
    // Icon sizes
    public const ushort SMALL = 16;
    public const ushort MEDIUM = 24;
    public const ushort LARGE = 32;
    public const ushort XLARGE = 48;
    // Positions
    public const ushort LEFT = 0;
    public const ushort TOP = 1;
    public const ushort RIGHT = 2;
    public const ushort BOTTOM = 3;
    // Background styles
    public const ushort SOLID = 0;
    public const ushort XP = 1;
    public const ushort VISTAEMBOSSED = 2;
    public const ushort VISTAORIGINAL = 3;
    public const ushort VISTAGLASS = 4;
    public const ushort TRANSPARENT = 5;
    // Border styles
    public const ushort BS_NONE = 0;
    public const ushort BS_SOLID = 1;
    public const ushort BS_RAISED = 2;
    public const ushort BS_ROUND = 3;
    // Line styles
    public const ushort LS_SOLID = 0;
    public const ushort LS_DASH = 1;
    public const ushort LS_DOT = 2;
    public const ushort LS_DASHDOT = 3;
    public const ushort LS_DASHDOTDOT = 4;
    // ToolTip styles
    public const ushort TTS_NONE = 0;
    public const ushort TTS_NORMAL = 1;
    public const ushort TTS_BALLOON = 2;
    // Button styles
    public const ushort BTS_NORMAL = 0;
    public const ushort BTS_DROPDOWN = 1;
    public const ushort BTS_SPLIT = 2;
    // Scroll flags
    public const long SF_FORWARD = 0;
    public const long SF_BACKWARD = 1;
    // States
    // ------------------------------------------------------------------------------------------
    // THE STATE_* FAMILY IS AN ADDITIVE BITMASK, NOT AN ORDINAL SET - enums.sru:L96-L111
    // Sixteen members: FIFTEEN single-bit values running 1, 2, 4 ... 16384, i.e. bits 0 through 14
    // contiguously with no gap, plus STATE_NONE = 0 as the empty mask. Declared `Constant Ulong` in
    // the source, so they arrive here as C# `uint` and combine with the bitwise operators. They are
    // consumed through the Bits.* helpers in this same assembly, which are authored against 32-bit
    // `uint`; that agreement is the reason PB Ulong maps to uint rather than to long. See the TYPE
    // MAPPING block in the file header.
    // ------------------------------------------------------------------------------------------
    public const uint STATE_NONE = 0;
    public const uint STATE_DISABLED = 1;
    public const uint STATE_HOVER = 2;
    public const uint STATE_PRESSED = 4;
    public const uint STATE_FOCUS = 8;
    public const uint STATE_ACTIVE = 16;
    public const uint STATE_CURRENT = 32;
    public const uint STATE_CHECKED = 64;
    public const uint STATE_SELECTED = 128;
    public const uint STATE_EXPANDED = 256;
    public const uint STATE_COLLAPSED = 512;
    public const uint STATE_HIGHLIGHTED = 1024;
    public const uint STATE_DRAGGING = 2048;
    public const uint STATE_FLASHING = 4096;
    public const uint STATE_DEFAULT = 8192;
    public const uint STATE_READONLY = 16384;

    #endregion

    // ==========================================================================================
    //  SECTION 03 of 24 - I18N
    //  ORACLE    ws_objects/pfw.shared.pbl.src/enums.sru:L113-L123
    //  CONSTANTS 8
    //  CONSUMER  PowerFramework.Shared.Localization (IN SCOPE) - Categories.cs, II18nProvider.cs
    //  NOTE      the source opens this section but never closes it with an
    //            'End' banner; it runs on until the next opening banner.
    // ==========================================================================================
    #region I18N - enums.sru:L113-L123 (8 constants)

    // ------------------------------------------------------------------------------------------
    // I18N_CAT_CUSTOM IS A PUBLISHED CONTRACT VALUE, NOT AN ARBITRARY LAST ENTRY - enums.sru:L118-L123
    // The sibling PowerFramework.Shared.Localization/Categories.cs defines its two framework
    // categories as OFFSETS FROM I18N_CAT_CUSTOM:
    //
    //     CAT_MSGBOX  and  CAT_DWSVC   are I18N_CAT_CUSTOM + n
    //     [ws_objects/pfw.ui.controls.ext.pbl.src/ne_cst_i18n.sru:L16-L17]
    //
    // So changing I18N_CAT_CUSTOM from 5 would silently shift BOTH of them, and CAT_DWSVC is reached
    // from inside DataServices' validation path
    // [ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L357, L368]. The value travels in
    // localization lookups and in characterization recordings. Treat 5 as frozen.
    // ------------------------------------------------------------------------------------------
    // Source
    public const long I18N_SRC_PFW = 0;
    public const long I18N_SRC_CUSTOM = 1;
    // Category
    public const long I18N_CAT_WINDOW = 0;
    public const long I18N_CAT_TABCONTROL = 1;
    public const long I18N_CAT_RIBBONBAR = 2;
    public const long I18N_CAT_SPLITCONTAINER = 3;
    public const long I18N_CAT_DATAWINDOW = 4;
    public const long I18N_CAT_CUSTOM = 5;

    #endregion

    // ==========================================================================================
    //  SECTION 04 of 24 - Sciter
    //  ORACLE    ws_objects/pfw.shared.pbl.src/enums.sru:L125-L488
    //  CONSTANTS 259
    //  CONSUMER  deferred ScriptBridge - constants only, nothing behind them (C-D)
    // ==========================================================================================
    #region Sciter - enums.sru:L125-L488 (259 constants)

    // SCITER_RT_OPTIONS (u_sciter/n_sciter/w_sciter::SetOption/SciterSetOption:[option])
    // value:TRUE - enable, value:FALSE - disable, enabled by default
    public const long SC_OPT_SMOOTH_SCROLL = 1;
    public const long SC_OPT_CONNECTION_TIMEOUT = 2;       // value: milliseconds, connection timeout of http client
    // value: 0 - drop connection, 1 - use builtin dialog, 2 - accept connection silently (SC_OPT_HTTPS_ERROR_VALUE)
    public const long SC_OPT_HTTPS_ERROR = 3;
    // value: 0 - system default, 1 - no smoothing, 2 - std smoothing, 3 - clear type (SC_OPT_FONT_SMOOTHING_VALUE)
    public const long SC_OPT_FONT_SMOOTHING = 4;
    // Windows Aero support, value: 0 - normal drawing, 1 - window has transparent background after calls
    // DwmExtendFrameIntoClientArea() or DwmEnableBlurBehindWindow().
    public const long SC_OPT_TRANSPARENT_WINDOW = 6;
    // value - combination of SC_OPT_SCRIPT_RUNTIME_FEATURES_VALUE flags.
    public const long SC_OPT_SCRIPT_RUNTIME_FEATURES = 8;
    public const long SC_OPT_GFX_LAYER = 9;                // value - SC_OPT_GFX_LAYER_VALUE
    public const long SC_OPT_DEBUG_MODE = 10;              // value - TRUE/FALSE
    // value - BOOL, TRUE - the engine will use "unisex" theme that is common for all platforms. That UX theme is not
    // using OS primitives for rendering input elements. Use it if you want exactly the same (modulo fonts)
    // look-n-feel on all platforms.
    public const long SC_OPT_UX_THEMING = 11;
    // hWnd, value - TRUE/FALSE - window uses per pixel alpha (e.g. WS_EX_LAYERED/UpdateLayeredWindow() window)
    public const long SC_OPT_ALPHA_WINDOW = 12;
    // value 1 - 1px in CSS is treated as 1dip, value 0 - default behavior - 1px is a physical pixel
    public const long SC_OPT_PX_AS_DIP = 16;

    // SC_OPT_HTTPS_ERROR_VALUE
    public const long SC_OPTV_HTTPS_ERROR_DROP = 0;
    public const long SC_OPTV_HTTPS_ERROR_PROMPT = 1;
    public const long SC_OPTV_HTTPS_ERROR_ACCEPT = 2;

    // SC_OPT_FONT_SMOOTHING_VALUE
    public const long SC_OPTV_FONT_DEFAULT = 0;
    public const long SC_OPTV_FONT_NO_SMOOTHING = 1;
    public const long SC_OPTV_FONT_SMOOTHING = 2;
    public const long SC_OPTV_FONT_CLEAR_TYPE = 3;

    // SC_OPT_SCRIPT_RUNTIME_FEATURES_VALUE
    public const long SC_OPTV_RT_ALLOW_FILE_IO = 1;
    public const long SC_OPTV_RT_ALLOW_SOCKET_IO = 2;
    public const long SC_OPTV_RT_ALLOW_EVAL = 4;
    public const long SC_OPTV_RT_ALLOW_SYSINFO = 8;

    // SC_OPT_GFX_LAYER_VALUE
    public const long SC_OPTV_GFX_LAYER_GDI = 1;
    public const long SC_OPTV_GFX_LAYER_WARP = 2;
    public const long SC_OPTV_GFX_LAYER_D2D = 3;
    public const long SC_OPTV_GFX_LAYER_SKIA = 4;
    public const long SC_OPTV_GFX_LAYER_SKIA_OPENGL = 5;
    public const long SC_OPTV_GFX_LAYER_AUTO = 65535;

    // ------------------------------------------------------------------------------------------
    // TWO SCITER WINDOW FLAGS ARE COMMENTED OUT IN THE SOURCE AND STAY COMMENTED OUT HERE.
    // enums.sru:L175 (SC_WS_CHILD = 1) and enums.sru:L185 (SC_WS_OWNS_VM = 1024) are commented-out
    // declarations, so they are NOT among the 723 constants and must NOT be declared. They are
    // carried across as comments because they document WHY the flag is unavailable, which is
    // information a reader needs and a deletion would destroy (C-B, C-C).
    //
    // A SECOND LEGACY SLIP IN THE SAME PLACE: the four Chinese notes just below write SC_SW_CHILD,
    // SC_SW_MAIN and SC_SW_POPUP, while the flags actually declared in this region are SC_WS_CHILD,
    // SC_WS_MAIN and SC_WS_POPUP - the source's own prose uses the wrong infix throughout. The
    // translation keeps the prose exactly as written rather than silently repairing the names, so the
    // slip stays visible to a reader comparing prose against declarations (C-B).
    // ------------------------------------------------------------------------------------------
    // SCITER_CREATE_WINDOW_FLAGS (n_sciter::CreateWindow:[flags])
    // Note (this note is Chinese in the source; translated here, meaning unchanged):
    // 1. PowerFramework does not permit creating a Sciter window with the SC_WS_CHILD style; use u_sciter when a
    // Sciter CHILD window is required.
    // 2. Inside PowerFramework, closing a window created with the SC_SW_MAIN style does NOT terminate the process!
    // 3. When creating with the SC_SW_POPUP style it is best to supply the [owner] argument.
    // 4. When creating with the SC_SW_MAIN style the [owner] argument is ignored.
    // Constant Long SC_WS_CHILD = 1 // child window only, if this flag is set all other flags ignored
    public const long SC_WS_TITLEBAR = 2;        // toplevel window, has titlebar
    public const long SC_WS_RESIZEABLE = 4;      // has resizeable frame
    public const long SC_WS_TOOL = 8;            // is tool window
    public const long SC_WS_CONTROLS = 16;       // has minimize / maximize buttons
    public const long SC_WS_GLASSY = 32;         // glassy window ( DwmExtendFrameIntoClientArea on windows )
    public const long SC_WS_ALPHA = 64;          // transparent window ( e.g. WS_EX_LAYERED on Windows )
    public const long SC_WS_MAIN = 128;          // main window of the app
    public const long SC_WS_POPUP = 256;         // the window is created as topmost window.
    public const long SC_WS_ENABLE_DEBUG = 512;  // make this window inspector ready
    // Constant Long SC_WS_OWNS_VM = 1024 // it has its own script VM
    public const long SC_WS_VISIBLE = 65536;  // the window is visible

    // EVENT_GROUPS (event filter)
    public const long SC_HANDLE_MOUSE = 1;    // mouse events
    public const long SC_HANDLE_KEY = 2;      // key events
    // focus events, if this flag is set it also means that element it attached to is focusable
    public const long SC_HANDLE_FOCUS = 4;
    public const long SC_HANDLE_SCROLL = 8;   // scroll events
    public const long SC_HANDLE_TIMER = 16;   // timer event
    public const long SC_HANDLE_SIZE = 32;    // size changed event
    // logical, synthetic events: BUTTON_CLICK, HYPERLINK_CLICK, etc.,a.k.a. notifications from intrinsic behaviors
    public const long SC_HANDLE_EVENT = 256;
    // ------------------------------------------------------------------------------------------
    // SC_HANDLE_INVOKE_METHOD IS WRITTEN AS 1024 + 512, EXACTLY AS THE SOURCE WRITES IT.
    // It evaluates to 1536. The sum is kept because it records that the flag is the composition of
    // two lower bits rather than a value in its own right; pre-computing it would erase that
    // (C-B, same reasoning as INIT_FLAG_ENABLE_ALL).
    // ------------------------------------------------------------------------------------------
    public const long SC_HANDLE_INVOKE_METHOD = 1024 + 512;  // behavior specific methods
    public const long SC_HANDLE_ALL = 65536;                 // all of them

    // PHASE_MASK (combine with [eventflag])
    // see: http://www.w3.org/TR/xml-events/Overview.html#s_intro
    public const long SC_PHASE_BUBBLING = 0;     // bubbling (emersion) phase
    // capture (immersion) phase, this flag is or'ed with EVENTS codes below
    public const long SC_PHASE_SINKING = 32768;
    public const long SC_PHASE_HANDLED = 65536;

    // MOUSE_BUTTONS (u_sciter/n_sciter/w_sciter/n_scitereventhandler::OnMouse:[mousebuttons])
    public const long SC_MOUSE_LEFT = 1;    // aka left button
    public const long SC_MOUSE_RIGHT = 2;   // aka right button
    public const long SC_MOUSE_MIDDLE = 4;

    // MOUSE_EVENTS (EVENT_GROUPS::SC_HANDLE_MOUSE,
    // u_sciter/n_sciter/w_sciter/n_scitereventhandler::OnMouse:[eventflag])
    public const long SC_MOUSE_ENTER = 0;
    public const long SC_MOUSE_LEAVE = 1;
    public const long SC_MOUSE_MOVE = 2;
    public const long SC_MOUSE_UP = 3;
    public const long SC_MOUSE_DOWN = 4;
    public const long SC_MOUSE_DCLICK = 5;
    public const long SC_MOUSE_WHEEL = 6;
    public const long SC_MOUSE_TICK = 7;    // mouse pressed ticks
    public const long SC_MOUSE_IDLE = 8;    // mouse stay idle for some time

    // KEYBOARD_STATES (OnMouse/OnKey:[keyboardstates])
    public const long SC_KEY_CONTROL = 1;
    public const long SC_KEY_SHIFT = 2;
    public const long SC_KEY_ALT = 4;

    // KEY_EVENTS (EVENT_GROUPS::SC_HANDLE_KEY, u_sciter/n_sciter/w_sciter/n_scitereventhandler::OnKey:[eventflag])
    public const long SC_KEY_DOWN = 0;
    public const long SC_KEY_UP = 1;
    public const long SC_KEY_CHAR = 2;

    // FOCUS_EVENTS (EVENT_GROUPS::SC_HANDLE_FOCUS,
    // u_sciter/n_sciter/w_sciter/n_scitereventhandler::OnFocus:[eventflag])
    public const long SC_FOCUS_LOST = 0;  // non-bubbling event, target is new focus element
    public const long SC_FOCUS_GOT = 1;   // non-bubbling event, target is old focus element
    public const long SC_FOCUS_IN = 2;    // bubbling event/notification, target is an element that got focus
    public const long SC_FOCUS_OUT = 3;   // bubbling event/notification, target is an element that lost focus

    // BEHAVIOR_EVENTS (EVENT_GROUPS::SC_HANDLE_EVENT,
    // u_sciter/n_sciter/w_sciter/n_scitereventhandler::OnEvent:[eventflag])
    public const long SC_EVT_BUTTON_CLICK = 0;                    // click on button
    public const long SC_EVT_BUTTON_PRESS = 1;                    // mouse down or key down in button
    public const long SC_EVT_BUTTON_STATE_CHANGED = 2;            // checkbox/radio/slider changed its state/value
    public const long SC_EVT_EDIT_VALUE_CHANGING = 3;             // before text change
    public const long SC_EVT_EDIT_VALUE_CHANGED = 4;              // after text change
    public const long SC_EVT_SELECT_SELECTION_CHANGED = 5;        // selection in <select> changed
    // node in select expanded/collapsedheTarget is the node
    public const long SC_EVT_SELECT_STATE_CHANGED = 6;
    // request to show popup just received, here DOM of popup element can be modifed.
    public const long SC_EVT_POPUP_REQUEST = 7;
    // popup element has been measured and ready to be shown on screen, here you can use functions like ScrollToView.
    public const long SC_EVT_POPUP_READY = 8;
    // popup element is closed, here DOM of popup element can be modifed again - e.g. some items can be removed to
    // free memory.
    public const long SC_EVT_POPUP_DISMISSED = 9;
    // menu item activated by mouse hover or by keyboard,
    public const long SC_EVT_MENU_ITEM_ACTIVE = 10;
    // menu item click, BEHAVIOR_EVENT_PARAMS structure layout BEHAVIOR_EVENT_PARAMS.cmd -
    // MENU_ITEM_CLICK/MENU_ITEM_ACTIVE BEHAVIOR_EVENT_PARAMS.heTarget - owner(anchor) of the menu
    // BEHAVIOR_EVENT_PARAMS.he - the menu item, presumably <li> element BEHAVIOR_EVENT_PARAMS.reason - BY_MOUSE_CLICK
    // | BY_KEY_CLICK
    public const long SC_EVT_MENU_ITEM_CLICK = 11;
    // "right-click"BEHAVIOR_EVENT_PARAMS::he is current popup menu HELEMENT being processed or NULL. application can
    // provide its own HELEMENT here (if it is NULL) or modify current menu element.
    public const long SC_EVT_CONTEXT_MENU_REQUEST = 16;
    // broadcast notification, sent to all elements of some container being shown or hidden
    public const long SC_EVT_VISIUAL_STATUS_CHANGED = 17;
    // broadcast notification, sent to all elements of some container that got new value of :disabled state
    public const long SC_EVT_DISABLED_STATUS_CHANGED = 18;
    public const long SC_EVT_POPUP_DISMISSING = 19;               // popup is about to be closed
    // content has been changed, is posted to the element that gets content changed reason is combination of
    // CONTENT_CHANGE_BITS. target == NULL means the window got new document and this event is dispatched only to the
    // window.
    public const long SC_EVT_CONTENT_CHANGED = 21;
    public const long SC_EVT_CLICK = 22;                          // generic click
    public const long SC_EVT_CHANGE = 23;                         // generic change
    public const long SC_EVT_HYPERLINK_CLICK = 128;               // hyperlink click
    // element was collapsed, so far only behavior:tabs is sending these two to the panels
    public const long SC_EVT_ELEMENT_COLLAPSED = 144;
    public const long SC_EVT_ELEMENT_EXPANDED = 145;              // element was expanded
    // activate (select) child, used for example by accesskeys behaviors to send activation request, e.g. tab on
    // behavior:tabs.
    public const long SC_EVT_ACTIVATE_CHILD = 146;
    public const long SC_EVT_INIT_DATA_VIEW = 147;                // request to virtual grid to initialize its view
    // request from virtual grid to data source behavior to fill data in the table parameters passed throug
    // DATA_ROWS_PARAMS structure.
    public const long SC_EVT_ROWS_DATA_REQUEST = 148;
    // ui state changed, observers shall update their visual states. is sent for example by behavior:richtext when
    // caret position/selection has changed.
    public const long SC_EVT_UI_STATE_CHANGED = 149;
    // behavior:form detected submission event. BEHAVIOR_EVENT_PARAMS::data field contains data to be posted.
    // BEHAVIOR_EVENT_PARAMS::data is of type T_MAP in this case key/value pairs of data that is about to be
    // submitted. You can modify the data or discard submission by returning true from the handler.
    public const long SC_EVT_FORM_SUBMIT = 150;
    // behavior:form detected reset event (from button type=reset). BEHAVIOR_EVENT_PARAMS::data field contains data to
    // be reset. BEHAVIOR_EVENT_PARAMS::data is of type T_MAP in this case key/value pairs of data that is about to be
    // rest. You can modify the data or discard reset by returning true from the handler.
    public const long SC_EVT_FORM_RESET = 151;
    // document in behavior:frame or root document is complete.
    public const long SC_EVT_DOCUMENT_COMPLETE = 152;
    public const long SC_EVT_HISTORY_PUSH = 153;                  // requests to behavior:history (commands)
    public const long SC_EVT_HISTORY_DROP = 154;
    public const long SC_EVT_HISTORY_PRIOR = 155;
    public const long SC_EVT_HISTORY_NEXT = 156;
    // behavior:history notification - history stack has changed
    public const long SC_EVT_HISTORY_STATE_CHANGED = 157;
    public const long SC_EVT_CLOSE_POPUP = 158;                   // close popup request
    // request tooltipevt.source <- is the tooltip element.
    public const long SC_EVT_REQUEST_TOOLTIP = 159;
    // animation started (reason=1) or ended(reason=0) on the element.
    public const long SC_EVT_ANIMATION = 160;
    // document created, script namespace initialized. target -> the document
    public const long SC_EVT_DOCUMENT_CREATED = 192;
    // document is about to be closed, to cancel closing do: evt.data = sciter::value("cancel");
    public const long SC_EVT_DOCUMENT_CLOSE_REQUEST = 193;
    // last notification before document removal from the DOM
    public const long SC_EVT_DOCUMENT_CLOSE = 194;
    // document has got DOM structure, styles and behaviors of DOM elements. Script loading run is complete at this
    // moment.
    public const long SC_EVT_DOCUMENT_READY = 195;
    // document just finished parsing - has got DOM structure. This event is generated before DOCUMENT_READY
    public const long SC_EVT_DOCUMENT_PARSED = 196;
    public const long SC_EVT_VIDEO_INITIALIZED = 209;             // <video> "ready" notification
    public const long SC_EVT_VIDEO_STARTED = 210;                 // <video> playback started notification
    public const long SC_EVT_VIDEO_STOPPED = 211;                 // <video> playback stoped/paused notification
    // <video> request for frame source binding, If you want to provide your own video frames source for the given
    // target <video> element do the following: 1. Handle and consume this VIDEO_BIND_RQ request 2. You will receive
    // second VIDEO_BIND_RQ request/event for the same <video> element but this time with the 'reason' field set to an
    // instance of sciter::video_destination interface. 3. add_ref() it and store it for example in worker thread
    // producing video frames. 4. call sciter::video_destination::start_streaming(...) providing needed parameters
    // call sciter::video_destination::render_frame(...) as soon as they are available call
    // sciter::video_destination::stop_streaming() to stop the rendering (a.k.a. end of movie reached)
    public const long SC_EVT_VIDEO_BIND_RQ = 212;
    public const long SC_EVT_PAGINATION_STARTS = 224;             // behavior:pager starts pagination
    // behavior:pager paginated page no, reason -> page no
    public const long SC_EVT_PAGINATION_PAGE = 225;
    // behavior:pager end pagination, reason -> total pages
    public const long SC_EVT_PAGINATION_ENDS = 226;
    public const long SC_EVT_CUSTOM_NAME = 240;                   // event with custom name
    // all custom event codes shall be greater than this number. All codes below this will be used solely by
    // application - Sciter will not intrepret it and will do just dispatching. To send event notifications with these
    // codes use SciterSend/PostEvent API.
    public const long SC_EVT_FIRST_APPLICATION_EVENT_CODE = 256;
    public const long SC_EVT_CUSTOM = 4096;

    // CONTENT_CHANGE_BITS (for SC_EVT_CONTENT_CHANGED reason,
    // u_sciter/n_sciter/n_scitereventhandler::OnEvent:[reason])
    public const long SC_EVR_CONTENT_ADDED = 1;
    public const long SC_EVR_CONTENT_REMOVED = 2;

    // CLICK_REASON (u_sciter/n_sciter/w_sciter/n_scitereventhandler::OnEvent:[reason])
    public const long SC_EVR_BY_MOUSE_CLICK = 0;
    public const long SC_EVR_BY_KEY_CLICK = 1;
    public const long SC_EVR_SYNTHESIZED = 2;       // synthesized, programmatically generated.
    public const long SC_EVR_BY_MOUSE_ON_ICON = 3;

    // EDIT_CHANGED_REASON (u_sciter/n_sciter/w_sciter/n_scitereventhandler::OnEvent:[reason])
    public const long SC_EVR_BY_INS_CHAR = 0;   // single char insertion
    public const long SC_EVR_BY_INS_CHARS = 1;  // character range insertion, clipboard
    public const long SC_EVR_BY_DEL_CHAR = 2;   // single char deletion
    public const long SC_EVR_BY_DEL_CHARS = 3;  // character range deletion (selection)
    public const long SC_EVR_BY_UNDO_REDO = 4;  // undo/redo

    // VALUE_TYPE (n_scitervalue::GetType/GetItemType)
    public const long SC_TYPE_UNDEFINED = 0;
    public const long SC_TYPE_NULL = 1;
    public const long SC_TYPE_BOOLEAN = 2;
    public const long SC_TYPE_NUMBER = 3;
    public const long SC_TYPE_STRING = 4;
    public const long SC_TYPE_DATETIME = 5;
    public const long SC_TYPE_CLASS = 6;
    public const long SC_TYPE_OBJECT = 7;
    public const long SC_TYPE_ARRAY = 8;
    public const long SC_TYPE_BYTES = 9;
    public const long SC_TYPE_BLOB = 10;
    public const long SC_TYPE_ELEMENT = 11;
    public const long SC_TYPE_FUNCTION = 12;
    public const long SC_TYPE_FUNCTOR = 13;
    public const long SC_TYPE_UNNAMED = 14;

    // VALUE_UNIT_TYPE (n_scitervalue::GetUnitType/GetItemUnitType)
    public const long SC_TYPE_UNIT_EM = 1;      // height of the element's font.
    public const long SC_TYPE_UNIT_EX = 2;      // height of letter 'x'
    public const long SC_TYPE_UNIT_PR = 3;      // %
    public const long SC_TYPE_UNIT_SP = 4;      // %% "springs", a.k.a. flex units
    public const long SC_TYPE_UNIT_PX = 7;      // pixels
    public const long SC_TYPE_UNIT_IN = 8;      // inches (1 inch = 2.54 centimeters).
    public const long SC_TYPE_UNIT_CM = 9;      // centimeters.
    public const long SC_TYPE_UNIT_MM = 10;     // millimeters.
    public const long SC_TYPE_UNIT_PT = 11;     // points (1 point = 1/72 inches).
    public const long SC_TYPE_UNIT_PC = 12;     // picas (1 pica = 12 points).
    public const long SC_TYPE_UNIT_DIP = 13;
    public const long SC_TYPE_UNIT_COLOR = 15;  // color in int
    public const long SC_TYPE_UNIT_URL = 16;    // url in string

    // SET_ELEMENT_HTML (n_sciterelement::SetHtml:[nwhere])
    public const long SC_SIH_REPLACE_CONTENT = 0;
    public const long SC_SIH_INSERT_AT_START = 1;
    public const long SC_SIH_APPEND_AFTER_LAST = 2;
    public const long SC_SOH_REPLACE = 3;
    public const long SC_SOH_INSERT_BEFORE = 4;
    public const long SC_SOH_INSERT_AFTER = 5;

    // ELEMENT_STATE_BITS (n_sciterelement::GetState/SetState)
    public const uint SC_STATE_LINK = 1;
    public const uint SC_STATE_HOVER = 2;
    public const uint SC_STATE_ACTIVE = 4;
    public const uint SC_STATE_FOCUS = 8;
    public const uint SC_STATE_VISITED = 16;
    public const uint SC_STATE_CURRENT = 32;            // current (hot) item
    public const uint SC_STATE_CHECKED = 64;            // element is checked (or selected)
    public const uint SC_STATE_DISABLED = 128;          // element is disabled
    public const uint SC_STATE_READONLY = 256;          // readonly input element
    public const uint SC_STATE_EXPANDED = 512;          // expanded state - nodes in tree view
    // collapsed state - nodes in tree view - mutually exclusive with
    public const uint SC_STATE_COLLAPSED = 1024;
    public const uint SC_STATE_INCOMPLETE = 2048;       // one of fore/back images requested but not delivered
    public const uint SC_STATE_ANIMATING = 4096;        // is animating currently
    public const uint SC_STATE_FOCUSABLE = 8192;        // will accept focus
    public const uint SC_STATE_ANCHOR = 16384;          // anchor in selection (used with current in selects)
    public const uint SC_STATE_SYNTHETIC = 32768;       // this is a synthetic element - don't emit it's head/tail
    public const uint SC_STATE_OWNS_POPUP = 65536;      // this is a synthetic element - don't emit it's head/tail
    public const uint SC_STATE_TABFOCUS = 131072;       // focus gained by tab traversal
    // empty - element is empty (text.size() == 0 && subs.size() == 0) if element has behavior attached then the
    // behavior is responsible for the value of this flag.
    public const uint SC_STATE_EMPTY = 262144;
    public const uint SC_STATE_BUSY = 524288;           // busy; loading
    // drag over the block that can accept it (so is current drop target). Flag is set for the drop target block
    public const uint SC_STATE_DRAG_OVER = 1048576;
    public const uint SC_STATE_DROP_TARGET = 2097152;   // active drop target.
    public const uint SC_STATE_MOVING = 4194304;        // dragging/moving - the flag is set for the moving block.
    public const uint SC_STATE_COPYING = 8388608;       // dragging/copying - the flag is set for the copying block.
    public const uint SC_STATE_DRAG_SOURCE = 16777216;  // element that is a drag source.
    public const uint SC_STATE_DROP_MARKER = 33554432;  // element is drop marker
    // pressed - close to active but has wider life span - e.g. in MOUSE_UP it is still on; so behavior can check it
    // in MOUSE_UP to discover CLICK condition.
    public const uint SC_STATE_PRESSED = 67108864;
    public const uint SC_STATE_POPUP = 134217728;       // this element is out of flow - popup
    public const uint SC_STATE_IS_LTR = 268435456;      // the element or one of its containers has dir=ltr declared
    public const uint SC_STATE_IS_RTL = 536870912;      // the element or one of its containers has dir=rtl declared

    // ELEMENT_AREAS (n_sciterelement::GetLocation:[area])
    // - or this flag if you want to get Sciter window relative coordinates, otherwise it will use nearest windowed
    // container e.g. popup window.
    public const uint SC_AREA_ROOT_RELATIVE = 1;
    // - "or" this flag if you want to get coordinates relative to the origin of element iself.
    public const uint SC_AREA_SELF_RELATIVE = 2;
    public const uint SC_AREA_CONTAINER_RELATIVE = 3;  // - position inside immediate container.
    public const uint SC_AREA_VIEW_RELATIVE = 4;       // - position relative to view - Sciter window
    public const uint SC_AREA_CONTENT_BOX = 0;         // content (inner)  box
    public const uint SC_AREA_PADDING_BOX = 16;        // content + paddings
    public const uint SC_AREA_BORDER_BOX = 32;         // content + paddings + border
    public const uint SC_AREA_MARGIN_BOX = 48;         // content + paddings + border + margins
    // relative to content origin - location of background image (if it set no-repeat)
    public const uint SC_AREA_BACK_IMAGE_AREA = 64;
    // relative to content origin - location of foreground image (if it set no-repeat)
    public const uint SC_AREA_FORE_IMAGE_AREA = 80;
    public const uint SC_AREA_SCROLLABLE_AREA = 96;    // scroll_area - scrollable area in content box

    // CTL_TYPE (n_sciterelement::GetType)
    // Control types.
    // Control here is any dom element having appropriate behavior applied
    //
    public const uint SC_CTL_NO = 0;               // < This dom element has no behavior at all.
    public const uint SC_CTL_UNKNOWN = 1;          // < This dom element has behavior but its type is unknown.
    public const uint SC_CTL_EDIT = 2;             // < Single line edit box.
    public const uint SC_CTL_NUMERIC = 3;          // < Numeric input with optional spin buttons.
    public const uint SC_CTL_CLICKABLE = 4;        // < toolbar button, behavior:clickable.
    public const uint SC_CTL_BUTTON = 5;           // < Command button.
    public const uint SC_CTL_CHECKBOX = 6;         // < CheckBox (button).
    public const uint SC_CTL_RADIO = 7;            // < OptionBox (button).
    public const uint SC_CTL_SELECT_SINGLE = 8;    // < Single select = ListBox or TreeView.
    public const uint SC_CTL_SELECT_MULTIPLE = 9;  // < Multiselectable select, ListBox or TreeView.
    public const uint SC_CTL_DD_SELECT = 10;       // < Dropdown single select.
    public const uint SC_CTL_TEXTAREA = 11;        // < Multiline TextBox.
    public const uint SC_CTL_HTMLAREA = 12;        // < WYSIWYG HTML editor.
    public const uint SC_CTL_PASSWORD = 13;        // < Password input element.
    public const uint SC_CTL_PROGRESS = 14;        // < Progress element.
    public const uint SC_CTL_SLIDER = 15;          // < Slider input element.
    public const uint SC_CTL_DECIMAL = 16;         // < Decimal number input element.
    public const uint SC_CTL_CURRENCY = 17;        // < Currency input element.
    public const uint SC_CTL_SCROLLBAR = 18;
    public const uint SC_CTL_HYPERLINK = 19;
    public const uint SC_CTL_MENUBAR = 20;
    public const uint SC_CTL_MENU = 21;
    public const uint SC_CTL_MENUBUTTON = 22;
    public const uint SC_CTL_CALENDAR = 23;
    public const uint SC_CTL_DATE = 24;
    public const uint SC_CTL_TIME = 25;
    public const uint SC_CTL_FRAME = 26;
    public const uint SC_CTL_FRAMESET = 27;
    public const uint SC_CTL_GRAPHICS = 28;
    public const uint SC_CTL_SPRITE = 29;
    public const uint SC_CTL_LIST = 30;
    public const uint SC_CTL_RICHTEXT = 31;
    public const uint SC_CTL_TOOLTIP = 32;
    public const uint SC_CTL_HIDDEN = 33;
    public const uint SC_CTL_URL = 34;             // < URL input element.
    public const uint SC_CTL_TOOLBAR = 35;
    public const uint SC_CTL_FORM = 36;
    public const uint SC_CTL_FILE = 37;            // < file input element.
    public const uint SC_CTL_PATH = 38;            // < path input element.
    public const uint SC_CTL_WINDOW = 39;          // < has HWND attached to it
    public const uint SC_CTL_LABEL = 40;
    public const uint SC_CTL_IMAGE = 41;           // < image/object.

    // OUTPUT_SUBSYTEMS (u_sciter/n_sciter/w_sciter::OnDebugOutput:[subsystem])
    public const uint SC_OT_DOM = 0;   // html parser & runtime
    public const uint SC_OT_CSSS = 1;  // csss! parser & runtime
    public const uint SC_OT_CSS = 2;   // css parser
    public const uint SC_OT_TIS = 3;   // TIS parser & runtime

    // OUTPUT_SEVERITY (u_sciter/n_sciter/w_sciter::OnDebugOutput:[severity])
    public const uint SC_OS_INFO = 0;
    public const uint SC_OS_WARNING = 1;
    public const uint SC_OS_ERROR = 2;

    // /*--- End Sciter ---*/   [enums.sru:L488]

    #endregion

    // ==========================================================================================
    //  SECTION 05 of 24 - Blink
    //  ORACLE    ws_objects/pfw.shared.pbl.src/enums.sru:L490-L547
    //  CONSTANTS 40
    //  CONSUMER  deferred ScriptBridge - constants only, nothing behind them (C-D)
    // ==========================================================================================
    #region Blink - enums.sru:L490-L547 (40 constants)

    // options (u_blink/n_blink::SetOption:[option])
    public const long BLINK_OPT_COOKIE_FILE = 1;        // value:string - file path, 'cookie.dat' by default
    // value:string - local storage directory, 'LocalStorage' by default
    public const long BLINK_OPT_STORAGE_DIR = 2;
    public const long BLINK_OPT_PLUGIN_DIR = 3;         // value:string - plugin directory, 'Plugin' by default
    // value:boolean - enable, value:FALSE - disable, enabled by default
    public const long BLINK_OPT_COOKIE_ENABLED = 4;
    // value:boolean - enable, value:FALSE - disable, disabled by default
    public const long BLINK_OPT_PLUGIN_ENABLED = 5;
    // value:boolean - enable, value:FALSE - disable, disabled by default
    public const long BLINK_OPT_CSP_CHECK_ENABLED = 6;
    // value:boolean - enable, value:FALSE - disable, disabled by default
    public const long BLINK_OPT_HEADLESS_ENABLED = 7;
    // value:boolean - window uses per pixel alpha (e.g. WS_EX_LAYERED/UpdateLayeredWindow() window)
    public const long BLINK_OPT_ALPHA_WINDOW = 8;
    public const long BLINK_OPT_GET_FAVICON = 9;        // value:boolean - get [favicon], onGetFavicon:data

    // ------------------------------------------------------------------------------------------
    // THE BLINK NOTES BELOW CITE THE SCITER FLAG NAMES, NOT THE BLINK ONES - enums.sru:L504-L506
    // The two Chinese notes name SC_SW_POPUP and SC_SW_MAIN, which belong to the Sciter region above;
    // the flags declared here are BLINK_WS_POPUP and BLINK_WS_MAIN. The guidance transfers - it is the
    // same owner-argument rule - but the identifiers cited are the wrong family. That is a legacy
    // copy-paste, carried across verbatim rather than repaired (C-B). The WebView notes at
    // enums.sru:L566-L567 got this right, which is what makes this one identifiable as a slip.
    // ------------------------------------------------------------------------------------------
    // create window flags (n_blink::CreateWindow:[flags])
    // Note (this note is Chinese in the source; translated here, meaning unchanged):
    // 1. When creating with the SC_SW_POPUP style it is best to supply the [owner] argument.
    // 2. When creating with the SC_SW_MAIN style the [owner] argument is ignored.
    public const long BLINK_WS_TITLEBAR = 1;     // toplevel window, has titlebar
    public const long BLINK_WS_RESIZEABLE = 2;   // has resizeable frame
    public const long BLINK_WS_TOOL = 4;         // is tool window
    public const long BLINK_WS_CONTROLS = 8;     // has minimize / maximize buttons
    public const long BLINK_WS_ALPHA = 16;       // transparent window ( e.g. WS_EX_LAYERED on Windows )
    public const long BLINK_WS_MAIN = 32;        // main window of the app
    public const long BLINK_WS_POPUP = 64;       // the window is created as topmost window.
    public const long BLINK_WS_VISIBLE = 65536;  // the window is visible

    // OUTPUT_SEVERITY (u_blink/n_blink/w_blink::OnDebugOutput:[severity])
    public const uint BLINK_OS_LOG = 1;
    public const uint BLINK_OS_WARNING = 2;
    public const uint BLINK_OS_ERROR = 3;
    public const uint BLINK_OS_DEBUG = 4;
    public const uint BLINK_OS_INFO = 5;
    public const uint BLINK_OS_REVOKED = 6;

    // VALUE_TYPE (n_blinkvalue::GetType/GetItemType)
    public const long BLINK_TYPE_NUMBER = 0;
    public const long BLINK_TYPE_STRING = 1;
    public const long BLINK_TYPE_BOOLEAN = 2;
    public const long BLINK_TYPE_OBJECT = 3;
    public const long BLINK_TYPE_FUNCTION = 4;
    public const long BLINK_TYPE_UNDEFINED = 5;
    public const long BLINK_TYPE_ARRAY = 6;
    public const long BLINK_TYPE_NULL = 7;

    // loading result (u_blink/n_blink/w_blink::OnLoadingFinish:[result])
    public const long BLINK_LOADING_SUCCEEDED = 0;
    public const long BLINK_LOADING_FAILED = 1;
    public const long BLINK_LOADING_CANCELED = 2;

    // NavigationType (u_blink/n_blink/w_blink::OnNavigation:[navtype])
    public const long BLINK_NAV_TYPE_LINKCLICK = 0;
    public const long BLINK_NAV_TYPE_FORMSUBMITTE = 1;
    public const long BLINK_NAV_TYPE_BACKFORWARD = 2;
    public const long BLINK_NAV_TYPE_RELOAD = 3;
    public const long BLINK_NAV_TYPE_FORMRESUBMITT = 4;
    public const long BLINK_NAV_TYPE_OTHER = 5;

    // /*--- End Blink ---*/   [enums.sru:L547]

    #endregion

    // ==========================================================================================
    //  SECTION 06 of 24 - WebView
    //  ORACLE    ws_objects/pfw.shared.pbl.src/enums.sru:L549-L576
    //  CONSTANTS 16
    //  CONSUMER  deferred ScriptBridge - constants only, nothing behind them (C-D)
    // ==========================================================================================
    #region WebView - enums.sru:L549-L576 (16 constants)

    // runtime mode (WetViewSetRuntimeMode::[mode])
    public const long WEBVIEW_RUNTIME_EVERGREEN = 0;
    public const long WEBVIEW_RUNTIME_FIXED = 1;
    public const long WEBVIEW_RUNTIME_AUTO = 2;

    // options (u_webview/n_webview::SetOption:[option])
    public const long WEBVIEW_OPT_STATUSBAR = 0;           // value:boolean - disabled by default
    public const long WEBVIEW_OPT_CONTEXT_MENU = 1;        // value:boolean - enabled by default
    // value:boolean - OnAlert/OnConfirm/OnPrompt, disabled by default
    public const long WEBVIEW_OPT_CUSTOM_DIALOG = 2;
    public const long WEBVIEW_OPT_BUILTIN_ERROR_PAGE = 3;  // value:boolean - disabled by default
    public const long WEBVIEW_OPT_DEVTOOLS = 4;            // value:boolean - disabled by default
    public const long WEBVIEW_OPT_GET_FAVICON = 5;         // value:boolean - OnFaviconChanged, disabled by default

    // create window flags (n_webview::CreateWindow:[flags])
    // Note (this note is Chinese in the source; translated here, meaning unchanged):
    // 1. When creating with the WEBVIEW_WS_POPUP style it is best to supply the [owner] argument.
    // 2. When creating with the WEBVIEW_WS_MAIN style the [owner] argument is ignored.
    public const long WEBVIEW_WS_TITLEBAR = 1;     // toplevel window, has titlebar
    public const long WEBVIEW_WS_RESIZEABLE = 2;   // has resizeable frame
    public const long WEBVIEW_WS_TOOL = 4;         // is tool window
    public const long WEBVIEW_WS_CONTROLS = 8;     // has minimize / maximize buttons
    public const long WEBVIEW_WS_ALPHA = 16;       // transparent window ( e.g. WS_EX_LAYERED on Windows )
    public const long WEBVIEW_WS_MAIN = 32;        // main window of the app
    public const long WEBVIEW_WS_VISIBLE = 65536;  // the window is visible

    // /*--- End WebView ---*/   [enums.sru:L576]

    #endregion

    // ==========================================================================================
    //  SECTION 07 of 24 - Thread
    //  ORACLE    ws_objects/pfw.shared.pbl.src/enums.sru:L578-L586
    //  CONSTANTS 4
    //  CONSUMER  Persistence (IN SCOPE) - Tasks/SqlTaskBase.cs and the task proxy pair
    // ==========================================================================================
    #region Thread - enums.sru:L578-L586 (4 constants)

    // Thread notify reasons
    public const long TNR_START = 1;
    public const long TNR_STOP = 2;    // wparam:exit code
    public const long TNR_ERROR = 3;   // wparam:error code,sparam:error info
    public const long TNR_NOTIFY = 4;

    // /*--- End Thread ---*/   [enums.sru:L586]

    #endregion

    // ==========================================================================================
    //  SECTION 08 of 24 - XML Parser
    //  ORACLE    ws_objects/pfw.shared.pbl.src/enums.sru:L588-L695
    //  CONSTANTS 53
    //  CONSUMER  deferred Documents - constants only, nothing behind them (C-D)
    // ==========================================================================================
    #region XML Parser - enums.sru:L588-L695 (53 constants)

    // Node types (n_xmlnode::GetType)
    public const long XML_NODE_NULL = 0;         // Empty (null) node handle
    public const long XML_NODE_DOCUMENT = 1;     // A document tree's absolute root
    public const long XML_NODE_ELEMENT = 2;      // Element tag, i.e. '<node/>'
    public const long XML_NODE_PCDATA = 3;       // Plain character data, i.e. 'text'
    public const long XML_NODE_CDATA = 4;        // Character data, i.e. '<![CDATA[text]]>'
    public const long XML_NODE_COMMENT = 5;      // Comment tag, i.e. '<!-- text -->'
    public const long XML_NODE_PI = 6;           // Processing instruction, i.e. '<?name?>'
    public const long XML_NODE_DECLARATION = 7;  // Document declaration, i.e. '<?xml version="1.0"?>'
    public const long XML_NODE_DOCTYPE = 8;      // Document type declaration, i.e. '<!DOCTYPE doc>'

    // XPath query return types (n_xmlqueryresult::GetValueType)
    public const long XML_XPATH_TYPE_NONE = 0;      // Unknown type (query failed to compile)
    public const long XML_XPATH_TYPE_NODE_SET = 1;  // Node set (xpath_node_set)
    public const long XML_XPATH_TYPE_NUMBER = 2;    // Number
    public const long XML_XPATH_TYPE_STRING = 3;    // String
    public const long XML_XPATH_TYPE_BOOLEAN = 4;   // Boolean

    // Node set sort types (n_xmlqueryresult::GetValueNodes:[sorttype])
    public const long XML_SORT_TYPE_UNSORTED = 0;        // Not ordered
    public const long XML_SORT_TYPE_SORTED = 1;          // Sorted by document order (ascending)
    public const long XML_SORT_TYPE_SORTED_REVERSE = 2;  // Sorted by document order (descending)

    // Parsing options (n_xmldoc::Parse/LoadFile:[opt])

    // Minimal parsing mode (equivalent to turning all other flags off).
    // Only elements and PCDATA sections are added to the DOM tree, no text conversions are performed.
    public const uint XML_PARSE_MINIMAL = 0;
    // This flag determines if processing instructions (node_pi) are added to the DOM tree. This flag is off by
    // default.
    public const uint XML_PARSE_PI = 1;
    // This flag determines if comments (node_comment) are added to the DOM tree. This flag is off by default.
    public const uint XML_PARSE_COMMENTS = 2;
    // This flag determines if CDATA sections (node_cdata) are added to the DOM tree. This flag is on by default.
    public const uint XML_PARSE_CDATA = 4;
    // This flag determines if plain character data (node_pcdata) that consist only of whitespace are added to the DOM
    // tree.
    // This flag is off by default; turning it on usually results in slower parsing and more memory consumption.
    public const uint XML_PARSE_WS_PCDATA = 8;
    // This flag determines if character and entity references are expanded during parsing. This flag is on by
    // default.
    public const uint XML_PARSE_ESCAPES = 16;
    // This flag determines if EOL characters are normalized (converted to #xA) during parsing. This flag is on by
    // default.
    public const uint XML_PARSE_EOL = 32;
    // This flag determines if attribute values are normalized using CDATA normalization rules during parsing. This
    // flag is on by default.
    public const uint XML_PARSE_WCONV_ATTRIBUTE = 64;
    // This flag determines if attribute values are normalized using NMTOKENS normalization rules during parsing. This
    // flag is off by default.
    public const uint XML_PARSE_WNORM_ATTRIBUTE = 128;
    // This flag determines if document declaration (node_declaration) is added to the DOM tree. This flag is off by
    // default.
    public const uint XML_PARSE_DECLARATION = 256;
    // This flag determines if document type declaration (node_doctype) is added to the DOM tree. This flag is off by
    // default.
    public const uint XML_PARSE_DOCTYPE = 512;
    // This flag determines if plain character data (node_pcdata) that is the only child of the parent node and that
    // consists only
    // of whitespace is added to the DOM tree.
    // This flag is off by default; turning it on may result in slower parsing and more memory consumption.
    public const uint XML_PARSE_WS_PCDATA_SINGLE = 1024;
    // This flag determines if leading and trailing whitespace is to be removed from plain character data. This flag
    // is off by default.
    public const uint XML_PARSE_TRIM_PCDATA = 2048;
    // This flag determines if plain character data that does not have a parent node is added to the DOM tree, and if
    // an empty document
    // is a valid document. This flag is off by default.
    public const uint XML_PARSE_FRAGMENT = 4096;
    // This flag determines if plain character data is be stored in the parent element's value. This significantly
    // changes the structure of
    // the document; this flag is only recommended for parsing documents with many PCDATA nodes in memory-constrained
    // environments.
    // This flag is off by default.
    public const uint XML_PARSE_EMBED_PCDATA = 8192;
    // The default parsing mode.
    // Elements, PCDATA and CDATA sections are added to the DOM tree, character/reference entities are expanded,
    // End-of-Line characters are normalized, attribute values are normalized using CDATA normalization rules.
    public const uint XML_PARSE_DEFAULT = XML_PARSE_CDATA
                                        + XML_PARSE_ESCAPES
                                        + XML_PARSE_WCONV_ATTRIBUTE
                                        + XML_PARSE_EOL;
    // The full parsing mode.
    // Nodes of all types are added to the DOM tree, character/reference entities are expanded,
    // End-of-Line characters are normalized, attribute values are normalized using CDATA normalization rules.
    public const uint XML_PARSE_FULL = XML_PARSE_DEFAULT
                                     + XML_PARSE_PI
                                     + XML_PARSE_COMMENTS
                                     + XML_PARSE_DECLARATION
                                     + XML_PARSE_DOCTYPE;

    // Formatting flags (n_xmldoc::Serialize/SaveFile:[format])

    // Indent the nodes that are written to output stream with as many indentation strings as deep the node is in DOM
    // tree. This flag is on by default.
    public const uint XML_FORMAT_INDENT = 1;
    // Write encoding-specific BOM to the output stream. This flag is off by default.
    public const uint XML_FORMAT_WRITE_BOM = 2;
    // Use raw output mode (no indentation and no line breaks are written). This flag is off by default.
    public const uint XML_FORMAT_RAW = 4;
    // Omit default XML declaration even if there is no declaration in the document. This flag is off by default.
    public const uint XML_FORMAT_NO_DECLARATION = 8;
    // Don't escape attribute values and PCDATA contents. This flag is off by default.
    public const uint XML_FORMAT_NO_ESCAPES = 16;
    // Open file using text mode in xml_document::save_file. This enables special character (i.e. new-line)
    // conversions on some systems. This flag is off by default.
    public const uint XML_FORMAT_SAVE_FILE_TEXT = 32;
    // Write every attribute on a new line with appropriate indentation. This flag is off by default.
    public const uint XML_FORMAT_INDENT_ATTRIBUTES = 64;
    // Don't output empty element tags, instead writing an explicit start and end tag even if there are no children.
    // This flag is off by default.
    public const uint XML_FORMAT_NO_EMPTY_ELEMENT_TAGS = 128;
    // The default set of formatting flags.
    // Nodes are indented depending on their depth in DOM tree, a default declaration is output if document has none.
    public const uint XML_FORMAT_DEFAULT = XML_FORMAT_INDENT;

    // ------------------------------------------------------------------------------------------
    // KNOWN FALSE POSITIVE FOR NATIVE-BINDING SCANS - enums.sru:L688 and enums.sru:L691
    // The two trailing comments just below are the ONLY two occurrences of the word "native" anywhere
    // in the legacy pfw.shared library, and neither is a binding: both describe UTF-16 / UTF-32
    // NATIVE ENDIANNESS. The library declares zero PBNI class bindings and zero external function
    // prototypes, so this whole file - and this region in particular - ports as pure data with no
    // closed-source binary behind it. Recorded here so a future native-surface sweep does not
    // re-raise them (C-K).
    // ------------------------------------------------------------------------------------------
    // Encoding flags (n_xmldoc::SaveFile/LoadFile:[encoding])
    // Auto-detect input encoding using BOM or < / <? detection; use UTF8 if BOM is not found
    public const uint XML_ENCODING_AUTO = 0;
    public const uint XML_ENCODING_UTF8 = 1;      // UTF8 encoding
    public const uint XML_ENCODING_UTF16_LE = 2;  // Little-endian UTF16
    public const uint XML_ENCODING_UTF16_BE = 3;  // Big-endian UTF16
    public const uint XML_ENCODING_UTF16 = 4;     // UTF16 with native endianness
    public const uint XML_ENCODING_UTF32_LE = 5;  // Little-endian UTF32
    public const uint XML_ENCODING_UTF32_BE = 6;  // Big-endian UTF32
    public const uint XML_ENCODING_UTF32 = 7;     // UTF32 with native endianness
    public const uint XML_ENCODING_WCHAR = 8;     // The same encoding wchar_t has (either UTF16 or UTF32)
    public const uint XML_ENCODING_LATIN1 = 9;

    // /*--- End XML Parser ---*/   [enums.sru:L695]

    #endregion

    // ==========================================================================================
    //  SECTION 09 of 24 - JSON Parser
    //  ORACLE    ws_objects/pfw.shared.pbl.src/enums.sru:L697-L713
    //  CONSTANTS 10
    //  CONSUMER  deferred Documents - constants only, nothing behind them (C-D)
    // ==========================================================================================
    #region JSON Parser - enums.sru:L697-L713 (10 constants)

    // Types (n_json::GetType)
    public const long JSON_TYPE_NONE = 0;
    public const long JSON_TYPE_BOOLEAN = 1;
    public const long JSON_TYPE_NUMBER = 2;
    public const long JSON_TYPE_STRING = 3;
    public const long JSON_TYPE_OBJECT = 4;
    public const long JSON_TYPE_ARRAY = 5;
    public const long JSON_TYPE_NULL = 6;

    // Formatting flags (n_json::Serialize:[format])
    public const long JSON_FORMAT_NONE = 0;
    public const long JSON_FORMAT_INDENT = 1;
    public const long JSON_FORMAT_DEFAULT = JSON_FORMAT_NONE;

    // /*--- End JSON Parser ---*/   [enums.sru:L713]

    #endregion

    // ==========================================================================================
    //  SECTION 10 of 24 - SQL Parser
    //  ORACLE    ws_objects/pfw.shared.pbl.src/enums.sru:L715-L722
    //  CONSTANTS 3
    //  CONSUMER  Persistence (IN SCOPE) - Sql/ClauseModifier.cs
    // ==========================================================================================
    #region SQL Parser - enums.sru:L715-L722 (3 constants)

    // ------------------------------------------------------------------------------------------
    // THE CLAUSE-MODIFICATION STYLES ARE A PERSISTENCE CONTRACT - enums.sru:L718-L720
    // Persistence's Sql/ClauseModifier.cs consumes these three values, and the legacy validation they
    // travel with must be reproduced there: a ZERO clause index or an EMPTY clause string yields
    // RetCode.E_INVALID_ARGUMENT
    // [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L269-L284, applied at :L691].
    // The style value is carried on the query contract, so the spelling and the numbering are both
    // frozen. This is also the legacy SQL-injection site - the clause arrives as a raw string - which
    // the .NET side addresses with parameterised commands while keeping the observable generated
    // statement byte-identical.
    // ------------------------------------------------------------------------------------------
    // Modify styles
    public const long SQL_MS_REPLACE = 1;
    public const long SQL_MS_APPEND = 2;
    public const long SQL_MS_PREPEND = 3;

    // /*--- End SQL Parser ---*/   [enums.sru:L722]

    #endregion

    // ==========================================================================================
    //  SECTION 11 of 24 - Http
    //  ORACLE    ws_objects/pfw.shared.pbl.src/enums.sru:L724-L822
    //  CONSTANTS 79
    //  CONSUMER  deferred Integration - constants only, nothing behind them (C-D)
    // ==========================================================================================
    #region Http - enums.sru:L724-L822 (79 constants)

    // Methods (n_httpclient::Request:[method])
    public const string HTTP_METHOD_GET = "GET";
    public const string HTTP_METHOD_POST = "POST";
    public const string HTTP_METHOD_PUT = "PUT";
    public const string HTTP_METHOD_DELETE = "DELETE";
    public const string HTTP_METHOD_HEAD = "HEAD";

    // Proxy modes (n_httpclient::SetProxyMode/GetProxyMode)
    public const long HTTP_PROXY_NONE = 0;      // default
    public const long HTTP_PROXY_DEFAULT = 1;
    public const long HTTP_PROXY_AUTO = 2;
    public const long HTTP_PROXY_PROVIDED = 3;

    // Cert types (n_httpclient::SetCert/SetCA:[ntype])
    public const long HTTP_CERT_FILE = 1;         // .pem/crt/cer - X509/PKCS7 (PEM)
    public const long HTTP_CERT_FILE_DER = 2;     // .crt/cer/der - X509/PKCS7
    public const long HTTP_CERT_FILE_PKCS12 = 3;  // .pfx/p12
    public const long HTTP_CERT_PEM = 4;

    // Cert key types (n_httpclient::SetKey:[ntype])
    public const long HTTP_CERT_KEY_FILE = 1;      // .pem - Private key (PKCS8)
    public const long HTTP_CERT_KEY_FILE_RSA = 2;  // .pem - RSA private key
    public const long HTTP_CERT_KEY_PEM = 3;       // Private key (PKCS8)
    public const long HTTP_CERT_KEY_PEM_RSA = 4;   // RSA private key

    // ------------------------------------------------------------------------------------------
    // FIVE PAIRS OF DUPLICATE-VALUED ALIASES FOLLOW, AND THEY ARE INTENTIONAL - enums.sru:L753-L770
    //     HTTP_ENCODING_UTF16   == HTTP_ENCODING_UTF16LE   == 2
    //     HTTP_ENCODING_GB2312  == HTTP_ENCODING_GBK       == 5
    //     HTTP_ENCODING_ISO88591== HTTP_ENCODING_LATIN1    == 8
    //     HTTP_ENCODING_ISO88592== HTTP_ENCODING_LATIN2    == 9
    //     HTTP_ENCODING_ISO88593== HTTP_ENCODING_LATIN3    == 10
    // Both spellings of each pair are declared because both appear at call sites and in recordings.
    // Duplicate members like these are one of the two reasons this catalogue is `const` rather than
    // `enum`: a C# enum cannot carry two members with one value without losing the round trip from
    // value back to name.
    // ------------------------------------------------------------------------------------------
    // Encoding (n_httputility::StringToBlob/BlobToString/UrlEncode/UrlDecode:[encoding])
    // UrlEncode/UrlDecode - default:HTTP_ENCODING_UTF8
    // support:HTTP_ENCODING_UTF8/HTTP_ENCODING_GB2312/HTTP_ENCODING_GBK
    public const long HTTP_ENCODING_UNKNOWN = 0;
    public const long HTTP_ENCODING_UTF8 = 1;
    public const long HTTP_ENCODING_UTF16 = 2;
    public const long HTTP_ENCODING_UTF16LE = 2;
    public const long HTTP_ENCODING_UTF16BE = 3;
    public const long HTTP_ENCODING_ANSI = 4;
    public const long HTTP_ENCODING_GB2312 = 5;
    public const long HTTP_ENCODING_GBK = 5;
    public const long HTTP_ENCODING_GB18030 = 6;
    public const long HTTP_ENCODING_BIG5 = 7;
    public const long HTTP_ENCODING_ISO88591 = 8;
    public const long HTTP_ENCODING_LATIN1 = 8;
    public const long HTTP_ENCODING_ISO88592 = 9;
    public const long HTTP_ENCODING_LATIN2 = 9;
    public const long HTTP_ENCODING_ISO88593 = 10;
    public const long HTTP_ENCODING_LATIN3 = 10;
    public const long HTTP_ENCODING_ISO2022JP = 11;
    public const long HTTP_ENCODING_ISO2022KR = 12;

    // Http status (n_httpresponse::GetHttpStatus)
    public const long HTTP_STATUS_CONTINUE = 100;
    public const long HTTP_STATUS_SWITCH_PROTOCOLS = 101;
    public const long HTTP_STATUS_OK = 200;
    public const long HTTP_STATUS_CREATED = 201;
    public const long HTTP_STATUS_ACCEPTED = 202;
    public const long HTTP_STATUS_PARTIAL = 203;
    public const long HTTP_STATUS_NO_CONTENT = 204;
    public const long HTTP_STATUS_RESET_CONTENT = 205;
    public const long HTTP_STATUS_PARTIAL_CONTENT = 206;
    public const long HTTP_STATUS_WEBDAV_MULTI_STATUS = 207;
    public const long HTTP_STATUS_AMBIGUOUS = 300;
    public const long HTTP_STATUS_MOVED = 301;
    public const long HTTP_STATUS_REDIRECT = 302;
    public const long HTTP_STATUS_REDIRECT_METHOD = 303;
    public const long HTTP_STATUS_NOT_MODIFIED = 304;
    public const long HTTP_STATUS_USE_PROXY = 305;
    public const long HTTP_STATUS_REDIRECT_KEEP_VERB = 307;
    public const long HTTP_STATUS_BAD_REQUEST = 400;
    public const long HTTP_STATUS_DENIED = 401;
    public const long HTTP_STATUS_PAYMENT_REQ = 402;
    public const long HTTP_STATUS_FORBIDDEN = 403;
    public const long HTTP_STATUS_NOT_FOUND = 404;
    public const long HTTP_STATUS_BAD_METHOD = 405;
    public const long HTTP_STATUS_NONE_ACCEPTABLE = 406;
    public const long HTTP_STATUS_PROXY_AUTH_REQ = 407;
    public const long HTTP_STATUS_REQUEST_TIMEOUT = 408;
    public const long HTTP_STATUS_CONFLICT = 409;
    public const long HTTP_STATUS_GONE = 410;
    public const long HTTP_STATUS_LENGTH_REQUIRED = 411;
    public const long HTTP_STATUS_PRECOND_FAILED = 412;
    public const long HTTP_STATUS_REQUEST_TOO_LARGE = 413;
    public const long HTTP_STATUS_URI_TOO_LONG = 414;
    public const long HTTP_STATUS_UNSUPPORTED_MEDIA = 415;
    public const long HTTP_STATUS_RETRY_WITH = 449;
    public const long HTTP_STATUS_SERVER_ERROR = 500;
    public const long HTTP_STATUS_NOT_SUPPORTED = 501;
    public const long HTTP_STATUS_BAD_GATEWAY = 502;
    public const long HTTP_STATUS_SERVICE_UNAVAIL = 503;
    public const long HTTP_STATUS_GATEWAY_TIMEOUT = 504;
    public const long HTTP_STATUS_VERSION_NOT_SUP = 505;

    // Transfer types (n_httpclient::OnTransData:[transtype])
    public const long HTTP_TRANS_WRITE = 0;
    public const long HTTP_TRANS_READ = 1;

    // Upload types (n_httpclient::UploadFile:[ultype])
    public const long HTTP_UPLOAD_CHUNK = 0;
    public const long HTTP_UPLOAD_MULTIPART = 1;

    // /*--- End Http ---*/   [enums.sru:L822]

    #endregion

    // ==========================================================================================
    //  SECTION 12 of 24 - WebSocket
    //  ORACLE    ws_objects/pfw.shared.pbl.src/enums.sru:L824-L902
    //  CONSTANTS 64
    //  CONSUMER  deferred Integration - constants only, nothing behind them (C-D)
    // ==========================================================================================
    #region WebSocket - enums.sru:L824-L902 (64 constants)

    // States (n_wsclient::GetState)
    public const long WS_STATE_CONNECTING = 0;
    public const long WS_STATE_OPEN = 1;
    public const long WS_STATE_CLOSING = 2;
    public const long WS_STATE_CLOSED = 3;

    // ------------------------------------------------------------------------------------------
    // THE WEBSOCKET CERTIFICATE CONSTANTS ARE CROSS-SECTION ALIASES OF THE HTTP ONES.
    // enums.sru:L833-L836 and :L839-L842 define eight WS_CERT_* values as the corresponding
    // HTTP_CERT_* value rather than as a literal. They are transcribed as those same references, so
    // the alias relationship survives - it is the documentation that the two transports share one
    // certificate-type vocabulary.
    //
    // This has a hard consequence for FILE LAYOUT: the Http region MUST precede the WebSocket region,
    // because a C# `const` may only reference a `const` that is already declared. Legacy section order
    // is therefore a COMPILE requirement here, not merely a fidelity preference, and no region in this
    // file may be reordered.
    // ------------------------------------------------------------------------------------------
    // Cert types (n_wsclient::SetCert/SetCA:[ntype])
    public const long WS_CERT_FILE = HTTP_CERT_FILE;                // .pem/crt/cer - X509/PKCS7 (PEM)
    public const long WS_CERT_FILE_DER = HTTP_CERT_FILE_DER;        // .crt/cer/der - X509/PKCS7
    public const long WS_CERT_FILE_PKCS12 = HTTP_CERT_FILE_PKCS12;  // .pfx/p12
    public const long WS_CERT_PEM = HTTP_CERT_PEM;

    // Cert key types (n_wsclient::SetKey:[ntype])
    public const long WS_CERT_KEY_FILE = HTTP_CERT_KEY_FILE;          // .pem - Private key (PKCS8)
    public const long WS_CERT_KEY_FILE_RSA = HTTP_CERT_KEY_FILE_RSA;  // .pem - RSA private key
    public const long WS_CERT_KEY_PEM = HTTP_CERT_KEY_PEM;            // Private key (PKCS8)
    public const long WS_CERT_KEY_PEM_RSA = HTTP_CERT_KEY_PEM_RSA;    // RSA private key

    // Close status (n_wsclient::OnClose:[code])
    public const long WS_CLOSE_NORMAL = 1000;
    public const long WS_CLOSE_GOING_AWAY = 1001;
    public const long WS_CLOSE_PROTOCOL_ERROR = 1002;
    public const long WS_CLOSE_UNSUPPORTED_DATA = 1003;
    public const long WS_CLOSE_NO_STATUS = 1005;
    public const long WS_CLOSE_ABNORMAL_CLOSE = 1006;
    public const long WS_CLOSE_INVALID_PAYLOAD = 1007;
    public const long WS_CLOSE_POLICY_VIOLATION = 1008;
    public const long WS_CLOSE_MESSAGE_TOO_BIG = 1009;
    public const long WS_CLOSE_EXTENSION_REQUIRED = 1010;
    public const long WS_CLOSE_INTERNAL_ENDPOINT_ERROR = 1011;
    public const long WS_CLOSE_TLS_HANDSHAKE = 1015;
    public const long WS_CLOSE_SUBPROTOCOL_ERROR = 3000;
    public const long WS_CLOSE_INVALID_SUBPROTOCOL_DATA = 3001;
    public const long WS_CLOSE_MQTT_CONN_TIMEOUT = 4001;
    public const long WS_CLOSE_MQTT_PING_TIMEOUT = 4002;
    public const long WS_CLOSE_MQTT_CONN_DENIED = 4003;
    public const long WS_CLOSE_MQTT_CONN_AUTH_FAILED = 4004;

    // Error codes (n_wsclient::OnError:[code])
    public const long WS_E_GENERAL = 1;
    public const long WS_E_SEND_QUEUE_FULL = 2;
    public const long WS_E_PAYLOAD_VIOLATION = 3;
    public const long WS_E_ENDPOINT_NOT_SECURE = 4;
    public const long WS_E_ENDPOINT_UNAVAILABLE = 5;
    public const long WS_E_INVALID_URI = 6;
    public const long WS_E_NO_OUTGOING_BUFFERS = 7;
    public const long WS_E_NO_INCOMING_BUFFERS = 8;
    public const long WS_E_INVALID_STATE = 9;
    public const long WS_E_BAD_CLOSE_CODE = 10;
    public const long WS_E_RESERVED_CLOSE_CODE = 11;
    public const long WS_E_INVALID_CLOSE_CODE = 12;
    public const long WS_E_INVALID_UTF8 = 13;
    public const long WS_E_INVALID_SUBPROTOCOL = 14;
    public const long WS_E_BAD_CONNECTION = 15;
    public const long WS_E_OPEN_HANDSHAKE_TIMEOUT = 22;
    // ------------------------------------------------------------------------------------------
    // A GENUINE LEGACY VALUE COLLISION, PRESERVED - enums.sru:L881 and enums.sru:L882
    // WS_E_CLOSE_HANDSHAKE_TIMEOUT and WS_E_INVALID_PORT are BOTH 23, so the two error conditions are
    // indistinguishable by code. Neighbouring values 22 and 26 leave 24 and 25 free, which makes this
    // look like a transcription slip in the original rather than a design choice. It is reproduced
    // exactly as it stands: correcting it would change observable behaviour, which this refactor
    // forbids (C-B).
    // ------------------------------------------------------------------------------------------
    public const long WS_E_CLOSE_HANDSHAKE_TIMEOUT = 23;
    public const long WS_E_INVALID_PORT = 23;
    public const long WS_E_OPERATION_CANCELED = 26;
    public const long WS_E_REJECTED = 27;
    public const long WS_E_EXTENSION_NEG_FAILED = 32;
    public const long WS_E_MQTT_CONN_DENIED = 401;
    public const long WS_E_MQTT_CONN_AUTH_FAILED = 402;
    public const long WS_E_MQTT_CONN_TIMEOUT = 403;
    public const long WS_E_MQTT_PUB_TIMEOUT = 404;
    public const long WS_E_MQTT_SUB_TIMEOUT = 405;
    public const long WS_E_MQTT_PUB_FAILED = 406;
    public const long WS_E_MQTT_SUB_FAILED = 407;

    // MQTT over WebSocket
    public const long WS_MQTT_QOS0 = 0;
    public const long WS_MQTT_QOS1 = 1;
    public const long WS_MQTT_QOS2 = 2;
    public const long WS_MQTT_QOS_AT_MOST_ONCE = WS_MQTT_QOS0;
    public const long WS_MQTT_QOS_AT_LEAST_ONCE = WS_MQTT_QOS1;
    public const long WS_MQTT_QOS_AT_EXACTLY_ONCE = WS_MQTT_QOS2;

    // /*--- End WebSocket ---*/   [enums.sru:L902]

    #endregion

    // ==========================================================================================
    //  SECTION 13 of 24 - Ftp
    //  ORACLE    ws_objects/pfw.shared.pbl.src/enums.sru:L904-L919
    //  CONSTANTS 7
    //  CONSUMER  deferred Integration - constants only, nothing behind them (C-D)
    // ==========================================================================================
    #region Ftp - enums.sru:L904-L919 (7 constants)

    // Ftp mode (n_ftpclient::SetMode:[mode])
    public const long FTP_MODE_ACTIVE = 0;
    public const long FTP_MODE_PASSIVE = 1;

    // File list flags (n_ftpclient::List:[flags])
    public const long FTP_LIST_FILE = 1;
    public const long FTP_LIST_DIR = 2;
    public const long FTP_LIST_ALL = 3;

    // File compare flags (n_ftpclient::CompareFile:[flags])
    public const long FTP_CMP_FLAG_LAST_WRITE_TIME = 1;
    public const long FTP_CMP_FLAG_SIZE = 2;

    // /*--- End Ftp ---*/   [enums.sru:L919]

    #endregion

    // ==========================================================================================
    //  SECTION 14 of 24 - Crypto
    //  ORACLE    ws_objects/pfw.shared.pbl.src/enums.sru:L921-L969
    //  CONSTANTS 30
    //  CONSUMER  Security (IN SCOPE) - Crypto/LegacyDefaults.cs and the provider set
    // ==========================================================================================
    #region Crypto - enums.sru:L921-L969 (30 constants)

    // ------------------------------------------------------------------------------------------
    // CRYPTOGRAPHIC ALGORITHM IDENTIFIERS - IDENTIFIERS, NOT SECRETS - enums.sru:L924-L967
    // Nothing in this region is key material, so transcribing it does not engage the secrets
    // constraint (C-F) at all. What it does carry is the legacy's set of WEAK DEFAULTS, which the
    // Security service's Crypto/LegacyDefaults.cs centralises and annotates in the published contract:
    //
    //     CRYPTO_SYMCRYPT_MODE_DEFAULT resolves to ECB          [L946] - a known weakness, preserved
    //     CRYPTO_RSA_PADDING_DEFAULT   resolves to PKCS#1       [L951] - no-padding is unsupported
    //     CRYPTO_RSA_BITS_1024         remains a legal key size [L965]
    //     no key-derivation function and no authenticated-encryption mode exist anywhere in the set
    //
    // Each is preserved AS THE DEFAULT and annotated, never corrected (C-B). Deliberately absent from
    // this file: any default-SELECTION logic. Choosing an algorithm is behaviour and belongs to
    // Security; this region declares names and values only.
    // ------------------------------------------------------------------------------------------
    // Encoding (n_crypto::StringToBlob/BlobToString:[encoding])
    public const long CRYPTO_ENCODING_BASE64 = 0;
    public const long CRYPTO_ENCODING_HEX = 1;

    // Hash types (n_crypto::Hash/RSASign/VerifyRSASign:[ntype])
    public const long CRYPTO_HASH_MD5 = 0;
    public const long CRYPTO_HASH_SHA1 = 1;
    public const long CRYPTO_HASH_SHA256 = 2;
    public const long CRYPTO_HASH_SHA384 = 3;
    public const long CRYPTO_HASH_SHA512 = 4;
    public const long CRYPTO_HASH_CRC32 = 5;

    // SymCrypt types (n_crypto::SymEncrypt/SymDecrypt:[ntype])
    public const long CRYPTO_SYMCRYPT_TYPE_DES = 0;
    public const long CRYPTO_SYMCRYPT_TYPE_3DES = 1;
    public const long CRYPTO_SYMCRYPT_TYPE_AES128 = 2;
    public const long CRYPTO_SYMCRYPT_TYPE_AES192 = 3;
    public const long CRYPTO_SYMCRYPT_TYPE_AES256 = 4;

    // SymCrypt modes (n_crypto::SymEncrypt/SymDecrypt:[mode])
    public const long CRYPTO_SYMCRYPT_MODE_ECB = 0;
    public const long CRYPTO_SYMCRYPT_MODE_CBC = 1;
    public const long CRYPTO_SYMCRYPT_MODE_CFB = 2;
    public const long CRYPTO_SYMCRYPT_MODE_DEFAULT = CRYPTO_SYMCRYPT_MODE_ECB;

    // RSA padding modes (n_crypto::RSAEncrypt/RSADecrypt:[padding])
    public const long CRYPTO_RSA_PADDING_PKCS1 = 0;                           // RSA_PKCS1_PADDING
    public const long CRYPTO_RSA_PADDING_OAEP = 1;                            // RSA_PKCS1_OAEP_PADDING
    public const long CRYPTO_RSA_PADDING_DEFAULT = CRYPTO_RSA_PADDING_PKCS1;

    // Random string flags (n_crypto::GenRandomString:[flags])
    public const uint CRYPTO_RNDSTRING_NUMBER = 1;
    public const uint CRYPTO_RNDSTRING_ALPHABET = 2;
    public const uint CRYPTO_RNDSTRING_SYMBOL = 4;
    public const uint CRYPTO_RNDSTRING_DEFAULT = CRYPTO_RNDSTRING_NUMBER + CRYPTO_RNDSTRING_ALPHABET;

    // Guid flags (n_crypto::GenGuid:[flags])
    public const uint CRYPTO_GUID_INCLUDE_BRACKET = 1;
    public const uint CRYPTO_GUID_INCLUDE_SEPARATOR = 2;
    public const uint CRYPTO_GUID_DEFAULT = CRYPTO_GUID_INCLUDE_BRACKET + CRYPTO_GUID_INCLUDE_SEPARATOR;

    // RSA predefine bits (n_crypto::GenRSAKey:[bits])
    public const ushort CRYPTO_RSA_BITS_1024 = 1024;
    public const ushort CRYPTO_RSA_BITS_2048 = 2048;
    public const ushort CRYPTO_RSA_BITS_4096 = 4096;

    // /*--- End Crypto ---*/   [enums.sru:L969]

    #endregion

    // ==========================================================================================
    //  SECTION 15 of 24 - Regular Expression
    //  ORACLE    ws_objects/pfw.shared.pbl.src/enums.sru:L971-L999
    //  CONSTANTS 11
    //  CONSUMER  deferred Documents - the one in-scope use is met by System.Text.RegularExpressions
    //            (C-D)
    //  NOTE      the source opens this section but never closes it with an
    //            'End' banner; it runs on until the next opening banner.
    // ==========================================================================================
    #region Regular Expression - enums.sru:L971-L999 (11 constants)

    // Match flags (n_regexp::Match/FindFirst/FindLast/Find/Replace:[flags])

    // Default matching behavior.**.
    public const uint REGEXP_MATCH_DEFAULT = 0;
    // The first character is not considered a beginning of line ("^" does not match).
    public const uint REGEXP_MATCH_NOT_BOL = 1;
    // The last character is not considered an end of line ("$" does not match).
    public const uint REGEXP_MATCH_NOT_EOL = 2;
    // The escape sequence "\b" does not match as a beginning-of-word.
    public const uint REGEXP_MATCH_NOT_BOW = 4;
    // The escape sequence "\b" does not match as an end-of-word.
    public const uint REGEXP_MATCH_NOT_EOW = 8;
    // Any match is acceptable if more than one match is possible.
    public const uint REGEXP_MATCH_ANY = 16;
    // Empty sequences do not match.
    public const uint REGEXP_MATCH_NOT_NULL = 32;
    // The expression must match a sub-sequence that begins at the first character.
    // Sub-sequences must begin at the first character to match.
    public const uint REGEXP_MATCH_CONTINUOUS = 64;
    // One or more characters exist before the first one. (match_not_bol and match_not_bow are ignored)
    public const uint REGEXP_MATCH_PREV_AVAIL = 256;
    // Only replace the first match in std::regex_replace
    public const uint REGEXP_FORMAT_FIRST_ONLY = 4096;
    // Global match (Find only)
    public const uint REGEXP_MATCH_GLOBAL = 65536;

    // LEGACY DEFECT, REPRODUCED VERBATIM - enums.sru:L999
    // The banner that closes the Regular Expression section is mislabelled in the
    // source: it reads 'End Crypto', duplicating the banner that genuinely closes the
    // Crypto section at enums.sru:L969. It is a copy-paste slip by the legacy author.
    // It is reproduced here exactly as it stands, as a comment, because defects are
    // preserved rather than corrected - it is legacy text, NOT a porting error, and NOT
    // something to tidy.
    // /*--- End Crypto ---*/   [enums.sru:L999]

    #endregion

    // ==========================================================================================
    //  SECTION 16 of 24 - Zip
    //  ORACLE    ws_objects/pfw.shared.pbl.src/enums.sru:L1001-L1009
    //  CONSTANTS 4
    //  CONSUMER  deferred Documents - constants only, nothing behind them (C-D)
    // ==========================================================================================
    #region Zip - enums.sru:L1001-L1009 (4 constants)

    // Formats (ZipCompress/ZipUncompress:[format])
    public const long ZIP_FORMAT_PFW = 0;
    public const long ZIP_FORMAT_GZIP = 1;
    public const long ZIP_FORMAT_ZLIB = 2;
    public const long ZIP_FORMAT_DEFAULT = ZIP_FORMAT_PFW;

    // /*--- End Zip ---*/   [enums.sru:L1009]

    #endregion

    // ==========================================================================================
    //  SECTION 17 of 24 - Device Info
    //  ORACLE    ws_objects/pfw.shared.pbl.src/enums.sru:L1011-L1022
    //  CONSTANTS 7
    //  CONSUMER  deferred Documents - constants only, nothing behind them (C-D)
    // ==========================================================================================
    #region Device Info - enums.sru:L1011-L1022 (7 constants)

    // Device types (n_devinfo::GetDevice/GetDevices/MatchDevice:[ntype])
    public const long DEV_TYPE_HDD = 0;
    public const long DEV_TYPE_MAC = 1;
    public const long DEV_TYPE_CPUID = 2;
    public const long DEV_TYPE_MAINBOARD = 3;
    public const long DEV_TYPE_BIOS = 4;
    public const long DEV_TYPE_IPV4 = 5;
    public const long DEV_TYPE_IPV6 = 6;

    // /*--- End Device Info ---*/   [enums.sru:L1022]

    #endregion

    // ==========================================================================================
    //  SECTION 18 of 24 - Logger
    //  ORACLE    ws_objects/pfw.shared.pbl.src/enums.sru:L1024-L1041
    //  CONSTANTS 11
    //  CONSUMER  deferred Documents - constants only, nothing behind them (C-D)
    // ==========================================================================================
    #region Logger - enums.sru:L1024-L1041 (11 constants)

    // Log levels (n_logger::SetLevel[level])
    public const uint LOG_LEVEL_NONE = 0;
    public const uint LOG_LEVEL_ERROR = 1;
    public const uint LOG_LEVEL_WARNING = 2;
    public const uint LOG_LEVEL_INFO = 4;
    public const uint LOG_LEVEL_DEBUG = 8;
    public const uint LOG_LEVEL_CUSTOM = 16;
    // ------------------------------------------------------------------------------------------
    // LOG_LEVEL_ALL = 4294967295 IS THE PROOF OF THE WIDTH MAPPING - enums.sru:L1033
    // That value is 0xFFFFFFFF, i.e. every bit of a 32-bit unsigned word. It fits C# `uint` EXACTLY
    // and does not fit `int` at all, which is independent confirmation that PB `Ulong` must map to
    // `uint` here and not to a signed 32-bit type. See the TYPE MAPPING block in the file header.
    // ------------------------------------------------------------------------------------------
    public const uint LOG_LEVEL_ALL = 4294967295;
    public const uint LOG_LEVEL_DEFAULT = LOG_LEVEL_ERROR + LOG_LEVEL_WARNING + LOG_LEVEL_INFO;

    // Clear flags (n_logger::Clear:[flags])
    public const uint LOG_CLEAR_FILE = 1;
    public const uint LOG_CLEAR_CONSOLE = 2;
    public const uint LOG_CLEAR_DEFAULT = LOG_CLEAR_FILE + LOG_CLEAR_CONSOLE;

    // /*--- End Logger ---*/   [enums.sru:L1041]

    #endregion

    // ==========================================================================================
    //  SECTION 19 of 24 - Compiler
    //  ORACLE    ws_objects/pfw.shared.pbl.src/enums.sru:L1043-L1057
    //  CONSTANTS 10
    //  CONSUMER  deferred ScriptBridge - constants only, nothing behind them (C-D)
    // ==========================================================================================
    #region Compiler - enums.sru:L1043-L1057 (10 constants)

    // Compile types (n_compiler::Import:[ntype])
    public const long CMP_TYPE_DATAWINDOW = 1;
    public const long CMP_TYPE_FUNCTION = 2;
    public const long CMP_TYPE_MENU = 3;
    public const long CMP_TYPE_QUERY = 4;
    public const long CMP_TYPE_STRUCTURE = 5;
    public const long CMP_TYPE_USEROBJECT = 6;
    public const long CMP_TYPE_WINDOW = 7;
    public const long CMP_TYPE_PIPELINE = 8;
    public const long CMP_TYPE_PROJECT = 9;
    public const long CMP_TYPE_PROXYOBJECT = 10;

    // /*--- End Compiler ---*/   [enums.sru:L1057]

    #endregion

    // ==========================================================================================
    //  SECTION 20 of 24 - Barcode
    //  ORACLE    ws_objects/pfw.shared.pbl.src/enums.sru:L1059-L1093
    //  CONSTANTS 21
    //  CONSUMER  deferred Documents - constants only, nothing behind them (C-D)
    // ==========================================================================================
    #region Barcode - enums.sru:L1059-L1093 (21 constants)

    // ------------------------------------------------------------------------------------------
    // THE BARCODE OPTION IDENTIFIERS DELIBERATELY OVERLAP - enums.sru:L1062-L1067
    // BARCODE_OPT_ROWS, BARCODE_OPT_SECURE and BARCODE_OPT_MODE are all 1; BARCODE_OPT_COLUMNS and
    // BARCODE_OPT_VERSION are both 2. This is not an error: the underlying option slot is reused with
    // a different meaning per symbology, and each trailing comment names the symbology it applies to.
    // All six names are declared, exactly as the source declares them.
    // ------------------------------------------------------------------------------------------
    // Options (n_barcode::GetOption/SetOption:[option])
    public const long BARCODE_OPT_OUTPUT = 0;   // output options
    public const long BARCODE_OPT_ROWS = 1;     // number of rows (Codablock-F)
    public const long BARCODE_OPT_COLUMNS = 2;  // the number of data columns in symbol
    public const long BARCODE_OPT_VERSION = 2;  // symbol version (QR Code/Han Xin)
    public const long BARCODE_OPT_SECURE = 1;   // error correction level
    public const long BARCODE_OPT_MODE = 1;     // encoding mode (Maxicode/Composite)

    // Option Values (n_barcode::SetOption:[val])
    // output options (BARCODE_OPT_OUTPUT)
    public const long BARCODE_OPTV_OUTPUT_READER_INIT = 16;  // create reader initialisation/programming symbol
    public const long BARCODE_OPTV_OUTPUT_SMALL_TEXT = 32;   // use half-size text
    public const long BARCODE_OPTV_OUTPUT_BOLD_TEXT = 64;    // use bold text

    // Unit types (n_barcode::GetUnit/SetUnit:[unit])
    public const long BARCODE_UNIT_PIXEL = 0;
    public const long BARCODE_UNIT_DIP = 1;
    public const long BARCODE_UNIT_DEFAULT = BARCODE_UNIT_PIXEL;

    // Generate SVG types (n_barcode::GenSvgString:[ntype])
    public const long BARCODE_SVG_FULL = 0;
    public const long BARCODE_SVG_INLINE = 1;
    public const long BARCODE_SVG_DEFAULT = BARCODE_SVG_FULL;

    // Generate Data types (n_barcode::GenDataString:[ntype])
    public const long BARCODE_DATA_PNG = 0;                     // image/png
    public const long BARCODE_DATA_JPEG = 1;                    // image/jpeg
    public const long BARCODE_DATA_GIF = 2;                     // image/gif
    public const long BARCODE_DATA_BMP = 3;                     // image/bmp
    public const long BARCODE_DATA_SVG = 4;                     // image/svg+xml
    public const long BARCODE_DATA_DEFAULT = BARCODE_DATA_PNG;

    // /*--- End Barcode ---*/   [enums.sru:L1093]

    #endregion

    // ==========================================================================================
    //  SECTION 21 of 24 - QR Code
    //  ORACLE    ws_objects/pfw.shared.pbl.src/enums.sru:L1095-L1124
    //  CONSTANTS 19
    //  CONSUMER  deferred Documents - constants only, nothing behind them (C-D)
    // ==========================================================================================
    #region QR Code - enums.sru:L1095-L1124 (19 constants)

    // Ecc levels (n_qrcode::GetEcc/SetEcc:[ecl])
    public const long QRCODE_ECC_AUTO = 0;
    public const long QRCODE_ECC_LOW = 1;
    public const long QRCODE_ECC_MEDIUM = 2;
    public const long QRCODE_ECC_QUARTILE = 3;
    public const long QRCODE_ECC_HIGH = 4;
    public const long QRCODE_ECC_DEFAULT = QRCODE_ECC_AUTO;

    // Unit types (n_qrcode::GetUnit/SetUnit:[unit])
    public const long QRCODE_UNIT_PIXEL = 0;
    public const long QRCODE_UNIT_DIP = 1;
    public const long QRCODE_UNIT_DEFAULT = QRCODE_UNIT_PIXEL;

    // Generate SVG types (n_qrcode::GenSvgString:[ntype])
    public const long QRCODE_SVG_FULL = 0;
    public const long QRCODE_SVG_INLINE = 1;
    public const long QRCODE_SVG_PATH = 2;
    public const long QRCODE_SVG_DEFAULT = QRCODE_SVG_FULL;

    // Generate Data types (n_qrcode::GenDataString:[ntype])
    public const long QRCODE_DATA_PNG = 0;                    // image/png
    public const long QRCODE_DATA_JPEG = 1;                   // image/jpeg
    public const long QRCODE_DATA_GIF = 2;                    // image/gif
    public const long QRCODE_DATA_BMP = 3;                    // image/bmp
    public const long QRCODE_DATA_SVG = 4;                    // image/svg+xml
    public const long QRCODE_DATA_DEFAULT = QRCODE_DATA_PNG;

    // /*--- End QR Code ---*/   [enums.sru:L1124]

    #endregion

    // ==========================================================================================
    //  SECTION 22 of 24 - File Scanner
    //  ORACLE    ws_objects/pfw.shared.pbl.src/enums.sru:L1126-L1132
    //  CONSTANTS 2
    //  CONSUMER  deferred Documents - constants only, nothing behind them (C-D)
    // ==========================================================================================
    #region File Scanner - enums.sru:L1126-L1132 (2 constants)

    // Scan flags
    public const uint FILE_SCANNER_SORT_ASC = 65536;
    public const uint FILE_SCANNER_SORT_DESC = 131072;

    // /*--- End File Scanner ---*/   [enums.sru:L1132]

    #endregion

    // ==========================================================================================
    //  SECTION 23 of 24 - Camera Capture
    //  ORACLE    ws_objects/pfw.shared.pbl.src/enums.sru:L1134-L1142
    //  CONSTANTS 4
    //  CONSUMER  no in-scope consumer in Phase 1; image handling maps to deferred DesignSystem and
    //            file formats to deferred Documents (C-D)
    // ==========================================================================================
    #region Camera Capture - enums.sru:L1134-L1142 (4 constants)

    // Image file format
    public const long IMAGE_FILE_FORMAT_JPEG = 0;
    public const long IMAGE_FILE_FORMAT_PNG = 1;
    public const long IMAGE_FILE_FORMAT_GIF = 2;
    public const long IMAGE_FILE_FORMAT_BMP = 3;

    // /*--- End Camera Capture ---*/   [enums.sru:L1142]

    #endregion

    // ==========================================================================================
    //  SECTION 24 of 24 - Pinyin
    //  ORACLE    ws_objects/pfw.shared.pbl.src/enums.sru:L1144-L1151
    //  CONSTANTS 3
    //  CONSUMER  DataServices (IN SCOPE) - Expressions/PinyinFirstLetterMatcher.cs
    // ==========================================================================================
    #region Pinyin - enums.sru:L1144-L1151 (3 constants)

    // ------------------------------------------------------------------------------------------
    // THE PINYIN FLAG CONTRACT IS DOCUMENTED HERE - AND THAT SETTLES AN OPEN QUESTION.
    // The three flags below are the [flags] argument of pfwPinyinFirstLetterLike
    // [ws_objects/pfw.utility.pbl.src/pinyinfirstletterlike.srf]. The sole in-scope call site is
    // ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_dropdownsearch.sru:L323, which passes
    // the literal 7. That literal is fully explained by this region:
    //
    //     7 == 1 | 2 | 4
    //       == PY_LIKE_IGNORE_CASE | PY_LIKE_IGNORE_WIDTH | PY_LIKE_FUZZY_SOUND
    //       == all three flags on
    //
    // So the flag SEMANTICS are documented, not undocumented: the plan's special analysis recorded
    // them as unknown, and this region falsifies that. DataServices'
    // Expressions/PinyinFirstLetterMatcher.cs must therefore compose Enums.PY_LIKE_* rather than
    // hardcode a magic 7.
    //
    // WHAT REMAINS GENUINELY UNKNOWN, stated without overclaiming: the pinyin LOOKUP TABLE itself
    // exists only inside the closed pfw.dll and there is no C++ source for it anywhere in the
    // repository. Knowing the flags does not yield the table. Bit-exact parity for the matcher
    // therefore still requires the behavioural oracle, and if the oracle cannot be exercised the
    // matcher must be reported BLOCKED rather than approximated. The flag contract is now known; the
    // table is not.
    //
    // TRANSLATION NOTE: the trailing comment on each of the three constants below is Chinese in the
    // source [enums.sru:L1147, :L1148, :L1149] and appears here in English. The meaning is unchanged,
    // and the source remains the oracle for the original wording.
    //
    // DECLARATION-CASE NOTE: these three are the only constants in the whole 1,162-line source
    // declared in lowercase (`constant long` rather than `Constant Long`), which is why a
    // case-sensitive grep of the source counts 720 instead of 723. The inconsistency is invisible in
    // C# - `const` is a keyword either way - so nothing about it is reproduced beyond this note.
    // ------------------------------------------------------------------------------------------
    // PinyinFirstLetterLike:[flags]
    public const long PY_LIKE_IGNORE_CASE = 1;   // ignore case
    public const long PY_LIKE_IGNORE_WIDTH = 2;  // ignore full-width versus half-width forms
    public const long PY_LIKE_FUZZY_SOUND = 4;   // match fuzzy pronunciation (l=n, f=h, r=l)

    // /*--- End Pinyin ---*/   [enums.sru:L1151]

    #endregion
}
