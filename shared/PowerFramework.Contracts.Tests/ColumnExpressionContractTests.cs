// ==================================================================================================
//  ColumnExpressionContractTests - THE GUARD ON C-04, THE HARDEST CONTRACT IN THE REFACTOR
//  ------------------------------------------------------------------------------------------------
//  SUBJECT   The column-expression half of shared/PowerFramework.Contracts/Proto/dataservices.v1.proto:
//            ExpansionMode, VarData, FuncData, ColumnExpData, ColumnData, GlobalVarData, LocalVarData,
//            ForeignVarRef, VarValue, ExpressionSentinels, ExpressionBinding, ExpressionError and the
//            ColumnExpressionService method surface.
//
//  ORACLE    ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru - 2,435 lines, the
//            largest in-scope legacy object - and docs/n_cst_dwsvc_columnexp.md, the authoritative
//            legacy specification. BOTH ARE READ AS SPECIFICATION ONLY (constraint C-C): every locator
//            below is a citation in a comment, and no test in this file opens a legacy file at run time.
//
//  ============ THE ONE SENTENCE THIS WHOLE FILE EXISTS TO ENFORCE ==================================
//
//      THE EXPANSION MODE IS A PROPERTY OF EACH VARIABLE REFERENCE, NEVER OF THE EXPRESSION.
//
//  That is proven by the oracle rather than asserted about it. The specification's worked example runs
//  the identical sequence twice - define an expression variable valued "5" and a numeric variable
//  valued 0, bind their sum, mutate the numeric variable to 1, recalculate - and gets a DIFFERENT
//  ANSWER each time, differing only by one extra `$`:
//
//      STATIC   `$上月读数 + $本月读数`    -> the result stays PERMANENTLY 5
//                                            [docs/n_cst_dwsvc_columnexp.md section 静态展开 :L37,
//                                             syntax :L41]
//      DYNAMIC  `$$上月读数 + $本月读数`   -> THE IDENTICAL SEQUENCE YIELDS 6
//                                            [section 动态展开 :L56, syntax :L60, example :L68,
//                                             result :L69]
//
//  Read the dynamic line again: it MIXES `$$` and `$` INSIDE ONE EXPRESSION. A second witness says the
//  same thing in pure ASCII, free of any transliteration question - `of_SetExp("n1", "$$bb + $aa")`
//  [docs/n_cst_dwsvc_columnexp.md:L160]. A contract that tagged the expression with one mode could not
//  represent either line, so ANY assertion treating mode as a property of the expression is asserting
//  the wrong contract.
//
//  ============ CITATION CONVENTION USED THROUGHOUT THIS FILE ======================================
//  TWO oracles are cited here and their line numbers collide, so the form is disambiguated rather than
//  left to context:
//
//      [:Lnnn]                                 ALWAYS ws_objects/pfw.datawindow.services.pbl.src/
//                                              n_cst_dwsvc_columnexp.sru - the 2,435-line source
//      [docs/n_cst_dwsvc_columnexp.md:Lnnn]    ALWAYS the 166-line specification, named in full
//      [se_cst_dw.sru:Lnnn]                    ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru
//
//  The collision is real and would mislead: :L117 is `VAR_LOCAL` in the source and the
//  `$FormatPrice(n2, $精度)` example in the specification, and :L160 is the `of_setvar time` overload in
//  the source and the mixed-mode `$$bb + $aa` witness in the specification. A bare number would name the
//  wrong evidence in both cases.
//
//  ============ WHY THE PAYLOAD NEEDS THREE PARTS AND NOT ONE ======================================
//  AAP 0.6.2.2 states the consequence exactly: the payload must transmit ALL THREE of the unexpanded
//  source expression, the bind-time variable snapshot and the live variable environment, plus, per
//  reference, which expansion mode applied. The mechanism is that the legacy parse routine rewrites the
//  expression IN PLACE - `_of_parseexp(ref string exp, ref vardata vars[], ref funcdata fns[])` takes
//  all three parameters by reference - and a STATIC reference is substituted into the rewritten text at
//  bind time, so THE VARIABLE'S NAME IS GONE from what is stored. Transmit only the rewritten string
//  and a static binding becomes indistinguishable from a literal while a dynamic binding loses the
//  environment it resolves against. Both losses are silent.
//
//  ============ WHAT THIS FILE IS NOT =============================================================
//  It asserts SHAPE, and only shape (constraint C-A). There is no expression evaluation here, no
//  parser, no engine, no `$`-expansion, and no attempt to reproduce the 5-versus-6 arithmetic. Those
//  belong to services/dataservices-service; PowerFramework.Contracts "carries no behaviour; it is the
//  boundary definition, not a shared-code back door" (AAP 0.4.2.3), and a test project that grew an
//  evaluator would breach that boundary in the one place nobody audits for it.
//
//  Every assertion reaches the contract through DESCRIPTORS (ContractDescriptors), never through CLR
//  reflection, for the reason ContractTestContext.cs sets out at length: protoc PascalCases the C#
//  projection, so `EXPANSION_MODE_MACRO_DYNAMIC` becomes the member `MacroDynamic` and the authored
//  SCREAMING_SNAKE spelling - which AAP 0.4.5.3 requires preserved verbatim because it appears in
//  serialized payloads, log records and characterization recordings - is unreachable from the CLR
//  surface. `EnumValueDescriptor.Name` is the only thing in the build that can read it back.
//
//  ============ PURITY ============================================================================
//  Descriptor reflection only. No clock, no randomness, no environment variable, no network, no file
//  I/O, no sleep, no mutable static state. Repeatability is the hard prerequisite of the Golden-Master
//  approach this repository adopts (AAP 0.6.7). No secret literal appears anywhere in this file, and
//  nothing here needs one.
//
// ==================================================================================================

using Google.Protobuf.Reflection;
using Xunit;

namespace PowerFramework.Contracts.Tests;

/// <summary>
/// Guards contract C-04, <c>dataservices.v1.ColumnExpressionService</c>, against the four ways the
/// column-expression model can be silently flattened across a network boundary: losing the
/// per-reference expansion mode, transmitting an already-expanded expression, collapsing the typed
/// variable environment into strings, and pretending a live object pointer can be serialized.
/// </summary>
/// <remarks>
/// <para>
/// <b>Oracle.</b> <c>ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru</c> and
/// <c>docs/n_cst_dwsvc_columnexp.md</c>, both read as specification only - never at run time
/// (constraint C-C). Line locators appear as <c>[:Lnnn]</c> citations throughout.
/// </para>
/// <para>
/// <b>Shape only.</b> No expression is evaluated, parsed or expanded anywhere in this class
/// (constraint C-A). The 5-versus-6 arithmetic of the specification's worked example is quoted as
/// evidence for what the contract must be able to carry, and is deliberately not reproduced.
/// </para>
/// <para>
/// <b>A descriptor limitation, stated rather than worked around.</b> Protobuf leading comments are not
/// retained in the runtime descriptor graph - <c>protoc</c> keeps them only in a
/// <c>SourceCodeInfo</c> section that the generated C# descriptor does not surface. So where the
/// contract records a fact ONLY in a comment, no test here can assert it, and the reader is pointed at
/// the <c>.proto</c> text instead. Every requirement that this affects is called out at the assertion
/// concerned rather than dropped: see
/// <see cref="TheTwoGrammarSentinelsAreCarriedAsContractFieldsRatherThanCommentedConstants"/>, which
/// asserts the carrier that exists instead of the constant values that only a comment can hold.
/// </para>
/// </remarks>
public sealed class ColumnExpressionContractTests
{
    // ==============================================================================================
    //  THE PROTO IDENTIFIERS UNDER TEST, NAMED ONCE.
    //
    //  Every one of these was read out of shared/PowerFramework.Contracts/Proto/dataservices.v1.proto
    //  rather than inferred from the legacy structure names, because the two DELIBERATELY DIFFER: the
    //  legacy `foreignvardata` [:L80-L83] becomes `ForeignVarRef` - "Ref", not "Data" - precisely
    //  because the field that made it "Data" was a pointer that cannot cross a wire. Naming them here
    //  means a rename in the contract produces ONE compile-visible edit rather than a scatter of
    //  string literals that drift apart.
    // ==============================================================================================

    /// <summary>The <c>vardata</c> counterpart [:L50-L56] - ONE VARIABLE REFERENCE inside an expression.</summary>
    private const string VarDataMessage = "VarData";

    /// <summary>The <c>funcdata</c> counterpart [:L58-L63] - one function or macro reference.</summary>
    private const string FuncDataMessage = "FuncData";

    /// <summary>The <c>columnexpdata</c> counterpart [:L22-L42] - one bound expression, 19 fields.</summary>
    private const string ColumnExpDataMessage = "ColumnExpData";

    /// <summary>The <c>columndata</c> counterpart [:L44-L48] - the reverse dependency index.</summary>
    private const string ColumnDataMessage = "ColumnData";

    /// <summary>The <c>globalvardata</c> counterpart [:L65-L71] - one entry of the variable table.</summary>
    private const string GlobalVarDataMessage = "GlobalVarData";

    /// <summary>The <c>localvardata</c> counterpart [:L73-L78] - a variable whose value is an expression.</summary>
    private const string LocalVarDataMessage = "LocalVarData";

    /// <summary>
    /// The <c>foreignvardata</c> counterpart [:L80-L83], renamed because <c>expsvc</c> is not portable.
    /// </summary>
    private const string ForeignVarRefMessage = "ForeignVarRef";

    /// <summary>The variable environment's value carrier - the seven-arm discriminated union.</summary>
    private const string VarValueMessage = "VarValue";

    /// <summary>The three-part payload the <c>$</c>/<c>$$</c> distinction forces onto the wire.</summary>
    private const string ExpressionBindingMessage = "ExpressionBinding";

    /// <summary>The carrier for <c>FUNC_VAR</c> [:L120] and <c>FUNC_INVOKE</c> [:L121].</summary>
    private const string ExpressionSentinelsMessage = "ExpressionSentinels";

    /// <summary>The structured replacement for the 28 live <c>MessageBox</c> parse-error sites.</summary>
    private const string ExpressionErrorMessage = "ExpressionError";

    /// <summary>The five-mode enum, declared at FILE scope in <c>dataservices.v1</c>.</summary>
    private const string ExpansionModeEnum = "ExpansionMode";

    /// <summary>The field of <see cref="VarDataMessage"/> that carries the mode. Proto spelling.</summary>
    private const string ExpansionModeField = "expansion_mode";

    /// <summary>
    /// The proto3 zero sentinel of <see cref="ExpansionModeEnum"/>. It is NOT one of the five modes -
    /// proto3 requires a zero-valued first member, and giving that slot a real mode would make the
    /// default indistinguishable from a deliberate choice.
    /// </summary>
    private const string ExpansionModeUnspecified = "EXPANSION_MODE_UNSPECIFIED";

    /// <summary>C-04's service, whose whole method surface is the column-expression API.</summary>
    private const string ColumnExpressionServiceName = "ColumnExpressionService";

    // ==============================================================================================
    //  SECTION 1 - THE MODE IS PER-REFERENCE, AND ALL FIVE MODES ARE REPRESENTABLE
    // ==============================================================================================

    /// <summary>
    /// The five expansion modes of the legacy specification, each paired with the section and line that
    /// documents it. One theory row per mode, as the folder requirement asks.
    /// </summary>
    /// <remarks>
    /// The five are a CLOSED SET taken from the specification's own section headings, not a taxonomy
    /// invented here. Two of the five are macro forms and three are variable forms, which is why the
    /// enum spans both: a macro reference and a variable reference are both REFERENCES, and both need a
    /// mode.
    /// </remarks>
    public static TheoryData<string, string> ExpansionModes() => new()
    {
        // `$name` - substituted AT BIND TIME; later mutation does not change the result.
        // [docs/n_cst_dwsvc_columnexp.md section 静态展开 :L37, syntax :L41, fixed-at-5 worked example :L49]
        { "EXPANSION_MODE_STATIC", "静态展开 :L37-L54 - `$name`, substituted at bind time, result fixed at 5" },

        // `$$name` - retained AS A REFERENCE, resolved at CALCULATION time; mutation propagates.
        // [section 动态展开 :L56, syntax :L60, mixed-mode example :L68, result 6 :L69]
        { "EXPANSION_MODE_DYNAMIC", "动态展开 :L56-L73 - `$$name`, resolved at calc time, same sequence yields 6" },

        // `$$('nameString')` - the VARIABLE NAME is itself computed at run time and may itself be a
        // DataWindow expression:
        // `of_SetExp("n1","$$(if(n2 > n3 ,'num1','num2'))")` [docs/n_cst_dwsvc_columnexp.md:L92].
        // [docs section 4 :L75-L94, syntax :L79]
        { "EXPANSION_MODE_DYNAMIC_INDIRECT", "动态展开-根据变量名 :L75-L94 - `$$('name')`, name computed at run time" },

        // `$FunctionName(args)` - a macro whose implementation THE APPLICATION owns, dispatched back
        // across the boundary:
        // `of_SetExp("n1", "$FormatPrice(n2, $精度)")` [docs/n_cst_dwsvc_columnexp.md:L117].
        // [docs section 宏函数 1 直接调用 :L108-L128, syntax :L110]
        { "EXPANSION_MODE_MACRO_DIRECT", "宏函数-直接调用 :L108-L128 - `$Func(args)`, application-owned macro" },

        // `$$Invoke('FunctionName', args)` - THE FUNCTION NAME IS ITSELF A VARIABLE:
        // `of_SetExp("n1", "$$Invoke($单价格式化, n2, $精度)")` [docs/n_cst_dwsvc_columnexp.md:L141], the
        // variable having been set to "FormatPrice" at [docs/n_cst_dwsvc_columnexp.md:L139]. Selected by
        // the FUNC_INVOKE sentinel, which is a source constant: [:L121].
        // [docs section 宏函数 2 动态调用函数 :L130-L151, syntax :L134]
        { "EXPANSION_MODE_MACRO_DYNAMIC", "宏函数-动态调用 :L130-L151 - `$$Invoke($var, args)`, function name is a variable" },
    };

    /// <summary>
    /// Each of the five documented expansion modes is representable as a distinct value of the
    /// <c>ExpansionMode</c> enum.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One row per mode, each citing the section of <c>docs/n_cst_dwsvc_columnexp.md</c> that documents
    /// it (constraint C-K). The <paramref name="oracleCitation"/> parameter is carried into the failure
    /// message so a missing mode reports WHICH legacy behaviour would become unrepresentable, rather
    /// than only that a count was wrong.
    /// </para>
    /// <para>
    /// Asserted by proto spelling through <see cref="EnumValueDescriptor.Name"/>, because the generated
    /// C# member is PascalCased to <c>MacroDynamic</c> and can never confirm the authored
    /// <c>EXPANSION_MODE_MACRO_DYNAMIC</c> that appears in payloads and recordings (AAP 0.4.5.3).
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ExpansionModes))]
    public void EachOfTheFiveDocumentedExpansionModesIsRepresentable(string modeName, string oracleCitation)
    {
        EnumDescriptor modes = ContractDescriptors.RequireEnum(ExpansionModeEnum);

        EnumValueDescriptor? declared = modes.Values
            .SingleOrDefault(value => string.Equals(value.Name, modeName, StringComparison.Ordinal));

        Assert.True(
            declared is not null,
            $"ExpansionMode declares no value '{modeName}', so the legacy behaviour documented at " +
            $"{oracleCitation} would be UNREPRESENTABLE on the wire. Declared values: " +
            $"{string.Join(", ", modes.Values.Select(value => value.Name))}.");
    }

    /// <summary>
    /// <c>ExpansionMode</c> carries the five documented modes and exactly one further value, the proto3
    /// zero sentinel - so it is neither short of a mode nor padded with an invented one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why the count is asserted as well as the membership.</b> The per-row test above proves each
    /// of the five is present; it cannot prove that a SIXTH mode has not been invented. A sixth would
    /// be a behaviour the legacy has no syntax for, and constraint C-B forbids adding capability just
    /// as firmly as it forbids losing it. The two assertions together pin the set exactly.
    /// </para>
    /// <para>
    /// <b>The zero sentinel is required, not tolerated.</b> proto3 gives every enum field an implicit
    /// default of zero, so if <c>EXPANSION_MODE_STATIC</c> occupied slot 0 then a producer that never
    /// set the field would be indistinguishable from one that deliberately chose static expansion -
    /// and static-versus-dynamic is the single distinction this whole contract exists to preserve.
    /// </para>
    /// <para>
    /// The context sigil <c>@</c> (<c>MACRO_CONTEXT</c>
    /// [<c>n_cst_dwsvc_columnexp.sru</c>:L1253]) is deliberately NOT a sixth mode: it selects the
    /// resolution SCOPE rather than the expansion TIMING, the two are orthogonal, and it is carried by
    /// <c>VarData.is_ctx</c> instead. Folding it in would multiply the taxonomy rather than describe it.
    /// </para>
    /// </remarks>
    [Fact]
    public void ExpansionModeIsExactlyTheFiveModesPlusTheProtoThreeZeroSentinel()
    {
        EnumDescriptor modes = ContractDescriptors.RequireEnum(ExpansionModeEnum);

        IReadOnlyList<string> names = [.. modes.Values.Select(value => value.Name)];

        Assert.Equal(6, names.Count);

        EnumValueDescriptor sentinel = Assert.Single(
            modes.Values,
            value => string.Equals(value.Name, ExpansionModeUnspecified, StringComparison.Ordinal));

        Assert.Equal(0, sentinel.Number);

        // The five real modes occupy 1..5, so no mode can be mistaken for an unset field.
        IReadOnlyList<int> realModeNumbers =
        [
            .. modes.Values
                .Where(value => !string.Equals(value.Name, ExpansionModeUnspecified, StringComparison.Ordinal))
                .Select(value => value.Number)
                .Order(),
        ];

        Assert.Equal<IReadOnlyList<int>>([1, 2, 3, 4, 5], realModeNumbers);

        // Distinctness matters independently of the count: protobuf ALLOWS aliased values when
        // `option allow_alias` is set, and two modes sharing a number would collapse the distinction
        // while leaving six names in place - passing a naive count assertion.
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(modes.Values.Count, modes.Values.Select(value => value.Number).Distinct().Count());
    }

    /// <summary>
    /// The variable-reference message carries the expansion mode as its OWN field, typed as the
    /// <c>ExpansionMode</c> enum.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>VarData</c> is the <c>vardata</c> structure of
    /// <c>n_cst_dwsvc_columnexp.sru</c>:L50-L56, and it models ONE REFERENCE inside an expression.
    /// Putting the mode here - and, per the sweep below, nowhere at expression scope - is the
    /// structural expression of the file's governing sentence.
    /// </para>
    /// <para>
    /// The field is asserted to be a singular enum rather than, say, a repeated one or a string: a
    /// string would admit spellings the contract never defined, and a repeated field would say a single
    /// reference had several modes at once, which no legacy syntax can express.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheVariableReferenceCarriesItsOwnExpansionModeFieldTypedAsTheEnum()
    {
        MessageDescriptor reference = ContractDescriptors.RequireMessage(VarDataMessage);
        FieldDescriptor mode = ContractDescriptors.RequireField(reference, ExpansionModeField);

        Assert.Equal(FieldType.Enum, mode.FieldType);
        Assert.Equal(ExpansionModeEnum, mode.EnumType.Name);
        Assert.False(mode.IsRepeated, "one reference has one expansion mode, so the field must be singular.");
        Assert.False(mode.IsMap);
    }

    /// <summary>
    /// Every message that carries expression TEXT carries no expansion-mode field - the assertion that
    /// enforces "per-reference, not per-expression".
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the negative that gives the requirement its teeth.</b> A contract could pass every
    /// positive assertion above and still be wrong by ALSO tagging the expression, which would give a
    /// consumer two sources of truth that disagree the moment an expression mixes modes - and
    /// <c>"$$上月读数 + $本月读数"</c> [docs/n_cst_dwsvc_columnexp.md:L68] and
    /// <c>of_SetExp("n1", "$$bb + $aa")</c> [docs/n_cst_dwsvc_columnexp.md:L160] both mix them.
    /// </para>
    /// <para>
    /// <b>Why the sweep is scoped to expression-text carriers rather than to every message.</b> Two
    /// fields in the contract are legitimately typed <c>ExpansionMode</c>: <c>VarData.expansion_mode</c>,
    /// which is the point of the design, and the macro-invocation form field, which classifies ONE
    /// invocation and is therefore also per-reference. Sweeping every message would have to special-case
    /// both and would then assert nothing about future additions. Scoping to "declares a field holding
    /// expression text" is the principled rule: a message that holds an expression is at EXPRESSION
    /// scope, and expression scope is exactly where a mode must not appear.
    /// </para>
    /// <para>
    /// The sweep is driven off the descriptor graph rather than a hand-written list, so a message added
    /// to the contract later with an <c>exp</c> field is covered automatically. Synthetic map-entry
    /// messages are excluded because they were never authored (see
    /// <see cref="ContractDescriptors.AllMessages"/>).
    /// </para>
    /// </remarks>
    [Fact]
    public void NoMessageCarryingExpressionTextAlsoCarriesAnExpansionModeField()
    {
        // The proto spellings of "this field holds expression text": `exp` is the legacy field name
        // itself [columnexpdata.exp :L27, localvardata.exp :L74], and `unexpanded_exp` is the
        // three-part payload's first part.
        string[] expressionTextFieldNames = ["exp", "unexpanded_exp"];

        List<MessageDescriptor> expressionScopedMessages =
        [
            .. ContractDescriptors.AllMessages()
                .Where(message => !message.IsMapEntry)
                .Where(message => message.Fields.InDeclarationOrder().Any(field =>
                    field.FieldType == FieldType.String
                    && expressionTextFieldNames.Contains(field.Name, StringComparer.Ordinal))),
        ];

        // Guard the guard: if the sweep matched nothing it would pass vacuously and prove nothing.
        // ColumnExpData, LocalVarData and ExpressionBinding alone put the floor at three.
        Assert.True(
            expressionScopedMessages.Count >= 3,
            $"the sweep found only {expressionScopedMessages.Count} expression-text-carrying messages, so it "
            + "cannot be testing what it claims. Expected at least ColumnExpData, LocalVarData and "
            + "ExpressionBinding.");

        List<string> offenders =
        [
            .. from message in expressionScopedMessages
               from field in message.Fields.InDeclarationOrder()
               where field.FieldType == FieldType.Enum
                     && string.Equals(field.EnumType.Name, ExpansionModeEnum, StringComparison.Ordinal)
               select $"{message.FullName}.{field.Name}",
        ];

        Assert.True(
            offenders.Count == 0,
            "the expansion mode is a PER-REFERENCE property and must not appear at expression scope, but "
            + $"these expression-text-carrying messages declare an {ExpansionModeEnum} field: "
            + $"{string.Join(", ", offenders)}. A single expression can mix modes - see "
            + "docs/n_cst_dwsvc_columnexp.md:L68 and docs/n_cst_dwsvc_columnexp.md:L160 - so an "
            + "expression-level mode field cannot be correct for either line.");
    }

    /// <summary>
    /// <c>ColumnExpData</c>, the expression message itself, is named explicitly as carrying no
    /// expansion-mode field.
    /// </summary>
    /// <remarks>
    /// Redundant against the sweep by design. The sweep proves the RULE and would keep passing if
    /// <c>ColumnExpData</c> were renamed or its <c>exp</c> field spelled differently, at which point it
    /// would silently stop covering the single most important case. This row names that case, so the two
    /// fail for different reasons and a rename cannot quietly reduce coverage.
    /// </remarks>
    [Fact]
    public void TheExpressionMessageItselfDeclaresNoExpansionModeField()
    {
        MessageDescriptor expression = ContractDescriptors.RequireMessage(ColumnExpDataMessage);

        Assert.Null(ContractDescriptors.FindField(expression, ExpansionModeField));

        Assert.DoesNotContain(
            expression.Fields.InDeclarationOrder(),
            field => field.FieldType == FieldType.Enum
                     && string.Equals(field.EnumType.Name, ExpansionModeEnum, StringComparison.Ordinal));
    }

    /// <summary>
    /// The two legacy reference discriminators <c>isctx</c> and <c>ismacro</c> survive as their own
    /// independent boolean fields, and are not folded into the expansion mode.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>vardata</c> declares <c>boolean isctx</c> [:L54] and <c>boolean ismacro</c> [:L55] alongside
    /// name, full name and index. Both are ORTHOGONAL to the expansion mode, and the legacy proves it by
    /// branching on them independently: with the context sigil set, <c>ismacro</c> chooses between
    /// calling the context service's variable calculator and reading a plain column value from the
    /// context row. So <c>@name</c> and <c>@$name</c> are both context-scoped and differ only in
    /// <c>ismacro</c> - two states one enum value could not distinguish.
    /// </para>
    /// <para>
    /// Folding either into the mode would multiply the enum by four to preserve the same information,
    /// and would make the taxonomy describe combinations rather than concepts. Keeping all three as
    /// separate fields is what replicating the legacy model means here (constraint C-B).
    /// </para>
    /// </remarks>
    [Fact]
    public void TheReferencePreservesIsCtxAndIsMacroAsIndependentBooleanFields()
    {
        MessageDescriptor reference = ContractDescriptors.RequireMessage(VarDataMessage);

        foreach (string discriminator in (string[])["is_ctx", "is_macro"])
        {
            FieldDescriptor field = ContractDescriptors.RequireField(reference, discriminator);

            Assert.Equal(FieldType.Bool, field.FieldType);
            Assert.False(field.IsRepeated);

            // Independent means not sharing a oneof with the mode: a oneof would make them mutually
            // exclusive with it, which is exactly the folding this test forbids.
            Assert.Null(field.RealContainingOneof);
        }

        // The three carry three distinct field numbers, so none is an alias of another.
        FieldDescriptor isCtx = ContractDescriptors.RequireField(reference, "is_ctx");
        FieldDescriptor isMacro = ContractDescriptors.RequireField(reference, "is_macro");
        FieldDescriptor mode = ContractDescriptors.RequireField(reference, ExpansionModeField);

        Assert.Equal(3, new[] { isCtx.FieldNumber, isMacro.FieldNumber, mode.FieldNumber }.Distinct().Count());
    }

    /// <summary>
    /// The variable reference carries the legacy's own five members - name, full matched token,
    /// one-based table index, and the two discriminators - so nothing the parser emitted is dropped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>full_name</c> is the member most easily mistaken for a decorated copy of <c>name</c> and is
    /// the one it would be most damaging to drop: the legacy substitutes a static reference by replacing
    /// THIS EXACT SUBSTRING out of the expression text, sigils included, so it is the substitution key
    /// rather than a display form. A contract carrying only <c>name</c> could not perform static
    /// substitution at all.
    /// </para>
    /// <para>
    /// <c>index</c> is asserted to be a 32-bit integer because the legacy declares
    /// <c>integer index</c> [:L53], and zero is a legitimate value on the wire meaning "not yet bound,
    /// resolve me by name" - so a consumer must not treat it as malformed. That is a value-level
    /// convention a shape test cannot assert; it is recorded here so the reader is not misled into
    /// thinking presence implies non-zero.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheVariableReferenceCarriesEveryMemberOfTheLegacyVardataStructure()
    {
        MessageDescriptor reference = ContractDescriptors.RequireMessage(VarDataMessage);

        // `string name` [:L51]
        Assert.Equal(FieldType.String, ContractDescriptors.RequireField(reference, "name").FieldType);

        // `string fullname` [:L52] - the exact matched token, sigils included; the substitution key.
        Assert.Equal(FieldType.String, ContractDescriptors.RequireField(reference, "full_name").FieldType);

        // `integer index` [:L53] - one-based index into the global variable table.
        Assert.Equal(FieldType.Int32, ContractDescriptors.RequireField(reference, "index").FieldType);

        // `boolean isctx` [:L54], `boolean ismacro` [:L55] - asserted in full above.
        Assert.Equal(FieldType.Bool, ContractDescriptors.RequireField(reference, "is_ctx").FieldType);
        Assert.Equal(FieldType.Bool, ContractDescriptors.RequireField(reference, "is_macro").FieldType);

        // Five legacy members plus the carried mode, and nothing else: an extra field here would be
        // capability the legacy reference model does not have (constraint C-B).
        Assert.Equal(6, reference.Fields.InDeclarationOrder().Count);
    }

    // ==============================================================================================
    //  SECTION 2 - THE THREE-PART PAYLOAD
    //
    //  AAP 0.6.2.2: "the payload must transmit all three of the unexpanded source expression, the
    //  bind-time variable snapshot, and the live variable environment - plus, per reference, which
    //  expansion mode applied. Transmitting an already-expanded string makes static bindings
    //  indistinguishable from literals and strips dynamic bindings of their resolution environment."
    //
    //  THE PAYLOAD LAYOUT, RECORDED AS THE FOLDER REQUIREMENT ASKS: the contract does NOT split the
    //  three parts across messages joined by a session or a handle. All three are fields of ONE message,
    //  `ExpressionBinding`, so no join needs asserting and a payload that omits a part is VISIBLY
    //  incomplete rather than merely wrong. The message is reachable from the wire surface in two ways -
    //  `GetExpressionResponse.binding` for a single expression and
    //  `GetExpressionStateResponse.bindings` (repeated) for a whole DataWindow - and both are asserted
    //  below, because a perfectly-shaped message no request can obtain is unreachable capability.
    // ==============================================================================================

    /// <summary>
    /// The three parts of the payload AAP 0.6.2.2 requires, each with the field that carries it and the
    /// loss that occurs when it is absent.
    /// </summary>
    /// <remarks>
    /// One row per part. The third column is carried into the failure message so a missing part reports
    /// WHAT BREAKS rather than merely that a field was not found - which is the difference between a
    /// failure a reader can act on and one they have to research.
    /// </remarks>
    public static TheoryData<string, string> ThreePartPayload() => new()
    {
        {
            "unexpanded_exp",
            "PART (i) the UNEXPANDED source text. The legacy parse routine rewrites the expression IN "
            + "PLACE and a static reference is substituted into the rewritten text at bind time, so the "
            + "variable's name is GONE from what is stored. Without this part a static binding is "
            + "indistinguishable from a literal."
        },
        {
            "bind_time_snapshot",
            "PART (ii) the BIND-TIME SNAPSHOT - the value each STATIC reference resolved to at the moment "
            + "the expression was set. Without it the value fixed at bind time (the specification's 5, at "
            + "docs/n_cst_dwsvc_columnexp.md:L49) cannot be recovered."
        },
        {
            "live_environment",
            "PART (iii) the LIVE ENVIRONMENT - the current value of every variable a DYNAMIC reference "
            + "resolves against. A dynamic reference's index points into the live global-variable table so "
            + "mutation propagates (the specification's 6, at docs/n_cst_dwsvc_columnexp.md:L69). Without "
            + "it a dynamic binding can never be recalculated correctly again."
        },
    };

    /// <summary>
    /// Each of the three required payload parts is present as a field of the binding message.
    /// </summary>
    /// <remarks>
    /// Per-part rows so a partial contract reports exactly which part is missing and what that costs.
    /// The simultaneity of the three - the property that actually makes the payload correct - is
    /// asserted separately by
    /// <see cref="AllThreePayloadPartsAreFieldsOfOneMessageSoAnIncompletePayloadIsVisiblyIncomplete"/>.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ThreePartPayload))]
    public void EachRequiredPayloadPartIsCarriedByTheBindingMessage(string fieldName, string lossIfAbsent)
    {
        MessageDescriptor binding = ContractDescriptors.RequireMessage(ExpressionBindingMessage);

        FieldDescriptor? part = ContractDescriptors.FindField(binding, fieldName);

        Assert.True(
            part is not null,
            $"{ExpressionBindingMessage} declares no field '{fieldName}'. {lossIfAbsent} Declared fields: "
            + $"{string.Join(", ", binding.Fields.InDeclarationOrder().Select(field => field.Name))}.");
    }

    /// <summary>
    /// All three payload parts are fields of ONE message, so a payload missing a part is structurally
    /// incomplete rather than merely semantically wrong.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Simultaneity is the requirement, not mere presence.</b> Three parts spread across three
    /// messages that a consumer must correlate would satisfy a naive "does the field exist" check while
    /// still permitting every failure AAP 0.6.2.2 describes, because nothing would stop a producer
    /// sending one and omitting the others. Making them siblings is what removes that freedom.
    /// </para>
    /// <para>
    /// The folder requirement allows the alternative - three parts split across messages joined by a
    /// session or handle, with the join asserted instead. This contract does not use it, and that
    /// finding is recorded in the section banner above rather than left for a reader to infer from the
    /// absence of a join assertion.
    /// </para>
    /// </remarks>
    [Fact]
    public void AllThreePayloadPartsAreFieldsOfOneMessageSoAnIncompletePayloadIsVisiblyIncomplete()
    {
        MessageDescriptor binding = ContractDescriptors.RequireMessage(ExpressionBindingMessage);

        FieldDescriptor unexpanded = ContractDescriptors.RequireField(binding, "unexpanded_exp");
        FieldDescriptor snapshot = ContractDescriptors.RequireField(binding, "bind_time_snapshot");
        FieldDescriptor live = ContractDescriptors.RequireField(binding, "live_environment");

        // Siblings on one message: same containing type, three distinct field numbers.
        Assert.Same(binding, unexpanded.ContainingType);
        Assert.Same(binding, snapshot.ContainingType);
        Assert.Same(binding, live.ContainingType);

        Assert.Equal(
            3,
            new[] { unexpanded.FieldNumber, snapshot.FieldNumber, live.FieldNumber }.Distinct().Count());

        // NONE of the three may sit in a oneof. A oneof would make them MUTUALLY EXCLUSIVE, which is the
        // exact opposite of the requirement: the whole point is that all three travel together.
        Assert.Null(unexpanded.RealContainingOneof);
        Assert.Null(snapshot.RealContainingOneof);
        Assert.Null(live.RealContainingOneof);
    }

    /// <summary>
    /// The unexpanded source expression is carried as a singular string, distinct from any
    /// already-expanded or evaluated rendering of the same expression.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>columnexpdata.exp</c> [<c>n_cst_dwsvc_columnexp.sru</c>:L27] is the field the legacy REWRITES
    /// IN PLACE: <c>_of_parseexp(ref string exp, ref vardata vars[], ref funcdata fns[])</c> takes all
    /// three parameters by reference, and a static reference is substituted by replacing its
    /// <c>full_name</c> out of the text. After that the variable's name is no longer in the string.
    /// </para>
    /// <para>
    /// The binding therefore has to carry the expression AS AUTHORED, sigils intact and nothing
    /// substituted - which is why the field is named for the property that matters rather than simply
    /// <c>exp</c>. That naming is itself a guard: a message with both <c>exp</c> and
    /// <c>unexpanded_exp</c> would be ambiguous about which one the producer rewrote.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheUnexpandedSourceExpressionIsCarriedAsItsOwnSingularStringField()
    {
        MessageDescriptor binding = ContractDescriptors.RequireMessage(ExpressionBindingMessage);
        FieldDescriptor unexpanded = ContractDescriptors.RequireField(binding, "unexpanded_exp");

        Assert.Equal(FieldType.String, unexpanded.FieldType);
        Assert.False(unexpanded.IsRepeated);
        Assert.False(unexpanded.IsMap);

        // The binding carries the unexpanded form and no competing plain `exp` alongside it, so there is
        // exactly one answer to "which text is this".
        Assert.Null(ContractDescriptors.FindField(binding, "exp"));
    }

    /// <summary>
    /// The bind-time snapshot and the live environment are both keyed collections of the TYPED value
    /// carrier, and both are separately addressable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both are <c>map</c> fields keyed by variable name, and the value type is the seven-arm union
    /// rather than a string - the property Section 3 asserts in full and the reason the negative there
    /// bites. What matters HERE is that the two are distinct fields: a single "variables" map could not
    /// express a variable whose bind-time value was 0 and whose current value is 1, and that is exactly
    /// the specification's worked example, whose two halves differ only in which of those two values the
    /// expression sees.
    /// </para>
    /// <para>
    /// Keying by NAME rather than by index is deliberate on the contract's side and is asserted because
    /// it is load-bearing: a static reference's name has already been substituted out of the expression
    /// text, so an index into the rewritten text could not address it, and the snapshot would be
    /// unreadable.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSnapshotAndTheLiveEnvironmentAreDistinctNameKeyedMapsOfTheTypedValueCarrier()
    {
        MessageDescriptor binding = ContractDescriptors.RequireMessage(ExpressionBindingMessage);

        foreach (string environment in (string[])["bind_time_snapshot", "live_environment"])
        {
            FieldDescriptor field = ContractDescriptors.RequireField(binding, environment);

            Assert.True(field.IsMap, $"{environment} must be a map keyed by variable name.");

            // A protobuf map field is a repeated synthetic entry message with `key` and `value` fields.
            MessageDescriptor entry = field.MessageType;
            Assert.True(entry.IsMapEntry);

            FieldDescriptor key = ContractDescriptors.RequireField(entry, "key");
            FieldDescriptor value = ContractDescriptors.RequireField(entry, "value");

            Assert.Equal(FieldType.String, key.FieldType);

            // The typed carrier, NOT a string. Asserted in full in Section 3; asserted here too because
            // a reader of this test should not have to trust a different section for the one property
            // that decides whether the snapshot is usable.
            Assert.Equal(FieldType.Message, value.FieldType);
            Assert.Equal(VarValueMessage, value.MessageType.Name);
        }

        // Two distinct fields, not one shared map: the same variable must be able to hold a bind-time
        // value and a different current value simultaneously.
        Assert.NotEqual(
            ContractDescriptors.RequireField(binding, "bind_time_snapshot").FieldNumber,
            ContractDescriptors.RequireField(binding, "live_environment").FieldNumber);
    }

    /// <summary>
    /// The binding carries the parser's reference arrays as repeated fields of the reference message
    /// types, so every reference arrives with its own expansion mode attached.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are <c>columnexpdata.vars[]</c> [:L28] and <c>columnexpdata.fns[]</c> [:L29], the two
    /// arrays <c>_of_parseexp</c> emits by reference alongside the rewritten text. Carrying them as
    /// <c>repeated VarData</c> rather than as, say, repeated strings is what makes the per-reference mode
    /// reachable: the mode lives on <c>VarData</c>, so a payload carrying reference NAMES would carry no
    /// modes at all and Section 1's design would be unreachable in practice.
    /// </para>
    /// <para>
    /// <c>fns</c> is the FUNCTION reference array and is asserted for the same reason. Its element type
    /// records a legacy subtlety worth stating: an EMPTY function name is meaningful rather than missing,
    /// because <c>FUNC_VAR = ""</c> [<c>n_cst_dwsvc_columnexp.sru</c>:L120] models a bare variable as a
    /// function entry with no name.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheBindingCarriesTheParserReferenceArraysAsRepeatedTypedReferences()
    {
        MessageDescriptor binding = ContractDescriptors.RequireMessage(ExpressionBindingMessage);

        FieldDescriptor variableReferences = ContractDescriptors.RequireField(binding, "vars");
        Assert.True(variableReferences.IsRepeated, "`vardata vars[]` [:L28] is an array.");
        Assert.Equal(FieldType.Message, variableReferences.FieldType);
        Assert.Equal(VarDataMessage, variableReferences.MessageType.Name);

        FieldDescriptor functionReferences = ContractDescriptors.RequireField(binding, "fns");
        Assert.True(functionReferences.IsRepeated, "`funcdata fns[]` [:L29] is an array.");
        Assert.Equal(FieldType.Message, functionReferences.FieldType);
        Assert.Equal(FuncDataMessage, functionReferences.MessageType.Name);

        // The mode is reachable from the payload BECAUSE the elements are VarData. This is the join
        // between Section 1 and Section 2, and without it each section would be individually satisfiable
        // while the contract as a whole still lost the mode.
        Assert.NotNull(
            ContractDescriptors.FindField(variableReferences.MessageType, ExpansionModeField));
    }

    /// <summary>
    /// The live variable table itself - the <c>globalvardata</c> counterpart - is reachable from the
    /// binding, so a dynamic reference's index has something to resolve through.
    /// </summary>
    /// <remarks>
    /// A dynamic reference is retained AS A REFERENCE whose <c>index</c> points into the global variable
    /// table, and mutation of the table is what makes the specification's second answer 6 rather than 5.
    /// The map in <c>live_environment</c> carries current VALUES by name; this repeated field carries the
    /// TABLE ENTRIES, which is strictly more - each entry knows whether it is local or foreign, and a
    /// local entry can itself be an expression. Both are needed: the map answers "what is this variable
    /// worth now", the table answers "what kind of variable is it and where does it come from".
    /// </remarks>
    [Fact]
    public void TheLiveGlobalVariableTableIsReachableFromTheBinding()
    {
        MessageDescriptor binding = ContractDescriptors.RequireMessage(ExpressionBindingMessage);
        FieldDescriptor table = ContractDescriptors.RequireField(binding, "global_vars");

        Assert.True(table.IsRepeated, "`globalvardata` is a table, so the field is repeated.");
        Assert.Equal(FieldType.Message, table.FieldType);
        Assert.Equal(GlobalVarDataMessage, table.MessageType.Name);
    }

    /// <summary>
    /// The binding is actually reachable over the wire - a request can obtain it for one expression and
    /// for a whole DataWindow.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A correctly-shaped message that no method returns is unreachable capability, and it would let the
    /// whole of Section 2 pass while a consumer had no way to get the three parts. So the shape
    /// assertions above are completed by this reachability assertion.
    /// </para>
    /// <para>
    /// Two routes are asserted because the legacy has two granularities: single-expression access
    /// (<c>of_getexp</c>) and whole-service state. The repeated form on the state response is what makes
    /// a DataWindow's entire expression set retrievable in one call.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheBindingIsReachableFromTheWireSurfaceForOneExpressionAndForAWholeDataWindow()
    {
        MessageDescriptor binding = ContractDescriptors.RequireMessage(ExpressionBindingMessage);

        // Route 1 - one expression.
        MessageDescriptor expressionResponse = ContractDescriptors.RequireMessage("GetExpressionResponse");
        FieldDescriptor single = ContractDescriptors.RequireField(expressionResponse, "binding");

        Assert.Equal(FieldType.Message, single.FieldType);
        Assert.Same(binding, single.MessageType);
        Assert.False(single.IsRepeated);

        // Route 2 - every expression of one DataWindow.
        MessageDescriptor stateResponse = ContractDescriptors.RequireMessage("GetExpressionStateResponse");
        FieldDescriptor many = ContractDescriptors.RequireField(stateResponse, "bindings");

        Assert.Equal(FieldType.Message, many.FieldType);
        Assert.Same(binding, many.MessageType);
        Assert.True(many.IsRepeated);
    }

    // ==============================================================================================
    //  SECTION 3 - THE VARIABLE ENVIRONMENT IS A DISCRIMINATED UNION OVER SEVEN TYPES
    //
    //  AAP 0.6.2.4: "Because the variable environment is strongly typed across seven distinct scalar
    //  types, its wire representation must be a DISCRIMINATED UNION, not a stringly-typed map.
    //  Collapsing it to strings would lose the type information the coercion behaviour depends on."
    //
    //  THE SEVEN ARE A CLOSED SET READ OFF THE LEGACY OVERLOADS, NOT A DESIGN CHOICE. The legacy
    //  declares exactly seven `of_addvar` overloads [n_cst_dwsvc_columnexp.sru:L153-L159] and exactly
    //  seven matching `of_setvar` overloads [:L160-L166], in this order:
    //
    //      time [:L153/:L160]      string [:L154/:L161]    long [:L155/:L162]
    //      double [:L156/:L163]    datetime [:L157/:L164]   date [:L158/:L165]
    //      boolean [:L159/:L166]
    //
    //  So a caller CANNOT declare a variable of any other type. Seven arms is therefore faithful; six
    //  loses a type a caller can legitimately declare, and eight invents capability the legacy has no
    //  overload for and whose coercion path would have nothing to dispatch on. Both directions breach
    //  constraint C-B, which is why the count is pinned rather than bounded.
    //
    //  THE WIRE TYPE OF EACH ARM IS DELIBERATELY NOT DICTATED HERE. `time`, `date` and `datetime` have
    //  no protobuf scalar, so a contract may reasonably carry them as a string, a well-known type or a
    //  dedicated message. What this section asserts is SEVEN DISTINCT ARMS, ONE PER LEGACY TYPE - the
    //  property the coercion depends on - and it records the wire type each arm actually uses rather
    //  than requiring a particular one.
    // ==============================================================================================

    /// <summary>
    /// The seven legacy scalar types of the variable environment, each with the arm that carries it and
    /// the <c>of_addvar</c>/<c>of_setvar</c> overload pair that proves it belongs to the set.
    /// </summary>
    /// <remarks>
    /// In the source's own declaration order, which is neither alphabetical nor grouped by kind - it is
    /// reproduced as declared so a reader can diff this table against the oracle line by line.
    /// </remarks>
    public static TheoryData<string, string> SevenScalarArms() => new()
    {
        { "time_value", "legacy `time`     - of_addvar [:L153] / of_setvar [:L160]" },
        { "string_value", "legacy `string`   - of_addvar [:L154] / of_setvar [:L161]" },
        { "long_value", "legacy `long`     - of_addvar [:L155] / of_setvar [:L162]" },
        { "double_value", "legacy `double`   - of_addvar [:L156] / of_setvar [:L163]" },
        { "datetime_value", "legacy `datetime` - of_addvar [:L157] / of_setvar [:L164]" },
        { "date_value", "legacy `date`     - of_addvar [:L158] / of_setvar [:L165]" },
        { "bool_value", "legacy `boolean`  - of_addvar [:L159] / of_setvar [:L166]" },
    };

    /// <summary>
    /// Each of the seven legacy scalar types has its own arm in the variable-value union.
    /// </summary>
    /// <remarks>
    /// One row per arm, each citing the overload pair that puts the type in the set (constraint C-K). The
    /// arm is required to be a MEMBER OF THE UNION rather than merely a field of the message: a field
    /// outside the oneof would be settable alongside another arm, and a value that claimed to be both a
    /// <c>date</c> and a <c>long</c> is precisely the ambiguity a discriminated union exists to prevent.
    /// </remarks>
    [Theory]
    [MemberData(nameof(SevenScalarArms))]
    public void EachOfTheSevenLegacyScalarTypesHasItsOwnArmInTheVariableValueUnion(
        string armName,
        string oracleCitation)
    {
        MessageDescriptor value = ContractDescriptors.RequireMessage(VarValueMessage);

        FieldDescriptor? arm = ContractDescriptors.FindField(value, armName);

        Assert.True(
            arm is not null,
            $"{VarValueMessage} declares no arm '{armName}', so a variable of the {oracleCitation} could "
            + "not be carried and its coercion behaviour would be unreachable. Declared arms: "
            + $"{string.Join(", ", value.Fields.InDeclarationOrder().Select(field => field.Name))}.");

        Assert.NotNull(arm.RealContainingOneof);
        Assert.False(arm.IsRepeated, "a variable holds one value, so an arm is singular.");
        Assert.False(arm.IsMap);
    }

    /// <summary>
    /// The variable-value carrier is a protobuf <c>oneof</c> with exactly seven arms - the discriminated
    /// union the coercion behaviour depends on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The count is pinned, not bounded.</b> Six would drop a type the legacy can declare; eight would
    /// invent one it cannot. The per-arm theory above proves the seven are present; only this assertion
    /// can prove no eighth was added.
    /// </para>
    /// <para>
    /// <b>The oneof must be authored, not synthetic.</b> proto3 <c>optional</c> makes <c>protoc</c>
    /// synthesise a single-field oneof named for the field with a leading underscore. A message with
    /// seven <c>optional</c> scalars would therefore report seven oneofs and look union-shaped to a naive
    /// count of <see cref="MessageDescriptor.Oneofs"/>, while permitting all seven to be set at once -
    /// which is not a union at all. Filtering on <see cref="OneofDescriptor.IsSynthetic"/> is what
    /// distinguishes the two, so it is done explicitly rather than incidentally.
    /// </para>
    /// <para>
    /// The wire type each arm uses is recorded here as a matter of fact rather than required, per the
    /// section banner: <c>time</c>, <c>date</c> and <c>datetime</c> have no protobuf scalar and so are
    /// carried as messages, while the other four map to scalars directly.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheVariableValueCarrierIsAOneofWithExactlySevenArmsAndTheOneofIsAuthored()
    {
        MessageDescriptor value = ContractDescriptors.RequireMessage(VarValueMessage);

        OneofDescriptor union = Assert.Single(value.Oneofs, oneof => !oneof.IsSynthetic);

        Assert.Equal(7, union.Fields.Count);

        // Every field of the message belongs to the union: a scalar sitting OUTSIDE it could be set
        // alongside an arm and would reintroduce exactly the "both a date and a long" ambiguity.
        Assert.Equal(7, value.Fields.InDeclarationOrder().Count);
        Assert.All(
            value.Fields.InDeclarationOrder(),
            field => Assert.Same(union, field.RealContainingOneof));

        // Seven distinct field numbers, so no arm aliases another.
        Assert.Equal(7, union.Fields.Select(field => field.FieldNumber).Distinct().Count());
    }

    /// <summary>
    /// The variable environment is not a stringly-typed map - the negative that gives the
    /// discriminated-union requirement its teeth.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this is the assertion that matters.</b> A contract could satisfy every positive assertion
    /// in this section by declaring <c>VarValue</c> correctly and then never using it, carrying the
    /// environment as <c>map&lt;string, string&gt;</c> instead. The result would serialize, round-trip
    /// and look entirely healthy while having thrown away the type information the coercion dispatches
    /// on. AAP 0.6.2.4: collapsing to strings "would lose the type information the coercion behaviour
    /// depends on", and an expression that adds a <c>long</c> variable to a <c>date</c> variable behaves
    /// differently from one concatenating their string renderings - a loss invisible until a result is
    /// subtly wrong, which is the worst failure mode for a parity migration.
    /// </para>
    /// <para>
    /// Two distinct wrong shapes are therefore excluded. First, a map whose VALUE type is a string:
    /// asserted in descriptor terms by walking every map field of the contract and requiring no
    /// string-valued one to be serving as a variable environment. Second, a single string field carrying
    /// the value for all seven types: asserted by requiring that no field of the union is a bare string
    /// standing alone, and that the environment maps resolve to the union rather than to a scalar.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheVariableEnvironmentIsNeverAStringlyTypedMapNorASingleUntypedStringField()
    {
        MessageDescriptor binding = ContractDescriptors.RequireMessage(ExpressionBindingMessage);

        // ---- Wrong shape 1: a string-valued map standing in for the environment. ------------------
        foreach (string environment in (string[])["bind_time_snapshot", "live_environment"])
        {
            FieldDescriptor field = ContractDescriptors.RequireField(binding, environment);
            FieldDescriptor mapValue = ContractDescriptors.RequireField(field.MessageType, "value");

            Assert.False(
                mapValue.FieldType == FieldType.String,
                $"{ExpressionBindingMessage}.{environment} is a map<string, string>, which collapses the "
                + "seven-type variable environment into text and loses the type information the coercion "
                + "behaviour depends on (AAP 0.6.2.4). Its value type must be the "
                + $"{VarValueMessage} union.");

            Assert.Equal(VarValueMessage, mapValue.MessageType.Name);
        }

        // ---- The same negative, swept across the whole contract. ------------------------------------
        // Any map anywhere in the published boundary whose key is a string and whose value is a string is
        // a candidate stringly-typed environment. There are none, and that is asserted rather than
        // assumed, so a later addition cannot slip one in beside the correct fields above.
        List<string> stringToStringMaps =
        [
            .. from message in ContractDescriptors.AllMessages()
               from field in message.Fields.InDeclarationOrder()
               where field.IsMap
               let key = ContractDescriptors.FindField(field.MessageType, "key")
               let mapValue = ContractDescriptors.FindField(field.MessageType, "value")
               where key is not null
                     && mapValue is not null
                     && key.FieldType == FieldType.String
                     && mapValue.FieldType == FieldType.String
               select $"{message.FullName}.{field.Name}",
        ];

        Assert.True(
            stringToStringMaps.Count == 0,
            "the published boundary declares map<string, string> field(s) - "
            + $"{string.Join(", ", stringToStringMaps)} - any of which could be serving as a "
            + "stringly-typed variable environment. A typed union is required (AAP 0.6.2.4).");

        // ---- Wrong shape 2: one untyped string carrying every type's value. -------------------------
        // The union has exactly ONE string arm, for the legacy `string` type [:L154]. A second would mean
        // some other legacy type was being carried as text.
        MessageDescriptor value = ContractDescriptors.RequireMessage(VarValueMessage);

        FieldDescriptor onlyStringArm = Assert.Single(
            value.Fields.InDeclarationOrder(),
            field => field.FieldType == FieldType.String);

        Assert.Equal("string_value", onlyStringArm.Name);
    }

    /// <summary>
    /// The global variable table entry carries the local-versus-foreign discriminator with the legacy's
    /// own numbering, <c>VAR_LOCAL</c> = 0 and <c>VAR_FOREIGN</c> = 1.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>globalvardata</c> declares <c>long vartype</c> [<c>n_cst_dwsvc_columnexp.sru</c>:L67] and the
    /// two constants <c>VAR_LOCAL = 0</c> [:L117] and <c>VAR_FOREIGN = 1</c> [:L118]. The NUMBERS are
    /// asserted, not just the names, because AAP 0.4.5.3 requires the legacy constants preserved verbatim
    /// - these values appear in serialized payloads, log records and characterization recordings, so a
    /// renumbering would silently invalidate every stored comparison while every test about names still
    /// passed.
    /// </para>
    /// <para>
    /// <c>VAR_LOCAL</c> occupying zero is a happy coincidence rather than a proto3 concession: the legacy
    /// genuinely numbers it zero, and local is genuinely the default kind, so the sentinel slot and the
    /// legacy value agree without either being bent to fit.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheGlobalVariableEntryCarriesTheLocalForeignDiscriminatorWithTheLegacyNumbering()
    {
        MessageDescriptor entry = ContractDescriptors.RequireMessage(GlobalVarDataMessage);
        FieldDescriptor discriminator = ContractDescriptors.RequireField(entry, "var_type");

        Assert.Equal(FieldType.Enum, discriminator.FieldType);

        EnumDescriptor varType = discriminator.EnumType;

        EnumValueDescriptor local = Assert.Single(
            varType.Values,
            enumValue => string.Equals(enumValue.Name, "VAR_LOCAL", StringComparison.Ordinal));
        EnumValueDescriptor foreignVariable = Assert.Single(
            varType.Values,
            enumValue => string.Equals(enumValue.Name, "VAR_FOREIGN", StringComparison.Ordinal));

        Assert.Equal(0, local.Number);
        Assert.Equal(1, foreignVariable.Number);

        // Two kinds and no third: the legacy declares exactly these two constants.
        Assert.Equal(2, varType.Values.Count);

        // Both arms are carried, and they are distinct messages - a local variable's value may be an
        // expression, a foreign one is a reference into another DataWindow's service.
        FieldDescriptor localArm = ContractDescriptors.RequireField(entry, "local");
        FieldDescriptor foreignArm = ContractDescriptors.RequireField(entry, "foreign");

        Assert.Equal(LocalVarDataMessage, localArm.MessageType.Name);
        Assert.Equal(ForeignVarRefMessage, foreignArm.MessageType.Name);
    }

    /// <summary>
    /// The local arm is recursive - a variable's value may itself be an expression carrying its own
    /// variable and function references.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>localvardata</c> [<c>n_cst_dwsvc_columnexp.sru</c>:L73-L78] declares <c>string exp</c>,
    /// <c>vardata vars[]</c>, <c>funcdata fns[]</c> and <c>boolean hasmacro</c> - the same shape as an
    /// expression. That is what makes the model recursive: a reference resolves to a table entry, whose
    /// local arm holds an expression, whose references resolve to table entries, and so on. The legacy
    /// specification exercises it directly with
    /// <c>of_AddVarExp("num1", "$$上月读数 + $本月读数")</c> [docs/n_cst_dwsvc_columnexp.md:L68] - a
    /// variable whose value is an expression over two further variables.
    /// </para>
    /// <para>
    /// A contract that carried a local variable as a bare value would flatten this, and the flattening
    /// would only become visible when a variable-valued-as-expression failed to recalculate - a silent
    /// wrong answer rather than an error. Asserting the reference collections here is what keeps the
    /// recursion representable.
    /// </para>
    /// <para>
    /// The recursion closes through <see cref="VarDataMessage"/>'s index back into the table rather than
    /// by <c>LocalVarData</c> containing itself, which is exactly how the legacy does it and is why the
    /// assertion looks for reference COLLECTIONS rather than for a self-referential field.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheLocalVariableArmIsRecursiveCarryingExpressionTextAndItsOwnReferenceCollections()
    {
        MessageDescriptor local = ContractDescriptors.RequireMessage(LocalVarDataMessage);

        // `string exp` [:L74] - the variable's value IS an expression.
        Assert.Equal(FieldType.String, ContractDescriptors.RequireField(local, "exp").FieldType);

        // `vardata vars[]` [:L75] - its own variable references, each with its own expansion mode.
        FieldDescriptor nestedVars = ContractDescriptors.RequireField(local, "vars");
        Assert.True(nestedVars.IsRepeated);
        Assert.Equal(VarDataMessage, nestedVars.MessageType.Name);

        // `funcdata fns[]` [:L76] - its own function references.
        FieldDescriptor nestedFns = ContractDescriptors.RequireField(local, "fns");
        Assert.True(nestedFns.IsRepeated);
        Assert.Equal(FuncDataMessage, nestedFns.MessageType.Name);

        // `boolean hasmacro` [:L77].
        Assert.Equal(FieldType.Bool, ContractDescriptors.RequireField(local, "has_macro").FieldType);

        // The recursion is real: the nested references carry modes, so a nested static reference is
        // distinguishable from a nested dynamic one at any depth.
        Assert.NotNull(ContractDescriptors.FindField(nestedVars.MessageType, ExpansionModeField));

        // And a local variable can also hold a plain typed value, so the arm covers both the
        // `of_AddVar` and the `of_AddVarExp` cases rather than only one.
        FieldDescriptor plainValue = ContractDescriptors.RequireField(local, "value");
        Assert.Equal(VarValueMessage, plainValue.MessageType.Name);
    }

    /// <summary>
    /// The two grammar sentinels are carried as fields of the contract, so their VALUES are part of the
    /// published boundary rather than a convention each consumer must rediscover.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What was found, stated plainly, because the folder requirement asks for exactly that and
    /// forbids fabricating a mechanism.</b> <c>FUNC_VAR = ""</c>
    /// [<c>n_cst_dwsvc_columnexp.sru</c>:L120] and <c>FUNC_INVOKE = "Invoke"</c> [:L121] are legacy
    /// STRING CONSTANTS, not enum members, and the legacy matches them BY VALUE at run time. The contract
    /// carries them as string FIELDS of an <c>ExpressionSentinels</c> message, transmitted with the
    /// service state. So the mechanism the contract exposes is a carrier, and that is what is asserted:
    /// the fields exist and are strings.
    /// </para>
    /// <para>
    /// <b>The limitation, not glossed over.</b> The VALUES <c>""</c> and <c>"Invoke"</c> are run-time
    /// payload contents, not schema elements. A descriptor test cannot assert them - and proto leading
    /// comments, where the contract does record them, are not retained in the runtime descriptor graph
    /// (see the class remarks). Asserting the carrier is therefore the strongest available descriptor-level
    /// assertion, and the requirement is discharged by asserting it rather than dropped: the values are
    /// verified where they can be, in the service implementation's own tests and in the
    /// <c>.proto</c> text at <c>ExpressionSentinels</c>.
    /// </para>
    /// <para>
    /// An empty <c>FuncData.name</c> is consequently MEANINGFUL RATHER THAN MISSING - it models a bare
    /// variable reference recorded as a function entry with no name - so a consumer must not treat it as
    /// an incomplete message. That too is a value-level convention, recorded here for the reader.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheTwoGrammarSentinelsAreCarriedAsContractFieldsRatherThanCommentedConstants()
    {
        MessageDescriptor sentinels = ContractDescriptors.RequireMessage(ExpressionSentinelsMessage);

        // FUNC_VAR [:L120] - an empty function name models a bare variable.
        FieldDescriptor funcVar = ContractDescriptors.RequireField(sentinels, "func_var");
        Assert.Equal(FieldType.String, funcVar.FieldType);
        Assert.False(funcVar.IsRepeated);

        // FUNC_INVOKE [:L121] - the reserved name that selects dynamic macro dispatch, and the sentinel
        // EXPANSION_MODE_MACRO_DYNAMIC is defined in terms of.
        FieldDescriptor funcInvoke = ContractDescriptors.RequireField(sentinels, "func_invoke");
        Assert.Equal(FieldType.String, funcInvoke.FieldType);
        Assert.False(funcInvoke.IsRepeated);

        // The expansion sigil `$` (MACRO_FLAG [:L1252]) and the context sigil `@` (MACRO_CONTEXT
        // [:L1253]) are carried by the same message, which is what lets a consumer parse `full_name`
        // without hardcoding the grammar.
        Assert.Equal(FieldType.String, ContractDescriptors.RequireField(sentinels, "macro_flag").FieldType);
        Assert.Equal(FieldType.String, ContractDescriptors.RequireField(sentinels, "macro_context").FieldType);

        // Reachable over the wire, so the sentinels are obtainable rather than merely declared.
        MessageDescriptor stateResponse = ContractDescriptors.RequireMessage("GetExpressionStateResponse");
        Assert.Same(sentinels, ContractDescriptors.RequireField(stateResponse, "sentinels").MessageType);
    }

    /// <summary>
    /// The legacy model is carried unsimplified: the duplicate-expression mechanism, the tri-state
    /// calculation cache and the reverse dependency index all survive (constraint C-B).
    /// </summary>
    /// <remarks>
    /// <para>
    /// These three are the parts of the model most tempting to tidy away, and the folder requirement
    /// names them for that reason.
    /// </para>
    /// <para>
    /// <c>dupexps[]</c> [:L32] is the mechanism that allows MULTIPLE EXPRESSIONS PER COLUMN, selected by
    /// which column changed. A contract permitting one expression per column would look cleaner and would
    /// silently drop that capability, so the field is asserted to be repeated. Its two neighbours in the
    /// structure are the relative-column arrays, <c>relativecolids[]</c> [:L30] and
    /// <c>relativeinputcolids[]</c> [:L31], which are the inputs that trigger recalculation.
    /// </para>
    /// <para>
    /// The calculation cache is TRI-STATE - <c>CLC_UNKNOWN</c> = 0 [:L113], <c>CLC_YES</c> = 1 [:L114],
    /// <c>CLC_NO</c> = 2 [:L115]. "Unknown" is not a synonym for "no": it means the answer has not been
    /// computed yet, and collapsing the three into a boolean would turn "not yet decided" into "decided
    /// against". The numbers are asserted for the same reason as the variable-type discriminator's.
    /// </para>
    /// <para>
    /// <c>columndata.indexes[]</c> and <c>varindexes[]</c> [:L46-L47] are the REVERSE dependency index -
    /// from a column to the expressions and variables depending on it - which is the graph that makes
    /// dirty propagation work. Both are repeated.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheLegacyModelIsCarriedUnsimplifiedIncludingDuplicateExpressionsAndTheTriStateCache()
    {
        // ---- dupexps[] : multiple expressions per column, selected by which column changed. ---------
        MessageDescriptor expression = ContractDescriptors.RequireMessage(ColumnExpDataMessage);
        FieldDescriptor duplicates = ContractDescriptors.RequireField(expression, "dup_exps");

        Assert.True(
            duplicates.IsRepeated,
            "`dupexps[]` [:L32] allows MULTIPLE expressions per column; a singular field would silently "
            + "drop that capability (constraint C-B).");

        // `relativecolids[]` [:L30] and `relativeinputcolids[]` [:L31] - the inputs that trigger
        // recalculation. Both are arrays, and the two are DISTINCT: input columns are the subset the user
        // can edit, so collapsing them into one would recalculate on changes the legacy ignores.
        Assert.True(ContractDescriptors.RequireField(expression, "relative_col_ids").IsRepeated);
        Assert.True(ContractDescriptors.RequireField(expression, "relative_input_col_ids").IsRepeated);
        Assert.NotEqual(
            ContractDescriptors.RequireField(expression, "relative_col_ids").FieldNumber,
            ContractDescriptors.RequireField(expression, "relative_input_col_ids").FieldNumber);

        // The 19 fields of `columnexpdata` [:L22-L42] are carried in full - no member dropped as
        // redundant.
        Assert.Equal(19, expression.Fields.InDeclarationOrder().Count);

        // ---- The tri-state calculation cache, with the legacy numbering. ----------------------------
        MessageDescriptor columnData = ContractDescriptors.RequireMessage(ColumnDataMessage);
        FieldDescriptor flag = ContractDescriptors.RequireField(columnData, "flag");

        Assert.Equal(FieldType.Enum, flag.FieldType);

        EnumDescriptor calcFlag = flag.EnumType;

        Assert.Equal(3, calcFlag.Values.Count);
        Assert.Equal(
            0,
            Assert.Single(calcFlag.Values, value => value.Name == "CLC_UNKNOWN").Number);
        Assert.Equal(
            1,
            Assert.Single(calcFlag.Values, value => value.Name == "CLC_YES").Number);
        Assert.Equal(
            2,
            Assert.Single(calcFlag.Values, value => value.Name == "CLC_NO").Number);

        // ---- The reverse dependency index. ----------------------------------------------------------
        Assert.True(
            ContractDescriptors.RequireField(columnData, "indexes").IsRepeated,
            "`indexes[]` [:L46] maps a column to the expressions depending on it.");
        Assert.True(
            ContractDescriptors.RequireField(columnData, "var_indexes").IsRepeated,
            "`varindexes[]` [:L47] maps a column to the variables depending on it.");
    }

    /// <summary>
    /// The item-calculation entry point is exposed on the service, and its boolean result is preserved -
    /// including the legacy's own naming anomaly, which is carried rather than tidied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>_of_calcitem</c> is declared <c>public function boolean</c> at
    /// <c>n_cst_dwsvc_columnexp.sru</c>:L142 DESPITE its leading underscore, which is the framework's own
    /// private-member convention throughout the rest of that same prototype block. It is genuinely part
    /// of the public surface, so it appears on the contract; the anomaly is the SPELLING, and constraint
    /// C-B says carry it rather than correct it.
    /// </para>
    /// <para>
    /// It is also the one calculation entry point returning a BOOLEAN rather than the framework's
    /// <c>long</c> return code, so its response carries a boolean alongside the return code rather than
    /// folding one into the other. Preserving that is what stops a consumer inferring success from a
    /// zero return code where the legacy would have said false.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheItemCalculationEntryPointIsExposedAndKeepsItsBooleanResult()
    {
        ServiceDescriptor service = ContractDescriptors.RequireService(ColumnExpressionServiceName);
        MethodDescriptor calcItem = ContractDescriptors.RequireMethod(service, "CalcItem");

        // A unary call: one item, one answer.
        Assert.False(calcItem.IsClientStreaming);
        Assert.False(calcItem.IsServerStreaming);

        // `public function boolean _of_calcitem` [:L142] - the boolean survives as its own field.
        FieldDescriptor succeeded = ContractDescriptors.RequireField(calcItem.OutputType, "succeeded");
        Assert.Equal(FieldType.Bool, succeeded.FieldType);

        // And the framework return code is carried too, so the two are not conflated.
        Assert.NotNull(ContractDescriptors.FindField(calcItem.OutputType, "ret_code"));
    }

    // ==============================================================================================
    //  SECTION 4 - THE ONE HARD LIMIT: A LIVE OBJECT POINTER CANNOT CROSS A WIRE
    //
    //  The legacy `foreignvardata` structure [n_cst_dwsvc_columnexp.sru:L80-L83] has exactly two members:
    //
    //      integer                 index
    //      n_cst_dwsvc_columnexp   expsvc     <-- A LIVE POINTER to ANOTHER DataWindow's expression
    //                                             service. Its type is the enclosing class itself.
    //
    //  AAP 0.6.2.3 states the consequence and the resolution: `foreignvardata.expsvc` is an in-process
    //  object pointer and CANNOT BE SERIALIZED, so cross-DataWindow variables are resolved by a
    //  SESSION-SCOPED HANDLE and are supported ONLY when both DataWindows are co-resident in one
    //  expression session inside one DataServices instance. A reference spanning sessions or service
    //  instances is BLOCKED AND RETURNS A DEFINED ERROR.
    //
    //  WHY A DEFINED ERROR RATHER THAN A BEST EFFORT, in the AAP's own terms: approximating a pointer
    //  dereference across a network "would produce results that are wrong in a way no test would
    //  obviously catch" - the expression would evaluate, return a number of the right type in the right
    //  range, and be silently stale. This is a GENUINE, UNAVOIDABLE NARROWING of the legacy contract,
    //  recorded before implementation rather than discovered during it, and the only place in this
    //  contract where the narrowing principle is applied.
    //
    //  The narrowing also explains the RENAME. The legacy name ends in "data"; the contract's ends in
    //  "Ref" - because the member that made it data was the pointer, and what remains is a reference.
    // ==============================================================================================

    /// <summary>
    /// The foreign-variable reference carries no field that could purport to serialize a live object
    /// pointer - every one of its fields is a scalar.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Asserted structurally rather than by name-blacklist.</b> A test forbidding fields called
    /// "pointer" or "expsvc" would be trivially defeated by any other spelling. The load-bearing property
    /// is that nothing here is a MESSAGE reference into the expression-service model: a scalar cannot
    /// smuggle object identity, whereas a nested message could carry an address, a process id or a
    /// service endpoint and would recreate the cross-instance reference this narrowing exists to forbid.
    /// So the assertion is that the message has no message-typed and no group-typed field at all.
    /// </para>
    /// <para>
    /// The name-based check is kept as well, but as a SECOND and weaker line: it catches the specific
    /// mistake of reintroducing the legacy member verbatim, which is the most likely way for the pointer
    /// to come back.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheForeignReferenceCarriesNoFieldThatCouldSerializeALiveObjectPointer()
    {
        MessageDescriptor foreignReference = ContractDescriptors.RequireMessage(ForeignVarRefMessage);

        List<string> nonScalarFields =
        [
            .. foreignReference.Fields.InDeclarationOrder()
                .Where(field => field.FieldType is FieldType.Message or FieldType.Group)
                .Select(field => $"{field.Name} ({field.FieldType})"),
        ];

        Assert.True(
            nonScalarFields.Count == 0,
            $"{ForeignVarRefMessage} declares message-typed field(s) - {string.Join(", ", nonScalarFields)} "
            + "- which could carry object identity across the boundary. `foreignvardata.expsvc` "
            + "[n_cst_dwsvc_columnexp.sru:L82] is an IN-PROCESS POINTER and cannot be serialized "
            + "(AAP 0.6.2.3); only a session-scoped handle may stand in for it.");

        // Second line of defence: the legacy member itself must not reappear under its own name.
        Assert.Null(ContractDescriptors.FindField(foreignReference, "expsvc"));
        Assert.Null(ContractDescriptors.FindField(foreignReference, "exp_svc"));
    }

    /// <summary>
    /// The foreign-variable reference carries the legacy index plus a session-scoped handle, which is the
    /// documented replacement for the pointer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>index</c> survives verbatim from <c>foreignvardata.index</c>
    /// [<c>n_cst_dwsvc_columnexp.sru</c>:L81] - the one-based index of the variable WITHIN THE FOREIGN
    /// SERVICE's table, which is meaningless without knowing which service, and that is exactly what the
    /// handle supplies.
    /// </para>
    /// <para>
    /// The handle is a STRING and deliberately opaque. The contract's own text is explicit that it is not
    /// a network address, not a URL and not a process identifier, because making it any of those would
    /// recreate the cross-instance reference the narrowing forbids. That intent is recorded in a proto
    /// comment and so is not descriptor-assertable (see the class remarks); what IS assertable, and is
    /// asserted, is that the handle exists, is a singular string, and sits beside a
    /// <c>resolvable</c> flag - the flag being the wire-visible signal that a handle cannot be resolved
    /// in the receiving session, which is the state that must produce the blocked error rather than a
    /// substituted value.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheForeignReferenceCarriesTheLegacyIndexAndASessionScopedHandleInstead()
    {
        MessageDescriptor foreignReference = ContractDescriptors.RequireMessage(ForeignVarRefMessage);

        // `integer index` [:L81] - one-based, and relative to the FOREIGN service's table.
        FieldDescriptor index = ContractDescriptors.RequireField(foreignReference, "index");
        Assert.Equal(FieldType.Int32, index.FieldType);
        Assert.False(index.IsRepeated);

        // The replacement for `expsvc`: an opaque, session-scoped handle.
        FieldDescriptor handle = ContractDescriptors.RequireField(
            foreignReference,
            "foreign_datawindow_handle");
        Assert.Equal(FieldType.String, handle.FieldType);
        Assert.False(handle.IsRepeated);

        // The wire-visible "this handle cannot be resolved here" signal.
        FieldDescriptor resolvable = ContractDescriptors.RequireField(foreignReference, "resolvable");
        Assert.Equal(FieldType.Bool, resolvable.FieldType);

        // Exactly these three, so nothing else is riding along.
        Assert.Equal(3, foreignReference.Fields.InDeclarationOrder().Count);
    }

    /// <summary>
    /// The defined error for a blocked cross-session foreign reference is reachable on the contract.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Found, not a gap.</b> The contract declares
    /// <c>ExpressionError.Category.CATEGORY_FOREIGN_REFERENCE_BLOCKED</c>, a dedicated category rather
    /// than a reused generic failure. That distinction is the whole point: a caller has to be able to tell
    /// "this reference is unreachable in this session" apart from "this expression is malformed", because
    /// the first is a topology problem it may be able to fix by co-locating the DataWindows and the second
    /// is not.
    /// </para>
    /// <para>
    /// Declaring the category is necessary but not sufficient, so REACHABILITY is asserted too: the
    /// foreign-variable response must be able to carry an <c>ExpressionError</c>, otherwise the category
    /// would exist with no route to a caller. AAP 0.6.2.3 requires the blocked reference to return a
    /// defined error and to be resolved to "a substituted value, a default, an empty string, or a stale
    /// reading" in no circumstances.
    /// </para>
    /// <para>
    /// A note on the response's shape, because it is evidence rather than noise:
    /// <c>AddForeignVariableResponse</c> RESERVES field number 1 and the name <c>index</c>. A reserved
    /// field is a deliberate, permanent statement that the slot must never be reused - the contract
    /// recording that this response does not hand back an index. That is the correct way to retire a field
    /// and is not something to tidy away.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheBlockedCrossSessionForeignReferenceHasItsOwnDefinedErrorCategory()
    {
        MessageDescriptor expressionError = ContractDescriptors.RequireMessage(ExpressionErrorMessage);
        FieldDescriptor category = ContractDescriptors.RequireField(expressionError, "category");

        Assert.Equal(FieldType.Enum, category.FieldType);

        EnumValueDescriptor blocked = Assert.Single(
            category.EnumType.Values,
            value => string.Equals(
                value.Name,
                "CATEGORY_FOREIGN_REFERENCE_BLOCKED",
                StringComparison.Ordinal));

        // A dedicated category, distinct from the zero sentinel, so it can never be the accidental
        // default of an unset field.
        Assert.NotEqual(0, blocked.Number);

        // Reachable: the foreign-variable response carries the error type that carries the category.
        ServiceDescriptor service = ContractDescriptors.RequireService(ColumnExpressionServiceName);
        MethodDescriptor addForeign = ContractDescriptors.RequireMethod(service, "AddForeignVariable");

        FieldDescriptor error = ContractDescriptors.RequireField(addForeign.OutputType, "error");
        Assert.Same(expressionError, error.MessageType);

        // The error travels with a return code, so a caller can branch on the code and then read the
        // category for the detail rather than parsing text.
        Assert.NotNull(ContractDescriptors.FindField(addForeign.OutputType, "ret_code"));
    }

    /// <summary>
    /// The <c>links[]</c> collection of the global variable entry is repeated.
    /// </summary>
    /// <remarks>
    /// <c>globalvardata.links[]</c> [<c>n_cst_dwsvc_columnexp.sru</c>:L70] is an ARRAY of foreign
    /// references, so one variable may be linked to several foreign services. Carrying it as a singular
    /// field would silently cap the model at one link, and the cap would only surface as a missing
    /// dependency when a multi-linked variable failed to propagate a change - a silent wrong answer.
    /// Its element type is the same narrowed reference, so every link is subject to the same
    /// session-scoping limit rather than only the primary one.
    /// </remarks>
    [Fact]
    public void TheGlobalVariableEntryCarriesItsForeignLinksAsARepeatedCollection()
    {
        MessageDescriptor entry = ContractDescriptors.RequireMessage(GlobalVarDataMessage);
        FieldDescriptor links = ContractDescriptors.RequireField(entry, "links");

        Assert.True(links.IsRepeated, "`links[]` [:L70] is an array - one variable may have several links.");
        Assert.Equal(FieldType.Message, links.FieldType);

        // Every link is the NARROWED reference, so the session-scoping limit applies to all of them.
        Assert.Equal(ForeignVarRefMessage, links.MessageType.Name);
    }

    /// <summary>
    /// The expression session that scopes the handles exists and is explicitly opened and closed over a
    /// named set of DataWindows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The session is not incidental infrastructure - it is the mechanism that makes the narrowing
    /// coherent. A handle is valid only inside the session that allocated it, so without an explicit
    /// session there is no scope for "co-resident" to mean anything and no boundary at which a
    /// cross-session reference can be detected and blocked.
    /// </para>
    /// <para>
    /// The open request naming a REPEATED set of DataWindow handles is the part that matters most here:
    /// that set IS the co-residency declaration. A session over one DataWindow could never host a foreign
    /// reference at all, so the plural is what makes the supported case reachable.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheExpressionSessionThatScopesTheHandlesIsExplicitlyOpenedOverASetOfDataWindows()
    {
        ServiceDescriptor service = ContractDescriptors.RequireService(ColumnExpressionServiceName);

        MethodDescriptor open = ContractDescriptors.RequireMethod(service, "OpenExpressionSession");
        MethodDescriptor close = ContractDescriptors.RequireMethod(service, "CloseExpressionSession");

        // Co-residency is declared by naming SEVERAL DataWindows on one session.
        FieldDescriptor requested = ContractDescriptors.RequireField(open.InputType, "datawindow_handles");
        Assert.True(
            requested.IsRepeated,
            "a session must be able to span several DataWindows, or a foreign reference could never be "
            + "co-resident and the supported case would be unreachable.");
        Assert.Equal(FieldType.String, requested.FieldType);

        // The session identifier the handles are scoped to.
        Assert.Equal(FieldType.String, ContractDescriptors.RequireField(open.OutputType, "session_id").FieldType);

        // The set actually admitted is echoed back, so a caller learns which handles the session holds
        // rather than assuming its request was honoured wholesale.
        Assert.True(ContractDescriptors.RequireField(open.OutputType, "datawindow_handles").IsRepeated);

        // Closing is explicit, so handle lifetime is bounded rather than left to a timeout.
        Assert.Equal(FieldType.String, ContractDescriptors.RequireField(close.InputType, "session_id").FieldType);

        // Both are unary: session lifecycle is request/response, not a stream.
        Assert.False(open.IsClientStreaming);
        Assert.False(open.IsServerStreaming);
        Assert.False(close.IsClientStreaming);
        Assert.False(close.IsServerStreaming);
    }

    /// <summary>
    /// Every method that resolves a foreign reference is session-scoped, so no call can reach a foreign
    /// variable outside a session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The narrowing is only enforceable if there is no way around it. A method that took a DataWindow
    /// handle WITHOUT a session identifier could resolve a foreign reference with no co-residency scope to
    /// check it against, which would leave the service no basis on which to block anything - so the limit
    /// would be documented but not enforceable.
    /// </para>
    /// <para>
    /// Asserted as a sweep over the whole service rather than for the foreign-variable call alone,
    /// because the foreign reference is reachable from expression retrieval and calculation too, not only
    /// from the call that creates it.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryUnaryMethodOfTheExpressionServiceIsScopedToASession()
    {
        ServiceDescriptor service = ContractDescriptors.RequireService(ColumnExpressionServiceName);

        // The session-opening call is the one legitimate exception: it has no session yet.
        const string SessionFactory = "OpenExpressionSession";

        List<string> unscoped =
        [
            .. from method in service.Methods
               where !method.IsClientStreaming
                     && !string.Equals(method.Name, SessionFactory, StringComparison.Ordinal)
               where ContractDescriptors.FindField(method.InputType, "session_id") is null
               select $"{method.Name}({method.InputType.Name})",
        ];

        Assert.True(
            unscoped.Count == 0,
            $"{ColumnExpressionServiceName} declares method(s) with no session_id - "
            + $"{string.Join(", ", unscoped)} - so a foreign reference could be resolved outside the "
            + "session that scopes its handle, leaving the AAP 0.6.2.3 narrowing unenforceable.");

        // Guard the guard: the sweep must actually have examined methods.
        Assert.True(
            service.Methods.Count > 1,
            "the sweep found no methods to examine, so it proves nothing.");
    }

    // ==============================================================================================
    //  SECTION 5 - THE TWO INVERTED STREAMS
    //
    //  These are not a stylistic preference; they are structurally forced, and they belong in this file
    //  because macro-direct and macro-dynamic are two of the five expansion modes and neither is usable
    //  without the inversion.
    //
    //  THE LEGACY EXPECTS THE APPLICATION TO IMPLEMENT THE MACRO SWITCH. A macro call is dispatched
    //  through `oncolumnexpinvokemethod(row, dwo, name, string args[]) returns any`
    //  [ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L14] - an event the HOST answers, not
    //  the service. Across a service boundary the caller therefore has to be callable, so DataServices
    //  must call BACK INTO ITS CLIENT, which a normal request/response cannot express.
    // ==============================================================================================

    /// <summary>
    /// Macro invocation is carried on an inverted bidirectional stream, so the service can call back into
    /// its client to have a macro executed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The inversion is visible in the method signature and is asserted there.</b> The channel's
    /// REQUEST type is the invocation RESPONSE message and its RESPONSE type is the invocation REQUEST
    /// message - the client streams results in and receives invocations out. That reversal is what
    /// reproduces <c>oncolumnexpinvokemethod</c> [<c>se_cst_dw.sru</c>:L14] across a boundary, and a
    /// contract that had the two the conventional way round would have the service waiting for a client
    /// that is itself waiting for the service.
    /// </para>
    /// <para>
    /// The invocation carries its expansion mode, which is why this assertion lives here rather than in a
    /// transport-shaped test: a macro reference is a reference, so macro-direct and macro-dynamic have to
    /// be distinguishable on the invocation itself. <c>args</c> is repeated, matching the legacy event's
    /// <c>string args[]</c> parameter.
    /// </para>
    /// </remarks>
    [Fact]
    public void MacroInvocationIsAnInvertedBidirectionalStreamCarryingThePerReferenceMode()
    {
        ServiceDescriptor service = ContractDescriptors.RequireService(ColumnExpressionServiceName);
        MethodDescriptor channel = ContractDescriptors.RequireMethod(service, "InvokeMethodChannel");

        Assert.True(channel.IsClientStreaming);
        Assert.True(channel.IsServerStreaming);

        // THE INVERSION: what the client sends is the RESPONSE message; what it receives is the REQUEST.
        Assert.Equal("InvokeMethodResponse", channel.InputType.Name);
        Assert.Equal("InvokeMethodRequest", channel.OutputType.Name);

        // The invocation the service pushes out carries the mode that dispatched it - so a
        // macro-direct call is distinguishable from a macro-dynamic one at the point of invocation.
        MessageDescriptor invocation = channel.OutputType;
        FieldDescriptor form = ContractDescriptors.RequireField(invocation, "form");
        Assert.Equal(FieldType.Enum, form.FieldType);
        Assert.Equal(ExpansionModeEnum, form.EnumType.Name);

        // `string args[]` from oncolumnexpinvokemethod [se_cst_dw.sru:L14].
        Assert.True(ContractDescriptors.RequireField(invocation, "args").IsRepeated);
        Assert.Equal(FieldType.String, ContractDescriptors.RequireField(invocation, "args").FieldType);

        // Correlated, because several invocations may be outstanding on one channel at once.
        Assert.Equal(
            FieldType.String,
            ContractDescriptors.RequireField(invocation, "invocation_id").FieldType);
        Assert.Equal(
            FieldType.String,
            ContractDescriptors.RequireField(channel.InputType, "invocation_id").FieldType);
    }

    /// <summary>
    /// The expression trace is carried on its own server-streaming channel, and it carries the call stack
    /// the legacy recursion vector builds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The trace corresponds to <c>oncolumnexptrace(row, dwo, stack, expr, value)</c>
    /// [<c>se_cst_dw.sru</c>:L32]. It is pure diagnostics and fire-and-forget, which is why it is a
    /// separate channel from macro invocation rather than another arm of the same one: mixing a channel
    /// the service BLOCKS on with a channel it does not would let trace volume stall calculation.
    /// </para>
    /// <para>
    /// <c>stack</c> is the recursion stack the legacy holds in its vector container
    /// [<c>n_cst_dwsvc_columnexp.sru</c>:L110]. The record carries both the legacy's single rendered
    /// string - preserving the exact observable form - and a structured frame list, so a consumer need not
    /// parse the string to read the stack.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheExpressionTraceIsItsOwnStreamAndCarriesTheRecursionStack()
    {
        ServiceDescriptor service = ContractDescriptors.RequireService(ColumnExpressionServiceName);
        MethodDescriptor trace = ContractDescriptors.RequireMethod(service, "TraceChannel");

        Assert.True(trace.IsClientStreaming);
        Assert.True(trace.IsServerStreaming);

        MessageDescriptor record = trace.OutputType;

        // The three payload members of oncolumnexptrace [se_cst_dw.sru:L32], preserved as authored.
        Assert.Equal(FieldType.String, ContractDescriptors.RequireField(record, "stack").FieldType);
        Assert.Equal(FieldType.String, ContractDescriptors.RequireField(record, "expr").FieldType);
        Assert.Equal(FieldType.String, ContractDescriptors.RequireField(record, "value").FieldType);

        // Plus a structured rendering of the same stack, so the string need not be parsed.
        Assert.True(ContractDescriptors.RequireField(record, "stack_frames").IsRepeated);

        // A distinct channel from macro invocation, so diagnostics cannot stall calculation.
        MethodDescriptor invoke = ContractDescriptors.RequireMethod(service, "InvokeMethodChannel");
        Assert.NotSame(invoke.OutputType, record);
    }
}
