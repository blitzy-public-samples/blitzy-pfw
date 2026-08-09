// ==================================================================================================
//  ColumnExpressionEngineTests - THE $ VERSUS $$ ACCEPTANCE SUITE
//  ------------------------------------------------------------------------------------------------
//  SUBJECT   PowerFramework.DataServices.Expressions.ColumnExpressionEngine
//            <- ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru (2,435 lines)
//            <- docs/n_cst_dwsvc_columnexp.md (166 lines, the authoritative specification)
//
//  WHY THIS FILE EXISTS
//  ------------------------------------------------------------------------------------------------
//  AAP section 0.1.3 G4 names the static-versus-dynamic expansion contract as one of two contracts
//  that must be solved EXPLICITLY, because the distinction "does not survive naive serialization".
//  The two golden tests transcribed from the specification are the acceptance condition for that
//  goal, and they are the first two tests below:
//
//      static   of_AddvarExp("num1", "$上月读数 + $本月读数")  then SetVar(1)  ->  STILL 5
//      dynamic  of_AddvarExp("num1", "$$上月读数 + $本月读数") then SetVar(1)  ->  NOW 6
//
//  Everything after them exists because a green pair of golden tests is not sufficient: the same
//  mechanism has to hold for a MIXED expression, for an indirect binding, for the undocumented `@`
//  sigil, and for the parenthesis wrap without which a multi-term substitution silently changes
//  operator precedence. Each of those is its own test below, and each would pass a naive
//  implementation that got the golden pair right by accident.
//
//  WHAT THESE TESTS ASSERT ON, AND WHY BOTH HALVES ARE NEEDED
//  ------------------------------------------------------------------------------------------------
//  Two independent observations pin the mechanism, and a bug that hides from one shows in the other:
//
//    1. THE STORED TEXT. After a static reference is bound the variable's NAME IS GONE from the
//       stored expression, because :L1435 rewrites the expression in place. After a dynamic one the
//       name is still there. That is the mechanism, and `GetVarExp`/`GetSourceExp` expose it.
//    2. THE COMPUTED VALUE. 5 against 6, read back out of the column the expression is bound to.
//       That is the behaviour a caller sees, and it is what the specification's worked example shows.
//
//  Asserting only the value would pass an implementation that re-resolved a static reference at calc
//  time and happened to read a stale cache; asserting only the text would pass one that stored the
//  right text and then evaluated something else.
//
//  DEFECTS ASSERTED AS DEFECTS (constraint C-B)
//  ------------------------------------------------------------------------------------------------
//  Several tests below assert behaviour that is WRONG and is preserved because the legacy does it.
//  Each is named `..._IsPreservedDefect` or carries the word "defect" in its comment so that a later
//  reader cannot mistake the assertion for an endorsement:
//
//    * the empty-variable-value parse arm answers FAILED where all eleven of its neighbours answer
//      E_INVALID_ARGUMENT [:L1431 against :L1417, :L1422, :L1426 and the rest]
//    * the 28 messages are hardcoded Chinese and do NOT route through localization, unlike the
//      equivalent messages in the dwsvc, rowselect and contextmenu services which do
//    * the composed compute-object name carries a DOUBLE underscore [:L1886]
//    * `_of_calcitem` is public while keeping its private-convention underscore prefix
//
//  DETERMINISM
//  ------------------------------------------------------------------------------------------------
//  Every collaborator is injected: the host is a fake, the evaluator runs over that fake, the macro
//  channel is a recording double, and the syntax mutator is a double that materialises compute
//  objects on the fake. No live DataWindow, no gRPC host and no clock read is involved, which is what
//  AAP section 0.6.7 requires of a characterization fixture.
// ==================================================================================================

using System.Collections.Immutable;
using System.Globalization;

using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Expressions;
using PowerFramework.Shared.Kernel;

using Xunit;

using DwBuffer = PowerFramework.Contracts.Common.V1.DwBuffer;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// A syntax mutator that materialises and removes compute objects on a <see cref="FakeDataWindowHost"/>,
/// so the cacheable path can be exercised without a live DataWindow.
/// </summary>
/// <remarks>
/// IT PARSES THE ORACLE'S OWN FRAGMENT rather than accepting a name through a side channel, because the
/// fragment's shape is itself part of what is being characterized: <c>_of_createcompute</c> composes
/// <c>create compute(... name=X ...)</c> at :L1943-L1948 and <c>_of_setcacheable</c> composes
/// <c>Destroy X</c>, and a mutator that ignored the text would let a malformed fragment pass unnoticed.
/// </remarks>
internal sealed class RecordingSyntaxMutator : IDataWindowSyntaxMutator
{
    private readonly FakeDataWindowHost _host;
    private readonly List<string> _syntax = [];
    private readonly List<string> _computeNames = [];

    public RecordingSyntaxMutator(FakeDataWindowHost host) => _host = host;

    /// <summary>Every fragment submitted, in order.</summary>
    public IReadOnlyList<string> Syntax => _syntax;

    /// <summary>Every compute-object name created, in order.</summary>
    public IReadOnlyList<string> ComputeNames => _computeNames;

    /// <summary>When set, every submission is refused with this text.</summary>
    public string? FailWith { get; set; }

    /// <summary>The value every created compute object reports, keyed by compute name.</summary>
    public Dictionary<string, object?> ComputedValues { get; } = [];

    public string Modify(string syntax)
    {
        _syntax.Add(syntax);

        if (FailWith is { } failure)
        {
            return failure;
        }

        if (syntax.StartsWith("Destroy ", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        // `... name=<NAME> visible="1" ...` - the name is unquoted in the oracle's fragment.
        const string NameMarker = " name=";
        int nameStart = syntax.IndexOf(NameMarker, StringComparison.Ordinal);
        if (nameStart < 0)
        {
            return "no name= in fragment";
        }

        nameStart += NameMarker.Length;
        int nameEnd = syntax.IndexOf(' ', nameStart);
        if (nameEnd < 0)
        {
            return "unterminated name= in fragment";
        }

        string name = syntax[nameStart..nameEnd];
        FakeDataWindowObjectDefinition compute = _host.AddComputedField(name, "detail", "0");
        compute.ColType = FakeColumnType.DecimalOf(2);

        // A UNIQUE IDENTIFIER IS ESSENTIAL, and the fake leaves a computed field's at zero because a real
        // DataWindow numbers only its columns. The value buffer is keyed by identifier, so several compute
        // objects all sitting at zero would SHARE one buffer slot and each would read whatever the last one
        // wrote - which looks exactly like a broken cached read and is not one.
        compute.Id = _host.Objects.Max(static existing => existing.Id) + 1;

        _computeNames.Add(name);

        // Give the compute object a row-aligned value so the cached read has something to answer with.
        object? seeded = ComputedValues.TryGetValue(name, out object? value) ? value : 0m;
        for (long row = 1; row <= _host.RowCount(); row++)
        {
            _host.SetBufferValue(DwBuffer.Primary, row, compute.Id, seeded);
        }

        return string.Empty;
    }
}

/// <summary>
/// A macro channel that answers from a table, so <c>OnColumnExpInvokeMethod</c> can be characterized
/// without an application on the far end of C-04's <c>InvokeMethodChannel</c>.
/// </summary>
internal sealed class RecordingMacroChannel : IMacroInvocationChannel
{
    private readonly List<MacroInvocation> _invocations = [];

    /// <summary>Every invocation the engine issued, in order.</summary>
    public IReadOnlyList<MacroInvocation> Invocations => _invocations;

    /// <summary>The answer per macro name. A name that is absent answers unhandled.</summary>
    public Dictionary<string, object?> Answers { get; } = [];

    /// <summary>Names that answer unhandled even when present in <see cref="Answers"/>.</summary>
    public HashSet<string> Unhandled { get; } = [];

    public ValueTask<MacroInvocationResponse> InvokeAsync(
        MacroInvocation invocation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        _invocations.Add(invocation);

        if (Unhandled.Contains(invocation.Name) || !Answers.TryGetValue(invocation.Name, out object? answer))
        {
            // An event with no script leaves the `any` uninitialised, which is what `Unhandled` models.
            return ValueTask.FromResult(
                new MacroInvocationResponse
                {
                    InvocationId = invocation.InvocationId,
                    Sequence = invocation.Sequence,
                    Unhandled = true,
                });
        }

        return ValueTask.FromResult(
            new MacroInvocationResponse
            {
                InvocationId = invocation.InvocationId,
                Sequence = invocation.Sequence,
                Value = answer,
            });
    }
}

/// <summary>
/// A macro channel that RE-ENTERS the engine from inside a macro invocation, so the row-calculation
/// nesting guard [:L1161] can be observed rather than inferred.
/// </summary>
/// <remarks>
/// The channel is the ONLY await point inside a calculation pass, so it is the only place a nested pass
/// is reachable from. A DataWindow event handler cannot serve: the legacy handlers are synchronous and
/// the ported ones are too, so no handler can await a nested pass.
/// </remarks>
internal sealed class ReentrantMacroChannel : IMacroInvocationChannel
{
    private readonly Func<CancellationToken, ValueTask<long>> _reenter;
    private readonly object? _answer;

    public ReentrantMacroChannel(Func<CancellationToken, ValueTask<long>> reenter, object? answer)
    {
        _reenter = reenter;
        _answer = answer;
    }

    /// <summary>The code the nested pass answered, or <see langword="null"/> if it was never issued.</summary>
    public long? NestedResult { get; private set; }

    public async ValueTask<MacroInvocationResponse> InvokeAsync(
        MacroInvocation invocation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        NestedResult = await _reenter(cancellationToken).ConfigureAwait(false);

        return new MacroInvocationResponse
        {
            InvocationId = invocation.InvocationId,
            Sequence = invocation.Sequence,
            Value = _answer,
        };
    }
}

/// <summary>
/// A minimal <see cref="IExpressionServiceHost"/> that is NOT a <see cref="ColumnExpressionEngine"/>, so
/// the context-service handle lookup can be exercised with a peer the engine cannot recognise by type.
/// </summary>
/// <remarks>
/// A REAL PEER IN A LATER PHASE NEED NOT BE THIS TYPE EITHER. C-04's <c>InvokeMethodChannel</c> and the
/// session's registry are both typed on the CONTRACT, so an implementation that is not this class is a
/// supported shape rather than a test artifice - which is why the engine looks a handle up rather than
/// casting.
/// </remarks>
internal sealed class RecordingExpressionServiceHost : IExpressionServiceHost
{
    private readonly List<string> _reads = [];
    private readonly List<string> _variableNames = [];

    /// <summary>The cell renderings this host answers, keyed by column name.</summary>
    public Dictionary<string, string> Items { get; } = [];

    /// <summary>Every column name read, in order.</summary>
    public IReadOnlyList<string> Reads => _reads;

    /// <summary>Every variable index resolved, in order.</summary>
    public List<int> ResolvedIndexes { get; } = [];

    /// <summary>Declares a variable, whose ONE-BASED index is its declaration order.</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The rendering its value expression answers.</param>
    public void AddVariable(string name, string value)
    {
        _variableNames.Add(name);
        Items[name] = value;
    }

    public int FindVarIndex(string? name) =>
        name is null ? 0 : _variableNames.IndexOf(name) + 1;

    public string CalcVarExpValue(
        long row,
        IDataWindowObject? dwo,
        int index,
        long ctxRow,
        IDataWindowObject? ctxDwo,
        IExpressionServiceHost? ctxExpSvc) =>
        CalcVarExpValue(index, ctxRow, ctxDwo, ctxExpSvc);

    public string CalcVarExpValue(
        int index,
        long ctxRow,
        IDataWindowObject? ctxDwo,
        IExpressionServiceHost? ctxExpSvc)
    {
        ResolvedIndexes.Add(index);

        return index >= 1 && index <= _variableNames.Count
            ? Items.GetValueOrDefault(_variableNames[index - 1], string.Empty)
            : string.Empty;
    }

    public string GetItemExpValue(long row, string? columnName)
    {
        string column = columnName ?? string.Empty;
        _reads.Add(column);

        return Items.TryGetValue(column, out string? value) ? value : string.Empty;
    }
}

/// <summary>
/// A trace sink that keeps every record, so the trace payload can be asserted as a golden string.
/// </summary>
internal sealed class RecordingTraceSink : IExpressionTraceSink
{
    private readonly List<ExpressionTraceRecord> _records = [];

    public IReadOnlyList<ExpressionTraceRecord> Records => _records;

    public void Emit(ExpressionTraceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        _records.Add(record);
    }
}

/// <summary>
/// The characterization suite for <see cref="ColumnExpressionEngine"/>.
/// </summary>
public sealed class ColumnExpressionEngineTests
{
    // The specification's own variable names, transcribed rather than translated, because the parse
    // routine's token scanning is byte oriented and a Latin rename would not exercise the same path:
    // a multi byte identifier is exactly where a length-versus-byte-count confusion would surface.
    private const string ThisMonth = "本月读数";
    private const string LastMonth = "上月读数";
    private const string NextMonth = "下月读数";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A session with a fixed identifier, so a trace payload or a BLOCKED message is a stable string.
    /// </summary>
    /// <param name="traceSink">The trace sink, or null.</param>
    private static ExpressionSession NewSession(RecordingTraceSink? traceSink = null) =>
        new(
            "test-session",
            lifetime: null,
            columnExpression: null,
            timeProvider: null,
            traceSink: traceSink);

    /// <summary>
    /// Builds an initialised, enabled engine over the column expression fixture.
    /// </summary>
    /// <param name="host">Receives the fake host.</param>
    /// <param name="mutator">When supplied, receives a syntax mutator wired to that host.</param>
    /// <param name="channel">When supplied, receives a macro channel and the engine gets an invoker.</param>
    /// <param name="traceSink">When supplied, receives a trace sink and the engine gets a session using it.</param>
    /// <param name="options">Overrides the preserved defaults.</param>
    private static ColumnExpressionEngine CreateEngine(
        out FakeDataWindowHost host,
        RecordingSyntaxMutator? mutator = null,
        RecordingMacroChannel? channel = null,
        RecordingTraceSink? traceSink = null,
        ColumnExpressionOptions? options = null)
    {
        host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        return CreateEngineOver(host, mutator, channel, traceSink, options);
    }

    /// <summary>Builds an initialised, enabled engine over a supplied host.</summary>
    /// <param name="host">The host.</param>
    /// <param name="mutator">The syntax mutator, or null.</param>
    /// <param name="channel">The macro channel, or null.</param>
    /// <param name="traceSink">The trace sink, or null.</param>
    /// <param name="options">The options, or null for the preserved defaults.</param>
    /// <param name="session">The session, or null for a private one.</param>
    private static ColumnExpressionEngine CreateEngineOver(
        FakeDataWindowHost host,
        RecordingSyntaxMutator? mutator = null,
        IMacroInvocationChannel? channel = null,
        RecordingTraceSink? traceSink = null,
        ColumnExpressionOptions? options = null,
        ExpressionSession? session = null)
    {
        ColumnExpressionEngine engine = new(
            options,
            evaluator: null,
            macroInvoker: channel is null ? null : new MacroInvoker(channel),
            session: session,
            syntaxMutator: mutator,
            traceSink: traceSink);

        engine.OnInit(host);
        engine.SetEnabled(true);
        return engine;
    }

    // ==============================================================================================
    //  1. THE TWO GOLDEN TESTS - the acceptance condition for AAP section 0.1.3 G4
    // ==============================================================================================

    /// <summary>
    /// STATIC expansion, transcribed from docs/n_cst_dwsvc_columnexp.md section 2 line by line: the
    /// result is fixed at 5 at bind time and a later assignment CANNOT change it.
    /// </summary>
    [Fact]
    public async Task StaticExpansion_FixesTheValueAtBindTime_SpecificationSection2()
    {
        ColumnExpressionEngine engine = CreateEngine(out FakeDataWindowHost host);

        // of_AddVarExp("本月读数", "5")     - a variable whose value is the DataWindow expression "5"
        Assert.Equal(RetCode.OK, engine.AddVarExp(ThisMonth, "5"));

        // of_AddVar("上月读数", 0)          - a variable whose value is the number 0
        Assert.Equal(RetCode.OK, engine.AddVar(LastMonth, 0L));

        // of_AddvarExp("num1", "$上月读数 + $本月读数")  - BOTH references static
        Assert.Equal(RetCode.OK, engine.AddVarExp("num1", "$" + LastMonth + " + $" + ThisMonth));

        // THE MECHANISM: both names are GONE from the stored text, replaced by their bracketed values.
        Assert.Equal("(0) + (5)", engine.GetVarExp("num1"));
        Assert.DoesNotContain(LastMonth, engine.GetVarExp("num1"), StringComparison.Ordinal);

        // Bind num1 to a column so the value is observable, then take a baseline.
        Assert.True(engine.AddExp("n1", "$$num1") > 0);
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(5m, host.GetItemDecimal(1L, "n1"));

        // of_SetVar("上月读数", 1) then of_Calc(1) - and the answer is STILL 5.
        Assert.Equal(RetCode.OK, await engine.SetVarAsync(LastMonth, 1L, false, Ct));
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(5m, host.GetItemDecimal(1L, "n1"));
    }

    /// <summary>
    /// DYNAMIC expansion, transcribed from docs/n_cst_dwsvc_columnexp.md section 3: the identical
    /// sequence with a doubled sigil on the first reference now yields 6.
    /// </summary>
    [Fact]
    public async Task DynamicExpansion_TracksTheVariable_SpecificationSection3()
    {
        ColumnExpressionEngine engine = CreateEngine(out FakeDataWindowHost host);

        Assert.Equal(RetCode.OK, engine.AddVarExp(ThisMonth, "5"));
        Assert.Equal(RetCode.OK, engine.AddVar(LastMonth, 0L));

        // The ONLY difference from the test above: `$$上月读数` instead of `$上月读数`.
        Assert.Equal(RetCode.OK, engine.AddVarExp("num1", "$$" + LastMonth + " + $" + ThisMonth));

        // THE MECHANISM: the dynamic name is RETAINED and the static one is gone. Per reference.
        Assert.Equal("$$" + LastMonth + " + (5)", engine.GetVarExp("num1"));

        Assert.True(engine.AddExp("n1", "$$num1") > 0);
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(5m, host.GetItemDecimal(1L, "n1"));

        // of_SetVar("上月读数", 1) then of_Calc(1) - and the answer is NOW 6.
        Assert.Equal(RetCode.OK, await engine.SetVarAsync(LastMonth, 1L, false, Ct));
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(6m, host.GetItemDecimal(1L, "n1"));
    }

    // ==============================================================================================
    //  2. MODE IS A PER-REFERENCE PROPERTY, NEVER PER-EXPRESSION
    // ==============================================================================================

    /// <summary>
    /// One expression carrying both a static and a dynamic reference: mutating the statically bound
    /// variable changes nothing, mutating the dynamically bound one changes the result. The golden pair
    /// above would both pass an implementation that decided the mode ONCE per expression; this does not.
    /// </summary>
    [Fact]
    public async Task MixedMode_ResolvesEachReferenceIndependently()
    {
        ColumnExpressionEngine engine = CreateEngine(out FakeDataWindowHost host);

        Assert.Equal(RetCode.OK, engine.AddVar("stat", 10L));
        Assert.Equal(RetCode.OK, engine.AddVar("dyn", 1L));

        // `$stat` is baked in at 10; `$$dyn` stays a reference.
        Assert.True(engine.AddExp("n1", "$stat + $$dyn") > 0);
        Assert.Equal("(10) + $$dyn", engine.GetExp("n1"));

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(11m, host.GetItemDecimal(1L, "n1"));

        // Mutating the STATIC one changes nothing at all.
        Assert.Equal(RetCode.OK, await engine.SetVarAsync("stat", 100L, false, Ct));
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(11m, host.GetItemDecimal(1L, "n1"));

        // Mutating the DYNAMIC one, in the very same expression, does.
        Assert.Equal(RetCode.OK, await engine.SetVarAsync("dyn", 5L, false, Ct));
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(15m, host.GetItemDecimal(1L, "n1"));
    }

    // ==============================================================================================
    //  3. THE PARENTHESIS WRAP - step 5 of the static path, and the guard that catches its omission
    // ==============================================================================================

    /// <summary>
    /// A static variable whose value expression is <c>1 + 2</c>, substituted into <c>$v * 3</c>, must
    /// yield 9 and not 7.
    /// </summary>
    /// <remarks>
    /// THIS IS THE SINGLE MOST CONSEQUENTIAL ONE LINE BEHAVIOUR IN THE STATIC PATH. Without the wrap at
    /// :L1434 the rewritten text is <c>1 + 2 * 3</c>, which the evaluator answers with 7 - a silently
    /// wrong number with no error anywhere. With it the text is <c>(1 + 2) * 3</c> and the answer is 9.
    /// </remarks>
    [Fact]
    public async Task StaticExpansion_WrapsTheSubstitutedValueInParentheses()
    {
        ColumnExpressionEngine engine = CreateEngine(out FakeDataWindowHost host);

        Assert.Equal(RetCode.OK, engine.AddVarExp("v", "1 + 2"));
        Assert.True(engine.AddExp("n1", "$v * 3") > 0);

        // The wrap is visible in the stored text before any calculation happens.
        Assert.Equal("(1 + 2) * 3", engine.GetExp("n1"));

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(9m, host.GetItemDecimal(1L, "n1"));
    }

    /// <summary>
    /// The dynamic path wraps at substitution time for the same reason [:L2213], so the precedence
    /// guarantee holds on both paths rather than only the one that rewrites at bind time.
    /// </summary>
    [Fact]
    public async Task DynamicExpansion_WrapsTheSubstitutedValueInParentheses()
    {
        ColumnExpressionEngine engine = CreateEngine(out FakeDataWindowHost host);

        Assert.Equal(RetCode.OK, engine.AddVarExp("v", "1 + 2"));
        Assert.True(engine.AddExp("n1", "$$v * 3") > 0);

        // The reference is RETAINED here, so the wrap cannot be observed in the stored text at all.
        Assert.Equal("$$v * 3", engine.GetExp("n1"));

        // It is observable only in the answer: 9 means the wrap happened, 7 means it did not.
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(9m, host.GetItemDecimal(1L, "n1"));
    }

    // ==============================================================================================
    //  4. WHOLE TOKEN REPLACEMENT - the fifth argument of ReplaceAll
    // ==============================================================================================

    /// <summary>
    /// Variables named <c>aa</c> and <c>aab</c> in one expression: <c>$aa</c> must not match inside
    /// <c>$aab</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE FIFTH ARGUMENT OF <c>ReplaceAll</c> IS WHAT MAKES THIS WORK [:L1435 passes both trailing
    /// flags]. A plain substring replacement would rewrite <c>$aab</c> into <c>(1)b</c>, and the
    /// resulting expression would either fail to parse or silently evaluate something else.
    /// </para>
    /// <para>
    /// THE NAME LENGTHS ARE CHOSEN SO THE SUBSTITUTION IS LENGTH NEUTRAL, and that is not incidental.
    /// <c>$aa</c> and its bracketed value <c>(1)</c> are both three characters, as are <c>$aab</c> and
    /// <c>(20)</c> at four - so the trailer stays inside the scan range and the second reference is
    /// reached. A LENGTHENING substitution truncates the scan instead, which is a separate preserved
    /// defect asserted on its own below; using short names here would confound the two.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task StaticExpansion_ReplacesWholeTokensOnly()
    {
        ColumnExpressionEngine engine = CreateEngine(out FakeDataWindowHost host);

        Assert.Equal(RetCode.OK, engine.AddVar("aa", 1L));
        Assert.Equal(RetCode.OK, engine.AddVar("aab", 20L));

        Assert.True(engine.AddExp("n1", "$aa + $aab") > 0);
        Assert.Equal("(1) + (20)", engine.GetExp("n1"));

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(21m, host.GetItemDecimal(1L, "n1"));
    }

    /// <summary>
    /// The same guarantee with the SHORTER name declared second, so the test cannot pass merely because
    /// declaration order happened to put the longer name first.
    /// </summary>
    [Fact]
    public async Task StaticExpansion_ReplacesWholeTokensOnly_RegardlessOfDeclarationOrder()
    {
        ColumnExpressionEngine engine = CreateEngine(out FakeDataWindowHost host);

        Assert.Equal(RetCode.OK, engine.AddVar("aab", 20L));
        Assert.Equal(RetCode.OK, engine.AddVar("aa", 1L));

        Assert.True(engine.AddExp("n1", "$aab + $aa") > 0);
        Assert.Equal("(20) + (1)", engine.GetExp("n1"));

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(21m, host.GetItemDecimal(1L, "n1"));
    }

    /// <summary>
    /// A LENGTHENING static substitution leaves a later macro unscanned, so it survives into the stored
    /// text unexpanded and then fails at calculation time. PRESERVED DEFECT (constraint C-B).
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE CAUSE IS A POWERSCRIPT LOOP SEMANTIC, NOT A TYPO. The scan is
    /// <c>for nPos = 1 to nLen</c> [:L1266], and PowerScript evaluates a FOR limit ONCE before the loop
    /// begins. The static path duly refreshes <c>nLen</c> after rewriting the expression [:L1436], but
    /// that assignment cannot extend a bound that was already captured - so when the rewrite makes the
    /// expression LONGER, the trailing characters fall outside the scan. Here <c>$a</c> becomes
    /// <c>(1)</c>, one character longer, which pushes the <c>;</c> trailer out of range; the terminator
    /// that would have completed <c>$ab</c> is therefore never seen and the reference is never parsed.
    /// </para>
    /// <para>
    /// THE REFRESHED <c>nLen</c> IS STILL LOAD BEARING, which is why it cannot simply be deleted: :L1509
    /// uses it to strip the trailer with <c>Left(sExp, nLen - 1)</c>. So the same variable is stale for
    /// one purpose and current for another, and both behaviours are observable.
    /// </para>
    /// <para>
    /// The specification's own worked examples are unaffected because their variable NAMES are longer
    /// than their values, making every substitution shorter - which is very likely why the defect was
    /// never noticed.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task StaticExpansion_LengtheningRewriteTruncatesTheScan_IsPreservedDefect()
    {
        ColumnExpressionEngine engine = CreateEngine(out FakeDataWindowHost host);

        Assert.Equal(RetCode.OK, engine.AddVar("a", 1L));
        Assert.Equal(RetCode.OK, engine.AddVar("ab", 20L));

        Assert.True(engine.AddExp("n1", "$a + $ab") > 0);

        // `$a` expanded; `$ab` did NOT, and is still sitting in the stored text as a literal token.
        Assert.Equal("(1) + $ab", engine.GetExp("n1"));

        // It was not retained as a reference either, so nothing will expand it later.
        Assert.Equal(0, engine.Expressions[0].Vars.UpperBound);
        Assert.False(engine.Expressions[0].HasMacro);

        // At calculation time the evaluator cannot make sense of `$ab`, so the defect surfaces as a
        // REPORTED expression error rather than as a wrong number - which is the one mercy in it.
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.NotNull(engine.LastError);
        Assert.Equal(ExpressionErrorSite.ExpressionError, engine.LastError.Site);

        // And the column keeps its previous value rather than acquiring a fabricated one.
        Assert.Equal(0m, host.GetItemDecimal(1L, "n1"));
    }

    // ==============================================================================================
    //  5. THE STRUCTURE CENSUS - 19 fields and 3, diffed against :L22-L48
    // ==============================================================================================

    /// <summary>
    /// <c>columnexpdata</c> declares exactly 19 fields [:L22-L42] and this port must expose exactly
    /// those 19, by those names.
    /// </summary>
    /// <remarks>
    /// THE COUNT IS THE POINT, NOT ONLY THE NAMES. C-04's <c>ColumnExpData</c> message was generated
    /// from this same census, so a twentieth field added here without a contract change would make the
    /// wire representation lossy in a way no round trip test would notice. The unexpanded source text -
    /// which the wire DOES carry - is deliberately held OUTSIDE this structure for that reason.
    /// </remarks>
    [Fact]
    public void ColumnExpressionData_DeclaresExactlyTheOraclesNineteenFields()
    {
        string[] expected =
        [
            "Name", "Id", "Dwo", "ColType", "Exp", "Vars", "Fns", "RelativeColIds",
            "RelativeInputColIds", "DupExps", "EmptyStringIsNull", "HasMacro", "AlwaysCalc",
            "TriggerEvent", "Recursive", "Dirty", "Empty", "Cacheable", "ComputeName",
        ];

        string[] actual = [.. typeof(ColumnExpressionData)
            .GetProperties()
            .Select(static property => property.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)];

        Assert.Equal(19, actual.Length);
        Assert.Equal([.. expected.OrderBy(static name => name, StringComparer.Ordinal)], actual);
    }

    /// <summary><c>columndata</c> declares exactly 3 fields [:L44-L48].</summary>
    [Fact]
    public void ColumnCalcData_DeclaresExactlyTheOraclesThreeFields()
    {
        string[] actual = [.. typeof(ColumnCalcData)
            .GetProperties()
            .Select(static property => property.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)];

        Assert.Equal(3, actual.Length);
        Assert.Equal(["Flag", "Indexes", "VarIndexes"], actual);
    }

    /// <summary>
    /// The preserved constants, spelled exactly as the oracle spells them.
    /// </summary>
    /// <remarks>
    /// These identifiers travel in serialized payloads, log records and characterization recordings, so
    /// a rename would silently invalidate every stored comparison. The root .editorconfig suppresses the
    /// naming analyzers for this one file precisely so the spellings can be kept.
    /// </remarks>
    [Fact]
    public void PreservedConstants_KeepTheirLegacyValuesAndSpellings()
    {
        Assert.Equal("_columnexp", ColumnExpressionEngine.DWOSUFFIX);
        Assert.Equal(";", ColumnExpressionEngine.TRAILER);
        Assert.Equal("$", ColumnExpressionEngine.MACRO_FLAG);
        Assert.Equal("@", ColumnExpressionEngine.MACRO_CONTEXT);
        Assert.Equal(0L, ColumnExpressionEngine.MACRO_PHASE_NONE);
        Assert.Equal(1L, ColumnExpressionEngine.MACRO_PHASE_FIND);
        Assert.Equal(2L, ColumnExpressionEngine.MACRO_PHASE_PARSE);

        // The tri-state calculation cache is its own alphabet - a FIFTH one in this system that shares
        // the numerals 0, 1 and 2 with RetCode, VetoResult, EventBroker.OnException and the item change
        // protocol. It is modelled as its own type so it cannot be mapped onto any of them.
        Assert.Equal(0L, (long)ColumnCalcFlag.CLC_UNKNOWN);
        Assert.Equal(1L, (long)ColumnCalcFlag.CLC_YES);
        Assert.Equal(2L, (long)ColumnCalcFlag.CLC_NO);

        // Consumed, never redeclared: these two pairs live on the sibling files that own them.
        Assert.Equal(0L, ExpressionVariableEnvironment.VAR_LOCAL);
        Assert.Equal(1L, ExpressionVariableEnvironment.VAR_FOREIGN);
        Assert.Same(string.Empty, MacroSentinels.FUNC_VAR);
        Assert.Empty(MacroSentinels.FUNC_VAR);
        Assert.Equal("Invoke", MacroSentinels.FUNC_INVOKE);
    }

    /// <summary>
    /// <c>_of_calcitem</c> is declared PUBLIC while keeping its private-convention underscore prefix
    /// [:L142]. AAP section 0.6.2.4 calls this "an anomaly to carry rather than tidy".
    /// </summary>
    [Fact]
    public void CalcItemEntryPoint_IsPublicDespiteItsUnderscorePrefix_IsPreservedDefect()
    {
        System.Reflection.MethodInfo? method = typeof(ColumnExpressionEngine)
            .GetMethod("_of_calcitem", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(method);
        Assert.True(method.IsPublic);
        Assert.Equal(typeof(ValueTask<bool>), method.ReturnType);
    }

    // ==============================================================================================
    //  6. DYNAMIC INDIRECT - the FUNC_VAR sentinel, `$$('name')`
    // ==============================================================================================

    /// <summary>
    /// The specification's section 4 example verbatim: <c>$$(if(n2 &gt; n3,'num1','num2'))</c> binds a
    /// column to whichever of two variables the CURRENT ROW's data selects.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS WHY <c>FUNC_VAR = ""</c> EXISTS. An empty function name with exactly one argument models
    /// a bare variable whose NAME is itself computed, and reading the empty sentinel as an inert
    /// placeholder is the natural mistake - it is the entire encoding of C-04's
    /// <c>EXPANSION_MODE_DYNAMIC_INDIRECT</c>.
    /// </para>
    /// <para>
    /// THE BINDING CANNOT BE RESOLVED AT PARSE TIME, which is the whole point: the name comes out of a
    /// DataWindow expression over two columns, so it can differ per row and change when the data does.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task DynamicIndirect_SelectsTheVariableFromTheRowsData_SpecificationSection4()
    {
        ColumnExpressionEngine engine = CreateEngine(out FakeDataWindowHost host);

        Assert.Equal(RetCode.OK, engine.AddVarExp(ThisMonth, "5"));
        Assert.Equal(RetCode.OK, engine.AddVar(LastMonth, 0L));
        Assert.Equal(RetCode.OK, engine.AddVar(NextMonth, 10L));

        Assert.Equal(RetCode.OK, engine.AddVarExp("num1", "$$" + LastMonth + " + $" + ThisMonth));
        Assert.Equal(RetCode.OK, engine.AddVarExp("num2", "$$" + NextMonth + " + $" + ThisMonth));
        Assert.Equal(RetCode.OK, await engine.SetVarAsync(LastMonth, 1L, false, Ct));

        Assert.True(engine.AddExp("n1", "$$(if(n2 > n3 ,'num1','num2'))") > 0);

        // n2 > n3 selects num1, which is 1 + 5.
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("n2").Id, 9m);
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("n3").Id, 2m);
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(6m, host.GetItemDecimal(1L, "n1"));

        // n2 <= n3 selects num2, which is 10 + 5. Same expression, same row, different binding.
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("n2").Id, 2m);
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("n3").Id, 9m);
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(15m, host.GetItemDecimal(1L, "n1"));
    }

    /// <summary>
    /// An indirect reference naming a variable that does not exist is reported at the preprocess site,
    /// not at the parse site, because the name only exists at calculation time.
    /// </summary>
    [Fact]
    public async Task DynamicIndirect_UndefinedName_ReportsAtThePreprocessSite()
    {
        ColumnExpressionEngine engine = CreateEngine(out FakeDataWindowHost host);

        Assert.True(engine.AddExp("n1", "$$('nosuchvariable')") > 0);
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));

        Assert.NotNull(engine.LastError);
        Assert.Equal(ExpressionErrorSite.PreprocessMacroVariableUndefined, engine.LastError.Site);
        Assert.Equal(0m, host.GetItemDecimal(1L, "n1"));
    }

    /// <summary>
    /// A dynamic-indirect reference resolves a FOREIGN variable through its owning peer, which the
    /// direct-reference path also does - so the indirect form is not a second, weaker resolution route.
    /// </summary>
    [Fact]
    public async Task DynamicIndirect_ResolvesAForeignVariableThroughItsPeer()
    {
        ExpressionSession session = NewSession();
        FakeDataWindowHost sourceHost = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        FakeDataWindowHost targetHost = FakeDataWindowFixtures.CreateColumnExpressionFixture();

        ColumnExpressionEngine source = CreateEngineOver(sourceHost, session: session);
        ColumnExpressionEngine target = CreateEngineOver(targetHost, session: session);

        // docs/n_cst_dwsvc_columnexp.md section 1: define on the source, reference from the target.
        Assert.Equal(RetCode.OK, source.AddVar("外部变量名", 123L));
        Assert.Equal(RetCode.OK, target.AddForeignVar("外部变量名", source));

        Assert.True(target.AddExp("n1", "$$('外部变量名')") > 0);
        Assert.Equal(RetCode.OK, await target.CalcAsync(1L, Ct));
        Assert.Equal(123m, targetHost.GetItemDecimal(1L, "n1"));
    }

    // ==============================================================================================
    //  7. THE `@` SIGIL - a grammar branch that appears in NO specification
    // ==============================================================================================

    /// <summary>
    /// A bare <c>@name</c> resolves a COLUMN out of the context DataWindow's row - not a variable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE SIGIL IS DOCUMENTED NOWHERE OUTSIDE THE ORACLE'S OWN COMMENT BLOCK [:L1220-L1222], and not at
    /// all in docs/n_cst_dwsvc_columnexp.md or the AAP. An implementation built from the documentation
    /// alone would drop the whole branch, and <c>vardata.isctx</c> would look like a vestigial field.
    /// </para>
    /// <para>
    /// RESOLUTION IS BY COLUMN NAME AND GOES THROUGH <c>_of_GetItemExpValue</c> [:L2185], so the answer
    /// is the cell rendered as an expression literal. With no peer supplying a context, the context IS
    /// this engine and this row, which is what makes a self referential <c>@name</c> behave like a plain
    /// column reference.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ContextColumnReference_ResolvesAColumnOutOfTheContextRow()
    {
        ExpressionSession session = NewSession();
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host, session: session);

        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("n2").Id, 7m);

        Assert.True(engine.AddExp("n1", "@n2 + 1") > 0);

        // The reference is RETAINED - a context reference is never statically expanded.
        Assert.Equal("@n2 + 1", engine.GetExp("n1"));
        Assert.Equal(1, engine.Expressions[0].Vars.UpperBound);
        Assert.True(engine.Expressions[0].Vars[1].IsCtx);
        Assert.False(engine.Expressions[0].Vars[1].IsMacro);

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(8m, host.GetItemDecimal(1L, "n1"));
    }

    /// <summary>
    /// <c>@$name</c> - a context variable with a SINGLE sigil - is refused, because a context variable
    /// cannot be expanded statically [:L1417].
    /// </summary>
    [Fact]
    public async Task ContextVariableReference_StaticForm_IsRefused()
    {
        ExpressionSession session = NewSession();
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host, session: session);

        Assert.Equal(RetCode.OK, engine.AddVar("v", 3L));

        // AddExp answers E_INVALID_ARGUMENT for any failed parse, so the site carries the specific reason.
        Assert.Equal((int)RetCode.E_INVALID_ARGUMENT, engine.AddExp("n1", "@$v + 1"));
        Assert.NotNull(engine.LastError);
        Assert.Equal(
            ExpressionErrorSite.FunctionMacroContextVariableStaticExpansionUnsupported,
            engine.LastError.Site);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, engine.LastError.ReturnCode);

        await Task.CompletedTask;
    }

    /// <summary>
    /// <c>@$$name</c> - a context variable with a DOUBLED sigil - is accepted, retained as a reference,
    /// and resolved against the context service at calculation time.
    /// </summary>
    [Fact]
    public async Task ContextVariableReference_DynamicForm_ResolvesAgainstTheContextService()
    {
        ExpressionSession session = NewSession();
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host, session: session);

        Assert.Equal(RetCode.OK, engine.AddVar("v", 3L));
        Assert.True(engine.AddExp("n1", "@$$v + 1") > 0);

        Assert.Equal("@$$v + 1", engine.GetExp("n1"));
        Assert.Equal(1, engine.Expressions[0].Vars.UpperBound);
        Assert.True(engine.Expressions[0].Vars[1].IsCtx);
        Assert.True(engine.Expressions[0].Vars[1].IsMacro);
        Assert.Equal("v", engine.Expressions[0].Vars[1].Name);

        // The name extraction has to skip the `@` when computing the name but keep it in the full token:
        // that is the `nMacPos++ ... nMacPos--` shift at :L1405 and :L1411.
        Assert.Equal("@$$v", engine.Expressions[0].Vars[1].FullName);

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(4m, host.GetItemDecimal(1L, "n1"));
    }

    /// <summary>
    /// A context FUNCTION macro is refused outright [:L1308], for any sigil combination.
    /// </summary>
    [Fact]
    public void ContextFunctionMacro_IsRefused()
    {
        ExpressionSession session = NewSession();
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host, session: session);

        Assert.Equal((int)RetCode.E_INVALID_ARGUMENT, engine.AddExp("n1", "@$$Fn(n2)"));
        Assert.NotNull(engine.LastError);
        Assert.Equal(
            ExpressionErrorSite.FunctionMacroContextReferenceUnsupported,
            engine.LastError.Site);
    }

    /// <summary>
    /// A context reference against a peer resolves against THAT peer's row and columns, which is the
    /// capability the sigil exists for.
    /// </summary>
    [Fact]
    public void ContextColumnReference_AgainstAPeer_ReadsThePeersRow()
    {
        ExpressionSession session = NewSession();
        FakeDataWindowHost peerHost = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();

        ColumnExpressionEngine peer = CreateEngineOver(peerHost, session: session);
        ColumnExpressionEngine engine = CreateEngineOver(host, session: session);

        peerHost.SetBufferValue(DwBuffer.Primary, 2L, peerHost.Column("n2").Id, 42m);

        // `_of_GetItemExpValue` on the peer, at the peer's row 2.
        Assert.Equal("42", peer.GetItemExpValue(2L, "n2"));

        // And the engine reaches it through the session rather than through an object pointer.
        Assert.True(session.IsCoResident(peer.Handle));
        Assert.True(session.IsCoResident(engine.Handle));
        Assert.NotEqual(peer.Handle, engine.Handle);
    }

    // ==============================================================================================
    //  8. THE PARSE ERROR MATRIX - eleven caret bearing sites, and the one that breaks the pattern
    // ==============================================================================================

    /// <summary>
    /// The caret-bearing parse errors, each with the return code its own arm answers.
    /// </summary>
    /// <remarks>
    /// THE POINT OF THE MATRIX IS THE ONE ARM THAT DIFFERS. Ten of these answer
    /// <c>E_INVALID_ARGUMENT</c>; the empty-variable-value arm answers <c>FAILED</c> [:L1431]. That is a
    /// genuine one-arm exception among otherwise uniform parse errors, it is preserved (constraint C-B),
    /// and a table is the only shape that makes the exception visible rather than buried.
    /// </remarks>
    public static TheoryData<string, ExpressionErrorSite, long> ParseErrorCases() => new()
    {
        // `@$v` - a context variable cannot expand statically.
        { "@$v + 1", ExpressionErrorSite.FunctionMacroContextVariableStaticExpansionUnsupported, RetCode.E_INVALID_ARGUMENT },

        // `$nosuch` - a static reference to a variable that was never defined.
        { "$nosuch + 1", ExpressionErrorSite.VariableMacroUndefined, RetCode.E_INVALID_ARGUMENT },

        // `$+` - a sigil with NOTHING before its terminator fails the `nPos - nMacPos - 1 > 0` gate
        // at :L1404 and is the only route to the invalid-name arm at :L1486.
        { "$+ 1", ExpressionErrorSite.VariableMacroInvalidName, RetCode.E_INVALID_ARGUMENT },

        // `$ + 1` by contrast PASSES that gate - the space counts - so the name is the EMPTY string and
        // the failure is an undefined variable rather than an invalid name. One character apart.
        { "$ + 1", ExpressionErrorSite.VariableMacroUndefined, RetCode.E_INVALID_ARGUMENT },

        // An unterminated quote. The caret is the OPENING quote, not the token midpoint. A sigil must
        // be present or :L1258-L1260 early-outs before the scan and the quote is never noticed.
        { "$$v + 'abc", ExpressionErrorSite.VariableMacroQuoteNotClosed, RetCode.E_INVALID_ARGUMENT },

        // `$$(...)` with no argument at all - FUNC_VAR requires exactly one.
        { "$$() + 1", ExpressionErrorSite.FunctionMacroArgumentCountIncorrectByFullName, RetCode.E_INVALID_ARGUMENT },

        // `$$(a,b)` - FUNC_VAR with two arguments, which is one too many.
        { "$$('a','b') + 1", ExpressionErrorSite.FunctionMacroArgumentCountIncorrectByFullName, RetCode.E_INVALID_ARGUMENT },

        // `$$Invoke()` with no argument - the dynamic dispatch sentinel requires at least the name.
        { "$$Invoke() + 1", ExpressionErrorSite.FunctionMacroArgumentCountIncorrectByName, RetCode.E_INVALID_ARGUMENT },

        // `$$Other(...)` - any OTHER doubled-sigil function name is not a built-in at all.
        { "$$Other(n2) + 1", ExpressionErrorSite.FunctionMacroUndefined, RetCode.E_INVALID_ARGUMENT },

        // A context function macro, refused before argument checking.
        { "@$$Fn(n2)", ExpressionErrorSite.FunctionMacroContextReferenceUnsupported, RetCode.E_INVALID_ARGUMENT },

        // An argument list that never closes.
        { "$Fn(n2 + 1", ExpressionErrorSite.FunctionMacroInvalidArgumentList, RetCode.E_INVALID_ARGUMENT },
    };

    /// <summary>Each caret-bearing site reports its own message and its own return code.</summary>
    /// <param name="expression">The expression to bind.</param>
    /// <param name="site">The expected site.</param>
    /// <param name="returnCode">The expected return code carried by the error.</param>
    [Theory]
    [MemberData(nameof(ParseErrorCases))]
    public void ParseErrors_ReportTheirOwnSiteAndReturnCode(
        string expression,
        ExpressionErrorSite site,
        long returnCode)
    {
        ExpressionSession session = NewSession();
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host, session: session);

        Assert.Equal(RetCode.OK, engine.AddVar("v", 3L));

        Assert.Equal((int)RetCode.E_INVALID_ARGUMENT, engine.AddExp("n1", expression));

        Assert.NotNull(engine.LastError);
        Assert.Equal(site, engine.LastError.Site);
        Assert.Equal(returnCode, engine.LastError.ReturnCode);

        // Every one of these carries a caret, and the caret is an index into the TRAILER EXTENDED string.
        Assert.NotNull(engine.LastError.CaretPosition);
        Assert.InRange(engine.LastError.CaretPosition.Value, 1L, expression.Length + 1L);
    }

    /// <summary>
    /// The ONE arm that answers <c>FAILED</c> instead of <c>E_INVALID_ARGUMENT</c>: a static reference to
    /// a variable whose value expression is empty [:L1431]. PRESERVED DEFECT (constraint C-B).
    /// </summary>
    /// <remarks>
    /// A variable can only have an empty value expression if it was defined with one, and
    /// <c>of_AddVarExp</c> refuses an empty expression - so the arm is reached through
    /// <c>of_AddVar(name, "")</c>, whose string overload renders the empty string as a QUOTED empty
    /// literal and therefore does not trip that guard. The asymmetry is real, reachable, and preserved.
    /// </remarks>
    [Fact]
    public void ParseError_EmptyVariableValue_AnswersFailedNotInvalidArgument_IsPreservedDefect()
    {
        ExpressionSession session = NewSession();
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host, session: session);

        // The catalogue is the single source of truth for the per-site return code, and it is what the
        // engine reports. Asserting it here pins the asymmetry independently of how the arm is reached.
        ExpressionParseError error = ParseErrorFormatter.CreateParseError(
            ExpressionErrorSite.VariableMacroValueInvalid,
            "$v + 1;",
            3L,
            "v");

        Assert.Equal(RetCode.FAILED, error.ReturnCode);
        Assert.NotEqual(RetCode.E_INVALID_ARGUMENT, error.ReturnCode);

        // Its immediate neighbours, for contrast: same shape, same caret convention, different code.
        foreach (ExpressionErrorSite neighbour in (ExpressionErrorSite[])
        [
            ExpressionErrorSite.FunctionMacroContextVariableStaticExpansionUnsupported,
            ExpressionErrorSite.VariableMacroUndefined,
            ExpressionErrorSite.VariableMacroForeignStaticExpansionUnsupported,
        ])
        {
            ExpressionParseError sibling = ParseErrorFormatter.CreateParseError(neighbour, "$v + 1;", 3L, "v");
            Assert.Equal(RetCode.E_INVALID_ARGUMENT, sibling.ReturnCode);
        }

        Assert.Equal(0, engine.ExpressionCount);
    }

    /// <summary>
    /// An unclosed quote reports the OPENING QUOTE's position, not the token midpoint every other
    /// caret-bearing site uses [:L1505 passes <c>nQuotePos</c>].
    /// </summary>
    [Fact]
    public void ParseError_UnclosedQuote_CaretIsTheOpeningQuoteNotTheMidpoint()
    {
        ColumnExpressionEngine engine = CreateEngine(out _);

        Assert.Equal(RetCode.OK, engine.AddVar("v", 3L));

        // `$$v + 'abcdefgh` - the quote opens at position 7, well past the midpoint of anything.
        Assert.Equal((int)RetCode.E_INVALID_ARGUMENT, engine.AddExp("n1", "$$v + 'abcdefgh"));

        Assert.NotNull(engine.LastError);
        Assert.Equal(ExpressionErrorSite.VariableMacroQuoteNotClosed, engine.LastError.Site);
        Assert.Equal(7L, engine.LastError.CaretPosition);
    }

    /// <summary>
    /// An expression carrying NEITHER sigil is never scanned at all [:L1258-L1260], so a malformation
    /// that only the scan would notice - an unclosed quote, for instance - passes unreported.
    /// </summary>
    /// <remarks>
    /// THE EARLY OUT IS A DELIBERATE FAST PATH, NOT AN OVERSIGHT, and its consequence is preserved: the
    /// engine is a macro expander, not an expression validator, so an ordinary DataWindow expression is
    /// stored verbatim and whatever is wrong with it surfaces at the evaluator instead. Reporting it here
    /// would reject expressions the legacy accepted.
    /// </remarks>
    [Fact]
    public void ExpressionWithNoSigil_IsStoredVerbatimAndNeverScanned()
    {
        ColumnExpressionEngine engine = CreateEngine(out _);

        // Malformed, and accepted anyway, because there is no `$` and no `@` to scan for.
        int index = engine.AddExp("n1", "'abc + 1");
        Assert.Equal(1, index);

        Assert.Equal("'abc + 1", engine.GetExp("n1"));
        Assert.False(engine.Expressions[0].HasMacro);
        Assert.Null(engine.LastError);
    }

    /// <summary>
    /// A sigil INSIDE a quoted literal is not parsed as a macro [:L1267-L1284].
    /// </summary>
    [Fact]
    public async Task QuotedSigil_IsNotParsedAsAMacro()
    {
        ColumnExpressionEngine engine = CreateEngine(out FakeDataWindowHost host);

        Assert.Equal(RetCode.OK, engine.AddVar("v", 3L));

        // The only `$v` here is inside the literal, so nothing is parsed and nothing is rewritten.
        Assert.True(engine.AddExp("s1", "'$v is unexpanded'") > 0);
        Assert.Equal("'$v is unexpanded'", engine.GetExp("s1"));
        Assert.False(engine.Expressions[0].HasMacro);
        Assert.Equal(0, engine.Expressions[0].Vars.UpperBound);

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal("$v is unexpanded", host.GetItemString(1L, "s1"));
    }

    /// <summary>
    /// But once the SAME token also occurs UNQUOTED, the static rewrite replaces BOTH occurrences -
    /// including the one inside the literal. PRESERVED DEFECT (constraint C-B).
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE PARSER IS QUOTE AWARE AND THE REWRITE IS NOT. Scanning correctly refuses to treat the quoted
    /// sigil as a macro, so no reference is created for it - but the rewrite that follows is
    /// <c>ReplaceAll(sExp, fullName, sVarExp, true, true)</c> [:L1435], a global replacement over the
    /// whole expression with no notion of where the literals are. So the quoted occurrence is rewritten
    /// as collateral, silently changing text the author intended as data.
    /// </para>
    /// <para>
    /// The whole-token flag limits the damage but cannot prevent it, because the quoted occurrence here is
    /// a whole token by any measure. Only a quote-aware replacement would fix it, and that would change
    /// output a characterization recording captures.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task StaticExpansion_RewritesInsideQuotedLiteralsToo_IsPreservedDefect()
    {
        ColumnExpressionEngine engine = CreateEngine(out FakeDataWindowHost host);

        Assert.Equal(RetCode.OK, engine.AddVar("v", 3L));

        // Authored intent: the literal shows the variable's NAME and the call shows its VALUE.
        Assert.True(engine.AddExp("s1", "'$v is ' + string($v)") > 0);

        // Actual: the literal now shows the value as well, because the rewrite reached inside it.
        Assert.Equal("'(3) is ' + string((3))", engine.GetExp("s1"));

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal("(3) is 3", host.GetItemString(1L, "s1"));
    }

    /// <summary>
    /// The 28 messages are hardcoded Chinese and are NOT routed through localization, unlike the
    /// equivalent messages in the dwsvc, rowselect and contextmenu services which are. PRESERVED
    /// INCONSISTENCY (constraint C-B).
    /// </summary>
    /// <remarks>
    /// THE STRUCTURAL GUARANTEE IS THE ABSENCE OF A REFERENCE. ColumnExpressionEngine.cs takes no
    /// dependency on the Localization library at all, so no future edit can route one of these through
    /// it by accident. This test pins the observable half: every error the engine raises reports itself
    /// as unlocalized and carries no localization category.
    /// </remarks>
    [Fact]
    public void RaisedErrors_AreNeverLocalized_IsPreservedInconsistency()
    {
        ColumnExpressionEngine engine = CreateEngine(out _);

        Assert.Equal(RetCode.OK, engine.AddVar("v", 3L));

        foreach (string bad in (string[])["$nosuch + 1", "$+ 1", "$$v + 'abc", "$$Other(n2) + 1"])
        {
            Assert.Equal((int)RetCode.E_INVALID_ARGUMENT, engine.AddExp("n1", bad));
        }

        Assert.Equal(4, engine.RaisedErrorCount);
        Assert.Equal(4, engine.Errors.Length);

        foreach (ExpressionParseError error in engine.Errors)
        {
            Assert.False(error.Localized);
            Assert.Equal(0L, error.LocalizationCategory);

            // The message text is the oracle's own Chinese, carried verbatim.
            Assert.Contains("失败", error.Text, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The error history is bounded and the OLDEST is dropped, so a caller diagnosing a cascade sees the
    /// most recent failures rather than a prefix of them.
    /// </summary>
    [Fact]
    public void RaisedErrors_AreBoundedAndDropTheOldest()
    {
        ColumnExpressionEngine engine = CreateEngine(out _);

        for (int attempt = 0; attempt < ColumnExpressionEngine.MaxRetainedErrors + 5; attempt++)
        {
            Assert.Equal((int)RetCode.E_INVALID_ARGUMENT, engine.AddExp("n1", "$nosuch + 1"));
        }

        Assert.Equal(ColumnExpressionEngine.MaxRetainedErrors + 5, engine.RaisedErrorCount);
        Assert.Equal(ColumnExpressionEngine.MaxRetainedErrors, engine.Errors.Length);

        engine.ClearErrors();
        Assert.Null(engine.LastError);
        Assert.Equal(0, engine.RaisedErrorCount);
        Assert.Empty(engine.Errors);
    }

    // ==============================================================================================
    //  9. THE REVERSE DEPENDENCY INDEX, THE TRI-STATE CACHE, AND ONE BASED GROWTH
    // ==============================================================================================

    /// <summary>
    /// <c>columndata</c> is a REVERSE index from a column to the expressions that depend on it, and the
    /// <c>CLC_*</c> flag caches whether a column has any dependents at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE CACHE IS PESSIMISTIC FIRST [:L239]. The flag is set to <c>CLC_NO</c> BEFORE the walk and only
    /// promoted to <c>CLC_YES</c> by a match, so a column that matches nothing is cached as NO and is
    /// never walked again - which is the optimisation, and also why the cache has to be invalidated
    /// wholesale when the expression set changes rather than incrementally.
    /// </para>
    /// <para>
    /// THE GROWTH IS ONE BASED [:L234-L236] and this is AAP section 0.8.6 R9's named hazard: the entry for
    /// column id N lives at index N, so a zero-based list would silently shift every dependent by one and
    /// recalculate the wrong expression - a defect indistinguishable from a behavioural regression.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ReverseDependencyIndex_IsOneBasedByColumnIdAndCachesPessimistically()
    {
        ColumnExpressionEngine engine = CreateEngine(out FakeDataWindowHost host);

        long n2Id = host.Column("n2").Id;
        long n3Id = host.Column("n3").Id;

        Assert.True(engine.AddExp("n1", "n2 * 2") > 0);
        Assert.Equal(RetCode.OK, engine.SetRelativeColumn("n1", "n2"));

        // Nothing is resolved until a column change asks the question.
        Assert.All(engine.ColumnCalcStates, static state => Assert.Equal(ColumnCalcFlag.CLC_UNKNOWN, state.Flag));

        host.SetBufferValue(DwBuffer.Primary, 1L, n2Id, 4m);
        await engine.OnDoItemChangedAsync(1L, "n2", n2Id, false, Ct);

        // n2 HAS a dependent, so its slot is promoted to YES and holds the expression's index.
        ImmutableArray<ColumnCalcData> states = engine.ColumnCalcStates;
        Assert.Equal(ColumnCalcFlag.CLC_YES, states[(int)n2Id - 1].Flag);
        Assert.Equal(1, states[(int)n2Id - 1].Indexes.UpperBound);
        Assert.Equal(1, states[(int)n2Id - 1].Indexes[1]);

        // And the expression actually ran.
        Assert.Equal(8m, host.GetItemDecimal(1L, "n1"));

        // n3 has no dependent at all, so its slot is cached as NO - the pessimistic default, retained.
        await engine.OnDoItemChangedAsync(1L, "n3", n3Id, false, Ct);
        states = engine.ColumnCalcStates;
        Assert.Equal(ColumnCalcFlag.CLC_NO, states[(int)n3Id - 1].Flag);
        Assert.Equal(0, states[(int)n3Id - 1].Indexes.UpperBound);
    }

    /// <summary>
    /// Adding an expression invalidates the whole cache, because a column previously cached as having no
    /// dependents may now have one [:L1560 calls <c>_of_ResetColumns</c>].
    /// </summary>
    [Fact]
    public async Task AddingAnExpression_InvalidatesTheWholeColumnCache()
    {
        ColumnExpressionEngine engine = CreateEngine(out FakeDataWindowHost host);

        long n3Id = host.Column("n3").Id;

        Assert.True(engine.AddExp("n1", "n2 * 2") > 0);
        Assert.Equal(RetCode.OK, engine.SetRelativeColumn("n1", "n2"));

        // Resolve n3 as having no dependents.
        await engine.OnDoItemChangedAsync(1L, "n3", n3Id, false, Ct);
        Assert.Equal(ColumnCalcFlag.CLC_NO, engine.ColumnCalcStates[(int)n3Id - 1].Flag);

        // Now give it one. Without the reset, the stale NO would suppress the new dependency for good.
        Assert.True(engine.AddExp("s1", "string(n3)") > 0);
        Assert.Equal(RetCode.OK, engine.SetRelativeColumn("s1", "n3"));

        Assert.All(engine.ColumnCalcStates, static state => Assert.Equal(ColumnCalcFlag.CLC_UNKNOWN, state.Flag));

        host.SetBufferValue(DwBuffer.Primary, 1L, n3Id, 12m);
        await engine.OnDoItemChangedAsync(1L, "n3", n3Id, false, Ct);
        Assert.Equal(ColumnCalcFlag.CLC_YES, engine.ColumnCalcStates[(int)n3Id - 1].Flag);
        Assert.Equal("12", host.GetItemString(1L, "s1"));
    }

    /// <summary>
    /// <c>of_SetAlwaysCalc</c> short-circuits every dependency test [:L244-L245], so the expression runs
    /// on ANY column change.
    /// </summary>
    [Fact]
    public async Task AlwaysCalc_RunsOnAnyColumnChange()
    {
        ColumnExpressionEngine engine = CreateEngine(out FakeDataWindowHost host);

        Assert.True(engine.AddExp("n1", "n2 + n3") > 0);
        Assert.Equal(RetCode.OK, engine.SetAlwaysCalc("n1", true));
        Assert.True(engine.Expressions[0].AlwaysCalc);

        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("n2").Id, 2m);
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("n3").Id, 5m);

        // s1 has nothing whatever to do with n1, and n1 recalculates anyway.
        await engine.OnDoItemChangedAsync(1L, "s1", host.Column("s1").Id, false, Ct);
        Assert.Equal(7m, host.GetItemDecimal(1L, "n1"));
    }

    /// <summary>
    /// <c>of_SetRelativeInputColumn</c> fires ONLY on user input; <c>of_SetRelativeColumn</c> fires on any
    /// change. The <c>frominput</c> flag is what tells them apart
    /// [docs/n_cst_dwsvc_columnexp.md:L162-L163].
    /// </summary>
    [Fact]
    public async Task RelativeInputColumn_FiresOnlyForUserInput()
    {
        ColumnExpressionEngine engine = CreateEngine(out FakeDataWindowHost host);

        Assert.True(engine.AddExp("n1", "n2 * 3") > 0);
        Assert.Equal(RetCode.OK, engine.SetRelativeInputColumn("n1", "n2"));
        Assert.Equal(1, engine.Expressions[0].RelativeInputColIds.UpperBound);

        long n2Id = host.Column("n2").Id;
        host.SetBufferValue(DwBuffer.Primary, 1L, n2Id, 4m);

        // A programmatic change: frominput false, so the expression does NOT run.
        await engine.OnDoItemChangedAsync(1L, "n2", n2Id, false, Ct);
        Assert.Equal(0m, host.GetItemDecimal(1L, "n1"));

        // The same change reported as user input: now it runs.
        await engine.OnDoItemChangedAsync(1L, "n2", n2Id, true, Ct);
        Assert.Equal(12m, host.GetItemDecimal(1L, "n1"));
    }

    /// <summary>
    /// A row pass skips input-triggered expressions unless <c>force</c> is set [:L1170-L1172], because a
    /// row recalculation is not user input into the bound column.
    /// </summary>
    [Fact]
    public async Task RowCalculation_SkipsInputTriggeredExpressionsUnlessForced()
    {
        ColumnExpressionEngine engine = CreateEngine(out FakeDataWindowHost host);

        Assert.True(engine.AddExp("n1", "n2 * 3") > 0);
        Assert.Equal(RetCode.OK, engine.SetRelativeInputColumn("n1", "n2"));

        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("n2").Id, 4m);

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(0m, host.GetItemDecimal(1L, "n1"));

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, true, Ct));
        Assert.Equal(12m, host.GetItemDecimal(1L, "n1"));
    }

    /// <summary>
    /// The plural and singular setters, by index and by name, are four spellings of one operation, and the
    /// oracle publishes all four [:L124-L205].
    /// </summary>
    [Fact]
    public void RelativeColumnSetters_AllFourSpellingsAgree()
    {
        ColumnExpressionEngine engine = CreateEngine(out FakeDataWindowHost host);

        Assert.True(engine.AddExp("n1", "n2 + n3") > 0);

        Assert.Equal(RetCode.OK, engine.SetRelativeColumns(1, new[] { "n2", "n3" }));
        Assert.Equal(2, engine.Expressions[0].RelativeColIds.UpperBound);
        Assert.Equal(host.Column("n2").Id, engine.Expressions[0].RelativeColIds[1]);
        Assert.Equal(host.Column("n3").Id, engine.Expressions[0].RelativeColIds[2]);

        // The singular form REPLACES the list rather than appending to it.
        Assert.Equal(RetCode.OK, engine.SetRelativeColumn("n1", "n3"));
        Assert.Equal(1, engine.Expressions[0].RelativeColIds.UpperBound);
        Assert.Equal(host.Column("n3").Id, engine.Expressions[0].RelativeColIds[1]);

        Assert.Equal(RetCode.OK, engine.SetRelativeColumns("n1", new[] { "n2" }));
        Assert.Equal(host.Column("n2").Id, engine.Expressions[0].RelativeColIds[1]);

        Assert.Equal(RetCode.OK, engine.SetRelativeInputColumns(1, new[] { "n2", "n3" }));
        Assert.Equal(2, engine.Expressions[0].RelativeInputColIds.UpperBound);
        Assert.Equal(RetCode.OK, engine.SetRelativeInputColumn(1, "n2"));
        Assert.Equal(1, engine.Expressions[0].RelativeInputColIds.UpperBound);
        Assert.Equal(RetCode.OK, engine.SetRelativeInputColumns("n1", new[] { "n3" }));
        Assert.Equal(host.Column("n3").Id, engine.Expressions[0].RelativeInputColIds[1]);

        // An unknown column name cannot be resolved to an identifier.
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, engine.SetRelativeColumn("n1", "nosuchcolumn"));

        // Neither can an unknown expression.
        Assert.Equal(RetCode.E_OUT_OF_BOUND, engine.SetRelativeColumns(99, new[] { "n2" }));
    }

    /// <summary>
    /// The three per-expression flag setters, each by index and by name.
    /// </summary>
    [Fact]
    public void FlagSetters_AreAvailableByIndexAndByName()
    {
        ColumnExpressionEngine engine = CreateEngine(out _);

        Assert.True(engine.AddExp("n1", "n2 + n3") > 0);

        Assert.Equal(RetCode.OK, engine.SetAlwaysCalc(1, true));
        Assert.True(engine.Expressions[0].AlwaysCalc);
        Assert.Equal(RetCode.OK, engine.SetAlwaysCalc("n1", false));
        Assert.False(engine.Expressions[0].AlwaysCalc);

        Assert.Equal(RetCode.OK, engine.SetRecursive(1, true));
        Assert.True(engine.Expressions[0].Recursive);
        Assert.Equal(RetCode.OK, engine.SetRecursive("n1", false));
        Assert.False(engine.Expressions[0].Recursive);

        Assert.Equal(RetCode.OK, engine.SetTriggerEvent(1, true));
        Assert.True(engine.Expressions[0].TriggerEvent);
        Assert.Equal(RetCode.OK, engine.SetTriggerEvent("n1", false));
        Assert.False(engine.Expressions[0].TriggerEvent);

        // Every one of the six answers E_OUT_OF_BOUND for an index that names no expression.
        Assert.Equal(RetCode.E_OUT_OF_BOUND, engine.SetAlwaysCalc(0, true));
        Assert.Equal(RetCode.E_OUT_OF_BOUND, engine.SetRecursive(0, true));
        Assert.Equal(RetCode.E_OUT_OF_BOUND, engine.SetTriggerEvent(0, true));
        Assert.Equal(RetCode.E_OUT_OF_BOUND, engine.SetAlwaysCalc("nosuchcolumn", true));
    }

    // ==============================================================================================
    //  10. dupExps - MULTIPLE EXPRESSIONS ON ONE COLUMN, SELECTED BY WHICH COLUMN CHANGED
    // ==============================================================================================

    /// <summary>
    /// Two expressions bound to the SAME column form a duplicate group, and which one runs is decided by
    /// which column changed [docs/n_cst_dwsvc_columnexp.md:L8].
    /// </summary>
    /// <remarks>
    /// EACH MEMBER CARRIES ITS RIVALS AND NOT ITSELF [:L487 and :L1572 both <c>continue</c> on the
    /// self comparison], which is what lets one member clear the dirty flags of all the others once it has
    /// produced a value [:L2023, :L2059] without immediately clearing its own.
    /// </remarks>
    [Fact]
    public async Task DuplicateExpressions_OnOneColumn_AreSelectedByWhichColumnChanged()
    {
        ColumnExpressionEngine engine = CreateEngine(out FakeDataWindowHost host);

        int first = engine.AddExp("n1", "n2 * 10");
        int second = engine.AddExp("n1", "n3 * 100");
        Assert.Equal(1, first);
        Assert.Equal(2, second);

        Assert.Equal(RetCode.OK, engine.SetRelativeColumn(first, "n2"));
        Assert.Equal(RetCode.OK, engine.SetRelativeColumn(second, "n3"));

        // Each member carries its RIVALS - so for a group of two, exactly the other one.
        Assert.Equal([2L], engine.Expressions[0].DupExps.Items);
        Assert.Equal([1L], engine.Expressions[1].DupExps.Items);

        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("n2").Id, 3m);
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("n3").Id, 7m);

        // n2 changed, so the FIRST expression is the one that runs: 3 * 10.
        await engine.OnDoItemChangedAsync(1L, "n2", host.Column("n2").Id, false, Ct);
        Assert.Equal(30m, host.GetItemDecimal(1L, "n1"));

        // n3 changed, so the SECOND runs: 7 * 100. Same column, same row, different expression.
        await engine.OnDoItemChangedAsync(1L, "n3", host.Column("n3").Id, false, Ct);
        Assert.Equal(700m, host.GetItemDecimal(1L, "n1"));
    }

    /// <summary>
    /// A single expression on a column has an EMPTY duplicate group, not a group of one, because the group
    /// only exists to coordinate rivals [:L487-L493 requires a count above one].
    /// </summary>
    [Fact]
    public void SingleExpression_HasNoDuplicateGroup()
    {
        ColumnExpressionEngine engine = CreateEngine(out _);

        Assert.True(engine.AddExp("n1", "n2 * 10") > 0);
        Assert.Equal(0, engine.Expressions[0].DupExps.UpperBound);
    }

    /// <summary>
    /// Removing one member of a two-member duplicate group leaves the survivor's group list STALE, still
    /// naming an index that no longer exists. PRESERVED DEFECT (constraint C-B).
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE REBUILD IS GATED ON THERE STILL BEING RIVALS [:L483 and :L1567 both require a count above one].
    /// A survivor whose only rival was just removed therefore falls through the gate with the list it
    /// already had, and <c>of_remove</c> copies survivors WHOLE [:L474] rather than resetting their group,
    /// so nothing clears it.
    /// </para>
    /// <para>
    /// It is latent rather than harmful: the group is only read to clear other members' dirty flags
    /// [:L2023, :L2059], and clearing a flag on a nonexistent index is a bounds-guarded no-op in the port.
    /// It is asserted here so that the guard cannot be removed as apparently redundant.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task RemovingADuplicate_LeavesTheSurvivorsGroupStale_IsPreservedDefect()
    {
        ColumnExpressionEngine engine = CreateEngine(out FakeDataWindowHost host);

        Assert.Equal(1, engine.AddExp("n1", "n2 * 10"));
        Assert.Equal(2, engine.AddExp("n1", "n3 * 100"));
        Assert.Equal([2L], engine.Expressions[0].DupExps.Items);

        Assert.Equal(RetCode.OK, engine.Remove(2));
        Assert.Equal(1, engine.ExpressionCount);

        // Still naming index 2, which no longer exists.
        Assert.Equal([2L], engine.Expressions[0].DupExps.Items);

        // And calculation is unharmed, which is the reason it went unnoticed.
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("n2").Id, 3m);
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(30m, host.GetItemDecimal(1L, "n1"));
    }

    // ==============================================================================================
    //  11. THE EXPRESSION REGISTRY - add, set, get, remove, remove all, find
    // ==============================================================================================

    /// <summary>The registry operations, and the codes each answers for a bad argument.</summary>
    [Fact]
    public async Task Registry_AddSetGetRemoveAndFind()
    {
        ColumnExpressionEngine engine = CreateEngine(out _);

        // The name is normalised to lower case and trimmed [:L1538], and every lookup compares against
        // that normalised form.
        Assert.Equal(1, engine.AddExp("  N1  ", "n2 + 1"));
        Assert.Equal("n1", engine.Expressions[0].Name);
        Assert.Equal(1, engine.FindExpIndex("N1"));
        Assert.Equal(1, engine.FindExpIndex(" n1 "));
        Assert.Equal(0, engine.FindExpIndex("nosuchcolumn"));

        Assert.Equal("n2 + 1", engine.GetExp(1));
        Assert.Equal("n2 + 1", engine.GetExp("n1"));
        Assert.Equal(string.Empty, engine.GetExp(99));
        Assert.Equal(string.Empty, engine.GetExp("nosuchcolumn"));

        Assert.Equal(RetCode.OK, await engine.SetExpAsync(1, "n3 + 2", Ct));
        Assert.Equal("n3 + 2", engine.GetExp(1));
        Assert.Equal(RetCode.OK, await engine.SetExpAsync("n1", "n3 + 3", Ct));
        Assert.Equal("n3 + 3", engine.GetExp(1));

        // Both empty-argument guards, and both bad-index guards.
        Assert.Equal((int)RetCode.E_INVALID_ARGUMENT, engine.AddExp(null, "n2"));
        Assert.Equal((int)RetCode.E_INVALID_ARGUMENT, engine.AddExp("n1", string.Empty));
        Assert.Equal((int)RetCode.E_INVALID_ARGUMENT, engine.AddExp("nosuchcolumn", "1"));
        Assert.Equal(RetCode.E_OUT_OF_BOUND, await engine.SetExpAsync(99, "1", Ct));
        Assert.Equal(RetCode.E_OUT_OF_BOUND, engine.Remove(99));
        Assert.Equal(RetCode.E_OUT_OF_BOUND, engine.Remove("nosuchcolumn"));

        Assert.Equal(RetCode.OK, engine.Remove("n1"));
        Assert.Equal(0, engine.ExpressionCount);

        Assert.Equal(1, engine.AddExp("n1", "1"));
        Assert.Equal(2, engine.AddExp("n2", "2"));
        Assert.Equal(RetCode.OK, engine.RemoveAll());
        Assert.Equal(0, engine.ExpressionCount);
        Assert.Empty(engine.Expressions);
    }

    /// <summary>
    /// <c>of_SetExp</c> on a column with no expression yet ADDS one [:L1655 region], so the setter is also
    /// an upsert - which is how every specification example uses it.
    /// </summary>
    [Fact]
    public async Task SetExp_OnAnUnboundColumn_AddsTheExpression()
    {
        ColumnExpressionEngine engine = CreateEngine(out _);

        Assert.Equal(0, engine.ExpressionCount);
        Assert.Equal(RetCode.OK, await engine.SetExpAsync("n1", "n2 + 1", Ct));
        Assert.Equal(1, engine.ExpressionCount);
        Assert.Equal("n2 + 1", engine.GetExp("n1"));
    }

    /// <summary>
    /// <c>of_SetExp</c> compares the RAW incoming text against the stored POST REWRITE text, so re-setting
    /// the very same macro expression is never recognised as unchanged. PRESERVED DEFECT (constraint C-B).
    /// </summary>
    /// <remarks>
    /// The comparison at :L1655 is against <c>ColExpDatas[index].exp</c>, which the parser already
    /// rewrote. For a plain expression with no macro the two are identical and the early-out works; for a
    /// macro expression the stored text no longer resembles what the caller passed, so the setter always
    /// does the full work. Harmless, and observable in the binding being rebuilt.
    /// </remarks>
    [Fact]
    public async Task SetExp_ComparesRawAgainstRewrittenText_IsPreservedDefect()
    {
        ColumnExpressionEngine engine = CreateEngine(out _);

        Assert.Equal(RetCode.OK, engine.AddVar("v", 3L));
        Assert.True(engine.AddExp("n1", "$v + 1") > 0);
        Assert.Equal("(3) + 1", engine.GetExp("n1"));

        // Re-setting the identical source text is accepted and re-parsed, because "(3) + 1" != "$v + 1".
        Assert.Equal(RetCode.OK, await engine.SetExpAsync("n1", "$v + 1", Ct));
        Assert.Equal("(3) + 1", engine.GetExp("n1"));

        // The unexpanded source is still recoverable, which is what C-04's payload carries.
        Assert.Equal("$v + 1", engine.GetSourceExp(1));

        // A plain expression, by contrast, stores exactly what was passed, so the texts DO match.
        Assert.True(engine.AddExp("n2", "n3 + 1") > 0);
        Assert.Equal("n3 + 1", engine.GetExp("n2"));
        Assert.Equal(RetCode.OK, await engine.SetExpAsync("n2", "n3 + 1", Ct));
        Assert.Equal("n3 + 1", engine.GetExp("n2"));
    }

    /// <summary>
    /// The bind-time binding carries all three parts C-04's payload requires: the UNEXPANDED source, the
    /// bind-time snapshot, and the retained references. AAP section 0.6.2.2.
    /// </summary>
    /// <remarks>
    /// TRANSMITTING THE REWRITTEN TEXT ALONE WOULD LOSE BOTH DIRECTIONS: a static binding becomes
    /// indistinguishable from a literal, because its variable's name is gone; and a dynamic binding is
    /// stripped of the environment it needs to resolve. The snapshot records what each variable's value
    /// WAS at bind time for static and dynamic references alike, which is what makes the two comparable.
    /// </remarks>
    [Fact]
    public void Binding_CarriesTheUnexpandedSourceTheSnapshotAndTheReferences()
    {
        ColumnExpressionEngine engine = CreateEngine(out _);

        Assert.Equal(RetCode.OK, engine.AddVar("stat", 10L));
        Assert.Equal(RetCode.OK, engine.AddVar("dyn", 1L));
        Assert.True(engine.AddExp("n1", "$stat + $$dyn") > 0);

        ExpressionBindingSnapshot? binding = engine.GetBinding(1);
        Assert.NotNull(binding);

        // 1. The source, exactly as supplied.
        Assert.Equal("$stat + $$dyn", binding.UnexpandedExp);

        // 2. The bind-time snapshot, for BOTH references - the static one because that is the value that
        //    was baked in, the dynamic one because that is what it WAS.
        Assert.Equal("10", binding.BindTimeSnapshot["stat"]);
        Assert.Equal("1", binding.BindTimeSnapshot["dyn"]);

        // 3. The retained references: only the dynamic one survives the rewrite.
        Assert.Single(binding.Vars);
        Assert.Equal("dyn", binding.Vars[0].Name);
        Assert.True(binding.Vars[0].IsMacro);
        Assert.Empty(binding.Fns);

        // The snapshot is a bind-time record and does NOT move when the variable does.
        Assert.Equal("10", engine.GetBinding(1)!.BindTimeSnapshot["stat"]);

        // A column with no expression has no binding at all.
        Assert.Null(engine.GetBinding(99));
        Assert.Equal(string.Empty, engine.GetSourceExp(99));
    }


    // ==============================================================================================
    //  12. THE TRACE - gated on #Trace and nothing else, with a payload that has no trailing ">"
    // ==============================================================================================

    /// <summary>
    /// The trace payload: the call stack, the PREPROCESSED expression and the value, emitted only while
    /// <c>#Trace</c> is set [:L751-L758].
    /// </summary>
    /// <remarks>
    /// THE STACK ENDS WITH THE COLUMN NAME AND THEREFORE HAS NO TRAILING DELIMITER [:L753-L757 appends
    /// the DataWindow object's name after the loop]. A payload with a trailing <c>&gt;</c> would be a
    /// different string in every characterization recording, so the absence is asserted rather than
    /// assumed.
    /// </remarks>
    [Fact]
    public async Task Trace_EmitsThePreprocessedExpressionAndHasNoTrailingDelimiter()
    {
        RecordingTraceSink sink = new();
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host, session: NewSession(sink));

        Assert.Equal(RetCode.OK, engine.AddVar("v", 4L));
        Assert.True(engine.AddExp("n1", "$$v + 1") > 0);

        // #Trace is OFF by default [:L98], so nothing is emitted at all.
        Assert.False(engine.Trace);
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Empty(sink.Records);

        Assert.Equal(RetCode.OK, engine.SetTrace(true));
        Assert.True(engine.Trace);

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        ExpressionTraceRecord record = Assert.Single(sink.Records);

        Assert.Equal("test-session", record.SessionId);
        Assert.Equal(1L, record.Row);
        Assert.Equal("n1", record.ColumnName);

        // The stack is the frames joined by ">" and then the column name, so it ends with "n1".
        Assert.Equal("n1", record.Stack);
        Assert.DoesNotContain('>', record.Stack);
        Assert.EndsWith("n1", record.Stack, StringComparison.Ordinal);

        // The PREPROCESSED text, with the dynamic reference already resolved - not the stored text.
        Assert.Equal("(4) + 1", record.Expression);
        Assert.Equal("5", record.Value);

        Assert.Equal(RetCode.OK, engine.SetTrace(false));
        Assert.False(engine.Trace);
    }

    /// <summary>
    /// The <c>"(null)"</c> sentinel is selected by TWO conditions [:L758]: an empty value on a column that
    /// treats empty as null, OR an empty value on any non-string column.
    /// </summary>
    /// <remarks>
    /// A STRING COLUMN WITHOUT <c>NilIsNull</c> THEREFORE TRACES THE EMPTY STRING, not the sentinel - which
    /// is the third case and the one that proves the selection is a conjunction rather than a simple
    /// emptiness test.
    /// </remarks>
    [Fact]
    public async Task Trace_SelectsTheNullSentinelOnBothConditionsAndNeitherElse()
    {
        RecordingTraceSink sink = new();
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host, session: NewSession(sink));
        Assert.Equal(RetCode.OK, engine.SetTrace(true));

        // Condition 2: a NON-STRING column with an empty value.
        Assert.True(engine.AddExp("n1", "''") > 0);

        // The third case: a STRING column with an empty value and no NilIsNull, which traces "".
        Assert.True(engine.AddExp("s1", "''") > 0);
        Assert.False(engine.Expressions[1].EmptyStringIsNull);

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));

        Assert.Equal(2, sink.Records.Count);
        Assert.Equal("(null)", sink.Records[0].Value);
        Assert.Equal(string.Empty, sink.Records[1].Value);

        // Condition 1: the same STRING column, now declaring NilIsNull, traces the sentinel instead.
        host.Column("s2").SetProperty("edit.NilIsNull", "yes");
        Assert.True(engine.AddExp("s2", "''") > 0);
        Assert.True(engine.Expressions[2].EmptyStringIsNull);

        int before = sink.Records.Count;
        Assert.Equal(RetCode.OK, await engine.CalcAsync(2L, Ct));
        Assert.True(sink.Records.Count > before);
        Assert.Equal("(null)", sink.Records[^1].Value);
    }

    // ==============================================================================================
    //  13. THE THREE SENTINELS - "!" and "?" are REPORTED, "" is a SILENT ABORT
    // ==============================================================================================

    /// <summary>
    /// The evaluator's two failure markers are reported and abort the item [:L760-L763].
    /// </summary>
    [Fact]
    public async Task InvalidExpressionSentinel_IsReportedAndAbortsTheItem()
    {
        ColumnExpressionEngine engine = CreateEngine(out FakeDataWindowHost host);

        // A function the evaluator does not know answers "!".
        Assert.True(engine.AddExp("n1", "nosuchfunction(n2)") > 0);

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));

        Assert.NotNull(engine.LastError);
        Assert.Equal(ExpressionErrorSite.ExpressionError, engine.LastError.Site);
        Assert.Equal(0m, host.GetItemDecimal(1L, "n1"));
    }

    /// <summary>
    /// The empty string is a SILENT ABORT and is never a legitimate value [:L745, :L2183, :L2186, :L2358].
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE THREE SENTINELS ARE THREE DIFFERENT THINGS and conflating any two of them loses information.
    /// <c>"!"</c> means the expression is invalid and <c>"?"</c> means its value could not be determined;
    /// both are REPORTED, so a caller learns why. The empty string means "stop, and say nothing", and it
    /// is what every resolution failure inside preprocessing propagates upward.
    /// </para>
    /// <para>
    /// Here the abort is provoked by a foreign reference whose peer is not co-resident, which the session
    /// refuses with its BLOCKED code - AAP section 0.6.2.3's deliberate narrowing.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AbortSentinel_StopsTheCalculationWithoutWritingAValue()
    {
        Assert.Empty(ColumnExpressionEngine.AbortSentinel);
        Assert.Equal("?", ColumnExpressionEngine.UndeterminedValueSentinel);
        Assert.Equal("!", ColumnExpressionEngine.InvalidExpressionSentinel);

        // A foreign reference whose peer is co-resident but whose variable was never defined on that peer:
        // the reference resolves to no index, and the calculation aborts silently at :L2199-L2201.
        ExpressionSession session = NewSession();
        FakeDataWindowHost sourceHost = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        FakeDataWindowHost targetHost = FakeDataWindowFixtures.CreateColumnExpressionFixture();

        ColumnExpressionEngine source = CreateEngineOver(sourceHost, session: session);
        ColumnExpressionEngine target = CreateEngineOver(targetHost, session: session);

        Assert.Equal(RetCode.OK, source.AddVar("far", 99L));
        Assert.Equal(RetCode.OK, target.AddForeignVar("far", source));
        Assert.True(target.AddExp("n1", "$$far + 1") > 0);

        // With the peer's variable intact the reference resolves and a value IS written.
        Assert.Equal(RetCode.OK, await target.CalcAsync(1L, Ct));
        Assert.Equal(100m, targetHost.GetItemDecimal(1L, "n1"));

        // Remove the peer from the session and the very same expression aborts instead: no value is
        // written, and the refusal is a DEFINED error rather than a silently wrong number.
        target.ClearErrors();
        Assert.True(session.Close());
        Assert.Equal(RetCode.OK, await target.CalcAsync(2L, Ct));
        Assert.Equal(0m, targetHost.GetItemDecimal(2L, "n1"));
        Assert.NotNull(target.LastError);
    }

    /// <summary>
    /// A foreign reference across TWO SESSIONS is refused at REGISTRATION time, with
    /// <see cref="RetCode.E_VAR_NOT_FOUND"/> - the oracle's own code for a foreign variable it cannot find
    /// on the peer [:L2133].
    /// </summary>
    /// <remarks>
    /// REFUSING EARLY IS THE POINT. AAP section 0.6.2.3 requires a cross-session reference to be BLOCKED
    /// with a defined error rather than silently wrong, and refusing at <c>of_AddForeignVar</c> is strictly
    /// better than refusing at calculation time: the caller learns at the moment it wires the reference up,
    /// while it still has the context to do something about it.
    /// </remarks>
    [Fact]
    public void ForeignVariable_AcrossTwoSessions_IsRefusedAtRegistration()
    {
        FakeDataWindowHost sourceHost = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        FakeDataWindowHost targetHost = FakeDataWindowFixtures.CreateColumnExpressionFixture();

        ColumnExpressionEngine source = CreateEngineOver(sourceHost, session: NewSession());
        ColumnExpressionEngine target = CreateEngineOver(targetHost, session: NewSession());

        Assert.Equal(RetCode.OK, source.AddVar("far", 99L));
        Assert.Equal(RetCode.E_VAR_NOT_FOUND, target.AddForeignVar("far", source));
        Assert.Equal(0, target.FindVarIndex("far"));

        // A null peer is the port's equivalent of the oracle's IsValidObject test at :L2120, and it
        // answers E_INVALID_OBJECT rather than E_INVALID_ARGUMENT - the object is the thing at fault.
        Assert.Equal(RetCode.E_INVALID_OBJECT, target.AddForeignVar("far", null));

        // An empty NAME, by contrast, is a bad argument.
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, target.AddForeignVar(null, source));
    }

    // ==============================================================================================
    //  14. #Enabled - FAILED at four sites, void at two, and onenable always allows
    // ==============================================================================================

    /// <summary>
    /// A disabled engine answers <see cref="RetCode.FAILED"/> from the four calculation entry points
    /// [:L1158, :L1201, :L1976, :L2087].
    /// </summary>
    /// <remarks>
    /// FAILED, NOT PREVENT AND NOT E_NO_SUPPORT. Those would each read as a different thing to a caller -
    /// a veto and an unimplemented capability respectively - and the oracle answers neither.
    /// </remarks>
    [Fact]
    public async Task DisabledEngine_AnswersFailedFromEveryCalculationEntryPoint()
    {
        ColumnExpressionEngine engine = CreateEngine(out FakeDataWindowHost host);

        Assert.True(engine.AddExp("n1", "n2 + 1") > 0);
        engine.SetEnabled(false);
        Assert.False(engine.Enabled);

        Assert.Equal(RetCode.FAILED, await engine.CalcAsync(1L, Ct));
        Assert.Equal(RetCode.FAILED, await engine.CalcAsync(1L, true, Ct));
        Assert.Equal(RetCode.FAILED, await engine.CalcAllAsync(Ct));
        Assert.Equal(RetCode.FAILED, await engine.CalcAllAsync(true, Ct));
        Assert.Equal(RetCode.FAILED, await engine.CalcEmptyAsync(1L, Ct));
        Assert.Equal(RetCode.FAILED, await engine.CalcEmptyAsync(Ct));

        // Nothing was calculated.
        Assert.Equal(0m, host.GetItemDecimal(1L, "n1"));

        // And nothing was reported: a disabled service is not an error condition.
        Assert.Null(engine.LastError);
    }

    /// <summary>
    /// The two EVENT entry points return void, so they answer the same condition by doing nothing
    /// [:L214, :L334]. The asymmetry is structural: an event has no return value to carry a code.
    /// </summary>
    [Fact]
    public async Task DisabledEngine_EventEntryPointsReturnVoidAndDoNothing()
    {
        ColumnExpressionEngine engine = CreateEngine(out FakeDataWindowHost host);

        Assert.True(engine.AddExp("n1", "n2 + 1") > 0);
        Assert.Equal(RetCode.OK, engine.SetAlwaysCalc("n1", true));
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("n2").Id, 5m);

        engine.SetEnabled(false);

        await engine.OnItemChangedAsync(1L, host.DwObject("n2"), Ct);
        await engine.OnVarChangedAsync(1, false, Ct);

        Assert.Equal(0m, host.GetItemDecimal(1L, "n1"));

        // Re-enabled, the very same call now calculates - so the guard was the only thing stopping it.
        engine.SetEnabled(true);
        await engine.OnItemChangedAsync(1L, host.DwObject("n2"), Ct);
        Assert.Equal(6m, host.GetItemDecimal(1L, "n1"));
    }

    /// <summary>
    /// <c>event onenable</c> [:L2428-L2433] has an entirely commented-out body and simply returns 0, so
    /// enable is ALWAYS allowed and no subscription changes occur. PRESERVED as inert (constraint C-B).
    /// </summary>
    [Fact]
    public void OnEnable_AlwaysAllows()
    {
        ColumnExpressionEngine engine = CreateEngine(out _);

        // Both directions, repeatedly, and never refused.
        for (int round = 0; round < 3; round++)
        {
            Assert.Equal(RetCode.OK, engine.SetEnabled(false));
            Assert.False(engine.Enabled);
            Assert.Equal(RetCode.OK, engine.SetEnabled(true));
            Assert.True(engine.Enabled);
        }
    }

    // ==============================================================================================
    //  15. MACRO FUNCTIONS - the inverted channel, both call forms, and the unhandled event
    // ==============================================================================================

    /// <summary>
    /// Direct invocation, <c>$FormatPrice(n2, $精度)</c>, transcribed from
    /// docs/n_cst_dwsvc_columnexp.md section "宏函数" part 1.
    /// </summary>
    /// <remarks>
    /// NOTE THE NESTED STATIC REFERENCE IN THE ARGUMENT LIST. <c>$精度</c> is expanded at bind time inside
    /// the argument, which is why the variable pass has to run BEFORE the function pass [:L2179 before
    /// :L2226] and has to rewrite the stored function tokens as it goes [:L2215-L2223].
    /// </remarks>
    [Fact]
    public async Task MacroFunction_DirectForm_CallsBackIntoTheClient()
    {
        RecordingMacroChannel channel = new();
        channel.Answers["FormatPrice"] = 12.3m;

        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host, channel: channel, session: NewSession());

        Assert.Equal(RetCode.OK, engine.AddVar("精度", 1L));
        Assert.True(engine.AddExp("n1", "$FormatPrice(n2, $精度)") > 0);

        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("n2").Id, 12.345m);

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(12.3m, host.GetItemDecimal(1L, "n1"));

        MacroInvocation invocation = Assert.Single(channel.Invocations);
        Assert.Equal("FormatPrice", invocation.Name);
        Assert.False(invocation.Builtin);
        Assert.Equal(1L, invocation.Row);
        Assert.Equal("n1", invocation.Dwo.Name);
        Assert.Equal("test-session", invocation.SessionId);

        // Two arguments, BOTH already evaluated to values before they crossed the boundary [:L2230].
        Assert.Equal(2, invocation.Arguments.UpperBound);
        Assert.Equal("12.345", invocation.Arguments[1]);
        Assert.Equal("1", invocation.Arguments[2]);
    }

    /// <summary>
    /// Dynamic invocation, <c>$$Invoke($单价格式化, n2, $精度)</c>, from the same specification section
    /// part 2: the function NAME is itself a variable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE FIRST ARGUMENT IS DROPPED BEFORE THE CALL [:L2258-L2284 builds a second array from index 2],
    /// because it named the function rather than being an argument to it. So the client sees the same
    /// invocation it would have seen from the direct form.
    /// </para>
    /// <para>
    /// THE INNER REFERENCES ARE DYNAMIC HERE, WHICH THE SPECIFICATION'S OWN EXAMPLE WRITES AS STATIC. That
    /// example does not survive the scan-truncation defect - substituting a function NAME lengthens the
    /// expression far more than a number does - and the defect is asserted on its own directly below. The
    /// dynamic spelling is the one that works, and it is also the more natural one for a call whose whole
    /// point is late binding.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task MacroFunction_DynamicForm_ResolvesTheNameAndDropsTheFirstArgument()
    {
        RecordingMacroChannel channel = new();
        channel.Answers["FormatPrice"] = 9.9m;

        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host, channel: channel, session: NewSession());

        Assert.Equal(RetCode.OK, engine.AddVar("精度", 1L));
        Assert.Equal(RetCode.OK, engine.AddVar("单价格式化", "FormatPrice"));
        Assert.True(engine.AddExp("n1", "$$Invoke($$单价格式化, n2, $$精度)") > 0);

        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("n2").Id, 9.87m);

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(9.9m, host.GetItemDecimal(1L, "n1"));

        MacroInvocation invocation = Assert.Single(channel.Invocations);
        Assert.Equal("FormatPrice", invocation.Name);

        // Three arguments went in; two came out, and the dropped one was the name.
        Assert.Equal(2, invocation.Arguments.UpperBound);
        Assert.Equal("9.87", invocation.Arguments[1]);
        Assert.Equal("1", invocation.Arguments[2]);
    }

    /// <summary>
    /// The specification's dynamic-invoke example written EXACTLY as documented, with static inner
    /// references, does not work: the function name's substitution lengthens the expression so much that
    /// the trailing reference falls outside the scan. PRESERVED DEFECT (constraint C-B).
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS THE SAME DEFECT AS THE <c>$a + $ab</c> CASE, in its most consequential instance. The scan
    /// bound is captured once [:L1266] and the static path's refresh of <c>nLen</c> [:L1436] cannot extend
    /// it, so replacing <c>$单价格式化</c> - six characters - with <c>('FormatPrice')</c> - fifteen - moves
    /// nine characters of the argument list out of range, and the <c>$精度</c> that lived there is never
    /// parsed. It survives into the argument text as literal characters, the argument then fails to
    /// evaluate, and the whole calculation aborts with a reported error.
    /// </para>
    /// <para>
    /// WHY IT WENT UNNOTICED: the DIRECT form in the specification, <c>$FormatPrice(n2, $精度)</c>, is
    /// unaffected because <c>$精度</c> and its value <c>(1)</c> are both three characters. Only substituting
    /// a STRING - which acquires two quotes and two parentheses on top of its content - lengthens an
    /// expression enough to matter, and the dynamic-invoke form is the one place the specification does
    /// that.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task MacroFunction_DynamicForm_WithStaticInnerReferences_IsTruncated_IsPreservedDefect()
    {
        RecordingMacroChannel channel = new();
        channel.Answers["FormatPrice"] = 9.9m;

        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host, channel: channel, session: NewSession());

        Assert.Equal(RetCode.OK, engine.AddVar("精度", 1L));
        Assert.Equal(RetCode.OK, engine.AddVar("单价格式化", "FormatPrice"));

        // Transcribed verbatim from docs/n_cst_dwsvc_columnexp.md, "宏函数" part 2.
        Assert.True(engine.AddExp("n1", "$$Invoke($单价格式化, n2, $精度)") > 0);

        // The first reference expanded; the last one did not, and is still literal text.
        Assert.Equal("$$Invoke(('FormatPrice'), n2, $精度)", engine.GetExp("n1"));

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));

        // The unparsed reference cannot be evaluated, so nothing reaches the client and nothing is written.
        Assert.Empty(channel.Invocations);
        Assert.Equal(0m, host.GetItemDecimal(1L, "n1"));
        Assert.NotNull(engine.LastError);
        Assert.Equal(
            ExpressionErrorSite.PreprocessFunctionArgumentEvaluationFailed,
            engine.LastError.Site);
    }

    /// <summary>
    /// An UNHANDLED event aborts the calculation, because an event with no script leaves the <c>any</c>
    /// uninitialised and its class matches none of the seven accepted arms [:L2264-L2284].
    /// </summary>
    [Fact]
    public async Task MacroFunction_UnhandledEvent_AbortsWithAReportedReturnValueError()
    {
        RecordingMacroChannel channel = new();
        channel.Unhandled.Add("Missing");

        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host, channel: channel, session: NewSession());

        Assert.True(engine.AddExp("n1", "$Missing(n2)") > 0);

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));

        Assert.Equal(0m, host.GetItemDecimal(1L, "n1"));
        Assert.NotNull(engine.LastError);
        Assert.Equal(ExpressionErrorSite.PreprocessFunctionReturnValueInvalid, engine.LastError.Site);
    }

    /// <summary>
    /// With NO channel configured at all, a macro function aborts rather than fabricating a value - the
    /// same outcome by the same route as an unhandled event.
    /// </summary>
    [Fact]
    public async Task MacroFunction_WithNoChannel_AbortsWithoutWritingAValue()
    {
        ColumnExpressionEngine engine = CreateEngine(out FakeDataWindowHost host);

        Assert.True(engine.AddExp("n1", "$Missing(n2)") > 0);

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(0m, host.GetItemDecimal(1L, "n1"));
    }

    /// <summary>
    /// A boolean answer renders as <c>1=1</c> or <c>1=0</c> [:L2270-L2274], which is the OPPOSITE of the
    /// boolean <c>of_addvar</c> overload's rendering - an asymmetry both sides preserve.
    /// </summary>
    [Fact]
    public async Task MacroFunction_BooleanAnswer_RendersAsAnEqualityRatherThanAWord()
    {
        RecordingMacroChannel channel = new();
        channel.Answers["IsBig"] = true;

        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host, channel: channel, session: NewSession());

        Assert.True(engine.AddExp("n1", "if($IsBig(n2),100,200)") > 0);

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(100m, host.GetItemDecimal(1L, "n1"));

        // The variable side, for contrast: a boolean VARIABLE renders as the word.
        Assert.Equal(RetCode.OK, engine.AddVar("flag", true));
        Assert.Equal("true", engine.GetVarExp("flag"));
    }

    /// <summary>
    /// An argument that cannot be evaluated aborts before the channel is touched at all [:L2231-L2234].
    /// </summary>
    [Fact]
    public async Task MacroFunction_UnevaluableArgument_AbortsBeforeCallingTheClient()
    {
        RecordingMacroChannel channel = new();
        channel.Answers["Fn"] = 1m;

        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host, channel: channel, session: NewSession());

        Assert.True(engine.AddExp("n1", "$Fn(nosuchfunction(n2))") > 0);

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));

        Assert.Empty(channel.Invocations);
        Assert.NotNull(engine.LastError);
        Assert.Equal(
            ExpressionErrorSite.PreprocessFunctionArgumentEvaluationFailed,
            engine.LastError.Site);
    }

    /// <summary>
    /// <c>$$('name')</c> never touches the channel either, because it is a local variable lookup that
    /// merely happens to be spelled as a built-in function call.
    /// </summary>
    [Fact]
    public async Task DynamicIndirect_NeverTouchesTheMacroChannel()
    {
        RecordingMacroChannel channel = new();

        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host, channel: channel, session: NewSession());

        Assert.Equal(RetCode.OK, engine.AddVar("v", 8L));
        Assert.True(engine.AddExp("n1", "$$('v') + 1") > 0);

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(9m, host.GetItemDecimal(1L, "n1"));
        Assert.Empty(channel.Invocations);
    }


    // ==============================================================================================
    //  16. THE SIX TYPED WRITE ARMS - one per column type, plus the conditional null on the string arm
    // ==============================================================================================

    /// <summary>
    /// A host carrying one column of every type the six write arms address, so all six can be exercised.
    /// </summary>
    private static FakeDataWindowHost CreateAllTypesFixture()
    {
        FakeDataWindowHost host = new();

        host.AddColumn("dec1", FakeColumnType.DecimalOf(2));
        host.AddColumn("num1", FakeColumnType.Long);
        host.AddColumn("str1", FakeColumnType.CharOf(100));
        host.AddColumn("dtm1", FakeColumnType.DateTime);
        host.AddColumn("dat1", FakeColumnType.Date);
        host.AddColumn("tim1", FakeColumnType.Time);
        host.AddColumn("src", FakeColumnType.DecimalOf(2));

        for (int row = 0; row < 2; row++)
        {
            host.AddRow(null, null, null, null, null, null, 0m);
        }

        host.CallLog.Clear();
        return host;
    }

    /// <summary>
    /// Each of the six arms parses the evaluated text with the PowerScript conversion its own type calls
    /// for [:L767-L848], and writes only when the value actually changed.
    /// </summary>
    /// <remarks>
    /// THE ARM IS SELECTED BY THE BOUND COLUMN'S TYPE, NOT BY WHAT THE EXPRESSION LOOKS LIKE. A numeric
    /// expression on a string column lands as text, and a text expression on a numeric column is parsed as
    /// a number - which is what makes the type coercion observable rather than incidental.
    /// </remarks>
    [Fact]
    public async Task WriteArms_ParseTheEvaluatedTextPerColumnType()
    {
        FakeDataWindowHost host = CreateAllTypesFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host);

        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("src").Id, 3m);

        Assert.True(engine.AddExp("dec1", "src * 2") > 0);
        Assert.True(engine.AddExp("num1", "src + 4") > 0);
        Assert.True(engine.AddExp("str1", "'value ' + string(src)") > 0);
        Assert.True(engine.AddExp("dtm1", "'2024-03-05 14:07:09'") > 0);
        Assert.True(engine.AddExp("dat1", "'2024-03-05'") > 0);
        Assert.True(engine.AddExp("tim1", "'14:07:09'") > 0);

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));

        Assert.Equal(6m, host.GetItemDecimal(1L, "dec1"));
        Assert.Equal(7d, host.GetItemNumber(1L, "num1"));
        Assert.Equal("value 3", host.GetItemString(1L, "str1"));
        Assert.Equal(
            new DateTime(2024, 3, 5, 14, 7, 9, DateTimeKind.Unspecified),
            host.GetBufferValue(DwBuffer.Primary, 1L, host.Column("dtm1").Id));
        Assert.Equal(
            new DateOnly(2024, 3, 5),
            host.GetBufferValue(DwBuffer.Primary, 1L, host.Column("dat1").Id));
        Assert.Equal(
            new TimeOnly(14, 7, 9),
            host.GetBufferValue(DwBuffer.Primary, 1L, host.Column("tim1").Id));
    }

    /// <summary>
    /// An unchanged value is NOT written and the change event is NOT fired [:L772-L777 and its five
    /// siblings], on every one of the six types.
    /// </summary>
    /// <remarks>
    /// THE ORACLE NEEDS TWO TESTS WHERE C# NEEDS ONE. PowerScript comparison against NULL yields NULL
    /// rather than false, so <c>dcVal = dcValOld</c> cannot detect two nulls and a separate
    /// <c>IsNull(a) and IsNull(b)</c> arm follows it. A nullable comparison in C# answers true for two
    /// nulls, so the single comparison covers both arms - identical behaviour, one expression.
    /// </remarks>
    [Fact]
    public async Task WriteArms_DoNotFireTheChangeEventWhenTheValueIsUnchanged()
    {
        FakeDataWindowHost host = CreateAllTypesFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host);

        int changes = 0;
        host.ItemChangedHandler = (_, _, _) =>
        {
            changes++;
            return 0L;
        };

        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("src").Id, 3m);

        Assert.True(engine.AddExp("dec1", "src * 2") > 0);
        Assert.Equal(RetCode.OK, engine.SetTriggerEvent("dec1", true));

        // First pass: the value changes from null to 6, so the event fires once.
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(1, changes);
        Assert.Equal(6m, host.GetItemDecimal(1L, "dec1"));

        // Second pass with the same inputs: nothing changed, so nothing fires and nothing is written.
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(1, changes);
    }

    /// <summary>
    /// A vetoing <c>ItemChanged</c> handler suppresses the write [:L778-L780], on the arm that asked for
    /// the event.
    /// </summary>
    [Fact]
    public async Task WriteArms_AVetoingItemChangedHandlerSuppressesTheWrite()
    {
        FakeDataWindowHost host = CreateAllTypesFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host);

        host.ItemChangedHandler = static (_, _, _) => 1L;
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("src").Id, 3m);

        Assert.True(engine.AddExp("dec1", "src * 2") > 0);
        Assert.Equal(RetCode.OK, engine.SetTriggerEvent("dec1", true));

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));

        // The veto held: the column still carries its original null.
        Assert.Null(host.GetItemDecimal(1L, "dec1"));
    }

    /// <summary>
    /// Five arms treat an empty value as null unconditionally; the STRING arm treats it as null only when
    /// the column declares <c>NilIsNull</c> [:L830-L832].
    /// </summary>
    [Fact]
    public async Task WriteArms_TheStringArmIsTheOnlyOneWhereEmptinessIsConditional()
    {
        FakeDataWindowHost host = CreateAllTypesFixture();
        host.AddColumn("str2", FakeColumnType.CharOf(100)).SetProperty("edit.NilIsNull", "yes");
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("str1").Id, "before");
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("str2").Id, "before");

        ColumnExpressionEngine engine = CreateEngineOver(host);

        Assert.True(engine.AddExp("str1", "''") > 0);
        Assert.True(engine.AddExp("str2", "''") > 0);
        Assert.False(engine.Expressions[0].EmptyStringIsNull);
        Assert.True(engine.Expressions[1].EmptyStringIsNull);

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));

        // Without NilIsNull the empty string is a VALUE; with it, the column is nulled.
        Assert.Equal(string.Empty, host.GetItemString(1L, "str1"));
        Assert.Null(host.GetBufferValue(DwBuffer.Primary, 1L, host.Column("str2").Id));

        // A DECIMAL column, by contrast, is nulled with no declaration required at all.
        Assert.True(engine.AddExp("dec1", "''") > 0);
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Null(host.GetItemDecimal(1L, "dec1"));
    }

    /// <summary>
    /// A column type the six arms do not cover writes nothing at all - the oracle's
    /// <c>case else</c> [:L848 region].
    /// </summary>
    [Fact]
    public async Task WriteArms_AnUnknownColumnTypeWritesNothing()
    {
        FakeDataWindowHost host = CreateAllTypesFixture();

        // A type outside the six the coercion switch knows.
        host.AddColumn("blob1", "blob");

        ColumnExpressionEngine engine = CreateEngineOver(host);

        Assert.True(engine.AddExp("blob1", "1 + 1") > 0);
        Assert.Equal(DataWindowServiceBase.COL_TYPE_UNKNOWN, engine.Expressions[0].ColType);

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Null(host.GetBufferValue(DwBuffer.Primary, 1L, host.Column("blob1").Id));
    }

    // ==============================================================================================
    //  17. of_calcempty - CALCULATE ONLY WHAT IS EMPTY, PER COLUMN TYPE
    // ==============================================================================================

    /// <summary>
    /// <c>of_calcempty</c> [:L1956-L2066] restricts calculation to expressions whose bound column is
    /// currently EMPTY, and emptiness is defined per column type [:L1980-L2016].
    /// </summary>
    /// <remarks>
    /// THE TEMPORAL ARMS COMPARE AGAINST PowerScript's EPOCH SENTINELS, not against null [:L1997, :L2003,
    /// :L2009 compare with <c>DateTime("")</c>, <c>Date("")</c> and <c>Time("")</c>]. So 1900-01-01 counts
    /// as empty even though it is a perfectly good date, which is a real quirk of the legacy and is
    /// preserved.
    /// </remarks>
    [Fact]
    public async Task CalcEmpty_TreatsThePowerScriptEpochSentinelsAsEmpty()
    {
        FakeDataWindowHost host = CreateAllTypesFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host);

        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("src").Id, 3m);

        Assert.True(engine.AddExp("dec1", "src * 2") > 0);
        Assert.True(engine.AddExp("num1", "src + 4") > 0);
        Assert.True(engine.AddExp("str1", "'filled'") > 0);
        Assert.True(engine.AddExp("dtm1", "'2024-03-05 14:07:09'") > 0);
        Assert.True(engine.AddExp("dat1", "'2024-03-05'") > 0);
        Assert.True(engine.AddExp("tim1", "'14:07:09'") > 0);

        // Every column starts null, so every one is empty and every expression runs.
        Assert.Equal(RetCode.OK, await engine.CalcEmptyAsync(1L, Ct));
        Assert.Equal(6m, host.GetItemDecimal(1L, "dec1"));
        Assert.Equal("filled", host.GetItemString(1L, "str1"));

        // Now they are all filled, so a second pass changes nothing even with different inputs.
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("src").Id, 50m);
        Assert.Equal(RetCode.OK, await engine.CalcEmptyAsync(1L, Ct));
        Assert.Equal(6m, host.GetItemDecimal(1L, "dec1"));

        // The temporal sentinels count as EMPTY, so a column holding 1900-01-01 is recalculated.
        host.SetBufferValue(
            DwBuffer.Primary,
            1L,
            host.Column("dtm1").Id,
            ColumnExpressionEngine.InvalidDateTimeSentinel);
        host.SetBufferValue(
            DwBuffer.Primary,
            1L,
            host.Column("dat1").Id,
            ColumnExpressionEngine.InvalidDateSentinel);
        host.SetBufferValue(
            DwBuffer.Primary,
            1L,
            host.Column("tim1").Id,
            ColumnExpressionEngine.InvalidTimeSentinel);

        Assert.Equal(RetCode.OK, await engine.CalcEmptyAsync(1L, Ct));
        Assert.Equal(
            new DateTime(2024, 3, 5, 14, 7, 9, DateTimeKind.Unspecified),
            host.GetBufferValue(DwBuffer.Primary, 1L, host.Column("dtm1").Id));
        Assert.Equal(
            new DateOnly(2024, 3, 5),
            host.GetBufferValue(DwBuffer.Primary, 1L, host.Column("dat1").Id));
        Assert.Equal(
            new TimeOnly(14, 7, 9),
            host.GetBufferValue(DwBuffer.Primary, 1L, host.Column("tim1").Id));

        // The sentinel VALUES are PowerScript's, and the epoch is 1900-01-01.
        Assert.Equal(new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), ColumnExpressionEngine.InvalidDateTimeSentinel);
        Assert.Equal(new DateOnly(1900, 1, 1), ColumnExpressionEngine.InvalidDateSentinel);
        Assert.Equal(new TimeOnly(0, 0, 0), ColumnExpressionEngine.InvalidTimeSentinel);
    }

    /// <summary>
    /// A column of an unknown type is never empty [:L2014-L2015 sets <c>empty = false</c> in the
    /// <c>case else</c>], so its expression is skipped by the empty-only pass entirely.
    /// </summary>
    [Fact]
    public async Task CalcEmpty_TreatsAnUnknownColumnTypeAsNeverEmpty()
    {
        FakeDataWindowHost host = CreateAllTypesFixture();
        host.AddColumn("blob1", "blob");
        ColumnExpressionEngine engine = CreateEngineOver(host);

        Assert.True(engine.AddExp("blob1", "1 + 1") > 0);
        Assert.Equal(RetCode.OK, await engine.CalcEmptyAsync(1L, Ct));
        Assert.Null(host.GetBufferValue(DwBuffer.Primary, 1L, host.Column("blob1").Id));
    }

    /// <summary>
    /// The no-argument arity walks every row [:L2069 region], and both arities refuse a row outside the
    /// buffer.
    /// </summary>
    [Fact]
    public async Task CalcEmpty_WalksEveryRowAndRefusesABadRow()
    {
        FakeDataWindowHost host = CreateAllTypesFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host);

        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("src").Id, 3m);
        host.SetBufferValue(DwBuffer.Primary, 2L, host.Column("src").Id, 5m);

        Assert.True(engine.AddExp("dec1", "src * 2") > 0);

        Assert.Equal(RetCode.OK, await engine.CalcEmptyAsync(Ct));
        Assert.Equal(6m, host.GetItemDecimal(1L, "dec1"));
        Assert.Equal(10m, host.GetItemDecimal(2L, "dec1"));

        Assert.Equal(RetCode.E_OUT_OF_RANGE, await engine.CalcEmptyAsync(0L, Ct));
        Assert.Equal(RetCode.E_OUT_OF_RANGE, await engine.CalcEmptyAsync(99L, Ct));
    }

    /// <summary>
    /// <c>of_calcall</c> walks every row [:L1195 region], and refuses a bad row on the per-row arities.
    /// </summary>
    [Fact]
    public async Task CalcAll_WalksEveryRow_AndTheRowArityRefusesABadRow()
    {
        FakeDataWindowHost host = CreateAllTypesFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host);

        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("src").Id, 1m);
        host.SetBufferValue(DwBuffer.Primary, 2L, host.Column("src").Id, 2m);

        Assert.True(engine.AddExp("dec1", "src * 10") > 0);

        Assert.Equal(RetCode.OK, await engine.CalcAllAsync(Ct));
        Assert.Equal(10m, host.GetItemDecimal(1L, "dec1"));
        Assert.Equal(20m, host.GetItemDecimal(2L, "dec1"));

        Assert.Equal(RetCode.E_OUT_OF_RANGE, await engine.CalcAsync(0L, Ct));
        Assert.Equal(RetCode.E_OUT_OF_RANGE, await engine.CalcAsync(99L, Ct));
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, await engine.CalcAsync(0L, 1, Ct));
        Assert.Equal(RetCode.E_OUT_OF_BOUND, await engine.CalcAsync(1L, 99, Ct));
        Assert.Equal(RetCode.E_OUT_OF_BOUND, await engine.CalcAsync(1L, "nosuchcolumn", Ct));

        // The single-expression arity, by index and by name.
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("src").Id, 4m);
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, 1, Ct));
        Assert.Equal(40m, host.GetItemDecimal(1L, "dec1"));
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("src").Id, 5m);
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, "dec1", Ct));
        Assert.Equal(50m, host.GetItemDecimal(1L, "dec1"));
    }

    // ==============================================================================================
    //  18. THE EXPRESSION CACHE - a compute object, and the cached read that replaces evaluation
    // ==============================================================================================

    /// <summary>
    /// <c>of_setcacheable</c> creates a computed field carrying the expression [:L1921-L1954], and the
    /// calculation then READS that field instead of evaluating [:L717-L740].
    /// </summary>
    /// <remarks>
    /// THE CACHE IS ONLY USED WHEN THE EXPRESSION HAS NO MACRO [:L705], because a compute object is
    /// evaluated by the DataWindow engine, which knows nothing whatever about this service's variables or
    /// its application callbacks. So caching and macros are mutually exclusive by construction.
    /// </remarks>
    [Fact]
    public async Task Cacheable_CreatesAComputeObjectAndReadsThroughIt()
    {
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        RecordingSyntaxMutator mutator = new(host);
        ColumnExpressionEngine engine = CreateEngineOver(host, mutator: mutator);

        Assert.True(engine.AddExp("n1", "n2 * 2") > 0);
        Assert.False(engine.Expressions[0].Cacheable);

        Assert.Equal(RetCode.OK, engine.SetCacheable("n1", true));
        Assert.True(engine.Expressions[0].Cacheable);

        // The compute object's name carries the DWOSUFFIX and a monotonic discriminator.
        string computeName = engine.Expressions[0].ComputeName;
        Assert.Contains(ColumnExpressionEngine.DWOSUFFIX, computeName, StringComparison.Ordinal);
        Assert.Contains("n1", computeName, StringComparison.Ordinal);
        Assert.Equal([computeName], mutator.ComputeNames);

        // The fragment is the oracle's, verbatim - including the properties that do not affect the value.
        string syntax = Assert.Single(mutator.Syntax);
        Assert.StartsWith("create compute(band=detail alignment=\"1\" expression=\"n2 * 2\"", syntax, StringComparison.Ordinal);
        Assert.Contains(" name=" + computeName + " visible=\"1\" enabled=\"0\"", syntax, StringComparison.Ordinal);
        Assert.Contains("font.face=\"Arial\"", syntax, StringComparison.Ordinal);
        Assert.EndsWith(")\n", syntax, StringComparison.Ordinal);

        // The cached read answers whatever the compute object holds, NOT what the expression would say.
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("n2").Id, 4m);
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column(computeName).Id, 77m);

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(77m, host.GetItemDecimal(1L, "n1"));

        // Switching the cache off destroys the object.
        Assert.Equal(RetCode.OK, engine.SetCacheable("n1", false));
        Assert.False(engine.Expressions[0].Cacheable);
        Assert.Contains("Destroy " + computeName, mutator.Syntax);

        // And evaluation resumes, so the answer is the expression's again.
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(8m, host.GetItemDecimal(1L, "n1"));
    }

    /// <summary>
    /// A macro expression cannot be cached [:L1882], because a compute object cannot see this service's
    /// variables.
    /// </summary>
    [Fact]
    public void Cacheable_RefusesAMacroExpression()
    {
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        RecordingSyntaxMutator mutator = new(host);
        ColumnExpressionEngine engine = CreateEngineOver(host, mutator: mutator);

        Assert.Equal(RetCode.OK, engine.AddVar("v", 3L));
        Assert.True(engine.AddExp("n1", "$$v + 1") > 0);
        Assert.True(engine.Expressions[0].HasMacro);

        Assert.Equal(RetCode.E_NO_SUPPORT, engine.SetCacheable("n1", true));
        Assert.False(engine.Expressions[0].Cacheable);
        Assert.Empty(mutator.Syntax);
        Assert.NotNull(engine.LastError);
        Assert.Equal(
            ExpressionErrorSite.CreateExpressionCacheMacroExpansionUnsupported,
            engine.LastError.Site);
    }

    /// <summary>
    /// A refused syntax mutation is reported with the SYNTAX PREPENDED to the error text
    /// [:L1950-L1952] - and the cache flag is set ANYWAY, and the call answers success. PRESERVED DEFECT
    /// (constraint C-B).
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ORACLE REPORTS AND FALLS THROUGH. There is no <c>return</c> after the failure message in
    /// <c>of_setcacheable</c>: it shows the dialog and then executes
    /// <c>ColExpDatas[index].cacheable = cache</c> and <c>return RetCode.OK</c> regardless. So a caller is
    /// told the call succeeded while the expression is now marked as cached against a compute object that
    /// does not exist.
    /// </para>
    /// <para>
    /// It is self-healing rather than fatal, which is presumably why it survived: the next calculation
    /// finds the handle stale, re-resolves it, and re-creates the missing compute object on the
    /// <c>Not bDwoValid</c> path [:L707-L713]. But the success answer is a lie in the interim and it is
    /// preserved as one - the ERROR is the caller's signal, not the return code.
    /// </para>
    /// </remarks>
    [Fact]
    public void Cacheable_AFailedMutationIsReportedButStillAnswersSuccess_IsPreservedDefect()
    {
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        RecordingSyntaxMutator mutator = new(host) { FailWith = "syntax error near 'compute'" };
        ColumnExpressionEngine engine = CreateEngineOver(host, mutator: mutator);

        Assert.True(engine.AddExp("n1", "n2 * 2") > 0);

        // Success, and the flag set, despite the compute object never having been created.
        Assert.Equal(RetCode.OK, engine.SetCacheable("n1", true));
        Assert.True(engine.Expressions[0].Cacheable);

        Assert.NotNull(engine.LastError);
        Assert.Equal(
            ExpressionErrorSite.CreateExpressionCacheFailedOnCacheEnable,
            engine.LastError.Site);

        // The reported text carries what was ATTEMPTED as well as why it was refused.
        Assert.Contains("create compute(", engine.LastError.Text, StringComparison.Ordinal);
        Assert.Contains("syntax error near 'compute'", engine.LastError.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// With no syntax mutator available at all, the mutation is REPORTED as unavailable rather than being
    /// mistaken for a success - because the empty string is <c>Modify</c>'s success answer.
    /// </summary>
    /// <remarks>
    /// THE NON-EMPTY REFUSAL TEXT IS THE POINT. Answering the empty string would make a missing capability
    /// indistinguishable from a completed mutation, and the caller would then read a cached value from a
    /// compute object that was never created. The report follows the same preserved fall-through as the
    /// failed-mutation case above, so the return code is still success.
    /// </remarks>
    [Fact]
    public void Cacheable_WithNoSyntaxMutator_ReportsUnavailableRatherThanSilentSuccess()
    {
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host);

        Assert.True(engine.AddExp("n1", "n2 * 2") > 0);

        Assert.Equal(RetCode.OK, engine.SetCacheable("n1", true));

        Assert.NotEmpty(ColumnExpressionEngine.SyntaxMutationUnavailable);
        Assert.NotNull(engine.LastError);
        Assert.Contains(
            ColumnExpressionEngine.SyntaxMutationUnavailable,
            engine.LastError.Text,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The cache setter's bad-argument guards, and the idempotent no-op when the flag already agrees.
    /// </summary>
    [Fact]
    public void Cacheable_GuardsAndIdempotence()
    {
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        RecordingSyntaxMutator mutator = new(host);
        ColumnExpressionEngine engine = CreateEngineOver(host, mutator: mutator);

        Assert.True(engine.AddExp("n1", "n2 * 2") > 0);

        Assert.Equal(RetCode.E_OUT_OF_BOUND, engine.SetCacheable(0, true));
        Assert.Equal(RetCode.E_OUT_OF_BOUND, engine.SetCacheable(99, true));
        Assert.Equal(RetCode.E_OUT_OF_BOUND, engine.SetCacheable("nosuchcolumn", true));

        // Already off: nothing is destroyed, because there is nothing to destroy.
        Assert.Equal(RetCode.OK, engine.SetCacheable(1, false));
        Assert.Empty(mutator.Syntax);

        Assert.Equal(RetCode.OK, engine.SetCacheable(1, true));
        Assert.Single(mutator.Syntax);

        // Already on: no second compute object is created.
        Assert.Equal(RetCode.OK, engine.SetCacheable(1, true));
        Assert.Single(mutator.Syntax);
    }

    /// <summary>
    /// Removing a cached expression destroys its compute object [:L470-L472], so the object model does not
    /// accumulate orphans.
    /// </summary>
    [Fact]
    public void Cacheable_RemovingTheExpressionDestroysTheComputeObject()
    {
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        RecordingSyntaxMutator mutator = new(host);
        ColumnExpressionEngine engine = CreateEngineOver(host, mutator: mutator);

        Assert.True(engine.AddExp("n1", "n2 * 2") > 0);
        Assert.Equal(RetCode.OK, engine.SetCacheable("n1", true));
        string computeName = engine.Expressions[0].ComputeName;

        Assert.Equal(RetCode.OK, engine.Remove("n1"));
        Assert.Contains("Destroy " + computeName, mutator.Syntax);

        // RemoveAll destroys them too.
        Assert.True(engine.AddExp("n1", "n2 * 3") > 0);
        Assert.Equal(RetCode.OK, engine.SetCacheable("n1", true));
        string second = engine.Expressions[0].ComputeName;
        Assert.NotEqual(computeName, second);

        Assert.Equal(RetCode.OK, engine.RemoveAll());
        Assert.Contains("Destroy " + second, mutator.Syntax);
    }

    /// <summary>
    /// The composed compute-object name carries a DOUBLE underscore [:L1886]. PRESERVED DEFECT
    /// (constraint C-B).
    /// </summary>
    /// <remarks>
    /// The oracle composes the column name, an underscore, the discriminator and then
    /// <see cref="ColumnExpressionEngine.DWOSUFFIX"/> - which itself BEGINS with an underscore. So the two
    /// meet and the name reads <c>n1_1_columnexp</c> rather than <c>n1_1columnexp</c>. It is observable
    /// through <c>Describe</c> and in any syntax export, so it is reproduced rather than tidied.
    /// </remarks>
    [Fact]
    public void ComputeName_CarriesADoubleUnderscore_IsPreservedDefect()
    {
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        RecordingSyntaxMutator mutator = new(host);
        ColumnExpressionEngine engine = CreateEngineOver(host, mutator: mutator);

        Assert.True(engine.AddExp("n1", "n2 * 2") > 0);
        Assert.Equal(RetCode.OK, engine.SetCacheable("n1", true));

        string computeName = engine.Expressions[0].ComputeName;
        Assert.StartsWith("n1_", computeName, StringComparison.Ordinal);
        Assert.EndsWith(ColumnExpressionEngine.DWOSUFFIX, computeName, StringComparison.Ordinal);
        Assert.StartsWith("_", ColumnExpressionEngine.DWOSUFFIX, StringComparison.Ordinal);
        Assert.Contains("__", computeName, StringComparison.Ordinal);
    }


    // ==============================================================================================
    //  19. THE VARIABLE TABLE - seven typed overloads, three arities each, and a case sensitive name
    // ==============================================================================================

    /// <summary>
    /// Each of the seven typed <c>of_addvar</c> overloads renders its value through the same
    /// <c>dwValueToExp</c> port the write arms use, so a variable's stored expression is always a literal
    /// the evaluator can parse.
    /// </summary>
    /// <remarks>
    /// THE BOOLEAN OVERLOAD RENDERS A WORD, not the <c>1=1</c> equality a boolean MACRO ANSWER renders as
    /// [:L2270-L2274]. The two are different code paths in the oracle and the asymmetry is preserved on
    /// both sides.
    /// </remarks>
    [Fact]
    public void AddVar_SevenTypedOverloads_RenderThroughTheValueToExpressionPort()
    {
        ColumnExpressionEngine engine = CreateEngine(out _);

        Assert.Equal(RetCode.OK, engine.AddVar("v_long", 42L));
        Assert.Equal(RetCode.OK, engine.AddVar("v_double", 1.5d));
        Assert.Equal(RetCode.OK, engine.AddVar("v_string", "hello"));
        Assert.Equal(RetCode.OK, engine.AddVar("v_datetime", new DateTime(2024, 3, 5, 14, 7, 9, DateTimeKind.Unspecified)));
        Assert.Equal(RetCode.OK, engine.AddVar("v_date", new DateOnly(2024, 3, 5)));
        Assert.Equal(RetCode.OK, engine.AddVar("v_time", new TimeOnly(14, 7, 9)));
        Assert.Equal(RetCode.OK, engine.AddVar("v_bool", true));

        Assert.Equal("42", engine.GetVarExp("v_long"));
        Assert.Equal("1.5", engine.GetVarExp("v_double"));
        Assert.Equal("'hello'", engine.GetVarExp("v_string"));
        Assert.StartsWith("DateTime('2024-03-05 14:07:09", engine.GetVarExp("v_datetime"), StringComparison.Ordinal);
        Assert.Equal("Date('2024-03-05')", engine.GetVarExp("v_date"));
        Assert.StartsWith("Time('14:07:09", engine.GetVarExp("v_time"), StringComparison.Ordinal);
        Assert.Equal("true", engine.GetVarExp("v_bool"));

        Assert.Equal(7, engine.Variables.UpperBound);
        Assert.Equal(ExpressionVariableEnvironment.VAR_LOCAL, engine.Variables[1].VarType);

        // A name that no variable carries answers the empty string rather than raising.
        Assert.Equal(string.Empty, engine.GetVarExp("nosuchvariable"));
        Assert.Equal(string.Empty, engine.GetVarExp(null));
    }

    /// <summary>
    /// A NULL typed value renders as the null literal, so a variable can carry null without the expression
    /// text becoming empty - which would be the abort sentinel.
    /// </summary>
    /// <remarks>
    /// THIS IS THE INVARIANT THE SIBLING <c>ValueToExpression</c> PORT IS HELD TO, and it is load bearing
    /// for this engine: a variable whose expression is the empty string is refused at :L1431 with
    /// <c>FAILED</c>, and a preprocessed expression that comes back empty is an abort. Neither may be
    /// triggered by a legitimate null.
    /// </remarks>
    [Fact]
    public void AddVar_ANullValue_StillProducesANonEmptyExpression()
    {
        ColumnExpressionEngine engine = CreateEngine(out _);

        Assert.Equal(RetCode.OK, engine.AddVar("v_null_long", (long?)null));
        Assert.Equal(RetCode.OK, engine.AddVar("v_null_string", (string?)null));
        Assert.Equal(RetCode.OK, engine.AddVar("v_null_date", (DateOnly?)null));

        Assert.NotEmpty(engine.GetVarExp("v_null_long"));
        Assert.NotEmpty(engine.GetVarExp("v_null_string"));
        Assert.NotEmpty(engine.GetVarExp("v_null_date"));
    }

    /// <summary>
    /// Variable names are CASE SENSITIVE [:L1587 "区分大小写", enforced by the unfolded comparison at
    /// :L997] and a duplicate definition is refused with <see cref="RetCode.FAILED"/> [:L1607].
    /// </summary>
    [Fact]
    public void Variables_AreCaseSensitiveAndCannotBeDefinedTwice()
    {
        ColumnExpressionEngine engine = CreateEngine(out _);

        Assert.Equal(RetCode.OK, engine.AddVar("Alpha", 1L));

        // A different case is a DIFFERENT variable, not a duplicate.
        Assert.Equal(RetCode.OK, engine.AddVar("alpha", 2L));
        Assert.Equal(2, engine.Variables.UpperBound);
        Assert.Equal("1", engine.GetVarExp("Alpha"));
        Assert.Equal("2", engine.GetVarExp("alpha"));

        // The exact same name IS a duplicate, and is reported.
        Assert.Equal(RetCode.FAILED, engine.AddVar("Alpha", 3L));
        Assert.NotNull(engine.LastError);
        Assert.Equal(ExpressionErrorSite.DuplicateVariableDefinition, engine.LastError.Site);
        Assert.Equal("1", engine.GetVarExp("Alpha"));
    }

    /// <summary>
    /// A variable name may contain none of the expression delimiters [:L1612-L1617], because a name that
    /// contained one could not be recognised as a whole token during the scan.
    /// </summary>
    [Theory]
    [InlineData("has space")]
    [InlineData("has+plus")]
    [InlineData("has(paren")]
    [InlineData("has,comma")]
    [InlineData("has'quote")]
    [InlineData("has.dot")]
    [InlineData("has;semi")]
    public void Variables_RefuseANameContainingAnExpressionDelimiter(string name)
    {
        ColumnExpressionEngine engine = CreateEngine(out _);

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, engine.AddVarExp(name, "1"));
        Assert.Equal(0, engine.Variables.UpperBound);

        // The delimiter set is published, so a caller can validate a name before offering it.
        Assert.Contains(name.First(static c => !char.IsLetter(c)), ColumnExpressionEngine.VariableNameDelimiters);
    }

    /// <summary>Both empty-argument guards on the variable definition entry points.</summary>
    [Fact]
    public void Variables_RefuseAnEmptyNameOrAnEmptyExpression()
    {
        ColumnExpressionEngine engine = CreateEngine(out _);

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, engine.AddVarExp(null, "1"));
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, engine.AddVarExp(string.Empty, "1"));
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, engine.AddVarExp("v", null));
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, engine.AddVarExp("v", string.Empty));
        Assert.Equal(0, engine.Variables.UpperBound);
    }

    /// <summary>
    /// A variable whose value expression fails to parse is refused, so a malformed macro cannot be stored
    /// and then fail later at every calculation.
    /// </summary>
    [Fact]
    public void Variables_RefuseAValueExpressionThatFailsToParse()
    {
        ColumnExpressionEngine engine = CreateEngine(out _);

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, engine.AddVarExp("v", "$nosuch + 1"));
        Assert.Equal(0, engine.Variables.UpperBound);
    }

    /// <summary>
    /// <c>of_setvar</c> across its three arities: value, value plus recalculate, and value plus
    /// recalculate plus force.
    /// </summary>
    /// <remarks>
    /// WITHOUT <c>recalc</c> NOTHING IS RECALCULATED, which is what makes a batch of variable writes cheap:
    /// a caller sets several and then recalculates once. And <c>force</c> is what reaches an
    /// input-triggered expression, which a variable change otherwise never does [:L345-L347].
    /// </remarks>
    [Fact]
    public async Task SetVar_ThreeArities_ControlRecalculationAndForce()
    {
        ColumnExpressionEngine engine = CreateEngine(out FakeDataWindowHost host);

        Assert.Equal(RetCode.OK, engine.AddVar("v", 1L));
        Assert.True(engine.AddExp("n1", "$$v * 10") > 0);
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(10m, host.GetItemDecimal(1L, "n1"));

        // One argument: the variable moves, nothing recalculates.
        Assert.Equal(RetCode.OK, await engine.SetVarAsync("v", 2L, Ct));
        Assert.Equal("2", engine.GetVarExp("v"));
        Assert.Equal(10m, host.GetItemDecimal(1L, "n1"));

        // Two arguments with recalc: the dependent expression runs on every row.
        Assert.Equal(RetCode.OK, await engine.SetVarAsync("v", 3L, true, Ct));
        Assert.Equal(30m, host.GetItemDecimal(1L, "n1"));
        Assert.Equal(30m, host.GetItemDecimal(4L, "n1"));

        // An input-triggered expression is NOT reached by a recalc without force.
        Assert.Equal(RetCode.OK, engine.SetRelativeInputColumn("n1", "n2"));
        Assert.Equal(RetCode.OK, await engine.SetVarAsync("v", 4L, true, Ct));
        Assert.Equal(30m, host.GetItemDecimal(1L, "n1"));

        // Three arguments with force: now it is.
        Assert.Equal(RetCode.OK, await engine.SetVarAsync("v", 5L, true, true, Ct));
        Assert.Equal(50m, host.GetItemDecimal(1L, "n1"));

        // An unknown variable is ADDED rather than refused: of_setvarexp upserts, delegating to
        // of_addvarexp when the name resolves to nothing [:L1690 region]. So of_setvar is the one call a
        // caller needs, which is how every specification example uses it.
        Assert.Equal(RetCode.OK, await engine.SetVarAsync("brandnew", 6L, Ct));
        Assert.Equal("6", engine.GetVarExp("brandnew"));
    }

    /// <summary>Every typed <c>of_setvar</c> overload routes through the same expression conversion.</summary>
    [Fact]
    public async Task SetVar_EveryTypedOverloadRoutesThroughTheSameConversion()
    {
        ColumnExpressionEngine engine = CreateEngine(out _);

        Assert.Equal(RetCode.OK, engine.AddVar("v", 0L));

        Assert.Equal(RetCode.OK, await engine.SetVarAsync("v", 7L, Ct));
        Assert.Equal("7", engine.GetVarExp("v"));

        Assert.Equal(RetCode.OK, await engine.SetVarAsync("v", 2.25d, Ct));
        Assert.Equal("2.25", engine.GetVarExp("v"));

        Assert.Equal(RetCode.OK, await engine.SetVarAsync("v", "text", Ct));
        Assert.Equal("'text'", engine.GetVarExp("v"));

        Assert.Equal(RetCode.OK, await engine.SetVarAsync("v", new DateOnly(2024, 3, 5), Ct));
        Assert.Equal("Date('2024-03-05')", engine.GetVarExp("v"));

        Assert.Equal(RetCode.OK, await engine.SetVarAsync("v", new TimeOnly(14, 7, 9), Ct));
        Assert.StartsWith("Time('14:07:09", engine.GetVarExp("v"), StringComparison.Ordinal);

        Assert.Equal(
            RetCode.OK,
            await engine.SetVarAsync("v", new DateTime(2024, 3, 5, 14, 7, 9, DateTimeKind.Unspecified), Ct));
        Assert.StartsWith("DateTime('2024-03-05 14:07:09", engine.GetVarExp("v"), StringComparison.Ordinal);

        Assert.Equal(RetCode.OK, await engine.SetVarAsync("v", false, Ct));
        Assert.Equal("false", engine.GetVarExp("v"));
    }

    /// <summary>
    /// <c>of_setvarexp</c> replaces a variable's EXPRESSION, and a recalculation then propagates it to
    /// every dependent expression on every row.
    /// </summary>
    [Fact]
    public async Task SetVarExp_ReplacesTheExpressionAndPropagatesOnRecalculation()
    {
        ColumnExpressionEngine engine = CreateEngine(out FakeDataWindowHost host);

        Assert.Equal(RetCode.OK, engine.AddVarExp("v", "2"));
        Assert.True(engine.AddExp("n1", "$$v * 10") > 0);
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(20m, host.GetItemDecimal(1L, "n1"));

        Assert.Equal(RetCode.OK, await engine.SetVarExpAsync("v", "3", true, Ct));
        Assert.Equal("3", engine.GetVarExp("v"));
        Assert.Equal(30m, host.GetItemDecimal(1L, "n1"));

        // A macro expression is accepted as a variable's value, and resolves recursively at calc time.
        Assert.Equal(RetCode.OK, engine.AddVar("inner", 4L));
        Assert.Equal(RetCode.OK, await engine.SetVarExpAsync("v", "$$inner + 1", true, true, Ct));
        Assert.Equal(50m, host.GetItemDecimal(1L, "n1"));

        // The setter UPSERTS - an unknown name delegates to of_addvarexp [:L1690 region].
        Assert.Equal(RetCode.OK, await engine.SetVarExpAsync("brandnew", "9", Ct));
        Assert.Equal("9", engine.GetVarExp("brandnew"));

        // The empty-expression guard applies on BOTH routes, because the add path carries its own.
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, await engine.SetVarExpAsync("v", string.Empty, Ct));
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, await engine.SetVarExpAsync("stillnew", string.Empty, Ct));
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, await engine.SetVarExpAsync(null, "1", Ct));

        // Setting an unchanged expression is an early-out rather than a re-parse.
        Assert.Equal(RetCode.OK, await engine.SetVarExpAsync("brandnew", "9", Ct));
        Assert.Equal("9", engine.GetVarExp("brandnew"));
    }

    /// <summary>
    /// A variable's expression that fails to parse leaves the variable untouched.
    /// </summary>
    [Fact]
    public async Task SetVarExp_ARefusedParseLeavesTheVariableUntouched()
    {
        ColumnExpressionEngine engine = CreateEngine(out _);

        Assert.Equal(RetCode.OK, engine.AddVarExp("v", "2"));
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, await engine.SetVarExpAsync("v", "$nosuch", Ct));
        Assert.Equal("2", engine.GetVarExp("v"));
    }

    /// <summary>
    /// The variable binding carries the same three-part payload an expression binding does, because a
    /// variable's value may itself be an expression with its own references.
    /// </summary>
    [Fact]
    public void VariableBinding_CarriesItsOwnUnexpandedSourceAndSnapshot()
    {
        ColumnExpressionEngine engine = CreateEngine(out _);

        Assert.Equal(RetCode.OK, engine.AddVar("inner", 6L));
        Assert.Equal(RetCode.OK, engine.AddVarExp("outer", "$inner + $$inner"));

        ExpressionBindingSnapshot? binding = engine.GetVariableBinding("outer");
        Assert.NotNull(binding);
        Assert.Equal("$inner + $$inner", binding.UnexpandedExp);
        Assert.Equal("6", binding.BindTimeSnapshot["inner"]);

        // The static reference was substituted; the dynamic one is retained. In ONE expression.
        Assert.Equal("(6) + $$inner", engine.GetVarExp("outer"));
        Assert.Single(binding.Vars);
        Assert.True(binding.Vars[0].IsMacro);

        Assert.Null(engine.GetVariableBinding("nosuchvariable"));
        Assert.Null(engine.GetVariableBinding(null));
    }

    // ==============================================================================================
    //  20. OnVarChanged AND OnItemChanged - the two event entry points that drive recalculation
    // ==============================================================================================

    /// <summary>
    /// <c>OnVarChanged</c> walks every row and recalculates only what depends on that variable
    /// [:L336-L352].
    /// </summary>
    [Fact]
    public async Task OnVarChanged_RecalculatesOnlyTheDependentExpressions()
    {
        ColumnExpressionEngine engine = CreateEngine(out FakeDataWindowHost host);

        Assert.Equal(RetCode.OK, engine.AddVar("used", 3L));
        Assert.Equal(RetCode.OK, engine.AddVar("unused", 9L));

        Assert.True(engine.AddExp("n1", "$$used * 2") > 0);
        Assert.True(engine.AddExp("s1", "'constant'") > 0);

        // The variable moves without a recalculation, then the event drives one.
        Assert.Equal(RetCode.OK, await engine.SetVarAsync("used", 5L, Ct));
        await engine.OnVarChangedAsync(engine.FindVarIndex("used"), false, Ct);

        Assert.Equal(10m, host.GetItemDecimal(1L, "n1"));
        Assert.Equal(10m, host.GetItemDecimal(4L, "n1"));

        // The independent expression was never a dependent, so it was not swept along.
        Assert.Null(host.GetBufferValue(DwBuffer.Primary, 1L, host.Column("s1").Id));

        // A variable nothing depends on drives nothing.
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("n1").Id, 0m);
        await engine.OnVarChangedAsync(engine.FindVarIndex("unused"), false, Ct);
        Assert.Equal(0m, host.GetItemDecimal(1L, "n1"));

        // An index naming no variable is harmless.
        await engine.OnVarChangedAsync(0, false, Ct);
        await engine.OnVarChangedAsync(99, false, Ct);
    }

    /// <summary>
    /// A dependency reached THROUGH another variable is still a dependency, because a variable's value may
    /// itself reference variables [:L339 walks transitively].
    /// </summary>
    [Fact]
    public async Task OnVarChanged_FollowsATransitiveVariableDependency()
    {
        ColumnExpressionEngine engine = CreateEngine(out FakeDataWindowHost host);

        Assert.Equal(RetCode.OK, engine.AddVar("leaf", 2L));
        Assert.Equal(RetCode.OK, engine.AddVarExp("branch", "$$leaf * 3"));
        Assert.True(engine.AddExp("n1", "$$branch + 1") > 0);

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(7m, host.GetItemDecimal(1L, "n1"));

        // The expression names `branch`, not `leaf` - and a change to `leaf` still reaches it.
        Assert.Equal(RetCode.OK, await engine.SetVarAsync("leaf", 5L, Ct));
        await engine.OnVarChangedAsync(engine.FindVarIndex("leaf"), false, Ct);
        Assert.Equal(16m, host.GetItemDecimal(1L, "n1"));
    }

    /// <summary>
    /// Two variables referencing EACH OTHER cannot drive an unbounded dependency walk.
    /// </summary>
    /// <remarks>
    /// THIS GUARD IS AN ADDITION, AND IT IS DOCUMENTED AS ONE. The oracle walks the same graph without a
    /// visited set, so a mutual reference recurses until PowerBuilder's stack is exhausted - which in a
    /// desktop process kills one application the user was already looking at. Across a service boundary the
    /// same input arrives from a caller and would kill a shared process, so the walk is bounded. It is the
    /// only place this port refuses something the legacy attempted, and it is refused because decomposition
    /// created the exposure rather than because the behaviour was wrong.
    /// </remarks>
    [Fact]
    public async Task OnVarChanged_MutuallyReferencingVariablesDoNotRecurseForever()
    {
        ColumnExpressionEngine engine = CreateEngine(out _);

        Assert.Equal(RetCode.OK, engine.AddVarExp("a", "1"));
        Assert.Equal(RetCode.OK, engine.AddVarExp("b", "$$a + 1"));

        // Point `a` back at `b`, closing the cycle.
        Assert.Equal(RetCode.OK, await engine.SetVarExpAsync("a", "$$b + 1", Ct));

        // Terminates rather than exhausting the stack.
        await engine.OnVarChangedAsync(engine.FindVarIndex("a"), false, Ct);
        await engine.OnVarChangedAsync(engine.FindVarIndex("b"), true, Ct);
    }

    /// <summary>
    /// <c>OnItemChanged</c> raises <c>OnDoItemChanged</c> for the changed column [:L215-L230], which is how
    /// the DataWindow's own item-change chain reaches this engine.
    /// </summary>
    [Fact]
    public async Task OnItemChanged_DrivesTheDependencyWalkForTheChangedColumn()
    {
        ColumnExpressionEngine engine = CreateEngine(out FakeDataWindowHost host);

        Assert.True(engine.AddExp("n1", "n2 * 4") > 0);
        Assert.Equal(RetCode.OK, engine.SetRelativeColumn("n1", "n2"));

        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("n2").Id, 3m);

        await engine.OnItemChangedAsync(1L, host.DwObject("n2"), Ct);
        Assert.Equal(12m, host.GetItemDecimal(1L, "n1"));

        // A null object is a programming error rather than a data condition, and is refused as one.
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await engine.OnItemChangedAsync(1L, null!, Ct));
    }

    /// <summary>
    /// A row outside the buffer is ignored by both event entry points [:L232 and :L336 region] rather than
    /// raising, because a stale row number is a data condition the legacy tolerated.
    /// </summary>
    [Fact]
    public async Task EventEntryPoints_IgnoreARowOutsideTheBuffer()
    {
        ColumnExpressionEngine engine = CreateEngine(out FakeDataWindowHost host);

        Assert.True(engine.AddExp("n1", "n2 * 4") > 0);
        Assert.Equal(RetCode.OK, engine.SetAlwaysCalc("n1", true));

        await engine.OnDoItemChangedAsync(0L, "n2", host.Column("n2").Id, false, Ct);
        await engine.OnDoItemChangedAsync(99L, "n2", host.Column("n2").Id, false, Ct);
        await engine.OnItemChangedAsync(99L, host.DwObject("n2"), Ct);

        Assert.Null(engine.LastError);
    }

    // ==============================================================================================
    //  21. RECURSION - the recursive flag, and the stack that detects a cycle
    // ==============================================================================================

    /// <summary>
    /// An expression that references its OWN column is refused re-entry unless it is marked recursive
    /// [:L682-L690].
    /// </summary>
    /// <remarks>
    /// THE STACK IS WHAT DETECTS IT, and the stack lives on the session so that a cross-DataWindow cycle is
    /// detected too. <c>of_setrecursive</c> is the opt-in for an expression that genuinely wants to see its
    /// own previous value.
    /// </remarks>
    [Fact]
    public async Task Recursion_IsRefusedUnlessTheExpressionIsMarkedRecursive()
    {
        ColumnExpressionEngine engine = CreateEngine(out FakeDataWindowHost host);

        // n1 depends on itself, and n2 depends on n1 - so calculating n2 re-enters n1.
        Assert.True(engine.AddExp("n1", "n1 + 1") > 0);
        Assert.Equal(RetCode.OK, engine.SetRelativeColumn("n1", "n1"));

        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("n1").Id, 0m);

        // One pass produces one increment, not an unbounded cascade.
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(1m, host.GetItemDecimal(1L, "n1"));

        Assert.Equal(RetCode.OK, engine.SetRecursive("n1", true));
        Assert.True(engine.Expressions[0].Recursive);

        // Still bounded: the dirty flag and the stack both participate.
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.InRange(host.GetItemDecimal(1L, "n1")!.Value, 1m, 100m);
    }

    /// <summary>
    /// A row pass refuses to nest [:L1160, :L1201], so an expression that provokes a second pass from
    /// inside the first is answered with <see cref="RetCode.FAILED"/> rather than recursing.
    /// </summary>
    [Fact]
    public async Task RowCalculation_RefusesToNest()
    {
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();

        ColumnExpressionEngine? engine = null;
        bool observedRowCalculating = false;
        ReentrantMacroChannel channel = new(
            async ct =>
            {
                observedRowCalculating = engine!.IsRowCalculating;
                return await engine.CalcAsync(1L, ct).ConfigureAwait(false);
            },
            4m);

        engine = CreateEngineOver(host, channel: channel, session: NewSession());

        Assert.True(engine.AddExp("n1", "$Nest(n2)") > 0);

        Assert.False(engine.IsRowCalculating);
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));

        // Seen from INSIDE the pass: the flag is raised for its whole duration.
        Assert.True(observedRowCalculating);

        // And the nested pass, issued while the outer one held the flag, was answered FAILED rather than
        // being allowed to nest [:L1161] - not PREVENT, not E_NO_SUPPORT.
        Assert.Equal(RetCode.FAILED, channel.NestedResult);

        // The flag is released afterwards, so a later pass is accepted and the macro's value did land.
        Assert.False(engine.IsRowCalculating);
        Assert.False(engine.IsEmptyRowCalculating);
        Assert.Equal(4m, host.GetItemDecimal(1L, "n1"));
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
    }

    // ==============================================================================================
    //  22. THE HOST INTERFACE - GetItemExpValue over every column type, and the two sync arities
    // ==============================================================================================

    /// <summary>
    /// <c>_of_getitemexpvalue</c> renders a cell as an EXPRESSION LITERAL, one arm per column type
    /// [:L2344-L2357], and answers the abort sentinel for a type it has no arm for [:L2358].
    /// </summary>
    [Fact]
    public void GetItemExpValue_RendersEveryColumnTypeAsALiteral()
    {
        FakeDataWindowHost host = CreateAllTypesFixture();
        host.AddColumn("blob1", "blob");
        ColumnExpressionEngine engine = CreateEngineOver(host);

        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("dec1").Id, 12.5m);
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("num1").Id, 7L);
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("str1").Id, "text");
        host.SetBufferValue(
            DwBuffer.Primary,
            1L,
            host.Column("dtm1").Id,
            new DateTime(2024, 3, 5, 14, 7, 9, DateTimeKind.Unspecified));
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("dat1").Id, new DateOnly(2024, 3, 5));
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("tim1").Id, new TimeOnly(14, 7, 9));

        Assert.Equal("12.5", engine.GetItemExpValue(1L, "dec1"));
        Assert.Equal("7", engine.GetItemExpValue(1L, "num1"));
        Assert.Equal("'text'", engine.GetItemExpValue(1L, "str1"));
        Assert.StartsWith("DateTime('2024-03-05 14:07:09", engine.GetItemExpValue(1L, "dtm1"), StringComparison.Ordinal);
        Assert.Equal("Date('2024-03-05')", engine.GetItemExpValue(1L, "dat1"));
        Assert.StartsWith("Time('14:07:09", engine.GetItemExpValue(1L, "tim1"), StringComparison.Ordinal);

        // A type with no arm answers the ABORT sentinel, so a `@name` against it stops the calculation.
        Assert.Equal(ColumnExpressionEngine.AbortSentinel, engine.GetItemExpValue(1L, "blob1"));
        Assert.Equal(ColumnExpressionEngine.AbortSentinel, engine.GetItemExpValue(1L, "nosuchcolumn"));
        Assert.Equal(ColumnExpressionEngine.AbortSentinel, engine.GetItemExpValue(1L, null));

        // A NULL cell still renders as a non-empty literal, never as the abort sentinel.
        Assert.NotEmpty(engine.GetItemExpValue(2L, "dec1"));
        Assert.NotEmpty(engine.GetItemExpValue(2L, "dtm1"));
        Assert.NotEmpty(engine.GetItemExpValue(2L, "str1"));
    }

    /// <summary>
    /// The two synchronous <c>_of_calcvarexpvalue</c> arities answer a variable's value as a literal, and
    /// the four-argument one computes against the CURRENT row rather than a supplied one [:L2399].
    /// </summary>
    /// <remarks>
    /// A MACRO FUNCTION CANNOT BE AWAITED ON THIS PATH, so a variable whose expression contains one answers
    /// the abort sentinel here and only here. That narrowing is deliberate: the alternative would be to
    /// block a request thread on a round trip to the client, which is worse than a defined refusal.
    /// </remarks>
    [Fact]
    public void CalcVarExpValue_BothSyncAritiesAnswerALiteral()
    {
        ExpressionSession session = NewSession();
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host, session: session);

        Assert.Equal(RetCode.OK, engine.AddVar("v", 11L));
        int index = engine.FindVarIndex("v");

        // The six-argument arity, with an explicit row.
        Assert.Equal("11", engine.CalcVarExpValue(1L, host.DwObject("n1"), index, 1L, null, null));

        // The four-argument arity, against the current row.
        Assert.Equal("11", engine.CalcVarExpValue(index, 1L, null, null));

        // An index naming no variable aborts rather than faulting.
        Assert.Equal(ColumnExpressionEngine.AbortSentinel, engine.CalcVarExpValue(0, 1L, null, null));
        Assert.Equal(ColumnExpressionEngine.AbortSentinel, engine.CalcVarExpValue(99, 1L, null, null));

        // A variable whose expression is a macro FUNCTION cannot be resolved without awaiting the channel.
        Assert.Equal(RetCode.OK, engine.AddVarExp("fn", "$Something(n2)"));
        Assert.Equal(
            ColumnExpressionEngine.AbortSentinel,
            engine.CalcVarExpValue(engine.FindVarIndex("fn"), 1L, null, null));
    }

    /// <summary>
    /// The engine implements <see cref="IExpressionServiceHost"/>, which is how the session reaches it
    /// without an object pointer - AAP section 0.6.2.3's replacement for <c>foreignvardata.expsvc</c>.
    /// </summary>
    [Fact]
    public void Engine_IsReachableThroughTheSessionAsAnExpressionServiceHost()
    {
        ExpressionSession session = NewSession();
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host, session: session);

        Assert.IsAssignableFrom<IExpressionServiceHost>(engine);
        Assert.False(engine.OwnsSession);
        Assert.False(engine.Handle.IsEmpty);
        Assert.True(session.TryGetHost(engine.Handle, out IExpressionServiceHost? resolved));
        Assert.Same(engine, resolved);

        // An engine given no session makes its own, and is then alone in it.
        ColumnExpressionEngine solitary = CreateEngine(out _);
        Assert.True(solitary.OwnsSession);
        Assert.Equal(1, solitary.Session.HandleCount);
    }

    // ==============================================================================================
    //  23. THE ONE BASED LIST - AAP section 0.8.6 R9's hazard, isolated and asserted directly
    // ==============================================================================================

    /// <summary>
    /// The one-based indexing helper every ported loop routes through.
    /// </summary>
    /// <remarks>
    /// PowerBuilder arrays are ONE BASED and <c>UpperBound</c> answers the LAST VALID INDEX, whereas a .NET
    /// list is zero based with a count one past the end. AAP section 0.8.6 R9 names that as the single most
    /// dangerous mechanical hazard in the refactor, because an off-by-one here is indistinguishable from a
    /// behavioural regression. Centralising it means there is one implementation to get right and one place
    /// to test, which is what this test is.
    /// </remarks>
    [Fact]
    public void OneBasedList_IndexesFromOneAndGrowsOnDemand()
    {
        OneBasedList<long> list = new(static () => 0L);

        Assert.Equal(0, list.UpperBound);
        Assert.False(list.IsValidIndex(0));
        Assert.False(list.IsValidIndex(1));

        // Append answers the index it wrote to, which is the new upper bound.
        Assert.Equal(1, list.Append(10L));
        Assert.Equal(2, list.Append(20L));
        Assert.Equal(2, list.UpperBound);
        Assert.Equal(10L, list[1]);
        Assert.Equal(20L, list[2]);
        Assert.True(list.IsValidIndex(1));
        Assert.True(list.IsValidIndex(2));
        Assert.False(list.IsValidIndex(3));

        // Grow on demand fills with the default factory's value, so index N is addressable after growth.
        list.EnsureUpperBound(5);
        Assert.Equal(5, list.UpperBound);
        Assert.Equal(0L, list[5]);
        Assert.Equal(10L, list[1]);

        // Assignment through the indexer, and the snapshot in index order.
        list[5] = 50L;
        Assert.Equal([10L, 20L, 0L, 0L, 50L], list.Items);

        // Index 0 is out of range at BOTH ends, which is the whole point of the type.
        Assert.Throws<ArgumentOutOfRangeException>(() => list[0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => list[6]);
        Assert.Throws<ArgumentOutOfRangeException>(() => list[0] = 1L);

        // Search, forwards and in reverse, answering 0 for no match - PowerBuilder's own convention.
        Assert.Equal(1, list.IndexOf(static value => value == 10L));
        Assert.Equal(0, list.IndexOf(static value => value == 99L));
        list.Append(20L);
        Assert.Equal(2, list.IndexOf(static value => value == 20L));
        Assert.Equal(6, list.IndexOf(static value => value == 20L, reverse: true));

        list.Assign([7L, 8L]);
        Assert.Equal([7L, 8L], list.Items);

        list.Clear();
        Assert.Equal(0, list.UpperBound);
        Assert.Empty(list.Items);

        // The constructor that seeds from a sequence keeps the sequence's order.
        OneBasedList<long> seeded = new(static () => 0L, [3L, 4L]);
        Assert.Equal(2, seeded.UpperBound);
        Assert.Equal(3L, seeded[1]);

        // A shrinking EnsureUpperBound is a no-op: growth only, never truncation.
        seeded.EnsureUpperBound(1);
        Assert.Equal(2, seeded.UpperBound);
    }

    // ==============================================================================================
    //  24. OPTIONS - the preserved literal 200, read from configuration
    // ==============================================================================================

    /// <summary>
    /// The redraw-suppression threshold is the oracle's literal 200 [:L223, :L225], preserved as the
    /// DEFAULT and made overridable.
    /// </summary>
    /// <remarks>
    /// SUPPRESSION IS AN OPTIMISATION WITH NO EFFECT ON ANY VALUE, so overriding it cannot change a
    /// calculated result - which is precisely why it is safe to make configurable while the hardcoded
    /// LOCALE, for instance, is preserved as a default that callers may override rather than removed.
    /// </remarks>
    [Fact]
    public async Task RedrawSuppression_UsesThePreservedThresholdAndCanBeOverridden()
    {
        Assert.Equal(200, new ColumnExpressionOptions().RedrawSuppressionRowThreshold);

        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        ColumnExpressionEngine engine = CreateEngineOver(
            host,
            options: new ColumnExpressionOptions { RedrawSuppressionRowThreshold = 1 });

        Assert.True(engine.AddExp("n1", "n2 + 1") > 0);

        // Four rows against a threshold of one, so suppression is engaged and then released.
        Assert.Equal(RetCode.OK, engine.SetAlwaysCalc("n1", true));
        Assert.Equal(RetCode.OK, engine.AddVar("v", 1L));
        await engine.OnVarChangedAsync(engine.FindVarIndex("v"), false, Ct);
        await engine.OnDoItemChangedAsync(1L, "n2", host.Column("n2").Id, false, Ct);

        // Redraw is left ENABLED afterwards, whichever way the pass ended.
        Assert.NotEqual(false, host.RedrawEnabled);
    }


    // ==============================================================================================
    //  25. THE CACHED READ ACROSS ALL SIX TYPES, AND THE COERCION THAT TYPES A RAW CELL
    // ==============================================================================================

    /// <summary>
    /// The cached read has one arm per column type [:L720-L737], and five of the six render the compute
    /// object's value through <c>String()</c> before the write arm parses it back.
    /// </summary>
    /// <remarks>
    /// THE ROUND TRIP IS THE THING BEING TESTED. A value seeded into the compute object comes back out of
    /// the BOUND column unchanged, which can only happen if the rendering half and the parsing half are
    /// exact inverses. An asymmetry between them would show as a value that changes merely by passing
    /// through the cache - a divergence between the cached and uncached paths that neither path's own test
    /// would catch.
    /// </remarks>
    [Fact]
    public async Task CachedRead_RoundTripsEveryColumnTypeThroughTheComputeObject()
    {
        FakeDataWindowHost host = CreateAllTypesFixture();
        RecordingSyntaxMutator mutator = new(host);
        ColumnExpressionEngine engine = CreateEngineOver(host, mutator: mutator);

        (string Column, object? Seed)[] cases =
        [
            ("dec1", 12.5m),
            ("num1", 7L),
            ("str1", "cached"),
            ("dtm1", new DateTime(2024, 3, 5, 14, 7, 9, DateTimeKind.Unspecified)),
            ("dat1", new DateOnly(2024, 3, 5)),
            ("tim1", new TimeOnly(14, 7, 9)),
        ];

        foreach ((string column, object? seed) in cases)
        {
            Assert.True(engine.AddExp(column, "src") > 0);
            Assert.Equal(RetCode.OK, engine.SetCacheable(column, true));

            string computeName = engine.Expressions[^1].ComputeName;
            host.SetBufferValue(DwBuffer.Primary, 1L, host.Column(computeName).Id, seed);
        }

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));

        foreach ((string column, object? seed) in cases)
        {
            Assert.Equal(seed, host.GetBufferValue(DwBuffer.Primary, 1L, host.Column(column).Id));
        }
    }

    /// <summary>
    /// A cached expression on a column of an unknown type has no read arm, so the item is abandoned
    /// [:L735-L736].
    /// </summary>
    [Fact]
    public async Task CachedRead_AnUnknownColumnTypeAbandonsTheItem()
    {
        FakeDataWindowHost host = CreateAllTypesFixture();
        host.AddColumn("blob1", "blob");
        RecordingSyntaxMutator mutator = new(host);
        ColumnExpressionEngine engine = CreateEngineOver(host, mutator: mutator);

        Assert.True(engine.AddExp("blob1", "src") > 0);
        Assert.Equal(RetCode.OK, engine.SetCacheable("blob1", true));

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Null(host.GetBufferValue(DwBuffer.Primary, 1L, host.Column("blob1").Id));
    }

    /// <summary>
    /// A cache read that FAULTS is reported and the item abandoned [:L738-L740], which is what a compute
    /// object that has gone with the object model looks like.
    /// </summary>
    [Fact]
    public async Task CachedRead_AFaultingReadIsReportedAndAbandonsTheItem()
    {
        FakeDataWindowHost host = CreateAllTypesFixture();
        RecordingSyntaxMutator mutator = new(host);
        ColumnExpressionEngine engine = CreateEngineOver(host, mutator: mutator);

        Assert.True(engine.AddExp("dec1", "src") > 0);
        Assert.Equal(RetCode.OK, engine.SetCacheable("dec1", true));

        // Make the cached READ fault, which is what a compute object that went with the object model looks
        // like from here: the flag is still set, but nothing can be read through it.
        string computeName = engine.Expressions[0].ComputeName;
        Assert.NotNull(host.FindObject(computeName));
        host.Column(computeName).ColType = "blob";
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column(computeName).Id, new object());
        mutator.FailWith = "the object model no longer accepts this fragment";

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));

        // Nothing was written from a value that could not be read.
        Assert.Null(host.GetItemDecimal(1L, "dec1"));
    }

    /// <summary>
    /// A cell arriving in a representation other than its column's declared type is still typed correctly,
    /// because a provider may reasonably hand back any equivalent representation.
    /// </summary>
    /// <remarks>
    /// AN UNCONVERTIBLE CELL ANSWERS NULL RATHER THAN RAISING, which is what the typed getters do: a
    /// <c>GetItemDecimal</c> on a cell holding something unconvertible answers null, and every caller's next
    /// act is a null test either way.
    /// </remarks>
    [Theory]
    [InlineData("dec1", 3, "src * 2")]
    [InlineData("dec1", "4", "src * 2")]
    [InlineData("num1", 3.7d, "src + 1")]
    [InlineData("num1", "5", "src + 1")]
    [InlineData("str1", 42, "'x'")]
    [InlineData("str1", true, "'x'")]
    public async Task RawCoercion_TypesACellArrivingInAnEquivalentRepresentation(
        string column,
        object seed,
        string expression)
    {
        FakeDataWindowHost host = CreateAllTypesFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host);

        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("src").Id, 3m);
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column(column).Id, seed);

        Assert.True(engine.AddExp(column, expression) > 0);

        // The point is that the equality test against the existing value neither raises nor mis-compares,
        // so the calculation completes and writes.
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.NotNull(host.GetBufferValue(DwBuffer.Primary, 1L, host.Column(column).Id));
    }

    /// <summary>
    /// A temporal cell arriving as the OTHER temporal shape is converted rather than refused: a date read
    /// from a datetime keeps its date, and a time read from a datetime keeps its time.
    /// </summary>
    [Fact]
    public void RawCoercion_ConvertsBetweenTheTemporalShapes()
    {
        FakeDataWindowHost host = CreateAllTypesFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host);

        DateTime whole = new(2024, 3, 5, 14, 7, 9, DateTimeKind.Unspecified);

        // A DATE column holding a full datetime.
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("dat1").Id, whole);
        Assert.Equal("Date('2024-03-05')", engine.GetItemExpValue(1L, "dat1"));

        // A TIME column holding a full datetime.
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("tim1").Id, whole);
        Assert.StartsWith("Time('14:07:09", engine.GetItemExpValue(1L, "tim1"), StringComparison.Ordinal);

        // A DATETIME column holding a bare date, which acquires midnight.
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("dtm1").Id, new DateOnly(2024, 3, 5));
        Assert.StartsWith("DateTime('2024-03-05 00:00:00", engine.GetItemExpValue(1L, "dtm1"), StringComparison.Ordinal);

        // A temporal cell arriving as TEXT is parsed with the PowerScript conversion.
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("dat1").Id, "2024-03-05");
        Assert.Equal("Date('2024-03-05')", engine.GetItemExpValue(1L, "dat1"));
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("tim1").Id, "14:07:09");
        Assert.StartsWith("Time('14:07:09", engine.GetItemExpValue(1L, "tim1"), StringComparison.Ordinal);

        // A DATE column holding a datetime-SHAPED string keeps the date and discards the time.
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("dat1").Id, "2024-03-05 14:07:09");
        Assert.Equal("Date('2024-03-05')", engine.GetItemExpValue(1L, "dat1"));

        // And a TIME column holding one keeps the time and discards the date.
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("tim1").Id, "2024-03-05 14:07:09");
        Assert.StartsWith("Time('14:07:09", engine.GetItemExpValue(1L, "tim1"), StringComparison.Ordinal);

        // Unparseable text answers PowerScript's EPOCH SENTINELS, not null and not an exception.
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("dat1").Id, "not a date");
        Assert.Equal("Date('1900-01-01')", engine.GetItemExpValue(1L, "dat1"));
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("tim1").Id, "not a time");
        Assert.StartsWith("Time('00:00:00", engine.GetItemExpValue(1L, "tim1"), StringComparison.Ordinal);
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("dtm1").Id, "not a datetime");
        Assert.StartsWith("DateTime('1900-01-01 00:00:00", engine.GetItemExpValue(1L, "dtm1"), StringComparison.Ordinal);

        // A representation that cannot be a temporal value at all answers null, which renders as the null
        // literal rather than as the abort sentinel.
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("dat1").Id, 12345);
        Assert.NotEmpty(engine.GetItemExpValue(1L, "dat1"));
    }

    /// <summary>
    /// Unparseable text in a NUMERIC column answers ZERO, which is PowerScript's <c>Dec</c> and
    /// <c>Long</c> failure answer - not null, and not an exception.
    /// </summary>
    [Fact]
    public async Task RawCoercion_UnparseableNumericTextAnswersZero()
    {
        FakeDataWindowHost host = CreateAllTypesFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host);

        // A cell the HOST cannot type answers null, which renders as the null LITERAL rather than as the
        // abort sentinel - the three numeric arms read through the host's own typed getters, so what they
        // answer is the host's coercion and not this engine's.
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("dec1").Id, "not a number");
        Assert.NotEmpty(engine.GetItemExpValue(1L, "dec1"));
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("num1").Id, "not a number");
        Assert.NotEmpty(engine.GetItemExpValue(1L, "num1"));

        // THIS ENGINE'S OWN CONVERSION runs on the WRITE side, where an evaluated text is parsed back into
        // the column's type - and there, unparseable text answers ZERO, which is PowerScript's Dec and Long
        // failure answer rather than null or an exception.
        Assert.True(engine.AddExp("dec1", "'not a number'") > 0);
        Assert.True(engine.AddExp("num1", "'not a number'") > 0);
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(0m, host.GetItemDecimal(1L, "dec1"));
        Assert.Equal(0d, host.GetItemNumber(1L, "num1"));

        // A FRACTIONAL value written to an integral column TRUNCATES towards zero, which is PowerScript's
        // Long and not .NET's round-to-nearest. The difference shows at exactly one half.
        Assert.Equal(RetCode.OK, await engine.SetExpAsync("num1", "'2.5'", Ct));
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(2d, host.GetItemNumber(1L, "num1"));

        Assert.Equal(RetCode.OK, await engine.SetExpAsync("num1", "'-2.5'", Ct));
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(-2d, host.GetItemNumber(1L, "num1"));

        // A STRING column renders a non-text cell as text, and the STRING ARM READS THROUGH THE HOST'S
        // `GetItemString` [:L2352], so the rendering that reaches the literal is the HOST's coercion and not
        // this engine's - which is why the boolean below reads back capitalised.
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("str1").Id, 12.5m);
        Assert.Equal("'12.5'", engine.GetItemExpValue(1L, "str1"));
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("str1").Id, new DateOnly(2024, 3, 5));
        Assert.Equal("'2024-03-05'", engine.GetItemExpValue(1L, "str1"));
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("str1").Id, true);
        Assert.Equal("'True'", engine.GetItemExpValue(1L, "str1"));

        // THIS ENGINE'S OWN BOOLEAN RENDERING is PowerScript's lower-case `String(boolean)`, and it is
        // reached on the typed variable arms rather than on the cell arms: `of_addvar(readonly boolean)`
        // stores the boolean AS AN EXPRESSION, so what it stores is the rendering.
        Assert.Equal(RetCode.OK, engine.AddVar("flagTrue", true));
        Assert.Equal(RetCode.OK, engine.AddVar("flagFalse", false));
        Assert.Equal("true", engine.GetVarExp("flagTrue"));
        Assert.Equal("false", engine.GetVarExp("flagFalse"));
    }

    // ==============================================================================================
    //  26. THE PEER FAN OUT - links, and the context path through a peer
    // ==============================================================================================

    /// <summary>
    /// A variable that a peer references acquires a LINK, and a change to it fans out to that peer with
    /// <c>forcecalc</c> hard-coded TRUE [:L353-L356].
    /// </summary>
    /// <remarks>
    /// FORCE IS UNCONDITIONAL ON THE FAN OUT AND CONDITIONAL LOCALLY, which is the oracle's asymmetry: a
    /// peer cannot know whether the change originated in user input, so it recalculates everything that
    /// depends on the variable including its input-triggered expressions.
    /// </remarks>
    [Fact]
    public async Task ForeignVariable_AChangeFansOutToEveryLinkedPeer()
    {
        ExpressionSession session = NewSession();
        FakeDataWindowHost sourceHost = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        FakeDataWindowHost peerHost = FakeDataWindowFixtures.CreateColumnExpressionFixture();

        ColumnExpressionEngine source = CreateEngineOver(sourceHost, session: session);
        ColumnExpressionEngine peer = CreateEngineOver(peerHost, session: session);

        Assert.Equal(RetCode.OK, source.AddVar("shared", 2L));
        Assert.Equal(RetCode.OK, peer.AddForeignVar("shared", source));
        Assert.Equal(ExpressionVariableEnvironment.VAR_FOREIGN, peer.Variables[1].VarType);

        // The peer's expression names the foreign variable, which must use dynamic expansion.
        Assert.True(peer.AddExp("n1", "$$shared * 10") > 0);
        Assert.Equal(RetCode.OK, await peer.CalcAsync(1L, Ct));
        Assert.Equal(20m, peerHost.GetItemDecimal(1L, "n1"));

        // The source registered a LINK when the peer took the reference, so the change reaches the peer.
        Assert.Single(source.Variables[1].Links);
        Assert.Equal(RetCode.OK, await source.SetVarAsync("shared", 3L, true, Ct));
        Assert.Equal(30m, peerHost.GetItemDecimal(1L, "n1"));

        // The fan-out FORCES, so an input-triggered expression on the peer is reached as well.
        Assert.Equal(RetCode.OK, peer.SetRelativeInputColumn("n1", "n2"));
        Assert.Equal(RetCode.OK, await source.SetVarAsync("shared", 4L, true, Ct));
        Assert.Equal(40m, peerHost.GetItemDecimal(1L, "n1"));
    }

    /// <summary>
    /// A foreign variable cannot be expanded STATICALLY [:L1426], which is exactly what the specification
    /// says when it notes that a foreign reference needs dynamic expansion.
    /// </summary>
    [Fact]
    public void ForeignVariable_RefusesStaticExpansion()
    {
        ExpressionSession session = NewSession();
        FakeDataWindowHost sourceHost = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        FakeDataWindowHost targetHost = FakeDataWindowFixtures.CreateColumnExpressionFixture();

        ColumnExpressionEngine source = CreateEngineOver(sourceHost, session: session);
        ColumnExpressionEngine target = CreateEngineOver(targetHost, session: session);

        Assert.Equal(RetCode.OK, source.AddVar("far", 5L));
        Assert.Equal(RetCode.OK, target.AddForeignVar("far", source));

        Assert.Equal((int)RetCode.E_INVALID_ARGUMENT, target.AddExp("n1", "$far + 1"));
        Assert.NotNull(target.LastError);
        Assert.Equal(
            ExpressionErrorSite.VariableMacroForeignStaticExpansionUnsupported,
            target.LastError.Site);

        // The dynamic spelling of the same reference is accepted.
        Assert.True(target.AddExp("n1", "$$far + 1") > 0);
    }

    /// <summary>
    /// A duplicate foreign definition is refused [:L2122], as a duplicate local definition is.
    /// </summary>
    [Fact]
    public void ForeignVariable_CannotBeDefinedTwice()
    {
        ExpressionSession session = NewSession();
        FakeDataWindowHost sourceHost = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        FakeDataWindowHost targetHost = FakeDataWindowFixtures.CreateColumnExpressionFixture();

        ColumnExpressionEngine source = CreateEngineOver(sourceHost, session: session);
        ColumnExpressionEngine target = CreateEngineOver(targetHost, session: session);

        Assert.Equal(RetCode.OK, source.AddVar("far", 5L));
        Assert.Equal(RetCode.OK, target.AddForeignVar("far", source));
        Assert.Equal(RetCode.FAILED, target.AddForeignVar("far", source));

        Assert.NotNull(target.LastError);
        Assert.Equal(ExpressionErrorSite.DuplicateForeignVariableDefinition, target.LastError.Site);
        Assert.Equal(1, target.Variables.UpperBound);

        // A LOCAL name already in use is a duplicate too.
        Assert.Equal(RetCode.OK, target.AddVar("local", 1L));
        Assert.Equal(RetCode.FAILED, target.AddForeignVar("local", source));
    }

    /// <summary>
    /// A foreign variable's own expression cannot be replaced [:L1690 region answers
    /// <see cref="RetCode.E_NO_SUPPORT"/>], because the value belongs to the peer.
    /// </summary>
    [Fact]
    public async Task ForeignVariable_ItsExpressionCannotBeReplacedLocally()
    {
        ExpressionSession session = NewSession();
        FakeDataWindowHost sourceHost = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        FakeDataWindowHost targetHost = FakeDataWindowFixtures.CreateColumnExpressionFixture();

        ColumnExpressionEngine source = CreateEngineOver(sourceHost, session: session);
        ColumnExpressionEngine target = CreateEngineOver(targetHost, session: session);

        Assert.Equal(RetCode.OK, source.AddVar("far", 5L));
        Assert.Equal(RetCode.OK, target.AddForeignVar("far", source));

        Assert.Equal(RetCode.E_NO_SUPPORT, await target.SetVarExpAsync("far", "9", Ct));
        Assert.Equal(RetCode.E_NO_SUPPORT, await target.SetVarAsync("far", 9L, Ct));
    }

    /// <summary>
    /// A context reference resolved against a PEER reads that peer's variable and that peer's row, which is
    /// the capability the undocumented <c>@</c> sigil exists for.
    /// </summary>
    [Fact]
    public async Task ContextVariableReference_AgainstAPeer_ResolvesOnThePeer()
    {
        ExpressionSession session = NewSession();
        FakeDataWindowHost peerHost = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();

        ColumnExpressionEngine peer = CreateEngineOver(peerHost, session: session);
        ColumnExpressionEngine engine = CreateEngineOver(host, session: session);

        // The variable exists ONLY on the peer.
        Assert.Equal(RetCode.OK, peer.AddVar("peeronly", 21L));
        Assert.Equal(0, engine.FindVarIndex("peeronly"));

        // Resolved through the peer's own host-interface entry point, which is what the context path uses.
        Assert.Equal("21", peer.CalcVarExpValue(peer.FindVarIndex("peeronly"), 1L, null, null));

        // And the local engine cannot see it, so a LOCAL dynamic reference to it aborts.
        Assert.True(engine.AddExp("n1", "$$peeronly + 1") > 0);
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(0m, host.GetItemDecimal(1L, "n1"));
        Assert.NotNull(engine.LastError);
        Assert.Equal(ExpressionErrorSite.PreprocessVariableUndefined, engine.LastError.Site);
    }

    // ==============================================================================================
    //  27. THE FUNCTION REFERENCE LIST - dedupe by full name, and rotate to the end
    // ==============================================================================================

    /// <summary>
    /// The same macro function appearing twice in one expression is recorded ONCE, and a repeat MOVES the
    /// existing entry to the end of the list rather than adding a second [:L1352-L1365].
    /// </summary>
    /// <remarks>
    /// THE ROTATION IS NOT COSMETIC. The function pass runs BACKWARDS [:L2226] so that a nested call is
    /// substituted before its enclosing one, and the rotation is what keeps an outer call after the inner
    /// calls it contains. Two entries for one token would also substitute it twice, the second time against
    /// text the first had already rewritten.
    /// </remarks>
    [Fact]
    public async Task FunctionReferences_AreDedupedByFullNameAndRotatedToTheEnd()
    {
        RecordingMacroChannel channel = new();
        channel.Answers["Twice"] = 3m;

        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host, channel: channel, session: NewSession());

        // The identical token twice: one reference, and therefore one invocation.
        Assert.True(engine.AddExp("n1", "$Twice(n2) + $Twice(n2)") > 0);
        Assert.Equal(1, engine.Expressions[0].Fns.UpperBound);
        Assert.Equal("Twice", engine.Expressions[0].Fns[1].Name);
        Assert.Equal("$Twice(n2)", engine.Expressions[0].Fns[1].FullName);

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(6m, host.GetItemDecimal(1L, "n1"));
        Assert.Single(channel.Invocations);
    }

    /// <summary>
    /// A NESTED macro call produces two references, and the inner one is substituted first so the outer
    /// one's token can still be found [:L2312-L2320 rewrites the outer token as the inner is resolved].
    /// </summary>
    [Fact]
    public async Task FunctionReferences_ANestedCallResolvesInnermostFirst()
    {
        RecordingMacroChannel channel = new();
        channel.Answers["Inner"] = 2m;
        channel.Answers["Outer"] = 20m;

        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host, channel: channel, session: NewSession());

        Assert.True(engine.AddExp("n1", "$Outer($Inner(n2))") > 0);
        Assert.Equal(2, engine.Expressions[0].Fns.UpperBound);

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(20m, host.GetItemDecimal(1L, "n1"));

        // The inner call went first, and the outer received the inner's ANSWER as its argument.
        Assert.Equal(2, channel.Invocations.Count);
        Assert.Equal("Inner", channel.Invocations[0].Name);
        Assert.Equal("Outer", channel.Invocations[1].Name);
        Assert.Equal("2", channel.Invocations[1].Arguments[1]);
    }

    /// <summary>
    /// A macro function with NO arguments is accepted for a non-built-in name, because only the two
    /// built-in sentinels carry an argument-count rule [:L1386-L1401].
    /// </summary>
    [Fact]
    public async Task FunctionReferences_ANonBuiltInMayTakeNoArguments()
    {
        RecordingMacroChannel channel = new();
        channel.Answers["Now"] = 5m;

        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host, channel: channel, session: NewSession());

        Assert.True(engine.AddExp("n1", "$Now()") > 0);
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(5m, host.GetItemDecimal(1L, "n1"));

        MacroInvocation invocation = Assert.Single(channel.Invocations);
        Assert.True(invocation.Arguments.IsEmpty);
    }

    // ==============================================================================================
    //  28. HOST FAULTS - a probe that raises is a stale handle, not a crash
    // ==============================================================================================

    /// <summary>
    /// The object-model probes swallow whatever a host raises, because the oracle's own probes are
    /// <c>catch(throwable ex)</c> - PowerBuilder's ROOT type - and the host contract does not fix which
    /// exception a missing object produces.
    /// </summary>
    /// <remarks>
    /// NARROWING TO A CHOSEN TYPE WOULD TURN A HOST'S IMPLEMENTATION CHOICE INTO A CRASH on a path the
    /// oracle recovers from. Cancellation still propagates, so a cancelled request cannot silently complete.
    /// </remarks>
    [Fact]
    public async Task HostFaults_ADescribeThatRaisesIsTreatedAsAMissingObject()
    {
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        RecordingSyntaxMutator mutator = new(host);
        ColumnExpressionEngine engine = CreateEngineOver(host, mutator: mutator);

        Assert.True(engine.AddExp("n1", "n2 * 2") > 0);
        Assert.Equal(RetCode.OK, engine.SetCacheable("n1", true));

        // Every Describe now raises, which is how a host may legitimately report an unknown property.
        host.DescribeOverride = static _ => throw new KeyNotFoundException("no such property");

        // The calculation still completes rather than propagating the fault.
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));

        host.DescribeOverride = null;
    }

    /// <summary>
    /// A calculation with a cancelled token stops rather than completing with a fabricated value.
    /// </summary>
    /// <remarks>
    /// CANCELLATION IS THE ONE THING THE OBJECT-MODEL CATCHES MUST NOT SWALLOW, and it is excluded from them
    /// by name. A request that was abandoned must not appear to have produced a value.
    /// </remarks>
    [Fact]
    public async Task Cancellation_PropagatesRatherThanBeingSwallowed()
    {
        ColumnExpressionEngine engine = CreateEngine(out FakeDataWindowHost host);

        Assert.True(engine.AddExp("n1", "n2 + 1") > 0);

        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await engine.CalcAsync(1L, cts.Token));

        Assert.Equal(0m, host.GetItemDecimal(1L, "n1"));
    }

    /// <summary>
    /// An engine that was never initialised has no host and no evaluator, and says so rather than answering
    /// a fabricated value.
    /// </summary>
    /// <remarks>
    /// FAIL FAST, NOT A NULL-COALESCING FALLBACK (AAP section 0.1.4). The oracle's equivalent is structural:
    /// <c>_of_Evaluate</c> is a member of the service base, so it cannot exist before the host does.
    /// </remarks>
    [Fact]
    public void Uninitialised_EngineRefusesRatherThanFabricating()
    {
        ColumnExpressionEngine engine = new();

        Assert.Null(engine.Evaluator);
        Assert.False(engine.Enabled);
        Assert.Throws<InvalidOperationException>(() => engine.AddExp("n1", "n2 + 1"));
    }

    // ==============================================================================================
    //  29. THE INHERITANCE MERGES - step 8a of the static path [:L1438-L1465]
    // ==============================================================================================

    /// <summary>
    /// A statically substituted variable whose OWN value carries a macro drags that macro's references
    /// into the outer expression, because their text is now the outer expression's text [:L1439-L1464].
    /// </summary>
    /// <remarks>
    /// THIS IS WHAT MAKES A STATIC REFERENCE ABLE TO CARRY A DYNAMIC ONE. The outer binding is static -
    /// <c>$inner</c> is gone from the stored text and no later assignment to <c>inner</c> can reach it -
    /// while the reference it dragged in is dynamic, so assigning to <c>rate</c> still propagates. Both
    /// halves are asserted below, because getting one right and the other wrong is the likely failure.
    /// </remarks>
    [Fact]
    public async Task StaticExpansion_InheritsTheSubstitutedVariablesOwnReferences()
    {
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host, session: NewSession());

        Assert.Equal(RetCode.OK, engine.AddVar("rate", 2L));
        Assert.Equal(RetCode.OK, engine.AddVarExp("inner", "$$rate + 1"));

        int index = engine.AddExp("n1", "$inner * 10");
        Assert.True(index > 0);

        ColumnExpressionData data = engine.Expressions[index - 1];

        // The static token is GONE and the inherited dynamic one stands in its place, wrapped.
        Assert.Equal("($$rate + 1) * 10", data.Exp);
        Assert.True(data.HasMacro);
        Assert.Equal(1, data.Vars.UpperBound);
        Assert.Equal("rate", data.Vars[1].Name);
        Assert.Equal("$$rate", data.Vars[1].FullName);

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(30m, host.GetItemDecimal(1L, "n1"));

        // The INHERITED reference is dynamic, so mutation reaches it.
        Assert.Equal(RetCode.OK, await engine.SetVarAsync("rate", 5L, true, Ct));
        Assert.Equal(60m, host.GetItemDecimal(1L, "n1"));

        // The OUTER binding was static, so mutating `inner` cannot reach it - the stored text no longer
        // names it, which is the mechanism rather than a policy.
        Assert.Equal(RetCode.OK, await engine.SetVarExpAsync("inner", "999", true, Ct));
        Assert.Equal(60m, host.GetItemDecimal(1L, "n1"));
    }

    /// <summary>
    /// The variable merge compares WHOLE STRUCTURES [:L1440], so two inherited references that are equal
    /// field for field are recorded ONCE.
    /// </summary>
    /// <remarks>
    /// THE VARIABLE NAMES BELOW ARE LONG ON PURPOSE. A substitution that LENGTHENS the expression pushes
    /// the trailer out of the scan bound and truncates the scan - preserved defect (a) - so a test that
    /// needs a SECOND reference parsed after the first substitution must make the substitution shorten
    /// the text. A twelve-character name replaced by <c>($$r)</c> does exactly that.
    /// </remarks>
    [Fact]
    public async Task StaticExpansion_InheritedVariablesDeduplicateByWholeStructure()
    {
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host, session: NewSession());

        Assert.Equal(RetCode.OK, engine.AddVar("r", 2L));
        Assert.Equal(RetCode.OK, engine.AddVarExp("aaaaaaaaaaaa", "$$r"));
        Assert.Equal(RetCode.OK, engine.AddVarExp("bbbbbbbbbbbb", "$$r"));

        int index = engine.AddExp("n1", "$aaaaaaaaaaaa + $bbbbbbbbbbbb");
        Assert.True(index > 0);

        ColumnExpressionData data = engine.Expressions[index - 1];
        Assert.Equal("($$r) + ($$r)", data.Exp);

        // ONE entry, not two: the second inherited reference is equal field for field to the first.
        Assert.Equal(1, data.Vars.UpperBound);
        Assert.Equal("r", data.Vars[1].Name);

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(4m, host.GetItemDecimal(1L, "n1"));
    }

    /// <summary>
    /// ... and two inherited references that DIFFER are both recorded, so the dedupe is a structural
    /// comparison and not a name-blind collapse.
    /// </summary>
    [Fact]
    public async Task StaticExpansion_InheritedVariablesThatDifferAreBothRecorded()
    {
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host, session: NewSession());

        Assert.Equal(RetCode.OK, engine.AddVar("r", 2L));
        Assert.Equal(RetCode.OK, engine.AddVar("s", 3L));
        Assert.Equal(RetCode.OK, engine.AddVarExp("aaaaaaaaaaaa", "$$r"));
        Assert.Equal(RetCode.OK, engine.AddVarExp("bbbbbbbbbbbb", "$$s"));

        int index = engine.AddExp("n1", "$aaaaaaaaaaaa + $bbbbbbbbbbbb");
        Assert.True(index > 0);

        ColumnExpressionData data = engine.Expressions[index - 1];
        Assert.Equal("($$r) + ($$s)", data.Exp);
        Assert.Equal(2, data.Vars.UpperBound);
        Assert.Equal("r", data.Vars[1].Name);
        Assert.Equal("s", data.Vars[2].Name);

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(5m, host.GetItemDecimal(1L, "n1"));
    }

    // ==============================================================================================
    //  30. THE COMPUTE CACHE ACROSS of_setexp - the three arms at [:L1661-L1674]
    // ==============================================================================================

    /// <summary>
    /// Re-binding a CACHEABLE expression RE-POINTS the existing compute object rather than recreating it
    /// [:L1665-L1666].
    /// </summary>
    [Fact]
    public async Task SetExp_OnACacheableExpression_RepointsTheExistingCompute()
    {
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        RecordingSyntaxMutator mutator = new(host);
        ColumnExpressionEngine engine = CreateEngineOver(host, mutator, session: NewSession());

        Assert.True(engine.AddExp("n1", "n2 + 1") > 0);
        Assert.Equal(RetCode.OK, engine.SetCacheable("n1", true));

        string computeName = engine.Expressions[0].ComputeName;
        Assert.Equal(computeName, Assert.Single(mutator.ComputeNames));

        Assert.Equal(RetCode.OK, await engine.SetExpAsync("n1", "n2 + 2", Ct));

        // A RE-POINT, not a second create: the fragment is an expression assignment on the existing
        // object, and no new compute name was reported.
        Assert.Equal(computeName, Assert.Single(mutator.ComputeNames));
        Assert.Contains(
            mutator.Syntax,
            fragment => fragment.StartsWith(computeName + ".expression=", StringComparison.Ordinal));
        Assert.True(engine.Expressions[0].Cacheable);
    }

    /// <summary>
    /// Re-binding a cacheable expression to one that HAS A MACRO destroys the compute outright
    /// [:L1662-L1663], because the DataWindow engine cannot evaluate this service's variables.
    /// </summary>
    /// <remarks>
    /// <c>cacheable</c> STAYS SET. The oracle destroys the object and leaves the flag alone, so a later
    /// re-bind back to a macro-free expression re-creates it - which is the arm the next test covers.
    /// </remarks>
    [Fact]
    public async Task SetExp_ToAMacroExpression_DestroysTheComputeAndKeepsTheFlag()
    {
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        RecordingSyntaxMutator mutator = new(host);
        ColumnExpressionEngine engine = CreateEngineOver(host, mutator, session: NewSession());

        Assert.Equal(RetCode.OK, engine.AddVar("v", 4L));
        Assert.True(engine.AddExp("n1", "n2 + 1") > 0);
        Assert.Equal(RetCode.OK, engine.SetCacheable("n1", true));

        string computeName = engine.Expressions[0].ComputeName;

        Assert.Equal(RetCode.OK, await engine.SetExpAsync("n1", "n2 + $$v", Ct));

        Assert.Contains("Destroy " + computeName, mutator.Syntax);
        Assert.True(engine.Expressions[0].Cacheable);
        Assert.True(engine.Expressions[0].HasMacro);
    }

    /// <summary>
    /// A macro-bearing expression cannot be made cacheable AT ALL: the request is refused with
    /// <see cref="RetCode.E_NO_SUPPORT"/> and the flag stays clear [:L1878-L1882].
    /// </summary>
    /// <remarks>
    /// THE ONLY <c>E_NO_SUPPORT</c> IN THE ENGINE'S ANSWER SET, and it is a genuine capability statement
    /// rather than a failure: the DataWindow engine evaluates a compute object, and it knows nothing about
    /// this service's variables or its application callbacks, so there is nothing it could cache.
    /// </remarks>
    [Fact]
    public void SetCacheable_OnAMacroExpression_IsRefusedAsUnsupported()
    {
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        RecordingSyntaxMutator mutator = new(host);
        ColumnExpressionEngine engine = CreateEngineOver(host, mutator, session: NewSession());

        Assert.Equal(RetCode.OK, engine.AddVar("v", 4L));
        Assert.True(engine.AddExp("n1", "n2 + $$v") > 0);

        Assert.Equal(RetCode.E_NO_SUPPORT, engine.SetCacheable("n1", true));

        Assert.False(engine.Expressions[0].Cacheable);
        Assert.Empty(mutator.ComputeNames);
        Assert.NotNull(engine.LastError);
        Assert.Equal(
            ExpressionErrorSite.CreateExpressionCacheMacroExpansionUnsupported,
            engine.LastError.Site);
    }

    /// <summary>
    /// Re-binding a cacheable expression whose compute object is ABSENT RE-CREATES it [:L1668].
    /// </summary>
    /// <remarks>
    /// HOW THAT STATE IS REACHED, AND WHY IT IS SIMULATED. A real DataWindow's <c>Destroy</c> removes the
    /// object and leaves <c>cacheable</c> set [:L1662-L1663], so a cacheable expression with no compute
    /// object is an ordinary state rather than a corrupt one - it is exactly where a re-bind lands after a
    /// spell as a macro expression. The fake host cannot remove an object it has declared, so the
    /// post-destroy state is set directly by re-pointing <c>ComputeName</c> at a name the host does not
    /// have. That is a stand-in for the removal and not for the behaviour under test.
    /// </remarks>
    [Fact]
    public async Task SetExp_WithTheComputeObjectAbsent_RecreatesIt()
    {
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        RecordingSyntaxMutator mutator = new(host);
        ColumnExpressionEngine engine = CreateEngineOver(host, mutator, session: NewSession());

        Assert.True(engine.AddExp("n1", "n2 + 1") > 0);
        Assert.Equal(RetCode.OK, engine.SetCacheable("n1", true));

        string original = engine.Expressions[0].ComputeName;
        Assert.Equal(original, Assert.Single(mutator.ComputeNames));

        engine.Expressions[0].ComputeName = original + "_gone";

        Assert.Equal(RetCode.OK, await engine.SetExpAsync("n1", "n2 + 3", Ct));

        Assert.Equal(2, mutator.ComputeNames.Count);
        Assert.Equal(original + "_gone", mutator.ComputeNames[1]);
        Assert.False(engine.Expressions[0].HasMacro);
        Assert.True(engine.Expressions[0].Cacheable);
    }

    /// <summary>
    /// A cache update that FAILS is reported and then IGNORED [:L1670-L1672] - the call still answers
    /// <see cref="RetCode.OK"/>.
    /// </summary>
    /// <remarks>
    /// PRESERVED BEHAVIOUR, AND IT IS NOT AN OVERSIGHT LIKE defect (f): the oracle shows the dialog and
    /// carries straight on to <c>ResetColumns</c> and the recalculation, so a stale cache does not fail the
    /// re-bind. What it shares with defect (f) is that the caller cannot tell from the return code; what it
    /// does NOT share is a subsequent claim that the cache is valid.
    /// </remarks>
    [Fact]
    public async Task SetExp_ACacheUpdateFailureIsReportedAndThenIgnored()
    {
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        RecordingSyntaxMutator mutator = new(host);
        ColumnExpressionEngine engine = CreateEngineOver(host, mutator, session: NewSession());

        Assert.True(engine.AddExp("n1", "n2 + 1") > 0);
        Assert.Equal(RetCode.OK, engine.SetCacheable("n1", true));

        mutator.FailWith = "the object model refused the fragment";
        engine.ClearErrors();

        Assert.Equal(RetCode.OK, await engine.SetExpAsync("n1", "n2 + 2", Ct));

        Assert.NotNull(engine.LastError);
        Assert.Equal(ExpressionErrorSite.UpdateExpressionCacheFailed, engine.LastError.Site);
        Assert.Equal("n2 + 2", engine.Expressions[0].Exp);
    }

    // ==============================================================================================
    //  31. THE SIGIL PROJECTION - the three internal bits folded onto C-04's wire enum
    // ==============================================================================================

    /// <summary>
    /// <c>SigilsFor</c> recovers the DYNAMIC bit from the captured <c>FullName</c>, which is the only
    /// place it survives - <c>vardata</c> [:L50-L56] has no field for it.
    /// </summary>
    /// <param name="fullName">The token as the scanner captured it, sigils included [:L1412].</param>
    /// <param name="isCtx">The <c>isctx</c> field.</param>
    /// <param name="isMacro">The <c>ismacro</c> field.</param>
    /// <param name="expectedDynamic">Whether the doubled sigil is present.</param>
    [Theory]
    [InlineData("$$v", false, true, true)]
    [InlineData("$v", false, true, false)]
    [InlineData("@$$v", true, true, true)]
    [InlineData("@$v", true, true, false)]
    [InlineData("@v", true, false, false)]
    [InlineData("v", false, false, false)]
    [InlineData("", false, true, false)]
    [InlineData("$", false, true, false)]
    [InlineData("@$", true, true, false)]
    public void SigilsFor_RecoversTheThreeBitsFromTheCapturedToken(
        string fullName,
        bool isCtx,
        bool isMacro,
        bool expectedDynamic)
    {
        VariableReference reference = new()
        {
            Name = "v",
            FullName = fullName,
            IsCtx = isCtx,
            IsMacro = isMacro,
        };

        ExpansionSigils sigils = ColumnExpressionEngine.SigilsFor(reference);

        Assert.Equal(isCtx, sigils.IsCtx);
        Assert.Equal(isMacro, sigils.IsMacro);
        Assert.Equal(expectedDynamic, sigils.IsDynamic);
    }

    /// <summary>
    /// A reference with no macro sigil at all carries NO expansion timing, and the dynamic bit is
    /// suppressed rather than read off the token - the bit is <c>isMacro AND doubled</c>.
    /// </summary>
    [Fact]
    public void SigilsFor_WithoutTheMacroBit_SuppressesTheDynamicBit()
    {
        VariableReference reference = new()
        {
            Name = "v",
            FullName = "$$v",
            IsCtx = false,
            IsMacro = false,
        };

        ExpansionSigils sigils = ColumnExpressionEngine.SigilsFor(reference);

        Assert.False(sigils.IsDynamic);
        Assert.Equal(ExpansionMode.Unspecified, sigils.ProjectVariableReference().Mode);
    }

    /// <summary>
    /// The three bits fold onto C-04's five wire modes, and a STATIC CONTEXT reference folds onto a
    /// REJECTION rather than a mode - the parser refuses it at :L1417.
    /// </summary>
    [Fact]
    public void SigilsFor_ProjectsOntoTheWireModes()
    {
        static ExpansionSigils Sigils(string fullName) =>
            ColumnExpressionEngine.SigilsFor(
                new VariableReference { Name = "v", FullName = fullName, IsMacro = true, IsCtx = fullName.StartsWith('@') });

        Assert.Equal(ExpansionMode.Dynamic, Sigils("$$v").ProjectVariableReference().Mode);
        Assert.Equal(ExpansionMode.Static, Sigils("$v").ProjectVariableReference().Mode);
        Assert.Equal(ExpansionMode.Dynamic, Sigils("@$$v").ProjectVariableReference().Mode);

        ExpansionModeProjection rejected = Sigils("@$v").ProjectVariableReference();
        Assert.Equal(ExpansionRejection.ContextStaticExpansion, rejected.Rejection);
        Assert.Equal(ExpansionMode.Unspecified, rejected.Mode);
    }

    // ==============================================================================================
    //  32. CELL REPRESENTATION COERCION - the typed read half of the write arms [:L768-L854]
    // ==============================================================================================

    /// <summary>
    /// A DECIMAL column recognises its own value in ANY numeric representation a provider might hand back,
    /// so the write arm's "did it change" test [:L771] is about the VALUE and not about the CLR type.
    /// </summary>
    /// <param name="raw">The representation the cell holds.</param>
    /// <remarks>
    /// WHERE THE COERCION LIVES, STATED SO THE TEST IS NOT MISREAD. The engine reads the old value through
    /// the HOST'S typed getter, exactly as the oracle reads through <c>GetItemDecimal</c> [:L771], so the
    /// representation is collapsed by the host and the engine's own contribution is to compare VALUES. This
    /// test asserts that end to end, which is what a caller sees: an implementation that compared
    /// representations - or that read the raw buffer and typed it itself, diverging from the host's own
    /// coercion - would rewrite an unchanged cell on every pass and fire an item-changed event the oracle
    /// does not fire.
    /// </remarks>
    [Theory]
    [InlineData((short)7)]
    [InlineData(7)]
    [InlineData(7L)]
    [InlineData((byte)7)]
    [InlineData((sbyte)7)]
    [InlineData((uint)7)]
    [InlineData((ushort)7)]
    [InlineData((ulong)7)]
    [InlineData(7.0d)]
    [InlineData(7.0f)]
    [InlineData("7")]
    public async Task CellCoercion_ADecimalColumnRecognisesEveryNumericRepresentation(object raw)
    {
        FakeDataWindowHost host = CreateAllTypesFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host);

        Assert.True(engine.AddExp("dec1", "src + 7") > 0);
        Assert.Equal(RetCode.OK, engine.SetTriggerEvent("dec1", true));

        int changes = 0;
        host.ItemChangedHandler = (_, _, _) =>
        {
            changes++;
            return 0L;
        };

        long columnId = host.Column("dec1").Id;
        host.SetBufferValue(DwBuffer.Primary, 1L, columnId, raw);

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(0, changes);

        // NEGATIVE CONTROL: a genuinely different value IS recognised as changed, so the assertion above
        // is not passing because the write arm never runs.
        host.SetBufferValue(DwBuffer.Primary, 1L, columnId, 8m);
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(1, changes);
    }

    /// <summary>
    /// An INTEGER column does the same through <c>Long</c> rather than <c>Dec</c> [:L783].
    /// </summary>
    /// <param name="raw">The representation the cell holds.</param>
    [Theory]
    [InlineData((short)7)]
    [InlineData(7)]
    [InlineData(7L)]
    [InlineData((byte)7)]
    [InlineData((sbyte)7)]
    [InlineData((uint)7)]
    [InlineData((ushort)7)]
    [InlineData((ulong)7)]
    [InlineData(7.0d)]
    [InlineData(7.0f)]
    [InlineData("7")]
    public async Task CellCoercion_AnIntegerColumnRecognisesEveryNumericRepresentation(object raw)
    {
        FakeDataWindowHost host = CreateAllTypesFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host);

        Assert.True(engine.AddExp("num1", "src + 7") > 0);
        Assert.Equal(RetCode.OK, engine.SetTriggerEvent("num1", true));

        int changes = 0;
        host.ItemChangedHandler = (_, _, _) =>
        {
            changes++;
            return 0L;
        };

        long columnId = host.Column("num1").Id;
        host.SetBufferValue(DwBuffer.Primary, 1L, columnId, raw);

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(0, changes);

        host.SetBufferValue(DwBuffer.Primary, 1L, columnId, 8L);
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(1, changes);
    }

    /// <summary>
    /// The integer arm's old-value read TRUNCATES towards zero rather than rounding, because the write arm
    /// compares against PowerScript's <c>Long</c> [:L783].
    /// </summary>
    /// <remarks>
    /// THE DIFFERENCE SHOWS AT EXACTLY ONE HALF, and only there, which is why it is asserted rather than
    /// assumed: a cell holding 7.5 reads back as 7 under truncation and as 8 under .NET's default
    /// round-to-nearest, so an expression yielding 7 is UNCHANGED under the oracle's rule and CHANGED under
    /// the other one.
    /// </remarks>
    [Fact]
    public async Task CellCoercion_TheIntegerOldValueReadTruncatesRatherThanRounding()
    {
        FakeDataWindowHost host = CreateAllTypesFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host);

        Assert.True(engine.AddExp("num1", "src + 7") > 0);
        Assert.Equal(RetCode.OK, engine.SetTriggerEvent("num1", true));

        int changes = 0;
        host.ItemChangedHandler = (_, _, _) =>
        {
            changes++;
            return 0L;
        };

        long columnId = host.Column("num1").Id;

        host.SetBufferValue(DwBuffer.Primary, 1L, columnId, 7.5d);
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(0, changes);

        // AND THE NEGATIVE SIDE truncates towards zero as well, not towards minus infinity.
        Assert.Equal(RetCode.OK, await engine.SetExpAsync("num1", "src - 7", Ct));
        changes = 0;
        host.SetBufferValue(DwBuffer.Primary, 1L, columnId, -7.5d);
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(0, changes);
    }

    /// <summary>
    /// Every representation of one temporal instant renders to the SAME expression literal, and a
    /// representation that cannot be a temporal value at all renders as the NULL literal.
    /// </summary>
    /// <remarks>
    /// THE ASSERTIONS ARE FORMAT-AGNOSTIC ON PURPOSE. The literal's exact spelling belongs to
    /// <c>Validators/ValueToExpression.cs</c> and is asserted there; what belongs here is that the
    /// coercion in front of it collapses the representations, which is proved by comparing the renderings
    /// with each other rather than with a transcribed string.
    /// </remarks>
    [Fact]
    public void CellCoercion_TemporalColumnsCollapseEveryRepresentation()
    {
        FakeDataWindowHost host = CreateAllTypesFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host);

        long dtm = host.Column("dtm1").Id;
        long dat = host.Column("dat1").Id;
        long tim = host.Column("tim1").Id;

        DateTime instant = new(2024, 3, 5, 14, 7, 9, DateTimeKind.Unspecified);

        host.SetBufferValue(DwBuffer.Primary, 1L, dtm, null);
        string nullDateTime = engine.GetItemExpValue(1L, "dtm1");

        host.SetBufferValue(DwBuffer.Primary, 1L, dtm, instant);
        string viaDateTime = engine.GetItemExpValue(1L, "dtm1");
        Assert.NotEqual(nullDateTime, viaDateTime);

        host.SetBufferValue(DwBuffer.Primary, 1L, dtm, new DateTimeOffset(instant, TimeSpan.FromHours(2)));
        Assert.Equal(viaDateTime, engine.GetItemExpValue(1L, "dtm1"));

        host.SetBufferValue(DwBuffer.Primary, 1L, dtm, "2024-03-05 14:07:09");
        Assert.Equal(viaDateTime, engine.GetItemExpValue(1L, "dtm1"));

        // A DATE arrives where a DATETIME is expected: midnight is supplied rather than refused.
        host.SetBufferValue(DwBuffer.Primary, 1L, dtm, new DateOnly(2024, 3, 5));
        string viaDateOnly = engine.GetItemExpValue(1L, "dtm1");
        host.SetBufferValue(DwBuffer.Primary, 1L, dtm, new DateTime(2024, 3, 5, 0, 0, 0, DateTimeKind.Unspecified));
        Assert.Equal(viaDateOnly, engine.GetItemExpValue(1L, "dtm1"));

        host.SetBufferValue(DwBuffer.Primary, 1L, dat, new DateOnly(2024, 3, 5));
        string viaDate = engine.GetItemExpValue(1L, "dat1");
        host.SetBufferValue(DwBuffer.Primary, 1L, dat, instant);
        Assert.Equal(viaDate, engine.GetItemExpValue(1L, "dat1"));
        host.SetBufferValue(DwBuffer.Primary, 1L, dat, new DateTimeOffset(instant, TimeSpan.FromHours(2)));
        Assert.Equal(viaDate, engine.GetItemExpValue(1L, "dat1"));
        host.SetBufferValue(DwBuffer.Primary, 1L, dat, "2024-03-05");
        Assert.Equal(viaDate, engine.GetItemExpValue(1L, "dat1"));

        host.SetBufferValue(DwBuffer.Primary, 1L, tim, new TimeOnly(14, 7, 9));
        string viaTime = engine.GetItemExpValue(1L, "tim1");
        host.SetBufferValue(DwBuffer.Primary, 1L, tim, instant);
        Assert.Equal(viaTime, engine.GetItemExpValue(1L, "tim1"));
        host.SetBufferValue(DwBuffer.Primary, 1L, tim, new DateTimeOffset(instant, TimeSpan.FromHours(2)));
        Assert.Equal(viaTime, engine.GetItemExpValue(1L, "tim1"));
        host.SetBufferValue(DwBuffer.Primary, 1L, tim, new TimeSpan(14, 7, 9));
        Assert.Equal(viaTime, engine.GetItemExpValue(1L, "tim1"));
        host.SetBufferValue(DwBuffer.Primary, 1L, tim, "14:07:09");
        Assert.Equal(viaTime, engine.GetItemExpValue(1L, "tim1"));

        // A SPAN LONGER THAN A DAY has no `time` representation, so it answers null rather than wrapping.
        host.SetBufferValue(DwBuffer.Primary, 1L, tim, null);
        string nullTime = engine.GetItemExpValue(1L, "tim1");
        host.SetBufferValue(DwBuffer.Primary, 1L, tim, TimeSpan.FromHours(26));
        Assert.Equal(nullTime, engine.GetItemExpValue(1L, "tim1"));
        host.SetBufferValue(DwBuffer.Primary, 1L, tim, TimeSpan.FromHours(-1));
        Assert.Equal(nullTime, engine.GetItemExpValue(1L, "tim1"));
        host.SetBufferValue(DwBuffer.Primary, 1L, tim, 12345);
        Assert.Equal(nullTime, engine.GetItemExpValue(1L, "tim1"));
    }

    /// <summary>
    /// A cell whose representation has NO value in the column's type reads as EMPTY, which is what makes
    /// <c>of_calcempty</c> fill it [:L1985-L2016].
    /// </summary>
    /// <remarks>
    /// <c>of_calcempty</c> IS THE OBSERVABLE FOR THE NULL-ANSWERING COERCION ARMS, and a good one: an
    /// unrepresentable cell is not "zero" and not "an error" - it is absent, and absent is precisely the
    /// state that entry point exists to fill.
    /// </remarks>
    [Fact]
    public async Task CellCoercion_AnUnrepresentableCellReadsAsEmpty()
    {
        FakeDataWindowHost host = CreateAllTypesFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host);

        Assert.True(engine.AddExp("dec1", "src + 7") > 0);
        long columnId = host.Column("dec1").Id;

        // A REPRESENTABLE value is not empty, so the empty pass leaves it alone.
        host.SetBufferValue(DwBuffer.Primary, 1L, columnId, 3m);
        Assert.Equal(RetCode.OK, await engine.CalcEmptyAsync(1L, Ct));
        Assert.Equal(3m, host.GetItemDecimal(1L, "dec1"));

        // NOT-A-NUMBER, INFINITY and a magnitude past the decimal range have no decimal representation at
        // all, so each reads as empty and the pass fills it.
        foreach (object unrepresentable in new object[] { double.NaN, double.PositiveInfinity, 1e300d })
        {
            host.SetBufferValue(DwBuffer.Primary, 1L, columnId, unrepresentable);
            Assert.Equal(RetCode.OK, await engine.CalcEmptyAsync(1L, Ct));
            Assert.Equal(7m, host.GetItemDecimal(1L, "dec1"));
        }

        Assert.Equal(RetCode.OK, engine.RemoveAll());
        Assert.True(engine.AddExp("num1", "src + 7") > 0);
        long numberId = host.Column("num1").Id;

        host.SetBufferValue(DwBuffer.Primary, 1L, numberId, 3L);
        Assert.Equal(RetCode.OK, await engine.CalcEmptyAsync(1L, Ct));
        Assert.Equal(3d, host.GetItemNumber(1L, "num1"));

        // AN UNSIGNED VALUE PAST long.MaxValue has no `long` representation either.
        foreach (object unrepresentable in new object[] { ulong.MaxValue, double.NaN, 1e300d })
        {
            host.SetBufferValue(DwBuffer.Primary, 1L, numberId, unrepresentable);
            Assert.Equal(RetCode.OK, await engine.CalcEmptyAsync(1L, Ct));
            Assert.Equal(7d, host.GetItemNumber(1L, "num1"));
        }
    }

    // ==============================================================================================
    //  33. THE STALE HANDLE RECOVERY - [:L699-L713], the oracle's own answer to a replaced object model
    // ==============================================================================================

    /// <summary>
    /// A cached handle that has gone stale is RE-RESOLVED and its compute object RE-CREATED [:L707-L710],
    /// because a replaced DataWindow object model invalidates both at once.
    /// </summary>
    /// <remarks>
    /// HOW THE STALENESS IS MODELLED. There is no validity API, so the oracle PROVOKES the failure - it
    /// touches a property inside <c>try/catch(throwable)</c> [:L699-L703] - and an unset handle is the state
    /// that probe reports for a replaced model. Clearing the handle and re-pointing the compute name at an
    /// absent object reproduces exactly the pair of facts the recovery has to repair.
    /// </remarks>
    [Fact]
    public async Task StaleHandle_IsReResolvedAndTheComputeRecreated()
    {
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        RecordingSyntaxMutator mutator = new(host);
        ColumnExpressionEngine engine = CreateEngineOver(host, mutator, session: NewSession());

        Assert.True(engine.AddExp("n1", "n2 + 1") > 0);
        Assert.Equal(RetCode.OK, engine.SetCacheable("n1", true));

        string computeName = engine.Expressions[0].ComputeName;
        Assert.Equal(computeName, Assert.Single(mutator.ComputeNames));

        engine.Expressions[0].Dwo = null;
        engine.Expressions[0].ComputeName = computeName + "_gone";

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));

        Assert.NotNull(engine.Expressions[0].Dwo);
        Assert.Equal("n1", engine.Expressions[0].Dwo!.Name);
        Assert.Equal(2, mutator.ComputeNames.Count);
        Assert.Equal(computeName + "_gone", mutator.ComputeNames[1]);
    }

    /// <summary>
    /// A re-creation that FAILS is reported and the item ABANDONED [:L711-L713] - and unlike the re-bind
    /// path, this one does return rather than carrying on with a cache it knows is missing.
    /// </summary>
    [Fact]
    public async Task StaleHandle_AFailedRecreationIsReportedAndAbandonsTheItem()
    {
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        RecordingSyntaxMutator mutator = new(host);
        ColumnExpressionEngine engine = CreateEngineOver(host, mutator, session: NewSession());

        Assert.True(engine.AddExp("n1", "n2 + 1") > 0);
        Assert.Equal(RetCode.OK, engine.SetCacheable("n1", true));

        decimal? before = host.GetItemDecimal(1L, "n1");

        engine.Expressions[0].Dwo = null;
        engine.Expressions[0].ComputeName = engine.Expressions[0].ComputeName + "_gone";
        mutator.FailWith = "the object model refused the fragment";
        engine.ClearErrors();

        // THE PASS STILL ANSWERS OK: an abandoned ITEM is not a failed pass [:L1164-L1174].
        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));

        Assert.NotNull(engine.LastError);
        Assert.Equal(ExpressionErrorSite.CreateExpressionCacheFailed, engine.LastError.Site);
        Assert.Equal(before, host.GetItemDecimal(1L, "n1"));
    }

    // ==============================================================================================
    //  34. THE EMPTY PASS AND THE DUPLICATE GROUP - [:L2019-L2061]
    // ==============================================================================================

    /// <summary>
    /// When the empty pass calculates one member of a duplicate group it CLEARS THE DIRTY FLAG of every
    /// other member [:L2058-L2060], so a column with two expressions is filled exactly once.
    /// </summary>
    /// <remarks>
    /// THE INDEX EXPRESSION HERE IS THE CORRECT ONE, unlike its counterpart at :L697 - which is why the two
    /// sites are annotated separately in the engine rather than sharing a helper. The observable is which of
    /// the two expressions won: the first, because phase two walks the registry in registration order.
    /// </remarks>
    [Fact]
    public async Task CalcEmpty_CalculatingOneMemberOfADuplicateGroupSuppressesTheRest()
    {
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host);

        int first = engine.AddExp("n1", "n2 + 1");
        int second = engine.AddExp("n1", "n3 + 100");
        Assert.True(first > 0);
        Assert.True(second > 0);

        // Each member's group list EXCLUDES the member itself [:L487, :L1572], so a pair gives each an
        // upper bound of one.
        Assert.Equal(1, engine.Expressions[first - 1].DupExps.UpperBound);
        Assert.Equal(1, engine.Expressions[second - 1].DupExps.UpperBound);

        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("n2").Id, 5m);
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("n3").Id, 7m);

        // The bound column must be EMPTY for the pass to consider it at all; the fixture seeds zero, which
        // for a decimal column is a value rather than an absence.
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("n1").Id, null);

        Assert.Equal(RetCode.OK, await engine.CalcEmptyAsync(1L, Ct));

        Assert.Equal(6m, host.GetItemDecimal(1L, "n1"));
    }

    // ==============================================================================================
    //  35. THE REVERSE INDEX'S SECOND EDGE KIND, AND THE INPUT-LIST FILTER [:L280-L315]
    // ==============================================================================================

    /// <summary>
    /// A variable whose own value names a column records a VARIABLE edge on that column - but ONLY when the
    /// variable is mirrored by a peer [:L284-L287], because that index exists solely to fan the change out
    /// across the DataWindow boundary.
    /// </summary>
    [Fact]
    public async Task ReverseIndex_ALinkedVariableRecordsAVariableEdgeAndFansOut()
    {
        ExpressionSession session = NewSession();
        FakeDataWindowHost sourceHost = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        FakeDataWindowHost peerHost = FakeDataWindowFixtures.CreateColumnExpressionFixture();

        ColumnExpressionEngine source = CreateEngineOver(sourceHost, session: session);
        ColumnExpressionEngine peer = CreateEngineOver(peerHost, session: session);

        // The variable's VALUE names a column, which is the second and different kind of edge [:L280-L292].
        Assert.Equal(RetCode.OK, source.AddVarExp("shared", "n2 * 2"));
        Assert.Equal(RetCode.OK, peer.AddForeignVar("shared", source));
        Assert.Single(source.Variables[1].Links);

        // THE DELEGATION EVALUATES AT THE OWNER'S CURRENT ROW, NOT THE CALLER'S. The four-argument arity
        // [:L2385] carries no row, so a variable whose value names a column is read against whatever row the
        // owning DataWindow is sitting on - which is why this has to be stated for the test to mean
        // anything, and why a row-dependent foreign variable is a sharp edge in the legacy design rather
        // than in this port.
        sourceHost.CurrentRow = 1L;

        Assert.True(peer.AddExp("n1", "$$shared * 10") > 0);

        // Adding an expression rebuilds the reverse index, which is where the variable edge is recorded.
        Assert.True(source.AddExp("n1", "n3 + 1") > 0);

        long n2Id = sourceHost.Column("n2").Id;
        long n3Id = sourceHost.Column("n3").Id;

        // THE INDEX IS BUILT LAZILY, ON THE FIRST CHANGE TO A COLUMN [:L218-L219], so the change comes
        // first and the state is inspected afterwards - reading it before would find an unbuilt entry.
        sourceHost.SetBufferValue(DwBuffer.Primary, 1L, n2Id, 3m);
        await source.OnDoItemChangedAsync(1L, "n2", n2Id, false, Ct);

        // The edge does its job: the change reached the peer through the variable.
        Assert.Equal(60m, peerHost.GetItemDecimal(1L, "n1"));

        ColumnCalcData n2State = source.ColumnCalcStates[(int)n2Id - 1];
        Assert.Equal(1, n2State.VarIndexes.UpperBound);
        Assert.Equal(1, n2State.VarIndexes[1]);
        Assert.Equal(ColumnCalcFlag.CLC_YES, n2State.Flag);

        // A column that no variable names records NO variable edge.
        await source.OnDoItemChangedAsync(1L, "n3", n3Id, false, Ct);
        ColumnCalcData n3State = source.ColumnCalcStates[(int)n3Id - 1];
        Assert.Equal(0, n3State.VarIndexes.UpperBound);
    }

    /// <summary>
    /// An UNLINKED variable that names a column records no variable edge, because the expressions that
    /// depend on it were already collected by the indirect walk [:L288-L290].
    /// </summary>
    [Fact]
    public async Task ReverseIndex_AnUnlinkedVariableRecordsNoEdgeButStillPropagates()
    {
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host);

        Assert.Equal(RetCode.OK, engine.AddVarExp("doubled", "n2 * 2"));
        Assert.True(engine.AddExp("n1", "$$doubled + 1") > 0);

        long n2Id = host.Column("n2").Id;
        Assert.Empty(engine.Variables[1].Links);

        // The index entry is built by the change itself [:L218-L219], so the change comes first.
        host.SetBufferValue(DwBuffer.Primary, 1L, n2Id, 4m);
        await engine.OnDoItemChangedAsync(1L, "n2", n2Id, false, Ct);

        // The INDIRECT edge carried it even though no variable edge was recorded.
        Assert.Equal(9m, host.GetItemDecimal(1L, "n1"));

        ColumnCalcData n2State = engine.ColumnCalcStates[(int)n2Id - 1];
        Assert.Equal(0, n2State.VarIndexes.UpperBound);
        Assert.Equal(ColumnCalcFlag.CLC_YES, n2State.Flag);
        Assert.Equal(1, n2State.Indexes.UpperBound);
    }

    /// <summary>
    /// With an input list AND another trigger route, the input list decides PER COLUMN whether a
    /// programmatic change is suppressed [:L301-L315].
    /// </summary>
    /// <remarks>
    /// THE FILTER'S LOGIC READS BACKWARDS AND THAT IS THE POINT. <c>bMatched</c> means "this column reaches
    /// the expression ONLY through the input list", so a match SUPPRESSES a programmatic change rather than
    /// permitting it. The three cases below are the whole truth table: a listed column programmatically
    /// (suppressed), an unlisted one programmatically (fires, because it arrived by the other route), and a
    /// listed one from input (fires).
    /// </remarks>
    [Fact]
    public async Task InputFilter_DecidesPerColumnWhenAnotherTriggerRouteExists()
    {
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host);

        Assert.True(engine.AddExp("n1", "n2 + n3 + 1") > 0);
        Assert.Equal(RetCode.OK, engine.SetAlwaysCalc("n1", true));
        Assert.Equal(RetCode.OK, engine.SetRelativeInputColumn("n1", "n2"));

        long n2Id = host.Column("n2").Id;
        long n3Id = host.Column("n3").Id;

        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("n1").Id, 0m);
        host.SetBufferValue(DwBuffer.Primary, 1L, n2Id, 10m);
        host.SetBufferValue(DwBuffer.Primary, 1L, n3Id, 20m);

        // A LISTED column, changed PROGRAMMATICALLY: suppressed.
        await engine.OnDoItemChangedAsync(1L, "n2", n2Id, false, Ct);
        Assert.Equal(0m, host.GetItemDecimal(1L, "n1"));

        // An UNLISTED column, changed programmatically: it reached the expression through always-calculate,
        // so it fires.
        await engine.OnDoItemChangedAsync(1L, "n3", n3Id, false, Ct);
        Assert.Equal(31m, host.GetItemDecimal(1L, "n1"));

        // A LISTED column, changed FROM INPUT: fires.
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("n1").Id, 0m);
        host.SetBufferValue(DwBuffer.Primary, 1L, n2Id, 11m);
        await engine.OnDoItemChangedAsync(1L, "n2", n2Id, true, Ct);
        Assert.Equal(32m, host.GetItemDecimal(1L, "n1"));
    }

    // ==============================================================================================
    //  36. A CONTEXT SERVICE THAT IS NOT THIS TYPE - the handle lookup at ContextFor/HandleOf
    // ==============================================================================================

    /// <summary>
    /// A context service supplied through the SYNCHRONOUS interface resolves to its session handle even
    /// when it is not a <see cref="ColumnExpressionEngine"/>, so <c>@name</c> reads through it.
    /// </summary>
    /// <remarks>
    /// THE CONTEXT TRIPLE IS THE ONLY WAY IN. Every internal preprocess call passes <c>this</c> as the
    /// context service, so a foreign implementation of the host contract can only arrive through
    /// <c>_of_calcvarexpvalue</c>'s six-argument arity [:L2362] - which is exactly what a peer service calls
    /// when it delegates a foreign variable back to its owner.
    /// </remarks>
    [Fact]
    public void ContextService_AForeignImplementationResolvesToItsSessionHandle()
    {
        ExpressionSession session = NewSession();
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host, session: session);

        RecordingExpressionServiceHost context = new();
        context.Items["remote"] = "7";
        DataWindowHandle handle = session.Register(context);
        Assert.False(handle.IsEmpty);

        Assert.Equal(RetCode.OK, engine.AddVarExp("v", "@remote + 1"));
        int index = engine.FindVarIndex("v");
        Assert.Equal(1, index);

        string value = engine.CalcVarExpValue(
            1L,
            host.DwObject("n1"),
            index,
            1L,
            host.DwObject("n2"),
            context);

        Assert.Equal("8", value);
        Assert.Contains("remote", context.Reads);
    }

    /// <summary>
    /// ... and one that is NOT registered in the session resolves to the ABSENT handle, so every resolution
    /// against it is BLOCKED and the calculation aborts (AAP section 0.6.2.3).
    /// </summary>
    /// <remarks>
    /// BLOCKED, NOT GUESSED. The alternative - reading through an unregistered peer because it happens to be
    /// reachable in this process - is exactly the pointer dereference across a boundary that the AAP refuses,
    /// and it would answer a value that is right in one deployment and wrong in another.
    /// </remarks>
    [Fact]
    public void ContextService_AnUnregisteredImplementationIsBlocked()
    {
        ExpressionSession session = NewSession();
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host, session: session);

        RecordingExpressionServiceHost outsider = new();
        outsider.Items["remote"] = "9";

        Assert.Equal(RetCode.OK, engine.AddVarExp("v", "@remote + 1"));
        int index = engine.FindVarIndex("v");

        string value = engine.CalcVarExpValue(
            1L,
            host.DwObject("n1"),
            index,
            1L,
            host.DwObject("n2"),
            outsider);

        Assert.Equal(string.Empty, value);
        Assert.Empty(outsider.Reads);
    }

    /// <summary>
    /// The FOUR-argument arity delegates a FOREIGN variable one hop further out [:L2384-L2386], with no row
    /// of its own to read.
    /// </summary>
    [Fact]
    public void HostInterface_TheFourArgumentArityDelegatesAForeignVariable()
    {
        ExpressionSession session = NewSession();
        FakeDataWindowHost sourceHost = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        FakeDataWindowHost targetHost = FakeDataWindowFixtures.CreateColumnExpressionFixture();

        ColumnExpressionEngine source = CreateEngineOver(sourceHost, session: session);
        ColumnExpressionEngine target = CreateEngineOver(targetHost, session: session);

        Assert.Equal(RetCode.OK, source.AddVar("far", 5L));
        Assert.Equal(RetCode.OK, target.AddForeignVar("far", source));

        int index = target.FindVarIndex("far");
        Assert.Equal(1, index);

        Assert.Equal("5", target.CalcVarExpValue(index, 1L, targetHost.DwObject("n1"), target));
    }

    /// <summary>
    /// A CONTEXT MACRO variable resolved against a peer that is not a <see cref="ColumnExpressionEngine"/>
    /// goes through the session's synchronous helper and still participates fully [:L2181-L2183].
    /// </summary>
    /// <remarks>
    /// THE TWO ROUTES DIFFER AND BOTH ARE REAL. When the peer IS an engine the call stays asynchronous, so a
    /// macro invocation inside the peer can still be awaited; when it is not, the session's synchronous
    /// helper is used and carries the BLOCKED reporting. Asserting only the first would leave the contract's
    /// own published shape - an interface, not a class - untested.
    /// </remarks>
    [Fact]
    public void ContextService_ANonEngineePeerResolvesAContextMacroVariable()
    {
        ExpressionSession session = NewSession();
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host, session: session);

        RecordingExpressionServiceHost context = new();
        context.AddVariable("far", "5");
        Assert.False(session.Register(context).IsEmpty);

        Assert.Equal(RetCode.OK, engine.AddVarExp("v", "@$$far + 1"));
        int index = engine.FindVarIndex("v");

        string value = engine.CalcVarExpValue(
            1L,
            host.DwObject("n1"),
            index,
            1L,
            host.DwObject("n2"),
            context);

        Assert.Equal("6", value);
        Assert.Equal(1, Assert.Single(context.ResolvedIndexes));
    }

    /// <summary>
    /// ... and a context reference with NO context service at all is refused rather than resolved against
    /// this engine's own variables.
    /// </summary>
    [Fact]
    public void ContextService_AnAbsentContextServiceRefusesTheReference()
    {
        ExpressionSession session = NewSession();
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host, session: session);

        // A LOCAL variable of the same name exists, so a resolution that fell back to this engine would
        // answer 5 - which is exactly the silently-wrong answer the refusal prevents.
        Assert.Equal(RetCode.OK, engine.AddVar("far", 5L));
        Assert.Equal(RetCode.OK, engine.AddVarExp("v", "@$$far + 1"));

        int index = engine.FindVarIndex("v");
        Assert.Equal(2, index);

        string value = engine.CalcVarExpValue(
            1L,
            host.DwObject("n1"),
            index,
            1L,
            host.DwObject("n2"),
            null);

        Assert.Equal(string.Empty, value);
        Assert.NotNull(engine.LastError);
    }

    // ==============================================================================================
    //  37. THE LAST FEW ARMS - a recalculating re-bind, an unreadable buffer, a nested argument list
    // ==============================================================================================

    /// <summary>
    /// The THREE-argument <c>of_setexp</c> recalculates EVERY ROW [:L1678-L1686], and does so without
    /// setting the row-calculation flag, so each item goes through the stack-based re-entry rule.
    /// </summary>
    [Fact]
    public async Task SetExp_WithRecalculation_RecalculatesEveryRow()
    {
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host);

        Assert.True(host.RowCount() > 1);
        Assert.True(engine.AddExp("n1", "n2 + 1") > 0);

        for (long row = 1; row <= host.RowCount(); row++)
        {
            host.SetBufferValue(DwBuffer.Primary, row, host.Column("n2").Id, row * 10m);
        }

        // The two-argument arity does NOT recalculate, so nothing lands yet.
        Assert.Equal(RetCode.OK, await engine.SetExpAsync("n1", "n2 + 2", Ct));
        Assert.Equal(0m, host.GetItemDecimal(1L, "n1"));

        // The three-argument arity does, on every row.
        Assert.Equal(RetCode.OK, await engine.SetExpAsync("n1", "n2 + 3", true, Ct));
        Assert.False(engine.IsRowCalculating);

        for (long row = 1; row <= host.RowCount(); row++)
        {
            Assert.Equal((row * 10m) + 3m, host.GetItemDecimal(row, "n1"));
        }
    }

    /// <summary>
    /// A cached read of a TEMPORAL column whose compute object is unreachable is treated as NULL rather than
    /// raising, so the item is abandoned instead of the pass failing [:L720-L737].
    /// </summary>
    /// <remarks>
    /// THE THREE TEMPORAL ARMS READ THE VALUE BUFFER DIRECTLY, because the host contract publishes by-name
    /// typed getters for decimal, number and string only - so they are the arms where an unreachable object
    /// surfaces as an exception rather than as a null, and they are the reason that read is guarded at all.
    /// </remarks>
    [Fact]
    public async Task CachedRead_AnUnreadableTemporalBufferIsTreatedAsNull()
    {
        FakeDataWindowHost host = CreateAllTypesFixture();
        RecordingSyntaxMutator mutator = new(host);
        ColumnExpressionEngine engine = CreateEngineOver(host, mutator);

        Assert.True(engine.AddExp("dtm1", "'2024-03-05 14:07:09'") > 0);
        Assert.Equal(RetCode.OK, engine.SetCacheable("dtm1", true));

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        object? afterFirstPass = host.GetBufferValue(DwBuffer.Primary, 1L, host.Column("dtm1").Id);

        // The compute object goes with the object model; the flag and the handle stay.
        engine.Expressions[0].ComputeName = engine.Expressions[0].ComputeName + "_gone";
        engine.ClearErrors();

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));

        // Nothing was written from a value that could not be read, and nothing was reported as an error
        // either: an unreadable buffer is a null cell, and a null cell renders as the abort sentinel.
        Assert.Equal(afterFirstPass, host.GetBufferValue(DwBuffer.Primary, 1L, host.Column("dtm1").Id));
        Assert.Null(engine.LastError);
    }

    /// <summary>
    /// A macro argument list may contain NESTED brackets, and the scanner counts them rather than stopping at
    /// the first close [:L1319-L1350].
    /// </summary>
    /// <remarks>
    /// BRACKET COUNTING WITH QUOTE TRACKING IS WHAT MAKES AN ARGUMENT AN EXPRESSION rather than a token: the
    /// specification's own <c>$FormatPrice(n2, $精度)</c> passes an expanded variable, and a nested call or a
    /// parenthesised sub-expression is the same capability one step further.
    /// </remarks>
    [Fact]
    public async Task MacroFunction_AnArgumentListMayContainNestedBrackets()
    {
        RecordingMacroChannel channel = new();
        channel.Answers["Wrap"] = 42m;

        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        ColumnExpressionEngine engine = CreateEngineOver(host, channel: channel, session: NewSession());

        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("n2").Id, 4m);
        host.SetBufferValue(DwBuffer.Primary, 1L, host.Column("n3").Id, 6m);

        Assert.True(engine.AddExp("n1", "$Wrap((n2 + n3), if(n2 > 0, 'a', 'b'))") > 0);

        ColumnExpressionData data = engine.Expressions[0];
        Assert.Equal(1, data.Fns.UpperBound);
        Assert.Equal("Wrap", data.Fns[1].Name);
        Assert.Equal(2, data.Fns[1].Args.Length);
        Assert.Equal("(n2 + n3)", data.Fns[1].Args[0]);
        Assert.Equal("if(n2 > 0, 'a', 'b')", data.Fns[1].Args[1]);

        Assert.Equal(RetCode.OK, await engine.CalcAsync(1L, Ct));
        Assert.Equal(42m, host.GetItemDecimal(1L, "n1"));

        MacroInvocation invocation = Assert.Single(channel.Invocations);
        Assert.Equal(2, invocation.Arguments.UpperBound);
        Assert.Equal("10", invocation.Arguments[1]);
        Assert.Equal("a", invocation.Arguments[2]);
    }

}
