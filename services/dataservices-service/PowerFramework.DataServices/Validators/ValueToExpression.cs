// ==================================================================================================
// ValueToExpression.cs
// ==================================================================================================
//
// WHAT THIS FILE IS
//   The complete port of the legacy PowerBuilder global function object
//   `ws_objects/pfw.datawindow.services.pbl.src/dwvaluetoexp.srf` (71 lines, nine overloads). The
//   legacy object turns a typed cell value into the TEXT OF A DATAWINDOW EXPRESSION LITERAL, so that
//   the column expression engine can splice a value into an expression string it is about to hand to
//   the DataWindow runtime for evaluation.
//
//   The nine legacy bodies, transcribed from the source rather than paraphrased:
//
//       L17-L21  (decimal)   sVal = String(val)                     ; if IsNull(sVal) then sVal = "dwNvlNumber()"
//       L23-L27  (int)       sVal = String(val)                     ; if IsNull(sVal) then sVal = "dwNvlNumber()"
//       L29-L33  (long)      sVal = String(val)                     ; if IsNull(sVal) then sVal = "dwNvlNumber()"
//       L35-L39  (double)    sVal = String(val)                     ; if IsNull(sVal) then sVal = "dwNvlNumber()"
//       L41-L45  (real)      sVal = String(val)                     ; if IsNull(sVal) then sVal = "dwNvlNumber()"
//       L47-L51  (string)    sVal = "'" + val + "'"                 ; if IsNull(sVal) then sVal = "dwNvlString()"
//       L53-L57  (datetime)  sVal = "DateTime('" + String(val) + "')"; if IsNull(sVal) then sVal = "dwNvlDateTime()"
//       L59-L63  (date)      sVal = "Date('" + String(val) + "')"    ; if IsNull(sVal) then sVal = "dwNvlDate()"
//       L65-L69  (time)      sVal = "Time('" + String(val) + "')"    ; if IsNull(sVal) then sVal = "dwNvlTime()"
//
//   Every body has the same four-statement shape: declare `string sVal`, assign it, test the RESULT
//   for null and substitute a sentinel, return it. That third statement is the whole difficulty of
//   this port and is treated in full under THE ONE HAZARD below.
//
// ==================================================================================================
// THE ONE HAZARD: POWERSCRIPT PROPAGATES NULL THROUGH `+`, AND C# DOES NOT
// ==================================================================================================
//
//   In PowerScript, concatenating null onto a string yields NULL for the whole expression. That is
//   the mechanism the legacy relies on, and it is why the legacy can test the RESULT instead of the
//   INPUT. At `dwvaluetoexp.srf:L54`, `"DateTime('" + String(val) + "')"` evaluates to null when
//   `val` is null, so `IsNull(sVal)` at `:L55` is true and the sentinel is selected. Identically for
//   `:L48`/`:L49`, `:L60`/`:L61` and `:L66`/`:L67`. In the five numeric bodies the same outcome comes
//   from `String(null)` itself returning null, so `:L19`, `:L25`, `:L31`, `:L37` and `:L43` are
//   reached.
//
//   C# has no such propagation. `"x" + null` is `"x"`, and `default(T?).ToString()` is `""`, never
//   null. A statement-for-statement transliteration of the legacy would therefore emit:
//
//       INPUT             LEGACY OUTPUT        NAIVE C# TRANSLITERATION   CONSEQUENCE
//       null datetime     dwNvlDateTime()      DateTime('')               malformed expression
//       null date         dwNvlDate()          Date('')                   malformed expression
//       null time         dwNvlTime()          Time('')                   malformed expression
//       null string       dwNvlString()        ''                         WRONG VALUE, silently
//       null numeric      dwNvlNumber()        (empty string)             violates H1, see below
//
//   No compiler diagnostic and no analyzer covers this. It would affect every one of the nine
//   overloads, and the first four rows above surface far from their cause: the malformed expression
//   reaches `Describe("Evaluate(...)")`, which answers `"?"` or `"!"`, and the caller raises an
//   expression error at `n_cst_dwsvc_columnexp.sru:L2391-L2392`. The fourth row is worse still,
//   because `''` is a perfectly well formed empty string literal, so it produces a WRONG RESULT with
//   no error at all.
//
//   THE RULE THIS FILE FOLLOWS, IN ALL NINE OVERLOADS WITHOUT EXCEPTION: test the INPUT for absence
//   FIRST, return the sentinel, and only then build the literal. The guard is annotated at every one
//   of the nine points of reproduction, because it looks redundant to a reader who has not met the
//   propagation difference and would otherwise be "simplified" away.
//
// ==================================================================================================
// DECISION LOG
// ==================================================================================================
//
//   DECISION 1 - THE MEMBER IS NAMED `Convert`, AND THE LEGACY NAME IS A STRING CONSTANT.
//     The legacy identifier cannot become the member name. `ValueToExpression.ValueToExpression`
//     is illegal (CS0542, a member may not share the name of its enclosing type), and the legacy
//     spelling `dwValueToExp` is camelCase, which under the repository's promoted warnings would
//     fail the build for a public member: the root .editorconfig scopes its naming-analyzer
//     suppressions to TEN named files and this is not one of them. The legacy spelling therefore
//     lives in the VALUE of `FunctionName` and never in an identifier, which is the same split the
//     five sibling producers in this folder use.
//
//   DECISION 2 - NINE OVERLOADS, AND THE FIVE NUMERIC ONES ARE NEVER MERGED.
//     The five numeric bodies are textually identical, which is an invitation to collapse them into
//     one method over a widest numeric type. They are kept distinct because the nine member surface
//     IS the contract: the legacy declares nine prototypes at `dwvaluetoexp.srf:L6-L14`, overload
//     selection is what decides which body a caller reaches, and a merged surface would silently
//     change which conversion a `real` argument receives. See DECISION 6 for why that is observable
//     for `double` and `real` even though it is not for the integral types. No tenth overload is
//     added for a type the legacy lacks: there is no `bool`, no `byte[]`, no `Guid`, no `object`.
//
//   DECISION 3 - EVERY VALUE PARAMETER IS A NULLABLE VALUE TYPE, NEVER A SENTINEL VALUE.
//     PowerBuilder has null for value types and this function's entire branch structure depends on
//     it. Collapsing null to `default` would make a genuine `DateOnly.MinValue` and an absent date
//     indistinguishable, so a null date would emit `Date('0001-01-01')` where the legacy emits
//     `dwNvlDate()`. `Validators/DateValidator.cs` records the same requirement from the producing
//     side. Legacy widths, annotated per overload below: `integer` is 16 bit, `long` is 32 bit,
//     `double` is 8 byte, `real` is 4 byte, `decimal` is fixed point.
//
//   DECISION 4 - `readonly` MAPS TO `in`, APPLIED TO ALL NINE WITH NO DEVIATION.
//     All nine legacy parameters are declared `readonly` at `dwvaluetoexp.srf:L6-L14` and the
//     transformation rules map a `readonly` parameter to an `in` parameter. Applied uniformly here,
//     because neither of the two conditions that would override it is present.
//       It does not fight nullability. `Nullable<T>` is NOT itself declared a readonly struct -
//       measured, its only attributes are NonVersionable, TypeForwardedFrom and Serializable - but
//       both of the members this file touches ARE readonly members: the `HasValue` and `Value`
//       getters each carry `IsReadOnlyAttribute` (measured by reflection on the open generic type).
//       Reading either one through the `in` reference therefore makes no defensive copy. The reason
//       is recorded precisely because the shorter and more obvious claim, that `Nullable<T>` is a
//       readonly struct, is false.
//       It creates no ambiguity. All nine parameter types are distinct and `in` plays no part in
//       overload resolution, so the modifier cannot make one overload shadow another.
//     One consequence worth naming for the consumer: `in` prevents binding a method GROUP directly
//     to `Func<,>`. A caller that wants a delegate table writes `v => Convert(v)`, which binds
//     fine. No in-repository call site takes a delegate to this function.
//
//   DECISION 5 - THE SENTINELS AND THE TEMPORAL FORMATS ARE CONSUMED, NEVER RE-TYPED.
//     The legacy hardcodes `"dwNvlNumber()"` and its four siblings at nine call sites. Here each
//     comes from the producing file's published constant, and each temporal literal's inner text
//     comes from that producer's own formatter. Literal duplication is not observable, but a
//     one-character DRIFT between the name `Expressions/DataWindowExpressionEvaluator.cs` registers
//     and the name this file emits would be, and it would surface as a runtime `"?"`/`"!"` rather
//     than as a compile error. Single sourcing turns that class of drift into a compile error. This
//     is an INTERNAL STRUCTURING CHOICE WITH NO OBSERVABLE EFFECT: the bytes reaching a DataWindow
//     expression are identical either way.
//
//   DECISION 6 - ONE PRIVATE CULTURE HELPER, AND `CultureInfo.InvariantCulture` IS ALWAYS EXPLICIT.
//     Every value to text conversion in this file goes through `FormatInvariant` at the bottom, and
//     the three temporal overloads delegate to sibling formatters that are already culture pinned,
//     so the file has exactly one conversion site and the string overload has none. The failure mode
//     this forecloses is environment dependent and silent: under a comma decimal ambient culture a
//     bare conversion emits `1,5`, which a DataWindow expression parses as an ARGUMENT SEPARATOR,
//     yielding `"?"`/`"!"` on machines whose locale happens not to be invariant and nowhere else.
//     No bare `ToString()`, no `string.Format`, no interpolation, no `Convert.*`, no `Parse` and no
//     `TryParse` appears anywhere in this file. String `+` on two `string` operands does appear: it
//     performs no formatting and consults no culture, which is exactly why it is used here in place
//     of interpolation, which would.
//
//     There is deliberately NO reuse of `PowerFramework.Shared.Kernel.Formatting.Sprintf` as a
//     stand in for PowerBuilder's `String()`. `Formatting` ports `formatretcode.srf` and
//     `sprintf.srf`, whose grammar is the PowerBuilder format mask form; `String(val)` with no mask
//     is a different primitive, and pressing one into service as the other would be a behavioural
//     guess dressed as reuse. This file owns its own conversion, and takes no dependency on the
//     shared kernel at all.
//
//   DECISION 7 - LEGACY DEFECT, REPRODUCED: THE STRING OVERLOAD DOES NOT ESCAPE SINGLE QUOTES.
//     `dwvaluetoexp.srf:L48` is `sVal = "'" + val + "'"` and nothing more. Annotated in full at the
//     point of reproduction.
//
//   DECISION 8 - LEGACY DEFECT, REPRODUCED: ONE `integer` DECLARED SENTINEL SERVES ALL FIVE NUMERIC
//     OVERLOADS. Annotated in full at the point of reproduction.
//
//   DECISION 9 - INVARIANT H1: NO OVERLOAD MAY EVER RETURN NULL OR THE EMPTY STRING.
//     `""` is the CALLER's abort sentinel, not a value. `n_cst_dwsvc_columnexp.sru:L2185` reads a
//     cell through `_of_GetItemExpValue` and `:L2186` immediately aborts the whole expression
//     preprocessing on an empty answer (`if sVal = "" then return ""`); the twin guard sits at
//     `:L2183` for the macro path. Inside `_of_GetItemExpValue` the ONLY legitimate producer of `""`
//     is the `case else` arm at `:L2357-L2358`, reached for `COL_TYPE_UNKNOWN` alone, since the six
//     named arms at `:L2345-L2356` cover `COL_TYPE_STRING`, `INTEGER`, `DECIMAL`, `DATETIME`, `DATE`
//     and `TIME` and `n_cst_dwsvc.sru:L26-L32` declares exactly those seven categories. So an
//     overload here that returned `""` for a VALID value would silently abort preprocessing for a
//     cell that is perfectly fine. Every branch below returns either a sentinel, or a literal that
//     is at minimum two characters. That includes the empty string input, which yields exactly `''`
//     and never `""`.
//
//   DECISION 10 - THE WRAPPER SPELLINGS ARE PRIVATE CONSTANTS AND STAY COMPILED IN.
//     `DateTime('`, `Date('`, `Time('`, the closing `')` and the single quote are byte exact
//     behavioural contract read out of `:L48`, `:L54`, `:L60` and `:L66`, including the capital
//     letters. They are declared once each so the file cannot drift against itself. They are NOT
//     configuration: the secrets and configuration constraint targets locale, capability masks,
//     connection URIs and key material, and moving an expression grammar token into `appsettings`
//     would make parity depend on deployment. They are private because the published surface of
//     this file is the nine overloads plus `FunctionName`, and nothing else needs them.
//
//   UNVERIFIED-FROM-REPOSITORY - three formatting questions this repository cannot settle. Each is
//   annotated again at its point of use so a characterization author can find it:
//     U-1  `decimal` trailing zeros. C# preserves scale, so `1.50m` renders `1.50` (measured).
//          Whether PowerBuilder's `String(dec)` preserves or trims is not shown anywhere in the tree.
//     U-2  `double` and `real` precision and the exponent at which scientific notation begins. C#
//          gives the shortest round trippable form, with E notation at the extremes: `1E-07`,
//          `1E+21`, `1.7976931348623157E+308`, `3.4028235E+38` (all measured).
//     U-3  The non finite doubles. `NaN`, `Infinity`, `-Infinity` and `-0` all render non empty
//          (measured), so H1 holds, but none of the four is a valid DataWindow numeric literal, so
//          each yields the same detectable `"?"`/`"!"` as DECISION 7's defect. PowerBuilder's
//          rendering of them is not shown anywhere in the tree.
//   None of the three can change WHICH branch is taken, so none of them can turn a null into a
//   value or a value into a null. They affect the text of a non null numeric literal only.
//
// ==================================================================================================
// WHAT THIS FILE DELIBERATELY DOES NOT DO
// ==================================================================================================
//
//   * It does not implement the six arm `COL_TYPE_*` dispatch of `n_cst_dwsvc_columnexp.sru`
//     `:L2344-L2359`, and it does not redeclare the `COL_TYPE_*` constants of
//     `n_cst_dwsvc.sru:L26-L32`. The dispatch belongs to the column expression engine and the
//     constants have a single home in `Domain/DataWindowServiceHost.cs`. This file is the nine
//     leaves that dispatch selects between, and it takes NO type dependency on `Domain/` or
//     `Expressions/` - which is precisely why it can be authored, compiled and tested before either
//     folder exists.
//   * It raises no dialog and formats no error. The `MessageBox` at
//     `n_cst_dwsvc_columnexp.sru:L2392`, and the other 27 like it in that object, belong to
//     `Expressions/ParseErrorFormatter.cs`, along with the preserved inconsistency that those
//     particular messages are hardcoded Chinese which does NOT route through `I18N` while the
//     equivalents elsewhere in this library do. None of that belongs here.
//   * It reads no clock, touches no file or socket, logs nothing, holds no state and calls no
//     localization. Every member is `static`, pure and deterministic, which is what lets the sibling
//     test project express table driven parity theories over it with no fixture at all.
//   * It is NOT promoted to `PowerFramework.Shared.Kernel`. It is the most generic looking file in
//     this folder and therefore the most tempting to share, but all thirteen
//     `pfw.datawindow.services` objects are assigned to DataServices and no shared behaviour may
//     cross a service boundary.
//   * It adds no package reference and no project reference. Its only dependencies are
//     `System.Globalization` from the base class library and the five sibling files in this folder,
//     which compile into this same assembly.
//
// ==================================================================================================

using System.Globalization;

namespace PowerFramework.DataServices.Validators;

/// <summary>
/// Renders a typed DataWindow cell value as the text of a DataWindow expression literal, or as the
/// matching null sentinel call when the value is absent. The port of
/// <c>ws_objects/pfw.datawindow.services.pbl.src/dwvaluetoexp.srf</c>.
/// </summary>
/// <remarks>
/// <para>
/// Nine overloads of <see cref="Convert(in decimal?)"/> reproduce the nine legacy prototypes at
/// <c>dwvaluetoexp.srf:L6-L14</c> one for one. Each returns either a literal or the sentinel
/// published by the corresponding producer in this folder, and none of them can return
/// <see langword="null"/> or the empty string - see DECISION 9 in the file header for why that is a
/// hard invariant rather than a courtesy.
/// </para>
/// <para>
/// THE CONSUMER CONTRACT, WHICH IS NOT SATISFIED BY A C# METHOD ALONE. The legacy function is
/// reached in two different ways, and the second one is easy to miss. Six call sites invoke it
/// directly and in a strongly typed way, from the column type dispatch at
/// <c>n_cst_dwsvc_columnexp.sru:L2346</c>, <c>:L2348</c>, <c>:L2350</c>, <c>:L2352</c>,
/// <c>:L2354</c> and <c>:L2356</c>. The seventh site names it FROM INSIDE AN EXPRESSION STRING:
/// <c>:L2390</c> reads <c>sVal = _of_Evaluate("dwValueToExp(" + sExp + ")",row)</c>, and
/// <c>_of_Evaluate</c> at <c>n_cst_dwsvc.sru:L217</c> hands that text straight to
/// <c>Describe("Evaluate(...)")</c>. PowerBuilder resolves the name because the object is an
/// application GLOBAL function. Nothing in .NET reproduces that automatically, so
/// <c>Expressions/DataWindowExpressionEvaluator.cs</c> MUST register this file as an expression
/// callable function under <see cref="FunctionName"/>, beside the five <c>dwNvl*</c> names, or the
/// variable expression path at <c>:L2388-L2396</c> will answer <c>"?"</c> and raise the error at
/// <c>:L2392</c>. Register <see cref="FunctionName"/> rather than a re-typed literal.
/// </para>
/// <para>
/// A repository wide search for the identifier across <c>ws_objects/**</c> returns exactly those
/// seven sites and nothing else, so the <c>int</c>, <c>long</c> and <c>real</c> overloads have no
/// PowerScript call site at all: <c>GetItemDecimal</c> selects the <see cref="decimal"/> body and
/// <c>GetItemNumber</c>, which is declared to return a double, selects the <see cref="double"/>
/// body. All three are ported regardless, because the nine member surface is the contract and
/// dropping an unreachable overload would narrow it - see DECISION 2.
/// </para>
/// </remarks>
public static class ValueToExpression
{
    // ----------------------------------------------------------------------------------------------
    //  THE PUBLISHED NAME                                     dwvaluetoexp.srf:L6-L14, columnexp:L2390
    // ----------------------------------------------------------------------------------------------
    //  Two spellings of the same name appear in the legacy tree, and the difference is not a typo.
    //  The prototypes at :L6-L14 are lower case (`dwvaluetoexp`) because that is how the PowerBuilder
    //  export writes a declaration; the DataWindow EXPRESSION at columnexp:L2390 spells it
    //  `dwValueToExp`. PowerScript resolves either, being case insensitive. The expression spelling
    //  is the one published here, because the expression text is the only place the spelling is
    //  observable - a DataWindow expression function table is looked up by the name as written.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The bare name of the legacy global function, spelled as a DataWindow expression spells it:
    /// <c>dwValueToExp</c>.
    /// </summary>
    /// <remarks>
    /// Source of truth: the expression text at
    /// <c>ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru:L2390</c>. Register
    /// this constant, never a re-typed literal, in the expression function table of
    /// <c>Expressions/DataWindowExpressionEvaluator.cs</c>; see the type level remarks for why that
    /// registration is mandatory rather than optional.
    /// </remarks>
    public const string FunctionName = "dwValueToExp";

    // ----------------------------------------------------------------------------------------------
    //  THE EXPRESSION GRAMMAR TOKENS                       dwvaluetoexp.srf:L48, L54, L60, L66
    // ----------------------------------------------------------------------------------------------
    //  Byte exact behavioural contract, declared once each per DECISION 10. Capitalisation is part
    //  of the contract, each opening token ends with a single quote, and the closing token is two
    //  characters, a quote then a parenthesis.
    // ----------------------------------------------------------------------------------------------

    /// <summary>The quote that opens and closes a string literal [dwvaluetoexp.srf:L48].</summary>
    private const string StringLiteralQuote = "'";

    /// <summary>The opening of a datetime literal, quote included [dwvaluetoexp.srf:L54].</summary>
    private const string DateTimeLiteralPrefix = "DateTime('";

    /// <summary>The opening of a date literal, quote included [dwvaluetoexp.srf:L60].</summary>
    private const string DateLiteralPrefix = "Date('";

    /// <summary>The opening of a time literal, quote included [dwvaluetoexp.srf:L66].</summary>
    private const string TimeLiteralPrefix = "Time('";

    /// <summary>
    /// The closing of every wrapped temporal literal: a quote then a parenthesis
    /// [dwvaluetoexp.srf:L54, L60, L66].
    /// </summary>
    private const string WrappedLiteralSuffix = "')";

    // ==============================================================================================
    //  THE FIVE NUMERIC OVERLOADS                                       dwvaluetoexp.srf:L17-L45
    // ==============================================================================================
    //
    //  All five legacy bodies are `sVal = String(val)` followed by the same sentinel substitution,
    //  and all five are kept as separate members per DECISION 2.
    //
    //  DEFECT 2, REPRODUCED - THE NUMERIC SENTINEL IS TYPE WIDENED, AND THERE IS EXACTLY ONE OF THEM.
    //  Every one of the five emits `dwNvlNumber()`, and that function is DECLARED TO RETURN AN
    //  INTEGER: `dwnvlnumber.srf:L6` reads `global function integer dwnvlnumber ()`. So a null
    //  `decimal` at `:L19`, a null `double` at `:L37` and a null `real` at `:L43` each receive an
    //  INTEGER typed null, widened by the expression engine rather than typed to the column. That is
    //  measurably a legacy choice and not an accident of this port: the four non numeric sentinels
    //  are all type matched to their overload - `dwnvlstring.srf:L6` returns `string`,
    //  `dwnvldate.srf:L6` returns `date`, `dwnvldatetime.srf:L6` returns `datetime` and
    //  `dwnvltime.srf:L6` returns `time` - so the numeric family is the only one that widens.
    //  Preserve it. Do NOT introduce a per type numeric sentinel: `NumberValidator` publishes one
    //  `NullLiteralExpression` and all five overloads below return that same constant.
    // ==============================================================================================

    /// <summary>
    /// Renders a <c>decimal</c> cell value as a DataWindow numeric literal, or
    /// <see cref="NumberValidator.NullLiteralExpression"/> when it is absent
    /// [dwvaluetoexp.srf:L17-L21].
    /// </summary>
    /// <param name="val">
    /// The value to render. <see langword="null"/> selects the sentinel. The legacy type is
    /// PowerBuilder <c>decimal</c>, a fixed point type, which is why the parameter is
    /// <see cref="decimal"/> and not a binary floating point type.
    /// </param>
    /// <returns>
    /// The invariant text of the value, or <see cref="NumberValidator.NullLiteralExpression"/>.
    /// Never <see langword="null"/> and never empty, per DECISION 9.
    /// </returns>
    /// <remarks>
    /// The rendering of a non null value is <c>UNVERIFIED-FROM-REPOSITORY (U-1)</c>: C# preserves
    /// the scale of a <see cref="decimal"/>, so <c>1.50m</c> renders as <c>1.50</c> and not
    /// <c>1.5</c>, and nothing in the legacy tree shows whether <c>String(dec)</c> trims. Pin it
    /// against the behavioural oracle. The BRANCH taken is unaffected either way.
    /// </remarks>
    public static string Convert(in decimal? val)
    {
        // THE HAZARD, GUARDED [dwvaluetoexp.srf:L18-L19]. The legacy assigns `String(val)` first and
        // then tests the RESULT, which reaches the sentinel only because PowerBuilder's
        // `String(null)` is itself null. In C# a nullable's conversion of an absent value yields the
        // EMPTY STRING, not null, so a result test here would return "" and violate H1 - and "" is
        // the caller's abort sentinel [n_cst_dwsvc_columnexp.sru:L2186]. The INPUT is tested first.
        if (!val.HasValue)
        {
            // dwvaluetoexp.srf:L19 - `sVal = "dwNvlNumber()"`. Single sourced per DECISION 5, and
            // deliberately the same integer declared sentinel as the other four numeric overloads
            // per DEFECT 2 above.
            return NumberValidator.NullLiteralExpression;
        }

        // dwvaluetoexp.srf:L18 - `sVal = String(val)`, routed through the file's single conversion
        // site so the culture is pinned once (DECISION 6).
        return FormatInvariant(val.Value);
    }

    /// <summary>
    /// Renders an <c>integer</c> cell value as a DataWindow numeric literal, or
    /// <see cref="NumberValidator.NullLiteralExpression"/> when it is absent
    /// [dwvaluetoexp.srf:L23-L27].
    /// </summary>
    /// <param name="val">
    /// The value to render. <see langword="null"/> selects the sentinel. PowerBuilder
    /// <c>integer</c> is 16 BIT, so the parameter is <see cref="short"/>; the wider
    /// <c>long</c> prototype has its own overload immediately below.
    /// </param>
    /// <returns>
    /// The invariant text of the value, or <see cref="NumberValidator.NullLiteralExpression"/>.
    /// Never <see langword="null"/> and never empty, per DECISION 9.
    /// </returns>
    /// <remarks>
    /// Integral rendering carries no precision question, so no <c>UNVERIFIED</c> marker applies
    /// here: every value in the 16 bit range has exactly one invariant decimal spelling, and the
    /// only culture sensitive element is the negative sign, which
    /// <see cref="CultureInfo.InvariantCulture"/> pins.
    /// </remarks>
    public static string Convert(in short? val)
    {
        // THE HAZARD, GUARDED [dwvaluetoexp.srf:L24-L25]. Same divergence as the decimal overload:
        // the legacy result test relies on `String(null)` being null and C# has no equivalent, so
        // the INPUT is tested before anything is built.
        if (!val.HasValue)
        {
            // dwvaluetoexp.srf:L25 - `sVal = "dwNvlNumber()"`, the shared integer declared sentinel.
            return NumberValidator.NullLiteralExpression;
        }

        // dwvaluetoexp.srf:L24 - `sVal = String(val)`.
        return FormatInvariant(val.Value);
    }

    /// <summary>
    /// Renders a <c>long</c> cell value as a DataWindow numeric literal, or
    /// <see cref="NumberValidator.NullLiteralExpression"/> when it is absent
    /// [dwvaluetoexp.srf:L29-L33].
    /// </summary>
    /// <param name="val">
    /// The value to render. <see langword="null"/> selects the sentinel. PowerBuilder <c>long</c> is
    /// 32 BIT and maps to <see cref="long"/> under the transformation rules, so this parameter is
    /// WIDER than the legacy type. Widening cannot change the emitted text for any value the legacy
    /// could hold, and it cannot change which branch is taken.
    /// </param>
    /// <returns>
    /// The invariant text of the value, or <see cref="NumberValidator.NullLiteralExpression"/>.
    /// Never <see langword="null"/> and never empty, per DECISION 9.
    /// </returns>
    /// <remarks>
    /// This overload has NO PowerScript call site anywhere in <c>ws_objects/**</c>: the six typed
    /// call sites at <c>n_cst_dwsvc_columnexp.sru:L2346-L2356</c> reach the <see cref="decimal"/>,
    /// <see cref="double"/>, <see cref="string"/>, <see cref="DateTime"/>, <see cref="DateOnly"/>
    /// and <see cref="TimeOnly"/> bodies. It is ported anyway, per DECISION 2: the nine member
    /// surface is the contract and an unreachable prototype is still a prototype.
    /// </remarks>
    public static string Convert(in long? val)
    {
        // THE HAZARD, GUARDED [dwvaluetoexp.srf:L30-L31]. The legacy tests the concatenation result;
        // C# cannot, because an absent nullable converts to "" rather than to null.
        if (!val.HasValue)
        {
            // dwvaluetoexp.srf:L31 - `sVal = "dwNvlNumber()"`, the shared integer declared sentinel.
            return NumberValidator.NullLiteralExpression;
        }

        // dwvaluetoexp.srf:L30 - `sVal = String(val)`.
        return FormatInvariant(val.Value);
    }

    /// <summary>
    /// Renders a <c>double</c> cell value as a DataWindow numeric literal, or
    /// <see cref="NumberValidator.NullLiteralExpression"/> when it is absent
    /// [dwvaluetoexp.srf:L35-L39].
    /// </summary>
    /// <param name="val">
    /// The value to render. <see langword="null"/> selects the sentinel. PowerBuilder <c>double</c>
    /// is the 8 BYTE binary floating point type, so the parameter is <see cref="double"/>.
    /// </param>
    /// <returns>
    /// The invariant text of the value, or <see cref="NumberValidator.NullLiteralExpression"/>.
    /// Never <see langword="null"/> and never empty, per DECISION 9.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the overload the column type dispatch actually reaches for an integer column, because
    /// <c>n_cst_dwsvc_columnexp.sru:L2348</c> passes <c>GetItemNumber</c>, which PowerBuilder
    /// declares to return a double. So merging the five numeric overloads would not merely tidy the
    /// file, it would move that call site onto a different conversion.
    /// </para>
    /// <para>
    /// <c>UNVERIFIED-FROM-REPOSITORY (U-2)</c>: the rendering is the shortest round trippable form,
    /// which switches to scientific notation at the extremes - <c>1E-07</c>, <c>1E+21</c> and
    /// <c>1.7976931348623157E+308</c> were measured - and the legacy tree shows no formatted double
    /// literal to pin the threshold against. <c>UNVERIFIED-FROM-REPOSITORY (U-3)</c>: the four non
    /// finite cases render <c>NaN</c>, <c>Infinity</c>, <c>-Infinity</c> and <c>-0</c>, all non
    /// empty so H1 holds, but none is a valid DataWindow numeric literal, so each produces the same
    /// detectable <c>"?"</c>/<c>"!"</c> answer as the unescaped quote defect. Both are
    /// characterization items; neither can change which branch is taken.
    /// </para>
    /// </remarks>
    public static string Convert(in double? val)
    {
        // THE HAZARD, GUARDED [dwvaluetoexp.srf:L36-L37]. Result testing cannot be transliterated:
        // PowerBuilder's `String(null)` is null, C#'s conversion of an absent double is "".
        if (!val.HasValue)
        {
            // dwvaluetoexp.srf:L37 - `sVal = "dwNvlNumber()"`. DEFECT 2 in its clearest form: a
            // null 8 byte float receives an INTEGER declared sentinel [dwnvlnumber.srf:L6].
            return NumberValidator.NullLiteralExpression;
        }

        // dwvaluetoexp.srf:L36 - `sVal = String(val)`.
        return FormatInvariant(val.Value);
    }

    /// <summary>
    /// Renders a <c>real</c> cell value as a DataWindow numeric literal, or
    /// <see cref="NumberValidator.NullLiteralExpression"/> when it is absent
    /// [dwvaluetoexp.srf:L41-L45].
    /// </summary>
    /// <param name="val">
    /// The value to render. <see langword="null"/> selects the sentinel. PowerBuilder <c>real</c> is
    /// the 4 BYTE binary floating point type, so the parameter is <see cref="float"/> and NOT
    /// <see cref="double"/>.
    /// </param>
    /// <returns>
    /// The invariant text of the value, or <see cref="NumberValidator.NullLiteralExpression"/>.
    /// Never <see langword="null"/> and never empty, per DECISION 9.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE WIDTH IS OBSERVABLE HERE, WHICH IS THE CONCRETE REASON DECISION 2 REFUSES TO MERGE THE
    /// NUMERIC FIVE. A 4 byte and an 8 byte float carrying the same nominal value render
    /// DIFFERENTLY, because each is rendered to its own shortest round trippable form: <c>0.1f</c>
    /// emits <c>0.1</c> while the same quantity widened to a double emits
    /// <c>0.10000000149011612</c> (both measured). Routing a <c>real</c> through the
    /// <see cref="double"/> overload would therefore change the emitted literal, silently.
    /// </para>
    /// <para>
    /// <c>UNVERIFIED-FROM-REPOSITORY (U-2)</c> applies as it does to <see cref="double"/>:
    /// <c>3.4028235E+38</c> and <c>1E-45</c> were measured at the extremes. This overload has no
    /// PowerScript call site, and is ported regardless per DECISION 2.
    /// </para>
    /// </remarks>
    public static string Convert(in float? val)
    {
        // THE HAZARD, GUARDED [dwvaluetoexp.srf:L42-L43]. As above: the legacy result test has no
        // C# equivalent, so the input is tested first.
        if (!val.HasValue)
        {
            // dwvaluetoexp.srf:L43 - `sVal = "dwNvlNumber()"`. DEFECT 2 again: a null 4 byte float
            // receives the INTEGER declared sentinel [dwnvlnumber.srf:L6].
            return NumberValidator.NullLiteralExpression;
        }

        // dwvaluetoexp.srf:L42 - `sVal = String(val)`. `FormatInvariant` is generic and the argument
        // is a `float`, so this renders at FLOAT precision; there is no widening to double anywhere
        // on this path.
        return FormatInvariant(val.Value);
    }

    // ==============================================================================================
    //  THE STRING OVERLOAD                                              dwvaluetoexp.srf:L47-L51
    // ==============================================================================================
    //
    //  DEFECT 1, REPRODUCED - NO ESCAPING OF EMBEDDED SINGLE QUOTES.
    //  `dwvaluetoexp.srf:L48` is exactly `sVal = "'" + val + "'"`. There is no escaping, no
    //  doubling, no stripping and no rejection, so a value that contains a single quote produces a
    //  MALFORMED expression: `O'Brien` becomes `'O'Brien'`, which a DataWindow expression parses as
    //  the literal `'O'` followed by the unresolvable token `Brien'`.
    //
    //  DO NOT FIX THIS. Behaviour preservation forbids correcting a legacy defect, and this one is
    //  the same class of unescaped interpolation that the plan records, and documents rather than
    //  repairs, elsewhere in the estate. Two further reasons it must stay:
    //    * It is DETECTABLE, not silent. The malformed expression reaches
    //      `Describe("Evaluate(...)")` through `_of_Evaluate` [n_cst_dwsvc.sru:L217], which answers
    //      `"?"` or `"!"`, and the caller raises an expression error at
    //      `n_cst_dwsvc_columnexp.sru:L2391-L2392`. Pre sanitising the quote away would convert a
    //      visible error into a DIFFERENT, quietly successful result - a behavioural change.
    //    * The producing side already declined to do it. `Validators/StringValidator.cs` records
    //      that escaping is this file's decision to make and that escaping there would change the
    //      value a cell holds. Adding it here instead would defeat that.
    //
    //  This is also the one overload with no conversion at all: the parameter is already a string,
    //  so no culture, no format and no `IFormattable` is involved on this path.
    // ==============================================================================================

    /// <summary>
    /// Renders a <c>string</c> cell value as a quoted DataWindow string literal, or
    /// <see cref="StringValidator.NullLiteralExpression"/> when it is absent
    /// [dwvaluetoexp.srf:L47-L51].
    /// </summary>
    /// <param name="val">
    /// The value to render. <see langword="null"/> selects the sentinel. The value is wrapped
    /// VERBATIM: no escaping, no trimming, no normalisation and no length check is applied to it.
    /// </param>
    /// <returns>
    /// The value wrapped in single quotes, or <see cref="StringValidator.NullLiteralExpression"/>.
    /// Never <see langword="null"/> and never empty: the shortest possible result is the two
    /// character literal <c>''</c>, produced for an empty input, which is a well formed empty string
    /// literal and NOT the caller's abort sentinel. See DECISION 9.
    /// </returns>
    /// <remarks>
    /// A value containing a single quote yields a malformed expression, verbatim from
    /// <c>dwvaluetoexp.srf:L48</c>. That is a PRESERVED LEGACY DEFECT and must not be repaired; the
    /// block comment above this member gives the full reasoning and the observable consequence.
    /// </remarks>
    public static string Convert(in string? val)
    {
        // THE HAZARD, GUARDED, AND THIS IS THE WORST OF THE NINE [dwvaluetoexp.srf:L48-L49]. In
        // PowerScript `"'" + null + "'"` is NULL, so `IsNull(sVal)` at :L49 selects the sentinel. In
        // C# the identical concatenation yields `''`, a VALID two character empty string literal, so
        // a transliteration would return a wrong value with no error anywhere - unlike the temporal
        // overloads, whose naive output is at least malformed enough to be detected. The INPUT is
        // therefore tested first, and `''` is reserved for a genuinely EMPTY string.
        if (val is null)
        {
            // dwvaluetoexp.srf:L49 - `sVal = "dwNvlString()"`. Single sourced per DECISION 5. Note
            // this sentinel IS type matched, unlike the numeric one: `dwnvlstring.srf:L6` declares a
            // `string` return.
            return StringValidator.NullLiteralExpression;
        }

        // dwvaluetoexp.srf:L48 - `sVal = "'" + val + "'"`, reproduced operand for operand. String
        // `+` performs no formatting and consults no culture, which is why it is used here in place
        // of interpolation. DEFECT 1: `val` is interpolated raw, quotes and all.
        return StringLiteralQuote + val + StringLiteralQuote;
    }

    // ==============================================================================================
    //  THE THREE TEMPORAL OVERLOADS                                     dwvaluetoexp.srf:L53-L69
    // ==============================================================================================
    //
    //  Each wraps its value in a DataWindow conversion call - `DateTime('...')`, `Date('...')`,
    //  `Time('...')` - so the expression engine gets a typed literal rather than a bare string. The
    //  wrapper spellings are byte exact contract, including the capital letters (DECISION 10).
    //
    //  THE INNER TEXT IS NOT FORMATTED HERE. Each of the three sibling producers publishes a
    //  culture pinned formatter and the canonical format constant behind it, and this file composes
    //  the wrapper around that call:
    //
    //      DateTimeValidator.FormatExpressionValue  <- ExpressionValueFormat  "yyyy-MM-dd HH:mm:ss"
    //      DateValidator.FormatValue                <- CanonicalValueFormat   "yyyy-MM-dd"
    //      TimeValidator.Format                     <- CanonicalFormat        "HH:mm:ss"
    //
    //  That is DECISION 5 applied to the format as well as to the sentinel: this file carries no
    //  format string of its own, so the emitted text and the text those producers' own round trip
    //  coercions accept cannot drift apart. Each of the three formats is itself an
    //  UNVERIFIED-FROM-REPOSITORY characterization item, recorded on the constant in the producing
    //  file; the annotation is not duplicated here beyond the pointer, because a second copy would
    //  be the very drift DECISION 5 exists to prevent.
    // ==============================================================================================

    /// <summary>
    /// Renders a <c>datetime</c> cell value as a DataWindow <c>DateTime('...')</c> literal, or
    /// <see cref="DateTimeValidator.NullLiteralExpression"/> when it is absent
    /// [dwvaluetoexp.srf:L53-L57].
    /// </summary>
    /// <param name="val">The value to render. <see langword="null"/> selects the sentinel.</param>
    /// <returns>
    /// <c>DateTime('</c>, the value in
    /// <see cref="DateTimeValidator.ExpressionValueFormat"/>, then <c>')</c>; or
    /// <see cref="DateTimeValidator.NullLiteralExpression"/>. Never <see langword="null"/> and never
    /// empty, per DECISION 9.
    /// </returns>
    /// <remarks>
    /// The inner text comes from <see cref="DateTimeValidator.FormatExpressionValue(DateTime)"/>, so
    /// it is culture pinned at its own site and this file adds no conversion. The
    /// <see cref="DateTime.Kind"/> of the argument is neither read nor normalised, matching a zone
    /// less PowerBuilder <c>datetime</c>.
    /// </remarks>
    public static string Convert(in DateTime? val)
    {
        // THE HAZARD, GUARDED [dwvaluetoexp.srf:L54-L55]. This is the textbook case: in PowerScript
        // `"DateTime('" + String(null) + "')"` is NULL for the whole expression, so :L55 selects the
        // sentinel. In C# the same concatenation yields the malformed `DateTime('')`, which would
        // reach `Describe("Evaluate(...)")` and come back as `"?"`/`"!"` far from its cause. The
        // INPUT is tested first and the wrapper is built only on the non null path.
        if (!val.HasValue)
        {
            // dwvaluetoexp.srf:L55 - `sVal = "dwNvlDateTime()"`. Single sourced per DECISION 5;
            // type matched, since `dwnvldatetime.srf:L6` declares a `datetime` return.
            return DateTimeValidator.NullLiteralExpression;
        }

        // dwvaluetoexp.srf:L54 - `sVal = "DateTime('" + String(val) + "')"`. Three string operands,
        // so no formatting and no culture is involved in the concatenation itself.
        return DateTimeLiteralPrefix
            + DateTimeValidator.FormatExpressionValue(val.Value)
            + WrappedLiteralSuffix;
    }

    /// <summary>
    /// Renders a <c>date</c> cell value as a DataWindow <c>Date('...')</c> literal, or
    /// <see cref="DateValidator.NullLiteralExpression"/> when it is absent
    /// [dwvaluetoexp.srf:L59-L63].
    /// </summary>
    /// <param name="val">The value to render. <see langword="null"/> selects the sentinel.</param>
    /// <returns>
    /// <c>Date('</c>, the value in <see cref="DateValidator.CanonicalValueFormat"/>, then <c>')</c>;
    /// or <see cref="DateValidator.NullLiteralExpression"/>. Never <see langword="null"/> and never
    /// empty, per DECISION 9.
    /// </returns>
    /// <remarks>
    /// The parameter is <see cref="DateOnly"/> and nullable, both deliberately.
    /// <see cref="DateOnly.MinValue"/> is a VALUE and renders as <c>Date('0001-01-01')</c>; only
    /// <see langword="null"/> reaches the sentinel. <c>Validators/DateValidator.cs</c> records the
    /// same requirement from the producing side, because collapsing the two would make an absent
    /// date indistinguishable from a real one.
    /// </remarks>
    public static string Convert(in DateOnly? val)
    {
        // THE HAZARD, GUARDED [dwvaluetoexp.srf:L60-L61]. PowerScript propagates the null out of the
        // concatenation and :L61 selects the sentinel; C# would emit the malformed `Date('')`.
        if (!val.HasValue)
        {
            // dwvaluetoexp.srf:L61 - `sVal = "dwNvlDate()"`. Single sourced per DECISION 5;
            // type matched, since `dwnvldate.srf:L6` declares a `date` return.
            return DateValidator.NullLiteralExpression;
        }

        // dwvaluetoexp.srf:L60 - `sVal = "Date('" + String(val) + "')"`.
        return DateLiteralPrefix
            + DateValidator.FormatValue(val.Value)
            + WrappedLiteralSuffix;
    }

    /// <summary>
    /// Renders a <c>time</c> cell value as a DataWindow <c>Time('...')</c> literal, or
    /// <see cref="TimeValidator.NullLiteralExpression"/> when it is absent
    /// [dwvaluetoexp.srf:L65-L69].
    /// </summary>
    /// <param name="val">The value to render. <see langword="null"/> selects the sentinel.</param>
    /// <returns>
    /// <c>Time('</c>, the value in <see cref="TimeValidator.CanonicalFormat"/>, then <c>')</c>; or
    /// <see cref="TimeValidator.NullLiteralExpression"/>. Never <see langword="null"/> and never
    /// empty, per DECISION 9.
    /// </returns>
    /// <remarks>
    /// The inner text comes from <see cref="TimeValidator.Format(TimeOnly)"/>, which renders 24 hour
    /// and therefore cannot emit an AM or PM designator whatever the host locale. Any sub second
    /// component of the value is truncated by that formatter's canonical format, which is a recorded
    /// decision on the producing side and a characterization item there.
    /// </remarks>
    public static string Convert(in TimeOnly? val)
    {
        // THE HAZARD, GUARDED [dwvaluetoexp.srf:L66-L67]. PowerScript propagates the null and :L67
        // selects the sentinel; C# would emit the malformed `Time('')`.
        if (!val.HasValue)
        {
            // dwvaluetoexp.srf:L67 - `sVal = "dwNvlTime()"`. Single sourced per DECISION 5;
            // type matched, since `dwnvltime.srf:L6` declares a `time` return.
            return TimeValidator.NullLiteralExpression;
        }

        // dwvaluetoexp.srf:L66 - `sVal = "Time('" + String(val) + "')"`.
        return TimeLiteralPrefix
            + TimeValidator.Format(val.Value)
            + WrappedLiteralSuffix;
    }

    // ==============================================================================================
    //  THE SINGLE CONVERSION SITE                            dwvaluetoexp.srf:L18, L24, L30, L36, L42
    // ==============================================================================================
    //
    //  DECISION 6 lives here. This is the ONLY place in the file where a value becomes text, and it
    //  is the only place `CultureInfo.InvariantCulture` is named, so the culture cannot be forgotten
    //  at one of five sites. The three temporal overloads do not pass through it: their producers
    //  already pin the culture at their own formatter, and re-pinning it here would duplicate the
    //  format strings this file deliberately does not own.
    //
    //  It is private and it lives IN THIS FILE rather than in a seventh file under Validators/,
    //  because the target structure fixes this folder at six files and this helper is an
    //  implementation detail of one of them, not a shared service.
    // ==============================================================================================

    /// <summary>
    /// Renders a value type as text under <see cref="CultureInfo.InvariantCulture"/> using its
    /// general format - the substitute for PowerBuilder's no mask <c>String(val)</c> primitive.
    /// </summary>
    /// <typeparam name="T">
    /// The value type to render. Constrained to <see cref="IFormattable"/> so the call below binds to
    /// the interface implementation directly, with no boxing and no ambient culture path.
    /// </typeparam>
    /// <param name="value">The value to render. Never an absent nullable: each caller has already
    /// tested its input and returned the sentinel, so this method is only ever reached with a real
    /// value and has no null branch of its own.</param>
    /// <returns>The invariant text of <paramref name="value"/>.</returns>
    /// <remarks>
    /// <para>
    /// The format argument is <see langword="null"/>, which selects each type's general format: the
    /// shortest round trippable form for <see cref="double"/> and <see cref="float"/>, and the scale
    /// preserving form for <see cref="decimal"/>. That is the closest available analogue to
    /// <c>String(val)</c> called with no format mask, which is how all five numeric legacy bodies
    /// call it.
    /// </para>
    /// <para>
    /// <c>PowerFramework.Shared.Kernel.Formatting.Sprintf</c> is deliberately NOT used as the
    /// substitute. It ports <c>sprintf.srf</c> and <c>formatretcode.srf</c>, whose grammar is the
    /// PowerBuilder format mask form; a maskless <c>String()</c> is a different primitive, and there
    /// is no port of that primitive anywhere in the plan. Pressing one into service as the other
    /// would be a behavioural guess dressed as reuse, so this file owns its own conversion and takes
    /// no dependency on the shared kernel.
    /// </para>
    /// <para>
    /// The parameter is taken BY VALUE and not by <c>in</c>, unlike the nine public entry points.
    /// The <c>readonly</c> to <c>in</c> mapping of DECISION 4 describes the legacy signatures being
    /// ported; this helper has no legacy counterpart. By value is also the correct choice on the
    /// merits here: <typeparamref name="T"/> is an unconstrained value type, so the compiler cannot
    /// know it is a readonly struct and would have to take a defensive copy before the interface
    /// call anyway.
    /// </para>
    /// </remarks>
    private static string FormatInvariant<T>(T value)
        where T : struct, IFormattable
    {
        return value.ToString(format: null, formatProvider: CultureInfo.InvariantCulture);
    }
}
