// ==================================================================================================
// NumberValidator.cs
// ==================================================================================================
//
// WHAT THIS FILE IS
//   The port of the legacy PowerBuilder global function object
//   `ws_objects/pfw.datawindow.services.pbl.src/dwnvlnumber.srf` (13 lines), together with the
//   NUMERIC HALF of the item-change type-coercion table at
//   `ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L231-L244`.
//
//   The legacy object is reproduced in full. All 13 of its lines are:
//
//       L1  $PBExportHeader$dwnvlnumber.srf
//       L2  global type dwnvlnumber from function_object
//       L3  end type
//       L5  forward prototypes
//       L6  global function integer dwnvlnumber ()
//       L7  end prototypes
//       L9  global function integer dwnvlnumber ();int nvl
//       L10 SetNull(nvl)
//       L11 return nvl
//       L12 end function
//
// THIS FOLDER IS MISNAMED, AND THAT IS DELIBERATELY NOT "FIXED"
//   `dwnvlnumber.srf` is not a validator. It declares one typed local, calls `SetNull` on it, and
//   returns it. It exists to be named from INSIDE a DataWindow expression string, which is why its
//   only appearance anywhere in the 544-object legacy estate is the sentinel string literal in
//   `dwvaluetoexp.srf`. That was checked exhaustively rather than assumed: a repository-wide search
//   for the identifier across `ws_objects/**`, excluding `dwnvlnumber.srf` itself and
//   `dwvaluetoexp.srf`, returns ZERO hits, so the five `dwNvl*` producers have no PowerScript call
//   sites at all. Their entire observable contract is the emitted spelling.
//
//   The folder name is retained because the target structure is fixed by the transformation plan and
//   because the four sibling files here carry the same shape. Renaming it would break that mapping
//   for no observable gain.
//
// WHAT THIS FILE DELIBERATELY DOES NOT DO
//   * It does not implement the item-change coercion switch. That switch belongs to
//     `Domain/ItemChangeProtocol.cs`, which ports the whole of `se_cst_dw.sru:L182-L253`. This file
//     supplies only the two per-arm token sets and the two per-arm coercions so that Domain can
//     drive its own dispatch by asking each validator whether a column type belongs to it.
//   * It does not redeclare the `COL_TYPE_*` constants. Those live at `n_cst_dwsvc.sru:L26-L32` and
//     their single C# home is `Domain/DataWindowServiceHost.cs`. A second declaration would be a
//     second source of truth. The alignment between an arm and a category is documented in prose
//     below and nowhere expressed as code.
//   * It references nothing from `Domain/` or `Expressions/`, and nothing outside the BCL and
//     `PowerFramework.Shared.Kernel`. That absence of a sibling-folder dependency is why this file
//     can be authored, compiled and tested before either of those folders exists.
//   * It contains no dialog, no message box, no localization call, no logging, no clock, no file or
//     network access and no configuration lookup. The invalid-value dialog at `se_cst_dw.sru:L357`,
//     with its `Describe(".ValidationMsg")` read, its outer-two-character strip at `:L351-L353` and
//     its `I18N(ne_cst_i18n.CAT_DWSVC, ...)` fallback, becomes a structured error owned by `Domain`;
//     its rendering half belongs to the deferred DesignSystem service.
//
// EVERY MEMBER IS STATIC, PURE AND DETERMINISTIC
//   No instance state, no interface, no dependency-injection registration. A `*.srf` global function
//   maps to a static method on a role-named static class, which is the same mapping that produced
//   `Predicates.IsSucceeded`, `Bits.BitAnd` and `Formatting.Sprintf` in the shared kernel. Purity is
//   also what lets the sibling test project express table-driven parity theories over this file
//   without a fixture, which the per-service line-coverage gate depends on.
//
// TWO LEGACY DEFECTS AND ONE LEGACY INCONSISTENCY ARE REPRODUCED HERE, NOT CORRECTED
//   They are each annotated at the point of reproduction so that a future reader cannot mistake any
//   of them for a porting error:
//     1. one `integer`-declared null sentinel serves the `decimal`, `double` and `real` overloads;
//     2. the coercion is unchecked, has no default arm, and reports nothing on failure;
//     3. two prefix tables in the same library disagree about what a `number` column is.
//
// ==================================================================================================

using System.Collections.Immutable;
using System.Globalization;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.DataServices.Validators;

/// <summary>
/// The numeric half of the DataWindow item-change type-coercion table, and the null sentinel the
/// legacy framework emits for every numeric column expression.
/// </summary>
/// <remarks>
/// <para>
/// Ported from <c>ws_objects/pfw.datawindow.services.pbl.src/dwnvlnumber.srf</c> and from the two
/// numeric arms of the coercion switch at
/// <c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L234-L237</c>.
/// </para>
/// <para>
/// The legacy switch at <c>se_cst_dw.sru:L231</c> has six arms and this refactor has five validator
/// files, so this one owns two of them. Both are numeric, which is coherent with all five numeric
/// <c>dwvaluetoexp</c> overloads sharing a single sentinel:
/// </para>
/// <list type="bullet">
///   <item>
///     the <c>Dec()</c> arm, <c>case "decim","real","numbe"</c> at <c>se_cst_dw.sru:L234-L235</c>,
///     surfaced as <see cref="DecimalCoercionColumnTypePrefixes"/>,
///     <see cref="IsDecimalCoercionColumnType"/> and <see cref="CoerceToDecimal"/>;
///   </item>
///   <item>
///     the <c>Long()</c> arm, <c>case "long","ulong"</c> at <c>se_cst_dw.sru:L236-L237</c>, surfaced
///     as <see cref="LongCoercionColumnTypePrefixes"/>, <see cref="IsLongCoercionColumnType"/> and
///     <see cref="CoerceToLong"/>.
///   </item>
/// </list>
/// <para>
/// The four arms this file does not own are <c>"char","char("</c> at <c>:L232-L233</c>,
/// <c>"datet"</c> at <c>:L238-L239</c>, <c>"date"</c> at <c>:L240-L241</c> and <c>"time"</c> at
/// <c>:L242-L243</c>. They belong to the four sibling files in this folder.
/// </para>
/// </remarks>
public static class NumberValidator
{
    // ----------------------------------------------------------------------------------------------
    // THE PUBLISHED SPELLING
    // ----------------------------------------------------------------------------------------------
    //
    // These two values are byte-exact contract. The literal was read out of the legacy source and
    // then checked byte by byte rather than by eye: `od -c` over the L19 occurrence yields
    //
    //     "   d   w   N   v   l   N   u   m   b   e   r   (   )   "
    //
    // so the casing is lowercase `dw`, capital `N` on `Nvl`, capital `N` on `Number`.
    //
    // WHY THESE ARE SINGLE-SOURCED HERE RATHER THAN RE-TYPED BY EACH CONSUMER
    // The legacy DOES hardcode the literal, five separate times, in `dwvaluetoexp.srf` at L19, L25,
    // L31, L37 and L43. Literal duplication is not itself an observable property, so reproducing the
    // duplication would buy no parity. What IS observable is a DRIFT between the name the expression
    // evaluator registers and the name the expression emitter writes. `n_cst_dwsvc.sru:L217` shows
    // `_of_evaluate` handing expression text straight to `Describe("Evaluate(...)")`, and
    // `n_cst_dwsvc_columnexp.sru:L2391` shows the caller detecting failure only as a returned `"?"`
    // or `"!"`. A one-character spelling drift would therefore surface as a runtime expression error
    // rather than a compile error, in a code path whose failure signal is a single punctuation mark.
    //
    // So `Validators/ValueToExpression.cs` emits `NullLiteralExpression` and
    // `Expressions/DataWindowExpressionEvaluator.cs` registers `FunctionName`; neither re-types the
    // text. This is an INTERNAL STRUCTURING CHOICE WITH NO OBSERVABLE EFFECT: the bytes that reach a
    // DataWindow expression are identical either way. It is recorded here because the constraint to
    // document every boundary-specific decision applies to the ones that turn out to be harmless as
    // much as to the ones that do not.
    //
    // NOTE ON THE IDENTIFIER, WHICH IS NOT THE VALUE. The legacy spelling lives in the string VALUE
    // and never in a C# identifier. The repository-root .editorconfig scopes its naming-analyzer
    // suppressions to the files on its BAND 3 roster - the single source of truth for that list - and
    // this is not one of them, so with warnings promoted to
    // errors a camelCase or underscored public member here would fail the build outright.

    /// <summary>
    /// The bare name of the legacy global function, exactly as a DataWindow expression must spell it:
    /// <c>dwNvlNumber</c>.
    /// </summary>
    /// <remarks>
    /// Register this, rather than a re-typed literal, in any expression function table. Source of
    /// truth: <c>ws_objects/pfw.datawindow.services.pbl.src/dwnvlnumber.srf:L6</c> for the
    /// declaration and <c>.../dwvaluetoexp.srf:L19</c> for the invoked spelling.
    /// </remarks>
    public const string FunctionName = "dwNvlNumber";

    /// <summary>
    /// The complete null-literal expression the legacy framework emits for a null numeric value:
    /// <c>dwNvlNumber()</c>.
    /// </summary>
    /// <remarks>
    /// Byte-exact contract, verified against
    /// <c>ws_objects/pfw.datawindow.services.pbl.src/dwvaluetoexp.srf</c> at L19 (the
    /// <c>decimal</c> overload), L25 (<c>int</c>), L31 (<c>long</c>), L37 (<c>double</c>) and L43
    /// (<c>real</c>). Emit this value rather than re-typing the literal.
    /// </remarks>
    public const string NullLiteralExpression = FunctionName + "()";

    // ----------------------------------------------------------------------------------------------
    // THE TYPED NULL
    // ----------------------------------------------------------------------------------------------
    //
    // WIDTH: THE LEGACY RETURN TYPE IS `integer`, NOT `decimal` AND NOT `long`.
    // `dwnvlnumber.srf:L6` reads `global function integer dwnvlnumber ()` and the body at `:L9`
    // declares `int nvl`. In PowerScript `integer` and `int` are the same type and it is a 16-BIT
    // SIGNED integer, so the faithful C# carrier is `short`, not `int` and not `decimal`.
    //
    // NULLABILITY: a PowerBuilder value-type null ports as a NULLABLE VALUE TYPE, never as a zero.
    // Collapsing null to zero would convert one state into a different one, which is the same class
    // of mistake that would turn "neither succeeded nor failed" into "succeeded" in the ported
    // return-code algebra. `SetNull(nvl)` at `:L10` followed by `return nvl` at `:L11` produces a
    // null `integer`, so the port produces a null `short?`.
    //
    // LEGACY DEFECT 1, REPRODUCED: ONE SENTINEL SERVES FIVE NUMERIC WIDTHS.
    // `dwvaluetoexp.srf` emits this same `integer`-declared function as the null literal for its
    // `decimal` overload at :L19, its `double` overload at :L37 and its `real` overload at :L43, as
    // well as for `int` at :L25 and `long` at :L31. A null decimal therefore does NOT receive a
    // decimal-typed sentinel; PowerBuilder tolerates the widening inside an expression because the
    // value is null and no arithmetic is performed on it. There is EXACTLY ONE numeric sentinel and
    // this is it. Introducing per-type sentinels would change the generated expression text, which
    // is precisely the observable surface this port has to preserve, so it is not done.

    /// <summary>
    /// Reproduces the legacy global function <c>dwnvlnumber()</c>: a null value whose declared type
    /// is the PowerScript 16-bit signed <c>integer</c>.
    /// </summary>
    /// <returns>
    /// Always <see langword="null"/>. That is the whole of the legacy behaviour, not a placeholder:
    /// <c>dwnvlnumber.srf:L9-L11</c> declares <c>int nvl</c>, calls <c>SetNull(nvl)</c> and returns
    /// it.
    /// </returns>
    /// <remarks>
    /// The nullable <see cref="short"/> return type is load-bearing in two directions. It records
    /// the legacy 16-bit width, and it keeps the null representable instead of degrading it to
    /// <c>0</c>. Callers that need the value inside generated expression text must use
    /// <see cref="NullLiteralExpression"/>, because a DataWindow expression carries the function
    /// CALL, not its evaluated result.
    /// </remarks>
    public static short? NullNumber() => null;

    // ----------------------------------------------------------------------------------------------
    // THE TWO PREFIX TOKEN SETS
    // ----------------------------------------------------------------------------------------------
    //
    // Transcribed verbatim from the inner switch at `se_cst_dw.sru:L231-L244`, whose six arms are, in
    // source order:
    //
    //     L231 choose case Left(dwo.ColType,5)
    //     L232     case "char","char("            L233 SetItem(row,Long(dwo.ID),data)          <- String
    //     L234     case "decim","real","numbe"    L235 SetItem(row,Long(dwo.ID),Dec(data))     <- HERE
    //     L236     case "long","ulong"            L237 SetItem(row,Long(dwo.ID),Long(data))    <- HERE
    //     L238     case "datet"                   L239 SetItem(row,Long(dwo.ID),DateTime(data)) <- DateTime
    //     L240     case "date"                    L241 SetItem(row,Long(dwo.ID),Date(data))    <- Date
    //     L242     case "time"                    L243 SetItem(row,Long(dwo.ID),Time(data))    <- Time
    //     L244 end choose
    //
    // Declaration order is preserved inside each set. Nothing depends on it today, because the arms
    // are mutually exclusive and the lookup is an equality test rather than a first-match scan, but a
    // reader comparing this file against the legacy source should be able to read straight across.
    //
    // LEGACY INCONSISTENCY 3, REPRODUCED RATHER THAN HARMONIZED: TWO PREFIX TABLES IN THE SAME
    // LIBRARY DISAGREE ABOUT WHAT A `number` COLUMN IS.
    //
    //   Table A, the coercion switch, `se_cst_dw.sru:L234-L237`
    //       "decim", "real", "numbe"  ->  Dec()   i.e. the decimal carrier
    //       "long",  "ulong"          ->  Long()  i.e. the integral carrier
    //
    //   Table B, `n_cst_dwsvc.sru::_of_convertcoltype`, `:L503-L518`
    //       "char", "char("           ->  COL_TYPE_STRING   = 1   [:L504-L505]
    //       "numbe", "long", "ulong"  ->  COL_TYPE_INTEGER  = 2   [:L506-L507]
    //       "decim", "real"           ->  COL_TYPE_DECIMAL  = 3   [:L508-L509]
    //       "datet"/"date"/"time"     ->  4 / 5 / 6               [:L510-L515]
    //       case else                 ->  COL_TYPE_UNKNOWN  = 0   [:L516-L517]
    //
    // The two agree on everything except `number`: Table B classifies "numbe" as COL_TYPE_INTEGER,
    // while Table A coerces it with `Dec()` alongside the genuine decimal types. A second, smaller
    // divergence is worth noting for the same reason: Table B has a `case else` arm and Table A has
    // NONE, so an unrecognised prefix yields COL_TYPE_UNKNOWN through Table B but performs no write
    // at all through Table A.
    //
    // Both behaviours are legacy behaviour and both are preserved. The two tables are NOT unified
    // into one shared C# table, because unifying them would have to pick a winner for `number` and
    // that choice would be a silent correction of the legacy. This file owns Table A only. Table B is
    // owned by whoever ports `_of_convertcoltype`, and the `COL_TYPE_*` values themselves are owned
    // by `Domain/DataWindowServiceHost.cs`.
    //
    // SEMANTIC ALIGNMENT, DOCUMENTED WITHOUT BEING EXPRESSED AS CODE. Under Table B the `Dec()` arm
    // corresponds to COL_TYPE_DECIMAL (= 3) for "decim" and "real" but to COL_TYPE_INTEGER (= 2) for
    // "numbe", and the `Long()` arm corresponds to COL_TYPE_INTEGER (= 2) throughout. Those values are
    // stated here as prose from `n_cst_dwsvc.sru:L26-L32` and are deliberately not redeclared.

    /// <summary>
    /// The five-character column-type prefixes the legacy coerces with <c>Dec()</c>, in legacy
    /// declaration order: <c>decim</c>, <c>real</c>, <c>numbe</c>.
    /// </summary>
    /// <remarks>
    /// Verbatim from <c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L234</c>. Two of the
    /// three tokens are shorter than five characters because the legacy compares the result of
    /// <c>Left(dwo.ColType,5)</c> for equality, and <c>Left</c> truncates without padding. Match
    /// against this set with <see cref="IsDecimalCoercionColumnType"/> rather than by hand, so that
    /// the truncation and comparison rules stay in one place.
    /// </remarks>
    public static ImmutableArray<string> DecimalCoercionColumnTypePrefixes { get; } =
        ["decim", "real", "numbe"];

    /// <summary>
    /// The five-character column-type prefixes the legacy coerces with <c>Long()</c>, in legacy
    /// declaration order: <c>long</c>, <c>ulong</c>.
    /// </summary>
    /// <remarks>
    /// Verbatim from <c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L236</c>. Match
    /// against this set with <see cref="IsLongCoercionColumnType"/>.
    /// </remarks>
    public static ImmutableArray<string> LongCoercionColumnTypePrefixes { get; } =
        ["long", "ulong"];

    // ----------------------------------------------------------------------------------------------
    // HAZARD H2: `Left(s,5)` IS NOT `Substring(0,5)`
    // ----------------------------------------------------------------------------------------------
    //
    // PowerScript `Left(s, n)` truncates safely: given a string shorter than `n` it returns the whole
    // string, and it never raises. C# `string.Substring(0, 5)` THROWS ArgumentOutOfRangeException on
    // anything shorter than five characters. That is not a theoretical difference here:
    //
    //   * five of the legacy switch's tokens are four characters long, "char", "real", "long",
    //     "date" and "time", so short column types are the normal case, not the edge case;
    //   * a `Describe` that fails returns "" or "!", so an EMPTY column type genuinely reaches this
    //     code on the error path;
    //   * the column type may be absent altogether, which arrives here as a null reference.
    //
    // The reproduction is therefore an explicit length-guarded take. It is exposed publicly because
    // the truncation is a documented step of the legacy algorithm and is worth being able to assert
    // on directly, and because `Domain/ItemChangeProtocol.cs` may want to truncate once and then ask
    // several validators rather than truncating inside each predicate.
    //
    // A NULL COLUMN TYPE MAPS TO THE EMPTY STRING, WHICH IS OBSERVATIONALLY EQUIVALENT.
    // In PowerScript `Left(NULL, 5)` is NULL, and a `choose case` over NULL matches no arm at all
    // because every comparison against NULL yields NULL rather than true. Returning the empty string
    // here reaches the identical observable outcome, since no arm token is empty, and it keeps this
    // helper's return type non-nullable so that callers need no null dance of their own. This is a
    // simplification of the internal representation and not of the behaviour.

    /// <summary>
    /// The prefix width the legacy coercion switch dispatches on: the <c>5</c> in
    /// <c>Left(dwo.ColType,5)</c> at <c>se_cst_dw.sru:L231</c>.
    /// </summary>
    /// <remarks>
    /// Named rather than inlined because the same width appears in the second prefix table at
    /// <c>n_cst_dwsvc.sru:L503</c>, and because the fact that it is five, not four and not six, is
    /// what makes <c>"datet"</c> and <c>"date"</c> distinguishable while collapsing
    /// <c>"decimal(2)"</c> and <c>"decimal(10)"</c> onto one token.
    /// </remarks>
    public const int ColumnTypePrefixLength = 5;

    /// <summary>
    /// Reproduces <c>Left(colType, 5)</c>, the truncation the legacy applies before dispatching the
    /// column-type coercion switch.
    /// </summary>
    /// <param name="columnType">
    /// A DataWindow column type as reported by <c>dwo.ColType</c> or <c>Describe(".ColType")</c>. May
    /// be <see langword="null"/>, empty, or shorter than five characters; none of those throws.
    /// </param>
    /// <returns>
    /// The first five characters of <paramref name="columnType"/>, or the whole value when it is
    /// shorter than five characters, or <see cref="string.Empty"/> when it is <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// Legacy locator: <c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L231</c>, and the
    /// identical truncation in the second, disagreeing prefix table at
    /// <c>.../n_cst_dwsvc.sru:L503</c>. This method is total: it has no failure mode and never
    /// throws.
    /// </remarks>
    public static string ColumnTypePrefix(string? columnType)
    {
        if (columnType is null)
        {
            return string.Empty;
        }

        // The length guard is the whole point of this method. `columnType[..5]` on a four-character
        // token would throw where PowerScript's `Left` returns the token unchanged.
        return columnType.Length <= ColumnTypePrefixLength ? columnType : columnType[..ColumnTypePrefixLength];
    }

    // ----------------------------------------------------------------------------------------------
    // HAZARD H6: EXACT EQUALITY, NEVER `StartsWith`
    // ----------------------------------------------------------------------------------------------
    //
    // This is the single most dangerous line in this file. PowerScript `choose case` compares for
    // EQUALITY, so the six arms of the legacy switch are mutually exclusive. Concretely:
    //
    //     Left("datetime", 5) == "datet"   which is NOT equal to "date"
    //
    // so a datetime column reaches the `"datet"` arm at `:L238` and nothing else. A port written with
    // `StartsWith` would break exactly that case, because "datetime".StartsWith("date") is true, and a
    // datetime column would silently route to the date arm and be coerced with the wrong conversion.
    // The failure would produce plausible-looking data rather than an exception, which is why it is
    // called out here instead of being left to a reviewer to notice. The regression is locked down by
    // test: a column type of "datetime" must match NEITHER of the two arms this file owns.
    //
    // The same reasoning rules out `Contains` and any prefix- or suffix-based matching.
    //
    // HAZARD H4: CASE SENSITIVITY IS PART OF THE CONTRACT
    // PowerScript's `=` on strings is case-SENSITIVE, so `choose case` is too, and every arm literal
    // in the legacy source is lowercase because `Describe(".ColType")` reports lowercase. The
    // comparison is therefore ordinal. `OrdinalIgnoreCase` is specifically NOT used: accepting
    // "DECIMAL(2)" where the legacy would accept only "decimal(2)" WIDENS the contract on a guess,
    // and a contract that cannot be reproduced exactly is to be narrowed with a defined behaviour
    // rather than widened. An uppercase column type consequently matches no arm, which is the same
    // outcome the legacy produces, and that too is locked down by test.
    //
    // Ordinal comparison is also the correct choice on its own merits: these tokens are protocol
    // identifiers, not human-readable text, so no culture-sensitive collation should touch them.

    /// <summary>
    /// Determines whether <paramref name="columnType"/> is one of the column types the legacy coerces
    /// with <c>Dec()</c>.
    /// </summary>
    /// <param name="columnType">
    /// A DataWindow column type as reported by <c>dwo.ColType</c>. May be <see langword="null"/>,
    /// empty or shorter than five characters; none of those throws.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when <see cref="ColumnTypePrefix"/> of the argument is exactly
    /// <c>decim</c>, <c>real</c> or <c>numbe</c>; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// Reproduces <c>case "decim","real","numbe"</c> at
    /// <c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L234</c>. The comparison is
    /// case-sensitive and ordinal, and it is an equality test on the truncated prefix rather than a
    /// prefix test on the whole value, so <c>"datetime"</c> matches nothing here and
    /// <c>"DECIMAL(2)"</c> matches nothing either.
    /// </remarks>
    public static bool IsDecimalCoercionColumnType(string? columnType) =>
        MatchesArm(columnType, DecimalCoercionColumnTypePrefixes);

    /// <summary>
    /// Determines whether <paramref name="columnType"/> is one of the column types the legacy coerces
    /// with <c>Long()</c>.
    /// </summary>
    /// <param name="columnType">
    /// A DataWindow column type as reported by <c>dwo.ColType</c>. May be <see langword="null"/>,
    /// empty or shorter than five characters; none of those throws.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when <see cref="ColumnTypePrefix"/> of the argument is exactly
    /// <c>long</c> or <c>ulong</c>; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// Reproduces <c>case "long","ulong"</c> at
    /// <c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L236</c>. Note the consequence of
    /// the five-character truncation combined with equality matching: a column type spelled
    /// <c>"unsigned long"</c> truncates to <c>"unsig"</c> and therefore matches nothing, exactly as
    /// in the legacy.
    /// </remarks>
    public static bool IsLongCoercionColumnType(string? columnType) =>
        MatchesArm(columnType, LongCoercionColumnTypePrefixes);

    /// <summary>
    /// The shared body of the two arm predicates: truncate as <c>Left(colType,5)</c> does, then test
    /// the result for ordinal equality against each token of the arm.
    /// </summary>
    /// <remarks>
    /// A linear scan is used deliberately in preference to a hash set. The arms hold three and two
    /// tokens, so a scan is faster than hashing the key, and it keeps the reproduction of the legacy
    /// <c>choose case</c> visible as a sequence of equality tests rather than hiding it behind a
    /// lookup structure whose comparer would then become the thing a reviewer has to verify.
    /// </remarks>
    private static bool MatchesArm(string? columnType, ImmutableArray<string> armPrefixes)
    {
        string prefix = ColumnTypePrefix(columnType);

        foreach (string armPrefix in armPrefixes)
        {
            // EQUALITY, ordinal, case-sensitive. See hazards H6 and H4 above; neither StartsWith nor
            // OrdinalIgnoreCase may be substituted here.
            if (string.Equals(prefix, armPrefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // ----------------------------------------------------------------------------------------------
    // HAZARD H5: CULTURE IS EXPLICIT, ALWAYS
    // ----------------------------------------------------------------------------------------------
    //
    // Every text-to-value conversion below passes CultureInfo.InvariantCulture explicitly. There is no
    // port of PowerBuilder's `String()`, `Dec()` or `Long()` primitives anywhere in this refactor -
    // the shared kernel's `Formatting` covers only `formatretcode.srf` and `sprintf.srf` - so this
    // file owns its own conversion and therefore owns the culture decision.
    //
    // The failure mode this closes is quiet and total. Under a comma-decimal ambient culture such as
    // de-DE, a bare `decimal.Parse("12.34")` yields 1234, and a bare `ToString()` of 12.34m yields
    // "12,34". Either would then be spliced into generated DataWindow expression text, where a comma
    // is an ARGUMENT SEPARATOR, so a single mis-cultured value silently changes the arity of the
    // expression it lands in. Nothing about that surfaces as an exception.
    //
    // Consequently no bare `Parse`, `TryParse`, `Convert.*`, `ToString()` or interpolated string is
    // permitted in this file, and the culture argument is never left to an overload default.
    //
    // ----------------------------------------------------------------------------------------------
    // HAZARD H3: THE LEGACY COERCION IS UNCHECKED AND REPORTS NOTHING
    // ----------------------------------------------------------------------------------------------
    //
    // The two legacy arms are, in full:
    //
    //     L234  case "decim","real","numbe"
    //     L235      SetItem(row,Long(dwo.ID),Dec(data))
    //     L236  case "long","ulong"
    //     L237      SetItem(row,Long(dwo.ID),Long(data))
    //
    // There is no validity test on `data`, the return value of `SetItem` is discarded, and the inner
    // `choose case` has NO `case else` arm at all, so an unrecognised prefix performs no write and
    // reports nothing to anybody. That is legacy behaviour, and this file reproduces it: the
    // legacy-faithful coercions below NEVER throw and never report. Adding a throw would be a
    // regression dressed up as robustness, and correcting behaviour is precisely what this refactor
    // is not permitted to do.
    //
    // Two entry points are therefore published for each arm, and the difference between them matters:
    //
    //   * `CoerceToDecimal` / `CoerceToLong`   - LEGACY-FAITHFUL, silent, total. These are the ones
    //                                            `Domain/ItemChangeProtocol.cs` uses on the
    //                                            item-change path.
    //   * `TryCoerceToDecimal` / `TryCoerceToLong` - ADDITIVE, with no legacy analogue whatsoever.
    //                                            They exist only for callers that genuinely need to
    //                                            know whether the text was numeric. THE ITEM-CHANGE
    //                                            PATH MUST NOT USE THEM: routing the item-change
    //                                            coercion through a reporting variant and then acting
    //                                            on the report would introduce an error path the
    //                                            legacy does not have.
    //
    // UNVERIFIED-FROM-REPOSITORY - pin against the behavioural oracle.
    // The exact value PowerBuilder's `Dec()` and `Long()` yield for UNCOERCIBLE NON-NULL text cannot
    // be proven from this repository: the functions are runtime primitives, no call site here
    // exercises the failure, and no document in the tree records it. The two candidate readings are
    // that the conversion yields 0, and that it yields null. THIS PORT RETURNS NULL, chosen because
    // it does not fabricate a numeric value that no input supplied, which is the same principle that
    // forbids collapsing a null return code to zero. Recording this here is what lets the
    // characterization suite pin it: the item belongs in `docs/PARITY.md` and in a
    // `characterization/workflows/` definition, neither of which is authored by this file.
    //
    // UNVERIFIED-FROM-REPOSITORY - pin against the behavioural oracle.
    // The exact ACCEPTANCE GRAMMAR of the two primitives is likewise unprovable here. Four specific
    // open questions, each of which the oracle can settle:
    //   1. does `Dec()` accept exponent notation such as "1e3"?          this port: yes
    //   2. does `Long()` accept exponent notation?                        this port: no
    //   3. does `Long("12.34")` fail, or truncate to 12?                  this port: fails
    //   4. does either accept a group separator such as "1,234"?          this port: no
    // The choices are the narrow ones. Question 3 in particular is answered by failing rather than
    // truncating, because a silent truncation would be data loss invented by the port; and a group
    // separator is rejected because under the invariant culture a comma in numeric text is far more
    // likely to be an expression argument separator that has leaked into a value.

    /// <summary>
    /// The number styles accepted by the <c>Dec()</c> arm: leading and trailing white space, a leading
    /// sign, a decimal point and an exponent.
    /// </summary>
    /// <remarks>
    /// <see cref="NumberStyles.Float"/> deliberately excludes <see cref="NumberStyles.AllowThousands"/>
    /// and <see cref="NumberStyles.AllowCurrencySymbol"/>. See the acceptance-grammar note above; both
    /// exclusions are the narrow choice pending oracle characterization.
    /// </remarks>
    private const NumberStyles DecimalCoercionStyles = NumberStyles.Float;

    /// <summary>
    /// The number styles accepted by the <c>Long()</c> arm: leading and trailing white space and a
    /// leading sign, and nothing else.
    /// </summary>
    /// <remarks>
    /// <see cref="NumberStyles.Integer"/> admits neither a decimal point nor an exponent, so
    /// <c>"12.34"</c> and <c>"1e3"</c> both fail rather than being silently truncated or expanded. See
    /// the acceptance-grammar note above.
    /// </remarks>
    private const NumberStyles LongCoercionStyles = NumberStyles.Integer;

    /// <summary>
    /// The legacy-faithful <c>Dec(data)</c> coercion of the <c>decim</c> / <c>real</c> / <c>numbe</c>
    /// arm. Never throws and never reports a failure.
    /// </summary>
    /// <param name="data">
    /// The edit text the DataWindow is about to apply. May be <see langword="null"/>, empty, or text
    /// that is not a number at all.
    /// </param>
    /// <returns>
    /// The parsed value when <paramref name="data"/> is numeric under the invariant culture;
    /// otherwise <see langword="null"/>, which is the value this arm writes when the legacy conversion
    /// produces nothing usable.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Reproduces <c>SetItem(row,Long(dwo.ID),Dec(data))</c> at
    /// <c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L235</c>. The legacy performs no
    /// validity test and discards the result of <c>SetItem</c>, so this method is total: there is no
    /// input for which it throws and no channel on which it reports.
    /// </para>
    /// <para>
    /// A <see langword="null"/> argument returns <see langword="null"/>, which is provable legacy
    /// behaviour rather than a choice: PowerScript propagates null through its conversion functions.
    /// The result for uncoercible non-null text is the UNVERIFIED-FROM-REPOSITORY decision documented
    /// above. Use <see cref="TryCoerceToDecimal"/> if, and only if, the caller needs the two cases
    /// distinguished; the item-change path must not.
    /// </para>
    /// <para>
    /// The carrier is <see cref="decimal"/> because the legacy target is <c>Dec()</c>, whose
    /// PowerScript type is a fixed-point decimal. A binary floating-point carrier would change the
    /// value written for `real` and `double` columns and is therefore not used, even though two of the
    /// three tokens in this arm name floating-point column types.
    /// </para>
    /// </remarks>
    public static decimal? CoerceToDecimal(string? data)
    {
        // Null in, null out. Provable legacy behaviour, and kept as its own branch so that the
        // reporting variant can treat it as a success rather than as invalid data.
        if (data is null)
        {
            return null;
        }

        // Explicit culture (H5) and explicit styles. TryParse rather than Parse is what makes this
        // total: "1e400" overflows and "abc" is not numeric, and neither raises here.
        return decimal.TryParse(data, DecimalCoercionStyles, CultureInfo.InvariantCulture, out decimal parsed)
            ? parsed
            : null;
    }

    /// <summary>
    /// The legacy-faithful <c>Long(data)</c> coercion of the <c>long</c> / <c>ulong</c> arm. Never
    /// throws and never reports a failure.
    /// </summary>
    /// <param name="data">
    /// The edit text the DataWindow is about to apply. May be <see langword="null"/>, empty, or text
    /// that is not a number at all.
    /// </param>
    /// <returns>
    /// The parsed value when <paramref name="data"/> is an integer under the invariant culture;
    /// otherwise <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Reproduces <c>SetItem(row,Long(dwo.ID),Long(data))</c> at
    /// <c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L237</c>, with the same totality and
    /// the same silence as <see cref="CoerceToDecimal"/>.
    /// </para>
    /// <para>
    /// WIDTH NOTE. PowerScript <c>long</c> is a 32-bit signed integer and <c>ulong</c> is a 32-bit
    /// unsigned integer, so this one arm serves two different legacy ranges. The transformation plan's
    /// type table maps <c>long</c> to C# <see cref="long"/>, and that single 64-bit signed carrier
    /// covers both legacy ranges without loss, since it spans the whole of the unsigned 32-bit range
    /// as well as the signed one. Splitting the arm into two carriers would therefore add a
    /// distinction the legacy switch does not make, and is not done.
    /// </para>
    /// <para>
    /// Text carrying a fractional part fails rather than truncating. That is the
    /// UNVERIFIED-FROM-REPOSITORY choice documented above, taken because inventing a truncation would
    /// be inventing data loss.
    /// </para>
    /// </remarks>
    public static long? CoerceToLong(string? data)
    {
        if (data is null)
        {
            return null;
        }

        return long.TryParse(data, LongCoercionStyles, CultureInfo.InvariantCulture, out long parsed)
            ? parsed
            : null;
    }

    /// <summary>
    /// The reporting counterpart of <see cref="CoerceToDecimal"/>. ADDITIVE: it has no legacy analogue
    /// and must not be used on the item-change path.
    /// </summary>
    /// <param name="data">The edit text to coerce. May be <see langword="null"/>.</param>
    /// <param name="value">
    /// Receives the coerced value on success, and <see langword="null"/> on failure or when
    /// <paramref name="data"/> is itself <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see cref="RetCode.OK"/> when <paramref name="data"/> is numeric, and also when it is
    /// <see langword="null"/>, because a null input legitimately produces a null value rather than a
    /// data error. <see cref="RetCode.E_INVALID_DATA"/> for any other text that does not coerce,
    /// including the empty string.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the one place in this file where the return-code algebra applies, and it applies to an
    /// ADDED path. The legacy coercion at <c>se_cst_dw.sru:L235</c> reports nothing at all, so no
    /// legacy behaviour is being described here; only a caller that has its own reason to distinguish
    /// numeric from non-numeric text should call this.
    /// </para>
    /// <para>
    /// The item-change protocol at <c>se_cst_dw.sru:L226-L251</c> must call
    /// <see cref="CoerceToDecimal"/> instead. Routing it through this method and then acting on the
    /// returned code would add an error path the legacy does not have, changing observable behaviour
    /// on exactly the branch the port exists to preserve.
    /// </para>
    /// <para>
    /// The return type is <see cref="long"/> because the ported return codes are declared
    /// <c>Constant Long</c> in <c>ws_objects/pfw.shared.pbl.src/retcode.sru</c> and are carried as
    /// <see cref="long"/> in <c>PowerFramework.Shared.Kernel</c>. It is not a boolean, so the tri-state
    /// return-code algebra stays available to callers rather than being flattened on the way out.
    /// </para>
    /// </remarks>
    public static long TryCoerceToDecimal(string? data, out decimal? value)
    {
        if (data is null)
        {
            value = null;
            return RetCode.OK;
        }

        if (decimal.TryParse(data, DecimalCoercionStyles, CultureInfo.InvariantCulture, out decimal parsed))
        {
            value = parsed;
            return RetCode.OK;
        }

        value = null;
        return RetCode.E_INVALID_DATA;
    }

    /// <summary>
    /// The reporting counterpart of <see cref="CoerceToLong"/>. ADDITIVE: it has no legacy analogue
    /// and must not be used on the item-change path.
    /// </summary>
    /// <param name="data">The edit text to coerce. May be <see langword="null"/>.</param>
    /// <param name="value">
    /// Receives the coerced value on success, and <see langword="null"/> on failure or when
    /// <paramref name="data"/> is itself <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see cref="RetCode.OK"/> when <paramref name="data"/> is an integer, and also when it is
    /// <see langword="null"/>. <see cref="RetCode.E_INVALID_DATA"/> for any other text that does not
    /// coerce, including the empty string and text carrying a fractional part.
    /// </returns>
    /// <remarks>
    /// Same additive status, same prohibition on the item-change path, and same reasoning for the
    /// <see cref="long"/> return type as <see cref="TryCoerceToDecimal"/>. The legacy counterpart is
    /// <c>SetItem(row,Long(dwo.ID),Long(data))</c> at <c>se_cst_dw.sru:L237</c>, which reports
    /// nothing.
    /// </remarks>
    public static long TryCoerceToLong(string? data, out long? value)
    {
        if (data is null)
        {
            value = null;
            return RetCode.OK;
        }

        if (long.TryParse(data, LongCoercionStyles, CultureInfo.InvariantCulture, out long parsed))
        {
            value = parsed;
            return RetCode.OK;
        }

        value = null;
        return RetCode.E_INVALID_DATA;
    }
}
