// =====================================================================================================
//  DataWindowServiceContractTests - THE C-03 TRANSPORT SUITE
//  ---------------------------------------------------------------------------------------------------
//  SUBJECT   PowerFramework.DataServices.Grpc.DataWindowService
//  ORACLE    ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru              (the 22-event surface)
//            ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc.sru            (864 L, zero PBNI)
//            ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_rowselect.sru
//            ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_contextmenu.sru
//            ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru             (the veto algebra)
//            ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru     (the update protocol)
//            ws_objects/pfw.shared.pbl.src/retcode.sru                             (the return algebra)
//
//  WHY THIS SUITE IS SEPARATE FROM ColumnExpressionServiceContractTests. That one characterises C-04,
//  which AAP section 0.4.3 versions APART from C-03 so the expansion engine can move independently of the
//  event chain; its acceptance evidence is therefore kept apart too. This suite characterises C-03: the
//  sixteen published operations, the per-capability-area ordering discipline, the four alphabets that
//  share the numerals 1 and 2, and the projections between the domain vocabulary and the wire.
//
//  WHAT IS ASSERTED HERE AND NOWHERE ELSE
//    1. All SIXTEEN contract methods exist and are overridden - the count the proto itself declares.
//    2. Both ordering patterns of AAP section 0.6.1.4, assigned PER CAPABILITY AREA rather than
//       globally, with an out-of-order arrival inside a synchronous group producing a HARD ERROR and
//       never a reorder.
//    3. `LegacyName` - not `LogicalName` - is the dispatch key, which is what keeps "0-itemchanged"
//       [se_cst_dw.sru:L54] ahead of "1-editchanged" [:L57]. LogicalName reverses them.
//    4. An updatewhereclause mismatch surfaces as Aborted with its ConflictDetail in the trailer the
//       contract declares, and is NEVER retried.
//    5. The four alphabets stay four types: the tri-valued veto, the broker's OnException codes, the
//       RetCode convention, and the {0,1,2,3} item-change alphabet.
//    6. All 22 event arms of the dispatch switch are reachable in the oracle's declaration order.
//    7. Three regressions this boundary is structurally prone to, each locked below: writes must not
//       carry a cancellation token, a separator is the MARKER LABEL and not an empty one, and a missing
//       object reference is a defined error rather than a leaked host exception.
//
//  DETERMINISM. Nothing here reads a clock, opens a socket or touches storage: Persistence is a scripted
//  double and the chain is a fake, so a recording taken from this suite is reproducible - the
//  Golden-Master technique's one hard prerequisite (AAP section 0.6.7).
// =====================================================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Microsoft.Extensions.Options;
using PowerFramework.Contracts.Common.V1;
using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.DataServices.Clients;
using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Grpc;
using PowerFramework.DataServices.Services;
using PowerFramework.Shared.Eventful;
using PowerFramework.Shared.Kernel;
using PowerFramework.Shared.Localization;
using Xunit;
using PowerFramework.DataServices.Expressions;
using DomainContextMenuModel = PowerFramework.DataServices.Services.ContextMenuModel;
using DomainEventGate = PowerFramework.DataServices.Domain.EventGate;
using DomainItemChangeResult = PowerFramework.DataServices.Domain.ItemChangeResult;
using EventId = PowerFramework.Contracts.DataServices.V1.EventId;
using BeginSessionRequest = PowerFramework.Contracts.Persistence.V1.BeginSessionRequest;
using TableUpdateContract = PowerFramework.Contracts.Persistence.V1.TableUpdateContract;
using PersistenceCarrierBufferSegment = PowerFramework.Contracts.Persistence.V1.CarrierBufferSegment;
using PersistenceCarrierState = PowerFramework.Contracts.Persistence.V1.CarrierState;
using PersistenceBeginSessionResponse = PowerFramework.Contracts.Persistence.V1.BeginSessionResponse;
using PersistenceCreateQueryTaskRequest =
    PowerFramework.Contracts.Persistence.V1.CreateQueryTaskRequest;
using PersistenceCreateQueryTaskResponse =
    PowerFramework.Contracts.Persistence.V1.CreateQueryTaskResponse;
using PersistenceCreateUpdateTaskResponse =
    PowerFramework.Contracts.Persistence.V1.CreateUpdateTaskResponse;
using PersistenceEndSessionResponse = PowerFramework.Contracts.Persistence.V1.EndSessionResponse;
using PersistenceOperationStatus = PowerFramework.Contracts.Persistence.V1.OperationStatus;
using PersistencePrepareUpdateRequest =
    PowerFramework.Contracts.Persistence.V1.PrepareUpdateRequest;
using PersistencePrepareUpdateResponse =
    PowerFramework.Contracts.Persistence.V1.PrepareUpdateResponse;
using PersistenceReleaseQueryTaskResponse =
    PowerFramework.Contracts.Persistence.V1.ReleaseQueryTaskResponse;
using PersistenceReleaseUpdateTaskResponse =
    PowerFramework.Contracts.Persistence.V1.ReleaseUpdateTaskResponse;
using PersistenceSessionHandle = PowerFramework.Contracts.Persistence.V1.SessionHandle;
using PersistenceTaskHandle = PowerFramework.Contracts.Persistence.V1.TaskHandle;
using PersistenceQueryDataChunk = PowerFramework.Contracts.Persistence.V1.QueryDataChunk;
using PersistenceQueryRequest = PowerFramework.Contracts.Persistence.V1.QueryRequest;
using PersistenceQueryResponse = PowerFramework.Contracts.Persistence.V1.QueryResponse;
using PersistenceQuerySpec = PowerFramework.Contracts.Persistence.V1.QuerySpec;
using PersistenceUpdateCounts = PowerFramework.Contracts.Persistence.V1.UpdateCounts;
using PersistenceUpdateRequest = PowerFramework.Contracts.Persistence.V1.UpdateRequest;
using PersistenceUpdateResponse = PowerFramework.Contracts.Persistence.V1.UpdateResponse;
using RetCode = PowerFramework.Shared.Kernel.RetCode;
using Svc = PowerFramework.DataServices.Grpc.DataWindowService;
using WireContextMenuModel = PowerFramework.Contracts.DataServices.V1.ContextMenuModel;
using WireEventGate = PowerFramework.Contracts.DataServices.V1.EventGate;
using WireItemChangeResult = PowerFramework.Contracts.DataServices.V1.ItemChangeResult;
using WireRetCode = PowerFramework.Contracts.Common.V1.RetCode.Types.Value;
using WireUpdateRequest = PowerFramework.Contracts.DataServices.V1.UpdateRequest;
using WireUpdateResponse = PowerFramework.Contracts.DataServices.V1.UpdateResponse;

namespace PowerFramework.DataServices.Tests;

// -----------------------------------------------------------------------------------------------------
//  DOUBLES
// -----------------------------------------------------------------------------------------------------

/// <summary>
/// A minimal <see cref="ServerCallContext"/> whose cancellation token is CANCELLABLE by default, because
/// a production one always is - and a non-cancellable one hides the fact that the ASP.NET Core stream
/// writer rejects the two-argument <c>WriteAsync</c> outright.
/// </summary>
internal sealed class C03CallContext(CancellationToken cancellationToken) : ServerCallContext
{
    private readonly CancellationToken _token = cancellationToken;

    protected override string MethodCore => "/dataservices.v1.DataWindowService/Adhoc";

    protected override string HostCore => "localhost:5102";

    protected override string PeerCore => "ipv4:127.0.0.1:0";

    protected override DateTime DeadlineCore => DateTime.MaxValue;

    protected override Metadata RequestHeadersCore { get; } = [];

    protected override CancellationToken CancellationTokenCore => _token;

    protected override Metadata ResponseTrailersCore { get; } = [];

    protected override Status StatusCore { get; set; }

    protected override WriteOptions? WriteOptionsCore { get; set; }

    protected override AuthContext AuthContextCore { get; } =
        new("c03", new Dictionary<string, List<AuthProperty>>(StringComparer.Ordinal));

    protected override ContextPropagationToken CreatePropagationTokenCore(
        ContextPropagationOptions? options) =>
        throw new NotSupportedException("Propagation is not exercised by this suite.");

    protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
}

/// <summary>
/// Records every message written on a stream. IT DELIBERATELY IMPLEMENTS ONLY THE SINGLE-ARGUMENT
/// <c>WriteAsync</c>, exactly as the production <c>HttpContextStreamWriter&lt;T&gt;</c> does, so that a
/// write which wrongly forwarded a cancellation token would fail here as it fails in production.
/// </summary>
internal sealed class C03StreamWriter<T> : IServerStreamWriter<T>
{
    private readonly List<T> _written = [];

    public WriteOptions? WriteOptions { get; set; }

    internal IReadOnlyList<T> Written
    {
        get
        {
            lock (_written)
            {
                return [.. _written];
            }
        }
    }

    /// <summary>
    /// Runs after each message is recorded, so a row can act at a point only the stream reaches.
    /// </summary>
    /// <remarks>
    /// THE ONLY SEAM THAT SITS BETWEEN THE ACQUISITION AND THE RELEASE. A row about what happens when a
    /// caller cancels MID-STREAM cannot cancel before the call, because a token that is already cancelled
    /// refuses the first upstream request and nothing is ever acquired - so there is nothing to release and
    /// the row proves the opposite of what it names. Cancelling from here happens after the session and the
    /// task exist and before the stream ends, which is the state the release obligation is about.
    /// </remarks>
    internal Action<T>? OnWrite { get; set; }

    public Task WriteAsync(T message)
    {
        lock (_written)
        {
            _written.Add(message);
        }

        OnWrite?.Invoke(message);

        return Task.CompletedTask;
    }
}

/// <summary>Replays a fixed script of inbound messages, then completes.</summary>
internal sealed class C03RequestStream(params EventChainRequest[] messages)
    : IAsyncStreamReader<EventChainRequest>
{
    private readonly EventChainRequest[] _messages = messages;
    private int _index = -1;

    public EventChainRequest Current => _messages[_index];

    public Task<bool> MoveNext(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(++_index < _messages.Length);
    }
}

/// <summary>A persistence client whose two consumed operations are scripted.</summary>
/// <remarks>
/// THE SIX HANDLE-LIFECYCLE OPERATIONS ARE DELIBERATELY NOT OVERRIDDEN. They travel the REAL client
/// implementation down to the generated-stub doubles below, so every test in this suite exercises the
/// production acquisition and release rather than a shortcut around them - and the stubs record what
/// reached the wire, which is where the assertions about the create call read from. Overriding
/// <c>OpenQueryScopeAsync</c> would have been shorter and would have verified nothing.
/// </remarks>
internal sealed class C03PersistenceClient : PersistenceClient
{
    internal C03PersistenceClient()
        : this(
            new FakeQueryServiceClient(),
            new FakeUpdateServiceClient(),
            new FakeTransactionServiceClient())
    {
    }

    private C03PersistenceClient(
        FakeQueryServiceClient queryStub,
        FakeUpdateServiceClient updateStub,
        FakeTransactionServiceClient transactionStub)
        : base(
            queryStub,
            updateStub,
            new FakeCommandServiceClient(),
            transactionStub,
            new PersistenceStubTokenProvider(),
            new PersistenceRecordingLogger())
    {
        QueryStub = queryStub;
        UpdateStub = updateStub;
        TransactionStub = transactionStub;
    }

    /// <summary>The C-05 stub, retained so what reached the wire can be asserted.</summary>
    internal FakeQueryServiceClient QueryStub { get; }

    /// <summary>The C-06 stub, retained on the same terms.</summary>
    internal FakeUpdateServiceClient UpdateStub { get; }

    /// <summary>The C-08 stub, retained so session begin and end can be paired.</summary>
    internal FakeTransactionServiceClient TransactionStub { get; }

    internal PersistenceQueryRequest? LastQuery { get; private set; }

    internal PersistenceUpdateRequest? LastUpdate { get; private set; }

    internal int UpdateCalls { get; private set; }

    internal PersistenceConflictException? Conflict { get; set; }

    internal List<PersistenceQueryResponse> QueryScript { get; } = [];

    internal PersistenceUpdateResponse UpdateScript { get; set; } = new()
    {
        Status = new PersistenceOperationStatus { RetCode = WireRetCode.Ok },
        Counts = new PersistenceUpdateCounts { Inserted = 3L },
        Identity = { new IdentityColumnData { IdentityColumnId = 1L } },
    };

    /// <summary>
    /// Every lifecycle and payload call this client received, in the order it received them.
    /// </summary>
    /// <remarks>
    /// ORDER IS THE ASSERTION, not merely presence. C-05 and C-06 are task-scoped and a task is
    /// session-scoped, so "the session was opened" and "the session was opened FIRST" are different claims
    /// and only the second one is the contract.
    /// </remarks>
    internal List<string> Lifecycle { get; } = [];

    /// <summary>The outcome <c>BeginSession</c> answers with.</summary>
    internal WireRetCode BeginSessionOutcome { get; set; } = WireRetCode.Ok;

    /// <summary>Whether <c>BeginSession</c> answers a success WITHOUT a handle.</summary>
    internal bool BeginSessionWithoutHandle { get; set; }

    /// <summary>The outcome task creation answers with, for both the query and the update contract.</summary>
    internal WireRetCode CreateTaskOutcome { get; set; } = WireRetCode.Ok;

    /// <summary>The outcome <c>PrepareUpdate</c> answers with.</summary>
    internal WireRetCode PrepareOutcome { get; set; } = WireRetCode.Ok;

    /// <summary>The specification the query task was created with.</summary>
    internal PersistenceCreateQueryTaskRequest? LastCreateQueryTask { get; private set; }

    /// <summary>The bind request the update task was prepared with.</summary>
    internal PersistencePrepareUpdateRequest? LastPrepareUpdate { get; private set; }

    /// <summary>The descriptor the session was opened from.</summary>
    internal PowerFramework.Contracts.Persistence.V1.BeginSessionRequest? LastBeginSession
    {
        get;
        private set;
    }

    // ==============================================================================================
    //  THE LIFECYCLE OVERRIDES - RECORD, THEN DELEGATE
    //  ---------------------------------------------------------------------------------------------
    //  ⚠ EVERY OVERRIDE BELOW CALLS `base`, AND THAT IS THE WHOLE DESIGN RATHER THAN A DETAIL ⚠
    //
    //  Two things have to be observable at once and they are observable in two different places, so
    //  neither may be substituted away.
    //
    //    1. THE ORDER OF THE CALLS ACROSS CONTRACTS. C-05 and C-06 are task-scoped and a task is
    //       session-scoped, so "the session was opened" and "the session was opened FIRST" are
    //       different claims and only the second is the contract. That ordering spans three separate
    //       wire stubs, each of which records only its own calls, so it can only be observed here -
    //       which is what `Lifecycle` is for.
    //    2. WHAT ACTUALLY REACHED THE WIRE. PersistenceClient is shipped code: it composes the request
    //       messages, attaches the credential, and chooses the cancellation token each release travels
    //       under. An override that answered from here instead of delegating would replace all of that
    //       with the test's own opinion of it, and every assertion against QueryStub, UpdateStub and
    //       TransactionStub would read an empty collection - not because the service failed to call,
    //       but because the call never got past this class.
    //
    //  So each member records what it was asked, then hands the request to the real client beneath it.
    //  The outcome knobs short-circuit ONLY when a test has set one to something other than success:
    //  that is how a refusal is scripted at a point the wire stubs cannot express, and leaving the
    //  default alone means the stub's own scripting - FakeUpdateServiceClient.CreateTaskCode, for
    //  instance - still decides. Both mechanisms therefore work, and neither hides the other.
    // ==============================================================================================

    public override async Task<PersistenceBeginSessionResponse> BeginSessionAsync(
        PowerFramework.Contracts.Persistence.V1.BeginSessionRequest request,
        CancellationToken cancellationToken)
    {
        Lifecycle.Add(nameof(BeginSessionAsync));
        LastBeginSession = request;

        if (BeginSessionOutcome != WireRetCode.Ok)
        {
            return new PersistenceBeginSessionResponse
            {
                Status = new PersistenceOperationStatus { RetCode = BeginSessionOutcome },
            };
        }

        PersistenceBeginSessionResponse response =
            await base.BeginSessionAsync(request, cancellationToken);

        // A SUCCESS WITHOUT A HANDLE IS A SHAPE THE WIRE STUB CANNOT PRODUCE, and it is the one the
        // service has to refuse rather than dereference: a producer that reports success and returns
        // nothing to address is a contract breach, not a caller error.
        if (BeginSessionWithoutHandle)
        {
            response.Session = null;
        }

        return response;
    }

    public override Task<PersistenceEndSessionResponse> EndSessionAsync(
        PowerFramework.Contracts.Persistence.V1.EndSessionRequest request,
        CancellationToken cancellationToken)
    {
        Lifecycle.Add(nameof(EndSessionAsync));

        // ASSERTS THE CLEANUP TOKEN IS NOT THE CALLER'S. A session holds a reference-counted pool entry
        // upstream, so a release that honoured the caller's cancellation would abandon the release on
        // exactly the path that creates the leak. Checked here rather than in a test body because it must
        // hold for EVERY test that ends a session, not only the one that remembers to look.
        Assert.False(cancellationToken.CanBeCanceled);

        return base.EndSessionAsync(request, cancellationToken);
    }

    public override Task<PersistenceCreateQueryTaskResponse> CreateQueryTaskAsync(
        PersistenceCreateQueryTaskRequest request,
        CancellationToken cancellationToken)
    {
        Lifecycle.Add(nameof(CreateQueryTaskAsync));
        LastCreateQueryTask = request;

        return CreateTaskOutcome == WireRetCode.Ok
            ? base.CreateQueryTaskAsync(request, cancellationToken)
            : Task.FromResult(new PersistenceCreateQueryTaskResponse
            {
                Status = new PersistenceOperationStatus { RetCode = CreateTaskOutcome },
            });
    }

    public override Task<PersistenceReleaseQueryTaskResponse> ReleaseQueryTaskAsync(
        PowerFramework.Contracts.Persistence.V1.ReleaseQueryTaskRequest request,
        CancellationToken cancellationToken)
    {
        Lifecycle.Add(nameof(ReleaseQueryTaskAsync));

        Assert.False(cancellationToken.CanBeCanceled);

        return base.ReleaseQueryTaskAsync(request, cancellationToken);
    }

    public override Task<PersistenceCreateUpdateTaskResponse> CreateUpdateTaskAsync(
        PowerFramework.Contracts.Persistence.V1.CreateUpdateTaskRequest request,
        CancellationToken cancellationToken)
    {
        Lifecycle.Add(nameof(CreateUpdateTaskAsync));

        return CreateTaskOutcome == WireRetCode.Ok
            ? base.CreateUpdateTaskAsync(request, cancellationToken)
            : Task.FromResult(new PersistenceCreateUpdateTaskResponse
            {
                Status = new PersistenceOperationStatus { RetCode = CreateTaskOutcome },
            });
    }

    public override Task<PersistencePrepareUpdateResponse> PrepareUpdateAsync(
        PersistencePrepareUpdateRequest request,
        CancellationToken cancellationToken)
    {
        Lifecycle.Add(nameof(PrepareUpdateAsync));
        LastPrepareUpdate = request;

        return PrepareOutcome == WireRetCode.Ok
            ? base.PrepareUpdateAsync(request, cancellationToken)
            : Task.FromResult(new PersistencePrepareUpdateResponse
            {
                Status = new PersistenceOperationStatus { RetCode = PrepareOutcome },
            });
    }

    public override Task<PersistenceReleaseUpdateTaskResponse> ReleaseUpdateTaskAsync(
        PowerFramework.Contracts.Persistence.V1.ReleaseUpdateTaskRequest request,
        CancellationToken cancellationToken)
    {
        Lifecycle.Add(nameof(ReleaseUpdateTaskAsync));

        Assert.False(cancellationToken.CanBeCanceled);

        return base.ReleaseUpdateTaskAsync(request, cancellationToken);
    }

    public override IAsyncEnumerable<PersistenceQueryResponse> QueryAsync(
        PersistenceQueryRequest request,
        CancellationToken cancellationToken)
    {
        Lifecycle.Add(nameof(QueryAsync));

        LastQuery = request;

        return Replay(QueryScript, cancellationToken);
    }

    public override Task<PersistenceUpdateResponse> UpdateAsync(
        PersistenceUpdateRequest request,
        CancellationToken cancellationToken)
    {
        Lifecycle.Add(nameof(UpdateAsync));

        UpdateCalls++;
        LastUpdate = request;

        return Conflict is not null
            ? throw Conflict
            : Task.FromResult(UpdateScript);
    }

    private static async IAsyncEnumerable<PersistenceQueryResponse> Replay(
        IEnumerable<PersistenceQueryResponse> script,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (PersistenceQueryResponse response in script)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await Task.Yield();

            yield return response;
        }
    }
}

/// <summary>Serves one retained model set for any handle except <c>"no-such-dw"</c>.</summary>
internal sealed class C03ModelSetProvider : IDataWindowModelSetProvider
{
    private readonly Dictionary<string, DataWindowModelSet> _sets = new(StringComparer.Ordinal);
    private readonly DataServicesOptions _options;

    internal C03ModelSetProvider(DataServicesOptions options) => _options = options;

    public DataWindowModelSet? GetOrCreate(string dataWindowHandle)
    {
        if (string.IsNullOrEmpty(dataWindowHandle)
            || string.Equals(dataWindowHandle, "no-such-dw", StringComparison.Ordinal))
        {
            return null;
        }

        if (_sets.TryGetValue(dataWindowHandle, out DataWindowModelSet? existing))
        {
            return existing;
        }

        FakeDataWindowHost host = new(new EventBroker());
        IOptions<DataServicesOptions> options = Options.Create(_options);
        I18n i18n = new();

        DomainContextMenuModel contextMenu = new(i18n, new DataWindowExpressionEvaluator(host), options);
        contextMenu.OnInit(host);

        RowSelectService rowSelect = new(i18n, options);
        rowSelect.OnInit(host);

        ColumnSortModel columnSort = new();
        columnSort.OnInit(host);

        DropDownSearchModel dropDownSearch = new(options);
        dropDownSearch.OnInit(host);

        DataWindowModelSet created = new(host, contextMenu, rowSelect, columnSort, dropDownSearch);
        _sets[dataWindowHandle] = created;

        return created;
    }
}

/// <summary>Serves nothing, so every headless operation takes its unknown-handle arm.</summary>
internal sealed class C03EmptyModelSetProvider : IDataWindowModelSetProvider
{
    public DataWindowModelSet? GetOrCreate(string dataWindowHandle) => null;
}

/// <summary>Binds a session to the chain double.</summary>
internal sealed class C03ChainFactory : IDataWindowEventChainFactory
{
    internal const string Column = "age";

    /// <summary>
    /// A handle the MODEL provider serves but this factory refuses.
    /// </summary>
    /// <remarks>
    /// IT HAS TO BE DISTINCT FROM THE MODEL PROVIDER'S OWN SENTINEL, because the two refusals belong to
    /// different arms and the service now resolves the handle when the SESSION is opened - so a handle no
    /// model resolves never reaches the chain factory at all. Sharing one sentinel made the chain arm
    /// unreachable the moment open-time validation existed.
    /// </remarks>
    internal const string UnboundHandle = "dw-no-chain";

    internal FakeEventChain? Created { get; private set; }

    public DataWindowEventChain? Create(
        ValidationSession session,
        string dataWindowHandle,
        IDataWindowEventObserver observer,
        IDataWindowSemanticResponder responder)
    {
        ArgumentNullException.ThrowIfNull(responder);

        if (string.Equals(dataWindowHandle, "no-such-dw", StringComparison.Ordinal)
            || string.Equals(dataWindowHandle, UnboundHandle, StringComparison.Ordinal))
        {
            return null;
        }

        FakeEventChain chain = new(session, new FakeAttachedServiceFactory(), observer);
        _ = chain.Host.AddColumn(Column, "long");
        Created = chain;

        return chain;
    }
}

/// <summary>The composed service under test.</summary>
internal sealed class C03Fixture
{
    internal C03Fixture(
        IDataWindowModelSetProvider? models = null,
        DataServicesOptions? configured = null,
        IDataWindowUpdateContractProvider? updateContracts = null)
    {
        Configured = configured ?? new DataServicesOptions();
        Registry = new ValidationSessionRegistry(Configured);
        Persistence = new C03PersistenceClient();
        Models = models ?? new C03ModelSetProvider(Configured);

        Chains = new C03ChainFactory();

        Service = new Svc(
            Options.Create(Configured),
            Registry,
            Chains,
            Models,
            Persistence,
            null,
            updateContracts);
    }

    internal DataServicesOptions Configured { get; }

    internal ValidationSessionRegistry Registry { get; }

    internal C03PersistenceClient Persistence { get; }

    internal IDataWindowModelSetProvider Models { get; }

    internal C03ChainFactory Chains { get; }

    internal Svc Service { get; }

    /// <summary>
    /// A CANCELLABLE token, because a production <see cref="ServerCallContext"/> always carries one and a
    /// non-cancellable one hides the fact that the ASP.NET Core stream writer rejects the two-argument
    /// WriteAsync outright.
    /// </summary>
    internal CancellationTokenSource Lifetime { get; } = new();

    internal ServerCallContext Context => _context ??= new C03CallContext(Lifetime.Token);

    private ServerCallContext? _context;
}

// =====================================================================================================
//  1. THE COERCION ALPHABET - se_cst_dw.sru:L231-L244
// =====================================================================================================
public sealed class DataWindowServiceColTypeTests
{
    [Theory]
    [InlineData("char", DwObjectRef.Types.ColTypePrefix.Char)]
    [InlineData("char(", DwObjectRef.Types.ColTypePrefix.CharParen)]
    [InlineData("decim", DwObjectRef.Types.ColTypePrefix.Decim)]
    [InlineData("real", DwObjectRef.Types.ColTypePrefix.Real)]
    [InlineData("numbe", DwObjectRef.Types.ColTypePrefix.Numbe)]
    [InlineData("long", DwObjectRef.Types.ColTypePrefix.Long)]
    [InlineData("ulong", DwObjectRef.Types.ColTypePrefix.Ulong)]
    [InlineData("datet", DwObjectRef.Types.ColTypePrefix.Datet)]
    [InlineData("date", DwObjectRef.Types.ColTypePrefix.Date)]
    [InlineData("time", DwObjectRef.Types.ColTypePrefix.Time)]
    public void EveryOracleArmClassifies(string colType, DwObjectRef.Types.ColTypePrefix expected) =>
        Assert.Equal(expected, DataWindowWireProjection.ClassifyColType(colType));

    [Theory]
    [InlineData("char(50)", DwObjectRef.Types.ColTypePrefix.CharParen)]
    [InlineData("decimal(2)", DwObjectRef.Types.ColTypePrefix.Decim)]
    [InlineData("number", DwObjectRef.Types.ColTypePrefix.Numbe)]
    [InlineData("datetime", DwObjectRef.Types.ColTypePrefix.Datet)]
    public void TheFiveCharacterTruncationIsWhatIsMatched(
        string colType,
        DwObjectRef.Types.ColTypePrefix expected) =>
        Assert.Equal(expected, DataWindowWireProjection.ClassifyColType(colType));

    [Fact]
    public void DatetimeDoesNotAliasOntoTheDateArm()
    {
        // THE PRESERVED TRAP: :L238 tests "datet" BEFORE :L240 tests "date". A prefix match would swallow
        // datetime into date and drop the time of day.
        Assert.Equal(
            DwObjectRef.Types.ColTypePrefix.Datet,
            DataWindowWireProjection.ClassifyColType("datetime"));

        Assert.NotEqual(
            DataWindowWireProjection.ClassifyColType("datetime"),
            DataWindowWireProjection.ClassifyColType("date"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("blob")]
    [InlineData("boolean")]
    [InlineData(null)]
    public void AnUnknownTypeIsASilentNoOpBecauseTheOracleHasNoCaseElse(string? colType) =>
        Assert.Equal(
            DwObjectRef.Types.ColTypePrefix.Unspecified,
            DataWindowWireProjection.ClassifyColType(colType));
}

// =====================================================================================================
//  2. THE FOUR ALPHABETS STAY DISTINCT
// =====================================================================================================
public sealed class DataWindowServiceAlphabetTests
{
    [Fact]
    public void TheVetoIsTriValuedAndPreventDeepIsNotPreventOnce()
    {
        Assert.Equal(Veto.Types.Result.Continue, DataWindowWireProjection.ToWireVeto(VetoResult.Continue));
        Assert.Equal(
            Veto.Types.Result.PreventOnce,
            DataWindowWireProjection.ToWireVeto(VetoResult.PreventOnce));
        Assert.Equal(
            Veto.Types.Result.PreventDeep,
            DataWindowWireProjection.ToWireVeto(VetoResult.PreventDeep));

        // n_cst_eventful.sru:L111-L112 - never flattened to a boolean.
        Assert.NotEqual(Veto.Types.Result.PreventOnce, Veto.Types.Result.PreventDeep);
    }

    [Fact]
    public void TheItemChangeAlphabetIsItsOwnTypeAndIsNotRetCode()
    {
        Assert.Equal(
            WireItemChangeResult.Default,
            DataWindowWireProjection.ToWireItemChange(DomainItemChangeResult.Default));
        Assert.Equal(
            WireItemChangeResult.TriggerValidationError,
            DataWindowWireProjection.ToWireItemChange(DomainItemChangeResult.TriggerValidationError));
        Assert.Equal(
            WireItemChangeResult.RestoreAndRejectText,
            DataWindowWireProjection.ToWireItemChange(DomainItemChangeResult.RestoreAndRejectText));
        Assert.Equal(
            WireItemChangeResult.KeepValueNoFocusMove,
            DataWindowWireProjection.ToWireItemChange(DomainItemChangeResult.KeepValueNoFocusMove));

        // The numeral 1 means "trigger the validation error" here and PREVENT in the return-code algebra.
        Assert.Equal(1, (int)WireItemChangeResult.TriggerValidationError);
        Assert.Equal(1L, RetCode.PREVENT);
        Assert.IsNotType<WireItemChangeResult>(DataWindowWireProjection.ToWireRetCode(RetCode.PREVENT));
    }

    [Fact]
    public void TheTriStateHoleIsPreserved()
    {
        // issucceeded.srf:L11-L13 is >= 0, so a PREVENT reads as a success. Never "fixed".
        Assert.True(Predicates.IsSucceeded(RetCode.PREVENT));

        // isfailed.srf:L11-L13 excludes CANCELLED explicitly, so cancelled is NEITHER.
        Assert.False(Predicates.IsFailed(RetCode.CANCELLED));
        Assert.False(Predicates.IsSucceeded(RetCode.CANCELLED));
    }

    [Fact]
    public void EveryPortedCodeSurvivesTheWireCast()
    {
        Assert.Equal(WireRetCode.Ok, DataWindowWireProjection.ToWireRetCode(RetCode.OK));
        Assert.Equal(
            WireRetCode.EInvalidArgument,
            DataWindowWireProjection.ToWireRetCode(RetCode.E_INVALID_ARGUMENT));
        Assert.Equal(
            WireRetCode.EInvalidHandle,
            DataWindowWireProjection.ToWireRetCode(RetCode.E_INVALID_HANDLE));
        Assert.Equal(WireRetCode.ENoSupport, DataWindowWireProjection.ToWireRetCode(RetCode.E_NO_SUPPORT));
        Assert.Equal(WireRetCode.Unknown, DataWindowWireProjection.ToWireRetCode(RetCode.UNKNOWN));
    }

    [Fact]
    public void NullTravelsAsTheExplicitNullArmAndNeverAsZero()
    {
        AnyValue nothing = DataWindowWireProjection.ToWireAny(null);

        Assert.Equal(AnyValue.KindOneofCase.IsNull, nothing.KindCase);
        Assert.NotEqual(AnyValue.KindOneofCase.Int64Value, nothing.KindCase);

        Assert.Equal(
            AnyValue.KindOneofCase.Int64Value,
            DataWindowWireProjection.ToWireAny(0L).KindCase);
        Assert.Equal(
            AnyValue.KindOneofCase.StringValue,
            DataWindowWireProjection.ToWireAny(string.Empty).KindCase);
        Assert.Equal(
            AnyValue.KindOneofCase.BoolValue,
            DataWindowWireProjection.ToWireAny(false).KindCase);
    }

    [Fact]
    public void EveryScalarArmOfTheVariantIsItsOwnArm()
    {
        // Eleven alternatives, and the unsigned and blob arms are distinct from their signed and string
        // neighbours - collapsing them would lose the type information the coercion behaviour depends on.
        Assert.Equal(
            AnyValue.KindOneofCase.Uint64Value,
            DataWindowWireProjection.ToWireAny((ushort)7).KindCase);
        Assert.Equal(
            AnyValue.KindOneofCase.Uint64Value,
            DataWindowWireProjection.ToWireAny(7u).KindCase);
        Assert.Equal(
            AnyValue.KindOneofCase.Uint64Value,
            DataWindowWireProjection.ToWireAny(7UL).KindCase);
        Assert.Equal(
            AnyValue.KindOneofCase.Int64Value,
            DataWindowWireProjection.ToWireAny((short)-7).KindCase);
        Assert.Equal(
            AnyValue.KindOneofCase.Int64Value,
            DataWindowWireProjection.ToWireAny(-7).KindCase);
        Assert.Equal(
            AnyValue.KindOneofCase.DoubleValue,
            DataWindowWireProjection.ToWireAny(1.5d).KindCase);
        Assert.Equal(
            AnyValue.KindOneofCase.DoubleValue,
            DataWindowWireProjection.ToWireAny(1.5f).KindCase);
        Assert.Equal(
            AnyValue.KindOneofCase.BlobValue,
            DataWindowWireProjection.ToWireAny(new byte[] { 1, 2, 3 }).KindCase);

        // decimal(n) is its OWN arm and is never flattened onto double: the fixture's salary column is
        // decimal(2) [ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14].
        Assert.Equal(
            AnyValue.KindOneofCase.DecimalValue,
            DataWindowWireProjection.ToWireAny(1.25m).KindCase);

        // Anything the oracle has no scalar for travels as its rendered text rather than being dropped.
        Assert.Equal(
            AnyValue.KindOneofCase.StringValue,
            DataWindowWireProjection.ToWireAny(new object()).KindCase);
    }

    [Fact]
    public void AnAbsentObjectReferenceProjectsToAnEmptyReferenceRatherThanNull()
    {
        DwObjectRef absent = DataWindowWireProjection.ToWireDwObject(null);

        Assert.Empty(absent.Name);
        Assert.Equal(0L, absent.Id);
        Assert.Equal(DwObjectRef.Types.ColTypePrefix.Unspecified, absent.ColTypePrefix);
    }

    /// <summary>
    /// The five PowerBuilder dialog icons each keep their own severity. The domain enum is internal, so the
    /// cases are given by value: <c>None!</c> = 1, <c>Information!</c> = 2, <c>Question!</c> = 3,
    /// <c>Exclamation!</c> = 4, <c>StopSign!</c> = 5 - the last being the one <c>se_cst_dw.sru:L357</c>
    /// passes.
    /// </summary>
    [Theory]
    [InlineData(1, Severity.None)]
    [InlineData(2, Severity.Information)]
    [InlineData(3, Severity.Question)]
    [InlineData(4, Severity.Exclamation)]
    [InlineData(5, Severity.StopSign)]
    public void EveryDialogIconMapsToItsOwnSeverity(int domain, Severity expected) =>
        Assert.Equal(expected, DataWindowWireProjection.ToWireSeverity((DialogSeverity)domain));

    /// <summary>
    /// The single row-selection dialog site [<c>n_cst_dwsvc_rowselect.sru:L239</c>] becomes a structured
    /// error. ONLY THE DELIVERY CHANNEL CHANGES: the text, the category, the substitution arguments and the
    /// severity all survive.
    /// </summary>
    [Fact]
    public void TheRowSelectDialogBecomesAStructuredErrorWithItsOperandsIntact()
    {
        Assert.Null(DataWindowWireProjection.ToWireError((RowSelectRejectionError?)null));

        StructuredError? projected = DataWindowWireProjection.ToWireError(new RowSelectRejectionError
        {
            Text = "rejected",
            LocalizationCategory = Categories.CAT_DWSVC,
            RowFormatTemplate = "row {0}",
            RejectionMessageKey = "key",
            Row = 3L,
            Icon = RowSelectMessageIcon.StopSign,
            Localized = true,
        });

        Assert.NotNull(projected);
        Assert.Equal("rejected", projected.Text);
        Assert.True(projected.Localized);
        Assert.Equal(Categories.CAT_DWSVC, projected.Category);
        Assert.Equal(Severity.StopSign, projected.Severity);
        Assert.Equal(["row {0}", "key", "3"], projected.FormatArgs);

        // A non-stop icon is not silently promoted.
        StructuredError? quiet = DataWindowWireProjection.ToWireError(new RowSelectRejectionError
        {
            Text = "t",
            LocalizationCategory = 0L,
            RowFormatTemplate = string.Empty,
            RejectionMessageKey = string.Empty,
            Row = 0L,
            Icon = RowSelectMessageIcon.None,
            Localized = false,
        });

        Assert.NotNull(quiet);
        Assert.Equal(Severity.None, quiet.Severity);
    }

    /// <summary>
    /// The ten context-menu dialog sites become structured errors, and the offending value is carried as a
    /// substitution argument rather than dropped - a consumer re-rendering in another locale needs it.
    /// </summary>
    [Fact]
    public void TheContextMenuDialogsBecomeStructuredErrorsCarryingTheirOffendingValue()
    {
        Assert.Null(DataWindowWireProjection.ToWireError((ContextMenuError?)null));

        StructuredError? withValue = DataWindowWireProjection.ToWireError(new ContextMenuError
        {
            Text = "bad value",
            LocalizationCategory = Categories.CAT_DWSVC,
            RowFormatTemplate = "row {0}",
            DetailMessageKey = "detail",
            Row = 2L,
            OffendingValue = "xyz",
            Kind = ContextMenuErrorKind.InvalidValue,
            Icon = ContextMenuMessageIcon.StopSign,
            Localized = true,
            SourceLocator = "n_cst_dwsvc_contextmenu.sru:L1009",
        });

        Assert.NotNull(withValue);
        Assert.Equal(Severity.StopSign, withValue.Severity);
        Assert.Equal(["row {0}", "detail", "2", "xyz"], withValue.FormatArgs);

        // An ABSENT offending value adds no argument - absence is not an empty operand.
        StructuredError? withoutValue = DataWindowWireProjection.ToWireError(new ContextMenuError
        {
            Text = "t",
            LocalizationCategory = 0L,
            RowFormatTemplate = string.Empty,
            DetailMessageKey = string.Empty,
            Row = 0L,
            Kind = ContextMenuErrorKind.None,
            Icon = ContextMenuMessageIcon.None,
            Localized = false,
            SourceLocator = "x",
        });

        Assert.NotNull(withoutValue);
        Assert.Equal(3, withoutValue.FormatArgs.Count);
        Assert.Equal(Severity.None, withoutValue.Severity);
    }

    [Fact]
    public void TheThreeTemporalTypesStayThreeTypes()
    {
        Assert.Equal(
            AnyValue.KindOneofCase.DateValue,
            DataWindowWireProjection.ToWireAny(new DateOnly(2022, 4, 14)).KindCase);
        Assert.Equal(
            AnyValue.KindOneofCase.TimeValue,
            DataWindowWireProjection.ToWireAny(new TimeOnly(1, 2, 3)).KindCase);
        Assert.Equal(
            AnyValue.KindOneofCase.DatetimeValue,
            DataWindowWireProjection.ToWireAny(new DateTime(2022, 4, 14, 1, 2, 3, DateTimeKind.Unspecified))
                .KindCase);

        Assert.Equal("2022-04-14", DataWindowWireProjection.ToWireDate(new DateOnly(2022, 4, 14)).Value);
        Assert.Equal("01:02:03", DataWindowWireProjection.ToWireTime(new TimeOnly(1, 2, 3)).Value);
        Assert.Equal(
            "2022-04-14T01:02:03",
            DataWindowWireProjection
                .ToWireDateTime(new DateTime(2022, 4, 14, 1, 2, 3, DateTimeKind.Unspecified))
                .Value);
    }
}

// =====================================================================================================
//  3. TOPIC IDENTITY - the decomposed triple, dispatched by LegacyName
// =====================================================================================================
public sealed class DataWindowServiceTopicTests
{
    [Fact]
    public void TheTwoPrefixedTopicsKeepTheirOrderingPrefix()
    {
        BrokerTopic itemChanged = Project("0-itemchanged");
        BrokerTopic editChanged = Project("1-editchanged");

        // se_cst_dw.sru:L54 and :L57 - the only two of the twelve that carry a prefix.
        Assert.Equal("0-itemchanged", itemChanged.LegacyName);
        Assert.Equal("1-editchanged", editChanged.LegacyName);
        Assert.True(itemChanged.HasSequencePrefix);
        Assert.True(editChanged.HasSequencePrefix);
        Assert.Equal(0, itemChanged.Sequence);
        Assert.Equal(1, editChanged.Sequence);

        // THE DISPATCH KEY. LegacyName keeps legacy order; LogicalName reverses it.
        Assert.True(
            string.CompareOrdinal(itemChanged.LegacyName, editChanged.LegacyName) < 0,
            "LegacyName must keep 0-itemchanged ahead of 1-editchanged.");
        Assert.True(
            string.CompareOrdinal(itemChanged.Name, editChanged.Name) > 0,
            "LogicalName reverses them, which is why it is never the sort key.");

        Assert.Equal("itemchanged", itemChanged.Name);
        Assert.Equal("editchanged", editChanged.Name);
    }

    [Theory]
    [InlineData("rowfocuschanging", BrokerTopic.Types.WellKnownName.EvtRowfocuschanging)]
    [InlineData("rowfocuschanged", BrokerTopic.Types.WellKnownName.EvtRowfocuschanged)]
    [InlineData("itemfocuschanged", BrokerTopic.Types.WellKnownName.EvtItemfocuschanged)]
    [InlineData("0-itemchanged", BrokerTopic.Types.WellKnownName.EvtItemchanged)]
    [InlineData("1-editchanged", BrokerTopic.Types.WellKnownName.EvtEditchanged)]
    [InlineData("clicked", BrokerTopic.Types.WellKnownName.EvtClicked)]
    [InlineData("doubleclicked", BrokerTopic.Types.WellKnownName.EvtDoubleclicked)]
    [InlineData("lbuttonup", BrokerTopic.Types.WellKnownName.EvtLbuttonup)]
    [InlineData("rbuttondown", BrokerTopic.Types.WellKnownName.EvtRbuttondown)]
    [InlineData("rbuttonup", BrokerTopic.Types.WellKnownName.EvtRbuttonup)]
    [InlineData("getfocus", BrokerTopic.Types.WellKnownName.EvtGetfocus)]
    [InlineData("losefocus", BrokerTopic.Types.WellKnownName.EvtLosefocus)]
    public void AllTwelveOracleTopicsClassify(string legacyName, BrokerTopic.Types.WellKnownName expected) =>
        Assert.Equal(expected, DataWindowWireProjection.ClassifyWellKnownTopic(legacyName));

    [Fact]
    public void AnApplicationTopicIsUnspecifiedAndNotAnError() =>
        Assert.Equal(
            BrokerTopic.Types.WellKnownName.EvtUnspecified,
            DataWindowWireProjection.ClassifyWellKnownTopic("app-topic"));

    [Fact]
    public void NoLifetimeSuffixIsSynthesizedForThisServicesTopics()
    {
        // se_cst_dw.sru:L102-L108 pass through WITHOUT appending a namespace; the .^persistent suffix
        // belongs to n_cst_threading.sru:L544,L596, which is Persistence's concern.
        BrokerTopic topic = Project("clicked");

        Assert.Equal(BrokerTopic.Types.Lifetime.Transient, topic.Lifetime);
        Assert.False(topic.HasNamespace);
    }

    [Fact]
    public void ANullTopicProjectsToNull() => Assert.Null(DataWindowWireProjection.ToWireTopic(null));

    private static BrokerTopic Project(string legacyName)
    {
        long parsed = SubscriptionTopic.ParseSubscription(legacyName, out SubscriptionTopic? topic);

        Assert.True(Predicates.IsSucceeded(parsed));

        BrokerTopic? projected = DataWindowWireProjection.ToWireTopic(topic);

        Assert.NotNull(projected);

        return projected;
    }
}

// =====================================================================================================
//  4. THE GATE - se_cst_dw.sru:L41-L43
// =====================================================================================================
public sealed class DataWindowServiceGateTests
{
    [Fact]
    public void TheThreeBitsAreEnumeratedInDeclarationOrder()
    {
        (WireEventGate gate, IReadOnlyList<WireEventGate.Types.Bit> bits) =
            DataWindowWireProjection.ToWireGate(
                DomainEventGate.EID_ROWFOCUSCHANGE
                | DomainEventGate.EID_ITEMFOCUSCHANGE
                | DomainEventGate.EID_ITEMCHANGE);

        Assert.Equal(7L, gate.Mask);
        Assert.Equal(
            [
                WireEventGate.Types.Bit.EidRowfocuschange,
                WireEventGate.Types.Bit.EidItemfocuschange,
                WireEventGate.Types.Bit.EidItemchange,
            ],
            bits);
    }

    [Fact]
    public void TheOracleValuesAreOneTwoAndFourOnBothSides()
    {
        Assert.Equal(1u, DomainEventGate.EID_ROWFOCUSCHANGE);
        Assert.Equal(2u, DomainEventGate.EID_ITEMFOCUSCHANGE);
        Assert.Equal(4u, DomainEventGate.EID_ITEMCHANGE);

        Assert.Equal(1, (int)WireEventGate.Types.Bit.EidRowfocuschange);
        Assert.Equal(2, (int)WireEventGate.Types.Bit.EidItemfocuschange);
        Assert.Equal(4, (int)WireEventGate.Types.Bit.EidItemchange);
    }

    [Fact]
    public void AnEmptyMaskReportsNoBits()
    {
        (WireEventGate gate, IReadOnlyList<WireEventGate.Types.Bit> bits) =
            DataWindowWireProjection.ToWireGate(0u);

        Assert.Equal(0L, gate.Mask);
        Assert.Empty(bits);
    }
}

// =====================================================================================================
//  5. THE INVERTED STREAM AND THE ORDERING DISCIPLINE
// =====================================================================================================
public sealed class DataWindowServiceConversationTests
{
    [Fact]
    public async Task AQuestionIsWrittenOutboundAndCompletedByItsAnswer()
    {
        C03StreamWriter<EventChainResponse> writer = new();
        using DataWindowEventConversation conversation = new("s-1", writer);
        conversation.Bind(new DataWindowEventSequencer());

        ValueTask<EventResult> pending = conversation.AskAsync(
            new EventNotification
            {
                CorrelationId = "q-1",
                EventId = EventId.Ondoitemchange,
                DoItemChange = new DoItemChangeEvent { Row = 1L, Data = "x" },
            },
            TestContext.Current.CancellationToken);

        // Written BEFORE the answer arrives: the question travels while the dispatch is still blocked.
        Assert.Single(writer.Written);
        Assert.Equal(EventChainResponse.PayloadOneofCase.Invoke, writer.Written[0].PayloadCase);
        Assert.Equal(OrderingDiscipline.Synchronous, writer.Written[0].Token.Discipline);

        Assert.True(conversation.Deliver(new EventResult { CorrelationId = "q-1", ReturnValue = 3L }));

        EventResult answer = await pending;

        Assert.Equal(3L, answer.ReturnValue);
    }

    [Fact]
    public async Task ASecondConcurrentQuestionIsRefused()
    {
        C03StreamWriter<EventChainResponse> writer = new();
        using DataWindowEventConversation conversation = new("s-1", writer);
        conversation.Bind(new DataWindowEventSequencer());

        ValueTask<EventResult> first = conversation.AskAsync(
            new EventNotification { CorrelationId = "q-1", EventId = EventId.Onddsgetfilter },
            TestContext.Current.CancellationToken);

        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await conversation.AskAsync(
                new EventNotification { CorrelationId = "q-2", EventId = EventId.Onddsgetfilter },
                TestContext.Current.CancellationToken));

        Assert.True(conversation.Deliver(new EventResult { CorrelationId = "q-1" }));

        _ = await first;
    }

    [Fact]
    public void AnUnmatchedAnswerIsReportedRatherThanSwallowed()
    {
        C03StreamWriter<EventChainResponse> writer = new();
        using DataWindowEventConversation conversation = new("s-1", writer);
        conversation.Bind(new DataWindowEventSequencer());

        Assert.False(conversation.Deliver(new EventResult { CorrelationId = "never-asked" }));
    }

    [Fact]
    public async Task DisposeCancelsAnOutstandingQuestionInsteadOfHanging()
    {
        C03StreamWriter<EventChainResponse> writer = new();
        DataWindowEventConversation conversation = new("s-1", writer);
        conversation.Bind(new DataWindowEventSequencer());

        ValueTask<EventResult> pending = conversation.AskAsync(
            new EventNotification { CorrelationId = "q-1", EventId = EventId.Onddsgetfilter },
            TestContext.Current.CancellationToken);

        conversation.Dispose();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
    }

    [Fact]
    public void AnOutOfOrderArrivalInASynchronousGroupIsAHardError()
    {
        C03StreamWriter<EventChainResponse> writer = new();
        using DataWindowEventConversation conversation = new("s-1", writer);
        conversation.Bind(new DataWindowEventSequencer());

        conversation.AcceptInbound(1L, EventId.Ondwnitemchange, strictOrdering: true);

        // 3 skips 2 inside the synchronous item-change group: semantically impossible, so it throws.
        _ = Assert.Throws<DataWindowEventSequenceException>(() =>
            conversation.AcceptInbound(3L, EventId.Ondwnitemchange, strictOrdering: true));
    }

    [Fact]
    public void RelaxedStrictnessRecordsTheViolationAndStillDoesNotReorder()
    {
        C03StreamWriter<EventChainResponse> writer = new();
        using DataWindowEventConversation conversation = new("s-1", writer);
        conversation.Bind(new DataWindowEventSequencer());

        conversation.AcceptInbound(1L, EventId.Ondwnitemchange, strictOrdering: true);

        // Same violation, strictness relaxed: no throw, and nothing is buffered or re-sorted.
        conversation.AcceptInbound(3L, EventId.Ondwnitemchange, strictOrdering: false);
    }

    /// <summary>
    /// A sequenced group admits a GAP and refuses a REVERSAL.
    /// </summary>
    /// <remarks>
    /// THIS ROW USED TO ASSERT THE DEFECT. It was called
    /// <c>ASequencedGroupToleratesAnArrivalBehindTheHighWaterMark</c> and it required exactly the
    /// behaviour that made the sequencing token decorative: an arrival behind the mark had to NOT throw,
    /// justified as "reorder authority is the consumer's". The consumer on this boundary is this server,
    /// and it was not reordering - it was dispatching in arrival order - so tolerating the reversal meant
    /// delivering the chain out of order and calling it authority. What is admitted is a GAP, because both
    /// directions draw from one counter and a client's token skips the numbers the server used.
    /// </remarks>
    [Fact]
    public void ASequencedGroupAdmitsAGapAndRefusesAReversal()
    {
        C03StreamWriter<EventChainResponse> writer = new();
        using DataWindowEventConversation conversation = new("s-1", writer);
        conversation.Bind(new DataWindowEventSequencer());

        // A GAP: 5 rather than 1, which is what a client sends once the server has consumed tokens of its
        // own. Admitted.
        conversation.AcceptInbound(5L, EventId.Ondwnsetfocus, strictOrdering: true);

        // A REVERSAL: 3 is behind the mark, so its position has already been dispatched past. Refused.
        DataWindowEventSequenceException reversed =
            Assert.Throws<DataWindowEventSequenceException>(() =>
                conversation.AcceptInbound(3L, EventId.Ondwnsetfocus, strictOrdering: true));

        Assert.Equal(OrderingDiscipline.Sequenced, reversed.Discipline);
        Assert.Equal(3L, reversed.ActualSequence);
        Assert.Equal(6L, reversed.ExpectedSequence);

        // A duplicate of the mark itself is the same case.
        _ = Assert.Throws<DataWindowEventSequenceException>(() =>
            conversation.AcceptInbound(5L, EventId.Ondwnsetfocus, strictOrdering: true));

        // AND RELAXED STRICTNESS RECORDS IT INSTEAD OF FAILING, without reordering anything - the dial
        // moves who reports the anomaly, never whether the server rearranges the chain.
        conversation.AcceptInbound(3L, EventId.Ondwnsetfocus, strictOrdering: false);
    }

    [Fact]
    public void ASequenceAtOrBelowZeroIsAlwaysRefused()
    {
        C03StreamWriter<EventChainResponse> writer = new();
        using DataWindowEventConversation conversation = new("s-1", writer);
        conversation.Bind(new DataWindowEventSequencer());

        _ = Assert.Throws<DataWindowEventSequenceException>(() =>
            conversation.AcceptInbound(0L, EventId.Ondwnsetfocus, strictOrdering: true));
    }

    [Fact]
    public void TheDisciplineAssignmentIsPerCapabilityArea()
    {
        // AAP 0.6.1.4 pattern (b) - strictly synchronous, no reordering permitted.
        Assert.Equal(
            OrderingDiscipline.Synchronous,
            DataWindowEventConversation.DisciplineOf(EventId.Ondwnitemchange));
        Assert.Equal(
            OrderingDiscipline.Synchronous,
            DataWindowEventConversation.DisciplineOf(EventId.Ondoitemchange));
        Assert.Equal(
            OrderingDiscipline.Synchronous,
            DataWindowEventConversation.DisciplineOf(EventId.Ondwnitemvalidationerror));
        Assert.Equal(
            OrderingDiscipline.Synchronous,
            DataWindowEventConversation.DisciplineOf(EventId.Oninitcontextmenu));
        Assert.Equal(
            OrderingDiscipline.Synchronous,
            DataWindowEventConversation.DisciplineOf(EventId.Oncontextmenu));
        Assert.Equal(
            OrderingDiscipline.Synchronous,
            DataWindowEventConversation.DisciplineOf(EventId.Onddsgetfilter));
        Assert.Equal(
            OrderingDiscipline.Synchronous,
            DataWindowEventConversation.DisciplineOf(EventId.Oncolumnexpinvokemethod));

        // Pattern (a) - a sequencing token, reorder authority with the consumer.
        Assert.Equal(
            OrderingDiscipline.Sequenced,
            DataWindowEventConversation.DisciplineOf(EventId.Ondwnsetfocus));
        Assert.Equal(
            OrderingDiscipline.Sequenced,
            DataWindowEventConversation.DisciplineOf(EventId.Ondwnrowchange));
        Assert.Equal(
            OrderingDiscipline.Sequenced,
            DataWindowEventConversation.DisciplineOf(EventId.Ondwnrbuttondown));
        Assert.Equal(
            OrderingDiscipline.Sequenced,
            DataWindowEventConversation.DisciplineOf(EventId.Oncolumnexptrace));
    }
}

// =====================================================================================================
//  6. THE SERVICE - all sixteen contract methods
// =====================================================================================================
public sealed class DataWindowServiceContractTests
{
    [Fact]
    public void TheContractDeclaresSixteenMethodsAndAllSixteenAreOverridden()
    {
        int declared = Contracts.DataServices.V1.DataWindowService.Descriptor.Methods.Count;

        Assert.Equal(16, declared);

        int overridden = typeof(Svc)
            .GetMethods(
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.DeclaredOnly)
            .Count(method => method.GetBaseDefinition().DeclaringType
                == typeof(Contracts.DataServices.V1.DataWindowService.DataWindowServiceBase));

        Assert.Equal(16, overridden);
    }

    [Fact]
    public async Task TheSessionLifecycleAndGateRoundTrip()
    {
        C03Fixture fixture = new();

        OpenValidationSessionResponse opened = await fixture.Service.OpenValidationSession(
            new OpenValidationSessionRequest { DatawindowHandle = "dw-1" },
            fixture.Context);

        Assert.Equal(WireRetCode.Ok, opened.RetCode);
        Assert.NotEmpty(opened.SessionId);
        Assert.NotNull(opened.State);

        DisableEventResponse disabled = await fixture.Service.DisableEvent(
            new DisableEventRequest
            {
                SessionId = opened.SessionId,
                EventMask = DomainEventGate.EID_ITEMCHANGE,
            },
            fixture.Context);

        Assert.Equal(WireRetCode.Ok, disabled.RetCode);
        Assert.Equal(4L, disabled.Gate.Mask);

        GetEventGateResponse read = await fixture.Service.GetEventGate(
            new GetEventGateRequest { SessionId = opened.SessionId },
            fixture.Context);

        // THE :L43 COUPLING IS OBSERVABLE THROUGH THIS SURFACE: the item-change bit is reported, and
        // disabling it also suppresses column-expression calculation.
        Assert.Contains(WireEventGate.Types.Bit.EidItemchange, read.DisabledBits);

        EnableEventResponse enabled = await fixture.Service.EnableEvent(
            new EnableEventRequest
            {
                SessionId = opened.SessionId,
                EventMask = DomainEventGate.EID_ITEMCHANGE,
            },
            fixture.Context);

        Assert.Equal(WireRetCode.Ok, enabled.RetCode);
        Assert.Equal(0L, enabled.Gate.Mask);

        CloseValidationSessionResponse closed = await fixture.Service.CloseValidationSession(
            new CloseValidationSessionRequest { SessionId = opened.SessionId },
            fixture.Context);

        Assert.Equal(WireRetCode.Ok, closed.RetCode);
        Assert.True(closed.WasOpen);

        // IDEMPOTENT: closing again is OK with was_open false, not an error.
        CloseValidationSessionResponse again = await fixture.Service.CloseValidationSession(
            new CloseValidationSessionRequest { SessionId = opened.SessionId },
            fixture.Context);

        Assert.Equal(WireRetCode.Ok, again.RetCode);
        Assert.False(again.WasOpen);
    }

    [Fact]
    public async Task AZeroMaskIsTheOraclesInvalidArgumentOnBothOperations()
    {
        C03Fixture fixture = new();

        OpenValidationSessionResponse opened = await fixture.Service.OpenValidationSession(
            new OpenValidationSessionRequest { DatawindowHandle = "dw-1" },
            fixture.Context);

        // se_cst_dw.sru:L506 and :L530 - both guards.
        Assert.Equal(
            WireRetCode.EInvalidArgument,
            (await fixture.Service.DisableEvent(
                new DisableEventRequest { SessionId = opened.SessionId, EventMask = 0L },
                fixture.Context)).RetCode);

        Assert.Equal(
            WireRetCode.EInvalidArgument,
            (await fixture.Service.EnableEvent(
                new EnableEventRequest { SessionId = opened.SessionId, EventMask = 0L },
                fixture.Context)).RetCode);
    }

    [Fact]
    public async Task AMaskOutsideTheLegacyThirtyTwoBitDomainIsRefusedNotTruncated()
    {
        C03Fixture fixture = new();

        OpenValidationSessionResponse opened = await fixture.Service.OpenValidationSession(
            new OpenValidationSessionRequest { DatawindowHandle = "dw-1" },
            fixture.Context);

        Assert.Equal(
            WireRetCode.EInvalidArgument,
            (await fixture.Service.DisableEvent(
                new DisableEventRequest
                {
                    SessionId = opened.SessionId,
                    EventMask = (long)uint.MaxValue + 1L,
                },
                fixture.Context)).RetCode);

        Assert.Equal(
            WireRetCode.EInvalidArgument,
            (await fixture.Service.OpenValidationSession(
                new OpenValidationSessionRequest
                {
                    DatawindowHandle = "dw-1",
                    InitialDisabledEventMask = -1L,
                },
                fixture.Context)).RetCode);
    }

    [Fact]
    public async Task AnUnknownSessionIsADefinedErrorAndNeverCreated()
    {
        C03Fixture fixture = new();

        Assert.Equal(
            WireRetCode.EInvalidHandle,
            (await fixture.Service.GetEventGate(
                new GetEventGateRequest { SessionId = "no-such-session" },
                fixture.Context)).RetCode);

        Assert.Equal(
            WireRetCode.EInvalidArgument,
            (await fixture.Service.GetEventGate(
                new GetEventGateRequest { SessionId = string.Empty },
                fixture.Context)).RetCode);

        // And it really was not created: opening never happened, so the registry stays empty.
        Assert.Equal(0, fixture.Registry.Count);
    }

    [Fact]
    public async Task RetrieveStreamsProgressivelyAndAlwaysMarksAFinalChunk()
    {
        C03Fixture fixture = new();
        fixture.Persistence.QueryScript.Add(Chunk(1L, 1L, 2L));
        fixture.Persistence.QueryScript.Add(Chunk(2L, 3L, 4L));

        C03StreamWriter<RetrieveChunk> writer = new();

        await fixture.Service.Retrieve(
            new RetrieveRequest { DatawindowHandle = "dw-1", ChunkSize = 2000 },
            writer,
            fixture.Context);

        Assert.Equal(3, writer.Written.Count);
        Assert.Equal(2L, writer.Written[0].RowCount);
        Assert.Equal(1L, writer.Written[0].ChunkIndex);
        Assert.Equal(DwBuffer.Primary, writer.Written[0].Buffer);
        Assert.Equal(2L, writer.Written[1].ChunkIndex);
        Assert.Equal(4L, writer.Written[1].CumulativeRowCount);
        Assert.True(writer.Written[^1].Final);
        Assert.Equal(4L, writer.Written[^1].CumulativeRowCount);

        // Nothing before the last is marked final, which is what "progressive" means.
        Assert.All(writer.Written.Take(2), chunk => Assert.False(chunk.Final));

        // The handle IS the data object, and the chunk size travelled unchanged - ON THE CREATE CALL,
        // which is where C-05 puts a task's initial configuration. It is deliberately NOT repeated on the
        // run call: QueryRequest.spec merges over the state the task already has, and a clause setter
        // carrying SQL_MS_APPEND applied twice would append the same fragment twice.
        PersistenceQuerySpec spec = Assert.Single(fixture.Persistence.QueryStub.CreateTaskRequests).Spec;
        Assert.Equal("dw-1", spec.DataObject);
        Assert.Equal(2000L, spec.ChunkSize);
        Assert.Null(fixture.Persistence.LastQuery?.Spec);

        // AND THE RUN CALL NAMED THE TASK THE CREATE CALL ISSUED. A default handle is not a lenient
        // default: Persistence refuses a blank one with E_INVALID_HANDLE before it reaches a statement.
        Assert.Equal(PersistenceStubTaskIds.Query, fixture.Persistence.LastQuery?.Task.TaskId);

        // AND WHAT IT TOOK, IT GAVE BACK. One session begun and one ended, one task created and one
        // released - the leak assertion, which no assertion about the chunks above can make.
        Assert.Single(fixture.Persistence.TransactionStub.BeginRequests);
        Assert.Single(fixture.Persistence.TransactionStub.EndRequests);
        Assert.Equal(
            PersistenceStubTaskIds.Query,
            Assert.Single(fixture.Persistence.QueryStub.ReleaseTaskRequests).Task.TaskId);
    }

    [Fact]
    public async Task RetrieveWritesTheFinalChunkEvenForAnEmptyResult()
    {
        C03Fixture fixture = new();
        C03StreamWriter<RetrieveChunk> writer = new();

        await fixture.Service.Retrieve(
            new RetrieveRequest { DatawindowHandle = "dw-1" },
            writer,
            fixture.Context);

        // An empty retrieval must be distinguishable from a truncated stream.
        RetrieveChunk only = Assert.Single(writer.Written);
        Assert.True(only.Final);
        Assert.Equal(0L, only.RowCount);
    }

    [Fact]
    public async Task RetrieveDropsBuffersTheCallerDidNotAskFor()
    {
        C03Fixture fixture = new();

        PersistenceQueryDataChunk mixed = new()
        {
            ChunkIndex = 1L,
            State = new PersistenceCarrierState
            {
                Segments =
                {
                    new PersistenceCarrierBufferSegment
                    {
                        Buffer = DwBuffer.Primary,
                        Rows = { new DataWindowRow { Buffer = DwBuffer.Primary, Row = 1L } },
                    },
                    new PersistenceCarrierBufferSegment
                    {
                        Buffer = DwBuffer.Delete,
                        Rows = { new DataWindowRow { Buffer = DwBuffer.Delete, Row = 9L } },
                    },
                },
            },
        };

        fixture.Persistence.QueryScript.Add(new PersistenceQueryResponse { DataChunk = mixed });

        C03StreamWriter<RetrieveChunk> writer = new();

        // Default request: the primary buffer only, which is the legacy default for a fresh retrieval.
        await fixture.Service.Retrieve(
            new RetrieveRequest { DatawindowHandle = "dw-1" },
            writer,
            fixture.Context);

        Assert.Equal(1L, writer.Written[0].RowCount);
        Assert.Equal(DwBuffer.Primary, writer.Written[0].Buffer);
    }

    [Fact]
    public async Task AMixedBufferChunkLeavesTheConvenienceFieldABSENT()
    {
        C03Fixture fixture = new();

        PersistenceQueryDataChunk mixed = new()
        {
            ChunkIndex = 1L,
            State = new PersistenceCarrierState
            {
                Segments =
                {
                    new PersistenceCarrierBufferSegment
                    {
                        Buffer = DwBuffer.Primary,
                        Rows = { new DataWindowRow { Buffer = DwBuffer.Primary, Row = 1L } },
                    },
                    new PersistenceCarrierBufferSegment
                    {
                        Buffer = DwBuffer.Filter,
                        Rows = { new DataWindowRow { Buffer = DwBuffer.Filter, Row = 2L } },
                    },
                },
            },
        };

        fixture.Persistence.QueryScript.Add(new PersistenceQueryResponse { DataChunk = mixed });

        C03StreamWriter<RetrieveChunk> writer = new();

        RetrieveRequest request = new() { DatawindowHandle = "dw-1" };
        request.Buffers.Add(DwBuffer.Primary);
        request.Buffers.Add(DwBuffer.Filter);

        await fixture.Service.Retrieve(request, writer, fixture.Context);

        // ABSENCE is the encoding of "no single answer" - never Primary as a stand-in.
        Assert.False(writer.Written[0].HasBuffer);
        Assert.Equal(2L, writer.Written[0].RowCount);
    }

    [Fact]
    public async Task AFailingUpstreamStatusEndsTheStreamOnAFinalErrorChunkAndThenFailsTheCall()
    {
        C03Fixture fixture = new();

        fixture.Persistence.QueryScript.Add(new PersistenceQueryResponse
        {
            Status = new PersistenceOperationStatus
            {
                RetCode = WireRetCode.EDbError,
                ErrorText = "no such table: COMPANY",
                DbError = new DbError { Sqldbcode = -1L, Sqlsyntax = "SELECT * FROM COMPANY" },
            },
        });

        C03StreamWriter<RetrieveChunk> writer = new();

        // THE CALL FAILS. It used to return normally after writing this chunk, which reported a FAILED
        // retrieval as a successful empty one: RetrieveChunk declares no return-code and no error-text
        // field, so a caller reading only the stream learned nothing at all about a non-database failure
        // and read a database failure as "zero rows, here is an error you may ignore".
        RpcException failure = await Assert.ThrowsAsync<RpcException>(async () =>
            await fixture.Service.Retrieve(
                new RetrieveRequest { DatawindowHandle = "dw-1" },
                writer,
                fixture.Context));

        // THE PAYLOAD IS WRITTEN FIRST AND THE STATUS RAISED AFTER IT, because gRPC delivers every message
        // before the status - so a consumer reads the DbError and then learns the call failed. Raising
        // first would discard the payload.
        RetrieveChunk terminal = Assert.Single(writer.Written);
        Assert.True(terminal.Final);
        Assert.Equal(0L, terminal.RowCount);
        Assert.NotNull(terminal.Error);

        // RELAYED to the caller, who is entitled to it. The redaction rule is about the LOG.
        Assert.Equal("SELECT * FROM COMPANY", terminal.Error.Sqlsyntax);

        // AND THE TWO FACTS THAT HAVE NOWHERE TO LIVE ON THE CHUNK TRAVEL IN THE STATUS.
        Assert.Contains(
            RetCode.E_DB_ERROR.ToString(CultureInfo.InvariantCulture),
            failure.Status.Detail,
            StringComparison.Ordinal);

        Assert.Contains("no such table: COMPANY", failure.Status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARetrievalOpensASessionAndATaskAndReleasesBothInOrder()
    {
        C03Fixture fixture = new();

        await fixture.Service.Retrieve(
            new RetrieveRequest { DatawindowHandle = "dw-1" },
            new C03StreamWriter<RetrieveChunk>(),
            fixture.Context);

        // A QueryRequest carries a TaskHandle as its FIRST field, so a retrieval sent without one reaches
        // Persistence with no task to run on and is answered E_INVALID_HANDLE before it reads a row.
        Assert.NotNull(fixture.Persistence.LastQuery?.Task);
        Assert.Equal(PersistenceStubTaskIds.Query, fixture.Persistence.LastQuery.Task.TaskId);

        // The session was opened, and the descriptor carries NO credential: the evidenced SQLite connection
        // is a file URI with no user [w_test_sqlite.srw:L450-L456].
        BeginSessionRequest opened = Assert.Single(fixture.Persistence.TransactionStub.BeginRequests);
        Assert.NotNull(opened.Descriptor_);
        Assert.Empty(opened.Descriptor_.Logid);
        Assert.Empty(opened.Descriptor_.Logpass);

        // BOTH acquisitions released, innermost first. An unreleased task or session holds a pooled
        // transaction - and therefore a connection - until the process ends.
        Assert.Contains(nameof(FakeQueryServiceClient.ReleaseQueryTaskAsync), fixture.Persistence.QueryStub.Calls);
        Assert.Contains(
            nameof(FakeTransactionServiceClient.EndSessionAsync),
            fixture.Persistence.TransactionStub.Calls);
    }

    [Fact]
    public async Task ARetrievalReleasesItsTaskAndSessionEVENWhenTheStreamFails()
    {
        C03Fixture fixture = new();

        fixture.Persistence.QueryScript.Add(new PersistenceQueryResponse
        {
            Status = new PersistenceOperationStatus { RetCode = WireRetCode.EDbError },
        });

        _ = await Assert.ThrowsAsync<RpcException>(async () =>
            await fixture.Service.Retrieve(
                new RetrieveRequest { DatawindowHandle = "dw-1" },
                new C03StreamWriter<RetrieveChunk>(),
                fixture.Context));

        // THE finally IS THE WHOLE POINT. A failure path that skips the release leaks exactly when the
        // system is already under stress, which is the worst possible moment to start holding connections.
        Assert.Contains(nameof(FakeQueryServiceClient.ReleaseQueryTaskAsync), fixture.Persistence.QueryStub.Calls);
        Assert.Contains(
            nameof(FakeTransactionServiceClient.EndSessionAsync),
            fixture.Persistence.TransactionStub.Calls);
    }

    [Fact]
    public async Task RetrieveRefusesABlankHandle()
    {
        C03Fixture fixture = new();

        RpcException failure = await Assert.ThrowsAsync<RpcException>(async () =>
            await fixture.Service.Retrieve(
                new RetrieveRequest(),
                new C03StreamWriter<RetrieveChunk>(),
                fixture.Context));

        Assert.Equal(StatusCode.InvalidArgument, failure.StatusCode);
    }

    /// <summary>
    /// ⚠ A BUFFER OUTSIDE THE THREE IS REFUSED, WHERE IT USED TO PRODUCE A SUCCESSFUL EMPTY STREAM.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A proto3 enum field is an open <see cref="int"/> on the wire, so <c>repeated DwBuffer</c> accepts 7
    /// as readily as it accepts Primary!, Delete! and Filter!. The unknown value then simply never matched
    /// the per-segment test, so the caller received a COMPLETE stream - final marker and all - carrying no
    /// rows. That is the worst available failure shape, because <c>RetrieveChunk</c> has no outcome field:
    /// a caller cannot tell it apart from a DataWindow that genuinely has no rows, so a typo'd or
    /// version-skewed buffer reads as "no rows" rather than as "no such buffer".
    /// </para>
    /// <para>
    /// The upstream assertions are the second half and are not decoration: the guard runs BEFORE the query
    /// scope is opened, so a request that cannot be served costs neither a Persistence session nor a
    /// borrowed transaction. A guard placed after the scope would refuse correctly and still pay for the
    /// refusal.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task RetrieveRefusesABufferOutsideTheThreeBeforeItAcquiresAnythingUpstream()
    {
        C03Fixture fixture = new();

        RetrieveRequest request = new() { DatawindowHandle = "dw-1" };
        request.Buffers.Add(DwBuffer.Primary);
        request.Buffers.Add((DwBuffer)7);

        C03StreamWriter<RetrieveChunk> writer = new();

        RpcException failure = await Assert.ThrowsAsync<RpcException>(async () =>
            await fixture.Service.Retrieve(request, writer, fixture.Context));

        Assert.Equal(StatusCode.InvalidArgument, failure.StatusCode);

        // The offending value is named, so a caller with a repeated field does not have to bisect it.
        Assert.Contains("7", failure.Status.Detail, StringComparison.Ordinal);

        // NOTHING WAS STREAMED, so the refusal cannot be mistaken for an empty result.
        Assert.Empty(writer.Written);

        // AND NOTHING WAS ACQUIRED - no session, no task.
        Assert.DoesNotContain(
            nameof(FakeTransactionServiceClient.BeginSessionAsync),
            fixture.Persistence.TransactionStub.Calls);
        Assert.DoesNotContain(
            nameof(FakeQueryServiceClient.CreateQueryTaskAsync),
            fixture.Persistence.QueryStub.Calls);
    }

    /// <summary>
    /// AND ALL THREE REAL BUFFERS ARE STILL ACCEPTED, so the guard above is not a blanket refusal of the
    /// non-default buffers.
    /// </summary>
    /// <param name="buffer">The buffer to request.</param>
    [Theory]
    [InlineData(DwBuffer.Primary)]
    [InlineData(DwBuffer.Delete)]
    [InlineData(DwBuffer.Filter)]
    public async Task RetrieveAcceptsEachOfTheThreeRealBuffers(DwBuffer buffer)
    {
        C03Fixture fixture = new();

        fixture.Persistence.QueryScript.Add(new PersistenceQueryResponse
        {
            Status = new PersistenceOperationStatus { RetCode = WireRetCode.Ok },
        });

        RetrieveRequest request = new() { DatawindowHandle = "dw-1" };
        request.Buffers.Add(buffer);

        C03StreamWriter<RetrieveChunk> writer = new();

        await fixture.Service.Retrieve(request, writer, fixture.Context);

        // The final marker is the only chunk for an empty result, and its presence is what says the stream
        // completed rather than being truncated.
        Assert.NotEmpty(writer.Written);
    }

    /// <summary>
    /// AND AN EMPTY BUFFER LIST IS STILL THE PRIMARY-ONLY DEFAULT, which the guard must not turn into a
    /// refusal: the legacy default is that a freshly retrieved DataWindow has nothing in Delete! and
    /// nothing in Filter!, so "unspecified" is a legitimate request rather than a missing one.
    /// </summary>
    [Fact]
    public async Task RetrieveStillTreatsAnEmptyBufferListAsPrimaryOnly()
    {
        C03Fixture fixture = new();

        fixture.Persistence.QueryScript.Add(new PersistenceQueryResponse
        {
            Status = new PersistenceOperationStatus { RetCode = WireRetCode.Ok },
        });

        C03StreamWriter<RetrieveChunk> writer = new();

        await fixture.Service.Retrieve(
            new RetrieveRequest { DatawindowHandle = "dw-1" },
            writer,
            fixture.Context);

        Assert.NotEmpty(writer.Written);
    }

    [Fact]
    public async Task RetrieveArgumentsCarryTheirOneBasedLegacyOrdinal()
    {
        C03Fixture fixture = new();

        RetrieveRequest request = new() { DatawindowHandle = "dw-1" };
        request.Arguments.Add(new AnyValue { StringValue = "first" });
        request.Arguments.Add(new AnyValue { StringValue = "second" });

        await fixture.Service.Retrieve(request, new C03StreamWriter<RetrieveChunk>(), fixture.Context);

        // Wire element 0 IS legacy args[1] - the one place a basis changes. Read from the CREATE call,
        // which is where the specification travels.
        PersistenceQuerySpec spec = Assert.Single(fixture.Persistence.QueryStub.CreateTaskRequests).Spec;
        Assert.Equal("1", spec.Parameters[0].Name);
        Assert.Equal("first", spec.Parameters[0].Value.StringValue);
        Assert.Equal("2", spec.Parameters[1].Name);
        Assert.Equal("second", spec.Parameters[1].Value.StringValue);
    }

    [Fact]
    public async Task AZeroChunkSizeIsNotForwardedBecauseItMeansTheServerChooses()
    {
        C03Fixture fixture = new();

        await fixture.Service.Retrieve(
            new RetrieveRequest { DatawindowHandle = "dw-1" },
            new C03StreamWriter<RetrieveChunk>(),
            fixture.Context);

        Assert.False(
            Assert.Single(fixture.Persistence.QueryStub.CreateTaskRequests).Spec.HasChunkSize);
    }

    /// <summary>
    /// Every chunk size a caller can actually state travels, so the guard that owns it adjudicates it.
    /// </summary>
    /// <param name="requested">The size the caller stated.</param>
    /// <remarks>
    /// 🔴 <b>THE FILTER USED TO BE <c>&gt; 0</c>, AND THAT DISCARDED THE WORST VALUES SILENTLY.</b> A caller
    /// sending <c>-5</c> had it dropped here and received a successful retrieval chunked at the server's own
    /// size, while a caller sending <c>500</c> had it forwarded and was refused
    /// <c>E_INVALID_ARGUMENT</c> by C-05's guard [<c>n_cst_thread_task_sqlquery.sru:L410</c>] - two equally
    /// nonsensical sizes, two different outcomes, and the more obviously wrong of the two accepted.
    /// <para>
    /// THE FIX FORWARDS RATHER THAN VALIDATES, WHICH IS WHY THE REFUSED VALUES ARE ROWS HERE TOO. This
    /// boundary deliberately does not reproduce C-05's guard (C-B): copying it would invent a validation
    /// the legacy does not perform here. So <c>500</c> and <c>1000</c> are asserted to TRAVEL, not to be
    /// refused - the refusal belongs to the layer that owns the rule, and
    /// <c>PersistenceWorkHandleLifecycleTests</c> asserts it arrives as <c>400</c>.
    /// </para>
    /// <para>
    /// Zero is the one value that cannot travel, and the sibling row above pins it: a proto3 scalar cannot
    /// distinguish an absent field from a zero one, so zero is the only size a caller cannot have stated
    /// deliberately. Both boundaries of the guard are rows here so a later change cannot quietly reinstate a
    /// range test that happens to agree with it on one side.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(-5)]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(500)]
    [InlineData(1000)]
    [InlineData(1001)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public async Task EveryStatedChunkSizeTravelsSoTheGuardThatOwnsItAdjudicatesIt(int requested)
    {
        C03Fixture fixture = new();

        await fixture.Service.Retrieve(
            new RetrieveRequest { DatawindowHandle = "dw-1", ChunkSize = requested },
            new C03StreamWriter<RetrieveChunk>(),
            fixture.Context);

        PersistenceQuerySpec spec = Assert.Single(fixture.Persistence.QueryStub.CreateTaskRequests).Spec;

        Assert.True(spec.HasChunkSize);
        Assert.Equal(requested, spec.ChunkSize);
    }

    [Fact]
    public async Task AConflictIsSurfacedAsAbortedWithItsDetailAndIsNeverRetried()
    {
        C03Fixture fixture = new();

        ConflictDetail detail = new() { UpdateTable = "COMPANY", RowsExpected = 1L, RowsMatched = 0L };

        fixture.Persistence.Conflict = new PersistenceConflictException(
            detail,
            RetCode.E_DB_ERROR,
            new RpcException(new Status(StatusCode.Aborted, "upstream")));

        WireUpdateRequest request = new() { DatawindowHandle = "dw-1" };
        request.Rows.Add(new DataWindowRow { Buffer = DwBuffer.Primary, Row = 1L });

        RpcException failure = await Assert.ThrowsAsync<RpcException>(async () =>
            await fixture.Service.Update(request, fixture.Context));

        // The canonical gRPC-to-HTTP 409 mapping.
        Assert.Equal(StatusCode.Aborted, failure.StatusCode);

        // NEVER RETRIED: exactly one upstream attempt.
        Assert.Equal(1, fixture.Persistence.UpdateCalls);

        MethodDescriptor method = Contracts.DataServices.V1.DataWindowService
            .Descriptor
            .FindMethodByName("Update")!;

        RichErrorBinding binding = method.GetOptions()!.GetExtension(CommonV1Extensions.RichError);

        Assert.Equal(10, binding.GrpcStatusCode);

        byte[]? payload = failure.Trailers.GetValueBytes(binding.TrailerKey);

        Assert.NotNull(payload);

        RichErrorTrailer trailer = RichErrorTrailer.Parser.ParseFrom(payload);

        Assert.Equal(RichErrorTrailer.DetailOneofCase.Conflict, trailer.DetailCase);
        Assert.Equal("COMPANY", trailer.Conflict.UpdateTable);
        Assert.Equal(RetCode.E_DB_ERROR, trailer.RetCode);
    }

    // =============================================================================================
    //  F-02 - THE UPSTREAM LIFECYCLE. C-05 and C-06 are TASK-scoped and a task is SESSION-scoped, so a
    //  retrieval and an update are each three or four upstream calls rather than one. These tests assert
    //  the ORDER, the handle actually reaching the payload call, and the release running on every exit
    //  path - the last of which is what separates a working orchestration from one that leaks a
    //  reference-counted pool entry per request.
    // =============================================================================================

    /// <summary>
    /// A retrieval opens a session, creates a task against it, runs, then releases both - in that order.
    /// </summary>
    /// <remarks>
    /// THE HANDLE ON THE RUN CALL IS THE POINT. Before the lifecycle existed this method sent a task-less
    /// QueryRequest, which C-05 rejects for the missing handle - so every retrieval failed upstream for a
    /// task this contract never created. Asserting the handle is present AND is the one the create call
    /// issued is what makes that unrepeatable.
    /// </remarks>
    [Fact]
    public async Task RetrieveRunsTheFullUpstreamLifecycleInOrderAndCarriesTheTaskHandle()
    {
        C03Fixture fixture = new();
        fixture.Persistence.QueryScript.Add(new PersistenceQueryResponse
        {
            Status = new PersistenceOperationStatus { RetCode = WireRetCode.Ok },
        });

        C03StreamWriter<RetrieveChunk> stream = new();

        await fixture.Service.Retrieve(
            new RetrieveRequest { DatawindowHandle = "dw-1" },
            stream,
            fixture.Context);

        Assert.Equal(
            [
                nameof(PersistenceClient.BeginSessionAsync),
                nameof(PersistenceClient.CreateQueryTaskAsync),
                nameof(PersistenceClient.QueryAsync),
                nameof(PersistenceClient.ReleaseQueryTaskAsync),
                nameof(PersistenceClient.EndSessionAsync),
            ],
            fixture.Persistence.Lifecycle);

        Assert.Equal(PersistenceStubTaskIds.Query, fixture.Persistence.LastQuery!.Task.TaskId);

        // ⚠ AND THE SPEC IS ON THE CREATE CALL ONLY, WHICH REPLACES WHAT THIS ROW FIRST ASSERTED ⚠
        //
        // It was written expecting the SAME spec instance on both calls, so that "a task cannot be created
        // with one specification and run with another". That property is desirable and this is not the way
        // to get it: repeating the spec on the run call is not a restatement, it is a SECOND APPLICATION.
        // C-05 documents QueryRequest.spec as "merged over whatever the Set* calls already applied", and a
        // clause setter is not idempotent - a WHERE clause carrying SQL_MS_APPEND applied twice appends the
        // same fragment twice and changes the statement that executes. The sibling row
        // ARetrievalOpensASessionAndATaskAndReleasesBothInOrder pins the correct shape directly, by
        // asserting the run call carries NO spec at all; what this row keeps is the other half, that the run
        // call names the handle the create call issued.
        Assert.Equal("dw-1", fixture.Persistence.LastCreateQueryTask!.Spec.DataObject);
        Assert.Null(fixture.Persistence.LastQuery.Spec);
    }

    /// <summary>
    /// The session descriptor comes from configuration, and NO connection-flag message travels with it -
    /// so the connection-parameter string is the single authority for the two bind flags.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The descriptor is the reason a session exists: it is resolved ONCE by <c>BeginSession</c> so that its
    /// log-password field does not travel on every later call.
    /// </para>
    /// <para>
    /// ⚠ THE ABSENT FLAGS MESSAGE IS THE ASSERTION, AND THIS ROW USED TO ASSERT THE OPPOSITE. It was
    /// written expecting <c>Flags.DisableBind</c> and <c>Flags.NcharBind</c> to arrive true, from two
    /// independently settable options beside <c>DbParm</c> - and the very configuration it used demonstrates
    /// why that shape was a defect. <c>DbParm="DisableBind=1"</c> resolves, by the oracle's own nested
    /// reading [<c>n_cst_thread_task_sqlbase.sru:L127-L132</c>], to <c>disable_bind=true</c> and
    /// <c>nchar_bind=FALSE</c>, because the second key is consulted only inside the first key's branch. The
    /// flags this row asserted therefore DISAGREED with the string sent beside them, and Persistence
    /// refuses such a session outright with <c>E_INVALID_ARGUMENT</c> - not the request, the SESSION, so
    /// every retrieval and every update on the deployment would have failed.
    /// </para>
    /// <para>
    /// The contract's flag message is a caller ASSERTION about what the string resolves to, and this
    /// service forwards the string unexamined, so it has nothing independent to assert. Omitting it selects
    /// the documented absent arm - "take them from the connection-parameter string" - which is the only
    /// reading with one authority. The configuration below is kept EXACTLY as it was, deliberately: it is
    /// the configuration that used to break the service, and it must now open a session cleanly.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task RetrieveOpensTheSessionFromTheConfiguredDescriptorAndSendsNoConnectionFlags()
    {
        DataServicesOptions configured = new()
        {
            PersistenceSession = new PersistenceSessionOptions
            {
                Dbms = "SQLite",
                Database = "powerframework",
                LogId = "operator",
                LogPass = "not-echoed",
                DbParm = "DisableBind=1",
            },
        };

        C03Fixture fixture = new(configured: configured);
        fixture.Persistence.QueryScript.Add(new PersistenceQueryResponse
        {
            Status = new PersistenceOperationStatus { RetCode = WireRetCode.Ok },
        });

        await fixture.Service.Retrieve(
            new RetrieveRequest { DatawindowHandle = "dw-1" },
            new C03StreamWriter<RetrieveChunk>(),
            fixture.Context);

        PowerFramework.Contracts.Persistence.V1.BeginSessionRequest opened =
            fixture.Persistence.LastBeginSession!;

        Assert.Equal("SQLite", opened.Descriptor_.Dbms);
        Assert.Equal("powerframework", opened.Descriptor_.Database);
        Assert.Equal("operator", opened.Descriptor_.Logid);
        Assert.Equal("not-echoed", opened.Descriptor_.Logpass);

        // The string travels; nothing that could contradict it does.
        Assert.Equal("DisableBind=1", opened.Descriptor_.Dbparm);
        Assert.Null(opened.Flags);
    }

    /// <summary>
    /// AND THERE IS NO WAY TO CONFIGURE A DISAGREEMENT, which is the half a behavioural assertion cannot
    /// reach: the two flag properties are gone from the options surface entirely.
    /// </summary>
    /// <remarks>
    /// A ROW ABOUT ABSENCE, DELIBERATELY. The sibling above proves the flags are not SENT today; this one
    /// proves an operator cannot reintroduce the disagreement by setting them, and it is what would fail if
    /// someone re-added the properties "for completeness" and wired them back onto the request. The names
    /// are asserted by string because a property that does not exist cannot be named in code.
    /// </remarks>
    [Fact]
    public void ThePersistenceSessionOptionsOfferNoWayToContradictTheConnectionParameterString()
    {
        Type options = typeof(PersistenceSessionOptions);

        Assert.Null(options.GetProperty("DisableBind"));
        Assert.Null(options.GetProperty("NCharBind"));

        // The one input is still there, so the assertions above are about a REMOVED alternative rather
        // than about a group that lost its whole purpose.
        Assert.NotNull(options.GetProperty(nameof(PersistenceSessionOptions.DbParm)));
    }

    /// <summary>
    /// The persistence client options carry no second, unread transaction group.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>DEAD CONFIGURATION IS WORSE THAN MISSING CONFIGURATION, WHICH IS WHY ITS ABSENCE IS ASSERTED.</b>
    /// A <c>Transaction</c> group used to be declared here and bound from
    /// <c>DataServices:Persistence:Transaction</c> while nothing in the service ever read it - and it
    /// DISAGREED with the group that is read, defaulting the database to a table name and auto-commit to
    /// true where the live group defaults to empty and false. An operator tuning the settings file would
    /// have seen two plausible places to configure one session, changed the one with the more specific
    /// defaults, observed no effect, and had nothing in the service to tell them why.
    /// </para>
    /// <para>
    /// Asserted by string, because a property that does not exist cannot be named in code, and paired with
    /// a positive assertion about the group that IS read so the row cannot pass by the whole surface having
    /// been renamed.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePersistenceClientOptionsDeclareNoSecondUnreadTransactionGroup()
    {
        Type options = typeof(PersistenceClientOptions);

        Assert.Null(options.GetProperty("Transaction"));

        // The group's remaining purpose is intact, so this is a row about a removed alternative rather than
        // about a type that lost everything. The descriptor lives on PersistenceSessionOptions, bound from
        // its own section, and that is the single authority the service reads.
        Assert.NotNull(options.GetProperty(nameof(PersistenceClientOptions.Address)));
        Assert.NotNull(typeof(PersistenceSessionOptions).GetProperty(nameof(PersistenceSessionOptions.Dbms)));
    }

    /// <summary>
    /// A session that cannot be opened is a call failure and streams nothing.
    /// </summary>
    /// <remarks>
    /// <b>THE ALTERNATIVE IS THE DEFECT THIS CLOSES.</b> <c>RetrieveChunk</c> has no outcome field, so a
    /// lifecycle failure written as a final chunk with no rows would be indistinguishable from a
    /// legitimately empty retrieval - a failure to reach the database would read to every caller as "this
    /// DataWindow has no rows". So it travels as the call status, and NO chunk is written.
    /// </remarks>
    [Fact]
    public async Task RetrieveProjectsASessionFailureAsACallStatusAndWritesNoChunk()
    {
        C03Fixture fixture = new();
        fixture.Persistence.BeginSessionOutcome = WireRetCode.EInvalidTransaction;

        C03StreamWriter<RetrieveChunk> stream = new();

        RpcException failure = await Assert.ThrowsAsync<RpcException>(() => fixture.Service.Retrieve(
            new RetrieveRequest { DatawindowHandle = "dw-1" },
            stream,
            fixture.Context));

        Assert.Equal(StatusCode.FailedPrecondition, failure.StatusCode);
        Assert.Empty(stream.Written);

        // No task was created, so nothing needed releasing; and no session was issued, so nothing was
        // ended. The absence is the correct shape - a release of a handle that was never issued would be a
        // second defect.
        Assert.Equal([nameof(PersistenceClient.BeginSessionAsync)], fixture.Persistence.Lifecycle);

        // THE OUTCOME IS NAMED NUMERICALLY, which identifies the refusal without carrying row data.
        //
        // This row previously asserted the status message contained the words "deliberately withheld",
        // which is a phrase rather than a property: it passed only while one particular sentence was
        // present, and it says nothing about what the message actually carries. What matters, and what is
        // asserted instead, is that the DRIVER's own message never reaches a status. It cannot: the driver's
        // text lives in DbError.sqlerrtext and no acquisition failure reads that field - the status carries
        // the numeric outcome and, when the upstream supplied one, PERSISTENCE's own refusal diagnostic,
        // which names which setting was refused and is the caller's to act on. That is the same division
        // BuildUpstreamFailure applies and the same one the update path applies to the statement field:
        // relayed to the caller, who is entitled to it, and never written to this service's own log.
        Assert.Contains(
            RetCode.E_INVALID_TRANSACTION.ToString(CultureInfo.InvariantCulture),
            failure.Status.Detail,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A success answered without a handle is reclassified rather than carried forward.
    /// </summary>
    [Fact]
    public async Task RetrieveRefusesAHandlelessSessionSuccessRatherThanSendingATasklessRequest()
    {
        C03Fixture fixture = new();
        fixture.Persistence.BeginSessionWithoutHandle = true;

        RpcException failure = await Assert.ThrowsAsync<RpcException>(() => fixture.Service.Retrieve(
            new RetrieveRequest { DatawindowHandle = "dw-1" },
            new C03StreamWriter<RetrieveChunk>(),
            fixture.Context));

        Assert.Equal(StatusCode.Internal, failure.StatusCode);
        Assert.Equal([nameof(PersistenceClient.BeginSessionAsync)], fixture.Persistence.Lifecycle);
    }

    /// <summary>
    /// A task that cannot be created ends the session it was created against.
    /// </summary>
    [Fact]
    public async Task RetrieveEndsTheSessionWhenTheTaskCannotBeCreated()
    {
        C03Fixture fixture = new();
        fixture.Persistence.CreateTaskOutcome = WireRetCode.EBusy;

        RpcException failure = await Assert.ThrowsAsync<RpcException>(() => fixture.Service.Retrieve(
            new RetrieveRequest { DatawindowHandle = "dw-1" },
            new C03StreamWriter<RetrieveChunk>(),
            fixture.Context));

        // E_BUSY is retryable, so it must not arrive as a flat Internal.
        Assert.Equal(StatusCode.ResourceExhausted, failure.StatusCode);

        Assert.Equal(
            [
                nameof(PersistenceClient.BeginSessionAsync),
                nameof(PersistenceClient.CreateQueryTaskAsync),
                nameof(PersistenceClient.EndSessionAsync),
            ],
            fixture.Persistence.Lifecycle);
    }

    /// <summary>
    /// A cancelled retrieval still releases its task and ends its session.
    /// </summary>
    /// <remarks>
    /// <b>THE PATH THAT MATTERS MOST.</b> Cancellation is the ordinary way a client abandons a stream, and
    /// it is precisely where a cleanup that honoured the caller's token would abandon the release - pinning
    /// a reference-counted pool entry per abandoned request until its idle expiry sweeps it. The recording
    /// client asserts the token it receives cannot be cancelled, so this test proves both halves at once.
    /// </remarks>
    [Fact]
    public async Task RetrieveReleasesTheTaskAndEndsTheSessionWhenTheCallerCancels()
    {
        C03Fixture fixture = new();
        fixture.Persistence.QueryScript.Add(new PersistenceQueryResponse
        {
            DataChunk = new PersistenceQueryDataChunk(),
        });

        // ⚠ CANCELLED MID-STREAM, NOT BEFORE THE CALL, AND THE DIFFERENCE IS THE WHOLE ROW ⚠
        //
        // This row used to cancel before calling, which cannot reach what it asserts: the very first
        // upstream request travels under the caller's token, so an already-cancelled token refuses the
        // session before it is opened and there is then no task and no session to release. The row would
        // then be asserting a release on a path where nothing was ever acquired. Cancelling on the first
        // written chunk puts the cancellation exactly where the finding was - after both handles exist and
        // before the stream ends - so the release really is the thing under test.
        C03StreamWriter<RetrieveChunk> stream = new();
        stream.OnWrite = _ => fixture.Lifetime.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.Retrieve(
            new RetrieveRequest { DatawindowHandle = "dw-1" },
            stream,
            fixture.Context));

        Assert.Equal(
            [
                nameof(PersistenceClient.ReleaseQueryTaskAsync),
                nameof(PersistenceClient.EndSessionAsync),
            ],
            fixture.Persistence.Lifecycle[^2..]);
    }

    /// <summary>
    /// A failing terminal status that carried no database error is a call failure, not an empty result.
    /// </summary>
    /// <remarks>
    /// <b>THE DEFECT THIS CLOSES PRODUCED A BYTE-FOR-BYTE CORRECT-LOOKING ANSWER.</b>
    /// <c>RetrieveChunk</c> can express a database error and nothing else, so a refusal that brought none -
    /// a rejected clause, an invalid paging request, a bad chunk size - had no field to occupy and was
    /// delivered as the plain final marker: identical to what a DataWindow with no matching rows produces.
    /// No caller could tell a REFUSED query from an EMPTY one, which is the worst class of failure because
    /// it needs no handling to look handled.
    /// </remarks>
    [Theory]
    [InlineData(WireRetCode.EInvalidArgument, StatusCode.FailedPrecondition)]
    [InlineData(WireRetCode.EInvalidSql, StatusCode.FailedPrecondition)]
    [InlineData(WireRetCode.EBusy, StatusCode.ResourceExhausted)]
    [InlineData(WireRetCode.EInvalidHandle, StatusCode.NotFound)]

    // 🔴 THE ROWS THAT USED TO FALL TO THE DEFAULT AND REACH A CALLER AS HTTP 500 FOR THEIR OWN MISTAKE.
    // E_INVALID_DATAOBJECT is the one the finding was raised on - a retrieval naming a DataWindow that does
    // not exist. The rest are the same class: a carrier that cannot be applied, an index past the end, a
    // name the upstream could not find, the expression engine's own two not-found codes, and a refusal by
    // policy. Every one of them is a 4xx at the ingress once mapped, and every one of them was a 5xx.
    [InlineData(WireRetCode.EInvalidDataobject, StatusCode.FailedPrecondition)]
    [InlineData(WireRetCode.EInvalidData, StatusCode.FailedPrecondition)]
    [InlineData(WireRetCode.EOutOfBound, StatusCode.FailedPrecondition)]
    [InlineData(WireRetCode.EOutOfRange, StatusCode.FailedPrecondition)]
    [InlineData(WireRetCode.EObjectNotFound, StatusCode.NotFound)]
    [InlineData(WireRetCode.ENotExists, StatusCode.NotFound)]
    [InlineData(WireRetCode.EVarNotFound, StatusCode.NotFound)]
    [InlineData(WireRetCode.EMemberNotFound, StatusCode.NotFound)]
    [InlineData(WireRetCode.EAccessDenied, StatusCode.PermissionDenied)]
    [InlineData(WireRetCode.ENoSupport, StatusCode.Unimplemented)]
    [InlineData(WireRetCode.ENoImplementation, StatusCode.Unimplemented)]
    [InlineData(WireRetCode.ETimeOut, StatusCode.DeadlineExceeded)]
    [InlineData(WireRetCode.ERetry, StatusCode.ResourceExhausted)]
    [InlineData(WireRetCode.EOutOfMemory, StatusCode.ResourceExhausted)]

    // AND THE TWO THAT MUST STAY Internal, because the default arm's own reasoning applies to them: the
    // data path behind this service answered badly, which is a server-side condition whatever its cause.
    // Without these rows a later change could widen the caller-error groups until nothing was left on the
    // default, which would blame the caller for a database failure.
    [InlineData(WireRetCode.EDbError, StatusCode.Internal)]
    [InlineData(WireRetCode.Failed, StatusCode.Internal)]
    public async Task AFailingTerminalStatusWithNoDatabaseErrorIsACallFailureAndNotAnEmptyResult(
        WireRetCode outcome,
        StatusCode expected)
    {
        C03Fixture fixture = new();
        fixture.Persistence.QueryScript.Add(new PersistenceQueryResponse
        {
            Status = new PersistenceOperationStatus { RetCode = outcome },
        });

        C03StreamWriter<RetrieveChunk> stream = new();

        RpcException failure = await Assert.ThrowsAsync<RpcException>(() => fixture.Service.Retrieve(
            new RetrieveRequest { DatawindowHandle = "dw-1" },
            stream,
            fixture.Context));

        Assert.Equal(expected, failure.StatusCode);

        // NO FINAL MARKER WAS WRITTEN. Writing one would be the defect itself: a caller reading the stream
        // would see a complete, empty, successful retrieval.
        Assert.Empty(stream.Written);

        // The lifecycle still unwinds - a refused query must not leak its task or its session.
        Assert.Equal(
            [
                nameof(PersistenceClient.ReleaseQueryTaskAsync),
                nameof(PersistenceClient.EndSessionAsync),
            ],
            fixture.Persistence.Lifecycle[^2..]);
    }

    /// <summary>
    /// A failing terminal status that DID carry a database error travels on the error chunk AND fails the
    /// call.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE CONTRACT HAS A FIELD FOR THE PAYLOAD, SO THE PAYLOAD USES IT. The error chunk is the contract's
    /// own encoding of "a failure the legacy would have surfaced through its error event", and the driver's
    /// code and text inside it reach the caller, who is entitled to them. That is what this row pins, and it
    /// is not covered by the status-only row above: a failure with a <c>DbError</c> must not lose the
    /// <c>DbError</c> on its way to becoming a status.
    /// </para>
    /// <para>
    /// ⚠ AND IT IS STILL A CALL FAILURE, WHICH IS A CORRECTION TO WHAT THIS ROW ORIGINALLY ASSERTED ⚠
    /// </para>
    /// <para>
    /// It was first written expecting the call to COMPLETE, on the reasoning that the error chunk is a
    /// sufficient encoding of the failure. It is not, for three reasons and the third alone decides it.
    /// <c>RetrieveChunk</c> declares no return-code field and no error-text field, so completing normally
    /// discards both - here, <c>E_DB_ERROR</c> and the driver's message. A caller's retry-or-surface policy
    /// keys on the STATUS CODE, as every mapping in this service says at the point it maps one, and an OK
    /// status tells that policy the retrieval succeeded. And the row above - a failing terminal status with
    /// NO database error - already makes exactly this failure class a call failure, so completing this one
    /// normally would make the same failure succeed or fail according to whether the driver happened to
    /// attach a code, which is the "no caller could tell a refused query from an empty one" defect displaced
    /// rather than closed. The payload claim survives; the outcome claim does not.
    /// </para>
    /// <para>
    /// THE ORDER IS PART OF IT. gRPC delivers every message before the status, so the chunk is written first
    /// and the status raised after it: a consumer reads the <c>DbError</c> and then learns the call failed.
    /// Raising first would discard the payload this row exists to protect.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AFailingTerminalStatusCarryingADatabaseErrorStillTravelsOnTheErrorChunk()
    {
        C03Fixture fixture = new();
        fixture.Persistence.QueryScript.Add(new PersistenceQueryResponse
        {
            Status = new PersistenceOperationStatus
            {
                RetCode = WireRetCode.EDbError,
                DbError = new DbError { Sqldbcode = 19L, Sqlerrtext = "UNIQUE constraint failed" },
            },
        });

        C03StreamWriter<RetrieveChunk> stream = new();

        RpcException failure = await Assert.ThrowsAsync<RpcException>(async () =>
            await fixture.Service.Retrieve(
                new RetrieveRequest { DatawindowHandle = "dw-1" },
                stream,
                fixture.Context));

        RetrieveChunk terminal = Assert.Single(stream.Written);

        Assert.True(terminal.Final);
        Assert.Equal(19L, terminal.Error.Sqldbcode);
        Assert.Equal("UNIQUE constraint failed", terminal.Error.Sqlerrtext);

        // AND THE RETURN CODE, WHICH THE CHUNK CANNOT CARRY, IS IN THE STATUS.
        Assert.Contains(
            RetCode.E_DB_ERROR.ToString(CultureInfo.InvariantCulture),
            failure.Status.Detail,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The failure a terminal status CARRYING a database error becomes projects onto its declared status.
    /// </summary>
    /// <param name="outcome">The upstream outcome.</param>
    /// <param name="expected">The gRPC status it must become.</param>
    /// <remarks>
    /// <para>
    /// <b>THE SECOND OF THE TWO STATUS PATHS ON ONE ROUTE, AND THE ONE THE FINDING WAS RAISED ON.</b> A
    /// failing terminal status with NO database error becomes its status through one helper (the theory
    /// above); one that DOES carry a database error writes the error chunk first and then becomes its status
    /// through a DIFFERENT helper. Persistence attaches a database error to an unresolvable-DataObject
    /// retrieval, so it was this path that answered <c>Internal</c> and reached the caller as HTTP 500 for a
    /// mistake that was entirely theirs.
    /// </para>
    /// <para>
    /// THE ROWS ARE THE SAME OUTCOME CODES THE THEORY ABOVE USES, ON PURPOSE. A caller keys its
    /// retry-or-surface policy on the status code, so one upstream code reaching them as a caller error on
    /// one path and a server fault on the other makes that policy unwritable. The two helpers spell the
    /// 400-class group differently - <c>InvalidArgument</c> here, <c>FailedPrecondition</c> there - which is
    /// a recorded divergence rather than a defect: both project to HTTP 400 at the ingress, and each
    /// spelling is the one its own helper's siblings already use.
    /// </para>
    /// <para>
    /// 🔴 <b>E_INVALID_HANDLE IS THE ONE ROW THAT MOVED, AND IT MOVED BECAUSE THE TWO HELPERS DID NOT MERELY
    /// SPELL IT DIFFERENTLY - THEY ANSWERED DIFFERENTLY.</b> This path used to give it
    /// <c>FailedPrecondition</c> while the theory above gave it <c>NotFound</c>, and the published projection
    /// declares no <c>FailedPrecondition</c> row: that status falls to the canonical mapping and reaches the
    /// caller as <c>400</c> carrying <c>E_INVALID_ARGUMENT</c>, replacing the originating code rather than
    /// re-spelling it. So a caller naming a handle the upstream no longer holds was told on one path that it
    /// could not be found and on the other that its argument was malformed. Both paths now answer
    /// <c>NotFound</c>.
    /// </para>
    /// <para>
    /// AND THE DATABASE ERROR STILL TRAVELS ON EVERY ROW. The chunk is asserted alongside the status,
    /// because the payload claim and the classification claim are independent and a fix to one must not
    /// quietly cost the other.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(WireRetCode.EInvalidDataobject, StatusCode.InvalidArgument)]
    [InlineData(WireRetCode.EInvalidArgument, StatusCode.InvalidArgument)]
    [InlineData(WireRetCode.EInvalidSql, StatusCode.InvalidArgument)]
    [InlineData(WireRetCode.EInvalidData, StatusCode.InvalidArgument)]
    [InlineData(WireRetCode.EOutOfBound, StatusCode.InvalidArgument)]
    [InlineData(WireRetCode.EOutOfRange, StatusCode.InvalidArgument)]
    [InlineData(WireRetCode.EObjectNotFound, StatusCode.NotFound)]
    [InlineData(WireRetCode.ENotExists, StatusCode.NotFound)]
    [InlineData(WireRetCode.EVarNotFound, StatusCode.NotFound)]
    [InlineData(WireRetCode.EMemberNotFound, StatusCode.NotFound)]
    [InlineData(WireRetCode.EInvalidHandle, StatusCode.NotFound)]
    [InlineData(WireRetCode.EInvalidTransaction, StatusCode.FailedPrecondition)]
    [InlineData(WireRetCode.EAccessDenied, StatusCode.PermissionDenied)]
    [InlineData(WireRetCode.ENoSupport, StatusCode.Unimplemented)]
    [InlineData(WireRetCode.ENoImplementation, StatusCode.Unimplemented)]
    [InlineData(WireRetCode.EBusy, StatusCode.ResourceExhausted)]
    [InlineData(WireRetCode.EOutOfMemory, StatusCode.ResourceExhausted)]
    [InlineData(WireRetCode.ERetry, StatusCode.Aborted)]
    [InlineData(WireRetCode.ETimeOut, StatusCode.DeadlineExceeded)]

    // The two that must remain server-side, for the reason the default arm states.
    [InlineData(WireRetCode.EDbError, StatusCode.Internal)]
    [InlineData(WireRetCode.Failed, StatusCode.Internal)]
    public async Task ATerminalStatusCarryingADatabaseErrorProjectsOntoItsDeclaredStatus(
        WireRetCode outcome,
        StatusCode expected)
    {
        C03Fixture fixture = new();
        fixture.Persistence.QueryScript.Add(new PersistenceQueryResponse
        {
            Status = new PersistenceOperationStatus
            {
                RetCode = outcome,
                DbError = new DbError { Sqldbcode = 1L, Sqlerrtext = "no such table: COMPANY" },
            },
        });

        C03StreamWriter<RetrieveChunk> stream = new();

        RpcException failure = await Assert.ThrowsAsync<RpcException>(() => fixture.Service.Retrieve(
            new RetrieveRequest { DatawindowHandle = "no_such_dw" },
            stream,
            fixture.Context));

        Assert.Equal(expected, failure.StatusCode);

        // The payload reached the caller before the status did, on every row.
        RetrieveChunk terminal = Assert.Single(stream.Written);
        Assert.True(terminal.Final);
        Assert.Equal(1L, terminal.Error.Sqldbcode);

        // And the numeric outcome is on the status, which is how the specific legacy code survives a
        // grouping that maps several codes onto one status.
        Assert.Contains(
            ((long)outcome).ToString(CultureInfo.InvariantCulture),
            failure.Status.Detail,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A non-failing terminal status still ends the stream with the final marker.
    /// </summary>
    /// <remarks>
    /// THE TRI-STATE ALGEBRA REACHES HERE TOO (C-B). PREVENT = 1 satisfies the legacy success predicate, so
    /// a prevented retrieval IS a success and must complete normally; CANCELLED = -2 is excluded from
    /// failure by name and is likewise not an error. A "not zero means failure" test would turn both into
    /// call failures and change observable behaviour.
    /// </remarks>
    [Theory]
    [InlineData(WireRetCode.Ok)]
    [InlineData(WireRetCode.Prevent)]
    [InlineData(WireRetCode.Cancelled)]
    public async Task ANonFailingTerminalStatusStillEndsTheStreamNormally(WireRetCode outcome)
    {
        C03Fixture fixture = new();
        fixture.Persistence.QueryScript.Add(new PersistenceQueryResponse
        {
            Status = new PersistenceOperationStatus { RetCode = outcome },
        });

        C03StreamWriter<RetrieveChunk> stream = new();

        await fixture.Service.Retrieve(
            new RetrieveRequest { DatawindowHandle = "dw-1" },
            stream,
            fixture.Context);

        RetrieveChunk terminal = Assert.Single(stream.Written);

        Assert.True(terminal.Final);
        Assert.Equal(0L, terminal.RowCount);
    }

    /// <summary>
    /// An update opens a session, creates a task, binds the DataWindow to it, runs, then releases both.
    /// </summary>
    /// <remarks>
    /// THE BIND STEP IS NOT OPTIONAL. C-06 applies the data object only from the prepare message, so an
    /// update run without one submits rows against a task that does not know what it is updating. The
    /// multi-table switch stays OFF and the descriptor array stays EMPTY, which is the legacy single-table
    /// path where the carrier's own static definition governs
    /// [<c>n_cst_thread_task_sqlupdate.sru:L365 versus :L371</c>].
    /// </remarks>
    [Fact]
    public async Task UpdateRunsTheFullUpstreamLifecycleInOrderAndBindsTheDataObject()
    {
        C03Fixture fixture = new();

        WireUpdateRequest request = new() { DatawindowHandle = "dw-1" };
        request.Rows.Add(new DataWindowRow { Buffer = DwBuffer.Primary, Row = 1L });

        WireUpdateResponse response = await fixture.Service.Update(request, fixture.Context);

        Assert.Equal(WireRetCode.Ok, response.RetCode);

        Assert.Equal(
            [
                nameof(PersistenceClient.BeginSessionAsync),
                nameof(PersistenceClient.CreateUpdateTaskAsync),
                nameof(PersistenceClient.PrepareUpdateAsync),
                nameof(PersistenceClient.UpdateAsync),
                nameof(PersistenceClient.ReleaseUpdateTaskAsync),
                nameof(PersistenceClient.EndSessionAsync),
            ],
            fixture.Persistence.Lifecycle);

        Assert.Equal(PersistenceStubTaskIds.Update, fixture.Persistence.LastUpdate!.Task.TaskId);

        PersistencePrepareUpdateRequest bind = fixture.Persistence.LastPrepareUpdate!;

        Assert.Equal("dw-1", bind.DataObject);
        Assert.False(bind.MultiTableUpdate);
        Assert.Empty(bind.Tables);
    }

    /// <summary>
    /// A bind failure is a projected failure and submits no rows.
    /// </summary>
    /// <remarks>
    /// ZERO COUNTS WITH AN OK OUTCOME WOULD BE A WRONG ANSWER ABOUT DATA - it tells the caller its rows were
    /// considered and none needed applying. The upstream outcome is relayed instead so the specific legacy
    /// code survives, and the task created before the bind failed is still released.
    /// </remarks>
    [Fact]
    public async Task UpdateProjectsABindFailureAsAFailureSubmitsNoRowsAndStillReleasesTheTask()
    {
        C03Fixture fixture = new();
        fixture.Persistence.PrepareOutcome = WireRetCode.EInvalidArgument;

        WireUpdateRequest request = new() { DatawindowHandle = "dw-1" };
        request.Rows.Add(new DataWindowRow { Buffer = DwBuffer.Primary, Row = 1L });

        WireUpdateResponse response = await fixture.Service.Update(request, fixture.Context);

        Assert.Equal(WireRetCode.EInvalidArgument, response.RetCode);
        Assert.Equal(0, fixture.Persistence.UpdateCalls);
        Assert.Equal(0L, response.RowsInserted);

        Assert.Equal(
            [
                nameof(PersistenceClient.BeginSessionAsync),
                nameof(PersistenceClient.CreateUpdateTaskAsync),
                nameof(PersistenceClient.PrepareUpdateAsync),
                nameof(PersistenceClient.ReleaseUpdateTaskAsync),
                nameof(PersistenceClient.EndSessionAsync),
            ],
            fixture.Persistence.Lifecycle);
    }

    /// <summary>
    /// A session failure on an update is a projected failure and never an empty success.
    /// </summary>
    [Fact]
    public async Task UpdateProjectsASessionFailureAsTheUpstreamOutcomeAndNeverAnEmptySuccess()
    {
        C03Fixture fixture = new();
        fixture.Persistence.BeginSessionOutcome = WireRetCode.EDbError;

        WireUpdateRequest request = new() { DatawindowHandle = "dw-1" };
        request.Rows.Add(new DataWindowRow { Buffer = DwBuffer.Primary, Row = 1L });

        WireUpdateResponse response = await fixture.Service.Update(request, fixture.Context);

        Assert.Equal(WireRetCode.EDbError, response.RetCode);
        Assert.Equal(0, fixture.Persistence.UpdateCalls);
        Assert.Equal([nameof(PersistenceClient.BeginSessionAsync)], fixture.Persistence.Lifecycle);
    }

    /// <summary>
    /// A conflict releases the task and ends the session on its way out.
    /// </summary>
    /// <remarks>
    /// A CONFLICT IS THE MOST LIKELY FAILURE ON THIS OPERATION, so it is the one that must not leak. It
    /// propagates through both <c>finally</c> blocks and reaches the caller as <c>Aborted</c> unchanged -
    /// surfaced, never retried, with no silent overwrite.
    /// </remarks>
    [Fact]
    public async Task UpdateReleasesTheTaskAndEndsTheSessionWhenAConflictIsRaised()
    {
        C03Fixture fixture = new();
        fixture.Persistence.Conflict = new PersistenceConflictException(
            new ConflictDetail { UpdateTable = "COMPANY" },
            RetCode.E_DB_ERROR,
            new RpcException(new Status(StatusCode.Aborted, "upstream")));

        WireUpdateRequest request = new() { DatawindowHandle = "dw-1" };
        request.Rows.Add(new DataWindowRow { Buffer = DwBuffer.Primary, Row = 1L });

        RpcException failure = await Assert.ThrowsAsync<RpcException>(
            () => fixture.Service.Update(request, fixture.Context));

        Assert.Equal(StatusCode.Aborted, failure.StatusCode);

        Assert.Equal(
            [
                nameof(PersistenceClient.ReleaseUpdateTaskAsync),
                nameof(PersistenceClient.EndSessionAsync),
            ],
            fixture.Persistence.Lifecycle[^2..]);
    }

    /// <summary>
    /// The carrier is ALWAYS three segments in canonical order, and row order inside each is the caller's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS CASE PREVIOUSLY ASSERTED THE DEFECT. It expected TWO segments in first-appearance order, which
    /// is what the implementation produced and what <c>persistence.v1.CarrierState</c> does not accept:
    /// the receiving codec tests the segment COUNT against the canonical length and then requires segment
    /// n to be buffer n [<c>Buffers/ChangesetCodec.cs</c>], so the payload this case was pinning would have
    /// been rejected outright by the service it was addressed to. The expectation is corrected to the
    /// contract rather than the implementation preserved to keep the expectation.
    /// </para>
    /// <para>
    /// WHAT IS GENUINELY PRESERVED IS ROW ORDER WITHIN A SEGMENT, and that half of the original assertion
    /// is kept and strengthened. The Filter buffer's rows arrive inverted relative to the source
    /// [<c>n_cst_thread_task_sqlupdate.sru:L235</c>] and the identity round trip reads that buffer
    /// backwards because of it, so re-sorting here would pair identity values with the wrong rows - a
    /// defect that returns the right COUNT of identities and survives every row-count assertion.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task UpdateEmitsAllThreeCanonicalSegmentsAndPreservesRowOrderWithinEach()
    {
        C03Fixture fixture = new();

        WireUpdateRequest request = new() { DatawindowHandle = "dw-1" };
        request.Rows.Add(new DataWindowRow { Buffer = DwBuffer.Primary, Row = 1L });
        request.Rows.Add(new DataWindowRow { Buffer = DwBuffer.Filter, Row = 9L });
        request.Rows.Add(new DataWindowRow { Buffer = DwBuffer.Primary, Row = 2L });

        WireUpdateResponse response = await fixture.Service.Update(request, fixture.Context);

        Assert.Equal(WireRetCode.Ok, response.RetCode);
        Assert.Equal(3L, response.RowsInserted);

        PersistenceCarrierState carrier = fixture.Persistence.LastUpdate!.UpdateData;

        // POSITIONAL, ALWAYS THREE: Primary, Delete, Filter - even though this request never mentioned the
        // Delete buffer at all. Asserting the buffer tag at each index rather than merely the count is what
        // catches a reordering, which a count alone would not.
        Assert.Equal(3, carrier.Segments.Count);
        Assert.Equal(DwBuffer.Primary, carrier.Segments[0].Buffer);
        Assert.Equal(DwBuffer.Delete, carrier.Segments[1].Buffer);
        Assert.Equal(DwBuffer.Filter, carrier.Segments[2].Buffer);

        // Row order inside each segment is the caller's, untouched.
        Assert.Equal([1L, 2L], carrier.Segments[0].Rows.Select(row => row.Row));
        Assert.Empty(carrier.Segments[1].Rows);
        Assert.Equal([9L], carrier.Segments[2].Rows.Select(row => row.Row));

        Assert.Equal(3L, fixture.Persistence.LastUpdate.UpdateRows);

        // The identity block is relayed element for element.
        IdentityColumnData identity = Assert.Single(response.Identity);
        Assert.Equal(1L, identity.IdentityColumnId);
    }

    /// <summary>
    /// A request touching no buffer at all still sends the full three-segment shape.
    /// </summary>
    /// <remarks>
    /// The degenerate case is the one a first-appearance grouping got most wrong: it produced ZERO segments,
    /// which the receiving codec rejects on the count test before it looks at anything else. The shape is a
    /// property of the encoding and not of the data.
    /// </remarks>
    [Fact]
    public async Task UpdateWithNoRowsStillSendsThreeEmptySegments()
    {
        C03Fixture fixture = new();

        WireUpdateResponse response = await fixture.Service.Update(
            new WireUpdateRequest { DatawindowHandle = "dw-1" },
            fixture.Context);

        Assert.Equal(WireRetCode.Ok, response.RetCode);

        PersistenceCarrierState carrier = fixture.Persistence.LastUpdate!.UpdateData;

        Assert.Equal(3, carrier.Segments.Count);
        Assert.All(carrier.Segments, segment => Assert.Empty(segment.Rows));
        Assert.Equal(
            [DwBuffer.Primary, DwBuffer.Delete, DwBuffer.Filter],
            carrier.Segments.Select(segment => segment.Buffer));
        Assert.Equal(0L, fixture.Persistence.LastUpdate.UpdateRows);
    }

    /// <summary>
    /// A row naming a buffer outside the canonical three is REFUSED, not silently dropped.
    /// </summary>
    /// <remarks>
    /// With a fixed three-segment shape there is nowhere to put such a row, and the two available failure
    /// modes are not equally bad: omitting it would submit an UPDATE missing a row the caller asked to
    /// apply and then report success, which is data loss reported as a success. The upstream must not be
    /// reached at all, which is the second assertion here and the more important one.
    /// </remarks>
    [Fact]
    public async Task UpdateRefusesARowNamingAnUndeclaredBuffer()
    {
        C03Fixture fixture = new();

        WireUpdateRequest request = new() { DatawindowHandle = "dw-1" };
        request.Rows.Add(new DataWindowRow { Buffer = DwBuffer.Primary, Row = 1L });
        request.Rows.Add(new DataWindowRow { Buffer = (DwBuffer)9999, Row = 2L });

        WireUpdateResponse response = await fixture.Service.Update(request, fixture.Context);

        Assert.Equal(WireRetCode.EInvalidArgument, response.RetCode);

        // NOTHING WAS SENT. A partial carrier reaching Persistence is the outcome this refusal exists to
        // prevent, so the absence of the call is the assertion that matters.
        Assert.Equal(0, fixture.Persistence.UpdateCalls);
    }

    [Fact]
    public async Task AnUpdateOpensASessionCreatesATaskPreparesItAndReleasesBothInOrder()
    {
        C03Fixture fixture = new();

        WireUpdateRequest request = new() { DatawindowHandle = "dw-1" };
        request.Rows.Add(new DataWindowRow { Buffer = DwBuffer.Primary, Row = 1L });

        _ = await fixture.Service.Update(request, fixture.Context);

        // An UpdateRequest carries a TaskHandle as its FIRST field, so an update sent without one is
        // answered E_INVALID_HANDLE before it can touch a row.
        Assert.NotNull(fixture.Persistence.LastUpdate?.Task);
        Assert.Equal(PersistenceStubTaskIds.Update, fixture.Persistence.LastUpdate.Task.TaskId);

        // The session was opened once.
        _ = Assert.Single(fixture.Persistence.TransactionStub.BeginRequests);

        // PREPARE RAN, AND IT NAMES THE DATA OBJECT. C-06 refuses an update naming neither a data object
        // nor a syntax string with E_INVALID_DATAOBJECT [n_cst_thread_task_sqlupdate.sru:L326-L330], so
        // this call is required even when there is no descriptor to send with it.
        PersistencePrepareUpdateRequest prepared =
            Assert.Single(fixture.Persistence.UpdateStub.PrepareRequests);

        Assert.Equal("dw-1", prepared.DataObject);
        Assert.Equal(PersistenceStubTaskIds.Update, prepared.Task.TaskId);

        // BOTH acquisitions released.
        Assert.Contains(
            nameof(FakeUpdateServiceClient.ReleaseUpdateTaskAsync),
            fixture.Persistence.UpdateStub.Calls);
        Assert.Contains(
            nameof(FakeTransactionServiceClient.EndSessionAsync),
            fixture.Persistence.TransactionStub.Calls);
    }

    [Fact]
    public async Task TheUpdateDescriptorIsDerivedFromTheDefinitionAndMultiTableStaysOff()
    {
        DataWindowCatalogue catalogue = new();

        C03Fixture fixture = new(
            updateContracts: new HeadlessDataWindowUpdateContractProvider(catalogue));

        WireUpdateRequest request = new()
        {
            DatawindowHandle = DataWindowCatalogue.SqliteFixtureName,
        };

        request.Rows.Add(new DataWindowRow { Buffer = DwBuffer.Primary, Row = 1L });

        _ = await fixture.Service.Update(request, fixture.Context);

        PersistencePrepareUpdateRequest prepared =
            Assert.Single(fixture.Persistence.UpdateStub.PrepareRequests);

        // OFF, unconditionally: C-03 publishes no way to request a multi-table update, and one definition
        // declares one update table. Turning it on would apply a single-table descriptor under multi-table
        // rules and change the generated statement for every existing caller.
        Assert.False(prepared.MultiTableUpdate);

        TableUpdateContract descriptor = Assert.Single(prepared.Tables);

        // Transcribed from dw_sqlite.srd:L14 - the UPDATE half of the definition, which the retrieve half
        // alone does not carry.
        Assert.Equal("COMPANY", descriptor.Name);

        // DECLARATION ORDER, NEVER SORTED: C-06 walks the array [:L111-L114].
        Assert.Equal(
            ["id", "name", "age", "address", "salary", "birth"],
            descriptor.Updatablecolumns);

        // id is the ONLY column marked key AND identity [dw_sqlite.srd:L8].
        Assert.Equal(["id"], descriptor.Keycolumns);
        Assert.Equal("id", descriptor.Identitycolumn);

        // MODE 1 - "key and updateable columns" - carried as a long, not flattened to a flag. With all six
        // columns marked updatewhereclause=yes the concurrency check spans all six original values.
        Assert.True(descriptor.HasUpdatewhere);
        Assert.Equal(1L, descriptor.Updatewhere);

        // FALSE, and present rather than defaulted: an absent field means "leave the carrier's setting
        // alone", so writing nothing here would silently discard the definition's own value.
        Assert.True(descriptor.HasUpdatekeyinplace);
        Assert.False(descriptor.Updatekeyinplace);
    }

    [Fact]
    public async Task AnUpdateWithoutADescriptorProviderStillNamesItsDataObject()
    {
        C03Fixture fixture = new();

        WireUpdateRequest request = new() { DatawindowHandle = "dw-1" };
        request.Rows.Add(new DataWindowRow { Buffer = DwBuffer.Primary, Row = 1L });

        _ = await fixture.Service.Update(request, fixture.Context);

        PersistencePrepareUpdateRequest prepared =
            Assert.Single(fixture.Persistence.UpdateStub.PrepareRequests);

        // NO DESCRIPTOR IS ORDINARY, NOT A FAULT. With multi-table off C-06 does not apply the array at all
        // [:L365 against :L371] and the carrier's own static definition governs - the legacy single-table
        // path exactly. What must still travel is the data object.
        Assert.Empty(prepared.Tables);
        Assert.Equal("dw-1", prepared.DataObject);
    }

    [Fact]
    public void AHandleDeclaringNoUpdateTableYieldsNoDescriptor()
    {
        DataWindowCatalogue catalogue = new();

        // A DERIVED DataWindow: columns but no update table, which is what a read-only definition looks
        // like. The provider answers nothing rather than fabricating a table name.
        DataWindowDefinition derived = new("dw-derived");
        _ = derived.AddColumn("id", "number");

        catalogue.Register(derived);

        HeadlessDataWindowUpdateContractProvider provider = new(catalogue);

        Assert.Null(provider.GetUpdateContract("dw-derived"));
        Assert.Null(provider.GetUpdateContract("no-such-dw"));

        // AND THE EVIDENCED DEFINITION STILL YIELDS ONE, with the six-column concurrency contract the
        // oracle declares [dw_sqlite.srd:L8-L14] - the positive that makes the two negatives meaningful.
        DataWindowUpdateContract evidenced = Assert.IsType<DataWindowUpdateContract>(
            provider.GetUpdateContract(DataWindowCatalogue.SqliteFixtureName));

        Assert.Equal("COMPANY", evidenced.TableName);
        Assert.Equal(1L, evidenced.UpdateWhere);
        Assert.False(evidenced.UpdateKeyInPlace);
        Assert.Equal("id", evidenced.IdentityColumn);
        Assert.Equal(["id"], evidenced.KeyColumns);
        Assert.Equal(
            ["id", "name", "age", "address", "salary", "birth"],
            evidenced.UpdatableColumns);
    }

    [Fact]
    public async Task AConflictStillReleasesItsTaskAndItsSession()
    {
        C03Fixture fixture = new();

        fixture.Persistence.Conflict = new PersistenceConflictException(
            new ConflictDetail { UpdateTable = "COMPANY" },
            RetCode.E_DB_ERROR,
            new RpcException(new Status(StatusCode.Aborted, "upstream")));

        WireUpdateRequest request = new() { DatawindowHandle = "dw-1" };
        request.Rows.Add(new DataWindowRow { Buffer = DwBuffer.Primary, Row = 1L });

        _ = await Assert.ThrowsAsync<RpcException>(async () =>
            await fixture.Service.Update(request, fixture.Context));

        // A conflict is the ONE failure a caller is most likely to react to by retrying, so a task leaked
        // on this path leaks once per retry. The catch therefore sits OUTSIDE the task's finally.
        Assert.Contains(
            nameof(FakeUpdateServiceClient.ReleaseUpdateTaskAsync),
            fixture.Persistence.UpdateStub.Calls);
        Assert.Contains(
            nameof(FakeTransactionServiceClient.EndSessionAsync),
            fixture.Persistence.TransactionStub.Calls);

        // STILL EXACTLY ONE ATTEMPT. Releasing is not retrying.
        Assert.Equal(1, fixture.Persistence.UpdateCalls);
    }

    [Fact]
    public async Task UpdateRefusesABlankHandleAndAnUnknownSession()
    {
        C03Fixture fixture = new();

        Assert.Equal(
            WireRetCode.EInvalidArgument,
            (await fixture.Service.Update(new WireUpdateRequest(), fixture.Context)).RetCode);

        Assert.Equal(
            WireRetCode.EInvalidHandle,
            (await fixture.Service.Update(
                new WireUpdateRequest { DatawindowHandle = "dw-1", SessionId = "no-such-session" },
                fixture.Context)).RetCode);

        // Refused before the upstream was reached.
        Assert.Equal(0, fixture.Persistence.UpdateCalls);
    }

    [Fact]
    public async Task AllEightHeadlessOperationsRefuseAnUnknownHandle()
    {
        C03Fixture fixture = new(new C03EmptyModelSetProvider());
        const string Missing = "no-such-dw";

        Assert.Equal(
            WireRetCode.EInvalidHandle,
            (await fixture.Service.GetDropDownSearchState(
                new GetDropDownSearchStateRequest { DatawindowHandle = Missing },
                fixture.Context)).RetCode);
        Assert.Equal(
            WireRetCode.EInvalidHandle,
            (await fixture.Service.ApplyDropDownSearch(
                new ApplyDropDownSearchRequest { DatawindowHandle = Missing },
                fixture.Context)).RetCode);
        Assert.Equal(
            WireRetCode.EInvalidHandle,
            (await fixture.Service.GetColumnSortState(
                new GetColumnSortStateRequest { DatawindowHandle = Missing },
                fixture.Context)).RetCode);
        Assert.Equal(
            WireRetCode.EInvalidHandle,
            (await fixture.Service.ApplyColumnSort(
                new ApplyColumnSortRequest { DatawindowHandle = Missing },
                fixture.Context)).RetCode);
        Assert.Equal(
            WireRetCode.EInvalidHandle,
            (await fixture.Service.GetContextMenuModel(
                new GetContextMenuModelRequest { DatawindowHandle = Missing },
                fixture.Context)).RetCode);
        Assert.Equal(
            WireRetCode.EInvalidHandle,
            (await fixture.Service.ApplyContextMenuModel(
                new ApplyContextMenuModelRequest { DatawindowHandle = Missing },
                fixture.Context)).RetCode);
        Assert.Equal(
            WireRetCode.EInvalidHandle,
            (await fixture.Service.GetRowSelectState(
                new GetRowSelectStateRequest { DatawindowHandle = Missing },
                fixture.Context)).RetCode);
        Assert.Equal(
            WireRetCode.EInvalidHandle,
            (await fixture.Service.ApplyRowSelectStyle(
                new ApplyRowSelectStyleRequest { DatawindowHandle = Missing },
                fixture.Context)).RetCode);
    }

    [Fact]
    public async Task TheContextMenuItemModelReadsAndAppliesAgainstARetainedSet()
    {
        C03Fixture fixture = new();

        GetContextMenuModelResponse menu = await fixture.Service.GetContextMenuModel(
            new GetContextMenuModelRequest { DatawindowHandle = "dw-1", Row = 1L },
            fixture.Context);

        Assert.Equal(WireRetCode.Ok, menu.RetCode);
        Assert.Equal(1L, menu.Model.Row);

        ApplyContextMenuModelRequest apply = new()
        {
            DatawindowHandle = "dw-1",
            Replace = true,
            Model = new WireContextMenuModel { Row = 1L },
        };

        apply.Model.Items.Add(new ContextMenuItem { Text = "One", Id = 101UL, Enabled = true });
        apply.Model.Items.Add(new ContextMenuItem { Separator = true });
        apply.Model.Items.Add(new ContextMenuItem { Text = "Two", Id = 102UL, Enabled = true });

        ApplyContextMenuModelResponse applied = await fixture.Service.ApplyContextMenuModel(
            apply,
            fixture.Context);

        Assert.Equal(WireRetCode.Ok, applied.RetCode);
        Assert.Equal(3, applied.Model.Items.Count);
        Assert.Equal("One", applied.Model.Items[0].Text);
        Assert.False(applied.Model.Items[0].Separator);

        // n_cst_dwsvc_contextmenu.sru:L511 - the marker IS the label, and it travels WITH the flag.
        Assert.True(applied.Model.Items[1].Separator);
        Assert.Equal(DomainContextMenuModel.SeparatorText, applied.Model.Items[1].Text);
        Assert.Equal(0UL, applied.Model.Items[1].Id);

        Assert.Equal("Two", applied.Model.Items[2].Text);

        // RETAINED: a second read sees what apply wrote, so the set is per-DataWindow state.
        GetContextMenuModelResponse reread = await fixture.Service.GetContextMenuModel(
            new GetContextMenuModelRequest { DatawindowHandle = "dw-1", Row = 1L },
            fixture.Context);

        Assert.Equal(3, reread.Model.Items.Count);

        // The contract's own defined "not computed" value: font measurement is the deferred half (C-D).
        Assert.Equal(0L, reread.Model.Items[0].LogicalTextWidth);
    }

    [Fact]
    public async Task AnUnsuppliedContextMenuToggleIsNotWritten()
    {
        C03Fixture fixture = new();

        // Every one of the five defaults TRUE at source, which is why they are optional on the wire.
        ApplyContextMenuModelResponse applied = await fixture.Service.ApplyContextMenuModel(
            new ApplyContextMenuModelRequest
            {
                DatawindowHandle = "dw-1",
                Model = new WireContextMenuModel { ColCheck = false },
            },
            fixture.Context);

        Assert.Equal(WireRetCode.Ok, applied.RetCode);
        Assert.False(applied.Model.ColCheck);
        Assert.True(applied.Model.ColCopy);
        Assert.True(applied.Model.ColAutoWidth);
    }

    [Fact]
    public async Task ColumnSortPreservesRequestOrderAsSortOrder()
    {
        C03Fixture fixture = new();

        ApplyColumnSortRequest request = new() { DatawindowHandle = "dw-1", ExpressionOnly = true };

        request.Columns.Add(new ColumnSortState.Types.ColumnSort
        {
            ColumnName = "age",
            Direction = ColumnSortState.Types.Direction.SortAsc,
        });
        request.Columns.Add(new ColumnSortState.Types.ColumnSort
        {
            ColumnName = "salary",
            Direction = ColumnSortState.Types.Direction.SortDesc,
        });
        request.Columns.Add(new ColumnSortState.Types.ColumnSort
        {
            ColumnName = "ignored",
            Direction = ColumnSortState.Types.Direction.SortNone,
        });

        ApplyColumnSortResponse response = await fixture.Service.ApplyColumnSort(request, fixture.Context);

        Assert.Equal(WireRetCode.Ok, response.RetCode);

        int age = response.State.SortExpression.IndexOf("age", StringComparison.Ordinal);
        int salary = response.State.SortExpression.IndexOf("salary", StringComparison.Ordinal);

        Assert.True(age >= 0, response.State.SortExpression);
        Assert.True(salary > age, response.State.SortExpression);

        // SORT_NONE names no direction, so it contributes nothing rather than defaulting to ascending.
        Assert.DoesNotContain("ignored", response.State.SortExpression, StringComparison.Ordinal);

        // 🔴 AND THE WHOLE STRING, not just the ordering. The two IndexOf assertions above are what let a
        // real defect through: the projection joined its clauses with the EMPTY STRING, so this published
        // "age Asalary D" - which satisfies "salary appears after age" perfectly while being an expression
        // no DataWindow can parse. Asserting relative position tells you the order is right and says
        // nothing about whether the result is well formed.
        Assert.Equal("age A,salary D", response.State.SortExpression);
    }

    /// <summary>
    /// The composed expression is joined with the ORACLE'S COMMA, at one, two and three columns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE DEFECT THIS PINS. <c>ApplyColumnSort</c> composes each clause through the model - so every
    /// individual clause was always the oracle's - and then joined them with <c>string.Empty</c>. One
    /// column therefore looked perfect and every multi-column sort was run together into a single
    /// unparseable token: <c>"age D,salary A"</c> was published as <c>"age Dsalary A"</c>. The model's own
    /// parity matrix could not catch it, because the model composes its multi-column expression itself
    /// [<c>ColumnSortModel.Sort</c>, oracle <c>:L195-L198</c>] and does so correctly; the fault was in the
    /// wire projection, which no test covered.
    /// </para>
    /// <para>
    /// ONE COLUMN IS INCLUDED DELIBERATELY as the control that fails no differently either way, so the
    /// two- and three-column rows are demonstrably the ones carrying the assertion.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("age D", "age:D")]
    [InlineData("age D,salary A", "age:D|salary:A")]
    [InlineData("age A,salary A,name D", "age:A|salary:A|name:D")]
    public async Task TheComposedSortExpressionIsJoinedWithTheOraclesComma(string expected, string columns)
    {
        C03Fixture fixture = new();

        ApplyColumnSortRequest request = new() { DatawindowHandle = "dw-1", ExpressionOnly = true };

        foreach (string column in columns.Split('|'))
        {
            string[] parts = column.Split(':');

            request.Columns.Add(new ColumnSortState.Types.ColumnSort
            {
                ColumnName = parts[0],
                Direction = string.Equals(parts[1], "A", StringComparison.Ordinal)
                    ? ColumnSortState.Types.Direction.SortAsc
                    : ColumnSortState.Types.Direction.SortDesc,
            });
        }

        ApplyColumnSortResponse response = await fixture.Service.ApplyColumnSort(request, fixture.Context);

        Assert.Equal(WireRetCode.Ok, response.RetCode);
        Assert.Equal(expected, response.State.SortExpression);
    }

    /// <summary>
    /// The projection joins with the model's OWN separator, so the two cannot drift apart again.
    /// </summary>
    /// <remarks>
    /// The root cause was two definitions of one thing: the model held <c>","</c> and the projection held
    /// <c>string.Empty</c>. This asserts the published expression against the model's constant rather than
    /// against a third literal written here, so a change to the separator moves this test with it instead
    /// of breaking it.
    /// </remarks>
    [Fact]
    public async Task TheProjectionUsesTheModelsOwnSeparatorRatherThanALiteralOfItsOwn()
    {
        C03Fixture fixture = new();

        ApplyColumnSortRequest request = new() { DatawindowHandle = "dw-1", ExpressionOnly = true };

        foreach ((string column, ColumnSortState.Types.Direction direction) in
            new[]
            {
                ("age", ColumnSortState.Types.Direction.SortDesc),
                ("salary", ColumnSortState.Types.Direction.SortAsc),
            })
        {
            request.Columns.Add(new ColumnSortState.Types.ColumnSort
            {
                ColumnName = column,
                Direction = direction,
            });
        }

        ApplyColumnSortResponse response = await fixture.Service.ApplyColumnSort(request, fixture.Context);

        Assert.Equal(
            "age D" + ColumnSortModel.ClauseSeparator + "salary A",
            response.State.SortExpression);

        // And the separator really is the oracle's comma, asserted once here so the row above cannot pass
        // against a separator that has been changed to something else on both sides at once.
        Assert.Equal(",", ColumnSortModel.ClauseSeparator);
    }

    [Fact]
    public async Task ExpressionOnlyDoesNotMutateTheModel()
    {
        C03Fixture fixture = new();

        ApplyColumnSortRequest request = new() { DatawindowHandle = "dw-1", ExpressionOnly = true };
        request.Columns.Add(new ColumnSortState.Types.ColumnSort
        {
            ColumnName = "age",
            Direction = ColumnSortState.Types.Direction.SortAsc,
        });

        ApplyColumnSortResponse applied = await fixture.Service.ApplyColumnSort(request, fixture.Context);

        Assert.NotEmpty(applied.State.SortExpression);

        GetColumnSortStateResponse read = await fixture.Service.GetColumnSortState(
            new GetColumnSortStateRequest { DatawindowHandle = "dw-1" },
            fixture.Context);

        Assert.Equal(WireRetCode.Ok, read.RetCode);
        Assert.Empty(read.State.SortExpression);
    }

    [Fact]
    public async Task RowSelectRejectsStyleZeroThroughTheModelsOwnGuard()
    {
        C03Fixture fixture = new();

        // n_cst_dwsvc_rowselect.sru:L169 - and the model's idempotent early-out at :L168 comes FIRST,
        // which is the preserved defect that makes this reachable only from a non-zero current style.
        ApplyRowSelectStyleResponse zero = await fixture.Service.ApplyRowSelectStyle(
            new ApplyRowSelectStyleRequest { DatawindowHandle = "dw-1", Style = 0UL },
            fixture.Context);

        Assert.Equal(WireRetCode.EInvalidArgument, zero.RetCode);

        ApplyRowSelectStyleResponse single = await fixture.Service.ApplyRowSelectStyle(
            new ApplyRowSelectStyleRequest { DatawindowHandle = "dw-1", Style = 1UL },
            fixture.Context);

        Assert.Equal(WireRetCode.Ok, single.RetCode);

        GetRowSelectStateResponse read = await fixture.Service.GetRowSelectState(
            new GetRowSelectStateRequest { DatawindowHandle = "dw-1" },
            fixture.Context);

        Assert.Equal(WireRetCode.Ok, read.RetCode);
        Assert.Equal(1UL, read.State.Style);
    }

    [Fact]
    public async Task DropDownSearchAppliesTheSettersAndRefusesAPinyinOverride()
    {
        C03Fixture fixture = new();

        // AAP 0.6.5 - the single genuine parity risk. Refused, never silently served.
        ApplyDropDownSearchResponse refused = await fixture.Service.ApplyDropDownSearch(
            new ApplyDropDownSearchRequest
            {
                DatawindowHandle = "dw-1",
                ColumnName = "name",
                PinyinFlags = 99UL,
            },
            fixture.Context);

        Assert.Equal(WireRetCode.ENoSupport, refused.RetCode);
        Assert.NotNull(refused.Error);
        Assert.False(refused.Error.Localized);

        ApplyDropDownSearchResponse applied = await fixture.Service.ApplyDropDownSearch(
            new ApplyDropDownSearchRequest
            {
                DatawindowHandle = "dw-1",
                ColumnName = "name",
                FilterType = 1UL,
                Clear = true,
            },
            fixture.Context);

        Assert.Equal(WireRetCode.Ok, applied.RetCode);
        Assert.Equal(1UL, applied.State.FilterType);

        GetDropDownSearchStateResponse read = await fixture.Service.GetDropDownSearchState(
            new GetDropDownSearchStateRequest { DatawindowHandle = "dw-1" },
            fixture.Context);

        Assert.Equal(WireRetCode.Ok, read.RetCode);
        Assert.Equal(1UL, read.State.FilterType);
    }

    [Fact]
    public async Task AConfiguredPinyinFlagValueIsAccepted()
    {
        DataServicesOptions configured = new();
        C03Fixture fixture = new(configured: configured);

        ApplyDropDownSearchResponse accepted = await fixture.Service.ApplyDropDownSearch(
            new ApplyDropDownSearchRequest
            {
                DatawindowHandle = "dw-1",
                ColumnName = "name",
                PinyinFlags = (ulong)configured.DropDownSearch.PinyinMatchFlags,
                Clear = true,
            },
            fixture.Context);

        Assert.Equal(WireRetCode.Ok, accepted.RetCode);
    }

    [Fact]
    public async Task EventChainRefusesAMessageWithNoSessionAndAnUnknownSession()
    {
        C03Fixture fixture = new();

        RpcException blank = await Assert.ThrowsAsync<RpcException>(async () =>
            await fixture.Service.EventChain(
                new C03RequestStream(new EventChainRequest()),
                new C03StreamWriter<EventChainResponse>(),
                fixture.Context));

        Assert.Equal(StatusCode.InvalidArgument, blank.StatusCode);

        RpcException unknown = await Assert.ThrowsAsync<RpcException>(async () =>
            await fixture.Service.EventChain(
                new C03RequestStream(new EventChainRequest { SessionId = "nope" }),
                new C03StreamWriter<EventChainResponse>(),
                fixture.Context));

        Assert.Equal(StatusCode.FailedPrecondition, unknown.StatusCode);
    }

    [Fact]
    public async Task EventChainRefusesASessionSwitchMidStream()
    {
        C03Fixture fixture = new();

        OpenValidationSessionResponse opened = await fixture.Service.OpenValidationSession(
            new OpenValidationSessionRequest { DatawindowHandle = "dw-1" },
            fixture.Context);

        RpcException switched = await Assert.ThrowsAsync<RpcException>(async () =>
            await fixture.Service.EventChain(
                new C03RequestStream(
                    Notify(opened.SessionId, 1L),
                    Notify("another-session", 2L)),
                new C03StreamWriter<EventChainResponse>(),
                fixture.Context));

        Assert.Equal(StatusCode.InvalidArgument, switched.StatusCode);
    }

    [Fact]
    public async Task EventChainRefusesAMessageWithNoPayload()
    {
        C03Fixture fixture = new();

        OpenValidationSessionResponse opened = await fixture.Service.OpenValidationSession(
            new OpenValidationSessionRequest { DatawindowHandle = "dw-1" },
            fixture.Context);

        RpcException empty = await Assert.ThrowsAsync<RpcException>(async () =>
            await fixture.Service.EventChain(
                new C03RequestStream(new EventChainRequest { SessionId = opened.SessionId }),
                new C03StreamWriter<EventChainResponse>(),
                fixture.Context));

        Assert.Equal(StatusCode.InvalidArgument, empty.StatusCode);
    }

    [Fact]
    public async Task ASequencedNotificationIsDispatchedAndAnsweredOnTheStream()
    {
        C03Fixture fixture = new();

        OpenValidationSessionResponse opened = await fixture.Service.OpenValidationSession(
            new OpenValidationSessionRequest { DatawindowHandle = "dw-1" },
            fixture.Context);

        C03StreamWriter<EventChainResponse> writer = new();

        await fixture.Service.EventChain(
            new C03RequestStream(Notify(opened.SessionId, 1L)),
            writer,
            fixture.Context);

        EventChainResponse response = Assert.Single(writer.Written);

        Assert.Equal(EventChainResponse.PayloadOneofCase.Result, response.PayloadCase);
        Assert.Equal(opened.SessionId, response.SessionId);
        Assert.Equal(EventId.Ondwnsetfocus, response.Result.EventId);
        Assert.Equal(OrderingDiscipline.Sequenced, response.Token.Discipline);

        // ABSENT BY DESIGN: DataWindowEventChain sets State on only two of its 29 reported outcomes - the
        // item-change and validation-error paths, the two where the four cross-event fields are meaningful
        // [se_cst_dw.sru:L89-L96]. The projection respects that absence rather than synthesising a snapshot.
        Assert.Null(response.Result.State);
    }

    [Fact]
    public async Task AnOutOfOrderSynchronousArrivalFailsTheCallAndIsNeverReordered()
    {
        C03Fixture fixture = new();

        OpenValidationSessionResponse opened = await fixture.Service.OpenValidationSession(
            new OpenValidationSessionRequest { DatawindowHandle = "dw-1" },
            fixture.Context);

        C03StreamWriter<EventChainResponse> writer = new();

        RpcException violation = await Assert.ThrowsAsync<RpcException>(async () =>
            await fixture.Service.EventChain(
                new C03RequestStream(
                    ItemChange(opened.SessionId, 1L),
                    ItemChange(opened.SessionId, 3L)),
                writer,
                fixture.Context));

        Assert.Equal(StatusCode.FailedPrecondition, violation.StatusCode);

        // Exactly one result was written: the first. Nothing was buffered awaiting the missing 2.
        Assert.Single(writer.Written);
    }

    /// <summary>
    /// THE REAL CONSUMER, ON A PATTERN-(a) REVERSAL: the service refuses it and dispatches nothing for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ROW THE FINDING ASKED FOR. Every other assertion about pattern (a) in this suite is made
    /// against the sequencer in isolation, and the defect was precisely that the sequencer's answer reached
    /// no consumer: production dispatched in arrival order, and the only reordering in the estate was a
    /// sort inside a test. This drives the service's own <c>EventChain</c> and reads the RESULTS IT WROTE,
    /// so it fails against an implementation that admits a reversal however the sequencer is written.
    /// </para>
    /// <para>
    /// THE FIRST TOKEN IS A GAP AND THE SECOND IS A REVERSAL, in one stream, which is what makes the pair
    /// diagnostic rather than merely a refusal: the gap proves the rule is not contiguity - it is admitted
    /// and answered - and the reversal proves the rule is enforced. The reversal's token is chosen BELOW
    /// the mark the first dispatch leaves, and that mark is above 1 because the chain's outcome report and
    /// the result write each consume a token from the same counter.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task EventChainRefusesAReversalInsideTheSequencedGroupAndDispatchesNothingForIt()
    {
        C03Fixture fixture = new();

        OpenValidationSessionResponse opened = await fixture.Service.OpenValidationSession(
            new OpenValidationSessionRequest { DatawindowHandle = "dw-1" },
            fixture.Context);

        C03StreamWriter<EventChainResponse> writer = new();

        RpcException reversed = await Assert.ThrowsAsync<RpcException>(async () =>
            await fixture.Service.EventChain(
                new C03RequestStream(
                    // ADMITTED, though 7 is nowhere near the first token: a sequenced arrival need only be
                    // above the mark.
                    Notify(opened.SessionId, 7L),

                    // REFUSED: 6 is behind the mark that dispatch left, so its position has been passed.
                    Notify(opened.SessionId, 6L)),
                writer,
                fixture.Context));

        Assert.Equal(StatusCode.FailedPrecondition, reversed.StatusCode);

        // ONE RESULT, FOR THE ADMITTED MESSAGE ONLY. The reversal produced no dispatch and no answer -
        // neither executed out of order nor buffered in the hope of a predecessor that cannot come.
        EventChainResponse answered = Assert.Single(writer.Written);
        Assert.Equal(EventChainResponse.PayloadOneofCase.Result, answered.PayloadCase);
        Assert.Equal("7", answered.Result.CorrelationId);
    }

    /// <summary>
    /// THE REAL CONSUMER, ON AN ASCENDING PATTERN-(a) RUN WITH GAPS: every message is dispatched, in the
    /// order it was sent, and each is answered.
    /// </summary>
    /// <remarks>
    /// THE POSITIVE COUNTERPART, WITHOUT WHICH THE REFUSAL ABOVE WOULD BE CONSISTENT WITH A SERVICE THAT
    /// REFUSES EVERY SECOND MESSAGE. The tokens ascend and skip, which is exactly the shape a correct
    /// client produces on a shared counter, and each arrival's token is chosen above the mark its
    /// predecessor's dispatch left.
    /// </remarks>
    [Fact]
    public async Task EventChainDispatchesAnAscendingSequencedRunWithGaps()
    {
        C03Fixture fixture = new();

        OpenValidationSessionResponse opened = await fixture.Service.OpenValidationSession(
            new OpenValidationSessionRequest { DatawindowHandle = "dw-1" },
            fixture.Context);

        C03StreamWriter<EventChainResponse> writer = new();

        await fixture.Service.EventChain(
            new C03RequestStream(
                Notify(opened.SessionId, 10L),
                Notify(opened.SessionId, 20L),
                Notify(opened.SessionId, 30L)),
            writer,
            fixture.Context);

        // THREE DISPATCHES, IN SEND ORDER, each answered on its own correlation identifier.
        Assert.Equal(
            ["10", "20", "30"],
            writer.Written.Select(response => response.Result.CorrelationId));

        // AND EVERY OUTBOUND TOKEN IS ABOVE THE INBOUND ONE IT ANSWERS, which is the shared-counter
        // property a client relies on to compute its next token: one past the highest it has seen.
        Assert.Equal(
            [true, true, true],
            writer.Written
                .Zip((long[])[10L, 20L, 30L], (response, inbound) => response.Token.Sequence > inbound));
    }

    [Fact]
    public async Task AnUnmatchedAnswerTravelsAsAStructuredErrorAndTheStreamSurvives()
    {
        C03Fixture fixture = new();

        OpenValidationSessionResponse opened = await fixture.Service.OpenValidationSession(
            new OpenValidationSessionRequest { DatawindowHandle = "dw-1" },
            fixture.Context);

        C03StreamWriter<EventChainResponse> writer = new();

        await fixture.Service.EventChain(
            new C03RequestStream(
                new EventChainRequest
                {
                    SessionId = opened.SessionId,
                    Result = new EventResult { CorrelationId = "never-asked" },
                },
                // TOKEN 2, NOT 1, AND THAT IS THE ONE-COUNTER RULE RATHER THAN AN ARBITRARY CHOICE. The
                // refusal above is a real message on the stream and it carries token 1, so the ordering
                // mark stands at 1 by the time this notification is read and the token expected next is 2.
                // This row used to send 1 and passed only because the sequenced discipline accepted any
                // positive token - the same over-permissiveness that let production ignore the token
                // altogether. A client computes this value the way it computes it for the synchronous
                // group: one past the highest token it has seen on the stream, in either direction.
                Notify(opened.SessionId, 2L)),
            writer,
            fixture.Context);

        // SURVIVABLE: the error is reported ON the stream and the next notification still dispatches.
        Assert.Equal(2, writer.Written.Count);
        Assert.Equal(EventChainResponse.PayloadOneofCase.Error, writer.Written[0].PayloadCase);
        Assert.Equal(EventChainResponse.PayloadOneofCase.Result, writer.Written[1].PayloadCase);
        Assert.False(writer.Written[0].Error.Localized);
    }

    // -------------------------------------------------------------------------------------------------
    //  ALL 22 EVENT ARMS - se_cst_dw.sru:L11-L32
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Every one of the 22 events the oracle declares is dispatchable over the chain, and each answers on
    /// the stream. The roster and its order are <c>se_cst_dw.sru:L11-L32</c>: nine semantic and thirteen
    /// raw <c>pbm_dwn*</c>. FIVE OF THEM DECLARE NO RETURN TYPE AT SOURCE and therefore answer zero -
    /// <c>onddsgetfilter</c> [<c>:L13</c>], <c>onitemchanged</c> [<c>:L25</c>], <c>ondoitemchanged</c>
    /// [<c>:L26</c>], <c>onddsfiltered</c> [<c>:L28</c>] and <c>oncolumnexptrace</c> [<c>:L32</c>] - which
    /// is the ABSENCE of a return contract and not a success code.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryEvent))]
    public async Task EveryOneOfTheTwentyTwoEventsDispatchesAndAnswers(EventId eventId)
    {
        C03Fixture fixture = new();

        OpenValidationSessionResponse opened = await fixture.Service.OpenValidationSession(
            new OpenValidationSessionRequest { DatawindowHandle = "dw-1" },
            fixture.Context);

        C03StreamWriter<EventChainResponse> writer = new();

        await fixture.Service.EventChain(
            new C03RequestStream(Body(opened.SessionId, eventId)),
            writer,
            fixture.Context);

        EventChainResponse response = Assert.Single(writer.Written);

        Assert.Equal(EventChainResponse.PayloadOneofCase.Result, response.PayloadCase);
        Assert.Equal(eventId, response.Result.EventId);
        Assert.Equal(opened.SessionId, response.SessionId);

        // The discipline stamped outbound is the SERVER's ruling for this event, never the client's claim.
        Assert.Equal(
            DataWindowEventConversation.DisciplineOf(eventId),
            response.Token.Discipline);
    }

    /// <summary>The 22 events, in the oracle's own declaration order.</summary>
    public static TheoryData<EventId> EveryEvent() =>
    [
        EventId.Oninitcontextmenu,       // :L11  semantic
        EventId.Oncontextmenu,           // :L12  semantic
        EventId.Onddsgetfilter,          // :L13  semantic, NO RETURN TYPE, ref string out-parameter
        EventId.Oncolumnexpinvokemethod, // :L14  semantic, returns any over string args[]
        EventId.Ondwnrbuttondown,        // :L15  raw
        EventId.Ondwnrbuttonup,          // :L16  raw
        EventId.Ondwnrowchange,          // :L17  raw
        EventId.Ondwnrowchanging,        // :L18  raw
        EventId.Ondwnlbuttondblclk,      // :L19  raw
        EventId.Ondwnlbuttonclk,         // :L20  raw
        EventId.Ondwnchanging,           // :L21  raw
        EventId.Ondwnitemchangefocus,    // :L22  raw
        EventId.Ondwnitemchange,         // :L23  raw
        EventId.Ondoitemchange,          // :L24  semantic
        EventId.Onitemchanged,           // :L25  semantic, NO RETURN TYPE
        EventId.Ondoitemchanged,         // :L26  semantic, NO RETURN TYPE
        EventId.Ondwnitemvalidationerror,// :L27  raw
        EventId.Onddsfiltered,           // :L28  semantic, NO RETURN TYPE
        EventId.Ondwnkillfocus,          // :L29  raw
        EventId.Ondwnlbuttonup,          // :L30  raw
        EventId.Ondwnsetfocus,           // :L31  raw
        EventId.Oncolumnexptrace,        // :L32  semantic, NO RETURN TYPE
    ];

    [Fact]
    public void TheRosterUnderTestIsExactlyTheTwentyTwoTheOracleDeclares()
    {
        // EventId also carries an UNSPECIFIED member, which is not an event.
        Assert.Equal(23, Enum.GetValues<EventId>().Length);
        Assert.Equal(22, EveryEvent().Count);

        Assert.Equal(
            Enum.GetValues<EventId>().Where(id => id != EventId.Unspecified).Order(),
            EveryEvent().Select(row => row.Data).Order());
    }

    [Fact]
    public async Task AnEventNotificationWithNoBodyIsRefused()
    {
        C03Fixture fixture = new();

        OpenValidationSessionResponse opened = await fixture.Service.OpenValidationSession(
            new OpenValidationSessionRequest { DatawindowHandle = "dw-1" },
            fixture.Context);

        RpcException bodyless = await Assert.ThrowsAsync<RpcException>(async () =>
            await fixture.Service.EventChain(
                new C03RequestStream(new EventChainRequest
                {
                    SessionId = opened.SessionId,
                    Token = new SequencingToken { Sequence = 1L },
                    Notify = new EventNotification
                    {
                        CorrelationId = "1",
                        EventId = EventId.Ondwnsetfocus,
                    },
                }),
                new C03StreamWriter<EventChainResponse>(),
                fixture.Context));

        Assert.Equal(StatusCode.InvalidArgument, bodyless.StatusCode);
    }

    /// <summary>
    /// Builds a well-formed request for any of the 22 events. Every body that declares a <c>dwo</c> gets
    /// one, because the oracle's runtime always supplies it and the chain's parameter is non-nullable.
    /// </summary>
    private static EventChainRequest Body(string sessionId, EventId eventId)
    {
        DwObjectRef dwo = new()
        {
            Name = C03ChainFactory.Column,
            Id = 1L,
            ColType = "long",
        };

        EventNotification notify = new()
        {
            CorrelationId = eventId.ToString(),
            EventId = eventId,
            DatawindowHandle = "dw-1",
        };

        // ROWS AND COLUMN IDENTIFIERS ARE ONE-BASED on both sides, so row 1 is the first row.
        switch (eventId)
        {
            case EventId.Oninitcontextmenu:
                notify.InitContextMenu = new InitContextMenuEvent { Row = 1L, Dwo = dwo };
                break;
            case EventId.Oncontextmenu:
                notify.ContextMenu = new ContextMenuEvent { Row = 1L, Dwo = dwo, Mid = 1L };
                break;
            case EventId.Onddsgetfilter:
                notify.DdsGetFilter = new DdsGetFilterEvent { Row = 1L, Dwo = dwo, Data = "a" };
                break;
            case EventId.Oncolumnexpinvokemethod:
                notify.ColumnExpInvokeMethod = new ColumnExpInvokeMethodEvent
                {
                    Row = 1L,
                    Dwo = dwo,
                    Name = "Invoke",

                    // Wire element 0 IS legacy args[1] - the ONE place a basis changes on this boundary.
                    Args = { "first", "second" },
                };
                break;
            case EventId.Ondwnrbuttondown:
                notify.DwnRbuttonDown = new DwnRButtonDownEvent
                {
                    Xpos = 10,
                    Ypos = 20,
                    Row = 1L,
                    Dwo = dwo,
                };
                break;
            case EventId.Ondwnrbuttonup:
                notify.DwnRbuttonUp = new DwnRButtonUpEvent { Xpos = 10, Ypos = 20, Row = 1L, Dwo = dwo };
                break;
            case EventId.Ondwnrowchange:
                notify.DwnRowChange = new DwnRowChangeEvent { CurrentRow = 1L };
                break;
            case EventId.Ondwnrowchanging:
                notify.DwnRowChanging = new DwnRowChangingEvent { CurrentRow = 1L, NewRow = 2L };
                break;
            case EventId.Ondwnlbuttondblclk:
                notify.DwnLbuttonDblClk = new DwnLButtonDblClkEvent
                {
                    Xpos = 10,
                    Ypos = 20,
                    Row = 1L,
                    Dwo = dwo,
                };
                break;
            case EventId.Ondwnlbuttonclk:
                notify.DwnLbuttonClk = new DwnLButtonClkEvent { Xpos = 10, Ypos = 20, Row = 1L, Dwo = dwo };
                break;
            case EventId.Ondwnchanging:
                notify.DwnChanging = new DwnChangingEvent { Row = 1L, Dwo = dwo, Data = "a" };
                break;
            case EventId.Ondwnitemchangefocus:
                notify.DwnItemChangeFocus = new DwnItemChangeFocusEvent { Row = 1L, Dwo = dwo };
                break;
            case EventId.Ondwnitemchange:
                notify.DwnItemChange = new DwnItemChangeEvent { Row = 1L, Dwo = dwo, Data = "1" };
                break;
            case EventId.Ondoitemchange:
                notify.DoItemChange = new DoItemChangeEvent { Row = 1L, Dwo = dwo, Data = "1" };
                break;
            case EventId.Onitemchanged:
                notify.ItemChanged = new ItemChangedEvent { Row = 1L, Dwo = dwo };
                break;
            case EventId.Ondoitemchanged:
                notify.DoItemChanged = new DoItemChangedEvent { Row = 1L, Dwo = dwo };
                break;
            case EventId.Ondwnitemvalidationerror:
                notify.DwnItemValidationError = new DwnItemValidationErrorEvent
                {
                    Row = 1L,
                    Dwo = dwo,
                    Data = "bad",
                };
                break;
            case EventId.Onddsfiltered:
                notify.DdsFiltered = new DdsFilteredEvent
                {
                    Row = 1L,
                    Dwo = dwo,
                    RowCount = 4L,
                    FilteredCount = 1L,
                };
                break;
            case EventId.Ondwnkillfocus:
                notify.DwnKillFocus = new DwnKillFocusEvent();
                break;
            case EventId.Ondwnlbuttonup:
                notify.DwnLbuttonUp = new DwnLButtonUpEvent { Xpos = 10, Ypos = 20, Row = 1L, Dwo = dwo };
                break;
            case EventId.Ondwnsetfocus:
                notify.DwnSetFocus = new DwnSetFocusEvent();
                break;
            case EventId.Oncolumnexptrace:
                notify.ColumnExpTrace = new ColumnExpTraceEvent
                {
                    Row = 1L,
                    Dwo = dwo,
                    Stack = "a>b",
                    Expr = "1+1",
                    Value = "2",
                };
                break;
            case EventId.Unspecified:
            default:
                throw new ArgumentOutOfRangeException(nameof(eventId), eventId, "Not one of the 22.");
        }

        return new EventChainRequest
        {
            SessionId = sessionId,
            Token = new SequencingToken
            {
                Sequence = 1L,
                Discipline = DataWindowEventConversation.DisciplineOf(eventId),
            },
            Notify = notify,
        };
    }

    // -------------------------------------------------------------------------------------------------
    //  REGRESSION - the three defects this validation pass found
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// The premise the one-argument write rests on, asserted rather than assumed: the production ASP.NET
    /// Core server writer declares ONLY <c>WriteAsync(T)</c>. A call supplying a token therefore resolves
    /// to the Grpc.Core default interface method, which throws for any cancellable token.
    /// </summary>
    [Fact]
    public void TheProductionServerWriterDeclaresNoTokenOverload()
    {
        Type[] writers = [.. System.Reflection.Assembly
            .Load("Grpc.AspNetCore.Server")
            .GetTypes()
            .Where(candidate =>
                candidate.Name.StartsWith("HttpContextStreamWriter", StringComparison.Ordinal))];

        Assert.NotEmpty(writers);

        System.Reflection.MethodInfo[] declared = [.. writers.SelectMany(writer =>
            writer.GetMethods(
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.DeclaredOnly))];

        // At least one write member exists, and NONE of them takes a cancellation token.
        Assert.Contains(declared, method => method.Name == "WriteAsync");
        Assert.DoesNotContain(
            declared,
            method => method.Name == "WriteAsync"
                && method.GetParameters().Length == 2
                && method.GetParameters()[1].ParameterType == typeof(CancellationToken));
    }

    /// <summary>
    /// The ASP.NET Core writer implements only the single-argument WriteAsync, so passing a cancellable
    /// token routes to the Grpc.Core default interface method, which throws outright. Every write on this
    /// boundary must therefore use the one-argument form. This drives BOTH streaming methods under a
    /// CANCELLABLE token, which is what a production ServerCallContext always carries.
    /// </summary>
    [Fact]
    public async Task BothStreamingMethodsWriteUnderACancellableTokenWithoutThrowing()
    {
        C03Fixture fixture = new();

        Assert.True(fixture.Context.CancellationToken.CanBeCanceled);

        fixture.Persistence.QueryScript.Add(Chunk(1L, 1L, 2L));

        C03StreamWriter<RetrieveChunk> chunks = new();

        await fixture.Service.Retrieve(
            new RetrieveRequest { DatawindowHandle = "dw-1" },
            chunks,
            fixture.Context);

        Assert.Equal(2, chunks.Written.Count);

        OpenValidationSessionResponse opened = await fixture.Service.OpenValidationSession(
            new OpenValidationSessionRequest { DatawindowHandle = "dw-1" },
            fixture.Context);

        C03StreamWriter<EventChainResponse> events = new();

        await fixture.Service.EventChain(
            new C03RequestStream(Notify(opened.SessionId, 1L)),
            events,
            fixture.Context);

        Assert.Single(events.Written);
    }

    [Fact]
    public async Task ACancelledCallStopsWritingRatherThanContinuing()
    {
        C03Fixture fixture = new();
        fixture.Persistence.QueryScript.Add(Chunk(1L, 1L, 2L));

        await fixture.Lifetime.CancelAsync();

        C03StreamWriter<RetrieveChunk> chunks = new();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await fixture.Service.Retrieve(
                new RetrieveRequest { DatawindowHandle = "dw-1" },
                chunks,
                fixture.Context));

        Assert.Empty(chunks.Written);
    }

    [Fact]
    public async Task AnEventMissingItsRequiredObjectReferenceIsADefinedInvalidArgument()
    {
        C03Fixture fixture = new();

        OpenValidationSessionResponse opened = await fixture.Service.OpenValidationSession(
            new OpenValidationSessionRequest { DatawindowHandle = "dw-1" },
            fixture.Context);

        // The host contract THROWS for a name it does not declare (n_cst_dwsvc.sru:L121), so an omitted
        // reference must be refused here rather than surfaced as an Unknown fault carrying host internals.
        RpcException absent = await Assert.ThrowsAsync<RpcException>(async () =>
            await fixture.Service.EventChain(
                new C03RequestStream(new EventChainRequest
                {
                    SessionId = opened.SessionId,
                    Token = new SequencingToken
                    {
                        Sequence = 1L,
                        Discipline = OrderingDiscipline.Synchronous,
                    },
                    Notify = new EventNotification
                    {
                        CorrelationId = "1",
                        EventId = EventId.Ondwnitemchange,
                        DwnItemChange = new DwnItemChangeEvent { Row = 1L, Data = "x" },
                    },
                }),
                new C03StreamWriter<EventChainResponse>(),
                fixture.Context));

        Assert.Equal(StatusCode.InvalidArgument, absent.StatusCode);
    }

    [Fact]
    public async Task AnEventNamingAnUnknownObjectIsADefinedInvalidArgument()
    {
        C03Fixture fixture = new();

        OpenValidationSessionResponse opened = await fixture.Service.OpenValidationSession(
            new OpenValidationSessionRequest { DatawindowHandle = "dw-1" },
            fixture.Context);

        RpcException unknown = await Assert.ThrowsAsync<RpcException>(async () =>
            await fixture.Service.EventChain(
                new C03RequestStream(new EventChainRequest
                {
                    SessionId = opened.SessionId,
                    Token = new SequencingToken
                    {
                        Sequence = 1L,
                        Discipline = OrderingDiscipline.Synchronous,
                    },
                    Notify = new EventNotification
                    {
                        CorrelationId = "1",
                        EventId = EventId.Ondwnitemchange,
                        DwnItemChange = new DwnItemChangeEvent
                        {
                            Row = 1L,
                            Data = "x",
                            Dwo = new DwObjectRef { Name = "not_a_column" },
                        },
                    },
                }),
                new C03StreamWriter<EventChainResponse>(),
                fixture.Context));

        Assert.Equal(StatusCode.InvalidArgument, unknown.StatusCode);

        // The host's own exception is retained for the server's diagnostics and NOT sent to the caller.
        Assert.NotNull(unknown.Status.DebugException);
        Assert.DoesNotContain("KeyNotFound", unknown.Status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMarkerLabelSentWithoutTheFlagStillStoresTheOraclesSeparatorShape()
    {
        C03Fixture fixture = new();

        ApplyContextMenuModelRequest apply = new()
        {
            DatawindowHandle = "dw-1",
            Replace = true,
            Model = new WireContextMenuModel(),
        };

        // The flag is absent and only the marker label is sent. In process these are two distinct entry
        // points; on one message they collapse, and the oracle's stored shape must win either way.
        apply.Model.Items.Add(new ContextMenuItem
        {
            Text = DomainContextMenuModel.SeparatorText,
            Id = 999UL,
            Tiptext = "should not survive on a separator",
            Enabled = true,
        });

        ApplyContextMenuModelResponse applied = await fixture.Service.ApplyContextMenuModel(
            apply,
            fixture.Context);

        Assert.Equal(WireRetCode.Ok, applied.RetCode);

        ContextMenuItem stored = Assert.Single(applied.Model.Items);

        Assert.True(stored.Separator);
        Assert.Equal(DomainContextMenuModel.SeparatorText, stored.Text);
        Assert.Equal(0UL, stored.Id);
        Assert.Empty(stored.Tiptext);
    }

    [Fact]
    public async Task AnOrdinaryItemIsNeverReportedAsASeparator()
    {
        C03Fixture fixture = new();

        ApplyContextMenuModelRequest apply = new()
        {
            DatawindowHandle = "dw-1",
            Replace = true,
            Model = new WireContextMenuModel(),
        };

        apply.Model.Items.Add(new ContextMenuItem { Text = "Copy", Id = 7UL, Enabled = true });

        ApplyContextMenuModelResponse applied = await fixture.Service.ApplyContextMenuModel(
            apply,
            fixture.Context);

        ContextMenuItem stored = Assert.Single(applied.Model.Items);

        Assert.False(stored.Separator);
        Assert.Equal("Copy", stored.Text);
        Assert.Equal(7UL, stored.Id);
    }

    // -------------------------------------------------------------------------------------------------
    //  THE REFUSAL PATHS - a defined code for every structurally impossible request
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// The concurrency ceiling is an ADMISSION BOUND the oracle never had to state, because its sessions
    /// were objects in the caller's own address space. A refused open carries a code and NO identifier, so
    /// a caller must test the code rather than the identifier's truthiness.
    /// </summary>
    [Fact]
    public async Task AnOpenBeyondTheConcurrencyCeilingIsRefusedWithNoIdentifier()
    {
        DataServicesOptions configured = new();
        configured.Sessions.ValidationSession.MaxConcurrentSessions = 1;

        C03Fixture fixture = new(configured: configured);

        OpenValidationSessionResponse first = await fixture.Service.OpenValidationSession(
            new OpenValidationSessionRequest { DatawindowHandle = "dw-1" },
            fixture.Context);

        Assert.Equal(WireRetCode.Ok, first.RetCode);

        OpenValidationSessionResponse refused = await fixture.Service.OpenValidationSession(
            new OpenValidationSessionRequest { DatawindowHandle = "dw-1" },
            fixture.Context);

        Assert.NotEqual(WireRetCode.Ok, refused.RetCode);
        Assert.Empty(refused.SessionId);
    }

    [Fact]
    public async Task AnEnableMaskOutsideTheLegacyDomainIsRefused()
    {
        C03Fixture fixture = new();

        OpenValidationSessionResponse opened = await fixture.Service.OpenValidationSession(
            new OpenValidationSessionRequest { DatawindowHandle = "dw-1" },
            fixture.Context);

        // PowerBuilder's ulong is 32-bit, so a wider value is refused rather than truncated: dropping the
        // high bits would enable a different set of gates than the caller asked for.
        Assert.Equal(
            WireRetCode.EInvalidArgument,
            (await fixture.Service.EnableEvent(
                new EnableEventRequest
                {
                    SessionId = opened.SessionId,
                    EventMask = (long)uint.MaxValue + 1L,
                },
                fixture.Context)).RetCode);
    }

    [Fact]
    public async Task AFilterTypeOutsideTheLegacyDomainIsRefused()
    {
        C03Fixture fixture = new();

        Assert.Equal(
            WireRetCode.EInvalidArgument,
            (await fixture.Service.ApplyDropDownSearch(
                new ApplyDropDownSearchRequest
                {
                    DatawindowHandle = "dw-1",
                    FilterType = (ulong)uint.MaxValue + 1UL,
                },
                fixture.Context)).RetCode);
    }

    [Fact]
    public async Task ARowSelectStyleOutsideTheLegacySignedDomainIsRefused()
    {
        C03Fixture fixture = new();

        // The legacy declares #Style as a 32-bit signed long, so a value past the signed range is refused.
        Assert.Equal(
            WireRetCode.EInvalidArgument,
            (await fixture.Service.ApplyRowSelectStyle(
                new ApplyRowSelectStyleRequest
                {
                    DatawindowHandle = "dw-1",
                    Style = (ulong)long.MaxValue + 1UL,
                },
                fixture.Context)).RetCode);
    }

    /// <summary>
    /// A handle nothing resolves is refused when the SESSION IS OPENED, not several calls later.
    /// </summary>
    /// <remarks>
    /// C-03 states that a session is scoped to one DataWindow, so a session against a handle no DataWindow
    /// resolves is not a session. Answering it <c>Ok</c> - which this service used to do for any string at
    /// all - meant a caller learned of its typo only when a later column-dependent call failed, under a code
    /// describing THAT call. A BLANK handle is a different mistake and keeps its own code.
    /// </remarks>
    [Fact]
    public async Task AHandleNoDataWindowResolvesIsRefusedWhenTheSessionIsOpened()
    {
        C03Fixture fixture = new();

        Assert.Equal(
            WireRetCode.EInvalidHandle,
            (await fixture.Service.OpenValidationSession(
                new OpenValidationSessionRequest { DatawindowHandle = "no-such-dw" },
                fixture.Context)).RetCode);

        Assert.Equal(
            WireRetCode.EInvalidArgument,
            (await fixture.Service.OpenValidationSession(
                new OpenValidationSessionRequest { DatawindowHandle = "   " },
                fixture.Context)).RetCode);

        Assert.Equal(
            WireRetCode.EInvalidArgument,
            (await fixture.Service.OpenValidationSession(
                new OpenValidationSessionRequest(),
                fixture.Context)).RetCode);
    }

    [Fact]
    public async Task AHandleNoChainCanBeBoundToEndsTheStreamWithFailedPrecondition()
    {
        C03Fixture fixture = new();

        // A handle the MODEL provider serves and the CHAIN factory refuses, which is the second of
        // BindConversation's two fail-fast arms. The open-time handle check above is the first, so this
        // handle has to be one that passes it - the two arms are now reachable independently.
        OpenValidationSessionResponse opened = await fixture.Service.OpenValidationSession(
            new OpenValidationSessionRequest { DatawindowHandle = C03ChainFactory.UnboundHandle },
            fixture.Context);

        Assert.Equal(WireRetCode.Ok, opened.RetCode);

        RpcException unbound = await Assert.ThrowsAsync<RpcException>(async () =>
            await fixture.Service.EventChain(
                new C03RequestStream(Notify(opened.SessionId, 1L)),
                new C03StreamWriter<EventChainResponse>(),
                fixture.Context));

        Assert.Equal(StatusCode.FailedPrecondition, unbound.StatusCode);
    }

    /// <summary>
    /// A failing update relays its database error INTACT, including the statement text the caller is
    /// entitled to - while the log deliberately reads only the numeric code, the buffer and the row. The
    /// legacy field carries interpolated literal values because the runtime skips bind variables when
    /// <c>DisableBind</c> is set [<c>n_cst_thread_task_sqlbase.sru:L128-L129</c>], and the legacy logger
    /// performs no redaction at all.
    /// </summary>
    [Fact]
    public async Task AFailingUpdateRelaysItsDatabaseErrorAndNeverLogsTheStatement()
    {
        C03Fixture fixture = new();

        fixture.Persistence.UpdateScript = new PersistenceUpdateResponse
        {
            Status = new PersistenceOperationStatus
            {
                RetCode = WireRetCode.EDbError,
                DbError = new DbError
                {
                    Sqldbcode = -195L,
                    Sqlerrtext = "constraint violated",
                    Sqlsyntax = "UPDATE COMPANY SET salary = 1 WHERE id = 1",
                    Buffer = DwBuffer.Primary,
                    Row = 1L,
                },
            },
        };

        WireUpdateRequest request = new() { DatawindowHandle = "dw-1" };
        request.Rows.Add(new DataWindowRow { Buffer = DwBuffer.Primary, Row = 1L });

        WireUpdateResponse response = await fixture.Service.Update(request, fixture.Context);

        Assert.Equal(WireRetCode.EDbError, response.RetCode);
        Assert.NotNull(response.Error);
        Assert.Equal("UPDATE COMPANY SET salary = 1 WHERE id = 1", response.Error.Sqlsyntax);
        Assert.Equal(-195L, response.Error.Sqldbcode);
    }

    [Fact]
    public async Task AnUpstreamWithNoStatusIsAnInternalErrorRatherThanASilentSuccess()
    {
        C03Fixture fixture = new();

        fixture.Persistence.UpdateScript = new PersistenceUpdateResponse();

        WireUpdateRequest request = new() { DatawindowHandle = "dw-1" };
        request.Rows.Add(new DataWindowRow { Buffer = DwBuffer.Primary, Row = 1L });

        WireUpdateResponse response = await fixture.Service.Update(request, fixture.Context);

        Assert.NotEqual(WireRetCode.Ok, response.RetCode);
        Assert.Equal(0L, response.RowsInserted);
    }

    /// <summary>
    /// A full apply succeeds and reports the MODEL'S OWN state, which is not the same thing as the sort now
    /// on the DataWindow - and the difference is deliberate. The legacy service publishes four read-only
    /// observation seams, of which the current sort and the sort entries are two, and BOTH are accumulated
    /// by its header-click cycle [<c>n_cst_dwsvc_columnsort.sru:L112-L132</c>] rather than by applying a
    /// clause. So after an apply the entries are legitimately empty while the clause has gone to the
    /// DataWindow. Reading the host's own <c>DataWindow.Table.Sort</c> here instead would report a value
    /// this SERVICE does not hold, and is exactly the "fix" a future reader must not make.
    /// </summary>
    [Fact]
    public async Task AFullApplyReportsTheModelsOwnStateAndNotTheDataWindowsLiveSort()
    {
        C03Fixture fixture = new();

        ApplyColumnSortRequest request = new() { DatawindowHandle = "dw-1" };
        request.Columns.Add(new ColumnSortState.Types.ColumnSort
        {
            ColumnName = C03ChainFactory.Column,
            Direction = ColumnSortState.Types.Direction.SortAsc,
        });

        ApplyColumnSortResponse applied = await fixture.Service.ApplyColumnSort(request, fixture.Context);

        Assert.Equal(WireRetCode.Ok, applied.RetCode);

        GetColumnSortStateResponse read = await fixture.Service.GetColumnSortState(
            new GetColumnSortStateRequest { DatawindowHandle = "dw-1" },
            fixture.Context);

        Assert.Equal(WireRetCode.Ok, read.RetCode);
        Assert.Empty(read.State.Columns);
        Assert.Empty(read.State.SortExpression);
    }

    [Theory]
    [InlineData(ColumnSortState.Types.Direction.SortNone)]
    [InlineData(ColumnSortState.Types.Direction.SortAsc)]
    [InlineData(ColumnSortState.Types.Direction.SortDesc)]
    public async Task EverySortDirectionIsAcceptedAndOnlyTheDirectionlessOneContributesNothing(
        ColumnSortState.Types.Direction direction)
    {
        C03Fixture fixture = new();

        ApplyColumnSortRequest request = new() { DatawindowHandle = "dw-1", ExpressionOnly = true };
        request.Columns.Add(new ColumnSortState.Types.ColumnSort
        {
            ColumnName = C03ChainFactory.Column,
            Direction = direction,
        });

        ApplyColumnSortResponse applied = await fixture.Service.ApplyColumnSort(request, fixture.Context);

        Assert.Equal(WireRetCode.Ok, applied.RetCode);

        // n_cst_dwsvc_columnsort.sru:L292 - SORT_NONE returns the empty clause before any describe runs.
        if (direction == ColumnSortState.Types.Direction.SortNone)
        {
            Assert.Empty(applied.State.SortExpression);
        }
        else
        {
            Assert.Contains(C03ChainFactory.Column, applied.State.SortExpression, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AnEchoedObjectReferenceCarriesItsClassifiedPrefix()
    {
        C03Fixture fixture = new();

        GetContextMenuModelResponse menu = await fixture.Service.GetContextMenuModel(
            new GetContextMenuModelRequest
            {
                DatawindowHandle = "dw-1",
                Row = 1L,
                Dwo = new DwObjectRef { Name = "salary", Id = 5L, ColType = "decimal(2)" },
            },
            fixture.Context);

        Assert.Equal(WireRetCode.Ok, menu.RetCode);
        Assert.Equal("salary", menu.Model.Dwo.Name);
        Assert.Equal(5L, menu.Model.Dwo.Id);

        // Classified from the FIVE-CHARACTER truncation, exactly as se_cst_dw.sru:L234 does.
        Assert.Equal(DwObjectRef.Types.ColTypePrefix.Decim, menu.Model.Dwo.ColTypePrefix);
    }

    private static PersistenceQueryResponse Chunk(long chunkIndex, long firstRow, long lastRow)
    {
        PersistenceCarrierBufferSegment segment = new() { Buffer = DwBuffer.Primary };

        for (long row = firstRow; row <= lastRow; row++)
        {
            segment.Rows.Add(new DataWindowRow
            {
                Buffer = DwBuffer.Primary,
                Row = row,
                ItemStatus = ItemStatus.NotModified,
            });
        }

        return new PersistenceQueryResponse
        {
            DataChunk = new PersistenceQueryDataChunk
            {
                ChunkIndex = chunkIndex,
                State = new PersistenceCarrierState { Segments = { segment } },
            },
        };
    }

    // =============================================================================================
    //  7. THE LIVE CARRIER - what actually crosses the Persistence edge, over the SHIPPED host
    //
    //  EVERY OTHER CASE IN THIS FILE DRIVES C03ModelSetProvider, WHOSE HOST IS A DOUBLE. That is the
    //  right default: this suite characterises the transport, and a double keeps the assertions about
    //  ordering, alphabets and lifecycle free of a DataWindow definition's detail. But three fields of
    //  the outbound carrier are derived from the host's OWN definition rather than from the caller's
    //  request, and for those a double asserts nothing about the deployed service - a host that answers
    //  a property the shipped host does not answer produces a carrier the shipped service cannot
    //  produce. All three were wrong in the deployed service while this file was entirely green:
    //
    //    processing    left unset, so it said "unassigned", which DISAGREES with the fixture's declared
    //                  kind - and the receiving codec refuses a carrier whose kind disagrees with its
    //                  target's, so EVERY non-empty update was refused with E_INVALID_DATA before a
    //                  single statement was generated.
    //    autocommit    left unset, so the session's teardown rolled back work that had already been
    //                  applied and counted: an update answering rowsUpdated:1 while storage still held
    //                  the old row. A success that loses data, which is worse than the refusal above.
    //    column_name   left empty on every relayed row, because the upstream carrier is positional like
    //                  the legacy blob and only THIS layer holds the definition that resolves an
    //                  ordinal to a name.
    //
    //  So these cases compose the service over the REAL HeadlessDataWindowModelSetProvider, exactly as
    //  the composition root does, and assert the carrier that reaches the scripted upstream.
    // =============================================================================================

    /// <summary>
    /// The outbound update carrier declares the processing kind the served definition declares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>persistence.v1.CarrierState.processing</c> IS RECONCILED RATHER THAN INFORMATIONAL.</b> Both
    /// sides build a carrier from their own transcription of the same DataWindow, so the receiving codec
    /// refuses a payload whose kind disagrees with its target's and adopts the payload's kind only for a
    /// target that has none [<c>Buffers/ChangesetCodec.cs - TryValidateSegments</c>]. The primary fixture
    /// declares <c>processing=1</c> [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L3</c>], so an unset
    /// field is not a harmless default here - it is a disagreement, and it refused every real update.
    /// </para>
    /// <para>
    /// AN UNRESOLVABLE HANDLE STILL SENDS ZERO AND STILL REACHES THE UPSTREAM, asserted below. Refusing
    /// at this layer would move an established refusal out of the layer that owns the update contract
    /// [<c>n_cst_thread_task_sqlupdate.sru:L179-L189</c>] and change which code a caller receives for an
    /// unknown handle on this operation alone.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task UpdateSendsTheProcessingKindTheServedDefinitionDeclares()
    {
        C03Fixture fixture = new(BuildLiveModelSetProvider());

        WireUpdateRequest request = new()
        {
            DatawindowHandle = DataWindowCatalogue.SqliteFixtureName,
        };

        request.Rows.Add(new DataWindowRow
        {
            Buffer = DwBuffer.Primary,
            Row = 1L,
            ItemStatus = ItemStatus.DataModified,
        });

        WireUpdateResponse response = await fixture.Service.Update(request, fixture.Context);

        Assert.Equal(WireRetCode.Ok, response.RetCode);

        PersistenceUpdateRequest sent = fixture.Persistence.LastUpdate!;

        // ⚠ THE REGRESSION GUARD. dw_sqlite.srd:L3 declares processing=1, and zero here is the value the
        // receiving codec reads as "unassigned" - which it refuses against a target that has a kind.
        Assert.Equal(1L, sent.UpdateData.Processing);
        Assert.NotEqual(0L, sent.UpdateData.Processing);

        // An unresolved handle sends zero AND still reaches the upstream, which keeps the refusal where
        // the update contract owns it.
        C03Fixture unresolved = new(BuildLiveModelSetProvider());

        WireUpdateRequest missing = new() { DatawindowHandle = "d_never_transcribed" };
        missing.Rows.Add(new DataWindowRow { Buffer = DwBuffer.Primary, Row = 1L });

        _ = await unresolved.Service.Update(missing, unresolved.Context);

        Assert.Equal(1, unresolved.Persistence.UpdateCalls);
        Assert.Equal(0L, unresolved.Persistence.LastUpdate!.UpdateData.Processing);
    }

    /// <summary>
    /// The outbound update request sets the TASK-LEVEL autocommit, without which applied work is lost.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THIS IS C-06'S TASK EPILOGUE SWITCH</b> - <c>if _bAutoCommit then rtCode = of_Commit(true)</c>
    /// [<c>n_cst_thread_task_sqlupdate.sru:L386-L387</c>] - and it is NOT the session descriptor's
    /// connection-level autocommit, which stays FALSE so the session keeps one explicit transaction. Both
    /// are asserted here together, because it is the PAIR that is correct: an explicit transaction that
    /// nothing commits is discarded by the session teardown, and a connection-level autocommit would
    /// remove the transaction the conflict check needs to roll back.
    /// </para>
    /// <para>
    /// WHY IT HAS TO BE SET ON THIS REQUEST. The session's whole lifetime is this one call - the work
    /// scope opens it, runs the update and ends it - and ending it rolls back any open transaction, which
    /// is the correct posture for a disconnect. C-08's commit names a session handle that never leaves the
    /// method, so nothing else on the published surface could commit it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task UpdateSetsTheTaskAutocommitAndLeavesTheSessionTransactionExplicit()
    {
        C03Fixture fixture = new(BuildLiveModelSetProvider());

        WireUpdateRequest request = new()
        {
            DatawindowHandle = DataWindowCatalogue.SqliteFixtureName,
        };

        request.Rows.Add(new DataWindowRow { Buffer = DwBuffer.Primary, Row = 1L });

        _ = await fixture.Service.Update(request, fixture.Context);

        // ⚠ THE REGRESSION GUARD. Unset, C-06 applied and counted the rows and the teardown threw them
        // away - a success that loses data.
        Assert.True(fixture.Persistence.LastUpdate!.Autocommit);

        // AND THE SESSION STAYS EXPLICITLY TRANSACTED, which is the other half of the pair.
        BeginSessionRequest begun = Assert.Single(fixture.Persistence.TransactionStub.BeginRequests);

        // `Descriptor_` with the trailing underscore is the GENERATED spelling: the field is named
        // `descriptor` and protobuf's own static `Descriptor` property already owns the plain name, so the
        // generator disambiguates. Named as generated rather than renamed in the proto, because the field
        // name is part of the published contract.
        Assert.False(begun.Descriptor_.Autocommit);
    }

    /// <summary>
    /// Every relayed column value carries its NAME beside the ordinal the upstream supplied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>common.v1.ColumnValue</c> DECLARES BOTH IDENTIFIERS AND NEITHER IS REDUNDANT</b> - a name
    /// survives a column reorder and the status APIs take the ordinal - but the upstream carrier is a
    /// faithful stand-in for the legacy's positional <c>GetChanges</c>/<c>GetFullState</c> blob, which has
    /// ordinals and no names in it at all. The DataWindow DEFINITION is what resolves one to the other and
    /// this service is the layer that holds it, so this is the only place the name can be filled in.
    /// </para>
    /// <para>
    /// A SUPPLIED NAME IS LEFT ALONE, and an ordinal no column carries stays nameless rather than being
    /// given an invented name - both asserted, because inventing one would let a consumer address a column
    /// that does not exist.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task RelayedColumnValuesCarryTheirNameBesideTheirOrdinal()
    {
        C03Fixture fixture = new(BuildLiveModelSetProvider());

        DataWindowRow row = new() { Buffer = DwBuffer.Primary, Row = 1L };

        // AS PERSISTENCE EMITS THEM: an ordinal and no name.
        row.Columns.Add(new ColumnValue { ColumnId = 2L });
        row.Columns.Add(new ColumnValue { ColumnId = 5L });

        // A name the upstream DID supply, which must survive untouched.
        row.Columns.Add(new ColumnValue { ColumnId = 3L, ColumnName = "supplied_by_upstream" });

        // Zero addresses the ROW rather than a column, and 99 is past the last one.
        row.Columns.Add(new ColumnValue { ColumnId = 0L });
        row.Columns.Add(new ColumnValue { ColumnId = 99L });

        // The original-value shadow is named too, because updatewhere=1 compares originals and a consumer
        // reading the shadow needs the same identifier the current value carries.
        row.OriginalValues.Add(new ColumnValue { ColumnId = 5L });

        fixture.Persistence.QueryScript.Add(new PersistenceQueryResponse
        {
            DataChunk = new PersistenceQueryDataChunk
            {
                ChunkIndex = 1L,
                State = new PersistenceCarrierState
                {
                    Segments =
                    {
                        new PersistenceCarrierBufferSegment
                        {
                            Buffer = DwBuffer.Primary,
                            Rows = { row },
                        },
                    },
                },
            },
        });

        C03StreamWriter<RetrieveChunk> writer = new();

        await fixture.Service.Retrieve(
            new RetrieveRequest { DatawindowHandle = DataWindowCatalogue.SqliteFixtureName },
            writer,
            fixture.Context);

        DataWindowRow relayed = Assert.Single(writer.Written[0].Rows);

        // ⚠ THE REGRESSION GUARD. dw_sqlite.srd:L21-L26 - ordinal 2 is name, ordinal 5 is salary.
        Assert.Equal("name", relayed.Columns[0].ColumnName);
        Assert.Equal("salary", relayed.Columns[1].ColumnName);

        // The supplied name is resolved, not decided: it survives exactly as it arrived.
        Assert.Equal("supplied_by_upstream", relayed.Columns[2].ColumnName);

        // Unresolvable ordinals stay nameless, and their ordinal is untouched so they remain addressable.
        Assert.Equal(string.Empty, relayed.Columns[3].ColumnName);
        Assert.Equal(0L, relayed.Columns[3].ColumnId);
        Assert.Equal(string.Empty, relayed.Columns[4].ColumnName);
        Assert.Equal(99L, relayed.Columns[4].ColumnId);

        Assert.Equal("salary", Assert.Single(relayed.OriginalValues).ColumnName);
    }

    /// <summary>
    /// Builds the model-set provider the composition root registers, over the shipped catalogue.
    /// </summary>
    /// <returns>The provider.</returns>
    /// <remarks>
    /// COMPOSED FROM THE REAL COLLABORATORS RATHER THAN A DOUBLE, which is the whole point of section 7:
    /// the pinyin matcher is the shipped BLOCKED one (AAP 0.6.5), the page resolver is built from the
    /// preserved option defaults, and the host factory reads the same <see cref="DataWindowCatalogue"/> the
    /// deployed service serves.
    /// </remarks>
    private static HeadlessDataWindowModelSetProvider BuildLiveModelSetProvider()
    {
        DataServicesOptions configured = new();

        return new HeadlessDataWindowModelSetProvider(
            new HeadlessDataWindowHostFactory(new DataWindowCatalogue()),
            Options.Create(configured),
            new I18n(),
            PinyinFirstLetterMatcher.Blocked,
            ExpressionPageResolverFactory.Create(
                configured.ColumnExpression.PageResolution,
                configured.ColumnExpression.PageRowsPerPage));
    }

    private static EventChainRequest Notify(string sessionId, long sequence) => new()
    {
        SessionId = sessionId,
        Token = new SequencingToken
        {
            Sequence = sequence,
            Discipline = OrderingDiscipline.Sequenced,
        },
        Notify = new EventNotification
        {
            CorrelationId = sequence.ToString(CultureInfo.InvariantCulture),
            EventId = EventId.Ondwnsetfocus,
            DwnSetFocus = new DwnSetFocusEvent(),
        },
    };

    private static EventChainRequest ItemChange(string sessionId, long sequence) => new()
    {
        SessionId = sessionId,
        Token = new SequencingToken
        {
            Sequence = sequence,
            Discipline = OrderingDiscipline.Synchronous,
        },
        Notify = new EventNotification
        {
            CorrelationId = sequence.ToString(CultureInfo.InvariantCulture),
            EventId = EventId.Ondwnitemchange,
            DwnItemChange = new DwnItemChangeEvent
            {
                Row = 1L,
                Data = "x",
                Dwo = new DwObjectRef { Name = C03ChainFactory.Column, ColType = "long" },
            },
        },
    };
}
