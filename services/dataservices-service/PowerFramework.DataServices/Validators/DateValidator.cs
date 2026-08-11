// ==============================================================================================
//  DateValidator - the typed-null producer for the PowerBuilder `date` column type, the published
//  `dwNvlDate` expression spelling, and the `"date"` arm of the item-change coercion table.
//  --------------------------------------------------------------------------------------------
//  PORTED FROM    ws_objects/pfw.datawindow.services.pbl.src/dwnvldate.srf   (13 lines, readable
//                 PowerScript, NO `native` clause - nothing about it is hidden inside pfw.dll)
//                 plus the single `case "date"` arm of the coercion table at
//                 ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L240-L241
//
//  READ AS REFERENCE, NOT PORTED HERE
//      dwvaluetoexp.srf:L59-L63    supplies and verifies the emitted sentinel spelling. The whole
//                                  of that function object is ported by Validators/
//                                  ValueToExpression.cs, which CONSUMES this file's constants.
//      se_cst_dw.sru:L182-L253     the item-change micro-protocol. The `choose case` itself
//                                  belongs to Domain/ItemChangeProtocol.cs; this file supplies
//                                  only the token set and the arm behaviour it dispatches to.
//      n_cst_dwsvc.sru:L26-L32     the column-type category constants, and L503-L518
//                                  `_of_convertcoltype`, the second truncation table.
//      retcode.sru:L39-L52         the return-code identifiers, ported to
//                                  shared/PowerFramework.Shared.Kernel/RetCode.cs.
//
//  ORACLE STATUS  Every `ws_objects/**` path above is READ ONLY (constraint C-C). Those files are
//                 the behavioural oracle for parity testing and are never an edit target. Outside
//                 them there is no other statement of intended behaviour to consult, so every
//                 behaviour reproduced below carries the line locator it was taken from.
//
//  ============================================================================================
//  THE FINDING THAT GOVERNS THIS WHOLE FILE: THE FOLDER NAME IS MISLEADING
//  ============================================================================================
//  `dwnvldate.srf` is NOT a validator. Its complete body is three statements:
//
//      L9   global function date dwnvldate ();date nvl      // declare a typed local
//      L10  SetNull(nvl)                                    // null it
//      L11  return nvl                                      // return it
//
//  It validates nothing, inspects nothing and rejects nothing. It exists to be called from inside
//  a DataWindow EXPRESSION STRING, as the null literal for a `date`-typed column: PowerScript has
//  no way to write a typed null inline in an expression, so the framework publishes a niladic
//  function per type and names it in the expression text instead.
//
//  This was confirmed rather than assumed. A repository-wide search for `dwnvl` across all 544
//  legacy objects returns exactly two kinds of hit: the five `dwNvl*` definition files themselves,
//  and the five sentinel STRING LITERALS in `dwvaluetoexp.srf` (L19, L25, L31, L37, L43, L49, L55,
//  L61, L67). There is not one PowerScript call site anywhere in the estate. THE OBSERVABLE
//  CONTRACT IS THEREFORE THE EMITTED SPELLING, and nothing else.
//
//  Two consequences follow, and both are load bearing:
//    * The spelling constants below are byte-exact contract, not convenience strings.
//    * No validation logic may be added here to "complete" a validator. Constraint C-B forbids
//      behaviour that the legacy does not have, and adding a rule set would obscure parity, which
//      is the one property that matters most in this refactor.
//
//  ============================================================================================
//  WHAT THIS FILE OWNS, AND WHAT IT DELIBERATELY DOES NOT
//  ============================================================================================
//  OWNS
//      * The published `dwNvlDate` / `dwNvlDate()` spellings.
//      * The typed null for the PowerBuilder `date` type.
//      * The canonical invariant text format for a `date` value inside a DataWindow expression.
//      * The `"date"` prefix token, and the length-safe truncate-then-compare predicate that
//        decides whether a `ColType` string belongs to this arm.
//      * The arm behaviour itself: the text-to-`DateOnly` coercion that `Date(data)` performs at
//        se_cst_dw.sru:L241.
//
//  DOES NOT OWN, ON PURPOSE
//      * The `choose case` dispatch. That is Domain/ItemChangeProtocol.cs, which ports
//        se_cst_dw.sru:L182-L253 in full, including the `{0,1,2,3}` return alphabet, the nested
//        changing event and the forced `rtCode = 2` at L250.
//      * The `"datet"` arm at se_cst_dw.sru:L238-L239. That is Validators/DateTimeValidator.cs.
//        Note how close the two literals sit - the datetime arm is the line ABOVE this file's -
//        and see DECISION 6 for the trap that adjacency creates.
//      * The `Date('...')` / `dwNvlDate()` emission itself, which is
//        Validators/ValueToExpression.cs porting dwvaluetoexp.srf:L59-L63. This file supplies the
//        pieces that emission is built from so the emitter carries no magic string of its own.
//      * The COL_TYPE_* constants. Domain/DataWindowServiceHost.cs owns them; a second copy would
//        be a second source of truth. This file records the correspondence in prose only - the
//        `"date"` arm is the `COL_TYPE_DATE = 5` category [n_cst_dwsvc.sru:L31] - and takes no
//        dependency on Domain/ or Expressions/ to say so. That absence of sibling-folder coupling
//        is why this file can be authored before either of them exists.
//      * Any dialog, any localized message, any structured error object. The invalid-value dialog
//        at se_cst_dw.sru:L357 and its `I18N(ne_cst_i18n.CAT_DWSVC, ...)` fallback belong to
//        Domain's structured error result, and the rendering half of it is DesignSystem work,
//        which constraint C-D defers entirely.
//
//  ============================================================================================
//  DECISIONS
//  ============================================================================================
//  DECISION 1 - PowerBuilder `date` becomes `DateOnly`, so the typed null is `DateOnly?`.
//      The plan's type map pairs `date` with `DateOnly`, `datetime` with `DateTime` and `time`
//      with `TimeOnly`, and the ported `Text.Iif` overload set in Shared.Kernel already commits to
//      exactly that separation. `DateTime?` is therefore WRONG here twice over: it would collide
//      with DateTimeValidator.cs, and it would change the overload set that
//      Validators/ValueToExpression.cs must expose to keep the nine legacy `dwvaluetoexp`
//      overloads distinguishable.
//
//  DECISION 2 - a null value is a NULLABLE VALUE TYPE and is never collapsed to a default.
//      PowerBuilder has null for value types and the framework's whole return-code algebra depends
//      on null being representable, so the plan's translation rule is explicit: port value-type
//      null as a nullable value type, never as a sentinel. Collapsing to `DateOnly.MinValue` or
//      `default` would convert one observable state into a different one, and the damage would
//      show up downstream rather than here: ValueToExpression's date overload tests
//      `IsNull(sVal)` [dwvaluetoexp.srf:L61], so a defaulted date would emit
//      `Date('0001-01-01')` where the legacy emits `dwNvlDate()`.
//
//  DECISION 3 - the two spellings are single-sourced, and `Expressions/
//      DataWindowExpressionEvaluator.cs` must register `FunctionName` rather than retype it.
//      `n_cst_dwsvc.sru:L217` hands expression text to `Describe("Evaluate(...)")`, and the
//      caller at `n_cst_dwsvc_columnexp.sru:L2391` detects failure only by the returned value
//      being `"?"` or `"!"`. A one-character drift between the name the evaluator registers and
//      the name the emitter writes therefore surfaces as a RUNTIME expression error carrying no
//      diagnostic, never as a compile error. `NullLiteralExpression` is consequently composed from
//      `FunctionName` at compile time instead of being spelled a second time, so the two cannot
//      disagree. Recorded per constraint C-K as an internal structuring choice with no observable
//      cost: both constants still hold byte-identical values to the legacy literals.
//
//  DECISION 4 - identifiers are PascalCase; the legacy spelling lives in the string VALUE.
//      The repository-root .editorconfig carries file-scoped CA1707 and IDE1006 suppressions for the
//      individually named files that declare preserved SCREAMING_SNAKE or underscore-prefixed
//      identifiers, and that file is the SOLE ROSTER - no count is repeated here, because a number
//      copied into a source header is a second place for the roster to be wrong and it silently
//      became wrong as files were added.
//      THIS FILE IS NOT ON THAT ROSTER, .editorconfig is not this file's to change, and
//      Directory.Build.props promotes warnings to errors - so a member spelled `dwNvlDate` would
//      be a build failure rather than a style debate. No member here needs the legacy spelling
//      anyway: the contract is the emitted TEXT, which is preserved exactly in the constant value.
//
//  DECISION 5 - `Left(s, 5)` is length-safe; `Substring(0, 5)` is not, and the difference is
//      fatal precisely here. PowerBuilder's `Left` truncates safely and returns a short string
//      unchanged; `string.Substring(0, 5)` throws `ArgumentOutOfRangeException` when the string is
//      shorter than five characters. This file's token is `"date"`, FOUR characters, so
//      `Left("date", 5)` is `"date"` - and a failed `Describe` call returns `""`. A mechanical
//      `Substring` port would therefore crash on the single most common input this arm exists to
//      serve. `TruncateColumnType` is the length-safe port and is used by every comparison below.
//
//  DECISION 6 - THE HEADLINE HAZARD: match by EXACT EQUALITY on the truncated string, never by
//      `StartsWith`. This file is where that mistake does the most damage anywhere in the port.
//      PowerScript `choose case` compares for equality, which makes the arms mutually exclusive:
//      `Left("datetime", 5)` is `"datet"`, which is NOT equal to `"date"`, so a datetime column
//      correctly falls through to the datetime arm one line above at se_cst_dw.sru:L238-L239. But
//      `"datetime".StartsWith("date")` is TRUE. A `StartsWith` port silently routes every datetime
//      column into the date arm and discards the time component, with no error, no warning and no
//      diagnostic anywhere in the system. Truncate to five, then compare for equality. The
//      regression test that pins this asserts `"datetime"` does NOT match.
//
//  DECISION 7 - case sensitivity is contract, so every comparison is `StringComparison.Ordinal`.
//      PowerScript `=` on strings, and therefore `choose case`, is case-sensitive; the arm literal
//      is lowercase and `Describe(".ColType")` is observed lowercase throughout the corpus.
//      `OrdinalIgnoreCase` would WIDEN the contract on a guess, which the plan forbids - the rule
//      is to narrow with a defined error, never to widen. `"DATE"` consequently does not match,
//      and a test pins that. Ordinal also keeps the comparison independent of the host locale,
//      which the characterization model requires: a culture-sensitive comparison would make a
//      legacy recording and a target recording diverge on the machine's locale for reasons that
//      have nothing to do with the port.
//
//  DECISION 8 - the coercion is UNCHECKED and reports nothing, and that is reproduced.
//      `SetItem(row, Long(dwo.ID), Date(data))` [se_cst_dw.sru:L241] performs no validity test,
//      inspects no return code, and the enclosing `choose case` at L231-L244 has NO `case else`
//      arm at all - an unrecognised prefix simply writes nothing and reports nothing. `Coerce`
//      reproduces that exactly: it is total, it never throws, and it reports no failure. Adding a
//      throw or a diagnostic would be a regression dressed as robustness, which constraint C-B
//      forbids. The Try-shaped variant that DOES report is separate, additive, and off the
//      item-change path - see DECISION 9.
//      Worth noting the contrast the oracle itself provides: the second truncation table,
//      `_of_convertcoltype` at n_cst_dwsvc.sru:L503-L518, uses the identical `Left(colType, 5)`
//      dispatch and DOES carry a `case else` returning `COL_TYPE_UNKNOWN` (L516-L517). The two
//      tables agree on `"date"` and `"datet"` and disagree on defaulting. That asymmetry is
//      legacy behaviour; it is not harmonized.
//
//  DECISION 9 - the Try-shaped coercion is ADDITIVE and has no legacy analogue.
//      `TryCoerce` reports `RetCode.OK` or `RetCode.E_INVALID_DATA` [retcode.sru:L52, = -9]. It
//      exists for the callers that genuinely need to distinguish "the text was not a date" from
//      "the date was null" - a REST or gRPC boundary answering a client, where silence is not an
//      option. It is not a replacement for `Coerce` and the item-change path MUST NOT use it:
//      routing L241 through a reporting coercion would introduce an error the legacy cannot
//      produce. This is the one place in this folder where the return-code algebra applies at all.
//
//  DECISION 10 - every conversion is explicitly `CultureInfo.InvariantCulture`, and parsing pins
//      `DateTimeStyles`. Nothing here reads `CultureInfo.CurrentCulture`. There is no port of
//      PowerBuilder's `String()` or `Date()` primitives anywhere in the plan - Shared.Kernel's
//      `Formatting` covers only `formatretcode.srf` and `sprintf.srf` - so this file owns its own
//      conversion and must pin the culture itself. Ambient-culture date handling is the classic
//      silent-corruption bug: `"03/04/2020"` is 4 March in one locale and 3 April in another, and
//      neither the parse nor the write would report anything. The pinned style is
//      `DateTimeStyles.None`: no `AssumeLocal`, no `AssumeUniversal`, no `AdjustToUniversal`, and
//      no time-zone interpretation of any kind, because a `date` carries no time zone and a
//      `DateOnly` has nowhere to put one. No bare `Parse`, `TryParse`, `Convert.*`, `ToString()`
//      or interpolated string appears anywhere below.
//
//  DECISION 11 - the schema type mismatch is TOLERATED, not validated. The primary fixture
//      `ws_objects/pfw.tests.pbl.src/dw_sqlite.srd` declares its birth column as a DataWindow
//      `date` against a SQLite `TEXT` column, and the plan records that mismatch as a PRESERVED
//      DEFECT. This file therefore must not reject it, must not warn about it, and must not coerce
//      it away. `TEXT` is exactly what this arm already receives - `data` arrives as a string - so
//      tolerating it costs nothing and requires only that no type-affinity check be added.
//
//  ============================================================================================
//  TWO ITEMS ARE UNVERIFIED FROM THE REPOSITORY, AND BOTH ARE MARKED AT THEIR SITE
//  ============================================================================================
//  Both are marked inline with `UNVERIFIED-FROM-REPOSITORY` so a characterization run against the
//  behavioural oracle can find and pin them, and so the choices are auditable rather than
//  invisible. They belong in `characterization/` and `docs/PARITY.md`, which this file does not
//  write; the markers are what let their author locate them.
//      U-1  the canonical value format - see CanonicalValueFormat
//      U-2  the fallback for text that is not a date - see Coerce
//
//  ============================================================================================
//  DELIBERATELY ABSENT, EACH FOR A STATED REASON
//  ============================================================================================
//      * Any `using` of a Domain or Expressions type, and any project or package reference. This
//        file adds neither (constraints C-A and C-I). Its only non-BCL dependency is
//        PowerFramework.Shared.Kernel, already referenced by the project for RetCode.
//      * FluentValidation or any validation framework, excluded by name in the plan and again in
//        this project's own .csproj, because a rule framework would hide the legacy semantics.
//      * Any dialog, MessageBox port, `I18N` call, logger, `Console` write, file or network
//        access, and any clock read. `DateTime.Now` in particular is absent: a defaulted "today"
//        would make every characterization recording non-reproducible, and repeatability is the
//        one hard prerequisite of the golden-master technique this refactor's parity model rests
//        on. Every member below is static, pure and deterministic, which is also what lets the
//        sibling PowerFramework.DataServices.Tests project drive table-driven parity theories
//        against the 80 percent per-service coverage gate (constraint C-H).
//      * Any secret or configuration value. Constraint C-F targets configuration; the sentinel
//        spelling, the value format and the prefix token are BEHAVIOURAL CONTRACT and stay
//        compiled in. Moving them to appsettings.json would make parity a deployment variable.
//      * Any promotion of this behaviour into a shared library. All 13 objects of
//        pfw.datawindow.services are assigned to DataServices, and constraint C-A permits no
//        shared behaviour across a service boundary.
//      * A per-file licence header. Assembly-level copyright is set once in
//        Directory.Build.props from the root LICENSE.
// ==============================================================================================

using System.Collections.Immutable;
using System.Globalization;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.DataServices.Validators;

/// <summary>
/// The <c>date</c> column type's typed-null producer, published expression spelling, and
/// item-change coercion arm, ported from
/// <c>ws_objects/pfw.datawindow.services.pbl.src/dwnvldate.srf</c> together with the
/// <c>case "date"</c> arm at <c>se_cst_dw.sru:L240-L241</c>.
/// </summary>
/// <remarks>
/// <para>
/// Despite the folder name, the legacy object this class ports validates nothing: it declares a
/// <c>date</c> local, calls <c>SetNull</c> on it and returns it [dwnvldate.srf:L9-L11]. It exists
/// to be named inside a DataWindow expression string as the null literal for a
/// <c>date</c>-typed column, which is why its only appearance anywhere in the 544-object legacy
/// estate is the sentinel string literal at <c>dwvaluetoexp.srf:L61</c>. The observable contract
/// is the emitted spelling.
/// </para>
/// <para>
/// A <c>*.srf</c> global function maps to a <c>static</c> method on a role-named static class, so
/// this class has no instances, no interface and no participation in dependency injection. Every
/// member is static, pure and deterministic: nothing here reads a clock, an ambient culture, a
/// file, a socket or a configuration value.
/// </para>
/// <para>
/// Semantically this arm is the <c>COL_TYPE_DATE = 5</c> category [n_cst_dwsvc.sru:L31]. That
/// correspondence is recorded in prose only - the constant itself belongs to
/// <c>Domain/DataWindowServiceHost.cs</c> and is deliberately not restated here.
/// </para>
/// </remarks>
public static class DateValidator
{
    // ------------------------------------------------------------------------------------------
    //  THE PUBLISHED SPELLINGS                                          dwvaluetoexp.srf:L59-L63
    // ------------------------------------------------------------------------------------------
    //  Byte-exact contract. See DECISION 3 for why they are single-sourced.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The DataWindow expression function name that yields a null <c>date</c>: <c>dwNvlDate</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Spelled exactly as the legacy emits it, including the lower-case <c>dw</c> prefix and the
    /// internal capitalisation, because this string is compared by a DataWindow expression parser
    /// rather than by a compiler [dwvaluetoexp.srf:L61].
    /// </para>
    /// <para>
    /// <c>Expressions/DataWindowExpressionEvaluator.cs</c> must register THIS CONSTANT in its
    /// expression function table rather than retyping the name. Expression evaluation goes through
    /// <c>Describe("Evaluate(...)")</c> [n_cst_dwsvc.sru:L217] and its caller detects failure only
    /// by the result being <c>"?"</c> or <c>"!"</c> [n_cst_dwsvc_columnexp.sru:L2391], so a
    /// one-character drift between registration and emission would surface at run time with no
    /// diagnostic, and never at compile time.
    /// </para>
    /// </remarks>
    public const string FunctionName = "dwNvlDate";

    /// <summary>
    /// The complete null literal as it appears inside a DataWindow expression:
    /// <c>dwNvlDate()</c>. [dwvaluetoexp.srf:L61]
    /// </summary>
    /// <remarks>
    /// Composed from <see cref="FunctionName"/> at compile time rather than spelled a second time,
    /// so the two constants cannot drift apart. The composed value is byte-identical to the legacy
    /// literal; <see cref="FunctionName"/> is a compile-time constant, so this remains a
    /// <c>const</c> and stays usable in attributes, <c>switch</c> labels and other constant
    /// contexts.
    /// </remarks>
    public const string NullLiteralExpression = FunctionName + "()";

    /// <summary>
    /// The canonical invariant format for the text a <c>date</c> value occupies inside a
    /// DataWindow expression - the <c>...</c> in <c>Date('...')</c> [dwvaluetoexp.srf:L60].
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>UNVERIFIED-FROM-REPOSITORY (U-1) - pin against the behavioural oracle.</c> The legacy
    /// inner text is PowerBuilder's no-format <c>String(date)</c> rendering, and the emitted string
    /// must round-trip through the DataWindow expression function <c>Date(...)</c> that consumes
    /// it. No fixture, document or expression anywhere in the repository shows a formatted date
    /// literal, so the exact rendering cannot be proven from the tree. ISO-8601
    /// <c>yyyy-MM-dd</c> is the starting point because it is unambiguous, culture-independent,
    /// fixed-width and zero-padded, and because it round-trips through
    /// <see cref="FormatValue(DateOnly)"/> and <see cref="Coerce(string?)"/> by construction. It
    /// is recorded as a characterization item, not asserted as parity.
    /// </para>
    /// <para>
    /// <c>Validators/ValueToExpression.cs</c> consumes this constant - by way of
    /// <see cref="FormatValue(DateOnly)"/> - so the emitter carries no format string of its own
    /// and the annotation above sits beside the null-sentinel annotation for the same type.
    /// </para>
    /// </remarks>
    public const string CanonicalValueFormat = "yyyy-MM-dd";

    // ------------------------------------------------------------------------------------------
    //  THE COLUMN-TYPE PREFIX TOKENS                                          se_cst_dw.sru:L231
    // ------------------------------------------------------------------------------------------
    //  The legacy dispatch is `choose case Left(dwo.ColType, 5)` at L231, and this file's arm is
    //  the single literal `"date"` at L240. The width and the token are published separately from
    //  the predicate so that Domain/ItemChangeProtocol.cs, which owns the switch, reads them from
    //  one place instead of restating either.
    //
    //  Truncation truth table, taken from the legacy dispatch and pinned by the regression tests:
    //
    //      "date"        -> "date"    MATCHES        (four characters; see DECISION 5)
    //      "datetime"    -> "datet"   DOES NOT MATCH (falls to L238-L239; see DECISION 6)
    //      "datet"       -> "datet"   DOES NOT MATCH
    //      "DATE"        -> "DATE"    DOES NOT MATCH (see DECISION 7)
    //      "decimal(2)"  -> "decim"   DOES NOT MATCH (L234-L235)
    //      "char(50)"    -> "char("   DOES NOT MATCH (L232-L233)
    //      "long"        -> "long"    DOES NOT MATCH (L236-L237)
    //      "time"        -> "time"    DOES NOT MATCH (L242-L243)
    //      ""            -> ""        DOES NOT MATCH (a failed Describe returns the empty string)
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The single truncated <c>ColType</c> prefix this arm claims: <c>date</c>.
    /// [se_cst_dw.sru:L240]
    /// </summary>
    /// <remarks>
    /// Four characters, which is shorter than the truncation width and is exactly why the
    /// truncation must be length-safe - see <see cref="TruncateColumnType(string?)"/>. Exposed as
    /// a <c>const</c> so a caller may use it in a <c>switch</c> label or another constant context;
    /// <see cref="ColumnTypePrefixes"/> is the collection form and
    /// <see cref="MatchesColumnType(string?)"/> is the comparison that should normally be used.
    /// </remarks>
    public const string ColumnTypePrefix = "date";

    /// <summary>
    /// The number of leading characters of a <c>ColType</c> string the legacy dispatch compares:
    /// <c>5</c>, from <c>Left(dwo.ColType, 5)</c>. [se_cst_dw.sru:L231]
    /// </summary>
    /// <remarks>
    /// The same width is used by the second truncation table, <c>_of_convertcoltype</c>
    /// [n_cst_dwsvc.sru:L503], so five is the framework-wide discriminating width rather than an
    /// artefact of one call site. It is published because
    /// <c>Domain/ItemChangeProtocol.cs</c> truncates once before dispatching and must use the same
    /// width the arms were written against.
    /// </remarks>
    public const int ColumnTypePrefixLength = 5;

    /// <summary>
    /// The immutable set of truncated <c>ColType</c> prefixes this arm claims, in the declaration
    /// order of the legacy <c>case</c> list. Exactly one element: <see cref="ColumnTypePrefix"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Published as a collection, and with one element, deliberately. Three of the sibling arms
    /// carry several tokens on one <c>case</c> label - <c>"char","char("</c> at
    /// se_cst_dw.sru:L232, <c>"decim","real","numbe"</c> at L234 and <c>"long","ulong"</c> at L236
    /// - so the collection shape is what makes the five validators uniform, and preserving the
    /// legacy declaration order keeps the published set readable against the oracle.
    /// </para>
    /// <para>
    /// <see cref="MatchesColumnType(string?)"/> is implemented in terms of this collection rather
    /// than against <see cref="ColumnTypePrefix"/> directly, so the published set and the predicate
    /// cannot disagree. An <c>ImmutableArray&lt;string&gt;</c> is used rather than an array so the
    /// contents cannot be rewritten by a caller through the exposed reference.
    /// </para>
    /// </remarks>
    public static ImmutableArray<string> ColumnTypePrefixes { get; } = [ColumnTypePrefix];

    // ------------------------------------------------------------------------------------------
    //  THE TYPED NULL                                                        dwnvldate.srf:L9-L11
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The typed null for a PowerBuilder <c>date</c>: always <see langword="null"/>. The complete
    /// port of <c>dwnvldate.srf</c>.
    /// </summary>
    /// <returns>
    /// <see langword="null"/>, as a <see cref="Nullable{T}"/> of <see cref="DateOnly"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Statement for statement, the legacy body is: declare a <c>date</c> local [dwnvldate.srf:L9],
    /// <c>SetNull</c> it [L10], return it [L11]. The local is kept below rather than collapsed to
    /// <c>return null</c> so the three-statement shape of the oracle stays legible; the emitted
    /// behaviour is identical.
    /// </para>
    /// <para>
    /// <see cref="DateOnly"/> and not <see cref="DateTime"/> - see DECISION 1 - and a nullable
    /// value type rather than a sentinel - see DECISION 2. Never substitute
    /// <see cref="DateOnly.MinValue"/> or <c>default</c>: doing so would make
    /// <c>Validators/ValueToExpression.cs</c> emit <c>Date('0001-01-01')</c> where the legacy emits
    /// <see cref="NullLiteralExpression"/> [dwvaluetoexp.srf:L60-L61].
    /// </para>
    /// <para>
    /// Exposed as a method rather than a property because the legacy artefact is a niladic global
    /// function, and because the DataWindow expression form it corresponds to is likewise a call.
    /// </para>
    /// </remarks>
    public static DateOnly? NullValue()
    {
        // dwnvldate.srf:L9 - `date nvl` declares the typed local.
        // dwnvldate.srf:L10 - `SetNull(nvl)` sets it to null.
        DateOnly? nvl = null;

        // dwnvldate.srf:L11 - `return nvl`.
        return nvl;
    }

    // ------------------------------------------------------------------------------------------
    //  THE COLUMN-TYPE PREDICATE                                        se_cst_dw.sru:L231, L240
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The length-safe port of PowerBuilder <c>Left(colType, 5)</c>. [se_cst_dw.sru:L231]
    /// </summary>
    /// <param name="colType">
    /// A DataWindow column type string, typically the result of <c>Describe(".ColType")</c>. May be
    /// <see langword="null"/> or shorter than <see cref="ColumnTypePrefixLength"/>.
    /// </param>
    /// <returns>
    /// The first <see cref="ColumnTypePrefixLength"/> characters of <paramref name="colType"/>, or
    /// the whole string when it is shorter, or <see cref="string.Empty"/> when it is
    /// <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// DECISION 5 in full: PowerBuilder <c>Left</c> truncates safely, whereas
    /// <c>string.Substring(0, 5)</c> throws when the string is shorter than five characters. This
    /// file's own token is four characters and a failed <c>Describe</c> yields the empty string, so
    /// the unsafe form would crash on the most common inputs this arm serves. This method therefore
    /// never throws for any input.
    /// </para>
    /// <para>
    /// A <see langword="null"/> input returns <see cref="string.Empty"/>. The legacy would carry
    /// the null into the <c>choose case</c>, where it matches no arm and - because the switch has
    /// no <c>case else</c> [se_cst_dw.sru:L244] - nothing is written and nothing is reported. The
    /// empty string reaches the same observable outcome here while keeping the return type
    /// non-nullable for callers.
    /// </para>
    /// <para>
    /// Idempotent: truncating an already-truncated value returns it unchanged, so a caller that
    /// pre-truncates before calling <see cref="MatchesColumnType(string?)"/> is safe, and one that
    /// does not is equally safe.
    /// </para>
    /// </remarks>
    public static string TruncateColumnType(string? colType)
    {
        if (colType is null)
        {
            return string.Empty;
        }

        return colType.Length <= ColumnTypePrefixLength
            ? colType
            : colType[..ColumnTypePrefixLength];
    }

    /// <summary>
    /// Answers whether a DataWindow <c>ColType</c> string dispatches to this arm - that is,
    /// whether <c>Left(colType, 5)</c> equals one of <see cref="ColumnTypePrefixes"/>.
    /// [se_cst_dw.sru:L231, L240]
    /// </summary>
    /// <param name="colType">
    /// A DataWindow column type string, typically the result of <c>Describe(".ColType")</c>. May be
    /// <see langword="null"/>, empty, or shorter than <see cref="ColumnTypePrefixLength"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when this arm claims the column type; otherwise
    /// <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// TRUNCATE TO FIVE, THEN COMPARE FOR EQUALITY - never <c>StartsWith</c>. This is DECISION 6,
    /// the single most damaging mistake available in this file. <c>choose case</c> compares for
    /// equality, so the arms are mutually exclusive: <c>Left("datetime", 5)</c> is <c>"datet"</c>,
    /// which does not equal <c>"date"</c>, so a datetime column correctly reaches the datetime arm
    /// at se_cst_dw.sru:L238-L239. <c>"datetime".StartsWith("date")</c> is <see langword="true"/>,
    /// so a <c>StartsWith</c> port would silently route every datetime column here and truncate the
    /// time component with no error anywhere.
    /// </para>
    /// <para>
    /// Comparison is <see cref="StringComparison.Ordinal"/> and therefore case-sensitive, matching
    /// PowerScript <c>=</c> on strings - DECISION 7. <c>"DATE"</c> does not match.
    /// </para>
    /// <para>
    /// Never throws, for any input, including <see langword="null"/> and the empty string.
    /// </para>
    /// </remarks>
    public static bool MatchesColumnType(string? colType)
    {
        if (colType is null)
        {
            return false;
        }

        string prefix = TruncateColumnType(colType);

        // Iterating the published set rather than testing ColumnTypePrefix directly is what keeps
        // the predicate and the published token set from drifting apart, and is the shape the
        // multi-token sibling arms at se_cst_dw.sru:L232, L234 and L236 need.
        foreach (string token in ColumnTypePrefixes)
        {
            if (string.Equals(prefix, token, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // ------------------------------------------------------------------------------------------
    //  THE ARM BEHAVIOUR                                                     se_cst_dw.sru:L241
    // ------------------------------------------------------------------------------------------
    //  `SetItem(row, Long(dwo.ID), Date(data))`. Two shapes are published: the legacy-faithful
    //  silent one that the item-change path uses, and an additive reporting one that it must not.
    //  See DECISION 8 and DECISION 9.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The legacy-faithful coercion of DataWindow edit text to a <c>date</c> value: the
    /// <c>Date(data)</c> half of <c>SetItem(row, Long(dwo.ID), Date(data))</c>.
    /// [se_cst_dw.sru:L241]
    /// </summary>
    /// <param name="data">
    /// The edit text the item-change event received. May be <see langword="null"/>, empty,
    /// whitespace, or text that is not a date at all.
    /// </param>
    /// <returns>
    /// The coerced <see cref="DateOnly"/>, or the typed null of <see cref="NullValue"/> when the
    /// text does not yield one.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THIS IS THE OVERLOAD THE ITEM-CHANGE PATH USES, and it is total: it never throws and it
    /// reports nothing. That is deliberate reproduction, not an oversight - DECISION 8. The legacy
    /// call site performs no validity test, inspects no return code, and its enclosing
    /// <c>choose case</c> has no <c>case else</c> arm at all [se_cst_dw.sru:L231-L244], so an
    /// unrecognised prefix writes nothing and reports nothing. Introducing a throw or a diagnostic
    /// here would be a regression dressed as robustness. Callers that genuinely need to know use
    /// <see cref="TryCoerce(string?, out DateOnly?)"/> instead, and the item-change path must not.
    /// </para>
    /// <para>
    /// <c>UNVERIFIED-FROM-REPOSITORY (U-2) - pin against the behavioural oracle.</c> The exact
    /// value PowerBuilder's <c>Date()</c> yields for text that is not a date cannot be proven from
    /// this repository: nothing in the tree exercises the case, and the primitive's implementation
    /// is not in the tree either. The typed null is chosen because it is the one value this file
    /// can defend from the oracle - it is the value <c>dwnvldate.srf</c> itself produces - because
    /// it keeps this method total, and because it fabricates no date that never existed. A future
    /// characterization run may replace it; the marker is here so that run can find the site. The
    /// second half of the same item is WHICH text counts as a date at all; the set this port
    /// accepts was measured rather than assumed and is tabulated on
    /// <c>TryParseInvariant</c> below.
    /// </para>
    /// <para>
    /// Writing the result is the caller's job and is unconditional, exactly as L241 is: a null
    /// result means the DataWindow item is set to null, which for a nullable <c>date</c> column is
    /// a legitimate value rather than an error.
    /// </para>
    /// <para>
    /// Parsing pins <see cref="CultureInfo.InvariantCulture"/> and
    /// <see cref="DateTimeStyles.None"/> - DECISION 10 - so the outcome is identical on every host
    /// locale, which the characterization model requires.
    /// </para>
    /// </remarks>
    public static DateOnly? Coerce(string? data)
    {
        // No validity test and no reported failure, mirroring se_cst_dw.sru:L241 exactly.
        return TryParseInvariant(data, out DateOnly parsed) ? parsed : NullValue();
    }

    /// <summary>
    /// The reporting coercion of DataWindow edit text to a <c>date</c> value. ADDITIVE: it has no
    /// legacy analogue, and the item-change path must not use it.
    /// </summary>
    /// <param name="data">
    /// The text to coerce. <see langword="null"/>, empty and whitespace-only input are treated as
    /// the absence of a value rather than as invalid data - see the remarks.
    /// </param>
    /// <param name="value">
    /// On success, the coerced value, or the typed null of <see cref="NullValue"/> when
    /// <paramref name="data"/> carried no value. On failure, the typed null. Always assigned.
    /// </param>
    /// <returns>
    /// <see cref="RetCode.OK"/> (<c>0</c>) [retcode.sru:L39] when <paramref name="data"/> yielded a
    /// value or was the absence of one; <see cref="RetCode.E_INVALID_DATA"/> (<c>-9</c>)
    /// [retcode.sru:L52] when it was text that is not a date.
    /// </returns>
    /// <remarks>
    /// <para>
    /// DECISION 9. Nothing in the legacy reports a coercion failure for a column type arm - see
    /// <see cref="Coerce(string?)"/> and DECISION 8 - so this method is an addition, not a port.
    /// It exists for the callers that must answer a client across one of the newly created service
    /// boundaries, where silently writing a null is not an acceptable answer to "was that a date?".
    /// It is the only place in this folder where the return-code algebra applies at all.
    /// </para>
    /// <para>
    /// THE ITEM-CHANGE PATH MUST NOT USE THIS METHOD. Routing <c>se_cst_dw.sru:L241</c> through a
    /// reporting coercion would surface an error the legacy cannot produce, which constraint C-B
    /// forbids.
    /// </para>
    /// <para>
    /// ADDITIVE DECISION, RECORDED BECAUSE IT HAS NO ORACLE: <see langword="null"/>, empty and
    /// whitespace-only text report <see cref="RetCode.OK"/> with a null
    /// <paramref name="value"/> rather than <see cref="RetCode.E_INVALID_DATA"/>. An empty edit is
    /// how a DataWindow expresses "this item has no value", and the legacy's own expression layer
    /// carries the same idea in the <c>emptystringisnull</c> flag on its column-expression
    /// structure, so treating emptiness as absence rather than as corruption is the reading that
    /// matches the framework. Text that is present but is not a date is the genuine
    /// <see cref="RetCode.E_INVALID_DATA"/> case.
    /// </para>
    /// <para>
    /// Never throws, for any input. Uses the same invariant, style-pinned parse as
    /// <see cref="Coerce(string?)"/>, so the two agree on exactly which text is a date.
    /// </para>
    /// </remarks>
    public static long TryCoerce(string? data, out DateOnly? value)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            // Absence of a value, not invalid data. See the additive decision in the remarks.
            value = NullValue();
            return RetCode.OK;
        }

        if (TryParseInvariant(data, out DateOnly parsed))
        {
            value = parsed;
            return RetCode.OK;
        }

        value = NullValue();
        return RetCode.E_INVALID_DATA;
    }

    // ------------------------------------------------------------------------------------------
    //  INVARIANT CONVERSION                                              dwvaluetoexp.srf:L59-L63
    // ------------------------------------------------------------------------------------------
    //  DECISION 10 lives here. Every conversion states its culture and its style explicitly, and
    //  no bare Parse, TryParse, Convert.*, ToString() or interpolated string appears.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Renders a <c>date</c> value as the invariant text that goes inside <c>Date('...')</c>, using
    /// <see cref="CanonicalValueFormat"/>. The <c>String(val)</c> half of
    /// <c>dwvaluetoexp.srf:L60</c>.
    /// </summary>
    /// <param name="value">The value to render.</param>
    /// <returns>
    /// The rendered text, always in <see cref="CanonicalValueFormat"/> and always independent of
    /// the ambient culture.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <c>Validators/ValueToExpression.cs</c> is the owner of the emission itself and composes it
    /// from this method, so the emitter carries no format string of its own:
    /// <c>"Date('" + FormatValue(value) + "')"</c> for a value, and
    /// <see cref="NullLiteralExpression"/> when the value is null [dwvaluetoexp.srf:L60-L61]. That
    /// emission is deliberately not implemented here - this file supplies the pieces, not the
    /// literal.
    /// </para>
    /// <para>
    /// <see cref="CultureInfo.InvariantCulture"/> is passed explicitly; the parameterless
    /// <c>ToString()</c> and the single-argument format overload are both forbidden here because
    /// both consult the ambient culture. The format is fixed-width and zero-padded, so a
    /// single-digit month or day renders as two characters and the output round-trips through
    /// <see cref="Coerce(string?)"/> for every value.
    /// </para>
    /// <para>
    /// The format itself is <c>UNVERIFIED-FROM-REPOSITORY (U-1)</c>; see
    /// <see cref="CanonicalValueFormat"/> for the reason and the characterization note.
    /// </para>
    /// </remarks>
    public static string FormatValue(DateOnly value)
    {
        return value.ToString(CanonicalValueFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The single invariant, style-pinned parse that both coercion shapes share.
    /// </summary>
    /// <param name="data">The text to parse. May be <see langword="null"/> or empty.</param>
    /// <param name="value">
    /// The parsed value on success; <c>default</c> otherwise. Always assigned.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="data"/> yielded a date; otherwise
    /// <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Two attempts, both explicitly <see cref="CultureInfo.InvariantCulture"/> and both pinned to
    /// <see cref="DateTimeStyles.None"/> - no <c>AssumeLocal</c>, no <c>AssumeUniversal</c>, no
    /// <c>AdjustToUniversal</c>, no time-zone interpretation of any kind. The canonical format is
    /// tried first so that round-tripping <see cref="FormatValue(DateOnly)"/> is exact by
    /// construction and cannot be reinterpreted by the general parser; the general invariant parse
    /// then covers the other renderings PowerBuilder's <c>Date()</c> accepts.
    /// </para>
    /// <para>
    /// The precise set of renderings the legacy primitive accepts is part of characterization item
    /// U-2: it is not stated anywhere in the repository. Pinning the culture is what makes whatever
    /// this port accepts deterministic across hosts, which is the property the parity model needs
    /// even before the set itself is confirmed. The set THIS PORT accepts was measured on
    /// .NET 10 rather than assumed, and is recorded here so the oracle comparison has something
    /// concrete to compare against:
    /// </para>
    /// <code>
    ///   "2020-02-29"           accepted -> 2020-02-29   the canonical form, matched exactly
    ///   "2020-1-2"             accepted -> 2020-01-02   unpadded month and day
    ///   "2020/02/29"           accepted -> 2020-02-29   slash separator
    ///   "03/04/2020"           accepted -> 2020-03-04   invariant order is MM/dd/yyyy, ALWAYS
    ///   " 2020-02-29 "         accepted -> 2020-02-29   surrounding white space is tolerated
    ///   "29 Feb 2020"          accepted -> 2020-02-29   invariant month name
    ///   "Feb 29, 2020"         accepted -> 2020-02-29   invariant month name, US order
    ///   "2020-02-29T00:00:00"  accepted -> 2020-02-29   a MIDNIGHT time component is allowed
    ///   "2020-02-29 10:30"     REJECTED                 a NON-ZERO time component is not
    ///   "2020-13-45"           REJECTED                 out-of-range components
    ///   "2020-02-30"           REJECTED                 valid shape, impossible day
    ///   "abc", ""              REJECTED
    /// </code>
    /// <para>
    /// The two time-component rows are the ones worth knowing, because they are not symmetrical and
    /// the asymmetry is the framework's rather than this port's: <see cref="DateOnly"/> accepts a
    /// date-time string only when the time of day is exactly midnight, so nothing is ever silently
    /// discarded - a non-zero time is refused outright. That refusal reinforces the arm separation
    /// of DECISION 6 rather than working against it: a genuine <c>datetime</c> value never
    /// half-lands in the <c>date</c> arm.
    /// </para>
    /// <para>
    /// The <c>03/04/2020</c> row is the reason DECISION 10 exists. Under the invariant culture that
    /// text is unambiguously 4 March; under <c>de-DE</c> the same text is 3 April. Pinning the
    /// culture is what makes the row above a fact rather than a property of the host.
    /// </para>
    /// </remarks>
    private static bool TryParseInvariant(string? data, out DateOnly value)
    {
        if (string.IsNullOrEmpty(data))
        {
            value = default;
            return false;
        }

        if (DateOnly.TryParseExact(
                data,
                CanonicalValueFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out value))
        {
            return true;
        }

        return DateOnly.TryParse(data, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }
}
