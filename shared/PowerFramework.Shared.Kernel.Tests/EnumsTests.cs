// ==================================================================================================
//  EnumsTests.cs - THE CONSTANT CATALOGUE, CHECKED AGAINST THE LEGACY DECLARATION FILE
//  ------------------------------------------------------------------------------------------------
//  UNIT UNDER TEST   PowerFramework.Shared.Kernel.Enums
//  ORACLE            ws_objects/pfw.shared.pbl.src/enums.sru  (1,162 lines, 723 declarations)
//
//  WHY THIS SUITE EXISTS, AND WHY IT IS A TABLE RATHER THAN A SAMPLE
//  ------------------------------------------------------------------------------------------------
//  Enums.cs declares 723 public constants and, before this file, executable C# exercised six of
//  them. That gap is not the usual "more coverage would be nice": these identifiers and their
//  values TRAVEL. AAP 0.4.5.3 keeps their SCREAMING_SNAKE spelling verbatim precisely because they
//  appear in serialized payloads, in log records and in stored characterization recordings, so a
//  renamed identifier or a shifted value silently invalidates every stored comparison rather than
//  breaking a build. A constant is also the one kind of member that cannot fail at run time in a
//  way anyone would notice - it is inlined into its consumers at compile time - so if the value is
//  wrong, nothing anywhere reports it. A table is the only instrument that catches that.
//
//  THE EXPECTATIONS COME FROM THE ORACLE, NOT FROM THE PORT
//  ------------------------------------------------------------------------------------------------
//  Every row below was extracted from ws_objects/pfw.shared.pbl.src/enums.sru - the read-only
//  legacy declaration file - and carries the line number it came from, so a failure names the
//  oracle line to open rather than leaving the reader to search. That direction matters: a table
//  generated from Enums.cs would be a tautology that passes no matter what the file says. The
//  extraction resolved the oracle's own non-literal right-hand sides the way PowerScript does:
//
//      L49    INIT_FLAG_ENABLE_ALL     a seven-term sum of previously declared flags
//      L196   SC_HANDLE_INVOKE_METHOD  the literal sum 1024 + 512
//      L655   XML_PARSE_DEFAULT        a four-term sum
//      L659   XML_PARSE_FULL           a five-term sum, one term of which is itself a sum
//      L681   XML_FORMAT_DEFAULT       an alias of a single flag
//      L711   JSON_FORMAT_DEFAULT      an alias of a single flag
//      plus the WS_CERT_*, WS_MQTT_QOS_*, CRYPTO_*_DEFAULT, ZIP_FORMAT_DEFAULT, LOG_*_DEFAULT,
//      BARCODE_*_DEFAULT and QRCODE_*_DEFAULT aliases, each resolved through its referent
//
//  THE POWERSCRIPT TO C# TYPE MAPPING IS PART OF WHAT IS BEING CHECKED
//  ------------------------------------------------------------------------------------------------
//  PowerBuilder's integer widths do not match the names a C# reader expects, and getting this wrong
//  would be invisible until a value overflowed:
//
//      Constant Long   ->  long      502 + 3 declarations = 505    (the 3 are lower-case `constant`)
//      Constant Ulong  ->  uint      179 declarations              PowerScript ulong is 32-BIT
//      Constant Uint   ->  ushort     34 declarations              PowerScript uint  is 16-BIT
//      Constant String ->  string      5 declarations
//                                    ---
//                                    723
//
//  The mapping is load-bearing rather than cosmetic: 18 of the 179 `Ulong` constants exceed
//  ushort.MaxValue - HTTP_STATUS and the Sciter bitmasks among them, up to uint.MaxValue itself -
//  so mapping Ulong onto ushort would not compile, and mapping Uint onto uint would silently widen
//  34 constants that the legacy stores in 16 bits. Both directions are asserted below.
//
//  WHAT EACH TEST IN THIS FILE IS FOR
//  ------------------------------------------------------------------------------------------------
//   1. EveryConstantMatchesItsLegacyDeclaration    723 rows: identifier, CLR type, value.
//   2. EveryPublicConstantAppearsInTheOracleTable  the reverse direction - nothing in Enums.cs is
//                                                  absent from the table, so a constant cannot be
//                                                  ADDED without a row.
//   3. TheDeclarationCensusMatchesTheOracle        the counts and the per-type histogram.
//   4. EverySectionCountMatchesItsOracleBanner     the 24 sections and their line ranges.
//   5. EveryMemberIsAPublicConstAndNothingElse     no static readonly, no property, no method.
//   6. ... then the named behavioural facts, one per family that a consumer depends on.
//
//  RULES POSITION
//  review_rules returns "No user rules provided.", so no user-specified rule governs this file. The
//  enterprise baseline applies, and the binding constraints cited inline are C-B (replicate legacy
//  behaviour and defects, never improve), C-C (the legacy tree is read-only and is the oracle) and
//  C-K (document every boundary decision).
// ==================================================================================================

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Xunit;

namespace PowerFramework.Shared.Kernel.Tests;

/// <summary>
/// Parity tests for <see cref="Enums"/>, checked row by row against
/// <c>ws_objects/pfw.shared.pbl.src/enums.sru</c>.
/// </summary>
/// <remarks>
/// No <c>using PowerFramework.Shared.Kernel;</c> directive appears above: this namespace is nested
/// inside it, so simple-name lookup walks outward and finds <see cref="Enums"/> there.
/// </remarks>
public class EnumsTests
{
    // ==============================================================================================
    //  THE ORACLE TABLE
    // ==============================================================================================

    /// <summary>
    /// One entry per declaration in <c>enums.sru</c>: the identifier, the expected CLR type keyword,
    /// the expected value rendered invariantly, and the oracle line the entry was transcribed from.
    /// </summary>
    /// <remarks>
    /// Held as a strongly-typed table and projected into <see cref="OracleRows"/> for the theory,
    /// rather than the other way round. Several facts in this file need to READ the table - to prove
    /// the reverse direction, to partition it by section - and reading it back out of a
    /// <see cref="TheoryData{T1, T2, T3, T4}"/> would mean unpacking positional rows, which is exactly
    /// where a silent column mix-up hides.
    /// </remarks>
    /// <returns>All 723 entries, in the oracle's own declaration order.</returns>
    private static (string Identifier, string ExpectedType, string ExpectedValue, int OracleLine)[] BuildOracleTable()
    {
        List<(string Identifier, string ExpectedType, string ExpectedValue, int OracleLine)> rows = [];

        // ---- PowerFramework enums  [enums.sru:L41-L49] ----
        rows.Add(("INIT_FLAG_ENABLE_UI", "long", "1", 41));
        rows.Add(("INIT_FLAG_ENABLE_SCITER", "long", "2", 42));
        rows.Add(("INIT_FLAG_ENABLE_BLINK", "long", "4", 43));
        rows.Add(("INIT_FLAG_ENABLE_BLINKFAST", "long", "8", 44));
        rows.Add(("INIT_FLAG_ENABLE_ORCA", "long", "256", 45));
        rows.Add(("INIT_FLAG_ENABLE_SQLITE", "long", "512", 46));
        rows.Add(("INIT_FLAG_ENABLE_DPIAWARE", "long", "1024", 47));
        rows.Add(("INIT_FLAG_ENABLE_WEBVIEW", "long", "2048", 48));
        rows.Add(("INIT_FLAG_ENABLE_ALL", "long", "3847", 49));

        // ---- UI  [enums.sru:L54-L111] ----
        rows.Add(("HORZ", "ushort", "0", 54));
        rows.Add(("VERT", "ushort", "1", 55));
        rows.Add(("SMALL", "ushort", "16", 57));
        rows.Add(("MEDIUM", "ushort", "24", 58));
        rows.Add(("LARGE", "ushort", "32", 59));
        rows.Add(("XLARGE", "ushort", "48", 60));
        rows.Add(("LEFT", "ushort", "0", 62));
        rows.Add(("TOP", "ushort", "1", 63));
        rows.Add(("RIGHT", "ushort", "2", 64));
        rows.Add(("BOTTOM", "ushort", "3", 65));
        rows.Add(("SOLID", "ushort", "0", 67));
        rows.Add(("XP", "ushort", "1", 68));
        rows.Add(("VISTAEMBOSSED", "ushort", "2", 69));
        rows.Add(("VISTAORIGINAL", "ushort", "3", 70));
        rows.Add(("VISTAGLASS", "ushort", "4", 71));
        rows.Add(("TRANSPARENT", "ushort", "5", 72));
        rows.Add(("BS_NONE", "ushort", "0", 74));
        rows.Add(("BS_SOLID", "ushort", "1", 75));
        rows.Add(("BS_RAISED", "ushort", "2", 76));
        rows.Add(("BS_ROUND", "ushort", "3", 77));
        rows.Add(("LS_SOLID", "ushort", "0", 79));
        rows.Add(("LS_DASH", "ushort", "1", 80));
        rows.Add(("LS_DOT", "ushort", "2", 81));
        rows.Add(("LS_DASHDOT", "ushort", "3", 82));
        rows.Add(("LS_DASHDOTDOT", "ushort", "4", 83));
        rows.Add(("TTS_NONE", "ushort", "0", 85));
        rows.Add(("TTS_NORMAL", "ushort", "1", 86));
        rows.Add(("TTS_BALLOON", "ushort", "2", 87));
        rows.Add(("BTS_NORMAL", "ushort", "0", 89));
        rows.Add(("BTS_DROPDOWN", "ushort", "1", 90));
        rows.Add(("BTS_SPLIT", "ushort", "2", 91));
        rows.Add(("SF_FORWARD", "long", "0", 93));
        rows.Add(("SF_BACKWARD", "long", "1", 94));
        rows.Add(("STATE_NONE", "uint", "0", 96));
        rows.Add(("STATE_DISABLED", "uint", "1", 97));
        rows.Add(("STATE_HOVER", "uint", "2", 98));
        rows.Add(("STATE_PRESSED", "uint", "4", 99));
        rows.Add(("STATE_FOCUS", "uint", "8", 100));
        rows.Add(("STATE_ACTIVE", "uint", "16", 101));
        rows.Add(("STATE_CURRENT", "uint", "32", 102));
        rows.Add(("STATE_CHECKED", "uint", "64", 103));
        rows.Add(("STATE_SELECTED", "uint", "128", 104));
        rows.Add(("STATE_EXPANDED", "uint", "256", 105));
        rows.Add(("STATE_COLLAPSED", "uint", "512", 106));
        rows.Add(("STATE_HIGHLIGHTED", "uint", "1024", 107));
        rows.Add(("STATE_DRAGGING", "uint", "2048", 108));
        rows.Add(("STATE_FLASHING", "uint", "4096", 109));
        rows.Add(("STATE_DEFAULT", "uint", "8192", 110));
        rows.Add(("STATE_READONLY", "uint", "16384", 111));

        // ---- I18N  [enums.sru:L115-L123] ----
        rows.Add(("I18N_SRC_PFW", "long", "0", 115));
        rows.Add(("I18N_SRC_CUSTOM", "long", "1", 116));
        rows.Add(("I18N_CAT_WINDOW", "long", "0", 118));
        rows.Add(("I18N_CAT_TABCONTROL", "long", "1", 119));
        rows.Add(("I18N_CAT_RIBBONBAR", "long", "2", 120));
        rows.Add(("I18N_CAT_SPLITCONTAINER", "long", "3", 121));
        rows.Add(("I18N_CAT_DATAWINDOW", "long", "4", 122));
        rows.Add(("I18N_CAT_CUSTOM", "long", "5", 123));

        // ---- Sciter  [enums.sru:L128-L486] ----
        rows.Add(("SC_OPT_SMOOTH_SCROLL", "long", "1", 128));
        rows.Add(("SC_OPT_CONNECTION_TIMEOUT", "long", "2", 129));
        rows.Add(("SC_OPT_HTTPS_ERROR", "long", "3", 130));
        rows.Add(("SC_OPT_FONT_SMOOTHING", "long", "4", 131));
        rows.Add(("SC_OPT_TRANSPARENT_WINDOW", "long", "6", 132));
        rows.Add(("SC_OPT_SCRIPT_RUNTIME_FEATURES", "long", "8", 135));
        rows.Add(("SC_OPT_GFX_LAYER", "long", "9", 136));
        rows.Add(("SC_OPT_DEBUG_MODE", "long", "10", 137));
        rows.Add(("SC_OPT_UX_THEMING", "long", "11", 138));
        rows.Add(("SC_OPT_ALPHA_WINDOW", "long", "12", 141));
        rows.Add(("SC_OPT_PX_AS_DIP", "long", "16", 142));
        rows.Add(("SC_OPTV_HTTPS_ERROR_DROP", "long", "0", 145));
        rows.Add(("SC_OPTV_HTTPS_ERROR_PROMPT", "long", "1", 146));
        rows.Add(("SC_OPTV_HTTPS_ERROR_ACCEPT", "long", "2", 147));
        rows.Add(("SC_OPTV_FONT_DEFAULT", "long", "0", 150));
        rows.Add(("SC_OPTV_FONT_NO_SMOOTHING", "long", "1", 151));
        rows.Add(("SC_OPTV_FONT_SMOOTHING", "long", "2", 152));
        rows.Add(("SC_OPTV_FONT_CLEAR_TYPE", "long", "3", 153));
        rows.Add(("SC_OPTV_RT_ALLOW_FILE_IO", "long", "1", 156));
        rows.Add(("SC_OPTV_RT_ALLOW_SOCKET_IO", "long", "2", 157));
        rows.Add(("SC_OPTV_RT_ALLOW_EVAL", "long", "4", 158));
        rows.Add(("SC_OPTV_RT_ALLOW_SYSINFO", "long", "8", 159));
        rows.Add(("SC_OPTV_GFX_LAYER_GDI", "long", "1", 162));
        rows.Add(("SC_OPTV_GFX_LAYER_WARP", "long", "2", 163));
        rows.Add(("SC_OPTV_GFX_LAYER_D2D", "long", "3", 164));
        rows.Add(("SC_OPTV_GFX_LAYER_SKIA", "long", "4", 165));
        rows.Add(("SC_OPTV_GFX_LAYER_SKIA_OPENGL", "long", "5", 166));
        rows.Add(("SC_OPTV_GFX_LAYER_AUTO", "long", "65535", 167));
        rows.Add(("SC_WS_TITLEBAR", "long", "2", 176));
        rows.Add(("SC_WS_RESIZEABLE", "long", "4", 177));
        rows.Add(("SC_WS_TOOL", "long", "8", 178));
        rows.Add(("SC_WS_CONTROLS", "long", "16", 179));
        rows.Add(("SC_WS_GLASSY", "long", "32", 180));
        rows.Add(("SC_WS_ALPHA", "long", "64", 181));
        rows.Add(("SC_WS_MAIN", "long", "128", 182));
        rows.Add(("SC_WS_POPUP", "long", "256", 183));
        rows.Add(("SC_WS_ENABLE_DEBUG", "long", "512", 184));
        rows.Add(("SC_WS_VISIBLE", "long", "65536", 186));
        rows.Add(("SC_HANDLE_MOUSE", "long", "1", 189));
        rows.Add(("SC_HANDLE_KEY", "long", "2", 190));
        rows.Add(("SC_HANDLE_FOCUS", "long", "4", 191));
        rows.Add(("SC_HANDLE_SCROLL", "long", "8", 192));
        rows.Add(("SC_HANDLE_TIMER", "long", "16", 193));
        rows.Add(("SC_HANDLE_SIZE", "long", "32", 194));
        rows.Add(("SC_HANDLE_EVENT", "long", "256", 195));
        rows.Add(("SC_HANDLE_INVOKE_METHOD", "long", "1536", 196));
        rows.Add(("SC_HANDLE_ALL", "long", "65536", 197));
        rows.Add(("SC_PHASE_BUBBLING", "long", "0", 201));
        rows.Add(("SC_PHASE_SINKING", "long", "32768", 202));
        rows.Add(("SC_PHASE_HANDLED", "long", "65536", 203));
        rows.Add(("SC_MOUSE_LEFT", "long", "1", 206));
        rows.Add(("SC_MOUSE_RIGHT", "long", "2", 207));
        rows.Add(("SC_MOUSE_MIDDLE", "long", "4", 208));
        rows.Add(("SC_MOUSE_ENTER", "long", "0", 211));
        rows.Add(("SC_MOUSE_LEAVE", "long", "1", 212));
        rows.Add(("SC_MOUSE_MOVE", "long", "2", 213));
        rows.Add(("SC_MOUSE_UP", "long", "3", 214));
        rows.Add(("SC_MOUSE_DOWN", "long", "4", 215));
        rows.Add(("SC_MOUSE_DCLICK", "long", "5", 216));
        rows.Add(("SC_MOUSE_WHEEL", "long", "6", 217));
        rows.Add(("SC_MOUSE_TICK", "long", "7", 218));
        rows.Add(("SC_MOUSE_IDLE", "long", "8", 219));
        rows.Add(("SC_KEY_CONTROL", "long", "1", 222));
        rows.Add(("SC_KEY_SHIFT", "long", "2", 223));
        rows.Add(("SC_KEY_ALT", "long", "4", 224));
        rows.Add(("SC_KEY_DOWN", "long", "0", 227));
        rows.Add(("SC_KEY_UP", "long", "1", 228));
        rows.Add(("SC_KEY_CHAR", "long", "2", 229));
        rows.Add(("SC_FOCUS_LOST", "long", "0", 232));
        rows.Add(("SC_FOCUS_GOT", "long", "1", 233));
        rows.Add(("SC_FOCUS_IN", "long", "2", 234));
        rows.Add(("SC_FOCUS_OUT", "long", "3", 235));
        rows.Add(("SC_EVT_BUTTON_CLICK", "long", "0", 238));
        rows.Add(("SC_EVT_BUTTON_PRESS", "long", "1", 239));
        rows.Add(("SC_EVT_BUTTON_STATE_CHANGED", "long", "2", 240));
        rows.Add(("SC_EVT_EDIT_VALUE_CHANGING", "long", "3", 241));
        rows.Add(("SC_EVT_EDIT_VALUE_CHANGED", "long", "4", 242));
        rows.Add(("SC_EVT_SELECT_SELECTION_CHANGED", "long", "5", 243));
        rows.Add(("SC_EVT_SELECT_STATE_CHANGED", "long", "6", 244));
        rows.Add(("SC_EVT_POPUP_REQUEST", "long", "7", 245));
        rows.Add(("SC_EVT_POPUP_READY", "long", "8", 247));
        rows.Add(("SC_EVT_POPUP_DISMISSED", "long", "9", 249));
        rows.Add(("SC_EVT_MENU_ITEM_ACTIVE", "long", "10", 252));
        rows.Add(("SC_EVT_MENU_ITEM_CLICK", "long", "11", 253));
        rows.Add(("SC_EVT_CONTEXT_MENU_REQUEST", "long", "16", 259));
        rows.Add(("SC_EVT_VISIUAL_STATUS_CHANGED", "long", "17", 261));
        rows.Add(("SC_EVT_DISABLED_STATUS_CHANGED", "long", "18", 262));
        rows.Add(("SC_EVT_POPUP_DISMISSING", "long", "19", 263));
        rows.Add(("SC_EVT_CONTENT_CHANGED", "long", "21", 264));
        rows.Add(("SC_EVT_CLICK", "long", "22", 266));
        rows.Add(("SC_EVT_CHANGE", "long", "23", 267));
        rows.Add(("SC_EVT_HYPERLINK_CLICK", "long", "128", 268));
        rows.Add(("SC_EVT_ELEMENT_COLLAPSED", "long", "144", 269));
        rows.Add(("SC_EVT_ELEMENT_EXPANDED", "long", "145", 270));
        rows.Add(("SC_EVT_ACTIVATE_CHILD", "long", "146", 271));
        rows.Add(("SC_EVT_INIT_DATA_VIEW", "long", "147", 273));
        rows.Add(("SC_EVT_ROWS_DATA_REQUEST", "long", "148", 274));
        rows.Add(("SC_EVT_UI_STATE_CHANGED", "long", "149", 276));
        rows.Add(("SC_EVT_FORM_SUBMIT", "long", "150", 278));
        rows.Add(("SC_EVT_FORM_RESET", "long", "151", 281));
        rows.Add(("SC_EVT_DOCUMENT_COMPLETE", "long", "152", 284));
        rows.Add(("SC_EVT_HISTORY_PUSH", "long", "153", 285));
        rows.Add(("SC_EVT_HISTORY_DROP", "long", "154", 286));
        rows.Add(("SC_EVT_HISTORY_PRIOR", "long", "155", 287));
        rows.Add(("SC_EVT_HISTORY_NEXT", "long", "156", 288));
        rows.Add(("SC_EVT_HISTORY_STATE_CHANGED", "long", "157", 289));
        rows.Add(("SC_EVT_CLOSE_POPUP", "long", "158", 290));
        rows.Add(("SC_EVT_REQUEST_TOOLTIP", "long", "159", 291));
        rows.Add(("SC_EVT_ANIMATION", "long", "160", 292));
        rows.Add(("SC_EVT_DOCUMENT_CREATED", "long", "192", 293));
        rows.Add(("SC_EVT_DOCUMENT_CLOSE_REQUEST", "long", "193", 294));
        rows.Add(("SC_EVT_DOCUMENT_CLOSE", "long", "194", 295));
        rows.Add(("SC_EVT_DOCUMENT_READY", "long", "195", 296));
        rows.Add(("SC_EVT_DOCUMENT_PARSED", "long", "196", 297));
        rows.Add(("SC_EVT_VIDEO_INITIALIZED", "long", "209", 298));
        rows.Add(("SC_EVT_VIDEO_STARTED", "long", "210", 299));
        rows.Add(("SC_EVT_VIDEO_STOPPED", "long", "211", 300));
        rows.Add(("SC_EVT_VIDEO_BIND_RQ", "long", "212", 301));
        rows.Add(("SC_EVT_PAGINATION_STARTS", "long", "224", 310));
        rows.Add(("SC_EVT_PAGINATION_PAGE", "long", "225", 311));
        rows.Add(("SC_EVT_PAGINATION_ENDS", "long", "226", 312));
        rows.Add(("SC_EVT_CUSTOM_NAME", "long", "240", 313));
        rows.Add(("SC_EVT_FIRST_APPLICATION_EVENT_CODE", "long", "256", 314));
        rows.Add(("SC_EVT_CUSTOM", "long", "4096", 320));
        rows.Add(("SC_EVR_CONTENT_ADDED", "long", "1", 323));
        rows.Add(("SC_EVR_CONTENT_REMOVED", "long", "2", 324));
        rows.Add(("SC_EVR_BY_MOUSE_CLICK", "long", "0", 327));
        rows.Add(("SC_EVR_BY_KEY_CLICK", "long", "1", 328));
        rows.Add(("SC_EVR_SYNTHESIZED", "long", "2", 329));
        rows.Add(("SC_EVR_BY_MOUSE_ON_ICON", "long", "3", 330));
        rows.Add(("SC_EVR_BY_INS_CHAR", "long", "0", 333));
        rows.Add(("SC_EVR_BY_INS_CHARS", "long", "1", 334));
        rows.Add(("SC_EVR_BY_DEL_CHAR", "long", "2", 335));
        rows.Add(("SC_EVR_BY_DEL_CHARS", "long", "3", 336));
        rows.Add(("SC_EVR_BY_UNDO_REDO", "long", "4", 337));
        rows.Add(("SC_TYPE_UNDEFINED", "long", "0", 340));
        rows.Add(("SC_TYPE_NULL", "long", "1", 341));
        rows.Add(("SC_TYPE_BOOLEAN", "long", "2", 342));
        rows.Add(("SC_TYPE_NUMBER", "long", "3", 343));
        rows.Add(("SC_TYPE_STRING", "long", "4", 344));
        rows.Add(("SC_TYPE_DATETIME", "long", "5", 345));
        rows.Add(("SC_TYPE_CLASS", "long", "6", 346));
        rows.Add(("SC_TYPE_OBJECT", "long", "7", 347));
        rows.Add(("SC_TYPE_ARRAY", "long", "8", 348));
        rows.Add(("SC_TYPE_BYTES", "long", "9", 349));
        rows.Add(("SC_TYPE_BLOB", "long", "10", 350));
        rows.Add(("SC_TYPE_ELEMENT", "long", "11", 351));
        rows.Add(("SC_TYPE_FUNCTION", "long", "12", 352));
        rows.Add(("SC_TYPE_FUNCTOR", "long", "13", 353));
        rows.Add(("SC_TYPE_UNNAMED", "long", "14", 354));
        rows.Add(("SC_TYPE_UNIT_EM", "long", "1", 357));
        rows.Add(("SC_TYPE_UNIT_EX", "long", "2", 358));
        rows.Add(("SC_TYPE_UNIT_PR", "long", "3", 359));
        rows.Add(("SC_TYPE_UNIT_SP", "long", "4", 360));
        rows.Add(("SC_TYPE_UNIT_PX", "long", "7", 361));
        rows.Add(("SC_TYPE_UNIT_IN", "long", "8", 362));
        rows.Add(("SC_TYPE_UNIT_CM", "long", "9", 363));
        rows.Add(("SC_TYPE_UNIT_MM", "long", "10", 364));
        rows.Add(("SC_TYPE_UNIT_PT", "long", "11", 365));
        rows.Add(("SC_TYPE_UNIT_PC", "long", "12", 366));
        rows.Add(("SC_TYPE_UNIT_DIP", "long", "13", 367));
        rows.Add(("SC_TYPE_UNIT_COLOR", "long", "15", 368));
        rows.Add(("SC_TYPE_UNIT_URL", "long", "16", 369));
        rows.Add(("SC_SIH_REPLACE_CONTENT", "long", "0", 372));
        rows.Add(("SC_SIH_INSERT_AT_START", "long", "1", 373));
        rows.Add(("SC_SIH_APPEND_AFTER_LAST", "long", "2", 374));
        rows.Add(("SC_SOH_REPLACE", "long", "3", 375));
        rows.Add(("SC_SOH_INSERT_BEFORE", "long", "4", 376));
        rows.Add(("SC_SOH_INSERT_AFTER", "long", "5", 377));
        rows.Add(("SC_STATE_LINK", "uint", "1", 380));
        rows.Add(("SC_STATE_HOVER", "uint", "2", 381));
        rows.Add(("SC_STATE_ACTIVE", "uint", "4", 382));
        rows.Add(("SC_STATE_FOCUS", "uint", "8", 383));
        rows.Add(("SC_STATE_VISITED", "uint", "16", 384));
        rows.Add(("SC_STATE_CURRENT", "uint", "32", 385));
        rows.Add(("SC_STATE_CHECKED", "uint", "64", 386));
        rows.Add(("SC_STATE_DISABLED", "uint", "128", 387));
        rows.Add(("SC_STATE_READONLY", "uint", "256", 388));
        rows.Add(("SC_STATE_EXPANDED", "uint", "512", 389));
        rows.Add(("SC_STATE_COLLAPSED", "uint", "1024", 390));
        rows.Add(("SC_STATE_INCOMPLETE", "uint", "2048", 391));
        rows.Add(("SC_STATE_ANIMATING", "uint", "4096", 392));
        rows.Add(("SC_STATE_FOCUSABLE", "uint", "8192", 393));
        rows.Add(("SC_STATE_ANCHOR", "uint", "16384", 394));
        rows.Add(("SC_STATE_SYNTHETIC", "uint", "32768", 395));
        rows.Add(("SC_STATE_OWNS_POPUP", "uint", "65536", 396));
        rows.Add(("SC_STATE_TABFOCUS", "uint", "131072", 397));
        rows.Add(("SC_STATE_EMPTY", "uint", "262144", 398));
        rows.Add(("SC_STATE_BUSY", "uint", "524288", 400));
        rows.Add(("SC_STATE_DRAG_OVER", "uint", "1048576", 401));
        rows.Add(("SC_STATE_DROP_TARGET", "uint", "2097152", 402));
        rows.Add(("SC_STATE_MOVING", "uint", "4194304", 403));
        rows.Add(("SC_STATE_COPYING", "uint", "8388608", 404));
        rows.Add(("SC_STATE_DRAG_SOURCE", "uint", "16777216", 405));
        rows.Add(("SC_STATE_DROP_MARKER", "uint", "33554432", 406));
        rows.Add(("SC_STATE_PRESSED", "uint", "67108864", 407));
        rows.Add(("SC_STATE_POPUP", "uint", "134217728", 409));
        rows.Add(("SC_STATE_IS_LTR", "uint", "268435456", 410));
        rows.Add(("SC_STATE_IS_RTL", "uint", "536870912", 411));
        rows.Add(("SC_AREA_ROOT_RELATIVE", "uint", "1", 415));
        rows.Add(("SC_AREA_SELF_RELATIVE", "uint", "2", 417));
        rows.Add(("SC_AREA_CONTAINER_RELATIVE", "uint", "3", 419));
        rows.Add(("SC_AREA_VIEW_RELATIVE", "uint", "4", 420));
        rows.Add(("SC_AREA_CONTENT_BOX", "uint", "0", 421));
        rows.Add(("SC_AREA_PADDING_BOX", "uint", "16", 422));
        rows.Add(("SC_AREA_BORDER_BOX", "uint", "32", 423));
        rows.Add(("SC_AREA_MARGIN_BOX", "uint", "48", 424));
        rows.Add(("SC_AREA_BACK_IMAGE_AREA", "uint", "64", 425));
        rows.Add(("SC_AREA_FORE_IMAGE_AREA", "uint", "80", 426));
        rows.Add(("SC_AREA_SCROLLABLE_AREA", "uint", "96", 427));
        rows.Add(("SC_CTL_NO", "uint", "0", 434));
        rows.Add(("SC_CTL_UNKNOWN", "uint", "1", 435));
        rows.Add(("SC_CTL_EDIT", "uint", "2", 436));
        rows.Add(("SC_CTL_NUMERIC", "uint", "3", 437));
        rows.Add(("SC_CTL_CLICKABLE", "uint", "4", 438));
        rows.Add(("SC_CTL_BUTTON", "uint", "5", 439));
        rows.Add(("SC_CTL_CHECKBOX", "uint", "6", 440));
        rows.Add(("SC_CTL_RADIO", "uint", "7", 441));
        rows.Add(("SC_CTL_SELECT_SINGLE", "uint", "8", 442));
        rows.Add(("SC_CTL_SELECT_MULTIPLE", "uint", "9", 443));
        rows.Add(("SC_CTL_DD_SELECT", "uint", "10", 444));
        rows.Add(("SC_CTL_TEXTAREA", "uint", "11", 445));
        rows.Add(("SC_CTL_HTMLAREA", "uint", "12", 446));
        rows.Add(("SC_CTL_PASSWORD", "uint", "13", 447));
        rows.Add(("SC_CTL_PROGRESS", "uint", "14", 448));
        rows.Add(("SC_CTL_SLIDER", "uint", "15", 449));
        rows.Add(("SC_CTL_DECIMAL", "uint", "16", 450));
        rows.Add(("SC_CTL_CURRENCY", "uint", "17", 451));
        rows.Add(("SC_CTL_SCROLLBAR", "uint", "18", 452));
        rows.Add(("SC_CTL_HYPERLINK", "uint", "19", 453));
        rows.Add(("SC_CTL_MENUBAR", "uint", "20", 454));
        rows.Add(("SC_CTL_MENU", "uint", "21", 455));
        rows.Add(("SC_CTL_MENUBUTTON", "uint", "22", 456));
        rows.Add(("SC_CTL_CALENDAR", "uint", "23", 457));
        rows.Add(("SC_CTL_DATE", "uint", "24", 458));
        rows.Add(("SC_CTL_TIME", "uint", "25", 459));
        rows.Add(("SC_CTL_FRAME", "uint", "26", 460));
        rows.Add(("SC_CTL_FRAMESET", "uint", "27", 461));
        rows.Add(("SC_CTL_GRAPHICS", "uint", "28", 462));
        rows.Add(("SC_CTL_SPRITE", "uint", "29", 463));
        rows.Add(("SC_CTL_LIST", "uint", "30", 464));
        rows.Add(("SC_CTL_RICHTEXT", "uint", "31", 465));
        rows.Add(("SC_CTL_TOOLTIP", "uint", "32", 466));
        rows.Add(("SC_CTL_HIDDEN", "uint", "33", 467));
        rows.Add(("SC_CTL_URL", "uint", "34", 468));
        rows.Add(("SC_CTL_TOOLBAR", "uint", "35", 469));
        rows.Add(("SC_CTL_FORM", "uint", "36", 470));
        rows.Add(("SC_CTL_FILE", "uint", "37", 471));
        rows.Add(("SC_CTL_PATH", "uint", "38", 472));
        rows.Add(("SC_CTL_WINDOW", "uint", "39", 473));
        rows.Add(("SC_CTL_LABEL", "uint", "40", 474));
        rows.Add(("SC_CTL_IMAGE", "uint", "41", 475));
        rows.Add(("SC_OT_DOM", "uint", "0", 478));
        rows.Add(("SC_OT_CSSS", "uint", "1", 479));
        rows.Add(("SC_OT_CSS", "uint", "2", 480));
        rows.Add(("SC_OT_TIS", "uint", "3", 481));
        rows.Add(("SC_OS_INFO", "uint", "0", 484));
        rows.Add(("SC_OS_WARNING", "uint", "1", 485));
        rows.Add(("SC_OS_ERROR", "uint", "2", 486));

        // ---- Blink  [enums.sru:L493-L545] ----
        rows.Add(("BLINK_OPT_COOKIE_FILE", "long", "1", 493));
        rows.Add(("BLINK_OPT_STORAGE_DIR", "long", "2", 494));
        rows.Add(("BLINK_OPT_PLUGIN_DIR", "long", "3", 495));
        rows.Add(("BLINK_OPT_COOKIE_ENABLED", "long", "4", 496));
        rows.Add(("BLINK_OPT_PLUGIN_ENABLED", "long", "5", 497));
        rows.Add(("BLINK_OPT_CSP_CHECK_ENABLED", "long", "6", 498));
        rows.Add(("BLINK_OPT_HEADLESS_ENABLED", "long", "7", 499));
        rows.Add(("BLINK_OPT_ALPHA_WINDOW", "long", "8", 500));
        rows.Add(("BLINK_OPT_GET_FAVICON", "long", "9", 501));
        rows.Add(("BLINK_WS_TITLEBAR", "long", "1", 507));
        rows.Add(("BLINK_WS_RESIZEABLE", "long", "2", 508));
        rows.Add(("BLINK_WS_TOOL", "long", "4", 509));
        rows.Add(("BLINK_WS_CONTROLS", "long", "8", 510));
        rows.Add(("BLINK_WS_ALPHA", "long", "16", 511));
        rows.Add(("BLINK_WS_MAIN", "long", "32", 512));
        rows.Add(("BLINK_WS_POPUP", "long", "64", 513));
        rows.Add(("BLINK_WS_VISIBLE", "long", "65536", 514));
        rows.Add(("BLINK_OS_LOG", "uint", "1", 517));
        rows.Add(("BLINK_OS_WARNING", "uint", "2", 518));
        rows.Add(("BLINK_OS_ERROR", "uint", "3", 519));
        rows.Add(("BLINK_OS_DEBUG", "uint", "4", 520));
        rows.Add(("BLINK_OS_INFO", "uint", "5", 521));
        rows.Add(("BLINK_OS_REVOKED", "uint", "6", 522));
        rows.Add(("BLINK_TYPE_NUMBER", "long", "0", 525));
        rows.Add(("BLINK_TYPE_STRING", "long", "1", 526));
        rows.Add(("BLINK_TYPE_BOOLEAN", "long", "2", 527));
        rows.Add(("BLINK_TYPE_OBJECT", "long", "3", 528));
        rows.Add(("BLINK_TYPE_FUNCTION", "long", "4", 529));
        rows.Add(("BLINK_TYPE_UNDEFINED", "long", "5", 530));
        rows.Add(("BLINK_TYPE_ARRAY", "long", "6", 531));
        rows.Add(("BLINK_TYPE_NULL", "long", "7", 532));
        rows.Add(("BLINK_LOADING_SUCCEEDED", "long", "0", 535));
        rows.Add(("BLINK_LOADING_FAILED", "long", "1", 536));
        rows.Add(("BLINK_LOADING_CANCELED", "long", "2", 537));
        rows.Add(("BLINK_NAV_TYPE_LINKCLICK", "long", "0", 540));
        rows.Add(("BLINK_NAV_TYPE_FORMSUBMITTE", "long", "1", 541));
        rows.Add(("BLINK_NAV_TYPE_BACKFORWARD", "long", "2", 542));
        rows.Add(("BLINK_NAV_TYPE_RELOAD", "long", "3", 543));
        rows.Add(("BLINK_NAV_TYPE_FORMRESUBMITT", "long", "4", 544));
        rows.Add(("BLINK_NAV_TYPE_OTHER", "long", "5", 545));

        // ---- WebView  [enums.sru:L552-L574] ----
        rows.Add(("WEBVIEW_RUNTIME_EVERGREEN", "long", "0", 552));
        rows.Add(("WEBVIEW_RUNTIME_FIXED", "long", "1", 553));
        rows.Add(("WEBVIEW_RUNTIME_AUTO", "long", "2", 554));
        rows.Add(("WEBVIEW_OPT_STATUSBAR", "long", "0", 557));
        rows.Add(("WEBVIEW_OPT_CONTEXT_MENU", "long", "1", 558));
        rows.Add(("WEBVIEW_OPT_CUSTOM_DIALOG", "long", "2", 559));
        rows.Add(("WEBVIEW_OPT_BUILTIN_ERROR_PAGE", "long", "3", 560));
        rows.Add(("WEBVIEW_OPT_DEVTOOLS", "long", "4", 561));
        rows.Add(("WEBVIEW_OPT_GET_FAVICON", "long", "5", 562));
        rows.Add(("WEBVIEW_WS_TITLEBAR", "long", "1", 568));
        rows.Add(("WEBVIEW_WS_RESIZEABLE", "long", "2", 569));
        rows.Add(("WEBVIEW_WS_TOOL", "long", "4", 570));
        rows.Add(("WEBVIEW_WS_CONTROLS", "long", "8", 571));
        rows.Add(("WEBVIEW_WS_ALPHA", "long", "16", 572));
        rows.Add(("WEBVIEW_WS_MAIN", "long", "32", 573));
        rows.Add(("WEBVIEW_WS_VISIBLE", "long", "65536", 574));

        // ---- Thread  [enums.sru:L581-L584] ----
        rows.Add(("TNR_START", "long", "1", 581));
        rows.Add(("TNR_STOP", "long", "2", 582));
        rows.Add(("TNR_ERROR", "long", "3", 583));
        rows.Add(("TNR_NOTIFY", "long", "4", 584));

        // ---- XML Parser  [enums.sru:L591-L693] ----
        rows.Add(("XML_NODE_NULL", "long", "0", 591));
        rows.Add(("XML_NODE_DOCUMENT", "long", "1", 592));
        rows.Add(("XML_NODE_ELEMENT", "long", "2", 593));
        rows.Add(("XML_NODE_PCDATA", "long", "3", 594));
        rows.Add(("XML_NODE_CDATA", "long", "4", 595));
        rows.Add(("XML_NODE_COMMENT", "long", "5", 596));
        rows.Add(("XML_NODE_PI", "long", "6", 597));
        rows.Add(("XML_NODE_DECLARATION", "long", "7", 598));
        rows.Add(("XML_NODE_DOCTYPE", "long", "8", 599));
        rows.Add(("XML_XPATH_TYPE_NONE", "long", "0", 602));
        rows.Add(("XML_XPATH_TYPE_NODE_SET", "long", "1", 603));
        rows.Add(("XML_XPATH_TYPE_NUMBER", "long", "2", 604));
        rows.Add(("XML_XPATH_TYPE_STRING", "long", "3", 605));
        rows.Add(("XML_XPATH_TYPE_BOOLEAN", "long", "4", 606));
        rows.Add(("XML_SORT_TYPE_UNSORTED", "long", "0", 609));
        rows.Add(("XML_SORT_TYPE_SORTED", "long", "1", 610));
        rows.Add(("XML_SORT_TYPE_SORTED_REVERSE", "long", "2", 611));
        rows.Add(("XML_PARSE_MINIMAL", "uint", "0", 617));
        rows.Add(("XML_PARSE_PI", "uint", "1", 619));
        rows.Add(("XML_PARSE_COMMENTS", "uint", "2", 621));
        rows.Add(("XML_PARSE_CDATA", "uint", "4", 623));
        rows.Add(("XML_PARSE_WS_PCDATA", "uint", "8", 626));
        rows.Add(("XML_PARSE_ESCAPES", "uint", "16", 628));
        rows.Add(("XML_PARSE_EOL", "uint", "32", 630));
        rows.Add(("XML_PARSE_WCONV_ATTRIBUTE", "uint", "64", 632));
        rows.Add(("XML_PARSE_WNORM_ATTRIBUTE", "uint", "128", 634));
        rows.Add(("XML_PARSE_DECLARATION", "uint", "256", 636));
        rows.Add(("XML_PARSE_DOCTYPE", "uint", "512", 638));
        rows.Add(("XML_PARSE_WS_PCDATA_SINGLE", "uint", "1024", 642));
        rows.Add(("XML_PARSE_TRIM_PCDATA", "uint", "2048", 644));
        rows.Add(("XML_PARSE_FRAGMENT", "uint", "4096", 647));
        rows.Add(("XML_PARSE_EMBED_PCDATA", "uint", "8192", 651));
        rows.Add(("XML_PARSE_DEFAULT", "uint", "116", 655));
        rows.Add(("XML_PARSE_FULL", "uint", "887", 659));
        rows.Add(("XML_FORMAT_INDENT", "uint", "1", 664));
        rows.Add(("XML_FORMAT_WRITE_BOM", "uint", "2", 666));
        rows.Add(("XML_FORMAT_RAW", "uint", "4", 668));
        rows.Add(("XML_FORMAT_NO_DECLARATION", "uint", "8", 670));
        rows.Add(("XML_FORMAT_NO_ESCAPES", "uint", "16", 672));
        rows.Add(("XML_FORMAT_SAVE_FILE_TEXT", "uint", "32", 674));
        rows.Add(("XML_FORMAT_INDENT_ATTRIBUTES", "uint", "64", 676));
        rows.Add(("XML_FORMAT_NO_EMPTY_ELEMENT_TAGS", "uint", "128", 678));
        rows.Add(("XML_FORMAT_DEFAULT", "uint", "1", 681));
        rows.Add(("XML_ENCODING_AUTO", "uint", "0", 684));
        rows.Add(("XML_ENCODING_UTF8", "uint", "1", 685));
        rows.Add(("XML_ENCODING_UTF16_LE", "uint", "2", 686));
        rows.Add(("XML_ENCODING_UTF16_BE", "uint", "3", 687));
        rows.Add(("XML_ENCODING_UTF16", "uint", "4", 688));
        rows.Add(("XML_ENCODING_UTF32_LE", "uint", "5", 689));
        rows.Add(("XML_ENCODING_UTF32_BE", "uint", "6", 690));
        rows.Add(("XML_ENCODING_UTF32", "uint", "7", 691));
        rows.Add(("XML_ENCODING_WCHAR", "uint", "8", 692));
        rows.Add(("XML_ENCODING_LATIN1", "uint", "9", 693));

        // ---- JSON Parser  [enums.sru:L700-L711] ----
        rows.Add(("JSON_TYPE_NONE", "long", "0", 700));
        rows.Add(("JSON_TYPE_BOOLEAN", "long", "1", 701));
        rows.Add(("JSON_TYPE_NUMBER", "long", "2", 702));
        rows.Add(("JSON_TYPE_STRING", "long", "3", 703));
        rows.Add(("JSON_TYPE_OBJECT", "long", "4", 704));
        rows.Add(("JSON_TYPE_ARRAY", "long", "5", 705));
        rows.Add(("JSON_TYPE_NULL", "long", "6", 706));
        rows.Add(("JSON_FORMAT_NONE", "long", "0", 709));
        rows.Add(("JSON_FORMAT_INDENT", "long", "1", 710));
        rows.Add(("JSON_FORMAT_DEFAULT", "long", "0", 711));

        // ---- SQL Parser  [enums.sru:L718-L720] ----
        rows.Add(("SQL_MS_REPLACE", "long", "1", 718));
        rows.Add(("SQL_MS_APPEND", "long", "2", 719));
        rows.Add(("SQL_MS_PREPEND", "long", "3", 720));

        // ---- Http  [enums.sru:L727-L820] ----
        rows.Add(("HTTP_METHOD_GET", "string", "GET", 727));
        rows.Add(("HTTP_METHOD_POST", "string", "POST", 728));
        rows.Add(("HTTP_METHOD_PUT", "string", "PUT", 729));
        rows.Add(("HTTP_METHOD_DELETE", "string", "DELETE", 730));
        rows.Add(("HTTP_METHOD_HEAD", "string", "HEAD", 731));
        rows.Add(("HTTP_PROXY_NONE", "long", "0", 734));
        rows.Add(("HTTP_PROXY_DEFAULT", "long", "1", 735));
        rows.Add(("HTTP_PROXY_AUTO", "long", "2", 736));
        rows.Add(("HTTP_PROXY_PROVIDED", "long", "3", 737));
        rows.Add(("HTTP_CERT_FILE", "long", "1", 740));
        rows.Add(("HTTP_CERT_FILE_DER", "long", "2", 741));
        rows.Add(("HTTP_CERT_FILE_PKCS12", "long", "3", 742));
        rows.Add(("HTTP_CERT_PEM", "long", "4", 743));
        rows.Add(("HTTP_CERT_KEY_FILE", "long", "1", 746));
        rows.Add(("HTTP_CERT_KEY_FILE_RSA", "long", "2", 747));
        rows.Add(("HTTP_CERT_KEY_PEM", "long", "3", 748));
        rows.Add(("HTTP_CERT_KEY_PEM_RSA", "long", "4", 749));
        rows.Add(("HTTP_ENCODING_UNKNOWN", "long", "0", 753));
        rows.Add(("HTTP_ENCODING_UTF8", "long", "1", 754));
        rows.Add(("HTTP_ENCODING_UTF16", "long", "2", 755));
        rows.Add(("HTTP_ENCODING_UTF16LE", "long", "2", 756));
        rows.Add(("HTTP_ENCODING_UTF16BE", "long", "3", 757));
        rows.Add(("HTTP_ENCODING_ANSI", "long", "4", 758));
        rows.Add(("HTTP_ENCODING_GB2312", "long", "5", 759));
        rows.Add(("HTTP_ENCODING_GBK", "long", "5", 760));
        rows.Add(("HTTP_ENCODING_GB18030", "long", "6", 761));
        rows.Add(("HTTP_ENCODING_BIG5", "long", "7", 762));
        rows.Add(("HTTP_ENCODING_ISO88591", "long", "8", 763));
        rows.Add(("HTTP_ENCODING_LATIN1", "long", "8", 764));
        rows.Add(("HTTP_ENCODING_ISO88592", "long", "9", 765));
        rows.Add(("HTTP_ENCODING_LATIN2", "long", "9", 766));
        rows.Add(("HTTP_ENCODING_ISO88593", "long", "10", 767));
        rows.Add(("HTTP_ENCODING_LATIN3", "long", "10", 768));
        rows.Add(("HTTP_ENCODING_ISO2022JP", "long", "11", 769));
        rows.Add(("HTTP_ENCODING_ISO2022KR", "long", "12", 770));
        rows.Add(("HTTP_STATUS_CONTINUE", "long", "100", 773));
        rows.Add(("HTTP_STATUS_SWITCH_PROTOCOLS", "long", "101", 774));
        rows.Add(("HTTP_STATUS_OK", "long", "200", 775));
        rows.Add(("HTTP_STATUS_CREATED", "long", "201", 776));
        rows.Add(("HTTP_STATUS_ACCEPTED", "long", "202", 777));
        rows.Add(("HTTP_STATUS_PARTIAL", "long", "203", 778));
        rows.Add(("HTTP_STATUS_NO_CONTENT", "long", "204", 779));
        rows.Add(("HTTP_STATUS_RESET_CONTENT", "long", "205", 780));
        rows.Add(("HTTP_STATUS_PARTIAL_CONTENT", "long", "206", 781));
        rows.Add(("HTTP_STATUS_WEBDAV_MULTI_STATUS", "long", "207", 782));
        rows.Add(("HTTP_STATUS_AMBIGUOUS", "long", "300", 783));
        rows.Add(("HTTP_STATUS_MOVED", "long", "301", 784));
        rows.Add(("HTTP_STATUS_REDIRECT", "long", "302", 785));
        rows.Add(("HTTP_STATUS_REDIRECT_METHOD", "long", "303", 786));
        rows.Add(("HTTP_STATUS_NOT_MODIFIED", "long", "304", 787));
        rows.Add(("HTTP_STATUS_USE_PROXY", "long", "305", 788));
        rows.Add(("HTTP_STATUS_REDIRECT_KEEP_VERB", "long", "307", 789));
        rows.Add(("HTTP_STATUS_BAD_REQUEST", "long", "400", 790));
        rows.Add(("HTTP_STATUS_DENIED", "long", "401", 791));
        rows.Add(("HTTP_STATUS_PAYMENT_REQ", "long", "402", 792));
        rows.Add(("HTTP_STATUS_FORBIDDEN", "long", "403", 793));
        rows.Add(("HTTP_STATUS_NOT_FOUND", "long", "404", 794));
        rows.Add(("HTTP_STATUS_BAD_METHOD", "long", "405", 795));
        rows.Add(("HTTP_STATUS_NONE_ACCEPTABLE", "long", "406", 796));
        rows.Add(("HTTP_STATUS_PROXY_AUTH_REQ", "long", "407", 797));
        rows.Add(("HTTP_STATUS_REQUEST_TIMEOUT", "long", "408", 798));
        rows.Add(("HTTP_STATUS_CONFLICT", "long", "409", 799));
        rows.Add(("HTTP_STATUS_GONE", "long", "410", 800));
        rows.Add(("HTTP_STATUS_LENGTH_REQUIRED", "long", "411", 801));
        rows.Add(("HTTP_STATUS_PRECOND_FAILED", "long", "412", 802));
        rows.Add(("HTTP_STATUS_REQUEST_TOO_LARGE", "long", "413", 803));
        rows.Add(("HTTP_STATUS_URI_TOO_LONG", "long", "414", 804));
        rows.Add(("HTTP_STATUS_UNSUPPORTED_MEDIA", "long", "415", 805));
        rows.Add(("HTTP_STATUS_RETRY_WITH", "long", "449", 806));
        rows.Add(("HTTP_STATUS_SERVER_ERROR", "long", "500", 807));
        rows.Add(("HTTP_STATUS_NOT_SUPPORTED", "long", "501", 808));
        rows.Add(("HTTP_STATUS_BAD_GATEWAY", "long", "502", 809));
        rows.Add(("HTTP_STATUS_SERVICE_UNAVAIL", "long", "503", 810));
        rows.Add(("HTTP_STATUS_GATEWAY_TIMEOUT", "long", "504", 811));
        rows.Add(("HTTP_STATUS_VERSION_NOT_SUP", "long", "505", 812));
        rows.Add(("HTTP_TRANS_WRITE", "long", "0", 815));
        rows.Add(("HTTP_TRANS_READ", "long", "1", 816));
        rows.Add(("HTTP_UPLOAD_CHUNK", "long", "0", 819));
        rows.Add(("HTTP_UPLOAD_MULTIPART", "long", "1", 820));

        // ---- WebSocket  [enums.sru:L827-L900] ----
        rows.Add(("WS_STATE_CONNECTING", "long", "0", 827));
        rows.Add(("WS_STATE_OPEN", "long", "1", 828));
        rows.Add(("WS_STATE_CLOSING", "long", "2", 829));
        rows.Add(("WS_STATE_CLOSED", "long", "3", 830));
        rows.Add(("WS_CERT_FILE", "long", "1", 833));
        rows.Add(("WS_CERT_FILE_DER", "long", "2", 834));
        rows.Add(("WS_CERT_FILE_PKCS12", "long", "3", 835));
        rows.Add(("WS_CERT_PEM", "long", "4", 836));
        rows.Add(("WS_CERT_KEY_FILE", "long", "1", 839));
        rows.Add(("WS_CERT_KEY_FILE_RSA", "long", "2", 840));
        rows.Add(("WS_CERT_KEY_PEM", "long", "3", 841));
        rows.Add(("WS_CERT_KEY_PEM_RSA", "long", "4", 842));
        rows.Add(("WS_CLOSE_NORMAL", "long", "1000", 845));
        rows.Add(("WS_CLOSE_GOING_AWAY", "long", "1001", 846));
        rows.Add(("WS_CLOSE_PROTOCOL_ERROR", "long", "1002", 847));
        rows.Add(("WS_CLOSE_UNSUPPORTED_DATA", "long", "1003", 848));
        rows.Add(("WS_CLOSE_NO_STATUS", "long", "1005", 849));
        rows.Add(("WS_CLOSE_ABNORMAL_CLOSE", "long", "1006", 850));
        rows.Add(("WS_CLOSE_INVALID_PAYLOAD", "long", "1007", 851));
        rows.Add(("WS_CLOSE_POLICY_VIOLATION", "long", "1008", 852));
        rows.Add(("WS_CLOSE_MESSAGE_TOO_BIG", "long", "1009", 853));
        rows.Add(("WS_CLOSE_EXTENSION_REQUIRED", "long", "1010", 854));
        rows.Add(("WS_CLOSE_INTERNAL_ENDPOINT_ERROR", "long", "1011", 855));
        rows.Add(("WS_CLOSE_TLS_HANDSHAKE", "long", "1015", 856));
        rows.Add(("WS_CLOSE_SUBPROTOCOL_ERROR", "long", "3000", 857));
        rows.Add(("WS_CLOSE_INVALID_SUBPROTOCOL_DATA", "long", "3001", 858));
        rows.Add(("WS_CLOSE_MQTT_CONN_TIMEOUT", "long", "4001", 859));
        rows.Add(("WS_CLOSE_MQTT_PING_TIMEOUT", "long", "4002", 860));
        rows.Add(("WS_CLOSE_MQTT_CONN_DENIED", "long", "4003", 861));
        rows.Add(("WS_CLOSE_MQTT_CONN_AUTH_FAILED", "long", "4004", 862));
        rows.Add(("WS_E_GENERAL", "long", "1", 865));
        rows.Add(("WS_E_SEND_QUEUE_FULL", "long", "2", 866));
        rows.Add(("WS_E_PAYLOAD_VIOLATION", "long", "3", 867));
        rows.Add(("WS_E_ENDPOINT_NOT_SECURE", "long", "4", 868));
        rows.Add(("WS_E_ENDPOINT_UNAVAILABLE", "long", "5", 869));
        rows.Add(("WS_E_INVALID_URI", "long", "6", 870));
        rows.Add(("WS_E_NO_OUTGOING_BUFFERS", "long", "7", 871));
        rows.Add(("WS_E_NO_INCOMING_BUFFERS", "long", "8", 872));
        rows.Add(("WS_E_INVALID_STATE", "long", "9", 873));
        rows.Add(("WS_E_BAD_CLOSE_CODE", "long", "10", 874));
        rows.Add(("WS_E_RESERVED_CLOSE_CODE", "long", "11", 875));
        rows.Add(("WS_E_INVALID_CLOSE_CODE", "long", "12", 876));
        rows.Add(("WS_E_INVALID_UTF8", "long", "13", 877));
        rows.Add(("WS_E_INVALID_SUBPROTOCOL", "long", "14", 878));
        rows.Add(("WS_E_BAD_CONNECTION", "long", "15", 879));
        rows.Add(("WS_E_OPEN_HANDSHAKE_TIMEOUT", "long", "22", 880));
        rows.Add(("WS_E_CLOSE_HANDSHAKE_TIMEOUT", "long", "23", 881));
        rows.Add(("WS_E_INVALID_PORT", "long", "23", 882));
        rows.Add(("WS_E_OPERATION_CANCELED", "long", "26", 883));
        rows.Add(("WS_E_REJECTED", "long", "27", 884));
        rows.Add(("WS_E_EXTENSION_NEG_FAILED", "long", "32", 885));
        rows.Add(("WS_E_MQTT_CONN_DENIED", "long", "401", 886));
        rows.Add(("WS_E_MQTT_CONN_AUTH_FAILED", "long", "402", 887));
        rows.Add(("WS_E_MQTT_CONN_TIMEOUT", "long", "403", 888));
        rows.Add(("WS_E_MQTT_PUB_TIMEOUT", "long", "404", 889));
        rows.Add(("WS_E_MQTT_SUB_TIMEOUT", "long", "405", 890));
        rows.Add(("WS_E_MQTT_PUB_FAILED", "long", "406", 891));
        rows.Add(("WS_E_MQTT_SUB_FAILED", "long", "407", 892));
        rows.Add(("WS_MQTT_QOS0", "long", "0", 895));
        rows.Add(("WS_MQTT_QOS1", "long", "1", 896));
        rows.Add(("WS_MQTT_QOS2", "long", "2", 897));
        rows.Add(("WS_MQTT_QOS_AT_MOST_ONCE", "long", "0", 898));
        rows.Add(("WS_MQTT_QOS_AT_LEAST_ONCE", "long", "1", 899));
        rows.Add(("WS_MQTT_QOS_AT_EXACTLY_ONCE", "long", "2", 900));

        // ---- Ftp  [enums.sru:L907-L917] ----
        rows.Add(("FTP_MODE_ACTIVE", "long", "0", 907));
        rows.Add(("FTP_MODE_PASSIVE", "long", "1", 908));
        rows.Add(("FTP_LIST_FILE", "long", "1", 911));
        rows.Add(("FTP_LIST_DIR", "long", "2", 912));
        rows.Add(("FTP_LIST_ALL", "long", "3", 913));
        rows.Add(("FTP_CMP_FLAG_LAST_WRITE_TIME", "long", "1", 916));
        rows.Add(("FTP_CMP_FLAG_SIZE", "long", "2", 917));

        // ---- Crypto  [enums.sru:L924-L967] ----
        rows.Add(("CRYPTO_ENCODING_BASE64", "long", "0", 924));
        rows.Add(("CRYPTO_ENCODING_HEX", "long", "1", 925));
        rows.Add(("CRYPTO_HASH_MD5", "long", "0", 928));
        rows.Add(("CRYPTO_HASH_SHA1", "long", "1", 929));
        rows.Add(("CRYPTO_HASH_SHA256", "long", "2", 930));
        rows.Add(("CRYPTO_HASH_SHA384", "long", "3", 931));
        rows.Add(("CRYPTO_HASH_SHA512", "long", "4", 932));
        rows.Add(("CRYPTO_HASH_CRC32", "long", "5", 933));
        rows.Add(("CRYPTO_SYMCRYPT_TYPE_DES", "long", "0", 936));
        rows.Add(("CRYPTO_SYMCRYPT_TYPE_3DES", "long", "1", 937));
        rows.Add(("CRYPTO_SYMCRYPT_TYPE_AES128", "long", "2", 938));
        rows.Add(("CRYPTO_SYMCRYPT_TYPE_AES192", "long", "3", 939));
        rows.Add(("CRYPTO_SYMCRYPT_TYPE_AES256", "long", "4", 940));
        rows.Add(("CRYPTO_SYMCRYPT_MODE_ECB", "long", "0", 943));
        rows.Add(("CRYPTO_SYMCRYPT_MODE_CBC", "long", "1", 944));
        rows.Add(("CRYPTO_SYMCRYPT_MODE_CFB", "long", "2", 945));
        rows.Add(("CRYPTO_SYMCRYPT_MODE_DEFAULT", "long", "0", 946));
        rows.Add(("CRYPTO_RSA_PADDING_PKCS1", "long", "0", 949));
        rows.Add(("CRYPTO_RSA_PADDING_OAEP", "long", "1", 950));
        rows.Add(("CRYPTO_RSA_PADDING_DEFAULT", "long", "0", 951));
        rows.Add(("CRYPTO_RNDSTRING_NUMBER", "uint", "1", 954));
        rows.Add(("CRYPTO_RNDSTRING_ALPHABET", "uint", "2", 955));
        rows.Add(("CRYPTO_RNDSTRING_SYMBOL", "uint", "4", 956));
        rows.Add(("CRYPTO_RNDSTRING_DEFAULT", "uint", "3", 957));
        rows.Add(("CRYPTO_GUID_INCLUDE_BRACKET", "uint", "1", 960));
        rows.Add(("CRYPTO_GUID_INCLUDE_SEPARATOR", "uint", "2", 961));
        rows.Add(("CRYPTO_GUID_DEFAULT", "uint", "3", 962));
        rows.Add(("CRYPTO_RSA_BITS_1024", "ushort", "1024", 965));
        rows.Add(("CRYPTO_RSA_BITS_2048", "ushort", "2048", 966));
        rows.Add(("CRYPTO_RSA_BITS_4096", "ushort", "4096", 967));

        // ---- Regular Expression  [enums.sru:L976-L997] ----
        rows.Add(("REGEXP_MATCH_DEFAULT", "uint", "0", 976));
        rows.Add(("REGEXP_MATCH_NOT_BOL", "uint", "1", 978));
        rows.Add(("REGEXP_MATCH_NOT_EOL", "uint", "2", 980));
        rows.Add(("REGEXP_MATCH_NOT_BOW", "uint", "4", 982));
        rows.Add(("REGEXP_MATCH_NOT_EOW", "uint", "8", 984));
        rows.Add(("REGEXP_MATCH_ANY", "uint", "16", 986));
        rows.Add(("REGEXP_MATCH_NOT_NULL", "uint", "32", 988));
        rows.Add(("REGEXP_MATCH_CONTINUOUS", "uint", "64", 991));
        rows.Add(("REGEXP_MATCH_PREV_AVAIL", "uint", "256", 993));
        rows.Add(("REGEXP_FORMAT_FIRST_ONLY", "uint", "4096", 995));
        rows.Add(("REGEXP_MATCH_GLOBAL", "uint", "65536", 997));

        // ---- Zip  [enums.sru:L1004-L1007] ----
        rows.Add(("ZIP_FORMAT_PFW", "long", "0", 1004));
        rows.Add(("ZIP_FORMAT_GZIP", "long", "1", 1005));
        rows.Add(("ZIP_FORMAT_ZLIB", "long", "2", 1006));
        rows.Add(("ZIP_FORMAT_DEFAULT", "long", "0", 1007));

        // ---- Device Info  [enums.sru:L1014-L1020] ----
        rows.Add(("DEV_TYPE_HDD", "long", "0", 1014));
        rows.Add(("DEV_TYPE_MAC", "long", "1", 1015));
        rows.Add(("DEV_TYPE_CPUID", "long", "2", 1016));
        rows.Add(("DEV_TYPE_MAINBOARD", "long", "3", 1017));
        rows.Add(("DEV_TYPE_BIOS", "long", "4", 1018));
        rows.Add(("DEV_TYPE_IPV4", "long", "5", 1019));
        rows.Add(("DEV_TYPE_IPV6", "long", "6", 1020));

        // ---- Logger  [enums.sru:L1027-L1039] ----
        rows.Add(("LOG_LEVEL_NONE", "uint", "0", 1027));
        rows.Add(("LOG_LEVEL_ERROR", "uint", "1", 1028));
        rows.Add(("LOG_LEVEL_WARNING", "uint", "2", 1029));
        rows.Add(("LOG_LEVEL_INFO", "uint", "4", 1030));
        rows.Add(("LOG_LEVEL_DEBUG", "uint", "8", 1031));
        rows.Add(("LOG_LEVEL_CUSTOM", "uint", "16", 1032));
        rows.Add(("LOG_LEVEL_ALL", "uint", "4294967295", 1033));
        rows.Add(("LOG_LEVEL_DEFAULT", "uint", "7", 1034));
        rows.Add(("LOG_CLEAR_FILE", "uint", "1", 1037));
        rows.Add(("LOG_CLEAR_CONSOLE", "uint", "2", 1038));
        rows.Add(("LOG_CLEAR_DEFAULT", "uint", "3", 1039));

        // ---- Compiler  [enums.sru:L1046-L1055] ----
        rows.Add(("CMP_TYPE_DATAWINDOW", "long", "1", 1046));
        rows.Add(("CMP_TYPE_FUNCTION", "long", "2", 1047));
        rows.Add(("CMP_TYPE_MENU", "long", "3", 1048));
        rows.Add(("CMP_TYPE_QUERY", "long", "4", 1049));
        rows.Add(("CMP_TYPE_STRUCTURE", "long", "5", 1050));
        rows.Add(("CMP_TYPE_USEROBJECT", "long", "6", 1051));
        rows.Add(("CMP_TYPE_WINDOW", "long", "7", 1052));
        rows.Add(("CMP_TYPE_PIPELINE", "long", "8", 1053));
        rows.Add(("CMP_TYPE_PROJECT", "long", "9", 1054));
        rows.Add(("CMP_TYPE_PROXYOBJECT", "long", "10", 1055));

        // ---- Barcode  [enums.sru:L1062-L1091] ----
        rows.Add(("BARCODE_OPT_OUTPUT", "long", "0", 1062));
        rows.Add(("BARCODE_OPT_ROWS", "long", "1", 1063));
        rows.Add(("BARCODE_OPT_COLUMNS", "long", "2", 1064));
        rows.Add(("BARCODE_OPT_VERSION", "long", "2", 1065));
        rows.Add(("BARCODE_OPT_SECURE", "long", "1", 1066));
        rows.Add(("BARCODE_OPT_MODE", "long", "1", 1067));
        rows.Add(("BARCODE_OPTV_OUTPUT_READER_INIT", "long", "16", 1071));
        rows.Add(("BARCODE_OPTV_OUTPUT_SMALL_TEXT", "long", "32", 1072));
        rows.Add(("BARCODE_OPTV_OUTPUT_BOLD_TEXT", "long", "64", 1073));
        rows.Add(("BARCODE_UNIT_PIXEL", "long", "0", 1076));
        rows.Add(("BARCODE_UNIT_DIP", "long", "1", 1077));
        rows.Add(("BARCODE_UNIT_DEFAULT", "long", "0", 1078));
        rows.Add(("BARCODE_SVG_FULL", "long", "0", 1081));
        rows.Add(("BARCODE_SVG_INLINE", "long", "1", 1082));
        rows.Add(("BARCODE_SVG_DEFAULT", "long", "0", 1083));
        rows.Add(("BARCODE_DATA_PNG", "long", "0", 1086));
        rows.Add(("BARCODE_DATA_JPEG", "long", "1", 1087));
        rows.Add(("BARCODE_DATA_GIF", "long", "2", 1088));
        rows.Add(("BARCODE_DATA_BMP", "long", "3", 1089));
        rows.Add(("BARCODE_DATA_SVG", "long", "4", 1090));
        rows.Add(("BARCODE_DATA_DEFAULT", "long", "0", 1091));

        // ---- QR Code  [enums.sru:L1098-L1122] ----
        rows.Add(("QRCODE_ECC_AUTO", "long", "0", 1098));
        rows.Add(("QRCODE_ECC_LOW", "long", "1", 1099));
        rows.Add(("QRCODE_ECC_MEDIUM", "long", "2", 1100));
        rows.Add(("QRCODE_ECC_QUARTILE", "long", "3", 1101));
        rows.Add(("QRCODE_ECC_HIGH", "long", "4", 1102));
        rows.Add(("QRCODE_ECC_DEFAULT", "long", "0", 1103));
        rows.Add(("QRCODE_UNIT_PIXEL", "long", "0", 1106));
        rows.Add(("QRCODE_UNIT_DIP", "long", "1", 1107));
        rows.Add(("QRCODE_UNIT_DEFAULT", "long", "0", 1108));
        rows.Add(("QRCODE_SVG_FULL", "long", "0", 1111));
        rows.Add(("QRCODE_SVG_INLINE", "long", "1", 1112));
        rows.Add(("QRCODE_SVG_PATH", "long", "2", 1113));
        rows.Add(("QRCODE_SVG_DEFAULT", "long", "0", 1114));
        rows.Add(("QRCODE_DATA_PNG", "long", "0", 1117));
        rows.Add(("QRCODE_DATA_JPEG", "long", "1", 1118));
        rows.Add(("QRCODE_DATA_GIF", "long", "2", 1119));
        rows.Add(("QRCODE_DATA_BMP", "long", "3", 1120));
        rows.Add(("QRCODE_DATA_SVG", "long", "4", 1121));
        rows.Add(("QRCODE_DATA_DEFAULT", "long", "0", 1122));

        // ---- File Scanner  [enums.sru:L1129-L1130] ----
        rows.Add(("FILE_SCANNER_SORT_ASC", "uint", "65536", 1129));
        rows.Add(("FILE_SCANNER_SORT_DESC", "uint", "131072", 1130));

        // ---- Camera Capture  [enums.sru:L1137-L1140] ----
        rows.Add(("IMAGE_FILE_FORMAT_JPEG", "long", "0", 1137));
        rows.Add(("IMAGE_FILE_FORMAT_PNG", "long", "1", 1138));
        rows.Add(("IMAGE_FILE_FORMAT_GIF", "long", "2", 1139));
        rows.Add(("IMAGE_FILE_FORMAT_BMP", "long", "3", 1140));

        // ---- Pinyin  [enums.sru:L1147-L1149] ----
        rows.Add(("PY_LIKE_IGNORE_CASE", "long", "1", 1147));
        rows.Add(("PY_LIKE_IGNORE_WIDTH", "long", "2", 1148));
        rows.Add(("PY_LIKE_FUZZY_SOUND", "long", "4", 1149));

        return [.. rows];
    }

    /// <summary>
    /// One entry per section banner in <c>enums.sru</c>: the banner text, the number of declarations
    /// under it, and the first and last oracle line those declarations occupy.
    /// </summary>
    /// <returns>All 24 sections, in the oracle's own order.</returns>
    private static (string Section, int Count, int FirstLine, int LastLine)[] BuildSectionTable()
    {
        List<(string Section, int Count, int FirstLine, int LastLine)> rows = [];
        rows.Add(("PowerFramework enums", 9, 41, 49));
        rows.Add(("UI", 49, 54, 111));
        rows.Add(("I18N", 8, 115, 123));
        rows.Add(("Sciter", 259, 128, 486));
        rows.Add(("Blink", 40, 493, 545));
        rows.Add(("WebView", 16, 552, 574));
        rows.Add(("Thread", 4, 581, 584));
        rows.Add(("XML Parser", 53, 591, 693));
        rows.Add(("JSON Parser", 10, 700, 711));
        rows.Add(("SQL Parser", 3, 718, 720));
        rows.Add(("Http", 79, 727, 820));
        rows.Add(("WebSocket", 64, 827, 900));
        rows.Add(("Ftp", 7, 907, 917));
        rows.Add(("Crypto", 30, 924, 967));
        rows.Add(("Regular Expression", 11, 976, 997));
        rows.Add(("Zip", 4, 1004, 1007));
        rows.Add(("Device Info", 7, 1014, 1020));
        rows.Add(("Logger", 11, 1027, 1039));
        rows.Add(("Compiler", 10, 1046, 1055));
        rows.Add(("Barcode", 21, 1062, 1091));
        rows.Add(("QR Code", 19, 1098, 1122));
        rows.Add(("File Scanner", 2, 1129, 1130));
        rows.Add(("Camera Capture", 4, 1137, 1140));
        rows.Add(("Pinyin", 3, 1147, 1149));
        return [.. rows];
    }

    /// <summary>
    /// The oracle table, built once.
    /// </summary>
    private static readonly (string Identifier, string ExpectedType, string ExpectedValue, int OracleLine)[] OracleTable
        = BuildOracleTable();

    /// <summary>
    /// The section table, built once.
    /// </summary>
    private static readonly (string Section, int Count, int FirstLine, int LastLine)[] SectionTable
        = BuildSectionTable();

    /// <summary>
    /// The oracle table projected into theory rows.
    /// </summary>
    /// <returns>All 723 rows, in the oracle's own declaration order.</returns>
    public static TheoryData<string, string, string, int> OracleRows()
    {
        TheoryData<string, string, string, int> rows = [];

        foreach ((string identifier, string expectedType, string expectedValue, int oracleLine) in OracleTable)
        {
            rows.Add(identifier, expectedType, expectedValue, oracleLine);
        }

        return rows;
    }

    /// <summary>
    /// The section table projected into theory rows.
    /// </summary>
    /// <returns>All 24 rows, in the oracle's own order.</returns>
    public static TheoryData<string, int, int, int> SectionRows()
    {
        TheoryData<string, int, int, int> rows = [];

        foreach ((string section, int count, int firstLine, int lastLine) in SectionTable)
        {
            rows.Add(section, count, firstLine, lastLine);
        }

        return rows;
    }

    /// <summary>
    /// The binding flags that select exactly the members a <c>Constant</c> declaration becomes.
    /// </summary>
    private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;

    /// <summary>
    /// Every public constant field of <see cref="Enums"/>, in reflection order.
    /// </summary>
    /// <returns>The literal fields.</returns>
    private static FieldInfo[] DeclaredConstants()
    {
        return typeof(Enums)
            .GetFields(PublicStatic)
            .Where(field => field.IsLiteral && !field.IsInitOnly)
            .ToArray();
    }

    /// <summary>
    /// Renders a CLR type as the C# keyword the port declares it with, so a failure message reads in
    /// the vocabulary of the source rather than in reflection's.
    /// </summary>
    /// <param name="type">The field type.</param>
    /// <returns>The C# keyword, or the type name when there is no keyword for it.</returns>
    private static string TypeKeyword(System.Type type)
    {
        if (type == typeof(long))
        {
            return "long";
        }

        if (type == typeof(uint))
        {
            return "uint";
        }

        if (type == typeof(ushort))
        {
            return "ushort";
        }

        return type == typeof(string) ? "string" : type.Name;
    }

    // ==============================================================================================
    //  1. THE 723 DECLARATIONS
    // ==============================================================================================

    /// <summary>
    /// Every one of the 723 constants declares the identifier, the CLR type and the value its oracle
    /// line declares.
    /// </summary>
    /// <param name="identifier">
    /// The SCREAMING_SNAKE identifier, preserved verbatim from PowerScript per AAP 0.4.5.3.
    /// </param>
    /// <param name="expectedType">The C# keyword the PowerScript type maps onto.</param>
    /// <param name="expectedValue">The value, rendered with the invariant culture.</param>
    /// <param name="oracleLine">The <c>enums.sru</c> line this row was transcribed from.</param>
    /// <remarks>
    /// <para>
    /// The value is read with <see cref="FieldInfo.GetRawConstantValue"/> rather than
    /// <see cref="FieldInfo.GetValue(object)"/>, which is the point of the row rather than a detail:
    /// the raw constant value is the literal recorded in the assembly's metadata, so this reads what
    /// callers will actually be compiled against.
    /// </para>
    /// <para>
    /// Comparing the rendered text rather than a boxed value keeps one row shape for all four types
    /// and makes a failure legible - "expected 3847, actual 3855" rather than a boxed-equality
    /// mismatch between two <c>object</c>s. The invariant culture is passed explicitly so that a host
    /// whose negative sign or digit shapes differ cannot turn a correct value into a failure.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(OracleRows))]
    public void EveryConstantMatchesItsLegacyDeclaration(
        string identifier,
        string expectedType,
        string expectedValue,
        int oracleLine)
    {
        FieldInfo? field = typeof(Enums).GetField(identifier, PublicStatic);

        Assert.NotNull(field);

        Assert.True(
            field.IsLiteral && !field.IsInitOnly,
            $"{identifier} must be a `const`, because enums.sru:L{oracleLine} declares a PowerScript "
                + "Constant. A static readonly field would compile but would stop being a compile-time "
                + "literal for consumers.");

        Assert.Equal(expectedType, TypeKeyword(field.FieldType));

        object? raw = field.GetRawConstantValue();

        Assert.NotNull(raw);
        Assert.Equal(
            expectedValue,
            System.Convert.ToString(raw, CultureInfo.InvariantCulture));
    }

    // ==============================================================================================
    //  2. THE REVERSE DIRECTION
    // ==============================================================================================

    /// <summary>
    /// Nothing declared in <see cref="Enums"/> is missing from the oracle table, so a constant cannot
    /// be added to the port without a row that names its oracle line.
    /// </summary>
    /// <remarks>
    /// The theory above proves every ORACLE row reaches the port. This proves the converse, and the
    /// pair together is what makes the catalogue closed: a constant invented here with no legacy
    /// declaration behind it is scope creep against C-B, and one silently deleted from the oracle side
    /// would leave a row with nothing to match.
    /// </remarks>
    [Fact]
    public void EveryPublicConstantAppearsInTheOracleTableExactlyOnce()
    {
        HashSet<string> tabled = new(System.StringComparer.Ordinal);

        foreach ((string identifier, _, _, _) in OracleTable)
        {
            Assert.True(
                tabled.Add(identifier),
                $"{identifier} appears more than once in the oracle table, so one of its rows is "
                    + "shadowing the other and a wrong value could pass unnoticed.");
        }

        List<string> undeclared = DeclaredConstants()
            .Select(field => field.Name)
            .Where(name => !tabled.Contains(name))
            .OrderBy(name => name, System.StringComparer.Ordinal)
            .ToList();

        Assert.True(
            undeclared.Count == 0,
            "These constants exist in Enums.cs with no row in the oracle table, so nothing checks "
                + "their value against enums.sru: "
                + string.Join(", ", undeclared));
    }

    // ==============================================================================================
    //  3. THE CENSUS
    // ==============================================================================================

    /// <summary>
    /// The catalogue holds exactly 723 constants, split across the four PowerScript types in the
    /// proportions the oracle declares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The total is the audit AAP 0.4.2.3 asks for. The per-type histogram is the part that catches a
    /// subtler mistake: a constant moved from <c>uint</c> to <c>long</c> keeps its value, keeps its
    /// name, changes no row's rendered text, and would pass the theory above while changing the width
    /// a consumer stores it in.
    /// </para>
    /// <para>
    /// 505 rather than 502 for <c>long</c> because three declarations spell the keyword in lower case
    /// - <c>constant long</c> - which PowerScript accepts and a case-sensitive census would miss.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDeclarationCensusMatchesTheOracle()
    {
        FieldInfo[] declared = DeclaredConstants();

        Assert.Equal(723, declared.Length);
        Assert.Equal(723, OracleTable.Length);

        Dictionary<string, int> histogram = declared
            .GroupBy(field => TypeKeyword(field.FieldType), System.StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), System.StringComparer.Ordinal);

        Assert.Equal(505, histogram["long"]);
        Assert.Equal(179, histogram["uint"]);
        Assert.Equal(34, histogram["ushort"]);
        Assert.Equal(5, histogram["string"]);
        Assert.Equal(4, histogram.Count);
    }

    // ==============================================================================================
    //  4. THE 24 SECTIONS
    // ==============================================================================================

    /// <summary>
    /// Each of the oracle's 24 section banners accounts for the number of declarations it covers, and
    /// the line ranges of the 24 sections partition the table without overlapping.
    /// </summary>
    /// <param name="section">The banner text, as <c>enums.sru</c> writes it between its comment markers.</param>
    /// <param name="expectedCount">How many declarations sit under the banner.</param>
    /// <param name="firstLine">The first declaration line in the section.</param>
    /// <param name="lastLine">The last declaration line in the section.</param>
    /// <remarks>
    /// This is what turns "723 in total" into "723 in the right places". A constant that moved from one
    /// capability family to another - Sciter to Blink, say - would leave the total untouched and both
    /// per-section counts wrong, and since the families map onto DIFFERENT target services in
    /// AAP 0.4.1, that is a mapping error rather than a cosmetic one.
    /// </remarks>
    [Theory]
    [MemberData(nameof(SectionRows))]
    public void EverySectionCountMatchesItsOracleBanner(
        string section,
        int expectedCount,
        int firstLine,
        int lastLine)
    {
        Assert.NotEmpty(section);
        Assert.True(firstLine <= lastLine, $"{section} has an inverted line range.");

        int inRange = OracleTable.Count(row => row.OracleLine >= firstLine && row.OracleLine <= lastLine);

        Assert.Equal(expectedCount, inRange);
    }

    /// <summary>
    /// The 24 section counts sum to the whole catalogue, and every row falls inside exactly one
    /// section's line range.
    /// </summary>
    /// <remarks>
    /// The complement of the theory above. Without it, two sections could overlap - each reporting the
    /// right count while double-counting the same declarations - or a run of constants could sit in a
    /// gap between two ranges and be covered by no section at all.
    /// </remarks>
    [Fact]
    public void TheSectionsPartitionTheCatalogueExactly()
    {
        Assert.Equal(24, SectionTable.Length);
        Assert.Equal(723, SectionTable.Sum(section => section.Count));

        foreach ((string identifier, _, _, int line) in OracleTable)
        {
            int covering = SectionTable.Count(
                section => line >= section.FirstLine && line <= section.LastLine);

            Assert.True(
                covering == 1,
                $"{identifier} at enums.sru:L{line} is covered by {covering} sections rather than "
                    + "exactly one, so the section ranges either overlap or leave a gap.");
        }
    }

    // ==============================================================================================
    //  5. THE SHAPE OF THE TYPE
    // ==============================================================================================

    /// <summary>
    /// <see cref="Enums"/> exposes constants and nothing else: no property, no method, no event, no
    /// nested type and no mutable or non-literal field.
    /// </summary>
    /// <remarks>
    /// <c>enums.sru</c> is a declaration file. Its PowerScript ancestor is a <c>nonvisualobject</c>
    /// whose entire body is a <c>type variables</c> block, so it has no behaviour to port, and a
    /// helper added here - a name-to-value lookup, a "parse a flag" convenience - would be new
    /// functionality rather than a migration (C-B). Reflection is the only way to state that as a
    /// standing check rather than a review habit.
    /// </remarks>
    [Fact]
    public void EnumsExposesConstantsAndNothingElse()
    {
        System.Type enums = typeof(Enums);

        Assert.True(enums.IsAbstract && enums.IsSealed, "Enums must be a static class.");

        Assert.Empty(enums.GetProperties(PublicStatic | BindingFlags.Instance));
        Assert.Empty(enums.GetEvents(PublicStatic | BindingFlags.Instance));
        Assert.Empty(enums.GetNestedTypes(BindingFlags.Public));

        Assert.DoesNotContain(
            enums.GetMethods(PublicStatic | BindingFlags.DeclaredOnly),
            method => !method.IsSpecialName);

        Assert.DoesNotContain(
            enums.GetFields(PublicStatic),
            field => !field.IsLiteral || field.IsInitOnly);

        // Nothing is hidden behind a non-public member either, which is what stops a "private helper"
        // from smuggling behaviour into a declaration file.
        Assert.Empty(enums.GetFields(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance));
    }

    // ==============================================================================================
    //  6. THE NAMED FAMILIES
    //
    //  Each fact below states a relationship BETWEEN constants that the table above cannot express,
    //  and each one has a consumer that depends on it.
    // ==============================================================================================

    /// <summary>
    /// The capability gate: eight bits, a sparse layout, and an <c>ALL</c> value that deliberately
    /// OMITS <c>BLINKFAST</c>. [enums.sru:L41-L49]
    /// </summary>
    /// <remarks>
    /// <para>
    /// C-B DEFECT-SHAPED BEHAVIOUR PINNED AS CORRECT. The omission is not an arithmetic slip:
    /// <c>blink.dll</c> and <c>blinkfast.dll</c> are two alternative builds of ONE engine, so asking
    /// for both at once is meaningless. Gateway's <c>CapabilityFlags</c> projects this exact mask, so
    /// the omission is observable at the ingress and a "helpful" completion of the sum would change
    /// the projected capability set.
    /// </para>
    /// <para>
    /// The sparse layout is asserted too. The values run 1, 2, 4, 8 and then jump to 256, leaving
    /// bits 4 through 7 - values 16, 32, 64 and 128 - unused, and the gap is itself observable through
    /// <c>ALL</c>. Filling it would collide with whatever the legacy author reserved the range for.
    /// </para>
    /// </remarks>
    [Fact]
    public void CapabilityFlagsAreEightSparseBitsAndAllOmitsBlinkFast()
    {
        Assert.Equal(1L, Enums.INIT_FLAG_ENABLE_UI);
        Assert.Equal(2L, Enums.INIT_FLAG_ENABLE_SCITER);
        Assert.Equal(4L, Enums.INIT_FLAG_ENABLE_BLINK);
        Assert.Equal(8L, Enums.INIT_FLAG_ENABLE_BLINKFAST);
        Assert.Equal(256L, Enums.INIT_FLAG_ENABLE_ORCA);
        Assert.Equal(512L, Enums.INIT_FLAG_ENABLE_SQLITE);
        Assert.Equal(1024L, Enums.INIT_FLAG_ENABLE_DPIAWARE);
        Assert.Equal(2048L, Enums.INIT_FLAG_ENABLE_WEBVIEW);

        // The composite, and the omission that is the whole point of it.
        Assert.Equal(3847L, Enums.INIT_FLAG_ENABLE_ALL);
        Assert.Equal(0L, Enums.INIT_FLAG_ENABLE_ALL & Enums.INIT_FLAG_ENABLE_BLINKFAST);

        // ...stated a second way, as the sum the oracle actually writes, so a failure distinguishes
        // "the total drifted" from "a term was added or dropped".
        Assert.Equal(
            Enums.INIT_FLAG_ENABLE_UI
                + Enums.INIT_FLAG_ENABLE_SCITER
                + Enums.INIT_FLAG_ENABLE_BLINK
                + Enums.INIT_FLAG_ENABLE_ORCA
                + Enums.INIT_FLAG_ENABLE_SQLITE
                + Enums.INIT_FLAG_ENABLE_DPIAWARE
                + Enums.INIT_FLAG_ENABLE_WEBVIEW,
            Enums.INIT_FLAG_ENABLE_ALL);

        // Every other bit IS present, so the omission is singular rather than the first of several.
        Assert.Equal(Enums.INIT_FLAG_ENABLE_UI, Enums.INIT_FLAG_ENABLE_ALL & Enums.INIT_FLAG_ENABLE_UI);
        Assert.Equal(Enums.INIT_FLAG_ENABLE_SCITER, Enums.INIT_FLAG_ENABLE_ALL & Enums.INIT_FLAG_ENABLE_SCITER);
        Assert.Equal(Enums.INIT_FLAG_ENABLE_BLINK, Enums.INIT_FLAG_ENABLE_ALL & Enums.INIT_FLAG_ENABLE_BLINK);
        Assert.Equal(Enums.INIT_FLAG_ENABLE_ORCA, Enums.INIT_FLAG_ENABLE_ALL & Enums.INIT_FLAG_ENABLE_ORCA);
        Assert.Equal(Enums.INIT_FLAG_ENABLE_SQLITE, Enums.INIT_FLAG_ENABLE_ALL & Enums.INIT_FLAG_ENABLE_SQLITE);
        Assert.Equal(Enums.INIT_FLAG_ENABLE_DPIAWARE, Enums.INIT_FLAG_ENABLE_ALL & Enums.INIT_FLAG_ENABLE_DPIAWARE);
        Assert.Equal(Enums.INIT_FLAG_ENABLE_WEBVIEW, Enums.INIT_FLAG_ENABLE_ALL & Enums.INIT_FLAG_ENABLE_WEBVIEW);

        // The sparse gap: bits 4 through 7 are unassigned, and ALL proves it.
        foreach (long unassigned in new[] { 16L, 32L, 64L, 128L })
        {
            Assert.Equal(0L, Enums.INIT_FLAG_ENABLE_ALL & unassigned);
        }

        // Each of the eight is a single bit, which is what makes the mask composable at all.
        foreach (long bit in new[]
        {
            Enums.INIT_FLAG_ENABLE_UI,
            Enums.INIT_FLAG_ENABLE_SCITER,
            Enums.INIT_FLAG_ENABLE_BLINK,
            Enums.INIT_FLAG_ENABLE_BLINKFAST,
            Enums.INIT_FLAG_ENABLE_ORCA,
            Enums.INIT_FLAG_ENABLE_SQLITE,
            Enums.INIT_FLAG_ENABLE_DPIAWARE,
            Enums.INIT_FLAG_ENABLE_WEBVIEW,
        })
        {
            Assert.Equal(0L, bit & (bit - 1L));
        }
    }

    /// <summary>
    /// The SQL clause-modification styles are 1, 2 and 3 - contiguous, distinct and non-zero.
    /// [enums.sru:L718-L720]
    /// </summary>
    /// <remarks>
    /// Persistence's clause modifier dispatches on these and treats a zero index or an empty clause as
    /// an invalid argument, so a zero value here would collide with its own "absent" sentinel. They
    /// also cross the wire on the Persistence contract, which is why the exact numbers matter and not
    /// merely their distinctness.
    /// </remarks>
    [Fact]
    public void SqlModificationStylesAreOneTwoThree()
    {
        Assert.Equal(1L, Enums.SQL_MS_REPLACE);
        Assert.Equal(2L, Enums.SQL_MS_APPEND);
        Assert.Equal(3L, Enums.SQL_MS_PREPEND);

        Assert.Equal(3, new[] { Enums.SQL_MS_REPLACE, Enums.SQL_MS_APPEND, Enums.SQL_MS_PREPEND }.Distinct().Count());
        Assert.DoesNotContain(0L, new[] { Enums.SQL_MS_REPLACE, Enums.SQL_MS_APPEND, Enums.SQL_MS_PREPEND });
    }

    /// <summary>
    /// The localization sources and categories, including the boundary value above which a consumer
    /// defines its own. [enums.sru:L115-L123]
    /// </summary>
    /// <remarks>
    /// <c>Categories.CAT_MSGBOX</c> and <c>Categories.CAT_DWSVC</c> are declared as offsets from
    /// <c>I18N_CAT_CUSTOM</c> in the Localization library, so its two constants move if this one does.
    /// The window and DataWindow categories being DIFFERENT values while mapping onto the SAME XML
    /// element is a quirk of the English provider rather than of these constants, and it is pinned
    /// there.
    /// </remarks>
    [Fact]
    public void LocalizationSourcesAndCategoriesAreContiguousFromZero()
    {
        Assert.Equal(0L, Enums.I18N_SRC_PFW);
        Assert.Equal(1L, Enums.I18N_SRC_CUSTOM);

        Assert.Equal(0L, Enums.I18N_CAT_WINDOW);
        Assert.Equal(1L, Enums.I18N_CAT_TABCONTROL);
        Assert.Equal(2L, Enums.I18N_CAT_RIBBONBAR);
        Assert.Equal(3L, Enums.I18N_CAT_SPLITCONTAINER);
        Assert.Equal(4L, Enums.I18N_CAT_DATAWINDOW);
        Assert.Equal(5L, Enums.I18N_CAT_CUSTOM);

        // The boundary is the LAST framework category, so a consumer's own categories start above it.
        Assert.Equal(
            Enums.I18N_CAT_CUSTOM,
            new[]
            {
                Enums.I18N_CAT_WINDOW,
                Enums.I18N_CAT_TABCONTROL,
                Enums.I18N_CAT_RIBBONBAR,
                Enums.I18N_CAT_SPLITCONTAINER,
                Enums.I18N_CAT_DATAWINDOW,
                Enums.I18N_CAT_CUSTOM,
            }.Max());
    }

    /// <summary>
    /// The cryptographic families, and every weak default among them, exactly as the legacy declares
    /// them. [enums.sru:L924-L967]
    /// </summary>
    /// <remarks>
    /// <para>
    /// C-B WEAK DEFAULTS PINNED AS CORRECT BEHAVIOUR, NOT AS AN OVERSIGHT. Two of these rows are the
    /// ones that matter: the default symmetric mode is ECB, and the default RSA padding is PKCS#1.
    /// Both are weak by modern standards, both are preserved rather than corrected, and both are
    /// annotated as known legacy weaknesses on the Security contract. A test that "fixed" either by
    /// pointing the default at a safer member would be a behaviour change dressed as a security
    /// improvement.
    /// </para>
    /// <para>
    /// The alias rows are asserted by IDENTITY against their referent rather than by literal value,
    /// which is how the oracle writes them - <c>CRYPTO_SYMCRYPT_MODE_DEFAULT = CRYPTO_SYMCRYPT_MODE_ECB</c>
    /// - so a failure says which relationship broke rather than merely that a number moved.
    /// </para>
    /// </remarks>
    [Fact]
    public void CryptographicFamiliesAndTheirWeakDefaultsArePreserved()
    {
        // Encoding.
        Assert.Equal(0L, Enums.CRYPTO_ENCODING_BASE64);
        Assert.Equal(1L, Enums.CRYPTO_ENCODING_HEX);

        // Hash types, contiguous 0..5 in the oracle's own order.
        Assert.Equal(0L, Enums.CRYPTO_HASH_MD5);
        Assert.Equal(1L, Enums.CRYPTO_HASH_SHA1);
        Assert.Equal(2L, Enums.CRYPTO_HASH_SHA256);
        Assert.Equal(3L, Enums.CRYPTO_HASH_SHA384);
        Assert.Equal(4L, Enums.CRYPTO_HASH_SHA512);
        Assert.Equal(5L, Enums.CRYPTO_HASH_CRC32);

        // Symmetric ciphers and modes.
        Assert.Equal(0L, Enums.CRYPTO_SYMCRYPT_MODE_ECB);
        Assert.Equal(1L, Enums.CRYPTO_SYMCRYPT_MODE_CBC);
        Assert.Equal(2L, Enums.CRYPTO_SYMCRYPT_MODE_CFB);

        // THE WEAK DEFAULT. ECB, and it stays ECB.
        Assert.Equal(Enums.CRYPTO_SYMCRYPT_MODE_ECB, Enums.CRYPTO_SYMCRYPT_MODE_DEFAULT);
        Assert.NotEqual(Enums.CRYPTO_SYMCRYPT_MODE_CBC, Enums.CRYPTO_SYMCRYPT_MODE_DEFAULT);

        // THE OTHER WEAK DEFAULT. PKCS#1, and it stays PKCS#1.
        Assert.Equal(Enums.CRYPTO_RSA_PADDING_PKCS1, Enums.CRYPTO_RSA_PADDING_DEFAULT);

        // The two composite defaults, each written as its own sum in the oracle.
        Assert.Equal(
            Enums.CRYPTO_RNDSTRING_NUMBER + Enums.CRYPTO_RNDSTRING_ALPHABET,
            Enums.CRYPTO_RNDSTRING_DEFAULT);
        Assert.Equal(
            Enums.CRYPTO_GUID_INCLUDE_BRACKET + Enums.CRYPTO_GUID_INCLUDE_SEPARATOR,
            Enums.CRYPTO_GUID_DEFAULT);
    }

    /// <summary>
    /// The two composite XML parse flags are the sums the oracle writes, one of which contains the
    /// other. [enums.sru:L655, L659]
    /// </summary>
    /// <remarks>
    /// <c>XML_PARSE_FULL</c> is declared in terms of <c>XML_PARSE_DEFAULT</c>, so the containment is a
    /// property of the declaration rather than a coincidence of the numbers, and it is asserted as
    /// such. These belong to the deferred Documents service and ship as a name and a value only, which
    /// is exactly why nothing else in the tree would notice if one drifted.
    /// </remarks>
    [Fact]
    public void CompositeXmlParseFlagsAreTheSumsTheOracleWrites()
    {
        Assert.Equal(
            Enums.XML_PARSE_CDATA
                + Enums.XML_PARSE_ESCAPES
                + Enums.XML_PARSE_WCONV_ATTRIBUTE
                + Enums.XML_PARSE_EOL,
            Enums.XML_PARSE_DEFAULT);

        Assert.Equal(
            Enums.XML_PARSE_DEFAULT
                + Enums.XML_PARSE_PI
                + Enums.XML_PARSE_COMMENTS
                + Enums.XML_PARSE_DECLARATION
                + Enums.XML_PARSE_DOCTYPE,
            Enums.XML_PARSE_FULL);

        // FULL contains DEFAULT, which is what the declaration says and what a consumer relies on.
        Assert.Equal(Enums.XML_PARSE_DEFAULT, Enums.XML_PARSE_FULL & Enums.XML_PARSE_DEFAULT);
    }

    /// <summary>
    /// The logger's two composite defaults are the sums the oracle writes. [enums.sru:L1034, L1039]
    /// </summary>
    /// <remarks>
    /// <c>LOG_LEVEL_DEFAULT</c> omits <c>LOG_LEVEL_DEBUG</c>, which is the behaviour
    /// <c>w_test_logger.srw</c> relies on when it notes that a debug line is emitted only once the
    /// debug level has been switched on - the same file whose comments are the oracle for the
    /// <c>Sprintf</c> grammar.
    /// </remarks>
    [Fact]
    public void LoggerCompositeDefaultsAreTheSumsTheOracleWrites()
    {
        Assert.Equal(
            Enums.LOG_LEVEL_ERROR + Enums.LOG_LEVEL_WARNING + Enums.LOG_LEVEL_INFO,
            Enums.LOG_LEVEL_DEFAULT);

        Assert.Equal(Enums.LOG_CLEAR_FILE + Enums.LOG_CLEAR_CONSOLE, Enums.LOG_CLEAR_DEFAULT);

        // The debug level is deliberately NOT in the default set.
        Assert.Equal(0u, Enums.LOG_LEVEL_DEFAULT & Enums.LOG_LEVEL_DEBUG);
    }

    /// <summary>
    /// Every alias the oracle declares by referring to another constant holds that relationship, not
    /// merely the same number today.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The WebSocket certificate family is the largest group: eight constants declared as the HTTP
    /// family's values [enums.sru:L833-L842]. Because both sides are separately declared, the two could
    /// drift apart in a way no single-value row would catch, so the relationship is what is asserted.
    /// </para>
    /// <para>
    /// The MQTT quality-of-service names are the same pattern with a different purpose: they give the
    /// numeric levels their protocol-standard names, so <c>AT_MOST_ONCE</c> and <c>QOS0</c> must remain
    /// the same value or a caller choosing by name would get a different delivery guarantee.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryDeclaredAliasStillPointsAtItsReferent()
    {
        // WebSocket certificate handling reuses the HTTP values verbatim. [L833-L842]
        Assert.Equal(Enums.HTTP_CERT_FILE, Enums.WS_CERT_FILE);
        Assert.Equal(Enums.HTTP_CERT_FILE_DER, Enums.WS_CERT_FILE_DER);
        Assert.Equal(Enums.HTTP_CERT_FILE_PKCS12, Enums.WS_CERT_FILE_PKCS12);
        Assert.Equal(Enums.HTTP_CERT_PEM, Enums.WS_CERT_PEM);
        Assert.Equal(Enums.HTTP_CERT_KEY_FILE, Enums.WS_CERT_KEY_FILE);
        Assert.Equal(Enums.HTTP_CERT_KEY_FILE_RSA, Enums.WS_CERT_KEY_FILE_RSA);
        Assert.Equal(Enums.HTTP_CERT_KEY_PEM, Enums.WS_CERT_KEY_PEM);
        Assert.Equal(Enums.HTTP_CERT_KEY_PEM_RSA, Enums.WS_CERT_KEY_PEM_RSA);

        // MQTT quality of service, by name and by number. [L898-L900]
        Assert.Equal(Enums.WS_MQTT_QOS0, Enums.WS_MQTT_QOS_AT_MOST_ONCE);
        Assert.Equal(Enums.WS_MQTT_QOS1, Enums.WS_MQTT_QOS_AT_LEAST_ONCE);
        Assert.Equal(Enums.WS_MQTT_QOS2, Enums.WS_MQTT_QOS_AT_EXACTLY_ONCE);

        // The single-referent defaults. [L681, L711, L1007, L1078, L1083, L1091, L1103, L1108, L1114, L1122]
        Assert.Equal(Enums.XML_FORMAT_INDENT, Enums.XML_FORMAT_DEFAULT);
        Assert.Equal(Enums.JSON_FORMAT_NONE, Enums.JSON_FORMAT_DEFAULT);
        Assert.Equal(Enums.ZIP_FORMAT_PFW, Enums.ZIP_FORMAT_DEFAULT);
        Assert.Equal(Enums.BARCODE_UNIT_PIXEL, Enums.BARCODE_UNIT_DEFAULT);
        Assert.Equal(Enums.BARCODE_SVG_FULL, Enums.BARCODE_SVG_DEFAULT);
        Assert.Equal(Enums.BARCODE_DATA_PNG, Enums.BARCODE_DATA_DEFAULT);
        Assert.Equal(Enums.QRCODE_ECC_AUTO, Enums.QRCODE_ECC_DEFAULT);
        Assert.Equal(Enums.QRCODE_UNIT_PIXEL, Enums.QRCODE_UNIT_DEFAULT);
        Assert.Equal(Enums.QRCODE_SVG_FULL, Enums.QRCODE_SVG_DEFAULT);
        Assert.Equal(Enums.QRCODE_DATA_PNG, Enums.QRCODE_DATA_DEFAULT);

        // The one arithmetic right-hand side that is not a plain literal. [L196]
        Assert.Equal(1024L + 512L, Enums.SC_HANDLE_INVOKE_METHOD);
    }

    /// <summary>
    /// The five HTTP method constants are the exact upper-case tokens the oracle spells, not an
    /// enumeration and not a normalised form. [enums.sru:L727-L731]
    /// </summary>
    /// <remarks>
    /// These are the only five <c>Constant String</c> declarations in the whole catalogue, and their
    /// case is part of the value: HTTP methods are case-sensitive on the wire, so a folded token would
    /// produce a request the far end rejects. Asserted with ordinal comparison for the same reason.
    /// </remarks>
    [Fact]
    public void HttpMethodConstantsAreTheExactUpperCaseTokens()
    {
        Assert.Equal("GET", Enums.HTTP_METHOD_GET, ignoreCase: false);
        Assert.Equal("POST", Enums.HTTP_METHOD_POST, ignoreCase: false);
        Assert.Equal("PUT", Enums.HTTP_METHOD_PUT, ignoreCase: false);
        Assert.Equal("DELETE", Enums.HTTP_METHOD_DELETE, ignoreCase: false);
        Assert.Equal("HEAD", Enums.HTTP_METHOD_HEAD, ignoreCase: false);

        // And they are the only strings in the catalogue, which is the census claim restated where a
        // reader looking for string constants will actually be.
        Assert.Equal(
            5,
            DeclaredConstants().Count(field => field.FieldType == typeof(string)));
    }

    /// <summary>
    /// The PowerScript-to-C# integer width mapping is load-bearing: 18 <c>Ulong</c> constants exceed
    /// what a 16-bit type could hold, and no <c>Uint</c> constant does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PowerScript <c>ulong</c> is 32-bit and PowerScript <c>uint</c> is 16-bit, which is the opposite
    /// of what the names suggest to a C# reader - so this is exactly the mapping a well-meant "tidy"
    /// would get backwards. The consequence is asymmetric and worth stating: mapping <c>Ulong</c> onto
    /// <c>ushort</c> would not compile, because 18 of those constants do not fit, whereas mapping
    /// <c>Uint</c> onto <c>uint</c> WOULD compile and would silently widen 34 constants the legacy
    /// stores in 16 bits. The second mistake is the dangerous one.
    /// </para>
    /// <para>
    /// Every value in the catalogue is also non-negative, which is why no signed-width question arises
    /// for the <c>Long</c> family at all.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheIntegerWidthMappingIsLoadBearing()
    {
        FieldInfo[] declared = DeclaredConstants();

        List<uint> wide = declared
            .Where(field => field.FieldType == typeof(uint))
            .Select(field => (uint)field.GetRawConstantValue()!)
            .Where(value => value > ushort.MaxValue)
            .ToList();

        Assert.Equal(18, wide.Count);
        Assert.Contains(uint.MaxValue, wide);

        // No 16-bit constant is anywhere near its own ceiling, so the narrower mapping is safe.
        Assert.All(
            declared.Where(field => field.FieldType == typeof(ushort)),
            field => Assert.True((ushort)field.GetRawConstantValue()! <= ushort.MaxValue));

        // Nothing in the catalogue is negative.
        Assert.All(
            declared.Where(field => field.FieldType == typeof(long)),
            field => Assert.True(
                (long)field.GetRawConstantValue()! >= 0L,
                $"{field.Name} is negative, which no enums.sru declaration is."));
    }
}
