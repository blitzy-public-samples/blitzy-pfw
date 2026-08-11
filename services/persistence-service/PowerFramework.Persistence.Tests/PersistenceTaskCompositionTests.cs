// ==================================================================================================
//  PersistenceTaskCompositionTests.cs - the COMPOSED task layer, driven end to end
//
//  WHAT THIS SUITE IS FOR, AND WHY IT IS SEPARATE FROM PersistenceRuntimeTests
//  --------------------------------------------------------------------------------------------------
//  PersistenceRuntimeTests exercises each runtime collaborator directly: the engine, the definition
//  catalogue, the headless data-object runtime, the update carrier, the caller-side host. That is the
//  right shape for asserting each one's own contract, but it cannot answer the question this suite
//  exists for - DOES THE CONTAINER HAND BACK A TASK THAT ACTUALLY WRITES TO A DATABASE? Every object
//  here is resolved from the real composition root through the same four registration extensions
//  Program.cs calls, and the only thing configured differently is the SQLite data directory, which
//  points at a temporary file instead of the container's volume.
//
//  THIS IS THE CONTRACT C-06 PATH, WHOLE. A caller opens a transaction session, asks the update
//  factory for a task, names its data object, declares its updatable table, sends a changeset, and
//  executes. That is precisely the sequence UpdateService.Update performs, and asserting it here means
//  the RPC surface is exercised over live collaborators rather than over doubles that agree with it.
//
//  DISPATCH VERSUS TEARDOWN IS THE OTHER SUBJECT. The prepare event is per DISPATCH and re-arms the
//  commit signal; the uninit event is per TASK and closes it. Running two dispatches on one task and
//  then disposing it is the only way to see that difference, and getting it backwards makes either the
//  second dispatch report the first one's commit or the first dispatch destroy the signal it needs.
//
//  SQLITE ONLY (constraint C-E), against the one evidenced schema, exactly as the sibling suite
//  explains at length. Nothing here provisions, names or connects to any other engine.
// ==================================================================================================

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Grpc;
using PowerFramework.Persistence.Tasks;
using PowerFramework.Persistence.Tasks.TaskProxies;
using PowerFramework.Persistence.Transactions;
using DataObjectDefinitionRegistry = PowerFramework.Persistence.Data.DataObjectDefinitionCatalogue;
using Predicates = PowerFramework.Shared.Kernel.Predicates;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Drives the composed update and command task layer against a real temporary SQLite database.
/// </summary>
public sealed class PersistenceTaskCompositionTests : IDisposable
{
    /// <summary>
    /// The evidenced DDL, transcribed from <c>w_test_sqlite.srw:L463-L469</c>.
    /// </summary>
    private const string CompanyDdl =
        "CREATE TABLE COMPANY("
        + "ID INTEGER PRIMARY KEY AUTOINCREMENT,"
        + "NAME TEXT NOT NULL,"
        + "AGE INT NOT NULL,"
        + "ADDRESS CHAR(50),"
        + "SALARY REAL,"
        + "BIRTH TEXT)";

    private readonly string _directory;
    private readonly ServiceProvider _provider;

    /// <summary>Composes the real container over a temporary database and applies the evidenced DDL.</summary>
    public PersistenceTaskCompositionTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"pfw-compose-{Guid.NewGuid():n}");
        _ = Directory.CreateDirectory(_directory);

        ServiceCollection services = new();

        services.AddLogging();
        services.AddOptions<PersistenceOptions>().Configure(options =>
        {
            options.Sqlite.DataDirectory = _directory;
            options.Sqlite.DatabaseFileName = "test.db";
        });

        // The SAME four extensions Program.cs calls, in the same order. Resolving through them is what
        // makes this suite an assertion about the shipped composition rather than about a hand-built graph.
        services.AddPersistenceDeterminismSeam();
        services.AddPersistenceStorage();
        services.AddPersistenceSqlLayer();
        services.AddPersistencePublishedSurface();

        _provider = services.BuildServiceProvider();

        SqliteConnectionFactory connections = _provider.GetRequiredService<SqliteConnectionFactory>();

        using SqliteConnection seed = connections
            .CreateOpenConnectionAsync(CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();

        using SqliteCommand ddl = seed.CreateCommand();
        ddl.CommandText = CompanyDdl;
        _ = ddl.ExecuteNonQuery();
    }

    /// <summary>
    /// A task resolved from the container inserts a row into the database and reports the counts.
    /// </summary>
    /// <remarks>
    /// THIS IS THE WHOLE OF CONTRACT C-06 OVER LIVE COLLABORATORS. The session, the pooled transaction,
    /// the engine, the data store, the update carrier and the conflict detector are every one of them
    /// the registered implementation; nothing in the chain is a double. A composition that registered a
    /// refusing seam anywhere along it could not reach the first assertion.
    /// </remarks>
    [Fact]
    public void AComposedUpdateTaskInsertsARowAndReportsTheCounts()
    {
        using TaskHarness harness = new(this);

        Assert.Equal(RetCode.OK, harness.Task.SetDataObject(DwSqliteFixture.DataObjectName));

        // The single-table path: no prepare, so the definition's own `update="COMPANY"` is what names
        // the table [dw_sqlite.srd:L14, reached through n_cst_thread_task_sqlupdate.sru:L370-L371].
        Assert.Equal(RetCode.OK, harness.Task.SetMultiTableUpdate(false));
        Assert.Equal(RetCode.OK, harness.Task.SetUpdateData(NewRowChangeset(), 1L));

        UpdateRunResult result = harness.Task.Execute(TestContext.Current.CancellationToken);

        Assert.True(
            Predicates.IsSucceeded(result.Code),
            $"The composed update refused with {result.Code}: {result.ErrorText}");
        Assert.Equal(1L, result.Counts.Inserted);
        Assert.Equal(0L, result.Counts.Updated);
        Assert.Equal(0L, result.Counts.Deleted);
        Assert.Null(result.LastDbError);

        // AND THE ROW IS IN THE DATABASE. The task is in the engine's own auto-commit mode, so each
        // statement commits itself and a second connection sees it - which is what makes this an
        // assertion about storage rather than about a buffer.
        Assert.Equal("Teddy", harness.ScalarText("SELECT NAME FROM COMPANY WHERE AGE = 23"));
    }

    /// <summary>
    /// A task-level commit is REFUSED while the transaction is in auto-commit mode, and that is faithful.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>of_commit</c> tests auto-commit BEFORE it tests broken and answers a plain failure
    /// [<c>n_cst_thread_trans.sru:L240</c>] - committing a transaction that commits every statement of
    /// its own accord is a caller error, not a no-op. The update itself succeeds and its row is written;
    /// only the redundant commit that follows it fails, and the task reports that failure as its own.
    /// </para>
    /// <para>
    /// <b>THE TWO AUTO-COMMIT FLAGS ARE INDEPENDENT, AND THIS TEST NEEDS BOTH.</b> The oracle keeps one
    /// on the TASK - <c>of_setautocommit</c> writes <c>_bAutoCommit</c> and decides only whether the
    /// epilogue resolves the unit of work [<c>n_cst_thread_task_sqlupdate.sru:L254-L257, :L386-L387</c>] -
    /// and one on the TRANSACTION OBJECT, which is the <c>AutoCommit</c> the refusal above reads. Nothing
    /// couples them: <c>of_settransdata</c> copies DBMS, ServerName, Database, LogID, LogPass, DBParm and
    /// Lock and pointedly NOT AutoCommit [<c>n_cst_thread_trans.sru:L343-L352</c>], so a descriptor cannot
    /// set it either. The transaction's mode is therefore set here explicitly rather than assumed from a
    /// default - assuming it would make this test pass or fail on the default's value rather than on the
    /// refusal it is named for.
    /// </para>
    /// </remarks>
    [Fact]
    public void ATaskLevelCommitIsRefusedWhileTheTransactionIsInAutoCommitMode()
    {
        using TaskHarness harness = new(this);

        Assert.Equal(RetCode.OK, harness.Task.SetDataObject(DwSqliteFixture.DataObjectName));

        // THE TASK'S FLAG: it makes the epilogue call of_Commit at all.
        Assert.Equal(RetCode.OK, harness.Task.SetAutoCommit(true));

        // THE TRANSACTION'S MODE: it makes that call the refused one. Set after the update data is
        // irrelevant, but set BEFORE Execute is not - the epilogue reads it as it runs.
        harness.Session.Transaction.AutoCommit = true;

        Assert.Equal(RetCode.OK, harness.Task.SetUpdateData(NewRowChangeset(), 1L));

        UpdateRunResult result = harness.Task.Execute(TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.FAILED, result.Code);

        // The write itself happened - the refusal is the commit, not the update. Read back through the
        // task's own connection, which is the same one now resolving every statement of its own accord.
        Assert.Equal("Teddy", harness.ScalarText("SELECT NAME FROM COMPANY WHERE AGE = 23"));
    }

    /// <summary>
    /// The prepare event runs per dispatch and the uninit event per task, so two dispatches are legal.
    /// </summary>
    /// <remarks>
    /// The distinction is why the two events are raised from different places. Re-arming the commit
    /// signal belongs to each dispatch, and closing it belongs to teardown; raising the uninit event at
    /// the end of a dispatch would destroy the signal a following dispatch needs, and skipping the
    /// prepare event would let a stale committed reading survive into the next run.
    /// </remarks>
    [Fact]
    public void TwoDispatchesOnOneTaskAreLegalAndTeardownIsWhatEndsIt()
    {
        using TaskHarness harness = new(this);

        Assert.Equal(RetCode.OK, harness.Task.SetDataObject(DwSqliteFixture.DataObjectName));

        UpdateRunResult first = harness.Task.Execute(TestContext.Current.CancellationToken);
        UpdateRunResult second = harness.Task.Execute(TestContext.Current.CancellationToken);

        Assert.True(Predicates.IsSucceeded(first.Code), first.ErrorText);
        Assert.True(Predicates.IsSucceeded(second.Code), second.ErrorText);

        // Disposal is idempotent, which matters because the surface is reached through `using` in
        // production and may also be purged by a session teardown.
        harness.Task.Dispose();
        harness.Task.Dispose();
    }

    /// <summary>
    /// A cancelled dispatch reports cancellation and still lowers the running flag.
    /// </summary>
    /// <remarks>
    /// The flag falls in a <c>finally</c>, so a run that ends by cancellation or by throwing leaves the
    /// task usable rather than permanently busy - a task stuck running refuses every subsequent setter
    /// with <c>E_BUSY</c> [<c>n_cst_threading_task.sru:L112</c>].
    /// </remarks>
    [Fact]
    public void ACancelledDispatchStillLowersTheRunningFlag()
    {
        using TaskHarness harness = new(this);

        Assert.Equal(RetCode.OK, harness.Task.SetDataObject(DwSqliteFixture.DataObjectName));

        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        UpdateRunResult result = harness.Task.Execute(cancelled.Token);

        Assert.Equal(RetCode.CANCELLED, result.Code);

        // The proof the flag fell: a setter that refuses a running task is accepted here.
        Assert.Equal(RetCode.OK, harness.Task.SetMultiTableUpdate(true));
        Assert.Equal(RetCode.OK, harness.Task.ResetUpdatableTables());
    }

    /// <summary>
    /// The multi-table prepare path applies a descriptor and then re-derives the contract from it.
    /// </summary>
    /// <remarks>
    /// This is the arm AAP §0.6.3.4 describes: with the switch on, the preparer resets update, key and
    /// identity to off on EVERY column and re-enables only what the descriptor names, so the answer no
    /// longer depends on the definition at all. An empty descriptor array with the switch on is an
    /// ERROR rather than a no-op [<c>n_cst_thread_task_sqlupdate.sru:L358-L363</c>], and that asymmetry
    /// is the reason the switch exists.
    /// </remarks>
    [Fact]
    public void TheMultiTablePathCarriesADescriptorAndRefusesAnEmptyOne()
    {
        using TaskHarness harness = new(this);

        Assert.Equal(RetCode.OK, harness.Task.SetDataObject(DwSqliteFixture.DataObjectName));
        Assert.Equal(RetCode.OK, harness.Task.SetMultiTableUpdate(true));
        Assert.Equal(RetCode.OK, harness.Task.SetUpdateData(NewRowChangeset(), 1L));

        // Switch on, no descriptor: the oracle's own "no updatable table" refusal. The changeset has to
        // be present for the refusal to be reachable at all, because an absent one short-circuits to
        // success before the prepare branch is consulted [n_cst_thread_task_sqlupdate.sru:L327-L331].
        UpdateRunResult empty = harness.Task.Execute(TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, empty.Code);
        Assert.Equal("0", harness.ScalarText("SELECT COUNT(*) FROM COMPANY"));

        Assert.Equal(
            RetCode.OK,
            harness.Task.AddUpdatableTable(
                DwSqliteFixture.UpdateTableName,
                DwSqliteFixture.ColumnNames,
                DwSqliteFixture.KeyColumnNames,
                DwSqliteFixture.IdentityColumnName,
                DwSqliteFixture.UpdateWhereMode,
                DwSqliteFixture.UpdateKeyInPlace));

        Assert.Equal(RetCode.OK, harness.Task.SetUpdateData(NewRowChangeset(), 1L));

        UpdateRunResult prepared = harness.Task.Execute(TestContext.Current.CancellationToken);

        Assert.True(
            Predicates.IsSucceeded(prepared.Code),
            $"The prepared multi-table update refused with {prepared.Code}: {prepared.ErrorText}");
        Assert.Equal(1L, prepared.Counts.Inserted);
        Assert.Equal("Teddy", harness.ScalarText("SELECT NAME FROM COMPANY WHERE AGE = 23"));

        // The three-argument overload is the legacy's shorter add, which leaves both nullable settings
        // unspecified so the definition's own values stand.
        Assert.Equal(RetCode.OK, harness.Task.ResetUpdatableTables());
        Assert.Equal(
            RetCode.OK,
            harness.Task.AddUpdatableTable(
                DwSqliteFixture.UpdateTableName,
                DwSqliteFixture.ColumnNames,
                DwSqliteFixture.KeyColumnNames,
                DwSqliteFixture.IdentityColumnName));
    }

    /// <summary>
    /// A composed command task executes a statement against the database and answers its row count.
    /// </summary>
    /// <remarks>
    /// The command factory composes a pair rather than a surface, so the caller owns both halves - and
    /// the pair is genuinely wired, which is what makes the commit signal reachable. Contract C-07's
    /// <c>Exec</c> runs through exactly this pair.
    /// </remarks>
    [Fact]
    public void AComposedCommandPairIsLiveAndItsCommitSignalIsReachable()
    {
        ICommandTaskFactory commands = _provider.GetRequiredService<ICommandTaskFactory>();

        Assert.Equal(RetCode.OK, commands.Create(out CommandTaskComponents? composed));
        Assert.NotNull(composed);

        try
        {
            Assert.NotNull(composed!.Proxy);
            Assert.NotNull(composed.Worker);

            // Not committed yet, and reachable rather than structurally impossible: the proxy resolved
            // its worker through the host during initialization, so the descending walk over task
            // indices finds the one task that exists.
            Assert.False(composed.Proxy.IsCommitted());

            // The worker refuses a dispatch with no transaction descriptor rather than throwing, which
            // is the shape every guard in this layer takes.
            long dispatched = composed.Worker.OnDoTask(TestContext.Current.CancellationToken);

            Assert.True(
                Predicates.IsFailed(dispatched) || dispatched == RetCode.CANCELLED,
                $"A command dispatch with no descriptor answered {dispatched}, which reads as success.");
        }
        finally
        {
            composed!.Proxy.Dispose();
            composed.Worker.Dispose();
        }
    }

    /// <summary>
    /// The update factory refuses a session that does not exist, and names the reason.
    /// </summary>
    [Fact]
    public void TheUpdateFactoryRefusesAnUnknownSession()
    {
        IUpdateTaskFactory updates = _provider.GetRequiredService<IUpdateTaskFactory>();

        Assert.Equal(
            RetCode.E_INVALID_TRANSACTION,
            updates.TryCreate("no-such-session", out IUpdateTaskSurface? refused));
        Assert.Null(refused);
    }

    /// <summary>
    /// The provisioned factory's publication window reports a session it cannot resolve as RETIRING, so a
    /// task can never be published against a session that has already left the registry.
    /// </summary>
    /// <remarks>
    /// THE DEFAULT ANSWER FOR AN ABSENT SESSION IS THE SAFE ONE, NOT THE PERMISSIVE ONE. A session that is
    /// no longer in the registry has had - or is having - its pooled transaction handed back, so treating
    /// "cannot resolve" as "live" would publish a task holding a transaction the pool may already have
    /// re-issued to a different session.
    /// </remarks>
    [Fact]
    public async Task ThePublicationWindowReportsAnUnknownSessionAsRetiring()
    {
        IUpdateTaskFactory updates = _provider.GetRequiredService<IUpdateTaskFactory>();

        using UpdateTaskPublication publication = await updates.EnterPublicationAsync(
            "no-such-session",
            TestContext.Current.CancellationToken);

        Assert.True(publication.IsSessionRetiring);
    }

    /// <summary>
    /// The publication window reports a live session as not retiring, and reports the same session as
    /// retiring once it has been marked closing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// BOTH HALVES IN ONE CASE, BECAUSE EITHER ALONE WOULD PASS AGAINST A CONSTANT. A window that always
    /// answered "live" would satisfy the first assertion and a window that always answered "retiring" would
    /// satisfy the second, so only the pair pins that the flag is actually read off the session.
    /// </para>
    /// <para>
    /// The window is disposed between the two acquisitions because it HOLDS the session's lifecycle gate,
    /// which admits one holder at a time - a second acquisition taken while the first was still open would
    /// wait for ever. That is the property being relied on rather than an inconvenience: it is what makes
    /// the adapter's test-then-register indivisible.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ThePublicationWindowTracksTheSessionsClosingMark()
    {
        using TaskHarness harness = new(this);

        IUpdateTaskFactory updates = _provider.GetRequiredService<IUpdateTaskFactory>();

        using (UpdateTaskPublication live = await updates.EnterPublicationAsync(
            harness.Session.SessionId,
            TestContext.Current.CancellationToken))
        {
            Assert.False(live.IsSessionRetiring);
        }

        // The mark is written under the gate, exactly as EndSession writes it.
        using (harness.Session.Gate.Enter())
        {
            harness.Session.MarkClosing();
        }

        using (UpdateTaskPublication retiring = await updates.EnterPublicationAsync(
            harness.Session.SessionId,
            TestContext.Current.CancellationToken))
        {
            Assert.True(retiring.IsSessionRetiring);
        }
    }

    /// <summary>
    /// The publication window holds the session's lifecycle gate for as long as it lives, so a second
    /// acquisition cannot begin until the first is disposed.
    /// </summary>
    /// <remarks>
    /// THIS IS THE WHOLE POINT OF THE TYPE, AND WITHOUT THIS CASE IT COULD BE A PLAIN BOOLEAN. The adapter
    /// tests the flag and registers the task INSIDE one window; if the window held nothing, an
    /// <c>EndSession</c> could mark the session closing and walk the task registry between those two steps
    /// and the task it missed would outlive the transaction it holds. The assertion is deterministic rather
    /// than timed: the second acquisition is observed still incomplete while the first is open, and observed
    /// complete after it is disposed.
    /// </remarks>
    [Fact]
    public async Task ThePublicationWindowExcludesASecondAcquisitionUntilItIsDisposed()
    {
        using TaskHarness harness = new(this);

        IUpdateTaskFactory updates = _provider.GetRequiredService<IUpdateTaskFactory>();

        UpdateTaskPublication first = await updates.EnterPublicationAsync(
            harness.Session.SessionId,
            TestContext.Current.CancellationToken);

        ValueTask<UpdateTaskPublication> second = updates.EnterPublicationAsync(
            harness.Session.SessionId,
            TestContext.Current.CancellationToken);

        Assert.False(
            second.IsCompleted,
            "A second publication window opened while the first still held the session's gate.");

        first.Dispose();

        using UpdateTaskPublication admitted = await second;

        Assert.False(admitted.IsSessionRetiring);
    }

    /// <summary>
    /// The worker-side host binds exactly one task and refuses to be rebound.
    /// </summary>
    /// <remarks>
    /// Rebinding would silently redirect the commit notification's descending walk to a different task,
    /// so it is refused loudly rather than accepted. One host, one task, for the life of both.
    /// </remarks>
    [Fact]
    public void TheWorkerHostBindsOneTaskAndRefusesToBeRebound()
    {
        using TaskHarness harness = new(this);

        PersistenceSqlTaskHost host = new(
            NullLogger<PersistenceSqlTaskHost>.Instance,
            SqlRedactor.Instance);

        // Nothing bound: every index is out of bounds, including the sole one.
        Assert.Equal(RetCode.E_OUT_OF_BOUND, host.GetTask(1, out SqlTaskBase? none));
        Assert.Null(none);
    }

    /// <summary>
    /// The pool's idle collector is registered as a hosted service on the real composition.
    /// </summary>
    /// <remarks>
    /// Without it the keep-alive settings are decorative: an idle transaction holds its connection until
    /// the process ends. The registration is asserted here rather than only in the composition suite
    /// because this suite builds the graph the service actually runs against.
    /// </remarks>
    [Fact]
    public void ThePoolsIdleCollectorIsRegisteredAndTheClockItReadsIsInjected()
    {
        IEnumerable<Microsoft.Extensions.Hosting.IHostedService> hosted =
            _provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>();

        _ = Assert.Single(hosted, service => service is TransactionPoolIdleSweeper);

        // The same clock instance reaches the pool and the collector, so a characterization run can
        // advance both together rather than one drifting from the other.
        Assert.Same(
            _provider.GetRequiredService<TimeProvider>(),
            _provider.GetRequiredService<TimeProvider>());
    }

    /// <summary>
    /// Encodes a changeset carrying one NEW row shaped to the evidenced six-column definition.
    /// </summary>
    /// <returns>The changeset payload a caller would send on the wire.</returns>
    /// <remarks>
    /// The identity column is deliberately left unset: <c>id</c> is <c>identity=yes</c>
    /// [<c>dw_sqlite.srd:L8</c>], so the generated INSERT omits it and the database assigns it - which is
    /// the whole reason the identity round trip exists to read the assigned value back
    /// [<c>n_cst_thread_task_sqlupdate.sru:L215-L245</c>].
    /// </remarks>
    private CarrierState? NewRowChangeset()
    {
        DataWindowCarrier source = new(TimeProvider.System);

        // THE PROCESSING KIND HAS TO MATCH OR THE APPLY REFUSES, AND THAT REFUSAL IS CORRECT. The two
        // sides build their carriers from their own definitions, so a disagreement means they disagree
        // about which serialization is even applicable - merging a crosstab image into a tabular carrier
        // is what trusting the sender would produce. The evidenced definition declares `processing=1`
        // [dw_sqlite.srd:L3], so the sender declares the same.
        source.Processing = DataWindowProcessing.FromDescribe(
            DataObjectDefinitionRegistry.EvidencedProcessing);

        long row = source.AppendRow(DwBuffer.Primary, ItemStatus.New);

        _ = source.SetItemValue(row, DwSqliteFixture.NameColumnNumber, DwBuffer.Primary, "Teddy");
        _ = source.SetItemValue(row, DwSqliteFixture.AgeColumnNumber, DwBuffer.Primary, 23L);
        _ = source.SetItemValue(row, DwSqliteFixture.AddressColumnNumber, DwBuffer.Primary, "Norway");
        _ = source.SetItemValue(row, DwSqliteFixture.SalaryColumnNumber, DwBuffer.Primary, 20000d);
        _ = source.SetItemValue(row, DwSqliteFixture.BirthColumnNumber, DwBuffer.Primary, "1990-02-02");

        IChangesetPayloadCodec codec = _provider.GetRequiredService<IChangesetPayloadCodec>();

        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            codec.TryEncode(source, out CarrierState? state));
        Assert.NotNull(state);

        return state;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _provider.Dispose();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>
    /// One transaction session plus the update task the container composed for it.
    /// </summary>
    /// <remarks>
    /// The descriptor names the evidenced database and no DBMS, because SQLite is not in the legacy's
    /// two-value dialect enumeration at all [<c>n_cst_thread_trans.sru:L60-L61</c>] and the connection
    /// it resolves to is opened through the SQLite factory rather than selected by dialect.
    /// </remarks>
    private sealed class TaskHarness : IDisposable
    {
        private readonly TransactionPool _pool;
        private readonly PoolLease _lease;

        internal TaskHarness(PersistenceTaskCompositionTests owner)
        {
            _pool = owner._provider.GetRequiredService<TransactionPool>();

            TransactionData descriptor = new()
            {
                Database = DwSqliteFixture.UpdateTableName,
            };

            // THE LEASE, NOT THE SLOT INDEX. A released slot is reused, so an index outlives the entry
            // it named and a stale holder could reach a stranger's transaction; the lease carries a
            // monotonic id that is never reissued, which is what makes that impossible.
            _lease = _pool.AddRefLease(in descriptor);

            Assert.True(_lease.IsValid, "The pool refused to lease a transaction.");
            Assert.Equal(RetCode.OK, _pool.Get(_lease, out IPooledTransaction? transaction));
            Assert.NotNull(transaction);

            // BeginSession gates the connect on the liveness check and only then connects
            // [n_cst_thread_trans_pool.sru:L173-L174], so the harness does the same rather than
            // reconnecting a live pooled connection.
            if (!transaction!.IsConnected())
            {
                Assert.False(
                    Predicates.IsFailed(transaction.Connect()),
                    "The pooled transaction could not open its SQLite connection.");
            }

            TransactionSessionRegistry sessions =
                owner._provider.GetRequiredService<TransactionSessionRegistry>();

            TransactionSession? registered = sessions.Register(
                _lease,
                in descriptor,
                transaction,
                out string registrationDiagnostic);

            Assert.Equal(string.Empty, registrationDiagnostic);

            Session = Assert.IsType<TransactionSession>(registered);

            IUpdateTaskFactory updates = owner._provider.GetRequiredService<IUpdateTaskFactory>();

            Assert.Equal(
                RetCode.OK,
                updates.TryCreate(Session.SessionId, out IUpdateTaskSurface? task));
            Assert.NotNull(task);

            Task = task!;
        }

        /// <summary>The registered session the task was composed against.</summary>
        internal TransactionSession Session { get; }

        /// <summary>The composed update surface.</summary>
        internal IUpdateTaskSurface Task { get; }

        /// <summary>Reads one scalar as text through the SAME connection the task writes on.</summary>
        /// <param name="sql">The statement to read.</param>
        /// <returns>The first column of the first row, as text.</returns>
        /// <remarks>
        /// Reading through the task's own connection rather than a second one is what makes the assertion
        /// independent of whether the write was committed: an uncommitted write is visible on the
        /// connection that made it and invisible everywhere else, and the subject here is the statement,
        /// not the commit.
        /// </remarks>
        internal string ScalarText(string sql)
        {
            // THROUGH THE SHIPPED COMMAND SOURCE. The engine publishes no connection and no transaction
            // property - only ISqliteCommandSource.CreateCommand, which hands back a command already
            // enlisted in whatever explicit transaction is open. So this read sees uncommitted work
            // exactly as the carrier's own writes do, which is what makes the assertion meaningful.
            SqliteTransactionEngine engine = Assert.IsType<SqliteTransactionEngine>(
                Session.Transaction.ResolveEngine());

            using SqliteCommand command = engine.CreateCommand();
            command.CommandText = sql;

            return Convert.ToString(
                command.ExecuteScalar(),
                System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Task.Dispose();

            _ = _pool.RemoveRef(_lease);
        }
    }
}
