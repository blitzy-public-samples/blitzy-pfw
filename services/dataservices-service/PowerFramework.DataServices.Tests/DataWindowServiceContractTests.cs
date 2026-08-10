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
using PersistenceCarrierBufferSegment = PowerFramework.Contracts.Persistence.V1.CarrierBufferSegment;
using PersistenceCarrierState = PowerFramework.Contracts.Persistence.V1.CarrierState;
using PersistenceOperationStatus = PowerFramework.Contracts.Persistence.V1.OperationStatus;
using PersistenceQueryDataChunk = PowerFramework.Contracts.Persistence.V1.QueryDataChunk;
using PersistenceQueryRequest = PowerFramework.Contracts.Persistence.V1.QueryRequest;
using PersistenceQueryResponse = PowerFramework.Contracts.Persistence.V1.QueryResponse;
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

    public Task WriteAsync(T message)
    {
        lock (_written)
        {
            _written.Add(message);
        }

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
internal sealed class C03PersistenceClient : PersistenceClient
{
    internal C03PersistenceClient()
        : base(
            new FakeQueryServiceClient(),
            new FakeUpdateServiceClient(),
            new FakeCommandServiceClient(),
            new FakeTransactionServiceClient(),
            new PersistenceStubTokenProvider(),
            new PersistenceRecordingLogger())
    {
    }

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

    public override IAsyncEnumerable<PersistenceQueryResponse> QueryAsync(
        PersistenceQueryRequest request,
        CancellationToken cancellationToken)
    {
        LastQuery = request;

        return Replay(QueryScript, cancellationToken);
    }

    public override Task<PersistenceUpdateResponse> UpdateAsync(
        PersistenceUpdateRequest request,
        CancellationToken cancellationToken)
    {
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

    internal FakeEventChain? Created { get; private set; }

    public DataWindowEventChain? Create(
        ValidationSession session,
        string dataWindowHandle,
        IDataWindowEventObserver observer,
        IDataWindowSemanticResponder responder)
    {
        ArgumentNullException.ThrowIfNull(responder);

        if (string.Equals(dataWindowHandle, "no-such-dw", StringComparison.Ordinal))
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
    internal C03Fixture(IDataWindowModelSetProvider? models = null, DataServicesOptions? configured = null)
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
            Persistence);
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

    [Fact]
    public void ASequencedGroupToleratesAnArrivalBehindTheHighWaterMark()
    {
        C03StreamWriter<EventChainResponse> writer = new();
        using DataWindowEventConversation conversation = new("s-1", writer);
        conversation.Bind(new DataWindowEventSequencer());

        conversation.AcceptInbound(5L, EventId.Ondwnsetfocus, strictOrdering: true);

        // Reorder authority is the consumer's in a sequenced group, so this must NOT throw.
        conversation.AcceptInbound(3L, EventId.Ondwnsetfocus, strictOrdering: true);
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

        // The handle IS the data object, and the chunk size travelled unchanged.
        Assert.Equal("dw-1", fixture.Persistence.LastQuery?.Spec.DataObject);
        Assert.Equal(2000L, fixture.Persistence.LastQuery?.Spec.ChunkSize);
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
    public async Task AFailingUpstreamStatusEndsTheStreamOnAFinalErrorChunk()
    {
        C03Fixture fixture = new();

        fixture.Persistence.QueryScript.Add(new PersistenceQueryResponse
        {
            Status = new PersistenceOperationStatus
            {
                RetCode = WireRetCode.EDbError,
                DbError = new DbError { Sqldbcode = -1L, Sqlsyntax = "SELECT * FROM COMPANY" },
            },
        });

        C03StreamWriter<RetrieveChunk> writer = new();

        await fixture.Service.Retrieve(
            new RetrieveRequest { DatawindowHandle = "dw-1" },
            writer,
            fixture.Context);

        RetrieveChunk terminal = Assert.Single(writer.Written);
        Assert.True(terminal.Final);
        Assert.Equal(0L, terminal.RowCount);
        Assert.NotNull(terminal.Error);

        // RELAYED to the caller, who is entitled to it. The redaction rule is about the LOG.
        Assert.Equal("SELECT * FROM COMPANY", terminal.Error.Sqlsyntax);
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

    [Fact]
    public async Task RetrieveArgumentsCarryTheirOneBasedLegacyOrdinal()
    {
        C03Fixture fixture = new();

        RetrieveRequest request = new() { DatawindowHandle = "dw-1" };
        request.Arguments.Add(new AnyValue { StringValue = "first" });
        request.Arguments.Add(new AnyValue { StringValue = "second" });

        await fixture.Service.Retrieve(request, new C03StreamWriter<RetrieveChunk>(), fixture.Context);

        // Wire element 0 IS legacy args[1] - the one place a basis changes.
        Assert.Equal("1", fixture.Persistence.LastQuery?.Spec.Parameters[0].Name);
        Assert.Equal("first", fixture.Persistence.LastQuery?.Spec.Parameters[0].Value.StringValue);
        Assert.Equal("2", fixture.Persistence.LastQuery?.Spec.Parameters[1].Name);
        Assert.Equal("second", fixture.Persistence.LastQuery?.Spec.Parameters[1].Value.StringValue);
    }

    [Fact]
    public async Task AZeroChunkSizeIsNotForwardedBecauseItMeansTheServerChooses()
    {
        C03Fixture fixture = new();

        await fixture.Service.Retrieve(
            new RetrieveRequest { DatawindowHandle = "dw-1" },
            new C03StreamWriter<RetrieveChunk>(),
            fixture.Context);

        Assert.False(fixture.Persistence.LastQuery?.Spec.HasChunkSize);
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

    [Fact]
    public async Task UpdateGroupsRowsByBufferPreservingOrderAndRelaysIdentityUnchanged()
    {
        C03Fixture fixture = new();

        WireUpdateRequest request = new() { DatawindowHandle = "dw-1" };
        request.Rows.Add(new DataWindowRow { Buffer = DwBuffer.Primary, Row = 1L });
        request.Rows.Add(new DataWindowRow { Buffer = DwBuffer.Filter, Row = 9L });
        request.Rows.Add(new DataWindowRow { Buffer = DwBuffer.Primary, Row = 2L });

        WireUpdateResponse response = await fixture.Service.Update(request, fixture.Context);

        Assert.Equal(WireRetCode.Ok, response.RetCode);
        Assert.Equal(3L, response.RowsInserted);

        // Segment order is FIRST APPEARANCE; row order inside a segment is the caller's, because the
        // Filter buffer's order is inverted at source [n_cst_thread_task_sqlupdate.sru:L235].
        Assert.Equal(2, fixture.Persistence.LastUpdate!.UpdateData.Segments.Count);
        Assert.Equal(DwBuffer.Primary, fixture.Persistence.LastUpdate.UpdateData.Segments[0].Buffer);
        Assert.Equal(DwBuffer.Filter, fixture.Persistence.LastUpdate.UpdateData.Segments[1].Buffer);
        Assert.Equal(
            [1L, 2L],
            fixture.Persistence.LastUpdate.UpdateData.Segments[0].Rows.Select(row => row.Row));
        Assert.Equal(3L, fixture.Persistence.LastUpdate.UpdateRows);

        // The identity block is relayed element for element.
        IdentityColumnData identity = Assert.Single(response.Identity);
        Assert.Equal(1L, identity.IdentityColumnId);
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
                Notify(opened.SessionId, 1L)),
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

    [Fact]
    public async Task AHandleNoChainCanBeBoundToEndsTheStreamWithFailedPrecondition()
    {
        C03Fixture fixture = new();

        // The registry issues a session for any handle; the CHAIN factory is what refuses this one, which
        // is the second of BindConversation's two fail-fast arms.
        OpenValidationSessionResponse opened = await fixture.Service.OpenValidationSession(
            new OpenValidationSessionRequest { DatawindowHandle = "no-such-dw" },
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
