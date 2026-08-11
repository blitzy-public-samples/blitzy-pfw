// ==============================================================================================
//  PreparedStatementStoreTests - the suite that pins the ENGINE half of the leading-`@` mode
//  --------------------------------------------------------------------------------------------
//  SYSTEM UNDER TEST
//      services/persistence-service/PowerFramework.Persistence/Data/SqliteTransactionEngine.cs
//          - IPreparedStatementStore and the retained-execution arm of Execute(in SqlCommandText, ...)
//      services/persistence-service/PowerFramework.Persistence/Transactions/TransactionPool.cs
//          - SqlCommandText.CacheStatement and SqlBoundStatement.CacheStatement, the carriage
//  BEHAVIOURAL ORACLE
//      ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L396-L406  the demonstration and its comment
//      ws_objects/pfw.utility.sqlite.pbl.src/n_sqlite.sru:L32-L43  the Exec ladder the prefix is on
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L219-L238  of_Exec, the ported body
//      all READ ONLY per constraint C-C - read as specification, never edited.
//
//  WHAT THIS SUITE IS ACTUALLY PROTECTING
//  --------------------------------------------------------------------------------------------
//  AAP §0.4.3 requires C-07's `Exec` to preserve "the leading-`@` prefix execution mode". The TASK
//  half of that - interpreting the selector and removing it before the statement is composed - is
//  pinned in CommandServiceTests REGION 12, against the recording engine, because that is where the
//  statement text is decided. This file pins the other half, against a REAL SQLite database, because
//  the retained prepared form is a property of a connection and nothing but a real connection can
//  demonstrate it.
//
//  Four facts are pinned here that no amount of documentation could establish, and each one is a
//  distinct way of getting the mode wrong:
//
//    1  THE RETENTION IS REAL AND IT MATCHES. The legacy demonstration runs one statement ten times
//       in a loop under a comment saying the prefix caches it [w_test_sqlite.srw:L397]. So ten
//       executions must produce ONE retained entry and NINE matches - not ten entries, which would
//       be a store that retains and never matches, and not one entry with one match, which would be
//       a store consulted once.
//
//    2  A RETAINED STATEMENT SURVIVES A COMMIT. Microsoft.Data.Sqlite refuses to execute a command
//       whose transaction is not the connection's current one, and both commit and rollback dispose
//       the explicit transaction and open a fresh one. A retained command therefore has to be
//       re-enlisted on every execution, and a suite that only ever executed inside one transaction
//       would never notice it was not.
//
//    3  THE VALUES ARE RE-BOUND, NOT ACCUMULATED. One retained entry serves every iteration of the
//       oracle's loop only if the values change with it. An entry that kept the previous execution's
//       parameters would insert the first row's data ten times, which is data corruption that a
//       row-count assertion would pass.
//
//    4  THE STORE IS BOUNDED AND IT IS RELEASED. The legacy comment calls the mode 空间换时间 -
//       trading space for time - so the space is acknowledged, not unlimited. And a prepared
//       statement is prepared against a connection: one outliving its connection is a
//       use-after-free at the provider layer.
//
//  WHAT THIS SUITE DELIBERATELY DOES NOT ASSERT (AAP §0.8.5)
//  --------------------------------------------------------------------------------------------
//  No duration, no rate, no saving. The repository publishes no latency budget, no throughput target
//  and no availability commitment anywhere, so a timing assertion here would be inventing a
//  requirement the plan forbids asserting - and it would be flaky besides. Every assertion below is
//  a COUNT of something that happened or a row that landed.
// ==============================================================================================

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Transactions;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// The parity suite for the retained prepared-statement store the leading-<c>@</c> execution mode
/// populates.
/// </summary>
public sealed class PreparedStatementStoreTests
{
    /// <summary>
    /// The evidenced DDL, transcribed from <c>w_test_sqlite.srw:L463-L469</c> - the only DDL in the
    /// repository.
    /// </summary>
    /// <remarks>
    /// THE TYPE MISMATCHES ARE THE ORACLE'S AND ARE PRESERVED (constraint C-E): no schema is invented
    /// here, and none is needed - the mode is about how often a statement is parsed, not about what it
    /// says.
    /// </remarks>
    private const string CompanyDdl =
        "CREATE TABLE COMPANY ("
        + "ID INTEGER PRIMARY KEY AUTOINCREMENT, "
        + "NAME TEXT NOT NULL, "
        + "AGE INTEGER NOT NULL, "
        + "ADDRESS CHAR(50), "
        + "SALARY REAL, "
        + "BIRTH TEXT)";

    /// <summary>
    /// The statement the oracle's loop executes, in this port's placeholder spelling.
    /// </summary>
    /// <remarks>
    /// The oracle's own is <c>"@INSERT INTO COMPANY (NAME,AGE,ADDRESS,SALARY,BIRTH) VALUES (?, ?, ?, ?,
    /// ?)"</c> [<c>w_test_sqlite.srw:L398-L400</c>]. This is the canonical form the task hands the engine
    /// after the selector is removed and the binder has run, which is what the engine actually receives -
    /// so the two placeholder spellings never meet.
    /// </remarks>
    private const string InsertCanonical = "INSERT INTO COMPANY (NAME,AGE) VALUES (@p1, @p2)";

    // ---------------------------------------------------------------------------------------------
    //  FACT 1 - THE RETENTION IS REAL AND IT MATCHES
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// THE ORACLE'S LOOP, REPRODUCED. Ten executions of one prefixed statement retain ONE prepared form
    /// and match it nine times, and all ten rows land - so the mode neither re-prepares on every call
    /// nor stops executing after the first.
    /// </summary>
    /// <remarks>
    /// The three counts are not interchangeable. Ten misses would be a store that retains and never
    /// matches; one miss with zero hits would be a store consulted once; and ten rows is what proves the
    /// retained form was still doing the work rather than being bypassed.
    /// </remarks>
    [Fact]
    public void TenExecutionsOfOneStatementRetainOnePreparedFormAndMatchItNineTimes()
    {
        using StoreFixture fixture = new(TestContext.Current.CancellationToken);

        for (long iteration = 1; iteration <= 10; iteration++)
        {
            Assert.Equal(
                0L,
                fixture.Exec(Retained(InsertCanonical, $"name-{iteration}", iteration)));
        }

        Assert.Equal(1, fixture.Store.PreparedStatementCount);
        Assert.Equal(1L, fixture.Store.PreparedStatementMisses);
        Assert.Equal(9L, fixture.Store.PreparedStatementHits);

        Assert.Equal(10L, fixture.Scalar("SELECT COUNT(*) FROM COMPANY"));
    }

    /// <summary>
    /// THE COMPLEMENT, WITHOUT WHICH THE STORE COULD BE UNCONDITIONAL. An ordinary immediate execution
    /// consults the store not at all, so it is neither a hit nor a miss and retains nothing.
    /// </summary>
    /// <remarks>
    /// Folding immediate executions into the counters would make them describe the connection's whole
    /// traffic rather than the mode, and a reader could no longer tell a mode that was honoured from one
    /// that was ignored.
    /// </remarks>
    [Fact]
    public void AnImmediateExecutionConsultsTheStoreNotAtAll()
    {
        using StoreFixture fixture = new(TestContext.Current.CancellationToken);

        for (long iteration = 1; iteration <= 3; iteration++)
        {
            Assert.Equal(
                0L,
                fixture.Exec(Immediate(InsertCanonical, $"name-{iteration}", iteration)));
        }

        Assert.Equal(0, fixture.Store.PreparedStatementCount);
        Assert.Equal(0L, fixture.Store.PreparedStatementMisses);
        Assert.Equal(0L, fixture.Store.PreparedStatementHits);

        Assert.Equal(3L, fixture.Scalar("SELECT COUNT(*) FROM COMPANY"));
    }

    /// <summary>
    /// THE STORE IS KEYED ON THE CANONICAL TEXT, so two distinct statements retain two entries and
    /// neither matches the other.
    /// </summary>
    [Fact]
    public void TwoDistinctStatementsRetainTwoPreparedForms()
    {
        using StoreFixture fixture = new(TestContext.Current.CancellationToken);

        Assert.Equal(0L, fixture.Exec(Retained(InsertCanonical, "first", 1L)));
        Assert.Equal(
            0L,
            fixture.Exec(
                Retained("INSERT INTO COMPANY (NAME,AGE,ADDRESS) VALUES (@p1, @p2, 'x')", "second", 2L)));

        Assert.Equal(2, fixture.Store.PreparedStatementCount);
        Assert.Equal(2L, fixture.Store.PreparedStatementMisses);
        Assert.Equal(0L, fixture.Store.PreparedStatementHits);
    }

    /// <summary>
    /// THE KEY IS THE PLACEHOLDER FORM AND NOT THE INTERPOLATED FORM, which is why one entry serves every
    /// value set. Keying on the rendered text would miss on every changed value, so the store would retain
    /// everything and match nothing - and the rendered text is also the literal-bearing one, which a store
    /// must not hold (constraint C-F).
    /// </summary>
    [Fact]
    public void ChangingOnlyTheValuesStillMatchesTheOneRetainedForm()
    {
        using StoreFixture fixture = new(TestContext.Current.CancellationToken);

        // Same canonical text, deliberately DIFFERENT rendered text on each call.
        Assert.Equal(0L, fixture.Exec(Retained(InsertCanonical, "Teddy", 23L)));
        Assert.Equal(0L, fixture.Exec(Retained(InsertCanonical, "Mark", 25L)));

        Assert.Equal(1, fixture.Store.PreparedStatementCount);
        Assert.Equal(1L, fixture.Store.PreparedStatementHits);
    }

    // ---------------------------------------------------------------------------------------------
    //  FACT 2 - A RETAINED STATEMENT SURVIVES A COMMIT
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// ⚠ A RETAINED STATEMENT IS RE-ENLISTED ON EVERY EXECUTION, so it keeps working across a commit.
    /// Microsoft.Data.Sqlite refuses a command whose transaction is not the connection's current one, and
    /// a commit disposes the explicit transaction and opens a fresh one - so a command retained without
    /// re-enlistment would start failing with a transaction mismatch that has nothing to do with the
    /// caller's statement.
    /// </summary>
    /// <remarks>
    /// This is the case that would fail if the re-assignment inside the retained arm were read as
    /// defensive coding and removed. The fixture runs with explicit transactions - autocommit off - so the
    /// commit genuinely replaces the ambient transaction.
    /// </remarks>
    [Fact]
    public void ARetainedStatementKeepsWorkingAfterACommitReplacesTheAmbientTransaction()
    {
        using StoreFixture fixture = new(TestContext.Current.CancellationToken, autoCommit: false);

        Assert.Equal(0L, fixture.Exec(Retained(InsertCanonical, "before", 1L)));
        Assert.Equal(0L, fixture.Transaction.Commit(autoRollback: true));

        // The SAME retained entry, now against a transaction the command was never created with.
        Assert.Equal(0L, fixture.Exec(Retained(InsertCanonical, "after", 2L)));

        Assert.Equal(1, fixture.Store.PreparedStatementCount);
        Assert.Equal(1L, fixture.Store.PreparedStatementHits);

        Assert.Equal(0L, fixture.Transaction.Commit(autoRollback: true));
        Assert.Equal(2L, fixture.Scalar("SELECT COUNT(*) FROM COMPANY"));
    }

    /// <summary>
    /// AND ACROSS A ROLLBACK TOO, which replaces the ambient transaction by the same mechanism. The work is
    /// gone, as a rollback requires; the retained form is not.
    /// </summary>
    [Fact]
    public void ARetainedStatementKeepsWorkingAfterARollbackReplacesTheAmbientTransaction()
    {
        using StoreFixture fixture = new(TestContext.Current.CancellationToken, autoCommit: false);

        Assert.Equal(0L, fixture.Exec(Retained(InsertCanonical, "discarded", 1L)));
        Assert.Equal(0L, fixture.Transaction.Rollback());

        Assert.Equal(0L, fixture.Exec(Retained(InsertCanonical, "kept", 2L)));
        Assert.Equal(0L, fixture.Transaction.Commit(autoRollback: true));

        Assert.Equal(1L, fixture.Store.PreparedStatementHits);
        Assert.Equal(1L, fixture.Scalar("SELECT COUNT(*) FROM COMPANY"));
        Assert.Equal("kept", fixture.Scalar("SELECT NAME FROM COMPANY"));
    }

    // ---------------------------------------------------------------------------------------------
    //  FACT 3 - THE VALUES ARE RE-BOUND, NOT ACCUMULATED
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// ⚠ EACH EXECUTION'S OWN VALUES LAND, WHICH IS THE ASSERTION A ROW COUNT WOULD NOT MAKE. A retained
    /// entry that kept the previous execution's parameters would insert the first row's data three times -
    /// three rows either way, and three DIFFERENT rows only if the values were genuinely re-bound.
    /// </summary>
    [Fact]
    public void EachExecutionThroughTheRetainedFormBindsItsOwnValues()
    {
        using StoreFixture fixture = new(TestContext.Current.CancellationToken);

        Assert.Equal(0L, fixture.Exec(Retained(InsertCanonical, "Teddy", 23L)));
        Assert.Equal(0L, fixture.Exec(Retained(InsertCanonical, "Mark", 25L)));
        Assert.Equal(0L, fixture.Exec(Retained(InsertCanonical, "Paul", 32L)));

        Assert.Equal("Mark", fixture.Scalar("SELECT NAME FROM COMPANY WHERE AGE = 25"));
        Assert.Equal("Paul", fixture.Scalar("SELECT NAME FROM COMPANY WHERE AGE = 32"));
        Assert.Equal(3L, fixture.Scalar("SELECT COUNT(DISTINCT NAME) FROM COMPANY"));
    }

    /// <summary>
    /// THE ROW COUNT COMES BACK FROM THE RETAINED FORM TOO, so a caller reading <c>sql_nrows</c> after a
    /// matched execution is not reading the first execution's answer.
    /// </summary>
    [Fact]
    public void TheAffectedRowCountIsTheMatchedExecutionsOwn()
    {
        using StoreFixture fixture = new(TestContext.Current.CancellationToken);

        Assert.Equal(0L, fixture.Exec(Retained(InsertCanonical, "Teddy", 23L)));
        Assert.Equal(0L, fixture.Exec(Retained(InsertCanonical, "Mark", 23L)));

        // One row inserted per execution, so the count is one and not two.
        Assert.Equal(1L, fixture.Transaction.SqlNRows);

        // And a matched statement that touches two rows reports two.
        Assert.Equal(
            0L,
            fixture.Exec(
                new SqlBoundStatement(
                    "UPDATE COMPANY SET ADDRESS = 'x' WHERE AGE = 23",
                    "UPDATE COMPANY SET ADDRESS = @p1 WHERE AGE = @p2",
                    [new SqlBoundParameter("@p1", "x"), new SqlBoundParameter("@p2", 23L)],
                    CacheStatement: true)));

        Assert.Equal(2L, fixture.Transaction.SqlNRows);
    }

    // ---------------------------------------------------------------------------------------------
    //  FACT 4 - THE STORE IS BOUNDED AND IT IS RELEASED
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// THE STORE NEVER EXCEEDS ITS BOUND. Its capacity is read from the store rather than hard-coded, so
    /// this case keeps testing eviction if the bound ever moves.
    /// </summary>
    [Fact]
    public void TheStoreNeverExceedsItsPublishedBound()
    {
        using StoreFixture fixture = new(TestContext.Current.CancellationToken);

        int capacity = fixture.Store.PreparedStatementCapacity;

        Assert.True(capacity > 0);

        // One more distinct statement than the store can hold. Each one is a genuinely different text, so
        // each is a miss.
        for (int index = 0; index <= capacity; index++)
        {
            Assert.Equal(
                0L,
                fixture.Exec(
                    Retained(
                        $"INSERT INTO COMPANY (NAME,AGE) VALUES (@p1, @p2 + {index})",
                        $"name-{index}",
                        1L)));
        }

        Assert.Equal(capacity, fixture.Store.PreparedStatementCount);
        Assert.Equal(capacity + 1L, fixture.Store.PreparedStatementMisses);
        Assert.Equal(0L, fixture.Store.PreparedStatementHits);
    }

    /// <summary>
    /// ⚠ THE VICTIM IS THE LEAST RECENTLY MATCHED, NOT THE OLDEST. A statement the caller keeps
    /// re-executing survives however long ago it was first retained - which is the whole population the
    /// mode exists to serve, so evicting it would leave the mode helping only the statements nobody reuses.
    /// </summary>
    /// <remarks>
    /// The sequence is the one that separates the two policies. The FIRST statement retained is also the
    /// one re-executed just before the store fills, so under a least-recently-matched policy it survives
    /// and under an oldest-first policy it does not. The surviving entry is then proved by a HIT, because a
    /// count alone cannot say WHICH entry is present.
    /// </remarks>
    [Fact]
    public void TheEvictedStatementIsTheLeastRecentlyMatchedAndNotTheOldest()
    {
        using StoreFixture fixture = new(TestContext.Current.CancellationToken);

        int capacity = fixture.Store.PreparedStatementCapacity;

        // The statement whose survival is under test - retained FIRST, so an oldest-first policy would
        // choose it.
        SqlBoundStatement veteran = Retained(InsertCanonical, "veteran", 1L);

        Assert.Equal(0L, fixture.Exec(veteran));

        // Fill the rest of the store with distinct statements.
        for (int index = 1; index < capacity; index++)
        {
            Assert.Equal(
                0L,
                fixture.Exec(
                    Retained(
                        $"INSERT INTO COMPANY (NAME,AGE) VALUES (@p1, @p2 + {index})",
                        $"filler-{index}",
                        1L)));
        }

        Assert.Equal(capacity, fixture.Store.PreparedStatementCount);

        // Re-match the veteran, which re-stamps it so it is now the MOST recently matched.
        Assert.Equal(0L, fixture.Exec(veteran));

        long hitsBeforeEviction = fixture.Store.PreparedStatementHits;

        // One more distinct statement forces exactly one eviction.
        Assert.Equal(
            0L,
            fixture.Exec(
                Retained("INSERT INTO COMPANY (NAME,AGE) VALUES (@p1, @p2 + 9000)", "newcomer", 1L)));

        Assert.Equal(capacity, fixture.Store.PreparedStatementCount);

        // The veteran is still there, which a HIT proves and a count could not.
        Assert.Equal(0L, fixture.Exec(veteran));
        Assert.Equal(hitsBeforeEviction + 1L, fixture.Store.PreparedStatementHits);
    }

    /// <summary>
    /// A DISCONNECT RELEASES EVERY RETAINED STATEMENT, because a prepared statement is prepared against a
    /// connection and one outliving its connection is a use-after-free at the provider layer.
    /// </summary>
    /// <remarks>
    /// ⚠ THE COUNTERS ARE DELIBERATELY NOT RESET. They describe what this engine did over its whole life,
    /// and zeroing them on a reconnect would hide the mode's history from its only observer.
    /// </remarks>
    [Fact]
    public void ADisconnectReleasesEveryRetainedStatementButKeepsTheHistory()
    {
        using StoreFixture fixture = new(TestContext.Current.CancellationToken);

        Assert.Equal(0L, fixture.Exec(Retained(InsertCanonical, "Teddy", 23L)));
        Assert.Equal(0L, fixture.Exec(Retained(InsertCanonical, "Mark", 25L)));

        Assert.Equal(1, fixture.Store.PreparedStatementCount);

        Assert.Equal(0L, fixture.Transaction.Disconnect());

        Assert.Equal(0, fixture.Store.PreparedStatementCount);
        Assert.Equal(1L, fixture.Store.PreparedStatementMisses);
        Assert.Equal(1L, fixture.Store.PreparedStatementHits);
    }

    /// <summary>
    /// AND THE MODE STILL WORKS AFTER A RECONNECT, retaining afresh - so releasing the store does not
    /// disable the mode, it only forgets what was prepared against a connection that no longer exists.
    /// </summary>
    [Fact]
    public void TheModeStillWorksAfterAReconnect()
    {
        using StoreFixture fixture = new(TestContext.Current.CancellationToken);

        Assert.Equal(0L, fixture.Exec(Retained(InsertCanonical, "Teddy", 23L)));
        Assert.Equal(0L, fixture.Transaction.Disconnect());
        Assert.Equal(0L, fixture.Connect());

        Assert.Equal(0L, fixture.Exec(Retained(InsertCanonical, "Mark", 25L)));
        Assert.Equal(0L, fixture.Exec(Retained(InsertCanonical, "Paul", 32L)));

        Assert.Equal(1, fixture.Store.PreparedStatementCount);

        // Two misses in total - one before the disconnect and one after - and one hit after it.
        Assert.Equal(2L, fixture.Store.PreparedStatementMisses);
        Assert.Equal(1L, fixture.Store.PreparedStatementHits);
    }

    // ---------------------------------------------------------------------------------------------
    //  FAILURE HANDLING - A REFUSED STATEMENT MUST NOT OCCUPY A BOUNDED SLOT
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A NEW STATEMENT THE PROVIDER REFUSES IS NOT RETAINED. A malformed statement has no useful prepared
    /// form, and retaining one would let it occupy a slot in a bounded store for the life of the
    /// connection - so a caller could exhaust the mode with statements that never run.
    /// </summary>
    [Fact]
    public void ANewStatementTheProviderRefusesIsNotRetained()
    {
        using StoreFixture fixture = new(TestContext.Current.CancellationToken);

        // E_DB_ERROR, through the ported arm that reads SQLCode [n_cst_thread_trans.sru:L233-L235].
        Assert.NotEqual(0L, fixture.Exec(Retained("INSERT INTO NO_SUCH_TABLE VALUES (@p1, @p2)", "x", 1L)));

        Assert.Equal(0, fixture.Store.PreparedStatementCount);
        Assert.Equal(1L, fixture.Store.PreparedStatementMisses);
        Assert.Equal(0L, fixture.Store.PreparedStatementHits);
    }

    /// <summary>
    /// BUT AN ALREADY-RETAINED STATEMENT KEEPS ITS ENTRY WHEN AN EXECUTION FAILS, because the failure there
    /// is about the VALUES and not about the text - the text has already run successfully at least once.
    /// </summary>
    /// <remarks>
    /// The failing execution violates the schema's NOT NULL on <c>NAME</c> with an unchanged statement, so
    /// the only thing wrong is the value. Dropping the entry would mean one bad value cost the mode for
    /// every subsequent good one.
    /// </remarks>
    [Fact]
    public void AnAlreadyRetainedStatementKeepsItsEntryWhenAnExecutionFails()
    {
        using StoreFixture fixture = new(TestContext.Current.CancellationToken);

        Assert.Equal(0L, fixture.Exec(Retained(InsertCanonical, "Teddy", 23L)));
        Assert.Equal(1, fixture.Store.PreparedStatementCount);

        // Same text, a null into a NOT NULL column.
        Assert.NotEqual(0L, fixture.Exec(Retained(InsertCanonical, null, 25L)));

        Assert.Equal(1, fixture.Store.PreparedStatementCount);
        Assert.Equal(1L, fixture.Store.PreparedStatementHits);

        // And the entry still works, which is what the retention was for.
        Assert.Equal(0L, fixture.Exec(Retained(InsertCanonical, "Mark", 25L)));
        Assert.Equal(2L, fixture.Store.PreparedStatementHits);
        Assert.Equal(2L, fixture.Scalar("SELECT COUNT(*) FROM COMPANY"));
    }

    // ---------------------------------------------------------------------------------------------
    //  THE CARRIAGE - THE MODE HAS TO SURVIVE THE ONE CONVERSION BETWEEN THE TWO STATEMENT TYPES
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// THE MODE SURVIVES THE STATEMENT-TO-COMMAND CONVERSION, which is the only bridge between the type
    /// the task composes and the type the engine receives. Dropping it there would discard a mode the
    /// caller selected without any other layer noticing.
    /// </summary>
    /// <remarks>
    /// Asserted through the store rather than by reading the conversion, because the conversion is a
    /// default interface member and the observable consequence of it is what matters: the same statement
    /// executed twice through the bound overload produces a hit only if the mode arrived.
    /// </remarks>
    [Fact]
    public void TheModeSurvivesTheStatementToCommandConversion()
    {
        using StoreFixture fixture = new(TestContext.Current.CancellationToken);

        Assert.Equal(0L, fixture.Exec(Retained(InsertCanonical, "Teddy", 23L)));
        Assert.Equal(0L, fixture.Exec(Retained(InsertCanonical, "Mark", 25L)));

        Assert.Equal(1L, fixture.Store.PreparedStatementHits);
    }

    /// <summary>
    /// AND THE DEFAULT IS THE IMMEDIATE MODE, so every pre-existing call site keeps the behaviour it had.
    /// </summary>
    [Fact]
    public void TheDefaultCarriageIsTheImmediateMode()
    {
        Assert.False(SqlBoundStatement.Unbound("SELECT 1").CacheStatement);
        Assert.False(SqlCommandText.FromRenderedStatement("SELECT 1").CacheStatement);
        Assert.False(SqlCommandText.FromBoundStatement("SELECT @p1", "SELECT 1", [1L]).CacheStatement);

        Assert.True(
            SqlCommandText.FromBoundStatement("SELECT @p1", "SELECT 1", [1L], cacheStatement: true)
                .CacheStatement);
    }

    // ==============================================================================================
    //  FIXTURE
    // ==============================================================================================

    /// <summary>
    /// Composes a retained-mode statement whose canonical form carries two placeholders.
    /// </summary>
    /// <param name="canonical">The canonical text, with <c>@p1</c> and <c>@p2</c> placeholders.</param>
    /// <param name="name">The first value.</param>
    /// <param name="age">The second value.</param>
    /// <returns>The statement, with the mode requested.</returns>
    /// <remarks>
    /// The rendered text is composed to differ from the canonical text on every call, deliberately: it is
    /// the parity artefact and must never be what the store keys on.
    /// </remarks>
    private static SqlBoundStatement Retained(string canonical, string? name, long age) =>
        new(
            Rendered(canonical, name, age),
            canonical,
            [new SqlBoundParameter("@p1", name), new SqlBoundParameter("@p2", age)],
            CacheStatement: true);

    /// <summary>The same statement without the mode.</summary>
    /// <param name="canonical">The canonical text.</param>
    /// <param name="name">The first value.</param>
    /// <param name="age">The second value.</param>
    /// <returns>The statement, in the ordinary immediate mode.</returns>
    private static SqlBoundStatement Immediate(string canonical, string? name, long age) =>
        Retained(canonical, name, age) with { CacheStatement = false };

    /// <summary>Renders the parity text the oracle would have interpolated.</summary>
    /// <param name="canonical">The canonical text.</param>
    /// <param name="name">The first value.</param>
    /// <param name="age">The second value.</param>
    /// <returns>The rendered text.</returns>
    private static string Rendered(string canonical, string? name, long age) =>
        canonical
            .Replace("@p1", name is null ? "NULL" : $"'{name}'", StringComparison.Ordinal)
            .Replace(
                "@p2",
                age.ToString(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal);

    /// <summary>
    /// A real SQLite database with the evidenced schema, reached through the production engine and the
    /// production pooled transaction.
    /// </summary>
    /// <remarks>
    /// EVERY COLLABORATOR IS THE PRODUCTION ONE. The connection factory, the engine and the pooled
    /// transaction are the types the composition root registers, so the retained-form behaviour these
    /// cases assert cannot drift away from what the service actually composes. Nothing here is a double:
    /// a prepared statement is a provider concept, and a double would prove nothing about it.
    /// </remarks>
    private sealed class StoreFixture : IDisposable
    {
        /// <summary>The temporary directory the database file lives in.</summary>
        private readonly string _directory;

        /// <summary>The storage seam.</summary>
        private readonly Data.SqliteConnectionFactory _storage;

        /// <summary>The running test's own cancellation, threaded into every verb that accepts one.</summary>
        /// <remarks>
        /// CAPTURED ONCE RATHER THAN PASSED THIRTY TIMES. Every case below drives several statements, and
        /// the token is the same for all of them, so holding it here keeps the assertions about the store
        /// rather than about ceremony - while still giving the provider a token that a cancelled test run
        /// actually cancels.
        /// </remarks>
        private readonly CancellationToken _cancellation;

        /// <summary>Initializes the fixture, creating the database and the evidenced table.</summary>
        /// <param name="cancellation">The running test's cancellation.</param>
        /// <param name="autoCommit">
        /// Whether the connection runs with its own auto-commit on. FALSE opens an explicit transaction,
        /// which is what makes the commit and rollback cases able to replace the ambient transaction.
        /// </param>
        internal StoreFixture(CancellationToken cancellation, bool autoCommit = true)
        {
            _cancellation = cancellation;

            _directory = Path.Combine(Path.GetTempPath(), $"pfw-stmtstore-{Guid.NewGuid():n}");
            Directory.CreateDirectory(_directory);

            _storage = new Data.SqliteConnectionFactory(
                Options.Create(new PersistenceOptions
                {
                    Sqlite = new SqliteOptions
                    {
                        DataDirectory = _directory,
                        DatabaseFileName = "test.db",
                        Mode = "rwc",
                        Journal = "DELETE",
                    },
                }),
                NullLogger<Data.SqliteConnectionFactory>.Instance,
                TimeProvider.System);

            Transaction = new PooledTransactionActivator(
                () => new Data.SqliteTransactionEngine(
                    _storage,
                    NullLogger<Data.SqliteTransactionEngine>.Instance),
                TimeProvider.System).CreateDefault();

            // The DDL is applied with auto-commit on regardless, so the table is durable before any case
            // that deliberately rolls back runs.
            Transaction.AutoCommit = true;
            Assert.Equal(0L, Transaction.Connect(cancellation));
            Assert.Equal(0L, Transaction.Exec(CompanyDdl, cancellation));

            Transaction.AutoCommit = autoCommit;

            Assert.True(Transaction.TryGetEngineCapability(out Data.IPreparedStatementStore? store));
            Store = store;
        }

        /// <summary>The pooled transaction under test.</summary>
        internal IPooledTransaction Transaction { get; }

        /// <summary>The store the retained mode populates.</summary>
        internal Data.IPreparedStatementStore Store { get; }

        /// <summary>Executes a bound statement, threading this fixture's token.</summary>
        /// <param name="statement">The statement, in either mode.</param>
        /// <returns>The return code the pooled transaction answers.</returns>
        internal long Exec(SqlBoundStatement statement) => Transaction.Exec(statement, _cancellation);

        /// <summary>Executes a plain statement, threading this fixture's token.</summary>
        /// <param name="sql">The statement text.</param>
        /// <returns>The return code the pooled transaction answers.</returns>
        internal long Exec(string sql) => Transaction.Exec(sql, _cancellation);

        /// <summary>Opens the connection, threading this fixture's token.</summary>
        /// <returns>The return code the pooled transaction answers.</returns>
        internal long Connect() => Transaction.Connect(_cancellation);

        /// <summary>
        /// Reads one scalar directly, so an assertion sees storage rather than the engine's own report.
        /// </summary>
        /// <param name="sql">The statement to read.</param>
        /// <returns>The first column of the first row.</returns>
        internal object? Scalar(string sql)
        {
            Assert.True(Transaction.TryGetEngineCapability(out Data.ISqliteCommandSource? commands));

            using SqliteCommand command = commands.CreateCommand();
            command.CommandText = sql;

            return command.ExecuteScalar();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Transaction.Dispose();
            _storage.Dispose();

            // SCOPED TO A PATH THIS FIXTURE ITSELF CREATED UNDER THE TEMPORARY ROOT, never to a configured
            // storage directory: the persistence volume's state is what the paired characterization
            // captures compare against.
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
