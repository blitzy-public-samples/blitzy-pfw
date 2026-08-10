// ==================================================================================================
//  ExpressionVariableEnvironmentTests - THE TYPED-UNION AND PER-REFERENCE-MODE ACCEPTANCE SUITE
//  ------------------------------------------------------------------------------------------------
//  SUBJECT   PowerFramework.DataServices.Expressions.ExpressionVariableEnvironment
//            (the file, i.e. all TWELVE types it declares - the kind discriminator, the value union,
//            the five structure ports, the dispatch and rejection enums, the projection, the sigil
//            descriptor and the environment itself)
//
//  ORACLE    ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru (2,435 lines)
//                :L50-L56   vardata          5 fields
//                :L58-L63   funcdata         4 fields
//                :L65-L71   globalvardata    5 fields
//                :L73-L78   localvardata     4 fields, RECURSIVE
//                :L80-L83   foreignvardata   2 fields, the live-pointer field
//                :L117-L118 VAR_LOCAL = 0 / VAR_FOREIGN = 1
//                :L153-L159 the SEVEN typed of_addvar overloads - and there is no eighth
//                :L1284-L1300 the three sigil bits the parser sets
//            docs/n_cst_dwsvc_columnexp.md (the authoritative specification)
//                :L34  a foreign variable REQUIRES dynamic expansion
//                :L49  the static worked example, permanently fixed at 5
//                :L68  the MIXED expression "$$上月读数 + $本月读数", which yields 6
//            ws_objects/pfw.tests.pbl.src/w_test_dwsvc_columnexp.srw (the live scenario)
//                :L104-L109 the six variable definitions, including the QUOTED 回程 and the TYPED 精度
//                :L111-L114 num1, num2, exp1, exp2 - exp2 wraps 回程 in dec()
//                :L157      one line mixing macro-direct, dynamic-indirect and dynamic-foreign
//                :L292      of_SetVar(name, value, recalc) - the middle arity, in live use
//            ws_objects/pfw.tests.pbl.src/dw_test_dwsvc_columnexp.srd (n1..n3 decimal(2), s1..s2 char)
//            All four are READ ONLY. They are the behavioural oracle and never an edit target (C-C).
//
//  GOVERNING RULES
//  ------------------------------------------------------------------------------------------------
//  `review_rules` returns "No user rules provided." NO USER RULE GOVERNS THIS FILE, and none is
//  invented here. The bar applied in their place is the refactor's own enterprise baseline - nullable
//  reference types on, warnings as errors, plain xunit assertions, hand-written doubles only, no
//  mocking or fluent-assertion package - together with these binding constraints:
//
//    C-A   The wire projection is asserted against the GENERATED dataservices.v1 types. No proto enum
//          value is ever retyped as a literal in this file; the generated members are referenced by
//          name and their numeric values are read off them.
//    C-B   The seven types, the five structure field lists and the two variable-type constants are
//          LEGACY BEHAVIOUR and are asserted as such - including the ones a modern design would have
//          chosen differently.
//    C-K   Every test and every theory row carries the locator that justifies it. The rows carry it as
//          a parameter, so it appears in the test's display name and a failure names its own evidence.
//
//  THE TWO PROPERTIES THIS SUITE EXISTS TO PIN
//  ------------------------------------------------------------------------------------------------
//  1. THE ENVIRONMENT IS A DISCRIMINATED UNION OVER EXACTLY SEVEN TYPES, NEVER A STRING MAP.
//     Contract C-04 states both the requirement and the reason: collapsing the seven to their string
//     renderings discards exactly the information the legacy coercion dispatches on, and the loss
//     stays invisible until a result is subtly wrong. So it is not enough to assert that a union
//     exists - the LOSS must be asserted not to occur. The single clearest proof is that a string
//     variable holding "5" is NOT equal to a numeric variable holding 5, and the oracle makes that
//     observable rather than theoretical: 回程 is defined as the QUOTED value '3' [w_test:L106] and an
//     expression later wraps it in dec() to get a number back out of it [w_test:L114].
//
//  2. THE EXPANSION MODE IS A PROPERTY OF A REFERENCE, NEVER OF AN EXPRESSION.
//     The specification's own mixed example puts a dynamic and a static reference in ONE expression
//     [docs:L68] and the live scenario puts three differently-classified references on one line
//     [w_test:L157]. A model that tagged the expression could represent neither, so the suite asserts
//     the mode per reference and then corroborates it against what the engine actually parses.
//
//  WHAT THIS SUITE DELIBERATELY DOES NOT DO
//  ------------------------------------------------------------------------------------------------
//  It does not re-test the engine's calculation semantics - the 5-against-6 golden pair, the
//  parenthesis wrap, whole-token replacement and the caret-bearing parse-error matrix all belong to
//  ColumnExpressionEngineTests and are asserted there. Where a test below drives the engine it does so
//  ONLY to observe the environment: what the table contains afterwards, what index a name resolves to,
//  and which sigils the parser actually recorded on a reference. The subject is always the type model.
//
//  DETERMINISM
//  ------------------------------------------------------------------------------------------------
//  Nothing here reads a clock, a random source, a network or a database. Most tests construct value
//  types directly; the few that need an engine build one over the ported column-expression fixture,
//  which is a hand-written in-memory double. That is what AAP section 0.6.7 requires of a
//  characterization fixture, and it is why every assertion below is repeatable.
// ==================================================================================================

using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;

using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.DataServices.Expressions;
using PowerFramework.Shared.Kernel;

using Xunit;

using WireDateTimeValue = PowerFramework.Contracts.Common.V1.DateTimeValue;
using WireDateValue = PowerFramework.Contracts.Common.V1.DateValue;
using WireTimeValue = PowerFramework.Contracts.Common.V1.TimeValue;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// The acceptance suite for the macro variable type model: the seven-variant union, the two preserved
/// variable-type constants, the five structure ports, the three-bit sigil descriptor and the one-based
/// environment itself.
/// </summary>
public sealed class ExpressionVariableEnvironmentTests
{
    // ==============================================================================================
    //  THE ORACLE'S OWN VOCABULARY
    //  --------------------------------------------------------------------------------------------
    //  The scenario's variable names are transcribed rather than translated, and that is a testing
    //  decision rather than a stylistic one. The legacy parser scans byte by byte and computes token
    //  offsets from character positions [:L1407-L1412], so a multi-byte identifier is exactly where a
    //  length-versus-byte-count confusion would surface; a Latin rename would not exercise the same
    //  path and would not be findable in the oracle either. Each is glossed so a reader who does not
    //  read Chinese can still follow what the test is doing.
    // ==============================================================================================

    /// <summary>损耗 - "loss". Defined as the expression "1" [w_test_dwsvc_columnexp.srw:L104].</summary>
    private const string Loss = "损耗";

    /// <summary>倍率 - "multiplier". Defined as the expression "2" [w_test:L105].</summary>
    private const string Multiplier = "倍率";

    /// <summary>
    /// 回程 - "return trip". Defined as the QUOTED value '3' [w_test:L106], which is why an expression
    /// that needs it as a number has to wrap it in <c>dec(...)</c> [w_test:L114]. It is this suite's
    /// evidence that string-versus-numeric identity is observable legacy behaviour and not a detail.
    /// </summary>
    private const string ReturnTrip = "回程";

    /// <summary>
    /// 上月读数 - "last month's reading". The variable that gets MUTATED in the specification's worked
    /// example, and therefore the one whose sigil decides whether the result changes [docs:L49, :L68].
    /// </summary>
    private const string LastMonth = "上月读数";

    /// <summary>本月读数 - "this month's reading". The expression variable valued "5" [docs:L47, w_test:L108].</summary>
    private const string ThisMonth = "本月读数";

    /// <summary>精度 - "precision". The one variable in the scenario defined with a TYPED value rather than
    /// an expression: <c>of_AddVar("精度", 0)</c> [w_test:L109].</summary>
    private const string Precision = "精度";

    /// <summary>num1 [w_test:L111] - an expression variable over 上月读数 and 精度.</summary>
    private const string Num1 = "num1";

    /// <summary>num2 [w_test:L112] - an expression variable over 本月读数 and 精度.</summary>
    private const string Num2 = "num2";

    /// <summary>exp1 [w_test:L113] - an expression variable over 本月读数, 倍率 and 损耗.</summary>
    private const string Exp1 = "exp1";

    /// <summary>exp2 [w_test:L114] - as exp1, plus <c>dec($回程)</c>.</summary>
    private const string Exp2 = "exp2";

    /// <summary>
    /// 数据汇总 - "data summary". Defined on the OTHER DataWindow as "SUM(n1)" [w_test:L140] and reached
    /// from this one through <c>of_AddForeignVar</c> [w_test:L143], so it is the scenario's foreign
    /// variable and the only one that requires dynamic expansion [docs:L34].
    /// </summary>
    private const string Summary = "数据汇总";

    /// <summary>
    /// The macro function the scenario's application implements, in the direct form <c>$FormatPrice(...)</c>
    /// [w_test:L111-L114, handler at :L305].
    /// </summary>
    private const string FormatPrice = "FormatPrice";

    /// <summary>The oracle path every locator in this file is relative to.</summary>
    private const string OraclePath = "n_cst_dwsvc_columnexp.sru";

    /// <summary>
    /// The FULL NAMES of the types that would represent a LIVE OBJECT REFERENCE if any of them appeared
    /// anywhere in the variable model's member closure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>foreignvardata.expsvc</c> is declared <c>n_cst_dwsvc_columnexp expsvc</c> [:L82] - a pointer to
    /// another live service instance - and the whole point of the narrowing is that the port carries an
    /// opaque handle VALUE instead. Resolving a handle is session state and belongs to
    /// <c>ExpressionSession</c>, so none of these may be reachable from a variable structure.
    /// </para>
    /// <para>
    /// THEY ARE NAMED AS STRINGS AND RESOLVED THROUGH THE ASSEMBLY, DELIBERATELY. The constraint being
    /// tested is expressed in terms of what a member must not be able to HOLD, and naming the types keeps
    /// this suite from taking a compile-time dependency on the host, session and evaluator modules merely in
    /// order to forbid them. <see cref="LiveObjectTypes"/> asserts every name resolves, so the list cannot
    /// rot into a vacuous check if one of them is renamed.
    /// </para>
    /// </remarks>
    private static readonly ImmutableArray<string> LiveObjectTypeNames =
    [
        "PowerFramework.DataServices.Domain.DataWindowServiceHost",
        "PowerFramework.DataServices.Domain.IDataWindowObject",
        "PowerFramework.DataServices.Expressions.ColumnExpressionEngine",
        "PowerFramework.DataServices.Expressions.ExpressionSession",
        "PowerFramework.DataServices.Expressions.DataWindowExpressionEvaluator",
        "PowerFramework.DataServices.Expressions.MacroInvoker",
        "PowerFramework.DataServices.Expressions.DataWindowHandle",
    ];

    /// <summary>
    /// The suite's cancellation token. Every awaited call that accepts one is given it, because the
    /// xunit analyzer that requires it is an ERROR under this repository's warnings-as-errors gate.
    /// </summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ==============================================================================================
    //  HELPERS - hand written, no mocking package, nothing that reads a clock
    // ==============================================================================================

    /// <summary>
    /// The live-object types, resolved from <see cref="LiveObjectTypeNames"/> against the service assembly.
    /// </summary>
    /// <returns>The resolved types, one per name.</returns>
    /// <remarks>
    /// Every name MUST resolve. A name that silently failed to resolve would turn the forbidden-type sweep
    /// into a check that forbids nothing, which is the one way that test could pass while being worthless.
    /// </remarks>
    private static ImmutableArray<Type> LiveObjectTypes()
    {
        List<Type> resolved = [];

        foreach (string name in LiveObjectTypeNames)
        {
            Type? live = typeof(ExpressionVariableEnvironment).Assembly.GetType(name);
            Assert.NotNull(live);
            resolved.Add(live);
        }

        Assert.Equal(LiveObjectTypeNames.Length, resolved.Count);
        return [.. resolved];
    }

    /// <summary>
    /// An engine over the ported column-expression fixture, used ONLY where a test needs to observe what
    /// the real parser recorded on a reference or what the real table contains after a failure.
    /// </summary>
    /// <param name="host">Receives the fake host, so a test can inspect it when it needs to.</param>
    /// <returns>An initialised, enabled engine with a private session and a fixed session identifier.</returns>
    private static ColumnExpressionEngine CreateEngine(out FakeDataWindowHost host) =>
        CreateEngineIn(NewSession(), out host);

    /// <summary>
    /// A session with a FIXED identifier, so any handle or message a test observes is a stable string
    /// rather than a fresh identifier per run - which is what AAP section 0.6.7's repeatability
    /// prerequisite requires.
    /// </summary>
    /// <returns>The session.</returns>
    private static ExpressionSession NewSession() =>
        new(
            "expression-variable-environment-tests",
            lifetime: null,
            columnExpression: null,
            timeProvider: null,
            traceSink: null);

    /// <summary>
    /// An engine registered in a SUPPLIED session, so two engines can be made co-resident - the only
    /// arrangement in which a cross-DataWindow variable resolves at all.
    /// </summary>
    /// <param name="session">The session both engines share.</param>
    /// <param name="host">Receives the fake host.</param>
    /// <returns>An initialised, enabled engine.</returns>
    private static ColumnExpressionEngine CreateEngineIn(
        ExpressionSession session,
        out FakeDataWindowHost host)
    {
        host = FakeDataWindowFixtures.CreateColumnExpressionFixture();

        ColumnExpressionEngine engine = new(session: session);
        engine.OnInit(host);
        engine.SetEnabled(true);
        return engine;
    }

    /// <summary>
    /// The canonical sample payload for one variant, chosen so that the string, long and double samples
    /// all render as the digit 3 - which is the 回程 value [w_test:L106] and therefore makes the
    /// cross-variant inequality assertions read as the oracle's own scenario.
    /// </summary>
    /// <param name="kind">The variant.</param>
    /// <returns>The payload, boxed.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The kind is not one of the seven.</exception>
    private static object SamplePayload(ExpressionVariableKind kind) => kind switch
    {
        ExpressionVariableKind.TimeValue => new TimeOnly(13, 45, 30),
        ExpressionVariableKind.StringValue => "3",
        ExpressionVariableKind.LongValue => 3L,
        ExpressionVariableKind.DoubleValue => 3d,
        ExpressionVariableKind.DateTimeValue => new DateTime(2026, 1, 1, 13, 45, 30, DateTimeKind.Unspecified),
        ExpressionVariableKind.DateValue => new DateOnly(2026, 1, 1),
        ExpressionVariableKind.BooleanValue => true,
        _ => throw new ArgumentOutOfRangeException(
            nameof(kind),
            kind,
            "Only the seven legacy variants have a payload. None is the unset discriminator and carries "
                + "no payload of any type [" + OraclePath + ":L153-L159]."),
    };

    /// <summary>
    /// Builds a value of one variant, either carrying its canonical payload or carrying the PowerBuilder
    /// null of that type.
    /// </summary>
    /// <param name="kind">The variant to build.</param>
    /// <param name="withPayload">
    /// <see langword="true"/> for the canonical payload; <see langword="false"/> for null, which every
    /// one of the seven must be able to represent WITHOUT collapsing to the type's default.
    /// </param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The kind is not one of the seven.</exception>
    private static ExpressionVariableValue Build(ExpressionVariableKind kind, bool withPayload) => kind switch
    {
        ExpressionVariableKind.TimeValue => ExpressionVariableValue.FromTime(
            withPayload ? (TimeOnly)SamplePayload(kind) : null),
        ExpressionVariableKind.StringValue => ExpressionVariableValue.FromString(
            withPayload ? (string)SamplePayload(kind) : null),
        ExpressionVariableKind.LongValue => ExpressionVariableValue.FromLong(
            withPayload ? (long)SamplePayload(kind) : null),
        ExpressionVariableKind.DoubleValue => ExpressionVariableValue.FromDouble(
            withPayload ? (double)SamplePayload(kind) : null),
        ExpressionVariableKind.DateTimeValue => ExpressionVariableValue.FromDateTime(
            withPayload ? (DateTime)SamplePayload(kind) : null),
        ExpressionVariableKind.DateValue => ExpressionVariableValue.FromDate(
            withPayload ? (DateOnly)SamplePayload(kind) : null),
        ExpressionVariableKind.BooleanValue => ExpressionVariableValue.FromBoolean(
            withPayload ? (bool)SamplePayload(kind) : null),
        _ => throw new ArgumentOutOfRangeException(
            nameof(kind),
            kind,
            "There is no factory for the unset discriminator; use ExpressionVariableValue.Unset."),
    };

    /// <summary>
    /// Builds a value of one variant carrying that CLR type's own <c>default</c> as a PRESENT payload -
    /// midnight, the empty string, zero, the zero date, false. Paired with <c>Build(kind, false)</c> it
    /// is what makes "null never collapses to the default" an assertion rather than a claim.
    /// </summary>
    /// <param name="kind">The variant to build.</param>
    /// <returns>The value, present and equal to its type's default.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The kind is not one of the seven.</exception>
    private static ExpressionVariableValue BuildTypeDefault(ExpressionVariableKind kind) => kind switch
    {
        ExpressionVariableKind.TimeValue => ExpressionVariableValue.FromTime(default(TimeOnly)),
        ExpressionVariableKind.StringValue => ExpressionVariableValue.FromString(string.Empty),
        ExpressionVariableKind.LongValue => ExpressionVariableValue.FromLong(0L),
        ExpressionVariableKind.DoubleValue => ExpressionVariableValue.FromDouble(0d),
        ExpressionVariableKind.DateTimeValue => ExpressionVariableValue.FromDateTime(default(DateTime)),
        ExpressionVariableKind.DateValue => ExpressionVariableValue.FromDate(default(DateOnly)),
        ExpressionVariableKind.BooleanValue => ExpressionVariableValue.FromBoolean(false),
        _ => throw new ArgumentOutOfRangeException(
            nameof(kind),
            kind,
            "There is no factory for the unset discriminator; use ExpressionVariableValue.Unset."),
    };

    /// <summary>
    /// Reads a value through the accessor that MATCHES a nominated variant, whether or not the value
    /// actually carries that variant. Used both for the round-trip assertions and, with a deliberately
    /// mismatched variant, for the assertions that a wrong-variant read is a defined failure.
    /// </summary>
    /// <param name="value">The value to read.</param>
    /// <param name="asKind">The variant whose accessor to use.</param>
    /// <returns>The payload, boxed, or <see langword="null"/> when the payload is null.</returns>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="value"/> does not carry <paramref name="asKind"/>. That is the subject's own
    /// contract and is exactly what several tests below assert.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The kind is not one of the seven.</exception>
    private static object? ReadAs(ExpressionVariableValue value, ExpressionVariableKind asKind) => asKind switch
    {
        ExpressionVariableKind.TimeValue => value.AsTime(),
        ExpressionVariableKind.StringValue => value.AsString(),
        ExpressionVariableKind.LongValue => value.AsLong(),
        ExpressionVariableKind.DoubleValue => value.AsDouble(),
        ExpressionVariableKind.DateTimeValue => value.AsDateTime(),
        ExpressionVariableKind.DateValue => value.AsDate(),
        ExpressionVariableKind.BooleanValue => value.AsBoolean(),
        _ => throw new ArgumentOutOfRangeException(
            nameof(asKind),
            asKind,
            "The unset discriminator has no accessor; test it through IsUnset."),
    };

    /// <summary>
    /// The seven legacy variants, read off the discriminator itself rather than retyped, so a variant
    /// added or removed on the subject changes every matrix in this file at once.
    /// </summary>
    /// <returns>The seven, excluding the unset discriminator, in declaration order.</returns>
    private static ImmutableArray<ExpressionVariableKind> SevenVariants() =>
    [
        .. Enum.GetValues<ExpressionVariableKind>()
            .Where(static kind => kind != ExpressionVariableKind.None)
            .OrderBy(static kind => (int)kind),
    ];

    /// <summary>
    /// Every type reachable as the declared type of a field or property of <paramref name="owner"/>,
    /// with <see cref="Nullable{T}"/> and generic collection arguments unwrapped so that an
    /// <c>ImmutableArray&lt;ForeignVariableReference&gt;</c> contributes both itself and its element
    /// type.
    /// </summary>
    /// <param name="owner">The type to inspect.</param>
    /// <returns>The distinct member types, including those of non-public members.</returns>
    /// <remarks>
    /// NON-PUBLIC MEMBERS ARE INCLUDED ON PURPOSE. A live object reference smuggled into a private field
    /// would be just as fatal to serialization as a public one, so the sweep must not stop at the public
    /// surface. Compiler-generated backing fields are therefore in scope too, which is harmless: their
    /// types are the same types as the properties they back.
    /// </remarks>
    private static ImmutableArray<Type> DeclaredMemberTypes(Type owner)
    {
        const BindingFlags Everything = BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        List<Type> seen = [];

        foreach (Type declared in owner.GetFields(Everything).Select(static field => field.FieldType)
            .Concat(owner.GetProperties(Everything).Select(static property => property.PropertyType)))
        {
            seen.Add(declared);

            Type? unwrapped = Nullable.GetUnderlyingType(declared);
            if (unwrapped is not null)
            {
                seen.Add(unwrapped);
            }

            if (declared.IsGenericType)
            {
                seen.AddRange(declared.GetGenericArguments());
            }

            if (declared.IsArray && declared.GetElementType() is Type element)
            {
                seen.Add(element);
            }
        }

        return [.. seen.Distinct()];
    }

    /// <summary>
    /// Walks a chain of variable definitions by the ONE-BASED indexes their references carry, which is the
    /// only way the recursion of <c>localvardata</c> is expressed in this model.
    /// </summary>
    /// <param name="environment">The table to walk.</param>
    /// <param name="startIndex">The one-based index to start from.</param>
    /// <param name="budget">
    /// The maximum number of hops the walk will take. A CONSUMER-IMPOSED BOUND, and the point of the
    /// signature: the model hands out <see cref="ExpressionVariableEnvironment.IsValidIndex(int)"/> and a
    /// zero-means-unbound convention precisely so that a walk can be bounded rather than trusted.
    /// </param>
    /// <returns>
    /// The names visited in order, and whether the walk ended because it ran out of references
    /// (<see langword="true"/>) or because it hit the budget or a repeat (<see langword="false"/>).
    /// </returns>
    private static (ImmutableArray<string> Visited, bool Completed) WalkVariableChain(
        ExpressionVariableEnvironment environment,
        int startIndex,
        int budget)
    {
        List<string> visited = [];
        HashSet<int> seen = [];
        int index = startIndex;

        for (int hop = 0; hop < budget; hop++)
        {
            if (!environment.IsValidIndex(index) || !seen.Add(index))
            {
                return ([.. visited], false);
            }

            GlobalVariable entry = environment[index];
            visited.Add(entry.Name);

            if (entry.Local.Vars.IsEmpty)
            {
                return ([.. visited], true);
            }

            index = entry.Local.Vars[0].Index;
        }

        return ([.. visited], false);
    }

    // ==============================================================================================
    //  1. THE SEVEN-VARIANT UNION
    //  --------------------------------------------------------------------------------------------
    //  Contract C-04's requirement in one sentence: "A DISCRIMINATED UNION OVER EXACTLY SEVEN TYPES ...
    //  NEVER A STRINGLY-TYPED MAP". Asserting that a union exists is the easy half. The half that
    //  matters is asserting that the information a string map would have LOST is still there, which is
    //  what the cross-variant inequality and the typed-null tests below are for.
    // ==============================================================================================

    /// <summary>
    /// The seven variants with the <c>of_addvar</c> overload each was read off, one row per variant.
    /// </summary>
    /// <remarks>
    /// THE ROWS ARE THE ORACLE'S OWN ORDER, and the locator on each row is the overload that proves the
    /// variant exists. Seven rows because there are seven overloads; an eighth row would have no
    /// overload to cite, which is the discipline the locator column enforces.
    /// </remarks>
    public static TheoryData<ExpressionVariableKind, string> VariantRoster() => new()
    {
        { ExpressionVariableKind.TimeValue, OraclePath + ":L153" },
        { ExpressionVariableKind.StringValue, OraclePath + ":L154" },
        { ExpressionVariableKind.LongValue, OraclePath + ":L155" },
        { ExpressionVariableKind.DoubleValue, OraclePath + ":L156" },
        { ExpressionVariableKind.DateTimeValue, OraclePath + ":L157" },
        { ExpressionVariableKind.DateValue, OraclePath + ":L158" },
        { ExpressionVariableKind.BooleanValue, OraclePath + ":L159" },
    };

    /// <summary>
    /// EXACTLY SEVEN scalar variants exist, plus the unset discriminator, and they are in the oracle's
    /// declaration order with the oracle's own numbering.
    /// </summary>
    /// <remarks>
    /// The seven are the seven typed <c>of_addvar</c> overloads [:L153-L159], confirmed by the seven
    /// matching <c>of_setvar</c> overloads. There is no eighth because a caller cannot declare a variable
    /// of any other type: no other overload exists to declare it with.
    /// </remarks>
    [Fact]
    public void SevenScalarVariants_AreTheClosedSetPlusTheUnsetDiscriminator()
    {
        ImmutableArray<ExpressionVariableKind> declared = [.. Enum.GetValues<ExpressionVariableKind>()];

        // Eight members: the seven legacy types, plus None for an unset union.
        Assert.Equal(8, declared.Length);
        Assert.Contains(ExpressionVariableKind.None, declared);
        Assert.Equal(0, (int)ExpressionVariableKind.None);

        ExpressionVariableKind[] expectedOrder =
        [
            ExpressionVariableKind.TimeValue,
            ExpressionVariableKind.StringValue,
            ExpressionVariableKind.LongValue,
            ExpressionVariableKind.DoubleValue,
            ExpressionVariableKind.DateTimeValue,
            ExpressionVariableKind.DateValue,
            ExpressionVariableKind.BooleanValue,
        ];

        Assert.Equal(expectedOrder, SevenVariants());

        // The numbering is contiguous from 1, which is what lets the domain discriminator and the wire
        // discriminator be audited against one another by value as well as by name.
        for (int position = 0; position < expectedOrder.Length; position++)
        {
            Assert.Equal(position + 1, (int)expectedOrder[position]);
        }
    }

    /// <summary>
    /// THE NUMERIC VARIANT IS <c>double</c> AND NOT <c>decimal</c>, and no decimal arm exists anywhere in
    /// the model or in the entry points that feed it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The oracle's fourth overload is declared <c>of_addvar(readonly string name, readonly double
    /// value)</c> [:L156] and the overload set at [:L153-L159] contains NO decimal arm at all - the
    /// published contract makes the same point where it declines to reuse the canonical decimal carrier.
    /// </para>
    /// <para>
    /// WIDENING IT TO <c>decimal</c> WOULD CHANGE THE ARITHMETIC THE ENGINE PERFORMS, which is why this
    /// is a test and not a comment. A decimal variant would also let a caller declare a variable the
    /// legacy has no overload for, leaving the coercion path with nothing to dispatch on. The refactor's
    /// general rule that a legacy decimal must never be carried as a double is untouched: there is no
    /// legacy decimal here to carry.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheNumericVariantIsDouble_AndNoDecimalArmExistsAnywhere()
    {
        Assert.Equal(ExpressionVariableKind.DoubleValue, ExpressionVariableValue.FromDouble(3d).Kind);
        Assert.Equal(3d, ExpressionVariableValue.FromDouble(3d).AsDouble());

        // No variant is named for a decimal.
        Assert.DoesNotContain(
            "Decimal",
            string.Join(',', Enum.GetNames<ExpressionVariableKind>()),
            StringComparison.OrdinalIgnoreCase);

        // No factory parameter and no accessor return type is a decimal, in either nullability.
        Type[] valueSignature =
        [
            .. typeof(ExpressionVariableValue)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .SelectMany(static method => method
                    .GetParameters()
                    .Select(static parameter => parameter.ParameterType)
                    .Append(method.ReturnType)),
        ];

        Assert.DoesNotContain(typeof(decimal), valueSignature);
        Assert.DoesNotContain(typeof(decimal?), valueSignature);

        // And the entry points that feed the model carry the same seven and only the seven: the engine's
        // AddVar overload set is one-for-one with [:L153-L159].
        Type[] addVarValueTypes =
        [
            .. typeof(ColumnExpressionEngine)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(static method => method.Name == nameof(ColumnExpressionEngine.AddVar))
                .Select(static method => method.GetParameters()[1].ParameterType),
        ];

        Assert.Equal(7, addVarValueTypes.Length);
        Assert.DoesNotContain(typeof(decimal), addVarValueTypes);
        Assert.DoesNotContain(typeof(decimal?), addVarValueTypes);
    }

    /// <summary>
    /// A value CARRIES ITS TYPE and round-trips through the matching accessor without ever passing through
    /// a string, and no two variants are ever equal.
    /// </summary>
    /// <param name="kind">The variant under test.</param>
    /// <param name="locator">The <c>of_addvar</c> overload this variant was read off.</param>
    [Theory]
    [MemberData(nameof(VariantRoster))]
    public void EachVariant_CarriesItsTypeAndRoundTripsWithoutStringification(
        ExpressionVariableKind kind,
        string locator)
    {
        Assert.StartsWith(OraclePath + ":L", locator, StringComparison.Ordinal);

        ExpressionVariableValue value = Build(kind, withPayload: true);

        Assert.Equal(kind, value.Kind);
        Assert.False(value.IsUnset);
        Assert.True(value.HasValue);

        // The payload comes back as the CLR type it went in as. Not a rendering of it, and not a
        // conversion of it - the same type, comparing equal.
        object? readBack = ReadAs(value, kind);
        Assert.NotNull(readBack);
        Assert.Equal(SamplePayload(kind), readBack);
        Assert.Equal(SamplePayload(kind).GetType(), readBack.GetType());

        // Equality covers kind AND payload, so an independently constructed twin is equal, which is what
        // makes these values directly assertable in a characterization recording.
        Assert.Equal(Build(kind, withPayload: true), value);
        Assert.Equal(Build(kind, withPayload: true).GetHashCode(), value.GetHashCode());

        // No other variant is equal to it, whatever that variant carries, and neither is unset.
        Assert.NotEqual(ExpressionVariableValue.Unset, value);
        foreach (ExpressionVariableKind other in SevenVariants().Where(other => other != kind))
        {
            Assert.NotEqual(Build(other, withPayload: true), value);
            Assert.NotEqual(Build(other, withPayload: false), value);
        }
    }

    /// <summary>
    /// Reading a value through the WRONG variant's accessor is a defined failure that names both kinds -
    /// never an implicit conversion and never a silent default.
    /// </summary>
    /// <param name="kind">The variant the value actually carries.</param>
    /// <param name="locator">The <c>of_addvar</c> overload this variant was read off.</param>
    /// <remarks>
    /// A silent default would be indistinguishable from a legitimate value and would corrupt a
    /// calculation without reporting anything, which is the failure mode a parity migration can least
    /// afford. The unset value answers the same way for every accessor, because no variant is selected at
    /// all.
    /// </remarks>
    [Theory]
    [MemberData(nameof(VariantRoster))]
    public void ReadingTheWrongVariant_IsADefinedFailureAndNeverACoercion(
        ExpressionVariableKind kind,
        string locator)
    {
        Assert.StartsWith(OraclePath + ":L", locator, StringComparison.Ordinal);

        ExpressionVariableValue value = Build(kind, withPayload: true);

        foreach (ExpressionVariableKind wrong in SevenVariants().Where(wrong => wrong != kind))
        {
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
                () => { _ = ReadAs(value, wrong); });

            // Both kinds are named, because the pair together is what a caller needs in order to fix the
            // branch that got it wrong.
            Assert.Contains(kind.ToString(), failure.Message, StringComparison.Ordinal);
            Assert.Contains(wrong.ToString(), failure.Message, StringComparison.Ordinal);
        }

        // A typed null fails the same way: the mismatch is decided by the KIND, not by the payload.
        ExpressionVariableValue nullValue = Build(kind, withPayload: false);
        foreach (ExpressionVariableKind wrong in SevenVariants().Where(wrong => wrong != kind))
        {
            Assert.Throws<InvalidOperationException>(() => { _ = ReadAs(nullValue, wrong); });
        }

        // And the unset value has no accessor that succeeds.
        Assert.Throws<InvalidOperationException>(
            () => { _ = ReadAs(ExpressionVariableValue.Unset, kind); });
    }

    /// <summary>
    /// A <c>long</c> variable is NOT silently widened to a <c>double</c> on the way out.
    /// </summary>
    /// <remarks>
    /// This is the one mismatch a reader is most likely to expect the model to forgive, so it gets its own
    /// test with the exact message asserted. Widening is a coercion, coercion is dispatched by the engine
    /// on the DECLARED type, and doing it in an accessor would hide which of the two overloads
    /// [:L155 against :L156] the caller actually reached.
    /// </remarks>
    [Fact]
    public void ALongVariable_IsNotSilentlyWidenedToADouble()
    {
        ExpressionVariableValue three = ExpressionVariableValue.FromLong(3L);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => { _ = three.AsDouble(); });

        Assert.Contains(
            "carries LongValue, not DoubleValue",
            failure.Message,
            StringComparison.Ordinal);

        // The value itself is untouched by the failed read.
        Assert.Equal(3L, three.AsLong());
        Assert.Equal(ExpressionVariableKind.LongValue, three.Kind);
    }

    /// <summary>
    /// A STRING variant holding digits is never equal to a NUMERIC variant holding that number. THE
    /// SINGLE CLEAREST PROOF THAT THE TYPE INFORMATION SURVIVED.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ORACLE MAKES THIS OBSERVABLE RATHER THAN THEORETICAL. 回程 is defined as the QUOTED value
    /// <c>'3'</c> [w_test_dwsvc_columnexp.srw:L106], and exp2 therefore has to wrap it in <c>dec(...)</c>
    /// to use it as a number [w_test:L114]. That conversion is only necessary BECAUSE a quoted 3 and a
    /// numeric 3 are different things, so string-versus-numeric identity is legacy behaviour and not a
    /// modelling preference.
    /// </para>
    /// <para>
    /// Collapsing the union to a string map would make all three of these values equal, and the loss
    /// would stay invisible until an expression that added a long to a date started concatenating instead.
    /// </para>
    /// </remarks>
    [Fact]
    public void AStringVariantHoldingDigits_IsNeverEqualToANumericVariantHoldingThatNumber()
    {
        ExpressionVariableValue quotedThree = ExpressionVariableValue.FromString("3");
        ExpressionVariableValue longThree = ExpressionVariableValue.FromLong(3L);
        ExpressionVariableValue doubleThree = ExpressionVariableValue.FromDouble(3d);

        Assert.NotEqual(longThree, quotedThree);
        Assert.NotEqual(doubleThree, quotedThree);
        Assert.NotEqual(doubleThree, longThree);

        Assert.NotEqual(quotedThree.Kind, longThree.Kind);
        Assert.NotEqual(longThree.Kind, doubleThree.Kind);

        // Neither numeric variant will even ANSWER a string read, so there is no path by which a consumer
        // could observe a numeric variable as text and compare the two that way.
        Assert.Throws<InvalidOperationException>(() => { _ = longThree.AsString(); });
        Assert.Throws<InvalidOperationException>(() => { _ = doubleThree.AsString(); });

        // The same holds for the specification's own 5, the value 本月读数 is defined with
        // [docs/n_cst_dwsvc_columnexp.md:L47, w_test:L108].
        Assert.NotEqual(ExpressionVariableValue.FromLong(5L), ExpressionVariableValue.FromString("5"));
        Assert.NotEqual(ExpressionVariableValue.FromDouble(5d), ExpressionVariableValue.FromString("5"));
    }

    /// <summary>
    /// NULL IS REPRESENTABLE IN EVERY ONE OF THE SEVEN and never collapses into the type's default, and the
    /// unset union is a third state distinct from both.
    /// </summary>
    /// <param name="kind">The variant under test.</param>
    /// <param name="locator">The <c>of_addvar</c> overload this variant was read off.</param>
    /// <remarks>
    /// PowerBuilder has null for value types and the framework's own return-code predicates depend on null
    /// being visible, so collapsing null to zero would convert "no value" into "the value zero" - the same
    /// class of silent defect as converting "neither succeeded nor failed" into "succeeded".
    /// </remarks>
    [Theory]
    [MemberData(nameof(VariantRoster))]
    public void NullIsRepresentableInEveryVariant_AndNeverCollapsesToTheDefault(
        ExpressionVariableKind kind,
        string locator)
    {
        Assert.StartsWith(OraclePath + ":L", locator, StringComparison.Ordinal);

        ExpressionVariableValue nullValue = Build(kind, withPayload: false);

        // The VARIANT survives even though the payload does not: this is a typed null, not an absence.
        Assert.Equal(kind, nullValue.Kind);
        Assert.False(nullValue.IsUnset);
        Assert.False(nullValue.HasValue);
        Assert.Null(ReadAs(nullValue, kind));

        // The type's own default is a PRESENT value and is not equal to null. For the string variant that
        // is the empty string, which is present precisely because whether an empty string READS as null
        // is a per-expression legacy setting owned by the engine [columnexpdata.emptystringisnull :L33].
        ExpressionVariableValue typeDefault = BuildTypeDefault(kind);
        Assert.True(typeDefault.HasValue);
        Assert.Equal(kind, typeDefault.Kind);
        Assert.NotEqual(typeDefault, nullValue);

        // Unset is the third state: no variant, no payload, and identical to default(T).
        Assert.NotEqual(ExpressionVariableValue.Unset, nullValue);
        Assert.True(ExpressionVariableValue.Unset.IsUnset);
        Assert.False(ExpressionVariableValue.Unset.HasValue);
        Assert.Equal(ExpressionVariableKind.None, ExpressionVariableValue.Unset.Kind);
        Assert.Equal(default(ExpressionVariableValue), ExpressionVariableValue.Unset);

        // Two typed nulls of the same variant are equal, so a null round-trips through a recording.
        Assert.Equal(Build(kind, withPayload: false), nullValue);
    }

    /// <summary>
    /// The seven domain variants paired with the seven arms of the GENERATED <c>VarValue</c> oneof, one row
    /// per pairing.
    /// </summary>
    /// <remarks>
    /// EVERY WIRE MEMBER IS REFERENCED, NEVER RETYPED (C-A). The rows name the generated enum members
    /// directly, so the numeric field numbers are read off the generated code rather than restated as
    /// literals in this file, and regenerating the protocol definition with a different field number
    /// breaks this matrix rather than sliding past it.
    /// </remarks>
    public static TheoryData<ExpressionVariableKind, VarValue.KindOneofCase, string> WireUnionArms() => new()
    {
        { ExpressionVariableKind.TimeValue, VarValue.KindOneofCase.TimeValue, OraclePath + ":L153" },
        { ExpressionVariableKind.StringValue, VarValue.KindOneofCase.StringValue, OraclePath + ":L154" },
        { ExpressionVariableKind.LongValue, VarValue.KindOneofCase.LongValue, OraclePath + ":L155" },
        { ExpressionVariableKind.DoubleValue, VarValue.KindOneofCase.DoubleValue, OraclePath + ":L156" },
        { ExpressionVariableKind.DateTimeValue, VarValue.KindOneofCase.DatetimeValue, OraclePath + ":L157" },
        { ExpressionVariableKind.DateValue, VarValue.KindOneofCase.DateValue, OraclePath + ":L158" },
        { ExpressionVariableKind.BooleanValue, VarValue.KindOneofCase.BoolValue, OraclePath + ":L159" },
    };

    /// <summary>
    /// Each domain variant projects onto the generated wire arm that carries the same legacy overload, and
    /// the two discriminators agree BY VALUE.
    /// </summary>
    /// <param name="kind">The domain variant.</param>
    /// <param name="arm">The generated wire arm it must project onto.</param>
    /// <param name="locator">The <c>of_addvar</c> overload both were read off.</param>
    [Theory]
    [MemberData(nameof(WireUnionArms))]
    public void EachDomainVariant_ProjectsOntoItsGeneratedWireArm(
        ExpressionVariableKind kind,
        VarValue.KindOneofCase arm,
        string locator)
    {
        Assert.StartsWith(OraclePath + ":L", locator, StringComparison.Ordinal);

        // The domain discriminator's value IS the wire oneof's field number. Asserting it by value rather
        // than by name is what makes the two independently auditable.
        Assert.Equal((int)arm, (int)kind);
    }

    /// <summary>
    /// The domain union and the generated wire union have IDENTICAL discriminator sets, so neither side can
    /// gain or lose an arm without the other.
    /// </summary>
    /// <remarks>
    /// The pairing matrix above proves each pair agrees; this proves the pairing is TOTAL. Without it a
    /// variant could be added to one side and simply left out of the matrix.
    /// </remarks>
    [Fact]
    public void TheDomainUnionAndTheGeneratedWireUnion_HaveIdenticalDiscriminatorSets()
    {
        int[] domain = [.. Enum.GetValues<ExpressionVariableKind>().Select(static kind => (int)kind).Order()];
        int[] wire = [.. Enum.GetValues<VarValue.KindOneofCase>().Select(static arm => (int)arm).Order()];

        Assert.Equal(wire, domain);
        Assert.Equal(8, wire.Length);
    }

    /// <summary>
    /// The generated wire union really is a ONEOF: exactly one arm is selected at a time, and setting a
    /// second clears the first.
    /// </summary>
    /// <remarks>
    /// This is what makes the domain union's closedness meaningful on the wire. A value can never carry two
    /// types at once, so a consumer branching on the discriminator can never be reading a payload that a
    /// different arm also set. Each arm is exercised with its canonical carrier - the three temporal arms
    /// reuse the shared single-string carriers from <c>common.v1</c> rather than redeclaring a format.
    /// </remarks>
    [Fact]
    public void TheGeneratedWireUnion_SelectsExactlyOneArmAtATime()
    {
        Assert.Equal(VarValue.KindOneofCase.None, new VarValue().KindCase);

        Assert.Equal(
            VarValue.KindOneofCase.TimeValue,
            new VarValue { TimeValue = new WireTimeValue { Value = "13:45:30" } }.KindCase);
        Assert.Equal(VarValue.KindOneofCase.StringValue, new VarValue { StringValue = "3" }.KindCase);
        Assert.Equal(VarValue.KindOneofCase.LongValue, new VarValue { LongValue = 3L }.KindCase);
        Assert.Equal(VarValue.KindOneofCase.DoubleValue, new VarValue { DoubleValue = 3d }.KindCase);
        Assert.Equal(
            VarValue.KindOneofCase.DatetimeValue,
            new VarValue { DatetimeValue = new WireDateTimeValue { Value = "2026-01-01 13:45:30" } }.KindCase);
        Assert.Equal(
            VarValue.KindOneofCase.DateValue,
            new VarValue { DateValue = new WireDateValue { Value = "2026-01-01" } }.KindCase);
        Assert.Equal(VarValue.KindOneofCase.BoolValue, new VarValue { BoolValue = true }.KindCase);

        VarValue overwritten = new() { LongValue = 3L };
        overwritten.StringValue = "3";

        Assert.Equal(VarValue.KindOneofCase.StringValue, overwritten.KindCase);
        Assert.False(overwritten.HasLongValue);
    }

    /// <summary>
    /// Six of the seven domain names equal their wire arm name ignoring case; the seventh is the boolean
    /// arm, where THE LEGACY SPELLING DELIBERATELY WINS.
    /// </summary>
    /// <remarks>
    /// The contract spells the arm <c>bool_value</c> while the PowerBuilder keyword is <c>boolean</c>
    /// [:L159], so the domain member reads <c>BooleanValue</c> and the generated arm reads
    /// <c>BoolValue</c>. That is the ONLY divergence, it is naming only, and the pairing above already
    /// pins the two together by value - this test exists so the divergence is recorded as intentional
    /// rather than rediscovered as a suspected bug.
    /// </remarks>
    [Fact]
    public void OnlyTheBooleanArmSpellsItsNameDifferently_AndThatIsDeliberate()
    {
        List<ExpressionVariableKind> namesDiffer = [];

        foreach (ExpressionVariableKind kind in SevenVariants())
        {
            string wireName = ((VarValue.KindOneofCase)(int)kind).ToString();
            if (!string.Equals(kind.ToString(), wireName, StringComparison.OrdinalIgnoreCase))
            {
                namesDiffer.Add(kind);
            }
        }

        ExpressionVariableKind onlyDivergence = Assert.Single(namesDiffer);
        Assert.Equal(ExpressionVariableKind.BooleanValue, onlyDivergence);
        Assert.Equal("BooleanValue", onlyDivergence.ToString());
        Assert.Equal("BoolValue", VarValue.KindOneofCase.BoolValue.ToString());
    }

    // ==============================================================================================
    //  2. THE TWO VARIABLE-TYPE CONSTANTS AND THE FIVE STRUCTURE PORTS
    //  --------------------------------------------------------------------------------------------
    //  THE FIELD COUNTS ARE THE AUDIT. Each port must carry EXACTLY the fields its legacy structure
    //  declares - no convenience field added, none dropped - because the counts are what let a reviewer
    //  diff the port against the .sru field by field:
    //
    //      vardata         [:L50-L56]   5 fields   ->  VariableReference
    //      funcdata        [:L58-L63]   4 fields   ->  FunctionReference
    //      globalvardata   [:L65-L71]   5 fields   ->  GlobalVariable
    //      localvardata    [:L73-L78]   4 fields   ->  LocalVariableDefinition   (RECURSIVE)
    //      foreignvardata  [:L80-L83]   2 fields   ->  ForeignVariableReference
    // ==============================================================================================

    /// <summary>
    /// The public instance property names a type declares, sorted, so a census can be asserted as a set
    /// without depending on reflection's ordering.
    /// </summary>
    /// <param name="port">The port type.</param>
    /// <returns>The sorted names.</returns>
    private static string[] PortFieldNames(Type port) =>
        [.. port.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(static p => p.Name).Order(StringComparer.Ordinal)];

    /// <summary>
    /// The declared type of one of a port's public instance properties.
    /// </summary>
    /// <param name="port">The port type.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The declared type.</returns>
    private static Type PortFieldType(Type port, string name)
    {
        PropertyInfo? property = port.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);
        return property.PropertyType;
    }

    /// <summary>
    /// The oracle's own chain of expression variables, three levels deep, expressed the only way this model
    /// expresses recursion: by ONE-BASED INDEX from a reference into the table.
    /// </summary>
    /// <returns>
    /// A four-entry table - 本月读数 and 精度 are leaves, num2 references both of them, and exp1 references
    /// num2 and 精度.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE SIGILS ARE THE DYNAMIC FORM ON PURPOSE. A static reference is substituted into the expression
    /// text at bind time and is GONE from the stored expression [:L1435], so it leaves no entry in
    /// <c>vars[]</c> to walk; a dynamic reference is retained as a reference and resolved at calculation
    /// time [:L1406, :L2189-L2195], which is what makes the chain observable at all. The oracle uses the
    /// dynamic form for exactly this purpose at [w_test_dwsvc_columnexp.srw:L154] and
    /// [docs/n_cst_dwsvc_columnexp.md:L68].
    /// </para>
    /// <para>
    /// The definitions themselves are the scenario's: 本月读数 is the expression "5" [w_test:L108], 精度 is
    /// the typed value 0 [w_test:L109] rendered into its expression text, and num2 wraps both in the
    /// application's own <c>FormatPrice</c> macro [w_test:L112].
    /// </para>
    /// </remarks>
    private static ExpressionVariableEnvironment ScenarioChain()
    {
        GlobalVariable thisMonth = new()
        {
            Name = ThisMonth,
            Local = new LocalVariableDefinition { Exp = "5" },
        };

        GlobalVariable precision = new()
        {
            Name = Precision,
            Local = new LocalVariableDefinition { Exp = "0" },
        };

        GlobalVariable num2 = new()
        {
            Name = Num2,
            Local = new LocalVariableDefinition
            {
                Exp = "$" + FormatPrice + "($$" + ThisMonth + ",$$" + Precision + ")",
                Vars =
                [
                    new VariableReference(ThisMonth, "$$" + ThisMonth, 1, IsCtx: false, IsMacro: true),
                    new VariableReference(Precision, "$$" + Precision, 2, IsCtx: false, IsMacro: true),
                ],
                Fns =
                [
                    new FunctionReference
                    {
                        Name = FormatPrice,
                        FullName = "$" + FormatPrice + "($$" + ThisMonth + ",$$" + Precision + ")",
                        Args = ["$$" + ThisMonth, "$$" + Precision],
                        Builtin = false,
                    },
                ],
                HasMacro = true,
            },
        };

        GlobalVariable exp1 = new()
        {
            Name = Exp1,
            Local = new LocalVariableDefinition
            {
                Exp = "$" + FormatPrice + "($$" + Num2 + ",$$" + Precision + ")",
                Vars =
                [
                    new VariableReference(Num2, "$$" + Num2, 3, IsCtx: false, IsMacro: true),
                    new VariableReference(Precision, "$$" + Precision, 2, IsCtx: false, IsMacro: true),
                ],
                Fns =
                [
                    new FunctionReference
                    {
                        Name = FormatPrice,
                        FullName = "$" + FormatPrice + "($$" + Num2 + ",$$" + Precision + ")",
                        Args = ["$$" + Num2, "$$" + Precision],
                        Builtin = false,
                    },
                ],
                HasMacro = true,
            },
        };

        return ExpressionVariableEnvironment.Create([thisMonth, precision, num2, exp1]);
    }

    /// <summary>
    /// <c>VAR_LOCAL</c> is 0 and <c>VAR_FOREIGN</c> is 1, both are <c>long</c>, and both keep their legacy
    /// SCREAMING_SNAKE spelling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The oracle declares them as <c>constant long VAR_LOCAL = 0</c> and <c>constant long VAR_FOREIGN =
    /// 1</c> [:L117-L118], immediately after the column calc-flag block. The SPELLING is asserted as well
    /// as the value because the discriminator travels in the serialized C-04 payload, in log records and in
    /// the characterization recordings that are this migration's parity evidence - a rename would silently
    /// invalidate every stored comparison that mentions it.
    /// </para>
    /// <para>
    /// This test REFERENCES those names; it declares no constant of its own, so no analyzer suppression is
    /// needed for this file.
    /// </para>
    /// </remarks>
    [Fact]
    public void VarTypeConstants_PreserveTheirLegacyValuesAndSpelling()
    {
        Assert.Equal(0L, ExpressionVariableEnvironment.VAR_LOCAL);
        Assert.Equal(1L, ExpressionVariableEnvironment.VAR_FOREIGN);

        FieldInfo[] constants =
        [
            .. typeof(ExpressionVariableEnvironment)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(static field => field.IsLiteral),
        ];

        Assert.Equal(2, constants.Length);
        Assert.Equal(
            ["VAR_FOREIGN", "VAR_LOCAL"],
            constants.Select(static field => field.Name).Order(StringComparer.Ordinal));
        Assert.All(constants, static field => Assert.Equal(typeof(long), field.FieldType));

        // VAR_LOCAL is also the DEFAULT, and that is faithful rather than chosen: PowerScript
        // zero-initialises a structure, so a globalvardata nobody assigned a vartype to is already local.
        Assert.Equal(ExpressionVariableEnvironment.VAR_LOCAL, new GlobalVariable().VarType);
    }

    /// <summary>
    /// The two constants agree with the GENERATED wire enum by value, and the wire enum has exactly those
    /// two members.
    /// </summary>
    /// <param name="wireVarType">The generated wire member.</param>
    /// <param name="expected">The domain constant it must equal.</param>
    /// <param name="locator">The oracle line the constant was read off.</param>
    [Theory]
    [MemberData(nameof(VarTypeAgreement))]
    public void VarTypeConstants_AgreeWithTheGeneratedWireEnum(
        GlobalVarData.Types.VarType wireVarType,
        long expected,
        string locator)
    {
        Assert.StartsWith(OraclePath + ":L", locator, StringComparison.Ordinal);
        Assert.Equal(expected, (long)wireVarType);
    }

    /// <summary>
    /// The pairing of the generated wire variable-type members with the preserved domain constants.
    /// </summary>
    /// <remarks>
    /// The wire members are referenced, not retyped (C-A); the expected value comes from the domain
    /// constant, not from a literal, so neither side of the pairing is written out twice.
    /// </remarks>
    public static TheoryData<GlobalVarData.Types.VarType, long, string> VarTypeAgreement() => new()
    {
        {
            GlobalVarData.Types.VarType.VarLocal,
            ExpressionVariableEnvironment.VAR_LOCAL,
            OraclePath + ":L117"
        },
        {
            GlobalVarData.Types.VarType.VarForeign,
            ExpressionVariableEnvironment.VAR_FOREIGN,
            OraclePath + ":L118"
        },
    };

    /// <summary>
    /// The generated wire variable-type enum declares exactly the two the oracle does, so the discriminator
    /// cannot acquire a third state on the wire.
    /// </summary>
    [Fact]
    public void TheGeneratedWireVarTypeEnum_DeclaresExactlyTheTwoLegacyValues()
    {
        long[] wire =
        [
            .. Enum.GetValues<GlobalVarData.Types.VarType>().Select(static value => (long)value).Order(),
        ];

        Assert.Equal(
            [ExpressionVariableEnvironment.VAR_LOCAL, ExpressionVariableEnvironment.VAR_FOREIGN],
            wire);
    }

    /// <summary>
    /// The <c>vardata</c> port carries exactly the five legacy fields, in the legacy's own declaration
    /// order, with the legacy's own types.
    /// </summary>
    /// <remarks>
    /// The order is asserted through the PRIMARY CONSTRUCTOR's parameter list rather than through
    /// reflection over properties, because the constructor parameter list is the thing a reviewer diffs
    /// against [:L50-L56] field by field. <c>index</c> is declared <c>integer</c> - PowerBuilder's 16-bit
    /// type - and is widened to <see cref="int"/> to match the contract's <c>int32</c>, a widening that
    /// cannot lose a value.
    /// </remarks>
    [Fact]
    public void VardataPort_CarriesExactlyTheFiveLegacyFieldsInOrder()
    {
        Assert.Equal(
            ["FullName", "Index", "IsCtx", "IsMacro", "Name"],
            PortFieldNames(typeof(VariableReference)));

        ConstructorInfo primary = Assert.Single(typeof(VariableReference).GetConstructors());
        ParameterInfo[] parameters = primary.GetParameters();

        Assert.Equal(5, parameters.Length);
        Assert.Equal(
            ["Name", "FullName", "Index", "IsCtx", "IsMacro"],
            parameters.Select(static parameter => parameter.Name));
        Assert.Equal(
            [typeof(string), typeof(string), typeof(int), typeof(bool), typeof(bool)],
            parameters.Select(static parameter => parameter.ParameterType));

        // Every parameter defaults, which reproduces PowerScript's zeroed structure declaration exactly:
        // `VARDATA varData` starts with empty strings, zero and false, and the parser then fills selected
        // members in [:L1407-L1414].
        Assert.All(parameters, static parameter => Assert.True(parameter.HasDefaultValue));

        VariableReference zeroed = new();
        Assert.Equal(string.Empty, zeroed.Name);
        Assert.Equal(string.Empty, zeroed.FullName);
        Assert.Equal(0, zeroed.Index);
        Assert.False(zeroed.IsCtx);
        Assert.False(zeroed.IsMacro);
    }

    /// <summary>
    /// <c>isctx</c> and <c>ismacro</c> are INDEPENDENT bits: all four combinations are representable and
    /// mutually distinguishable.
    /// </summary>
    /// <remarks>
    /// They are two of the three orthogonal bits the parser sets - <c>varData.isCtx = bIsCtx</c> [:L1413]
    /// and <c>varData.isMacro = bIsMacro</c> [:L1414] - and they mean different things: one selects the
    /// RESOLUTION SCOPE and the other says whether the reference is a macro at all. Folding either into the
    /// other, or into a single enum, would lose a state the parser can produce.
    /// </remarks>
    [Fact]
    public void IsCtxAndIsMacro_AreIndependentBits()
    {
        VariableReference[] combinations =
        [
            new(Precision, Precision, 1, IsCtx: false, IsMacro: false),
            new(Precision, "$" + Precision, 1, IsCtx: false, IsMacro: true),
            new(Precision, "@" + Precision, 1, IsCtx: true, IsMacro: false),
            new(Precision, "@$" + Precision, 1, IsCtx: true, IsMacro: true),
        ];

        Assert.Equal(4, new HashSet<VariableReference>(combinations).Count);
        Assert.Equal(4, new HashSet<(bool, bool)>(
            combinations.Select(static reference => (reference.IsCtx, reference.IsMacro))).Count);

        // Neither bit implies the other in either direction.
        Assert.Contains(combinations, static reference => reference.IsCtx && !reference.IsMacro);
        Assert.Contains(combinations, static reference => !reference.IsCtx && reference.IsMacro);
    }

    /// <summary>
    /// The <c>globalvardata</c> port carries exactly the five legacy fields, and <c>links[]</c> is a
    /// collection whose element type is the FOREIGN descriptor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>links[]</c> [:L70] is an array of <c>foreignvardata</c>, so every element of it carries the same
    /// unserializable-pointer narrowing as the singular <c>foreign</c> member. That it is a SECOND
    /// pointer-bearing field is easy to miss, which is why its element type is asserted here rather than
    /// assumed.
    /// </para>
    /// <para>
    /// BOTH PAYLOAD MEMBERS ARE ALWAYS PRESENT, and that is faithful: in PowerScript a nested structure
    /// member is not a reference that can be absent, so a <c>globalvardata</c> physically contains a zeroed
    /// <c>localvardata</c> and a zeroed <c>foreignvardata</c> at all times and <c>vartype</c> is the only
    /// thing that says which to read [:L2199 against :L2202-L2206]. Modelling the unused member as null
    /// would invent a state the oracle has no representation for.
    /// </para>
    /// </remarks>
    [Fact]
    public void GlobalvardataPort_CarriesExactlyTheFiveLegacyFields()
    {
        Assert.Equal(
            ["Foreign", "Links", "Local", "Name", "VarType"],
            PortFieldNames(typeof(GlobalVariable)));

        Assert.Equal(typeof(string), PortFieldType(typeof(GlobalVariable), nameof(GlobalVariable.Name)));
        Assert.Equal(typeof(long), PortFieldType(typeof(GlobalVariable), nameof(GlobalVariable.VarType)));
        Assert.Equal(
            typeof(LocalVariableDefinition),
            PortFieldType(typeof(GlobalVariable), nameof(GlobalVariable.Local)));
        Assert.Equal(
            typeof(ForeignVariableReference),
            PortFieldType(typeof(GlobalVariable), nameof(GlobalVariable.Foreign)));

        Type links = PortFieldType(typeof(GlobalVariable), nameof(GlobalVariable.Links));
        Assert.True(typeof(IEnumerable<ForeignVariableReference>).IsAssignableFrom(links));
        Assert.Equal(typeof(ForeignVariableReference), Assert.Single(links.GetGenericArguments()));

        // A PowerBuilder array is never null - an unassigned one simply has an UpperBound of 0 - so the
        // zeroed structure has an EMPTY links collection rather than a missing one, and both payload
        // members are already present.
        GlobalVariable zeroed = new();
        Assert.Empty(zeroed.Links);
        Assert.NotNull(zeroed.Local);
        Assert.NotNull(zeroed.Foreign);
        Assert.Equal(string.Empty, zeroed.Name);
    }

    /// <summary>
    /// The <c>localvardata</c> port carries exactly the four legacy fields and NO typed-value field.
    /// </summary>
    /// <remarks>
    /// The absent typed value is the interesting half. A value variable's value lives INSIDE
    /// <see cref="LocalVariableDefinition.Exp"/> as a rendered literal, because every typed
    /// <c>of_addvar</c> overload renders and delegates to <c>of_addvarexp</c> [:L1091-L1109]; the typed
    /// <see cref="ExpressionVariableValue"/> therefore travels BESIDE this record rather than inside it,
    /// which keeps the field count legacy-exact while still letting the type survive the boundary.
    /// <see cref="LocalVariableDefinition.HasMacro"/> is likewise carried and never derived, because the
    /// oracle computes it once inside <c>of_addvarexp</c> [:L1623] and deriving it here would disagree with
    /// any recorded value.
    /// </remarks>
    [Fact]
    public void LocalvardataPort_CarriesExactlyTheFourLegacyFieldsAndNoTypedValue()
    {
        Assert.Equal(
            ["Exp", "Fns", "HasMacro", "Vars"],
            PortFieldNames(typeof(LocalVariableDefinition)));

        Assert.Equal(
            typeof(string),
            PortFieldType(typeof(LocalVariableDefinition), nameof(LocalVariableDefinition.Exp)));
        Assert.Equal(
            typeof(bool),
            PortFieldType(typeof(LocalVariableDefinition), nameof(LocalVariableDefinition.HasMacro)));

        Type vars = PortFieldType(typeof(LocalVariableDefinition), nameof(LocalVariableDefinition.Vars));
        Type fns = PortFieldType(typeof(LocalVariableDefinition), nameof(LocalVariableDefinition.Fns));
        Assert.Equal(typeof(VariableReference), Assert.Single(vars.GetGenericArguments()));
        Assert.Equal(typeof(FunctionReference), Assert.Single(fns.GetGenericArguments()));

        // No typed value anywhere on the port - it is carried alongside, per the contract's wire-only
        // fifth field.
        Assert.DoesNotContain(typeof(ExpressionVariableValue), DeclaredMemberTypes(typeof(LocalVariableDefinition)));

        LocalVariableDefinition zeroed = new();
        Assert.Equal(string.Empty, zeroed.Exp);
        Assert.Empty(zeroed.Vars);
        Assert.Empty(zeroed.Fns);
        Assert.False(zeroed.HasMacro);
    }

    /// <summary>
    /// The <c>funcdata</c> port carries exactly the four legacy fields, and <c>builtin</c> keeps its
    /// MISLEADING legacy name and meaning unchanged.
    /// </summary>
    /// <remarks>
    /// <c>builtin</c> does not mean "a DataWindow built-in function". It is assigned straight from the
    /// doubled-sigil test - <c>fnData.builtIn = bDD</c> [:L1311] - so it means "WRITTEN WITH <c>$$</c>", the
    /// dynamic macro form, and it is what routes dispatch to the <c>Invoke</c> sentinel branch instead of
    /// the direct branch. Preserved verbatim (C-B): renaming it would break every recording that carries it.
    /// </remarks>
    [Fact]
    public void FuncdataPort_CarriesExactlyTheFourLegacyFields()
    {
        Assert.Equal(
            ["Args", "Builtin", "FullName", "Name"],
            PortFieldNames(typeof(FunctionReference)));

        Assert.Equal(typeof(string), PortFieldType(typeof(FunctionReference), nameof(FunctionReference.Name)));
        Assert.Equal(
            typeof(string),
            PortFieldType(typeof(FunctionReference), nameof(FunctionReference.FullName)));
        Assert.Equal(
            typeof(bool),
            PortFieldType(typeof(FunctionReference), nameof(FunctionReference.Builtin)));

        Type args = PortFieldType(typeof(FunctionReference), nameof(FunctionReference.Args));
        Assert.Equal(typeof(string), Assert.Single(args.GetGenericArguments()));

        // The arguments are EXPRESSIONS, not values, and they compare element by element - which is what
        // makes two structurally identical references equal despite ImmutableArray's reference equality.
        FunctionReference left = new() { Name = FormatPrice, Args = ["$$" + Num2, "$$" + Precision] };
        FunctionReference right = new() { Name = FormatPrice, Args = ["$$" + Num2, "$$" + Precision] };
        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());

        FunctionReference zeroed = new();
        Assert.Equal(string.Empty, zeroed.Name);
        Assert.Empty(zeroed.Args);
        Assert.False(zeroed.Builtin);
    }

    /// <summary>
    /// The <c>foreignvardata</c> port carries exactly two fields, and the second is an OPAQUE STRING HANDLE
    /// where the oracle held a live object pointer.
    /// </summary>
    /// <remarks>
    /// The legacy field is declared <c>n_cst_dwsvc_columnexp expsvc</c> [:L82] - a pointer to another live
    /// instance of the service class - and it is dereferenced as one, synchronously, into a private method
    /// of the other instance at two independent sites [:L2199-L2200 and :L2384-L2385]. A pointer cannot be
    /// serialized, so the port carries a session-scoped handle VALUE instead: a string and nothing more,
    /// never a network address, never a URL and never a process identifier. Whether a handle is currently
    /// resolvable is SESSION STATE and deliberately not duplicated here, which is also what keeps the field
    /// count at the legacy's two.
    /// </remarks>
    [Fact]
    public void ForeignvardataPort_CarriesTwoFieldsAndReplacesThePointerWithAnOpaqueHandle()
    {
        Assert.Equal(
            ["Handle", "Index"],
            PortFieldNames(typeof(ForeignVariableReference)));

        ConstructorInfo primary = Assert.Single(typeof(ForeignVariableReference).GetConstructors());
        ParameterInfo[] parameters = primary.GetParameters();

        Assert.Equal(2, parameters.Length);
        Assert.Equal(["Index", "Handle"], parameters.Select(static parameter => parameter.Name));
        Assert.Equal(
            [typeof(int), typeof(string)],
            parameters.Select(static parameter => parameter.ParameterType));

        ForeignVariableReference zeroed = new();
        Assert.Equal(0, zeroed.Index);
        Assert.Equal(string.Empty, zeroed.Handle);

        // The narrowing applies to `links[]` too, because every element is this same type.
        GlobalVariable summary = new()
        {
            Name = Summary,
            VarType = ExpressionVariableEnvironment.VAR_FOREIGN,
            Foreign = new ForeignVariableReference(1, "dw_2"),
            Links = [new ForeignVariableReference(1, "dw_1")],
        };

        Assert.Equal("dw_2", summary.Foreign.Handle);
        Assert.Equal("dw_1", Assert.Single(summary.Links).Handle);
    }

    /// <summary>
    /// A variable's value may itself be an expression with its own references: a TWO-LEVEL chain resolves in
    /// exactly two hops and round-trips through structural equality.
    /// </summary>
    /// <remarks>
    /// The oracle walks the chain through <c>GlobalVars[nVarIdx].local.vars[nGVarIdx]</c> during static
    /// expansion [:L1439-L1448] and again through <c>GlobalVars[nGVarIdx].local</c> during preprocessing
    /// [:L2202-L2206], so the nesting is real rather than defensive. num2's definition [w_test:L112]
    /// references 本月读数, whose own definition [w_test:L108] is a leaf.
    /// </remarks>
    [Fact]
    public void LocalDefinition_NestsToTwoLevelsAndRoundTrips()
    {
        ExpressionVariableEnvironment environment = ScenarioChain();

        (ImmutableArray<string> visited, bool completed) = WalkVariableChain(environment, 3, budget: 8);

        Assert.True(completed);
        Assert.Equal([Num2, ThisMonth], visited);

        // The intermediate level really does carry its own references and its own macro flag.
        GlobalVariable middle = environment[3];
        Assert.Equal(Num2, middle.Name);
        Assert.True(middle.Local.HasMacro);
        Assert.Equal(2, middle.Local.Vars.Length);
        Assert.Single(middle.Local.Fns);

        // The leaf carries neither, which is what terminates the walk.
        GlobalVariable leaf = environment[1];
        Assert.Equal(ThisMonth, leaf.Name);
        Assert.False(leaf.Local.HasMacro);
        Assert.Empty(leaf.Local.Vars);
        Assert.Equal("5", leaf.Local.Exp);

        // An independently constructed twin is equal, nesting included, so the whole chain survives a
        // characterization round trip rather than only its top level.
        Assert.Equal(ScenarioChain(), environment);
        Assert.Equal(ScenarioChain().GetHashCode(), environment.GetHashCode());
    }

    /// <summary>
    /// The same chain at THREE LEVELS, so the model is proven to nest beyond the one level a flat design
    /// could accidentally satisfy.
    /// </summary>
    /// <remarks>
    /// exp1 [w_test:L113] references num2 [w_test:L112], which references 本月读数 [w_test:L108]. A flat
    /// model would truncate at the first hop and still pass the two-level test, which is why both depths are
    /// asserted.
    /// </remarks>
    [Fact]
    public void LocalDefinition_NestsToThreeLevelsAndRoundTrips()
    {
        ExpressionVariableEnvironment environment = ScenarioChain();

        (ImmutableArray<string> visited, bool completed) = WalkVariableChain(environment, 4, budget: 8);

        Assert.True(completed);
        Assert.Equal([Exp1, Num2, ThisMonth], visited);

        // Each hop is a real index into the table, and each index addresses the entry the name says it does.
        Assert.Equal(3, environment[4].Local.Vars[0].Index);
        Assert.Equal(Num2, environment[environment[4].Local.Vars[0].Index].Name);
        Assert.Equal(1, environment[3].Local.Vars[0].Index);
        Assert.Equal(ThisMonth, environment[environment[3].Local.Vars[0].Index].Name);

        Assert.Equal(ScenarioChain(), environment);
    }

    /// <summary>
    /// RECURSION IS EXPRESSED BY INDEX, NOT BY OBJECT NESTING, so a chain of any depth - a cycle included -
    /// is representable and no structural operation on the model can recurse.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the structural reason the "unbounded depth" of <c>localvardata</c> costs nothing. A reference
    /// carries an <see cref="int"/> index [:L53], never a nested definition, so the object graph is a tree
    /// of scalars and equality, hashing and lookup are all O(n) walks over a flat array. A model that nested
    /// the definition itself would make a self-referential variable an infinite object graph, and equality
    /// on it would overflow the stack instead of answering.
    /// </para>
    /// <para>
    /// A CONSUMER'S WALK IS THEREFORE BOUNDED BY A DEFINED OUTCOME. The model hands out
    /// <see cref="ExpressionVariableEnvironment.IsValidIndex(int)"/> and the zero-means-unbound convention
    /// [:L2189-L2195] precisely so a walk can stop rather than run away, and the helper this suite uses stops
    /// on a repeat, on an unbound index and on its own budget - reporting which, never faulting. The engine's
    /// own guard is the same shape: it pushes the column onto the calculation stack before anything runs and
    /// refuses an expression that writes back to it [:L682-L690].
    /// </para>
    /// </remarks>
    [Fact]
    public void RecursionIsByIndexSoNoStructuralOperationCanRecurse()
    {
        // A variable whose only reference points at ITSELF - representable, because the reference is an
        // integer rather than an object.
        GlobalVariable selfReferential = new()
        {
            Name = Num1,
            Local = new LocalVariableDefinition
            {
                Exp = "$$" + Num1 + " + 1",
                Vars = [new VariableReference(Num1, "$$" + Num1, 1, IsCtx: false, IsMacro: true)],
                HasMacro = true,
            },
        };

        // And a two-entry cycle, so the guard is not merely a self-reference special case.
        GlobalVariable first = new()
        {
            Name = Exp1,
            Local = new LocalVariableDefinition
            {
                Exp = "$$" + Exp2,
                Vars = [new VariableReference(Exp2, "$$" + Exp2, 3, IsCtx: false, IsMacro: true)],
                HasMacro = true,
            },
        };

        GlobalVariable second = new()
        {
            Name = Exp2,
            Local = new LocalVariableDefinition
            {
                Exp = "$$" + Exp1,
                Vars = [new VariableReference(Exp1, "$$" + Exp1, 2, IsCtx: false, IsMacro: true)],
                HasMacro = true,
            },
        };

        ExpressionVariableEnvironment cyclic =
            ExpressionVariableEnvironment.Create([selfReferential, first, second]);

        // No structural operation recurses: reaching these assertions at all is the proof, because a
        // recursive model would have faulted before returning.
        Assert.Equal(3, cyclic.Count);
        Assert.Equal(cyclic, ExpressionVariableEnvironment.Create([selfReferential, first, second]));
        Assert.Equal(
            ExpressionVariableEnvironment.Create([selfReferential, first, second]).GetHashCode(),
            cyclic.GetHashCode());
        Assert.Equal(1, cyclic.IndexOf(Num1));
        Assert.Equal(Exp2, cyclic[3].Name);

        // The bounded walk reports NOT COMPLETED rather than running away, on both shapes.
        (ImmutableArray<string> selfVisited, bool selfCompleted) = WalkVariableChain(cyclic, 1, budget: 8);
        Assert.False(selfCompleted);
        Assert.Equal([Num1], selfVisited);

        (ImmutableArray<string> cycleVisited, bool cycleCompleted) = WalkVariableChain(cyclic, 2, budget: 8);
        Assert.False(cycleCompleted);
        Assert.Equal([Exp1, Exp2], cycleVisited);

        // The structural reason, asserted directly: no member of a definition or a reference is a definition
        // or an entry, so there is no object nesting to recurse through in the first place.
        ImmutableArray<Type> definitionMembers = DeclaredMemberTypes(typeof(LocalVariableDefinition));
        Assert.DoesNotContain(typeof(LocalVariableDefinition), definitionMembers);
        Assert.DoesNotContain(typeof(GlobalVariable), definitionMembers);
        Assert.DoesNotContain(typeof(LocalVariableDefinition), DeclaredMemberTypes(typeof(VariableReference)));

        // And an unbound reference answers with the legacy's own "not found" value rather than a default
        // entry, so a walk that reaches one stops instead of silently reading entry one.
        Assert.Equal(0, cyclic.IndexOf("没有这个变量"));
        Assert.False(cyclic.IsValidIndex(0));
    }

    /// <summary>
    /// The sigil tokens the parser matches, each with the bare name it strips out of them.
    /// </summary>
    /// <remarks>
    /// <c>fullname</c> is <c>Mid(sExp, nMacPos, nPos - nMacPos)</c> right-trimmed [:L1412] and <c>name</c> is
    /// taken from offset +2 past a doubled sigil [:L1407] or +1 past a single one [:L1409], so the two differ
    /// by exactly the sigil - and the pair is what the error messages disagree about, one site naming
    /// <c>fullName</c> [:L1390] and the next naming <c>name</c> [:L1395].
    /// </remarks>
    public static TheoryData<string, string, string> SigilTokens() => new()
    {
        { "$" + Precision, Precision, OraclePath + ":L1409" },
        { "$$" + Precision, Precision, OraclePath + ":L1407" },
        { "@" + Precision, Precision, OraclePath + ":L1296" },
        { "@$" + Precision, Precision, OraclePath + ":L1297" },
        { "@$$" + Precision, Precision, OraclePath + ":L1299" },
        { "$$" + Summary, Summary, OraclePath + ":L1407" },
        { "$$" + LastMonth, LastMonth, OraclePath + ":L1407" },
    };

    /// <summary>
    /// <c>fullname</c> and <c>name</c> are DISTINCT FIELDS WITH DISTINCT VALUES for a macro reference, and
    /// <c>fullname</c> keeps the sigil verbatim.
    /// </summary>
    /// <param name="fullName">The full matched token, sigils included.</param>
    /// <param name="name">The bare name the parser strips out of it.</param>
    /// <param name="locator">The oracle line that produces this pairing.</param>
    /// <remarks>
    /// <c>fullname</c> IS LOAD BEARING AND MUST NOT BE "TIDIED": it is the REPLACEMENT KEY. Static expansion
    /// substitutes a reference by replacing this exact string out of the expression [:L1435] and
    /// preprocessing does the same at calculation time [:L2214], both through the whole-token form of the
    /// framework's <c>ReplaceAll</c>. A consumer that regenerated it from <paramref name="name"/> plus a
    /// sigil would break substitution for any token the parser trimmed differently.
    /// </remarks>
    [Theory]
    [MemberData(nameof(SigilTokens))]
    public void FullNameAndName_AreDistinctFieldsWithDistinctValues(
        string fullName,
        string name,
        string locator)
    {
        Assert.StartsWith(OraclePath + ":L", locator, StringComparison.Ordinal);

        VariableReference reference = new(name, fullName, 1, IsCtx: fullName[0] == '@', IsMacro: fullName.Contains('$', StringComparison.Ordinal));

        Assert.NotEqual(reference.Name, reference.FullName);
        Assert.EndsWith(reference.Name, reference.FullName, StringComparison.Ordinal);
        Assert.Equal(fullName, reference.FullName);
        Assert.Equal(name, reference.Name);

        // Two references differing ONLY in FullName are unequal, which is what makes whole-structure
        // deduplication [:L1442] able to tell a static token from a dynamic one bearing the same name.
        VariableReference doubled = reference with { FullName = "$" + reference.FullName };
        Assert.NotEqual(reference, doubled);
        Assert.Equal(reference.Name, doubled.Name);

        // Two references differing only in Index are also unequal, because 0 and a resolved index are
        // different BINDING STATES rather than the same reference twice.
        Assert.NotEqual(reference, reference with { Index = 0 });
    }

    /// <summary>
    /// THE PARSER ITSELF records distinct <c>name</c> and <c>fullname</c> values, so the distinction is the
    /// oracle's and not this suite's construction.
    /// </summary>
    /// <remarks>
    /// The reference is written dynamically so that it is RETAINED rather than substituted away - a static
    /// reference is gone from the stored expression by the time anyone could inspect it [:L1435].
    /// </remarks>
    [Fact]
    public void TheParserRecordsDistinctNameAndFullName()
    {
        ColumnExpressionEngine engine = CreateEngine(out _);

        Assert.Equal(RetCode.OK, engine.AddVarExp(Precision, "0"));
        Assert.Equal(RetCode.OK, engine.AddVarExp(Num1, "$$" + Precision + " + 1"));

        ExpressionBindingSnapshot? binding = engine.GetVariableBinding(Num1);
        Assert.NotNull(binding);

        VariableReference recorded = Assert.Single(binding.Vars);
        Assert.Equal(Precision, recorded.Name);
        Assert.Equal("$$" + Precision, recorded.FullName);
        Assert.True(recorded.IsMacro);
        Assert.False(recorded.IsCtx);

        // The unexpanded source text is retained alongside, which is the first of the three things contract
        // C-04 requires the payload to carry.
        Assert.Equal("$$" + Precision + " + 1", binding.UnexpandedExp);
    }

    // ==============================================================================================
    //  3. THE PER-REFERENCE EXPANSION-MODE DESCRIPTOR
    //  --------------------------------------------------------------------------------------------
    //  THE PARSER SETS THREE ORTHOGONAL BITS, NOT ONE ENUM, and that is read directly off its two sigil
    //  arms [:L1284-L1300]:
    //
    //      case MACRO_FLAG "$"      bIsCtx = false; bIsMacro = true; bDD = next char is another "$"
    //      case MACRO_CONTEXT "@"   bIsCtx = true;  bIsMacro = next char is "$";
    //                               bDD = the char after THAT is "$"
    //
    //  The first two are copied onto the reference verbatim [:L1413, :L1414] while the third selects
    //  static against dynamic for a variable [:L1406, :L1415] and becomes funcdata.builtin for a
    //  function [:L1311].
    //
    //  THE "@" CONTEXT SIGIL IS DOCUMENTED NOWHERE - it is absent from the specification, which describes
    //  only "$" and "$$" - yet it is real, it is the mechanism behind vardata.isctx, and it resolves
    //  through the context triple at [:L2180-L2186]. It is a bit of its own and never folded into the
    //  mode, because it selects the RESOLUTION SCOPE while the mode selects the EXPANSION TIMING.
    // ==============================================================================================

    /// <summary>
    /// The descriptor carries THREE ORTHOGONAL BITS and all eight combinations are representable and
    /// mutually distinguishable.
    /// </summary>
    /// <remarks>
    /// Representable is not the same as reachable: two of the eight cannot be produced by the parser, and
    /// the matrix below says which and why. Both facts matter - the type must be able to hold whatever the
    /// parser hands it, and the projection must still answer for a pattern the parser cannot make, because
    /// a total function is easier to reason about than a partial one.
    /// </remarks>
    [Fact]
    public void SigilDescriptor_CarriesThreeOrthogonalBitsAndAllEightCombinationsAreRepresentable()
    {
        List<ExpansionSigils> all = [];

        foreach (bool isCtx in (bool[])[false, true])
        {
            foreach (bool isMacro in (bool[])[false, true])
            {
                foreach (bool isDynamic in (bool[])[false, true])
                {
                    ExpansionSigils sigils = new(isCtx, isMacro, isDynamic);

                    Assert.Equal(isCtx, sigils.IsCtx);
                    Assert.Equal(isMacro, sigils.IsMacro);
                    Assert.Equal(isDynamic, sigils.IsDynamic);

                    all.Add(sigils);
                }
            }
        }

        Assert.Equal(8, all.Count);
        Assert.Equal(8, new HashSet<ExpansionSigils>(all).Count);

        // The bare descriptor is default(T), which reproduces the parser's own starting point: no sigil
        // seen yet means no bit set.
        Assert.Equal(default, ExpansionSigils.Bare);
    }

    /// <summary>
    /// The named shorthands match the bit patterns their sigil spellings imply, so a test or a caller can
    /// name a form instead of restating three booleans.
    /// </summary>
    [Fact]
    public void TheNamedSigilShorthands_MatchTheirBitPatterns()
    {
        Assert.Equal(new ExpansionSigils(false, false, false), ExpansionSigils.Bare);
        Assert.Equal(new ExpansionSigils(false, true, false), ExpansionSigils.StaticMacro);
        Assert.Equal(new ExpansionSigils(false, true, true), ExpansionSigils.DynamicMacro);
        Assert.Equal(new ExpansionSigils(true, false, false), ExpansionSigils.Context);
        Assert.Equal(new ExpansionSigils(true, true, false), ExpansionSigils.ContextStaticMacro);
        Assert.Equal(new ExpansionSigils(true, true, true), ExpansionSigils.ContextDynamicMacro);

        // Six named forms and eight patterns: the two unnamed ones are exactly the two the parser cannot
        // produce, which is why they have no spelling to be named after.
        Assert.Equal(
            6,
            new HashSet<ExpansionSigils>(
            [
                ExpansionSigils.Bare,
                ExpansionSigils.StaticMacro,
                ExpansionSigils.DynamicMacro,
                ExpansionSigils.Context,
                ExpansionSigils.ContextStaticMacro,
                ExpansionSigils.ContextDynamicMacro,
            ]).Count);
    }

    /// <summary>
    /// All eight bit patterns with the mode each projects onto, the rejection it raises, whether the parser
    /// can produce it at all, and the oracle line that decides it.
    /// </summary>
    /// <remarks>
    /// THE TWO UNREACHABLE PATTERNS ARE PROVED UNREACHABLE FROM THE SOURCE, not assumed. A dynamic bit
    /// cannot occur without a macro bit: <c>bDD</c> is assigned in exactly two places, both inside a sigil
    /// arm. In the "$" arm <c>bIsMacro</c> is set unconditionally [:L1289], so (macro=false, dynamic=true)
    /// cannot arise there; in the "@" arm a false <c>bIsMacro</c> leaves the scan position un-advanced
    /// [:L1298], so the <c>bDD</c> test at [:L1299] re-reads the same character and <c>bDD</c> equals
    /// <c>bIsMacro</c>.
    /// </remarks>
    public static TheoryData<bool, bool, bool, ExpansionMode, ExpansionRejection, bool, string>
        VariableSigilProjections() => new()
    {
        // A bare reference - an ordinary column reference the parser never records as a macro. No macro
        // sigil means no expansion TIMING at all, so the mode is unspecified rather than static.
        { false, false, false, ExpansionMode.Unspecified, ExpansionRejection.None, true, OraclePath + ":L1258" },

        // `$name` - static expansion, substituted at bind time.
        { false, true, false, ExpansionMode.Static, ExpansionRejection.None, true, OraclePath + ":L1415" },

        // `$$name` - dynamic expansion, retained as a reference and resolved at calculation time.
        { false, true, true, ExpansionMode.Dynamic, ExpansionRejection.None, true, OraclePath + ":L1406" },

        // `@name` - a context COLUMN read. The sigil selects SCOPE, not timing, so the mode stays
        // unspecified and the scope travels on VariableReference.IsCtx.
        { true, false, false, ExpansionMode.Unspecified, ExpansionRejection.None, true, OraclePath + ":L2185" },

        // `@$name` - context plus STATIC, which the parser refuses outright. Reachable from the grammar and
        // rejected by the engine.
        { true, true, false, ExpansionMode.Unspecified, ExpansionRejection.ContextStaticExpansion, true, OraclePath + ":L1417" },

        // `@$$name` - context plus DYNAMIC, the only accepted context macro form.
        { true, true, true, ExpansionMode.Dynamic, ExpansionRejection.None, true, OraclePath + ":L2182" },

        // UNREACHABLE: the "$" arm sets bIsMacro unconditionally.
        { false, false, true, ExpansionMode.Unspecified, ExpansionRejection.None, false, OraclePath + ":L1289" },

        // UNREACHABLE: in the "@" arm a false bIsMacro leaves nPos un-advanced, so bDD == bIsMacro.
        { true, false, true, ExpansionMode.Unspecified, ExpansionRejection.None, false, OraclePath + ":L1298" },
    };

    /// <summary>
    /// The variable projection is TOTAL over all eight bit patterns, and each pattern maps to exactly one
    /// outcome - one mode, or one rejection.
    /// </summary>
    /// <param name="isCtx">The <c>@</c> bit.</param>
    /// <param name="isMacro">The <c>$</c> bit.</param>
    /// <param name="isDynamic">The doubled-sigil bit.</param>
    /// <param name="expectedMode">The wire mode this combination denotes.</param>
    /// <param name="expectedRejection">The rejection this combination raises, if any.</param>
    /// <param name="reachable">Whether the parser can produce this combination at all.</param>
    /// <param name="locator">The oracle line that decides the outcome.</param>
    [Theory]
    [MemberData(nameof(VariableSigilProjections))]
    public void VariableReferenceProjection_IsTotalOverAllEightBitPatterns(
        bool isCtx,
        bool isMacro,
        bool isDynamic,
        ExpansionMode expectedMode,
        ExpansionRejection expectedRejection,
        bool reachable,
        string locator)
    {
        Assert.StartsWith(OraclePath + ":L", locator, StringComparison.Ordinal);

        ExpansionSigils sigils = new(isCtx, isMacro, isDynamic);
        ExpansionModeProjection projection = sigils.ProjectVariableReference();

        Assert.Equal(expectedMode, projection.Mode);
        Assert.Equal(expectedRejection, projection.Rejection);
        Assert.Equal(expectedRejection != ExpansionRejection.None, projection.IsRejected);

        // A rejected combination carries NO mode, so a consumer cannot read a plausible mode off a refusal.
        if (projection.IsRejected)
        {
            Assert.Equal(ExpansionMode.Unspecified, projection.Mode);
        }

        // The two unreachable patterns are answered rather than thrown on, which is what makes the
        // projection total - and neither of them is ever reported as a real mode.
        if (!reachable)
        {
            Assert.Equal(ExpansionMode.Unspecified, projection.Mode);
            Assert.False(projection.IsRejected);
            Assert.True(isDynamic && !isMacro);
        }

        // The projection is a pure function of the three bits: calling it twice cannot differ.
        Assert.Equal(projection, sigils.ProjectVariableReference());
    }

    /// <summary>
    /// The three legacy function-dispatch arms with the wire mode each selects and the oracle line that
    /// dispatches it.
    /// </summary>
    /// <remarks>
    /// Telling the two function modes apart requires the function NAME, matched against the two reserved
    /// sentinels <c>FUNC_VAR</c> and <c>FUNC_INVOKE</c> [:L120-L121, checked at :L1386-L1401]. The sentinel
    /// VALUES belong to the engine, so the classification arrives here as a parameter instead of this file
    /// duplicating the two string literals - a second definition of the grammar could not be kept in
    /// agreement forever.
    /// </remarks>
    public static TheoryData<MacroFunctionDispatch, ExpansionMode, string> FunctionSigilProjections() => new()
    {
        // `$Fn(args)` - the APPLICATION owns the implementation and is called back across the boundary.
        { MacroFunctionDispatch.Application, ExpansionMode.MacroDirect, OraclePath + ":L2286" },

        // `$$('name')` - FUNC_VAR: the VARIABLE NAME is itself computed at run time.
        { MacroFunctionDispatch.VariableLookup, ExpansionMode.DynamicIndirect, OraclePath + ":L1388" },

        // `$$Invoke('Fn', args)` - FUNC_INVOKE: the FUNCTION NAME is itself a variable.
        { MacroFunctionDispatch.DynamicInvoke, ExpansionMode.MacroDynamic, OraclePath + ":L1393" },
    };

    /// <summary>
    /// Each legacy dispatch arm projects onto its own wire mode, and the three arms cover three distinct
    /// modes.
    /// </summary>
    /// <param name="dispatch">The dispatch arm the engine classified the call into.</param>
    /// <param name="expectedMode">The wire mode it must project onto.</param>
    /// <param name="locator">The oracle line that dispatches it.</param>
    [Theory]
    [MemberData(nameof(FunctionSigilProjections))]
    public void FunctionReferenceProjection_MapsEachDispatchArmToItsOwnWireMode(
        MacroFunctionDispatch dispatch,
        ExpansionMode expectedMode,
        string locator)
    {
        Assert.StartsWith(OraclePath + ":L", locator, StringComparison.Ordinal);

        // The doubled-sigil bit is NOT re-tested for a function reference: the oracle derives
        // funcdata.builtin FROM it [:L1311] and then dispatches on the resulting name classification, so
        // `dispatch` already carries that bit's consequence. Both spellings must therefore agree.
        ExpansionModeProjection fromStatic = ExpansionSigils.StaticMacro.ProjectFunctionReference(dispatch);
        ExpansionModeProjection fromDynamic = ExpansionSigils.DynamicMacro.ProjectFunctionReference(dispatch);

        Assert.Equal(expectedMode, fromStatic.Mode);
        Assert.Equal(expectedMode, fromDynamic.Mode);
        Assert.False(fromStatic.IsRejected);
        Assert.False(fromDynamic.IsRejected);
    }

    /// <summary>
    /// A CONTEXT-SCOPED FUNCTION MACRO IS REFUSED whatever the dispatch arm would have been, because the
    /// oracle refuses on meeting the opening bracket - before it has looked at the name.
    /// </summary>
    /// <remarks>
    /// The refusal is at [:L1306-L1310] and the engine reports it through the site whose enum value IS that
    /// locator. No function name rescues a context-scoped call, which is why the context check has to come
    /// first rather than being folded into the dispatch switch.
    /// </remarks>
    [Fact]
    public void AContextFunctionMacro_IsRefusedWhateverTheDispatchArmWouldHaveBeen()
    {
        foreach (MacroFunctionDispatch dispatch in Enum.GetValues<MacroFunctionDispatch>())
        {
            ExpansionModeProjection projection =
                ExpansionSigils.ContextDynamicMacro.ProjectFunctionReference(dispatch);

            Assert.True(projection.IsRejected);
            Assert.Equal(ExpansionRejection.ContextFunctionMacro, projection.Rejection);
            Assert.Equal(ExpansionMode.Unspecified, projection.Mode);

            Assert.Equal(
                ExpansionRejection.ContextFunctionMacro,
                ExpansionSigils.ContextStaticMacro.ProjectFunctionReference(dispatch).Rejection);
            Assert.Equal(
                ExpansionRejection.ContextFunctionMacro,
                ExpansionSigils.Context.ProjectFunctionReference(dispatch).Rejection);
        }

        // The site the engine reports it through carries its own locator as its VALUE, so the refusal and
        // the oracle line that produces it cannot drift apart.
        Assert.Equal(1308, (int)ExpressionErrorSite.FunctionMacroContextReferenceUnsupported);
    }

    /// <summary>
    /// An undeclared dispatch arm is REPORTED rather than guessed, because every remaining wire mode is
    /// already claimed and guessing would mislabel a reference on the wire.
    /// </summary>
    [Fact]
    public void AnUndeclaredDispatchArm_IsReportedRatherThanGuessed()
    {
        ArgumentOutOfRangeException failure = Assert.Throws<ArgumentOutOfRangeException>(
            () => { _ = ExpansionSigils.DynamicMacro.ProjectFunctionReference((MacroFunctionDispatch)99); });

        Assert.Equal("dispatch", failure.ParamName);

        // The three declared arms are exactly the three the oracle's dispatch switch has.
        Assert.Equal(3, Enum.GetValues<MacroFunctionDispatch>().Length);
        Assert.Equal(0, (int)MacroFunctionDispatch.Application);
        Assert.Equal(1, (int)MacroFunctionDispatch.VariableLookup);
        Assert.Equal(2, (int)MacroFunctionDispatch.DynamicInvoke);
    }

    /// <summary>
    /// The FIVE wire expansion modes, each with the bit combination and dispatch arm that produce it.
    /// </summary>
    /// <remarks>
    /// One row per real mode, so a mode that lost its producer - or gained a second one - shows up here
    /// rather than in a downstream integration failure. The rows name the generated
    /// <see cref="ExpansionMode"/> members directly (C-A).
    /// </remarks>
    public static TheoryData<ExpansionMode, bool, bool, bool, bool, MacroFunctionDispatch, string>
        FiveWireModes() => new()
    {
        // isCtx, isMacro, isDynamic, isFunctionReference, dispatch
        { ExpansionMode.Static, false, true, false, false, MacroFunctionDispatch.Application, OraclePath + ":L1415" },
        { ExpansionMode.Dynamic, false, true, true, false, MacroFunctionDispatch.Application, OraclePath + ":L1406" },
        { ExpansionMode.DynamicIndirect, false, true, true, true, MacroFunctionDispatch.VariableLookup, OraclePath + ":L1388" },
        { ExpansionMode.MacroDirect, false, true, false, true, MacroFunctionDispatch.Application, OraclePath + ":L2286" },
        { ExpansionMode.MacroDynamic, false, true, true, true, MacroFunctionDispatch.DynamicInvoke, OraclePath + ":L1393" },
    };

    /// <summary>
    /// Each of contract C-04's five expansion modes is produced by exactly one combination of the three bits
    /// and the dispatch classification.
    /// </summary>
    /// <param name="expectedMode">The wire mode.</param>
    /// <param name="isCtx">The <c>@</c> bit.</param>
    /// <param name="isMacro">The <c>$</c> bit.</param>
    /// <param name="isDynamic">The doubled-sigil bit.</param>
    /// <param name="isFunctionReference">Whether the reference is a function call rather than a variable.</param>
    /// <param name="dispatch">The dispatch arm, meaningful only for a function reference.</param>
    /// <param name="locator">The oracle line that produces the mode.</param>
    [Theory]
    [MemberData(nameof(FiveWireModes))]
    public void EachOfTheFiveWireModes_HasItsOwnProducingCombination(
        ExpansionMode expectedMode,
        bool isCtx,
        bool isMacro,
        bool isDynamic,
        bool isFunctionReference,
        MacroFunctionDispatch dispatch,
        string locator)
    {
        Assert.StartsWith(OraclePath + ":L", locator, StringComparison.Ordinal);

        ExpansionSigils sigils = new(isCtx, isMacro, isDynamic);

        ExpansionModeProjection projection = isFunctionReference
            ? sigils.ProjectFunctionReference(dispatch)
            : sigils.ProjectVariableReference();

        Assert.Equal(expectedMode, projection.Mode);
        Assert.False(projection.IsRejected);
        Assert.NotEqual(ExpansionMode.Unspecified, projection.Mode);
    }

    /// <summary>
    /// The generated <c>ExpansionMode</c> enum declares exactly five real modes plus the unspecified
    /// discriminator, and every one of the five has a producer in the matrix above.
    /// </summary>
    /// <remarks>
    /// The pairing matrix proves each mode has a producer; this proves the matrix is TOTAL over the wire
    /// enum, so a sixth mode added to the contract fails here rather than going unnoticed. The context sigil
    /// is deliberately NOT a sixth mode: it selects scope, not timing.
    /// </remarks>
    [Fact]
    public void TheGeneratedExpansionModeEnum_DeclaresFiveRealModesAndEveryOneHasAProducer()
    {
        ExpansionMode[] wire = [.. Enum.GetValues<ExpansionMode>()];

        Assert.Equal(6, wire.Length);
        Assert.Contains(ExpansionMode.Unspecified, wire);
        Assert.Equal(0, (int)ExpansionMode.Unspecified);

        HashSet<ExpansionMode> produced = [];
        foreach (ITheoryDataRow row in FiveWireModes())
        {
            produced.Add((ExpansionMode)row.GetData()[0]!);
        }

        Assert.Equal(
            wire.Where(static mode => mode != ExpansionMode.Unspecified).Order(),
            produced.Order());
    }

    /// <summary>
    /// THE MODE IS A PER-REFERENCE PROPERTY, NEVER A PER-EXPRESSION ONE: the specification's own mixed
    /// expression puts a dynamic reference and a static reference in ONE expression, and each carries its
    /// own mode.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The authoritative line is <c>of_AddvarExp("num1", "$$上月读数 + $本月读数")</c>
    /// [docs/n_cst_dwsvc_columnexp.md:L68], and it is the line the whole 5-against-6 worked example turns
    /// on. A model that tagged the EXPRESSION could not represent it at all.
    /// </para>
    /// <para>
    /// The doubled-sigil bit is derived from <see cref="VariableReference.FullName"/>, which is a second
    /// reason that field is load bearing: strip the sigil out of it and the two references become
    /// indistinguishable.
    /// </para>
    /// </remarks>
    [Fact]
    public void ExpansionModeIsAPerReferenceProperty_NotAPerExpressionOne()
    {
        // The two references the parser emits from that one expression.
        VariableReference dynamicReference =
            new(LastMonth, "$$" + LastMonth, 0, IsCtx: false, IsMacro: true);
        VariableReference staticReference =
            new(ThisMonth, "$" + ThisMonth, 0, IsCtx: false, IsMacro: true);

        ExpansionSigils dynamicSigils = ColumnExpressionEngine.SigilsFor(dynamicReference);
        ExpansionSigils staticSigils = ColumnExpressionEngine.SigilsFor(staticReference);

        Assert.True(dynamicSigils.IsDynamic);
        Assert.False(staticSigils.IsDynamic);

        Assert.Equal(ExpansionMode.Dynamic, dynamicSigils.ProjectVariableReference().Mode);
        Assert.Equal(ExpansionMode.Static, staticSigils.ProjectVariableReference().Mode);

        // Same expression, same name shape, DIFFERENT modes - which is only expressible because the mode
        // hangs off the reference.
        Assert.NotEqual(
            dynamicSigils.ProjectVariableReference().Mode,
            staticSigils.ProjectVariableReference().Mode);

        // Neither the descriptor nor the projection is reachable from an expression-level type: there is no
        // mode field on the definition port at all.
        Assert.DoesNotContain(typeof(ExpansionMode), DeclaredMemberTypes(typeof(LocalVariableDefinition)));
        Assert.DoesNotContain(typeof(ExpansionSigils), DeclaredMemberTypes(typeof(LocalVariableDefinition)));
    }

    /// <summary>
    /// THE PARSER AGREES: binding the specification's mixed expression leaves the dynamic reference in the
    /// stored text and substitutes the static one away, so the two really did take different paths.
    /// </summary>
    /// <remarks>
    /// This is the corroboration the descriptor-level test cannot give on its own. Two independent
    /// observations pin it: the STORED TEXT still names 上月读数 and no longer names 本月读数 because a
    /// static substitution rewrites the expression in place [:L1435], and the RETAINED REFERENCE ARRAY holds
    /// exactly the dynamic one. Contract C-04's three-part payload is visible here too - the unexpanded
    /// source, the bind-time snapshot, and the live reference - and the bind-time snapshot carries BOTH
    /// variables even though only one survived into the text.
    /// </remarks>
    [Fact]
    public void TheParserGivesTheTwoReferencesInOneMixedExpressionDifferentModes()
    {
        ColumnExpressionEngine engine = CreateEngine(out _);

        // docs:L66-L67 - 本月读数 is the expression "5" and 上月读数 is the numeric 0 that later mutates.
        Assert.Equal(RetCode.OK, engine.AddVarExp(ThisMonth, "5"));
        Assert.Equal(RetCode.OK, engine.AddVar(LastMonth, 0L));

        // docs:L68 - the mixed expression, verbatim.
        Assert.Equal(RetCode.OK, engine.AddVarExp(Num1, "$$" + LastMonth + " + $" + ThisMonth));

        // The static reference is GONE from the stored text, wrapped in the parentheses that preserve
        // precedence; the dynamic reference is still there, unexpanded.
        Assert.Equal("$$" + LastMonth + " + (5)", engine.GetVarExp(Num1));
        Assert.DoesNotContain("$" + ThisMonth, engine.GetVarExp(Num1), StringComparison.Ordinal);

        ExpressionBindingSnapshot? binding = engine.GetVariableBinding(Num1);
        Assert.NotNull(binding);

        // Exactly one reference retained, and it is the dynamic one.
        VariableReference retained = Assert.Single(binding.Vars);
        Assert.Equal(LastMonth, retained.Name);
        Assert.Equal("$$" + LastMonth, retained.FullName);
        Assert.Equal(
            ExpansionMode.Dynamic,
            ColumnExpressionEngine.SigilsFor(retained).ProjectVariableReference().Mode);

        // ZERO MEANS "NOT YET BOUND", AND IT IS A REQUEST RATHER THAN AN ERROR: the oracle resolves a zero
        // index by name at calculation time and caches the answer back [:L2189-L2195]. So a consumer must
        // not treat 0 as malformed - the environment answers the name perfectly well, and here it answers 2
        // because 本月读数 was declared first and 上月读数 second.
        Assert.Equal(0, retained.Index);
        Assert.False(engine.Variables.IsValidIndex(retained.Index));
        Assert.Equal(2, engine.Variables.IndexOf(retained.Name));
        Assert.Equal(LastMonth, engine.Variables[engine.Variables.IndexOf(retained.Name)].Name);

        // The three-part payload: the unexpanded source, the bind-time snapshot of BOTH variables, and the
        // live environment that still declares both.
        Assert.Equal("$$" + LastMonth + " + $" + ThisMonth, binding.UnexpandedExp);
        Assert.Equal("0", binding.BindTimeSnapshot[LastMonth]);
        Assert.Equal("5", binding.BindTimeSnapshot[ThisMonth]);
        Assert.Equal(3, engine.Variables.Count);
    }

    /// <summary>
    /// ONE LINE OF THE LIVE SCENARIO CARRIES THREE DIFFERENTLY CLASSIFIED REFERENCES - macro-direct,
    /// dynamic-indirect and a dynamic variable - which is the strongest available proof that the mode cannot
    /// be an expression-level property.
    /// </summary>
    /// <remarks>
    /// The line is <c>of_SetExp("n1", "$FormatPrice(n3-n2,$$('精度'))+$$数据汇总")</c>
    /// [w_test_dwsvc_columnexp.srw:L157]. <c>$FormatPrice(...)</c> is the application's own macro
    /// [dispatch at :L2286]; <c>$$('精度')</c> is the <c>FUNC_VAR</c> form whose recorded name is the EMPTY
    /// STRING sentinel [:L120, :L1388]; and <c>$$数据汇总</c> is a plain dynamic variable reference which in
    /// the scenario resolves to the OTHER DataWindow [w_test:L143].
    /// </remarks>
    [Fact]
    public void OneLineOfTheScenarioCarriesThreeDifferentlyClassifiedReferences()
    {
        ColumnExpressionEngine engine = CreateEngine(out _);

        // w_test:L109 - the typed precision variable the indirect reference will name at run time.
        Assert.Equal(RetCode.OK, engine.AddVar(Precision, 0L));

        int index = engine.AddExp(
            "n1",
            "$" + FormatPrice + "(n3-n2,$$('" + Precision + "'))+$$" + Summary);

        Assert.Equal(1, index);
        Assert.Null(engine.LastError);

        ColumnExpressionData bound = engine.Expressions[0];

        // One variable reference: the dynamic 数据汇总.
        VariableReference variable = Assert.Single(bound.Vars.Items);
        Assert.Equal(Summary, variable.Name);
        Assert.Equal(
            ExpansionMode.Dynamic,
            ColumnExpressionEngine.SigilsFor(variable).ProjectVariableReference().Mode);

        // Two function references, and they are classified differently from one another.
        ImmutableArray<FunctionReference> functions = bound.Fns.Items;
        Assert.Equal(2, functions.Length);

        FunctionReference direct = Assert.Single(functions, function => !function.Builtin);
        Assert.Equal(FormatPrice, direct.Name);
        Assert.Equal(
            ExpansionMode.MacroDirect,
            ExpansionSigils.StaticMacro
                .ProjectFunctionReference(MacroFunctionDispatch.Application)
                .Mode);

        FunctionReference indirect = Assert.Single(functions, function => function.Builtin);
        Assert.Equal(string.Empty, indirect.Name);
        Assert.Equal("$$('" + Precision + "')", indirect.FullName);
        Assert.Equal("'" + Precision + "'", Assert.Single(indirect.Args));
        Assert.Equal(
            ExpansionMode.DynamicIndirect,
            ExpansionSigils.DynamicMacro
                .ProjectFunctionReference(MacroFunctionDispatch.VariableLookup)
                .Mode);

        // Three references, three distinct wire modes, one expression.
        HashSet<ExpansionMode> modes =
        [
            ColumnExpressionEngine.SigilsFor(variable).ProjectVariableReference().Mode,
            ExpansionSigils.StaticMacro.ProjectFunctionReference(MacroFunctionDispatch.Application).Mode,
            ExpansionSigils.DynamicMacro.ProjectFunctionReference(MacroFunctionDispatch.VariableLookup).Mode,
        ];

        Assert.Equal(3, modes.Count);
    }

    /// <summary>
    /// The undocumented <c>@</c> context sigil is representable as <c>isctx</c>, and its presence FORCES the
    /// reference away from static expansion.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sigil is declared <c>MACRO_CONTEXT = "@"</c> [:L1253] and appears in no specification at all, so
    /// this test is the only place its behaviour is written down outside the parser. Static expansion
    /// substitutes ONCE at bind time and there is no calling context to resolve against then, so the
    /// restriction is inherent rather than incidental [:L1415-L1419].
    /// </para>
    /// <para>
    /// The refusal is asserted for EVERY context-bearing bit pattern, not just the one spelling, because the
    /// guarantee that matters is "no @ reference is ever reported as static" rather than "@$ is rejected".
    /// </para>
    /// </remarks>
    [Fact]
    public void TheContextSigil_IsRepresentableAsIsCtxAndForcesAwayFromStaticExpansion()
    {
        // `@name` and `@$$name` both set the bit; only the macro bit tells them apart.
        Assert.True(ExpansionSigils.Context.IsCtx);
        Assert.True(ExpansionSigils.ContextDynamicMacro.IsCtx);
        Assert.False(ExpansionSigils.Context.IsMacro);
        Assert.True(ExpansionSigils.ContextDynamicMacro.IsMacro);

        // The bit travels on the reference itself, which is what the parser copies it to [:L1413].
        VariableReference contextReference =
            new(Precision, "@$$" + Precision, 0, IsCtx: true, IsMacro: true);
        Assert.True(ColumnExpressionEngine.SigilsFor(contextReference).IsCtx);

        // NO context-bearing combination is ever reported as static.
        foreach (bool isMacro in (bool[])[false, true])
        {
            foreach (bool isDynamic in (bool[])[false, true])
            {
                ExpansionModeProjection projection =
                    new ExpansionSigils(IsCtx: true, isMacro, isDynamic).ProjectVariableReference();

                Assert.NotEqual(ExpansionMode.Static, projection.Mode);
            }
        }

        // The one spelling that ASKS for static is refused with the rejection named for it.
        Assert.Equal(
            ExpansionRejection.ContextStaticExpansion,
            ExpansionSigils.ContextStaticMacro.ProjectVariableReference().Rejection);

        // The accepted context macro form is dynamic, resolved by the CONTEXT service's own calculator.
        Assert.Equal(
            ExpansionMode.Dynamic,
            ExpansionSigils.ContextDynamicMacro.ProjectVariableReference().Mode);

        // And the engine reports the refusal through the site whose enum value is its own locator.
        Assert.Equal(
            1417,
            (int)ExpressionErrorSite.FunctionMacroContextVariableStaticExpansionUnsupported);
    }

    /// <summary>
    /// THE PARSER AGREES ABOUT THE CONTEXT SIGIL TOO: <c>@$精度</c> is refused and <c>@$$精度</c> is
    /// accepted with the context bit recorded on the reference.
    /// </summary>
    [Fact]
    public void TheParserRefusesAContextStaticReferenceAndAcceptsAContextDynamicOne()
    {
        ColumnExpressionEngine engine = CreateEngine(out _);

        Assert.Equal(RetCode.OK, engine.AddVar(Precision, 0L));

        Assert.Equal((int)RetCode.E_INVALID_ARGUMENT, engine.AddExp("n1", "@$" + Precision + " + 1"));
        Assert.NotNull(engine.LastError);
        Assert.Equal(
            ExpressionErrorSite.FunctionMacroContextVariableStaticExpansionUnsupported,
            engine.LastError.Site);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, engine.LastError.ReturnCode);

        engine.ClearErrors();

        Assert.Equal(1, engine.AddExp("n2", "@$$" + Precision + " + 1"));
        Assert.Null(engine.LastError);

        VariableReference recorded = Assert.Single(engine.Expressions[0].Vars.Items);
        Assert.Equal(Precision, recorded.Name);
        Assert.Equal("@$$" + Precision, recorded.FullName);
        Assert.True(recorded.IsCtx);
        Assert.True(recorded.IsMacro);
        Assert.Equal(
            ExpansionMode.Dynamic,
            ColumnExpressionEngine.SigilsFor(recorded).ProjectVariableReference().Mode);
    }

    /// <summary>
    /// A FOREIGN variable is marked <c>VAR_FOREIGN</c>, carries an opaque handle instead of a pointer, and
    /// CANNOT be expanded statically.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The specification states the rule where it introduces the feature - a foreign variable must use
    /// dynamic expansion [docs/n_cst_dwsvc_columnexp.md:L34] - and the parser enforces it at [:L1425-L1428].
    /// A static substitution would read the foreign value ONCE at bind time and then never see it change,
    /// which is precisely the staleness the narrowing exists to prevent.
    /// </para>
    /// <para>
    /// THE REFUSAL IS THE ENGINE'S AND NOT THE DESCRIPTOR'S, and that boundary is asserted here on purpose:
    /// deciding that a name is foreign requires looking it up in the table, which the sigil descriptor cannot
    /// do. So <see cref="ExpansionRejection"/> carries the two refusals decidable FROM THE SIGILS ALONE and
    /// the foreign one is reported through the error site whose enum value is its own locator.
    /// </para>
    /// </remarks>
    [Fact]
    public void AForeignVariable_IsMarkedForeignAndCannotBeExpandedStatically()
    {
        ExpressionSession session = NewSession();
        ColumnExpressionEngine peer = CreateEngineIn(session, out _);
        ColumnExpressionEngine engine = CreateEngineIn(session, out _);

        // w_test:L140 - the summary is defined on the OTHER DataWindow ...
        Assert.Equal(RetCode.OK, peer.AddVarExp(Summary, "SUM(n1)"));

        // ... and w_test:L143 references it from this one.
        Assert.Equal(RetCode.OK, engine.AddForeignVar(Summary, peer));

        int index = engine.Variables.IndexOf(Summary);
        Assert.Equal(1, index);

        GlobalVariable entry = engine.Variables[index];
        Assert.Equal(ExpressionVariableEnvironment.VAR_FOREIGN, entry.VarType);
        Assert.Equal(peer.Handle.Value, entry.Foreign.Handle);
        Assert.Equal(peer.Variables.IndexOf(Summary), entry.Foreign.Index);

        // BOTH payload members are present, and the local half is the zeroed default - which is why
        // of_getvarexp answers the empty string for a foreign variable rather than failing [:L1088].
        Assert.Equal(new LocalVariableDefinition(), entry.Local);
        Assert.Equal(string.Empty, engine.GetVarExp(Summary));

        // A static reference to it is refused ...
        Assert.Equal((int)RetCode.E_INVALID_ARGUMENT, engine.AddExp("n1", "$" + Summary + " + 1"));
        Assert.NotNull(engine.LastError);
        Assert.Equal(
            ExpressionErrorSite.VariableMacroForeignStaticExpansionUnsupported,
            engine.LastError.Site);
        Assert.Equal(1426, (int)engine.LastError.Site);

        // ... while the dynamic reference the specification requires is accepted.
        engine.ClearErrors();
        Assert.Equal(1, engine.AddExp("n2", "$$" + Summary + " + 1"));
        Assert.Null(engine.LastError);

        // The descriptor's two rejections are the two decidable from the sigils alone; the foreign refusal
        // needs the table and therefore is not one of them.
        Assert.Equal(3, Enum.GetValues<ExpansionRejection>().Length);
        Assert.Equal(0, (int)ExpansionRejection.None);
        Assert.Equal(1, (int)ExpansionRejection.ContextStaticExpansion);
        Assert.Equal(2, (int)ExpansionRejection.ContextFunctionMacro);
    }

    // ==============================================================================================
    //  4. ENVIRONMENT SEMANTICS
    //  --------------------------------------------------------------------------------------------
    //  The environment is THE TABLE, NOT THE ENGINE: it holds no live object reference, performs no
    //  validation and rejects no duplicate. Every behaviour in this section that IS a rejection therefore
    //  belongs to the engine and is observed HERE for its effect on the table - what the table contains
    //  afterwards, what index a name still resolves to, and what a caller is told. That split is itself
    //  asserted, because an omission that reads as a boundary is auditable and one that reads as an
    //  oversight is not.
    // ==============================================================================================

    /// <summary>
    /// The nine types of the variable model, so a sweep over the closure cannot silently omit one.
    /// </summary>
    /// <returns>The types.</returns>
    public static TheoryData<string> VariableModelTypeNames() => new()
    {
        nameof(ExpressionVariableEnvironment),
        nameof(GlobalVariable),
        nameof(LocalVariableDefinition),
        nameof(VariableReference),
        nameof(FunctionReference),
        nameof(ForeignVariableReference),
        nameof(ExpressionVariableValue),
        nameof(ExpansionSigils),
        nameof(ExpansionModeProjection),
    };

    /// <summary>
    /// The variable model holds NO DataWindow reference, no service reference, no session reference and no
    /// live object pointer of any kind - in a public member or a private one.
    /// </summary>
    /// <param name="typeName">The model type being swept.</param>
    /// <remarks>
    /// <para>
    /// <c>foreignvardata.expsvc</c> is declared <c>n_cst_dwsvc_columnexp expsvc</c> [:L82] and dereferenced
    /// synchronously into a private method of the other instance at [:L2199-L2200] and again at
    /// [:L2384-L2385]. A pointer cannot be serialized, so resolving one is the expression SESSION's job and
    /// the model carries an opaque handle value instead. If any of these types could hold one of those
    /// references, the narrowing would be defeated in the one place nobody would look.
    /// </para>
    /// <para>
    /// PRIVATE MEMBERS ARE IN SCOPE, because a reference smuggled into a private field would be just as
    /// fatal as a public one. The namespace whitelist is the second half of the sweep: a member may come from
    /// the base class library, from the immutable collections, from the model's own namespace, or from the
    /// generated contract namespace - and in that last case it must be an ENUM, so no wire MESSAGE object can
    /// leak into the domain model either.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(VariableModelTypeNames))]
    public void TheVariableModel_HoldsNoLiveObjectReferenceAnywhere(string typeName)
    {
        Type? model = typeof(ExpressionVariableEnvironment).Assembly
            .GetType("PowerFramework.DataServices.Expressions." + typeName);
        Assert.NotNull(model);

        string[] allowedNamespaces =
        [
            "System",
            "System.Collections.Immutable",
            "PowerFramework.DataServices.Expressions",
            "PowerFramework.Contracts.DataServices.V1",
        ];

        ImmutableArray<Type> liveObjectTypes = LiveObjectTypes();

        foreach (Type member in DeclaredMemberTypes(model))
        {
            foreach (Type live in liveObjectTypes)
            {
                Assert.False(
                    live.IsAssignableFrom(member),
                    $"{typeName} has a member typed {member.Name}, which could hold a {live.Name}.");

                if (member.IsInterface)
                {
                    Assert.False(
                        member.IsAssignableFrom(live),
                        $"{typeName} has a member typed {member.Name}, which a {live.Name} implements.");
                }
            }

            string memberNamespace = member.Namespace ?? string.Empty;
            Assert.Contains(memberNamespace, allowedNamespaces);

            if (string.Equals(memberNamespace, "PowerFramework.Contracts.DataServices.V1", StringComparison.Ordinal))
            {
                Assert.True(
                    member.IsEnum,
                    $"{typeName} has a member typed {member.Name} from the generated contract namespace, "
                        + "and only an enum may come from there.");
            }
        }
    }

    /// <summary>
    /// The handle that replaced the pointer is a plain string, and nothing about it is a network address.
    /// </summary>
    [Fact]
    public void TheForeignHandle_IsAPlainOpaqueString()
    {
        Assert.Equal(
            typeof(string),
            PortFieldType(typeof(ForeignVariableReference), nameof(ForeignVariableReference.Handle)));

        // Two references with the same index but different handles are DIFFERENT references, which is what
        // makes the handle load bearing rather than decorative.
        Assert.NotEqual(
            new ForeignVariableReference(1, "dw_1"),
            new ForeignVariableReference(1, "dw_2"));
    }

    /// <summary>
    /// A DUPLICATE variable definition answers <c>FAILED</c> with the oracle's own hardcoded Chinese message,
    /// and THE FIRST DEFINITION SURVIVES UNCHANGED.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>of_addvarexp</c> checks for a duplicate second of four checks and reports at [:L1607]. The code is
    /// <c>FAILED</c> and not <c>E_INVALID_ARGUMENT</c>, which is a real distinction preserved verbatim: the
    /// argument is well formed and it is the STATE that rejects it. The message does NOT route through
    /// localization - all 28 messages in this legacy object are hardcoded Chinese while the equivalent
    /// messages on the validation path do localize - and that inconsistency is reproduced rather than
    /// harmonized (C-B).
    /// </para>
    /// <para>
    /// THAT THE FIRST DEFINITION SURVIVES IS THE PART THAT MATTERS TO THE TABLE. A rejection that had already
    /// overwritten the entry would leave the environment holding the loser of the collision.
    /// </para>
    /// </remarks>
    [Fact]
    public void DuplicateVariableDefinition_AnswersFailedAndPreservesTheFirstDefinition()
    {
        ColumnExpressionEngine engine = CreateEngine(out _);

        // w_test:L104 - 损耗 is defined as the expression "1".
        Assert.Equal(RetCode.OK, engine.AddVarExp(Loss, "1"));
        Assert.Equal(RetCode.FAILED, engine.AddVarExp(Loss, "999"));

        Assert.NotNull(engine.LastError);
        Assert.Equal(ExpressionErrorSite.DuplicateVariableDefinition, engine.LastError.Site);
        Assert.Equal(1607, (int)engine.LastError.Site);
        Assert.Equal(RetCode.FAILED, engine.LastError.ReturnCode);
        Assert.Equal("变量[" + Loss + "],重复定义", engine.LastError.Text);
        Assert.False(engine.LastError.Localized);

        // The table is untouched: one entry, at index one, still holding the FIRST expression.
        Assert.Equal(1, engine.Variables.Count);
        Assert.Equal(1, engine.Variables.IndexOf(Loss));
        Assert.Equal("1", engine.GetVarExp(Loss));
        Assert.Equal("1", engine.Variables[1].Local.Exp);
        Assert.Equal(ExpressionVariableEnvironment.VAR_LOCAL, engine.Variables[1].VarType);
    }

    /// <summary>
    /// The FOREIGN duplicate entry point reports a byte-identical message and the same code, from its own
    /// site.
    /// </summary>
    /// <remarks>
    /// <c>of_addforeignvar</c> has its own duplicate check at [:L2122] whose template is byte-identical to
    /// [:L1607] - a different entry point, hence a different site with the same text. Preserving both sites
    /// keeps a recording able to say WHICH entry point rejected, while the observable message stays the same
    /// as the oracle's.
    /// </remarks>
    [Fact]
    public void TheForeignDuplicateEntryPoint_SharesTheMessageAndTheCodeFromItsOwnSite()
    {
        ExpressionSession session = NewSession();
        ColumnExpressionEngine peer = CreateEngineIn(session, out _);
        ColumnExpressionEngine engine = CreateEngineIn(session, out _);

        Assert.Equal(RetCode.OK, peer.AddVarExp(Summary, "SUM(n1)"));
        Assert.Equal(RetCode.OK, engine.AddVarExp(Summary, "0"));

        // The name is already taken locally, so the foreign entry point rejects it - from its own site.
        Assert.Equal(RetCode.FAILED, engine.AddForeignVar(Summary, peer));

        Assert.NotNull(engine.LastError);
        Assert.Equal(ExpressionErrorSite.DuplicateForeignVariableDefinition, engine.LastError.Site);
        Assert.Equal(2122, (int)engine.LastError.Site);
        Assert.Equal(RetCode.FAILED, engine.LastError.ReturnCode);
        Assert.Equal("变量[" + Summary + "],重复定义", engine.LastError.Text);

        // And the local definition survives as a LOCAL one - it was not converted to a foreign entry.
        Assert.Equal(1, engine.Variables.Count);
        Assert.Equal(ExpressionVariableEnvironment.VAR_LOCAL, engine.Variables[1].VarType);
        Assert.Equal("0", engine.Variables[1].Local.Exp);
    }

    /// <summary>
    /// An UNDEFINED variable reference answers a defined error - never a null, never a default and never a
    /// silently empty value.
    /// </summary>
    /// <remarks>
    /// The static path fails at bind time, where <c>_of_findvarindex</c> answered zero [:L1422]. The message
    /// carries the offending name, the expression and a caret. Nothing is added to the table, so the caller
    /// is not left with a half-defined variable.
    /// </remarks>
    [Fact]
    public void AnUndefinedVariableReference_AnswersADefinedErrorRatherThanANullOrADefault()
    {
        ColumnExpressionEngine engine = CreateEngine(out _);

        Assert.Equal(RetCode.OK, engine.AddVarExp(Loss, "1"));

        const string Undefined = "没有这个变量";
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, engine.AddVarExp(Num1, "$" + Undefined + " + 1"));

        Assert.NotNull(engine.LastError);
        Assert.Equal(ExpressionErrorSite.VariableMacroUndefined, engine.LastError.Site);
        Assert.Equal(1422, (int)engine.LastError.Site);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, engine.LastError.ReturnCode);
        Assert.Contains("变量[" + Undefined + "],未定义", engine.LastError.Text, StringComparison.Ordinal);
        Assert.NotNull(engine.LastError.CaretPosition);

        // The failed definition was NOT added, and the successful one is untouched.
        Assert.Equal(1, engine.Variables.Count);
        Assert.Equal(0, engine.Variables.IndexOf(Num1));
        Assert.Equal(1, engine.Variables.IndexOf(Loss));

        // And the environment's own answer for the missing name is the legacy's zero, not an entry.
        Assert.Equal(0, engine.Variables.IndexOf(Undefined));
        Assert.Equal(string.Empty, engine.GetVarExp(Undefined));
    }

    /// <summary>
    /// The environment answers ZERO for an unknown name - the legacy's own "not found" value - and REFUSES to
    /// be subscripted with it rather than yielding a default entry.
    /// </summary>
    /// <remarks>
    /// <c>_of_findvarindex</c> returns 0 when the name is absent [:L1000] and callers test it as such
    /// [:L1421, :L2191], so 0 is returned rather than -1 and rather than an exception. Subscripting with 0 is
    /// a different matter: index 0 is NOT the first entry, so the indexer faults exactly as a PowerBuilder
    /// array subscript would, and <see cref="ExpressionVariableEnvironment.IsValidIndex(int)"/> exists so a
    /// consumer holding a possibly-unbound index can test it without provoking that.
    /// </remarks>
    [Fact]
    public void TheEnvironment_AnswersZeroForAnUnknownNameAndRefusesToSubscriptIt()
    {
        ExpressionVariableEnvironment environment = ScenarioChain();

        Assert.Equal(0, environment.IndexOf("没有这个变量"));
        Assert.Equal(0, environment.IndexOf(null));
        Assert.Equal(0, environment.IndexOf(string.Empty));

        Assert.False(environment.IsValidIndex(0));
        Assert.False(environment.IsValidIndex(-1));
        Assert.False(environment.IsValidIndex(environment.UpperBound + 1));

        ArgumentOutOfRangeException failure =
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = environment[0]; });
        Assert.Equal("index", failure.ParamName);

        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = environment[environment.UpperBound + 1]; });

        // The empty environment is the faithful analogue of the array before anything was added, where
        // UpperBound(GlobalVars) is 0.
        Assert.Equal(0, ExpressionVariableEnvironment.Empty.UpperBound);
        Assert.Equal(0, ExpressionVariableEnvironment.Empty.Count);
        Assert.Empty(ExpressionVariableEnvironment.Empty.Variables);
        Assert.Equal(0, ExpressionVariableEnvironment.Empty.IndexOf(Precision));
    }

    /// <summary>
    /// The table is ONE-BASED and its <c>UpperBound</c> is the LAST VALID SUBSCRIPT, while
    /// <see cref="ExpressionVariableEnvironment.Variables"/> stays zero-based as a .NET collection.
    /// </summary>
    /// <remarks>
    /// A PowerBuilder array is one-based and its upper bound is the last valid subscript, whereas a .NET
    /// length is one past the end. Conflating the two is the single most dangerous mechanical hazard in this
    /// migration, which is why the legacy quantity is exposed under its own name and asserted here against
    /// both index spaces at once.
    /// </remarks>
    [Fact]
    public void TheTableIsOneBasedAndItsUpperBoundIsTheLastValidSubscript()
    {
        GlobalVariable[] entries =
        [
            .. Enumerable.Range(1, 5).Select(static position => new GlobalVariable
            {
                Name = "v" + position.ToString(CultureInfo.InvariantCulture),
                Local = new LocalVariableDefinition
                {
                    Exp = position.ToString(CultureInfo.InvariantCulture),
                },
            }),
        ];

        ExpressionVariableEnvironment environment = ExpressionVariableEnvironment.Create(entries);

        Assert.Equal(5, environment.UpperBound);
        Assert.Equal(5, environment.Count);
        Assert.Equal(5, environment.Variables.Length);

        // `for i = 1 to env.UpperBound` addresses every entry, and entry i is Variables[i - 1].
        for (int index = 1; index <= environment.UpperBound; index++)
        {
            Assert.True(environment.IsValidIndex(index));
            Assert.Equal("v" + index.ToString(CultureInfo.InvariantCulture), environment[index].Name);
            Assert.Same(environment.Variables[index - 1], environment[index]);
            Assert.Equal(index, environment.IndexOf(environment[index].Name));
        }

        // Append lands the new entry at the returned environment's UpperBound, which is what makes its
        // one-based index knowable without a second lookup [:L1625].
        ExpressionVariableEnvironment grown =
            environment.Append(new GlobalVariable { Name = Precision, Local = new LocalVariableDefinition { Exp = "0" } });

        Assert.Equal(6, grown.UpperBound);
        Assert.Equal(Precision, grown[grown.UpperBound].Name);

        // And the snapshot it grew from is untouched, which is what "snapshot" has to mean for contract
        // C-04's bind-time-against-live distinction to be meaningful at all.
        Assert.Equal(5, environment.UpperBound);
        Assert.Equal(0, environment.IndexOf(Precision));
    }

    /// <summary>
    /// The name lookup is ORDINAL AND CASE-SENSITIVE, and it SCANS BACKWARDS so that a duplicated name
    /// resolves to the LAST entry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The oracle iterates <c>for nIndex = UpperBound(GlobalVars) to 1 step -1</c> [:L996] and returns on the
    /// first match, and its own header states the case sensitivity in as many words [:L1587]. REVERSE
    /// ITERATION IS THE MOST DANGEROUS PATTERN IN THIS MIGRATION precisely because it looks like a mistake and
    /// is not: "correcting" it to a forward scan would silently change which entry a duplicated name resolves
    /// to, and no count or arity assertion would notice.
    /// </para>
    /// <para>
    /// The engine's own duplicate rejection means duplicates do not arise through its entry points - which is
    /// exactly why the tie-break is invisible in normal operation and would survive a careless rewrite
    /// unnoticed. The environment does not enforce that rejection, so it must behave correctly when a
    /// duplicate does exist, and this test constructs one directly to prove that it does.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheNameLookupIsOrdinalCaseSensitiveAndScansBackwards()
    {
        ExpressionVariableEnvironment environment = ExpressionVariableEnvironment.Create(
        [
            new GlobalVariable { Name = Precision, Local = new LocalVariableDefinition { Exp = "first" } },
            new GlobalVariable { Name = Loss, Local = new LocalVariableDefinition { Exp = "loss" } },
            new GlobalVariable { Name = Precision, Local = new LocalVariableDefinition { Exp = "last" } },
        ]);

        // THE LAST DUPLICATE WINS.
        Assert.Equal(3, environment.IndexOf(Precision));
        Assert.Equal("last", environment[environment.IndexOf(Precision)].Local.Exp);

        // The first one is still there and still reachable by subscript - the scan chose, it did not delete.
        Assert.Equal("first", environment[1].Local.Exp);

        // Case-sensitive and ordinal: a differently-cased ASCII name does not match.
        ExpressionVariableEnvironment cased = ExpressionVariableEnvironment.Create(
        [
            new GlobalVariable { Name = Num1, Local = new LocalVariableDefinition { Exp = "1" } },
        ]);

        Assert.Equal(1, cased.IndexOf(Num1));
        Assert.Equal(0, cased.IndexOf("NUM1"));
        Assert.Equal(0, cased.IndexOf("Num1"));
    }

    /// <summary>
    /// SET AFTER ADD UPDATES IN PLACE AND PRESERVES THE VARIABLE'S INDEX, and every other entry keeps its
    /// index too.
    /// </summary>
    /// <remarks>
    /// This is not housekeeping. <c>columndata.varindexes[]</c> [:L47] is a REVERSE DEPENDENCY INDEX from a
    /// column to the variables its expressions depend on, and it refers to variables BY THESE INDEXES - so
    /// re-indexing on update would silently break dirty propagation: the recalculation would run against the
    /// wrong variable, or against none, and produce a stale value with no error anywhere. w_test:L292 is the
    /// live call.
    /// </remarks>
    [Fact]
    public async Task SetAfterAdd_UpdatesInPlaceAndPreservesTheIndex()
    {
        ColumnExpressionEngine engine = CreateEngine(out _);

        // w_test:L104, :L105, :L109 - three variables, in the scenario's own order.
        Assert.Equal(RetCode.OK, engine.AddVarExp(Loss, "1"));
        Assert.Equal(RetCode.OK, engine.AddVarExp(Multiplier, "2"));
        Assert.Equal(RetCode.OK, engine.AddVarExp(Precision, "0"));

        Assert.Equal(1, engine.Variables.IndexOf(Loss));
        Assert.Equal(2, engine.Variables.IndexOf(Multiplier));
        Assert.Equal(3, engine.Variables.IndexOf(Precision));

        long? replacement = 99L;
        Assert.Equal(RetCode.OK, await engine.SetVarAsync(Multiplier, replacement, recalc: false, Ct));

        // The value changed ...
        Assert.Equal("99", engine.GetVarExp(Multiplier));

        // ... and not one index moved, nor did the count.
        Assert.Equal(3, engine.Variables.Count);
        Assert.Equal(1, engine.Variables.IndexOf(Loss));
        Assert.Equal(2, engine.Variables.IndexOf(Multiplier));
        Assert.Equal(3, engine.Variables.IndexOf(Precision));
        Assert.Equal("1", engine.GetVarExp(Loss));
        Assert.Equal("0", engine.GetVarExp(Precision));
    }

    /// <summary>
    /// At the environment level, <see cref="ExpressionVariableEnvironment.Replace(int, GlobalVariable)"/> is
    /// the operation that preserves an index and <see cref="ExpressionVariableEnvironment.Append"/> is the one
    /// that does not - which is why an update must be a replace.
    /// </summary>
    [Fact]
    public void ReplacePreservesTheIndexWhileAppendDoesNot()
    {
        ExpressionVariableEnvironment environment = ScenarioChain();
        int precisionIndex = environment.IndexOf(Precision);
        Assert.Equal(2, precisionIndex);

        GlobalVariable updated = environment[precisionIndex] with
        {
            Local = new LocalVariableDefinition { Exp = "2" },
        };

        ExpressionVariableEnvironment replaced = environment.Replace(precisionIndex, updated);

        Assert.Equal(environment.UpperBound, replaced.UpperBound);
        Assert.Equal(precisionIndex, replaced.IndexOf(Precision));
        Assert.Equal("2", replaced[precisionIndex].Local.Exp);

        // Every other entry is exactly where it was, so a varindexes[] reference into this table still lands
        // on the same variable.
        for (int index = 1; index <= environment.UpperBound; index++)
        {
            if (index != precisionIndex)
            {
                Assert.Equal(environment[index], replaced[index]);
            }
        }

        // Appending the same update instead would give the name TWO entries and move the resolved index,
        // which is precisely the silent breakage the replace exists to avoid.
        ExpressionVariableEnvironment appended = environment.Append(updated);
        Assert.Equal(environment.UpperBound + 1, appended.UpperBound);
        Assert.NotEqual(precisionIndex, appended.IndexOf(Precision));

        // Guard rails on the replace itself.
        Assert.Throws<ArgumentOutOfRangeException>(() => environment.Replace(0, updated));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => environment.Replace(environment.UpperBound + 1, updated));
        Assert.Throws<ArgumentNullException>(() => environment.Replace(precisionIndex, null!));
        Assert.Throws<ArgumentNullException>(() => environment.Append(null!));
        Assert.Throws<ArgumentNullException>(
            () => ExpressionVariableEnvironment.Create((IEnumerable<GlobalVariable>)null!));
    }

    /// <summary>
    /// ALL SEVEN TYPED ADD FAMILIES ARE REACHABLE, and each lands a <c>VAR_LOCAL</c> entry in the table.
    /// </summary>
    /// <remarks>
    /// Every <c>of_addvar</c> overload is a one-line adapter over <c>of_addvarexp</c> [:L1091-L1109], so a
    /// typed add does not create typed STORAGE - it renders a literal into the variable's expression text.
    /// That is why the typed value travels beside the structure port rather than inside it, and why all seven
    /// arrive at the same table shape.
    /// </remarks>
    [Fact]
    public void AllSevenTypedAddFamilies_AreReachableAndLandLocalEntries()
    {
        ColumnExpressionEngine engine = CreateEngine(out _);

        Assert.Equal(RetCode.OK, engine.AddVar("t", new TimeOnly(13, 45, 30)));
        Assert.Equal(RetCode.OK, engine.AddVar("s", "3"));
        Assert.Equal(RetCode.OK, engine.AddVar(Precision, 0L));
        Assert.Equal(RetCode.OK, engine.AddVar("d", 3d));
        Assert.Equal(RetCode.OK, engine.AddVar("dt", new DateTime(2026, 1, 1, 13, 45, 30, DateTimeKind.Unspecified)));
        Assert.Equal(RetCode.OK, engine.AddVar("dd", new DateOnly(2026, 1, 1)));
        Assert.Equal(RetCode.OK, engine.AddVar("b", true));

        Assert.Equal(7, engine.Variables.Count);

        for (int index = 1; index <= engine.Variables.UpperBound; index++)
        {
            GlobalVariable entry = engine.Variables[index];

            Assert.Equal(ExpressionVariableEnvironment.VAR_LOCAL, entry.VarType);
            Assert.NotEqual(string.Empty, entry.Local.Exp);
            Assert.False(entry.Local.HasMacro);
            Assert.Empty(entry.Local.Vars);
            Assert.Empty(entry.Local.Fns);

            // The foreign half of every LOCAL entry is the zeroed default, present but unread.
            Assert.Equal(new ForeignVariableReference(), entry.Foreign);
            Assert.Empty(entry.Links);
        }

        // The seven land in declaration order, which is the index space every reference resolves against.
        Assert.Equal(3, engine.Variables.IndexOf(Precision));
    }

    /// <summary>
    /// The SEVEN typed SET families exist in THREE ARITIES EACH - value, value plus recalculate, and value
    /// plus recalculate plus force - so all twenty-one legacy entry points are reachable.
    /// </summary>
    /// <remarks>
    /// The oracle declares the same seven types three times over: the one-argument arity [:L1112-L1130], the
    /// recalculate arity [:L1713-L1731] and the recalculate-plus-force arity [:L1779-L1798]. The boolean
    /// overload is the one whose value parameter is NOT nullable, because there is no boolean null literal to
    /// render - PowerScript's <c>String(null)</c> would poison the expression and inventing a form would be a
    /// widening.
    /// </remarks>
    [Fact]
    public void TheSevenTypedSetFamilies_ExistInThreeAritiesEach()
    {
        MethodInfo[] setters =
        [
            .. typeof(ColumnExpressionEngine)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(static method => method.Name == nameof(ColumnExpressionEngine.SetVarAsync)),
        ];

        Assert.Equal(21, setters.Length);

        Type[] expectedValueTypes =
        [
            typeof(TimeOnly?),
            typeof(string),
            typeof(long?),
            typeof(double?),
            typeof(DateTime?),
            typeof(DateOnly?),
            typeof(bool),
        ];

        // Arity is counted including the trailing cancellation token, which every one of them takes.
        foreach (int parameterCount in (int[])[3, 4, 5])
        {
            MethodInfo[] arity =
            [
                .. setters.Where(method => method.GetParameters().Length == parameterCount),
            ];

            Assert.Equal(7, arity.Length);
            Assert.Equal(
                expectedValueTypes.Select(static type => type.FullName).Order(StringComparer.Ordinal),
                arity.Select(static method => method.GetParameters()[1].ParameterType.FullName).Order(StringComparer.Ordinal));

            Assert.All(
                arity,
                static method =>
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    Assert.Equal(typeof(string), parameters[0].ParameterType);
                    Assert.Equal(typeof(CancellationToken), parameters[^1].ParameterType);
                    Assert.Equal(typeof(ValueTask<long>), method.ReturnType);
                });
        }

        // The two extra parameters the wider arities take are the recalculate and force flags, in that order.
        MethodInfo widest = Assert.Single(
            setters,
            static method =>
                method.GetParameters().Length == 5
                && method.GetParameters()[1].ParameterType == typeof(long?));

        Assert.Equal("recalc", widest.GetParameters()[2].Name);
        Assert.Equal("force", widest.GetParameters()[3].Name);
    }

    /// <summary>
    /// The three set arities DIFFER ONLY IN THE RECALCULATION BEHAVIOUR, NOT IN THE STORED VALUE: the same
    /// value through all three stores byte-identical expression text.
    /// </summary>
    /// <remarks>
    /// The narrower arities are adapters that supply <see langword="false"/> for the flags they do not take
    /// [:L1112-L1130 delegating, :L1713-L1731 delegating], so the value path is shared and the flags only
    /// decide whether the variable-changed event fires and whether input-triggered expressions are included.
    /// w_test:L292 uses the middle arity in live code.
    /// </remarks>
    [Fact]
    public async Task TheThreeSetArities_DifferOnlyInRecalculationNotInTheStoredValue()
    {
        ColumnExpressionEngine engine = CreateEngine(out _);

        Assert.Equal(RetCode.OK, engine.AddVarExp(Loss, "0"));
        Assert.Equal(RetCode.OK, engine.AddVarExp(Multiplier, "0"));
        Assert.Equal(RetCode.OK, engine.AddVarExp(Precision, "0"));

        long? seven = 7L;

        Assert.Equal(RetCode.OK, await engine.SetVarAsync(Loss, seven, Ct));
        Assert.Equal(RetCode.OK, await engine.SetVarAsync(Multiplier, seven, true, Ct));
        Assert.Equal(RetCode.OK, await engine.SetVarAsync(Precision, seven, true, true, Ct));

        Assert.Equal("7", engine.GetVarExp(Loss));
        Assert.Equal("7", engine.GetVarExp(Multiplier));
        Assert.Equal("7", engine.GetVarExp(Precision));

        // Same stored value means the three entries are structurally identical apart from their names, so a
        // recording taken through one arity is comparable with one taken through another.
        Assert.Equal(
            engine.Variables[1].Local,
            engine.Variables[2].Local);
        Assert.Equal(
            engine.Variables[2].Local,
            engine.Variables[3].Local);

        // And no index moved, which the recalculating arities could plausibly have disturbed.
        Assert.Equal(3, engine.Variables.Count);
        Assert.Equal(1, engine.Variables.IndexOf(Loss));
        Assert.Equal(2, engine.Variables.IndexOf(Multiplier));
        Assert.Equal(3, engine.Variables.IndexOf(Precision));
    }

    /// <summary>
    /// The environment is a SNAPSHOT: it declares no mutator, and every one of the engine's mutating entry
    /// points produces a new one.
    /// </summary>
    /// <remarks>
    /// Contract C-04 requires the payload to carry a BIND-TIME SNAPSHOT and a LIVE ENVIRONMENT as two separate
    /// things, which is only meaningful if a snapshot cannot change after it is taken. Immutability is also
    /// what makes the type safe to share across the concurrent calls a gRPC service handles, and what gives it
    /// the value equality a characterization recording compares directly.
    /// </remarks>
    [Fact]
    public void TheEnvironmentIsASnapshotAndDeclaresNoMutator()
    {
        // No settable property anywhere on the type.
        Assert.All(
            typeof(ExpressionVariableEnvironment).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            static property => Assert.False(property.CanWrite));

        ExpressionVariableEnvironment before = ScenarioChain();
        ExpressionVariableEnvironment after = before
            .Append(new GlobalVariable { Name = Multiplier, Local = new LocalVariableDefinition { Exp = "2" } });

        Assert.NotSame(before, after);
        Assert.NotEqual(before, after);
        Assert.Equal(4, before.UpperBound);
        Assert.Equal(0, before.IndexOf(Multiplier));

        // Order participates in equality, because order defines the one-based index space every reference
        // resolves against - so two tables with the same entries differently ordered are different
        // environments, not the same one twice.
        GlobalVariable first = before[1];
        GlobalVariable second = before[2];

        ExpressionVariableEnvironment swapped = before.Replace(1, second).Replace(2, first);

        Assert.NotEqual(before, swapped);
        Assert.Equal(before.Count, swapped.Count);

        // Empty is a singleton-shaped answer, and an uninitialised array is treated as empty because a
        // PowerBuilder array is never null.
        Assert.Same(ExpressionVariableEnvironment.Empty, ExpressionVariableEnvironment.Create([]));
        Assert.Same(
            ExpressionVariableEnvironment.Empty,
            ExpressionVariableEnvironment.Create(default(ImmutableArray<GlobalVariable>)));
        Assert.Equal(ExpressionVariableEnvironment.Empty, ExpressionVariableEnvironment.Empty);
    }

    /// <summary>
    /// The environment performs NO ENGINE BEHAVIOUR: it does not reject a duplicate, parse an expression,
    /// derive <c>hasmacro</c> or reset a column binding, and that boundary is deliberate rather than an
    /// omission.
    /// </summary>
    /// <remarks>
    /// All four belong to <c>of_addvarexp</c> [:L1605-L1627] and are asserted against the ENGINE elsewhere in
    /// this suite. Asserting their absence here is what makes the split auditable: a reader who expected the
    /// table to reject a duplicate can see that the table's own contract says it does not, and can see where
    /// the rejection actually lives.
    /// </remarks>
    [Fact]
    public void TheEnvironmentPerformsNoEngineBehaviour()
    {
        GlobalVariable entry = new()
        {
            Name = Precision,
            Local = new LocalVariableDefinition { Exp = "0" },
        };

        // It appends a duplicate name without complaint - the rejection is the engine's [:L1606-L1609].
        ExpressionVariableEnvironment duplicated = ExpressionVariableEnvironment.Empty
            .Append(entry)
            .Append(entry);

        Assert.Equal(2, duplicated.Count);
        Assert.Equal(2, duplicated.IndexOf(Precision));

        // It accepts an empty name and an empty expression - both rejections are the engine's [:L1605].
        ExpressionVariableEnvironment empties = ExpressionVariableEnvironment.Empty
            .Append(new GlobalVariable());

        Assert.Equal(1, empties.Count);
        Assert.Equal(string.Empty, empties[1].Name);
        Assert.Equal(string.Empty, empties[1].Local.Exp);

        // It does not DERIVE HasMacro from the reference arrays: an entry may declare references and report
        // false, because the oracle computes the flag once inside of_addvarexp [:L1623] and this table only
        // carries whatever it was told.
        GlobalVariable inconsistent = new()
        {
            Name = Num1,
            Local = new LocalVariableDefinition
            {
                Exp = "$$" + Precision,
                Vars = [new VariableReference(Precision, "$$" + Precision, 1, IsCtx: false, IsMacro: true)],
                HasMacro = false,
            },
        };

        ExpressionVariableEnvironment carried = ExpressionVariableEnvironment.Empty.Append(inconsistent);

        Assert.False(carried[1].Local.HasMacro);
        Assert.Single(carried[1].Local.Vars);

        // And it declares no parsing, rendering or recalculation surface at all - only the table operations.
        string[] methods =
        [
            .. typeof(ExpressionVariableEnvironment)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(static method => method.Name)
                .Where(static name => !name.StartsWith("get_", StringComparison.Ordinal)
                    && !name.StartsWith("op_", StringComparison.Ordinal))
                .Distinct()
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal(
            ["Append", "Create", "Equals", "GetHashCode", "IndexOf", "IsValidIndex", "Replace"],
            methods);
    }
}
