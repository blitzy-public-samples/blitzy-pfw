// ==================================================================================================
//  ValueToExpressionTests - THE SOLE COVERAGE OF Validators/ValueToExpression.cs
//  ------------------------------------------------------------------------------------------------
//  SUBJECT   services/dataservices-service/PowerFramework.DataServices/Validators/ValueToExpression.cs
//  ORACLE    ws_objects/pfw.datawindow.services.pbl.src/dwvaluetoexp.srf, all 71 lines, read
//            literally, together with the five typed-null producers it names:
//              dwnvlnumber.srf:L6   global function integer  dwnvlnumber ()
//              dwnvlstring.srf:L6   global function string   dwnvlstring ()
//              dwnvldatetime.srf:L6 global function datetime dwnvldatetime ()
//              dwnvldate.srf:L6     global function date     dwnvldate ()
//              dwnvltime.srf:L6     global function time     dwnvltime ()
//            and the two call shapes in n_cst_dwsvc_columnexp.sru - the six-arm typed dispatch at
//            :L2344-:L2359 and the expression-string invocation at :L2390.
//
// ==================================================================================================
//  GOVERNING RULES AND CONSTRAINTS
// ==================================================================================================
//
//  NO USER RULES GOVERN THIS FILE. `review_rules` reports, in full, that no user rules were
//  provided, and it was re-checked rather than assumed. None is therefore invented, inferred or
//  back-filled from convention here. The enterprise-standard baseline of AAP 0.7.2 applies in their
//  place, and the four binding non-rule constraints of AAP 0.7.3 that actually reach this file are
//  named at every point they bite rather than listed once and forgotten:
//
//    C-B / G2  PRESERVE BEHAVIOUR EXACTLY, REPLICATING DOCUMENTED DEFECTS RATHER THAN CORRECTING
//              THEM. Two defects are reachable from this file's subject and both are asserted as
//              defects, in REGION 3: the unescaped single quote of `dwvaluetoexp.srf:L48`, and the
//              type-widened numeric sentinel of `dwnvlnumber.srf:L6` that all five numeric
//              overloads share. Every assertion about them is EXACT. There is deliberately no
//              "accepts either" assertion anywhere in this file - a test that tolerated an escaped
//              quote as well as an unescaped one would let the silent correction the constraint
//              forbids through while still reporting green, which is worse than having no test.
//    C-C       THE ORACLE IS READ-ONLY. `dwvaluetoexp.srf` is opened for READING in REGION 1 so
//              that the locators below are load-bearing rather than decorative, and nothing under
//              `ws_objects/**` is written, moved, created or reformatted by any code here.
//    C-K       EVERY ROW CITES ITS LOCATOR. Each theory row carries the `dwvaluetoexp.srf` line it
//              characterises, and each locator is ASSERTED rather than merely carried - REGION 1
//              checks the cited line against the oracle's own text, so a wrong citation fails.
//    C-H       SOLE COVERAGE. CI enforces 80% line coverage per service from Cobertura. This file
//              is the only dedicated suite over `Validators/ValueToExpression.cs`, so its nine
//              `Convert` overloads, its nine `Bind` overloads, both sentinel families, all three
//              temporal wrappers and its single conversion site are covered systematically rather
//              than sampled. The sibling suites touch the type in passing - NullValueValidator-
//              ParityTests.cs from the producing side and DataWindowExpressionEvaluatorTests.cs
//              from the consuming side - and that overlap is deliberate on their part, not a
//              substitute for this file.
//
//  NAMING CONSTRAINT (AAP 0.4.5.3). The preserved SCREAMING_SNAKE identifiers are REFERENCED here
//  and DECLARED nowhere in this file. `DataWindowServiceBase.COL_TYPE_*` is read in REGION 5;
//  CA1707 and IDE1006 report DECLARATIONS only, and the root `.editorconfig` scopes its
//  suppressions for those two diagnostics to a named BAND 3 roster that does not include this file.
//  Declaring one here would therefore break the build under `TreatWarningsAsErrors`, which is
//  exactly the guard rail it is meant to be.
//
//  SHAPE (AAP 0.6.7). Table-driven parity matrices expressed as xunit theories with member data.
//  Plain xunit `Assert` throughout: the dependency inventory carries no assertion library and no
//  mocking library, and adding one would fail restore outright because `Directory.Packages.props`
//  holds no `PackageVersion` for it. Row payloads are restricted to serializable primitives so the
//  runner can address every row individually; a row that needs a CLR type or a temporal value
//  carries a short key or its integer components and the body resolves it through the helpers at
//  the foot of the class.
//
//  THE THEORY-VERSUS-FACT POLICY, STATED ONCE SO IT IS NOT RE-ARGUED PER MEMBER. Anything that is a
//  TABLE is a theory with member data - twelve tables, covering the nine prototypes, the nine null
//  paths, the five typed-null producers, the numeric matrix, the non-finite values, the culture
//  matrix, the string matrix, the temporal matrix, the three temporal format owners, the 45-cell
//  invariant sweep, the five abort-sentinel oracle lines and the six typed dispatch arms. What
//  remains a fact is what is NOT a table: a claim about the SHAPE OF A WHOLE SET (the surface is
//  exactly eighteen members; the five numeric answers collapse to one and the four others do not;
//  the six arms are distinct and cover every category but one), a claim about the ADJACENCY of two
//  lines, or a genuine singleton. Forcing one of those into a single-row theory would be worse than
//  leaving it a fact, not better: it advertises a matrix that does not exist, and the row it carries
//  would be the whole population rather than one case drawn from it. Each such member says so at its
//  own declaration, so a reader never has to guess whether the shape was chosen or defaulted to.
//
//  ORDINAL AND BYTE-EXACT. Every string comparison here is ordinal - `Assert.Equal(string, string)`
//  compares ordinally by construction, and every `Contains`, `StartsWith` and `EndsWith` call
//  passes `StringComparison.Ordinal` explicitly. Nothing is trimmed, normalised, case-folded or
//  rendered through an ambient culture, because the subject's output is text a DataWindow
//  expression parser reads and a characterization recording stores.
//
// ==================================================================================================
//  WHY THIS SUITE IS WORTH ITS LENGTH: THE ONE HAZARD IT EXISTS TO CATCH
// ==================================================================================================
//
//  Read the nine legacy bodies in the order they execute. EACH ONE BUILDS THE STRING FIRST AND ONLY
//  THEN TESTS `IsNull(sVal)` - never the input:
//
//      :L48  sVal = "'" + val + "'"                    :L49  if IsNull(sVal) then sVal = "dwNvlString()"
//      :L54  sVal = "DateTime('" + String(val) + "')"  :L55  if IsNull(sVal) then sVal = "dwNvlDateTime()"
//      :L60  sVal = "Date('" + String(val) + "')"      :L61  if IsNull(sVal) then sVal = "dwNvlDate()"
//      :L66  sVal = "Time('" + String(val) + "')"      :L67  if IsNull(sVal) then sVal = "dwNvlTime()"
//
//  That reads as a bug and is not one. PowerScript PROPAGATES NULL THROUGH CONCATENATION: if `val`
//  is null the whole concatenated expression is null, so `IsNull(sVal)` is true and the sentinel is
//  selected. In the five numeric bodies the same outcome arrives by a slightly different route -
//  `String(null)` is itself null - so :L19, :L25, :L31, :L37 and :L43 are reached.
//
//  C# HAS NO SUCH PROPAGATION, AND THE DIVERGENCE IS SILENT. `"DateTime('" + null + "')"` is
//  `DateTime('')`, and `default(T?).ToString()` is `""`, never null. A statement-for-statement
//  transliteration of the legacy would therefore emit:
//
//      INPUT          LEGACY OUTPUT      NAIVE TRANSLITERATION   HOW IT SURFACES
//      null string    dwNvlString()      ''                      NOT AT ALL - a valid empty
//                                                                literal, so a WRONG VALUE with no
//                                                                error anywhere. The worst of the four.
//      null datetime  dwNvlDateTime()    DateTime('')            "?"/"!" far from the cause
//      null date      dwNvlDate()        Date('')                "?"/"!" far from the cause
//      null time      dwNvlTime()        Time('')                "?"/"!" far from the cause
//      null numeric   dwNvlNumber()      (empty string)          silently ABORTS the calculation
//
//  No compiler diagnostic and no analyzer covers any of the five. REGION 2 is therefore the centre
//  of gravity of this file: it asserts each sentinel positively AND asserts the naive-port output
//  negatively, so a regression cannot pass by producing plausible-looking text.
//
//  The fifth row is the one that gives REGION 4 its weight. `""` is not a value in this system, it
//  is the CALLER'S ABORT SENTINEL: `n_cst_dwsvc_columnexp.sru:L2185` reads a cell through
//  `_of_GetItemExpValue` and :L2186 is `if sVal = "" then return ""`, abandoning the whole
//  expression preprocessing pass; :L2183 is the twin guard on the macro path. Inside
//  `_of_GetItemExpValue` the only legitimate producer of `""` is the `case else` arm at :L2357-:L2358,
//  reached for an UNKNOWN column type alone, because the six named arms at :L2345-:L2356 cover the
//  other six categories. An overload that answered `""` for a perfectly valid value would therefore
//  abort a calculation that had nothing wrong with it.
// ==================================================================================================

using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;

using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Expressions;
using PowerFramework.DataServices.Validators;

using Xunit;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// The parity suite for <see cref="ValueToExpression"/>, the port of
/// <c>ws_objects/pfw.datawindow.services.pbl.src/dwvaluetoexp.srf</c>.
/// </summary>
/// <remarks>
/// <para>
/// Five regions, in the order a reader needs them: the overload surface exists and is exactly nine
/// wide (REGION 1); the null paths select the sentinel and NOT the naive-port text (REGION 2); the
/// non-null paths are byte exact and culture invariant, preserved defects included (REGION 3); no
/// overload can ever answer <see langword="null"/> or the empty string (REGION 4); and the function
/// is reachable from inside an expression string under its legacy name, with the six typed arms
/// landing on the right overload (REGION 5).
/// </para>
/// <para>
/// Rows carry the ORACLE side of every claim - the legacy locator, the legacy type spelling, the
/// legacy sentinel text - and the implementation side is resolved inside the body from the subject.
/// That direction is deliberate and is the difference between a test and a tautology: a row that
/// embedded the ported constant would assert the constant against itself and pass whatever it said.
/// </para>
/// </remarks>
public sealed class ValueToExpressionTests
{
    // ==============================================================================================
    //  TYPE KEYS. Nine keys for the nine prototypes at dwvaluetoexp.srf:L6-L14, spelled as the C#
    //  source spells the mapped type so a failing row names the overload directly. Strings rather
    //  than an enum, for the two reasons the sibling suites give: a row reads better in a test report
    //  as text, and an enum declared here would add a public type to the test assembly for no gain.
    //
    //  THREE OF THE NINE ARE NOT THE OBVIOUS MAPPING, AND THE WIDTHS ARE THE REASON:
    //    int    -> short   PowerBuilder `integer` is 16 BIT, so the mapped type is `short`.
    //    real   -> float   PowerBuilder `real` is the 4 BYTE floating point type.
    //    long   -> long    PowerBuilder `long` is 32 BIT, so the mapped type is WIDER than the
    //                      legacy one. Widening cannot change the emitted text for any value the
    //                      legacy could hold, nor which branch is taken, so it is safe - unlike
    //                      merging `real` into `double`, which WOULD change the emitted text
    //                      because float renders at float precision.
    // ==============================================================================================

    private const string DecimalKey = "decimal";
    private const string ShortKey = "short";
    private const string LongKey = "long";
    private const string DoubleKey = "double";
    private const string FloatKey = "float";
    private const string StringKey = "string";
    private const string DateTimeKey = "DateTime";
    private const string DateOnlyKey = "DateOnly";
    private const string TimeOnlyKey = "TimeOnly";

    /// <summary>
    /// The nine keys in the oracle's own prototype order (<c>dwvaluetoexp.srf:L6-L14</c>), used by
    /// the surface theories to prove the set is complete and by REGION 4 to sweep every overload.
    /// </summary>
    private static readonly string[] AllTypeKeys =
    [
        DecimalKey,
        ShortKey,
        LongKey,
        DoubleKey,
        FloatKey,
        StringKey,
        DateTimeKey,
        DateOnlyKey,
        TimeOnlyKey,
    ];

    /// <summary>The five keys whose overloads share the one type-widened numeric sentinel.</summary>
    private static readonly string[] NumericTypeKeys =
    [
        DecimalKey,
        ShortKey,
        LongKey,
        DoubleKey,
        FloatKey,
    ];

    // ==============================================================================================
    //  ORACLE AND SOURCE PATHS. Read-only, both of them (C-C).
    // ==============================================================================================

    /// <summary>
    /// The oracle, relative to the repository root. Opened for READING only, by REGION 1, so that
    /// the line locators every row carries are checked against the file rather than trusted.
    /// </summary>
    private const string OracleRepositoryPath =
        "ws_objects/pfw.datawindow.services.pbl.src/dwvaluetoexp.srf";

    /// <summary>The subject, relative to the repository root.</summary>
    private const string SubjectRepositoryPath =
        "services/dataservices-service/PowerFramework.DataServices/Validators/ValueToExpression.cs";

    /// <summary>The two repository-root markers the upward walk anchors on, plus the subject.</summary>
    private const string SolutionMarkerFileName = "PowerFramework.slnx";

    private const string PackageVersionMarkerFileName = "Directory.Packages.props";

    /// <summary>The exact shape of a legacy prototype line, with one hole for the declared type.</summary>
    /// <remarks>
    /// Measured against the file, not reconstructed from memory: the nine lines are byte identical
    /// apart from the type token, they carry LF endings and no trailing whitespace, and the single
    /// space before the opening parenthesis is present in all nine.
    /// </remarks>
    private const string PrototypeLineShape = "global function string dwvaluetoexp (readonly {0} val)";

    /// <summary>The published constant name every null sentinel is sourced from.</summary>
    private const string NullLiteralConstantName = "NullLiteralExpression";

    // ==============================================================================================
    //  REGION 1 - THE OVERLOAD SURFACE: NINE ENTRY POINTS, AND ONLY NINE
    //  Source: dwvaluetoexp.srf:L6-L14 (the `forward prototypes` block)
    // ==============================================================================================
    //
    //  The nine-member surface IS the contract, which is why it is asserted by reflection rather
    //  than exercised implicitly by the value theories. Overload selection is what decides which
    //  legacy body a caller reaches, so a MISSING overload silently redirects a call site to a
    //  different conversion and an ADDED one offers a conversion the legacy never had. Neither shows
    //  up in any value assertion, because a value assertion can only test the overloads that exist.
    //
    //  The `Bind` family is held to the same nine, because `Validators/ValueToExpression.cs` records
    //  it as a parallel surface over exactly the nine mapped types - the safe-execution counterpart
    //  of the parity text, added under the AAP 0.1.5 clause that permits an implementation to be
    //  safer than the legacy where the change is unobservable. Nine plus nine is therefore the whole
    //  published method surface, and this region asserts that too: a tenth member of either family,
    //  or a stray public static helper, fails here.
    // ==============================================================================================

    /// <summary>
    /// One row per legacy prototype: the mapped-type key, the oracle line, and the type token that
    /// line declares.
    /// </summary>
    /// <remarks>
    /// All three columns are load bearing. The key selects the overload, the line number is checked
    /// against the oracle text, and the token is what that check looks for - so a row whose locator
    /// or whose legacy type spelling is wrong fails rather than passing with a stale comment.
    /// </remarks>
    public static TheoryData<string, int, string> OverloadPrototypes =>
        new()
        {
            { DecimalKey, 6, "decimal" },
            { ShortKey, 7, "int" },
            { LongKey, 8, "long" },
            { DoubleKey, 9, "double" },
            { FloatKey, 10, "real" },
            { StringKey, 11, "string" },
            { DateTimeKey, 12, "datetime" },
            { DateOnlyKey, 13, "date" },
            { TimeOnlyKey, 14, "time" },
        };

    /// <summary>
    /// Every legacy prototype has exactly one <c>Convert</c> overload over its mapped type, that
    /// overload returns <see cref="string"/>, and its parameter is <see langword="in"/> - and the
    /// cited oracle line really does declare the type the row names.
    /// </summary>
    [Theory]
    [MemberData(nameof(OverloadPrototypes))]
    public void EachLegacyPrototypeHasExactlyOneConvertOverloadOverItsMappedType(
        string typeKey,
        int oracleLine,
        string legacyTypeToken)
    {
        // THE LOCATOR IS ASSERTED, NOT ASSUMED (C-K). This is what stops the citations in this file
        // from decaying into comments nobody maintains: the claim below rests on what that line of
        // the oracle declares, so the line is read and matched. Read-only (C-C).
        string oracleText = ReadRepositoryLine(OracleRepositoryPath, oracleLine);
        string expectedPrototype = string.Format(
            CultureInfo.InvariantCulture,
            PrototypeLineShape,
            legacyTypeToken);

        Assert.Equal(expectedPrototype, oracleText);

        // And now the implementation side, resolved from the subject rather than restated in the row.
        Type mappedParameterType = MappedParameterType(typeKey);

        MethodInfo[] matching = DeclaredPublicStaticMethods("Convert")
            .Where(method => ParameterValueType(method) == mappedParameterType)
            .ToArray();

        Assert.Single(matching);

        MethodInfo overload = matching[0];

        // `global function string dwvaluetoexp (...)` - the return type is part of all nine
        // prototypes, and a `Convert` that answered anything else could not be spliced into an
        // expression at all.
        Assert.Equal(typeof(string), overload.ReturnType);

        ParameterInfo parameter = Assert.Single(overload.GetParameters());

        // `readonly` maps to `in`, applied to all nine with no deviation. Three facts together are
        // what `in` actually is at the metadata level, and asserting only one of them would also
        // accept `ref` or `out`: the parameter is by-reference, it is marked in, and it is not out.
        Assert.True(
            parameter.ParameterType.IsByRef,
            $"'Convert({typeKey})' does not take its parameter by reference, so the `readonly` "
                + $"declared at {OracleRepositoryPath}:L{oracleLine.ToString(CultureInfo.InvariantCulture)} "
                + "has not been mapped to `in`.");
        Assert.True(parameter.IsIn, $"'Convert({typeKey})' is by-reference but not `in`.");
        Assert.False(parameter.IsOut, $"'Convert({typeKey})' is an output parameter, which `in` is not.");
    }

    /// <summary>
    /// Every legacy prototype also has exactly one <c>Bind</c> overload over the same mapped type,
    /// answering <c>BoundLiteral&lt;T&gt;</c> closed over the UNDERLYING type - so the parity surface
    /// and the safe-execution surface cover the identical nine types and neither is wider.
    /// </summary>
    [Theory]
    [MemberData(nameof(OverloadPrototypes))]
    public void EachLegacyPrototypeHasExactlyOneBindOverloadOverTheSameMappedType(
        string typeKey,
        int oracleLine,
        string legacyTypeToken)
    {
        Type mappedParameterType = MappedParameterType(typeKey);

        MethodInfo[] matching = DeclaredPublicStaticMethods("Bind")
            .Where(method => ParameterValueType(method) == mappedParameterType)
            .ToArray();

        Assert.Single(matching);

        MethodInfo overload = matching[0];

        Assert.True(overload.ReturnType.IsGenericType);
        Assert.Equal(
            typeof(ValueToExpression.BoundLiteral<>),
            overload.ReturnType.GetGenericTypeDefinition());

        // Closed over the UNDERLYING type rather than the nullable one, because absence travels in
        // `HasValue` instead of in the type parameter - which is what lets the type argument stay
        // non-nullable and still distinguish an absent cell from an empty one.
        Assert.Equal(
            UnderlyingValueType(typeKey),
            overload.ReturnType.GetGenericArguments()[0]);

        ParameterInfo parameter = Assert.Single(overload.GetParameters());
        Assert.True(parameter.ParameterType.IsByRef);
        Assert.True(parameter.IsIn);
        Assert.False(parameter.IsOut);

        // The row's oracle columns are asserted here too rather than carried unused: this overload
        // exists because that prototype does, so the citation is checked on this path as well.
        Assert.Equal(
            string.Format(CultureInfo.InvariantCulture, PrototypeLineShape, legacyTypeToken),
            ReadRepositoryLine(OracleRepositoryPath, oracleLine));
    }

    /// <summary>
    /// The surface is EXACTLY nine `Convert` plus EXACTLY nine `Bind`, and nothing else is published
    /// as a public static method - so an added overload for a type the legacy lacks fails here.
    /// </summary>
    /// <remarks>
    /// The negative half is the half that matters and it cannot be expressed row by row. Nine rows
    /// asserting nine overloads exist would all still pass with a tenth present, and a tenth is a
    /// real hazard rather than a theoretical one: the legacy has no <c>bool</c>, no <c>byte[]</c>,
    /// no <c>Guid</c> and no <c>object</c> prototype, and a boolean in particular has a DIFFERENT
    /// legacy marshalling - <c>n_cst_dwsvc_columnexp.sru:L2269-L2274</c> and <c>:L2293-L2298</c>
    /// render one as the comparison <c>1=1</c> or <c>1=0</c>, never through this function.
    /// </remarks>
    [Fact]
    public void TheSurfaceIsExactlyNineConvertOverloadsAndNineBindOverloadsAndNothingElse()
    {
        MethodInfo[] convert = DeclaredPublicStaticMethods("Convert");
        MethodInfo[] bind = DeclaredPublicStaticMethods("Bind");

        Assert.Equal(AllTypeKeys.Length, convert.Length);
        Assert.Equal(AllTypeKeys.Length, bind.Length);
        Assert.Equal(9, AllTypeKeys.Length);

        // The parameter type sets are equal, element for element, and each is exactly the nine
        // mapped types - so neither family carries a type the other lacks. Ordered by full name with
        // an ordinal comparer so the comparison is order independent AND deterministic: reflection
        // does not guarantee overload order, and a culture-aware sort could differ between hosts.
        static Type[] Ordered(IEnumerable<Type> types) =>
            types.OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray();

        Type[] expected = Ordered(AllTypeKeys.Select(MappedParameterType));

        Assert.Equal(expected, Ordered(convert.Select(ParameterValueType)));
        Assert.Equal(expected, Ordered(bind.Select(ParameterValueType)));

        // NOTHING ELSE. Every public static method the type declares belongs to one of the two
        // families, so a helper promoted to public - or a tenth conversion added under a third name -
        // fails here rather than joining the surface unnoticed.
        MethodInfo[] allPublicStatic = typeof(ValueToExpression)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

        Assert.Equal(convert.Length + bind.Length, allPublicStatic.Length);
        Assert.All(
            allPublicStatic,
            method => Assert.True(
                string.Equals(method.Name, "Convert", StringComparison.Ordinal)
                    || string.Equals(method.Name, "Bind", StringComparison.Ordinal),
                $"'{method.Name}' is published as a public static method of "
                    + "ValueToExpression, which has a nine-plus-nine surface and no third family. "
                    + $"The oracle declares nine prototypes at {OracleRepositoryPath}:L6-L14 and no more."));
    }

    /// <summary>
    /// The eight value-type overloads take a NULLABLE VALUE TYPE, and the string overload takes a
    /// nullable annotated reference - so absence is representable on all nine and is never collapsed
    /// onto a sentinel value such as <see cref="DateOnly.MinValue"/> or zero.
    /// </summary>
    /// <remarks>
    /// This is the assertion that keeps REGION 2 possible. PowerBuilder has null for value types and
    /// the whole branch structure of the oracle depends on it; collapsing null onto
    /// <c>default</c> would make an absent date and a genuine <see cref="DateOnly.MinValue"/>
    /// indistinguishable, so a null date would render <c>Date('0001-01-01')</c> where the oracle
    /// renders <c>dwNvlDate()</c>. Note the two halves are checked by different mechanisms because
    /// they ARE different: a value type's nullability is in its runtime type, a reference type's is
    /// an annotation, so the latter needs <see cref="NullabilityInfoContext"/> to be visible at all.
    /// </remarks>
    [Theory]
    [MemberData(nameof(OverloadPrototypes))]
    public void AbsenceIsRepresentableOnEveryOverload(
        string typeKey,
        int oracleLine,
        string legacyTypeToken)
    {
        MethodInfo overload = ConvertOverload(typeKey);
        ParameterInfo parameter = overload.GetParameters()[0];
        Type valueType = ParameterValueType(overload);

        if (string.Equals(typeKey, StringKey, StringComparison.Ordinal))
        {
            Assert.False(valueType.IsValueType);

            NullabilityInfo nullability = new NullabilityInfoContext().Create(parameter);

            Assert.Equal(NullabilityState.Nullable, nullability.ReadState);
        }
        else
        {
            Assert.True(valueType.IsGenericType);
            Assert.Equal(typeof(Nullable<>), valueType.GetGenericTypeDefinition());
            Assert.Equal(UnderlyingValueType(typeKey), valueType.GetGenericArguments()[0]);
        }

        // The oracle columns, asserted on this path too: `readonly <token> val` is the declaration
        // whose null-carrying capability this test is about.
        Assert.Equal(
            string.Format(CultureInfo.InvariantCulture, PrototypeLineShape, legacyTypeToken),
            ReadRepositoryLine(OracleRepositoryPath, oracleLine));
    }

    // ==============================================================================================
    //  REGION 2 - THE NULL PATHS, AND THE NULL-PROPAGATION MECHANISM
    //  Source: dwvaluetoexp.srf:L19, :L25, :L31, :L37, :L43 (numeric), :L49, :L55, :L61, :L67
    // ==============================================================================================
    //
    //  THIS IS THE CENTRE OF GRAVITY OF THE FILE. The file header explains the mechanism in full; the
    //  short form is that the oracle tests the RESULT of a concatenation because PowerScript
    //  propagates null through `+`, and C# does not, so a statement-for-statement transliteration
    //  reaches the wrong branch on every one of the nine overloads without any diagnostic.
    //
    //  Each row therefore carries TWO expectations and both are asserted:
    //    * the SENTINEL the oracle selects, which must be produced; and
    //    * the NAIVE-PORT TEXT a transliteration would produce, which must NOT be produced.
    //
    //  The four non-numeric negatives are the highest-value assertions in this file, and the string
    //  one is the highest-value of the four: `''` is a perfectly well formed empty string literal, so
    //  a port that emitted it would return a WRONG VALUE with no error anywhere, whereas the three
    //  temporal negatives are at least malformed enough that `Describe("Evaluate(...)")` answers
    //  `"?"` or `"!"` somewhere downstream. The numeric negative is `""` itself, which is worse again
    //  in a different way - it is the caller's abort sentinel, so it would ABANDON a calculation
    //  rather than mis-answer one. REGION 4 sweeps that invariant across every input.
    // ==============================================================================================

    /// <summary>
    /// One row per overload: the type key, the oracle's assignment line for the sentinel, the
    /// sentinel text the oracle assigns there, and the text a naive transliteration would produce
    /// instead.
    /// </summary>
    /// <remarks>
    /// The naive-port column is not decoration and is not a hypothetical. It is what
    /// <c>"'" + val + "'"</c>, <c>"DateTime('" + String(val) + "')"</c>,
    /// <c>"Date('" + String(val) + "')"</c> and <c>"Time('" + String(val) + "')"</c> evaluate to in C#
    /// when <c>val</c> is absent, and what <c>String(val)</c> alone evaluates to for the five numeric
    /// bodies - measured on this toolchain rather than assumed. Asserting against it is what makes the
    /// difference between "the sentinel is produced" and "the sentinel is produced FOR THE RIGHT
    /// REASON".
    /// </remarks>
    public static TheoryData<string, int, string, string> NullPaths =>
        new()
        {
            // The five numeric bodies. All five assign the SAME sentinel, and all five would
            // transliterate to the empty string, because an absent nullable renders as "" in C#.
            { DecimalKey, 19, "dwNvlNumber()", "" },
            { ShortKey, 25, "dwNvlNumber()", "" },
            { LongKey, 31, "dwNvlNumber()", "" },
            { DoubleKey, 37, "dwNvlNumber()", "" },
            { FloatKey, 43, "dwNvlNumber()", "" },

            // The four with a wrapper, each with the exact text its own concatenation would yield.
            { StringKey, 49, "dwNvlString()", "''" },
            { DateTimeKey, 55, "dwNvlDateTime()", "DateTime('')" },
            { DateOnlyKey, 61, "dwNvlDate()", "Date('')" },
            { TimeOnlyKey, 67, "dwNvlTime()", "Time('')" },
        };

    /// <summary>
    /// Converting an ABSENT value emits the oracle's sentinel, byte for byte, on all nine overloads.
    /// </summary>
    [Theory]
    [MemberData(nameof(NullPaths))]
    public void ConvertingAnAbsentValueEmitsTheOracleSentinel(
        string typeKey,
        int oracleLine,
        string oracleSentinel,
        string naivePortText)
    {
        string emitted = ConvertAbsent(typeKey);

        // Byte for byte against the ORACLE's spelling, which is the row's own column and not the
        // ported constant - so this assertion cannot be satisfied by a constant agreeing with itself.
        Assert.Equal(oracleSentinel, emitted);

        // THE LOCATOR, ASSERTED (C-K). The oracle line really is the assignment this row claims:
        // `if IsNull(sVal) then sVal = "<sentinel>"`. A drifted citation fails here.
        Assert.Equal(
            $"if IsNull(sVal) then sVal = \"{oracleSentinel}\"",
            ReadRepositoryLine(OracleRepositoryPath, oracleLine));

        // AND THE SENTINEL COMES FROM THE OWNING VALIDATOR'S PUBLISHED CONSTANT, not from a literal
        // retyped in this test. Referencing the constant here is what makes this suite move together
        // with the producer if the spelling is ever pinned differently by characterization; a literal
        // would silently keep asserting the old text.
        Assert.Equal(OwningSentinelConstant(typeKey), emitted);

        // THE NEGATIVE. This is the assertion a naive transliteration fails and every other
        // assertion in this theory passes. See the region banner for why each of the five naive
        // outputs is dangerous in a different way.
        Assert.NotEqual(naivePortText, emitted);
    }

    /// <summary>
    /// The four non-numeric null paths do NOT produce the text a statement-for-statement
    /// transliteration of the oracle would produce - stated one overload at a time, with the
    /// mechanism named.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from the theory above rather than folded into it. The theory proves the
    /// sentinel is produced; this one exists so the four dangerous outputs are named EXPLICITLY in the
    /// test report, each beside the concatenation that would produce it, because a future reader
    /// deleting an assertion needs to see what is being ruled out and not merely that something is.
    /// </remarks>
    [Theory]
    [MemberData(nameof(NullPaths))]
    public void AnAbsentValueNeverProducesTheNaiveTransliterationText(
        string typeKey,
        int oracleLine,
        string oracleSentinel,
        string naivePortText)
    {
        string emitted = ConvertAbsent(typeKey);

        switch (typeKey)
        {
            case StringKey:

                // dwvaluetoexp.srf:L48 is `sVal = "'" + val + "'"`. In PowerScript that whole
                // expression is NULL when `val` is null, so :L49 selects the sentinel. In C# the
                // identical concatenation is `''` - a VALID two-character empty string literal - so
                // the mis-port produces a wrong VALUE and no error at all. The worst of the four.
                Assert.NotEqual("''", emitted);
                Assert.Equal(StringValidator.NullLiteralExpression, emitted);
                break;

            case DateTimeKey:

                // dwvaluetoexp.srf:L54 is `sVal = "DateTime('" + String(val) + "')"`; the null
                // propagates out of the concatenation and :L55 selects the sentinel. C# would emit
                // the malformed `DateTime('')`, which reaches Describe("Evaluate(...)") and comes
                // back as "?"/"!" far from its cause.
                Assert.NotEqual("DateTime('')", emitted);
                Assert.Equal(DateTimeValidator.NullLiteralExpression, emitted);
                break;

            case DateOnlyKey:

                // dwvaluetoexp.srf:L60 / :L61, same mechanism, malformed `Date('')`.
                Assert.NotEqual("Date('')", emitted);
                Assert.Equal(DateValidator.NullLiteralExpression, emitted);
                break;

            case TimeOnlyKey:

                // dwvaluetoexp.srf:L66 / :L67, same mechanism, malformed `Time('')`.
                Assert.NotEqual("Time('')", emitted);
                Assert.Equal(TimeValidator.NullLiteralExpression, emitted);
                break;

            default:

                // The five numeric bodies: `String(null)` is itself null in PowerScript, so :L19,
                // :L25, :L31, :L37 and :L43 are reached. In C# an absent nullable renders as the
                // EMPTY STRING, which is the caller's abort sentinel at
                // n_cst_dwsvc_columnexp.sru:L2185-L2186 - so this mis-port abandons a calculation
                // instead of mis-answering it.
                Assert.NotEqual(string.Empty, emitted);
                Assert.Equal(NumberValidator.NullLiteralExpression, emitted);
                break;
        }

        // The row's own two oracle columns close the loop: the naive text is what is ruled out, and
        // the sentinel is what the cited line assigns.
        Assert.NotEqual(naivePortText, emitted);
        Assert.Equal(
            $"if IsNull(sVal) then sVal = \"{oracleSentinel}\"",
            ReadRepositoryLine(OracleRepositoryPath, oracleLine));
    }

    /// <summary>
    /// Binding an ABSENT value reports no value, carries the same sentinel as its parity text, and -
    /// for the string overload - still distinguishes absence from an empty cell.
    /// </summary>
    /// <remarks>
    /// <c>Bind</c> is a second route to the same sentinel and therefore a second place the text could
    /// drift, so it is asserted rather than assumed to follow. The string row carries the extra claim
    /// because it is the only one where the absent and the empty case could be confused: an absent
    /// string binds <see cref="string.Empty"/> in <c>Value</c> with <c>HasValue</c> false, which is
    /// the same distinction <c>Convert</c> preserves by answering the sentinel rather than <c>''</c>.
    /// </remarks>
    [Theory]
    [MemberData(nameof(NullPaths))]
    public void BindingAnAbsentValueReportsNoValueAndCarriesTheSameSentinel(
        string typeKey,
        int oracleLine,
        string oracleSentinel,
        string naivePortText)
    {
        string parityText = BindAbsent(typeKey, out bool hasValue, out ValueToExpression.BoundLiteralKind kind);

        Assert.False(hasValue);
        Assert.Equal(oracleSentinel, parityText);
        Assert.Equal(OwningSentinelConstant(typeKey), parityText);
        Assert.NotEqual(naivePortText, parityText);

        // THE KIND DOES NOT RECORD ABSENCE - `HasValue` does. So an absent decimal is still a number
        // literal rather than a kind of its own, which is what lets a consumer bind the right type
        // for a null cell instead of losing the column's type along with its value.
        Assert.Equal(ExpectedLiteralKind(typeKey), kind);

        Assert.Equal(
            $"if IsNull(sVal) then sVal = \"{oracleSentinel}\"",
            ReadRepositoryLine(OracleRepositoryPath, oracleLine));
    }

    /// <summary>
    /// PRESERVED DEFECT (C-B): all five numeric overloads answer the ONE sentinel whose producer is
    /// declared to return <c>integer</c>, while the four non-numeric sentinels are type matched to
    /// their overload.
    /// </summary>
    /// <remarks>
    /// <c>dwnvlnumber.srf:L6</c> is <c>global function integer dwnvlnumber ()</c>, so a null
    /// <c>decimal</c> at <c>:L19</c>, a null <c>double</c> at <c>:L37</c> and a null <c>real</c> at
    /// <c>:L43</c> each receive a 16-BIT-DECLARED null that the expression engine widens, rather than
    /// one typed to the column. That this is a legacy CHOICE rather than an accident of the port is
    /// measurable from the other four producers, every one of which IS type matched -
    /// <c>dwnvlstring.srf:L6</c> returns <c>string</c>, <c>dwnvldatetime.srf:L6</c> <c>datetime</c>,
    /// <c>dwnvldate.srf:L6</c> <c>date</c> and <c>dwnvltime.srf:L6</c> <c>time</c> - so the numeric
    /// family is the only one that widens. Preserve it: introducing a per-type numeric sentinel would
    /// be exactly the silent correction constraint C-B forbids.
    /// </remarks>
    [Fact]
    public void TheOneIntegerDeclaredNumericSentinelIsSharedByAllFiveNumericOverloadsAndOnlyThose()
    {
        // A GENUINELY SET-LEVEL CLAIM, so a [Fact] and not a one-row [Theory]. It is about the SHAPE of
        // the whole answer set - five overloads collapsing to one text, four collapsing to four - and a
        // per-row theory could not express either half. The per-producer DECLARATIONS that make this a
        // legacy choice rather than a port artefact ARE a table, and they are one:
        // TheNullProducerDeclaresItsOwnReturnTypeExceptTheNumericOne below.

        // Five overloads, ONE distinct answer.
        string[] numeric = NumericTypeKeys.Select(ConvertAbsent).Distinct(StringComparer.Ordinal).ToArray();

        string sole = Assert.Single(numeric);
        Assert.Equal(NumberValidator.NullLiteralExpression, sole);

        // And the four non-numeric answers are four DISTINCT texts, none of them the numeric one - so
        // the sharing is confined to the family that shares it in the oracle.
        string[] nonNumeric = AllTypeKeys
            .Where(key => !NumericTypeKeys.Contains(key, StringComparer.Ordinal))
            .Select(ConvertAbsent)
            .ToArray();

        Assert.Equal(4, nonNumeric.Length);
        Assert.Equal(4, nonNumeric.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(NumberValidator.NullLiteralExpression, nonNumeric, StringComparer.Ordinal);
    }

    /// <summary>
    /// One row per typed-null producer: the type key, the producer's own <c>.srf</c> file, its
    /// declared legacy return type, and the emitted sentinel text.
    /// </summary>
    /// <remarks>
    /// Five rows for nine overloads, because the five numeric overloads share one producer. The
    /// declared-type column is what turns the type widening from an assertion into a measurement: four
    /// of the five producers return their own type and exactly one returns <c>integer</c> for three
    /// types that are not integers.
    /// </remarks>
    public static TheoryData<string, string, string, string> NullProducerDeclarations =>
        new()
        {
            { DecimalKey, "dwnvlnumber.srf", "integer", "dwNvlNumber()" },
            { StringKey, "dwnvlstring.srf", "string", "dwNvlString()" },
            { DateTimeKey, "dwnvldatetime.srf", "datetime", "dwNvlDateTime()" },
            { DateOnlyKey, "dwnvldate.srf", "date", "dwNvlDate()" },
            { TimeOnlyKey, "dwnvltime.srf", "time", "dwNvlTime()" },
        };

    /// <summary>
    /// Each producer's declared return type is read from its own <c>.srf:L6</c>, and only the numeric
    /// one is not type matched to the overloads that use it.
    /// </summary>
    /// <remarks>
    /// The row's own locator is the whole point: <c>dwnvlnumber.srf:L6</c> declaring <c>integer</c> is
    /// the evidence that a null <c>decimal</c>, a null <c>double</c> and a null <c>real</c> each
    /// receive a 16-bit-declared null the expression engine widens, rather than one typed to their
    /// column - and the four sibling rows are the contrast that makes it a deliberate legacy choice
    /// instead of an accident. Preserving it is constraint C-B; introducing a per-type numeric sentinel
    /// would be the silent correction that constraint forbids.
    /// </remarks>
    [Theory]
    [MemberData(nameof(NullProducerDeclarations))]
    public void TheNullProducerDeclaresItsOwnReturnTypeExceptTheNumericOne(
        string typeKey,
        string producerFileName,
        string declaredLegacyReturnType,
        string emittedSentinel)
    {
        string declaration = ReadRepositoryLine(
            $"ws_objects/pfw.datawindow.services.pbl.src/{producerFileName}",
            6);

        // The producer's own declaration, byte exact, from the read-only oracle (C-C, C-K).
        string producerObjectName = producerFileName.Replace(".srf", string.Empty, StringComparison.Ordinal);

        Assert.Equal(
            $"global function {declaredLegacyReturnType} {producerObjectName} ()",
            declaration);

        // The sentinel this producer's name spells, and the overload that emits it.
        Assert.Equal(emittedSentinel, ConvertAbsent(typeKey));
        Assert.Equal(emittedSentinel, OwningSentinelConstant(typeKey));

        // TYPE MATCHED, OR NOT - AND THE ONE EXCEPTION IS THE POINT. Four of the five producers declare
        // the same type their overload takes. The numeric one declares `integer` while serving
        // `decimal`, `short`, `long`, `double` and `real`, so three of those five receive a null whose
        // declared type is narrower than the column's.
        bool isNumericProducer = string.Equals(
            emittedSentinel,
            NumberValidator.NullLiteralExpression,
            StringComparison.Ordinal);

        if (isNumericProducer)
        {
            Assert.Equal("integer", declaredLegacyReturnType);
            Assert.Equal(5, NumericTypeKeys.Length);
            Assert.All(
                NumericTypeKeys,
                numericKey => Assert.Equal(emittedSentinel, ConvertAbsent(numericKey)));
        }
        else
        {
            // The declared legacy type names the same concept as the mapped CLR type: `string` for
            // string, `datetime` for DateTime, `date` for DateOnly, `time` for TimeOnly. Compared
            // case-insensitively on purpose, since PowerScript spells its type names in lower case
            // while C# spells the mapped types in Pascal case - and this is a NAME comparison, not
            // emitted text, so it is the one place case folding is correct rather than forbidden.
            Assert.Equal(
                declaredLegacyReturnType,
                MappedLegacyTypeName(typeKey),
                ignoreCase: true);

            // And exactly one overload uses it, unlike the numeric producer's five.
            Assert.Single(
                AllTypeKeys,
                key => string.Equals(
                    ConvertAbsent(key),
                    emittedSentinel,
                    StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// The sentinels are SINGLE SOURCED: the subject returns each owning validator's published
    /// constant and contains no copy of any sentinel's text as a literal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS WHERE SINGLE SOURCING IS ACTUALLY OBSERVABLE, AND IT IS THE SOURCE TEXT. Every value
    /// assertion above can only compare values, and a duplicated literal is value-identical by
    /// construction - which is the whole reason a duplicate is dangerous rather than untidy. The only
    /// artifact in which "referenced" and "copied" differ is the source file, so the source file is
    /// what this reads.
    /// </para>
    /// <para>
    /// Two complementary claims. The POSITIVE one is that each of the five owners appears as a
    /// returned constant, so the emitting overloads genuinely defer to the validator that owns the
    /// spelling. The NEGATIVE one is that no sentinel text appears as a string literal anywhere in
    /// the file's CODE, so a future edit cannot inline one beside the reference and leave both
    /// present - the state in which the two sides drift while every value assertion still passes.
    /// </para>
    /// <para>
    /// Comments are excluded before searching, and the distinction is essential rather than tidy: the
    /// subject QUOTES the legacy assignment <c>sVal = "dwNvlNumber()"</c> in a comment above each of
    /// the nine overloads, and it should - that citation is how a reader ties the C# arm back to
    /// <c>dwvaluetoexp.srf</c>. Searching the raw text would report those citations as duplication,
    /// which is the opposite of the truth.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(NullProducerDeclarations))]
    public void TheSentinelIsSingleSourcedFromTheOwningValidator(
        string typeKey,
        string producerFileName,
        string declaredLegacyReturnType,
        string emittedSentinel)
    {
        string code = CodeOnlyText(ReadRepositoryText(SubjectRepositoryPath));
        string owner = OwningValidatorTypeName(typeKey);

        // THE POSITIVE HALF: the emitting overload returns this validator's published constant, so it
        // genuinely defers to the type that owns the spelling.
        Assert.Contains(
            $"return {owner}.{NullLiteralConstantName};",
            code,
            StringComparison.Ordinal);

        // THE NEGATIVE HALF: no copy of this sentinel's text appears as a literal in the subject's
        // code, so a future edit cannot inline one beside the reference and leave both present.
        Assert.DoesNotContain($"\"{emittedSentinel}\"", code, StringComparison.Ordinal);

        // AND THE LOOP CLOSES BACK ON THE ORACLE: the owning constant's VALUE is the spelling the
        // producer's own declaration names, so the emitted text is the owner's constant, the owner's
        // constant is the oracle's spelling, and there is no second copy of either.
        //
        // THE ONE CASE-INSENSITIVE COMPARISON IN THIS FILE, AND IT IS DELIBERATE. The emitted text is
        // camel cased and the PowerBuilder DECLARATION is lower cased, because that is how an export
        // writes a declaration - PowerScript resolves either, being case insensitive about NAMES. This
        // is a name-resolution comparison, not an emitted-text comparison, so folding case is correct
        // here and forbidden everywhere else in this file. The casing of the EMITTED text is asserted
        // ordinally by ConvertingAnAbsentValueEmitsTheOracleSentinel and, for the registered spelling,
        // by TheFunctionIsRegisteredUnderTheLegacyExpressionSpelling.
        Assert.Equal(emittedSentinel, OwningSentinelConstant(typeKey));
        Assert.Contains(
            emittedSentinel[..^2],
            ReadRepositoryLine(
                $"ws_objects/pfw.datawindow.services.pbl.src/{producerFileName}",
                6),
            StringComparison.OrdinalIgnoreCase);

        // The row's declared-type column is asserted on this path too, so the row stays self-contained
        // rather than carrying a column only one of its two theories reads.
        Assert.False(string.IsNullOrWhiteSpace(declaredLegacyReturnType));
        Assert.DoesNotContain($"\"{declaredLegacyReturnType} \"", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// The subject owns its own value-to-text conversion and takes NO dependency on the shared
    /// kernel's formatting port.
    /// </summary>
    /// <remarks>
    /// A recorded decision worth pinning because it is the tempting reuse. <c>Shared.Kernel</c>'s
    /// formatting file ports <c>sprintf.srf</c> and <c>formatretcode.srf</c>, whose grammar is the
    /// PowerBuilder format-MASK form; the oracle's five numeric bodies call <c>String(val)</c> with NO
    /// mask, which is a different primitive with no port anywhere in the plan. Pressing one into
    /// service as the other would be a behavioural guess dressed as reuse, and it would move the
    /// emitted text of every numeric literal in the system.
    /// <para>
    /// A SINGLETON, so a fact rather than a one-row theory: the subject either takes that dependency
    /// or it does not, and there is no population of cases for a row to be drawn from.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSubjectOwnsItsConversionAndImportsNoSharedKernelFormatting()
    {
        string code = CodeOnlyText(ReadRepositoryText(SubjectRepositoryPath));

        Assert.DoesNotContain("using PowerFramework.Shared.Kernel", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Formatting.Sprintf", code, StringComparison.Ordinal);

        // What it uses instead: one invariant conversion site, named explicitly.
        Assert.Contains("CultureInfo.InvariantCulture", code, StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  REGION 3 - THE NON-NULL PATHS, BYTE EXACT
    //  Source: dwvaluetoexp.srf:L18, :L24, :L30, :L36, :L42 (numeric), :L48, :L54, :L60, :L66
    // ==============================================================================================
    //
    //  Three shapes, and the oracle gives each of them a different one:
    //    NUMERIC  the invariant text of the value and NOTHING ELSE - no quotes, no wrapping call.
    //             `sVal = String(val)`.
    //    STRING   the value between two single quotes, verbatim. `sVal = "'" + val + "'"`.
    //    TEMPORAL a DataWindow conversion call around the value's text, capitalisation included.
    //             `DateTime('...')`, `Date('...')`, `Time('...')`.
    //
    //  Every expected text below was MEASURED on this toolchain rather than reasoned about, because
    //  the numeric shape is the one place where a plausible guess is wrong: C# preserves a decimal's
    //  SCALE, so `1000.50m` renders `1000.50` and not `1000.5`, and `double`/`float` render the
    //  shortest round-trippable form which switches to E notation at the extremes.
    // ==============================================================================================

    /// <summary>
    /// One row per numeric case: the type key, the oracle's assignment line, the value in a
    /// serializable form, and the exact text expected.
    /// </summary>
    /// <remarks>
    /// The value travels as a <see cref="decimal"/> because it is the one primitive that carries every
    /// case in this table exactly - including the two-place <c>decimal(2)</c> scale that a
    /// <see cref="double"/> could not represent without rounding - and the body narrows it to the
    /// row's own type before calling. Rows whose value cannot be a decimal at all (the floating-point
    /// extremes and the four non-finite doubles) belong to the theories below this one instead.
    /// </remarks>
    public static TheoryData<string, int, decimal, string> NumericLiterals =>
        new()
        {
            // A positive integer, a negative integer and zero, through every numeric overload that
            // can hold them - so the sign handling and the zero case are covered nine ways, not one.
            { ShortKey, 24, 7m, "7" },
            { ShortKey, 24, -7m, "-7" },
            { ShortKey, 24, 0m, "0" },
            { LongKey, 30, 42m, "42" },
            { LongKey, 30, -42m, "-42" },
            { LongKey, 30, 0m, "0" },
            { DecimalKey, 18, 0m, "0" },
            { DoubleKey, 36, 0m, "0" },
            { FloatKey, 42, 0m, "0" },

            // THE FIXTURE'S SALARY COLUMN. dw_sqlite.srd:L12 declares `type=decimal(2)`, and the
            // fixture rows carry 1000.50 and 3000.00 - values whose SCALE is observable. A port that
            // trimmed trailing zeros would render 1000.5 and 3000, and every characterization
            // recording of that column would differ from the master.
            { DecimalKey, 18, 1000.50m, "1000.50" },
            { DecimalKey, 18, 3000.00m, "3000.00" },
            { DecimalKey, 18, -12.75m, "-12.75" },

            // A large `long`. 2147483647 is the largest value PowerBuilder's 32-bit `long` can hold,
            // so it is the widest value the legacy prototype at :L8 could ever have received.
            { LongKey, 30, 2147483647m, "2147483647" },
            { LongKey, 30, -2147483648m, "-2147483648" },

            // A `double` and a `real` with a fractional part. Both render 1.5 identically, which is
            // why the float-precision divergence needs the dedicated theory below to be visible.
            { DoubleKey, 36, 1.5m, "1.5" },
            { DoubleKey, 36, -0.125m, "-0.125" },
            { FloatKey, 42, 1.5m, "1.5" },
            { FloatKey, 42, -0.125m, "-0.125" },
        };

    /// <summary>
    /// A non-null numeric value renders as its invariant text with NO quotes and NO wrapping call.
    /// </summary>
    [Theory]
    [MemberData(nameof(NumericLiterals))]
    public void ANumericValueRendersAsBareInvariantText(
        string typeKey,
        int oracleLine,
        decimal value,
        string expectedText)
    {
        string emitted = ConvertNumeric(typeKey, value);

        Assert.Equal(expectedText, emitted);

        // BYTE FOR BYTE, not merely char for char. Encoding both sides and comparing the byte
        // sequences rules out an invisible difference that ordinal equality would also catch but that
        // a future reader might assume was never checked - a BOM, a non-breaking space, a homoglyph
        // digit, or a Unicode minus sign in place of the ASCII hyphen.
        Assert.Equal(Encoding.UTF8.GetBytes(expectedText), Encoding.UTF8.GetBytes(emitted));

        // NO QUOTES AND NO WRAPPING CALL. `sVal = String(val)` and nothing more, so a numeric literal
        // that arrived quoted would be read by the expression parser as a STRING and would then
        // compare and sort as one.
        Assert.DoesNotContain("'", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("(", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain(")", emitted, StringComparison.Ordinal);

        // NEVER THE COMMA DECIMAL SEPARATOR, whatever the host locale. A DataWindow expression parser
        // reads a comma as an ARGUMENT SEPARATOR, so `1,5` does not merely look wrong - it silently
        // turns one argument into two and the expression answers "?"/"!" only on machines whose
        // culture happens not to be invariant.
        Assert.DoesNotContain(",", emitted, StringComparison.Ordinal);

        // THE LOCATOR, ASSERTED (C-K): the cited line really is this overload's `String(val)`
        // assignment, read from the read-only oracle. The second assertion ties the row's own line
        // number to the single mapping the generated invariant matrix uses, so an authored table and a
        // generated one cannot come to disagree about where an overload lives.
        Assert.Equal("sVal = String(val)", ReadRepositoryLine(OracleRepositoryPath, oracleLine));
        Assert.Equal(OracleAssignmentLine(typeKey), oracleLine);
        Assert.Equal(
            ReadRepositoryLine(OracleRepositoryPath, OracleAssignmentLine(typeKey)),
            OracleAssignmentText(typeKey));
    }

    /// <summary>
    /// The five numeric overloads are NOT interchangeable: <c>real</c> renders at float precision and
    /// <c>double</c> at double precision, so merging them would move the emitted text.
    /// </summary>
    /// <remarks>
    /// This is the measurable half of the decision to keep nine members rather than one over a widest
    /// numeric type. The integral keys really are interchangeable for an integral value - which is why
    /// the divergence has to be demonstrated on a value that is not exactly representable in binary
    /// floating point, rather than asserted in a comment.
    /// <para>
    /// A CLAIM ABOUT A RELATIONSHIP BETWEEN OVERLOADS, so a fact: every assertion here compares two or
    /// more overloads' answers to each other, which is not something a row describing one case can
    /// express. The per-case renderings that feed it ARE tables, and they are
    /// <see cref="NumericLiterals"/> and <see cref="NonFiniteNumerics"/>.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFloatAndDoubleOverloadsRenderAtTheirOwnPrecisionAndAreNotInterchangeable()
    {
        const double asDouble = 0.1d + 0.2d;

        // The classic non-representable sum. As a double it renders with its full shortest
        // round-trippable expansion; narrowed to a float it renders shorter, because fewer bits are
        // being round-tripped.
        Assert.Equal("0.30000000000000004", ValueToExpression.Convert(asDouble));
        Assert.Equal("0.3", ValueToExpression.Convert((float)asDouble));
        Assert.NotEqual(ValueToExpression.Convert(asDouble), ValueToExpression.Convert((float)asDouble));

        // The extremes, which is where the shortest round-trippable form switches to E notation. Both
        // remain non-empty, so the never-empty invariant of REGION 4 holds even here, and both are
        // recorded as characterization items in the subject rather than claimed to match PowerBuilder.
        Assert.Equal("1.7976931348623157E+308", ValueToExpression.Convert(double.MaxValue));
        Assert.Equal("3.4028235E+38", ValueToExpression.Convert(float.MaxValue));
        Assert.Equal("-1.7976931348623157E+308", ValueToExpression.Convert(double.MinValue));
        Assert.Equal("-3.4028235E+38", ValueToExpression.Convert(float.MinValue));

        // And the integral keys ARE interchangeable for an integral value, which is the contrast that
        // makes the divergence above meaningful rather than incidental. It also settles the one arm
        // where the .NET column-type dispatch of REGION 5 lands on a different overload from the
        // legacy's - the legacy INTEGER arm passes `GetItemNumber`, declared to return a double, while
        // the .NET path types the cell as an integer - because both answer the same text.
        Assert.Equal("30", ValueToExpression.Convert((long?)30L));
        Assert.Equal("30", ValueToExpression.Convert((double?)30d));
        Assert.Equal("30", ValueToExpression.Convert((short?)30));
        Assert.Equal("30", ValueToExpression.Convert((decimal?)30m));
        Assert.Equal("30", ValueToExpression.Convert((float?)30f));
    }

    /// <summary>
    /// One row per non-finite floating-point value: the type key, the oracle's assignment line, the
    /// value's name and the exact text expected.
    /// </summary>
    /// <remarks>
    /// The value travels as a NAME rather than as a number, because a non-finite value cannot survive
    /// the <see cref="decimal"/> payload the ordinary numeric table uses and because a row reading
    /// <c>NaN</c> is clearer in a test report than one reading a serialized bit pattern.
    /// <c>NegativeZero</c> is included even though it is finite: it is the fourth value whose rendering
    /// is surprising, and a port that normalised it would silently lose the sign a DataWindow would
    /// display.
    /// </remarks>
    public static TheoryData<string, int, string, string> NonFiniteNumerics =>
        new()
        {
            { DoubleKey, 36, "NaN", "NaN" },
            { DoubleKey, 36, "PositiveInfinity", "Infinity" },
            { DoubleKey, 36, "NegativeInfinity", "-Infinity" },
            { DoubleKey, 36, "NegativeZero", "-0" },
            { FloatKey, 42, "NaN", "NaN" },
            { FloatKey, 42, "PositiveInfinity", "Infinity" },
            { FloatKey, 42, "NegativeInfinity", "-Infinity" },
        };

    /// <summary>
    /// A non-finite value renders non-empty, so the never-empty invariant holds - and its text is
    /// recorded as a characterization item rather than asserted to match PowerBuilder.
    /// </summary>
    /// <remarks>
    /// None of these is a valid DataWindow numeric literal, so each produces the same detectable
    /// <c>"?"</c>/<c>"!"</c> answer downstream as the unescaped-quote defect does. What matters for
    /// THIS file is only that none of them can answer the empty string and thereby abort a
    /// calculation, and that is what is asserted. PowerBuilder's own rendering of them is not shown
    /// anywhere in the legacy tree, so no parity claim is made about the text.
    /// </remarks>
    [Theory]
    [MemberData(nameof(NonFiniteNumerics))]
    public void ANonFiniteNumericRendersNonEmptyEvenThoughItIsNotAValidLiteral(
        string typeKey,
        int oracleLine,
        string nonFiniteName,
        string expectedText)
    {
        string emitted = ConvertNonFinite(typeKey, nonFiniteName);

        Assert.Equal(expectedText, emitted);

        // THE CLAIM THAT MATTERS FOR THIS FILE, and the only one made about these seven values: none
        // of them can answer the empty string, so none of them can reach the caller's abort sentinel.
        Assert.NotEqual(string.Empty, emitted);
        Assert.False(string.IsNullOrWhiteSpace(emitted));

        // NO PARITY CLAIM IS MADE ABOUT THE TEXT. PowerBuilder's rendering of a non-finite value is not
        // shown anywhere in the legacy tree, so these expectations characterise THIS toolchain and are
        // recorded as such on the subject. None of the seven is a valid DataWindow numeric literal
        // either, so each produces the same detectable "?"/"!" downstream as the unescaped-quote defect.
        Assert.DoesNotContain(",", emitted, StringComparison.Ordinal);

        // The cited oracle line is still just `sVal = String(val)` - the oracle has no special arm for
        // a non-finite value, which is exactly why its rendering is unspecified rather than defined.
        Assert.Equal("sVal = String(val)", ReadRepositoryLine(OracleRepositoryPath, oracleLine));
        Assert.Equal(OracleAssignmentLine(typeKey), oracleLine);
    }

    /// <summary>
    /// One row per culture-sensitivity case: the type key, the oracle's assignment line, the value, the
    /// text the subject must emit, and the text the AMBIENT culture would have produced.
    /// </summary>
    /// <remarks>
    /// The fifth column is the counter-proof, carried per row rather than asserted once, so a reader
    /// can see for each value whether the culture had anything to change. The two integral rows are
    /// included precisely because their counter-proof is IDENTICAL to their expected text - an integer
    /// has no decimal separator to move - which is the contrast that shows the other rows are detecting
    /// something real.
    /// </remarks>
    public static TheoryData<string, int, decimal, string, string> CultureInvariantRenderings =>
        new()
        {
            { DoubleKey, 36, 1.5m, "1.5", "1,5" },
            { DecimalKey, 18, 1000.50m, "1000.50", "1000,50" },
            { FloatKey, 42, -0.125m, "-0.125", "-0,125" },
            { DecimalKey, 18, -12.75m, "-12.75", "-12,75" },
            { ShortKey, 24, -7m, "-7", "-7" },
            { LongKey, 30, 2147483647m, "2147483647", "2147483647" },
        };

    /// <summary>
    /// The numeric rendering is INVARIANT: the same value renders identically under a comma-decimal
    /// ambient culture, and the row's counter-proof shows what a culture-sensitive rendering would
    /// have produced instead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The failure this forecloses is environment dependent and silent, which is exactly the kind a
    /// unit test has to catch because no review will: on a host whose culture uses a comma decimal
    /// separator, an unpinned conversion emits <c>1,5</c>, the expression parser reads the comma as an
    /// argument separator, and the expression answers <c>"?"</c>/<c>"!"</c> on that host and nowhere
    /// else.
    /// </para>
    /// <para>
    /// THE COUNTER-PROOF IS NOT OPTIONAL. Asserting only that the output is unchanged would also pass
    /// on a host where the chosen culture does not exist and silently falls back to invariant, in
    /// which case the test proves nothing at all. Asserting that the BARE rendering differs under the
    /// same culture is what establishes the test had something to detect, so every row carries its own
    /// ambient-culture text as a column.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(CultureInvariantRenderings))]
    public void TheRenderingIsInvariantUnderACommaDecimalCulture(
        string typeKey,
        int oracleLine,
        decimal value,
        string expectedText,
        string ambientCultureCounterProof)
    {
        CultureInfo commaDecimal = CultureInfo.GetCultureInfo("de-DE");

        // The chosen culture really does use a comma, on this host, right now. Without this the whole
        // row could pass vacuously on a machine whose ICU data resolved de-DE to something else.
        Assert.Equal(",", commaDecimal.NumberFormat.NumberDecimalSeparator);

        RunUnderCulture(commaDecimal, () =>
        {
            // THE SUBJECT, UNDER THE HOSTILE CULTURE: unchanged and comma free.
            string emitted = ConvertNumeric(typeKey, value);

            Assert.Equal(expectedText, emitted);
            Assert.DoesNotContain(",", emitted, StringComparison.Ordinal);

            // THE COUNTER-PROOF, PER ROW. The same value rendered through the AMBIENT culture, which is
            // what an unpinned conversion would have used. Where the row's counter-proof differs from
            // its expected text, this assertion is what establishes the culture is genuinely in force
            // and the row had something to detect; where they agree - an integral value has no decimal
            // separator to move - the row is still swept, and the agreement is asserted rather than
            // assumed so the table has no untested cell.
            Assert.Equal(ambientCultureCounterProof, value.ToString(CultureInfo.CurrentCulture));
        });

        // AND THE ROW'S ORACLE LINE (C-K): still the plain `String(val)` assignment, which is precisely
        // why the culture has to be pinned in the port - the oracle names no format and no locale, so
        // an unpinned .NET conversion would inherit the host's.
        Assert.Equal("sVal = String(val)", ReadRepositoryLine(OracleRepositoryPath, oracleLine));
        Assert.Equal(OracleAssignmentLine(typeKey), oracleLine);
    }

    /// <summary>
    /// The three temporal wrappers are invariant under the same hostile culture, which matters because
    /// a locale can reorder a date's components or render a 12-hour clock with a designator.
    /// </summary>
    /// <remarks>
    /// A separate theory from the numeric one because the mechanism is different and worth keeping
    /// visible: the numeric path is pinned at the subject's own single conversion site, while the
    /// temporal paths are pinned inside the three producing validators' formatters. Both have to hold,
    /// and if only one did, only one of these two theories would fail - which is the diagnostic
    /// difference between them.
    /// </remarks>
    [Theory]
    [MemberData(nameof(TemporalLiterals))]
    public void TheTemporalRenderingIsInvariantUnderACommaDecimalCulture(
        string typeKey,
        int oracleLine,
        string oracleAssignment,
        int firstComponent,
        int secondComponent,
        int thirdComponent,
        string expectedText)
    {
        CultureInfo commaDecimal = CultureInfo.GetCultureInfo("de-DE");

        RunUnderCulture(commaDecimal, () =>
        {
            string emitted = ConvertTemporal(
                typeKey,
                firstComponent,
                secondComponent,
                thirdComponent,
                out string innerTextFromOwningValidator);

            Assert.Equal(expectedText, emitted);
            Assert.Equal(
                ExpectedWrapperPrefix(typeKey) + innerTextFromOwningValidator + "')",
                emitted);

            // No comma, no reordering, no AM/PM designator - the three things a locale would otherwise
            // introduce into a temporal rendering.
            Assert.DoesNotContain(",", emitted, StringComparison.Ordinal);
            Assert.DoesNotContain("AM", emitted, StringComparison.Ordinal);
            Assert.DoesNotContain("PM", emitted, StringComparison.Ordinal);
        });

        Assert.Equal(oracleAssignment, ReadRepositoryLine(OracleRepositoryPath, oracleLine));
    }

    // ----------------------------------------------------------------------------------------------
    //  THE STRING SHAPE, AND THE PRESERVED DEFECT
    //  Source: dwvaluetoexp.srf:L48 - `sVal = "'" + val + "'"`
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// One row per string case: the value and the exact text expected, plus a flag marking the rows
    /// that exercise the preserved unescaped-quote defect.
    /// </summary>
    public static TheoryData<string, int, string, bool> StringLiterals =>
        new()
        {
            // Every row cites :L48, because the string shape has exactly one body line and that is it -
            // `sVal = "'" + val + "'"`. The column is a constant across the table on purpose: carrying
            // it per row keeps the C-K discipline uniform with every other table here, and the body
            // asserts it rather than trusting it.

            // A plain word, a value with spaces, and a value with leading and trailing spaces that are
            // NOT trimmed - because trimming would change the value a cell holds.
            { "Alice", 48, "'Alice'", false },
            { "New York", 48, "'New York'", false },
            { "  padded  ", 48, "'  padded  '", false },

            // Non-ASCII, taken from the fixture rows the sibling suites use. The wrapper is a byte
            // pair either side of whatever the value is; nothing is transliterated or normalised.
            { "深圳市南山区", 48, "'深圳市南山区'", false },

            // AN EMPTY NON-NULL STRING. Two quote characters and NOT the sentinel: the sentinel is
            // selected on null alone, and `''` is a well formed empty string literal rather than the
            // caller's abort sentinel. This row is the boundary between REGION 2 and REGION 3.
            { "", 48, "''", false },

            // THE PRESERVED DEFECT. See the assertions and the comment block below.
            { "O'Brien", 48, "'O'Brien'", true },
            { "it's 'quoted'", 48, "'it's 'quoted''", true },
            { "'", 48, "'''", true },
        };

    /// <summary>
    /// A non-null string renders as the value between two single quotes, verbatim - and an embedded
    /// quote is PRESERVED UNESCAPED, which is a legacy defect this port reproduces rather than
    /// repairs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PRESERVED DEFECT (C-B / G2). <c>dwvaluetoexp.srf:L48</c> is <c>sVal = "'" + val + "'"</c> and
    /// nothing else: no doubling, no backslash, no rejection, no strip. <c>Convert("O'Brien")</c>
    /// therefore answers <c>'O'Brien'</c>, and that text is asserted EXACTLY. "Fixing" it here would
    /// be precisely the silent correction of legacy behaviour that constraint C-B forbids - and it
    /// would be a behavioural change in both directions, because the malformed text is DETECTABLE
    /// today: it reaches <c>Describe("Evaluate(...)")</c>, which answers <c>"?"</c> or <c>"!"</c>, and
    /// the caller raises an expression error at <c>n_cst_dwsvc_columnexp.sru:L2391-L2392</c>.
    /// Pre-sanitising the quote away would convert that visible error into a different, quietly
    /// successful result.
    /// </para>
    /// <para>
    /// IT IS REACHABLE FROM ANY <c>char</c> COLUMN, so it is not a curiosity. The fixture's own
    /// <c>name</c> and <c>address</c> columns are <c>char(100)</c> and <c>char(200)</c>
    /// (<c>dw_sqlite.srd:L9</c>, <c>:L11</c>), any apostrophe typed into either reaches this overload
    /// through the <c>COL_TYPE_STRING</c> arm at <c>n_cst_dwsvc_columnexp.sru:L2350</c>, and an
    /// apostrophe in a name is ordinary rather than exotic.
    /// </para>
    /// <para>
    /// AND IT IS THE SAME CLASS OF UNESCAPED INTERPOLATION AAP 0.6.4 ANALYSES AS THE MECHANICAL ROOT
    /// OF THE ESTATE'S SQL-INJECTION EXPOSURE - the where-clause setter that splices a raw clause
    /// string, and the drop-down search service that interpolates user data unescaped into a filter
    /// expression. The resolution the plan gives for all of them is the same and it is the reason the
    /// subject publishes two surfaces: the parity text is preserved byte for byte and is compatibility
    /// and diagnostic data, while a consumer that has to EVALUATE binds the value instead of splicing
    /// the text. That is asserted here as well, on the two-quote row, because a BALANCED quote leaves
    /// the parity text well formed and meaning something the caller never asked for - which is a
    /// sharper hazard than the unbalanced case and is easy to miss.
    /// </para>
    /// <para>
    /// THERE IS NO "ACCEPTS EITHER" ASSERTION. Every expectation here is exact equality against the
    /// malformed text. A test that tolerated an escaped form as well would let the correction through
    /// while still reporting green, which is worse than not testing the defect at all.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(StringLiterals))]
    public void AStringValueIsWrappedInSingleQuotesVerbatimIncludingAnyEmbeddedQuote(
        string value,
        int oracleLine,
        string expectedText,
        bool exercisesTheUnescapedQuoteDefect)
    {
        string emitted = ValueToExpression.Convert(value);

        Assert.Equal(expectedText, emitted);
        Assert.Equal(Encoding.UTF8.GetBytes(expectedText), Encoding.UTF8.GetBytes(emitted));

        // The wrapper is exactly one quote either side, and the value between them is UNTOUCHED -
        // asserted by reconstruction rather than by inspection, so no trimming, casing, normalisation
        // or escaping can hide inside a passing row.
        Assert.StartsWith("'", emitted, StringComparison.Ordinal);
        Assert.EndsWith("'", emitted, StringComparison.Ordinal);
        Assert.Equal(value, emitted[1..^1]);
        Assert.Equal(value.Length + 2, emitted.Length);

        // NEVER THE SENTINEL. A non-null value takes the literal path however empty it is, which is
        // what makes `''` and `dwNvlString()` two different answers to two different questions.
        Assert.NotEqual(StringValidator.NullLiteralExpression, emitted);

        if (exercisesTheUnescapedQuoteDefect)
        {
            // The embedded quote really is still there, inside the wrapper.
            Assert.Contains("'", emitted[1..^1], StringComparison.Ordinal);

            // NOT DOUBLED, NOT BACKSLASH-ESCAPED, NOT STRIPPED. Each repair is CONSTRUCTED and then
            // ruled out by exact inequality, rather than probed for with a sub-string search. The
            // difference matters: a search for a doubled quote reports a false positive on a value
            // that is ITSELF a single quote, where the correct unescaped output `'''` already contains
            // two adjacent quotes. Building the three repairs and comparing against them is exact for
            // every value, and it names in the test report which repair a regression attempted.
            Assert.NotEqual(
                "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'",
                emitted);
            Assert.NotEqual(
                "'" + value.Replace("'", "\\'", StringComparison.Ordinal) + "'",
                emitted);
            Assert.NotEqual(
                "'" + value.Replace("'", string.Empty, StringComparison.Ordinal) + "'",
                emitted);

            // The quote count is the value's own plus the two wrappers - so a doubling repair, which
            // would raise the count, fails here even if the surrounding text still matched.
            Assert.Equal(
                value.Count(character => character == '\'') + 2,
                emitted.Count(character => character == '\''));

            // AND THE SAFE PATH IS AVAILABLE AND CARRIES THE VALUE UNMODIFIED. This is the half that
            // makes preserving the defect defensible rather than negligent: a consumer that evaluates
            // binds `Value`, and `Value` is the caller's own text with nothing done to it.
            ValueToExpression.BoundLiteral<string> bound = ValueToExpression.Bind(value);

            Assert.True(bound.HasValue);
            Assert.Equal(value, bound.Value);
            Assert.Equal(emitted, bound.ParityText);
            Assert.Equal(ValueToExpression.BoundLiteralKind.StringLiteral, bound.Kind);
        }
        else
        {
            Assert.DoesNotContain("'", emitted[1..^1], StringComparison.Ordinal);
        }

        // THE ROW'S LOCATOR, ASSERTED (C-K): the cited line really is the plain concatenation this
        // whole theory rests on. If it ever read anything else, every claim above would need revisiting.
        Assert.Equal(
            "sVal = \"'\" + val + \"'\"",
            ReadRepositoryLine(OracleRepositoryPath, oracleLine));
        Assert.Equal(OracleAssignmentLine(StringKey), oracleLine);
    }

    // ----------------------------------------------------------------------------------------------
    //  THE THREE TEMPORAL SHAPES
    //  Source: dwvaluetoexp.srf:L54, :L60, :L66
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// One row per temporal case: the type key, the oracle's assignment line, the legacy assignment
    /// text, the value's integer components, and the exact text expected.
    /// </summary>
    /// <remarks>
    /// The value travels as integer components rather than as a <see cref="DateTime"/> so every row
    /// stays serializable and individually addressable by the runner, and the body composes the value
    /// of the row's own type. Unused components are zero and the body ignores them for the two
    /// narrower types.
    /// </remarks>
    public static TheoryData<string, int, string, int, int, int, string> TemporalLiterals =>
        new()
        {
            // DATETIME. A whole-second value and a sub-second one that renders identically, because
            // the canonical format carries no fractional-second field - a recorded decision on the
            // producing side and a characterization item there, pinned here so both paths move
            // together if it is ever settled differently.
            {
                DateTimeKey, 54, "sVal = \"DateTime('\" + String(val) + \"')\"",
                2024, 3, 5, "DateTime('2024-03-05 07:08:09')"
            },
            {
                DateTimeKey, 54, "sVal = \"DateTime('\" + String(val) + \"')\"",
                1999, 12, 31, "DateTime('1999-12-31 07:08:09')"
            },

            // DATE - INCLUDING THE FIXTURE'S OWN BIRTH COLUMN SHAPE. dw_sqlite.srd:L26 gives the
            // `birth` column `editmask.mask="yyyy-mm-dd"`, so the emitted inner text is exactly what
            // the DataWindow would DISPLAY for that column, and 1990-01-02 is the fixture's own first
            // row. A single-digit month and day are deliberate: they are what proves the rendering is
            // zero padded rather than merely correct-looking.
            { DateOnlyKey, 60, "sVal = \"Date('\" + String(val) + \"')\"", 1990, 1, 2, "Date('1990-01-02')" },
            { DateOnlyKey, 60, "sVal = \"Date('\" + String(val) + \"')\"", 1970, 5, 6, "Date('1970-05-06')" },
            { DateOnlyKey, 60, "sVal = \"Date('\" + String(val) + \"')\"", 2024, 11, 30, "Date('2024-11-30')" },

            // TIME. A morning value, so the zero-padded 24-hour hour field is exercised; and an
            // afternoon one, which is what a 12-hour rendering would corrupt.
            { TimeOnlyKey, 66, "sVal = \"Time('\" + String(val) + \"')\"", 3, 4, 5, "Time('03:04:05')" },
            { TimeOnlyKey, 66, "sVal = \"Time('\" + String(val) + \"')\"", 14, 7, 9, "Time('14:07:09')" },
            { TimeOnlyKey, 66, "sVal = \"Time('\" + String(val) + \"')\"", 0, 0, 0, "Time('00:00:00')" },
        };

    /// <summary>
    /// A non-null temporal value is wrapped in its DataWindow conversion call, with the inner text
    /// produced by the OWNING validator's canonical invariant format constant.
    /// </summary>
    [Theory]
    [MemberData(nameof(TemporalLiterals))]
    public void ATemporalValueIsWrappedInItsConversionCallUsingTheOwningValidatorsFormat(
        string typeKey,
        int oracleLine,
        string oracleAssignment,
        int firstComponent,
        int secondComponent,
        int thirdComponent,
        string expectedText)
    {
        string emitted = ConvertTemporal(
            typeKey,
            firstComponent,
            secondComponent,
            thirdComponent,
            out string innerTextFromOwningValidator);

        Assert.Equal(expectedText, emitted);
        Assert.Equal(Encoding.UTF8.GetBytes(expectedText), Encoding.UTF8.GetBytes(emitted));

        // THE WRAPPER, BYTE EXACT INCLUDING ITS CAPITAL LETTERS. `DateTime(`, `Date(` and `Time(` are
        // read straight out of :L54, :L60 and :L66; a lower-cased wrapper is a different function name
        // to an expression parser.
        Assert.StartsWith(ExpectedWrapperPrefix(typeKey), emitted, StringComparison.Ordinal);
        Assert.EndsWith("')", emitted, StringComparison.Ordinal);

        // AND THE INNER TEXT IS THE OWNING VALIDATOR'S OWN RENDERING, NOT A SECOND COPY OF THE FORMAT.
        // Comparing against the producer's formatter rather than against a literal is what keeps this
        // assertion correct after the format's characterization item is settled: if the constant
        // changes, BOTH sides move, and the round trip that Describe("Evaluate(...)") depends on stays
        // intact. The literal in the row is the second, independent check on today's value.
        Assert.Equal(
            ExpectedWrapperPrefix(typeKey) + innerTextFromOwningValidator + "')",
            emitted);

        // The inner text is fixed width and zero padded, which is why the round trip is exact for
        // every value rather than for most of them.
        Assert.Equal(
            PublishedTemporalFormat(typeKey).Length,
            emitted.Length - ExpectedWrapperPrefix(typeKey).Length - 2);

        // NEVER THE SENTINEL, and never a comma - the same argument-separator hazard the numeric
        // theory guards, reached here through a locale that reorders or re-punctuates a date.
        Assert.NotEqual(OwningSentinelConstant(typeKey), emitted);
        Assert.DoesNotContain(",", emitted, StringComparison.Ordinal);

        // THE LOCATOR, ASSERTED (C-K): the cited oracle line is the concatenation the row claims,
        // wrapper spelling included - and the row's line and text both match the single mapping the
        // generated invariant matrix uses.
        Assert.Equal(oracleAssignment, ReadRepositoryLine(OracleRepositoryPath, oracleLine));
        Assert.Equal(OracleAssignmentLine(typeKey), oracleLine);
        Assert.Equal(OracleAssignmentText(typeKey), oracleAssignment);
    }

    /// <summary>
    /// The three temporal formats are DECLARED ONCE, on the producing validators, and the subject
    /// carries no format string of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The values on today's toolchain are <c>yyyy-MM-dd HH:mm:ss</c>, <c>yyyy-MM-dd</c> and
    /// <c>HH:mm:ss</c>, and each is itself an unsettled characterization item recorded on its own
    /// constant. What is NOT unsettled, and is what this test pins, is that there is exactly one
    /// declaration of each: the emitter composes its wrapper around the producer's formatter, so the
    /// text it emits and the text that producer's coercion accepts cannot drift apart.
    /// </para>
    /// <para>
    /// The negative half is asserted against the SOURCE TEXT for the same reason REGION 2's
    /// single-sourcing assertion is: a duplicated format string is value-identical to the constant it
    /// duplicates until the day one of the two is edited alone, so no value assertion could ever see
    /// it.
    /// </para>
    /// </remarks>
    /// <summary>
    /// One row per temporal type: the type key, the oracle's wrapper line, the format constant's value
    /// on today's toolchain, and the formatter call the emitter must make.
    /// </summary>
    public static TheoryData<string, int, string, string> TemporalFormatOwners =>
        new()
        {
            { DateTimeKey, 54, "yyyy-MM-dd HH:mm:ss", "DateTimeValidator.FormatExpressionValue(" },
            { DateOnlyKey, 60, "yyyy-MM-dd", "DateValidator.FormatValue(" },
            { TimeOnlyKey, 66, "HH:mm:ss", "TimeValidator.Format(" },
        };

    [Theory]
    [MemberData(nameof(TemporalFormatOwners))]
    public void TheTemporalFormatIsSingleSourcedFromTheOwningValidator(
        string typeKey,
        int oracleLine,
        string publishedFormat,
        string expectedFormatterCall)
    {
        // Today's value of the constant, pinned so a change is visible - it is an unsettled
        // characterization item, and the point of this theory is not the value but that there is
        // exactly ONE declaration of it.
        Assert.Equal(publishedFormat, PublishedTemporalFormat(typeKey));

        string code = CodeOnlyText(ReadRepositoryText(SubjectRepositoryPath));

        // POSITIVE: the emitter calls this producer's formatter rather than formatting for itself.
        Assert.Contains(expectedFormatterCall, code, StringComparison.Ordinal);

        // NEGATIVE: no copy of this format appears as a literal in the subject's code. Asserted
        // against the source text for the same reason the sentinel check is: a duplicated format string
        // is value-identical to the constant it duplicates until the day one of the two is edited
        // alone, so no value assertion could ever see it.
        Assert.DoesNotContain($"\"{publishedFormat}\"", code, StringComparison.Ordinal);
        Assert.DoesNotContain($"\"{publishedFormat[..4]}", code, StringComparison.Ordinal);

        // AND THE ORACLE LINE THIS FORMAT SITS INSIDE (C-K): the wrapper concatenation whose middle
        // operand is `String(val)` - the maskless conversion this format constant stands in for.
        Assert.Contains(
            "+ String(val) +",
            ReadRepositoryLine(OracleRepositoryPath, oracleLine),
            StringComparison.Ordinal);
        Assert.Equal(OracleAssignmentLine(typeKey), oracleLine);
    }

    /// <summary>
    /// The fixture's <c>birth</c> column really does carry the mask this suite's date rows are aligned
    /// to, so the emitted <c>Date('...')</c> text matches what the DataWindow would display.
    /// </summary>
    /// <remarks>
    /// Read from the fixture rather than restated, so the citation is load-bearing (C-K). The mask is
    /// spelled in PowerBuilder's lower-case form, <c>yyyy-mm-dd</c>, while the .NET format constant is
    /// <c>yyyy-MM-dd</c> - the same field layout in the two frameworks' own notations, which is why
    /// the two spellings differ while the rendered text does not. Reading only one of them and
    /// assuming the other would miss exactly that.
    /// <para>
    /// A SINGLETON, so a fact: there is exactly one <c>birth</c> column in exactly one fixture, and
    /// the date rows that consume its layout are already a table in <see cref="TemporalLiterals"/>.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFixtureBirthColumnMaskMatchesTheEmittedDateLayout()
    {
        string birthColumn = ReadRepositoryLine("ws_objects/pfw.tests.pbl.src/dw_sqlite.srd", 26);

        Assert.Contains("name=birth", birthColumn, StringComparison.Ordinal);
        Assert.Contains("editmask.mask=\"yyyy-mm-dd\"", birthColumn, StringComparison.Ordinal);

        // The two notations agree field for field: four year digits, two month digits, two day digits,
        // hyphen separated - so the rendered text is what that column displays.
        Assert.Equal(
            "yyyy-mm-dd".Length,
            DateValidator.CanonicalValueFormat.Length);
        Assert.Equal(
            "yyyy-mm-dd",
            DateValidator.CanonicalValueFormat.Replace("MM", "mm", StringComparison.Ordinal));

        // And the fixture's own first-row birth value renders accordingly.
        Assert.Equal(
            "Date('1990-01-02')",
            ValueToExpression.Convert((DateOnly?)new DateOnly(1990, 1, 2)));
    }

    // ==============================================================================================
    //  REGION 4 - THE INVARIANT: NO OVERLOAD MAY EVER ANSWER NULL OR THE EMPTY STRING
    //  Source: n_cst_dwsvc_columnexp.sru:L2183, :L2185-:L2186 (the consumers) and :L2357-:L2358
    //          (the only legitimate producer)
    // ==============================================================================================
    //
    //  THIS IS THE SINGLE MOST CONSEQUENTIAL INVARIANT OF THE SUBJECT, and it is worth stating exactly
    //  why, because "never return empty" reads like defensive tidiness and is nothing of the kind.
    //
    //  `""` IS NOT A VALUE IN THIS SYSTEM. IT IS THE CALLER'S ABORT SENTINEL.
    //  `n_cst_dwsvc_columnexp.sru:L2185` reads a cell through `_of_GetItemExpValue` and :L2186 is
    //  `if sVal = "" then return ""` - which abandons the ENTIRE expression preprocessing pass, not
    //  just that one variable. :L2183 is the twin guard on the macro path. So an empty answer does not
    //  produce a wrong result that a later assertion might catch; it produces NO result, silently, and
    //  the expression the user asked for simply does not evaluate.
    //
    //  AND THE ABORT IS SUPPOSED TO BE UNREACHABLE FOR A VALID CELL. Inside `_of_GetItemExpValue` the
    //  only legitimate producer of `""` is the `case else` arm at :L2357-:L2358, which is reached for
    //  an UNKNOWN column type alone - the six named arms at :L2345-:L2356 cover string, integer,
    //  decimal, datetime, date and time between them. An overload here that answered `""` for a
    //  perfectly good value would therefore counterfeit "this column has no type I recognise", and it
    //  would do so for a cell that had nothing wrong with it at all.
    //
    //  THE MATRIX IS EXHAUSTIVE ON PURPOSE. The naive-transliteration failure of REGION 2 lands
    //  exactly here for the five numeric overloads, and the extremes are where a formatting change
    //  would land: a hypothetical port that pinned a numeric format mask could easily render some
    //  boundary value as nothing at all. Sweeping every overload against every boundary is cheap, and
    //  it is the only shape of test that cannot miss the one cell that breaks.
    //
    //  A NOTE ON THE MINIMUM LENGTH, STATED PRECISELY RATHER THAN CONVENIENTLY. It is tempting to
    //  strengthen this into "at least two characters", since every sentinel is far longer and every
    //  quoted or wrapped literal is at least two. That is FALSE for the bare numeric shape:
    //  `Convert((short?)0)` is `"0"`, one character, and correctly so. The invariant that actually
    //  holds - and the one the caller's guard actually tests - is non-empty. So non-empty is what is
    //  asserted universally, and the two-character floor is asserted only where it genuinely applies.
    // ==============================================================================================

    /// <summary>The input-kind keys the invariant matrix sweeps.</summary>
    private const string AbsentInputKind = "absent";

    private const string DefaultInputKind = "default";

    private const string MinimumInputKind = "minimum";

    private const string MaximumInputKind = "maximum";

    private const string EmptyStringInputKind = "empty";

    /// <summary>
    /// The five input kinds, used to build the exhaustive matrix below.
    /// </summary>
    /// <remarks>
    /// The empty kind is meaningful for the string overload alone; for the other eight the matrix
    /// generator maps it onto <see cref="DefaultInputKind"/> rather than dropping the row, so every
    /// overload is swept the same number of times and the table has no holes to hide a gap in.
    /// </remarks>
    private static readonly string[] AllInputKinds =
    [
        AbsentInputKind,
        DefaultInputKind,
        MinimumInputKind,
        MaximumInputKind,
        EmptyStringInputKind,
    ];

    /// <summary>
    /// EVERY overload crossed with EVERY interesting input: nine types by five input kinds.
    /// </summary>
    public static TheoryData<string, string, int> InvariantMatrix
    {
        get
        {
            TheoryData<string, string, int> rows = new();

            foreach (string typeKey in AllTypeKeys)
            {
                foreach (string inputKind in AllInputKinds)
                {
                    // Every cell carries the overload's own oracle body line, so the C-K discipline
                    // holds across the generated table exactly as it does across the authored ones.
                    rows.Add(typeKey, inputKind, OracleAssignmentLine(typeKey));
                }
            }

            return rows;
        }
    }

    /// <summary>
    /// No overload, for any input, answers <see langword="null"/> or the empty string - on either the
    /// <c>Convert</c> surface or the <c>Bind</c> surface's parity text.
    /// </summary>
    [Theory]
    [MemberData(nameof(InvariantMatrix))]
    public void NoOverloadEverAnswersNullOrTheEmptyString(
        string typeKey,
        string inputKind,
        int oracleLine)
    {
        // THE ROW'S LOCATOR, ASSERTED (C-K): the cited line is this overload's own body line, and its
        // text is the assignment whose RESULT the oracle tests for null - the statement that makes the
        // whole never-empty question live in the first place.
        Assert.Equal(OracleAssignmentText(typeKey), ReadRepositoryLine(OracleRepositoryPath, oracleLine));

        string emitted = ConvertInput(typeKey, inputKind, out string parityText);

        // THE INVARIANT, both ways round. `Assert.NotNull` and the empty check are separate
        // assertions because they are separate failures with separate causes: a null would be a
        // missing return path, an empty string would be a formatting path that produced nothing.
        Assert.NotNull(emitted);
        Assert.NotEqual(string.Empty, emitted);
        Assert.False(
            string.IsNullOrEmpty(emitted),
            $"Convert({typeKey}) answered null or the empty string for the '{inputKind}' input. "
                + "The empty string is the CALLER'S ABORT SENTINEL at "
                + "n_cst_dwsvc_columnexp.sru:L2185-L2186, so this does not mis-answer a calculation - "
                + "it abandons one, for a cell that may be perfectly valid.");

        // AND THE SAME ON THE BIND SURFACE, because its parity text is obtained by calling Convert and
        // is compared by characterization recordings just as Convert's own answer is.
        Assert.NotNull(parityText);
        Assert.NotEqual(string.Empty, parityText);
        Assert.Equal(emitted, parityText);

        // Nor is it whitespace that merely looks non-empty: the caller's guard is an exact `= ""`
        // comparison, so a single space would slip past it and then reach the expression parser as a
        // literally empty expression.
        Assert.False(string.IsNullOrWhiteSpace(emitted));

        // THE PRECISE FLOOR, per shape, rather than one convenient claim for all nine. The string
        // shape always carries its two wrapper quotes; the temporal shapes carry their call syntax;
        // the numeric shape can legitimately be a single character, and `Convert((short?)0)` is
        // exactly that.
        int floor = MinimumEmittedLength(typeKey, inputKind);

        Assert.True(
            emitted.Length >= floor,
            $"Convert({typeKey}) answered '{emitted}' for the '{inputKind}' input, which is shorter "
                + $"than the {floor.ToString(CultureInfo.InvariantCulture)}-character floor that shape "
                + "guarantees.");
    }

    /// <summary>
    /// The abort-sentinel claim this whole region rests on is READ from the oracle, not cited from
    /// memory.
    /// </summary>
    /// <remarks>
    /// The region banner makes a strong assertion about someone else's file - that an empty answer
    /// abandons a preprocessing pass rather than mis-answering it, and that the only legitimate
    /// producer of an empty answer is the unknown-column-type arm. Both halves are checked here
    /// against the read-only oracle so the reasoning cannot quietly become wrong while the tests keep
    /// passing.
    /// </remarks>
    /// <summary>
    /// One row per oracle line the abort-sentinel argument rests on: the line number, the exact text it
    /// must carry, and the role it plays in that argument.
    /// </summary>
    /// <remarks>
    /// Carrying the ROLE as a column is what makes a failing row self-explanatory: a mismatch on the
    /// consumer rows means the empty string is no longer an abort, and a mismatch on the producer rows
    /// means it is no longer confined to the unknown-column-type arm. Those are different findings with
    /// different consequences, and a single fact asserting all six lines could not tell them apart. The
    /// role is itself VERIFIED against the text of the line it labels rather than merely carried, so a
    /// row cannot claim a part of the argument that its own oracle line does not play.
    /// Tab characters are part of the expected text because the PowerBuilder export is tab indented.
    /// </remarks>
    public static TheoryData<int, string, string> AbortSentinelOracleLines =>
        new()
        {
            {
                2183,
                "\t\t\tif sVal = \"\" then return \"\"",
                "consumer - the macro path's twin guard"
            },
            {
                2186,
                "\t\t\tif sVal = \"\" then return \"\"",
                "consumer - abandons the whole preprocessing pass on an empty answer"
            },
            {
                2344,
                "choose case _of_GetColumnType(colName)",
                "dispatch head - the six named arms follow"
            },
            {
                2357,
                "\tcase else",
                "producer - the only arm that may legitimately answer empty"
            },
            {
                2358,
                "\t\treturn \"\"",
                "producer - the empty answer itself, for an unknown column type alone"
            },
        };

    [Theory]
    [MemberData(nameof(AbortSentinelOracleLines))]
    public void TheAbortSentinelArgumentRestsOnOracleLinesThatSayWhatItClaims(
        int oracleLine,
        string expectedText,
        string roleInTheArgument)
    {
        const string columnExpPath =
            "ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru";

        string oracleText = ReadRepositoryLine(columnExpPath, oracleLine);

        Assert.Equal(expectedText, oracleText);

        // THE ROLE COLUMN IS LOAD-BEARING, NOT DECORATIVE. Each of the three parts of the argument is
        // recognised by the exact oracle text it must carry, so a row cannot be mislabelled. Checking
        // only that the role names a KNOWN category would be far weaker: it would let the abort guard
        // at `:L2186` be filed as a producer - the exact opposite of what it does - while the test kept
        // passing, and the role would then document a reading of the oracle that nothing verifies.
        // The comparisons below carry the export's own tab indentation rather than trimming it, so the
        // file's no-normalisation rule holds on the oracle side too.
        switch (roleInTheArgument.Split(' ')[0])
        {
            case "consumer":
                // A consumer TESTS for the empty string and abandons the pass on it. Both consumer
                // rows are the same guard text, which is the point: `:L2183` and `:L2186` are twins.
                Assert.Equal("\t\t\tif sVal = \"\" then return \"\"", oracleText);
                break;

            case "dispatch":
                // The dispatch head opens the six typed arms. It neither tests for nor answers empty,
                // which is why it is neither a consumer nor a producer.
                Assert.Equal("choose case _of_GetColumnType(colName)", oracleText);
                break;

            case "producer":
                // A producer is the unknown-column-type arm itself or the empty answer inside it, and
                // nothing else. Confining the empty string to this one arm is the whole argument: any
                // OTHER line answering empty would make an abort indistinguishable from a literal.
                Assert.Contains(
                    oracleText,
                    new[] { "\tcase else", "\t\treturn \"\"" },
                    StringComparer.Ordinal);
                break;

            default:
                Assert.Fail(
                    $"Line {oracleLine.ToString(CultureInfo.InvariantCulture)} carries the role "
                        + $"'{roleInTheArgument}', which is not one of the consumer, producer or "
                        + "dispatch parts of the abort-sentinel argument. Add the reasoning for the "
                        + "new part before adding the row.");
                break;
        }
    }

    /// <summary>
    /// The reading of <c>_of_GetItemExpValue</c> that the abort guard protects really is at
    /// <c>:L2185</c>, immediately above its guard.
    /// </summary>
    /// <remarks>
    /// A <see cref="FactAttribute"/> and not a row of the theory above, because the claim is about the
    /// ADJACENCY of two lines rather than about the content of one: the read at <c>:L2185</c> and the
    /// guard at <c>:L2186</c> are a pair, and it is the pair that makes an empty answer an abort. A row
    /// asserting one line's text could not express "and the very next line abandons the pass".
    /// </remarks>
    [Fact]
    public void TheGuardedReadSitsImmediatelyAboveItsAbortGuard()
    {
        const string columnExpPath =
            "ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru";

        Assert.Contains(
            "_of_GetItemExpValue(ctx_row,vars[nVarIdx].name)",
            ReadRepositoryLine(columnExpPath, 2185),
            StringComparison.Ordinal);
        Assert.Equal(
            "\t\t\tif sVal = \"\" then return \"\"",
            ReadRepositoryLine(columnExpPath, 2186));
    }

    // ==============================================================================================
    //  REGION 5 - EXPRESSION-CALLABLE REGISTRATION, AND THE SIX TYPED ARMS
    //  Source: n_cst_dwsvc_columnexp.sru:L2390 (the expression-string call) and :L2344-:L2359
    //          (the typed dispatch)
    // ==============================================================================================
    //
    //  THE LEGACY FUNCTION IS REACHED IN TWO DIFFERENT WAYS, AND THE SECOND IS EASY TO MISS.
    //
    //  SIX call sites invoke it directly and in a strongly typed way, from the column-type dispatch at
    //  :L2346, :L2348, :L2350, :L2352, :L2354 and :L2356. Those are ordinary calls and a C# method
    //  reproduces them for free.
    //
    //  THE SEVENTH NAMES IT FROM INSIDE AN EXPRESSION STRING. :L2390 is
    //  `sVal = _of_Evaluate("dwValueToExp(" + sExp + ")",row)` - the engine wraps a preprocessed
    //  sub-expression in a CALL TO THIS FUNCTION SPELLED AS TEXT and hands the whole thing to
    //  `Describe("Evaluate(...)")`. PowerBuilder resolves the name because the object is an application
    //  GLOBAL function. NOTHING IN .NET DOES THAT AUTOMATICALLY. A C# method, however correct, is
    //  invisible to an expression evaluator unless it is registered in that evaluator's function table
    //  - so if the registration is missing, the variable-expression path at :L2388-:L2396 answers
    //  `"?"` and raises the error at :L2392, and no test of the nine overloads themselves would show
    //  it. That is what this region exists to prevent.
    //
    //  THE CASING IS OBSERVABLE, WHICH IS WHY IT IS ASSERTED. Two spellings appear in the oracle and
    //  the difference is not a typo: the prototypes at :L6-:L14 are lower case, because that is how a
    //  PowerBuilder export writes a declaration, while the EXPRESSION at :L2390 spells it
    //  `dwValueToExp`. PowerScript resolves either, being case insensitive. An expression function
    //  table is looked up by the name as written, so the expression spelling is the one that has to be
    //  published and registered.
    //
    //  AND THE LOOP CLOSES BACK ON THE SENTINELS. `dwValueToExp` of an absent cell answers the literal
    //  TEXT `dwNvlNumber()` and friends, which the preprocessor then SPLICES INTO ANOTHER EXPRESSION
    //  and asks the same evaluator to evaluate. So the five zero-argument producers must be registered
    //  as well, or REGION 2's sentinels become unevaluatable the moment they are used for the purpose
    //  they exist for.
    // ==============================================================================================

    /// <summary>
    /// The function is registered under the legacy EXPRESSION spelling, exactly, and the evaluator
    /// takes that name from the subject's published constant rather than from a retyped literal.
    /// </summary>
    /// <remarks>
    /// A SINGLETON, so a fact: there is ONE function name, published once and registered once, and the
    /// whole point of the claim is that it is one. The five typed-null producers registered beside it
    /// ARE a table, and they are <see cref="TheTypedNullProducerIsRegisteredSoItsSentinelCanBeEvaluated"/>.
    /// </remarks>
    [Fact]
    public void TheFunctionIsRegisteredUnderTheLegacyExpressionSpelling()
    {
        DataWindowExpressionEvaluator evaluator = new(BuildSixArmFixture());

        Assert.True(
            evaluator.IsFunctionRegistered(ValueToExpression.FunctionName),
            "The evaluator does not register the subject as an expression-callable function. "
                + "n_cst_dwsvc_columnexp.sru:L2390 names it from INSIDE an expression string, so "
                + "without the registration the variable-expression path answers \"?\" and raises the "
                + "error at :L2392 - and no test of the nine overloads alone would notice.");

        // THE EXACT SPELLING, ORDINAL. The registered roster carries the camel-cased expression form
        // and not the lower-cased declaration form, which a case-insensitive lookup would have hidden.
        Assert.Contains("dwValueToExp", evaluator.FunctionNames, StringComparer.Ordinal);
        Assert.Equal("dwValueToExp", ValueToExpression.FunctionName);
        Assert.DoesNotContain("dwvaluetoexp", evaluator.FunctionNames, StringComparer.Ordinal);

        // THE TWO ORACLE SPELLINGS, BOTH READ. The declaration at :L6 is lower case; the expression at
        // :L2390 is camel cased. Reading both is what turns "the casing matters" from an assertion
        // into a demonstration.
        Assert.Contains(
            "dwvaluetoexp",
            ReadRepositoryLine(OracleRepositoryPath, 6),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ValueToExpression.FunctionName,
            ReadRepositoryLine(OracleRepositoryPath, 6),
            StringComparison.Ordinal);
        Assert.Equal(
            "sVal = _of_Evaluate(\"dwValueToExp(\" + sExp + \")\",row)",
            ReadRepositoryLine(
                "ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru",
                2390));

        // AND THE REGISTRATION IS SINGLE SOURCED. A one-character drift between the name the evaluator
        // registers and the name the subject publishes would surface as a runtime "?"/"!" rather than
        // as a compile error, so the registration must reference the constant. Asserted against the
        // source text, because a retyped literal is value-identical until one of the two is edited.
        string evaluatorCode = CodeOnlyText(
            ReadRepositoryText(
                "services/dataservices-service/PowerFramework.DataServices/Expressions/"
                    + "DataWindowExpressionEvaluator.cs"));

        Assert.Contains(
            $"RegisterFunction({nameof(ValueToExpression)}.{nameof(ValueToExpression.FunctionName)},",
            evaluatorCode,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            $"RegisterFunction(\"{ValueToExpression.FunctionName}\"",
            evaluatorCode,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The five zero-argument typed-null producers are registered too, which is what makes a sentinel
    /// usable for the purpose it exists for.
    /// </summary>
    /// <remarks>
    /// The closed loop, stated once more because it is the reason this is not redundant with REGION 2:
    /// <c>dwValueToExp</c> of an absent cell answers the TEXT <c>dwNvlNumber()</c>, the preprocessor
    /// splices that text into another expression, and this same evaluator is then asked to evaluate it.
    /// A sentinel that no evaluator can evaluate is not a sentinel, it is a syntax error waiting for a
    /// null cell.
    /// </remarks>
    [Theory]
    [MemberData(nameof(NullProducerDeclarations))]
    public void TheTypedNullProducerIsRegisteredSoItsSentinelCanBeEvaluated(
        string typeKey,
        string producerFileName,
        string declaredLegacyReturnType,
        string emittedSentinel)
    {
        DataWindowExpressionEvaluator evaluator = new(BuildSixArmFixture());

        // The sentinel text is `<name>()`, so the registered name is the text without its parentheses -
        // DERIVED rather than restated, so this cannot drift from REGION 2's rows.
        Assert.EndsWith("()", emittedSentinel, StringComparison.Ordinal);

        string producerName = emittedSentinel[..^2];

        Assert.True(
            evaluator.IsFunctionRegistered(producerName),
            $"'{producerName}' is emitted as expression TEXT for an absent {typeKey} cell and is then "
                + "spliced into an expression this evaluator must evaluate, so it has to be in the "
                + "function table. Its producer is declared at "
                + $"ws_objects/pfw.datawindow.services.pbl.src/{producerFileName}:L6 returning "
                + $"{declaredLegacyReturnType}.");

        // The exact spelling is in the roster, ordinally - the emitted text is looked up by name as
        // written, so a case difference here is a lookup miss at run time.
        Assert.Contains(producerName, evaluator.FunctionNames, StringComparer.Ordinal);

        // AND EVALUATING IT YIELDS A NULL, not a sentinel string and not an abort - which is what closes
        // the loop rather than merely starting it.
        DataWindowExpressionResult result = evaluator.TryEvaluate(emittedSentinel, OneBasedFirstRow);

        Assert.True(result.IsSuccess);
        Assert.True(result.IsNullValue);

        // And the sentinel the subject emits for this type is the one just evaluated, so the two halves
        // of the loop are the same text rather than two texts that happen to agree.
        Assert.Equal(emittedSentinel, ConvertAbsent(typeKey));
    }

    /// <summary>
    /// Each of the six typed arms of the legacy dispatch reaches the correct <c>Convert</c> overload,
    /// driven by the column's type.
    /// </summary>
    [Theory]
    [MemberData(nameof(TypedDispatchArms))]
    public void EachTypedColumnArmReachesTheMatchingConvertOverload(
        string categoryName,
        long category,
        int caseLine,
        int callLine,
        string legacyAccessor,
        string columnName)
    {
        const string columnExpPath =
            "ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru";

        TypedArm arm = Assert.Single(
            SixTypedArms,
            candidate => string.Equals(candidate.CategoryName, categoryName, StringComparison.Ordinal));

        // THE ORACLE'S OWN TWO LINES FOR THIS ARM (C-K). The `case` line names the category constant
        // and the `return` line passes the accessor, so both halves of the row are checked against the
        // read-only oracle instead of trusted.
        Assert.Equal($"\tcase {categoryName}", ReadRepositoryLine(columnExpPath, caseLine));
        Assert.Equal(
            $"\t\treturn dwValueToExp(#DataWindow.{legacyAccessor}(row,colName))",
            ReadRepositoryLine(columnExpPath, callLine));

        // THE COLUMN TYPE RESOLVES TO THIS CATEGORY. The truncation to five characters is the legacy's
        // own, which is what makes `char(100)` and `decimal(2)` match at all, and the ORDER of the last
        // two arms is what keeps `datetime` - which truncates to `datet` - from being read as a date.
        Assert.Equal(category, DataWindowExpressionEvaluator.ConvertColumnType(arm.FakeColumnType));
        Assert.Equal(arm.Category, category);

        // AND THE EXPRESSION PATH LANDS ON THE MATCHING OVERLOAD. The expected text is produced by
        // calling the overload DIRECTLY with the same cell value, so this asserts agreement between
        // the two routes rather than restating a literal - which is what "reaches the correct
        // overload" actually means.
        DataWindowExpressionEvaluator evaluator = new(BuildSixArmFixture());

        string throughExpression = evaluator.Evaluate(
            $"{ValueToExpression.FunctionName}({columnName})",
            OneBasedFirstRow);
        string throughOverload = ConvertSixArmFixtureValue(arm.TypeKey);

        Assert.Equal(throughOverload, throughExpression);
        Assert.Equal(Encoding.UTF8.GetBytes(throughOverload), Encoding.UTF8.GetBytes(throughExpression));

        // Non-empty, so the abort sentinel of REGION 4 is not reached through this route either.
        Assert.NotEqual(string.Empty, throughExpression);

        // AND AN ABSENT CELL IN THE SAME COLUMN STILL YIELDS A SENTINEL RATHER THAN THE NAIVE TEXT.
        // Row 2 of the fixture is null in every column, so this exercises REGION 2's guard through the
        // expression path instead of through a direct call - the route a real caller takes.
        string absentThroughExpression = evaluator.Evaluate(
            $"{ValueToExpression.FunctionName}({columnName})",
            OneBasedAbsentRow);

        Assert.EndsWith("()", absentThroughExpression, StringComparison.Ordinal);
        Assert.NotEqual("''", absentThroughExpression);
        Assert.NotEqual("Date('')", absentThroughExpression);
        Assert.NotEqual("DateTime('')", absentThroughExpression);
        Assert.NotEqual("Time('')", absentThroughExpression);
        Assert.NotEqual(string.Empty, absentThroughExpression);

        // A NULL LOSES ITS COLUMN TYPE ON THIS ROUTE, AND THAT IS PINNED RATHER THAN GLOSSED. The
        // evaluator's value model has a SINGLE null arm, so a null cell arriving through an expression
        // has no type left to dispatch on and answers the NUMERIC producer whatever the column was -
        // which is also the oracle's own majority arm, since seven of the nine overloads emit it. A
        // caller that must preserve the column's type calls the matching overload directly with a typed
        // null, which is exactly what REGION 2 asserts and what the typed dispatch of this arm does
        // when the cell is present. Asserting the numeric producer here rather than the arm's own
        // sentinel records where the two routes genuinely differ.
        Assert.Equal(NumberValidator.NullLiteralExpression, absentThroughExpression);
        Assert.Equal(OwningSentinelConstant(arm.TypeKey), ConvertAbsent(arm.TypeKey));
    }

    /// <summary>
    /// The six arms, projected out of <see cref="SixTypedArms"/> as serializable row payloads.
    /// </summary>
    public static TheoryData<string, long, int, int, string, string> TypedDispatchArms
    {
        get
        {
            TheoryData<string, long, int, int, string, string> rows = new();

            foreach (TypedArm arm in SixTypedArms)
            {
                rows.Add(
                    arm.CategoryName,
                    arm.Category,
                    arm.CaseLine,
                    arm.CallLine,
                    arm.LegacyAccessor,
                    arm.ColumnName);
            }

            return rows;
        }
    }

    /// <summary>
    /// The six typed arms are SIX DISTINCT categories and they cover every category the ported
    /// catalogue declares except the unknown one.
    /// </summary>
    /// <remarks>
    /// The completeness half of the dispatch, which no per-arm row can express. It is what licenses
    /// REGION 4's claim that the abort arm is unreachable for a typed column: if a seventh named
    /// category existed with no arm of its own, a perfectly valid cell of that type would fall to
    /// <c>case else</c> and abandon the pass.
    /// </remarks>
    [Fact]
    public void TheSixArmsAreDistinctAndCoverEveryCategoryButTheUnknownOne()
    {
        Assert.Equal(6, SixTypedArms.Length);
        Assert.Equal(6, SixTypedArms.Select(arm => arm.Category).Distinct().Count());
        Assert.Equal(6, SixTypedArms.Select(arm => arm.TypeKey).Distinct(StringComparer.Ordinal).Count());

        // The unknown category has no arm, which is precisely why `case else` exists.
        Assert.DoesNotContain(DataWindowServiceBase.COL_TYPE_UNKNOWN, SixTypedArms.Select(arm => arm.Category));

        // An unrecognised column type resolves to it, so the abort arm is reachable only that way.
        Assert.Equal(
            DataWindowServiceBase.COL_TYPE_UNKNOWN,
            DataWindowExpressionEvaluator.ConvertColumnType("blob"));

        // THE ONE PLACE THE .NET DISPATCH LANDS DIFFERENTLY FROM THE LEGACY, RECORDED RATHER THAN
        // HIDDEN. The legacy INTEGER arm at :L2348 passes `GetItemNumber`, which PowerBuilder declares
        // to return a DOUBLE, so that arm reaches the double body; the .NET path types the cell and
        // reaches the long overload. The divergence is UNOBSERVABLE in the emitted text for any
        // integral value - proved in
        // TheFloatAndDoubleOverloadsRenderAtTheirOwnPrecisionAndAreNotInterchangeable - which is why
        // it is a documented note rather than a defect.
        TypedArm integerArm = Assert.Single(
            SixTypedArms,
            arm => arm.Category == DataWindowServiceBase.COL_TYPE_INTEGER);

        Assert.Equal("GetItemNumber", integerArm.LegacyAccessor);
        Assert.Equal(LongKey, integerArm.TypeKey);
        Assert.Equal(
            ValueToExpression.Convert((double?)SixArmAge),
            ValueToExpression.Convert((long?)SixArmAge));
    }

    // ==============================================================================================
    //  HELPERS - REFLECTION OVER THE SUBJECT
    // ==============================================================================================

    /// <summary>
    /// The public static methods <see cref="ValueToExpression"/> DECLARES under one name.
    /// </summary>
    /// <param name="name">The method name, compared ordinally.</param>
    /// <returns>The overloads, in no defined order.</returns>
    /// <remarks>
    /// <see cref="BindingFlags.DeclaredOnly"/> is passed deliberately even though it changes nothing
    /// on today's runtime - measured, a static class returns no inherited public statics either way.
    /// Stating the intent in the query rather than relying on that measurement keeps the count
    /// assertion in
    /// <see cref="TheSurfaceIsExactlyNineConvertOverloadsAndNineBindOverloadsAndNothingElse"/>
    /// meaningful, since that assertion is about what THIS type publishes.
    /// </remarks>
    private static MethodInfo[] DeclaredPublicStaticMethods(string name) =>
        typeof(ValueToExpression)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => string.Equals(method.Name, name, StringComparison.Ordinal))
            .ToArray();

    /// <summary>
    /// The single <c>Convert</c> overload over the keyed type.
    /// </summary>
    /// <param name="typeKey">One of the nine type keys.</param>
    /// <returns>The overload.</returns>
    private static MethodInfo ConvertOverload(string typeKey)
    {
        Type mapped = MappedParameterType(typeKey);

        return Assert.Single(
            DeclaredPublicStaticMethods("Convert"),
            method => ParameterValueType(method) == mapped);
    }

    /// <summary>
    /// The VALUE type of a method's single parameter, with the <see langword="in"/> by-reference
    /// wrapper removed.
    /// </summary>
    /// <param name="method">The overload.</param>
    /// <returns>
    /// <c>Nullable&lt;T&gt;</c> for the eight value-type overloads and <see cref="string"/> for the
    /// ninth.
    /// </returns>
    /// <remarks>
    /// The unwrapping is not cosmetic. An <see langword="in"/> parameter's
    /// <see cref="ParameterInfo.ParameterType"/> is the BY-REFERENCE type - <c>Nullable`1&amp;</c>,
    /// not <c>Nullable`1</c> - so comparing it directly against <c>typeof(decimal?)</c> would never
    /// match and every overload lookup in this file would silently find nothing.
    /// </remarks>
    private static Type ParameterValueType(MethodInfo method)
    {
        Type declared = method.GetParameters()[0].ParameterType;

        return declared.IsByRef ? declared.GetElementType()! : declared;
    }

    /// <summary>
    /// The parameter type the keyed overload declares, nullable wrapper included for a value type.
    /// </summary>
    /// <param name="typeKey">One of the nine type keys.</param>
    /// <returns>The mapped parameter type.</returns>
    private static Type MappedParameterType(string typeKey) => typeKey switch
    {
        DecimalKey => typeof(decimal?),
        ShortKey => typeof(short?),
        LongKey => typeof(long?),
        DoubleKey => typeof(double?),
        FloatKey => typeof(float?),

        // A reference type carries no runtime nullable wrapper; the `?` is an annotation, which
        // AbsenceIsRepresentableOnEveryOverload checks through NullabilityInfoContext instead.
        StringKey => typeof(string),
        DateTimeKey => typeof(DateTime?),
        DateOnlyKey => typeof(DateOnly?),
        TimeOnlyKey => typeof(TimeOnly?),
        _ => throw new ArgumentOutOfRangeException(nameof(typeKey), typeKey, "Unknown type key."),
    };

    /// <summary>
    /// The keyed type WITHOUT its nullable wrapper - the type argument a <c>BoundLiteral</c> closes
    /// over.
    /// </summary>
    /// <param name="typeKey">One of the nine type keys.</param>
    /// <returns>The underlying type.</returns>
    private static Type UnderlyingValueType(string typeKey) => typeKey switch
    {
        DecimalKey => typeof(decimal),
        ShortKey => typeof(short),
        LongKey => typeof(long),
        DoubleKey => typeof(double),
        FloatKey => typeof(float),
        StringKey => typeof(string),
        DateTimeKey => typeof(DateTime),
        DateOnlyKey => typeof(DateOnly),
        TimeOnlyKey => typeof(TimeOnly),
        _ => throw new ArgumentOutOfRangeException(nameof(typeKey), typeKey, "Unknown type key."),
    };

    // ==============================================================================================
    //  HELPERS - INVOKING THE SUBJECT WITH A TYPED ABSENT VALUE
    // ==============================================================================================
    //
    //  TYPED `default(T?)`, ONE ARM PER OVERLOAD, AND THE TYPING IS THE POINT. A single arm taking
    //  `object?` and casting would resolve to whichever overload the compiler picked for `object`,
    //  which is none of the nine - so the switch is what guarantees each row actually reaches the
    //  overload its key names. `default(decimal?)` is the absent value, not zero.
    // ==============================================================================================

    /// <summary>
    /// <c>Convert</c> called with a typed ABSENT value of the keyed type.
    /// </summary>
    /// <param name="typeKey">One of the nine type keys.</param>
    /// <returns>The emitted text.</returns>
    private static string ConvertAbsent(string typeKey) => typeKey switch
    {
        DecimalKey => ValueToExpression.Convert(default(decimal?)),
        ShortKey => ValueToExpression.Convert(default(short?)),
        LongKey => ValueToExpression.Convert(default(long?)),
        DoubleKey => ValueToExpression.Convert(default(double?)),
        FloatKey => ValueToExpression.Convert(default(float?)),
        StringKey => ValueToExpression.Convert(default(string?)),
        DateTimeKey => ValueToExpression.Convert(default(DateTime?)),
        DateOnlyKey => ValueToExpression.Convert(default(DateOnly?)),
        TimeOnlyKey => ValueToExpression.Convert(default(TimeOnly?)),
        _ => throw new ArgumentOutOfRangeException(nameof(typeKey), typeKey, "Unknown type key."),
    };

    /// <summary>
    /// <c>Bind</c> called with a typed ABSENT value of the keyed type.
    /// </summary>
    /// <param name="typeKey">One of the nine type keys.</param>
    /// <param name="hasValue">Receives the bound literal's <c>HasValue</c>.</param>
    /// <param name="kind">Receives the bound literal's <c>Kind</c>.</param>
    /// <returns>The bound literal's parity text.</returns>
    /// <remarks>
    /// The two <see langword="out"/> parameters exist so a caller can assert the whole node without
    /// this helper being generic over nine closed <c>BoundLiteral&lt;T&gt;</c> types, which would
    /// force either a common interface the subject does not declare or nine near-identical helpers.
    /// The string arm additionally asserts that absence binds <see cref="string.Empty"/> rather than
    /// <see langword="null"/>, because that is the one arm where the type argument is a reference type
    /// and the distinction between absent and empty could otherwise be lost inside the node itself.
    /// </remarks>
    private static string BindAbsent(
        string typeKey,
        out bool hasValue,
        out ValueToExpression.BoundLiteralKind kind)
    {
        switch (typeKey)
        {
            case DecimalKey:
            {
                ValueToExpression.BoundLiteral<decimal> bound = ValueToExpression.Bind(default(decimal?));
                hasValue = bound.HasValue;
                kind = bound.Kind;
                Assert.Equal(default, bound.Value);
                return bound.ParityText;
            }

            case ShortKey:
            {
                ValueToExpression.BoundLiteral<short> bound = ValueToExpression.Bind(default(short?));
                hasValue = bound.HasValue;
                kind = bound.Kind;
                Assert.Equal(default, bound.Value);
                return bound.ParityText;
            }

            case LongKey:
            {
                ValueToExpression.BoundLiteral<long> bound = ValueToExpression.Bind(default(long?));
                hasValue = bound.HasValue;
                kind = bound.Kind;
                Assert.Equal(default, bound.Value);
                return bound.ParityText;
            }

            case DoubleKey:
            {
                ValueToExpression.BoundLiteral<double> bound = ValueToExpression.Bind(default(double?));
                hasValue = bound.HasValue;
                kind = bound.Kind;
                Assert.Equal(default, bound.Value);
                return bound.ParityText;
            }

            case FloatKey:
            {
                ValueToExpression.BoundLiteral<float> bound = ValueToExpression.Bind(default(float?));
                hasValue = bound.HasValue;
                kind = bound.Kind;
                Assert.Equal(default, bound.Value);
                return bound.ParityText;
            }

            case StringKey:
            {
                ValueToExpression.BoundLiteral<string> bound = ValueToExpression.Bind(default(string?));
                hasValue = bound.HasValue;
                kind = bound.Kind;

                // Absence binds the EMPTY string with HasValue false, which is how an absent cell
                // stays distinguishable from an empty one inside the node - the same distinction
                // Convert preserves by answering the sentinel rather than `''`.
                Assert.Equal(string.Empty, bound.Value);
                return bound.ParityText;
            }

            case DateTimeKey:
            {
                ValueToExpression.BoundLiteral<DateTime> bound = ValueToExpression.Bind(default(DateTime?));
                hasValue = bound.HasValue;
                kind = bound.Kind;
                Assert.Equal(default, bound.Value);
                return bound.ParityText;
            }

            case DateOnlyKey:
            {
                ValueToExpression.BoundLiteral<DateOnly> bound = ValueToExpression.Bind(default(DateOnly?));
                hasValue = bound.HasValue;
                kind = bound.Kind;
                Assert.Equal(default, bound.Value);
                return bound.ParityText;
            }

            case TimeOnlyKey:
            {
                ValueToExpression.BoundLiteral<TimeOnly> bound = ValueToExpression.Bind(default(TimeOnly?));
                hasValue = bound.HasValue;
                kind = bound.Kind;
                Assert.Equal(default, bound.Value);
                return bound.ParityText;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(typeKey), typeKey, "Unknown type key.");
        }
    }

    // ==============================================================================================
    //  HELPERS - INVOKING THE SUBJECT WITH A TYPED PRESENT VALUE
    // ==============================================================================================

    /// <summary>
    /// <c>Convert</c> called with a numeric value NARROWED to the keyed overload's own type.
    /// </summary>
    /// <param name="typeKey">One of the five numeric type keys.</param>
    /// <param name="value">
    /// The value, carried as a <see cref="decimal"/> because it is the one primitive that represents
    /// every row of the numeric table exactly, two-place scales included.
    /// </param>
    /// <returns>The emitted text.</returns>
    /// <remarks>
    /// The narrowing casts are CHECKED by the table rather than by an exception: every numeric row
    /// carries a value inside the keyed type's range, so no cast here can overflow, and the two
    /// floating-point extremes that could not survive a decimal at all are asserted in
    /// <see cref="TheFloatAndDoubleOverloadsRenderAtTheirOwnPrecisionAndAreNotInterchangeable"/>
    /// against directly typed values instead of through this helper.
    /// </remarks>
    private static string ConvertNumeric(string typeKey, decimal value) => typeKey switch
    {
        DecimalKey => ValueToExpression.Convert((decimal?)value),
        ShortKey => ValueToExpression.Convert((short?)(short)value),
        LongKey => ValueToExpression.Convert((long?)(long)value),
        DoubleKey => ValueToExpression.Convert((double?)(double)value),
        FloatKey => ValueToExpression.Convert((float?)(float)value),
        _ => throw new ArgumentOutOfRangeException(
            nameof(typeKey),
            typeKey,
            "Not a numeric type key."),
    };

    /// <summary>
    /// <c>Convert</c> called with the NAMED non-finite value of the keyed floating-point type.
    /// </summary>
    /// <param name="typeKey">Either the double or the float type key.</param>
    /// <param name="nonFiniteName">
    /// <c>NaN</c>, <c>PositiveInfinity</c>, <c>NegativeInfinity</c> or <c>NegativeZero</c>.
    /// </param>
    /// <returns>The emitted text.</returns>
    /// <remarks>
    /// Selected by NAME rather than carried as a number, because none of the four survives the
    /// <see cref="decimal"/> payload the ordinary numeric table uses - a decimal has no infinity, no NaN
    /// and no signed zero - and because a name is what a reader of a failing row needs to see.
    /// </remarks>
    private static string ConvertNonFinite(string typeKey, string nonFiniteName)
    {
        if (string.Equals(typeKey, DoubleKey, StringComparison.Ordinal))
        {
            double value = nonFiniteName switch
            {
                "NaN" => double.NaN,
                "PositiveInfinity" => double.PositiveInfinity,
                "NegativeInfinity" => double.NegativeInfinity,
                "NegativeZero" => -0.0d,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(nonFiniteName),
                    nonFiniteName,
                    "Unknown non-finite name."),
            };

            return ValueToExpression.Convert(value);
        }

        if (string.Equals(typeKey, FloatKey, StringComparison.Ordinal))
        {
            float value = nonFiniteName switch
            {
                "NaN" => float.NaN,
                "PositiveInfinity" => float.PositiveInfinity,
                "NegativeInfinity" => float.NegativeInfinity,
                "NegativeZero" => -0.0f,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(nonFiniteName),
                    nonFiniteName,
                    "Unknown non-finite name."),
            };

            return ValueToExpression.Convert(value);
        }

        throw new ArgumentOutOfRangeException(
            nameof(typeKey),
            typeKey,
            "Only the double and float overloads have non-finite values.");
    }

    /// <summary>
    /// <c>Convert</c> called with a temporal value composed from the row's integer components, also
    /// returning the OWNING validator's own rendering of that same value so the two can be compared.
    /// </summary>
    /// <param name="typeKey">One of the three temporal type keys.</param>
    /// <param name="firstComponent">Year for a date or datetime, hour for a time.</param>
    /// <param name="secondComponent">Month for a date or datetime, minute for a time.</param>
    /// <param name="thirdComponent">Day for a date or datetime, second for a time.</param>
    /// <param name="innerText">
    /// Receives the producing validator's rendering of the value - what the emitted wrapper should be
    /// wrapped AROUND.
    /// </param>
    /// <returns>The emitted text.</returns>
    /// <remarks>
    /// The datetime arm fixes its time-of-day at 07:08:09 so the row table needs only three
    /// components for all three types while still exercising a zero-padded hour, a two-digit minute
    /// and a two-digit second. Its <see cref="DateTimeKind"/> is
    /// <see cref="DateTimeKind.Unspecified"/>, matching a zone-less PowerBuilder <c>datetime</c>: the
    /// subject neither reads nor normalises the kind, so choosing Local or Utc here would imply a
    /// contract that does not exist.
    /// </remarks>
    private static string ConvertTemporal(
        string typeKey,
        int firstComponent,
        int secondComponent,
        int thirdComponent,
        out string innerText)
    {
        switch (typeKey)
        {
            case DateTimeKey:
            {
                DateTime value = new(
                    firstComponent,
                    secondComponent,
                    thirdComponent,
                    7,
                    8,
                    9,
                    DateTimeKind.Unspecified);

                innerText = DateTimeValidator.FormatExpressionValue(value);
                return ValueToExpression.Convert(value);
            }

            case DateOnlyKey:
            {
                DateOnly value = new(firstComponent, secondComponent, thirdComponent);

                innerText = DateValidator.FormatValue(value);
                return ValueToExpression.Convert(value);
            }

            case TimeOnlyKey:
            {
                TimeOnly value = new(firstComponent, secondComponent, thirdComponent);

                innerText = TimeValidator.Format(value);
                return ValueToExpression.Convert(value);
            }

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(typeKey),
                    typeKey,
                    "Not a temporal type key.");
        }
    }

    // ==============================================================================================
    //  HELPERS - THE EXHAUSTIVE INVARIANT SWEEP
    // ==============================================================================================

    /// <summary>
    /// Calls BOTH surfaces with the keyed type's value for the named input kind.
    /// </summary>
    /// <param name="typeKey">One of the nine type keys.</param>
    /// <param name="inputKind">One of the five input kinds.</param>
    /// <param name="parityText">Receives the matching <c>Bind</c> overload's parity text.</param>
    /// <returns>The <c>Convert</c> overload's answer.</returns>
    /// <remarks>
    /// <para>
    /// The absent kind delegates to the two absent helpers so there is exactly one place in this file
    /// that constructs a typed absent value. Every other kind is built here, typed to the row's own
    /// overload.
    /// </para>
    /// <para>
    /// THE STRING OVERLOAD'S THREE NON-ABSENT KINDS ARE MAPPED RATHER THAN INVENTED, and the mapping is
    /// stated because "minimum" and "maximum" have no natural meaning for a string. The default and the
    /// empty kinds are both <see cref="string.Empty"/> - the shortest legal non-absent value, and the
    /// one that must still answer <c>''</c> rather than the sentinel. The minimum kind is a lone single
    /// quote, the shortest value that reaches the preserved defect. The maximum kind is a value longer
    /// than the fixture's widest <c>char(200)</c> column, so a hypothetical length check would show up
    /// here rather than in production.
    /// </para>
    /// </remarks>
    private static string ConvertInput(string typeKey, string inputKind, out string parityText)
    {
        if (string.Equals(inputKind, AbsentInputKind, StringComparison.Ordinal))
        {
            parityText = BindAbsent(typeKey, out _, out _);
            return ConvertAbsent(typeKey);
        }

        switch (typeKey)
        {
            case DecimalKey:
            {
                decimal value = inputKind switch
                {
                    MinimumInputKind => decimal.MinValue,
                    MaximumInputKind => decimal.MaxValue,
                    _ => default,
                };

                parityText = ValueToExpression.Bind(value).ParityText;
                return ValueToExpression.Convert(value);
            }

            case ShortKey:
            {
                short value = inputKind switch
                {
                    MinimumInputKind => short.MinValue,
                    MaximumInputKind => short.MaxValue,
                    _ => default,
                };

                parityText = ValueToExpression.Bind(value).ParityText;
                return ValueToExpression.Convert(value);
            }

            case LongKey:
            {
                long value = inputKind switch
                {
                    MinimumInputKind => long.MinValue,
                    MaximumInputKind => long.MaxValue,
                    _ => default,
                };

                parityText = ValueToExpression.Bind(value).ParityText;
                return ValueToExpression.Convert(value);
            }

            case DoubleKey:
            {
                double value = inputKind switch
                {
                    MinimumInputKind => double.MinValue,
                    MaximumInputKind => double.MaxValue,
                    _ => default,
                };

                parityText = ValueToExpression.Bind(value).ParityText;
                return ValueToExpression.Convert(value);
            }

            case FloatKey:
            {
                float value = inputKind switch
                {
                    MinimumInputKind => float.MinValue,
                    MaximumInputKind => float.MaxValue,
                    _ => default,
                };

                parityText = ValueToExpression.Bind(value).ParityText;
                return ValueToExpression.Convert(value);
            }

            case StringKey:
            {
                string value = inputKind switch
                {
                    // The shortest value that reaches the preserved unescaped-quote defect.
                    MinimumInputKind => "'",

                    // Longer than the fixture's widest char(200) column [dw_sqlite.srd:L11].
                    MaximumInputKind => new string('W', 260),

                    // Both the default and the empty kind: the shortest legal non-absent value, which
                    // must still answer `''` rather than the sentinel.
                    _ => string.Empty,
                };

                parityText = ValueToExpression.Bind(value).ParityText;
                return ValueToExpression.Convert(value);
            }

            case DateTimeKey:
            {
                DateTime value = inputKind switch
                {
                    MinimumInputKind => DateTime.MinValue,
                    MaximumInputKind => DateTime.MaxValue,
                    _ => default,
                };

                parityText = ValueToExpression.Bind(value).ParityText;
                return ValueToExpression.Convert(value);
            }

            case DateOnlyKey:
            {
                DateOnly value = inputKind switch
                {
                    MinimumInputKind => DateOnly.MinValue,
                    MaximumInputKind => DateOnly.MaxValue,
                    _ => default,
                };

                parityText = ValueToExpression.Bind(value).ParityText;
                return ValueToExpression.Convert(value);
            }

            case TimeOnlyKey:
            {
                TimeOnly value = inputKind switch
                {
                    MinimumInputKind => TimeOnly.MinValue,
                    MaximumInputKind => TimeOnly.MaxValue,
                    _ => default,
                };

                parityText = ValueToExpression.Bind(value).ParityText;
                return ValueToExpression.Convert(value);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(typeKey), typeKey, "Unknown type key.");
        }
    }

    /// <summary>
    /// The shortest text the keyed shape can legitimately produce for the named input kind.
    /// </summary>
    /// <param name="typeKey">One of the nine type keys.</param>
    /// <param name="inputKind">One of the five input kinds.</param>
    /// <returns>The floor, in characters.</returns>
    /// <remarks>
    /// PER SHAPE, AND NOT ONE CONVENIENT NUMBER FOR ALL NINE. A single floor of two would be wrong for
    /// the bare numeric shape - <c>Convert((short?)0)</c> is <c>"0"</c>, one character, and correctly
    /// so - and asserting it anyway would either fail a correct implementation or, if softened to one,
    /// stop detecting a string overload that lost its wrapper quotes. The invariant that holds
    /// universally is non-emptiness, which the theory asserts separately for every cell.
    /// </remarks>
    private static int MinimumEmittedLength(string typeKey, string inputKind)
    {
        if (string.Equals(inputKind, AbsentInputKind, StringComparison.Ordinal))
        {
            // The sentinel is a fixed spelling, so its own length is the exact floor and the ceiling.
            return OwningSentinelConstant(typeKey).Length;
        }

        return typeKey switch
        {
            // Two wrapper quotes, which an empty value reduces to exactly.
            StringKey => 2,

            // The call syntax plus the fixed-width, zero-padded rendering: `Xxx('` + format + `')`.
            DateTimeKey or DateOnlyKey or TimeOnlyKey =>
                ExpectedWrapperPrefix(typeKey).Length + PublishedTemporalFormat(typeKey).Length + 2,

            // One digit. `0` is a complete, correct numeric literal.
            _ => 1,
        };
    }

    // ==============================================================================================
    //  HELPERS - THE SIX TYPED ARMS OF THE LEGACY COLUMN-TYPE DISPATCH
    //  Source: n_cst_dwsvc_columnexp.sru:L2344-:L2359
    // ==============================================================================================

    /// <summary>
    /// One arm of the legacy <c>choose case _of_GetColumnType(colName)</c> dispatch, with everything
    /// needed to reproduce it: the oracle lines, the legacy accessor, the column-type category, a
    /// column shape that lands on that category, and the overload the .NET path reaches.
    /// </summary>
    /// <param name="CategoryName">The preserved constant's spelling, REFERENCED and never declared here.</param>
    /// <param name="Category">The category value, read from the ported constant catalogue.</param>
    /// <param name="CaseLine">The oracle's <c>case</c> line.</param>
    /// <param name="CallLine">The oracle's <c>return dwValueToExp(...)</c> line.</param>
    /// <param name="LegacyAccessor">The DataWindow accessor that line passes.</param>
    /// <param name="ColumnName">The column name used in this suite's fixture.</param>
    /// <param name="FakeColumnType">The column type string that resolves to <paramref name="Category"/>.</param>
    /// <param name="TypeKey">The <c>Convert</c> overload the .NET dispatch lands on.</param>
    private readonly record struct TypedArm(
        string CategoryName,
        long Category,
        int CaseLine,
        int CallLine,
        string LegacyAccessor,
        string ColumnName,
        string FakeColumnType,
        string TypeKey);

    /// <summary>
    /// The six arms in the oracle's declaration order.
    /// </summary>
    /// <remarks>
    /// SIX AND NOT SEVEN. The seventh category, the unknown one, has no arm of its own - it falls to
    /// the <c>case else</c> at <c>:L2357-:L2358</c>, which is the abort producer REGION 4 is about.
    /// The column-type strings are the fixture's own shapes, and the truncation to five characters
    /// that makes <c>char(100)</c> and <c>decimal(2)</c> resolve at all is the legacy's, not this
    /// file's.
    /// </remarks>
    private static readonly TypedArm[] SixTypedArms =
    [
        new(
            nameof(DataWindowServiceBase.COL_TYPE_DECIMAL),
            DataWindowServiceBase.COL_TYPE_DECIMAL,
            2345,
            2346,
            "GetItemDecimal",
            "salary",
            "decimal(2)",
            DecimalKey),
        new(
            nameof(DataWindowServiceBase.COL_TYPE_INTEGER),
            DataWindowServiceBase.COL_TYPE_INTEGER,
            2347,
            2348,
            "GetItemNumber",
            "age",
            "number",
            LongKey),
        new(
            nameof(DataWindowServiceBase.COL_TYPE_STRING),
            DataWindowServiceBase.COL_TYPE_STRING,
            2349,
            2350,
            "GetItemString",
            "name",
            "char(100)",
            StringKey),
        new(
            nameof(DataWindowServiceBase.COL_TYPE_DATETIME),
            DataWindowServiceBase.COL_TYPE_DATETIME,
            2351,
            2352,
            "GetItemDateTime",
            "moment",
            "datetime",
            DateTimeKey),
        new(
            nameof(DataWindowServiceBase.COL_TYPE_DATE),
            DataWindowServiceBase.COL_TYPE_DATE,
            2353,
            2354,
            "GetItemDate",
            "birth",
            "date",
            DateOnlyKey),
        new(
            nameof(DataWindowServiceBase.COL_TYPE_TIME),
            DataWindowServiceBase.COL_TYPE_TIME,
            2355,
            2356,
            "GetItemTime",
            "clock",
            "time",
            TimeOnlyKey),
    ];

    // ==============================================================================================
    //  HELPERS - THE SIX-ARM EXPRESSION FIXTURE
    // ==============================================================================================
    //
    //  A DataWindow shape carrying ONE column per typed arm, so the dispatch can be driven by column
    //  type rather than asserted about. Four of the six columns are the primary fixture's own -
    //  `salary` decimal(2), `age` number, `name` char(100) and `birth` date [dw_sqlite.srd:L8-:L14] -
    //  and two are added because that fixture has no datetime and no time column, and the oracle's
    //  arms at :L2352 and :L2356 would otherwise be unreachable.
    //
    //  ROW 1 CARRIES A VALUE IN EVERY COLUMN AND ROW 2 CARRIES NONE, so the same expression exercises
    //  both the value path and the sentinel path without a second fixture.
    // ==============================================================================================

    /// <summary>The one-based row of the fixture that carries a value in every column.</summary>
    private const long OneBasedFirstRow = 1L;

    /// <summary>The one-based row of the fixture whose every column is absent.</summary>
    private const long OneBasedAbsentRow = 2L;

    /// <summary>Row 1's <c>salary</c>, matching the primary fixture's own first-row value.</summary>
    private const decimal SixArmSalary = 1000.50m;

    /// <summary>Row 1's <c>age</c>, matching the primary fixture's own first-row value.</summary>
    private const long SixArmAge = 30L;

    /// <summary>Row 1's <c>name</c>, matching the primary fixture's own first-row value.</summary>
    private const string SixArmName = "Alice";

    /// <summary>Row 1's <c>moment</c>. No datetime column exists in the primary fixture.</summary>
    private static readonly DateTime SixArmMoment = new(2024, 3, 5, 7, 8, 9, DateTimeKind.Unspecified);

    /// <summary>Row 1's <c>birth</c>, matching the primary fixture's own first-row value.</summary>
    private static readonly DateOnly SixArmBirth = new(1990, 1, 2);

    /// <summary>Row 1's <c>clock</c>. No time column exists in the primary fixture.</summary>
    private static readonly TimeOnly SixArmClock = new(3, 4, 5);

    /// <summary>
    /// A DataWindow host with one column per typed arm, a fully populated row 1 and a fully absent
    /// row 2.
    /// </summary>
    /// <returns>The host.</returns>
    /// <remarks>
    /// The column ORDER here and the value order in the two <c>AddRow</c> calls must agree, because
    /// values are taken positionally in declared column order. The order chosen is the oracle's own arm
    /// order at <c>:L2345-:L2356</c>, so a reader comparing the two can do it line by line.
    /// </remarks>
    private static FakeDataWindowHost BuildSixArmFixture()
    {
        FakeDataWindowHost host = new();

        foreach (TypedArm arm in SixTypedArms)
        {
            host.AddColumn(arm.ColumnName, arm.FakeColumnType);
        }

        // Row 1, in the same order the columns were declared: decimal, integer, string, datetime,
        // date, time.
        host.AddRow(SixArmSalary, SixArmAge, SixArmName, SixArmMoment, SixArmBirth, SixArmClock);

        // Row 2, absent in every column.
        host.AddRow(null, null, null, null, null, null);

        host.CurrentRow = OneBasedFirstRow;
        return host;
    }

    /// <summary>
    /// <c>Convert</c> called DIRECTLY with row 1's value for the keyed type - the expected answer the
    /// expression route has to agree with.
    /// </summary>
    /// <param name="typeKey">The type key of one of the six arms.</param>
    /// <returns>The emitted text.</returns>
    /// <remarks>
    /// Producing the expectation by calling the overload rather than by restating a literal is what
    /// makes the dispatch assertion an AGREEMENT between two routes. A literal would still pass if both
    /// routes drifted together, and would have to be re-edited every time a characterization item on a
    /// temporal format was settled.
    /// </remarks>
    private static string ConvertSixArmFixtureValue(string typeKey) => typeKey switch
    {
        DecimalKey => ValueToExpression.Convert((decimal?)SixArmSalary),
        LongKey => ValueToExpression.Convert((long?)SixArmAge),
        StringKey => ValueToExpression.Convert(SixArmName),
        DateTimeKey => ValueToExpression.Convert((DateTime?)SixArmMoment),
        DateOnlyKey => ValueToExpression.Convert((DateOnly?)SixArmBirth),
        TimeOnlyKey => ValueToExpression.Convert((TimeOnly?)SixArmClock),
        _ => throw new ArgumentOutOfRangeException(
            nameof(typeKey),
            typeKey,
            "Not one of the six typed dispatch arms."),
    };

    // ==============================================================================================
    //  HELPERS - RUNNING UNDER A HOSTILE AMBIENT CULTURE
    // ==============================================================================================

    /// <summary>
    /// Runs an action on a DEDICATED thread whose ambient culture is <paramref name="culture"/>,
    /// rethrowing whatever it threw with its original stack trace intact.
    /// </summary>
    /// <param name="culture">The culture to impose.</param>
    /// <param name="action">The assertions to run under it.</param>
    /// <remarks>
    /// <para>
    /// A DEDICATED THREAD RATHER THAN A SAVE-AND-RESTORE ON THIS ONE, and the reason is test isolation
    /// rather than style. <see cref="CultureInfo.CurrentCulture"/> is per-thread, and a test body runs
    /// on a pooled thread; mutating that thread's culture and restoring it in a <c>finally</c> works
    /// only if nothing throws in a way that skips the restore, and it leaves a window in which the
    /// pooled thread carries a foreign culture. A thread created here is owned here, is joined before
    /// the method returns, and is then discarded - so no other test can observe the culture at all,
    /// however this one ends.
    /// </para>
    /// <para>
    /// Both culture properties are set. The formatting under test reads
    /// <see cref="CultureInfo.CurrentCulture"/>, but an assertion failure message renders through
    /// <see cref="CultureInfo.CurrentUICulture"/>, and leaving the two disagreeing would be a
    /// difference between this thread and a real one for no benefit.
    /// </para>
    /// <para>
    /// The captured failure is rethrown through <see cref="ExceptionDispatchInfo"/> rather than
    /// wrapped, so a failing assertion inside the action reports as that assertion - with its own
    /// message and its own stack - instead of as a thread-marshalling error several frames away from
    /// the claim that actually failed.
    /// </para>
    /// </remarks>
    private static void RunUnderCulture(CultureInfo culture, Action action)
    {
        ExceptionDispatchInfo? failure = null;

        Thread worker = new(() =>
        {
            try
            {
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                action();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        })
        {
            IsBackground = true,
            Name = nameof(RunUnderCulture),
        };

        worker.Start();
        worker.Join();

        failure?.Throw();
    }

    // ==============================================================================================
    //  HELPERS - THE PRODUCING VALIDATORS' PUBLISHED CONSTANTS
    // ==============================================================================================

    /// <summary>
    /// The five validator TYPE NAMES that own a sentinel spelling, taken with <c>nameof</c> so a
    /// rename is a compile error rather than a stale string.
    /// </summary>
    /// <remarks>
    /// Five names for nine overloads, which is the asymmetry
    /// <see cref="TheOneIntegerDeclaredNumericSentinelIsSharedByAllFiveNumericOverloadsAndOnlyThose"/>
    /// characterises: the five numeric overloads share the one <c>integer</c>-declared producer.
    /// </remarks>
    private static readonly string[] SentinelOwningValidatorNames =
    [
        nameof(NumberValidator),
        nameof(StringValidator),
        nameof(DateTimeValidator),
        nameof(DateValidator),
        nameof(TimeValidator),
    ];

    /// <summary>
    /// The null-sentinel constant PUBLISHED by the validator that owns the keyed type's spelling.
    /// </summary>
    /// <param name="typeKey">One of the nine type keys.</param>
    /// <returns>The owning validator's <c>NullLiteralExpression</c>.</returns>
    /// <remarks>
    /// Referencing the constants rather than retyping their text is the whole point of this helper:
    /// it is what keeps this suite from asserting one copy of a spelling against another copy of the
    /// same spelling. Note this can only ever prove VALUE agreement - a <c>const string</c> is
    /// inlined at compile time and equal literals are interned, so no identity check could tell a
    /// referenced constant from a duplicated literal. That claim is asserted against the SOURCE TEXT
    /// by <see cref="TheSentinelsAreSingleSourcedFromTheOwningValidators"/> instead.
    /// </remarks>
    private static string OwningSentinelConstant(string typeKey) => typeKey switch
    {
        DecimalKey or ShortKey or LongKey or DoubleKey or FloatKey =>
            NumberValidator.NullLiteralExpression,
        StringKey => StringValidator.NullLiteralExpression,
        DateTimeKey => DateTimeValidator.NullLiteralExpression,
        DateOnlyKey => DateValidator.NullLiteralExpression,
        TimeOnlyKey => TimeValidator.NullLiteralExpression,
        _ => throw new ArgumentOutOfRangeException(nameof(typeKey), typeKey, "Unknown type key."),
    };

    /// <summary>
    /// The TYPE NAME of the validator that owns the keyed type's sentinel spelling, taken with
    /// <c>nameof</c> so a rename is a compile error rather than a stale string.
    /// </summary>
    /// <param name="typeKey">One of the nine type keys.</param>
    /// <returns>The owning validator's type name.</returns>
    private static string OwningValidatorTypeName(string typeKey) => typeKey switch
    {
        DecimalKey or ShortKey or LongKey or DoubleKey or FloatKey => nameof(NumberValidator),
        StringKey => nameof(StringValidator),
        DateTimeKey => nameof(DateTimeValidator),
        DateOnlyKey => nameof(DateValidator),
        TimeOnlyKey => nameof(TimeValidator),
        _ => throw new ArgumentOutOfRangeException(nameof(typeKey), typeKey, "Unknown type key."),
    };

    /// <summary>
    /// The PowerScript type token the oracle's prototype declares for the keyed mapped type.
    /// </summary>
    /// <param name="typeKey">One of the four non-numeric type keys.</param>
    /// <returns>The legacy type token, in PowerScript's own lower-case spelling.</returns>
    /// <remarks>
    /// Only the four non-numeric keys are answerable, and that is the finding rather than a limitation:
    /// the numeric producer is NOT type matched, so asking which legacy type its sentinel corresponds to
    /// has no single answer - it serves <c>decimal</c>, <c>int</c>, <c>long</c>, <c>double</c> and
    /// <c>real</c> from one <c>integer</c> declaration.
    /// </remarks>
    private static string MappedLegacyTypeName(string typeKey) => typeKey switch
    {
        StringKey => "string",
        DateTimeKey => "datetime",
        DateOnlyKey => "date",
        TimeOnlyKey => "time",
        _ => throw new ArgumentOutOfRangeException(
            nameof(typeKey),
            typeKey,
            "The numeric producer is deliberately not type matched, so it has no single legacy type."),
    };

    /// <summary>
    /// The literal kind a bound value of the keyed type reports.
    /// </summary>
    /// <param name="typeKey">One of the nine type keys.</param>
    /// <returns>The expected kind.</returns>
    /// <remarks>
    /// Five kinds for nine types, grouped exactly as the oracle groups them: the five numeric bodies
    /// all render a bare number and share one sentinel, while the string body and the three temporal
    /// bodies each render their own shape.
    /// </remarks>
    private static ValueToExpression.BoundLiteralKind ExpectedLiteralKind(string typeKey) => typeKey switch
    {
        DecimalKey or ShortKey or LongKey or DoubleKey or FloatKey =>
            ValueToExpression.BoundLiteralKind.NumberLiteral,
        StringKey => ValueToExpression.BoundLiteralKind.StringLiteral,
        DateTimeKey => ValueToExpression.BoundLiteralKind.DateTimeLiteral,
        DateOnlyKey => ValueToExpression.BoundLiteralKind.DateLiteral,
        TimeOnlyKey => ValueToExpression.BoundLiteralKind.TimeLiteral,
        _ => throw new ArgumentOutOfRangeException(nameof(typeKey), typeKey, "Unknown type key."),
    };

    /// <summary>
    /// The invariant emission FORMAT constant published by the validator that owns the keyed temporal
    /// type.
    /// </summary>
    /// <param name="typeKey">One of the three temporal type keys.</param>
    /// <returns>The owning validator's canonical format.</returns>
    private static string PublishedTemporalFormat(string typeKey) => typeKey switch
    {
        DateTimeKey => DateTimeValidator.ExpressionValueFormat,
        DateOnlyKey => DateValidator.CanonicalValueFormat,
        TimeOnlyKey => TimeValidator.CanonicalFormat,
        _ => throw new ArgumentOutOfRangeException(
            nameof(typeKey),
            typeKey,
            "Not a temporal type key."),
    };

    /// <summary>
    /// The opening token of the keyed temporal type's DataWindow conversion call, closing quote
    /// included, read straight out of <c>dwvaluetoexp.srf:L54</c>, <c>:L60</c> and <c>:L66</c>.
    /// </summary>
    /// <param name="typeKey">One of the three temporal type keys.</param>
    /// <returns>The wrapper prefix.</returns>
    /// <remarks>
    /// Spelled here as the ORACLE spells it - capital letters included - rather than read back from
    /// the subject's own private constants, which are not visible and should not be. The assertion has
    /// to come from the oracle side to mean anything.
    /// </remarks>
    private static string ExpectedWrapperPrefix(string typeKey) => typeKey switch
    {
        DateTimeKey => "DateTime('",
        DateOnlyKey => "Date('",
        TimeOnlyKey => "Time('",
        _ => throw new ArgumentOutOfRangeException(
            nameof(typeKey),
            typeKey,
            "Not a temporal type key."),
    };

    // ==============================================================================================
    //  HELPERS - THE ORACLE'S OWN BODY LINE PER OVERLOAD
    // ==============================================================================================

    /// <summary>
    /// The 1-BASED <c>dwvaluetoexp.srf</c> line carrying the keyed overload's assignment statement -
    /// the <c>sVal = ...</c> line, not the <c>IsNull</c> guard on the line after it.
    /// </summary>
    /// <param name="typeKey">One of the nine type keys.</param>
    /// <returns>The oracle line number.</returns>
    /// <remarks>
    /// Declared once so that the generated invariant matrix can carry a locator on every one of its
    /// forty-five cells without restating the mapping, and so that the authored tables and the
    /// generated one cannot disagree about which line an overload lives on. The nine values are the
    /// oracle's own: <c>:L18</c>, <c>:L24</c>, <c>:L30</c>, <c>:L36</c>, <c>:L42</c>, <c>:L48</c>,
    /// <c>:L54</c>, <c>:L60</c>, <c>:L66</c> - each exactly one line above its guard.
    /// </remarks>
    private static int OracleAssignmentLine(string typeKey) => typeKey switch
    {
        DecimalKey => 18,
        ShortKey => 24,
        LongKey => 30,
        DoubleKey => 36,
        FloatKey => 42,
        StringKey => 48,
        DateTimeKey => 54,
        DateOnlyKey => 60,
        TimeOnlyKey => 66,
        _ => throw new ArgumentOutOfRangeException(nameof(typeKey), typeKey, "Unknown type key."),
    };

    /// <summary>
    /// The exact text of the keyed overload's assignment statement in the oracle.
    /// </summary>
    /// <param name="typeKey">One of the nine type keys.</param>
    /// <returns>The oracle line's text.</returns>
    /// <remarks>
    /// The five numeric bodies share one text, which is itself part of the parity story: they are
    /// textually identical in the oracle and are still kept as five separate members, because overload
    /// selection is what decides which one a caller reaches.
    /// </remarks>
    private static string OracleAssignmentText(string typeKey) => typeKey switch
    {
        DecimalKey or ShortKey or LongKey or DoubleKey or FloatKey => "sVal = String(val)",
        StringKey => "sVal = \"'\" + val + \"'\"",
        DateTimeKey => "sVal = \"DateTime('\" + String(val) + \"')\"",
        DateOnlyKey => "sVal = \"Date('\" + String(val) + \"')\"",
        TimeOnlyKey => "sVal = \"Time('\" + String(val) + \"')\"",
        _ => throw new ArgumentOutOfRangeException(nameof(typeKey), typeKey, "Unknown type key."),
    };

    // ==============================================================================================
    //  HELPERS - READING THE REPOSITORY, READ-ONLY (C-C)
    // ==============================================================================================

    /// <summary>
    /// One 1-BASED line of a repository file, with its line terminator removed.
    /// </summary>
    /// <param name="repositoryRelativePath">Forward-slashed path from the repository root.</param>
    /// <param name="lineNumber">The 1-based line number, as a legacy locator spells it.</param>
    /// <returns>The line's text.</returns>
    /// <remarks>
    /// ONE-BASED, MATCHING THE LOCATORS RATHER THAN THE ARRAY. Every citation in this file and in the
    /// AAP is a 1-based PowerBuilder export line number, and silently reading a 0-based index would
    /// shift every assertion by one line - the single most dangerous mechanical hazard in this
    /// refactor, applied to a test instead of to a loop. The conversion is done once, here, and the
    /// bound is checked so an out-of-range citation fails with the locator in the message rather than
    /// with an index exception.
    /// </remarks>
    private static string ReadRepositoryLine(string repositoryRelativePath, int lineNumber)
    {
        string[] lines = ReadRepositoryText(repositoryRelativePath).Split('\n');

        Assert.InRange(lineNumber, 1, lines.Length);

        return lines[lineNumber - 1].TrimEnd('\r');
    }

    /// <summary>
    /// The full text of a repository file, located by walking up from the test assembly.
    /// </summary>
    /// <param name="repositoryRelativePath">Forward-slashed path from the repository root.</param>
    /// <returns>The file's text.</returns>
    /// <remarks>
    /// An upward SEARCH rather than a fixed number of parent hops, because the distance from a test
    /// assembly to the repository root is a function of the configuration and the target framework in
    /// the output path, so a fixed count breaks silently when either changes. The walk anchors on BOTH
    /// root markers together, so it cannot latch onto a same-named directory elsewhere on the machine.
    /// <para>
    /// A file that cannot be located FAILS rather than skips. Standing down would turn a structural
    /// claim into a test that reports success without having read anything, which is the failure mode
    /// a source-level assertion exists to rule out.
    /// </para>
    /// <para>
    /// Strictly read-only: nothing here creates, writes, moves or reformats a path, which is what
    /// constraint C-C requires of every access to the legacy tree.
    /// </para>
    /// </remarks>
    private static string ReadRepositoryText(string repositoryRelativePath)
    {
        string[] segments = repositoryRelativePath.Split('/');

        DirectoryInfo? candidate = new(AppContext.BaseDirectory);

        while (candidate is not null)
        {
            if (File.Exists(Path.Combine(candidate.FullName, SolutionMarkerFileName))
                && File.Exists(Path.Combine(candidate.FullName, PackageVersionMarkerFileName)))
            {
                string file = Path.Combine([candidate.FullName, .. segments]);

                Assert.True(
                    File.Exists(file),
                    $"'{repositoryRelativePath}' was not found at '{file}'. It is the subject of a "
                        + "source-level assertion, so its absence is the finding rather than a reason "
                        + "to stand down.");

                return File.ReadAllText(file);
            }

            // Parent is null at the filesystem root, which is what terminates the walk.
            candidate = candidate.Parent;
        }

        Assert.Fail(
            $"No directory from '{AppContext.BaseDirectory}' up to the filesystem root holds both "
                + $"repository markers '{SolutionMarkerFileName}' and "
                + $"'{PackageVersionMarkerFileName}', so '{repositoryRelativePath}' could not be "
                + "located. Both probes are read-only; nothing was created.");

        return string.Empty;
    }

    /// <summary>
    /// C# source text with its <c>//</c> line comments removed, so a search sees CODE only.
    /// </summary>
    /// <param name="source">C# source text.</param>
    /// <returns>The same text with line comments cut away.</returns>
    /// <remarks>
    /// <para>
    /// A deliberately small scanner rather than a real parser, and its limits are stated because they
    /// determine which claims may rest on it. It tracks string literals so a <c>//</c> INSIDE a
    /// literal does not truncate the line, and it skips the character after a backslash inside a
    /// literal so an escaped quote does not close it. It does NOT handle block comments, verbatim
    /// literals or raw string literals.
    /// </para>
    /// <para>
    /// That is safe here, and safe in the conservative direction. The subject's citations of the
    /// oracle all sit in <c>//</c> and <c>///</c> lines, and it contains no verbatim or raw string
    /// literal. Should a future edit put a sentinel spelling inside a block comment, this filter
    /// would report duplication that is not there - a LOUD failure, never a silent pass, which is the
    /// only acceptable direction for a check whose whole purpose is to notice a copy.
    /// </para>
    /// </remarks>
    private static string CodeOnlyText(string source)
    {
        StringBuilder code = new(source.Length);

        foreach (string line in source.Split('\n'))
        {
            bool inString = false;
            int cut = line.Length;

            for (int index = 0; index < line.Length; index++)
            {
                char current = line[index];

                if (current == '\\' && inString)
                {
                    // Skip the escaped character so an escaped quote does not close the literal.
                    index++;
                    continue;
                }

                if (current == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (!inString && current == '/' && index + 1 < line.Length && line[index + 1] == '/')
                {
                    cut = index;
                    break;
                }
            }

            code.Append(line, 0, cut).Append('\n');
        }

        return code.ToString();
    }
}
