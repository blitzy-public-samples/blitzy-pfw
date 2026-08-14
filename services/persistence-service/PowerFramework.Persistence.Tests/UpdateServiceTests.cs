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
//      the ONE-BASED table ordinal reaching the refusal diagnostic              [:L86], OneBasedIndex.FirstIndex
//      the after-update hook seeing the UNRECONCILED result                     [:L206] before [:L208-L210]
//      THIS TYPE BEING THE SOLE Aborted THROW SITE IN THE WHOLE SERVICE         constraint C-K
//      no C-06 message carrying a credential-shaped member, LogPass included    constraint C-F
//      every DbError statement reaching the wire THROUGH THE REDACTOR           constraint C-F
//      the SORT DROPPED BEFORE the changeset is applied, so rows commit in      [:L333-L334, :L336]
//        COPY ORDER
//      a SetChanges of -1 with a ZERO row count being SUCCESS-WITH-NO-DATA,     [:L339] versus [:L343-L345]
//        and the same -1 with a NON-ZERO count being a failure
//      the transaction attached BEFORE the branch                               [:L350] before [:L356]
//      the multi-table path preparing ONCE PER TABLE, IN ORDER, EACH BEFORE     [:L364-L369]
//        ITS OWN EXECUTION
//      the SINGLE-TABLE path executing with NO PREPARE AT ALL                   [:L370-L372]
//      the commit epilogue, the rollback epilogue, and the LAST-ERROR           [:L385-L401], [:L389, :L397]
//        DE-DUPLICATION guard that reports an identical code only once
//      finalize releasing the large payload DETERMINISTICALLY                   [:L406-L408]
//      reset guarding on the running flag, clearing every input, and CHAINING   [:L58-L72], [:L60], [:L71]
//        TO THE BASE LAST
//      the fixture's own updatekeyinplace=no driving the DELETE-THEN-INSERT     ws_objects/pfw.tests.pbl.src/
//        key refresh, which is the MAINLINE case and not a rare branch            dw_sqlite.srd:L14, [:L151-L167]
//
//  ------------------------------------------------------------------------------------------------
//  THE PRODUCE / THROW / MAP DIVISION OF RESPONSIBILITY (constraint C-K, and it is THREE types)
//
//  A conflict travels through three owners and no one of them does another's job. Recording the split
//  here is the point: a reader who assumes any single type owns the whole thing will put a fix in the
//  wrong place, and two of the three boundaries have no compiler to stop them.
//
//    1. Concurrency/ConflictDetector.cs  PRODUCES.  It measures the mismatch, builds the
//       common.v1.ConflictDetail from the current and original row state, redacts the statement, and
//       returns it as DATA on an UpdateOutcome whose Kind is Conflict. It throws NOTHING, and it names
//       no transport: a classifier that threw would decide the wire on its caller's behalf and would
//       bypass the multi-table loop's own inspection of the returned code [:L366, :L368]. Its own suite
//       is ConflictDetectorTests.
//    2. Grpc/UpdateService.cs - THIS FILE'S SUBJECT - THROWS.  It is the DESIGNATED and, as asserted
//       below, the ONLY Aborted throw site in the service. It consumes the projection whole and raises
//       it; it builds no status, chooses no code and assembles no payload.
//    3. Program.cs  MAPS.  The status interceptor owns the central exception-to-status mapping for the
//       whole service and deliberately passes an already-chosen status THROUGH UNTOUCHED - rewriting it
//       would strip the trailers and turn a 409 carrying evidence into a bare 500. Pinned in
//       CompositionRootTests.AChosenStatusPassesThroughUntouched.
//
//  Downstream, Gateway projects Aborted onto HTTP 409 (AAP 0.1.5, 0.6.3.8). NO PATH ANYWHERE YIELDS A
//  SUCCESS RESPONSE FOR A MISMATCH, and the theory below walks the four outcomes to say so.
//
//  ------------------------------------------------------------------------------------------------
//  WHY ABSENT-VERSUS-DEFAULT IS A WIRE-LEVEL DISTINCTION AND NOT A STYLE CHOICE (constraint C-K)
//
//  updatewhere and updatekeyinplace are proto3 `optional` fields, which is what gives them explicit
//  presence - HasUpdatewhere and HasUpdatekeyinplace - on the generated message. The oracle needs
//  exactly that: it applies each setting ONLY when the field is not null [:L131, :L135], and its
//  caller-side four-argument overload reaches that state by SetNull-ing both locals before forwarding
//  [n_cst_threading_task_sqlupdate.sru:L227-L234]. So "unset" and "zero" are DIFFERENT REQUESTS.
//  Collapsing absent into the proto3 default would write `DataWindow.Table.UpdateWhere = '0'` into the
//  modification script for a caller who never asked for a concurrency mode - silently imposing the
//  key-columns-only mode in place of whatever the DataWindow's own definition declared. That is a
//  changed WHERE clause on every generated UPDATE, which is precisely the class of defect the
//  updatewhereclause contract exists to prevent, and no test on either side of the wire alone would
//  see it.
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
//
//  ------------------------------------------------------------------------------------------------
// ==================================================================================================
// System.Globalization is imported locally rather than globally, exactly as GlobalUsings.cs prescribes for
// the minority of files that need it: the ordinal diagnostics below are composed culture-invariantly, so a
// machine's locale cannot change what a test asserts.
//
// System.Reflection is deliberately NOT imported. The one member that reflects
// - TheServiceDerivesFromTheGeneratedBaseAndOverridesEveryDeclaredRpc - qualifies BindingFlags in full, and
// an import used by a single expression would make reflection read as this file's norm when it is its
// exception. The one architectural assertion that could use reflection reads SOURCE instead; see
// ThisTypeIsTheSoleAbortedThrowSiteInTheService for the measurement behind that choice.
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PowerFramework.Persistence.Concurrency;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Grpc;
using PowerFramework.Persistence.Tasks;
using PowerFramework.Persistence.Transactions;

// The adapter and the generated contract wrapper share the simple name UpdateService, exactly as
// GlobalUsings.cs anticipates for this folder. An alias directive is resolved ahead of the namespace
// imports at the same scope, so it is the fix for CS0104 rather than another candidate for it - and it
// RENAMES a reference without collapsing either type: both remain distinct and both remain reachable.
using DataObjectDefinitionRegistry = PowerFramework.Persistence.Data.DataObjectDefinitionCatalogue;
using UpdateService = PowerFramework.Persistence.Grpc.UpdateService;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Characterization coverage for <c>Grpc/UpdateService.cs</c>, the C-06 adapter and the designated
/// <see cref="RpcException"/>/<see cref="StatusCode.Aborted"/> throw site for this service, together with
/// the <see cref="SqlUpdateTask"/> orchestration beneath it.
/// </summary>
[SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "Every disposable the orchestration harness creates is owned by the harness and "
        + "released by its own Dispose.")]
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

        /// <summary>
        /// A per-position answer for successive adds, consumed in order; <see cref="AddResult"/> answers
        /// every position once this list is exhausted.
        /// </summary>
        /// <remarks>
        /// The seam the one-based-ordinal matrix needs: refusing the SECOND or THIRD descriptor rather than
        /// the first is the only way to show the reported ordinal tracks the array position instead of
        /// being fixed at one.
        /// </remarks>
        internal IReadOnlyList<long> AddResultsInOrder { get; set; } = [];

        /// <summary>
        /// When set, every add is forwarded to a REAL descriptor collection and its answer is returned, so
        /// the oracle's own admission arms decide rather than <see cref="AddResult"/>.
        /// </summary>
        /// <remarks>
        /// Null by default, which keeps every other case on the steered answer. See
        /// <see cref="TheThreeArmAdmissionTestSurfacesThroughTheRpcAgainstTheRealCollection"/> for why one
        /// matrix needs the provisioned collection.
        /// </remarks>
        internal UpdatableTableCollection? RealTables { get; set; }

        /// <summary>Answers one add, honouring the per-position list, the collection and the busy flag.</summary>
        /// <param name="descriptor">The descriptor as recorded, forwarded when a real collection is set.</param>
        /// <returns>The code this add answers.</returns>
        private long AnswerAdd(RecordedAddCall descriptor)
        {
            if (Busy)
            {
                return RetCode.E_BUSY;
            }

            if (RealTables is { } tables)
            {
                return tables.AddUpdatableTable(
                    descriptor.Name,
                    descriptor.UpdatableColumns,
                    descriptor.KeyColumns,
                    descriptor.IdentityColumn,
                    descriptor.UpdateWhere,
                    descriptor.UpdateKeyInPlace);
            }

            // The recorded call is already appended by the caller, so its ordinal is the count.
            int position = _adds.Count;

            return position <= AddResultsInOrder.Count
                ? AddResultsInOrder[position - OneBasedIndex.FirstIndex]
                : AddResult;
        }

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

            RecordedAddCall recorded = new(
                name,
                [.. updatableColumns],
                [.. keyColumns],
                identityColumn,
                updateWhere,
                updateKeyInPlace,
                SixArgumentForm: true);

            _adds.Add(recorded);

            return AnswerAdd(recorded);
        }

        public long AddUpdatableTable(
            string name,
            IEnumerable<string> updatableColumns,
            IEnumerable<string> keyColumns,
            string identityColumn)
        {
            _calls.Add("AddUpdatableTable4");

            RecordedAddCall recorded = new(
                name,
                [.. updatableColumns],
                [.. keyColumns],
                identityColumn,
                UpdateWhere: null,
                UpdateKeyInPlace: null,
                SixArgumentForm: false);

            _adds.Add(recorded);

            return AnswerAdd(recorded);
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
    /// <summary>
    /// Captures the formatted text of every log record, so the diagnostics that travel to an OPERATOR
    /// rather than to a caller can be asserted.
    /// </summary>
    /// <typeparam name="TCategory">The logger's category type.</typeparam>
    /// <remarks>
    /// The adapter's logger is an OPTIONAL constructor dependency, so most cases below leave it null and
    /// assert only the response. This exists for the cases where the two halves of one diagnostic are
    /// deliberately split - the terse caller-facing text and the fuller operator-facing record.
    /// </remarks>
    private sealed class RecordingLogger<TCategory> : ILogger<TCategory>
    {
        private readonly List<string> _messages = [];

        /// <summary>The formatted text of every record, in the order they were written.</summary>
        internal IReadOnlyList<string> Messages => _messages;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            _messages.Add(formatter(state, exception));
        }
    }

    private static readonly ServerCallContext Context = new FakeCallContext(CancellationToken.None);

    private static (UpdateService Service, FakeTaskFactory Factory, UpdateTaskRegistry Registry)
        CreateService(ILogger<UpdateService>? logger = null)
    {
        FakeTaskFactory factory = new();
        UpdateTaskRegistry registry = new(Options.Create(new PersistenceOptions()), TimeProvider.System);

        return (
            new UpdateService(factory, registry, new DataObjectDefinitionRegistry(), logger),
            factory,
            registry);
    }

    private static async Task<(UpdateService Service, FakeTaskSurface Surface, TaskHandle Handle,
        UpdateTaskRegistry Registry)> CreateTaskAsync(ILogger<UpdateService>? logger = null)
    {
        (UpdateService service, FakeTaskFactory factory, UpdateTaskRegistry registry) =
            CreateService(logger);

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

    /// <summary>
    /// 🔴 All four presence combinations land on <see cref="UpdatableTableDescriptor"/>'s
    /// <see langword="long"/>? and <see langword="bool"/>? members with presence intact, asserted against the
    /// REAL descriptor collection rather than a recording of the call.
    /// </summary>
    /// <param name="stateMode">Whether the request states the concurrency mode.</param>
    /// <param name="stateKeyInPlace">Whether it states the key-in-place setting.</param>
    /// <remarks>
    /// <para>
    /// <b>THIS IS THE CASE THAT CLOSES THE LOOP.</b> The three cases above assert what the ADAPTER PASSED,
    /// which is the adapter's own responsibility; this one asserts what the DESCRIPTOR HOLDS, which is what
    /// the prepare later reads when it decides whether to emit each setting at all [<c>:L131, :L135</c>].
    /// A mapping that passed null correctly into a collection that then defaulted it would satisfy the three
    /// above and still write a concurrency mode the caller never asked for.
    /// </para>
    /// <para>
    /// <b>ZERO AND FALSE ARE ASSERTED AS PRESENT, WHICH IS THE OTHER HALF OF THE DISTINCTION.</b> The failure
    /// this guards against is symmetric: an implementation that treated the proto3 default as absence would
    /// DROP an explicit <c>updatewhere = 0</c> - a caller asking for the key-columns-only mode - just as
    /// surely as one that treated absence as the default would invent it. Both directions are covered here,
    /// which is why the values chosen are exactly the defaults rather than convenient non-defaults.
    /// </para>
    /// <para>
    /// The two settings are varied INDEPENDENTLY because the oracle tests the two nulls separately
    /// [<c>:L131</c> versus <c>:L135</c>], so a caller may legitimately state one and omit the other and the
    /// four combinations are four genuinely different requests.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task EveryPresenceCombinationLandsOnTheRealDescriptorWithPresenceIntact(
        bool stateMode,
        bool stateKeyInPlace)
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        surface.RealTables = new UpdatableTableCollection();

        TableUpdateContract descriptor = new()
        {
            Name = DwSqliteFixture.UpdateTableName,
            Identitycolumn = DwSqliteFixture.IdentityColumnName,
        };

        descriptor.Updatablecolumns.AddRange(DwSqliteFixture.ColumnNames);
        descriptor.Keycolumns.AddRange(DwSqliteFixture.KeyColumnNames);

        if (stateMode)
        {
            // THE PROTO3 DEFAULT, DELIBERATELY. Stating zero is the case an implementation that confused
            // absence with default would silently drop.
            descriptor.Updatewhere = 0L;
        }

        if (stateKeyInPlace)
        {
            descriptor.Updatekeyinplace = false;
        }

        // The generated message's own presence, asserted before the call so the request's shape is not in
        // doubt when the descriptor is read back.
        Assert.Equal(stateMode, descriptor.HasUpdatewhere);
        Assert.Equal(stateKeyInPlace, descriptor.HasUpdatekeyinplace);

        PrepareUpdateResponse response = await service.PrepareUpdate(
            new PrepareUpdateRequest
            {
                Task = handle,
                MultiTableUpdate = true,
                Tables = { descriptor },
                DataObject = EvidencedDataObject,
            },
            Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);

        UpdatableTableDescriptor recorded =
            surface.RealTables.DescriptorAt(OneBasedIndex.FirstIndex);

        // ABSENT SURVIVES AS ABSENT - null, and never 0 or false.
        if (stateMode)
        {
            Assert.Equal(0L, recorded.UpdateWhere);
        }
        else
        {
            Assert.Null(recorded.UpdateWhere);
        }

        if (stateKeyInPlace)
        {
            Assert.False(recorded.UpdateKeyInPlace);
        }
        else
        {
            Assert.Null(recorded.UpdateKeyInPlace);
        }

        // The four non-optional members round-trip unconditionally, so a presence failure cannot be mistaken
        // for a wholesale mapping failure.
        Assert.Equal(DwSqliteFixture.UpdateTableName, recorded.Name);
        Assert.Equal(DwSqliteFixture.ColumnNames, recorded.UpdatableColumns);
        Assert.Equal(DwSqliteFixture.KeyColumnNames, recorded.KeyColumns);
        Assert.Equal(DwSqliteFixture.IdentityColumnName, recorded.IdentityColumn);
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

    /// <summary>
    /// The ordinal in the refusal diagnostic is the descriptor's ONE-BASED array position, and it tracks
    /// the position rather than being fixed at the first.
    /// </summary>
    /// <param name="refusedPosition">The one-based position the collection refuses.</param>
    /// <param name="tableCount">How many descriptors the request carries.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// R9 (AAP 0.4.5.4) IS WHY THIS IS ASSERTED RATHER THAN ASSUMED. The oracle appends at
    /// <c>UpperBound(Tables) + 1</c> [<c>:L86</c>] and prepares over <c>for nIndex = 1 to nCount</c>
    /// [<c>:L364</c>], so every ordinal a caller can be told about is one-based; the adapter's own loop is
    /// zero-based over a repeated field and rebases for the report only
    /// [<c>Grpc/UpdateService.cs</c>, <c>ordinal = index + OneBasedIndex.FirstIndex</c>]. A silent
    /// off-by-one here would name the wrong descriptor in the one message a caller has to debug from -
    /// indistinguishable from a behavioural regression, which is exactly R9's warning.
    /// </para>
    /// <para>
    /// THE RESPONSE CARRIES THE ORDINAL AND THE LOG RECORD CARRIES THE PAIR, and both are asserted because
    /// each answers a different question. "Update table 2" is what the CALLER receives and is deliberately
    /// terse - it quotes no table name, no column name and no value, so nothing sensitive can travel on it
    /// (constraint C-F). "table 2 of 3" is what the OPERATOR receives, and the count is what tells them the
    /// refusal was positional rather than terminal.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(RefusedDescriptorPositions))]
    public async Task TheOneBasedTableOrdinalTracksTheRefusedDescriptorsArrayPosition(
        int refusedPosition,
        int tableCount)
    {
        RecordingLogger<UpdateService> log = new();

        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) =
            await CreateTaskAsync(log);

        // Admits every descriptor before the refused one, refuses that one, and is never reached again -
        // the adapter stops at the first refusal, which is the oracle's own shape [:L84, :L366].
        surface.AddResultsInOrder = [
            .. Enumerable.Repeat(RetCode.OK, refusedPosition - OneBasedIndex.FirstIndex),
            RetCode.E_INVALID_ARGUMENT];

        PrepareUpdateRequest request = new()
        {
            Task = handle,
            MultiTableUpdate = true,
            DataObject = EvidencedDataObject,
        };

        for (int position = OneBasedIndex.FirstIndex; position <= tableCount; position++)
        {
            request.Tables.Add(Company(
                string.Create(CultureInfo.InvariantCulture, $"COMPANY_{position}")));
        }

        PrepareUpdateResponse refused = await service.PrepareUpdate(request, Context);

        Assert.Equal(WireRetCode.EInvalidArgument, refused.Status.RetCode);

        // THE CALLER'S HALF: the one-based ordinal, and the oracle locator that explains the three arms.
        Assert.Contains(
            string.Create(CultureInfo.InvariantCulture, $"Update table {refusedPosition} was refused"),
            refused.Status.ErrorText,
            StringComparison.Ordinal);
        Assert.Contains("sqlupdate.sru:L84", refused.Status.ErrorText, StringComparison.Ordinal);

        // THE OPERATOR'S HALF: the ordinal AND the count, on the log record rather than the response.
        string warning = Assert.Single(log.Messages, message =>
            message.Contains("refused update table", StringComparison.Ordinal));

        Assert.Contains(
            string.Create(CultureInfo.InvariantCulture, $"table {refusedPosition} of {tableCount}"),
            warning,
            StringComparison.Ordinal);

        // STOPPED AT THE REFUSAL, leaving the descriptors admitted so far in place [:L84, :L366].
        Assert.Equal(refusedPosition, surface.Adds.Count);
    }

    /// <summary>The refused position and the descriptor count each case sends.</summary>
    public static TheoryData<int, int> RefusedDescriptorPositions => new()
    {
        { 1, 1 },
        { 1, 3 },
        { 2, 3 },
        { 3, 3 },
    };

    /// <summary>
    /// The three-arm admission test of <c>:L84</c>, driven through the RPC against the REAL descriptor
    /// collection rather than a steered refusal.
    /// </summary>
    /// <param name="arm">Which of the three conditions the descriptor violates.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// <b>WHY THE REAL COLLECTION AND NOT THE FAKE.</b> Its sibling case above proves the adapter RELAYS
    /// whatever the collection answers, which is the adapter's own responsibility. It cannot prove that
    /// the three arms are the three the oracle has - a collection that admitted an empty key-column array
    /// would pass it unchanged. Driving the provisioned <see cref="UpdatableTableCollection"/> through the
    /// same RPC closes that gap without duplicating <c>UpdateWhereBuilderTests</c>, which owns the
    /// collection in isolation.
    /// </para>
    /// <para>
    /// <b>AN EMPTY IDENTITY COLUMN IS THE FOURTH CASE AND IT IS ADMITTED.</b> The oracle's condition names
    /// three members and NOT the identity column [<c>:L84</c>], and the prepare applies that one only when
    /// it is non-empty [<c>:L127-L129</c>] - a table with no identity column is ordinary, not malformed.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(AdmissionArms))]
    public async Task TheThreeArmAdmissionTestSurfacesThroughTheRpcAgainstTheRealCollection(string arm)
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        // The fake forwards every add to a real collection, so the arms under test are the collection's.
        surface.RealTables = new UpdatableTableCollection();

        TableUpdateContract descriptor = new() { Identitycolumn = DwSqliteFixture.IdentityColumnName };

        switch (arm)
        {
            case "empty-name":
                descriptor.Updatablecolumns.AddRange(DwSqliteFixture.ColumnNames);
                descriptor.Keycolumns.AddRange(DwSqliteFixture.KeyColumnNames);
                break;

            case "no-updatable-columns":
                descriptor.Name = DwSqliteFixture.UpdateTableName;
                descriptor.Keycolumns.AddRange(DwSqliteFixture.KeyColumnNames);
                break;

            case "no-key-columns":
                descriptor.Name = DwSqliteFixture.UpdateTableName;
                descriptor.Updatablecolumns.AddRange(DwSqliteFixture.ColumnNames);
                break;

            default:
                // The control: nothing is violated EXCEPT that the identity column is empty, which the
                // oracle's condition does not name - so this one is admitted.
                descriptor.Name = DwSqliteFixture.UpdateTableName;
                descriptor.Updatablecolumns.AddRange(DwSqliteFixture.ColumnNames);
                descriptor.Keycolumns.AddRange(DwSqliteFixture.KeyColumnNames);
                descriptor.Identitycolumn = string.Empty;
                break;
        }

        PrepareUpdateResponse response = await service.PrepareUpdate(
            new PrepareUpdateRequest
            {
                Task = handle,
                MultiTableUpdate = true,
                Tables = { descriptor },
                DataObject = EvidencedDataObject,
            },
            Context);

        if (string.Equals(arm, "empty-identity-column", StringComparison.Ordinal))
        {
            Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
            Assert.Equal(
                OneBasedIndex.FirstIndex,
                surface.RealTables.UpperBound);
            Assert.Equal(string.Empty, surface.RealTables.DescriptorAt(OneBasedIndex.FirstIndex)
                .IdentityColumn);

            return;
        }

        Assert.Equal(WireRetCode.EInvalidArgument, response.Status.RetCode);

        // NOTHING WAS APPENDED. The oracle returns BEFORE its first assignment [:L84], so a refused
        // descriptor leaves the array exactly as it was.
        Assert.Equal(OneBasedIndex.EmptyUpperBound, surface.RealTables.UpperBound);
    }

    /// <summary>The three refused arms of <c>:L84</c> plus the admitted empty-identity control.</summary>
    public static TheoryData<string> AdmissionArms =>
    [
        "empty-name",
        "no-updatable-columns",
        "no-key-columns",
        "empty-identity-column",
    ];


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

    /// <summary>
    /// A descriptor field whose text cannot occupy an identifier position is refused, atomically, and
    /// never reaches the task.
    /// </summary>
    /// <param name="table">The update table the descriptor states.</param>
    /// <param name="column">The single updatable column the descriptor states.</param>
    /// <param name="key">The single key column the descriptor states.</param>
    /// <param name="identity">The identity column the descriptor states.</param>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>THIS IS THE ROW THE CWE-89 FINDING EXISTS FOR.</b> Values on the C-06 write path are
    /// parameterized as <c>@pN</c> and identifiers cannot be, because no dialect has a parameter form for
    /// a table or a column - so these four fields are the text that reaches the engine as SQL. Before this
    /// screen a descriptor naming <c>COMPANY; DROP TABLE COMPANY --</c> was recorded verbatim and
    /// <c>Tasks/SqlUpdateCarrier.cs</c> concatenated it into generated DML, where the separator turns one
    /// statement into two and the comment marker removes the concurrency predicate that followed.
    /// </para>
    /// <para>
    /// EACH ROW EXERCISES A DIFFERENT FIELD, in the oracle's own visit order - the table, an updatable
    /// column, a key column and the identity column
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L111-L129</c>] - because a
    /// gate that screened only the table would leave three positions open and would still pass a
    /// single-row case.
    /// </para>
    /// <para>
    /// THE REFUSAL IS <c>E_INVALID_ARGUMENT</c>, which is the code both sibling descriptor arms already
    /// answer, and it is ATOMIC: nothing was cleared, nothing was switched and nothing was recorded, so a
    /// refused prepare leaves the task exactly as it was. THE DIAGNOSTIC QUOTES NOTHING the caller sent
    /// (constraint C-F).
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("COMPANY; DROP TABLE COMPANY --", "id", "id", "id")]
    [InlineData("COMPANY WHERE 1=1", "id", "id", "id")]
    [InlineData("COMPANY", "id) --", "id", "id")]
    [InlineData("COMPANY", "id", "id;DELETE FROM COMPANY", "id")]
    [InlineData("COMPANY", "id", "id", "id,name")]
    [InlineData("COMPANY", "", "id", "id")]
    [InlineData("", "id", "id", "id")]
    public async Task ADescriptorNamingTextThatCannotBeAnIdentifierIsRefusedAtomically(
        string table,
        string column,
        string key,
        string identity)
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        PrepareUpdateResponse refused = await service.PrepareUpdate(
            new PrepareUpdateRequest
            {
                Task = handle,

                // THE SWITCH IS OFF, WHICH IS THE HARDER HALF. A descriptor sent with it off is inert
                // today, but the switch is settable on a later prepare and this call REPLACES the array
                // wholesale, so admitting an inadmissible name now would leave it in place for a call that
                // does apply it.
                MultiTableUpdate = false,
                DataObject = EvidencedDataObject,
                Tables =
                {
                    new TableUpdateContract
                    {
                        Name = table,
                        Updatablecolumns = { column },
                        Keycolumns = { key },
                        Identitycolumn = identity,
                    },
                },
            },
            Context);

        Assert.Equal(WireRetCode.EInvalidArgument, refused.Status.RetCode);
        Assert.Equal(UpdateService.InadmissibleIdentifierDiagnostic, refused.Status.ErrorText);

        // THE DIAGNOSTIC CARRIES NONE OF THE CALLER'S OWN TEXT (constraint C-F).
        Assert.DoesNotContain("DROP", refused.Status.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE", refused.Status.ErrorText, StringComparison.Ordinal);

        // ATOMIC: nothing was cleared, nothing was switched, nothing was recorded.
        Assert.Empty(surface.Calls);
        Assert.Empty(surface.Adds);
    }

    /// <summary>
    /// The evidenced descriptor is still admitted, and so is a qualified update table.
    /// </summary>
    /// <remarks>
    /// THE NEGATIVE CONTROL FOR THE CASE ABOVE, and it is not optional: a gate that refused everything
    /// would satisfy every refusal assertion while breaking the only cross-service update path in the
    /// estate. The first descriptor is the sole updatable DataWindow in the legacy tree
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14</c>], transcribed by <c>Company()</c>; the
    /// second qualifies the table, which is the shape a deployment addressing an attached database sends
    /// and which all three target dialects accept. An EMPTY identity column is legal and means "no
    /// identity column" [<c>n_cst_thread_task_sqlupdate.sru:L127-L129</c>], so it is admitted rather than
    /// refused.
    /// </remarks>
    [Fact]
    public async Task TheEvidencedDescriptorAndAQualifiedTableAreStillAdmitted()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        TableUpdateContract qualified = Company("main.COMPANY");
        qualified.Identitycolumn = string.Empty;

        PrepareUpdateResponse accepted = await service.PrepareUpdate(
            new PrepareUpdateRequest
            {
                Task = handle,
                MultiTableUpdate = true,
                Tables = { Company(), qualified },
                DataObject = EvidencedDataObject,
            },
            Context);

        Assert.Equal(WireRetCode.Ok, accepted.Status.RetCode);
        Assert.Equal(["COMPANY", "main.COMPANY"], surface.Adds.Select(static add => add.Name));
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
    /// A source field that is present and EMPTY is refused with the run's own code, and never reaches the
    /// setter that asserts on it.
    /// </summary>
    /// <param name="emptyDataObject">Whether the request states an empty data object.</param>
    /// <param name="emptySqlSyntax">Whether the request states an empty SQL syntax.</param>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>THIS IS THE ROW THE FINDING EXISTS FOR, AND WHAT IT PREVENTS IS A PROCESS EXIT.</b> The
    /// caller-side proxy's data-object setter carries the oracle's UN-GATED assertion
    /// [<c>Tasks/TaskProxies/SqlUpdateTaskProxy.cs:1077</c>, oracle <c>:L101</c>], and this host answers an
    /// <c>AssertionFailure</c> by SHUTTING DOWN - the reproduced fail-fast posture. So before this screen an
    /// authenticated caller holding the write scope terminated the whole instance, and with it every open
    /// session, task and transaction, by sending one explicitly-present empty string
    /// (CWE-20, CWE-617, CWE-248). The sibling syntax setter's assertion is DEBUG-gated [<c>:1134</c>], so
    /// the same input reached the same fail-fast in a debug build; both fields are covered.
    /// </para>
    /// <para>
    /// <b>THE ASSERTION IS NOT WEAKENED, AND THAT IS ASSERTED ELSEWHERE RATHER THAN HERE.</b>
    /// <c>SqlUpdateTaskProxyTests.SetDataObject_AssertsOnAnEmptyNameInEveryConfiguration</c> still requires
    /// the un-gated assertion to fire for an in-process caller, which is what it is for (constraint C-B).
    /// What this row requires is that REMOTE input never reaches it.
    /// </para>
    /// <para>
    /// <b>THE CODE AND THE MESSAGE ARE THE RUN'S OWN.</b> An empty value installs no source and CLEARS the
    /// sibling one [<c>:L259-L260</c>, <c>:L270-L271</c>], so the state it leaves is exactly the no-source
    /// state the case above refuses - same constant, same verbatim diagnostic, consumed from the task type
    /// that owns it rather than retyped.
    /// </para>
    /// <para>
    /// <b>AND THE REFUSAL IS ATOMIC</b>: the surface records NO call at all, which is the assertion that
    /// proves the screen sits ahead of both the clear and the setters rather than merely converting an
    /// exception into a status afterwards.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task APresentButEmptySourceFieldIsRefusedAndNeverReachesTheSetter(
        bool emptyDataObject,
        bool emptySqlSyntax)
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        // THE TASK ALREADY HOLDS A USABLE SOURCE, so this row cannot pass by way of the no-source arm above:
        // the refusal it asserts can only come from the emptiness screen.
        Assert.Equal(
            WireRetCode.Ok,
            (await service.PrepareUpdate(
                new PrepareUpdateRequest { Task = handle, DataObject = EvidencedDataObject },
                Context)).Status.RetCode);

        Assert.Equal(EvidencedDataObject, surface.DataObjectSeen);

        int callsBefore = surface.Calls.Count;

        PrepareUpdateRequest request = new() { Task = handle, Tables = { Company() } };

        if (emptyDataObject)
        {
            request.DataObject = string.Empty;
        }

        if (emptySqlSyntax)
        {
            request.SqlSyntax = string.Empty;
        }

        PrepareUpdateResponse refused = await service.PrepareUpdate(request, Context);

        Assert.Equal(WireRetCode.EInvalidDataobject, refused.Status.RetCode);
        Assert.Equal(SqlUpdateTask.InvalidDataObjectMessage, refused.Status.ErrorText);

        // NOTHING REACHED THE TASK: no clear, no switch, no descriptor, and above all neither setter - so
        // the empty string never reached the assertion that would have ended the process.
        Assert.Equal(callsBefore, surface.Calls.Count);
        Assert.Empty(surface.Adds);

        // The source the task already held is intact, so the refusal did not half-apply.
        Assert.Equal(EvidencedDataObject, surface.DataObjectSeen);
        Assert.Null(surface.SqlSyntaxSeen);

        // THE POSITIVE ARM: a NON-EMPTY value on the very same field is still accepted, so the screen tests
        // emptiness rather than presence.
        PrepareUpdateRequest accepted = new() { Task = handle };

        if (emptyDataObject)
        {
            accepted.DataObject = "d_company";
        }

        if (emptySqlSyntax)
        {
            accepted.SqlSyntax = "release 12.5;";
        }

        Assert.Equal(
            WireRetCode.Ok,
            (await service.PrepareUpdate(accepted, Context)).Status.RetCode);
    }

    /// <summary>
    /// The refusal names the offending field in its log record and quotes no request value.
    /// </summary>
    /// <remarks>
    /// A caller that sent both fields has to be told which one to correct, and an operator reading the log
    /// needs the same. What must NOT appear is any request value: the diagnostic carries the field's ROLE
    /// and the task identifier only (constraint C-F).
    /// </remarks>
    [Fact]
    public async Task TheEmptySourceRefusalNamesTheFieldWithoutQuotingAValue()
    {
        RecordingLogger<UpdateService> logger = new();

        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) =
            await CreateTaskAsync(logger);

        Assert.Equal(
            WireRetCode.Ok,
            (await service.PrepareUpdate(
                new PrepareUpdateRequest { Task = handle, DataObject = EvidencedDataObject },
                Context)).Status.RetCode);

        _ = await service.PrepareUpdate(
            new PrepareUpdateRequest { Task = handle, DataObject = string.Empty },
            Context);

        string record = Assert.Single(
            logger.Messages,
            message => message.Contains("EMPTY data object", StringComparison.Ordinal));

        Assert.Contains("no update could ever run", record, StringComparison.Ordinal);
        Assert.DoesNotContain(EvidencedDataObject, record, StringComparison.Ordinal);
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
    /// 🔴 A descriptor declaring a SUBSET of the definition's updatable columns, with the multi-table
    /// switch OFF, is REFUSED rather than accepted and then ignored.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ARM THAT CLOSES A SILENT DATA-INTEGRITY FAULT. With the switch off the descriptor array is never
    /// applied, so a caller stating two updatable columns previously received <c>RetCode.OK</c> and then had
    /// every column of the definition written - a modification to a column the caller had EXCLUDED reached
    /// storage, and the response asserted the restriction had been accepted. A misdirected write reported as
    /// a success is the same hazard the different-update-table arm already refuses, and it is worse here,
    /// because a wrong table is visible in storage a caller can read while an unauthorised column is not.
    /// </para>
    /// <para>
    /// THE CODE IS <c>E_INVALID_ARGUMENT</c>, matching the table arm rather than the undeclared-column arm:
    /// the column names all EXIST, so the oracle's 无效的列名: is the wrong answer - nothing here failed to
    /// resolve. What is wrong is the combination of a stated contract with the switch that would apply it
    /// turned off.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ADescriptorRestrictingUpdatableColumnsWithoutMultiTableIsRefused()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        TableUpdateContract subset = new()
        {
            Name = "COMPANY",
            Updatablecolumns = { "NAME", "salary" },
            Keycolumns = { "ID" },
            Identitycolumn = string.Empty,
        };

        PrepareUpdateResponse refused = await service.PrepareUpdate(
            new PrepareUpdateRequest
            {
                Task = handle,
                MultiTableUpdate = false,
                DataObject = EvidencedDataObject,
                Tables = { subset },
            },
            Context);

        Assert.Equal(WireRetCode.EInvalidArgument, refused.Status.RetCode);
        Assert.Equal(UpdateService.DisagreeingDescriptorDiagnostic, refused.Status.ErrorText);

        // ATOMIC: nothing was cleared, switched, added or installed.
        Assert.Empty(surface.Calls);
        Assert.Empty(surface.Adds);
    }

    /// <summary>
    /// The SAME restricting descriptor with the multi-table switch ON is admitted, because then it governs.
    /// </summary>
    /// <remarks>
    /// THE PAIR THAT PROVES THE REFUSAL IS ABOUT INERTNESS AND NOT ABOUT THE SUBSET. A restricted updatable
    /// set is a legitimate contract - it is the whole point of the descriptor array - and with the switch on
    /// <c>_of_updateprepare</c> resets every column's flags and re-enables only the stated ones
    /// [<c>n_cst_thread_task_sqlupdate.sru:L104-L129</c>], so the caller gets exactly what it asked for.
    /// </remarks>
    [Fact]
    public async Task ARestrictingDescriptorWithMultiTableIsAdmitted()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        PrepareUpdateResponse admitted = await service.PrepareUpdate(
            new PrepareUpdateRequest
            {
                Task = handle,
                MultiTableUpdate = true,
                DataObject = EvidencedDataObject,
                Tables =
                {
                    new TableUpdateContract
                    {
                        Name = "COMPANY",
                        Updatablecolumns = { "NAME", "salary" },
                        Keycolumns = { "ID" },
                        Identitycolumn = string.Empty,
                    },
                },
            },
            Context);

        Assert.Equal(WireRetCode.Ok, admitted.Status.RetCode);
        Assert.Equal("COMPANY", Assert.Single(surface.Adds).Name);
    }

    /// <summary>
    /// A descriptor stating the definition's FULL contract is admitted with the switch off, whatever the
    /// letter case, whatever the order, and with a duplicate.
    /// </summary>
    /// <remarks>
    /// 🔴 THE SHAPE THIS SYSTEM'S OWN DATASERVICES CONSUMER SENDS ON EVERY UPDATE. It derives the descriptor
    /// FROM the definition and sends <c>MultiTableUpdate = false</c>, so the agreement test has to admit it
    /// or the only cross-service update path in the estate breaks. The comparison is therefore a
    /// case-insensitive SET comparison rather than a sequence comparison: order carries no meaning in the
    /// oracle's script, which emits one independent line per column [<c>:L111-L129</c>], nor in the generated
    /// statement, whose column order comes from the carrier's own one-based model.
    /// </remarks>
    [Fact]
    public async Task ADescriptorAgreeingWithTheDefinitionIsAdmittedInAnyCaseOrderOrMultiplicity()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        PrepareUpdateResponse admitted = await service.PrepareUpdate(
            new PrepareUpdateRequest
            {
                Task = handle,
                MultiTableUpdate = false,
                DataObject = EvidencedDataObject,
                Tables =
                {
                    new TableUpdateContract
                    {
                        Name = "company",
                        Updatablecolumns =
                        {
                            "BIRTH", "salary", "ADDRESS", "age", "NAME", "id", "NAME",
                        },
                        Keycolumns = { "id", "ID" },
                        Identitycolumn = "Id",
                        Updatewhere = UpdateWhereBuilder.KeyAndUpdatableColumnsMode,
                        Updatekeyinplace = false,
                    },
                },
            },
            Context);

        Assert.Equal(WireRetCode.Ok, admitted.Status.RetCode);
        Assert.Equal("company", Assert.Single(surface.Adds).Name);
    }

    /// <summary>
    /// A descriptor that states NOTHING beyond the table is admitted with the switch off: an empty array and
    /// an empty identity column mean "unstated", not "stated empty".
    /// </summary>
    /// <remarks>
    /// THE LIMIT THAT KEEPS THE NARROWING MINIMAL. A repeated field cannot distinguish absent from empty on
    /// the wire, and the identity column's emptiness is already read as "no identity column" rather than as a
    /// name everywhere in this class [<c>:L127-L129</c>]. So a caller naming only the table has contradicted
    /// nothing and is asking for exactly the definition's own behaviour, which is what it gets.
    /// </remarks>
    [Fact]
    public async Task ADescriptorStatingOnlyTheTableIsAdmittedWithoutMultiTable()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        PrepareUpdateResponse admitted = await service.PrepareUpdate(
            new PrepareUpdateRequest
            {
                Task = handle,
                MultiTableUpdate = false,
                DataObject = EvidencedDataObject,
                Tables = { new TableUpdateContract { Name = "COMPANY", Identitycolumn = string.Empty } },
            },
            Context);

        Assert.Equal(WireRetCode.Ok, admitted.Status.RetCode);
        Assert.Equal("COMPANY", Assert.Single(surface.Adds).Name);
    }

    /// <summary>
    /// Each of the four remaining stated fields is refused on its own when it differs from the definition's.
    /// </summary>
    /// <param name="keyColumns">The stated key set, empty for unstated.</param>
    /// <param name="identityColumn">The stated identity column, empty for unstated.</param>
    /// <param name="updateWhere">The stated concurrency mode, <see langword="null"/> for unstated.</param>
    /// <param name="updateKeyInPlace">The stated key handling, <see langword="null"/> for unstated.</param>
    /// <remarks>
    /// FOUR SEPARATE MISDIRECTIONS, EACH REPORTED THE SAME WAY. A stated key set that differs means the
    /// predicate addresses different rows than declared; a stated identity column means the write-back
    /// reports a different column; a stated <c>updatewhere</c> means the row is guarded under a policy the
    /// caller did not choose; a stated <c>updatekeyinplace</c> means a key change takes the other statement
    /// shape. Every one of them was previously accepted and silently discarded.
    /// </remarks>
    [Theory]
    [InlineData("NAME", "", null, null)]
    [InlineData("", "NAME", null, null)]
    [InlineData("", "", UpdateWhereBuilder.KeyOnlyMode, null)]
    [InlineData("", "", UpdateWhereBuilder.KeyAndModifiedColumnsMode, null)]
    [InlineData("", "", null, true)]
    public async Task EachStatedFieldDifferingFromTheDefinitionIsRefusedWithoutMultiTable(
        string keyColumns,
        string identityColumn,
        long? updateWhere,
        bool? updateKeyInPlace)
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        TableUpdateContract descriptor = new()
        {
            Name = "COMPANY",
            Identitycolumn = identityColumn,
        };

        if (keyColumns.Length != 0)
        {
            descriptor.Keycolumns.Add(keyColumns);
        }

        if (updateWhere.HasValue)
        {
            descriptor.Updatewhere = updateWhere.Value;
        }

        if (updateKeyInPlace.HasValue)
        {
            descriptor.Updatekeyinplace = updateKeyInPlace.Value;
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

        Assert.Equal(WireRetCode.EInvalidArgument, refused.Status.RetCode);
        Assert.Equal(UpdateService.DisagreeingDescriptorDiagnostic, refused.Status.ErrorText);
        Assert.Empty(surface.Calls);
        Assert.Empty(surface.Adds);
    }

    /// <summary>
    /// 🔴 An <c>updatewhere</c> outside the three modes is refused whatever the multi-table switch says.
    /// </summary>
    /// <param name="multiTableUpdate">Both switch positions.</param>
    /// <param name="updateWhere">Values that name no mode, on both sides of the domain.</param>
    /// <remarks>
    /// <para>
    /// THE MODE DECIDES WHICH COLUMNS GUARD THE ROW, so a value naming no mode has no predicate and was
    /// previously installed anyway - the update then ran under whichever arm an unrecognised value reached,
    /// which is a concurrency policy of the service's choosing substituted for the caller's.
    /// </para>
    /// <para>
    /// BOTH SWITCH POSITIONS, because the descriptor array is replaced wholesale and the switch is settable
    /// on a later prepare, so admitting a mode now would leave it in place for a call that does apply it.
    /// This refusal therefore precedes the agreement arm, which is why the mode-specific diagnostic and not
    /// <see cref="UpdateService.DisagreeingDescriptorDiagnostic"/> is what a caller sees.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(false, 7L)]
    [InlineData(true, 7L)]
    [InlineData(false, 3L)]
    [InlineData(true, -1L)]
    [InlineData(true, long.MaxValue)]
    public async Task AnUpdateWhereOutsideTheThreeModesIsRefusedOnBothSwitchPositions(
        bool multiTableUpdate,
        long updateWhere)
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        PrepareUpdateResponse refused = await service.PrepareUpdate(
            new PrepareUpdateRequest
            {
                Task = handle,
                MultiTableUpdate = multiTableUpdate,
                DataObject = EvidencedDataObject,
                Tables =
                {
                    new TableUpdateContract
                    {
                        Name = "COMPANY",
                        Identitycolumn = string.Empty,
                        Updatewhere = updateWhere,
                    },
                },
            },
            Context);

        Assert.Equal(WireRetCode.EInvalidArgument, refused.Status.RetCode);
        Assert.Equal(UpdateService.UnsupportedUpdateWhereDiagnostic, refused.Status.ErrorText);
        Assert.Empty(surface.Calls);
        Assert.Empty(surface.Adds);
    }

    /// <summary>
    /// Each of the three modes that DO exist is admitted with the multi-table switch on.
    /// </summary>
    /// <param name="updateWhere">The three modes.</param>
    /// <remarks>
    /// THE COMPANION TO THE REFUSAL ABOVE, and the proof that the domain test screens rather than narrows to
    /// one. The oracle does not implement any mode itself: it passes the value straight into
    /// <c>Modify("DataWindow.Table.UpdateWhere = " + String(...))</c>
    /// [<c>n_cst_thread_task_sqlupdate.sru:L132</c>] and the runtime generates the predicate. Refusing 0 or 2
    /// would therefore be a narrowing the legacy does not have.
    /// </remarks>
    [Theory]
    [InlineData(UpdateWhereBuilder.KeyOnlyMode)]
    [InlineData(UpdateWhereBuilder.KeyAndUpdatableColumnsMode)]
    [InlineData(UpdateWhereBuilder.KeyAndModifiedColumnsMode)]
    public async Task EachRealUpdateWhereModeIsAdmittedWithMultiTable(long updateWhere)
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        PrepareUpdateResponse admitted = await service.PrepareUpdate(
            new PrepareUpdateRequest
            {
                Task = handle,
                MultiTableUpdate = true,
                DataObject = EvidencedDataObject,
                Tables =
                {
                    new TableUpdateContract
                    {
                        Name = "COMPANY",
                        Identitycolumn = string.Empty,
                        Updatewhere = updateWhere,
                    },
                },
            },
            Context);

        Assert.Equal(WireRetCode.Ok, admitted.Status.RetCode);
        Assert.Equal(updateWhere, Assert.Single(surface.Adds).UpdateWhere);
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

    /// <summary>
    /// A submitted carrier over either published bound is refused, and the task's own payload survives.
    /// </summary>
    /// <param name="overRows">Whether the carrier exceeds the row ceiling.</param>
    /// <remarks>
    /// <para>
    /// <b>WHAT WAS WRONG.</b> The oracle's payload setter takes a blob and a row count and bounds neither
    /// [<c>n_cst_thread_task_sqlupdate.sru:L44, :L74-L77</c>], and it was right not to: the blob was produced
    /// by the SAME PROCESS a moment earlier from a DataWindow the application owned, so its size was a
    /// property of that application's own data. Across a network boundary the carrier is caller-submitted,
    /// and the only thing bounding it was the transport's message ceiling - which a caller fills with
    /// millions of one-column rows as easily as with a few wide ones. Every row is then decoded, reconciled
    /// against its buffer segment and walked twice, current values and original values, before a single
    /// statement is generated: item COUNT and payload SIZE are different quantities and only one was bounded
    /// (CWE-400, CWE-770).
    /// </para>
    /// <para>
    /// <b>THE REFUSAL IS ATOMIC, WHICH IS THE HALF A STATUS ASSERTION ALONE WOULD MISS.</b> The setter it
    /// guards ASSIGNS the payload before anything decodes it, so a carrier refused further in would already
    /// have replaced the payload the task held. The row therefore installs a usable payload first and
    /// requires it to survive the refusal untouched.
    /// </para>
    /// <para>
    /// <b>THIS NARROWS THE CONTRACT DELIBERATELY</b> [AAP 0.1.5]: where a legacy behaviour cannot cross a
    /// network boundary unchanged, the contract is narrowed with a DEFINED error rather than widened with a
    /// guess. The error is <c>E_OUT_OF_RANGE</c> and it names the bound, because a published ceiling is not
    /// a fact about this deployment's state or its other callers.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnOverBudgetCarrierIsRefusedAndTheHeldPayloadSurvives(bool overRows)
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();
        surface.Result = new UpdateRunResult
        {
            Code = RetCode.OK,
            Outcome = UpdateOutcome.Succeeded(Resolution(new UpdateRowCounts(1, 0, 0))),
        };

        // THE TASK ALREADY HOLDS A USABLE PAYLOAD, so the survival assertion below has something to be about.
        CarrierState held = new() { Processing = 1 };

        Assert.Equal(
            WireRetCode.Ok,
            (await service.Update(
                new UpdateRequest { Task = handle, UpdateData = held, UpdateRows = 1 },
                Context)).Status.RetCode);

        Assert.Same(held, surface.PayloadSeen);

        CarrierState offending = overRows
            ? CarrierOfRows(UpdateCarrierBounds.MaximumRows + 1, valuesPerRow: 1)
            : CarrierOfRows(1, UpdateCarrierBounds.MaximumValuesPerRow + 1);

        UpdateResponse refused = await service.Update(
            new UpdateRequest { Task = handle, UpdateData = offending, UpdateRows = 1 },
            Context);

        Assert.Equal(WireRetCode.EOutOfRange, refused.Status.RetCode);
        Assert.Equal(
            overRows
                ? UpdateCarrierBounds.TooManyRowsDiagnostic
                : UpdateCarrierBounds.TooManyValuesDiagnostic,
            refused.Status.ErrorText);

        // ATOMIC: the payload the task held is the one it still holds.
        Assert.Same(held, surface.PayloadSeen);
    }

    /// <summary>
    /// A carrier at both ceilings is accepted, and an absent one still means what the oracle means by it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE POSITIVE ARM, without which the rows above are satisfied by an implementation that refuses every
    /// carrier - the failure mode a newly added bound is most likely to have. It submits a carrier sitting
    /// exactly ON both ceilings, so the comparison is proved to be inclusive rather than merely present.
    /// </para>
    /// <para>
    /// AND THE ABSENT CARRIER, which is a legal submission whose meaning is decided further in: a rejected
    /// change set with a ZERO row count is the oracle's SUCCESS arm [<c>:L339-L342</c>]. A screen that
    /// refused an absent carrier would convert a documented success into a refusal.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ACarrierAtBothCeilingsIsAcceptedAndAnAbsentOneIsUntouched()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();
        surface.Result = new UpdateRunResult
        {
            Code = RetCode.OK,
            Outcome = UpdateOutcome.Succeeded(Resolution(new UpdateRowCounts(1, 0, 0))),
        };

        CarrierState atCeiling = CarrierOfRows(
            UpdateCarrierBounds.MaximumRows,
            UpdateCarrierBounds.MaximumValuesPerRow);

        Assert.Equal(
            WireRetCode.Ok,
            (await service.Update(
                new UpdateRequest { Task = handle, UpdateData = atCeiling, UpdateRows = 1 },
                Context)).Status.RetCode);

        Assert.Same(atCeiling, surface.PayloadSeen);

        Assert.Equal(
            WireRetCode.Ok,
            (await service.Update(
                new UpdateRequest { Task = handle, UpdateRows = 0 },
                Context)).Status.RetCode);

        Assert.Null(surface.PayloadSeen);
    }

    /// <summary>
    /// The row ceiling counts across buffers rather than within one.
    /// </summary>
    /// <remarks>
    /// A conforming carrier holds three segments, one per buffer, and every one of their rows is decoded and
    /// walked. A ceiling applied per segment would admit three times the work it claims to - which is the
    /// specific mistake a reader checking only the primary buffer would not see.
    /// </remarks>
    [Fact]
    public async Task TheRowCeilingCountsAcrossEveryBuffer()
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();
        surface.Result = new UpdateRunResult
        {
            Code = RetCode.OK,
            Outcome = UpdateOutcome.Succeeded(Resolution(new UpdateRowCounts(1, 0, 0))),
        };

        int perBuffer = (UpdateCarrierBounds.MaximumRows / 2) + 1;

        CarrierState split = new() { Processing = 1 };

        foreach (DwBuffer buffer in (DwBuffer[])[DwBuffer.Primary, DwBuffer.Delete])
        {
            CarrierBufferSegment segment = new() { Buffer = buffer };

            for (int row = 0; row < perBuffer; row++)
            {
                segment.Rows.Add(new DataWindowRow { Buffer = buffer, Row = row + 1 });
            }

            split.Segments.Add(segment);
        }

        UpdateResponse refused = await service.Update(
            new UpdateRequest { Task = handle, UpdateData = split, UpdateRows = 1 },
            Context);

        Assert.Equal(WireRetCode.EOutOfRange, refused.Status.RetCode);
        Assert.Equal(UpdateCarrierBounds.TooManyRowsDiagnostic, refused.Status.ErrorText);
    }

    /// <summary>
    /// Builds a carrier with the requested shape and nothing else.
    /// </summary>
    /// <param name="rows">How many rows to place in the primary segment.</param>
    /// <param name="valuesPerRow">How many column values each row carries, in each value set.</param>
    /// <returns>The carrier.</returns>
    /// <remarks>
    /// The values carry column identifiers and no data. What the bound counts is ITEMS, so a row's payload
    /// is irrelevant to it, and leaving the values empty keeps the row's cost proportional to the count
    /// under test rather than to a fixture's idea of a realistic row.
    /// </remarks>
    private static CarrierState CarrierOfRows(int rows, int valuesPerRow)
    {
        CarrierBufferSegment segment = new() { Buffer = DwBuffer.Primary };

        for (int row = 0; row < rows; row++)
        {
            DataWindowRow carried = new() { Buffer = DwBuffer.Primary, Row = row + 1 };

            for (int column = 0; column < valuesPerRow; column++)
            {
                carried.Columns.Add(new ColumnValue { ColumnId = column + 1 });
                carried.OriginalValues.Add(new ColumnValue { ColumnId = column + 1 });
            }

            segment.Rows.Add(carried);
        }

        return new CarrierState { Processing = 1, Segments = { segment } };
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

    /// <summary>
    /// 🔴 The four update outcomes reach the caller as FOUR DISTINCT ANSWERS, and no path anywhere yields a
    /// success response for a concurrency mismatch.
    /// </summary>
    /// <param name="kind">
    /// Which outcome the run produced, named rather than typed: <c>UpdateOutcomeKind</c> is internal to the
    /// application assembly and a public theory member cannot expose it, so the name is the discriminator.
    /// </param>
    /// <param name="runCode">The code the run reconciled to.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// <b>THIS IS THE C-06 HEADLINE STATED AS A MATRIX.</b> The individual cases above each pin one
    /// outcome's payload in detail; this one pins the property that only the whole set can show - that
    /// <see cref="StatusCode.Aborted"/> is raised for the mismatch and for NOTHING ELSE, and that the
    /// mismatch never lands anywhere but there. AAP 0.6.3.8 and 0.1.5 make Aborted the canonical mapping
    /// onto HTTP 409 and forbid a silent overwrite anywhere in the system, so "a mismatch that answered
    /// OK" is the single defect this file exists to make impossible.
    /// </para>
    /// <para>
    /// A CANCELLATION IS NOT A FAILURE AND A FAILURE IS NOT A CONFLICT. The three non-conflict rows are
    /// the controls: each answers on the response rather than by raising, each carries its own wire code,
    /// and none is conflated with the conflict row.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(DistinctUpdateOutcomes))]
    public async Task TheFourOutcomesMapDistinctlyAndAMismatchNeverAnswersASuccess(
        string kind,
        long runCode)
    {
        (UpdateService service, FakeTaskSurface surface, TaskHandle handle, _) = await CreateTaskAsync();

        surface.Result = new UpdateRunResult
        {
            Code = runCode,
            Outcome = OutcomeOfKind(kind),
            Counts = new UpdateRowCounts(1, 0, 0),
            Identity = [new ResolvedIdentityColumnData(DwSqliteFixture.IdColumnNumber, [1L], [])],
        };

        if (string.Equals(kind, nameof(UpdateOutcomeKind.Conflict), StringComparison.Ordinal))
        {
            RpcException raised = await Assert.ThrowsAsync<RpcException>(
                () => service.Update(new UpdateRequest { Task = handle }, Context));

            // THE ONE ANSWER A MISMATCH MAY HAVE, and it carries its evidence.
            Assert.Equal(StatusCode.Aborted, raised.StatusCode);
            Assert.NotNull(raised.Trailers.GetValueBytes(ConflictDetector.RichErrorTrailerKey));

            return;
        }

        UpdateResponse response = await service.Update(new UpdateRequest { Task = handle }, Context);

        // NOT Aborted, and not raised at all: the three other outcomes answer on the response.
        Assert.Equal(UpdateWireCodes.ToWireRetCode(runCode), response.Status.RetCode);

        // The counts travel only on the reconciled success, so success is distinguishable from the two
        // failures by more than its code alone.
        if (runCode == RetCode.OK)
        {
            Assert.NotNull(response.Counts);
            Assert.Equal(1L, response.Counts.Inserted);
        }
        else
        {
            Assert.Null(response.Counts);
            Assert.Empty(response.Identity);
        }
    }

    /// <summary>The four outcome kinds C-06 can produce, each with the code its run reconciles to.</summary>
    /// <remarks>
    /// The kind is carried by NAME because the enum is internal to the application assembly; the names are
    /// taken from the enum itself with <c>nameof</c>, so a rename breaks compilation here rather than
    /// silently skipping a row.
    /// </remarks>
    public static TheoryData<string, long> DistinctUpdateOutcomes => new()
    {
        { nameof(UpdateOutcomeKind.Succeeded), RetCode.OK },
        { nameof(UpdateOutcomeKind.Cancelled), RetCode.CANCELLED },
        { nameof(UpdateOutcomeKind.DatabaseError), RetCode.E_DB_ERROR },
        { nameof(UpdateOutcomeKind.Conflict), RetCode.E_DB_ERROR },
    };

    /// <summary>Builds a representative outcome of the requested kind.</summary>
    /// <param name="kind">The kind's name.</param>
    /// <returns>The outcome.</returns>
    private static UpdateOutcome OutcomeOfKind(string kind) => kind switch
    {
        nameof(UpdateOutcomeKind.Succeeded) => UpdateOutcome.Succeeded(
            Resolution(
                new UpdateRowCounts(1, 0, 0),
                new ResolvedIdentityColumnData(DwSqliteFixture.IdColumnNumber, [1L], []))),
        nameof(UpdateOutcomeKind.Cancelled) => UpdateOutcome.Cancelled(updateInvoked: false),
        nameof(UpdateOutcomeKind.DatabaseError) => UpdateOutcome.DatabaseError(
            DbErrorData.FromTransaction(-1, "the driver refused"),
            errorReported: true),
        nameof(UpdateOutcomeKind.Conflict) => UpdateOutcome.ConflictDetected(
            StaleRowConflict(),
            DbErrorData.FromTransaction(-1, "the row changed"),
            updateTable: DwSqliteFixture.UpdateTableName,
            updateResult: DataWindowBufferStore.DataStoreFailure,
            observedByHook: DataWindowBufferStore.DataStoreSuccess,
            overrideApplied: true),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The matrix names four kinds."),
    };

    /// <summary>
    /// A populated conflict detail over the sole evidenced fixture: one row whose salary another writer
    /// moved, reported with both the current and the original value.
    /// </summary>
    /// <returns>The detail.</returns>
    /// <remarks>
    /// BOTH VALUES ARE CARRIED BECAUSE <c>updatewhere=1</c> REQUIRES BOTH. The fixture marks all six
    /// columns <c>updatewhereclause=yes</c> [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14</c>], so
    /// the concurrency predicate is the key column plus the ORIGINAL value of every marked column - a
    /// detail carrying only the current state could not tell a caller what it was rebasing from.
    /// The values are obvious non-secrets and match no provider credential pattern (constraint C-F).
    /// </remarks>
    private static ConflictDetail StaleRowConflict()
    {
        ConflictDetail detail = new()
        {
            UpdateTable = DwSqliteFixture.UpdateTableName,
            RowsExpected = 1L,
            RowsMatched = 0L,
        };

        detail.Rows.Add(new ConflictRow
        {
            Buffer = DwBuffer.Primary,
            Row = OneBasedIndex.FirstIndex,
            ItemStatus = ItemStatus.DataModified,
            CurrentValues =
            {
                new ColumnValue
                {
                    ColumnId = DwSqliteFixture.SalaryColumnNumber,
                    Value = new AnyValue { Int64Value = 5200L },
                },
            },
            OriginalValues =
            {
                new ColumnValue
                {
                    ColumnId = DwSqliteFixture.SalaryColumnNumber,
                    Value = new AnyValue { Int64Value = 4800L },
                },
            },
        });

        return detail;
    }

    /// <summary>
    /// 🔴 <c>Grpc/UpdateService.cs</c> is the ONLY type in the whole Persistence service that raises
    /// <see cref="StatusCode.Aborted"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>WHY THIS IS ASSERTED AND NOT MERELY DOCUMENTED (constraint C-K).</b> The produce/throw/map split
    /// this file's header records has NO compile-time enforcement: any of the four gRPC services, the
    /// classifier, a task or the status interceptor could construct an <c>Aborted</c> status and nothing
    /// would object. A second throw site is the specific way the guarantee decays - one that raised Aborted
    /// WITHOUT the conflict trailer would give a caller a 409 carrying no evidence to rebase from, which is
    /// worse than a plain failure because it looks actionable. Counting the sites is how the split stays a
    /// property of the code rather than a claim about it.
    /// </para>
    /// <para>
    /// <b>THE SCAN IS OVER SOURCE, AND THAT IS A DELIBERATE CHOICE MADE AFTER MEASURING THE ALTERNATIVE.</b>
    /// An IL scan was tried first - look for the <see cref="RpcException"/> constructor's metadata token in
    /// every method body - and it OVER-REPORTS: four raw bytes coincide with unrelated tokens and inline
    /// data, and it named a type that constructs no exception at all. A source scan cannot make that
    /// mistake, reads the way a reviewer reads, and names the offending FILE rather than a mangled type,
    /// which is what a failure has to say to be actionable. Comment and documentation lines are skipped, so
    /// the many places that DISCUSS <c>Aborted</c> - including this file's own header - are not confused
    /// with the one that raises it.
    /// </para>
    /// <para>
    /// <b>BOTH HALVES OF THE SPLIT ARE COUNTED, WHICH IS WHAT MAKES IT A SPLIT.</b>
    /// <see cref="StatusCode.Aborted"/> is NAMED in exactly one file - <c>Concurrency/ConflictDetector.cs</c>,
    /// which produces the status - and <c>throw new RpcException</c> appears in exactly one, this file's
    /// subject, which raises it. Neither file does the other's job, and <c>Program.cs</c> names neither
    /// because it passes an already-chosen status through untouched.
    /// </para>
    /// <para>
    /// SKIPPED RATHER THAN FAILED WHEN NO SOURCE TREE IS REACHABLE. Under an out-of-tree artifacts path the
    /// production sources are simply not there, and a locator failure would report a defect in the test
    /// environment as a defect in the code. The reachability is asserted separately by
    /// <c>TestRepositoryRootTests</c>, so a silently-skipping locator cannot hide.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThisTypeIsTheSoleAbortedThrowSiteInTheService()
    {
        if (LocateApplicationSourceRoot() is not { } sourceRoot)
        {
            return;
        }

        SortedSet<string> naming = new(StringComparer.Ordinal);
        SortedSet<string> raising = new(StringComparer.Ordinal);

        foreach (string file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            // The intermediate and output directories hold generated copies of the same code; counting them
            // would double every finding.
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
            {
                continue;
            }

            string relative = Path.GetRelativePath(sourceRoot, file).Replace('\\', '/');

            foreach (string line in File.ReadLines(file))
            {
                string code = line.TrimStart();

                if (code.StartsWith("//", StringComparison.Ordinal)
                    || code.StartsWith('*')
                    || code.StartsWith("/*", StringComparison.Ordinal))
                {
                    continue;
                }

                if (code.Contains("StatusCode.Aborted", StringComparison.Ordinal))
                {
                    naming.Add(relative);
                }

                if (code.Contains("throw new RpcException", StringComparison.Ordinal))
                {
                    raising.Add(relative);
                }
            }
        }

        // A GUARD ON THE GUARD: an empty scan would satisfy nothing below by accident.
        Assert.NotEmpty(raising);

        // PRODUCES - the classifier, and only the classifier. THIS is the assertion that carries the
        // guarantee: a status that could be Aborted can be constructed in exactly one file, so no other
        // file can raise a 409 at all, whatever else it raises.
        Assert.Equal(["Concurrency/ConflictDetector.cs"], [.. naming]);

        // RAISES - exactly two files, and the second is not a second CONFLICT site.
        //
        // WHY THE SET GREW, AND WHY THE PROPERTY DID NOT WEAKEN. Grpc/GrpcIngressLimit.cs raises
        // RESOURCE_EXHAUSTED when an ingress bound is met, which is the canonical gRPC status for a refusal
        // rather than a fault and is the only status it can raise - it names no other, and the assertion
        // above proves it cannot name Aborted, because that spelling appears in one file and this is not
        // that file. So the division this test defends is intact: the classifier still owns producing the
        // conflict status, Grpc/UpdateService.cs still owns raising it, and no path anywhere can answer a
        // caller 409 without the conflict trailer to rebase from.
        //
        // The bound cannot be enforced from either existing file. It has to refuse BEFORE a handler runs,
        // which is what an interceptor is, and an interceptor's only way of refusing a call is to raise.
        Assert.Equal(["Grpc/GrpcIngressLimit.cs", "Grpc/UpdateService.cs"], [.. raising]);

        // AND THE SECOND SITE IS PINNED TO ITS ONE STATUS, so a later edit cannot quietly repurpose the
        // interceptor into a general-purpose throw site. Read from source for the same reason the scan
        // above is: it names the file a reviewer would open.
        string limiter = File.ReadAllText(Path.Combine(sourceRoot, "Grpc", "GrpcIngressLimit.cs"));

        Assert.Contains("StatusCode.ResourceExhausted", limiter, StringComparison.Ordinal);
        Assert.Single(
            limiter.Split("new Status(", StringSplitOptions.None).Skip(1).ToArray());
    }

    /// <summary>
    /// The <c>PowerFramework.Persistence</c> application project directory, or <see langword="null"/> when
    /// no source tree is reachable from this test run.
    /// </summary>
    /// <returns>The directory, or <see langword="null"/>.</returns>
    /// <remarks>
    /// The embedded repository root forms the path DIRECTLY because the marker being sought is
    /// service-relative rather than repository-relative; the upward walk from the output directory is
    /// retained beneath it so a run with no embedded root behaves as it did before. This mirrors the
    /// locator in <c>TransactionServiceTests</c> rather than inventing a second convention -
    /// see <see cref="TestRepositoryRoot"/>.
    /// </remarks>
    private static string? LocateApplicationSourceRoot()
    {
        const string ProjectDirectory = "PowerFramework.Persistence";

        if (TestRepositoryRoot.Embedded is { } root)
        {
            string direct = Path.Combine(root, "services", "persistence-service", ProjectDirectory);

            if (Directory.Exists(direct))
            {
                return direct;
            }
        }

        DirectoryInfo? probe = new(AppContext.BaseDirectory);

        while (probe is not null)
        {
            string candidate = Path.Combine(probe.FullName, ProjectDirectory);

            if (Directory.Exists(candidate)
                && File.Exists(Path.Combine(candidate, ProjectDirectory + ".csproj")))
            {
                return candidate;
            }

            probe = probe.Parent;
        }

        return null;
    }

    /// <summary>
    /// No C-06 message declares a member that could carry a credential, so the transaction descriptor's
    /// write-only <c>logpass</c> has no route onto this contract at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>CONSTRAINT C-F, ASSERTED STRUCTURALLY RATHER THAN BY REVIEW.</b> The legacy transaction structure
    /// carries <c>string logpass</c> [<c>ws_objects/pfw.thread.ext.pbl.src/transactiondata.srs</c>] and the
    /// port keeps it WRITE-ONLY - never echoed in a response, never logged. A response message that grew a
    /// credential-shaped field would defeat that at the contract level, where no amount of care in the
    /// adapter could recover it, and it would do so invisibly: the field would simply be empty until
    /// something populated it.
    /// </para>
    /// <para>
    /// THE WHOLE REACHABLE GRAPH IS WALKED, not just the top-level messages, because a nested message is
    /// exactly where such a field would hide. The walk follows every message-typed field transitively from
    /// each C-06 response, so <c>OperationStatus</c>, <c>DbError</c>, <c>ConflictDetail</c>,
    /// <c>ConflictRow</c>, <c>ColumnValue</c>, <c>AnyValue</c>, <c>IdentityColumnData</c> and
    /// <c>NullableInt64</c> are all covered without being listed.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoMessageOnTheUpdateContractCarriesACredentialShapedMember()
    {
        string[] forbidden = ["pass", "password", "credential", "secret", "logpass"];

        HashSet<string> visited = new(StringComparer.Ordinal);
        Queue<MessageDescriptor> pending = new();

        foreach (MessageDescriptor root in new[]
        {
            CreateUpdateTaskResponse.Descriptor,
            ReleaseUpdateTaskResponse.Descriptor,
            ResetUpdateTaskResponse.Descriptor,
            PrepareUpdateResponse.Descriptor,
            UpdateResponse.Descriptor,
        })
        {
            pending.Enqueue(root);
        }

        int inspected = 0;

        while (pending.Count > 0)
        {
            MessageDescriptor message = pending.Dequeue();

            if (!visited.Add(message.FullName))
            {
                continue;
            }

            inspected++;

            foreach (FieldDescriptor field in message.Fields.InDeclarationOrder())
            {
                Assert.DoesNotContain(
                    forbidden,
                    marker => field.Name.Contains(marker, StringComparison.OrdinalIgnoreCase));

                if (field.FieldType == FieldType.Message && field.MessageType is { } nested)
                {
                    pending.Enqueue(nested);
                }
            }
        }

        // A GUARD ON THE GUARD. If the walk ever visited nothing - a renamed descriptor, a message that
        // stopped being reachable - the loop above would pass by inspecting nothing at all.
        Assert.True(inspected >= 5, "The walk must reach at least the five C-06 response messages.");
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
        // THE CANCELLATION CONTRACT, FOR THIS CONTRACT: the worker side is synchronous by contract, so what the token buys is
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
        // THE THIRD LOOP, AND THE ONE MOST EASILY ARGUED NOT TO NEED THE HANDOFF - on the grounds that
        // every C-06 operation refreshes the stamp on the way in, so a task in use is never near the idle
        // window. "Never" is too strong: an update against a contended file-backed store can outrun the
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

    // =================================================================================================
    //  THE ORCHESTRATION BENEATH THE ADAPTER - `ondotask` [:L284-L404], DRIVEN END TO END
    //  -----------------------------------------------------------------------------------------------
    //  WHY THE WORKER IS EXERCISED IN THIS FILE AND NOT ONLY BEHIND THE FAKE SURFACE. Everything above
    //  substitutes the task at the boundary seam, which is right for asserting the TRANSLATION the adapter
    //  owns. It cannot assert the SEQUENCE beneath it, and the sequence is where C-06's hardest guarantees
    //  live: the sort must be dropped before the changeset is applied, the prepare must run per table on
    //  one path and not at all on the other, and an identical error must be reported once rather than
    //  twice. None of those is visible from a response; all of them are observable as an ordered series of
    //  calls. So the cases below compose a REAL SqlUpdateTask over recording collaborators and read the
    //  order back.
    //
    //  NO DATABASE, NO THREAD, NO CLOCK AND NO SLEEP (constraints C-E and AAP 0.6.7). The transaction
    //  engine opens no connection and composes no connection string; the carrier is a recording view over a
    //  real in-memory buffer store; the clock is frozen; nothing is posted, awaited or slept on. Every case
    //  is a synchronous call and repeats identically.
    //
    //  THE SOLE EVIDENCED FIXTURE IS THE PRINCIPAL CASE (AAP 0.6.3.1). The descriptor, the column names,
    //  the key column, the identity column, the expected modification script and the identity values all
    //  come from DwSqliteFixture, which is dw_sqlite.srd read into code - so `updatewhere=1` and
    //  `updatekeyinplace=no` are the fixture's own settings rather than values chosen to make a test pass.
    // =================================================================================================

    /// <summary>The ordered series of calls one orchestration run made, shared by every collaborator.</summary>
    /// <remarks>
    /// ONE LIST ACROSS ALL SURFACES, because the facts under test are about the order calls happen in
    /// RELATIVE TO EACH OTHER - a sort on the store versus an apply on the carrier versus a modify on the
    /// modifier. Per-surface counters could not express that at all.
    /// </remarks>
    private sealed class OrchestrationRecorder
    {
        private readonly List<string> _calls = [];

        /// <summary>Every recorded call, in order.</summary>
        internal IReadOnlyList<string> Calls => _calls;

        /// <summary>Appends one call.</summary>
        /// <param name="call">The call's name.</param>
        internal void Record(string call) => _calls.Add(call);

        /// <summary>The zero-based position of the first occurrence, or <c>-1</c> when absent.</summary>
        /// <param name="call">The call's name.</param>
        /// <returns>The position.</returns>
        internal int PositionOf(string call) => _calls.IndexOf(call);

        /// <summary>How many times a call was recorded.</summary>
        /// <param name="call">The call's name.</param>
        /// <returns>The count.</returns>
        internal int CountOf(string call) =>
            _calls.Count(recorded => string.Equals(recorded, call, StringComparison.Ordinal));

        /// <summary>Asserts that <paramref name="first"/> was recorded strictly before <paramref name="second"/>.</summary>
        /// <param name="first">The call that must come first.</param>
        /// <param name="second">The call that must come second.</param>
        internal void AssertPrecedes(string first, string second)
        {
            int firstAt = PositionOf(first);
            int secondAt = PositionOf(second);

            Assert.True(firstAt >= 0, $"{first} was never recorded; the series was [{string.Join(", ", _calls)}].");
            Assert.True(secondAt >= 0, $"{second} was never recorded; the series was [{string.Join(", ", _calls)}].");
            Assert.True(
                firstAt < secondAt,
                $"{first} must precede {second}; the series was [{string.Join(", ", _calls)}].");
        }
    }

    /// <summary>The call names the orchestration series is written in.</summary>
    /// <remarks>
    /// Named constants rather than inline strings so a typo is a compile error instead of an assertion that
    /// can never match - the failure mode a string-keyed recorder is otherwise prone to.
    /// </remarks>
    private static class OrchestrationCall
    {
        internal const string DropSort = "Store.SetSort";

        internal const string ApplyChangeset = "Carrier.SetChanges";

        internal const string AttachTransaction = "Carrier.SetTransObject";

        internal const string BuildFromSyntax = "Carrier.Create";

        internal const string Modify = "Modifier.Modify";

        internal const string DescribeKeyInPlace = "Metadata.DescribeUpdateKeyInPlace";

        internal const string Execute = "Target.Update";

        internal const string Commit = "Engine.Commit";

        internal const string Rollback = "Engine.Rollback";

        internal const string Raise = "Host.OnError";
    }

    /// <summary>
    /// A frozen clock. Both readings are overridden together, because the transaction pool measures liveness
    /// and idle expiry as MONOTONIC elapsed time and a fake answering only <c>GetUtcNow</c> would let those
    /// measurements escape onto the real <c>Stopwatch</c> - which is exactly the non-determinism
    /// AAP 0.6.7 requires be seamed.
    /// </summary>
    private sealed class FrozenOrchestrationClock : TimeProvider
    {
        internal static readonly FrozenOrchestrationClock Instance = new();

        private static readonly DateTimeOffset Fixed = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Fixed;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Fixed.UtcTicks;
    }

    /// <summary>
    /// The storage engine, recording and refusing. It opens no connection and composes no connection
    /// string, so the whole matrix runs with no database of any kind (constraint C-E).
    /// </summary>
    private sealed class OrchestrationEngine(OrchestrationRecorder recorder) : ITransactionEngine
    {
        /// <summary>Whether <see cref="Connect"/> answers a success. Steered per case.</summary>
        internal bool ConnectSucceeds { get; set; } = true;

        /// <summary>Whether <see cref="Commit"/> answers a success. Steered per case.</summary>
        internal bool CommitSucceeds { get; set; } = true;

        public int DbHandle { get; private set; }

        public string Dbms { get; private set; } = string.Empty;

        public bool AutoCommit { get; set; }

        /// <summary>Moves the auto-commit mode and answers success, because this double opens no transaction.</summary>
        /// <param name="autoCommit">The mode to put in force.</param>
        /// <returns>Always a succeeded state.</returns>
        /// <remarks>
        /// ROUTED THROUGH THE PROPERTY so whatever the property records still records. A double with no
        /// provider behind it has nothing the transition can fail on, which is the contract's own
        /// nothing-to-do case.
        /// </remarks>
        public SqlState TrySetAutoCommit(bool autoCommit)
        {
            AutoCommit = autoCommit;

            return SqlState.Succeeded();
        }

        public void ApplyConnectionFields(in TransactionData descriptor) => Dbms = descriptor.Dbms;

        public SqlState Connect(CancellationToken cancellationToken = default)
        {
            if (!ConnectSucceeds)
            {
                return SqlState.Failed(RetCode.SQLITE_CANTOPEN, "connect refused by the orchestration fake");
            }

            DbHandle = 1;

            return SqlState.Succeeded();
        }

        public SqlState Disconnect()
        {
            DbHandle = 0;

            return SqlState.Succeeded();
        }

        public SqlState Commit()
        {
            recorder.Record(OrchestrationCall.Commit);

            return CommitSucceeds
                ? SqlState.Succeeded()
                : SqlState.Failed(RetCode.SQLITE_BUSY, "commit refused by the orchestration fake");
        }

        public SqlState Rollback()
        {
            recorder.Record(OrchestrationCall.Rollback);

            return SqlState.Succeeded();
        }

        public SqlState Execute(string sqlCommand, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("No case in this region issues a statement; C-07 owns that.");

        public SqlState Execute(in SqlCommandText command, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("No case in this region issues a statement; C-07 owns that.");

        public void Dispose()
        {
        }
    }

    /// <summary>One raise that reached the framework error channel.</summary>
    private sealed record OrchestrationError(long Code, string Text);

    /// <summary>
    /// The threading substrate, recording every raise. That channel is what the epilogue's de-duplication
    /// guard [<c>:L389, :L397</c>] decides to use or skip, so counting arrivals here is how suppression is
    /// measured.
    /// </summary>
    private sealed class OrchestrationHost(OrchestrationRecorder recorder) : ISqlTaskHost
    {
        internal List<OrchestrationError> Errors { get; } = [];

        public bool IsMainThread => false;

        public bool IsCancelled => false;

        public int TaskIndex => OneBasedIndex.FirstIndex;

        public ISqlTaskProxy? ParentTasking => null;

        public long GetTask(int index, out SqlTaskBase? task)
        {
            task = null;

            return RetCode.E_OUT_OF_BOUND;
        }

        public bool HasData(string name) => false;

        public object? GetData(string name) => null;

        public long SetData(string name, object? data) => RetCode.OK;

        public long OnPrepare() => RetCode.OK;

        public void OnUninit()
        {
        }

        public long OnError(long errCode, string errInfo)
        {
            recorder.Record(OrchestrationCall.Raise);
            Errors.Add(new OrchestrationError(errCode, errInfo));

            return RetCode.OK;
        }

        public long OnNotify(long notifyCode, long payload, string text) => RetCode.OK;
    }

    /// <summary>
    /// The data store, recording the one call the orchestration makes on it that matters -
    /// <c>data.SetSort("")</c> [<c>:L334</c>].
    /// </summary>
    /// <remarks>
    /// The carrier behind it is REAL rather than faked, because the preparer's key-change refresh walks a
    /// <c>DataWindowBufferStore</c> and a stub could not be walked. It holds no rows, so the refresh finds
    /// nothing to flip - which is correct here: <c>UpdateWhereBuilderTests</c> owns the refresh MECHANICS
    /// and this region owns whether the orchestration REACHES them.
    /// </remarks>
    private sealed class OrchestrationStore(OrchestrationRecorder recorder) : ISqlDataStore
    {
        private readonly DataWindowCarrier _carrier =
            DataWindowCarrierFactory.Create(
                CarrierThreadAffinity.WorkerThread,
                FrozenOrchestrationClock.Instance);

        /// <summary>The sort value the orchestration wrote, or null when it never wrote one.</summary>
        internal string? SortSeen { get; private set; }

        public DataWindowCarrier Carrier => _carrier;

        public string DataObject { get; set; } = string.Empty;

        public string GetSqlSelect() => string.Empty;

        public string Modify(string modificationScript) => string.Empty;

        // Non-empty for every property, which is what makes the added data-object probe
        // [Tasks/SqlUpdateTask.cs, DataWindow.Units] read as RESOLVED. "!" is PowerBuilder's own
        // describe-failure sentinel and is what the sibling harnesses answer.
        public string Describe(string property) => DwSqliteFixture.InvalidPropertyAnswer;

        public long SetFilter(string? filter) => DataWindowBufferStore.DataStoreSuccess;

        public long SetSort(string? sort)
        {
            recorder.Record(OrchestrationCall.DropSort);
            SortSeen = sort;

            return DataWindowBufferStore.DataStoreSuccess;
        }

        public void ClearState() => _carrier.ClearState();

        public void OnInit(ICarrierParentTask parentTask) => _carrier.OnInit(parentTask);

        public ValueTask<long> RetrieveAsync(
            IReadOnlyList<object?> parameters,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("No case in this region retrieves; C-05 owns retrieval.");
    }

    /// <summary>Hands out one store per call, as the production factory does.</summary>
    private sealed class OrchestrationStoreFactory(OrchestrationRecorder recorder) : ISqlDataStoreFactory
    {
        /// <summary>Every store handed out, so a case can read the sort back off the one that was used.</summary>
        internal List<OrchestrationStore> Created { get; } = [];

        public ISqlDataStore Create(CarrierThreadAffinity affinity)
        {
            OrchestrationStore store = new(recorder);
            Created.Add(store);

            return store;
        }
    }

    /// <summary>
    /// The describe surface the modification script is built against, decorating the fixture's own metadata
    /// so the answers are <c>dw_sqlite.srd</c>'s and only the CALL is recorded.
    /// </summary>
    /// <remarks>
    /// <see cref="DescribeUpdateKeyInPlace"/> is recorded because it is the gate on the delete-then-insert
    /// key refresh [<c>:L155</c>], and reaching that gate is precisely what this region asserts about
    /// Phase 5's mainline. The answers themselves are the fixture's: it declares
    /// <c>updatekeyinplace=no</c> [<c>dw_sqlite.srd:L14</c>], so the gate opens.
    /// </remarks>
    private sealed class OrchestrationMetadata(OrchestrationRecorder recorder) : IUpdateTargetMetadata
    {
        private readonly DwSqliteTargetMetadata _fixture = new();

        public int GetColumnCount() => _fixture.GetColumnCount();

        public int GetColumnId(string columnIdProperty) => _fixture.GetColumnId(columnIdProperty);

        public string DescribeUpdateKeyInPlace()
        {
            recorder.Record(OrchestrationCall.DescribeKeyInPlace);

            return _fixture.DescribeUpdateKeyInPlace();
        }
    }

    /// <summary>
    /// The modify surface - <c>sErr = Data.Modify(sModString)</c> [<c>:L145</c>] - following the
    /// EMPTY-STRING-MEANS-SUCCESS convention.
    /// </summary>
    private sealed class OrchestrationModifier(OrchestrationRecorder recorder) : IUpdateTargetModifier
    {
        private readonly List<string> _scripts = [];

        /// <summary>Every script applied, in the order the prepare applied them.</summary>
        internal IReadOnlyList<string> Scripts => _scripts;

        /// <summary>The driver diagnostic to answer; empty is success. Steered per case.</summary>
        internal string Error { get; set; } = string.Empty;

        public string Modify(string modificationScript)
        {
            recorder.Record(OrchestrationCall.Modify);
            _scripts.Add(modificationScript);

            return Error;
        }
    }

    /// <summary>
    /// The update executor the classifier drives - <c>Data.of_ClearState()</c> [<c>:L186</c>] and
    /// <c>Data.Update(true,false)</c> [<c>:L204</c>].
    /// </summary>
    private sealed class OrchestrationTarget(OrchestrationRecorder recorder) : IUpdateTarget
    {
        /// <summary>What the update answers. SUCCESS IS LITERALLY 1 [<c>:L214</c>], not the algebra's zero.</summary>
        internal long UpdateResult { get; set; } = DataWindowBufferStore.DataStoreSuccess;

        /// <summary>The measurement the narrowing reads, or null - the oracle's own state.</summary>
        internal ConcurrencyEvidence? Evidence { get; set; }

        /// <summary>How many times the update ran. One per descriptor on the multi-table path.</summary>
        internal int UpdateCalls { get; private set; }

        /// <summary>The argument pair every invocation was made with.</summary>
        internal List<(bool AcceptText, bool ResetFlag)> Arguments { get; } = [];

        public void ClearState()
        {
        }

        public long Update(bool acceptText, bool resetFlag, CancellationToken cancellationToken = default)
        {
            recorder.Record(OrchestrationCall.Execute);
            UpdateCalls++;
            Arguments.Add((acceptText, resetFlag));

            return UpdateResult;
        }

        public ConcurrencyEvidence? CaptureConcurrencyEvidence() => Evidence;
    }

    /// <summary>The update-capable carrier the orchestration drives, recording each of its own steps.</summary>
    private sealed class OrchestrationCarrier : ISqlUpdateCarrier
    {
        private readonly OrchestrationRecorder _recorder;

        internal OrchestrationCarrier(
            OrchestrationStore store,
            OrchestrationRecorder recorder,
            OrchestrationMetadata metadata,
            OrchestrationModifier modifier,
            OrchestrationTarget target,
            IIdentityValueSource identityValues)
        {
            RecordingStore = store;
            _recorder = recorder;
            Metadata = metadata;
            Modifier = modifier;
            UpdateTarget = target;
            Identity = new IdentityTableSurfaces(new DwSqliteTargetMetadata(), identityValues);
        }

        /// <summary>What <c>data.Create(_sSQLSyntax, ref sError)</c> answers [<c>:L317</c>].</summary>
        internal long CreateResult { get; set; } = DataWindowBufferStore.DataStoreSuccess;

        /// <summary>The diagnostic the create writes into the oracle's by-reference local.</summary>
        internal string CreateErrors { get; set; } = string.Empty;

        /// <summary>What <c>data.SetChanges(_blbUpdateData)</c> answers [<c>:L336</c>].</summary>
        internal long SetChangesResult { get; set; } = DataWindowBufferStore.DataStoreSuccess;

        /// <summary>What <c>data.SetTransObject(transObject)</c> answers [<c>:L350</c>].</summary>
        internal long SetTransObjectResult { get; set; } = DataWindowBufferStore.DataStoreSuccess;

        /// <summary>The payload the apply received, so its release can be observed.</summary>
        internal CarrierState? ChangesSeen { get; private set; }

        /// <summary>How many times the payload was applied.</summary>
        internal int SetChangesCalls { get; private set; }

        internal OrchestrationMetadata Metadata { get; }

        internal OrchestrationModifier Modifier { get; }

        internal OrchestrationTarget UpdateTarget { get; }

        /// <summary>
        /// The store this carrier wraps, typed so the sort it was handed can be read back.
        /// </summary>
        /// <remarks>
        /// ⚠ THE STORE THE FACTORY HANDED OUT IS NOT NECESSARILY THIS ONE. The adapter answers the same
        /// carrier whatever store it is given, so the sort drop lands HERE and not on
        /// <c>OrchestrationStoreFactory.Created</c>. Reading the wrong one is how an ordering assertion
        /// silently observes nothing at all.
        /// </remarks>
        internal OrchestrationStore RecordingStore { get; }

        public ISqlDataStore Store => RecordingStore;

        public IUpdateTarget Target => UpdateTarget;

        public IUpdateTargetMetadata TargetMetadata => Metadata;

        public IUpdateTargetModifier TargetModifier => Modifier;

        public IdentityTableSurfaces Identity { get; }

        public long Create(string sqlSyntax, out string errors)
        {
            _recorder.Record(OrchestrationCall.BuildFromSyntax);
            errors = CreateErrors;

            return CreateResult;
        }

        public long SetChanges(CarrierState? changes)
        {
            _recorder.Record(OrchestrationCall.ApplyChangeset);
            ChangesSeen = changes;
            SetChangesCalls++;

            // MIRRORS THE REAL SURFACE RATHER THAN ANSWERING A FIXED VALUE. A null payload is the oracle's
            // zero-length blob and there is nothing to apply, which the contract answers with the datastore
            // failure value - exactly what IChangesetPayloadCodec.TryApply answers. Modelling it is what lets
            // the payload's RELEASE be observed: a run after the release applies null and therefore takes the
            // no-data arm, which a fake answering success unconditionally would hide.
            return changes is null ? DataWindowBufferStore.DataStoreFailure : SetChangesResult;
        }

        public long SetTransObject(IPooledTransaction? transaction)
        {
            _recorder.Record(OrchestrationCall.AttachTransaction);

            return SetTransObjectResult;
        }

        public ConcurrencyEvidence? CaptureConcurrencyEvidence() => UpdateTarget.CaptureConcurrencyEvidence();

        /// <summary>How many times the teardown released this carrier [<c>:L378</c>].</summary>
        internal int Disposals { get; private set; }

        public void Dispose() => Disposals++;
    }

    /// <summary>Adapts a store into the recording carrier, answering the SAME carrier every time.</summary>
    /// <remarks>
    /// One carrier per harness rather than one per adapt call, so a case can steer it before the run and
    /// read it after. The orchestration adapts exactly once per run on every reachable path, so this is not
    /// a behavioural shortcut.
    /// </remarks>
    private sealed class OrchestrationCarrierAdapter(OrchestrationCarrier carrier) : ISqlUpdateCarrierAdapter
    {
        /// <summary>How many times a store was adapted.</summary>
        internal int Adaptations { get; private set; }

        public ISqlUpdateCarrier Adapt(ISqlDataStore store)
        {
            Adaptations++;

            return carrier;
        }
    }

    /// <summary>
    /// The caller-side proxy, recording the two events one successful invocation publishes -
    /// <c>OnIdentityColumnDataRetrieved</c> [<c>:L243</c>] then <c>OnUpdated</c> [<c>:L247</c>].
    /// </summary>
    private sealed class OrchestrationProxy : ISqlUpdateTaskProxy
    {
        /// <summary>One published counts triple per successful invocation.</summary>
        internal List<UpdateRowCounts> Published { get; } = [];

        /// <summary>One published identity block per invocation that collected values.</summary>
        internal List<(long ColumnId, IReadOnlyList<long?> Primary, IReadOnlyList<long?> Filter)> Identity
        { get; } = [];

        public void OnUpdated(long inserted, long updated, long deleted) =>
            Published.Add(new UpdateRowCounts(inserted, updated, deleted));

        public void OnIdentityColumnDataRetrieved(
            long identityColumnId,
            IReadOnlyList<long?> primaryValues,
            IReadOnlyList<long?> filterValues) =>
            Identity.Add((identityColumnId, primaryValues, filterValues));
    }

    /// <summary>
    /// One composed orchestration: a real <see cref="SqlUpdateTask"/> over recording collaborators, with no
    /// database, no thread and a frozen clock.
    /// </summary>
    private sealed class OrchestrationHarness : IDisposable
    {
        internal OrchestrationHarness(
            IIdentityValueSource? identityValues = null,
            ILogger<SqlUpdateTask>? logger = null)
        {
            Recorder = new OrchestrationRecorder();
            Engine = new OrchestrationEngine(Recorder);
            Host = new OrchestrationHost(Recorder);
            StoreFactory = new OrchestrationStoreFactory(Recorder);
            Redactor = new RecordingSqlRedactor();

            Carrier = new OrchestrationCarrier(
                new OrchestrationStore(Recorder),
                Recorder,
                new OrchestrationMetadata(Recorder),
                new OrchestrationModifier(Recorder),
                new OrchestrationTarget(Recorder),

                // ZERO INSERTED ROWS BY DEFAULT, which closes the identity gate at [:L215] - the oracle's
                // own behaviour for an update that inserted nothing. A case that wants the round trip hands
                // in the fixture's sample source instead.
                identityValues ?? new ZeroIdentityValues());

            Adapter = new OrchestrationCarrierAdapter(Carrier);
            Proxy = new OrchestrationProxy();

            Pool = new TransactionPool(
                Options.Create(new PersistenceOptions()),
                FrozenOrchestrationClock.Instance,
                new PooledTransactionActivator(() => Engine, FrozenOrchestrationClock.Instance));

            Worker = new SqlUpdateTask(
                Host,
                Pool,
                StoreFactory,
                new SqlRetrievalHookActivator(),
                Adapter,

                // THE REAL CLASSIFIER, HANDED A RECORDING REDACTOR. The narrowing, the veto discrimination
                // and the defensive override are the classifier's own; wrapping the redactor is what lets
                // constraint C-F be asserted rather than assumed.
                new ConflictDetector(Redactor),
                FrozenOrchestrationClock.Instance,

                // The worker's logger is REQUIRED rather than optional, so a case that has nothing to assert
                // about the log still supplies one. The de-duplication guard writes its suppression there and
                // nowhere else, which is the one place a case needs to read it.
                logger ?? NullLogger<SqlUpdateTask>.Instance,
                Proxy);
        }

        internal OrchestrationRecorder Recorder { get; }

        internal OrchestrationEngine Engine { get; }

        internal OrchestrationHost Host { get; }

        internal OrchestrationStoreFactory StoreFactory { get; }

        internal OrchestrationCarrier Carrier { get; }

        internal OrchestrationCarrierAdapter Adapter { get; }

        internal OrchestrationProxy Proxy { get; }

        internal RecordingSqlRedactor Redactor { get; }

        internal TransactionPool Pool { get; }

        internal SqlUpdateTask Worker { get; }

        /// <summary>
        /// Configures the run to take the SQL-SYNTAX source arm [<c>:L316-L320</c>].
        /// </summary>
        /// <returns>This harness, for chaining.</returns>
        /// <remarks>
        /// The syntax arm rather than the data-object arm, deliberately: the data-object arm goes through
        /// the datastore CACHE [<c>:L303-L306</c>], whose own restore logic issues further describes,
        /// modifies and filter calls [<c>n_cst_thread_task_sqlbase.sru:L538-L568</c>] that would appear in
        /// the recorded series and obscure the ordering under test. The cache path is owned by
        /// <c>SqlTaskBaseTests</c>. Everything this region asserts happens AFTER the source arm, so the
        /// choice changes nothing it measures.
        /// </remarks>
        internal OrchestrationHarness WithSyntaxSource()
        {
            Assert.Equal(RetCode.OK, Worker.SetSqlSyntax(DwSqliteFixture.RetrieveStatement));

            // A PAYLOAD IS PART OF THE SOURCE CONFIGURATION, because every real update carries one: the
            // caller measures its rows with GetChanges and sends both together
            // [n_cst_threading_task_sqlupdate.sru:L255, :L258]. Cases that are ABOUT the payload overwrite
            // this; cases that are about the sequence around it need it present so the run reaches them.
            Assert.Equal(
                RetCode.OK,
                Worker.SetUpdateData(
                    new CarrierState { Processing = DwSqliteFixture.ProcessingValue },
                    DefaultPayloadRows));

            return this;
        }

        /// <summary>The row count the default payload declares - non-zero, so a refusal is a failure.</summary>
        internal const long DefaultPayloadRows = 3L;

        /// <summary>Runs the orchestration once.</summary>
        /// <returns>The code <c>ondotask</c> answered.</returns>
        internal long Run() => Worker.OnDoTask(TestContext.Current.CancellationToken);

        public void Dispose()
        {
            Worker.Dispose();
            Pool.Dispose();
            Engine.Dispose();
        }
    }

    /// <summary>
    /// An identity value source reporting nothing inserted, which closes the identity gate at
    /// <c>:L215</c>.
    /// </summary>
    private sealed class ZeroIdentityValues : IIdentityValueSource
    {
        public long GetInsertedCount() => 0L;

        public long GetUpdatedCount() => 0L;

        public long GetDeletedCount() => 0L;

        public long RowCount() => 0L;

        public long FilteredCount() => 0L;

        public ItemStatus GetItemStatus(long row, int columnIndex, DwBuffer buffer) => ItemStatus.NotModified;

        public long? GetItemNumber(long row, int columnNumber) => null;

        public long? GetItemNumber(long row, int columnNumber, DwBuffer buffer, bool originalValue) => null;
    }


    // ---- STEP 4 [:L333-L334] - THE SORT IS DROPPED, AND IT IS DROPPED FIRST ----------------------

    /// <summary>
    /// 🔴 The sort is dropped BEFORE the changeset is applied, so the rows commit in COPY ORDER.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE ORACLE SAYS WHY IN ITS OWN COMMENT</b> [<c>:L333</c>]:
    /// <c>//*去掉排序条件,保证数据按拷贝的顺序被提交</c> - "remove the sort condition so the data is
    /// committed in copy order". Row order determines the order the UPDATE, INSERT and DELETE statements are
    /// generated in, so a carrier that kept its sort emits the same statements in a DIFFERENT SEQUENCE. That
    /// is observably different behaviour and a different outcome under a unique constraint or a trigger, so
    /// it is preserved rather than tidied (constraint C-B).
    /// </para>
    /// <para>
    /// <b>THE ORDER IS THE ASSERTION, NOT MERELY THE FACT.</b> Dropping the sort AFTER the apply would leave
    /// the changeset landing into a sorted buffer, which is the state the row-loss defect punishes -
    /// changeset application can lose rows on a sorted DataWindow larger than one block
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L148</c>], and the ordering here
    /// is precisely what keeps this path away from it.
    /// </para>
    /// <para>
    /// AND IT IS DROPPED TO EMPTY, not to the fixture's own sort. <c>dw_sqlite.srd:L14</c> declares
    /// <c>sort="age A salary A "</c>, so "the sort was left alone" and "the sort was dropped" are
    /// distinguishable states here rather than both reading as empty.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSortIsDroppedBeforeTheChangesetIsAppliedSoRowsCommitInCopyOrder()
    {
        using OrchestrationHarness harness = new();
        harness.WithSyntaxSource();
        Assert.Equal(
            RetCode.OK,
            harness.Worker.SetUpdateData(
                new CarrierState { Processing = DwSqliteFixture.ProcessingValue },
                OrchestrationHarness.DefaultPayloadRows));

        Assert.Equal(RetCode.OK, harness.Run());

        harness.Recorder.AssertPrecedes(OrchestrationCall.DropSort, OrchestrationCall.ApplyChangeset);

        // THE WHOLE PREFIX, in the oracle's order: build from syntax [:L317], drop the sort [:L334], apply
        // the changeset [:L336], attach the transaction [:L350], then execute [:L371].
        Assert.Equal(
            [
                OrchestrationCall.BuildFromSyntax,
                OrchestrationCall.DropSort,
                OrchestrationCall.ApplyChangeset,
                OrchestrationCall.AttachTransaction,
                OrchestrationCall.Execute,
            ],
            harness.Recorder.Calls);

        // DROPPED, not preserved, and read off the store the RUN used - which is the carrier's own, not
        // the one the factory handed out. The fixture's own sort expression is a non-empty string, so
        // "dropped" and "left alone" are distinguishable states here rather than both reading as empty.
        Assert.Equal(string.Empty, harness.Carrier.RecordingStore.SortSeen);
        Assert.NotEmpty(DwSqliteFixture.SortExpression);
    }

    // ---- STEP 5 [:L336-L346] - THE TWO-CONDITION "NO UPDATE DATA" RULE --------------------------

    /// <summary>
    /// 🔴 A changeset refusal is a SUCCESS only when the result is exactly <c>-1</c> AND the row count is
    /// exactly zero; every other refusal is <see cref="RetCode.E_INVALID_DATA"/>.
    /// </summary>
    /// <param name="setChangesResult">What the carrier's apply answers.</param>
    /// <param name="updateRows">The row count that accompanied the payload.</param>
    /// <param name="expected">The code the run must answer.</param>
    /// <remarks>
    /// <para>
    /// <b>THE ORACLE'S RULE IS A CONJUNCTION AND IT IS EASY TO PORT AS ONE TEST</b>
    /// [<c>:L339</c>]: <c>if rtCode = -1 and _nUpdateRows = 0 then rtCode = OK ; exit</c>. Both conjuncts
    /// carry weight. A <c>-1</c> with a NON-ZERO count means the sender measured rows with
    /// <c>GetChanges</c> and the payload then failed to apply them - a real failure
    /// [<c>:L343-L345</c>]. A refusal that is not exactly <c>-1</c> is likewise a failure, because the
    /// oracle compares against the literal rather than testing "not success".
    /// </para>
    /// <para>
    /// AN EMPTY UPDATE IS THEREFORE NOT AN ERROR - a caller with nothing to commit gets
    /// <see cref="RetCode.OK"/> and no statement is issued. Collapsing the pair into a single "the apply
    /// failed" check would turn every empty update into a failure, and collapsing it the other way would
    /// silently swallow a payload that was expected to carry rows.
    /// </para>
    /// <para>
    /// THE ROW COUNT IS ASSERTED TO HAVE REACHED THE RUN, because it is the second conjunct and travels
    /// separately from the payload [<c>:L44, :L74-L77</c>].
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ChangesetApplyOutcomes))]
    public void AChangesetRefusalIsSuccessOnlyWhenTheRowCountIsAlsoZero(
        long setChangesResult,
        long updateRows,
        long expected)
    {
        using OrchestrationHarness harness = new();
        harness.WithSyntaxSource();
        harness.Carrier.SetChangesResult = setChangesResult;

        Assert.Equal(
            RetCode.OK,
            harness.Worker.SetUpdateData(
                new CarrierState { Processing = DwSqliteFixture.ProcessingValue },
                updateRows));

        Assert.Equal(updateRows, harness.Worker.GetUpdateRows());
        Assert.Equal(expected, harness.Run());

        // The apply always happens; what differs is what the run does with its answer.
        Assert.Equal(1, harness.Carrier.SetChangesCalls);

        if (expected == RetCode.OK && setChangesResult != DataWindowBufferStore.DataStoreSuccess)
        {
            // THE SUCCESS-WITH-NO-DATA ARM. It exits before the transaction is attached [:L341 precedes
            // :L350], so nothing is executed and nothing is committed - there was nothing to commit.
            Assert.Equal(0, harness.Recorder.CountOf(OrchestrationCall.AttachTransaction));
            Assert.Equal(0, harness.Recorder.CountOf(OrchestrationCall.Execute));

            // AND NOTHING IS REPORTED. The epilogue's rollback arm is not reached on an OK code [:L385].
            Assert.Empty(harness.Host.Errors);
            Assert.Equal(0, harness.Recorder.CountOf(OrchestrationCall.Rollback));

            return;
        }

        if (expected == RetCode.OK)
        {
            // The ordinary success: the payload applied, so the run proceeds to the branch.
            Assert.Equal(1, harness.Recorder.CountOf(OrchestrationCall.Execute));

            return;
        }

        // THE FAILURE ARM, carrying the oracle's own diagnostic verbatim [:L343].
        Assert.Equal(0, harness.Recorder.CountOf(OrchestrationCall.Execute));

        OrchestrationError reported = Assert.Single(harness.Host.Errors);

        Assert.Equal(RetCode.E_INVALID_DATA, reported.Code);
        Assert.Equal(SqlUpdateTask.InvalidUpdateDataMessage, reported.Text);

        // A FAILED RUN IS ROLLED BACK TWICE, and that is the legacy's own shape rather than a defect in
        // the port (constraint C-B). The substrate's error event rolls back BEFORE forwarding the raise
        // [n_cst_thread_task_sqlbase.sru:L731-L734], and the epilogue then rolls back again on its failure
        // arm [:L395]. A rollback of an already-unwound transaction is a no-op, so the duplication is
        // harmless - but it is OBSERVABLE, so it is pinned rather than smoothed over.
        Assert.Equal(2, harness.Recorder.CountOf(OrchestrationCall.Rollback));
    }

    /// <summary>
    /// The apply-result and row-count pairs, with the code each must answer.
    /// </summary>
    /// <remarks>
    /// The fourth row is the one a single-condition port gets wrong: a refusal that is NOT the literal
    /// <c>-1</c> fails the first conjunct and is therefore a failure, even with a zero row count.
    /// </remarks>
    public static TheoryData<long, long, long> ChangesetApplyOutcomes => new()
    {
        // applied, so the run proceeds - the control that keeps the matrix from passing for one reason.
        { DataWindowBufferStore.DataStoreSuccess, 3L, RetCode.OK },

        // -1 AND zero rows: success with no data [:L339-L342].
        { DataWindowBufferStore.DataStoreFailure, 0L, RetCode.OK },

        // -1 with rows the sender measured: a real failure [:L343-L345].
        { DataWindowBufferStore.DataStoreFailure, 1L, RetCode.E_INVALID_DATA },
        { DataWindowBufferStore.DataStoreFailure, 42L, RetCode.E_INVALID_DATA },

        // not -1 at all, so the first conjunct fails whatever the count is.
        { 0L, 0L, RetCode.E_INVALID_DATA },
        { 2L, 0L, RetCode.E_INVALID_DATA },
    };

    // ---- STEP 6 [:L350-L354] - THE TRANSACTION IS ATTACHED BEFORE EITHER BRANCH -----------------

    /// <summary>
    /// The transaction is attached to the carrier before the branch runs, and a failed attach stops the run
    /// with the oracle's own code and diagnostic.
    /// </summary>
    /// <param name="multiTable">Which branch the run takes.</param>
    /// <remarks>
    /// Asserted on BOTH branches because they consume the attachment differently - the multi-table branch
    /// modifies the carrier before executing and the single-table branch executes straight away - and an
    /// attachment sequenced after either would leave the statements running on no transaction at all.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheTransactionIsAttachedBeforeTheBranchAndAFailedAttachStopsTheRun(bool multiTable)
    {
        using (OrchestrationHarness attached = new())
        {
            attached.WithSyntaxSource();
            ConfigureFixtureDescriptors(attached.Worker, multiTable, DwSqliteFixture.UpdateTableName);

            Assert.Equal(RetCode.OK, attached.Run());

            attached.Recorder.AssertPrecedes(
                OrchestrationCall.AttachTransaction,
                OrchestrationCall.Execute);

            if (multiTable)
            {
                attached.Recorder.AssertPrecedes(
                    OrchestrationCall.AttachTransaction,
                    OrchestrationCall.Modify);
            }
        }

        using OrchestrationHarness refused = new();
        refused.WithSyntaxSource();
        ConfigureFixtureDescriptors(refused.Worker, multiTable, DwSqliteFixture.UpdateTableName);
        refused.Carrier.SetTransObjectResult = DataWindowBufferStore.DataStoreFailure;

        Assert.Equal(RetCode.E_INVALID_TRANSACTION, refused.Run());

        // [:L351-L352] the diagnostic is the oracle's, consumed from the type that owns it.
        OrchestrationError reported = Assert.Single(refused.Host.Errors);

        Assert.Equal(RetCode.E_INVALID_TRANSACTION, reported.Code);
        Assert.Equal(SqlUpdateTask.SetTransObjectFailedMessage, reported.Text);

        // NEITHER BRANCH RAN. The refusal exits the body [:L353], so nothing was prepared or executed.
        Assert.Equal(0, refused.Recorder.CountOf(OrchestrationCall.Modify));
        Assert.Equal(0, refused.Recorder.CountOf(OrchestrationCall.Execute));
    }

    // ---- STEP 7 [:L356-L372] - THE BRANCH, WHICH IS THE HEART OF THE ORCHESTRATION --------------

    /// <summary>
    /// 🔴 The multi-table path prepares ONCE PER TABLE, IN ARRAY ORDER, and EACH PREPARE RUNS BEFORE ITS OWN
    /// EXECUTION.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE INTERLEAVING IS THE CONTRACT, NOT AN IMPLEMENTATION DETAIL</b> [<c>:L364-L369</c>]:
    /// <c>for nIndex = 1 to nCount { _of_UpdatePrepare(data,nIndex) ; _of_Update(data) }</c>. Each prepare
    /// REWRITES the carrier's update table, key columns and identity column, so preparing all tables first
    /// and then executing once would run every statement against the LAST table's contract - a write
    /// landing in the wrong table while reporting success. Preparing table n+1 before table n has executed
    /// would do the same thing one step earlier.
    /// </para>
    /// <para>
    /// AAP 0.6.3.4 records multi-table update from one DataWindow as a REAL legacy capability rather than a
    /// theoretical one: the descriptor is an ARRAY with its own reset and append paths, so the contract has
    /// to carry it.
    /// </para>
    /// <para>
    /// THE SCRIPTS THEMSELVES ARE ASSERTED, in order, and the first is compared against the fixture's own
    /// DERIVED expectation rather than a string typed here - so a change in the script's shape fails at the
    /// fixture, once, instead of in every suite that touches it.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheMultiTablePathPreparesOncePerTableEachBeforeItsOwnExecution()
    {
        using OrchestrationHarness harness = new();
        harness.WithSyntaxSource();

        ConfigureFixtureDescriptors(
            harness.Worker,
            multiTable: true,
            DwSqliteFixture.UpdateTableName,
            SecondUpdatableTable);

        Assert.Equal(RetCode.OK, harness.Run());

        // ONE PREPARE AND ONE EXECUTION PER DESCRIPTOR.
        Assert.Equal(2, harness.Recorder.CountOf(OrchestrationCall.Modify));
        Assert.Equal(2, harness.Recorder.CountOf(OrchestrationCall.Execute));
        Assert.Equal(2, harness.Carrier.UpdateTarget.UpdateCalls);

        // STRICTLY ALTERNATING, which is what "each before its own execution" means. Read off the tail of
        // the series so the earlier steps do not have to be restated here.
        Assert.Equal(
            [
                OrchestrationCall.Modify,
                OrchestrationCall.DescribeKeyInPlace,
                OrchestrationCall.Execute,
                OrchestrationCall.Modify,
                OrchestrationCall.DescribeKeyInPlace,
                OrchestrationCall.Execute,
            ],
            harness.Recorder.Calls.Skip(
                harness.Recorder.PositionOf(OrchestrationCall.Modify)));

        // THE SCRIPTS, IN ORDER. The first is the fixture's own derived expectation; the second differs from
        // it only in the update table it names, which is exactly what per-table preparation means.
        Assert.Equal(2, harness.Carrier.Modifier.Scripts.Count);
        Assert.Equal(DwSqliteFixture.ExpectedModificationScript, harness.Carrier.Modifier.Scripts[0]);

        Assert.Contains(
            UpdateWhereBuilder.UpdateTableProperty + " = '" + DwSqliteFixture.UpdateTableName + "'",
            harness.Carrier.Modifier.Scripts[0],
            StringComparison.Ordinal);
        Assert.Contains(
            UpdateWhereBuilder.UpdateTableProperty + " = '" + SecondUpdatableTable + "'",
            harness.Carrier.Modifier.Scripts[1],
            StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 The single-table path executes with NO PREPARE AT ALL.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE ORACLE HAS EXACTLY ONE CALLER FOR <c>_of_UpdatePrepare</c></b>, at [<c>:L365</c>], INSIDE the
    /// multi-table branch. The single-table branch calls the update directly [<c>:L371</c>] and lets the
    /// carrier's own compiled definition govern the update table, the key columns and the identity column.
    /// </para>
    /// <para>
    /// <b>A PORT THAT ALWAYS PREPARED WOULD CHANGE THE OBSERVABLE MODIFY-CALL SEQUENCE</b>, and it would do
    /// more than that: the descriptor a single-table caller sends is INERT, so preparing from it would
    /// overwrite a definition the caller never asked to change and would rewrite every generated statement's
    /// column set. This case is written with a descriptor PRESENT and the switch OFF precisely so that "the
    /// prepare was skipped" is distinguishable from "there was nothing to prepare from".
    /// </para>
    /// <para>
    /// The key-in-place describe is asserted absent as well, because it is only ever reached from inside the
    /// prepare [<c>:L155</c>] - so its absence is independent evidence that the prepare did not run.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSingleTablePathExecutesWithNoPrepareAtAll()
    {
        using OrchestrationHarness harness = new();
        harness.WithSyntaxSource();

        // A DESCRIPTOR IS PRESENT AND THE SWITCH IS OFF - the state that makes the skip observable.
        ConfigureFixtureDescriptors(harness.Worker, multiTable: false, DwSqliteFixture.UpdateTableName);

        Assert.Equal(OneBasedIndex.FirstIndex, harness.Worker.Tables.UpperBound);
        Assert.False(harness.Worker.MultiTableUpdate);

        Assert.Equal(RetCode.OK, harness.Run());

        // NOT PREPARED, AND NOT PARTIALLY PREPARED.
        Assert.Equal(0, harness.Recorder.CountOf(OrchestrationCall.Modify));
        Assert.Equal(0, harness.Recorder.CountOf(OrchestrationCall.DescribeKeyInPlace));
        Assert.Empty(harness.Carrier.Modifier.Scripts);

        // EXECUTED ONCE, DIRECTLY - and with the oracle's own argument pair: accept text TRUE and reset flag
        // FALSE [:L204], so the caller owns the buffer state afterwards.
        Assert.Equal(1, harness.Carrier.UpdateTarget.UpdateCalls);
        Assert.Equal((true, false), Assert.Single(harness.Carrier.UpdateTarget.Arguments));
    }

    /// <summary>
    /// With the switch ON, an empty descriptor array is refused with the oracle's own diagnostic - and with
    /// the switch OFF the same empty array is ordinary.
    /// </summary>
    /// <remarks>
    /// The pair is what makes the asymmetry legible [<c>:L358-L363</c>] versus [<c>:L370-L372</c>]: the
    /// multi-table branch CONSULTS the array and therefore an empty one is an error, while the single-table
    /// branch never consults it at all. The diagnostic is consumed from the production type rather than
    /// retyped, because a transposed character in a CJK literal is invisible in review.
    /// </remarks>
    [Fact]
    public void MultiTableWithNoDescriptorsIsRefusedWhileSingleTableWithNoneIsOrdinary()
    {
        using (OrchestrationHarness refused = new())
        {
            refused.WithSyntaxSource();
            Assert.Equal(RetCode.OK, refused.Worker.SetMultiTableUpdate(true));

            Assert.Equal(RetCode.E_INVALID_ARGUMENT, refused.Run());

            OrchestrationError reported = Assert.Single(refused.Host.Errors);

            Assert.Equal(RetCode.E_INVALID_ARGUMENT, reported.Code);
            Assert.Equal(UpdateWhereBuilder.NoUpdatableTableMessage, reported.Text);

            Assert.Equal(0, refused.Recorder.CountOf(OrchestrationCall.Modify));
            Assert.Equal(0, refused.Recorder.CountOf(OrchestrationCall.Execute));
        }

        using OrchestrationHarness ordinary = new();
        ordinary.WithSyntaxSource();

        Assert.Equal(RetCode.OK, ordinary.Run());
        Assert.Equal(1, ordinary.Recorder.CountOf(OrchestrationCall.Execute));
        Assert.Empty(ordinary.Host.Errors);
    }

    /// <summary>
    /// A prepare that the driver refuses stops the multi-table loop at that table, and the table after it is
    /// neither prepared nor executed.
    /// </summary>
    /// <remarks>
    /// [<c>:L365-L366</c>] <c>if rtCode &lt;&gt; RetCode.OK then exit</c> - a VERBATIM zero comparison rather
    /// than a failure predicate, so a prevention would stop the loop too. The stop matters for correctness
    /// and not only for efficiency: continuing past a failed prepare would execute the next table's
    /// statements against a carrier whose contract was left half-rewritten.
    /// </remarks>
    [Fact]
    public void ARefusedPrepareStopsTheMultiTableLoopAtThatTable()
    {
        using OrchestrationHarness harness = new();
        harness.WithSyntaxSource();

        ConfigureFixtureDescriptors(
            harness.Worker,
            multiTable: true,
            DwSqliteFixture.UpdateTableName,
            SecondUpdatableTable);

        // [:L145-L149] the EMPTY-STRING-MEANS-SUCCESS convention: a non-empty answer is the driver's
        // diagnostic and yields E_INTERNAL_ERROR.
        harness.Carrier.Modifier.Error = "the driver rejected the modification script";

        Assert.Equal(RetCode.E_INTERNAL_ERROR, harness.Run());

        // ONE prepare attempted, NONE executed, and the second table never reached.
        Assert.Equal(1, harness.Recorder.CountOf(OrchestrationCall.Modify));
        Assert.Equal(0, harness.Recorder.CountOf(OrchestrationCall.Execute));

        OrchestrationError reported = Assert.Single(harness.Host.Errors);

        Assert.Equal(RetCode.E_INTERNAL_ERROR, reported.Code);
        Assert.Equal("the driver rejected the modification script", reported.Text);
    }


    // ---- THE EPILOGUE [:L385-L401] - COMMIT, ROLLBACK, AND THE DE-DUPLICATION GUARD --------------

    /// <summary>
    /// The epilogue commits only on a successful run with auto-commit on, and rolls back on every
    /// unsuccessful one whether auto-commit is on or off.
    /// </summary>
    /// <param name="autoCommit">Whether the task was told to commit.</param>
    /// <param name="succeeds">Whether the run reached the OK arm.</param>
    /// <param name="expectedCommits">How many commits the epilogue must issue.</param>
    /// <param name="expectedRollbacks">How many rollbacks it must issue.</param>
    /// <remarks>
    /// <para>
    /// <b>THE TEST AT [:L385] IS EXACT EQUALITY AGAINST OK, NOT A SUCCESS PREDICATE</b>, which is why the
    /// two arms are mutually exclusive and why a cancellation - rewritten to
    /// <see cref="RetCode.CANCELLED"/> at [<c>:L381-L383</c>], after the teardown - takes the rollback arm.
    /// </para>
    /// <para>
    /// THE ROLLBACK IS UNCONDITIONAL ON THE FAILURE ARM [<c>:L395</c>], including for a run that never
    /// issued a statement, and it goes to the TRANSACTION OBJECT DIRECTLY rather than through the task's own
    /// rollback - the oracle's choice, preserved because the task's version additionally validates its own
    /// attachment and would answer a different code for a detached task.
    /// </para>
    /// <para>
    /// ⚠ <b>A REPORTED FAILURE ROLLS BACK TWICE, AND THAT IS THE LEGACY'S SHAPE (constraint C-B).</b> The
    /// substrate's error event rolls back BEFORE forwarding the raise
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L731-L734</c>], and the epilogue
    /// then rolls back again [<c>:L395</c>]. The second is a no-op against an already-unwound transaction, so
    /// nothing is harmed - but the count is observable, so the expectation states it rather than tolerating
    /// either value.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(true, true, 1, 0)]
    [InlineData(false, true, 0, 0)]
    [InlineData(true, false, 0, 2)]
    [InlineData(false, false, 0, 2)]
    public void TheEpilogueCommitsOnlyOnASuccessfulRunAndRollsBackOnEveryOtherOne(
        bool autoCommit,
        bool succeeds,
        int expectedCommits,
        int expectedRollbacks)
    {
        using OrchestrationHarness harness = new();
        harness.WithSyntaxSource();

        Assert.Equal(RetCode.OK, harness.Worker.SetAutoCommit(autoCommit));
        Assert.Equal(autoCommit, harness.Worker.AutoCommit);

        if (!succeeds)
        {
            // A refusal that is not the literal -1 fails whatever the row count is, so this reaches the
            // failure arm without depending on the two-condition rule asserted elsewhere.
            harness.Carrier.SetChangesResult = 0L;
        }

        long result = harness.Run();

        Assert.Equal(succeeds ? RetCode.OK : RetCode.E_INVALID_DATA, result);
        Assert.Equal(expectedCommits, harness.Recorder.CountOf(OrchestrationCall.Commit));
        Assert.Equal(expectedRollbacks, harness.Recorder.CountOf(OrchestrationCall.Rollback));
    }

    /// <summary>
    /// 🔴 The de-duplication guard reports a code the body has ALREADY raised exactly ONCE, and reports a
    /// code the body has not raised.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE GUARD IS <c>if rtCode &lt;&gt; of_GetLastErrorCode() then Event OnError(...)</c></b>
    /// [<c>:L389, :L397</c>]. Its purpose is that a failure the body has already reported in detail is not
    /// reported a second time by the epilogue with a poorer diagnostic - a caller receiving the same code
    /// twice cannot tell whether two things failed or one thing was announced twice.
    /// </para>
    /// <para>
    /// <b>BOTH ROWS OF THIS CASE PRODUCE EXACTLY ONE RAISE, AND THAT IS THE POINT.</b> Counting raises alone
    /// cannot distinguish "the epilogue suppressed a duplicate" from "the epilogue reported the only one", so
    /// the suppression is asserted DIRECTLY through the debug record the guard writes when it takes the
    /// suppressing branch. Without that, removing the guard would still leave a passing test on the first
    /// row - with two identical raises instead of one.
    /// </para>
    /// <para>
    /// The prepare-failure route is used for the suppressed row because the preparer raises through the same
    /// error channel the epilogue would use, carrying the SAME code - which is the only shape in which the
    /// guard can fire. The commit-failure route is the control: the body raised nothing, so the latch is
    /// clear and the epilogue's report goes out.
    /// </para>
    /// </remarks>
    [Fact]
    public void ARepeatedIdenticalErrorIsReportedOnceAndAFreshOneIsAlwaysReported()
    {
        RecordingLogger<SqlUpdateTask> suppressedLog = new();

        using (OrchestrationHarness suppressed = new(logger: suppressedLog))
        {
            suppressed.WithSyntaxSource();
            ConfigureFixtureDescriptors(
                suppressed.Worker,
                multiTable: true,
                DwSqliteFixture.UpdateTableName);

            suppressed.Carrier.Modifier.Error = "the driver rejected the modification script";

            Assert.Equal(RetCode.E_INTERNAL_ERROR, suppressed.Run());

            // ONE raise, from the BODY, and the latch holds its code by the time the epilogue reads it.
            OrchestrationError only = Assert.Single(suppressed.Host.Errors);

            Assert.Equal(RetCode.E_INTERNAL_ERROR, only.Code);
            Assert.Equal(RetCode.E_INTERNAL_ERROR, suppressed.Worker.GetLastErrorCode());

            // AND THE SUPPRESSION GENUINELY HAPPENED rather than there having been nothing to suppress.
            Assert.Contains(
                suppressedLog.Messages,
                message => message.Contains("suppressed a duplicate", StringComparison.Ordinal));
        }

        RecordingLogger<SqlUpdateTask> reportedLog = new();

        using OrchestrationHarness reported = new(logger: reportedLog);
        reported.WithSyntaxSource();
        Assert.Equal(RetCode.OK, reported.Worker.SetAutoCommit(true));
        reported.Engine.CommitSucceeds = false;

        long result = reported.Run();

        // The commit failed, so the epilogue's own code REPLACES the body's OK [:L387].
        Assert.True(Predicates.IsFailed(result));

        // ONE raise, from the EPILOGUE this time, and nothing was suppressed.
        Assert.Equal(result, Assert.Single(reported.Host.Errors).Code);
        Assert.DoesNotContain(
            reportedLog.Messages,
            message => message.Contains("suppressed a duplicate", StringComparison.Ordinal));
    }

    /// <summary>
    /// A cancellation is rolled back but never reported, which is the one failure arm that raises nothing.
    /// </summary>
    /// <remarks>
    /// [<c>:L396</c>] <c>if rtCode &lt;&gt; RetCode.CANCELLED then</c> guards the raise and NOT the rollback,
    /// so the two are deliberately asymmetric: the transaction is always unwound, and a caller who asked to
    /// stop is not told that stopping was an error. The cancellation is observed through the task's own
    /// caller token, which needs no thread and no clock.
    /// </remarks>
    [Fact]
    public void ACancelledRunIsRolledBackButNeverReported()
    {
        using OrchestrationHarness harness = new();
        harness.WithSyntaxSource();

        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Assert.Equal(RetCode.CANCELLED, harness.Worker.OnDoTask(cancelled.Token));

        Assert.Equal(1, harness.Recorder.CountOf(OrchestrationCall.Rollback));
        Assert.Equal(0, harness.Recorder.CountOf(OrchestrationCall.Commit));
        Assert.Empty(harness.Host.Errors);
    }

    // ---- FINALIZE [:L406-L408] AND RESET [:L58-L72] - THE TWO DETERMINISTIC CLEARS ---------------

    /// <summary>
    /// Finalize releases the changeset payload and its row count DETERMINISTICALLY, at a defined point rather
    /// than being left to the collector.
    /// </summary>
    /// <remarks>
    /// <para>
    /// [<c>:L406-L408</c>] <c>_blbUpdateData = Blob("") ; _nUpdateRows = 0</c>. The payload is the largest
    /// thing the task holds and it has already been applied by the time this runs, so releasing it here is
    /// the same discipline <c>docs/PB多线程绕坑提示.md</c> hazard 2 prescribes for cross-thread references.
    /// </para>
    /// <para>
    /// <b>THE PAYLOAD IS PRIVATE, SO ITS RELEASE IS OBSERVED THROUGH THE RUN THAT WOULD HAVE USED IT.</b>
    /// A run after the finalize applies <see langword="null"/> - the oracle's zero-length blob - which is
    /// exactly what a released payload looks like from the carrier's side. Asserting the row count alone
    /// would leave the larger of the two fields unproven, which is the wrong half to leave out.
    /// </para>
    /// <para>
    /// THE MID-RUN RELEASE AT [<c>:L348</c>] IS ASSERTED ALONGSIDE IT, because it is the same discipline
    /// applied at the earliest point it can be: a second run without a fresh payload also sees null, so the
    /// release happened when the payload was applied rather than at the end of the run.
    /// </para>
    /// </remarks>
    [Fact]
    public void FinalizeAndTheApplyItselfBothReleaseTheChangesetPayloadDeterministically()
    {
        using OrchestrationHarness harness = new();
        harness.WithSyntaxSource();

        CarrierState payload = new() { Processing = DwSqliteFixture.ProcessingValue };

        Assert.Equal(RetCode.OK, harness.Worker.SetUpdateData(payload, 7L));
        Assert.Equal(7L, harness.Worker.GetUpdateRows());

        // FIRST RUN: the payload is applied, then released at [:L348].
        Assert.Equal(RetCode.OK, harness.Run());
        Assert.Same(payload, harness.Carrier.ChangesSeen);

        // SECOND RUN, no fresh payload: the release at [:L348] is why nothing arrives. The row count is
        // deliberately still 7, because [:L348] clears the blob and NOT the count - so this run takes the
        // E_INVALID_DATA arm rather than the success-with-no-data one, which is itself the proof that only
        // the blob was cleared.
        Assert.Equal(7L, harness.Worker.GetUpdateRows());
        Assert.Equal(RetCode.E_INVALID_DATA, harness.Run());
        Assert.Null(harness.Carrier.ChangesSeen);

        // NOW THE FINALIZE, which clears BOTH.
        Assert.Equal(RetCode.OK, harness.Worker.SetUpdateData(payload, 7L));
        harness.Worker.OnFinalize();

        Assert.Equal(0L, harness.Worker.GetUpdateRows());

        // With both cleared, the same input reaches the SUCCESS-WITH-NO-DATA arm instead [:L339].
        Assert.Equal(RetCode.OK, harness.Run());
        Assert.Null(harness.Carrier.ChangesSeen);
    }

    /// <summary>
    /// Reset guards on the running flag with NOTHING cleared behind the refusal, then clears every input and
    /// chains to the base LAST.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE GUARD IS THE FIRST STATEMENT</b> [<c>:L60</c>] <c>if #Running then return RetCode.E_BUSY</c>,
    /// and the oracle returns BEFORE its first assignment - so a refused reset cannot half-clear the inputs
    /// and leave the task in a state the caller neither asked for nor can see. Every one of the six inputs is
    /// asserted intact after the refusal, because "nothing was cleared" is only meaningful if it covers all
    /// of them.
    /// </para>
    /// <para>
    /// <b>THE BASE IS CHAINED LAST AND ITS RESULT IS THE ANSWER</b> [<c>:L71</c>] -
    /// <c>return super::of_Reset()</c> is a RETURN rather than a fire-and-forget call. The chaining is
    /// observed through a piece of state the BASE owns and this task does not: the SQL parameter collection,
    /// which the base's reset clears. A port that cleared its own fields and forgot the chain would pass
    /// every other assertion here.
    /// </para>
    /// </remarks>
    [Fact]
    public void ResetGuardsOnTheRunningFlagAndClearsEveryInputBeforeChainingToTheBase()
    {
        using OrchestrationHarness harness = new();

        // Every input set, including one the BASE owns so the chain can be observed.
        Assert.Equal(RetCode.OK, harness.Worker.SetAutoCommit(true));
        Assert.Equal(RetCode.OK, harness.Worker.SetSqlSyntax(DwSqliteFixture.RetrieveStatement));
        ConfigureFixtureDescriptors(harness.Worker, multiTable: true, DwSqliteFixture.UpdateTableName);
        Assert.Equal(
            RetCode.OK,
            harness.Worker.SetUpdateData(
                new CarrierState { Processing = DwSqliteFixture.ProcessingValue },
                ResetProbePayloadRows));
        Assert.Equal(RetCode.OK, harness.Worker.SetParam(DwSqliteFixture.KeyColumnName, "COMPANY"));

        Assert.True(harness.Worker.HasParams());

        // THE REFUSAL, with nothing cleared behind it.
        harness.Worker.IsRunning = () => true;

        Assert.Equal(RetCode.E_BUSY, harness.Worker.Reset());
        Assert.Equal(RetCode.E_BUSY, harness.Worker.ResetUpdatableTables());

        Assert.True(harness.Worker.AutoCommit);
        Assert.True(harness.Worker.MultiTableUpdate);
        Assert.Equal(DwSqliteFixture.RetrieveStatement, harness.Worker.SqlSyntax);
        Assert.Equal(OneBasedIndex.FirstIndex, harness.Worker.Tables.UpperBound);
        Assert.Equal(ResetProbePayloadRows, harness.Worker.GetUpdateRows());
        Assert.True(harness.Worker.HasParams());

        // THE RESET ITSELF.
        harness.Worker.IsRunning = () => false;

        Assert.Equal(RetCode.OK, harness.Worker.Reset());

        Assert.False(harness.Worker.AutoCommit);
        Assert.False(harness.Worker.MultiTableUpdate);
        Assert.Equal(string.Empty, harness.Worker.DataObject);
        Assert.Equal(string.Empty, harness.Worker.SqlSyntax);
        Assert.Equal(OneBasedIndex.EmptyUpperBound, harness.Worker.Tables.UpperBound);
        Assert.Equal(0L, harness.Worker.GetUpdateRows());

        // THE CHAIN: base-owned state cleared too [:L71].
        Assert.False(harness.Worker.HasParams());
        Assert.Equal(OneBasedIndex.EmptyUpperBound, harness.Worker.GetParamCount());

        // AND THE PAYLOAD WITH IT [:L68], observed through the run that would have used it: with no source
        // left either, the run now takes the oracle's no-source arm.
        Assert.Equal(RetCode.E_INVALID_DATAOBJECT, harness.Run());
        Assert.Null(harness.Carrier.ChangesSeen);
    }

    // ---- PHASE 5 - THE `updatekeyinplace=no` MAINLINE, AND THE MANDATORY REDACTOR ----------------

    /// <summary>
    /// 🔴 The FIXTURE'S OWN <c>updatekeyinplace=no</c> setting drives the delete-then-insert key refresh, and
    /// the orchestration reaches it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THIS IS THE MAINLINE PATH AND NOT A RARE BRANCH.</b> The sole evidenced updatable DataWindow
    /// declares <c>updatekeyinplace=no</c> in its own table specification
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14</c>], so every update against the only fixture the
    /// repository has takes this path. Treating it as an edge case is the specific mistake this comment
    /// exists to prevent.
    /// </para>
    /// <para>
    /// <b>WHAT THE SETTING MEANS AND WHY A WORKAROUND IS NEEDED.</b> <c>updatekeyinplace=no</c> makes a key
    /// change a DELETE PLUS AN INSERT rather than an in-place UPDATE. The oracle documents a defect against
    /// itself at [<c>:L151-L154</c>]: when the requested key field is not marked as a key, the modified state
    /// will not generate the delete and insert statements, so the internal modified state must be
    /// force-refreshed. Lines [<c>:L155-L167</c>] implement that by ASSIGNING EACH MODIFIED KEY COLUMN TO
    /// ITSELF [<c>:L163</c>] purely to flip its item status - a statement with no .NET analogue, which is why
    /// the port models original-value and item-status tracking explicitly.
    /// </para>
    /// <para>
    /// <b>THE REFRESH MECHANICS BELONG TO <c>UpdateWhereBuilderTests</c>; THIS CASE OWNS WHETHER THE
    /// ORCHESTRATION REACHES THEM.</b> Two things are asserted, and the ORDER between them is the oracle's:
    /// the modification script carries the setting [<c>:L135-L141</c>], and the runtime describe that gates
    /// the refresh is consulted AFTERWARDS [<c>:L155</c>] - the gate reads the value the modify has just
    /// written, so a port that read it first would gate on the definition's old value.
    /// </para>
    /// <para>
    /// THE CASE-SENSITIVITY IS LOAD-BEARING TOO. The oracle's gate is the exact comparison
    /// <c>Describe(...) = "no"</c> and PowerScript string equality is case-sensitive, so <c>"No"</c> would
    /// close the gate and skip the refresh entirely. The literal is consumed from the production constant.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFixtureKeyInPlaceSettingDrivesTheDeleteThenInsertRefreshOnTheMainlinePath()
    {
        // THE FIXTURE'S OWN SETTING, asserted rather than assumed: this is what makes the path mainline.
        Assert.False(DwSqliteFixture.UpdateKeyInPlace);
        Assert.False(DwSqliteFixture.Descriptor().UpdateKeyInPlace);

        using OrchestrationHarness harness = new();
        harness.WithSyntaxSource();
        ConfigureFixtureDescriptors(harness.Worker, multiTable: true, DwSqliteFixture.UpdateTableName);

        Assert.Equal(RetCode.OK, harness.Run());

        string script = Assert.Single(harness.Carrier.Modifier.Scripts);

        // THE SETTING REACHED THE SCRIPT, in the oracle's own spelling and with the lower-case literal.
        Assert.Contains(
            UpdateWhereBuilder.UpdateKeyInPlaceProperty + " = " + UpdateWhereBuilder.NoLiteral,
            script,
            StringComparison.Ordinal);

        // AND THE CONCURRENCY MODE WITH IT - updatewhere=1 is the fixture's own, and it is what makes the
        // predicate span all six columns' ORIGINAL values (AAP 0.6.3.2).
        Assert.Equal(1L, DwSqliteFixture.UpdateWhereMode);
        Assert.Contains(
            UpdateWhereBuilder.UpdateWhereProperty + " = '"
                + DwSqliteFixture.UpdateWhereMode.ToString(CultureInfo.InvariantCulture) + "'",
            script,
            StringComparison.Ordinal);

        // THE WHOLE SCRIPT, against the fixture's DERIVED expectation - so the six columns, the key column
        // and the identity column are all covered without being restated here.
        Assert.Equal(DwSqliteFixture.ExpectedModificationScript, script);

        // THE GATE WAS REACHED, AND AFTER THE MODIFY [:L145 then :L155].
        Assert.Equal(1, harness.Recorder.CountOf(OrchestrationCall.DescribeKeyInPlace));
        harness.Recorder.AssertPrecedes(
            OrchestrationCall.Modify,
            OrchestrationCall.DescribeKeyInPlace);
    }

    /// <summary>
    /// The identity round trip publishes ONE block per invocation, with the Primary values FORWARD and the
    /// Filter values in the REVERSE source order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// [<c>:L228-L241</c>] walks <c>Primary!</c> forward and <c>Filter!</c>
    /// <b>BACKWARD</b> - <c>for nIndex = nCount to 1 step -1</c> [<c>:L237</c>] - because the filter
    /// buffer's row order is THE REVERSE of the source's, which the oracle records in its own comment at
    /// [<c>:L235</c>]. AAP 0.4.5.4 R9 names this the single most dangerous line in the refactor for
    /// one-based-to-zero-based translation: it LOOKS like a bug, it is NOT a bug, and "correcting" the
    /// direction produces wrong identity values that a row-count assertion would not catch.
    /// </para>
    /// <para>
    /// The expected values come from the fixture, whose Filter expectation is ordered DESCENDING by row for
    /// exactly this reason - so this case and <c>IdentityColumnResolverTests</c> agree by construction rather
    /// than by two independently typed lists.
    /// </para>
    /// <para>
    /// TWO ARRAYS AND NOT ONE, at this level as well as on the wire: a merged array would lose both the
    /// buffer distinction and the ordering asymmetry, and there would be nothing left to compare against.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheIdentityRoundTripPublishesOneBlockPerInvocationWithFilterValuesReversed()
    {
        using OrchestrationHarness harness = new(new DwSqliteSampleRowSource());
        harness.WithSyntaxSource();

        ConfigureFixtureDescriptors(
            harness.Worker,
            multiTable: true,
            DwSqliteFixture.UpdateTableName,
            SecondUpdatableTable);

        Assert.Equal(RetCode.OK, harness.Run());

        // ONE COUNTS TRIPLE PER INVOCATION [:L247]. Accumulation across the loop is the caller-side proxy's
        // job [n_cst_threading_task_sqlupdate.sru:L66-L68], not this task's, so two invocations publish two.
        Assert.Equal(2, harness.Proxy.Published.Count);
        Assert.All(
            harness.Proxy.Published,
            counts => Assert.Equal(DwSqliteFixture.SampleRowCounts, counts));

        // ONE IDENTITY BLOCK PER INVOCATION, appended in table order [:L243].
        Assert.Equal(2, harness.Proxy.Identity.Count);

        foreach ((long columnId, IReadOnlyList<long?> primary, IReadOnlyList<long?> filter)
            in harness.Proxy.Identity)
        {
            Assert.Equal(DwSqliteFixture.ExpectedDiscoveredIdentityColumnNumber, columnId);

            // FORWARD.
            Assert.Equal(DwSqliteFixture.ExpectedPrimaryIdentityValues, primary);

            // BACKWARD - and not the same order as Primary, which is what makes the assertion meaningful.
            Assert.Equal(DwSqliteFixture.ExpectedFilterIdentityValues, filter);
        }
    }

    /// <summary>
    /// 🔴 Every database-error payload C-06 emits passes through the mandatory redactor, and the redactor
    /// masks a statement rather than forwarding it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>CONSTRAINT C-F.</b> The legacy <c>sqlsyntax</c> field carries the COMPLETE generated statement
    /// including interpolated literal values [<c>ws_objects/pfw.thread.ext.pbl.src/dberrordata.srs</c>], and
    /// the legacy logger performs NO redaction at all - which is why the redactor is a required addition
    /// rather than a ported behaviour, and why every payload on the error channel has to go through it.
    /// </para>
    /// <para>
    /// <b>THREE THINGS ARE ASSERTED, AND EACH CLOSES A DIFFERENT WAY THE GUARANTEE COULD DECAY.</b>
    /// First, the redactor CANNOT BE OMITTED: the classifier refuses to be constructed without one, so no
    /// container misconfiguration can produce an unmasked path. Second, the error run OFFERS its payload to
    /// the redactor while the success run offers nothing - so the redactor is on the error path and the error
    /// path alone. Third, the redactor genuinely MASKS: a payload carrying a statement with a literal comes
    /// back with the statement replaced, which is what proves the offer is not a no-op.
    /// </para>
    /// <para>
    /// The masking assertion is made against the REAL <see cref="SqlRedactor"/> rather than the recording
    /// double, because a double that masked would prove only that the double masks.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryDatabaseErrorPayloadPassesThroughTheMandatoryRedactor()
    {
        // 1. THE REDACTOR IS MANDATORY, so an unmasked classifier cannot be constructed at all.
        Assert.Throws<ArgumentNullException>(() => new ConflictDetector(null!));

        // 2. THE ERROR PATH OFFERS ITS PAYLOAD; THE SUCCESS PATH OFFERS NOTHING.
        using (OrchestrationHarness succeeded = new())
        {
            succeeded.WithSyntaxSource();

            Assert.Equal(RetCode.OK, succeeded.Run());
            Assert.Equal(0, succeeded.Redactor.CallCount);
        }

        using OrchestrationHarness failed = new();
        failed.WithSyntaxSource();

        // The update itself refuses, which is the classifier's database-error arm.
        failed.Carrier.UpdateTarget.UpdateResult = DataWindowBufferStore.DataStoreFailure;

        Assert.Equal(RetCode.E_DB_ERROR, failed.Run());
        Assert.True(
            failed.Redactor.CallCount >= 1,
            "Every database-error payload must be offered to the redactor before it leaves the service.");

        // NO STATEMENT TEXT REACHES THE CALLER on this arm - the oracle leaves the field empty here, and the
        // payload still went through the redactor so that a future arm cannot reach the channel unmasked.
        UpdateOutcome outcome = Assert.IsType<UpdateOutcome>(failed.Worker.LastOutcome);
        DbErrorData reported = Assert.NotNull(outcome.DbError);

        Assert.Equal(string.Empty, reported.SqlSyntax);

        // 3. AND THE REDACTOR GENUINELY MASKS, asserted against the production instance.
        DbErrorData masked = new ConflictDetector(SqlRedactor.Instance).Redact(
            DbErrorData.FromStatement(
                -1L,
                "constraint failed",
                "UPDATE COMPANY SET salary = 4800 WHERE id = 7 AND salary = 5200",
                DwBuffer.Primary,
                OneBasedIndex.FirstIndex));

        Assert.DoesNotContain("4800", masked.SqlSyntax, StringComparison.Ordinal);
        Assert.DoesNotContain("5200", masked.SqlSyntax, StringComparison.Ordinal);
        Assert.Contains(SqlRedactor.DefaultPlaceholder, masked.SqlSyntax, StringComparison.Ordinal);

        // The other members are untouched: redaction masks the STATEMENT and nothing else, so the diagnostic
        // a caller needs survives.
        Assert.Equal(-1L, masked.SqlDbCode);
        Assert.Equal("constraint failed", masked.SqlErrText);
        Assert.Equal(DwBuffer.Primary, masked.Buffer);
        Assert.Equal(OneBasedIndex.FirstIndex, masked.Row);
    }

    /// <summary>
    /// The second update table the multi-table cases name.
    /// </summary>
    /// <remarks>
    /// It reuses the fixture's COLUMN names, because the fixture's metadata is the only describe surface that
    /// can resolve a column id - which is also what a real multi-table update over one result set looks like:
    /// one column set, two update tables. Only the table NAME differs, which is exactly the part per-table
    /// preparation rewrites.
    /// </remarks>
    private const string SecondUpdatableTable = "DEPARTMENT";

    /// <summary>
    /// The row count the reset probe declares, chosen so that "still set" and "cleared" are unmistakable.
    /// </summary>
    private const long ResetProbePayloadRows = 9L;

    /// <summary>
    /// Sets the multi-table switch and appends one fixture-shaped descriptor per named table.
    /// </summary>
    /// <param name="worker">The task to configure.</param>
    /// <param name="multiTable">Whether the switch is on.</param>
    /// <param name="tableNames">The update tables to describe, in order.</param>
    /// <remarks>
    /// THE SWITCH IS SET FIRST AND THE DESCRIPTORS AFTER, which is the order the RPC uses and the order the
    /// oracle's own reset forces [<c>:L63</c> clears the switch, <c>:L265-L268</c> sets it]. Each descriptor
    /// carries the fixture's six columns, its key column, its identity column and BOTH optional settings
    /// stated - so the modification script the prepare builds is the fixture's own derived expectation.
    /// </remarks>
    private static void ConfigureFixtureDescriptors(
        SqlUpdateTask worker,
        bool multiTable,
        params string[] tableNames)
    {
        Assert.Equal(RetCode.OK, worker.SetMultiTableUpdate(multiTable));

        foreach (string tableName in tableNames)
        {
            Assert.Equal(
                RetCode.OK,
                worker.AddUpdatableTable(
                    tableName,
                    DwSqliteFixture.ColumnNames,
                    DwSqliteFixture.KeyColumnNames,
                    DwSqliteFixture.IdentityColumnName,
                    DwSqliteFixture.UpdateWhereMode,
                    DwSqliteFixture.UpdateKeyInPlace));
        }
    }

}
