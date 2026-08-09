// ==================================================================================================
// ParseErrorFormatter.cs
// ==================================================================================================
//
// WHAT THIS FILE IS
//   The error-reporting foundation of the Expressions/ folder, and the complete port of the two
//   `_of_buildparseerror` overloads plus the 28 dialog call sites of the legacy PowerBuilder object
//   `ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru` (2,435 lines, the largest
//   in-scope object in the refactor).
//
//   Two things live here and nothing else does:
//
//     1. THE RENDERER. `ParseErrorFormatter.BuildParseError`, a byte-exact reproduction of the
//        legacy formatter at [:L2402-L2404] and its delegating two-argument arity at [:L2406].
//
//     2. THE VOCABULARY. The structured error type that replaces the legacy `MessageBox`, the
//        severity and category discriminators, the caret-position conventions, and a catalogue of
//        all 28 legacy dialog sites with their verbatim message text, their substitution arguments
//        and their return codes.
//
//   The file has NO intra-folder dependency. `ColumnExpressionEngine.cs`,
//   `ExpressionVariableEnvironment.cs`, `ExpressionSession.cs`, `MacroInvoker.cs`,
//   `DataWindowExpressionEvaluator.cs` and `PinyinFirstLetterMatcher.cs` all raise errors THROUGH
//   the types defined here, so this file must be readable and buildable on its own.
//
// ==================================================================================================
// THE FORMATTING CONTRACT, TRANSCRIBED FROM THE ORACLE RATHER THAN PARAPHRASED
// ==================================================================================================
//
//   [n_cst_dwsvc_columnexp.sru:L2402-L2404]
//
//       private function string _of_buildparseerror (readonly string syntax, readonly long pos,
//                                                    string errinfo);
//         if errInfo <> "" then errInfo += "~n~n"
//         return errInfo + syntax + "~n" + Fill("_",LenA(Left(syntax,pos)) - 1)
//                + "^ (" + String(pos) + ")"
//       end function
//
//   [n_cst_dwsvc_columnexp.sru:L2406]
//
//       private function string _of_buildparseerror (readonly string syntax, readonly long pos);
//         return _of_BuildParseError(syntax,pos,"")
//       end function
//
//   Declared at [:L206] and [:L207] respectively, both `private function string`. Four observations
//   that the transcription makes and a paraphrase would lose:
//
//     * The separator is DOUBLED after a non-empty message and ABSENT after an empty one. `~n~n`
//       is appended to `errinfo` only inside the guard, so the two-argument arity - which passes
//       the empty string - emits the expression on the FIRST line, not the second.
//     * `errinfo` is the only parameter NOT declared `readonly`, and that is not an oversight: the
//       guard mutates it in place. The C# port keeps the mutation local, which is observably the
//       same and does not surprise a caller with an aliased argument.
//     * The pad is sized from `LenA`, the BYTE length, and NOT from `Len`. See THE LenA HAZARD.
//     * `Fill` receives `LenA(...) - 1`, which is NEGATIVE when `pos` is zero or less. PowerBuilder
//       yields the empty string there rather than raising. See DECISION 4.
//
// ==================================================================================================
// THE LenA HAZARD - THE SINGLE MOST LIKELY DEFECT IN THIS FILE
// ==================================================================================================
//
//   `LenA` is the length of a string IN BYTES under the host's ANSI/DBCS codepage. `Len` is the
//   length in characters. The legacy formatter uses `LenA` [:L2403], and every message in this
//   object is hardcoded Chinese, so the distinction is not academic:
//
//       expression      pos   Len(Left(exp,pos))-1   LenA(Left(exp,pos))-1   caret lands
//       -----------     ---   --------------------   ---------------------   -----------------
//       "abc$x"          4            3                       3              correct either way
//       "\u59D3\u540D$x" 3            2                       4              WRONG with Len
//
//   The caret was designed for a fixed-width console in which a Han character occupies TWO columns,
//   which is also exactly how many bytes it occupies in the DBCS codepage. Sizing the underscore run
//   from a UTF-16 character count would misplace the caret under EVERY Chinese expression the engine
//   ever reports on, and no compiler diagnostic and no analyzer covers it: both expressions are
//   well-typed and both produce a plausible-looking string.
//
//   `LenA` is therefore implemented here explicitly, per DECISION 3, and pinned by a test that
//   fails if it is ever swapped for a character count.
//
//   A CONSEQUENCE FOR CONSUMERS, stated because it is easy to get wrong on the far side of the wire:
//   `caret_position` on the published `dataservices.v1.ExpressionError` message is the legacy's
//   POSITION, not its column. A consumer that wants to draw its own marker must size it with these
//   same byte-width rules; the fully rendered legacy marker travels alongside in `rendered_marker`
//   precisely so a byte-exact characterization comparison never has to recompute anything.
//
// ==================================================================================================
// POSITIONS ARE INDICES INTO THE TRAILER-EXTENDED EXPRESSION, NOT INTO THE CALLER'S EXPRESSION
// ==================================================================================================
//
//   Before scanning, the legacy parser appends a sentinel to the expression:
//
//       constant string TRAILER = ";"                      [:L1251]
//       sExp = exp + TRAILER                               [:L1262]
//
//   Every one of the 11 caret-bearing sites passes `sExp` - the EXTENDED string - as the formatter's
//   `syntax` argument, and every reported position is an index into it. At the end of a successful
//   parse the sentinel is stripped again with `exp = Left(sExp,nLen - 1)` [:L1509].
//
//   So a caller that renders a caret against the ORIGINAL expression is off by the trailer, and a
//   position that points AT the sentinel exceeds the original string's length outright. This file
//   never strips and never adds the sentinel: it renders exactly the `syntax` it is handed, and
//   `ExpressionParseError.Expression` carries that same extended text so the position and the
//   string it indexes always travel together.
//
// ==================================================================================================
// THE PRESERVED LOCALIZATION INCONSISTENCY (CONSTRAINT C-B)
// ==================================================================================================
//
//   ALL 28 MESSAGES IN THIS OBJECT ARE HARDCODED CHINESE AND DO NOT ROUTE THROUGH LOCALIZATION.
//   Measured, not assumed: the object contains 28 `MessageBox` calls, ZERO of them commented out,
//   and ZERO calls to `I18N` of any kind.
//
//   That is in DELIBERATE CONTRAST with the equivalent messages elsewhere in the same DataWindow
//   service layer, WHICH DO localize - `se_cst_dw.sru:L357` and `:L368` route their validation-error
//   text through `I18N(ne_cst_i18n.CAT_DWSVC, ...)`, and `n_cst_dwsvc_rowselect.sru` and
//   `n_cst_dwsvc_contextmenu.sru` do the same.
//
//   The inconsistency is REPRODUCED, NOT HARMONIZED. Routing these 28 through the localization layer
//   would change observable output text, which is precisely the silent correction the mandate
//   forbids. Concretely, and this is the enforcement rather than the intention:
//
//     * THIS FILE HAS NO `using PowerFramework.Shared.Localization`, AND MUST NEVER ACQUIRE ONE.
//       The omission is deliberate. The DataServices project DOES reference that library - see the
//       `ProjectReference` in PowerFramework.DataServices.csproj, added for the 23 genuinely
//       localized call sites elsewhere in the service - so adding the import here would compile on
//       the first try and silently repair a defect the plan requires be kept. A test asserts the
//       absence.
//     * `ExpressionParseError.Localized` is hard-wired to `false` and
//       `ExpressionParseError.LocalizationCategory` to zero for every error this file produces. The
//       flag exists so a consumer can tell translatable text from untranslatable text WITHOUT ANY
//       MESSAGE CHANGING.
//
// ==================================================================================================
// DECISION LOG
// ==================================================================================================
//
//   DECISION 1 - BOTH ARITIES SHIP, AND THEY ARE NOT COLLAPSED INTO AN OPTIONAL PARAMETER.
//     The obvious C# shape is one method with `string? errinfo = null`. It is rejected, for a reason
//     stronger than fidelity to the legacy's two prototypes: THE TWO ARITIES PRODUCE THE TWO
//     DIFFERENT FIELDS THE PUBLISHED CONTRACT CARRIES. On
//     `shared/PowerFramework.Contracts/Proto/dataservices.v1.proto`, `ExpressionError.rendered_marker`
//     is documented as "the expression, underscore run, `^`, and the position in parentheses" - which
//     is exactly the TWO-argument output - while `StructuredError.text` is the message the legacy
//     dialog showed, which is the THREE-argument output. Every caret-bearing error therefore calls
//     both, and the delegation at [:L2406] is the guarantee that the two can never disagree about
//     the expression, the pad or the position.
//
//   DECISION 2 - THE FORMATTER TAKES A POSITION; IT DOES NOT COMPUTE ONE.
//     The legacy computes the position at the CALL SITE, in two different ways
//     (see `CaretPositionConvention`), from three parser locals - `nMacPos`, `nPos` and `nQuotePos`
//     [:L1241] - that only the scanner owns. Recomputing either formula here would require this file
//     to hold parser state, which is how a formatter turns into a second parser. The convention that
//     produced a position travels WITH it on `ExpressionParseError.CaretConvention`, so the choice is
//     recoverable from the error without this file ever performing the arithmetic.
//
//   DECISION 3 - `LenA` IS IMPLEMENTED FROM ITS RULE, NOT FROM A CODEPAGE TABLE.
//     `Encoding.GetEncoding(936)` is not reachable: the single-byte and DBCS codepages live in
//     `System.Text.Encoding.CodePages`, which is a package this refactor does not take (AAP 0.5.3
//     admits no dependency that is not required), and picking some other codepage would silently
//     change the answer. The rule is encoded directly instead: an ASCII code point is one byte and
//     every other code point is two. That is EXACT for ASCII and for the entire double-byte
//     repertoire of the Chinese ANSI codepage - which is 100% of what the oracle contains, namely
//     ASCII plus BMP Han - and its two known bounds are stated at the implementation rather than
//     hidden. See `LenA`.
//
//   DECISION 4 - THE OUT-OF-RANGE POSITION REPRODUCES THE LEGACY RESULT INSTEAD OF THROWING.
//     `Fill("_", LenA(Left(syntax,pos)) - 1)` is handed a NEGATIVE count whenever `pos` is zero or
//     less, because `Left` yields the empty string there and `0 - 1` is `-1`. PowerBuilder's `Fill`
//     answers the empty string for a non-positive count; it does not raise. Both behaviours are
//     reproduced exactly - `Left` and `Fill` are ported as private helpers with their PowerBuilder
//     semantics - so `pos = 0` renders `syntax`, a newline, no underscores at all, and `^ (0)`.
//     Throwing would be a behaviour change dressed up as robustness, and it would fire while the
//     engine is in the middle of REPORTING a fault, replacing a legacy diagnostic with a .NET stack
//     trace.
//
//   DECISION 5 - THE ARGUMENTS ARE `string?`, NOT `object?`.
//     Every substitution argument at all 28 sites is already a string in the legacy - a `String(...)`
//     conversion, a `.name`, an `.exp`, an `sErrInfo` or an `ex.text`. Typing them `string?` removes
//     the object-to-text conversion step entirely, which is what guarantees that the rendered
//     `Text` and the structured `FormatArguments` can never disagree: they are built from the same
//     values, and the published contract's `repeated string format_args` needs strings anyway.
//
//   DECISION 6 - NULL IS TREATED AS ABSENT, AND THE LEGACY'S NULL PROPAGATION IS NOT REPRODUCED.
//     In PowerScript, `null <> ""` evaluates to null rather than true, so a null `errinfo` would
//     skip the guard and then poison the concatenation, making the whole return value null. That
//     path is UNREACHABLE: all 11 caret-bearing sites pass a non-empty literal and the two-argument
//     arity passes `""` [:L2406], so no legacy call can produce it. Propagating it into a public C#
//     API would force every caller to null-check a state the legacy cannot reach - a branch the
//     legacy does not have, which is exactly what constraint C-B forbids adding. Null is therefore
//     normalised to the empty string at the boundary, and this file's return type is non-nullable.
//
//   DECISION 7 - THE ONLY EXCEPTIONS RAISED ARE FOR PROGRAMMING ERRORS, NEVER FOR DATA.
//     Two conditions throw: asking for a caret-bearing rendering of a plain site (or the reverse),
//     and supplying the wrong number of substitution arguments. Neither is reachable from data -
//     each of the 28 sites has a fixed family and a fixed argument count, both fixed by the
//     read-only oracle - so both are caller mistakes, and the 28-site parity matrix converts them
//     into build failures rather than runtime failures. Everything data-shaped degrades instead: a
//     null string, an out-of-range position, an unrepresentable character. That split is what keeps
//     an error reporter from becoming a second source of errors.
//
//   DECISION 8 - THE SITE IDENTIFIER'S VALUE IS THE LEGACY LINE NUMBER.
//     `ExpressionErrorSite.VariableMacroQuoteNotClosed` is `1505`. The plan requires every
//     behavioural assertion to be traceable to a `ws_objects/**` locator, and this makes the locator
//     machine-readable rather than a comment: `LegacyLine` is simply the enum value, uniqueness is
//     guaranteed because two dialogs cannot share a line, and a characterization recording that
//     names a site names a line in the oracle. It is safe from drift for a structural reason - the
//     legacy tree is read-only under constraint C-C, so the numbering cannot move.
//
//   DECISION 9 - THE ENUMS MIRROR THE PUBLISHED CONTRACT MEMBER FOR MEMBER AND VALUE FOR VALUE.
//     `ExpressionErrorSeverity` mirrors `dataservices.v1.Severity` and `ExpressionErrorCategory`
//     mirrors `dataservices.v1.ExpressionError.Category`, including the numeric values, so the gRPC
//     layer projects with a cast and no lookup table can rot. This file still does NOT reference the
//     generated types: the folder's error foundation depends on `PowerFramework.Shared.Kernel` and
//     the BCL only (constraints C-A and C-I), so it stays buildable and testable without the
//     protocol toolchain, and the domain model is not shaped by a wire format's defaults.
//
//   DECISION 10 - NO CATEGORY IS INVENTED, AND EVERY SITE GETS EXACTLY ONE.
//     The contract's category comments deliberately list some locators twice - `:L2192` appears under
//     both the preprocess and the undefined-name categories, `:L2282` and `:L2306` under both
//     preprocess and invalid-value, and `:L1888` under macro-unsupported although its text is
//     identical to the cache-creation text at `:L713`. Those lists are locator hints, not a
//     partition. The catalogue below assigns exactly one category per site, chosen from the
//     contract's own members with no additions, and every divergence from a hint is annotated at the
//     site with the evidence that settles it.
//
// ==================================================================================================
// BOUNDARIES THIS FILE HONOURS
// ==================================================================================================
//
//   C-A / C-I  Imports are `PowerFramework.Shared.Kernel` and the BCL. No other service's project,
//              no generated contract type, no package.
//   C-B        No message is improved, no category is invented, and the localization inconsistency
//              is preserved rather than repaired.
//   C-C        The oracle is read, never written. Every locator above points into a read-only tree.
//   C-D        No dialog, no UI type, no DPI conversion and no Win32 call appears anywhere here. A
//              structured result is the whole of the delivery mechanism.
//   C-F        No secret and no key material. The only literals are the legacy's own message text,
//              the underscore, the caret and the parentheses.
//   C-K        Every decision above is recorded with the evidence that drove it.
//
// ==================================================================================================

using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.DataServices.Expressions;

/// <summary>
/// The severity a legacy dialog was raised with, preserved because it is observable output.
/// </summary>
/// <remarks>
/// <para>
/// This is the PowerBuilder <c>Icon</c> domain. All 28 dialogs in
/// <c>n_cst_dwsvc_columnexp.sru</c> use <c>StopSign!</c> without exception, so
/// <see cref="StopSign"/> is the only value this file's own factories ever produce; the other four
/// named values exist because the domain has them and because the published
/// <c>dataservices.v1.Severity</c> enum declares them, which keeps the projection a cast rather
/// than a lookup (DECISION 9).
/// </para>
/// <para>
/// Only the DELIVERY CHANNEL changes in this port. The text, the substitution arguments, the
/// category, the caret position and this severity are all carried through unchanged, which is the
/// difference between reproducing the legacy's error ergonomics and merely reporting that something
/// failed.
/// </para>
/// </remarks>
public enum ExpressionErrorSeverity
{
    /// <summary>
    /// No severity was stated. Never produced by this file; present so that a default-initialised
    /// value is not silently mistaken for a real severity.
    /// </summary>
    Unspecified = 0,

    /// <summary>PowerBuilder <c>None!</c> - a dialog with no icon.</summary>
    None = 1,

    /// <summary>PowerBuilder <c>Information!</c>.</summary>
    Information = 2,

    /// <summary>PowerBuilder <c>Question!</c>.</summary>
    Question = 3,

    /// <summary>PowerBuilder <c>Exclamation!</c>.</summary>
    Exclamation = 4,

    /// <summary>
    /// PowerBuilder <c>StopSign!</c>. The severity of every one of the 28 legacy sites.
    /// </summary>
    StopSign = 5,
}

/// <summary>
/// What kind of failure an expression error reports, so a consumer can tell a macro parse failure
/// from a variable-definition failure from a cache failure from a preprocess failure.
/// </summary>
/// <remarks>
/// <para>
/// The member set MIRRORS <c>dataservices.v1.ExpressionError.Category</c> exactly, name for name and
/// value for value (DECISION 9), and nothing was added to it (DECISION 10 and constraint C-B). Each
/// member below names the legacy sites the catalogue assigns to it, so the assignment is adjudicable
/// against the oracle without reading the catalogue.
/// </para>
/// <para>
/// Two members carry no legacy site, and both deliberately. <see cref="Unspecified"/> guards the
/// default-initialised value. <see cref="ForeignReferenceBlocked"/> reports the ONE narrowing this
/// refactor makes to the legacy contract, which the legacy itself cannot produce - see its own
/// documentation.
/// </para>
/// </remarks>
public enum ExpressionErrorCategory
{
    /// <summary>
    /// No category was stated. Never produced by this file; present so that a default-initialised
    /// value is not silently mistaken for a real category.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// Creating the DataWindow compute object that caches an expression failed.
    /// [n_cst_dwsvc_columnexp.sru:L713, :L1888]
    /// </summary>
    CacheCreation = 1,

    /// <summary>
    /// Reading a cached expression's value raised a runtime exception, whose text the message
    /// embeds. [n_cst_dwsvc_columnexp.sru:L739]
    /// </summary>
    CacheError = 2,

    /// <summary>
    /// Updating an already-created expression cache failed.
    /// [n_cst_dwsvc_columnexp.sru:L1671]
    /// </summary>
    CacheUpdate = 3,

    /// <summary>
    /// A general expression failure: the DataWindow evaluator answered <c>"?"</c> or <c>"!"</c>.
    /// [n_cst_dwsvc_columnexp.sru:L762]
    /// </summary>
    Expression = 4,

    /// <summary>
    /// A macro parse failure. All 11 caret-bearing sites, and the only category that carries a
    /// caret position. [n_cst_dwsvc_columnexp.sru:L1308, :L1383, :L1390, :L1395, :L1399, :L1417,
    /// :L1422, :L1426, :L1431, :L1486, :L1505]
    /// </summary>
    Parse = 5,

    /// <summary>
    /// Expanding an expression's variables and macros before evaluation failed.
    /// [n_cst_dwsvc_columnexp.sru:L2192, :L2208, :L2232, :L2242, :L2254, :L2392]
    /// </summary>
    Preprocess = 6,

    /// <summary>
    /// A name that should have resolved did not. The one legacy site is the FOREIGN variable lookup.
    /// [n_cst_dwsvc_columnexp.sru:L2133]
    /// </summary>
    UndefinedName = 7,

    /// <summary>
    /// A value that should have been usable was not. The two legacy sites are both an invalid macro
    /// RETURN VALUE - a returned type that matched no arm of the conversion switch.
    /// [n_cst_dwsvc_columnexp.sru:L2282, :L2306]
    /// </summary>
    InvalidValue = 8,

    /// <summary>
    /// A macro form the engine does not support: caching was requested for an expression that
    /// contains a macro. [n_cst_dwsvc_columnexp.sru:L1882]
    /// </summary>
    MacroUnsupported = 9,

    /// <summary>
    /// A variable name was defined twice. [n_cst_dwsvc_columnexp.sru:L1607, :L2122]
    /// </summary>
    DuplicateDefinition = 10,

    /// <summary>
    /// A cross-DataWindow variable reference that spans expression sessions or service instances -
    /// THE ONE DEFINED ERROR OF THE ONE NARROWING.
    /// </summary>
    /// <remarks>
    /// It has NO legacy locator, and it cannot have one: in-process, the legacy holds a live object
    /// pointer to the other DataWindow's expression service (<c>foreignvardata.expsvc</c>
    /// [n_cst_dwsvc_columnexp.sru:L80-L83]) and the dereference always succeeds. A pointer cannot
    /// cross a network boundary, so co-resident references are resolved by a session-scoped handle
    /// and everything else is BLOCKED with this category rather than answered with a guess. This
    /// file defines the category; the narrowing itself is enforced by <c>ExpressionSession.cs</c>.
    /// </remarks>
    ForeignReferenceBlocked = 11,
}

/// <summary>
/// Which of the two shapes an expression error takes: a plain message, or a message with the
/// expression and a caret rendered beneath it.
/// </summary>
/// <remarks>
/// The distinction is not cosmetic. A caret-bearing error is one the legacy routed through
/// <c>_of_BuildParseError</c> [n_cst_dwsvc_columnexp.sru:L2402], and only those carry an expression,
/// a position, a position convention and a rendered marker. The split is exactly 11 caret-bearing to
/// 17 plain across the 28 sites.
/// </remarks>
public enum ExpressionErrorFamily
{
    /// <summary>
    /// No family was stated. Never produced by this file; present so that a default-initialised
    /// value is not silently mistaken for a real family.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// A message on its own, raised directly by <c>MessageBox</c> with no caret. 17 sites.
    /// [n_cst_dwsvc_columnexp.sru:L713, :L739, :L762, :L1607, :L1671, :L1882, :L1888, :L2122,
    /// :L2133, :L2192, :L2208, :L2232, :L2242, :L2254, :L2282, :L2306, :L2392]
    /// </summary>
    PlainMessage = 1,

    /// <summary>
    /// A message routed through <c>_of_BuildParseError</c>, carrying the expression text and a caret
    /// position. 11 sites. [n_cst_dwsvc_columnexp.sru:L1308, :L1383, :L1390, :L1395, :L1399, :L1417,
    /// :L1422, :L1426, :L1431, :L1486, :L1505]
    /// </summary>
    CaretBearingParseError = 2,
}

/// <summary>
/// How the caret position a caret-bearing error carries was arrived at.
/// </summary>
/// <remarks>
/// <para>
/// The legacy computes the position at the CALL SITE, and it uses two different formulas. This
/// enumeration names both so the choice travels with the error, WITHOUT this file performing either
/// computation: the inputs are parser locals declared at
/// <c>n_cst_dwsvc_columnexp.sru:L1241</c> - <c>nMacPos</c>, <c>nPos</c> and <c>nQuotePos</c> - which
/// only the scanner owns (DECISION 2).
/// </para>
/// <para>
/// Both conventions produce a ONE-BASED index into the TRAILER-EXTENDED expression, not into the
/// caller's original expression. See the note on trailer-relative positions in this file's header.
/// </para>
/// </remarks>
public enum CaretPositionConvention
{
    /// <summary>
    /// No convention applies, because the error carries no caret. Every plain-message site.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// The MIDPOINT OF THE OFFENDING MACRO TOKEN: <c>nMacPos + (nPos - nMacPos) / 2</c>, evaluated
    /// as truncating integer division. Ten of the eleven caret-bearing sites.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>nMacPos</c> is where the macro token started - it is set to the current scan position when
    /// the macro flag character is met [n_cst_dwsvc_columnexp.sru:L1295] - and <c>nPos</c> is where
    /// the scanner stood when the failure was detected. The formula therefore points into the MIDDLE
    /// of the offending token rather than at its first character.
    /// </para>
    /// <para>
    /// THAT LOOKS LIKE SLOPPINESS AND IT IS NOT A DEFECT TO FIX. It is the legacy's observable
    /// output: the caret column appears in every rendered message, so it appears in every
    /// characterization recording, and moving the caret to the token's start would fail every one of
    /// them. The division truncates rather than rounds, and the truncation is part of the contract -
    /// for a two-character token the caret lands on the first character, not the second.
    /// </para>
    /// <para>
    /// Sites: [n_cst_dwsvc_columnexp.sru:L1308, :L1383, :L1390, :L1395, :L1399, :L1417, :L1422,
    /// :L1426, :L1431, :L1486].
    /// </para>
    /// </remarks>
    MacroTokenMidpoint = 1,

    /// <summary>
    /// The position of the OPENING QUOTE: <c>nQuotePos</c>, recorded when the scanner entered the
    /// quoted run [n_cst_dwsvc_columnexp.sru:L1274]. The eleventh caret-bearing site, and the only
    /// one that is not a midpoint.
    /// </summary>
    /// <remarks>
    /// Used solely by the unclosed-quote failure at [n_cst_dwsvc_columnexp.sru:L1505], which is
    /// detected AFTER the scan loop has run off the end of the expression. A midpoint would be
    /// meaningless there, because there is no closing delimiter to take a midpoint against; the
    /// useful position is where the run that was never closed began.
    /// </remarks>
    OpeningQuote = 2,
}

/// <summary>
/// One of the 28 dialog call sites of <c>n_cst_dwsvc_columnexp.sru</c>, identified by the line it
/// occupies in the read-only oracle.
/// </summary>
/// <remarks>
/// <para>
/// THE VALUE OF EACH MEMBER IS ITS LEGACY LINE NUMBER (DECISION 8). That makes the locator
/// machine-readable rather than a comment, guarantees uniqueness - two dialogs cannot share a line -
/// and means a characterization recording that names a site names a line in the oracle.
/// <see cref="ExpressionErrorSiteDescriptor.LegacyLine"/> is simply this value, so the two can never
/// disagree. The numbering is safe from drift for a structural reason: the legacy tree is read-only
/// under constraint C-C.
/// </para>
/// <para>
/// The census is exact and was measured rather than estimated: 28 <c>MessageBox</c> calls, ZERO of
/// them commented out. There is therefore no dormant dialog path in this object to carry across -
/// unlike <c>se_cst_dw.sru</c>'s item-change handler, which does have a commented-out check.
/// <see cref="Unspecified"/> is a guard for the default-initialised value and is NOT one of the 28;
/// <see cref="ExpressionErrorCatalog.All"/> contains exactly the 28 real sites.
/// </para>
/// </remarks>
public enum ExpressionErrorSite
{
    /// <summary>
    /// No site. A guard for the default-initialised value; not one of the 28 legacy sites.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// Creating an expression's compute-object cache failed during a lazy first calculation.
    /// [n_cst_dwsvc_columnexp.sru:L713]
    /// </summary>
    CreateExpressionCacheFailed = 713,

    /// <summary>
    /// Reading a cached expression's value threw, and the caught exception's text is embedded in the
    /// message. [n_cst_dwsvc_columnexp.sru:L739]
    /// </summary>
    ExpressionCacheError = 739,

    /// <summary>
    /// The DataWindow evaluator answered <c>"?"</c> or <c>"!"</c> for a column expression.
    /// [n_cst_dwsvc_columnexp.sru:L762]
    /// </summary>
    ExpressionError = 762,

    /// <summary>
    /// A function macro referenced the calculation CONTEXT, which the engine does not support.
    /// [n_cst_dwsvc_columnexp.sru:L1308]
    /// </summary>
    FunctionMacroContextReferenceUnsupported = 1308,

    /// <summary>
    /// A function macro's argument list did not close, or left an argument dangling.
    /// [n_cst_dwsvc_columnexp.sru:L1383]
    /// </summary>
    FunctionMacroInvalidArgumentList = 1383,

    /// <summary>
    /// The variable-reference built-in macro was given other than exactly one argument. The message
    /// names the macro by its FULL name, including the macro flag characters.
    /// [n_cst_dwsvc_columnexp.sru:L1390]
    /// </summary>
    FunctionMacroArgumentCountIncorrectByFullName = 1390,

    /// <summary>
    /// The dynamic-invocation built-in macro was given no arguments at all. The message names the
    /// macro by its bare name. [n_cst_dwsvc_columnexp.sru:L1395]
    /// </summary>
    FunctionMacroArgumentCountIncorrectByName = 1395,

    /// <summary>
    /// A built-in function macro name matched neither of the two the engine defines.
    /// [n_cst_dwsvc_columnexp.sru:L1399]
    /// </summary>
    FunctionMacroUndefined = 1399,

    /// <summary>
    /// A context variable was written with the STATIC expansion form, which context variables do not
    /// support because their value is not known until calculation time.
    /// [n_cst_dwsvc_columnexp.sru:L1417]
    /// </summary>
    FunctionMacroContextVariableStaticExpansionUnsupported = 1417,

    /// <summary>
    /// A statically expanded variable macro named a variable that is not defined.
    /// [n_cst_dwsvc_columnexp.sru:L1422]
    /// </summary>
    VariableMacroUndefined = 1422,

    /// <summary>
    /// A FOREIGN variable was written with the STATIC expansion form, which foreign variables do not
    /// support - the legacy specification states that a foreign variable requires dynamic expansion.
    /// [n_cst_dwsvc_columnexp.sru:L1426]
    /// </summary>
    VariableMacroForeignStaticExpansionUnsupported = 1426,

    /// <summary>
    /// A statically expanded variable resolved to an EMPTY expression. This is the one parse arm that
    /// answers <c>RetCode.FAILED</c> rather than <c>RetCode.E_INVALID_ARGUMENT</c>.
    /// [n_cst_dwsvc_columnexp.sru:L1431]
    /// </summary>
    VariableMacroValueInvalid = 1431,

    /// <summary>
    /// A macro flag character was met but no name followed it.
    /// [n_cst_dwsvc_columnexp.sru:L1486]
    /// </summary>
    VariableMacroInvalidName = 1486,

    /// <summary>
    /// The scan reached the end of the expression with a quoted run still open. The ONE site whose
    /// caret position is the opening quote rather than a token midpoint.
    /// [n_cst_dwsvc_columnexp.sru:L1505]
    /// </summary>
    VariableMacroQuoteNotClosed = 1505,

    /// <summary>
    /// A LOCAL variable was added under a name that is already defined.
    /// [n_cst_dwsvc_columnexp.sru:L1607]
    /// </summary>
    DuplicateVariableDefinition = 1607,

    /// <summary>
    /// Rewriting an already-cached expression failed. This site reports and CONTINUES - it has no
    /// <c>return</c> at all. [n_cst_dwsvc_columnexp.sru:L1671]
    /// </summary>
    UpdateExpressionCacheFailed = 1671,

    /// <summary>
    /// Caching was requested for an expression that contains a macro, which cannot be cached because
    /// a compute object is evaluated by the DataWindow runtime and knows nothing of macros.
    /// [n_cst_dwsvc_columnexp.sru:L1882]
    /// </summary>
    CreateExpressionCacheMacroExpansionUnsupported = 1882,

    /// <summary>
    /// Creating the compute object failed while caching was being turned ON. Its message is IDENTICAL
    /// to the lazy-path message at :L713, and it likewise reports and continues.
    /// [n_cst_dwsvc_columnexp.sru:L1888]
    /// </summary>
    CreateExpressionCacheFailedOnCacheEnable = 1888,

    /// <summary>
    /// A FOREIGN variable was added under a name that is already defined. Its message is identical to
    /// the local-variable message at :L1607. [n_cst_dwsvc_columnexp.sru:L2122]
    /// </summary>
    DuplicateForeignVariableDefinition = 2122,

    /// <summary>
    /// A foreign variable was added, but the name does not exist in the OTHER DataWindow's expression
    /// service. [n_cst_dwsvc_columnexp.sru:L2133]
    /// </summary>
    ForeignVariableUndefined = 2133,

    /// <summary>
    /// Preprocessing met a dynamically expanded variable reference whose name is not defined.
    /// [n_cst_dwsvc_columnexp.sru:L2192]
    /// </summary>
    PreprocessVariableUndefined = 2192,

    /// <summary>
    /// Preprocessing resolved a dynamically expanded variable to an EMPTY expression.
    /// [n_cst_dwsvc_columnexp.sru:L2208]
    /// </summary>
    PreprocessVariableValueInvalid = 2208,

    /// <summary>
    /// Evaluating a macro ARGUMENT sub-expression answered <c>"?"</c> or <c>"!"</c>. The one message
    /// that reports both the whole expression and the offending sub-expression.
    /// [n_cst_dwsvc_columnexp.sru:L2232]
    /// </summary>
    PreprocessFunctionArgumentEvaluationFailed = 2232,

    /// <summary>
    /// The variable-reference built-in macro was handed a name that is not defined. The name arrives
    /// as the macro's FIRST EVALUATED ARGUMENT, so unlike :L2192 it is not known until calculation
    /// time. [n_cst_dwsvc_columnexp.sru:L2242]
    /// </summary>
    PreprocessMacroVariableUndefined = 2242,

    /// <summary>
    /// The variable-reference built-in macro resolved its first argument to an EMPTY expression.
    /// [n_cst_dwsvc_columnexp.sru:L2254]
    /// </summary>
    PreprocessMacroVariableValueInvalid = 2254,

    /// <summary>
    /// A DYNAMICALLY invoked macro returned a value whose runtime type matched no arm of the
    /// conversion switch. [n_cst_dwsvc_columnexp.sru:L2282]
    /// </summary>
    PreprocessMacroReturnValueInvalid = 2282,

    /// <summary>
    /// A DIRECTLY invoked macro returned a value whose runtime type matched no arm of the conversion
    /// switch. [n_cst_dwsvc_columnexp.sru:L2306]
    /// </summary>
    PreprocessFunctionReturnValueInvalid = 2306,

    /// <summary>
    /// Evaluating a VARIABLE's own expression answered <c>"?"</c> or <c>"!"</c>. The last dialog in
    /// the object. [n_cst_dwsvc_columnexp.sru:L2392]
    /// </summary>
    VariableExpressionError = 2392,
}

/// <summary>
/// Everything the oracle fixes about one of the 28 dialog sites: its family, its category, its
/// severity, its return code, its message template and its substitution arguments.
/// </summary>
/// <remarks>
/// <para>
/// A descriptor is the migrated form of a single legacy <c>MessageBox</c> STATEMENT. It holds only
/// what the source fixes at that line; the runtime values that fill the template arrive per call, so
/// the descriptor set is immutable and shared.
/// </para>
/// <para>
/// Its purpose is auditability as much as construction. Every field can be checked against the line
/// the <see cref="Site"/> names, and <see cref="LegacyArgumentSources"/> records the actual
/// PowerScript sub-expression that supplies each argument, so a reviewer can confirm the port
/// without holding the source open beside it.
/// </para>
/// </remarks>
public sealed record ExpressionErrorSiteDescriptor
{
    /// <summary>The legacy dialog site this descriptor migrates.</summary>
    public required ExpressionErrorSite Site { get; init; }

    /// <summary>
    /// Whether the site renders a caret beneath the expression, and therefore whether
    /// <see cref="CaretConvention"/> and the caret-bearing members of
    /// <see cref="ExpressionParseError"/> apply.
    /// </summary>
    public required ExpressionErrorFamily Family { get; init; }

    /// <summary>The kind of failure the site reports.</summary>
    public required ExpressionErrorCategory Category { get; init; }

    /// <summary>
    /// The severity the legacy dialog was raised with. <see cref="ExpressionErrorSeverity.StopSign"/>
    /// for all 28 sites, and stated per site rather than assumed globally so the claim stays
    /// checkable.
    /// </summary>
    public required ExpressionErrorSeverity Severity { get; init; }

    /// <summary>
    /// The <c>RetCode</c> value the enclosing legacy function returns immediately after raising the
    /// dialog, or <see langword="null"/> where it returns something that is not a return code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// NULL IS NOT "UNKNOWN", IT IS A MEASURED FACT ABOUT 13 OF THE 28 SITES. Three of them sit in a
    /// <c>boolean</c> function and answer <c>false</c> [:L713, :L739, :L762]; eight sit in a
    /// <c>string</c> function and answer the empty string [:L2192, :L2208, :L2232, :L2242, :L2254,
    /// :L2282, :L2306, :L2392]; and two report and CONTINUE with no <c>return</c> at all [:L1671,
    /// :L1888]. Substituting <c>RetCode.OK</c> for those would assert a success the legacy never
    /// claims, and substituting <c>RetCode.FAILED</c> would invent a code no recording contains.
    /// </para>
    /// <para>
    /// The 15 sites that DO carry one are not uniform either, and the spread is the reason this is a
    /// per-site field rather than a constant: <c>RetCode.E_INVALID_ARGUMENT</c> at ten of the eleven
    /// parse arms, <c>RetCode.FAILED</c> at the empty-variable-value parse arm [:L1431] and at both
    /// duplicate-definition arms [:L1607, :L2122], <c>RetCode.E_NO_SUPPORT</c> at the macro-caching
    /// arm [:L1882], and <c>RetCode.E_VAR_NOT_FOUND</c> at the foreign-variable arm [:L2133].
    /// </para>
    /// </remarks>
    public required long? ReturnCode { get; init; }

    /// <summary>
    /// The legacy message with each runtime value replaced by a one-based
    /// <c>PowerFramework.Shared.Kernel.Formatting.Sprintf</c> placeholder, and each <c>~n</c> escape
    /// replaced by the line feed it denotes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The literal fragments are the legacy's own text, VERBATIM AND UNTRANSLATED - see the preserved
    /// localization inconsistency in this file's header. The bracketed-name interpolation style is
    /// preserved with it, brackets and ASCII comma included, because that punctuation is observable
    /// output: the legacy writes <c>"[" + name + "]..."</c> and every character of it appears in a
    /// characterization recording.
    /// </para>
    /// <para>
    /// No template contains a literal <c>{</c>, <c>}</c> or <c>\</c>, which was verified across all
    /// 28 sites rather than assumed. That matters because those three are the only characters the
    /// format dialect treats specially, so no template needs escaping and none is escaped.
    /// </para>
    /// </remarks>
    public required string FormatTemplate { get; init; }

    /// <summary>
    /// The exact number of substitution arguments <see cref="FormatTemplate"/> requires: 0, 1, 2
    /// or 3.
    /// </summary>
    /// <remarks>
    /// Fixed by the oracle, because the legacy builds each message by concatenation and a
    /// concatenation cannot have a variable arity. Supplying a different count is a caller mistake
    /// and is rejected rather than rendered (DECISION 7).
    /// </remarks>
    public required int ArgumentCount { get; init; }

    /// <summary>
    /// How the caret position is arrived at for a caret-bearing site, or
    /// <see cref="CaretPositionConvention.Unspecified"/> for a plain-message site.
    /// </summary>
    public required CaretPositionConvention CaretConvention { get; init; }

    /// <summary>
    /// The ONE-BASED index within <see cref="ExpressionParseError.FormatArguments"/> of the argument
    /// that carries a CAUGHT RUNTIME EXCEPTION's text, or <see langword="null"/> where the site
    /// carries none.
    /// </summary>
    /// <remarks>
    /// Non-null for exactly one of the 28 sites: the cache-read failure at
    /// [n_cst_dwsvc_columnexp.sru:L739] embeds <c>ex.text</c> as its third argument. The published
    /// contract carries that text in its own field so a consumer can tell an engine diagnostic from a
    /// propagated runtime fault; recording the INDEX here, rather than copying the text into a second
    /// place, is what makes <see cref="ExpressionParseError.CaughtExceptionText"/> derivable and
    /// therefore incapable of disagreeing with the rendered message.
    /// </remarks>
    public required int? CaughtExceptionArgumentIndex { get; init; }

    /// <summary>
    /// The PowerScript sub-expression that supplies each substitution argument, in order, exactly as
    /// it is written in the oracle.
    /// </summary>
    /// <remarks>
    /// Documentation that a test can check rather than prose that rots. It is what distinguishes the
    /// four pairs of sites whose templates are byte-identical but whose arguments are not - the two
    /// argument-count failures at [:L1390] and [:L1395] differ only in that the first names the macro
    /// by <c>fnData.fullName</c> and the second by <c>fnData.name</c>, and the same pattern separates
    /// [:L1607] from [:L2122], [:L2192] from [:L2242], and [:L2282] from [:L2306].
    /// </remarks>
    public required ImmutableArray<string> LegacyArgumentSources { get; init; }

    /// <summary>
    /// The line in <c>ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru</c> that
    /// this descriptor migrates.
    /// </summary>
    /// <remarks>
    /// Derived from <see cref="Site"/> rather than stored beside it, so the locator and the identity
    /// cannot drift apart (DECISION 8). Zero only for
    /// <see cref="ExpressionErrorSite.Unspecified"/>, which no descriptor uses.
    /// </remarks>
    public int LegacyLine
    {
        get { return (int)Site; }
    }

    /// <summary>
    /// Compares two descriptors by VALUE, comparing <see cref="LegacyArgumentSources"/> element by
    /// element.
    /// </summary>
    /// <param name="other">The descriptor to compare against.</param>
    /// <returns>
    /// <see langword="true"/> when every recorded member is equal; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// WRITTEN OUT BECAUSE THE COMPILER'S VERSION IS WRONG FOR THIS TYPE.
    /// <see cref="ImmutableArray{T}"/> implements equality as REFERENCE equality of its underlying
    /// array, so the generated record <c>Equals</c> would report two descriptors with identical
    /// contents as different. A record advertises value semantics, so one that quietly lacks them is a
    /// trap rather than a nuance - and it is the kind of trap that makes a characterization comparison
    /// fail for a reason that has nothing to do with the behaviour being compared.
    /// </para>
    /// <para>
    /// <see cref="LegacyLine"/> is deliberately absent: it is derived from <see cref="Site"/>, so
    /// including it would compare the same fact twice. ADDING A MEMBER TO THIS RECORD MEANS ADDING IT
    /// HERE AND TO <see cref="GetHashCode"/>.
    /// </para>
    /// </remarks>
    public bool Equals(ExpressionErrorSiteDescriptor? other)
    {
        return other is not null
            && Site == other.Site
            && Family == other.Family
            && Category == other.Category
            && Severity == other.Severity
            && ReturnCode == other.ReturnCode
            && FormatTemplate == other.FormatTemplate
            && ArgumentCount == other.ArgumentCount
            && CaretConvention == other.CaretConvention
            && CaughtExceptionArgumentIndex == other.CaughtExceptionArgumentIndex
            && LegacyArgumentSources.AsSpan().SequenceEqual(other.LegacyArgumentSources.AsSpan());
    }

    /// <summary>
    /// Hashes the same members <see cref="Equals(ExpressionErrorSiteDescriptor)"/> compares.
    /// </summary>
    /// <returns>A hash code consistent with value equality.</returns>
    public override int GetHashCode()
    {
        HashCode hash = default;

        hash.Add(Site);
        hash.Add(Family);
        hash.Add(Category);
        hash.Add(Severity);
        hash.Add(ReturnCode);
        hash.Add(FormatTemplate, StringComparer.Ordinal);
        hash.Add(ArgumentCount);
        hash.Add(CaretConvention);
        hash.Add(CaughtExceptionArgumentIndex);

        foreach (string source in LegacyArgumentSources)
        {
            hash.Add(source, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// The 28 dialog sites of <c>n_cst_dwsvc_columnexp.sru</c>, migrated to structured descriptors.
/// </summary>
/// <remarks>
/// <para>
/// THE CENSUS IS EXACT AND WAS MEASURED: 28 <c>MessageBox</c> calls in the object, ZERO commented
/// out, ZERO localized. <see cref="All"/> therefore has exactly 28 entries and there is no dormant
/// dialog path left behind.
/// </para>
/// <para>
/// The set splits 11 caret-bearing to 17 plain, and the category assignment gives every site exactly
/// one category drawn from the published contract's own members with no additions (DECISION 10):
/// two cache-creation, one cache-error, one cache-update, one general expression, eleven parse, six
/// preprocess, one undefined-name, two invalid-value, one macro-unsupported and two
/// duplicate-definition. 2 + 1 + 1 + 1 + 11 + 6 + 1 + 2 + 1 + 2 = 28.
/// </para>
/// </remarks>
public static class ExpressionErrorCatalog
{
    /// <summary>
    /// The dialog title every one of the 28 sites uses: the legacy literal <c>"\u9519\u8BEF"</c>
    /// ("error").
    /// </summary>
    /// <remarks>
    /// Verified at all 28 call sites, not sampled at one. It is carried as the error's own title
    /// field rather than folded into the message text because the published contract keeps title and
    /// text apart, and because the legacy passed them as two separate <c>MessageBox</c> arguments.
    /// It is NOT localized - see the preserved localization inconsistency in this file's header.
    /// </remarks>
    public const string LegacyTitle = "错误";

    /// <summary>
    /// The relative path of the read-only oracle every locator in this file points into.
    /// </summary>
    /// <remarks>
    /// Present so that a characterization recording, a log record or a test failure can name the
    /// oracle without a reader having to know which of the 39 legacy libraries it lives in. There are
    /// two distinct files named <c>pfw.sra</c> in this repository, so a bare file name genuinely is
    /// ambiguous here; a full path never is.
    /// </remarks>
    public const string OracleSourcePath =
        "ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru";

    /// <summary>
    /// All 28 descriptors, in the order their sites appear in the oracle.
    /// </summary>
    /// <remarks>
    /// Oracle order rather than alphabetical or categorical order, so that reading this collection
    /// top to bottom is reading the legacy object top to bottom.
    /// </remarks>
    public static ImmutableArray<ExpressionErrorSiteDescriptor> All { get; } = BuildAll();

    /// <summary>
    /// <see cref="All"/> indexed by site, for O(1) lookup on the engine's error paths.
    /// </summary>
    private static readonly FrozenDictionary<ExpressionErrorSite, ExpressionErrorSiteDescriptor>
        SiteIndex = BuildIndex(All);

    /// <summary>
    /// Returns the descriptor for <paramref name="site"/>.
    /// </summary>
    /// <param name="site">One of the 28 legacy dialog sites.</param>
    /// <returns>The descriptor the oracle fixes for that site.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="site"/> is not one of the 28 - which includes
    /// <see cref="ExpressionErrorSite.Unspecified"/>.
    /// </exception>
    /// <remarks>
    /// This throws by design, and it is one of the two programming-error exceptions this file admits
    /// (DECISION 7): the site set is closed by a read-only source, so an unknown site is a caller
    /// mistake rather than a data condition. Use <see cref="TryGet"/> where a miss is a legitimate
    /// answer.
    /// </remarks>
    public static ExpressionErrorSiteDescriptor Get(ExpressionErrorSite site)
    {
        if (!SiteIndex.TryGetValue(site, out ExpressionErrorSiteDescriptor? descriptor))
        {
            throw new ArgumentOutOfRangeException(
                nameof(site),
                site,
                "There is no such dialog site in " + OracleSourcePath
                    + ". The 28 sites are fixed by the read-only oracle; "
                    + nameof(ExpressionErrorSite.Unspecified) + " is not one of them.");
        }

        return descriptor;
    }

    /// <summary>
    /// Returns the descriptor for <paramref name="site"/> without throwing when there is none.
    /// </summary>
    /// <param name="site">A candidate site.</param>
    /// <param name="descriptor">
    /// The descriptor when the return value is <see langword="true"/>; otherwise
    /// <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="site"/> is one of the 28; otherwise
    /// <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// The <see cref="NotNullWhenAttribute"/> is load-bearing rather than decorative: without it the
    /// nullable analysis cannot see that a <see langword="true"/> result guarantees a non-null
    /// <paramref name="descriptor"/>, and every caller that short-circuits on the result - including
    /// <see cref="ExpressionParseError.CaughtExceptionText"/> in this file - would need a redundant
    /// null check to satisfy the promoted warnings.
    /// </remarks>
    public static bool TryGet(
        ExpressionErrorSite site,
        [NotNullWhen(true)] out ExpressionErrorSiteDescriptor? descriptor)
    {
        return SiteIndex.TryGetValue(site, out descriptor);
    }

    /// <summary>
    /// Builds a descriptor for a PLAIN-MESSAGE site - one the legacy raised directly, with no caret.
    /// </summary>
    /// <param name="site">The legacy dialog site.</param>
    /// <param name="category">The failure kind, chosen from the published contract's members.</param>
    /// <param name="severity">The severity the legacy dialog used.</param>
    /// <param name="returnCode">
    /// The <c>RetCode</c> the enclosing function returns immediately afterwards, or
    /// <see langword="null"/> where it returns <c>false</c>, the empty string, or nothing at all.
    /// </param>
    /// <param name="caughtExceptionArgumentIndex">
    /// The one-based argument index carrying a caught exception's text, or <see langword="null"/>.
    /// </param>
    /// <param name="formatTemplate">The legacy message with one-based placeholders.</param>
    /// <param name="legacyArgumentSources">
    /// The PowerScript sub-expression supplying each argument, in order.
    /// </param>
    /// <returns>The validated descriptor.</returns>
    /// <remarks>
    /// The severity is a parameter rather than a constant even though all 28 sites agree on it, so
    /// that the claim "every site uses the stop sign" is stated 28 times and can be checked 28 times
    /// instead of being asserted once in a comment.
    /// </remarks>
    private static ExpressionErrorSiteDescriptor Plain(
        ExpressionErrorSite site,
        ExpressionErrorCategory category,
        ExpressionErrorSeverity severity,
        long? returnCode,
        int? caughtExceptionArgumentIndex,
        string formatTemplate,
        params string[] legacyArgumentSources)
    {
        return Validate(new ExpressionErrorSiteDescriptor
        {
            Site = site,
            Family = ExpressionErrorFamily.PlainMessage,
            Category = category,
            Severity = severity,
            ReturnCode = returnCode,
            FormatTemplate = formatTemplate,
            ArgumentCount = legacyArgumentSources.Length,
            CaretConvention = CaretPositionConvention.Unspecified,
            CaughtExceptionArgumentIndex = caughtExceptionArgumentIndex,
            LegacyArgumentSources = [.. legacyArgumentSources],
        });
    }

    /// <summary>
    /// Builds a descriptor for a CARET-BEARING site - one the legacy routed through
    /// <c>_of_BuildParseError</c> [n_cst_dwsvc_columnexp.sru:L2402].
    /// </summary>
    /// <param name="site">The legacy dialog site.</param>
    /// <param name="category">
    /// The failure kind. <see cref="ExpressionErrorCategory.Parse"/> for all eleven, because all
    /// eleven sit inside the macro scanner.
    /// </param>
    /// <param name="severity">The severity the legacy dialog used.</param>
    /// <param name="returnCode">The <c>RetCode</c> the scanner returns immediately afterwards.</param>
    /// <param name="caretConvention">Which formula produced the position the site reports.</param>
    /// <param name="formatTemplate">
    /// The legacy message with one-based placeholders. For a caret-bearing site this is the
    /// formatter's <c>errinfo</c> argument, NOT the whole rendered message.
    /// </param>
    /// <param name="legacyArgumentSources">
    /// The PowerScript sub-expression supplying each argument, in order.
    /// </param>
    /// <returns>The validated descriptor.</returns>
    /// <remarks>
    /// No caret-bearing site carries a caught exception, so
    /// <see cref="ExpressionErrorSiteDescriptor.CaughtExceptionArgumentIndex"/> is fixed to
    /// <see langword="null"/> here rather than offered as a parameter - the one site that does carry
    /// one is a plain message [:L739].
    /// </remarks>
    private static ExpressionErrorSiteDescriptor Caret(
        ExpressionErrorSite site,
        ExpressionErrorCategory category,
        ExpressionErrorSeverity severity,
        long? returnCode,
        CaretPositionConvention caretConvention,
        string formatTemplate,
        params string[] legacyArgumentSources)
    {
        return Validate(new ExpressionErrorSiteDescriptor
        {
            Site = site,
            Family = ExpressionErrorFamily.CaretBearingParseError,
            Category = category,
            Severity = severity,
            ReturnCode = returnCode,
            FormatTemplate = formatTemplate,
            ArgumentCount = legacyArgumentSources.Length,
            CaretConvention = caretConvention,
            CaughtExceptionArgumentIndex = null,
            LegacyArgumentSources = [.. legacyArgumentSources],
        });
    }

    /// <summary>
    /// Checks the structural invariants of one descriptor and returns it unchanged.
    /// </summary>
    /// <param name="descriptor">The descriptor to check.</param>
    /// <returns><paramref name="descriptor"/>, when every invariant holds.</returns>
    /// <exception cref="InvalidOperationException">Any invariant is violated.</exception>
    /// <remarks>
    /// <para>
    /// FAIL FAST ON A STRUCTURAL FAULT, which is the posture the migration plan requires and which
    /// the legacy itself takes - a decoded assertion failure there unpacks its payload and then halts
    /// [ws_objects/pfw.pbl.src/pfw.sra:L111-L144]. The 28 templates are hand-transcribed from a
    /// read-only source, so a placeholder that does not line up with its argument list is the single
    /// most likely transcription slip in this file, and it would otherwise surface as a message with
    /// an unsubstituted <c>{2}</c> sitting in a characterization recording.
    /// </para>
    /// <para>
    /// Every condition below is a coding error and none is reachable from data (DECISION 7), so this
    /// runs once per descriptor while the type initialises and never on an error path.
    /// </para>
    /// </remarks>
    private static ExpressionErrorSiteDescriptor Validate(ExpressionErrorSiteDescriptor descriptor)
    {
        if (descriptor.Site == ExpressionErrorSite.Unspecified)
        {
            throw new InvalidOperationException(
                "A descriptor must name one of the 28 dialog sites in " + OracleSourcePath + ".");
        }

        if (descriptor.FormatTemplate.Length == 0)
        {
            throw new InvalidOperationException(
                "The message template for site " + descriptor.Site + " is empty, but every one of "
                    + "the 28 legacy sites raises non-empty text.");
        }

        // The dialect's only special characters. Verified absent across all 28 legacy messages, so a
        // template that grew one would silently change how the rest of it renders.
        if (descriptor.FormatTemplate.Contains('\\', StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The message template for site " + descriptor.Site + " contains a backslash, which "
                    + "the format dialect treats as an escape. No legacy message contains one.");
        }

        // Exactly {1}..{ArgumentCount} must appear, and nothing beyond. This is what ties the
        // transcribed template to the transcribed argument list.
        for (int index = 1; index <= descriptor.ArgumentCount; index++)
        {
            string placeholder = "{" + index.ToString(CultureInfo.InvariantCulture) + "}";
            if (!descriptor.FormatTemplate.Contains(placeholder, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The message template for site " + descriptor.Site + " declares "
                        + descriptor.ArgumentCount.ToString(CultureInfo.InvariantCulture)
                        + " argument(s) but does not use " + placeholder + ".");
            }
        }

        string unexpected = "{"
            + (descriptor.ArgumentCount + 1).ToString(CultureInfo.InvariantCulture) + "}";
        if (descriptor.FormatTemplate.Contains(unexpected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The message template for site " + descriptor.Site + " uses " + unexpected
                    + " but only "
                    + descriptor.ArgumentCount.ToString(CultureInfo.InvariantCulture)
                    + " argument source(s) are recorded for it.");
        }

        if (descriptor.CaughtExceptionArgumentIndex is int exceptionIndex
            && (exceptionIndex < 1 || exceptionIndex > descriptor.ArgumentCount))
        {
            throw new InvalidOperationException(
                "Site " + descriptor.Site + " points its caught-exception argument at index "
                    + exceptionIndex.ToString(CultureInfo.InvariantCulture)
                    + ", which is outside its argument list.");
        }

        bool caretBearing = descriptor.Family == ExpressionErrorFamily.CaretBearingParseError;
        bool hasConvention = descriptor.CaretConvention != CaretPositionConvention.Unspecified;
        if (caretBearing != hasConvention)
        {
            throw new InvalidOperationException(
                "Site " + descriptor.Site + " disagrees with itself: a caret-bearing site must name "
                    + "a caret-position convention and a plain-message site must not.");
        }

        return descriptor;
    }

    /// <summary>
    /// Indexes the descriptors by site, rejecting a duplicate.
    /// </summary>
    /// <param name="descriptors">The descriptor set to index.</param>
    /// <returns>A frozen lookup from site to descriptor.</returns>
    /// <exception cref="InvalidOperationException">Two descriptors name the same site.</exception>
    private static FrozenDictionary<ExpressionErrorSite, ExpressionErrorSiteDescriptor> BuildIndex(
        ImmutableArray<ExpressionErrorSiteDescriptor> descriptors)
    {
        Dictionary<ExpressionErrorSite, ExpressionErrorSiteDescriptor> index =
            new(descriptors.Length);

        foreach (ExpressionErrorSiteDescriptor descriptor in descriptors)
        {
            if (!index.TryAdd(descriptor.Site, descriptor))
            {
                throw new InvalidOperationException(
                    "Site " + descriptor.Site + " is described twice. Each of the 28 sites occupies "
                        + "one line of " + OracleSourcePath + ", so each may be described once.");
            }
        }

        return index.ToFrozenDictionary();
    }

    /// <summary>
    /// Transcribes all 28 dialog sites, in the order they appear in the oracle.
    /// </summary>
    /// <returns>The 28 validated descriptors.</returns>
    /// <remarks>
    /// <para>
    /// Each entry is annotated with its oracle line and, where it is not obvious from the site name,
    /// the evidence that settles its category and its return code. The Chinese text is the legacy's
    /// own and is NOT translated, NOT normalised and NOT routed through localization - see the
    /// preserved localization inconsistency in this file's header. Every <c>~n</c> in the source is a
    /// line feed here, and every ASCII bracket, comma, colon and exclamation mark is the legacy's,
    /// verified code point by code point rather than assumed.
    /// </para>
    /// <para>
    /// FOUR PAIRS OF SITES HAVE BYTE-IDENTICAL TEMPLATES and are still distinct sites, which is why
    /// <see cref="ExpressionErrorSiteDescriptor.LegacyArgumentSources"/> exists: [:L1390] and
    /// [:L1395], [:L1607] and [:L2122], [:L2192] and [:L2242], and [:L2282] and [:L2306]. Collapsing
    /// any pair would lose the locator that tells a reader which branch of the engine reported.
    /// </para>
    /// </remarks>
    private static ImmutableArray<ExpressionErrorSiteDescriptor> BuildAll()
    {
        return
        [
            // ------------------------------------------------------------------------------------
            //  THE LAZY CALCULATION PATH
            // ------------------------------------------------------------------------------------

            // [:L713] `if Not _of_CreateCompute(...) then` ... `return false`. Enclosing function is
            // boolean, hence no return code.
            Plain(
                ExpressionErrorSite.CreateExpressionCacheFailed,
                ExpressionErrorCategory.CacheCreation,
                ExpressionErrorSeverity.StopSign,
                returnCode: null,
                caughtExceptionArgumentIndex: null,
                "[{1}]创建表达式缓存失败:\nExpression:{2}\nError:{3}",
                "String(ColExpDatas[index].name)",
                "ColExpDatas[index].exp",
                "sErrInfo"),

            // [:L739] Inside `catch(throwable ex2)`, and the message embeds `ex.text` - the OUTER
            // exception, not `ex2`. That is the legacy's own text and is reproduced as written; the
            // third argument is therefore the caught-exception text the contract carries separately.
            Plain(
                ExpressionErrorSite.ExpressionCacheError,
                ExpressionErrorCategory.CacheError,
                ExpressionErrorSeverity.StopSign,
                returnCode: null,
                caughtExceptionArgumentIndex: 3,
                "[{1}]表达式缓存错误:\nExpression:{2}\nError:{3}",
                "String(ColExpDatas[index].name)",
                "ColExpDatas[index].exp",
                "ex.text"),

            // [:L762] Raised when the DataWindow evaluator answers `"?"` or `"!"`, the runtime's two
            // error sentinels. Reported AFTER the trace event has already fired [:L757].
            Plain(
                ExpressionErrorSite.ExpressionError,
                ExpressionErrorCategory.Expression,
                ExpressionErrorSeverity.StopSign,
                returnCode: null,
                caughtExceptionArgumentIndex: null,
                "[{1}]表达式错误:\nExpression:{2}",
                "String(ColExpDatas[index].name)",
                "sExp"),

            // ------------------------------------------------------------------------------------
            //  THE MACRO SCANNER - ALL ELEVEN CARET-BEARING SITES
            //  Positions are indices into the TRAILER-EXTENDED expression `sExp = exp + ";"`
            //  [:L1251, :L1262].
            // ------------------------------------------------------------------------------------

            // [:L1308] A function macro written with the context flag. Deliberate legacy restriction,
            // not a gap: a context reference is resolved against the CALLING DataWindow's row, which a
            // function macro's argument list has no way to carry.
            Caret(
                ExpressionErrorSite.FunctionMacroContextReferenceUnsupported,
                ExpressionErrorCategory.Parse,
                ExpressionErrorSeverity.StopSign,
                RetCode.E_INVALID_ARGUMENT,
                CaretPositionConvention.MacroTokenMidpoint,
                "解析函数宏失败!\n不支持引用上下文的函数宏"),

            // [:L1383] `if bFnQuoted or nFnBrCnt <> -1 or nFnArgPos <> 0 then` - an unterminated
            // quote inside the arguments, unbalanced brackets, or a trailing argument with no comma.
            Caret(
                ExpressionErrorSite.FunctionMacroInvalidArgumentList,
                ExpressionErrorCategory.Parse,
                ExpressionErrorSeverity.StopSign,
                RetCode.E_INVALID_ARGUMENT,
                CaretPositionConvention.MacroTokenMidpoint,
                "解析函数宏失败!\n无效的参数列表"),

            // [:L1390] The variable-reference built-in takes exactly one argument. Names the macro by
            // `fullName`, which INCLUDES the macro flag characters - the one difference from :L1395.
            Caret(
                ExpressionErrorSite.FunctionMacroArgumentCountIncorrectByFullName,
                ExpressionErrorCategory.Parse,
                ExpressionErrorSeverity.StopSign,
                RetCode.E_INVALID_ARGUMENT,
                CaretPositionConvention.MacroTokenMidpoint,
                "解析函数宏失败!\n函数[{1}],参数数量不正确",
                "fnData.fullName"),

            // [:L1395] The dynamic-invocation built-in takes at least one argument. Byte-identical
            // template to :L1390; names the macro by its BARE `name`.
            Caret(
                ExpressionErrorSite.FunctionMacroArgumentCountIncorrectByName,
                ExpressionErrorCategory.Parse,
                ExpressionErrorSeverity.StopSign,
                RetCode.E_INVALID_ARGUMENT,
                CaretPositionConvention.MacroTokenMidpoint,
                "解析函数宏失败!\n函数[{1}],参数数量不正确",
                "fnData.name"),

            // [:L1399] The `case else` arm of the built-in switch: the engine defines exactly two
            // built-in macros and this name is neither.
            Caret(
                ExpressionErrorSite.FunctionMacroUndefined,
                ExpressionErrorCategory.Parse,
                ExpressionErrorSeverity.StopSign,
                RetCode.E_INVALID_ARGUMENT,
                CaretPositionConvention.MacroTokenMidpoint,
                "解析函数宏失败!\n函数[{1}],未定义",
                "fnData.name"),

            // [:L1417] Static expansion substitutes at BIND time, and a context variable has no value
            // then - it is resolved against the row being calculated. Note the legacy says "function
            // macro" here although the failing construct is a variable; that wording is reproduced.
            Caret(
                ExpressionErrorSite.FunctionMacroContextVariableStaticExpansionUnsupported,
                ExpressionErrorCategory.Parse,
                ExpressionErrorSeverity.StopSign,
                RetCode.E_INVALID_ARGUMENT,
                CaretPositionConvention.MacroTokenMidpoint,
                "解析函数宏失败!\n上下文变量[{1}],不支持静态展开",
                "varData.name"),

            // [:L1422] `nVarIdx = _of_FindVarIndex(varData.name)` answered zero.
            Caret(
                ExpressionErrorSite.VariableMacroUndefined,
                ExpressionErrorCategory.Parse,
                ExpressionErrorSeverity.StopSign,
                RetCode.E_INVALID_ARGUMENT,
                CaretPositionConvention.MacroTokenMidpoint,
                "解析变量宏失败!\n变量[{1}],未定义",
                "varData.name"),

            // [:L1426] A foreign variable's value lives behind a live pointer to another DataWindow's
            // expression service, so it cannot be substituted at bind time. The legacy specification
            // states the same rule in words: a foreign variable requires dynamic expansion.
            Caret(
                ExpressionErrorSite.VariableMacroForeignStaticExpansionUnsupported,
                ExpressionErrorCategory.Parse,
                ExpressionErrorSeverity.StopSign,
                RetCode.E_INVALID_ARGUMENT,
                CaretPositionConvention.MacroTokenMidpoint,
                "解析变量宏失败!\n外部变量[{1}],不支持静态展开",
                "varData.name"),

            // [:L1431] THE ONE PARSE ARM THAT ANSWERS `RetCode.FAILED`. Every other arm in the scanner
            // answers `RetCode.E_INVALID_ARGUMENT`; this one is reached when the variable RESOLVES but
            // its expression is empty, which is a state rather than a malformed argument. The
            // distinction is preserved exactly.
            Caret(
                ExpressionErrorSite.VariableMacroValueInvalid,
                ExpressionErrorCategory.Parse,
                ExpressionErrorSeverity.StopSign,
                RetCode.FAILED,
                CaretPositionConvention.MacroTokenMidpoint,
                "解析变量宏失败!\n变量[{1}],值无效",
                "varData.name"),

            // [:L1486] The `else` of `if nPos - nMacPos - 1 > 0 then` - a macro flag with nothing
            // after it.
            Caret(
                ExpressionErrorSite.VariableMacroInvalidName,
                ExpressionErrorCategory.Parse,
                ExpressionErrorSeverity.StopSign,
                RetCode.E_INVALID_ARGUMENT,
                CaretPositionConvention.MacroTokenMidpoint,
                "解析变量宏失败!\n无效的名称"),

            // [:L1505] Raised AFTER the scan loop ends, from `if bQuoted then`. THE ONLY SITE WHOSE
            // POSITION IS NOT A MIDPOINT: it reports `nQuotePos`, the opening quote recorded at
            // [:L1274], because there is no closing delimiter to take a midpoint against.
            Caret(
                ExpressionErrorSite.VariableMacroQuoteNotClosed,
                ExpressionErrorCategory.Parse,
                ExpressionErrorSeverity.StopSign,
                RetCode.E_INVALID_ARGUMENT,
                CaretPositionConvention.OpeningQuote,
                "解析变量宏失败!\n引号未关闭"),

            // ------------------------------------------------------------------------------------
            //  VARIABLE AND CACHE ADMINISTRATION
            // ------------------------------------------------------------------------------------

            // [:L1607] Adding a LOCAL variable whose name is taken. Answers `RetCode.FAILED`, not
            // `E_INVALID_ARGUMENT` - the argument is well formed, the state rejects it.
            Plain(
                ExpressionErrorSite.DuplicateVariableDefinition,
                ExpressionErrorCategory.DuplicateDefinition,
                ExpressionErrorSeverity.StopSign,
                RetCode.FAILED,
                caughtExceptionArgumentIndex: null,
                "变量[{1}],重复定义",
                "name"),

            // [:L1671] `if sErrInfo <> "" then` and then NOTHING - no `return` follows, so the
            // function carries on with a cache it has just failed to update. Reported as a null return
            // code because there is no code to report, not because the code is unknown.
            Plain(
                ExpressionErrorSite.UpdateExpressionCacheFailed,
                ExpressionErrorCategory.CacheUpdate,
                ExpressionErrorSeverity.StopSign,
                returnCode: null,
                caughtExceptionArgumentIndex: null,
                "[{1}]更新表达式缓存失败:\nExpression:{2}\nError:{3}",
                "String(ColExpDatas[index].name)",
                "exp",
                "sErrInfo"),

            // [:L1882] Caching is implemented as a DataWindow compute object, which the runtime
            // evaluates and which therefore cannot see a macro. THE ONLY SITE ANSWERING
            // `RetCode.E_NO_SUPPORT`.
            Plain(
                ExpressionErrorSite.CreateExpressionCacheMacroExpansionUnsupported,
                ExpressionErrorCategory.MacroUnsupported,
                ExpressionErrorSeverity.StopSign,
                RetCode.E_NO_SUPPORT,
                caughtExceptionArgumentIndex: null,
                "[{1}]创建表达式缓存失败,不支持宏扩展",
                "String(ColExpDatas[index].name)"),

            // [:L1888] Compute-object creation failed while caching was being switched ON, and again
            // no `return` follows. CATEGORY NOTE: the published contract's macro-unsupported category
            // lists this locator alongside :L1882 because the two share a function, but the site's own
            // text is byte-identical to the cache-creation text at :L713 and the two failures are the
            // same failure. It is categorised as cache creation on that evidence; the contract's
            // locator lists are hints and already double-list :L2192, :L2282 and :L2306 (DECISION 10).
            Plain(
                ExpressionErrorSite.CreateExpressionCacheFailedOnCacheEnable,
                ExpressionErrorCategory.CacheCreation,
                ExpressionErrorSeverity.StopSign,
                returnCode: null,
                caughtExceptionArgumentIndex: null,
                "[{1}]创建表达式缓存失败:\nExpression:{2}\nError:{3}",
                "String(ColExpDatas[index].name)",
                "ColExpDatas[index].exp",
                "sErrInfo"),

            // [:L2122] Adding a FOREIGN variable whose name is taken. Byte-identical template to
            // :L1607 and the same `RetCode.FAILED`; a different entry point, hence a different site.
            Plain(
                ExpressionErrorSite.DuplicateForeignVariableDefinition,
                ExpressionErrorCategory.DuplicateDefinition,
                ExpressionErrorSeverity.StopSign,
                RetCode.FAILED,
                caughtExceptionArgumentIndex: null,
                "变量[{1}],重复定义",
                "name"),

            // [:L2133] `varData.foreign.index = expSvc._of_FindVarIndex(name)` came back non-positive:
            // the name does not exist in the OTHER DataWindow's service. THE ONLY SITE ANSWERING
            // `RetCode.E_VAR_NOT_FOUND`, and the one legacy site in the undefined-name category.
            Plain(
                ExpressionErrorSite.ForeignVariableUndefined,
                ExpressionErrorCategory.UndefinedName,
                ExpressionErrorSeverity.StopSign,
                RetCode.E_VAR_NOT_FOUND,
                caughtExceptionArgumentIndex: null,
                "外部变量[{1}],未定义",
                "name"),

            // ------------------------------------------------------------------------------------
            //  THE PREPROCESSOR - EIGHT SITES, EVERY ONE ANSWERING THE EMPTY STRING
            //  `_of_PreprocessExp` returns a string, so its failure value is `""` and no return code
            //  is produced at any of these eight lines.
            // ------------------------------------------------------------------------------------

            // [:L2192] A dynamically expanded reference whose name is not defined. CATEGORY NOTE: the
            // contract lists this locator under undefined-name as well as preprocess. It is
            // categorised as preprocess so that all eight preprocessor sites share one category and
            // the undefined-name category keeps the meaning its own text gives it at :L2133 - a
            // FOREIGN variable that does not exist (DECISION 10).
            Plain(
                ExpressionErrorSite.PreprocessVariableUndefined,
                ExpressionErrorCategory.Preprocess,
                ExpressionErrorSeverity.StopSign,
                returnCode: null,
                caughtExceptionArgumentIndex: null,
                "预处理表达式发生错误!\n[{1}]表达式错误:\n变量[{2}],未定义",
                "String(dwo.name)",
                "vars[nVarIdx].name"),

            // [:L2208] The variable resolved but its expression is empty.
            Plain(
                ExpressionErrorSite.PreprocessVariableValueInvalid,
                ExpressionErrorCategory.Preprocess,
                ExpressionErrorSeverity.StopSign,
                returnCode: null,
                caughtExceptionArgumentIndex: null,
                "预处理表达式发生错误!\n[{1}]表达式错误:\n变量[{2}],值无效",
                "String(dwo.name)",
                "vars[nVarIdx].name"),

            // [:L2232] Evaluating a macro ARGUMENT answered `"!"` or `"?"`. The only message that
            // reports both the whole expression and the offending sub-expression, and note the ASCII
            // space in the literal `Sub Expression:`.
            Plain(
                ExpressionErrorSite.PreprocessFunctionArgumentEvaluationFailed,
                ExpressionErrorCategory.Preprocess,
                ExpressionErrorSeverity.StopSign,
                returnCode: null,
                caughtExceptionArgumentIndex: null,
                "预处理表达式发生错误!\n[{1}]表达式错误:\nExpression:{2}\nSub Expression:{3}",
                "String(dwo.name)",
                "fns[nFnIdx].fullName",
                "fns[nFnIdx].args[nFnArgIdx]"),

            // [:L2242] The variable-reference built-in was handed a name that is not defined.
            // Byte-identical template to :L2192, but the name arrives as the macro's first EVALUATED
            // argument, so this failure cannot be detected until calculation time.
            Plain(
                ExpressionErrorSite.PreprocessMacroVariableUndefined,
                ExpressionErrorCategory.Preprocess,
                ExpressionErrorSeverity.StopSign,
                returnCode: null,
                caughtExceptionArgumentIndex: null,
                "预处理表达式发生错误!\n[{1}]表达式错误:\n变量[{2}],未定义",
                "String(dwo.name)",
                "sArgs[1]"),

            // [:L2254] The variable-reference built-in resolved its argument to an empty expression.
            Plain(
                ExpressionErrorSite.PreprocessMacroVariableValueInvalid,
                ExpressionErrorCategory.Preprocess,
                ExpressionErrorSeverity.StopSign,
                returnCode: null,
                caughtExceptionArgumentIndex: null,
                "预处理表达式发生错误!\n[{1}]表达式错误:\n变量[{2}],值无效",
                "String(dwo.name)",
                "sArgs[1]"),

            // [:L2282] The `case else` of the DYNAMIC macro-result conversion switch: the value's
            // runtime type matched no arm. Categorised invalid-value on its own text - "invalid
            // function return value" - which is also how the contract's invalid-value category
            // describes this exact locator.
            Plain(
                ExpressionErrorSite.PreprocessMacroReturnValueInvalid,
                ExpressionErrorCategory.InvalidValue,
                ExpressionErrorSeverity.StopSign,
                returnCode: null,
                caughtExceptionArgumentIndex: null,
                "预处理表达式发生错误!\n[{1}]表达式错误:\n函数[{2}],返回值无效",
                "String(dwo.name)",
                "sArgs[1]"),

            // [:L2306] The `case else` of the DIRECT macro-result conversion switch. Byte-identical
            // template to :L2282; the name comes from the function entry rather than the first
            // argument.
            Plain(
                ExpressionErrorSite.PreprocessFunctionReturnValueInvalid,
                ExpressionErrorCategory.InvalidValue,
                ExpressionErrorSeverity.StopSign,
                returnCode: null,
                caughtExceptionArgumentIndex: null,
                "预处理表达式发生错误!\n[{1}]表达式错误:\n函数[{2}],返回值无效",
                "String(dwo.name)",
                "fns[nFnIdx].name"),

            // [:L2392] Evaluating a VARIABLE's own expression answered `"?"` or `"!"`. The last dialog
            // in the object, and the one whose expression argument is the ALREADY-PREPROCESSED text
            // [:L2388] rather than the variable's stored source.
            Plain(
                ExpressionErrorSite.VariableExpressionError,
                ExpressionErrorCategory.Preprocess,
                ExpressionErrorSeverity.StopSign,
                returnCode: null,
                caughtExceptionArgumentIndex: null,
                "变量[{1}]表达式错误:\nExpression:{2}",
                "GlobalVars[index].name",
                "sExp"),
        ];
    }
}

/// <summary>
/// A legacy <c>MessageBox</c> from the column-expression engine, re-expressed as a structured,
/// machine-readable result.
/// </summary>
/// <remarks>
/// <para>
/// ONLY THE DELIVERY CHANNEL CHANGES. The message text, the substitution arguments, the title, the
/// severity, the failure category, the return code and - for the eleven caret-bearing sites - the
/// expression and the caret position are all carried through unaltered. There is no dialog anywhere
/// in this port and there is no information loss either.
/// </para>
/// <para>
/// The member set mirrors the published <c>dataservices.v1.ExpressionError</c> message and the
/// <c>dataservices.v1.StructuredError</c> it embeds, so the gRPC layer projects field for field
/// (DECISION 9). This type deliberately does NOT reference the generated contract classes: the
/// folder's error foundation depends on <c>PowerFramework.Shared.Kernel</c> and the BCL only, and
/// keeping it that way is what lets every other file in the folder be unit-tested without the
/// protocol toolchain.
/// </para>
/// <para>
/// Construct instances through <see cref="ParseErrorFormatter.CreatePlainError"/> or
/// <see cref="ParseErrorFormatter.CreateParseError"/>. Those factories are what guarantee that
/// <see cref="Text"/>, <see cref="FormatArguments"/> and <see cref="RenderedMarker"/> describe the
/// same failure; a hand-assembled instance can be made to contradict itself.
/// </para>
/// </remarks>
public sealed record ExpressionParseError
{
    /// <summary>The legacy dialog site this error migrates.</summary>
    public required ExpressionErrorSite Site { get; init; }

    /// <summary>Whether the error carries a caret, and therefore the caret-bearing members.</summary>
    public required ExpressionErrorFamily Family { get; init; }

    /// <summary>The kind of failure being reported.</summary>
    public required ExpressionErrorCategory Category { get; init; }

    /// <summary>The severity of the dialog this error replaces.</summary>
    public required ExpressionErrorSeverity Severity { get; init; }

    /// <summary>
    /// The dialog title, always the legacy literal <see cref="ExpressionErrorCatalog.LegacyTitle"/>.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="Text"/> because the legacy passed them as two separate
    /// <c>MessageBox</c> arguments and because the published contract keeps them apart too. Folding
    /// them together would make it impossible to render the message without also rendering a title.
    /// </remarks>
    public required string Title { get; init; }

    /// <summary>
    /// The message exactly as the legacy dialog would have shown it: the template with its arguments
    /// substituted, and for a caret-bearing error the full formatter output including the expression,
    /// the underscore run, the caret and the position.
    /// </summary>
    /// <remarks>
    /// This is observable output and is reproduced verbatim, HARDCODED CHINESE INCLUDED. Nothing here
    /// passes through the localization layer - see <see cref="Localized"/> and the note in this file's
    /// header.
    /// </remarks>
    public required string Text { get; init; }

    /// <summary>
    /// Whether <see cref="Text"/> was produced through the localization layer. ALWAYS
    /// <see langword="false"/> for every error this engine raises.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS FIELD REPORTS A LEGACY INCONSISTENCY; IT DOES NOT RESOLVE ONE. All 28 messages in
    /// <c>n_cst_dwsvc_columnexp.sru</c> are hardcoded Chinese, while the equivalent messages on
    /// C-03's validation path DO localize through <c>CAT_DWSVC</c>
    /// [se_cst_dw.sru:L357, :L368]. Which is which is an accident of where the code was written, not
    /// a pattern.
    /// </para>
    /// <para>
    /// The flag exists so a consumer can tell translatable text from untranslatable text WITHOUT ANY
    /// MESSAGE CHANGING. Routing these 28 through localization would change observable output, which
    /// is exactly the silent correction constraint C-B forbids.
    /// </para>
    /// </remarks>
    public required bool Localized { get; init; }

    /// <summary>
    /// The localization category <see cref="Text"/> was translated under. ALWAYS zero here, because
    /// <see cref="Localized"/> is always <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Carried as a bare number rather than a typed category for a deliberate reason: the category
    /// space belongs to <c>PowerFramework.Shared.Localization</c>, and typing this field would
    /// require importing that library into the one file in the service that must not import it.
    /// </remarks>
    public required long LocalizationCategory { get; init; }

    /// <summary>The template <see cref="Text"/> was rendered from, with its placeholders intact.</summary>
    public required string FormatTemplate { get; init; }

    /// <summary>
    /// The substitution arguments, in order, BEFORE substitution.
    /// </summary>
    /// <remarks>
    /// The migration plan requires the substitution arguments to survive, not merely the rendered
    /// string, so that a consumer can re-render the message - in another format, or with a value
    /// redacted - without parsing the rendered text back apart. A null argument is normalised to the
    /// empty string here (DECISION 6), which is also what the contract's <c>repeated string</c> field
    /// requires.
    /// </remarks>
    public required ImmutableArray<string> FormatArguments { get; init; }

    /// <summary>
    /// The <c>RetCode</c> the enclosing legacy function returned, or <see langword="null"/> where it
    /// returned something that is not a return code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// BRANCH ON THE SPECIFIC VALUE, NEVER ON A TWO-WAY SUCCESS TEST. The framework's return algebra
    /// has a tri-state hole that the shared <c>Predicates</c> reproduce exactly: a veto reads as a
    /// success, and cancelled and null are neither succeeded nor failed.
    /// </para>
    /// <para>
    /// Null is a measured fact about 13 of the 28 sites, not missing information - see
    /// <see cref="ExpressionErrorSiteDescriptor.ReturnCode"/>.
    /// </para>
    /// </remarks>
    public required long? ReturnCode { get; init; }

    /// <summary>
    /// The expression the failure occurred in - the formatter's <c>syntax</c> argument - or
    /// <see langword="null"/> for a plain-message error.
    /// </summary>
    /// <remarks>
    /// THIS IS THE TRAILER-EXTENDED EXPRESSION, and <see cref="CaretPosition"/> indexes it. The parser
    /// appends a <c>";"</c> sentinel before scanning [n_cst_dwsvc_columnexp.sru:L1251, :L1262], so a
    /// consumer that renders the caret against the caller's ORIGINAL expression is off by the trailer
    /// and may find the position past the end. The two travel together here precisely so that cannot
    /// happen.
    /// </remarks>
    public string? Expression { get; init; }

    /// <summary>
    /// The ONE-BASED caret position within <see cref="Expression"/>, or <see langword="null"/> for a
    /// plain-message error.
    /// </summary>
    /// <remarks>
    /// Nullable rather than defaulting to zero because ZERO IS A MEANINGFUL POSITION: it is the
    /// boundary the legacy renders with no underscores at all (DECISION 4). A consumer that drew its
    /// own marker from a character count would misplace it - see THE LenA HAZARD in this file's
    /// header - which is why <see cref="RenderedMarker"/> travels alongside.
    /// </remarks>
    public long? CaretPosition { get; init; }

    /// <summary>
    /// Which formula produced <see cref="CaretPosition"/>, or
    /// <see cref="CaretPositionConvention.Unspecified"/> for a plain-message error.
    /// </summary>
    public CaretPositionConvention CaretConvention { get; init; }

    /// <summary>
    /// The legacy's fully rendered marker - the expression, a line feed, the underscore run, the
    /// caret and the position in parentheses - or <see langword="null"/> for a plain-message error.
    /// </summary>
    /// <remarks>
    /// This is the TWO-ARGUMENT formatter output, whereas <see cref="Text"/> is the three-argument
    /// output that also carries the message (DECISION 1). It exists for byte-exact characterization
    /// comparison: a recording can be diffed against it without any consumer having to reimplement
    /// the byte-width rules the underscore run depends on.
    /// </remarks>
    public string? RenderedMarker { get; init; }

    /// <summary>
    /// The line in the read-only oracle this error was migrated from.
    /// </summary>
    public int LegacyLine
    {
        get { return (int)Site; }
    }

    /// <summary>
    /// The text of a CAUGHT RUNTIME EXCEPTION, where the legacy captured one; otherwise
    /// <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Non-null for exactly one of the 28 sites - the cache-read failure at
    /// [n_cst_dwsvc_columnexp.sru:L739], which embeds <c>ex.text</c> as its third argument. It is
    /// DERIVED from <see cref="FormatArguments"/> at the index the site's descriptor records, rather
    /// than stored a second time, so it can never disagree with the rendered message. Consumers use
    /// it to tell an engine diagnostic from a propagated runtime fault.
    /// </remarks>
    public string? CaughtExceptionText
    {
        get
        {
            if (!ExpressionErrorCatalog.TryGet(Site, out ExpressionErrorSiteDescriptor? descriptor)
                || descriptor.CaughtExceptionArgumentIndex is not int index)
            {
                return null;
            }

            // The descriptor's index is one-based, matching the format placeholders it describes.
            // Its bounds are checked when the descriptor is built, so this guard covers only an error
            // whose argument list was assembled by hand rather than by a factory.
            return index >= 1 && index <= FormatArguments.Length
                ? FormatArguments[index - 1]
                : null;
        }
    }

    /// <summary>
    /// Compares two errors by VALUE, comparing <see cref="FormatArguments"/> element by element.
    /// </summary>
    /// <param name="other">The error to compare against.</param>
    /// <returns>
    /// <see langword="true"/> when every recorded member is equal; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// WRITTEN OUT BECAUSE THE COMPILER'S VERSION IS WRONG FOR THIS TYPE, and it was a real failing
    /// test rather than a theoretical concern. <see cref="ImmutableArray{T}"/> implements equality as
    /// REFERENCE equality of its underlying array, so the generated record <c>Equals</c> reported two
    /// errors built from identical inputs as different. That matters most in exactly the place this
    /// type is meant to serve: a characterization suite comparing a recorded error against a
    /// reproduced one would fail for a reason unrelated to the behaviour under comparison.
    /// </para>
    /// <para>
    /// <see cref="LegacyLine"/> and <see cref="CaughtExceptionText"/> are deliberately absent because
    /// both are DERIVED - from <see cref="Site"/> and from <see cref="FormatArguments"/> respectively -
    /// so comparing them would compare the same fact twice. ADDING A STORED MEMBER TO THIS RECORD MEANS
    /// ADDING IT HERE AND TO <see cref="GetHashCode"/>.
    /// </para>
    /// </remarks>
    public bool Equals(ExpressionParseError? other)
    {
        return other is not null
            && Site == other.Site
            && Family == other.Family
            && Category == other.Category
            && Severity == other.Severity
            && Title == other.Title
            && Text == other.Text
            && Localized == other.Localized
            && LocalizationCategory == other.LocalizationCategory
            && FormatTemplate == other.FormatTemplate
            && ReturnCode == other.ReturnCode
            && Expression == other.Expression
            && CaretPosition == other.CaretPosition
            && CaretConvention == other.CaretConvention
            && RenderedMarker == other.RenderedMarker
            && FormatArguments.AsSpan().SequenceEqual(other.FormatArguments.AsSpan());
    }

    /// <summary>
    /// Hashes the same members <see cref="Equals(ExpressionParseError)"/> compares.
    /// </summary>
    /// <returns>A hash code consistent with value equality.</returns>
    public override int GetHashCode()
    {
        HashCode hash = default;

        hash.Add(Site);
        hash.Add(Family);
        hash.Add(Category);
        hash.Add(Severity);
        hash.Add(Title, StringComparer.Ordinal);
        hash.Add(Text, StringComparer.Ordinal);
        hash.Add(Localized);
        hash.Add(LocalizationCategory);
        hash.Add(FormatTemplate, StringComparer.Ordinal);
        hash.Add(ReturnCode);
        hash.Add(Expression, StringComparer.Ordinal);
        hash.Add(CaretPosition);
        hash.Add(CaretConvention);
        hash.Add(RenderedMarker, StringComparer.Ordinal);

        foreach (string argument in FormatArguments)
        {
            hash.Add(argument, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// Renders the column-expression engine's failures exactly as the legacy did, and builds the
/// structured errors that replace its dialogs.
/// </summary>
/// <remarks>
/// <para>
/// The port of <c>_of_buildparseerror</c> [n_cst_dwsvc_columnexp.sru:L2402-L2407], plus the two
/// factories the rest of the <c>Expressions/</c> folder raises its errors through. It holds no state,
/// touches no I/O and is safe to call from any thread.
/// </para>
/// <para>
/// It also holds the three PowerBuilder string primitives the formatter depends on -
/// <see cref="LenA"/>, <c>Left</c> and <c>Fill</c> - because the shared kernel does not provide them
/// and their exact boundary behaviour is what makes this port byte-exact.
/// </para>
/// </remarks>
public static class ParseErrorFormatter
{
    /// <summary>
    /// The line separator PowerScript's <c>~n</c> escape denotes, and the one the legacy formatter
    /// emits between the message and the expression.
    /// </summary>
    /// <remarks>
    /// A LINE FEED, NOT <see cref="Environment.NewLine"/>. That distinction is the whole reason this
    /// is a named constant: PowerScript's <c>~n</c> is U+000A, and on a Windows host
    /// <see cref="Environment.NewLine"/> would emit a carriage return as well, adding a byte to every
    /// rendered message and breaking every characterization comparison. The services run on Linux
    /// today, where the two happen to agree, which is exactly what makes the mistake easy to make and
    /// invisible until the platform changes.
    /// </remarks>
    public const string LineSeparator = "\n";

    /// <summary>
    /// The character the legacy pads the caret's indent with: <c>Fill("_", ...)</c> [:L2403].
    /// </summary>
    /// <remarks>
    /// An UNDERSCORE, not a space. It draws a visible rule from the start of the expression to the
    /// offending position, so it is observable output rather than whitespace a renderer may normalise.
    /// </remarks>
    public const char PaddingCharacter = '_';

    /// <summary>The caret the legacy points at the offending position [:L2403].</summary>
    public const char CaretCharacter = '^';

    /// <summary>
    /// Renders the legacy parse-error message: an optional leading message, the expression, and a
    /// caret indented to the offending position.
    /// </summary>
    /// <param name="syntax">
    /// The expression to render. This is the TRAILER-EXTENDED expression at every legacy call site -
    /// <c>sExp = exp + ";"</c> [n_cst_dwsvc_columnexp.sru:L1262] - and it is rendered exactly as given:
    /// this method never adds the sentinel and never strips it. <see langword="null"/> is treated as
    /// the empty string (DECISION 6).
    /// </param>
    /// <param name="pos">
    /// The ONE-BASED caret position within <paramref name="syntax"/>, computed by the caller under one
    /// of the two conventions <see cref="CaretPositionConvention"/> documents (DECISION 2). Values at
    /// or below zero, and values past the end of <paramref name="syntax"/>, are reproduced rather than
    /// rejected (DECISION 4).
    /// </param>
    /// <param name="errinfo">
    /// The message to place above the expression. When non-empty it is followed by TWO line
    /// separators; when empty or <see langword="null"/> nothing at all is prepended, so the expression
    /// starts on the first line.
    /// </param>
    /// <returns>The rendered message. Never <see langword="null"/>.</returns>
    /// <remarks>
    /// <para>
    /// The oracle, transcribed [n_cst_dwsvc_columnexp.sru:L2402-L2404]:
    /// </para>
    /// <code>
    /// if errInfo &lt;&gt; "" then errInfo += "~n~n"
    /// return errInfo + syntax + "~n" + Fill("_",LenA(Left(syntax,pos)) - 1) + "^ (" + String(pos) + ")"
    /// </code>
    /// <para>
    /// Rendered for <c>syntax</c> = <c>"a+$x;"</c> and <c>pos</c> = <c>4</c>, with no message:
    /// </para>
    /// <code>
    /// a+$x;
    /// ___^ (4)
    /// </code>
    /// <para>
    /// THE PAD IS SIZED IN BYTES, NOT CHARACTERS. It is <c>LenA(Left(syntax,pos)) - 1</c>, and
    /// <see cref="LenA"/> counts a Han character as two. Sizing it from a UTF-16 character count would
    /// misplace the caret under every Chinese expression the engine reports on - see THE LenA HAZARD
    /// in this file's header, and the test that pins it.
    /// </para>
    /// <para>
    /// There is exactly ONE space in the output, the one inside the literal <c>"^ ("</c>. Everything
    /// else is the pad, and the pad is underscores.
    /// </para>
    /// </remarks>
    public static string BuildParseError(string? syntax, long pos, string? errinfo)
    {
        // DECISION 6: null is normalised at the boundary. PowerScript would propagate it and answer
        // null for the whole expression, but that path is unreachable from all 12 legacy call sites,
        // and reproducing it would hand every caller a null to check for a state that cannot arise.
        string expression = syntax ?? string.Empty;
        string message = errinfo ?? string.Empty;

        // [:L2402] `if errInfo <> "" then errInfo += "~n~n"`. TWO separators after a non-empty
        // message and NONE after an empty one, which is what puts the expression on the first line
        // for the two-argument arity.
        if (message.Length != 0)
        {
            message += LineSeparator + LineSeparator;
        }

        // [:L2403], term by term and in the oracle's own order. `Left` then `LenA` then `- 1` then
        // `Fill`: the subtraction happens on the BYTE count, so it cannot be folded into the slice.
        string padding = Fill(PaddingCharacter, LenA(Left(expression, pos)) - 1);

        return message
            + expression
            + LineSeparator
            + padding
            + CaretCharacter
            + " ("
            // `String(pos)` [:L2403]. Invariant culture, so a host whose locale renders digits or
            // signs differently cannot change a byte of a characterization recording.
            + pos.ToString(CultureInfo.InvariantCulture)
            + ")";
    }

    /// <summary>
    /// Renders the legacy parse-error message with no leading message, delegating to the
    /// three-argument form with an empty one.
    /// </summary>
    /// <param name="syntax">
    /// The expression to render; the trailer-extended expression at every legacy call site.
    /// </param>
    /// <param name="pos">The one-based caret position within <paramref name="syntax"/>.</param>
    /// <returns>The rendered expression and caret. Never <see langword="null"/>.</returns>
    /// <remarks>
    /// <para>
    /// The oracle, transcribed [n_cst_dwsvc_columnexp.sru:L2406]:
    /// </para>
    /// <code>
    /// private function string _of_buildparseerror (readonly string syntax, readonly long pos);
    ///   return _of_BuildParseError(syntax,pos,"")
    /// end function
    /// </code>
    /// <para>
    /// THIS IS NOT A CONVENIENCE OVERLOAD AND IT IS NOT COLLAPSED INTO AN OPTIONAL PARAMETER
    /// (DECISION 1). Its output is what the published contract carries as
    /// <c>ExpressionError.rendered_marker</c>, while the three-argument form's output is what it
    /// carries as <c>StructuredError.text</c>. Both travel on every caret-bearing error, and the
    /// delegation is the guarantee that they can never disagree about the expression, the pad or the
    /// position.
    /// </para>
    /// </remarks>
    public static string BuildParseError(string? syntax, long pos)
    {
        // [:L2406] verbatim: the two-argument arity delegates with an EMPTY message. Passing
        // `string.Empty` rather than `null` mirrors the legacy literal `""` exactly, and the two are
        // observably identical here only because of DECISION 6.
        return BuildParseError(syntax, pos, string.Empty);
    }

    /// <summary>
    /// The PowerBuilder <c>LenA</c> function: the length of a string in BYTES under the host's
    /// ANSI/DBCS codepage.
    /// </summary>
    /// <param name="value">The string to measure. <see langword="null"/> measures zero.</param>
    /// <returns>The byte length. Never negative.</returns>
    /// <remarks>
    /// <para>
    /// THE RULE: AN ASCII CODE POINT IS ONE BYTE AND EVERY OTHER CODE POINT IS TWO. The legacy runs on
    /// a Chinese Windows host, whose ANSI codepage is double-byte: a Han character occupies two bytes
    /// and, in the fixed-width console the caret was designed for, two columns. That correspondence is
    /// why the legacy sizes a visual indent with a byte count in the first place.
    /// </para>
    /// <para>
    /// WHY THE RULE IS ENCODED HERE RATHER THAN DELEGATED (DECISION 3).
    /// <c>Encoding.GetEncoding(936).GetByteCount</c> would be the obvious implementation and it is not
    /// available: the DBCS codepages live in <c>System.Text.Encoding.CodePages</c>, a package this
    /// refactor does not take, and substituting a codepage that IS available would silently change the
    /// answer. UTF-8, for instance, encodes a Han character in THREE bytes, so
    /// <c>Encoding.UTF8.GetByteCount</c> would over-indent the caret by one column per Chinese
    /// character - a defect that looks like an off-by-one and is not one.
    /// </para>
    /// <para>
    /// EXACTNESS AND ITS TWO KNOWN BOUNDS, stated rather than hidden. The rule is exact for ASCII and
    /// for the entire double-byte repertoire of the Chinese ANSI codepage, which covers 100% of what
    /// the oracle contains - ASCII plus Han in the basic multilingual plane - and also its Greek,
    /// Cyrillic, kana and pinyin-diacritic rows. It over-counts by one byte in two regions the oracle
    /// cannot adjudicate: the euro sign, which that codepage encodes as a single byte, and any
    /// character the codepage cannot represent at all, which the legacy would substitute with a
    /// one-byte question mark. Neither appears in any expression or message in the corpus.
    /// </para>
    /// <para>
    /// A supplementary code point - a UTF-16 surrogate pair - counts TWO, not four, because
    /// <c>LenA</c> measures characters as bytes and a surrogate pair is one character. An UNPAIRED
    /// surrogate counts two as well, under the same non-ASCII rule; it is malformed text, and the
    /// alternative of special-casing it would add a branch no legacy behaviour asks for.
    /// </para>
    /// </remarks>
    public static long LenA(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        long bytes = 0;

        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];

            // A well-formed surrogate pair is ONE code point and is consumed as one. The second half
            // is skipped rather than counted, which is what keeps a supplementary character at two
            // bytes instead of four.
            if (char.IsHighSurrogate(current)
                && index + 1 < value.Length
                && char.IsLowSurrogate(value[index + 1]))
            {
                index++;
                bytes += 2;
                continue;
            }

            // The whole rule, in one comparison: ASCII is single-byte in every ANSI codepage, and
            // everything else is a double-byte sequence in a DBCS codepage.
            bytes += current < 0x0080 ? 1 : 2;
        }

        return bytes;
    }

    /// <summary>
    /// Builds the structured error for a PLAIN-MESSAGE site - one of the 17 the legacy raised with no
    /// caret.
    /// </summary>
    /// <param name="site">The legacy dialog site being reported.</param>
    /// <param name="args">
    /// The substitution arguments, in the order the site's template expects. Exactly
    /// <see cref="ExpressionErrorSiteDescriptor.ArgumentCount"/> of them; a <see langword="null"/>
    /// element renders as the empty string (DECISION 6).
    /// </param>
    /// <returns>The structured error, ready to project onto the published contract.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="site"/> is not one of the 28 legacy sites.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="site"/> is caret-bearing - use <see cref="CreateParseError"/> - or the wrong
    /// number of arguments was supplied.
    /// </exception>
    /// <remarks>
    /// Both exceptions report a CALLER MISTAKE and neither is reachable from data (DECISION 7): each
    /// site's family and argument count are fixed by a read-only source, so the 28-site parity matrix
    /// turns either mistake into a build failure rather than a runtime one. Nothing data-shaped throws.
    /// </remarks>
    public static ExpressionParseError CreatePlainError(
        ExpressionErrorSite site,
        params string?[]? args)
    {
        ExpressionErrorSiteDescriptor descriptor = ExpressionErrorCatalog.Get(site);

        if (descriptor.Family != ExpressionErrorFamily.PlainMessage)
        {
            throw new ArgumentException(
                "Site " + site + " is a caret-bearing parse error at "
                    + ExpressionErrorCatalog.OracleSourcePath + ":L"
                    + descriptor.LegacyLine.ToString(CultureInfo.InvariantCulture)
                    + ", so it must be reported through " + nameof(CreateParseError)
                    + " with its expression and caret position.",
                nameof(site));
        }

        ImmutableArray<string> arguments = NormaliseArguments(descriptor, args);

        return new ExpressionParseError
        {
            Site = site,
            Family = descriptor.Family,
            Category = descriptor.Category,
            Severity = descriptor.Severity,
            Title = ExpressionErrorCatalog.LegacyTitle,
            Text = Formatting.Sprintf(descriptor.FormatTemplate, [.. arguments]),
            // Hard-wired, both of them, for every error this engine raises. See the preserved
            // localization inconsistency in this file's header: harmonising these 28 messages with the
            // 23 that DO localize elsewhere in the service would change observable output text.
            Localized = false,
            LocalizationCategory = 0,
            FormatTemplate = descriptor.FormatTemplate,
            FormatArguments = arguments,
            ReturnCode = descriptor.ReturnCode,
            // A plain-message site has no expression, no position, no convention and no marker. All
            // four stay absent rather than being filled with a plausible default, because a zero
            // position is a MEANINGFUL position at the caret-bearing sites.
            Expression = null,
            CaretPosition = null,
            CaretConvention = CaretPositionConvention.Unspecified,
            RenderedMarker = null,
        };
    }

    /// <summary>
    /// Builds the structured error for a CARET-BEARING site - one of the 11 the legacy routed through
    /// <c>_of_BuildParseError</c>.
    /// </summary>
    /// <param name="site">The legacy dialog site being reported.</param>
    /// <param name="syntax">
    /// The expression the failure occurred in. Pass the TRAILER-EXTENDED expression the scanner holds
    /// - <c>sExp</c>, not the caller's original - because <paramref name="caretPosition"/> indexes it
    /// [n_cst_dwsvc_columnexp.sru:L1262].
    /// </param>
    /// <param name="caretPosition">
    /// The one-based position within <paramref name="syntax"/>, already computed by the caller under
    /// the site's own convention (DECISION 2).
    /// </param>
    /// <param name="args">
    /// The substitution arguments for the site's message, in order. Exactly
    /// <see cref="ExpressionErrorSiteDescriptor.ArgumentCount"/> of them.
    /// </param>
    /// <returns>The structured error, carrying both formatter renderings.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="site"/> is not one of the 28 legacy sites.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="site"/> is a plain-message site - use <see cref="CreatePlainError"/> - or the
    /// wrong number of arguments was supplied.
    /// </exception>
    /// <remarks>
    /// <para>
    /// BOTH FORMATTER ARITIES ARE CALLED, and that is the point of them existing separately
    /// (DECISION 1). <see cref="ExpressionParseError.Text"/> is the three-argument rendering, message
    /// included, exactly as the legacy dialog showed it;
    /// <see cref="ExpressionParseError.RenderedMarker"/> is the two-argument rendering, for byte-exact
    /// characterization comparison. Because the second delegates to the first, the two cannot disagree
    /// about the expression, the pad or the position.
    /// </para>
    /// <para>
    /// The position is recorded, never recomputed and never validated against
    /// <paramref name="syntax"/>: an out-of-range value renders exactly as the legacy renders it
    /// (DECISION 4), which is what lets a recording of a legacy fault be reproduced rather than
    /// rejected.
    /// </para>
    /// </remarks>
    public static ExpressionParseError CreateParseError(
        ExpressionErrorSite site,
        string? syntax,
        long caretPosition,
        params string?[]? args)
    {
        ExpressionErrorSiteDescriptor descriptor = ExpressionErrorCatalog.Get(site);

        if (descriptor.Family != ExpressionErrorFamily.CaretBearingParseError)
        {
            throw new ArgumentException(
                "Site " + site + " is a plain message at "
                    + ExpressionErrorCatalog.OracleSourcePath + ":L"
                    + descriptor.LegacyLine.ToString(CultureInfo.InvariantCulture)
                    + ", which the legacy raises with no caret, so it must be reported through "
                    + nameof(CreatePlainError) + ".",
                nameof(site));
        }

        ImmutableArray<string> arguments = NormaliseArguments(descriptor, args);
        string message = Formatting.Sprintf(descriptor.FormatTemplate, [.. arguments]);
        string expression = syntax ?? string.Empty;

        return new ExpressionParseError
        {
            Site = site,
            Family = descriptor.Family,
            Category = descriptor.Category,
            Severity = descriptor.Severity,
            Title = ExpressionErrorCatalog.LegacyTitle,
            // The three-argument arity [:L2402]: message, blank line, expression, caret.
            Text = BuildParseError(expression, caretPosition, message),
            Localized = false,
            LocalizationCategory = 0,
            FormatTemplate = descriptor.FormatTemplate,
            FormatArguments = arguments,
            ReturnCode = descriptor.ReturnCode,
            Expression = expression,
            CaretPosition = caretPosition,
            CaretConvention = descriptor.CaretConvention,
            // The two-argument arity [:L2406]: expression and caret only.
            RenderedMarker = BuildParseError(expression, caretPosition),
        };
    }

    /// <summary>
    /// Checks a supplied argument list against the site's fixed arity and normalises it to non-null
    /// strings.
    /// </summary>
    /// <param name="descriptor">The site's descriptor, which fixes the expected count.</param>
    /// <param name="args">The caller's arguments, possibly <see langword="null"/>.</param>
    /// <returns>Exactly <c>descriptor.ArgumentCount</c> non-null strings, in order.</returns>
    /// <exception cref="ArgumentException">The count does not match.</exception>
    /// <remarks>
    /// <c>params</c> permits the ARRAY itself to be null, at the call <c>CreatePlainError(site, null)</c>,
    /// so it is normalised before use rather than dereferenced. A null ELEMENT becomes the empty string
    /// (DECISION 6), which keeps the rendered text and the structured arguments in agreement and
    /// satisfies the contract's non-nullable string collection.
    /// </remarks>
    private static ImmutableArray<string> NormaliseArguments(
        ExpressionErrorSiteDescriptor descriptor,
        string?[]? args)
    {
        string?[] supplied = args ?? [];

        if (supplied.Length != descriptor.ArgumentCount)
        {
            throw new ArgumentException(
                "Site " + descriptor.Site + " at " + ExpressionErrorCatalog.OracleSourcePath + ":L"
                    + descriptor.LegacyLine.ToString(CultureInfo.InvariantCulture) + " builds its "
                    + "message from exactly "
                    + descriptor.ArgumentCount.ToString(CultureInfo.InvariantCulture)
                    + " value(s), but " + supplied.Length.ToString(CultureInfo.InvariantCulture)
                    + " were supplied. The count is fixed by the oracle, which concatenates them.",
                nameof(args));
        }

        ImmutableArray<string>.Builder builder =
            ImmutableArray.CreateBuilder<string>(supplied.Length);

        foreach (string? argument in supplied)
        {
            builder.Add(argument ?? string.Empty);
        }

        return builder.MoveToImmutable();
    }

    /// <summary>
    /// The PowerBuilder <c>Left</c> function: the first <paramref name="count"/> characters of
    /// <paramref name="value"/>.
    /// </summary>
    /// <param name="value">The string to take from.</param>
    /// <param name="count">How many characters to take.</param>
    /// <returns>
    /// The empty string when <paramref name="count"/> is zero or less; the whole of
    /// <paramref name="value"/> when <paramref name="count"/> reaches or passes its length; otherwise
    /// the leading <paramref name="count"/> characters.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Ported for its BOUNDARY behaviour, which is the half a .NET substring does not share.
    /// <c>value.Substring(0, count)</c> throws for a negative count and for a count past the end;
    /// PowerBuilder clamps at both ends and returns a string. Both clamps are load-bearing here: the
    /// lower one is what makes a zero position render at all (DECISION 4), and the upper one is what
    /// makes a position pointing at the trailer sentinel - or past it - render rather than fault.
    /// </para>
    /// <para>
    /// It counts CHARACTERS, matching PowerBuilder's own <c>Len</c> domain, and so it may split a
    /// surrogate pair. That is faithful: PowerBuilder strings are UTF-16 and its <c>Left</c> would
    /// split one too. <see cref="LenA"/> then measures the split half under its non-ASCII rule.
    /// </para>
    /// </remarks>
    private static string Left(string value, long count)
    {
        if (count <= 0)
        {
            return string.Empty;
        }

        // Compared as `long` deliberately: a cast to `int` first would wrap a large position into a
        // negative one and turn a clamp into an exception.
        if (count >= value.Length)
        {
            return value;
        }

        return value[..(int)count];
    }

    /// <summary>
    /// The PowerBuilder <c>Fill</c> function, specialised to the single character the formatter pads
    /// with.
    /// </summary>
    /// <param name="character">The character to repeat.</param>
    /// <param name="count">How many times to repeat it.</param>
    /// <returns>
    /// The empty string when <paramref name="count"/> is zero or less; otherwise
    /// <paramref name="count"/> copies of <paramref name="character"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE NON-POSITIVE COUNT IS THE WHOLE REASON THIS EXISTS (DECISION 4). The formatter hands it
    /// <c>LenA(Left(syntax,pos)) - 1</c>, which is <c>-1</c> whenever <c>pos</c> is zero or less, and
    /// PowerBuilder answers the empty string there rather than raising. <c>new string(c, -1)</c>
    /// throws instead, so the guard is what keeps a legacy edge case from becoming a .NET exception
    /// raised while the engine is reporting a fault.
    /// </para>
    /// <para>
    /// The upper clamp is unreachable in practice and present so that no input can reach the negative
    /// length a narrowing conversion would otherwise produce: a .NET string cannot hold more than
    /// <see cref="int.MaxValue"/> characters, so a count beyond it cannot be honoured and is capped
    /// rather than wrapped.
    /// </para>
    /// </remarks>
    private static string Fill(char character, long count)
    {
        if (count <= 0)
        {
            return string.Empty;
        }

        int length = count > int.MaxValue ? int.MaxValue : (int)count;

        return new string(character, length);
    }
}
