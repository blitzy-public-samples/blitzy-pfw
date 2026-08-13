// =====================================================================================================
//  ColumnExpressionExpansionTests.cs - PARITY MATRIX FAMILY 3 of the four the Agent Action Plan
//  mandates, and the acceptance evidence for AAP goal G4(b): static (`$name`) versus dynamic
//  (`$$name`) column-expression expansion, "a distinction that does not survive naive serialization".
//
//  UNIT UNDER TEST
//  ---------------
//  services/dataservices-service/PowerFramework.DataServices/Expressions/ColumnExpressionEngine.cs -
//  the port of n_cst_dwsvc_columnexp.sru, at 2,435 lines the largest in-scope legacy object.
//
//  ORACLE (READ-ONLY - constraint C-C). Every `:Lnnn` locator with no file name is a line in the
//  first of these; locators into any other file name it. None of them is ever an edit target: they
//  are the only specification that exists for this behaviour.
//    ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru   the implementation
//    docs/n_cst_dwsvc_columnexp.md                                         the AUTHORITATIVE spec
//    ws_objects/pfw.tests.pbl.src/w_test_dwsvc_columnexp.srw               the live driver window
//    ws_objects/pfw.tests.pbl.src/dw_test_dwsvc_columnexp.srd              the DataWindow fixture
//    ws_objects/pfw.tests.pbl.src/dw_svc_sample_columnexp.srd              its release-12.5 twin
//    ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru              the hosting control
//    ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc.sru            the service base
//    ws_objects/pfw.common.pbl.src/replaceall.srf                          the substitution primitive
//    ws_objects/pfw.utility.container.pbl.src/n_vector.sru                 the calculation stack
//
//  BINDING CONSTRAINTS AT THIS SITE
//    C-B  Replicate legacy behaviour EXACTLY, defects included, and never correct one. Several
//         assertions below encode behaviour that looks wrong and is not: the abort sentinel, the
//         `FAILED`-not-`E_INVALID_ARGUMENT` parse arm, the double underscore in a generated compute
//         name, and the empty function name that means something. Each is annotated where it appears.
//    C-C  The legacy tree is read-only. It is read here, never written.
//    C-D  No deferred-service work. Nothing here touches UI, DPI, fonts, window geometry or Win32.
//    C-H  The 80 percent per-service line-coverage gate, which the engine dominates.
//    C-K  Document every technology-specific and boundary-specific decision - hence the locators.
//
//  NAMING CONSTRAINT. AAP section 0.4.5.3 preserves the oracle's SCREAMING_SNAKE constant spellings
//  because they travel on serialized C-04 payloads, in log records and in characterization
//  recordings. This file REFERENCES those identifiers - `CLC_UNKNOWN`, `FUNC_VAR`, `FUNC_INVOKE`,
//  `DWOSUFFIX`, `MACRO_CONTEXT`, `VAR_FOREIGN` - and DECLARES none of its own, so it needs no
//  `.editorconfig` naming suppression of its own and deliberately has none.
//
// -----------------------------------------------------------------------------------------------------
//  WHY EVERY TEST HERE IS A THEORY WITH MEMBER DATA
// -----------------------------------------------------------------------------------------------------
//  AAP section 0.6.7 prescribes "table-driven parity matrices expressed as theories with member data"
//  as the shape of the parity evidence, and the shape is doing real work rather than decorating.
//
//  The `$` versus `$$` pair is the case in point. Two separate example-shaped tests, one expecting 5
//  and one expecting 6, both pass against an implementation that decides the expansion mode ONCE PER
//  EXPRESSION - because each test only ever uses one sigil. Put the two in ONE matrix whose rows
//  differ in a single character and the row set itself becomes the assertion: the sigil, and nothing
//  else, is what changed. `WorkedExamplesDifferOnlyInTheSigil` then proves that claim about the data
//  rather than trusting the author of the data.
//
//  Two further consequences of the shape are load-bearing:
//    * ORDER. Every ordering assertion compares an ORDERED LIST and never a set. `of_CalcAll` runs
//      expressions in the order they were ADDED [w_test_dwsvc_columnexp.srw:L158-L159], which is
//      observable and matters when one expression's input is another's output. A set comparison would
//      pass on a reordered implementation.
//    * THE PAYLOAD. No row in the `$`/`$$` matrix asserts only the computed number. Each also asserts
//      all three parts of the AAP section 0.6.2.2 payload - the unexpanded source text, the bind-time
//      snapshot and the live environment - plus the PER-REFERENCE expansion mode. A naive port that
//      merely cached the computed value would satisfy the number and fail the payload, which is
//      exactly the failure the AAP warns about.
//
// -----------------------------------------------------------------------------------------------------
//  WHAT THIS FILE DELIBERATELY DOES NOT DO
// -----------------------------------------------------------------------------------------------------
//  * No mocking library and no fluent assertion library. Plain xunit `Assert` and the hand-written
//    doubles already published by TestDoubles.cs and FakeDataWindowHost.cs, which is the whole
//    dependency inventory this project is allowed (AAP section 0.5.3).
//  * No database, no network, no container, no listening socket, no file system. The engine takes
//    every collaborator by constructor injection and every one is optional, so the entire matrix runs
//    in process against a fake host.
//  * No secret, key, password, connection string or certificate in any form.
//  * No double declared here that a sibling test file already declares. ColumnExpressionEngineTests.cs
//    publishes `RecordingTraceSink`, `RecordingMacroChannel`, `RecordingSyntaxMutator`,
//    `ReentrantMacroChannel` and `RecordingExpressionServiceHost` as internals of this same assembly;
//    this file neither redeclares nor depends on any of them, and uses the shared `ScriptedTraceSink`
//    and `ScriptedMacroChannel` from TestDoubles.cs instead.
//  * No use of the gRPC service's own `ExpressionWireProjection` helper, and that is a decision rather
//    than an oversight. Section 3 asserts a property of THE CONTRACT - that `ExpressionBinding` can
//    carry all three payload parts, that they survive a real protobuf round trip, and that they suffice
//    to rebuild the expression on the far side - so it composes the wire message from the generated
//    contract types and the engine's own domain state. Borrowing the service's projection would make the
//    claim "the projection helper agrees with itself" instead; the projection's own correctness is
//    ColumnExpressionServiceContractTests.cs's subject, and duplicating it here would create a second
//    authority over the payload shape.
// =====================================================================================================

using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;

using Google.Protobuf;

using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Expressions;
using PowerFramework.Shared.Kernel;

using Xunit;

using DwBuffer = PowerFramework.Contracts.Common.V1.DwBuffer;
using ExpansionMode = PowerFramework.Contracts.DataServices.V1.ExpansionMode;
using WireColumnData = PowerFramework.Contracts.DataServices.V1.ColumnData;
using WireColumnExpData = PowerFramework.Contracts.DataServices.V1.ColumnExpData;
using WireExpressionBinding = PowerFramework.Contracts.DataServices.V1.ExpressionBinding;
using WireVarData = PowerFramework.Contracts.DataServices.V1.VarData;
using WireVarValue = PowerFramework.Contracts.DataServices.V1.VarValue;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// The column-expression EXPANSION parity matrix: the authoritative worked examples, the mechanism
/// behind them, the three-part wire payload they force, the observable fields of the seven-structure
/// model, and the cache, sentinels and calculation ordering that surround them.
/// </summary>
/// <remarks>
/// Read this file's header banner first. It records the oracle set, the rules position, and why every
/// test here is a theory over a matrix rather than a worked example on its own.
/// </remarks>
public sealed class ColumnExpressionExpansionTests
{
    // =================================================================================================
    //  THE SPECIFICATION'S OWN IDENTIFIERS, TRANSCRIBED AND NOT TRANSLATED
    // =================================================================================================
    //  docs/n_cst_dwsvc_columnexp.md:L44-L47 and w_test_dwsvc_columnexp.srw:L104-L109 name every
    //  variable in Chinese, and the names are kept byte for byte. That is not fidelity for its own
    //  sake: the parse routine scans CHARACTER BY CHARACTER over the raw expression [:L1266-L1284] and
    //  computes every name by offset arithmetic - `Mid(sExp, nMacPos + 1, nPos - nMacPos - 1)` [:L1409]
    //  - so a multi-byte identifier is precisely where a length-versus-byte-count confusion would
    //  surface. A Latin rename would exercise a different path and prove less.

    /// <summary><c>本月读数</c> - "this month's reading" [docs/n_cst_dwsvc_columnexp.md:L44].</summary>
    private const string ThisMonthReading = "本月读数";

    /// <summary><c>上月读数</c> - "last month's reading" [docs/n_cst_dwsvc_columnexp.md:L46].</summary>
    private const string LastMonthReading = "上月读数";

    /// <summary><c>下月读数</c> - "next month's reading" [docs/n_cst_dwsvc_columnexp.md:L84].</summary>
    private const string NextMonthReading = "下月读数";

    /// <summary><c>损耗</c> - "loss" [w_test_dwsvc_columnexp.srw:L104].</summary>
    private const string Loss = "损耗";

    /// <summary><c>倍率</c> - "multiplier" [w_test_dwsvc_columnexp.srw:L105].</summary>
    private const string Multiplier = "倍率";

    /// <summary><c>回程</c> - "return trip" [w_test_dwsvc_columnexp.srw:L106].</summary>
    private const string ReturnTrip = "回程";

    /// <summary><c>精度</c> - "precision" [w_test_dwsvc_columnexp.srw:L109].</summary>
    private const string Precision = "精度";

    /// <summary>
    /// The STATIC expansion sigil, <c>MACRO_FLAG</c> undoubled - syntax <c>$变量名</c>
    /// [docs/n_cst_dwsvc_columnexp.md:L41].
    /// </summary>
    private const string StaticSigil = "$";

    /// <summary>
    /// The DYNAMIC expansion sigil, <c>MACRO_FLAG</c> doubled - syntax <c>$$变量名</c>
    /// [docs/n_cst_dwsvc_columnexp.md:L60].
    /// </summary>
    private const string DynamicSigil = "$$";

    /// <summary>
    /// The first column of the fixture, the one every matrix binds its expression to
    /// [dw_test_dwsvc_columnexp.srd:L8, <c>decimal(2)</c>].
    /// </summary>
    private const string BoundColumn = "n1";

    /// <summary>
    /// xUnit1051 is an ERROR in this repository, because <c>TreatWarningsAsErrors</c> is set for test
    /// projects too. Every awaited engine call in this file therefore carries this token.
    /// </summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // =================================================================================================
    //  FIXTURE HELPERS
    // =================================================================================================

    /// <summary>
    /// An initialised, ENABLED engine over the legacy column-expression DataWindow fixture.
    /// </summary>
    /// <param name="host">Receives the fake host - five columns, four rows of zeros with null text.</param>
    /// <param name="traceSink">When supplied, the engine's own session delivers traces to it.</param>
    /// <param name="channel">When supplied, the engine gets a macro invoker over it.</param>
    /// <param name="options">Overrides the preserved defaults; null keeps them.</param>
    /// <returns>The engine, ready to bind expressions.</returns>
    /// <remarks>
    /// <para>
    /// <c>SetEnabled(true)</c> is not optional scaffolding. <c>#Enabled</c> gates the event entry points
    /// [:L214, :L334] and four calculation entry points answer <see cref="RetCode.FAILED"/> without it
    /// [:L1158, :L1201, :L1976, :L2087], and the legacy driver window opens by calling
    /// <c>dw_1.ColumnExp.of_SetEnabled(true)</c> [w_test_dwsvc_columnexp.srw:L96] before it does
    /// anything else. Reproducing that order is what makes the rest of the matrix reachable.
    /// </para>
    /// <para>
    /// THE ENGINE OWNS ITS SESSION HERE, which is deliberate for this file: an owned session has no
    /// co-resident peer, so every cross-DataWindow reference answers the session's BLOCKED code exactly
    /// as AAP section 0.6.2.3 requires. Cross-DataWindow resolution is ExpressionSessionTests.cs's
    /// subject, not this file's, and taking the private session keeps the two from overlapping.
    /// </para>
    /// </remarks>
    private static ColumnExpressionEngine NewEngine(
        out FakeDataWindowHost host,
        ScriptedTraceSink? traceSink = null,
        ScriptedMacroChannel? channel = null,
        ColumnExpressionOptions? options = null)
    {
        host = FakeDataWindowFixtures.CreateColumnExpressionFixture();

        ColumnExpressionEngine engine = new(
            options,
            evaluator: null,
            macroInvoker: channel is null ? null : new MacroInvoker(channel),
            session: null,
            syntaxMutator: null,
            traceSink: traceSink);

        engine.OnInit(host);
        engine.SetEnabled(true);

        return engine;
    }

    /// <summary>
    /// Composes a macro-variable reference from its sigil and name - <c>$name</c> or <c>$$name</c>.
    /// </summary>
    /// <param name="sigil">Either <see cref="StaticSigil"/> or <see cref="DynamicSigil"/>.</param>
    /// <param name="name">The variable name, without any sigil.</param>
    /// <returns>The reference exactly as it appears in an expression.</returns>
    /// <remarks>
    /// A helper rather than a literal per row, so a matrix row can differ from its neighbour in the
    /// SIGIL ALONE and the difference is structural rather than a promise made in a comment.
    /// </remarks>
    private static string Ref(string sigil, string name) => sigil + name;

    /// <summary>
    /// Writes a value straight into the primary buffer, bypassing the event chain.
    /// </summary>
    /// <param name="host">The fake host.</param>
    /// <param name="row">The one-based row.</param>
    /// <param name="column">The column name.</param>
    /// <param name="value">The value.</param>
    /// <remarks>
    /// The buffer write is deliberately NOT an <c>ItemChanged</c>: several rows below need the data to
    /// differ WITHOUT the reverse dependency index having been consulted, so that the test drives the
    /// index itself rather than a side effect of arranging the data.
    /// </remarks>
    private static void Poke(FakeDataWindowHost host, long row, string column, decimal value) =>
        host.SetBufferValue(DwBuffer.Primary, row, host.Column(column).Id, value);

    // =================================================================================================
    //  SECTION 1 - THE TWO AUTHORITATIVE WORKED EXAMPLES
    //             docs/n_cst_dwsvc_columnexp.md sections 2, 3 and 4
    // =================================================================================================

    /// <summary>
    /// The `$` versus `$$` matrix - the two worked examples of the authoritative legacy specification,
    /// transcribed line for line, as TWO ROWS OF ONE TABLE whose only difference is the sigil.
    /// </summary>
    /// <returns>
    /// Per row: the sigil under test; the stored form of the variable expression after binding; the
    /// value before any mutation; the value after <c>of_SetVar</c> plus <c>of_Calc</c>; how many
    /// references SURVIVE the parse; and the wire expansion mode the sigil denotes.
    /// </returns>
    /// <remarks>
    /// <para>
    /// ROW 1 - STATIC, docs/n_cst_dwsvc_columnexp.md section 2 [:L37-L54]. Syntax <c>$变量名</c> [:L41].
    /// The specification's own commentary is the expected value: "静态展开是在表达式设置的时候将对应
    /// 变量的值进行替换，后续变量的值改变不会影响对应表达式的值的改变" [:L39] - substitution happens
    /// WHEN THE EXPRESSION IS SET, and a later change to the variable does not change the result. Its
    /// worked code says so twice more, in comments on the lines that do it: "计算结果固定为5" [:L48] and
    /// "此处修改变量的值不会影响到num1的计算结果，始终为5" [:L50].
    /// </para>
    /// <para>
    /// ROW 2 - DYNAMIC, section 3 [:L56-L73]. Syntax <c>$$变量名</c> [:L60]. "动态展开是在表达式设置的
    /// 时候以变量的形式进行计算，后续变量的值改变时会影响对应表达式的值的改变" [:L58] - the reference is
    /// kept AS A VARIABLE and a later change propagates: "结果将改变为6" [:L69].
    /// </para>
    /// <para>
    /// THE STORED FORMS ARE THE MECHANISM MADE VISIBLE, and they are why this pair cannot be satisfied
    /// by caching. The static row's stored text is <c>(0) + (5)</c>: BOTH names are gone, because the
    /// parse substituted each variable's value expression in place and wrapped it in brackets
    /// [:L1434-L1435]. The dynamic row's is <c>$$上月读数 + (5)</c>: the doubled reference SURVIVES
    /// verbatim while the single one is still substituted away - in the SAME expression, which is the
    /// per-reference property in one string.
    /// </para>
    /// <para>
    /// THE SIXTH COLUMN IS THE CONTRACT'S VIEW OF THE SAME FACT. <c>ExpansionMode.Static</c> and
    /// <c>ExpansionMode.Dynamic</c> are wire values 1 and 2 of the five-valued
    /// <c>dataservices.v1.ExpansionMode</c>, and the projection from sigils onto them is
    /// <see cref="ExpansionSigils.ProjectVariableReference"/>'s, not this test's.
    /// </para>
    /// </remarks>
    public static TheoryData<string, string, decimal, decimal, int, ExpansionMode> WorkedExpansionExamples() =>
        new()
        {
            // docs/n_cst_dwsvc_columnexp.md:L49 - "$上月读数 + $本月读数", fixed at 5 for ever [:L48, :L50].
            { StaticSigil, "(0) + (5)", 5m, 5m, 0, ExpansionMode.Static },

            // docs/n_cst_dwsvc_columnexp.md:L68 - "$$上月读数 + $本月读数", which becomes 6 [:L69].
            { DynamicSigil, DynamicSigil + LastMonthReading + " + (5)", 5m, 6m, 1, ExpansionMode.Dynamic },
        };

    /// <summary>
    /// THE GOLDEN PARITY ASSERTION. The sigil on ONE reference decides whether a later
    /// <c>of_SetVar</c> reaches the expression at all - and the same row asserts every part of the
    /// payload that makes the distinction survive a network boundary.
    /// </summary>
    /// <param name="sigil">The sigil under test.</param>
    /// <param name="expectedStoredVarExp">The stored, rewritten variable expression.</param>
    /// <param name="expectedBaseline">The calculated value before the mutation.</param>
    /// <param name="expectedAfterMutation">The calculated value after the mutation.</param>
    /// <param name="expectedRetainedReferences">How many references survive the parse.</param>
    /// <param name="expectedMode">The wire expansion mode the sigil denotes.</param>
    /// <remarks>
    /// <para>
    /// THE SEQUENCE IS THE SPECIFICATION'S, STATEMENT FOR STATEMENT
    /// [docs/n_cst_dwsvc_columnexp.md:L43-L54 and :L62-L73]:
    /// </para>
    /// <code>
    /// dw_1.ColumnExp.of_AddVarExp("本月读数","5")            // a variable whose value is an expression
    /// dw_1.ColumnExp.of_AddVar("上月读数",0)                 // a variable whose value is the number 0
    /// dw_1.ColumnExp.of_AddvarExp("num1","$上月读数 + $本月读数")   // section 2: both static
    /// dw_1.ColumnExp.of_SetVar("上月读数", 1)
    /// dw_1.ColumnExp.of_Calc(1)
    /// </code>
    /// <para>
    /// ONE STATEMENT IS ADDED AND IT IS NOT IN THE SPECIFICATION: <c>of_AddExp("n1","$$num1")</c>. The
    /// specification observes its result in a screenshot of the DataWindow [:L98, :L102], which a
    /// headless test cannot read, so <c>num1</c> is bound to the fixture's first column to make the
    /// value observable. The binding is DELIBERATELY DYNAMIC - a static <c>$num1</c> would bake
    /// <c>num1</c>'s text at bind time and mask the very propagation row 2 exists to prove, so the
    /// added statement must not be allowed to decide the outcome.
    /// </para>
    /// <para>
    /// <c>of_Calc(1)</c> is the ONE-ARGUMENT arity [:L1179], which is what the specification writes
    /// [:L53, :L72] and which does NOT force input-triggered expressions.
    /// </para>
    /// <para>
    /// AND THE PAYLOAD, ALL THREE PARTS AT ONCE (AAP section 0.6.2.2). The number alone is not
    /// evidence: an implementation that memoised the computed value would answer 5 twice on row 1 for
    /// entirely the wrong reason. What distinguishes a correct port is that the binding still carries
    /// (i) the UNEXPANDED source, in which the substituted name is still present even though the stored
    /// text no longer has it; (ii) the BIND-TIME SNAPSHOT, which is the only surviving record of what a
    /// static reference was bound to; and (iii) the LIVE ENVIRONMENT, which is what a dynamic reference
    /// resolves against and which has MOVED by the end of the test. Drop any one and the pair becomes
    /// irreproducible on the far side of a boundary.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(WorkedExpansionExamples))]
    public async Task WorkedExample_TheSigilDecidesWhetherALaterMutationReachesTheExpression(
        string sigil,
        string expectedStoredVarExp,
        decimal expectedBaseline,
        decimal expectedAfterMutation,
        int expectedRetainedReferences,
        ExpansionMode expectedMode)
    {
        ColumnExpressionEngine engine = NewEngine(out FakeDataWindowHost host);

        // :L44-L45 / :L63-L64 - of_AddVarExp("本月读数","5")
        Assert.Equal(RetCode.OK, engine.AddVarExp(ThisMonthReading, "5"));

        // :L46-L47 / :L65-L66 - of_AddVar("上月读数",0)
        Assert.Equal(RetCode.OK, engine.AddVar(LastMonthReading, 0L));

        // :L49 / :L68 - the ONE line that differs between the two rows.
        string source = Ref(sigil, LastMonthReading) + " + " + Ref(StaticSigil, ThisMonthReading);
        Assert.Equal(RetCode.OK, engine.AddVarExp("num1", source));

        // The parse rewrote the stored text in place [:L1435]; the source is retained separately.
        Assert.Equal(expectedStoredVarExp, engine.GetVarExp("num1"));

        ExpressionBindingSnapshot? binding = engine.GetVariableBinding("num1");
        Assert.NotNull(binding);

        // PAYLOAD PART (i) - the UNEXPANDED source. Both names are still here, including the one the
        // rewrite removed from the stored text. Without this field a static binding is a literal.
        Assert.Equal(source, binding.UnexpandedExp);
        Assert.Contains(LastMonthReading, binding.UnexpandedExp, StringComparison.Ordinal);
        Assert.Contains(ThisMonthReading, binding.UnexpandedExp, StringComparison.Ordinal);

        // PAYLOAD PART (ii) - the BIND-TIME SNAPSHOT, recorded for EVERY macro variable the parse met,
        // static and dynamic alike, which is what makes the two rows comparable on the wire.
        Assert.Equal("0", binding.BindTimeSnapshot[LastMonthReading]);
        Assert.Equal("5", binding.BindTimeSnapshot[ThisMonthReading]);

        // PER-REFERENCE MODE - and the count is itself the mechanism: a static reference leaves NOTHING
        // behind [:L1435 substitutes and never appends], a dynamic one leaves exactly one entry [:L1477].
        Assert.Equal(expectedRetainedReferences, binding.Vars.Length);
        Assert.Empty(binding.Fns);

        foreach (VariableReference retained in binding.Vars)
        {
            Assert.Equal(LastMonthReading, retained.Name);
            Assert.True(retained.IsMacro);
            Assert.False(retained.IsCtx);
            Assert.Equal(
                expectedMode,
                ColumnExpressionEngine.SigilsFor(retained).ProjectVariableReference().Mode);
        }

        // The mode the SIGIL denotes, asserted for the static row too - where there is no retained
        // reference to read it off, because the reference is exactly what static expansion consumes.
        ExpansionSigils sigils = new(
            IsCtx: false,
            IsMacro: true,
            IsDynamic: string.Equals(sigil, DynamicSigil, StringComparison.Ordinal));
        Assert.Equal(expectedMode, sigils.ProjectVariableReference().Mode);
        Assert.False(sigils.ProjectVariableReference().IsRejected);

        // The added observation point. Dynamic on purpose - see the remarks.
        Assert.True(engine.AddExp(BoundColumn, Ref(DynamicSigil, "num1")) > 0);

        // :L53 / :L72 - of_Calc(1), the one-argument arity.
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(expectedBaseline, host.GetItemDecimal(1L, BoundColumn));

        // :L51 / :L70 - of_SetVar("上月读数", 1). The specification passes no recalculate flag.
        Assert.Equal(RetCode.OK, await engine.SetVarAsync(LastMonthReading, 1L, false, Ct));

        // PAYLOAD PART (iii) - the LIVE ENVIRONMENT has moved, while the bind-time snapshot has not.
        // Both statements are needed: the first is what a dynamic reference resolves against, the
        // second is what proves the snapshot is a bind-time record rather than a live view.
        int liveIndex = engine.Variables.IndexOf(LastMonthReading);
        Assert.NotEqual(0, liveIndex);
        Assert.Equal("1", engine.Variables[liveIndex].Local.Exp);
        Assert.Equal("0", engine.GetVariableBinding("num1")!.BindTimeSnapshot[LastMonthReading]);

        // :L52-L53 / :L71-L72 - recalculate row 1, and THIS is where the two rows part company.
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(expectedAfterMutation, host.GetItemDecimal(1L, BoundColumn));

        // Stated as the specification states it, so a regression reads as the sentence it breaks:
        // row 1 "始终为5" [:L50] - unchanged; row 2 "结果将改变为6" [:L69] - changed.
        if (string.Equals(sigil, StaticSigil, StringComparison.Ordinal))
        {
            Assert.Equal(expectedBaseline, host.GetItemDecimal(1L, BoundColumn));
        }
        else
        {
            Assert.NotEqual(expectedBaseline, host.GetItemDecimal(1L, BoundColumn));
        }
    }

    /// <summary>
    /// The names the two worked examples share, so the sigil relationship is asserted about DATA
    /// rather than promised in a comment.
    /// </summary>
    /// <returns>One row per variable name used anywhere in the matrix.</returns>
    public static TheoryData<string> ExpansionExampleVariableNames() =>
        new()
        {
            LastMonthReading,
            ThisMonthReading,
            NextMonthReading,
            "num1",
            "num2",
        };

    /// <summary>
    /// The two worked examples differ in EXACTLY ONE CHARACTER, so the sigil is demonstrably the cause
    /// of the different outcome and not one of several differences between two hand-written examples.
    /// </summary>
    /// <param name="name">The variable name to compose both references over.</param>
    /// <remarks>
    /// <para>
    /// WITHOUT THIS THE PAIR PROVES LESS THAN IT APPEARS TO. Two example tests, one expecting 5 and one
    /// expecting 6, are consistent with any number of differences between them; the claim being made is
    /// specifically that DOUBLING THE SIGIL is what changed. That claim is about the inputs, so it is
    /// asserted about the inputs: the dynamic reference is the static one with a single <c>$</c>
    /// inserted at position 0, and deleting that character recovers the static form exactly.
    /// </para>
    /// <para>
    /// It is also the scanner's own reading. <c>bDD = (Mid(sExp,nPos + 1,1) = MACRO_FLAG)</c> [:L1290] is
    /// a ONE-CHARACTER LOOKAHEAD after the first <c>$</c> - the whole of the static/dynamic decision -
    /// and <c>MACRO_FLAG</c> is <c>"$"</c> [:L1252].
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ExpansionExampleVariableNames))]
    public void WorkedExamplesDifferOnlyInTheSigil(string name)
    {
        string staticReference = Ref(StaticSigil, name);
        string dynamicReference = Ref(DynamicSigil, name);

        // The doubling is one character, and it is MACRO_FLAG [:L1252].
        Assert.Equal(ColumnExpressionEngine.MACRO_FLAG, StaticSigil);
        Assert.Equal(StaticSigil + StaticSigil, DynamicSigil);
        Assert.Equal(staticReference.Length + 1, dynamicReference.Length);

        // Deleting the inserted character recovers the static form exactly - nothing else differs.
        Assert.Equal(staticReference, dynamicReference.Remove(0, 1));

        // And the full expression sources differ by that character alone, in the same position.
        string staticSource = staticReference + " + " + Ref(StaticSigil, ThisMonthReading);
        string dynamicSource = dynamicReference + " + " + Ref(StaticSigil, ThisMonthReading);
        Assert.Equal(staticSource, dynamicSource.Remove(0, 1));
    }

    /// <summary>
    /// The DYNAMIC-INDIRECT branches of docs/n_cst_dwsvc_columnexp.md section 4 [:L75-L94], selected by
    /// the DATA in two columns rather than by the expression.
    /// </summary>
    /// <returns>
    /// Per row: the value written to <c>n2</c>; the value written to <c>n3</c>; the variable name the
    /// row's own expression therefore resolves to; and the value that variable yields.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE SPECIFICATION STATES BOTH BRANCHES AS PROSE AND SHOWS EACH AS A SCREENSHOT: "当n2 &gt; n3 时n1
    /// 的对应num1表达式，否则为num2表达式" [:L91], then "`n2` &gt; `n3`, `n1`绑定`num1`" [:L96] and
    /// "`n2` &lt;= `n3`, `n1`绑定`num2`" [:L100]. Two rows, one per screenshot.
    /// </para>
    /// <para>
    /// THE ARITHMETIC IS THE SPECIFICATION'S OWN [:L82-L89]. <c>本月读数</c> is the expression <c>"5"</c>,
    /// <c>上月读数</c> is 0 and then reassigned to 1 [:L89], <c>下月读数</c> is 10. So <c>num1</c> is
    /// <c>$$上月读数 + $本月读数</c> = 1 + 5 = 6 and <c>num2</c> is <c>$$下月读数 + $本月读数</c> = 10 + 5
    /// = 15. The 6 is itself the section-3 result carried forward, which is why the reassignment at :L89
    /// is part of the example rather than incidental to it.
    /// </para>
    /// <para>
    /// THE THIRD ROW IS THE BOUNDARY AND IT IS NOT IN THE SPECIFICATION. The condition is a STRICT
    /// <c>&gt;</c>, so equal values take the <c>否则</c> ("otherwise") branch. The specification words
    /// its second case as <c>&lt;=</c> [:L100], which asserts the boundary in prose; the row asserts it
    /// in data, because <c>&gt;</c> against <c>&gt;=</c> is a one-character difference that no other row
    /// here would catch.
    /// </para>
    /// </remarks>
    public static TheoryData<decimal, decimal, string, decimal> DynamicIndirectBranches() =>
        new()
        {
            // docs/n_cst_dwsvc_columnexp.md:L96 - n2 > n3, so n1 binds num1 = 1 + 5.
            { 9m, 2m, "num1", 6m },

            // docs/n_cst_dwsvc_columnexp.md:L100 - n2 <= n3, so n1 binds num2 = 10 + 5.
            { 2m, 9m, "num2", 15m },

            // The strict-comparison boundary: equal is NOT greater, so the otherwise branch is taken.
            { 4m, 4m, "num2", 15m },
        };

    /// <summary>
    /// <c>$$('变量名字符串')</c> - the variable is named by a DataWindow expression evaluated per row, so
    /// ONE bound expression resolves to DIFFERENT variables as the data changes.
    /// </summary>
    /// <param name="n2">The value written to <c>n2</c>.</param>
    /// <param name="n3">The value written to <c>n3</c>.</param>
    /// <param name="expectedVariable">The variable the row's data selects.</param>
    /// <param name="expectedValue">The value that variable yields.</param>
    /// <remarks>
    /// <para>
    /// BOTH BRANCHES ARE DRIVEN BY CHANGING THE COLUMN VALUES AND NEVER THE EXPRESSION. That is the
    /// whole content of the third expansion mode: the binding cannot be resolved at parse time at all,
    /// because the name comes out of an expression over two columns and can differ per row and change
    /// when the data does. Re-binding a different expression per branch would test the parser twice and
    /// the mode not at all.
    /// </para>
    /// <para>
    /// THIS IS WHAT <c>FUNC_VAR = ""</c> IS FOR [:L120]. The parser records <c>$$(...)</c> as a FUNCTION
    /// reference whose name is the EMPTY STRING and which requires exactly one argument
    /// [:L1388-L1392], and the empty name is the sentinel that routes it to variable lookup rather than
    /// to a client callback. Reading that empty name as an absent name is the natural mistake, and it
    /// would delete <c>EXPANSION_MODE_DYNAMIC_INDIRECT</c> - wire value 3 of five - from the contract.
    /// </para>
    /// <para>
    /// NOTE THE MODE OF THE OUTER REFERENCE IS <c>DynamicIndirect</c> AND NOT <c>Dynamic</c>, and the
    /// projection is not this test's to make: it is
    /// <see cref="ExpansionSigils.ProjectFunctionReference"/> applied to
    /// <see cref="MacroFunctionDispatch.VariableLookup"/>.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(DynamicIndirectBranches))]
    public async Task DynamicIndirect_BindsTheVariableTheRowsOwnExpressionNames(
        decimal n2,
        decimal n3,
        string expectedVariable,
        decimal expectedValue)
    {
        ColumnExpressionEngine engine = NewEngine(out FakeDataWindowHost host);

        // docs/n_cst_dwsvc_columnexp.md:L82-L84 - the three variables, verbatim.
        Assert.Equal(RetCode.OK, engine.AddVarExp(ThisMonthReading, "5"));
        Assert.Equal(RetCode.OK, engine.AddVar(LastMonthReading, 0L));
        Assert.Equal(RetCode.OK, engine.AddVar(NextMonthReading, 10L));

        // :L86-L87 - two expression variables, each mixing one dynamic and one static reference.
        Assert.Equal(
            RetCode.OK,
            engine.AddVarExp(
                "num1",
                Ref(DynamicSigil, LastMonthReading) + " + " + Ref(StaticSigil, ThisMonthReading)));
        Assert.Equal(
            RetCode.OK,
            engine.AddVarExp(
                "num2",
                Ref(DynamicSigil, NextMonthReading) + " + " + Ref(StaticSigil, ThisMonthReading)));

        // :L89 - of_SetVar("上月读数", 1). num1 becomes 6 precisely because its reference is dynamic.
        Assert.Equal(RetCode.OK, await engine.SetVarAsync(LastMonthReading, 1L, false, Ct));

        // :L92 - of_SetExp("n1","$$(if(n2 > n3 ,'num1','num2'))"). Note the space before the comma; it
        // is the specification's own spacing and is carried through unchanged.
        Assert.Equal(RetCode.OK, await engine.SetExpAsync(BoundColumn, "$$(if(n2 > n3 ,'num1','num2'))", Ct));

        // The parse recorded ONE function reference with the FUNC_VAR sentinel name, and NO variable
        // reference at all - the variable is not named until calculation time.
        ColumnExpressionData bound = Assert.Single(engine.Expressions);
        Assert.Equal(0, bound.Vars.UpperBound);
        Assert.Equal(1, bound.Fns.UpperBound);
        Assert.Equal(MacroSentinels.FUNC_VAR, bound.Fns[1].Name);
        Assert.True(bound.Fns[1].Builtin);
        Assert.True(MacroSentinels.IsFuncVar(bound.Fns[1].Name));

        // The wire mode for this reference is the THIRD of the five, projected from the dispatch arm.
        Assert.Equal(
            ExpansionMode.DynamicIndirect,
            ExpansionSigils.DynamicMacro
                .ProjectFunctionReference(MacroFunctionDispatch.VariableLookup)
                .Mode);

        // The DATA selects the branch. Nothing about the expression changes between rows.
        Poke(host, 1L, "n2", n2);
        Poke(host, 1L, "n3", n3);

        // :L93 - of_Calc(1)
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(expectedValue, host.GetItemDecimal(1L, BoundColumn));

        // And the value really is the named variable's, rather than a coincidence of arithmetic: the
        // selected variable's own live definition produces the same number.
        int selected = engine.Variables.IndexOf(expectedVariable);
        Assert.NotEqual(0, selected);
        Assert.Equal(
            expectedValue,
            decimal.Parse(
                engine.CalcVarExpValue(1L, host.DwObject(BoundColumn), selected, 1L, host.DwObject(BoundColumn), engine),
                CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The live scenario the legacy driver window opens with [w_test_dwsvc_columnexp.srw:L104-L117],
    /// with the branch its own data selects made into the matrix parameter.
    /// </summary>
    /// <returns>
    /// Per row: the value expression bound to <c>上月读数</c>; the variable the comparison therefore
    /// selects; the value it yields; and the ordered first arguments of the macro invocations the
    /// calculation issues.
    /// </returns>
    /// <remarks>
    /// <para>
    /// ROW 1 IS THE WINDOW'S OWN SCENARIO, UNMODIFIED [:L104-L117]. Six variables, four expression
    /// variables built on a macro function, one bound column, one <c>of_Calc(1)</c>. It is richer than
    /// the specification's examples in four ways that each exercise something the examples do not:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     <description>
    ///       A variable's value is a QUOTED STRING - <c>of_AddVarExp("回程","'3'")</c> [:L106]. A value
    ///       expression is expression TEXT, not a value, so the quotes are part of it and the bracket
    ///       wrap at [:L1434] produces <c>('3')</c>. Anything that stripped or escaped them would break
    ///       the arithmetic that consumes it.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       Static references NEST INSIDE A MACRO FUNCTION'S ARGUMENT LIST -
    ///       <c>"$FormatPrice($上月读数,$精度)"</c> [:L111]. Both inner references are substituted while
    ///       the function reference itself survives, so one expression carries both a rewritten part and
    ///       a retained part.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       The DYNAMIC-INDIRECT selector's own argument is built FROM static references -
    ///       <c>"$$(if($num1-$num2&gt;0 ,'exp1','exp2'))"</c> [:L116] - and each of those variables'
    ///       values is itself macro-bearing, so the merge at [:L1437-L1460] carries the inner function
    ///       references up into the outer expression. Three function references end up recorded for one
    ///       bound expression.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       The macro is the one the specification documents, <c>Round(Double(args[1]),Long(args[2]))</c>
    ///       [docs/n_cst_dwsvc_columnexp.md:L126], reproduced by <c>MacroScripts.FormatPrice</c>.
    ///     </description>
    ///   </item>
    /// </list>
    /// <para>
    /// THE ARITHMETIC, END TO END. <c>num1</c> = FormatPrice(4, 0) = 4 and <c>num2</c> = FormatPrice(5, 0)
    /// = 5, so <c>num1 - num2</c> is -1, which is not greater than 0, so the selector resolves to
    /// <c>'exp2'</c> = FormatPrice((5 + dec('3')) * 2 + 1, 0) = FormatPrice(17, 0) = 17.
    /// </para>
    /// <para>
    /// ROW 2 CHANGES ONE VALUE AND NOTHING ELSE. <c>上月读数</c> becomes 6, so <c>num1 - num2</c> is 1,
    /// the selector resolves to <c>'exp1'</c> = FormatPrice(5 * 2 + 1, 0) = 11, and a DIFFERENT one of
    /// the four expression variables is consulted. The change is made where the window makes it - at
    /// DEFINITION time [:L107] - because <c>$上月读数</c> is a STATIC reference inside <c>num1</c> and a
    /// later <c>of_SetVar</c> could not reach it. That is the section-2 rule doing observable work
    /// inside the section-4 mechanism.
    /// </para>
    /// <para>
    /// THE FOURTH COLUMN IS AN ORDERED LIST AND NOT A SET. Three macro invocations are issued per row
    /// and the LAST one carries the selected branch's computed total, which is only meaningful because
    /// the two operands were computed first. A set comparison would accept them in any order and so
    /// would accept an implementation that evaluated the branch before its condition.
    /// </para>
    /// </remarks>
    public static TheoryData<string, string, decimal, string[]> LiveScenarioBranches() =>
        new()
        {
            // w_test_dwsvc_columnexp.srw:L107 as written - 上月读数 = "4" - selects exp2.
            { "4", "exp2", 17m, ["5", "4", "17"] },

            // The same scenario with 上月读数 = "6", which selects exp1 instead.
            { "6", "exp1", 11m, ["5", "6", "11"] },
        };

    /// <summary>
    /// The legacy driver window's opening scenario, reproduced statement for statement, with both
    /// branches of its selector driven by the value of one variable.
    /// </summary>
    /// <param name="lastMonthReadingExp">The value expression bound to <c>上月读数</c>.</param>
    /// <param name="expectedVariable">The expression variable the selector resolves to.</param>
    /// <param name="expectedValue">The value the bound column receives.</param>
    /// <param name="expectedMacroArguments">The ordered first arguments of the macro invocations.</param>
    /// <remarks>
    /// <para>
    /// <c>Dec</c> IS REGISTERED BY THIS TEST AND THAT IS DELIBERATE, DOCUMENTED, AND NOT A GAP BEING
    /// PAPERED OVER. In the legacy the expression evaluator IS the PowerBuilder runtime, whose whole
    /// built-in library - <c>Dec</c> included - is available to any DataWindow expression. The port's
    /// evaluator is a net-new reimplementation of <c>Describe("Evaluate(...)")</c> (AAP section 0.6.5)
    /// and registers exactly the functions the IN-SCOPE legacy objects actually reach, each with the
    /// locator that reaches it. <c>dec(...)</c> is reached only from this DRIVER WINDOW's expression
    /// [:L114], which is a characterization fixture and is permanently out of scope (AAP section
    /// 0.2.2.3), so its absence from the default table is a scope boundary rather than a defect. The
    /// evaluator publishes <c>RegisterFunction</c> as a public extension point for exactly this: a host
    /// completing the table for expressions it intends to evaluate. Supplying it here is what lets the
    /// fixture's expression be reproduced verbatim instead of being edited to suit the port - and
    /// editing it would violate C-C in spirit even though the legacy file itself is untouched.
    /// </para>
    /// <para>
    /// The conversion reproduces PowerScript's: a numeric or numeric-string argument converts, a null
    /// stays null through the argument result, and a non-numeric string yields 0 rather than raising -
    /// the same lenient contract the evaluator's own <c>Double</c> and <c>Long</c> registrations carry.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(LiveScenarioBranches))]
    public async Task LiveScenario_FromTheLegacyDriverWindow(
        string lastMonthReadingExp,
        string expectedVariable,
        decimal expectedValue,
        string[] expectedMacroArguments)
    {
        ScriptedMacroChannel channel = new ScriptedMacroChannel().ScriptFormatPrice();
        ScriptedTraceSink sink = new();
        ColumnExpressionEngine engine = NewEngine(out FakeDataWindowHost host, sink, channel);

        // w_test_dwsvc_columnexp.srw:L98 - of_SetTrace(true), which the window does before anything else.
        Assert.Equal(RetCode.OK, engine.SetTrace(true));

        RegisterPowerScriptDec(engine);

        // :L104-L108 - five expression variables. Note :L106's QUOTED value and that :L107 is the row's
        // parameter, set here at DEFINITION time because $上月读数 is static inside num1.
        Assert.Equal(RetCode.OK, engine.AddVarExp(Loss, "1"));
        Assert.Equal(RetCode.OK, engine.AddVarExp(Multiplier, "2"));
        Assert.Equal(RetCode.OK, engine.AddVarExp(ReturnTrip, "'3'"));
        Assert.Equal(RetCode.OK, engine.AddVarExp(LastMonthReading, lastMonthReadingExp));
        Assert.Equal(RetCode.OK, engine.AddVarExp(ThisMonthReading, "5"));

        // :L109 - of_AddVar("精度",0), a VALUE variable rather than an expression variable.
        Assert.Equal(RetCode.OK, engine.AddVar(Precision, 0L));

        // :L111-L114 - the four expression variables built on the documented macro function.
        Assert.Equal(
            RetCode.OK,
            engine.AddVarExp(
                "num1",
                "$" + MacroScripts.FormatPriceName + "($" + LastMonthReading + ",$" + Precision + ")"));
        Assert.Equal(
            RetCode.OK,
            engine.AddVarExp(
                "num2",
                "$" + MacroScripts.FormatPriceName + "($" + ThisMonthReading + ",$" + Precision + ")"));
        Assert.Equal(
            RetCode.OK,
            engine.AddVarExp(
                "exp1",
                "$" + MacroScripts.FormatPriceName + "($" + ThisMonthReading + "*$" + Multiplier
                    + "+$" + Loss + ",$" + Precision + ")"));
        Assert.Equal(
            RetCode.OK,
            engine.AddVarExp(
                "exp2",
                "$" + MacroScripts.FormatPriceName + "(($" + ThisMonthReading + "+dec($" + ReturnTrip
                    + "))*$" + Multiplier + "+$" + Loss + ",$" + Precision + ")"));

        // THE QUOTED VALUE SURVIVES WITH ITS QUOTES, wrapped rather than unwrapped or escaped [:L1434].
        Assert.Contains("dec(('3'))", engine.GetVarExp("exp2"), StringComparison.Ordinal);

        // Every inner static reference is gone from the stored text; the function reference is not.
        Assert.Equal(
            "$" + MacroScripts.FormatPriceName + "((" + lastMonthReadingExp + "),(0))",
            engine.GetVarExp("num1"));
        Assert.DoesNotContain(Precision, engine.GetVarExp("num1"), StringComparison.Ordinal);
        Assert.DoesNotContain(LastMonthReading, engine.GetVarExp("num1"), StringComparison.Ordinal);

        // :L116 - of_SetExp("n1","$$(if($num1-$num2>0 ,'exp1','exp2'))")
        Assert.Equal(
            RetCode.OK,
            await engine.SetExpAsync(
                BoundColumn,
                "$$(if($num1-$num2>0 ,'exp1','exp2'))",
                Ct));

        ColumnExpressionData bound = Assert.Single(engine.Expressions);

        // The merge carried the inner function references up: the FUNC_VAR selector plus the two
        // FormatPrice calls that came in with $num1 and $num2 [:L1437-L1460].
        Assert.True(bound.HasMacro);
        Assert.Equal(3, bound.Fns.UpperBound);
        Assert.Equal(MacroSentinels.FUNC_VAR, bound.Fns[1].Name);
        Assert.True(bound.Fns[1].Builtin);
        Assert.Equal(MacroScripts.FormatPriceName, bound.Fns[2].Name);
        Assert.Equal(MacroScripts.FormatPriceName, bound.Fns[3].Name);

        // The two merged references are NOT built-in: they are `$Fn(...)`, the client-callback form.
        Assert.False(bound.Fns[2].Builtin);
        Assert.False(bound.Fns[3].Builtin);

        // Every static reference was consumed, so no VARIABLE reference survives at all.
        Assert.Equal(0, bound.Vars.UpperBound);

        // :L117 - of_Calc(1)
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(expectedValue, host.GetItemDecimal(1L, BoundColumn));

        // ORDERED, not a set: the two operands are computed before the selected branch's total.
        ImmutableArray<string> actualMacroArguments =
            [.. channel.Invocations.Select(invocation => invocation.Arguments[1])];
        Assert.Equal<IEnumerable<string>>(expectedMacroArguments, actualMacroArguments);
        Assert.All(
            channel.Invocations,
            invocation =>
            {
                Assert.Equal(MacroScripts.FormatPriceName, invocation.Name);

                // `$Fn(...)` is the DIRECT form, wire mode 4 of five, and the client owns it.
                Assert.Equal(ExpansionMode.MacroDirect, invocation.Form);
                Assert.False(invocation.Builtin);
            });

        // The selected variable is the one the row names, and it alone yields the answer.
        int selected = engine.Variables.IndexOf(expectedVariable);
        Assert.NotEqual(0, selected);
        Assert.Equal(expectedVariable, engine.Variables[selected].Name);

        // The trace carries the FULLY PREPROCESSED expression and the value [:L751-L758], so the
        // resolved total appears in the payload rather than the unexpanded selector.
        ExpressionTraceRecord traced = Assert.Single(sink.For(BoundColumn));
        Assert.Equal(BoundColumn, traced.Stack);
        Assert.Equal(
            expectedValue.ToString(CultureInfo.InvariantCulture),
            traced.Value);
        Assert.Contains(
            expectedValue.ToString(CultureInfo.InvariantCulture),
            traced.Expression,
            StringComparison.Ordinal);

        // Nothing was reported: the whole scenario parses, preprocesses and calculates cleanly.
        Assert.Null(engine.LastError);
    }

    /// <summary>
    /// Registers PowerScript's <c>Dec()</c> conversion on an engine's evaluator.
    /// </summary>
    /// <param name="engine">The engine whose evaluator is completed.</param>
    /// <remarks>
    /// See the remarks on <see cref="LiveScenario_FromTheLegacyDriverWindow"/> for why this is supplied
    /// by the test rather than by the product. The semantics are PowerScript's and match the evaluator's
    /// own <c>Double</c> registration: convert what converts, keep a null null, and answer 0 for text
    /// that is not a number rather than raising - an expression is data, and the oracle answers a
    /// sentinel for a bad expression rather than throwing [:L761].
    /// </remarks>
    private static void RegisterPowerScriptDec(ColumnExpressionEngine engine)
    {
        Assert.NotNull(engine.Evaluator);

        engine.Evaluator.RegisterFunction(
            "Dec",
            static invocation =>
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

                if (argument.Value.IsNull)
                {
                    return invocation.Success(ExpressionValue.Null);
                }

                return invocation.Success(
                    argument.Value.TryGetDecimal(out decimal number)
                        ? ExpressionValue.FromDecimal(number)
                        : ExpressionValue.FromDecimal(0m));
            });
    }

    // =================================================================================================
    //  SECTION 2 - THE MECHANISM, NOT JUST THE OUTCOME
    // =================================================================================================
    //  Section 1 asserts WHAT the two sigils produce. This section asserts WHY, because the two are not
    //  the same claim: an implementation that memoised a static reference's computed value would answer
    //  every number in section 1 correctly and still be wrong, and the difference is only visible in the
    //  stored text and the retained references. Every row here reads the mechanism directly.

    /// <summary>
    /// The static rewrite matrix - what the parse LEAVES BEHIND in the stored expression.
    /// </summary>
    /// <returns>
    /// Per row: the variable definitions to install, as name and value expression; the expression source;
    /// and the stored text the rewrite must produce.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE FIVE-LINE MECHANISM, QUOTED. [:L1431-L1435] is the entire static path:
    /// </para>
    /// <code>
    /// sVarExp = GlobalVars[nVarIdx].local.exp
    /// if sVarExp = "" then ... return RetCode.FAILED
    /// sVarExp = "(" + sVarExp + ")"
    /// sExp = ReplaceAll(sExp,varData.fullName,sVarExp,true,true)
    /// </code>
    /// <para>
    /// ROW 1 IS THE PARENTHESIS WRAP AND IT IS THE SINGLE MOST CONSEQUENTIAL LINE IN THE PATH. Without
    /// the wrap, <c>$v * 3</c> over <c>v = "1 + 2"</c> stores <c>1 + 2 * 3</c>, which evaluates to 7 -
    /// a silently wrong number with no error raised anywhere. With it the stored text is
    /// <c>(1 + 2) * 3</c> and the answer is 9. Nothing in the return code, the reference arrays or the
    /// error list distinguishes the two; only the stored text does.
    /// </para>
    /// <para>
    /// ROW 2 IS THE GLOBALITY. <c>ReplaceAll</c> replaces EVERY occurrence, so one reference written
    /// twice is substituted twice - the substitution is not a single positional edit at the point the
    /// scanner happened to stop.
    /// </para>
    /// <para>
    /// ROWS 3 AND 4 ARE THE WHOLE-TOKEN SEMANTICS, and they are why the fifth argument is <c>true</c>. A
    /// global replace of <c>$aa</c> in <c>$aa + $aab</c> without whole-token matching produces
    /// <c>(1) + (1)b</c> - a corrupted expression from a correct-looking substitution. Two variables
    /// whose names are a PREFIX PAIR are the only way to reach it, and the pair is asserted in both
    /// declaration orders so neither row can pass merely because the longer name happened to be defined
    /// first.
    /// </para>
    /// <para>
    /// THE NAME LENGTHS IN ROWS 3 AND 4 ARE CHOSEN SO EACH SUBSTITUTION IS LENGTH NEUTRAL - <c>$aa</c>
    /// and <c>(1)</c> are both three characters, <c>$aab</c> and <c>(20)</c> both four - and that is not
    /// cosmetic. A LENGTHENING substitution truncates the scan and leaves a later reference unexpanded,
    /// which is a separate preserved defect asserted on its own in
    /// <see cref="StaticExpansion_ALengtheningRewriteTruncatesTheScan"/>. Short names here would confound
    /// the two, and a reader would not be able to tell which mechanism the row was about.
    /// </para>
    /// <para>
    /// ROW 5 IS THE QUOTED VALUE from w_test_dwsvc_columnexp.srw:L106. A value expression is TEXT, so
    /// its quotes are part of it and the wrap goes outside them: <c>('3')</c>, never <c>'3'</c> and never
    /// a re-escaped form.
    /// </para>
    /// </remarks>
    public static TheoryData<string[], string[], string, string> StaticRewriteCases() =>
        new()
        {
            // The precedence guard: 9 and not 7.
            { ["v"], ["1 + 2"], "$v * 3", "(1 + 2) * 3" },

            // Every occurrence, not just the first - one ReplaceAll call covers both.
            { ["a"], ["7"], "$a + $a", "(7) + (7)" },

            // Whole tokens only: the short name must not match inside the long one.
            { ["aa", "aab"], ["1", "20"], "$aa + $aab", "(1) + (20)" },

            // The same guarantee with the declaration order reversed.
            { ["aab", "aa"], ["20", "1"], "$aab + $aa", "(20) + (1)" },

            // The quoted value expression, wrapped outside its own quotes.
            { [ReturnTrip], ["'3'"], "dec($" + ReturnTrip + ")", "dec(('3'))" },
        };

    /// <summary>
    /// Static expansion REWRITES THE EXPRESSION IN PLACE at bind time: the variable's name is gone from
    /// the stored text, its value expression stands in its place, and the substituted value is wrapped
    /// in brackets.
    /// </summary>
    /// <param name="names">The variable names to define, in order.</param>
    /// <param name="valueExpressions">Their value expressions, positionally matched to the names.</param>
    /// <param name="source">The expression source to bind.</param>
    /// <param name="expectedStored">The stored text the rewrite must produce.</param>
    /// <remarks>
    /// THE NAME'S ABSENCE IS THE ASSERTION THAT MATTERS MOST, because it is the CAUSE of the section-2
    /// worked example rather than a symptom of it. Once the name is gone there is nothing for a later
    /// <c>of_SetVar</c> to find, which is why the result is fixed for ever rather than merely cached -
    /// and an implementation that cached instead would keep the name and pass every numeric assertion.
    /// </remarks>
    [Theory]
    [MemberData(nameof(StaticRewriteCases))]
    public void StaticExpansion_RewritesInPlaceWithAParenthesisWrap(
        string[] names,
        string[] valueExpressions,
        string source,
        string expectedStored)
    {
        Assert.Equal(names.Length, valueExpressions.Length);

        ColumnExpressionEngine engine = NewEngine(out _);

        for (int position = 0; position < names.Length; position++)
        {
            Assert.Equal(RetCode.OK, engine.AddVarExp(names[position], valueExpressions[position]));
        }

        Assert.True(engine.AddExp(BoundColumn, source) > 0);

        // The rewrite, exactly [:L1435].
        Assert.Equal(expectedStored, engine.GetExp(BoundColumn));

        // Every substituted value is bracketed [:L1434] - assert the brackets are present around each.
        foreach (string valueExpression in valueExpressions)
        {
            Assert.Contains("(" + valueExpression + ")", expectedStored, StringComparison.Ordinal);
        }

        // NOT ONE NAME SURVIVES in the stored text, while every one is still in the source.
        foreach (string name in names)
        {
            Assert.DoesNotContain(name, engine.GetExp(BoundColumn), StringComparison.Ordinal);
            Assert.Contains(name, source, StringComparison.Ordinal);
        }

        // Nothing is retained: a consumed static reference leaves no entry [:L1435 substitutes and
        // never appends, unlike the dynamic branch at :L1477].
        ColumnExpressionData bound = Assert.Single(engine.Expressions);
        Assert.Equal(0, bound.Vars.UpperBound);

        // And the UNEXPANDED source is still recoverable, which is the only reason the wire payload can
        // reconstruct what was bound.
        Assert.Equal(source, engine.GetSourceExp(1));
    }

    /// <summary>
    /// The single-reference subset of <see cref="StaticRewriteCases"/>, where one call to the shared
    /// text primitive reproduces the whole rewrite.
    /// </summary>
    /// <returns>Per row: the variable name, its value expression, and the expression source.</returns>
    public static TheoryData<string, string, string> SingleReferenceStaticRewriteCases() =>
        new()
        {
            { "v", "1 + 2", "$v * 3" },
            { "a", "7", "$a + $a" },
            { ReturnTrip, "'3'", "dec($" + ReturnTrip + ")" },
            { Precision, "0", "round($" + Precision + " + 1,0)" },
        };

    /// <summary>
    /// The substitution is the SHARED <c>ReplaceAll</c> primitive with the oracle's own arguments, not a
    /// bespoke replace that happens to agree on easy inputs.
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="valueExpression">Its value expression.</param>
    /// <param name="source">The expression source.</param>
    /// <remarks>
    /// <para>
    /// [:L1435] calls <c>ReplaceAll(sExp, varData.fullName, sVarExp, true, true)</c> - the FIVE-ARGUMENT
    /// form of the framework's own primitive, ported as
    /// <see cref="Text.ReplaceAll(string?, string?, string?, bool, bool)"/>. Both trailing arguments are
    /// load-bearing: <c>matchCase</c> is true because variable names are case-sensitive [:L997], and
    /// <c>keywordReplace</c> is true because a name must match as a whole token.
    /// </para>
    /// <para>
    /// THE ASSERTION IS AN AGREEMENT AND NOT AN INSPECTION. Reflecting over the call site would prove
    /// which method was called and nothing about the result; comparing the engine's stored text against
    /// the primitive's own output over the same three arguments proves the behaviour, and it keeps
    /// proving it if the primitive's whole-token rules are ever refined - because both sides move
    /// together, which is exactly the property sharing the primitive exists to buy.
    /// </para>
    /// <para>
    /// The replacement key is <c>varData.fullName</c> - the substring the scanner matched WITH ITS SIGIL
    /// [:L1412] - and not the bare name, so the composed key here carries the <c>$</c>.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(SingleReferenceStaticRewriteCases))]
    public void StaticExpansion_SubstitutesThroughTheSharedReplaceAllPrimitive(
        string name,
        string valueExpression,
        string source)
    {
        ColumnExpressionEngine engine = NewEngine(out _);
        Assert.Equal(RetCode.OK, engine.AddVarExp(name, valueExpression));
        Assert.True(engine.AddExp(BoundColumn, source) > 0);

        // The oracle's own three arguments: the full token as the key, the BRACKETED value expression as
        // the replacement, case-sensitive, whole-token.
        string expected = Text.ReplaceAll(
            source,
            Ref(StaticSigil, name),
            "(" + valueExpression + ")",
            true,
            true);

        Assert.Equal(expected, engine.GetExp(BoundColumn));

        // And the primitive really did something - a vacuous agreement would prove nothing.
        Assert.NotEqual(source, expected);
    }

    /// <summary>
    /// Prefix-pair names whose static substitution makes the expression LONGER, so the scan is truncated
    /// and the second reference is never parsed.
    /// </summary>
    /// <returns>
    /// Per row: the two names; their two value expressions; the source; and the stored text, in which the
    /// SECOND reference is still present verbatim.
    /// </returns>
    public static TheoryData<string, string, string, string, string, string> LengtheningRewriteCases() =>
        new()
        {
            // `$a` -> `(1)` grows by one character, pushing the trailer past the captured scan bound.
            { "a", "1", "ab", "20", "$a + $ab", "(1) + $ab" },

            // A longer value grows it by more, with the same outcome - the amount does not matter.
            { "b", "1234", "bc", "9", "$b + $bc", "(1234) + $bc" },
        };

    /// <summary>
    /// A LENGTHENING static substitution leaves a later reference UNSCANNED, so it survives into the
    /// stored text as a literal token and fails at calculation time. PRESERVED DEFECT (constraint C-B) -
    /// reproduced deliberately and never corrected.
    /// </summary>
    /// <param name="firstName">The first variable's name.</param>
    /// <param name="firstValue">The first variable's value expression.</param>
    /// <param name="secondName">The second variable's name, of which the first is a prefix.</param>
    /// <param name="secondValue">The second variable's value expression.</param>
    /// <param name="source">The expression source naming both.</param>
    /// <param name="expectedStored">The stored text, with the second reference unexpanded.</param>
    /// <remarks>
    /// <para>
    /// THE CAUSE IS A POWERSCRIPT LOOP SEMANTIC AND NOT A TYPO. The scan is <c>for nPos = 1 to nLen</c>
    /// [:L1266] and PowerScript evaluates a <c>FOR</c> limit ONCE, before the first iteration. The static
    /// path does refresh <c>nLen</c> after rewriting [:L1436], but that assignment cannot extend a bound
    /// already captured - so when the rewrite makes the expression longer, its trailing characters fall
    /// outside the scan, the <c>;</c> trailer that would have terminated the second reference is never
    /// reached, and the reference is never recorded.
    /// </para>
    /// <para>
    /// THE REFRESHED <c>nLen</c> IS STILL LOAD-BEARING, so it cannot be deleted as dead: [:L1509] strips
    /// the trailer with <c>Left(sExp, nLen - 1)</c> and needs the CURRENT length. One variable, stale for
    /// one purpose and current for another, both observable.
    /// </para>
    /// <para>
    /// WHY IT WAS NEVER NOTICED. Every variable in the authoritative specification and in the driver
    /// window has a name LONGER than its value - <c>上月读数</c> against <c>4</c> - so every substitution
    /// there SHORTENS the expression and the bound is never exceeded. That is also why this row set uses
    /// deliberately short Latin names: they are the only way to reach the defect at all.
    /// </para>
    /// <para>
    /// THE ONE MERCY IS THAT IT IS LOUD. The unexpanded token is not silently ignored: the evaluator
    /// cannot make sense of <c>$ab</c>, so the defect surfaces as a REPORTED expression error and the
    /// column keeps its previous value rather than acquiring a fabricated one.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(LengtheningRewriteCases))]
    public async Task StaticExpansion_ALengtheningRewriteTruncatesTheScan(
        string firstName,
        string firstValue,
        string secondName,
        string secondValue,
        string source,
        string expectedStored)
    {
        ColumnExpressionEngine engine = NewEngine(out FakeDataWindowHost host);

        Assert.Equal(RetCode.OK, engine.AddVarExp(firstName, firstValue));
        Assert.Equal(RetCode.OK, engine.AddVarExp(secondName, secondValue));

        // The substitution really is lengthening, which is the precondition for the defect.
        Assert.True(("(" + firstValue + ")").Length > Ref(StaticSigil, firstName).Length);

        Assert.True(engine.AddExp(BoundColumn, source) > 0);

        // The first reference expanded; the second is still sitting there as a literal token.
        Assert.Equal(expectedStored, engine.GetExp(BoundColumn));
        Assert.Contains(Ref(StaticSigil, secondName), engine.GetExp(BoundColumn), StringComparison.Ordinal);

        // It was not RETAINED either, so nothing will ever expand it: the expression does not even know
        // it has a macro in it.
        Assert.Equal(0, engine.Expressions[0].Vars.UpperBound);
        Assert.Equal(0, engine.Expressions[0].Fns.UpperBound);
        Assert.False(engine.Expressions[0].HasMacro);

        // At calculation time it is REPORTED rather than silently mis-evaluated, and no value is written.
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.NotNull(engine.LastError);
        Assert.Equal(ExpressionErrorSite.ExpressionError, engine.LastError.Site);
        Assert.Equal(0m, host.GetItemDecimal(1L, BoundColumn));
    }

    /// <summary>
    /// The dynamic-retention matrix: one reference kept, one variable mutated, and the propagation that
    /// follows.
    /// </summary>
    /// <returns>
    /// Per row: the variable name; its value at bind time; the value assigned afterwards; the expression
    /// source; the value before the assignment; and the value after it.
    /// </returns>
    /// <remarks>
    /// The names are chosen so that one row uses a MULTI-BYTE name and one a single-character name, and
    /// so that one row's arithmetic changes sign - because a reference whose resolution silently failed
    /// would read as zero, which a same-sign row could not distinguish from a small change.
    /// </remarks>
    public static TheoryData<string, long, long, string, decimal, decimal> DynamicRetentionCases() =>
        new()
        {
            { LastMonthReading, 0L, 1L, Ref(DynamicSigil, LastMonthReading) + " + 5", 5m, 6m },
            { "v", 4L, 40L, Ref(DynamicSigil, "v") + " * 2", 8m, 80m },
            { "d", 3L, -3L, Ref(DynamicSigil, "d") + " + 1", 4m, -2m },
        };

    /// <summary>
    /// Dynamic expansion RETAINS THE REFERENCE, and the retained entry's index points into the LIVE
    /// global variable table - which is the mechanism by which a later mutation propagates.
    /// </summary>
    /// <param name="name">The variable name.</param>
    /// <param name="boundValue">Its value at bind time.</param>
    /// <param name="assignedValue">The value assigned after binding.</param>
    /// <param name="source">The expression source.</param>
    /// <param name="expectedBefore">The value before the assignment.</param>
    /// <param name="expectedAfter">The value after it.</param>
    /// <remarks>
    /// <para>
    /// THE INDEX IS THE WHOLE OF THE MECHANISM. <c>vardata.index</c> [:L53] is a ONE-BASED index into
    /// <c>GlobalVars</c>, and preprocessing resolves the reference by subscripting that table at
    /// calculation time [:L2189-L2200]. So the retained entry is a POINTER INTO LIVE STATE, not a copy of
    /// a value - which is why writing to the table is visible to the expression, and why the wire payload
    /// has to carry the live environment for the far side to be able to do the same.
    /// </para>
    /// <para>
    /// A ZERO INDEX IS A LEGITIMATE BINDING STATE AND NOT AN ERROR: the legacy resolves a zero by NAME at
    /// calculation time and memoises the answer back into the array [:L2189-L2195]. The assertion here is
    /// therefore that the index, once resolved, AGREES with the table's own lookup by name - not merely
    /// that it is non-zero.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(DynamicRetentionCases))]
    public async Task DynamicExpansion_RetainsTheReferenceIntoTheLiveVariableTable(
        string name,
        long boundValue,
        long assignedValue,
        string source,
        decimal expectedBefore,
        decimal expectedAfter)
    {
        ColumnExpressionEngine engine = NewEngine(out FakeDataWindowHost host);

        Assert.Equal(RetCode.OK, engine.AddVar(name, boundValue));
        Assert.True(engine.AddExp(BoundColumn, source) > 0);

        // The reference SURVIVES the parse, sigil and all [:L1477].
        ColumnExpressionData bound = Assert.Single(engine.Expressions);
        VariableReference retained = Assert.Single(bound.Vars.Items);
        Assert.Equal(name, retained.Name);
        Assert.Equal(Ref(DynamicSigil, name), retained.FullName);
        Assert.True(retained.IsMacro);
        Assert.False(retained.IsCtx);

        // The stored text still carries it - nothing was substituted away.
        Assert.Equal(source, engine.GetExp(BoundColumn));
        Assert.Contains(Ref(DynamicSigil, name), engine.GetExp(BoundColumn), StringComparison.Ordinal);

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(expectedBefore, host.GetItemDecimal(1L, BoundColumn));

        // The index, once resolved, names the same entry the table's own by-name lookup does.
        int liveIndex = engine.Variables.IndexOf(name);
        Assert.NotEqual(0, liveIndex);
        Assert.True(engine.Variables.IsValidIndex(liveIndex));
        Assert.Equal(name, engine.Variables[liveIndex].Name);
        Assert.Equal(ExpressionVariableEnvironment.VAR_LOCAL, engine.Variables[liveIndex].VarType);

        VariableReference resolved = Assert.Single(engine.Expressions[0].Vars.Items);
        Assert.Equal(liveIndex, resolved.Index);

        // Write to the LIVE table, and the retained reference sees it.
        Assert.Equal(RetCode.OK, await engine.SetVarAsync(name, assignedValue, false, Ct));
        Assert.Equal(
            assignedValue.ToString(CultureInfo.InvariantCulture),
            engine.Variables[liveIndex].Local.Exp);

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(expectedAfter, host.GetItemDecimal(1L, BoundColumn));
        Assert.NotEqual(expectedBefore, expectedAfter);
    }

    /// <summary>
    /// A sequence of assignments to a statically bound variable, each of which must change nothing.
    /// </summary>
    /// <returns>One row per assigned value.</returns>
    /// <remarks>
    /// Several values rather than one, and including the bind-time value itself, so the assertion is
    /// that NOTHING reaches the expression rather than that one particular value did not.
    /// </remarks>
    public static TheoryData<long> AssignmentsAfterAStaticBind() =>
        new() { 0L, 1L, 7L, -5L, long.MaxValue };

    /// <summary>
    /// After a static bind there is NOTHING LEFT FOR AN ASSIGNMENT TO REACH - which is the cause of the
    /// fixed result, and an implementation that merely cached the value would fail this while passing
    /// every numeric assertion in section 1.
    /// </summary>
    /// <param name="assignedValue">The value assigned after binding.</param>
    /// <remarks>
    /// <para>
    /// THE PROOF IS STRUCTURAL AND IS MADE IN THREE PARTS. The stored text does not contain the name;
    /// the expression retains no reference to it; and the reverse dependency search that
    /// <c>of_SetVar</c> drives - <c>_of_FindVarDepends</c>, reached through the variable-changed event
    /// [:L340] - therefore finds nothing to recalculate. Assert only the value and a caching
    /// implementation is indistinguishable; assert all three and it is not.
    /// </para>
    /// <para>
    /// <c>of_SetVar</c> IS CALLED WITH <c>recalc</c> TRUE HERE, which is stronger than the
    /// specification's own call [:L51]. The specification passes no flag and then calls
    /// <c>of_Calc</c> explicitly; asking for the recalculation as part of the assignment gives the
    /// implementation every opportunity to propagate, and it still must not.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(AssignmentsAfterAStaticBind))]
    public async Task AfterAStaticBind_NoAssignmentCanReachTheExpression(long assignedValue)
    {
        ColumnExpressionEngine engine = NewEngine(out FakeDataWindowHost host);

        Assert.Equal(RetCode.OK, engine.AddVar(LastMonthReading, 2L));
        Assert.True(engine.AddExp(BoundColumn, Ref(StaticSigil, LastMonthReading) + " + 3") > 0);

        // The name is already gone before anything is calculated.
        Assert.Equal("(2) + 3", engine.GetExp(BoundColumn));
        Assert.DoesNotContain(LastMonthReading, engine.GetExp(BoundColumn), StringComparison.Ordinal);
        Assert.Equal(0, engine.Expressions[0].Vars.UpperBound);

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(5m, host.GetItemDecimal(1L, BoundColumn));

        // Assign, and ask for the recalculation as part of the assignment.
        Assert.Equal(RetCode.OK, await engine.SetVarAsync(LastMonthReading, assignedValue, true, Ct));

        // The live table moved.
        int liveIndex = engine.Variables.IndexOf(LastMonthReading);
        Assert.Equal(
            assignedValue.ToString(CultureInfo.InvariantCulture),
            engine.Variables[liveIndex].Local.Exp);

        // The expression did not, in text or in value, before or after an explicit recalculation.
        Assert.Equal("(2) + 3", engine.GetExp(BoundColumn));
        Assert.Equal(5m, host.GetItemDecimal(1L, BoundColumn));

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(5m, host.GetItemDecimal(1L, BoundColumn));

        // The bind-time snapshot still records what was baked in, which is the ONLY surviving record.
        ExpressionBindingSnapshot? binding = engine.GetBinding(1);
        Assert.NotNull(binding);
        Assert.Equal("2", binding.BindTimeSnapshot[LastMonthReading]);
        Assert.Equal(Ref(StaticSigil, LastMonthReading) + " + 3", binding.UnexpandedExp);
    }

    /// <summary>
    /// The <c>@</c> context sigil's ACCEPTED forms - a third sigil the published specification never
    /// mentions.
    /// </summary>
    /// <returns>
    /// Per row: the expression source; whether the retained reference is context-scoped; whether it is a
    /// macro; the full token the parser captured; the wire expansion mode; and the value produced.
    /// </returns>
    /// <remarks>
    /// <para>
    /// docs/n_cst_dwsvc_columnexp.md DOCUMENTS TWO SIGILS AND THERE ARE THREE. The specification names
    /// <c>$</c> [:L41] and <c>$$</c> [:L60] and never mentions <c>@</c> at all, while the oracle declares
    /// it as a first-class scanner constant - <c>constant string MACRO_CONTEXT = "@"</c> [:L1253] - and
    /// describes the grammar only in its own internal comment block [:L1220-L1222]. An implementation
    /// built from the published specification alone would drop the whole branch, and
    /// <c>vardata.isctx</c> [:L54] would read as a vestigial field.
    /// </para>
    /// <para>
    /// ROW 1 - <c>@name</c>, a context COLUMN read. <c>isCtx</c> set, <c>isMacro</c> CLEAR [:L1296-L1297],
    /// resolved by reading the named column out of the calling context's row [:L2185]. With no peer
    /// supplying a context the context IS this engine and this row, which is what makes a self-referential
    /// <c>@name</c> behave like a plain column reference.
    /// </para>
    /// <para>
    /// ROW 2 - <c>@$$name</c>, a context macro in its DYNAMIC form, which is the only context macro form
    /// the parser accepts. Both bits set plus the doubling, resolved against the context service
    /// [:L2181-L2183].
    /// </para>
    /// <para>
    /// THE CONTEXT BIT IS NOT A MODE, and the fifth column says so. <c>@name</c> projects to
    /// <c>Unspecified</c> because the sigil selects a resolution SCOPE and not an expansion TIMING, and
    /// <c>@$$name</c> projects to plain <c>Dynamic</c> for the same reason - the scope travels separately
    /// on <c>VarData.is_ctx</c>. Folding the context bit into the five wire modes would need a sixth and
    /// a seventh value that the contract deliberately does not have.
    /// </para>
    /// </remarks>
    public static TheoryData<string, bool, bool, string, ExpansionMode, decimal> ContextReferenceForms() =>
        new()
        {
            // @name - the context COLUMN read. n2 is poked to 7 by the test body.
            { "@n2 + 1", true, false, "@n2", ExpansionMode.Unspecified, 8m },

            // @$$name - the context MACRO, dynamic form. v is defined as 3 by the test body.
            { "@$$v + 1", true, true, "@$$v", ExpansionMode.Dynamic, 4m },
        };

    /// <summary>
    /// The undocumented <c>@</c> sigil is parsed as a CONTEXT reference, is retained rather than
    /// expanded, and keeps its sigils in the full token the parser captured.
    /// </summary>
    /// <param name="source">The expression source.</param>
    /// <param name="expectedIsCtx">Whether the retained reference is context-scoped.</param>
    /// <param name="expectedIsMacro">Whether it is a macro rather than a plain column read.</param>
    /// <param name="expectedFullName">The full token, sigils included.</param>
    /// <param name="expectedMode">The wire expansion mode.</param>
    /// <param name="expectedValue">The value the calculation produces.</param>
    /// <remarks>
    /// <para>
    /// THE FULL NAME IS LOAD-BEARING AND IS ASSERTED FOR THAT REASON. It is the exact substring the
    /// parser matched, right-trimmed [:L1412], so it carries every sigil verbatim - and it is also the
    /// REPLACEMENT KEY that preprocessing substitutes on [:L2214]. Note the name extraction has to skip
    /// the <c>@</c> when computing <c>name</c> and then put it back for <c>fullName</c>, which is the
    /// <c>nMacPos++ ... nMacPos--</c> shift at [:L1405] and [:L1411]; a consumer that rebuilt the token
    /// from the bare name plus a guessed sigil would break substitution.
    /// </para>
    /// <para>
    /// A CONTEXT REFERENCE IS NEVER STATICALLY EXPANDED, so both rows retain their reference and neither
    /// stored expression differs from its source. That is not a coincidence of these two rows: the only
    /// context form that would reach the static branch is refused outright, which
    /// <see cref="ContextStaticExpansion_IsRefused"/> asserts.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ContextReferenceForms))]
    public async Task ContextSigil_IsParsedAsAContextReference(
        string source,
        bool expectedIsCtx,
        bool expectedIsMacro,
        string expectedFullName,
        ExpansionMode expectedMode,
        decimal expectedValue)
    {
        ColumnExpressionEngine engine = NewEngine(out FakeDataWindowHost host);

        // The sigil is the oracle's own constant, and it is not the macro flag.
        Assert.Equal("@", ColumnExpressionEngine.MACRO_CONTEXT);
        Assert.NotEqual(ColumnExpressionEngine.MACRO_FLAG, ColumnExpressionEngine.MACRO_CONTEXT);

        // Both row setups, so a row differs from its neighbour only in its source expression.
        Assert.Equal(RetCode.OK, engine.AddVar("v", 3L));
        Poke(host, 1L, "n2", 7m);

        Assert.True(engine.AddExp(BoundColumn, source) > 0);

        // RETAINED, never substituted: the stored text is the source unchanged.
        Assert.Equal(source, engine.GetExp(BoundColumn));

        ColumnExpressionData bound = Assert.Single(engine.Expressions);
        VariableReference retained = Assert.Single(bound.Vars.Items);

        Assert.Equal(expectedIsCtx, retained.IsCtx);
        Assert.Equal(expectedIsMacro, retained.IsMacro);
        Assert.Equal(expectedFullName, retained.FullName);

        // The bare name has the sigils stripped; the full token keeps every one of them.
        Assert.DoesNotContain(ColumnExpressionEngine.MACRO_CONTEXT, retained.Name, StringComparison.Ordinal);
        Assert.DoesNotContain(ColumnExpressionEngine.MACRO_FLAG, retained.Name, StringComparison.Ordinal);
        Assert.StartsWith(ColumnExpressionEngine.MACRO_CONTEXT, retained.FullName, StringComparison.Ordinal);
        Assert.EndsWith(retained.Name, retained.FullName, StringComparison.Ordinal);

        // The projection, and the context bit deliberately does NOT change the mode.
        ExpansionModeProjection projection =
            ColumnExpressionEngine.SigilsFor(retained).ProjectVariableReference();
        Assert.Equal(expectedMode, projection.Mode);
        Assert.False(projection.IsRejected);
        Assert.Equal(ExpansionRejection.None, projection.Rejection);

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(expectedValue, host.GetItemDecimal(1L, BoundColumn));
        Assert.Null(engine.LastError);
    }

    /// <summary>
    /// Every placement of a context variable written with a SINGLE sigil, each of which the parser must
    /// refuse.
    /// </summary>
    /// <returns>One row per source, with the variable it names.</returns>
    /// <remarks>
    /// Three placements - leading, trailing and inside a function call - so the refusal is demonstrably a
    /// property of the SIGIL COMBINATION rather than of where in the expression it happened to appear.
    /// </remarks>
    public static TheoryData<string, string> ContextStaticExpansionRefusals() =>
        new()
        {
            { "v", "@$v + 1" },
            { "v", "1 + @$v" },
            { Precision, "round(@$" + Precision + " + 1,0)" },
        };

    /// <summary>
    /// <c>@$name</c> - context plus STATIC expansion - is REFUSED outright, with
    /// <see cref="RetCode.E_INVALID_ARGUMENT"/> and the oracle's own error site. PRESERVED RESTRICTION
    /// (constraint C-B): it is the legacy's refusal, not a gap in this port.
    /// </summary>
    /// <param name="name">The variable the source names.</param>
    /// <param name="source">The expression source.</param>
    /// <remarks>
    /// <para>
    /// THE REFUSAL IS INHERENT RATHER THAN INCIDENTAL. A static substitution is performed ONCE, at bind
    /// time [:L1435], and at bind time there is no calling context to resolve a context-scoped variable
    /// against - the <c>(ctx_row, ctx_dwo, ctx_expsvc)</c> triple only exists during a calculation
    /// [:L2148]. So the combination is not merely unimplemented; it has no meaning. [:L1415-L1419] is the
    /// guard, and it sits BEFORE the mode decision for exactly that reason.
    /// </para>
    /// <para>
    /// THE ERROR SITE CARRIES THE ORACLE'S OWN LINE NUMBER AS ITS VALUE -
    /// <c>FunctionMacroContextVariableStaticExpansionUnsupported = 1417</c> - so the structured error the
    /// dialog became is traceable straight back to the <c>MessageBox</c> it replaced. And the site is what
    /// distinguishes this refusal from the ten other parse arms that answer the same return code.
    /// </para>
    /// <para>
    /// THE PROJECTION AGREES WITH THE PARSER, which is the second half of the assertion. The engine
    /// refuses at parse time; <see cref="ExpansionSigils.ProjectVariableReference"/> independently reports
    /// <see cref="ExpansionRejection.ContextStaticExpansion"/> for the same bit pattern, so a contract
    /// consumer holding only the sigils reaches the same conclusion the parser did rather than inventing a
    /// sixth mode for it.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ContextStaticExpansionRefusals))]
    public void ContextStaticExpansion_IsRefused(string name, string source)
    {
        ColumnExpressionEngine engine = NewEngine(out _);
        Assert.Equal(RetCode.OK, engine.AddVar(name, 3L));

        // A failed parse answers the return code through the index-returning entry point.
        Assert.Equal((int)RetCode.E_INVALID_ARGUMENT, engine.AddExp(BoundColumn, source));

        // Nothing was bound: the expression is refused whole, not partially registered.
        Assert.Equal(0, engine.ExpressionCount);
        Assert.Empty(engine.Expressions);

        Assert.NotNull(engine.LastError);
        Assert.Equal(
            ExpressionErrorSite.FunctionMacroContextVariableStaticExpansionUnsupported,
            engine.LastError.Site);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, engine.LastError.ReturnCode);

        // The site's numeric value IS the oracle's line number [:L1417].
        Assert.Equal(1417, (int)engine.LastError.Site);

        // And the sigil projection reaches the same verdict from the bits alone.
        ExpansionModeProjection projection =
            ExpansionSigils.ContextStaticMacro.ProjectVariableReference();
        Assert.True(projection.IsRejected);
        Assert.Equal(ExpansionRejection.ContextStaticExpansion, projection.Rejection);
        Assert.Equal(ExpansionMode.Unspecified, projection.Mode);
    }

    // =================================================================================================
    //  SECTION 3 - THE WIRE PAYLOAD: THREE PARTS, AND A MODE PER REFERENCE
    // =================================================================================================
    //  AAP section 0.6.2.2 states the requirement in one sentence: "the payload must transmit all three
    //  of the unexpanded source expression, the bind-time variable snapshot, and the live variable
    //  environment - plus, per reference, which expansion mode applied." Each clause is a matrix here,
    //  and the last row set proves the requirement is not merely satisfied on paper by rebuilding an
    //  expression on the FAR SIDE of a serialization round trip and mutating it there.

    /// <summary>
    /// The five wire expansion modes, each with the numeral the contract assigns it and the sigil form
    /// that produces it.
    /// </summary>
    /// <returns>Per row: the mode, its wire numeral, and the source form that denotes it.</returns>
    /// <remarks>
    /// <para>
    /// FIVE VALUES AND NOT THREE OR SEVEN. <c>dataservices.v1.ExpansionMode</c> declares
    /// <c>UNSPECIFIED = 0</c> plus exactly five named modes, and AAP section 0.6.2.2 enumerates them:
    /// static, dynamic, dynamic-indirect, macro-direct and macro-dynamic. The numerals are asserted
    /// because they, and not the C# member names, are what travels: a protobuf enum is a varint on the
    /// wire, so a renumbering would silently reinterpret every stored characterization recording.
    /// </para>
    /// <para>
    /// TWO OF THE FIVE DECORATE A VARIABLE AND THREE DECORATE A FUNCTION, and that split is the grammar's
    /// rather than a simplification. A VARIABLE reference can only be static or dynamic. The other three
    /// are FUNCTION forms: <c>$$('name')</c> is recorded as a function whose name is the empty
    /// <c>FUNC_VAR</c> sentinel [:L120, :L1388], <c>$Fn(args)</c> is the client-callback direct form
    /// [:L2286-L2287], and <c>$$Invoke('Fn',args)</c> is the dynamic form whose function name is itself a
    /// variable [:L121, :L1393].
    /// </para>
    /// </remarks>
    public static TheoryData<ExpansionMode, int, string> WireExpansionModes() =>
        new()
        {
            { ExpansionMode.Unspecified, 0, "a bare reference or a context column read" },
            { ExpansionMode.Static, 1, "$name" },
            { ExpansionMode.Dynamic, 2, "$$name" },
            { ExpansionMode.DynamicIndirect, 3, "$$('name')" },
            { ExpansionMode.MacroDirect, 4, "$Fn(args)" },
            { ExpansionMode.MacroDynamic, 5, "$$Invoke('Fn',args)" },
        };

    /// <summary>
    /// The contract declares exactly these six <c>ExpansionMode</c> values with exactly these numerals,
    /// and every one is reachable from the sigil projections.
    /// </summary>
    /// <param name="mode">The mode.</param>
    /// <param name="expectedNumeral">Its wire numeral.</param>
    /// <param name="sourceForm">The source form that denotes it, for the failure message.</param>
    [Theory]
    [MemberData(nameof(WireExpansionModes))]
    public void WireExpansionMode_CarriesItsContractNumeral(
        ExpansionMode mode,
        int expectedNumeral,
        string sourceForm)
    {
        Assert.NotEmpty(sourceForm);
        Assert.Equal(expectedNumeral, (int)mode);
        Assert.Contains(mode, Enum.GetValues<ExpansionMode>());

        // Six declared values and no more: five modes plus the unspecified default, which is what the
        // per-reference projections between them have to cover.
        Assert.Equal(6, Enum.GetValues<ExpansionMode>().Length);
    }

    /// <summary>
    /// The complete projection table from sigil bits and dispatch arm onto the five wire modes.
    /// </summary>
    /// <returns>
    /// Per row: the sigil bits; the function dispatch arm, or null for a variable reference; the mode; and
    /// the rejection, if the parser refuses the combination.
    /// </returns>
    /// <remarks>
    /// EVERY ROW IS A GRAMMAR FORM WITH AN ORACLE LOCATOR, and the two refusals are rows in their own
    /// right rather than omissions. The table is exhaustive over the forms the parser can reach, which is
    /// what makes "exactly one of the five" an assertion rather than a hope: a combination that mapped to
    /// two modes, or to none, would have to appear here as a contradiction.
    /// </remarks>
    public static TheoryData<ExpansionSigils, MacroFunctionDispatch?, ExpansionMode, ExpansionRejection>
        ExpansionModeProjections() =>
        new()
        {
            // Variable references. `$name` [:L1415] and `$$name` [:L1290, :L1406].
            { ExpansionSigils.Bare, null, ExpansionMode.Unspecified, ExpansionRejection.None },
            { ExpansionSigils.StaticMacro, null, ExpansionMode.Static, ExpansionRejection.None },
            { ExpansionSigils.DynamicMacro, null, ExpansionMode.Dynamic, ExpansionRejection.None },

            // `@name` is a scope and not a timing [:L2185]; `@$$name` is dynamic in that scope [:L2181].
            { ExpansionSigils.Context, null, ExpansionMode.Unspecified, ExpansionRejection.None },
            { ExpansionSigils.ContextDynamicMacro, null, ExpansionMode.Dynamic, ExpansionRejection.None },

            // `@$name` - reachable from the grammar and REFUSED [:L1415-L1419].
            {
                ExpansionSigils.ContextStaticMacro,
                null,
                ExpansionMode.Unspecified,
                ExpansionRejection.ContextStaticExpansion
            },

            // Function references, one row per dispatch arm [:L1386-L1401, :L2237-L2287].
            {
                ExpansionSigils.StaticMacro,
                MacroFunctionDispatch.Application,
                ExpansionMode.MacroDirect,
                ExpansionRejection.None
            },
            {
                ExpansionSigils.DynamicMacro,
                MacroFunctionDispatch.VariableLookup,
                ExpansionMode.DynamicIndirect,
                ExpansionRejection.None
            },
            {
                ExpansionSigils.DynamicMacro,
                MacroFunctionDispatch.DynamicInvoke,
                ExpansionMode.MacroDynamic,
                ExpansionRejection.None
            },

            // A context FUNCTION macro is refused whatever the name would have been [:L1306-L1310], so
            // the dispatch arm is supplied and deliberately ignored.
            {
                ExpansionSigils.ContextDynamicMacro,
                MacroFunctionDispatch.DynamicInvoke,
                ExpansionMode.Unspecified,
                ExpansionRejection.ContextFunctionMacro
            },
        };

    /// <summary>
    /// Every reference form projects onto EXACTLY ONE of the five wire modes, or onto one of the two
    /// combinations the legacy parser refuses.
    /// </summary>
    /// <param name="sigils">The three sigil bits.</param>
    /// <param name="dispatch">The dispatch arm for a function reference, or null for a variable.</param>
    /// <param name="expectedMode">The single mode the combination denotes.</param>
    /// <param name="expectedRejection">The refusal, or <see cref="ExpansionRejection.None"/>.</param>
    [Theory]
    [MemberData(nameof(ExpansionModeProjections))]
    public void EveryReferenceForm_ProjectsOntoExactlyOneWireMode(
        ExpansionSigils sigils,
        MacroFunctionDispatch? dispatch,
        ExpansionMode expectedMode,
        ExpansionRejection expectedRejection)
    {
        ExpansionModeProjection projection = dispatch is null
            ? sigils.ProjectVariableReference()
            : sigils.ProjectFunctionReference(dispatch.Value);

        Assert.Equal(expectedMode, projection.Mode);
        Assert.Equal(expectedRejection, projection.Rejection);
        Assert.Equal(expectedRejection != ExpansionRejection.None, projection.IsRejected);

        // A refusal carries no mode, and a mode carries no refusal - the two are exclusive.
        if (projection.IsRejected)
        {
            Assert.Equal(ExpansionMode.Unspecified, projection.Mode);
        }
    }

    /// <summary>
    /// One expression carrying two references with two different timings, so a per-EXPRESSION mode could
    /// not represent it.
    /// </summary>
    /// <returns>
    /// Per row: the source; the mode of the surviving reference; and the name that survives.
    /// </returns>
    /// <remarks>
    /// THE SPECIFICATION ITSELF MIXES THEM IN ONE STRING. docs/n_cst_dwsvc_columnexp.md:L68 is
    /// <c>"$$上月读数 + $本月读数"</c> - one doubled sigil and one single one - and its comment on the very
    /// next line says the result CHANGES, which is only true of the doubled one. Both orderings are
    /// asserted so the property cannot be an artefact of which reference the scanner met first.
    /// </remarks>
    public static TheoryData<string, ExpansionMode, string> MixedModeExpressions() =>
        new()
        {
            // docs/n_cst_dwsvc_columnexp.md:L68, verbatim - dynamic first.
            {
                Ref(DynamicSigil, LastMonthReading) + " + " + Ref(StaticSigil, ThisMonthReading),
                ExpansionMode.Dynamic,
                LastMonthReading
            },

            // The same two references with the sigils swapped, so the static one comes first.
            {
                Ref(StaticSigil, LastMonthReading) + " + " + Ref(DynamicSigil, ThisMonthReading),
                ExpansionMode.Dynamic,
                ThisMonthReading
            },
        };

    /// <summary>
    /// THE EXPANSION MODE IS A PROPERTY OF A REFERENCE AND NEVER OF AN EXPRESSION - asserted on the wire
    /// messages themselves, including the absence of any per-expression mode field.
    /// </summary>
    /// <param name="source">The mixed-mode expression source.</param>
    /// <param name="expectedMode">The mode of the reference that survives the parse.</param>
    /// <param name="expectedSurvivingName">The name of that reference.</param>
    /// <remarks>
    /// <para>
    /// <c>dataservices.v1.VarData</c> CARRIES <c>expansion_mode</c> AS FIELD 6 and
    /// <c>dataservices.v1.ColumnExpData</c> CARRIES NO SUCH FIELD AT ALL. That asymmetry is the contract
    /// encoding the per-reference property structurally, and both halves are asserted by reflection over
    /// the generated types - the presence, because a consumer needs it, and the ABSENCE, because a
    /// well-meaning addition of a convenience mode field to the expression message would create a second,
    /// necessarily wrong, source of truth for a mixed expression.
    /// </para>
    /// <para>
    /// AN EXPRESSION MIXING BOTH TIMINGS RETAINS EXACTLY ONE REFERENCE, and which one depends only on
    /// which sigil was doubled - so the static reference's absence is as much part of the evidence as the
    /// dynamic reference's presence.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(MixedModeExpressions))]
    public void MixedModeExpression_TagsEachReferenceIndividually(
        string source,
        ExpansionMode expectedMode,
        string expectedSurvivingName)
    {
        ColumnExpressionEngine engine = NewEngine(out _);

        Assert.Equal(RetCode.OK, engine.AddVar(LastMonthReading, 0L));
        Assert.Equal(RetCode.OK, engine.AddVarExp(ThisMonthReading, "5"));
        Assert.True(engine.AddExp(BoundColumn, source) > 0);

        ColumnExpressionData bound = Assert.Single(engine.Expressions);

        // Exactly one reference survives, and it is the doubled one.
        VariableReference retained = Assert.Single(bound.Vars.Items);
        Assert.Equal(expectedSurvivingName, retained.Name);
        Assert.Equal(Ref(DynamicSigil, expectedSurvivingName), retained.FullName);

        // ON THE WIRE THE MODE RIDES ON THE REFERENCE. The message is composed here from the reference and
        // the engine's own sigil projection, because what is being asserted is that VarData HAS somewhere
        // to put a per-reference mode - and that the mode the engine derives is the one that goes there.
        WireVarData wireVariable = new()
        {
            Name = retained.Name,
            FullName = retained.FullName,
            Index = retained.Index,
            IsCtx = retained.IsCtx,
            IsMacro = retained.IsMacro,
            ExpansionMode = ColumnExpressionEngine.SigilsFor(retained).ProjectVariableReference().Mode,
        };

        Assert.Equal(expectedMode, wireVariable.ExpansionMode);
        Assert.Equal(expectedSurvivingName, wireVariable.Name);
        Assert.Equal(retained.FullName, wireVariable.FullName);
        Assert.Equal(retained.Index, wireVariable.Index);
        Assert.True(wireVariable.IsMacro);
        Assert.False(wireVariable.IsCtx);

        // The other reference is gone entirely, because static expansion consumed it.
        string consumedName = string.Equals(expectedSurvivingName, LastMonthReading, StringComparison.Ordinal)
            ? ThisMonthReading
            : LastMonthReading;
        Assert.DoesNotContain(
            consumedName,
            bound.Vars.Items.Select(reference => reference.Name));

        // BOTH are still in the bind-time snapshot, which is how the far side learns that the missing
        // one was a static binding rather than a literal that was always there.
        ExpressionBindingSnapshot? binding = engine.GetBinding(1);
        Assert.NotNull(binding);
        Assert.True(binding.BindTimeSnapshot.ContainsKey(consumedName));
        Assert.True(binding.BindTimeSnapshot.ContainsKey(expectedSurvivingName));

        // The expression message carries its references and has NO mode of its own.
        WireColumnExpData wireExpression = new() { Name = bound.Name, Exp = bound.Exp };
        wireExpression.Vars.Add(wireVariable);
        WireVarData projectedReference = Assert.Single(wireExpression.Vars);
        Assert.Equal(expectedMode, projectedReference.ExpansionMode);

        Assert.Contains(
            nameof(WireVarData.ExpansionMode),
            typeof(WireVarData).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name));

        Assert.DoesNotContain(
            typeof(WireColumnExpData).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name),
            name => name.Contains("Expansion", StringComparison.Ordinal));
    }

    /// <summary>
    /// Mixed-mode bindings to project onto the wire, serialize, and REBUILD ON THE FAR SIDE.
    /// </summary>
    /// <returns>
    /// Per row: the statically bound variable's name and value; the dynamically bound variable's name and
    /// value; the baseline result; the value later assigned to the dynamic variable and the result that
    /// follows; and the value later assigned to the static variable, which must change nothing.
    /// </returns>
    /// <remarks>
    /// One row over Latin names and one over the specification's own multi-byte names, because the payload
    /// carries variable names as protobuf map KEYS and a map key is a UTF-8 string on the wire - so a
    /// multi-byte name is where an encoding fault in the round trip would surface.
    /// </remarks>
    public static TheoryData<string, long, string, long, decimal, long, decimal, long>
        RoundTrippedBindingCases() =>
        new()
        {
            { "stat", 10L, "dyn", 1L, 11m, 5L, 15m, 100L },
            { LastMonthReading, 2L, ThisMonthReading, 3L, 5m, 30L, 32m, 99L },
        };

    /// <summary>
    /// THE PAYLOAD IS SUFFICIENT AND THE EXPANDED TEXT IS NOT: a binding projected onto the wire,
    /// serialized, parsed back and rebuilt on the FAR SIDE still distinguishes its static reference from
    /// its dynamic one - and mutating the dynamic one there still propagates.
    /// </summary>
    /// <param name="staticName">The statically bound variable's name.</param>
    /// <param name="staticValue">Its value at bind time.</param>
    /// <param name="dynamicName">The dynamically bound variable's name.</param>
    /// <param name="dynamicValue">Its value at bind time.</param>
    /// <param name="expectedBaseline">The result both sides must reach before any mutation.</param>
    /// <param name="mutatedDynamicValue">The value assigned to the dynamic variable on the far side.</param>
    /// <param name="expectedAfterDynamic">The result that must follow.</param>
    /// <param name="mutatedStaticValue">The value assigned to the static variable on the far side.</param>
    /// <remarks>
    /// <para>
    /// THIS IS THE ASSERTION THE AAP'S PHRASE "DOES NOT SURVIVE NAIVE SERIALIZATION" IS ABOUT, and it is
    /// the only one in this file that actually crosses a boundary. The naive payload is the STORED text,
    /// and this test shows exactly what it loses: after the rewrite the stored text reads <c>(10) + $$dyn</c>,
    /// in which <c>(10)</c> is indistinguishable from a literal that was always there. A far side handed
    /// only that cannot tell a static binding from a constant, and so cannot report the binding, cannot
    /// re-derive it against a different environment, and cannot recognise that a variable it holds is the
    /// one that was baked in.
    /// </para>
    /// <para>
    /// THE REBUILD USES NOTHING BUT THE PAYLOAD. The far-side engine is constructed over its own host with
    /// no knowledge of the near side: its variables come from <c>live_environment</c> and its expression
    /// from <c>unexpanded_exp</c>. It then reaches the same baseline, which is what "sufficient" means.
    /// </para>
    /// <para>
    /// AND THE TWO MUTATIONS ARE THE PROOF THAT THE DISTINCTION SURVIVED RATHER THAN THE NUMBER. On the far
    /// side the dynamic variable still propagates and the static one still cannot, which is only possible
    /// if the rebuild reconstructed the SAME two bindings rather than one uniform kind.
    /// </para>
    /// <para>
    /// THE SERIALIZATION IS REAL AND NOT A CLONE. <c>ToByteArray</c> followed by <c>Parser.ParseFrom</c>
    /// goes through the actual protobuf encoder and decoder, so a field the contract does not declare
    /// could not survive it - which is the point of doing it rather than copying the message.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(RoundTrippedBindingCases))]
    public async Task ThreePartPayload_RoundTripsAndTheFarSideStillTracksTheDynamicVariable(
        string staticName,
        long staticValue,
        string dynamicName,
        long dynamicValue,
        decimal expectedBaseline,
        long mutatedDynamicValue,
        decimal expectedAfterDynamic,
        long mutatedStaticValue)
    {
        // ---- THE NEAR SIDE -------------------------------------------------------------------------
        ColumnExpressionEngine near = NewEngine(out FakeDataWindowHost nearHost);

        Assert.Equal(RetCode.OK, near.AddVar(staticName, staticValue));
        Assert.Equal(RetCode.OK, near.AddVar(dynamicName, dynamicValue));

        string source = Ref(StaticSigil, staticName) + " + " + Ref(DynamicSigil, dynamicName);
        Assert.True(near.AddExp(BoundColumn, source) > 0);

        Assert.Equal(RetCode.OK, await near.CalcAsync(1L, Ct));
        Assert.Equal(expectedBaseline, nearHost.GetItemDecimal(1L, BoundColumn));

        ExpressionBindingSnapshot? binding = near.GetBinding(1);
        Assert.NotNull(binding);

        WireExpressionBinding projected = ComposeWireBinding(binding, near.Variables);

        // PART (i) - the unexpanded source, which the stored text is NOT.
        Assert.Equal(source, projected.UnexpandedExp);
        Assert.NotEqual(near.GetExp(BoundColumn), projected.UnexpandedExp);
        Assert.Contains(staticName, projected.UnexpandedExp, StringComparison.Ordinal);
        Assert.DoesNotContain(staticName, near.GetExp(BoundColumn), StringComparison.Ordinal);

        // PART (ii) - the bind-time snapshot, for BOTH references.
        Assert.Equal(
            staticValue.ToString(CultureInfo.InvariantCulture),
            projected.BindTimeSnapshot[staticName].StringValue);
        Assert.Equal(
            dynamicValue.ToString(CultureInfo.InvariantCulture),
            projected.BindTimeSnapshot[dynamicName].StringValue);

        // PART (iii) - the live environment, which is what a dynamic reference resolves against.
        Assert.Equal(
            staticValue.ToString(CultureInfo.InvariantCulture),
            projected.LiveEnvironment[staticName].StringValue);
        Assert.Equal(
            dynamicValue.ToString(CultureInfo.InvariantCulture),
            projected.LiveEnvironment[dynamicName].StringValue);

        // All three at once, which is the requirement: any one alone is lossy.
        Assert.NotEmpty(projected.UnexpandedExp);
        Assert.Equal(2, projected.BindTimeSnapshot.Count);
        Assert.Equal(2, projected.LiveEnvironment.Count);

        // The mode still rides on the surviving reference.
        WireVarData wireReference = Assert.Single(projected.Vars);
        Assert.Equal(dynamicName, wireReference.Name);
        Assert.Equal(ExpansionMode.Dynamic, wireReference.ExpansionMode);

        // ---- THE WIRE ------------------------------------------------------------------------------
        WireExpressionBinding parsed = WireExpressionBinding.Parser.ParseFrom(projected.ToByteArray());

        Assert.Equal(projected, parsed);
        Assert.Equal(source, parsed.UnexpandedExp);
        Assert.Equal(2, parsed.BindTimeSnapshot.Count);
        Assert.Equal(2, parsed.LiveEnvironment.Count);
        Assert.Equal(ExpansionMode.Dynamic, Assert.Single(parsed.Vars).ExpansionMode);

        // ---- THE FAR SIDE, BUILT FROM THE PAYLOAD ALONE ---------------------------------------------
        ColumnExpressionEngine far = NewEngine(out FakeDataWindowHost farHost);

        foreach (KeyValuePair<string, WireVarValue> live in
            parsed.LiveEnvironment.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            Assert.Equal(RetCode.OK, far.AddVarExp(live.Key, live.Value.StringValue));
        }

        Assert.True(far.AddExp(BoundColumn, parsed.UnexpandedExp) > 0);

        // The same baseline, reached from the payload and nothing else.
        Assert.Equal(RetCode.OK, await far.CalcAsync(1L, Ct));
        Assert.Equal(expectedBaseline, farHost.GetItemDecimal(1L, BoundColumn));

        // The far side rebuilt the SAME two bindings: the static one is gone from its stored text and
        // exactly one dynamic reference survives, just as on the near side.
        Assert.Equal(near.GetExp(BoundColumn), far.GetExp(BoundColumn));
        VariableReference farReference = Assert.Single(far.Expressions[0].Vars.Items);
        Assert.Equal(dynamicName, farReference.Name);
        Assert.Equal(
            ExpansionMode.Dynamic,
            ColumnExpressionEngine.SigilsFor(farReference).ProjectVariableReference().Mode);

        // Mutating the DYNAMIC variable on the far side propagates.
        Assert.Equal(RetCode.OK, await far.SetVarAsync(dynamicName, mutatedDynamicValue, false, Ct));
        Assert.Equal(RetCode.OK, await far.CalcAsync(1L, Ct));
        Assert.Equal(expectedAfterDynamic, farHost.GetItemDecimal(1L, BoundColumn));
        Assert.NotEqual(expectedBaseline, expectedAfterDynamic);

        // Mutating the STATIC one does not - which is the distinction, alive on the far side.
        Assert.Equal(RetCode.OK, await far.SetVarAsync(staticName, mutatedStaticValue, true, Ct));
        Assert.Equal(RetCode.OK, await far.CalcAsync(1L, Ct));
        Assert.Equal(expectedAfterDynamic, farHost.GetItemDecimal(1L, BoundColumn));

        // Nothing was reported on either side.
        Assert.Null(near.LastError);
        Assert.Null(far.LastError);
    }

    /// <summary>
    /// Composes contract C-04's <c>ExpressionBinding</c> from an engine's bind-time binding and its LIVE
    /// variable table - the three-part payload of AAP section 0.6.2.2.
    /// </summary>
    /// <param name="snapshot">The engine's binding record for one expression.</param>
    /// <param name="environment">The engine's live global variable table.</param>
    /// <returns>The wire message.</returns>
    /// <remarks>
    /// <para>
    /// COMPOSED HERE ON PURPOSE, AND THE REASON IS WHAT IS BEING ASSERTED. Section 3's claim is about THE
    /// CONTRACT - that <c>ExpressionBinding</c> has somewhere to put all three parts, that they survive a
    /// real protobuf round trip, and that they suffice to rebuild the expression on a far side that holds
    /// nothing else. Reaching for the gRPC service's own projection helper would turn that into "the
    /// projection agrees with itself", and the helper's correctness is already
    /// ColumnExpressionServiceContractTests.cs's subject; two authorities over the payload shape is exactly
    /// the duplication this file avoids elsewhere.
    /// </para>
    /// <para>
    /// THE VALUES TRAVEL AS <c>string_value</c> BECAUSE A VALUE EXPRESSION IS TEXT. That is not a
    /// flattening of the seven-type variable union: a static substitution writes RENDERED TEXT into the
    /// expression [:L1434-L1435], so a typed arm here would claim a type the substitution never had. The
    /// live side carries the same rendering because the engine stores a value variable as a literal in its
    /// own value expression.
    /// </para>
    /// <para>
    /// THE LIVE SIDE IS WALKED ONE-BASED, 1..UpperBound, so a dynamic reference's index lands on the entry
    /// it names. A FOREIGN entry is skipped: its value lives in another DataWindow's table and is reached
    /// through the session rather than copied, because copying it would be the stale reading that AAP
    /// section 0.6.2.3's narrowing exists to prevent.
    /// </para>
    /// </remarks>
    private static WireExpressionBinding ComposeWireBinding(
        ExpressionBindingSnapshot snapshot,
        ExpressionVariableEnvironment environment)
    {
        WireExpressionBinding composed = new() { UnexpandedExp = snapshot.UnexpandedExp };

        // PART (ii) - the bind-time snapshot, exactly as the engine recorded it.
        foreach (KeyValuePair<string, string> bound in snapshot.BindTimeSnapshot)
        {
            composed.BindTimeSnapshot[bound.Key] = new WireVarValue { StringValue = bound.Value };
        }

        // The retained references, each tagged with the mode its own sigils denote.
        foreach (VariableReference reference in snapshot.Vars)
        {
            composed.Vars.Add(
                new WireVarData
                {
                    Name = reference.Name,
                    FullName = reference.FullName,
                    Index = reference.Index,
                    IsCtx = reference.IsCtx,
                    IsMacro = reference.IsMacro,
                    ExpansionMode = ColumnExpressionEngine
                        .SigilsFor(reference)
                        .ProjectVariableReference()
                        .Mode,
                });
        }

        // PART (iii) - the live environment, one-based over the table.
        for (int index = 1; index <= environment.UpperBound; index++)
        {
            GlobalVariable variable = environment[index];

            if (variable.VarType != ExpressionVariableEnvironment.VAR_FOREIGN
                && variable.Name.Length > 0)
            {
                composed.LiveEnvironment[variable.Name] =
                    new WireVarValue { StringValue = variable.Local.Exp };
            }
        }

        return composed;
    }

    // =================================================================================================
    //  SECTION 4 - THE SEVEN-STRUCTURE MODEL'S OBSERVABLE FIELDS
    //             n_cst_dwsvc_columnexp.sru:L22-L83
    // =================================================================================================

    /// <summary>
    /// The census of <c>columnexpdata</c> [:L22-L42] - nineteen fields, each with the oracle line that
    /// declares it.
    /// </summary>
    /// <returns>Per row: the ported property name and the oracle line number that declares its field.</returns>
    /// <remarks>
    /// <para>
    /// THE COUNT IS AS MUCH THE ASSERTION AS THE NAMES, and the reason is the contract.
    /// <c>dataservices.v1.ColumnExpData</c> was generated from this same census, so a twentieth field
    /// added here without a corresponding contract change would make the wire representation lossy in a
    /// way that no round-trip test would notice - the projection simply would not carry it, and nothing
    /// would fail.
    /// </para>
    /// <para>
    /// THE UNEXPANDED SOURCE TEXT IS DELIBERATELY NOT THE TWENTIETH FIELD. The wire does carry it, but the
    /// oracle has no field for it because the oracle had no boundary to cross, so this port keeps it
    /// alongside the structure on <see cref="ExpressionBindingSnapshot"/> rather than smuggling it in.
    /// This row set is what would fail if someone did.
    /// </para>
    /// </remarks>
    public static TheoryData<string, int> ColumnExpressionDataFields() =>
        new()
        {
            { nameof(ColumnExpressionData.Name), 23 },
            { nameof(ColumnExpressionData.Id), 24 },
            { nameof(ColumnExpressionData.Dwo), 25 },
            { nameof(ColumnExpressionData.ColType), 26 },
            { nameof(ColumnExpressionData.Exp), 27 },
            { nameof(ColumnExpressionData.Vars), 28 },
            { nameof(ColumnExpressionData.Fns), 29 },
            { nameof(ColumnExpressionData.RelativeColIds), 30 },
            { nameof(ColumnExpressionData.RelativeInputColIds), 31 },
            { nameof(ColumnExpressionData.DupExps), 32 },
            { nameof(ColumnExpressionData.EmptyStringIsNull), 33 },
            { nameof(ColumnExpressionData.HasMacro), 34 },
            { nameof(ColumnExpressionData.AlwaysCalc), 35 },
            { nameof(ColumnExpressionData.TriggerEvent), 36 },
            { nameof(ColumnExpressionData.Recursive), 37 },
            { nameof(ColumnExpressionData.Dirty), 38 },
            { nameof(ColumnExpressionData.Empty), 39 },
            { nameof(ColumnExpressionData.Cacheable), 40 },
            { nameof(ColumnExpressionData.ComputeName), 41 },
        };

    /// <summary>
    /// Every field of <c>columnexpdata</c> is published, and there are exactly nineteen of them.
    /// </summary>
    /// <param name="propertyName">The ported property name.</param>
    /// <param name="oracleLine">The oracle line that declares the field, for the failure message.</param>
    [Theory]
    [MemberData(nameof(ColumnExpressionDataFields))]
    public void ColumnExpressionData_PublishesEveryOracleField(string propertyName, int oracleLine)
    {
        Assert.InRange(oracleLine, 23, 41);

        PropertyInfo? property = typeof(ColumnExpressionData).GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(property);
        Assert.True(property.CanRead);

        // Nineteen and no more [:L22-L42].
        Assert.Equal(
            19,
            typeof(ColumnExpressionData)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Length);
    }

    /// <summary>
    /// The census of <c>columndata</c> [:L44-L48] - three fields, which together ARE the reverse
    /// dependency graph.
    /// </summary>
    /// <returns>Per row: the ported property name and the oracle line that declares its field.</returns>
    public static TheoryData<string, int> ColumnCalcDataFields() =>
        new()
        {
            { nameof(ColumnCalcData.Flag), 45 },
            { nameof(ColumnCalcData.Indexes), 46 },
            { nameof(ColumnCalcData.VarIndexes), 47 },
        };

    /// <summary>
    /// <c>columndata</c> publishes exactly its three fields: the tri-state flag, the dependent expression
    /// indexes and the dependent variable indexes.
    /// </summary>
    /// <param name="propertyName">The ported property name.</param>
    /// <param name="oracleLine">The oracle line that declares the field.</param>
    [Theory]
    [MemberData(nameof(ColumnCalcDataFields))]
    public void ColumnCalcData_PublishesTheReverseDependencyIndex(string propertyName, int oracleLine)
    {
        Assert.InRange(oracleLine, 45, 47);

        PropertyInfo? property = typeof(ColumnCalcData).GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(property);
        Assert.Equal(
            3,
            typeof(ColumnCalcData).GetProperties(BindingFlags.Public | BindingFlags.Instance).Length);
    }

    /// <summary>
    /// One column, TWO expressions, and the column change that selects between them.
    /// </summary>
    /// <returns>
    /// Per row: the column whose change is reported; the expression index that must run; and the value the
    /// bound column must end up with.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THIS IS CAPABILITY 4 OF THE LEGACY FEATURE LIST: "一个列可以绑定多个表达式（指定由什么列修改了数据
    /// 则触发对应的表达式计算）" - one column may bind several expressions, and WHICH COLUMN WAS MODIFIED
    /// selects the one that runs [docs/n_cst_dwsvc_columnexp.md:L8].
    /// </para>
    /// <para>
    /// <c>dupexps[]</c> [:L32] IS THE MECHANISM AND IT CARRIES RIVALS, NOT MEMBERS. Both the build at
    /// [:L487] and the rebuild at [:L1572] <c>continue</c> on the self-comparison, so a group of two has
    /// one entry each and a lone expression has an EMPTY group rather than a group of one - the list exists
    /// only so that a member which produced a value can clear the others' dirty flags [:L2023, :L2059].
    /// </para>
    /// <para>
    /// THE VALUES ARE DELIBERATELY FAR APART - ten times against a hundred times - so a row cannot pass by
    /// arithmetic coincidence: 30 can only come from the first expression and 700 only from the second.
    /// </para>
    /// </remarks>
    public static TheoryData<string, long, decimal> DuplicateExpressionSelection() =>
        new()
        {
            // n2 changed, so the expression whose relative column is n2 runs: 3 * 10.
            { "n2", 1L, 30m },

            // n3 changed, so its rival runs instead: 7 * 100. Same column, same row.
            { "n3", 2L, 700m },
        };

    /// <summary>
    /// Two expressions bound to ONE column are selected by WHICH COLUMN CHANGED, and each knows its
    /// rivals through <c>dupexps[]</c>.
    /// </summary>
    /// <param name="changedColumn">The column whose change is reported.</param>
    /// <param name="expectedIndex">The expression index that must run.</param>
    /// <param name="expectedValue">The value the bound column must receive.</param>
    [Theory]
    [MemberData(nameof(DuplicateExpressionSelection))]
    public async Task DupExps_SelectTheExpressionByWhichColumnChanged(
        string changedColumn,
        long expectedIndex,
        decimal expectedValue)
    {
        ColumnExpressionEngine engine = NewEngine(out FakeDataWindowHost host);

        int first = engine.AddExp(BoundColumn, "n2 * 10");
        int second = engine.AddExp(BoundColumn, "n3 * 100");

        // ADD APPENDS AND NEVER REPLACES, which is what makes several expressions per column possible.
        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.Equal(2, engine.ExpressionCount);

        Assert.Equal(RetCode.OK, engine.SetRelativeColumn(first, "n2"));
        Assert.Equal(RetCode.OK, engine.SetRelativeColumn(second, "n3"));

        // Each member carries its RIVALS, so for a group of two that is exactly the other one.
        Assert.Equal([2L], engine.Expressions[0].DupExps.Items);
        Assert.Equal([1L], engine.Expressions[1].DupExps.Items);

        Poke(host, 1L, "n2", 3m);
        Poke(host, 1L, "n3", 7m);

        await engine.OnDoItemChangedAsync(1L, changedColumn, host.Column(changedColumn).Id, false, Ct);

        Assert.Equal(expectedValue, host.GetItemDecimal(1L, BoundColumn));

        // The reverse index names the expression that ran, and only that one.
        ImmutableArray<ColumnCalcData> states = engine.ColumnCalcStates;
        int slot = (int)host.Column(changedColumn).Id - 1;
        Assert.Equal(ColumnCalcFlag.CLC_YES, states[slot].Flag);
        Assert.Equal([(int)expectedIndex], states[slot].Indexes.Items);
    }

    /// <summary>
    /// A lone expression has NO duplicate group, because the group only exists to coordinate rivals.
    /// </summary>
    /// <returns>One row per column that could carry an expression.</returns>
    public static TheoryData<string> SingleExpressionColumns() =>
        new() { "n1", "n2", "s1" };

    /// <summary>
    /// A column carrying ONE expression has an EMPTY duplicate group, not a group of one [:L487-L493 and
    /// :L1567 both require a count above one before building the group at all].
    /// </summary>
    /// <param name="column">The column to bind.</param>
    [Theory]
    [MemberData(nameof(SingleExpressionColumns))]
    public void SingleExpression_HasNoDuplicateGroup(string column)
    {
        ColumnExpressionEngine engine = NewEngine(out _);

        Assert.True(engine.AddExp(column, "n3 * 10") > 0);
        Assert.Equal(0, engine.Expressions[0].DupExps.UpperBound);
        Assert.Empty(engine.Expressions[0].DupExps.Items);
    }

    /// <summary>
    /// The reverse dependency index after ONE column change, per column - including the column that
    /// depends on nothing.
    /// </summary>
    /// <returns>
    /// Per row: the column whose change is reported; the tri-state flag its slot must reach; the ORDERED
    /// expression indexes its slot must hold; and the column the CASCADE then resolves, or the empty
    /// string when no expression ran.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE THIRD ROW IS THE ONE THAT MATTERS MOST. A column with no dependent must resolve to
    /// <c>CLC_NO</c> and hold NO indexes - "marks exactly the dependents and no others" is only an
    /// assertion if a non-dependent is in the table. Without it, an implementation that marked everything
    /// dirty would pass rows 1 and 2 perfectly.
    /// </para>
    /// <para>
    /// THE FOURTH COLUMN IS THE CASCADE, AND IT IS REAL BEHAVIOUR RATHER THAN NOISE TO BE TOLERATED. When
    /// an expression writes its value, the oracle immediately reports a change to the column it just wrote
    /// [:L860-L862] so that anything depending on THAT column recalculates in turn - with
    /// <c>frominput</c> FALSE, because a calculation is not user input. So exactly TWO slots are resolved
    /// by a change that ran an expression: the column that changed, and the column the expression wrote.
    /// The second resolves to <c>CLC_NO</c> here because nothing depends on it, which is what makes the
    /// cascade observable at all.
    /// </para>
    /// <para>
    /// THE SLOT IS THE COLUMN ID AND THE INDEXING IS ONE-BASED [:L218-L219, :L234-L235 grow the array by
    /// writing PAST its end at <c>colID</c>]. AAP section 0.8.6 R9 names this the most dangerous
    /// mechanical hazard in the refactor: a zero-based table would shift every dependent by one and
    /// recalculate the WRONG expression, which is indistinguishable from a behavioural regression.
    /// </para>
    /// </remarks>
    public static TheoryData<string, ColumnCalcFlag, int[], string> ReverseDependencyIndexCases() =>
        new()
        {
            // n2 drives the first expression, which writes n1 - so n1 is resolved by the cascade.
            { "n2", ColumnCalcFlag.CLC_YES, [1], "n1" },

            // n3 drives the second, which writes s1.
            { "n3", ColumnCalcFlag.CLC_YES, [2], "s1" },

            // s2 drives nothing at all, is cached as such, and nothing cascades.
            { "s2", ColumnCalcFlag.CLC_NO, [], "" },
        };

    /// <summary>
    /// A column change marks EXACTLY the expressions that depend on that column, through the one-based
    /// reverse index on <c>columndata</c> - a column with no dependents is cached as having none, and the
    /// only other slot the change touches is the one the calculation wrote.
    /// </summary>
    /// <param name="changedColumn">The column whose change is reported.</param>
    /// <param name="expectedFlag">The tri-state flag the column's slot must reach.</param>
    /// <param name="expectedIndexes">The ordered expression indexes the slot must hold.</param>
    /// <param name="cascadedColumn">The column the cascade resolves, or the empty string.</param>
    /// <remarks>
    /// THE INDEX IS BUILT LAZILY AND EXACTLY ONCE PER COLUMN. Every slot starts
    /// <see cref="ColumnCalcFlag.CLC_UNKNOWN"/> [:L113], is promoted on the first change to that column
    /// [:L238-L293], and is then reused - which is why a stale <c>CLC_NO</c> would suppress a dependency
    /// for good and why adding an expression has to invalidate the whole table [:L1560].
    /// </remarks>
    [Theory]
    [MemberData(nameof(ReverseDependencyIndexCases))]
    public async Task ReverseDependencyIndex_MarksExactlyTheDependentExpressions(
        string changedColumn,
        ColumnCalcFlag expectedFlag,
        int[] expectedIndexes,
        string cascadedColumn)
    {
        ColumnExpressionEngine engine = NewEngine(out FakeDataWindowHost host);

        Assert.True(engine.AddExp(BoundColumn, "n2 * 2") > 0);
        Assert.Equal(RetCode.OK, engine.SetRelativeColumn(BoundColumn, "n2"));

        Assert.True(engine.AddExp("s1", "string(n3)") > 0);
        Assert.Equal(RetCode.OK, engine.SetRelativeColumn("s1", "n3"));

        // NOTHING is resolved until a column change asks the question.
        Assert.All(
            engine.ColumnCalcStates,
            static state => Assert.Equal(ColumnCalcFlag.CLC_UNKNOWN, state.Flag));

        Poke(host, 1L, "n2", 4m);
        Poke(host, 1L, "n3", 12m);

        long changedId = host.Column(changedColumn).Id;
        await engine.OnDoItemChangedAsync(1L, changedColumn, changedId, false, Ct);

        ImmutableArray<ColumnCalcData> states = engine.ColumnCalcStates;
        int slot = (int)changedId - 1;

        Assert.Equal(expectedFlag, states[slot].Flag);
        Assert.Equal<IEnumerable<int>>(expectedIndexes, states[slot].Indexes.Items);
        Assert.Equal(expectedIndexes.Length, states[slot].Indexes.UpperBound);

        // No variable depends on any column here, so the variable half of the index stays empty.
        Assert.Equal(0, states[slot].VarIndexes.UpperBound);

        // THE CASCADE [:L860-L862]: the written column is resolved too, and it depends on nothing.
        int cascadedSlot = -1;
        if (cascadedColumn.Length > 0)
        {
            cascadedSlot = (int)host.Column(cascadedColumn).Id - 1;
            Assert.NotEqual(slot, cascadedSlot);
            Assert.Equal(ColumnCalcFlag.CLC_NO, states[cascadedSlot].Flag);
            Assert.Equal(0, states[cascadedSlot].Indexes.UpperBound);
        }

        // And every OTHER slot is still unresolved, because the build is per column and lazy.
        for (int position = 0; position < states.Length; position++)
        {
            if (position != slot && position != cascadedSlot)
            {
                Assert.Equal(ColumnCalcFlag.CLC_UNKNOWN, states[position].Flag);
            }
        }
    }

    /// <summary>
    /// The two relative-column kinds against the two provenances of a change - the complete 2x2.
    /// </summary>
    /// <returns>
    /// Per row: whether the INPUT variant of the setter is used; whether the change is reported as user
    /// input; and the value the bound column ends up with.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE SPECIFICATION STATES THE DIFFERENCE IN TWO ADJACENT LINES, and it is the entire content of this
    /// matrix: <c>of_SetRelativeColumn("n2", {"n1"})</c> is annotated "n1的值变化的时候触发表达式计算" -
    /// fire when n1's VALUE CHANGES [docs/n_cst_dwsvc_columnexp.md:L162] - while
    /// <c>of_SetRelativeInputColumn("n2", {"n1"})</c> is annotated "只有当用户输入n1的时候触发表达式计算" -
    /// fire ONLY WHEN THE USER TYPES n1 [:L163].
    /// </para>
    /// <para>
    /// THE DISCRIMINATOR IS THE <c>frominput</c> ARGUMENT, and only one of the four cells is negative. The
    /// raw item-change entry point passes it TRUE [:L224], because that event is a user edit by
    /// construction; the calculation cascade passes it FALSE [:L860-L862], because a calculated write is
    /// not user input. So the single row that must NOT fire is exactly the one that keeps a calculated
    /// change from re-triggering an input-gated expression - which is what stops two expressions that feed
    /// each other from looping.
    /// </para>
    /// <para>
    /// ALL FOUR CELLS ARE PRESENT ON PURPOSE. Three of them fire, so a test that only asserted the
    /// negative case would not notice an implementation that had stopped firing altogether, and a test
    /// that only asserted the positives would not notice one that ignored <c>frominput</c> entirely.
    /// </para>
    /// </remarks>
    public static TheoryData<bool, bool, decimal> RelativeTriggerCases() =>
        new()
        {
            // of_SetRelativeColumn - fires on ANY change to the relative column.
            { false, false, 12m },
            { false, true, 12m },

            // of_SetRelativeInputColumn - a programmatic change must NOT fire it.
            { true, false, 0m },

            // ...and the same change reported as user input must.
            { true, true, 12m },
        };

    /// <summary>
    /// <c>of_SetRelativeColumn</c> fires on any change to its column; <c>of_SetRelativeInputColumn</c>
    /// fires ONLY when the change came from the user.
    /// </summary>
    /// <param name="useInputVariant">Whether the INPUT variant of the setter is used.</param>
    /// <param name="fromInput">Whether the change is reported as user input.</param>
    /// <param name="expectedValue">The value the bound column ends up with.</param>
    [Theory]
    [MemberData(nameof(RelativeTriggerCases))]
    public async Task RelativeColumn_FiresOnAnyChange_RelativeInputColumn_OnlyOnTypedInput(
        bool useInputVariant,
        bool fromInput,
        decimal expectedValue)
    {
        ColumnExpressionEngine engine = NewEngine(out FakeDataWindowHost host);

        Assert.True(engine.AddExp(BoundColumn, "n2 * 3") > 0);

        if (useInputVariant)
        {
            Assert.Equal(RetCode.OK, engine.SetRelativeInputColumn(BoundColumn, "n2"));
            Assert.Equal(1, engine.Expressions[0].RelativeInputColIds.UpperBound);
            Assert.Equal(0, engine.Expressions[0].RelativeColIds.UpperBound);
        }
        else
        {
            Assert.Equal(RetCode.OK, engine.SetRelativeColumn(BoundColumn, "n2"));
            Assert.Equal(1, engine.Expressions[0].RelativeColIds.UpperBound);
            Assert.Equal(0, engine.Expressions[0].RelativeInputColIds.UpperBound);
        }

        long n2Id = host.Column("n2").Id;
        Poke(host, 1L, "n2", 4m);

        await engine.OnDoItemChangedAsync(1L, "n2", n2Id, fromInput, Ct);

        Assert.Equal(expectedValue, host.GetItemDecimal(1L, BoundColumn));
    }

    /// <summary>
    /// The four published spellings of the relative-column setter, across both kinds.
    /// </summary>
    /// <returns>Per row: the spelling to exercise, and whether the INPUT variant is used.</returns>
    /// <remarks>
    /// THE ORACLE PUBLISHES ALL EIGHT ENTRY POINTS - plural and singular, by index and by name, for each
    /// of the two kinds [:L128-L140 in the forward prototypes] - and the singular forms are thin wrappers
    /// that build a one-element array and delegate [:L403-L417]. Eight rows rather than one because a
    /// wrapper that delegated to the WRONG kind, or that appended where it should replace, would still
    /// answer <see cref="RetCode.OK"/>.
    /// </remarks>
    public static TheoryData<string, bool> RelativeSetterSpellings() =>
        new()
        {
            { "PluralByIndex", false },
            { "PluralByName", false },
            { "SingularByIndex", false },
            { "SingularByName", false },
            { "PluralByIndex", true },
            { "PluralByName", true },
            { "SingularByIndex", true },
            { "SingularByName", true },
        };

    /// <summary>
    /// All four spellings of each relative-column setter reach the SAME state, and each writes only its
    /// own kind's list.
    /// </summary>
    /// <param name="spelling">Which of the four entry points to call.</param>
    /// <param name="useInputVariant">Whether the INPUT variant is used.</param>
    /// <remarks>
    /// THE SINGULAR FORM REPLACES AND DOES NOT APPEND, which is asserted by calling it after the plural
    /// form has already installed TWO columns: the surviving list must hold one entry, not three. That is
    /// the oracle's own shape - the singular wrapper builds a one-element array and hands it to the plural
    /// setter [:L403-L409], so replacement is inherited rather than implemented twice.
    /// </remarks>
    [Theory]
    [MemberData(nameof(RelativeSetterSpellings))]
    public void RelativeSetters_AllFourSpellingsAgree(string spelling, bool useInputVariant)
    {
        ColumnExpressionEngine engine = NewEngine(out FakeDataWindowHost host);
        Assert.True(engine.AddExp(BoundColumn, "n2 + n3") > 0);

        long n2Id = host.Column("n2").Id;
        long n3Id = host.Column("n3").Id;

        // Install TWO columns first, so a singular call that appended rather than replaced would show.
        Assert.Equal(
            RetCode.OK,
            useInputVariant
                ? engine.SetRelativeInputColumns(BoundColumn, ["n2", "n3"])
                : engine.SetRelativeColumns(BoundColumn, ["n2", "n3"]));

        OneBasedList<long> installed = useInputVariant
            ? engine.Expressions[0].RelativeInputColIds
            : engine.Expressions[0].RelativeColIds;
        Assert.Equal([n2Id, n3Id], installed.Items);

        long answer = (spelling, useInputVariant) switch
        {
            ("PluralByIndex", false) => engine.SetRelativeColumns(1, ["n2"]),
            ("PluralByName", false) => engine.SetRelativeColumns(BoundColumn, ["n2"]),
            ("SingularByIndex", false) => engine.SetRelativeColumn(1, "n2"),
            ("SingularByName", false) => engine.SetRelativeColumn(BoundColumn, "n2"),
            ("PluralByIndex", true) => engine.SetRelativeInputColumns(1, ["n2"]),
            ("PluralByName", true) => engine.SetRelativeInputColumns(BoundColumn, ["n2"]),
            ("SingularByIndex", true) => engine.SetRelativeInputColumn(1, "n2"),
            ("SingularByName", true) => engine.SetRelativeInputColumn(BoundColumn, "n2"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(spelling),
                spelling,
                "Every row of RelativeSetterSpellings must name one of the four published entry points."),
        };

        Assert.Equal(RetCode.OK, answer);

        // ONE entry, replacing the two - and the OTHER kind's list is untouched and empty.
        Assert.Equal(
            [n2Id],
            useInputVariant
                ? engine.Expressions[0].RelativeInputColIds.Items
                : engine.Expressions[0].RelativeColIds.Items);
        Assert.Equal(
            0,
            useInputVariant
                ? engine.Expressions[0].RelativeColIds.UpperBound
                : engine.Expressions[0].RelativeInputColIds.UpperBound);
    }

    // =================================================================================================
    //  SECTION 5 - THE TRI-STATE CACHE, THE SENTINELS, AND CALCULATION ORDERING
    // =================================================================================================

    /// <summary>
    /// The tri-state calculation cache, with the numeral the oracle assigns each state.
    /// </summary>
    /// <returns>Per row: the state, its numeral, and the oracle line that declares it.</returns>
    /// <remarks>
    /// <para>
    /// TRI-STATE AND NOT BOOLEAN, AND THE THIRD STATE IS NOT "NO". <c>CLC_UNKNOWN</c> means NOT YET
    /// DETERMINED, and the entry point at [:L221] SKIPS a column entirely on <c>CLC_NO</c> while
    /// EVALUATING the reverse index on <c>CLC_UNKNOWN</c> - so collapsing the two would change which
    /// columns are walked, and a column that arrived as UNKNOWN and was read as NO would never be
    /// evaluated at all.
    /// </para>
    /// <para>
    /// THE NUMERALS ARE ASSERTED BECAUSE THEY TRAVEL AND BECAUSE THEY COLLIDE. <c>1</c> and <c>2</c> are
    /// also values in four other alphabets in this system - the return-code algebra, the item-change
    /// alphabet, the tri-valued broker veto and the two variable kinds - and <c>0</c> reads as SUCCESS in
    /// the return-code algebra while here it means UNDETERMINED. The member spellings are the oracle's on
    /// purpose (AAP section 0.4.5.3) because they appear in serialized payloads and characterization
    /// recordings, and this file references them without declaring any of its own.
    /// </para>
    /// </remarks>
    public static TheoryData<ColumnCalcFlag, long, int> ColumnCalcFlagValues() =>
        new()
        {
            { ColumnCalcFlag.CLC_UNKNOWN, 0L, 113 },
            { ColumnCalcFlag.CLC_YES, 1L, 114 },
            { ColumnCalcFlag.CLC_NO, 2L, 115 },
        };

    /// <summary>
    /// The tri-state cache keeps the oracle's three numerals and its three spellings, and projects onto
    /// the contract's own three.
    /// </summary>
    /// <param name="flag">The state.</param>
    /// <param name="expectedNumeral">Its numeral.</param>
    /// <param name="oracleLine">The oracle line that declares it.</param>
    [Theory]
    [MemberData(nameof(ColumnCalcFlagValues))]
    public void ColumnCalcFlag_IsTriStateWithTheOraclesNumerals(
        ColumnCalcFlag flag,
        long expectedNumeral,
        int oracleLine)
    {
        Assert.InRange(oracleLine, 113, 115);
        Assert.Equal(expectedNumeral, (long)flag);
        Assert.Equal(3, Enum.GetValues<ColumnCalcFlag>().Length);

        // THE CONTRACT KEEPS THE SAME THREE STATES WITH THE SAME NUMERALS rather than folding UNKNOWN
        // into NO. `dataservices.v1.ColumnData.CalcFlag` is asserted by NUMERAL and not by member name,
        // because the numeral is what a varint on the wire carries and what a stored recording holds.
        WireColumnData.Types.CalcFlag wireFlag = (WireColumnData.Types.CalcFlag)(int)expectedNumeral;
        Assert.Contains(wireFlag, Enum.GetValues<WireColumnData.Types.CalcFlag>());
        Assert.Equal(expectedNumeral, (long)(int)wireFlag);
        Assert.Equal(
            Enum.GetValues<ColumnCalcFlag>().Length,
            Enum.GetValues<WireColumnData.Types.CalcFlag>().Length);

        // And a default-constructed record starts UNDETERMINED, which is what makes absence on the wire
        // read as CLC_UNKNOWN rather than as CLC_NO.
        Assert.Equal(ColumnCalcFlag.CLC_UNKNOWN, new ColumnCalcData().Flag);
    }

    /// <summary>
    /// The columns whose calculation state is resolved on first use, and the state each reaches.
    /// </summary>
    /// <returns>Per row: the column to report a change for, and the state its slot must reach.</returns>
    public static TheoryData<string, ColumnCalcFlag> FirstUseResolutionCases() =>
        new()
        {
            { "n2", ColumnCalcFlag.CLC_YES },
            { "s2", ColumnCalcFlag.CLC_NO },
        };

    /// <summary>
    /// A freshly bound expression leaves every column UNDETERMINED; the first change to a column resolves
    /// that column's slot; and the resolution STICKS, because the build is the expensive half.
    /// </summary>
    /// <param name="changedColumn">The column to report a change for.</param>
    /// <param name="expectedFlag">The state its slot must reach and keep.</param>
    /// <remarks>
    /// STICKINESS IS THE WHOLE POINT OF THE CACHE AND IS ASSERTED BY REPEATING THE CHANGE. The build walks
    /// every expression and every variable [:L240-L292]; the walk happens once per column and the answer is
    /// reused [:L238 guards it on UNKNOWN]. A second identical change must therefore find the slot already
    /// resolved and reach the same conclusion - which is also why <c>of_AddExp</c> has to invalidate the
    /// whole table [:L1560], since a stale <c>CLC_NO</c> would suppress a new dependency for good.
    /// </remarks>
    [Theory]
    [MemberData(nameof(FirstUseResolutionCases))]
    public async Task ColumnCalcState_StartsUndeterminedThenResolvesAndSticks(
        string changedColumn,
        ColumnCalcFlag expectedFlag)
    {
        ColumnExpressionEngine engine = NewEngine(out FakeDataWindowHost host);

        Assert.True(engine.AddExp(BoundColumn, "n2 * 2") > 0);
        Assert.Equal(RetCode.OK, engine.SetRelativeColumn(BoundColumn, "n2"));

        // Freshly bound: every slot UNDETERMINED, nothing walked yet.
        Assert.All(
            engine.ColumnCalcStates,
            static state =>
            {
                Assert.Equal(ColumnCalcFlag.CLC_UNKNOWN, state.Flag);
                Assert.Equal(0, state.Indexes.UpperBound);
                Assert.Equal(0, state.VarIndexes.UpperBound);
            });

        long changedId = host.Column(changedColumn).Id;
        int slot = (int)changedId - 1;

        Poke(host, 1L, "n2", 3m);
        await engine.OnDoItemChangedAsync(1L, changedColumn, changedId, false, Ct);
        Assert.Equal(expectedFlag, engine.ColumnCalcStates[slot].Flag);

        // The same change again: already resolved, same answer, same index contents.
        ImmutableArray<int> resolvedIndexes = engine.ColumnCalcStates[slot].Indexes.Items;
        await engine.OnDoItemChangedAsync(1L, changedColumn, changedId, false, Ct);
        Assert.Equal(expectedFlag, engine.ColumnCalcStates[slot].Flag);
        Assert.Equal<IEnumerable<int>>(resolvedIndexes, engine.ColumnCalcStates[slot].Indexes.Items);

        // Binding a NEW expression invalidates the whole table, because the stale answer may now be wrong.
        Assert.True(engine.AddExp("s1", "string(n3)") > 0);
        Assert.All(
            engine.ColumnCalcStates,
            static state => Assert.Equal(ColumnCalcFlag.CLC_UNKNOWN, state.Flag));
    }

    /// <summary>
    /// The columns to enable the compute cache on, so the generated name is asserted for more than one.
    /// </summary>
    /// <returns>Per row: the column, and the discriminator the generated name must carry.</returns>
    public static TheoryData<string, int> ComputeCacheColumns() =>
        new()
        {
            { "n1", 1 },
            { "n2", 1 },
            { "s1", 1 },
        };

    /// <summary>
    /// The compute cache's generated object name is composed from <c>DWOSUFFIX</c> - carrying the DOUBLE
    /// underscore that composition produces - and switching the cache off clears it so the resolution does
    /// not stick.
    /// </summary>
    /// <param name="column">The column to enable the cache on.</param>
    /// <param name="expectedDiscriminator">The discriminator the generated name must carry.</param>
    /// <remarks>
    /// <para>
    /// THE NAME IS <c>name + "_" + id + "_" + DWOSUFFIX</c> [:L1886] AND <c>DWOSUFFIX</c> ALREADY BEGINS
    /// WITH AN UNDERSCORE [:L95], so the composed name carries a DOUBLE one. That is a preserved defect
    /// (constraint C-B, and it is observable on
    /// <see cref="ColumnExpressionData.ComputeName"/> and therefore on C-04) rather than a typo to tidy,
    /// and tidying it would change every characterization recording that names a compute object.
    /// </para>
    /// <para>
    /// A FAILED CREATION STILL ANSWERS SUCCESS AND STILL SETS THE FLAG. [:L1887-L1889] reports the failure
    /// and then FALLS THROUGH to [:L1895], so an expression is marked cacheable with no compute object
    /// behind it and [:L1897] answers OK. That is not left broken by accident - the calculation path
    /// notices the missing object and creates it on the next pass [:L706-L712] - and reproducing it is what
    /// keeps that recovery path reachable. No syntax mutator is supplied here, so this test takes exactly
    /// that path and asserts BOTH the reported error and the successful return code.
    /// </para>
    /// <para>
    /// SWITCHING THE CACHE OFF DESTROYS THE OBJECT AND CLEARS THE NAME [:L1891-L1893], so the resolution
    /// does not stick: a later re-enable mints a NEW name with the next discriminator, which never resets
    /// [:L1885], so no two generated objects can collide even across repeated cycles.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ComputeCacheColumns))]
    public void ComputeCache_ComposesItsNameFromDwoSuffixAndClearingItDoesNotStick(
        string column,
        int expectedDiscriminator)
    {
        ColumnExpressionEngine engine = NewEngine(out _);

        Assert.Equal("_columnexp", ColumnExpressionEngine.DWOSUFFIX);
        Assert.StartsWith("_", ColumnExpressionEngine.DWOSUFFIX, StringComparison.Ordinal);

        Assert.True(engine.AddExp(column, "n3 * 2") > 0);
        Assert.False(engine.Expressions[0].Cacheable);
        Assert.Equal(string.Empty, engine.Expressions[0].ComputeName);

        // A failed creation is REPORTED and still answers OK - the preserved fall-through.
        Assert.Equal(RetCode.OK, engine.SetCacheable(1, true));
        Assert.True(engine.Expressions[0].Cacheable);
        Assert.NotNull(engine.LastError);
        Assert.Equal(
            ExpressionErrorSite.CreateExpressionCacheFailedOnCacheEnable,
            engine.LastError.Site);

        string generated = engine.Expressions[0].ComputeName;
        Assert.Equal(
            column
                + "_"
                + expectedDiscriminator.ToString(CultureInfo.InvariantCulture)
                + "_"
                + ColumnExpressionEngine.DWOSUFFIX,
            generated);
        Assert.EndsWith(ColumnExpressionEngine.DWOSUFFIX, generated, StringComparison.Ordinal);
        Assert.Contains("__", generated, StringComparison.Ordinal);

        // Off again: the name is cleared, so the resolution does not stick.
        Assert.Equal(RetCode.OK, engine.SetCacheable(1, false));
        Assert.False(engine.Expressions[0].Cacheable);
        Assert.Equal(string.Empty, engine.Expressions[0].ComputeName);

        // And a re-enable mints a NEW name, because the discriminator never resets.
        Assert.Equal(RetCode.OK, engine.SetCacheable(1, true));
        Assert.NotEqual(generated, engine.Expressions[0].ComputeName);
        Assert.EndsWith(
            ColumnExpressionEngine.DWOSUFFIX,
            engine.Expressions[0].ComputeName,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The two built-in function sentinels, with their values and the oracle lines that declare them.
    /// </summary>
    /// <returns>Per row: the sentinel's value, the oracle line, and the source form it models.</returns>
    /// <remarks>
    /// THE EMPTY STRING IS A VALUE HERE AND NOT AN ABSENCE, which is the whole reason this row set exists.
    /// <c>FUNC_VAR = ""</c> [:L120] models a bare variable whose NAME is computed at run time, written
    /// <c>$$('name')</c>, and reading it as "no function name" would delete
    /// <c>EXPANSION_MODE_DYNAMIC_INDIRECT</c> from the contract. Their BEHAVIOUR is asserted in
    /// MacroInvokerTests.cs; what is asserted here is only that the values are these and that the engine
    /// exposes them, because the values themselves travel on the wire.
    /// </remarks>
    public static TheoryData<string, int, string> MacroFunctionSentinels() =>
        new()
        {
            { "", 120, "$$('name')" },
            { "Invoke", 121, "$$Invoke('Fn',args)" },
        };

    /// <summary>
    /// <c>FUNC_VAR</c> and <c>FUNC_INVOKE</c> keep their oracle values and are reachable from the engine's
    /// own sentinel surface.
    /// </summary>
    /// <param name="expectedValue">The sentinel's value.</param>
    /// <param name="oracleLine">The oracle line that declares it.</param>
    /// <param name="sourceForm">The source form it models, for the failure message.</param>
    [Theory]
    [MemberData(nameof(MacroFunctionSentinels))]
    public void MacroFunctionSentinels_KeepTheirOracleValues(
        string expectedValue,
        int oracleLine,
        string sourceForm)
    {
        Assert.NotEmpty(sourceForm);
        Assert.InRange(oracleLine, 120, 121);

        if (oracleLine == 120)
        {
            Assert.Equal(MacroSentinels.FUNC_VAR, expectedValue);
            Assert.Empty(MacroSentinels.FUNC_VAR);
            Assert.True(MacroSentinels.IsFuncVar(expectedValue));
            Assert.False(MacroSentinels.IsFuncInvoke(expectedValue));
        }
        else
        {
            Assert.Equal(MacroSentinels.FUNC_INVOKE, expectedValue);
            Assert.Equal("Invoke", MacroSentinels.FUNC_INVOKE);
            Assert.True(MacroSentinels.IsFuncInvoke(expectedValue));
            Assert.False(MacroSentinels.IsFuncVar(expectedValue));
        }

        // The two are distinct, which is what makes the dispatch switch at [:L1386-L1401] a switch.
        Assert.NotEqual(MacroSentinels.FUNC_VAR, MacroSentinels.FUNC_INVOKE);
    }

    /// <summary>
    /// The engine members whose ported shape is an ORACLE ANOMALY carried across rather than tidied.
    /// </summary>
    /// <returns>
    /// Per row: the member name; the oracle line that declares it; and the result type the oracle gives it.
    /// </returns>
    /// <remarks>
    /// ONE ROW TODAY, AND A MATRIX ANYWAY. The shape is the point: an anomaly asserted in a table invites
    /// the next one to be added beside it rather than asserted somewhere else, and each row carries the
    /// locator that justifies the anomaly so nobody has to take it on trust.
    /// </remarks>
    public static TheoryData<string, int, Type> OracleShapeAnomalies() =>
        new()
        {
            { "_of_calcitem", 142, typeof(bool) },
        };

    /// <summary>
    /// <c>_of_calcitem</c> is PUBLIC despite the private-convention underscore prefix, and answers
    /// <c>boolean</c> rather than a return code. Both are the oracle's own choices and both are carried
    /// across rather than tidied (constraint C-B).
    /// </summary>
    /// <param name="memberName">The member name, spelled as the oracle spells it.</param>
    /// <param name="oracleLine">The oracle line that declares it.</param>
    /// <param name="expectedResultType">The result type the oracle gives it.</param>
    /// <remarks>
    /// <para>
    /// THE ANOMALY IS DECLARED AT [:L142]: <c>public function boolean _of_calcitem(readonly long row,
    /// readonly integer index)</c>. Every other underscore-prefixed member of the object is private, so the
    /// prefix is a naming CONVENTION that this one member breaks - and it is reachable across the C-04
    /// boundary as a result, which is why the name cannot be quietly changed to match the convention.
    /// </para>
    /// <para>
    /// THE <c>bool</c> RESULT IS ALSO THE ORACLE'S, and false is heavily overloaded: not dirty, recursion
    /// refused, the wrong emptiness for the current pass, an unknown column type, an aborted preprocess, an
    /// evaluator sentinel, an unchanged value, or a vetoing item-changed handler. Its only internal caller
    /// reads it as "did anything happen" and <c>of_calc</c> DISCARDS it entirely [:L372], so widening it to
    /// a return code would invent information the oracle never had.
    /// </para>
    /// <para>
    /// THE PORT IS ASYNCHRONOUS, so the result type is <c>ValueTask&lt;bool&gt;</c> and the assertion
    /// unwraps it. That difference is structural rather than behavioural: a macro invocation crosses a
    /// network boundary in this architecture, so the calculation chain cannot be synchronous - and the
    /// ASSERTION is on the unwrapped type precisely so the oracle's contract is what is being checked.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(OracleShapeAnomalies))]
    public void OracleShapeAnomaly_IsCarriedAcrossRatherThanTidied(
        string memberName,
        int oracleLine,
        Type expectedResultType)
    {
        Assert.Equal(142, oracleLine);

        MethodInfo? method = typeof(ColumnExpressionEngine).GetMethod(
            memberName,
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(method);

        // PUBLIC, despite the private-convention prefix.
        Assert.True(method.IsPublic);
        Assert.StartsWith("_", method.Name, StringComparison.Ordinal);
        Assert.Equal(memberName, method.Name);

        // And the oracle's own result type, unwrapped from the asynchronous carrier.
        Assert.True(method.ReturnType.IsGenericType);
        Assert.Equal(typeof(ValueTask<>), method.ReturnType.GetGenericTypeDefinition());
        Assert.Equal(expectedResultType, method.ReturnType.GetGenericArguments()[0]);
    }

    /// <summary>
    /// Three chained expressions bound in two different orders, where one expression's INPUT is another's
    /// OUTPUT - so the execution order is not merely observable, it decides the answers.
    /// </summary>
    /// <returns>
    /// Per row: the columns in the order their expressions are ADDED; their expressions, positionally
    /// matched; and the final values of <c>n1</c>, <c>n2</c> and <c>n3</c> in that fixed order.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE LEGACY ASSERTS THIS ABOUT ITSELF, TWICE, IN A COMMENT ON THE CALL:
    /// "*重新计算所有列的表达示时是以表达示添加的顺序执行的" - when recalculating every column's
    /// expression, execution follows THE ORDER THE EXPRESSIONS WERE ADDED
    /// [w_test_dwsvc_columnexp.srw:L159, repeated at :L222 and :L255].
    /// </para>
    /// <para>
    /// ROW 1 ADDS THEM IN DEPENDENCY ORDER, so each expression's input is already computed when it runs:
    /// <c>n1 = 5</c>, then <c>n2 = n1 * 2 = 10</c>, then <c>n3 = n2 + 1 = 11</c>.
    /// </para>
    /// <para>
    /// ROW 2 ADDS THE IDENTICAL THREE EXPRESSIONS IN REVERSE, and the answers change: <c>n3 = n2 + 1</c>
    /// runs first against an as-yet-uncalculated <c>n2</c> of 0 and yields 1, then <c>n2 = n1 * 2</c> runs
    /// against an uncalculated <c>n1</c> and yields 0, then <c>n1 = 5</c> finally lands. So <c>n3</c> is 1
    /// rather than 11. NOTHING ELSE DIFFERS between the rows - same expressions, same columns, same data -
    /// which is what makes the pair a proof about ORDER rather than about arithmetic.
    /// </para>
    /// <para>
    /// AND THE PASS DOES NOT ITERATE TO CONVERGENCE, which row 2 also shows. A row pass raises the dirty
    /// flags ONCE before its loop [:L1164] so each expression calculates at most once however many cascades
    /// reach it, and the cascade [:L860-L862] is suppressed outright while <c>_bRowCalcing</c> is set. Row 2
    /// would be indistinguishable from row 1 in an implementation that re-ran until nothing changed.
    /// </para>
    /// </remarks>
    public static TheoryData<string[], string[], decimal[]> CalcAllOrderingCases() =>
        new()
        {
            // Added in dependency order: 5, then 5*2, then 10+1.
            { ["n1", "n2", "n3"], ["5", "n1 * 2", "n2 + 1"], [5m, 10m, 11m] },

            // The same three, added in reverse. n3 runs first against an uncalculated n2.
            { ["n3", "n2", "n1"], ["n2 + 1", "n1 * 2", "5"], [5m, 0m, 1m] },
        };

    /// <summary>
    /// <c>of_CalcAll</c> executes expressions IN THE ORDER THEY WERE ADDED, on every row, and the order is
    /// load-bearing when one expression's input is another's output.
    /// </summary>
    /// <param name="columns">The columns in the order their expressions are added.</param>
    /// <param name="expressions">Their expressions, positionally matched to the columns.</param>
    /// <param name="expectedValues">The final values of n1, n2 and n3, in that fixed order.</param>
    /// <remarks>
    /// THE ASSERTION IS AN ORDERED LIST AND NEVER A SET. The trace sink records every calculated item in
    /// emission order [:L751-L758 emits before the write arms, so an item that computed an unchanged value
    /// still appears], so the recorded column names ARE the execution order. A set comparison would accept
    /// the reverse ordering and therefore accept the wrong answers in row 2.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CalcAllOrderingCases))]
    public async Task CalcAll_RunsExpressionsInTheOrderTheyWereAdded(
        string[] columns,
        string[] expressions,
        decimal[] expectedValues)
    {
        Assert.Equal(columns.Length, expressions.Length);

        ScriptedTraceSink sink = new();
        ColumnExpressionEngine engine = NewEngine(out FakeDataWindowHost host, sink);
        Assert.Equal(RetCode.OK, engine.SetTrace(true));

        for (int position = 0; position < columns.Length; position++)
        {
            Assert.Equal(position + 1, engine.AddExp(columns[position], expressions[position]));
        }

        // w_test_dwsvc_columnexp.srw:L160 - of_CalcAll(), the no-argument arity.
        Assert.Equal(RetCode.OK, await engine.CalcAllAsync(Ct));

        // THE EXECUTION ORDER, as an ordered list: the add order, repeated once per row.
        long rowCount = host.RowCount();
        Assert.True(rowCount > 1);

        List<string> expectedOrder = [];
        for (long row = 1; row <= rowCount; row++)
        {
            expectedOrder.AddRange(columns);
        }

        Assert.Equal<IEnumerable<string>>(
            expectedOrder,
            sink.Records.Select(record => record.ColumnName));

        // Every row was walked, and each expression ran exactly once per row.
        Assert.Equal((int)rowCount * columns.Length, sink.Count);

        // And the answers, which the order decided.
        Assert.Equal(expectedValues[0], host.GetItemDecimal(1L, "n1"));
        Assert.Equal(expectedValues[1], host.GetItemDecimal(1L, "n2"));
        Assert.Equal(expectedValues[2], host.GetItemDecimal(1L, "n3"));
    }

    /// <summary>
    /// Every published calculation arity, each of which must reach the same calculation for an expression
    /// that is not input-gated.
    /// </summary>
    /// <returns>Per row: the arity to exercise, and the oracle line that declares it.</returns>
    /// <remarks>
    /// <para>
    /// SIX ARITIES ACROSS TWO ENTRY POINTS. <c>of_calc</c> has four - by index [:L348], by column name
    /// [:L375], with a force flag [:L1138] and with neither [:L1179] - and <c>of_calcall</c> has two,
    /// forcing [:L1182] and not [:L378]. The legacy driver window uses three of them: <c>of_Calc(1)</c>
    /// [srw:L117], <c>of_CalcAll()</c> [srw:L160] and <c>of_CalcAll(true)</c> [srw:L223].
    /// </para>
    /// <para>
    /// THE DOCUMENTED DIFFERENCES ARE THE ONLY DIFFERENCES, which is what this matrix asserts. <c>force</c>
    /// changes exactly one thing - whether an expression gated on user input recalculates during a row pass
    /// [:L1168-L1170] - and that difference is asserted where it belongs, in
    /// <see cref="RelativeColumn_FiresOnAnyChange_RelativeInputColumn_OnlyOnTypedInput"/>. For an expression
    /// with no input gate the six must be indistinguishable in effect, and a row here that answered
    /// differently would mean an arity had acquired a behaviour of its own.
    /// </para>
    /// </remarks>
    public static TheoryData<string, int> CalculationArities() =>
        new()
        {
            { "CalcRow", 1179 },
            { "CalcRowNoForce", 1138 },
            { "CalcRowForce", 1138 },
            { "CalcRowByIndex", 348 },
            { "CalcRowByName", 375 },
            { "CalcAll", 378 },
            { "CalcAllForce", 1182 },
        };

    /// <summary>
    /// All six calculation arities reach the SAME calculation, with only the documented <c>force</c>
    /// difference between them - which an expression without an input gate cannot observe.
    /// </summary>
    /// <param name="arity">Which entry point to call.</param>
    /// <param name="oracleLine">The oracle line that declares it.</param>
    [Theory]
    [MemberData(nameof(CalculationArities))]
    public async Task EveryCalculationArity_ReachesTheSameCalculation(string arity, int oracleLine)
    {
        Assert.InRange(oracleLine, 348, 1182);

        ColumnExpressionEngine engine = NewEngine(out FakeDataWindowHost host);
        Assert.True(engine.AddExp(BoundColumn, "n2 * 2") > 0);
        Poke(host, 1L, "n2", 4m);

        long answer = arity switch
        {
            "CalcRow" => await engine.CalcAsync(1L, Ct),
            "CalcRowNoForce" => await engine.CalcAsync(1L, false, Ct),
            "CalcRowForce" => await engine.CalcAsync(1L, true, Ct),
            "CalcRowByIndex" => await engine.CalcAsync(1L, 1, Ct),
            "CalcRowByName" => await engine.CalcAsync(1L, BoundColumn, Ct),
            "CalcAll" => await engine.CalcAllAsync(Ct),
            "CalcAllForce" => await engine.CalcAllAsync(true, Ct),
            _ => throw new ArgumentOutOfRangeException(
                nameof(arity),
                arity,
                "Every row of CalculationArities must name one of the published entry points."),
        };

        Assert.Equal(RetCode.OK, answer);
        Assert.Equal(8m, host.GetItemDecimal(1L, BoundColumn));
        Assert.Null(engine.LastError);
    }

    /// <summary>
    /// The three emptiness cells: a non-string column, a string column, and a string column that declares
    /// empty to BE null.
    /// </summary>
    /// <returns>
    /// Per row: the column to bind an empty-valued expression to; whether the column declares empty as
    /// null; and the value the trace must carry.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE SENTINEL IS SELECTED BY A DISJUNCTION INSIDE A CONJUNCTION [:L758]:
    /// <c>iif(sVal = "" and (emptyStringIsNull or colType &lt;&gt; COL_TYPE_STRING), "(null)", sVal)</c>.
    /// Row 1 reaches it through the COLUMN TYPE limb, row 3 through the <c>emptystringisnull</c> limb, and
    /// row 2 reaches NEITHER - which is the cell that proves the condition is a conjunction rather than a
    /// plain emptiness test, and the one an implementation that just tested for emptiness would fail.
    /// </para>
    /// <para>
    /// <c>"(null)"</c> IS A RENDERING AND NOT A NULL. The traced string is literally those six characters,
    /// which is what a golden-master comparison of a recording contains, so it is asserted as text.
    /// </para>
    /// </remarks>
    public static TheoryData<string, bool, string> NullSentinelTraceCases() =>
        new()
        {
            // A DECIMAL column: the colType limb, so the sentinel appears without any flag being set.
            { "n1", false, "(null)" },

            // A CHAR column with no NilIsNull: NEITHER limb, so the empty string is traced as itself.
            { "s1", false, "" },

            // A CHAR column that declares NilIsNull: the emptyStringIsNull limb.
            { "s2", true, "(null)" },
        };

    /// <summary>
    /// An empty calculated value is traced as <c>"(null)"</c> when the expression declares empty to be
    /// null OR the column is not a string column - and as the empty string when neither holds.
    /// </summary>
    /// <param name="column">The column to bind.</param>
    /// <param name="declaresNilIsNull">Whether the column declares empty as null.</param>
    /// <param name="expectedTracedValue">The value the trace must carry.</param>
    [Theory]
    [MemberData(nameof(NullSentinelTraceCases))]
    public async Task Trace_SelectsTheNullSentinelOnEitherLimbAndNeitherElse(
        string column,
        bool declaresNilIsNull,
        string expectedTracedValue)
    {
        ScriptedTraceSink sink = new();
        ColumnExpressionEngine engine = NewEngine(out FakeDataWindowHost host, sink);
        Assert.Equal(RetCode.OK, engine.SetTrace(true));

        if (declaresNilIsNull)
        {
            // The column's own declaration, read during the bind [:L1386 sets emptystringisnull from it].
            host.Column(column).SetProperty("edit.NilIsNull", "yes");
        }

        // An expression that evaluates to the empty string, which is what both limbs are about.
        Assert.True(engine.AddExp(column, "''") > 0);

        ColumnExpressionData bound = Assert.Single(engine.Expressions);
        Assert.Equal(declaresNilIsNull, bound.EmptyStringIsNull);

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));

        ExpressionTraceRecord traced = Assert.Single(sink.Records);
        Assert.Equal(expectedTracedValue, traced.Value);

        // The same selection, computed independently from the two limbs rather than restated.
        Assert.Equal(
            ScriptedTraceSink.ExpectedValue(string.Empty, bound.EmptyStringIsNull, bound.ColType),
            traced.Value);

        // The sentinel is literal text, six characters, not an absent value.
        if (expectedTracedValue.Length > 0)
        {
            Assert.Equal(ScriptedTraceSink.NullValueSentinel, traced.Value);
            Assert.Equal(6, traced.Value.Length);
        }
    }

    /// <summary>
    /// Which columns the EMPTY-ONLY pass considers empty on the fixture's first row.
    /// </summary>
    /// <returns>Per row: the column, its one-based expression index, and whether it counts as empty.</returns>
    /// <remarks>
    /// THE FIXTURE'S OWN <c>data(...)</c> LINE DECIDES THIS [dw_test_dwsvc_columnexp.srd:L14]: it seeds
    /// <c>0, 0, 0, null, null</c> on every row, so the two CHAR columns are genuinely empty and the three
    /// DECIMAL columns hold a zero - and a zero is a VALUE, not an absence. That is why <c>n1</c> is not
    /// recalculated by an empty-only pass while <c>s1</c> and <c>s2</c> are, and it is the distinction the
    /// pass exists to make.
    /// </remarks>
    public static TheoryData<string, int, bool> EmptyOnlyPassCases() =>
        new()
        {
            // A decimal column holding 0 is NOT empty - the zero is a value.
            { "n1", 1, false },

            // Both char columns are seeded null, so both are.
            { "s1", 2, true },
            { "s2", 3, true },
        };

    /// <summary>
    /// <c>of_calcempty</c> calculates only the expressions whose bound column is currently EMPTY, and a
    /// zero in a numeric column is not empty.
    /// </summary>
    /// <param name="column">The column under test.</param>
    /// <param name="expressionIndex">Its one-based expression index.</param>
    /// <param name="expectedEmpty">Whether the pass considers it empty.</param>
    /// <remarks>
    /// ALL THREE EXPRESSIONS ARE BOUND IN EVERY ROW so the pass is asked the same question each time and
    /// only the assertion moves. That matters because the pass walks every expression [:L1956-L2066]: a row
    /// that bound only its own column could not show that the pass DISCRIMINATED between them.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EmptyOnlyPassCases))]
    public async Task CalcEmpty_TreatsAZeroAsAValueAndOnlyANullAsEmpty(
        string column,
        int expressionIndex,
        bool expectedEmpty)
    {
        ScriptedTraceSink sink = new();
        ColumnExpressionEngine engine = NewEngine(out FakeDataWindowHost host, sink);
        Assert.Equal(RetCode.OK, engine.SetTrace(true));

        host.Column("s2").SetProperty("edit.NilIsNull", "yes");

        Assert.Equal(1, engine.AddExp("n1", "''"));
        Assert.Equal(2, engine.AddExp("s1", "''"));
        Assert.Equal(3, engine.AddExp("s2", "''"));

        Assert.Equal(RetCode.OK, await engine.CalcEmptyAsync(1L, Ct));

        Assert.Equal(expectedEmpty, engine.Expressions[expressionIndex - 1].Empty);

        // Only the empty ones were traced, which is the observable half of the same fact.
        Assert.Equal(expectedEmpty, sink.For(column).Length > 0);
        Assert.Equal(column, engine.Expressions[expressionIndex - 1].Name);
    }

    /// <summary>
    /// The <c>#Trace</c> flag's initial state, from the option that carries it.
    /// </summary>
    /// <returns>Per row: the option value, or null for the preserved default, and the expected state.</returns>
    /// <remarks>
    /// [:L98] DECLARES <c>privatewrite boolean #Trace</c> WITH NO INITIALIZER, so PowerBuilder's own
    /// default is false and the port carries that default forward through
    /// <c>ColumnExpressionOptions.Trace</c>. The null row is the one that asserts the preserved default;
    /// the other two assert that deployment can override it in both directions.
    /// </remarks>
    public static TheoryData<bool?, bool> TraceDefaultCases() =>
        new()
        {
            { null, false },
            { false, false },
            { true, true },
        };

    /// <summary>
    /// <c>#Trace</c> defaults to OFF, is publicly readable and privately written, and
    /// <c>of_settrace</c> answers <see cref="RetCode.OK"/> unconditionally while gating the payload.
    /// </summary>
    /// <param name="optionValue">The configured value, or null for the preserved default.</param>
    /// <param name="expectedInitialState">The state the engine must start in.</param>
    /// <remarks>
    /// <para>
    /// THE FLAG IS THE SINGLE GATE [:L751 tests <c>#Trace</c> and nothing else], so with it off NOTHING is
    /// emitted however much is calculated - which is asserted before it is turned on, because an
    /// implementation that always emitted and filtered at the sink would pass an on-only test.
    /// </para>
    /// <para>
    /// <c>of_settrace</c> ANSWERS OK UNCONDITIONALLY [:L2410], including when the state is unchanged, and
    /// is the ONLY mutator - <c>#Trace</c> is <c>privatewrite</c> [:L98], which the port expresses as a
    /// private setter and which is asserted by reflection so the property cannot quietly acquire a public
    /// one.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(TraceDefaultCases))]
    public async Task Trace_DefaultsOffAndSetTraceIsTheOnlyMutator(
        bool? optionValue,
        bool expectedInitialState)
    {
        ScriptedTraceSink sink = new();
        ColumnExpressionOptions? options =
            optionValue is null ? null : new ColumnExpressionOptions { Trace = optionValue.Value };

        // The preserved default is asserted on the options type itself as well as on the engine.
        Assert.False(new ColumnExpressionOptions().Trace);

        ColumnExpressionEngine engine = NewEngine(out FakeDataWindowHost host, sink, options: options);
        Assert.Equal(expectedInitialState, engine.Trace);

        Assert.True(engine.AddExp(BoundColumn, "n2 + 1") > 0);
        Poke(host, 1L, "n2", 6m);

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(7m, host.GetItemDecimal(1L, BoundColumn));

        // With the gate down nothing is emitted at all; with it up the payload arrives.
        Assert.Equal(expectedInitialState, sink.Count > 0);

        sink.Clear();
        Assert.Equal(RetCode.OK, engine.SetTrace(true));
        Assert.True(engine.Trace);

        Poke(host, 1L, "n2", 9m);
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(10m, host.GetItemDecimal(1L, BoundColumn));

        ExpressionTraceRecord traced = Assert.Single(sink.Records);
        Assert.Equal(BoundColumn, traced.ColumnName);
        Assert.Equal(1L, traced.Row);
        Assert.Equal("10", traced.Value);

        // Setting it again, to the state it already holds, still answers OK [:L2410].
        Assert.Equal(RetCode.OK, engine.SetTrace(true));

        // And off again suppresses emission without suppressing the calculation.
        sink.Clear();
        Assert.Equal(RetCode.OK, engine.SetTrace(false));
        Assert.False(engine.Trace);

        Poke(host, 1L, "n2", 11m);
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(12m, host.GetItemDecimal(1L, BoundColumn));
        Assert.Empty(sink.Records);

        // privatewrite [:L98] - readable publicly, writable only through of_settrace.
        PropertyInfo? trace = typeof(ColumnExpressionEngine).GetProperty(
            nameof(ColumnExpressionEngine.Trace),
            BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(trace);
        Assert.True(trace.CanRead);
        Assert.False(trace.SetMethod?.IsPublic ?? false);
    }
}
