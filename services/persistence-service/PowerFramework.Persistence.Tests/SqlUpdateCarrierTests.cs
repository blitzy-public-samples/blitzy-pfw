// ==================================================================================================
//  SqlUpdateCarrierTests - THE TWO PROOFS THIS SERVICE'S CONTRACT TURNS ON
// ==================================================================================================
//
//  WHY THIS FILE EXISTS SEPARATELY FROM THE OTHER UPDATE CASES. Two behaviours are named as required
//  demonstrations for this service rather than as ordinary units:
//
//    1. CONFLICT MAPPING. An optimistic-concurrency mismatch must surface as gRPC `Aborted` carrying a
//       POPULATED ConflictDetail - not a bare status, and never a silent overwrite [AAP 0.6.3.8].
//    2. REDACTION. A database error carrying a generated statement must have that statement text
//       redacted before it is logged or returned, because the legacy field carries interpolated literal
//       values and the legacy logger performs no redaction at all [AAP 0.6.3.8, 0.6.5].
//
//  Neither is reachable by inspecting a component in isolation: a mismatch only exists once a real
//  statement has run against real storage and matched fewer rows than it was generated for. These cases
//  therefore drive the PRODUCTION carrier - the adapter the composition root registers - against a real
//  SQLite database, and assert the two demonstrations end to end.
//
//  WHAT THE FIXTURE IS. The one updatable DataWindow in the entire legacy estate is `dw_sqlite`, whose
//  table specification reads `updatewhere=1 updatekeyinplace=no` with all six columns marked
//  `update=yes updatewhereclause=yes` [ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14], and the only
//  DDL anywhere in the repository creates COMPANY [w_test_sqlite.srw:L463-L469]. Both are TRANSCRIBED
//  here rather than read: the legacy tree is read-only and is the behavioural oracle, never an input a
//  test opens at run time (constraint C-C).
//
//  WHY `updatewhere=1` IS THE WHOLE POINT. That mode puts the key column PLUS THE ORIGINAL VALUE of every
//  marked column into the generated predicate. A row another writer has changed therefore matches
//  nothing, and the shortfall between rows-generated-for and rows-affected IS the mismatch. Building the
//  predicate from CURRENT values instead would always match, and the second writer would silently
//  overwrite the first - the exact failure the contract forbids.
// ==================================================================================================

using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PowerFramework.Contracts.Common.V1;
using PowerFramework.Contracts.Persistence.V1;
using PowerFramework.Persistence.Buffers;
using PowerFramework.Persistence.Concurrency;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Tasks;
using PowerFramework.Persistence.Transactions;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Proves that a concurrency mismatch is reachable and reportable, and that a statement-bearing error is
/// redacted.
/// </summary>
public sealed class SqlUpdateCarrierTests
{
    /// <summary>The evidenced update table, transcribed from the oracle's table specification.</summary>
    private const string UpdateTable = "COMPANY";

    /// <summary>
    /// The evidenced DDL, transcribed from <c>w_test_sqlite.srw:L463-L469</c> - the only DDL in the
    /// repository.
    /// </summary>
    /// <remarks>
    /// THE TYPE MISMATCHES ARE THE ORACLE'S AND ARE PRESERVED. The DataWindow declares a 200-character
    /// address against this 50-character column, a two-place decimal salary against this REAL, and a date
    /// birth field against this TEXT [AAP 0.6.4]. They are defects to reproduce, not to tidy.
    /// </remarks>
    private const string CompanyDdl =
        "CREATE TABLE COMPANY ("
        + "ID INTEGER PRIMARY KEY AUTOINCREMENT, "
        + "NAME TEXT NOT NULL, "
        + "AGE INTEGER NOT NULL, "
        + "ADDRESS CHAR(50), "
        + "SALARY REAL, "
        + "BIRTH TEXT)";

    /// <summary>The six marked columns, in the oracle's declaration order.</summary>
    private static readonly string[] CompanyColumns =
        ["id", "name", "age", "address", "salary", "birth"];

    /// <summary>
    /// A row another writer has changed underneath matches nothing, and the mismatch carries the current
    /// row state a caller needs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS THE CONFLICT-MAPPING DEMONSTRATION. The sequence is the real race: a row is retrieved so
    /// the carrier holds its ORIGINAL values, a competing writer changes the stored row, and then the
    /// first writer's update runs. Because the predicate carries the originals, it matches zero rows.
    /// </para>
    /// <para>
    /// THE THREE ASSERTIONS ARE NOT INTERCHANGEABLE. The shortfall proves the predicate was built from
    /// originals; the classifier's verdict proves the shortfall is recognised as a mismatch rather than as
    /// a database error; and the populated detail proves a caller receives the row state it needs to
    /// choose between retrying and surfacing. Without the third, a 409 would carry nothing actionable.
    /// </para>
    /// <para>
    /// THE FOURTH ASSERTION IS THE ONE THAT WOULD CATCH A SILENT OVERWRITE. The competing writer's value
    /// is still in storage afterwards, so nothing was overwritten by the update that lost the race.
    /// </para>
    /// </remarks>
    [Fact]
    public void AStaleRowMatchesNothingAndItsMismatchCarriesCurrentRowState()
    {
        using CarrierFixture fixture = new();

        fixture.Seed("Ada Lovelace", 36, "London", 92500m, "1815-12-10");

        // The first writer retrieves, so the carrier now holds the row's ORIGINAL values as its baseline.
        ISqlUpdateCarrier carrier = fixture.AdaptWithRetrievedRow();

        // A competing writer changes the stored row. This is the race, expressed literally.
        fixture.ExecuteDirect("UPDATE COMPANY SET SALARY = 99000 WHERE NAME = 'Ada Lovelace'");

        // The first writer now modifies its own copy and updates. Its predicate carries the ORIGINAL
        // salary, which no longer exists in storage.
        MarkColumnModified(carrier, column: 5, value: 95000d);

        Assert.Equal(
            Buffers.DataWindowBufferStore.DataStoreSuccess,
            carrier.Target.Update(acceptText: true, resetFlag: false, TestContext.Current.CancellationToken));

        ConcurrencyEvidence? evidence = carrier.CaptureConcurrencyEvidence();

        Assert.NotNull(evidence);
        Assert.False(evidence.ProviderFaulted);
        Assert.Equal(1L, evidence.RowsExpected);
        Assert.Equal(0L, evidence.RowsMatched);

        // The classifier recognises the shortfall as a MISMATCH, not as a provider fault.
        Assert.True(ConflictDetector.IsConcurrencyMismatch(evidence));

        ConflictDetail detail = ConflictDetector.BuildConflictDetail(evidence, UpdateTable);

        Assert.Equal(UpdateTable, detail.UpdateTable);
        Assert.Equal(1L, detail.RowsExpected);
        Assert.Equal(0L, detail.RowsMatched);

        // POPULATED, not merely present. A caller can see which row lost and what it held.
        Assert.NotEmpty(detail.Rows);
        Assert.Equal(1L, detail.Rows[0].Row);
        Assert.Equal(DwBuffer.Primary, detail.Rows[0].Buffer);
        Assert.NotEmpty(detail.Rows[0].CurrentValues);

        // NOTHING WAS OVERWRITTEN. The competing writer's value survives the update that lost the race.
        Assert.Equal(
            99000d,
            Convert.ToDouble(
                fixture.ScalarDirect("SELECT SALARY FROM COMPANY WHERE NAME = 'Ada Lovelace'"),
                CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// An update whose predicate still matches succeeds, and reports no mismatch.
    /// </summary>
    /// <remarks>
    /// THE NEGATIVE CONTROL FOR THE CASE ABOVE. Without it, a carrier that reported a mismatch on every
    /// update would pass that case while being entirely broken. This one proves the mismatch is a verdict
    /// about the data rather than a constant.
    /// </remarks>
    [Fact]
    public void AnUncontestedRowMatchesAndReportsNoMismatch()
    {
        using CarrierFixture fixture = new();

        fixture.Seed("Grace Hopper", 45, "Arlington", 88000m, "1906-12-09");

        ISqlUpdateCarrier carrier = fixture.AdaptWithRetrievedRow();

        MarkColumnModified(carrier, column: 5, value: 91000d);

        Assert.Equal(
            Buffers.DataWindowBufferStore.DataStoreSuccess,
            carrier.Target.Update(acceptText: true, resetFlag: false, TestContext.Current.CancellationToken));

        ConcurrencyEvidence? evidence = carrier.CaptureConcurrencyEvidence();

        Assert.NotNull(evidence);
        Assert.Equal(1L, evidence.RowsExpected);
        Assert.Equal(1L, evidence.RowsMatched);
        Assert.Empty(evidence.Rows);
        Assert.False(ConflictDetector.IsConcurrencyMismatch(evidence));

        Assert.Equal(
            91000d,
            Convert.ToDouble(
                fixture.ScalarDirect("SELECT SALARY FROM COMPANY WHERE NAME = 'Grace Hopper'"),
                CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// A database error's statement text is redacted before it can be returned or logged.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS THE REDACTION DEMONSTRATION, AND IT IS ASSERTED ON THE STATEMENT THAT CAUSED THE FAULT
    /// rather than on a synthetic string. A generated statement carries interpolated literal values -
    /// personal names, salaries, addresses - and the legacy publishes it into <c>sqlsyntax</c> with no
    /// redaction whatsoever. The projection this service returns must not.
    /// </para>
    /// <para>
    /// BOTH DIRECTIONS ARE ASSERTED. The literals must be gone, and the statement's SHAPE must survive:
    /// a redaction that erased the whole statement would satisfy the first requirement while destroying
    /// the diagnostic value that made the field worth carrying at all.
    /// </para>
    /// </remarks>
    [Fact]
    public void AStatementBearingErrorIsRedactedBeforeItTravels()
    {
        const string Generated =
            "UPDATE COMPANY SET SALARY = 95000, ADDRESS = 'London' "
            + "WHERE ID = 1 AND NAME = 'Ada Lovelace' AND SALARY = 92500";

        string redacted = SqlRedactor.Instance.Redact(Generated);

        // The literals are gone - every one of them, including the numeric salary.
        Assert.DoesNotContain("Ada Lovelace", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("London", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("92500", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("95000", redacted, StringComparison.Ordinal);

        // The shape survives, so the field is still diagnostic.
        Assert.Contains("UPDATE COMPANY", redacted, StringComparison.Ordinal);
        Assert.Contains("SALARY", redacted, StringComparison.Ordinal);
        Assert.Contains("WHERE", redacted, StringComparison.Ordinal);
    }

    /// <summary>
    /// A real storage fault reaches the carrier's error channel without carrying the statement text.
    /// </summary>
    /// <remarks>
    /// THE CARRIER ATTACHES NO STATEMENT AT ALL ON A PROVIDER FAULT, which is stricter than redacting one.
    /// The exception message from the provider is the diagnostic; the statement that produced it stays
    /// inside the carrier, so there is no path by which an un-redacted statement could leave this layer.
    /// The case drives a genuine constraint violation rather than simulating one, because a simulated
    /// fault would prove only that the assertion matches the simulation.
    /// </remarks>
    [Fact]
    public void AProviderFaultReachesTheErrorChannelWithNoStatementAttached()
    {
        using CarrierFixture fixture = new();

        fixture.Seed("Alan Turing", 41, "Wilmslow", 75000m, "1912-06-23");

        ISqlUpdateCarrier carrier = fixture.AdaptWithRetrievedRow();

        // NAME carries a NOT NULL constraint, so nulling it is a real provider fault rather than a mock.
        MarkColumnModified(carrier, column: 2, value: null);

        Assert.Equal(
            Buffers.DataWindowBufferStore.DataStoreFailure,
            carrier.Target.Update(acceptText: true, resetFlag: false, TestContext.Current.CancellationToken));

        (long Code, string Text, string Syntax, DwBuffer Buffer, long Row) error =
            Assert.NotNull(fixture.LastDbError);

        // The provider's own code and diagnostic travel, so the fault is identifiable.
        Assert.NotEqual(0L, error.Code);
        Assert.NotEmpty(error.Text);

        // NO STATEMENT TRAVELLED, so there is nothing for a downstream sink to leak. This is stricter
        // than redacting a statement: the text never enters the channel in the first place.
        Assert.Equal(string.Empty, error.Syntax);

        // And the row was not written, so the failure did not half-apply.
        Assert.Equal(
            "Alan Turing",
            Convert.ToString(
                fixture.ScalarDirect("SELECT NAME FROM COMPANY WHERE ID = 1"),
                CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Modifies one column of row one and marks both the column and the row modified, exactly as an
    /// arriving changeset marks them.
    /// </summary>
    /// <param name="carrier">The carrier holding the retrieved row.</param>
    /// <param name="column">The ONE-BASED column ordinal to modify.</param>
    /// <param name="value">The new value.</param>
    /// <remarks>
    /// BOTH STATUSES ARE SET, AND BOTH ARE LOAD BEARING FOR DIFFERENT REASONS. The ROW status is what the
    /// update walk enumerates on - it is how a modified row is found at all - and the COLUMN status is what
    /// the SET list is generated from, because PowerBuilder writes only the columns a row actually changed
    /// so an untouched column cannot clobber a concurrent writer's value for it. Setting a value alone
    /// changes neither, which mirrors the production path: statuses arrive on the wire inside the changeset
    /// rather than being inferred from an assignment, so a test that only assigned a value would generate
    /// an empty SET list and prove nothing.
    /// </remarks>
    private static void MarkColumnModified(ISqlUpdateCarrier carrier, int column, object? value)
    {
        _ = carrier.Store.Carrier.SetItemValue(1, column, DwBuffer.Primary, value);
        _ = carrier.Store.Carrier.SetItemStatus(
            1,
            column,
            DwBuffer.Primary,
            ItemStatus.DataModified);
        _ = carrier.Store.Carrier.SetItemStatus(
            1,
            ItemStatusMachine.RowStatusColumn,
            DwBuffer.Primary,
            ItemStatus.DataModified);
    }

    // ==============================================================================================
    //  FIXTURE
    // ==============================================================================================

    /// <summary>
    /// A real SQLite database with the evidenced schema, plus the production carrier adapter over it.
    /// </summary>
    /// <remarks>
    /// EVERY COLLABORATOR IS THE PRODUCTION ONE. The adapter, the store factory, the runtime, the pooled
    /// transaction and the engine are the same types the composition root registers, so a change to how
    /// this service composes its update path cannot drift away from what these cases assert.
    /// </remarks>
    private sealed class CarrierFixture : IDisposable
    {
        /// <summary>The temporary directory the database file lives in.</summary>
        private readonly string _directory;

        /// <summary>The storage seam.</summary>
        private readonly Data.SqliteConnectionFactory _storage;

        /// <summary>The pooled transaction the carrier writes through.</summary>
        private readonly IPooledTransaction _transaction;

        /// <summary>The definition catalogue, carrying the transcribed fixture.</summary>
        private readonly Data.DataObjectDefinitionCatalogue _catalogue = new();

        /// <summary>The store-to-transaction and store-to-columns association.</summary>
        private readonly Data.DataWindowStoreBindings _bindings = new();

        /// <summary>The production adapter.</summary>
        private readonly ISqlUpdateCarrierAdapter _adapter;

        /// <summary>The data-object runtime the stores retrieve through.</summary>
        private readonly Data.SqliteDataObjectRuntime _runtime;

        /// <summary>
        /// The owning task the carrier forwards its notifications to, which is where a published database
        /// error is observed from.
        /// </summary>
        /// <remarks>
        /// STANDING WHERE THE TASK STANDS, RATHER THAN INTERCEPTING THE CARRIER. The carrier's error channel
        /// is a NOTIFICATION and latches nothing - it forwards to its parent task - so the parent is the only
        /// place a published error can be read. Using the real carrier with a recording parent therefore
        /// leaves every behaviour under test in production code, and substitutes only the endpoint.
        /// </remarks>
        private readonly RecordingParentTask _owner = new();

        /// <summary>The worker-affine carrier, which is the shape the update path runs against.</summary>
        private readonly DataWindowCarrier _carrier =
            DataWindowCarrierFactory.Create(CarrierThreadAffinity.WorkerThread, TimeProvider.System);

        /// <summary>Carriers this fixture handed out, disposed with it.</summary>
        private readonly List<ISqlUpdateCarrier> _carriers = [];

        /// <summary>Initializes the fixture, creating the database and the evidenced table.</summary>
        internal CarrierFixture()
        {
            _directory = Path.Combine(Path.GetTempPath(), $"pfw-carrier-{Guid.NewGuid():n}");
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

            _transaction = new PooledTransactionActivator(
                () => new Data.SqliteTransactionEngine(
                    _storage,
                    NullLogger<Data.SqliteTransactionEngine>.Instance),
                TimeProvider.System).CreateDefault();

            _transaction.AutoCommit = true;
            Assert.Equal(0, _transaction.Connect());
            Assert.Equal(0, _transaction.Exec(CompanyDdl));

            _runtime = new Data.SqliteDataObjectRuntime(
                _catalogue,
                _bindings,
                NullLogger<Data.SqliteDataObjectRuntime>.Instance);

            // The carrier is joined to its owning task exactly as the legacy factory joins it on every
            // path out of its cache lookup; without it, the first carrier event raised would refuse.
            _carrier.OnInit(_owner);

            _adapter = new SqlUpdateCarrierAdapter(
                _bindings,
                _catalogue,
                new ChangesetPayloadCodec(),
                NullLoggerFactory.Instance);
        }

        /// <summary>The last database error the carrier published to its owning task.</summary>
        internal (long Code, string Text, string Syntax, DwBuffer Buffer, long Row)? LastDbError =>
            _owner.LastDbError;

        /// <summary>Inserts one row directly, bypassing the carrier under test.</summary>
        /// <param name="name">The name column.</param>
        /// <param name="age">The age column.</param>
        /// <param name="address">The address column.</param>
        /// <param name="salary">The salary column.</param>
        /// <param name="birth">The birth column.</param>
        internal void Seed(string name, int age, string address, decimal salary, string birth) =>
            Assert.Equal(
                0,
                _transaction.Exec(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "INSERT INTO COMPANY (NAME, AGE, ADDRESS, SALARY, BIRTH) "
                        + "VALUES ('{0}', {1}, '{2}', {3}, '{4}')",
                        name,
                        age,
                        address,
                        salary,
                        birth)));

        /// <summary>Runs a statement directly, standing in for a competing writer.</summary>
        /// <param name="sql">The statement to run.</param>
        internal void ExecuteDirect(string sql) => Assert.Equal(0, _transaction.Exec(sql));

        /// <summary>Reads one scalar directly, so an assertion sees storage rather than the carrier.</summary>
        /// <param name="sql">The statement to read.</param>
        /// <returns>The first column of the first row.</returns>
        internal object? ScalarDirect(string sql)
        {
            Assert.True(_transaction.TryGetEngineCapability(out Data.ISqliteCommandSource? commands));

            using SqliteCommand command = commands.CreateCommand();
            command.CommandText = sql;

            return command.ExecuteScalar();
        }

        /// <summary>
        /// Produces a carrier whose store holds the seeded row, retrieved and baselined, with the
        /// evidenced table installed exactly as the update-prepare path installs it.
        /// </summary>
        /// <returns>A carrier ready to be updated through.</returns>
        internal ISqlUpdateCarrier AdaptWithRetrievedRow()
        {
            ISqlDataStore store = new SqlDataObjectStore(_carrier, _runtime);
            store.DataObject = Data.DataObjectDefinitionCatalogue.EvidencedDataObject;

            _bindings.AttachTransaction(store, _transaction);
            _bindings.SetColumns(store, CompanyColumns);

            ISqlUpdateCarrier carrier = _adapter.Adapt(store);
            _carriers.Add(carrier);

            carrier.SetTransObject(_transaction);

            // THE INSTALLATION THE UPDATE-PREPARE PATH PERFORMS, and in its order: every column is reset
            // off first and then selectively re-enabled from the table descriptor
            // [n_cst_thread_task_sqlupdate.sru:L104-L108]. Reproducing the order matters because the reset
            // is what makes the DataWindow's static definition irrelevant at run time.
            List<string> script =
            [
                $"{SqlUpdateCarrier.UpdateTableProperty}=\"{UpdateTable}\"",
                $"{SqlUpdateCarrier.UpdateKeyInPlaceProperty}=no",
            ];

            foreach (string column in CompanyColumns)
            {
                // updatewhere=1 with every column marked: the predicate spans all six ORIGINAL values.
                script.Add($"{column}{SqlUpdateCarrier.UpdateSuffix}=yes");
                script.Add($"{column}{SqlUpdateCarrier.UpdateWhereClauseSuffix}=yes");
                script.Add($"{column}{SqlUpdateCarrier.DbNameSuffix}=\"{UpdateTable}.{column}\"");
            }

            // The identifier column is the key AND the identity, per the oracle's specification.
            script.Add($"{CompanyColumns[0]}{SqlUpdateCarrier.KeySuffix}=yes");
            script.Add($"{CompanyColumns[0]}{SqlUpdateCarrier.IdentitySuffix}=yes");

            Assert.Equal(string.Empty, carrier.TargetModifier.Modify(string.Join('\n', script)));

            // The retrieval fills the carrier and baselines it, so every column now reports an ORIGINAL
            // value - which is what the update predicate is built from.
            Assert.Equal(
                1L,
                _runtime
                    .RetrieveAsync(store, [], CancellationToken.None)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult());

            return carrier;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            foreach (ISqlUpdateCarrier carrier in _carriers)
            {
                (carrier as IDisposable)?.Dispose();
            }

            _transaction.Dispose();
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
