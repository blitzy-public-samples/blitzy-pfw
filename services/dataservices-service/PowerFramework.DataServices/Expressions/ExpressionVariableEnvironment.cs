// ==============================================================================================
//  ExpressionVariableEnvironment - the macro variable type model of the column-expression engine
//  --------------------------------------------------------------------------------------------
//  PORTED FROM    ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru (2,435
//                 lines, the largest in-scope legacy object), specifically:
//                     :L50-L56    vardata          -> VariableReference
//                     :L58-L63    funcdata         -> FunctionReference
//                     :L65-L71    globalvardata    -> GlobalVariable
//                     :L73-L78    localvardata     -> LocalVariableDefinition
//                     :L80-L83    foreignvardata   -> ForeignVariableReference
//                     :L103       GLOBALVARDATA GlobalVars[]  -> ExpressionVariableEnvironment
//                     :L117-L118  VAR_LOCAL / VAR_FOREIGN     -> the two preserved constants
//                     :L153-L159  the seven of_addvar overloads -> ExpressionVariableValue
//                     :L1284-L1300 the three parser sigil bits  -> ExpansionSigils
//                 and docs/n_cst_dwsvc_columnexp.md, the authoritative legacy specification for
//                 the macro variable surface (§宏变量 :L13-L102, §宏函数 :L104-L151).
//
//  ORACLE STATUS  Both of those files are READ ONLY (constraint C-C). They are the behavioural
//                 oracle for parity testing, never an edit target, and nothing else in the
//                 repository can adjudicate a disagreement about a shape declared here. EVERY
//                 member below therefore carries the n_cst_dwsvc_columnexp.sru:L locator it was
//                 taken from, written as [:Lnnn] once the file is named.
//
//  LICENCE        The legacy object header carries the framework's BSD 2-Clause notice and its
//                 four-condition Chinese restatement. That block is deliberately NOT copied here:
//                 the obligation is discharged by the repository-root NOTICE and LICENSE.
//
//  WHAT THIS FILE IS, AND WHY IT IS THE FIRST FILE IN Expressions/
//  --------------------------------------------------------------------------------------------
//  It is the FOUNDATIONAL VALUE-TYPE MODULE of the Expressions/ folder: it has no intra-folder
//  dependency, and every other file in the folder - ColumnExpressionEngine, ExpressionSession,
//  MacroInvoker, ParseErrorFormatter, DataWindowExpressionEvaluator, PinyinFirstLetterMatcher -
//  consumes the shapes declared here. Get the type model right and the rest of the folder follows;
//  collapse a type to a string and the coercion behaviour of the whole engine changes silently.
//
//  It is a TYPE MODEL, NOT A RULES ENGINE (constraint C-B). Nothing here validates, normalises,
//  coerces, renders, recalculates, propagates a dirty flag or rejects a duplicate. Every one of
//  those is legacy behaviour that belongs to a specific legacy routine, and each is named at the
//  point where a reader might expect to find it, with the locator of the routine that owns it, so
//  the omission is auditable rather than accidental.
//
//  THE FIVE DESIGN DECISIONS, AND THE EVIDENCE FOR EACH
//  --------------------------------------------------------------------------------------------
//  DECISION 1 - EXACTLY SEVEN VARIANTS, AS A CLOSED DISCRIMINATED UNION, NEVER A STRING MAP.
//  The seven are read off the seven typed `of_addvar` overloads [:L153-L159] and confirmed against
//  the seven matching `of_setvar` overloads [:L160-L166], which recur in two further arities
//  [:L179-L185 with recalc, :L188-L194 with recalc and force]. In the source's own order:
//
//      time [:L153]   string [:L154]   long [:L155]   double [:L156]   datetime [:L157]
//      date [:L158]   boolean [:L159]
//
//  THE FOURTH IS `double`, NOT `decimal`. The legacy parameter is declared `readonly double value`
//  [:L156] and there is NO decimal overload anywhere in the set, so a decimal variant would invent
//  a capability the legacy cannot express and a caller could then declare a variable the coercion
//  path has nothing to dispatch on. Collapsing all seven to their string renderings would lose
//  exactly the information the coercion depends on: adding a `long` variable to a `date` variable
//  does not behave like concatenating their renderings, and the loss stays invisible until a
//  result is subtly wrong - the worst failure mode for a parity migration.
//
//  DECISION 2 - THE VALUE IS CARRIED BESIDE THE STRUCTURE PORTS, NOT INSIDE THEM.
//  `localvardata` has FOUR fields [:L73-L78] and `globalvardata` has FIVE [:L65-L71]; the ports
//  below match those counts exactly, so neither gains a typed-value field. That is faithful,
//  because the legacy holds a value variable's value INSIDE ITS EXPRESSION TEXT: each typed
//  `of_addvar` overload renders a literal and delegates to `of_addvarexp` [:L1091-L1109]. The
//  typed value therefore travels as a separate ExpressionVariableValue, paired with a variable by
//  the engine - which is precisely what the published contract does when it adds `value` as a
//  wire-only fifth field on LocalVarData [shared/PowerFramework.Contracts/Proto/
//  dataservices.v1.proto:L2669-L2676].
//
//  DECISION 3 - NO RENDERING LIVES HERE, BECAUSE IT ALREADY LIVES SOMEWHERE ELSE.
//  The per-type literal forms the legacy emits are, verbatim from the oracle:
//
//      time      "Time('" + String(value) + "')"       [:L1091]
//      string    "'" + value + "'"                     [:L1094]
//      long      String(value)                         [:L1097]
//      double    String(value)                         [:L1100]
//      datetime  "DateTime('" + String(value) + "')"   [:L1103]
//      date      "Date('" + String(value) + "')"       [:L1106]
//      boolean   String(value)                         [:L1109]
//
//  They are recorded here as the REASON the type must survive the boundary, and nowhere else in
//  this file. The DataWindow expression literal grammar is already ported, once, in
//  Validators/ValueToExpression.cs (from dwvaluetoexp.srf) together with the five column-type
//  validators that supply its formatting and its null sentinels. A second implementation of that
//  grammar here would be a duplicate that cannot be kept in agreement forever, so `of_addvar`'s
//  rendering belongs to the engine's `of_addvar` port, which owns both halves of [:L1091-L1109].
//
//  DECISION 4 - NO LIVE OBJECT REFERENCE, ANYWHERE, OF ANY KIND.
//  `foreignvardata.expsvc` is declared `n_cst_dwsvc_columnexp expsvc` [:L82] - a pointer to
//  another live instance of the service class, dereferenced synchronously at [:L2199-L2200] and
//  again at [:L2384-L2385]. A pointer cannot be serialized, so ForeignVariableReference carries an
//  OPAQUE SESSION-SCOPED HANDLE VALUE instead. The handle abstraction itself belongs to
//  ExpressionSession, which declares it so that neither the session nor the engine depends on the
//  other; this file holds the handle's VALUE and never a service reference.
//
//  DECISION 5 - THE SIGIL MODEL IS THREE ORTHOGONAL BITS, NOT ONE ENUM.
//  See ExpansionSigils. This is the single most easily lost discovery in the port and it is not
//  stated in any brief: the parser sets `bIsCtx`, `bIsMacro` and `bDD` independently
//  [:L1284-L1300], and the `@` context sigil is documented in no specification at all.
//
//  BOUNDARIES THIS FILE OBSERVES
//  --------------------------------------------------------------------------------------------
//  C-A / C-I  Only the BCL and the published contracts project are referenced. No project under
//             services/gateway-service, services/persistence-service or services/security-service
//             is reachable from here, and no shared behaviour crosses a service boundary.
//  C-B        No behaviour beyond the legacy's: no validation, no normalisation, no convenience
//             coercion, no rendering. Introducing a validation framework here is excluded outright
//             by the refactor's dependency policy.
//  C-D        Pure headless data. No UI, no theming, no DPI conversion, no font measurement, no
//             window geometry, no popup menu, no win32 interop, and no dialog of any kind - the
//             28 MessageBox call sites in the legacy object are ParseErrorFormatter's obligation,
//             reproduced there as structured errors, and none of them is reachable from here.
//  C-F        No secret, no credential, no key material and no connection string appears here.
//  C-K        Every technology-specific and boundary-specific decision above is documented with
//             the evidence that forced it, including the two decisions NOT to do something.
// ==============================================================================================

using System.Collections.Immutable;

using PowerFramework.Contracts.DataServices.V1;

namespace PowerFramework.DataServices.Expressions;

/// <summary>
/// The seven scalar types a PowerFramework macro variable can hold, plus the unset discriminator.
/// </summary>
/// <remarks>
/// <para>
/// EXACTLY SEVEN, AND THEY ARE READ OFF THE ORACLE RATHER THAN CHOSEN. The members below are the
/// seven typed <c>of_addvar</c> overloads
/// [ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru:L153-L159], in the
/// source's own declaration order, and the same seven recur as <c>of_setvar</c> overloads in three
/// arities [:L160-L166, :L179-L185, :L188-L194]. There is no eighth: a caller cannot declare a
/// variable of any other type, because no other overload exists.
/// </para>
/// <para>
/// THE NUMBERING IS NOT ARBITRARY. Values 1 through 7 are deliberately identical to the field
/// numbers of the <c>VarValue</c> oneof arms on contract C-04
/// [shared/PowerFramework.Contracts/Proto/dataservices.v1.proto:L2864-L2892], so the domain
/// discriminator and the wire discriminator can be audited against one another by value as well as
/// by name.
/// </para>
/// <para>
/// THE <c>Value</c> SUFFIX IS UNIFORM, AND IT IS APPLIED FOR TWO REASONS RATHER THAN AS DECORATION.
/// It names each member after the wire oneof arm it corresponds to, keeping one vocabulary across the
/// domain and the contract; and it keeps three of the seven - the string, long and double arms - clear
/// of the analyzer that reports an identifier which IS a type name, so no naming suppression is needed
/// for this file beyond the one the two preserved constants require. The suffix is applied to all seven
/// even where neither reason bites, because a rule that holds for seven cases out of seven is one a
/// reader can trust, whereas a rule with exceptions invites the exceptions to be relitigated. This is
/// naming only: the discriminator's VALUES, and every legacy spelling that travels in a payload, are
/// untouched.
/// </para>
/// <para>
/// <see cref="None"/> IS THE UNSET DISCRIMINATOR, NOT AN EIGHTH VARIABLE TYPE. It exists because
/// <see cref="ExpressionVariableValue"/> is a struct and <c>default</c> has to mean something
/// honest: it mirrors an UNSET protobuf oneof, which the contract distinguishes from a set arm
/// carrying null. A value whose kind is <see cref="None"/> carries no payload of any type and
/// cannot be produced by any factory.
/// </para>
/// </remarks>
public enum ExpressionVariableKind
{
    /// <summary>
    /// No variant is set. The state of <c>default(ExpressionVariableValue)</c> and of
    /// <see cref="ExpressionVariableValue.Unset"/>, equivalent to an unset protobuf oneof. Not one
    /// of the seven legacy types.
    /// </summary>
    None = 0,

    /// <summary>
    /// A time of day with no date component - PowerBuilder <c>time</c>, carried as
    /// <see cref="TimeOnly"/>. <c>of_addvar(name, readonly time value)</c> [:L153].
    /// </summary>
    TimeValue = 1,

    /// <summary>
    /// Text - PowerBuilder <c>string</c>, carried as <see cref="string"/>.
    /// <c>of_addvar(name, readonly string value)</c> [:L154].
    /// </summary>
    StringValue = 2,

    /// <summary>
    /// A 32-bit signed integer at source, widened to <see cref="long"/> here and to <c>int64</c> on
    /// the wire - PowerBuilder <c>long</c>. <c>of_addvar(name, readonly long value)</c> [:L155].
    /// </summary>
    LongValue = 3,

    /// <summary>
    /// A double-precision binary floating-point number - PowerBuilder <c>double</c>, carried as
    /// <see cref="double"/>. <c>of_addvar(name, readonly double value)</c> [:L156].
    /// </summary>
    /// <remarks>
    /// GENUINELY <c>double</c> AND NOT <c>decimal</c>. The legacy parameter is declared
    /// <c>readonly double value</c> and the overload set contains no decimal arm, so <c>double</c>
    /// is the faithful carrier. The refactor's general rule that a legacy <c>decimal</c> must never
    /// be carried as a double is untouched by this, because there is no legacy decimal here to
    /// carry.
    /// </remarks>
    DoubleValue = 4,

    /// <summary>
    /// A date and time together, unzoned - PowerBuilder <c>datetime</c>, carried as
    /// <see cref="System.DateTime"/>. <c>of_addvar(name, readonly datetime value)</c> [:L157].
    /// </summary>
    DateTimeValue = 5,

    /// <summary>
    /// A calendar date with no time component - PowerBuilder <c>date</c>, carried as
    /// <see cref="DateOnly"/>. <c>of_addvar(name, readonly date value)</c> [:L158].
    /// </summary>
    DateValue = 6,

    /// <summary>
    /// A truth value - PowerBuilder <c>boolean</c>, carried as <see cref="bool"/>.
    /// <c>of_addvar(name, readonly boolean value)</c> [:L159].
    /// </summary>
    /// <remarks>
    /// The only member whose name is not a straight PascalCase of its wire arm: the contract spells
    /// this one <c>bool_value</c> while the PowerBuilder keyword is <c>boolean</c>. The legacy spelling
    /// wins here, so the member, the factory and the accessor all read <c>Boolean</c> consistently.
    /// </remarks>
    BooleanValue = 7,
}

/// <summary>
/// A macro variable's value, as a closed discriminated union over the seven scalar types the legacy
/// <c>of_addvar</c> overload set can express, with null representable in every one of them.
/// </summary>
/// <remarks>
/// <para>
/// WHY A UNION AND NOT A STRING. Contract C-04 states the requirement and the reason:
/// the variable environment is "A DISCRIMINATED UNION OVER EXACTLY SEVEN TYPES ... NEVER A
/// STRINGLY-TYPED MAP", because collapsing the seven to their string renderings discards exactly
/// the type information the legacy coercion dispatches on
/// [shared/PowerFramework.Contracts/Proto/dataservices.v1.proto:L2839-L2841]. An
/// <c>object?</c> payload would be no better: it re-opens a closed set and forces every consumer to
/// unbox and re-test.
/// </para>
/// <para>
/// THREE STATES, ALL DISTINGUISHABLE, AND THAT IS THE POINT. PowerBuilder has null for value types
/// and the framework's own return-code predicates depend on null being visible, so this type keeps
/// three states apart rather than two:
/// </para>
/// <list type="bullet">
///   <item><description>
///     UNSET - <see cref="Kind"/> is <see cref="ExpressionVariableKind.None"/>. No variable value
///     at all, equivalent to an unset protobuf oneof.
///   </description></item>
///   <item><description>
///     SET AND NULL - <see cref="Kind"/> names a type and <see cref="HasValue"/> is
///     <see langword="false"/>. A variable of that type whose value is null.
///   </description></item>
///   <item><description>
///     SET AND PRESENT - <see cref="Kind"/> names a type and <see cref="HasValue"/> is
///     <see langword="true"/>. The payload is meaningful, INCLUDING when it equals the type's
///     default: <c>FromLong(0)</c> is not <c>FromLong(null)</c>, and <c>FromString("")</c> is not
///     <c>FromString(null)</c>. Never collapse null to zero - doing so converts "no value" into
///     "the value zero", which is the same class of silent defect as converting "neither succeeded
///     nor failed" into "succeeded".
///   </description></item>
/// </list>
/// <para>
/// A <see langword="readonly record struct"/> for three reasons: it allocates nothing on the path
/// the engine walks per variable per calculation; its synthesized value equality makes it directly
/// assertable in a table-driven parity theory; and immutability means a snapshot handed to a
/// consumer cannot be mutated behind the engine's back. Equality covers every field, payload
/// included, and every factory leaves the arms it does not set at their default, so two values of
/// the same kind and payload are always equal and two of different kinds never are.
/// </para>
/// <para>
/// NO RENDERING AND NO CONVERSION IS OFFERED HERE, DELIBERATELY (DECISION 3 in the file header).
/// There is no <c>ToExpressionLiteral</c>, no <c>ToString(IFormatProvider)</c> and no cross-type
/// getter. The DataWindow expression literal grammar is ported once, in
/// <c>Validators/ValueToExpression.cs</c>, and the <c>of_addvar</c> rendering at
/// [n_cst_dwsvc_columnexp.sru:L1091-L1109] belongs to the engine's <c>of_addvar</c> port.
/// </para>
/// </remarks>
public readonly record struct ExpressionVariableValue
{
    private readonly ExpressionVariableKind _kind;
    private readonly bool _hasValue;
    private readonly string? _stringValue;
    private readonly long _longValue;
    private readonly double _doubleValue;
    private readonly DateTime _dateTimeValue;
    private readonly DateOnly _dateValue;
    private readonly TimeOnly _timeValue;
    private readonly bool _booleanValue;

    /// <summary>
    /// The single private constructor. Each factory sets exactly one payload arm and leaves the
    /// other six at their default, which is what makes the synthesized field-wise equality
    /// well defined.
    /// </summary>
    private ExpressionVariableValue(
        ExpressionVariableKind kind,
        bool hasValue,
        string? stringValue = null,
        long longValue = 0L,
        double doubleValue = 0d,
        DateTime dateTimeValue = default,
        DateOnly dateValue = default,
        TimeOnly timeValue = default,
        bool booleanValue = false)
    {
        _kind = kind;
        _hasValue = hasValue;
        _stringValue = stringValue;
        _longValue = longValue;
        _doubleValue = doubleValue;
        _dateTimeValue = dateTimeValue;
        _dateValue = dateValue;
        _timeValue = timeValue;
        _booleanValue = booleanValue;
    }

    /// <summary>
    /// The unset value - no variant selected, no payload. Identical to
    /// <c>default(ExpressionVariableValue)</c>, and named so that the intent is legible at a call
    /// site.
    /// </summary>
    public static ExpressionVariableValue Unset => default;

    /// <summary>
    /// Which of the seven types this value carries, or <see cref="ExpressionVariableKind.None"/>
    /// when it is unset. Branch on this before reading a payload; no accessor performs a
    /// conversion, so reading the wrong one is a programming error rather than a coercion.
    /// </summary>
    public ExpressionVariableKind Kind => _kind;

    /// <summary>
    /// <see langword="true"/> when no variant is selected at all.
    /// </summary>
    public bool IsUnset => _kind == ExpressionVariableKind.None;

    /// <summary>
    /// <see langword="false"/> when this value is null - either because no variant is selected, or
    /// because the selected variant's value is the PowerBuilder null of that type. It is therefore
    /// <see langword="false"/> for <see cref="Unset"/> and for <c>FromLong(null)</c> alike;
    /// <see cref="IsUnset"/> separates those two.
    /// </summary>
    public bool HasValue => _hasValue;

    /// <summary>
    /// Creates a <c>time</c> variable value [:L153]. Pass <see langword="null"/> for the
    /// PowerBuilder null time; the null stays null and is never coerced to midnight.
    /// </summary>
    /// <param name="value">The value, or <see langword="null"/>.</param>
    /// <returns>A value whose <see cref="Kind"/> is <see cref="ExpressionVariableKind.TimeValue"/>.</returns>
    public static ExpressionVariableValue FromTime(TimeOnly? value) =>
        new(ExpressionVariableKind.TimeValue, value.HasValue, timeValue: value.GetValueOrDefault());

    /// <summary>
    /// Creates a <c>string</c> variable value [:L154]. Pass <see langword="null"/> for the
    /// PowerBuilder null string, which stays distinct from <see cref="string.Empty"/>.
    /// </summary>
    /// <param name="value">The value, or <see langword="null"/>.</param>
    /// <returns>A value whose <see cref="Kind"/> is <see cref="ExpressionVariableKind.StringValue"/>.</returns>
    /// <remarks>
    /// The empty string is a PRESENT value here, not an absent one. Whether an empty string should
    /// READ as null is a per-expression legacy setting - <c>columnexpdata.emptystringisnull</c>
    /// [:L33] - owned by the engine, so this factory does not apply it and must not.
    /// </remarks>
    public static ExpressionVariableValue FromString(string? value) =>
        new(ExpressionVariableKind.StringValue, value is not null, stringValue: value);

    /// <summary>
    /// Creates a <c>long</c> variable value [:L155]. Pass <see langword="null"/> for the
    /// PowerBuilder null long; the null stays null and is never coerced to zero.
    /// </summary>
    /// <param name="value">The value, or <see langword="null"/>.</param>
    /// <returns>A value whose <see cref="Kind"/> is <see cref="ExpressionVariableKind.LongValue"/>.</returns>
    public static ExpressionVariableValue FromLong(long? value) =>
        new(ExpressionVariableKind.LongValue, value.HasValue, longValue: value.GetValueOrDefault());

    /// <summary>
    /// Creates a <c>double</c> variable value [:L156]. Pass <see langword="null"/> for the
    /// PowerBuilder null double; the null stays null and is never coerced to zero.
    /// </summary>
    /// <param name="value">The value, or <see langword="null"/>.</param>
    /// <returns>A value whose <see cref="Kind"/> is <see cref="ExpressionVariableKind.DoubleValue"/>.</returns>
    /// <remarks>
    /// <c>double</c> AND NOT <c>decimal</c> - see <see cref="ExpressionVariableKind.DoubleValue"/>. The
    /// payload is stored and returned bit for bit: no rounding, no scale normalisation, and no
    /// special handling of NaN or the infinities, because the legacy applies none.
    /// </remarks>
    public static ExpressionVariableValue FromDouble(double? value) =>
        new(ExpressionVariableKind.DoubleValue, value.HasValue, doubleValue: value.GetValueOrDefault());

    /// <summary>
    /// Creates a <c>datetime</c> variable value [:L157]. Pass <see langword="null"/> for the
    /// PowerBuilder null datetime.
    /// </summary>
    /// <param name="value">The value, or <see langword="null"/>.</param>
    /// <returns>A value whose <see cref="Kind"/> is <see cref="ExpressionVariableKind.DateTimeValue"/>.</returns>
    /// <remarks>
    /// The <see cref="System.DateTime"/> is stored exactly as supplied, <see cref="DateTimeKind"/>
    /// included. The legacy <c>datetime</c> has no time-zone concept at all, so nothing here
    /// converts, shifts or re-kinds the value; forcing it to a single kind would discard
    /// information the caller supplied and the oracle cannot adjudicate.
    /// </remarks>
    public static ExpressionVariableValue FromDateTime(DateTime? value) =>
        new(ExpressionVariableKind.DateTimeValue, value.HasValue, dateTimeValue: value.GetValueOrDefault());

    /// <summary>
    /// Creates a <c>date</c> variable value [:L158]. Pass <see langword="null"/> for the
    /// PowerBuilder null date.
    /// </summary>
    /// <param name="value">The value, or <see langword="null"/>.</param>
    /// <returns>A value whose <see cref="Kind"/> is <see cref="ExpressionVariableKind.DateValue"/>.</returns>
    public static ExpressionVariableValue FromDate(DateOnly? value) =>
        new(ExpressionVariableKind.DateValue, value.HasValue, dateValue: value.GetValueOrDefault());

    /// <summary>
    /// Creates a <c>boolean</c> variable value [:L159]. Pass <see langword="null"/> for the
    /// PowerBuilder null boolean, which is a real third state and is never coerced to
    /// <see langword="false"/>.
    /// </summary>
    /// <param name="value">The value, or <see langword="null"/>.</param>
    /// <returns>A value whose <see cref="Kind"/> is <see cref="ExpressionVariableKind.BooleanValue"/>.</returns>
    public static ExpressionVariableValue FromBoolean(bool? value) =>
        new(ExpressionVariableKind.BooleanValue, value.HasValue, booleanValue: value.GetValueOrDefault());

    /// <summary>
    /// Reads the <c>time</c> payload.
    /// </summary>
    /// <returns>
    /// The value, or <see langword="null"/> when this is a time variable whose value is null.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Kind"/> is not <see cref="ExpressionVariableKind.TimeValue"/>. The mismatch fails
    /// loudly rather than returning a default, because a silent default is indistinguishable from a
    /// legitimate value and would corrupt a calculation without reporting anything.
    /// </exception>
    public TimeOnly? AsTime() =>
        _kind == ExpressionVariableKind.TimeValue
            ? (_hasValue ? _timeValue : null)
            : throw KindMismatch(ExpressionVariableKind.TimeValue);

    /// <summary>
    /// Reads the <c>string</c> payload.
    /// </summary>
    /// <returns>
    /// The value, or <see langword="null"/> when this is a string variable whose value is null. The
    /// empty string is returned as the empty string and never as <see langword="null"/>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Kind"/> is not <see cref="ExpressionVariableKind.StringValue"/>.
    /// </exception>
    public string? AsString() =>
        _kind == ExpressionVariableKind.StringValue
            ? (_hasValue ? _stringValue : null)
            : throw KindMismatch(ExpressionVariableKind.StringValue);

    /// <summary>
    /// Reads the <c>long</c> payload.
    /// </summary>
    /// <returns>
    /// The value, or <see langword="null"/> when this is a long variable whose value is null.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Kind"/> is not <see cref="ExpressionVariableKind.LongValue"/>.
    /// </exception>
    public long? AsLong() =>
        _kind == ExpressionVariableKind.LongValue
            ? (_hasValue ? _longValue : null)
            : throw KindMismatch(ExpressionVariableKind.LongValue);

    /// <summary>
    /// Reads the <c>double</c> payload.
    /// </summary>
    /// <returns>
    /// The value, or <see langword="null"/> when this is a double variable whose value is null.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Kind"/> is not <see cref="ExpressionVariableKind.DoubleValue"/>. In particular a
    /// <see cref="ExpressionVariableKind.LongValue"/> value is NOT silently widened to a double here:
    /// widening is a coercion, coercion is the engine's, and doing it in an accessor would hide
    /// which type the caller actually declared.
    /// </exception>
    public double? AsDouble() =>
        _kind == ExpressionVariableKind.DoubleValue
            ? (_hasValue ? _doubleValue : null)
            : throw KindMismatch(ExpressionVariableKind.DoubleValue);

    /// <summary>
    /// Reads the <c>datetime</c> payload.
    /// </summary>
    /// <returns>
    /// The value, or <see langword="null"/> when this is a datetime variable whose value is null.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Kind"/> is not <see cref="ExpressionVariableKind.DateTimeValue"/>.
    /// </exception>
    public DateTime? AsDateTime() =>
        _kind == ExpressionVariableKind.DateTimeValue
            ? (_hasValue ? _dateTimeValue : null)
            : throw KindMismatch(ExpressionVariableKind.DateTimeValue);

    /// <summary>
    /// Reads the <c>date</c> payload.
    /// </summary>
    /// <returns>
    /// The value, or <see langword="null"/> when this is a date variable whose value is null.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Kind"/> is not <see cref="ExpressionVariableKind.DateValue"/>.
    /// </exception>
    public DateOnly? AsDate() =>
        _kind == ExpressionVariableKind.DateValue
            ? (_hasValue ? _dateValue : null)
            : throw KindMismatch(ExpressionVariableKind.DateValue);

    /// <summary>
    /// Reads the <c>boolean</c> payload.
    /// </summary>
    /// <returns>
    /// The value, or <see langword="null"/> when this is a boolean variable whose value is null.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Kind"/> is not <see cref="ExpressionVariableKind.BooleanValue"/>.
    /// </exception>
    public bool? AsBoolean() =>
        _kind == ExpressionVariableKind.BooleanValue
            ? (_hasValue ? _booleanValue : null)
            : throw KindMismatch(ExpressionVariableKind.BooleanValue);

    /// <summary>
    /// Builds the exception a mismatched typed read throws. Names both the kind actually carried and
    /// the kind requested, because the two together are what a caller needs in order to fix the
    /// branch that got it wrong.
    /// </summary>
    private InvalidOperationException KindMismatch(ExpressionVariableKind requested) =>
        new($"This expression variable value carries {_kind}, not {requested}. "
            + "The seven variants are a closed set: branch on Kind before reading a payload. "
            + "No accessor coerces between variants, because the legacy coercion is dispatched by "
            + "the engine on the declared type and a silent conversion here would hide it.");
}


// ==============================================================================================
//  THE FOUR STRUCTURE PORTS, WITH THEIR FIELD COUNTS AS AN AUDIT
//  --------------------------------------------------------------------------------------------
//  Each record below carries EXACTLY the fields its legacy structure declares, in the legacy's own
//  declaration order, so the constructor parameter list can be diffed against the .sru field by
//  field. The counts are the audit that nothing was added or dropped:
//
//      vardata         [:L50-L56]   5 fields   ->  VariableReference
//      funcdata        [:L58-L63]   4 fields   ->  FunctionReference
//      localvardata    [:L73-L78]   4 fields   ->  LocalVariableDefinition   (RECURSIVE)
//      foreignvardata  [:L80-L83]   2 fields   ->  ForeignVariableReference
//      globalvardata   [:L65-L71]   5 fields   ->  GlobalVariable
//
//  The remaining two structures of the legacy object's seven - columnexpdata [:L22-L42, 19 fields]
//  and columndata [:L44-L48, 3 fields] - are NOT ported here. They describe a COLUMN's binding
//  rather than a VARIABLE's, and they belong to ColumnExpressionEngine.
//
//  WHY THE NAMES DIFFER FROM BOTH THE LEGACY AND THE WIRE, WHICH IS DELIBERATE (C-K)
//  The published contract generates C# classes named VarData, FuncData, GlobalVarData, LocalVarData
//  and ForeignVarRef into PowerFramework.Contracts.DataServices.V1. The gRPC service that maps
//  domain to wire imports BOTH namespaces, so a domain type sharing a generated type's name would
//  make every mention of it CS0104 "ambiguous reference" - and because warnings are errors in this
//  repository, that is a build failure rather than a nuisance. Distinct descriptive names are the
//  cheap fix, and each record below names the legacy structure and the wire message it corresponds
//  to so the three-way mapping stays greppable.
//
//  WHY EVERY PARAMETER HAS A DEFAULT
//  PowerScript has no constructors: `VARDATA varData` declares a zeroed structure whose numeric
//  members are 0, whose booleans are false and whose string members are the empty string, and the
//  parser then fills selected members in [:L1407-L1414]. Defaulting every parameter reproduces that
//  starting point exactly, so `new VariableReference()` is the faithful analogue of the legacy
//  declaration and a partially-populated structure needs no ceremony.
//
//  WHY COLLECTIONS ARE ImmutableArray<T>
//  A PowerBuilder array is never null: an unassigned array has an UpperBound of 0, which is an
//  EMPTY array rather than a missing one. ImmutableArray<T> has an uninitialised default state that
//  throws on enumeration, so each collection member normalises that default to Empty in its `init`
//  accessor. That is CLR hygiene reproducing PowerBuilder array semantics, not added validation:
//  no element is inspected, reordered, deduplicated or rejected.
// ==============================================================================================

/// <summary>
/// One variable REFERENCE inside an expression - the port of <c>vardata</c>
/// [ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru:L50-L56], five fields, and
/// the wire counterpart of <c>dataservices.v1.VarData</c>.
/// </summary>
/// <param name="Name">
/// <c>string name</c> [:L51] - the variable's logical name with EVERY SIGIL STRIPPED. The parser
/// takes it from offset +2 past a doubled sigil [:L1407] or +1 past a single one [:L1409], and
/// trims it.
/// </param>
/// <param name="FullName">
/// <c>string fullname</c> [:L52] - THE FULL MATCHED TOKEN, SIGILS INCLUDED, and the substitution
/// key. See the remarks: this is not a decorated copy of <paramref name="Name"/>.
/// </param>
/// <param name="Index">
/// <c>integer index</c> [:L53] - the ONE-BASED index into the global variable table
/// (<see cref="ExpressionVariableEnvironment"/>). ZERO MEANS "NOT YET BOUND", AND IT IS A REQUEST
/// RATHER THAN AN ERROR: the legacy resolves a zero index by name at calculation time and caches the
/// answer back into the array [:L2189-L2195]. A consumer must therefore not treat 0 as malformed.
/// The legacy declares this <c>integer</c>, PowerBuilder's 16-bit type; it is widened to
/// <see cref="int"/> here to match the contract's <c>int32</c>, a widening that cannot lose a value.
/// </param>
/// <param name="IsCtx">
/// <c>boolean isctx</c> [:L54] - the reference is CONTEXT-SCOPED, set by the <c>@</c> sigil
/// [:L1296, assigned at :L1413]. See <see cref="ExpansionSigils"/> for what that means mechanically
/// and for the restriction that travels with it.
/// </param>
/// <param name="IsMacro">
/// <c>boolean ismacro</c> [:L55] - the reference is a MACRO rather than a plain value, set by the
/// <c>$</c> sigil [:L1289, :L1297, assigned at :L1414].
/// </param>
/// <remarks>
/// <para>
/// <paramref name="FullName"/> IS LOAD-BEARING AND MUST NOT BE "TIDIED". It is the exact substring
/// the parser matched - <c>Mid(sExp, nMacPos, nPos - nMacPos)</c>, right-trimmed [:L1412] - so it
/// carries the <c>$</c>, <c>$$</c>, <c>@</c>, <c>@$</c> or <c>@$$</c> prefix verbatim. It is also
/// the REPLACEMENT KEY: static expansion substitutes a reference by replacing this exact string out
/// of the expression [:L1435], and preprocessing does the same at calculation time [:L2214]. Both
/// call sites use the five-argument keyword form of the framework's <c>ReplaceAll</c>, whose
/// whole-token semantics are what stop a short variable name matching inside a longer one. A
/// consumer that regenerated this string from <paramref name="Name"/> plus a sigil would break
/// substitution for any token the parser trimmed differently.
/// </para>
/// <para>
/// WHOLE-STRUCTURE EQUALITY IS A REQUIREMENT, NOT A CONVENIENCE. The static-expansion merge step
/// deduplicates references by comparing entire structures - <c>if varDatas[nIndex] =
/// GlobalVars[nVarIdx].local.vars[nGVarIdx] then exit</c> [:L1442] - not by comparing names. Every
/// member here is a scalar, so the synthesized record equality IS that comparison, field for field.
/// Two references that differ only in <paramref name="Index"/> are correctly unequal, which matters
/// because 0 and a resolved index are different binding states.
/// </para>
/// </remarks>
public sealed record VariableReference(
    string Name = "",
    string FullName = "",
    int Index = 0,
    bool IsCtx = false,
    bool IsMacro = false);

/// <summary>
/// One macro-FUNCTION reference inside an expression - the port of <c>funcdata</c>
/// [n_cst_dwsvc_columnexp.sru:L58-L63], four fields, and the wire counterpart of
/// <c>dataservices.v1.FuncData</c>.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS TYPE IS DECLARED IN THIS FILE. <c>localvardata.fns[]</c> [:L76] is an array of
/// <c>funcdata</c>, so <see cref="LocalVariableDefinition"/> cannot be expressed without it, and
/// this file is the folder's foundational type module with no intra-folder dependency available to
/// borrow it from. It is declared here once and consumed by the engine rather than redeclared
/// there.
/// </para>
/// <para>
/// <see cref="Builtin"/> IS MISNAMED IN THE ORACLE AND THE MEANING IS PRESERVED VERBATIM (C-B). It
/// does not mean "a DataWindow built-in function". It is assigned straight from the doubled-sigil
/// test - <c>fnData.builtIn = bDD</c> [:L1311] - so it means "WRITTEN WITH <c>$$</c>", the dynamic
/// macro form, and it is what routes dispatch to the <c>Invoke</c> sentinel branch instead of the
/// direct branch. The field is neither renamed nor reinterpreted, and this remark exists so the
/// meaning is available without deducing it from the parser.
/// </para>
/// </remarks>
public sealed record FunctionReference
{
    private readonly ImmutableArray<string> _args = ImmutableArray<string>.Empty;

    /// <summary>
    /// <c>string name</c> [:L59] - the function name with sigils stripped. The parser takes it from
    /// offset +2 for the dynamic form [:L1313] and +1 for the direct form [:L1315], and trims it.
    /// </summary>
    /// <remarks>
    /// THE EMPTY STRING IS MEANINGFUL HERE AND NOT MISSING. An empty function name is the
    /// <c>FUNC_VAR</c> sentinel [:L120], which models a bare variable lookup written <c>$$(...)</c>.
    /// The sentinel's VALUE belongs to the engine, which declares it; this record only carries
    /// whatever the parser produced.
    /// </remarks>
    public string Name { get; init; } = "";

    /// <summary>
    /// <c>string fullname</c> [:L60] - the full matched token including the sigils and the whole
    /// argument list, taken as <c>Mid(sExp, nMacPos, nFnPos - nMacPos + 1)</c> [:L1350]. It is the
    /// substitution key for the function form, exactly as <see cref="VariableReference.FullName"/>
    /// is for the variable form.
    /// </summary>
    public string FullName { get; init; } = "";

    /// <summary>
    /// <c>string args[]</c> [:L61] - THE ARGUMENT EXPRESSIONS AS TEXT, one per argument, in source
    /// order. ZERO-BASED here against ONE-BASED at source.
    /// </summary>
    /// <remarks>
    /// THEY ARE EXPRESSIONS, NOT VALUES. The parser extracts them by bracket counting with quote
    /// tracking [:L1319-L1350], so an argument may itself contain a variable reference, a nested
    /// call or a quoted literal - the legacy specification's <c>$FormatPrice(n2, $精度)</c>
    /// [docs/n_cst_dwsvc_columnexp.md:L117] passes a statically expanded variable as an argument,
    /// which is the per-reference-mode point made from inside an argument list. Nothing here parses,
    /// trims further, escapes or evaluates them.
    /// </remarks>
    public ImmutableArray<string> Args
    {
        get => _args;
        init => _args = value.IsDefault ? ImmutableArray<string>.Empty : value;
    }

    /// <summary>
    /// <c>boolean builtin</c> [:L62] - "written with <c>$$</c>", i.e. the dynamic macro form. See
    /// the type remarks: the name is misleading and is preserved unchanged.
    /// </summary>
    public bool Builtin { get; init; }

    /// <summary>
    /// Compares two references field for field, with <see cref="Args"/> compared ELEMENT BY ELEMENT.
    /// </summary>
    /// <param name="other">The reference to compare with, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when every field is equal.</returns>
    /// <remarks>
    /// Hand-written because <see cref="ImmutableArray{T}"/> equality compares the UNDERLYING ARRAY
    /// REFERENCE, so the synthesized record equality would report two structurally identical
    /// references as different. The legacy deduplication that reaches this type compares
    /// <c>fullName</c> [:L1452], and structural equality is the stronger, safer relation to publish.
    /// </remarks>
    public bool Equals(FunctionReference? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null
            && Name == other.Name
            && FullName == other.FullName
            && Builtin == other.Builtin
            && _args.AsSpan().SequenceEqual(other._args.AsSpan());
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(Name);
        hash.Add(FullName);
        hash.Add(Builtin);
        hash.Add(_args.Length);
        foreach (string arg in _args)
        {
            hash.Add(arg);
        }

        return hash.ToHashCode();
    }
}


/// <summary>
/// A LOCAL variable's definition - the port of <c>localvardata</c>
/// [n_cst_dwsvc_columnexp.sru:L73-L78], four fields, and the wire counterpart of
/// <c>dataservices.v1.LocalVarData</c>.
/// </summary>
/// <remarks>
/// <para>
/// IT IS RECURSIVE, AND THE RECURSION IS MODELLED EXPLICITLY. A variable's value MAY ITSELF BE AN
/// EXPRESSION carrying its own variable and function references - that is what <c>of_addvarexp</c>
/// creates [docs/n_cst_dwsvc_columnexp.md:L23-L27] - and those references may in turn name further
/// expression variables. That is why <see cref="Vars"/> and <see cref="Fns"/> appear HERE as well as
/// on the column-level structure, and it is not defensive nesting: the legacy walks the chain
/// through <c>GlobalVars[nVarIdx].local.vars[nGVarIdx]</c> during static expansion [:L1439-L1448]
/// and again through <c>GlobalVars[nGVarIdx].local</c> during preprocessing [:L2202-L2206], so the
/// depth is genuinely unbounded and a flat model would truncate it.
/// </para>
/// <para>
/// <see cref="Exp"/> IS THE UNEXPANDED SOURCE TEXT. Contract C-04 requires that the payload transmit
/// the unexpanded expression, the bind-time snapshot AND the live environment
/// [dataservices.v1.proto:L2984-L3000], because an already-expanded string makes a static binding
/// indistinguishable from a literal and strips a dynamic binding of its resolution environment. This
/// field is the first of those three.
/// </para>
/// <para>
/// FOUR FIELDS, AND THE TYPED VALUE IS NOT ONE OF THEM. A value variable's value lives INSIDE
/// <see cref="Exp"/> as a rendered literal, because every typed <c>of_addvar</c> overload renders and
/// delegates to <c>of_addvarexp</c> [:L1091-L1109]. The typed
/// <see cref="ExpressionVariableValue"/> is carried BESIDE this record - see DECISION 2 in the file
/// header - which keeps the field count legacy-exact while still letting the type survive the
/// boundary.
/// </para>
/// <para>
/// WHAT IS DELIBERATELY ABSENT. <see cref="HasMacro"/> is carried, never derived: the legacy computes
/// it once, as <c>(UpperBound(vars) &gt; 0 or UpperBound(fns) &gt; 0)</c> inside <c>of_addvarexp</c>
/// [:L1623], and deriving it here would move a decision out of the routine that owns it and would
/// silently disagree with any recorded value the oracle produced. Likewise absent are
/// <c>of_addvarexp</c>'s empty-name and empty-expression rejections and its word-delimiter scan
/// [:L1605-L1617], and its duplicate-name rejection [:L1606-L1609]: all four are engine behaviour,
/// and reproducing them in a value type would be added validation (C-B).
/// </para>
/// </remarks>
public sealed record LocalVariableDefinition
{
    private readonly ImmutableArray<VariableReference> _vars = ImmutableArray<VariableReference>.Empty;
    private readonly ImmutableArray<FunctionReference> _fns = ImmutableArray<FunctionReference>.Empty;

    /// <summary>
    /// <c>string exp</c> [:L74] - the variable's DataWindow expression text, UNEXPANDED. For a value
    /// variable this is the literal the matching <c>of_addvar</c> overload rendered
    /// [:L1091-L1109]; for an expression variable it is whatever the caller supplied to
    /// <c>of_addvarexp</c>, after the parser rewrote its static references in place.
    /// </summary>
    public string Exp { get; init; } = "";

    /// <summary>
    /// <c>vardata vars[]</c> [:L75] - the variable references this variable's own expression makes,
    /// as the parser emitted them. Empty when <see cref="HasMacro"/> is <see langword="false"/>.
    /// </summary>
    public ImmutableArray<VariableReference> Vars
    {
        get => _vars;
        init => _vars = value.IsDefault ? ImmutableArray<VariableReference>.Empty : value;
    }

    /// <summary>
    /// <c>funcdata fns[]</c> [:L76] - the macro-function references this variable's own expression
    /// makes, as the parser emitted them.
    /// </summary>
    public ImmutableArray<FunctionReference> Fns
    {
        get => _fns;
        init => _fns = value.IsDefault ? ImmutableArray<FunctionReference>.Empty : value;
    }

    /// <summary>
    /// <c>boolean hasmacro</c> [:L77] - whether the expression contains any macro reference at all.
    /// Carried, not derived; see the type remarks. It is the flag the preprocessing path branches on
    /// when deciding whether a variable's expression needs a recursive pass [:L2202].
    /// </summary>
    public bool HasMacro { get; init; }

    /// <summary>
    /// Compares two definitions field for field, with both reference arrays compared ELEMENT BY
    /// ELEMENT.
    /// </summary>
    /// <param name="other">The definition to compare with, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when every field is equal.</returns>
    /// <remarks>
    /// Hand-written for the reason given on <see cref="FunctionReference.Equals(FunctionReference)"/>:
    /// <see cref="ImmutableArray{T}"/> equality is reference-based, so the synthesized record
    /// equality would report two structurally identical definitions as different. Element comparison
    /// delegates to the element types' own equality, which is exactly the whole-structure comparison
    /// the legacy deduplication performs on <c>vardata</c> [:L1442].
    /// </remarks>
    public bool Equals(LocalVariableDefinition? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null
            && Exp == other.Exp
            && HasMacro == other.HasMacro
            && _vars.AsSpan().SequenceEqual(other._vars.AsSpan())
            && _fns.AsSpan().SequenceEqual(other._fns.AsSpan());
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(Exp);
        hash.Add(HasMacro);
        hash.Add(_vars.Length);
        foreach (VariableReference variable in _vars)
        {
            hash.Add(variable);
        }

        hash.Add(_fns.Length);
        foreach (FunctionReference function in _fns)
        {
            hash.Add(function);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// A reference to a variable that lives in ANOTHER DataWindow's expression service - the port of
/// <c>foreignvardata</c> [n_cst_dwsvc_columnexp.sru:L80-L83], two fields, and the wire counterpart of
/// <c>dataservices.v1.ForeignVarRef</c>.
/// </summary>
/// <param name="Index">
/// <c>integer index</c> [:L81] - the ONE-BASED index of the variable WITHIN THE FOREIGN SERVICE's
/// table, not within this one. Widened from PowerBuilder's 16-bit <c>integer</c> to
/// <see cref="int"/> to match the contract's <c>int32</c>.
/// </param>
/// <param name="Handle">
/// THE REPLACEMENT FOR <c>expsvc</c> [:L82]: an OPAQUE, SESSION-SCOPED HANDLE naming the foreign
/// DataWindow. A string value and nothing more - never a network address, never a URL, never a
/// process identifier, and never an object reference. Making it any of those would recreate the
/// cross-instance reference the narrowing exists to forbid.
/// </param>
/// <remarks>
/// <para>
/// THIS IS THE ONE HARD LIMIT OF THE WHOLE EXPRESSION CONTRACT, AND IT IS WHY THIS RECORD HAS TWO
/// FIELDS RATHER THAN A POINTER. The legacy field is declared <c>n_cst_dwsvc_columnexp expsvc</c>
/// [:L82] - typed as another LIVE INSTANCE of the same service class, belonging to another
/// DataWindow. It is not a handle, not a name and not an identifier: it is a pointer, and it is
/// dereferenced as one, synchronously, into a PRIVATE method of the other instance, at two
/// independent sites - the preprocessing path
/// <c>GlobalVars[nGVarIdx].foreign.expSvc._of_CalcVarExpValue(...)</c> [:L2199-L2200] and the
/// variable-value path [:L2384-L2385]. An in-process pointer cannot be serialized, so the contract is
/// narrowed with a defined error rather than widened with a guess.
/// </para>
/// <para>
/// THE RESOLUTION, WHICH THIS RECORD ONLY CARRIES AND DOES NOT ENFORCE. A cross-DataWindow variable
/// resolves through a session-scoped handle owned by the expression session, and is supported ONLY
/// when both DataWindows are co-resident in the same expression session inside one DataServices
/// instance. A reference spanning sessions or service instances is BLOCKED and returns a defined
/// error naming the unreachable handle - never a substituted value, a default, an empty string or a
/// stale reading. Whether a given handle is currently resolvable is SESSION STATE, not structure, so
/// it is <c>ExpressionSession</c>'s to answer and the contract carries it as a separate wire field;
/// this record deliberately does not duplicate it, which is also what keeps the field count at the
/// legacy's two.
/// </para>
/// <para>
/// A FOREIGN VARIABLE REQUIRES DYNAMIC EXPANSION, AND THAT IS THE LEGACY'S OWN RULE, NOT A
/// CONSEQUENCE OF THE NARROWING. The specification says so where it introduces the feature
/// [docs/n_cst_dwsvc_columnexp.md:L34] and the parser enforces it: attempting to expand a
/// <c>VAR_FOREIGN</c> variable statically is rejected outright [:L1425-L1428]. The rejection belongs
/// to the engine, which is the only party that can look a name up in the table.
/// </para>
/// <para>
/// <c>globalvardata.links[]</c> [:L70] IS AN ARRAY OF THIS SAME STRUCTURE, so everything above applies
/// to every element of it and not only to the singular <c>foreign</c> member. That is easy to miss,
/// so <see cref="GlobalVariable.Links"/> restates it.
/// </para>
/// </remarks>
public sealed record ForeignVariableReference(
    int Index = 0,
    string Handle = "");

/// <summary>
/// One entry in the global variable table - the port of <c>globalvardata</c>
/// [n_cst_dwsvc_columnexp.sru:L65-L71], five fields, and the wire counterpart of
/// <c>dataservices.v1.GlobalVarData</c>.
/// </summary>
/// <remarks>
/// <para>
/// THE DISCRIMINATOR IS <see cref="VarType"/> AND IT IS A <see cref="long"/>, BECAUSE THE LEGACY
/// DECLARES <c>long vartype</c> [:L67]. Its two defined values are
/// <see cref="ExpressionVariableEnvironment.VAR_LOCAL"/> and
/// <see cref="ExpressionVariableEnvironment.VAR_FOREIGN"/>, whose spellings and values are preserved
/// verbatim from [:L117-L118]. It is deliberately NOT narrowed to an enum: the legacy stores and
/// compares a number, no legacy path validates it, and narrowing would add a rejection the oracle
/// does not perform (C-B).
/// </para>
/// <para>
/// BOTH PAYLOAD MEMBERS ARE ALWAYS PRESENT, WHICH IS FAITHFUL RATHER THAN WASTEFUL. In PowerScript a
/// nested structure member is not a reference that can be absent - <c>globalvardata</c> physically
/// contains a zeroed <c>localvardata</c> and a zeroed <c>foreignvardata</c> at all times, and
/// <c>vartype</c> is the only thing that says which one to read. The legacy relies on exactly that:
/// it tests <c>varType = VAR_FOREIGN</c> and then reads <c>foreign</c> [:L2199], otherwise reads
/// <c>local</c> [:L2202-L2206]. Modelling the unused member as null would invent a state the oracle
/// has no representation for.
/// </para>
/// </remarks>
public sealed record GlobalVariable
{
    private readonly ImmutableArray<ForeignVariableReference> _links =
        ImmutableArray<ForeignVariableReference>.Empty;

    /// <summary>
    /// <c>string name</c> [:L66] - the variable's name as declared, without sigils. Lookup against it
    /// is CASE-SENSITIVE; see <see cref="ExpressionVariableEnvironment.IndexOf(string)"/>.
    /// </summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// <c>long vartype</c> [:L67] - <see cref="ExpressionVariableEnvironment.VAR_LOCAL"/> or
    /// <see cref="ExpressionVariableEnvironment.VAR_FOREIGN"/>. See the type remarks for why this is a
    /// <see cref="long"/> and not an enum.
    /// </summary>
    public long VarType { get; init; } = ExpressionVariableEnvironment.VAR_LOCAL;

    /// <summary>
    /// <c>localvardata local</c> [:L68] - meaningful when <see cref="VarType"/> is
    /// <see cref="ExpressionVariableEnvironment.VAR_LOCAL"/>. Never <see langword="null"/>; see the
    /// type remarks.
    /// </summary>
    public LocalVariableDefinition Local { get; init; } = new();

    /// <summary>
    /// <c>foreignvardata foreign</c> [:L69] - meaningful when <see cref="VarType"/> is
    /// <see cref="ExpressionVariableEnvironment.VAR_FOREIGN"/>. Never <see langword="null"/>. This is
    /// the member that cannot cross the boundary intact; see
    /// <see cref="ForeignVariableReference"/>.
    /// </summary>
    public ForeignVariableReference Foreign { get; init; } = new();

    /// <summary>
    /// <c>foreignvardata links[]</c> [:L70] - the additional foreign references attached to this
    /// variable.
    /// </summary>
    /// <remarks>
    /// A SECOND POINTER-BEARING FIELD, AND IT IS EASY TO MISS. Every element is a
    /// <see cref="ForeignVariableReference"/>, so the whole of that type's narrowing applies to each
    /// one of them and not only to <see cref="Foreign"/>.
    /// </remarks>
    public ImmutableArray<ForeignVariableReference> Links
    {
        get => _links;
        init => _links = value.IsDefault ? ImmutableArray<ForeignVariableReference>.Empty : value;
    }

    /// <summary>
    /// Compares two entries field for field, with <see cref="Links"/> compared ELEMENT BY ELEMENT.
    /// </summary>
    /// <param name="other">The entry to compare with, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when every field is equal.</returns>
    /// <remarks>
    /// Hand-written because <see cref="ImmutableArray{T}"/> equality is reference-based. Note that
    /// BOTH payload members participate regardless of <see cref="VarType"/>: the legacy structure
    /// physically carries both, so two entries that differ only in the member their
    /// <see cref="VarType"/> does not select are genuinely different structures, and a characterization
    /// recording of one would not match the other.
    /// </remarks>
    public bool Equals(GlobalVariable? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null
            && Name == other.Name
            && VarType == other.VarType
            && Local == other.Local
            && Foreign == other.Foreign
            && _links.AsSpan().SequenceEqual(other._links.AsSpan());
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(Name);
        hash.Add(VarType);
        hash.Add(Local);
        hash.Add(Foreign);
        hash.Add(_links.Length);
        foreach (ForeignVariableReference link in _links)
        {
            hash.Add(link);
        }

        return hash.ToHashCode();
    }
}


// ==============================================================================================
//  THE PER-REFERENCE EXPANSION-MODE DESCRIPTOR                                      DECISION 5
//  --------------------------------------------------------------------------------------------
//  THE PARSER SETS THREE ORTHOGONAL BITS, NOT ONE ENUM. This is the single most easily lost
//  discovery in the whole port, it is stated in no specification and in no brief, and it is read
//  directly off the parser's two sigil arms [:L1284-L1300]:
//
//      case MACRO_FLAG "$"      [:L1284-L1291]
//          bIsCtx   = false
//          bIsMacro = true
//          bDD      = (the next character is another "$")            [:L1290]
//
//      case MACRO_CONTEXT "@"   [:L1292-L1300], declared MACRO_CONTEXT = "@" at [:L1253]
//          bIsCtx   = true
//          bIsMacro = (the next character is "$")                    [:L1297]
//          bDD      = (the character after THAT is "$")              [:L1299]
//
//  and the first two are copied onto the reference verbatim - varData.isCtx = bIsCtx [:L1413],
//  varData.isMacro = bIsMacro [:L1414] - while bDD selects static against dynamic for a variable
//  [:L1406-L1410, :L1415] and becomes funcdata.builtin for a function [:L1311].
//
//  THE "@" CONTEXT SIGIL IS DOCUMENTED NOWHERE. It is absent from
//  docs/n_cst_dwsvc_columnexp.md, which describes only "$" and "$$", and it is named in no brief.
//  It is nonetheless real, it is the mechanism behind vardata.isctx, and it resolves through the
//  context triple at [:L2180-L2186]: with isMacro it calls the CONTEXT service's variable
//  calculator [:L2182], and without isMacro it reads a COLUMN VALUE from the context row [:L2185].
//  It is modelled as its own bit and never folded into the mode, because it selects the RESOLUTION
//  SCOPE while the mode selects the EXPANSION TIMING, and the two are independent - the contract
//  makes the same point where it declines to add a sixth mode
//  [shared/PowerFramework.Contracts/Proto/dataservices.v1.proto:L2806-L2807].
//
//  SIX REACHABLE FORMS OUT OF EIGHT BIT PATTERNS, WITH THE PROOF
//  A dynamic bit cannot occur without a macro bit, and that is provable from the source rather than
//  assumed. bDD is assigned in exactly two places, both inside a sigil arm. In the "$" arm bIsMacro
//  is unconditionally true [:L1289], so (macro=false, dynamic=true) cannot arise there. In the "@"
//  arm, when bIsMacro is false nPos is NOT advanced [:L1298], so the bDD test at [:L1299] inspects
//  THE SAME CHARACTER bIsMacro already tested at [:L1297] and therefore yields the same answer -
//  bDD == bIsMacro. So (ctx=true, macro=false, dynamic=true) cannot arise either. Both patterns are
//  unreachable by construction; the projection still answers for them, because a total function is
//  easier to reason about than a partial one, and each is annotated where it is handled.
//
//  THE MODE IS PER-REFERENCE, NEVER PER-EXPRESSION, AND THE ORACLE PROVES IT TWICE.
//  "$$上月读数 + $本月读数" [docs/n_cst_dwsvc_columnexp.md:L68] mixes a dynamic and a static
//  reference inside ONE expression, and of_SetExp("n1", "$$bb + $aa") [:L160] does the same in pure
//  ASCII. It reaches inside argument lists too: $FormatPrice(n2, $精度) [:L117] passes a
//  statically expanded variable to a macro-direct call. A model that tagged the expression rather
//  than each reference could not represent any of those three lines.
// ==============================================================================================

/// <summary>
/// Which legacy dispatch arm a macro-FUNCTION reference selects, as classified by the engine from
/// the function's name and the doubled-sigil flag.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS AS AN INPUT RATHER THAN BEING DERIVED HERE. Two of contract C-04's five expansion
/// modes are function modes, and telling them apart requires the function NAME, which is matched
/// against two reserved sentinels - <c>FUNC_VAR</c> and <c>FUNC_INVOKE</c>
/// [n_cst_dwsvc_columnexp.sru:L120-L121], checked at [:L1386-L1401] and dispatched at [:L2237-L2287].
/// The sentinels' VALUES belong to the engine, which declares them, so this file takes the
/// classification as a parameter instead of duplicating the two string literals. Duplicating them
/// would create a second definition of the grammar that cannot be kept in agreement forever.
/// </para>
/// <para>
/// WHAT IS DELIBERATELY NOT A MEMBER. A <c>$$</c> call whose name matches NEITHER sentinel is a parse
/// error - "function undefined" [:L1398-L1400] - and it is not represented here, because rejecting an
/// unknown name is name validation and therefore the engine's (C-B). The engine classifies first and
/// projects second.
/// </para>
/// </remarks>
public enum MacroFunctionDispatch
{
    /// <summary>
    /// The DIRECT form, <c>$FunctionName(args)</c>: <c>funcdata.builtin</c> is
    /// <see langword="false"/> and the implementation belongs to the APPLICATION, which is called back
    /// across the boundary [dispatch at :L2286-L2287; specification at
    /// docs/n_cst_dwsvc_columnexp.md:L108-L128].
    /// </summary>
    Application = 0,

    /// <summary>
    /// The indirect VARIABLE LOOKUP form, <c>$$('nameString')</c>: <c>funcdata.builtin</c> is
    /// <see langword="true"/> and the name is the empty <c>FUNC_VAR</c> sentinel [:L1388, dispatched
    /// at :L2239]. The variable name is itself computed at run time and may be a DataWindow expression
    /// [docs/n_cst_dwsvc_columnexp.md:L75-L94]. Exactly one argument is required [:L1389-L1392].
    /// </summary>
    VariableLookup = 1,

    /// <summary>
    /// The DYNAMIC INVOKE form, <c>$$Invoke('FunctionName', args)</c>: <c>funcdata.builtin</c> is
    /// <see langword="true"/> and the name is the <c>FUNC_INVOKE</c> sentinel [:L1393, dispatched at
    /// :L2258]. The function name is itself a variable [docs/n_cst_dwsvc_columnexp.md:L130-L151]. At
    /// least one argument is required [:L1394-L1397].
    /// </summary>
    DynamicInvoke = 2,
}

/// <summary>
/// The reason the legacy parser REFUSES a sigil combination outright, preserved as a refusal rather
/// than worked around (C-B).
/// </summary>
/// <remarks>
/// Both members are restrictions the oracle enforces with its own error message and an
/// <c>E_INVALID_ARGUMENT</c> return, not gaps in this port. They are surfaced as data so the engine can
/// report them through <c>ParseErrorFormatter</c> - which owns the message text, the caret position and
/// the preserved fact that these particular messages are hardcoded and do NOT route through
/// localization - instead of rediscovering them.
/// </remarks>
public enum ExpansionRejection
{
    /// <summary>
    /// Not rejected.
    /// </summary>
    None = 0,

    /// <summary>
    /// <c>@$name</c> - A CONTEXT-SCOPED VARIABLE CANNOT BE EXPANDED STATICALLY. The parser reaches
    /// the static branch with the context bit set and refuses [:L1415-L1419]; the reason is inherent
    /// rather than incidental, because a static substitution is performed once at bind time and there
    /// is no calling context to resolve against at bind time.
    /// </summary>
    ContextStaticExpansion = 1,

    /// <summary>
    /// <c>@name(</c> - A CONTEXT-SCOPED FUNCTION MACRO IS UNSUPPORTED. On meeting an opening bracket
    /// after a context sigil the parser refuses [:L1306-L1310], whatever the function name would have
    /// been.
    /// </summary>
    ContextFunctionMacro = 2,
}

/// <summary>
/// The result of projecting a reference's sigils onto contract C-04's expansion modes: either a single
/// wire mode, or one of the two combinations the legacy parser refuses.
/// </summary>
/// <param name="Mode">
/// The wire mode. <c>ExpansionMode.Unspecified</c> when <paramref name="Rejection"/> is set, and also
/// when the reference genuinely carries no expansion timing - a bare reference and a non-macro context
/// reference are both scope-or-nothing rather than static-or-dynamic, and the contract deliberately
/// keeps the context sigil out of the five modes.
/// </param>
/// <param name="Rejection">
/// <see cref="ExpansionRejection.None"/> unless the combination is one the parser refuses.
/// </param>
public readonly record struct ExpansionModeProjection(
    ExpansionMode Mode,
    ExpansionRejection Rejection)
{
    /// <summary>
    /// <see langword="true"/> when the legacy parser refuses this combination, in which case
    /// <see cref="Mode"/> carries no meaning and <see cref="Rejection"/> says why.
    /// </summary>
    public bool IsRejected => Rejection != ExpansionRejection.None;
}

/// <summary>
/// The three orthogonal sigil bits the legacy parser sets for one reference, and the projection from
/// them onto contract C-04's five expansion modes.
/// </summary>
/// <param name="IsCtx">
/// The <c>@</c> context sigil was present [MACRO_CONTEXT declared :L1253, set :L1296, copied to
/// <c>vardata.isctx</c> :L1413]. Selects the RESOLUTION SCOPE: the reference resolves against the
/// CALLING context's DataWindow and row rather than against this service's own table [:L2180-L2186].
/// </param>
/// <param name="IsMacro">
/// A <c>$</c> sigil was present [MACRO_FLAG declared :L1252; set unconditionally in the "$" arm
/// :L1289, and conditionally in the "@" arm :L1297; copied to <c>vardata.ismacro</c> :L1414]. The
/// reference is a macro rather than a plain value.
/// </param>
/// <param name="IsDynamic">
/// The sigil was DOUBLED - <c>bDD</c> in the source [:L1290, :L1299]. For a variable it selects
/// dynamic expansion over static [:L1406, :L1415]; for a function it becomes
/// <see cref="FunctionReference.Builtin"/> [:L1311].
/// </param>
/// <remarks>
/// See the banner comment above this type for the full derivation, the reachability proof for the two
/// unreachable bit patterns, and the evidence that the mode is a per-reference property.
/// </remarks>
public readonly record struct ExpansionSigils(
    bool IsCtx = false,
    bool IsMacro = false,
    bool IsDynamic = false)
{
    /// <summary>
    /// A bare reference with no sigil at all - an ordinary DataWindow column or expression reference.
    /// All three bits clear.
    /// </summary>
    public static ExpansionSigils Bare => default;

    /// <summary>
    /// <c>$name</c> - a statically expanded macro variable [docs/n_cst_dwsvc_columnexp.md:L37-L54,
    /// syntax at :L41]. Substituted at BIND TIME, so a later assignment to the variable cannot change
    /// the result: the specification's worked example stays fixed at 5 [:L48-L53].
    /// </summary>
    public static ExpansionSigils StaticMacro => new(IsCtx: false, IsMacro: true, IsDynamic: false);

    /// <summary>
    /// <c>$$name</c> - a dynamically expanded macro variable [docs/n_cst_dwsvc_columnexp.md:L56-L73,
    /// syntax at :L60]. Retained AS A REFERENCE and resolved at CALCULATION time, so mutation
    /// propagates: the identical worked example yields 6 [:L68-L70].
    /// </summary>
    public static ExpansionSigils DynamicMacro => new(IsCtx: false, IsMacro: true, IsDynamic: true);

    /// <summary>
    /// <c>@name</c> - a context-scoped COLUMN read: the value is fetched from the calling context's
    /// row by column name [:L2184-L2186]. Context without a macro sigil.
    /// </summary>
    public static ExpansionSigils Context => new(IsCtx: true, IsMacro: false, IsDynamic: false);

    /// <summary>
    /// <c>@$name</c> - context plus STATIC expansion. Reachable from the grammar and REFUSED by the
    /// parser [:L1415-L1419]; see <see cref="ExpansionRejection.ContextStaticExpansion"/>.
    /// </summary>
    public static ExpansionSigils ContextStaticMacro => new(IsCtx: true, IsMacro: true, IsDynamic: false);

    /// <summary>
    /// <c>@$$name</c> - context plus DYNAMIC expansion, which is the only accepted context macro form.
    /// The context service's variable calculator resolves it [:L2181-L2183].
    /// </summary>
    public static ExpansionSigils ContextDynamicMacro => new(IsCtx: true, IsMacro: true, IsDynamic: true);

    /// <summary>
    /// Projects a VARIABLE reference's sigils onto its wire expansion mode.
    /// </summary>
    /// <returns>
    /// The single mode this combination denotes, or the rejection the parser raises for it. The
    /// function is TOTAL over all eight bit patterns and maps each to exactly one outcome.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The complete table, with the reachability of each pattern:
    /// </para>
    /// <list type="table">
    ///   <listheader>
    ///     <term>ctx / macro / dynamic</term>
    ///     <description>form, reachability and outcome</description>
    ///   </listheader>
    ///   <item>
    ///     <term>false / false / false</term>
    ///     <description>
    ///       a bare reference. Reachable - it is an ordinary column reference the parser never records
    ///       as a macro. No expansion timing applies, so <c>Unspecified</c>.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <term>false / true / false</term>
    ///     <description><c>$name</c>. Reachable. <c>Static</c> [:L1415].</description>
    ///   </item>
    ///   <item>
    ///     <term>false / true / true</term>
    ///     <description><c>$$name</c>. Reachable. <c>Dynamic</c> [:L1290, :L1406].</description>
    ///   </item>
    ///   <item>
    ///     <term>true / false / false</term>
    ///     <description>
    ///       <c>@name</c>. Reachable. A context COLUMN read [:L2185]: the sigil selects scope, not
    ///       timing, so <c>Unspecified</c> and the scope travels on
    ///       <see cref="VariableReference.IsCtx"/>.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <term>true / true / false</term>
    ///     <description>
    ///       <c>@$name</c>. Reachable and REFUSED -
    ///       <see cref="ExpansionRejection.ContextStaticExpansion"/> [:L1415-L1419].
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <term>true / true / true</term>
    ///     <description>
    ///       <c>@$$name</c>. Reachable. <c>Dynamic</c> timing, resolved in the context scope
    ///       [:L2181-L2183].
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <term>false / false / true</term>
    ///     <description>
    ///       UNREACHABLE: the "$" arm sets <c>bIsMacro</c> unconditionally [:L1289] and <c>bDD</c> is
    ///       assigned nowhere else. Answered as <c>Unspecified</c> to keep the projection total.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <term>true / false / true</term>
    ///     <description>
    ///       UNREACHABLE: in the "@" arm a false <c>bIsMacro</c> leaves nPos un-advanced [:L1298], so
    ///       the <c>bDD</c> test at [:L1299] re-reads the same character and <c>bDD</c> equals
    ///       <c>bIsMacro</c>. Answered as <c>Unspecified</c>.
    ///     </description>
    ///   </item>
    /// </list>
    /// </remarks>
    public ExpansionModeProjection ProjectVariableReference()
    {
        // A reference with no macro sigil carries no expansion timing at all. That covers the bare
        // reference, the context column read at [:L2185], and both unreachable dynamic-without-macro
        // patterns proved impossible in the banner comment above.
        if (!IsMacro)
        {
            return new ExpansionModeProjection(ExpansionMode.Unspecified, ExpansionRejection.None);
        }

        // Static expansion of a context-scoped variable is refused outright [:L1415-L1419]. The guard
        // sits before the mode decision for the same reason the legacy's does: the refusal is reached
        // on the static branch, so it must be answered before static is reported as a mode.
        if (IsCtx && !IsDynamic)
        {
            return new ExpansionModeProjection(
                ExpansionMode.Unspecified,
                ExpansionRejection.ContextStaticExpansion);
        }

        // Doubled sigil means dynamic - resolved at calculation time so mutation propagates
        // [:L1290, :L2189-L2200]. Single sigil means static - substituted at bind time and gone from
        // the stored expression [:L1435]. The context bit does not alter the TIMING and so does not
        // alter the mode; it travels separately on VariableReference.IsCtx.
        return new ExpansionModeProjection(
            IsDynamic ? ExpansionMode.Dynamic : ExpansionMode.Static,
            ExpansionRejection.None);
    }

    /// <summary>
    /// Projects a macro-FUNCTION reference's sigils onto its wire expansion mode.
    /// </summary>
    /// <param name="dispatch">
    /// Which legacy dispatch arm the engine classified this call into, from the function name and the
    /// doubled-sigil flag. See <see cref="MacroFunctionDispatch"/> for why the classification is an
    /// input rather than something derived here.
    /// </param>
    /// <returns>
    /// The single mode this call denotes, or <see cref="ExpansionRejection.ContextFunctionMacro"/> when
    /// the context sigil is present.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="dispatch"/> is not one of the three declared members. Reported rather than
    /// silently defaulted, because every remaining wire mode is already claimed and guessing would
    /// mislabel a reference on the wire.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE CONTEXT CHECK COMES FIRST AND IGNORES <paramref name="dispatch"/>, because the legacy's does:
    /// the parser refuses on meeting the opening bracket [:L1306-L1310], before it has looked at the
    /// name, so no function name rescues a context-scoped call.
    /// </para>
    /// <para>
    /// <see cref="IsDynamic"/> is not re-tested here, and that is deliberate. For a function reference
    /// the legacy derives <c>funcdata.builtin</c> FROM the doubled sigil [:L1311] and then dispatches on
    /// the resulting name classification [:L1386-L1401, :L2237-L2287], so <paramref name="dispatch"/>
    /// already carries that bit's consequence. Re-deriving it would create a second source of truth that
    /// could disagree with the engine's own classification.
    /// </para>
    /// </remarks>
    public ExpansionModeProjection ProjectFunctionReference(MacroFunctionDispatch dispatch)
    {
        if (IsCtx)
        {
            return new ExpansionModeProjection(
                ExpansionMode.Unspecified,
                ExpansionRejection.ContextFunctionMacro);
        }

        ExpansionMode mode = dispatch switch
        {
            // $Fn(args) - the application owns the implementation [:L2286-L2287].
            MacroFunctionDispatch.Application => ExpansionMode.MacroDirect,

            // $$('name') - FUNC_VAR, the variable name computed at run time [:L1388, :L2239].
            MacroFunctionDispatch.VariableLookup => ExpansionMode.DynamicIndirect,

            // $$Invoke('Fn', args) - FUNC_INVOKE, the function name itself a variable [:L1393, :L2258].
            MacroFunctionDispatch.DynamicInvoke => ExpansionMode.MacroDynamic,

            _ => throw new ArgumentOutOfRangeException(
                nameof(dispatch),
                dispatch,
                "A macro function reference must be classified as Application, VariableLookup or "
                    + "DynamicInvoke. Those are the only three arms the legacy dispatch switch has "
                    + "[n_cst_dwsvc_columnexp.sru:L1386-L1401], and each already owns one of the "
                    + "five wire expansion modes."),
        };

        return new ExpansionModeProjection(mode, ExpansionRejection.None);
    }
}


/// <summary>
/// The macro variable environment: an immutable, ONE-BASED snapshot of the legacy global variable
/// table, and the declaring home of the two preserved variable-type constants.
/// </summary>
/// <remarks>
/// <para>
/// PORTED FROM <c>GLOBALVARDATA GlobalVars[]</c>
/// [ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru:L103], the private array the
/// expression service keeps its variables in, together with <c>_of_findvarindex</c> [:L994-L1001], the
/// only routine that turns a name into a position in it. The two variable-type constants are declared
/// on this type because the legacy declares them on the very object that owns the array [:L117-L118] -
/// keeping the constant and the table it discriminates in one place preserves that adjacency.
/// </para>
/// <para>
/// WHY IMMUTABLE, WHEN THE LEGACY ARRAY IS MUTABLE INSTANCE STATE. Contract C-04 requires the payload
/// to carry a BIND-TIME SNAPSHOT and a LIVE ENVIRONMENT as two separate things
/// [shared/PowerFramework.Contracts/Proto/dataservices.v1.proto:L2988-L3000], which is only meaningful
/// if a snapshot cannot change after it is taken. An immutable snapshot also makes this type safe to
/// share across the concurrent gRPC calls a service handles, and gives value equality that a
/// characterization recording can compare directly. Mutation is expressed as
/// <see cref="Append(GlobalVariable)"/> and <see cref="Replace(int, GlobalVariable)"/>, which produce
/// the next snapshot.
/// </para>
/// <para>
/// WHAT THIS TYPE DELIBERATELY DOES NOT DO (C-B). It is the table, not the engine. There is no
/// <c>of_addvar</c>, no <c>of_setvar</c> and no <c>of_addvarexp</c> here, and therefore none of their
/// behaviour: no duplicate-name rejection [:L1606-L1609], no empty-name or empty-expression rejection
/// and no word-delimiter scan over the name [:L1605-L1617], no expression parsing [:L1619], no
/// <c>hasMacro</c> derivation [:L1623], no literal rendering [:L1091-L1109], no column reset
/// [:L1627], no recalculation, and no dirty-flag propagation. Every one of those belongs to
/// <c>ColumnExpressionEngine</c>, and each is cited here so that its absence reads as a boundary rather
/// than as an omission.
/// </para>
/// </remarks>
public sealed class ExpressionVariableEnvironment : IEquatable<ExpressionVariableEnvironment>
{
    // ==========================================================================================
    //  THE TWO PRESERVED CONSTANT SPELLINGS
    //  ----------------------------------------------------------------------------------------
    //  VAR_LOCAL and VAR_FOREIGN keep the legacy SCREAMING_SNAKE spelling in deliberate defiance of
    //  .NET naming convention, exactly as RetCode, Enums, Categories, EventGate, ItemChangeProtocol,
    //  ClauseModifier, IPagingRewriter and LegacyDefaults do elsewhere in this repository. The reason
    //  is data integrity, not taste: the discriminator travels in the serialized C-04 payload, in log
    //  records and in the characterization recordings that are this migration's parity evidence, so a
    //  rename would silently invalidate every stored comparison that mentions it.
    //
    //  A REPORTED FINDING, RECORDED HERE RATHER THAN RESOLVED BY RENAMING. The repository-root
    //  .editorconfig carries per-file naming-analyzer suppressions for exactly this pattern, and its
    //  Expressions/ entry names ColumnExpressionEngine.cs, which is where the neighbouring CLC_* and
    //  FUNC_* constants land. This file was NOT in that scope when it was written. The correct remedy
    //  is the one .editorconfig documents for itself - one section per DECLARING file, citing the
    //  PowerScript declaration site - so a section naming this file was added alongside the existing
    //  ones. The constants were not renamed: CA1707 and IDE1006 are the two diagnostics that report
    //  these spellings, both are off under the repository's current analysis configuration, and the
    //  suppression exists so that raising AnalysisMode later cannot break the port.
    // ==========================================================================================

    /// <summary>
    /// <c>constant long VAR_LOCAL = 0</c> [:L117]. The variable is resolved from THIS service's own
    /// table: <see cref="GlobalVariable.Local"/> carries its definition.
    /// </summary>
    /// <remarks>
    /// It is also the DEFAULT, and that is faithful rather than chosen: PowerScript zero-initialises a
    /// structure, so a <c>globalvardata</c> that nobody assigned a <c>vartype</c> to is already local.
    /// The wire counterpart is <c>GlobalVarData.VarType.VAR_LOCAL</c>, same value.
    /// </remarks>
    public const long VAR_LOCAL = 0;

    /// <summary>
    /// <c>constant long VAR_FOREIGN = 1</c> [:L118]. The variable is resolved through ANOTHER
    /// DataWindow's expression service: <see cref="GlobalVariable.Foreign"/> carries the reference, and
    /// the legacy branches on this exact comparison before dereferencing it [:L2199-L2200].
    /// </summary>
    /// <remarks>
    /// A foreign variable cannot be expanded statically - the parser refuses it [:L1425-L1428] - and the
    /// legacy specification states the rule where it introduces the feature
    /// [docs/n_cst_dwsvc_columnexp.md:L34]. The wire counterpart is
    /// <c>GlobalVarData.VarType.VAR_FOREIGN</c>, same value.
    /// </remarks>
    public const long VAR_FOREIGN = 1;

    private static readonly ExpressionVariableEnvironment EmptyEnvironment =
        new(ImmutableArray<GlobalVariable>.Empty);

    private readonly ImmutableArray<GlobalVariable> _variables;

    private ExpressionVariableEnvironment(ImmutableArray<GlobalVariable> variables)
    {
        _variables = variables;
    }

    /// <summary>
    /// The empty environment - no variables declared. The faithful analogue of the legacy array before
    /// anything has been added to it, where <c>UpperBound(GlobalVars)</c> is 0.
    /// </summary>
    public static ExpressionVariableEnvironment Empty => EmptyEnvironment;

    /// <summary>
    /// Creates an environment from an ordered sequence of entries.
    /// </summary>
    /// <param name="variables">
    /// The entries, in table order. The order is significant: it determines the one-based index every
    /// <see cref="VariableReference.Index"/> refers to, and therefore which entry
    /// <see cref="IndexOf(string)"/> finds when names repeat. Nothing here reorders, deduplicates or
    /// inspects an entry.
    /// </param>
    /// <returns>The environment, or <see cref="Empty"/> when the sequence is empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="variables"/> is <see langword="null"/>.</exception>
    public static ExpressionVariableEnvironment Create(IEnumerable<GlobalVariable> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);

        return Create([.. variables]);
    }

    /// <summary>
    /// Creates an environment from an ordered array of entries.
    /// </summary>
    /// <param name="variables">
    /// The entries, in table order. An uninitialised <see cref="ImmutableArray{T}"/> is treated as
    /// empty, because a PowerBuilder array is never null - an unassigned one simply has an
    /// <c>UpperBound</c> of 0.
    /// </param>
    /// <returns>The environment, or <see cref="Empty"/> when there are no entries.</returns>
    public static ExpressionVariableEnvironment Create(ImmutableArray<GlobalVariable> variables)
    {
        if (variables.IsDefaultOrEmpty)
        {
            return EmptyEnvironment;
        }

        return new ExpressionVariableEnvironment(variables);
    }

    /// <summary>
    /// The entries in table order, ZERO-BASED as a .NET collection. Use <see cref="this[int]"/> when
    /// working in the legacy's one-based index space, which is what
    /// <see cref="VariableReference.Index"/> and <see cref="ForeignVariableReference.Index"/> carry.
    /// </summary>
    public ImmutableArray<GlobalVariable> Variables => _variables;

    /// <summary>
    /// How many variables the environment declares.
    /// </summary>
    public int Count => _variables.Length;

    /// <summary>
    /// The legacy <c>UpperBound(GlobalVars)</c> - THE LAST VALID ONE-BASED INDEX, which is 0 when the
    /// table is empty.
    /// </summary>
    /// <remarks>
    /// Numerically equal to <see cref="Count"/>, and exposed under its PowerScript name on purpose. A
    /// PowerBuilder array is one-based and its upper bound is the last valid subscript, whereas a .NET
    /// length is one past the end; conflating the two is the single most dangerous mechanical hazard in
    /// this migration. Naming the legacy quantity explicitly gives a ported loop something to be
    /// written against - <c>for i = 1 to env.UpperBound</c> - instead of an off-by-one waiting to
    /// happen.
    /// </remarks>
    public int UpperBound => _variables.Length;

    /// <summary>
    /// The entry at a ONE-BASED index, matching <c>GlobalVars[index]</c> in the oracle.
    /// </summary>
    /// <param name="index">
    /// The one-based index, from 1 to <see cref="UpperBound"/>. This is the index space
    /// <see cref="VariableReference.Index"/> uses, where 0 means "not yet bound" rather than "the first
    /// entry".
    /// </param>
    /// <returns>The entry.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is below 1 or above <see cref="UpperBound"/>. That is the faithful
    /// translation of a PowerBuilder array subscript fault, not added validation; use
    /// <see cref="IsValidIndex(int)"/> to test an index without provoking it, which is what a consumer
    /// holding a possibly-unbound <see cref="VariableReference.Index"/> should do.
    /// </exception>
    public GlobalVariable this[int index]
    {
        get
        {
            if (!IsValidIndex(index))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index),
                    index,
                    $"The global variable table holds {_variables.Length} entries, so a one-based "
                        + "index must be between 1 and that count. Index 0 is not the first entry: on "
                        + "a variable reference it means \"not yet bound\", which is resolved by name "
                        + "through IndexOf rather than by subscript "
                        + "[n_cst_dwsvc_columnexp.sru:L2189-L2195].");
            }

            return _variables[index - 1];
        }
    }

    /// <summary>
    /// Whether a ONE-BASED index addresses an existing entry.
    /// </summary>
    /// <param name="index">The one-based index to test. 0 and negatives are never valid.</param>
    /// <returns><see langword="true"/> when <see cref="this[int]"/> would succeed.</returns>
    public bool IsValidIndex(int index) => index >= 1 && index <= _variables.Length;

    /// <summary>
    /// Finds a variable by name and returns its ONE-BASED index, or 0 when it is not declared - the
    /// port of <c>_of_findvarindex</c> [:L994-L1001].
    /// </summary>
    /// <param name="name">
    /// The name to find, WITHOUT sigils - i.e. <see cref="VariableReference.Name"/>, never
    /// <see cref="VariableReference.FullName"/>.
    /// </param>
    /// <returns>
    /// The one-based index of the match, or 0 when there is none. ZERO IS THE LEGACY'S OWN
    /// "NOT FOUND" ANSWER [:L1000] and callers test it as such [:L1421, :L2191], so it is returned
    /// rather than -1 and rather than an exception.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE SCAN RUNS BACKWARDS, AND THAT IS PRESERVED DELIBERATELY. The oracle iterates
    /// <c>for nIndex = UpperBound(GlobalVars) to 1 step -1</c> [:L996] and returns on the first match,
    /// so when a name occurs more than once THE LAST ENTRY WINS. Reverse iteration is the most
    /// dangerous single pattern in this migration precisely because it looks like a mistake and is not:
    /// "correcting" it to a forward scan would silently change which entry a duplicated name resolves
    /// to, and no row-count or arity assertion would notice.
    /// </para>
    /// <para>
    /// The legacy's own <c>of_addvarexp</c> rejects a duplicate name before it can be appended
    /// [:L1606-L1609], so duplicates do not arise through that path - which is exactly why the reverse
    /// scan's tie-break is invisible in normal operation and would survive a careless rewrite unnoticed.
    /// It is reproduced here because this type does not enforce that rejection and must therefore behave
    /// correctly when a duplicate does exist.
    /// </para>
    /// <para>
    /// MATCHING IS ORDINAL AND CASE-SENSITIVE. PowerScript's <c>=</c> on strings compares exactly, and
    /// <c>of_addvarexp</c>'s own header says so in as many words - 区分大小写, "case-sensitive"
    /// [:L1587]. A <see langword="null"/> name matches nothing and returns 0, because no entry's
    /// <see cref="GlobalVariable.Name"/> is ever null.
    /// </para>
    /// </remarks>
    public int IndexOf(string? name)
    {
        if (name is null)
        {
            return 0;
        }

        // Backwards, from the last valid one-based subscript down to 1, exactly as [:L996] does.
        for (int index = _variables.Length; index >= 1; index--)
        {
            if (string.Equals(_variables[index - 1].Name, name, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return 0;
    }

    /// <summary>
    /// Produces the next snapshot with one entry appended at the end of the table - the structural half
    /// of <c>GlobalVars[UpperBound(GlobalVars) + 1] = varData</c> [:L1625].
    /// </summary>
    /// <param name="variable">The entry to append.</param>
    /// <returns>A new environment; this one is unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="variable"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// STRUCTURAL ONLY. It does not reject a duplicate name, parse an expression, derive
    /// <see cref="LocalVariableDefinition.HasMacro"/> or reset the column bindings, all of which
    /// <c>of_addvarexp</c> does around this assignment [:L1605-L1627]. Appending at the end is what
    /// makes the new entry's one-based index equal to the returned environment's
    /// <see cref="UpperBound"/>.
    /// </remarks>
    public ExpressionVariableEnvironment Append(GlobalVariable variable)
    {
        ArgumentNullException.ThrowIfNull(variable);

        return new ExpressionVariableEnvironment(_variables.Add(variable));
    }

    /// <summary>
    /// Produces the next snapshot with the entry at a ONE-BASED index replaced - the structural half of
    /// <c>GlobalVars[index] = varData</c>, which is how the legacy updates a variable in place.
    /// </summary>
    /// <param name="index">The one-based index of the entry to replace.</param>
    /// <param name="variable">The replacement entry.</param>
    /// <returns>A new environment; this one is unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="variable"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is below 1 or above <see cref="UpperBound"/>.
    /// </exception>
    /// <remarks>
    /// STRUCTURAL ONLY, on the same terms as <see cref="Append(GlobalVariable)"/>: no recalculation and
    /// no dirty-flag propagation, both of which belong to the engine's <c>of_setvarexp</c> path.
    /// </remarks>
    public ExpressionVariableEnvironment Replace(int index, GlobalVariable variable)
    {
        ArgumentNullException.ThrowIfNull(variable);

        if (!IsValidIndex(index))
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                $"The global variable table holds {_variables.Length} entries, so a one-based index "
                    + "must be between 1 and that count.");
        }

        return new ExpressionVariableEnvironment(_variables.SetItem(index - 1, variable));
    }

    /// <summary>
    /// Compares two snapshots ENTRY BY ENTRY, in order.
    /// </summary>
    /// <param name="other">The snapshot to compare with, or <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="true"/> when both hold the same entries in the same order. Order participates
    /// because it defines the one-based index space every reference resolves against, so two tables with
    /// the same entries in a different order are genuinely different environments.
    /// </returns>
    public bool Equals(ExpressionVariableEnvironment? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null && _variables.AsSpan().SequenceEqual(other._variables.AsSpan());
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as ExpressionVariableEnvironment);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(_variables.Length);
        foreach (GlobalVariable variable in _variables)
        {
            hash.Add(variable);
        }

        return hash.ToHashCode();
    }
}
