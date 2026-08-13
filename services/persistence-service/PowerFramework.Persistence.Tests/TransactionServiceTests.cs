// ==================================================================================================
//  TransactionServiceTests.cs - the parity suite for contract C-08, persistence.v1.TransactionService
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS SUITE PINS, AND WHY EACH ITEM IS HERE
//  Every fact asserted below was measured in the read-only behavioural oracle under ws_objects/**, and
//  each test names the locator it pins. These are CHARACTERIZATION assertions in Michael Feathers' sense:
//  they record what the legacy ACTUALLY does, including where that is a defect, because constraint C-B
//  forbids correcting legacy behaviour during the migration. A test here failing after an edit means the
//  edit changed observable behaviour - not that the test is wrong.
//
//      the nine-field descriptor order, boolean EIGHTH and userparm NINTH  transactiondata.srs:L4-L12
//      logpass write-only - absent from every response and every rendering  AAP 0.4.2.6, constraint C-F
//      the seven-field fold moving neither autocommit nor userparm          n_cst_thread_trans.sru:L343-L354
//      autocommit never read back by the outbound accessor                  :L410-L416
//      rollback and commit returning FAILED under autocommit               :L185, :L240
//      the rollback preserving all FIVE statement status values             :L163-L183
//      SQLCode = 100 reading as SUCCEEDED, and the kernel algebra differing :L337, :L340
//      the upper-cased ORACLE substring test with MSSQL as the fallback     :L356-L361
//      the ONE-BASED index guard at 0, at -1 and at upper-bound + 1         n_cst_thread_trans_pool.sru:L89, :L120, :L154
//      an unset auto-rollback argument meaning TRUE                         :L383
//
//  THE CLOCK IS SUBSTITUTED, WHICH IS THE POINT OF THE SEAM. Every test drives a fake TimeProvider, so
//  the liveness cache and the pool's idle expiry are deterministic. AAP 0.6.7 requires non-deterministic
//  values to be maskable from BOTH the golden master and the candidate recording, and a suite that read a
//  wall clock could not honour that. The liveness test advances the fake across the 9,999 / 10,000 ms
//  boundary deliberately, because the oracle's comparison is STRICTLY less than [:L198].
//
//  NO DATABASE IS TOUCHED (constraint C-E). The transaction engine is a recording fake: it opens no
//  connection, composes no connection string and provisions nothing. That is what makes the whole matrix
//  runnable in CI, which is what makes the per-service coverage gate reachable (constraint C-H).
//
//  THE PASSWORD USED BELOW IS AN OBVIOUS NON-SECRET and matches no provider credential pattern. It exists
//  only so the write-only assertions have something to search for and fail on.
// ==================================================================================================
using System.Reflection;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Tasks;
using PowerFramework.Persistence.Transactions;
using GeneratedBase =
    global::PowerFramework.Contracts.Persistence.V1.TransactionService.TransactionServiceBase;
using Predicates = PowerFramework.Shared.Kernel.Predicates;
using CommandTaskRegistry = PowerFramework.Persistence.Grpc.CommandTaskRegistry;
using QueryTaskRegistry = PowerFramework.Persistence.Grpc.QueryTaskRegistry;
using TransactionService = PowerFramework.Persistence.Grpc.TransactionService;
using TransactionSession = PowerFramework.Persistence.Grpc.TransactionSession;
using TransactionSessionRegistry = PowerFramework.Persistence.Grpc.TransactionSessionRegistry;
using IUpdateTaskSurface = PowerFramework.Persistence.Grpc.IUpdateTaskSurface;
using UpdateRunResult = PowerFramework.Persistence.Grpc.UpdateRunResult;
using UpdateTaskEntry = PowerFramework.Persistence.Grpc.UpdateTaskEntry;
using UpdateTaskRegistry = PowerFramework.Persistence.Grpc.UpdateTaskRegistry;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// The parity and contract suite for <c>Grpc/TransactionService.cs</c>.
/// </summary>
public sealed class TransactionServiceTests
{
    private const string Password = "placeholder-value-not-a-credential";
    private const string UserParm = "fixture-userparm";

    // ---------------------------------------------------------------------------------------------
    //  THE HARNESS - A FAKE CLOCK, A FAKE ENGINE AND A FAKE QUERY SURFACE. NO DATABASE.
    // ---------------------------------------------------------------------------------------------

    private sealed class FakeClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        // ⚠ THE TIMESTAMP OVERRIDES ARE NOT OPTIONAL FOR A CLOCK THIS TEST CONTROLS. The base
        // TimeProvider answers GetTimestamp() from Stopwatch, which a fake cannot influence, so a
        // component measuring MONOTONIC elapsed time would silently escape this clock and every
        // deterministic assertion below would become a race against real wall time. Answering from the
        // same field GetUtcNow() reads keeps the two readings in lockstep, and a tick-resolution
        // frequency keeps the elapsed conversion exact so the >= boundary lands on the same millisecond
        // it did before.
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _now.UtcTicks;

        internal void Advance(TimeSpan delta) => _now += delta;
    }

    /// <summary>
    /// A server call context whose only interesting member is its cancellation token.
    /// </summary>
    /// <remarks>
    /// Needed by exactly one RPC. <c>BeginSession</c> is the only handler on this contract that reads
    /// its context - it observes the request's cancellation before issuing a connect, because that is
    /// the only verb here that starts new work - so every other call below still passes a null context
    /// deliberately, and that is the evidence those handlers consult nothing.
    /// </remarks>
    private sealed class FakeCallContext(CancellationToken cancellationToken) : ServerCallContext
    {
        private readonly CancellationToken _token = cancellationToken;

        protected override string MethodCore => "/persistence.v1.TransactionService/BeginSession";

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

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) =>
            Task.CompletedTask;
    }

    private sealed class FakeEngine : ITransactionEngine
    {
        internal int Handle { get; set; }

        internal string DbmsValue { get; set; } = string.Empty;

        internal SqlState ConnectResult { get; set; } = SqlState.Succeeded();

        internal SqlState RollbackResult { get; set; } = SqlState.Succeeded();

        internal SqlState CommitResult { get; set; } = SqlState.Succeeded();

        internal SqlState ExecuteResult { get; set; } = SqlState.Succeeded(1);

        internal int ExecuteCalls { get; private set; }

        internal int RollbackCalls { get; private set; }

        internal int DisconnectCalls { get; private set; }

        public int DbHandle => Handle;

        public string Dbms => DbmsValue;

        public bool AutoCommit { get; set; }

        /// <summary>What <see cref="TrySetAutoCommit(bool)"/> answers - the C-08 mode transition's outcome.</summary>
        /// <remarks>
        /// SCRIPTABLE BECAUSE THE REAL ENGINE'S FAILURE ARM IS NOT REACHABLE THROUGH ITS OWN SURFACE. Its
        /// begin is deferred, which is what keeps a second session from inventing a lock conflict the oracle
        /// does not have - and a deferred begin has nothing left to fail on. Scripting it here is the only way
        /// to drive the projection a failed transition must produce.
        /// </remarks>
        internal SqlState SetAutoCommitResult { get; set; } = SqlState.Succeeded();

        /// <summary>
        /// Moves the auto-commit mode and answers <see cref="SetAutoCommitResult"/>.
        /// </summary>
        /// <param name="autoCommit">The mode to put in force.</param>
        /// <returns>Whatever this double was scripted to answer.</returns>
        /// <remarks>
        /// THE MODE IS STILL RECORDED ON THE FAILURE PATH, deliberately: the real engine keeps the mode the
        /// caller asked for and refuses to execute a statement until the transaction it promises exists, so a
        /// double that skipped the assignment when it failed would model a state the engine cannot be in.
        /// </remarks>
        public SqlState TrySetAutoCommit(bool autoCommit)
        {
            AutoCommit = autoCommit;

            return SetAutoCommitResult;
        }

        /// <summary>
        /// How many times <see cref="Connect"/> has been entered, counted atomically.
        /// </summary>
        /// <remarks>
        /// INTERLOCKED BECAUSE THE THING UNDER TEST IS CONCURRENT. The acquisition-race test invokes
        /// <c>BeginSession</c> from several tasks at once, and before the acquisition gate existed those
        /// tasks genuinely entered this member simultaneously - so a non-atomic increment could lose the
        /// very second connect the test is looking for and report the defect as fixed.
        /// </remarks>
        internal int ConnectCalls => Volatile.Read(ref _connectCalls);

        /// <summary>
        /// Run on entry to <see cref="Connect"/>, before the handle is assigned.
        /// </summary>
        /// <remarks>
        /// IT EXISTS TO WIDEN THE RACE WINDOW DELIBERATELY. A connect that returns instantly may finish
        /// before a sibling task has even reached the liveness test, so an unguarded implementation could
        /// pass by luck. Holding inside the connect makes the overlap certain, which is what turns the
        /// race test into a reliable one rather than a flaky one.
        /// </remarks>
        internal Action? OnConnect { get; set; }

        private int _connectCalls;

        public void ApplyConnectionFields(in TransactionData descriptor) => DbmsValue = descriptor.Dbms;

        public SqlState Connect(CancellationToken cancellationToken = default)
        {
            _ = Interlocked.Increment(ref _connectCalls);

            OnConnect?.Invoke();

            Handle = 1;
            return ConnectResult;
        }

        public SqlState Disconnect()
        {
            DisconnectCalls++;
            Handle = 0;
            return SqlState.Succeeded();
        }

        public SqlState Commit() => CommitResult;

        public SqlState Rollback()
        {
            RollbackCalls++;
            return RollbackResult;
        }

        public SqlState Execute(string sqlCommand, CancellationToken cancellationToken = default)
        {
            ExecuteCalls++;
            _ = sqlCommand;
            return ExecuteResult;
        }

        // The BOUND overload. It delegates to the rendered one so this double keeps recording exactly
        // what it recorded before and every existing assertion on it still holds; what the overload adds
        // is that the production seam's parameter-carrying shape is exercised as well.
        public SqlState Execute(in SqlCommandText command, CancellationToken cancellationToken = default) => Execute(command.RenderedText);

        public void Dispose()
        {
        }
    }

    private sealed class FakeQuerySurface : IQueryTransactionSurface
    {
        internal GridSyntaxOutcome Outcome { get; set; } = new("table(column=(x))", string.Empty);

        internal int Calls { get; private set; }

        public GridSyntaxOutcome GridSyntaxFromSql(IPooledTransaction transaction, string sql)
        {
            Calls++;
            _ = transaction;
            _ = sql;
            return Outcome;
        }

        public ValueTask<CountQueryOutcome> Query(
            IPooledTransaction transaction,
            string sql,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("This suite never retrieves; C-05 owns retrieval.");

        public long RaiseBeforeRetrieve(IPooledTransaction transaction, DataWindowCarrier data) =>
            RetCode.OK;

        public void RaiseAfterRetrieve(
            IPooledTransaction transaction,
            DataWindowCarrier data,
            long rowCount)
        {
        }
    }

    /// <summary>Collects the service's own log records so a case can assert what was RECORDED.</summary>
    /// <remarks>
    /// Needed by section 20, where the observable outcome of the behaviour under test is deliberately
    /// UNCHANGED - a non-positive keep-alive expiry is accepted, as the oracle accepts it - and the whole
    /// of the property is that the caller is TOLD. An assertion on the outcome alone cannot distinguish a
    /// substitution that is reported from one that is silent.
    /// </remarks>
    private sealed class CapturingLogger : ILogger<TransactionService>
    {
        internal List<string> Records { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Records.Add($"{logLevel}|{formatter(state, exception)}");
    }

    /// <summary>
    /// Pooled-transaction hooks whose liveness check cancels the request in flight and then reports the
    /// transaction as not live.
    /// </summary>
    /// <remarks>
    /// <para>
    /// IT EXISTS TO REACH ONE ARM AND NOTHING ELSE. The cancellation observation between the acquisition
    /// gate and the connect is reachable only by a token signalled at that instant.
    /// </para>
    /// <para>
    /// <b><c>OnTest</c> AND NOT <c>OnCheck</c>, WHICH IS THE WHOLE REASON THIS DOUBLE IS PRECISE.</b> Both
    /// are consulted by the liveness sequence, but <c>OnCheck</c> is ALSO consulted by <c>IsBroken</c>,
    /// which the pool asks while handing the transaction back - BEFORE the acquisition gate is taken - so a
    /// hook that cancelled there would land in the gate-wait arm instead and prove nothing about this one.
    /// <c>OnTest</c> is consulted from exactly one place, the liveness probe inside <c>IsConnected</c>
    /// [<c>Transactions/TransactionPool</c>], which runs inside the gate.
    /// </para>
    /// <para>
    /// IT IS INERT UNTIL ARMED, and reaching it needs two preconditions the test sets up deliberately: one
    /// session must already have connected the transaction, because the sequence answers false at its first
    /// arm while an entry has never connected; and the clock must be past the liveness CACHE WINDOW, because
    /// inside it the sequence answers true from the cache without probing at all.
    /// </para>
    /// <para>
    /// THE FAILURE CODE IS LOAD BEARING. The probe's answer is tested with the shared kernel's SUCCESS
    /// predicate, under which a prevention would read as connected, so <c>FAILED</c> is what makes the
    /// liveness test report "not live" and carry execution into the branch the arm sits in.
    /// </para>
    /// </remarks>
    private sealed class CancellingCheckHooks : IPooledTransactionHooks
    {
        private CancellationTokenSource? _source;

        /// <summary>How many times the liveness probe has been asked while armed.</summary>
        internal int ProbeCalls { get; private set; }

        /// <summary>Arms the hook against a request's own cancellation source.</summary>
        /// <param name="source">The source to cancel from inside the next liveness probe.</param>
        internal void Arm(CancellationTokenSource source) => _source = source;

        /// <inheritdoc/>
        public long? OnTest()
        {
            if (_source is null)
            {
                // Unarmed: answer nothing, so the sequence takes its own dialect probe exactly as it would
                // with no hooks installed at all.
                return null;
            }

            ProbeCalls++;

            _source.Cancel();

            return RetCode.FAILED;
        }
    }

    private sealed class Harness
    {
        /// <summary>Builds the service over fakes.</summary>
        /// <param name="keepAlive">Whether the pool parks an entry at zero references.</param>
        /// <param name="logger">Optional capturing logger.</param>
        /// <param name="hooks">
        /// Optional pooled-transaction hooks, handed to every transaction the activator creates.
        /// </param>
        /// <remarks>
        /// THE HOOKS PARAMETER EXISTS BECAUSE THE LIVENESS SEQUENCE CONSULTS THEM, and that is the only
        /// deterministic way into the arms that sit between the acquisition gate and the connect: the
        /// pooled transaction asks <c>OnCheck</c> from inside <c>IsConnected</c>
        /// [<c>Transactions/TransactionPool</c>, the liveness sequence], so a hook is the one place a test
        /// can act at that instant without a seam invented for it. It is the same reasoning that put
        /// <see cref="FakeEngine.OnConnect"/> on the engine.
        /// </remarks>
        internal Harness(
            bool keepAlive = false,
            CapturingLogger? logger = null,
            IPooledTransactionHooks? hooks = null)
        {
            Clock = new FakeClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            Engine = new FakeEngine();

            PersistenceOptions options = new()
            {
                TransactionPool = new TransactionPoolOptions { KeepAlive = keepAlive },
            };

            Pool = new TransactionPool(
                Options.Create(options),
                Clock,
                new PooledTransactionActivator(() => Engine, Clock, hooks));

            Registry = new TransactionSessionRegistry(Options.Create(options), Clock, Pool);
            QuerySurface = new FakeQuerySurface();

            // The three task tables are REAL, because EndSession purges them: a task cannot outlive the
            // session it was created against, so the service needs the tables to retire them from.
            QueryTasks = new QueryTaskRegistry(Options.Create(options), Clock);
            UpdateTasks = new UpdateTaskRegistry(Options.Create(options), Clock);
            CommandTasks = new CommandTaskRegistry(Options.Create(options), Clock);

            Log = logger;

            Service = new TransactionService(
                Pool,
                Registry,
                QuerySurface,
                QueryTasks,
                UpdateTasks,
                CommandTasks,
                logger);
        }

        internal CapturingLogger? Log { get; }

        internal FakeClock Clock { get; }

        internal FakeEngine Engine { get; }

        internal TransactionPool Pool { get; }

        internal QueryTaskRegistry QueryTasks { get; }

        internal UpdateTaskRegistry UpdateTasks { get; }

        internal CommandTaskRegistry CommandTasks { get; }

        internal TransactionSessionRegistry Registry { get; }

        internal FakeQuerySurface QuerySurface { get; }

        internal TransactionService Service { get; }
    }

    /// <summary>The context every BeginSession below is called with unless it is testing cancellation.</summary>
    private static readonly ServerCallContext Context = new FakeCallContext(CancellationToken.None);

    private static TransactionDescriptor Descriptor(string dbms = "SQLite") => new()
    {
        Dbms = dbms,
        Servername = "server-1",
        Database = "test.db",
        Logid = "account-1",
        Logpass = Password,
        Dbparm = "DisableBind=1,NCharBind=1",
        Lock = "RU",
        Autocommit = true,
        Userparm = UserParm,
    };

    private static async Task<SessionHandle> Open(Harness harness, string dbms = "SQLite")
    {
        BeginSessionResponse response = await harness.Service.BeginSession(
            new BeginSessionRequest { Descriptor_ = Descriptor(dbms) },
            Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
        Assert.NotNull(response.Session);
        return response.Session;
    }

    // ---------------------------------------------------------------------------------------------
    //  1. THE THIRTEEN RPCs - no missing override, no extra public RPC
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void EveryContractRpcIsOverriddenAndNoExtraPublicRpcExists()
    {
        MethodInfo[] baseRpcs = typeof(GeneratedBase)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(static m => m.IsVirtual && !m.IsFinal)
            .ToArray();

        Assert.Equal(13, baseRpcs.Length);

        foreach (MethodInfo rpc in baseRpcs)
        {
            MethodInfo? overridden = typeof(TransactionService).GetMethod(
                rpc.Name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                binder: null,
                rpc.GetParameters().Select(static p => p.ParameterType).ToArray(),
                modifiers: null);

            Assert.NotNull(overridden);
            Assert.Same(typeof(TransactionService), overridden.DeclaringType);
        }

        // Every declared public instance method is an override of a contract RPC - nothing extra.
        string[] declared = typeof(TransactionService)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(static m => m.Name)
            .ToArray();

        Assert.Equal(
            baseRpcs.Select(static m => m.Name).OrderBy(static n => n, StringComparer.Ordinal),
            declared.OrderBy(static n => n, StringComparer.Ordinal));
    }

    // ---------------------------------------------------------------------------------------------
    //  2. THE NINE-FIELD ROUND TRIP, IN ORDER
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void NineFieldOrderMatchesTheOracleDeclarationOrder()
    {
        // transactiondata.srs:L4-L12 - eight strings then... no: eight strings PLUS the boolean EIGHTH
        // and userparm NINTH.
        string[] expected =
        [
            "dbms", "servername", "database", "logid", "logpass", "dbparm", "lock", "autocommit",
            "userparm",
        ];

        string[] actual = TransactionDescriptor.Descriptor.Fields
            .InFieldNumberOrder()
            .Select(static f => f.Name)
            .ToArray();

        Assert.Equal(expected, actual);
        Assert.Equal(8, TransactionDescriptor.Descriptor.FindFieldByName("autocommit").FieldNumber);
        Assert.Equal(9, TransactionDescriptor.Descriptor.FindFieldByName("userparm").FieldNumber);
    }

    [Fact]
    public async Task DescriptorRoundTripPreservesTheSixCarriedFields()
    {
        Harness harness = new();
        SessionHandle session = await Open(harness);

        GetTransactionDataResponse response = await harness.Service.GetTransactionData(
            new GetTransactionDataRequest { Session = session },
            null!);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
        Assert.Equal("SQLite", response.Descriptor_.Dbms);
        Assert.Equal("server-1", response.Descriptor_.Servername);
        Assert.Equal("test.db", response.Descriptor_.Database);
        Assert.Equal("account-1", response.Descriptor_.Logid);
        Assert.Equal("RU", response.Descriptor_.Lock);

        // The typed projection of dbparm: DisableBind=1 AND NCharBind=1 - the nested rule is satisfied.
        Assert.True(response.Descriptor_.Flags.DisableBind);
        Assert.True(response.Descriptor_.Flags.NcharBind);
    }

    // ---------------------------------------------------------------------------------------------
    //  3. logpass IS WRITE-ONLY - C-F
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void TheResponseViewHasNoSlotForAnyCredentialCapableField()
    {
        string[] names = TransactionDescriptorView.Descriptor.Fields
            .InFieldNumberOrder()
            .Select(static f => f.Name)
            .ToArray();

        Assert.DoesNotContain("logpass", names);
        Assert.DoesNotContain("dbparm", names);
        Assert.DoesNotContain("userparm", names);
    }

    [Fact]
    public async Task NoResponseAndNoDescriptorRenderingEverCarriesThePassword()
    {
        Harness harness = new();
        SessionHandle session = await Open(harness);

        // ToString() of the in-process descriptor - the record hazard the file is written against.
        TransactionData descriptor = new()
        {
            Dbms = "SQLite",
            LogPass = Password,
            DbParm = "DisableBind=1",
            UserParm = UserParm,
        };

        string rendered = descriptor.ToString();
        Assert.DoesNotContain(Password, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(UserParm, rendered, StringComparison.Ordinal);

        // Every response this contract can produce, serialized whole.
        string[] payloads =
        [
            (await harness.Service.BeginSession(
                new BeginSessionRequest { Descriptor_ = Descriptor() }, Context)).ToString(),
            (await harness.Service.GetTransactionData(
                new GetTransactionDataRequest { Session = session }, null!)).ToString(),
            (await harness.Service.GetSessionState(
                new GetSessionStateRequest { Session = session }, null!)).ToString(),
            (await harness.Service.IsConnected(
                new IsConnectedRequest { Session = session }, null!)).ToString(),
            (await harness.Service.GetDatabaseType(
                new GetDatabaseTypeRequest { Session = session }, null!)).ToString(),
            (await harness.Service.SetAutoCommit(
                new SetTransactionAutoCommitRequest { Session = session }, null!)).ToString(),
            (await harness.Service.AutoCommit(
                new AutoCommitRequest { Session = session }, null!)).ToString(),
            (await harness.Service.Commit(
                new CommitRequest { Session = session }, null!)).ToString(),
            (await harness.Service.Rollback(
                new RollbackRequest { Session = session }, null!)).ToString(),
            (await harness.Service.ClearState(
                new ClearStateRequest { Session = session }, null!)).ToString(),
            (await harness.Service.GridSyntaxFromSql(
                new GridSyntaxFromSqlRequest { Session = session, Sql = "SELECT 1" }, null!)).ToString(),
            (await harness.Service.SetBroken(
                new SetBrokenRequest { Session = session }, null!)).ToString(),
            (await harness.Service.EndSession(
                new EndSessionRequest { Session = session }, null!)).ToString(),
        ];

        foreach (string payload in payloads)
        {
            Assert.DoesNotContain(Password, payload, StringComparison.Ordinal);
            Assert.DoesNotContain(UserParm, payload, StringComparison.Ordinal);
            Assert.DoesNotContain("DisableBind", payload, StringComparison.Ordinal);
        }
    }

    // ---------------------------------------------------------------------------------------------
    //  4. THE COPY ASYMMETRY - userparm never copied, autocommit never read back
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task AutocommitIsNeverReadBackByTheOutboundAccessorEvenWhenSuppliedTrue()
    {
        Harness harness = new();

        // The descriptor supplies autocommit = true.
        Assert.True(Descriptor().Autocommit);

        SessionHandle session = await Open(harness);

        GetTransactionDataResponse response = await harness.Service.GetTransactionData(
            new GetTransactionDataRequest { Session = session },
            null!);

        // of_gettransdata copies SEVEN fields and autocommit is not among them
        // [n_cst_thread_trans.sru:L410-L416], so the cleared receiver value survives. Preserved defect.
        Assert.False(response.Descriptor_.Autocommit);
    }

    [Fact]
    public void TheSevenFieldFoldMovesNeitherAutocommitNorUserparm()
    {
        TransactionData receiver = new() { AutoCommit = false, UserParm = "receiver-own" };
        TransactionData source = new()
        {
            Dbms = "SQLite",
            AutoCommit = true,
            UserParm = "source-own",
        };

        TransactionData folded = receiver.WithConnectionFieldsFrom(in source);

        Assert.Equal("SQLite", folded.Dbms);
        Assert.False(folded.AutoCommit);
        Assert.Equal("receiver-own", folded.UserParm);
    }

    // ---------------------------------------------------------------------------------------------
    //  5. ROLLBACK - FAILED under auto-commit, and the five-value preservation
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task RollbackUnderAutoCommitReturnsFailedRatherThanBeingANoOp()
    {
        Harness harness = new();
        SessionHandle session = await Open(harness);

        Assert.Equal(
            WireRetCode.Ok,
            (await harness.Service.SetAutoCommit(
                new SetTransactionAutoCommitRequest { Session = session, Autocommit = true },
                null!)).Status.RetCode);

        RollbackResponse rollback = await harness.Service.Rollback(
            new RollbackRequest { Session = session },
            null!);

        // [:L185] `if AutoCommit then return RetCode.FAILED`
        Assert.Equal(WireRetCode.Failed, rollback.Status.RetCode);
        Assert.Equal(0, harness.Engine.RollbackCalls);

        // The identical guard on commit [:L240].
        CommitResponse commit = await harness.Service.Commit(
            new CommitRequest { Session = session },
            null!);

        Assert.Equal(WireRetCode.Failed, commit.Status.RetCode);
    }

    [Fact]
    public async Task RollbackPreservesAllFiveStatementStatusValues()
    {
        Harness harness = new();
        SessionHandle session = await Open(harness);

        // Stamp a failure the caller must still be able to read AFTER the rollback.
        SqlState original = new(-1, 4711, 7, "original driver message", "original return data");
        Assert.True(harness.Registry.TryResolve(session, out TransactionSession? live));
        live!.Transaction.StampSqlState(in original);

        // The rollback itself succeeds and would otherwise overwrite the state.
        harness.Engine.RollbackResult = SqlState.Succeeded(99);

        RollbackResponse rollback = await harness.Service.Rollback(
            new RollbackRequest { Session = session },
            null!);

        Assert.Equal(WireRetCode.Ok, rollback.Status.RetCode);
        Assert.Equal(1, harness.Engine.RollbackCalls);

        GetSessionStateResponse state = await harness.Service.GetSessionState(
            new GetSessionStateRequest { Session = session },
            null!);

        // All five restored [:L176-L180].
        Assert.Equal(-1, state.SqlCode);
        Assert.Equal(4711, state.SqlDbCode);
        Assert.Equal(7, state.SqlNrows);
        Assert.Equal("original driver message", state.SqlErrText);
        Assert.Equal("original return data", state.SqlReturnData);
    }

    // ---------------------------------------------------------------------------------------------
    //  6. SQLCode = 100 READS AS SUCCEEDED, AND Predicates.IsFailed IS NOT THE DECIDER
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task DriverCodeOneHundredReadsAsSucceededThroughTheTransactionPredicates()
    {
        Harness harness = new();
        SessionHandle session = await Open(harness);

        Assert.True(harness.Registry.TryResolve(session, out TransactionSession? live));
        live!.Transaction.StampSqlState(new SqlState(100, 0, 0, string.Empty, string.Empty));

        GetSessionStateResponse state = await harness.Service.GetSessionState(
            new GetSessionStateRequest { Session = session },
            null!);

        // [:L340] SQLCode >= 0
        Assert.True(state.Succeeded);

        // [:L337] SQLCode < 0
        Assert.False(state.Failed);
        Assert.Equal(100, state.SqlCode);

        // The kernel predicate would answer differently on the values that matter, which is why it is
        // NOT what decides this: it treats PREVENT (1) as a success and excludes CANCELLED (-2) from
        // failure, so it is not a partition at zero at all.
        Assert.True(Predicates.IsSucceeded(RetCode.PREVENT));
        Assert.False(Predicates.IsFailed(RetCode.CANCELLED));
        Assert.False(Predicates.IsSucceeded(RetCode.CANCELLED));
    }

    // ---------------------------------------------------------------------------------------------
    //  7. THE ORACLE-SUBSTRING DIALECT ACCESSOR - SQLite CLASSIFIES AS MSSQL
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("SQLite", DatabaseType.DbtMssql)]
    [InlineData("", DatabaseType.DbtMssql)]
    [InlineData("MSS Microsoft SQL Server", DatabaseType.DbtMssql)]
    [InlineData("PostgreSQL", DatabaseType.DbtMssql)]
    [InlineData("ORACLE", DatabaseType.DbtOracle)]
    [InlineData("oracle", DatabaseType.DbtOracle)]
    [InlineData("O84 Oracle 8.0.4", DatabaseType.DbtOracle)]
    public async Task DialectIsAnUpperCasedSubstringTestWithMssqlAsTheFallback(
        string dbms,
        DatabaseType expected)
    {
        Harness harness = new();
        SessionHandle session = await Open(harness, dbms);

        GetDatabaseTypeResponse response = await harness.Service.GetDatabaseType(
            new GetDatabaseTypeRequest { Session = session },
            null!);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
        Assert.Equal(expected, response.DatabaseType);
    }

    [Fact]
    public void TheDialectEnumerationHasExactlyTwoValuesAndSqliteIsNotOneOfThem()
    {
        Assert.Equal(2, Enum.GetValues<DatabaseType>().Length);
        Assert.Equal(0, (int)DatabaseType.DbtMssql);
        Assert.Equal(1, (int)DatabaseType.DbtOracle);
        Assert.DoesNotContain(
            "sqlite",
            string.Join(',', Enum.GetNames<DatabaseType>()),
            StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------------------------------
    //  8. THE ONE-BASED E_OUT_OF_BOUND GUARD, AT 0 AND AT UPPER-BOUND + 1
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task EndSessionReportsOutOfBoundForAZeroAndForAnOverUpperBoundIndex()
    {
        Harness harness = new();

        // Establish exactly one real entry first, so upper-bound + 1 is computed against a populated
        // pool. ONE-BASED: with one entry the valid index is 1 and the bad indices are 0, -1 and 2.
        IPooledTransaction borrowed = SeedEntry(harness);
        Assert.Equal(1, harness.Pool.UpperBound);

        // A LEASE RATHER THAN AN ORDINAL, so the forged values are the invalid handle and two identities
        // the pool never issued. The guard's job is unchanged - a handle that names no live entry is out of
        // bounds - but a lease can no longer be renumbered into naming somebody else's entry, which is the
        // whole point of the change.
        foreach (long badLease in new[] { 0L, -1L, long.MaxValue })
        {
            TransactionSession? forged = harness.Registry.Register(
                new PoolLease(badLease),
                new TransactionData { Dbms = "SQLite" },
                borrowed,
                out _);

            Assert.NotNull(forged);

            EndSessionResponse response = await harness.Service.EndSession(
                new EndSessionRequest { Session = new SessionHandle { SessionId = forged.SessionId } },
                null!);

            Assert.Equal(WireRetCode.EOutOfBound, response.Status.RetCode);
        }
    }

    [Fact]
    public async Task AValidSessionUsesAOneBasedIndexOfExactlyOneForTheFirstEntry()
    {
        Harness harness = new();
        SessionHandle session = await Open(harness);

        Assert.True(harness.Registry.TryResolve(session, out TransactionSession? live));

        // The session holds a LIVE lease, and the pool still reports the one-based upper bound the oracle
        // reports - the positional API and its renumbering are untouched.
        Assert.True(live!.Lease.IsValid);
        Assert.True(harness.Pool.IsLeaseLive(live.Lease));
        Assert.Equal(1, harness.Pool.UpperBound);

        EndSessionResponse end = await harness.Service.EndSession(
            new EndSessionRequest { Session = session },
            null!);

        Assert.Equal(WireRetCode.Ok, end.Status.RetCode);

        // The handle is single-use: a second end reports the unknown-session code, not a second release.
        EndSessionResponse again = await harness.Service.EndSession(
            new EndSessionRequest { Session = session },
            null!);

        Assert.Equal(WireRetCode.EInvalidTransaction, again.Status.RetCode);
    }

    /// <summary>
    /// Ending a session RETIRES every task that was created against it.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// A TASK CANNOT OUTLIVE ITS SESSION, AND A SURVIVOR IS NOT MERELY UNTIDY. Every task holds the
    /// session's borrowed transaction, so a task left in its table after the session ends names a
    /// transaction the pool has already taken back - and the pool may hand that same transaction to a
    /// different session as soon as its reference count drops, at which point a call on the stale handle
    /// writes through somebody else's transaction. This case proves all three tables are purged.
    /// </remarks>
    [Fact]
    public async Task EndSessionRetiresEveryTaskOwnedByTheSession()
    {
        Harness harness = new();
        SessionHandle session = await Open(harness);

        Assert.True(harness.Registry.TryResolve(session, out TransactionSession? live));
        Assert.NotNull(live);

        UpdateTaskEntry? mineOrNull = harness.UpdateTasks.Register(
            live!.SessionId,
            new StubUpdateSurface(),
            out string mineDiagnostic);
        UpdateTaskEntry mine = Assert.IsType<UpdateTaskEntry>(mineOrNull);
        Assert.Equal(string.Empty, mineDiagnostic);

        UpdateTaskEntry? otherOrNull = harness.UpdateTasks.Register(
            "a-different-session",
            new StubUpdateSurface(),
            out string otherDiagnostic);
        UpdateTaskEntry other = Assert.IsType<UpdateTaskEntry>(otherOrNull);
        Assert.Equal(string.Empty, otherDiagnostic);

        Assert.True(harness.UpdateTasks.TryResolve(
            new TaskHandle { TaskId = mine.TaskId },
            out UpdateTaskEntry? resolved));
        Assert.NotNull(resolved);

        EndSessionResponse end = await harness.Service.EndSession(
            new EndSessionRequest { Session = session },
            null!);

        Assert.Equal(WireRetCode.Ok, end.Status.RetCode);

        // Purged AND disposed - the handle is gone from the table and the surface was torn down.
        Assert.False(harness.UpdateTasks.TryResolve(
            new TaskHandle { TaskId = mine.TaskId },
            out UpdateTaskEntry? purged));
        Assert.Null(purged);
        Assert.True(Assert.IsType<StubUpdateSurface>(mine.Task).Disposed);

        // ANOTHER SESSION'S TASK IS UNTOUCHED. Purging by session, not wholesale: a second session's
        // handles must survive its neighbour ending.
        Assert.True(harness.UpdateTasks.TryResolve(
            new TaskHandle { TaskId = other.TaskId },
            out UpdateTaskEntry? survivor));
        Assert.NotNull(survivor);
        Assert.False(Assert.IsType<StubUpdateSurface>(other.Task).Disposed);
    }

    /// <summary>An update surface that records only whether it was disposed.</summary>
    /// <remarks>
    /// Every member refuses, because these cases are about the TABLE's lifetime rather than about what an
    /// update does. Refusing rather than pretending keeps a mis-wired purge visible.
    /// </remarks>
    private sealed class StubUpdateSurface : IUpdateTaskSurface
    {
        /// <summary>Whether the surface was disposed.</summary>
        internal bool Disposed { get; private set; }

        /// <inheritdoc/>
        public long Reset() => RetCode.E_NO_IMPLEMENTATION;

        /// <inheritdoc/>
        /// <remarks>
        /// False, in keeping with this stub's refuse-everything posture: no source was ever installed on it,
        /// and claiming one would be the pretence the remarks above rule out.
        /// </remarks>
        public bool HasUpdateSource => false;

        /// <inheritdoc/>
        /// <remarks>Empty, in keeping with this stub's refuse-everything posture.</remarks>
        public string DataObject => string.Empty;

        /// <inheritdoc/>
        public long ResetUpdatableTables() => RetCode.E_NO_IMPLEMENTATION;

        /// <inheritdoc/>
        public long SetMultiTableUpdate(bool multiTable) => RetCode.E_NO_IMPLEMENTATION;

        /// <inheritdoc/>
        public long AddUpdatableTable(
            string name,
            IEnumerable<string> updatableColumns,
            IEnumerable<string> keyColumns,
            string identityColumn,
            long? updateWhere,
            bool? updateKeyInPlace) => RetCode.E_NO_IMPLEMENTATION;

        /// <inheritdoc/>
        public long AddUpdatableTable(
            string name,
            IEnumerable<string> updatableColumns,
            IEnumerable<string> keyColumns,
            string identityColumn) => RetCode.E_NO_IMPLEMENTATION;

        /// <inheritdoc/>
        public long SetDataObject(string dataObject) => RetCode.E_NO_IMPLEMENTATION;

        /// <inheritdoc/>
        public long SetSqlSyntax(string sqlSyntax) => RetCode.E_NO_IMPLEMENTATION;

        /// <inheritdoc/>
        public long SetUpdateData(CarrierState? updateData, long updateRows) =>
            RetCode.E_NO_IMPLEMENTATION;

        /// <inheritdoc/>
        public long SetAutoCommit(bool autoCommit) => RetCode.E_NO_IMPLEMENTATION;

        /// <inheritdoc/>
        public UpdateRunResult Execute(CancellationToken cancellationToken) =>
            new() { Code = RetCode.E_NO_IMPLEMENTATION };

        /// <inheritdoc/>
        public void Dispose() => Disposed = true;
    }

    /// <summary>Creates one real pool entry and returns its borrowed transaction.</summary>
    private static IPooledTransaction SeedEntry(Harness harness)
    {
        int real = harness.Pool.AddRef(new TransactionData { Dbms = "SQLite" });
        Assert.Equal(1, real);
        Assert.Equal(RetCode.OK, harness.Pool.Get(real, out IPooledTransaction? borrowed));
        Assert.NotNull(borrowed);
        return borrowed;
    }

    // ---------------------------------------------------------------------------------------------
    //  9. THE CLOCK SEAM - liveness caching is driven only by the injected TimeProvider
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task LivenessCachingIsDrivenSolelyByTheInjectedTimeProvider()
    {
        Harness harness = new();
        SessionHandle session = await Open(harness);

        int probesAfterConnect = harness.Engine.ExecuteCalls;

        // Inside the 10,000 ms window: the answer comes from the cache and no probe runs.
        harness.Clock.Advance(TimeSpan.FromMilliseconds(9_999));

        IsConnectedResponse cached = await harness.Service.IsConnected(
            new IsConnectedRequest { Session = session },
            null!);

        Assert.True(cached.Connected);
        Assert.False(cached.Probed);
        Assert.Equal(probesAfterConnect, harness.Engine.ExecuteCalls);

        // At exactly the window the comparison is strictly-less-than, so it re-probes.
        harness.Clock.Advance(TimeSpan.FromMilliseconds(1));

        IsConnectedResponse probed = await harness.Service.IsConnected(
            new IsConnectedRequest { Session = session },
            null!);

        Assert.True(probed.Connected);
        Assert.True(probed.Probed);
        Assert.Equal(probesAfterConnect + 1, harness.Engine.ExecuteCalls);
    }

    [Fact]
    public void TheServiceSourceReadsNoWallClock()
    {
        string source = File.ReadAllText(LocateServiceSource());

        // Strip comments so the assertions below are about CODE, not about the file's own prose.
        string code = string.Join(
            '\n',
            source
                .Split('\n')
                .Select(static line => line.TrimStart())
                .Where(static line => !line.StartsWith("//", StringComparison.Ordinal)
                    && !line.StartsWith("///", StringComparison.Ordinal)));

        foreach (string forbidden in
            new[] { "DateTime.Now", "DateTime.UtcNow", "Environment.TickCount", "Stopwatch" })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }

        foreach (string forbidden in
            new[]
            {
                "NotImplementedException", "AllowAnonymous", "RpcException", "Sqlsyntax =",
                "/v1/design", "/v1/documents", "/v1/integration", "/v1/scripting",
            })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }
    }

    private static string LocateServiceSource()
    {
        // ⚠ THIS LOCATOR'S MARKER IS SERVICE RELATIVE, NOT REPOSITORY RELATIVE, WHICH IS WHY IT RESOLVES
        // DIFFERENTLY FROM ITS SIBLINGS. The walk below joins "PowerFramework.Persistence/Grpc/..." onto
        // each ancestor, so the directory it is looking for is services/persistence-service - NOT the
        // repository root. Starting the walk at the root would climb AWAY from the file and never find
        // it, so the embedded root is used to form the path DIRECTLY rather than as a walk start.
        //
        // The upward walk is retained beneath it unchanged, so a run with no embedded root behaves
        // exactly as it did before: it climbs out of the test output directory and finds the service
        // directory on the way. That path holds only while the output sits inside the checkout, which is
        // the whole reason the direct form exists - under an out of tree artifacts path no ancestor of
        // the output carries the marker at all. See TestRepositoryRoot.
        if (TestRepositoryRoot.Embedded is { } root)
        {
            string direct = Path.Combine(
                root,
                "services",
                "persistence-service",
                "PowerFramework.Persistence",
                "Grpc",
                "TransactionService.cs");

            // TESTED RATHER THAN TRUSTED. If the tree is ever laid out differently the walk below still
            // gets its chance, so a stale layout assumption here degrades to the original behaviour
            // instead of failing the test.
            if (File.Exists(direct))
            {
                return direct;
            }
        }

        DirectoryInfo? probe = new(AppContext.BaseDirectory);

        while (probe is not null)
        {
            string candidate = Path.Combine(
                probe.FullName,
                "PowerFramework.Persistence",
                "Grpc",
                "TransactionService.cs");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            probe = probe.Parent;
        }

        throw new FileNotFoundException("Grpc/TransactionService.cs was not found above the test output.");
    }

    // ---------------------------------------------------------------------------------------------
    //  10. THE TWO GUARDS, AND THE UNKNOWN-SESSION OUTCOME
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task DisagreeingConnectionFlagsAreRefusedRatherThanResolvedSilently()
    {
        Harness harness = new();

        BeginSessionResponse response = await harness.Service.BeginSession(
            new BeginSessionRequest
            {
                Descriptor_ = Descriptor(),

                // The string says DisableBind=1; the explicit flags claim otherwise.
                Flags = new ConnectionParameterFlags { DisableBind = false, NcharBind = false },
            },
            Context);

        Assert.Equal(WireRetCode.EInvalidArgument, response.Status.RetCode);
        Assert.Contains("disable_bind", response.Status.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, response.Status.ErrorText, StringComparison.Ordinal);
        Assert.Null(response.Session);
        Assert.Equal(0, harness.Pool.UpperBound);
    }

    [Fact]
    public async Task AgreeingConnectionFlagsAndAgreeingPoolSettingsAreAccepted()
    {
        Harness harness = new(keepAlive: true);

        BeginSessionResponse response = await harness.Service.BeginSession(
            new BeginSessionRequest
            {
                Descriptor_ = Descriptor(),
                Flags = new ConnectionParameterFlags { DisableBind = true, NcharBind = true },
                KeepAlive = new PoolKeepAliveSettings
                {
                    KeepAlive = true,

                    // Non-positive means "use the default", exactly as the legacy reads it.
                    KeepAliveExpireSeconds = 0d,
                    LivenessCacheWindowMs = 10_000,
                },
            },
            Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
        Assert.NotNull(response.Session);
    }

    [Fact]
    public async Task AnUnhonourableLivenessWindowIsRefused()
    {
        Harness harness = new();

        BeginSessionResponse response = await harness.Service.BeginSession(
            new BeginSessionRequest
            {
                Descriptor_ = Descriptor(),
                KeepAlive = new PoolKeepAliveSettings { LivenessCacheWindowMs = 0 },
            },
            Context);

        Assert.Equal(WireRetCode.EInvalidArgument, response.Status.RetCode);
        Assert.Contains("liveness-cache window", response.Status.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingDescriptorIsRefusedWithInvalidArgument()
    {
        Harness harness = new();

        BeginSessionResponse response = await harness.Service.BeginSession(
            new BeginSessionRequest(),
            Context);

        Assert.Equal(WireRetCode.EInvalidArgument, response.Status.RetCode);
    }

    [Fact]
    public async Task EveryHandlerReportsInvalidTransactionForAnUnknownHandle()
    {
        Harness harness = new();
        SessionHandle stale = new() { SessionId = "not-a-live-handle" };

        WireRetCode[] outcomes =
        [
            (await harness.Service.EndSession(new EndSessionRequest { Session = stale }, null!))
                .Status.RetCode,
            (await harness.Service.GetTransactionData(
                new GetTransactionDataRequest { Session = stale }, null!)).Status.RetCode,
            (await harness.Service.SetAutoCommit(
                new SetTransactionAutoCommitRequest { Session = stale }, null!)).Status.RetCode,
            (await harness.Service.AutoCommit(new AutoCommitRequest { Session = stale }, null!))
                .Status.RetCode,
            (await harness.Service.Commit(new CommitRequest { Session = stale }, null!))
                .Status.RetCode,
            (await harness.Service.Rollback(new RollbackRequest { Session = stale }, null!))
                .Status.RetCode,
            (await harness.Service.IsConnected(new IsConnectedRequest { Session = stale }, null!))
                .Status.RetCode,
            (await harness.Service.GetDatabaseType(
                new GetDatabaseTypeRequest { Session = stale }, null!)).Status.RetCode,
            (await harness.Service.GetSessionState(
                new GetSessionStateRequest { Session = stale }, null!)).Status.RetCode,
            (await harness.Service.ClearState(new ClearStateRequest { Session = stale }, null!))
                .Status.RetCode,
            (await harness.Service.SetBroken(new SetBrokenRequest { Session = stale }, null!))
                .Status.RetCode,
            (await harness.Service.GridSyntaxFromSql(
                new GridSyntaxFromSqlRequest { Session = stale, Sql = "SELECT 1" }, null!))
                .Status.RetCode,
        ];

        Assert.All(outcomes, code => Assert.Equal(WireRetCode.EInvalidTransaction, code));

        // Absent and blank handles take the same arm, deliberately.
        Assert.Equal(
            WireRetCode.EInvalidTransaction,
            (await harness.Service.Rollback(new RollbackRequest(), null!)).Status.RetCode);
    }

    // ---------------------------------------------------------------------------------------------
    //  11. COMMIT'S UNSET AUTO-ROLLBACK MEANS TRUE, AND E_DB_ERROR CARRIES A REDACTED PAYLOAD
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task AnUnsetAutoRollbackArgumentMeansTrueAndDrivesTheRollbackOnFailure()
    {
        Harness harness = new();
        SessionHandle session = await Open(harness);

        harness.Engine.CommitResult = SqlState.Failed(9001, "commit refused by the driver");

        CommitRequest request = new() { Session = session };
        Assert.False(request.HasAutoRollback);

        CommitResponse response = await harness.Service.Commit(request, null!);

        Assert.Equal(WireRetCode.EDbError, response.Status.RetCode);

        // Unset means TRUE [:L383], so the failure path performed the state-preserving rollback.
        Assert.Equal(1, harness.Engine.RollbackCalls);

        // The driver detail travels; the statement field is masked by the sanctioned projection.
        Assert.NotNull(response.Status.DbError);
        Assert.Equal(9001, response.Status.DbError.Sqldbcode);
        Assert.Equal("commit refused by the driver", response.Status.DbError.Sqlerrtext);
        Assert.Equal(string.Empty, response.Status.DbError.Sqlsyntax);
    }

    [Fact]
    public async Task AnExplicitFalseAutoRollbackSuppressesTheRollback()
    {
        Harness harness = new();
        SessionHandle session = await Open(harness);

        harness.Engine.CommitResult = SqlState.Failed(9002, "commit refused");

        CommitResponse response = await harness.Service.Commit(
            new CommitRequest { Session = session, AutoRollback = false },
            null!);

        Assert.Equal(WireRetCode.EDbError, response.Status.RetCode);
        Assert.Equal(0, harness.Engine.RollbackCalls);
    }

    [Fact]
    public async Task GuardShapedOutcomesCarryNoDriverPayload()
    {
        Harness harness = new();
        SessionHandle session = await Open(harness);

        _ = await harness.Service.SetAutoCommit(
            new SetTransactionAutoCommitRequest { Session = session, Autocommit = true },
            null!);

        RollbackResponse response = await harness.Service.Rollback(
            new RollbackRequest { Session = session },
            null!);

        Assert.Equal(WireRetCode.Failed, response.Status.RetCode);
        Assert.Null(response.Status.DbError);
    }

    /// <summary>
    /// 🔴 <c>SetAutoCommit</c> PROJECTS THE TRANSITION'S REAL OUTCOME, so an engine that could not open the
    /// transaction the mode promises is reported rather than answered <c>OK</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>WHAT THE UNCONDITIONAL <c>OK</c> COST.</b> Moving out of auto-commit obliges the engine to open an
    /// EXPLICIT transaction, because .NET has no implicit one after connecting the way PowerBuilder does.
    /// The handler wrote the mode through a void property and answered success whatever happened, so a
    /// caller could receive <c>OK</c> for a session that held no transaction - and every write it then
    /// issued applied itself immediately while a later rollback undid nothing. Nothing in the response said
    /// so, which is why this is a durability defect rather than a diagnostics gap.
    /// </para>
    /// <para>
    /// <b>THE DRIVER PAYLOAD IS ASSERTED, AND SO IS THE STATEMENT FIELD BEING EMPTY.</b> The failure travels
    /// as <c>E_DB_ERROR</c> with the provider's own code and text, exactly as a failed commit does, and the
    /// statement field stays masked because the transition generated no statement of the caller's - the same
    /// sanctioned projection region 11's commit cases assert.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task SetAutoCommitReportsATransitionTheEngineCouldNotPerform()
    {
        Harness harness = new();
        SessionHandle session = await Open(harness);

        harness.Engine.SetAutoCommitResult = SqlState.Failed(9003, "the explicit transaction was refused");

        SetTransactionAutoCommitResponse refused = await harness.Service.SetAutoCommit(
            new SetTransactionAutoCommitRequest { Session = session, Autocommit = false },
            null!);

        Assert.Equal(WireRetCode.EDbError, refused.Status.RetCode);
        Assert.NotNull(refused.Status.DbError);
        Assert.Equal(9003, refused.Status.DbError.Sqldbcode);
        Assert.Equal("the explicit transaction was refused", refused.Status.DbError.Sqlerrtext);
        Assert.Equal(string.Empty, refused.Status.DbError.Sqlsyntax);

        // AND THE SUCCESS PATH IS UNCHANGED, so the projection is the outcome's rather than a new refusal:
        // the same call answers OK with no driver payload once the engine can perform the transition, and
        // the mode reaches the engine either way.
        harness.Engine.SetAutoCommitResult = SqlState.Succeeded();

        SetTransactionAutoCommitResponse accepted = await harness.Service.SetAutoCommit(
            new SetTransactionAutoCommitRequest { Session = session, Autocommit = true },
            null!);

        Assert.Equal(WireRetCode.Ok, accepted.Status.RetCode);
        Assert.Null(accepted.Status.DbError);
        Assert.True(harness.Engine.AutoCommit);
    }

    [Fact]
    public async Task SetAutoCommitRefusesAnUnknownSessionWithoutTouchingTheEngine()
    {
        Harness harness = new();
        _ = await Open(harness);

        // THE REFUSAL THAT MUST SURVIVE THE PROJECTION CHANGE. Routing the handler through the shared
        // project-and-run helper must not alter what an unnamed session receives, and the engine must stay
        // untouched - a handler that moved the mode first and validated afterwards would leave the mode of
        // whichever session it happened to reach.
        SetTransactionAutoCommitResponse response = await harness.Service.SetAutoCommit(
            new SetTransactionAutoCommitRequest
            {
                Session = new SessionHandle { SessionId = "not-a-session" },
                Autocommit = true,
            },
            null!);

        Assert.Equal(WireRetCode.EInvalidTransaction, response.Status.RetCode);
        Assert.Null(response.Status.DbError);
        Assert.False(harness.Engine.AutoCommit);
    }

    // ---------------------------------------------------------------------------------------------
    //  12. GRID SYNTAX - EMPTY STATEMENT AND EMPTY SYNTAX BOTH YIELD E_INVALID_SQL
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task AnEmptyStatementIsRefusedBeforeTheSurfaceIsConsulted()
    {
        Harness harness = new();
        SessionHandle session = await Open(harness);

        GridSyntaxFromSqlResponse response = await harness.Service.GridSyntaxFromSql(
            new GridSyntaxFromSqlRequest { Session = session, Sql = string.Empty },
            null!);

        Assert.Equal(WireRetCode.EInvalidSql, response.Status.RetCode);
        Assert.Equal("E_INVALID_SQL", response.Status.ErrorText);
        Assert.Equal(0, harness.QuerySurface.Calls);
    }

    [Fact]
    public async Task AnEmptyDerivedSyntaxIsEInvalidSqlAndCarriesTheDiagnostic()
    {
        Harness harness = new();
        SessionHandle session = await Open(harness);

        harness.QuerySurface.Outcome = new GridSyntaxOutcome(string.Empty, "SQL解析失败!");

        GridSyntaxFromSqlResponse response = await harness.Service.GridSyntaxFromSql(
            new GridSyntaxFromSqlRequest { Session = session, Sql = "SELECT bogus" },
            null!);

        Assert.Equal(WireRetCode.EInvalidSql, response.Status.RetCode);
        Assert.Equal("SQL解析失败!", response.Status.ErrorText);
        Assert.Equal(1, harness.QuerySurface.Calls);
    }

    [Fact]
    public async Task ANonEmptyDiagnosticFailsEvenWhenASyntaxWasDerived()
    {
        Harness harness = new();
        SessionHandle session = await Open(harness);

        // The arm where the two legacy consumers DISAGREE: of_query would accept this because the syntax
        // is non-empty [n_cst_thread_trans.sru:L313-L316], while the query task rejects it because the
        // diagnostic is non-empty [n_cst_thread_task_sqlquery.sru:L611-L613]. The contract takes the
        // union, so it must FAIL - reporting OK here would tell a caller a derivation succeeded that one
        // of the two legacy consumers would have refused.
        harness.QuerySurface.Outcome = new GridSyntaxOutcome("table(column=(x))", "partial derivation");

        GridSyntaxFromSqlResponse response = await harness.Service.GridSyntaxFromSql(
            new GridSyntaxFromSqlRequest { Session = session, Sql = "SELECT * FROM COMPANY" },
            null!);

        Assert.Equal(WireRetCode.EInvalidSql, response.Status.RetCode);
        Assert.Equal("partial derivation", response.Status.ErrorText);

        // Empty on failure, as the contract describes it.
        Assert.Equal(string.Empty, response.Syntax);
    }

    [Fact]
    public async Task ASuccessfulDerivationReturnsTheSyntaxAndNoDiagnostic()
    {
        Harness harness = new();
        SessionHandle session = await Open(harness);

        GridSyntaxFromSqlResponse response = await harness.Service.GridSyntaxFromSql(
            new GridSyntaxFromSqlRequest { Session = session, Sql = "SELECT * FROM COMPANY" },
            null!);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
        Assert.Equal("table(column=(x))", response.Syntax);

        // Both tests passed, so the diagnostic is empty by construction.
        Assert.Equal(string.Empty, response.Status.ErrorText);
    }

    [Fact]
    public async Task GridSyntaxFromSqlEnforcesTheReadOnlyStatementGrammarBeforeAnyDerivation()
    {
        // ⚠ THE SECOND READ-SCOPED SURFACE THAT TAKES CALLER-AUTHORED SQL, and it needs the same gate
        // C-05's QuerySpec.sql does: a guard applied to one of the two and not the other would leave the
        // read scope reaching arbitrary SQL through whichever was missed. The grammar itself is pinned in
        // ReadOnlyStatementGuardTests; what this case adds is that this RPC actually applies it, and
        // applies it BEFORE the derivation - the query surface is never called at all.
        //
        // The derivation asks the engine for a result SCHEMA rather than executing the statement, so this
        // is defence in depth rather than the sole barrier. That is precisely why it is asserted: "the
        // provider will not step it" is a property of a collaborator this handler does not own.
        Harness harness = new();
        SessionHandle session = await Open(harness);

        GridSyntaxFromSqlResponse refused = await harness.Service.GridSyntaxFromSql(
            new GridSyntaxFromSqlRequest { Session = session, Sql = "DROP TABLE COMPANY" },
            null!);

        Assert.Equal(WireRetCode.EInvalidSql, refused.Status.RetCode);
        Assert.Equal(string.Empty, refused.Syntax);

        // NOTHING WAS DERIVED, which is what makes this a pre-derivation guard rather than a filter on the
        // way out.
        Assert.Equal(0, harness.QuerySurface.Calls);

        // THE DIAGNOSTIC NAMES THE RULE AND NEVER THE REJECTED TEXT (constraint C-F).
        Assert.DoesNotContain("DROP TABLE", refused.Status.ErrorText, StringComparison.Ordinal);
        Assert.Contains("read scope", refused.Status.ErrorText, StringComparison.Ordinal);
        Assert.Contains("C-07", refused.Status.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GridSyntaxFromSqlStillDerivesForAnOrdinaryReadStatement()
    {
        // THE OTHER HALF OF THE CLAIM. Without it the guard could be a constant refusal and the case above
        // would still pass. The accepted statement carries a refused word as DATA inside a string literal,
        // so it also pins that the grammar is quote-aware rather than a substring search.
        Harness harness = new();
        SessionHandle session = await Open(harness);

        GridSyntaxFromSqlResponse derived = await harness.Service.GridSyntaxFromSql(
            new GridSyntaxFromSqlRequest
            {
                Session = session,
                Sql = "SELECT * FROM COMPANY WHERE NOTE = 'drop table x'",
            },
            null!);

        Assert.Equal(WireRetCode.Ok, derived.Status.RetCode);
        Assert.Equal("table(column=(x))", derived.Syntax);
        Assert.Equal(1, harness.QuerySurface.Calls);
    }

    [Fact]
    public async Task GridSyntaxFromSqlStillReportsAnEmptyStatementThroughTheLegacyGuardFirst()
    {
        // CONSTRAINT C-B. The legacy refuses an empty statement with its own guard and sets the diagnostic
        // to the CODE'S OWN NAME rather than to a sentence [n_cst_thread_trans.sru:L296-L299]. The new
        // scope guard must not intercept that case and answer its own message instead, because the legacy
        // diagnostic is observable.
        Harness harness = new();
        SessionHandle session = await Open(harness);

        GridSyntaxFromSqlResponse refused = await harness.Service.GridSyntaxFromSql(
            new GridSyntaxFromSqlRequest { Session = session, Sql = string.Empty },
            null!);

        Assert.Equal(WireRetCode.EInvalidSql, refused.Status.RetCode);
        Assert.Equal(nameof(RetCode.E_INVALID_SQL), refused.Status.ErrorText);
        Assert.Equal(0, harness.QuerySurface.Calls);
    }

    // ---------------------------------------------------------------------------------------------
    //  13. THE RET CODE CORRESPONDENCE - the review invariant, asserted value for value
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void TheKernelAlgebraAndTheWireEnumAgreeValueForValue()
    {
        (long Kernel, WireRetCode Wire)[] pairs =
        [
            (RetCode.OK, WireRetCode.Ok),
            (RetCode.PREVENT, WireRetCode.Prevent),
            (RetCode.FAILED, WireRetCode.Failed),
            (RetCode.CANCELLED, WireRetCode.Canceled),
            (RetCode.E_INVALID_ARGUMENT, WireRetCode.EInvalidArgument),
            (RetCode.E_INVALID_OBJECT, WireRetCode.EInvalidObject),
            (RetCode.E_INVALID_TRANSACTION, WireRetCode.EInvalidTransaction),
            (RetCode.E_INVALID_SQL, WireRetCode.EInvalidSql),
            (RetCode.E_OUT_OF_BOUND, WireRetCode.EOutOfBound),
            (RetCode.E_DB_ERROR, WireRetCode.EDbError),
        ];

        foreach ((long kernel, WireRetCode wire) in pairs)
        {
            Assert.Equal(kernel, (long)wire);
        }
    }

    // ---------------------------------------------------------------------------------------------
    //  14. CONSTRUCTOR INJECTION - every collaborator is required and validated
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void EveryCollaboratorIsRequiredByTheConstructor()
    {
        Harness harness = new();

        Assert.Throws<ArgumentNullException>(() => new TransactionService(
            null!,
            harness.Registry,
            harness.QuerySurface,
            harness.QueryTasks,
            harness.UpdateTasks,
            harness.CommandTasks));

        Assert.Throws<ArgumentNullException>(() => new TransactionService(
            harness.Pool,
            null!,
            harness.QuerySurface,
            harness.QueryTasks,
            harness.UpdateTasks,
            harness.CommandTasks));

        Assert.Throws<ArgumentNullException>(() => new TransactionService(
            harness.Pool,
            harness.Registry,
            null!,
            harness.QueryTasks,
            harness.UpdateTasks,
            harness.CommandTasks));

        Assert.Throws<ArgumentNullException>(() => new TransactionService(
            harness.Pool,
            harness.Registry,
            harness.QuerySurface,
            null!,
            harness.UpdateTasks,
            harness.CommandTasks));

        Assert.Throws<ArgumentNullException>(() => new TransactionService(
            harness.Pool,
            harness.Registry,
            harness.QuerySurface,
            harness.QueryTasks,
            null!,
            harness.CommandTasks));

        Assert.Throws<ArgumentNullException>(() => new TransactionService(
            harness.Pool,
            harness.Registry,
            harness.QuerySurface,
            harness.QueryTasks,
            harness.UpdateTasks,
            null!));

        // The logger is optional so a unit test can construct the service with behaviour only.
        Assert.NotNull(new TransactionService(
            harness.Pool,
            harness.Registry,
            harness.QuerySurface,
            harness.QueryTasks,
            harness.UpdateTasks,
            harness.CommandTasks));
    }

    [Fact]
    public async Task ANullRequestIsAProgrammingErrorAndThrows()
    {
        Harness harness = new();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            harness.Service.BeginSession(null!, Context));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            harness.Service.Rollback(null!, null!));
    }

    // ---------------------------------------------------------------------------------------------
    //  15. TWO SESSIONS ON ONE DESCRIPTOR SHARE ONE POOL ENTRY, ONE TRANSACTION AND ONE GATE
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task EqualDescriptorsShareOneEntryOneTransactionAndOneGate()
    {
        Harness harness = new();

        SessionHandle first = await Open(harness);
        SessionHandle second = await Open(harness);

        Assert.True(harness.Registry.TryResolve(first, out TransactionSession? a));
        Assert.True(harness.Registry.TryResolve(second, out TransactionSession? b));

        Assert.NotEqual(a!.SessionId, b!.SessionId);
        // Two sessions opened with EQUAL descriptors share one pooled entry, so they share its lease.
        Assert.Equal(a.Lease, b.Lease);
        Assert.Equal(1, harness.Pool.UpperBound);
        Assert.Same(a.Transaction, b.Transaction);

        // The gate is per TRANSACTION, so co-borrowers must share it or they would race on one object.
        // CS9216 fires on ANY conversion of System.Threading.Lock to object, and an identity
        // assertion necessarily performs one. Suppressed for this single assertion, because the
        // property being pinned - that co-borrowers share one gate - is exactly reference identity.
#pragma warning disable CS9216
        Assert.Same(a.Gate, b.Gate);
#pragma warning restore CS9216
    }

    // ---------------------------------------------------------------------------------------------
    //  16. THE CLOSING STATE - THE LIFECYCLE IS ATOMIC, NOT MERELY ORDERED
    //
    //  Retiring the handle from the registry is not on its own enough to make an end atomic: a request
    //  that resolved the session a moment earlier is already past that lookup, and would otherwise enter
    //  the session gate AFTER the release and operate on a transaction that has been handed back to the
    //  pool - or, with keep-alive on, handed to somebody else. The closing flag is set inside the same
    //  gate every other operation tests inside, so the two cannot interleave. A registered-but-closing
    //  session is exactly the state that race produces, so the suite constructs it directly rather than
    //  trying to win a race.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task EveryOperationRefusesASessionThatIsAlreadyClosing()
    {
        Harness harness = new();
        SessionHandle opened = await Open(harness);

        Assert.True(harness.Registry.TryResolve(opened, out TransactionSession? live));

        // The state an interleaved EndSession leaves behind: still resolvable, already retiring.
        live!.MarkClosing();

        SessionHandle session = opened;

        string[] refusals =
        [
            (await harness.Service.GetTransactionData(
                new GetTransactionDataRequest { Session = session }, null!)).Status.RetCode.ToString(),
            (await harness.Service.IsConnected(
                new IsConnectedRequest { Session = session }, null!)).Status.RetCode.ToString(),
            (await harness.Service.GetDatabaseType(
                new GetDatabaseTypeRequest { Session = session }, null!)).Status.RetCode.ToString(),
            (await harness.Service.SetAutoCommit(
                new SetTransactionAutoCommitRequest { Session = session, Autocommit = true },
                null!)).Status.RetCode.ToString(),
            (await harness.Service.Commit(
                new CommitRequest { Session = session }, null!)).Status.RetCode.ToString(),
            (await harness.Service.Rollback(
                new RollbackRequest { Session = session }, null!)).Status.RetCode.ToString(),
            (await harness.Service.ClearState(
                new ClearStateRequest { Session = session }, null!)).Status.RetCode.ToString(),
            (await harness.Service.GetSessionState(
                new GetSessionStateRequest { Session = session }, null!)).Status.RetCode.ToString(),
            (await harness.Service.AutoCommit(
                new AutoCommitRequest { Session = session }, null!)).Status.RetCode.ToString(),
            (await harness.Service.SetBroken(
                new SetBrokenRequest { Session = session }, null!)).Status.RetCode.ToString(),
        ];

        // ONE code for all ten, and it is the UNKNOWN-SESSION code: from the caller's point of view the
        // handle it named is gone, so a closing session is indistinguishable from a retired one.
        Assert.All(refusals, code => Assert.Equal(WireRetCode.EInvalidTransaction.ToString(), code));

        // AND THE TRANSACTION WAS NEVER TOUCHED. The engine's rollback and commit counters are the proof:
        // a refusal that had fallen through to the operation would have moved one of them.
        Assert.Equal(0, harness.Engine.RollbackCalls);
    }

    [Fact]
    public async Task AnEndReleasesExactlyOnceEvenWhenTheHandleIsPresentedTwice()
    {
        Harness harness = new();
        SessionHandle session = await Open(harness);

        Assert.True(harness.Registry.TryResolve(session, out TransactionSession? live));
        PoolLease lease = live!.Lease;

        Assert.Equal(
            WireRetCode.Ok,
            (await harness.Service.EndSession(
                new EndSessionRequest { Session = session }, null!)).Status.RetCode);

        // The lease is dead, so nothing that still holds it can reach an entry.
        Assert.False(harness.Pool.IsLeaseLive(lease));
        Assert.Equal(1, harness.Engine.DisconnectCalls);

        Assert.Equal(
            WireRetCode.EInvalidTransaction,
            (await harness.Service.EndSession(
                new EndSessionRequest { Session = session }, null!)).Status.RetCode);

        // STILL EXACTLY ONE. A second end neither released again nor disconnected again.
        Assert.Equal(1, harness.Engine.DisconnectCalls);
    }

    // ---------------------------------------------------------------------------------------------
    //  17. THE REQUEST'S CANCELLATION - OBSERVED BY THE ONE VERB THAT ISSUES WORK, AND BY NO OTHER
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task OnlyBeginSessionReadsItsContextAndEveryOtherHandlerRunsWithoutOne()
    {
        // The twelve other handlers are called throughout this suite with a NULL context and none of
        // them throws, which is the evidence that they consult nothing. Restated here as one explicit
        // sweep so the property is asserted rather than merely implied by other tests passing.
        Harness harness = new();
        SessionHandle session = await Open(harness);

        Assert.Equal(
            WireRetCode.Ok,
            (await harness.Service.GetTransactionData(
                new GetTransactionDataRequest { Session = session }, null!)).Status.RetCode);
        Assert.Equal(
            WireRetCode.Ok,
            (await harness.Service.GetSessionState(
                new GetSessionStateRequest { Session = session }, null!)).Status.RetCode);
        Assert.Equal(
            WireRetCode.Ok,
            (await harness.Service.IsConnected(
                new IsConnectedRequest { Session = session }, null!)).Status.RetCode);
        Assert.Equal(
            WireRetCode.Ok,
            (await harness.Service.GetDatabaseType(
                new GetDatabaseTypeRequest { Session = session }, null!)).Status.RetCode);
        Assert.Equal(
            WireRetCode.Ok,
            (await harness.Service.SetAutoCommit(
                new SetTransactionAutoCommitRequest { Session = session, Autocommit = false },
                null!)).Status.RetCode);
        Assert.Equal(
            WireRetCode.Ok,
            (await harness.Service.Commit(new CommitRequest { Session = session }, null!))
                .Status.RetCode);
        Assert.Equal(
            WireRetCode.Ok,
            (await harness.Service.Rollback(new RollbackRequest { Session = session }, null!))
                .Status.RetCode);
        Assert.Equal(
            WireRetCode.Ok,
            (await harness.Service.ClearState(new ClearStateRequest { Session = session }, null!))
                .Status.RetCode);
        Assert.Equal(
            WireRetCode.Ok,
            (await harness.Service.GridSyntaxFromSql(
                new GridSyntaxFromSqlRequest { Session = session, Sql = "SELECT 1" },
                null!)).Status.RetCode);
        Assert.Equal(
            WireRetCode.Ok,
            (await harness.Service.SetBroken(new SetBrokenRequest { Session = session }, null!))
                .Status.RetCode);
        Assert.Equal(
            WireRetCode.Ok,
            (await harness.Service.EndSession(new EndSessionRequest { Session = session }, null!))
                .Status.RetCode);

        // BeginSession is the exception, and it is a hard one: it reads the token, so a null context is
        // a programming error there rather than a tolerated absence.
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            harness.Service.BeginSession(
                new BeginSessionRequest { Descriptor_ = Descriptor() },
                null!));
    }

    [Fact]
    public async Task ACancelledRequestIsRefusedBeforeAConnectIsIssuedAndLeavesNoPoolReference()
    {
        Harness harness = new();

        using CancellationTokenSource source = new();
        await source.CancelAsync();

        BeginSessionResponse response = await harness.Service.BeginSession(
            new BeginSessionRequest { Descriptor_ = Descriptor() },
            new FakeCallContext(source.Token));

        // CANCELLED, and NOT the E_INVALID_TRANSACTION a genuine connect failure answers - the two
        // outcomes are distinct because their causes are.
        Assert.Equal(WireRetCode.Cancelled, response.Status.RetCode);
        Assert.Null(response.Session);

        // THE THREE THINGS A CANCELLATION MUST NOT LEAVE BEHIND.
        // No connect was issued at all: the engine's handle is still zero, which is what it would not be
        // had Connect run [FakeEngine.Connect sets Handle = 1].
        Assert.Equal(0, harness.Engine.Handle);

        // No pool reference is pinned - the entry the acquisition took was released on this path exactly
        // as on every other failure path.
        Assert.Equal(0, harness.Pool.UpperBound);
        Assert.False(harness.Pool.Exists(new TransactionData
        {
            Dbms = "SQLite",
            ServerName = "server-1",
            Database = "test.db",
            LogId = "account-1",
            LogPass = Password,
            DbParm = "DisableBind=1,NCharBind=1",
            Lock = "RU",
            AutoCommit = true,
            UserParm = UserParm,
        }));

        // And no session exists, so there is nothing the caller could end. The diagnostic says exactly
        // that, and it quotes no descriptor field (constraint C-F).
        //
        // 🔴 IT IS THE GATE-WAIT ARM'S SENTENCE NOW, because that arm is reached first: the acquisition
        // gate honours the request token, and a semaphore consults an already-signalled token before it
        // considers admitting a waiter, so an already-cancelled caller never reaches the liveness test at
        // all. The pre-connect arm below is still live for a token signalled between the two, which
        // ACancellationObservedInsideTheLivenessGateIsRefusedBeforeTheConnect drives.
        Assert.Equal(0, harness.Registry.Count);
        Assert.Contains("no session", response.Status.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, response.Status.ErrorText, StringComparison.Ordinal);
    }

    /// <summary>
    /// A cancellation observed after the acquisition gate but before the connect takes the pre-connect arm,
    /// which reports that no connection was opened.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ARM BETWEEN THE OTHER TWO, AND IT NEEDS A HOOK TO REACH. The gate wait refuses an
    /// already-signalled token, and the pre-registration check catches one signalled later, so this arm is
    /// reached only by a cancellation that lands while the liveness test is running. The pooled transaction
    /// asks its <c>OnTest</c> hook from inside that test and from nowhere else, so a hook cancelling there
    /// puts the request at exactly the instant this arm exists for - and answering a failure makes the
    /// liveness test report "not live", which is what carries execution into the arm's enclosing branch.
    /// </para>
    /// <para>
    /// KEEP-ALIVE, A FIRST SESSION AND A CLOCK ADVANCE ARE ALL REQUIRED. The liveness sequence answers false
    /// at its first arm while the entry has never connected, and answers TRUE from its cache while the
    /// configured window has not elapsed - so the probe, and therefore the hook, is unreachable until one
    /// session has connected and the clock has moved past that window.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ACancellationObservedInsideTheLivenessGateIsRefusedBeforeTheConnect()
    {
        using CancellationTokenSource source = new();

        CancellingCheckHooks hooks = new();
        Harness harness = new(keepAlive: true, hooks: hooks);

        SessionHandle first = await Open(harness);
        Assert.Equal(1, harness.Engine.ConnectCalls);

        // PAST THE LIVENESS CACHE WINDOW, so the next liveness test actually probes instead of answering
        // from the cache. Read from the production constant rather than restated, so the two cannot drift.
        harness.Clock.Advance(
            TimeSpan.FromMilliseconds(PooledTransaction.LivenessCacheWindowMilliseconds + 1));

        // Arm the hook: the next liveness probe cancels the request and answers "not live", which is the
        // pre-connect arm's exact precondition.
        hooks.Arm(source);

        BeginSessionResponse response = await harness.Service.BeginSession(
            new BeginSessionRequest { Descriptor_ = Descriptor() },
            new FakeCallContext(source.Token));

        Assert.Equal(WireRetCode.Cancelled, response.Status.RetCode);
        Assert.Null(response.Session);

        // THE PROBE RAN, so the arm really was reached through the liveness test rather than by some other
        // route that happens to answer the same code.
        Assert.Equal(1, hooks.ProbeCalls);

        // NO SECOND CONNECT WAS ISSUED, which is what the arm exists to prevent.
        Assert.Equal(1, harness.Engine.ConnectCalls);

        // THE PRE-CONNECT ARM'S OWN SENTENCE, which is how this case is told apart from the other two.
        Assert.Contains(
            "before a connection was opened",
            response.Status.ErrorText,
            StringComparison.Ordinal);

        // The first caller keeps its session and its pool entry; nothing was torn down.
        Assert.Equal(1, harness.Pool.UpperBound);
        Assert.Equal(1, harness.Registry.Count);
        Assert.True(harness.Registry.TryResolve(first, out _));
        Assert.Equal(0, harness.Engine.DisconnectCalls);
    }

    [Fact]
    public async Task AnUncancelledRequestConnectsExactlyAsBefore()
    {
        // The control for the test above: the same call with a live token is unaffected by the new arm,
        // so the cancellation observation costs the ordinary path nothing.
        Harness harness = new();

        BeginSessionResponse response = await harness.Service.BeginSession(
            new BeginSessionRequest { Descriptor_ = Descriptor() },
            new FakeCallContext(CancellationToken.None));

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
        Assert.NotNull(response.Session);
        Assert.Equal(1, harness.Engine.Handle);
        Assert.Equal(1, harness.Pool.UpperBound);
    }

    /// <summary>
    /// 🔴 A cancellation arriving after the shared transaction is connected does not undo the connect or the
    /// FIRST caller's session - and the cancelled caller is refused rather than issued a session it can
    /// never end.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE CANCELLED CALLER IS REFUSED, AND THAT REFUSAL IS THE LOAD-BEARING HALF OF THIS CASE.</b>
    /// Asserting that the second caller receives a session is the tempting reading, on the grounds that a
    /// live pooled transaction is not reconnected, so "no work is issued and the cancellation has nothing
    /// to refuse". The premise is right and the conclusion costs a permanent leak: a caller whose token is already
    /// signalled IS GONE, gRPC discards the response, and the handle that response carried is the ONLY way
    /// that session could ever be named - so the session could never be ended. Nothing reclaimed it either,
    /// because the registry has no expiry sweep and the pool reference the session held stopped the pool's
    /// own idle collection from reaping the entry. Repeating the cancellation walked the caller up to its
    /// handle ceiling against sessions it did not know it owned.
    /// </para>
    /// <para>
    /// THE THREE PROPERTIES PINNED ALONGSIDE IT ARE THE ONES A CANCELLATION MUST NOT DISTURB: the connect is not undone, the
    /// FIRST caller's session is untouched, and one pool entry still serves the descriptor. A cancellation
    /// is a refusal to be issued a handle, never a rollback of work already done - which is exactly what the
    /// original name says, now asserted over the caller it actually applies to.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ACancellationArrivingWhenTheTransactionIsConnectedIsRefusedAndUndoesNothing()
    {
        Harness harness = new(keepAlive: true);

        SessionHandle first = await Open(harness);
        Assert.Equal(1, harness.Engine.Handle);
        Assert.Equal(1, harness.Engine.ConnectCalls);

        using CancellationTokenSource source = new();
        await source.CancelAsync();

        BeginSessionResponse second = await harness.Service.BeginSession(
            new BeginSessionRequest { Descriptor_ = Descriptor() },
            new FakeCallContext(source.Token));

        // 🔴 THE CANCELLED CALLER IS REFUSED AND IS GIVEN NOTHING TO END.
        Assert.Equal(WireRetCode.Cancelled, second.Status.RetCode);
        Assert.Null(second.Session);

        // NOTHING WAS UNDONE. The connect stands, no disconnect was issued, and the descriptor is still
        // served by exactly one pool entry - so the refusal cost the first caller nothing.
        Assert.Equal(1, harness.Engine.ConnectCalls);
        Assert.Equal(0, harness.Engine.DisconnectCalls);
        Assert.Equal(1, harness.Engine.Handle);
        Assert.Equal(1, harness.Pool.UpperBound);

        // THE FIRST CALLER'S SESSION IS UNTOUCHED, and it is the ONLY session: the second caller left no
        // orphan behind.
        Assert.True(harness.Registry.TryResolve(first, out _));
        Assert.Equal(1, harness.Registry.Count);
    }

    /// <summary>
    /// 🔴 Repeated cancellations over a connected shared descriptor leave no orphan session and consume no
    /// pool reference, which is the leak the pre-registration check exists to close.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>ONE CANCELLATION IS A BUG AND REPETITION IS THE IMPACT, so the impact is what this asserts.</b>
    /// Each cancelled request used to mint a session and take a pool reference that nothing could release:
    /// not the caller, which never received the handle; not the registry, which has no expiry sweep; and not
    /// the pool's idle collection, which cannot reap an entry whose reference count is non-zero. Twenty
    /// cancellations therefore left twenty permanent sessions over one connection, and every one of them
    /// counted against the caller's own ceiling - so a caller that cancelled could lock ITSELF out with
    /// <c>E_BUSY</c> and never recover inside the process's lifetime.
    /// </para>
    /// <para>
    /// THE FIRST SESSION IS OPENED UNCANCELLED ON PURPOSE. It connects the shared transaction, which is what
    /// makes every subsequent request take the already-connected path - the path on which the only
    /// cancellation check must not sit inside a branch that path skips entirely.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task RepeatedCancellationsOverAConnectedDescriptorLeaveNoOrphanSessionOrPoolReference()
    {
        const int cancellations = 20;

        Harness harness = new(keepAlive: true);

        SessionHandle first = await Open(harness);

        for (int attempt = 0; attempt < cancellations; attempt++)
        {
            using CancellationTokenSource source = new();
            await source.CancelAsync();

            BeginSessionResponse refused = await harness.Service.BeginSession(
                new BeginSessionRequest { Descriptor_ = Descriptor() },
                new FakeCallContext(source.Token));

            Assert.Equal(WireRetCode.Cancelled, refused.Status.RetCode);
            Assert.Null(refused.Session);
        }

        // ONE SESSION AND ONE POOL ENTRY, after twenty-one requests. Both numbers would have been
        // twenty-one before the check existed.
        Assert.Equal(1, harness.Registry.Count);
        Assert.Equal(1, harness.Pool.UpperBound);
        Assert.True(harness.Registry.TryResolve(first, out _));

        // AND THE CALLER CAN STILL OPEN ONE, which is the consequence a ceiling charge would have taken
        // away.
        SessionHandle another = await Open(harness);
        Assert.Equal(2, harness.Registry.Count);
        Assert.True(harness.Registry.TryResolve(another, out _));
    }

    /// <summary>
    /// A cancelled request queueing for the shared transaction's gate GIVES UP THE WAIT instead of being
    /// served behind the holder, and the holder completes untouched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE THIRD CANCELLATION POINT, AND THE ONE AN ALREADY-CONNECTED TEST CANNOT REACH. Acquisition
    /// serializes the liveness test and the connect on one gate per pooled transaction, so a second caller
    /// arriving during another's open QUEUES. While that wait ignored the request token, a caller who had
    /// already gone waited out the holder's whole connect and was then handed a session it could not
    /// receive - the same orphan, arrived at by a different route.
    /// </para>
    /// <para>
    /// THE DISCRIMINATING ASSERTION IS THE TIMING RATHER THAN THE CODE, which is why the holder is still
    /// blocked inside its connect when the cancelled caller's answer is awaited: a build that ignores the
    /// token cannot answer at that moment at all, so it fails by exceeding the budget rather than by
    /// returning the wrong status. The budget is generous because it detects a wait, not a latency.
    /// </para>
    /// <para>
    /// KEEP-ALIVE IS ON so both callers borrow ONE pooled transaction, which is the condition under which
    /// the gate is shared and the queueing exists at all.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ACancelledRequestQueuedBehindAConnectGivesUpTheWaitRatherThanBeingServed()
    {
        Harness harness = new(keepAlive: true);

        using ManualResetEventSlim connecting = new(false);
        using ManualResetEventSlim release = new(false);

        // THE HOLDER PARKS INSIDE THE CONNECT, so the gate is genuinely held while the second caller
        // arrives. Synchronous, because the provider this stands in for is synchronous - which is the
        // reason the acquisition needed a gate in the first place.
        harness.Engine.OnConnect = () =>
        {
            connecting.Set();
            release.Wait();
        };

        Task<BeginSessionResponse> holder = Task.Run(
            () => harness.Service.BeginSession(
                new BeginSessionRequest { Descriptor_ = Descriptor() },
                Context),
            TestContext.Current.CancellationToken);

        Assert.True(
            connecting.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken),
            "the holder never reached its connect.");

        using CancellationTokenSource source = new();
        await source.CancelAsync();

        Task<BeginSessionResponse> queued = harness.Service.BeginSession(
            new BeginSessionRequest { Descriptor_ = Descriptor() },
            new FakeCallContext(source.Token));

        // ANSWERED WHILE THE HOLDER IS STILL INSIDE ITS CONNECT. A build that waits cannot get here.
        BeginSessionResponse refused = await queued.WaitAsync(
            TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken);

        Assert.Equal(WireRetCode.Cancelled, refused.Status.RetCode);
        Assert.Null(refused.Session);

        // The holder is still holding, which is what makes the assertion above about the WAIT.
        Assert.False(holder.IsCompleted);

        release.Set();

        BeginSessionResponse served = await holder.WaitAsync(
            TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken);

        // THE HOLDER IS UNAFFECTED: cancelling a queued waiter cancels the WAIT, never the holder.
        Assert.Equal(WireRetCode.Ok, served.Status.RetCode);
        Assert.NotNull(served.Session);
        Assert.True(harness.Registry.TryResolve(served.Session, out _));

        // ONE CONNECT, ONE ENTRY, AND NO DISCONNECT: the refusal released only its own reference.
        Assert.Equal(1, harness.Engine.ConnectCalls);
        Assert.Equal(0, harness.Engine.DisconnectCalls);
        Assert.Equal(1, harness.Pool.UpperBound);
    }

    // ==============================================================================================
    //  ACQUISITION UNDER CONCURRENCY - THE POOL SHARES ONE TRANSACTION, SO ONE CALLER MAY CONNECT IT
    // ==============================================================================================

    /// <summary>
    /// Concurrent sessions over one descriptor connect the shared transaction exactly once, and all of
    /// them are issued a handle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>⚠ THIS CASE GUARDS THE ONE UNGATED PATH THAT WOULD LOSE EVERY CONCURRENT REQUEST.</b> The pool
    /// keys entries on whole-descriptor value equality with reference counting, so every session opened
    /// with an equal descriptor borrows the SAME transaction instance. Acquisition is the one path on this
    /// contract where an ungated liveness test is fatal: N concurrent openings all observe that transaction
    /// as unconnected and all connect it. Because the engine assigns its connection and only then begins
    /// its transaction, it is left holding one caller's connection beside another's transaction, and every
    /// command built from that pair fails as not associated with the same connection - five concurrent
    /// retrievals producing five HTTP 500s over one shared pool lease.
    /// </para>
    /// <para>
    /// THE CONNECT COUNT IS THE ASSERTION THAT MATTERS. Asserting only that every caller got a handle
    /// pass against an ungated implementation too: <c>BeginSession</c> itself answers OK, and the damage
    /// only surfaces later when a command is built. Counting connects tests the invariant directly -
    /// <c>[:L173]</c> says a live pooled connection is NOT reconnected, and under concurrency that means
    /// exactly one connect for one shared transaction.
    /// </para>
    /// <para>
    /// THE OVERLAP IS FORCED RATHER THAN HOPED FOR. Every task waits on one barrier so they arrive
    /// together, and the first connect holds briefly inside the engine so any sibling that could reach the
    /// liveness test unguarded certainly does. Without both, an unguarded implementation could serialize
    /// by chance and the test would prove nothing.
    /// </para>
    /// <para>
    /// KEEP-ALIVE IS ON, which is what makes the pool hand back one entry rather than one per opening -
    /// the condition under which sharing, and therefore the race, exists at all.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ConcurrentSessionsOverOneDescriptorConnectTheSharedTransactionExactlyOnce()
    {
        const int callers = 8;

        Harness harness = new(keepAlive: true);

        using Barrier gate = new(callers);

        // ================================================================================================
        //  THE OVERLAP IS HELD OPEN BY A SIGNAL, NOT BY A SLEEP
        //
        //  This used to widen the race window with `Thread.Sleep(40)` inside the connect. The intent was
        //  right - a connect that returns instantly can finish before a sibling has reached the liveness
        //  test, so an unguarded implementation could pass by luck - but forty milliseconds is a guess
        //  about how long eight tasks take to be scheduled, and a guess is exactly what makes an outcome a
        //  function of host load. On a busy agent it is too short and the test proves nothing; it can never
        //  be long enough to be certain, only long enough to be usually right.
        //
        //  Two signals replace it and between them the window is held open for precisely as long as it
        //  needs to be, with no duration named anywhere:
        //
        //    * `entered` counts callers that have entered `BeginSession`, and completes `allEntered` when
        //      the last one does.
        //    * `release` is what the winning caller waits on INSIDE its connect. The test sets it only
        //      after `allEntered` has completed, so the connect cannot return until every sibling is
        //      already inside the acquisition path - which is the state an unguarded implementation would
        //      turn into a second connect.
        //
        //  Neither signal carries a timeout. A run in which a caller never arrives is ended by the test
        //  runner's own timeout, which is the separate liveness bound that belongs outside an assertion;
        //  nothing here asserts a duration (AAP 0.8.5).
        // ================================================================================================
        using ManualResetEventSlim release = new(initialState: false);

        TaskCompletionSource allEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int entered = 0;

        harness.Engine.OnConnect = () => release.Wait(TestContext.Current.CancellationToken);

        Task<BeginSessionResponse>[] openings = [.. Enumerable.Range(0, callers).Select(_ => Task.Run(
            async () =>
            {
                // ARRIVE TOGETHER. Every task blocks until the last one reaches this line.
                gate.SignalAndWait(TestContext.Current.CancellationToken);

                // ENTERED, ANNOUNCED BEFORE THE CALL. The last caller to get here completes `allEntered`,
                // which is what lets the test decide the window has been held open long enough.
                if (Interlocked.Increment(ref entered) == callers)
                {
                    // NOT DISCARDED WITH `_`: the enclosing Select lambda already binds `_` as its int
                    // parameter, so a discard here would assign a bool to it.
                    allEntered.TrySetResult();
                }

                return await harness.Service.BeginSession(
                    new BeginSessionRequest { Descriptor_ = Descriptor() },
                    new FakeCallContext(CancellationToken.None));
            },
            TestContext.Current.CancellationToken))];

        // EVERY CALLER IS IN THE ACQUISITION PATH before the winning connect is allowed to finish.
        await allEntered.Task.WaitAsync(TestContext.Current.CancellationToken);

        release.Set();

        BeginSessionResponse[] responses = await Task.WhenAll(openings);

        // EVERY CALLER IS SERVED. A gate that excluded by refusing would be a different defect.
        Assert.All(responses, response =>
        {
            Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
            Assert.NotNull(response.Session);
        });

        // THE INVARIANT: one shared transaction is connected once, however many callers arrive at once.
        Assert.Equal(1, harness.Engine.ConnectCalls);

        // ONE POOL ENTRY, so the sharing that creates the race was genuinely in play.
        Assert.Equal(1, harness.Pool.UpperBound);

        // EVERY HANDLE IS DISTINCT AND EVERY ONE RESOLVES, so exclusion cost no caller its session.
        Assert.Equal(
            callers,
            responses.Select(response => response.Session.SessionId).Distinct(StringComparer.Ordinal).Count());

        Assert.All(responses, response => Assert.True(harness.Registry.TryResolve(response.Session, out _)));
    }

    // ---------------------------------------------------------------------------------------------
    //  18. A TRANSACTION THE POOL DESTROYED UNDERNEATH A LIVE SESSION
    //
    //  The state is reachable without any race, and it is the pool that creates it. SetBroken condemns the
    //  entry's transaction; the next borrow of that same descriptor takes the broken arm
    //  [n_cst_thread_trans_pool.sru:L164], which sets the entry's transaction to null and DESTROYS the old
    //  object before creating a replacement. Every session that was already holding the old object is now
    //  holding a destroyed one - and its handle still resolves, because nothing retired it.
    //
    //  The oracle guards this with IsValidObject, which in PowerScript reports FALSE for a DESTROYED
    //  object as well as for a null reference. The managed null test only covers the second half: a
    //  disposed object is still a non-null reference. So the port needed the first half stated
    //  explicitly - IPooledTransaction.IsDestroyed - and every liveness guard needed to consult it.
    //  Without that, these verbs reached a disposed object and answered an unhandled Internal fault
    //  instead of a status.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Drives a harness into the destroyed-transaction state and hands back the session left holding it.
    /// </summary>
    /// <param name="harness">A keep-alive harness, so two borrows of one descriptor share one entry.</param>
    /// <returns>
    /// The stranded session - the one left holding the destroyed transaction - and the replacement
    /// session whose borrow destroyed it.
    /// </returns>
    private static async Task<(SessionHandle Stranded, SessionHandle Replacement)>
        OpenASessionWhoseTransactionThePoolDestroys(Harness harness)
    {
        SessionHandle stranded = await Open(harness);

        Assert.True(harness.Registry.TryResolve(stranded, out TransactionSession? live));
        IPooledTransaction condemned = live!.Transaction;

        // CONDEMN IT, which is a supported operation on a live session and answers success.
        Assert.Equal(
            WireRetCode.Ok,
            (await harness.Service.SetBroken(new SetBrokenRequest { Session = stranded }, null!))
                .Status.RetCode);

        // AND THEN BORROW THE SAME DESCRIPTOR AGAIN. This is the step that destroys the object above:
        // the pool's broken arm replaces it rather than handing it out.
        SessionHandle replacement = await Open(harness);

        Assert.True(harness.Registry.TryResolve(replacement, out TransactionSession? fresh));
        Assert.NotSame(condemned, fresh!.Transaction);

        // THE PRECONDITION, ASSERTED RATHER THAN ASSUMED: the stranded session still resolves, and the
        // object it holds is destroyed.
        Assert.True(harness.Registry.TryResolve(stranded, out TransactionSession? afterwards));
        Assert.Same(condemned, afterwards!.Transaction);
        Assert.True(condemned.IsDestroyed);
        Assert.True(afterwards.IsUnusable);

        return (stranded, replacement);
    }

    [Fact]
    public async Task EveryOperationRefusesASessionWhoseTransactionThePoolDestroyed()
    {
        Harness harness = new(keepAlive: true);
        (SessionHandle session, _) = await OpenASessionWhoseTransactionThePoolDestroys(harness);

        (string Verb, WireRetCode Code)[] refusals =
        [
            ("IsConnected", (await harness.Service.IsConnected(
                new IsConnectedRequest { Session = session }, null!)).Status.RetCode),
            ("GetDatabaseType", (await harness.Service.GetDatabaseType(
                new GetDatabaseTypeRequest { Session = session }, null!)).Status.RetCode),
            ("SetAutoCommit", (await harness.Service.SetAutoCommit(
                new SetTransactionAutoCommitRequest { Session = session, Autocommit = true },
                null!)).Status.RetCode),
            ("Commit", (await harness.Service.Commit(
                new CommitRequest { Session = session }, null!)).Status.RetCode),
            ("Rollback", (await harness.Service.Rollback(
                new RollbackRequest { Session = session }, null!)).Status.RetCode),
            ("ClearState", (await harness.Service.ClearState(
                new ClearStateRequest { Session = session }, null!)).Status.RetCode),
            ("GetSessionState", (await harness.Service.GetSessionState(
                new GetSessionStateRequest { Session = session }, null!)).Status.RetCode),
            ("AutoCommit", (await harness.Service.AutoCommit(
                new AutoCommitRequest { Session = session }, null!)).Status.RetCode),
            ("SetBroken", (await harness.Service.SetBroken(
                new SetBrokenRequest { Session = session }, null!)).Status.RetCode),
            ("GridSyntaxFromSql", (await harness.Service.GridSyntaxFromSql(
                new GridSyntaxFromSqlRequest { Session = session, Sql = "SELECT * FROM COMPANY" },
                null!)).Status.RetCode),
        ];

        // TEN VERBS, ONE CODE, AND IT IS THE UNKNOWN-SESSION CODE - the same answer a closing session
        // gives, because from the caller's point of view both mean the transaction it named is gone.
        // The tenth is GRID SYNTAX, which had no liveness guard at all: it reached the destroyed object
        // through the query surface and faulted.
        Assert.All(
            refusals,
            outcome => Assert.Equal(
                (outcome.Verb, WireRetCode.EInvalidTransaction),
                (outcome.Verb, outcome.Code)));

        // AND NOTHING WAS ISSUED ON THE WAY TO THOSE REFUSALS. The query surface was never called, so the
        // grid-syntax refusal happened before the destroyed object was handed to it.
        Assert.Equal(0, harness.QuerySurface.Calls);
    }

    /// <summary>
    /// ⚠ THE ONE DELIBERATE EXCEPTION: reading the DESCRIPTOR back still succeeds, because it never
    /// touches the transaction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>GetTransactionData</c> copies out of <c>session.Descriptor</c> - the session's own immutable
    /// copy of what the caller supplied - and reaches no pooled object at all. Refusing it on a destroyed
    /// transaction would be a fabrication of exactly the kind the plan forbids: nothing about the copy
    /// became untrue when the pool replaced the connection behind it, and the descriptor is precisely what
    /// a caller needs in hand to open a replacement session.
    /// </para>
    /// <para>
    /// It DOES still refuse a CLOSING session, and the asymmetry is deliberate rather than an oversight.
    /// Closing means the handle itself is being retired, so there is no session left to answer for;
    /// destroyed means the session is still registered and its descriptor is still exactly what it was.
    /// The guard on this one verb is therefore <c>IsClosing</c> and not <c>IsUnusable</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ReadingTheDescriptorBackSucceedsEvenOnADestroyedTransaction()
    {
        Harness harness = new(keepAlive: true);
        (SessionHandle session, _) = await OpenASessionWhoseTransactionThePoolDestroys(harness);

        GetTransactionDataResponse read = await harness.Service.GetTransactionData(
            new GetTransactionDataRequest { Session = session }, null!);

        Assert.Equal(WireRetCode.Ok, read.Status.RetCode);
        Assert.NotNull(read.Descriptor_);
        Assert.Equal("SQLite", read.Descriptor_.Dbms);

        // AND THE PASSWORD IS STILL UNREACHABLE ON THIS PATH (constraint C-F): the response carries a
        // TransactionDescriptorView, a projection that has no logpass FIELD at all rather than an empty
        // one - so a destroyed transaction cannot become a route to echoing it back even by mistake.
        Assert.DoesNotContain(
            "logpass",
            TransactionDescriptorView.Descriptor.Fields.InFieldNumberOrder().Select(
                static field => field.Name),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnEndSessionOnADestroyedTransactionStillRetiresTheHandleAndDropsTheReference()
    {
        Harness harness = new(keepAlive: true);

        (SessionHandle session, SessionHandle replacement) =
            await OpenASessionWhoseTransactionThePoolDestroys(harness);

        Assert.True(harness.Registry.TryResolve(session, out TransactionSession? live));
        PoolLease lease = live!.Lease;

        // ⚠ THE CODE IS THE RELEASE'S OWN, AND THE TEARDOWN HAPPENED ANYWAY. E_INVALID_OBJECT is what
        // the oracle's release answers for a handle that is not a valid object [:L164-L172], so it is
        // reported rather than papered over with OK - a caller learns the true fact that the transaction
        // it was holding had already been destroyed. What it must NOT mean is that the session is still
        // open, which is what the two assertions below pin down and what the response field documents.
        Assert.Equal(
            WireRetCode.EInvalidObject,
            (await harness.Service.EndSession(new EndSessionRequest { Session = session }, null!))
                .Status.RetCode);

        // THE HANDLE IS GONE.
        Assert.False(harness.Registry.TryResolve(session, out _));

        // AND A SECOND END SAYS SO PLAINLY, so a caller that read the code above as "still open" and
        // retried is told the handle names nothing rather than being left to retry for ever.
        Assert.Equal(
            WireRetCode.EInvalidTransaction,
            (await harness.Service.EndSession(new EndSessionRequest { Session = session }, null!))
                .Status.RetCode);

        // THE ENTRY IS STILL ALIVE, because keep-alive is on and the replacement session is still
        // borrowing it. Under keep-alive an entry deliberately outlives its last borrower so the next
        // caller with the same descriptor reuses the connection [n_cst_thread_trans_pool.sru:L215], so
        // liveness is NOT the thing to read here.
        Assert.True(harness.Pool.IsLeaseLive(lease));

        // THE REPLACEMENT GIVES ITS SESSION UP TOO, so the entry now has no borrowers at all - IF the
        // stranded session's reference was really dropped.
        Assert.Equal(
            WireRetCode.Ok,
            (await harness.Service.EndSession(new EndSessionRequest { Session = replacement }, null!))
                .Status.RetCode);

        // ⚠ AND HERE IS THE REFERENCE-DROP ASSERTION, READ THROUGH THE COLLECTOR. A forced collect
        // overrides the IDLE WINDOW and nothing else - the reference count still has to be zero for an
        // entry to go [:L215] - so an entry that survives a forced collect is an entry somebody is still
        // counted as holding. A failed release that returned early would leave this session's
        // reference standing, so the entry could never reach zero: its connection would never be
        // disconnected and its slot never reclaimed - a leak produced by the one path whose job is to
        // prevent one.
        harness.Pool.Collect(force: true);

        Assert.Equal(0, harness.Pool.UpperBound);
        Assert.False(harness.Pool.IsLeaseLive(lease));
    }

    // ---------------------------------------------------------------------------------------------
    //  19. SHARING IS REPORTED RATHER THAN PREVENTED - M-5
    //
    //  Two sessions opened with EQUAL descriptors share one pooled transaction (section 15), so one
    //  session's commit commits the other's uncommitted work as well. That is the oracle's own behaviour -
    //  the pool keys entries on whole-descriptor equality [n_cst_thread_trans_pool.sru:L136-L146] - and
    //  constraint C-B forbids changing it. What a caller has no other way to KNOW is that it is sharing, so
    //  the count is on the response and the operation records a warning when it exceeds one.
    //
    //  A COUNT AND NOT A LIST (constraint C-F). A handle belongs to whoever was issued it; naming another
    //  holder's would disclose it to a caller who was never given it.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task CommitAndRollbackReportHowManySessionsShareTheTransactionTheyActedOn()
    {
        Harness harness = new();

        SessionHandle first = await Open(harness);
        SessionHandle second = await Open(harness);

        // ONE ENTRY, ONE TRANSACTION - the precondition section 15 establishes, restated so this case
        // stands on its own.
        Assert.Equal(1, harness.Pool.UpperBound);

        // AUTO-COMMIT OFF, so the commit and the rollback both reach the engine and succeed: the count is
        // asserted on operations that DID something, which is when it matters.
        harness.Engine.AutoCommit = false;

        CommitResponse committed = await harness.Service.Commit(
            new CommitRequest { Session = first }, null!);

        Assert.Equal(WireRetCode.Ok, committed.Status.RetCode);
        Assert.Equal(2, committed.SharingSessionCount);

        RollbackResponse rolledBack = await harness.Service.Rollback(
            new RollbackRequest { Session = second }, null!);

        Assert.Equal(WireRetCode.Ok, rolledBack.Status.RetCode);
        Assert.Equal(2, rolledBack.SharingSessionCount);

        // AND THE STATE REPORT AGREES WITH BOTH, so a caller can read the number before it acts rather
        // than only afterwards.
        GetSessionStateResponse state = await harness.Service.GetSessionState(
            new GetSessionStateRequest { Session = first }, null!);

        Assert.Equal(WireRetCode.Ok, state.Status.RetCode);
        Assert.Equal(2, state.SharingSessionCount);
    }

    [Fact]
    public async Task ASoleHolderIsReportedAsOneAndAnEndedCoHolderStopsCounting()
    {
        Harness harness = new();

        SessionHandle first = await Open(harness);
        SessionHandle second = await Open(harness);

        harness.Engine.AutoCommit = false;

        Assert.Equal(
            2,
            (await harness.Service.Commit(new CommitRequest { Session = first }, null!))
                .SharingSessionCount);

        // THE CO-HOLDER GIVES ITS SESSION UP.
        Assert.Equal(
            WireRetCode.Ok,
            (await harness.Service.EndSession(new EndSessionRequest { Session = second }, null!))
                .Status.RetCode);

        // ONE, NOT TWO. The count is the live population at the moment of the operation, so a session
        // that has been retired no longer contributes - and ONE is the ordinary, unremarkable case that
        // raises no warning.
        Assert.Equal(
            1,
            (await harness.Service.Commit(new CommitRequest { Session = first }, null!))
                .SharingSessionCount);
    }

    [Fact]
    public async Task SessionsOnDIFFERENTDescriptorsDoNotCountTowardsOneAnother()
    {
        Harness harness = new();

        SessionHandle sqlite = await Open(harness);
        SessionHandle other = await Open(harness, dbms: "ORACLE");

        // TWO ENTRIES, because the descriptors differ - so neither session shares the other's
        // transaction and each reports itself as the sole holder.
        Assert.Equal(2, harness.Pool.UpperBound);

        harness.Engine.AutoCommit = false;

        Assert.Equal(
            1,
            (await harness.Service.Commit(new CommitRequest { Session = sqlite }, null!))
                .SharingSessionCount);

        Assert.Equal(
            1,
            (await harness.Service.Commit(new CommitRequest { Session = other }, null!))
                .SharingSessionCount);
    }

    [Fact]
    public async Task TheCountIsByTRANSACTIONIDENTITYSoAReplacedEntryDoesNotCountItsStrandedHolder()
    {
        Harness harness = new(keepAlive: true);

        (SessionHandle stranded, SessionHandle replacement) =
            await OpenASessionWhoseTransactionThePoolDestroys(harness);

        // BOTH SESSIONS ARE LIVE ON ONE ENTRY AND ONE LEASE, and the stranded one is even resolvable -
        // so a count taken by descriptor equality or by lease would say TWO. It holds a DIFFERENT
        // transaction object, though, and the count is reference equality on that object, so it says ONE.
        Assert.True(harness.Registry.TryResolve(stranded, out TransactionSession? strandedSession));
        Assert.True(harness.Registry.TryResolve(replacement, out TransactionSession? liveSession));
        Assert.Equal(strandedSession!.Lease, liveSession!.Lease);
        Assert.NotSame(strandedSession.Transaction, liveSession.Transaction);
        Assert.Equal(2, harness.Registry.Count);

        harness.Engine.AutoCommit = false;

        Assert.Equal(
            1,
            (await harness.Service.Commit(new CommitRequest { Session = replacement }, null!))
                .SharingSessionCount);
    }

    // ---------------------------------------------------------------------------------------------
    //  20. A NON-POSITIVE KEEP-ALIVE EXPIRY IS ACCEPTED, AND NOT SILENTLY
    //
    //  A REFUSAL IS THE TEMPTING ANSWER AND IT WOULD BE WRONG. The oracle reads
    //  the expiry and then folds anything non-positive to its own default:
    //      constant long KEEPALIVE_EXPIRE = 30000                        [:L53]
    //      _nKeepAliveExpireTime = ...GetDataDouble(...) * 1000           [:L78]
    //      if _nKeepAliveExpireTime <= 0 then _nKeepAliveExpireTime = KEEPALIVE_EXPIRE   [:L79]
    //  and the contract field says the same in as many words. A negative expiry is therefore an IN-DOMAIN
    //  value with a DEFINED meaning - "use the default" - not an out-of-domain one like an undeclared
    //  enumerator. Refusing it would contradict the oracle, the published field text and constraint C-B at
    //  once. It is accepted, and what was actually wrong - that the caller was told nothing about either
    //  the fold or the outright non-consultation - is fixed by recording both.
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(-1d)]
    [InlineData(-30d)]
    [InlineData(0d)]
    public async Task ANonPositiveKeepAliveExpiryIsACCEPTEDAndFoldedToTheDefault(double expirySeconds)
    {
        CapturingLogger log = new();
        Harness harness = new(keepAlive: true, logger: log);

        BeginSessionResponse response = await harness.Service.BeginSession(
            new BeginSessionRequest
            {
                Descriptor_ = Descriptor(),
                KeepAlive = new PoolKeepAliveSettings
                {
                    KeepAlive = true,
                    KeepAliveExpireSeconds = expirySeconds,
                },
            },
            Context);

        // ACCEPTED. The session opens, exactly as it does today and exactly as the oracle's own fold
        // implies it must.
        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
        Assert.NotNull(response.Session);

        // AND THE CONFIGURED DEFAULT IS WHAT IS IN FORCE - 30 s, the oracle's KEEPALIVE_EXPIRE - so the
        // number the caller sent was genuinely not used as a duration.
        Assert.Equal(30_000, harness.Pool.KeepAliveExpireMilliseconds);

        // AND THE CALLER IS TOLD SO. Informational, not a warning: nothing went wrong.
        Assert.Contains(
            log.Records,
            record => record.StartsWith("Information|", StringComparison.Ordinal)
                && record.Contains("non-positive keep-alive expiry", StringComparison.Ordinal)
                && record.Contains("use the default", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnExpirySENTWITHKEEPALIVEOFFIsRecordedAsNotCONSULTEDATALL()
    {
        CapturingLogger log = new();

        // KEEP-ALIVE OFF on the instance, and the request agrees - so the guard's keep-alive branch is
        // never entered and the expiry is not read for any purpose. The oracle reads it only inside
        // `if ...KeepAlive then` [:L76-L83], which is precisely why this silence was a separate one.
        Harness harness = new(keepAlive: false, logger: log);

        BeginSessionResponse response = await harness.Service.BeginSession(
            new BeginSessionRequest
            {
                Descriptor_ = Descriptor(),
                KeepAlive = new PoolKeepAliveSettings
                {
                    KeepAlive = false,
                    KeepAliveExpireSeconds = 45d,
                },
            },
            Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);

        Assert.Contains(
            log.Records,
            record => record.StartsWith("Information|", StringComparison.Ordinal)
                && record.Contains("keep-alive is off on this instance", StringComparison.Ordinal)
                && record.Contains("accepted and ignored", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AKeepAliveDescriptorWhoseEXPIRYMATCHESTheConfiguredOneIsAcceptedSilently()
    {
        CapturingLogger log = new();
        Harness harness = new(keepAlive: true, logger: log);

        // THE POSITIVE, AGREEING CASE: 30 s is the configured default, so nothing is folded and nothing is
        // ignored - and therefore nothing is recorded. The two records above are about the two anomalies
        // and must not fire on the ordinary path.
        BeginSessionResponse response = await harness.Service.BeginSession(
            new BeginSessionRequest
            {
                Descriptor_ = Descriptor(),
                KeepAlive = new PoolKeepAliveSettings
                {
                    KeepAlive = true,
                    KeepAliveExpireSeconds = 30d,
                },
            },
            Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
        Assert.NotNull(response.Session);
        Assert.DoesNotContain(
            log.Records,
            record => record.Contains("keep-alive expiry", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AKeepAliveDescriptorWhosePOSITIVEExpiryDISAGREESIsRefusedWithTheArgumentCode()
    {
        Harness harness = new(keepAlive: true);

        // AND THE DISTINCTION THAT MAKES THE ACCEPTANCE ABOVE COHERENT. A POSITIVE expiry is a real
        // duration request, so one that disagrees with the instance's configured window is refused - the
        // pool reads its window once at startup exactly as the legacy pool does, so honouring a
        // per-session duration would be a capability the oracle does not have. A NON-POSITIVE value is not
        // a duration request at all, which is why it is accepted instead.
        BeginSessionResponse response = await harness.Service.BeginSession(
            new BeginSessionRequest
            {
                Descriptor_ = Descriptor(),
                KeepAlive = new PoolKeepAliveSettings
                {
                    KeepAlive = true,
                    KeepAliveExpireSeconds = 45d,
                },
            },
            Context);

        Assert.Equal(WireRetCode.EInvalidArgument, response.Status.RetCode);
        Assert.Contains("keep-alive expiry disagrees", response.Status.ErrorText, StringComparison.Ordinal);

        // AND NO SESSION WAS OPENED, so a refusal costs nothing.
        Assert.Equal(0, harness.Registry.Count);
    }
}
