// ==================================================================================================
//  CommandAndTransactionServiceTests.cs
//  THE TWO THIN ADAPTERS, PINNED TOGETHER: C-07 persistence.v1.CommandService OVER SqlCommandTask,
//  AND C-08 persistence.v1.TransactionService OVER TransactionPool.
//  ------------------------------------------------------------------------------------------------
//  SUBJECTS
//      services/persistence-service/PowerFramework.Persistence/Grpc/CommandService.cs        (C-07)
//      services/persistence-service/PowerFramework.Persistence/Grpc/TransactionService.cs    (C-08)
//      services/persistence-service/PowerFramework.Persistence/Tasks/SqlCommandTask.cs
//
//  WHY ONE FILE FOR TWO CONTRACTS. Both are ADAPTERS over machinery that is covered on its own
//  elsewhere - the task substrate by SqlTaskBaseTests, the proxy pair by SqlCommandTaskProxyTests,
//  the pool mechanics by TransactionPoolTests and TransactionPoolIdleSweeperTests, the descriptor
//  type by TransactionDataTests, the masking scanner by SqlRedactorTests. What is NOT covered by any
//  of those, and is therefore what this file exists for, is the behaviour that only appears WHERE THE
//  TWO MEET:
//
//      the tri-valued auto-commit epilogue, all three arms, by OBSERVED EFFECT      [:L88-L112]
//      AC_NATIVE notifying INSTEAD of committing                                    [:L97]
//      AC_NATIVE's failure arm performing NO ROLLBACK OF ITS OWN                    [:L105-L107]
//      the flag toggled true before the statement and false after it, BOTH paths    [:L89], [:L96], [:L106]
//      the two setters' PRESERVED VALIDATION ASYMMETRY                              [:L40-L43] vs [:L45-L49]
//      E_BUSY on every mutator while a command is in flight                         [n_cst_threading_task_sqlcommand.sru:L34], [:L43]
//      SQLCode = 100 READING AS SUCCESS                                             [n_cst_thread_trans.sru:L233]
//      the bound statement reaching the payload and the WIRE MASKING IT             [:L110] + Errors/SqlRedactor.cs
//      the four delegated capabilities belonging to the TRANSACTION contract        [:L81-L86], [:L92], [n_cst_thread_trans.sru:L309]
//      a TWELFTH argument ACCEPTED - the ceilings are recorded, never enforced      [n_sqlite.sru:L33-L43]
//      the ONE-BASED pool reference index behind the session handle                 [n_cst_thread_trans_pool.sru:L136-L151]
//      index ZERO refused as an ERROR rather than as "the first entry"              [n_cst_thread_trans_pool.sru:L89], [:L120], [:L154]
//      the nine-field descriptor in the oracle's declaration order                  [transactiondata.srs:L4-L12]
//      LogPass INBOUND-ONLY, proven by grepping every captured payload and record   [transactiondata.srs:L8]
//      the verbatim Chinese diagnostics, neither routed through localization        [:L66], [:L83]
//      CANCELLED answered SILENTLY, and being NEITHER succeeded nor failed          [:L76-L78] + issucceeded.srf, isfailed.srf
//
//  CONSTRAINTS THIS FILE IS WRITTEN AGAINST (AAP 0.7.3), each named where it is discharged:
//
//      C-A  The generated contracts project is the ONLY cross-service coupling. Nothing below
//           references another service's assembly, and a case at the end asserts that by reflection.
//      C-B  Replicate verbatim. Every quirk above is asserted as EXPECTED, is commented, and carries
//           its oracle locator. Nothing here asserts a corrected behaviour.
//      C-D  No coupling to any deferred service. The oracle's variadic escape hatch belongs to
//           ScriptBridge; a case at the end asserts no such type is referenced from Persistence.
//      C-E  No fabricated database. The engine seam is a RECORDING DOUBLE, so no connection is
//           opened, no dialect client is constructed and no container is started - while the pool,
//           the pooled transaction, the task and both adapters are the REAL types.
//      C-F  No credential literal appears in this file. The descriptor's log password is an
//           obviously fake sentinel that cannot match any provider's key format, and it is asserted
//           ABSENT from every response, every log record, every ToString() and every error payload.
//      C-G  Authentication is never bypassed and no signing key is constructed. The handlers are
//           invoked directly, which is a unit-test call and not a pipeline call; a case at the end
//           asserts the [Authorize] policy metadata Program.cs relies on is still present.
//      C-H  Coverage. The three-mode epilogue and the session correlation are the bulk of it.
//      C-K  The legacy arity ceilings are RECORDED LIMITS, never requirements, and the reason the
//           oracle has them at all is stated where they are asserted.
//      0.6.7  Determinism. One injected clock, no wall-clock read, NO SLEEP anywhere.
//      0.8.5  No performance objective is asserted, because the repository publishes none.
//      0.7.2  Warnings-as-errors and nullable are inherited. No SCREAMING_SNAKE identifier is
//           declared here; the AC_* and DBT_* values are reached through the GENERATED enum.
// ==================================================================================================

using System.Globalization;
using System.Reflection;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PowerFramework.Persistence.Authorization;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Grpc;
using PowerFramework.Persistence.Tasks;
using PowerFramework.Persistence.Tasks.TaskProxies;
using PowerFramework.Persistence.Transactions;
using PowerFramework.Shared.Diagnostics;

// PowerFramework.Contracts.Persistence.V1 is a global using and it contains GENERATED static classes
// also called CommandService and TransactionService. These aliases make every bare name below
// unambiguous and name the types under test explicitly.
using CommandService = PowerFramework.Persistence.Grpc.CommandService;
using TransactionService = PowerFramework.Persistence.Grpc.TransactionService;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Parity and contract tests for the C-07 command adapter and the C-08 transaction adapter, plus the
/// worker task the first of the two drives.
/// </summary>
public sealed class CommandAndTransactionServiceTests
{
    // ---------------------------------------------------------------------------------------------
    //  FIXTURE CONSTANTS
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The fixture's log-password value: an obviously fake, deliberately conspicuous sentinel.
    /// </summary>
    /// <remarks>
    /// <b>CHOSEN SO THAT A GREP CAN PROVE ABSENCE, AND SO THAT IT CANNOT BE MISTAKEN FOR A SECRET
    /// (constraint C-F).</b> It is not a credential and it cannot match any provider's key format: it is
    /// lower-case hyphenated English that says what it is for, so it matches no vendor key prefix, no
    /// encoded key block and no token grammar. It also contains a run of characters that appears nowhere
    /// else in the repository, which is what makes "this string appears in NO captured output" a
    /// meaningful assertion rather than a lucky one.
    /// </remarks>
    private const string SentinelLogPass = "sentinel-logpass-must-never-be-echoed";

    /// <summary>
    /// The mask the sanctioned projection substitutes for a literal.
    /// </summary>
    /// <remarks>
    /// Read from the production type rather than duplicated, so a change of placeholder cannot leave
    /// this suite asserting a stale string.
    /// </remarks>
    private const string RedactionPlaceholder = SqlRedactor.DefaultPlaceholder;

    /// <summary>
    /// A literal that must survive into the in-process payload and must NOT survive onto the wire.
    /// </summary>
    /// <remarks>
    /// Row data rather than a credential: the point of the redaction site is that ROW VALUES leak
    /// through an unredacted statement, and using a fake secret here would confuse two different
    /// exposures.
    /// </remarks>
    private const string BoundLiteral = "Kenneth-Fixture-Row";

    /// <summary>
    /// The call context every handler in this suite is invoked with.
    /// </summary>
    /// <remarks>
    /// A REAL context rather than <c>null!</c>: both adapters validate the context and thread
    /// <c>context.CancellationToken</c> into the provider call, so a null would trip the argument
    /// guard before the behaviour under test is reached. One shared instance is safe because it is
    /// immutable in every respect this suite observes.
    /// </remarks>
    private static ServerCallContext Context { get; } = new CallContext(CancellationToken.None);

    // ---------------------------------------------------------------------------------------------
    //  DOUBLES - C-E: THIS IS THE ONLY LAYER THAT IS FAKED. EVERYTHING ABOVE IT IS THE REAL TYPE.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The database-verb seam: it records what was asked of it and answers a scripted outcome.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>WHY A RECORDING ENGINE RATHER THAN A MOCKED TRANSACTION.</b> The three epilogues differ only
    /// in WHICH VERBS THEY ISSUE and IN WHAT ORDER, so the assertions have to be able to count commits
    /// and rollbacks and to read the ordered history of writes to the auto-commit flag. Faking one
    /// layer higher - at <see cref="IPooledTransaction"/> - would also fake the
    /// <c>SQLCode &lt;&gt; 0 and SQLCode &lt;&gt; 100</c> carve-out
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L233</c>], which is one of the
    /// behaviours under test. Faking here keeps <see cref="PooledTransaction"/>, the pool, the task
    /// and both adapters REAL.
    /// </para>
    /// <para>
    /// <b>NO DATABASE IS INVOLVED, WHICH IS THE POINT (constraint C-E).</b> Nothing here opens a
    /// connection, nothing constructs a <c>SqliteConnectionFactory</c>, and nothing needs a container.
    /// </para>
    /// </remarks>
    private sealed class RecordingEngine : ITransactionEngine
    {
        private bool _autoCommit;

        /// <summary>The connection handle a successful connect publishes.</summary>
        internal int Handle { get; set; }

        /// <summary>The DBMS name, which is also the dialect selector.</summary>
        internal string DbmsValue { get; set; } = "SQLite";

        internal SqlState ConnectResult { get; set; } = SqlState.Succeeded();

        internal SqlState CommitResult { get; set; } = SqlState.Succeeded();

        internal SqlState RollbackResult { get; set; } = SqlState.Succeeded();

        internal SqlState ExecuteResult { get; set; } = SqlState.Succeeded(1);

        internal int CommitCalls { get; private set; }

        internal int RollbackCalls { get; private set; }

        internal int ExecuteCalls { get; private set; }

        /// <summary>The rendered - interpolated - text of the most recent execution.</summary>
        internal string LastRenderedStatement { get; private set; } = string.Empty;

        /// <summary>The canonical - placeholder-carrying - text of the most recent execution.</summary>
        internal string LastCanonicalStatement { get; private set; } = string.Empty;

        /// <summary>The bound values of the most recent execution, in positional order.</summary>
        internal IReadOnlyList<object?> LastParameters { get; private set; } = [];

        /// <summary>
        /// Whether the most recent execution asked for the prepared-form mode - the port of the
        /// leading-<c>@</c> execution mode C-07 preserves.
        /// </summary>
        internal bool LastCacheStatement { get; private set; }

        /// <summary>
        /// Every write to the auto-commit flag, in order.
        /// </summary>
        /// <remarks>
        /// <b>THE ONLY WAY THE AC_NATIVE INVERSION IS OBSERVABLE.</b> The expected history for a native
        /// execution is <c>[true, false]</c> - raised before the statement [<c>:L89</c>] and restored
        /// after it on BOTH the success path [<c>:L96</c>] and the failure path [<c>:L106</c>].
        /// </remarks>
        internal List<bool> AutoCommitWrites { get; } = [];

        /// <summary>
        /// The credential the connection was handed, read through the descriptor's own internal
        /// reveal door.
        /// </summary>
        /// <remarks>
        /// <b>THE SECOND HALF OF THE WRITE-ONLY PROOF (constraint C-F).</b> Proving the password never
        /// leaves is only half a contract; the other half is that the CONNECTION STILL RECEIVES IT.
        /// This is the only member in the whole suite that reads it, and it reads it through
        /// <c>RevealLogPassForConnect()</c> - the internal, purpose-named door - because
        /// <c>TransactionData.LogPass</c> has no getter at all.
        /// </remarks>
        internal string AppliedLogPass { get; private set; } = string.Empty;

        /// <summary>The DBParm string the connection was handed.</summary>
        internal string AppliedDbParm { get; private set; } = string.Empty;

        /// <summary>How many times the connection fields were applied.</summary>
        internal int ApplyCalls { get; private set; }

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

        public void ApplyConnectionFields(in TransactionData descriptor)
        {
            ApplyCalls++;
            DbmsValue = descriptor.Dbms;
            AppliedDbParm = descriptor.DbParm;

            // The reveal door, not a property read - see AppliedLogPass.
            AppliedLogPass = descriptor.RevealLogPassForConnect();
        }

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
            LastRenderedStatement = sqlCommand;
            LastCanonicalStatement = sqlCommand;
            LastParameters = [];
            LastCacheStatement = false;

            return ExecuteResult;
        }

        public SqlState Execute(in SqlCommandText command, CancellationToken cancellationToken = default)
        {
            ExecuteCalls++;
            LastRenderedStatement = command.RenderedText;
            LastCanonicalStatement = command.CanonicalText;
            LastParameters = [.. command.Parameters];
            LastCacheStatement = command.CacheStatement;

            return ExecuteResult;
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// The retrieval surface C-08's adapter requires as a collaborator. Never exercised here.
    /// </summary>
    /// <remarks>
    /// C-07 retrieves nothing and C-08's session verbs retrieve nothing, so every member that would
    /// imply a retrieval throws rather than answering a plausible value: a test that reached one would
    /// be testing C-05, which has its own suite.
    /// </remarks>
    private sealed class UnusedQuerySurface : IQueryTransactionSurface
    {
        public GridSyntaxOutcome GridSyntaxFromSql(IPooledTransaction transaction, string sql) =>
            new("table(column=(x))", string.Empty);

        public ValueTask<CountQueryOutcome> Query(
            IPooledTransaction transaction,
            string sql,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Retrieval is C-05's contract, not C-07's or C-08's.");

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
    /// A carrier factory that must never be reached.
    /// </summary>
    /// <remarks>
    /// A command produces no result set. The base task requires the seam only because the oracle's own
    /// inheritance puts the carrier machinery on <c>n_cst_thread_task_sqlbase</c> and a command task
    /// inherits it UNUSED rather than opting out of it - so the faithful double is one that proves it
    /// stays unused.
    /// </remarks>
    private sealed class UnreachableDataStoreFactory : ISqlDataStoreFactory
    {
        public ISqlDataStore Create(CarrierThreadAffinity affinity) =>
            throw new NotSupportedException("A command task creates no result carrier.");
    }

    /// <summary>
    /// The worker-side threading substrate.
    /// </summary>
    /// <remarks>
    /// <see cref="ParentTasking"/> is what carries the database-error event back to the caller-side
    /// sink, and <see cref="GetTask"/> is what lets the committed notification reach an already-created
    /// signal - <c>OnCommitted</c> walks the host's task indexes and signals each task's handle
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L100-L111</c>], so a host
    /// that cannot produce the worker would silence every committed notification.
    /// </remarks>
    private sealed class FakeTaskHost : ISqlTaskHost
    {
        internal SqlTaskBase? Worker { get; set; }

        internal ISqlTaskProxy? Proxy { get; set; }

        internal bool Cancelled { get; set; }

        /// <summary>Every general-error event the task raised, in order, with its code and text.</summary>
        internal List<(long Code, string Info)> Errors { get; } = [];

        /// <summary>
        /// Runs inside the task body, at the moment the general-error event is raised. Inert when unset.
        /// </summary>
        /// <remarks>
        /// <b>THE ONLY WINDOW ONTO STATE THAT EXISTS ONLY MID-EXECUTION.</b> The worker's running flag is
        /// raised for the duration of its body and lowered in a finally clause, so it is
        /// FALSE both before and after every call - which means a guard written against it cannot be
        /// observed from outside at all. The error event fires from INSIDE the body, so a hook here is a
        /// legitimate seam onto that window rather than a contrivance.
        /// </remarks>
        internal Action? OnErrorAction { get; set; }

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
        }

        public long OnError(long errCode, string errInfo)
        {
            Errors.Add((errCode, errInfo));

            OnErrorAction?.Invoke();

            return RetCode.OK;
        }

        public long OnNotify(long notifyCode, long payload, string text) => RetCode.OK;
    }

    /// <summary>
    /// The caller-side threading substrate, whose two flags are what <c>of_IsBusy()</c> reads.
    /// </summary>
    /// <remarks>
    /// <b>THE BUSY PREDICATE IS NOT ONE FLAG.</b> It answers the controller flag while the worker is
    /// stopped and the INVERSE of the synchronization signal while the worker is running
    /// [<c>Tasks/TaskProxies/SqlTaskProxyBase.cs</c>, the port of
    /// <c>n_cst_threading_task.sru</c>'s own predicate]. Both flags are settable here so the busy
    /// matrix can drive either arm without a thread and without a sleep (AAP 0.6.7).
    /// </remarks>
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

        public string TaskClassName => SqlCommandTaskProxy.LegacyWorkerClassName;

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
    /// Builds the real proxy pair over the two fake substrates, and hands the most recent pair back so
    /// a case can drive the busy flags or read the worker's private-equivalent seams.
    /// </summary>
    private sealed class RecordingCommandTaskFactory : ICommandTaskFactory
    {
        internal RecordingCommandTaskFactory(TransactionPool pool, TimeProvider clock, List<string> log)
        {
            Pool = pool;
            Clock = clock;
            Log = log;
        }

        private TransactionPool Pool { get; }

        private TimeProvider Clock { get; }

        private List<string> Log { get; }

        internal FakeTaskHost? LastTaskHost { get; private set; }

        internal FakeProxyHost? LastProxyHost { get; private set; }

        public long Create(out CommandTaskComponents? components)
        {
            FakeTaskHost taskHost = new();

            SqlCommandTask worker = new(
                taskHost,
                Pool,
                new UnreachableDataStoreFactory(),
                new SqlRetrievalHookActivator(),
                Clock,
                new CapturingLogger<SqlCommandTask>(Log));

            taskHost.Worker = worker;

            FakeProxyHost proxyHost = new() { Worker = worker };

            SqlCommandTaskProxy proxy = new(
                proxyHost,
                new CapturingLogger<SqlCommandTaskProxy>(Log),
                Clock);

            // THE SEAM'S ORDERING OBLIGATION. Initialization borrows the worker's commit signal, and the
            // committed notification signals only an ALREADY CREATED handle
            // [n_cst_thread_task_sqlbase.sru:L100-L111]. Without this line every ExecResponse.committed
            // would read false even on a real commit.
            Assert.Equal(RetCode.OK, proxy.Initialize());

            // The worker reaches the caller-side database-error sink through its substrate.
            taskHost.Proxy = proxy;

            LastTaskHost = taskHost;
            LastProxyHost = proxyHost;

            components = new CommandTaskComponents(proxy, worker);
            return RetCode.OK;
        }
    }

    /// <summary>
    /// A logger that appends every formatted record to a SHARED sink.
    /// </summary>
    /// <typeparam name="TCategory">The category, so one implementation serves every collaborator.</typeparam>
    /// <param name="sink">The shared record list.</param>
    /// <remarks>
    /// <b>ONE SINK FOR EVERY COLLABORATOR, AND THAT IS WHAT MAKES THE C-F GREP MEANINGFUL.</b> The
    /// credential could in principle escape through any of six loggers - the two adapters, the two
    /// registries, the worker task and its proxy - so all six write HERE, and the assertion then greps
    /// the whole sink rather than inspecting one field of one message.
    /// <see cref="IsEnabled"/> answers true for every level on purpose: a level-gated record that is
    /// never formatted is a record this suite would fail to inspect.
    /// </remarks>
    private sealed class CapturingLogger<TCategory>(List<string> sink) : ILogger<TCategory>
    {
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

            sink.Add(formatter(state, exception));
        }
    }

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

    /// <summary>
    /// The real pool, the real registries and both real adapters over one recording engine and one
    /// injected clock.
    /// </summary>
    /// <remarks>
    /// <b>THE COMMAND TASK TABLE IS SHARED WITH THE TRANSACTION ADAPTER DELIBERATELY.</b> C-08's
    /// <c>EndSession</c> purges the command tasks bound to the session it is closing, so the two
    /// adapters must be handed the SAME table for that behaviour to be reachable at all - which is
    /// exactly the "the RPC drives the lifecycle" property this file is asked to pin.
    /// </remarks>
    private sealed class Harness
    {
        internal Harness()
        {
            Clock = new FakeTimeProvider();
            Engine = new RecordingEngine();

            IOptions<PersistenceOptions> options = PersistenceOptionsBuilder.DefaultOptions();

            Pool = new TransactionPool(
                options,
                Clock,
                new PooledTransactionActivator(() => Engine, Clock));

            // Named arguments: both registries take an optional principal resolver AHEAD of the logger,
            // and the default resolver is the one production uses when no caller identity is attributed.
            Sessions = new TransactionSessionRegistry(
                options,
                Clock,
                Pool,
                logger: new CapturingLogger<TransactionSessionRegistry>(Log));

            Tasks = new CommandTaskRegistry(
                options,
                Clock,
                logger: new CapturingLogger<CommandTaskRegistry>(Log));

            Factory = new RecordingCommandTaskFactory(Pool, Clock, Log);

            Transactions = new TransactionService(
                Pool,
                Sessions,
                new UnusedQuerySurface(),
                new QueryTaskRegistry(options, Clock),
                new UpdateTaskRegistry(options, Clock),
                Tasks,
                new CapturingLogger<TransactionService>(Log));

            Commands = new CommandService(
                Sessions,
                Tasks,
                Factory,
                new CapturingLogger<CommandService>(Log));
        }

        /// <summary>The single clock seam. Nothing in this suite reads any other (AAP 0.6.7).</summary>
        internal FakeTimeProvider Clock { get; }

        internal RecordingEngine Engine { get; }

        internal TransactionPool Pool { get; }

        internal TransactionSessionRegistry Sessions { get; }

        internal CommandTaskRegistry Tasks { get; }

        internal RecordingCommandTaskFactory Factory { get; }

        internal TransactionService Transactions { get; }

        internal CommandService Commands { get; }

        /// <summary>Every log record every collaborator emitted, in order.</summary>
        internal List<string> Log { get; } = [];
    }

    // ---------------------------------------------------------------------------------------------
    //  FIXTURE HELPERS
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The nine-field descriptor, populated in the oracle's own declaration order
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/transactiondata.srs:L4-L12</c>] with a distinguishable
    /// value in every slot.
    /// </summary>
    /// <remarks>
    /// Every value is distinct so a mis-ordered mapping cannot pass by coincidence, and every value is
    /// obviously synthetic. The password slot carries <see cref="SentinelLogPass"/>, which is not a
    /// credential (constraint C-F).
    /// </remarks>
    private static TransactionDescriptor Descriptor() => new()
    {
        // 1 - dbms [:L4], also the dialect selector
        Dbms = "SQLite",

        // 2 - servername [:L5]
        Servername = "fixture-server",

        // 3 - database [:L6]
        Database = "test.db",

        // 4 - logid [:L7] - an ACCOUNT NAME, not a secret, so the response view legitimately carries it
        Logid = "fixture-account",

        // 5 - logpass [:L8] - WRITE-ONLY. Asserted absent from every outbound artefact below.
        Logpass = SentinelLogPass,

        // 6 - dbparm [:L9] - carries the two connection flags the oracle extracts by regular expression
        //     [n_cst_thread_task_sqlbase.sru:L128-L129]. DisableBind=1 means the runtime uses NO BIND
        //     VARIABLES, which is the mechanical root of the injection exposure (AAP 0.6.4).
        Dbparm = "DisableBind=1,NCharBind=1",

        // 7 - lock [:L10] - the isolation-level STRING, not a synchronization object
        Lock = "RU",

        // 8 - autocommit [:L11] - EIGHTH, not last, and boolean
        Autocommit = false,

        // 9 - userparm [:L12] - NINTH and last
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

    private static async Task<TaskHandle> CreateTask(Harness harness, SessionHandle session)
    {
        CreateCommandTaskResponse response = await harness.Commands.CreateCommandTask(
            new CreateCommandTaskRequest { Session = session },
            Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
        Assert.NotNull(response.Task);

        return response.Task;
    }

    private static async Task<TaskHandle> CreateTask(Harness harness) =>
        await CreateTask(harness, await OpenSession(harness));

    private static CommandTask Resolve(Harness harness, TaskHandle handle)
    {
        Assert.True(harness.Tasks.TryResolve(handle, out CommandTask? task));
        Assert.NotNull(task);
        return task;
    }

    /// <summary>
    /// A POSITIONAL parameter, which the oracle models as the EMPTY-NAMED case rather than as a
    /// separate kind [<c>n_cst_threading_task_sqlbase.sru:L138</c>].
    /// </summary>
    private static PositionalParameter Positional(string value) =>
        new() { Name = string.Empty, Value = new AnyValue { StringValue = value } };

    private static PositionalParameter Positional(long value) =>
        new() { Name = string.Empty, Value = new AnyValue { Int64Value = value } };

    // ---------------------------------------------------------------------------------------------
    //  THEORY DATA - the three modes, the four SQL-code readings, and the mutator busy matrix
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The three declared auto-commit modes with the commit and rollback counts each one produces on a
    /// SUCCEEDING statement.
    /// </summary>
    /// <remarks>
    /// <b>THE VALUES COME FROM THE SINGLE GENERATED ENUM (constraint 0.7.2).</b> Nothing in this file
    /// declares an <c>AC_OFF</c>, <c>AC_ON</c> or <c>AC_NATIVE</c> of its own - which is the managed
    /// equivalent of the oracle's caller-side aliasing, where the proxy's constants are literally
    /// initialised FROM the task's [<c>n_cst_threading_task_sqlcommand.sru:L19-L21</c>].
    /// </remarks>
    public static TheoryData<AutoCommitMode, int, int, bool> SucceedingModeCases =>
        new()
        {
            // mode, engine commits, engine rollbacks, committed notification observed
            //
            // AC_OFF: the success epilogue is an if / else-if with NO ELSE [:L94-L103], so nothing at
            // all happens - no commit, no rollback, no notification. The caller's session owns the
            // transaction and decides when the work becomes durable.
            { AutoCommitMode.AcOff, 0, 0, false },

            // AC_ON: `rtCode = of_Commit(true)` [:L99] - the BASE task's commit path, which commits
            // through the transaction and raises the committed notification on success
            // [n_cst_thread_task_sqlbase.sru:L235-L237].
            { AutoCommitMode.AcOn, 1, 0, true },

            // AC_NATIVE: the notification fires INSTEAD of a commit [:L97]. Zero commits is the
            // assertion that matters here, and it is the one an "obvious" implementation gets wrong.
            { AutoCommitMode.AcNative, 0, 0, true },
        };

    /// <summary>
    /// The four provider-code readings, and whether each reads as a framework success.
    /// </summary>
    /// <remarks>
    /// <b>THE CARVE-OUT IS <c>SQLCode &lt;&gt; 0 AND SQLCode &lt;&gt; 100</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L233</c>].</b> So 0 is success, ONE
    /// HUNDRED IS ALSO SUCCESS, and anything else is a database error - including a positive code, which
    /// is why 19 is in the table beside -1: a reading written as "negative means failure" would pass on
    /// -1 and silently accept 19 as a success.
    /// </remarks>
    public static TheoryData<long, bool> SqlCodeReadings =>
        new()
        {
            { 0L, true },      // the ordinary success
            { 100L, true },    // ⚠ NOT FOUND, AND EXPLICITLY NOT A FAILURE
            { -1L, false },    // the ordinary failure
            { 19L, false },    // a NON-ZERO, NON-100, POSITIVE code - a constraint violation
        };

    // ---------------------------------------------------------------------------------------------
    //  REGION 1 - C-07: THE TWO SETTERS AND THEIR PRESERVED VALIDATION ASYMMETRY
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The statement setter refuses the EMPTY string with <see cref="RetCode.E_INVALID_SQL"/>.
    /// </summary>
    /// <remarks>
    /// The oracle is one line: <c>if sql = "" then return RetCode.E_INVALID_SQL</c> [<c>:L45</c>], and
    /// the code is -8 [<c>ws_objects/pfw.shared.pbl.src/retcode.sru:L51</c>]. Driven against the WORKER
    /// rather than the proxy because the caller-side object carries the oracle's own
    /// <c>#IF DEFINED DEBUG THEN Assert(Len(sql) &gt; 0)</c>
    /// [<c>n_cst_threading_task_sqlcommand.sru:L36-L38</c>], which is preserved and which trips FIRST in
    /// a Debug build - so a proxy-level case would assert a different thing in each configuration.
    /// </remarks>
    [Fact]
    public async Task TheStatementSetterRefusesTheEmptyStringWithInvalidSql()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        Assert.Equal(RetCode.E_INVALID_SQL, Resolve(harness, handle).Worker.SetSql(string.Empty));
        Assert.Equal(-8L, RetCode.E_INVALID_SQL);
    }

    /// <summary>
    /// A WHITESPACE-ONLY statement is ACCEPTED by the ported setter and stored VERBATIM.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THIS IS THE ABSENCE OF A CORRECTION, AND IT IS ASSERTED SO THE CORRECTION CANNOT BE ADDED
    /// LATER (constraint C-B).</b> The oracle compares against the empty string ONLY [<c>:L45</c>]. It
    /// does not trim, and it has no concept of a blank statement, so a single space passes its guard and
    /// is installed. Teaching the setter to trim-and-reject would be a legacy defect corrected, which
    /// the plan forbids.
    /// </para>
    /// <para>
    /// The verbatim storage half matters just as much: a setter that accepted <c>" "</c> but stored
    /// <c>""</c> would satisfy the return-code half of this test while changing what the next
    /// <c>Exec</c> does.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("   ")]
    public async Task TheStatementSetterAcceptsAWhitespaceOnlyStatementVerbatim(string statement)
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);
        SqlCommandTask worker = Resolve(harness, handle).Worker;

        Assert.Equal(RetCode.OK, worker.SetSql(statement));

        // Verbatim - not trimmed, not normalised, not emptied.
        Assert.Equal(statement, worker.Sql);
    }

    /// <summary>
    /// The WIRE boundary adds NOTHING to the setter's rule: a blank statement is accepted and installed,
    /// and only the empty string is refused - by the worker's own message-free guard.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>A BOUNDARY BLANKNESS REFUSAL IS THE TEMPTING ADDITION HERE, AND THIS ROW IS THE
    /// REGRESSION GUARD FOR ITS ABSENCE (constraint C-B, AAP G2).</b> It would answer <c>E_INVALID_SQL</c>
    /// with its own diagnostic for a whitespace-only statement, on the reasoning that submitting one
    /// produces a success that changed no rows and that an actionable refusal serves a caller better. That
    /// is a judgement about the legacy's DESIGN rather than a statement about its BEHAVIOUR, and the
    /// oracle's guard compares against the empty string only [<c>:L45</c>, <c>:L65-L68</c>]. A port that
    /// refuses input its oracle accepts has changed behaviour just as much as one that accepts input its
    /// oracle refuses, so the boundary stays silent on blankness and the whole rule is the setter's.
    /// </para>
    /// <para>
    /// THE PAIR IS ASSERTED TOGETHER BECAUSE THE TWO CASES LOOK ALIKE AND DIFFER. Blank is accepted and
    /// INSTALLED - so the next <c>Exec</c> submits it - while empty is refused; nothing is trimmed on
    /// either path, and nothing is rewritten.
    /// </para>
    /// <para>
    /// <b>THE EMPTY-STRING HALF IS SPLIT BY BUILD, and the split is the port's intent rather than a
    /// defect.</b> The empty string passes the boundary and reaches <c>SqlCommandTaskProxy.SetSql</c>, which
    /// reproduces the oracle's own <c>#IF DEFINED DEBUG</c> assertion verbatim
    /// [<c>n_cst_threading_task_sqlcommand.sru:L36-L38</c>], so an <see cref="AssertionFailure"/> leaves
    /// the handler before the worker's message-free guard is ever reached in a Debug build. Both arms are
    /// asserted because both are shipped - the container image is built <c>-c Release</c> while the
    /// documented per-service gate's bare <c>dotnet test</c> builds Debug - which is the same treatment
    /// <see cref="CommandServiceTests.SetSqlStillAnswersTheWorkersMessageFreeRefusalForTheEmptyStatement"/>
    /// already gives the identical condition.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheWireAcceptsABlankStatementAndRefusesOnlyTheEmptyString()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        SetCommandSqlResponse blank = await harness.Commands.SetSql(
            new SetCommandSqlRequest { Task = handle, Sql = "   " },
            Context);

        Assert.Equal(WireRetCode.Ok, blank.Status.RetCode);
        Assert.Empty(blank.Status.ErrorText);

        // INSTALLED, verbatim: the boundary forwarded it and the worker stored what arrived.
        Assert.Equal("   ", Resolve(harness, handle).Worker.Sql);

#if DEBUG
        AssertionFailure failure = await Assert.ThrowsAsync<AssertionFailure>(
            () => harness.Commands.SetSql(
                new SetCommandSqlRequest { Task = handle, Sql = string.Empty },
                Context));

        // The oracle's own message text, verbatim - what a Debug build reports in place of the code.
        Assert.Contains("Len(sql) <= 0", failure.Message, StringComparison.Ordinal);
#else
        SetCommandSqlResponse empty = await harness.Commands.SetSql(
            new SetCommandSqlRequest { Task = handle, Sql = string.Empty },
            Context);

        Assert.Equal(WireRetCode.EInvalidSql, empty.Status.RetCode);

        // The oracle's setter raises no message at all, and that silence is preserved.
        Assert.Empty(empty.Status.ErrorText);
#endif

        // AND THE REFUSED CALL CHANGED NOTHING: the blank statement installed above is still installed,
        // which is what makes the empty-string arm a refusal rather than a reset.
        Assert.Equal("   ", Resolve(harness, handle).Worker.Sql);
    }

    /// <summary>
    /// The auto-commit setter VALIDATES NOTHING and always succeeds - including for a value outside the
    /// three declared enumerators.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE ASYMMETRY BETWEEN THE TWO SETTERS IS PRESERVED LEGACY BEHAVIOUR (constraint C-B).</b> One
    /// of them guards its argument [<c>:L45</c>] and the other does not [<c>:L40-L43</c>]: the oracle's
    /// whole body is <c>_nAutoCommit = autoCommit</c> followed by <c>return RetCode.OK</c>. There is no
    /// domain check, no clamp and no diagnostic. <b>DO NOT ADD VALIDATION HERE TO MAKE THE PAIR
    /// CONSISTENT</b> - the inconsistency is the behaviour, and the task body then treats every
    /// unrecognised value as the <c>AC_OFF</c> arm [<c>:L107-L109</c>].
    /// </para>
    /// <para>
    /// A proto3 enum is an OPEN <see cref="int"/> in C#, so <c>(AutoCommitMode)7</c> is a value that can
    /// genuinely arrive - which is what makes this case reachable rather than theoretical.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(-1)]
    public async Task TheAutoCommitSetterValidatesNothingAndAlwaysSucceeds(int mode)
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);
        SqlCommandTask worker = Resolve(harness, handle).Worker;

        Assert.Equal(RetCode.OK, worker.SetAutoCommit((AutoCommitMode)mode));

        // Stored EXACTLY as sent, undeclared values included.
        Assert.Equal((AutoCommitMode)mode, worker.AutoCommitSetting);
    }

    /// <summary>
    /// The reset restores auto-commit to <c>AC_OFF</c> and the statement to empty, and chains to the
    /// base.
    /// </summary>
    /// <remarks>
    /// The oracle is four lines - the busy guard, <c>_nAutoCommit = AC_OFF</c> [<c>:L34</c>],
    /// <c>_sSQL = ""</c> [<c>:L35</c>] and <c>return super::of_Reset()</c> [<c>:L37</c>]. The chain is
    /// observed rather than assumed: the base's reset clears the PARAMETER collection, so a reset that
    /// cleared only this object's two fields would leave parameters installed and the next statement
    /// would bind values the caller never re-sent.
    /// </remarks>
    [Fact]
    public async Task TheResetRestoresTheDefaultModeTheEmptyStatementAndChainsToTheBase()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);
        SqlCommandTask worker = Resolve(harness, handle).Worker;

        Assert.Equal(RetCode.OK, worker.SetAutoCommit(AutoCommitMode.AcNative));
        Assert.Equal(RetCode.OK, worker.SetSql("DELETE FROM COMPANY"));
        Assert.Equal(RetCode.OK, worker.AddParam(string.Empty, 1L));
        Assert.True(worker.HasParams());

        Assert.Equal(RetCode.OK, worker.Reset());

        Assert.Equal(AutoCommitMode.AcOff, worker.AutoCommitSetting);
        Assert.Equal(string.Empty, worker.Sql);

        // The chain: the parameter collection belongs to the BASE and is cleared by super::of_Reset().
        Assert.False(worker.HasParams());
    }

    /// <summary>
    /// The reset carries its OWN guard on the worker's running flag, independently of the caller-side
    /// busy predicate.
    /// </summary>
    /// <remarks>
    /// <b>THE ONE GUARD IN THIS OBJECT THAT IS LIVE (constraint C-B).</b> <c>of_reset</c> opens with
    /// <c>if #Running then return RetCode.E_BUSY</c> [<c>:L32</c>], uncommented - unlike every
    /// <c>#Running</c> guard in the BASE object, all four of which are commented out in the oracle
    /// [<c>n_cst_thread_task_sqlbase.sru:L113, :L253, :L343, :L361</c>] and are carried across inert.
    /// The flag is raised for the duration of the task body, so a reset attempted from inside the
    /// execution - which is reachable through the retrieval hooks - is refused rather than half-applying.
    /// </remarks>
    [Fact]
    public async Task TheResetIsRefusedWhileTheWorkerIsInsideItsTaskBody()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);
        SqlCommandTask worker = Resolve(harness, handle).Worker;
        FakeTaskHost host = harness.Factory.LastTaskHost!;

        // Idle: the guard is open.
        Assert.False(worker.IsRunning);
        Assert.Equal(RetCode.OK, worker.Reset());

        // FROM INSIDE THE BODY. The error event fires while the flag is still raised, which is the only
        // window in which the guard is observable at all - see FakeTaskHost.OnErrorAction.
        bool observedRunning = false;
        long resetFromInside = RetCode.OK;

        host.OnErrorAction = () =>
        {
            observedRunning = worker.IsRunning;
            resetFromInside = worker.Reset();
        };

        // Executing with no statement raises the error event and returns through the first arm
        // [:L65-L68], so the hook above runs mid-body.
        Assert.Equal(RetCode.E_INVALID_SQL, worker.OnDoTask(TestContext.Current.CancellationToken));

        Assert.True(observedRunning);
        Assert.Equal(RetCode.E_BUSY, resetFromInside);

        // And the flag is lowered on EVERY exit, including that early return, because it is lowered in a
        // finally clause - so a task that returned through a guard is not left permanently un-resettable.
        Assert.False(worker.IsRunning);
        Assert.Equal(RetCode.OK, worker.Reset());
    }

    /// <summary>
    /// EVERY mutator answers <see cref="RetCode.E_BUSY"/> while a command is in flight, and the SAME
    /// mutators succeed when the task is idle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE GUARD IS THE CALLER-SIDE OBJECT'S, WHICH IS WHERE THE ORACLE PUTS IT.</b> Both command
    /// mutators open with <c>if of_IsBusy() then return RetCode.E_BUSY</c>
    /// [<c>n_cst_threading_task_sqlcommand.sru:L34</c>, <c>:L43</c>], and the inherited parameter and
    /// reset mutators do the same on the base
    /// [<c>n_cst_threading_task_sqlbase.sru:L57</c>, <c>:L162</c>]. The worker-side objects carry no such
    /// guard on their setters at all - which is why this matrix is driven through the proxy.
    /// </para>
    /// <para>
    /// Enumerated as a theory rather than written out five times so that a mutator added later without a
    /// guard is a visible omission from a named list.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("SetSql")]
    [InlineData("SetAutoCommit")]
    [InlineData("Reset")]
    [InlineData("AddParam")]
    [InlineData("ResetParams")]
    public async Task EveryMutatorIsRefusedWhileBusyAndSucceedsWhenIdle(string mutator)
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);
        CommandTask task = Resolve(harness, handle);
        SqlCommandTaskProxy proxy = task.Proxy;

        FakeProxyHost host = harness.Factory.LastProxyHost!;

        // Idle first, so the theory proves a REFUSAL rather than a permanently broken mutator.
        Assert.False(proxy.IsBusy());
        Assert.Equal(RetCode.OK, Invoke(proxy, mutator));

        // Busy: the worker is running and its synchronization signal is clear, which is the arm of the
        // busy predicate that means "the worker is mid-operation".
        host.IsRunning = true;
        host.IsSyncSignalSet = false;
        Assert.True(proxy.IsBusy());

        Assert.Equal(RetCode.E_BUSY, Invoke(proxy, mutator));
        Assert.Equal(-23L, RetCode.E_BUSY);

        // And idle again, so nothing above left the object latched.
        host.IsRunning = false;
        Assert.Equal(RetCode.OK, Invoke(proxy, mutator));

        static long Invoke(SqlCommandTaskProxy proxy, string mutator) => mutator switch
        {
            // A non-empty statement, so the oracle's own DEBUG assertion
            // [n_cst_threading_task_sqlcommand.sru:L36-L38] is satisfied in either configuration and the
            // busy guard is what this case observes.
            "SetSql" => proxy.SetSql("SELECT 1"),
            "SetAutoCommit" => proxy.SetAutoCommit(AutoCommitMode.AcOn),
            "Reset" => proxy.Reset(),
            "AddParam" => proxy.AddParam(1L),
            "ResetParams" => proxy.ResetParams(),
            _ => throw new ArgumentOutOfRangeException(nameof(mutator), mutator, "Unlisted mutator."),
        };
    }

    /// <summary>
    /// The WIRE mutators refuse with <see cref="RetCode.E_BUSY"/> too, through the adapter's own
    /// operation lease.
    /// </summary>
    /// <remarks>
    /// <b>TWO INDEPENDENT REFUSALS, AND BOTH ARE WANTED.</b> The adapter takes a per-task operation
    /// lease before it touches the proxy, so a second concurrent request is refused at the boundary
    /// even before the proxy's own predicate is consulted. The lease exists because a wire caller can
    /// genuinely issue two overlapping requests against one handle, which the oracle's single-threaded
    /// caller could not; the code it answers with is the SAME <c>E_BUSY</c>, so the observable contract
    /// is unchanged.
    /// </remarks>
    [Fact]
    public async Task TheWireMutatorsRefuseWithBusyWhileTheTaskIsLeased()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);
        CommandTask task = Resolve(harness, handle);

        // Take the lease the way the adapter takes it, then observe the refusals from outside.
        Assert.Equal(TaskLatchOutcome.Acquired, task.TryBeginOperation());

        try
        {
            SetCommandSqlResponse sql = await harness.Commands.SetSql(
                new SetCommandSqlRequest { Task = handle, Sql = "SELECT 1" },
                Context);
            Assert.Equal(WireRetCode.EBusy, sql.Status.RetCode);

            SetCommandAutoCommitResponse mode = await harness.Commands.SetAutoCommit(
                new SetCommandAutoCommitRequest { Task = handle, Autocommit = AutoCommitMode.AcOn },
                Context);
            Assert.Equal(WireRetCode.EBusy, mode.Status.RetCode);

            ResetCommandTaskResponse reset = await harness.Commands.Reset(
                new ResetCommandTaskRequest { Task = handle },
                Context);
            Assert.Equal(WireRetCode.EBusy, reset.Status.RetCode);

            ExecResponse exec = await harness.Commands.Exec(
                new ExecRequest { Task = handle, Sql = "SELECT 1" },
                Context);
            Assert.Equal(WireRetCode.EBusy, exec.Status.RetCode);
        }
        finally
        {
            Assert.False(task.EndOperation());
        }

        // Released: the same request now succeeds, so the refusal was the lease and nothing else.
        SetCommandSqlResponse afterRelease = await harness.Commands.SetSql(
            new SetCommandSqlRequest { Task = handle, Sql = "SELECT 1" },
            Context);
        Assert.Equal(WireRetCode.Ok, afterRelease.Status.RetCode);
    }

    // ---------------------------------------------------------------------------------------------
    //  REGION 2 - C-07: THE THREE-MODE EPILOGUE. THE HIGHEST-VALUE CONTENT IN THIS FILE.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// On a SUCCEEDING statement each of the three modes issues exactly the verbs the oracle issues -
    /// and <c>AC_OFF</c> issues NONE.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE SUCCESS EPILOGUE IS AN <c>if</c> / <c>else if</c> WITH NO <c>else</c>
    /// [<c>:L94-L103</c>], AND THE MISSING ARM IS THE BEHAVIOUR.</b> Under <c>AC_OFF</c> nothing at all
    /// happens: no commit, no rollback, no notification, no diagnostic. Writing the "obvious" commit
    /// into that position would change transactional semantics for every batching caller, so its ABSENCE
    /// is asserted here as an expected outcome rather than left to be rediscovered.
    /// </para>
    /// <para>
    /// <b>AND <c>AC_NATIVE</c> COMMITS NOTHING EITHER, WHILE STILL REPORTING THE WORK AS COMMITTED.</b>
    /// It raises the committed notification INSTEAD of committing [<c>:L97</c>], because the connection's
    /// own auto-commit has already made the statement durable. The two zero-commit rows therefore mean
    /// opposite things, and the notification column is what tells them apart.
    /// </para>
    /// </remarks>
    /// <param name="mode">The installed mode.</param>
    /// <param name="expectedCommits">Commits the engine should have seen.</param>
    /// <param name="expectedRollbacks">Rollbacks the engine should have seen.</param>
    /// <param name="expectedCommitted">Whether the committed notification should have fired.</param>
    [Theory]
    [MemberData(nameof(SucceedingModeCases))]
    public async Task EachModeIssuesExactlyItsOwnVerbsOnASucceedingStatement(
        AutoCommitMode mode,
        int expectedCommits,
        int expectedRollbacks,
        bool expectedCommitted)
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest
            {
                Task = handle,
                Sql = "UPDATE COMPANY SET AGE = 33 WHERE ID = 1",
                Autocommit = mode,
            },
            Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
        Assert.Equal(1, harness.Engine.ExecuteCalls);

        Assert.Equal(expectedCommits, harness.Engine.CommitCalls);
        Assert.Equal(expectedRollbacks, harness.Engine.RollbackCalls);
        Assert.Equal(expectedCommitted, response.Committed);
    }

    /// <summary>
    /// <c>AC_NATIVE</c> raises the connection's own auto-commit flag BEFORE the statement and lowers it
    /// AFTER, and no other mode touches it at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE INVERSION IS THE MOST COUNTER-INTUITIVE LINE IN THE ORACLE.</b> <c>AC_NATIVE</c> is
    /// commented <c>不开启事务</c> - "do not open a transaction" [<c>:L18</c>] - and it achieves that by
    /// turning the connection's auto-commit ON [<c>:L89</c>], which is what stops the runtime opening an
    /// explicit one. Reading the constant's name without the comment suggests the opposite write.
    /// </para>
    /// <para>
    /// The ordered history <c>[true, false]</c> is what proves BOTH halves: raised before the execution
    /// and restored after it [<c>:L96</c>]. The restore was verified against the source rather than
    /// assumed - the oracle performs it on the success path at <c>:L96</c> and on the failure path at
    /// <c>:L106</c>, and the failure half is asserted by its own case below.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheNativeModeRaisesTheConnectionFlagBeforeTheStatementAndLowersItAfter()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        harness.Engine.AutoCommitWrites.Clear();

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest
            {
                Task = handle,
                Sql = "INSERT INTO COMPANY (NAME) VALUES ('fixture')",
                Autocommit = AutoCommitMode.AcNative,
            },
            Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);

        // Exactly two writes, in this order: raised [:L89], then restored [:L96].
        Assert.Equal([true, false], harness.Engine.AutoCommitWrites);
        Assert.False(harness.Engine.AutoCommit);
    }

    /// <summary>
    /// Neither <c>AC_OFF</c> nor <c>AC_ON</c> writes the connection's auto-commit flag at all.
    /// </summary>
    /// <remarks>
    /// The inversion is guarded by <c>if _nAutoCommit = AC_NATIVE</c> on both the prologue [<c>:L88</c>]
    /// and both epilogues [<c>:L95</c>, <c>:L105</c>], so for the other two modes the flag is never
    /// touched - and it must not be, because the commit path itself refuses to run while auto-commit is
    /// on: <c>if AutoCommit then return RetCode.FAILED</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L240</c>]. A stray write here would
    /// turn every <c>AC_ON</c> commit into a failure.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task TheNonNativeModesNeverTouchTheConnectionFlag(int mode)
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        harness.Engine.AutoCommitWrites.Clear();

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest
            {
                Task = handle,
                Sql = "UPDATE COMPANY SET AGE = 34 WHERE ID = 1",
                Autocommit = (AutoCommitMode)mode,
            },
            Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
        Assert.Empty(harness.Engine.AutoCommitWrites);
    }

    /// <summary>
    /// <c>AC_ON</c> commits through the BASE task's commit path with auto-rollback, and a failing commit
    /// REPLACES the statement's own outcome.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The oracle is <c>rtCode = of_Commit(true)</c> [<c>:L99</c>] - the base helper, with auto-rollback
    /// TRUE - and the assignment is the point: <b>the commit's code overwrites the statement's</b>, so a
    /// statement that succeeded and then failed to commit is reported as a FAILURE. That is the honest
    /// reading, and an implementation that returned the statement's own success would report durable
    /// work that was rolled back.
    /// </para>
    /// <para>
    /// The auto-rollback is the transaction object's, not this task's: on a non-zero code after the
    /// commit it performs the clean rollback itself
    /// [<c>n_cst_thread_trans.sru:L249-L252</c>]. The error event then rolls back AGAIN on the base
    /// [<c>n_cst_thread_task_sqlbase.sru</c>'s <c>OnError</c>], and <b>that double rollback is legacy
    /// behaviour</b> rather than a defect to collapse.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheOnModeCommitsAndAFailingCommitReplacesTheStatementsOutcome()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        // The statement succeeds; the COMMIT is what fails.
        harness.Engine.ExecuteResult = SqlState.Succeeded(1);
        harness.Engine.CommitResult = SqlState.Failed(1555, "commit refused by the fixture");

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest
            {
                Task = handle,
                Sql = "UPDATE COMPANY SET AGE = 35 WHERE ID = 1",
                Autocommit = AutoCommitMode.AcOn,
            },
            Context);

        // The commit's code, not the statement's.
        Assert.Equal(WireRetCode.EDbError, response.Status.RetCode);
        Assert.Equal(1, harness.Engine.ExecuteCalls);
        Assert.Equal(1, harness.Engine.CommitCalls);

        // The commit's own auto-rollback, plus the general-error event's rollback on the base.
        Assert.Equal(2, harness.Engine.RollbackCalls);

        // No committed notification: the base raises it only when the commit SUCCEEDS.
        Assert.False(response.Committed);
    }

    /// <summary>
    /// ⚠ On a FAILING statement <c>AC_NATIVE</c> performs NO ROLLBACK OF ITS OWN, so its observable
    /// rollback count is ONE where every other mode's is TWO - and it still lowers the connection flag.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THIS IS THE MOST SURPRISING ARM IN THE WHOLE CONTRACT AND THE ONE MOST LIKELY TO BE
    /// "CORRECTED" (constraint C-B).</b> The failure epilogue reads
    /// <c>if _nAutoCommit = AC_NATIVE then transObject.AutoCommit = false else transObject.of_Rollback()</c>
    /// [<c>:L105-L109</c>]. The native arm therefore SKIPS the rollback entirely - deliberately, because
    /// under connection-level auto-commit there was never an explicit transaction to roll back, and
    /// issuing one would either fail or roll back work that is already durable.
    /// </para>
    /// <para>
    /// <b>ONE ROLLBACK IS STILL OBSERVED, AND ITS PROVENANCE MATTERS.</b> It comes from the general-error
    /// event, which rolls back on the BASE before forwarding
    /// [<c>n_cst_thread_task_sqlbase.sru</c>'s <c>onerror</c>], and that path is common to every mode. So
    /// the assertion cannot be "zero rollbacks" - it is "ONE FEWER THAN EVERY OTHER MODE", which is why
    /// this case asserts the native count and the off count side by side rather than in isolation.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheNativeModeFailureArmPerformsNoRollbackOfItsOwn()
    {
        // The native mode, on a failing statement.
        Harness native = new();
        TaskHandle nativeHandle = await CreateTask(native);
        native.Engine.ExecuteResult = SqlState.Failed(19, "the fixture refuses this statement");
        native.Engine.AutoCommitWrites.Clear();

        ExecResponse nativeResponse = await native.Commands.Exec(
            new ExecRequest
            {
                Task = nativeHandle,
                Sql = "INSERT INTO COMPANY (NAME) VALUES ('fixture')",
                Autocommit = AutoCommitMode.AcNative,
            },
            Context);

        Assert.Equal(WireRetCode.EDbError, nativeResponse.Status.RetCode);

        // ⚠ ONE - the general-error event's, on the base. The arm itself issued none.
        Assert.Equal(1, native.Engine.RollbackCalls);

        // The flag is still restored on the failure path [:L106], exactly as on the success path.
        Assert.Equal([true, false], native.Engine.AutoCommitWrites);

        // And no commit was attempted on a failure, under any mode.
        Assert.Equal(0, native.Engine.CommitCalls);

        // The same statement under AC_OFF, for the side-by-side comparison the claim depends on.
        Harness off = new();
        TaskHandle offHandle = await CreateTask(off);
        off.Engine.ExecuteResult = SqlState.Failed(19, "the fixture refuses this statement");

        ExecResponse offResponse = await off.Commands.Exec(
            new ExecRequest
            {
                Task = offHandle,
                Sql = "INSERT INTO COMPANY (NAME) VALUES ('fixture')",
                Autocommit = AutoCommitMode.AcOff,
            },
            Context);

        Assert.Equal(WireRetCode.EDbError, offResponse.Status.RetCode);

        // TWO - the else arm's own rollback [:L108] PLUS the general-error event's.
        Assert.Equal(2, off.Engine.RollbackCalls);
    }

    /// <summary>
    /// An UNDECLARED auto-commit value follows the <c>AC_OFF</c> arm - including its rollback - when it
    /// is installed on the worker directly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The oracle's dispatch is a chain of equality tests against <c>AC_NATIVE</c> and <c>AC_ON</c>
    /// [<c>:L95</c>, <c>:L98</c>, <c>:L105</c>], so anything else falls to the <c>else</c> - which is to
    /// say to <c>AC_OFF</c>'s behaviour. That fold is preserved (constraint C-B).
    /// </para>
    /// <para>
    /// <b>AND THE BOUNDARY STILL REFUSES THE SAME VALUE, WHICH IS NOT A CONTRADICTION.</b> A proto3 enum
    /// is open, so an undeclared number travels intact and arrives as a value the CONTRACT does not
    /// define; folding it silently into "do not commit" would tell a caller who believed they asked for a
    /// commit nothing at all. So the worker preserves the legacy fold and the wire declines the malformed
    /// request - both are asserted here, in that order, so neither can be mistaken for the other.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnUndeclaredModeFoldsIntoTheOffArmOnTheWorkerAndIsRefusedAtTheWire()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);
        CommandTask task = Resolve(harness, handle);

        // The wire declines it, with the contract's own diagnostic, and installs nothing.
        ExecResponse refused = await harness.Commands.Exec(
            new ExecRequest
            {
                Task = handle,
                Sql = "UPDATE COMPANY SET AGE = 36 WHERE ID = 1",
                Autocommit = (AutoCommitMode)7,
            },
            Context);

        Assert.Equal(WireRetCode.EInvalidArgument, refused.Status.RetCode);
        Assert.NotEmpty(refused.Status.ErrorText);
        Assert.Equal(0, harness.Engine.ExecuteCalls);

        // The worker's own fold, reached the way the oracle reaches it - by installing the value.
        Assert.Equal(RetCode.OK, task.Worker.SetAutoCommit((AutoCommitMode)7));
        Assert.Equal(RetCode.OK, task.Worker.SetSql("UPDATE COMPANY SET AGE = 36 WHERE ID = 1"));

        harness.Engine.ExecuteResult = SqlState.Failed(19, "the fixture refuses this statement");

        Assert.Equal(
            RetCode.E_DB_ERROR,
            task.Worker.OnDoTask(TestContext.Current.CancellationToken));

        // The AC_OFF shape exactly: no commit, the else arm's rollback plus the error event's, and no
        // write to the connection flag because the inversion is gated on AC_NATIVE.
        Assert.Equal(0, harness.Engine.CommitCalls);
        Assert.Equal(2, harness.Engine.RollbackCalls);
        Assert.Empty(harness.Engine.AutoCommitWrites);
    }

    /// <summary>
    /// The three mode values are the GENERATED contract enum's, with the oracle's own numbers, and
    /// nothing re-declares them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>ONE DECLARATION, REACHED FROM EVERYWHERE (constraint 0.7.2).</b> The oracle already worked this
    /// way: the caller-side object does not restate the numbers, it initialises its constants FROM the
    /// worker's - <c>constant long AC_OFF = n_cst_thread_task_sqlcommand.AC_OFF</c>
    /// [<c>n_cst_threading_task_sqlcommand.sru:L19-L21</c>]. The managed equivalent is that the ONLY
    /// declaration lives in the protocol definition and both the task and the adapter consume the
    /// generated type.
    /// </para>
    /// <para>
    /// The assertion is by reflection on the enum's assembly, because that is the claim: not merely that
    /// the values are 0, 1 and 2, but that the type carrying them was generated into the CONTRACTS
    /// assembly and is therefore the single source both sides share. A local copy would satisfy a
    /// value-only assertion and fail this one.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheAutoCommitValuesComeFromTheSingleGeneratedContractEnum()
    {
        // The oracle's numbers [n_cst_thread_task_sqlcommand.sru:L16-L18].
        Assert.Equal(0, (int)AutoCommitMode.AcOff);
        Assert.Equal(1, (int)AutoCommitMode.AcOn);
        Assert.Equal(2, (int)AutoCommitMode.AcNative);

        // Exactly three declared enumerators - the contract states no synthetic sentinel is needed
        // because AC_OFF already occupies zero.
        Assert.Equal(3, Enum.GetValues<AutoCommitMode>().Length);

        Type mode = typeof(AutoCommitMode);

        // Generated INTO THE CONTRACTS ASSEMBLY, which is what makes it the shared declaration.
        Assert.Equal("PowerFramework.Contracts", mode.Assembly.GetName().Name);
        Assert.Equal("PowerFramework.Contracts.Persistence.V1", mode.Namespace);

        // The task's mutator and the adapter's request field are typed by THAT enum, not by a copy.
        Assert.Equal(
            mode,
            typeof(SqlCommandTask).GetMethod(
                nameof(SqlCommandTask.SetAutoCommit),
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
                .GetParameters()[0]
                .ParameterType);

        Assert.Equal(mode, typeof(SetCommandAutoCommitRequest).GetProperty("Autocommit")!.PropertyType);

        // And the dialect enum is generated beside it, from DBT_MSSQL = 0 / DBT_ORACLE = 1
        // [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L60-L61].
        Assert.Equal(0, (int)DatabaseType.DbtMssql);
        Assert.Equal(1, (int)DatabaseType.DbtOracle);
        Assert.Equal(mode.Assembly, typeof(DatabaseType).Assembly);
    }

    /// <summary>
    /// ⚠ A provider code of ONE HUNDRED reads as SUCCESS, and any other non-zero code does not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE CARVE-OUT IS EXPLICIT IN THE ORACLE AND MUST NOT BE SIMPLIFIED (constraint C-B):</b>
    /// <c>if SQLCode &lt;&gt; 0 and SQLCode &lt;&gt; 100 then return RetCode.E_DB_ERROR</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L233-L235</c>]. A hundred is the
    /// runtime's "not found", and an implementation that treated every non-zero code as a failure would
    /// invent errors the legacy does not report.
    /// </para>
    /// <para>
    /// <b>THREE CODE SPACES MEET IN THIS RESPONSE AND NONE IS COMPARABLE TO ANOTHER.</b>
    /// <c>status.ret_code</c> is the FRAMEWORK's algebra; <c>sql_code</c> is the RUNTIME's statement
    /// status where both 0 and 100 mean success; <c>sql_db_code</c> is the DRIVER's own number. So this
    /// case asserts the framework code AND that the raw provider code is reported through UNCHANGED - a
    /// hundred must still read as a hundred on the wire, or a caller cannot tell "nothing found" from
    /// "one row changed".
    /// </para>
    /// </remarks>
    /// <param name="sqlCode">The provider code the statement returns.</param>
    /// <param name="expectedSuccess">Whether the framework reads it as a success.</param>
    [Theory]
    [MemberData(nameof(SqlCodeReadings))]
    public async Task AProviderCodeOfOneHundredReadsAsSuccess(long sqlCode, bool expectedSuccess)
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        // The engine answers the raw provider state, and the carve-out is applied where the oracle
        // applies it - inside the transaction object.
        harness.Engine.ExecuteResult = new SqlState(
            sqlCode,
            sqlCode == 0L ? 0L : sqlCode,
            0L,
            sqlCode == 0L ? string.Empty : "provider text",
            string.Empty);

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest
            {
                Task = handle,
                Sql = "DELETE FROM COMPANY WHERE ID = 4242",
                Autocommit = AutoCommitMode.AcOff,
            },
            Context);

        Assert.Equal(expectedSuccess, Predicates.IsSucceeded((long)response.Status.RetCode));
        Assert.Equal(expectedSuccess ? WireRetCode.Ok : WireRetCode.EDbError, response.Status.RetCode);

        // The RAW provider code, reported through untouched.
        Assert.Equal(sqlCode, response.SqlCode);

        // And the framework's own reading of a hundred, stated directly against the constant the task
        // declares so the number is discoverable from the type that depends on it.
        if (sqlCode == SqlCommandTask.SqlCodeNotFound)
        {
            Assert.True(expectedSuccess);
            Assert.Equal(0, harness.Engine.RollbackCalls);
        }
    }

    /// <summary>
    /// The failure path raises a structured database error carrying the FULLY BOUND statement, and the
    /// wire projection MASKS it - so the interpolated literal never leaves the process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THIS IS THE CONCRETE REDACTION SITE IN C-07 (constraint C-F).</b> The oracle's own line is
    /// <c>Event OnDBError(transObject.SQLDBCode, transObject.SQLErrText, sSQL, Primary!, 0)</c>
    /// [<c>:L110</c>], and its third argument is <c>sSQL</c> - the statement AFTER binding, which under
    /// <c>DisableBind=1</c> means after every value has been interpolated into the text
    /// [<c>n_cst_thread_task_sqlbase.sru:L128-L129</c>]. <b>The legacy logger performs no redaction at
    /// all</b>, so the exposure is real and the masking is the port's own required addition.
    /// </para>
    /// <para>
    /// <b>BOTH HALVES ARE ASSERTED, AND ONE WITHOUT THE OTHER WOULD BE THE WRONG TEST.</b> The
    /// IN-PROCESS payload must still carry the bound text, because that is what a diagnostic is FOR and
    /// what the caller-side proxy's sink receives; the OUTBOUND projection must mask it. A port that
    /// masked at the raise site would satisfy the second half while destroying the first.
    /// </para>
    /// <para>
    /// The buffer and row arguments travel too, as <c>Primary!</c> and <c>0</c> exactly as the oracle
    /// passes them - a zero row rather than a one-based first row, which is legacy contract and not an
    /// off-by-one to normalise.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheFailurePathCarriesTheBoundStatementAndTheWireMasksIt()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);
        CommandTask task = Resolve(harness, handle);

        harness.Engine.ExecuteResult = SqlState.Failed(19, "NOT NULL constraint failed: COMPANY.AGE");

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest
            {
                Task = handle,
                Sql = "INSERT INTO COMPANY (NAME) VALUES (?)",
                Parameters = { Positional(BoundLiteral) },
                Autocommit = AutoCommitMode.AcOff,
            },
            Context);

        Assert.Equal(WireRetCode.EDbError, response.Status.RetCode);

        // -----------------------------------------------------------------------------------------
        //  HALF ONE - THE IN-PROCESS PAYLOAD CARRIES THE BOUND TEXT.
        // -----------------------------------------------------------------------------------------
        DbErrorData captured = task.Proxy.GetLastDbErrorData();

        Assert.Equal(19L, captured.SqlDbCode);
        Assert.Contains(BoundLiteral, captured.SqlSyntax, StringComparison.Ordinal);

        // The oracle's own two trailing arguments [:L110].
        Assert.Equal(DwBuffer.Primary, captured.Buffer);
        Assert.Equal(0L, captured.Row);

        // The provider received the PLACEHOLDER form with the value bound, never the interpolated text -
        // which is the CWE-89 exposure the dual form exists to close (AAP 0.6.4, 0.7.2).
        Assert.DoesNotContain(BoundLiteral, harness.Engine.LastCanonicalStatement, StringComparison.Ordinal);
        Assert.Contains(BoundLiteral, harness.Engine.LastRenderedStatement, StringComparison.Ordinal);
        Assert.Equal([BoundLiteral], harness.Engine.LastParameters);

        // -----------------------------------------------------------------------------------------
        //  HALF TWO - THE OUTBOUND PROJECTION MASKS IT.
        // -----------------------------------------------------------------------------------------
        Assert.NotNull(response.Status.DbError);

        Assert.DoesNotContain(BoundLiteral, response.Status.DbError.Sqlsyntax, StringComparison.Ordinal);
        Assert.Contains(RedactionPlaceholder, response.Status.DbError.Sqlsyntax, StringComparison.Ordinal);

        // The wire text is EXACTLY the sanctioned redactor's output over the captured statement, which is
        // the claim "it went through the mandatory redactor" stated as an equality rather than as a
        // substring search that a partial mask could also satisfy.
        Assert.Equal(
            SqlRedactor.Instance.Redact(captured.SqlSyntax),
            response.Status.DbError.Sqlsyntax);
    }

    /// <summary>
    /// Every field on ONE response that carries the provider's message discloses at the SAME depth.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>THE DEFECT THIS CLOSES.</b> One provider message reached this response through three fields at
    /// two different depths. <c>Microsoft.Data.Sqlite</c> does not hand back a bare message - it wraps it,
    /// <c>SQLite Error 1: 'no such table: NO_SUCH_TABLE'.</c> - and in that shape the result code is a
    /// numeric literal and the whole diagnosis is a quoted string. <c>DbError.sqlerrtext</c> went through the
    /// envelope-preserving policy and read the condition in full, while <c>ExecResponse.sql_err_text</c> and
    /// <c>status.error_text</c> went through the strict one and read
    /// <c>SQLite Error &lt;redacted&gt;: '&lt;redacted&gt;'.</c> Same value, same response, two depths: a
    /// consumer could not tell which field to trust, and a caller that named a table which does not exist
    /// was told only that a database error had occurred.
    /// </para>
    /// <para>
    /// <b>THE ASSERTION IS EQUALITY AGAINST THE POLICY, NOT A SUBSTRING SEARCH.</b> Each field is compared
    /// with <c>RedactProviderDiagnostic</c>'s output over the same input, so a field that masked a little
    /// differently would fail here rather than pass a "contains the column name" check.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task EveryFieldCarryingTheProviderMessageDisclosesAtTheSameDepth()
    {
        const string Envelope = "SQLite Error 1: 'no such table: NO_SUCH_TABLE'.";

        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        harness.Engine.ExecuteResult = SqlState.Failed(1, Envelope);

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest
            {
                Task = handle,
                Sql = "INSERT INTO NO_SUCH_TABLE (A) VALUES (?)",
                Parameters = { Positional(BoundLiteral) },
                Autocommit = AutoCommitMode.AcOff,
            },
            Context);

        Assert.Equal(WireRetCode.EDbError, response.Status.RetCode);
        Assert.NotNull(response.Status.DbError);

        string expected = SqlRedactor.Instance.RedactProviderDiagnostic(Envelope);

        // THE THREE FIELDS AGREE, and each equals the policy's own output.
        Assert.Equal(expected, response.Status.DbError.Sqlerrtext);
        Assert.Equal(expected, response.SqlErrText);
        Assert.Equal(expected, response.Status.ErrorText);

        // AND THE CONDITION IS LEGIBLE, which is what the depth is for. The envelope quotes no value, so
        // the whole message survives - this is the row that shows the defect was a loss of diagnosis rather
        // than a difference of formatting.
        Assert.Equal(Envelope, expected);
        Assert.Contains("no such table", response.SqlErrText, StringComparison.Ordinal);
        Assert.Contains("no such table", response.Status.ErrorText, StringComparison.Ordinal);

        // THE STATEMENT FIELD IS UNAFFECTED AND STILL STRICTLY MASKED. The widening is per field class, so
        // the field that carries interpolated literals by construction keeps the strict policy (C-F).
        Assert.DoesNotContain(BoundLiteral, response.Status.DbError.Sqlsyntax, StringComparison.Ordinal);
        Assert.Contains(RedactionPlaceholder, response.Status.DbError.Sqlsyntax, StringComparison.Ordinal);
    }

    /// <summary>
    /// A value the provider quoted INSIDE its message is still masked in every one of those fields.
    /// </summary>
    /// <remarks>
    /// <b>THIS IS THE ROW THAT MAKES THE WIDENING SAFE RATHER THAN A HOLE.</b> Preserving the envelope
    /// preserves the wrapper and the result code only; the interior still goes through the identical
    /// literal-scoped scan. Without this row, "the fields now agree" would be indistinguishable from
    /// "masking was switched off on two of them".
    /// </remarks>
    [Fact]
    public async Task AValueQuotedInsideTheProviderMessageIsStillMaskedOnEveryField()
    {
        const string Secret = "Kenneth-Fixture-Secret";
        const string Envelope = "SQLite Error 19: 'CHECK constraint failed: name > '" + Secret + "''.";

        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        harness.Engine.ExecuteResult = SqlState.Failed(19, Envelope);

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest
            {
                Task = handle,
                Sql = "UPDATE COMPANY SET NAME = ? WHERE ID = ?",
                Parameters = { Positional(BoundLiteral), Positional(7L) },
                Autocommit = AutoCommitMode.AcOff,
            },
            Context);

        Assert.NotNull(response.Status.DbError);

        foreach (string field in new[]
        {
            response.Status.DbError.Sqlerrtext,
            response.SqlErrText,
            response.Status.ErrorText,
        })
        {
            Assert.DoesNotContain(Secret, field, StringComparison.Ordinal);
            Assert.Contains(RedactionPlaceholder, field, StringComparison.Ordinal);

            // The condition survives even though the value does not.
            Assert.Contains("CHECK constraint failed", field, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A diagnostic that is NOT the provider's envelope is masked exactly as it was before.
    /// </summary>
    /// <remarks>
    /// The shape must match the WHOLE input or not at all, so a framework-authored sentence, a bare message
    /// and a statement-bearing string all take the strict path unchanged. This is what confines the widening
    /// to the one shape it was written for, and it is asserted on the WIRE fields rather than only on the
    /// redactor, because the redactor's own contract test cannot see which policy a projection selected.
    /// </remarks>
    [Theory]
    [InlineData("NOT NULL constraint failed: COMPANY.NAME")]
    [InlineData("SELECT * FROM COMPANY WHERE name = 'Kenneth-Fixture-Row'")]
    [InlineData("SQLITE Error 19: 'NOT NULL constraint failed: COMPANY.NAME'.")]
    public async Task ADiagnosticThatIsNotTheProviderEnvelopeIsMaskedExactlyAsBefore(string diagnostic)
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        harness.Engine.ExecuteResult = SqlState.Failed(19, diagnostic);

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest
            {
                Task = handle,
                Sql = "UPDATE COMPANY SET NAME = ? WHERE ID = ?",
                Parameters = { Positional(BoundLiteral), Positional(7L) },
                Autocommit = AutoCommitMode.AcOff,
            },
            Context);

        string strict = SqlRedactor.Instance.Redact(diagnostic);

        Assert.NotNull(response.Status.DbError);
        Assert.Equal(strict, response.Status.DbError.Sqlerrtext);
        Assert.Equal(strict, response.SqlErrText);
        Assert.Equal(strict, response.Status.ErrorText);
    }

    /// <summary>
    /// The outbound projection CANNOT be weakened by handing it a permissive redactor - it reaches its
    /// policy directly and takes no policy argument at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE RECORDING REDACTOR IS USED HERE AS THE ADVERSARY, NOT AS THE SEAM (constraint C-F).</b>
    /// Scripted with a PASS-THROUGH mask it is exactly the double an implementation that accepted an
    /// injected policy would have to honour - and it records what it was shown, so the case can prove the
    /// bound statement genuinely reached it. That the sanctioned projection nevertheless masks the field
    /// is the property worth pinning: <c>ToDbError()</c> calls <c>SqlRedactor.Instance</c> directly, so
    /// there is no call site, no container registration and no test double that can turn masking off.
    /// </para>
    /// <para>
    /// The recorder also gives the assertion its exactness. Because it captures the statement in call
    /// order, the case compares the WIRE text against the mask of the RECORDED text rather than against a
    /// text it reconstructed for itself.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheOutboundProjectionCannotBeWeakenedByAPermissiveRedactor()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);
        CommandTask task = Resolve(harness, handle);

        harness.Engine.ExecuteResult = SqlState.Failed(19, "UNIQUE constraint failed: COMPANY.ID");

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest
            {
                Task = handle,
                Sql = "UPDATE COMPANY SET NAME = ? WHERE ID = ?",
                Parameters = { Positional(BoundLiteral), Positional(7L) },
                Autocommit = AutoCommitMode.AcOff,
            },
            Context);

        Assert.Equal(WireRetCode.EDbError, response.Status.RetCode);

        DbErrorData captured = task.Proxy.GetLastDbErrorData();

        // The adversary: a redactor that masks NOTHING, and records everything it is shown.
        RecordingSqlRedactor permissive = new() { Mask = static statement => statement };

        ISqlRedactor asContract = permissive;
        string passedThrough = asContract.Redact(captured.SqlSyntax);

        // It saw the statement, exactly once, and it saw the INTERPOLATED form.
        Assert.Equal(1, permissive.CallCount);
        Assert.True(permissive.Observed(captured.SqlSyntax));
        Assert.Equal(captured.SqlSyntax, permissive.LastStatement);
        Assert.Contains(BoundLiteral, passedThrough, StringComparison.Ordinal);

        // And the SANCTIONED projection masked anyway - the permissive policy reached nothing.
        DbError wire = captured.ToDbError();

        Assert.DoesNotContain(BoundLiteral, wire.Sqlsyntax, StringComparison.Ordinal);
        Assert.Equal(wire.Sqlsyntax, response.Status.DbError.Sqlsyntax);

        // The recorder is reusable, and clearing it is what lets one instance serve a longer pipeline.
        permissive.Clear();
        Assert.Equal(0, permissive.CallCount);
        Assert.Empty(permissive.Statements);
    }

    /// <summary>
    /// No log record any collaborator emitted contains the interpolated literal.
    /// </summary>
    /// <remarks>
    /// <b>THE DIAGNOSTIC PATH IS THE OTHER WAY OUT OF THE PROCESS, AND IT IS CHECKED BY GREPPING THE
    /// WHOLE SINK (constraint C-F).</b> Six collaborators write to one shared list - both adapters, both
    /// registries, the worker task and its proxy - and this case searches every record rather than
    /// inspecting one field of one message. The worker's own two records are the ones that matter most:
    /// both format the statement through <c>SqlRedactor.Instance.Redact</c> before writing it, and a
    /// future record that forgot to would fail here.
    /// </remarks>
    [Fact]
    public async Task NoLogRecordCarriesTheInterpolatedLiteral()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        harness.Engine.ExecuteResult = SqlState.Failed(19, "CHECK constraint failed: COMPANY");

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest
            {
                Task = handle,
                Sql = "INSERT INTO COMPANY (NAME) VALUES (?)",
                Parameters = { Positional(BoundLiteral) },
                Autocommit = AutoCommitMode.AcOn,
            },
            Context);

        Assert.Equal(WireRetCode.EDbError, response.Status.RetCode);

        // Records were genuinely produced, so the grep below is not vacuously passing over an empty list.
        Assert.NotEmpty(harness.Log);

        Assert.DoesNotContain(
            harness.Log,
            record => record.Contains(BoundLiteral, StringComparison.Ordinal));

        // At least one record DID carry the statement - in its masked form. That is the positive half:
        // the statement is logged, and it is logged redacted.
        Assert.Contains(
            harness.Log,
            record => record.Contains(RedactionPlaceholder, StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------------------------------------
    //  REGION 3 - C-07: WHAT BELONGS TO THE TRANSACTION AND NOT TO THE TASK
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Positional <c>?</c> binding is provided by the pooled transaction's execute surface and CONSUMED
    /// by the task, which re-implements none of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE DIVISION OF LABOUR IS THE ORACLE'S.</b> The task's whole involvement is
    /// <c>if of_HasParams() then _of_SQLBindParams(ref sSQL, TransObject.of_GetDBType())</c>
    /// [<c>:L81-L86</c>] followed by <c>transObject.of_Exec(sSQL)</c> [<c>:L92</c>] - it asks the
    /// TRANSACTION for the dialect and hands the TRANSACTION the statement. Everything about how a
    /// statement is issued lives behind that call.
    /// </para>
    /// <para>
    /// A POSITIONAL parameter is the EMPTY-NAMED case rather than a separate kind
    /// [<c>n_cst_threading_task_sqlbase.sru:L138</c>], and the ordinal is the collection order - which is
    /// why the delegation assertion reads the engine's parameter list IN ORDER rather than by name.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task PositionalBindingIsProvidedByTheTransactionAndConsumedByTheTask()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest
            {
                Task = handle,
                Sql = "INSERT INTO COMPANY (NAME, AGE, ADDRESS) VALUES (?, ?, ?)",
                Parameters = { Positional("Paul"), Positional(32L), Positional("California") },
                Autocommit = AutoCommitMode.AcOff,
            },
            Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);

        // Bound, in positional order, by the layer BELOW the task.
        Assert.Equal(["Paul", 32L, "California"], harness.Engine.LastParameters);

        // The canonical form still carries placeholders - the task did not splice values into the text.
        Assert.DoesNotContain("Paul", harness.Engine.LastCanonicalStatement, StringComparison.Ordinal);

        // And the observable form is the interpolated one the oracle would have produced, preserved for
        // characterization comparison.
        Assert.Contains("Paul", harness.Engine.LastRenderedStatement, StringComparison.Ordinal);
        Assert.Contains("California", harness.Engine.LastRenderedStatement, StringComparison.Ordinal);
    }

    /// <summary>
    /// The leading <c>@</c> selects the statement-caching execution mode on a COMMAND, and the selector
    /// itself is not part of the statement.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE SAME CHARACTER MEANS TWO DIFFERENT THINGS ON TWO DIFFERENT VERBS, AND CONFLATING THEM IS
    /// THE MISTAKE THIS CASE GUARDS.</b> On the RETRIEVE verb the prefix names a DATAWINDOW OBJECT -
    /// <c>if Left(sql,1) = "@" then ds.DataObject = Mid(sql,2)</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L309-L310</c>] - and that meaning
    /// belongs to C-05, which carries it as a field of its own rather than as an in-band prefix. On a
    /// COMMAND the prefix selects statement caching, demonstrated by the oracle's own ten-iteration loop
    /// [<c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L396-L406</c>], and that meaning is C-07's.
    /// </para>
    /// <para>
    /// The mode must be CARRIED to the provider rather than left in the text: the selector is not SQL,
    /// and a provider handed <c>@INSERT ...</c> as statement text would reject it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheLeadingSelectorCarriesTheCachingModeAndLeavesTheStatement()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest
            {
                Task = handle,
                Sql = "@INSERT INTO COMPANY (NAME, AGE) VALUES (?, ?)",
                Parameters = { Positional("Paul"), Positional(32L) },
                Autocommit = AutoCommitMode.AcOff,
            },
            Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);

        // The mode reached the provider seam.
        Assert.True(harness.Engine.LastCacheStatement);

        // The selector did not. Exactly one character is consumed, off the SNAPSHOT.
        Assert.StartsWith("INSERT", harness.Engine.LastCanonicalStatement, StringComparison.Ordinal);
        Assert.StartsWith("INSERT", harness.Engine.LastRenderedStatement, StringComparison.Ordinal);

        // And the installed statement keeps the caller's text verbatim, so the task stays re-runnable in
        // the same mode.
        Assert.Equal(
            "@INSERT INTO COMPANY (NAME, AGE) VALUES (?, ?)",
            Resolve(harness, handle).Worker.Sql);

        // The two meanings are declared as two constants with one value, which is the codebase's own way
        // of saying that a reader who arrives at either is told about the other.
        Assert.Equal(SqlCommandTask.DataWindowObjectPrefix, SqlCommandTask.StatementCachingPrefix);
    }

    /// <summary>
    /// Multi-statement batch execution and error-text retrieval are the transaction's, and the task
    /// merely surfaces them.
    /// </summary>
    /// <remarks>
    /// The oracle issues a batch the same way it issues a single statement - <c>of_Exec</c> takes one
    /// string and <c>EXECUTE IMMEDIATE</c> it [<c>n_cst_thread_trans.sru:L229</c>] - so a batch is not a
    /// second code path and the task has nothing to split. Error text is likewise read from the
    /// transaction's own accessor at the two points the oracle reads it [<c>:L110-L111</c>], never
    /// synthesized by the task.
    /// </remarks>
    [Fact]
    public async Task BatchExecutionAndErrorTextAreReadFromTheTransaction()
    {
        const string batch = "DELETE FROM COMPANY; INSERT INTO COMPANY (NAME) VALUES ('a')";

        // Deliberately free of quotes and digits, so the outbound masking of this field is the IDENTITY
        // and the assertion below is about FORWARDING rather than about the mask. A quoted run here would
        // be masked on the way out - correctly, because a provider message can quote row data - and the
        // case would then be testing the redactor, which SqlRedactorTests owns.
        const string providerText = "the fixture provider text";

        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        // A batch, executed as ONE statement by the layer below.
        ExecResponse ok = await harness.Commands.Exec(
            new ExecRequest { Task = handle, Sql = batch, Autocommit = AutoCommitMode.AcOff },
            Context);

        Assert.Equal(WireRetCode.Ok, ok.Status.RetCode);
        Assert.Equal(1, harness.Engine.ExecuteCalls);
        Assert.Equal(batch, harness.Engine.LastCanonicalStatement);

        // And the error text on a failure is the TRANSACTION's, forwarded rather than composed.
        harness.Engine.ExecuteResult = SqlState.Failed(1, providerText);

        ExecResponse failed = await harness.Commands.Exec(
            new ExecRequest { Task = handle, Sql = batch, Autocommit = AutoCommitMode.AcOff },
            Context);

        Assert.Equal(WireRetCode.EDbError, failed.Status.RetCode);
        Assert.Equal(providerText, failed.SqlErrText);
        Assert.Equal(providerText, failed.Status.DbError.Sqlerrtext);
        Assert.Equal(1L, failed.SqlDbCode);
    }

    /// <summary>
    /// The four delegated capabilities are members of the POOLED-TRANSACTION contract, asserted against
    /// the pooled-transaction double, and the task consumes them rather than re-implementing any.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>ASSERTED AGAINST THE DOUBLE, WHICH IS A CLAIM ABOUT WHERE THE CAPABILITY LIVES.</b> The cases
    /// above prove the capabilities WORK by driving them through the real pooled transaction; this one
    /// proves they belong to the <see cref="IPooledTransaction"/> CONTRACT, by exercising a completely
    /// independent implementation of it. If any of the four had leaked into the task, the double would
    /// have to grow a member the interface does not declare - and it does not.
    /// </para>
    /// <para>
    /// The double also records its calls in one ordered log, which is what lets the ordering half be
    /// asserted: the statement is cleared, executed and only then committed, matching
    /// <c>of_ClearState</c> before <c>EXECUTE IMMEDIATE</c> before the epilogue
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L222</c>, <c>:L229</c>].
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFourDelegatedCapabilitiesBelongToThePooledTransactionContract()
    {
        const string pooledProviderText = "the pooled double's own provider text";

        using ScriptedPooledTransaction pooled = new();

        // The seam the task actually calls, reached through the INTERFACE so this case cannot accidentally
        // exercise a member the contract does not publish.
        IPooledTransaction contract = pooled;

        // (1) POSITIONAL BINDING - carried as a bound statement, both renderings plus the values.
        SqlCommandText bound = SqlCommandText.FromBoundStatement(
            "INSERT INTO COMPANY (NAME, AGE) VALUES (?, ?)",
            "INSERT INTO COMPANY (NAME, AGE) VALUES ('Paul', 32)",
            [new SqlBoundParameter("@p1", "Paul"), new SqlBoundParameter("@p2", 32L)],
            cacheStatement: false);

        contract.ClearState();
        Assert.Equal(RetCode.OK, contract.Exec(bound, TestContext.Current.CancellationToken));

        // (2) MULTI-STATEMENT BATCH - one call, one string, no splitting anywhere.
        Assert.Equal(
            RetCode.OK,
            contract.Exec(
                "DELETE FROM COMPANY; INSERT INTO COMPANY (NAME) VALUES ('a')",
                TestContext.Current.CancellationToken));

        // (3) THE COMMIT AND ROLLBACK VERBS, both published by the contract.
        Assert.Equal(RetCode.OK, contract.Commit(autoRollback: true));
        Assert.Equal(RetCode.OK, contract.Rollback());

        // (4) ERROR-TEXT RETRIEVAL - the transaction's own accessor, which is where the task reads it
        // [n_cst_thread_task_sqlcommand.sru:L110-L111]. Stamped HERE rather than at construction because
        // the clear precedes the statement [n_cst_thread_trans.sru:L222] and would have erased it: the
        // provider's text exists only after an execution, which is exactly when the task reads it.
        pooled.SqlErrText = pooledProviderText;
        Assert.Equal(pooledProviderText, contract.SqlErrText);

        // The ordered log: cleared, then executed twice, then committed, then rolled back.
        Assert.Equal(2, pooled.CountOf(TransactionCallKind.Exec));
        Assert.True(pooled.Precedes(TransactionCallKind.ClearState, TransactionCallKind.Exec));
        Assert.True(pooled.Precedes(TransactionCallKind.Exec, TransactionCallKind.Commit));
        Assert.True(pooled.Precedes(TransactionCallKind.Commit, TransactionCallKind.Rollback));

        // The OBSERVABLE - interpolated - form is what the log records, because that is the parity artefact
        // a characterization recording compares byte for byte.
        Assert.Equal(
            [
                "INSERT INTO COMPANY (NAME, AGE) VALUES ('Paul', 32)",
                "DELETE FROM COMPANY; INSERT INTO COMPANY (NAME) VALUES ('a')",
            ],
            pooled.ExecutedStatements);

        // The dialect selector is the transaction's too - the task ASKS for it [:L82] rather than
        // configuring it, and its two arms are the oracle's only two.
        Assert.Equal(DatabaseType.DbtMssql, contract.GetDbType());

        // And the auto-commit flag AC_NATIVE inverts is a member of this same contract, which is why the
        // task can toggle it without knowing what kind of connection is underneath.
        contract.AutoCommit = true;
        Assert.True(contract.AutoCommit);
        contract.AutoCommit = false;
        Assert.False(contract.AutoCommit);

        // The not-found code the carve-out spares is published by the double as well, so a test that needs
        // it does not have to restate 100 for itself.
        Assert.Equal(SqlCommandTask.SqlCodeNotFound, ScriptedPooledTransaction.NotFoundSqlCode);
    }

    /// <summary>
    /// The contract accepts AT LEAST the legacy maximum number of parameters, and imposes NO ceiling of
    /// its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE LEGACY CEILINGS ARE RECORDED LIMITS, NOT REQUIREMENTS (constraint C-K, AAP 0.2.1.4).</b>
    /// There are three of them and all three exist for ONE reason: PowerScript cannot forward an
    /// arbitrary-length argument list, so every surface had to be written out by hand one arity at a time.
    /// The SQLite binding's command entry point is overloaded from zero arguments up to ELEVEN and then
    /// simply stops [<c>ws_objects/pfw.utility.sqlite.pbl.src/n_sqlite.sru:L33-L43</c>]; the transaction
    /// object's retrieval unrolls to EIGHT [<c>n_cst_thread_trans.sru:L465-L466</c>]; the base task's
    /// unrolls to TWENTY [<c>n_cst_thread_task_sqlbase.sru:L690-L692</c>].
    /// </para>
    /// <para>
    /// <b>SO THIS CASE ASSERTS THE FLOOR AND DELIBERATELY ASSERTS NO CAP.</b> C# is natively variadic and
    /// a repeated protobuf field has no cap, so a twelfth parameter is not a behavioural regression - the
    /// twelfth simply had no overload to call. A test that pinned eleven as a maximum would convert a
    /// statement about the SOURCE LANGUAGE into a requirement about the contract, and would then have to
    /// be deleted the first time a caller sent twelve.
    /// </para>
    /// </remarks>
    /// <param name="parameterCount">How many positional parameters to send.</param>
    [Theory]
    [InlineData(8)]     // the transaction object's unrolled maximum
    [InlineData(11)]    // the SQLite binding's unrolled maximum
    [InlineData(12)]    // ⚠ ONE PAST IT, AND ACCEPTED
    [InlineData(20)]    // the base task's unrolled maximum
    [InlineData(21)]    // ⚠ ONE PAST THAT TOO, AND ALSO ACCEPTED
    public async Task TheContractAcceptsAtLeastTheLegacyMaximumAndImposesNoCeiling(int parameterCount)
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);

        // A statement with exactly as many placeholders as there are parameters, because an UNMATCHED
        // placeholder is a binding failure and would mask the arity question with a different refusal.
        string placeholders = string.Join(", ", Enumerable.Repeat("?", parameterCount));

        ExecRequest request = new()
        {
            Task = handle,
            Sql = string.Create(
                CultureInfo.InvariantCulture,
                $"INSERT INTO COMPANY (NAME) VALUES ({placeholders})"),
            Autocommit = AutoCommitMode.AcOff,
        };

        for (int ordinal = 1; ordinal <= parameterCount; ordinal++)
        {
            request.Parameters.Add(Positional((long)ordinal));
        }

        ExecResponse response = await harness.Commands.Exec(request, Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);

        // Every one of them was bound, in order - nothing was silently dropped at a boundary.
        Assert.Equal(parameterCount, harness.Engine.LastParameters.Count);
        Assert.Equal(
            Enumerable.Range(1, parameterCount).Select(static ordinal => (long)ordinal),
            harness.Engine.LastParameters.Select(static value => (long)value!));

        // The recorded ceilings, stated so they are discoverable, and asserted only as VALUES.
        Assert.Equal(11, SqlCommandTask.LegacySqliteExecArgumentCeiling);
        Assert.Equal(20, SqlCommandTask.LegacyDataStoreRetrieveArgumentCeiling);
        Assert.Equal(8, SqlCommandTask.LegacyTransactionRetrieveArgumentCeiling);
    }

    /// <summary>
    /// Persistence acquires NO coupling to the deferred ScriptBridge service, and none to any other
    /// service.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>n_scriptinvoker</c> IS NOT A DEPENDENCY, AND THE REASON IS WORTH STATING PRECISELY
    /// (constraint C-D).</b> The oracle does reference it - at
    /// <c>n_cst_thread_task_sqlbase.sru:L695-L701</c> and <c>n_cst_thread_trans.sru:L469-L475</c> - but in
    /// both places purely as a VARIADIC-CALL ESCAPE HATCH: the code <c>choose case</c>-unrolls the call to
    /// its maximum arity and falls back to dynamic invocation only PAST that maximum. C# forwards an
    /// arbitrary-length argument list natively, so the workaround has no analogue to port and the
    /// dynamic-invocation family stays where the plan puts it - in the deferred ScriptBridge service.
    /// </para>
    /// <para>
    /// <b>AND THE GENERATED CONTRACTS PROJECT IS THE ONLY CROSS-SERVICE COUPLING (constraint C-A).</b>
    /// Asserted by reflection over the assembly's own reference list rather than by reading the project
    /// file, because a transitive reference introduced through a shared library would not appear in the
    /// project file at all.
    /// </para>
    /// </remarks>
    [Fact]
    public void PersistenceReferencesNoDeferredServiceAndNoOtherService()
    {
        Assembly persistence = typeof(SqlCommandTask).Assembly;

        // The five in-family assemblies this service may depend on: the published contracts, plus the
        // four shared behaviour libraries. Nothing else in the PowerFramework family is permitted.
        //
        // Shared.Eventful is one of the five, and it is a REQUIREMENT rather than a convenience: AAP
        // 0.4.1 assigns the whole of ws_objects/pfw.thread.pbl.src to Persistence in scope, one of that
        // library's six objects is n_cst_threading_eventful.sru, and it derives from n_cst_eventful -
        // which is itself one of the two structural facts the AAP cites as proof the base belongs in a
        // shared in-scope library. Tasks/TaskProxies/ThreadingEventBroker.cs is that derived port.
        // Shared.Localization is deliberately NOT on this list: nothing in this service consumes
        // localized text, so it would be an unused coupling.
        string[] permitted =
        [
            "PowerFramework.Contracts",
            "PowerFramework.Shared.Kernel",
            "PowerFramework.Shared.Diagnostics",
            "PowerFramework.Shared.Containers",
            "PowerFramework.Shared.Eventful",
        ];

        IReadOnlyList<string> family =
        [
            .. persistence
                .GetReferencedAssemblies()
                .Select(static reference => reference.Name ?? string.Empty)
                .Where(static name => name.StartsWith("PowerFramework.", StringComparison.Ordinal)),
        ];

        Assert.NotEmpty(family);
        Assert.All(family, name => Assert.Contains(name, permitted));

        // No sibling or deferred service, named or transitively reached.
        string[] forbidden =
        [
            "PowerFramework.Gateway",
            "PowerFramework.DataServices",
            "PowerFramework.Security",
            "PowerFramework.DesignSystem",
            "PowerFramework.Documents",
            "PowerFramework.Integration",
            "PowerFramework.ScriptBridge",
            "PowerFramework.Shared.Localization",
        ];

        Assert.All(forbidden, name => Assert.DoesNotContain(name, family));

        // And no type inside this service stands in for the deferred dynamic-invocation family, under any
        // of the names the legacy uses for it.
        string[] deferredMarkers = ["ScriptInvoker", "ScriptBridge", "Sciter", "Blink", "WebView"];

        IReadOnlyList<string> offenders =
        [
            .. persistence
                .GetTypes()
                .Select(static type => type.Name)
                .Where(name => deferredMarkers.Any(
                    marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase))),
        ];

        Assert.Empty(offenders);
    }

    // ---------------------------------------------------------------------------------------------
    //  REGION 4 - C-08: SESSION CORRELATION, THE DESCRIPTOR MAPPING, AND THE WRITE-ONLY CREDENTIAL
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Session begin and end, auto-commit control, commit and rollback are all exposed, and each reaches
    /// the established connection.
    /// </summary>
    /// <remarks>
    /// These are the five verbs AAP 0.4.3 names for C-08, and they are the oracle's own transaction-object
    /// surface: <c>of_Connect</c> / <c>of_Disconnect</c> behind the session pair, the <c>AutoCommit</c>
    /// attribute, <c>of_Commit</c> and <c>of_Rollback</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L73-L79</c>]. Asserted by OBSERVED
    /// EFFECT on the engine rather than by presence of a method, because a handler that returned OK
    /// without reaching the connection would satisfy a presence check.
    /// </remarks>
    [Fact]
    public async Task TheFiveSessionVerbsAreExposedAndEachReachesTheConnection()
    {
        Harness harness = new();
        SessionHandle session = await OpenSession(harness);

        // BEGIN reached the connection: the descriptor was applied and the engine has a handle.
        Assert.Equal(1, harness.Engine.ApplyCalls);
        Assert.Equal(1, harness.Engine.Handle);

        // AUTO-COMMIT CONTROL, which is a BOOLEAN on this contract because the transaction object's own
        // attribute is boolean - see the case below for why that is not a second enum.
        SetTransactionAutoCommitResponse enabled = await harness.Transactions.SetAutoCommit(
            new SetTransactionAutoCommitRequest { Session = session, Autocommit = true },
            Context);

        Assert.Equal(WireRetCode.Ok, enabled.Status.RetCode);
        Assert.Equal([true], harness.Engine.AutoCommitWrites);

        // Back off, so the commit below is not refused by `if AutoCommit then return RetCode.FAILED`
        // [n_cst_thread_trans.sru:L240].
        SetTransactionAutoCommitResponse disabled = await harness.Transactions.SetAutoCommit(
            new SetTransactionAutoCommitRequest { Session = session, Autocommit = false },
            Context);

        Assert.Equal(WireRetCode.Ok, disabled.Status.RetCode);

        // COMMIT.
        CommitResponse commit = await harness.Transactions.Commit(
            new CommitRequest { Session = session },
            Context);

        Assert.Equal(WireRetCode.Ok, commit.Status.RetCode);
        Assert.Equal(1, harness.Engine.CommitCalls);

        // ROLLBACK.
        RollbackResponse rollback = await harness.Transactions.Rollback(
            new RollbackRequest { Session = session },
            Context);

        Assert.Equal(WireRetCode.Ok, rollback.Status.RetCode);
        Assert.Equal(1, harness.Engine.RollbackCalls);

        // END.
        EndSessionResponse end = await harness.Transactions.EndSession(
            new EndSessionRequest { Session = session },
            Context);

        Assert.Equal(WireRetCode.Ok, end.Status.RetCode);
        Assert.Equal(0, harness.Sessions.Count);
    }

    // ---------------------------------------------------------------------------------------------
    //  THE DESCRIPTOR'S autocommit MEMBER - REFUSED, NEVER APPLIED, NEVER PART OF THE POOL KEY
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A begin request whose descriptor sets <c>autocommit</c> is refused <c>E_INVALID_ARGUMENT</c>,
    /// no session is minted, no pool reference is taken and no connection is opened.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>ACCEPTING IT SILENTLY WAS A LOST UPDATE REPORTED AS A SUCCESS, AND THAT IS WHAT THIS PINS.</b>
    /// The oracle erases the member before it can reach anything - <c>_transData.AutoCommit = false</c>
    /// under 擦除连接目标无关的参数
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L118-L119</c>] - and its
    /// descriptor-to-transaction transfer is the SEVEN-field copy that touches neither <c>autocommit</c>
    /// nor <c>userparm</c> [<c>n_cst_thread_trans.sru:L343-L354</c>]. Because <c>of_addref</c> keys pool
    /// entries on WHOLE-descriptor equality, a session that kept the flag resolved to a DIFFERENT pooled
    /// connection from the tasks created on it, which acquire with the erased descriptor: the caller's
    /// commit met the auto-committing engine and was refused by the oracle's own guard, while its write sat
    /// in the other engine's explicit transaction and was rolled back when the task's lease dropped.
    /// </para>
    /// <para>
    /// The refusal is AAP §0.1.5 applied literally - narrow with a defined error rather than widen with a
    /// guess - and it is asserted by EFFECT as well as by code: nothing was applied, nothing connected and
    /// the registry stayed empty, because a refusal that still took a pool reference would leak one.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ADescriptorThatSetsAutoCommitIsRefusedAndNothingIsAcquired()
    {
        Harness harness = new();

        TransactionDescriptor descriptor = Descriptor();
        descriptor.Autocommit = true;

        BeginSessionResponse response = await harness.Transactions.BeginSession(
            new BeginSessionRequest { Descriptor_ = descriptor },
            Context);

        Assert.Equal(WireRetCode.EInvalidArgument, response.Status.RetCode);

        // The refusal NAMES the member and every supported route to the same effect, so a caller can act
        // on it, and quotes no handle or credential.
        Assert.Contains("autocommit", response.Status.ErrorText, StringComparison.Ordinal);
        Assert.Contains("SetAutoCommit", response.Status.ErrorText, StringComparison.Ordinal);
        Assert.Contains("UpdateRequest", response.Status.ErrorText, StringComparison.Ordinal);
        Assert.Contains("ExecRequest", response.Status.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain(SentinelLogPass, response.Status.ErrorText, StringComparison.Ordinal);

        // Nothing was acquired: no session handle, no descriptor application, no connect, no pool entry.
        Assert.Null(response.Session);
        Assert.Equal(0, harness.Engine.ApplyCalls);
        Assert.Equal(0, harness.Engine.Handle);
        Assert.Equal(0, harness.Sessions.Count);
        Assert.Equal(0, harness.Pool.UpperBound);
    }

    /// <summary>
    /// An explicitly false <c>autocommit</c> member is admitted, because false and absent are the same
    /// request and both ask for the default.
    /// </summary>
    /// <remarks>
    /// The member is a plain proto3 <c>bool</c>, so the guard can only distinguish true from
    /// not-true - which is exactly the distinction that matters, since only true asks for behaviour the
    /// oracle has no route for. The fixture descriptor already sets it false, so this is the ordinary path
    /// every other case in this file takes; asserted explicitly so a future tightening of the guard into
    /// "the field was present" fails here rather than in production.
    /// </remarks>
    [Fact]
    public async Task AnExplicitlyFalseAutoCommitMemberIsAdmitted()
    {
        Harness harness = new();

        TransactionDescriptor descriptor = Descriptor();
        descriptor.Autocommit = false;

        BeginSessionResponse response = await harness.Transactions.BeginSession(
            new BeginSessionRequest { Descriptor_ = descriptor },
            Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
        Assert.NotNull(response.Session);
    }

    /// <summary>
    /// The descriptor never moves the engine's auto-commit mode: the seven-field transfer does not carry
    /// the member, so only <c>SetAutoCommit</c> writes it.
    /// </summary>
    /// <remarks>
    /// The oracle's transfer copies dbms, servername, database, logid, logpass, dbparm and lock
    /// [<c>n_cst_thread_trans.sru:L343-L354</c>] and touches neither <c>autocommit</c> nor
    /// <c>userparm</c>; the pooled transaction's own interface says the same in its contract. An engine
    /// that adopted the member from a descriptor was the second half of the lost update, because it made
    /// the session's mode disagree with the mode its tasks' connection was in. Asserted through the
    /// recorded writes rather than the flag's value, so an adoption that happened to write the same value
    /// still fails.
    /// </remarks>
    [Fact]
    public async Task ApplyingADescriptorWritesNoAutoCommitMode()
    {
        Harness harness = new();
        SessionHandle session = await OpenSession(harness);

        Assert.Equal(1, harness.Engine.ApplyCalls);
        Assert.Empty(harness.Engine.AutoCommitWrites);

        // The one member that does move it, so the emptiness above is a fact about the descriptor path
        // rather than about a double that ignores the mode altogether.
        SetTransactionAutoCommitResponse moved = await harness.Transactions.SetAutoCommit(
            new SetTransactionAutoCommitRequest { Session = session, Autocommit = true },
            Context);

        Assert.Equal(WireRetCode.Ok, moved.Status.RetCode);
        Assert.Equal([true], harness.Engine.AutoCommitWrites);
    }

    /// <summary>
    /// A commit and a rollback on an auto-committing session answer the oracle's plain <c>FAILED</c> WITH
    /// a diagnostic beside it, rather than a bare code and an empty string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The CODE is the oracle's and is unchanged: <c>if AutoCommit then return RetCode.FAILED</c> is the
    /// first test in both verbs [<c>n_cst_thread_trans.sru:L185</c>, <c>:L240</c>]. What is asserted here
    /// is the text, in a field PowerScript has no channel for - a function returning <c>long</c> cannot
    /// carry one - because a bare <c>-1</c> on a refusal that means "your work was never in a transaction"
    /// is unreadable, and reading it was how a caller lost a write without learning anything.
    /// </para>
    /// <para>
    /// The engine is NOT reached on either arm, which is the guard's own shape: the oracle returns before
    /// it touches the transaction.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task CommitAndRollbackUnderAutoCommitCarryADiagnosticBesideTheOraclesFailedCode()
    {
        Harness harness = new();
        SessionHandle session = await OpenSession(harness);

        SetTransactionAutoCommitResponse enabled = await harness.Transactions.SetAutoCommit(
            new SetTransactionAutoCommitRequest { Session = session, Autocommit = true },
            Context);

        Assert.Equal(WireRetCode.Ok, enabled.Status.RetCode);

        CommitResponse commit = await harness.Transactions.Commit(
            new CommitRequest { Session = session },
            Context);

        Assert.Equal(WireRetCode.Failed, commit.Status.RetCode);
        Assert.NotEmpty(commit.Status.ErrorText);
        Assert.Contains("auto-committing", commit.Status.ErrorText, StringComparison.Ordinal);
        Assert.Contains("SetAutoCommit", commit.Status.ErrorText, StringComparison.Ordinal);
        Assert.Null(commit.Status.DbError);

        RollbackResponse rollback = await harness.Transactions.Rollback(
            new RollbackRequest { Session = session },
            Context);

        Assert.Equal(WireRetCode.Failed, rollback.Status.RetCode);
        Assert.Contains("auto-committing", rollback.Status.ErrorText, StringComparison.Ordinal);

        // The oracle returns before it reaches the transaction on both arms.
        Assert.Equal(0, harness.Engine.CommitCalls);
        Assert.Equal(0, harness.Engine.RollbackCalls);
    }

    /// <summary>
    /// The wire session handle correlates to the ONE-BASED pool reference index, and that index addresses
    /// the very connection the session established.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>ONE-BASED, BECAUSE THE ORACLE APPENDS AT <c>UpperBound + 1</c> AND RETURNS THAT
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L143-L151</c>].</b> The first
    /// entry is index ONE, not zero, and the pool's own guards treat a non-positive index as out of bounds
    /// rather than as "the first entry" [<c>:L89</c>, <c>:L120</c>, <c>:L154</c>]. This is the single most
    /// dangerous class of mechanical error in the whole refactor (AAP 0.4.5.4), so it is asserted
    /// directly.
    /// </para>
    /// <para>
    /// <b>THE HANDLE IS AN OPAQUE STRING AND THE INDEX IS AN INTEGER, AND THAT IS THE CORRELATION RATHER
    /// THAN A CONTRADICTION.</b> Publishing the raw index as the handle would let a caller address another
    /// caller's session by guessing a small number, so the boundary issues an unguessable identifier and
    /// keeps the one-based index behind it - and the correlation asserted here is the one that matters:
    /// resolving the handle yields a LIVE lease, the pool answers that lease and the equal-descriptor
    /// index with the SAME connection instance, and the reference count is what the session took.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheSessionHandleCorrelatesToTheOneBasedPoolReferenceIndex()
    {
        Harness harness = new();
        SessionHandle handle = await OpenSession(harness);

        // The handle is opaque and unguessable - it is not the index rendered as text.
        Assert.NotEmpty(handle.SessionId);
        Assert.False(int.TryParse(handle.SessionId, CultureInfo.InvariantCulture, out _));

        Assert.True(harness.Sessions.TryResolve(handle, out TransactionSession? session));
        Assert.NotNull(session);

        // The lease behind it is LIVE and positive.
        Assert.True(session.Lease.IsValid);
        Assert.True(session.Lease.Id > 0L);
        Assert.True(harness.Pool.IsLeaseLive(session.Lease));

        // The pool holds exactly one entry, so the one-based index of this session's entry is ONE.
        Assert.Equal(1, harness.Pool.UpperBound);

        // Addressing the pool BY THAT ONE-BASED INDEX yields the very connection the session borrowed.
        Assert.Equal(RetCode.OK, harness.Pool.Get(1, out IPooledTransaction? byIndex));
        Assert.Same(session.Transaction, byIndex);

        // And addressing it by the lease yields the same instance, which is what makes the opaque handle
        // and the positional index two views of one entry rather than two entries.
        Assert.Equal(RetCode.OK, harness.Pool.Get(session.Lease, out IPooledTransaction? byLease));
        Assert.Same(byIndex, byLease);

        // The reference the session took: AddRef on an EQUAL descriptor finds the existing entry and
        // returns its one-based index rather than appending a second one - whole-descriptor equality keys
        // the pool [n_cst_thread_trans_pool.sru:L138].
        Assert.Equal(1, harness.Pool.AddRef(session.Descriptor));
        Assert.Equal(1, harness.Pool.UpperBound);

        // Balanced again, so this assertion left the reference count as it found it.
        Assert.Equal(RetCode.OK, harness.Pool.RemoveRef(1));

        // ONE HANDLE TYPE ADDRESSES ALL THREE TASK CONTRACTS, which is what makes this correlation shared
        // rather than per-contract: C-05, C-06 and C-07 all create their task FROM a session handle, so the
        // mapping asserted above is the one every request carrying that handle resolves through. Asserted
        // on the generated contract rather than by driving the other two services, whose own suites own
        // their behaviour.
        Assert.Equal(
            typeof(SessionHandle),
            typeof(CreateQueryTaskRequest).GetProperty("Session")!.PropertyType);
        Assert.Equal(
            typeof(SessionHandle),
            typeof(CreateUpdateTaskRequest).GetProperty("Session")!.PropertyType);
        Assert.Equal(
            typeof(SessionHandle),
            typeof(CreateCommandTaskRequest).GetProperty("Session")!.PropertyType);
    }

    /// <summary>
    /// The pool refuses index ZERO and every non-positive index, because zero is an ERROR and not "the
    /// first entry".
    /// </summary>
    /// <remarks>
    /// All three of the oracle's positional entry points open with the identical guard -
    /// <c>if refIndex &lt;= 0 or refIndex &gt; UpperBound(_transactions) then return RetCode.E_OUT_OF_BOUND</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L89</c>, <c>:L120</c>,
    /// <c>:L154</c>] - so a zero-based caller does not silently address the first entry, it gets
    /// <see cref="RetCode.E_OUT_OF_BOUND"/>. The boundary states the same invariant on the lease it
    /// issues: an invalid lease answers that code rather than opening a session.
    /// </remarks>
    /// <param name="refIndex">The index to address the pool with.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(2)]
    public async Task ThePoolRefusesANonPositiveOrOutOfRangeIndex(int refIndex)
    {
        Harness harness = new();
        _ = await OpenSession(harness);

        // Exactly one entry exists, so ONE is the only valid index.
        Assert.Equal(1, harness.Pool.UpperBound);

        Assert.Equal(RetCode.E_OUT_OF_BOUND, harness.Pool.Get(refIndex, out IPooledTransaction? absent));
        Assert.Null(absent);

        Assert.Equal(RetCode.E_OUT_OF_BOUND, harness.Pool.RemoveRef(refIndex));
        Assert.Equal(-12L, RetCode.E_OUT_OF_BOUND);

        // The one valid index still resolves, so the guard rejected the address and not the entry.
        Assert.Equal(RetCode.OK, harness.Pool.Get(1, out IPooledTransaction? present));
        Assert.NotNull(present);
    }

    /// <summary>
    /// An EMPTY or UNKNOWN session handle is refused with a documented code and a diagnostic - never by
    /// silently creating a session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>SILENTLY CREATING A SESSION WOULD BE THE WORST AVAILABLE BEHAVIOUR</b>, because the caller would
    /// then be operating on a connection nobody is serializing and against a descriptor nobody supplied.
    /// The refusal code is <see cref="RetCode.E_INVALID_TRANSACTION"/>, which is the oracle's own code for
    /// a transaction that cannot be resolved
    /// [<c>ws_objects/pfw.shared.pbl.src/retcode.sru:L50</c>], and every session-addressed verb on the
    /// contract answers it the same way.
    /// </para>
    /// <para>
    /// The empty handle is the wire's equivalent of index zero: a default-constructed
    /// <c>SessionHandle</c> carries the empty string, so "handle 0" and "no handle" arrive as the same
    /// message and both are rejected.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnEmptyOrUnknownSessionHandleIsRefusedRatherThanSilentlyCreated()
    {
        Harness harness = new();

        // Nothing has been opened, and nothing below may open anything.
        Assert.Equal(0, harness.Sessions.Count);
        Assert.Equal(0, harness.Pool.UpperBound);

        SessionHandle[] rejected =
        [
            new SessionHandle(),                                      // the wire's "handle 0"
            new SessionHandle { SessionId = string.Empty },            // stated explicitly
            new SessionHandle { SessionId = "not-a-live-session" },    // well formed, unknown
        ];

        foreach (SessionHandle handle in rejected)
        {
            GetTransactionDataResponse descriptor = await harness.Transactions.GetTransactionData(
                new GetTransactionDataRequest { Session = handle },
                Context);
            Assert.Equal(WireRetCode.EInvalidTransaction, descriptor.Status.RetCode);
            Assert.NotEmpty(descriptor.Status.ErrorText);

            CommitResponse commit = await harness.Transactions.Commit(
                new CommitRequest { Session = handle },
                Context);
            Assert.Equal(WireRetCode.EInvalidTransaction, commit.Status.RetCode);

            RollbackResponse rollback = await harness.Transactions.Rollback(
                new RollbackRequest { Session = handle },
                Context);
            Assert.Equal(WireRetCode.EInvalidTransaction, rollback.Status.RetCode);

            SetTransactionAutoCommitResponse mode = await harness.Transactions.SetAutoCommit(
                new SetTransactionAutoCommitRequest { Session = handle, Autocommit = true },
                Context);
            Assert.Equal(WireRetCode.EInvalidTransaction, mode.Status.RetCode);

            EndSessionResponse end = await harness.Transactions.EndSession(
                new EndSessionRequest { Session = handle },
                Context);
            Assert.Equal(WireRetCode.EInvalidTransaction, end.Status.RetCode);

            // C-07's task creation is addressed by a session handle too, and refuses the same way.
            CreateCommandTaskResponse created = await harness.Commands.CreateCommandTask(
                new CreateCommandTaskRequest { Session = handle },
                Context);
            Assert.NotEqual(WireRetCode.Ok, created.Status.RetCode);
        }

        // Nothing was created by any of the eighteen refusals above - three handles across six verbs.
        Assert.Equal(0, harness.Sessions.Count);
        Assert.Equal(0, harness.Pool.UpperBound);
        Assert.Equal(0, harness.Engine.ApplyCalls);
        Assert.Equal(0, harness.Engine.Handle);
    }

    /// <summary>
    /// A command task carrying a live session handle reaches the connection that session established, and
    /// the session's end releases the reference and purges the task.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THIS IS THE "THE RPC DRIVES THE LIFECYCLE" ASSERTION, NOT A RE-TEST OF THE MECHANICS.</b>
    /// Reference counting and idle expiry belong to <c>TransactionPoolTests</c> and
    /// <c>TransactionPoolIdleSweeperTests</c>; what only appears HERE is that the wire verbs are wired to
    /// them - that <c>EndSession</c> releases the pool reference the session took
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L120-L131</c>, where a release at
    /// a reference count of one disconnects] and purges the command tasks bound to the closing session.
    /// </para>
    /// <para>
    /// The connection identity is asserted by SAMENESS rather than by equality, because the whole point of
    /// a descriptor-keyed pool is that a task and its session borrow ONE instance: a task that resolved its
    /// own connection would pass an equality check and still be executing outside the session's gate.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ACommandTaskReachesTheSessionsConnectionAndTheSessionsEndReleasesIt()
    {
        Harness harness = new();
        SessionHandle session = await OpenSession(harness);
        TaskHandle task = await CreateTask(harness, session);

        Assert.Equal(1, harness.Sessions.Count);
        Assert.Equal(1, harness.Tasks.Count);

        ExecResponse response = await harness.Commands.Exec(
            new ExecRequest
            {
                Task = task,
                Sql = "UPDATE COMPANY SET AGE = 40 WHERE ID = 1",
                Autocommit = AutoCommitMode.AcOff,
            },
            Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);

        // ONE connection, shared. The task resolved the SAME instance the session borrowed, which is what
        // makes the session's gate meaningful.
        Assert.True(harness.Sessions.TryResolve(session, out TransactionSession? resolved));
        Assert.NotNull(resolved);
        Assert.Same(resolved.Transaction, Resolve(harness, task).Worker.AttachedTransaction);

        // The connection was established ONCE, by BeginSession, and the task did not re-establish it.
        Assert.Equal(1, harness.Engine.ApplyCalls);
        Assert.Equal(1, harness.Engine.Handle);

        EndSessionResponse end = await harness.Transactions.EndSession(
            new EndSessionRequest { Session = session },
            Context);

        Assert.Equal(WireRetCode.Ok, end.Status.RetCode);

        // The reference was released - at a count of one the pool disconnects, which the engine records by
        // dropping its handle - and the entry is gone.
        Assert.Equal(0, harness.Engine.Handle);
        Assert.Equal(0, harness.Pool.UpperBound);
        Assert.False(harness.Pool.IsLeaseLive(resolved.Lease));

        // And the command task bound to that session was purged with it, so no handle outlives its
        // connection.
        Assert.Equal(0, harness.Tasks.Count);
        Assert.False(harness.Tasks.TryResolve(task, out _));

        // A request on the purged task handle is refused rather than resurrecting anything.
        ExecResponse afterEnd = await harness.Commands.Exec(
            new ExecRequest { Task = task, Sql = "SELECT 1" },
            Context);

        Assert.Equal(WireRetCode.EInvalidHandle, afterEnd.Status.RetCode);
        Assert.Equal(0, harness.Pool.UpperBound);
    }

    /// <summary>
    /// The NINE-FIELD descriptor maps on and off the wire in the oracle's own declaration order, with the
    /// boolean EIGHTH and <c>userparm</c> NINTH.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE ORDER IS THE CONTRACT, AND IT IS NOT ALPHABETICAL OR TYPE-GROUPED.</b> The structure declares
    /// <c>dbms</c>, <c>servername</c>, <c>database</c>, <c>logid</c>, <c>logpass</c>, <c>dbparm</c>,
    /// <c>lock</c>, <c>autocommit</c>, <c>userparm</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/transactiondata.srs:L4-L12</c>] - so the boolean sits at
    /// position EIGHT with a string after it, which is exactly the shape a reader "tidying" the message
    /// would move. The protobuf field numbers pin the wire order and this case pins the mapping.
    /// </para>
    /// <para>
    /// <b>THREE OF THE NINE ARE DELIBERATELY ABSENT FROM THE OUTBOUND VIEW, AND EACH FOR ITS OWN
    /// REASON.</b> <c>logpass</c> is the credential; <c>dbparm</c> is a connection string treated as
    /// sensitive and replaced by a typed, closed flag allowlist; <c>userparm</c> is withheld because
    /// nothing in the oracle ever reads it - not the seven-field transfer pair
    /// [<c>n_cst_thread_trans.sru:L343-L354</c>, <c>:L402-L419</c>], not the proxy copy, not the six-string
    /// overload - while it still participates in the pool's whole-descriptor equality key. The contract
    /// states that absence structurally by RESERVING slots 5, 6 and 9 of the view, so no future field can
    /// silently occupy them.
    /// </para>
    /// <para>
    /// <c>autocommit</c> is CAPTURED and never applied by the transaction object's own transfer, and the
    /// task layer erases it outright - preserved, not corrected (constraint C-B).
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheNineFieldDescriptorMapsInTheOraclesDeclarationOrder()
    {
        TransactionDescriptor sent = Descriptor();

        // The request-side message carries all nine, at field numbers 1 through 9.
        Assert.Equal(9, TransactionDescriptor.Descriptor.Fields.InFieldNumberOrder().Count);
        Assert.Equal(
            ["dbms", "servername", "database", "logid", "logpass", "dbparm", "lock", "autocommit", "userparm"],
            TransactionDescriptor.Descriptor.Fields
                .InFieldNumberOrder()
                .Select(static field => field.Name));

        Harness harness = new();
        SessionHandle session = await OpenSession(harness);

        GetTransactionDataResponse response = await harness.Transactions.GetTransactionData(
            new GetTransactionDataRequest { Session = session },
            Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
        Assert.NotNull(response.Descriptor_);

        TransactionDescriptorView view = response.Descriptor_;

        // The six that round-trip, each read back as it was sent.
        Assert.Equal(sent.Dbms, view.Dbms);
        Assert.Equal(sent.Servername, view.Servername);
        Assert.Equal(sent.Database, view.Database);
        Assert.Equal(sent.Logid, view.Logid);
        Assert.Equal(sent.Lock, view.Lock);
        Assert.Equal(sent.Autocommit, view.Autocommit);

        // The three that are withheld are withheld STRUCTURALLY: their slots are reserved, so the
        // generated view has no property for them at all and this is a compile-time guarantee rather than
        // a runtime blank.
        IReadOnlyList<string> viewFields =
        [
            .. TransactionDescriptorView.Descriptor.Fields
                .InFieldNumberOrder()
                .Select(static field => field.Name),
        ];

        Assert.DoesNotContain("logpass", viewFields);
        Assert.DoesNotContain("dbparm", viewFields);
        Assert.DoesNotContain("userparm", viewFields);

        // And the in-process descriptor kept all nine, because the pool keys on the WHOLE descriptor.
        Assert.True(harness.Sessions.TryResolve(session, out TransactionSession? resolved));
        Assert.NotNull(resolved);
        Assert.Equal(sent.Userparm, resolved.Descriptor.UserParm);
        Assert.Equal(sent.Dbparm, resolved.Descriptor.DbParm);
    }

    /// <summary>
    /// ⚠ The log password is STRICTLY INBOUND: the connection receives it, and it appears in NO response,
    /// NO log record, NO <c>ToString()</c> and NO error payload.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THIS IS CONSTRAINT C-F ON THE C-08 BOUNDARY, AND IT CANNOT BE DELEGATED TO
    /// <c>TransactionDataTests</c>, WHICH OWNS ONLY THE TYPE.</b> What has to be proven here is a property
    /// of the WHOLE ADAPTER: that no path from a request carrying the credential to anything leaving the
    /// process reproduces it. So the assertion greps every captured artefact rather than inspecting one
    /// field - the two response messages in full, the ordered log sink, the descriptor's own rendering, and
    /// the error payload of a deliberately failed command.
    /// </para>
    /// <para>
    /// <b>THE ORACLE ECHOES IT AND THE PORT DOES NOT, WHICH IS A DELIBERATE NARROWING RATHER THAN A
    /// DIVERGENCE.</b> In process, <c>of_getTransData</c> assigns <c>data.LogPass = LogPass</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L414</c>] - which is harmless when the
    /// caller is the same process that supplied it and is a credential disclosure the moment the answer
    /// crosses a network. AAP 0.4.3 therefore specifies the field write-only on this contract, and the
    /// narrowing is closed three independent ways: the view reserves the slot, the in-process type has no
    /// getter, and nothing logs it.
    /// </para>
    /// <para>
    /// <b>BOTH HALVES, BECAUSE EITHER ALONE WOULD BE THE WRONG TEST.</b> A port that never accepted the
    /// password at all would pass every absence assertion and be unable to connect - so the first
    /// assertion below is that the CONNECTION RECEIVED IT, read through the descriptor's internal reveal
    /// door, which is the only door there is.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheLogPasswordReachesTheConnectionAndNothingElse()
    {
        Harness harness = new();
        SessionHandle session = await OpenSession(harness);
        TaskHandle task = await CreateTask(harness, session);

        // -----------------------------------------------------------------------------------------
        //  HALF ONE - THE CONNECTION RECEIVED IT.
        // -----------------------------------------------------------------------------------------
        Assert.Equal(SentinelLogPass, harness.Engine.AppliedLogPass);
        Assert.True(harness.Sessions.TryResolve(session, out TransactionSession? resolved));
        Assert.NotNull(resolved);
        Assert.True(resolved.Descriptor.HasCredential);

        // -----------------------------------------------------------------------------------------
        //  HALF TWO - NOTHING ELSE DID. Every artefact below is searched in full.
        // -----------------------------------------------------------------------------------------
        GetTransactionDataResponse descriptor = await harness.Transactions.GetTransactionData(
            new GetTransactionDataRequest { Session = session },
            Context);

        Assert.Equal(WireRetCode.Ok, descriptor.Status.RetCode);

        // A deliberately failing command, so the error payload is populated and can be searched too.
        harness.Engine.ExecuteResult = SqlState.Failed(19, "the fixture refuses this statement");

        ExecResponse exec = await harness.Commands.Exec(
            new ExecRequest
            {
                Task = task,
                Sql = "INSERT INTO COMPANY (NAME) VALUES (?)",
                Parameters = { Positional(BoundLiteral) },
                Autocommit = AutoCommitMode.AcOn,
            },
            Context);

        Assert.Equal(WireRetCode.EDbError, exec.Status.RetCode);
        Assert.NotNull(exec.Status.DbError);

        CommitResponse commit = await harness.Transactions.Commit(
            new CommitRequest { Session = session },
            Context);

        EndSessionResponse end = await harness.Transactions.EndSession(
            new EndSessionRequest { Session = session },
            Context);

        // THE GREP. Whole messages, rendered in full - not one field of one of them.
        string[] outbound =
        [
            descriptor.ToString(),
            descriptor.Descriptor_.ToString(),
            exec.ToString(),
            exec.Status.ToString(),
            exec.Status.DbError.ToString(),
            commit.ToString(),
            end.ToString(),

            // The in-process descriptor's own rendering, which a hand-rolled diagnostic would reach for.
            resolved.Descriptor.ToString(),
        ];

        Assert.All(
            outbound,
            rendered => Assert.DoesNotContain(SentinelLogPass, rendered, StringComparison.Ordinal));

        // Every log record every collaborator emitted, in full.
        Assert.NotEmpty(harness.Log);
        Assert.DoesNotContain(
            harness.Log,
            record => record.Contains(SentinelLogPass, StringComparison.Ordinal));

        // The account NAME is not a secret and legitimately survives, which is what makes the assertion
        // above about the CREDENTIAL rather than about the descriptor being emptied wholesale.
        Assert.Contains("fixture-account", descriptor.Descriptor_.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The two <c>DBParm</c> flags are carried as a typed allowlist, and the connection string itself is
    /// not echoed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The oracle extracts both flags from the connection string by regular expression -
    /// <c>DisableBind</c> and <c>NCharBind</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L128-L129</c>] - and the PARSE
    /// itself is asserted by <c>SqlTaskBaseTests</c>. What belongs here is that C-08 CARRIES the resolved
    /// flags, so a consumer no longer has to re-implement those expressions over an opaque string.
    /// </para>
    /// <para>
    /// <b>⚠ THE SECURITY NOTE THIS FLAG EXISTS TO MAKE VISIBLE.</b> <c>DisableBind=1</c> means the legacy
    /// runtime USES NO BIND VARIABLES - values are interpolated into the statement text - and that
    /// interpolation is the mechanical root of the injection exposure analysed in AAP 0.6.4. The port
    /// executes through bound parameters regardless, while preserving the interpolated text as the
    /// observable artefact, so the flag is reported rather than obeyed.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheTwoConnectionFlagsAreCarriedWithoutEchoingTheConnectionString()
    {
        Harness harness = new();
        SessionHandle session = await OpenSession(harness);

        GetTransactionDataResponse response = await harness.Transactions.GetTransactionData(
            new GetTransactionDataRequest { Session = session },
            Context);

        Assert.Equal(WireRetCode.Ok, response.Status.RetCode);
        Assert.NotNull(response.Descriptor_.Flags);

        // The fixture's DBParm sets both, so both must read true.
        Assert.True(response.Descriptor_.Flags.DisableBind);
        Assert.True(response.Descriptor_.Flags.NcharBind);

        // The in-process descriptor agrees, through its own resolver.
        Assert.True(harness.Sessions.TryResolve(session, out TransactionSession? resolved));
        Assert.NotNull(resolved);
        resolved.Descriptor.ResolveDbParmFlags(out bool disableBind, out bool ncharBind);
        Assert.True(disableBind);
        Assert.True(ncharBind);

        // The connection received the string, and the response did not echo it.
        Assert.Equal("DisableBind=1,NCharBind=1", harness.Engine.AppliedDbParm);
        Assert.DoesNotContain("DisableBind", response.Descriptor_.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// C-08's auto-commit control writes the very connection flag <c>AC_NATIVE</c> toggles - ONE flag with
    /// ONE meaning, and no second enum.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>WHY C-08's CONTROL IS A BOOLEAN WHILE C-07's MODE IS A TRI-VALUED ENUM, AND WHY THAT IS
    /// CORRECT.</b> They are not two spellings of one concept. The transaction object's
    /// <c>AutoCommit</c> is a BOOLEAN ATTRIBUTE of the connection, written directly by the oracle -
    /// <c>transObject.AutoCommit = true</c> [<c>:L89</c>] - whereas <c>AC_OFF</c> / <c>AC_ON</c> /
    /// <c>AC_NATIVE</c> is a THREE-WAY CHOICE OF EPILOGUE that a command task makes, and only one of its
    /// three arms touches the boolean at all. Declaring C-08's control as the enum would imply a
    /// connection can be in a "commit afterwards" state, which is not a property a connection has.
    /// </para>
    /// <para>
    /// <b>WHAT IS SHARED IS THE FLAG ITSELF, AND THAT IS WHAT THIS CASE PROVES.</b> Setting it through
    /// C-08 and then observing <c>AC_NATIVE</c>'s inversion through C-07 shows both writes landing on one
    /// piece of connection state, in one ordered history - so there is exactly one flag, exactly one
    /// generated <c>AC_*</c> enum, and no local re-declaration of either (constraint 0.7.2).
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheSessionAutoCommitControlAndTheNativeModeWriteOneSharedFlag()
    {
        Harness harness = new();
        SessionHandle session = await OpenSession(harness);
        TaskHandle task = await CreateTask(harness, session);

        harness.Engine.AutoCommitWrites.Clear();

        // C-08 writes the flag directly: a boolean, because the connection attribute is a boolean.
        Assert.Equal(
            typeof(bool),
            typeof(SetTransactionAutoCommitRequest).GetProperty("Autocommit")!.PropertyType);

        SetTransactionAutoCommitResponse control = await harness.Transactions.SetAutoCommit(
            new SetTransactionAutoCommitRequest { Session = session, Autocommit = true },
            Context);

        Assert.Equal(WireRetCode.Ok, control.Status.RetCode);
        Assert.True(harness.Engine.AutoCommit);

        // Put it back, so the native inversion below starts from the state it expects.
        SetTransactionAutoCommitResponse restore = await harness.Transactions.SetAutoCommit(
            new SetTransactionAutoCommitRequest { Session = session, Autocommit = false },
            Context);

        Assert.Equal(WireRetCode.Ok, restore.Status.RetCode);

        // C-07's AC_NATIVE writes the SAME flag, raising and lowering it around the statement.
        ExecResponse exec = await harness.Commands.Exec(
            new ExecRequest
            {
                Task = task,
                Sql = "INSERT INTO COMPANY (NAME) VALUES ('fixture')",
                Autocommit = AutoCommitMode.AcNative,
            },
            Context);

        Assert.Equal(WireRetCode.Ok, exec.Status.RetCode);

        // One ordered history over one flag: C-08's two writes, then C-07's inversion pair.
        Assert.Equal([true, false, true, false], harness.Engine.AutoCommitWrites);
        Assert.False(harness.Engine.AutoCommit);

        // And the shared flag is the pooled transaction's own property, not a copy either side keeps.
        Assert.True(harness.Sessions.TryResolve(session, out TransactionSession? resolved));
        Assert.NotNull(resolved);
        Assert.False(resolved.Transaction.AutoCommit);
    }

    // ---------------------------------------------------------------------------------------------
    //  REGION 5 - THE CONSTRAINTS THIS FILE IS WRITTEN AGAINST, ASSERTED RATHER THAN ASSERTED-IN-PROSE
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Both surfaces still carry the authorization metadata the composition root relies on - this suite
    /// bypasses nothing and constructs no key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>WHY THIS ASSERTION EXISTS AT ALL (constraint C-G).</b> Every case above invokes a handler as a
    /// METHOD, which is a unit-test call and not a pipeline call: attribute-driven authorization is
    /// enforced by the gRPC pipeline, so a direct invocation never consults it. That is the correct way to
    /// unit-test behaviour, and it has one hazard - the metadata could be REMOVED and every case above
    /// would still pass. So the metadata is asserted here, directly, and no signing key is constructed
    /// anywhere in this file to "make authentication work".
    /// </para>
    /// <para>
    /// The two shapes differ deliberately and both are checked: C-07 carries ONE class-level requirement
    /// because every one of its methods mutates, while C-08 carries a PER-METHOD requirement because its
    /// surface genuinely splits into readers and writers.
    /// </para>
    /// </remarks>
    [Fact]
    public void BothSurfacesKeepTheirAuthorizationMetadata()
    {
        // C-07: one class-level policy, and it is the WRITE policy.
        AuthorizeAttribute[] commandPolicies =
            [.. typeof(CommandService).GetCustomAttributes<AuthorizeAttribute>(inherit: false)];

        Assert.Single(commandPolicies);
        Assert.Equal(PersistenceAuthorizationPolicies.Write, commandPolicies[0].Policy);

        // C-08: per-method policies, with the readers and the writers separated.
        AssertPolicy(nameof(TransactionService.BeginSession), PersistenceAuthorizationPolicies.Read);
        AssertPolicy(nameof(TransactionService.EndSession), PersistenceAuthorizationPolicies.Read);
        AssertPolicy(nameof(TransactionService.GetTransactionData), PersistenceAuthorizationPolicies.Read);
        AssertPolicy(nameof(TransactionService.SetAutoCommit), PersistenceAuthorizationPolicies.Write);
        AssertPolicy(nameof(TransactionService.AutoCommit), PersistenceAuthorizationPolicies.Write);
        AssertPolicy(nameof(TransactionService.Commit), PersistenceAuthorizationPolicies.Write);
        AssertPolicy(nameof(TransactionService.Rollback), PersistenceAuthorizationPolicies.Write);

        // The policy names themselves, so a rename cannot silently detach the handlers from the
        // registrations in Program.cs.
        Assert.Equal("persistence.read", PersistenceAuthorizationPolicies.Read);
        Assert.Equal("persistence.write", PersistenceAuthorizationPolicies.Write);

        static void AssertPolicy(string method, string expected)
        {
            AuthorizeAttribute? attribute = typeof(TransactionService)
                .GetMethod(method, BindingFlags.Instance | BindingFlags.Public)!
                .GetCustomAttribute<AuthorizeAttribute>(inherit: false);

            Assert.NotNull(attribute);
            Assert.Equal(expected, attribute.Policy);
        }
    }

    /// <summary>
    /// Nothing in this suite touches a database, and the clock never moves unless a case moves it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>NO FABRICATED DATABASE (constraint C-E).</b> The whole flow below - open a session, create a
    /// task, execute a statement, commit, end - runs against a recording engine, so the connection seam is
    /// the ONLY faked layer and no SQLite connection, no dialect client and no container is involved. The
    /// engine identity is asserted through the pooled transaction's own resolver, which is the strongest
    /// available form of the claim: not "we did not ask for a database" but "the engine that was used is
    /// this double".
    /// </para>
    /// <para>
    /// <b>DETERMINISM (AAP 0.6.7).</b> One injected clock is the only time source, and the assertion is
    /// that it did not advance: no production path under C-07 or C-08 reads a wall clock, nothing here
    /// sleeps, and the pool's idle expiry is therefore driven only when a case chooses to drive it. This is
    /// what makes the paired characterization recordings comparable - a value that varies between runs
    /// would have to be masked from BOTH the master and the candidate.
    /// </para>
    /// <para>
    /// <b>AND NO PERFORMANCE OBJECTIVE IS ASSERTED ANYWHERE IN THIS FILE (AAP 0.8.5)</b>, because the
    /// repository publishes no service-level agreement, no latency budget and no throughput target. The
    /// counts asserted throughout are counts of VERBS ISSUED, which is behaviour, not counts of time.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheWholeFlowRunsWithNoDatabaseAndWithoutMovingTheClock()
    {
        Harness harness = new();

        DateTimeOffset start = harness.Clock.GetUtcNow();

        SessionHandle session = await OpenSession(harness);
        TaskHandle task = await CreateTask(harness, session);

        ExecResponse exec = await harness.Commands.Exec(
            new ExecRequest
            {
                Task = task,
                Sql = "UPDATE COMPANY SET SALARY = ? WHERE ID = ?",
                Parameters = { Positional(20_000L), Positional(1L) },
                Autocommit = AutoCommitMode.AcOn,
            },
            Context);

        Assert.Equal(WireRetCode.Ok, exec.Status.RetCode);

        // C-E: the engine actually used is the double, reached through the transaction's own resolver.
        Assert.True(harness.Sessions.TryResolve(session, out TransactionSession? resolved));
        Assert.NotNull(resolved);
        Assert.Same(harness.Engine, resolved.Transaction.ResolveEngine());
        Assert.IsType<RecordingEngine>(resolved.Transaction.ResolveEngine());

        EndSessionResponse end = await harness.Transactions.EndSession(
            new EndSessionRequest { Session = session },
            Context);

        Assert.Equal(WireRetCode.Ok, end.Status.RetCode);

        // Determinism: the clock is where it started, and nothing advanced it.
        Assert.Equal(start, harness.Clock.GetUtcNow());
        Assert.Equal(TimeSpan.Zero, harness.Clock.Elapsed);
        Assert.Equal(0, harness.Clock.AdvanceCount);
        Assert.Equal(FakeTimeProvider.DefaultStart, start);
    }

    /// <summary>
    /// The task's verbatim Chinese diagnostics are preserved character for character, and neither routes
    /// through localization.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THESE STRINGS ARE OBSERVABLE, WHICH IS WHY THEY ARE PINNED (constraint C-B).</b> They travel in
    /// the general-error event, land in log records and appear in characterization recordings, so a
    /// reworded or translated form would silently invalidate every stored comparison. Both are quoted
    /// verbatim from the oracle: the empty-statement diagnostic at <c>:L66</c> and the binding-failure
    /// diagnostic at <c>:L83</c>, Chinese punctuation and trailing exclamation mark included.
    /// </para>
    /// <para>
    /// <b>AND NEITHER ROUTES THROUGH LOCALIZATION, WHICH IS A PRESERVED INCONSISTENCY RATHER THAN AN
    /// OVERSIGHT.</b> The equivalent messages in the DataWindow service layer DO route through
    /// <c>I18N</c>; the column-expression engine's twenty-eight do NOT. That inconsistency is legacy
    /// behaviour and is reproduced rather than harmonized (AAP 0.2.1.3).
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheVerbatimDiagnosticsArePreservedAndReachTheErrorEvent()
    {
        Assert.Equal("无效的SQL!", SqlCommandTask.InvalidSqlMessage);
        Assert.Equal("SQL参数绑定失败!", SqlCommandTask.BindArgumentFailureMessage);

        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);
        SqlCommandTask worker = Resolve(harness, handle).Worker;
        FakeTaskHost host = harness.Factory.LastTaskHost!;

        // The empty-statement arm [:L65-L68]: the error event, then the code, and NO transaction taken.
        Assert.Equal(RetCode.E_INVALID_SQL, worker.OnDoTask(TestContext.Current.CancellationToken));

        Assert.Contains((RetCode.E_INVALID_SQL, SqlCommandTask.InvalidSqlMessage), host.Errors);
        Assert.Equal(0, harness.Engine.ExecuteCalls);

        // The binding-failure arm [:L82-L85]: a statement whose placeholder count exceeds the parameters
        // it was given, which is the condition the oracle's binder reports out of bounds for.
        host.Errors.Clear();

        Assert.Equal(RetCode.OK, worker.SetSql("INSERT INTO COMPANY (NAME, AGE) VALUES (?, ?)"));
        Assert.Equal(RetCode.OK, worker.AddParam(string.Empty, "only-one-value"));

        Assert.Equal(
            RetCode.E_SQL_BIND_ARG_FAILED,
            worker.OnDoTask(TestContext.Current.CancellationToken));

        Assert.Contains(
            (RetCode.E_SQL_BIND_ARG_FAILED, SqlCommandTask.BindArgumentFailureMessage),
            host.Errors);

        // Nothing was executed, so a refused binding never reached the provider.
        Assert.Equal(0, harness.Engine.ExecuteCalls);
    }

    /// <summary>
    /// Cancellation answers <see cref="RetCode.CANCELLED"/> SILENTLY, with no event and no statement
    /// issued.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The oracle's arm is three lines - <c>if of_IsCancelled() then return RetCode.CANCELLED</c>
    /// [<c>:L76-L78</c>] - and its silence is the behaviour: no error event, no database-error event, no
    /// diagnostic. It also sits BEFORE the binder, so a cancelled task never interpolates a caller's
    /// literal into a statement, which is a security property as well as a behavioural one.
    /// </para>
    /// <para>
    /// <b>AND CANCELLED IS NEITHER SUCCEEDED NOR FAILED, WHICH IS THE TRI-STATE HOLE THE PLAN NAMES
    /// EXPLICITLY.</b> The success predicate is <c>rtCode &gt;= 0</c> and the failure predicate excludes
    /// the cancelled value by an explicit guard
    /// [<c>ws_objects/pfw.shared.pbl.src/issucceeded.srf:L11-L13</c>,
    /// <c>isfailed.srf:L11-L13</c>], so both answer false. Asserted here because a port that collapsed the
    /// hole would classify a cancellation as a failure and roll back work a caller merely stopped waiting
    /// for.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task CancellationAnswersCancelledSilentlyAndIssuesNoStatement()
    {
        Harness harness = new();
        TaskHandle handle = await CreateTask(harness);
        SqlCommandTask worker = Resolve(harness, handle).Worker;
        FakeTaskHost host = harness.Factory.LastTaskHost!;

        Assert.Equal(RetCode.OK, worker.SetSql("INSERT INTO COMPANY (NAME) VALUES (?)"));
        Assert.Equal(RetCode.OK, worker.AddParam(string.Empty, BoundLiteral));

        host.Cancelled = true;

        Assert.Equal(RetCode.CANCELLED, worker.OnDoTask(TestContext.Current.CancellationToken));

        // Silent: no event of any kind.
        Assert.Empty(host.Errors);

        // Nothing issued, and - because the arm precedes the binder - nothing interpolated either.
        Assert.Equal(0, harness.Engine.ExecuteCalls);
        Assert.Equal(0, harness.Engine.CommitCalls);
        Assert.Equal(0, harness.Engine.RollbackCalls);
        Assert.Empty(harness.Engine.LastRenderedStatement);

        // The tri-state hole: cancelled is NEITHER.
        Assert.False(Predicates.IsSucceeded(RetCode.CANCELLED));
        Assert.False(Predicates.IsFailed(RetCode.CANCELLED));
        Assert.Equal(-2L, RetCode.CANCELLED);
    }
}
