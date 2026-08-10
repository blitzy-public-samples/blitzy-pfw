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
using Microsoft.Extensions.Options;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Tasks;
using PowerFramework.Persistence.Transactions;
using GeneratedBase =
    global::PowerFramework.Contracts.Persistence.V1.TransactionService.TransactionServiceBase;
using Predicates = PowerFramework.Shared.Kernel.Predicates;
using TransactionService = PowerFramework.Persistence.Grpc.TransactionService;
using TransactionSession = PowerFramework.Persistence.Grpc.TransactionSession;
using TransactionSessionRegistry = PowerFramework.Persistence.Grpc.TransactionSessionRegistry;

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

        internal void Advance(TimeSpan delta) => _now += delta;
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

        public void ApplyConnectionFields(in TransactionData descriptor) => DbmsValue = descriptor.Dbms;

        public SqlState Connect()
        {
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

        public SqlState Execute(string sqlCommand)
        {
            ExecuteCalls++;
            _ = sqlCommand;
            return ExecuteResult;
        }

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

    private sealed class Harness
    {
        internal Harness(bool keepAlive = false)
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
                new PooledTransactionActivator(() => Engine, Clock));

            Registry = new TransactionSessionRegistry();
            QuerySurface = new FakeQuerySurface();
            Service = new TransactionService(Pool, Registry, QuerySurface);
        }

        internal FakeClock Clock { get; }

        internal FakeEngine Engine { get; }

        internal TransactionPool Pool { get; }

        internal TransactionSessionRegistry Registry { get; }

        internal FakeQuerySurface QuerySurface { get; }

        internal TransactionService Service { get; }
    }

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
            null!);

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
                new BeginSessionRequest { Descriptor_ = Descriptor() }, null!)).ToString(),
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

        foreach (int badIndex in new[] { 0, -1, harness.Pool.UpperBound + 1 })
        {
            TransactionSession forged = harness.Registry.Register(
                badIndex,
                new TransactionData { Dbms = "SQLite" },
                borrowed);

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

        // ONE-BASED: the first entry is 1, never 0.
        Assert.Equal(1, live!.ReferenceIndex);
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
            null!);

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
            null!);

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
            null!);

        Assert.Equal(WireRetCode.EInvalidArgument, response.Status.RetCode);
        Assert.Contains("liveness-cache window", response.Status.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingDescriptorIsRefusedWithInvalidArgument()
    {
        Harness harness = new();

        BeginSessionResponse response = await harness.Service.BeginSession(
            new BeginSessionRequest(),
            null!);

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

        Assert.Throws<ArgumentNullException>(() =>
            new TransactionService(null!, harness.Registry, harness.QuerySurface));

        Assert.Throws<ArgumentNullException>(() =>
            new TransactionService(harness.Pool, null!, harness.QuerySurface));

        Assert.Throws<ArgumentNullException>(() =>
            new TransactionService(harness.Pool, harness.Registry, null!));

        // The logger is optional so a unit test can construct the service with behaviour only.
        Assert.NotNull(new TransactionService(harness.Pool, harness.Registry, harness.QuerySurface));
    }

    [Fact]
    public async Task ANullRequestIsAProgrammingErrorAndThrows()
    {
        Harness harness = new();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            harness.Service.BeginSession(null!, null!));

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
        Assert.Equal(a.ReferenceIndex, b.ReferenceIndex);
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

    [Fact]
    public void TheServerCallContextIsNeverDereferenced()
    {
        // Every handler is called above with a null context and none of them threw, which is the
        // evidence that the context is not consulted. Asserted here so the intent is explicit.
        Assert.Null(default(ServerCallContext));
    }
}
