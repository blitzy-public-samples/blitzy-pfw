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

// The adapter and the generated contract wrapper share the simple name UpdateService, exactly as
// GlobalUsings.cs anticipates for this folder. An alias directive is resolved ahead of the namespace
// imports at the same scope, so it is the fix for CS0104 rather than another candidate for it - and it
// RENAMES a reference without collapsing either type: both remain distinct and both remain reachable.
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
    }
    private static readonly ServerCallContext Context = new FakeCallContext(CancellationToken.None);

    private static (UpdateService Service, FakeTaskFactory Factory, UpdateTaskRegistry Registry)
        CreateService()
    {
        FakeTaskFactory factory = new();
        UpdateTaskRegistry registry = new(Options.Create(new PersistenceOptions()), TimeProvider.System);

        return (new UpdateService(factory, registry), factory, registry);
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
            new PrepareUpdateRequest { Task = handle, MultiTableUpdate = true, Tables = { stated } },
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
            new PrepareUpdateRequest { Task = handle, Tables = { Company() } },
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
            new PrepareUpdateRequest { Task = handle, Tables = { zero } },
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
            new PrepareUpdateRequest { Task = handle, Tables = { only } },
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
            new PrepareUpdateRequest { Task = handle, Tables = { noIdentity } },
            Context);

        Assert.Equal(WireRetCode.Ok, accepted.Status.RetCode);
        Assert.Equal(string.Empty, Assert.Single(surface.Adds).IdentityColumn);

        // The refusal is whatever the collection answers - this boundary restates no arm of its own.
        surface.AddResult = RetCode.E_INVALID_ARGUMENT;

        PrepareUpdateResponse refused = await service.PrepareUpdate(
            new PrepareUpdateRequest { Task = handle, Tables = { Company(), Company("SECOND") } },
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

        PrepareUpdateResponse response = await service.PrepareUpdate(
            new PrepareUpdateRequest { Task = handle, MultiTableUpdate = false },
            Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
        Assert.Empty(surface.Adds);
        Assert.False(surface.MultiTableSeen);
        Assert.Null(surface.DataObjectSeen);
        Assert.Null(surface.SqlSyntaxSeen);
    }

    [Fact]
    public async Task PrepareUpdateReportsBusyWithoutTouchingAnythingElse()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();
        surface.Busy = true;

        PrepareUpdateResponse response = await service.PrepareUpdate(
            new PrepareUpdateRequest { Task = handle, Tables = { Company() } },
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
                new UpdateTaskRegistry(Options.Create(new PersistenceOptions()), TimeProvider.System)));
        Assert.Throws<ArgumentNullException>(() => new UpdateService(new FakeTaskFactory(), null!));
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
}
