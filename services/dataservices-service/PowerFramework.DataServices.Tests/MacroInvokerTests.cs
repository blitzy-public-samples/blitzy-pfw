// ==================================================================================================
//  MacroInvokerTests - THE MACRO-INVOCATION PROTOCOL, CHARACTERIZED AGAINST THE ORACLE
//  ------------------------------------------------------------------------------------------------
//  SUBJECT   services/dataservices-service/PowerFramework.DataServices/Expressions/MacroInvoker.cs
//
//  ORACLE    ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L14
//              event type any oncolumnexpinvokemethod ( long row, dwobject dwo, string name,
//                                                       string args[] )
//            ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru
//              :L120-L121   the two sentinels          :L1290/:L1311  bDD -> funcdata.builtin
//              :L1306-L1310 the context refusal        :L1313/:L1315  Mid(+2) versus Mid(+1)
//              :L1383       invalid argument list      :L1386-L1401   the arity switch
//              :L2226-L2321 the dispatch loop          :L2259-L2263   the argument re-basing
//              :L2282/:L2306 the two invalid-return sites
//            docs/n_cst_dwsvc_columnexp.md, section 宏函数
//              :L110 direct syntax  :L117 the direct example  :L126 the handler reading args[1]/args[2]
//              :L134 dynamic syntax :L138-L141 the dynamic example  :L149 the same handler
//            ws_objects/pfw.tests.pbl.src/w_test_dwsvc_columnexp.srw
//              :L109 of_AddVar("精度",0)  :L151/:L154/:L157 the three live scenario shapes
//              :L305-L309 the LIVE handler: return Round(Double(args[1]),Long(args[2]))
//
//  RULES THAT GOVERN THIS FILE
//  ------------------------------------------------------------------------------------------------
//  `review_rules` returns "No user rules provided." - NO USER RULE GOVERNS THIS FILE, and none is
//  invented here. AAP 0.7.2's enterprise-standard baseline applies in their place, and the binding
//  constraints are the AAP's own (0.7.3):
//
//    C-B / G2      The sentinels, the arity rules and the built-in discrimination are LEGACY
//                  BEHAVIOUR, asserted as the oracle has them and never as they "should" be. Two
//                  refusal messages differ only in which field they name; a port that unified them
//                  would change observable output, so both are asserted separately.
//    0.6.2 / C-04  The InvokeMethodChannel inversion is STRUCTURALLY REQUIRED, not a preference: the
//                  legacy expects the APPLICATION to implement the macro switch, so across a
//                  boundary DataServices must call back into its client. These tests assert THE
//                  INVERSION - that the client is asked and its answer is required - and never a
//                  local macro switch. Nothing in this file implements `choose case name`.
//    0.4.5.4 / R9  Legacy arrays are ONE-BASED, and the AAP names one-based-to-zero-based translation
//                  the single most dangerous mechanical hazard in the refactor, because a silent
//                  off-by-one is indistinguishable from a behavioural regression. EVERY expectation
//                  below indexes from 1, and the negative assertions prove index 0 is not the first
//                  argument rather than merely leaving it untested.
//    C-K           Every theory row carries its oracle locator as its last column.
//    Naming        SCREAMING_SNAKE constants are REFERENCED (MacroSentinels.FUNC_VAR,
//                  MacroSentinels.FUNC_INVOKE, RetCode.E_INVALID_ARGUMENT) and NEVER re-declared
//                  here. A local copy would let the test agree with itself while disagreeing with
//                  the engine.
//    0.6.7         Multi-row assertions are `[Theory]` over `[MemberData]`; the macro double comes
//                  from TestDoubles.cs because no mocking library is in the dependency inventory
//                  (AAP 0.5.3); plain xunit `Assert` throughout.
//
//  WHAT THIS FILE DOES NOT COVER, SO THE BOUNDARIES READ AS BOUNDARIES
//  ------------------------------------------------------------------------------------------------
//    * The caret ARITHMETIC itself - `nMacPos + (nPos - nMacPos) / 2` and its integer division - is
//      ParseErrorFormatterTests.cs's. This file asserts only that the midpoint VALUE it is handed is
//      the value that reaches the structured error unchanged.
//    * The ORDERING assertion for pattern (b) is EventOrderingPatternTests.cs's. This file asserts
//      the VALUE DEPENDENCY: the calculation cannot proceed until the client answers.
//    * The temporal/number/boolean/string rendering arms of the conversion switch are
//      MacroTemporalRenderingTests.cs's. This file uses rendering only where a form or a refusal is
//      what is under test.
//    * Expression scanning, variable tables and substitution are ColumnExpressionEngineTests.cs's.
// ==================================================================================================

using System.Collections.Immutable;
using System.Globalization;

using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Expressions;
using PowerFramework.Shared.Kernel;

using Xunit;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// Characterizes <see cref="MacroInvoker"/> against the macro half of the legacy column-expression
/// service: the invocation shape, the one-based argument contract, the two invocation forms, the
/// sentinel/arity/built-in rules, and the client-callback inversion with its two invalid-return paths.
/// </summary>
public sealed class MacroInvokerTests
{
    // ==============================================================================================
    //  SHARED FIXTURE
    //  --------------------------------------------------------------------------------------------
    //  The names are the ORACLE'S OWN, Chinese included, so a failure message reads as the scenario
    //  the specification documents rather than as an abstraction of it:
    //    精度            "precision"        - a variable, of_AddVar("精度",0) [w_test:L109],
    //                                        of_SetVar("精度", 1) [docs:L115, L138]
    //    单价格式化       "unit-price format" - a variable HOLDING THE FUNCTION NAME "FormatPrice"
    //                                        [docs:L139], which is what makes $$Invoke dynamic
    //    FormatPrice     the application macro both examples call [docs:L117, L141]
    // ==============================================================================================

    /// <summary>The macro both worked examples invoke [docs/n_cst_dwsvc_columnexp.md:L117, L141].</summary>
    private const string FormatPrice = "FormatPrice";

    /// <summary>
    /// The variable holding the callee name for the dynamic form
    /// [docs/n_cst_dwsvc_columnexp.md:L139 - <c>of_SetVar("单价格式化", "FormatPrice")</c>].
    /// </summary>
    private const string UnitPriceFormatVariable = "单价格式化";

    /// <summary>
    /// The precision variable [w_test_dwsvc_columnexp.srw:L109, docs/n_cst_dwsvc_columnexp.md:L115].
    /// </summary>
    private const string PrecisionVariable = "精度";

    /// <summary>
    /// The one-based row ordinal every invocation in this file is issued for - the legacy
    /// <c>row</c> parameter [se_cst_dw.sru:L14].
    /// </summary>
    /// <remarks>
    /// Deliberately not 1. A row of 1 would let a port that ignored the parameter entirely still pass,
    /// because 1 is also the first one-based ARGUMENT position and the default of a great many things.
    /// </remarks>
    private const long Row = 7L;

    /// <summary>
    /// The token the deferred, xUnit-supplied cancellation token is reached through.
    /// </summary>
    /// <remarks>
    /// Threaded into EVERY awaited call that accepts one. xUnit1051 is an ERROR here, not a warning,
    /// because <c>Directory.Build.props</c> sets <c>TreatWarningsAsErrors</c>; and it is the right
    /// thing regardless, since <see cref="MacroInvoker.InvokeAsync"/> waits on a client that may never
    /// answer.
    /// </remarks>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The column an expression is bound to - the legacy <c>dwo</c> parameter [se_cst_dw.sru:L14].
    /// </summary>
    /// <remarks>
    /// Named <c>n1</c> because that is the column the specification binds in both worked examples
    /// [docs:L117 <c>of_SetExp("n1", ...)</c>, L141], and the invalid-return message reports this name
    /// as <c>String(dwo.name)</c> [:L2282, :L2306] - so the fixture's name is observable output, not
    /// scaffolding.
    /// </remarks>
    private static FakeDataWindowObject BoundColumn() =>
        new("n1", FakeColumnType.Number, id: 1L);

    /// <summary>
    /// Builds a DIRECT application-macro reference - the port of a <c>funcdata</c> entry written with a
    /// SINGLE sigil, <c>$Func(args)</c> [docs/n_cst_dwsvc_columnexp.md:L110].
    /// </summary>
    /// <param name="name">The macro name, as the scanner recorded it in <c>funcdata.name</c>.</param>
    /// <param name="args">The argument source texts, as <c>funcdata.args</c> holds them.</param>
    /// <returns>The reference, with <c>Builtin</c> false because a single sigil is not <c>bDD</c>.</returns>
    /// <remarks>
    /// <c>FullName</c> is composed the way the scanner composes it at
    /// <c>n_cst_dwsvc_columnexp.sru:L1350</c> - <c>Mid(sExp, nMacPos, nFnPos - nMacPos + 1)</c>, the
    /// WHOLE macro text from its sigil through its closing bracket - because that is the field the
    /// <see cref="MacroSentinels.FUNC_VAR"/> arity message names [:L1390] and the difference from
    /// <c>Name</c> is exactly what one of the tests below asserts.
    /// </remarks>
    private static FunctionReference DirectReference(string name, params string[] args) =>
        new()
        {
            Name = name,
            FullName = "$" + name + "(" + string.Join(", ", args) + ")",
            Args = [.. args],
            Builtin = false,
        };

    /// <summary>
    /// Builds a BUILT-IN reference - the port of a <c>funcdata</c> entry written with a DOUBLED sigil,
    /// <c>$$Name(args)</c>, whose <c>builtin</c> flag is literally <c>bDD</c> [:L1290, :L1311].
    /// </summary>
    /// <param name="name">
    /// The name after the two sigils. <see cref="MacroSentinels.FUNC_INVOKE"/> and
    /// <see cref="MacroSentinels.FUNC_VAR"/> are the only two the oracle accepts [:L1386-L1401].
    /// </param>
    /// <param name="args">The argument source texts.</param>
    /// <returns>The reference, with <c>Builtin</c> true.</returns>
    private static FunctionReference BuiltinReference(string name, params string[] args) =>
        new()
        {
            Name = name,
            FullName = "$$" + name + "(" + string.Join(", ", args) + ")",
            Args = [.. args],
            Builtin = true,
        };

    /// <summary>
    /// Builds the dynamic-invoke reference of the specification's worked example
    /// [docs/n_cst_dwsvc_columnexp.md:L141 - <c>"$$Invoke($单价格式化, n2, $精度)"</c>].
    /// </summary>
    /// <returns>The reference, whose three arguments are the callee-name variable and two operands.</returns>
    private static FunctionReference DocumentedInvokeReference() =>
        BuiltinReference(
            MacroSentinels.FUNC_INVOKE,
            "$" + UnitPriceFormatVariable,
            "n2",
            "$" + PrecisionVariable);

    /// <summary>
    /// Builds the direct reference of the specification's worked example
    /// [docs/n_cst_dwsvc_columnexp.md:L117 - <c>"$FormatPrice(n2, $精度)"</c>].
    /// </summary>
    /// <returns>The reference, whose two arguments are the operand column and the precision variable.</returns>
    private static FunctionReference DocumentedDirectReference() =>
        DirectReference(FormatPrice, "n2", "$" + PrecisionVariable);

    /// <summary>
    /// Runs one dispatch plan through an invoker over the supplied channel and returns both the result
    /// and the channel, so a test can assert the ANSWER and the QUESTION in the same act.
    /// </summary>
    /// <param name="channel">The client-callback double.</param>
    /// <param name="plan">The plan to carry out.</param>
    /// <param name="dwo">The bound column, or <see langword="null"/> for the shared fixture column.</param>
    /// <returns>The invocation result.</returns>
    /// <remarks>
    /// The identifier factory is pinned so a failure message names a stable identifier: AAP 0.6.7 lists
    /// GUID generation among the determinism seams a characterization suite must substitute, and an
    /// identifier that appears in a recording cannot be random if paired recordings are to compare.
    /// </remarks>
    private static ValueTask<MacroInvocationResult> DispatchAsync(
        ScriptedMacroChannel channel,
        MacroDispatchPlan plan,
        FakeDataWindowObject? dwo = null) =>
        new MacroInvoker(
                channel,
                invocationTimeout: null,
                timeProvider: new DeterministicTimeProvider(),
                invocationIdFactory: () => "inv-1")
            .InvokeAsync(Row, dwo ?? BoundColumn(), plan, cancellationToken: Ct);

    // ==============================================================================================
    //  PHASE 1 - THE CONTRACT SHAPE AND THE ONE-BASED ARGUMENT LIST
    // ==============================================================================================

    /// <summary>
    /// The invocation carries exactly the four values <c>se_cst_dw.sru:L14</c> declares, and the answer
    /// is an unnarrowed <c>any</c>.
    /// </summary>
    [Fact]
    public async Task TheInvocationShapeMirrorsTheLegacyEventSignature()
    {
        // ORACLE [se_cst_dw.sru:L14]
        //   event type any oncolumnexpinvokemethod ( long row, dwobject dwo, string name,
        //                                            string args[] )
        // Four parameters in, one `any` out. AAP 0.4.5.2 maps `any` onto `object?`, so the returned
        // value must be able to carry a NUMBER - which is what the specification's own handler returns
        // [docs:L126 Round(Double(args[1]), Long(args[2]))].
        FakeDataWindowObject column = BoundColumn();
        ScriptedMacroChannel channel = new ScriptedMacroChannel().ScriptFormatPrice();

        MacroDispatchPlan plan = MacroInvoker.Plan(
            DocumentedDirectReference(),
            MacroArgumentList.FromEvaluated("12.345", "2"),
            "$FormatPrice(n2, $" + PrecisionVariable + ")",
            caretPosition: 7L);

        MacroInvocationResult result = await DispatchAsync(channel, plan, column);

        MacroInvocation invocation = Assert.Single(channel.Invocations);

        // `long row`
        Assert.Equal(Row, invocation.Row);

        // `dwobject dwo` - the HOST'S ACCESSOR SURFACE and not a live UI handle (C-D). The legacy
        // dwobject is a pointer into a Windows control; IDataWindowObject exposes only the members the
        // ported call sites actually read, and this protocol reads exactly one of them - Name, which the
        // invalid-return message needs as String(dwo.name) [:L2282, :L2306].
        IDataWindowObject dwo = Assert.IsAssignableFrom<IDataWindowObject>(invocation.Dwo);
        Assert.Same(column, dwo);
        Assert.Equal("n1", dwo.Name);

        // `string name`
        Assert.Equal(FormatPrice, invocation.Name);

        // `string args[]`
        Assert.Equal(2, invocation.Arguments.UpperBound);

        // `any` out, NOT narrowed to string: 12.345 rounded to 2 places is a double.
        Assert.Equal(MacroInvocationOutcome.Rendered, result.Outcome);
        Assert.IsType<double>(result.Value);
        Assert.Equal(12.35d, (double)result.Value!, precision: 10);
    }

    /// <summary>
    /// Position <b>1</b> is the FIRST argument, and position <b>0</b> is not an argument at all.
    /// </summary>
    [Fact]
    public async Task TheFirstArgumentArrivesAtPositionOneAndPositionZeroIsNotAnArgument()
    {
        // ORACLE - BOTH handlers, the documented one and the live one, read args[1] and args[2]:
        //   docs/n_cst_dwsvc_columnexp.md:L126          return Round(Double(args[1]),Long(args[2]))
        //   w_test_dwsvc_columnexp.srw:L305-L309        return Round(Double(args[1]),Long(args[2]))
        // AAP 0.4.5.4 / 0.8.6 R9: this is THE hazard. An off-by-one here shifts every macro argument
        // and still "works" for a one-argument call, so the negative assertion is not optional.
        ScriptedMacroChannel channel = new ScriptedMacroChannel().ScriptFormatPrice();

        MacroDispatchPlan plan = MacroInvoker.Plan(
            DocumentedDirectReference(),
            MacroArgumentList.FromEvaluated("first", "second"),
            "$FormatPrice(n2, $" + PrecisionVariable + ")",
            caretPosition: 7L);

        _ = await DispatchAsync(channel, plan);

        MacroInvocation invocation = Assert.Single(channel.Invocations);

        // POSITIVE: args[1] IS the first argument and args[2] IS the second.
        Assert.Equal("first", invocation.Arguments[1]);
        Assert.Equal("second", invocation.Arguments[2]);

        // NEGATIVE, AND THIS IS THE POINT OF THE TEST: position 0 is NOT the first argument. It is not
        // the first argument, it is not the empty string, and it is not a silently clamped read - it is
        // out of range, exactly as reading args[0] of a one-based PowerScript array is a runtime error.
        ArgumentOutOfRangeException zero = Assert.Throws<ArgumentOutOfRangeException>(
            () => invocation.Arguments[0]);
        Assert.Equal("oneBasedIndex", zero.ParamName);
        Assert.Equal(0, zero.ActualValue);

        // And the non-throwing reader agrees rather than offering a second, laxer convention.
        Assert.False(invocation.Arguments.TryGet(0, out string atZero));
        Assert.Equal(string.Empty, atZero);

        // The last valid position is UpperBound, mirroring PowerScript's UpperBound - which returns the
        // LAST VALID INDEX and not a length. One past it is out of range.
        Assert.Equal(2, invocation.Arguments.UpperBound);
        Assert.False(invocation.Arguments.TryGet(3, out _));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => invocation.Arguments[3]);
    }

    /// <summary>
    /// The one-based addressing goes through the CENTRALISED helper, not through arithmetic scattered
    /// across call sites.
    /// </summary>
    [Fact]
    public void TheArgumentListIndexesThroughTheSharedOneBasedHelper()
    {
        // AAP 0.8.6 R9 prescribes a centralised one-based indexing helper OR an individual audit of
        // every ported loop. MacroArgumentList.ToZeroBasedIndex is that helper, and this test pins two
        // things: that it exists with the legacy's own semantics, and that the type's OWN indexer is
        // expressed in terms of it rather than duplicating `- 1`.
        MacroArgumentList args = MacroArgumentList.FromEvaluated("a", "b", "c");

        // The helper's contract: one-based in, zero-based out.
        Assert.Equal(0, MacroArgumentList.ToZeroBasedIndex(1));
        Assert.Equal(1, MacroArgumentList.ToZeroBasedIndex(2));
        Assert.Equal(2, MacroArgumentList.ToZeroBasedIndex(3));

        // ... and its inverse, present so a translation in the other direction is also named.
        Assert.Equal(1, MacroArgumentList.ToOneBasedIndex(0));
        Assert.Equal(3, MacroArgumentList.ToOneBasedIndex(2));

        // THE AGREEMENT ASSERTION: for every valid position, the one-based indexer answers exactly what
        // the zero-based projection answers through the helper. If the indexer ever grew its own
        // arithmetic and drifted, this fails.
        for (int oneBased = 1; oneBased <= args.UpperBound; oneBased++)
        {
            Assert.Equal(
                args.Values[MacroArgumentList.ToZeroBasedIndex(oneBased)],
                args[oneBased]);
        }

        // The wire projection is the ONLY zero-based surface, and it is named for the convention it
        // hands out: contract C-04's `InvokeMethodRequest.args` is a protobuf repeated field.
        Assert.Equal(["a", "b", "c"], args.Values);

        // Enumeration carries no index, so it carries no ambiguity - the reason the type is
        // IEnumerable<string> and deliberately NOT IReadOnlyList<string>, whose indexer would be
        // contractually zero-based and would put two conventions on one type.
        Assert.Equal(["a", "b", "c"], [.. args]);
        Assert.IsNotAssignableFrom<IReadOnlyList<string>>(args);
    }

    /// <summary>
    /// A zero-argument macro is representable, and its argument collection is EMPTY rather than
    /// <see langword="null"/>.
    /// </summary>
    [Fact]
    public async Task AZeroArgumentInvocationCarriesAnEmptyArgumentCollectionAndNeverNull()
    {
        // ORACLE [:L2227] `sArgs = sEmptyArray` resets the list on every dispatch iteration, and
        // [:L1317] `fnData.args = sEmptyArray` initialises it during parsing. A PowerScript string
        // array is never null - it is empty - and only the two BUILT-IN forms carry an arity rule
        // [:L1386-L1401], so `$Refresh()` with no arguments at all is a legitimate application macro.
        ScriptedMacroChannel channel = new ScriptedMacroChannel()
            .ScriptValue("Refresh", "done");

        MacroDispatchPlan plan = MacroInvoker.Plan(
            DirectReference("Refresh"),
            MacroArgumentList.Empty,
            "$Refresh();",
            caretPosition: 5L);

        MacroInvocationResult result = await DispatchAsync(channel, plan);

        MacroInvocation invocation = Assert.Single(channel.Invocations);

        Assert.NotNull(invocation.Arguments);
        Assert.True(invocation.Arguments.IsEmpty);
        Assert.Equal(0, invocation.Arguments.UpperBound);
        Assert.Equal(0, invocation.Arguments.Count);
        Assert.Empty(invocation.Arguments);

        // Even empty, the list refuses position 0 - the convention does not soften when there is
        // nothing to read.
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => invocation.Arguments[0]);
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => invocation.Arguments[1]);

        // The empty list is the shared instance, which is the port of `sEmptyArray` being one value.
        Assert.Same(MacroArgumentList.Empty, invocation.Arguments);

        // And the call still happened and still produced a value: empty is not a refusal.
        Assert.Equal(MacroInvocationOutcome.Rendered, result.Outcome);
        Assert.Equal("'done'", result.Rendered);
    }

    /// <summary>
    /// Every way of building an argument list agrees, and none of them admits a null element.
    /// </summary>
    [Fact]
    public void EveryArgumentListFactoryProducesTheSameOneBasedList()
    {
        // A PowerScript `string[]` element is initialised to "" and there is no null element to hold,
        // so a null supplied here becomes "" - which is faithful rather than lenient, because "" is
        // precisely what the oracle would hold in that position.
        MacroArgumentList fromParams = MacroArgumentList.FromEvaluated("a", null, "c");
        MacroArgumentList fromSequence =
            MacroArgumentList.FromEvaluated((IEnumerable<string?>)["a", null, "c"]);
        MacroArgumentList fromImmutable =
            MacroArgumentList.FromEvaluated(ImmutableArray.Create("a", string.Empty, "c"));

        Assert.Equal(fromParams, fromSequence);
        Assert.Equal(fromParams, fromImmutable);
        Assert.Equal(string.Empty, fromParams[2]);

        // Absent input is the empty list, never null and never a one-element list holding null.
        Assert.Same(MacroArgumentList.Empty, MacroArgumentList.FromEvaluated((string?[]?)null));
        Assert.Same(MacroArgumentList.Empty, MacroArgumentList.FromEvaluated((IEnumerable<string?>?)null));
        Assert.Same(MacroArgumentList.Empty, MacroArgumentList.FromEvaluated(default(ImmutableArray<string>)));

        // Value equality, so a characterization recording can compare argument lists directly.
        Assert.Equal(
            MacroArgumentList.FromEvaluated("a", "b"),
            MacroArgumentList.FromEvaluated("a", "b"));
        Assert.NotEqual(
            MacroArgumentList.FromEvaluated("a", "b"),
            MacroArgumentList.FromEvaluated("b", "a"));
    }

    /// <summary>
    /// The argument list's VALUE SEMANTICS, which are what let a paired recording compare two argument
    /// lists at all.
    /// </summary>
    [Fact]
    public void TheArgumentListHasFullValueSemanticsAndEnumeratesNonGenerically()
    {
        MacroArgumentList first = MacroArgumentList.FromEvaluated("12.345", "1");
        MacroArgumentList second = MacroArgumentList.FromEvaluated("12.345", "1");
        MacroArgumentList different = MacroArgumentList.FromEvaluated("12.345", "2");

        // AAP 0.6.7's paired legacy/target recordings compare captured values, so equal contents MUST
        // hash equally - otherwise a recording lookup keyed on an argument list would miss.
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, different);

        // Length is part of the hash, so a prefix does not collide with the whole.
        Assert.NotEqual(
            first.GetHashCode(),
            MacroArgumentList.FromEvaluated("12.345").GetHashCode());

        // The reference-equality fast path, and the null comparand.
        MacroArgumentList same = first;
        Assert.True(first.Equals(same));
        Assert.False(first.Equals(null));

        // The empty list hashes consistently with itself, which matters because it is a SHARED instance.
        Assert.Equal(MacroArgumentList.Empty.GetHashCode(), MacroArgumentList.FromEvaluated().GetHashCode());

        // The non-generic enumerator, reached through the untyped collection interface - the surface a
        // reflection-driven serializer or an older assertion helper would take.
        System.Collections.IEnumerable untyped = first;
        List<object?> walked = [];

        foreach (object? argument in untyped)
        {
            walked.Add(argument);
        }

        Assert.Equal(["12.345", "1"], walked);
    }

    // ==============================================================================================
    //  PHASE 2 - THE TWO INVOCATION FORMS
    // ==============================================================================================

    /// <summary>
    /// DIRECT <c>$Func(args)</c>: the function name routes straight to the client macro and the value
    /// it returns flows back into the calculation.
    /// </summary>
    [Fact]
    public async Task TheDirectFormRoutesTheFunctionNameToTheClientAndTheAnswerFlowsBack()
    {
        // ORACLE - the documented example, verbatim:
        //   docs/n_cst_dwsvc_columnexp.md:L115  dw_1.ColumnExp.of_SetVar("精度", 1)
        //   docs/n_cst_dwsvc_columnexp.md:L117  of_SetExp("n1", "$FormatPrice(n2, $精度)")
        //   docs/n_cst_dwsvc_columnexp.md:L126  case "FormatPrice" return Round(Double(args[1]),Long(args[2]))
        // and the firing itself [:L2287]:
        //   aVal = #DataWindow.Event OnColumnExpInvokeMethod(row,dwo,fns[nFnIdx].name,sArgs)
        // So the name is the REFERENCE'S OWN name and the WHOLE evaluated list travels with it.
        ScriptedMacroChannel channel = new ScriptedMacroChannel().ScriptFormatPrice();

        // 精度 = 1, so 12.345 rounds to one decimal place.
        MacroDispatchPlan plan = MacroInvoker.Plan(
            DocumentedDirectReference(),
            MacroArgumentList.FromEvaluated("12.345", "1"),
            "$FormatPrice(n2, $" + PrecisionVariable + ");",
            caretPosition: 7L);

        Assert.Equal(MacroDispatchKind.ClientCallback, plan.Kind);
        Assert.Equal(MacroFunctionDispatch.Application, plan.Dispatch);
        Assert.Equal(ExpansionMode.MacroDirect, plan.Form);

        // A SINGLE sigil is not bDD, so this reference is NOT built-in [:L1290, :L1311] - which is why
        // its name was never compared against the two sentinels at all.
        Assert.False(plan.Builtin);

        MacroInvocationResult result = await DispatchAsync(channel, plan);

        // THE NAME REACHED THE CLIENT.
        MacroInvocation invocation = Assert.Single(channel.Invocations);
        Assert.Equal(FormatPrice, invocation.Name);
        Assert.Equal([FormatPrice], channel.InvokedNames);
        Assert.Equal(ExpansionMode.MacroDirect, invocation.Form);

        // THE WHOLE LIST TRAVELLED, one-based, in order.
        Assert.Equal(2, invocation.Arguments.UpperBound);
        Assert.Equal("12.345", invocation.Arguments[1]);
        Assert.Equal("1", invocation.Arguments[2]);

        // THE ANSWER FLOWED BACK INTO THE CALCULATION - as a value, as the rendered fragment, and as
        // the parenthesised fragment the oracle substitutes [:L2310].
        Assert.Equal(MacroInvocationOutcome.Rendered, result.Outcome);
        Assert.Equal(12.3d, Assert.IsType<double>(result.Value), precision: 10);
        Assert.Equal("12.3", result.Rendered);
        Assert.Equal("(12.3)", result.WrappedRendering);
        Assert.False(result.Aborts);
    }

    /// <summary>
    /// DYNAMIC <c>$$Invoke($nameVar, args)</c>: the callee name arrives as the FIRST argument and the
    /// client is called with the RESOLVED name.
    /// </summary>
    [Fact]
    public async Task TheDynamicFormResolvesTheCalleeNameFromTheFirstArgument()
    {
        // ORACLE - the documented example, verbatim:
        //   docs/n_cst_dwsvc_columnexp.md:L138  of_SetVar("精度", 1)
        //   docs/n_cst_dwsvc_columnexp.md:L139  of_SetVar("单价格式化", "FormatPrice")
        //   docs/n_cst_dwsvc_columnexp.md:L141  of_SetExp("n1", "$$Invoke($单价格式化, n2, $精度)")
        //   docs/n_cst_dwsvc_columnexp.md:L149  case "FormatPrice" return Round(Double(args[1]),Long(args[2]))
        // and the firing [:L2263]:
        //   aVal = #DataWindow.Event OnColumnExpInvokeMethod(row,dwo,sArgs[1],sArgs2)
        // sArgs[1] IS THE NAME. The handler's `choose case name` matches "FormatPrice" - the variable's
        // VALUE - and never "单价格式化" the variable, nor "Invoke" the sentinel.
        ScriptedMacroChannel channel = new ScriptedMacroChannel().ScriptFormatPrice();

        // The evaluated list: 单价格式化 evaluated to "FormatPrice" [docs:L139], then the two operands.
        MacroDispatchPlan plan = MacroInvoker.Plan(
            DocumentedInvokeReference(),
            MacroArgumentList.FromEvaluated(FormatPrice, "12.345", "1"),
            "$$Invoke($" + UnitPriceFormatVariable + ", n2, $" + PrecisionVariable + ");",
            caretPosition: 5L);

        Assert.Equal(MacroDispatchKind.ClientCallback, plan.Kind);
        Assert.Equal(MacroFunctionDispatch.DynamicInvoke, plan.Dispatch);
        Assert.Equal(ExpansionMode.MacroDynamic, plan.Form);

        // A DOUBLED sigil IS bDD, so this reference IS built-in - and that is the only reason its name
        // was compared against the sentinels and matched FUNC_INVOKE [:L1393].
        Assert.True(plan.Builtin);

        MacroInvocationResult result = await DispatchAsync(channel, plan);

        MacroInvocation invocation = Assert.Single(channel.Invocations);

        // THE RESOLVED NAME, and neither the sentinel nor the variable that held it.
        Assert.Equal(FormatPrice, invocation.Name);
        Assert.NotEqual(MacroSentinels.FUNC_INVOKE, invocation.Name);
        Assert.NotEqual(UnitPriceFormatVariable, invocation.Name);
        Assert.NotEqual("$" + UnitPriceFormatVariable, invocation.Name);

        // The client's handler ran under its own name and produced the documented value.
        Assert.Equal(MacroInvocationOutcome.Rendered, result.Outcome);
        Assert.Equal(12.3d, Assert.IsType<double>(result.Value), precision: 10);
        Assert.Equal("(12.3)", result.WrappedRendering);
    }

    /// <summary>
    /// The dynamic form RE-BASES the remaining arguments: the client sees the first real argument at
    /// position 1, not the callee name.
    /// </summary>
    [Fact]
    public async Task TheDynamicFormRebasesTheRemainingArgumentsFromOne()
    {
        // ORACLE [:L2259-L2262]
        //   sArgs2 = sEmptyArray
        //   for nFnArgIdx = 2 to nFnArgCnt
        //       sArgs2[nFnArgIdx - 1] = sArgs[nFnArgIdx]
        //   next
        // THE SECOND ONE-BASED HAZARD IN THIS FILE (AAP 0.4.5.4 / 0.8.6 R9). A port that forwarded the
        // full list would hand the callee NAME to the handler as args[1], and every client macro would
        // then read its arguments one position out - while still returning a plausible number. The
        // documented handler would compute Round(Double("FormatPrice"), Long("12.345")), which parses
        // to Round(0, 12) - a wrong answer that looks like an answer.
        ScriptedMacroChannel channel = new ScriptedMacroChannel()
            .Script(FormatPrice, MacroScripts.EchoArgumentsScript);

        MacroDispatchPlan plan = MacroInvoker.Plan(
            BuiltinReference(MacroSentinels.FUNC_INVOKE, "$" + UnitPriceFormatVariable, "a", "b"),
            MacroArgumentList.FromEvaluated(FormatPrice, "a", "b"),
            "$$Invoke($" + UnitPriceFormatVariable + ", a, b);",
            caretPosition: 5L);

        // The plan already carries the shifted list, so the shift is assertable with no channel at all.
        Assert.Equal(FormatPrice, plan.Name);
        Assert.Equal(2, plan.Arguments.UpperBound);
        Assert.Equal("a", plan.Arguments[1]);
        Assert.Equal("b", plan.Arguments[2]);

        MacroInvocationResult result = await DispatchAsync(channel, plan);

        MacroInvocation invocation = Assert.Single(channel.Invocations);

        // args[1] = a AND args[2] = b - the shift, asserted positionally.
        Assert.Equal("a", invocation.Arguments[1]);
        Assert.Equal("b", invocation.Arguments[2]);

        // THE NAME IS NOT AT POSITION 1. Stated as its own assertion because "args[1] = a" alone would
        // also hold for a list that had merely been reordered.
        Assert.NotEqual(FormatPrice, invocation.Arguments[1]);

        // And it is nowhere in the forwarded list at all: the name was REMOVED, not moved.
        Assert.DoesNotContain(FormatPrice, invocation.Arguments);
        Assert.Equal(2, invocation.Arguments.UpperBound);

        // The echo double read positions 1..UpperBound, so its answer is independent proof of what the
        // client actually saw.
        Assert.Equal("'a,b'", result.Rendered);

        // The list-level helper behind the shift, pinned directly: DropFirst renumbers from 1 and
        // answers the shared empty list when there is nothing left [:L2259-L2262 with nFnArgCnt = 1].
        MacroArgumentList shifted = MacroArgumentList.FromEvaluated(FormatPrice, "a", "b").DropFirst();
        Assert.Equal(MacroArgumentList.FromEvaluated("a", "b"), shifted);
        Assert.Same(MacroArgumentList.Empty, MacroArgumentList.FromEvaluated("only").DropFirst());
        Assert.Same(MacroArgumentList.Empty, MacroArgumentList.Empty.DropFirst());
    }

    /// <summary>
    /// A single-argument <c>$$Invoke</c> is legal and forwards NO arguments - the boundary of the
    /// re-basing loop.
    /// </summary>
    [Fact]
    public async Task TheDynamicFormWithOnlyACalleeNameForwardsAnEmptyArgumentList()
    {
        // ORACLE [:L2260] `for nFnArgIdx = 2 to nFnArgCnt` does not execute when nFnArgCnt = 1, so
        // sArgs2 stays sEmptyArray - and [:L1394] permits exactly this, because FUNC_INVOKE requires AT
        // LEAST one argument rather than at least two. `$$Invoke($单价格式化)` is therefore a
        // zero-argument dynamic call, not a malformed one.
        ScriptedMacroChannel channel = new ScriptedMacroChannel()
            .Script(FormatPrice, MacroScripts.EchoArgumentsScript);

        MacroDispatchPlan plan = MacroInvoker.Plan(
            BuiltinReference(MacroSentinels.FUNC_INVOKE, "$" + UnitPriceFormatVariable),
            MacroArgumentList.FromEvaluated(FormatPrice),
            "$$Invoke($" + UnitPriceFormatVariable + ");",
            caretPosition: 5L);

        Assert.Equal(MacroDispatchKind.ClientCallback, plan.Kind);
        Assert.Equal(FormatPrice, plan.Name);
        Assert.True(plan.Arguments.IsEmpty);

        MacroInvocationResult result = await DispatchAsync(channel, plan);

        MacroInvocation invocation = Assert.Single(channel.Invocations);
        Assert.Equal(FormatPrice, invocation.Name);
        Assert.Empty(invocation.Arguments);
        Assert.Equal(MacroInvocationOutcome.Rendered, result.Outcome);

        // The echo double joined nothing, so the string arm rendered an empty quoted literal.
        Assert.Equal("''", result.Rendered);
    }

    /// <summary>
    /// The three LIVE scenario shapes the oracle's own test window binds
    /// [w_test_dwsvc_columnexp.srw:L151, L154, L157].
    /// </summary>
    /// <returns>The expression text, its evaluated argument pair, the expected value, and the locator.</returns>
    /// <remarks>
    /// ALL THREE ARE THE DIRECT FORM AT THE MACRO LEVEL, AND THAT IS THE FINDING THIS MATRIX RECORDS.
    /// What differs between them is how the ARGUMENTS expand - statically, dynamically, or
    /// dynamic-indirectly - and expansion happens BEFORE dispatch [:L2229-L2236 evaluates each argument
    /// through <c>_of_Evaluate</c>]. By the time the invoker runs, all three present the client with the
    /// same shape and differ only in the VALUES carried. That is precisely why the arguments crossing
    /// the boundary must be evaluated values rather than unexpanded reference text: the text would make
    /// the three distinguishable to a client that has no variable table to resolve them with.
    /// </remarks>
    public static TheoryData<string, string, string, double, string> LiveScenarioMatrix() => new()
    {
        // ---- 静态展开【精度】- the precision variable substituted AT BIND TIME.
        {
            "$FormatPrice($应收-$未收,$精度)",
            "12.345",
            "1",
            12.3d,
            "w_test_dwsvc_columnexp.srw:L151 - of_SetExp(\"n2\", ...), static $精度"
        },

        // ---- 动态展开【精度】- the same variable retained as a REFERENCE and resolved at calculation
        //      time. The macro form is unchanged; only the argument's expansion mode differs.
        {
            "$FormatPrice($未收+$实收,$$精度)",
            "12.345",
            "2",
            12.35d,
            "w_test_dwsvc_columnexp.srw:L154 - of_SetExp(\"n3\", ...), dynamic $$精度"
        },

        // ---- 动态获取【精度】- the variable NAME is itself computed, through the FUNC_VAR form
        //      $$('精度') nested inside the macro's argument list, and the whole macro result is then
        //      added to a dynamic foreign variable.
        {
            "$FormatPrice(n3-n2,$$('精度'))+$$数据汇总",
            "12.345",
            "0",
            12d,
            "w_test_dwsvc_columnexp.srw:L157 - of_SetExp(\"n1\", ...), $$('精度') nested"
        },
    };

    /// <summary>
    /// Each live scenario shape reaches the client as the direct form with its arguments already
    /// evaluated.
    /// </summary>
    /// <param name="expression">The expression text bound in the oracle's test window.</param>
    /// <param name="amount">The evaluated first argument.</param>
    /// <param name="precision">The evaluated second argument - whatever 精度 resolved to.</param>
    /// <param name="expected">The value the live handler returns for that pair.</param>
    /// <param name="locator">The oracle locator for this row.</param>
    [Theory]
    [MemberData(nameof(LiveScenarioMatrix))]
    public async Task EveryLiveScenarioShapeReachesTheClientAsTheDirectForm(
        string expression,
        string amount,
        string precision,
        double expected,
        string locator)
    {
        Assert.False(string.IsNullOrWhiteSpace(locator));

        // The live handler [w_test_dwsvc_columnexp.srw:L305-L309] is exactly the documented one:
        //   case "FormatPrice" return Round(Double(args[1]),Long(args[2]))
        ScriptedMacroChannel channel = new ScriptedMacroChannel().ScriptFormatPrice();

        MacroDispatchPlan plan = MacroInvoker.Plan(
            DocumentedDirectReference(),
            MacroArgumentList.FromEvaluated(amount, precision),
            expression,
            caretPosition: 7L);

        // Same form, same dispatch arm, for all three shapes.
        Assert.Equal(MacroFunctionDispatch.Application, plan.Dispatch);
        Assert.Equal(ExpansionMode.MacroDirect, plan.Form);

        MacroInvocationResult result = await DispatchAsync(channel, plan);

        MacroInvocation invocation = Assert.Single(channel.Invocations);
        Assert.Equal(FormatPrice, invocation.Name);

        // The client received VALUES, one-based, and no trace of the expression text they came from.
        Assert.Equal(amount, invocation.Arguments[1]);
        Assert.Equal(precision, invocation.Arguments[2]);
        Assert.DoesNotContain("$", invocation.Arguments[1], StringComparison.Ordinal);
        Assert.DoesNotContain("$", invocation.Arguments[2], StringComparison.Ordinal);

        Assert.Equal(MacroInvocationOutcome.Rendered, result.Outcome);
        Assert.Equal(expected, Assert.IsType<double>(result.Value), precision: 10);
        Assert.Equal(
            "(" + expected.ToString(CultureInfo.InvariantCulture) + ")",
            result.WrappedRendering);
    }

    /// <summary>
    /// The <see cref="MacroSentinels.FUNC_VAR"/> form <c>$$('name')</c> is a VARIABLE lookup and NEVER
    /// reaches the client.
    /// </summary>
    [Fact]
    public async Task TheVariableFormResolvesLocallyAndNeverTouchesTheClientChannel()
    {
        // ORACLE [:L2239-L2240]
        //   case FUNC_VAR
        //       nGVarIdx = _of_FindVarIndex(sArgs[1])
        // The oracle fires OnColumnExpInvokeMethod on the FUNC_INVOKE arm [:L2263] and on the
        // non-built-in path [:L2287] - and on NEITHER of those is this arm. Forwarding it would invent a
        // callback the application has no handler for, and the client would answer Unhandled, turning a
        // legitimate variable reference into a refusal.
        //
        // The channel below has no scripts at all, so any name that reached it would come back
        // Unhandled - which makes "the channel was never touched" and "the result is a variable lookup"
        // two independent proofs of the same routing decision.
        ScriptedMacroChannel channel = new();

        MacroDispatchPlan plan = MacroInvoker.Plan(
            BuiltinReference(MacroSentinels.FUNC_VAR, "'" + PrecisionVariable + "'"),
            MacroArgumentList.FromEvaluated(PrecisionVariable),
            "$$('" + PrecisionVariable + "');",
            caretPosition: 4L);

        Assert.Equal(MacroDispatchKind.VariableLookup, plan.Kind);
        Assert.Equal(MacroFunctionDispatch.VariableLookup, plan.Dispatch);
        Assert.Equal(ExpansionMode.DynamicIndirect, plan.Form);
        Assert.Equal(PrecisionVariable, plan.VariableName);

        // A variable lookup carries no name and no arguments to forward.
        Assert.Equal(string.Empty, plan.Name);
        Assert.Same(MacroArgumentList.Empty, plan.Arguments);

        MacroInvoker invoker = new(channel, invocationIdFactory: () => "inv-1");
        MacroInvocationResult result = await invoker.InvokeAsync(
            Row,
            BoundColumn(),
            plan,
            cancellationToken: Ct);

        Assert.Equal(MacroInvocationOutcome.VariableLookup, result.Outcome);
        Assert.Equal(PrecisionVariable, result.VariableName);

        // A variable lookup is NOT an abort: it hands off to the engine's own resolution, which has
        // abort conditions of its own [:L2243, :L2255].
        Assert.False(result.Aborts);
        Assert.Null(result.Error);
        Assert.Null(result.ReturnCode);

        // THE CHANNEL WAS NEVER TOUCHED, and no invocation was even issued.
        Assert.Equal(0, channel.InvocationCount);
        Assert.Empty(channel.Invocations);
        Assert.Null(result.InvocationId);
        Assert.Null(result.Sequence);
        Assert.Equal(0L, invoker.IssuedInvocationCount);
    }

    // ==============================================================================================
    //  PHASE 3 - THE SENTINELS, THE bDD DISCRIMINATION AND THE ARITY RULES
    // ==============================================================================================

    /// <summary>
    /// The two sentinels carry the oracle's values, and classification is driven by THEM rather than by
    /// a re-typed spelling.
    /// </summary>
    [Fact]
    public void TheTwoSentinelsCarryTheOraclesValuesAndDriveClassification()
    {
        // ORACLE [n_cst_dwsvc_columnexp.sru:L120-L121]
        //   constant string FUNC_VAR    = ""
        //   constant string FUNC_INVOKE = "Invoke"
        // The empty string is a LIVE SENTINEL, not a placeholder: `$$(` is a doubled sigil immediately
        // followed by a bracket, so the text between them is empty and funcdata.name comes out as ""
        // [:L1313]. It still carries a real arity rule [:L1389] and a real dispatch arm [:L2239].
        //
        // Asserted through Empty/Same rather than Equal because the xunit analyser refuses a comparison
        // between two constants - and Same is the stronger statement anyway: the sentinel IS the interned
        // empty literal and not merely a zero-length string that happens to compare equal.
        Assert.Empty(MacroSentinels.FUNC_VAR);
        Assert.Same(string.Empty, MacroSentinels.FUNC_VAR);
        Assert.Equal("Invoke", MacroSentinels.FUNC_INVOKE);

        // The predicates, including the null asymmetry the two deliberately do not share: a PowerScript
        // structure member initialises to "" and never to null, so a null Name is a PORT-SIDE absence
        // rather than the oracle's empty-name case, and routing it into the variable arm would be wrong.
        Assert.True(MacroSentinels.IsFuncVar(MacroSentinels.FUNC_VAR));
        Assert.False(MacroSentinels.IsFuncVar(null));
        Assert.False(MacroSentinels.IsFuncVar(" "));
        Assert.True(MacroSentinels.IsFuncInvoke(MacroSentinels.FUNC_INVOKE));
        Assert.False(MacroSentinels.IsFuncInvoke(null));

        // ORDINAL AND CASE-SENSITIVE, because PowerScript's `choose case` on strings [:L1387] is. A
        // case-insensitive port would ACCEPT two spellings the oracle refuses as undefined [:L1399],
        // which is a behaviour change dressed up as leniency (C-B).
        Assert.False(MacroSentinels.IsFuncInvoke("invoke"));
        Assert.False(MacroSentinels.IsFuncInvoke("INVOKE"));

        // REFERENCED, NOT RE-TYPED: feeding the CONSTANT into classification selects the dynamic arm,
        // while every near-miss spelling is refused. A local copy of "Invoke" in this test would let the
        // test agree with itself while drifting from the engine; using the constant makes that
        // impossible, and the case variants below prove the comparison is the constant's own.
        Assert.True(TryClassifyByName(MacroSentinels.FUNC_INVOKE, out MacroFunctionDispatch invoke, out _));
        Assert.Equal(MacroFunctionDispatch.DynamicInvoke, invoke);

        Assert.True(TryClassifyByName(MacroSentinels.FUNC_VAR, out MacroFunctionDispatch variable, out _));
        Assert.Equal(MacroFunctionDispatch.VariableLookup, variable);

        foreach (string nearMiss in (string[])["invoke", "INVOKE", "Invoke ", " Invoke", "Invoke2"])
        {
            Assert.False(
                TryClassifyByName(nearMiss, out _, out ExpressionParseError? refusal),
                "A doubled-sigil name of '" + nearMiss + "' must be refused as undefined [:L1399].");
            Assert.NotNull(refusal);
            Assert.Equal(ExpressionErrorSite.FunctionMacroUndefined, refusal.Site);
        }
    }

    /// <summary>
    /// Classifies a BUILT-IN reference carrying exactly one argument, which satisfies both sentinels'
    /// arity rules, so the outcome depends only on the NAME.
    /// </summary>
    /// <param name="name">The name after the two sigils.</param>
    /// <param name="dispatch">The arm selected, when accepted.</param>
    /// <param name="error">The refusal, when not.</param>
    /// <returns><see langword="true"/> when the name is one of the two sentinels.</returns>
    private static bool TryClassifyByName(
        string name,
        out MacroFunctionDispatch dispatch,
        out ExpressionParseError? error) =>
        MacroInvoker.TryClassify(
            BuiltinReference(name, "'x'"),
            ExpansionSigils.DynamicMacro,
            "$$" + name + "('x');",
            caretPosition: 4L,
            out dispatch,
            out error);

    /// <summary>
    /// <c>funcdata.builtin</c> IS <c>bDD</c> - the doubled-sigil flag - and it is the single bit that
    /// separates an application macro from a checked built-in.
    /// </summary>
    [Fact]
    public void TheBuiltinFlagIsTheDoubledSigilFlagAndNothingElse()
    {
        // ORACLE [:L1290] bDD = (Mid(sExp,nPos + 1,1) = MACRO_FLAG)   MACRO_FLAG = "$" [:L1252]
        //        [:L1311] fnData.builtIn = bDD
        // So "built-in" does not mean "the engine implements it"; it means "written with TWO dollar
        // signs". That one line is why $Func(...) and $$Func(...) behave completely differently.

        // $$Invoke(...) - DOUBLED. Built-in, so the name IS checked, and it matches FUNC_INVOKE.
        Assert.True(MacroInvoker.TryClassify(
            BuiltinReference(MacroSentinels.FUNC_INVOKE, "$" + UnitPriceFormatVariable),
            ExpansionSigils.DynamicMacro,
            "$$Invoke($" + UnitPriceFormatVariable + ");",
            caretPosition: 5L,
            out MacroFunctionDispatch doubled,
            out ExpressionParseError? doubledError));
        Assert.Equal(MacroFunctionDispatch.DynamicInvoke, doubled);
        Assert.Null(doubledError);

        // $FormatPrice(...) - SINGLE. Not built-in, so the name is never compared against anything and
        // the application owns the implementation [:L2286-L2287].
        Assert.True(MacroInvoker.TryClassify(
            DocumentedDirectReference(),
            ExpansionSigils.StaticMacro,
            "$FormatPrice(n2, $" + PrecisionVariable + ");",
            caretPosition: 7L,
            out MacroFunctionDispatch single,
            out ExpressionParseError? singleError));
        Assert.Equal(MacroFunctionDispatch.Application, single);
        Assert.Null(singleError);

        // $$FormatPrice(...) - THE SAME NAME, DOUBLED, IS REFUSED. This is the closed-set consequence of
        // the flag [:L1398-L1400]: only $$(...) and $$Invoke(...) are legal doubled function forms.
        Assert.False(MacroInvoker.TryClassify(
            BuiltinReference(FormatPrice, "n2"),
            ExpansionSigils.DynamicMacro,
            "$$FormatPrice(n2);",
            caretPosition: 7L,
            out _,
            out ExpressionParseError? refused));
        Assert.NotNull(refused);
        Assert.Equal(ExpressionErrorSite.FunctionMacroUndefined, refused.Site);

        // ... AND THE MIRROR CASE, which is the one that makes the sentinel a real limitation rather
        // than a curiosity: an application macro genuinely NAMED "Invoke" is reachable through the
        // SINGLE-sigil form, because Builtin is false there and the sentinels are never consulted.
        Assert.True(MacroInvoker.TryClassify(
            DirectReference(MacroSentinels.FUNC_INVOKE, "n2"),
            ExpansionSigils.StaticMacro,
            "$Invoke(n2);",
            caretPosition: 4L,
            out MacroFunctionDispatch applicationInvoke,
            out _));
        Assert.Equal(MacroFunctionDispatch.Application, applicationInvoke);

        // SigilsFor recovers the flag at dispatch time, when the scanner's bits are gone: IsDynamic is
        // exactly funcdata.builtin, IsMacro is always true because only the macro arm creates a
        // funcdata, and IsCtx is always false because a context function macro was already refused
        // [:L1306-L1310] and so cannot exist to be dispatched.
        ExpansionSigils recoveredDoubled = MacroInvoker.SigilsFor(
            BuiltinReference(MacroSentinels.FUNC_INVOKE, "x"));
        Assert.True(recoveredDoubled.IsDynamic);
        Assert.True(recoveredDoubled.IsMacro);
        Assert.False(recoveredDoubled.IsCtx);

        ExpansionSigils recoveredSingle = MacroInvoker.SigilsFor(DirectReference(FormatPrice, "x"));
        Assert.False(recoveredSingle.IsDynamic);
        Assert.True(recoveredSingle.IsMacro);
        Assert.False(recoveredSingle.IsCtx);
    }

    /// <summary>
    /// The name extraction skips TWO sigil characters for the doubled form and ONE for the single form,
    /// and applying the wrong arm's arithmetic is observable.
    /// </summary>
    [Fact]
    public void TheNameExtractionSkipsTwoSigilsForBuiltInsAndOneForApplicationMacros()
    {
        // ORACLE [:L1311-L1316]
        //   fnData.builtIn = bDD
        //   if fnData.builtIn then
        //       fnData.name = Trim(Mid(sExp,nMacPos + 2,nPos - nMacPos - 2))   <- TWO
        //   else
        //       fnData.name = Trim(Mid(sExp,nMacPos + 1,nPos - nMacPos - 1))   <- ONE
        //   end if
        // The scanner owns that arithmetic (ColumnExpressionEngine); this test states the two formulae
        // as EXPECTATIONS and asserts the CONSEQUENCE through the engine's own classifier, which is what
        // makes the off-by-one visible instead of merely arithmetic.
        //
        // nMacPos is the position of the FIRST sigil [:L1286 nMacPos = nPos], and nPos is the position of
        // the opening bracket, both ONE-BASED into the trailer-extended expression [:L1262].

        const string doubledForm = "$$Invoke('x');";
        const long doubledMacroStart = 1L;
        const long doubledBracket = 9L; // "$$Invoke" is 8 characters, so "(" is the 9th.

        // The CORRECT arm for a doubled sigil skips two characters and yields the sentinel exactly.
        string correctDoubled = LegacyMid(
            doubledForm,
            doubledMacroStart + 2L,
            doubledBracket - doubledMacroStart - 2L);
        Assert.Equal(MacroSentinels.FUNC_INVOKE, correctDoubled);

        // The WRONG arm - the single-sigil arithmetic applied to a doubled token - keeps the second
        // sigil, and the resulting name matches NEITHER sentinel.
        string wrongDoubled = LegacyMid(
            doubledForm,
            doubledMacroStart + 1L,
            doubledBracket - doubledMacroStart - 1L);
        Assert.Equal("$Invoke", wrongDoubled);
        Assert.NotEqual(MacroSentinels.FUNC_INVOKE, wrongDoubled);

        // THE OBSERVABLE CONSEQUENCE: the correct extraction dispatches dynamically, the off-by-one is
        // refused as an undefined function [:L1399]. For a BUILT-IN the mistake is therefore LOUD.
        Assert.True(TryClassifyByName(correctDoubled, out MacroFunctionDispatch correctArm, out _));
        Assert.Equal(MacroFunctionDispatch.DynamicInvoke, correctArm);

        Assert.False(TryClassifyByName(wrongDoubled, out _, out ExpressionParseError? wrongError));
        Assert.NotNull(wrongError);
        Assert.Equal(ExpressionErrorSite.FunctionMacroUndefined, wrongError.Site);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, wrongError.ReturnCode);

        const string singleForm = "$FormatPrice(n2);";
        const long singleMacroStart = 1L;
        const long singleBracket = 13L; // "$FormatPrice" is 12 characters.

        // The CORRECT arm for a single sigil skips one character.
        string correctSingle = LegacyMid(
            singleForm,
            singleMacroStart + 1L,
            singleBracket - singleMacroStart - 1L);
        Assert.Equal(FormatPrice, correctSingle);

        // The WRONG arm eats the macro's first LETTER.
        string wrongSingle = LegacyMid(
            singleForm,
            singleMacroStart + 2L,
            singleBracket - singleMacroStart - 2L);
        Assert.Equal("ormatPrice", wrongSingle);

        // AND FOR AN APPLICATION MACRO THE SAME MISTAKE IS SILENT, which is why the skip count is
        // asserted rather than assumed: the name is never validated on this path [:L2286-L2287], so a
        // mangled name is forwarded to the client and simply fails to match its `choose case`, arriving
        // as an unhandled macro instead of as a parse error.
        Assert.True(MacroInvoker.TryClassify(
            DirectReference(wrongSingle, "n2"),
            ExpansionSigils.StaticMacro,
            singleForm,
            caretPosition: 7L,
            out MacroFunctionDispatch mangledArm,
            out ExpressionParseError? mangledError));
        Assert.Equal(MacroFunctionDispatch.Application, mangledArm);
        Assert.Null(mangledError);

        // The two arms differ by EXACTLY one character position, which is the whole content of the rule.
        Assert.Equal(correctDoubled.Length + 1, wrongDoubled.Length);
        Assert.Equal(correctSingle.Length - 1, wrongSingle.Length);
    }

    /// <summary>
    /// PowerScript's <c>Mid</c>, stated so the extraction expectations above read as the oracle's own
    /// arithmetic.
    /// </summary>
    /// <param name="value">The trailer-extended expression text.</param>
    /// <param name="oneBasedStart">The one-based start position.</param>
    /// <param name="length">The number of characters to take.</param>
    /// <returns>The substring, trimmed as <c>Trim(Mid(...))</c> trims it.</returns>
    /// <remarks>
    /// THIS IS AN EXPECTATION, NOT AN IMPLEMENTATION, and the distinction matters: nothing in the
    /// production path calls it, no test uses it to compute an ACTUAL value, and it exists only so the
    /// two <c>Mid</c> formulae at <c>:L1313</c> and <c>:L1315</c> can be written in the test as the
    /// oracle writes them instead of as pre-computed string literals a reader would have to verify by
    /// hand. It is one-based because <c>Mid</c> is.
    /// </remarks>
    private static string LegacyMid(string value, long oneBasedStart, long length) =>
        value.Substring((int)oneBasedStart - 1, (int)length).Trim();

    /// <summary>
    /// Every arity and name refusal in the built-in switch, with the field its message names.
    /// </summary>
    /// <returns>The name, the argument count, the expected site, which field the message names, and the locator.</returns>
    /// <remarks>
    /// THE TWO ARITY MESSAGES ARE CHARACTER-FOR-CHARACTER IDENTICAL TEMPLATES THAT NAME DIFFERENT
    /// FIELDS, and that is exactly why they are easy to lose: <c>:L1390</c> formats
    /// <c>fnData.fullName</c> - the whole macro text, sigils and argument list included - while
    /// <c>:L1395</c> formats <c>fnData.name</c>, which on that arm is the bare word <c>Invoke</c>. A port
    /// that unified them would change observable output text (C-B), so the rows below discriminate on
    /// the field and the test asserts the resolved value.
    /// </remarks>
    public static TheoryData<string, int, ExpressionErrorSite, string, string> ArityRefusalMatrix() => new()
    {
        // ---- FUNC_VAR requires EXACTLY one argument: `UpperBound(fnData.args) <> 1` [:L1389].
        {
            "",
            0,
            ExpressionErrorSite.FunctionMacroArgumentCountIncorrectByFullName,
            "fullName",
            "n_cst_dwsvc_columnexp.sru:L1388-L1391 - $$() with no argument"
        },
        {
            "",
            2,
            ExpressionErrorSite.FunctionMacroArgumentCountIncorrectByFullName,
            "fullName",
            "n_cst_dwsvc_columnexp.sru:L1388-L1391 - $$('a','b'), one too many"
        },
        {
            "",
            5,
            ExpressionErrorSite.FunctionMacroArgumentCountIncorrectByFullName,
            "fullName",
            "n_cst_dwsvc_columnexp.sru:L1389 - <> 1 refuses ANY count but one"
        },

        // ---- FUNC_INVOKE requires AT LEAST one: `UpperBound(fnData.args) < 1` [:L1394]. Only zero
        //      fails, because the single argument it insists on is the callee NAME.
        {
            "Invoke",
            0,
            ExpressionErrorSite.FunctionMacroArgumentCountIncorrectByName,
            "name",
            "n_cst_dwsvc_columnexp.sru:L1393-L1396 - $$Invoke() with no callee name"
        },

        // ---- Any OTHER doubled-sigil name is undefined [:L1398-L1400], whatever its arity.
        {
            "Other",
            1,
            ExpressionErrorSite.FunctionMacroUndefined,
            "name",
            "n_cst_dwsvc_columnexp.sru:L1399 - $$Other('x'), case else"
        },
        {
            "FormatPrice",
            2,
            ExpressionErrorSite.FunctionMacroUndefined,
            "name",
            "n_cst_dwsvc_columnexp.sru:L1399 - $$FormatPrice(a,b): a real macro, wrongly doubled"
        },
    };

    /// <summary>
    /// Each built-in refusal reports its own site, names its own field, and carries
    /// <c>RetCode.E_INVALID_ARGUMENT</c> with the midpoint caret.
    /// </summary>
    /// <param name="name">The name after the two sigils.</param>
    /// <param name="argumentCount">How many arguments the reference declares.</param>
    /// <param name="site">The expected error site, whose value IS the oracle line number.</param>
    /// <param name="namedField">Which <c>funcdata</c> field the message formats.</param>
    /// <param name="locator">The oracle locator for this row.</param>
    [Theory]
    [MemberData(nameof(ArityRefusalMatrix))]
    public void EveryBuiltInRefusalNamesItsOwnFieldAndCarriesInvalidArgument(
        string name,
        int argumentCount,
        ExpressionErrorSite site,
        string namedField,
        string locator)
    {
        Assert.False(string.IsNullOrWhiteSpace(locator));

        string[] args = [.. Enumerable
            .Range(1, argumentCount)
            .Select(index => "'a" + index.ToString(CultureInfo.InvariantCulture) + "'")];

        FunctionReference reference = BuiltinReference(name, args);
        string syntax = reference.FullName + ";";

        // The midpoint the scanner would have computed for this token, using the oracle's own formula
        // with its INTEGER division [:L1390 and every other caret-bearing macro site].
        const long macroStart = 1L;
        long bracketPosition = 3L + name.Length;
        long midpoint = MacroTokenMidpoint(macroStart, bracketPosition);

        Assert.False(MacroInvoker.TryClassify(
            reference,
            ExpansionSigils.DynamicMacro,
            syntax,
            midpoint,
            out _,
            out ExpressionParseError? error));

        Assert.NotNull(error);
        Assert.Equal(site, error.Site);

        // The site's VALUE is the oracle line, so the locator is machine-readable rather than a comment.
        Assert.Equal((int)site, error.LegacyLine);

        // EVERY REJECTION CARRIES E_INVALID_ARGUMENT [:L1391, :L1396, :L1400].
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, error.ReturnCode);

        // The caret POSITION passed through unchanged, under the midpoint convention. The arithmetic
        // itself belongs to ParseErrorFormatterTests.cs; what is asserted here is the passthrough.
        Assert.Equal(midpoint, error.CaretPosition);
        Assert.Equal(CaretPositionConvention.MacroTokenMidpoint, error.CaretConvention);
        Assert.Equal(syntax, error.Expression);

        // THE FIELD THE MESSAGE NAMES - the distinction a unifying port would erase.
        string expected = namedField switch
        {
            "fullName" => reference.FullName,
            "name" => reference.Name,
            _ => throw new ArgumentOutOfRangeException(
                nameof(namedField),
                namedField,
                "The oracle's two arity messages name either fnData.fullName [:L1390] or fnData.name "
                    + "[:L1395]; there is no third field."),
        };

        Assert.Equal([expected], error.FormatArguments);
        Assert.Contains("[" + expected + "]", error.Text, StringComparison.Ordinal);

        // ... and the two are genuinely different strings on the FUNC_VAR arm, so the assertion above is
        // discriminating rather than incidentally true.
        if (site == ExpressionErrorSite.FunctionMacroArgumentCountIncorrectByFullName)
        {
            Assert.NotEqual(reference.Name, reference.FullName);
            Assert.StartsWith("$$", expected, StringComparison.Ordinal);
        }

        // Not localized, and that inconsistency is preserved rather than harmonised (C-B): the
        // equivalent messages elsewhere in the DataWindow service layer DO route through I18N.
        Assert.False(error.Localized);
        Assert.Equal(0L, error.LocalizationCategory);
        Assert.Equal(ExpressionErrorSeverity.StopSign, error.Severity);
        Assert.Equal(ExpressionErrorCategory.Parse, error.Category);
        Assert.Equal(ExpressionErrorFamily.CaretBearingParseError, error.Family);
    }

    /// <summary>
    /// The two arity messages share one template and differ only in the value substituted into it.
    /// </summary>
    [Fact]
    public void TheTwoArityMessagesShareOneTemplateAndDifferOnlyInTheValueTheyName()
    {
        // ORACLE - byte-identical templates, three lines apart:
        //   [:L1390] "解析函数宏失败!~n函数[" + fnData.fullName + "],参数数量不正确"
        //   [:L1395] "解析函数宏失败!~n函数[" + fnData.name     + "],参数数量不正确"
        // The prompt calls these out explicitly: they are DIFFERENT STRINGS, and a port that unified
        // them would change observable output. This test states both halves - the template is shared,
        // the substituted value is not.
        Assert.False(MacroInvoker.TryClassify(
            BuiltinReference(MacroSentinels.FUNC_VAR),
            ExpansionSigils.DynamicMacro,
            "$$();",
            caretPosition: 2L,
            out _,
            out ExpressionParseError? byFullName));

        Assert.False(MacroInvoker.TryClassify(
            BuiltinReference(MacroSentinels.FUNC_INVOKE),
            ExpansionSigils.DynamicMacro,
            "$$Invoke();",
            caretPosition: 5L,
            out _,
            out ExpressionParseError? byName));

        Assert.NotNull(byFullName);
        Assert.NotNull(byName);

        // ONE TEMPLATE.
        Assert.Equal(byFullName.FormatTemplate, byName.FormatTemplate);
        Assert.Equal("解析函数宏失败!\n函数[{1}],参数数量不正确", byFullName.FormatTemplate);

        // TWO VALUES, and therefore two different rendered messages.
        Assert.Equal("$$()", Assert.Single(byFullName.FormatArguments));
        Assert.Equal(MacroSentinels.FUNC_INVOKE, Assert.Single(byName.FormatArguments));
        Assert.NotEqual(byFullName.Text, byName.Text);

        // TWO SITES, so which oracle line was reached is recoverable from the error itself.
        Assert.Equal(1390, byFullName.LegacyLine);
        Assert.Equal(1395, byName.LegacyLine);

        // Both still carry the same return code, severity and convention - only the named value differs.
        Assert.Equal(byName.ReturnCode, byFullName.ReturnCode);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, byFullName.ReturnCode);
        Assert.Equal(byName.CaretConvention, byFullName.CaretConvention);
    }

    /// <summary>
    /// A CONTEXT function macro is refused outright, before the built-in flag or the arity is even
    /// looked at.
    /// </summary>
    [Fact]
    public void AContextFunctionMacroIsRefusedBeforeAnythingElseIsChecked()
    {
        // ORACLE [:L1306-L1310]
        //   if sChar = "(" then
        //       if bIsCtx then
        //           MessageBox("错误",_of_BuildParseError(...,"解析函数宏失败!~n不支持引用上下文的函数宏"),StopSign!)
        //           return RetCode.E_INVALID_ARGUMENT
        //       end if
        //       fnData.builtIn = bDD                     <- only NOW is the flag read [:L1311]
        // So the refusal precedes classification: @$Func(...) and @$$Invoke(...) get the SAME message
        // whatever their name or arity would have been. This is a deliberate legacy restriction, not a
        // gap - a context reference resolves against the CALLING DataWindow's row, which a function
        // macro's argument list has no way to carry.
        (FunctionReference Reference, ExpansionSigils Sigils, string Syntax)[] contextForms =
        [
            // @$Func(...) - context plus a single sigil. A name that would otherwise be accepted.
            (DirectReference(FormatPrice, "n2"),
                ExpansionSigils.ContextStaticMacro,
                "@$FormatPrice(n2);"),

            // @$$Invoke(...) - context plus a doubled sigil, with a name and arity that would otherwise
            // be perfectly legal.
            (BuiltinReference(MacroSentinels.FUNC_INVOKE, "$" + UnitPriceFormatVariable),
                ExpansionSigils.ContextDynamicMacro,
                "@$$Invoke($" + UnitPriceFormatVariable + ");"),

            // @$$() - context plus the variable sentinel with a WRONG arity. Were the order reversed,
            // this would report the arity failure at :L1390 instead; it does not.
            (BuiltinReference(MacroSentinels.FUNC_VAR),
                ExpansionSigils.ContextDynamicMacro,
                "@$$();"),

            // @$$Other(...) - context plus an undefined name. Same reasoning against :L1399.
            (BuiltinReference("Other", "n2"),
                ExpansionSigils.ContextDynamicMacro,
                "@$$Other(n2);"),
        ];

        foreach ((FunctionReference reference, ExpansionSigils sigils, string syntax) in contextForms)
        {
            Assert.False(MacroInvoker.TryClassify(
                reference,
                sigils,
                syntax,
                caretPosition: 4L,
                out _,
                out ExpressionParseError? error));

            Assert.NotNull(error);

            // ALWAYS the context site, never the arity site and never the undefined site.
            Assert.Equal(ExpressionErrorSite.FunctionMacroContextReferenceUnsupported, error.Site);
            Assert.Equal(1308, error.LegacyLine);
            Assert.Equal(RetCode.E_INVALID_ARGUMENT, error.ReturnCode);
            Assert.Equal("解析函数宏失败!\n不支持引用上下文的函数宏", error.FormatTemplate);
            Assert.Empty(error.FormatArguments);
            Assert.Equal(CaretPositionConvention.MacroTokenMidpoint, error.CaretConvention);
            Assert.Equal(4L, error.CaretPosition);
            Assert.Equal(ExpressionErrorCategory.Parse, error.Category);
        }

        // A refused reference produces a REJECTED PLAN, so the refusal survives the step from
        // classification to dispatch instead of being re-derived there.
        MacroDispatchPlan plan = MacroInvoker.Plan(
            DirectReference(FormatPrice, "n2"),
            ExpansionSigils.ContextStaticMacro,
            MacroArgumentList.FromEvaluated("12.3"),
            "@$FormatPrice(n2);",
            caretPosition: 4L);

        Assert.True(plan.IsRejected);
        Assert.Equal(MacroDispatchKind.Rejected, plan.Kind);
        Assert.NotNull(plan.Error);
        Assert.Equal(ExpressionErrorSite.FunctionMacroContextReferenceUnsupported, plan.Error.Site);
    }

    /// <summary>
    /// An INVALID ARGUMENT LIST is rejected with <c>E_INVALID_ARGUMENT</c> under the midpoint
    /// convention - and it is the SCANNER that rejects it, not this classifier.
    /// </summary>
    [Fact]
    public void AnInvalidArgumentListIsRejectedAndBelongsToTheScanner()
    {
        // ORACLE [:L1382-L1384]
        //   if bFnQuoted or nFnBrCnt <> -1 or nFnArgPos <> 0 then
        //       MessageBox("错误",_of_BuildParseError(...,"解析函数宏失败!~n无效的参数列表"),StopSign!)
        //       return RetCode.E_INVALID_ARGUMENT
        //   end if
        //
        // WHERE THIS LIVES, STATED SO THE BOUNDARY READS AS A BOUNDARY. The refusal happens WHILE the
        // argument list is being collected [:L1319-L1381], which is before a funcdata entry is complete
        // - so a reference that trips it never reaches classification at all, and MacroInvoker cannot
        // raise it. The scanner half is ColumnExpressionEngine.cs, exercised end to end by
        // ColumnExpressionEngineTests.cs over `"$Fn(n2 + 1"`.
        //
        // What IS asserted here is the site's contract, through the very formatter the scanner calls,
        // because the four properties below are what make it indistinguishable from the other macro
        // refusals at the boundary: same code, same severity, same convention, same non-localization.
        ExpressionParseError error = ParseErrorFormatter.CreateParseError(
            ExpressionErrorSite.FunctionMacroInvalidArgumentList,
            "$Fn(n2 + 1;",
            caretPosition: 3L);

        Assert.Equal(1383, error.LegacyLine);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, error.ReturnCode);
        Assert.Equal("解析函数宏失败!\n无效的参数列表", error.FormatTemplate);
        Assert.Empty(error.FormatArguments);
        Assert.Equal(CaretPositionConvention.MacroTokenMidpoint, error.CaretConvention);
        Assert.Equal(3L, error.CaretPosition);
        Assert.Equal(ExpressionErrorCategory.Parse, error.Category);
        Assert.Equal(ExpressionErrorSeverity.StopSign, error.Severity);
        Assert.False(error.Localized);

        // AND THE BOUNDARY ITSELF: a reference that DID reach classification is never refused with this
        // site, whatever its shape - because by then its argument list has already parsed.
        foreach (FunctionReference reference in (FunctionReference[])
        [
            BuiltinReference(MacroSentinels.FUNC_VAR),
            BuiltinReference(MacroSentinels.FUNC_INVOKE),
            BuiltinReference("Other", "n2"),
            DirectReference(FormatPrice, "n2"),
        ])
        {
            _ = MacroInvoker.TryClassify(
                reference,
                ExpansionSigils.DynamicMacro,
                reference.FullName + ";",
                caretPosition: 3L,
                out _,
                out ExpressionParseError? classified);

            if (classified is not null)
            {
                Assert.NotEqual(ExpressionErrorSite.FunctionMacroInvalidArgumentList, classified.Site);
            }
        }

        // A rejected plan carries whatever refusal it was handed, including one the scanner raised -
        // which is how the scanner's refusal reaches a dispatch-shaped caller without being re-derived.
        MacroDispatchPlan plan = MacroDispatchPlan.ForRejection(error);
        Assert.True(plan.IsRejected);
        Assert.Same(error, plan.Error);
    }

    /// <summary>
    /// The caret position every macro refusal carries is the INTEGER-DIVIDED midpoint of the macro
    /// token.
    /// </summary>
    /// <returns>The macro start, the bracket position, the expected midpoint, and the locator.</returns>
    /// <remarks>
    /// The formula is <c>nMacPos + (nPos - nMacPos) / 2</c>, and every caret-bearing macro site uses it
    /// - <c>:L1308</c>, <c>:L1383</c>, <c>:L1390</c>, <c>:L1395</c>, <c>:L1399</c>. PowerScript's
    /// <c>/</c> on two integers TRUNCATES, so an odd span loses its remainder and the caret sits one
    /// character LEFT of the true centre. The rows below include odd spans precisely to pin that.
    /// </remarks>
    public static TheoryData<long, long, long, string> MidpointMatrix() => new()
    {
        // "$$();"       nMacPos = 1, "(" at 3  -> 1 + 2/2 = 2
        { 1L, 3L, 2L, "n_cst_dwsvc_columnexp.sru:L1390 over $$()" },

        // "$$Invoke();" nMacPos = 1, "(" at 9  -> 1 + 8/2 = 5
        { 1L, 9L, 5L, "n_cst_dwsvc_columnexp.sru:L1395 over $$Invoke()" },

        // "$Fn(x);"     nMacPos = 1, "(" at 4  -> 1 + 3/2 = 1 + 1 = 2   TRUNCATION, not 2.5
        { 1L, 4L, 2L, "n_cst_dwsvc_columnexp.sru:L1383 - odd span truncates" },

        // "$$Other(x);" nMacPos = 1, "(" at 8  -> 1 + 7/2 = 1 + 3 = 4   TRUNCATION again
        { 1L, 8L, 4L, "n_cst_dwsvc_columnexp.sru:L1399 - odd span truncates" },

        // A macro that does not start the expression: "a+$$Fn();" nMacPos = 3, "(" at 7 -> 3 + 4/2 = 5
        { 3L, 7L, 5L, "n_cst_dwsvc_columnexp.sru:L1286 - nMacPos is the FIRST sigil, not always 1" },

        // The degenerate span: bracket immediately after the sigil -> the midpoint IS nMacPos.
        { 5L, 5L, 5L, "n_cst_dwsvc_columnexp.sru:L1390 - zero span leaves the caret on the sigil" },
    };

    /// <summary>
    /// The midpoint value handed to a refusal reaches the structured error unchanged.
    /// </summary>
    /// <param name="macroStart">The one-based position of the first sigil - <c>nMacPos</c>.</param>
    /// <param name="bracketPosition">The one-based position of the opening bracket - <c>nPos</c>.</param>
    /// <param name="expected">The midpoint the oracle's integer division produces.</param>
    /// <param name="locator">The oracle locator for this row.</param>
    [Theory]
    [MemberData(nameof(MidpointMatrix))]
    public void TheRefusalCaretIsTheIntegerDividedMidpointAndPassesThroughUnchanged(
        long macroStart,
        long bracketPosition,
        long expected,
        string locator)
    {
        Assert.False(string.IsNullOrWhiteSpace(locator));

        // The formula, stated as the oracle states it - INTEGER division included.
        Assert.Equal(expected, MacroTokenMidpoint(macroStart, bracketPosition));

        // ... and the value reaches the error untouched. The caret ARITHMETIC - how the marker line is
        // laid out from this position, and how a double-byte character widens the padding - is
        // ParseErrorFormatterTests.cs's; the passthrough is this file's.
        Assert.False(MacroInvoker.TryClassify(
            BuiltinReference("Other", "n2"),
            ExpansionSigils.DynamicMacro,
            "$$Other(n2);",
            expected,
            out _,
            out ExpressionParseError? error));

        Assert.NotNull(error);
        Assert.Equal(expected, error.CaretPosition);
        Assert.Equal(CaretPositionConvention.MacroTokenMidpoint, error.CaretConvention);
    }

    /// <summary>
    /// The oracle's caret formula, <c>nMacPos + (nPos - nMacPos) / 2</c>.
    /// </summary>
    /// <param name="macroStart">The one-based position of the first sigil.</param>
    /// <param name="bracketPosition">The one-based position of the opening bracket.</param>
    /// <returns>The midpoint, truncated as PowerScript's integer division truncates.</returns>
    /// <remarks>
    /// An EXPECTATION, like <see cref="LegacyMid"/>: it computes what the scanner would have passed, so
    /// the theory rows can be read against the oracle rather than against pre-computed numbers.
    /// </remarks>
    private static long MacroTokenMidpoint(long macroStart, long bracketPosition) =>
        macroStart + ((bracketPosition - macroStart) / 2L);

    /// <summary>
    /// The accepted arities, so the boundary between refusal and acceptance is pinned from both sides.
    /// </summary>
    /// <returns>The name, the argument count, the expected dispatch arm, and the locator.</returns>
    public static TheoryData<string, int, MacroFunctionDispatch, string> AcceptedArityMatrix() => new()
    {
        // FUNC_VAR: exactly one, and only one.
        { "", 1, MacroFunctionDispatch.VariableLookup, "n_cst_dwsvc_columnexp.sru:L1389 - $$('name')" },

        // FUNC_INVOKE: one is the minimum - the callee name alone.
        { "Invoke", 1, MacroFunctionDispatch.DynamicInvoke, "n_cst_dwsvc_columnexp.sru:L1394 - name only" },
        { "Invoke", 2, MacroFunctionDispatch.DynamicInvoke, "n_cst_dwsvc_columnexp.sru:L1394 - name + 1" },
        { "Invoke", 3, MacroFunctionDispatch.DynamicInvoke, "docs/n_cst_dwsvc_columnexp.md:L141 - name + 2" },
        { "Invoke", 9, MacroFunctionDispatch.DynamicInvoke, "n_cst_dwsvc_columnexp.sru:L1394 - no upper bound" },
    };

    /// <summary>
    /// Each accepted arity selects its arm without raising anything.
    /// </summary>
    /// <param name="name">The name after the two sigils.</param>
    /// <param name="argumentCount">How many arguments the reference declares.</param>
    /// <param name="expected">The arm the oracle's switch selects.</param>
    /// <param name="locator">The oracle locator for this row.</param>
    [Theory]
    [MemberData(nameof(AcceptedArityMatrix))]
    public void EveryAcceptedArityStillSelectsItsArm(
        string name,
        int argumentCount,
        MacroFunctionDispatch expected,
        string locator)
    {
        Assert.False(string.IsNullOrWhiteSpace(locator));

        string[] args = [.. Enumerable
            .Range(1, argumentCount)
            .Select(index => "'a" + index.ToString(CultureInfo.InvariantCulture) + "'")];

        Assert.True(MacroInvoker.TryClassify(
            BuiltinReference(name, args),
            ExpansionSigils.DynamicMacro,
            "$$" + name + "(...);",
            caretPosition: 3L,
            out MacroFunctionDispatch dispatch,
            out ExpressionParseError? error));

        Assert.Equal(expected, dispatch);
        Assert.Null(error);
    }

    /// <summary>
    /// An APPLICATION macro has no arity rule at all: only the two built-ins are checked.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(21)]
    public void AnApplicationMacroHasNoArityRule(int argumentCount)
    {
        // ORACLE [:L1386] `if fnData.builtIn then` - the ENTIRE arity switch is inside that guard, so a
        // single-sigil macro is never arity-checked. The application's own `choose case name` decides
        // what it accepts, which is why 21 arguments classify exactly as 0 do.
        //
        // The count 21 is deliberate: AAP 0.2.1.4 records the legacy's 20-argument variadic ceiling in
        // the SQL layer as an artefact of PowerScript's inability to forward an arbitrary argument list.
        // The macro path has no such ceiling, and this row states that the port did not import one.
        string[] args = [.. Enumerable
            .Range(1, argumentCount)
            .Select(index => "'a" + index.ToString(CultureInfo.InvariantCulture) + "'")];

        Assert.True(MacroInvoker.TryClassify(
            DirectReference(FormatPrice, args),
            ExpansionSigils.StaticMacro,
            "$FormatPrice(...);",
            caretPosition: 7L,
            out MacroFunctionDispatch dispatch,
            out ExpressionParseError? error));

        Assert.Equal(MacroFunctionDispatch.Application, dispatch);
        Assert.Null(error);
    }

    // ==============================================================================================
    //  PHASE 4 - THE INVERSION, AND THE TWO INVALID-RETURN PATHS
    // ==============================================================================================

    /// <summary>
    /// The invoker CALLS BACK INTO ITS CLIENT and resolves nothing locally: with no client handler, the
    /// invocation produces a defined error rather than a default, a zero, or a silent skip.
    /// </summary>
    [Fact]
    public async Task WithNoClientHandlerTheInvocationProducesADefinedErrorAndNeverADefaultValue()
    {
        // ORACLE - the macro switch is the APPLICATION'S, not the engine's:
        //   docs/n_cst_dwsvc_columnexp.md:L106  "可以将函数定义在数据窗口的 OnColumnExpInvokeMethod 中"
        //   docs/n_cst_dwsvc_columnexp.md:L120  "- 实现 OnColumnExpInvokeMethod 事件"
        //   se_cst_dw.sru:L14                   the event the application implements
        //   n_cst_dwsvc_columnexp.sru:L2263/:L2287  the engine FIRES it and consumes the answer
        // AAP 0.6.2 / contract C-04: because the application owns the switch, the boundary must INVERT -
        // DataServices, the server, calls back into its client. So the engine has no macro table to
        // consult and cannot answer for itself.
        //
        // The channel below is scripted with NOTHING. A port that had quietly kept a local switch - or
        // that substituted a default when the client was silent - would still produce a number here.
        ScriptedMacroChannel channel = new();
        FakeDataWindowObject column = BoundColumn();

        MacroDispatchPlan plan = MacroInvoker.Plan(
            DocumentedDirectReference(),
            MacroArgumentList.FromEvaluated("12.345", "1"),
            "$FormatPrice(n2, $" + PrecisionVariable + ");",
            caretPosition: 7L);

        MacroInvocationResult result = await DispatchAsync(channel, plan, column);

        // THE CLIENT WAS ASKED. That is the inversion, and it is asserted before anything else.
        MacroInvocation invocation = Assert.Single(channel.Invocations);
        Assert.Equal(FormatPrice, invocation.Name);

        // A DEFINED ERROR - and it is the oracle's own invalid-return refusal, because in process the
        // application's `choose case` FALLS THROUGH when it does not know the name, and a fall-through
        // yields a value whose class matches no arm [:L2288-L2305] and lands on `case else` [:L2306].
        Assert.Equal(MacroInvocationOutcome.Rejected, result.Outcome);
        Assert.True(result.Unhandled);
        Assert.NotNull(result.Error);
        Assert.Equal(ExpressionErrorSite.PreprocessFunctionReturnValueInvalid, result.Error.Site);
        Assert.Equal(2306, result.Error.LegacyLine);

        // NOT A DEFAULT, NOT A ZERO, NOT A SILENT SKIP - each stated separately, because each is a
        // different way a port could have gone wrong.
        Assert.Null(result.Value);
        Assert.Null(result.Rendered);
        Assert.Null(result.WrappedRendering);
        Assert.NotEqual("0", result.Rendered);
        Assert.NotEqual("(0)", result.WrappedRendering);

        // AND THE CALCULATION ABORTS - the port of `return ""` [:L2307], which every caller reads as
        // failure [:L745 `if sExp = "" then return false`]. A silent skip would leave Aborts false and
        // let the expression continue with a hole in it.
        Assert.True(result.Aborts);

        // The abort sentinel IS the empty string, and the predicate names it so no site tests a bare "".
        Assert.Empty(MacroInvoker.AbortSentinel);
        Assert.True(MacroInvoker.IsAbort(MacroInvoker.AbortSentinel));
        Assert.True(MacroInvoker.IsAbort(null));
        Assert.False(MacroInvoker.IsAbort("(12.3)"));
    }

    /// <summary>
    /// The calculation cannot proceed until the client answers: the value the client returns IS the
    /// result, and the invoker holds the invocation open while the client computes it.
    /// </summary>
    [Fact]
    public async Task TheCalculationDependsOnTheClientsAnswerAndWaitsForIt()
    {
        // AAP 0.6.1.4 assigns macro invocation ordering pattern (b) - strictly synchronous, no
        // reordering - and gives the reason in one sentence: "the calculation cannot proceed without the
        // returned value". EventOrderingPatternTests.cs owns the ORDERING assertion; what is asserted
        // here is the VALUE DEPENDENCY, in three independent ways.
        MacroInvoker invoker;
        int outstandingWhileClientRan = -1;
        List<string> stages = [];

        ScriptedMacroChannel channel = new ScriptedMacroChannel().ScriptFormatPrice();

        invoker = new MacroInvoker(
            channel,
            invocationTimeout: null,
            timeProvider: new DeterministicTimeProvider(),
            invocationIdFactory: () => "inv-1");

        channel.OnInvoke = _ =>
        {
            stages.Add("client-asked");

            // (1) THE INVOKER IS WAITING AT THIS MOMENT. The invocation is registered as outstanding
            //     while the client computes, so the engine has not carried on without the answer.
            outstandingWhileClientRan = invoker.OutstandingCount;
        };

        MacroDispatchPlan plan = MacroInvoker.Plan(
            DocumentedDirectReference(),
            MacroArgumentList.FromEvaluated("12.345", "1"),
            "$FormatPrice(n2, $" + PrecisionVariable + ");",
            caretPosition: 7L);

        MacroInvocationResult first = await invoker.InvokeAsync(
            Row,
            BoundColumn(),
            plan,
            cancellationToken: Ct);

        stages.Add("engine-continued");

        Assert.Equal(["client-asked", "engine-continued"], stages);
        Assert.Equal(1, outstandingWhileClientRan);

        // ... and the invocation is retired once answered, so the wait is bounded by the answer rather
        // than leaking.
        Assert.Equal(0, invoker.OutstandingCount);
        Assert.Equal(1L, invoker.IssuedInvocationCount);

        // (2) THE ANSWER IS THE RESULT. 精度 = 1 rounds 12.345 to 12.3.
        Assert.Equal(MacroInvocationOutcome.Rendered, first.Outcome);
        Assert.Equal("(12.3)", first.WrappedRendering);

        // (3) A DIFFERENT ANSWER PRODUCES A DIFFERENT RESULT, from the identical plan. This is what rules
        //     out a port that computed the value itself and merely notified the client.
        ScriptedMacroChannel other = new ScriptedMacroChannel().ScriptValue(FormatPrice, 99L);

        MacroInvocationResult second = await DispatchAsync(other, plan);

        Assert.Equal(MacroInvocationOutcome.Rendered, second.Outcome);
        Assert.Equal("(99)", second.WrappedRendering);
        Assert.NotEqual(first.WrappedRendering, second.WrappedRendering);
    }

    /// <summary>
    /// The DIRECT form's invalid return lands at <c>:L2306</c> and names the reference's own name.
    /// </summary>
    [Fact]
    public async Task TheDirectFormsInvalidReturnLandsAtItsOwnSiteAndNamesTheReferenceName()
    {
        // ORACLE [:L2306]
        //   MessageBox("错误","预处理表达式发生错误!~n[" + String(dwo.name)
        //              + "]表达式错误:~n函数[" + fns[nFnIdx].name + "],返回值无效",StopSign!)
        //   return ""
        // Keyed on fns[nFnIdx].name - the reference's OWN name, because on this path there is nothing
        // else it could be.
        ScriptedMacroChannel channel = new ScriptedMacroChannel()
            .ScriptInvalidReturnValue(FormatPrice);

        FakeDataWindowObject column = BoundColumn();

        MacroDispatchPlan plan = MacroInvoker.Plan(
            DocumentedDirectReference(),
            MacroArgumentList.FromEvaluated("12.345", "1"),
            "$FormatPrice(n2, $" + PrecisionVariable + ");",
            caretPosition: 7L);

        MacroInvocationResult result = await DispatchAsync(channel, plan, column);

        Assert.Equal(MacroInvocationOutcome.Rejected, result.Outcome);
        Assert.NotNull(result.Error);
        Assert.Equal(ExpressionErrorSite.PreprocessFunctionReturnValueInvalid, result.Error.Site);
        Assert.Equal(2306, result.Error.LegacyLine);

        // The message, assembled from the column name and the FUNCTION name, in that order.
        Assert.Equal([column.Name, FormatPrice], result.Error.FormatArguments);
        Assert.Equal(
            "预处理表达式发生错误!\n[n1]表达式错误:\n函数[FormatPrice],返回值无效",
            result.Error.Text);

        // The value that was refused is carried, so a diagnosis can name the offending type rather than
        // only reporting that something was wrong.
        Assert.NotNull(result.Value);
        Assert.False(result.Unhandled);
        Assert.True(result.Aborts);

        // NO RETURN CODE, AND THAT IS FAITHFUL RATHER THAN MISSING. _of_PreprocessExp is declared to
        // return a STRING, so its failure value is "" and it has no code to give [:L2307]. The
        // parse-time refusals DO carry E_INVALID_ARGUMENT because the scanner returns a long.
        Assert.Null(result.Error.ReturnCode);
        Assert.Null(result.ReturnCode);

        // A PLAIN MESSAGE, not a caret-bearing parse error: the oracle raises this one with no
        // expression and no position at all, so none is fabricated here.
        Assert.Equal(ExpressionErrorFamily.PlainMessage, result.Error.Family);
        Assert.Equal(ExpressionErrorCategory.InvalidValue, result.Error.Category);
        Assert.Equal(ExpressionErrorSeverity.StopSign, result.Error.Severity);
        Assert.Null(result.Error.Expression);
        Assert.Null(result.Error.CaretPosition);
        Assert.Equal(CaretPositionConvention.Unspecified, result.Error.CaretConvention);

        // Hardcoded Chinese, NOT routed through I18N - the inconsistency AAP 0.4.2.5 requires be
        // reproduced rather than harmonised (C-B).
        Assert.False(result.Error.Localized);
        Assert.Equal(0L, result.Error.LocalizationCategory);
        Assert.Equal(ExpressionErrorCatalog.LegacyTitle, result.Error.Title);
    }

    /// <summary>
    /// The DYNAMIC form's invalid return lands at <c>:L2282</c> and names the RESOLVED callee - the
    /// value of <c>sArgs[1]</c>.
    /// </summary>
    [Fact]
    public async Task TheDynamicFormsInvalidReturnLandsAtItsOwnSiteAndNamesTheResolvedCallee()
    {
        // ORACLE [:L2282]
        //   MessageBox("错误","预处理表达式发生错误!~n[" + String(dwo.name)
        //              + "]表达式错误:~n函数[" + sArgs[1] + "],返回值无效",StopSign!)
        //   return ""
        // Keyed on sArgs[1] - THE FIRST ARGUMENT, which on this arm is the resolved callee name and not
        // the sentinel. So a $$Invoke whose callee misbehaves reports the callee, never "Invoke".
        ScriptedMacroChannel channel = new ScriptedMacroChannel()
            .ScriptInvalidReturnValue(FormatPrice);

        FakeDataWindowObject column = BoundColumn();

        MacroDispatchPlan plan = MacroInvoker.Plan(
            DocumentedInvokeReference(),
            MacroArgumentList.FromEvaluated(FormatPrice, "12.345", "1"),
            "$$Invoke($" + UnitPriceFormatVariable + ", n2, $" + PrecisionVariable + ");",
            caretPosition: 5L);

        MacroInvocationResult result = await DispatchAsync(channel, plan, column);

        Assert.Equal(MacroInvocationOutcome.Rejected, result.Outcome);
        Assert.NotNull(result.Error);
        Assert.Equal(ExpressionErrorSite.PreprocessMacroReturnValueInvalid, result.Error.Site);
        Assert.Equal(2282, result.Error.LegacyLine);

        // THE RESOLVED CALLEE, and neither the sentinel nor the variable that held it.
        Assert.Equal([column.Name, FormatPrice], result.Error.FormatArguments);
        Assert.DoesNotContain(MacroSentinels.FUNC_INVOKE, result.Error.FormatArguments);
        Assert.DoesNotContain(UnitPriceFormatVariable, result.Error.Text, StringComparison.Ordinal);
        Assert.Equal(
            "预处理表达式发生错误!\n[n1]表达式错误:\n函数[FormatPrice],返回值无效",
            result.Error.Text);

        Assert.True(result.Aborts);
        Assert.Null(result.Error.ReturnCode);
        Assert.Equal(ExpressionErrorCategory.InvalidValue, result.Error.Category);
    }

    /// <summary>
    /// The two invalid-return paths are TWO SITES sharing one template: identical text when the names
    /// coincide, different text when they do not, and always distinguishable by site.
    /// </summary>
    [Fact]
    public async Task TheTwoInvalidReturnPathsAreDistinctSitesOverOneSharedTemplate()
    {
        // ORACLE - byte-identical templates twenty-four lines apart, differing ONLY in provenance:
        //   [:L2282] ... 函数[" + sArgs[1]           + "],返回值无效   (dynamic)
        //   [:L2306] ... 函数[" + fns[nFnIdx].name    + "],返回值无效   (direct)
        // The prompt requires both be asserted because they are different strings whenever the resolved
        // callee differs from the reference name - and because a port that unified them would lose which
        // line was reached even when the strings coincide.
        FakeDataWindowObject column = BoundColumn();

        // A DIRECT reference named "Other", which the client refuses by returning an unusable type.
        ScriptedMacroChannel directChannel = new ScriptedMacroChannel()
            .ScriptInvalidReturnValue("Other");

        MacroInvocationResult direct = await DispatchAsync(
            directChannel,
            MacroInvoker.Plan(
                DirectReference("Other", "n2"),
                MacroArgumentList.FromEvaluated("12.345"),
                "$Other(n2);",
                caretPosition: 4L),
            column);

        // A DYNAMIC reference whose callee resolves to "FormatPrice", also refused.
        ScriptedMacroChannel dynamicChannel = new ScriptedMacroChannel()
            .ScriptInvalidReturnValue(FormatPrice);

        MacroInvocationResult dynamic = await DispatchAsync(
            dynamicChannel,
            MacroInvoker.Plan(
                DocumentedInvokeReference(),
                MacroArgumentList.FromEvaluated(FormatPrice, "12.345", "1"),
                "$$Invoke($" + UnitPriceFormatVariable + ", n2, $" + PrecisionVariable + ");",
                caretPosition: 5L),
            column);

        Assert.NotNull(direct.Error);
        Assert.NotNull(dynamic.Error);

        // TWO SITES.
        Assert.Equal(ExpressionErrorSite.PreprocessFunctionReturnValueInvalid, direct.Error.Site);
        Assert.Equal(ExpressionErrorSite.PreprocessMacroReturnValueInvalid, dynamic.Error.Site);
        Assert.NotEqual(direct.Error.Site, dynamic.Error.Site);
        Assert.Equal(2306, direct.Error.LegacyLine);
        Assert.Equal(2282, dynamic.Error.LegacyLine);

        // ONE TEMPLATE.
        Assert.Equal(direct.Error.FormatTemplate, dynamic.Error.FormatTemplate);
        Assert.Equal(
            "预处理表达式发生错误!\n[{1}]表达式错误:\n函数[{2}],返回值无效",
            direct.Error.FormatTemplate);

        // TWO DIFFERENT MESSAGES, because the names differ.
        Assert.NotEqual(direct.Error.Text, dynamic.Error.Text);
        Assert.Contains("函数[Other],", direct.Error.Text, StringComparison.Ordinal);
        Assert.Contains("函数[FormatPrice],", dynamic.Error.Text, StringComparison.Ordinal);

        // Neither carries a return code, and both abort.
        Assert.Null(direct.Error.ReturnCode);
        Assert.Null(dynamic.Error.ReturnCode);
        Assert.True(direct.Aborts);
        Assert.True(dynamic.Aborts);

        // AND WHEN THE NAMES COINCIDE THE TEXT COINCIDES TOO - so the SITE is the only discriminator
        // left, which is exactly why the port records it.
        ScriptedMacroChannel sameNameDirect = new ScriptedMacroChannel()
            .ScriptInvalidReturnValue(FormatPrice);

        MacroInvocationResult coincident = await DispatchAsync(
            sameNameDirect,
            MacroInvoker.Plan(
                DocumentedDirectReference(),
                MacroArgumentList.FromEvaluated("12.345", "1"),
                "$FormatPrice(n2, $" + PrecisionVariable + ");",
                caretPosition: 7L),
            column);

        Assert.NotNull(coincident.Error);
        Assert.Equal(dynamic.Error.Text, coincident.Error.Text);
        Assert.NotEqual(dynamic.Error.Site, coincident.Error.Site);
    }

    /// <summary>
    /// Every return type the oracle's conversion switch refuses reaches the invalid-return site rather
    /// than being coerced into something usable.
    /// </summary>
    /// <returns>The scripted return value's description, the value, and the locator.</returns>
    /// <remarks>
    /// <c>ushort</c> is the PRESERVED DEFECT: PowerBuilder reports it as <c>unsignedinteger</c>, the
    /// oracle's label list misspells that as <c>usignedinteger</c> [:L2267, :L2291], and so a genuine
    /// <c>unsignedinteger</c> return is refused even though every other integral type is accepted.
    /// Correcting the spelling would make a previously failing macro start working, which is a behaviour
    /// change dressed up as a typo fix (C-B).
    /// </remarks>
    public static TheoryData<string, object, string> RefusedReturnTypeMatrix() => new()
    {
        {
            "unsignedinteger - THE MISSPELLING DEFECT",
            (ushort)7,
            "n_cst_dwsvc_columnexp.sru:L2291 - label spelled 'usignedinteger', so ushort matches none"
        },
        {
            "blob",
            new byte[] { 1, 2, 3 },
            "n_cst_dwsvc_columnexp.sru:L2289-L2304 - blob is not among the labels"
        },
        {
            "char",
            'x',
            "n_cst_dwsvc_columnexp.sru:L2289-L2304 - char is not among the labels"
        },
        {
            "byte",
            (byte)5,
            "n_cst_dwsvc_columnexp.sru:L2289-L2304 - byte is not among the labels"
        },
        {
            "an arbitrary object - the choose case falls through",
            new object(),
            "n_cst_dwsvc_columnexp.sru:L2305-L2307 - case else"
        },
    };

    /// <summary>
    /// A refused return type aborts with the invalid-return message and never renders.
    /// </summary>
    /// <param name="description">What the scripted value is, for the failure message.</param>
    /// <param name="value">The value the client returns.</param>
    /// <param name="locator">The oracle locator for this row.</param>
    [Theory]
    [MemberData(nameof(RefusedReturnTypeMatrix))]
    public async Task ARefusedReturnTypeAbortsWithTheInvalidReturnMessage(
        string description,
        object value,
        string locator)
    {
        Assert.False(string.IsNullOrWhiteSpace(description));
        Assert.False(string.IsNullOrWhiteSpace(locator));

        ScriptedMacroChannel channel = new ScriptedMacroChannel().ScriptValue(FormatPrice, value);

        MacroInvocationResult result = await DispatchAsync(
            channel,
            MacroInvoker.Plan(
                DocumentedDirectReference(),
                MacroArgumentList.FromEvaluated("12.345", "1"),
                "$FormatPrice(n2, $" + PrecisionVariable + ");",
                caretPosition: 7L));

        Assert.Equal(MacroInvocationOutcome.Rejected, result.Outcome);
        Assert.True(result.Aborts);
        Assert.Null(result.Rendered);
        Assert.NotNull(result.Error);
        Assert.Equal(ExpressionErrorSite.PreprocessFunctionReturnValueInvalid, result.Error.Site);

        // The value is carried through so the refusal is diagnosable, and it is the very value scripted.
        Assert.Same(value, result.Value);

        // The refusal is decided by the CLASS-NAME PROJECTION disagreeing with the label list, which is
        // where the misspelling defect lives - so the same conclusion is reachable without a channel.
        Assert.False(MacroInvoker.TryRender(value, out string rendered, out _));
        Assert.Empty(rendered);
    }

    /// <summary>
    /// A misbehaving client is surfaced as a DEFINED error and never as a fabricated value, and the
    /// invoker is left usable.
    /// </summary>
    [Fact]
    public async Task AMisbehavingClientSurfacesADefinedErrorAndLeavesNoLeakedState()
    {
        // WHAT "DEFINED" MEANS HERE, STATED PRECISELY, BECAUSE THERE ARE THREE KINDS OF MISBEHAVIOUR AND
        // THE PORT ANSWERS EACH DIFFERENTLY - by design, and never with a value:
        //
        //   (a) NO HANDLER              -> a defined RESULT: Rejected, carrying the oracle's own
        //                                 invalid-return refusal [:L2306]. Asserted above.
        //   (b) BROKEN CORRELATION      -> a defined, NAMED, TYPED exception,
        //                                 MacroProtocolViolationException, carrying the identifiers that
        //                                 failed to match. Without it a mismatched answer would be
        //                                 dereferenced blindly and surface as a NullReferenceException -
        //                                 THAT would be the undefined escape. AAP 0.6.1.4: under ordering
        //                                 pattern (b) an out-of-order arrival is a hard error and never a
        //                                 reorder opportunity, because substituting a value computed for
        //                                 one macro into another's expression is silent data corruption.
        //   (c) A FAULTING TRANSPORT    -> the fault reaches the caller intact. It has no legacy
        //                                 counterpart - an in-process event trigger cannot fail in
        //                                 transit - so there is no behaviour to preserve, and the only
        //                                 alternatives are to retry, which is the channel's own
        //                                 resilience policy, or to substitute a value, which would put a
        //                                 fabricated number into a calculation. Failing loudly is the
        //                                 fail-fast posture AAP 0.1.4 requires be preserved AS
        //                                 fail-fast rather than softened into graceful degradation.
        //
        // What this test proves about all three: no path yields a value, and no path leaks the
        // invocation - the bookkeeping is retired in a finally, so the failure is HANDLED internally even
        // where the exception is deliberately not.
        MacroDispatchPlan plan = MacroInvoker.Plan(
            DocumentedDirectReference(),
            MacroArgumentList.FromEvaluated("12.345", "1"),
            "$FormatPrice(n2, $" + PrecisionVariable + ");",
            caretPosition: 7L);

        // ---- (b) THE ANSWER CORRELATES TO A DIFFERENT INVOCATION.
        ScriptedMacroChannel wrongId = new ScriptedMacroChannel().ScriptFormatPrice();
        wrongId.OverrideInvocationId = "someone-elses-invocation";

        MacroInvoker idInvoker = new(wrongId, invocationIdFactory: () => "inv-1");

        MacroProtocolViolationException mismatch =
            await Assert.ThrowsAsync<MacroProtocolViolationException>(
                async () => await idInvoker.InvokeAsync(
                    Row,
                    BoundColumn(),
                    plan,
                    cancellationToken: Ct));

        Assert.Equal("inv-1", mismatch.ExpectedInvocationId);
        Assert.Equal("someone-elses-invocation", mismatch.InvocationId);
        Assert.False(mismatch.Late);
        Assert.Contains("out of order", mismatch.Message, StringComparison.Ordinal);
        Assert.Equal(0, idInvoker.OutstandingCount);

        // ---- (b) THE ANSWER ECHOES A DIFFERENT SEQUENCE NUMBER. The token is for DETECTION only, so a
        //          mismatch is a hard error rather than something to reconcile.
        ScriptedMacroChannel wrongSequence = new ScriptedMacroChannel().ScriptFormatPrice();
        wrongSequence.OverrideSequence = 99L;

        MacroInvoker sequenceInvoker = new(wrongSequence, invocationIdFactory: () => "inv-1");

        MacroProtocolViolationException echoed =
            await Assert.ThrowsAsync<MacroProtocolViolationException>(
                async () => await sequenceInvoker.InvokeAsync(
                    Row,
                    BoundColumn(),
                    plan,
                    cancellationToken: Ct));

        Assert.Contains("sequence 99", echoed.Message, StringComparison.Ordinal);
        Assert.Equal("inv-1", echoed.ExpectedInvocationId);
        Assert.Equal(0, sequenceInvoker.OutstandingCount);

        // ---- (b) A DUPLICATE IDENTIFIER, which would make two answers indistinguishable. Reached by
        //          pinning the factory to one value and issuing a NESTED invocation from inside the
        //          client - the shape the oracle creates when a macro ARGUMENT contains another macro
        //          [:L2216-L2220].
        ScriptedMacroChannel duplicate = new ScriptedMacroChannel().ScriptFormatPrice();
        MacroInvoker nestedInvoker = new(duplicate, invocationIdFactory: () => "inv-same");
        Exception? nestedFault = null;

        duplicate.OnInvoke = _ =>
        {
            if (nestedFault is not null)
            {
                return;
            }

            try
            {
                // The outer invocation is still outstanding under "inv-same", so this must be refused.
                nestedInvoker
                    .InvokeAsync(Row, BoundColumn(), plan, cancellationToken: Ct)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
            catch (MacroProtocolViolationException violation)
            {
                nestedFault = violation;
            }
        };

        _ = await nestedInvoker.InvokeAsync(Row, BoundColumn(), plan, cancellationToken: Ct);

        MacroProtocolViolationException duplicateViolation =
            Assert.IsType<MacroProtocolViolationException>(nestedFault);
        Assert.Contains("already outstanding", duplicateViolation.Message, StringComparison.Ordinal);
        Assert.Equal(0, nestedInvoker.OutstandingCount);

        // ---- (c) A FAULTING TRANSPORT.
        ScriptedMacroChannel faulting = new ScriptedMacroChannel().ScriptFormatPrice();
        faulting.Fault = new InvalidOperationException("the macro channel is broken");

        MacroInvoker faultInvoker = new(faulting, invocationIdFactory: () => "inv-1");

        InvalidOperationException fault = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await faultInvoker.InvokeAsync(
                Row,
                BoundColumn(),
                plan,
                cancellationToken: Ct));

        // The fault reaches the caller INTACT - same type, same message - so it is diagnosable rather
        // than laundered into a generic failure. And it is NOT a protocol violation, so the two kinds of
        // misbehaviour stay distinguishable at the catch site.
        Assert.Equal("the macro channel is broken", fault.Message);
        Assert.IsNotType<MacroProtocolViolationException>(fault);

        // NO VALUE WAS FABRICATED - the caller received no result at all rather than a plausible number.
        // NO STATE LEAKED - the invocation was retired in the finally, so the invoker is still usable.
        Assert.Equal(0, faultInvoker.OutstandingCount);
        Assert.Equal(1L, faultInvoker.IssuedInvocationCount);

        faulting.Fault = null;

        MacroInvocationResult recovered = await faultInvoker.InvokeAsync(
            Row,
            BoundColumn(),
            plan,
            cancellationToken: Ct);

        Assert.Equal(MacroInvocationOutcome.Rendered, recovered.Outcome);
        Assert.Equal("(12.3)", recovered.WrappedRendering);
    }

    /// <summary>
    /// The protocol-violation type exposes the full exception surface, so a diagnostic can be built from
    /// it the way one is built from any framework exception.
    /// </summary>
    [Fact]
    public void TheProtocolViolationTypeExposesTheFullExceptionSurface()
    {
        // IT IS AN InvalidOperationException BY DESIGN, not a bespoke root: a channel that answers the
        // wrong question has been used in a state where the call is invalid, and inheriting from the
        // framework's own type means existing catch sites and existing logging enrichers already handle
        // it. It is deliberately NOT a MacroInvocationOutcome member - cancellation and timeout are
        // outcomes because they are things the ENVIRONMENT does, whereas a correlation failure is a
        // BROKEN IMPLEMENTATION, and giving it a result value would invite a caller to carry on past it.
        MacroProtocolViolationException fromDefault = new();
        Assert.IsAssignableFrom<InvalidOperationException>(fromDefault);
        Assert.Contains("ordering discipline", fromDefault.Message, StringComparison.Ordinal);
        Assert.Null(fromDefault.ExpectedInvocationId);
        Assert.Null(fromDefault.InvocationId);
        Assert.False(fromDefault.Late);

        MacroProtocolViolationException fromMessage = new("the channel went quiet");
        Assert.Equal("the channel went quiet", fromMessage.Message);
        Assert.Null(fromMessage.InnerException);

        InvalidOperationException cause = new("the stream faulted");
        MacroProtocolViolationException fromCause = new("the channel went quiet", cause);
        Assert.Equal("the channel went quiet", fromCause.Message);
        Assert.Same(cause, fromCause.InnerException);

        // The correlation-bearing constructor, including the LATE flag that distinguishes an answer for a
        // finished invocation from an answer for one that never existed - a distinction worth keeping
        // because the two have different causes: a slow client versus a wrong one.
        MacroProtocolViolationException late = new("answered late", "inv-1", "inv-0", late: true);
        Assert.Equal("inv-1", late.ExpectedInvocationId);
        Assert.Equal("inv-0", late.InvocationId);
        Assert.True(late.Late);
    }

    /// <summary>
    /// A cancelled or timed-out wait is a DEFINED RESULT with its own code, not an escaping exception -
    /// and never a value.
    /// </summary>
    [Fact]
    public async Task ACancelledWaitIsADefinedResultCarryingItsOwnReturnCode()
    {
        // NEITHER OUTCOME HAS A LEGACY LOCATOR, and that is the point: an in-process event trigger
        // cannot be cancelled mid-flight and cannot time out. Handling them is required BY the
        // transition (AAP 0.5.3) rather than being an improvement layered on top of it, and each carries
        // a defined RetCode so a caller can tell it from a refusal.
        ScriptedMacroChannel channel = new ScriptedMacroChannel().ScriptFormatPrice();

        MacroDispatchPlan plan = MacroInvoker.Plan(
            DocumentedDirectReference(),
            MacroArgumentList.FromEvaluated("12.345", "1"),
            "$FormatPrice(n2, $" + PrecisionVariable + ");",
            caretPosition: 7L);

        MacroInvoker invoker = new(channel, invocationIdFactory: () => "inv-1");

        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        MacroInvocationResult result = await invoker.InvokeAsync(
            Row,
            BoundColumn(),
            plan,
            cancellationToken: cancelled.Token);

        Assert.Equal(MacroInvocationOutcome.Cancelled, result.Outcome);
        Assert.Equal(RetCode.CANCELLED, result.ReturnCode);
        Assert.True(result.Aborts);
        Assert.Null(result.Value);
        Assert.Null(result.Rendered);
        Assert.Equal("inv-1", result.InvocationId);
        Assert.Equal(1L, result.Sequence);
        Assert.Equal(0, invoker.OutstandingCount);

        // A non-positive timeout is refused at construction rather than silently abandoning every
        // invocation before the client can answer - a stalled channel dressed up as configuration.
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => new MacroInvoker(channel, TimeSpan.Zero));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => new MacroInvoker(channel, TimeSpan.FromMilliseconds(-5)));

        // The infinite timeout is accepted and normalises to "no limit of my own".
        Assert.Null(new MacroInvoker(channel, Timeout.InfiniteTimeSpan).InvocationTimeout);
        Assert.Null(new MacroInvoker(channel).InvocationTimeout);
        Assert.Equal(
            TimeSpan.FromSeconds(30),
            new MacroInvoker(channel, TimeSpan.FromSeconds(30)).InvocationTimeout);

        // The channel is exposed so a caller can prove which edge an invoker is bound to, and it is
        // required rather than optional - there is no macro implementation to fall back on.
        Assert.Same(channel, invoker.Channel);
        _ = Assert.Throws<ArgumentNullException>(() => new MacroInvoker(null!));
    }

    /// <summary>
    /// A REJECTED plan never reaches the client, and its refusal is carried through rather than
    /// re-derived.
    /// </summary>
    [Fact]
    public async Task ARejectedPlanIsAnsweredWithoutTouchingTheClientChannel()
    {
        // ORACLE - the parser refuses before dispatch ever begins [:L1306-L1310, :L1386-L1401 all
        // `return RetCode.E_INVALID_ARGUMENT`], so the event is never fired for a refused reference. The
        // channel below is scripted so that ANY invocation would succeed; proving it was never asked is
        // therefore proof of routing and not of a coincidental failure.
        ScriptedMacroChannel channel = new ScriptedMacroChannel().ScriptFormatPrice();

        MacroDispatchPlan rejected = MacroInvoker.Plan(
            DocumentedDirectReference(),
            ExpansionSigils.ContextStaticMacro,
            MacroArgumentList.FromEvaluated("12.345", "1"),
            "@$FormatPrice(n2, $" + PrecisionVariable + ");",
            caretPosition: 8L);

        Assert.True(rejected.IsRejected);

        MacroInvoker invoker = new(channel, invocationIdFactory: () => "inv-1");

        MacroInvocationResult result = await invoker.InvokeAsync(
            Row,
            BoundColumn(),
            rejected,
            cancellationToken: Ct);

        Assert.Equal(MacroInvocationOutcome.Rejected, result.Outcome);
        Assert.True(result.Aborts);

        // THE SAME refusal instance, carried rather than rebuilt - so the caret and the message the
        // scanner computed survive the step into dispatch unchanged.
        Assert.Same(rejected.Error, result.Error);
        Assert.Equal(ExpressionErrorSite.FunctionMacroContextReferenceUnsupported, result.Error!.Site);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, result.ReturnCode);
        Assert.Equal(8L, result.Error.CaretPosition);

        // NOT ASKED, and no invocation even issued - so a refusal costs no round trip.
        Assert.Equal(0, channel.InvocationCount);
        Assert.Equal(0L, invoker.IssuedInvocationCount);
        Assert.Null(result.InvocationId);
    }

    /// <summary>
    /// An invoker-imposed timeout is a DEFINED RESULT distinct from a caller cancellation, and it does
    /// not fabricate a value either.
    /// </summary>
    [Fact]
    public async Task AnElapsedInvocationTimeoutIsItsOwnDefinedResult()
    {
        // NO LEGACY LOCATOR, DELIBERATELY: an in-process event trigger cannot time out, so this outcome
        // exists because the boundary does (AAP 0.5.3). It is kept DISTINCT from cancellation because the
        // two have different causes - a caller that changed its mind versus a client that never answered
        // - and a caller that retries should only retry one of them.
        //
        // Driven by letting the invoker's own timeout elapse while the client is still working, and then
        // having the client report cancellation. The margin is twenty-fold, and a slower machine only
        // makes the elapsed timeout MORE certain rather than less.
        TimeSpan timeout = TimeSpan.FromMilliseconds(25);

        ScriptedMacroChannel channel = new ScriptedMacroChannel().ScriptFormatPrice();
        channel.OnInvoke = _ => Thread.Sleep(TimeSpan.FromMilliseconds(500));
        channel.Fault = new OperationCanceledException("the client gave up waiting");

        MacroInvoker invoker = new(channel, timeout, invocationIdFactory: () => "inv-1");

        Assert.Equal(timeout, invoker.InvocationTimeout);

        MacroDispatchPlan plan = MacroInvoker.Plan(
            DocumentedDirectReference(),
            MacroArgumentList.FromEvaluated("12.345", "1"),
            "$FormatPrice(n2, $" + PrecisionVariable + ");",
            caretPosition: 7L);

        MacroInvocationResult result = await invoker.InvokeAsync(
            Row,
            BoundColumn(),
            plan,
            cancellationToken: Ct);

        // TIMED OUT, not cancelled: the caller's token was never signalled, so the caller did not ask to
        // stop - the client simply did not answer in time.
        Assert.Equal(MacroInvocationOutcome.TimedOut, result.Outcome);
        Assert.Equal(RetCode.E_TIME_OUT, result.ReturnCode);
        Assert.NotEqual(RetCode.CANCELLED, result.ReturnCode);

        // Still no fabricated value, still aborting, still correlated, still no leaked state.
        Assert.True(result.Aborts);
        Assert.Null(result.Value);
        Assert.Null(result.Rendered);
        Assert.Equal("inv-1", result.InvocationId);
        Assert.Equal(1L, result.Sequence);
        Assert.Equal(0, invoker.OutstandingCount);
    }

    /// <summary>
    /// The retired-invocation history is BOUNDED, so a long-lived expression session cannot grow it
    /// without limit.
    /// </summary>
    [Fact]
    public async Task TheRetiredInvocationHistoryIsBounded()
    {
        // The history exists only so that a LATE answer - one for an invocation that has already finished
        // - is diagnosed as late rather than as unrecognised. That is a diagnostic nicety, and it must
        // not become a leak: an expression session lives as long as its DataWindow, and a calculation
        // over many rows issues an invocation per row per macro. An answer older than the bound is still
        // refused, just with the less specific diagnosis.
        ScriptedMacroChannel channel = new ScriptedMacroChannel().ScriptFormatPrice();
        MacroInvoker invoker = new(channel);

        MacroDispatchPlan plan = MacroInvoker.Plan(
            DocumentedDirectReference(),
            MacroArgumentList.FromEvaluated("12.345", "1"),
            "$FormatPrice(n2, $" + PrecisionVariable + ");",
            caretPosition: 7L);

        // Comfortably past the bound, so the trimming path runs many times.
        for (int index = 1; index <= 200; index++)
        {
            MacroInvocationResult result = await invoker.InvokeAsync(
                index,
                BoundColumn(),
                plan,
                cancellationToken: Ct);

            Assert.Equal(MacroInvocationOutcome.Rendered, result.Outcome);
        }

        // Nothing outstanding, the sequence is monotonic across all of them, and each row's invocation
        // carried its own row ordinal - which is what a per-row calculation looks like.
        Assert.Equal(0, invoker.OutstandingCount);
        Assert.Equal(200L, invoker.IssuedInvocationCount);
        Assert.Equal(200, channel.InvocationCount);
        Assert.Equal([1L, 2L, 3L], [.. channel.Invocations.Take(3).Select(invocation => invocation.Sequence)]);
        Assert.Equal(200L, channel.Invocations[^1].Sequence);
        Assert.Equal(200L, channel.Invocations[^1].Row);

        // And a mismatched answer is STILL refused after all that churn, so the bound trimmed history
        // without disabling the correlation check itself.
        channel.OverrideInvocationId = "not-mine";

        _ = await Assert.ThrowsAsync<MacroProtocolViolationException>(
            async () => await invoker.InvokeAsync(Row, BoundColumn(), plan, cancellationToken: Ct));
    }

    /// <summary>
    /// The class-name projection and the accepted-label list are the PAIR whose mismatch produces the
    /// preserved misspelling defect.
    /// </summary>
    /// <returns>The value, the token PowerBuilder's <c>ClassName</c> reports, whether the switch accepts it, and the locator.</returns>
    /// <remarks>
    /// This matrix asserts the ROUTING MECHANISM only - which token a value projects onto and whether the
    /// oracle's label list contains it. The rendered TEXT for each accepted arm is
    /// MacroTemporalRenderingTests.cs's, and is deliberately not restated here.
    /// </remarks>
    public static TheoryData<object, string, bool, string> ClassNameProjectionMatrix() => new()
    {
        { "text", "string", true, "n_cst_dwsvc_columnexp.sru:L2265 - case \"string\"" },
        { (short)7, "integer", true, "n_cst_dwsvc_columnexp.sru:L2267 - PowerBuilder integer is 16-bit" },
        { 7, "long", true, "n_cst_dwsvc_columnexp.sru:L2267 - PowerBuilder long is 32-bit" },
        { 7L, "long", true, "AAP 0.4.5.2 - long maps onto System.Int64 as well" },
        { 7U, "unsignedlong", true, "n_cst_dwsvc_columnexp.sru:L2267 - case \"unsignedlong\"" },
        { 7UL, "unsignedlong", true, "AAP 0.4.5.2 - unsignedlong maps onto System.UInt64 as well" },
        { 7.5d, "double", true, "n_cst_dwsvc_columnexp.sru:L2267 - case \"double\"" },
        { 7.5f, "real", true, "n_cst_dwsvc_columnexp.sru:L2267 - case \"real\"" },
        { 7.5m, "decimal", true, "n_cst_dwsvc_columnexp.sru:L2267 - case \"decimal\"" },
        { true, "boolean", true, "n_cst_dwsvc_columnexp.sru:L2269 - case \"boolean\"" },

        // THE DEFECT. PowerBuilder reports a genuine unsignedinteger as "unsignedinteger", the oracle's
        // label is misspelled "usignedinteger", and so this value alone among the integral types is
        // refused. The projection answers the CORRECT spelling on purpose, so the refusal EMERGES from
        // the same mismatch that causes it in the oracle instead of being hardcoded.
        {
            (ushort)7,
            "unsignedinteger",
            false,
            "n_cst_dwsvc_columnexp.sru:L2267/:L2291 - PRESERVED MISSPELLING DEFECT"
        },

        { new byte[] { 1 }, "blob", false, "n_cst_dwsvc_columnexp.sru:L2265-L2280 - blob has no arm" },
        { (byte)1, "byte", false, "n_cst_dwsvc_columnexp.sru:L2265-L2280 - byte has no arm" },
        { 'x', "char", false, "n_cst_dwsvc_columnexp.sru:L2265-L2280 - char has no arm" },
    };

    /// <summary>
    /// Each value projects onto the token PowerBuilder would report, and acceptance follows from the
    /// oracle's own label list.
    /// </summary>
    /// <param name="value">The value a client handler returned.</param>
    /// <param name="token">The <c>ClassName</c> token it projects onto.</param>
    /// <param name="accepted">Whether the oracle's conversion switch has an arm for that token.</param>
    /// <param name="locator">The oracle locator for this row.</param>
    [Theory]
    [MemberData(nameof(ClassNameProjectionMatrix))]
    public void EveryReturnValueProjectsOntoTheTokenPowerBuilderWouldReport(
        object value,
        string token,
        bool accepted,
        string locator)
    {
        Assert.False(string.IsNullOrWhiteSpace(locator));

        Assert.Equal(token, MacroInvoker.ProjectClassName(value));
        Assert.Equal(accepted, MacroInvoker.IsAcceptedClassName(token));

        // Acceptance decides whether the value renders at all, which is the routing this matrix pins.
        Assert.Equal(accepted, MacroInvoker.TryRender(value, out _, out string? projected));
        Assert.Equal(token, projected);
    }

    /// <summary>
    /// The accepted-label list is the oracle's, in the oracle's order, misspelling included.
    /// </summary>
    [Fact]
    public void TheAcceptedLabelListIsTheOraclesIncludingItsMisspelling()
    {
        // Read off [:L2265-L2280] and again, identically, off [:L2289-L2304]. Anything not here reaches
        // `case else` and is refused as an invalid return value.
        Assert.Equal(
            [
                "string",
                "long",
                "integer",
                "unsignedlong",
                "usignedinteger",
                "double",
                "real",
                "decimal",
                "boolean",
                "datetime",
                "date",
                "time",
            ],
            MacroInvoker.AcceptedReturnClassNames);

        // THE MISSPELLING IS PRESENT AND THE CORRECT SPELLING IS ABSENT. Both halves are asserted,
        // because adding the correct spelling would make a previously failing macro start working - a
        // behaviour change dressed up as a typo fix (C-B).
        Assert.Contains("usignedinteger", MacroInvoker.AcceptedReturnClassNames);
        Assert.DoesNotContain("unsignedinteger", MacroInvoker.AcceptedReturnClassNames);

        // A null carries no type at all in C#, which is the documented narrowing against PowerScript's
        // TYPED null - a null string is still a string there, and ClassName reports "string" for it. Both
        // the oracle and the port ABORT in that case, so the abort is preserved and only the diagnostic
        // differs (AAP 0.1.5: narrow with a defined error, never widen with a guess).
        Assert.Null(MacroInvoker.ProjectClassName(null));
        Assert.False(MacroInvoker.IsAcceptedClassName(null));
        Assert.False(MacroInvoker.TryRender(null, out string nothing, out string? noToken));
        Assert.Empty(nothing);
        Assert.Null(noToken);

        // The parenthesis wrap, applied OUTSIDE the switch to the result of both forms [:L2310]. Load
        // bearing rather than cosmetic: it stops a returned expression re-associating with the operators
        // around the macro call.
        Assert.Equal("(1+2)", MacroInvoker.WrapRendering("1+2"));
        Assert.Equal("()", MacroInvoker.WrapRendering(null));
    }

    /// <summary>
    /// The values delivered to the client are the EVALUATED arguments, never the unexpanded reference
    /// text.
    /// </summary>
    [Fact]
    public async Task TheClientReceivesEvaluatedArgumentValuesAndNotTheUnexpandedReferenceText()
    {
        // ORACLE [:L2229-L2235]
        //   for nFnArgIdx = 1 to nFnArgCnt
        //       sVal = _of_Evaluate(fns[nFnIdx].args[nFnArgIdx],row)
        //       if sVal = "!" or sVal = "?" then ... return "" end if
        //       sArgs[nFnArgIdx] = sVal
        //   next
        // EVERY argument goes through the DataWindow evaluator BEFORE the event is fired, so the handler
        // receives VALUES. That is what makes the documented handler possible at all: it feeds args[1] to
        // Double() and args[2] to Long() [docs:L126], which cannot work on the text "$精度".
        //
        // It is also why the client needs no variable table: the source text would be meaningless to a
        // client that cannot resolve it, which is the whole reason the boundary carries values.
        ScriptedMacroChannel channel = new ScriptedMacroChannel().ScriptFormatPrice();

        // The reference holds SOURCE TEXT - a column name and a static variable reference ...
        FunctionReference reference = DocumentedDirectReference();
        Assert.Equal(["n2", "$" + PrecisionVariable], reference.Args);

        // ... while the list handed to Plan holds the EVALUATED values of those same two arguments.
        MacroDispatchPlan plan = MacroInvoker.Plan(
            reference,
            MacroArgumentList.FromEvaluated("12.345", "1"),
            "$FormatPrice(n2, $" + PrecisionVariable + ");",
            caretPosition: 7L);

        _ = await DispatchAsync(channel, plan);

        MacroInvocation invocation = Assert.Single(channel.Invocations);

        // THE VALUES ARRIVED.
        Assert.Equal("12.345", invocation.Arguments[1]);
        Assert.Equal("1", invocation.Arguments[2]);

        // THE SOURCE TEXT DID NOT - neither the column reference nor the sigil-bearing variable, and no
        // sigil survives anywhere in the delivered list.
        Assert.DoesNotContain("n2", invocation.Arguments);
        Assert.DoesNotContain("$" + PrecisionVariable, invocation.Arguments);
        Assert.DoesNotContain(PrecisionVariable, invocation.Arguments);
        Assert.All(
            invocation.Arguments,
            argument => Assert.DoesNotContain("$", argument, StringComparison.Ordinal));

        // And the values are usable AS values: the double parsed both and produced a number.
        Assert.Equal(
            2,
            invocation.Arguments.Count(
                argument => double.TryParse(
                    argument,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out _)));

        // THE COUNT AGREEMENT IS ENFORCED, because a mismatch is a caller mistake and not data: the
        // oracle fills sArgs from fns[nFnIdx].args one for one, so the two lengths cannot differ there,
        // and a difference here means the wrong evaluated list was paired with this reference - which
        // would make the parse-time arity guarantee about args[1] a guarantee about the wrong list.
        ArgumentException mismatch = Assert.Throws<ArgumentException>(
            () => MacroInvoker.Plan(
                reference,
                MacroArgumentList.FromEvaluated("12.345"),
                "$FormatPrice(n2, $" + PrecisionVariable + ");",
                caretPosition: 7L));
        Assert.Equal("evaluatedArguments", mismatch.ParamName);

        // The evaluator's own failure markers, exposed for the caller that prepares the list. EXACT
        // equality, not a prefix or substring test [:L2231], so an argument that legitimately evaluates
        // to "?!" or "what?" is not a failure.
        Assert.Equal(["!", "?"], MacroInvoker.EvaluationFailureMarkers);
        Assert.True(MacroInvoker.IsEvaluationFailure("!"));
        Assert.True(MacroInvoker.IsEvaluationFailure("?"));
        Assert.False(MacroInvoker.IsEvaluationFailure("?!"));
        Assert.False(MacroInvoker.IsEvaluationFailure("what?"));
        Assert.False(MacroInvoker.IsEvaluationFailure(string.Empty));
        Assert.False(MacroInvoker.IsEvaluationFailure(null));
    }

    /// <summary>
    /// An invocation carries its session and DataWindow handle, which is what scopes a cross-DataWindow
    /// reference to one expression session.
    /// </summary>
    [Fact]
    public async Task AnInvocationCarriesItsSessionAndDataWindowHandle()
    {
        // AAP 0.6.2.3: `foreignvardata.expsvc` is a LIVE OBJECT POINTER and cannot be serialized, so a
        // cross-DataWindow reference is resolved by a SESSION-SCOPED handle instead and is supported only
        // while both DataWindows are co-resident in one session. Session identity is therefore part of
        // the protocol rather than transport detail, and an invocation must carry it.
        //
        // It stays NULLABLE so an in-process caller need not fabricate one - which is exactly how every
        // other test in this file drives the invoker.
        ScriptedMacroChannel channel = new ScriptedMacroChannel().ScriptFormatPrice();

        MacroDispatchPlan plan = MacroInvoker.Plan(
            DocumentedDirectReference(),
            MacroArgumentList.FromEvaluated("12.345", "1"),
            "$FormatPrice(n2, $" + PrecisionVariable + ");",
            caretPosition: 7L);

        MacroInvoker invoker = new(channel, invocationIdFactory: () => "inv-1");

        _ = await invoker.InvokeAsync(
            Row,
            BoundColumn(),
            plan,
            sessionId: "session-42",
            dataWindowHandle: "dw-1",
            cancellationToken: Ct);

        MacroInvocation carried = Assert.Single(channel.Invocations);
        Assert.Equal("session-42", carried.SessionId);
        Assert.Equal("dw-1", carried.DataWindowHandle);

        // The correlation pair, both pinned by the injected factory and the monotonic counter.
        Assert.Equal("inv-1", carried.InvocationId);
        Assert.Equal(1L, carried.Sequence);

        // The sequence is monotonic from 1 and is carried FOR DETECTION ONLY.
        ScriptedMacroChannel second = new ScriptedMacroChannel().ScriptFormatPrice();
        MacroInvoker counting = new(second);

        _ = await counting.InvokeAsync(Row, BoundColumn(), plan, cancellationToken: Ct);
        _ = await counting.InvokeAsync(Row, BoundColumn(), plan, cancellationToken: Ct);
        _ = await counting.InvokeAsync(Row, BoundColumn(), plan, cancellationToken: Ct);

        Assert.Equal([1L, 2L, 3L], [.. second.Invocations.Select(invocation => invocation.Sequence)]);
        Assert.Equal(3L, counting.IssuedInvocationCount);

        // Each identifier is distinct, so answers can be matched to questions even when a macro argument
        // contains another macro [:L2216-L2220].
        Assert.Equal(
            3,
            second.Invocations.Select(invocation => invocation.InvocationId).Distinct(StringComparer.Ordinal).Count());

        // Guard rails: a dispatch needs a column and a plan, both of them non-null.
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await invoker.InvokeAsync(Row, null!, plan, cancellationToken: Ct));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await invoker.InvokeAsync(Row, BoundColumn(), null!, cancellationToken: Ct));
    }
}
