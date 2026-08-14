// ==================================================================================================
//  CommandServiceTests.cs - PARITY AND CONTRACT TESTS FOR C-07 persistence.v1.CommandService
//  ------------------------------------------------------------------------------------------------
//  SUBJECT: services/persistence-service/PowerFramework.Persistence/Grpc/CommandService.cs
//
//  WHAT THIS SUITE PINS, AND WHY EACH ITEM IS HERE RATHER THAN ASSUMED.
//  Every assertion below corresponds to a behaviour the migration plan requires be PRESERVED
//  VERBATIM rather than corrected (constraint C-B), and each names its oracle locator so a future
//  reader can adjudicate the expectation against the source rather than against this file:
//
//      AC_OFF is the default, and a reset restores it        [:L21], [:L34]
//      the autocommit setter validates NOTHING               [:L40-L43]
//      AC_OFF commits nothing - the missing else arm         [:L94-L103]
//      AC_ON commits, and the commit's code REPLACES         [:L98-L103]
//      AC_NATIVE toggles the flag on then off, both paths    [:L88-L90], [:L96], [:L106]
//      AC_NATIVE raises committed INSTEAD of committing      [:L97]
//      AC_NATIVE's failure arm performs NO ROLLBACK of its
//        own, so its rollback count is ONE where every
//        other mode's is TWO                                [:L105-L109] + base OnError rollback
//      an empty statement is E_INVALID_SQL, twice            [:L45], [:L65-L68]
//      and a DEBUG build reaches NEITHER of those two,
//        because the proxy's preserved #IF DEFINED DEBUG
//        assertion trips first                               [n_cst_threading_task_sqlcommand.sru:L36-L38]
//      SQLCode = 100 reads as SUCCESS                        [n_cst_thread_trans.sru:L233]
//      the ordered failure arms and their exact codes        [:L65], [:L70], [:L76], [:L82]
//      a positional parameter IS the empty-NAMED case        [n_cst_threading_task_sqlbase.sru:L138]
//      the 2-D and empty-array parameter rejections          [n_cst_threading_task_sqlbase.sru:L148-L149]
//      the DORMANT null check stays dormant                  [n_cst_threading_task_sqlbase.sru:L147]
//      E_BUSY on every mutator while running                 [n_cst_threading_task_sqlbase.sru:L57..L188]
//      committed is a NON-BLOCKING poll                      [n_cst_threading_task_sqlbase.sru:L174]
//      a failing statement's DbError reaches the wire with
//        its statement REDACTED                              [:L110] + Errors/SqlRedactor.cs
//      a TWELFTH argument is ACCEPTED - the legacy ceiling
//        is recorded, never enforced                         [n_sqlite.sru:L42-L43]
//
//  AND ONE REVIEW INVARIANT WITH NO COMPILE-TIME ENFORCEMENT ANYWHERE IN THE REPOSITORY.
//  PowerFramework.Contracts declares zero ProjectReference, deliberately, so nothing checks that the
//  generated common.v1.RetCode.Value enum and shared/PowerFramework.Shared.Kernel/RetCode.cs agree
//  numerically. The Persistence projects are the first and only place both are referenced together,
//  so the value-for-value assertion belongs here - see the final region.
//
//  NO DATABASE, NO REAL THREAD, NO NETWORK. The transaction is a real PooledTransaction over a FAKE
//  ENGINE, so Exec, the SQLCode carve-out, the rollback counting and the autocommit toggle are all
//  exercised for real while nothing connects to anything. The clock is injected, per the
//  characterization model's determinism requirement.
//
//  C-F: no credential, key or token appears anywhere below, and no statement in any fixture contains
//  anything that could be mistaken for one. The password field on the fixture descriptor is an
//  obviously fake marker, and the redaction test asserts the statement is MASKED rather than quoting
//  a realistic secret to prove it.
// ==================================================================================================

using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Grpc;
using PowerFramework.Persistence.Runtime;
using PowerFramework.Persistence.Tasks;
using PowerFramework.Persistence.Tasks.TaskProxies;
using PowerFramework.Persistence.Transactions;
using PowerFramework.Shared.Diagnostics;

// PowerFramework.Contracts.Persistence.V1 is a global using, and it contains a GENERATED static class
// also called CommandService. These aliases make every bare name below unambiguous and name the type
// under test explicitly.
using CommandService = PowerFramework.Persistence.Grpc.CommandService;
using TransactionService = PowerFramework.Persistence.Grpc.TransactionService;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Parity and contract tests for the C-07 command adapter.
/// </summary>
public sealed class CommandServiceTests
{
    private const string FakePassword = "not-a-real-password";
    private const string RedactionPlaceholder = SqlRedactor.DefaultPlaceholder;

    /// <summary>
    /// The call context every handler in this suite is invoked with.
    /// </summary>
    /// <remarks>
    /// <b>A REAL CONTEXT RATHER THAN <c>null!</c>, BECAUSE THE HANDLERS READ IT.</b> Every C-07 and
    /// C-08 handler validates its context and threads <c>context.CancellationToken</c> into the provider
    /// call, so passing null would fail the argument guard before reaching the behaviour under test - and
    /// passing a non-cancellable stand-in would hide the fact that the token is threaded at all. One shared
    /// instance is safe because it is immutable in every respect this suite observes.
    /// </remarks>
    private static ServerCallContext Context { get; } = new CallContext(CancellationToken.None);

    // ---------------------------------------------------------------------------------------------
    //  DOUBLES
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// An injected clock, so the connection-liveness cache window is steerable and every reading is
    /// deterministic.
    /// </summary>
    private sealed class FakeClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        internal void Advance(TimeSpan delta) => _now += delta;
    }

    /// <summary>
    /// The database-verb seam. Records what was asked of it so the epilogues can be asserted by
    /// OBSERVED EFFECT rather than by reading private state.
    /// </summary>
    private sealed class FakeEngine : ITransactionEngine
    {
        private bool _autoCommit;

        internal int Handle { get; set; }

        internal string DbmsValue { get; set; } = "SQLite";

        internal SqlState ConnectResult { get; set; } = SqlState.Succeeded();

        internal SqlState CommitResult { get; set; } = SqlState.Succeeded();

        internal SqlState RollbackResult { get; set; } = SqlState.Succeeded();

        internal SqlState ExecuteResult { get; set; } = SqlState.Succeeded(1);

        internal int CommitCalls { get; private set; }

        internal int RollbackCalls { get; private set; }

        internal int ExecuteCalls { get; private set; }

        internal string LastStatement { get; private set; } = string.Empty;

        /// <summary>The canonical statement of the most recent parameterized execution.</summary>
        internal string LastCanonicalStatement { get; private set; } = string.Empty;

        /// <summary>The bound parameters of the most recent parameterized execution, in order.</summary>
        internal IReadOnlyList<object?> LastParameters { get; private set; } = [];

        /// <summary>
        /// Whether the most recent parameterized execution asked for the prepared-form mode - the port of
        /// the leading-<c>@</c> execution mode C-07 preserves (AAP §0.4.3).
        /// </summary>
        /// <remarks>
        /// RECORDED RATHER THAN HONOURED, because this engine keeps no prepared form and the seam's own
        /// contract says an implementation may decline the request. What a boundary case has to prove is
        /// that the mode was CARRIED to the engine at all: the earlier defect was that the prefix reached
        /// the provider as statement text and the mode reached nothing.
        /// </remarks>
        internal bool LastCacheStatement { get; private set; }

        /// <summary>Signalled once execution has entered the provider. Null when unused.</summary>
        internal ManualResetEventSlim? Entered { get; set; }

        /// <summary>Blocks execution inside the provider until set. Null when unused.</summary>
        internal ManualResetEventSlim? Gate { get; set; }

        /// <summary>
        /// Every write to the autocommit flag, in order. This is how the AC_NATIVE inversion is
        /// observed: the expected history for a native execution is true then false.
        /// </summary>
        internal List<bool> AutoCommitWrites { get; } = [];

        public int DbHandle => Handle;

        public string Dbms => DbmsValue;

        public bool AutoCommit
        {
            get => _autoCommit;
            set
            {
                _autoCommit = value;
                AutoCommitWrites.Add(value);
            }
        }

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

        public void ApplyConnectionFields(in TransactionData descriptor) => DbmsValue = descriptor.Dbms;

        public SqlState Connect(CancellationToken cancellationToken = default)
        {
            if (ConnectResult.SqlCode == 0)
            {
                Handle = 1;
            }

            return ConnectResult;
        }

        public SqlState Disconnect()
        {
            Handle = 0;
            return SqlState.Succeeded();
        }

        public SqlState Commit()
        {
            CommitCalls++;
            return CommitResult;
        }

        public SqlState Rollback()
        {
            RollbackCalls++;
            return RollbackResult;
        }

        public SqlState Execute(string sqlCommand, CancellationToken cancellationToken = default)
        {
            ExecuteCalls++;
            LastStatement = sqlCommand;
            return ExecuteResult;
        }

        /// <summary>
        /// The parameterized execution seam, recording BOTH renderings.
        /// </summary>
        /// <param name="command">The canonical statement, its rendered form and its parameters.</param>
        /// <param name="cancellationToken">The caller's token.</param>
        /// <returns>The scripted outcome.</returns>
        /// <remarks>
        /// <b>THE RENDERED TEXT IS RECORDED AS THE PARITY ARTIFACT AND THE CANONICAL TEXT AS WHAT WOULD BE
        /// EXECUTED.</b> The AAP requires parameterized SQL in the implementation "with the observable
        /// generated statement preserved", so both must be observable from a test: the canonical form is
        /// what a real provider binds parameters against, and the rendered form is what a characterization
        /// recording compares byte for byte. Recording only one of the two would make half the requirement
        /// untestable.
        /// </remarks>
        public SqlState Execute(in SqlCommandText command, CancellationToken cancellationToken = default)
        {
            ExecuteCalls++;
            LastStatement = command.RenderedText;
            LastCanonicalStatement = command.CanonicalText;
            LastParameters = [.. command.Parameters];
            LastCacheStatement = command.CacheStatement;

            // THE ONLY SEAM IN THIS SUITE THAT CAN HOLD AN OPERATION OPEN. The operation lease is taken
            // before the provider is reached and released after, so a test can only observe a HELD lease
            // from inside the provider call. Both handles are inert unless a test opts in.
            Entered?.Set();
            Gate?.Wait(cancellationToken);

            return ExecuteResult;
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// The query surface C-08's adapter requires. Never exercised here; C-07 retrieves nothing.
    /// </summary>
    private sealed class UnusedQuerySurface : IQueryTransactionSurface
    {
        public GridSyntaxOutcome GridSyntaxFromSql(IPooledTransaction transaction, string sql) =>
            new("table(column=(x))", string.Empty);

        public ValueTask<CountQueryOutcome> Query(
            IPooledTransaction transaction,
            string sql,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("C-07 never retrieves; retrieval is C-05's contract.");

        public long RaiseBeforeRetrieve(IPooledTransaction transaction, DataWindowCarrier data) =>
            RetCode.OK;

        public void RaiseAfterRetrieve(
            IPooledTransaction transaction,
            DataWindowCarrier data,
            long rowCount)
        {
        }
    }

    /// <summary>
    /// A carrier factory that must never be reached. A command produces no result set; the base
    /// requires the seam only because the oracle's own inheritance puts carrier machinery on it.
    /// </summary>
    private sealed class UnreachableDataStoreFactory : ISqlDataStoreFactory
    {
        public ISqlDataStore Create(CarrierThreadAffinity affinity) =>
            throw new NotSupportedException("A command task creates no result carrier.");
    }

    /// <summary>
    /// The worker-side threading substrate. <see cref="ParentTasking"/> is what carries the
    /// database-error event back to the caller-side sink, and <see cref="GetTask"/> is what lets the
    /// committed notification reach an already-created signal.
    /// </summary>
    private sealed class FakeTaskHost : ISqlTaskHost
    {
        internal SqlTaskBase? Worker { get; set; }

        internal ISqlTaskProxy? Proxy { get; set; }

        internal bool Cancelled { get; set; }

        internal List<(long Code, string Info)> Errors { get; } = [];

        /// <summary>
        /// How many times the worker's teardown reached this host. THE DISPOSAL OBSERVER for the lifecycle
        /// cases below: <c>SqlTaskBase.Dispose</c> routes through <c>OnUninit</c>
        /// [<c>Tasks/SqlTaskBase.cs</c>, the port of the substrate's own uninit hook], so a count here is a
        /// faithful record of whether the pair was actually torn down - which a sealed production worker
        /// exposes no other way.
        /// </summary>
        internal int Uninits { get; private set; }

        /// <summary>
        /// Run inside <see cref="OnUninit"/>, so a test can inject a request at the exact moment a purge is
        /// tearing this task down. That instant is the one the mark-before-purge ordering is about, and it
        /// is unreachable from outside the purge.
        /// </summary>
        internal Action? OnUninitAction { get; set; }

        public bool IsMainThread => false;

        public bool IsCancelled => Cancelled;

        public int TaskIndex => 1;

        public long GetTask(int index, out SqlTaskBase? task)
        {
            task = index == 1 ? Worker : null;
            return task is null ? RetCode.E_OUT_OF_BOUND : RetCode.OK;
        }

        public bool HasData(string name) => false;

        public object? GetData(string name) => null;

        public long SetData(string name, object? data) => RetCode.OK;

        public ISqlTaskProxy? ParentTasking => Proxy;

        public long OnPrepare() => RetCode.OK;

        public void OnUninit()
        {
            Uninits++;

            OnUninitAction?.Invoke();
        }

        public long OnError(long errCode, string errInfo)
        {
            Errors.Add((errCode, errInfo));
            return RetCode.OK;
        }

        public long OnNotify(long notifyCode, long payload, string text) => RetCode.OK;
    }

    /// <summary>
    /// The caller-side threading substrate, with the busy inputs made settable so the E_BUSY guards
    /// are reachable without a real thread.
    /// </summary>
    private sealed class FakeProxyHost : ISqlTaskProxyHost
    {
        internal SqlTaskBase? Worker { get; set; }

        public bool IsRunning { get; set; }

        public bool IsControllerBusy { get; set; }

        public bool IsSyncSignalSet { get; set; }

        public bool IsCancelled { get; set; }

        public CancellationToken Cancellation => CancellationToken.None;

        public long LastExitCode => RetCode.OK;

        public long LastErrorCode => RetCode.OK;

        public string LastErrorInfo => string.Empty;

        public ulong TaskId => 1UL;

        public int TaskIndex => 1;

        public string TaskClassName => "n_cst_thread_task_sqlcommand";

        public SqlTaskBase? Task => Worker;

        public void RaiseSyncSignal() => IsSyncSignalSet = true;

        public void ClearSyncSignal() => IsSyncSignalSet = false;

        public long OnInit(string workerClassName) => RetCode.OK;

        public long OnPrepare() => RetCode.OK;

        public long Cancel()
        {
            IsCancelled = true;
            return RetCode.OK;
        }

        public long SetWorkerDelayFor(double seconds) => RetCode.OK;

        public long SetWorkerSkip(bool skip) => RetCode.OK;
    }

    /// <summary>
    /// Builds the proxy pair the adapter needs, honouring the seam's initialization obligation.
    /// </summary>
    private sealed class FakeCommandTaskFactory : ICommandTaskFactory
    {
        internal FakeCommandTaskFactory(TransactionPool pool, TimeProvider clock)
        {
            Pool = pool;
            Clock = clock;
        }

        private TransactionPool Pool { get; }

        private TimeProvider Clock { get; }

        /// <summary>
        /// When set, <see cref="Create"/> answers this code and produces nothing.
        /// </summary>
        internal long? RefusalCode { get; set; }

        /// <summary>
        /// When set, the pair is returned already busy, so the adapter's descriptor installation is
        /// refused and the unconfigurable pair has to be released rather than registered.
        /// </summary>
        internal bool BusyOnCreate { get; set; }

        internal FakeTaskHost? LastTaskHost { get; private set; }

        internal FakeProxyHost? LastProxyHost { get; private set; }

        public long Create(out CommandTaskComponents? components)
        {
            if (RefusalCode is { } refusal)
            {
                components = null;
                return refusal;
            }

            FakeTaskHost taskHost = new();

            SqlCommandTask worker = new(
                taskHost,
                Pool,
                new UnreachableDataStoreFactory(),
                new SqlRetrievalHookActivator(),
                Clock,
                NullLogger<SqlCommandTask>.Instance);

            taskHost.Worker = worker;

            FakeProxyHost proxyHost = new() { Worker = worker };

            SqlCommandTaskProxy proxy = new(
                proxyHost,
                NullLogger<SqlCommandTaskProxy>.Instance,
                Clock);

            // THE SEAM'S ORDERING OBLIGATION. Initialization borrows the worker's commit signal
            // [n_cst_threading_task_sqlbase.sru:L218], and the committed notification signals only an
            // ALREADY CREATED handle [n_cst_thread_task_sqlbase.sru:L100-L111]. Without this line every
            // ExecResponse.committed would be false even on a real commit.
            long rtCode = proxy.Initialize();
            Assert.Equal(RetCode.OK, rtCode);

            // The worker reaches the caller-side driver-error sink through its substrate.
            taskHost.Proxy = proxy;

            LastTaskHost = taskHost;
            LastProxyHost = proxyHost;

            proxyHost.IsRunning = BusyOnCreate;

            components = new CommandTaskComponents(proxy, worker);
            return RetCode.OK;
        }
    }

    /// <summary>
    /// A logger that records every formatted record, so the C-F claim about what may appear in a
    /// diagnostic is ASSERTED rather than asserted-by-reading.
    /// </summary>
    private sealed class CapturingLogger : ILogger<CommandService>
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
            Records.Add(formatter(state, exception));
    }

    /// <summary>
    /// Wires a pool over the fake engine, the two registries, the factory and both adapters.
    /// </summary>
    private sealed class Harness
    {
        internal Harness(ILogger<CommandService>? logger = null)
        {
            Clock = new FakeClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            Engine = new FakeEngine();

            Pool = new TransactionPool(
                Options.Create(new PersistenceOptions()),
                Clock,
                new PooledTransactionActivator(() => Engine, Clock));

            // The two registries carry a ceiling and an idle window, so both take the bound settings
            // and the harness's fake clock - which is what lets a test drive expiry without waiting.
            IOptions<PersistenceOptions> handleOptions = Options.Create(new PersistenceOptions());

            Sessions = new TransactionSessionRegistry(handleOptions, Clock, Pool);
            Tasks = new CommandTaskRegistry(handleOptions, Clock);
            Factory = new FakeCommandTaskFactory(Pool, Clock);

            // `Tasks` is the SAME table the command service uses, so a session ended through this
            // service genuinely purges the command tasks these cases created.
            Transactions = new TransactionService(
                Pool,
                Sessions,
                new UnusedQuerySurface(),
                new QueryTaskRegistry(handleOptions, Clock),
                new UpdateTaskRegistry(handleOptions, Clock),
                Tasks);
            Commands = new CommandService(
                Sessions,
                Tasks,
                Factory,
                logger ?? NullLogger<CommandService>.Instance);
        }

        internal FakeClock Clock { get; }

        internal FakeEngine Engine { get; }

        internal TransactionPool Pool { get; }

        internal TransactionSessionRegistry Sessions { get; }

        internal CommandTaskRegistry Tasks { get; }

        internal FakeCommandTaskFactory Factory { get; }

        internal TransactionService Transactions { get; }

        internal CommandService Commands { get; }
    }

    // ---------------------------------------------------------------------------------------------
    //  FIXTURE HELPERS
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The nine-field descriptor. <c>Autocommit</c> is FALSE deliberately: the transaction refuses an
    /// explicit commit while its own autocommit is on [<c>n_cst_thread_trans.sru:L240</c>], so an AC_ON
    /// fixture has to open with it off for the commit arm to be reachable at all.
    /// </summary>
    private static TransactionDescriptor Descriptor() => new()
    {
        Dbms = "SQLite",
        Servername = "server-1",
        Database = "test.db",
        Logid = "account-1",
        Logpass = FakePassword,
        Dbparm = "DisableBind=1",
        Lock = "RU",
        Autocommit = false,
        Userparm = "fixture-userparm",
    };

    private static async Task<SessionHandle> OpenSession(Harness harness)
    {
        BeginSessionResponse response = await harness.Transactions.BeginSession(
            new BeginSessionRequest { Descriptor_ = Descriptor() },
            Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
        Assert.NotNull(response.Session);

        return response.Session;
    }

    private static async Task<TaskHandle> CreateTask(Harness harness)
    {
        SessionHandle session = await OpenSession(harness);

        CreateCommandTaskResponse response = await harness.Commands.CreateCommandTask(
            new CreateCommandTaskRequest { Session = session },
            Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
        Assert.NotNull(response.Task);

        return response.Task;
    }

    private static PositionalParameter Positional(long value) =>
        new() { Name = string.Empty, Value = new AnyValue { Int64Value = value } };

    private static PositionalParameter Named(string name, long value) =>
        new() { Name = name, Value = new AnyValue { Int64Value = value } };

    private static CommandTask Resolve(Harness harness, TaskHandle handle)
    {
        Assert.True(harness.Tasks.TryResolve(handle, out CommandTask? task));
        Assert.NotNull(task);
        return task;
    }

    // ---------------------------------------------------------------------------------------------
    //  REGION 1 - THE AUTOCOMMIT DOMAIN: THE DEFAULT, AND THE VALIDATE-NOTHING SETTER
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A freshly created task holds <c>AC_OFF</c> - the oracle initialises <c>_nAutoCommit = AC_OFF</c>
    /// [<c>:L21</c>], and because <c>AC_OFF</c> genuinely occupies zero an unset wire field means the
    /// same thing.
    /// </summary>
    [Fact]
    public async Task ANewTaskDefaultsToAcOff()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        Assert.Equal(AutoCommitMode.AcOff, Resolve(harness, handle).Worker.AutoCommitSetting);
    }

    /// <summary>
    /// A reset restores <c>AC_OFF</c> and clears the statement [<c>:L34-L35</c>]. Both halves are
    /// contract: a caller that reset a task and ran it without setting the mode again gets
    /// <c>AC_OFF</c>, not whatever was set before.
    /// </summary>
    [Fact]
    public async Task ResetRestoresAcOffAndClearsTheStatement()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        Assert.Equal(
            WireRetCode.Ok,
            (await harness.Commands.SetSql(
                new SetCommandSqlRequest { Task = handle, Sql = "DELETE FROM t" },
                Context)).Status.RetCode);

        Assert.Equal(
            WireRetCode.Ok,
            (await harness.Commands.SetAutoCommit(
                new SetCommandAutoCommitRequest { Task = handle, Autocommit = AutoCommitMode.AcOn },
                Context)).Status.RetCode);

        ResetCommandTaskResponse reset = await harness.Commands.Reset(
            new ResetCommandTaskRequest { Task = handle },
            Context);

        Assert.Equal(WireRetCode.Ok, reset.Status.RetCode);

        CommandTask task = Resolve(harness, handle);
        Assert.Equal(AutoCommitMode.AcOff, task.Worker.AutoCommitSetting);
        Assert.Equal(string.Empty, task.Worker.Sql);
    }

    /// <summary>
    /// ⚠ THE WORKER validates NOTHING [<c>:L40-L43</c>] and THE BOUNDARY does. A proto3 enum is open, so a
    /// value outside the three enumerators arrives intact - and the RPC refuses it with
    /// <c>E_INVALID_ARGUMENT</c> rather than forwarding it, because an undeclared number is a malformed
    /// request rather than a legacy input. Folding it into <c>AC_OFF</c> silently, which is what the
    /// epilogue's else arm would do, means a caller who asked for a commit gets no commit and is told
    /// nothing. The worker's own tolerance is untouched and is asserted separately below.
    /// </summary>
    [Fact]
    public async Task SetAutoCommitRefusesAValueOutsideTheThreeEnumeratorsAtTheBoundary()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        SetCommandAutoCommitResponse response = await harness.Commands.SetAutoCommit(
            new SetCommandAutoCommitRequest { Task = handle, Autocommit = (AutoCommitMode)7 },
            Context);

        Assert.Equal(WireRetCode.EInvalidArgument, response.Status.RetCode);
        Assert.Contains("AC_NATIVE", response.Status.ErrorText, StringComparison.Ordinal);

        // THE REFUSAL IS ATOMIC: the task keeps the mode it had, so a rejected call leaves no half-applied
        // state behind for the next execution to pick up.
        Assert.Equal(AutoCommitMode.AcOff, Resolve(harness, handle).Worker.AutoCommitSetting);
    }

    /// <summary>
    /// The three declared modes all pass the boundary's domain test - so the refusal above is a domain
    /// test and not an accidental narrowing of the contract's own values.
    /// </summary>
    /// <param name="mode">The declared mode under test.</param>
    [Theory]
    [InlineData(AutoCommitMode.AcOff)]
    [InlineData(AutoCommitMode.AcOn)]
    [InlineData(AutoCommitMode.AcNative)]
    public async Task SetAutoCommitAcceptsEveryDeclaredMode(AutoCommitMode mode)
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        SetCommandAutoCommitResponse response = await harness.Commands.SetAutoCommit(
            new SetCommandAutoCommitRequest { Task = handle, Autocommit = mode },
            Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
        Assert.Equal(mode, Resolve(harness, handle).Worker.AutoCommitSetting);
    }

    /// <summary>
    /// ⚠ THE WORKER's OWN TOLERANCE IS PRESERVED VERBATIM (constraint C-B). Reached directly rather than
    /// through the RPC - which is the only way to reach it now - this proves the boundary refusal above
    /// added a guard at the boundary WITHOUT reviving the range check the legacy setter does not have.
    /// </summary>
    [Fact]
    public async Task TheWorkerStillStoresAValueOutsideTheThreeEnumeratorsWithoutValidating()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);
        CommandTask task = Resolve(harness, handle);

        Assert.Equal(RetCode.OK, task.Proxy.SetAutoCommit((AutoCommitMode)7));
        Assert.Equal((AutoCommitMode)7, task.Worker.AutoCommitSetting);

        await Task.CompletedTask;
    }

    // ---------------------------------------------------------------------------------------------
    //  REGION 2 - THE THREE EPILOGUES, ONE TEST EACH, PLUS THE ASYMMETRY THAT LOOKS LIKE A BUG
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// ⚠ <c>AC_OFF</c> commits NOTHING. The success epilogue is an <c>if</c>/<c>else if</c> with no
    /// <c>else</c> [<c>:L94-L103</c>], because the caller's session owns the transaction and decides
    /// when the work becomes durable.
    /// </summary>
    [Fact]
    public async Task AcOffSuccessCommitsNothingAndReportsUncommitted()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest { Task = handle, Sql = "DELETE FROM t WHERE id = 1" },
            Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
        Assert.Equal(0, harness.Engine.CommitCalls);
        Assert.Equal(0, harness.Engine.RollbackCalls);

        // The committed flag is read through a ZERO-TIMEOUT probe
        // [n_cst_threading_task_sqlbase.sru:L174]. A blocking wait on a manual-reset event that is
        // never set would hang this test forever rather than answering false, so the fact that this
        // assertion is reachable at all is what proves the read is non-blocking.
        Assert.False(response.Committed);
    }

    /// <summary>
    /// <c>AC_ON</c> commits explicitly through the task's commit helper [<c>:L99</c>], and the helper
    /// raises the committed notification only on a successful commit
    /// [<c>n_cst_thread_task_sqlbase.sru:L235-L237</c>].
    /// </summary>
    [Fact]
    public async Task AcOnSuccessCommitsAndReportsCommitted()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest
            {
                Task = handle,
                Sql = "INSERT INTO t(id) VALUES(1)",
                Autocommit = AutoCommitMode.AcOn,
            },
            Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
        Assert.Equal(1, harness.Engine.CommitCalls);
        Assert.True(response.Committed);
    }

    /// <summary>
    /// ⚠ On <c>AC_ON</c> the COMMIT'S code REPLACES the statement's [<c>:L99</c>], so a statement that
    /// succeeded and then failed to commit is reported as a FAILURE and as UNCOMMITTED. An
    /// implementation that returned the statement's own code would overstate durability.
    /// </summary>
    [Fact]
    public async Task AcOnCommitFailureReplacesTheStatementsOwnCodeAndReportsUncommitted()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        harness.Engine.CommitResult = SqlState.Failed(-5, "commit refused");

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest
            {
                Task = handle,
                Sql = "INSERT INTO t(id) VALUES(1)",
                Autocommit = AutoCommitMode.AcOn,
            },
            Context);

        Assert.Equal(WireRetCode.EDbError, response.Status.RetCode);
        Assert.Equal(1, harness.Engine.CommitCalls);
        Assert.False(response.Committed);

        // The statement status is the LAST operation's, which on this path is the COMMIT's rather than
        // the statement's - the oracle's SQLCode is likewise whatever the most recent database operation
        // left behind. So a caller must not infer "which step failed" from sql_code; that is what
        // ret_code is for, and it is why the three code spaces are documented as non-comparable.
        Assert.Equal(-1L, response.SqlCode);
        Assert.Equal(-5L, response.SqlDbCode);
    }

    /// <summary>
    /// ⚠ THE AC_NATIVE INVERSION. The mode whose source comment reads 不开启事务 - "does NOT open a
    /// transaction" - is implemented by turning the connection's OWN autocommit flag <b>ON</b> before
    /// the statement [<c>:L88-L90</c>] and back OFF afterwards [<c>:L96</c>], and it raises the
    /// committed notification INSTEAD of committing [<c>:L97</c>].
    /// </summary>
    [Fact]
    public async Task AcNativeSuccessTogglesTheFlagOnThenOffAndReportsCommittedWithoutCommitting()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        harness.Engine.AutoCommitWrites.Clear();

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest
            {
                Task = handle,
                Sql = "INSERT INTO t(id) VALUES(1)",
                Autocommit = AutoCommitMode.AcNative,
            },
            Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
        Assert.Equal(new[] { true, false }, harness.Engine.AutoCommitWrites);

        // No explicit commit was issued - the driver committed as part of executing the statement.
        Assert.Equal(0, harness.Engine.CommitCalls);
        Assert.True(response.Committed);
    }

    /// <summary>
    /// ⚠ THE FINDING MOST LIKELY TO BE GOT WRONG IN EITHER DIRECTION. <c>AC_NATIVE</c>'s failure arm
    /// only restores the flag [<c>:L105-L106</c>] and SKIPS the <c>else</c> arm's rollback
    /// [<c>:L108</c>], because there was no explicit transaction to roll back - while the general-error
    /// event fired immediately afterwards [<c>:L111</c>] rolls back on the base
    /// [<c>n_cst_thread_task_sqlbase.sru:L730-L734</c>]. So the observable rollback counts on the
    /// failure path are exactly ONE for native and exactly TWO for every other mode. The contrast IS
    /// the assertion: suppressing the second to make the arms "consistent" would be a behavioural
    /// change, and so would adding a rollback to the native arm.
    /// </summary>
    [Fact]
    public async Task AcNativeFailureRollsBackOnceWhereAcOffFailureRollsBackTwice()
    {
        Harness nativeHarness = new();
        TaskHandle nativeHandle = await CreateTask(nativeHarness);
        nativeHarness.Engine.ExecuteResult = SqlState.Failed(-1, "constraint violated");
        nativeHarness.Engine.AutoCommitWrites.Clear();

        ExecResponse nativeResponse = await nativeHarness.Commands.Exec(
            new ExecRequest
            {
                Task = nativeHandle,
                Sql = "INSERT INTO t(id) VALUES(1)",
                Autocommit = AutoCommitMode.AcNative,
            },
            Context);

        Harness offHarness = new();
        TaskHandle offHandle = await CreateTask(offHarness);
        offHarness.Engine.ExecuteResult = SqlState.Failed(-1, "constraint violated");

        ExecResponse offResponse = await offHarness.Commands.Exec(
            new ExecRequest
            {
                Task = offHandle,
                Sql = "INSERT INTO t(id) VALUES(1)",
                Autocommit = AutoCommitMode.AcOff,
            },
            Context);

        Assert.Equal(WireRetCode.EDbError, nativeResponse.Status.RetCode);
        Assert.Equal(WireRetCode.EDbError, offResponse.Status.RetCode);

        Assert.Equal(1, nativeHarness.Engine.RollbackCalls);
        Assert.Equal(2, offHarness.Engine.RollbackCalls);

        // The flag is restored on the FAILURE path too, not only on the success path [:L106].
        Assert.Equal(new[] { true, false }, nativeHarness.Engine.AutoCommitWrites);

        // Neither mode reports durability on a failing statement.
        Assert.False(nativeResponse.Committed);
        Assert.False(offResponse.Committed);
    }

    /// <summary>
    /// 🔴 On a REUSED task the durability signal is re-armed for every dispatch, so a statement that fails
    /// after one that committed reports <c>committed = false</c>.
    /// </summary>
    /// <param name="mode">All three modes, because the rule the contract states applies to every one.</param>
    /// <remarks>
    /// <para>
    /// THE SIGNAL IS MANUAL-RESET, AND THAT IS WHY A DISPATCH HAS TO LOWER IT. The worker creates it with
    /// <c>CreateEvent(0, true, false, 0)</c> - manual-reset, initially unsignalled
    /// [<c>n_cst_thread_task_sqlbase.sru:L224-L226</c>] - so that the zero-timeout poll behind
    /// <c>committed</c> answers the same thing every time it is asked. Nothing consumes it by reading it;
    /// only the worker's prepare event lowers it [<c>:L725-L727</c>], and the substrate raises that event
    /// before every run of the body.
    /// </para>
    /// <para>
    /// WHILE THAT EVENT WENT UNRAISED THE FLAG WAS A LATCH RATHER THAN A REPORT. Reusing a task is
    /// explicitly supported - the contract exposes <c>SetSql</c> and <c>Exec</c> as separate calls for
    /// exactly that - so the first statement that committed left the signal set for the life of the
    /// handle, and every later FAILING statement answered <c>committed = true</c>. The contract states the
    /// rule verbatim: any failing statement answers false, on every mode, because the success arm is not
    /// entered at all. Claiming durability for work that never happened is the worse direction of that
    /// error, since a caller acts on it by not retrying.
    /// </para>
    /// <para>
    /// THE SEQUENCE IS FOUR DISPATCHES ON ONE HANDLE and each assertion needs the ones before it: the
    /// first proves the signal rises, the second that it falls again, the third that falling did not break
    /// it, and the fourth that the fall is repeatable rather than a one-off. A latch would pass the first
    /// and fail the second; a signal that was destroyed rather than lowered would pass the second and fail
    /// the third.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(AutoCommitMode.AcNative)]
    [InlineData(AutoCommitMode.AcOn)]
    public async Task AReusedTaskReArmsTheDurabilitySignalForEveryDispatch(AutoCommitMode mode)
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        async Task<ExecResponse> Dispatch(string sql) => await harness.Commands.Exec(
            new ExecRequest { Task = handle, Sql = sql, Autocommit = mode },
            Context);

        ExecResponse committed = await Dispatch("INSERT INTO t(id) VALUES(1)");

        Assert.Equal(WireRetCode.Ok, committed.Status.RetCode);
        Assert.True(committed.Committed);

        // THE SAME TASK, A STATEMENT THE ENGINE REFUSES. No success arm runs, so nothing raises the
        // notification - and the previous dispatch's raised signal must not answer for this one.
        harness.Engine.ExecuteResult = SqlState.Failed(-1, "no such table");

        ExecResponse failed = await Dispatch("INSERT INTO nowhere(id) VALUES(2)");

        Assert.Equal(WireRetCode.EDbError, failed.Status.RetCode);
        Assert.False(failed.Committed);

        // THE SIGNAL WAS LOWERED, NOT DESTROYED: the next success raises it again.
        harness.Engine.ExecuteResult = SqlState.Succeeded(1L);

        ExecResponse committedAgain = await Dispatch("INSERT INTO t(id) VALUES(3)");

        Assert.Equal(WireRetCode.Ok, committedAgain.Status.RetCode);
        Assert.True(committedAgain.Committed);

        // AND THE FALL IS REPEATABLE rather than a single re-arm that then latches again.
        harness.Engine.ExecuteResult = SqlState.Failed(-1, "no such table");

        ExecResponse failedAgain = await Dispatch("INSERT INTO nowhere(id) VALUES(4)");

        Assert.Equal(WireRetCode.EDbError, failedAgain.Status.RetCode);
        Assert.False(failedAgain.Committed);
    }

    /// <summary>
    /// On a REUSED task the latched driver payload belongs to the dispatch that produced it, so a
    /// successful statement after a failing one carries none.
    /// </summary>
    /// <remarks>
    /// THE CALLER-SIDE HALF OF THE SAME PREPARE EVENT [<c>n_cst_threading_task_sqlbase.sru:L208</c>]. The
    /// sink is last-error-wins and is never accumulated, but nothing in the run CLEARS it - so without the
    /// per-dispatch raise a payload produced by an earlier failure travelled with a later success, and the
    /// presence of that payload IS the signal that a driver error occurred. A caller would read a
    /// database error on a statement that worked.
    /// </remarks>
    [Fact]
    public async Task AReusedTaskDoesNotCarryAnEarlierDispatchsDriverPayload()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        harness.Engine.ExecuteResult = SqlState.Failed(-1, "no such table");

        ExecResponse failed = await harness.Commands.Exec(
            new ExecRequest { Task = handle, Sql = "INSERT INTO nowhere(id) VALUES(1)" },
            Context);

        Assert.Equal(WireRetCode.EDbError, failed.Status.RetCode);
        Assert.NotNull(failed.Status.DbError);

        harness.Engine.ExecuteResult = SqlState.Succeeded(1L);

        ExecResponse succeeded = await harness.Commands.Exec(
            new ExecRequest { Task = handle, Sql = "INSERT INTO t(id) VALUES(2)" },
            Context);

        Assert.Equal(WireRetCode.Ok, succeeded.Status.RetCode);
        Assert.Null(succeeded.Status.DbError);
    }

    // ---------------------------------------------------------------------------------------------
    //  REGION 3 - THE EMPTY STATEMENT, TWICE, WITH TWO DIFFERENT OBSERVABLE MESSAGES
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// PINNED SPLIT, at the adapter layer, matching
    /// <c>SqlCommandTaskProxyTests.AnEmptyStatementBehavesDifferentlyInTheTwoBuilds</c> one layer down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠ In a <b>Release</b> build an empty statement is <c>E_INVALID_SQL</c> and NOT
    /// <c>E_INVALID_ARGUMENT</c> [<c>:L45</c>] - an empty statement is classified as a bad STATEMENT
    /// rather than as a bad argument, unlike most other guards in the SQL layer. The setter's arm
    /// carries NO message.
    /// </para>
    /// <para>
    /// ⚠ In a <b>Debug</b> build that arm is UNREACHABLE through this adapter. The proxy reproduces the
    /// oracle's own <c>#IF DEFINED DEBUG THEN Assert(Len(sql) &gt; 0,"Len(sql) &lt;= 0") #END IF</c>
    /// [<c>n_cst_threading_task_sqlcommand.sru:L36-L38</c>], which trips before the delegation at
    /// [<c>:L40</c>], so <c>AssertionFailure</c> leaves the handler and the worker's guard is never
    /// reached. That gating is preserved deliberately under C-B and is neither promoted to a runtime
    /// guard nor removed - see the remarks on
    /// <see cref="PowerFramework.Persistence.Tasks.TaskProxies.SqlCommandTaskProxy.SetSql"/>.
    /// </para>
    /// <para>
    /// BOTH arms are asserted because both are shipped: the container image is built
    /// <c>-c Release</c>, while the documented per-service gate's bare <c>dotnet test</c> builds Debug.
    /// Asserting only the Release arm makes the suite pass under the
    /// whole-solution Release sweep and fail under the documented per-service command, for a
    /// difference the port intends.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task SetSqlOnAnEmptyStatementBehavesDifferentlyInTheTwoBuilds()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

#if DEBUG
        AssertionFailure failure = await Assert.ThrowsAsync<AssertionFailure>(
            () => harness.Commands.SetSql(
                new SetCommandSqlRequest { Task = handle, Sql = string.Empty },
                null!));

        // The oracle's own message text, verbatim, which is what a failing Debug build reports.
        Assert.Contains("Len(sql) <= 0", failure.Message, StringComparison.Ordinal);
#else
        SetCommandSqlResponse response = await harness.Commands.SetSql(
            new SetCommandSqlRequest { Task = handle, Sql = string.Empty },
            null!);

        Assert.Equal(WireRetCode.EInvalidSql, response.Status.RetCode);
        Assert.Equal(string.Empty, response.Status.ErrorText);
#endif
    }

    /// <summary>
    /// ⚠ THE LEGACY CHECKS THE SAME CONDITION TWICE AND THE TWO GUARDS ARE OBSERVABLY DIFFERENT. The
    /// task body's guard answers the same code with a VERBATIM CHINESE diagnostic [<c>:L65-L68</c>]
    /// where the setter's carries none, and it is the only one a caller who never called the setter can
    /// reach - exactly this case. Collapsing the two would remove an observable difference.
    /// </summary>
    [Fact]
    public async Task ExecWithNoStatementEverSetRefusesWithTheWorkersOwnChineseDiagnostic()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest { Task = handle },
            Context);

        Assert.Equal(WireRetCode.EInvalidSql, response.Status.RetCode);
        Assert.Equal(SqlCommandTask.InvalidSqlMessage, response.Status.ErrorText);

        // BEFORE the transaction is acquired, so nothing was executed [:L65 precedes :L70].
        Assert.Equal(0, harness.Engine.ExecuteCalls);
    }

    /// <summary>
    /// ⚠ A PRESENT-BUT-BLANK statement is ACCEPTED and stored VERBATIM, because the oracle's guard is an
    /// emptiness test and this port may not be stricter than the behaviour it preserves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE GUARD BEING PRESERVED IS <c>sql = ""</c> AND NOTHING WIDER.</b> The oracle writes
    /// <c>if sql = "" then return RetCode.E_INVALID_SQL</c> [<c>:L45</c>] and repeats the same emptiness
    /// test in the task body [<c>:L65-L68</c>]. Neither tests blankness, so a run of spaces is accepted,
    /// installed on the task and later submitted - and whatever the provider answers for it is the
    /// answer the legacy gave.
    /// </para>
    /// <para>
    /// 🔴 <b>A BOUNDARY BLANKNESS REFUSAL IS THE TEMPTING ADDITION HERE, AND THIS ROW IS WHAT KEEPS IT
    /// OUT.</b> It would answer <c>E_INVALID_SQL</c> with its own diagnostic for a
    /// whitespace-only statement, on the reasoning that submitting one is a success that did nothing and
    /// an actionable refusal is better. That reasoning is a JUDGEMENT ABOUT THE LEGACY'S DESIGN, not a
    /// statement about its behaviour, and acting on it makes the port refuse input the legacy accepts.
    /// Constraint C-B and AAP G2 forbid that in either direction: a port that is stricter than its
    /// oracle has changed behaviour exactly as much as one that is laxer, and "the legacy defect is
    /// documented, never corrected" is the whole posture of this refactor.
    /// </para>
    /// <para>
    /// STORED VERBATIM AND NOT TRIMMED, which is the second half of the claim: the value that reaches the
    /// worker is the value the caller sent, character for character.
    /// </para>
    /// </remarks>
    /// <param name="blank">The blank statement under test.</param>
    [Theory]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    public async Task SetSqlAcceptsAPresentButBlankStatementVerbatim(string blank)
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        SetCommandSqlResponse response = await harness.Commands.SetSql(
            new SetCommandSqlRequest { Task = handle, Sql = blank },
            Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);

        // VERBATIM: not trimmed, not normalised, not rejected.
        Assert.Equal(blank, Resolve(harness, handle).Worker.Sql);
    }

    /// <summary>
    /// The same acceptance on <c>Exec</c>'s optional statement field, so the two entry points agree - and
    /// the statement really is submitted rather than quietly dropped.
    /// </summary>
    /// <remarks>
    /// THE EXECUTION IS THE POINT. A refusal at the boundary and a silent no-op are both distinguishable
    /// from what the legacy does, which is to hand the text to the provider and report what came back; this
    /// row reads the execution seam to say that is what happens, and reads the statement recorded there to
    /// say the text arrived unaltered.
    /// </remarks>
    [Fact]
    public async Task ExecAcceptsAPresentButBlankStatementAndSubmitsIt()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest { Task = handle, Sql = "   " },
            Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
        Assert.Equal(1, harness.Engine.ExecuteCalls);
        Assert.Equal("   ", harness.Engine.LastStatement);
    }

    /// <summary>
    /// ⚠ THE EMPTY STRING IS THE ONE CASE THE ORACLE ITSELF REFUSES, and the refusal is the WORKER'S own
    /// preserved arm - the same code with NO diagnostic at all [<c>:L45</c>]. It is asserted beside the
    /// blank rows above precisely because the two look alike and behave differently: empty is refused
    /// because the legacy refuses it, whitespace is accepted because the legacy accepts it, and no
    /// boundary test of this service's own invention stands between them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>SPLIT BY BUILD, for the same reason as
    /// <see cref="SetSqlOnAnEmptyStatementBehavesDifferentlyInTheTwoBuilds"/>.</b> The proxy the RPC
    /// forwards to carries the oracle's own <c>#IF DEFINED DEBUG</c> assertion
    /// [<c>n_cst_threading_task_sqlcommand.sru:L36-L38</c>], reproduced verbatim in
    /// <c>SqlCommandTaskProxy.SetSql</c>, so the empty statement raises <see cref="AssertionFailure"/>
    /// before the worker is ever reached in a Debug build and only ever reaches the worker's message-free
    /// refusal in Release. Asserting the Release arm alone made this row pass under the whole-solution
    /// Release sweep and fail under the documented per-service gate, whose bare <c>dotnet test</c> builds
    /// Debug - for a difference the port INTENDS rather than a defect.
    /// </para>
    /// <para>
    /// The Release arm below is unchanged, so what this row asserted before it still asserts.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task SetSqlStillAnswersTheWorkersMessageFreeRefusalForTheEmptyStatement()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

#if DEBUG
        AssertionFailure failure = await Assert.ThrowsAsync<AssertionFailure>(
            () => harness.Commands.SetSql(
                new SetCommandSqlRequest { Task = handle, Sql = string.Empty },
                Context));

        // The oracle's own message text, verbatim - the same one the sibling row above reports.
        Assert.Contains("Len(sql) <= 0", failure.Message, StringComparison.Ordinal);
#else
        SetCommandSqlResponse response = await harness.Commands.SetSql(
            new SetCommandSqlRequest { Task = handle, Sql = string.Empty },
            Context);

        Assert.Equal(WireRetCode.EInvalidSql, response.Status.RetCode);
        Assert.Equal(string.Empty, response.Status.ErrorText);
#endif
    }

    /// <summary>
    /// A statement made only of a COMMENT is stored verbatim, which is the control for the whole group: a
    /// statement that plainly does nothing is still not this service's business to judge.
    /// </summary>
    [Fact]
    public async Task SetSqlAcceptsAStatementThatIsOnlyAComment()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        SetCommandSqlResponse response = await harness.Commands.SetSql(
            new SetCommandSqlRequest { Task = handle, Sql = "-- nothing" },
            Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
        Assert.Equal("-- nothing", Resolve(harness, handle).Worker.Sql);
    }

    /// <summary>
    /// <c>Exec</c> refuses an out-of-domain autocommit override on the same terms the setter RPC does, and
    /// executes nothing - so a malformed mode cannot reach the epilogue's else arm and be silently read as
    /// <c>AC_OFF</c>.
    /// </summary>
    [Fact]
    public async Task ExecRefusesAnOutOfDomainAutoCommitOverrideAndExecutesNothing()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest
            {
                Task = handle,
                Sql = "DELETE FROM t",
                Autocommit = (AutoCommitMode)99,
            },
            Context);

        Assert.Equal(WireRetCode.EInvalidArgument, response.Status.RetCode);
        Assert.Contains("AC_OFF", response.Status.ErrorText, StringComparison.Ordinal);
        Assert.Equal(0, harness.Engine.ExecuteCalls);
    }

    // ---------------------------------------------------------------------------------------------
    //  REGION 4 - THE PRESERVED SUCCESS CARVE-OUT
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// ⚠ <c>SQLCode = 100</c> IS SUCCESS, NOT AN ERROR. The transaction's command path tests
    /// <c>if SQLCode &lt;&gt; 0 and SQLCode &lt;&gt; 100</c> [<c>n_cst_thread_trans.sru:L233</c>], so a
    /// "nothing found" outcome is explicitly not a failure. An implementation treating any non-zero
    /// code as failure would invent errors the legacy does not report.
    /// </summary>
    [Fact]
    public async Task SqlCode100IsClassifiedAsSuccessAndCarriedVerbatim()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        harness.Engine.ExecuteResult = new SqlState(100L, 0L, 0L, string.Empty, string.Empty);

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest { Task = handle, Sql = "DELETE FROM t WHERE id = 999" },
            Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
        Assert.Equal(100L, response.SqlCode);

        // A success raises neither error event, so there is no payload and no diagnostic.
        Assert.Null(response.Status.DbError);
        Assert.Equal(string.Empty, response.Status.ErrorText);
        Assert.Equal(0, harness.Engine.RollbackCalls);
    }

    // ---------------------------------------------------------------------------------------------
    //  REGION 5 - THE ORDERED FAILURE ARMS
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A failed transaction acquisition raises the DATABASE-ERROR event AND answers
    /// <c>E_INVALID_TRANSACTION</c> - both, not either [<c>:L70-L74</c>] - and it does so BEFORE the
    /// statement is executed. The diagnostic is the ACQUIRED PAYLOAD's text rather than a synthesized
    /// one [<c>:L72</c>].
    /// </summary>
    /// <remarks>
    /// The lever is deliberately two-part, because condemning the connection alone is NOT enough: the
    /// pool replaces a broken entry's transaction and disposes the old one
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L164-L172</c>], so the
    /// acquisition is handed a FRESH transaction which has never connected - and it is that
    /// transaction's connect attempt which then fails. Getting this wrong is what first revealed that
    /// the response must read the driver accessors off the instance the WORKER attached rather than off
    /// the one the session captured, since the latter is by then disposed.
    /// </remarks>
    [Fact]
    public async Task AFailedAcquisitionRaisesTheDriverPayloadAndAnswersEInvalidTransaction()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        Resolve(harness, handle).Session.Transaction.SetBroken();
        harness.Engine.ConnectResult = SqlState.Failed(-99, "connect refused");

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest { Task = handle, Sql = "DELETE FROM t" },
            Context);

        Assert.Equal(WireRetCode.EInvalidTransaction, response.Status.RetCode);
        Assert.Equal(0, harness.Engine.ExecuteCalls);

        // BOTH events fired, so a payload AND a message coexist.
        Assert.NotNull(response.Status.DbError);
        Assert.Equal(-99L, response.Status.DbError.Sqldbcode);
        Assert.Equal("connect refused", response.Status.DbError.Sqlerrtext);
        Assert.Equal("connect refused", response.Status.ErrorText);

        // The acquisition arm raises with an EMPTY statement, because a failure to acquire has no
        // statement [:L71]; the buffer is Primary and the row is 0 on every raise from this path.
        Assert.Equal(string.Empty, response.Status.DbError.Sqlsyntax);
        Assert.Equal(DwBuffer.Primary, response.Status.DbError.Buffer);
        Assert.Equal(0L, response.Status.DbError.Row);
    }

    /// <summary>
    /// Cancellation answers <c>CANCELLED</c> SILENTLY - no event of either kind is raised
    /// [<c>:L76-L78</c>] - and it does so BEFORE binding, so a cancelled task never interpolates a
    /// caller's value into a statement.
    /// </summary>
    [Fact]
    public async Task CancellationAnswersCancelledSilentlyAndBindsNothing()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        harness.Factory.LastTaskHost!.Cancelled = true;

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest
            {
                Task = handle,
                Sql = "DELETE FROM t WHERE id = :id",
                Parameters = { Positional(7L) },
            },
            Context);

        Assert.Equal(WireRetCode.Canceled, response.Status.RetCode);

        // No event, so no message and no payload.
        Assert.Equal(string.Empty, response.Status.ErrorText);
        Assert.Null(response.Status.DbError);
        Assert.Empty(harness.Factory.LastTaskHost!.Errors);
        Assert.Equal(0, harness.Engine.ExecuteCalls);
    }

    /// <summary>
    /// A binding failure answers <c>E_SQL_BIND_ARG_FAILED</c> with the worker's own verbatim Chinese
    /// diagnostic [<c>:L82-L85</c>]. Reached here by naming a parameter that matches no placeholder, so
    /// the binder's unreplaced-placeholder check refuses
    /// [<c>n_cst_thread_task_sqlbase.sru:L477-L479</c>].
    /// </summary>
    [Fact]
    public async Task ABindingFailureAnswersESqlBindArgFailedWithTheWorkersOwnDiagnostic()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest
            {
                Task = handle,
                Sql = "UPDATE t SET a = :expected",
                Parameters = { Named("unexpected", 1L) },
            },
            Context);

        Assert.Equal(WireRetCode.ESqlBindArgFailed, response.Status.RetCode);
        Assert.Equal(SqlCommandTask.BindArgumentFailureMessage, response.Status.ErrorText);

        // The half-bound statement is never executed, and it never reaches the response either.
        Assert.Equal(0, harness.Engine.ExecuteCalls);
        Assert.Null(response.Status.DbError);
    }

    // ---------------------------------------------------------------------------------------------
    //  REGION 6 - PARAMETERS: THE EMPTY-NAMED CASE, THE SHAPE GUARDS, AND THE DORMANT ONE
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A POSITIONAL parameter IS the empty-NAMED case, and the binder treats an empty name as "match the
    /// next unreplaced placeholder" rather than as a name to look up
    /// [<c>n_cst_threading_task_sqlbase.sru:L138</c>,
    /// <c>n_cst_thread_task_sqlbase.sru:L460</c>, <c>:L472</c>]. The contrast with the named case is the
    /// proof: the identical statement and value bind successfully when the name is empty and FAIL when
    /// the name does not match, so the empty name is doing real work rather than being a missing field.
    /// </summary>
    [Fact]
    public async Task AnEmptyNamedParameterBindsPositionallyWhereANonMatchingNameDoesNot()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        ExecResponse positional = await harness.Commands.Exec(
            new ExecRequest
            {
                Task = handle,
                Sql = "UPDATE t SET a = :expected",
                Parameters = { Positional(4242L) },
            },
            Context);

        Assert.Equal(WireRetCode.Ok, positional.Status.RetCode);

        // The placeholder was substituted, so the interpolated value is in the executed statement and
        // the placeholder is gone.
        Assert.Contains("4242", harness.Engine.LastStatement, StringComparison.Ordinal);
        Assert.DoesNotContain(":expected", harness.Engine.LastStatement, StringComparison.Ordinal);

        ExecResponse named = await harness.Commands.Exec(
            new ExecRequest
            {
                Task = handle,
                Sql = "UPDATE t SET a = :expected",
                Parameters = { Named("unexpected", 4242L) },
            },
            Context);

        Assert.Equal(WireRetCode.ESqlBindArgFailed, named.Status.RetCode);
    }

    /// <summary>
    /// ⚠ A TWELFTH ARGUMENT IS ACCEPTED. The legacy surfaces stop at ELEVEN
    /// [<c>ws_objects/pfw.utility.sqlite.pbl.src/n_sqlite.sru:L42-L43</c>], and the neighbouring
    /// unrolled ceilings are twenty and eight, ONLY because PowerScript cannot forward an
    /// arbitrary-length argument list. Those are statements about the source language, so exceeding them
    /// is not a behavioural regression and NO VALIDATION REJECTING A TWELFTH ARGUMENT MAY EXIST. This
    /// test is what would fail if someone added one.
    /// </summary>
    [Fact]
    public async Task ATwelfthArgumentIsAcceptedBecauseTheLegacyCeilingIsRecordedAndNotEnforced()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        ExecRequest request = new()
        {
            Task = handle,
            Sql = "INSERT INTO t VALUES(:a,:b,:c,:d,:e,:f,:g,:h,:i,:j,:k,:l)",
        };

        // Twelve values, one past the eleven-argument ceiling, each distinctive enough to be found in
        // the interpolated statement.
        for (long index = 1; index <= 12; index++)
        {
            request.Parameters.Add(Positional((index * 1000L) + index));
        }

        ExecResponse response = await harness.Commands.Exec(request, Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
        Assert.Equal(12, request.Parameters.Count);

        for (long index = 1; index <= 12; index++)
        {
            Assert.Contains(
                ((index * 1000L) + index).ToString(System.Globalization.CultureInfo.InvariantCulture),
                harness.Engine.LastStatement,
                StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A wire value with NO ARM SET is refused with <c>E_INVALID_ARGUMENT</c>. That is the contract's own
    /// narrowing rather than a guess: <c>is_null = true</c> is how a null is STATED, so an empty value
    /// message is a producer that said nothing, and reading it as null would accept a truncated payload
    /// as a legitimate one.
    /// </summary>
    [Fact]
    public async Task AParameterWithNoValueArmIsRefusedWithEInvalidArgument()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest
            {
                Task = handle,
                Sql = "UPDATE t SET a = :expected",
                Parameters = { new PositionalParameter { Name = string.Empty, Value = new AnyValue() } },
            },
            Context);

        Assert.Equal(WireRetCode.EInvalidArgument, response.Status.RetCode);
        Assert.NotEqual(string.Empty, response.Status.ErrorText);
        Assert.Equal(0, harness.Engine.ExecuteCalls);
    }

    /// <summary>
    /// The SHAPE guards and the DORMANT one, asserted at the seam the adapter delegates to. A value of
    /// rank two or higher and an empty array are each refused with <c>E_INVALID_ARGUMENT</c>
    /// [<c>n_cst_threading_task_sqlbase.sru:L148-L149</c>], while a NULL VALUE IS ACCEPTED because the
    /// oracle's null check is COMMENTED OUT [<c>:L147</c>] and is carried across inert. This test is what
    /// stops that line being quietly revived.
    /// </summary>
    /// <remarks>
    /// Asserted against the proxy because that is where the guards live, and because ONE OF THE TWO IS
    /// NOT REACHABLE FROM THE WIRE AT ALL while the other is. <c>common.v1.AnyValue</c> has no
    /// multi-dimensional arm, so the rank guard cannot be tripped by any request C-07 accepts - but its
    /// blob arm deserializes to a one-dimensional <see cref="byte"/> array, so an EMPTY BLOB trips the
    /// empty-array guard through <c>Exec</c> itself. That reachable half is covered separately by
    /// <see cref="AnEmptyBlobParameterTripsTheDelegatedEmptyArrayGuard"/>; this test pins both guards at
    /// the seam so neither can be removed as "unreachable".
    /// </remarks>
    [Fact]
    public async Task TheShapeGuardsRefuseRankTwoAndEmptyArraysWhileTheDormantNullCheckStaysDormant()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        SqlCommandTaskProxy proxy = Resolve(harness, handle).Proxy;

        // [:L148] a second dimension exists.
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, proxy.AddParam("p", new long[2, 2]));

        // [:L149] a one-based upper bound of zero is PowerBuilder's empty variable-size array.
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, proxy.AddParam("p", Array.Empty<long>()));

        // [:L147] DORMANT. A null value is ACCEPTED, both positionally and by name.
        Assert.Equal(RetCode.OK, proxy.AddParam((object?)null));
        Assert.Equal(RetCode.OK, proxy.AddParam("p", null));

        // A scalar and a one-dimensional array both reach neither guard.
        Assert.Equal(RetCode.OK, proxy.AddParam("p", 1L));
        Assert.Equal(RetCode.OK, proxy.AddParam("p", new long[] { 1L, 2L }));
    }

    /// <summary>
    /// THE EMPTY-ARRAY GUARD IS REACHABLE FROM THE WIRE, AND THE ROUTE IS NOT OBVIOUS: the blob arm of
    /// <c>common.v1.AnyValue</c> deserializes to a one-dimensional <see cref="byte"/> array, so an EMPTY
    /// blob is PowerBuilder's empty variable-size array and is refused with
    /// <c>E_INVALID_ARGUMENT</c> by the delegated guard
    /// [<c>n_cst_threading_task_sqlbase.sru:L149</c>]. A non-empty blob is accepted.
    /// </summary>
    [Fact]
    public async Task AnEmptyBlobParameterTripsTheDelegatedEmptyArrayGuard()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        ExecResponse refused = await harness.Commands.Exec(
            new ExecRequest
            {
                Task = handle,
                Sql = "UPDATE t SET a = :expected",
                Parameters =
                {
                    new PositionalParameter
                    {
                        Name = string.Empty,
                        Value = new AnyValue { BlobValue = Google.Protobuf.ByteString.Empty },
                    },
                },
            },
            Context);

        Assert.Equal(WireRetCode.EInvalidArgument, refused.Status.RetCode);
        Assert.Equal(0, harness.Engine.ExecuteCalls);

        // The guard is about EMPTINESS and not about the blob arm itself: a non-empty blob installs.
        ExecResponse accepted = await harness.Commands.Exec(
            new ExecRequest
            {
                Task = handle,
                Sql = "UPDATE t SET a = :expected",
                Parameters =
                {
                    new PositionalParameter
                    {
                        Name = string.Empty,
                        Value = new AnyValue
                        {
                            BlobValue = Google.Protobuf.ByteString.CopyFrom(1, 2, 3),
                        },
                    },
                },
            },
            Context);

        Assert.Equal(WireRetCode.Ok, accepted.Status.RetCode);
        Assert.Equal(1, harness.Engine.ExecuteCalls);
    }

    // ---------------------------------------------------------------------------------------------
    //  REGION 7 - E_BUSY ON EVERY MUTATOR WHILE A COMMAND IS IN FLIGHT
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The pooled transaction observes cancellation itself, after its argument guard and before any side
    /// effect.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>THIS IS THE SECOND OF TWO INDEPENDENT CANCELLATION GUARDS AND IT IS NOT
    /// REDUNDANT.</b> The task layer checks the token before it asks for a transaction, and the transaction
    /// checks again before it asks the provider - which mirrors the legacy's own habit of guarding one
    /// condition on both sides of a boundary. Driven directly here because no seam exists between the two
    /// checks through which a token could be cancelled, so the only way to reach the inner one on its own is
    /// to call the inner surface.
    /// </para>
    /// <para>
    /// THE ORDER OF THE TWO GUARDS IS ITSELF THE ASSERTION. An EMPTY statement answers
    /// <c>E_INVALID_ARGUMENT</c> even when the token is already cancelled, because a malformed request is
    /// malformed whether or not the caller is still there. A well-formed statement under a cancelled token
    /// answers <c>CANCELLED</c> and leaves NO trace - no execution, no state change - because a statement
    /// that is never issued must not look like one that was considered.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePooledTransactionObservesCancellationAfterItsArgumentGuardAndBeforeAnySideEffect()
    {
        Harness harness = new();

        // The pool's own descriptor type, not the wire message: this test exercises the transaction
        // surface directly rather than through the contract, so it speaks the surface's own vocabulary.
        TransactionData descriptor = new() { Dbms = "SQLITE", Database = "pfw-cancellation-guard" };

        int refIndex = harness.Pool.AddRef(descriptor);

        Assert.Equal(RetCode.OK, harness.Pool.Get(refIndex, out IPooledTransaction? transaction));
        Assert.NotNull(transaction);

        using CancellationTokenSource cancelled = new();

        cancelled.Cancel();

        // THE ARGUMENT GUARD WINS over cancellation, because it is checked first.
        Assert.Equal(
            RetCode.E_INVALID_ARGUMENT,
            transaction!.Exec(string.Empty, cancelled.Token));

        // A well-formed statement under a cancelled token is CANCELLED - which is neither succeeded nor
        // failed in the ported algebra - and the provider is never reached.
        Assert.Equal(RetCode.CANCELLED, transaction.Exec("DELETE FROM t", cancelled.Token));
        Assert.Equal(0, harness.Engine.ExecuteCalls);

        // And an uncancelled call still runs, so the guard is the token's doing and not a latch.
        Assert.Equal(RetCode.OK, transaction.Exec("DELETE FROM t", CancellationToken.None));
        Assert.Equal(1, harness.Engine.ExecuteCalls);

        _ = harness.Pool.RemoveRef(refIndex);
    }

    /// <summary>
    /// Every verb refuses with <c>E_BUSY</c> while another operation genuinely HOLDS the lease.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>THIS IS A DIFFERENT GUARD FROM THE ONE THE SIBLING TEST BELOW EXERCISES, AND BOTH
    /// ARE NEEDED.</b> That test raises the caller-side proxy's own busy flag, which refuses BEFORE the
    /// lease is ever reached [<c>n_cst_threading_task_sqlbase.sru:L57</c>]. This one holds the LEASE itself,
    /// which is the guard the oracle does not have and cannot need: its caller is one thread, so nothing
    /// there can interleave two requests against one task. Across an RPC boundary two requests can, and
    /// without the lease the second would interleave its statement, parameter and autocommit writes with the
    /// first request's execution - so the statement that runs belongs to one request and the values bound
    /// into it to the other.
    /// </para>
    /// <para>
    /// THE LEASE IS OBSERVED FROM INSIDE THE PROVIDER, which is the only place it is provably held: it is
    /// taken before the provider call and released after, so any assertion made outside that window would
    /// be racing the thing it is trying to measure.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task EveryVerbRefusesWithEBusyWhileAnotherOperationHoldsTheLease()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        using ManualResetEventSlim entered = new(false);
        using ManualResetEventSlim gate = new(false);

        harness.Engine.Entered = entered;
        harness.Engine.Gate = gate;

        Task<ExecResponse> inFlight = Task.Run(
            () => harness.Commands.Exec(
                new ExecRequest { Task = handle, Sql = "DELETE FROM t" },
                Context),
            TestContext.Current.CancellationToken);

        Assert.True(
            entered.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken),
            "The execution never reached the provider, so the lease was never held and this test would "
                + "have asserted nothing.");

        try
        {
            Assert.Equal(
                WireRetCode.EBusy,
                (await harness.Commands.Reset(
                    new ResetCommandTaskRequest { Task = handle },
                    Context)).Status.RetCode);

            Assert.Equal(
                WireRetCode.EBusy,
                (await harness.Commands.SetSql(
                    new SetCommandSqlRequest { Task = handle, Sql = "SELECT 1" },
                    Context)).Status.RetCode);

            Assert.Equal(
                WireRetCode.EBusy,
                (await harness.Commands.SetAutoCommit(
                    new SetCommandAutoCommitRequest { Task = handle, Autocommit = AutoCommitMode.AcOn },
                    Context)).Status.RetCode);

            Assert.Equal(
                WireRetCode.EBusy,
                (await harness.Commands.Exec(
                    new ExecRequest { Task = handle, Sql = "SELECT 2" },
                    Context)).Status.RetCode);

            // NOTHING THE REFUSED REQUESTS CARRIED WAS INSTALLED. A refusal that had already written the
            // statement would be worse than no guard at all, because the in-flight execution would then run
            // the OTHER request's statement.
            Assert.Equal(1, harness.Engine.ExecuteCalls);
            Assert.Equal("DELETE FROM t", harness.Engine.LastStatement);
        }
        finally
        {
            gate.Set();
        }

        ExecResponse completed = await inFlight;

        Assert.Equal(WireRetCode.Ok, completed.Status.RetCode);

        // AND THE LEASE IS RELEASED AFTERWARDS, so a task is usable again once the execution finishes -
        // a lease that leaked would make the task permanently busy, which is worse than not leasing.
        Assert.Equal(
            WireRetCode.Ok,
            (await harness.Commands.Reset(
                new ResetCommandTaskRequest { Task = handle },
                Context)).Status.RetCode);
    }

    /// <summary>
    /// The busy report is a DIAGNOSTIC read and tracks the lease.
    /// </summary>
    /// <remarks>
    /// It is read for reporting only and never to decide whether to proceed - deciding on it would be
    /// exactly the check-then-act the lease exists to remove, because the state can change between the read
    /// and the act. Asserted in both directions so it cannot silently become constant.
    /// </remarks>
    [Fact]
    public async Task TheBusyReportTracksTheLeaseAndIsClearOnceItIsReleased()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        using ManualResetEventSlim entered = new(false);
        using ManualResetEventSlim gate = new(false);

        harness.Engine.Entered = entered;
        harness.Engine.Gate = gate;

        Task<ExecResponse> inFlight = Task.Run(
            () => harness.Commands.Exec(
                new ExecRequest { Task = handle, Sql = "DELETE FROM t" },
                Context),
            TestContext.Current.CancellationToken);

        Assert.True(entered.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));

        try
        {
            Assert.True(harness.Tasks.TryResolve(handle, out CommandTask? held));
            Assert.True(held!.IsRunning);
        }
        finally
        {
            gate.Set();
        }

        _ = await inFlight;

        Assert.True(harness.Tasks.TryResolve(handle, out CommandTask? idle));
        Assert.False(idle!.IsRunning);
    }

    /// <summary>
    /// A release that races an in-flight operation defers disposal to the operation rather than pulling the
    /// task out from under it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>THE RELEASE MUST NOT DISPOSE A TASK AN OPERATION IS STILL USING</b>, and it must
    /// not simply refuse either, because a caller releasing a handle is entitled to have it gone. So the
    /// release retires the handle immediately and the LAST party out disposes: here that is the execution,
    /// which finds the task retired when it ends its operation.
    /// </para>
    /// <para>
    /// AFTERWARDS THE HANDLE IS GONE RATHER THAN BUSY. A retired task answers <c>E_INVALID_HANDLE</c> and
    /// not <c>E_BUSY</c> - the distinction matters because one tells a caller to retry and the other tells
    /// it never to use this handle again.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AReleaseThatRacesAnInFlightOperationDefersDisposalAndRetiresTheHandle()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        using ManualResetEventSlim entered = new(false);
        using ManualResetEventSlim gate = new(false);

        harness.Engine.Entered = entered;
        harness.Engine.Gate = gate;

        Task<ExecResponse> inFlight = Task.Run(
            () => harness.Commands.Exec(
                new ExecRequest { Task = handle, Sql = "DELETE FROM t" },
                Context),
            TestContext.Current.CancellationToken);

        Assert.True(entered.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));

        ReleaseCommandTaskResponse released;

        try
        {
            released = await harness.Commands.ReleaseCommandTask(
                new ReleaseCommandTaskRequest { Task = handle },
                Context);
        }
        finally
        {
            gate.Set();
        }

        Assert.Equal(WireRetCode.Ok, released.Status.RetCode);

        // The execution still completes against the task it was already holding.
        Assert.Equal(WireRetCode.Ok, (await inFlight).Status.RetCode);

        // GONE, NOT BUSY.
        Assert.Equal(
            WireRetCode.EInvalidHandle,
            (await harness.Commands.Reset(
                new ResetCommandTaskRequest { Task = handle },
                Context)).Status.RetCode);
    }

    /// <summary>
    /// Every mutator refuses with <c>E_BUSY</c> while a command is in flight for that task. The oracle
    /// opens every caller-side mutator with that guard
    /// [<c>n_cst_threading_task_sqlbase.sru:L57</c>, <c>:L73</c>, <c>:L95</c>, <c>:L110</c>,
    /// <c>:L124</c>, <c>:L144</c>, <c>:L162</c>, <c>:L179</c>, <c>:L188</c>], and the two command-specific
    /// setters add their own [<c>n_cst_threading_task_sqlcommand.sru:L34</c>, <c>:L43</c>], so a task
    /// cannot be reconfigured underneath a running statement.
    /// </summary>
    [Fact]
    public async Task EveryMutatorRefusesWithEBusyWhileACommandIsInFlight()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        harness.Factory.LastProxyHost!.IsRunning = true;

        Assert.Equal(
            WireRetCode.EBusy,
            (await harness.Commands.SetSql(
                new SetCommandSqlRequest { Task = handle, Sql = "DELETE FROM t" },
                Context)).Status.RetCode);

        Assert.Equal(
            WireRetCode.EBusy,
            (await harness.Commands.SetAutoCommit(
                new SetCommandAutoCommitRequest { Task = handle, Autocommit = AutoCommitMode.AcOn },
                Context)).Status.RetCode);

        Assert.Equal(
            WireRetCode.EBusy,
            (await harness.Commands.Reset(
                new ResetCommandTaskRequest { Task = handle },
                Context)).Status.RetCode);

        // Exec refuses at its first mutating step - the statement override - and again at the parameter
        // list, so neither a statement nor a value is installed while busy.
        Assert.Equal(
            WireRetCode.EBusy,
            (await harness.Commands.Exec(
                new ExecRequest { Task = handle, Sql = "DELETE FROM t" },
                Context)).Status.RetCode);

        Assert.Equal(
            WireRetCode.EBusy,
            (await harness.Commands.Exec(
                new ExecRequest { Task = handle, Parameters = { Positional(1L) } },
                Context)).Status.RetCode);

        Assert.Equal(0, harness.Engine.ExecuteCalls);
    }

    // ---------------------------------------------------------------------------------------------
    //  REGION 8 - THE REDACTION SITE
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// ⚠ A FAILING COMMAND'S DRIVER PAYLOAD REACHES THE WIRE WITH ITS STATEMENT REDACTED (constraint
    /// C-F). The oracle passes THE FULLY BOUND STATEMENT into the database-error event -
    /// <c>Event OnDBError(transObject.SQLDBCode, transObject.SQLErrText, sSQL, Primary!, 0)</c>
    /// [<c>:L110</c>] - and its logger redacts nothing at all. With bind variables disabled the runtime
    /// interpolates values as literals [<c>n_cst_thread_task_sqlbase.sru:L128</c>], so an unredacted
    /// field would emit whatever the failing row contained to a network peer.
    /// </summary>
    [Fact]
    public async Task AFailingCommandsPayloadReachesTheWireWithItsStatementRedacted()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        harness.Engine.ExecuteResult = SqlState.Failed(-1, "near \"FROM\": syntax error");

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest
            {
                Task = handle,
                Sql = "DELETE FROM ledger WHERE account = :account",
                Parameters = { Positional(90210L) },
            },
            Context);

        Assert.Equal(WireRetCode.EDbError, response.Status.RetCode);
        Assert.NotNull(response.Status.DbError);

        // THE INTERPOLATED VALUE IS GONE, AND THAT IS THE WHOLE POINT: it is caller data, the legacy
        // emitted it verbatim, and it must not reach a network peer through any field of the response.
        Assert.DoesNotContain("90210", response.Status.DbError.Sqlsyntax, StringComparison.Ordinal);
        Assert.DoesNotContain("90210", response.Status.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain("90210", response.SqlErrText, StringComparison.Ordinal);

        // In its place stands the REDACTOR'S OWN placeholder rather than a hand-rolled mask, which is
        // what makes the policy unweakenable from a call site. The redaction is LITERAL-SCOPED, not
        // wholesale: statement structure and identifiers survive so an operator can still see WHICH
        // statement failed and where a value stood, while the values themselves do not. That is the
        // "redacted or parameter-separated" shape the plan requires.
        Assert.Contains(RedactionPlaceholder, response.Status.DbError.Sqlsyntax, StringComparison.Ordinal);
        Assert.Equal(
            "DELETE FROM ledger WHERE account = " + RedactionPlaceholder,
            response.Status.DbError.Sqlsyntax);

        // THE DRIVER'S OWN TEXT IS MASKED TOO, ON ALL THREE FIELDS - and this particular message arrives
        // UNCHANGED, which is the point rather than a contradiction. The mask is LITERAL-SCOPED: string,
        // radix and numeric literals and comment bodies become placeholders and every other byte is copied
        // through, so a driver message that quotes no value is byte-identical after it. That is what keeps
        // the field a useful diagnostic while removing the row data SQLite echoes into constraint and type
        // failures - see AValueBearingDriverMessageIsMaskedOnEveryFieldThatCarriesIt below for the other
        // half of the claim.
        Assert.Equal("near \"FROM\": syntax error", response.Status.DbError.Sqlerrtext);
        Assert.Equal("near \"FROM\": syntax error", response.Status.ErrorText);
        Assert.Equal("near \"FROM\": syntax error", response.SqlErrText);

        // The command path always raises with the Primary buffer and row 0 [:L110].
        Assert.Equal(DwBuffer.Primary, response.Status.DbError.Buffer);
        Assert.Equal(0L, response.Status.DbError.Row);
        Assert.Equal(-1L, response.SqlCode);
    }

    /// <summary>
    /// ⚠ A DRIVER MESSAGE THAT QUOTES THE OFFENDING VALUE IS MASKED ON EVERY FIELD THAT CARRIES IT
    /// (constraint C-F).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE HALF OF THE REDACTION CLAIM THE STATEMENT FIELD ALONE COULD NOT ESTABLISH.</b> The message
    /// field invites being copied through verbatim, on the reasoning that it is opaque display text a
    /// consumer must not parse. Opacity describes how a consumer may READ a field; it says nothing about what the
    /// provider PUTS in it, and SQLite routinely puts the caller's own data there - a uniqueness violation
    /// names the duplicated column, a constraint or type failure quotes the offending value, a bad
    /// identifier echoes the text the caller sent. The legacy could publish that safely because it
    /// published nothing: it is a library, and the message never left the process.
    /// </para>
    /// <para>
    /// Three fields carry it on this one response - the payload's message, the status diagnostic and the
    /// response's own <c>sql_err_text</c> - so all three are asserted. A fix applied to one and not the
    /// others would leave the value reachable through whichever was missed.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AValueBearingDriverMessageIsMaskedOnEveryFieldThatCarriesIt()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        // Shaped like a real SQLite constraint diagnostic that quotes the offending row value.
        harness.Engine.ExecuteResult = SqlState.Failed(
            -1,
            "CHECK constraint failed: account = '90210-SECRET'");

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest
            {
                Task = handle,
                Sql = "DELETE FROM ledger WHERE account = :account",
                Parameters = { Positional(90210L) },
            },
            Context);

        Assert.Equal(WireRetCode.EDbError, response.Status.RetCode);
        Assert.NotNull(response.Status.DbError);

        // THE QUOTED VALUE IS GONE FROM ALL THREE FIELDS.
        Assert.DoesNotContain(
            "90210-SECRET", response.Status.DbError.Sqlerrtext, StringComparison.Ordinal);
        Assert.DoesNotContain("90210-SECRET", response.Status.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain("90210-SECRET", response.SqlErrText, StringComparison.Ordinal);

        // ...replaced by the redactor's own placeholder rather than a hand-rolled mask, which is what
        // makes the policy unweakenable from a call site.
        Assert.Contains(
            RedactionPlaceholder, response.Status.DbError.Sqlerrtext, StringComparison.Ordinal);
        Assert.Contains(RedactionPlaceholder, response.Status.ErrorText, StringComparison.Ordinal);
        Assert.Contains(RedactionPlaceholder, response.SqlErrText, StringComparison.Ordinal);

        // ...AND THE MESSAGE STILL READS AS ITSELF, which is what keeps it useful to an operator: the
        // condition, the column and even the LITERAL'S OWN DELIMITERS survive, so the message still shows
        // that a quoted value stood there. Only the value itself does not.
        Assert.Equal(
            "CHECK constraint failed: account = '" + RedactionPlaceholder + "'",
            response.Status.DbError.Sqlerrtext);

        // The numeric code is untouched - it carries no caller data and is what a consumer classifies on.
        Assert.Equal(-1L, response.SqlCode);
    }

    /// <summary>
    /// ⚠ NO STATEMENT AND NO PARAMETER VALUE EVER REACHES A DIAGNOSTIC FROM THIS ADAPTER (constraint
    /// C-F). The legacy logger redacted nothing at all, so this is asserted rather than assumed: with
    /// every level enabled and a failing command carrying an interpolated value, the recorded diagnostics
    /// carry the codes, the counts and the handle - and neither the statement's text nor the caller's
    /// value.
    /// </summary>
    [Fact]
    public async Task NoStatementAndNoParameterValueEverReachesADiagnostic()
    {
        CapturingLogger logger = new();
        Harness harness = new(logger);
        TaskHandle handle = await CreateTask(harness);

        harness.Engine.ExecuteResult = SqlState.Failed(-1, "constraint violated");

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest
            {
                Task = handle,
                Sql = "DELETE FROM ledger WHERE account = :account",
                Parameters = { Positional(90210L) },
            },
            Context);

        Assert.Equal(WireRetCode.EDbError, response.Status.RetCode);
        Assert.NotEmpty(logger.Records);

        foreach (string record in logger.Records)
        {
            Assert.DoesNotContain("90210", record, StringComparison.Ordinal);
            Assert.DoesNotContain("DELETE FROM", record, StringComparison.Ordinal);
            Assert.DoesNotContain(FakePassword, record, StringComparison.Ordinal);
        }

        // What the record DOES carry is the safe shape: the handle, the outcome and the counts.
        Assert.Contains(logger.Records, record => record.Contains("parameters 1", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------------------------------------
    //  REGION 9 - HANDLE LIFECYCLE AND THE UNKNOWN-HANDLE CODES
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// An unknown SESSION is <c>E_INVALID_TRANSACTION</c>, matching the code the oracle answers when
    /// asked to act on a transaction it cannot use.
    /// </summary>
    [Fact]
    public async Task CreateAgainstAnUnknownSessionAnswersEInvalidTransaction()
    {
        Harness harness = new();

        CreateCommandTaskResponse response = await harness.Commands.CreateCommandTask(
            new CreateCommandTaskRequest { Session = new SessionHandle { SessionId = "not-a-session" } },
            Context);

        Assert.Equal(WireRetCode.EInvalidTransaction, response.Status.RetCode);
        Assert.Null(response.Task);
    }

    /// <summary>
    /// A caller that does not own a command task can neither use it nor release it, and cannot tell either
    /// refusal from one naming a task this service never issued.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A HANDLE IS UNGUESSABLE, WHICH IS NOT THE SAME AS OWNER-BOUND.</b> Unguessable bounds DISCOVERY
    /// and not USE: a handle that escapes through a log record, a proxy trace or the caller's own bug is
    /// otherwise a bearer credential for the command behind it - and a command task EXECUTES STATEMENTS
    /// inside another caller's transaction, so a foreign use here writes, and a foreign release disposes a
    /// task its owner is mid-execution on (CWE-639, CWE-862, CWE-863).
    /// </para>
    /// <para>
    /// <b>THE SECOND CALLER IS PRODUCED BY RE-ATTRIBUTING THE ENTRY, AND THAT IS THE SAME COMPARISON.</b>
    /// This harness constructs the service directly, so there is no ambient request to read a principal
    /// from and every task is attributed to the unattributed sentinel. Ownership is decided by comparing
    /// the STORED principal against the RESOLVED caller, so moving the stored value away from the caller
    /// exercises the same code path a second caller's request would, without a host.
    /// <c>HandleOwnershipTests</c> drives the comparison from the other side by varying the ambient
    /// caller, and additionally proves the deployed host supplies one.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AForeignCallerCanNeitherUseNorReleaseAnotherCallersCommandTask()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);
        TaskHandle unknownHandle = new() { TaskId = "no-such-command-task" };

        Assert.True(harness.Tasks.TryResolve(handle, out CommandTask? task));
        Assert.NotNull(task);
        Assert.Equal(HandlePrincipalResolver.Unattributed, task.Principal);

        // Charged to somebody else from here on.
        task.Principal = "powerframework-another-caller";

        // --- USE: the registry refuses, and refuses as it does for an unknown handle. ---------------
        Assert.False(harness.Tasks.TryResolve(handle, out CommandTask? foreignResolve));
        Assert.Null(foreignResolve);
        Assert.False(harness.Tasks.TryResolve(unknownHandle, out CommandTask? unknownResolve));
        Assert.Null(unknownResolve);

        // --- USE, over the wire: a per-task mutator answers exactly as it does for an unknown task. --
        SetCommandSqlResponse foreignSet = await harness.Commands.SetSql(
            new SetCommandSqlRequest { Task = handle, Sql = "DELETE FROM COMPANY" },
            Context);

        SetCommandSqlResponse unknownSet = await harness.Commands.SetSql(
            new SetCommandSqlRequest { Task = unknownHandle, Sql = "DELETE FROM COMPANY" },
            Context);

        Assert.Equal(unknownSet.Status.RetCode, foreignSet.Status.RetCode);
        Assert.Equal(unknownSet.Status.ErrorText, foreignSet.Status.ErrorText);

        // --- RELEASE: refused the same way, and the task SURVIVES the attempt. ----------------------
        ReleaseCommandTaskResponse foreignRelease = await harness.Commands.ReleaseCommandTask(
            new ReleaseCommandTaskRequest { Task = handle },
            Context);

        ReleaseCommandTaskResponse unknownRelease = await harness.Commands.ReleaseCommandTask(
            new ReleaseCommandTaskRequest { Task = unknownHandle },
            Context);

        Assert.Equal(unknownRelease.Status.RetCode, foreignRelease.Status.RetCode);
        Assert.Equal(unknownRelease.Status.ErrorText, foreignRelease.Status.ErrorText);
        Assert.Equal(1, harness.Tasks.Count);

        // --- THE POSITIVE ARM, so a registry refusing everybody cannot pass this row. ---------------
        task.Principal = HandlePrincipalResolver.Unattributed;

        Assert.True(harness.Tasks.TryResolve(handle, out _));

        SetCommandSqlResponse mineSet = await harness.Commands.SetSql(
            new SetCommandSqlRequest { Task = handle, Sql = "DELETE FROM COMPANY" },
            Context);

        Assert.Equal(WireRetCode.Ok, mineSet.Status.RetCode);

        ReleaseCommandTaskResponse mineRelease = await harness.Commands.ReleaseCommandTask(
            new ReleaseCommandTaskRequest { Task = handle },
            Context);

        Assert.Equal(WireRetCode.Ok, mineRelease.Status.RetCode);
        Assert.Equal(0, harness.Tasks.Count);
    }

    /// <summary>
    /// A refusing factory is reported verbatim and nothing is registered, so a caller never receives a
    /// handle to a task that could not be built.
    /// </summary>
    [Fact]
    public async Task ARefusingFactoryIsReportedVerbatimAndNothingIsRegistered()
    {
        Harness harness = new();
        SessionHandle session = await OpenSession(harness);

        harness.Factory.RefusalCode = RetCode.E_INVALID_OBJECT;

        CreateCommandTaskResponse response = await harness.Commands.CreateCommandTask(
            new CreateCommandTaskRequest { Session = session },
            Context);

        Assert.Equal(WireRetCode.EInvalidObject, response.Status.RetCode);
        Assert.Null(response.Task);
    }

    /// <summary>
    /// A pair that cannot be CONFIGURED is released rather than registered, so a caller never receives a
    /// handle to a task whose every later call would fail for a reason it could not see - and the
    /// worker's pool reference is not pinned until process exit.
    /// </summary>
    [Fact]
    public async Task APairThatCannotBeConfiguredIsReleasedRatherThanRegistered()
    {
        Harness harness = new();
        SessionHandle session = await OpenSession(harness);

        harness.Factory.BusyOnCreate = true;

        CreateCommandTaskResponse response = await harness.Commands.CreateCommandTask(
            new CreateCommandTaskRequest { Session = session },
            Context);

        // The descriptor installer carries its own busy guard
        // [n_cst_threading_task_sqlbase.sru:L110], so a busy pair cannot be configured.
        Assert.Equal(WireRetCode.EBusy, response.Status.RetCode);
        Assert.Null(response.Task);
    }

    /// <summary>
    /// Releasing a task twice disposes exactly once. Disposal is idempotent, so a duplicate release is
    /// harmless rather than a double dispose of the borrowed commit signal.
    /// </summary>
    [Fact]
    public async Task DisposalIsIdempotent()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        CommandTask task = Resolve(harness, handle);

        task.Dispose();
        task.Dispose();

        // Still resolvable, because Dispose does not remove from the table - only the release RPC does,
        // and it is the removal that makes the disposal happen exactly once in the live path.
        Assert.True(harness.Tasks.TryResolve(handle, out CommandTask? resolved));
        Assert.Same(task, resolved);
    }

    /// <summary>
    /// Every method answers <c>E_INVALID_HANDLE</c> for a stale, blank or unknown task handle - one
    /// outcome rather than three, so a caller cannot learn which of its guesses was closer to a live
    /// handle.
    /// </summary>
    [Fact]
    public async Task EveryMethodAnswersEInvalidHandleForAnUnknownTask()
    {
        Harness harness = new();
        TaskHandle unknown = new() { TaskId = "not-a-task" };

        Assert.Equal(
            WireRetCode.EInvalidHandle,
            (await harness.Commands.Reset(
                new ResetCommandTaskRequest { Task = unknown }, Context)).Status.RetCode);

        Assert.Equal(
            WireRetCode.EInvalidHandle,
            (await harness.Commands.SetAutoCommit(
                new SetCommandAutoCommitRequest { Task = unknown }, Context)).Status.RetCode);

        Assert.Equal(
            WireRetCode.EInvalidHandle,
            (await harness.Commands.SetSql(
                new SetCommandSqlRequest { Task = unknown, Sql = "DELETE FROM t" }, Context)).Status.RetCode);

        Assert.Equal(
            WireRetCode.EInvalidHandle,
            (await harness.Commands.Exec(
                new ExecRequest { Task = unknown }, Context)).Status.RetCode);

        Assert.Equal(
            WireRetCode.EInvalidHandle,
            (await harness.Commands.ReleaseCommandTask(
                new ReleaseCommandTaskRequest { Task = unknown }, Context)).Status.RetCode);

        // A blank handle is the same outcome as an unknown one.
        Assert.Equal(
            WireRetCode.EInvalidHandle,
            (await harness.Commands.Exec(
                new ExecRequest { Task = new TaskHandle() }, Context)).Status.RetCode);
    }

    /// <summary>
    /// A handle is single-use once released: the second release loses, and exactly one disposal happens.
    /// </summary>
    [Fact]
    public async Task AHandleIsSingleUseOnceReleased()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        Assert.Equal(
            WireRetCode.Ok,
            (await harness.Commands.ReleaseCommandTask(
                new ReleaseCommandTaskRequest { Task = handle }, Context)).Status.RetCode);

        Assert.Equal(
            WireRetCode.EInvalidHandle,
            (await harness.Commands.ReleaseCommandTask(
                new ReleaseCommandTaskRequest { Task = handle }, Context)).Status.RetCode);

        Assert.False(harness.Tasks.TryResolve(handle, out CommandTask? resolved));
        Assert.Null(resolved);
    }

    /// <summary>
    /// Two tasks on one session receive DISTINCT handles and distinct proxy pairs, so neither can
    /// reconfigure the other.
    /// </summary>
    [Fact]
    public async Task TwoTasksOnOneSessionReceiveDistinctHandlesAndDistinctPairs()
    {
        Harness harness = new();
        SessionHandle session = await OpenSession(harness);

        CreateCommandTaskResponse first = await harness.Commands.CreateCommandTask(
            new CreateCommandTaskRequest { Session = session }, Context);
        CreateCommandTaskResponse second = await harness.Commands.CreateCommandTask(
            new CreateCommandTaskRequest { Session = session }, Context);

        Assert.Equal(WireRetCode.Ok, first.Status.RetCode);
        Assert.Equal(WireRetCode.Ok, second.Status.RetCode);
        Assert.NotEqual(first.Task.TaskId, second.Task.TaskId);

        CommandTask firstTask = Resolve(harness, first.Task);
        CommandTask secondTask = Resolve(harness, second.Task);

        Assert.NotSame(firstTask.Proxy, secondTask.Proxy);
        Assert.NotSame(firstTask.Worker, secondTask.Worker);

        // Both run against the SAME session object, and therefore against its ONE gate - which is what
        // serializes their executions against the single borrowed connection. The gate is compared
        // DIRECTLY as well: it is a TransactionGate - a class, because the streaming query has to hold
        // it across an await - and a reference comparison on it is the strongest available statement that
        // the two tasks are genuinely serialized rather than merely co-located on one session.
        Assert.Same(firstTask.Session, secondTask.Session);
        Assert.Same(firstTask.Session.Gate, secondTask.Session.Gate);
    }

    // ---------------------------------------------------------------------------------------------
    //  REGION 10 - THE SURFACE, THE CONSTRUCTOR, AND THE REVIEW INVARIANT
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The adapter overrides EXACTLY the six RPCs C-07 declares - none missing, none extra. A seventh
    /// override would mean a method the contract does not publish; a missing one would silently inherit
    /// the generated base's unimplemented answer.
    /// </summary>
    [Fact]
    public void TheAdapterOverridesExactlyTheSixDeclaredRpcs()
    {
        string[] overrides = typeof(CommandService)
            .GetMethods(
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(method => method.IsVirtual && method.GetBaseDefinition() != method)
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "CreateCommandTask",
                "Exec",
                "ReleaseCommandTask",
                "Reset",
                "SetAutoCommit",
                "SetSql",
            ],
            overrides);

        Assert.True(
            typeof(global::PowerFramework.Contracts.Persistence.V1.CommandService.CommandServiceBase)
                .IsAssignableFrom(typeof(CommandService)));
    }

    /// <summary>
    /// Every required collaborator arrives through the constructor and a missing one fails fast
    /// (constraints C-H, and the framework's own posture for a structural fault). The logger is optional
    /// so a unit test can build the adapter from behavioural collaborators alone.
    /// </summary>
    [Fact]
    public void TheConstructorRefusesAMissingCollaboratorAndTreatsTheLoggerAsOptional()
    {
        Harness harness = new();

        Assert.Throws<ArgumentNullException>(() =>
            new CommandService(null!, harness.Tasks, harness.Factory));
        Assert.Throws<ArgumentNullException>(() =>
            new CommandService(harness.Sessions, null!, harness.Factory));
        Assert.Throws<ArgumentNullException>(() =>
            new CommandService(harness.Sessions, harness.Tasks, null!));

        Assert.NotNull(new CommandService(harness.Sessions, harness.Tasks, harness.Factory));
    }

    /// <summary>
    /// A null request message is a structural transport fault rather than a domain outcome, and the
    /// oracle has no return code for it, so it fails fast on every method.
    /// </summary>
    [Fact]
    public async Task ANullRequestFailsFastOnEveryMethod()
    {
        Harness harness = new();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            harness.Commands.CreateCommandTask(null!, Context));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            harness.Commands.ReleaseCommandTask(null!, Context));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            harness.Commands.Reset(null!, Context));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            harness.Commands.SetAutoCommit(null!, Context));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            harness.Commands.SetSql(null!, Context));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            harness.Commands.Exec(null!, Context));
    }

    /// <summary>
    /// ⚠ THE REVIEW INVARIANT WITH NO COMPILE-TIME ENFORCEMENT ANYWHERE IN THE REPOSITORY.
    /// <c>PowerFramework.Contracts</c> declares zero <c>ProjectReference</c>, deliberately, so nothing
    /// checks that the generated <c>common.v1.RetCode.Value</c> enum and
    /// <c>shared/PowerFramework.Shared.Kernel/RetCode.cs</c> agree numerically - no compiler, no
    /// analyzer, no single-sided test. The Persistence projects are the first and only place both are
    /// referenced together, so THIS is where the value-for-value assertion belongs. A divergence would be
    /// a silent wire-compatibility defect.
    /// </summary>
    /// <remarks>
    /// Every code C-07 can answer is listed, plus the three aliases for zero and both spellings of the
    /// cancelled value, because the alias structure is as much a part of the correspondence as the
    /// numbers are.
    /// </remarks>
    [Fact]
    public void TheWireRetCodeEnumAgreesNumericallyWithTheSharedKernelAlgebra()
    {
        Assert.Equal(RetCode.OK, (long)WireRetCode.Ok);
        Assert.Equal(RetCode.SUCCESS, (long)WireRetCode.Success);
        Assert.Equal(RetCode.ALLOW, (long)WireRetCode.Allow);
        Assert.Equal(RetCode.PREVENT, (long)WireRetCode.Prevent);
        Assert.Equal(RetCode.FAILED, (long)WireRetCode.Failed);
        Assert.Equal(RetCode.CANCELED, (long)WireRetCode.Canceled);
        Assert.Equal(RetCode.CANCELLED, (long)WireRetCode.Cancelled);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, (long)WireRetCode.EInvalidArgument);
        Assert.Equal(RetCode.E_INVALID_OBJECT, (long)WireRetCode.EInvalidObject);
        Assert.Equal(RetCode.E_INVALID_TRANSACTION, (long)WireRetCode.EInvalidTransaction);
        Assert.Equal(RetCode.E_INVALID_SQL, (long)WireRetCode.EInvalidSql);
        Assert.Equal(RetCode.E_INVALID_HANDLE, (long)WireRetCode.EInvalidHandle);
        Assert.Equal(RetCode.E_OUT_OF_BOUND, (long)WireRetCode.EOutOfBound);
        Assert.Equal(RetCode.E_BUSY, (long)WireRetCode.EBusy);
        Assert.Equal(RetCode.E_DB_ERROR, (long)WireRetCode.EDbError);
        Assert.Equal(RetCode.E_SQL_BIND_ARG_FAILED, (long)WireRetCode.ESqlBindArgFailed);
    }

    /// <summary>
    /// The autocommit enumerators carry the oracle's own numbers [<c>:L16-L18</c>], and
    /// <c>AC_OFF</c> occupies ZERO - which is what makes an unset wire field mean the legacy default
    /// rather than a synthetic unspecified state.
    /// </summary>
    [Fact]
    public void TheAutoCommitEnumeratorsCarryTheOraclesOwnNumbers()
    {
        Assert.Equal(0, (int)AutoCommitMode.AcOff);
        Assert.Equal(1, (int)AutoCommitMode.AcOn);
        Assert.Equal(2, (int)AutoCommitMode.AcNative);
    }

    // ---------------------------------------------------------------------------------------------
    //  REGION 11 - THE SESSION LIFECYCLE: THE CLOSING MARK, THE PURGE, AND THE DISPOSAL HANDOFF
    // ---------------------------------------------------------------------------------------------
    //
    //  THREE SEPARATE PROPERTIES, AND EACH ONE FAILED BEFORE THIS REGION EXISTED.
    //
    //    1. A create that reaches the session's lifecycle gate after the closing mark is REFUSED, and the
    //       pair it had already built is torn down rather than leaked.
    //    2. EndSession marks the session closing BEFORE it walks this registry, so there is no instant at
    //       which a create can publish into a registry the purge has already passed.
    //    3. The purge goes through the release/disposal handoff, so a task with an operation in flight is
    //       NOT torn down underneath itself - destroying an object with work still pending against it is
    //       hazard 1 [docs/PB多线程绕坑提示.md], and here it would dispose the worker's commit signal from
    //       under a statement that is still executing.
    //
    //  Every case is deterministic: the closing mark is written explicitly under the gate exactly as
    //  EndSession writes it, the operation lease is taken explicitly on the entry exactly as Exec takes it,
    //  and property 2 is observed by injecting a create INSIDE the teardown the purge performs. Nothing
    //  here waits on a timer or races two threads.

    [Fact]
    public async Task ACreateThatReachesTheGateAfterTheClosingMarkIsRefusedAndTearsDownItsPair()
    {
        Harness harness = new();
        SessionHandle session = await OpenSession(harness);

        Assert.True(harness.Sessions.TryResolve(session, out TransactionSession? resolved));
        Assert.NotNull(resolved);

        using (resolved!.Gate.Enter())
        {
            resolved.MarkClosing();
        }

        CreateCommandTaskResponse refused = await harness.Commands.CreateCommandTask(
            new CreateCommandTaskRequest { Session = session },
            Context);

        // The code the oracle answers for a transaction it cannot use
        // [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L113].
        Assert.Equal(WireRetCode.EInvalidTransaction, refused.Status.RetCode);
        Assert.Null(refused.Task);
        Assert.Contains("began retiring", refused.Status.ErrorText, StringComparison.Ordinal);

        // NOTHING WAS PUBLISHED - the property that matters, because a handle issued here would name a task
        // the purge has already walked past.
        Assert.Equal(0, harness.Tasks.Count);

        // AND THE PAIR IT BUILT WAS TORN DOWN, which is the second half: the worker's disposal is what
        // returns its pool reference [n_cst_thread_task_sqlbase.sru:L160], so a refusal that forgot it would
        // pin a connection for the life of the process.
        Assert.NotNull(harness.Factory.LastTaskHost);
        Assert.Equal(1, harness.Factory.LastTaskHost!.Uninits);
    }

    [Fact]
    public async Task ALiveSessionStillPublishesSoTheRefusalIsNotUnconditional()
    {
        // THE OTHER HALF OF THE CLAIM ABOVE. Without it the liveness test could be a constant refusal and
        // that case would still pass.
        Harness harness = new();

        TaskHandle handle = await CreateTask(harness);

        Assert.NotEqual(string.Empty, handle.TaskId);
        Assert.Equal(1, harness.Tasks.Count);
        Assert.Equal(0, harness.Factory.LastTaskHost!.Uninits);
    }

    [Fact]
    public async Task TheClosingMarkPrecedesThePurgeSoNoCreateCanPublishIntoAWalkedRegistry()
    {
        // THE ORDERING CASE, AND THE ONLY WAY TO OBSERVE IT FROM OUTSIDE. The purge's teardown reaches the
        // worker-side host, so a create injected from there runs at the exact instant EndSession is walking
        // this registry. If the mark were written AFTER the walk - the obvious ordering -
        // the injected create would see a live session, publish a task, and that task would survive its own
        // session holding a transaction the pool has taken back.
        Harness harness = new();
        SessionHandle session = await OpenSession(harness);

        CreateCommandTaskResponse first = await harness.Commands.CreateCommandTask(
            new CreateCommandTaskRequest { Session = session },
            Context);

        Assert.Equal(WireRetCode.Ok, first.Status.RetCode);

        FakeTaskHost host = Assert.IsType<FakeTaskHost>(harness.Factory.LastTaskHost);

        CreateCommandTaskResponse? duringPurge = null;

        host.OnUninitAction = () => duringPurge = harness.Commands
            .CreateCommandTask(new CreateCommandTaskRequest { Session = session }, Context)
            .GetAwaiter()
            .GetResult();

        EndSessionResponse ended = await harness.Transactions.EndSession(
            new EndSessionRequest { Session = session },
            Context);

        Assert.Equal(WireRetCode.Ok, ended.Status.RetCode);

        // The injection actually ran - otherwise the assertion below would pass vacuously.
        Assert.NotNull(duringPurge);
        Assert.Equal(WireRetCode.EInvalidTransaction, duringPurge!.Status.RetCode);
        Assert.Null(duringPurge.Task);

        // AND THE REGISTRY IS EMPTY AFTERWARDS. This is the assertion the ordering exists for: the purge
        // walked, the injected create refused, and no later arrival is left behind for a second walk to
        // catch.
        Assert.Equal(0, harness.Tasks.Count);
    }

    [Fact]
    public async Task APurgeDoesNotTearDownATaskWithAnOperationInFlight()
    {
        Harness harness = new();
        SessionHandle session = await OpenSession(harness);

        CreateCommandTaskResponse created = await harness.Commands.CreateCommandTask(
            new CreateCommandTaskRequest { Session = session },
            Context);

        Assert.Equal(WireRetCode.Ok, created.Status.RetCode);

        CommandTask task = Resolve(harness, created.Task);
        FakeTaskHost host = Assert.IsType<FakeTaskHost>(harness.Factory.LastTaskHost);

        // THE LEASE Exec ITSELF TAKES, taken directly so the case is deterministic. There is no seam inside
        // the production worker's execution to pause at, and TaskOperationLatchTests already exercises every
        // interleaving of the state machine; what this case adds is that the PURGE respects it.
        Assert.Equal(TaskLatchOutcome.Acquired, task.TryBeginOperation());

        EndSessionResponse ended = await harness.Transactions.EndSession(
            new EndSessionRequest { Session = session },
            Context);

        Assert.Equal(WireRetCode.Ok, ended.Status.RetCode);

        // RETIRED FROM THE TABLE BUT NOT TORN DOWN. The handle is unreachable, so no new call can arrive,
        // and the pair the in-flight operation is still using is intact.
        Assert.Equal(0, harness.Tasks.Count);
        Assert.Equal(0, host.Uninits);

        // THE HANDOFF'S OTHER SIDE: the operation's own exit is told that disposal is now its duty, and it
        // performs it. Exactly one of the two paths disposes, and it is this one.
        Assert.True(task.EndOperation());

        task.Dispose();

        Assert.Equal(1, host.Uninits);
    }

    [Fact]
    public async Task APurgeTearsDownATaskWithNothingInFlight()
    {
        // THE COMPLEMENT, WITHOUT WHICH THE HANDOFF COULD SIMPLY NEVER DISPOSE. A task with no operation in
        // flight is the releaser's duty, and the purge performs it there and then.
        Harness harness = new();
        SessionHandle session = await OpenSession(harness);

        CreateCommandTaskResponse created = await harness.Commands.CreateCommandTask(
            new CreateCommandTaskRequest { Session = session },
            Context);

        Assert.Equal(WireRetCode.Ok, created.Status.RetCode);

        FakeTaskHost host = Assert.IsType<FakeTaskHost>(harness.Factory.LastTaskHost);

        Assert.Equal(0, host.Uninits);

        EndSessionResponse ended = await harness.Transactions.EndSession(
            new EndSessionRequest { Session = session },
            Context);

        Assert.Equal(WireRetCode.Ok, ended.Status.RetCode);
        Assert.Equal(0, harness.Tasks.Count);
        Assert.Equal(1, host.Uninits);
    }

    // ---------------------------------------------------------------------------------------------
    //  REGION 12 - THE LEADING-`@` EXECUTION MODE (AAP §0.4.3 C-07)
    // ---------------------------------------------------------------------------------------------
    //  WHAT THESE CASES ARE PROTECTING
    //  The AAP requires C-07's `Exec` to preserve "positional `?` binding, the leading-`@` prefix
    //  execution mode, multi-statement batch execution, and error-text retrieval". Three of those
    //  four were implemented; the mode was not, and the published contract said two contradictory
    //  things about it at once - the request field documented the mode while the RPC comment declared
    //  the statement passed verbatim. The observable consequence was the worst of the three possible
    //  outcomes: a caller who sent the documented form got a provider syntax error, because "@INSERT
    //  INTO ..." is not valid SQL in any dialect.
    //
    //  The legacy demonstration is one statement with five `?` placeholders, executed ten times in a
    //  loop, under a comment recording that the prefix caches the statement to speed up re-parsing
    //  [ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L396-L406]. So the cases below pin four
    //  separate facts, because getting any one of them wrong reintroduces the defect in a new shape:
    //  the selector is REMOVED from what the provider receives; the mode is CARRIED to the engine;
    //  the removal is exactly ONE character; and the installed statement keeps the selector so the
    //  task stays re-runnable in the same mode.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// THE DEFECT THIS RULES OUT, IN ONE CASE. A prefixed statement reaches the provider WITHOUT its
    /// selector, and the mode reaches the engine instead - where a naive split sends the selector to the
    /// provider as statement text and the mode nowhere.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The statement is the oracle's own shape [<c>w_test_sqlite.srw:L398-L400</c>] - a prefixed insert
    /// with placeholders and positional values - reduced to two columns. BOTH renderings are asserted:
    /// the canonical form is what a provider binds against and the rendered form is what a
    /// characterization recording compares, so a selector surviving into either one would report a
    /// statement that was never executed.
    /// </para>
    /// <para>
    /// The placeholders are spelled <c>:name</c> rather than <c>?</c> because that is THIS binder's
    /// marker - the ported scan reads <c>ARG_PREFIX = ":"</c>
    /// [<c>n_cst_thread_task_sqlbase.sru:L396, :L440</c>], and the <c>?</c> in the demonstration belongs
    /// to the native binding's own argument list. "Positional" is carried by the EMPTY NAME, which the
    /// binder reads as "the next unreplaced placeholder" - REGION 6 pins that separately. Combining the
    /// mode with real binding is the point of this case, because the binder composes a FRESH statement
    /// and a mode set before it runs is the one most easily lost.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task APrefixedStatementReachesTheProviderWithoutItsSelectorAndCarriesTheMode()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest
            {
                Task = handle,
                Sql = "@INSERT INTO COMPANY (NAME,AGE) VALUES (:name, :age)",
                Parameters = { Positional(23L), Positional(25L) },
            },
            Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
        Assert.Equal(1, harness.Engine.ExecuteCalls);

        // The provider's text - placeholders bound - carries no selector.
        Assert.Equal(
            "INSERT INTO COMPANY (NAME,AGE) VALUES (@p1, @p2)",
            harness.Engine.LastCanonicalStatement);

        // The parity text - literals interpolated - carries no selector either.
        Assert.Equal("INSERT INTO COMPANY (NAME,AGE) VALUES (23, 25)", harness.Engine.LastStatement);

        // And the mode the selector chose did reach the engine, having survived the binder.
        Assert.True(harness.Engine.LastCacheStatement);
        Assert.Equal([23L, 25L], harness.Engine.LastParameters);
    }

    /// <summary>
    /// THE COMPLEMENT, WITHOUT WHICH THE ARM ABOVE COULD BE UNCONDITIONAL. An ordinary statement binds
    /// identically and carries no mode.
    /// </summary>
    [Fact]
    public async Task AnUnprefixedStatementBindsIdenticallyAndCarriesNoMode()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest
            {
                Task = handle,
                Sql = "INSERT INTO COMPANY (NAME,AGE) VALUES (:name, :age)",
                Parameters = { Positional(23L), Positional(25L) },
            },
            Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
        Assert.Equal(
            "INSERT INTO COMPANY (NAME,AGE) VALUES (@p1, @p2)",
            harness.Engine.LastCanonicalStatement);
        Assert.Equal("INSERT INTO COMPANY (NAME,AGE) VALUES (23, 25)", harness.Engine.LastStatement);
        Assert.False(harness.Engine.LastCacheStatement);
    }

    /// <summary>
    /// EXACTLY ONE CHARACTER IS REMOVED, which is the oracle's own <c>Mid(sql,2)</c> arithmetic
    /// [<c>n_cst_thread_trans.sru:L310</c>]. A doubled selector is therefore NOT an escape sequence: the
    /// second one survives into the statement and the provider is the layer that rejects it.
    /// </summary>
    [Fact]
    public async Task ADoubledSelectorIsNotAnEscapeSequenceAndTheSecondOneReachesTheProvider()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest { Task = handle, Sql = "@@INSERT INTO COMPANY (NAME) VALUES ('x')" },
            Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
        Assert.Equal("@INSERT INTO COMPANY (NAME) VALUES ('x')", harness.Engine.LastCanonicalStatement);
        Assert.True(harness.Engine.LastCacheStatement);
    }

    /// <summary>
    /// A LONE SELECTOR IS ANSWERED BY THE TRANSACTION AND NOT BY A NEW GUARD. <c>"@"</c> is not the empty
    /// string, so it passes both emptiness guards [<c>:L45</c>, <c>:L65</c>] exactly as the oracle's
    /// would; the empty remainder is then refused by the transaction's own first line with
    /// <c>E_INVALID_ARGUMENT</c> [<c>n_cst_thread_trans.sru:L219</c>].
    /// </summary>
    /// <remarks>
    /// ⚠ THE CODE IS <c>E_INVALID_ARGUMENT</c> AND NOT <c>E_INVALID_SQL</c>, and the difference is the
    /// point of the case: short-circuiting the empty remainder inside the task would answer the
    /// statement-shaped code, when the layer that actually owns the question answers the argument-shaped
    /// one. That is the same route a null statement takes, and it is preserved rather than harmonized
    /// (constraint C-B).
    /// </remarks>
    [Fact]
    public async Task ALoneSelectorIsRefusedByTheTransactionRatherThanByANewGuard()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest { Task = handle, Sql = "@" },
            Context);

        Assert.Equal(WireRetCode.EInvalidArgument, response.Status.RetCode);

        // The transaction refuses before the engine is reached [n_cst_thread_trans.sru:L219 precedes
        // :L229], so no statement was issued.
        Assert.Equal(0, harness.Engine.ExecuteCalls);
    }

    /// <summary>
    /// THE INSTALLED STATEMENT KEEPS ITS SELECTOR, so a task re-run stays in the same mode. The mode is
    /// derived from the statement text on every execution rather than latched at installation, which is
    /// why there is no second source of truth for it to drift from.
    /// </summary>
    [Fact]
    public async Task TheInstalledStatementKeepsItsSelectorSoARerunStaysInTheSameMode()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        const string Prefixed = "@INSERT INTO COMPANY (NAME) VALUES ('x')";

        SetCommandSqlResponse installed = await harness.Commands.SetSql(
            new SetCommandSqlRequest { Task = handle, Sql = Prefixed },
            null!);

        Assert.Equal(WireRetCode.Ok, installed.Status.RetCode);

        CommandTask task = Resolve(harness, handle);

        // Stored verbatim - the setter is a faithful port of `_sSQL = sql` [:L47] and interprets nothing.
        Assert.Equal(Prefixed, task.Worker.Sql);
        Assert.True(task.Worker.StatementSelectsCaching);

        // Two executions of the one installed statement, which is the shape of the oracle's loop.
        for (int iteration = 0; iteration < 2; iteration++)
        {
            ExecResponse response = await harness.Commands.Exec(
                new ExecRequest { Task = handle },
                Context);

            Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
            Assert.Equal("INSERT INTO COMPANY (NAME) VALUES ('x')", harness.Engine.LastCanonicalStatement);
            Assert.True(harness.Engine.LastCacheStatement);
        }

        Assert.Equal(2, harness.Engine.ExecuteCalls);
        Assert.Equal(Prefixed, task.Worker.Sql);
    }

    /// <summary>
    /// THE SELECTOR MUST BE THE FIRST CHARACTER, WITH NO WHITESPACE TOLERANCE. The oracle's sibling test
    /// is <c>Left(sql,1) = "@"</c> [<c>n_cst_thread_trans.sru:L309</c>] - position one, exactly - so
    /// trimming first would accept a form the legacy rejects, which is a widening rather than a
    /// preservation (constraint C-B).
    /// </summary>
    [Fact]
    public async Task ASelectorBehindWhitespaceIsNotASelector()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest { Task = handle, Sql = " @INSERT INTO COMPANY (NAME) VALUES ('x')" },
            Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);

        // Forwarded whole, leading space and selector alike, because neither is this layer's business.
        Assert.Equal(" @INSERT INTO COMPANY (NAME) VALUES ('x')", harness.Engine.LastCanonicalStatement);
        Assert.False(harness.Engine.LastCacheStatement);
    }

    /// <summary>
    /// THE MODE TRAVELS ON A BATCH TOO, because both are carried in the one statement string and neither
    /// this layer nor the transaction splits or counts statements.
    /// </summary>
    [Fact]
    public async Task ThePrefixedFormIsCompatibleWithABatchBecauseBothTravelInOneString()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        const string Batch =
            "INSERT INTO COMPANY (NAME) VALUES ('Teddy');INSERT INTO COMPANY (NAME) VALUES ('Mark')";

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest { Task = handle, Sql = '@' + Batch },
            Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
        Assert.Equal(Batch, harness.Engine.LastCanonicalStatement);
        Assert.True(harness.Engine.LastCacheStatement);
    }

    /// <summary>
    /// A FAILING PREFIXED STATEMENT REPORTS THE STATEMENT THE PROVIDER RAN, NOT THE ONE THE CALLER
    /// TYPED - so the selector is absent from the error payload's statement field as well.
    /// </summary>
    /// <remarks>
    /// This is the case that fixes the ORDER of the strip and the bind. Removing the selector after
    /// binding would leave it sitting in front of an interpolated statement, and the payload would then
    /// name a statement no execution ever issued. The field is redacted on its way to the wire, so the
    /// assertion is made on the raised payload, which is where the unmasked text exists.
    /// </remarks>
    [Fact]
    public async Task AFailingPrefixedStatementReportsTheStatementTheProviderRan()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        harness.Engine.ExecuteResult = SqlState.Failed(RetCode.SQLITE_CONSTRAINT, "constraint failed");

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest { Task = handle, Sql = "@INSERT INTO COMPANY (NAME) VALUES ('x')" },
            Context);

        Assert.Equal(WireRetCode.EDbError, response.Status.RetCode);

        CommandTask task = Resolve(harness, handle);
        DbErrorData raised = task.Proxy.GetLastDbErrorData();

        Assert.NotEqual(DbErrorData.Empty, raised);
        Assert.Equal("INSERT INTO COMPANY (NAME) VALUES ('x')", raised.SqlSyntax);
        Assert.DoesNotContain("@", raised.SqlSyntax, StringComparison.Ordinal);
    }

    /// <summary>
    /// THE DECISION IS A PURE FUNCTION OF THE STATEMENT, so the two halves of it can be pinned without a
    /// transaction, an engine or a task at all.
    /// </summary>
    /// <param name="statement">The statement.</param>
    /// <param name="selects">Whether it selects the mode.</param>
    [Theory]
    [InlineData("@INSERT INTO COMPANY (NAME) VALUES ('x')", true)]
    [InlineData("@", true)]
    [InlineData("@@SELECT 1", true)]
    [InlineData("INSERT INTO COMPANY (NAME) VALUES ('x')", false)]
    [InlineData(" @SELECT 1", false)]
    [InlineData("SELECT '@' FROM COMPANY", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TheModeDecisionIsAPureFunctionOfTheStatementText(string? statement, bool selects) =>
        Assert.Equal(selects, SqlCommandTask.SelectsStatementCaching(statement));

    /// <summary>
    /// AND THE REMOVAL IS EXACTLY ONE CHARACTER, including in the degenerate case where nothing is left.
    /// </summary>
    /// <param name="statement">A statement that selects the mode.</param>
    /// <param name="executable">What the provider is asked to run.</param>
    [Theory]
    [InlineData("@SELECT 1", "SELECT 1")]
    [InlineData("@@SELECT 1", "@SELECT 1")]
    [InlineData("@", "")]
    [InlineData("@ SELECT 1", " SELECT 1")]
    public void TheSelectorRemovalTakesExactlyOneCharacter(string statement, string executable) =>
        Assert.Equal(executable, SqlCommandTask.StripStatementCachingPrefix(statement));

    /// <summary>A server call context whose only interesting member is its cancellation token.</summary>
    /// <param name="cancellationToken">The token the handlers will observe.</param>
    private sealed class CallContext(CancellationToken cancellationToken) : ServerCallContext
    {
        private readonly CancellationToken _token = cancellationToken;

        protected override string MethodCore => "/persistence.v1.CommandService/Exec";

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
}
