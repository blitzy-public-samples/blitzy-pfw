// =====================================================================================================
//  ColumnExpressionServiceContractTests - THE C-04 TRANSPORT AND SESSION FACADE SUITE
//  ---------------------------------------------------------------------------------------------------
//  SUBJECT   PowerFramework.DataServices.Grpc.ColumnExpressionService
//  ORACLE    ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru  (2,435 lines)
//            ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc.sru            (of_setenabled)
//            ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru              (the event surface)
//            docs/n_cst_dwsvc_columnexp.md                                         (the authoritative
//                                                                                   expansion spec)
//
//  WHY THIS SUITE IS SEPARATE FROM ColumnExpressionEngineTests. That suite characterises the ENGINE -
//  the parse routine, the expansion algebra and the calculation walk. This one characterises the
//  BOUNDARY: the session facade that replaces `foreignvardata.expsvc`, the projection of every legacy
//  structure onto the wire, the two INVERTED duplex streams, and the resolution ladder that answers a
//  defined code for every structurally impossible state instead of degrading gracefully. The split
//  mirrors the contract split itself - C-04 is deliberately versioned apart from C-03 (AAP section
//  0.4.3), so its acceptance evidence is kept apart too.
//
//  WHAT IS ASSERTED HERE AND NOWHERE ELSE
//    1. The golden pair - docs section 静态展开 fixes a result at 5 FOREVER while 动态展开 moves it to
//       6 - reproduced END TO END THROUGH THE RPC SURFACE, not against the engine object. This is the
//       acceptance condition for AAP section 0.1.3 G4(b), whose whole point is that the distinction
//       "does not survive naive serialization".
//    2. Expansion mode is a PER-REFERENCE property. One expression carrying both sigils is driven over
//       the wire and each reference resolves independently.
//    3. The payload carries ALL THREE components - the unexpanded source text, the bind-time snapshot
//       and the live environment - because an already-expanded string makes a static binding
//       indistinguishable from a literal.
//    4. A veto surfaces as FAILED (-1) and NEVER as PREVENT (+1) [n_cst_dwsvc.sru:L89-L94].
//    5. A cross-session foreign reference is BLOCKED with a defined error, never approximated
//       [:L80-L83].
//    6. The macro channel is inverted, strictly synchronous, and its arguments are ONE-BASED on the
//       oracle side [:L2258-L2263].
//    7. The trace payload's '>'-delimited stack and its "(null)" sentinel travel verbatim [:L752-L759].
//
//  TEMPORAL AND ORDERING DETERMINISM. Nothing here reads a clock or depends on wall time: the trace
//  timestamps are fixed at the Unix epoch and every wait is a bounded poll on an observable count, so a
//  recording taken from this suite is reproducible - which is the Golden-Master technique's one hard
//  prerequisite (AAP section 0.6.7).
// =====================================================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Options;
using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Expressions;
using PowerFramework.DataServices.Grpc;
using PowerFramework.Shared.Kernel;
using Xunit;
using AnyValue = PowerFramework.Contracts.Common.V1.AnyValue;
using EventGate = PowerFramework.DataServices.Domain.EventGate;
using Svc = PowerFramework.DataServices.Grpc.ColumnExpressionService;
using WireDate = PowerFramework.Contracts.Common.V1.DateValue;
using WireDateTime = PowerFramework.Contracts.Common.V1.DateTimeValue;
using WireRetCode = PowerFramework.Contracts.Common.V1.RetCode.Types.Value;
using WireTime = PowerFramework.Contracts.Common.V1.TimeValue;

namespace PowerFramework.DataServices.Tests;

// -----------------------------------------------------------------------------------------------------
//  TEST DOUBLES
// -----------------------------------------------------------------------------------------------------

/// <summary>
/// A minimal <see cref="ServerCallContext"/> carrying settable request headers, so a duplex channel
/// can be identified from call metadata exactly as it is in production.
/// </summary>
internal sealed class C04CallContext(Metadata? headers = null, CancellationToken cancellationToken = default)
    : ServerCallContext
{
    private readonly Metadata _headers = headers ?? [];
    private readonly CancellationToken _token = cancellationToken;

    protected override string MethodCore => "/dataservices.v1.ColumnExpressionService/Adhoc";

    protected override string HostCore => "localhost:5102";

    protected override string PeerCore => "ipv4:127.0.0.1:0";

    protected override DateTime DeadlineCore => DateTime.MaxValue;

    protected override Metadata RequestHeadersCore => _headers;

    protected override CancellationToken CancellationTokenCore => _token;

    protected override Metadata ResponseTrailersCore { get; } = [];

    protected override Status StatusCore { get; set; }

    protected override WriteOptions? WriteOptionsCore { get; set; }

    protected override AuthContext AuthContextCore { get; } =
        new("adhoc", new Dictionary<string, List<AuthProperty>>(StringComparer.Ordinal));

    protected override ContextPropagationToken CreatePropagationTokenCore(
        ContextPropagationOptions? options) =>
        throw new NotSupportedException("Propagation is not exercised by this suite.");

    protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) =>
        Task.CompletedTask;
}

/// <summary>A server stream writer that records every message and may answer each one.</summary>
internal sealed class C04StreamWriter<T> : IServerStreamWriter<T>
{
    private readonly List<T> _written = [];
    private readonly Func<T, Task>? _onWrite;

    public C04StreamWriter(Func<T, Task>? onWrite = null) => _onWrite = onWrite;

    public WriteOptions? WriteOptions { get; set; }

    public IReadOnlyList<T> Written
    {
        get
        {
            lock (_written)
            {
                return [.. _written];
            }
        }
    }

    public Task WriteAsync(T message)
    {
        lock (_written)
        {
            _written.Add(message);
        }

        return _onWrite is null ? Task.CompletedTask : _onWrite(message);
    }
}

/// <summary>A client-to-server stream the test pushes messages into and completes explicitly.</summary>
internal sealed class C04StreamReader<T> : IAsyncStreamReader<T>
{
    private readonly Channel<T> _channel = Channel.CreateUnbounded<T>();

    public T Current { get; private set; } = default!;

    public void Push(T message) => Assert.True(_channel.Writer.TryWrite(message));

    public void Complete() => _channel.Writer.TryComplete();

    public async Task<bool> MoveNext(CancellationToken cancellationToken)
    {
        if (await _channel.Reader.WaitToReadAsync(cancellationToken))
        {
            if (_channel.Reader.TryRead(out T? message))
            {
                Current = message;
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Serves the column-expression fixture for any name except the literal <c>"missing"</c>, which answers
/// null so the partial-open refusal is drivable.
/// </summary>
internal sealed class C04HostFactory : IDataWindowHostFactory
{
    private readonly List<FakeDataWindowHost> _created = [];

    public IReadOnlyList<FakeDataWindowHost> Created => _created;

    public DataWindowServiceHost? Create(string dataWindowName)
    {
        if (string.Equals(dataWindowName, "missing", StringComparison.Ordinal))
        {
            return null;
        }

        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        _created.Add(host);
        return host;
    }
}

// -----------------------------------------------------------------------------------------------------
//  THE SUITE
// -----------------------------------------------------------------------------------------------------

public sealed class ColumnExpressionServiceContractTests
{
    private const string ThisMonth = "本月读数";
    private const string LastMonth = "上月读数";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Harness(
        Svc Service,
        C04HostFactory Hosts,
        MacroInvocationRouter Macro,
        ExpressionTraceBroker Trace,
        ExpressionSessionRegistry Sessions,
        ColumnExpressionEventRelay Relay);

    private static Harness NewHarness()
    {
        DataServicesOptions options = new();
        ExpressionTraceBroker trace = new();
        ExpressionSessionRegistry sessions = new(
            Options.Create(options),
            timeProvider: null,
            traceSink: trace);

        C04HostFactory hosts = new();
        MacroInvocationRouter macro = new();
        ColumnExpressionEventRelay relay = new();

        Svc service = new(
            Options.Create(options),
            sessions,
            hosts,
            macro,
            trace,
            relay,
            UnresolvedPageResolver.Instance);

        return new Harness(service, hosts, macro, trace, sessions, relay);
    }

    /// <summary>Opens a session over <paramref name="names"/> and returns the minted handles.</summary>
    private static async Task<(string SessionId, IReadOnlyList<string> Handles)> OpenAsync(
        Svc service,
        params string[] names)
    {
        OpenExpressionSessionRequest request = new();
        request.DatawindowHandles.AddRange(names);

        OpenExpressionSessionResponse response =
            await service.OpenExpressionSession(request, new C04CallContext());

        Assert.Equal(WireRetCode.Ok, response.RetCode);
        return (response.SessionId, [.. response.DatawindowHandles]);
    }

    private static async Task EnableAsync(Svc service, string sessionId, string handle)
    {
        SetEnabledResponse response = await service.SetEnabled(
            new SetEnabledRequest { SessionId = sessionId, DatawindowHandle = handle, Enabled = true },
            new C04CallContext());

        Assert.Equal(WireRetCode.Ok, response.RetCode);
        Assert.True(response.Changed);
        Assert.True(response.Enabled);
    }

    // =================================================================================================
    //  1. THE GOLDEN PAIR, DRIVEN ENTIRELY THROUGH THE WIRE SURFACE
    //     docs/n_cst_dwsvc_columnexp.md sections 静态展开 (5) and 动态展开 (6)
    // =================================================================================================

    [Fact]
    public async Task StaticExpansion_ThroughTheServiceSurface_StaysAtFive()
    {
        Harness h = NewHarness();
        (string session, IReadOnlyList<string> handles) = await OpenAsync(h.Service, "dwA");
        string dw = handles[0];
        await EnableAsync(h.Service, session, dw);
        C04CallContext ctx = new();

        Assert.Equal(
            WireRetCode.Ok,
            (await h.Service.AddVariableExpression(
                new AddVariableExpressionRequest
                {
                    SessionId = session,
                    DatawindowHandle = dw,
                    Name = ThisMonth,
                    Exp = "5",
                },
                ctx)).RetCode);

        Assert.Equal(
            WireRetCode.Ok,
            (await h.Service.AddVariable(
                new AddVariableRequest
                {
                    SessionId = session,
                    DatawindowHandle = dw,
                    Name = LastMonth,
                    Value = new VarValue { LongValue = 0L },
                },
                ctx)).RetCode);

        // BOTH references static.
        Assert.Equal(
            WireRetCode.Ok,
            (await h.Service.AddVariableExpression(
                new AddVariableExpressionRequest
                {
                    SessionId = session,
                    DatawindowHandle = dw,
                    Name = "num1",
                    Exp = "$" + LastMonth + " + $" + ThisMonth,
                },
                ctx)).RetCode);

        GetVariableExpressionResponse stored = await h.Service.GetVariableExpression(
            new GetVariableExpressionRequest { SessionId = session, DatawindowHandle = dw, Name = "num1" },
            ctx);

        // THE MECHANISM: the names are GONE from the stored text.
        Assert.Equal("(0) + (5)", stored.Exp);
        Assert.DoesNotContain(LastMonth, stored.Exp, StringComparison.Ordinal);

        AddExpressionResponse added = await h.Service.AddExpression(
            new AddExpressionRequest
            {
                SessionId = session,
                DatawindowHandle = dw,
                ColumnName = "n1",
                Exp = "$$num1",
            },
            ctx);

        Assert.Equal(WireRetCode.Ok, added.RetCode);
        Assert.True(added.Index > 0, "of_addexp returns the ONE-BASED index, not a return code.");

        FakeDataWindowHost host = h.Hosts.Created[0];

        Assert.Equal(WireRetCode.Ok, (await CalcRowAsync(h.Service, session, dw, 1L, ctx)).RetCode);
        Assert.Equal(5m, host.GetItemDecimal(1L, "n1"));

        Assert.Equal(
            WireRetCode.Ok,
            (await h.Service.SetVariable(
                new SetVariableRequest
                {
                    SessionId = session,
                    DatawindowHandle = dw,
                    Name = LastMonth,
                    Value = new VarValue { LongValue = 1L },
                    Recalc = false,
                },
                ctx)).RetCode);

        Assert.Equal(WireRetCode.Ok, (await CalcRowAsync(h.Service, session, dw, 1L, ctx)).RetCode);

        // STILL 5 - the static binding cannot be reached by a later assignment.
        Assert.Equal(5m, host.GetItemDecimal(1L, "n1"));
    }

    [Fact]
    public async Task DynamicExpansion_ThroughTheServiceSurface_MovesToSix()
    {
        Harness h = NewHarness();
        (string session, IReadOnlyList<string> handles) = await OpenAsync(h.Service, "dwA");
        string dw = handles[0];
        await EnableAsync(h.Service, session, dw);
        C04CallContext ctx = new();

        _ = await h.Service.AddVariableExpression(
            new AddVariableExpressionRequest
            {
                SessionId = session,
                DatawindowHandle = dw,
                Name = ThisMonth,
                Exp = "5",
            },
            ctx);

        _ = await h.Service.AddVariable(
            new AddVariableRequest
            {
                SessionId = session,
                DatawindowHandle = dw,
                Name = LastMonth,
                Value = new VarValue { LongValue = 0L },
            },
            ctx);

        // The ONLY difference from the test above: a doubled sigil on the FIRST reference.
        _ = await h.Service.AddVariableExpression(
            new AddVariableExpressionRequest
            {
                SessionId = session,
                DatawindowHandle = dw,
                Name = "num1",
                Exp = "$$" + LastMonth + " + $" + ThisMonth,
            },
            ctx);

        GetVariableExpressionResponse stored = await h.Service.GetVariableExpression(
            new GetVariableExpressionRequest { SessionId = session, DatawindowHandle = dw, Name = "num1" },
            ctx);

        // PER REFERENCE: the dynamic name is RETAINED, the static one is substituted.
        Assert.Equal("$$" + LastMonth + " + (5)", stored.Exp);

        _ = await h.Service.AddExpression(
            new AddExpressionRequest
            {
                SessionId = session,
                DatawindowHandle = dw,
                ColumnName = "n1",
                Exp = "$$num1",
            },
            ctx);

        FakeDataWindowHost host = h.Hosts.Created[0];

        _ = await CalcRowAsync(h.Service, session, dw, 1L, ctx);
        Assert.Equal(5m, host.GetItemDecimal(1L, "n1"));

        _ = await h.Service.SetVariable(
            new SetVariableRequest
            {
                SessionId = session,
                DatawindowHandle = dw,
                Name = LastMonth,
                Value = new VarValue { LongValue = 1L },
                Recalc = false,
            },
            ctx);

        _ = await CalcRowAsync(h.Service, session, dw, 1L, ctx);

        // NOW 6.
        Assert.Equal(6m, host.GetItemDecimal(1L, "n1"));
    }

    [Fact]
    public async Task MixedMode_IsResolvedPerReference_AndThePayloadCarriesTheUnexpandedSource()
    {
        Harness h = NewHarness();
        (string session, IReadOnlyList<string> handles) = await OpenAsync(h.Service, "dwA");
        string dw = handles[0];
        await EnableAsync(h.Service, session, dw);
        C04CallContext ctx = new();

        _ = await h.Service.AddVariable(
            new AddVariableRequest
            {
                SessionId = session,
                DatawindowHandle = dw,
                Name = "stat",
                Value = new VarValue { LongValue = 10L },
            },
            ctx);

        _ = await h.Service.AddVariable(
            new AddVariableRequest
            {
                SessionId = session,
                DatawindowHandle = dw,
                Name = "dyn",
                Value = new VarValue { LongValue = 1L },
            },
            ctx);

        _ = await h.Service.AddExpression(
            new AddExpressionRequest
            {
                SessionId = session,
                DatawindowHandle = dw,
                ColumnName = "n1",
                Exp = "$stat + $$dyn",
            },
            ctx);

        FakeDataWindowHost host = h.Hosts.Created[0];
        _ = await CalcRowAsync(h.Service, session, dw, 1L, ctx);
        Assert.Equal(11m, host.GetItemDecimal(1L, "n1"));

        // Mutating the STATIC reference changes nothing.
        _ = await h.Service.SetVariable(
            new SetVariableRequest
            {
                SessionId = session,
                DatawindowHandle = dw,
                Name = "stat",
                Value = new VarValue { LongValue = 99L },
                Recalc = false,
            },
            ctx);

        _ = await CalcRowAsync(h.Service, session, dw, 1L, ctx);
        Assert.Equal(11m, host.GetItemDecimal(1L, "n1"));

        // Mutating the DYNAMIC reference does.
        _ = await h.Service.SetVariable(
            new SetVariableRequest
            {
                SessionId = session,
                DatawindowHandle = dw,
                Name = "dyn",
                Value = new VarValue { LongValue = 5L },
                Recalc = false,
            },
            ctx);

        _ = await CalcRowAsync(h.Service, session, dw, 1L, ctx);
        Assert.Equal(15m, host.GetItemDecimal(1L, "n1"));

        // THE THREE-COMPONENT PAYLOAD: the UNEXPANDED source travels, so a static binding is
        // distinguishable from a literal on the far side.
        GetExpressionStateResponse state = await h.Service.GetExpressionState(
            new GetExpressionStateRequest
            {
                SessionId = session,
                DatawindowHandle = dw,
                IncludeBindings = true,
            },
            ctx);

        Assert.Equal(WireRetCode.Ok, state.RetCode);
        Assert.NotEmpty(state.Bindings);
        Assert.Contains(state.Bindings, b => b.UnexpandedExp == "$stat + $$dyn");

        // The sentinels are carried verbatim.
        Assert.Equal(string.Empty, state.Sentinels.FuncVar);
        Assert.Equal("Invoke", state.Sentinels.FuncInvoke);
        Assert.Equal("_columnexp", state.Sentinels.DwoSuffix);

        // The live environment travels alongside, and every variable reference carries its own mode.
        Assert.NotEmpty(state.GlobalVars);
        ExpressionBinding binding = state.Bindings.First(b => b.UnexpandedExp == "$stat + $$dyn");
        Assert.All(binding.Vars, v => Assert.NotEqual(ExpansionMode.Unspecified, v.ExpansionMode));
    }

    // =================================================================================================
    //  2. THE VETO MAPPING - 1 becomes FAILED (-1), NEVER PREVENT (+1)
    //     [n_cst_dwsvc.sru:L89-L94]
    // =================================================================================================

    [Fact]
    public async Task SetEnabled_IsIdempotentAndTheVetoAlphabetIsNotThePreventAlphabet()
    {
        Harness h = NewHarness();
        (string session, IReadOnlyList<string> handles) = await OpenAsync(h.Service, "dwA");
        string dw = handles[0];
        C04CallContext ctx = new();

        SetEnabledResponse first = await h.Service.SetEnabled(
            new SetEnabledRequest { SessionId = session, DatawindowHandle = dw, Enabled = true }, ctx);

        Assert.Equal(WireRetCode.Ok, first.RetCode);
        Assert.True(first.Changed);

        // :L89 - `if #Enabled = bEnabled then return RetCode.OK` WITHOUT raising OnEnable.
        SetEnabledResponse again = await h.Service.SetEnabled(
            new SetEnabledRequest { SessionId = session, DatawindowHandle = dw, Enabled = true }, ctx);

        Assert.Equal(WireRetCode.Ok, again.RetCode);
        Assert.False(again.Changed);
        Assert.True(again.Enabled);

        // THE COUNTER-INTUITIVE PART, asserted as an invariant: a veto is FAILED, and FAILED is a
        // failure under IsSucceeded while PREVENT is a SUCCESS. Mapping a veto to PREVENT would flip it.
        Assert.Equal(-1L, RetCode.FAILED);
        Assert.Equal(1L, RetCode.PREVENT);
        Assert.True(Predicates.IsSucceeded(RetCode.PREVENT));
        Assert.False(Predicates.IsSucceeded(RetCode.FAILED));
        Assert.Equal(WireRetCode.Failed, (WireRetCode)(int)RetCode.FAILED);
        Assert.NotEqual(WireRetCode.Prevent, (WireRetCode)(int)RetCode.FAILED);
    }

    // =================================================================================================
    //  3. THE RESOLUTION LADDER - fail fast with a defined code, never a best-effort recovery
    // =================================================================================================

    [Fact]
    public async Task Resolution_AnswersADefinedCodeForEveryStructurallyImpossibleState()
    {
        Harness h = NewHarness();
        (string session, IReadOnlyList<string> handles) = await OpenAsync(h.Service, "dwA");
        C04CallContext ctx = new();

        // Blank session identifier.
        Assert.Equal(
            WireRetCode.EInvalidArgument,
            (await h.Service.SetEnabled(
                new SetEnabledRequest { SessionId = "", DatawindowHandle = handles[0], Enabled = true },
                ctx)).RetCode);

        // Unknown session.
        Assert.Equal(
            WireRetCode.ENotExists,
            (await h.Service.SetEnabled(
                new SetEnabledRequest
                {
                    SessionId = "no-such-session",
                    DatawindowHandle = handles[0],
                    Enabled = true,
                },
                ctx)).RetCode);

        // Known session, handle that is not co-resident in it.
        Assert.Equal(
            WireRetCode.EInvalidHandle,
            (await h.Service.SetEnabled(
                new SetEnabledRequest
                {
                    SessionId = session,
                    DatawindowHandle = session + "/999",
                    Enabled = true,
                },
                ctx)).RetCode);

        // A closed session stops resolving.
        CloseExpressionSessionResponse closed = await h.Service.CloseExpressionSession(
            new CloseExpressionSessionRequest { SessionId = session }, ctx);

        Assert.Equal(WireRetCode.Ok, closed.RetCode);
        Assert.True(closed.WasOpen);

        Assert.Equal(
            WireRetCode.ENotExists,
            (await h.Service.SetEnabled(
                new SetEnabledRequest
                {
                    SessionId = session,
                    DatawindowHandle = handles[0],
                    Enabled = true,
                },
                ctx)).RetCode);

        // Closing twice is not an error, but it is not a second open either.
        Assert.False(
            (await h.Service.CloseExpressionSession(
                new CloseExpressionSessionRequest { SessionId = session }, ctx)).WasOpen);
    }

    [Fact]
    public async Task OpenExpressionSession_RefusesAPartialOpenRatherThanLeavingAHole()
    {
        Harness h = NewHarness();
        OpenExpressionSessionRequest request = new();
        request.DatawindowHandles.AddRange(["dwA", "missing", "dwC"]);

        OpenExpressionSessionResponse response =
            await h.Service.OpenExpressionSession(request, new C04CallContext());

        Assert.Equal(WireRetCode.EObjectNotFound, response.RetCode);
        Assert.Empty(response.DatawindowHandles);
        Assert.Equal(0, h.Sessions.SweepExpired());
    }

    [Fact]
    public async Task ArgumentValidation_RefusesForceWithoutRecalcAndCalcWithBothTargetAndForce()
    {
        Harness h = NewHarness();
        (string session, IReadOnlyList<string> handles) = await OpenAsync(h.Service, "dwA");
        string dw = handles[0];
        await EnableAsync(h.Service, session, dw);
        C04CallContext ctx = new();

        _ = await h.Service.AddVariable(
            new AddVariableRequest
            {
                SessionId = session,
                DatawindowHandle = dw,
                Name = "v",
                Value = new VarValue { LongValue = 1L },
            },
            ctx);

        // force without recalc has no legacy arity.
        Assert.Equal(
            WireRetCode.EInvalidArgument,
            (await h.Service.SetVariable(
                new SetVariableRequest
                {
                    SessionId = session,
                    DatawindowHandle = dw,
                    Name = "v",
                    Value = new VarValue { LongValue = 2L },
                    Force = true,
                },
                ctx)).RetCode);

        // An unset value oneof is not a typed null.
        Assert.Equal(
            WireRetCode.EInvalidArgument,
            (await h.Service.SetVariable(
                new SetVariableRequest
                {
                    SessionId = session,
                    DatawindowHandle = dw,
                    Name = "v",
                    Value = new VarValue(),
                },
                ctx)).RetCode);

        // of_calc(row, index) and of_calc(row, force) are DIFFERENT arities; both at once is neither.
        Assert.Equal(
            WireRetCode.EInvalidArgument,
            (await h.Service.Calc(
                new CalcRequest
                {
                    SessionId = session,
                    DatawindowHandle = dw,
                    Row = 1L,
                    Target = new ColumnRef { ColumnName = "n1" },
                    Force = true,
                },
                ctx)).RetCode);
    }

    // =================================================================================================
    //  4. THE ONE HARD LIMIT - a cross-session foreign reference is BLOCKED, never approximated
    //     [:L80-L83] `foreignvardata.expsvc` is an in-process pointer
    // =================================================================================================

    [Fact]
    public async Task ForeignVariable_ResolvesWhenCoResidentAndIsBlockedAcrossSessions()
    {
        Harness h = NewHarness();
        C04CallContext ctx = new();

        // TWO DataWindows in ONE session: co-resident, so the reference resolves.
        (string session, IReadOnlyList<string> handles) = await OpenAsync(h.Service, "dwA", "dwB");
        Assert.Equal(2, handles.Count);
        await EnableAsync(h.Service, session, handles[0]);
        await EnableAsync(h.Service, session, handles[1]);

        // [:L2131-L2135] - of_addforeignvar resolves the name IN THE PEER'S OWN TABLE and answers
        // E_VAR_NOT_FOUND when it is undefined there. So an undefined name is NOT a blocked reference:
        // the two outcomes are distinct and both are preserved.
        Assert.Equal(
            WireRetCode.EVarNotFound,
            (await h.Service.AddForeignVariable(
                new AddForeignVariableRequest
                {
                    SessionId = session,
                    DatawindowHandle = handles[0],
                    Name = "undefinedInPeer",
                    ForeignDatawindowHandle = handles[1],
                },
                ctx)).RetCode);

        // Define it in the PEER, then the co-resident reference resolves.
        Assert.Equal(
            WireRetCode.Ok,
            (await h.Service.AddVariable(
                new AddVariableRequest
                {
                    SessionId = session,
                    DatawindowHandle = handles[1],
                    Name = "peer",
                    Value = new VarValue { LongValue = 4L },
                },
                ctx)).RetCode);

        AddForeignVariableResponse coResident = await h.Service.AddForeignVariable(
            new AddForeignVariableRequest
            {
                SessionId = session,
                DatawindowHandle = handles[0],
                Name = "peer",
                ForeignDatawindowHandle = handles[1],
            },
            ctx);

        Assert.Equal(WireRetCode.Ok, coResident.RetCode);

        // The spec at L29-L35 requires DYNAMIC expansion for a foreign variable, and it resolves.
        Assert.Equal(
            WireRetCode.Ok,
            (await h.Service.AddVariableExpression(
                new AddVariableExpressionRequest
                {
                    SessionId = session,
                    DatawindowHandle = handles[0],
                    Name = "local",
                    Exp = "$$peer",
                },
                ctx)).RetCode);

        _ = await h.Service.AddExpression(
            new AddExpressionRequest
            {
                SessionId = session,
                DatawindowHandle = handles[0],
                ColumnName = "n1",
                Exp = "$$local",
            },
            ctx);

        Assert.Equal(
            WireRetCode.Ok,
            (await CalcRowAsync(h.Service, session, handles[0], 1L, ctx)).RetCode);

        Assert.Equal(4m, h.Hosts.Created[0].GetItemDecimal(1L, "n1"));

        // A SECOND session's handle: BLOCKED with a defined error, and the category names why.
        (string other, IReadOnlyList<string> otherHandles) = await OpenAsync(h.Service, "dwZ");

        AddForeignVariableResponse blocked = await h.Service.AddForeignVariable(
            new AddForeignVariableRequest
            {
                SessionId = session,
                DatawindowHandle = handles[0],
                Name = "crossSession",
                ForeignDatawindowHandle = otherHandles[0],
            },
            ctx);

        Assert.NotEqual(WireRetCode.Ok, blocked.RetCode);
        Assert.NotNull(blocked.Error);
        Assert.Equal(ExpressionError.Types.Category.ForeignReferenceBlocked, blocked.Error.Category);
        Assert.NotEqual(0, other.Length);

        // An empty handle is an argument fault, not a blocked reference.
        Assert.Equal(
            WireRetCode.EInvalidArgument,
            (await h.Service.AddForeignVariable(
                new AddForeignVariableRequest
                {
                    SessionId = session,
                    DatawindowHandle = handles[0],
                    Name = "x",
                    ForeignDatawindowHandle = "",
                },
                ctx)).RetCode);

        // [:L2121-L2124] - a duplicate definition is FAILED with the hardcoded 错误 dialog, and the
        // structured error carries that text UN-LOCALIZED.
        AddForeignVariableResponse duplicate = await h.Service.AddForeignVariable(
            new AddForeignVariableRequest
            {
                SessionId = session,
                DatawindowHandle = handles[0],
                Name = "peer",
                ForeignDatawindowHandle = handles[1],
            },
            ctx);

        Assert.Equal(WireRetCode.Failed, duplicate.RetCode);
        Assert.NotNull(duplicate.Error);
        Assert.Equal("错误", duplicate.Error.Error.Title);
        Assert.False(duplicate.Error.Error.Localized);
        Assert.Equal(Severity.StopSign, duplicate.Error.Error.Severity);
    }

    // =================================================================================================
    //  5. THE INVERTED MACRO CHANNEL - strictly synchronous, ONE-BASED arguments
    //     [se_cst_dw.sru:L14], [:L2258-L2263] and [:L2287]
    // =================================================================================================

    [Fact]
    public async Task InvokeMethodChannel_DispatchesMacroDirect_WithOneBasedArgumentsPreserved()
    {
        Harness h = NewHarness();
        (string session, IReadOnlyList<string> handles) = await OpenAsync(h.Service, "dwA");
        string dw = handles[0];
        await EnableAsync(h.Service, session, dw);

        C04StreamReader<InvokeMethodResponse> answers = new();
        List<InvokeMethodRequest> asked = [];

        C04StreamWriter<InvokeMethodRequest> requests = new(request =>
        {
            asked.Add(request);

            // The documented handler shape: `case "FormatPrice" return Round(Double(args[1]), ...)`.
            // ARGS ARE ONE-BASED ON THE ORACLE SIDE; the wire array is zero-based, so args[1] is [0].
            answers.Push(
                new InvokeMethodResponse
                {
                    InvocationId = request.InvocationId,
                    Result = new MacroResult
                    {
                        Value = new AnyValue
                        {
                            DoubleValue = double.Parse(
                                request.Args[0], CultureInfo.InvariantCulture) * 2d,
                        },
                    },
                });

            return Task.CompletedTask;
        });

        using CancellationTokenSource channelLife =
            CancellationTokenSource.CreateLinkedTokenSource(Ct);

        Metadata headers = new()
        {
            { ColumnExpressionChannelHeaders.SessionId, session },
            { ColumnExpressionChannelHeaders.DataWindowHandle, dw },
        };

        Task channel = h.Service.InvokeMethodChannel(
            answers, requests, new C04CallContext(headers, channelLife.Token));

        await WaitUntilAsync(() => h.Macro.AttachedCount == 1);

        C04CallContext ctx = new();

        AddExpressionResponse added = await h.Service.AddExpression(
            new AddExpressionRequest
            {
                SessionId = session,
                DatawindowHandle = dw,
                ColumnName = "n1",
                Exp = "$FormatPrice(21)",
            },
            ctx);

        Assert.Equal(WireRetCode.Ok, added.RetCode);

        CalcResponse calc = await CalcRowAsync(h.Service, session, dw, 1L, ctx);
        Assert.Equal(WireRetCode.Ok, calc.RetCode);

        InvokeMethodRequest first = Assert.Single(asked);
        Assert.Equal("FormatPrice", first.Name);
        Assert.False(first.Builtin, "$Func(...) is a CLIENT macro, so funcdata.builtin is false.");
        Assert.Equal(ExpansionMode.MacroDirect, first.Form);
        Assert.Equal(["21"], first.Args.ToArray());
        Assert.Equal(session, first.SessionId);
        Assert.Equal(dw, first.DatawindowHandle);
        Assert.False(string.IsNullOrEmpty(first.InvocationId));

        FakeDataWindowHost host = h.Hosts.Created[0];
        Assert.Equal(42m, host.GetItemDecimal(1L, "n1"));

        answers.Complete();
        await channel;
        Assert.Equal(0, h.Macro.AttachedCount);
    }

    [Fact]
    public async Task InvokeMethodChannel_DispatchesMacroDynamic_TakingTheNameFromTheFirstArgument()
    {
        Harness h = NewHarness();
        (string session, IReadOnlyList<string> handles) = await OpenAsync(h.Service, "dwA");
        string dw = handles[0];
        await EnableAsync(h.Service, session, dw);

        C04StreamReader<InvokeMethodResponse> answers = new();
        List<InvokeMethodRequest> asked = [];

        C04StreamWriter<InvokeMethodRequest> requests = new(request =>
        {
            asked.Add(request);
            answers.Push(
                new InvokeMethodResponse
                {
                    InvocationId = request.InvocationId,
                    Result = new MacroResult { Value = new AnyValue { DoubleValue = 7d } },
                });

            return Task.CompletedTask;
        });

        Metadata headers = new()
        {
            { ColumnExpressionChannelHeaders.SessionId, session },
            { ColumnExpressionChannelHeaders.DataWindowHandle, dw },
        };

        Task channel = h.Service.InvokeMethodChannel(
            answers, requests, new C04CallContext(headers, Ct));

        await WaitUntilAsync(() => h.Macro.AttachedCount == 1);

        C04CallContext ctx = new();

        // $$Invoke('Name', args...) - the FUNCTION name arrives as the first argument [:L2258-L2263].
        Assert.Equal(
            WireRetCode.Ok,
            (await h.Service.AddExpression(
                new AddExpressionRequest
                {
                    SessionId = session,
                    DatawindowHandle = dw,
                    ColumnName = "n1",
                    Exp = "$$Invoke('Chosen', 3)",
                },
                ctx)).RetCode);

        _ = await CalcRowAsync(h.Service, session, dw, 1L, ctx);

        InvokeMethodRequest first = Assert.Single(asked);

        // The name is LIFTED out of position one and the remainder shifts down by one.
        Assert.Equal("Chosen", first.Name);
        Assert.Equal(ExpansionMode.MacroDynamic, first.Form);
        Assert.Equal(["3"], first.Args.ToArray());

        Assert.Equal(7m, h.Hosts.Created[0].GetItemDecimal(1L, "n1"));

        answers.Complete();
        await channel;
    }

    [Fact]
    public async Task InvokeMethodChannel_RequiresItsIdentityHeaders_AndRefusesASecondAttach()
    {
        Harness h = NewHarness();
        (string session, IReadOnlyList<string> handles) = await OpenAsync(h.Service, "dwA");
        string dw = handles[0];

        // No headers at all: the payload carries no session, so the channel cannot be identified.
        RpcException missing = await Assert.ThrowsAsync<RpcException>(() =>
            h.Service.InvokeMethodChannel(
                new C04StreamReader<InvokeMethodResponse>(),
                new C04StreamWriter<InvokeMethodRequest>(),
                new C04CallContext(cancellationToken: Ct)));

        Assert.Equal(StatusCode.InvalidArgument, missing.StatusCode);

        // Headers naming a session that does not exist.
        Metadata bogus = new()
        {
            { ColumnExpressionChannelHeaders.SessionId, "no-such-session" },
            { ColumnExpressionChannelHeaders.DataWindowHandle, dw },
        };

        RpcException unknown = await Assert.ThrowsAsync<RpcException>(() =>
            h.Service.InvokeMethodChannel(
                new C04StreamReader<InvokeMethodResponse>(),
                new C04StreamWriter<InvokeMethodRequest>(),
                new C04CallContext(bogus, Ct)));

        Assert.NotEqual(StatusCode.OK, unknown.StatusCode);

        // Two clients on one DataWindow would break the single-slot synchronous discipline.
        Metadata headers = new()
        {
            { ColumnExpressionChannelHeaders.SessionId, session },
            { ColumnExpressionChannelHeaders.DataWindowHandle, dw },
        };

        C04StreamReader<InvokeMethodResponse> firstReader = new();
        Task first = h.Service.InvokeMethodChannel(
            firstReader, new C04StreamWriter<InvokeMethodRequest>(), new C04CallContext(headers, Ct));

        await WaitUntilAsync(() => h.Macro.AttachedCount == 1);

        RpcException duplicate = await Assert.ThrowsAsync<RpcException>(() =>
            h.Service.InvokeMethodChannel(
                new C04StreamReader<InvokeMethodResponse>(),
                new C04StreamWriter<InvokeMethodRequest>(),
                new C04CallContext(headers, Ct)));

        Assert.Equal(StatusCode.AlreadyExists, duplicate.StatusCode);

        firstReader.Complete();
        await first;
    }

    [Fact]
    public async Task MacroInvocation_WithNoAttachedClient_IsUnhandledExactlyAsTheOracleRefusedIt()
    {
        Harness h = NewHarness();
        (string session, IReadOnlyList<string> handles) = await OpenAsync(h.Service, "dwA");
        string dw = handles[0];
        await EnableAsync(h.Service, session, dw);
        C04CallContext ctx = new();

        _ = await h.Service.AddExpression(
            new AddExpressionRequest
            {
                SessionId = session,
                DatawindowHandle = dw,
                ColumnName = "n1",
                Exp = "$Nobody(1)",
            },
            ctx);

        // [:L2282] / [:L2306] - the oracle's `case else` shows the dialog and returns "", which makes
        // _of_PreprocessExp yield "" and _of_CalcItem `return false` at [:L745]. THAT IS NOT A FAILURE
        // CODE: of_calc DISCARDS the boolean at [:L1171] and returns RetCode.OK unconditionally at
        // [:L1176]. So the faithful outcome is OK with NOTHING WRITTEN plus a raised error - and an
        // implementation that "helpfully" reported a failure here would be a behaviour change.
        CalcResponse calc = await CalcRowAsync(h.Service, session, dw, 1L, ctx);
        Assert.Equal(WireRetCode.Ok, calc.RetCode);

        // Nothing was written: the fixture's four rows start at 0 and stay there.
        Assert.Equal(0m, h.Hosts.Created[0].GetItemDecimal(1L, "n1"));

        // The refusal surfaced as a structured error carrying the HARDCODED CHINESE and the literal
        // title 错误, deliberately NOT routed through I18N - the asymmetry the oracle has and keeps.
        Assert.NotNull(calc.Error);
        Assert.Equal("错误", calc.Error.Error.Title);
        Assert.False(calc.Error.Error.Localized);
        Assert.Contains("函数", calc.Error.Error.Text, StringComparison.Ordinal);

        // [:L1158] - a DISABLED service refuses to calculate at all, with FAILED.
        Assert.Equal(
            WireRetCode.Ok,
            (await h.Service.SetEnabled(
                new SetEnabledRequest { SessionId = session, DatawindowHandle = dw, Enabled = false },
                ctx)).RetCode);

        Assert.Equal(
            WireRetCode.Failed,
            (await CalcRowAsync(h.Service, session, dw, 1L, ctx)).RetCode);
    }

    // =================================================================================================
    //  6. THE TRACE CHANNEL - sequencing token, '>'-delimited stack, "(null)" sentinel
    //     [:L752-L759]
    // =================================================================================================

    [Fact]
    public async Task TraceChannel_CarriesTheDelimitedStackAndTheNullSentinelVerbatim()
    {
        Harness h = NewHarness();
        (string session, IReadOnlyList<string> handles) = await OpenAsync(h.Service, "dwA");
        string dw = handles[0];
        await EnableAsync(h.Service, session, dw);
        C04CallContext ctx = new();

        SetTraceResponse traced = await h.Service.SetTrace(
            new SetTraceRequest { SessionId = session, DatawindowHandle = dw, Trace = true }, ctx);

        Assert.Equal(WireRetCode.Ok, traced.RetCode);
        Assert.True(traced.Trace);

        C04StreamReader<TraceChannelRequest> control = new();
        C04StreamWriter<TraceRecord> records = new();

        Task channel = h.Service.TraceChannel(control, records, new C04CallContext(cancellationToken: Ct));

        control.Push(
            new TraceChannelRequest { SessionId = session, DatawindowHandle = dw, Subscribe = true });

        await WaitUntilAsync(() => h.Trace.SubscriptionCount == 1);

        // LIMB ONE of the compound condition: emptyStringIsNull on a string column.
        h.Trace.Emit(NewTrace(session, dw, 1L, "s1", ["n2_columnexp", "n3_columnexp"], "(null)", 2));

        // LIMB TWO: a non-string column with an empty value.
        h.Trace.Emit(NewTrace(session, dw, 2L, "n1", [], "(null)", 0));

        // A non-empty value passes through untouched.
        h.Trace.Emit(NewTrace(session, dw, 3L, "n1", ["n1_columnexp"], "42.00", 1));

        await WaitUntilAsync(() => records.Written.Count == 3);

        IReadOnlyList<TraceRecord> seen = records.Written;

        // THE STACK: one '>' per frame, INCLUDING a trailing one, then the terminal appended after the
        // loop - so a two-frame stack is "a>b>terminal".
        Assert.Equal("n2_columnexp>n3_columnexp>s1_columnexp", seen[0].Stack);
        Assert.Equal(["n2_columnexp", "n3_columnexp"], seen[0].StackFrames.ToArray());
        Assert.Equal("(null)", seen[0].Value);
        Assert.Equal(2, seen[0].Depth);

        // An EMPTY stack yields just the terminal, with no delimiter at all.
        Assert.Equal("n1_columnexp", seen[1].Stack);
        Assert.Empty(seen[1].StackFrames);
        Assert.Equal("(null)", seen[1].Value);

        Assert.Equal("42.00", seen[2].Value);

        // The token is monotonic and declares the fire-and-forget discipline.
        Assert.All(seen, r => Assert.Equal(OrderingDiscipline.Sequenced, r.Token.Discipline));
        Assert.Equal([1L, 2L, 3L], seen.Select(r => r.Token.Sequence).ToArray());

        // The DwObjectRef is resolved from the engine's own column, not echoed from the record.
        Assert.Equal("s1", seen[0].Dwo.Name);
        Assert.Equal("n1", seen[1].Dwo.Name);

        // Unsubscribing stops the pump; a later record is dropped rather than delivered.
        control.Push(
            new TraceChannelRequest { SessionId = session, DatawindowHandle = dw, Subscribe = false });

        await WaitUntilAsync(() => h.Trace.SubscriptionCount == 0);

        h.Trace.Emit(NewTrace(session, dw, 4L, "n1", [], "ignored", 0));
        Assert.Equal(3, records.Written.Count);

        control.Complete();
        await channel;
    }

    [Fact]
    public async Task SetTrace_TogglesTheFlagAndTheFlagGatesEmission()
    {
        Harness h = NewHarness();
        (string session, IReadOnlyList<string> handles) = await OpenAsync(h.Service, "dwA");
        string dw = handles[0];
        C04CallContext ctx = new();

        Assert.False(
            (await h.Service.SetTrace(
                new SetTraceRequest { SessionId = session, DatawindowHandle = dw, Trace = false },
                ctx)).Trace);

        Assert.True(
            (await h.Service.SetTrace(
                new SetTraceRequest { SessionId = session, DatawindowHandle = dw, Trace = true },
                ctx)).Trace);

        // An unknown session cannot be traced.
        Assert.Equal(
            WireRetCode.ENotExists,
            (await h.Service.SetTrace(
                new SetTraceRequest { SessionId = "nope", DatawindowHandle = dw, Trace = true },
                ctx)).RetCode);
    }

    // =================================================================================================
    //  7. THE EVENT STREAM AND THE EID_ITEMCHANGE COUPLING [se_cst_dw.sru:L43, :L182]
    // =================================================================================================

    [Fact]
    public async Task EventStream_DeliversTheThreeServiceEvents_AndItemChangeIsGatedWhereTheOraclePutsIt()
    {
        Harness h = NewHarness();
        (string session, IReadOnlyList<string> handles) = await OpenAsync(h.Service, "dwA");
        string dw = handles[0];
        await EnableAsync(h.Service, session, dw);

        C04StreamWriter<EventStreamResponse> events = new();

        using CancellationTokenSource life = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        Task stream = h.Service.EventStream(
            new EventStreamRequest { SessionId = session, DatawindowHandle = dw },
            events,
            new C04CallContext(cancellationToken: life.Token));

        await WaitUntilAsync(() => h.Relay.SubscriptionCount == 1);

        FakeDataWindowHost host = h.Hosts.Created[0];
        ColumnExpressionEngine engine = ResolveEngine(h, session, dw);
        IDataWindowObject column = host.DwObject("n1");

        // ONE-BASED column identifier, via the documented conversion rather than a raw cast.
        long columnId = column.ColumnId() ?? 0L;
        Assert.True(columnId > 0L);

        await h.Relay.RaiseItemChangedAsync(engine, 1L, column, Ct);
        await h.Relay.RaiseDoItemChangedAsync(engine, 1L, "n1", columnId, true, Ct);
        await h.Relay.RaiseVarChangedAsync(engine, 1, true, Ct);

        await WaitUntilAsync(() => events.Written.Count >= 3);

        IReadOnlyList<EventStreamResponse> seen = events.Written;
        Assert.Contains(seen, e => e.Event?.ItemChanged is not null);
        Assert.Contains(seen, e => e.Event?.DoItemChanged is not null);
        Assert.Contains(seen, e => e.Event?.VarChanged is not null);

        // THE DIVERGENT SHAPE: ondoitemchanged(row, colname, colid, frominput) - NOT (row, dwo).
        ColumnExpEvent.Types.DoItemChanged doItem =
            seen.First(e => e.Event?.DoItemChanged is not null).Event.DoItemChanged;

        Assert.Equal("n1", doItem.ColName);
        Assert.Equal(columnId, doItem.ColId);
        Assert.True(doItem.FromInput);

        // THE COUPLING: EID_ITEMCHANGE suppresses the item-changed relay, exactly as the raw event
        // gate does at se_cst_dw.sru:L182 - and NOWHERE ELSE, so a direct Calc is unaffected.
        int before = events.Written.Count;
        host.DisableEvent(EventGate.EID_ITEMCHANGE);

        await h.Relay.RaiseItemChangedAsync(engine, 1L, column, Ct);
        Assert.Equal(before, events.Written.Count);

        C04CallContext ctx = new();
        _ = await h.Service.AddExpression(
            new AddExpressionRequest
            {
                SessionId = session,
                DatawindowHandle = dw,
                ColumnName = "n1",
                Exp = "2 + 3",
            },
            ctx);

        Assert.Equal(WireRetCode.Ok, (await CalcRowAsync(h.Service, session, dw, 1L, ctx)).RetCode);
        Assert.Equal(5m, host.GetItemDecimal(1L, "n1"));

        await life.CancelAsync();

        try
        {
            await stream;
        }
        catch (OperationCanceledException)
        {
            // Ending the stream is the whole of the correct response.
        }
    }

    // =================================================================================================
    //  8. THE SEVEN TYPED VARIABLE FAMILIES, IN THE LEGACY DECLARATION ORDER
    //     of_addvar has ONE arity per type (7 members) and of_setvar has THREE per type (21 members)
    // =================================================================================================

    [Fact]
    public async Task AddVariable_AcceptsAllSevenTypedFamilies_InTheOracleDeclarationOrder()
    {
        Harness h = NewHarness();
        (string session, IReadOnlyList<string> handles) = await OpenAsync(h.Service, "dwA");
        string dw = handles[0];
        await EnableAsync(h.Service, session, dw);
        C04CallContext ctx = new();

        // The order is the prototype block's own: time, string, long, double, datetime, date, boolean.
        // `double` and NOT `decimal` - the oracle declares of_addvar(string, double).
        (string Name, VarValue Value)[] families =
        [
            ("vTime", new VarValue { TimeValue = new WireTime { Value = "13:45:00" } }),
            ("vString", new VarValue { StringValue = "abc" }),
            ("vLong", new VarValue { LongValue = 7L }),
            ("vDouble", new VarValue { DoubleValue = 2.5d }),
            ("vDatetime", new VarValue { DatetimeValue = new WireDateTime { Value = "2020-06-13 13:45:00" } }),
            ("vDate", new VarValue { DateValue = new WireDate { Value = "2020-06-13" } }),
            ("vBool", new VarValue { BoolValue = true }),
        ];

        foreach ((string name, VarValue value) in families)
        {
            AddVariableResponse added = await h.Service.AddVariable(
                new AddVariableRequest
                {
                    SessionId = session,
                    DatawindowHandle = dw,
                    Name = name,
                    Value = value,
                },
                ctx);

            Assert.Equal(WireRetCode.Ok, added.RetCode);
        }

        // Every family landed in the live environment, and the wire projection names each one.
        GetExpressionStateResponse state = await h.Service.GetExpressionState(
            new GetExpressionStateRequest { SessionId = session, DatawindowHandle = dw }, ctx);

        Assert.Equal(WireRetCode.Ok, state.RetCode);

        foreach ((string name, VarValue _) in families)
        {
            Assert.Contains(state.GlobalVars, v => v.Name == name);

            // Every one is LOCAL, not FOREIGN - VAR_LOCAL = 0 is preserved as the default.
            Assert.Equal(
                GlobalVarData.Types.VarType.VarLocal,
                state.GlobalVars.First(v => v.Name == name).VarType);
        }

        // A duplicate definition is refused rather than silently replacing the first.
        Assert.NotEqual(
            WireRetCode.Ok,
            (await h.Service.AddVariable(
                new AddVariableRequest
                {
                    SessionId = session,
                    DatawindowHandle = dw,
                    Name = "vLong",
                    Value = new VarValue { LongValue = 8L },
                },
                ctx)).RetCode);
    }

    [Fact]
    public async Task SetVariable_AcceptsAllThreeAritiesForEveryTypedFamily()
    {
        Harness h = NewHarness();
        (string session, IReadOnlyList<string> handles) = await OpenAsync(h.Service, "dwA");
        string dw = handles[0];
        await EnableAsync(h.Service, session, dw);
        C04CallContext ctx = new();

        (string Name, VarValue First, VarValue Second)[] families =
        [
            ("vTime",
                new VarValue { TimeValue = new WireTime { Value = "01:02:03" } },
                new VarValue { TimeValue = new WireTime { Value = "04:05:06" } }),
            ("vString", new VarValue { StringValue = "a" }, new VarValue { StringValue = "b" }),
            ("vLong", new VarValue { LongValue = 1L }, new VarValue { LongValue = 2L }),
            ("vDouble", new VarValue { DoubleValue = 1.5d }, new VarValue { DoubleValue = 2.5d }),
            ("vDatetime",
                new VarValue { DatetimeValue = new WireDateTime { Value = "2020-01-01 00:00:00" } },
                new VarValue { DatetimeValue = new WireDateTime { Value = "2021-01-01 00:00:00" } }),
            ("vDate",
                new VarValue { DateValue = new WireDate { Value = "2020-01-01" } },
                new VarValue { DateValue = new WireDate { Value = "2021-01-01" } }),
            ("vBool", new VarValue { BoolValue = false }, new VarValue { BoolValue = true }),
        ];

        foreach ((string name, VarValue first, VarValue _) in families)
        {
            Assert.Equal(
                WireRetCode.Ok,
                (await h.Service.AddVariable(
                    new AddVariableRequest
                    {
                        SessionId = session,
                        DatawindowHandle = dw,
                        Name = name,
                        Value = first,
                    },
                    ctx)).RetCode);
        }

        foreach ((string name, VarValue first, VarValue second) in families)
        {
            // Arity 1: value only.
            Assert.Equal(
                WireRetCode.Ok,
                (await h.Service.SetVariable(
                    new SetVariableRequest
                    {
                        SessionId = session,
                        DatawindowHandle = dw,
                        Name = name,
                        Value = second,
                    },
                    ctx)).RetCode);

            // Arity 2: value plus recalc.
            Assert.Equal(
                WireRetCode.Ok,
                (await h.Service.SetVariable(
                    new SetVariableRequest
                    {
                        SessionId = session,
                        DatawindowHandle = dw,
                        Name = name,
                        Value = first,
                        Recalc = true,
                    },
                    ctx)).RetCode);

            // Arity 3: value plus recalc plus force.
            Assert.Equal(
                WireRetCode.Ok,
                (await h.Service.SetVariable(
                    new SetVariableRequest
                    {
                        SessionId = session,
                        DatawindowHandle = dw,
                        Name = name,
                        Value = second,
                        Recalc = true,
                        Force = true,
                    },
                    ctx)).RetCode);
        }

        // SET IS AN UPSERT, AND THAT IS THE ORACLE'S OWN BEHAVIOUR, NOT A CONVENIENCE. [:L1756-L1757]
        // reads `index = _of_FindVarIndex(name)` / `if index = 0 then return of_AddVarExp(name,exp)`, and
        // every of_setvar overload delegates straight to of_SetVarExp [:L1779]. So setting a name that
        // was never added DEFINES it and answers OK. An implementation that "helpfully" rejected the
        // unknown name would be correcting the oracle, which C-B forbids.
        Assert.Equal(
            WireRetCode.Ok,
            (await h.Service.SetVariable(
                new SetVariableRequest
                {
                    SessionId = session,
                    DatawindowHandle = dw,
                    Name = "neverAdded",
                    Value = new VarValue { LongValue = 1L },
                },
                ctx)).RetCode);

        // And it really is defined afterwards, not merely accepted.
        GetExpressionStateResponse upserted = await h.Service.GetExpressionState(
            new GetExpressionStateRequest { SessionId = session, DatawindowHandle = dw }, ctx);

        Assert.Contains(upserted.GlobalVars, v => v.Name == "neverAdded");
    }

    [Fact]
    public async Task SetVariable_RefusesAnUnparseableTemporalRatherThanCoercingItToNull()
    {
        Harness h = NewHarness();
        (string session, IReadOnlyList<string> handles) = await OpenAsync(h.Service, "dwA");
        string dw = handles[0];
        await EnableAsync(h.Service, session, dw);
        C04CallContext ctx = new();

        _ = await h.Service.AddVariable(
            new AddVariableRequest
            {
                SessionId = session,
                DatawindowHandle = dw,
                Name = "vTime",
                Value = new VarValue { TimeValue = new WireTime { Value = "00:00:00" } },
            },
            ctx);

        // NEVER COLLAPSE AN UNPARSEABLE VALUE TO NULL. Null is a distinct legacy state that the
        // tri-state predicates depend on, so a garbled temporal is an argument fault, not a null.
        VarValue[] garbled =
        [
            new VarValue { TimeValue = new WireTime { Value = "not-a-time" } },
            new VarValue { DateValue = new WireDate { Value = "13/06/2020" } },
            new VarValue { DatetimeValue = new WireDateTime { Value = "yesterday" } },
        ];

        foreach (VarValue value in garbled)
        {
            Assert.Equal(
                WireRetCode.EInvalidArgument,
                (await h.Service.SetVariable(
                    new SetVariableRequest
                    {
                        SessionId = session,
                        DatawindowHandle = dw,
                        Name = "vTime",
                        Value = value,
                    },
                    ctx)).RetCode);
        }

        // The same discipline on the add path.
        Assert.Equal(
            WireRetCode.EInvalidArgument,
            (await h.Service.AddVariable(
                new AddVariableRequest
                {
                    SessionId = session,
                    DatawindowHandle = dw,
                    Name = "vBad",
                    Value = new VarValue { DateValue = new WireDate { Value = "" } },
                },
                ctx)).RetCode);
    }

    // =================================================================================================
    //  9. THE EXPRESSION SURFACE - four SetExp arities, two GetExp, two Remove, RemoveAll
    // =================================================================================================

    [Fact]
    public async Task SetExpression_AcceptsAllFourArities_ByIndexAndByName()
    {
        Harness h = NewHarness();
        (string session, IReadOnlyList<string> handles) = await OpenAsync(h.Service, "dwA");
        string dw = handles[0];
        await EnableAsync(h.Service, session, dw);
        C04CallContext ctx = new();

        AddExpressionResponse added = await h.Service.AddExpression(
            new AddExpressionRequest
            {
                SessionId = session,
                DatawindowHandle = dw,
                ColumnName = "n1",
                Exp = "1 + 1",
            },
            ctx);

        Assert.Equal(WireRetCode.Ok, added.RetCode);
        int index = added.Index;

        // BY INDEX, no recalc flag.
        Assert.Equal(
            WireRetCode.Ok,
            (await h.Service.SetExpression(
                new SetExpressionRequest
                {
                    SessionId = session,
                    DatawindowHandle = dw,
                    Target = new ColumnRef { Index = index },
                    Exp = "2 + 2",
                },
                ctx)).RetCode);

        // BY INDEX, with recalc.
        Assert.Equal(
            WireRetCode.Ok,
            (await h.Service.SetExpression(
                new SetExpressionRequest
                {
                    SessionId = session,
                    DatawindowHandle = dw,
                    Target = new ColumnRef { Index = index },
                    Exp = "3 + 3",
                    Recalc = true,
                },
                ctx)).RetCode);

        // BY NAME, no recalc flag.
        Assert.Equal(
            WireRetCode.Ok,
            (await h.Service.SetExpression(
                new SetExpressionRequest
                {
                    SessionId = session,
                    DatawindowHandle = dw,
                    Target = new ColumnRef { ColumnName = "n1" },
                    Exp = "4 + 4",
                },
                ctx)).RetCode);

        // BY NAME, with recalc - which writes the value, so the effect is observable.
        Assert.Equal(
            WireRetCode.Ok,
            (await h.Service.SetExpression(
                new SetExpressionRequest
                {
                    SessionId = session,
                    DatawindowHandle = dw,
                    Target = new ColumnRef { ColumnName = "n1" },
                    Exp = "5 + 5",
                    Recalc = true,
                },
                ctx)).RetCode);

        GetExpressionResponse stored = await h.Service.GetExpression(
            new GetExpressionRequest
            {
                SessionId = session,
                DatawindowHandle = dw,
                Target = new ColumnRef { ColumnName = "n1" },
            },
            ctx);

        Assert.Equal(WireRetCode.Ok, stored.RetCode);
        Assert.Equal("5 + 5", stored.Exp);

        // BY INDEX too, and the binding travels with it.
        GetExpressionResponse byIndex = await h.Service.GetExpression(
            new GetExpressionRequest
            {
                SessionId = session,
                DatawindowHandle = dw,
                Target = new ColumnRef { Index = index },
            },
            ctx);

        Assert.Equal("5 + 5", byIndex.Exp);
        Assert.NotNull(byIndex.Binding);
        Assert.Equal("5 + 5", byIndex.Binding.UnexpandedExp);

        // An UNADDRESSED ColumnRef is neither an index nor a name, so it is an argument fault.
        Assert.Equal(
            WireRetCode.EInvalidArgument,
            (await h.Service.SetExpression(
                new SetExpressionRequest
                {
                    SessionId = session,
                    DatawindowHandle = dw,
                    Target = new ColumnRef(),
                    Exp = "0",
                },
                ctx)).RetCode);

        // A column with no expression on it does not resolve.
        Assert.NotEqual(
            WireRetCode.Ok,
            (await h.Service.GetExpression(
                new GetExpressionRequest
                {
                    SessionId = session,
                    DatawindowHandle = dw,
                    Target = new ColumnRef { ColumnName = "n3" },
                },
                ctx)).RetCode);
    }

    [Fact]
    public async Task RemoveExpression_WorksByIndexAndByName_AndRemoveAllClearsTheWholeSet()
    {
        Harness h = NewHarness();
        (string session, IReadOnlyList<string> handles) = await OpenAsync(h.Service, "dwA");
        string dw = handles[0];
        await EnableAsync(h.Service, session, dw);
        C04CallContext ctx = new();

        foreach (string column in new[] { "n1", "n2", "n3" })
        {
            Assert.True(
                (await h.Service.AddExpression(
                    new AddExpressionRequest
                    {
                        SessionId = session,
                        DatawindowHandle = dw,
                        ColumnName = column,
                        Exp = "1",
                    },
                    ctx)).Index > 0);
        }

        // BY NAME.
        RemoveExpressionResponse byName = await h.Service.RemoveExpression(
            new RemoveExpressionRequest
            {
                SessionId = session,
                DatawindowHandle = dw,
                Target = new ColumnRef { ColumnName = "n2" },
            },
            ctx);

        Assert.Equal(WireRetCode.Ok, byName.RetCode);

        // BY INDEX - one-based, and the surviving set has renumbered, so re-resolve by name first.
        GetExpressionStateResponse afterName = await h.Service.GetExpressionState(
            new GetExpressionStateRequest { SessionId = session, DatawindowHandle = dw }, ctx);

        Assert.Equal(2, afterName.Expressions.Count);
        Assert.DoesNotContain(afterName.Expressions, e => e.Name == "n2");

        Assert.Equal(
            WireRetCode.Ok,
            (await h.Service.RemoveExpression(
                new RemoveExpressionRequest
                {
                    SessionId = session,
                    DatawindowHandle = dw,
                    Target = new ColumnRef { Index = 1 },
                },
                ctx)).RetCode);

        RemoveAllExpressionsResponse cleared = await h.Service.RemoveAllExpressions(
            new RemoveAllExpressionsRequest { SessionId = session, DatawindowHandle = dw }, ctx);

        Assert.Equal(WireRetCode.Ok, cleared.RetCode);

        Assert.Empty(
            (await h.Service.GetExpressionState(
                new GetExpressionStateRequest { SessionId = session, DatawindowHandle = dw },
                ctx)).Expressions);

        // The resolution ladder applies to these RPCs too.
        Assert.Equal(
            WireRetCode.ENotExists,
            (await h.Service.RemoveAllExpressions(
                new RemoveAllExpressionsRequest { SessionId = "nope", DatawindowHandle = dw },
                ctx)).RetCode);

        Assert.Equal(
            WireRetCode.ENotExists,
            (await h.Service.RemoveExpression(
                new RemoveExpressionRequest
                {
                    SessionId = "nope",
                    DatawindowHandle = dw,
                    Target = new ColumnRef { ColumnName = "n1" },
                },
                ctx)).RetCode);
    }

    [Fact]
    public async Task SetVariableExpression_AcceptsAllThreeArities_AndGetReturnsTheStoredText()
    {
        Harness h = NewHarness();
        (string session, IReadOnlyList<string> handles) = await OpenAsync(h.Service, "dwA");
        string dw = handles[0];
        await EnableAsync(h.Service, session, dw);
        C04CallContext ctx = new();

        Assert.Equal(
            WireRetCode.Ok,
            (await h.Service.AddVariableExpression(
                new AddVariableExpressionRequest
                {
                    SessionId = session,
                    DatawindowHandle = dw,
                    Name = "v",
                    Exp = "1",
                },
                ctx)).RetCode);

        // Arity 1: name plus expression.
        Assert.Equal(
            WireRetCode.Ok,
            (await h.Service.SetVariableExpression(
                new SetVariableExpressionRequest
                {
                    SessionId = session,
                    DatawindowHandle = dw,
                    Name = "v",
                    Exp = "2",
                },
                ctx)).RetCode);

        // Arity 2: plus recalc.
        Assert.Equal(
            WireRetCode.Ok,
            (await h.Service.SetVariableExpression(
                new SetVariableExpressionRequest
                {
                    SessionId = session,
                    DatawindowHandle = dw,
                    Name = "v",
                    Exp = "3",
                    Recalc = true,
                },
                ctx)).RetCode);

        // Arity 3: plus force.
        Assert.Equal(
            WireRetCode.Ok,
            (await h.Service.SetVariableExpression(
                new SetVariableExpressionRequest
                {
                    SessionId = session,
                    DatawindowHandle = dw,
                    Name = "v",
                    Exp = "4",
                    Recalc = true,
                    Force = true,
                },
                ctx)).RetCode);

        GetVariableExpressionResponse stored = await h.Service.GetVariableExpression(
            new GetVariableExpressionRequest { SessionId = session, DatawindowHandle = dw, Name = "v" },
            ctx);

        Assert.Equal(WireRetCode.Ok, stored.RetCode);
        Assert.Equal("4", stored.Exp);

        // The recursive local structure travels: an expression variable's value IS an expression, with
        // its own variable and function reference arrays [:L73-L78].
        Assert.NotNull(stored.Local);
        Assert.Equal("4", stored.Local.Exp);

        // force without recalc has no legacy arity here either.
        Assert.Equal(
            WireRetCode.EInvalidArgument,
            (await h.Service.SetVariableExpression(
                new SetVariableExpressionRequest
                {
                    SessionId = session,
                    DatawindowHandle = dw,
                    Name = "v",
                    Exp = "5",
                    Force = true,
                },
                ctx)).RetCode);

        // An unknown name does not resolve.
        Assert.NotEqual(
            WireRetCode.Ok,
            (await h.Service.GetVariableExpression(
                new GetVariableExpressionRequest
                {
                    SessionId = session,
                    DatawindowHandle = dw,
                    Name = "absent",
                },
                ctx)).RetCode);
    }

    // =================================================================================================
    //  10. RELATIVE COLUMNS - singular AND plural, each by index AND by name, for both input kinds
    // =================================================================================================

    [Fact]
    public async Task SetRelativeColumns_CoversAllEightMembers_AndRefusesASingularBatch()
    {
        Harness h = NewHarness();
        (string session, IReadOnlyList<string> handles) = await OpenAsync(h.Service, "dwA");
        string dw = handles[0];
        await EnableAsync(h.Service, session, dw);
        C04CallContext ctx = new();

        int index = (await h.Service.AddExpression(
            new AddExpressionRequest
            {
                SessionId = session,
                DatawindowHandle = dw,
                ColumnName = "n1",
                Exp = "n2 + n3",
            },
            ctx)).Index;

        foreach (bool plural in new[] { false, true })
        {
            foreach (bool inputOnly in new[] { false, true })
            {
                foreach (ColumnRef target in new[]
                {
                    new ColumnRef { Index = index },
                    new ColumnRef { ColumnName = "n1" },
                })
                {
                    SetRelativeColumnsRequest request = new()
                    {
                        SessionId = session,
                        DatawindowHandle = dw,
                        Target = target,
                        Plural = plural,
                        InputOnly = inputOnly,
                    };

                    request.RelativeColumns.AddRange(plural ? ["n2", "n3"] : ["n2"]);

                    SetRelativeColumnsResponse response =
                        await h.Service.SetRelativeColumns(request, ctx);

                    Assert.Equal(WireRetCode.Ok, response.RetCode);
                }
            }
        }

        // The reverse dependency index is now observable on the wire [:L46-L47].
        GetExpressionStateResponse state = await h.Service.GetExpressionState(
            new GetExpressionStateRequest { SessionId = session, DatawindowHandle = dw }, ctx);

        ColumnExpData expression = Assert.Single(state.Expressions);
        Assert.NotEmpty(expression.RelativeColIds);
        Assert.NotEmpty(expression.RelativeInputColIds);

        // THE SINGULAR FORM TAKES EXACTLY ONE COLUMN. Two would silently drop one, so it is refused.
        SetRelativeColumnsRequest overloaded = new()
        {
            SessionId = session,
            DatawindowHandle = dw,
            Target = new ColumnRef { Index = index },
            Plural = false,
        };

        overloaded.RelativeColumns.AddRange(["n2", "n3"]);

        Assert.Equal(
            WireRetCode.EInvalidArgument,
            (await h.Service.SetRelativeColumns(overloaded, ctx)).RetCode);

        // And zero columns is not the singular form either.
        Assert.Equal(
            WireRetCode.EInvalidArgument,
            (await h.Service.SetRelativeColumns(
                new SetRelativeColumnsRequest
                {
                    SessionId = session,
                    DatawindowHandle = dw,
                    Target = new ColumnRef { Index = index },
                    Plural = false,
                },
                ctx)).RetCode);
    }

    // =================================================================================================
    //  11. THE FLAG SETTERS - AlwaysCalc, Recursive, TriggerEvent and Cacheable, by index and by name
    // =================================================================================================

    [Fact]
    public async Task SetExpressionFlag_CoversEveryFlagByIndexAndByName_AndRefusesAnUnspecifiedFlag()
    {
        Harness h = NewHarness();
        (string session, IReadOnlyList<string> handles) = await OpenAsync(h.Service, "dwA");
        string dw = handles[0];
        await EnableAsync(h.Service, session, dw);
        C04CallContext ctx = new();

        int index = (await h.Service.AddExpression(
            new AddExpressionRequest
            {
                SessionId = session,
                DatawindowHandle = dw,
                ColumnName = "n1",
                Exp = "1",
            },
            ctx)).Index;

        SetExpressionFlagRequest.Types.Flag[] flags =
        [
            SetExpressionFlagRequest.Types.Flag.AlwaysCalc,
            SetExpressionFlagRequest.Types.Flag.Recursive,
            SetExpressionFlagRequest.Types.Flag.TriggerEvent,
            SetExpressionFlagRequest.Types.Flag.Cacheable,
        ];

        foreach (SetExpressionFlagRequest.Types.Flag flag in flags)
        {
            foreach (ColumnRef target in new[]
            {
                new ColumnRef { Index = index },
                new ColumnRef { ColumnName = "n1" },
            })
            {
                Assert.Equal(
                    WireRetCode.Ok,
                    (await h.Service.SetExpressionFlag(
                        new SetExpressionFlagRequest
                        {
                            SessionId = session,
                            DatawindowHandle = dw,
                            Target = target,
                            Flag = flag,
                            Enabled = true,
                        },
                        ctx)).RetCode);
            }
        }

        GetExpressionStateResponse state = await h.Service.GetExpressionState(
            new GetExpressionStateRequest { SessionId = session, DatawindowHandle = dw }, ctx);

        ColumnExpData expression = Assert.Single(state.Expressions);
        Assert.True(expression.AlwaysCalc);
        Assert.True(expression.Recursive);
        Assert.True(expression.TriggerEvent);
        Assert.True(expression.Cacheable);

        // Turning each back off is the same call with the flag cleared.
        foreach (SetExpressionFlagRequest.Types.Flag flag in flags)
        {
            Assert.Equal(
                WireRetCode.Ok,
                (await h.Service.SetExpressionFlag(
                    new SetExpressionFlagRequest
                    {
                        SessionId = session,
                        DatawindowHandle = dw,
                        Target = new ColumnRef { Index = index },
                        Flag = flag,
                        Enabled = false,
                    },
                    ctx)).RetCode);
        }

        // FLAG_UNSPECIFIED names no legacy setter, so it is refused rather than defaulted.
        Assert.Equal(
            WireRetCode.EInvalidArgument,
            (await h.Service.SetExpressionFlag(
                new SetExpressionFlagRequest
                {
                    SessionId = session,
                    DatawindowHandle = dw,
                    Target = new ColumnRef { Index = index },
                    Flag = SetExpressionFlagRequest.Types.Flag.Unspecified,
                    Enabled = true,
                },
                ctx)).RetCode);
    }

    // =================================================================================================
    //  12. THE CALCULATION SURFACE - Calc x4, CalcAll x2, CalcEmpty x2 and _of_calcitem
    // =================================================================================================

    [Fact]
    public async Task Calc_CoversAllFourAritiesAndCalcAllAndCalcEmptyCoverBoth()
    {
        Harness h = NewHarness();
        (string session, IReadOnlyList<string> handles) = await OpenAsync(h.Service, "dwA");
        string dw = handles[0];
        await EnableAsync(h.Service, session, dw);
        C04CallContext ctx = new();

        int index = (await h.Service.AddExpression(
            new AddExpressionRequest
            {
                SessionId = session,
                DatawindowHandle = dw,
                ColumnName = "n1",
                Exp = "6 + 6",
            },
            ctx)).Index;

        FakeDataWindowHost host = h.Hosts.Created[0];

        // of_calc(row, index) - a SINGLE target, so the response carries its result.
        CalcResponse byIndex = await h.Service.Calc(
            new CalcRequest
            {
                SessionId = session,
                DatawindowHandle = dw,
                Row = 1L,
                Target = new ColumnRef { Index = index },
            },
            ctx);

        Assert.Equal(WireRetCode.Ok, byIndex.RetCode);
        CalcResult single = Assert.Single(byIndex.Results);
        Assert.Equal("n1", single.ColumnName);
        Assert.Equal(1L, single.Row);
        Assert.Equal(12m, host.GetItemDecimal(1L, "n1"));

        // of_calc(row, colname).
        Assert.Equal(
            WireRetCode.Ok,
            (await h.Service.Calc(
                new CalcRequest
                {
                    SessionId = session,
                    DatawindowHandle = dw,
                    Row = 2L,
                    Target = new ColumnRef { ColumnName = "n1" },
                },
                ctx)).RetCode);

        Assert.Equal(12m, host.GetItemDecimal(2L, "n1"));

        // of_calc(row, force) - a WHOLE-ROW walk, so Results is empty: the engine does not report
        // which expressions it chose to evaluate, and inventing a list would be a fabrication.
        CalcResponse forced = await h.Service.Calc(
            new CalcRequest
            {
                SessionId = session,
                DatawindowHandle = dw,
                Row = 3L,
                Force = true,
            },
            ctx);

        Assert.Equal(WireRetCode.Ok, forced.RetCode);
        Assert.Empty(forced.Results);
        Assert.Equal(12m, host.GetItemDecimal(3L, "n1"));

        // of_calc(row).
        Assert.Equal(
            WireRetCode.Ok,
            (await h.Service.Calc(
                new CalcRequest { SessionId = session, DatawindowHandle = dw, Row = 4L },
                ctx)).RetCode);

        // of_calcall() and of_calcall(force).
        Assert.Equal(
            WireRetCode.Ok,
            (await h.Service.CalcAll(
                new CalcAllRequest { SessionId = session, DatawindowHandle = dw }, ctx)).RetCode);

        Assert.Equal(
            WireRetCode.Ok,
            (await h.Service.CalcAll(
                new CalcAllRequest { SessionId = session, DatawindowHandle = dw, Force = true },
                ctx)).RetCode);

        // of_calcempty(row) and of_calcempty().
        Assert.Equal(
            WireRetCode.Ok,
            (await h.Service.CalcEmpty(
                new CalcEmptyRequest { SessionId = session, DatawindowHandle = dw, Row = 1L },
                ctx)).RetCode);

        Assert.Equal(
            WireRetCode.Ok,
            (await h.Service.CalcEmpty(
                new CalcEmptyRequest { SessionId = session, DatawindowHandle = dw }, ctx)).RetCode);

        // [:L1159] - a row outside the buffer is out of range, ONE-BASED at both ends.
        Assert.Equal(
            WireRetCode.EOutOfRange,
            (await h.Service.Calc(
                new CalcRequest { SessionId = session, DatawindowHandle = dw, Row = 0L },
                ctx)).RetCode);

        Assert.Equal(
            WireRetCode.EOutOfRange,
            (await h.Service.Calc(
                new CalcRequest { SessionId = session, DatawindowHandle = dw, Row = 99L },
                ctx)).RetCode);
    }

    [Fact]
    public async Task CalcItem_KeepsItsBooleanResultAndItsUnderscoredWireName()
    {
        Harness h = NewHarness();
        (string session, IReadOnlyList<string> handles) = await OpenAsync(h.Service, "dwA");
        string dw = handles[0];
        await EnableAsync(h.Service, session, dw);
        C04CallContext ctx = new();

        int index = (await h.Service.AddExpression(
            new AddExpressionRequest
            {
                SessionId = session,
                DatawindowHandle = dw,
                ColumnName = "n1",
                Exp = "8",
            },
            ctx)).Index;

        // `_of_calcitem` is declared PUBLIC despite the private-convention underscore prefix, and it
        // returns BOOLEAN rather than a return code. Both anomalies are carried, not tidied: the wire
        // method is CalcItem only because a protobuf identifier cannot begin with '_', and the legacy
        // spelling rides along as the contract's legacy_name option.
        CalcItemResponse applied = await h.Service.CalcItem(
            new CalcItemRequest
            {
                SessionId = session,
                DatawindowHandle = dw,
                Row = 1L,
                Index = index,
            },
            ctx);

        Assert.Equal(WireRetCode.Ok, applied.RetCode);
        Assert.True(applied.Succeeded);
        Assert.NotNull(applied.Result);
        Assert.Equal(8m, h.Hosts.Created[0].GetItemDecimal(1L, "n1"));

        // [:L773-L774] - an UNCHANGED value answers false. That is NOT a failure: it means nothing was
        // written, which is exactly why of_calc discards the boolean.
        CalcItemResponse unchanged = await h.Service.CalcItem(
            new CalcItemRequest
            {
                SessionId = session,
                DatawindowHandle = dw,
                Row = 1L,
                Index = index,
            },
            ctx);

        Assert.Equal(WireRetCode.Ok, unchanged.RetCode);
        Assert.False(unchanged.Succeeded);

        // A one-based index outside the set does not resolve.
        Assert.NotEqual(
            WireRetCode.Ok,
            (await h.Service.CalcItem(
                new CalcItemRequest
                {
                    SessionId = session,
                    DatawindowHandle = dw,
                    Row = 1L,
                    Index = 0,
                },
                ctx)).RetCode);
    }

    // =================================================================================================
    //  13. STATE PROJECTION - the seven structures, the sentinels and the filtered view
    // =================================================================================================

    [Fact]
    public async Task GetServiceState_AndGetExpressionState_ProjectTheLegacyStructuresFaithfully()
    {
        Harness h = NewHarness();
        (string session, IReadOnlyList<string> handles) = await OpenAsync(h.Service, "dwA");
        string dw = handles[0];
        C04CallContext ctx = new();

        // A PowerBuilder service is INERT until of_setenabled turns it on [n_cst_dwsvc.sru:L38].
        GetServiceStateResponse inert = await h.Service.GetServiceState(
            new GetServiceStateRequest { SessionId = session, DatawindowHandle = dw }, ctx);

        Assert.Equal(WireRetCode.Ok, inert.RetCode);
        Assert.False(inert.Enabled);
        Assert.False(inert.Trace);

        await EnableAsync(h.Service, session, dw);

        _ = await h.Service.SetTrace(
            new SetTraceRequest { SessionId = session, DatawindowHandle = dw, Trace = true }, ctx);

        GetServiceStateResponse live = await h.Service.GetServiceState(
            new GetServiceStateRequest { SessionId = session, DatawindowHandle = dw }, ctx);

        Assert.True(live.Enabled);
        Assert.True(live.Trace);

        _ = await h.Service.AddExpression(
            new AddExpressionRequest
            {
                SessionId = session,
                DatawindowHandle = dw,
                ColumnName = "n1",
                Exp = "1",
            },
            ctx);

        _ = await h.Service.AddExpression(
            new AddExpressionRequest
            {
                SessionId = session,
                DatawindowHandle = dw,
                ColumnName = "s1",
                Exp = "'x'",
            },
            ctx);

        // Unfiltered, with no bindings requested.
        GetExpressionStateResponse all = await h.Service.GetExpressionState(
            new GetExpressionStateRequest { SessionId = session, DatawindowHandle = dw }, ctx);

        Assert.Equal(2, all.Expressions.Count);
        Assert.Empty(all.Bindings);

        // The column type is projected from the DataWindow's own declaration, not guessed.
        Assert.Equal(
            ColumnExpData.Types.ColType.Decimal,
            all.Expressions.First(e => e.Name == "n1").ColType);

        Assert.Equal(
            ColumnExpData.Types.ColType.String,
            all.Expressions.First(e => e.Name == "s1").ColType);

        // THE COMPUTE CACHE DEGRADES RATHER THAN PRETENDING. `columnexpdata.computename` names the
        // computed field that `_of_createcompute` [:L1921-L1954] adds through `#DataWindow.Modify`, and
        // this service deliberately injects NO syntax mutator so the HOST provides it when it can -
        // syntax mutation belongs to the presentational half Phase 1 does not ship (C-D). A host without
        // that seam therefore has no compute object and the name is EMPTY, which is the documented
        // fall-back to the uncached path and not a projection fault.
        Assert.Equal(string.Empty, all.Expressions.First(e => e.Name == "n1").ComputeName);

        // The preserved DWOSUFFIX sentinel still travels, so a consumer can build the name itself.
        Assert.Equal("_columnexp", all.Sentinels.DwoSuffix);

        // FILTERED by column name, with bindings.
        GetExpressionStateResponse filtered = await h.Service.GetExpressionState(
            new GetExpressionStateRequest
            {
                SessionId = session,
                DatawindowHandle = dw,
                ColumnName = "s1",
                IncludeBindings = true,
            },
            ctx);

        ColumnExpData only = Assert.Single(filtered.Expressions);
        Assert.Equal("s1", only.Name);
        Assert.Single(filtered.Bindings);

        // An unknown column filters to nothing rather than erroring.
        Assert.Empty(
            (await h.Service.GetExpressionState(
                new GetExpressionStateRequest
                {
                    SessionId = session,
                    DatawindowHandle = dw,
                    ColumnName = "nosuch",
                },
                ctx)).Expressions);

        // The resolution ladder covers the state RPCs too.
        Assert.Equal(
            WireRetCode.ENotExists,
            (await h.Service.GetServiceState(
                new GetServiceStateRequest { SessionId = "nope", DatawindowHandle = dw },
                ctx)).RetCode);

        Assert.Equal(
            WireRetCode.ENotExists,
            (await h.Service.GetExpressionState(
                new GetExpressionStateRequest { SessionId = "nope", DatawindowHandle = dw },
                ctx)).RetCode);
    }

    // -------------------------------------------------------------------------------------------------
    //  HELPERS
    // -------------------------------------------------------------------------------------------------

    private static Task<CalcResponse> CalcRowAsync(
        Svc service,
        string session,
        string handle,
        long row,
        ServerCallContext context) =>
        service.Calc(
            new CalcRequest { SessionId = session, DatawindowHandle = handle, Row = row },
            context);

    /// <summary>Reaches the live engine behind a minted handle, to drive the relay directly.</summary>
    private static ColumnExpressionEngine ResolveEngine(Harness h, string session, string handle)
    {
        Assert.True(h.Sessions.TryGet(session, out ExpressionSession? scope));
        Assert.True(scope!.TryGetHost(DataWindowHandle.From(handle), out IExpressionServiceHost? host));
        return Assert.IsType<ColumnExpressionEngine>(host);
    }

    private static ExpressionTraceRecord NewTrace(
        string session,
        string handle,
        long row,
        string column,
        string[] frames,
        string value,
        int depth)
    {
        string stack = string.Concat(frames.Select(f => f + ">")) + column + "_columnexp";

        return new ExpressionTraceRecord
        {
            SequenceNumber = row,
            SessionId = session,
            Handle = DataWindowHandle.From(handle),
            Row = row,
            ColumnName = column,
            Stack = stack,
            StackFrames = [.. frames],
            Expression = "expr-" + row.ToString(CultureInfo.InvariantCulture),
            Value = value,
            Depth = depth,
            Timestamp = DateTimeOffset.UnixEpoch,
        };
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 500; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.Fail("The awaited condition never became true.");
    }
}

// =====================================================================================================
//  ExpressionWireProjectionTests - THE PURE CONVERSION LAYER
//  ---------------------------------------------------------------------------------------------------
//  WHY A SECOND CLASS AND WHY IT REACHES AN INTERNAL TYPE. `ExpressionWireProjection` is the seam where
//  every legacy structure, return code, severity, category and scalar becomes a wire message. It is
//  DELIBERATELY internal - a projection is not part of C-04's published surface, and making it public to
//  test it would widen the service's API for a test's convenience, which is the wrong trade (C-A). The
//  application project already declares
//  `<InternalsVisibleTo Include="PowerFramework.DataServices.Tests" />`, the same mechanism the
//  persistence service uses for its codecs, so the seam is reachable without widening anything.
//
//  These are TOTAL FUNCTIONS over closed domains: every oneof arm, every enumeration member, every
//  temporal format. A missed arm is a silently wrong value on the wire rather than a compilation error,
//  so exhaustiveness here is worth more than any amount of end-to-end driving.
// =====================================================================================================

public sealed class ExpressionWireProjectionTests
{
    // ---------------------------------------------------------------------------------------------
    //  THE RETURN-CODE ALGEBRA - carried by VALUE, so the tri-state hole survives the boundary
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(-1L)]
    [InlineData(-2L)]
    [InlineData(-3L)]
    [InlineData(-22L)]
    [InlineData(-2001L)]
    public void ToWireRetCode_CarriesTheNumericValueUnchanged(long code)
    {
        WireRetCode projected = ExpressionWireProjection.ToWireRetCode(code);

        Assert.Equal(code, (long)(int)projected);
    }

    [Fact]
    public void ToWireRetCode_KeepsPreventAndFailedDistinctOnTheWire()
    {
        // The whole point of projecting by VALUE: PREVENT (+1) reads as a SUCCESS under IsSucceeded
        // (>= 0) while FAILED (-1) does not, so collapsing them - or mapping a veto to PREVENT - would
        // invert the caller's decision [issucceeded.srf:L11-L13, isfailed.srf:L11-L13].
        Assert.Equal(WireRetCode.Prevent, ExpressionWireProjection.ToWireRetCode(RetCode.PREVENT));
        Assert.Equal(WireRetCode.Failed, ExpressionWireProjection.ToWireRetCode(RetCode.FAILED));
        Assert.NotEqual(
            ExpressionWireProjection.ToWireRetCode(RetCode.PREVENT),
            ExpressionWireProjection.ToWireRetCode(RetCode.FAILED));

        // CANCELLED is neither succeeded nor failed, and both legacy spellings share the value -2.
        Assert.Equal(WireRetCode.Cancelled, ExpressionWireProjection.ToWireRetCode(RetCode.CANCELLED));
        Assert.Equal(
            ExpressionWireProjection.ToWireRetCode(RetCode.CANCELLED),
            ExpressionWireProjection.ToWireRetCode(RetCode.CANCELED));
    }

    // ---------------------------------------------------------------------------------------------
    //  THE PRESERVED SENTINELS
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Sentinels_CarryTheirPreservedSpellingsVerbatim()
    {
        ExpressionSentinels sentinels = ExpressionWireProjection.Sentinels();

        // [:L120-L121] - an EMPTY function name models a bare variable and "Invoke" models dynamic
        // dispatch. Both spellings appear in serialized payloads and characterization recordings, so
        // they are carried exactly and never normalised.
        Assert.Equal(string.Empty, sentinels.FuncVar);
        Assert.Equal("Invoke", sentinels.FuncInvoke);
        Assert.Equal(MacroSentinels.FUNC_VAR, sentinels.FuncVar);
        Assert.Equal(MacroSentinels.FUNC_INVOKE, sentinels.FuncInvoke);

        // [:L95] and the engine's own macro tokens.
        Assert.Equal("_columnexp", sentinels.DwoSuffix);
        Assert.Equal("$", sentinels.MacroFlag);
        Assert.Equal("@", sentinels.MacroContext);
    }

    // ---------------------------------------------------------------------------------------------
    //  ANY-VALUE - EVERY oneof arm, in both directions
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void FromWire_AnyValue_CoversEveryOneofArm()
    {
        Assert.Null(ExpressionWireProjection.FromWire((AnyValue?)null));

        // An unset oneof and an explicit is-null are BOTH null - the distinction the wire draws is
        // "the caller said nothing" versus "the caller said null", and neither is a value.
        Assert.Null(ExpressionWireProjection.FromWire(new AnyValue()));
        Assert.Null(ExpressionWireProjection.FromWire(new AnyValue { IsNull = true }));

        Assert.Equal(true, ExpressionWireProjection.FromWire(new AnyValue { BoolValue = true }));
        Assert.Equal(42L, ExpressionWireProjection.FromWire(new AnyValue { Int64Value = 42L }));
        Assert.Equal(7UL, ExpressionWireProjection.FromWire(new AnyValue { Uint64Value = 7UL }));
        Assert.Equal(1.5d, ExpressionWireProjection.FromWire(new AnyValue { DoubleValue = 1.5d }));
        Assert.Equal("s", ExpressionWireProjection.FromWire(new AnyValue { StringValue = "s" }));

        Assert.Equal(
            2.25m,
            ExpressionWireProjection.FromWire(
                new AnyValue
                {
                    DecimalValue = new PowerFramework.Contracts.Common.V1.DecimalValue
                    {
                        Value = "2.25",
                    },
                }));

        Assert.Equal(
            new DateOnly(2020, 6, 13),
            ExpressionWireProjection.FromWire(
                new AnyValue { DateValue = new WireDate { Value = "2020-06-13" } }));

        Assert.Equal(
            new TimeOnly(13, 45, 0),
            ExpressionWireProjection.FromWire(
                new AnyValue { TimeValue = new WireTime { Value = "13:45:00" } }));

        Assert.Equal(
            new DateTime(2020, 6, 13, 13, 45, 0, DateTimeKind.Unspecified),
            ExpressionWireProjection.FromWire(
                new AnyValue
                {
                    DatetimeValue = new WireDateTime { Value = "2020-06-13 13:45:00" },
                }));

        byte[] blob = [1, 2, 3];
        Assert.Equal(
            blob,
            ExpressionWireProjection.FromWire(
                new AnyValue { BlobValue = ByteString.CopyFrom(blob) }));
    }

    [Fact]
    public void ToWire_Object_CoversEveryAcceptedClrShape()
    {
        Assert.True(ExpressionWireProjection.ToWire((object?)null).IsNull);
        Assert.True(ExpressionWireProjection.ToWire(true).BoolValue);
        Assert.Equal("s", ExpressionWireProjection.ToWire("s").StringValue);

        // The whole signed family collapses onto int64 and the unsigned family onto uint64, because the
        // oracle's twelve accepted return classes name the PowerScript spellings and .NET has more.
        foreach (object signed in new object[] { (short)1, 1, 1L })
        {
            Assert.Equal(1L, ExpressionWireProjection.ToWire(signed).Int64Value);
        }

        foreach (object unsigned in new object[] { (ushort)2, 2u, 2UL })
        {
            Assert.Equal(2UL, ExpressionWireProjection.ToWire(unsigned).Uint64Value);
        }

        Assert.Equal(1.5d, ExpressionWireProjection.ToWire(1.5f).DoubleValue, 3d);
        Assert.Equal(1.5d, ExpressionWireProjection.ToWire(1.5d).DoubleValue);

        // A decimal travels as its INVARIANT rendering, never as a double: the fixture's columns are
        // decimal(2) and a binary round trip would not be byte-exact.
        Assert.Equal("2.25", ExpressionWireProjection.ToWire(2.25m).DecimalValue.Value);

        Assert.Equal(
            "2020-06-13",
            ExpressionWireProjection.ToWire((object?)new DateOnly(2020, 6, 13)).DateValue.Value);

        Assert.Equal(
            "13:45:00",
            ExpressionWireProjection.ToWire((object?)new TimeOnly(13, 45, 0)).TimeValue.Value);

        // EMISSION IS ALWAYS CANONICAL ISO-8601 WITH 'T'. Parsing additionally accepts the
        // SPACE-separated form because that is what PowerScript's own String(datetime) produces, so a
        // client echoing an oracle recording is not refused for a difference the oracle does not make.
        // The asymmetry is deliberate and is asserted from both sides.
        Assert.Equal(
            "2020-06-13T13:45:00",
            ExpressionWireProjection.ToWire(
                (object?)new DateTime(2020, 6, 13, 13, 45, 0, DateTimeKind.Unspecified))
                .DatetimeValue.Value);

        Assert.Equal(
            [9, 8],
            ExpressionWireProjection.ToWire(new byte[] { 9, 8 }).BlobValue.ToByteArray());

        // ANYTHING ELSE travels as its own rendering rather than being dropped, so the far side refuses
        // it BY NAME exactly as the oracle's `case else` does [:L2282, :L2306] instead of seeing nothing.
        Guid unmatched = Guid.Empty;
        Assert.Equal(
            unmatched.ToString(),
            ExpressionWireProjection.ToWire(unmatched).StringValue);
    }

    [Fact]
    public void ToWire_Temporal_CarriesAFractionOnlyWhenItIsNonZero()
    {
        // A whole second renders WITHOUT a fractional part, so the common case is byte-identical to the
        // oracle's own String(time) rendering; a sub-second value keeps its precision rather than being
        // truncated into a value that would compare unequal on the way back.
        Assert.Equal("13:45:00", ExpressionWireProjection.ToWire(new TimeOnly(13, 45, 0)).Value);
        Assert.StartsWith(
            "13:45:00.",
            ExpressionWireProjection.ToWire(new TimeOnly(13, 45, 0, 250)).Value,
            StringComparison.Ordinal);

        Assert.Equal(
            "2020-06-13T13:45:00",
            ExpressionWireProjection.ToWire(
                new DateTime(2020, 6, 13, 13, 45, 0, DateTimeKind.Unspecified)).Value);

        Assert.StartsWith(
            "2020-06-13T13:45:00.",
            ExpressionWireProjection.ToWire(
                new DateTime(2020, 6, 13, 13, 45, 0, 250, DateTimeKind.Unspecified)).Value,
            StringComparison.Ordinal);

        Assert.Equal("2020-06-13", ExpressionWireProjection.ToWire(new DateOnly(2020, 6, 13)).Value);
    }

    [Fact]
    public void Parse_AcceptsOnlyTheDeclaredFormatsAndNeverGuesses()
    {
        Assert.Equal(new DateOnly(2020, 6, 13), ExpressionWireProjection.ParseDate("2020-06-13"));
        Assert.Equal(new TimeOnly(13, 45, 0), ExpressionWireProjection.ParseTime("13:45:00"));
        Assert.Equal(new TimeOnly(13, 45, 0), ExpressionWireProjection.ParseTime("13:45"));
        Assert.Equal(
            new TimeOnly(13, 45, 0, 250),
            ExpressionWireProjection.ParseTime("13:45:00.250000"));

        // BOTH separators parse: the canonical 'T' this layer emits, and the SPACE that PowerScript's
        // String(datetime) produces.
        Assert.Equal(
            new DateTime(2020, 6, 13, 13, 45, 0, DateTimeKind.Unspecified),
            ExpressionWireProjection.ParseDateTime("2020-06-13 13:45:00"));

        Assert.Equal(
            new DateTime(2020, 6, 13, 13, 45, 0, DateTimeKind.Unspecified),
            ExpressionWireProjection.ParseDateTime("2020-06-13T13:45:00"));

        // A DATE-ONLY string is a legal datetime, matching the oracle's own DateTime('yyyy-mm-dd').
        Assert.Equal(
            new DateTime(2020, 6, 13, 0, 0, 0, DateTimeKind.Unspecified),
            ExpressionWireProjection.ParseDateTime("2020-06-13"));

        Assert.Equal(2.25m, ExpressionWireProjection.ParseDecimal("2.25"));
        Assert.Equal(-2.25m, ExpressionWireProjection.ParseDecimal("-2.25"));

        // NULL AND BLANK ARE NULL, AND GARBAGE IS ALSO NULL RATHER THAN A GUESS. The caller turns the
        // null into E_INVALID_ARGUMENT; this layer never invents a value and never throws.
        foreach (string? empty in new[] { null, "", "   " })
        {
            Assert.Null(ExpressionWireProjection.ParseDate(empty));
            Assert.Null(ExpressionWireProjection.ParseTime(empty));
            Assert.Null(ExpressionWireProjection.ParseDateTime(empty));
            Assert.Null(ExpressionWireProjection.ParseDecimal(empty));
        }

        Assert.Null(ExpressionWireProjection.ParseDate("13/06/2020"));
        Assert.Null(ExpressionWireProjection.ParseTime("1:45 PM"));
        Assert.Null(ExpressionWireProjection.ParseDateTime("yesterday"));
        Assert.Null(ExpressionWireProjection.ParseDecimal("two"));
    }

    // ---------------------------------------------------------------------------------------------
    //  THE ENUMERATION MAPPERS - closed domains, so every member is asserted
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(ExpressionErrorSeverity.Unspecified, Severity.Unspecified)]
    [InlineData(ExpressionErrorSeverity.None, Severity.None)]
    [InlineData(ExpressionErrorSeverity.Information, Severity.Information)]
    [InlineData(ExpressionErrorSeverity.Question, Severity.Question)]
    [InlineData(ExpressionErrorSeverity.Exclamation, Severity.Exclamation)]
    [InlineData(ExpressionErrorSeverity.StopSign, Severity.StopSign)]
    public void ToWire_Severity_MapsEveryMember(ExpressionErrorSeverity source, Severity expected) =>
        Assert.Equal(expected, ExpressionWireProjection.ToWire(source));

    [Theory]
    [InlineData(ExpressionErrorCategory.Unspecified, ExpressionError.Types.Category.Unspecified)]
    [InlineData(ExpressionErrorCategory.CacheCreation, ExpressionError.Types.Category.CacheCreation)]
    [InlineData(ExpressionErrorCategory.CacheError, ExpressionError.Types.Category.CacheError)]
    [InlineData(ExpressionErrorCategory.CacheUpdate, ExpressionError.Types.Category.CacheUpdate)]
    [InlineData(ExpressionErrorCategory.Expression, ExpressionError.Types.Category.Expression)]
    [InlineData(ExpressionErrorCategory.Parse, ExpressionError.Types.Category.Parse)]
    [InlineData(ExpressionErrorCategory.Preprocess, ExpressionError.Types.Category.Preprocess)]
    [InlineData(ExpressionErrorCategory.UndefinedName, ExpressionError.Types.Category.UndefinedName)]
    [InlineData(ExpressionErrorCategory.InvalidValue, ExpressionError.Types.Category.InvalidValue)]
    [InlineData(
        ExpressionErrorCategory.MacroUnsupported,
        ExpressionError.Types.Category.MacroUnsupported)]
    [InlineData(
        ExpressionErrorCategory.DuplicateDefinition,
        ExpressionError.Types.Category.DuplicateDefinition)]
    [InlineData(
        ExpressionErrorCategory.ForeignReferenceBlocked,
        ExpressionError.Types.Category.ForeignReferenceBlocked)]
    public void ToWire_Category_MapsEveryMember(
        ExpressionErrorCategory source,
        ExpressionError.Types.Category expected) =>
        Assert.Equal(expected, ExpressionWireProjection.ToWire(source));

    [Theory]
    [InlineData(ColumnCalcFlag.CLC_UNKNOWN, ColumnData.Types.CalcFlag.ClcUnknown)]
    [InlineData(ColumnCalcFlag.CLC_YES, ColumnData.Types.CalcFlag.ClcYes)]
    [InlineData(ColumnCalcFlag.CLC_NO, ColumnData.Types.CalcFlag.ClcNo)]
    public void ToWire_CalcFlag_PreservesTheTriStateCache(
        ColumnCalcFlag source,
        ColumnData.Types.CalcFlag expected) =>
        Assert.Equal(expected, ExpressionWireProjection.ToWire(source));

    [Fact]
    public void ToWireColType_MapsEveryOracleColumnTypeAndDefaultsUnknown()
    {
        Assert.Equal(
            ColumnExpData.Types.ColType.Unknown,
            ExpressionWireProjection.ToWireColType(DataWindowServiceBase.COL_TYPE_UNKNOWN));

        Assert.Equal(
            ColumnExpData.Types.ColType.String,
            ExpressionWireProjection.ToWireColType(DataWindowServiceBase.COL_TYPE_STRING));

        Assert.Equal(
            ColumnExpData.Types.ColType.Integer,
            ExpressionWireProjection.ToWireColType(DataWindowServiceBase.COL_TYPE_INTEGER));

        Assert.Equal(
            ColumnExpData.Types.ColType.Decimal,
            ExpressionWireProjection.ToWireColType(DataWindowServiceBase.COL_TYPE_DECIMAL));

        Assert.Equal(
            ColumnExpData.Types.ColType.Datetime,
            ExpressionWireProjection.ToWireColType(DataWindowServiceBase.COL_TYPE_DATETIME));

        Assert.Equal(
            ColumnExpData.Types.ColType.Date,
            ExpressionWireProjection.ToWireColType(DataWindowServiceBase.COL_TYPE_DATE));

        Assert.Equal(
            ColumnExpData.Types.ColType.Time,
            ExpressionWireProjection.ToWireColType(DataWindowServiceBase.COL_TYPE_TIME));

        // An unrecognised code is UNKNOWN rather than an exception: the oracle stores a long and the
        // contract must be total over it.
        Assert.Equal(
            ColumnExpData.Types.ColType.Unknown,
            ExpressionWireProjection.ToWireColType(9999L));
    }

    // ---------------------------------------------------------------------------------------------
    //  THE OBJECT REFERENCE AND THE MACRO REQUEST
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ToWire_DwObjectRef_ToleratesANullObject()
    {
        // A null DataWindow object is not an error at the projection layer: an event can fire for the
        // DataWindow itself rather than a column, and the reference is then simply empty.
        DwObjectRef projected = ExpressionWireProjection.ToWire((IDataWindowObject?)null);

        Assert.NotNull(projected);
        Assert.Equal(string.Empty, projected.Name);
        Assert.Equal(0L, projected.Id);
    }

    [Fact]
    public void ToWire_MacroInvocation_StampsTheSessionAndFlattensOneBasedArgumentsInOrder()
    {
        FakeDataWindowHost host = FakeDataWindowFixtures.CreateColumnExpressionFixture();
        IDataWindowObject column = host.DwObject("n1");

        MacroInvocation invocation = new()
        {
            InvocationId = "inv-1",
            Sequence = 3L,
            Row = 2L,
            Dwo = column,
            Name = "FormatPrice",
            Arguments = MacroArgumentList.Empty with { Values = ["21", "2"] },
            Form = ExpansionMode.MacroDirect,
            Builtin = false,
            DataWindowHandle = "sess/1",
        };

        InvokeMethodRequest projected = ExpressionWireProjection.ToWire(invocation, "sess");

        Assert.Equal("sess", projected.SessionId);
        Assert.Equal("sess/1", projected.DatawindowHandle);
        Assert.Equal("inv-1", projected.InvocationId);
        Assert.Equal(2L, projected.Row);
        Assert.Equal("FormatPrice", projected.Name);
        Assert.Equal(ExpansionMode.MacroDirect, projected.Form);
        Assert.False(projected.Builtin);
        Assert.Equal("n1", projected.Dwo.Name);

        // THE BASIS CHANGES AND THE ORDER DOES NOT. args[1] on the oracle side is Args[0] here, so a
        // handler written as `Round(Double(args[1]), Long(args[2]))` reads Args[0] and Args[1].
        Assert.Equal(["21", "2"], projected.Args.ToArray());
    }

    [Fact]
    public void FromWire_MacroAnswer_TreatsARejectionAsNoValueRatherThanAsZero()
    {
        MacroInvocationResponse rejected = ExpressionWireProjection.FromWire(
            new InvokeMethodResponse
            {
                InvocationId = "inv-1",
                Result = new MacroResult { Rejected = true, Value = new AnyValue { Int64Value = 5L } },
            });

        // A REJECTION DISCARDS THE VALUE. Carrying the 5 through would let a refused answer feed the
        // calculation, which is precisely what the oracle's `case else` prevents by returning "".
        Assert.Equal("inv-1", rejected.InvocationId);
        Assert.Null(rejected.Value);
        Assert.False(rejected.Unhandled);

        MacroInvocationResponse unhandled = ExpressionWireProjection.FromWire(
            new InvokeMethodResponse { InvocationId = "inv-2", Unhandled = true });

        Assert.True(unhandled.Unhandled);
        Assert.Null(unhandled.Value);

        MacroInvocationResponse accepted = ExpressionWireProjection.FromWire(
            new InvokeMethodResponse
            {
                InvocationId = "inv-3",
                Result = new MacroResult { Value = new AnyValue { Int64Value = 5L } },
            });

        Assert.Equal(5L, accepted.Value);
    }

    [Fact]
    public void ToWire_ParseError_CarriesTheCaretAndTheUnlocalizedLegacyTitle()
    {
        Assert.Null(ExpressionWireProjection.ToWire((ExpressionParseError?)null));

        // The unclosed-quote site [:L1505], whose caret convention is the RAW quote position rather
        // than the integer-divided macro midpoint the other ten sites use. Both conventions exist and
        // both are preserved; ParseErrorFormatter owns the rendering, including the DBCS byte-width
        // padding, so this only has to prove the projection carries what it produced.
        ExpressionParseError error = ParseErrorFormatter.CreateParseError(
            ExpressionErrorSite.VariableMacroQuoteNotClosed,
            "1 + $$('abc",
            caretPosition: 5);

        ExpressionError? projected = ExpressionWireProjection.ToWire(error);

        Assert.NotNull(projected);
        Assert.Equal(ExpressionError.Types.Category.Parse, projected.Category);
        Assert.Equal("1 + $$('abc", projected.Expression);
        Assert.Equal(5L, projected.CaretPosition);

        // THE 28 PARSE MESSAGES ARE HARDCODED CHINESE UNDER THE LITERAL TITLE 错误 AND DO NOT ROUTE
        // THROUGH I18N - unlike the dwsvc, rowselect and contextmenu messages, which do. That asymmetry
        // is the oracle's and is reproduced rather than harmonised (C-B).
        Assert.Equal(ExpressionErrorCatalog.LegacyTitle, projected.Error.Title);
        Assert.Equal("错误", projected.Error.Title);
        Assert.False(projected.Error.Localized);
        Assert.Equal(Severity.StopSign, projected.Error.Severity);

        // The rendered marker travels too, so a consumer can show the caret without recomputing the
        // DBCS byte width the oracle pads with.
        Assert.False(string.IsNullOrEmpty(projected.RenderedMarker));
    }
}
