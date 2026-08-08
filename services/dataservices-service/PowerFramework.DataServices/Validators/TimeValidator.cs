// ==============================================================================================
//  TimeValidator - the `time` arm of the DataWindow item-change coercion table, and the port of
//  the legacy typed-null producer `dwnvltime`.
//  --------------------------------------------------------------------------------------------
//  PORTED FROM
//      ws_objects/pfw.datawindow.services.pbl.src/dwnvltime.srf              (13 lines, in full)
//          L2   global type dwnvltime from function_object
//          L6   global function time dwnvltime ()
//          L9   time nvl
//          L10  SetNull(nvl)
//          L11  return nvl
//      ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L242-L243
//          the "time" arm of the coercion switch that opens at :L231 and closes at :L244
//
//  READ AS SPECIFICATION, NOT PORTED HERE
//      dwvaluetoexp.srf:L65-L69              the emitted spelling this file publishes. :L66 is
//                                            "Time('" + String(val) + "')" and :L67 is
//                                            if IsNull(sVal) then sVal = "dwNvlTime()"
//      n_cst_dwsvc.sru:L26-L32               the column-type catalogue, in which COL_TYPE_TIME = 6
//      n_cst_dwsvc.sru:L503-L518             _of_convertcoltype, a SECOND Left(colType,5) dispatch
//                                            whose "time" arm at :L514-L515 agrees with ours
//      n_cst_dwsvc_columnsort.sru:L322-L323  the only hand-authored time literal anywhere in the
//                                            repository. It corroborates the emitted format; see
//                                            DECISION 6
//      retcode.sru:L39, :L52                 OK = 0 and E_INVALID_DATA = -9, the two codes the
//                                            additive Try path returns
//
//  ORACLE STATUS
//      Every legacy file named above is READ ONLY. The PowerBuilder tree is the behavioural oracle
//      for parity testing and is never an edit target, so it is the only thing that can adjudicate
//      a disagreement about anything below. Every behavioural claim in this file therefore carries
//      the `<file>:L<line>` locator it was taken from, and the two claims the repository CANNOT
//      adjudicate are marked UNVERIFIED-FROM-REPOSITORY rather than asserted.
//
//  THE MOST IMPORTANT THING TO UNDERSTAND BEFORE EDITING: THIS IS NOT A VALIDATOR
//  --------------------------------------------------------------------------------------------
//  The folder is called Validators/ and this type is called TimeValidator, and neither name is
//  accurate. `dwnvltime` validates nothing. Its entire body declares a typed local, sets it to
//  null, and returns it [dwnvltime.srf:L9-L11]. It exists to be named from INSIDE a DataWindow
//  expression string, as the null branch of a generated expression, which is why the only place
//  its name appears in the whole legacy estate is a string literal.
//
//  That was measured, not assumed. A case-insensitive search for `dwnvltime` across all 544
//  PowerBuilder objects - every .sru, .srf, .srw, .srd, .srs, .sra and .srm - returns exactly two
//  files: dwnvltime.srf's own four self-declaration lines, and the sentinel literal at
//  dwvaluetoexp.srf:L67. There is not one PowerScript CALL SITE. The observable contract of the
//  ported function is therefore the SPELLING it publishes and nothing else, which is why
//  FunctionName and NullLiteralExpression below are byte-exact contract rather than convenience.
//
//  The names are deliberately NOT corrected. The transformation plan fixes this file's path, and
//  a rename would put the type somewhere no consumer is looking for it. The inaccuracy is
//  recorded here instead.
//
//  WHAT THIS FILE OWNS, AND WHAT IT DELIBERATELY DOES NOT
//  --------------------------------------------------------------------------------------------
//  OWNS      the published `dwNvlTime` spelling; the typed null; the canonical invariant text form
//            of a time value; the single column-type token "time"; the predicate that decides
//            whether a column type belongs to this arm; and the two coercions of edit text into a
//            time value, one legacy-faithful and one additive.
//
//  DOES NOT  own the `Time('...')` wrapper - Expressions/ValueToExpression.cs composes that from
//            CanonicalFormat and NullLiteralExpression, so the wrapper lives at exactly one site.
//            Own the coercion SWITCH - Domain/ItemChangeProtocol.cs ports se_cst_dw.sru:L182-L253
//            and dispatches to this arm. Own the COL_TYPE_* constants - Domain/
//            DataWindowServiceHost.cs owns them, and a second copy would be a second source of
//            truth, so the correspondence to COL_TYPE_TIME = 6 [n_cst_dwsvc.sru:L26-L32] is noted
//            in prose here and nowhere expressed as a declaration. Own the invalid-value dialog -
//            se_cst_dw.sru:L357 raises MessageBoxEx with an I18N(CAT_DWSVC, ...) fallback, and the
//            structured-error half of that belongs to Domain while the rendering half is deferred
//            to DesignSystem, which this phase must not touch even as a stub.
//
//  This file has NO dependency on any sibling folder. It references no type from Domain/ or
//  Expressions/, which is precisely why it can be authored before either of them.
//
//  DECISION 1 - EVERY MEMBER IS STATIC, PURE AND DETERMINISTIC
//  --------------------------------------------------------------------------------------------
//  `dwnvltime.srf` and `dwvaluetoexp.srf` are PowerBuilder global function objects, and a global
//  function maps to a static method on a role-named static class. There is no instance, no
//  interface and no dependency injection here, matching Predicates.IsSucceeded, Bits.BitAnd and
//  Formatting.Sprintf in the shared kernel.
//
//  Nothing below performs I/O, logs, holds mutable state, allocates a lock, or reads a clock. The
//  clock prohibition is not stylistic: paired legacy and target characterization recordings are
//  the parity evidence for this migration, every clock read is a seam that must be injectable, and
//  a hidden DateTime.Now here would make a recording unreproducible. There is deliberately no
//  "default to the current time" behaviour anywhere in this file. Consequently every member is
//  safe to call concurrently from any number of threads.
//
//  DECISION 2 - PowerBuilder `time` MAPS TO TimeOnly. NOT TimeSpan, AND NOT DateTime.
//  --------------------------------------------------------------------------------------------
//  dwnvltime.srf:L6 declares the return type as PowerBuilder `time`, a time of day. TimeOnly is
//  the .NET type with the same domain, and the transformation plan's type table pins it.
//
//  TimeSpan was considered and REJECTED: it models a duration, so it admits negative values and
//  values beyond 24 hours. Adopting it would widen the contract to states the legacy type cannot
//  represent, and it would silently change the overload set that Expressions/ValueToExpression.cs
//  resolves against, because the legacy overload list at dwvaluetoexp.srf:L6-L14 distinguishes
//  `time` from `datetime` and from `date` as three separate types.
//
//  DateTime was considered and REJECTED for the same widening reason in the other direction: it
//  carries a date component that a PowerBuilder `time` does not have, and dwvaluetoexp.srf already
//  has a distinct `datetime` overload at :L53-L57 that owns that case.
//
//  DECISION 3 - THE NULL IS A NULLABLE VALUE TYPE, NEVER A DEFAULT
//  --------------------------------------------------------------------------------------------
//  PowerBuilder has null for value types and dwnvltime.srf:L10 uses it deliberately. The port is
//  `TimeOnly?`, and a null is NEVER collapsed to `default(TimeOnly)`.
//
//  The reason is a concrete corruption, not a principle. `default(TimeOnly)` is 00:00:00. Collapse
//  a null time to it and the emitter at dwvaluetoexp.srf:L66-L67 would produce Time('00:00:00')
//  where the legacy produces dwNvlTime() - and because midnight is a perfectly legitimate time of
//  day, the corrupted expression is indistinguishable from one carrying real data. Nothing
//  downstream could detect it and no assertion on row counts would fail.
//
//  That midnight collision recurs one level down, in the uncoercible-text fallback, and is called
//  out again at DECISION 11 where it is a REPRODUCED legacy behaviour rather than a defect to
//  avoid. The two cases must not be conflated: a null time is never midnight, while uncoercible
//  TEXT does become midnight.
//
//  DECISION 4 - IDENTIFIERS ARE PascalCase. THE LEGACY SPELLING LIVES IN STRING VALUES.
//  --------------------------------------------------------------------------------------------
//  No member below is named `dwNvlTime`. The refactor does preserve legacy SCREAMING_SNAKE
//  constant identifier spellings verbatim in the handful of constant catalogues whose identifiers
//  travel in serialized payloads, and the repository-root .editorconfig scopes its CA1707 and
//  IDE1006 suppressions to exactly those files. This file is deliberately NOT one of them, and
//  Directory.Build.props sets TreatWarningsAsErrors, so a camelCase or underscore-bearing public
//  member declared here would be a build ERROR rather than advice.
//
//  Nothing is lost by that, because the legacy spelling is DATA here, not an identifier: it is the
//  value of FunctionName, and that value is what reaches a DataWindow expression string.
// ==============================================================================================

using System.Collections.Frozen;
using System.Globalization;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.DataServices.Validators;

/// <summary>
/// The <c>time</c> arm of the DataWindow item-change coercion table, together with the ported
/// typed-null producer and the canonical invariant text form of a time-of-day value.
/// </summary>
/// <remarks>
/// <para>
/// Ported from <c>ws_objects/pfw.datawindow.services.pbl.src/dwnvltime.srf</c> and from the
/// <c>case "time"</c> arm at <c>se_cst_dw.sru:L242-L243</c>. Despite the type name this class
/// performs no validation; the legacy object it is named for declares a typed local, sets it to
/// null and returns it. See the file header for why the name is not corrected.
/// </para>
/// <para>
/// Every member is a pure, deterministic static function of its arguments. Nothing here performs
/// I/O, logs, reads a clock, consults the ambient culture, or holds mutable state, so every member
/// is safe to call concurrently from any number of threads and every member is reproducible inside
/// a characterization recording.
/// </para>
/// <para>
/// Every conversion in both directions pins <see cref="CultureInfo.InvariantCulture"/> explicitly,
/// and every parse additionally pins <see cref="DateTimeStyles"/>. No member calls a bare
/// <c>Parse</c>, <c>TryParse</c>, <c>Convert</c> or <c>ToString()</c>, and no member uses string
/// interpolation, because a time of day is exactly where the ambient culture does the most damage:
/// the AM/PM designator, the time separator and the fractional-second decimal separator are all
/// culture-derived.
/// </para>
/// </remarks>
public static class TimeValidator
{
    // ==========================================================================================
    // REGION 1 - THE PUBLISHED SPELLINGS
    // Source: dwvaluetoexp.srf:L65-L69 (the `time` overload)
    // ==========================================================================================
    //
    // DECISION 5 - THESE THREE STRINGS ARE BYTE-EXACT CONTRACT, AND THEY ARE SINGLE-SOURCED
    // ------------------------------------------------------------------------------------------
    // Two independent components have to agree on the spelling `dwNvlTime`, and they agree by
    // both reading it from here:
    //
    //     Expressions/ValueToExpression.cs           EMITS it, porting dwvaluetoexp.srf:L67
    //     Expressions/DataWindowExpressionEvaluator  REGISTERS it in its expression function table
    //
    // Getting that agreement structurally rather than by convention matters because of how the
    // failure would present. n_cst_dwsvc.sru:L217 shows _of_evaluate handing the composed
    // expression text to Describe("Evaluate(...)"), and n_cst_dwsvc_columnexp.sru:L2391 shows the
    // caller detecting failure as the returned value "?" or "!". A one-character drift between the
    // registered name and the emitted name is therefore a RUNTIME expression error carried back as
    // an opaque single character - never a compile error, and never a stack trace. Reading both
    // from one constant removes that failure mode entirely at zero observable cost, and it is
    // recorded here as an internal structuring choice with no behavioural effect.
    //
    // NullLiteralExpression is written as a concatenation of FunctionName rather than as a second
    // literal for the same reason. Both are compile-time constants, so the compiled value is
    // identical to the oracle's literal at dwvaluetoexp.srf:L67, character for character:
    // `dwNvlTime()`. The unit tests assert that value directly against the oracle so the
    // concatenation can never drift from it either.
    //
    // AND THEY STAY COMPILED IN. This file holds three string constants and no secret of any kind,
    // so the no-hardcoded-values constraint is satisfied by there being nothing here it governs -
    // that constraint targets CONFIGURATION, which is bound through an options type from the
    // environment. The spelling, the format and the column-type token are the opposite of
    // configuration: they are behavioural contract, fixed by the read-only oracle, and identical in
    // every environment. Moving any of them into appsettings.json or an environment variable would
    // make parity a deployment-time variable and is forbidden.

    /// <summary>
    /// The DataWindow expression function name that produces a null time, spelled exactly as the
    /// legacy emits it: <c>dwNvlTime</c>.
    /// </summary>
    /// <remarks>
    /// Taken from the literal at <c>dwvaluetoexp.srf:L67</c>. This spelling is the ONLY observable
    /// contract of the ported <c>dwnvltime</c> function object, because the legacy estate contains
    /// no PowerScript call site for it. Both the expression emitter and the expression evaluator's
    /// function table must read the name from here rather than retyping it; a one-character
    /// difference between them surfaces at run time as an opaque <c>"?"</c> or <c>"!"</c> from
    /// <c>Describe("Evaluate(...)")</c> and never as a compile error.
    /// </remarks>
    public const string FunctionName = "dwNvlTime";

    /// <summary>
    /// The complete DataWindow expression the legacy substitutes for a null time:
    /// <c>dwNvlTime()</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reproduces <c>dwvaluetoexp.srf:L67</c> verbatim. Note the shape carefully: for a null time
    /// the legacy replaces the WHOLE expression, so this value is not wrapped in
    /// <c>Time('...')</c>. For a non-null time the legacy instead composes
    /// <c>Time('</c> + the invariant text + <c>')</c> at <c>dwvaluetoexp.srf:L66</c>. An emitter
    /// therefore selects between this constant and that composition, and it must never wrap this
    /// one.
    /// </para>
    /// <para>
    /// Declared as a concatenation of <see cref="FunctionName"/> so the two cannot drift apart.
    /// Both are compile-time constants, so the compiled value equals the oracle literal exactly.
    /// </para>
    /// </remarks>
    public const string NullLiteralExpression = FunctionName + "()";

    // DECISION 6 - THE CANONICAL FORMAT IS INVARIANT, 24-HOUR, ZERO-PADDED, WHOLE-SECOND
    // ------------------------------------------------------------------------------------------
    // The legacy inner text at dwvaluetoexp.srf:L66 is PowerBuilder's no-format String(time)
    // rendering, and whatever it produces has to be accepted by the DataWindow expression function
    // Time(...) that consumes it. Two parts of that shape are on different evidential footings and
    // are deliberately reported differently.
    //
    // CORROBORATED, from the repository itself. The transformation plan records that no expression
    // in the tree shows a formatted time literal. That turned out to be wrong, and the
    // counter-example is in this very library:
    //
    //     n_cst_dwsvc_columnsort.sru:L322-L323
    //         case COL_TYPE_TIME
    //             sClause = "if(IsNull(" + colName + "),Time('00:00:00')," + colName + ")"
    //
    // sitting beside DateTime('1900-01-01') at :L319 and Date('1900-01-01') at :L321. That is a
    // hand-authored literal, written by the framework's own authors, fed to the same Time(...)
    // expression function, and its shape is unambiguous: 24-hour, colon-separated, zero-padded to
    // two digits in every field, no AM/PM designator. `HH:mm:ss` reproduces it exactly.
    //
    // UNVERIFIED-FROM-REPOSITORY - pin against the behavioural oracle. Whether PowerBuilder's
    // no-format String(time) appends fractional seconds when the value carries a sub-second
    // component cannot be settled from anything in this checkout. The evidence available is
    // entirely negative: the single literal above carries no fraction, `grep "type=time"` across
    // all twelve DataWindow definitions in the repository matches NOTHING so no fixture exercises
    // a time column at all, and none of the five legacy documents mentions a time format. The
    // decision taken is to emit NO fractional seconds, which means Format truncates any sub-second
    // component a TimeOnly happens to carry. Record this as a characterization item alongside
    // DECISION 11's fallback value; docs/PARITY.md and characterization/ are where it lands.
    //
    // REJECTED, and it must stay rejected: any 12-hour form, and any form carrying an AM/PM
    // designator. The designator string is culture-derived, so emitting one would make the output
    // depend on the host locale - measured on this toolchain, formatting 13:05:09 with the
    // culture-sensitive short-time specifier yields "13:05" under the invariant culture and
    // "1:05 PM" under en-US. Widening the contract on a guess is exactly what the transformation
    // plan forbids; narrowing it with a defined error is what it requires.
    //
    // `HH` rather than `hh` is load-bearing for the same reason: `hh` is the 12-hour hour, so it
    // renders 13:05:09 as "01:05:09" and loses the afternoon entirely once the designator is gone.

    /// <summary>
    /// The canonical invariant format string for the text that goes inside a
    /// <c>Time('...')</c> DataWindow expression: 24-hour, colon-separated, zero-padded to two
    /// digits per field, with no fractional seconds and no AM/PM designator.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Corroborated by the framework's own hand-authored literal <c>Time('00:00:00')</c> at
    /// <c>n_cst_dwsvc_columnsort.sru:L323</c>, which is fed to the same DataWindow expression
    /// function that consumes the output of <c>dwvaluetoexp.srf:L66</c>.
    /// </para>
    /// <para>
    /// Consumed by <see cref="Format(TimeOnly)"/> and by the expression emitter, so the emitter
    /// carries no format string of its own. Use <see cref="Format(TimeOnly)"/> in preference to
    /// this constant wherever possible, because <see cref="Format(TimeOnly)"/> also pins the
    /// culture; a caller that passes this constant to a formatting overload without an explicit
    /// <see cref="CultureInfo.InvariantCulture"/> reintroduces the hazard this constant exists to
    /// remove.
    /// </para>
    /// <para>
    /// Whether the legacy appends fractional seconds for a sub-second value is UNVERIFIED from this
    /// repository and is a characterization item; the choice recorded here is to emit none, so
    /// formatting truncates any sub-second component. Never substitute a 12-hour form or a form
    /// carrying a designator: both are culture-derived and would widen the contract.
    /// </para>
    /// </remarks>
    public const string CanonicalFormat = "HH:mm:ss";

    // ==========================================================================================
    // REGION 2 - THE TYPED NULL
    // Source: dwnvltime.srf:L6, L9-L11 (the whole object)
    // ==========================================================================================
    //
    // DECISION 7 - THE PORT IS A PARAMETERLESS STATIC METHOD, MIRRORING THE THREE LEGACY LINES
    // ------------------------------------------------------------------------------------------
    // The oracle is a global function object, so the port is a static method rather than a property
    // or a field. That keeps the shape of the surface aligned with dwvaluetoexp.srf's other
    // producers and with the shared kernel's own global-function ports.
    //
    // The body is written as three statements rather than as `return null` so that each legacy line
    // has a visible counterpart:
    //
    //     dwnvltime.srf:L9    time nvl          ->  declare a local of the mapped type
    //     dwnvltime.srf:L10   SetNull(nvl)      ->  assign the typed null
    //     dwnvltime.srf:L11   return nvl        ->  return it
    //
    // There is no clock read, no default value and no fallback here. This method returns the null,
    // always, unconditionally - which is the entire behaviour of the object it ports.

    /// <summary>
    /// Returns the typed null time that the legacy <c>dwnvltime</c> function object produces.
    /// </summary>
    /// <returns>
    /// Always <see langword="null"/>. The declared type is a nullable
    /// <see cref="TimeOnly"/>, deliberately not a nullable <see cref="TimeSpan"/> and not a
    /// nullable <see cref="DateTime"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Ports <c>dwnvltime.srf:L6</c> and <c>dwnvltime.srf:L9-L11</c> in full. The legacy body
    /// declares a <c>time</c> local, calls <c>SetNull</c> on it and returns it; there is nothing
    /// else in the object.
    /// </para>
    /// <para>
    /// The null is never collapsed to <c>default(TimeOnly)</c>. That value is midnight, which is a
    /// legitimate time of day, so collapsing would make the emitter produce
    /// <c>Time('00:00:00')</c> where the legacy produces
    /// <see cref="NullLiteralExpression"/> - a corruption that is indistinguishable from real data.
    /// </para>
    /// <para>
    /// This method reads no clock and has no configurable default, so it is safe inside a
    /// characterization recording.
    /// </para>
    /// </remarks>
    public static TimeOnly? NullValue()
    {
        // dwnvltime.srf:L9-L10 - `time nvl` followed by `SetNull(nvl)`.
        TimeOnly? nvl = null;

        // dwnvltime.srf:L11 - `return nvl`.
        return nvl;
    }

    // ==========================================================================================
    // REGION 3 - THE COLUMN-TYPE ARM
    // Source: se_cst_dw.sru:L231 (the discriminator) and :L242 (this arm's literal)
    // ==========================================================================================
    //
    // WHAT THE LEGACY ACTUALLY WRITES, quoted so the three hazards below are checkable:
    //
    //     se_cst_dw.sru:L231      choose case Left(dwo.ColType,5)
    //     se_cst_dw.sru:L232-L233     case "char","char("    SetItem(row,Long(dwo.ID),data)
    //     se_cst_dw.sru:L234-L235     case "decim","real","numbe"
    //     se_cst_dw.sru:L236-L237     case "long","ulong"
    //     se_cst_dw.sru:L238-L239     case "datet"
    //     se_cst_dw.sru:L240-L241     case "date"
    //     se_cst_dw.sru:L242-L243     case "time"            SetItem(row,Long(dwo.ID),Time(data))
    //     se_cst_dw.sru:L244      end choose
    //
    // This arm is the LAST one and `end choose` follows it immediately: there is NO `case else` in
    // this switch. That absence is behaviour, and DECISION 11 reproduces it.
    //
    // The switch itself belongs to Domain/ItemChangeProtocol.cs. This region supplies only the
    // token and the predicate, so the switch has exactly one place to ask.
    //
    // SEMANTIC ALIGNMENT, STATED IN PROSE AND NOWHERE DECLARED. This arm corresponds to
    // COL_TYPE_TIME = 6 in the column-type catalogue at n_cst_dwsvc.sru:L26-L32, and the second
    // Left(colType,5) dispatch at n_cst_dwsvc.sru:L503-L518 maps the same "time" token onto it at
    // :L514-L515, so the two legacy tables agree. The constant is NOT redeclared here:
    // Domain/DataWindowServiceHost.cs owns the COL_TYPE_* catalogue and a copy would be a second
    // source of truth. Note also a real asymmetry between the two legacy dispatches, worth knowing
    // before reading DECISION 11: _of_convertcoltype DOES have a `case else` (:L516-L517, yielding
    // COL_TYPE_UNKNOWN) while the item-change switch does not.
    //
    // DECISION 8 - HAZARD H2: Left(s,5) IS A SAFE TRUNCATION, NOT Substring(0,5)
    // ------------------------------------------------------------------------------------------
    // PowerBuilder's Left truncates: Left("time",5) returns "time", and Left("",5) returns "".
    // C#'s Substring(0,5) THROWS ArgumentOutOfRangeException on anything shorter than five
    // characters. The token this arm matches is "time", which is FOUR characters, and a failed
    // Describe(".ColType") returns the empty string - so a mechanical Substring port would crash on
    // both the ordinary success case and the ordinary failure case. TruncateToPrefix below is
    // length-checked, and the unit tests drive "", "t" and "time" through the predicate to keep it
    // that way.
    //
    // DECISION 9 - HAZARD H6: MATCH BY EXACT EQUALITY ON THE TRUNCATED STRING, NEVER StartsWith
    // ------------------------------------------------------------------------------------------
    // `choose case` compares for equality, so the arms are mutually exclusive. StartsWith is not
    // merely a stylistic difference here, it is a data-destroying defect, and "timestamp" is the
    // proof:
    //
    //     Left("timestamp",5)            = "times"  ->  matches NO arm  ->  no write, no report
    //     "timestamp".StartsWith("time") = true     ->  would coerce to a time of day, destroying
    //                                                   the date component of the stored value
    //
    // So under the legacy a timestamp-typed column silently receives no write at all, and a
    // StartsWith port would instead write a truncated value that looks plausible. Truncate to five
    // characters first, then compare for equality. The unit tests assert the "timestamp" case
    // explicitly and name this trap, so a later edit cannot quietly reintroduce it.
    //
    // DECISION 10 - HAZARD H4: THE COMPARISON IS ORDINAL AND CASE-SENSITIVE
    // ------------------------------------------------------------------------------------------
    // PowerScript `=` on strings, and therefore `choose case`, is case-SENSITIVE. The arm literal
    // at se_cst_dw.sru:L242 is lowercase, and Describe(".ColType") is observed to return lowercase.
    // StringComparison.Ordinal is used, never OrdinalIgnoreCase and never a culture-sensitive
    // comparison, so "TIME" does NOT match. Accepting "TIME" would widen the contract on a guess
    // where the requirement is to narrow it with a defined outcome, and the unit tests assert the
    // uppercase rejection so no case-insensitivity can creep back in.

    /// <summary>
    /// The column-type prefix token this arm matches, taken verbatim from the <c>case "time"</c>
    /// literal at <c>se_cst_dw.sru:L242</c>.
    /// </summary>
    /// <remarks>
    /// Four characters, which is shorter than the five-character discriminator width the legacy
    /// truncates to. That is not an inconsistency: <c>Left("time",5)</c> returns <c>"time"</c>
    /// unchanged, which is why the truncation must be length-safe. Prefer
    /// <see cref="MatchesColumnType(string?)"/> over comparing against this constant directly, so
    /// the truncation and the ordinal comparison are applied for you.
    /// </remarks>
    public const string ColumnTypeToken = "time";

    // The discriminator width from `Left(dwo.ColType,5)` at se_cst_dw.sru:L231, also used by the
    // parallel dispatch at n_cst_dwsvc.sru:L503. Deliberately PRIVATE: the switch that needs this
    // width is Domain/ItemChangeProtocol.cs's, and publishing a second copy of it from a
    // type-specific class would invite exactly the divergence a single source of truth prevents.
    // MatchesColumnType applies it internally, so no caller ever needs the number.
    private const int ColumnTypePrefixLength = 5;

    /// <summary>
    /// The immutable set of column-type prefix tokens this arm claims. Contains exactly one
    /// element, <see cref="ColumnTypeToken"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Published as a set rather than a list so that membership is answered by the set's own
    /// <see cref="StringComparer.Ordinal"/> comparer, which makes the case sensitivity required by
    /// <c>choose case</c> a property of the collection rather than of each call site.
    /// </para>
    /// <para>
    /// The single element mirrors <c>se_cst_dw.sru:L242</c>, which lists one literal, unlike the
    /// sibling arms at <c>:L232</c>, <c>:L234</c> and <c>:L236</c> which list two or three each.
    /// Membership in this set is a prefix test on an ALREADY-TRUNCATED string; callers holding an
    /// untruncated column type should use <see cref="MatchesColumnType(string?)"/> instead.
    /// </para>
    /// </remarks>
    public static IReadOnlySet<string> ColumnTypeTokens { get; } =
        FrozenSet.ToFrozenSet(new[] { ColumnTypeToken }, StringComparer.Ordinal);

    /// <summary>
    /// Answers whether a DataWindow column type belongs to this coercion arm, reproducing the
    /// <c>case "time"</c> test at <c>se_cst_dw.sru:L242</c> against the
    /// <c>Left(dwo.ColType,5)</c> discriminator at <c>se_cst_dw.sru:L231</c>.
    /// </summary>
    /// <param name="colType">
    /// The raw column type as <c>Describe(".ColType")</c> reports it, untruncated. May be
    /// <see langword="null"/> or empty; neither throws.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the first five characters of
    /// <paramref name="colType"/> are exactly <see cref="ColumnTypeToken"/>, compared ordinally;
    /// otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The truncation is length-safe, so a value shorter than the five-character discriminator
    /// width is compared as-is rather than throwing. That is the ordinary case here, not an edge
    /// case: the token itself is four characters long, and a failed <c>Describe</c> yields the
    /// empty string.
    /// </para>
    /// <para>
    /// The comparison is EXACT EQUALITY on the truncated string, never a prefix test on the whole
    /// string. <c>"timestamp"</c> therefore returns <see langword="false"/>, because it truncates
    /// to <c>"times"</c>, which matches no arm of the legacy switch at all. A
    /// <c>StartsWith</c>-based implementation would return <see langword="true"/> for it and
    /// destroy the date component of a timestamp value.
    /// </para>
    /// <para>
    /// The comparison is ordinal and case-sensitive, matching PowerScript <c>choose case</c>, so
    /// <c>"TIME"</c> returns <see langword="false"/>. A <see langword="null"/> column type also
    /// returns <see langword="false"/>, reproducing the legacy outcome: <c>Left</c> of a null is
    /// null, a null matches no <c>case</c> arm, and the switch has no <c>case else</c>, so nothing
    /// is written and nothing is reported.
    /// </para>
    /// </remarks>
    public static bool MatchesColumnType(string? colType)
    {
        if (colType is null)
        {
            // PowerScript propagates null through Left(), and a null discriminator matches no
            // `case` arm. With no `case else` at se_cst_dw.sru:L244, the legacy outcome is silence.
            return false;
        }

        string prefix = TruncateToPrefix(colType);

        // Exact equality, ordinal, case-sensitive. See DECISION 9 and DECISION 10.
        return string.Equals(prefix, ColumnTypeToken, StringComparison.Ordinal);
    }

    // The managed equivalent of PowerBuilder `Left(value, ColumnTypePrefixLength)`. Length-safe by
    // construction: a string at or below the discriminator width is returned unchanged rather than
    // indexed past its end, which is what makes the "" and "time" cases work. See DECISION 8.
    private static string TruncateToPrefix(string value)
    {
        if (value.Length <= ColumnTypePrefixLength)
        {
            return value;
        }

        return value[..ColumnTypePrefixLength];
    }

    // ==========================================================================================
    // REGION 4 - COERCING EDIT TEXT INTO A TIME VALUE
    // Source: se_cst_dw.sru:L243 - SetItem(row,Long(dwo.ID),Time(data))
    // ==========================================================================================
    //
    // DECISION 11 - HAZARD H3: THE LEGACY COERCION IS UNCHECKED, REPORTS NOTHING, NEVER THROWS
    // ------------------------------------------------------------------------------------------
    // Read se_cst_dw.sru:L243 closely. SetItem's return value is DISCARDED, there is no validity
    // test on `data` before the conversion, and there is no return-code check after the write. Two
    // separate silences follow, and both are reproduced:
    //
    //     an UNRECOGNISED column-type prefix   the switch at :L231-L244 has no `case else`, so no
    //                                          write happens and nothing is reported. That silence
    //                                          is MatchesColumnType returning false.
    //     UNCOERCIBLE TEXT in a recognised arm the conversion does not fail loudly, so a value is
    //                                          still written. That silence is Coerce's fallback.
    //
    // Coerce therefore NEVER throws, for any input, including null. Adding a throw would be a
    // regression dressed as robustness, and behaviour preservation forbids it - the legacy's error
    // ergonomics are as much a part of the contract as its happy path.
    //
    // UNVERIFIED-FROM-REPOSITORY - pin against the behavioural oracle. The value PowerBuilder's
    // Time(string) conversion yields for an unparseable string cannot be settled from anything in
    // this checkout: there is no fixture with a time column, no document naming the behaviour, and
    // no expression that exercises it. The decision taken is 00:00:00, and it is exposed as
    // UncoercibleFallback rather than buried as a literal so the characterization author can pin it
    // by name. The reasoning behind the choice, offered as reasoning and not as proof: PowerBuilder
    // scalar conversion functions yield the type's zero value rather than a null for text they
    // cannot parse, and this codebase's own authors treat 00:00:00 as the canonical zero time when
    // they need one, choosing Time('00:00:00') as the null-substitute sort sentinel at
    // n_cst_dwsvc_columnsort.sru:L323. Record it as a characterization item beside DECISION 6's
    // fractional-second question.
    //
    // THE MIDNIGHT COLLISION, THIS TIME REPRODUCED RATHER THAN AVOIDED. Coerce("00:00:00") and
    // Coerce("not a time") both return 00:00:00, and a caller cannot tell them apart. That IS the
    // legacy behaviour and it is NOT corrected here. Contrast DECISION 3, where the analogous
    // collision between a null time and midnight is AVOIDED - because there the legacy avoids it
    // too, by having a distinct null. The two cases look alike and must be handled oppositely: a
    // null is never midnight, uncoercible text becomes midnight. A caller that genuinely needs the
    // distinction uses TryCoerce, which is additive and is described at DECISION 13.
    //
    // NULL PROPAGATION. PowerScript propagates null through a conversion rather than substituting a
    // value, so Coerce(null) returns the typed null of DECISION 3 and NOT the fallback.

    /// <summary>
    /// The time value substituted when the edit text cannot be coerced: <c>00:00:00</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// UNVERIFIED FROM THIS REPOSITORY and recorded as a characterization item. Nothing in the
    /// checkout proves what PowerBuilder's <c>Time(string)</c> conversion yields for unparseable
    /// text; this value is the reasoned choice, exposed by name so it can be pinned against the
    /// behavioural oracle without hunting for a literal.
    /// </para>
    /// <para>
    /// Because this equals midnight, <see cref="Coerce(string?)"/> cannot be used to distinguish a
    /// genuine <c>00:00:00</c> from uncoercible text. That indistinguishability is the legacy
    /// behaviour of <c>se_cst_dw.sru:L243</c> and is deliberately preserved. Use
    /// <see cref="TryCoerce(string?, out System.Nullable{TimeOnly})"/> when the distinction is
    /// needed, and never on the item-change path.
    /// </para>
    /// </remarks>
    public static readonly TimeOnly UncoercibleFallback = TimeOnly.MinValue;

    // DECISION 12 - HAZARD H5: THE ACCEPTED PARSE FORMATS ARE EXPLICIT, INVARIANT AND NARROW
    // ------------------------------------------------------------------------------------------
    // Nothing in the migration plan ports PowerBuilder's String() or Time() primitives - the shared
    // kernel's Formatting covers only formatretcode.srf and sprintf.srf - so this file owns its own
    // conversion in both directions and has to pin the culture itself.
    //
    // Measured on this toolchain rather than assumed:
    //
    //     "H:m:s"           accepts "1:2:3" AND "01:02:03" AND "23:59:59". A single custom
    //                       specifier matches one OR two digits when PARSING, so this format is a
    //                       strict superset of the canonical "HH:mm:ss".
    //     "H:m:s.FFFFFFF"   additionally accepts "1:2:3.5" and "01:02:03.1234567".
    //     "H:m"             accepts "10:25", the two-field form PowerBuilder's own documentation
    //                       gives as a valid time string.
    //
    // and the set as a whole REJECTS "25:61:61", "1:2:3 PM", "abc" and "" - which are exactly the
    // inputs the parity tests drive through Coerce to prove it returns the fallback without
    // throwing.
    //
    // NO DESIGNATOR-BEARING FORMAT IS ACCEPTED, deliberately. Accepting one would make whether a
    // parse succeeds depend on the host locale's designator strings, so text that parsed on a
    // developer's machine could fail in a container. Narrowing with a defined outcome is required;
    // widening on a guess is forbidden.
    //
    // DateTimeStyles.None is pinned at every call, so leading and trailing whitespace are NOT
    // tolerated. Whether the legacy tolerates them is unknown, and the narrow choice is the correct
    // one when the oracle is silent.
    //
    // THE ASYMMETRY IS DELIBERATE: this accepted set is WIDER than the single emitted format. Those
    // are two different obligations. Emission is contract, so it has exactly one byte-exact form
    // (DECISION 6). Acceptance mirrors the leniency of the PowerBuilder conversion being replaced.
    // Round-tripping therefore holds in the direction that matters, and the tests assert it:
    // Coerce(Format(t)) equals t truncated to whole seconds.
    private static readonly string[] AcceptedFormatsCore =
    [
        "H:m:s",
        "H:m:s.FFFFFFF",
        "H:m",
    ];

    /// <summary>
    /// The exact, ordered set of invariant time formats <see cref="Coerce(string?)"/> and
    /// <see cref="TryCoerce(string?, out System.Nullable{TimeOnly})"/> accept.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All three are 24-hour and none carries an AM/PM designator, so whether a parse succeeds
    /// never depends on the host locale. Single specifiers such as <c>H</c> match one or two digits
    /// when parsing, so this set accepts <c>1:2:3</c> as readily as <c>01:02:03</c>.
    /// </para>
    /// <para>
    /// Published so that a parity test, and the characterization record, can state the accepted
    /// surface exactly rather than inferring it from behaviour. This set is intentionally wider
    /// than the single <see cref="CanonicalFormat"/> used for emission.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> AcceptedFormats { get; } = Array.AsReadOnly(AcceptedFormatsCore);

    /// <summary>
    /// Coerces DataWindow edit text into a time value exactly as the legacy
    /// <c>SetItem(row, Long(dwo.ID), Time(data))</c> arm at <c>se_cst_dw.sru:L243</c> does. This is
    /// the coercion the item-change path uses.
    /// </summary>
    /// <param name="data">
    /// The edit text. May be <see langword="null"/>, empty, or entirely uncoercible; none of those
    /// throws.
    /// </param>
    /// <returns>
    /// The typed null when <paramref name="data"/> is <see langword="null"/>, reproducing
    /// PowerScript null propagation; the parsed time when
    /// <paramref name="data"/> matches one of <see cref="AcceptedFormats"/>; and
    /// <see cref="UncoercibleFallback"/> otherwise.
    /// </returns>
    /// <remarks>
    /// <para>
    /// NEVER THROWS, for any input. The legacy write discards the result of <c>SetItem</c>, applies
    /// no validity test and performs no return-code check, and the enclosing switch has no
    /// <c>case else</c> arm at all, so an unrecognised or uncoercible value is met with silence.
    /// Reproducing that silence is required; raising an exception instead would be a behavioural
    /// regression presented as robustness.
    /// </para>
    /// <para>
    /// Because <see cref="UncoercibleFallback"/> is midnight, this method cannot distinguish a
    /// genuine <c>00:00:00</c> from uncoercible text. That is the legacy behaviour and is preserved.
    /// <see cref="TryCoerce(string?, out System.Nullable{TimeOnly})"/> reports the difference, but
    /// it is additive and must not be used on the item-change path.
    /// </para>
    /// <para>
    /// Parsing pins <see cref="CultureInfo.InvariantCulture"/> and
    /// <see cref="DateTimeStyles.None"/> explicitly, so the result never depends on the host locale
    /// and surrounding whitespace is not tolerated.
    /// </para>
    /// </remarks>
    public static TimeOnly? Coerce(string? data)
    {
        if (data is null)
        {
            // PowerScript propagates null through a conversion. This is DECISION 3's typed null,
            // never UncoercibleFallback: a null time is not midnight.
            return null;
        }

        if (TimeOnly.TryParseExact(
                data,
                AcceptedFormatsCore,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out TimeOnly parsed))
        {
            return parsed;
        }

        // se_cst_dw.sru:L243 writes whatever the conversion produced without checking it. See
        // DECISION 11 for why this value is UNVERIFIED and how it is to be pinned.
        return UncoercibleFallback;
    }

    // DECISION 13 - THE Try-SHAPED COERCION IS ADDITIVE AND HAS NO LEGACY ANALOGUE
    // ------------------------------------------------------------------------------------------
    // There is no legacy counterpart to this method. It exists because a service boundary needs to
    // be able to report a coercion failure that the in-process legacy simply swallowed, and this is
    // the one place in this arm where the return-code algebra applies at all.
    //
    // Domain/ItemChangeProtocol.cs MUST NOT USE IT on the item-change path. That path reproduces
    // se_cst_dw.sru:L182-L253, whose contract is the silence of DECISION 11, and routing it through
    // a reporting variant would change observable behaviour.
    //
    // THE CORRESPONDENCE TO Coerce IS AN EXACT, TESTABLE RULE: TryCoerce reports
    // RetCode.E_INVALID_DATA precisely when Coerce would have TAKEN THE FALLBACK BRANCH, and
    // RetCode.OK in every other case. Stating it as one rule rather than as two implementations is
    // what lets a single unit test prove the two methods can never disagree. Written as the pair of
    // assertions the test actually makes, for every input:
    //
    //     TryCoerce returned OK                ->  Coerce(data) equals the out value
    //     TryCoerce returned E_INVALID_DATA    ->  Coerce(data) equals UncoercibleFallback
    //
    // Note carefully that the second line does NOT invert into a value test, and mistaking it for
    // one is the trap. Coerce("00:00:00") also returns midnight, by the PARSE branch, while
    // TryCoerce("00:00:00") correctly reports OK. The branch taken is not recoverable from the
    // returned value - which is precisely the collision DECISION 11 preserves.
    //
    // A NULL INPUT IS RetCode.OK, NOT E_INVALID_DATA. A null is the typed null the legacy itself
    // produces at dwnvltime.srf:L10, and its expression form is dwNvlTime() - an ABSENT value, not
    // invalid data. Classifying it as invalid would make the additive path contradict the very
    // function this file ports.
    //
    // ON FAILURE THE OUT PARAMETER IS THE TYPED NULL, NOT UncoercibleFallback. That is deliberate
    // defensive design on an additive surface: a caller that ignores the return code then writes a
    // null rather than silently writing midnight. The legacy midnight substitution stays reachable
    // through Coerce and only through Coerce.
    //
    // The return type is `long`, matching the `Constant Long` declarations in retcode.sru, so no
    // cast appears at any call site. It is NOT the {0,1,2,3} item-change alphabet of
    // se_cst_dw.sru:L182-L253; that alphabet is a different code space, owned by
    // Domain/ItemChangeProtocol.cs, and the two must never be compared.

    /// <summary>
    /// Coerces DataWindow edit text into a time value AND reports whether the coercion succeeded.
    /// This is an ADDITIVE path with no legacy analogue; it must not be used on the item-change
    /// path.
    /// </summary>
    /// <param name="data">The edit text. May be <see langword="null"/> or empty.</param>
    /// <param name="value">
    /// On success, the coerced value, which is the typed null when <paramref name="data"/> is
    /// <see langword="null"/>. On failure, the typed null - deliberately NOT
    /// <see cref="UncoercibleFallback"/>, so that a caller which ignores the return code writes a
    /// null rather than silently writing midnight.
    /// </param>
    /// <returns>
    /// <see cref="RetCode.OK"/> (<c>0</c>, from <c>retcode.sru:L39</c>) when
    /// <paramref name="data"/> is <see langword="null"/> or matches one of
    /// <see cref="AcceptedFormats"/>; <see cref="RetCode.E_INVALID_DATA"/> (<c>-9</c>, from
    /// <c>retcode.sru:L52</c>) otherwise.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The legacy has no reporting coercion: <c>se_cst_dw.sru:L243</c> discards the result of the
    /// write and the enclosing switch has no <c>case else</c>. This method is therefore an addition
    /// made for the service boundary, where a caller across a network needs to be told that the
    /// text could not be coerced. <c>Domain/ItemChangeProtocol.cs</c> must call
    /// <see cref="Coerce(string?)"/> instead, so that the legacy silence is reproduced.
    /// </para>
    /// <para>
    /// The relationship to <see cref="Coerce(string?)"/> is exact and is asserted by test: this
    /// method reports failure precisely when <see cref="Coerce(string?)"/> would have returned
    /// <see cref="UncoercibleFallback"/>, and success in every other case.
    /// </para>
    /// <para>
    /// The returned code is a PowerFramework return code, not the four-value item-change alphabet
    /// of <c>se_cst_dw.sru:L182-L253</c>. Those are separate code spaces and a value from one is
    /// never meaningful against a constant from the other.
    /// </para>
    /// </remarks>
    public static long TryCoerce(string? data, out TimeOnly? value)
    {
        if (data is null)
        {
            // A null is an ABSENT value, whose expression form is NullLiteralExpression. It is the
            // very thing dwnvltime.srf:L10 produces, so it is not invalid data.
            value = null;
            return RetCode.OK;
        }

        if (TimeOnly.TryParseExact(
                data,
                AcceptedFormatsCore,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out TimeOnly parsed))
        {
            value = parsed;
            return RetCode.OK;
        }

        // Failure is reported exactly where Coerce would have substituted UncoercibleFallback, and
        // the out parameter is left as the typed null rather than that fallback. See DECISION 13.
        value = null;
        return RetCode.E_INVALID_DATA;
    }

    // ==========================================================================================
    // REGION 5 - RENDERING A TIME VALUE AS EXPRESSION TEXT
    // Source: dwvaluetoexp.srf:L66 - sVal = "Time('" + String(val) + "')"
    // ==========================================================================================
    //
    // DECISION 14 - THIS METHOD PRODUCES THE INNER TEXT ONLY. IT DOES NOT BUILD THE EXPRESSION.
    // ------------------------------------------------------------------------------------------
    // dwvaluetoexp.srf:L66 does two things: it renders the value, and it wraps the rendering in
    // Time('...'). Only the rendering belongs here. Expressions/ValueToExpression.cs owns the
    // wrapper and the null branch, so the composition
    //
    //     value is null  ->  NullLiteralExpression                   [dwvaluetoexp.srf:L67]
    //     otherwise      ->  "Time('" + Format(value) + "')"         [dwvaluetoexp.srf:L66]
    //
    // exists at exactly one site. Splitting it this way is what keeps the format string and the
    // null sentinel for this type side by side in one file while the emitter stays free of magic
    // strings of its own.
    //
    // The rendering pins the culture EXPLICITLY at the call, rather than relying on the ambient
    // culture being invariant in a container. Measured on this toolchain, 13:05:09 renders as
    // "13:05:09" under the invariant culture, under en-US and under de-DE when the format and
    // culture are pinned, while the culture-sensitive short-time specifier yields "1:05 PM" under
    // en-US - a designator the DataWindow expression function must never be handed.
    //
    // TRUNCATION IS EXPECTED, NOT AN OVERSIGHT. CanonicalFormat carries no fractional-second field,
    // so a TimeOnly holding a sub-second component renders without it. See DECISION 6 for why that
    // is the choice and why it is marked UNVERIFIED-FROM-REPOSITORY.

    /// <summary>
    /// Renders a time value as the invariant text that goes inside a <c>Time('...')</c> DataWindow
    /// expression, reproducing the rendering half of <c>dwvaluetoexp.srf:L66</c>.
    /// </summary>
    /// <param name="value">The time value to render.</param>
    /// <returns>
    /// The value formatted with <see cref="CanonicalFormat"/> under
    /// <see cref="CultureInfo.InvariantCulture"/>: 24-hour, colon-separated, zero-padded to two
    /// digits per field, with no fractional seconds and no AM/PM designator.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method returns the INNER text only. It does not wrap the result in <c>Time('...')</c>
    /// and it has no null branch, because <c>Expressions/ValueToExpression.cs</c> owns the wrapper
    /// and selects <see cref="NullLiteralExpression"/> for a null. That keeps the composition at a
    /// single site.
    /// </para>
    /// <para>
    /// The culture is pinned at the call rather than assumed, so the output is byte-identical
    /// whatever the host locale, and no AM/PM designator can ever appear in it.
    /// </para>
    /// <para>
    /// Any sub-second component of <paramref name="value"/> is truncated, because
    /// <see cref="CanonicalFormat"/> carries no fractional-second field. That is a recorded
    /// decision, not an oversight, and it is a characterization item.
    /// </para>
    /// </remarks>
    public static string Format(TimeOnly value)
    {
        return value.ToString(CanonicalFormat, CultureInfo.InvariantCulture);
    }
}
