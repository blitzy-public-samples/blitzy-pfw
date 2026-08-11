// ==================================================================================================
//  SqlUpdateCarrier.cs - THE PROVISIONED UPDATE CARRIER AND ITS STATEMENT GENERATOR
//  ------------------------------------------------------------------------------------------------
//  ROLE
//  The join between the buffer model and the storage engine on the UPDATE side. `SqlUpdateTask.cs`
//  owns the update PROTOCOL - the prepare step, the self-assignment workaround, the vetoable hooks,
//  the defensive override, the identity round trip - and drives it through `ISqlUpdateCarrier`.
//  Nothing in that protocol is reimplemented, reordered or second-guessed here. What this file adds is
//  the one thing the protocol cannot do for itself: turn a carrier's rows, item statuses and
//  original-value shadow into statements and run them.
//
//  WHY THE STATEMENT GENERATOR LIVES HERE AT ALL
//  In the legacy this is `Data.Update(true, false)` on the DataWindow, and the PowerBuilder runtime
//  generates the statements from the object's own update, key and identity column flags. Those flags
//  are installed by `Concurrency/UpdateWhereBuilder.cs` through the modification script, exactly as the
//  legacy installs them; what has no managed equivalent is the generator that reads them back and emits
//  the DML. So it is written here, against the flags the prepare step already wrote.
//
//  THE CONCURRENCY CONTRACT, WHICH IS THE WHOLE REASON THIS FILE IS CAREFUL
//  `ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14` declares `updatewhere=1` and all six of its
//  columns carry `update=yes updatewhereclause=yes` [:L8-L14]. `updatewhere=1` is the "key and
//  updateable columns" mode: the generated statement's where-clause carries the key column PLUS THE
//  ORIGINAL VALUE OF EVERY MARKED COLUMN. That is why the buffer model shadows originals per column and
//  why this generator reads `GetItemOriginalValue` rather than the current value when it builds a
//  predicate. An implementation that used current values would compare a row against itself, always
//  match, and silently overwrite a concurrent writer - which is the single failure this contract exists
//  to prevent.
//
//  PARAMETERIZED, AND WHY THAT IS PARITY RATHER THAN A DEVIATION
//  The legacy interpolates literal values into the statement whenever the connection disabled bind
//  variables, and that is the mechanical root of the injection exposure the migration documents. The
//  migration explicitly permits an implementation to be SAFER where the change is unobservable, and
//  names parameterized SQL as the canonical case. So values travel as parameters while the statement
//  TEXT - the clause order, the column order, the operator spelling - is generated to the same shape,
//  and the shape is what the SQL-preview channel publishes and what a characterization recording
//  compares.
//
//  LEGACY REFERENCE (read only - never edited, never built, never shipped: constraint C-C)
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru
//          :L98-L145   _of_updateprepare, the runtime re-derivation this file's flags come from
//          :L151-L167  the self-assignment workaround, owned by the task rather than by this file
//          :L186       Data.of_ClearState()
//          :L195-L210  the vetoable hook, the update call and the DEFENSIVE OVERRIDE
//          :L204       Data.Update(true, false) - accept text TRUE, reset flag FALSE
//          :L214       success on the DataWindow channel is 1, not the return-code algebra's zero
//          :L217-L245  the identity round trip, Primary! forward and Filter! BACKWARD
//      ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14   the six marked columns and updatewhere=1
//      ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L463-L469  the estate's only DDL
//
//  NOTHING IN THIS FILE READS THE LEGACY TREE. Every ws_objects/** path above appears in a comment.
//
//  WHAT THIS FILE DELIBERATELY DOES NOT DO
//    * It never deletes, recreates or reseeds the database, and issues no DROP - see the paired-capture
//      rule in `Data/SqliteTransactionEngine.cs`.
//    * It reads no clock.
//    * It does not decide anything about conflict classification. `Concurrency/ConflictDetector.cs`
//      owns that, and this file's only contribution is the affected-row evidence it can measure.
//    * It never logs a generated statement. The statement travels on the carrier's SQL-preview channel
//      and on the database-error payload, both of which apply the service's redaction policy.
// ==================================================================================================

using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using PowerFramework.Contracts.Common.V1;
using PowerFramework.Contracts.Persistence.V1;
using PowerFramework.Persistence.Buffers;
using PowerFramework.Persistence.Concurrency;
using PowerFramework.Persistence.Data;
using PowerFramework.Persistence.Transactions;

using Predicates = PowerFramework.Shared.Kernel.Predicates;
using RetCode = PowerFramework.Shared.Kernel.RetCode;

namespace PowerFramework.Persistence.Tasks
{
    /// <summary>
    /// The provisioned <see cref="ISqlUpdateCarrierAdapter"/>: it wraps a store as an update-capable
    /// carrier over the evidenced storage engine.
    /// </summary>
    /// <remarks>
    /// A SINGLETON, because it holds nothing: every piece of per-update state lives on the carrier it
    /// produces, and the collaborators it hands on are themselves stateless or singletons. Keeping it
    /// injectable is what lets the whole update orchestration be driven with no carrier at all, which is
    /// how the protocol's arms stay reachable without a database.
    /// </remarks>
    internal sealed class SqlUpdateCarrierAdapter : ISqlUpdateCarrierAdapter
    {
        /// <summary>The store-to-transaction association the generator reads its connection through.</summary>
        private readonly DataWindowStoreBindings _bindings;

        /// <summary>The definition catalogue a syntax-built carrier registers into.</summary>
        private readonly DataObjectDefinitionCatalogue _catalogue;

        /// <summary>The one changeset payload codec, shared by every carrier this adapter produces.</summary>
        /// <remarks>
        /// INJECTED RATHER THAN CONSTRUCTED, because the codec owns the two documented changeset defects -
        /// rows may be lost on a sorted multi-block carrier, and a reset breaks a later apply - and a
        /// second instance would be a second place for either to be got wrong.
        /// </remarks>
        private readonly IChangesetPayloadCodec _payloads;

        /// <summary>Creates loggers for the carriers this adapter produces.</summary>
        private readonly ILoggerFactory _loggerFactory;

        /// <summary>Initializes the adapter.</summary>
        /// <param name="bindings">The store-to-transaction association.</param>
        /// <param name="catalogue">The definition catalogue.</param>
        /// <param name="payloads">The changeset payload codec.</param>
        /// <param name="loggerFactory">The factory carrier loggers are created from.</param>
        public SqlUpdateCarrierAdapter(
            DataWindowStoreBindings bindings,
            DataObjectDefinitionCatalogue catalogue,
            IChangesetPayloadCodec payloads,
            ILoggerFactory loggerFactory)
        {
            _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
            _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
            _payloads = payloads ?? throw new ArgumentNullException(nameof(payloads));
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        }

        /// <inheritdoc/>
        public ISqlUpdateCarrier Adapt(ISqlDataStore store)
        {
            ArgumentNullException.ThrowIfNull(store);

            return new SqlUpdateCarrier(
                store,
                _bindings,
                _catalogue,
                _payloads,
                _loggerFactory.CreateLogger<SqlUpdateCarrier>());
        }
    }

    /// <summary>
    /// The provisioned <see cref="ISqlUpdateCarrier"/>: one store, its update executor, and the three
    /// describe/modify surfaces the prepare step and the identity round trip read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ONE CARRIER PER UPDATE, WHICH IS WHY IT IS NOT A SINGLETON AND NOT REGISTERED. It holds the
    /// per-update column flags the prepare step installs and the affected-row evidence the classifier
    /// reads, and sharing either between two concurrent updates would let one update's flags govern the
    /// other's statements. The adapter above is the substitution point.
    /// </para>
    /// <para>
    /// THE FOUR SURFACES ARE THIS SAME OBJECT, DELIBERATELY. The legacy has no split - a datastore is at
    /// once the update executor, the describe surface, the modify surface and the value source - so
    /// splitting them across four objects here would mean four objects that must agree about one
    /// DataWindow's column flags, which is a way for the port to be wrong that nothing would report.
    /// </para>
    /// </remarks>
    internal sealed class SqlUpdateCarrier
        : ISqlUpdateCarrier, IUpdateTarget, IUpdateTargetMetadata, IUpdateTargetModifier,
          IIdentityColumnMetadata, IIdentityValueSource
    {
        /// <summary>The property name the update table is described and modified through.</summary>
        internal const string UpdateTableProperty = "DataWindow.Table.UpdateTable";

        /// <summary>The property name the key-in-place setting is described through.</summary>
        internal const string UpdateKeyInPlaceProperty = "DataWindow.Table.UpdateKeyInPlace";

        /// <summary>The describe result a property with no value carries.</summary>
        internal const string UnsetDescribeResult = "";

        /// <summary>The suffix of the per-column update flag property.</summary>
        internal const string UpdateSuffix = ".Update";

        /// <summary>The suffix of the per-column key flag property.</summary>
        internal const string KeySuffix = ".Key";

        /// <summary>The suffix of the per-column identity flag property.</summary>
        internal const string IdentitySuffix = ".Identity";

        /// <summary>The suffix of the per-column update-where-clause flag property.</summary>
        internal const string UpdateWhereClauseSuffix = ".UpdateWhereClause";

        /// <summary>The suffix of the per-column database-name property.</summary>
        internal const string DbNameSuffix = ".DBName";

        /// <summary>The suffix of the per-column identifier property.</summary>
        internal const string IdSuffix = ".ID";

        /// <summary>The store this carrier wraps.</summary>
        private readonly ISqlDataStore _store;

        /// <summary>The store-to-transaction association.</summary>
        private readonly DataWindowStoreBindings _bindings;

        /// <summary>The definition catalogue a syntax-built carrier registers into.</summary>
        private readonly DataObjectDefinitionCatalogue _catalogue;

        /// <summary>The changeset payload codec the payload arm delegates to.</summary>
        private readonly IChangesetPayloadCodec _payloads;

        /// <summary>Records generator faults the DataWindow alphabet alone would not explain.</summary>
        private readonly ILogger<SqlUpdateCarrier> _logger;

        /// <summary>
        /// The per-column properties the prepare step installs through <see cref="Modify"/>.
        /// </summary>
        /// <remarks>
        /// ORDINAL AND CASE-INSENSITIVE, matching PowerBuilder's own property lookup. This dictionary is
        /// the managed stand-in for the DataWindow object's own attribute store, and the generator below
        /// reads it back exactly as the legacy runtime reads the object's flags.
        /// </remarks>
        private readonly Dictionary<string, string> _properties =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The data object whose table-level update settings are currently installed, or
        /// <see langword="null"/> when none has been.
        /// </summary>
        /// <remarks>
        /// A NAME RATHER THAN A FLAG, so the seed is RE-DONE when the store is pointed at a different
        /// definition. A carrier is reused across definitions - <c>Create</c> derives one from a syntax
        /// and assigns it to the same store - and a latched flag would carry the first definition's update
        /// table into the second, so an update over a derived retrieve-only definition would reach a table
        /// the caller never named. Re-seeding is also why the previous seed is CLEARED first.
        /// </remarks>
        private string? _installedContractDataObject;

        /// <summary>The rows the last update inserted, updated and deleted.</summary>
        private long _inserted;

        /// <summary>The rows the last update updated.</summary>
        private long _updated;

        /// <summary>The rows the last update deleted.</summary>
        private long _deleted;

        /// <summary>The affected-row evidence the last update measured, or <see langword="null"/>.</summary>
        private ConcurrencyEvidence? _evidence;

        /// <summary>Whether this carrier has been disposed.</summary>
        private bool _disposed;

        /// <summary>Initializes the carrier.</summary>
        /// <param name="store">The store to wrap.</param>
        /// <param name="bindings">The store-to-transaction association.</param>
        /// <param name="catalogue">The definition catalogue.</param>
        /// <param name="payloads">The changeset payload codec.</param>
        /// <param name="logger">The logger generator faults are recorded through.</param>
        internal SqlUpdateCarrier(
            ISqlDataStore store,
            DataWindowStoreBindings bindings,
            DataObjectDefinitionCatalogue catalogue,
            IChangesetPayloadCodec payloads,
            ILogger<SqlUpdateCarrier> logger)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
            _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
            _payloads = payloads ?? throw new ArgumentNullException(nameof(payloads));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc/>
        public ISqlDataStore Store => _store;

        /// <inheritdoc/>
        public IUpdateTarget Target => this;

        /// <inheritdoc/>
        public IUpdateTargetMetadata TargetMetadata => this;

        /// <inheritdoc/>
        public IUpdateTargetModifier TargetModifier => this;

        /// <inheritdoc/>
        public IdentityTableSurfaces Identity => new(this, this);

        /// <inheritdoc/>
        /// <remarks>
        /// Delegates to the same grid-syntax parse the retrieval side uses, so a syntax that builds a
        /// retrieval carrier builds an update carrier identically. Only the diagnostic differs, because
        /// the update path's own arm reports <see cref="RetCode.E_INVALID_SQL"/> against it.
        /// </remarks>
        public long Create(string sqlSyntax, out string errors)
        {
            ArgumentNullException.ThrowIfNull(sqlSyntax);

            string[] columns = GridSyntax.ColumnsOf(sqlSyntax);
            string select = GridSyntax.SelectOf(sqlSyntax);

            if (columns.Length == 0 || select.Length == 0)
            {
                errors = SqliteQueryDataWindowRuntime.NoColumnsText;

                return DataWindowBufferStore.DataStoreFailure;
            }

            string name = SqliteQueryDataWindowRuntime.DerivedDataObjectPrefix
                + string.GetHashCode(sqlSyntax, StringComparison.Ordinal)
                    .ToString("x8", CultureInfo.InvariantCulture);

            // The declared types travel with the definition, for the reason recorded on Register: a
            // definition registered without them resolves to the EVIDENCED fixture's six columns, so a
            // carrier created from a two-column syntax would describe six columns of another table.
            IReadOnlyList<DeclaredDataObjectColumn>? declared =
                GridSyntax.TryParse(sqlSyntax, out DataObjectDefinitionEntry? parsed, out _)
                    ? parsed.Columns
                    : null;

            _catalogue.Register(
                new DataObjectDefinition(
                    DataObject: name,
                    SqlSelect: select,
                    Sort: string.Empty,
                    Filter: string.Empty,
                    Processing: DataObjectDefinitionCatalogue.EvidencedProcessing,
                    Arguments: DataObjectDefinitionCatalogue.NoArguments,
                    Units: DataObjectDefinitionCatalogue.ResolvedUnits),
                declared);

            _bindings.SetColumns(_store, columns);
            _store.DataObject = name;

            errors = string.Empty;

            return DataWindowBufferStore.DataStoreSuccess;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Delegated to the sibling payload codec rather than decoded here, because the codec already
        /// owns the two documented changeset defects and a second decoder would be a second place for
        /// them to be got wrong. A <see langword="null"/> payload is the oracle's zero-length
        /// <c>Blob("")</c> and answers the failure value, which the caller tells apart from a rejected
        /// payload by the update-row count.
        /// </remarks>
        public long SetChanges(CarrierState? changes)
        {
            if (changes is null)
            {
                return DataWindowBufferStore.DataStoreFailure;
            }

            return _payloads.TryApply(_store.Carrier, changes);
        }

        /// <inheritdoc/>
        public long SetTransObject(IPooledTransaction? transaction)
        {
            if (transaction is null)
            {
                return DataWindowBufferStore.DataStoreFailure;
            }

            _bindings.AttachTransaction(_store, transaction);

            return DataWindowBufferStore.DataStoreSuccess;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// READ AFTER THE UPDATE HAS RUN, WHICH IS THE ONLY MOMENT THIS IMPLEMENTATION CAN MEASURE IT.
        /// The interface documents that an implementation which cannot measure beforehand answers
        /// <see langword="null"/> and is no worse off than the oracle, which measures nothing at all - so
        /// before the first update this answers null, and afterwards it answers what the statements
        /// actually affected. That keeps every conflict arm reachable with no database while still giving
        /// the classifier real evidence when there is some.
        /// </remarks>
        public ConcurrencyEvidence? CaptureConcurrencyEvidence() => _evidence;

        /// <inheritdoc/>
        /// <remarks>
        /// Clears the row-count accumulators and the measured evidence, and NOTHING ELSE. It is
        /// emphatically not a buffer reset: the caller owns the carrier's rows and the oracle resets
        /// nothing here.
        /// </remarks>
        public void ClearState()
        {
            _inserted = 0L;
            _updated = 0L;
            _deleted = 0L;
            _evidence = null;

            _store.ClearState();
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <para>
        /// THE RETURN VALUE IS THE DATAWINDOW UPDATE CONTRACT'S OWN: <c>1</c> for success and <c>-1</c>
        /// for failure, never the return-code algebra. The caller's defensive override then reconciles a
        /// claimed success against the transaction's SQL code, and that reconciliation is the caller's -
        /// this member reports what the statements did.
        /// </para>
        /// <para>
        /// THE RESET FLAG IS HONOURED BY NOT RESETTING. The caller passes <see langword="false"/>, and the
        /// consequence is a contract: item statuses and the original-value shadow SURVIVE the call, which
        /// is what makes a retry, an identity round trip and a conflict report possible at all. This
        /// implementation therefore never baselines, never clears a status and never discards a row.
        /// </para>
        /// <para>
        /// THE THREE BUFFERS ARE WALKED IN THE ORDER A RELATIONAL STORE REQUIRES rather than in buffer
        /// declaration order: deletes first, then modifications, then inserts. Deleting before inserting
        /// is what lets a key-change expressed as delete-plus-insert - which is exactly what
        /// <c>updatekeyinplace=no</c> produces, and the evidenced fixture sets it - apply without the
        /// insert colliding with the row the delete is about to remove.
        /// </para>
        /// </remarks>
        public long Update(bool acceptText, bool resetFlag, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // OBSERVED BEFORE THE FIRST STATEMENT IS GENERATED, AND NOT AGAIN AFTERWARDS. Once the first
            // statement has run the unit of work is partly applied, and abandoning it midway would leave
            // the carrier's item statuses describing a state the database is not in - which is the one
            // outcome the reset-flag contract exists to prevent. A cancelled call therefore either does
            // nothing at all or runs to completion and lets the caller's rollback decide.
            //
            // REPORTED AS THE DATAWINDOW FAILURE VALUE rather than raised, because this member's return
            // alphabet is the DataWindow update contract's - 1 for success, -1 for failure - and not the
            // return-code algebra.
            if (cancellationToken.IsCancellationRequested)
            {
                return DataWindowBufferStore.DataStoreFailure;
            }

            string table = Describe(UpdateTableProperty);

            if (table.Length == 0)
            {
                _logger.LogError(
                    "An update was attempted on a carrier with no update table installed, so no "
                        + "statement could be generated.");

                return DataWindowBufferStore.DataStoreFailure;
            }

            IPooledTransaction? transaction = _bindings.GetTransaction(_store);

            // THE THIRD CONDITION IS THE ONE A CAPABILITY CHECK ALONE MISSES: a transaction attached but
            // never connected hands out a command source that refuses every command. Asked rather than
            // caught, because this member answers in the datastore alphabet.
            if (transaction is null
                || !transaction.TryGetEngineCapability(out ISqliteCommandSource? commands)
                || !commands.CanCreateCommand)
            {
                _logger.LogError(
                    "An update was attempted through a carrier with no transaction able to supply a "
                        + "command, so it could not reach the storage engine.");

                return DataWindowBufferStore.DataStoreFailure;
            }

            // The update-start hook fires before any statement is generated, which is the position the
            // worker-side carrier's own override depends on. A prevention is a veto rather than a fault.
            if (Predicates.IsPrevented(_store.Carrier.OnUpdateStart()))
            {
                return DataWindowBufferStore.DataStoreSuccess;
            }

            UpdateColumnPlan plan = BuildPlan();

            if (plan.UpdatableColumns.Count == 0)
            {
                _logger.LogError(
                    "An update was attempted on a carrier with no updatable column installed, so no "
                        + "statement could be generated.");

                return DataWindowBufferStore.DataStoreFailure;
            }

            long inserted = 0L;
            long updated = 0L;
            long deleted = 0L;
            long affected = 0L;

            // THE ROWS WHOSE PREDICATE MATCHED NOTHING, RECORDED AS THEY ARE FOUND. A statement that
            // affected zero rows under updatewhere=1 is the concurrency mismatch itself - the predicate
            // carried every marked column's ORIGINAL value, so nothing matching means another writer had
            // changed the row. Collecting the pair here rather than re-deriving it afterwards is what lets
            // the conflict detail carry the CURRENT row state the contract requires, because after the
            // walk there is no longer any record of which row it was.
            List<(DwBuffer Buffer, long Row)> unmatched = [];

            try
            {
                // DELETES FIRST. See the remarks: a key change under updatekeyinplace=no is a delete
                // plus an insert, and the insert must not collide with the row being removed.
                //
                // EVERY ROW OF THE DELETE BUFFER, AND NOT ONLY THE MODIFIED ONES. A row is IN that buffer
                // because something deleted it - membership is the pending delete - so its item status
                // says nothing about whether a statement is owed. An ordinary retrieved-then-deleted row
                // sits there as NotModified!, and the status machine's own delete-buffer predicate counts
                // exactly that status towards the update total [ItemStatusMachine.IsDeleteCountable],
                // which is the reading that settles it: filtering this walk by "modified" would silently
                // drop the commonest delete there is.
                foreach (long row in RowsOf(DwBuffer.Delete))
                {
                    long removed = ApplyDelete(commands, table, plan, row);

                    if (removed == 0L)
                    {
                        unmatched.Add((DwBuffer.Delete, row));
                    }

                    affected += removed;
                    deleted++;
                }

                foreach (DwBuffer buffer in ModifiableBuffers)
                {
                    // EVERY ROW, WITH THE STATUS DECIDING WHAT IS OWED, rather than a modified-row walk
                    // that decides it in advance. The two differ on exactly one status and the difference
                    // is a lost INSERT: New! is "inserted, not yet edited through this carrier", and it
                    // is a status the in-scope oracle never names literally
                    // [shared/PowerFramework.Contracts/Proto/common.v1.proto, the ItemStatus domain], so
                    // no legacy arm settles it. What does settle it is that a row present in the primary
                    // buffer and absent from storage has exactly one faithful statement - an INSERT - and
                    // that writing values through the buffer surface does NOT flip a status, deliberately
                    // and by design [CarrierRow.SetValue]. Selecting on "modified" would therefore make
                    // an inserted row's survival depend on whether something also called SetItemStatus,
                    // which is a way for a caller to lose a row silently.
                    foreach (long row in RowsOf(buffer))
                    {
                        ItemStatus status = _store.Carrier.GetItemStatus(
                            row,
                            ItemStatusMachine.RowStatusColumn,
                            buffer);

                        if (status is ItemStatus.New or ItemStatus.NewModified)
                        {
                            affected += ApplyInsert(commands, table, plan, buffer, row);
                            inserted++;
                            continue;
                        }

                        if (status == ItemStatus.DataModified)
                        {
                            long changed = ApplyUpdate(commands, table, plan, buffer, row);

                            if (changed == 0L)
                            {
                                unmatched.Add((buffer, row));
                            }

                            affected += changed;
                            updated++;
                        }

                        // NotModified! generates nothing, which is the whole point of the status machine.
                    }
                }
            }
            catch (SqliteException failure)
            {
                // The fault reaches the caller through the carrier's own database-error channel, which is
                // where the ported arms read it, and the statement text is NOT attached: it may carry
                // interpolated literal values, and the channel that publishes it applies the service's
                // redaction policy of its own accord.
                _ = _store.Carrier.OnDbError(
                    SqliteConnectionFactory.MapSqliteResultCode(
                        failure.SqliteErrorCode,
                        failure.SqliteExtendedErrorCode),
                    failure.Message,
                    string.Empty,
                    DwBuffer.Primary,
                    0L);

                _logger.LogError(failure, "An update failed inside the storage engine.");

                return DataWindowBufferStore.DataStoreFailure;
            }

            _inserted = inserted;
            _updated = updated;
            _deleted = deleted;

            long expected = inserted + updated + deleted;

            // NO STATEMENT MEANS NO EVIDENCE, AND THE ABSENCE IS THE ANSWER. An update over a carrier with
            // nothing modified is an ordinary success that writes nothing, and there is no reconciliation
            // to report - so the evidence stays null rather than becoming a zero-versus-zero record that a
            // caller would have to interpret. The classifier's own question is "did fewer rows match than
            // statements were generated for", which is not askable of a walk that generated none.
            if (expected == 0L)
            {
                _evidence = null;

                _store.Carrier.OnUpdateEnd(inserted, updated, deleted);

                return DataWindowBufferStore.DataStoreSuccess;
            }

            // THE EVIDENCE THE CLASSIFIER READS. Expected is the number of rows the statements were
            // generated for; actual is what the store reported affecting. Under updatewhere=1 a shortfall
            // is precisely a concurrency mismatch: the predicate carried every marked column's ORIGINAL
            // value, so a row that failed to match is a row another writer had changed.
            _evidence = new ConcurrencyEvidence
            {
                RowsExpected = expected,
                RowsMatched = affected,
                Rows = ProjectUnmatchedRows(plan, unmatched),
            };

            _store.Carrier.OnUpdateEnd(inserted, updated, deleted);

            // A SHORTFALL IS *NOT* REPORTED HERE, AND THAT IS FAITHFUL RATHER THAN LENIENT. This member's
            // return value is `Data.Update(true,false)`'s [n_cst_thread_task_sqlupdate.sru:L204], and
            // PowerBuilder answers 1 when every generated statement executed without a DBMS ERROR. A
            // statement that matched zero rows is not a DBMS error on any provider - the driver returns a
            // count of zero and raises nothing - so the oracle answers success and the zero-row condition
            // is visible only through the row counts. Reporting -1 here would put the concurrency verdict
            // in two places at once and would take it out of the one place the contract puts it:
            // ConflictDetector reads the evidence above, IsConcurrencyMismatch distinguishes a shortfall
            // from a provider fault, and BuildConflictDetail mints the Aborted-to-409 payload with the
            // current row state (AAP 0.3.1, 0.6.3.8). NOTHING IS SILENTLY OVERWRITTEN either way: the
            // predicate carried the originals, so the losing statement changed no row at all.
            //
            // The one place a claimed success IS rewritten into a failure is the task above, and it keys on
            // the TRANSACTION's SQL code rather than on a row count - the oracle's own defensive override
            // at [:L208-L210].
            return DataWindowBufferStore.DataStoreSuccess;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Reads the installed column count when the prepare step has installed one, and otherwise falls
        /// back to the count the store's own definition declares. Both are needed: the prepare step
        /// installs a count only in the multi-table mode, and the single-table mode never prepares at all
        /// - which is a legacy asymmetry the migration preserves rather than harmonizes.
        /// </remarks>
        public int GetColumnCount()
        {
            string described = Describe(UpdateWhereBuilder.ColumnCountProperty);

            if (int.TryParse(described, CultureInfo.InvariantCulture, out int installed) && installed > 0)
            {
                return installed;
            }

            return ColumnModel().Length;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Resolves a <c>&lt;column&gt;.ID</c> property to the column's ONE-BASED number. A name that
        /// resolves to nothing answers zero, which the caller treats as an internal error - the oracle
        /// fails with a Chinese diagnostic when the resolution yields a non-positive identifier
        /// [<c>n_cst_thread_task_sqlupdate.sru:L118-L122</c>], and answering zero is what reaches that arm.
        /// </remarks>
        public int GetColumnId(string columnIdProperty)
        {
            ArgumentNullException.ThrowIfNull(columnIdProperty);

            string name = TrimSuffix(columnIdProperty, IdSuffix);

            if (name.Length == 0)
            {
                return 0;
            }

            string[] columns = ColumnModel();

            for (int index = 0; index < columns.Length; index++)
            {
                if (string.Equals(columns[index], name, StringComparison.OrdinalIgnoreCase))
                {
                    // ONE-BASED. Named rather than written inline because one-based to zero-based
                    // translation is the single most dangerous mechanical hazard in this port.
                    return index + 1;
                }
            }

            return 0;
        }

        /// <inheritdoc/>
        public string DescribeUpdateKeyInPlace() => Describe(UpdateKeyInPlaceProperty);

        /// <inheritdoc/>
        public string DescribeUpdateTable() => Describe(UpdateTableProperty);

        /// <inheritdoc/>
        /// <remarks>
        /// <b>A YES/NO LITERAL ALWAYS, AND NEVER THE EMPTY STRING.</b> PowerBuilder answers
        /// <c>Describe("#3.Identity")</c> with <c>yes</c> or <c>no</c> for every column ordinal it can
        /// resolve, so the empty answer an uninstalled property would otherwise produce is a shape the
        /// oracle never emits. The distinction is not cosmetic even though the identity resolver's own
        /// test is against <see cref="UpdateWhereBuilder.YesLiteral"/> and would read either as
        /// "not the identity column": the property also travels into diagnostics and into the describe
        /// projection a caller reads back, and an empty answer there reads as "the DataWindow could not
        /// answer" rather than as "this column is not the identity".
        /// </remarks>
        public string DescribeColumnIdentity(string identityProperty)
        {
            ArgumentNullException.ThrowIfNull(identityProperty);

            string described = Describe(identityProperty);

            return described.Length != 0 ? described : UpdateWhereBuilder.NoLiteral;
        }

        /// <inheritdoc/>
        public string DescribeColumnDbName(string dbNameProperty)
        {
            ArgumentNullException.ThrowIfNull(dbNameProperty);

            // A DBName that was never installed answers the column's own name, which is what PowerBuilder
            // answers for a column whose database name matches its DataWindow name - the ordinary case,
            // and the case the evidenced fixture is in for all six of its columns.
            string described = Describe(dbNameProperty);

            if (described.Length > 0)
            {
                return described;
            }

            string name = TrimSuffix(dbNameProperty, DbNameSuffix);

            return name.Length > 0 ? name : UnsetDescribeResult;
        }

        /// <inheritdoc/>
        public string Modify(string modificationScript)
        {
            ArgumentNullException.ThrowIfNull(modificationScript);

            // The definition's own settings first, so a script overrides them rather than being overwritten
            // by a seed that had not run yet.
            EnsureDefinitionContract();

            if (modificationScript.Length == 0)
            {
                // PowerBuilder answers a description rather than raising, and the caller tests for a
                // non-empty string [n_cst_thread_task_sqlupdate.sru:L145].
                return "empty modification script";
            }

            // The script arrives as one or more newline-separated `property = value` lines, which is the
            // shape UpdateWhereBuilder composes. Each is applied in order, and the FIRST failure stops the
            // application and is reported - matching PowerBuilder, which stops at the first bad line.
            foreach (string line in modificationScript.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int separator = line.IndexOf('=', StringComparison.Ordinal);

                if (separator <= 0)
                {
                    return "malformed modification script line: " + line;
                }

                string property = line[..separator].Trim();
                string value = line[(separator + 1)..].Trim();

                // EITHER QUOTE CHARACTER, because the two composers do not agree and neither is wrong.
                // UpdateWhereBuilder emits the oracle's own form - `DataWindow.Table.UpdateTable =
                // 'COMPANY'` with SINGLE quotes [n_cst_thread_task_sqlupdate.sru:L143] - while the
                // retrieval-side scripts use double quotes. Stripping only one form left the other's
                // quotes IN the value, so the update table came out as `'COMPANY'` and every generated
                // statement named a table whose name included the quotes; in SQLite that is a string
                // literal rather than an identifier, so the statement fails.
                //
                // ONLY A MATCHED PAIR, so a value that legitimately contains a quote survives.
                if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
                {
                    value = value[1..^1];
                }

                _properties[property] = value;
            }

            return string.Empty;
        }

        /// <inheritdoc/>
        public long GetInsertedCount() => _inserted;

        /// <inheritdoc/>
        public long GetUpdatedCount() => _updated;

        /// <inheritdoc/>
        public long GetDeletedCount() => _deleted;

        /// <inheritdoc/>
        public long RowCount() => _store.Carrier.RowCount();

        /// <inheritdoc/>
        public long FilteredCount() => _store.Carrier.FilteredCount();

        /// <inheritdoc/>
        public ItemStatus GetItemStatus(long row, int columnIndex, DwBuffer buffer) =>
            _store.Carrier.GetItemStatus(row, columnIndex, buffer);

        /// <inheritdoc/>
        public long? GetItemNumber(long row, int columnNumber) =>
            GetItemNumber(row, columnNumber, DwBuffer.Primary, originalValue: false);

        /// <inheritdoc/>
        public long? GetItemNumber(long row, int columnNumber, DwBuffer buffer, bool originalValue)
        {
            object? value = originalValue
                ? _store.Carrier.GetItemOriginalValue(row, columnNumber, buffer)
                : _store.Carrier.GetItemValue(row, columnNumber, buffer);

            return ToNumber(value);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The carrier holds no unmanaged resource of its own - the connection belongs to the pooled
        /// transaction and the rows belong to the store - so this releases the installed flags and marks
        /// the object unusable. Implemented rather than omitted because the interface requires it and
        /// because the update task disposes the carrier it was handed.
        /// </remarks>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _properties.Clear();
            _evidence = null;
            _disposed = true;
        }

        /// <summary>The two buffers that can hold a modified or new row, in walk order.</summary>
        /// <remarks>
        /// Primary then Filter. Both are walked because a filtered row is still a modified row the update
        /// must apply - the legacy's identity round trip walks both for exactly that reason - and Primary
        /// is first so the ordinary case is applied before the filtered one.
        /// </remarks>
        private static DwBuffer[] ModifiableBuffers => [DwBuffer.Primary, DwBuffer.Filter];

        /// <summary>
        /// Every one-based row number of a buffer, in buffer order.
        /// </summary>
        /// <param name="buffer">The buffer to enumerate.</param>
        /// <returns>The row numbers, materialised so the walk is stable while statements run.</returns>
        /// <remarks>
        /// MATERIALISED RATHER THAN LAZY, because the walk executes statements and a lazy enumeration over
        /// a live buffer would re-read counts that a statement may have changed. The counts themselves are
        /// the buffer-specific ones the legacy uses - <c>RowCount()</c>, <c>FilteredCount()</c> and
        /// <c>DeletedCount()</c> - because a buffer's length is not a property of the carrier as a whole.
        /// </remarks>
        private long[] RowsOf(DwBuffer buffer)
        {
            long count = buffer switch
            {
                DwBuffer.Delete => _store.Carrier.DeletedCount(),
                DwBuffer.Filter => _store.Carrier.FilteredCount(),
                _ => _store.Carrier.RowCount(),
            };

            if (count <= 0L)
            {
                return [];
            }

            long[] rows = new long[count];

            for (long row = ItemStatusMachine.FirstRowNumber; row <= count; row++)
            {
                // R9: the ARRAY index is zero-based while the ROW NUMBER it holds is one-based, and the
                // subtraction converts between the two exactly once.
                rows[row - ItemStatusMachine.FirstRowNumber] = row;
            }

            return rows;
        }

        /// <summary>
        /// Coerces a carrier value to a number, or answers <see langword="null"/>.
        /// </summary>
        /// <param name="value">The value read from the carrier.</param>
        /// <returns>The number, or <see langword="null"/> when the value is null or not numeric.</returns>
        /// <remarks>
        /// NULL IS PRESERVED AS NULL AND NEVER COLLAPSED TO ZERO. The identity round trip distinguishes a
        /// row with no identity value from a row whose identity value is zero, and collapsing the two
        /// would report an identity of zero for a row that had none.
        /// </remarks>
        private static long? ToNumber(object? value) => value switch
        {
            null => null,
            long number => number,
            int number => number,
            short number => number,
            byte number => number,
            decimal number => (long)number,
            double number => (long)number,
            float number => (long)number,
            string text when long.TryParse(text, CultureInfo.InvariantCulture, out long parsed) => parsed,
            _ => null,
        };

        /// <summary>
        /// Removes a property suffix, answering the column name in front of it.
        /// </summary>
        /// <param name="property">The property name.</param>
        /// <param name="suffix">The suffix to remove.</param>
        /// <returns>The column name, or the empty string when the suffix was absent.</returns>
        private static string TrimSuffix(string property, string suffix) =>
            property.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                ? property[..^suffix.Length].Trim()
                : string.Empty;

        /// <summary>
        /// Reads an installed property.
        /// </summary>
        /// <param name="property">The property name.</param>
        /// <returns>The installed value, or the empty string when none was installed.</returns>
        private string Describe(string property)
        {
            EnsureDefinitionContract();

            return _properties.TryGetValue(property, out string? value) ? value : UnsetDescribeResult;
        }

        /// <summary>
        /// Installs the table-level update settings the assigned definition declares, once.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>THIS IS THE MANAGED EQUIVALENT OF LOADING THE DATAWINDOW, AND WITHOUT IT THE SINGLE-TABLE
        /// UPDATE PATH CANNOT WORK.</b> The legacy's single-table path performs NO PREPARE at all - it goes
        /// straight to <c>_of_Update(data)</c> [<c>n_cst_thread_task_sqlupdate.sru:L370-L371</c>] - and it
        /// works because <c>ds.DataObject = name</c> loaded a compiled DataWindow whose table
        /// specification already carries <c>update="COMPANY" updatewhere=1 updatekeyinplace=no</c>
        /// [<c>dw_sqlite.srd:L14</c>]. The update path then reads those back through <c>Describe</c>, and
        /// its own guard is <c>Describe("DataWindow.Table.UpdateTable") &lt;&gt; "?"</c>. A carrier that
        /// answered nothing to that describe would refuse every single-table update with the
        /// no-updatable-table database error - which is the correct answer to a question the port had
        /// simply failed to let the definition answer.
        /// </para>
        /// <para>
        /// SEEDED, NOT PINNED. The values land in the same property map <see cref="Modify"/> writes, so a
        /// later modification script overrides any of them - which is exactly the layering the oracle has,
        /// where <c>_of_updateprepare</c> resets and re-enables the flags from a caller's descriptor
        /// [<c>:L104-L129</c>].
        /// </para>
        /// <para>
        /// ONCE, AND LAZILY. Once because a second seed would undo a caller's modification; lazily because
        /// the data object is assigned to the store AFTER this carrier is adapted around it, so nothing is
        /// resolvable at construction. The flag is set before the resolution rather than after, so a name
        /// that resolves to nothing is not retried on every describe.
        /// </para>
        /// <para>
        /// A RETRIEVE-ONLY DEFINITION SEEDS NOTHING, and that is a real answer rather than an omission: a
        /// definition derived from a bare statement names no update table, so the describe stays empty and
        /// the update path refuses exactly as the oracle refuses a datastore whose data object declares no
        /// update table.
        /// </para>
        /// </remarks>
        private void EnsureDefinitionContract()
        {
            string dataObject = _store.DataObject;

            if (string.Equals(_installedContractDataObject, dataObject, StringComparison.Ordinal))
            {
                return;
            }

            // CLEARED BEFORE THE RE-SEED, because the properties installed for the PREVIOUS definition
            // are not defaults for this one - an update table in particular would otherwise survive onto
            // a definition that declares none.
            _properties.Clear();

            // Recorded BEFORE the resolution, so a name that resolves to nothing is attempted once rather
            // than on every describe.
            _installedContractDataObject = dataObject;

            if (dataObject.Length == 0
                || !_catalogue.TryResolveUpdateSettings(dataObject, out DataObjectUpdateSettings? settings)
                || settings.Table.Length == 0)
            {
                return;
            }

            _properties[UpdateWhereBuilder.UpdateTableProperty] = settings.Table;
            _properties[UpdateWhereBuilder.UpdateWhereProperty] =
                settings.UpdateWhereMode.ToString(CultureInfo.InvariantCulture);
            _properties[UpdateWhereBuilder.UpdateKeyInPlaceProperty] = settings.UpdateKeyInPlace
                ? UpdateWhereBuilder.YesLiteral
                : UpdateWhereBuilder.NoLiteral;

            string[] model = ColumnModelCore();

            for (int index = 0; index < model.Length; index++)
            {
                string column = model[index];

                // R9: the ARRAY index is zero-based and the DESCRIBE ORDINAL is one-based.
                string ordinal = UpdateWhereBuilder.ColumnOrdinalPrefix
                    + (index + 1).ToString(CultureInfo.InvariantCulture);

                string key = Contains(settings.KeyColumns, column)
                    ? UpdateWhereBuilder.YesLiteral
                    : UpdateWhereBuilder.NoLiteral;

                string identity =
                    string.Equals(column, settings.IdentityColumn, StringComparison.OrdinalIgnoreCase)
                        ? UpdateWhereBuilder.YesLiteral
                        : UpdateWhereBuilder.NoLiteral;

                // EVERY COLUMN IS UPDATABLE AND EVERY COLUMN IS IN THE WHERE-CLAUSE, which is what the
                // evidenced fixture declares for all six of its columns [dw_sqlite.srd:L8-L14] and is the
                // ordinary shape of a DataWindow built over one table. `updatewhere=1` is what makes the
                // where-clause membership load bearing: the generated predicate carries the ORIGINAL value
                // of every marked column, which is the whole optimistic-concurrency check.
                InstallBoth(UpdateSuffix, UpdateWhereBuilder.YesLiteral);
                InstallBoth(UpdateWhereClauseSuffix, UpdateWhereBuilder.YesLiteral);
                InstallBoth(KeySuffix, key);
                InstallBoth(IdentitySuffix, identity);

                // THE DATABASE NAME EQUALS THE DATAWINDOW NAME for every column of the evidenced fixture,
                // which is the ordinary case for a DataWindow built over one table. It is installed rather
                // than left to a fallback because the identity resolver PREFIX-MATCHES the lower-cased
                // update table name against it [n_cst_thread_task_sqlupdate.sru:L221], and a fallback that
                // handed back the describe property's own stem would answer "#3" for an ordinal-addressed
                // column - a string no table name can prefix, so the match would silently never fire.
                InstallBoth(DbNameSuffix, column);

                // BOTH ADDRESSING FORMS FOR EVERY PROPERTY, because PowerBuilder's Describe accepts both
                // and the two are used by different callers in this service: the modification script the
                // update preparer composes addresses columns BY NAME
                // [UpdateWhereBuilder, `columnName + ".Update"`], while the identity round trip addresses
                // them BY ORDINAL [IdentityColumnResolver, `"#" + String(nIndex) + ".Identity"`]. Seeding
                // only one form leaves the other answering nothing, and the identity round trip's failure
                // mode is silent: it finds no identity column, falls through to its first-wins arm, and
                // reports identity values for the wrong column.
                void InstallBoth(string suffix, string value)
                {
                    _properties[column + suffix] = value;
                    _properties[ordinal + suffix] = value;
                }
            }

            static bool Contains(IReadOnlyList<string> names, string candidate)
            {
                foreach (string name in names)
                {
                    if (string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Reads the installed flags back into the column plan the generator emits from.
        /// </summary>
        /// <returns>The plan.</returns>
        /// <remarks>
        /// THE FLAGS ARE THE PREPARE STEP'S, READ BACK RATHER THAN RE-DERIVED. `_of_updateprepare` resets
        /// update, key and identity to off on every column and then selectively re-enables from the table
        /// descriptor array [<c>n_cst_thread_task_sqlupdate.sru:L104-L129</c>], which is precisely what
        /// arrives here through <see cref="Modify"/>. Re-deriving them from the definition would discard
        /// the caller's descriptor and generate a statement for columns it never asked to update.
        /// </remarks>
        /// <summary>
        /// Projects the current state of every row whose predicate matched nothing.
        /// </summary>
        /// <param name="plan">The installed column plan, which names the columns to project.</param>
        /// <param name="unmatched">The buffer and row of each statement that affected zero rows.</param>
        /// <returns>
        /// One conflict row per unmatched row, or an empty list when every statement matched.
        /// </returns>
        /// <remarks>
        /// <para>
        /// THIS IS WHAT MAKES A MISMATCH REPORTABLE RATHER THAN MERELY COUNTABLE. The classifier treats a
        /// shortfall as a concurrency mismatch only when it can also name the rows
        /// [<see cref="ConflictDetector.IsConcurrencyMismatch"/>], and the contract requires the conflict
        /// response to carry current row state so a caller can choose between retrying and surfacing
        /// rather than guessing. Counting alone would produce a 409 with nothing in it.
        /// </para>
        /// <para>
        /// THE PROJECTION READS THE CARRIER, NOT THE DATABASE, and that is the right source: the state a
        /// caller needs is the state IT holds and the server rejected, so it can see which of its own
        /// values the predicate was built from. Re-reading the row from storage would answer a different
        /// question - what the other writer put there - which the caller can ask for with a fresh
        /// retrieval if it wants it, and which cannot be read here anyway without a second round trip
        /// inside a failed update.
        /// </para>
        /// <para>
        /// ORDINALS ARE ONE-BASED THROUGHOUT (hazard R9). The column numbers come from the plan, which
        /// derives them from the carrier's own one-based column ordinals, and the row ordinals are the
        /// carrier's own. Nothing is rebased.
        /// </para>
        /// </remarks>
        private IReadOnlyList<ConflictRow> ProjectUnmatchedRows(
            UpdateColumnPlan plan,
            List<(DwBuffer Buffer, long Row)> unmatched)
        {
            if (unmatched.Count == 0)
            {
                return [];
            }

            ConflictColumn[] columns = [.. plan.UpdatableColumns
                .Where(static column => column.Number >= ItemStatusMachine.FirstColumnNumber
                    && !string.IsNullOrWhiteSpace(column.Name))
                .Select(static column => new ConflictColumn(column.Name, column.Number))];

            if (columns.Length == 0)
            {
                // Nothing addressable to project. The counts still travel, so the caller learns a
                // shortfall occurred even though no per-column state could be described.
                return [];
            }

            List<ConflictRow> rows = new(unmatched.Count);

            foreach ((DwBuffer buffer, long row) in unmatched)
            {
                rows.Add(ConflictDetector.ProjectConflictRow(
                    _store.Carrier,
                    buffer,
                    row,
                    columns));
            }

            return rows;
        }

        /// <summary>
        /// The carrier's column model, in one-based column order.
        /// </summary>
        /// <returns>The column names, or an empty array when no definition has resolved.</returns>
        /// <remarks>
        /// <para>
        /// <b>TWO SOURCES, AND THE SECOND IS WHAT THE PREPARE STEP DEPENDS ON.</b> The store bindings hold
        /// the columns a carrier was BUILT FROM A SYNTAX with, which is the derived-definition path and the
        /// only one that can name columns no catalogue carries. A carrier whose data object was ASSIGNED
        /// BY NAME has no binding at all until something builds one, and the whole update contract is
        /// installed BEFORE any retrieval: <c>_of_updateprepare</c> resets and re-enables per-column flags
        /// and resolves each key column's identifier through <c>Describe</c>
        /// [<c>n_cst_thread_task_sqlupdate.sru:L104-L129</c>], all of it against a datastore that has only
        /// been given a name. In the legacy that works because <c>ds.DataObject = X</c> LOADS the compiled
        /// DataWindow, so its column model exists from the assignment onwards
        /// [<c>n_cst_thread_task_sqlbase.sru:L558</c>]. Resolving the assigned name against the catalogue
        /// here is the managed equivalent of that load.
        /// </para>
        /// <para>
        /// WITHOUT THE SECOND SOURCE THE FAILURE IS SILENT AND TOTAL: the column count answers zero, every
        /// <c>&lt;column&gt;.Id</c> resolves to zero, the preparer reaches its own non-positive-identifier
        /// arm, and an update that had a perfectly good contract refuses with an invalid-column-name
        /// diagnostic naming a column that does exist.
        /// </para>
        /// </remarks>
        private string[] ColumnModel()
        {
            EnsureDefinitionContract();

            return ColumnModelCore();
        }

        /// <summary>
        /// The column model WITHOUT triggering the definition-contract seed.
        /// </summary>
        /// <returns>The column names, or an empty array when no definition has resolved.</returns>
        /// <remarks>
        /// SPLIT OUT SO THE SEED CAN USE IT. The seed installs a per-column flag for every column, so it
        /// needs the model - and calling the seeding entry point from inside the seed would recurse.
        /// </remarks>
        private string[] ColumnModelCore()
        {
            string[] bound = _bindings.GetColumns(_store);

            if (bound.Length > 0)
            {
                return bound;
            }

            string dataObject = _store.DataObject;

            if (dataObject.Length > 0
                && _catalogue.TryResolve(dataObject, out DataObjectDefinitionEntry? entry))
            {
                string[] declared = new string[entry.Columns.Count];

                for (int index = 0; index < declared.Length; index++)
                {
                    declared[index] = entry.Columns[index].Name;
                }

                return declared;
            }

            return [];
        }

        private UpdateColumnPlan BuildPlan()
        {
            string[] columns = ColumnModel();

            List<UpdateColumn> updatable = [];
            List<UpdateColumn> keys = [];
            List<UpdateColumn> whereColumns = [];

            for (int index = 0; index < columns.Length; index++)
            {
                // ONE-BASED column numbers on the carrier against the zero-based array index here.
                UpdateColumn column = new(columns[index], index + 1);

                if (IsFlagSet(columns[index] + UpdateSuffix))
                {
                    updatable.Add(column);
                }

                if (IsFlagSet(columns[index] + KeySuffix))
                {
                    keys.Add(column);
                }

                if (IsFlagSet(columns[index] + UpdateWhereClauseSuffix))
                {
                    whereColumns.Add(column);
                }
            }

            return new UpdateColumnPlan(updatable, keys, whereColumns);
        }

        /// <summary>
        /// Whether a per-column boolean flag is installed as set.
        /// </summary>
        /// <param name="property">The flag property name.</param>
        /// <returns><see langword="true"/> when the flag reads as set.</returns>
        /// <remarks>
        /// BOTH SPELLINGS ARE ACCEPTED, AND THAT IS THE LEGACY'S OWN GRAMMAR RATHER THAN LENIENCE. The
        /// composer writes a boolean flag as <c>yes</c> for the per-column update and key flags and as a
        /// quoted NUMBER for the update-where mode [<c>:L132</c>], so a reader that accepted only one
        /// spelling would silently drop half the installed flags.
        /// </remarks>
        private bool IsFlagSet(string property)
        {
            string value = Describe(property);

            return value.Length > 0
                && (string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                    || (int.TryParse(value, CultureInfo.InvariantCulture, out int flag) && flag != 0));
        }

        /// <summary>
        /// Generates and runs the delete statement for one row of the delete buffer.
        /// </summary>
        /// <param name="commands">The command source.</param>
        /// <param name="table">The update table.</param>
        /// <param name="plan">The installed column plan.</param>
        /// <param name="row">The one-based row number within the delete buffer.</param>
        /// <returns>The rows the statement affected.</returns>
        private long ApplyDelete(
            ISqliteCommandSource commands,
            string table,
            UpdateColumnPlan plan,
            long row)
        {
            StringBuilder statement = new("DELETE FROM ");
            _ = statement.Append(table);

            List<object?> values = [];
            AppendWhere(statement, plan, DwBuffer.Delete, row, values);

            return Execute(commands, statement.ToString(), values, SqlPreviewType.Delete, DwBuffer.Delete);
        }

        /// <summary>
        /// Generates and runs the insert statement for one new row.
        /// </summary>
        /// <param name="commands">The command source.</param>
        /// <param name="table">The update table.</param>
        /// <param name="plan">The installed column plan.</param>
        /// <param name="buffer">The buffer the row lives in.</param>
        /// <param name="row">The one-based row number within that buffer.</param>
        /// <returns>The rows the statement affected.</returns>
        /// <remarks>
        /// THE IDENTITY COLUMN IS OMITTED FROM THE COLUMN LIST, which is what lets the store assign it and
        /// is the whole reason the identity round trip exists: the caller reads the assigned values back
        /// out afterwards. Including it would insert the carrier's placeholder and make the round trip
        /// report a value the store never generated.
        /// </remarks>
        private long ApplyInsert(
            ISqliteCommandSource commands,
            string table,
            UpdateColumnPlan plan,
            DwBuffer buffer,
            long row)
        {
            StringBuilder statement = new("INSERT INTO ");
            _ = statement.Append(table).Append(" ( ");

            List<object?> values = [];
            bool first = true;

            foreach (UpdateColumn column in plan.UpdatableColumns)
            {
                if (IsFlagSet(column.Name + IdentitySuffix))
                {
                    continue;
                }

                if (!first)
                {
                    _ = statement.Append(", ");
                }

                _ = statement.Append(column.Name);
                values.Add(_store.Carrier.GetItemValue(row, column.Number, buffer));
                first = false;
            }

            _ = statement.Append(" ) VALUES ( ");

            for (int index = 0; index < values.Count; index++)
            {
                if (index > 0)
                {
                    _ = statement.Append(", ");
                }

                _ = statement.Append('@').Append('p').Append((index + 1).ToString(CultureInfo.InvariantCulture));
            }

            _ = statement.Append(" )");

            return Execute(commands, statement.ToString(), values, SqlPreviewType.Insert, buffer);
        }

        /// <summary>
        /// Generates and runs the update statement for one modified row.
        /// </summary>
        /// <param name="commands">The command source.</param>
        /// <param name="table">The update table.</param>
        /// <param name="plan">The installed column plan.</param>
        /// <param name="buffer">The buffer the row lives in.</param>
        /// <param name="row">The one-based row number within that buffer.</param>
        /// <returns>The rows the statement affected.</returns>
        private long ApplyUpdate(
            ISqliteCommandSource commands,
            string table,
            UpdateColumnPlan plan,
            DwBuffer buffer,
            long row)
        {
            StringBuilder statement = new("UPDATE ");
            _ = statement.Append(table).Append(" SET ");

            List<object?> values = [];
            bool first = true;

            foreach (UpdateColumn column in plan.UpdatableColumns)
            {
                // ONLY THE COLUMNS THIS ROW ACTUALLY MODIFIED are assigned, which is the legacy's own
                // shape: PowerBuilder generates a SET list from the per-column item statuses rather than
                // from the updatable set, so an untouched column is not written and cannot clobber a
                // concurrent writer's value for it.
                if (_store.Carrier.GetItemStatus(row, column.Number, buffer) == ItemStatus.NotModified)
                {
                    continue;
                }

                if (!first)
                {
                    _ = statement.Append(", ");
                }

                values.Add(_store.Carrier.GetItemValue(row, column.Number, buffer));

                _ = statement
                    .Append(column.Name)
                    .Append(" = @p")
                    .Append(values.Count.ToString(CultureInfo.InvariantCulture));

                first = false;
            }

            if (values.Count == 0)
            {
                // A row flagged modified whose every column reads unmodified generates no statement, and
                // reporting zero affected rows is the honest answer: nothing was asked of the store.
                return 0L;
            }

            AppendWhere(statement, plan, buffer, row, values);

            return Execute(commands, statement.ToString(), values, SqlPreviewType.Update, buffer);
        }

        /// <summary>
        /// Appends the optimistic-concurrency predicate for one row.
        /// </summary>
        /// <param name="statement">The statement being built.</param>
        /// <param name="plan">The installed column plan.</param>
        /// <param name="buffer">The buffer the row lives in.</param>
        /// <param name="row">The one-based row number within that buffer.</param>
        /// <param name="values">The parameter values collected so far, appended to.</param>
        /// <remarks>
        /// <para>
        /// 🔴 <b>THE ORIGINAL VALUE, NOT THE CURRENT ONE, AND THIS IS THE WHOLE CONCURRENCY CONTRACT.</b>
        /// <c>updatewhere=1</c> is the key-and-updateable-columns mode: the predicate carries the key
        /// column PLUS THE ORIGINAL VALUE OF EVERY MARKED COLUMN. A predicate built from current values
        /// would compare a row against itself, always match, and silently overwrite a concurrent writer -
        /// which is the single failure the contract exists to prevent, and there is no silent overwrite
        /// anywhere in this system.
        /// </para>
        /// <para>
        /// A NULL ORIGINAL BECOMES <c>IS NULL</c> RATHER THAN AN EQUALITY AGAINST NULL, because an
        /// equality against null is never true in SQL and would make every row whose marked column was
        /// null unmatchable - reported as a conflict on a row nobody had touched.
        /// </para>
        /// <para>
        /// The key columns come first and the marked columns follow, which is the order PowerBuilder
        /// generates and therefore the order a characterization recording compares.
        /// </para>
        /// </remarks>
        private void AppendWhere(
            StringBuilder statement,
            UpdateColumnPlan plan,
            DwBuffer buffer,
            long row,
            List<object?> values)
        {
            _ = statement.Append(" WHERE ");

            bool first = true;

            foreach (UpdateColumn column in plan.KeyColumns.Concat(
                plan.WhereClauseColumns.Where(candidate => !plan.KeyColumns.Contains(candidate))))
            {
                if (!first)
                {
                    _ = statement.Append(" AND ");
                }

                object? original = _store.Carrier.GetItemOriginalValue(row, column.Number, buffer);

                if (original is null)
                {
                    _ = statement.Append(column.Name).Append(" IS NULL");
                }
                else
                {
                    values.Add(original);

                    _ = statement
                        .Append(column.Name)
                        .Append(" = @p")
                        .Append(values.Count.ToString(CultureInfo.InvariantCulture));
                }

                first = false;
            }

            if (first)
            {
                // NO PREDICATE MEANS NO STATEMENT MAY RUN. A where-clause-less update or delete would
                // affect every row in the table, so an always-false predicate is appended instead: the
                // statement runs, affects nothing, and the shortfall reaches the classifier as a mismatch
                // rather than as a silent mass overwrite.
                _ = statement.Append("1 = 0");
            }
        }

        /// <summary>
        /// Publishes a statement on the preview channel and runs whatever the channel returns.
        /// </summary>
        /// <param name="commands">The command source.</param>
        /// <param name="statement">The generated statement.</param>
        /// <param name="values">The parameter values, in placeholder order.</param>
        /// <param name="previewType">The kind of statement, for the preview channel.</param>
        /// <param name="buffer">The buffer the statement was generated for.</param>
        /// <returns>The rows the statement affected.</returns>
        /// <remarks>
        /// THE PREVIEW CHANNEL IS CONSULTED BEFORE EXECUTION AND ITS VETO IS HONOURED, because that is
        /// what the legacy's <c>SQLPreview</c> interception does: a carrier may stop processing, and the
        /// worker-side carrier's own override uses exactly that to count progress and to apply the N-char
        /// rewrite. Skipping the channel would remove a documented extension point.
        /// </remarks>
        private long Execute(
            ISqliteCommandSource commands,
            string statement,
            List<object?> values,
            SqlPreviewType previewType,
            DwBuffer buffer)
        {
            _ = _store.Carrier.SetSqlPreview(statement);

            if (_store.Carrier.OnSqlPreview(previewType, statement, buffer)
                != DataWindowBufferStore.EventContinue)
            {
                return 0L;
            }

            using SqliteCommand command = commands.CreateCommand();

            // The carrier may have REPLACED the statement through the preview channel, so the text that
            // runs is read back from the carrier rather than reused from the local. That is the point of
            // the interception, and reusing the local would make the replacement unobservable.
            command.CommandText = _store.Carrier.SqlPreviewStatement is { Length: > 0 } replaced
                ? replaced
                : statement;

            for (int index = 0; index < values.Count; index++)
            {
                _ = command.Parameters.AddWithValue(
                    "@p" + (index + 1).ToString(CultureInfo.InvariantCulture),
                    values[index] ?? DBNull.Value);
            }

            return command.ExecuteNonQuery();
        }

        /// <summary>One column of the installed update plan: its name and its one-based number.</summary>
        /// <param name="Name">The column name, as the definition declares it.</param>
        /// <param name="Number">The ONE-BASED column number the carrier indexes by.</param>
        private readonly record struct UpdateColumn(string Name, int Number);

        /// <summary>The installed column flags, read back from the prepare step's modification script.</summary>
        /// <param name="UpdatableColumns">The columns marked updatable, in column order.</param>
        /// <param name="KeyColumns">The columns marked as keys, in column order.</param>
        /// <param name="WhereClauseColumns">The columns marked for the concurrency predicate.</param>
        private sealed record UpdateColumnPlan(
            IReadOnlyList<UpdateColumn> UpdatableColumns,
            IReadOnlyList<UpdateColumn> KeyColumns,
            IReadOnlyList<UpdateColumn> WhereClauseColumns);
    }
}
