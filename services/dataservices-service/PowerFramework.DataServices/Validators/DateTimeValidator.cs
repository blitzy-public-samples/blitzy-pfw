// ==============================================================================================
//  DateTimeValidator - the PowerBuilder `datetime` arm of the DataWindow item-change coercion
//  --------------------------------------------------------------------------------------------
//  PORTED FROM    ws_objects/pfw.datawindow.services.pbl.src/dwnvldatetime.srf (13 lines), plus
//                 the single `case "datet"` arm of the item-change coercion table at
//                 ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L238-L239.
//  ORACLE STATUS  Both sources are READ ONLY. They are the behavioural oracle for parity testing
//                 and are never an edit target (constraint C-C). Nothing else in the repository
//                 can adjudicate a disagreement about a value below, so every decision here
//                 carries the ws_objects/** locator it was taken from (constraint C-K).
//  READ AS REFERENCE, NOT PORTED HERE
//                 dwvaluetoexp.srf:L53-L57    the sole consumer of the null sentinel spelling
//                 se_cst_dw.sru:L231-L244     the switch this arm belongs to, owned by Domain/
//                 n_cst_dwsvc.sru:L26-L32     the column-type category this arm corresponds to
//                 n_cst_dwsvc.sru:L503-L518   a SECOND truncation table, subtly different
//                 retcode.sru:L39, :L52       the two return codes named below
//
//  THE FOLDER NAME IS MISLEADING, AND THAT IS DELIBERATELY NOT "FIXED"
//  --------------------------------------------------------------------------------------------
//  `dwnvldatetime.srf` is not a validator and performs no validation. Its entire body is three
//  lines - it declares a typed local, nulls it, and returns it:
//
//      dwnvldatetime.srf:L6    global function datetime dwnvldatetime ()
//      dwnvldatetime.srf:L9    datetime nvl
//      dwnvldatetime.srf:L10   SetNull(nvl)
//      dwnvldatetime.srf:L11   return nvl
//
//  It exists to be named from inside a DataWindow EXPRESSION STRING rather than called from
//  PowerScript, and the five `dwNvl*` producers have no PowerScript call site anywhere in the
//  544-object estate. Their only appearance is as a string literal in the expression emitter:
//
//      dwvaluetoexp.srf:L54    sVal = "DateTime('" + String(val) + "')"
//      dwvaluetoexp.srf:L55    if IsNull(sVal) then sVal = "dwNvlDateTime()"
//
//  So the observable contract of the ported function is its EMITTED SPELLING and the typed null it
//  stands for - nothing else. That is why this file publishes constants and a typed null before it
//  publishes any behaviour, and why the folder keeps its legacy-derived name.
//
//  Worth understanding about L54-L55, because it explains why the sentinel replaces the WHOLE
//  expression rather than just the text inside the quotes: PowerScript propagates null through
//  concatenation, so when `val` is null, `String(val)` is null and the entire concatenation at L54
//  collapses to null. The `IsNull(sVal)` test at L55 then swaps in the sentinel for the complete
//  expression. `DateTime('')` is never emitted - it is `dwNvlDateTime()` or a full literal.
//
//  WHAT THIS FILE DOES NOT DO, AND MUST NOT BE EXTENDED TO DO
//  --------------------------------------------------------------------------------------------
//    * It does not implement the switch. `choose case Left(dwo.ColType,5)` at se_cst_dw.sru:L231
//      belongs to Domain/ItemChangeProtocol.cs, which ports se_cst_dw.sru:L182-L253 in full. This
//      file supplies one arm's token set and one arm's behaviour, and stops there.
//    * It does not redeclare the COL_TYPE_* constants. Domain/DataWindowServiceHost.cs owns them;
//      a second copy would be a second source of truth. The correspondence is recorded in prose
//      only: this arm is the COL_TYPE_DATETIME category, value 4 (n_cst_dwsvc.sru:L30).
//    * It does not reference anything under Domain/ or Expressions/, and it must not acquire such
//      a reference. Validators/ is deliberately sequenced ahead of both and has no sibling-folder
//      dependency, so the whole folder can be compiled and tested in isolation.
//    * It emits no expression text of its own. The `DateTime('...')` wrapper at
//      dwvaluetoexp.srf:L54 belongs to Validators/ValueToExpression.cs, which consumes
//      ExpressionValueFormat and NullLiteralExpression from here rather than restating either.
//    * It contains no dialog, no MessageBox port, no I18N call, no logging, no I/O and no clock
//      read. The invalid-value dialog at se_cst_dw.sru:L357 and its I18N(CAT_DWSVC, ...) fallback
//      become Domain's structured error, and the rendering half of it is DesignSystem-deferred,
//      which constraint C-D places entirely outside this refactor.
//    * It takes no dependency on a validation framework. FluentValidation is excluded by name,
//      because a rule engine would obscure parity - the single property that matters most here.
//
//  DECISION 1 - A STATIC CLASS OF STATIC MEMBERS
//  --------------------------------------------------------------------------------------------
//  The plan's object-kind mapping is explicit that a `*.srf` global function becomes a `static`
//  method on a role-named static class, and gives Predicates.IsSucceeded, Bits.BitAnd and
//  Formatting.Sprintf as its own examples. There is therefore no instance, no interface and no DI
//  registration for this type. Every member is a pure function of its arguments: nothing here
//  performs I/O, logs, reads a clock or holds mutable state, so every member is safe to call
//  concurrently from any number of threads. That purity is not decoration - it is what lets the
//  sibling test project drive table-driven parity theories without a fixture (constraint C-H).
//
//  DECISION 2 - MEMBER NAMES ARE PascalCase; THE LEGACY SPELLING LIVES IN STRING VALUES
//  --------------------------------------------------------------------------------------------
//  This refactor preserves legacy identifier spellings verbatim in exactly the places that carry
//  a constant catalogue, and the repository-root .editorconfig scopes its CA1707 and IDE1006
//  suppressions to those individual files. THIS FILE IS DELIBERATELY NOT ONE OF THEM. Combined
//  with TreatWarningsAsErrors from Directory.Build.props, a member here named `dwNvlDateTime`
//  would be a build ERROR rather than a style remark. The legacy spelling is preserved where it is
//  actually observable - inside the string VALUE of FunctionName and NullLiteralExpression - which
//  is the only place a consumer or a characterization recording can see it. There is no
//  `#pragma warning disable` in this file and no project-wide suppression added for it.
//
//  DECISION 3 - PowerBuilder `datetime` IS `DateTime`, AND ITS NULL IS `DateTime?`
//  --------------------------------------------------------------------------------------------
//  The plan's type mapping fixes `datetime` -> `DateTime`, and separately requires that a
//  PowerBuilder value-type null be ported as a NULLABLE VALUE TYPE and never collapsed to a
//  default. Both halves matter, and getting either wrong is silent:
//
//      DateOnly        WRONG. It is the mapping for PowerBuilder `date`, which is the ADJACENT arm
//                      at se_cst_dw.sru:L240-L241 and belongs to DateValidator.cs. Conflating the
//                      two would collide with that file and would change the overload set
//                      Validators/ValueToExpression.cs has to offer, because dwvaluetoexp.srf
//                      declares `date` and `datetime` as two separate overloads (L12 and L13).
//      DateTimeOffset  WRONG. See DECISION 4 - it carries a zone, and `datetime` does not.
//      default(DateTime)
//                      WRONG, and this is the dangerous one because it compiles and looks right.
//                      `default(DateTime)` formats as `0001-01-01 00:00:00` (measured), so a null
//                      collapsed to a default makes the emitter produce
//                      `DateTime('0001-01-01 00:00:00')` exactly where dwvaluetoexp.srf:L55
//                      produces `dwNvlDateTime()`. That is a wrong VALUE, not a wrong format, and
//                      no round-trip test would catch it.
//
//  DECISION 4 - NO TIME-ZONE SEMANTICS ARE ATTACHED, IN EITHER DIRECTION
//  --------------------------------------------------------------------------------------------
//  A PowerBuilder `datetime` is a bare wall-clock value with no zone and no offset. Nothing in
//  this file converts a DateTimeKind, normalises to UTC, or applies a time-zone adjustment, and
//  nothing may be added that does. This was measured rather than assumed: formatting a
//  `DateTimeKind.Utc` value and a `DateTimeKind.Local` value that carry identical components
//  produces identical text, which is the direct evidence that no normalisation is applied.
//
//  One consequence is worth stating precisely so it is not later mistaken for a defect. Parsing
//  always yields `DateTimeKind.Unspecified`, because the emitted text carries no zone marker to
//  parse. A round trip therefore preserves every component and every tick exactly and SHIFTS
//  NOTHING, while a `Utc` or `Local` input comes back as `Unspecified`. That is the correct
//  outcome for a zone-less value: the alternative - emitting an offset so Kind could round-trip -
//  would add zone semantics the legacy type does not have, which is precisely what is forbidden.
//
//  DECISION 5 - "datet" IS A TRUNCATION, AND THE MATCH IS CASE-SENSITIVE EXACT EQUALITY
//  --------------------------------------------------------------------------------------------
//  se_cst_dw.sru:L231 switches on `Left(dwo.ColType,5)`, so every arm literal is already a
//  five-character truncation of a PowerBuilder column type. `"datet"` is therefore the correct,
//  complete token for `datetime` and is NOT a misspelling. "Correcting" it to `"datetime"` would
//  produce a token that can never match anything, because the value it is compared against has
//  already been cut to five characters. Three separate hazards live in reproducing that one line,
//  and each is reproduced at its point of use further down this file:
//
//      Left is length-safe, Substring is not.  PowerScript `Left(s,5)` truncates a shorter string
//                                              harmlessly; `s.Substring(0,5)` THROWS on one. The
//                                              inputs that reach here really are shorter - a
//                                              failed Describe returns `""`, and the adjacent
//                                              `"date"` column type is four characters.
//      Equality, never StartsWith.             `choose case` compares for equality, which makes
//                                              the arms mutually exclusive. `Left("datetime",5)`
//                                              is `"datet"` and must match ONLY here, while
//                                              `Left("date",5)` is `"date"` and must match ONLY
//                                              in DateValidator.cs. A StartsWith port collapses
//                                              the pair, because "datetime".StartsWith("date") is
//                                              true - the datetime column would then be coerced
//                                              by whichever of the two arms was tested first.
//      Ordinal, never OrdinalIgnoreCase.       PowerScript `=` on strings, and therefore
//                                              `choose case`, is case-sensitive; the arm literal
//                                              is lowercase and `Describe(".ColType")` is
//                                              observed lowercase. Accepting `"DATETIME"` would
//                                              WIDEN the contract on a guess, where the rule is
//                                              to narrow with a defined outcome instead.
//
//  A related trap, recorded because the two tables look interchangeable and are not: the OTHER
//  truncation table in this library, `_of_convertcoltype` at n_cst_dwsvc.sru:L503-L518, uses the
//  same tokens but DOES carry a `case else` arm (L516-L517, returning COL_TYPE_UNKNOWN). The
//  item-change table at se_cst_dw.sru:L231-L244 carries none. This file reproduces the
//  item-change table, so an unrecognised column type has no fallback here either.
//
//  DECISION 6 - TWO COERCION PATHS, AND ONLY ONE OF THEM IS THE LEGACY ONE
//  --------------------------------------------------------------------------------------------
//  The legacy arm is `SetItem(row,Long(dwo.ID),DateTime(data))` (se_cst_dw.sru:L239). It performs
//  no validity test, its return value is not captured, and the enclosing switch has no `case else`
//  (L244), so an uncoercible value produces no write and reports nothing at all. Coerce reproduces
//  that silence exactly and NEVER throws; adding a throw would be a regression dressed up as
//  robustness, and behaviour improvements beyond what the technology transition requires are
//  forbidden. TryCoerce is offered separately for callers that genuinely need to know, reporting
//  RetCode.OK or RetCode.E_INVALID_DATA. It is ADDITIVE and has no legacy analogue, and the
//  item-change path must not use it - see the remarks on each member.
//
//  DECISION 7 - EVERY CONVERSION PINS InvariantCulture AND DateTimeStyles EXPLICITLY
//  --------------------------------------------------------------------------------------------
//  No port of PowerBuilder's `String()` and `DateTime()` primitives exists anywhere in this
//  refactor - the shared Formatting class covers only formatretcode.srf and sprintf.srf - so this
//  file owns its own text-to-value and value-to-text conversion for the `datetime` type. Every one
//  of them passes CultureInfo.InvariantCulture and an explicit DateTimeStyles, and there is
//  deliberately no bare Parse, TryParse, Convert.*, ToString() or interpolated string anywhere
//  below. Ambient-culture date handling is the classic silent-corruption bug in a port like this
//  one, and it is exactly the class of difference that makes a legacy recording and a target
//  recording incomparable. Measured under Invariant, de-DE, en-US and th-TH - the last of which
//  has a non-Gregorian default calendar that would render 2020 as 2563 - the results are
//  byte-identical, which is what the explicit pin buys.
//
//  THE TWO UNVERIFIED-FROM-REPOSITORY ITEMS, COLLECTED HERE FOR THE PARITY AUTHOR
//  --------------------------------------------------------------------------------------------
//  Two behaviours below cannot be established from anything in this repository and must be pinned
//  against the behavioural oracle. Both are marked `UNVERIFIED-FROM-REPOSITORY` at their point of
//  reproduction as well, and both are confined to a single declaration so that pinning either is a
//  one-line change:
//
//      1. ExpressionValueFormat - the exact rendering PowerBuilder's no-format `String(datetime)`
//         produces at dwvaluetoexp.srf:L54, and in particular whether it carries fractional
//         seconds. No fixture, document or expression in the tree shows a formatted datetime
//         literal.
//      2. The value the legacy `DateTime(data)` at se_cst_dw.sru:L239 yields for an uncoercible
//         string. The repository never exercises the failure path.
//
//  Both belong in characterization/ and docs/PARITY.md as characterization items. This file does
//  not author either of those, but these annotations are what let their author find the two sites.
// ==============================================================================================

using System.Collections.Immutable;
using System.Globalization;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.DataServices.Validators;

/// <summary>
/// The PowerBuilder <c>datetime</c> column arm of the DataWindow item-change coercion table,
/// ported from <c>dwnvldatetime.srf</c> and <c>se_cst_dw.sru:L238-L239</c>.
/// </summary>
/// <remarks>
/// <para>
/// Publishes four things and nothing else: the emitted spelling of the legacy null-sentinel
/// expression function, the canonical invariant rendering of a <c>datetime</c> value, the typed
/// null that <c>dwnvldatetime.srf</c> returns, and the single column-type token
/// (<c>"datet"</c>) together with the coercion that the matching switch arm performs.
/// </para>
/// <para>
/// Every member is a pure, deterministic function of its arguments. Nothing performs I/O, logs,
/// reads a clock, consults the ambient culture or holds mutable state, so every member is safe to
/// call concurrently from any number of threads and produces identical results on every host.
/// </para>
/// <para>
/// This type does not implement the item-change switch itself; that belongs to
/// <c>Domain/ItemChangeProtocol.cs</c>. It also does not emit expression text: the
/// <c>DateTime('...')</c> wrapper at <c>dwvaluetoexp.srf:L54</c> belongs to
/// <c>Validators/ValueToExpression.cs</c>, which consumes <see cref="ExpressionValueFormat"/> and
/// <see cref="NullLiteralExpression"/> from here rather than restating either.
/// </para>
/// </remarks>
public static class DateTimeValidator
{
    // ==========================================================================================
    // REGION 1 - THE PUBLISHED SPELLINGS
    // Ported from dwnvldatetime.srf:L6 (the function name) and dwvaluetoexp.srf:L55 (the literal
    // form in which that name is emitted into a DataWindow expression).
    // ==========================================================================================
    //
    // WHY THESE ARE CONSTANTS HERE RATHER THAN LITERALS AT EACH USE SITE
    // ------------------------------------------------------------------------------------------
    // Two independent files have to agree on this spelling, character for character, and the
    // compiler cannot check that they do:
    //
    //     Validators/ValueToExpression.cs                 EMITS the name, porting
    //                                                     dwvaluetoexp.srf:L55.
    //     Expressions/DataWindowExpressionEvaluator.cs    REGISTERS the name in its expression
    //                                                     function table, standing in for the
    //                                                     runtime's Describe("Evaluate(...)")
    //                                                     mechanism used at n_cst_dwsvc.sru:L217.
    //
    // A one-character drift between the two does not fail to compile. It surfaces at RUNTIME as an
    // expression error, and the legacy detection idiom for that is coarse: the caller at
    // n_cst_dwsvc_columnexp.sru:L2391 recognises failure only as the returned value "?" or "!",
    // which carries no indication of which name was wrong. Single-sourcing the spelling removes
    // that failure mode entirely at zero observable cost. Recording the reasoning is required
    // because the choice is an internal structuring decision rather than a ported behaviour
    // (constraint C-K); the OBSERVABLE contract is only that the emitted characters match the
    // oracle, which the constants below guarantee and a test pins.
    //
    // MIND THE INTERNAL CAPITAL `T`. Cross-checked against all five sentinels declared in
    // dwvaluetoexp.srf - dwNvlNumber() at L19/L25/L31/L37/L43, dwNvlString() at L49,
    // dwNvlDateTime() at L55, dwNvlDate() at L61 and dwNvlTime() at L67 - `dwNvlDateTime` is the
    // only one carrying an internal capital in its type suffix. `dwNvlDatetime` is WRONG.
    // ==========================================================================================

    /// <summary>
    /// The name of the legacy DataWindow expression function that yields a null
    /// <c>datetime</c>, spelled exactly as the oracle spells it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Register this constant in an expression function table rather than retyping the name, so
    /// that registration and emission cannot drift apart. Note the internal capital <c>T</c>:
    /// the value is <c>dwNvlDateTime</c>, not <c>dwNvlDatetime</c>. Taken from
    /// <c>dwnvldatetime.srf:L6</c> and <c>dwvaluetoexp.srf:L55</c>.
    /// </para>
    /// <para>
    /// This is behavioural contract, not configuration. It stays compiled in and must never be
    /// moved to <c>appsettings.json</c> or bound from an options type: the spelling appears in
    /// emitted expression text and therefore in characterization recordings, so making it
    /// environment-dependent would destroy parity.
    /// </para>
    /// </remarks>
    public const string FunctionName = "dwNvlDateTime";

    /// <summary>
    /// The complete expression text emitted in place of a <c>datetime</c> literal when the value
    /// is null, namely <see cref="FunctionName"/> followed by an empty argument list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This replaces the WHOLE expression, not merely the text inside the quotes. At
    /// <c>dwvaluetoexp.srf:L54-L55</c> a null value makes the entire concatenation null - because
    /// PowerScript propagates null through concatenation - and the <c>IsNull</c> test then swaps in
    /// this sentinel for all of it. <c>DateTime('')</c> is never produced.
    /// </para>
    /// <para>
    /// Deliberately declared independently of <see cref="FunctionName"/> rather than composed from
    /// it at runtime, so that the exact 15 characters the oracle emits appear once, literally, and
    /// can be asserted character for character. A test pins both against
    /// <c>dwvaluetoexp.srf:L55</c>.
    /// </para>
    /// </remarks>
    public const string NullLiteralExpression = "dwNvlDateTime()";

    /// <summary>
    /// The canonical invariant format string for the text that goes inside
    /// <c>DateTime('...')</c>, standing in for PowerBuilder's no-format <c>String(datetime)</c>
    /// rendering at <c>dwvaluetoexp.srf:L54</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Validators/ValueToExpression.cs</c> consumes this constant so the emitter carries no
    /// format string of its own, and <see cref="FormatExpressionValue"/> and
    /// <see cref="Coerce"/> use it as well, so one declaration governs emission and acceptance
    /// alike. Always apply it with <see cref="CultureInfo.InvariantCulture"/>; see
    /// <see cref="FormatExpressionValue"/>.
    /// </para>
    /// <para>
    /// UNVERIFIED-FROM-REPOSITORY - pin against the behavioural oracle. The exact rendering
    /// PowerBuilder produces here cannot be established from this repository: no fixture, document
    /// or expression in the tree shows a formatted datetime literal, so the fractional-second
    /// precision in particular is unknown. Fractional seconds are DELIBERATELY NOT EMITTED, on
    /// three grounds. The legacy text comes from the no-format <c>String(datetime)</c> overload,
    /// whose rendering carries whole seconds. The emitted text has to round-trip through the
    /// DataWindow expression function <c>DateTime(...)</c> that consumes it, and a fractional
    /// component that expression parser rejected would turn a value literal into an expression
    /// error - strictly worse than losing sub-second precision. And no datetime column anywhere in
    /// the repository exercises a sub-second value: the only updatable DataWindow declares its one
    /// temporal column as <c>date</c>. Changing this decision is a one-line change here;
    /// <see cref="Coerce"/> already ACCEPTS an optional fractional component, so only emission is
    /// affected.
    /// </para>
    /// </remarks>
    public const string ExpressionValueFormat = "yyyy-MM-dd HH:mm:ss";

    // ==========================================================================================
    // REGION 2 - THE TYPED NULL
    // Ported from dwnvldatetime.srf:L9-L11, whose entire body is `datetime nvl` / `SetNull(nvl)` /
    // `return nvl`.
    // ==========================================================================================

    /// <summary>
    /// The typed null <c>datetime</c> that the legacy function produces: a
    /// <see cref="Nullable{T}"/> of <see cref="DateTime"/> whose value is absent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The declared type is deliberate on both counts. PowerBuilder <c>datetime</c> maps to
    /// <see cref="DateTime"/> - not to <see cref="DateOnly"/>, which is the mapping for
    /// PowerBuilder <c>date</c> and belongs to the adjacent arm at <c>se_cst_dw.sru:L240-L241</c>,
    /// and not to <see cref="DateTimeOffset"/>, which carries a zone that <c>datetime</c> does
    /// not. A PowerBuilder value-type null is ported as a nullable value type and is NEVER
    /// collapsed to a default: <c>default(DateTime)</c> renders as <c>0001-01-01 00:00:00</c>, so
    /// collapsing would make the emitter produce <c>DateTime('0001-01-01 00:00:00')</c> exactly
    /// where <c>dwvaluetoexp.srf:L55</c> produces <see cref="NullLiteralExpression"/>.
    /// </para>
    /// <para>
    /// Expressed as a property with no backing storage rather than as a field, so that it cannot
    /// be reassigned and reads identically on every call. Ported from
    /// <c>dwnvldatetime.srf:L9-L11</c>.
    /// </para>
    /// </remarks>
    public static DateTime? NullValue => null;

    // ==========================================================================================
    // REGION 3 - THE COLUMN-TYPE TOKEN AND ITS PREDICATE
    // Ported from se_cst_dw.sru:L231 (`choose case Left(dwo.ColType,5)`) and its `case "datet"`
    // arm at :L238. Semantically this is the COL_TYPE_DATETIME category, value 4
    // (n_cst_dwsvc.sru:L30) - recorded in prose only, because Domain/DataWindowServiceHost.cs owns
    // those constants and a second declaration would be a second source of truth.
    // ==========================================================================================
    //
    // The three hazards this region exists to contain are set out in DECISION 5 of the file header
    // and are each reproduced at their point of use below: the truncation is length-safe, the
    // comparison is equality rather than a prefix test, and the comparison is case-sensitive.
    // ==========================================================================================

    /// <summary>
    /// The number of characters the legacy switch truncates a column type to before comparing it,
    /// namely the <c>5</c> of <c>Left(dwo.ColType,5)</c> at <c>se_cst_dw.sru:L231</c>.
    /// </summary>
    /// <remarks>
    /// Private on purpose. It is an implementation detail of <see cref="MatchesColumnType"/>, and
    /// the same literal <c>5</c> also governs the second, subtly different truncation table at
    /// <c>n_cst_dwsvc.sru:L503</c>, which this file does not reproduce.
    /// </remarks>
    private const int ColumnTypeTokenLength = 5;

    /// <summary>
    /// The single column-type token this arm answers to: the five-character truncation of the
    /// PowerBuilder column type <c>datetime</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>"datet"</c> IS NOT A MISSPELLING and must not be "corrected" to <c>"datetime"</c>. The
    /// legacy switch at <c>se_cst_dw.sru:L231</c> compares against <c>Left(dwo.ColType,5)</c>, so
    /// every arm literal in that table is already truncated to five characters and <c>"datet"</c>
    /// is the complete, correct token. A token of <c>"datetime"</c> could never match anything,
    /// because the value it is compared against has already been cut short. Taken from
    /// <c>se_cst_dw.sru:L238</c>, and corroborated by the same token at
    /// <c>n_cst_dwsvc.sru:L510</c>.
    /// </para>
    /// <para>
    /// Behavioural contract, not configuration: it stays compiled in for the same reason
    /// <see cref="FunctionName"/> does.
    /// </para>
    /// </remarks>
    public const string ColumnTypePrefix = "datet";

    /// <summary>
    /// The complete, immutable set of column-type tokens this arm answers to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exactly one element, because <c>se_cst_dw.sru:L238</c> declares exactly one literal on this
    /// arm. It is published as a collection rather than as the bare
    /// <see cref="ColumnTypePrefix"/> so that <c>Domain/ItemChangeProtocol.cs</c> can assemble the
    /// whole switch uniformly across arms: sibling arms in the same table genuinely do carry
    /// several literals each - <c>case "char","char("</c> at <c>se_cst_dw.sru:L232</c>,
    /// <c>case "decim","real","numbe"</c> at <c>:L234</c> and <c>case "long","ulong"</c> at
    /// <c>:L236</c>.
    /// </para>
    /// <para>
    /// <see cref="ImmutableArray{T}"/> rather than an array or a <see cref="List{T}"/> so that no
    /// caller can mutate a shared token table, and rather than a set type because the legacy arm
    /// is an ordered literal list and preserving that order keeps this collection directly
    /// comparable with the oracle line it came from.
    /// </para>
    /// </remarks>
    public static ImmutableArray<string> ColumnTypePrefixes { get; } = [ColumnTypePrefix];

    /// <summary>
    /// Reports whether a DataWindow column type belongs to this arm, reproducing the
    /// <c>case "datet"</c> test of <c>se_cst_dw.sru:L231, :L238</c>.
    /// </summary>
    /// <param name="colType">
    /// The column type as reported by the DataWindow, normally the value of
    /// <c>Describe(".ColType")</c>. May be null, empty or shorter than the token; may equally be a
    /// value that has already been truncated, since truncation here is idempotent.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the first <c>5</c> characters equal <c>"datet"</c> exactly and
    /// case-sensitively; otherwise <see langword="false"/>, including for a null or empty input.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Truncate first, then compare for EQUALITY - never call
    /// <see cref="string.StartsWith(string, StringComparison)"/> with the token. The legacy
    /// <c>choose case</c> compares for equality, which makes its arms mutually exclusive, and this
    /// arm is one half of the pair that makes the distinction load-bearing:
    /// <c>Left("datetime",5)</c> is <c>"datet"</c> and must match only here, while
    /// <c>Left("date",5)</c> is <c>"date"</c> and must match only in <c>DateValidator.cs</c>. A
    /// prefix test collapses the pair, because <c>"datetime".StartsWith("date")</c> is
    /// <see langword="true"/>, and a <c>datetime</c> column would then be coerced by whichever of
    /// the two arms happened to be tested first.
    /// </para>
    /// <para>
    /// The comparison is <see cref="StringComparison.Ordinal"/> and never
    /// <see cref="StringComparison.OrdinalIgnoreCase"/>. PowerScript <c>=</c> on strings, and
    /// therefore <c>choose case</c>, is case-sensitive; the arm literal is lowercase and the
    /// observed <c>Describe(".ColType")</c> value is lowercase. Accepting <c>"DATETIME"</c> would
    /// widen the contract on a guess, where the governing rule is to narrow with a defined outcome
    /// instead. Ordinal also keeps the result independent of the ambient culture, which matters for
    /// the same reason every conversion in this file pins its culture.
    /// </para>
    /// </remarks>
    public static bool MatchesColumnType(string? colType)
    {
        if (colType is null)
        {
            // Not an error and not an exception. A DataWindow whose Describe failed yields no
            // usable column type, and the legacy switch at se_cst_dw.sru:L231-L244 has no
            // `case else` arm, so an unrecognisable type simply matches nothing. Reproducing that
            // here keeps the silence at the point where the legacy is silent.
            return false;
        }

        return string.Equals(LeftToken(colType), ColumnTypePrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// The PowerScript <c>Left(s, n)</c> primitive, applied with
    /// <see cref="ColumnTypeTokenLength"/>.
    /// </summary>
    /// <param name="value">
    /// The string to truncate. Never null at any call site in this file, because
    /// <see cref="MatchesColumnType"/> returns early on null before reaching here.
    /// </param>
    /// <returns>
    /// The first <see cref="ColumnTypeTokenLength"/> characters, or the whole string unchanged when
    /// it is shorter than that.
    /// </returns>
    /// <remarks>
    /// <c>Left</c> IS NOT <c>Substring</c>. PowerScript <c>Left(s,5)</c> truncates a shorter string
    /// harmlessly and returns it whole; <c>s.Substring(0,5)</c> throws
    /// <see cref="ArgumentOutOfRangeException"/> on one. Short inputs genuinely reach here: a
    /// failed <c>Describe</c> returns the empty string, and the adjacent column type <c>"date"</c>
    /// is four characters. The length test below is therefore load-bearing and must not be removed
    /// as redundant. Ported from the <c>Left(dwo.ColType,5)</c> of
    /// <c>se_cst_dw.sru:L231</c>.
    /// </remarks>
    private static string LeftToken(string value)
    {
        return value.Length <= ColumnTypeTokenLength ? value : value[..ColumnTypeTokenLength];
    }

    // ==========================================================================================
    // REGION 4 - THE ARM'S COERCION
    // Ported from se_cst_dw.sru:L239, whose entire body is
    // `SetItem(row,Long(dwo.ID),DateTime(data))`.
    // ==========================================================================================
    //
    // WHAT THE LEGACY LINE DOES, AND JUST AS IMPORTANTLY WHAT IT DOES NOT DO
    // ------------------------------------------------------------------------------------------
    // It converts the edit text and writes it to the buffer. It performs no validity test. Its
    // return value is not captured, compared or logged. The switch that contains it has no
    // `case else` arm at all (se_cst_dw.sru:L244), and the whole switch sits inside
    // `if bEqual then` at :L229 whose only other effect is that the enclosing default arm ends by
    // forcing `rtCode = 2` at :L250. So along this path an uncoercible value produces NO write and
    // NO report of any kind - the failure is completely silent.
    //
    // That silence is reproduced exactly, and it is why there are two members below rather than
    // one. `Coerce` is the legacy path and never throws. `TryCoerce` is an ADDITIVE path with no
    // legacy analogue, offered for the callers that genuinely need to know, and the item-change
    // path must not use it. Turning the silent path into a throwing one would be a behaviour
    // improvement beyond what the technology transition requires, which is forbidden - and it
    // would be a particularly damaging one here, because it would convert a legacy no-op into a
    // fault on a path that fires on every keystroke-completed edit.
    // ==========================================================================================

    /// <summary>
    /// Converts DataWindow edit text to a <c>datetime</c> value exactly as the legacy arm does,
    /// reporting nothing and never throwing.
    /// </summary>
    /// <param name="data">
    /// The edit text handed to the item-change event. May be null, empty, or any text at all,
    /// including text that is not a datetime.
    /// </param>
    /// <returns>
    /// The converted value when the text is coercible; otherwise <see cref="NullValue"/>, meaning
    /// no value was produced.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THIS IS THE MEMBER THE ITEM-CHANGE PATH USES. It reproduces
    /// <c>SetItem(row,Long(dwo.ID),DateTime(data))</c> at <c>se_cst_dw.sru:L239</c>, which is
    /// unchecked: the legacy neither tests the value nor captures a return code, and the enclosing
    /// switch has no <c>case else</c> arm (<c>:L244</c>), so an uncoercible value is silently
    /// ignored. This method therefore NEVER throws, for any input whatsoever - not for null, not
    /// for the empty string, not for arbitrary text, and not for text that looks like a date but
    /// holds impossible components. Do not add a throw, an exception filter or a diagnostic here;
    /// use <see cref="TryCoerce"/> when a caller needs to know, and leave this path silent.
    /// </para>
    /// <para>
    /// A null result is not distinguishable from a legitimately null column value, and that
    /// conflation is inherited from the legacy rather than introduced here: PowerBuilder's
    /// conversion has one result channel and no error channel, so the caller of <c>:L239</c> cannot
    /// tell the two apart either.
    /// </para>
    /// <para>
    /// UNVERIFIED-FROM-REPOSITORY - pin against the behavioural oracle. What the legacy
    /// <c>DateTime(data)</c> yields for an uncoercible string cannot be established from this
    /// repository, because no fixture, test window or document exercises the failure path.
    /// <see cref="NullValue"/> is returned, chosen over a sentinel value on two grounds. It reuses
    /// the one representation of absence this type already publishes, so a caller has exactly one
    /// thing to test. And it cannot fabricate a plausible-looking wrong value: a sentinel such as
    /// <c>1900-01-01</c> is indistinguishable from a user legitimately typing that date, whereas an
    /// absent value is unambiguously "nothing was coerced". The competing candidate, PowerBuilder's
    /// documented invalid-conversion sentinel, must be confirmed or ruled out against the oracle;
    /// changing the answer is a one-line change confined to this method, and
    /// <see cref="TryCoerce"/> already reports the same set of inputs as invalid either way.
    /// </para>
    /// </remarks>
    public static DateTime? Coerce(string? data)
    {
        return TryParseValue(data, out DateTime value) ? value : NullValue;
    }

    /// <summary>
    /// Converts DataWindow edit text to a <c>datetime</c> value and reports the outcome as a
    /// PowerFramework return code.
    /// </summary>
    /// <param name="data">The text to convert. May be null or empty.</param>
    /// <param name="value">
    /// Receives the converted value on success, and <see cref="NullValue"/> on failure. Always
    /// assigned.
    /// </param>
    /// <returns>
    /// <c>RetCode.OK</c> (<c>0</c>, <c>retcode.sru:L39</c>) when the text was coerced;
    /// <c>RetCode.E_INVALID_DATA</c> (<c>-9</c>, <c>retcode.sru:L52</c>) when it was not.
    /// </returns>
    /// <remarks>
    /// <para>
    /// ADDITIVE - THERE IS NO LEGACY ANALOGUE OF THIS MEMBER, AND THE ITEM-CHANGE PATH MUST NOT
    /// USE IT. The legacy arm at <c>se_cst_dw.sru:L239</c> is unchecked and reports nothing, so
    /// reproducing it faithfully is <see cref="Coerce"/>'s job. This overload exists for the
    /// callers created by the decomposition itself - a service boundary that has to turn an
    /// uncoercible payload into a structured error rather than swallowing it - and for the parity
    /// tests, which need to assert that a given input is uncoercible rather than merely observing
    /// that nothing happened. Routing the item-change path through here would change observable
    /// behaviour: values the legacy silently ignores would start producing errors.
    /// </para>
    /// <para>
    /// It returns a <see cref="long"/> return code, NOT a <see cref="bool"/>, so
    /// <c>if (TryCoerce(text, out var v))</c> does not compile. That is deliberate rather than an
    /// oversight: the surrounding codebase reports outcomes in the ported return-code algebra, and
    /// a silent conversion to a boolean is exactly how the algebra's tri-state distinctions get
    /// lost. Only <c>RetCode.OK</c> and <c>RetCode.E_INVALID_DATA</c> are ever returned, so a
    /// caller may test <c>== RetCode.OK</c> for an exact result; the general <c>&gt;= 0</c>
    /// success predicate also holds, and is the right choice for code that stays uniform with the
    /// rest of the algebra.
    /// </para>
    /// <para>
    /// The accepted input set is identical to <see cref="Coerce"/>'s, because both delegate to the
    /// same private conversion. The two can therefore never disagree about whether a given string
    /// is coercible - only about whether they say so.
    /// </para>
    /// </remarks>
    public static long TryCoerce(string? data, out DateTime? value)
    {
        if (TryParseValue(data, out DateTime parsed))
        {
            value = parsed;
            return RetCode.OK;
        }

        // The same absent value Coerce yields, so the two members agree on the result and differ
        // only in whether the failure is reported. See the UNVERIFIED-FROM-REPOSITORY note on
        // Coerce for why absence rather than a sentinel.
        value = NullValue;
        return RetCode.E_INVALID_DATA;
    }

    // ==========================================================================================
    // REGION 5 - THE CONVERSION PRIMITIVES
    // Standing in for PowerBuilder's `String(datetime)` at dwvaluetoexp.srf:L54 and
    // `DateTime(string)` at se_cst_dw.sru:L239. Neither primitive is ported anywhere else in this
    // refactor - the shared Formatting class covers only formatretcode.srf and sprintf.srf - so
    // this file owns both directions for the `datetime` type.
    // ==========================================================================================
    //
    // EVERY CONVERSION PINS ITS CULTURE AND ITS STYLES EXPLICITLY (DECISION 7)
    // ------------------------------------------------------------------------------------------
    // There is deliberately no bare Parse, TryParse, Convert.*, ToString() or interpolated string
    // in this region. Both directions pass CultureInfo.InvariantCulture, and parsing additionally
    // pins DateTimeStyles.None, which is what rules out every implicit behaviour that could shift
    // or reinterpret a value:
    //
    //     no AssumeLocal / AssumeUniversal   nothing infers a zone for a value that has none
    //     no AdjustToUniversal               nothing converts the value while reading it
    //     no AllowWhiteSpaces                surrounding whitespace is rejected rather than
    //                                        quietly trimmed, which keeps the accepted set exactly
    //                                        the set enumerated below
    //
    // Verified by measurement rather than assumed: with the ambient culture set to Invariant,
    // de-DE, en-US and th-TH in turn - the last of which has a non-Gregorian default calendar that
    // would render the year 2020 as 2563 - every parse and every format below produces
    // byte-identical output. That is the property a characterization comparison depends on, and it
    // is why the pin is explicit at every single call rather than assumed from a host setting.
    // ==========================================================================================

    /// <summary>
    /// The ordered set of textual forms <see cref="Coerce"/> and <see cref="TryCoerce"/> accept.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first two entries are built from <see cref="ExpressionValueFormat"/> rather than
    /// retyped, so the canonical emitted form is by construction always accepted back - the
    /// round-trip property cannot be broken by editing one of the two in isolation. The first entry
    /// appends <c>.FFFFFFF</c>, which makes both the fractional component AND its decimal point
    /// optional; that was verified by measurement, including for the degenerate input ending in a
    /// bare <c>'.'</c>.
    /// </para>
    /// <para>
    /// Accepting a fractional component while <see cref="ExpressionValueFormat"/> does not emit one
    /// is deliberate asymmetry, not an oversight. Emission has one job - reproduce
    /// <c>String(datetime)</c> - whereas acceptance sits on the item-change path, where rejecting a
    /// value PowerBuilder would have accepted is observable: under the silent-failure semantics of
    /// <see cref="Coerce"/> it shows up as an edit that vanishes with no message. The narrower
    /// choice is the riskier one here, so the fractional forms are accepted.
    /// </para>
    /// <para>
    /// A private cached array rather than an inline literal at the call site, so no allocation
    /// happens per conversion and no caller can mutate the accepted set.
    /// </para>
    /// </remarks>
    private static readonly string[] AcceptedValueFormats =
    [
        ExpressionValueFormat + ".FFFFFFF",
        ExpressionValueFormat,
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd",
    ];

    /// <summary>
    /// Renders a <c>datetime</c> as the text that goes inside <c>DateTime('...')</c>, standing in
    /// for PowerBuilder's no-format <c>String(datetime)</c> at <c>dwvaluetoexp.srf:L54</c>.
    /// </summary>
    /// <param name="value">The value to render.</param>
    /// <returns>
    /// The value rendered with <see cref="ExpressionValueFormat"/> under
    /// <see cref="CultureInfo.InvariantCulture"/>, zero-padded in every field.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Renders the value only. It does NOT wrap the result in <c>DateTime('...')</c> and it does
    /// not substitute <see cref="NullLiteralExpression"/>, because both of those belong to
    /// <c>Validators/ValueToExpression.cs</c>, which ports the whole of
    /// <c>dwvaluetoexp.srf:L53-L57</c> and decides between a literal and the sentinel by testing
    /// the value for absence. Keeping the split here means the parameter can stay non-nullable and
    /// this method has exactly one behaviour.
    /// </para>
    /// <para>
    /// The <see cref="DateTime.Kind"/> of the argument does not affect the result, and no
    /// normalisation of any kind is applied - a <see cref="DateTimeKind.Utc"/> value and a
    /// <see cref="DateTimeKind.Local"/> value carrying identical components render identically
    /// (measured). The emitted text therefore carries no zone marker, which is correct for a
    /// zone-less PowerBuilder <c>datetime</c> and is why a round trip through
    /// <see cref="Coerce"/> returns a <see cref="DateTimeKind.Unspecified"/> value with every
    /// component and every tick preserved and nothing shifted. See DECISION 4 in the file header.
    /// </para>
    /// </remarks>
    public static string FormatExpressionValue(DateTime value)
    {
        return value.ToString(ExpressionValueFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The single text-to-value conversion both <see cref="Coerce"/> and <see cref="TryCoerce"/>
    /// delegate to, standing in for PowerBuilder's <c>DateTime(string)</c>.
    /// </summary>
    /// <param name="data">The text to convert. May be null.</param>
    /// <param name="value">
    /// Receives the converted value on success and <c>default</c> on failure. Callers in this file
    /// never surface the failure value; they substitute <see cref="NullValue"/> instead, precisely
    /// so that <c>default(DateTime)</c> can never escape as if it were data.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the text matched one of <see cref="AcceptedValueFormats"/>;
    /// otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// Never throws, for any input. That is a hard requirement of the legacy semantics rather than
    /// a convenience, and it is achieved by construction: <c>DateTime.TryParseExact</c> - the
    /// multi-format overload taking a format array, an <see cref="IFormatProvider"/> and a
    /// <see cref="DateTimeStyles"/> - reports failure through its return value for malformed text,
    /// for impossible component values and for a null input alike. The explicit null branch below
    /// therefore adds no capability; it is kept because "never throws for null" is an asserted
    /// guarantee of the public members and this is where a reader looks to confirm it.
    /// </remarks>
    private static bool TryParseValue(string? data, out DateTime value)
    {
        if (data is null)
        {
            value = default;
            return false;
        }

        return DateTime.TryParseExact(
            data,
            AcceptedValueFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out value);
    }
}
