// ==================================================================================================
//  DataWindowExpressionEvaluator - the reimplementation of PowerBuilder's
//  Describe("Evaluate(<expression>,<row>)") facility.
// ==================================================================================================
//
//  WHY THIS FILE IS A REIMPLEMENTATION AND NOT A SUBSTITUTION (constraint C-K).
//
//  Every other native surface in the in-scope set has somewhere to land. The bit primitives become C#
//  operators, the cryptographic surface becomes System.Security.Cryptography, the SQLite binding
//  becomes Microsoft.Data.Sqlite, the container classes become BCL collections. This one has nowhere
//  to land at all:
//
//    * It is NOT a PBNI binding, so there is no DLL to reimplement against. It is a pseudo-property of
//      the PowerBuilder DataWindow control, reached by handing the string "Evaluate(...)" to
//      Describe(), and the whole of its behaviour lives inside the closed DataWindow runtime.
//    * There is no BCL equivalent. The .NET base class library contains no DataWindow, no DataWindow
//      expression language, and no general expression evaluator of any kind.
//    * No package may be added (AAP 0.5.3). The acceptance criterion for this refactor is BYTE-EXACT
//      output against a behavioural oracle, and a general-purpose expression library would have its
//      own operator precedence, its own null semantics and its own number formatting - three places
//      where "close enough" is indistinguishable from a silent regression. The AAP records the same
//      build-not-buy decision for the SQL clause model, and for the same reason.
//
//  AAP 0.4.2.5 and 0.6.5 therefore both name this THE LARGEST SINGLE NET-NEW IMPLEMENTATION OBLIGATION
//  IN THE REFACTOR, and 0.6.5 adds that it is "the substrate the expansion engine sits on top of".
//  Expressions/ColumnExpressionEngine.cs, Services/ColumnSortModel.cs,
//  Services/ContextMenuModel.cs and Services/DropDownSearchModel.cs all reach the DataWindow's
//  expression language exclusively through this type.
//
// --------------------------------------------------------------------------------------------------
//  THE SIX LEGACY CALL SITES, AND WHAT EACH ONE PROVES
// --------------------------------------------------------------------------------------------------
//
//  ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc.sru
//    :L195  Describe("Evaluate("+sExp+","+String(row)+")")            in _of_GetItemProp
//    :L217  Describe("Evaluate(~""+exp+"~","+String(row)+")")         in _of_Evaluate(exp,row)
//    :L220  return _of_Evaluate(exp,0)                                 the one-argument arity
//    :L289  Describe("Evaluate('LookUpDisplay("+colName+")',"+string(row)+")")
//    :L344  Describe("Evaluate("+sExp+",0)")                          in _of_GetColumnProp
//    :L782  Describe("Evaluate("+sExp+",0)")                          in _of_GetDWOProp
//  ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_contextmenu.sru
//    :L534 :L541 :L617 :L624   Describe("Evaluate('"+colName+"',"+String(row)+")")
//    :L543 :L626               Describe("Evaluate('LookUpDisplay("+colName+")',"+string(row)+")")
//    :L1130 :L1296             _of_Evaluate(objectName)               row-less, so row 0
//    :L1132 :L1298             _of_Evaluate("Max(Len(LookUpDisplay("+obj+")))")
//    :L1133 :L1299             _of_Evaluate("Max(LenA(LookUpDisplay("+obj+")))")
//    :L1183 :L1349             a nested Describe("Evaluate('col',<row>)") INSIDE an expression
//  ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru
//    :L750                     sVal = _of_Evaluate(sExp,row)          the column-expression engine
//    :L2390                    _of_Evaluate("dwValueToExp(" + sExp + ")",row)
//  ws_objects/pfw.tests.pbl.src/dw_sqlite.srd
//    :L27                      compute expression="sum(salary for page)" format="[GENERAL]"
//  ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_dropdownsearch.sru
//    :L323                     " OR PinyinFirstLetterLike(<col>,'<py>',7)" spliced into a filter
//
//  THREE QUOTING CONVENTIONS, AND A FOURTH SHAPE THE BRIEF DID NOT NAME. The brief lists :L195,
//  :L344 and :L782 as "unquoted" because their format strings carry no quote character. At RUNTIME
//  they are not unquoted: :L194, :L343 and :L781 each execute
//
//      sExp = "~"" + Mid(sExp,nPos + 1)
//
//  which prepends ONE double quote and never appends a closing one, so the payload those three sites
//  hand to Describe is `"<expression>` - a LONE LEADING QUOTE. NormaliseExpressionPayload therefore
//  strips an unbalanced leading quote as well as a balanced pair. Handling only the balanced pair
//  would leave three of the six sites carrying a stray quote into the tokenizer, which is exactly the
//  partial failure the brief warns is hard to spot.
//
//  THE NESTED Describe IS RECURSIVE EVALUATION, NOT A SPECIAL CASE. :L1183 and :L1349 build
//
//      String({1}) <> if(GetRow() < RowCount(),Describe(~"Evaluate('{1}',~" + String(GetRow() + 1) + ~")~"),'')
//
//  so `Describe` is a callable INSIDE an expression whose argument is itself an Evaluate invocation.
//  The registered Describe function routes an Evaluate-shaped argument back through this evaluator and
//  anything else through DataWindowServiceHost.Describe - which is precisely the division of labour
//  the real runtime has, where one Describe serves both properties and expressions.
//
// --------------------------------------------------------------------------------------------------
//  THREE OUTCOMES, NEVER CONFLATED
// --------------------------------------------------------------------------------------------------
//
//    "!"  MALFORMED. The expression does not parse, or names something that does not exist.
//         Tested at n_cst_dwsvc_columnexp.sru:L761 and :L2391, and again at :L1141/:L1195/:L1307.
//    "?"  UNDETERMINED - a property whose value the DataWindow cannot determine. Tested alongside "!"
//         at n_cst_dwsvc.sru:L587, :L590, :L598, :L599 and :L775, all of which are PROPERTY reads. It
//         reaches a caller through TryDescribe's classification of a host answer, which is where the
//         oracle tests for it; a parsed EXPRESSION has no undetermined outcome, because it either
//         produces a value - possibly null - or is malformed. See TryDescribe for the full argument.
//    ""   ABORT, and it is a THIRD outcome rather than an empty value. Confirmed at four independent
//         lines: n_cst_dwsvc_columnexp.sru:L745 (`if sExp = "" then return false`), :L2183 and :L2186
//         (`if sVal = "" then return ""`, propagating the abort upward) and :L2358
//         (`case else return ""`). Validators/ValueToExpression.cs carries a hard invariant never to
//         return "" for the same reason.
//
//  HOW A GENUINELY EMPTY DISPLAY VALUE STAYS DISTINGUISHABLE FROM AN ABORT. The legacy conflates the
//  two - its only channel is one string - and this file must reproduce that at the boundary while
//  keeping the distinction usable internally. It does so by separating the two surfaces:
//
//    * Evaluate(...) returns the legacy string. An empty successful value and an abort both render as
//      "", byte for byte as the oracle renders them. Nothing is added, nothing is removed.
//    * TryEvaluate(...) returns DataWindowExpressionResult, on which Outcome is Value for the empty
//      success and Abort for the abort, IsNullValue separates a null from an empty string, and Error
//      carries the structured reason. Callers that need the distinction - the column-expression
//      engine's :L745 guard, and the published contract - read the result; callers reproducing the
//      legacy read the string.
//
//  Every failure becomes a structured ExpressionParseError. There is no MessageBox, no dialog and no
//  UI type anywhere in this file (constraints C-B, C-D, AAP 0.2.1.3 Correction 5).
//
// --------------------------------------------------------------------------------------------------
//  DECISIONS RECORDED HERE SO THEY ARE NOT REDISCOVERED AS SURPRISES (constraint C-K)
// --------------------------------------------------------------------------------------------------
//
//  D1. FUNCTION AND COLUMN NAMES ARE MATCHED CASE-INSENSITIVELY, ORDINALLY. PowerBuilder is a
//      case-insensitive language and its DataWindow expression parser is case-insensitive too; the
//      legacy relies on it directly, spelling the same facility `_of_Evaluate` and `Evaluate`, and
//      comparing object names with `Lower(...) = Lower(...)` at n_cst_dwsvc.sru:L483. The comparison
//      is ORDINAL-ignore-case rather than culture-aware because a DataWindow identifier is structural
//      ASCII text, not localized prose: a culture-aware fold would make `LEN` stop resolving under a
//      Turkish culture, which is a defect the legacy cannot have. `Lower()`'s own culture sensitivity
//      is unobservable over the identifiers this file actually meets.
//  D2. THE OPERATOR GRAMMAR IS COMPLETE; THE FUNCTION ROSTER IS EVIDENCE-BOUNDED. Column expressions
//      are supplied by the application through of_SetExp, so they are arbitrary DataWindow
//      expressions and the grammar must accept the whole operator set or it would reject valid input.
//      Functions are the opposite: each registration below cites a legacy call site, and no function
//      the legacy never uses is registered (constraint C-B).
//  D3. NUMERIC PROMOTION IS Long -> Double -> Decimal, DECIMAL DOMINANT. No in-scope expression mixes
//      a double with a decimal, so the choice is unobservable; it is made in favour of decimal because
//      the parity criterion is byte-exact output and only decimal preserves the base-ten scale a
//      DataWindow `decimal(n)` column carries - dw_sqlite.srd:L12 declares `decimal(2)`. An overflow
//      converting a double to decimal answers "!" rather than throwing.
//  D4. A COLUMN REFERENCE OUTSIDE A ROW - row 0, or a row past the buffer - EVALUATES TO NULL, not to
//      a sentinel. The legacy evaluates property expressions with row 0 at :L344 and :L782 and with
//      the one-argument arity at :L1130, :L1132, :L1133, :L1296, :L1298 and :L1299, and those
//      expressions reference columns. Answering "!" there would make every one of those six sites fail,
//      which the legacy plainly does not do. A null column value is an ordinary outcome in any case -
//      all six typed getters on IDataWindowChild are nullable by contract.
//  D5. AN AGGREGATE WITH NO `for` CLAUSE COVERS ALL ROWS. :L1132 evaluates
//      Max(Len(LookUpDisplay(col))) through the row-0 arity and uses the answer as a column width, so
//      it must span the buffer rather than one row.
//  D6. GetRow() ALWAYS REPORTS THE HOST'S CURRENT ROW, even while an aggregate is iterating. It is a
//      DataWindow-level function, and :L1183 depends on that: GetRow() + 1 there means "the row after
//      the cursor", not "the row after the one being aggregated".
//  D7. THREE DEFINED NARROWINGS. AAP 0.1.5 requires that a behaviour which cannot be reproduced be
//      narrowed with a defined error and never widened with a guess. The three are:
//        (a) `for page` needs the page's row range, and a page boundary is computed from band heights
//            and print margins - presentation, owned by the deferred DesignSystem and reserved at
//            /v1/design/** (AAP 0.4.4). The headless half takes the range from PageResolver, whose
//            default - UnresolvedPageResolver - resolves NOTHING, so dw_sqlite.srd:L27's
//            `sum(salary for page)` evaluates to "!" with a structured error until a caller injects an
//            authoritative resolver. It is deliberately NOT defaulted to the whole primary buffer:
//            that returns the grand total where a page total was asked for, on a paginated surface,
//            with nothing in the answer to distinguish the two - the widening 0.1.5 forbids, wearing
//            the costume of a sensible default. A caller that knows its page size supplies
//            FixedRowsPerPageResolver; one that means "unpaginated" says so with
//            WholeBufferPageResolver.
//        (b) `for group n` needs the group-band model, which is the same deferred surface. It parses,
//            and evaluates to "!" with a structured error naming the gap. It is not silently treated
//            as `for all`, because that would return a plausible wrong number.
//        (c) String(value, mask) with a mask other than the general mask answers "!" with a structured
//            error. No in-scope expression supplies one - the legacy applies masks in PowerScript, at
//            n_cst_dwsvc_contextmenu.sru:L1146, :L1148, :L1199 - and PowerBuilder's mask dialect
//            cannot be characterized without the oracle, so implementing a guess would be exactly the
//            widening 0.1.5 forbids. The general mask itself IS handled, because :L1143, :L1168,
//            :L1309 and :L1334 all define it to mean "no mask".
//  D8. NULL PROPAGATES THROUGH EVERYTHING and is never collapsed to zero or to the empty string
//      (AAP 0.4.5.4). The legacy corroborates it from the other side: :L1186 and :L1352 wrap every
//      operand in `if(IsNull(x),'',x)` precisely because a bare comparison against null does not
//      answer true or false. Null-safety is therefore a reproduced behaviour, not a defensive habit.
//  D9. THE NULL-CONCATENATION HAZARD IS GUARDED EXPLICITLY. dwvaluetoexp.srf:L48-L49 reads
//      `sVal = "'" + val + "'"` then `if IsNull(sVal) then sVal = "dwNvlString()"`, which works only
//      because PowerScript propagates null through concatenation. In C# `"'" + null + "'"` is `"''"`
//      and the guard would never fire, so every value-to-text path in this file tests the value for
//      null BEFORE formatting it. The same trap applies to String(nullNumeric), which is why
//      ExpressionValue.ToDisplayText answers null - never "" - for a null.
//  D10. ROWS ARE ONE-BASED ON EVERY SURFACE. AAP 0.4.5.4 names one-based to zero-based translation the
//      single most dangerous mechanical hazard in the refactor, so a row number crosses this file in
//      the numbering the oracle uses and is never rebased. RowCount() is the LAST VALID ROW NUMBER.
//
// --------------------------------------------------------------------------------------------------
//  BOUNDARIES
// --------------------------------------------------------------------------------------------------
//
//  * The DataWindow is reached ONLY through Domain/DataWindowServiceHost.cs. No theming, no DPI or
//    pixel conversion, no font measurement, no window geometry and no Win32 (constraint C-D). Text
//    width is computed logically, with Len and LenA, exactly as n_cst_dwsvc_contextmenu.sru:L1132-L1135
//    computes it before handing the result to the font measurement this service does not own.
//  * No P/Invoke. The PBNI dependency is eliminated rather than wrapped (AAP 0.1.2) and the target is
//    a Linux container.
//  * No package reference is added, and nothing outside the six shared projects and this project's own
//    folders is referenced (constraints C-A, C-I).
//
// ==================================================================================================

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Validators;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.DataServices.Expressions;

/// <summary>
/// The nine value shapes a DataWindow expression can carry.
/// </summary>
/// <remarks>
/// <para>
/// The seven scalar arms mirror <see cref="ExpressionVariableKind"/> one for one, because a variable's
/// value and an expression's value are the same universe of values seen from two ends. The eighth arm,
/// <see cref="Decimal"/>, has no counterpart there and is the reason this enumeration exists at all:
/// the column-expression engine reads a decimal column through <c>GetItemDecimal</c>
/// (<c>n_cst_dwsvc_columnexp.sru:L724</c>, <c>:L2346</c>) and <c>dw_sqlite.srd:L12</c> declares
/// <c>decimal(2)</c>, so folding decimal onto double would discard the base-ten scale that byte-exact
/// output depends on. <see cref="Null"/> is the ninth and is a value in its own right, never an absence
/// of one (DECISION D8).
/// </para>
/// </remarks>
public enum ExpressionValueKind
{
    /// <summary>
    /// The PowerBuilder null. Carried as a value so that null can propagate through an operator rather
    /// than being represented by the absence of a result.
    /// </summary>
    Null = 0,

    /// <summary>
    /// A boolean, as produced by a comparison, by <c>IsNull</c>, or by the <c>1=1</c> / <c>1=0</c>
    /// literals the macro marshaller splices in
    /// (<c>n_cst_dwsvc_columnexp.sru:L2271</c>, <c>:L2273</c>).
    /// </summary>
    Boolean = 1,

    /// <summary>
    /// A 64-bit integer, as produced by <c>Long(...)</c>, by <c>Len</c>, by <c>LenA</c>, by
    /// <c>GetRow</c>, by <c>RowCount</c> and by an integral literal.
    /// </summary>
    Long = 2,

    /// <summary>
    /// A base-ten decimal, as produced by a <c>decimal(n)</c> column, by a literal carrying a decimal
    /// point, and by division.
    /// </summary>
    Decimal = 3,

    /// <summary>
    /// A binary floating-point number, as produced by <c>Double(...)</c>, by <c>^</c> and by a literal
    /// carrying an exponent.
    /// </summary>
    Double = 4,

    /// <summary>
    /// Text. Distinct from <see cref="Null"/> even when empty (DECISION D9).
    /// </summary>
    String = 5,

    /// <summary>
    /// A date with no time component, as produced by <c>Date(...)</c> and by a <c>date</c> column.
    /// </summary>
    Date = 6,

    /// <summary>
    /// A time of day with no date component, as produced by <c>Time(...)</c> and by a <c>time</c>
    /// column.
    /// </summary>
    Time = 7,

    /// <summary>
    /// A date and time, as produced by <c>DateTime(...)</c> and by a <c>datetime</c> column.
    /// </summary>
    DateTime = 8,
}

/// <summary>
/// One value flowing through a DataWindow expression: a discriminated union over the nine
/// <see cref="ExpressionValueKind"/> arms, with the PowerBuilder null modelled as its own arm.
/// </summary>
/// <remarks>
/// <para>
/// SHAPED AFTER <see cref="ExpressionVariableValue"/> ON PURPOSE - one private constructor, one payload
/// field per arm, and every other field left at its default so the synthesized field-wise equality is
/// well defined. Two differences are deliberate. This type adds the decimal arm
/// (see <see cref="ExpressionValueKind"/>), and its typed readers COERCE rather than throw on a kind
/// mismatch, because an expression's whole job is to combine operands of different types whereas a
/// variable declaration fixes one.
/// </para>
/// <para>
/// A null is <see cref="ExpressionValueKind.Null"/> and NOT a null-valued arm of some other kind. The
/// DataWindow expression language has no typed null - <c>IsNull(x)</c> asks one question and gets one
/// answer regardless of the column's type - so a single arm is the faithful shape. The five
/// <c>dwNvl*</c> producers all land here, which is exactly why they are five separate functions in the
/// legacy and one value here: their PowerScript return types differ, but the expression-level value
/// each contributes is the same null.
/// </para>
/// </remarks>
public readonly record struct ExpressionValue
{
    private readonly ExpressionValueKind _kind;
    private readonly bool _booleanValue;
    private readonly long _longValue;
    private readonly decimal _decimalValue;
    private readonly double _doubleValue;
    private readonly string? _stringValue;
    private readonly DateOnly _dateValue;
    private readonly TimeOnly _timeValue;
    private readonly DateTime _dateTimeValue;

    /// <summary>
    /// The single private constructor. Each factory selects one arm and leaves the rest at default.
    /// </summary>
    private ExpressionValue(
        ExpressionValueKind kind,
        bool booleanValue = false,
        long longValue = 0L,
        decimal decimalValue = 0m,
        double doubleValue = 0d,
        string? stringValue = null,
        DateOnly dateValue = default,
        TimeOnly timeValue = default,
        DateTime dateTimeValue = default)
    {
        _kind = kind;
        _booleanValue = booleanValue;
        _longValue = longValue;
        _decimalValue = decimalValue;
        _doubleValue = doubleValue;
        _stringValue = stringValue;
        _dateValue = dateValue;
        _timeValue = timeValue;
        _dateTimeValue = dateTimeValue;
    }

    /// <summary>
    /// The PowerBuilder null, and the value of <c>default(ExpressionValue)</c>.
    /// </summary>
    /// <remarks>
    /// Null being the zero of this type is deliberate: an uninitialised operand is null rather than
    /// zero or the empty string, which is the direction AAP 0.4.5.4 requires any accident to fall in.
    /// </remarks>
    public static ExpressionValue Null => default;

    /// <summary>
    /// Boolean true, the value the <c>1=1</c> literal at
    /// <c>n_cst_dwsvc_columnexp.sru:L2271</c> evaluates to.
    /// </summary>
    public static ExpressionValue True { get; } = FromBoolean(true);

    /// <summary>
    /// Boolean false, the value the <c>1=0</c> literal at
    /// <c>n_cst_dwsvc_columnexp.sru:L2273</c> evaluates to.
    /// </summary>
    public static ExpressionValue False { get; } = FromBoolean(false);

    /// <summary>
    /// The empty string - a SUCCESSFUL value, and never the abort sentinel.
    /// </summary>
    /// <remarks>
    /// Named so that the difference from <see cref="DataWindowExpressionEvaluator.AbortSentinel"/> is
    /// legible at a call site. <c>if(...,'')</c> at <c>n_cst_dwsvc_contextmenu.sru:L1183</c> produces
    /// exactly this: an empty string the comparison then uses as a value.
    /// </remarks>
    public static ExpressionValue EmptyString { get; } = FromString(string.Empty);

    /// <summary>
    /// Which arm this value carries.
    /// </summary>
    public ExpressionValueKind Kind => _kind;

    /// <summary>
    /// <see langword="true"/> when this value is the PowerBuilder null.
    /// </summary>
    public bool IsNull => _kind == ExpressionValueKind.Null;

    /// <summary>
    /// <see langword="true"/> for the four numeric arms - <see cref="ExpressionValueKind.Long"/>,
    /// <see cref="ExpressionValueKind.Decimal"/>, <see cref="ExpressionValueKind.Double"/> and
    /// <see cref="ExpressionValueKind.Boolean"/>.
    /// </summary>
    /// <remarks>
    /// Boolean counts as numeric because the DataWindow expression language has no separate boolean
    /// storage: <c>if(IsNull(x),1,0)</c> at <c>n_cst_dwsvc_contextmenu.sru:L1188</c> compares the
    /// numeric projections of two conditions, so a boolean has to be reachable as a number.
    /// </remarks>
    public bool IsNumeric =>
        _kind is ExpressionValueKind.Long
            or ExpressionValueKind.Decimal
            or ExpressionValueKind.Double
            or ExpressionValueKind.Boolean;

    /// <summary>
    /// <see langword="true"/> for the three temporal arms.
    /// </summary>
    public bool IsTemporal =>
        _kind is ExpressionValueKind.Date or ExpressionValueKind.Time or ExpressionValueKind.DateTime;

    /// <summary>Creates a boolean value.</summary>
    /// <param name="value">The value.</param>
    /// <returns>A <see cref="ExpressionValueKind.Boolean"/> value.</returns>
    public static ExpressionValue FromBoolean(bool value) =>
        new(ExpressionValueKind.Boolean, booleanValue: value);

    /// <summary>Creates an integer value.</summary>
    /// <param name="value">The value.</param>
    /// <returns>A <see cref="ExpressionValueKind.Long"/> value.</returns>
    public static ExpressionValue FromLong(long value) =>
        new(ExpressionValueKind.Long, longValue: value);

    /// <summary>Creates a base-ten decimal value.</summary>
    /// <param name="value">The value.</param>
    /// <returns>A <see cref="ExpressionValueKind.Decimal"/> value.</returns>
    public static ExpressionValue FromDecimal(decimal value) =>
        new(ExpressionValueKind.Decimal, decimalValue: value);

    /// <summary>Creates a binary floating-point value.</summary>
    /// <param name="value">The value.</param>
    /// <returns>A <see cref="ExpressionValueKind.Double"/> value.</returns>
    public static ExpressionValue FromDouble(double value) =>
        new(ExpressionValueKind.Double, doubleValue: value);

    /// <summary>
    /// Creates a text value, or <see cref="Null"/> when <paramref name="value"/> is
    /// <see langword="null"/>.
    /// </summary>
    /// <param name="value">The value, or <see langword="null"/> for the PowerBuilder null string.</param>
    /// <returns>
    /// A <see cref="ExpressionValueKind.String"/> value, or <see cref="Null"/>. The empty string
    /// produces a <see cref="ExpressionValueKind.String"/> value and NEVER a null (DECISION D9).
    /// </returns>
    public static ExpressionValue FromString(string? value) =>
        value is null ? Null : new(ExpressionValueKind.String, stringValue: value);

    /// <summary>Creates a date value, or <see cref="Null"/>.</summary>
    /// <param name="value">The value, or <see langword="null"/>.</param>
    /// <returns>A <see cref="ExpressionValueKind.Date"/> value, or <see cref="Null"/>.</returns>
    public static ExpressionValue FromDate(DateOnly? value) =>
        value.HasValue ? new(ExpressionValueKind.Date, dateValue: value.Value) : Null;

    /// <summary>Creates a time value, or <see cref="Null"/>.</summary>
    /// <param name="value">The value, or <see langword="null"/>.</param>
    /// <returns>A <see cref="ExpressionValueKind.Time"/> value, or <see cref="Null"/>.</returns>
    public static ExpressionValue FromTime(TimeOnly? value) =>
        value.HasValue ? new(ExpressionValueKind.Time, timeValue: value.Value) : Null;

    /// <summary>Creates a date-and-time value, or <see cref="Null"/>.</summary>
    /// <param name="value">The value, or <see langword="null"/>.</param>
    /// <returns>A <see cref="ExpressionValueKind.DateTime"/> value, or <see cref="Null"/>.</returns>
    public static ExpressionValue FromDateTime(DateTime? value) =>
        value.HasValue ? new(ExpressionValueKind.DateTime, dateTimeValue: value.Value) : Null;

    /// <summary>Creates a decimal value, or <see cref="Null"/>.</summary>
    /// <param name="value">The value, or <see langword="null"/>.</param>
    /// <returns>A <see cref="ExpressionValueKind.Decimal"/> value, or <see cref="Null"/>.</returns>
    public static ExpressionValue FromDecimal(decimal? value) =>
        value.HasValue ? FromDecimal(value.Value) : Null;

    /// <summary>Creates an integer value, or <see cref="Null"/>.</summary>
    /// <param name="value">The value, or <see langword="null"/>.</param>
    /// <returns>A <see cref="ExpressionValueKind.Long"/> value, or <see cref="Null"/>.</returns>
    public static ExpressionValue FromLong(long? value) =>
        value.HasValue ? FromLong(value.Value) : Null;

    /// <summary>Creates a floating-point value, or <see cref="Null"/>.</summary>
    /// <param name="value">The value, or <see langword="null"/>.</param>
    /// <returns>A <see cref="ExpressionValueKind.Double"/> value, or <see cref="Null"/>.</returns>
    public static ExpressionValue FromDouble(double? value) =>
        value.HasValue ? FromDouble(value.Value) : Null;

    /// <summary>Creates a boolean value, or <see cref="Null"/>.</summary>
    /// <param name="value">The value, or <see langword="null"/>.</param>
    /// <returns>A <see cref="ExpressionValueKind.Boolean"/> value, or <see cref="Null"/>.</returns>
    public static ExpressionValue FromBoolean(bool? value) =>
        value.HasValue ? FromBoolean(value.Value) : Null;


    /// <summary>
    /// Projects one of the column-expression engine's seven typed variable values onto an expression
    /// value.
    /// </summary>
    /// <param name="value">The variable value, possibly <see cref="ExpressionVariableValue.Unset"/>.</param>
    /// <returns>
    /// The corresponding expression value, or <see cref="Null"/> for an unset variable and for a
    /// variable whose value is null.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE BRIDGE BETWEEN THE TWO HALVES OF THE ENGINE. A variable's value is declared through the seven
    /// <c>of_AddVar</c> overloads that Expressions/ExpressionVariableEnvironment.cs models
    /// (<c>n_cst_dwsvc_columnexp.sru:L153-L159</c>); the same value then has to be usable as an operand
    /// once the preprocessor has spliced it into an expression. This is the one-way conversion that
    /// makes that possible, and it is one-way on purpose: the decimal arm has no counterpart on the
    /// variable side, so a round trip would have to invent a lossy mapping for it.
    /// </para>
    /// <para>
    /// An unset variable becomes null rather than raising. The legacy reaches the same place from the
    /// other direction at <c>:L2207</c> and <c>:L2253</c>, where a variable with no value is reported as
    /// an invalid value and the preprocess aborts - the abort is the CALLER's decision, taken on the
    /// null, and is not this conversion's to take.
    /// </para>
    /// </remarks>
    public static ExpressionValue FromVariableValue(in ExpressionVariableValue value) =>
        value.Kind switch
        {
            ExpressionVariableKind.TimeValue => FromTime(value.AsTime()),
            ExpressionVariableKind.StringValue => FromString(value.AsString()),
            ExpressionVariableKind.LongValue => FromLong(value.AsLong()),
            ExpressionVariableKind.DoubleValue => FromDouble(value.AsDouble()),
            ExpressionVariableKind.DateTimeValue => FromDateTime(value.AsDateTime()),
            ExpressionVariableKind.DateValue => FromDate(value.AsDate()),
            ExpressionVariableKind.BooleanValue => FromBoolean(value.AsBoolean()),

            // ExpressionVariableKind.None - the unset variable. Null, per the remarks.
            _ => Null,
        };

    /// <summary>
    /// Projects a raw DataWindow item value - the <c>any</c> that
    /// <see cref="IDataWindowValueBuffer"/> hands back - onto an expression value.
    /// </summary>
    /// <param name="value">The raw value, or <see langword="null"/>.</param>
    /// <returns>The typed expression value, or <see cref="Null"/>.</returns>
    /// <remarks>
    /// <para>
    /// The arms are ordered widest-first within each family so that nothing is narrowed on the way
    /// through, which is the same rule <c>DataWindowObjectExtensions.ToColumnId</c> follows for the
    /// same reason. The integral family lands on <see cref="ExpressionValueKind.Long"/>, matching
    /// <c>_of_ConvertColType</c>'s <c>COL_TYPE_INTEGER</c> arm at <c>n_cst_dwsvc.sru:L506-L507</c>,
    /// which covers <c>number</c>, <c>long</c> and <c>ulong</c> alike.
    /// </para>
    /// <para>
    /// AN UNRECOGNISED TYPE BECOMES ITS TEXT rather than becoming null or raising. A DataWindow item is
    /// an <c>any</c> and the host contract cannot enumerate what an implementation may put in it; text
    /// is the one projection every value has, and it is what <c>String(...)</c> would have produced at
    /// the legacy call site anyway. <see cref="ExpressionVariableValue"/> is recognised explicitly so a
    /// caller holding one may pass it through without unwrapping it first.
    /// </para>
    /// </remarks>
    public static ExpressionValue FromObject(object? value)
    {
        switch (value)
        {
            case null:
                return Null;
            case ExpressionValue already:
                return already;
            case ExpressionVariableValue variable:
                return FromVariableValue(variable);
            case bool boolean:
                return FromBoolean(boolean);
            case string text:
                return FromString(text);
            case decimal number:
                return FromDecimal(number);
            case double number:
                return FromDouble(number);
            case float number:
                return FromDouble(number);
            case long number:
                return FromLong(number);
            case int number:
                return FromLong(number);
            case short number:
                return FromLong(number);
            case sbyte number:
                return FromLong(number);
            case byte number:
                return FromLong(number);
            case ushort number:
                return FromLong(number);
            case uint number:
                return FromLong(number);

            // An unsigned 64-bit value past long.MaxValue cannot be a long, and the DataWindow
            // `ulong` it stands for is a 32-bit type, so the overflow is genuinely out of domain.
            // Decimal holds it exactly, which keeps the value rather than wrapping it.
            case ulong number:
                return number <= long.MaxValue ? FromLong((long)number) : FromDecimal(number);
            case DateTime moment:
                return FromDateTime(moment);
            case DateOnly date:
                return FromDate(date);
            case TimeOnly time:
                return FromTime(time);
            case TimeSpan span:
                return FromTime(TimeOnly.FromTimeSpan(span));
            case char character:
                return FromString(character.ToString());
            default:
                return FromString(FormatUnknown(value));
        }
    }

    /// <summary>
    /// Renders an unrecognised item value as text, invariantly where the value supports it.
    /// </summary>
    /// <param name="value">The value, never <see langword="null"/> at this point.</param>
    /// <returns>The text, or the empty string when the value renders as <see langword="null"/>.</returns>
    private static string FormatUnknown(object value) =>
        value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : value.ToString() ?? string.Empty;

    /// <summary>
    /// The text this value contributes to a DataWindow expression's result - the port of PowerScript's
    /// <c>String(...)</c> applied to each type.
    /// </summary>
    /// <returns>
    /// The text, or <see langword="null"/> when this value is the PowerBuilder null. NEVER the empty
    /// string for a null (DECISION D9).
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE NULL RETURN IS THE POINT OF THIS MEMBER'S SIGNATURE. <c>dwvaluetoexp.srf</c> is written as
    /// <c>sVal = String(val)</c> followed by <c>if IsNull(sVal) then sVal = "dwNvlNumber()"</c>
    /// (<c>:L18-L19</c>), which relies on <c>String</c> of a null being null. A C# method returning
    /// <c>string</c> would have to answer <c>""</c> there and the guard could never fire, so the
    /// nullable return is what keeps the ported guard reachable.
    /// </para>
    /// <para>
    /// THE FORMATS ARE THE VALIDATORS' OWN CONSTANTS, not copies of them:
    /// <see cref="DateValidator.CanonicalValueFormat"/>, <see cref="TimeValidator.CanonicalFormat"/> and
    /// <see cref="DateTimeValidator.ExpressionValueFormat"/>. Those three files are the authority for
    /// what a date, a time and a datetime look like on this service's wire, and restating the patterns
    /// here would create a second place for them to drift.
    /// </para>
    /// <para>
    /// Numbers render invariantly. A decimal renders with the scale it carries - <c>123.50m</c> is
    /// <c>"123.50"</c> - which is what preserves <c>dw_sqlite.srd:L12</c>'s <c>decimal(2)</c> through
    /// <c>sum(salary for page)</c>. A boolean renders as <c>"true"</c> or <c>"false"</c>, PowerScript's
    /// own spelling for <c>String(boolean)</c>.
    /// </para>
    /// </remarks>
    public string? ToDisplayText() =>
        _kind switch
        {
            ExpressionValueKind.Boolean => _booleanValue ? "true" : "false",
            ExpressionValueKind.Long => _longValue.ToString(CultureInfo.InvariantCulture),
            ExpressionValueKind.Decimal => _decimalValue.ToString(CultureInfo.InvariantCulture),
            ExpressionValueKind.Double => _doubleValue.ToString(CultureInfo.InvariantCulture),
            ExpressionValueKind.String => _stringValue,
            ExpressionValueKind.Date =>
                _dateValue.ToString(DateValidator.CanonicalValueFormat, CultureInfo.InvariantCulture),
            ExpressionValueKind.Time =>
                _timeValue.ToString(TimeValidator.CanonicalFormat, CultureInfo.InvariantCulture),
            ExpressionValueKind.DateTime =>
                _dateTimeValue.ToString(
                    DateTimeValidator.ExpressionValueFormat,
                    CultureInfo.InvariantCulture),

            // ExpressionValueKind.Null.
            _ => null,
        };

    /// <summary>
    /// The expression LITERAL TEXT for this value - the port of the nine
    /// <c>dwvaluetoexp.srf</c> overloads, reached from inside an expression as
    /// <c>dwValueToExp(...)</c> (<c>n_cst_dwsvc_columnexp.sru:L2390</c>).
    /// </summary>
    /// <returns>
    /// Text that, re-parsed as an expression, yields this value again. Never <see langword="null"/> and
    /// never the empty string.
    /// </returns>
    /// <remarks>
    /// <para>
    /// DELEGATED TO <see cref="ValueToExpression"/> RATHER THAN REIMPLEMENTED. That file is the port of
    /// <c>dwvaluetoexp.srf</c>, including its null guards and its hard invariant never to return the
    /// empty string; duplicating the formatting here would create a second implementation of the same
    /// nine overloads, and any drift between them would show up as a corrupted expression rather than
    /// as a failure.
    /// </para>
    /// <para>
    /// THE CLOSED LOOP THIS MEMBER COMPLETES. For a null the delegate answers the literal TEXT
    /// <c>"dwNvlNumber()"</c>, <c>"dwNvlString()"</c>, <c>"dwNvlDate()"</c>, <c>"dwNvlDateTime()"</c> or
    /// <c>"dwNvlTime()"</c> (<c>dwvaluetoexp.srf:L19</c>, <c>:L49</c>, <c>:L61</c>, <c>:L55</c>,
    /// <c>:L67</c>), which the preprocessor then splices into an expression that this evaluator has to
    /// be able to evaluate. That is why all five zero-argument producers are registered in the roster.
    /// </para>
    /// <para>
    /// A NULL LOSES ITS TYPE, AND THE MAPPING IS FORCED. <see cref="ExpressionValueKind.Null"/> is a
    /// single arm (see that enumeration's remarks), so a null reaching here has no type to dispatch on
    /// and answers <see cref="NumberValidator.NullLiteralExpression"/>. That is the same choice the
    /// oracle's own text makes: seven of the nine overloads - the five numeric ones plus the two the
    /// engine reaches for <c>COL_TYPE_DECIMAL</c> and <c>COL_TYPE_INTEGER</c> at <c>:L2346</c> and
    /// <c>:L2348</c> - emit the numeric producer, so it is the majority arm as well as the only one a
    /// typeless null can name. A caller that must preserve a null's column type calls the matching
    /// <see cref="ValueToExpression"/> overload directly with a typed <see langword="null"/>, which is
    /// what Expressions/ColumnExpressionEngine.cs does when it dispatches on
    /// <c>_of_GetColumnType</c>.
    /// </para>
    /// <para>
    /// A boolean has no <c>dwvaluetoexp.srf</c> overload at all. The oracle marshals a boolean into an
    /// expression as the comparison <c>1=1</c> or <c>1=0</c> instead
    /// (<c>n_cst_dwsvc_columnexp.sru:L2269-L2274</c>, <c>:L2293-L2298</c>), and that is reproduced here
    /// verbatim rather than routed through a tenth overload the legacy does not have.
    /// </para>
    /// </remarks>
    public string ToExpressionLiteral() =>
        _kind switch
        {
            ExpressionValueKind.Boolean => _booleanValue ? "1=1" : "1=0",
            ExpressionValueKind.Long => ValueToExpression.Convert(_longValue),
            ExpressionValueKind.Decimal => ValueToExpression.Convert(_decimalValue),
            ExpressionValueKind.Double => ValueToExpression.Convert(_doubleValue),
            ExpressionValueKind.String => ValueToExpression.Convert(_stringValue),
            ExpressionValueKind.Date => ValueToExpression.Convert(_dateValue),
            ExpressionValueKind.Time => ValueToExpression.Convert(_timeValue),
            ExpressionValueKind.DateTime => ValueToExpression.Convert(_dateTimeValue),

            // ExpressionValueKind.Null - typeless, so the numeric producer. See the remarks.
            _ => NumberValidator.NullLiteralExpression,
        };


    /// <summary>
    /// Reads this value as text, coercing every non-null arm through
    /// <see cref="ToDisplayText"/>.
    /// </summary>
    /// <param name="value">Receives the text when the return is <see langword="true"/>.</param>
    /// <returns>
    /// <see langword="false"/> when this value is the PowerBuilder null - and only then. Every other arm
    /// has a text projection, so no other input can fail.
    /// </returns>
    public bool TryGetText(out string value)
    {
        string? text = ToDisplayText();
        value = text ?? string.Empty;

        return text is not null;
    }

    /// <summary>
    /// Reads this value as a base-ten decimal, coercing the other numeric arms and parsing text.
    /// </summary>
    /// <param name="value">Receives the number when the return is <see langword="true"/>.</param>
    /// <returns>
    /// <see langword="false"/> for the null arm, for text that does not parse as a number, for a
    /// temporal arm, and for a double whose magnitude no decimal can hold.
    /// </returns>
    /// <remarks>
    /// A boolean coerces to <c>1</c> or <c>0</c>, which is what makes
    /// <c>if(IsNull(x),1,0) &lt;&gt; if(IsNull(y),1,0)</c>
    /// [<c>n_cst_dwsvc_contextmenu.sru:L1188</c>] comparable. Text parses invariantly, because an
    /// expression's numeric literal is structural rather than localized. The double overflow answers
    /// <see langword="false"/> rather than raising, so the caller renders it as the malformed sentinel
    /// (DECISION D3).
    /// </remarks>
    public bool TryGetDecimal(out decimal value)
    {
        switch (_kind)
        {
            case ExpressionValueKind.Decimal:
                value = _decimalValue;
                return true;
            case ExpressionValueKind.Long:
                value = _longValue;
                return true;
            case ExpressionValueKind.Boolean:
                value = _booleanValue ? 1m : 0m;
                return true;
            case ExpressionValueKind.Double:
                // decimal cannot hold every double, and the conversion throws rather than saturating.
                if (double.IsNaN(_doubleValue)
                    || double.IsInfinity(_doubleValue)
                    || _doubleValue > (double)decimal.MaxValue
                    || _doubleValue < (double)decimal.MinValue)
                {
                    value = 0m;
                    return false;
                }

                value = (decimal)_doubleValue;
                return true;
            case ExpressionValueKind.String:
                return decimal.TryParse(
                    _stringValue,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value);
            default:
                value = 0m;
                return false;
        }
    }

    /// <summary>
    /// Reads this value as a binary floating-point number.
    /// </summary>
    /// <param name="value">Receives the number when the return is <see langword="true"/>.</param>
    /// <returns><see langword="false"/> for the null arm, for a temporal arm, and for unparseable text.</returns>
    public bool TryGetDouble(out double value)
    {
        switch (_kind)
        {
            case ExpressionValueKind.Double:
                value = _doubleValue;
                return true;
            case ExpressionValueKind.Decimal:
                value = (double)_decimalValue;
                return true;
            case ExpressionValueKind.Long:
                value = _longValue;
                return true;
            case ExpressionValueKind.Boolean:
                value = _booleanValue ? 1d : 0d;
                return true;
            case ExpressionValueKind.String:
                return double.TryParse(
                    _stringValue,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value);
            default:
                value = 0d;
                return false;
        }
    }

    /// <summary>
    /// Reads this value as a 64-bit integer, TRUNCATING any fraction toward zero.
    /// </summary>
    /// <param name="value">Receives the number when the return is <see langword="true"/>.</param>
    /// <returns>
    /// <see langword="false"/> for the null arm, for a temporal arm, for unparseable text, and for a
    /// magnitude no <see cref="long"/> can hold.
    /// </returns>
    /// <remarks>
    /// TRUNCATION AND NOT ROUNDING, deliberately: PowerScript's <c>Long(1.9)</c> is <c>1</c>, and
    /// <c>n_cst_dwsvc_contextmenu.sru:L1132</c> wraps this evaluator's answer in exactly that
    /// conversion before using it as a character count. <c>Round</c> is a separate function in the
    /// roster precisely because the two are different operations.
    /// </remarks>
    public bool TryGetLong(out long value)
    {
        if (_kind == ExpressionValueKind.Long)
        {
            value = _longValue;
            return true;
        }

        if (_kind == ExpressionValueKind.Boolean)
        {
            value = _booleanValue ? 1L : 0L;
            return true;
        }

        if (!TryGetDecimal(out decimal number))
        {
            value = 0L;
            return false;
        }

        decimal truncated = decimal.Truncate(number);
        if (truncated > long.MaxValue || truncated < long.MinValue)
        {
            value = 0L;
            return false;
        }

        value = (long)truncated;
        return true;
    }

    /// <summary>
    /// Reads this value as a boolean, under PowerBuilder's expression truthiness.
    /// </summary>
    /// <param name="value">Receives the flag when the return is <see langword="true"/>.</param>
    /// <returns><see langword="false"/> for the null arm and for any arm with no truth value.</returns>
    /// <remarks>
    /// A NUMBER IS TRUE WHEN IT IS NON-ZERO, which is what lets the numeric projections at
    /// <c>n_cst_dwsvc_contextmenu.sru:L1188</c> serve as conditions. Text has NO truth value and
    /// answers <see langword="false"/> from this method - it is not treated as true because it is
    /// non-empty, because that would silently accept a mis-typed condition. A temporal value likewise
    /// has none.
    /// </remarks>
    public bool TryGetBoolean(out bool value)
    {
        switch (_kind)
        {
            case ExpressionValueKind.Boolean:
                value = _booleanValue;
                return true;
            case ExpressionValueKind.Long:
                value = _longValue != 0L;
                return true;
            case ExpressionValueKind.Decimal:
                value = _decimalValue != 0m;
                return true;
            case ExpressionValueKind.Double:
                value = _doubleValue != 0d;
                return true;
            default:
                value = false;
                return false;
        }
    }

    /// <summary>
    /// Reads this value as a point in time, widening a date to midnight and a time to the zero date.
    /// </summary>
    /// <param name="value">Receives the moment when the return is <see langword="true"/>.</param>
    /// <returns><see langword="false"/> for every non-temporal arm, the null arm included.</returns>
    /// <remarks>
    /// The widening exists so that the three temporal arms are mutually comparable, which is the only
    /// thing this method is used for. Text is NOT parsed here: a DataWindow expression turns text into a
    /// date through <c>Date(...)</c>, <c>Time(...)</c> or <c>DateTime(...)</c> explicitly - the
    /// conversions the macro marshaller emits at <c>n_cst_dwsvc_columnexp.sru:L2276-L2280</c> - and an
    /// implicit parse here would make those three functions redundant while accepting text the oracle
    /// rejects.
    /// </remarks>
    public bool TryGetMoment(out DateTime value)
    {
        switch (_kind)
        {
            case ExpressionValueKind.DateTime:
                value = _dateTimeValue;
                return true;
            case ExpressionValueKind.Date:
                value = _dateValue.ToDateTime(TimeOnly.MinValue);
                return true;
            case ExpressionValueKind.Time:
                value = DateOnly.MinValue.ToDateTime(_timeValue);
                return true;
            default:
                value = default;
                return false;
        }
    }

    /// <summary>
    /// The underlying value as a plain CLR object - the inverse of <see cref="FromObject"/>.
    /// </summary>
    /// <returns>
    /// The boxed payload, or <see langword="null"/> for the null arm.
    /// </returns>
    /// <remarks>
    /// PRESENT FOR EXACTLY ONE PURPOSE: handing evaluated arguments to a collaborator that takes an
    /// untyped list. <see cref="PinyinFirstLetterMatcher.TryInvokeFromExpression"/> is declared over
    /// <c>IReadOnlyList&lt;object?&gt;</c> and reads its arguments through its own text and flag
    /// coercions, so it must receive the payload itself and not this wrapper - a boxed
    /// <see cref="ExpressionValue"/> would reach its <c>IFormattable</c> arm and arrive as
    /// <c>"(null)"</c> for a null instead of as a null. Nothing else in this file uses it, and it
    /// deliberately boxes rather than exposing the fields.
    /// </remarks>
    public object? ToRawObject() =>
        _kind switch
        {
            ExpressionValueKind.Boolean => _booleanValue,
            ExpressionValueKind.Long => _longValue,
            ExpressionValueKind.Decimal => _decimalValue,
            ExpressionValueKind.Double => _doubleValue,
            ExpressionValueKind.String => _stringValue,
            ExpressionValueKind.Date => _dateValue,
            ExpressionValueKind.Time => _timeValue,
            ExpressionValueKind.DateTime => _dateTimeValue,
            _ => null,
        };

    /// <summary>
    /// A stable, diagnostic-only rendering that keeps a null visible.
    /// </summary>
    /// <returns>The display text, or <c>"(null)"</c> for the null arm.</returns>
    /// <remarks>
    /// <c>"(null)"</c> is the oracle's own spelling: the trace event at
    /// <c>n_cst_dwsvc_columnexp.sru:L758</c> substitutes exactly that string for a value it has decided
    /// is null. Reusing it keeps a trace recording comparable.
    /// </remarks>
    public override string ToString() => ToDisplayText() ?? "(null)";
}

/// <summary>
/// The four answers comparing two expression values can give.
/// </summary>
/// <remarks>
/// FOUR AND NOT THREE, because null and "no common type" are different failures and the evaluator
/// answers them differently: a null comparison yields a null value and propagates
/// (DECISION D8), while an incomparable pair is a malformed expression and yields the <c>"!"</c>
/// sentinel.
/// </remarks>
public enum ExpressionComparison
{
    /// <summary>At least one operand is the PowerBuilder null, so the comparison has no answer.</summary>
    Null = 0,

    /// <summary>The left operand sorts before the right.</summary>
    Less = 1,

    /// <summary>The operands are equal.</summary>
    Equal = 2,

    /// <summary>The left operand sorts after the right.</summary>
    Greater = 3,

    /// <summary>
    /// The operands have no common type - a date against a number, for instance - so no comparison is
    /// defined.
    /// </summary>
    Incomparable = 4,
}


/// <summary>
/// The three legacy outcomes of evaluating a DataWindow expression, kept apart as the file header
/// requires: a value, the two failure sentinels, and the abort.
/// </summary>
public enum ExpressionEvaluationOutcome
{
    /// <summary>
    /// A value was produced. It may be the PowerBuilder null and it may be the empty string; both are
    /// values, and neither is an abort.
    /// </summary>
    Value = 0,

    /// <summary>
    /// The expression is malformed, or names something that does not exist. Renders as <c>"!"</c>.
    /// Tested by the oracle at <c>n_cst_dwsvc_columnexp.sru:L761</c> and <c>:L2391</c>.
    /// </summary>
    InvalidExpression = 1,

    /// <summary>
    /// The expression is well formed but no value can be determined for this row. Renders as
    /// <c>"?"</c>. Tested alongside the above at the same two sites.
    /// </summary>
    Undetermined = 2,

    /// <summary>
    /// The evaluation aborted: there was nothing to evaluate. Renders as the EMPTY STRING, and
    /// propagates upward through the caller's preprocess and calculate chain
    /// (<c>n_cst_dwsvc_columnexp.sru:L745</c>, <c>:L2183</c>, <c>:L2186</c>, <c>:L2358</c>).
    /// </summary>
    Abort = 3,
}

/// <summary>
/// The structured result of one evaluation: the legacy string, the typed value behind it, and the
/// reason when there is no value.
/// </summary>
/// <remarks>
/// <para>
/// This type is what lets the boundary stay byte-identical to the oracle while the inside of the service
/// keeps the distinction the oracle throws away. <see cref="Text"/> is what
/// <c>#DataWindow.Describe("Evaluate(...)")</c> returned;
/// <see cref="Outcome"/>, <see cref="Value"/> and <see cref="Error"/> are what the oracle had no channel
/// for. See the file header for the full argument.
/// </para>
/// </remarks>
public readonly record struct DataWindowExpressionResult
{
    /// <summary>
    /// Which of the four outcomes this is.
    /// </summary>
    public ExpressionEvaluationOutcome Outcome { get; init; }

    /// <summary>
    /// The string the oracle would have returned: the value's text for
    /// <see cref="ExpressionEvaluationOutcome.Value"/>, <c>"!"</c>, <c>"?"</c>, or the empty string.
    /// </summary>
    /// <remarks>
    /// Never <see langword="null"/>. A null VALUE renders here as the empty string, because that is what
    /// the oracle's own string channel does with it - and the caller separates the two cases by reading
    /// <see cref="IsNullValue"/>, exactly as <c>n_cst_dwsvc_columnexp.sru:L770</c> re-derives the null
    /// with <c>if sVal = "" then SetNull(sVal)</c> once it knows the column's type.
    /// </remarks>
    public string Text { get; init; }

    /// <summary>
    /// The typed value, meaningful only when <see cref="Outcome"/> is
    /// <see cref="ExpressionEvaluationOutcome.Value"/>.
    /// </summary>
    public ExpressionValue Value { get; init; }

    /// <summary>
    /// The structured reason, populated for <see cref="ExpressionEvaluationOutcome.InvalidExpression"/>
    /// and <see cref="ExpressionEvaluationOutcome.Undetermined"/>, and <see langword="null"/> otherwise.
    /// </summary>
    /// <remarks>
    /// AN ABORT CARRIES NO ERROR, and that is not an omission. The abort sites in the oracle
    /// (<c>:L745</c>, <c>:L2183</c>, <c>:L2186</c>, <c>:L2358</c>) all return quietly without raising a
    /// dialog, unlike the sentinel sites which raise one at <c>:L762</c> and <c>:L2392</c>. Attaching an
    /// error to an abort would invent a diagnostic the oracle does not produce.
    /// </remarks>
    public ExpressionParseError? Error { get; init; }

    /// <summary>
    /// The expression as evaluated, after the quoting conventions were normalised. Carried so a caller
    /// can quote it in its own diagnostic - which is what <c>:L762</c> and <c>:L2392</c> both do.
    /// </summary>
    public string Expression { get; init; }

    /// <summary>
    /// The one-based row the expression was evaluated against, or <c>0</c> for no row context.
    /// </summary>
    public long Row { get; init; }

    /// <summary><see langword="true"/> when a value was produced.</summary>
    public bool IsSuccess => Outcome == ExpressionEvaluationOutcome.Value;

    /// <summary>
    /// <see langword="true"/> for either failure sentinel - the exact condition
    /// <c>if sVal = "?" or sVal = "!"</c> tests at <c>:L761</c> and <c>:L2391</c>.
    /// </summary>
    public bool IsSentinel =>
        Outcome is ExpressionEvaluationOutcome.InvalidExpression
            or ExpressionEvaluationOutcome.Undetermined;

    /// <summary>
    /// <see langword="true"/> for the abort - the condition <c>if sExp = ""</c> tests at <c>:L745</c>.
    /// </summary>
    public bool IsAbort => Outcome == ExpressionEvaluationOutcome.Abort;

    /// <summary>
    /// <see langword="true"/> when a value was produced AND that value is the PowerBuilder null. This is
    /// the member that separates a successful null from an abort, both of which render
    /// <see cref="Text"/> as the empty string.
    /// </summary>
    public bool IsNullValue => IsSuccess && Value.IsNull;

    /// <summary>
    /// Builds a successful result and derives its text from the value.
    /// </summary>
    /// <param name="value">The value produced.</param>
    /// <param name="expression">The normalised expression.</param>
    /// <param name="row">The one-based row, or <c>0</c>.</param>
    /// <returns>The result.</returns>
    public static DataWindowExpressionResult Success(
        in ExpressionValue value,
        string? expression,
        long row) =>
        new()
        {
            Outcome = ExpressionEvaluationOutcome.Value,
            // A null renders as the empty string HERE and only here - the value itself keeps its null,
            // so nothing is lost (DECISION D9).
            Text = value.ToDisplayText() ?? string.Empty,
            Value = value,
            Error = null,
            Expression = expression ?? string.Empty,
            Row = row,
        };

    /// <summary>
    /// Builds a malformed-expression result carrying the <c>"!"</c> sentinel.
    /// </summary>
    /// <param name="error">The structured reason.</param>
    /// <param name="expression">The normalised expression.</param>
    /// <param name="row">The one-based row, or <c>0</c>.</param>
    /// <returns>The result.</returns>
    public static DataWindowExpressionResult Invalid(
        ExpressionParseError? error,
        string? expression,
        long row) =>
        new()
        {
            Outcome = ExpressionEvaluationOutcome.InvalidExpression,
            Text = DataWindowExpressionEvaluator.InvalidExpressionSentinel,
            Value = ExpressionValue.Null,
            Error = error,
            Expression = expression ?? string.Empty,
            Row = row,
        };

    /// <summary>
    /// Builds an undetermined-value result carrying the <c>"?"</c> sentinel.
    /// </summary>
    /// <param name="error">The structured reason.</param>
    /// <param name="expression">The normalised expression.</param>
    /// <param name="row">The one-based row, or <c>0</c>.</param>
    /// <returns>The result.</returns>
    public static DataWindowExpressionResult Undetermined(
        ExpressionParseError? error,
        string? expression,
        long row) =>
        new()
        {
            Outcome = ExpressionEvaluationOutcome.Undetermined,
            Text = DataWindowExpressionEvaluator.UndeterminedValueSentinel,
            Value = ExpressionValue.Null,
            Error = error,
            Expression = expression ?? string.Empty,
            Row = row,
        };

    /// <summary>
    /// Builds an abort result carrying the empty string and no error.
    /// </summary>
    /// <param name="expression">The expression, which is normally empty at this point.</param>
    /// <param name="row">The one-based row, or <c>0</c>.</param>
    /// <returns>The result.</returns>
    public static DataWindowExpressionResult Abort(string? expression, long row) =>
        new()
        {
            Outcome = ExpressionEvaluationOutcome.Abort,
            Text = DataWindowExpressionEvaluator.AbortSentinel,
            Value = ExpressionValue.Null,
            Error = null,
            Expression = expression ?? string.Empty,
            Row = row,
        };

    /// <summary>
    /// A diagnostic rendering that names the outcome, so an abort and an empty success are legible apart
    /// in a test failure message.
    /// </summary>
    /// <returns>The rendering.</returns>
    public override string ToString() =>
        Outcome switch
        {
            ExpressionEvaluationOutcome.Value => "Value:" + Value,
            ExpressionEvaluationOutcome.InvalidExpression => "Invalid:!",
            ExpressionEvaluationOutcome.Undetermined => "Undetermined:?",
            _ => "Abort:",
        };
}


/// <summary>
/// The row range an aggregate covers - the <c>for</c> clause of a DataWindow aggregate.
/// </summary>
/// <remarks>
/// <c>dw_sqlite.srd:L27</c> is the only <c>for</c> clause in the repository:
/// <c>expression="sum(salary for page)"</c>. <see cref="All"/> is the default because
/// <c>n_cst_dwsvc_contextmenu.sru:L1132</c> writes <c>Max(Len(LookUpDisplay(col)))</c> with no clause and
/// uses the answer as a column width (DECISION D5).
/// </remarks>
public enum ExpressionAggregateScope
{
    /// <summary>Every row in the primary buffer. The default when no <c>for</c> clause is written.</summary>
    All = 0,

    /// <summary>
    /// The rows on the current page - <c>for page</c>. The range comes from
    /// <see cref="IExpressionPageResolver"/>, because a page boundary is derived from band heights and
    /// print margins (DECISION D7a).
    /// </summary>
    Page = 1,

    /// <summary>
    /// The rows in a group level - <c>for group n</c>. A DEFINED NARROWING: it parses and then answers
    /// the malformed sentinel with a structured error, because the group-band model belongs to the
    /// deferred DesignSystem (DECISION D7b).
    /// </summary>
    Group = 2,
}

/// <summary>
/// Supplies the row range that <see cref="ExpressionAggregateScope.Page"/> covers.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS IS A SEAM AND NOT A CALCULATION. A DataWindow page boundary falls where the accumulated
/// height of the detail band, the group bands and the trailers reaches the printable height implied by
/// the paper size and the four print margins - all of which
/// <c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L3</c> declares and none of which DataServices owns.
/// Band geometry is the deferred DesignSystem's half, reserved at <c>/v1/design/**</c> (AAP 0.4.4), so
/// this service takes the range from whoever does know it and never guesses at it.
/// </para>
/// <para>
/// THE DEFAULT IMPLEMENTATION RESOLVES NOTHING, ON PURPOSE. <see cref="UnresolvedPageResolver"/> is
/// installed until a caller supplies an authoritative one, so <c>sum(salary for page)</c> answers the
/// malformed sentinel with a structured error naming the gap rather than a number computed over the
/// wrong set of rows. Treating the whole buffer as one page looks accommodating and is a WIDENING: on a
/// four-row fixture it happens to be right, on a paginated report it silently returns the grand total
/// where the page total was asked for, and nothing about the answer says which happened. AAP 0.1.5
/// requires the narrowing with a defined error instead, which is the same treatment
/// <see cref="ExpressionAggregateScope.Group"/> already receives for the same reason.
/// </para>
/// <para>
/// A caller that knows its own page size supplies <see cref="FixedRowsPerPageResolver"/>; a caller that
/// genuinely means "the buffer is one page" - a characterization run over an unpaginated fixture, say -
/// says so explicitly with <see cref="WholeBufferPageResolver"/>. Both are opt-in, and being opt-in is
/// what makes the resulting number attributable to a stated pagination rather than to a default.
/// </para>
/// </remarks>
public interface IExpressionPageResolver
{
    /// <summary>
    /// Resolves the inclusive, one-based row range of the page containing <paramref name="row"/>.
    /// </summary>
    /// <param name="row">
    /// The one-based row being evaluated, or <c>0</c> when there is no row context.
    /// </param>
    /// <param name="rowCount">
    /// The primary buffer's row count, which is also its last valid row number (DECISION D10).
    /// </param>
    /// <param name="firstRow">Receives the first row of the page.</param>
    /// <param name="lastRow">Receives the last row of the page, inclusive.</param>
    /// <returns>
    /// <see langword="false"/> when no page can be resolved, which the evaluator reports as the
    /// malformed sentinel rather than silently widening the scope.
    /// </returns>
    bool TryGetPageRange(long row, long rowCount, out long firstRow, out long lastRow);
}

/// <summary>
/// The default page resolver: no page can be resolved, so <c>for page</c> is refused.
/// </summary>
/// <remarks>
/// <para>
/// Stated as its own type rather than as a null check inside the evaluator so that the default is
/// visible, replaceable and testable, and so the refusal reads as a decision rather than as an
/// unconfigured service. See <see cref="IExpressionPageResolver"/> for why a page boundary is not
/// computed here at all.
/// </para>
/// <para>
/// WHAT A CALLER SEES. <c>sum(salary for page)</c> evaluates to the malformed sentinel and
/// <c>TryEvaluate</c> reports a structured error naming the deferred capability and both opt-in
/// resolvers. The aggregate is not evaluated over any row range, because there is no range to evaluate
/// it over - which is the whole content of the outcome.
/// </para>
/// </remarks>
public sealed class UnresolvedPageResolver : IExpressionPageResolver
{
    /// <summary>
    /// The shared instance. The type holds no state, so one instance serves every evaluator.
    /// </summary>
    public static UnresolvedPageResolver Instance { get; } = new();

    /// <inheritdoc/>
    /// <remarks>
    /// Always <see langword="false"/>, and the two out-parameters are left at the empty range so a
    /// caller that ignored the return value still cannot read a plausible span out of them.
    /// </remarks>
    public bool TryGetPageRange(long row, long rowCount, out long firstRow, out long lastRow)
    {
        firstRow = 0L;
        lastRow = 0L;

        return false;
    }
}

/// <summary>
/// An opt-in page resolver for which the whole primary buffer is one page.
/// </summary>
/// <remarks>
/// <para>
/// Retained because it is the correct model for an unpaginated surface, and a characterization run over
/// a fixture that fits on one page needs to be able to SAY so. It is deliberately NOT the default: see
/// <see cref="IExpressionPageResolver"/> and <see cref="UnresolvedPageResolver"/> for why a default that
/// silently equates <c>for page</c> with <c>for all</c> is a widening rather than a convenience.
/// </para>
/// </remarks>
public sealed class WholeBufferPageResolver : IExpressionPageResolver
{
    /// <summary>
    /// The shared instance. The type holds no state, so one instance serves every evaluator.
    /// </summary>
    public static WholeBufferPageResolver Instance { get; } = new();

    /// <inheritdoc/>
    public bool TryGetPageRange(long row, long rowCount, out long firstRow, out long lastRow)
    {
        firstRow = 1L;
        lastRow = rowCount;

        // An empty buffer still resolves: the range 1..0 is simply empty, and an aggregate over no rows
        // is a defined outcome rather than a failure.
        return true;
    }
}

/// <summary>
/// A page resolver for a fixed number of detail rows per page.
/// </summary>
/// <remarks>
/// Provided so a caller that DOES know its pagination - a characterization run reproducing a printed
/// page, or a client that paginates itself - can supply it without this service acquiring any band
/// geometry. It computes nothing about layout; it divides a row range.
/// </remarks>
public sealed class FixedRowsPerPageResolver : IExpressionPageResolver
{
    private readonly long _rowsPerPage;

    /// <summary>
    /// Creates a resolver for <paramref name="rowsPerPage"/> rows per page.
    /// </summary>
    /// <param name="rowsPerPage">The number of rows on a page. Must be positive.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="rowsPerPage"/> is zero or negative, which describes no pagination at all.
    /// </exception>
    public FixedRowsPerPageResolver(long rowsPerPage)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rowsPerPage);

        _rowsPerPage = rowsPerPage;
    }

    /// <summary>
    /// The number of rows on a page.
    /// </summary>
    public long RowsPerPage => _rowsPerPage;

    /// <inheritdoc/>
    /// <remarks>
    /// ONE-BASED ARITHMETIC THROUGHOUT, and it is written out rather than folded into one expression
    /// because AAP 0.4.5.4 names one-based translation the refactor's most dangerous mechanical hazard.
    /// Row 0 - no row context - resolves to the FIRST page, matching the row-0 evaluations at
    /// <c>n_cst_dwsvc.sru:L344</c> and <c>:L782</c>, which mean "outside a row" rather than "outside the
    /// data".
    /// </remarks>
    public bool TryGetPageRange(long row, long rowCount, out long firstRow, out long lastRow)
    {
        long effectiveRow = row < 1L ? 1L : row;
        long zeroBasedPage = (effectiveRow - 1L) / _rowsPerPage;

        firstRow = (zeroBasedPage * _rowsPerPage) + 1L;
        lastRow = firstRow + _rowsPerPage - 1L;

        if (lastRow > rowCount)
        {
            lastRow = rowCount;
        }

        return true;
    }
}


/// <summary>
/// How a deployment states the pagination that <c>for page</c> is measured against.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS AT ALL, WHEN <see cref="IExpressionPageResolver"/> IS ALREADY A SEAM. The seam is
/// reached by assigning <see cref="DataWindowExpressionEvaluator.PageResolver"/> or by passing a resolver
/// to the constructor - both of which require a caller who knows to do it. The class default is
/// <see cref="UnresolvedPageResolver"/>, deliberately, so an evaluator constructed without that knowledge
/// REFUSES <c>for page</c>. That is right as a class default and wrong as a DEPLOYED one: it means the
/// running service answers the malformed sentinel for <c>dw_sqlite.srd:L27</c>'s
/// <c>sum(salary for page)</c> - the only <c>for page</c> expression in the repository - unless some
/// wiring site remembers to inject a resolver. A capability that works only when someone remembers is
/// the gap review found, and a seam is not wiring.
/// </para>
/// <para>
/// SO THE DECISION IS MOVED INTO CONFIGURATION, WHERE IT IS STATED ONCE AND ATTRIBUTABLE. The deployment
/// says which pagination its surface has; <see cref="ExpressionPageResolverFactory"/> turns that into the
/// one resolver that expresses it; and the composition root installs it, so nothing downstream can
/// silently fall back to the refusing default.
/// </para>
/// <para>
/// NAMED FOR THE PAGINATION AND NOT FOR THE RESOLVER TYPES, because the setting outlives them: a
/// deployment states a fact about its own surface, not a class name.
/// </para>
/// </remarks>
public enum ExpressionPageResolution
{
    /// <summary>
    /// No pagination is stated, so <c>for page</c> is REFUSED with the malformed sentinel and a
    /// structured error.
    /// </summary>
    /// <remarks>
    /// SELECTABLE ON PURPOSE, AND NOT THE CONFIGURED DEFAULT. A deployment that genuinely does not know
    /// its pagination is better served by a refusal than by a number computed over the wrong rows, and
    /// AAP 0.1.5 requires exactly that - narrow with a defined error rather than widen with a guess. It
    /// remains the CLASS default of <see cref="DataWindowExpressionEvaluator.PageResolver"/> for the same
    /// reason. What it must not be is what a configured, running service silently gets.
    /// </remarks>
    Unresolved = 0,

    /// <summary>
    /// The whole primary buffer is one page: an explicit statement that the surface is unpaginated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE CONFIGURED DEFAULT, AND IT IS EVIDENCED RATHER THAN CONVENIENT. The one surface this system
    /// has evidence for is <c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd</c>, whose only <c>for page</c>
    /// use is the footer sum at <c>:L27</c> and which declares no page-break band - so "this surface is
    /// one page" is the measured truth for it, not an assumption made in the absence of one.
    /// </para>
    /// <para>
    /// IT IS STILL A STATEMENT AND NOT A SHRUG. A paginated deployment MUST choose
    /// <see cref="FixedRowsPerPage"/>, or <see cref="Unresolved"/> to get the refusal back; leaving this
    /// value in place on a paginated surface returns the grand total where the page total was asked for,
    /// which is the widening <see cref="IExpressionPageResolver"/> warns about. The difference between
    /// this and a default that guesses is that here the deployment has said so, and the setting records
    /// who said it.
    /// </para>
    /// </remarks>
    WholeBuffer = 1,

    /// <summary>
    /// A fixed number of detail rows per page, taken from
    /// <c>DataServices:ColumnExpression:PageRowsPerPage</c>.
    /// </summary>
    /// <remarks>
    /// For a deployment that paginates and knows its own page size - a characterization run reproducing
    /// a printed page, or a client that paginates itself. This service acquires no band geometry from it;
    /// the row count is divided, nothing is laid out.
    /// </remarks>
    FixedRowsPerPage = 2,
}

/// <summary>
/// Builds the one <see cref="IExpressionPageResolver"/> a stated
/// <see cref="ExpressionPageResolution"/> means.
/// </summary>
/// <remarks>
/// <para>
/// ONE PLACE THAT MAPS THE SETTING TO A RESOLVER, so a second wiring site cannot map it differently.
/// That is the same reasoning that puts the carrier-value mapper next to the buffers it serves and the
/// identity projection next to its collector: a decision with one home cannot drift.
/// </para>
/// <para>
/// IT LIVES BESIDE THE RESOLVERS RATHER THAN BESIDE THE OPTIONS, because it is the enum's meaning that
/// has to stay in step with the three implementations - add a fourth resolver and this switch is where
/// the compiler and the reader both look.
/// </para>
/// </remarks>
public static class ExpressionPageResolverFactory
{
    /// <summary>
    /// Creates the resolver for <paramref name="resolution"/>.
    /// </summary>
    /// <param name="resolution">The pagination the deployment stated.</param>
    /// <param name="rowsPerPage">
    /// The rows per page, used only when <paramref name="resolution"/> is
    /// <see cref="ExpressionPageResolution.FixedRowsPerPage"/>. Must be positive in that case.
    /// </param>
    /// <returns>The resolver.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="resolution"/> is not a declared member, or it is
    /// <see cref="ExpressionPageResolution.FixedRowsPerPage"/> and <paramref name="rowsPerPage"/> is not
    /// positive.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE UNRECOGNISED ARM THROWS RATHER THAN FALLING BACK, and that is the point of having a factory.
    /// A <c>default:</c> arm that quietly returned the refusing resolver would turn a mistyped setting -
    /// or a fourth member someone forgot to handle here - into the exact silent-sentinel behaviour this
    /// whole change exists to remove. The options validator rejects an out-of-range value first, so in a
    /// configured service this arm is unreachable; it is the backstop for a caller that bypassed
    /// validation, and it fails loudly.
    /// </para>
    /// <para>
    /// The two stateless resolvers hand back their shared instances, because neither holds state and one
    /// instance serves every evaluator.
    /// </para>
    /// </remarks>
    public static IExpressionPageResolver Create(ExpressionPageResolution resolution, int rowsPerPage)
    {
        return resolution switch
        {
            ExpressionPageResolution.Unresolved => UnresolvedPageResolver.Instance,
            ExpressionPageResolution.WholeBuffer => WholeBufferPageResolver.Instance,
            ExpressionPageResolution.FixedRowsPerPage => CreateFixed(rowsPerPage),
            _ => throw new ArgumentOutOfRangeException(
                nameof(resolution),
                resolution,
                "The stated page resolution is not a declared ExpressionPageResolution member. A "
                    + "deployment states Unresolved, WholeBuffer or FixedRowsPerPage; there is "
                    + "deliberately no fallback, because a silent one would reinstate the malformed "
                    + "sentinel for `for page` on a mistyped setting."),
        };
    }

    /// <summary>
    /// Creates the fixed-page resolver, reporting the row count against the setting's own name.
    /// </summary>
    /// <param name="rowsPerPage">The rows per page. Must be positive.</param>
    /// <returns>The resolver.</returns>
    /// <remarks>
    /// A separate method purely so the argument name in the thrown exception is the SETTING's name rather
    /// than the resolver constructor's parameter, which is what an operator reading the failure needs.
    /// </remarks>
    private static IExpressionPageResolver CreateFixed(int rowsPerPage)
    {
        if (rowsPerPage <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rowsPerPage),
                rowsPerPage,
                "DataServices:ColumnExpression:PageRowsPerPage must be positive when PageResolution "
                    + "is FixedRowsPerPage: a page of zero or fewer rows describes no pagination.");
        }

        return new FixedRowsPerPageResolver(rowsPerPage);
    }
}


/// <summary>
/// One call to a registered expression function, presented to its implementation.
/// </summary>
/// <remarks>
/// <para>
/// ARGUMENTS ARE EVALUATED ON DEMAND, NOT UP FRONT, and that is what lets every function in the roster
/// live in the same registry. Three of them cannot accept pre-evaluated arguments:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>if(cond,a,b)</c> must evaluate one branch only. <c>n_cst_dwsvc_contextmenu.sru:L1183</c> relies on
/// it: the false branch is reached when the cursor is on the last row, and the true branch's nested
/// <c>Describe</c> would be asked for the row after the last one if both were evaluated.
/// </description></item>
/// <item><description>
/// An aggregate must evaluate its argument ONCE PER ROW over its scope - <c>:L1132</c>'s
/// <c>Max(Len(LookUpDisplay(col)))</c> is one expression evaluated for every row in the buffer.
/// </description></item>
/// <item><description>
/// <c>Describe</c> may need its argument's TEXT before deciding whether the argument names a property or
/// nests another evaluation.
/// </description></item>
/// </list>
/// <para>
/// A single registry with on-demand arguments serves all three plus the plain functions, which is why
/// there is no separate notion of a special form. That in turn is what makes the roster extensible in
/// the way the folder brief requires: Expressions/ColumnExpressionEngine.cs can register a macro
/// function that routes through Expressions/MacroInvoker.cs and it behaves like any other entry.
/// </para>
/// </remarks>
public sealed class ExpressionFunctionInvocation
{
    private readonly DataWindowExpressionEvaluator _evaluator;
    private readonly Func<int, long, DataWindowExpressionResult> _argumentEvaluator;
    private readonly Func<int, string?> _argumentColumnName;

    /// <summary>
    /// Creates an invocation. Called by the evaluator only.
    /// </summary>
    /// <param name="evaluator">The evaluator running this call.</param>
    /// <param name="name">The function name as written in the expression.</param>
    /// <param name="argumentCount">How many arguments were written.</param>
    /// <param name="row">The one-based row in force, or <c>0</c>.</param>
    /// <param name="scope">The aggregate scope written after the arguments.</param>
    /// <param name="groupLevel">The group level for <see cref="ExpressionAggregateScope.Group"/>.</param>
    /// <param name="expression">The whole expression being evaluated.</param>
    /// <param name="position">The one-based position of the function name within it.</param>
    /// <param name="argumentEvaluator">
    /// Evaluates the argument at a zero-based index against a given row.
    /// </param>
    /// <param name="argumentColumnName">
    /// Answers the column name when the argument at a zero-based index is a bare column reference, and
    /// <see langword="null"/> otherwise.
    /// </param>
    internal ExpressionFunctionInvocation(
        DataWindowExpressionEvaluator evaluator,
        string name,
        int argumentCount,
        long row,
        ExpressionAggregateScope scope,
        long groupLevel,
        string expression,
        long position,
        Func<int, long, DataWindowExpressionResult> argumentEvaluator,
        Func<int, string?> argumentColumnName)
    {
        _evaluator = evaluator;
        _argumentEvaluator = argumentEvaluator;
        _argumentColumnName = argumentColumnName;
        Name = name;
        ArgumentCount = argumentCount;
        Row = row;
        Scope = scope;
        GroupLevel = groupLevel;
        Expression = expression;
        Position = position;
    }

    /// <summary>
    /// The function name exactly as the expression spells it. Preserved rather than normalised so a
    /// diagnostic quotes what the author wrote (DECISION D1 makes the LOOKUP insensitive, not the text).
    /// </summary>
    public string Name { get; }

    /// <summary>The number of arguments written.</summary>
    public int ArgumentCount { get; }

    /// <summary>The one-based row in force, or <c>0</c> for no row context.</summary>
    public long Row { get; }

    /// <summary>
    /// The scope written after the arguments, or <see cref="ExpressionAggregateScope.All"/> when none
    /// was.
    /// </summary>
    public ExpressionAggregateScope Scope { get; }

    /// <summary>
    /// The group level from a <c>for group n</c> clause, or <c>0</c> when the clause named no level.
    /// </summary>
    public long GroupLevel { get; }

    /// <summary>The whole expression, for a diagnostic.</summary>
    public string Expression { get; }

    /// <summary>
    /// The one-based position of this function's name within <see cref="Expression"/>, for the caret in
    /// a diagnostic.
    /// </summary>
    public long Position { get; }

    /// <summary>The evaluator running this call, so a function may recurse deliberately.</summary>
    public DataWindowExpressionEvaluator Evaluator => _evaluator;

    /// <summary>The DataWindow host, reached only through its published contract.</summary>
    public DataWindowServiceHost Host => _evaluator.Host;

    /// <summary>
    /// Evaluates one argument against <see cref="Row"/>.
    /// </summary>
    /// <param name="index">The zero-based argument index.</param>
    /// <returns>The argument's result, which may itself be a sentinel or an abort.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is outside <c>0 .. ArgumentCount - 1</c>. A function must check
    /// <see cref="ArgumentCount"/> first; reaching past the list is a programming error in the function,
    /// not a malformed expression, so it fails loudly instead of answering a sentinel.
    /// </exception>
    public DataWindowExpressionResult EvaluateArgument(int index) => EvaluateArgument(index, Row);

    /// <summary>
    /// Evaluates one argument against a chosen row - the member an aggregate iterates with.
    /// </summary>
    /// <param name="index">The zero-based argument index.</param>
    /// <param name="row">The one-based row to bind, or <c>0</c> for no row context.</param>
    /// <returns>The argument's result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is outside <c>0 .. ArgumentCount - 1</c>.
    /// </exception>
    public DataWindowExpressionResult EvaluateArgument(int index, long row)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, ArgumentCount);

        return _argumentEvaluator(index, row);
    }

    /// <summary>
    /// Evaluates every argument in order against <see cref="Row"/>, stopping at the first that does not
    /// produce a value.
    /// </summary>
    /// <param name="values">
    /// The evaluated values when the return is <see langword="true"/>; an empty array otherwise.
    /// </param>
    /// <param name="failure">
    /// The offending argument's result when the return is <see langword="false"/>; default otherwise.
    /// </param>
    /// <returns><see langword="true"/> when every argument produced a value.</returns>
    /// <remarks>
    /// STOPPING AT THE FIRST FAILURE REPRODUCES THE ORACLE'S OWN LOOP. <c>:L2229-L2236</c> evaluates a
    /// macro's arguments one at a time and returns immediately on <c>"!"</c> or <c>"?"</c>, so the
    /// remaining arguments are never evaluated and their side effects never happen. Evaluating them all
    /// and reporting afterwards would change which sub-expressions run.
    /// </remarks>
    public bool TryEvaluateArguments(
        out ExpressionValue[] values,
        out DataWindowExpressionResult failure)
    {
        ExpressionValue[] evaluated = ArgumentCount == 0 ? [] : new ExpressionValue[ArgumentCount];

        for (int index = 0; index < ArgumentCount; index++)
        {
            DataWindowExpressionResult result = EvaluateArgument(index);
            if (!result.IsSuccess)
            {
                values = [];
                failure = result;
                return false;
            }

            evaluated[index] = result.Value;
        }

        values = evaluated;
        failure = default;
        return true;
    }

    /// <summary>
    /// Answers the column name when the argument at <paramref name="index"/> is written as a bare
    /// column reference rather than as a computed value.
    /// </summary>
    /// <param name="index">The zero-based argument index.</param>
    /// <param name="columnName">
    /// Receives the name exactly as the expression spells it when the return is
    /// <see langword="true"/>; the empty string otherwise.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the argument is a bare column reference with no row offset.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is outside <c>0 .. ArgumentCount - 1</c>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// REQUIRED BY <c>LookUpDisplay</c>, WHICH TAKES A COLUMN AND NOT A VALUE.
    /// <c>n_cst_dwsvc.sru:L289</c> builds <c>Evaluate('LookUpDisplay(<i>colName</i>)',<i>row</i>)</c>, and
    /// resolving a display value needs the column's IDENTITY - its drop-down DataWindow, its code table -
    /// which its value alone cannot supply. Every other function in the roster takes values, which is why
    /// this is a separate member rather than the general argument shape.
    /// </para>
    /// <para>
    /// A row offset disqualifies the argument: <c>col[1]</c> names a different row's value, and
    /// <c>LookUpDisplay</c> has no offset form in the oracle, so admitting it would invent one.
    /// </para>
    /// </remarks>
    public bool TryGetArgumentColumnName(int index, out string columnName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, ArgumentCount);

        string? name = _argumentColumnName(index);
        columnName = name ?? string.Empty;

        return name is not null;
    }

    /// <summary>
    /// Builds a successful result for this call.
    /// </summary>
    /// <param name="value">The value the function produced.</param>
    /// <returns>The result.</returns>
    public DataWindowExpressionResult Success(in ExpressionValue value) =>
        DataWindowExpressionResult.Success(value, Expression, Row);

    /// <summary>
    /// Builds the malformed-expression result for this call, with a caret at the function name.
    /// </summary>
    /// <param name="message">
    /// The diagnostic text. It is embedded in the caret rendering by
    /// <see cref="ParseErrorFormatter.BuildParseError(string?, long, string?)"/>, the very formatter the
    /// oracle uses at <c>n_cst_dwsvc_columnexp.sru:L2402</c>.
    /// </param>
    /// <returns>The result.</returns>
    public DataWindowExpressionResult Invalid(string message) =>
        DataWindowExpressionResult.Invalid(
            DataWindowExpressionEvaluator.CreateEvaluationError(Expression, Position, message),
            Expression,
            Row);

    /// <summary>
    /// Builds the undetermined-value result for this call, with a caret at the function name.
    /// </summary>
    /// <param name="message">The diagnostic text.</param>
    /// <returns>The result.</returns>
    public DataWindowExpressionResult Undetermined(string message) =>
        DataWindowExpressionResult.Undetermined(
            DataWindowExpressionEvaluator.CreateEvaluationError(Expression, Position, message),
            Expression,
            Row);

    /// <summary>
    /// Builds the malformed-expression result for this call from an error another component produced.
    /// </summary>
    /// <param name="error">
    /// The structured error, for instance the one
    /// <see cref="PinyinFirstLetterMatcher.TryInvokeFromExpression"/> hands back when matching is
    /// unavailable.
    /// </param>
    /// <returns>The result.</returns>
    public DataWindowExpressionResult Invalid(ExpressionParseError? error) =>
        DataWindowExpressionResult.Invalid(error, Expression, Row);

    /// <summary>
    /// Builds the arity diagnostic, so every function in the roster words it identically.
    /// </summary>
    /// <param name="expected">A description of the accepted arity, for example <c>"1"</c> or <c>"2 or 3"</c>.</param>
    /// <returns>The malformed-expression result.</returns>
    public DataWindowExpressionResult WrongArity(string expected) =>
        Invalid(
            Formatting.Sprintf(
                "{1} takes {2} argument(s) but the expression supplied {3}.",
                Name,
                expected,
                ArgumentCount.ToString(CultureInfo.InvariantCulture)));
}

/// <summary>
/// The implementation of one DataWindow expression function.
/// </summary>
/// <param name="invocation">The call, carrying on-demand access to its arguments.</param>
/// <returns>
/// The function's result. A function reports a failure by returning a sentinel result rather than by
/// throwing, because an expression is data - the oracle answers <c>"!"</c> for a bad expression and
/// never raises (<c>n_cst_dwsvc_columnexp.sru:L761</c>).
/// </returns>
public delegate DataWindowExpressionResult DataWindowExpressionFunction(
    ExpressionFunctionInvocation invocation);


/// <summary>
/// The reimplementation of PowerBuilder's <c>Describe("Evaluate(&lt;expression&gt;,&lt;row&gt;)")</c>:
/// a DataWindow expression evaluator that reaches its DataWindow only through
/// <see cref="DataWindowServiceHost"/>.
/// </summary>
/// <remarks>
/// See this file's header for why the facility is reimplemented rather than substituted, for the six
/// legacy call sites, for the three-outcome sentinel model and for the ten recorded decisions. This
/// summary states only what a caller needs.
/// <list type="bullet">
/// <item><description>
/// <see cref="Evaluate(string?, long)"/> and <see cref="Evaluate(string?)"/> are the legacy-shaped
/// entry points and return the legacy string, <c>"!"</c> / <c>"?"</c> / <c>""</c> included. They are the
/// ports of <c>n_cst_dwsvc.sru:L217</c> and <c>:L220</c>.
/// </description></item>
/// <item><description>
/// <see cref="TryEvaluate(string?, long)"/> returns the structured result, which keeps a successful empty
/// value distinguishable from an abort.
/// </description></item>
/// <item><description>
/// <see cref="Describe(string?)"/> is the port of <c>#DataWindow.Describe(...)</c> as the legacy actually
/// calls it - it accepts the whole <c>"Evaluate(...)"</c> property text, in any of the quoting
/// conventions, and passes anything that is not an evaluation through to the host.
/// </description></item>
/// <item><description>
/// <see cref="RegisterFunction"/> extends the roster, which is how
/// Expressions/ColumnExpressionEngine.cs routes a macro function through
/// Expressions/MacroInvoker.cs.
/// </description></item>
/// </list>
/// <para>
/// THREAD SAFETY. An instance is not thread-safe and is not meant to be: it holds a recursion depth and a
/// mutable roster, and it is scoped to one DataWindow, which the legacy reaches from one thread by
/// construction (its own threading guidance in <c>docs/PB多线程绕坑提示.md</c> is that a DataWindow is
/// touched from one thread only). One evaluator per session, per request, is the intended lifetime.
/// </para>
/// </remarks>
public sealed class DataWindowExpressionEvaluator
{
    /// <summary>
    /// The value <c>Describe</c> returns for an expression it cannot parse, and for a name that does not
    /// exist. Tested by the oracle at <c>n_cst_dwsvc_columnexp.sru:L761</c> and <c>:L2391</c>.
    /// </summary>
    public const string InvalidExpressionSentinel = "!";

    /// <summary>
    /// The character a bound-value placeholder begins with. NOTHING ELSE in this grammar begins with it, so
    /// recognising it can shadow no identifier, operator, literal or column reference.
    /// </summary>
    public const char BoundValuePlaceholderSigil = ':';

    /// <summary>The full prefix of a bound-value placeholder; the suffix is its zero-based ordinal.</summary>
    public const string BoundValuePlaceholderPrefix = ":pfwVal";

    /// <summary>Builds the placeholder token for a zero-based bound-value ordinal.</summary>
    /// <param name="ordinal">The zero-based ordinal.</param>
    /// <returns>The placeholder token.</returns>
    public static string BoundValuePlaceholder(int ordinal) =>
        BoundValuePlaceholderPrefix + ordinal.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The value <c>Describe</c> returns for an expression whose value cannot be determined. Tested
    /// alongside <see cref="InvalidExpressionSentinel"/> at the same two sites.
    /// </summary>
    public const string UndeterminedValueSentinel = "?";

    /// <summary>
    /// The abort outcome's text - the empty string, and a THIRD outcome rather than an empty value
    /// (<c>n_cst_dwsvc_columnexp.sru:L745</c>, <c>:L2183</c>, <c>:L2186</c>, <c>:L2358</c>).
    /// </summary>
    public const string AbortSentinel = "";

    /// <summary>
    /// The property name that carries an expression to be evaluated.
    /// </summary>
    public const string EvaluateFunctionName = "Evaluate";

    /// <summary>
    /// The expression function that reads a DataWindow property, and that may nest another evaluation
    /// (<c>n_cst_dwsvc_contextmenu.sru:L1183</c>, <c>:L1349</c>).
    /// </summary>
    public const string DescribeFunctionName = "Describe";

    /// <summary>
    /// The row number that means "no row context" - what the one-argument arity supplies
    /// (<c>n_cst_dwsvc.sru:L220</c>) and what <c>:L344</c> and <c>:L782</c> write literally.
    /// </summary>
    public const long NoRowContext = 0L;

    /// <summary>
    /// PowerBuilder's default display format, which means "no mask at all".
    /// </summary>
    /// <remarks>
    /// Spelled in lower case here because <c>n_cst_dwsvc_contextmenu.sru:L1143</c>, <c>:L1168</c>,
    /// <c>:L1309</c> and <c>:L1334</c> all test <c>Lower(sFormat) = "[general]"</c>. The repository
    /// carries BOTH casings and this refactor preserves each where it stands:
    /// <c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd</c> writes <c>format="[general]"</c> on all six
    /// columns at <c>:L21-L26</c> and <c>format="[GENERAL]"</c> on the footer compute at <c>:L27</c>.
    /// Neither is rewritten; <see cref="IsGeneralFormat"/> recognises both, exactly as the oracle does.
    /// </remarks>
    public const string GeneralFormatMask = "[general]";

    /// <summary>
    /// The default ceiling on nested evaluation.
    /// </summary>
    /// <remarks>
    /// A guard rather than a ported behaviour, and stated as a constant so it is visible. Nesting is real
    /// - <c>n_cst_dwsvc_contextmenu.sru:L1183</c> nests one evaluation inside another - and a compute
    /// field whose expression names itself would recur without end. The legacy fails that case by
    /// exhausting the PowerBuilder stack; this file fails it as a malformed expression with a
    /// diagnostic, which is the same class of outcome reached without taking the process down. The
    /// column-expression engine has its own recursion guard for its own recursion
    /// (<c>n_cst_dwsvc_columnexp.sru:L110</c>'s calculation stack); this one covers only nesting inside a
    /// single expression.
    /// </remarks>
    public const int DefaultMaximumRecursionDepth = 32;

    private readonly DataWindowServiceHost _host;
    private readonly PinyinFirstLetterMatcher _pinyinMatcher;

    /// <summary>
    /// The roster. Ordinal-ignore-case by DECISION D1.
    /// </summary>
    private readonly Dictionary<string, DataWindowExpressionFunction> _functions =
        new(StringComparer.OrdinalIgnoreCase);

    private IExpressionPageResolver _pageResolver = UnresolvedPageResolver.Instance;
    private int _maximumRecursionDepth = DefaultMaximumRecursionDepth;
    private int _depth;

    /// <summary>
    /// The bound-value table in force for the evaluation currently running, or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// SCOPED TO ONE CALL AND RESTORED BY ITS <c>finally</c>, not accumulated. Nested evaluation is a real
    /// path here - the recursion ceiling exists for it - and an expression reached through a macro
    /// invocation carries its own bindings or none at all; inheriting a parent's table would let a
    /// placeholder resolve in an expression that never declared it.
    /// </remarks>
    private IReadOnlyDictionary<string, ExpressionValue>? _boundValues;

    /// <summary>
    /// Creates an evaluator over <paramref name="host"/> with pinyin matching blocked.
    /// </summary>
    /// <param name="host">The DataWindow host.</param>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// BLOCKED IS THE HONEST DEFAULT, not a degraded one. AAP 0.6.5 records pinyin first-letter matching
    /// as the single genuine parity risk in the in-scope set, and the risk is precisely the LOOKUP TABLE
    /// AND THE MATCHING RULE: both exist only inside the closed <c>pfw.dll</c>, with no table, data file
    /// or C++ source for either anywhere in the tree.
    /// </para>
    /// <para>
    /// THE FLAGS ARE NOT PART OF THE RISK, WHICH NARROWS IT. The flag value <c>7</c> that
    /// <c>n_cst_dwsvc_dropdownsearch.sru:L323</c> passes IS documented: <c>enums.sru:L1146-L1149</c> names
    /// the three bits, so <c>7 == PY_LIKE_IGNORE_CASE | PY_LIKE_IGNORE_WIDTH | PY_LIKE_FUZZY_SOUND</c>,
    /// carried as <c>Enums.PY_LIKE_*</c> and composed rather than hard-coded. Knowing WHICH behaviours are
    /// switched on does not yield the table they operate over or the rule that consumes it, so the risk
    /// stands - scoped to the native algorithm rather than resting on a documentation gap that does not
    /// exist. An evaluator built without a characterized table therefore reports the gap - as a structured
    /// error, from <see cref="PinyinFirstLetterMatcher.CreateUnavailableExpressionError"/> - rather than
    /// approximating a filter that would return subtly different rows.
    /// </para>
    /// </remarks>
    public DataWindowExpressionEvaluator(DataWindowServiceHost host)
        : this(host, PinyinFirstLetterMatcher.Blocked)
    {
    }

    /// <summary>
    /// Creates an evaluator over <paramref name="host"/> with a supplied pinyin matcher.
    /// </summary>
    /// <param name="host">The DataWindow host.</param>
    /// <param name="pinyinMatcher">
    /// The matcher the <c>PinyinFirstLetterLike</c> function dispatches to.
    /// </param>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    public DataWindowExpressionEvaluator(
        DataWindowServiceHost host,
        PinyinFirstLetterMatcher pinyinMatcher)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(pinyinMatcher);

        _host = host;
        _pinyinMatcher = pinyinMatcher;

        RegisterBuiltInFunctions();
    }

    /// <summary>
    /// Creates an evaluator over <paramref name="host"/> with a supplied pinyin matcher AND a supplied
    /// page resolver - the constructor the composition root uses.
    /// </summary>
    /// <param name="host">The DataWindow host.</param>
    /// <param name="pinyinMatcher">
    /// The matcher the <c>PinyinFirstLetterLike</c> function dispatches to.
    /// </param>
    /// <param name="pageResolver">
    /// The pagination <c>for page</c> is measured against, normally obtained from
    /// <see cref="ExpressionPageResolverFactory.Create(ExpressionPageResolution, int)"/> over the
    /// deployment's own <c>DataServices:ColumnExpression</c> settings.
    /// </param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// WHY A CONSTRUCTOR AND NOT JUST THE SETTABLE PROPERTY. The property is how a test varies the
    /// pagination mid-flight and it stays exactly as it was; it is not how a SERVICE should acquire its
    /// resolver, because an assignment that a wiring site can forget is precisely how the deployed path
    /// came to be running on <see cref="UnresolvedPageResolver"/>. Passing the resolver in means an
    /// evaluator is never briefly wrong, and a wiring site that omits it is visible as a shorter
    /// constructor call rather than as a missing statement somewhere after one.
    /// </para>
    /// <para>
    /// THE TWO SHORTER CONSTRUCTORS ARE UNCHANGED AND KEEP THE REFUSING CLASS DEFAULT. That default is a
    /// deliberate narrowing (AAP 0.1.5) and is right for an evaluator built by code that has stated
    /// nothing about pagination; the finding was never that the default is wrong, but that a configured
    /// service was silently getting it.
    /// </para>
    /// </remarks>
    public DataWindowExpressionEvaluator(
        DataWindowServiceHost host,
        PinyinFirstLetterMatcher pinyinMatcher,
        IExpressionPageResolver pageResolver)
        : this(host, pinyinMatcher)
    {
        ArgumentNullException.ThrowIfNull(pageResolver);

        _pageResolver = pageResolver;
    }

    /// <summary>
    /// The DataWindow host. The ONLY route to the DataWindow from this file (constraint C-D).
    /// </summary>
    public DataWindowServiceHost Host => _host;

    /// <summary>
    /// The matcher <c>PinyinFirstLetterLike</c> dispatches to.
    /// </summary>
    public PinyinFirstLetterMatcher PinyinMatcher => _pinyinMatcher;

    /// <summary>
    /// Resolves the row range that <c>for page</c> covers.
    /// </summary>
    /// <value>
    /// Defaults to <see cref="UnresolvedPageResolver.Instance"/>, which REFUSES <c>for page</c> rather
    /// than widening it to every row. See <see cref="IExpressionPageResolver"/> for why this is a seam
    /// and why the default resolves nothing (DECISION D7a).
    /// </value>
    /// <exception cref="ArgumentNullException">The value assigned is <see langword="null"/>.</exception>
    public IExpressionPageResolver PageResolver
    {
        get => _pageResolver;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _pageResolver = value;
        }
    }

    /// <summary>
    /// The ceiling on nested evaluation.
    /// </summary>
    /// <value>Defaults to <see cref="DefaultMaximumRecursionDepth"/>. Must be positive.</value>
    /// <exception cref="ArgumentOutOfRangeException">The value assigned is zero or negative.</exception>
    public int MaximumRecursionDepth
    {
        get => _maximumRecursionDepth;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _maximumRecursionDepth = value;
        }
    }

    /// <summary>
    /// The names in the roster, in no particular order.
    /// </summary>
    public IReadOnlyCollection<string> FunctionNames => _functions.Keys;

    /// <summary>
    /// Adds a function to the roster, or replaces one already registered under the same name.
    /// </summary>
    /// <param name="name">The function name. Matched case-insensitively (DECISION D1).</param>
    /// <param name="function">The implementation.</param>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or white space.</exception>
    /// <remarks>
    /// <para>
    /// REPLACEMENT IS PERMITTED ON PURPOSE. The column-expression engine has to be able to put a macro
    /// function in front of a built-in of the same name, because
    /// <c>n_cst_dwsvc_columnexp.sru:L2287</c> raises <c>OnColumnExpInvokeMethod</c> for ANY
    /// non-built-in function name the scanner found, without first asking whether the DataWindow already
    /// has a function so spelled. Refusing a duplicate here would make that unreachable.
    /// </para>
    /// </remarks>
    public void RegisterFunction(string name, DataWindowExpressionFunction function)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(function);

        _functions[name] = function;
    }

    /// <summary>
    /// Removes a function from the roster.
    /// </summary>
    /// <param name="name">The function name, matched case-insensitively.</param>
    /// <returns><see langword="true"/> when a registration was removed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public bool RemoveFunction(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return _functions.Remove(name);
    }

    /// <summary>
    /// Reports whether a name is in the roster.
    /// </summary>
    /// <param name="name">The function name, matched case-insensitively.</param>
    /// <returns><see langword="true"/> when it is registered.</returns>
    public bool IsFunctionRegistered(string? name) =>
        name is not null && _functions.ContainsKey(name);


    // ==============================================================================================
    //  ENTRY POINTS
    // ==============================================================================================

    /// <summary>
    /// Evaluates <paramref name="exp"/> against <paramref name="row"/> and returns the legacy string -
    /// the port of <c>n_cst_dwsvc.sru:L217</c>.
    /// </summary>
    /// <param name="exp">
    /// The expression. Any of the three quoting conventions is accepted, so a caller may pass the payload
    /// it already holds without unwrapping it first.
    /// </param>
    /// <param name="row">The one-based row, or <see cref="NoRowContext"/>.</param>
    /// <returns>
    /// The value's text, <see cref="InvalidExpressionSentinel"/>,
    /// <see cref="UndeterminedValueSentinel"/>, or <see cref="AbortSentinel"/>. Never
    /// <see langword="null"/>.
    /// </returns>
    public string Evaluate(string? exp, long row) => TryEvaluate(exp, row).Text;

    /// <summary>
    /// Evaluates <paramref name="exp"/> with no row context - the port of the one-argument arity at
    /// <c>n_cst_dwsvc.sru:L220</c>, which delegates with row <c>0</c>.
    /// </summary>
    /// <param name="exp">The expression.</param>
    /// <returns>The legacy string.</returns>
    /// <remarks>
    /// THE DELEGATION SHAPE IS PRESERVED RATHER THAN DUPLICATED, exactly as <c>:L220</c> writes it:
    /// <c>return _of_Evaluate(exp,0)</c>. There is no second implementation for a caller to drift from,
    /// which is what makes the arity test in this file's suite a real equivalence check.
    /// </remarks>
    public string Evaluate(string? exp) => Evaluate(exp, NoRowContext);

    /// <summary>
    /// Evaluates <paramref name="exp"/> with no row context and returns the structured result.
    /// </summary>
    /// <param name="exp">The expression.</param>
    /// <returns>The result.</returns>
    public DataWindowExpressionResult TryEvaluate(string? exp) => TryEvaluate(exp, NoRowContext);

    /// <summary>
    /// Evaluates an expression carrying BOUND VALUES and returns the structured result.
    /// </summary>
    /// <param name="exp">The executable expression, whose caller-derived values are placeholders.</param>
    /// <param name="row">The one-based row, or <see cref="NoRowContext"/>.</param>
    /// <param name="boundValues">Placeholder token to value.</param>
    /// <returns>The result.</returns>
    /// <remarks>
    /// <para>
    /// <b>THIS IS THE EXECUTION PATH FOR ANY EXPRESSION CARRYING A CALLER-SUPPLIED VALUE.</b> A value that
    /// arrives as a placeholder cannot alter the expression's structure, because the token is lexed as ONE
    /// primary whatever the value inside it is - so a value containing a quote, an operator or a function
    /// call is inert. The rendered form, in which the value IS spliced into the text unescaped exactly as
    /// the oracle splices it [<c>n_cst_dwsvc_columnexp.sru:L1094</c>], is retained for reporting, tracing
    /// and characterization and is never evaluated.
    /// </para>
    /// <para>
    /// AN EMPTY EXPRESSION IS AN ABORT, AND THAT IS THE ONE PLACE THIS FILE ORIGINATES ONE. The oracle
    /// never lets an empty expression reach <c>Describe</c>: <c>n_cst_dwsvc_columnexp.sru:L745</c> guards
    /// it (<c>if sExp = "" then return false</c>), and <c>:L2183</c>, <c>:L2186</c> and <c>:L2358</c>
    /// return the empty string so the abort travels up. Answering the abort here rather than the malformed
    /// sentinel makes the abort SELF-PROPAGATING: a caller that reproduced the chain without the guard
    /// still observes the oracle's outcome instead of a spurious <c>"!"</c>.
    /// </para>
    /// </remarks>
    public DataWindowExpressionResult TryEvaluate(
        string? exp,
        long row,
        IReadOnlyDictionary<string, ExpressionValue>? boundValues)
    {
        IReadOnlyDictionary<string, ExpressionValue>? outer = _boundValues;

        // RESTORED IN A finally SO ONE NESTED EVALUATION CANNOT LEAK ITS TABLE INTO ITS PARENT. Nested
        // evaluation is real here - the depth ceiling below exists for it - and an expression that reached
        // this evaluator through a macro invocation carries its own bindings or none.
        _boundValues = boundValues;

        try
        {
            return TryEvaluate(exp, row);
        }
        finally
        {
            _boundValues = outer;
        }
    }

    /// <summary>
    /// Evaluates an expression carrying BOUND VALUES and returns its legacy string.
    /// </summary>
    /// <param name="exp">The executable expression, whose caller-derived values are placeholders.</param>
    /// <param name="row">The one-based row, or <see cref="NoRowContext"/>.</param>
    /// <param name="boundValues">
    /// Placeholder token to value. A token this table does not name is a lexical error rather than a
    /// silently empty value, because a placeholder in the text with no value behind it can only mean the
    /// text and the table disagree - and guessing at that would evaluate a different expression from the
    /// one the caller composed.
    /// </param>
    /// <returns>The legacy string.</returns>
    public string Evaluate(
        string? exp,
        long row,
        IReadOnlyDictionary<string, ExpressionValue>? boundValues) =>
        TryEvaluate(exp, row, boundValues).Text;

    /// <summary>
    /// Evaluates <paramref name="exp"/> against <paramref name="row"/> and returns the structured result.
    /// </summary>
    /// <param name="exp">The expression, in any of the three quoting conventions.</param>
    /// <param name="row">The one-based row, or <see cref="NoRowContext"/>.</param>
    /// <returns>The result. Never throws for a malformed expression.</returns>
    public DataWindowExpressionResult TryEvaluate(string? exp, long row)
    {
        string expression = NormaliseExpressionPayload(exp);

        if (expression.Length == 0)
        {
            return DataWindowExpressionResult.Abort(expression, row);
        }

        if (_depth >= _maximumRecursionDepth)
        {
            return DataWindowExpressionResult.Invalid(
                CreateEvaluationError(
                    expression,
                    1L,
                    Formatting.Sprintf(
                        "Nested evaluation exceeded the ceiling of {1}. An expression that names itself "
                            + "cannot terminate.",
                        _maximumRecursionDepth.ToString(CultureInfo.InvariantCulture))),
                expression,
                row);
        }

        _depth++;
        try
        {
            return EvaluateNormalised(expression, row);
        }
        finally
        {
            _depth--;
        }
    }

    /// <summary>
    /// Reads a DataWindow property, evaluating it when it is an <c>Evaluate(...)</c> property - the port
    /// of <c>#DataWindow.Describe(...)</c> as the six legacy call sites actually invoke it.
    /// </summary>
    /// <param name="property">
    /// The property text, for instance <c>"Evaluate('LookUpDisplay(name)',3)"</c> or
    /// <c>"name.ColType"</c>.
    /// </param>
    /// <returns>
    /// The evaluation's legacy string for an <c>Evaluate</c> property, otherwise whatever
    /// <see cref="DataWindowServiceHost.Describe"/> answers - its sentinels included, unaltered.
    /// </returns>
    /// <remarks>
    /// THIS IS THE DIVISION OF LABOUR THE REAL RUNTIME HAS. One <c>Describe</c> serves both properties and
    /// expressions there; here the host owns properties and this file owns expressions, and this member is
    /// the seam. Every one of <c>n_cst_dwsvc.sru:L195</c>, <c>:L217</c>, <c>:L289</c>, <c>:L344</c>,
    /// <c>:L782</c> and the context menu's <c>:L534</c>, <c>:L541</c>, <c>:L543</c>, <c>:L617</c>,
    /// <c>:L624</c> and <c>:L626</c> ports through it unchanged.
    /// </remarks>
    public string Describe(string? property) => TryDescribe(property).Text;

    /// <summary>
    /// The structured form of <see cref="Describe(string?)"/>.
    /// </summary>
    /// <param name="property">The property text.</param>
    /// <returns>
    /// The evaluation's result for an <c>Evaluate</c> property; otherwise a successful text result
    /// carrying the host's answer.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A HOST SENTINEL IS CLASSIFIED HERE WITHOUT CHANGING WHAT IS OBSERVED. A plain property read that
    /// answers <c>"!"</c> becomes <see cref="ExpressionEvaluationOutcome.InvalidExpression"/> and one that
    /// answers <c>"?"</c> becomes <see cref="ExpressionEvaluationOutcome.Undetermined"/> - and
    /// <see cref="DataWindowExpressionResult.Text"/> is the same one-character string either way, so
    /// <see cref="Describe(string?)"/> is byte-identical to the oracle and every ported test of the form
    /// <c>if sExp = "!" or sExp = "?"</c> (<c>n_cst_dwsvc.sru:L775</c>) fires exactly when it fired before.
    /// The classification is additive information on a channel the oracle does not have. THIS IS ALSO THE
    /// ONLY SOURCE OF <c>"?"</c> IN THE WHOLE FACILITY: the expression evaluator itself has no
    /// undetermined outcome, because a parsed expression either produces a value - possibly null - or is
    /// malformed. <c>"?"</c> is a property-read answer, which is exactly where the oracle tests for it, at
    /// <c>:L587</c>, <c>:L590</c>, <c>:L598</c>, <c>:L599</c> and <c>:L775</c>.
    /// </para>
    /// <para>
    /// THE NESTED <c>Describe(...)</c> FUNCTION DELIBERATELY DOES NOT CLASSIFY. Inside an expression the
    /// answer is an operand, and <c>n_cst_dwsvc_contextmenu.sru:L1183</c> compares it as a string, so
    /// <see cref="EvaluateDescribeFunction"/> keeps a sentinel as a successful text value. The difference
    /// is between reading a property, which can fail, and using its answer in a comparison, which cannot.
    /// </para>
    /// </remarks>
    public DataWindowExpressionResult TryDescribe(string? property)
    {
        if (string.IsNullOrEmpty(property))
        {
            return DataWindowExpressionResult.Abort(string.Empty, NoRowContext);
        }

        if (TryUnwrapEvaluateProperty(property, out string payload, out long row))
        {
            return TryEvaluate(payload, row);
        }

        string described = _host.Describe(property);

        if (string.Equals(described, InvalidExpressionSentinel, StringComparison.Ordinal))
        {
            return DataWindowExpressionResult.Invalid(
                CreateEvaluationError(
                    property,
                    0L,
                    Formatting.Sprintf(
                        "The DataWindow reports [{1}] as an invalid property expression.",
                        property)),
                property,
                NoRowContext);
        }

        if (string.Equals(described, UndeterminedValueSentinel, StringComparison.Ordinal))
        {
            return DataWindowExpressionResult.Undetermined(
                CreateEvaluationError(
                    property,
                    0L,
                    Formatting.Sprintf(
                        "The DataWindow cannot determine a value for the property [{1}].",
                        property)),
                property,
                NoRowContext);
        }

        return DataWindowExpressionResult.Success(
            ExpressionValue.FromString(described),
            property,
            NoRowContext);
    }

    // ==============================================================================================
    //  THE QUOTING CONVENTIONS
    // ==============================================================================================

    /// <summary>
    /// Splits an <c>Evaluate(&lt;payload&gt;,&lt;row&gt;)</c> property into its normalised payload and its
    /// row.
    /// </summary>
    /// <param name="property">The property text.</param>
    /// <param name="payload">
    /// Receives the expression with its outer quoting removed; the empty string when the return is
    /// <see langword="false"/>.
    /// </param>
    /// <param name="row">
    /// Receives the row, or <see cref="NoRowContext"/> when the property wrote no row argument.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="property"/> is an <c>Evaluate</c> property.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE ROW IS FOUND BY SCANNING FROM THE RIGHT, AND QUOTES ARE DELIBERATELY NOT TRACKED. The row
    /// argument is the text after the LAST comma that is followed by nothing but an integer; if no such
    /// comma exists there is no row argument. Tracking quotes while scanning would be the obvious
    /// implementation and it would be WRONG here, because <c>n_cst_dwsvc.sru:L194</c>, <c>:L343</c> and
    /// <c>:L781</c> each produce a payload with ONE UNMATCHED LEADING DOUBLE QUOTE - a quote-aware scan
    /// would treat the whole remainder as one open string, find no comma, and hand
    /// <c>"if(a&gt;1,2,3),1</c> to the tokenizer as the expression while reporting row 0. The right-hand
    /// scan is immune to that because it never has to decide what is inside a string.
    /// </para>
    /// <para>
    /// The scan cannot misread a payload that merely ENDS in a number, because a top-level closing
    /// parenthesis always separates the two: in <c>Evaluate(Round(x,2))</c> the text after the last comma
    /// is <c>2)</c>, which is not an integer, so no split happens. A payload whose own last argument is a
    /// bare integer is only ambiguous if the property also omits the row argument, and no legacy site
    /// writes that: all six pass a row, five of them literally.
    /// </para>
    /// </remarks>
    public static bool TryUnwrapEvaluateProperty(string? property, out string payload, out long row)
    {
        payload = string.Empty;
        row = NoRowContext;

        if (string.IsNullOrWhiteSpace(property))
        {
            return false;
        }

        string trimmed = property.Trim();

        if (!trimmed.StartsWith(EvaluateFunctionName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Whitespace between the name and its opening parenthesis is legal in a DataWindow expression and
        // costs one loop to accept.
        int cursor = EvaluateFunctionName.Length;
        while (cursor < trimmed.Length && char.IsWhiteSpace(trimmed[cursor]))
        {
            cursor++;
        }

        if (cursor >= trimmed.Length || trimmed[cursor] != '(' || trimmed[^1] != ')')
        {
            return false;
        }

        string inner = trimmed[(cursor + 1)..^1];

        int comma = inner.LastIndexOf(',');
        if (comma >= 0
            && long.TryParse(
                inner.AsSpan(comma + 1).Trim(),
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out long parsedRow))
        {
            row = parsedRow;
            inner = inner[..comma];
        }

        payload = NormaliseExpressionPayload(inner);

        // An `Evaluate()` with nothing inside it is still an Evaluate property; the empty payload then
        // reaches TryEvaluate, which answers the abort. Reporting false here would send the empty property
        // to the host instead, which is not where the oracle sends it.
        return true;
    }

    /// <summary>
    /// Removes the outer quoting from an expression payload, accepting all three legacy conventions.
    /// </summary>
    /// <param name="payload">The payload, possibly quoted.</param>
    /// <returns>The bare expression, trimmed. Never <see langword="null"/>.</returns>
    /// <remarks>
    /// <para>
    /// FOUR SHAPES, THREE OF THEM NAMED IN THE BRIEF AND THE FOURTH MEASURED:
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// SINGLE-QUOTED, <c>'expr'</c> - <c>n_cst_dwsvc.sru:L289</c> and
    /// <c>n_cst_dwsvc_contextmenu.sru:L534</c>, <c>:L541</c>, <c>:L543</c>, <c>:L617</c>, <c>:L624</c>,
    /// <c>:L626</c>. Both quotes are removed. They are the PAYLOAD DELIMITER and not a string literal,
    /// which <c>:L534</c> settles: what it wraps is a column name, and the expression there means the
    /// column's value.
    /// </description></item>
    /// <item><description>
    /// DOUBLE-QUOTED, <c>"expr"</c> - <c>n_cst_dwsvc.sru:L217</c>, written <c>"Evaluate(~""+exp+"~","</c>
    /// where <c>~"</c> is PowerScript's escaped double quote. Both quotes are removed.
    /// </description></item>
    /// <item><description>
    /// LEADING QUOTE ONLY, <c>"expr</c> - <c>n_cst_dwsvc.sru:L195</c>, <c>:L344</c> and <c>:L782</c>,
    /// whose payload is built one line earlier by <c>sExp = "~"" + Mid(sExp,nPos + 1)</c> at <c>:L194</c>,
    /// <c>:L343</c> and <c>:L781</c>. The lone quote is removed. The brief lists these three as
    /// "unquoted", which is true of their format strings and not of their runtime values; handling only
    /// shapes 1 and 2 would leave a stray quote in three of the six sites.
    /// </description></item>
    /// <item><description>
    /// BARE, <c>expr</c> - what <c>n_cst_dwsvc_columnexp.sru:L750</c> and <c>:L2390</c> pass to
    /// <c>_of_Evaluate</c>, which then applies shape 2 itself. Returned unchanged apart from trimming.
    /// A bare expression may itself BEGIN and END with a quote - <c>'a' + 'b'</c> does - so shapes 1 and 2
    /// are recognised by scanning the first literal to its end and requiring that it reach the last
    /// character, not by comparing the first and last characters. See
    /// <see cref="FindClosingDelimiter"/>.
    /// </description></item>
    /// </list>
    /// <para>
    /// A LONE LEADING SINGLE QUOTE IS NOT STRIPPED, deliberately. No legacy site produces one, and
    /// leaving it makes the tokenizer report an unterminated string - the malformed sentinel - rather than
    /// silently reinterpreting text the oracle would have rejected.
    /// </para>
    /// <para>
    /// ONE AMBIGUITY REMAINS AFTER THAT SCAN AND IS RESOLVED IN FAVOUR OF THE DELIMITER READING, RECORDED
    /// HERE RATHER THAN HIDDEN. A payload that is EXACTLY one complete literal - <c>''</c>, <c>'name'</c>,
    /// <c>'a~~b'</c> - could be shape 1 delimiting the text inside it or shape 4 consisting of a single
    /// string literal, and no scan can tell those apart. The delimiter reading wins, because that is the
    /// shape all six legacy call sites produce. Two consequences follow and both match the oracle:
    /// <c>''</c> yields the empty payload and therefore the abort, which is what the oracle does with an
    /// empty expression at <c>n_cst_dwsvc_columnexp.sru:L745</c>; and a caller that genuinely wants a lone
    /// string literal evaluated reaches it the way the oracle does, by splicing it into a larger expression
    /// - <c>:L2213</c> and <c>:L2310</c> both wrap a substituted literal in parentheses as
    /// <c>sVal = "(" + sVal + ")"</c> before it is ever evaluated.
    /// </para>
    /// </remarks>
    public static string NormaliseExpressionPayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return string.Empty;
        }

        string trimmed = payload.Trim();

        if (trimmed[0] != '\'' && trimmed[0] != '"')
        {
            return trimmed;
        }

        int closing = FindClosingDelimiter(trimmed);

        if (closing == trimmed.Length - 1)
        {
            // Shapes 1 and 2: the quote pair delimits the WHOLE payload, so it is the delimiter.
            return trimmed[1..closing].Trim();
        }

        if (closing < 0 && trimmed[0] == '"')
        {
            // Shape 3: no closing quote anywhere, so this is n_cst_dwsvc.sru:L194's lone leading quote.
            return trimmed[1..].Trim();
        }

        // Shape 4: the payload merely BEGINS with a literal - `'a' + 'b'` closes its first quote at
        // index 2 and has seven more characters after it - so it is a bare expression and the tokenizer
        // reads the literal itself. Also the lone leading SINGLE quote, left in place on purpose.
        return trimmed;
    }

    /// <summary>
    /// Finds the index of the quote that closes the literal beginning at index <c>0</c>.
    /// </summary>
    /// <param name="payload">The trimmed payload, whose first character is a quote.</param>
    /// <returns>The closing quote's index, or <c>-1</c> when the literal never closes.</returns>
    /// <remarks>
    /// <para>
    /// THIS IS WHAT SEPARATES A DELIMITER FROM A LITERAL, and getting it wrong is worse than either
    /// reading alone. Testing only the first and last characters - the obvious implementation - strips the
    /// outer quotes off <c>'a' + 'b'</c> and hands <c>a' + 'b</c> to the tokenizer, which then reports a
    /// perfectly valid expression as malformed. Scanning the first literal to its end and requiring that it
    /// reach the very last character is what tells the two apart.
    /// </para>
    /// <para>
    /// The scan honours PowerScript's tilde escape so a literal containing an escaped quote is measured
    /// correctly, and it accepts either quote character with the opening one fixing the closing one - the
    /// same two rules <see cref="TryReadStringLiteral"/> applies.
    /// </para>
    /// </remarks>
    private static int FindClosingDelimiter(string payload)
    {
        char quote = payload[0];

        for (int index = 1; index < payload.Length; index++)
        {
            char current = payload[index];

            if (current == '~')
            {
                // The escaped character cannot close the literal, whatever it is.
                index++;
                continue;
            }

            if (current == quote)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Reports whether a display format is PowerBuilder's general format, which means "no mask".
    /// </summary>
    /// <param name="format">The format text, as read from a <c>.Format</c> property.</param>
    /// <returns>
    /// <see langword="true"/> for the general mask in either casing, for the empty string, for
    /// <see langword="null"/>, and for the two <c>Describe</c> sentinels.
    /// </returns>
    /// <remarks>
    /// THE PORT OF ONE CONDITION WRITTEN FOUR TIMES:
    /// <c>if sFormat = "!" or sFormat = "?" or Lower(sFormat) = "[general]" then sFormat = ""</c>
    /// at <c>n_cst_dwsvc_contextmenu.sru:L1143</c>, <c>:L1168</c>, <c>:L1309</c> and <c>:L1334</c>. All
    /// four arms are reproduced, including the two sentinels - a column with no format property answers
    /// <c>"!"</c>, and the oracle treats that as "no mask" rather than as an error.
    /// The comparison is ordinal-ignore-case for the reason given in DECISION D1; <c>Lower()</c>'s culture
    /// sensitivity cannot be observed over the seven ASCII letters of this token.
    /// </remarks>
    public static bool IsGeneralFormat(string? format) =>
        string.IsNullOrWhiteSpace(format)
            || string.Equals(format, InvalidExpressionSentinel, StringComparison.Ordinal)
            || string.Equals(format, UndeterminedValueSentinel, StringComparison.Ordinal)
            || string.Equals(format.Trim(), GeneralFormatMask, StringComparison.OrdinalIgnoreCase);


    // ==============================================================================================
    //  STRUCTURED ERRORS
    // ==============================================================================================

    /// <summary>
    /// Builds the structured error this file reports a failure with.
    /// </summary>
    /// <param name="expression">The expression the failure occurred in.</param>
    /// <param name="position">
    /// The one-based position of the offending token within it, or <c>0</c> when the failure has no
    /// position.
    /// </param>
    /// <param name="message">The diagnostic text.</param>
    /// <returns>The error, carrying both of the legacy formatter's renderings when it has a position.</returns>
    /// <remarks>
    /// <para>
    /// SITE IS <see cref="ExpressionErrorSite.Unspecified"/> BECAUSE NO LEGACY LINE CAN BE CITED, and that
    /// is a requirement rather than a shortcut. <see cref="ParseErrorFormatter"/>'s two factories accept
    /// only the 28 dialog sites the column-expression engine raises and they stamp each site's legacy
    /// Chinese message; routing an evaluator failure through one of them would claim the oracle raised it,
    /// which is false - the oracle's evaluator is inside the closed DataWindow runtime and raises nothing,
    /// it merely answers <c>"!"</c>. The precedent is exact and deliberate:
    /// <see cref="PinyinFirstLetterMatcher.CreateUnavailableExpressionError"/> takes the same position for
    /// the same structural reason, as does the cross-session foreign-variable narrowing.
    /// </para>
    /// <para>
    /// THE RENDERINGS ARE THE ORACLE'S OWN, THOUGH. Both come from
    /// <see cref="ParseErrorFormatter.BuildParseError(string?, long, string?)"/>, which is the port of
    /// <c>_of_BuildParseError</c> at <c>n_cst_dwsvc_columnexp.sru:L2402-L2407</c> - the same underscore
    /// padding, the same caret, the same parenthesised position, the same DBCS-aware column count. A
    /// caller therefore sees one caret format across the engine whether the failure came from the macro
    /// scanner or from here.
    /// </para>
    /// <para>
    /// <see cref="ExpressionParseError.Localized"/> is false and the localization category is zero,
    /// matching all 28 legacy sites: those messages are hardcoded and do NOT route through the
    /// localization surface, an inconsistency this refactor preserves rather than harmonises (AAP 0.2.1.3
    /// Correction 5). The return code is <see cref="RetCode.E_INVALID_ARGUMENT"/>, which is the code the
    /// ten caret-bearing legacy parse sites answer, and the category is
    /// <see cref="ExpressionErrorCategory.Expression"/> - the general expression-evaluation failure, and
    /// deliberately not <see cref="ExpressionErrorCategory.ForeignReferenceBlocked"/>, which belongs to the
    /// foreign-variable narrowing alone.
    /// </para>
    /// </remarks>
    public static ExpressionParseError CreateEvaluationError(
        string? expression,
        long position,
        string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        string syntax = expression ?? string.Empty;
        bool hasCaret = position > 0L;

        return new ExpressionParseError
        {
            Site = ExpressionErrorSite.Unspecified,
            Family = hasCaret
                ? ExpressionErrorFamily.CaretBearingParseError
                : ExpressionErrorFamily.PlainMessage,
            Category = ExpressionErrorCategory.Expression,
            Severity = ExpressionErrorSeverity.StopSign,
            Title = ExpressionErrorCatalog.LegacyTitle,
            Text = hasCaret
                ? ParseErrorFormatter.BuildParseError(syntax, position, message)
                : message,
            Localized = false,
            LocalizationCategory = 0L,
            // No substitution happens - the message arrives already composed - so the template IS the
            // message and the argument list is empty. Stating both keeps the record self-consistent for a
            // reader who expects a template and its arguments to agree.
            FormatTemplate = message,
            FormatArguments = ImmutableArray<string>.Empty,
            ReturnCode = RetCode.E_INVALID_ARGUMENT,
            Expression = syntax,
            CaretPosition = hasCaret ? position : null,
            // Neither legacy convention applies: those two describe where the MACRO SCANNER puts its
            // caret. This file's caret is at the offending token's first character, which is its own
            // convention and is recorded as unspecified rather than misfiled under one of theirs.
            CaretConvention = CaretPositionConvention.Unspecified,
            RenderedMarker = hasCaret ? ParseErrorFormatter.BuildParseError(syntax, position) : null,
        };
    }

    // ==============================================================================================
    //  TOKENIZER
    // ==============================================================================================

    /// <summary>The lexical classes the tokenizer produces.</summary>
    private enum TokenKind
    {
        End = 0,
        Number = 1,
        Text = 2,
        Identifier = 3,
        Operator = 4,
        LeftParen = 5,
        RightParen = 6,
        LeftBracket = 7,
        RightBracket = 8,
        Comma = 9,
    }

    /// <summary>
    /// One token. <c>Position</c> is ONE-BASED so it can be handed to the legacy caret formatter without
    /// adjustment (DECISION D10).
    /// </summary>
    private readonly record struct Token(
        TokenKind Kind,
        string Text,
        ExpressionValue Value,
        long Position);

    /// <summary>
    /// Splits an expression into tokens.
    /// </summary>
    /// <param name="expression">The expression, already normalised and non-empty.</param>
    /// <param name="boundValues">The ordered values bound to the statement.</param>
    /// <param name="tokens">The tokens, terminated by an <see cref="TokenKind.End"/> token.</param>
    /// <param name="error">The diagnostic when the return is <see langword="false"/>.</param>
    /// <param name="position">The one-based position of the offending character.</param>
    /// <returns><see langword="true"/> when the whole expression tokenized.</returns>
    /// <remarks>
    /// <para>
    /// NO EXCEPTIONS ANYWHERE IN THE FRONT END. A malformed expression is DATA - it arrives from
    /// <c>of_SetExp</c>, from a drop-down filter assembled by concatenation
    /// (<c>n_cst_dwsvc_dropdownsearch.sru:L319-L334</c>), or from a DataWindow definition - and the oracle
    /// answers <c>"!"</c> for it rather than raising. Reporting through a return value keeps that true by
    /// construction.
    /// </para>
    /// </remarks>
    private static bool TryTokenize(
        string expression,
        IReadOnlyDictionary<string, ExpressionValue>? boundValues,
        out List<Token> tokens,
        out string? error,
        out long position)
    {
        tokens = [];
        error = null;
        position = 0L;

        int index = 0;
        while (index < expression.Length)
        {
            char current = expression[index];

            if (char.IsWhiteSpace(current))
            {
                index++;
                continue;
            }

            long start = index + 1L;

            // ---------------------------------------------------------------------------------------------
            //  THE BOUND-VALUE PLACEHOLDER, LEXED AS ONE PRIMARY
            //  -------------------------------------------------------------------------------------------
            //  A placeholder is the ONLY construct in this grammar beginning with a colon, so recognising it
            //  costs nothing and can shadow nothing. The value it names becomes the token's value directly,
            //  so the caller's data never passes through the lexer as text - which is what makes a value
            //  containing a quote, an operator or a whole function call inert rather than executable.
            //
            //  THE TOKEN KIND IS Text FOR EVERY VALUE KIND, WHICH IS NOT A LOSS. ParsePrimary treats Number
            //  and Text identically - both become a LiteralNode over the token's ExpressionValue - so the
            //  kind carries no information past this point and the VALUE carries all of it.
            //
            //  AN UNKNOWN PLACEHOLDER IS A LEXICAL ERROR, NOT AN EMPTY VALUE. It can only mean the text and
            //  the bound table disagree, and substituting nothing would evaluate a DIFFERENT expression
            //  from the one composed - the class of defect that returns a plausible answer.
            // ---------------------------------------------------------------------------------------------
            if (current == BoundValuePlaceholderSigil
                && expression.AsSpan(index).StartsWith(BoundValuePlaceholderPrefix, StringComparison.Ordinal))
            {
                int scan = index + BoundValuePlaceholderPrefix.Length;

                while (scan < expression.Length && char.IsAsciiDigit(expression[scan]))
                {
                    scan++;
                }

                if (scan > index + BoundValuePlaceholderPrefix.Length)
                {
                    string placeholder = expression[index..scan];

                    if (boundValues is null
                        || !boundValues.TryGetValue(placeholder, out ExpressionValue bound))
                    {
                        error = Formatting.Sprintf(
                            "'{1}' is a bound-value placeholder with no value supplied for it.",
                            placeholder);
                        position = start;

                        return false;
                    }

                    tokens.Add(new Token(TokenKind.Text, placeholder, bound, start));
                    index = scan;

                    continue;
                }
            }

            switch (current)
            {
                case '(':
                    tokens.Add(new Token(TokenKind.LeftParen, "(", ExpressionValue.Null, start));
                    index++;
                    continue;
                case ')':
                    tokens.Add(new Token(TokenKind.RightParen, ")", ExpressionValue.Null, start));
                    index++;
                    continue;
                case '[':
                    tokens.Add(new Token(TokenKind.LeftBracket, "[", ExpressionValue.Null, start));
                    index++;
                    continue;
                case ']':
                    tokens.Add(new Token(TokenKind.RightBracket, "]", ExpressionValue.Null, start));
                    index++;
                    continue;
                case ',':
                    tokens.Add(new Token(TokenKind.Comma, ",", ExpressionValue.Null, start));
                    index++;
                    continue;
                case '+':
                case '-':
                case '*':
                case '/':
                case '^':
                case '=':
                    tokens.Add(
                        new Token(
                            TokenKind.Operator,
                            current.ToString(),
                            ExpressionValue.Null,
                            start));
                    index++;
                    continue;

                case '<':
                case '>':
                // The three two-character relational operators. `<>` is PowerBuilder's inequality;
                // `!=` is NOT accepted, because PowerScript does not have it and accepting it would
                // admit expressions the oracle rejects (constraint C-B).
                {
                    string text = current.ToString();
                    if (index + 1 < expression.Length)
                    {
                        char next = expression[index + 1];
                        if ((current == '<' && (next == '>' || next == '='))
                            || (current == '>' && next == '='))
                        {
                            text += next;
                        }
                    }

                    tokens.Add(new Token(TokenKind.Operator, text, ExpressionValue.Null, start));
                    index += text.Length;
                    continue;
                }

                case '\'':
                case '"':
                {
                    if (!TryReadStringLiteral(
                            expression,
                            ref index,
                            current,
                            out string literal,
                            out error))
                    {
                        position = start;
                        return false;
                    }

                    tokens.Add(
                        new Token(
                            TokenKind.Text,
                            literal,
                            ExpressionValue.FromString(literal),
                            start));
                    continue;
                }

                default:
                    if (char.IsAsciiDigit(current)
                        || (current == '.'
                            && index + 1 < expression.Length
                            && char.IsAsciiDigit(expression[index + 1])))
                    {
                        if (!TryReadNumber(expression, ref index, out ExpressionValue number, out error))
                        {
                            position = start;
                            return false;
                        }

                        tokens.Add(
                            new Token(
                                TokenKind.Number,
                                number.ToDisplayText() ?? string.Empty,
                                number,
                                start));
                        continue;
                    }

                    if (IsIdentifierStart(current))
                    {
                        int from = index;
                        index++;
                        while (index < expression.Length && IsIdentifierPart(expression[index]))
                        {
                            index++;
                        }

                        tokens.Add(
                            new Token(
                                TokenKind.Identifier,
                                expression[from..index],
                                ExpressionValue.Null,
                                start));
                        continue;
                    }

                    error = Formatting.Sprintf(
                        "Unexpected character '{1}' in the expression.",
                        current.ToString());
                    position = start;
                    return false;
            }
        }

        tokens.Add(
            new Token(TokenKind.End, string.Empty, ExpressionValue.Null, expression.Length + 1L));

        return true;
    }

    /// <summary>
    /// Reports whether <paramref name="value"/> may begin an identifier.
    /// </summary>
    /// <param name="value">The character.</param>
    /// <returns><see langword="true"/> when it may.</returns>
    /// <remarks>
    /// <c>#</c> IS AN IDENTIFIER START, AND THAT IS NOT DEFENSIVE. The oracle addresses a column by
    /// POSITION as well as by name: <c>_of_LookupDisplay(row,colNum)</c> at
    /// <c>n_cst_dwsvc.sru:L292</c> composes <c>"#" + String(colNum)</c> and hands it to the by-name
    /// overload, which at <c>:L289</c> builds <c>Evaluate('LookUpDisplay(#1)',row)</c>. The same
    /// composition appears at <c>:L244</c>, <c>:L268</c>, <c>:L348</c> and <c>:L351</c>. Refusing
    /// <c>#</c> would make every positional call site a malformed expression.
    /// </remarks>
    private static bool IsIdentifierStart(char value) =>
        char.IsAsciiLetter(value) || value == '_' || value == '#';

    /// <summary>
    /// Reports whether <paramref name="value"/> may continue an identifier.
    /// </summary>
    /// <param name="value">The character.</param>
    /// <returns><see langword="true"/> when it may.</returns>
    private static bool IsIdentifierPart(char value) =>
        char.IsAsciiLetterOrDigit(value) || value == '_' || value == '#';


    /// <summary>
    /// Reads a quoted string literal, resolving PowerScript's tilde escapes.
    /// </summary>
    /// <param name="expression">The whole expression.</param>
    /// <param name="index">The index of the opening quote; advanced past the closing quote on success.</param>
    /// <param name="quote">The opening quote character, which the closing one must match.</param>
    /// <param name="literal">The decoded text.</param>
    /// <param name="error">The diagnostic when the return is <see langword="false"/>.</param>
    /// <returns><see langword="false"/> when the literal is not closed.</returns>
    /// <remarks>
    /// <para>
    /// BOTH QUOTE CHARACTERS ARE LEGAL AND THE OPENING ONE FIXES THE CLOSING ONE. PowerBuilder accepts
    /// either, and the oracle uses both: the drop-down filter writes
    /// <c>LIKE '%...%'</c> at <c>n_cst_dwsvc_dropdownsearch.sru:L319</c> while the nested
    /// <c>Describe</c> at <c>n_cst_dwsvc_contextmenu.sru:L1183</c> is written with double quotes inside
    /// single-quoted PowerScript. Matching the pair is what lets a single quote appear unescaped inside a
    /// double-quoted literal, which is how <c>Describe("Evaluate('col',2)")</c> works at all.
    /// </para>
    /// <para>
    /// THE ESCAPE SET IS POWERSCRIPT'S, not C#'s. <c>~</c> is the escape character, and the recognised
    /// sequences are <c>~~</c>, <c>~'</c>, <c>~"</c>, <c>~t</c>, <c>~n</c>, <c>~r</c>, <c>~f</c>,
    /// <c>~b</c>, <c>~v</c>, a three-digit decimal code, <c>~h</c> plus two hex digits and <c>~o</c> plus
    /// three octal digits. <c>~t</c> and <c>~n</c> are the two that matter most here: the oracle searches
    /// property values for a tab with <c>Pos(sExp,"~t")</c> at <c>n_cst_dwsvc.sru:L189</c>, <c>:L338</c>
    /// and <c>:L777</c>, and builds every diagnostic around <c>~n</c>.
    /// </para>
    /// <para>
    /// AN UNRECOGNISED SEQUENCE YIELDS THE CHARACTER ITSELF, so <c>~x</c> is <c>x</c>. That is
    /// PowerScript's own behaviour and it is preserved rather than reported, because reporting it would
    /// reject text the oracle accepts.
    /// </para>
    /// </remarks>
    private static bool TryReadStringLiteral(
        string expression,
        ref int index,
        char quote,
        out string literal,
        out string? error)
    {
        StringBuilder builder = new();

        // Step past the opening quote.
        int cursor = index + 1;

        while (cursor < expression.Length)
        {
            char current = expression[cursor];

            if (current == quote)
            {
                index = cursor + 1;
                literal = builder.ToString();
                error = null;
                return true;
            }

            if (current != '~')
            {
                builder.Append(current);
                cursor++;
                continue;
            }

            cursor++;
            if (cursor >= expression.Length)
            {
                break;
            }

            char escaped = expression[cursor];
            switch (escaped)
            {
                case '~':
                    builder.Append('~');
                    cursor++;
                    break;
                case '\'':
                    builder.Append('\'');
                    cursor++;
                    break;
                case '"':
                    builder.Append('"');
                    cursor++;
                    break;
                case 't':
                    builder.Append('\t');
                    cursor++;
                    break;
                case 'n':
                    builder.Append('\n');
                    cursor++;
                    break;
                case 'r':
                    builder.Append('\r');
                    cursor++;
                    break;
                case 'f':
                    builder.Append('\f');
                    cursor++;
                    break;
                case 'b':
                    builder.Append('\b');
                    cursor++;
                    break;
                case 'v':
                    builder.Append('\v');
                    cursor++;
                    break;
                case 'h':
                case 'H':
                    AppendNumericEscape(expression, builder, ref cursor, digits: 2, radix: 16);
                    break;
                case 'o':
                case 'O':
                    AppendNumericEscape(expression, builder, ref cursor, digits: 3, radix: 8);
                    break;
                default:
                    if (char.IsAsciiDigit(escaped))
                    {
                        // The decimal form has NO letter prefix, so the cursor is already on the first
                        // digit and must stay there - AppendNumericEscape derives `start` from it directly
                        // for radix 10, where the hex and octal forms have it skip a prefix character.
                        // Decrementing here would aim the scan at the tilde itself, whose digit value is
                        // -1, so nothing would decode and `~065` would emit as four literal characters
                        // instead of the letter A.
                        AppendNumericEscape(expression, builder, ref cursor, digits: 3, radix: 10);
                        break;
                    }

                    builder.Append(escaped);
                    cursor++;
                    break;
            }
        }

        literal = string.Empty;
        error = Formatting.Sprintf(
            "The string literal opened with {1} is never closed.",
            quote.ToString());
        return false;
    }

    /// <summary>
    /// Appends one numeric tilde escape and advances past it.
    /// </summary>
    /// <param name="expression">The whole expression.</param>
    /// <param name="builder">Receives the decoded character.</param>
    /// <param name="cursor">
    /// On entry, the index of the letter prefix for a hex or octal escape and of the first digit for a
    /// decimal one; advanced past the whole escape on return.
    /// </param>
    /// <param name="digits">How many digits the form takes.</param>
    /// <param name="radix">The radix - 16, 8 or 10.</param>
    /// <remarks>
    /// A SHORT OR MALFORMED SEQUENCE IS NOT AN ERROR. PowerScript decodes as many digits as it finds and
    /// takes the rest literally, so this method never fails: it consumes what it can and appends whatever
    /// that describes, leaving any leftover character to the caller's next loop. Failing here would reject
    /// text the oracle accepts.
    /// </remarks>
    private static void AppendNumericEscape(
        string expression,
        StringBuilder builder,
        ref int cursor,
        int digits,
        int radix)
    {
        // Step past the letter prefix for the hex and octal forms; the decimal form has none and the
        // caller has already positioned the cursor on its first digit.
        int start = radix == 10 ? cursor : cursor + 1;
        int scanned = start;
        int value = 0;
        int taken = 0;

        while (taken < digits && scanned < expression.Length)
        {
            int digit = DigitValue(expression[scanned], radix);
            if (digit < 0)
            {
                break;
            }

            value = (value * radix) + digit;
            scanned++;
            taken++;
        }

        if (taken == 0)
        {
            // Nothing decodable followed the prefix. Emit the prefix character itself, which is what
            // taking the sequence literally means.
            builder.Append(expression[cursor]);
            cursor++;
            return;
        }

        builder.Append((char)value);
        cursor = scanned;
    }

    /// <summary>
    /// The value of one digit in a radix, or <c>-1</c> when the character is not a digit of it.
    /// </summary>
    /// <param name="value">The character.</param>
    /// <param name="radix">The radix - 16, 8 or 10.</param>
    /// <returns>The value, or <c>-1</c>.</returns>
    private static int DigitValue(char value, int radix)
    {
        int digit;

        if (char.IsAsciiDigit(value))
        {
            digit = value - '0';
        }
        else if (radix == 16 && char.IsAsciiHexDigit(value))
        {
            digit = char.IsAsciiLetterLower(value) ? value - 'a' + 10 : value - 'A' + 10;
        }
        else
        {
            return -1;
        }

        return digit < radix ? digit : -1;
    }

    /// <summary>
    /// Reads a numeric literal.
    /// </summary>
    /// <param name="expression">The whole expression.</param>
    /// <param name="index">The index of the first character; advanced past the literal on success.</param>
    /// <param name="value">The parsed value.</param>
    /// <param name="error">The diagnostic when the return is <see langword="false"/>.</param>
    /// <returns><see langword="false"/> when the text is not a number this evaluator can hold.</returns>
    /// <remarks>
    /// <para>
    /// THE ARM A LITERAL LANDS ON IS DETERMINED BY ITS SPELLING, AND THAT IS A PARITY DECISION.
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// Digits only become <see cref="ExpressionValueKind.Long"/>, so <c>Max(Len(...))</c> against a
    /// literal stays integral and <c>String(...)</c> of it carries no decimal point.
    /// </description></item>
    /// <item><description>
    /// A decimal point makes it <see cref="ExpressionValueKind.Decimal"/> and NOT a double, because
    /// decimal keeps the written scale: <c>0.50</c> renders as <c>"0.50"</c>, which is what byte-exact
    /// output against a <c>decimal(2)</c> column requires (<c>dw_sqlite.srd:L12</c>).
    /// </description></item>
    /// <item><description>
    /// An exponent makes it <see cref="ExpressionValueKind.Double"/>, because exponential notation IS the
    /// binary floating-point spelling and no scale is being asserted.
    /// </description></item>
    /// </list>
    /// <para>
    /// A literal too large for its arm falls back to the next wider one rather than failing, and only a
    /// value no arm can hold is reported.
    /// </para>
    /// </remarks>
    private static bool TryReadNumber(
        string expression,
        ref int index,
        out ExpressionValue value,
        out string? error)
    {
        int from = index;
        bool hasPoint = false;
        bool hasExponent = false;

        while (index < expression.Length)
        {
            char current = expression[index];

            if (char.IsAsciiDigit(current))
            {
                index++;
                continue;
            }

            if (current == '.' && !hasPoint && !hasExponent)
            {
                hasPoint = true;
                index++;
                continue;
            }

            if ((current == 'e' || current == 'E') && !hasExponent && index > from)
            {
                // An exponent must be followed by at least one digit, optionally signed. When it is not,
                // the letter belongs to whatever follows - `1e` is the number 1 next to the identifier
                // `e` - so the scan stops here rather than consuming it.
                int lookahead = index + 1;
                if (lookahead < expression.Length
                    && (expression[lookahead] == '+' || expression[lookahead] == '-'))
                {
                    lookahead++;
                }

                if (lookahead < expression.Length && char.IsAsciiDigit(expression[lookahead]))
                {
                    hasExponent = true;
                    index = lookahead + 1;
                    continue;
                }
            }

            break;
        }

        string text = expression[from..index];

        if (hasExponent)
        {
            if (double.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double asDouble))
            {
                value = ExpressionValue.FromDouble(asDouble);
                error = null;
                return true;
            }
        }
        else if (hasPoint)
        {
            if (decimal.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out decimal asDecimal))
            {
                value = ExpressionValue.FromDecimal(asDecimal);
                error = null;
                return true;
            }

            // Past decimal's range but inside double's. Widening keeps the literal usable, which is what
            // PowerBuilder does with an over-long numeric constant.
            if (double.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double widened))
            {
                value = ExpressionValue.FromDouble(widened);
                error = null;
                return true;
            }
        }
        else
        {
            if (long.TryParse(
                    text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long asLong))
            {
                value = ExpressionValue.FromLong(asLong);
                error = null;
                return true;
            }

            if (decimal.TryParse(
                    text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out decimal widened))
            {
                value = ExpressionValue.FromDecimal(widened);
                error = null;
                return true;
            }
        }

        value = ExpressionValue.Null;
        error = Formatting.Sprintf("'{1}' is not a number this evaluator can hold.", text);
        return false;
    }


    // ==============================================================================================
    //  SYNTAX TREE
    // ==============================================================================================

    /// <summary>The binary operators PowerBuilder's expression grammar defines.</summary>
    private enum BinaryOperator
    {
        Or = 0,
        And = 1,
        Equal = 2,
        NotEqual = 3,
        Less = 4,
        Greater = 5,
        LessOrEqual = 6,
        GreaterOrEqual = 7,
        Like = 8,
        Add = 9,
        Subtract = 10,
        Multiply = 11,
        Divide = 12,
        Power = 13,
    }

    /// <summary>The unary operators.</summary>
    private enum UnaryOperator
    {
        Negate = 0,
        Identity = 1,
        Not = 2,
    }

    /// <summary>One node of a parsed expression. <c>Position</c> is one-based, for the caret.</summary>
    private abstract class Node
    {
        protected Node(long position) => Position = position;

        public long Position { get; }
    }

    /// <summary>A literal - a number or a quoted string.</summary>
    private sealed class LiteralNode : Node
    {
        public LiteralNode(long position, in ExpressionValue value)
            : base(position) => Value = value;

        public ExpressionValue Value { get; }
    }

    /// <summary>
    /// A reference to a DataWindow object by name, with an optional relative row offset.
    /// </summary>
    private sealed class ColumnNode : Node
    {
        public ColumnNode(long position, string name, Node? offset)
            : base(position)
        {
            Name = name;
            Offset = offset;
        }

        public string Name { get; }

        public Node? Offset { get; }
    }

    /// <summary>A call to a registered function, with an optional aggregate scope.</summary>
    private sealed class CallNode : Node
    {
        public CallNode(
            long position,
            string name,
            List<Node> arguments,
            ExpressionAggregateScope scope,
            long groupLevel)
            : base(position)
        {
            Name = name;
            Arguments = arguments;
            Scope = scope;
            GroupLevel = groupLevel;
        }

        public string Name { get; }

        public List<Node> Arguments { get; }

        public ExpressionAggregateScope Scope { get; }

        public long GroupLevel { get; }
    }

    /// <summary>A unary application.</summary>
    private sealed class UnaryNode : Node
    {
        public UnaryNode(long position, UnaryOperator op, Node operand)
            : base(position)
        {
            Operator = op;
            Operand = operand;
        }

        public UnaryOperator Operator { get; }

        public Node Operand { get; }
    }

    /// <summary>A binary application.</summary>
    private sealed class BinaryNode : Node
    {
        public BinaryNode(long position, BinaryOperator op, Node left, Node right)
            : base(position)
        {
            Operator = op;
            Left = left;
            Right = right;
        }

        public BinaryOperator Operator { get; }

        public Node Left { get; }

        public Node Right { get; }
    }

    // ==============================================================================================
    //  PARSER
    // ==============================================================================================

    /// <summary>
    /// A recursive-descent parser over the token list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PRECEDENCE IS POWERBUILDER'S, LOWEST BINDING FIRST: <c>or</c>, <c>and</c>, <c>not</c>, the
    /// relational operators, <c>+</c> and <c>-</c>, <c>*</c> and <c>/</c>, unary <c>-</c> and <c>+</c>,
    /// then <c>^</c>. Two consequences are worth stating because they differ from C#. <c>not</c> binds
    /// LOOSER than a comparison, so <c>not a = b</c> is <c>not (a = b)</c>. And <c>^</c> binds TIGHTER
    /// than unary minus, so <c>-2^2</c> is <c>-4</c>.
    /// </para>
    /// <para>
    /// THE GRAMMAR IS COMPLETE WHILE THE ROSTER IS NOT (DECISION D2). A column expression is supplied by
    /// the application through <c>of_SetExp</c> and may use any operator PowerBuilder defines, so
    /// rejecting one would reject valid input; a FUNCTION, by contrast, is only registered when a legacy
    /// call site justifies it.
    /// </para>
    /// <para>
    /// NO EXCEPTIONS. Every production answers <see langword="null"/> and leaves the reason on
    /// <see cref="Error"/>, for the reason given on <see cref="TryTokenize"/>.
    /// </para>
    /// </remarks>
    private sealed class ExpressionParser
    {
        private const string KeywordOr = "or";
        private const string KeywordAnd = "and";
        private const string KeywordNot = "not";
        private const string KeywordLike = "like";
        private const string KeywordFor = "for";
        private const string KeywordAll = "all";
        private const string KeywordPage = "page";
        private const string KeywordGroup = "group";

        private readonly List<Token> _tokens;
        private int _index;

        /// <summary>
        /// Creates a parser over <paramref name="tokens"/>, which must end with an
        /// <see cref="TokenKind.End"/> token.
        /// </summary>
        /// <param name="tokens">The tokens.</param>
        internal ExpressionParser(List<Token> tokens) => _tokens = tokens;

        /// <summary>The diagnostic, set when a production failed.</summary>
        internal string? Error { get; private set; }

        /// <summary>The one-based position the diagnostic points at.</summary>
        internal long ErrorPosition { get; private set; }

        private Token Current => _tokens[_index];

        /// <summary>
        /// Parses the whole token list and requires that nothing is left over.
        /// </summary>
        /// <returns>The root node, or <see langword="null"/> on failure.</returns>
        internal Node? ParseComplete()
        {
            Node? node = ParseOr();
            if (node is null)
            {
                return null;
            }

            if (Current.Kind != TokenKind.End)
            {
                Fail(
                    Formatting.Sprintf(
                        "Unexpected '{1}' after the end of the expression.",
                        Current.Text),
                    Current.Position);
                return null;
            }

            return node;
        }

        private void Fail(string message, long position)
        {
            // FIRST FAILURE WINS. A later production may also fail while unwinding, and the first
            // diagnostic is the one that points at the actual defect.
            Error ??= message;
            if (ErrorPosition == 0L)
            {
                ErrorPosition = position;
            }
        }

        private static bool IsKeyword(in Token token, string keyword) =>
            token.Kind == TokenKind.Identifier
                && string.Equals(token.Text, keyword, StringComparison.OrdinalIgnoreCase);

        private static bool IsOperator(in Token token, string text) =>
            token.Kind == TokenKind.Operator && string.Equals(token.Text, text, StringComparison.Ordinal);

        private Node? ParseOr()
        {
            Node? left = ParseAnd();

            while (left is not null && IsKeyword(Current, KeywordOr))
            {
                long position = Current.Position;
                _index++;

                Node? right = ParseAnd();
                if (right is null)
                {
                    return null;
                }

                left = new BinaryNode(position, BinaryOperator.Or, left, right);
            }

            return left;
        }

        private Node? ParseAnd()
        {
            Node? left = ParseNot();

            while (left is not null && IsKeyword(Current, KeywordAnd))
            {
                long position = Current.Position;
                _index++;

                Node? right = ParseNot();
                if (right is null)
                {
                    return null;
                }

                left = new BinaryNode(position, BinaryOperator.And, left, right);
            }

            return left;
        }

        private Node? ParseNot()
        {
            if (!IsKeyword(Current, KeywordNot))
            {
                return ParseRelational();
            }

            long position = Current.Position;
            _index++;

            Node? operand = ParseNot();

            return operand is null ? null : new UnaryNode(position, UnaryOperator.Not, operand);
        }

        private Node? ParseRelational()
        {
            Node? left = ParseAdditive();

            while (left is not null && TryReadRelationalOperator(out BinaryOperator op, out long position))
            {
                Node? right = ParseAdditive();
                if (right is null)
                {
                    return null;
                }

                left = new BinaryNode(position, op, left, right);
            }

            return left;
        }

        private bool TryReadRelationalOperator(out BinaryOperator op, out long position)
        {
            position = Current.Position;

            if (Current.Kind == TokenKind.Operator)
            {
                switch (Current.Text)
                {
                    case "=":
                        op = BinaryOperator.Equal;
                        _index++;
                        return true;
                    case "<>":
                        op = BinaryOperator.NotEqual;
                        _index++;
                        return true;
                    case "<":
                        op = BinaryOperator.Less;
                        _index++;
                        return true;
                    case ">":
                        op = BinaryOperator.Greater;
                        _index++;
                        return true;
                    case "<=":
                        op = BinaryOperator.LessOrEqual;
                        _index++;
                        return true;
                    case ">=":
                        op = BinaryOperator.GreaterOrEqual;
                        _index++;
                        return true;
                    default:
                        break;
                }
            }
            else if (IsKeyword(Current, KeywordLike))
            {
                // `LIKE` is a relational operator and sits at the same level as `=`. Justified by
                // n_cst_dwsvc_dropdownsearch.sru:L319 and :L331, which splice
                // `(Lower(<col>) LIKE '%<data>%')` into the filter that also carries
                // PinyinFirstLetterLike. `NOT LIKE` is deliberately absent - no legacy site writes it,
                // and `not (a like b)` expresses it through the unary operator (constraint C-B).
                op = BinaryOperator.Like;
                _index++;
                return true;
            }

            op = BinaryOperator.Equal;
            return false;
        }

        private Node? ParseAdditive()
        {
            Node? left = ParseMultiplicative();

            while (left is not null && (IsOperator(Current, "+") || IsOperator(Current, "-")))
            {
                BinaryOperator op = IsOperator(Current, "+")
                    ? BinaryOperator.Add
                    : BinaryOperator.Subtract;
                long position = Current.Position;
                _index++;

                Node? right = ParseMultiplicative();
                if (right is null)
                {
                    return null;
                }

                left = new BinaryNode(position, op, left, right);
            }

            return left;
        }

        private Node? ParseMultiplicative()
        {
            Node? left = ParseUnary();

            while (left is not null && (IsOperator(Current, "*") || IsOperator(Current, "/")))
            {
                BinaryOperator op = IsOperator(Current, "*")
                    ? BinaryOperator.Multiply
                    : BinaryOperator.Divide;
                long position = Current.Position;
                _index++;

                Node? right = ParseUnary();
                if (right is null)
                {
                    return null;
                }

                left = new BinaryNode(position, op, left, right);
            }

            return left;
        }

        private Node? ParseUnary()
        {
            if (IsOperator(Current, "-") || IsOperator(Current, "+"))
            {
                UnaryOperator op = IsOperator(Current, "-")
                    ? UnaryOperator.Negate
                    : UnaryOperator.Identity;
                long position = Current.Position;
                _index++;

                Node? operand = ParseUnary();

                return operand is null ? null : new UnaryNode(position, op, operand);
            }

            return ParsePower();
        }

        private Node? ParsePower()
        {
            Node? left = ParsePrimary();
            if (left is null || !IsOperator(Current, "^"))
            {
                return left;
            }

            long position = Current.Position;
            _index++;

            // RIGHT-ASSOCIATIVE, and the right operand is a UNARY so that `2^-1` parses. Right
            // associativity is the conventional reading of exponentiation; no in-scope expression chains
            // two, so the choice is unobservable and is recorded rather than hidden.
            Node? right = ParseUnary();

            return right is null ? null : new BinaryNode(position, BinaryOperator.Power, left, right);
        }

        private Node? ParsePrimary()
        {
            Token token = Current;

            switch (token.Kind)
            {
                case TokenKind.Number:
                case TokenKind.Text:
                    _index++;
                    return new LiteralNode(token.Position, token.Value);

                case TokenKind.LeftParen:
                {
                    _index++;
                    Node? inner = ParseOr();
                    if (inner is null)
                    {
                        return null;
                    }

                    if (Current.Kind != TokenKind.RightParen)
                    {
                        Fail("A parenthesised sub-expression is not closed.", Current.Position);
                        return null;
                    }

                    _index++;
                    return inner;
                }

                case TokenKind.Identifier:
                    return _tokens[_index + 1].Kind == TokenKind.LeftParen
                        ? ParseCall()
                        : ParseColumnReference();

                default:
                    Fail(
                        token.Kind == TokenKind.End
                            ? "The expression ends where a value was expected."
                            : Formatting.Sprintf(
                                "'{1}' cannot begin a value.",
                                token.Text),
                        token.Position);
                    return null;
            }
        }

        private Node? ParseColumnReference()
        {
            Token token = Current;
            _index++;

            if (Current.Kind != TokenKind.LeftBracket)
            {
                return new ColumnNode(token.Position, token.Text, offset: null);
            }

            // The relative row-offset form, `col[n]`. Justified by
            // n_cst_dwsvc_contextmenu.sru:L1186, :L1188, :L1352 and :L1354, all of which compare a
            // column against `col[1]`. That the offset is RELATIVE rather than absolute is settled by
            // the oracle itself: the compute-column twin of those very expressions, at :L1183 and
            // :L1349, writes the same comparison as `String(GetRow() + 1)` - the row AFTER the cursor.
            _index++;

            Node? offset = ParseOr();
            if (offset is null)
            {
                return null;
            }

            if (Current.Kind != TokenKind.RightBracket)
            {
                Fail("A row offset is not closed.", Current.Position);
                return null;
            }

            _index++;
            return new ColumnNode(token.Position, token.Text, offset);
        }

        private Node? ParseCall()
        {
            Token name = Current;

            // The identifier and its opening parenthesis, both already established by ParsePrimary.
            _index += 2;

            List<Node> arguments = [];
            ExpressionAggregateScope scope = ExpressionAggregateScope.All;
            long groupLevel = 0L;

            if (Current.Kind != TokenKind.RightParen && !IsKeyword(Current, KeywordFor))
            {
                while (true)
                {
                    Node? argument = ParseOr();
                    if (argument is null)
                    {
                        return null;
                    }

                    arguments.Add(argument);

                    if (Current.Kind != TokenKind.Comma)
                    {
                        break;
                    }

                    _index++;
                }
            }

            if (IsKeyword(Current, KeywordFor) && !TryReadAggregateScope(out scope, out groupLevel))
            {
                return null;
            }

            if (Current.Kind != TokenKind.RightParen)
            {
                Fail(
                    Formatting.Sprintf(
                        "The argument list of {1} is not closed.",
                        name.Text),
                    Current.Position);
                return null;
            }

            _index++;
            return new CallNode(name.Position, name.Text, arguments, scope, groupLevel);
        }

        private bool TryReadAggregateScope(out ExpressionAggregateScope scope, out long groupLevel)
        {
            scope = ExpressionAggregateScope.All;
            groupLevel = 0L;

            // The `for` keyword itself.
            _index++;

            if (IsKeyword(Current, KeywordAll))
            {
                _index++;
                return true;
            }

            if (IsKeyword(Current, KeywordPage))
            {
                scope = ExpressionAggregateScope.Page;
                _index++;
                return true;
            }

            if (IsKeyword(Current, KeywordGroup))
            {
                scope = ExpressionAggregateScope.Group;
                _index++;

                // The level is optional in the grammar; the evaluator narrows the whole scope in any
                // case (DECISION D7b), so the level is captured for the diagnostic rather than used.
                if (Current.Kind == TokenKind.Number && Current.Value.TryGetLong(out long level))
                {
                    groupLevel = level;
                    _index++;
                }

                return true;
            }

            Fail(
                Formatting.Sprintf(
                    "'{1}' is not an aggregate scope. Expected all, page or group.",
                    Current.Text),
                Current.Position);
            return false;
        }
    }


    // ==============================================================================================
    //  EVALUATION
    // ==============================================================================================

    /// <summary>
    /// Tokenizes, parses and evaluates an already-normalised, non-empty expression.
    /// </summary>
    /// <param name="expression">The expression.</param>
    /// <param name="row">The one-based row, or <see cref="NoRowContext"/>.</param>
    /// <returns>The result.</returns>
    private DataWindowExpressionResult EvaluateNormalised(string expression, long row)
    {
        if (!TryTokenize(
            expression,
            _boundValues,
            out List<Token> tokens,
            out string? lexicalError,
            out long lexicalPosition))
        {
            return DataWindowExpressionResult.Invalid(
                CreateEvaluationError(
                    expression,
                    lexicalPosition,
                    lexicalError ?? "The expression could not be tokenized."),
                expression,
                row);
        }

        ExpressionParser parser = new(tokens);
        Node? root = parser.ParseComplete();

        if (root is null)
        {
            return DataWindowExpressionResult.Invalid(
                CreateEvaluationError(
                    expression,
                    parser.ErrorPosition,
                    parser.Error ?? "The expression could not be parsed."),
                expression,
                row);
        }

        return EvaluateNode(root, expression, row);
    }

    /// <summary>
    /// Evaluates one node.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="expression">The whole expression, for a diagnostic.</param>
    /// <param name="row">The one-based row in force, or <see cref="NoRowContext"/>.</param>
    /// <returns>The result.</returns>
    private DataWindowExpressionResult EvaluateNode(Node node, string expression, long row) =>
        node switch
        {
            LiteralNode literal => DataWindowExpressionResult.Success(literal.Value, expression, row),
            ColumnNode column => EvaluateColumn(column, expression, row),
            CallNode call => EvaluateCall(call, expression, row),
            UnaryNode unary => EvaluateUnary(unary, expression, row),
            BinaryNode binary => EvaluateBinary(binary, expression, row),

            // Unreachable: the parser produces exactly the five node kinds above. Stated rather than
            // omitted so that adding a sixth cannot fail silently.
            _ => DataWindowExpressionResult.Invalid(
                CreateEvaluationError(
                    expression,
                    node.Position,
                    "Internal error: an unrecognised expression node was produced."),
                expression,
                row),
        };

    /// <summary>
    /// Evaluates a unary application.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="expression">The whole expression.</param>
    /// <param name="row">The row in force.</param>
    /// <returns>The result.</returns>
    /// <remarks>
    /// NULL PROPAGATES THROUGH ALL THREE OPERATORS (DECISION D8): the negation of an unknown value is
    /// unknown, and so is its truth. Answering <c>0</c> or <c>false</c> instead would be exactly the
    /// null-to-zero collapse AAP 0.4.5.4 forbids.
    /// </remarks>
    private DataWindowExpressionResult EvaluateUnary(UnaryNode node, string expression, long row)
    {
        DataWindowExpressionResult operand = EvaluateNode(node.Operand, expression, row);
        if (!operand.IsSuccess)
        {
            return operand;
        }

        ExpressionValue value = operand.Value;

        if (value.IsNull)
        {
            return DataWindowExpressionResult.Success(ExpressionValue.Null, expression, row);
        }

        switch (node.Operator)
        {
            case UnaryOperator.Identity:
                // Unary plus is a no-op on a number and a type error on anything else, which is what
                // asking for the numeric coercion establishes.
                return value.TryGetDecimal(out _) || value.Kind == ExpressionValueKind.Double
                    ? DataWindowExpressionResult.Success(value, expression, row)
                    : Malformed(expression, node.Position, row, "Unary + requires a number.");

            case UnaryOperator.Negate:
                return NegateValue(value, expression, node.Position, row);

            default:
                if (!value.TryGetBoolean(out bool flag))
                {
                    return Malformed(
                        expression,
                        node.Position,
                        row,
                        "NOT requires a condition, and this operand has no truth value.");
                }

                return DataWindowExpressionResult.Success(
                    ExpressionValue.FromBoolean(!flag),
                    expression,
                    row);
        }
    }

    /// <summary>
    /// Negates a non-null value, preserving its arm.
    /// </summary>
    /// <param name="value">The operand.</param>
    /// <param name="expression">The whole expression.</param>
    /// <param name="position">The operator's position.</param>
    /// <param name="row">The row in force.</param>
    /// <returns>The result.</returns>
    private static DataWindowExpressionResult NegateValue(
        in ExpressionValue value,
        string expression,
        long position,
        long row)
    {
        switch (value.Kind)
        {
            case ExpressionValueKind.Long:
            {
                _ = value.TryGetLong(out long number);

                // long.MinValue has no positive counterpart, so negating it in the Long arm would
                // overflow. Widening to decimal keeps the value exact rather than wrapping it.
                return number == long.MinValue
                    ? DataWindowExpressionResult.Success(
                        ExpressionValue.FromDecimal(-(decimal)number),
                        expression,
                        row)
                    : DataWindowExpressionResult.Success(
                        ExpressionValue.FromLong(-number),
                        expression,
                        row);
            }

            case ExpressionValueKind.Decimal:
            {
                _ = value.TryGetDecimal(out decimal number);
                return DataWindowExpressionResult.Success(
                    ExpressionValue.FromDecimal(-number),
                    expression,
                    row);
            }

            case ExpressionValueKind.Double:
            {
                _ = value.TryGetDouble(out double number);
                return DataWindowExpressionResult.Success(
                    ExpressionValue.FromDouble(-number),
                    expression,
                    row);
            }

            case ExpressionValueKind.Boolean:
            {
                // A boolean's numeric projection is 1 or 0, so its negation is -1 or 0. Reached
                // through the same coercion that makes `if(IsNull(x),1,0)` comparable
                // [n_cst_dwsvc_contextmenu.sru:L1188].
                _ = value.TryGetLong(out long number);
                return DataWindowExpressionResult.Success(
                    ExpressionValue.FromLong(-number),
                    expression,
                    row);
            }

            default:
                return Malformed(expression, position, row, "Unary - requires a number.");
        }
    }

    /// <summary>
    /// Evaluates a binary application, short-circuiting the two boolean operators.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="expression">The whole expression.</param>
    /// <param name="row">The row in force.</param>
    /// <returns>The result.</returns>
    /// <remarks>
    /// SHORT-CIRCUITING IS OBSERVABLE AND IS THEREFORE REPRODUCED. The drop-down filter at
    /// <c>n_cst_dwsvc_dropdownsearch.sru:L319-L323</c> is a chain of <c>OR</c> terms one of which is
    /// <c>PinyinFirstLetterLike</c> - a function that reports the deferred-table gap as a structured error
    /// when it is unavailable (AAP 0.6.5). With short-circuiting, a row already matched by the display-name
    /// term never reaches the pinyin term and never raises that gap; evaluating both sides
    /// unconditionally would surface a blocked-capability error for rows that match perfectly well.
    /// </remarks>
    private DataWindowExpressionResult EvaluateBinary(BinaryNode node, string expression, long row)
    {
        if (node.Operator is BinaryOperator.And or BinaryOperator.Or)
        {
            return EvaluateBooleanOperator(node, expression, row);
        }

        DataWindowExpressionResult left = EvaluateNode(node.Left, expression, row);
        if (!left.IsSuccess)
        {
            return left;
        }

        DataWindowExpressionResult right = EvaluateNode(node.Right, expression, row);
        if (!right.IsSuccess)
        {
            return right;
        }

        return node.Operator switch
        {
            BinaryOperator.Add
                or BinaryOperator.Subtract
                or BinaryOperator.Multiply
                or BinaryOperator.Divide
                or BinaryOperator.Power =>
                EvaluateArithmetic(node, left.Value, right.Value, expression, row),

            BinaryOperator.Like => EvaluateLike(node, left.Value, right.Value, expression, row),

            _ => EvaluateRelational(node, left.Value, right.Value, expression, row),
        };
    }

    /// <summary>
    /// Evaluates <c>and</c> or <c>or</c>, with PowerBuilder's null semantics.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="expression">The whole expression.</param>
    /// <param name="row">The row in force.</param>
    /// <returns>The result.</returns>
    /// <remarks>
    /// A NULL OPERAND YIELDS NULL UNLESS THE OTHER ONE ALREADY DECIDES THE ANSWER. <c>false and null</c>
    /// is false and <c>true or null</c> is true, because in both the second operand cannot change the
    /// outcome; every other combination involving a null is null. This is three-valued logic, and it is
    /// the reading that keeps <c>and</c> and <c>or</c> consistent with the null propagation the rest of
    /// the grammar has (DECISION D8).
    /// </remarks>
    private DataWindowExpressionResult EvaluateBooleanOperator(
        BinaryNode node,
        string expression,
        long row)
    {
        bool isAnd = node.Operator == BinaryOperator.And;

        DataWindowExpressionResult left = EvaluateNode(node.Left, expression, row);
        if (!left.IsSuccess)
        {
            return left;
        }

        bool leftIsNull = left.Value.IsNull;
        bool leftFlag = false;

        if (!leftIsNull && !left.Value.TryGetBoolean(out leftFlag))
        {
            return Malformed(
                expression,
                node.Position,
                row,
                Formatting.Sprintf(
                    "{1} requires conditions, and the left operand has no truth value.",
                    isAnd ? "AND" : "OR"));
        }

        // The short circuit, which is also where the decided-by-one-operand null rule lives.
        if (!leftIsNull && leftFlag != isAnd)
        {
            return DataWindowExpressionResult.Success(
                ExpressionValue.FromBoolean(leftFlag),
                expression,
                row);
        }

        DataWindowExpressionResult right = EvaluateNode(node.Right, expression, row);
        if (!right.IsSuccess)
        {
            return right;
        }

        if (right.Value.IsNull)
        {
            return DataWindowExpressionResult.Success(ExpressionValue.Null, expression, row);
        }

        if (!right.Value.TryGetBoolean(out bool rightFlag))
        {
            return Malformed(
                expression,
                node.Position,
                row,
                Formatting.Sprintf(
                    "{1} requires conditions, and the right operand has no truth value.",
                    isAnd ? "AND" : "OR"));
        }

        // The left operand was null and the right does not decide the answer on its own.
        if (leftIsNull && rightFlag == isAnd)
        {
            return DataWindowExpressionResult.Success(ExpressionValue.Null, expression, row);
        }

        return DataWindowExpressionResult.Success(
            ExpressionValue.FromBoolean(rightFlag),
            expression,
            row);
    }

    /// <summary>
    /// Evaluates the five arithmetic operators, including <c>+</c> as string concatenation.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <param name="expression">The whole expression.</param>
    /// <param name="row">The row in force.</param>
    /// <returns>The result.</returns>
    /// <remarks>
    /// <para>
    /// <c>+</c> CONCATENATES WHEN EITHER OPERAND IS TEXT. PowerBuilder spells concatenation with <c>+</c>
    /// and the oracle relies on it throughout - every diagnostic it builds is a <c>+</c> chain. A text
    /// operand under any other arithmetic operator is a type error rather than a silent parse.
    /// </para>
    /// <para>
    /// PROMOTION IS Long -> Double -> Decimal (DECISION D3), and division always leaves the integral arm.
    /// PowerBuilder's <c>/</c> is true division - <c>5/2</c> is <c>2.5</c>, not <c>2</c> - so two integers
    /// divide into a decimal, which is where an exact quotient can be represented. Division by zero
    /// answers the malformed sentinel rather than raising, because the oracle answers <c>"!"</c> for an
    /// expression it cannot evaluate and never raises out of <c>Describe</c>.
    /// </para>
    /// </remarks>
    private static DataWindowExpressionResult EvaluateArithmetic(
        BinaryNode node,
        in ExpressionValue left,
        in ExpressionValue right,
        string expression,
        long row)
    {
        if (left.IsNull || right.IsNull)
        {
            return DataWindowExpressionResult.Success(ExpressionValue.Null, expression, row);
        }

        if (node.Operator == BinaryOperator.Add
            && (left.Kind == ExpressionValueKind.String || right.Kind == ExpressionValueKind.String))
        {
            if (!left.TryGetText(out string leftText) || !right.TryGetText(out string rightText))
            {
                return Malformed(
                    expression,
                    node.Position,
                    row,
                    "Concatenation requires text on both sides.");
            }

            return DataWindowExpressionResult.Success(
                ExpressionValue.FromString(leftText + rightText),
                expression,
                row);
        }

        if (left.Kind == ExpressionValueKind.String || right.Kind == ExpressionValueKind.String)
        {
            // Text under -, *, / or ^. PowerBuilder does not coerce text into a number for arithmetic;
            // an expression has to say Long(...) or Double(...) to mean that.
            return Malformed(
                expression,
                node.Position,
                row,
                Formatting.Sprintf(
                    "Arithmetic requires numbers, and an operand of {1} is text.",
                    OperatorText(node.Operator)));
        }

        if (left.IsTemporal || right.IsTemporal)
        {
            return Malformed(
                expression,
                node.Position,
                row,
                Formatting.Sprintf(
                    "Arithmetic requires numbers, and an operand of {1} is a date or a time.",
                    OperatorText(node.Operator)));
        }

        if (node.Operator == BinaryOperator.Power)
        {
            // The exponent is the one operator whose result is always binary floating point, matching
            // PowerScript's own `^`.
            if (!left.TryGetDouble(out double baseValue) || !right.TryGetDouble(out double exponent))
            {
                return Malformed(expression, node.Position, row, "The exponent requires numbers.");
            }

            return DataWindowExpressionResult.Success(
                ExpressionValue.FromDouble(Math.Pow(baseValue, exponent)),
                expression,
                row);
        }

        bool preferDecimal = left.Kind == ExpressionValueKind.Decimal
            || right.Kind == ExpressionValueKind.Decimal
            || node.Operator == BinaryOperator.Divide;
        bool anyDouble = left.Kind == ExpressionValueKind.Double
            || right.Kind == ExpressionValueKind.Double;

        if (anyDouble && !preferDecimal)
        {
            if (!left.TryGetDouble(out double leftDouble) || !right.TryGetDouble(out double rightDouble))
            {
                return Malformed(expression, node.Position, row, "Arithmetic requires numbers.");
            }

            double computed = node.Operator switch
            {
                BinaryOperator.Add => leftDouble + rightDouble,
                BinaryOperator.Subtract => leftDouble - rightDouble,
                _ => leftDouble * rightDouble,
            };

            return DataWindowExpressionResult.Success(
                ExpressionValue.FromDouble(computed),
                expression,
                row);
        }

        if (!left.TryGetDecimal(out decimal leftNumber) || !right.TryGetDecimal(out decimal rightNumber))
        {
            return Malformed(
                expression,
                node.Position,
                row,
                "Arithmetic requires numbers a decimal can hold.");
        }

        if (node.Operator == BinaryOperator.Divide && rightNumber == 0m)
        {
            return Malformed(expression, node.Position, row, "Division by zero.");
        }

        try
        {
            decimal computed = node.Operator switch
            {
                BinaryOperator.Add => leftNumber + rightNumber,
                BinaryOperator.Subtract => leftNumber - rightNumber,
                BinaryOperator.Multiply => leftNumber * rightNumber,
                _ => leftNumber / rightNumber,
            };

            // The integral arm is preserved when both operands were integral and the operator is not
            // division, so `Max(Len(...)) + 1` stays a Long and renders without a decimal point.
            bool integral = !preferDecimal
                && !anyDouble
                && left.Kind != ExpressionValueKind.Double
                && right.Kind != ExpressionValueKind.Double
                && computed == decimal.Truncate(computed)
                && computed <= long.MaxValue
                && computed >= long.MinValue;

            return DataWindowExpressionResult.Success(
                integral
                    ? ExpressionValue.FromLong((long)computed)
                    : ExpressionValue.FromDecimal(computed),
                expression,
                row);
        }
        catch (OverflowException)
        {
            // A decimal result past the type's range. Reported as a malformed expression rather than
            // propagated, for the reason given in the remarks.
            return Malformed(
                expression,
                node.Position,
                row,
                "The arithmetic result is too large to represent.");
        }
    }

    /// <summary>
    /// Evaluates a relational operator.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <param name="expression">The whole expression.</param>
    /// <param name="row">The row in force.</param>
    /// <returns>The result.</returns>
    /// <remarks>
    /// A COMPARISON INVOLVING A NULL IS NULL, NOT FALSE, and the oracle proves it needs to be: the
    /// context menu wraps every operand in <c>if(IsNull(x),'',x)</c> at
    /// <c>n_cst_dwsvc_contextmenu.sru:L1186</c> and <c>:L1352</c>, and replaces the null with a numeric
    /// flag at <c>:L1188</c> and <c>:L1354</c>. Those wrappers exist precisely BECAUSE a bare comparison
    /// against a null does not answer true or false; implementing it as false here would make all four
    /// lines pointless and would silently change which row <c>Find</c> lands on.
    /// </remarks>
    private static DataWindowExpressionResult EvaluateRelational(
        BinaryNode node,
        in ExpressionValue left,
        in ExpressionValue right,
        string expression,
        long row)
    {
        ExpressionComparison comparison = CompareValues(left, right);

        switch (comparison)
        {
            case ExpressionComparison.Null:
                return DataWindowExpressionResult.Success(ExpressionValue.Null, expression, row);

            case ExpressionComparison.Incomparable:
                return Malformed(
                    expression,
                    node.Position,
                    row,
                    Formatting.Sprintf(
                        "A {1} value cannot be compared with a {2} value.",
                        left.Kind.ToString(),
                        right.Kind.ToString()));

            default:
                break;
        }

        bool answer = node.Operator switch
        {
            BinaryOperator.Equal => comparison == ExpressionComparison.Equal,
            BinaryOperator.NotEqual => comparison != ExpressionComparison.Equal,
            BinaryOperator.Less => comparison == ExpressionComparison.Less,
            BinaryOperator.Greater => comparison == ExpressionComparison.Greater,
            BinaryOperator.LessOrEqual => comparison != ExpressionComparison.Greater,
            _ => comparison != ExpressionComparison.Less,
        };

        return DataWindowExpressionResult.Success(
            ExpressionValue.FromBoolean(answer),
            expression,
            row);
    }


    /// <summary>
    /// Evaluates <c>LIKE</c>, PowerBuilder's wildcard match.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="left">The subject.</param>
    /// <param name="right">The pattern.</param>
    /// <param name="expression">The whole expression.</param>
    /// <param name="row">The row in force.</param>
    /// <returns>The result.</returns>
    /// <remarks>
    /// <para>
    /// THE MATCH IS CASE-SENSITIVE, AND THE ORACLE SETTLES IT RATHER THAN A MANUAL DOING SO.
    /// <c>n_cst_dwsvc_dropdownsearch.sru:L316-L319</c> lower-cases the search data with
    /// <c>Lower(data)</c> and then writes the filter as <c>(Lower(<i>col</i>) LIKE '%<i>data</i>%')</c> -
    /// lower-casing BOTH sides. If <c>LIKE</c> were case-insensitive neither call would be needed, so
    /// their presence is the proof that it is not. A case-insensitive match here would make the two
    /// <c>Lower</c> calls dead code and would change which rows the drop-down filter keeps.
    /// </para>
    /// <para>
    /// TWO WILDCARDS AND NO MORE: <c>%</c> for any run of characters and <c>_</c> for exactly one. No
    /// escape clause and no character class, because the oracle uses neither and adding either would
    /// accept patterns it rejects (constraint C-B).
    /// </para>
    /// </remarks>
    private static DataWindowExpressionResult EvaluateLike(
        BinaryNode node,
        in ExpressionValue left,
        in ExpressionValue right,
        string expression,
        long row)
    {
        if (left.IsNull || right.IsNull)
        {
            return DataWindowExpressionResult.Success(ExpressionValue.Null, expression, row);
        }

        if (!left.TryGetText(out string subject) || !right.TryGetText(out string pattern))
        {
            return Malformed(expression, node.Position, row, "LIKE requires text on both sides.");
        }

        return DataWindowExpressionResult.Success(
            ExpressionValue.FromBoolean(MatchesLikePattern(subject, pattern)),
            expression,
            row);
    }

    /// <summary>
    /// Matches <paramref name="subject"/> against a <c>LIKE</c> pattern.
    /// </summary>
    /// <param name="subject">The text.</param>
    /// <param name="pattern">The pattern, where <c>%</c> matches any run and <c>_</c> exactly one.</param>
    /// <returns><see langword="true"/> on a match.</returns>
    /// <remarks>
    /// Iterative with one backtrack point, which is the standard linear-space wildcard match. It is
    /// written out rather than compiled to a regular expression for two reasons: a pattern arrives from
    /// user-typed search text spliced into a filter string
    /// (<c>n_cst_dwsvc_dropdownsearch.sru:L317</c>), so regular-expression metacharacters in it would have
    /// to be escaped and any mistake there would change the match; and this loop has no pathological
    /// backtracking, which a translated pattern could.
    /// </remarks>
    private static bool MatchesLikePattern(string subject, string pattern)
    {
        int subjectIndex = 0;
        int patternIndex = 0;
        int starSubject = -1;
        int starPattern = -1;

        while (subjectIndex < subject.Length)
        {
            if (patternIndex < pattern.Length
                && (pattern[patternIndex] == '_' || pattern[patternIndex] == subject[subjectIndex]))
            {
                subjectIndex++;
                patternIndex++;
                continue;
            }

            if (patternIndex < pattern.Length && pattern[patternIndex] == '%')
            {
                starPattern = patternIndex;
                starSubject = subjectIndex;
                patternIndex++;
                continue;
            }

            if (starPattern >= 0)
            {
                // Give the last `%` one more character and resume from just after it.
                patternIndex = starPattern + 1;
                starSubject++;
                subjectIndex = starSubject;
                continue;
            }

            return false;
        }

        // A trailing run of `%` matches the empty remainder.
        while (patternIndex < pattern.Length && pattern[patternIndex] == '%')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }

    /// <summary>
    /// Compares two values under PowerBuilder's expression rules.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The comparison, which may be <see cref="ExpressionComparison.Null"/> or
    /// <see cref="ExpressionComparison.Incomparable"/>.</returns>
    /// <remarks>
    /// <para>
    /// TEXT COMPARES ORDINALLY AND CASE-SENSITIVELY, for the reason given on <see cref="EvaluateLike"/>:
    /// the drop-down filter lower-cases both sides itself, which it would not do if comparison folded case.
    /// Ordinal rather than culture-aware because a DataWindow value is data being compared for identity,
    /// not prose being sorted for a reader, and because a culture-aware comparison would make the same
    /// expression answer differently on two hosts.
    /// </para>
    /// <para>
    /// A NUMBER AND A TEXT VALUE ARE INCOMPARABLE, and the oracle keeps its own types aligned rather than
    /// relying on a coercion: <c>n_cst_dwsvc_contextmenu.sru:L1185-L1189</c> chooses between a text
    /// comparison and a numeric one by first asking <c>_of_GetColumnType(colName) = COL_TYPE_STRING</c>.
    /// Silently coercing here would make that branch unnecessary and would let <c>'10' &lt; '9'</c> answer
    /// the numeric way in one expression and the textual way in another.
    /// </para>
    /// <para>
    /// A TIME IS INCOMPARABLE WITH A DATE OR A DATETIME. A date widens to midnight and a datetime narrows
    /// to nothing, so any answer would be an artefact of the widening rather than a comparison the author
    /// asked for. A date and a datetime DO compare, by widening the date - that pair has a meaning.
    /// </para>
    /// </remarks>
    public static ExpressionComparison CompareValues(in ExpressionValue left, in ExpressionValue right)
    {
        if (left.IsNull || right.IsNull)
        {
            return ExpressionComparison.Null;
        }

        if (left.Kind == ExpressionValueKind.String || right.Kind == ExpressionValueKind.String)
        {
            if (left.Kind != right.Kind)
            {
                return ExpressionComparison.Incomparable;
            }

            _ = left.TryGetText(out string leftText);
            _ = right.TryGetText(out string rightText);

            return FromSign(string.CompareOrdinal(leftText, rightText));
        }

        if (left.IsTemporal || right.IsTemporal)
        {
            if (!left.IsTemporal || !right.IsTemporal)
            {
                return ExpressionComparison.Incomparable;
            }

            bool sameKind = left.Kind == right.Kind;
            bool dateAndMoment =
                (left.Kind == ExpressionValueKind.Date && right.Kind == ExpressionValueKind.DateTime)
                    || (left.Kind == ExpressionValueKind.DateTime
                        && right.Kind == ExpressionValueKind.Date);

            if (!sameKind && !dateAndMoment)
            {
                return ExpressionComparison.Incomparable;
            }

            _ = left.TryGetMoment(out DateTime leftMoment);
            _ = right.TryGetMoment(out DateTime rightMoment);

            return FromSign(leftMoment.CompareTo(rightMoment));
        }

        // Both operands are numeric. Double takes part only when one of them already is, so an exact
        // decimal comparison is never routed through binary floating point and never loses a digit.
        if (left.Kind == ExpressionValueKind.Double || right.Kind == ExpressionValueKind.Double)
        {
            if (!left.TryGetDouble(out double leftDouble) || !right.TryGetDouble(out double rightDouble))
            {
                return ExpressionComparison.Incomparable;
            }

            // A NaN operand is unordered against everything, itself included, which is neither equal nor
            // ordered - the same shape as a null comparison, and reported the same way.
            if (double.IsNaN(leftDouble) || double.IsNaN(rightDouble))
            {
                return ExpressionComparison.Null;
            }

            return FromSign(leftDouble.CompareTo(rightDouble));
        }

        if (!left.TryGetDecimal(out decimal leftNumber) || !right.TryGetDecimal(out decimal rightNumber))
        {
            return ExpressionComparison.Incomparable;
        }

        return FromSign(leftNumber.CompareTo(rightNumber));
    }

    /// <summary>
    /// Projects a comparison sign onto <see cref="ExpressionComparison"/>.
    /// </summary>
    /// <param name="sign">A negative, zero or positive comparison result.</param>
    /// <returns>The corresponding member.</returns>
    private static ExpressionComparison FromSign(int sign) =>
        sign switch
        {
            < 0 => ExpressionComparison.Less,
            0 => ExpressionComparison.Equal,
            _ => ExpressionComparison.Greater,
        };

    /// <summary>
    /// The symbol for an operator, for a diagnostic.
    /// </summary>
    /// <param name="op">The operator.</param>
    /// <returns>The symbol.</returns>
    private static string OperatorText(BinaryOperator op) =>
        op switch
        {
            BinaryOperator.Or => "OR",
            BinaryOperator.And => "AND",
            BinaryOperator.Equal => "=",
            BinaryOperator.NotEqual => "<>",
            BinaryOperator.Less => "<",
            BinaryOperator.Greater => ">",
            BinaryOperator.LessOrEqual => "<=",
            BinaryOperator.GreaterOrEqual => ">=",
            BinaryOperator.Like => "LIKE",
            BinaryOperator.Add => "+",
            BinaryOperator.Subtract => "-",
            BinaryOperator.Multiply => "*",
            BinaryOperator.Divide => "/",
            _ => "^",
        };

    /// <summary>
    /// Builds the malformed-expression result with a caret at <paramref name="position"/>.
    /// </summary>
    /// <param name="expression">The whole expression.</param>
    /// <param name="position">The one-based caret position.</param>
    /// <param name="row">The row in force.</param>
    /// <param name="message">The diagnostic text.</param>
    /// <returns>The result.</returns>
    private static DataWindowExpressionResult Malformed(
        string expression,
        long position,
        long row,
        string message) =>
        DataWindowExpressionResult.Invalid(
            CreateEvaluationError(expression, position, message),
            expression,
            row);

    // ==============================================================================================
    //  DATAWINDOW OBJECT ACCESS - THE ONLY PLACE THIS FILE TOUCHES THE HOST
    // ==============================================================================================

    /// <summary>
    /// Evaluates a reference to a DataWindow object, with its optional relative row offset.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="expression">The whole expression.</param>
    /// <param name="row">The row in force.</param>
    /// <returns>The result.</returns>
    /// <remarks>
    /// <para>
    /// A COMPUTE FIELD IS EVALUATED, NOT READ. <c>n_cst_dwsvc_contextmenu.sru:L1130</c> and <c>:L1296</c>
    /// pass a compute field's NAME to the evaluator, and <c>:L1194</c> and <c>:L1360</c> do it per row
    /// inside the <c>bIsCompute</c> branch. A compute field holds no buffer value, so its own expression is
    /// fetched from <c>&lt;name&gt;.Expression</c> and evaluated at the same row. The nesting ceiling
    /// (<see cref="MaximumRecursionDepth"/>) is what stops a compute field that names itself.
    /// </para>
    /// <para>
    /// A ROW OUTSIDE THE BUFFER YIELDS NULL (DECISION D4). That covers row <c>0</c> - the no-row-context
    /// evaluations at <c>n_cst_dwsvc.sru:L344</c> and <c>:L782</c> - and an offset that walks off either
    /// end, which is exactly what <c>col[1]</c> does on the last row at
    /// <c>n_cst_dwsvc_contextmenu.sru:L1186</c>. Answering a sentinel there would turn the oracle's
    /// ordinary last-row case into a failure.
    /// </para>
    /// </remarks>
    private DataWindowExpressionResult EvaluateColumn(ColumnNode node, string expression, long row)
    {
        long effectiveRow = row;

        if (node.Offset is not null)
        {
            DataWindowExpressionResult offsetResult = EvaluateNode(node.Offset, expression, row);
            if (!offsetResult.IsSuccess)
            {
                return offsetResult;
            }

            if (offsetResult.Value.IsNull)
            {
                return DataWindowExpressionResult.Success(ExpressionValue.Null, expression, row);
            }

            if (!offsetResult.Value.TryGetLong(out long offset))
            {
                return Malformed(
                    expression,
                    node.Position,
                    row,
                    "A row offset must be a whole number.");
            }

            effectiveRow = row + offset;
        }

        if (!ObjectExists(node.Name))
        {
            return Malformed(
                expression,
                node.Position,
                row,
                Formatting.Sprintf(
                    "[{1}] is neither a registered function nor an object on this DataWindow.",
                    node.Name));
        }

        if (IsComputeObject(node.Name))
        {
            string computeExpression = _host.Describe(node.Name + ".Expression");

            if (computeExpression.Length == 0
                || string.Equals(computeExpression, InvalidExpressionSentinel, StringComparison.Ordinal)
                || string.Equals(computeExpression, UndeterminedValueSentinel, StringComparison.Ordinal))
            {
                return Malformed(
                    expression,
                    node.Position,
                    row,
                    Formatting.Sprintf(
                        "The compute field [{1}] reports no expression to evaluate.",
                        node.Name));
            }

            return TryEvaluate(computeExpression, effectiveRow);
        }

        if (effectiveRow < 1L || effectiveRow > _host.RowCount())
        {
            return DataWindowExpressionResult.Success(ExpressionValue.Null, expression, row);
        }

        if (!TryReadColumnValue(node.Name, effectiveRow, out ExpressionValue value, out string? failure))
        {
            return Malformed(expression, node.Position, row, failure);
        }

        return DataWindowExpressionResult.Success(value, expression, row);
    }

    /// <summary>
    /// Reports whether the DataWindow has an object of this name.
    /// </summary>
    /// <param name="name">The object name, which may be the positional form <c>#n</c>.</param>
    /// <returns><see langword="true"/> when it exists.</returns>
    /// <remarks>
    /// <para>
    /// PROBED WITH <c>&lt;name&gt;.Name</c> RATHER THAN WITH THE ORACLE'S OWN
    /// <c>_of_HasDWObject</c>, AND THE DIFFERENCE IS FORCED. That helper, at
    /// <c>n_cst_dwsvc.sru:L483</c>, is
    /// <c>Lower(Describe(dwoname + ".Name")) = Lower(dwoname)</c> - it asks whether the answer EQUALS the
    /// name it asked about. For the positional form the answer is the column's real name, so
    /// <c>#1</c> would fail a test that <c>id</c> passes, and every positional call site
    /// (<c>:L244</c>, <c>:L268</c>, <c>:L292</c>, <c>:L348</c>, <c>:L351</c>) would report a malformed
    /// expression. This probe therefore keeps the same single <c>Describe</c> call and the same property,
    /// and tests only that the answer is not one of the three non-answers - which is the same test
    /// <c>:L598</c> and <c>:L599</c> apply to a <c>Describe</c> result they intend to use.
    /// </para>
    /// <para>
    /// EXISTENCE IS ESTABLISHED BEFORE <see cref="DataWindowServiceHost.GetObjectAttribute"/> IS CALLED,
    /// not after, because that member's contract is to RAISE for a name that does not exist - the legacy
    /// warns about it in terms at <c>n_cst_dwsvc.sru:L121</c>. Probing first means an unknown name becomes
    /// the <c>"!"</c> the oracle answers instead of an exception escaping a filter predicate.
    /// </para>
    /// </remarks>
    private bool ObjectExists(string name)
    {
        string described = _host.Describe(name + ".Name");

        return described.Length > 0
            && !string.Equals(described, InvalidExpressionSentinel, StringComparison.Ordinal)
            && !string.Equals(described, UndeterminedValueSentinel, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reports whether the named object is a compute field.
    /// </summary>
    /// <param name="name">The object name.</param>
    /// <returns><see langword="true"/> when its <c>Type</c> property reads <c>compute</c>.</returns>
    /// <remarks>
    /// The property and the literal are the oracle's:
    /// <c>bCompute = (#DataWindow.Describe(colName+".type") = "compute")</c> at
    /// <c>n_cst_dwsvc_contextmenu.sru:L526</c> and <c>:L614</c>, and the same switch at <c>:L1128</c> and
    /// <c>:L1294</c>. The comparison is ordinal-ignore-case per DECISION D1; the oracle's is
    /// case-sensitive against a value PowerBuilder always reports in lower case, so the two cannot differ
    /// on any input a DataWindow produces.
    /// </remarks>
    private bool IsComputeObject(string name) =>
        string.Equals(
            _host.Describe(name + ".Type"),
            "compute",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads one column's buffer value for a row.
    /// </summary>
    /// <param name="name">The column name, or the positional form <c>#n</c>.</param>
    /// <param name="row">The one-based row, already known to be inside the buffer.</param>
    /// <param name="value">The value, which may be <see cref="ExpressionValue.Null"/>.</param>
    /// <param name="failure">The diagnostic when the return is <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the value was read.</returns>
    /// <remarks>
    /// <para>
    /// <c>dwo.Primary[row]</c> IS THE ONLY READ PATH THE HOST CONTRACT OFFERS, and that is not an
    /// oversight in the contract: Domain/DataWindowServiceHost.cs records that every typed-getter call
    /// site in both ported sources is on a drop-down CHILD rather than on the host, so the host has
    /// <see cref="IDataWindowValueBuffer"/> and no <c>GetItem*</c> at all. The indexer returns the
    /// legacy <c>any</c>, which <see cref="ExpressionValue.FromObject"/> then types.
    /// </para>
    /// <para>
    /// THE TWO CATCHES ARE THE HOST CONTRACT'S, NOT A GENERAL SAFETY NET. Only the two exception types a
    /// conforming implementation may raise for a name it cannot resolve are caught, and each becomes the
    /// <c>"!"</c> the oracle answers. Anything else propagates, because a filter predicate that swallowed
    /// an unexpected fault would report "no match" for a broken DataWindow.
    /// </para>
    /// </remarks>
    private bool TryReadColumnValue(
        string name,
        long row,
        out ExpressionValue value,
        [NotNullWhen(false)] out string? failure)
    {
        try
        {
            IDataWindowObject dwo = _host.GetObjectAttribute(name);
            value = ExpressionValue.FromObject(dwo.Primary[row]);
            failure = null;
            return true;
        }
        catch (InvalidOperationException)
        {
            value = ExpressionValue.Null;
            failure = Formatting.Sprintf("[{1}] could not be resolved on this DataWindow.", name);
            return false;
        }
        catch (ArgumentException)
        {
            value = ExpressionValue.Null;
            failure = Formatting.Sprintf("[{1}] could not be resolved on this DataWindow.", name);
            return false;
        }
    }

    /// <summary>
    /// Dispatches a function call to its registration.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="expression">The whole expression.</param>
    /// <param name="row">The row in force.</param>
    /// <returns>The result.</returns>
    /// <remarks>
    /// AN UNREGISTERED NAME IS A MALFORMED EXPRESSION, which is what PowerBuilder answers for a function
    /// its DataWindow does not know. It is deliberately NOT forwarded anywhere: the column-expression
    /// engine decides what an unknown name means - <c>n_cst_dwsvc_columnexp.sru:L2287</c> raises
    /// <c>OnColumnExpInvokeMethod</c> for it - and it expresses that decision by REGISTERING the name
    /// here, through <see cref="RegisterFunction"/>. A fallback in this method would take that decision
    /// away from the engine and make the macro protocol unobservable.
    /// </remarks>
    private DataWindowExpressionResult EvaluateCall(CallNode node, string expression, long row)
    {
        if (!_functions.TryGetValue(node.Name, out DataWindowExpressionFunction? function))
        {
            return Malformed(
                expression,
                node.Position,
                row,
                Formatting.Sprintf("Function [{1}] is not defined.", node.Name));
        }

        ExpressionFunctionInvocation invocation = new(
            this,
            node.Name,
            node.Arguments.Count,
            row,
            node.Scope,
            node.GroupLevel,
            expression,
            node.Position,
            (index, boundRow) => EvaluateNode(node.Arguments[index], expression, boundRow),
            index => node.Arguments[index] is ColumnNode column && column.Offset is null
                ? column.Name
                : null);

        return function(invocation);
    }


    // ==============================================================================================
    //  THE FUNCTION ROSTER
    //  ---------------------------------------------------------------------------------------------
    //  EVERY ENTRY CITES A LEGACY CALL SITE. Nothing here is speculative, and nothing the legacy uses
    //  is missing. Min, Avg and Count are ABSENT for exactly that reason: PowerBuilder defines them
    //  and no in-scope expression writes one, so registering them would widen the surface past the
    //  evidence (constraint C-B, DECISION D2).
    // ==============================================================================================

    /// <summary>
    /// Registers the roster. Called once, from the constructor.
    /// </summary>
    private void RegisterBuiltInFunctions()
    {
        // Display-value resolution. n_cst_dwsvc.sru:L289; n_cst_dwsvc_contextmenu.sru:L543, :L626,
        // :L1132, :L1133, :L1298, :L1299.
        RegisterFunction("LookUpDisplay", EvaluateLookUpDisplay);

        // The pinyin filter. Reached ONLY through an expression, at
        // n_cst_dwsvc_dropdownsearch.sru:L323 - never as a script call - so this registration is the
        // only path that reproduces the legacy behaviour at all.
        RegisterFunction(PinyinFirstLetterMatcher.ExpressionFunctionName, EvaluatePinyinFirstLetterLike);

        // Character length and DBCS byte length. n_cst_dwsvc_contextmenu.sru:L1132 and :L1133, whose
        // difference counts the wide characters in a column's widest display value.
        RegisterFunction("Len", EvaluateLen);
        RegisterFunction("LenA", EvaluateLenA);

        // The two aggregates. Max at n_cst_dwsvc_contextmenu.sru:L1132, :L1133, :L1298, :L1299;
        // Sum at ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L27, `sum(salary for page)`.
        RegisterFunction("Max", invocation => EvaluateAggregate(invocation, isSum: false));
        RegisterFunction("Sum", invocation => EvaluateAggregate(invocation, isSum: true));

        // The numeric conversions. n_cst_dwsvc_contextmenu.sru:L1132-L1133 wraps this evaluator's
        // answer in Long and Abs; docs/n_cst_dwsvc_columnexp.md:L126 documents a macro implemented as
        // Round(Double(args[1]),Long(args[2])). All four are PowerBuilder expression functions in their
        // own right and the folder brief lists them in the required roster.
        RegisterFunction("Abs", EvaluateAbs);
        RegisterFunction("Long", EvaluateLong);
        RegisterFunction("Double", EvaluateDouble);
        RegisterFunction("Round", EvaluateRound);

        // Text projection, the conditional, and the two DataWindow-level readings. All four appear in
        // one expression at n_cst_dwsvc_contextmenu.sru:L1183 and :L1349.
        RegisterFunction("String", EvaluateString);
        RegisterFunction("if", EvaluateIf);
        RegisterFunction("GetRow", EvaluateGetRow);
        RegisterFunction("RowCount", EvaluateRowCount);

        // The nested property read, which may nest another evaluation.
        // n_cst_dwsvc_contextmenu.sru:L1183 and :L1349.
        RegisterFunction(DescribeFunctionName, EvaluateDescribeFunction);

        // The null test. n_cst_dwsvc_contextmenu.sru:L1186, :L1188, :L1352, :L1354.
        RegisterFunction("IsNull", EvaluateIsNull);

        // The three temporal constructors. dwvaluetoexp.srf:L54, :L60, :L66 EMIT these three forms as
        // expression text, and n_cst_dwsvc_columnexp.sru:L2276-L2280 and :L2300-L2304 splice the same
        // three into an expression when marshalling a macro's return value - so the evaluator has to be
        // able to evaluate what its own collaborators produce.
        RegisterFunction("Date", EvaluateDate);
        RegisterFunction("DateTime", EvaluateDateTime);
        RegisterFunction("Time", EvaluateTime);

        // Case folding, used by the drop-down filter on BOTH sides of its LIKE
        // (n_cst_dwsvc_dropdownsearch.sru:L319, :L331).
        RegisterFunction("Lower", EvaluateLower);

        // The value-to-expression converter, called from INSIDE an expression at
        // n_cst_dwsvc_columnexp.sru:L2390: _of_Evaluate("dwValueToExp(" + sExp + ")",row).
        RegisterFunction(ValueToExpression.FunctionName, EvaluateValueToExpression);

        // The five zero-argument typed-null producers. They close the loop the previous registration
        // opens: dwValueToExp of a null emits the literal TEXT "dwNvlNumber()" and friends
        // (dwvaluetoexp.srf:L19, :L49, :L55, :L61, :L67), which the preprocessor splices into an
        // expression that this evaluator must then be able to evaluate. Each name comes from its owning
        // validator's own constant, so a rename cannot desynchronise the two halves.
        RegisterFunction(
            NumberValidator.FunctionName,
            invocation => EvaluateNullProducer(
                invocation,
                ExpressionValue.FromLong(NumberValidator.NullNumber())));
        RegisterFunction(
            StringValidator.FunctionName,
            invocation => EvaluateNullProducer(
                invocation,
                ExpressionValue.FromString(StringValidator.NullValue())));
        RegisterFunction(
            DateValidator.FunctionName,
            invocation => EvaluateNullProducer(
                invocation,
                ExpressionValue.FromDate(DateValidator.NullValue())));
        RegisterFunction(
            DateTimeValidator.FunctionName,
            invocation => EvaluateNullProducer(
                invocation,
                ExpressionValue.FromDateTime(DateTimeValidator.NullValue)));
        RegisterFunction(
            TimeValidator.FunctionName,
            invocation => EvaluateNullProducer(
                invocation,
                ExpressionValue.FromTime(TimeValidator.NullValue())));
    }

    /// <summary>
    /// The shared body of the five <c>dwNvl*</c> producers: no arguments, one typed null.
    /// </summary>
    /// <param name="invocation">The call.</param>
    /// <param name="nullValue">
    /// The value the owning validator's producer answered, which is always
    /// <see cref="ExpressionValue.Null"/>.
    /// </param>
    /// <returns>The result.</returns>
    /// <remarks>
    /// <para>
    /// THE VALUE IS TAKEN FROM THE VALIDATOR RATHER THAN WRITTEN AS A NULL HERE, and the indirection is
    /// the point. Each of the five <c>.srf</c> files is three lines - declare a local of the type,
    /// <c>SetNull</c> it, return it - and each validator ports exactly that. Reaching through the
    /// validator makes this file's roster agree with those ports by construction, and it makes the
    /// PowerScript return types visible in the registration: <c>dwnvlnumber.srf:L6</c> returns
    /// <c>integer</c>, which is why <see cref="NumberValidator.NullNumber"/> is a
    /// <see cref="short"/> and not a <see cref="long"/>.
    /// </para>
    /// <para>
    /// ALL FIVE COLLAPSE ONTO THE ONE NULL ARM once they reach an expression, because the DataWindow
    /// expression language has no typed null - see <see cref="ExpressionValueKind"/>. That is not a loss:
    /// the type mattered in PowerScript because the value was about to be assigned to a typed column, and
    /// the caller that does the assigning still knows the column's type.
    /// </para>
    /// </remarks>
    private static DataWindowExpressionResult EvaluateNullProducer(
        ExpressionFunctionInvocation invocation,
        in ExpressionValue nullValue) =>
        invocation.ArgumentCount == 0
            ? invocation.Success(nullValue)
            : invocation.WrongArity("0");

    /// <summary>
    /// <c>LookUpDisplay(column)</c> - the display value the DataWindow would show for a column's value.
    /// </summary>
    /// <param name="invocation">The call.</param>
    /// <returns>The result, always text or null.</returns>
    /// <remarks>
    /// <para>
    /// THE RESOLUTION ORDER IS THE ORACLE'S OWN, taken from <c>_of_GetColumnValueMap</c> at
    /// <c>n_cst_dwsvc.sru:L561-L664</c> and walked in the same sequence: the drop-down DataWindow first
    /// (<c>:L589-L636</c>), then the code table (<c>:L650-L659</c>), and the raw value when neither
    /// supplies a mapping. That function BUILDS a display-to-value map; this one performs the inverse
    /// lookup, so it walks the same sources looking for the value rather than collecting every pair.
    /// </para>
    /// <para>
    /// THE CHECKBOX ARM IS DELIBERATELY ABSENT. <c>:L639-L648</c> has one, and the context menu shows why
    /// it does not belong here: at <c>:L533-L539</c> and <c>:L616-L622</c> it routes a checkbox column AWAY
    /// from <c>LookUpDisplay</c> entirely, evaluating the column directly and mapping the result to
    /// <c>"Y"</c> or <c>"N"</c> itself. A checkbox arm here would therefore be code no legacy path reaches
    /// (constraint C-B).
    /// </para>
    /// <para>
    /// THE RESULT IS ALWAYS TEXT, because PowerBuilder's <c>LookUpDisplay</c> returns a string. A null
    /// column value answers null rather than the empty string, so a caller can still tell the two apart
    /// (DECISION D9).
    /// </para>
    /// </remarks>
    private DataWindowExpressionResult EvaluateLookUpDisplay(ExpressionFunctionInvocation invocation)
    {
        if (invocation.ArgumentCount != 1)
        {
            return invocation.WrongArity("1");
        }

        if (!invocation.TryGetArgumentColumnName(0, out string column))
        {
            return invocation.Invalid(
                "LookUpDisplay takes a column, not a computed value. Write LookUpDisplay(colname).");
        }

        if (!ObjectExists(column))
        {
            return invocation.Invalid(
                Formatting.Sprintf("[{1}] is not a column on this DataWindow.", column));
        }

        // No row, or a row past the buffer: there is no value to look up, so there is no display value.
        if (invocation.Row < 1L || invocation.Row > _host.RowCount())
        {
            return invocation.Success(ExpressionValue.Null);
        }

        if (!TryReadColumnValue(column, invocation.Row, out ExpressionValue value, out string? failure))
        {
            return invocation.Invalid(failure);
        }

        if (value.IsNull)
        {
            return invocation.Success(ExpressionValue.Null);
        }

        // Non-null, so the text projection exists.
        _ = value.TryGetText(out string target);

        if (TryResolveDropDownDisplay(column, target, out string? display)
            || TryResolveCodeTableDisplay(column, target, out display))
        {
            return invocation.Success(ExpressionValue.FromString(display));
        }

        // Neither source maps this value, so the display value IS the value - which is what a plain edit
        // column shows.
        return invocation.Success(ExpressionValue.FromString(target));
    }

    /// <summary>
    /// Looks a data value up in a column's drop-down DataWindow.
    /// </summary>
    /// <param name="column">The column name.</param>
    /// <param name="target">The data value's text.</param>
    /// <param name="display">The display value on a match.</param>
    /// <returns><see langword="true"/> when the drop-down maps this value.</returns>
    /// <remarks>
    /// Every guard is the oracle's, line for line: the <c>"!"</c>/<c>"?"</c> test on
    /// <c>dddw.name</c> [<c>n_cst_dwsvc.sru:L590</c>], the validity test on the child rather than on the
    /// return code [<c>:L592-L593</c>], the two column names [<c>:L594-L595</c>], their two column types
    /// [<c>:L596-L597</c>], the three-way rejection of each type [<c>:L598-L599</c>], the one-based row
    /// loop to <c>RowCount</c> inclusive [<c>:L600-L601</c>], and the skip on a null value
    /// [<c>:L617</c>, <c>:L633</c>].
    /// </remarks>
    private bool TryResolveDropDownDisplay(
        string column,
        string target,
        [NotNullWhen(true)] out string? display)
    {
        display = null;

        string dropDownName = _host.Describe(column + ".dddw.name");
        if (string.Equals(dropDownName, InvalidExpressionSentinel, StringComparison.Ordinal)
            || string.Equals(dropDownName, UndeterminedValueSentinel, StringComparison.Ordinal))
        {
            return false;
        }

        IDataWindowChild? child = null;

        // The return code is DISCARDED exactly as :L592 discards it; validity is established on the
        // handle itself at :L593.
        _ = _host.GetChild(column, ref child);
        if (child is null)
        {
            return false;
        }

        string displayColumn = _host.Describe(column + ".dddw.displaycolumn");
        string dataColumn = _host.Describe(column + ".dddw.datacolumn");

        string displayType = child.Describe(displayColumn + ".coltype");
        string dataType = child.Describe(dataColumn + ".coltype");

        if (IsUnusableColumnType(displayType) || IsUnusableColumnType(dataType))
        {
            return false;
        }

        long displayKind = ConvertColumnType(displayType);
        long dataKind = ConvertColumnType(dataType);
        long rowCount = child.RowCount();

        for (long row = 1L; row <= rowCount; row++)
        {
            string? dataText = ReadChildItemText(child, row, dataColumn, dataKind);
            if (dataText is null || !string.Equals(dataText, target, StringComparison.Ordinal))
            {
                continue;
            }

            string? displayText = ReadChildItemText(child, row, displayColumn, displayKind);
            if (displayText is null)
            {
                continue;
            }

            display = displayText;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Reports whether a column type read from a <c>Describe</c> is one of the three non-answers.
    /// </summary>
    /// <param name="colType">The column type text.</param>
    /// <returns><see langword="true"/> when it is the empty string or either sentinel.</returns>
    /// <remarks>
    /// The port of <c>if sDispColType = "" or sDispColType = "?" or sDispColType = "!" then return map</c>
    /// at <c>n_cst_dwsvc.sru:L598</c>, written once and applied to both column types as <c>:L598</c> and
    /// <c>:L599</c> apply it.
    /// </remarks>
    private static bool IsUnusableColumnType(string colType) =>
        colType.Length == 0
            || string.Equals(colType, InvalidExpressionSentinel, StringComparison.Ordinal)
            || string.Equals(colType, UndeterminedValueSentinel, StringComparison.Ordinal);

    /// <summary>
    /// Looks a data value up in a column's code table.
    /// </summary>
    /// <param name="column">The column name.</param>
    /// <param name="target">The data value's text.</param>
    /// <param name="display">The display value on a match.</param>
    /// <returns><see langword="true"/> when the code table maps this value.</returns>
    /// <remarks>
    /// <para>
    /// The port of <c>n_cst_dwsvc.sru:L650-L659</c>: start at index <c>1</c>, read, and loop
    /// <c>do while(sVal &lt;&gt; "")</c>. The empty string is the terminator and not an error, which
    /// Domain/DataWindowServiceHost.cs states as part of <c>GetValue</c>'s contract.
    /// </para>
    /// <para>
    /// A TAB AT THE VERY START IS ACCEPTED, AND THAT IS A ONE-BASED TRANSLATION TRAP AVOIDED. The oracle
    /// guards with <c>if nPos &gt; 0</c> where <c>nPos</c> is a ONE-BASED <c>Pos</c> result, so a tab in
    /// the first character gives <c>nPos = 1</c>, passes the guard, and yields an EMPTY display through
    /// <c>Left(sVal,0)</c>. The zero-based equivalent is therefore <c>tab &gt;= 0</c>; writing
    /// <c>tab &gt; 0</c> would look like a faithful transcription and would silently drop every
    /// empty-display entry. AAP 0.4.5.4 names this class of error the refactor's most dangerous hazard.
    /// </para>
    /// </remarks>
    private bool TryResolveCodeTableDisplay(
        string column,
        string target,
        [NotNullWhen(true)] out string? display)
    {
        display = null;

        long index = 1L;
        string entry = _host.GetValue(column, index);

        while (entry.Length != 0)
        {
            int tab = entry.IndexOf('\t');
            if (tab >= 0)
            {
                string entryData = entry[(tab + 1)..];
                if (string.Equals(entryData, target, StringComparison.Ordinal))
                {
                    display = entry[..tab];
                    return true;
                }
            }

            index++;
            entry = _host.GetValue(column, index);
        }

        return false;
    }

    /// <summary>
    /// Reads one item of a drop-down child as text, choosing the typed getter by column type.
    /// </summary>
    /// <param name="child">The child DataWindow.</param>
    /// <param name="row">The one-based row.</param>
    /// <param name="column">The column name.</param>
    /// <param name="columnKind">The <c>COL_TYPE_*</c> constant for the column.</param>
    /// <returns>The text, or <see langword="null"/> when the item is null or the type is unknown.</returns>
    /// <remarks>
    /// THE SIX ARMS ARE THE SIX AT <c>n_cst_dwsvc.sru:L603-L616</c> AND <c>:L619-L632</c>, in the same
    /// order and with the same getters. The oracle takes the string arm's value directly and wraps the
    /// other five in <c>String(...)</c>; <see cref="ExpressionValue.ToDisplayText"/> is that wrapping, so
    /// both arms funnel through one projection here and cannot disagree about formatting. A column type
    /// the switch does not recognise leaves the value unassigned in the oracle, which its very next line
    /// treats as null [<c>:L617</c>, <c>:L633</c>] - so null is what this method answers for it.
    /// </remarks>
    private static string? ReadChildItemText(
        IDataWindowChild child,
        long row,
        string column,
        long columnKind)
    {
        if (columnKind == DataWindowServiceBase.COL_TYPE_STRING)
        {
            return child.GetItemString(row, column);
        }

        if (columnKind == DataWindowServiceBase.COL_TYPE_DECIMAL)
        {
            return ExpressionValue.FromDecimal(child.GetItemDecimal(row, column)).ToDisplayText();
        }

        if (columnKind == DataWindowServiceBase.COL_TYPE_INTEGER)
        {
            return ExpressionValue.FromDouble(child.GetItemNumber(row, column)).ToDisplayText();
        }

        if (columnKind == DataWindowServiceBase.COL_TYPE_DATETIME)
        {
            return ExpressionValue.FromDateTime(child.GetItemDateTime(row, column)).ToDisplayText();
        }

        if (columnKind == DataWindowServiceBase.COL_TYPE_DATE)
        {
            return ExpressionValue.FromDate(child.GetItemDate(row, column)).ToDisplayText();
        }

        if (columnKind == DataWindowServiceBase.COL_TYPE_TIME)
        {
            return ExpressionValue.FromTime(child.GetItemTime(row, column)).ToDisplayText();
        }

        return null;
    }

    /// <summary>
    /// Converts a DataWindow column-type string to its <c>COL_TYPE_*</c> constant - the port of
    /// <c>_of_ConvertColType</c>.
    /// </summary>
    /// <param name="colType">
    /// The raw type text, for instance <c>"char(100)"</c>, <c>"decimal(2)"</c>, <c>"number"</c>,
    /// <c>"long"</c>, <c>"datetime"</c>, <c>"date"</c> or <c>"time"</c>.
    /// </param>
    /// <returns>
    /// One of <see cref="DataWindowServiceBase.COL_TYPE_STRING"/>,
    /// <see cref="DataWindowServiceBase.COL_TYPE_INTEGER"/>,
    /// <see cref="DataWindowServiceBase.COL_TYPE_DECIMAL"/>,
    /// <see cref="DataWindowServiceBase.COL_TYPE_DATETIME"/>,
    /// <see cref="DataWindowServiceBase.COL_TYPE_DATE"/>,
    /// <see cref="DataWindowServiceBase.COL_TYPE_TIME"/> or
    /// <see cref="DataWindowServiceBase.COL_TYPE_UNKNOWN"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE FIRST FIVE CHARACTERS DECIDE, AND ONLY THE FIRST FIVE. <c>n_cst_dwsvc.sru:L503-L518</c> is
    /// <c>choose case Left(colType,5)</c> over the arms <c>"char"</c>/<c>"char("</c>,
    /// <c>"numbe"</c>/<c>"long"</c>/<c>"ulong"</c>, <c>"decim"</c>/<c>"real"</c>, <c>"datet"</c>,
    /// <c>"date"</c> and <c>"time"</c>. Two consequences are load-bearing and are preserved: the
    /// truncation is what makes <c>char(100)</c> and <c>decimal(2)</c> match at all, and the ORDER of the
    /// last two arms is what keeps <c>datetime</c> - which truncates to <c>"datet"</c> - from being read
    /// as a date.
    /// </para>
    /// <para>
    /// PUBLIC SO THE SIBLING PORTS SHARE IT. <c>_of_ConvertColType</c> is <c>n_cst_dwsvc</c>'s own
    /// protected helper and this evaluator needs it to read a drop-down child's columns; exposing the one
    /// implementation is better than a second copy that could drift from these six arms.
    /// </para>
    /// </remarks>
    public static long ConvertColumnType(string? colType)
    {
        if (string.IsNullOrEmpty(colType))
        {
            return DataWindowServiceBase.COL_TYPE_UNKNOWN;
        }

        // PowerScript's Left clamps rather than throwing when the string is shorter than the count.
        string prefix = colType.Length <= 5 ? colType : colType[..5];

        if (string.Equals(prefix, "char", StringComparison.OrdinalIgnoreCase)
            || string.Equals(prefix, "char(", StringComparison.OrdinalIgnoreCase))
        {
            return DataWindowServiceBase.COL_TYPE_STRING;
        }

        if (string.Equals(prefix, "numbe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(prefix, "long", StringComparison.OrdinalIgnoreCase)
            || string.Equals(prefix, "ulong", StringComparison.OrdinalIgnoreCase))
        {
            return DataWindowServiceBase.COL_TYPE_INTEGER;
        }

        if (string.Equals(prefix, "decim", StringComparison.OrdinalIgnoreCase)
            || string.Equals(prefix, "real", StringComparison.OrdinalIgnoreCase))
        {
            return DataWindowServiceBase.COL_TYPE_DECIMAL;
        }

        if (string.Equals(prefix, "datet", StringComparison.OrdinalIgnoreCase))
        {
            return DataWindowServiceBase.COL_TYPE_DATETIME;
        }

        if (string.Equals(prefix, "date", StringComparison.OrdinalIgnoreCase))
        {
            return DataWindowServiceBase.COL_TYPE_DATE;
        }

        if (string.Equals(prefix, "time", StringComparison.OrdinalIgnoreCase))
        {
            return DataWindowServiceBase.COL_TYPE_TIME;
        }

        return DataWindowServiceBase.COL_TYPE_UNKNOWN;
    }


    /// <summary>
    /// <c>PinyinFirstLetterLike(text, pattern[, flags])</c> - dispatched to
    /// <see cref="PinyinFirstLetterMatcher"/>.
    /// </summary>
    /// <param name="invocation">The call.</param>
    /// <returns>The result: a boolean, or the malformed sentinel when matching is unavailable.</returns>
    /// <remarks>
    /// <para>
    /// THIS REGISTRATION IS THE ONLY WAY THE LEGACY REACHES THE MATCHER. It is never called from
    /// PowerScript: <c>n_cst_dwsvc_dropdownsearch.sru:L323</c> splices its NAME into a filter string,
    /// which the DataWindow then evaluates. So an evaluator that did not register it would leave the
    /// pinyin capability unreachable no matter how faithfully the matcher itself were ported.
    /// </para>
    /// <para>
    /// ARGUMENTS ARE PASSED AS RAW OBJECTS because
    /// <see cref="PinyinFirstLetterMatcher.TryInvokeFromExpression"/> is declared over an untyped list and
    /// applies its own text and flag coercions - see
    /// <see cref="ExpressionValue.ToRawObject"/> for why the wrapper must not be boxed instead.
    /// </para>
    /// <para>
    /// UNAVAILABILITY BECOMES THE MALFORMED SENTINEL PLUS THE MATCHER'S OWN STRUCTURED ERROR, never a
    /// silent <see langword="false"/>. A silent false would answer "this row does not match" for a
    /// capability that is missing rather than for a row that fails the test, which is the difference
    /// between a reported gap and a wrong result set - and AAP 0.6.5 is explicit that the pinyin filter is
    /// to be reported as blocked rather than approximated.
    /// </para>
    /// </remarks>
    private DataWindowExpressionResult EvaluatePinyinFirstLetterLike(
        ExpressionFunctionInvocation invocation)
    {
        if (invocation.ArgumentCount is not (2 or 3))
        {
            return invocation.WrongArity("2 or 3");
        }

        if (!invocation.TryEvaluateArguments(
                out ExpressionValue[] values,
                out DataWindowExpressionResult failure))
        {
            return failure;
        }

        object?[] raw = new object?[values.Length];
        for (int index = 0; index < values.Length; index++)
        {
            raw[index] = values[index].ToRawObject();
        }

        if (!_pinyinMatcher.TryInvokeFromExpression(
                raw,
                out PinyinMatchResult result,
                out ExpressionParseError? error))
        {
            return invocation.Invalid(error);
        }

        return invocation.Success(ExpressionValue.FromBoolean(result.IsMatch));
    }

    /// <summary>
    /// <c>Len(value)</c> - the CHARACTER count.
    /// </summary>
    /// <param name="invocation">The call.</param>
    /// <returns>The result.</returns>
    /// <remarks>
    /// PAIRED WITH <see cref="EvaluateLenA"/> AND NEVER THE SAME AS IT.
    /// <c>n_cst_dwsvc_contextmenu.sru:L1132-L1134</c> evaluates both over the same column and SUBTRACTS
    /// one from the other to count the wide characters:
    /// <c>nChsCnt = Abs(Long(Evaluate("Max(LenA(...)")) - nAsc2Cnt)</c> followed by
    /// <c>nAsc2Cnt -= nChsCnt</c>. Conflating the two would make that difference zero and every column
    /// would be measured as pure ASCII.
    /// </remarks>
    private static DataWindowExpressionResult EvaluateLen(ExpressionFunctionInvocation invocation)
    {
        if (invocation.ArgumentCount != 1)
        {
            return invocation.WrongArity("1");
        }

        DataWindowExpressionResult argument = invocation.EvaluateArgument(0);
        if (!argument.IsSuccess)
        {
            return argument;
        }

        if (!argument.Value.TryGetText(out string text))
        {
            // A null has no length. Never 0, which is a real length (DECISION D8).
            return invocation.Success(ExpressionValue.Null);
        }

        return invocation.Success(ExpressionValue.FromLong(text.Length));
    }

    /// <summary>
    /// <c>LenA(value)</c> - the DBCS BYTE count.
    /// </summary>
    /// <param name="invocation">The call.</param>
    /// <returns>The result.</returns>
    /// <remarks>
    /// DELEGATED TO <see cref="ParseErrorFormatter.LenA"/>, which is the port already in this folder: the
    /// oracle's own caret formatter measures its padding with <c>LenA</c> at
    /// <c>n_cst_dwsvc_columnexp.sru:L2403</c>, so the rule - ASCII one byte, everything else two, a
    /// surrogate pair counted once as two - is stated there once and reused here rather than restated.
    /// </remarks>
    private static DataWindowExpressionResult EvaluateLenA(ExpressionFunctionInvocation invocation)
    {
        if (invocation.ArgumentCount != 1)
        {
            return invocation.WrongArity("1");
        }

        DataWindowExpressionResult argument = invocation.EvaluateArgument(0);
        if (!argument.IsSuccess)
        {
            return argument;
        }

        if (!argument.Value.TryGetText(out string text))
        {
            return invocation.Success(ExpressionValue.Null);
        }

        return invocation.Success(ExpressionValue.FromLong(ParseErrorFormatter.LenA(text)));
    }

    /// <summary>
    /// <c>Abs(number)</c>.
    /// </summary>
    /// <param name="invocation">The call.</param>
    /// <returns>The result, on the same arm as its operand.</returns>
    private static DataWindowExpressionResult EvaluateAbs(ExpressionFunctionInvocation invocation)
    {
        if (invocation.ArgumentCount != 1)
        {
            return invocation.WrongArity("1");
        }

        DataWindowExpressionResult argument = invocation.EvaluateArgument(0);
        if (!argument.IsSuccess)
        {
            return argument;
        }

        ExpressionValue value = argument.Value;

        if (value.IsNull)
        {
            return invocation.Success(ExpressionValue.Null);
        }

        switch (value.Kind)
        {
            case ExpressionValueKind.Long:
            case ExpressionValueKind.Boolean:
            {
                _ = value.TryGetLong(out long number);

                // long.MinValue has no positive counterpart. Widening keeps the value exact instead
                // of overflowing, the same choice negation makes.
                return number == long.MinValue
                    ? invocation.Success(ExpressionValue.FromDecimal(-(decimal)number))
                    : invocation.Success(ExpressionValue.FromLong(Math.Abs(number)));
            }

            case ExpressionValueKind.Decimal:
            {
                _ = value.TryGetDecimal(out decimal number);
                return invocation.Success(ExpressionValue.FromDecimal(Math.Abs(number)));
            }

            case ExpressionValueKind.Double:
            {
                _ = value.TryGetDouble(out double number);
                return invocation.Success(ExpressionValue.FromDouble(Math.Abs(number)));
            }

            default:
                return invocation.Invalid("Abs requires a number.");
        }
    }

    /// <summary>
    /// <c>Long(value)</c> - conversion to a 64-bit integer, truncating toward zero.
    /// </summary>
    /// <param name="invocation">The call.</param>
    /// <returns>The result.</returns>
    /// <remarks>
    /// <para>
    /// UNPARSEABLE TEXT YIELDS ZERO, and that is a PRESERVED LEGACY BEHAVIOUR rather than leniency.
    /// PowerScript's <c>Long(string)</c> answers <c>0</c> for text that is not a number, and
    /// <c>DataWindowObjectExtensions.ToColumnId</c> in Domain/DataWindowServiceHost.cs preserves exactly
    /// that for the same reason - it is the ported <c>Long(dwo.ID)</c>. Throwing or answering a sentinel
    /// here would be an improvement the legacy does not have (constraint C-B).
    /// </para>
    /// <para>
    /// A MAGNITUDE PAST THE RANGE CLAMPS rather than wrapping, again matching the ported conversion in
    /// Domain/. A date or a time answers the malformed sentinel: PowerScript has no <c>Long(date)</c>.
    /// </para>
    /// </remarks>
    private static DataWindowExpressionResult EvaluateLong(ExpressionFunctionInvocation invocation)
    {
        if (invocation.ArgumentCount != 1)
        {
            return invocation.WrongArity("1");
        }

        DataWindowExpressionResult argument = invocation.EvaluateArgument(0);
        if (!argument.IsSuccess)
        {
            return argument;
        }

        ExpressionValue value = argument.Value;

        if (value.IsNull)
        {
            return invocation.Success(ExpressionValue.Null);
        }

        if (value.TryGetLong(out long number))
        {
            return invocation.Success(ExpressionValue.FromLong(number));
        }

        if (value.Kind == ExpressionValueKind.String)
        {
            return invocation.Success(ExpressionValue.FromLong(0L));
        }

        if (value.TryGetDouble(out double wide))
        {
            if (double.IsNaN(wide))
            {
                return invocation.Success(ExpressionValue.FromLong(0L));
            }

            if (wide >= long.MaxValue)
            {
                return invocation.Success(ExpressionValue.FromLong(long.MaxValue));
            }

            if (wide <= long.MinValue)
            {
                return invocation.Success(ExpressionValue.FromLong(long.MinValue));
            }

            return invocation.Success(ExpressionValue.FromLong((long)Math.Truncate(wide)));
        }

        return invocation.Invalid("Long requires a number or text.");
    }

    /// <summary>
    /// <c>Double(value)</c> - conversion to binary floating point.
    /// </summary>
    /// <param name="invocation">The call.</param>
    /// <returns>The result.</returns>
    /// <remarks>
    /// Unparseable text yields zero, for the reason given on <see cref="EvaluateLong"/>. A date or a time
    /// answers the malformed sentinel.
    /// </remarks>
    private static DataWindowExpressionResult EvaluateDouble(ExpressionFunctionInvocation invocation)
    {
        if (invocation.ArgumentCount != 1)
        {
            return invocation.WrongArity("1");
        }

        DataWindowExpressionResult argument = invocation.EvaluateArgument(0);
        if (!argument.IsSuccess)
        {
            return argument;
        }

        ExpressionValue value = argument.Value;

        if (value.IsNull)
        {
            return invocation.Success(ExpressionValue.Null);
        }

        if (value.TryGetDouble(out double number))
        {
            return invocation.Success(ExpressionValue.FromDouble(number));
        }

        if (value.Kind == ExpressionValueKind.String)
        {
            return invocation.Success(ExpressionValue.FromDouble(0d));
        }

        return invocation.Invalid("Double requires a number or text.");
    }

    /// <summary>
    /// <c>Round(number, digits)</c>.
    /// </summary>
    /// <param name="invocation">The call.</param>
    /// <returns>The result.</returns>
    /// <remarks>
    /// <para>
    /// HALF AWAY FROM ZERO, AND THIS IS THE SINGLE MOST IMPORTANT LINE IN THIS FUNCTION.
    /// PowerScript's <c>Round</c> rounds a midpoint AWAY FROM ZERO - <c>Round(0.5,0)</c> is <c>1</c> - while
    /// .NET's <c>Math.Round</c> and <c>decimal.Round</c> default to BANKER'S ROUNDING, which answers
    /// <c>0</c> for the same input. Omitting <see cref="MidpointRounding.AwayFromZero"/> would therefore
    /// produce a silently different number for every midpoint, and the documented macro at
    /// <c>docs/n_cst_dwsvc_columnexp.md:L126</c> - <c>Round(Double(args[1]),Long(args[2]))</c> - is a price
    /// formatter, where a midpoint is not a rare input.
    /// </para>
    /// <para>
    /// The decimal path is preferred so a rounded price keeps its scale; the double path is the fallback
    /// for a magnitude no decimal can hold, and is capped at the 15 digits <see cref="Math.Round(double,
    /// int, MidpointRounding)"/> accepts.
    /// </para>
    /// </remarks>
    private static DataWindowExpressionResult EvaluateRound(ExpressionFunctionInvocation invocation)
    {
        if (invocation.ArgumentCount != 2)
        {
            return invocation.WrongArity("2");
        }

        if (!invocation.TryEvaluateArguments(
                out ExpressionValue[] values,
                out DataWindowExpressionResult failure))
        {
            return failure;
        }

        if (values[0].IsNull || values[1].IsNull)
        {
            return invocation.Success(ExpressionValue.Null);
        }

        if (!values[1].TryGetLong(out long requested) || requested < 0L || requested > 28L)
        {
            return invocation.Invalid("Round takes a digit count between 0 and 28.");
        }

        int digits = (int)requested;

        if (values[0].Kind != ExpressionValueKind.Double
            && values[0].TryGetDecimal(out decimal exact))
        {
            try
            {
                return invocation.Success(
                    ExpressionValue.FromDecimal(
                        decimal.Round(exact, digits, MidpointRounding.AwayFromZero)));
            }
            catch (OverflowException)
            {
                // Rounding up at the very top of decimal's range. Reported rather than propagated.
                return invocation.Invalid("The rounded result is too large to represent.");
            }
        }

        if (values[0].TryGetDouble(out double wide))
        {
            int capped = digits > 15 ? 15 : digits;

            return invocation.Success(
                ExpressionValue.FromDouble(
                    Math.Round(wide, capped, MidpointRounding.AwayFromZero)));
        }

        return invocation.Invalid("Round requires a number.");
    }

    /// <summary>
    /// <c>String(value)</c> and <c>String(value, format)</c>.
    /// </summary>
    /// <param name="invocation">The call.</param>
    /// <returns>The result.</returns>
    /// <remarks>
    /// <para>
    /// THE ONE-ARGUMENT FORM IS WHAT THE ORACLE USES, at <c>n_cst_dwsvc_contextmenu.sru:L1183</c> and
    /// <c>:L1349</c>: <c>String({1}) &lt;&gt; if(...)</c>. It projects the value through
    /// <see cref="ExpressionValue.ToDisplayText"/>, and a null answers NULL rather than the empty string so
    /// the comparison at those two lines behaves as it does in the oracle (DECISION D9).
    /// </para>
    /// <para>
    /// THE TWO-ARGUMENT FORM ACCEPTS ONLY THE GENERAL MASK - A DEFINED NARROWING (DECISION D7c). The
    /// general mask means "no mask", which <c>:L1143</c>, <c>:L1168</c>, <c>:L1309</c> and <c>:L1334</c>
    /// each state by neutralising it, so that case is exact. Any other mask answers the malformed sentinel
    /// with a diagnostic naming the gap, because no in-scope EXPRESSION supplies one - the oracle applies
    /// real masks in PowerScript, at <c>:L1146</c>, <c>:L1148</c> and <c>:L1199</c>, outside the evaluator -
    /// and PowerBuilder's mask dialect cannot be characterized from the repository. AAP 0.1.5 requires that
    /// such a behaviour be narrowed with a defined error rather than widened with a guess, and a guessed
    /// mask would corrupt output while looking plausible.
    /// </para>
    /// </remarks>
    private static DataWindowExpressionResult EvaluateString(ExpressionFunctionInvocation invocation)
    {
        if (invocation.ArgumentCount is not (1 or 2))
        {
            return invocation.WrongArity("1 or 2");
        }

        DataWindowExpressionResult argument = invocation.EvaluateArgument(0);
        if (!argument.IsSuccess)
        {
            return argument;
        }

        if (invocation.ArgumentCount == 2)
        {
            DataWindowExpressionResult mask = invocation.EvaluateArgument(1);
            if (!mask.IsSuccess)
            {
                return mask;
            }

            string maskText = mask.Value.ToDisplayText() ?? string.Empty;

            if (!IsGeneralFormat(maskText))
            {
                return invocation.Invalid(
                    Formatting.Sprintf(
                        "String(value,'{1}') is not supported. Only the general mask is, because no "
                            + "in-scope expression supplies another and PowerBuilder's mask dialect cannot "
                            + "be reproduced without the behavioural oracle.",
                        maskText));
            }
        }

        if (!argument.Value.TryGetText(out string text))
        {
            return invocation.Success(ExpressionValue.Null);
        }

        return invocation.Success(ExpressionValue.FromString(text));
    }

    /// <summary>
    /// <c>if(condition, whenTrue, whenFalse)</c>.
    /// </summary>
    /// <param name="invocation">The call.</param>
    /// <returns>The result.</returns>
    /// <remarks>
    /// <para>
    /// EXACTLY ONE BRANCH IS EVALUATED, and <c>n_cst_dwsvc_contextmenu.sru:L1183</c> requires it: its true
    /// branch is <c>Describe("Evaluate('col'," + String(GetRow() + 1) + ")")</c> and its condition is
    /// <c>GetRow() &lt; RowCount()</c>, so on the last row the true branch would ask for the row after the
    /// last one. Evaluating both would turn the oracle's ordinary last-row case into a spurious read.
    /// </para>
    /// <para>
    /// A NULL CONDITION YIELDS NULL, not the false branch. A condition whose truth is unknown does not
    /// select an answer, and treating it as false would be the null-to-default collapse AAP 0.4.5.4
    /// forbids. The oracle's own defence against this is the <c>if(IsNull(x),...)</c> wrapper at
    /// <c>:L1186</c> and <c>:L1352</c>, which exists precisely because the condition has to be made
    /// definite first.
    /// </para>
    /// </remarks>
    private static DataWindowExpressionResult EvaluateIf(ExpressionFunctionInvocation invocation)
    {
        if (invocation.ArgumentCount != 3)
        {
            return invocation.WrongArity("3");
        }

        DataWindowExpressionResult condition = invocation.EvaluateArgument(0);
        if (!condition.IsSuccess)
        {
            return condition;
        }

        if (condition.Value.IsNull)
        {
            return invocation.Success(ExpressionValue.Null);
        }

        if (!condition.Value.TryGetBoolean(out bool flag))
        {
            return invocation.Invalid("The first argument of if(...) has no truth value.");
        }

        return invocation.EvaluateArgument(flag ? 1 : 2);
    }

    /// <summary>
    /// <c>GetRow()</c> - the DataWindow's current row.
    /// </summary>
    /// <param name="invocation">The call.</param>
    /// <returns>The result.</returns>
    /// <remarks>
    /// THE HOST'S CURRENT ROW, NOT THE ROW BEING EVALUATED (DECISION D6). <c>:L1183</c> depends on the
    /// difference: it writes <c>GetRow() + 1</c> inside an expression that <c>Find</c> evaluates for every
    /// row in the buffer, and it means "the row after the cursor" every time - not "the row after the one
    /// currently being tested".
    /// </remarks>
    private DataWindowExpressionResult EvaluateGetRow(ExpressionFunctionInvocation invocation) =>
        invocation.ArgumentCount == 0
            ? invocation.Success(ExpressionValue.FromLong(_host.GetRow()))
            : invocation.WrongArity("0");

    /// <summary>
    /// <c>RowCount()</c> - the primary buffer's row count, which is also its last valid row number.
    /// </summary>
    /// <param name="invocation">The call.</param>
    /// <returns>The result.</returns>
    private DataWindowExpressionResult EvaluateRowCount(ExpressionFunctionInvocation invocation) =>
        invocation.ArgumentCount == 0
            ? invocation.Success(ExpressionValue.FromLong(_host.RowCount()))
            : invocation.WrongArity("0");

    /// <summary>
    /// <c>IsNull(value)</c>.
    /// </summary>
    /// <param name="invocation">The call.</param>
    /// <returns>The result, always a boolean.</returns>
    /// <remarks>
    /// NEVER NULL ITSELF. Asking whether a value is null always has a definite answer, which is what makes
    /// it the tool the oracle reaches for at <c>n_cst_dwsvc_contextmenu.sru:L1186</c>, <c>:L1188</c>,
    /// <c>:L1352</c> and <c>:L1354</c> to turn an indefinite comparison into a definite one.
    /// </remarks>
    private static DataWindowExpressionResult EvaluateIsNull(ExpressionFunctionInvocation invocation)
    {
        if (invocation.ArgumentCount != 1)
        {
            return invocation.WrongArity("1");
        }

        DataWindowExpressionResult argument = invocation.EvaluateArgument(0);

        return argument.IsSuccess
            ? invocation.Success(ExpressionValue.FromBoolean(argument.Value.IsNull))
            : argument;
    }

    /// <summary>
    /// <c>Lower(value)</c>.
    /// </summary>
    /// <param name="invocation">The call.</param>
    /// <returns>The result.</returns>
    /// <remarks>
    /// INVARIANT CASE FOLDING, because the drop-down filter applies this to a column value on one side and
    /// to user-typed search text on the other (<c>n_cst_dwsvc_dropdownsearch.sru:L319</c>, <c>:L331</c>) and
    /// the two must fold identically for the comparison to mean anything. A culture-sensitive fold would
    /// make the same filter keep different rows on two hosts.
    /// </remarks>
    private static DataWindowExpressionResult EvaluateLower(ExpressionFunctionInvocation invocation)
    {
        if (invocation.ArgumentCount != 1)
        {
            return invocation.WrongArity("1");
        }

        DataWindowExpressionResult argument = invocation.EvaluateArgument(0);
        if (!argument.IsSuccess)
        {
            return argument;
        }

        if (!argument.Value.TryGetText(out string text))
        {
            return invocation.Success(ExpressionValue.Null);
        }

        return invocation.Success(ExpressionValue.FromString(text.ToLowerInvariant()));
    }


    /// <summary>
    /// <c>Date(value)</c>.
    /// </summary>
    /// <param name="invocation">The call.</param>
    /// <returns>The result.</returns>
    /// <remarks>
    /// <para>
    /// TEXT IS COERCED THROUGH <see cref="DateValidator.Coerce"/>, the port of the oracle's own date
    /// coercion at <c>se_cst_dw.sru:L241</c> - so this function and the item-change protocol agree about
    /// what a date literal is, including that unparseable text yields a NULL rather than an error. Sharing
    /// the port is what keeps them from drifting.
    /// </para>
    /// <para>
    /// The single-argument form only. PowerBuilder also has a three-argument <c>Date(year,month,day)</c>
    /// and no in-scope expression writes it, so it is not accepted (constraint C-B).
    /// </para>
    /// </remarks>
    private static DataWindowExpressionResult EvaluateDate(ExpressionFunctionInvocation invocation)
    {
        if (invocation.ArgumentCount != 1)
        {
            return invocation.WrongArity("1");
        }

        DataWindowExpressionResult argument = invocation.EvaluateArgument(0);
        if (!argument.IsSuccess)
        {
            return argument;
        }

        ExpressionValue value = argument.Value;

        switch (value.Kind)
        {
            case ExpressionValueKind.Null:
                return invocation.Success(ExpressionValue.Null);

            case ExpressionValueKind.Date:
                return invocation.Success(value);

            case ExpressionValueKind.DateTime:
                _ = value.TryGetMoment(out DateTime moment);
                return invocation.Success(
                    ExpressionValue.FromDate(DateOnly.FromDateTime(moment)));

            case ExpressionValueKind.String:
                _ = value.TryGetText(out string text);
                return invocation.Success(ExpressionValue.FromDate(DateValidator.Coerce(text)));

            default:
                return invocation.Invalid("Date requires text, a date or a datetime.");
        }
    }

    /// <summary>
    /// <c>DateTime(value)</c>.
    /// </summary>
    /// <param name="invocation">The call.</param>
    /// <returns>The result.</returns>
    /// <remarks>
    /// Text is coerced through <see cref="DateTimeValidator.Coerce"/>, the port of the oracle's coercion at
    /// <c>se_cst_dw.sru:L239</c>. A date widens to midnight, which is what PowerBuilder's own
    /// <c>DateTime(date)</c> does.
    /// </remarks>
    private static DataWindowExpressionResult EvaluateDateTime(ExpressionFunctionInvocation invocation)
    {
        if (invocation.ArgumentCount != 1)
        {
            return invocation.WrongArity("1");
        }

        DataWindowExpressionResult argument = invocation.EvaluateArgument(0);
        if (!argument.IsSuccess)
        {
            return argument;
        }

        ExpressionValue value = argument.Value;

        switch (value.Kind)
        {
            case ExpressionValueKind.Null:
                return invocation.Success(ExpressionValue.Null);

            case ExpressionValueKind.DateTime:
                return invocation.Success(value);

            case ExpressionValueKind.Date:
                _ = value.TryGetMoment(out DateTime widened);
                return invocation.Success(ExpressionValue.FromDateTime(widened));

            case ExpressionValueKind.String:
                _ = value.TryGetText(out string text);
                return invocation.Success(
                    ExpressionValue.FromDateTime(DateTimeValidator.Coerce(text)));

            default:
                return invocation.Invalid("DateTime requires text, a date or a datetime.");
        }
    }

    /// <summary>
    /// <c>Time(value)</c>.
    /// </summary>
    /// <param name="invocation">The call.</param>
    /// <returns>The result.</returns>
    /// <remarks>
    /// <para>
    /// A DOCUMENTED DEFECT TRAVELS THROUGH HERE, AND IT IS PRESERVED. Text is coerced through
    /// <see cref="TimeValidator.Coerce"/>, the port of <c>se_cst_dw.sru:L243</c>, and that port answers
    /// <see cref="TimeValidator.UncoercibleFallback"/> - midnight - for text it cannot parse, because the
    /// oracle writes whatever the conversion produced without checking it. This function therefore inherits
    /// the same behaviour rather than answering a null, which is the whole point of routing through the
    /// port instead of parsing here (constraint C-B).
    /// </para>
    /// </remarks>
    private static DataWindowExpressionResult EvaluateTime(ExpressionFunctionInvocation invocation)
    {
        if (invocation.ArgumentCount != 1)
        {
            return invocation.WrongArity("1");
        }

        DataWindowExpressionResult argument = invocation.EvaluateArgument(0);
        if (!argument.IsSuccess)
        {
            return argument;
        }

        ExpressionValue value = argument.Value;

        switch (value.Kind)
        {
            case ExpressionValueKind.Null:
                return invocation.Success(ExpressionValue.Null);

            case ExpressionValueKind.Time:
                return invocation.Success(value);

            case ExpressionValueKind.DateTime:
                _ = value.TryGetMoment(out DateTime moment);
                return invocation.Success(
                    ExpressionValue.FromTime(TimeOnly.FromDateTime(moment)));

            case ExpressionValueKind.String:
                _ = value.TryGetText(out string text);
                return invocation.Success(ExpressionValue.FromTime(TimeValidator.Coerce(text)));

            default:
                return invocation.Invalid("Time requires text, a time or a datetime.");
        }
    }

    /// <summary>
    /// <c>Describe(property)</c> inside an expression - the nested read, which may nest another
    /// evaluation.
    /// </summary>
    /// <param name="invocation">The call.</param>
    /// <returns>The result, always text or null.</returns>
    /// <remarks>
    /// <para>
    /// THE CONSTRUCTION THIS EXISTS FOR, at <c>n_cst_dwsvc_contextmenu.sru:L1183</c> AND <c>:L1349</c>:
    /// </para>
    /// <code>
    /// String({1}) &lt;&gt; if(GetRow() &lt; RowCount(),Describe("Evaluate('{1}'," + String(GetRow() + 1) + ")"),'')
    /// </code>
    /// <para>
    /// The argument is a STRING whose content is itself an <c>Evaluate</c> property, so evaluation is
    /// genuinely recursive and had to be designed in. The dispatch is the same one
    /// <see cref="TryDescribe"/> performs at the top level, which is why both go through
    /// <see cref="TryUnwrapEvaluateProperty"/>: one seam, one behaviour.
    /// </para>
    /// <para>
    /// AN INNER SENTINEL COMES BACK AS TEXT AND DOES NOT ABORT THE OUTER EXPRESSION. The oracle's
    /// <c>Describe</c> returns a string, and the comparison at <c>:L1183</c> is a string comparison
    /// against whatever that string is - <c>"!"</c> included. Propagating an inner failure outward would
    /// change which row <c>Find</c> stops on.
    /// </para>
    /// </remarks>
    private DataWindowExpressionResult EvaluateDescribeFunction(
        ExpressionFunctionInvocation invocation)
    {
        if (invocation.ArgumentCount != 1)
        {
            return invocation.WrongArity("1");
        }

        DataWindowExpressionResult argument = invocation.EvaluateArgument(0);
        if (!argument.IsSuccess)
        {
            return argument;
        }

        if (!argument.Value.TryGetText(out string property))
        {
            return invocation.Success(ExpressionValue.Null);
        }

        if (TryUnwrapEvaluateProperty(property, out string payload, out long nestedRow))
        {
            DataWindowExpressionResult nested = TryEvaluate(payload, nestedRow);

            return invocation.Success(ExpressionValue.FromString(nested.Text));
        }

        return invocation.Success(ExpressionValue.FromString(_host.Describe(property)));
    }

    /// <summary>
    /// <c>dwValueToExp(value)</c> - the value's expression literal, as TEXT.
    /// </summary>
    /// <param name="invocation">The call.</param>
    /// <returns>The result, always text.</returns>
    /// <remarks>
    /// <para>
    /// CALLED FROM INSIDE AN EXPRESSION, WHICH IS WHY IT IS REGISTERED HERE.
    /// <c>n_cst_dwsvc_columnexp.sru:L2390</c> is
    /// <c>sVal = _of_Evaluate("dwValueToExp(" + sExp + ")",row)</c> - the engine wraps a preprocessed
    /// sub-expression in this call and evaluates the whole thing, so the function has to exist in the
    /// evaluator and not only as a PowerScript global.
    /// </para>
    /// <para>
    /// A NULL ARGUMENT IS NOT AN ABORT. It answers the literal TEXT <c>"dwNvlNumber()"</c>, which the
    /// engine then splices into another expression and asks this evaluator to evaluate - closing the loop
    /// the five zero-argument producers complete. See
    /// <see cref="ExpressionValue.ToExpressionLiteral"/> for why a typeless null names the numeric producer.
    /// </para>
    /// </remarks>
    private static DataWindowExpressionResult EvaluateValueToExpression(
        ExpressionFunctionInvocation invocation)
    {
        if (invocation.ArgumentCount != 1)
        {
            return invocation.WrongArity("1");
        }

        DataWindowExpressionResult argument = invocation.EvaluateArgument(0);

        return argument.IsSuccess
            ? invocation.Success(
                ExpressionValue.FromString(argument.Value.ToExpressionLiteral()))
            : argument;
    }

    // ==============================================================================================
    //  AGGREGATES
    // ==============================================================================================

    /// <summary>
    /// <c>Max(expression [for scope])</c> and <c>Sum(expression [for scope])</c>.
    /// </summary>
    /// <param name="invocation">The call.</param>
    /// <param name="isSum">
    /// <see langword="true"/> for <c>Sum</c>, <see langword="false"/> for <c>Max</c>.
    /// </param>
    /// <returns>The result.</returns>
    /// <remarks>
    /// <para>
    /// THE ARGUMENT IS RE-EVALUATED ONCE PER ROW, which is what an aggregate is.
    /// <c>n_cst_dwsvc_contextmenu.sru:L1132</c> writes <c>Max(Len(LookUpDisplay(col)))</c> and uses the
    /// answer as a column width, so the inner expression is evaluated for every row in scope and the
    /// widest answer wins.
    /// </para>
    /// <para>
    /// NULL ROWS ARE SKIPPED, not counted as zero. That is what an aggregate over a partly-null column
    /// means, and it keeps <c>Sum</c> from being dragged toward zero by absent values.
    /// </para>
    /// <para>
    /// AN EMPTY SCOPE ANSWERS 0 FOR <c>Sum</c> AND NULL FOR <c>Max</c>, and the asymmetry is deliberate.
    /// Zero is the identity of addition, so the sum of no rows genuinely is zero; the maximum of no rows
    /// has no value at all, and answering zero would fabricate one. The consequence at the call site is
    /// the right one: <c>:L1132</c> wraps the answer in PowerScript's <c>Long(...)</c>, which turns the
    /// empty string this file renders a null as into <c>0</c> - an empty DataWindow measures as width
    /// zero, which is what it should be.
    /// </para>
    /// <para>
    /// A SENTINEL OR AN ABORT FROM ANY ROW STOPS THE WHOLE AGGREGATE AND PROPAGATES. An aggregate whose
    /// input failed for one row has no defensible value, and continuing would report a total computed over
    /// a silently smaller set.
    /// </para>
    /// </remarks>
    private DataWindowExpressionResult EvaluateAggregate(
        ExpressionFunctionInvocation invocation,
        bool isSum)
    {
        if (invocation.ArgumentCount != 1)
        {
            return invocation.WrongArity("1");
        }

        if (!TryResolveAggregateRange(
                invocation,
                out long firstRow,
                out long lastRow,
                out DataWindowExpressionResult rangeFailure))
        {
            return rangeFailure;
        }

        ExpressionValue accumulator = ExpressionValue.Null;
        bool any = false;

        for (long row = firstRow; row <= lastRow; row++)
        {
            DataWindowExpressionResult element = invocation.EvaluateArgument(0, row);
            if (!element.IsSuccess)
            {
                return element;
            }

            if (element.Value.IsNull)
            {
                continue;
            }

            if (!any)
            {
                accumulator = element.Value;
                any = true;
                continue;
            }

            if (isSum)
            {
                if (!TryAccumulateSum(accumulator, element.Value, out ExpressionValue total))
                {
                    return invocation.Invalid(
                        "Sum requires numbers a decimal or a double can hold on every row in scope.");
                }

                accumulator = total;
                continue;
            }

            ExpressionComparison comparison = CompareValues(element.Value, accumulator);

            if (comparison == ExpressionComparison.Incomparable)
            {
                return invocation.Invalid(
                    "Max requires values of one comparable type across every row in scope.");
            }

            if (comparison == ExpressionComparison.Greater)
            {
                accumulator = element.Value;
            }
        }

        if (!any)
        {
            return invocation.Success(
                isSum ? ExpressionValue.FromLong(0L) : ExpressionValue.Null);
        }

        return invocation.Success(accumulator);
    }

    /// <summary>
    /// Resolves the inclusive one-based row range an aggregate covers.
    /// </summary>
    /// <param name="invocation">The call, which carries the scope.</param>
    /// <param name="firstRow">The first row.</param>
    /// <param name="lastRow">The last row, inclusive.</param>
    /// <param name="failure">The result to return when the range cannot be resolved.</param>
    /// <returns><see langword="true"/> when a range was resolved.</returns>
    /// <remarks>
    /// <para>
    /// <c>for group</c> IS A DEFINED NARROWING AND ANSWERS THE MALFORMED SENTINEL (DECISION D7b). A group's
    /// row range comes from the group-band model, which belongs to the deferred DesignSystem and is
    /// reserved at <c>/v1/design/**</c> (AAP 0.4.4). It is deliberately NOT quietly widened to every row:
    /// that would answer a plausible number computed over the wrong set, and AAP 0.1.5 requires a narrowing
    /// with a defined error instead of a widening with a guess. No in-scope expression writes a group
    /// scope, so nothing that works today stops working.
    /// </para>
    /// <para>
    /// <c>for page</c> IS THE SAME DEFINED NARROWING (DECISION D7a). It delegates to
    /// <see cref="PageResolver"/>, whose default - <see cref="UnresolvedPageResolver"/> - resolves no
    /// range at all, so <c>dw_sqlite.srd:L27</c>'s <c>sum(salary for page)</c> answers the malformed
    /// sentinel until a caller injects an authoritative resolver. It is deliberately NOT widened to the
    /// whole buffer by default: that returns the grand total where a page total was asked for, and the
    /// answer carries nothing that distinguishes the two. See <see cref="IExpressionPageResolver"/> for
    /// why the page boundary itself is not computed here and for the two opt-in resolvers.
    /// </para>
    /// <para>
    /// The resolved range is clamped to the buffer, so a resolver that over-reports cannot make the loop
    /// read past the last row.
    /// </para>
    /// </remarks>
    private bool TryResolveAggregateRange(
        ExpressionFunctionInvocation invocation,
        out long firstRow,
        out long lastRow,
        out DataWindowExpressionResult failure)
    {
        long rowCount = _host.RowCount();
        firstRow = 1L;
        lastRow = rowCount;
        failure = default;

        switch (invocation.Scope)
        {
            case ExpressionAggregateScope.Group:
                failure = invocation.Invalid(
                    Formatting.Sprintf(
                        "{1}(... for group {2}) is not available in this service. A group's row range "
                            + "comes from the DataWindow band model, which is the deferred DesignSystem "
                            + "capability reserved at /v1/design/**. The scope is refused rather than "
                            + "widened to every row, which would answer a plausible total over the wrong "
                            + "set of rows.",
                        invocation.Name,
                        invocation.GroupLevel.ToString(CultureInfo.InvariantCulture)));
                return false;

            case ExpressionAggregateScope.Page:
                if (!_pageResolver.TryGetPageRange(
                        invocation.Row,
                        rowCount,
                        out firstRow,
                        out lastRow))
                {
                    failure = invocation.Invalid(
                        Formatting.Sprintf(
                            "{1}(... for page) could not resolve the page containing row {2}. A page "
                                + "boundary is computed from band heights and print margins, which is "
                                + "the deferred DesignSystem capability reserved at /v1/design/**, so "
                                + "no page range is assumed here. The scope is refused rather than "
                                + "widened to every row, which would answer the grand total where the "
                                + "page total was asked for. Install an authoritative page resolver - "
                                + "FixedRowsPerPageResolver for a known page size, or "
                                + "WholeBufferPageResolver to state that this surface is unpaginated.",
                            invocation.Name,
                            invocation.Row.ToString(CultureInfo.InvariantCulture)));
                    return false;
                }

                break;

            default:
                break;
        }

        if (firstRow < 1L)
        {
            firstRow = 1L;
        }

        if (lastRow > rowCount)
        {
            lastRow = rowCount;
        }

        return true;
    }

    /// <summary>
    /// Adds one element into a running sum, promoting as little as the operands require.
    /// </summary>
    /// <param name="accumulator">The running total.</param>
    /// <param name="element">The element to add.</param>
    /// <param name="sum">The new total.</param>
    /// <returns>
    /// <see langword="false"/> when either operand is not numeric, or the total overflows every arm.
    /// </returns>
    /// <remarks>
    /// PROMOTION FOLLOWS DECISION D3 - decimal is preferred over double, and the integral arm survives only
    /// while both operands are integral and the total still fits. That is what makes
    /// <c>sum(salary for page)</c> over <c>dw_sqlite.srd:L12</c>'s <c>decimal(2)</c> column answer a decimal
    /// with two places rather than a binary approximation of one.
    /// </remarks>
    private static bool TryAccumulateSum(
        in ExpressionValue accumulator,
        in ExpressionValue element,
        out ExpressionValue sum)
    {
        sum = ExpressionValue.Null;

        if (!accumulator.IsNumeric || !element.IsNumeric)
        {
            return false;
        }

        bool anyDouble = accumulator.Kind == ExpressionValueKind.Double
            || element.Kind == ExpressionValueKind.Double;

        if (anyDouble)
        {
            if (!accumulator.TryGetDouble(out double left) || !element.TryGetDouble(out double right))
            {
                return false;
            }

            sum = ExpressionValue.FromDouble(left + right);
            return true;
        }

        if (!accumulator.TryGetDecimal(out decimal exactLeft)
            || !element.TryGetDecimal(out decimal exactRight))
        {
            return false;
        }

        decimal total;
        try
        {
            total = exactLeft + exactRight;
        }
        catch (OverflowException)
        {
            // Past decimal's range. Reported so the caller can answer the malformed sentinel rather than
            // propagating an exception out of an expression.
            return false;
        }

        bool integral = accumulator.Kind != ExpressionValueKind.Decimal
            && element.Kind != ExpressionValueKind.Decimal
            && total == decimal.Truncate(total)
            && total <= long.MaxValue
            && total >= long.MinValue;

        sum = integral
            ? ExpressionValue.FromLong((long)total)
            : ExpressionValue.FromDecimal(total);

        return true;
    }
}
