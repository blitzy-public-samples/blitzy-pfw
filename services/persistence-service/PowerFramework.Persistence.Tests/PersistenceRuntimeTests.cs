// ==================================================================================================
//  PersistenceRuntimeTests.cs - the SHIPPED runtime, exercised against a REAL SQLite database
//
//  WHY A REAL DATABASE HERE AND NOWHERE ELSE IN THIS PROJECT
//  --------------------------------------------------------------------------------------------------
//  Every other suite in this project is a unit suite over a double, and rightly so: a paging rewriter
//  is a pure string transform, an item-status machine is arithmetic, and a proxy pair's contract is its
//  forwarding. This suite is different because its subject IS the storage boundary - the engine that
//  opens a connection, the headless data-object runtime that fills a carrier from a reader, and the
//  update carrier that generates and executes DELETE/INSERT/UPDATE against real rows. Substituting a
//  double for the provider would leave exactly the code that talks to it untested, which is the code
//  most likely to be wrong.
//
//  SQLITE ONLY, AND THAT IS THE WHOLE STORAGE DECISION (constraint C-E). The legacy enumerates two
//  database types - SQL Server and Oracle [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:
//  L60-L61] - and provides a schema, a connection string or a line of DDL for NEITHER. SQLite is not in
//  that enumeration at all yet is the only engine with evidence: one DDL statement
//  [ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L463-L469] and one URI grammar [:L450-L456]. So this
//  suite provisions SQLite against that single evidenced schema and nothing else. The two enumerated
//  dialects survive as paging rewriters, which are pure string transforms and are held to byte-exact
//  parity by their own suites with no instance of either engine running.
//
//  THE DATABASE IS PER-CLASS, TEMPORARY AND SELF-DELETING. One file per test class instance, created
//  under the system temporary directory and removed by Dispose, so nothing is left behind and no two
//  runs share state. It is deliberately NOT the compose volume: the paired-capture persistence rule
//  governs characterization recordings, and these are unit-scoped assertions rather than recordings.
//
//  LEGACY PROVENANCE. The DDL, the sample rows, the six-column concurrency contract and the sort
//  expression all come from the read-only oracle through DwSqliteFixture, which transcribes them with
//  locators. Nothing here invents a schema.
// ==================================================================================================

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Grpc.Core;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PowerFramework.Persistence.Concurrency;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Grpc;
using PowerFramework.Persistence.Tasks;
using PowerFramework.Persistence.Tasks.TaskProxies;
using PowerFramework.Persistence.Transactions;

// THE READ-SIDE RUNTIME UNDER ITS SHIPPED NAMES. This suite was authored against a differently named
// runtime surface; the aliases below point every one of its names at the type the composition root
// actually registers, so the rows exercise the SHIPPED implementation rather than a parallel one. Aliases
// rather than a rename because the names read better in the assertions they appear in.
using DataObjectDefinitionEntry = PowerFramework.Persistence.Data.DataObjectDefinitionEntry;
using DataObjectDefinitionRegistry = PowerFramework.Persistence.Data.DataObjectDefinitionCatalogue;
using DataObjectRuntime = PowerFramework.Persistence.Data.SqliteDataObjectRuntime;
using DataWindowRuntimeState = PowerFramework.Persistence.Data.DataWindowStoreBindings;
using DeclaredColumn = PowerFramework.Persistence.Data.DeclaredDataObjectColumn;
using GridSyntax = PowerFramework.Persistence.Data.GridSyntax;
using Predicates = PowerFramework.Shared.Kernel.Predicates;
using QueryDataWindowRuntime = PowerFramework.Persistence.Data.SqliteQueryDataWindowRuntime;
using QueryTransactionSurface = PowerFramework.Persistence.Data.SqliteQueryTransactionSurface;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Holds the shipped SQLite runtime - engine, headless data-object runtime, query surface and update
/// carrier - to its behaviour against a real database.
/// </summary>
public sealed class PersistenceRuntimeTests : IDisposable
{
    /// <summary>The evidenced DDL, transcribed from the oracle's only CREATE TABLE.</summary>
    private const string CompanyDdl =
        "CREATE TABLE COMPANY("
        + "ID INTEGER PRIMARY KEY NOT NULL,"
        + "NAME           TEXT    NOT NULL,"
        + "AGE            INT     NOT NULL,"
        + "ADDRESS        CHAR(50),"
        + "SALARY         REAL,"
        + "BIRTH          TEXT)";

    private readonly string _directory;
    private readonly SqliteConnectionFactory _connections;

    /// <summary>Creates the temporary database and applies the evidenced DDL.</summary>
    public PersistenceRuntimeTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"pfw-runtime-{Guid.NewGuid():n}");
        _ = Directory.CreateDirectory(_directory);

        PersistenceOptions options = new()
        {
            Sqlite = new SqliteOptions
            {
                DataDirectory = _directory,
                DatabaseFileName = "test.db",
            },
        };

        _connections = new SqliteConnectionFactory(
            Options.Create(options),
            NullLogger<SqliteConnectionFactory>.Instance,
            TimeProvider.System);

        using SqliteConnection seed = _connections
            .CreateOpenConnectionAsync(CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();

        using SqliteCommand ddl = seed.CreateCommand();
        ddl.CommandText = CompanyDdl;
        _ = ddl.ExecuteNonQuery();
    }

    /// <summary>
    /// The handle an unconnected engine reports.
    /// </summary>
    /// <remarks>
    /// Named here rather than on the engine, because the engine publishes only the CONNECTED value as a
    /// constant - the pool tests the handle for truthiness and for <c>&lt;= 0</c>, so zero is the
    /// absence of a handle rather than a second handle value the production type needs to name.
    /// </remarks>
    private const int ClosedHandle = 0;

    // ==============================================================================================
    //  1. THE ENGINE
    // ==============================================================================================

    /// <summary>
    /// ⚠ A CONNECT THAT CANNOT OPEN ITS EXPLICIT TRANSACTION FAILS, rather than reporting success for a
    /// session that holds no transaction - asserted here through the ONE cause that is reachable, an
    /// unopenable data source, because the begin itself can no longer be made to fail on purpose.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With auto-commit OFF a caller is asking for a transaction, and the port opens an explicit one
    /// because .NET has no implicit transaction the way PowerBuilder does - there the transaction follows
    /// CONNECT and cannot fail separately, so there is no oracle arm for this and the nearest legacy
    /// behaviour is a CONNECT that left a non-zero status, which is <c>E_DB_ERROR</c>
    /// [<c>n_cst_thread_trans.sru:L129-L133</c>].
    /// </para>
    /// <para>
    /// Previously the begin failure was only LOGGED and the connect answered success, so
    /// <c>BeginSession</c> handed back a session whose first commit refused for a reason the caller could
    /// not relate to its own request. Now the provider's own failure is the connect's answer, and the
    /// connection is released rather than left half-open.
    /// </para>
    /// <para>
    /// <b>WHY THE BEGIN ARM IS DEFENCE IN DEPTH RATHER THAN A LIVE PATH.</b> The begin is DEFERRED - see
    /// the remark on <c>TryBeginTransaction</c>, which explains that an eager <c>BEGIN IMMEDIATE</c> would
    /// invent a lock conflict PowerBuilder's <c>CONNECT</c> never had. A deferred begin takes no lock, so
    /// short of a closed connection or a nested transaction, both of which the method guards against, it
    /// has nothing left to fail on: measured against the shipped provider, a held write slot fails an
    /// immediate begin and does NOT fail a deferred one. The failure PROPAGATION is therefore asserted
    /// here through the reachable connect failure - an unopenable data source - which exercises the same
    /// guarantee: the fault is reported, and the engine is left CLOSED rather than half-open.
    /// </para>
    /// </remarks>
    [Fact]
    public void AConnectThatCannotBeEstablishedReportsTheFaultAndLeavesNoHandle()
    {
        // A DIRECTORY WHERE THE FILE CANNOT BE CREATED. The path names a file inside a file, which the
        // operating system cannot satisfy, so the open fails for a reason that has nothing to do with
        // this suite's own database and cannot race with it.
        string occupied = Path.Combine(_directory, "occupied");
        File.WriteAllText(occupied, "not a directory");

        PersistenceOptions options = new()
        {
            Sqlite = new SqliteOptions
            {
                DataDirectory = occupied,
                DatabaseFileName = "test.db",
            },
        };

        SqliteConnectionFactory unopenable = new(
            Options.Create(options),
            NullLogger<SqliteConnectionFactory>.Instance,
            TimeProvider.System);

        using SqliteTransactionEngine engine = CreateEngine(unopenable);

        engine.AutoCommit = false;

        SqlState connected = engine.Connect(TestContext.Current.CancellationToken);

        // THE FAULT IS REPORTED, with the provider's own code beneath it rather than a generic failure.
        Assert.Equal(-1L, connected.SqlCode);
        Assert.Equal(RetCode.SQLITE_CANTOPEN, connected.SqlDbCode);
        Assert.NotEmpty(connected.SqlErrText);

        // AND THE ENGINE IS CLOSED, so nothing downstream can mistake it for a usable session.
        Assert.Equal(ClosedHandle, engine.DbHandle);

        // A COMMIT ON IT REPORTS THE ABSENT CONNECTION rather than claiming to have committed.
        Assert.Equal(RetCode.E_INVALID_TRANSACTION, engine.Commit().SqlDbCode);
    }

    /// <summary>
    /// AND THE FIDELITY PROPERTY THE DEFERRED BEGIN BUYS: a second auto-commit-OFF session CONNECTS while
    /// another holds the write slot, and only its WRITE conflicts.
    /// </summary>
    /// <remarks>
    /// This is PowerBuilder's own shape. Its transaction is implicit after <c>CONNECT</c> and takes no
    /// lock until a statement writes, so two transaction objects on one database both connect and the
    /// second write is what fails. An eager <c>BEGIN IMMEDIATE</c> would move that failure to connect time
    /// and invent a refusal the oracle does not have - which, combined with the fix above making a begin
    /// failure fatal to the connect, would have turned an ordinary second session into a refused one.
    /// <para>
    /// IN <b>WAL</b>, WHICH IS THE MODE A PROVISIONED DATABASE IS ACTUALLY IN - the migration tool leaves
    /// it there. WAL is also what keeps this case honest AND quick: it admits readers alongside the one
    /// writer and refuses a second writer at once, whereas in DELETE mode the second engine's open read
    /// would block the first engine's commit for the full busy timeout, which would be asserting the
    /// journal mode's locking rather than the connect's behaviour.
    /// </para>
    /// </remarks>
    [Fact]
    public void ASecondAutoCommitOffEngineCONNECTSWhileTheFirstHoldsTheWriteSlot()
    {
        SqliteConnectionFactory wal = CreateWalConnectionFactory();

        using SqliteTransactionEngine first = CreateEngine(wal);
        first.AutoCommit = false;

        Assert.Equal(0L, first.Connect(TestContext.Current.CancellationToken).SqlCode);

        // THE FIRST TAKES THE WRITE SLOT by actually writing under its transaction.
        Assert.Equal(
            1,
            first.Execute(
                "INSERT INTO COMPANY (NAME, AGE, ADDRESS, SALARY, BIRTH) "
                + "VALUES ('slot-holder', 41, 'x', 1, '1980-01-01')",
                TestContext.Current.CancellationToken).SqlNRows);

        using SqliteTransactionEngine second = CreateEngine(wal);
        second.AutoCommit = false;

        // THE SECOND CONNECT SUCCEEDS. This is the assertion that would have broken had the begin stayed
        // immediate once begin failures became fatal.
        Assert.Equal(0L, second.Connect(TestContext.Current.CancellationToken).SqlCode);
        Assert.Equal(SqliteTransactionEngine.ConnectedHandle, second.DbHandle);

        // AND IT CAN READ, because a deferred transaction takes no write lock.
        Assert.Equal("0", ScalarText(second, "SELECT COUNT(*) FROM COMPANY WHERE NAME = 'slot-holder'"));

        // ITS WRITE IS WHAT CONFLICTS, and the conflict is reported as the provider's own busy fault
        // rather than as a connect failure.
        SqlState blocked = second.Execute(
            "INSERT INTO COMPANY (NAME, AGE, ADDRESS, SALARY, BIRTH) "
            + "VALUES ('second-writer', 42, 'y', 2, '1981-01-01')",
            TestContext.Current.CancellationToken);

        Assert.Equal(-1L, blocked.SqlCode);
        Assert.Equal(RetCode.SQLITE_BUSY, blocked.SqlDbCode);

        // THE FIRST IS UNAFFECTED and its work commits - with the second's read transaction still open,
        // which is precisely what WAL allows and DELETE does not.
        Assert.Equal(0L, first.Commit().SqlCode);
        Assert.Equal("1", ScalarText(first, "SELECT COUNT(*) FROM COMPANY WHERE NAME = 'slot-holder'"));

        Assert.Equal(0, second.Rollback().SqlCode);
        Assert.Equal(0, second.Disconnect().SqlCode);
        Assert.Equal(0, first.Disconnect().SqlCode);
    }

    /// <summary>
    /// AND THE INVARIANT THE PAIR ABOVE EXISTS TO PROTECT: with auto-commit OFF, a connect that reports
    /// SUCCESS has an OPEN explicit transaction - reported by a commit that commits rather than one that
    /// refuses for want of a transaction.
    /// </summary>
    /// <remarks>
    /// Stated separately, and without any lock, because it is the property callers actually depend on.
    /// <see cref="SqliteTransactionEngine.Commit"/> discriminates the two states precisely: connected with
    /// a transaction commits and answers success, while connected WITHOUT one answers
    /// <c>SQLITE_MISUSE</c> and says so in its text. So this case would have caught the original defect
    /// from the other side - a connect that logged its begin failure and answered success would reach
    /// exactly that refusal, on a session the caller believed was transactional.
    /// </remarks>
    [Fact]
    public void AnAutoCommitOffConnectThatSucceedsHasAnOpenTransaction()
    {
        using SqliteTransactionEngine engine = CreateEngine();

        engine.AutoCommit = false;

        Assert.Equal(0L, engine.Connect(TestContext.Current.CancellationToken).SqlCode);

        SqlState committed = engine.Commit();

        Assert.Equal(0L, committed.SqlCode);
        Assert.DoesNotContain(
            SqliteTransactionEngine.NoOpenTransactionText,
            committed.SqlErrText,
            StringComparison.Ordinal);

        // AND THE DISCRIMINATOR IS REAL RATHER THAN VACUOUS. Switching auto-commit ON is the one way to
        // reach "connected, no transaction" on purpose - a commit does NOT reach it, because the engine
        // re-begins after committing so that the object stays transactional. From that state the same
        // commit answers the refusal, which is what the two assertions above are asserting the
        // transactional state is NOT.
        engine.AutoCommit = true;

        SqlState withoutOne = engine.Commit();

        Assert.Equal(-1L, withoutOne.SqlCode);
        Assert.Equal(RetCode.SQLITE_MISUSE, withoutOne.SqlDbCode);
        Assert.Equal(SqliteTransactionEngine.NoOpenTransactionText, withoutOne.SqlErrText);
    }

    /// <summary>
    /// The engine connects, reports an open handle, executes and unwinds.
    /// </summary>
    [Fact]
    public void TheEngineConnectsExecutesAndUnwinds()
    {
        using SqliteTransactionEngine engine = CreateEngine();

        // Unconnected: the handle is CLOSED and every write refuses with a diagnostic rather than
        // reporting success on nothing.
        Assert.Equal(ClosedHandle, engine.DbHandle);
        Assert.Equal(RetCode.E_INVALID_TRANSACTION, engine.Execute("SELECT 1", TestContext.Current.CancellationToken).SqlDbCode);
        Assert.Equal(RetCode.E_INVALID_TRANSACTION, engine.Commit().SqlDbCode);

        // The unwinds are idempotent successes on something that never connected, which is how the
        // legacy tolerates disconnecting an unconnected transaction.
        Assert.Equal(0, engine.Rollback().SqlCode);
        Assert.Equal(0, engine.Disconnect().SqlCode);

        TransactionData descriptor = new() { Dbms = "SQLite", Database = "COMPANY" };
        engine.ApplyConnectionFields(in descriptor);

        Assert.Equal("SQLite", engine.Dbms);
        Assert.Equal(0, engine.Connect(TestContext.Current.CancellationToken).SqlCode);
        Assert.Equal(SqliteTransactionEngine.ConnectedHandle, engine.DbHandle);

        // A COMMAND SOURCE IS THE OBSERVABLE FORM OF "CONNECTED". Unconnected it refuses; connected it
        // hands back a command enlisted on the open connection, which is the only thing any consumer
        // of this engine ever needs from it.
        using (SqliteCommand probe = engine.CreateCommand())
        {
            Assert.NotNull(probe.Connection);
        }

        // Connecting twice is a success on the connection already open, not a second connection.
        Assert.Equal(0, engine.Connect(TestContext.Current.CancellationToken).SqlCode);

        SqlState inserted = engine.Execute(
            "INSERT INTO COMPANY (NAME, AGE, ADDRESS, SALARY, BIRTH) "
            + "VALUES ('Paul', 32, 'California', 20000, '1999-05-08')",
            TestContext.Current.CancellationToken);

        Assert.Equal(0, inserted.SqlCode);
        Assert.Equal(1, inserted.SqlNRows);

        Assert.Equal(0, engine.Disconnect().SqlCode);
        Assert.Equal(ClosedHandle, engine.DbHandle);
    }

    /// <summary>
    /// A JOURNAL-MODE DISAGREEMENT NEVER FAILS THE CONNECTION - whichever way the conversion goes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE REGRESSION THIS PINS TOOK THE WHOLE DATA PLANE DOWN.</b> The migration tool leaves a freshly
    /// provisioned file in WAL and the configured mode is <c>DELETE</c>, so every connect against a
    /// provisioned database meets a disagreement. Converting out of WAL can answer <c>SQLITE_BUSY</c>, and
    /// while that failure was allowed to fail the connect, every session answered
    /// <see cref="RetCode.E_INVALID_TRANSACTION"/> permanently - one anonymous <c>GET /health</c> was
    /// enough to cause it.
    /// </para>
    /// <para>
    /// <b>WHAT THIS CASE ASSERTS, AND WHY IT DOES NOT ASSERT A REFUSAL.</b> Whether the provider refuses a
    /// given conversion is not something a test can pin down reliably, and it was measured rather than
    /// assumed: with the connection-string shape this service actually uses, the conversion out of WAL
    /// SUCCEEDS immediately even with another connection holding a read mark or the write slot, while with
    /// an un-pooled string and the same holders it answers "database is locked". Asserting one of those two
    /// outcomes would be asserting a provider detail, and the guarantee that matters is the same either
    /// way: <b>the connect succeeds and the connection is usable</b>. So this case connects against a WAL
    /// file under a DELETE configuration, holds a second connection open for the engine's whole lifetime,
    /// and asserts exactly that - plus that a write through it lands and commits.
    /// </para>
    /// <para>
    /// The cheap half of the same fix - read the mode first and issue no pragma at all when it already
    /// matches - is asserted separately by
    /// <see cref="AConnectAgainstTheConfiguredJournalModeLeavesItAlone"/>.
    /// </para>
    /// </remarks>
    [Fact]
    public void AJournalModeDisagreementIsNotFatal()
    {
        string databasePath = Path.Combine(_directory, "test.db");

        // Into WAL first, on a connection that then STAYS OPEN. Pooling is off so "stays open" is exactly
        // what it says rather than a property of the provider's pool.
        using SqliteConnection holder = new($"Data Source={databasePath};Pooling=False");
        holder.Open();

        using (SqliteCommand toWal = holder.CreateCommand())
        {
            toWal.CommandText = "PRAGMA journal_mode = WAL;";
            Assert.Equal("wal", Convert.ToString(toWal.ExecuteScalar(), CultureInfo.InvariantCulture));
        }

        // ⚠ AND THE HOLDER TAKES A READ MARK, WHICH IS WHAT ACTUALLY PRODUCES THE REFUSAL. Measured
        // against the shipped provider: converting OUT of WAL needs exclusive access, so an IDLE open
        // connection does NOT refuse it - only one holding a read transaction or the write slot does. A
        // read mark is the right one to use here, because WAL admits a writer alongside readers and the
        // rest of this case has to be able to write.
        using SqliteTransaction readMark = holder.BeginTransaction(
            System.Data.IsolationLevel.Serializable,
            deferred: true);

        using (SqliteCommand read = holder.CreateCommand())
        {
            read.CommandText = "SELECT COUNT(*) FROM COMPANY";
            _ = read.ExecuteScalar();
        }

        // The factory this fixture built is configured with the default DELETE, so the engine will try to
        // convert and will be refused.
        using SqliteTransactionEngine engine = CreateEngine();

        Assert.Equal(0, engine.Connect(TestContext.Current.CancellationToken).SqlCode);
        Assert.Equal(SqliteTransactionEngine.ConnectedHandle, engine.DbHandle);

        // THE MODE IS ONE OF THE TWO, AND EITHER IS ACCEPTABLE - see the remark. What is NOT acceptable
        // is a connect that failed, which the two assertions above have already ruled out.
        Assert.Contains(
            ScalarText(engine, "PRAGMA journal_mode"),
            new[] { "wal", "delete" },
            StringComparer.OrdinalIgnoreCase);

        // And the connection is fully usable in whichever mode the file ended up in.
        Assert.Equal(
            1,
            engine.Execute(
                "INSERT INTO COMPANY (NAME,AGE) VALUES ('journal-refused', 51)",
                TestContext.Current.CancellationToken).SqlNRows);

        Assert.Equal(0, engine.Commit().SqlCode);
        Assert.Equal("1", ScalarText(engine, "SELECT COUNT(*) FROM COMPANY WHERE NAME = 'journal-refused'"));
        Assert.Equal(0, engine.Disconnect().SqlCode);
    }

    /// <summary>
    /// A connect against a file already in the configured mode issues no conversion at all.
    /// </summary>
    /// <remarks>
    /// The cheap half of the same fix, and the half that runs on every ordinary connect: the mode in force
    /// is READ first, and when it already matches, the pragma - the one operation that can fail - is never
    /// issued. Asserted through the observable outcome, which is that the mode is unchanged and the
    /// connection works.
    /// </remarks>
    [Fact]
    public void AConnectAgainstTheConfiguredJournalModeLeavesItAlone()
    {
        using SqliteTransactionEngine engine = CreateEngine();

        Assert.Equal(0, engine.Connect(TestContext.Current.CancellationToken).SqlCode);
        Assert.Equal("delete", ScalarText(engine, "PRAGMA journal_mode"));
        Assert.Equal(0, engine.Disconnect().SqlCode);
    }

    /// <summary>
    /// The engine executes the PARAMETERIZED form of a bound statement, not the observable one.
    /// </summary>
    /// <remarks>
    /// The point of the dual form: the interpolated text is what a preview hook and an error payload
    /// see, and the placeholder text is what reaches the provider. Asserting the row landed with the
    /// bound value proves the executable form is the one that ran.
    /// </remarks>
    [Fact]
    public void TheEngineExecutesTheParameterizedFormOfABoundStatement()
    {
        using SqliteTransactionEngine engine = CreateEngine();
        Assert.Equal(0, engine.Connect(TestContext.Current.CancellationToken).SqlCode);

        // THE ENGINE'S CARRIER IS SqlCommandText, and the two texts are named for what each is FOR: the
        // canonical text is what the provider executes, the rendered text is the parity artefact every
        // observer compares against. Binding is POSITIONAL - @p1..@pN re-derived from the ordinal - which
        // is the single placeholder convention this service uses everywhere.
        SqlCommandText bound = SqlCommandText.FromBoundStatement(
            "INSERT INTO COMPANY (NAME, AGE) VALUES (@p1, @p2)",
            "INSERT INTO COMPANY (NAME, AGE) VALUES ('Alice', 30)",
            ["Alice", 30L]);

        SqlState state = engine.Execute(in bound, TestContext.Current.CancellationToken);

        Assert.Equal(0, state.SqlCode);
        Assert.Equal(1, state.SqlNRows);
        Assert.Equal("Alice", ScalarText(engine, "SELECT NAME FROM COMPANY WHERE AGE = 30"));
    }

    /// <summary>
    /// A refused statement answers a failing state carrying the provider's own mapped code.
    /// </summary>
    [Fact]
    public void ARefusedStatementAnswersAFailingStateWithTheProviderCode()
    {
        using SqliteTransactionEngine engine = CreateEngine();
        Assert.Equal(0, engine.Connect(TestContext.Current.CancellationToken).SqlCode);

        SqlState state = engine.Execute("INSERT INTO NO_SUCH_TABLE (a) VALUES (1)", TestContext.Current.CancellationToken);

        Assert.Equal(-1, state.SqlCode);
        Assert.NotEqual(0L, state.SqlDbCode);
        Assert.NotEmpty(state.SqlErrText);
    }

    /// <summary>
    /// With auto-commit off the engine opens an ambient transaction, and a rollback discards the work.
    /// </summary>
    [Fact]
    public void WithAutoCommitOffTheEngineOpensAnAmbientTransactionAndRollbackDiscardsTheWork()
    {
        using SqliteTransactionEngine engine = CreateEngine();
        engine.AutoCommit = false;

        // OPENED AT CONNECT, NOT AT THE FIRST STATEMENT. With auto-commit off the engine begins the
        // explicit transaction as part of connecting, so the very first statement is already inside one -
        // there is no window in which a caller that asked for a transaction is silently running without.
        Assert.Equal(0, engine.Connect(TestContext.Current.CancellationToken).SqlCode);
        Assert.NotNull(Ambient(engine));

        Assert.Equal(0, engine.Execute("INSERT INTO COMPANY (NAME, AGE) VALUES ('Rolled', 1)", TestContext.Current.CancellationToken).SqlCode);
        Assert.NotNull(Ambient(engine));

        // The rollback discards the work AND re-opens a transaction, so the object never drifts into
        // auto-commit behaviour after its first unwind. Both halves matter: without the discard the write
        // would survive, and without the re-open the NEXT write would commit itself.
        Assert.Equal(0, engine.Rollback().SqlCode);
        Assert.NotNull(Ambient(engine));
        Assert.Equal("0", ScalarText(engine, "SELECT COUNT(*) FROM COMPANY"));

        Assert.Equal(0, engine.Execute("INSERT INTO COMPANY (NAME, AGE) VALUES ('Kept', 2)", TestContext.Current.CancellationToken).SqlCode);
        Assert.Equal(0, engine.Commit().SqlCode);
        Assert.NotNull(Ambient(engine));
        Assert.Equal("1", ScalarText(engine, "SELECT COUNT(*) FROM COMPANY"));

        // And under auto-commit there is no explicit transaction at all, which is the other half of the
        // contract the two unwind verbs are judged against.
        engine.AutoCommit = true;
        Assert.Null(Ambient(engine));
        Assert.Equal(RetCode.SQLITE_MISUSE, engine.Commit().SqlDbCode);
        Assert.Equal(SqliteTransactionEngine.NoOpenTransactionText, engine.Rollback().SqlErrText);
    }

    /// <summary>
    /// An unresolved ambient transaction is ROLLED BACK on disconnect, never committed.
    /// </summary>
    /// <remarks>
    /// The safe direction is the only defensible one: committing work a caller never committed would
    /// make durable whatever a crashing request happened to have written.
    /// </remarks>
    [Fact]
    public void DisconnectRollsBackAnUnresolvedAmbientTransaction()
    {
        using SqliteTransactionEngine engine = CreateEngine();
        engine.AutoCommit = false;

        Assert.Equal(0, engine.Connect(TestContext.Current.CancellationToken).SqlCode);
        Assert.Equal(0, engine.Execute("INSERT INTO COMPANY (NAME, AGE) VALUES ('Lost', 3)", TestContext.Current.CancellationToken).SqlCode);
        Assert.Equal(0, engine.Disconnect().SqlCode);

        using SqliteTransactionEngine reader = CreateEngine();
        Assert.Equal(0, reader.Connect(TestContext.Current.CancellationToken).SqlCode);
        Assert.Equal("0", ScalarText(reader, "SELECT COUNT(*) FROM COMPANY"));
    }

    /// <summary>
    /// A supplied credential is REFUSED rather than silently ignored.
    /// </summary>
    /// <remarks>
    /// SQLite has no password on the unencrypted path, and the encrypted one is out of Phase-1 scope
    /// because the shipped cipher library's key derivation is not reachable through any framework API.
    /// Ignoring a credential would connect WITHOUT the protection the caller asked for, which is worse
    /// than refusing.
    /// </remarks>
    [Fact]
    public void ASuppliedCredentialIsRefusedRatherThanIgnored()
    {
        using SqliteTransactionEngine engine = CreateEngine();

        TransactionData descriptor = new() { Dbms = "SQLite", LogPass = "secret" };
        engine.ApplyConnectionFields(in descriptor);

        SqlState state = engine.Connect(TestContext.Current.CancellationToken);

        Assert.Equal(-1, state.SqlCode);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, state.SqlDbCode);
        Assert.Equal(SqliteTransactionEngine.CredentialRefusedText, state.SqlErrText);
        Assert.Equal(ClosedHandle, engine.DbHandle);
    }

    /// <summary>
    /// A disposed engine refuses every member rather than acting on a closed connection.
    /// </summary>
    [Fact]
    public void ADisposedEngineRefusesEveryMember()
    {
        SqliteTransactionEngine engine = CreateEngine();
        Assert.Equal(0, engine.Connect(TestContext.Current.CancellationToken).SqlCode);

        engine.Dispose();
        engine.Dispose();

        Assert.Throws<ObjectDisposedException>(() => engine.Connect(TestContext.Current.CancellationToken));
        Assert.Throws<ObjectDisposedException>(() => engine.Execute("SELECT 1", TestContext.Current.CancellationToken));
        Assert.Throws<ObjectDisposedException>(() => engine.Commit());
        Assert.Throws<ObjectDisposedException>(() => engine.Rollback());
        Assert.Throws<ObjectDisposedException>(() => engine.Disconnect());
    }

    /// <summary>
    /// The resolver finds the SQLite engine behind a pooled transaction and nothing behind a double.
    /// </summary>
    [Fact]
    public void TheEngineResolverFindsTheEngineBehindAPooledTransaction()
    {
        using SqliteTransactionEngine engine = CreateEngine();
        using PooledTransaction pooled = new(engine, TimeProvider.System);

        // THE CAPABILITY LOOKUP IS THE SHIPPED SEAM, and it is a lookup rather than a cast because the
        // consumer must not know which engine implementation it has: a transaction whose engine cannot
        // hand out a command answers false, and the caller reports the defined refusal instead of faulting.
        Assert.True(pooled.TryGetEngineCapability(out ISqliteCommandSource? resolved));
        Assert.Same(engine, resolved);

        // A transaction composed against something else is an ORDINARY negative, not a fault: every
        // consumer publishes a defined answer for "this provider cannot serve that".
        Assert.False(new UnbackedTransaction().TryGetEngineCapability(out ISqliteCommandSource? none));
        Assert.Null(none);
    }

    // ==============================================================================================
    //  2. THE DEFINITION CATALOGUE AND THE GRID SYNTAX
    // ==============================================================================================

    /// <summary>
    /// The catalogue carries the ONE evidenced definition and mints synthetic names for derived syntax.
    /// </summary>
    [Fact]
    public void TheCatalogueCarriesTheEvidencedDefinitionAndMintsSyntheticNames()
    {
        DataObjectDefinitionRegistry registry = new();

        Assert.True(registry.TryResolve(
            DataObjectDefinitionRegistry.EvidencedDataObject,
            out DataObjectDefinitionEntry? entry));
        Assert.NotNull(entry);
        Assert.Equal(DataObjectDefinitionRegistry.EvidencedSelect, entry!.Definition.SqlSelect);
        Assert.Equal(DataObjectDefinitionRegistry.EvidencedSort, entry.Definition.Sort);
        Assert.Equal(DataObjectDefinitionRegistry.EvidencedProcessing, entry.Definition.Processing);
        Assert.Equal(DwSqliteFixture.ColumnCount, entry.Columns.Count);

        // The two DDL divergences are PRESERVED AS DEFECTS: the DataWindow declares a 200-character
        // address against a 50-character column and a two-place decimal salary against a REAL column.
        Assert.Equal("char(200)", ColumnTypeOf(entry, DwSqliteFixture.AddressColumnNumber));
        Assert.Equal("decimal(2)", ColumnTypeOf(entry, DwSqliteFixture.SalaryColumnNumber));

        Assert.False(registry.TryResolve("d_unknown", out DataObjectDefinitionEntry? missing));
        Assert.Null(missing);

        string first = registry.RegisterSynthetic(
            ParseSyntax(GridSyntax.Compose("SELECT 1 AS one", [new DeclaredColumn(1, "one", "long")])));
        string second = registry.RegisterSynthetic(
            ParseSyntax(GridSyntax.Compose("SELECT 2 AS two", [new DeclaredColumn(1, "two", "long")])));

        Assert.StartsWith(DataObjectDefinitionRegistry.SyntheticNamePrefix, first, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
        Assert.True(registry.TryResolve(first, out DataObjectDefinitionEntry? synthetic));
        Assert.Equal("SELECT 1 AS one", synthetic!.Definition.SqlSelect);
    }

    /// <summary>
    /// Grid syntax round-trips: composed from a statement and its columns, parsed back to both.
    /// </summary>
    [Fact]
    public void GridSyntaxRoundTripsThroughComposeAndParse()
    {
        List<DeclaredColumn> columns =
        [
            new(1, "id", "long"),
            new(2, "name", "char(50)"),
        ];

        string syntax = GridSyntax.Compose("SELECT id, name FROM COMPANY", columns);

        Assert.True(GridSyntax.TryParse(syntax, out DataObjectDefinitionEntry? parsed, out string error));
        Assert.NotNull(parsed);
        Assert.Empty(error);

        Assert.Equal("SELECT id, name FROM COMPANY", parsed!.Definition.SqlSelect);
        Assert.Equal(2, parsed.Columns.Count);
        Assert.Equal("id", parsed.Columns[0].Name);
        Assert.Equal("long", parsed.Columns[0].DeclaredType);
        Assert.Equal(2, parsed.Columns[1].Number);
        Assert.Equal("name", parsed.Columns[1].Name);

        // Not grid syntax at all is a false answer WITH A DIAGNOSTIC rather than a throw: the caller
        // already has a create-failure path that surfaces the text.
        Assert.False(GridSyntax.TryParse("not syntax", out DataObjectDefinitionEntry? none, out string refusal));
        Assert.Null(none);
        Assert.NotEmpty(refusal);
    }

    // ==============================================================================================
    //  3. THE HEADLESS DATA-OBJECT RUNTIME
    // ==============================================================================================

    /// <summary>
    /// The runtime fills a carrier from the evidenced definition, with every row baselined.
    /// </summary>
    /// <remarks>
    /// BASELINING IS THE LOAD-BEARING PART. `updatewhere=1` compares the ORIGINAL value of every marked
    /// column [dw_sqlite.srd:L8-L14], so a retrieved row whose original-value shadow was not set would
    /// generate an update whose where-clause compared nothing - a silent overwrite, which this system
    /// permits nowhere.
    /// </remarks>
    /// <returns>A task representing the assertion.</returns>
    [Fact]
    public async Task TheRuntimeFillsACarrierAndBaselinesEveryRetrievedRow()
    {
        Seed(("Paul", 32, "California", 20000d, "1999-05-08"), ("Allen", 25, "Texas", 15000d, "1985-01-01"));

        RuntimeHarness harness = new(this);
        using SqliteTransactionEngine engine = harness.Engine;

        Assert.Equal(0, engine.Connect(TestContext.Current.CancellationToken).SqlCode);

        ISqlDataStore store = harness.CreateStore(DataObjectDefinitionRegistry.EvidencedDataObject);
        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            harness.Runtime.AttachTransaction(store, harness.Pooled));

        long rows = await harness.DataObjects.RetrieveAsync(
            store,
            [],
            TestContext.Current.CancellationToken);

        Assert.Equal(2L, rows);
        Assert.Equal(2L, store.Carrier.RowCount());

        // The rows arrive in the provider's order and every column is projected.
        Assert.Equal("Paul", store.Carrier.GetItemValue(1L, DwSqliteFixture.NameColumnNumber, DwBuffer.Primary));
        Assert.Equal("Allen", store.Carrier.GetItemValue(2L, DwSqliteFixture.NameColumnNumber, DwBuffer.Primary));

        // A DOUBLE, NOT AN INTEGER, AND THAT IS THE FAITHFUL ANSWER. `age` is declared `type=number`
        // [dw_sqlite.srd:L10] and PowerBuilder's DataWindow `number` type is double-precision, so the
        // carrier holds 32 as a number even though the DDL column is `AGE INT NOT NULL`
        // [w_test_sqlite.srw:L463-L469]. That is one more DataWindow-versus-DDL divergence preserved
        // rather than reconciled (AAP §0.6.4).
        Assert.Equal(
            32d,
            Assert.IsType<double>(
                store.Carrier.GetItemValue(1L, DwSqliteFixture.AgeColumnNumber, DwBuffer.Primary)));

        // Baselined: the original shadow equals the current value and the row reads as unmodified.
        Assert.Equal(
            ItemStatus.NotModified,
            store.Carrier.GetItemStatus(1L, ItemStatusMachine.RowStatusColumn, DwBuffer.Primary));
        Assert.Equal(
            "Paul",
            store.Carrier.GetItemOriginalValue(1L, DwSqliteFixture.NameColumnNumber, DwBuffer.Primary));
    }

    /// <summary>
    /// ⚠ A RETRIEVED <c>decimal(2)</c> CARRIES TWO DECIMAL PLACES, so the declared scale reaches the wire.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fixture declares <c>salary decimal(2)</c> over a <c>REAL</c> column [<c>dw_sqlite.srd:L13</c>
    /// against <c>w_test_sqlite.srw:L467</c>], so the provider's REAL-to-decimal conversion answers whatever
    /// scale the stored double needs - 20000 arrives as a ONE-place <c>20000.0</c> - and the wire projection
    /// then faithfully renders that wrong scale. <c>common.v1.DecimalValue</c> is explicit that this is not
    /// acceptable: trailing zeroes are significant BECAUSE they carry the declared scale, and it names this
    /// very fixture with <c>"1500.00"</c> as the required rendering. Two encodings of one number have to be
    /// byte-identical for a golden-master comparison to mean anything.
    /// </para>
    /// <para>
    /// ASSERTED ON <see cref="decimal.Scale"/> AND ON THE RENDERED TEXT, because the scale is the mechanism
    /// and the text is the observable: a decimal that merely compares equal to 20000.00m would satisfy an
    /// equality assertion while still rendering "20000".
    /// </para>
    /// </remarks>
    /// <returns>A task representing the assertion.</returns>
    [Fact]
    public async Task ARetrievedDecimalCarriesItsDECLAREDScale()
    {
        Seed(
            ("Paul", 32, "California", 20000d, "1999-05-08"),
            ("Round", 40, "Texas", 4321.5d, "1985-01-01"),
            ("Zero", 41, "Utah", 0d, "1986-01-01"),
            ("Exact", 42, "Ohio", 8250.75d, "1987-01-01"));

        RuntimeHarness harness = new(this);
        using SqliteTransactionEngine engine = harness.Engine;

        Assert.Equal(0, engine.Connect(TestContext.Current.CancellationToken).SqlCode);

        ISqlDataStore store = harness.CreateStore(DataObjectDefinitionRegistry.EvidencedDataObject);
        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            harness.Runtime.AttachTransaction(store, harness.Pooled));

        Assert.Equal(
            4L,
            await harness.DataObjects.RetrieveAsync(store, [], TestContext.Current.CancellationToken));

        (long Row, string Rendered)[] expected =
        [
            (1L, "20000.00"),
            (2L, "4321.50"),
            (3L, "0.00"),
            (4L, "8250.75"),
        ];

        foreach ((long row, string rendered) in expected)
        {
            decimal salary = Assert.IsType<decimal>(
                store.Carrier.GetItemValue(row, DwSqliteFixture.SalaryColumnNumber, DwBuffer.Primary));

            Assert.Equal(2, salary.Scale);
            Assert.Equal(rendered, salary.ToString(CultureInfo.InvariantCulture));

            // AND THE ORIGINAL SHADOW CARRIES THE SAME SCALE, because the baseline is what the concurrency
            // predicate compares against - a shadow at a different scale would render differently in a
            // conflict payload than the current value it is being compared with.
            decimal original = Assert.IsType<decimal>(
                store.Carrier.GetItemOriginalValue(
                    row,
                    DwSqliteFixture.SalaryColumnNumber,
                    DwBuffer.Primary));

            Assert.Equal(2, original.Scale);
            Assert.Equal(rendered, original.ToString(CultureInfo.InvariantCulture));
        }
    }

    // ==============================================================================================
    //  F-05 - THE READ-BACK GUARD: STORED TEXT A DECLARED NUMBER CANNOT HOLD
    // ==============================================================================================

    /// <summary>
    /// 🔴 STORED TEXT THAT IS NOT THE DECLARED NUMBER IS ANSWERED AS STORED, and never as <c>0</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>WHAT WAS WRONG.</b> The read path's documented fallback - "a coercion the provider refuses
    /// falls back to the provider's own value" - never fired for the numeric families, because
    /// <c>sqlite3_column_int64</c> and <c>sqlite3_column_double</c> do not refuse text that is not a
    /// number: they apply the engine's conversion rules and answer <c>0</c>. So the text
    /// <c>not-a-number</c> stored in a column the DataWindow declares <c>number</c> was answered to every
    /// caller as the double <c>0</c> - a value nothing had written, that no round trip could reproduce,
    /// and that arrived with no error anywhere.
    /// </para>
    /// <para>
    /// THREE OF THESE FOUR ROWS ARE CONTROLS, and they are what stops the guard from becoming a blanket
    /// "answer everything as text". A stored INTEGER still answers a double, and text that genuinely IS
    /// the declared number still coerces - including the fractional case, because the DataWindow type
    /// <c>number</c> is double-precision [<c>dw_sqlite.srd:L10</c>].
    /// </para>
    /// <para>
    /// AND THE ORIGINAL SHADOW CARRIES THE SAME ANSWER, which is load-bearing rather than incidental:
    /// <c>updatewhere=1</c> compares the ORIGINAL value of every marked column, so a shadow holding
    /// <c>0</c> beside a current value of <c>not-a-number</c> would make the row read as modified the
    /// moment it was retrieved.
    /// </para>
    /// </remarks>
    /// <returns>A task representing the assertion.</returns>
    [Fact]
    public async Task StoredTextThatIsNotTheDeclaredNumberIsAnsweredAsStoredRatherThanAsZero()
    {
        // ROW 1 stores TEXT in the AGE column, which the DDL gives INTEGER affinity and the DataWindow
        // declares `number`. SQLite keeps text that is not convertible to a number AS TEXT, which is
        // exactly the state a caller reading such a row back has to be told the truth about.
        // ROW 2 does the same for SALARY, declared `decimal(2)` over a REAL column, and pins AGE as a
        // genuinely stored integer so one row carries both the defect and its control.
        // ROWS 3 and 4 store text that IS the declared number.
        SeedRaw(
            ("Text", "not-a-number", "California", 20000d, "1999-05-08"),
            ("Money", 32L, "Texas", "abc", "1985-01-01"),
            ("Fraction", "3.5", "Utah", 100d, "1986-01-01"),
            ("Whole", "42", "Ohio", "1250.75", "1987-01-01"));

        RuntimeHarness harness = new(this);
        using SqliteTransactionEngine engine = harness.Engine;

        Assert.Equal(0, engine.Connect(TestContext.Current.CancellationToken).SqlCode);

        ISqlDataStore store = harness.CreateStore(DataObjectDefinitionRegistry.EvidencedDataObject);
        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            harness.Runtime.AttachTransaction(store, harness.Pooled));

        Assert.Equal(
            4L,
            await harness.DataObjects.RetrieveAsync(store, [], TestContext.Current.CancellationToken));

        // THE DEFECT, CLOSED. The stored text arrives as the stored text.
        Assert.Equal(
            "not-a-number",
            Assert.IsType<string>(
                store.Carrier.GetItemValue(1L, DwSqliteFixture.AgeColumnNumber, DwBuffer.Primary)));

        // ASSERTED NEGATIVELY AS WELL, because 0d is the specific wrong answer this closes and an
        // equality assertion against the string alone would still pass if the guard answered null.
        Assert.NotEqual(
            0d,
            store.Carrier.GetItemValue(1L, DwSqliteFixture.AgeColumnNumber, DwBuffer.Primary));

        Assert.Equal(
            "not-a-number",
            store.Carrier.GetItemOriginalValue(1L, DwSqliteFixture.AgeColumnNumber, DwBuffer.Primary));

        // A row retrieved with a value the declared type cannot hold is STILL BASELINED as unmodified -
        // the divergence is a definition-versus-schema disagreement, not a pending edit.
        Assert.Equal(
            ItemStatus.NotModified,
            store.Carrier.GetItemStatus(1L, ItemStatusMachine.RowStatusColumn, DwBuffer.Primary));

        // THE SAME FOR THE decimal FAMILY, with the stored-integer control beside it in one row.
        Assert.Equal(
            "abc",
            Assert.IsType<string>(
                store.Carrier.GetItemValue(2L, DwSqliteFixture.SalaryColumnNumber, DwBuffer.Primary)));
        Assert.Equal(
            32d,
            Assert.IsType<double>(
                store.Carrier.GetItemValue(2L, DwSqliteFixture.AgeColumnNumber, DwBuffer.Primary)));

        // CONTROLS: text that IS the declared number still coerces, fractional and whole alike.
        Assert.Equal(
            3.5d,
            Assert.IsType<double>(
                store.Carrier.GetItemValue(3L, DwSqliteFixture.AgeColumnNumber, DwBuffer.Primary)));
        Assert.Equal(
            42d,
            Assert.IsType<double>(
                store.Carrier.GetItemValue(4L, DwSqliteFixture.AgeColumnNumber, DwBuffer.Primary)));

        decimal salary = Assert.IsType<decimal>(
            store.Carrier.GetItemValue(4L, DwSqliteFixture.SalaryColumnNumber, DwBuffer.Primary));

        Assert.Equal(2, salary.Scale);
        Assert.Equal("1250.75", salary.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// ⚠ THE TEST IS PER FAMILY: one stored text, accepted by <c>number</c> and refused by <c>long</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THIS IS THE ROW THAT RULES OUT A SINGLE "IS IT A NUMBER" TEST.</b> The text <c>3.5</c> is a
    /// number and is NOT an integer, so a column the definition declares <c>long</c> would be answered
    /// <c>3</c> by the provider - truncated, and again a value nothing wrote. The identical text under a
    /// <c>number</c> declaration is answered <c>3.5</c> by the case above, so the two assertions together
    /// pin the per-family behaviour rather than a global one.
    /// </para>
    /// <para>
    /// READ OVER THE ADDRESS COLUMN, which has TEXT affinity, because a numeric-affinity column would
    /// convert <c>3.5</c> to a REAL on the way in and the value would no longer be stored TEXT at all -
    /// this guard is about stored TEXT and the case has to actually produce some.
    /// </para>
    /// </remarks>
    /// <returns>A task representing the assertion.</returns>
    [Fact]
    public async Task ADeclaredIntegerRefusesTheFractionalTextADeclaredNumberAccepts()
    {
        SeedRaw(
            ("Fraction", 30L, "3.5", 100d, "1986-01-01"),
            ("Integral", 31L, "7", 100d, "1986-01-01"));

        RuntimeHarness harness = new(this);
        using SqliteTransactionEngine engine = harness.Engine;

        Assert.Equal(0, engine.Connect(TestContext.Current.CancellationToken).SqlCode);

        string asLong = harness.Definitions.RegisterSynthetic(ParseSyntax(GridSyntax.Compose(
            "SELECT ADDRESS FROM COMPANY",
            [new DeclaredColumn(1, "v", "long")])));

        ISqlDataStore store = harness.CreateStore(asLong);
        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            harness.Runtime.AttachTransaction(store, harness.Pooled));

        Assert.Equal(
            2L,
            await harness.DataObjects.RetrieveAsync(store, [], TestContext.Current.CancellationToken));

        // REFUSED BY long: answered as stored rather than truncated to 3.
        Assert.Equal("3.5", Assert.IsType<string>(store.Carrier.GetItemValue(1L, 1, DwBuffer.Primary)));
        Assert.NotEqual(3L, store.Carrier.GetItemValue(1L, 1, DwBuffer.Primary));

        // ACCEPTED BY long: text that genuinely is an integer still coerces.
        Assert.Equal(7L, Assert.IsType<long>(store.Carrier.GetItemValue(2L, 1, DwBuffer.Primary)));
    }

    /// <summary>
    /// AND THE TEMPORAL FALLBACK STILL FIRES, so the guard added no second answer for the families that
    /// already had one.
    /// </summary>
    /// <remarks>
    /// <c>birth</c> is declared <c>date</c> over a <c>TEXT</c> column [<c>dw_sqlite.srd:L13</c> against
    /// <c>w_test_sqlite.srw:L468</c>], and asking the provider for a <see cref="DateTime"/> over the text
    /// <c>31/02/1984</c> genuinely throws - that is the path the documented fallback was written for and
    /// it works. The guard names no temporal family, so such a column reaches the switch exactly as it did
    /// before; this case is what proves it, and the well-formed control beside it proves the fallback did
    /// not become the only answer.
    /// </remarks>
    /// <returns>A task representing the assertion.</returns>
    [Fact]
    public async Task ADateTheProviderCannotParseStillFallsBackToItsStoredText()
    {
        SeedRaw(
            ("Bad", 30L, "California", 100d, "31/02/1984"),
            ("Good", 31L, "Texas", 100d, "1999-05-08"));

        RuntimeHarness harness = new(this);
        using SqliteTransactionEngine engine = harness.Engine;

        Assert.Equal(0, engine.Connect(TestContext.Current.CancellationToken).SqlCode);

        ISqlDataStore store = harness.CreateStore(DataObjectDefinitionRegistry.EvidencedDataObject);
        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            harness.Runtime.AttachTransaction(store, harness.Pooled));

        Assert.Equal(
            2L,
            await harness.DataObjects.RetrieveAsync(store, [], TestContext.Current.CancellationToken));

        Assert.Equal(
            "31/02/1984",
            Assert.IsType<string>(
                store.Carrier.GetItemValue(1L, DwSqliteFixture.BirthColumnNumber, DwBuffer.Primary)));

        Assert.Equal(
            new DateOnly(1999, 5, 8),
            Assert.IsType<DateOnly>(
                store.Carrier.GetItemValue(2L, DwSqliteFixture.BirthColumnNumber, DwBuffer.Primary)));
    }

    /// <summary>
    /// AND A RETRIEVAL RECORDS ITS COLUMN NAMES, so every projected column can carry the NAME identifier
    /// the contract requires beside its ordinal.
    /// </summary>
    /// <remarks>
    /// <c>common.v1.ColumnValue</c> states that "BOTH IDENTIFIERS ARE CARRIED, and neither is redundant" -
    /// a name survives a column reorder while an ordinal does not - and marks only <c>item_status</c>
    /// optional. The codecs had no name to put in the field and left it empty; the carrier now knows them,
    /// so they do. The one-based pairing is asserted explicitly, because a name-to-ordinal shift is the
    /// kind of defect that produces a plausible payload in which the two identifiers disagree.
    /// </remarks>
    /// <returns>A task representing the assertion.</returns>
    [Fact]
    public async Task ARetrievalRecordsItsColumnNamesInOneBasedOrder()
    {
        Seed(("Paul", 32, "California", 20000d, "1999-05-08"));

        RuntimeHarness harness = new(this);
        using SqliteTransactionEngine engine = harness.Engine;

        Assert.Equal(0, engine.Connect(TestContext.Current.CancellationToken).SqlCode);

        ISqlDataStore store = harness.CreateStore(DataObjectDefinitionRegistry.EvidencedDataObject);
        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            harness.Runtime.AttachTransaction(store, harness.Pooled));

        // BEFORE: nothing has told the carrier the names, and "not known" is an empty list rather than a
        // list of empty strings - the distinction the codecs rely on.
        Assert.Empty(store.Carrier.ColumnNames);
        Assert.Equal(string.Empty, store.Carrier.ColumnNameOf(DwSqliteFixture.SalaryColumnNumber));

        Assert.Equal(
            1L,
            await harness.DataObjects.RetrieveAsync(store, [], TestContext.Current.CancellationToken));

        Assert.Equal(DwSqliteFixture.ColumnNames, store.Carrier.ColumnNames);

        // ONE-BASED, and the two boundary ordinals answer the empty string: 0 denotes THE ROW ITSELF under
        // the convention common.v1.ColumnValue.column_id records, and one past the end names no column.
        Assert.Equal(string.Empty, store.Carrier.ColumnNameOf(0L));
        Assert.Equal(string.Empty, store.Carrier.ColumnNameOf(DwSqliteFixture.ColumnCount + 1));

        for (int number = 1; number <= DwSqliteFixture.ColumnCount; number++)
        {
            Assert.Equal(DwSqliteFixture.ColumnNames[number - 1], store.Carrier.ColumnNameOf(number));
        }
    }

    /// <summary>
    /// The runtime refuses an unresolvable data object and an unattached store, and never claims zero rows.
    /// </summary>
    /// <remarks>
    /// A zero row count would read as "retrieved successfully, nothing matched", which is the most
    /// damaging answer available. The datastore channel's own failure value says what happened instead.
    /// </remarks>
    /// <returns>A task representing the assertion.</returns>
    [Fact]
    public async Task TheRuntimeRefusesWithoutEverClaimingZeroRows()
    {
        RuntimeHarness harness = new(this);
        using SqliteTransactionEngine engine = harness.Engine;

        // Attached but never connected: there is no connection to run on.
        ISqlDataStore attached = harness.CreateStore(DataObjectDefinitionRegistry.EvidencedDataObject);
        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            harness.Runtime.AttachTransaction(attached, harness.Pooled));
        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            await harness.DataObjects.RetrieveAsync(attached, [], TestContext.Current.CancellationToken));

        // Never attached at all: no transaction, so no connection either.
        Assert.Equal(0, engine.Connect(TestContext.Current.CancellationToken).SqlCode);
        ISqlDataStore unattached = harness.CreateStore(DataObjectDefinitionRegistry.EvidencedDataObject);
        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            await harness.DataObjects.RetrieveAsync(unattached, [], TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A retrieval binds its arguments as provider parameters rather than splicing them.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    [Fact]
    public async Task ARetrievalBindsItsArgumentsAsProviderParameters()
    {
        Seed(("Paul", 32, "California", 20000d, "1999-05-08"), ("Allen", 25, "Texas", 15000d, "1985-01-01"));

        RuntimeHarness harness = new(this);
        using SqliteTransactionEngine engine = harness.Engine;
        Assert.Equal(0, engine.Connect(TestContext.Current.CancellationToken).SqlCode);

        // A synthetic definition whose statement carries one placeholder, so the argument channel is
        // genuinely exercised rather than inferred.
        //
        // ONE-BASED, MATCHING EVERY OTHER BOUND-PARAMETER NAME IN THIS SERVICE. The retrieval binder,
        // SqlCommandText.BindTo, SqlTaskBase's own binder and the update carrier all mint @p1..@pN from
        // the ordinal - the row above at "INSERT INTO COMPANY (NAME, AGE) VALUES (@p1, @p2)" pins the
        // same convention - because a legacy retrieval argument is positional and PowerBuilder counts
        // positions from one [AAP 0.4.5.4 R9]. A zero-based name here would name a parameter the binder
        // never adds, which the provider answers by refusing the whole command.
        string dataObject = harness.Definitions.RegisterSynthetic(ParseSyntax(GridSyntax.Compose(
            "SELECT NAME FROM COMPANY WHERE AGE > @p1",
            [new DeclaredColumn(1, "name", "char(50)")])));

        ISqlDataStore store = harness.CreateStore(dataObject);
        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            harness.Runtime.AttachTransaction(store, harness.Pooled));

        Assert.Equal(
            1L,
            await harness.DataObjects.RetrieveAsync(store, [30L], TestContext.Current.CancellationToken));
        Assert.Equal("Paul", store.Carrier.GetItemValue(1L, 1, DwBuffer.Primary));
    }

    /// <summary>
    /// The query runtime creates a carrier from grid syntax and resolves only a dddw child.
    /// </summary>
    /// <remarks>
    /// A non-dropdown column has no child, and answering false is the legacy's own skip answer rather
    /// than an error - the caller iterates every column and asks about each.
    /// </remarks>
    [Fact]
    public void TheQueryRuntimeCreatesFromSyntaxAndResolvesOnlyADropdownChild()
    {
        RuntimeHarness harness = new(this);
        using SqliteTransactionEngine engine = harness.Engine;

        ISqlDataStore store = harness.CreateStore(string.Empty);

        string syntax = GridSyntax.Compose(
            "SELECT id, name FROM COMPANY",
            [new DeclaredColumn(1, "id", "long"), new DeclaredColumn(2, "name", "dddw_name")]);

        CarrierCreateOutcome created = harness.Runtime.CreateFromSyntax(store, syntax);

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, created.Result);
        Assert.Empty(created.ErrorText);
        Assert.StartsWith(
            DataObjectDefinitionRegistry.SyntheticNamePrefix,
            store.DataObject,
            StringComparison.Ordinal);

        Assert.True(harness.Runtime.TryGetChild(store, "name", out DataWindowBufferStore? child));
        Assert.NotNull(child);

        Assert.False(harness.Runtime.TryGetChild(store, "id", out DataWindowBufferStore? none));
        Assert.Null(none);

        Assert.False(harness.Runtime.TryGetChild(store, "no_such_column", out DataWindowBufferStore? absent));
        Assert.Null(absent);

        // Syntax that is not grid syntax fails with an error text rather than an empty carrier.
        CarrierCreateOutcome refused = harness.Runtime.CreateFromSyntax(store, "not syntax");
        Assert.Equal(DataWindowBufferStore.DataStoreFailure, refused.Result);
        Assert.NotEmpty(refused.ErrorText);
    }

    // ==============================================================================================
    //  4. THE QUERY SURFACE
    // ==============================================================================================

    /// <summary>
    /// The surface derives grid syntax from a statement's real result-set schema, and counts rows.
    /// </summary>
    /// <remarks>
    /// SCHEMA-ONLY PREPARATION IS THE MECHANISM. Asking the provider to describe the statement is what
    /// makes the derived column list the statement's own rather than a guess from its text - which is
    /// the only way an arbitrary SELECT can produce a usable carrier.
    /// </remarks>
    [Fact]
    public async Task TheSurfaceDerivesGridSyntaxAndCountsRows()
    {
        Seed(("Paul", 32, "California", 20000d, "1999-05-08"), ("Allen", 25, "Texas", 15000d, "1985-01-01"));

        RuntimeHarness harness = new(this);
        using SqliteTransactionEngine engine = harness.Engine;
        Assert.Equal(0, engine.Connect(TestContext.Current.CancellationToken).SqlCode);

        GridSyntaxOutcome derived = harness.Surface.GridSyntaxFromSql(
            harness.Pooled,
            "SELECT ID, NAME FROM COMPANY");

        Assert.Empty(derived.ErrorText);
        Assert.NotEmpty(derived.Syntax);
        Assert.True(GridSyntax.TryParse(
            derived.Syntax,
            out DataObjectDefinitionEntry? derivedEntry,
            out string derivedError));
        Assert.NotNull(derivedEntry);
        Assert.Empty(derivedError);
        Assert.Equal("SELECT ID, NAME FROM COMPANY", derivedEntry!.Definition.SqlSelect);
        Assert.Equal(2, derivedEntry.Columns.Count);

        // The hooks are vetoable NOTIFICATIONS, so a surface with nothing to notify prevents nothing.
        DataWindowCarrier carrier = new(TimeProvider.System);
        Assert.Equal(RetCode.OK, harness.Surface.RaiseBeforeRetrieve(harness.Pooled, carrier));
        harness.Surface.RaiseAfterRetrieve(harness.Pooled, carrier, rowCount: 2L);

        // The count wrapper's alias is quoted, because the legacy emits `1 AS _` verbatim
        // [n_cst_thread_task_sqlquery.sru:L830] and a bare alias opening with a digit is not an
        // identifier in any dialect.
        CountQueryOutcome counted = await harness.Surface.Query(
            harness.Pooled,
            "SELECT COUNT(*) AS \"1 AS _\" FROM COMPANY",
            TestContext.Current.CancellationToken);

        // The return value is the ROW COUNT, not a return code: the legacy tests it against 1 before it
        // reads a value out of the carrier [:L845-L852], so a single-row count answers 1 and not OK.
        Assert.Equal(1L, counted.ReturnCode);
        Assert.Empty(counted.ErrorText);
        Assert.NotNull(counted.Result);
        Assert.Equal(1L, counted.Result!.RowCount());
        Assert.Equal(
            2L,
            Convert.ToInt64(counted.Result.GetItemValue(1L, 1, DwBuffer.Primary), CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// A statement the provider refuses answers a redacted error rather than throwing.
    /// </summary>
    [Fact]
    public async Task ARefusedStatementOnTheSurfaceAnswersAnErrorText()
    {
        RuntimeHarness harness = new(this);
        using SqliteTransactionEngine engine = harness.Engine;
        Assert.Equal(0, engine.Connect(TestContext.Current.CancellationToken).SqlCode);

        GridSyntaxOutcome derived = harness.Surface.GridSyntaxFromSql(harness.Pooled, "SELECT FROM");

        Assert.Empty(derived.Syntax);
        Assert.NotEmpty(derived.ErrorText);

        CountQueryOutcome refused = await harness.Surface.Query(
            harness.Pooled,
            "SELECT FROM",
            TestContext.Current.CancellationToken);

        Assert.NotEqual(RetCode.OK, refused.ReturnCode);
        Assert.Null(refused.Result);
        Assert.NotEmpty(refused.ErrorText);
    }

    /// <summary>
    /// A statement declaring no result column cannot yield grid syntax, and says so.
    /// </summary>
    [Fact]
    public void AStatementWithNoResultColumnCannotYieldGridSyntax()
    {
        RuntimeHarness harness = new(this);
        using SqliteTransactionEngine engine = harness.Engine;
        Assert.Equal(0, engine.Connect(TestContext.Current.CancellationToken).SqlCode);

        GridSyntaxOutcome derived = harness.Surface.GridSyntaxFromSql(
            harness.Pooled,
            "DELETE FROM COMPANY WHERE 1 = 0");

        Assert.Empty(derived.Syntax);
        Assert.NotEmpty(derived.ErrorText);
    }

    /// <summary>
    /// An unconnected SQLite transaction is a DIFFERENT negative from an unbacked one.
    /// </summary>
    [Fact]
    public async Task AnUnconnectedTransactionAnswersInvalidTransactionRatherThanNotImplemented()
    {
        RuntimeHarness harness = new(this);
        using SqliteTransactionEngine engine = harness.Engine;

        GridSyntaxOutcome derived = harness.Surface.GridSyntaxFromSql(harness.Pooled, "SELECT 1");
        Assert.Empty(derived.Syntax);
        // THE SURFACE'S OWN DIAGNOSTIC, WHICH IS DELIBERATELY A DIFFERENT SENTENCE FROM THE ENGINE'S.
        // An unconnected transaction hands out no command source, so the refusal the surface publishes
        // names THAT - the seam it could not cross - rather than echoing the engine's own
        // not-connected text. The two report the same underlying condition from opposite sides, and
        // conflating them would hide which side refused.
        Assert.Equal(QueryTransactionSurface.NoCommandSourceText, derived.ErrorText);
        Assert.NotEqual(SqliteTransactionEngine.NotConnectedText, derived.ErrorText);

        CountQueryOutcome refused = await harness.Surface.Query(
            harness.Pooled,
            "SELECT 1",
            TestContext.Current.CancellationToken);

        // E_INVALID_TRANSACTION, not E_NO_IMPLEMENTATION: a SQLite engine IS present, it is simply not
        // connected. The unsupported-dialect answer belongs to a transaction backed by something else.
        Assert.Equal(RetCode.E_INVALID_TRANSACTION, refused.ReturnCode);
        Assert.NotEmpty(refused.ErrorText);
    }

    // ==============================================================================================
    //  5. THE UPDATE CARRIER - THE WHOLE RETRIEVAL / VALIDATION / UPDATE TRIPLE'S WRITE HALF
    // ==============================================================================================

    /// <summary>
    /// The carrier generates and executes an INSERT for a new row, and reports the identity value back.
    /// </summary>
    [Fact]
    public void TheCarrierInsertsANewRowAndReportsItsIdentity()
    {
        UpdateHarness harness = new(this);
        using SqliteTransactionEngine engine = harness.Engine;
        Assert.Equal(0, engine.Connect(TestContext.Current.CancellationToken).SqlCode);

        harness.PrepareCompany();

        long row = harness.Carrier.Store.Carrier.AppendRow(DwBuffer.Primary, ItemStatus.New);
        SetRow(harness.Carrier.Store.Carrier, row, "Teddy", 23L, "Norway", 20000d, "1990-02-02");

        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            harness.Carrier.Target.Update(acceptText: true, resetFlag: false, TestContext.Current.CancellationToken));

        Assert.Equal("Teddy", ScalarText(engine, "SELECT NAME FROM COMPANY WHERE AGE = 23"));

        // THE IDENTITY COLUMN IS DISCOVERED FROM THE UPDATE TABLE'S OWN COLUMN METADATA, and the
        // assertion is against the ORDINAL-ADDRESSED property because that is the form the identity
        // round trip actually asks for: `"#" + String(nIndex) + ".Identity"`
        // [n_cst_thread_task_sqlupdate.sru:L215-L226, reproduced in Concurrency/IdentityColumnResolver.cs].
        //
        // ASSERTED BOTH WAYS, POSITIVE AND NEGATIVE. A describe that answered the same literal for every
        // column would satisfy a positive-only assertion while telling the resolver nothing, and the
        // resolver's failure mode on that input is SILENT: it finds no identity column, falls through to
        // its first-wins arm, and reports identity values for whichever column came first. The negative
        // row is what makes the positive one mean something.
        IIdentityColumnMetadata metadata = harness.Carrier.Identity.Metadata;

        Assert.Equal(
            UpdateWhereBuilder.YesLiteral,
            metadata.DescribeColumnIdentity(
                UpdateWhereBuilder.ColumnOrdinalPrefix + "1" + UpdateWhereBuilder.IdentityAttributeSuffix));
        Assert.Equal(
            UpdateWhereBuilder.NoLiteral,
            metadata.DescribeColumnIdentity(
                UpdateWhereBuilder.ColumnOrdinalPrefix + "2" + UpdateWhereBuilder.IdentityAttributeSuffix));

        // AND BY NAME, because the modification script the update preparer composes addresses columns by
        // name while the round trip addresses them by ordinal, and both forms must answer.
        Assert.Equal(
            UpdateWhereBuilder.YesLiteral,
            metadata.DescribeColumnIdentity(
                DwSqliteFixture.IdentityColumnName + UpdateWhereBuilder.IdentityAttributeSuffix));

        // A PROPERTY NAMING NO COLUMN STILL ANSWERS A LITERAL, never the empty string: PowerBuilder has
        // no empty answer for this describe, so an empty one would be a shape the oracle never emits.
        Assert.Equal(
            UpdateWhereBuilder.NoLiteral,
            metadata.DescribeColumnIdentity(DwSqliteFixture.IdentityColumnName));

        // ...and the inserted count is read back from the primary buffer FORWARD.
        Assert.Equal(1L, harness.Carrier.Identity.Values.GetInsertedCount());
    }

    /// <summary>
    /// An UPDATE compares the ORIGINAL value of every marked column, so a stale row does not overwrite.
    /// </summary>
    /// <remarks>
    /// THIS IS THE OPTIMISTIC-CONCURRENCY CONTRACT ITSELF. `updatewhere=1` with all six columns marked
    /// [dw_sqlite.srd:L8-L14] means the generated where-clause carries the key plus the original value
    /// of every updateable column. A row changed underneath the caller therefore matches nothing, and
    /// the mismatch is reported as a FAILURE - never a silent overwrite.
    /// </remarks>
    [Fact]
    public void AnUpdateComparesOriginalValuesAndAStaleRowIsAFailureRatherThanAnOverwrite()
    {
        Seed(("Paul", 32, "California", 20000d, "1999-05-08"));

        UpdateHarness harness = new(this);
        using SqliteTransactionEngine engine = harness.Engine;
        Assert.Equal(0, engine.Connect(TestContext.Current.CancellationToken).SqlCode);

        harness.PrepareCompany();

        long id = long.Parse(
            ScalarText(engine, "SELECT ID FROM COMPANY WHERE NAME = 'Paul'"),
            CultureInfo.InvariantCulture);

        // A row that mirrors what a retrieval would have produced: baselined, then edited.
        long row = harness.Carrier.Store.Carrier.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);
        SetRow(harness.Carrier.Store.Carrier, row, "Paul", 32L, "California", 20000d, "1999-05-08");
        _ = harness.Carrier.Store.Carrier.SetItemValue(row, DwSqliteFixture.IdColumnNumber, DwBuffer.Primary, id);
        harness.Carrier.Store.Carrier.RowAt(row, DwBuffer.Primary).Baseline();

        EditColumn(
            harness.Carrier.Store.Carrier,
            row,
            DwSqliteFixture.AgeColumnNumber,
            33L);

        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            harness.Carrier.Target.Update(acceptText: true, resetFlag: false, TestContext.Current.CancellationToken));
        Assert.Equal("33", ScalarText(engine, "SELECT AGE FROM COMPANY WHERE ID = " + id));

        // One row expected, one matched: no conflict, so nothing for a caller to retry.
        ConcurrencyEvidence clean = Assert.IsType<ConcurrencyEvidence>(
            harness.Carrier.CaptureConcurrencyEvidence());
        Assert.Equal(1L, clean.RowsExpected);
        Assert.Equal(1L, clean.RowsMatched);

        // NOW THE STALE CASE. Another writer moves the row, the caller's original shadow is out of date,
        // and the generated where-clause matches nothing.
        Assert.Equal(0, engine.Execute("UPDATE COMPANY SET AGE = 99 WHERE ID = " + id, TestContext.Current.CancellationToken).SqlCode);

        UpdateHarness stale = new(this);
        stale.Attach(harness.Pooled);
        stale.PrepareCompany();

        long staleRow = stale.Carrier.Store.Carrier.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);
        SetRow(stale.Carrier.Store.Carrier, staleRow, "Paul", 33L, "California", 20000d, "1999-05-08");
        _ = stale.Carrier.Store.Carrier.SetItemValue(
            staleRow,
            DwSqliteFixture.IdColumnNumber,
            DwBuffer.Primary,
            id);
        stale.Carrier.Store.Carrier.RowAt(staleRow, DwBuffer.Primary).Baseline();

        EditColumn(
            stale.Carrier.Store.Carrier,
            staleRow,
            DwSqliteFixture.AgeColumnNumber,
            34L);

        // THE CARRIER REPORTS SUCCESS, AND THE VERDICT COMES FROM THE CLASSIFIER. This member's return is
        // `Data.Update(true,false)`'s [n_cst_thread_task_sqlupdate.sru:L204], and PowerBuilder answers 1
        // when every statement executed without a DBMS ERROR - which a zero-row match is not, on any
        // provider. The concurrency verdict therefore lives in exactly one place, ConflictDetector reading
        // the evidence, which is what mints the Aborted-to-409 payload the contract publishes. Asserting a
        // -1 here would demand the judgement be made twice and would make the conflict path unreachable.
        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            stale.Carrier.Target.Update(acceptText: true, resetFlag: false, TestContext.Current.CancellationToken));

        // The evidence a conflict response is minted from: one row expected, none matched.
        ConcurrencyEvidence evidence = Assert.IsType<ConcurrencyEvidence>(
            stale.Carrier.CaptureConcurrencyEvidence());
        Assert.Equal(1L, evidence.RowsExpected);
        Assert.Equal(0L, evidence.RowsMatched);
        Assert.False(evidence.ProviderFaulted);

        // AND THE SHORTFALL IS RECOGNISED AS A MISMATCH RATHER THAN AS A PROVIDER FAULT, which is the
        // claim this row's own name makes: a caller learns its edit did not apply.
        Assert.True(ConflictDetector.IsConcurrencyMismatch(evidence));

        // AND THE ROW IS UNCHANGED. No silent overwrite anywhere in this system.
        Assert.Equal("99", ScalarText(engine, "SELECT AGE FROM COMPANY WHERE ID = " + id));
    }

    /// <summary>
    /// A DELETE is generated from the delete buffer, and the order is DELETE then INSERT then UPDATE.
    /// </summary>
    /// <remarks>
    /// The order is contract, not preference: `updatekeyinplace=no` [dw_sqlite.srd:L14] makes a key
    /// change a delete-plus-insert pair, and running the insert before the delete would collide on the
    /// key the delete is about to free.
    /// </remarks>
    [Fact]
    public void ADeleteIsGeneratedFromTheDeleteBuffer()
    {
        Seed(("Paul", 32, "California", 20000d, "1999-05-08"));

        UpdateHarness harness = new(this);
        using SqliteTransactionEngine engine = harness.Engine;
        Assert.Equal(0, engine.Connect(TestContext.Current.CancellationToken).SqlCode);

        harness.PrepareCompany();

        long id = long.Parse(
            ScalarText(engine, "SELECT ID FROM COMPANY WHERE NAME = 'Paul'"),
            CultureInfo.InvariantCulture);

        long row = harness.Carrier.Store.Carrier.AppendRow(DwBuffer.Delete, ItemStatus.NotModified);
        SetRow(harness.Carrier.Store.Carrier, row, "Paul", 32L, "California", 20000d, "1999-05-08", DwBuffer.Delete);
        _ = harness.Carrier.Store.Carrier.SetItemValue(
            row,
            DwSqliteFixture.IdColumnNumber,
            DwBuffer.Delete,
            id);
        harness.Carrier.Store.Carrier.RowAt(row, DwBuffer.Delete).Baseline();

        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            harness.Carrier.Target.Update(acceptText: true, resetFlag: false, TestContext.Current.CancellationToken));

        Assert.Equal("0", ScalarText(engine, "SELECT COUNT(*) FROM COMPANY WHERE ID = " + id));
    }

    /// <summary>
    /// 🔴 THE WHOLE STALE-WRITE WORKFLOW, OVER A REAL DATABASE: a competing writer moves a row, the
    /// caller's update matches nothing, and the REAL classifier turns that into the <c>Aborted</c>
    /// projection carrying the row state STORAGE holds - with the caller's edit never applied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>WHY THIS EXERCISE EXISTS AND WHAT A SPLIT-HALF SUITE MISSED.</b> Every part of this chain had
    /// its own test - the carrier measured a shortfall, the classifier narrowed a supplied measurement,
    /// the service relayed a supplied conflict - and the workflow was still broken end to end, because
    /// nothing drove the REAL carrier into the REAL classifier over a REAL competing write. Two defects
    /// lived in the gaps between those halves: the measurement was read before the update had run, so it
    /// was always the previous attempt's, and the classifier consulted it only after the success arm had
    /// already returned. A stale write therefore reported success and the caller was told its edit
    /// applied. This exercise fails if either returns.
    /// </para>
    /// <para>
    /// EVERY COLLABORATOR HERE IS THE SHIPPED ONE: the connection factory, the engine, the pooled
    /// transaction, the update carrier and its statement generator, the update preparer that installs the
    /// evidenced contract, the conflict detector, and the rich-error projection the C-06 throw site
    /// consumes whole. The only substitution is the temporary database file.
    /// </para>
    /// </remarks>
    [Fact]
    public void AStaleRowWorkflowAnswersAbortedWithStorageStateAndNeverOverwrites()
    {
        Seed(("Paul", 32, "California", 20000d, "1999-05-08"));

        UpdateHarness harness = new(this);
        using SqliteTransactionEngine engine = harness.Engine;
        Assert.Equal(0, engine.Connect(TestContext.Current.CancellationToken).SqlCode);

        harness.PrepareCompany();

        long id = long.Parse(
            ScalarText(engine, "SELECT ID FROM COMPANY WHERE NAME = 'Paul'"),
            CultureInfo.InvariantCulture);

        // The caller's carrier: retrieved, baselined, then edited - which is the state a real retrieval
        // followed by a user edit produces.
        long row = harness.Carrier.Store.Carrier.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);
        SetRow(harness.Carrier.Store.Carrier, row, "Paul", 32L, "California", 20000d, "1999-05-08");
        _ = harness.Carrier.Store.Carrier.SetItemValue(
            row,
            DwSqliteFixture.IdColumnNumber,
            DwBuffer.Primary,
            id);
        harness.Carrier.Store.Carrier.RowAt(row, DwBuffer.Primary).Baseline();

        // ANOTHER WRITER GETS THERE FIRST, through the provider and outside this carrier entirely.
        Assert.Equal(
            0,
            engine.Execute(
                "UPDATE COMPANY SET AGE = 99 WHERE ID = " + id,
                TestContext.Current.CancellationToken).SqlCode);

        EditColumn(harness.Carrier.Store.Carrier, row, DwSqliteFixture.AgeColumnNumber, 34L);

        // THE REAL CLASSIFIER, DRIVING THE REAL CARRIER. Nothing supplies it a measurement: it takes one
        // from the target after the update has run, which is the ordering the contract requires.
        ConflictDetector detector = new(SqlRedactor.Instance);

        UpdateOutcome outcome = detector.Classify(new UpdateAttempt
        {
            Transaction = new WorkflowTransaction(),
            Target = harness.Carrier.Target,
            Identity = harness.Carrier.Identity,
            Errors = new WorkflowErrorSink(),
            Cancellation = TestContext.Current.CancellationToken,
        });

        // A CLAIMED SUCCESS OVER A ZERO-ROW MATCH IS A CONFLICT. The DataWindow update answered 1 - no
        // provider raised anything - and the verdict comes from the measurement instead.
        Assert.Equal(UpdateOutcomeKind.Conflict, outcome.Kind);
        Assert.Equal(RetCode.E_DB_ERROR, outcome.Code);
        Assert.True(outcome.RequiresRollback);

        // NOTHING IS PUBLISHED FROM A CONFLICT: no identity block and no counts report reach a caller
        // that changed no row.
        Assert.Null(outcome.Identity);

        ConflictDetail detail = Assert.IsType<ConflictDetail>(outcome.Conflict);
        Assert.Equal(DwSqliteFixture.UpdateTableName, detail.UpdateTable);
        Assert.Equal(1L, detail.RowsExpected);
        Assert.Equal(0L, detail.RowsMatched);

        ConflictRow reported = Assert.Single(detail.Rows);

        // 🔴 THE CURRENT VALUES ARE THE DATABASE'S, NOT THE CALLER'S. The winning writer left 99 in the
        // age column; the caller submitted 34 and believed 32. A payload echoing 34 here would send the
        // caller round a retry loop resubmitting the value that just lost.
        ColumnValue currentAge = ColumnOf(reported.CurrentValues, DwSqliteFixture.AgeColumnNumber);
        ColumnValue originalAge = ColumnOf(reported.OriginalValues, DwSqliteFixture.AgeColumnNumber);

        Assert.Equal(99L, currentAge.Value.Int64Value);
        Assert.Equal(32L, originalAge.Value.Int64Value);

        // THE UNION OF KEY AND MARKED COLUMNS TRAVELS, not the modified column alone: a caller rebasing a
        // retry needs every column whose original value formed the predicate that failed. The evidenced
        // fixture marks all six [dw_sqlite.srd:L8-L14].
        Assert.Equal(DwSqliteFixture.Columns.Count, reported.CurrentValues.Count);
        Assert.Equal(reported.CurrentValues.Count, reported.OriginalValues.Count);
        Assert.NotNull(ColumnOf(reported.CurrentValues, DwSqliteFixture.IdColumnNumber));

        // THE WIRE PROJECTION THE C-06 THROW SITE CONSUMES WHOLE - built here from the real outcome, so
        // the status and the trailer a caller receives are the ones this workflow actually produces.
        Assert.True(ConflictDetector.TryProjectAborted(outcome, out RichErrorProjection projection));
        Assert.Equal(StatusCode.Aborted, projection.Status.StatusCode);

        byte[]? raw = projection.Trailers.GetValueBytes(ConflictDetector.RichErrorTrailerKey);
        Assert.NotNull(raw);

        RichErrorTrailer trailer = RichErrorTrailer.Parser.ParseFrom(raw);
        Assert.Equal(RichErrorTrailer.DetailOneofCase.Conflict, trailer.DetailCase);
        Assert.Equal(RetCode.E_DB_ERROR, trailer.RetCode);
        Assert.Equal(
            99L,
            ColumnOf(
                Assert.Single(trailer.Conflict.Rows).CurrentValues,
                DwSqliteFixture.AgeColumnNumber).Value.Int64Value);

        // 🔴 AND NO SILENT OVERWRITE. The winning writer's value stands, the caller's 34 was never
        // written, and the caller learns it must re-read and rebase or surface the conflict - which is a
        // decision it makes, not one this service makes for it. Nothing here retried: the update ran
        // exactly once.
        Assert.Equal("99", ScalarText(engine, "SELECT AGE FROM COMPANY WHERE ID = " + id));
    }

    // ==============================================================================================
    //  F-04 - A CALLER-CONTROLLED CONSTRAINT REFUSAL, AND THE TRANSACTION STATE THAT MAKES IT LEGIBLE
    // ==============================================================================================

    /// <summary>
    /// 🔴 A ROW OMITTING A <c>NOT NULL</c> COLUMN IS A PAYLOAD FAULT CARRYING THE COLUMN'S NAME, not an
    /// unspecific database error.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>WHAT WAS WRONG, AND IT WAS TWO THINGS COMPOUNDING.</b> First, nothing in this service ever wrote
    /// the transaction's SQL state, so the classification's else arm built its failure payload from
    /// <c>transaction.SqlDbCode</c> and <c>transaction.SqlErrText</c> and got an EMPTY one every time -
    /// and the discrimination that reads the same code could never fire. Second, the provider's diagnostic
    /// went through the strict mask, which read the wrapper <c>SQLite Error 19: '...'</c> as a numeric
    /// literal beside a quoted string and masked both. So a caller who omitted a required field was told
    /// the database had failed - HTTP 502, the fault lies behind the gateway - by a message that would not
    /// say which column.
    /// </para>
    /// <para>
    /// DRIVEN THROUGH THE REAL CARRIER AND THE REAL ENGINE, because the discriminator is the DRIVER's own
    /// extended result code and no fake produces one. The DDL this suite applies is the oracle's only
    /// CREATE TABLE, in which <c>NAME</c> and <c>AGE</c> are both <c>NOT NULL</c>
    /// [<c>w_test_sqlite.srw:L463-L469</c>], and the DataWindow definition declares no required flag on
    /// any column [<c>dw_sqlite.srd:L8-L13</c>] - which is exactly why this refusal has to be classified
    /// HERE and cannot be pre-empted by the projecting service.
    /// </para>
    /// </remarks>
    [Fact]
    public void ARowOmittingARequiredColumnIsAPayloadFaultNamingThatColumn()
    {
        UpdateHarness harness = new(this);
        using SqliteTransactionEngine engine = harness.Engine;
        Assert.Equal(0, engine.Connect(TestContext.Current.CancellationToken).SqlCode);

        harness.PrepareCompany();

        // A NEW row whose NAME is never written. The identity column is omitted by the carrier on an
        // insert, so the statement supplies AGE and ADDRESS and leaves NAME to the column default - which
        // does not exist, so the engine refuses.
        long row = harness.Carrier.Store.Carrier.AppendRow(DwBuffer.Primary, ItemStatus.NewModified);
        _ = harness.Carrier.Store.Carrier.SetItemValue(row, DwSqliteFixture.AgeColumnNumber, DwBuffer.Primary, 44L);
        _ = harness.Carrier.Store.Carrier.SetItemValue(
            row,
            DwSqliteFixture.AddressColumnNumber,
            DwBuffer.Primary,
            "California");

        StampingTransaction transaction = new(harness.Pooled);
        WorkflowErrorSink errors = new();

        UpdateOutcome outcome = new ConflictDetector(SqlRedactor.Instance).Classify(new UpdateAttempt
        {
            Transaction = transaction,
            Target = harness.Carrier.Target,
            Identity = harness.Carrier.Identity,
            Errors = errors,
            Cancellation = TestContext.Current.CancellationToken,
        });

        // THE CLASSIFICATION, AND THE CODE A CALLER READS. E_INVALID_DATA publishes as HTTP 400.
        Assert.Equal(UpdateOutcomeKind.ConstraintViolation, outcome.Kind);
        Assert.Equal(RetCode.E_INVALID_DATA, outcome.Code);

        // NOT A CONFLICT, which is the neighbouring classification and the one that would send a caller
        // round a pointless retry loop: no original value was stale, and no row lost a race.
        Assert.Null(outcome.Conflict);

        // 🔴 THE PAYLOAD NAMES THE COLUMN. This is the assertion the finding is about.
        DbErrorData reported = Assert.IsType<DbErrorData>(outcome.DbError);
        Assert.Equal(RetCode.SQLITE_CONSTRAINT_NOTNULL, reported.SqlDbCode);
        Assert.Contains("COMPANY.NAME", reported.SqlErrText, StringComparison.Ordinal);
        Assert.StartsWith("SQLite Error 19: '", reported.SqlErrText, StringComparison.Ordinal);

        // AND THE STATEMENT IS STILL MASKED, on the same payload, because it is the field that carries
        // interpolated literal values by construction.
        Assert.DoesNotContain("California", reported.SqlSyntax, StringComparison.Ordinal);

        // THE TRANSACTION STATE THE CLASSIFICATION READ. SqlCode is the DataWindow failure value, which is
        // PowerBuilder's own -1 after a failed operation, so the transaction's own failure predicate now
        // answers truthfully instead of always answering false.
        Assert.Equal(RetCode.SQLITE_CONSTRAINT_NOTNULL, transaction.SqlDbCode);
        Assert.Equal(DataWindowBufferStore.DataStoreFailure, transaction.SqlCode);
        Assert.True(transaction.IsFailed());

        // CLEARED ONCE, AT THE START OF THIS ATTEMPT. A pooled transaction outlives one update, so the
        // per-attempt clear is what stops an earlier stamp being read as this attempt's evidence.
        Assert.Equal(1, transaction.ClearStateCalls);

        // AND NOTHING WAS WRITTEN.
        Assert.Equal("0", ScalarText(engine, "SELECT COUNT(*) FROM COMPANY"));
    }

    /// <summary>
    /// A clean update leaves the transaction's state cleared, so the stamp is a failure-only signal.
    /// </summary>
    /// <remarks>
    /// <b>THE CONTROL THAT KEEPS THE DEFENSIVE OVERRIDE UNREACHABLE.</b> That override rewrites a claimed
    /// success into a failure when the SQL code is negative
    /// [<c>n_cst_thread_task_sqlupdate.sru:L208-L210</c>], and it had never been able to fire because
    /// nothing wrote the code. Stamping could have activated it by accident - which would silently turn
    /// successful updates into failures - so this pins that a successful attempt leaves the code at zero
    /// and the override therefore still cannot fire.
    /// </remarks>
    [Fact]
    public void ASuccessfulUpdateLeavesTheTransactionStateClearedAndTheOverrideUnreachable()
    {
        Seed(("Paul", 32, "California", 20000d, "1999-05-08"));

        UpdateHarness harness = new(this);
        using SqliteTransactionEngine engine = harness.Engine;
        Assert.Equal(0, engine.Connect(TestContext.Current.CancellationToken).SqlCode);

        harness.PrepareCompany();

        long id = long.Parse(
            ScalarText(engine, "SELECT ID FROM COMPANY WHERE NAME = 'Paul'"),
            CultureInfo.InvariantCulture);

        long row = harness.Carrier.Store.Carrier.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);
        SetRow(harness.Carrier.Store.Carrier, row, "Paul", 32L, "California", 20000d, "1999-05-08");
        _ = harness.Carrier.Store.Carrier.SetItemValue(row, DwSqliteFixture.IdColumnNumber, DwBuffer.Primary, id);
        harness.Carrier.Store.Carrier.RowAt(row, DwBuffer.Primary).Baseline();

        EditColumn(harness.Carrier.Store.Carrier, row, DwSqliteFixture.AgeColumnNumber, 33L);

        StampingTransaction transaction = new(harness.Pooled);

        UpdateOutcome outcome = new ConflictDetector(SqlRedactor.Instance).Classify(new UpdateAttempt
        {
            Transaction = transaction,
            Target = harness.Carrier.Target,
            Identity = harness.Carrier.Identity,
            Errors = new WorkflowErrorSink(),
            Cancellation = TestContext.Current.CancellationToken,
        });

        Assert.Equal(UpdateOutcomeKind.Succeeded, outcome.Kind);
        Assert.Equal(RetCode.OK, outcome.Code);

        // NO STAMP ON A SUCCESS: the code stays at its cleared value, so the negative-code arm of the
        // defensive override is not reachable from a successful attempt.
        Assert.Equal(0L, transaction.SqlCode);
        Assert.Equal(0L, transaction.SqlDbCode);
        Assert.False(transaction.IsFailed());
        Assert.False(outcome.DefensiveOverrideApplied);

        Assert.Equal("33", ScalarText(engine, "SELECT AGE FROM COMPANY WHERE ID = " + id));
    }

    /// <summary>
    /// 🔴 An INSERT reads the generated identity back onto the carrier, so the identity round trip reports
    /// the value the DATABASE assigned rather than the placeholder the caller sent.
    /// </summary>
    /// <remarks>
    /// WITHOUT THE READ-BACK THE ROUND TRIP IS A LOOP THAT REPORTS ITS OWN INPUT. The insert deliberately
    /// omits the identity column so the store assigns it [<c>n_cst_thread_task_sqlupdate.sru:L215-L245</c>
    /// is the collection half], and the resolver reads the value straight off this same carrier at
    /// [<c>:L231</c>] - so a carrier still holding the caller's placeholder makes the whole identity block
    /// a fiction. This exercise pins the value against the one the database actually generated.
    /// </remarks>
    [Fact]
    public void AnInsertWritesTheGeneratedIdentityBackOntoTheCarrier()
    {
        UpdateHarness harness = new(this);
        using SqliteTransactionEngine engine = harness.Engine;
        Assert.Equal(0, engine.Connect(TestContext.Current.CancellationToken).SqlCode);

        harness.PrepareCompany();

        long row = harness.Carrier.Store.Carrier.AppendRow(DwBuffer.Primary, ItemStatus.NewModified);
        SetRow(harness.Carrier.Store.Carrier, row, "Grace", 41L, "Kent", 33000d, "1984-03-01");

        // THE PLACEHOLDER A CALLER SENDS FOR A ROW IT HAS NOT SEEN A KEY FOR. If this survives the update,
        // the identity block is reporting the caller's own guess.
        const long Placeholder = -7L;
        _ = harness.Carrier.Store.Carrier.SetItemValue(
            row,
            DwSqliteFixture.IdColumnNumber,
            DwBuffer.Primary,
            Placeholder);

        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            harness.Carrier.Target.Update(
                acceptText: true,
                resetFlag: false,
                TestContext.Current.CancellationToken));

        long generated = long.Parse(
            ScalarText(engine, "SELECT ID FROM COMPANY WHERE NAME = 'Grace'"),
            CultureInfo.InvariantCulture);

        Assert.NotEqual(Placeholder, generated);

        // The carrier now answers the DATABASE's value, which is what the resolver collects.
        Assert.Equal(
            generated,
            harness.Carrier.Identity.Values.GetItemNumber(row, DwSqliteFixture.IdColumnNumber));

        // AND THROUGH THE SHIPPED RESOLVER, over the same surfaces the classifier hands it: the row is
        // NewModified! so the forward primary walk collects it [:L228-L233].
        IdentityResolutionOutcome resolved = IdentityColumnResolver.Resolve(
            harness.Carrier.Identity.Metadata,
            harness.Carrier.Identity.Values);

        ResolvedIdentityColumnData block = Assert.Single(resolved.Identity);
        Assert.Equal(DwSqliteFixture.ExpectedDiscoveredIdentityColumnNumber, block.IdentityColumnId);
        Assert.Equal(generated, Assert.Single(block.PrimaryValues));
        Assert.Equal(1L, resolved.Counts.Inserted);
    }

    /// <summary>
    /// 🔴 A KEY CHANGE UNDER <c>updatekeyinplace=no</c> IS A DELETE PLUS AN INSERT, never an in-place
    /// update - and the pair is atomic: a delete that matches nothing runs no insert.
    /// </summary>
    /// <remarks>
    /// THE EVIDENCED FIXTURE SETS THE MODE, so this is the ordinary path rather than a rare branch:
    /// <c>update="COMPANY" updatewhere=1 updatekeyinplace=no</c> [dw_sqlite.srd:L14]. The observable
    /// difference is the statement pair on the preview channel and the row's new key in storage; an
    /// in-place UPDATE would leave the old key row rewritten instead of removed and re-created.
    /// </remarks>
    [Fact]
    public void AKeyChangeUnderKeyInPlaceNoBecomesADeleteAndAnInsert()
    {
        Seed(("Paul", 32, "California", 20000d, "1999-05-08"));

        UpdateHarness harness = new(this);
        using SqliteTransactionEngine engine = harness.Engine;
        Assert.Equal(0, engine.Connect(TestContext.Current.CancellationToken).SqlCode);

        harness.PrepareCompany();

        long id = long.Parse(
            ScalarText(engine, "SELECT ID FROM COMPANY WHERE NAME = 'Paul'"),
            CultureInfo.InvariantCulture);

        long row = harness.Carrier.Store.Carrier.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);
        SetRow(harness.Carrier.Store.Carrier, row, "Paul", 32L, "California", 20000d, "1999-05-08");
        _ = harness.Carrier.Store.Carrier.SetItemValue(
            row,
            DwSqliteFixture.IdColumnNumber,
            DwBuffer.Primary,
            id);
        harness.Carrier.Store.Carrier.RowAt(row, DwBuffer.Primary).Baseline();

        // THE KEY ITSELF MOVES, which is the condition the mode is about.
        EditColumn(harness.Carrier.Store.Carrier, row, DwSqliteFixture.IdColumnNumber, id + 100L);

        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            harness.Carrier.Target.Update(
                acceptText: true,
                resetFlag: false,
                TestContext.Current.CancellationToken));

        // THE OLD ROW IS GONE AND A NEW ONE EXISTS - the delete-plus-insert outcome. An in-place update
        // would have left exactly one row carrying the new key and no delete at all, which the counts
        // below distinguish.
        Assert.Equal("0", ScalarText(engine, "SELECT COUNT(*) FROM COMPANY WHERE ID = " + id));
        Assert.Equal("1", ScalarText(engine, "SELECT COUNT(*) FROM COMPANY WHERE NAME = 'Paul'"));

        // THE KEY THE CALLER ASKED FOR IS THE KEY IN STORAGE. The insert half writes the changed key even
        // though the column carries the identity flag: a key change is an explicit assignment, so letting
        // the store pick instead would re-create the row under a value nobody requested.
        Assert.Equal("1", ScalarText(engine, "SELECT COUNT(*) FROM COMPANY WHERE ID = " + (id + 100L)));

        // COUNTED AS A DELETE AND AN INSERT, NOT AS AN UPDATE, because two statements were generated.
        Assert.Equal(1L, harness.Carrier.Identity.Values.GetDeletedCount());
        Assert.Equal(1L, harness.Carrier.Identity.Values.GetInsertedCount());
        Assert.Equal(0L, harness.Carrier.Identity.Values.GetUpdatedCount());

        // BOTH STATEMENTS MATCHED, so there is no shortfall and nothing for a caller to retry.
        ConcurrencyEvidence evidence = Assert.IsType<ConcurrencyEvidence>(
            harness.Carrier.CaptureConcurrencyEvidence());

        Assert.Equal(2L, evidence.RowsExpected);
        Assert.Equal(2L, evidence.RowsMatched);
        Assert.False(ConflictDetector.IsConcurrencyMismatch(evidence));
    }

    /// <summary>
    /// 🔴 A KEY CHANGE WHOSE DELETE MATCHES NOTHING RUNS NO INSERT, so a stale key change cannot duplicate
    /// the row it failed to remove - and it is reported as the conflict it is.
    /// </summary>
    [Fact]
    public void AStaleKeyChangeRunsNoInsertAndIsReportedAsAConflict()
    {
        Seed(("Paul", 32, "California", 20000d, "1999-05-08"));

        UpdateHarness harness = new(this);
        using SqliteTransactionEngine engine = harness.Engine;
        Assert.Equal(0, engine.Connect(TestContext.Current.CancellationToken).SqlCode);

        harness.PrepareCompany();

        long id = long.Parse(
            ScalarText(engine, "SELECT ID FROM COMPANY WHERE NAME = 'Paul'"),
            CultureInfo.InvariantCulture);

        long row = harness.Carrier.Store.Carrier.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);
        SetRow(harness.Carrier.Store.Carrier, row, "Paul", 32L, "California", 20000d, "1999-05-08");
        _ = harness.Carrier.Store.Carrier.SetItemValue(
            row,
            DwSqliteFixture.IdColumnNumber,
            DwBuffer.Primary,
            id);
        harness.Carrier.Store.Carrier.RowAt(row, DwBuffer.Primary).Baseline();

        // Another writer moves a NON-key column, so the delete's updatewhere predicate no longer matches.
        Assert.Equal(
            0,
            engine.Execute(
                "UPDATE COMPANY SET AGE = 99 WHERE ID = " + id,
                TestContext.Current.CancellationToken).SqlCode);

        EditColumn(harness.Carrier.Store.Carrier, row, DwSqliteFixture.IdColumnNumber, id + 100L);

        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            harness.Carrier.Target.Update(
                acceptText: true,
                resetFlag: false,
                TestContext.Current.CancellationToken));

        // NO INSERT RAN, so the table still holds exactly the winning writer's row.
        Assert.Equal("1", ScalarText(engine, "SELECT COUNT(*) FROM COMPANY"));
        Assert.Equal("0", ScalarText(engine, "SELECT COUNT(*) FROM COMPANY WHERE ID = " + (id + 100L)));
        Assert.Equal("99", ScalarText(engine, "SELECT AGE FROM COMPANY WHERE ID = " + id));

        // AND THE SHORTFALL IS THE MISMATCH. One statement was owed and none matched.
        ConcurrencyEvidence evidence = Assert.IsType<ConcurrencyEvidence>(
            harness.Carrier.CaptureConcurrencyEvidence());

        Assert.Equal(1L, evidence.RowsExpected);
        Assert.Equal(0L, evidence.RowsMatched);
        Assert.True(ConflictDetector.IsConcurrencyMismatch(evidence));
    }

    /// <summary>
    /// An unattached carrier and one whose definition names no updatable table both refuse.
    /// </summary>
    /// <remarks>
    /// THE SECOND CASE IS WHY A DERIVED DEFINITION IS USED RATHER THAN THE EVIDENCED ONE. The evidenced
    /// <c>dw_sqlite</c> definition NAMES its updatable table - <c>update="COMPANY" updatewhere=1
    /// updatekeyinplace=no</c> [dw_sqlite.srd:L14] - so a carrier over it is prepared the moment it
    /// resolves, which is exactly what makes the single-table path work with no prepare call
    /// [n_cst_thread_task_sqlupdate.sru:L370-L371]. A definition DERIVED from an arbitrary SELECT
    /// carries no update attributes at all, and that is the genuine "no updatable table" state.
    /// </remarks>
    [Fact]
    public void AnUnattachedOrUnpreparedCarrierRefusesWithTheLegacyDiagnostic()
    {
        UpdateHarness harness = new(this);
        using SqliteTransactionEngine engine = harness.Engine;

        // Never attached: the oracle's own "no transaction object" condition.
        UpdateHarness detached = new(this, attach: false);
        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            detached.Carrier.Target.Update(acceptText: true, resetFlag: false, TestContext.Current.CancellationToken));

        Assert.Equal(0, engine.Connect(TestContext.Current.CancellationToken).SqlCode);

        // Attached, and with NO modified row, over a definition that DOES name its table: a success
        // that writes nothing, exactly as the legacy reports. No prepare call was made.
        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            harness.Carrier.Target.Update(acceptText: true, resetFlag: false, TestContext.Current.CancellationToken));
        Assert.Null(harness.Carrier.CaptureConcurrencyEvidence());

        // Now point the SAME carrier at a definition derived from a bare SELECT. It declares no
        // updatable table, so the very next update refuses - and the refusal proves the contract is
        // re-seeded per data object rather than latched at construction.
        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            harness.Carrier.Create(
                GridSyntax.Compose("SELECT ID FROM COMPANY", [new DeclaredColumn(1, "id", "number")]),
                out string errors));
        Assert.Empty(errors);

        long derivedRow = harness.Carrier.Store.Carrier.AppendRow(DwBuffer.Primary, ItemStatus.New);
        _ = harness.Carrier.Store.Carrier.SetItemValue(derivedRow, 1, DwBuffer.Primary, 1L);

        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            harness.Carrier.Target.Update(acceptText: true, resetFlag: false, TestContext.Current.CancellationToken));
        Assert.Empty(harness.Carrier.Identity.Metadata.DescribeUpdateTable());
    }

    /// <summary>
    /// The carrier's metadata surface answers column counts, ids and the key-in-place setting.
    /// </summary>
    [Fact]
    public void TheCarrierMetadataSurfaceAnswersTheDescribeQuestions()
    {
        UpdateHarness harness = new(this);
        using SqliteTransactionEngine engine = harness.Engine;

        harness.PrepareCompany();

        Assert.Equal(DwSqliteFixture.ColumnCount, harness.Carrier.TargetMetadata.GetColumnCount());

        // The argument is the DESCRIBE PROPERTY `<name>.Id`, not a bare column name - the preparer
        // resolves each key column that way [n_cst_thread_task_sqlupdate.sru:L118-L122].
        Assert.Equal(
            DwSqliteFixture.IdColumnNumber,
            harness.Carrier.TargetMetadata.GetColumnId(
                DwSqliteFixture.KeyColumnName + UpdateWhereBuilder.ColumnIdSuffix));

        // An unknown column is NOT a positive id, which is what the preparer's own guard keys on - and
        // neither is a property that is not spelled as one.
        Assert.True(
            harness.Carrier.TargetMetadata.GetColumnId(
                "no_such_column" + UpdateWhereBuilder.ColumnIdSuffix) <= 0);
        Assert.True(harness.Carrier.TargetMetadata.GetColumnId(DwSqliteFixture.KeyColumnName) <= 0);

        Assert.Equal(
            UpdateWhereBuilder.NoLiteral,
            harness.Carrier.TargetMetadata.DescribeUpdateKeyInPlace());

        Assert.Equal(
            DwSqliteFixture.UpdateTableName,
            harness.Carrier.Identity.Metadata.DescribeUpdateTable());
        Assert.Equal(DwSqliteFixture.ColumnCount, harness.Carrier.Identity.Metadata.GetColumnCount());
    }

    // ==============================================================================================
    //  6. THE CALLER-SIDE PROXY HOST
    // ==============================================================================================

    /// <summary>
    /// The proxy host latches a run's outcome and reports the inverted sync-signal sense correctly.
    /// </summary>
    /// <remarks>
    /// THE SIGNAL SENSE IS INVERTED AND IT IS EASY TO GET BACKWARDS: a RUNNING task is busy while the
    /// sync signal is CLEAR, and the signal is raised when the task stops. Reading it the other way
    /// would make every busy guard fire on an idle task.
    /// </remarks>
    [Fact]
    public void TheProxyHostLatchesTheRunOutcomeAndCarriesTheInvertedSignalSense()
    {
        using SqlTaskProxyHost host = new(NullLogger<SqlTaskProxyHost>.Instance);

        Assert.False(host.IsRunning);
        Assert.False(host.IsControllerBusy);
        Assert.Equal(SqlTaskProxyHost.SoleTaskIndex, host.TaskIndex);
        Assert.Equal(RetCode.OK, host.LastExitCode);
        Assert.Empty(host.LastErrorInfo);
        Assert.Null(host.Task);

        // With no worker bound there is nothing to initialize, and a blank class name is refused.
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, host.OnInit(string.Empty));
        Assert.Equal(RetCode.E_INVALID_OBJECT, host.OnInit("n_cst_thread_task_sqlquery"));

        Assert.Equal(DataWindowBufferStore.EventContinue, host.OnPrepare());
        Assert.Equal(RetCode.OK, host.SetWorkerDelayFor(0.5d));
        Assert.Equal(RetCode.OK, host.SetWorkerSkip(true));

        host.RaiseSyncSignal();
        Assert.True(host.IsSyncSignalSet);
        host.ClearSyncSignal();
        Assert.False(host.IsSyncSignalSet);

        // A cancellation is always accepted and latches the cancelled code.
        Assert.Equal(RetCode.OK, host.Cancel());
        Assert.True(host.IsCancelled);
        Assert.Equal(RetCode.CANCELLED, host.LastExitCode);
    }

    /// <summary>
    /// Marking a run running then stopped latches the exit code and lowers the flag.
    /// </summary>
    [Fact]
    public void MarkingARunRaisesAndLowersTheFlagAndLatchesTheExitCode()
    {
        using SqlTaskProxyHost host = new(NullLogger<SqlTaskProxyHost>.Instance);

        host.MarkRunning();

        Assert.True(host.IsRunning);
        Assert.False(host.IsSyncSignalSet);

        host.LatchError(RetCode.E_DB_ERROR, "refused");

        Assert.Equal(RetCode.E_DB_ERROR, host.LastErrorCode);
        Assert.Equal("refused", host.LastErrorInfo);

        host.MarkStopped(RetCode.E_DB_ERROR);

        Assert.False(host.IsRunning);
        Assert.Equal(RetCode.E_DB_ERROR, host.LastExitCode);

        // THE RUN LATCH AND THE SYNC HANDSHAKE ARE INDEPENDENT, AND THAT IS THE ORACLE'S SHAPE. The
        // oracle raises the event and RESETS IT AGAIN inside the same script
        // [n_cst_threading_task.sru:L234 paired with :L245, and :L253 paired with :L260], so the signal
        // is a transient handshake held only for the duration of a phase - never a flag that outlives
        // the run. Stopping the run therefore leaves it low, and the proxy is what raises and clears it
        // around each notification [SqlTaskProxyBase.RaiseNotify].
        Assert.False(host.IsSyncSignalSet);

        host.RaiseSyncSignal();
        Assert.True(host.IsSyncSignalSet);
        host.ClearSyncSignal();
        Assert.False(host.IsSyncSignalSet);

        // The controller is never busy on this host: there is no controller above it to be busy.
        Assert.False(host.IsControllerBusy);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _connections.Dispose();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    // ==============================================================================================
    //  HELPERS
    // ==============================================================================================

    /// <summary>Builds an engine over this class's temporary database.</summary>
    private SqliteTransactionEngine CreateEngine() => CreateEngine(_connections);

    /// <summary>Builds an engine over a caller-supplied connection factory.</summary>
    /// <param name="connections">The factory the engine draws its connection from.</param>
    /// <returns>An unconnected engine.</returns>
    private static SqliteTransactionEngine CreateEngine(SqliteConnectionFactory connections) =>
        new(connections, NullLogger<SqliteTransactionEngine>.Instance);

    /// <summary>
    /// A second factory over the SAME database file, configured for the <c>WAL</c> journal mode.
    /// </summary>
    /// <returns>A factory whose connections apply <c>journal=WAL</c>.</returns>
    /// <remarks>
    /// WAL is the one mode in the legacy grammar's six [<c>w_test_sqlite.srw:L455</c>] under which a held
    /// write lock still admits READERS. The connect path reads <c>PRAGMA journal_mode</c> before it opens
    /// any transaction, so WAL is what separates "the connection could not be established" from "the
    /// transaction could not be begun" - without it both arms of that distinction fail at the same
    /// earlier step and neither says anything about the transaction. The mode is persistent in the file
    /// once set, which is why the holder and the engine under test both draw from THIS factory: a
    /// connection from the class-level DELETE-mode factory would try to convert the file back.
    /// </remarks>
    private SqliteConnectionFactory CreateWalConnectionFactory()
    {
        PersistenceOptions options = new()
        {
            Sqlite = new SqliteOptions
            {
                DataDirectory = _directory,
                DatabaseFileName = "test.db",
                Journal = "WAL",
            },
        };

        return new SqliteConnectionFactory(
            Options.Create(options),
            NullLogger<SqliteConnectionFactory>.Instance,
            TimeProvider.System);
    }


    /// <summary>Reads one scalar as text through an already-connected engine's own connection.</summary>
    /// <param name="engine">The connected engine.</param>
    /// <param name="sql">The statement to read one value from.</param>
    /// <returns>The value as invariant text, or the empty string when it is null.</returns>
    /// <remarks>
    /// THROUGH THE SHIPPED COMMAND SOURCE, NOT THROUGH THE ENGINE'S FIELDS. The engine publishes its
    /// connection as <c>ISqliteCommandSource.CreateCommand</c> and nothing else - no connection
    /// property and no transaction property - which is deliberate: a consumer that could reach the
    /// connection could open a second transaction on it or outlive the engine that owns it. The
    /// command it hands back is already enlisted in whatever explicit transaction is open, so a read
    /// through it sees uncommitted work exactly as the engine's own writes do, which is precisely what
    /// these cases need to observe.
    /// </remarks>
    private static string ScalarText(SqliteTransactionEngine engine, string sql)
    {
        using SqliteCommand command = engine.CreateCommand();
        command.CommandText = sql;

        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    /// <summary>Observes whether an explicit transaction is open on a connected engine.</summary>
    /// <param name="engine">The connected engine.</param>
    /// <returns>The open explicit transaction, or <see langword="null"/> under auto-commit.</returns>
    /// <remarks>
    /// Read off the command the engine hands out, because that is where the engine itself enlists a
    /// statement: whatever the command is enlisted in IS the ambient transaction, so this observes the
    /// same thing the engine acts on rather than a parallel copy of it.
    /// </remarks>
    private static SqliteTransaction? Ambient(SqliteTransactionEngine engine)
    {
        using SqliteCommand probe = engine.CreateCommand();

        return probe.Transaction;
    }

    /// <summary>
    /// Inserts sample rows whose AGE, SALARY and BIRTH values are bound AS SUPPLIED, so a case can put
    /// stored TEXT into a column the DataWindow declares numeric.
    /// </summary>
    /// <param name="rows">The rows, with the three loosely typed columns as <see cref="object"/>.</param>
    /// <remarks>
    /// SEPARATE FROM <c>Seed</c> RATHER THAN REPLACING IT, because the typed helper is what every other
    /// case in this class states its data with and a loosely typed signature there would make those rows
    /// say less than they do today. SQLite stores a value the column's affinity cannot losslessly convert
    /// AS SUPPLIED - text that is not a number stays TEXT in an INTEGER-affinity column - which is the
    /// state this helper exists to produce and the state the read-back guard answers for.
    /// </remarks>
    private void SeedRaw(params (string Name, object Age, string Address, object Salary, object Birth)[] rows)
    {
        using SqliteConnection connection = _connections
            .CreateOpenConnectionAsync(CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();

        foreach ((string name, object age, string address, object salary, object birth) in rows)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO COMPANY (NAME, AGE, ADDRESS, SALARY, BIRTH) VALUES (@n, @a, @d, @s, @b)";
            _ = command.Parameters.AddWithValue("@n", name);
            _ = command.Parameters.AddWithValue("@a", age);
            _ = command.Parameters.AddWithValue("@d", address);
            _ = command.Parameters.AddWithValue("@s", salary);
            _ = command.Parameters.AddWithValue("@b", birth);
            _ = command.ExecuteNonQuery();
        }
    }

    /// <summary>Inserts sample rows straight through the provider, bypassing the runtime.</summary>
    private void Seed(params (string Name, long Age, string Address, double Salary, string Birth)[] rows)
    {
        using SqliteConnection connection = _connections
            .CreateOpenConnectionAsync(CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();

        foreach ((string name, long age, string address, double salary, string birth) in rows)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO COMPANY (NAME, AGE, ADDRESS, SALARY, BIRTH) VALUES (@n, @a, @d, @s, @b)";
            _ = command.Parameters.AddWithValue("@n", name);
            _ = command.Parameters.AddWithValue("@a", age);
            _ = command.Parameters.AddWithValue("@d", address);
            _ = command.Parameters.AddWithValue("@s", salary);
            _ = command.Parameters.AddWithValue("@b", birth);
            _ = command.ExecuteNonQuery();
        }
    }

    /// <summary>Writes the five non-identity columns of one carrier row.</summary>
    private static void SetRow(
        DataWindowCarrier carrier,
        long row,
        string name,
        long age,
        string address,
        double salary,
        string birth,
        DwBuffer buffer = DwBuffer.Primary)
    {
        _ = carrier.SetItemValue(row, DwSqliteFixture.NameColumnNumber, buffer, name);
        _ = carrier.SetItemValue(row, DwSqliteFixture.AgeColumnNumber, buffer, age);
        _ = carrier.SetItemValue(row, DwSqliteFixture.AddressColumnNumber, buffer, address);
        _ = carrier.SetItemValue(row, DwSqliteFixture.SalaryColumnNumber, buffer, salary);
        _ = carrier.SetItemValue(row, DwSqliteFixture.BirthColumnNumber, buffer, birth);
    }

    /// <summary>
    /// Edits one already-baselined column and marks the row modified, which is what the legacy's
    /// <c>SetItem</c> does in one step.
    /// </summary>
    /// <remarks>
    /// THE STATUS IS NOT IMPLIED BY THE VALUE WRITE IN THIS PORT, AND THAT IS DELIBERATE.
    /// <c>SetItemValue</c> is the value setter and <c>SetItemStatus</c> is the status setter, mirroring
    /// the legacy's <c>SetItem</c> / <c>SetItemStatus</c> split; a caller that writes a value without
    /// declaring the row modified has produced a row the update loop skips, exactly as a DataWindow
    /// whose status was reset behaves. Every edit in this suite therefore goes through here.
    /// </remarks>
    private static void EditColumn(
        DataWindowCarrier carrier,
        long row,
        int columnNumber,
        object? value,
        DwBuffer buffer = DwBuffer.Primary)
    {
        _ = carrier.SetItemValue(row, columnNumber, buffer, value);
        _ = carrier.SetItemStatus(row, columnNumber, buffer, ItemStatus.DataModified);
        _ = carrier.SetItemStatus(
            row,
            ItemStatusMachine.RowStatusColumn,
            buffer,
            ItemStatus.DataModified);
    }

    /// <summary>Finds one projected column value by its one-based column number.</summary>
    /// <param name="values">The projected values.</param>
    /// <param name="columnNumber">The one-based column number. R9: never rebased.</param>
    /// <returns>The value.</returns>
    /// <remarks>
    /// LOOKED UP BY ORDINAL RATHER THAN BY POSITION, so an assertion cannot silently start reading a
    /// different column if the payload's column set changes.
    /// </remarks>
    private static ColumnValue ColumnOf(IEnumerable<ColumnValue> values, int columnNumber) =>
        values.Single(value => value.ColumnId == columnNumber);

    /// <summary>
    /// The transaction surface one classified attempt reads, over a real connected engine.
    /// </summary>
    /// <remarks>
    /// THE DEFAULT STATE IS THE ONE A STALE WRITE PRODUCES: the provider raised nothing, so the code is
    /// zero and the defensive override at [<c>n_cst_thread_task_sqlupdate.sru:L208-L210</c>] - which fires
    /// only on EXACTLY -1 - does not apply. That is what makes the exercise meaningful: the update's own
    /// claimed success stands, and the verdict has to come from the measurement instead. The hooks answer
    /// the non-preventing value, because a veto is a different exercise.
    /// </remarks>
    private sealed class WorkflowTransaction : IUpdateTransaction
    {
        /// <summary>The driver code the classification reads. Zero is "the provider raised nothing".</summary>
        internal long Code { get; set; }

        public long SqlCode => Code;

        public long SqlDbCode => Code;

        public string SqlErrText => string.Empty;

        public bool IsFailed() => Code < 0L;

        // The staged driver code survives the per-attempt clear, because a case sets it to reach one arm.
        public void ClearState()
        {
        }

        public long OnBeforeUpdate() => RetCode.OK;

        public void OnAfterUpdate(long updateResult) => AfterUpdateResults.Add(updateResult);

        /// <summary>Every value the after-update hook observed, in order.</summary>
        internal List<long> AfterUpdateResults { get; } = [];
    }

    /// <summary>Collects whatever the classification raises on the two error channels.</summary>
    /// <summary>
    /// An update transaction over the REAL pooled transaction, so the carrier's stamp and the
    /// classification's read are the same state.
    /// </summary>
    /// <param name="transaction">The pooled transaction the carrier is attached to.</param>
    /// <remarks>
    /// A FAITHFUL STAND-IN FOR THE SHIPPED WRAPPER rather than a stub: <c>PooledUpdateTransaction</c> is
    /// private to <c>SqlUpdateTask</c>, and every member below forwards exactly as it does. The two hooks
    /// answer the unvetoed values, because these cases are about the constraint path and a veto would
    /// return before the update ran. <see cref="ClearStateCalls"/> is counted as well as forwarded, so a
    /// case can assert the per-attempt clear actually happened.
    /// </remarks>
    private sealed class StampingTransaction(PooledTransaction transaction) : IUpdateTransaction
    {
        public long SqlCode => transaction.SqlCode;

        public long SqlDbCode => transaction.SqlDbCode;

        public string SqlErrText => transaction.SqlErrText;

        /// <summary>How many times the classification cleared the transaction's state.</summary>
        internal int ClearStateCalls { get; private set; }

        public bool IsFailed() => transaction.IsSqlFailed();

        public void ClearState()
        {
            ClearStateCalls++;
            transaction.ClearState();
        }

        public long OnBeforeUpdate() => RetCode.OK;

        public void OnAfterUpdate(long result)
        {
        }
    }

    private sealed class WorkflowErrorSink : IUpdateErrorSink
    {
        /// <summary>The database-error payloads raised, in order.</summary>
        internal List<DbErrorData> DbErrors { get; } = [];

        /// <summary>The general-channel raises, in order.</summary>
        internal List<(long Code, string ErrorText)> Raises { get; } = [];

        public void OnDbError(in DbErrorData error) => DbErrors.Add(error);

        public void OnError(long code, string errorText) => Raises.Add((code, errorText));
    }

    /// <summary>Parses grid syntax, failing the case rather than the assertion when it will not.</summary>
    private static DataObjectDefinitionEntry ParseSyntax(string syntax)
    {
        Assert.True(
            GridSyntax.TryParse(syntax, out DataObjectDefinitionEntry? entry, out string error),
            $"The composed grid syntax did not parse back: {error}");

        return entry!;
    }

    /// <summary>Returns a definition entry's declared column type by column number.</summary>
    private static string ColumnTypeOf(DataObjectDefinitionEntry entry, int columnNumber) =>
        entry.Columns.Single(column => column.Number == columnNumber).DeclaredType;

    /// <summary>The read-side collaborators, composed exactly as the container composes them.</summary>
    private sealed class RuntimeHarness
    {
        internal RuntimeHarness(PersistenceRuntimeTests owner)
        {
            Definitions = new DataObjectDefinitionRegistry();
            State = new DataWindowRuntimeState();

            Engine = owner.CreateEngine();
            Pooled = new PooledTransaction(Engine, TimeProvider.System);

            DataObjects = new DataObjectRuntime(
                Definitions,
                State,
                NullLogger<DataObjectRuntime>.Instance);

            Runtime = new QueryDataWindowRuntime(Definitions, State);

            Surface = new QueryTransactionSurface(
                NullLogger<QueryTransactionSurface>.Instance);

            Stores = new SqlDataStoreFactory(DataObjects, TimeProvider.System);
        }

        internal DataObjectDefinitionRegistry Definitions { get; }

        internal DataWindowRuntimeState State { get; }

        internal SqliteTransactionEngine Engine { get; }

        internal PooledTransaction Pooled { get; }

        internal DataObjectRuntime DataObjects { get; }

        internal QueryDataWindowRuntime Runtime { get; }

        internal QueryTransactionSurface Surface { get; }

        internal SqlDataStoreFactory Stores { get; }

        internal OwningTask Owner { get; } = new();

        /// <summary>Creates a worker-affine store, names its data object, and initializes its carrier.</summary>
        /// <remarks>
        /// THE OnInit CALL IS NOT HARNESS CEREMONY - IT IS THE LIVE PATH. <c>of_getcacheds</c> converges
        /// both its cache-hit and cache-miss arms on <c>store.OnInit(this)</c>
        /// [<c>SqlTaskBase</c>, the port of <c>n_cst_thread_task_sqlbase.sru:L568</c>], and the carrier
        /// fails fast without it because every one of its events needs the parent task to ask about
        /// thread affinity, N-char binding and cancellation
        /// [<c>n_cst_thread_task_sqlbase_ds.sru:L63</c>]. Skipping it here would test a state the
        /// service never reaches.
        /// </remarks>
        internal ISqlDataStore CreateStore(string dataObject)
        {
            ISqlDataStore store = Stores.Create(CarrierThreadAffinity.WorkerThread);

            if (dataObject.Length > 0)
            {
                store.DataObject = dataObject;
            }

            store.OnInit(Owner);

            return store;
        }
    }

    /// <summary>
    /// The owning task every carrier is initialized with - the four questions the carrier asks back.
    /// </summary>
    /// <remarks>
    /// A worker-affine carrier on a NON-N-char connection, which is the evidenced SQLite configuration:
    /// the N-char rewriter is reachable only when BOTH <c>DisableBind=1</c> and <c>NCharBind=1</c> appear
    /// in the connection string [<c>n_cst_thread_task_sqlbase.sru:L127-L132</c>], and the SQLite URI
    /// grammar carries neither [<c>w_test_sqlite.srw:L452-L456</c>].
    /// </remarks>
    private sealed class OwningTask : ICarrierParentTask
    {
        /// <summary>The statements the carrier previewed, in the order it previewed them.</summary>
        internal List<string> Previews { get; } = [];

        /// <summary>The database errors the carrier forwarded.</summary>
        internal List<string> DbErrors { get; } = [];

        /// <inheritdoc/>
        public bool IsMainThread => false;

        /// <inheritdoc/>
        public bool IsNCharBinding => false;

        /// <inheritdoc/>
        public bool IsCancelled => Cancelled;

        /// <summary>Whether this task reports itself cancelled.</summary>
        internal bool Cancelled { get; set; }

        /// <inheritdoc/>
        public long OnDbError(long sqlDbCode, string sqlErrText, string sqlSyntax, DwBuffer buffer, long row)
        {
            DbErrors.Add(sqlErrText);

            return DataWindowBufferStore.EventContinue;
        }

        /// <inheritdoc/>
        public long OnNotify(long notifyCode, long payload, string text)
        {
            Previews.Add(text);

            return DataWindowBufferStore.EventContinue;
        }
    }

    /// <summary>The write-side collaborators, plus the descriptor the evidenced fixture supplies.</summary>
    private sealed class UpdateHarness
    {
        private readonly RuntimeHarness _read;

        internal UpdateHarness(PersistenceRuntimeTests owner, bool attach = true)
        {
            _read = new RuntimeHarness(owner);

            // The shipped adapter takes the two RUNTIME collaborators it actually needs - the store
            // bindings and the definition catalogue - plus the payload codec and a logger factory.
            // The read-side query runtime and the engine resolver are NOT among them: a write carrier
            // reaches its connection through the store's attached transaction, so handing it a second
            // route to the same connection would give it two ways to disagree with itself.
            SqlUpdateCarrierAdapter adapter = new(
                _read.State,
                _read.Definitions,
                new ChangesetPayloadCodec(),
                NullLoggerFactory.Instance);

            Carrier = adapter.Adapt(_read.CreateStore(DataObjectDefinitionRegistry.EvidencedDataObject));

            if (attach)
            {
                // DataStoreSuccess (1), not RetCode.OK (0): SetTransObject reports through the
                // DataWindow's own 1/-1 alphabet, because the legacy `SetTransObject` is a DataWindow
                // function and answers 1 for success [n_cst_thread_task_sqlupdate.sru], not a RetCode.
                Assert.Equal(DataWindowBufferStore.DataStoreSuccess, Carrier.SetTransObject(_read.Pooled));
            }
        }

        internal SqliteTransactionEngine Engine => _read.Engine;

        internal PooledTransaction Pooled => _read.Pooled;

        internal ISqlUpdateCarrier Carrier { get; }

        /// <summary>Attaches an existing pooled transaction, for the stale-row case.</summary>
        internal void Attach(PooledTransaction transaction) =>
            Assert.Equal(DataWindowBufferStore.DataStoreSuccess, Carrier.SetTransObject(transaction));

        /// <summary>
        /// Applies the evidenced COMPANY update contract through the SAME modification script the
        /// update preparer generates, so this suite exercises the real parity surface rather than a
        /// shortcut around it.
        /// </summary>
        internal void PrepareCompany()
        {
            UpdatableTableCollection tables = new();

            Assert.Equal(
                RetCode.OK,
                tables.AddUpdatableTable(
                    DwSqliteFixture.UpdateTableName,
                    DwSqliteFixture.ColumnNames,
                    DwSqliteFixture.KeyColumnNames,
                    DwSqliteFixture.IdentityColumnName,
                    DwSqliteFixture.UpdateWhereMode,
                    DwSqliteFixture.UpdateKeyInPlace));

            UpdatePreparer preparer = new(
                Carrier.TargetMetadata,
                Carrier.TargetModifier,
                static (_, _) => { });

            long prepared = preparer.PrepareAndUpdate(
                tables,
                Carrier.Store.Carrier,
                static () => DataWindowBufferStore.DataStoreSuccess,
                out string errorText);

            Assert.True(
                Predicates.IsSucceeded(prepared),
                $"The evidenced update contract did not apply: {errorText}");
        }
    }

    /// <summary>A pooled transaction backed by no engine at all.</summary>
    /// <remarks>
    /// Only <c>ResolveEngine</c> is meaningful here: the resolver's negative arm is what this double
    /// exists to reach, and every other member would be a distraction from it.
    /// </remarks>
    private sealed class UnbackedTransaction : IPooledTransaction
    {
        /// <inheritdoc/>
        public long SqlCode => 0L;

        /// <inheritdoc/>
        public long SqlDbCode => 0L;

        /// <inheritdoc/>
        public long SqlNRows => 0L;

        /// <inheritdoc/>
        public string SqlErrText => string.Empty;

        /// <inheritdoc/>
        public string SqlReturnData => string.Empty;

        /// <inheritdoc/>
        public bool AutoCommit { get; set; }

        /// <inheritdoc/>
        public void StampSqlState(in SqlState state)
        {
        }

        /// <inheritdoc/>
        public long Connect() => RetCode.E_NO_IMPLEMENTATION;

        public long Connect(CancellationToken cancellationToken) => RetCode.E_NO_IMPLEMENTATION;

        /// <inheritdoc/>
        public long Disconnect() => RetCode.OK;

        /// <inheritdoc/>
        public long Rollback() => RetCode.OK;

        /// <inheritdoc/>
        public long Commit(bool autoRollback) => RetCode.E_NO_IMPLEMENTATION;

        /// <inheritdoc/>
        public long Commit() => RetCode.E_NO_IMPLEMENTATION;

        /// <inheritdoc/>
        public long AutoCommitCheckpoint() => RetCode.OK;

        /// <inheritdoc/>
        public long Exec(string? sqlCommand) => RetCode.E_NO_IMPLEMENTATION;

        public long Exec(string? sqlCommand, CancellationToken cancellationToken) =>
            RetCode.E_NO_IMPLEMENTATION;

        public long Exec(in SqlCommandText command, CancellationToken cancellationToken) =>
            RetCode.E_NO_IMPLEMENTATION;

        // NOTHING BEHIND THIS TRANSACTION, WHICH IS THE WHOLE POINT OF THE DOUBLE. A capability lookup on
        // it answers false rather than throwing, because "this provider cannot serve that" is an ordinary
        // negative every consumer publishes a defined answer for.
        public bool TryGetEngineCapability<TCapability>(
            [NotNullWhen(true)] out TCapability? capability)
            where TCapability : class
        {
            capability = null;

            return false;
        }

        /// <inheritdoc/>
        public bool IsConnected() => false;

        /// <inheritdoc/>
        public bool IsConnected(out bool broken)
        {
            broken = false;

            return false;
        }

        /// <inheritdoc/>
        public bool IsBroken() => false;

        /// <inheritdoc/>
        public long SetBroken() => RetCode.OK;

        /// <inheritdoc/>
        public void ClearState()
        {
        }

        /// <inheritdoc/>
        public DatabaseType GetDbType() => DatabaseType.DbtMssql;

        /// <inheritdoc/>
        public long ApplyTransactionData(in TransactionData transData) => RetCode.OK;

        /// <inheritdoc/>
        public bool IsSqlFailed() => false;

        /// <inheritdoc/>
        public bool IsSqlSucceeded() => true;

        /// <inheritdoc/>
        public DbErrorData CaptureError() => DbErrorData.Empty;

        /// <inheritdoc/>
        public void Dispose()
        {
        }
    }
}
