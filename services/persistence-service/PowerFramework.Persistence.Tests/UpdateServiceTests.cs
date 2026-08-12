// ==================================================================================================
//  UpdateServiceTests.cs - the parity suite for contract C-06, persistence.v1.UpdateService
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS SUITE PINS, AND WHY EACH ITEM IS HERE
//  Every fact asserted below was measured in the read-only behavioural oracle under ws_objects/**, and
//  each test names the locator it pins. These are CHARACTERIZATION assertions in Michael Feathers' sense:
//  they record what the legacy ACTUALLY does, including where that is a defect, because constraint C-B
//  forbids correcting legacy behaviour during the migration. A test here failing after an edit means the
//  edit changed observable behaviour - not that the test is wrong.
//
//  Unqualified locators of the form [:Lnnn] name the primary oracle,
//  ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru; every other file is spelled out.
//
//      the SIX-FIELD descriptor order, updatewhere FIFTH and keyinplace SIXTH   [:L10-L17]
//      an unset updatewhere / keyinplace surviving as UNSET, never defaulted    n_cst_threading_task_sqlupdate.sru:L227-L234
//      an explicit zero mode being distinguishable from an absent one           [:L131, :L135]
//      the THREE-ARM admission test, with an EMPTY IDENTITY COLUMN ACCEPTED     [:L84]
//      the multi-table switch set after the clear that turns it off             [:L63, :L265-L268]
//      multi-table prepare over the repeated descriptor, ONE-BASED ordinals     [:L358, :L364-L369]
//      multi-table ON with an empty array refused with the legacy diagnostic    [:L359-L363]
//      the SINGLE-TABLE path that never prepares at all                         [:L371]
//      counts ACCUMULATING and ONE identity block appended per table            [:L247], threading:L66-L68, L73-L76
//      the Primary-forward / Filter-backward arrays serialized AS COLLECTED     [:L228-L241]
//      both identity gates, and an EMPTY identity payload as a valid success    [:L215, :L242-L244]
//      a CLEAN VETO yielding cancelled with no text and no driver detail        [:L201, :L396-L400]
//      a VETO PLUS A TRANSACTION FAILURE yielding a database error, both        [:L196-L199],
//        channels carried                                                       n_cst_thread_trans.sru:L337
//      the three update-table sentinels surfacing the synthesized payload       [:L188-L193]
//      E_BUSY answered by the task itself on every mutating call                [:L60], threading:L99/L106/L223/L236
//      a concurrency mismatch raised as ABORTED with a POPULATED ConflictDetail AAP 0.6.3.8, constraint C-K
//      the in-process algebra and the generated wire enum agreeing VALUE FOR    ws_objects/pfw.shared.pbl.src/retcode.sru,
//        VALUE - the review invariant with no compile-time enforcement           AAP 0.4.5.3
//
//  WHY THE VALUE-FOR-VALUE TEST IS LOAD-BEARING. PowerFramework.Contracts declares zero
//  ProjectReference - deliberately none to Shared.Kernel - so the numeric agreement between the
//  generated RetCode enum in common.v1.proto and Shared.Kernel/RetCode.cs has NO compile-time
//  enforcement anywhere in the repository, and the adapter under test mixes the two on nearly every
//  path. A divergence would be a silent wire-compatibility defect that no compiler and no single-sided
//  unit test would catch, which is exactly why it is asserted here rather than assumed.
//
//  THE TASK IS SUBSTITUTED AT THE BOUNDARY SEAM, WHICH IS THE POINT OF THE SEAM. Every test drives a
//  recording fake of IUpdateTaskSurface and IUpdateTaskFactory, so the adapter is exercised without a
//  worker thread, without a transaction and without a scheduler. That keeps the assertions on the
//  translation this file owns - validate, translate, delegate, map - and leaves the behavioural detail
//  to the suites that own it: ConflictDetectorTests, IdentityColumnResolverTests, UpdateWhereBuilderTests
//  and SqlUpdateTaskProxyTests.
//
//  NO DATABASE IS TOUCHED (constraint C-E). The fake opens no connection, composes no connection string
//  and provisions nothing, so the whole matrix runs in CI - which is what makes the per-service coverage
//  gate reachable (constraint C-H).
//
//  NO CREDENTIAL APPEARS ANYWHERE BELOW (constraint C-F). The conflict fixture carries column values
//  because the contract requires a conflict to report current and original row state, and those values
//  are obvious non-secrets that match no provider credential pattern.
// ==================================================================================================
using Grpc.Core;
using Microsoft.Extensions.Options;
using PowerFramework.Persistence.Concurrency;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Grpc;
using PowerFramework.Persistence.Tasks;

// The adapter and the generated contract wrapper share the simple name UpdateService, exactly as
// GlobalUsings.cs anticipates for this folder. An alias directive is resolved ahead of the namespace
// imports at the same scope, so it is the fix for CS0104 rather than another candidate for it - and it
// RENAMES a reference without collapsing either type: both remain distinct and both remain reachable.
using DataObjectDefinitionRegistry = PowerFramework.Persistence.Data.DataObjectDefinitionCatalogue;
using UpdateService = PowerFramework.Persistence.Grpc.UpdateService;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Characterization coverage for <c>Grpc/UpdateService.cs</c>, the C-06 adapter and the designated
/// <see cref="RpcException"/>/<see cref="StatusCode.Aborted"/> throw site for this service.
/// </summary>
public sealed class UpdateServiceTests
{
    private sealed class FakeCallContext(CancellationToken cancellationToken) : ServerCallContext
    {
        private readonly CancellationToken _token = cancellationToken;

        protected override string MethodCore => "/persistence.v1.UpdateService/Update";

        protected override string HostCore => "localhost:5101";

        protected override string PeerCore => "ipv4:127.0.0.1:0";

        protected override DateTime DeadlineCore => DateTime.MaxValue;

        protected override Metadata RequestHeadersCore { get; } = [];

        protected override CancellationToken CancellationTokenCore => _token;

        protected override Metadata ResponseTrailersCore { get; } = [];

        protected override Status StatusCore { get; set; }

        protected override WriteOptions? WriteOptionsCore { get; set; }

        protected override AuthContext AuthContextCore { get; } =
            new("fake", new Dictionary<string, List<AuthProperty>>(StringComparer.Ordinal));

        protected override ContextPropagationToken CreatePropagationTokenCore(
            ContextPropagationOptions? options) => throw new NotSupportedException();

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }

    /// <summary>One recorded AddUpdatableTable call, with presence preserved.</summary>
    private sealed record RecordedAddCall(
        string Name,
        IReadOnlyList<string> UpdatableColumns,
        IReadOnlyList<string> KeyColumns,
        string IdentityColumn,
        long? UpdateWhere,
        bool? UpdateKeyInPlace,
        bool SixArgumentForm);

    private sealed class FakeTaskSurface : IUpdateTaskSurface
    {
        private readonly List<RecordedAddCall> _adds = [];
        private readonly List<string> _calls = [];

        internal IReadOnlyList<RecordedAddCall> Adds => _adds;

        internal IReadOnlyList<string> Calls => _calls;

        internal bool Busy { get; set; }

        internal bool Disposed { get; private set; }

        internal long AddResult { get; set; } = RetCode.OK;

        internal bool? AutoCommitSeen { get; private set; }

        internal CarrierState? PayloadSeen { get; private set; }

        internal long PayloadRowsSeen { get; private set; }

        internal bool MultiTableSeen { get; private set; }

        internal string? DataObjectSeen { get; private set; }

        internal string? SqlSyntaxSeen { get; private set; }

        internal CancellationToken TokenSeen { get; private set; }

        internal UpdateRunResult Result { get; set; } =
            new() { Code = RetCode.OK };

        public long Reset()
        {
            _calls.Add(nameof(Reset));

            return Busy ? RetCode.E_BUSY : RetCode.OK;
        }

        public long ResetUpdatableTables()
        {
            _calls.Add(nameof(ResetUpdatableTables));

            return Busy ? RetCode.E_BUSY : RetCode.OK;
        }

        public long SetMultiTableUpdate(bool multiTable)
        {
            _calls.Add(nameof(SetMultiTableUpdate));
            MultiTableSeen = multiTable;

            return Busy ? RetCode.E_BUSY : RetCode.OK;
        }

        public long AddUpdatableTable(
            string name,
            IEnumerable<string> updatableColumns,
            IEnumerable<string> keyColumns,
            string identityColumn,
            long? updateWhere,
            bool? updateKeyInPlace)
        {
            _calls.Add("AddUpdatableTable6");
            _adds.Add(new RecordedAddCall(
                name,
                [.. updatableColumns],
                [.. keyColumns],
                identityColumn,
                updateWhere,
                updateKeyInPlace,
                SixArgumentForm: true));

            return Busy ? RetCode.E_BUSY : AddResult;
        }

        public long AddUpdatableTable(
            string name,
            IEnumerable<string> updatableColumns,
            IEnumerable<string> keyColumns,
            string identityColumn)
        {
            _calls.Add("AddUpdatableTable4");
            _adds.Add(new RecordedAddCall(
                name,
                [.. updatableColumns],
                [.. keyColumns],
                identityColumn,
                UpdateWhere: null,
                UpdateKeyInPlace: null,
                SixArgumentForm: false));

            return Busy ? RetCode.E_BUSY : AddResult;
        }

        public long SetDataObject(string dataObject)
        {
            _calls.Add(nameof(SetDataObject));
            DataObjectSeen = dataObject;

            return Busy ? RetCode.E_BUSY : RetCode.OK;
        }

        public long SetSqlSyntax(string sqlSyntax)
        {
            _calls.Add(nameof(SetSqlSyntax));
            SqlSyntaxSeen = sqlSyntax;

            return Busy ? RetCode.E_BUSY : RetCode.OK;
        }

        // MIRRORS THE REAL SURFACE, WHICH READS THE WORKER'S TWO SOURCE FIELDS: either one non-empty is a
        // source. Modelled rather than hard-coded true so that a test which prepares WITHOUT naming a source
        // meets the same refusal a real task would.
        public bool HasUpdateSource =>
            DataObjectSeen is { Length: > 0 } || SqlSyntaxSeen is { Length: > 0 };

        // MIRRORS THE REAL SURFACE: the data object the task currently holds, which the source setters clear
        // each other out of. Empty rather than null, because the real member answers the worker's own field.
        public string DataObject => SqlSyntaxSeen is { Length: > 0 } ? string.Empty : DataObjectSeen ?? string.Empty;

        public long SetUpdateData(CarrierState? updateData, long updateRows)
        {
            _calls.Add(nameof(SetUpdateData));
            PayloadSeen = updateData;
            PayloadRowsSeen = updateRows;

            return Busy ? RetCode.E_BUSY : RetCode.OK;
        }

        public long SetAutoCommit(bool autoCommit)
        {
            _calls.Add(nameof(SetAutoCommit));
            AutoCommitSeen = autoCommit;

            return Busy ? RetCode.E_BUSY : RetCode.OK;
        }

        /// <summary>Completes the moment <see cref="Execute"/> has been entered.</summary>
        /// <remarks>
        /// The seam the lease matrix needs. The worker side is SYNCHRONOUS by contract (AAP 0.4.5.4), so
        /// the adapter blocks inside this call - which is what lets a test hold the operation lease open
        /// and send a second request at it. A TaskCompletionSource rather than a wait handle so nothing
        /// here is disposable.
        /// </remarks>
        internal TaskCompletionSource Entered { get; } = new();

        /// <summary>Set by a test to block inside <see cref="Execute"/> until it is completed.</summary>
        internal TaskCompletionSource? Gate { get; set; }

        public UpdateRunResult Execute(CancellationToken cancellationToken)
        {
            _calls.Add(nameof(Execute));
            TokenSeen = cancellationToken;

            _ = Entered.TrySetResult();

            // Blocking, deliberately: this is the worker-side half of the proxy pair and it does not
            // await. Only a test ever sets the gate, so the ordinary path is unchanged.
            Gate?.Task.GetAwaiter().GetResult();

            return Result;
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeTaskFactory : IUpdateTaskFactory
    {
        /// <summary>
        /// When set, the publication window this factory answers reports the session as already retiring, so
        /// the adapter's post-build liveness arm is reachable without a pool, a session registry or a
        /// database.
        /// </summary>
        /// <remarks>
        /// The default interface implementation of <see cref="IUpdateTaskFactory.EnterPublicationAsync"/>
        /// answers an unguarded, live window - correct for a double with no sessions to retire - so this flag
        /// is how a test asks for the other answer. The REAL gate behaviour is pinned separately, against the
        /// provisioned factory, in <c>PersistenceTaskCompositionTests</c>.
        /// </remarks>
        internal bool PublicationRetiring { get; set; }

        /// <summary>How many publication windows this factory was asked for.</summary>
        internal int PublicationsEntered { get; private set; }

        internal long CreateResult { get; set; } = RetCode.OK;

        internal bool ProduceNull { get; set; }

        internal string? SessionSeen { get; private set; }

        internal List<FakeTaskSurface> Created { get; } = [];

        public long TryCreate(string sessionId, out IUpdateTaskSurface? task)
        {
            SessionSeen = sessionId;

            if (CreateResult != RetCode.OK || ProduceNull)
            {
                task = null;

                return CreateResult;
            }

            FakeTaskSurface surface = new();
            Created.Add(surface);
            task = surface;

            return RetCode.OK;
        }

        public ValueTask<UpdateTaskPublication> EnterPublicationAsync(
            string sessionId,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(sessionId);

            PublicationsEntered++;

            return ValueTask.FromResult(
                new UpdateTaskPublication(default, isSessionRetiring: PublicationRetiring));
        }
    }
    private static readonly ServerCallContext Context = new FakeCallContext(CancellationToken.None);

    private static (UpdateService Service, FakeTaskFactory Factory, UpdateTaskRegistry Registry)
        CreateService()
    {
        FakeTaskFactory factory = new();
        UpdateTaskRegistry registry = new(Options.Create(new PersistenceOptions()), TimeProvider.System);

        return (new UpdateService(factory, registry, new DataObjectDefinitionRegistry()), factory, registry);
    }

    private static async Task<(UpdateService Service, FakeTaskSurface Surface, TaskHandle Handle,
        UpdateTaskRegistry Registry)> CreateTaskAsync()
    {
        (UpdateService service, FakeTaskFactory factory, UpdateTaskRegistry registry) = CreateService();

        CreateUpdateTaskResponse created = await service.CreateUpdateTask(
            new CreateUpdateTaskRequest { Session = new SessionHandle { SessionId = "s-1" } },
            Context);

        Assert.Equal(WireRetCode.Ok, created.Status.RetCode);

        return (service, factory.Created[0], created.Task, registry);
    }

    // ---- the generated base and the RPC set -----------------------------------------------------

    [Fact]
    public void TheServiceDerivesFromTheGeneratedBaseAndOverridesEveryDeclaredRpc()
    {
        Assert.True(typeof(global::PowerFramework.Contracts.Persistence.V1.UpdateService.UpdateServiceBase)
            .IsAssignableFrom(typeof(UpdateService)));

        string[] declared = [.. global::PowerFramework.Contracts.Persistence.V1.UpdateService.Descriptor
            .Methods.Select(static method => method.Name)];

        string[] overridden = [.. typeof(UpdateService)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(static method => method.IsVirtual)
            .Select(static method => method.Name)];

        Assert.Equal(5, declared.Length);
        Assert.Equal([.. declared.Order()], [.. overridden.Order()]);
    }

    [Fact]
    public void NoMethodCarriesAnAnonymousAuthorizationEscape()
    {
        Assert.DoesNotContain(
            typeof(UpdateService).GetCustomAttributes(inherit: true),
            static attribute => attribute.GetType().Name.Contains(
                "AllowAnonymous", StringComparison.Ordinal));
    }

    // ---- CreateUpdateTask / ReleaseUpdateTask / Reset --------------------------------------------

    [Fact]
    public async Task CreateUpdateTaskRequiresASession()
    {
        (UpdateService service, _, UpdateTaskRegistry registry) = CreateService();

        CreateUpdateTaskResponse blank = await service.CreateUpdateTask(
            new CreateUpdateTaskRequest(),
            Context);

        Assert.Equal(WireRetCode.EInvalidTransaction, blank.Status.RetCode);
        Assert.Null(blank.Task);
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public async Task CreateUpdateTaskReportsAFactoryRefusalAndNeverDereferencesANullTask()
    {
        (UpdateService service, FakeTaskFactory factory, UpdateTaskRegistry registry) = CreateService();
        factory.ProduceNull = true;

        CreateUpdateTaskResponse response = await service.CreateUpdateTask(
            new CreateUpdateTaskRequest { Session = new SessionHandle { SessionId = "s-1" } },
            Context);

        Assert.Equal(WireRetCode.EInvalidObject, response.Status.RetCode);
        Assert.Equal(0, registry.Count);
        Assert.Equal("s-1", factory.SessionSeen);
    }

    [Fact]
    public async Task ReleaseUpdateTaskIsSingleUseAndDisposesExactlyOnce()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, UpdateTaskRegistry registry) =
            await CreateTaskAsync();

        Assert.Equal(1, registry.Count);

        ReleaseUpdateTaskResponse first = await service.ReleaseUpdateTask(
            new ReleaseUpdateTaskRequest { Task = handle },
            Context);

        ReleaseUpdateTaskResponse second = await service.ReleaseUpdateTask(
            new ReleaseUpdateTaskRequest { Task = handle },
            Context);

        Assert.Equal(WireRetCode.Ok, first.Status.RetCode);
        Assert.Equal(WireRetCode.EInvalidHandle, second.Status.RetCode);
        Assert.True(surface.Disposed);
        Assert.Equal(0, registry.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-handle")]
    public async Task EveryHandleThatNamesNoLiveTaskAnswersInvalidHandle(string? taskId)
    {
        (UpdateService service, _, _) = CreateService();

        TaskHandle? handle = taskId is null ? null : new TaskHandle { TaskId = taskId };

        Assert.Equal(
            WireRetCode.EInvalidHandle,
            (await service.Reset(new ResetUpdateTaskRequest { Task = handle }, Context)).Status.RetCode);
        Assert.Equal(
            WireRetCode.EInvalidHandle,
            (await service.PrepareUpdate(new PrepareUpdateRequest { Task = handle }, Context))
                .Status.RetCode);
        Assert.Equal(
            WireRetCode.EInvalidHandle,
            (await service.Update(new UpdateRequest { Task = handle }, Context)).Status.RetCode);
        Assert.Equal(
            WireRetCode.EInvalidHandle,
            (await service.ReleaseUpdateTask(new ReleaseUpdateTaskRequest { Task = handle }, Context))
                .Status.RetCode);
    }

    [Fact]
    public async Task ResetForwardsTheTasksOwnBusyAnswer()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();
        surface.Busy = true;

        ResetUpdateTaskResponse response = await service.Reset(
            new ResetUpdateTaskRequest { Task = handle },
            Context);

        Assert.Equal(WireRetCode.EBusy, response.Status.RetCode);
    }

    // ---- PrepareUpdate ---------------------------------------------------------------------------

    /// <summary>
    /// The data-object name these cases name so that the prepare has a source to run against.
    /// </summary>
    /// <remarks>
    /// A PREPARE MUST LEAVE THE TASK HOLDING A SOURCE, and PrepareUpdate is the only place either source can
    /// be set - so a request naming neither, against a task that holds neither, is refused with the very code
    /// its Update would have answered. These cases are about the DESCRIPTOR half of the call, so they name a
    /// source to get past that admission rule rather than asserting anything about it; the rule itself is
    /// pinned by its own case below.
    /// </remarks>
    private const string EvidencedDataObject = "dw_sqlite";

    private static TableUpdateContract Company(string name = "COMPANY") => new()
    {
        Name = name,
        Updatablecolumns = { "id", "name", "age", "address", "salary", "birth" },
        Keycolumns = { "id" },
        Identitycolumn = "id",
    };

    [Fact]
    public async Task TheSixFieldDescriptorRoundTripsInDeclarationOrderWithPresenceStated()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        TableUpdateContract stated = Company();
        stated.Updatewhere = 1L;
        stated.Updatekeyinplace = false;

        PrepareUpdateResponse response = await service.PrepareUpdate(
            new PrepareUpdateRequest
            {
                Task = handle,
                MultiTableUpdate = true,
                Tables = { stated },
                DataObject = EvidencedDataObject,
            },
            Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);

        RecordedAddCall add = Assert.Single(surface.Adds);
        Assert.True(add.SixArgumentForm);
        Assert.Equal("COMPANY", add.Name);
        Assert.Equal(["id", "name", "age", "address", "salary", "birth"], add.UpdatableColumns);
        Assert.Equal(["id"], add.KeyColumns);
        Assert.Equal("id", add.IdentityColumn);
        Assert.Equal(1L, add.UpdateWhere);
        Assert.False(add.UpdateKeyInPlace);
    }

    [Fact]
    public async Task AnUnsetUpdateWhereAndKeyInPlaceSurviveAsUnsetThroughTheFourArgumentForm()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        _ = await service.PrepareUpdate(
            new PrepareUpdateRequest
            {
                Task = handle,
                MultiTableUpdate = true,
                Tables = { Company() },
                DataObject = EvidencedDataObject,
            },
            Context);

        RecordedAddCall add = Assert.Single(surface.Adds);
        Assert.False(add.SixArgumentForm);
        Assert.Null(add.UpdateWhere);
        Assert.Null(add.UpdateKeyInPlace);
    }

    [Fact]
    public async Task AnExplicitZeroModeIsDistinguishableFromAnUnsetOne()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        TableUpdateContract zero = Company();
        zero.Updatewhere = 0L;

        _ = await service.PrepareUpdate(
            new PrepareUpdateRequest
            {
                Task = handle,
                MultiTableUpdate = true,
                Tables = { zero },
                DataObject = EvidencedDataObject,
            },
            Context);

        RecordedAddCall add = Assert.Single(surface.Adds);
        Assert.True(add.SixArgumentForm);
        Assert.Equal(0L, add.UpdateWhere);
        Assert.Null(add.UpdateKeyInPlace);
    }

    [Fact]
    public async Task StatingOnlyKeyInPlaceTakesTheSixArgumentFormWithTheModeStillAbsent()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        TableUpdateContract only = Company();
        only.Updatekeyinplace = false;

        _ = await service.PrepareUpdate(
            new PrepareUpdateRequest
            {
                Task = handle,
                MultiTableUpdate = true,
                Tables = { only },
                DataObject = EvidencedDataObject,
            },
            Context);

        RecordedAddCall add = Assert.Single(surface.Adds);
        Assert.True(add.SixArgumentForm);
        Assert.Null(add.UpdateWhere);
        Assert.False(add.UpdateKeyInPlace);
    }

    [Fact]
    public async Task AnEmptyIdentityColumnIsAcceptedAndTheThreeArmRejectionIsDelegated()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        TableUpdateContract noIdentity = Company();
        noIdentity.Identitycolumn = string.Empty;

        PrepareUpdateResponse accepted = await service.PrepareUpdate(
            new PrepareUpdateRequest
            {
                Task = handle,
                MultiTableUpdate = true,
                Tables = { noIdentity },
                DataObject = EvidencedDataObject,
            },
            Context);

        Assert.Equal(WireRetCode.Ok, accepted.Status.RetCode);
        Assert.Equal(string.Empty, Assert.Single(surface.Adds).IdentityColumn);

        // The refusal is whatever the collection answers - this boundary restates no arm of its own.
        surface.AddResult = RetCode.E_INVALID_ARGUMENT;

        PrepareUpdateResponse refused = await service.PrepareUpdate(
            new PrepareUpdateRequest
            {
                Task = handle,
                MultiTableUpdate = true,
                Tables = { Company(), Company("SECOND") },
                DataObject = EvidencedDataObject,
            },
            Context);

        Assert.Equal(WireRetCode.EInvalidArgument, refused.Status.RetCode);
        Assert.Contains("Update table 1", refused.Status.ErrorText, StringComparison.Ordinal);
        Assert.Contains("sqlupdate.sru:L84", refused.Status.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MultiTablePrepareCarriesEveryDescriptorInArrayOrderAndClearsFirst()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        PrepareUpdateResponse response = await service.PrepareUpdate(
            new PrepareUpdateRequest
            {
                Task = handle,
                MultiTableUpdate = true,
                Tables = { Company("FIRST"), Company("SECOND"), Company("THIRD") },
                DataObject = "d_company",
                SqlSyntax = "release 12.5;",
            },
            Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
        Assert.Equal(["FIRST", "SECOND", "THIRD"], surface.Adds.Select(static add => add.Name));
        Assert.True(surface.MultiTableSeen);
        Assert.Equal("d_company", surface.DataObjectSeen);
        Assert.Equal("release 12.5;", surface.SqlSyntaxSeen);

        // Clear, then the switch, then the adds - and the syntax LAST so it wins over the data object.
        Assert.Equal("ResetUpdatableTables", surface.Calls[0]);
        Assert.Equal("SetMultiTableUpdate", surface.Calls[1]);
        Assert.Equal("SetDataObject", surface.Calls[^2]);
        Assert.Equal("SetSqlSyntax", surface.Calls[^1]);
    }

    [Fact]
    public async Task MultiTableWithAnEmptyArrayIsRefusedWithTheLegacyDiagnostic()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        PrepareUpdateResponse response = await service.PrepareUpdate(
            new PrepareUpdateRequest { Task = handle, MultiTableUpdate = true },
            Context);

        Assert.Equal(WireRetCode.EInvalidArgument, response.Status.RetCode);
        Assert.Equal(UpdateWhereBuilder.NoUpdatableTableMessage, response.Status.ErrorText);
        Assert.Empty(surface.Calls);
    }

    [Fact]
    public async Task SingleTableWithAnEmptyArrayIsOrdinaryAndForcesNoPrepare()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        // THE SOURCE IS NAMED AND THE ARRAY IS EMPTY, which is exactly the shape a single-table caller
        // sends: the carrier's own compiled definition governs the update table, the key columns and the
        // identity column, and no descriptor is prepared at all.
        PrepareUpdateResponse response = await service.PrepareUpdate(
            new PrepareUpdateRequest
            {
                Task = handle,
                MultiTableUpdate = false,
                DataObject = EvidencedDataObject,
            },
            Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
        Assert.Empty(surface.Adds);
        Assert.False(surface.MultiTableSeen);
        Assert.Equal(EvidencedDataObject, surface.DataObjectSeen);
        Assert.Null(surface.SqlSyntaxSeen);
    }

    /// <summary>
    /// A prepare that would leave the task with no source at all is refused with the code its own
    /// <c>Update</c> would have answered, and nothing is recorded.
    /// </summary>
    /// <remarks>
    /// <b>A SUCCESS THE CALLER COULD NOT ACT ON.</b> The two sources are mutually exclusive and each setter
    /// clears the other, and this RPC is the ONLY place either can be set - <c>Update</c> carries the payload
    /// and nothing else, and <c>Reset</c> only clears. So a task left holding neither has exactly one
    /// reachable future: the no-source arm at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L326-L330</c>. This case pins
    /// that the code and the diagnostic are that arm's own, that the refusal is atomic, and that a prepare
    /// naming no source is STILL admitted once the task holds one - which is the incremental order a
    /// multi-table caller uses.
    /// </remarks>
    [Fact]
    public async Task APrepareThatWouldLeaveTheTaskWithoutASourceIsRefusedWithTheRunsOwnCode()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        PrepareUpdateResponse refused = await service.PrepareUpdate(
            new PrepareUpdateRequest { Task = handle, MultiTableUpdate = false },
            Context);

        Assert.Equal(WireRetCode.EInvalidDataobject, refused.Status.RetCode);
        Assert.Equal(SqlUpdateTask.InvalidDataObjectMessage, refused.Status.ErrorText);

        // ATOMIC: nothing was cleared, nothing was switched, nothing was recorded.
        Assert.Empty(surface.Calls);
        Assert.Empty(surface.Adds);

        // ONCE A SOURCE IS HELD, A SOURCE-FREE PREPARE IS ORDINARY AGAIN - the second call of the only
        // incremental order that can work.
        PrepareUpdateResponse seeded = await service.PrepareUpdate(
            new PrepareUpdateRequest { Task = handle, DataObject = EvidencedDataObject },
            Context);

        Assert.Equal(WireRetCode.Ok, seeded.Status.RetCode);

        PrepareUpdateResponse descriptorsOnly = await service.PrepareUpdate(
            new PrepareUpdateRequest { Task = handle, MultiTableUpdate = true, Tables = { Company() } },
            Context);

        Assert.Equal(WireRetCode.Ok, descriptorsOnly.Status.RetCode);
        Assert.Equal("COMPANY", Assert.Single(surface.Adds).Name);
    }

    /// <summary>
    /// A descriptor sent with the multi-table switch off that names a DIFFERENT update table from the one
    /// the data object's definition declares is REFUSED, and nothing is recorded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>WITH THE SWITCH OFF THE ARRAY IS NEVER APPLIED</b> - the oracle's <c>_of_UpdatePrepare</c> has one
    /// caller and it is inside the multi-table branch [<c>:L365</c> versus <c>:L371</c>] - so the data
    /// object's own definition governs the update table. In process that was harmless because the caller
    /// could see which datastore it had loaded. Across the boundary it is not: the caller names table X, is
    /// told OK, and the update writes to whatever the definition declares, reported as a success with
    /// nothing in the response to reveal the redirection.
    /// </para>
    /// <para>
    /// <b>THE REFUSAL IS SCOPED TO THE MISDIRECTION AND NOTHING ELSE.</b> A descriptor that AGREES with the
    /// definition is admitted: it is still inert, but inert and agreeing redirects no write - and it is the
    /// shape a caller that derives its descriptor FROM the definition necessarily sends, which is what this
    /// system's own DataServices consumer does on every update. Both halves are pinned here, because a
    /// refusal that covered the agreeing shape would break the only cross-service update path in the estate.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ADescriptorNamingAnotherTableWithTheSwitchOffIsRefusedAndAnAgreeingOneIsNot()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        PrepareUpdateResponse refused = await service.PrepareUpdate(
            new PrepareUpdateRequest
            {
                Task = handle,
                MultiTableUpdate = false,
                DataObject = EvidencedDataObject,
                Tables = { Company("QA_PROBE") },
            },
            Context);

        Assert.Equal(WireRetCode.EInvalidArgument, refused.Status.RetCode);
        Assert.Equal(UpdateService.DescriptorsWithoutMultiTableDiagnostic, refused.Status.ErrorText);

        // NOTHING WAS RECORDED, so the refusal leaves the task exactly as it was - no clear, no switch, no
        // add, no source, and in particular no half-applied descriptor array.
        Assert.Empty(surface.Calls);
        Assert.Empty(surface.Adds);

        // AND THE REFUSAL QUOTES NO CALLER VALUE. Neither table name is echoed (constraint C-F).
        Assert.DoesNotContain("QA_PROBE", refused.Status.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain("COMPANY", refused.Status.ErrorText, StringComparison.Ordinal);

        // AN AGREEING DESCRIPTOR IS ADMITTED, and its case is not what decides it: a table name is an
        // identifier and SQLite compares identifiers without regard to case.
        PrepareUpdateResponse agreeing = await service.PrepareUpdate(
            new PrepareUpdateRequest
            {
                Task = handle,
                MultiTableUpdate = false,
                DataObject = EvidencedDataObject,
                Tables = { Company("company") },
            },
            Context);

        Assert.Equal(WireRetCode.Ok, agreeing.Status.RetCode);
        Assert.Equal("company", Assert.Single(surface.Adds).Name);
    }

    /// <summary>
    /// The misdirection check reads the data object the task ALREADY holds when the request names none.
    /// </summary>
    /// <remarks>
    /// Both source setters are presence-gated, so an unstated source survives from an earlier prepare and is
    /// what the update will actually run against. Comparing only against the request's own field would let a
    /// second prepare slip a contradicting descriptor past the check.
    /// </remarks>
    [Fact]
    public async Task TheMisdirectionCheckUsesTheSourceTheTaskAlreadyHolds()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        Assert.Equal(
            WireRetCode.Ok,
            (await service.PrepareUpdate(
                new PrepareUpdateRequest { Task = handle, DataObject = EvidencedDataObject },
                Context)).Status.RetCode);

        PrepareUpdateResponse refused = await service.PrepareUpdate(
            new PrepareUpdateRequest
            {
                Task = handle,
                MultiTableUpdate = false,
                Tables = { Company("QA_PROBE") },
            },
            Context);

        Assert.Equal(WireRetCode.EInvalidArgument, refused.Status.RetCode);
        Assert.Empty(surface.Adds);
    }

    /// <summary>
    /// A descriptor naming a column the governing definition does not declare is refused with the oracle's
    /// own invalid-column arm, even with the multi-table switch off.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE SAME ANSWER THE SWITCH-ON PATH GIVES, ARRIVING ONE CALL EARLIER. With the switch on, the preparer
    /// resolves each key column and fails with <c>E_INTERNAL_ERROR</c> and 无效的列名: plus the name
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L118-L122</c>], and the
    /// carrier's own <c>Modify</c> refuses an unknown updatable column for the same reason. With the switch
    /// off the descriptor is inert, so nothing used to resolve it at all and the update proceeded on the
    /// definition's own columns while reporting success - which is what made an unknown column name look
    /// accepted.
    /// </para>
    /// <para>
    /// ONLY EXISTENCE IS CHECKED, NEVER THE FLAGS. A caller may legitimately declare a subset of the
    /// definition's updatable or key columns, and with the switch off the definition's own flags govern
    /// anyway - so a flag comparison would refuse requests that are neither wrong nor harmful.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("updatable")]
    [InlineData("key")]
    [InlineData("identity")]
    public async Task ADescriptorNamingAnUndeclaredColumnIsRefusedWithTheOraclesOwnArm(string position)
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        TableUpdateContract descriptor = Company();

        switch (position)
        {
            case "updatable":
                descriptor.Updatablecolumns.Add("no_such_column");
                break;

            case "key":
                descriptor.Keycolumns.Add("no_such_column");
                break;

            default:
                descriptor.Identitycolumn = "no_such_column";
                break;
        }

        PrepareUpdateResponse refused = await service.PrepareUpdate(
            new PrepareUpdateRequest
            {
                Task = handle,
                MultiTableUpdate = false,
                DataObject = EvidencedDataObject,
                Tables = { descriptor },
            },
            Context);

        Assert.Equal(WireRetCode.EInternalError, refused.Status.RetCode);
        Assert.Equal(
            UpdateWhereBuilder.InvalidColumnNameMessage + "no_such_column",
            refused.Status.ErrorText);

        // ATOMIC: nothing was cleared, switched, added or installed.
        Assert.Empty(surface.Calls);
        Assert.Empty(surface.Adds);
    }

    /// <summary>
    /// A descriptor declaring a SUBSET of the definition's columns is admitted, in any letter case.
    /// </summary>
    /// <remarks>
    /// The existence test must not become a flag or completeness test: a caller that declares only the
    /// columns it intends to write is neither wrong nor harmful, and with the switch off the definition's own
    /// flags govern regardless.
    /// </remarks>
    [Fact]
    public async Task ADescriptorDeclaringASubsetOfDeclaredColumnsIsAdmitted()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        TableUpdateContract subset = new()
        {
            Name = "COMPANY",
            Updatablecolumns = { "NAME", "salary" },
            Keycolumns = { "ID" },
            Identitycolumn = string.Empty,
        };

        PrepareUpdateResponse admitted = await service.PrepareUpdate(
            new PrepareUpdateRequest
            {
                Task = handle,
                MultiTableUpdate = false,
                DataObject = EvidencedDataObject,
                Tables = { subset },
            },
            Context);

        Assert.Equal(WireRetCode.Ok, admitted.Status.RetCode);
        Assert.Equal("COMPANY", Assert.Single(surface.Adds).Name);
    }

    /// <summary>
    /// A data object that resolves to no definition is not compared against, and neither is a request whose
    /// source is a SQL syntax.
    /// </summary>
    /// <remarks>
    /// A DELIBERATE LIMIT RATHER THAN AN OVERSIGHT. Neither can misdirect a write - an update against an
    /// unresolvable definition fails for want of an update table before any statement reaches the engine, and
    /// a syntax-sourced update takes its table from the syntax, which this boundary does not parse. Refusing
    /// either would narrow the contract without protecting anything.
    /// </remarks>
    [Fact]
    public async Task AnUnresolvableDefinitionAndASyntaxSourceAreNotCompared()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        PrepareUpdateResponse unknown = await service.PrepareUpdate(
            new PrepareUpdateRequest
            {
                Task = handle,
                MultiTableUpdate = false,
                DataObject = "d_not_registered",
                Tables = { Company("QA_PROBE") },
            },
            Context);

        Assert.Equal(WireRetCode.Ok, unknown.Status.RetCode);

        PrepareUpdateResponse syntax = await service.PrepareUpdate(
            new PrepareUpdateRequest
            {
                Task = handle,
                MultiTableUpdate = false,
                SqlSyntax = "release 12.5;",
                Tables = { Company("QA_PROBE") },
            },
            Context);

        Assert.Equal(WireRetCode.Ok, syntax.Status.RetCode);
        Assert.Equal(2, surface.Adds.Count);
    }

    [Fact]
    public async Task PrepareUpdateReportsBusyWithoutTouchingAnythingElse()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();
        surface.Busy = true;

        PrepareUpdateResponse response = await service.PrepareUpdate(
            new PrepareUpdateRequest
            {
                Task = handle,
                MultiTableUpdate = true,
                Tables = { Company() },
                DataObject = EvidencedDataObject,
            },
            Context);

        Assert.Equal(WireRetCode.EBusy, response.Status.RetCode);
        Assert.Equal(["ResetUpdatableTables"], surface.Calls);
    }

    // ---- Update ----------------------------------------------------------------------------------

    private static IdentityResolutionOutcome Resolution(
        UpdateRowCounts counts,
        params ResolvedIdentityColumnData[] blocks) =>
        new() { Counts = counts, Identity = [.. blocks] };

    [Fact]
    public async Task UpdatePassesTheAutocommitPresenceThePayloadAndTheToken()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();
        surface.Result = new UpdateRunResult
        {
            Code = RetCode.OK,
            Outcome = UpdateOutcome.Succeeded(Resolution(new UpdateRowCounts(1, 0, 0))),
        };

        CarrierState carrier = new() { Processing = 1 };

        UpdateResponse unset = await service.Update(
            new UpdateRequest { Task = handle, UpdateData = carrier, UpdateRows = 3 },
            Context);

        Assert.Equal(WireRetCode.Ok, unset.Status.RetCode);
        Assert.Null(surface.AutoCommitSeen);
        Assert.Same(carrier, surface.PayloadSeen);
        Assert.Equal(3L, surface.PayloadRowsSeen);
        Assert.DoesNotContain("SetAutoCommit", surface.Calls);

        _ = await service.Update(
            new UpdateRequest { Task = handle, Autocommit = true, UpdateRows = 0 },
            Context);

        Assert.True(surface.AutoCommitSeen);
        Assert.Contains("SetAutoCommit", surface.Calls);
    }

    [Fact]
    public async Task UpdateReportsBusyFromEitherMutatorBeforeExecuting()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();
        surface.Busy = true;

        UpdateResponse response = await service.Update(
            new UpdateRequest { Task = handle, Autocommit = false },
            Context);

        Assert.Equal(WireRetCode.EBusy, response.Status.RetCode);
        Assert.Equal(["SetAutoCommit"], surface.Calls);
        Assert.DoesNotContain("Execute", surface.Calls);
    }

    [Fact]
    public async Task CountsAccumulateAndOneIdentityBlockPerTableSurvivesUnsorted()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        ResolvedIdentityColumnData first = new(1, [7L, 4L, 9L], [3L, 1L]);
        ResolvedIdentityColumnData second = new(2, [null, 5L], []);

        surface.Result = new UpdateRunResult
        {
            Code = RetCode.OK,
            Outcome = UpdateOutcome.Succeeded(Resolution(new UpdateRowCounts(5, 2, 1))),
            Counts = new UpdateRowCounts(5, 2, 1),
            Identity = [first, second],
        };

        UpdateResponse response = await service.Update(
            new UpdateRequest { Task = handle },
            Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
        Assert.Equal(5L, response.Counts.Inserted);
        Assert.Equal(2L, response.Counts.Updated);
        Assert.Equal(1L, response.Counts.Deleted);

        Assert.Equal(2, response.Identity.Count);
        Assert.Equal(1L, response.Identity[0].IdentityColumnId);
        Assert.Equal([7L, 4L, 9L], response.Identity[0].PrimaryValues.Select(static v => v.Value));
        Assert.Equal([3L, 1L], response.Identity[0].FilterValues.Select(static v => v.Value));

        Assert.Equal(2L, response.Identity[1].IdentityColumnId);
        Assert.False(response.Identity[1].PrimaryValues[0].HasValue);
        Assert.Equal(5L, response.Identity[1].PrimaryValues[1].Value);
        Assert.Empty(response.Identity[1].FilterValues);
    }

    [Fact]
    public async Task AnEmptyIdentityPayloadIsAValidSuccess()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();
        surface.Result = new UpdateRunResult
        {
            Code = RetCode.OK,
            Outcome = UpdateOutcome.Succeeded(Resolution(default)),
        };

        UpdateResponse response = await service.Update(new UpdateRequest { Task = handle }, Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
        Assert.Empty(response.Identity);
        Assert.NotNull(response.Counts);
        Assert.Equal(0L, response.Counts.Inserted);
    }

    [Fact]
    public async Task ASuccessfulClassificationWithAFailedEpilogueReportsNoCounts()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();
        surface.Result = new UpdateRunResult
        {
            Code = RetCode.CANCELLED,
            Outcome = UpdateOutcome.Succeeded(Resolution(new UpdateRowCounts(4, 0, 0))),
            Counts = new UpdateRowCounts(4, 0, 0),
            Identity = [new ResolvedIdentityColumnData(1, [1L], [])],
        };

        UpdateResponse response = await service.Update(new UpdateRequest { Task = handle }, Context);

        Assert.Equal(WireRetCode.Cancelled, response.Status.RetCode);
        Assert.Null(response.Counts);
        Assert.Empty(response.Identity);
    }

    [Fact]
    public async Task ACleanVetoYieldsCancelledWithNoTextAndNoDriverDetail()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();
        surface.Result = new UpdateRunResult
        {
            Code = RetCode.CANCELLED,
            Outcome = UpdateOutcome.Cancelled(updateInvoked: false),
            LastDbError = new DbErrorData { SqlDbCode = -1 }.ToDbError(),
            ErrorText = "should not travel",
        };

        UpdateResponse response = await service.Update(new UpdateRequest { Task = handle }, Context);

        Assert.Equal(WireRetCode.Cancelled, response.Status.RetCode);
        Assert.Equal(string.Empty, response.Status.ErrorText);
        Assert.Null(response.Status.DbError);
    }

    [Fact]
    public async Task AVetoWithATransactionFailureYieldsADatabaseErrorCarryingBothChannels()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        DbErrorData raised = DbErrorData.FromTransaction(-1, "SQLITE_BUSY");

        surface.Result = new UpdateRunResult
        {
            Code = RetCode.E_DB_ERROR,
            Outcome = UpdateOutcome.DatabaseError(raised, errorReported: true, errorText: string.Empty),
            ErrorText = string.Empty,
        };

        UpdateResponse response = await service.Update(new UpdateRequest { Task = handle }, Context);

        Assert.Equal(WireRetCode.EDbError, response.Status.RetCode);
        Assert.NotNull(response.Status.DbError);
        Assert.Equal(-1L, response.Status.DbError.Sqldbcode);
        Assert.Equal("SQLITE_BUSY", response.Status.DbError.Sqlerrtext);
        Assert.Equal(string.Empty, response.Status.ErrorText);
    }

    [Fact]
    public async Task TheSentinelUpdateTableArmSurfacesTheSynthesizedPayloadFaithfully()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        DbErrorData sentinel = DbErrorData.NoUpdatableTable();

        surface.Result = new UpdateRunResult
        {
            Code = RetCode.E_DB_ERROR,
            Outcome = UpdateOutcome.DatabaseError(
                sentinel,
                errorReported: true,
                errorText: DbErrorMessages.NoUpdatableTable),
            ErrorText = DbErrorMessages.NoUpdatableTable,
        };

        UpdateResponse response = await service.Update(new UpdateRequest { Task = handle }, Context);

        Assert.Equal(WireRetCode.EDbError, response.Status.RetCode);
        Assert.Equal(DbErrorMessages.NoUpdatableTable, response.Status.ErrorText);
        Assert.Equal(DbErrorMessages.NoUpdatableTable, response.Status.DbError.Sqlerrtext);
        Assert.Equal(-1L, response.Status.DbError.Sqldbcode);
        Assert.Equal(string.Empty, response.Status.DbError.Sqlsyntax);
        Assert.Equal(DwBuffer.Primary, response.Status.DbError.Buffer);
        Assert.Equal(0L, response.Status.DbError.Row);
    }

    [Fact]
    public async Task AnUnattemptedRunReportsTheRunsCodeTextAndLatchedDriverError()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        surface.Result = new UpdateRunResult
        {
            Code = RetCode.E_INVALID_TRANSACTION,
            Outcome = null,
            ErrorText = "acquisition failed",
            LastDbError = DbErrorData.FromTransaction(-42, "no connection").ToDbError(),
        };

        UpdateResponse response = await service.Update(new UpdateRequest { Task = handle }, Context);

        Assert.Equal(WireRetCode.EInvalidTransaction, response.Status.RetCode);
        Assert.Equal("acquisition failed", response.Status.ErrorText);
        Assert.Equal(-42L, response.Status.DbError.Sqldbcode);
        Assert.Null(response.Counts);
    }

    /// <summary>
    /// 🔴 A conflict the REAL classifier produced from a REAL measurement - rather than one assembled by
    /// the test - is relayed as <c>Aborted</c> with its detail intact.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>WHY THE OUTCOME IS NOT HAND-BUILT HERE.</b> The sibling exercise below builds a
    /// <c>ConflictDetected</c> outcome directly, which proves the relay but cannot prove that the classifier
    /// ever produces one on the path a caller takes. Two defects survived exactly that gap: the
    /// measurement was read before the update had run, and the classifier consulted it only after the
    /// success arm had returned. This exercise drives the shipped <see cref="ConflictDetector"/> with a
    /// target that reports a CLAIMED SUCCESS over a zero-row match - the shape a stale write really has -
    /// and asserts the service relays what comes out.
    /// </para>
    /// <para>
    /// The database itself is exercised in
    /// <c>PersistenceRuntimeTests.AStaleRowWorkflowAnswersAbortedWithStorageStateAndNeverOverwrites</c>,
    /// which runs the same classifier against a real competing write; this one covers the C-06 hop.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AClassifierProducedConflictIsRelayedAsAbortedFromAClaimedSuccess()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        ConflictRow conflicting = new()
        {
            Buffer = DwBuffer.Primary,
            Row = 1,
            ItemStatus = ItemStatus.DataModified,
            CurrentValues = { new ColumnValue { ColumnId = 3, Value = new AnyValue { Int64Value = 99 } } },
            OriginalValues = { new ColumnValue { ColumnId = 3, Value = new AnyValue { Int64Value = 32 } } },
        };

        RelayUpdateTarget target = new()
        {
            // THE UPDATE CLAIMS SUCCESS, which is what a zero-row match really answers: no provider raised
            // anything, so the DataWindow contract reports 1.
            UpdateResult = DataWindowBufferStore.DataStoreSuccess,
            Evidence = new ConcurrencyEvidence
            {
                RowsExpected = 1L,
                RowsMatched = 0L,
                Rows = [conflicting],
            },
        };

        UpdateOutcome classified = new ConflictDetector(SqlRedactor.Instance).Classify(new UpdateAttempt
        {
            Transaction = new RelayUpdateTransaction(),
            Target = target,
            Identity = new IdentityTableSurfaces(target, target),
            Errors = new RelayErrorSink(),
            Cancellation = TestContext.Current.CancellationToken,
        });

        Assert.Equal(UpdateOutcomeKind.Conflict, classified.Kind);

        surface.Result = new UpdateRunResult { Code = classified.Code, Outcome = classified };

        RpcException raised = await Assert.ThrowsAsync<RpcException>(
            () => service.Update(new UpdateRequest { Task = handle }, Context));

        Assert.Equal(StatusCode.Aborted, raised.StatusCode);

        byte[]? raw = raised.Trailers.GetValueBytes(ConflictDetector.RichErrorTrailerKey);
        Assert.NotNull(raw);

        RichErrorTrailer trailer = RichErrorTrailer.Parser.ParseFrom(raw);
        Assert.Equal(RichErrorTrailer.DetailOneofCase.Conflict, trailer.DetailCase);
        Assert.Equal(RetCode.E_DB_ERROR, trailer.RetCode);

        ConflictRow relayed = Assert.Single(trailer.Conflict.Rows);
        Assert.Equal(99L, relayed.CurrentValues[0].Value.Int64Value);
        Assert.Equal(32L, relayed.OriginalValues[0].Value.Int64Value);

        // THE MEASUREMENT WAS READ AFTER THE UPDATE RAN, never before it.
        Assert.Equal(1, target.UpdateCalls);
        Assert.True(target.EvidenceReadAfterUpdate);
    }

    /// <summary>
    /// The update target one relayed classification runs against: it claims success and reports a zero-row
    /// match, which is the shape a stale write has.
    /// </summary>
    private sealed class RelayUpdateTarget : IUpdateTarget, IIdentityColumnMetadata, IIdentityValueSource
    {
        internal long UpdateResult { get; set; } = DataWindowBufferStore.DataStoreSuccess;

        internal ConcurrencyEvidence? Evidence { get; set; }

        internal int UpdateCalls { get; private set; }

        /// <summary>Whether every measurement read happened after the update had run.</summary>
        internal bool EvidenceReadAfterUpdate { get; private set; } = true;

        public void ClearState()
        {
        }

        public long Update(bool acceptText, bool resetFlag, CancellationToken cancellationToken = default)
        {
            UpdateCalls++;

            return UpdateResult;
        }

        public ConcurrencyEvidence? CaptureConcurrencyEvidence()
        {
            if (UpdateCalls == 0)
            {
                EvidenceReadAfterUpdate = false;

                return null;
            }

            return Evidence;
        }

        public string DescribeUpdateTable() => "COMPANY";

        public int GetColumnCount() => 0;

        public string DescribeColumnIdentity(string identityProperty) => string.Empty;

        public string DescribeColumnDbName(string dbNameProperty) => string.Empty;

        public long GetInsertedCount() => 0L;

        public long GetUpdatedCount() => 0L;

        public long GetDeletedCount() => 0L;

        public long RowCount() => 0L;

        public long FilteredCount() => 0L;

        public ItemStatus GetItemStatus(long row, int columnIndex, DwBuffer buffer) =>
            ItemStatus.NotModified;

        public long? GetItemNumber(long row, int columnNumber) => null;

        public long? GetItemNumber(long row, int columnNumber, DwBuffer buffer, bool originalValue) => null;
    }

    /// <summary>The transaction surface a relayed classification reads: the provider raised nothing.</summary>
    private sealed class RelayUpdateTransaction : IUpdateTransaction
    {
        public long SqlCode => 0L;

        public long SqlDbCode => 0L;

        public string SqlErrText => string.Empty;

        public bool IsFailed() => false;

        public void ClearState()
        {
        }

        public long OnBeforeUpdate() => RetCode.OK;

        public void OnAfterUpdate(long updateResult)
        {
        }
    }

    /// <summary>Swallows the two error channels; this exercise asserts the relayed status, not the raises.</summary>
    private sealed class RelayErrorSink : IUpdateErrorSink
    {
        public void OnDbError(in DbErrorData error)
        {
        }

        public void OnError(long code, string errorText)
        {
        }
    }

    [Fact]
    public async Task AConcurrencyMismatchIsAbortedWithAPopulatedConflictDetail()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        ConflictDetail detail = new()
        {
            UpdateTable = "COMPANY",
            RowsExpected = 1,
            RowsMatched = 0,
        };

        detail.Rows.Add(new ConflictRow
        {
            Buffer = DwBuffer.Primary,
            Row = 1,
            ItemStatus = ItemStatus.DataModified,
            CurrentValues = { new ColumnValue { ColumnId = 3, Value = new AnyValue { Int64Value = 41 } } },
            OriginalValues = { new ColumnValue { ColumnId = 3, Value = new AnyValue { Int64Value = 40 } } },
        });

        surface.Result = new UpdateRunResult
        {
            Code = RetCode.E_DB_ERROR,
            Outcome = UpdateOutcome.ConflictDetected(
                detail,
                DbErrorData.FromTransaction(-1, "conflict"),
                updateTable: "COMPANY",
                updateResult: -1L,
                observedByHook: 1L,
                overrideApplied: true),
        };

        RpcException raised = await Assert.ThrowsAsync<RpcException>(
            () => service.Update(new UpdateRequest { Task = handle }, Context));

        Assert.Equal(StatusCode.Aborted, raised.StatusCode);

        byte[]? raw = raised.Trailers.GetValueBytes(ConflictDetector.RichErrorTrailerKey);
        Assert.NotNull(raw);

        RichErrorTrailer trailer = RichErrorTrailer.Parser.ParseFrom(raw);
        Assert.Equal(RichErrorTrailer.DetailOneofCase.Conflict, trailer.DetailCase);
        Assert.Equal(RetCode.E_DB_ERROR, trailer.RetCode);

        ConflictRow row = Assert.Single(trailer.Conflict.Rows);
        Assert.Equal("COMPANY", trailer.Conflict.UpdateTable);
        Assert.Equal(1L, trailer.Conflict.RowsExpected);
        Assert.Equal(0L, trailer.Conflict.RowsMatched);
        Assert.Equal(41L, row.CurrentValues[0].Value.Int64Value);
        Assert.Equal(40L, row.OriginalValues[0].Value.Int64Value);
    }

    [Fact]
    public async Task AConflictWithoutADetailIsNeverRaisedAsAnEmptyAborted()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        surface.Result = new UpdateRunResult
        {
            Code = RetCode.E_DB_ERROR,
            Outcome = UpdateOutcome.DatabaseError(null, errorReported: false) with
            {
                Kind = UpdateOutcomeKind.Conflict,
            },
            ErrorText = "narrowed without a detail",
        };

        UpdateResponse response = await service.Update(new UpdateRequest { Task = handle }, Context);

        Assert.Equal(WireRetCode.EDbError, response.Status.RetCode);
        Assert.Equal("narrowed without a detail", response.Status.ErrorText);
        Assert.Null(response.Counts);
    }

    [Fact]
    public async Task AnUnmappedOutcomeKindIsAStructuralFaultRatherThanAPlausibleStatus()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        surface.Result = new UpdateRunResult
        {
            Code = RetCode.OK,
            Outcome = UpdateOutcome.InvalidTransaction() with { Kind = UpdateOutcomeKind.None },
        };

        InvalidOperationException fault = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.Update(new UpdateRequest { Task = handle }, Context));

        Assert.Contains("does not map", fault.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheCallContextsCancellationTokenReachesTheRun()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        using CancellationTokenSource source = new();
        ServerCallContext cancellable = new FakeCallContext(source.Token);

        _ = await service.Update(new UpdateRequest { Task = handle }, cancellable);

        Assert.Equal(source.Token, surface.TokenSeen);
    }

    [Fact]
    public async Task EveryHandlerRejectsANullRequest()
    {
        (UpdateService service, _, _) = CreateService();

        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.CreateUpdateTask(null!, Context));
        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.ReleaseUpdateTask(null!, Context));
        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => service.Reset(null!, Context));
        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => service.PrepareUpdate(null!, Context));
        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => service.Update(null!, Context));
        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.Update(new UpdateRequest(), null!));
    }

    [Fact]
    public void TheConstructorRequiresEveryBehaviouralCollaborator()
    {
        Assert.Throws<ArgumentNullException>(() => new UpdateService(
                null!,
                new UpdateTaskRegistry(Options.Create(new PersistenceOptions()), TimeProvider.System),
                new DataObjectDefinitionRegistry()));
        Assert.Throws<ArgumentNullException>(() => new UpdateService(
                new FakeTaskFactory(),
                null!,
                new DataObjectDefinitionRegistry()));

        // THE DEFINITION REGISTRY IS REQUIRED TOO, because it is what the prepare boundary compares a
        // descriptor's update table against - a container that failed to register it would otherwise drop
        // that check silently.
        Assert.Throws<ArgumentNullException>(() => new UpdateService(
                new FakeTaskFactory(),
                new UpdateTaskRegistry(Options.Create(new PersistenceOptions()), TimeProvider.System),
                null!));
    }

    // ---- the Phase 7 review invariant ------------------------------------------------------------

    [Theory]
    [InlineData(RetCode.OK, WireRetCode.Ok)]
    [InlineData(RetCode.PREVENT, WireRetCode.Prevent)]
    [InlineData(RetCode.CANCELLED, WireRetCode.Cancelled)]
    [InlineData(RetCode.E_INVALID_ARGUMENT, WireRetCode.EInvalidArgument)]
    [InlineData(RetCode.E_INVALID_OBJECT, WireRetCode.EInvalidObject)]
    [InlineData(RetCode.E_INVALID_TRANSACTION, WireRetCode.EInvalidTransaction)]
    [InlineData(RetCode.E_INVALID_DATA, WireRetCode.EInvalidData)]
    [InlineData(RetCode.E_INVALID_HANDLE, WireRetCode.EInvalidHandle)]
    [InlineData(RetCode.E_BUSY, WireRetCode.EBusy)]
    [InlineData(RetCode.E_INTERNAL_ERROR, WireRetCode.EInternalError)]
    [InlineData(RetCode.E_DB_ERROR, WireRetCode.EDbError)]
    public void TheInProcessAlgebraAndTheWireEnumAgreeValueForValue(long inProcess, WireRetCode wire)
    {
        Assert.Equal(wire, UpdateWireCodes.ToWireRetCode(inProcess));
        Assert.Equal((long)(int)wire, inProcess);
        Assert.Equal(wire, UpdateWireCodes.Status(inProcess).RetCode);
    }

    [Fact]
    public void AStatusCarryingNoTextNeverProjectsANullString()
    {
        OperationStatus bare = UpdateWireCodes.Status(RetCode.OK);
        OperationStatus normalized = UpdateWireCodes.Status(RetCode.OK, null);

        Assert.Equal(string.Empty, bare.ErrorText);
        Assert.Equal(string.Empty, normalized.ErrorText);
        Assert.Null(bare.DbError);
        Assert.Null(normalized.DbError);
    }
    // ---- THE OPERATION LEASE - one prepare, update or reset at a time, and no teardown under one ----

    [Fact]
    public async Task EveryMutatingRpcIsRefusedWhileAnUpdateIsInFlight()
    {
        // THE FINDING THIS PINS. The oracle's caller-side guard is `if of_IsBusy() then return E_BUSY`
        // [n_cst_threading_task_sqlupdate.sru:L223, :L236], and in process it cannot be raced because the
        // caller and the task are one thread of control. Across a request boundary two calls can both
        // observe "not busy" and both proceed, so the second one's descriptor array, payload or reset
        // lands on the task the first is already executing. The guard is therefore taken as a LEASE, and
        // this asserts that every mutating RPC goes through it.
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        TaskCompletionSource gate = new();
        surface.Gate = gate;

        // The update runs on another thread and blocks inside the worker, holding the lease.
        Task<UpdateResponse> inFlight = Task.Run(
            () => service.Update(new UpdateRequest { Task = handle }, Context),
            TestContext.Current.CancellationToken);

        await surface.Entered.Task;

        // ALL FOUR refusals carry E_BUSY - retryable advice - and none of them reached the task.
        int callsBefore = surface.Calls.Count;

        Assert.Equal(
            WireRetCode.EBusy,
            (await service.Reset(new ResetUpdateTaskRequest { Task = handle }, Context)).Status.RetCode);
        Assert.Equal(
            WireRetCode.EBusy,
            (await service.PrepareUpdate(new PrepareUpdateRequest { Task = handle, Tables = { Company() } },
                Context)).Status.RetCode);
        Assert.Equal(
            WireRetCode.EBusy,
            (await service.Update(new UpdateRequest { Task = handle }, Context)).Status.RetCode);

        // The refusal is the ADAPTER's, taken before delegation - so the surface saw nothing new. That is
        // the whole difference between a lease and a re-read of a flag.
        Assert.Equal(callsBefore, surface.Calls.Count);

        gate.SetResult();
        Assert.Equal(WireRetCode.Ok, (await inFlight).Status.RetCode);

        // And the lease was given back, so the very same call now succeeds unchanged.
        Assert.Equal(
            WireRetCode.Ok,
            (await service.Reset(new ResetUpdateTaskRequest { Task = handle }, Context)).Status.RetCode);
    }

    [Fact]
    public async Task ABusyRefusalCarriesTheRetryableDiagnosticAndNotTheUnknownHandleOne()
    {
        // TWO REASONS, TWO CODES. E_BUSY says "retry"; E_INVALID_HANDLE says "do not". Conflating them
        // would either send a caller into a loop that can never succeed or make it give up on a task that
        // was merely busy.
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        TaskCompletionSource gate = new();
        surface.Gate = gate;

        Task<UpdateResponse> inFlight = Task.Run(
            () => service.Update(new UpdateRequest { Task = handle }, Context),
            TestContext.Current.CancellationToken);

        await surface.Entered.Task;

        UpdateResponse refused = await service.Update(new UpdateRequest { Task = handle }, Context);

        Assert.Equal(WireRetCode.EBusy, refused.Status.RetCode);
        Assert.Contains("in flight", refused.Status.ErrorText, StringComparison.Ordinal);
        Assert.Contains("Retry", refused.Status.ErrorText, StringComparison.Ordinal);

        gate.SetResult();
        _ = await inFlight;
    }

    [Fact]
    public async Task AReleaseArrivingDuringAnUpdateDefersTheTeardownToTheUpdatesExit()
    {
        // HAZARD 1 OF docs/PB多线程绕坑提示.md, at a request boundary: destroying an object with work still
        // pending against it faults. The release therefore records itself, unregisters the handle and
        // hands the teardown to whichever path finishes last - which here is the update.
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, UpdateTaskRegistry registry) =
            await CreateTaskAsync();

        TaskCompletionSource gate = new();
        surface.Gate = gate;

        Task<UpdateResponse> inFlight = Task.Run(
            () => service.Update(new UpdateRequest { Task = handle }, Context),
            TestContext.Current.CancellationToken);

        await surface.Entered.Task;

        // The release succeeds - the handle is retired - but it MUST NOT dispose.
        Assert.Equal(
            WireRetCode.Ok,
            (await service.ReleaseUpdateTask(new ReleaseUpdateTaskRequest { Task = handle }, Context))
                .Status.RetCode);

        Assert.False(surface.Disposed);
        Assert.False(registry.TryResolve(handle, out _));

        gate.SetResult();
        _ = await inFlight;

        // The update's exit performed the teardown, exactly once.
        Assert.True(surface.Disposed);

        // A second release finds nothing, and answers so.
        Assert.Equal(
            WireRetCode.EInvalidHandle,
            (await service.ReleaseUpdateTask(new ReleaseUpdateTaskRequest { Task = handle }, Context))
                .Status.RetCode);
    }

    [Fact]
    public async Task AReleaseWithNothingInFlightTearsTheTaskDownItself()
    {
        // The ordinary case, and the other half of the handoff: with no operation to hand it to, the
        // releaser disposes. Asserted alongside the deferred case so the pair cannot drift into either
        // "nobody disposes" or "both dispose".
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        Assert.False(surface.Disposed);

        Assert.Equal(
            WireRetCode.Ok,
            (await service.ReleaseUpdateTask(new ReleaseUpdateTaskRequest { Task = handle }, Context))
                .Status.RetCode);

        Assert.True(surface.Disposed);
    }

    [Fact]
    public async Task ARequestArrivingAfterAReleaseIsAnUnknownHandleRatherThanBusy()
    {
        (UpdateService service, _, TaskHandle handle, _) = await CreateTaskAsync();

        Assert.Equal(
            WireRetCode.Ok,
            (await service.ReleaseUpdateTask(new ReleaseUpdateTaskRequest { Task = handle }, Context))
                .Status.RetCode);

        Assert.Equal(
            WireRetCode.EInvalidHandle,
            (await service.Reset(new ResetUpdateTaskRequest { Task = handle }, Context)).Status.RetCode);
        Assert.Equal(
            WireRetCode.EInvalidHandle,
            (await service.Update(new UpdateRequest { Task = handle }, Context)).Status.RetCode);
        Assert.Equal(
            WireRetCode.EInvalidHandle,
            (await service.PrepareUpdate(new PrepareUpdateRequest { Task = handle }, Context))
                .Status.RetCode);
    }

    [Fact]
    public async Task TheRequestsCancellationTokenReachesTheWorker()
    {
        // F-11 for this contract: the worker side is synchronous by contract, so what the token buys is
        // that a statement is not ISSUED for a caller that has gone, and that a multi-row apply stops
        // between rows. Both live below this seam; what is asserted here is that the request's own token
        // - not None, and not a fresh one - is what arrives.
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        using CancellationTokenSource source = new();
        FakeCallContext context = new(source.Token);

        _ = await service.Update(new UpdateRequest { Task = handle }, context);

        Assert.Equal(source.Token, surface.TokenSeen);
    }

    // ---- the session lifecycle: the publication window and the disposal handoff -------------------
    //
    //  TWO PROPERTIES, BOTH OF WHICH FAILED BEFORE THIS SECTION EXISTED.
    //
    //    1. A create whose publication window reports the session retiring is REFUSED, and the surface it
    //       had already built is disposed rather than leaked. Publishing there would leave a task holding a
    //       pooled transaction that EndSession has handed back - and the pool may already have re-issued it
    //       to a different session [n_cst_thread_trans_pool.sru:L94-L114].
    //    2. The registry's purge, drain and reclaim go through the release/disposal handoff, so a task with
    //       an operation in flight is not torn down underneath itself; destroying an object with work still
    //       pending against it is hazard 1 [docs/PB多线程绕坑提示.md].
    //
    //  Both are deterministic. The window's answer is set on the double, and the operation lease is taken
    //  directly on the entry exactly as the three mutators take it.

    [Fact]
    public async Task ACreateWhosePublicationWindowReportsTheSessionRetiringIsRefusedAndDisposesTheSurface()
    {
        (UpdateService service, FakeTaskFactory factory, UpdateTaskRegistry registry) = CreateService();

        factory.PublicationRetiring = true;

        CreateUpdateTaskResponse refused = await service.CreateUpdateTask(
            new CreateUpdateTaskRequest { Session = new SessionHandle { SessionId = "s-1" } },
            Context);

        // The code the oracle answers for a transaction it cannot use
        // [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L113].
        Assert.Equal(WireRetCode.EInvalidTransaction, refused.Status.RetCode);
        Assert.Null(refused.Task);
        Assert.Contains("began retiring", refused.Status.ErrorText, StringComparison.Ordinal);

        // The window WAS entered - otherwise this case would pass against a handler that ignored it.
        Assert.Equal(1, factory.PublicationsEntered);

        // NOTHING PUBLISHED, and the surface the factory built was dropped deterministically. Leaving it
        // would pin its pool reference for the life of the process
        // [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L174].
        Assert.Equal(0, registry.Count);

        FakeTaskSurface built = Assert.Single(factory.Created);
        Assert.True(built.Disposed);
    }

    [Fact]
    public async Task ALiveWindowStillPublishesSoTheRefusalIsNotUnconditional()
    {
        // THE OTHER HALF OF THE CLAIM ABOVE. Without it the liveness arm could be a constant refusal and
        // that case would still pass.
        (UpdateService service, FakeTaskFactory factory, UpdateTaskRegistry registry) = CreateService();

        CreateUpdateTaskResponse created = await service.CreateUpdateTask(
            new CreateUpdateTaskRequest { Session = new SessionHandle { SessionId = "s-1" } },
            Context);

        Assert.Equal(WireRetCode.Ok, created.Status.RetCode);
        Assert.NotNull(created.Task);
        Assert.Equal(1, factory.PublicationsEntered);
        Assert.Equal(1, registry.Count);
        Assert.False(Assert.Single(factory.Created).Disposed);
    }

    [Fact]
    public async Task APurgeDoesNotDisposeATaskWithAnOperationInFlight()
    {
        (UpdateService service, FakeTaskFactory factory, UpdateTaskRegistry registry) =
            CreateService();

        CreateUpdateTaskResponse created = await service.CreateUpdateTask(
            new CreateUpdateTaskRequest { Session = new SessionHandle { SessionId = "s-1" } },
            Context);

        Assert.Equal(WireRetCode.Ok, created.Status.RetCode);
        Assert.True(registry.TryResolve(created.Task, out UpdateTaskEntry? entry));
        Assert.NotNull(entry);

        FakeTaskSurface surface = Assert.Single(factory.Created);

        // THE LEASE THE THREE MUTATORS TAKE, taken directly so the case is deterministic. What it adds over
        // TaskOperationLatchTests is that the PURGE respects it.
        Assert.Equal(TaskLatchOutcome.Acquired, entry!.TryBeginOperation());

        Assert.Equal(1, registry.PurgeSession("s-1"));

        // RETIRED FROM THE TABLE BUT NOT DISPOSED: the handle is unreachable, so no new call can arrive, and
        // the surface the in-flight operation is still using is intact.
        Assert.Equal(0, registry.Count);
        Assert.False(surface.Disposed);

        // THE HANDOFF'S OTHER SIDE: the operation's own exit is told disposal is now its duty, and performs
        // it. Exactly one of the two paths disposes, and it is this one.
        Assert.True(entry.EndOperation());

        entry.DisposeTask();

        Assert.True(surface.Disposed);
    }

    [Fact]
    public async Task APurgeDisposesATaskWithNothingInFlight()
    {
        // THE COMPLEMENT, WITHOUT WHICH THE HANDOFF COULD SIMPLY NEVER DISPOSE.
        (UpdateService service, FakeTaskFactory factory, UpdateTaskRegistry registry) = CreateService();

        CreateUpdateTaskResponse created = await service.CreateUpdateTask(
            new CreateUpdateTaskRequest { Session = new SessionHandle { SessionId = "s-1" } },
            Context);

        Assert.Equal(WireRetCode.Ok, created.Status.RetCode);

        FakeTaskSurface surface = Assert.Single(factory.Created);

        Assert.False(surface.Disposed);
        Assert.Equal(1, registry.PurgeSession("s-1"));
        Assert.Equal(0, registry.Count);
        Assert.True(surface.Disposed);
    }

    [Fact]
    public async Task ADrainDoesNotDisposeATaskWithAnOperationInFlight()
    {
        // THE SHUTDOWN PATH TAKES THE SAME HANDOFF AS THE PURGE. It is a separate member with its own loop,
        // so a fix applied to one and not the other would leave the host tearing down a task mid-operation
        // on the way out - the same hazard, at the least observable moment.
        (UpdateService service, FakeTaskFactory factory, UpdateTaskRegistry registry) = CreateService();

        CreateUpdateTaskResponse created = await service.CreateUpdateTask(
            new CreateUpdateTaskRequest { Session = new SessionHandle { SessionId = "s-1" } },
            Context);

        Assert.Equal(WireRetCode.Ok, created.Status.RetCode);
        Assert.True(registry.TryResolve(created.Task, out UpdateTaskEntry? entry));

        FakeTaskSurface surface = Assert.Single(factory.Created);

        Assert.Equal(TaskLatchOutcome.Acquired, entry!.TryBeginOperation());
        Assert.Equal(1, registry.Drain());

        Assert.Equal(0, registry.Count);
        Assert.False(surface.Disposed);

        Assert.True(entry.EndOperation());

        entry.DisposeTask();

        Assert.True(surface.Disposed);
    }

    [Fact]
    public async Task AnIdleReclaimDoesNotDisposeATaskWithAnOperationInFlight()
    {
        // THE THIRD LOOP, AND THE ONE THE OLD CODE ARGUED DID NOT NEED THE HANDOFF - on the grounds that
        // every C-06 operation refreshes the stamp on the way in, so a task in use is never near the idle
        // window. "Never" was too strong: an update against a contended file-backed store can outrun the
        // window, and the teardown would then land on a task still executing. The handoff costs nothing and
        // removes the argument.
        (UpdateService service, FakeTaskFactory factory, UpdateTaskRegistry registry) = CreateService();

        CreateUpdateTaskResponse created = await service.CreateUpdateTask(
            new CreateUpdateTaskRequest { Session = new SessionHandle { SessionId = "s-1" } },
            Context);

        Assert.Equal(WireRetCode.Ok, created.Status.RetCode);
        Assert.True(registry.TryResolve(created.Task, out UpdateTaskEntry? entry));

        FakeTaskSurface surface = Assert.Single(factory.Created);

        Assert.Equal(TaskLatchOutcome.Acquired, entry!.TryBeginOperation());

        // A window of zero against a clock read far in the future: every entry is past it, deterministically
        // and without waiting.
        Assert.Equal(
            1,
            registry.ReclaimIdle(
                DateTimeOffset.UnixEpoch.AddYears(100),
                TimeSpan.Zero));

        Assert.Equal(0, registry.Count);
        Assert.False(surface.Disposed);

        Assert.True(entry.EndOperation());

        entry.DisposeTask();

        Assert.True(surface.Disposed);
    }
}
