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
//  AND THE POSITION PARAMETERS CANNOT COVER: THE IDENTIFIER GATE
//  No dialect has a parameter form for a table or a column, so the identifier positions of the four
//  statement shapes below are the only places caller-supplied text reaches the engine as SQL. Across
//  this boundary both sources of those identifiers - the installed update table and the carrier's
//  column model - are caller-controlled, which the legacy's compiled DataWindow never was. `Update`
//  therefore admits them through `Sql/SqlIdentifierGuard.cs` BEFORE generating anything, refusing on
//  the datastore channel and emitting no statement at all when a name cannot occupy an identifier
//  position. Nothing is quoted, escaped or normalised: an admitted name is concatenated exactly as it
//  arrived, so byte-exact statement parity is untouched. The C-06 boundary applies the same gate to a
//  descriptor one layer out, where it can answer E_INVALID_ARGUMENT.
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
using PowerFramework.Persistence.Errors;
using PowerFramework.Persistence.Sql;
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

        /// <summary>
        /// The prefix every table-level describe and modify property carries.
        /// </summary>
        /// <remarks>
        /// THE DISCRIMINATOR BETWEEN A TABLE-LEVEL AND A COLUMN-SCOPED PROPERTY. PowerBuilder's
        /// vocabulary puts everything that is not addressed to a named object under
        /// <c>DataWindow.</c> - <c>DataWindow.Table.UpdateTable</c>, <c>DataWindow.Table.UpdateWhere</c>,
        /// <c>DataWindow.Table.UpdateKeyinPlace</c> and <c>DataWindow.Column.Count</c> are the four this
        /// service composes - so a property that does NOT carry it is addressed to a column and its stem
        /// must resolve. Held as a constant because two members test against it and a literal in either
        /// would let them drift apart.
        /// </remarks>
        internal const string DataWindowPropertyPrefix = "DataWindow.";

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

        /// <summary>
        /// The statement that answers the identity the store assigned to the most recent insert on the
        /// current connection.
        /// </summary>
        /// <remarks>
        /// CONNECTION-SCOPED BY DEFINITION, which is why it is read through the same command source the
        /// insert ran on and immediately after it. It carries no parameter, no identifier and no value, so
        /// it is a fixed statement rather than a composed one and there is nothing in it to interpolate.
        /// </remarks>
        internal const string LastInsertRowIdStatement = "SELECT last_insert_rowid()";

        /// <summary>
        /// The alias each branch of the batched conflict reread selects its submitted-row ordinal under.
        /// </summary>
        /// <remarks>
        /// NAMED IN THE FRAMEWORK'S OWN SENTINEL STYLE, exactly as the paging rewriters name theirs
        /// (<c>pfwPagedSQL_RN</c> and its siblings) and for the same reason: the alias sits in the same
        /// result set as the caller's own columns, so a name that could collide with a real column would
        /// make the ordinal unreadable on precisely the payload a caller has to act on. It is never
        /// returned to a caller - the projection reads it and drops it.
        /// </remarks>
        internal const string ConflictRowOrdinalAlias = "pfwConflictRow";

        /// <summary>
        /// The most parameters one batched conflict-reread command may carry.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A CEILING WELL INSIDE THE PROVIDER'S OWN. SQLite's compiled default for
        /// <c>SQLITE_MAX_VARIABLE_NUMBER</c> has been 32766 since 3.32 and was 999 before it; the reread
        /// runs on whatever build the host resolves, so the batch is sized under the OLDER limit rather
        /// than the newer one. Nothing about the answer depends on the value - a larger ceiling means
        /// fewer commands and an identical payload - so the conservative figure costs only round trips on
        /// a conflict large enough to reach it.
        /// </para>
        /// <para>
        /// PARAMETERS RATHER THAN ROWS, because the parameter count is what the provider limits: a row
        /// contributes one parameter per non-null key column, so the row count a batch admits is derived
        /// from the key width at the call site.
        /// </para>
        /// </remarks>
        internal const int MaximumBatchedParameters = 900;

        /// <summary>
        /// The most rows one batched conflict-reread command may cover.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 🔴 A SECOND CEILING, AND IT IS THE BINDING ONE. Each row contributes one term to a compound
        /// <c>SELECT</c>, and SQLite refuses a compound statement past
        /// <c>SQLITE_MAX_COMPOUND_SELECT</c> - whose compiled default is 500 - with
        /// <c>too many terms in compound SELECT</c>. THAT WAS OBSERVED RATHER THAN REASONED ABOUT: a
        /// batch sized only by the parameter ceiling reached it on the first conflict wider than 500
        /// rows, and the reread then threw where a row-at-a-time reread had succeeded. Both ceilings
        /// therefore bound the batch and the smaller wins.
        /// </para>
        /// <para>
        /// A HUNDRED, WHICH IS A FIFTH OF THE LIMIT IT RESPECTS. The limit is compile-time
        /// configurable, so a host could ship a lower one; sitting well inside the default leaves room
        /// for that without making the value a claim about anything. Nothing about the projected payload
        /// depends on it - a larger batch means fewer commands and an identical answer - so the
        /// conservative figure costs only round trips, and a hundredfold reduction is already the whole
        /// of what the batching is for.
        /// </para>
        /// </remarks>
        internal const int MaximumBatchedRows = 100;

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
        /// <para>
        /// Delegated to the sibling payload codec rather than decoded here, because the codec already
        /// owns the two documented changeset defects and a second decoder would be a second place for
        /// them to be got wrong. A <see langword="null"/> payload is the oracle's zero-length
        /// <c>Blob("")</c> and answers the failure value, which the caller tells apart from a rejected
        /// payload by the update-row count.
        /// </para>
        /// <para>
        /// 🔴 <b>AND IT IS THE ONE APPLY IN THE SERVICE THAT DEMANDS AN EXPLICIT BASELINE.</b> This
        /// payload was composed by a CALLER and every statement generated from it carries a predicate
        /// read out of its ORIGINAL values, so <see cref="CarrierBaselineTrust.RequiredOnChangedRows"/>
        /// is passed: a row that is not insert-shaped must state the original of every column it carries,
        /// or the payload is refused here rather than applied against a baseline inferred from the values
        /// being written. The retrieve path passes <see cref="CarrierBaselineTrust.AsStated"/> instead,
        /// because there the producer is this service. See <see cref="CarrierBaselineTrust"/> for the
        /// full reasoning, and note that this asks a caller for nothing the update contract did not
        /// already require it to send (AAP 0.6.3.2).
        /// </para>
        /// <para>
        /// A REFUSAL IS THE ORACLE'S OWN INVALID-UPDATE-DATA OUTCOME rather than a new failure mode: the
        /// failure value travels back through <c>of_setupdatedata</c> exactly as a malformed changeset
        /// already did, and the update task answers <c>E_INVALID_DATA</c> with the legacy diagnostic
        /// [<c>n_cst_thread_task_sqlupdate.sru</c>, 无效的更新数据!]. DataServices additionally refuses
        /// the same shape at the ingress with a legible per-column error
        /// [<c>Validators/UpdateRowValidator</c>], so a caller normally learns which column it left
        /// unstated before this line is ever reached.
        /// </para>
        /// </remarks>
        public long SetChanges(CarrierState? changes)
        {
            if (changes is null)
            {
                return DataWindowBufferStore.DataStoreFailure;
            }

            return _payloads.TryApply(
                _store.Carrier,
                changes,
                CarrierBaselineTrust.RequiredOnChangedRows);
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

            // ==========================================================================================
            //  🔴 THE IDENTIFIER ADMISSION GATE - THE ONE POSITION IN A GENERATED STATEMENT THAT CANNOT
            //  BE PARAMETERIZED.
            //
            //  EVERY VALUE BELOW THIS LINE TRAVELS AS AN `@pN` PARAMETER; NO IDENTIFIER CAN. There is no
            //  parameter form for a table or a column in any dialect - `UPDATE @p1 SET ...` is not a
            //  statement - so the identifier positions of the four statement shapes this class composes
            //  (ApplyInsert, ApplyUpdate, ApplyDelete and the conflict re-read) are the only positions
            //  where caller-supplied text reaches the engine as SQL rather than as data. This gate is
            //  what admits it, and it sits HERE because every one of those shapes is reached only from
            //  this member and only after these two facts have resolved: the update table it names, and
            //  the column model its plan was read from.
            //
            //  BOTH SOURCES ARE CALLER-CONTROLLED ACROSS THIS BOUNDARY, WHICH IS WHAT THE LEGACY'S WERE
            //  NOT. The table arrives through `DataWindow.Table.UpdateTable`, written by the prepare
            //  step's modification script [n_cst_thread_task_sqlupdate.sru:L143], and the column model
            //  comes either from the bindings a supplied `sql_syntax` produced or from a catalogue entry
            //  that supplied syntax registered. In the legacy both came from a COMPILED DataWindow
            //  shipped inside the application, so no remote party could reach either.
            //
            //  A SHAPE TEST, AND DELIBERATELY NOT A CATALOGUE LOOKUP. Checking a name against the
            //  carrier's own model cannot close this: on the derived-definition path the model IS the
            //  caller's declaration, so the input would be validated against itself. See
            //  Sql/SqlIdentifierGuard.cs for the full account, including why nothing is quoted - the
            //  parity criterion for this service is byte-exact generated SQL, so an admitted name is
            //  concatenated exactly as it arrived and a refused one produces no statement at all.
            //
            //  THE WHOLE MODEL IS TESTED, NOT ONLY THE FLAGGED SUBSETS. `plan.AllColumns` is the model
            //  every subset is drawn from - updatable, key, where-clause and the conflict projection's
            //  union - so testing it once covers every position any of the four shapes can emit, and
            //  cannot be left behind by a later change that emits from a different subset.
            //
            //  ANSWERED IN THE DATASTORE ALPHABET because that is this member's return contract - 1 for
            //  success, -1 for failure [n_cst_thread_task_sqlupdate.sru:L204] - and the caller turns it
            //  into a database error. The precise E_INVALID_ARGUMENT refusal a caller can act on is
            //  produced one layer out, at the C-06 boundary, where the descriptor arrives; this arm is
            //  the sink that also covers the syntax-derived path that boundary does not parse.
            //
            //  NO IDENTIFIER IS LOGGED (constraint C-F). The caller sent the name and gets a refusal for
            //  it; a log record is read by someone who did not, and the name is caller-supplied text.
            // ==========================================================================================
            if (!SqlIdentifierGuard.IsAdmissibleQualifiedName(table))
            {
                _logger.LogError(
                    "An update was refused before any statement was generated: the installed update "
                        + "table name cannot occupy an identifier position. No name, column or value is "
                        + "recorded.");

                return DataWindowBufferStore.DataStoreFailure;
            }

            foreach (UpdateColumn column in plan.AllColumns)
            {
                if (SqlIdentifierGuard.IsAdmissibleIdentifier(column.Name))
                {
                    continue;
                }

                _logger.LogError(
                    "An update was refused before any statement was generated: a column of the "
                        + "carrier's model cannot occupy an identifier position. No name, column or "
                        + "value is recorded.");

                return DataWindowBufferStore.DataStoreFailure;
            }

            // THE CARRIER LEARNS ITS COLUMN NAMES HERE TOO, once per update rather than per row. The
            // retrieve path records them as it fills; an update carrier is filled from a payload the caller
            // supplied, so this is the first point at which the names are resolved - and it is resolved
            // anyway, for the plan. Recording it lets anything projected back out of this carrier - most
            // importantly a conflict detail's rows - carry the NAME identifier alongside the ordinal, which
            // is what common.v1.ColumnValue requires wherever both are known.
            _store.Carrier.SetColumnNames(plan.ColumnNames);

            if (plan.UpdatableColumns.Count == 0)
            {
                _logger.LogError(
                    "An update was attempted on a carrier with no updatable column installed, so no "
                        + "statement could be generated.");

                return DataWindowBufferStore.DataStoreFailure;
            }

            // THE KEY-IN-PLACE SETTING, READ ONCE FROM THE RUNTIME VALUE THE PREPARE STEP INSTALLED and
            // not from a descriptor. `_of_updateprepare` writes it through the modification script
            // [n_cst_thread_task_sqlupdate.sru:L135-L141] and the key-change workaround at [:L155] gates
            // on the DESCRIBED value for the same reason: a descriptor that leaves the setting absent
            // leaves the carrier's own definition in force, which on the sole evidenced fixture is `no`
            // [ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14].
            //
            // ORDINAL AND CASE-SENSITIVE, because PowerScript's `=` on strings is. Anything other than
            // exactly "no" - including "yes", "!", "?" and empty - leaves a key change an ordinary UPDATE.
            bool keyChangeIsDeleteAndInsert = string.Equals(
                Describe(UpdateKeyInPlaceProperty),
                UpdateWhereBuilder.NoLiteral,
                StringComparison.Ordinal);

            long inserted = 0L;
            long updated = 0L;
            long deleted = 0L;
            long affected = 0L;

            // ROWS THE PAYLOAD CONTRADICTED ITSELF ABOUT, counted apart from every other total. A row
            // flagged modified that supplies no updatable column value generates nothing, so it belongs
            // in neither the statement count nor the unmatched list - see the arm that increments it.
            long withoutAssignableValues = 0L;

            // THE ROWS WHOSE PREDICATE MATCHED NOTHING, RECORDED AS THEY ARE FOUND. A statement that
            // affected zero rows under updatewhere=1 is the concurrency mismatch itself - the predicate
            // carried every marked column's ORIGINAL value, so nothing matching means another writer had
            // changed the row. Collecting the pair here rather than re-deriving it afterwards is what lets
            // the conflict detail carry the CURRENT row state the contract requires, because after the
            // walk there is no longer any record of which row it was.
            List<(DwBuffer Buffer, long Row)> unmatched = [];

            // THE ROW THE WALK IS ON, HELD OUTSIDE THE TRY SO THE FAILURE ARM CAN NAME IT. PowerBuilder's
            // dberror event carries the offending buffer and row as two of its five arguments
            // [n_cst_thread_task_sqlbase.sru:L85], and the caller-side proxy's row translation reads the
            // row specifically [n_cst_threading_task_sqlupdate.sru:L315]. The catch below sits outside every
            // loop, so without these two the arm could only report the default pair - and a constraint
            // violation would then answer "buffer Primary, row 0" no matter which row actually failed,
            // which is indistinguishable from "not known" and is what a consumer must not be handed.
            //
            // THE DEFAULT PAIR IS STILL Primary/0, for the failure that happens before any row is visited -
            // an engine fault raised while preparing rather than while applying. That is genuinely "no row".
            DwBuffer failingBuffer = DwBuffer.Primary;
            long failingRow = 0L;

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
                    // RECORDED BEFORE THE STATEMENT RUNS, so the failure arm names the row that failed
                    // rather than the row before it.
                    failingBuffer = DwBuffer.Delete;
                    failingRow = row;

                    long removed = ApplyDelete(commands, table, plan, DwBuffer.Delete, row);

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
                        // See the note on the two locals: recorded before anything is applied.
                        failingBuffer = buffer;
                        failingRow = row;

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
                            // ============ THE `updatekeyinplace=no` KEY CHANGE: DELETE PLUS INSERT =====
                            // 🔴 A CHANGED KEY IS NOT AN UPDATE WHEN KEY-IN-PLACE IS OFF, AND EMITTING
                            // ONE WRITES THE WRONG STATEMENT. `updatekeyinplace=no` [dw_sqlite.srd:L14 -
                            // the sole evidenced fixture sets it, so this is the ORDINARY path and not a
                            // rare branch] means PowerBuilder performs a key change as a DELETE of the
                            // old row followed by an INSERT of the new one. An UPDATE that assigned the
                            // key would leave the row's identity rewritten in place, which is a different
                            // database state and a different set of trigger and constraint effects.
                            //
                            // The pair is applied ATOMICALLY: both statements run inside the caller's
                            // pooled transaction, and the INSERT is skipped outright when the DELETE
                            // matched nothing, so no half of the pair can ever land alone. A zero-row
                            // DELETE is the concurrency mismatch itself and is recorded as such below.
                            if (keyChangeIsDeleteAndInsert && HasKeyChange(plan, buffer, row))
                            {
                                long removedForKeyChange =
                                    ApplyDelete(commands, table, plan, buffer, row);

                                deleted++;

                                if (removedForKeyChange == 0L)
                                {
                                    // THE INSERT DOES NOT RUN. Another writer changed or removed the row
                                    // underneath this caller, so the delete's optimistic predicate matched
                                    // nothing; inserting anyway would duplicate the row it failed to
                                    // remove. The shortfall reaches the classifier as a mismatch.
                                    unmatched.Add((buffer, row));

                                    continue;
                                }

                                affected += removedForKeyChange
                                    + ApplyInsert(
                                        commands,
                                        table,
                                        plan,
                                        buffer,
                                        row,
                                        assignChangedKeys: true);

                                inserted++;

                                continue;
                            }

                            long changed = ApplyUpdate(
                                commands,
                                table,
                                plan,
                                buffer,
                                row,
                                out bool hadAssignableValues);

                            if (!hadAssignableValues)
                            {
                                // 🔴 NOT AN UNMATCHED ROW, AND KEEPING THE TWO APART IS THE WHOLE
                                // CORRECTION. No statement was generated for this row, so it cannot have
                                // lost a race with anything: counting it towards `updated` and recording
                                // it as unmatched made the classifier report an optimistic-concurrency
                                // conflict for a row nothing had touched. It is counted on its own so the
                                // classifier can answer the payload fault it actually is.
                                withoutAssignableValues++;

                                continue;
                            }

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
                long resultCode = SqliteConnectionFactory.MapSqliteResultCode(
                    failure.SqliteErrorCode,
                    failure.SqliteExtendedErrorCode);

                // ==================================================================================
                //  🔴 THE TRANSACTION'S OWN SQL STATE IS STAMPED, WHICH NOTHING IN THIS SERVICE DID.
                //
                //  In PowerBuilder the DBMS interface writes SQLCode, SQLDBCode and SQLErrText onto the
                //  transaction object after every operation, and the ported update classification reads
                //  exactly those three: it builds its failure payload from
                //  `DbErrorData.FromTransaction(transaction.SqlDbCode, transaction.SqlErrText)`
                //  [ConflictDetector.Classify, the else arm - n_cst_thread_task_sqlupdate.sru:L249-L250]
                //  and discriminates a caller-correctable constraint refusal from an engine fault on the
                //  same code. Nothing wrote that state, so the payload was always empty and the
                //  discrimination could never fire: a row omitting a NOT NULL column was answered as an
                //  unspecific database error - HTTP 502, "the fault is behind the gateway" - when the
                //  caller's own payload was the whole cause and the driver had named the column.
                //
                //  STAMPED BEFORE THE EVENT IS RAISED, so a handler that inspects the transaction sees
                //  the same state the classifier will. SQLCode is the DataWindow failure value, which is
                //  PowerBuilder's own -1 on a failed operation; SQLNRows is zero because the statement
                //  did not complete; SQLReturnData is null, which the state reads back as empty.
                //
                //  THE STATEMENT TEXT IS DELIBERATELY NOT STAMPED. It may carry interpolated literal
                //  values, and the redaction policy lives on the publication paths rather than here.
                // ==================================================================================
                transaction.StampSqlState(new SqlState(
                    sqlCode: DataWindowBufferStore.DataStoreFailure,
                    sqlDbCode: resultCode,
                    sqlNRows: 0L,
                    sqlErrText: failure.Message,
                    sqlReturnData: null));

                // The fault reaches the caller through the carrier's own database-error channel, which is
                // where the ported arms read it, and the statement text is NOT attached: it may carry
                // interpolated literal values, and the channel that publishes it applies the service's
                // redaction policy of its own accord.
                _ = _store.Carrier.OnDbError(
                    resultCode,
                    failure.Message,
                    string.Empty,
                    failingBuffer,
                    failingRow);

                // Described rather than attached - see Errors/FaultRecord.cs. This is the update path, so
                // the provider's message carries the generated statement with the row's literal VALUES
                // interpolated; the wire payload relays it to the caller, who is entitled to it, and the
                // record does not.
                _logger.LogError(
                    "An update failed inside the storage engine. FaultTypes={FaultTypes} "
                        + "RedactedMessage={RedactedMessage}",
                    FaultRecord.Types(failure),
                    FaultRecord.RedactedMessages(failure));

                return DataWindowBufferStore.DataStoreFailure;
            }

            _inserted = inserted;
            _updated = updated;
            _deleted = deleted;

            long expected = inserted + updated + deleted;

            // A CONTRADICTORY PAYLOAD IS REPORTED EVEN WHEN NO STATEMENT RAN, and it is reported BEFORE the
            // no-evidence arm below. A payload whose only modified row supplies no updatable column value
            // generates nothing at all, so `expected` is zero and the next arm would answer an ordinary
            // success - telling a caller its edit applied when no statement was ever built. The evidence
            // therefore carries the count with both row counts at zero, which IsConcurrencyMismatch
            // declines by its own third condition, and the classifier's payload-fault arm answers it.
            if (withoutAssignableValues > 0L)
            {
                _evidence = new ConcurrencyEvidence
                {
                    RowsExpected = expected,
                    RowsMatched = affected,
                    RowsWithoutAssignableValues = withoutAssignableValues,
                    Rows = ProjectUnmatchedRows(commands, table, plan, unmatched),
                };

                _store.Carrier.OnUpdateEnd(inserted, updated, deleted);

                return DataWindowBufferStore.DataStoreSuccess;
            }

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

                // THE REREAD HAPPENS HERE, INSIDE THE SAME TRANSACTION AND WHILE THE COMMAND SOURCE IS
                // STILL IN SCOPE. Once this method returns the connection is no longer addressable from
                // the classifier, and the row ordinals that name the conflicting rows are gone with the
                // walk - so a later reread could neither be attributed to a row nor be certain of
                // observing the same isolation snapshot the failed predicate was compared against.
                Rows = ProjectUnmatchedRows(commands, table, plan, unmatched),
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

                // THE PROPERTY MUST NAME SOMETHING THIS CARRIER HAS. PowerBuilder's Modify parses the
                // script against the loaded DataWindow's own object model and answers a non-empty error
                // for a property whose object does not exist, which is why the oracle's step 2 has no
                // test of its own for an updatable column name: `<name>.Update = 'yes'` for a column the
                // DataWindow does not declare is refused BY MODIFY, and `_of_updateprepare` then takes
                // its `if sErr <> ""` arm [n_cst_thread_task_sqlupdate.sru:L145-L148]. Installing the
                // property into a plain dictionary instead accepted any name at all, so a descriptor
                // naming a column that does not exist reported success and the generated statement simply
                // omitted the column - a silent misdirection rather than the refusal the oracle produces.
                //
                // THE OBJECT IS THE STEM, NOT THE WHOLE PROPERTY: a column-scoped property is
                // `<column>.<attribute>` or `#<ordinal>.<attribute>`, and PowerBuilder accepts both
                // addressing forms - the update script addresses columns by name while the identity
                // round trip addresses them by ordinal. A table-level property is `DataWindow.<...>` and
                // is not column-scoped at all, so it is passed through untouched: those are the update
                // table, the update-where mode, the key-in-place setting and the column count, none of
                // which names a column.
                //
                // THE ORACLE'S OWN FAILURE TEXT IS REUSED rather than a second shape being invented,
                // exactly as BuildModificationString's script-safety guard does: the refusal reaching the
                // caller is `无效的列名:<name>` with E_INTERNAL_ERROR, which is the arm and the wording an
                // unresolvable key column already produces [:L119-L122]. PowerBuilder's own driver wording
                // for a rejected Modify line is a runtime string that exists nowhere in the read-only
                // legacy tree, so it cannot be reproduced verbatim; what IS observable, and what the
                // caller acts on, is that the answer is non-empty and carries the offending name.
                if (!IsInstallableProperty(property, out string unresolvedColumn))
                {
                    return UpdateWhereBuilder.InvalidColumnNameMessage + unresolvedColumn;
                }

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
        /// Decides whether a modification-script property names an object this carrier holds.
        /// </summary>
        /// <param name="property">The property name, already trimmed.</param>
        /// <param name="unresolvedColumn">
        /// The column stem that could not be resolved, for the refusal's diagnostic. Empty when the
        /// property is installable.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when the property may be installed; <see langword="false"/> when the
        /// caller must refuse the whole script.
        /// </returns>
        /// <remarks>
        /// <para>
        /// TABLE-LEVEL PROPERTIES ARE ALWAYS INSTALLABLE. Every one of them begins with
        /// <c>DataWindow.</c> - the update table, the update-where mode, the key-in-place setting and the
        /// column count - and none of them names a column, so there is nothing here to resolve. The test
        /// is on the prefix rather than on an enumeration of the four names because a caller may install
        /// a table-level property this service does not itself compose, and PowerBuilder would accept it.
        /// </para>
        /// <para>
        /// A COLUMN-SCOPED PROPERTY SPLITS AT ITS LAST DOT, so <c>salary.Update</c> resolves the stem
        /// <c>salary</c> and <c>#5.Identity</c> resolves the stem <c>#5</c>. Splitting at the FIRST dot
        /// would mis-parse nothing today but would break the moment an attribute name contained one, and
        /// PowerBuilder's own vocabulary has such attributes elsewhere.
        /// </para>
        /// <para>
        /// A PROPERTY WITH NO DOT AT ALL IS REFUSED. There is no such property in PowerBuilder's
        /// vocabulary for this carrier: every describe and modify target is either
        /// <c>DataWindow.&lt;...&gt;</c> or <c>&lt;object&gt;.&lt;attribute&gt;</c>. Refusing it here is
        /// what keeps a malformed name from being installed and then silently answering a later describe.
        /// </para>
        /// <para>
        /// THE ORDINAL FORM IS RANGE-CHECKED against the column count, so <c>#0</c> and an ordinal past
        /// the last column are refused exactly as an unknown name is. R9: the ordinal is ONE-BASED in the
        /// describe vocabulary, so the valid range is 1 through the column count inclusive and is never
        /// rebased.
        /// </para>
        /// </remarks>
        private bool IsInstallableProperty(string property, out string unresolvedColumn)
        {
            unresolvedColumn = string.Empty;

            if (property.StartsWith(DataWindowPropertyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            int lastDot = property.LastIndexOf('.');

            if (lastDot <= 0)
            {
                unresolvedColumn = property;

                return false;
            }

            string stem = property[..lastDot].Trim();

            if (ResolvesToColumn(stem))
            {
                return true;
            }

            unresolvedColumn = stem;

            return false;
        }

        /// <summary>
        /// Resolves a column stem against the installed column model, by name or by ordinal.
        /// </summary>
        /// <param name="stem">The stem, either a column name or <c>#&lt;ordinal&gt;</c>.</param>
        /// <returns><see langword="true"/> when the stem names a column of this carrier.</returns>
        private bool ResolvesToColumn(string stem)
        {
            if (stem.Length == 0)
            {
                return false;
            }

            string[] columns = ColumnModel();

            if (stem.StartsWith(UpdateWhereBuilder.ColumnOrdinalPrefix, StringComparison.Ordinal))
            {
                return int.TryParse(
                        stem[UpdateWhereBuilder.ColumnOrdinalPrefix.Length..],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out int ordinal)
                    && ordinal >= 1
                    && ordinal <= columns.Length;
            }

            foreach (string column in columns)
            {
                if (string.Equals(column, stem, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

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
        /// Projects the current state of every row whose predicate matched nothing.
        /// </summary>
        /// <param name="commands">The command source statements are issued through.</param>
        /// <param name="table">The update table name the plan targets.</param>
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
            ISqliteCommandSource commands,
            string table,
            UpdateColumnPlan plan,
            List<(DwBuffer Buffer, long Row)> unmatched)
        {
            if (unmatched.Count == 0)
            {
                return [];
            }

            ConflictColumn[] columns = [.. plan.ConflictColumns
                .Where(static column => column.Number >= ItemStatusMachine.FirstColumnNumber
                    && !string.IsNullOrWhiteSpace(column.Name))
                .Select(static column => new ConflictColumn(column.Name, column.Number))];

            if (columns.Length == 0)
            {
                // Nothing addressable to project. The counts still travel, so the caller learns a
                // shortfall occurred even though no per-column state could be described.
                return [];
            }

            ConflictColumn[] keyColumns = [.. plan.KeyColumns
                .Where(static column => column.Number >= ItemStatusMachine.FirstColumnNumber
                    && !string.IsNullOrWhiteSpace(column.Name))
                .Select(static column => new ConflictColumn(column.Name, column.Number))];

            // ONE ROUND TRIP PER BATCH RATHER THAN ONE PER ROW. The reread is a compound statement whose
            // branches are the SAME per-row predicates a row-at-a-time reread would have issued, so what
            // changes is the number of commands and nothing about what is compared or what is reported.
            // See ReadStorageRows for why the attribution stays on the engine's side of the boundary.
            IReadOnlyDictionary<int, IReadOnlyDictionary<int, object?>> storage =
                ReadStorageRows(commands, table, keyColumns, columns, unmatched);

            List<ConflictRow> rows = new(unmatched.Count);

            for (int index = 0; index < unmatched.Count; index++)
            {
                (DwBuffer buffer, long row) = unmatched[index];

                // AN ABSENT ENTRY IS AN ANSWER, NOT A GAP: the other writer deleted the row, and the
                // pairing of populated originals against no current values is what tells a caller so.
                rows.Add(ConflictDetector.ProjectConflictRow(
                    _store.Carrier,
                    buffer,
                    row,
                    columns,
                    storage.TryGetValue(index, out IReadOnlyDictionary<int, object?>? current)
                        ? current
                        : null));
            }

            return rows;
        }

        /// <summary>
        /// Rereads every conflicting row's CURRENT values out of storage, on the connection the failed
        /// statements ran on, in one round trip per batch.
        /// </summary>
        /// <param name="commands">
        /// The command source. <b>THE SAME ONE THE UPDATES RAN ON</b> - see the remarks.
        /// </param>
        /// <param name="table">The update table.</param>
        /// <param name="keyColumns">The key columns, whose ORIGINAL values address each row.</param>
        /// <param name="columns">The columns to read, in payload order.</param>
        /// <param name="unmatched">
        /// The buffer and row of every statement that affected zero rows, in the order the conflict
        /// payload reports them. The POSITION in this list is the key of the returned map.
        /// </param>
        /// <returns>
        /// The storage values of each row that could still be read, keyed by that row's position in
        /// <paramref name="unmatched"/> and then by one-based column number. A position is ABSENT when
        /// its row could not be read - either because no key column is installed to address it by, or
        /// because STORAGE NO LONGER HOLDS IT.
        /// </returns>
        /// <remarks>
        /// <para>
        /// 🔴 <b>WHY THE CURRENT VALUES MUST COME FROM STORAGE AND NOT FROM THE CARRIER.</b> The
        /// contract declares <c>current_values</c> as "the CURRENT server-side values of the marked
        /// columns - the state a retry would be rebased onto"
        /// [<c>shared/PowerFramework.Contracts/Proto/common.v1.proto</c>, <c>ConflictRow</c>]. The carrier
        /// holds what THE CALLER SUBMITTED, so projecting it would echo the caller's own edit back at it as
        /// though it were the winning writer's state - and a caller that rebased on that would resubmit the
        /// identical values and conflict again for ever. The whole diagnostic value of the payload is the
        /// difference between what the caller believed and what is actually stored, and only a reread can
        /// supply the second half.
        /// </para>
        /// <para>
        /// <b>ON THE SAME CONNECTION AND THEREFORE INSIDE THE SAME TRANSACTION.</b> Read through the
        /// command source the statements ran on, so the state reported is the state the failed predicate
        /// was compared against under the same isolation. A second connection could observe a different
        /// snapshot and would report a row state that never coexisted with the failure.
        /// </para>
        /// <para>
        /// <b>ADDRESSED BY THE KEY COLUMNS' ORIGINAL VALUES</b>, because that is the one part of the
        /// failed predicate that still identifies the row: under <c>updatewhere=1</c> the mismatch is a
        /// NON-key column moving underneath the caller, so the key still locates it. With no key column
        /// installed there is nothing to address the row by and the reread declines rather than guessing a
        /// predicate.
        /// </para>
        /// <para>
        /// 🔴 <b>ONE COMMAND FOR THE WHOLE BATCH, AND THE ATTRIBUTION STAYS ON THE ENGINE'S SIDE.</b> An
        /// earlier form issued one keyed <c>SELECT</c> per conflicting row, so a conflict over N rows cost
        /// N round trips inside an already-failed update. The batch is a compound statement whose branches
        /// are the SAME per-row predicates - branch <c>i</c> carries exactly the text and the parameters
        /// row <c>i</c> would have been read with - joined by <c>UNION ALL</c>, and each branch selects a
        /// LITERAL ORDINAL as its first column so every returned row names the submitted row it belongs
        /// to.
        /// </para>
        /// <para>
        /// <b>THAT TAG IS WHY THE BATCH IS SAFE, AND AN <c>IN</c> LIST WOULD NOT HAVE BEEN.</b> Matching
        /// returned rows back to submitted rows by comparing key VALUES in managed code would introduce a
        /// second comparison semantics beside the engine's: SQLite compares under column type affinity and
        /// the column's declared collation, so a key the engine matched could fail a managed comparison and
        /// the row would be reported as DELETED when it is present - a wrong conflict payload, which is
        /// worse than the round trips. Reading the engine's own answer to "which branch matched" keeps the
        /// comparison exactly where the per-row form had it. The ordinal is a server-side integer this
        /// method generates, never caller input, so it is a literal rather than a parameter.
        /// </para>
        /// <para>
        /// <b>CHUNKED, BECAUSE A PARAMETER CEILING IS A REAL LIMIT RATHER THAN A HYPOTHETICAL ONE.</b>
        /// Every non-null key value is a parameter, so a batch's parameter count is the number of rows
        /// times the number of key columns; SQLite refuses a statement past its own ceiling. The batch is
        /// therefore split so no single command exceeds <see cref="MaximumBatchedParameters"/>, and a
        /// batch of one row degenerates to exactly the statement the per-row form issued.
        /// </para>
        /// <para>
        /// <b>AN ABSENT POSITION IS AN ANSWER, NOT A FAULT.</b> The other writer may have DELETED the row.
        /// The projection then reports the original values with NO current values, and the pairing of a
        /// populated original set against an empty current set is what tells a caller the row is gone -
        /// distinct from a row whose values changed, which reports both sets.
        /// </para>
        /// <para>
        /// PARAMETERIZED, and the statement carries only identifiers admitted by
        /// <see cref="SqlIdentifierGuard"/> from the carrier's own installed column model - never a
        /// caller-supplied fragment. It is a plain SELECT and mutates nothing, so it cannot alter the
        /// state it is reporting.
        /// </para>
        /// </remarks>
        private IReadOnlyDictionary<int, IReadOnlyDictionary<int, object?>> ReadStorageRows(
            ISqliteCommandSource commands,
            string table,
            IReadOnlyList<ConflictColumn> keyColumns,
            IReadOnlyList<ConflictColumn> columns,
            List<(DwBuffer Buffer, long Row)> unmatched)
        {
            Dictionary<int, IReadOnlyDictionary<int, object?>> projected = [];

            if (keyColumns.Count == 0)
            {
                // Nothing addresses a row, so nothing is reread - the same answer the per-row form gave,
                // reached without issuing a statement that could not have had a predicate.
                return projected;
            }

            // ROWS PER COMMAND, BOUNDED BY BOTH CEILINGS, SMALLER WINS. The parameter ceiling is derived
            // from the key width, because a wide key uses more of it per row; the compound-term ceiling is
            // flat, because each row contributes exactly one term whatever its key. At least one row
            // always travels, because a batch of none would loop for ever.
            int rowsPerCommand = Math.Max(
                1,
                Math.Min(MaximumBatchedRows, MaximumBatchedParameters / keyColumns.Count));

            for (int start = 0; start < unmatched.Count; start += rowsPerCommand)
            {
                int length = Math.Min(rowsPerCommand, unmatched.Count - start);

                ReadStorageBatch(commands, table, keyColumns, columns, unmatched, start, length, projected);
            }

            if (projected.Count < unmatched.Count)
            {
                // ONE RECORD FOR THE WHOLE BATCH, and it names no key, no column and no value: a caller
                // reading the payload sees which rows carry no current values, and an operator reading the
                // log needs only the fact that some no longer exist (constraint C-F).
                _logger.LogWarning(
                    "{Missing} of {Conflicting} conflicting row(s) could not be reread from storage, so "
                        + "their conflict payload reports the submitted originals with no current values - "
                        + "those rows no longer exist.",
                    unmatched.Count - projected.Count,
                    unmatched.Count);
            }

            return projected;
        }

        /// <summary>
        /// Rereads one chunk of the conflicting rows with a single compound statement.
        /// </summary>
        /// <param name="commands">The command source the failed statements ran on.</param>
        /// <param name="table">The update table.</param>
        /// <param name="keyColumns">The key columns whose ORIGINAL values address each row.</param>
        /// <param name="columns">The columns to read, in payload order.</param>
        /// <param name="unmatched">The whole unmatched list.</param>
        /// <param name="start">The index in <paramref name="unmatched"/> this chunk begins at.</param>
        /// <param name="length">How many rows this chunk covers.</param>
        /// <param name="projected">
        /// The map every chunk contributes to, keyed by position in <paramref name="unmatched"/>.
        /// </param>
        /// <remarks>
        /// <para>
        /// THE BRANCH TEXT IS THE PER-ROW STATEMENT, UNCHANGED. Each branch is
        /// <c>SELECT &lt;ordinal&gt; AS &lt;sentinel&gt;, &lt;columns&gt; FROM &lt;table&gt; WHERE
        /// &lt;key predicate&gt;</c>, and the key predicate is built exactly as the update's own predicate
        /// builds it: an equality against a parameter for a non-null original, and <c>IS NULL</c> for a
        /// null one, because an equality against null is never true in SQL and the reread must address the
        /// same row the statement did.
        /// </para>
        /// <para>
        /// THE SENTINEL ALIAS IS NAMED IN THE FRAMEWORK'S OWN SENTINEL STYLE, for the same reason the
        /// paging rewriters' sentinels are: an alias that could collide with a real column name would make
        /// the ordinal unreadable on exactly the payload a caller acts on.
        /// </para>
        /// <para>
        /// A ROW WHOSE BRANCH MATCHED NOTHING SIMPLY CONTRIBUTES NO RESULT ROW, which is how a deleted row
        /// stays distinguishable without a second query.
        /// </para>
        /// </remarks>
        private void ReadStorageBatch(
            ISqliteCommandSource commands,
            string table,
            IReadOnlyList<ConflictColumn> keyColumns,
            IReadOnlyList<ConflictColumn> columns,
            List<(DwBuffer Buffer, long Row)> unmatched,
            int start,
            int length,
            Dictionary<int, IReadOnlyDictionary<int, object?>> projected)
        {
            StringBuilder statement = new();
            List<object?> values = [];

            for (int offset = 0; offset < length; offset++)
            {
                int position = start + offset;
                (DwBuffer buffer, long row) = unmatched[position];

                if (offset > 0)
                {
                    _ = statement.Append(" UNION ALL ");
                }

                _ = statement
                    .Append("SELECT ")
                    .Append(position.ToString(CultureInfo.InvariantCulture))
                    .Append(" AS ")
                    .Append(ConflictRowOrdinalAlias);

                for (int index = 0; index < columns.Count; index++)
                {
                    _ = statement.Append(", ").Append(columns[index].Name);
                }

                _ = statement.Append(" FROM ").Append(table).Append(" WHERE ");

                for (int index = 0; index < keyColumns.Count; index++)
                {
                    if (index > 0)
                    {
                        _ = statement.Append(" AND ");
                    }

                    object? original =
                        _store.Carrier.GetItemOriginalValue(row, keyColumns[index].Number, buffer);

                    if (original is null)
                    {
                        // AN EQUALITY AGAINST NULL IS NEVER TRUE IN SQL, so the null key is compared with
                        // IS NULL exactly as the update predicate does - the two must agree or the reread
                        // would address a different row than the statement did.
                        _ = statement.Append(keyColumns[index].Name).Append(" IS NULL");

                        continue;
                    }

                    values.Add(original);

                    _ = statement
                        .Append(keyColumns[index].Name)
                        .Append(" = @p")
                        .Append(values.Count.ToString(CultureInfo.InvariantCulture));
                }
            }

            using SqliteCommand command = commands.CreateCommand();
            command.CommandText = statement.ToString();

            for (int index = 0; index < values.Count; index++)
            {
                _ = command.Parameters.AddWithValue(
                    "@p" + (index + 1).ToString(CultureInfo.InvariantCulture),
                    values[index] ?? DBNull.Value);
            }

            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                // COLUMN 0 IS THE ORDINAL THE BRANCH CARRIED, so the row that came back names the
                // submitted row it belongs to and no value comparison is needed to find out.
                int position = (int)reader.GetInt64(0);

                Dictionary<int, object?> storage = new(columns.Count);

                for (int index = 0; index < columns.Count; index++)
                {
                    // DBNull IS FOLDED ONTO NULL, because null is a value in this model and the wire
                    // projection has a published null arm for it; leaving DBNull would reach the value
                    // mapper as a type it cannot express and fail the projection of a legitimate stored
                    // null. The reader offset is one past the ordinal the branch selected first.
                    storage[columns[index].Number] =
                        reader.IsDBNull(index + 1) ? null : reader.GetValue(index + 1);
                }

                projected[position] = storage;
            }
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
        private UpdateColumnPlan BuildPlan()
        {
            string[] columns = ColumnModel();

            List<UpdateColumn> all = [];
            List<UpdateColumn> updatable = [];
            List<UpdateColumn> keys = [];
            List<UpdateColumn> whereColumns = [];

            for (int index = 0; index < columns.Length; index++)
            {
                // ONE-BASED column numbers on the carrier against the zero-based array index here.
                UpdateColumn column = new(columns[index], index + 1);

                all.Add(column);

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

            return new UpdateColumnPlan(all, updatable, keys, whereColumns);
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
        /// <param name="buffer">
        /// The buffer the row lives in. <see cref="DwBuffer.Delete"/> for an ordinary pending delete, and
        /// a MODIFIABLE buffer for the first half of a <c>updatekeyinplace=no</c> key change - which is
        /// why this is a parameter rather than the delete buffer assumed: the row whose key changed sits
        /// in the primary or filter buffer and its predicate must be read from there.
        /// </param>
        /// <param name="row">The one-based row number within <paramref name="buffer"/>.</param>
        /// <returns>The rows the statement affected.</returns>
        private long ApplyDelete(
            ISqliteCommandSource commands,
            string table,
            UpdateColumnPlan plan,
            DwBuffer buffer,
            long row)
        {
            StringBuilder statement = new("DELETE FROM ");
            _ = statement.Append(table);

            List<object?> values = [];
            AppendWhere(statement, plan, buffer, row, values);

            return Execute(commands, statement.ToString(), values, SqlPreviewType.Delete, buffer);
        }

        /// <summary>
        /// Whether one row's key has genuinely changed - a key column that is both marked modified AND
        /// holds a value differing from its original.
        /// </summary>
        /// <param name="plan">The installed column plan.</param>
        /// <param name="buffer">The buffer the row lives in.</param>
        /// <param name="row">The one-based row number within <paramref name="buffer"/>.</param>
        /// <returns><see langword="true"/> when at least one key column changed value.</returns>
        /// <remarks>
        /// <para>
        /// <b>BOTH CONJUNCTS ARE LOAD-BEARING, AND THE VALUE COMPARISON IS THE ONE THAT MATTERS.</b> The
        /// legacy's own key-change workaround RE-STAMPS every modified key column as
        /// <c>DataModified!</c> without altering its value
        /// [<c>n_cst_thread_task_sqlupdate.sru:L155-L167</c>, ported at
        /// <c>Concurrency/UpdateWhereBuilder.cs</c> <c>ApplyKeyChangeRefresh</c>], so a status test alone
        /// would report a key change for a row whose key never moved - and would then emit a DELETE plus
        /// INSERT that discards and re-creates a row for no reason, losing its generated identity in the
        /// process. Requiring the value to differ as well is what distinguishes the workaround's
        /// re-stamp from a real key edit.
        /// </para>
        /// <para>
        /// The status is still required, because an unmodified column's original value IS its current
        /// value [<c>Buffers/DataWindowBuffers.cs</c> <c>CarrierRow.GetOriginalValue</c>], so the
        /// comparison alone can never fire without it and stating it makes the intent explicit.
        /// </para>
        /// <para>
        /// NULL IS A VALUE ON BOTH SIDES, so null versus null is "unchanged" and null versus a value is
        /// "changed", rather than either being coerced to zero or to the empty string - a coercion here
        /// would silently suppress or invent a key change.
        /// </para>
        /// <para>
        /// 🔴 <b>THE COMPARISON IS <see cref="CarrierValue.AreEquivalent"/> AND NOT <c>Equals</c>, BECAUSE
        /// ONE NUMBER HAS SEVERAL FAITHFUL WIRE SPELLINGS.</b> A retrieval answers a legacy
        /// <c>number</c> column through <c>double_value</c> while a caller re-sending that same key as
        /// <c>int64_value</c> is equally within the contract, so a round trip legitimately produces
        /// <c>28L</c> beside an original of <c>28.0d</c>. <c>Equals</c> reports those DIFFERENT because
        /// their boxed types differ, and the storage engine does not agree - SQLite compares
        /// <c>28.0</c> against an <c>INTEGER</c> 28 as equal under numeric affinity, so the generated
        /// predicate matched while this test claimed the key had moved. The result was an ordinary update
        /// executed as a DELETE plus an INSERT: the right data by luck, the wrong statements, a
        /// re-created row, and <c>rowsUpdated = 0</c> reported to a caller that had updated one row.
        /// </para>
        /// </remarks>
        private bool HasKeyChange(UpdateColumnPlan plan, DwBuffer buffer, long row)
        {
            foreach (UpdateColumn column in plan.KeyColumns)
            {
                if (_store.Carrier.GetItemStatus(row, column.Number, buffer) == ItemStatus.NotModified)
                {
                    continue;
                }

                object? current = _store.Carrier.GetItemValue(row, column.Number, buffer);
                object? original = _store.Carrier.GetItemOriginalValue(row, column.Number, buffer);

                if (!CarrierValue.AreEquivalent(current, original))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Generates and runs the insert statement for one new row.
        /// </summary>
        /// <param name="commands">The command source.</param>
        /// <param name="table">The update table.</param>
        /// <param name="plan">The installed column plan.</param>
        /// <param name="buffer">The buffer the row lives in.</param>
        /// <param name="row">The one-based row number within that buffer.</param>
        /// <param name="assignChangedKeys">
        /// <see langword="true"/> only for the INSERT half of a <c>updatekeyinplace=no</c> key change, where
        /// the caller assigned a SPECIFIC key and that key must be written even when the column also
        /// carries the identity flag. <see langword="false"/> for an ordinary insert of a new row, where
        /// the store assigns the identity.
        /// </param>
        /// <returns>The rows the statement affected.</returns>
        /// <remarks>
        /// <para>
        /// THE IDENTITY COLUMN IS OMITTED FROM THE COLUMN LIST, which is what lets the store assign it and
        /// is the whole reason the identity round trip exists: the caller reads the assigned values back
        /// out afterwards. Including it would insert the carrier's placeholder and make the round trip
        /// report a value the store never generated.
        /// </para>
        /// <para>
        /// 🔴 <b>AND THE ASSIGNED VALUE IS READ BACK ONTO THE ROW BEFORE THIS METHOD RETURNS.</b> Omitting
        /// the column is only half of the round trip; without the read-back the carrier still holds
        /// whatever placeholder the caller sent, and
        /// <c>Concurrency/IdentityColumnResolver.CollectPrimaryValues</c> - which reads
        /// <c>GetItemNumber(row, identityColumn)</c> off this same carrier
        /// [<c>n_cst_thread_task_sqlupdate.sru:L231</c>] - would report that placeholder as the generated
        /// identity. A caller that inserted rows could then never reconcile them with the values the
        /// database actually assigned, which is precisely what the identity block exists to tell it.
        /// </para>
        /// </remarks>
        private long ApplyInsert(
            ISqliteCommandSource commands,
            string table,
            UpdateColumnPlan plan,
            DwBuffer buffer,
            long row,
            bool assignChangedKeys = false)
        {
            StringBuilder statement = new("INSERT INTO ");
            _ = statement.Append(table).Append(" ( ");

            List<object?> values = [];
            bool first = true;

            foreach (UpdateColumn column in plan.UpdatableColumns)
            {
                // THE IDENTITY COLUMN IS OMITTED FOR A GENUINELY NEW ROW AND WRITTEN FOR A CHANGED KEY,
                // and the asymmetry is the difference between the two operations rather than an
                // inconsistency. A new row has no key yet, so the store assigns one and the round trip
                // reports it. A KEY CHANGE is the caller assigning a SPECIFIC key: omitting it there would
                // let the store pick a value the caller did not ask for, so the row would be re-created
                // under a different key and the requested change would be silently discarded.
                if (IsFlagSet(column.Name + IdentitySuffix)
                    && !(assignChangedKeys && IsFlagSet(column.Name + KeySuffix)))
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

            long affected = Execute(commands, statement.ToString(), values, SqlPreviewType.Insert, buffer);

            if (affected > 0L)
            {
                WriteBackGeneratedIdentity(commands, plan, buffer, row);
            }

            return affected;
        }

        /// <summary>
        /// Reads the identity the store assigned to the row just inserted and writes it onto that row.
        /// </summary>
        /// <param name="commands">
        /// The command source. <b>THE SAME ONE THE INSERT RAN ON, which is a correctness requirement
        /// rather than an efficiency one</b> - see the remarks.
        /// </param>
        /// <param name="plan">The installed column plan.</param>
        /// <param name="buffer">The buffer the inserted row lives in.</param>
        /// <param name="row">The one-based row number within <paramref name="buffer"/>.</param>
        /// <remarks>
        /// <para>
        /// <b>THE RETRIEVAL IS CONNECTION-SCOPED AND IS ONLY CORRECT IMMEDIATELY AFTER THE INSERT.</b>
        /// <c>last_insert_rowid()</c> answers the most recent successful insert ON THIS CONNECTION, so it
        /// is read through the same command source the statement ran on and before any other statement can
        /// run - the walk is serialized by the transaction gate, and this call sits inside the same
        /// per-row step as the insert it belongs to. Reading it on a second connection, or after a later
        /// insert, would attribute one row's identity to another.
        /// </para>
        /// <para>
        /// NOTHING HAPPENS WHEN NO IDENTITY COLUMN IS INSTALLED, which is the ordinary case for a table
        /// without one. The column is found from the installed <c>.Identity</c> flag rather than from a
        /// descriptor, so it is the same source the insert used when it decided to omit a column - one
        /// reading, not two.
        /// </para>
        /// <para>
        /// <b>THE WRITE FLIPS NO STATUS, DELIBERATELY.</b> <see cref="DataWindowBufferStore.SetItemValue"/>
        /// changes the value and nothing else [<c>Buffers/DataWindowBuffers.cs</c>
        /// <c>CarrierRow.SetValue</c>], so the row keeps the <c>NewModified!</c> status that makes the
        /// resolver collect it [<c>n_cst_thread_task_sqlupdate.sru:L230, :L238</c>]. A write that stamped a
        /// status would silently remove the row from the collection it exists to feed.
        /// </para>
        /// <para>
        /// A NON-NUMERIC OR ABSENT ANSWER LEAVES THE ROW ALONE rather than writing a zero. The resolver
        /// preserves null as null on purpose, and fabricating a zero identity would put a value on the wire
        /// that no row carries.
        /// </para>
        /// </remarks>
        private void WriteBackGeneratedIdentity(
            ISqliteCommandSource commands,
            UpdateColumnPlan plan,
            DwBuffer buffer,
            long row)
        {
            UpdateColumn? identityColumn = null;

            foreach (UpdateColumn candidate in plan.AllColumns)
            {
                if (IsFlagSet(candidate.Name + IdentitySuffix))
                {
                    identityColumn = candidate;
                    break;
                }
            }

            if (identityColumn is null)
            {
                return;
            }

            using SqliteCommand command = commands.CreateCommand();
            command.CommandText = LastInsertRowIdStatement;

            object? scalar = command.ExecuteScalar();
            long? generated = ToNumber(scalar);

            if (generated is null)
            {
                _logger.LogWarning(
                    "The storage engine answered no generated identity for an inserted row, so the "
                        + "carrier's own value for column {ColumnNumber} was left as it stands.",
                    identityColumn.Value.Number);

                return;
            }

            _ = _store.Carrier.SetItemValue(row, identityColumn.Value.Number, buffer, generated.Value);
        }

        /// <summary>
        /// Generates and runs the update statement for one modified row.
        /// </summary>
        /// <param name="commands">The command source.</param>
        /// <param name="table">The update table.</param>
        /// <param name="plan">The installed column plan.</param>
        /// <param name="buffer">The buffer the row lives in.</param>
        /// <param name="row">The one-based row number within that buffer.</param>
        /// <param name="hadAssignableValues">
        /// <see langword="false"/> when the row produced NO assignment at all, which is a payload
        /// contradiction rather than a statement that matched nothing - see the remarks.
        /// </param>
        /// <returns>The rows the statement affected, or zero when no statement was generated.</returns>
        /// <remarks>
        /// 🔴 <b>THE OUT-PARAMETER IS WHAT KEEPS "NO STATEMENT" DISTINGUISHABLE FROM "NO MATCH", AND
        /// COLLAPSING THEM REPORTED A CONCURRENCY CONFLICT FOR A ROW NOTHING HAD TOUCHED.</b> Both cases
        /// answer zero affected rows and the two mean opposite things: a statement that ran and matched
        /// nothing is the optimistic miss this whole contract exists to detect, while a row that generated
        /// no statement was never submitted to anything and cannot have lost a race. The caller counts the
        /// second separately and the classifier answers it as the caller's own payload fault.
        /// </remarks>
        private long ApplyUpdate(
            ISqliteCommandSource commands,
            string table,
            UpdateColumnPlan plan,
            DwBuffer buffer,
            long row,
            out bool hadAssignableValues)
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
                // A row flagged modified whose every updatable column reads unmodified generates no
                // statement. Reported through the out-parameter rather than as a zero-row result, because
                // the caller must not read it as a predicate that matched nothing - see the remarks.
                hadAssignableValues = false;

                return 0L;
            }

            hadAssignableValues = true;

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
        /// <param name="AllColumns">
        /// Every column of the carrier's model, in ONE-BASED column order, whatever flags it carries.
        /// Carried because two decisions are not expressible from the flagged subsets: the identity column
        /// need not be updatable, and the conflict projection reports the union of key and marked columns
        /// in model order rather than in the order either subset happens to hold them.
        /// </param>
        /// <param name="UpdatableColumns">The columns marked updatable, in column order.</param>
        /// <param name="KeyColumns">The columns marked as keys, in column order.</param>
        /// <param name="WhereClauseColumns">The columns marked for the concurrency predicate.</param>
        private sealed record UpdateColumnPlan(
            IReadOnlyList<UpdateColumn> AllColumns,
            IReadOnlyList<UpdateColumn> UpdatableColumns,
            IReadOnlyList<UpdateColumn> KeyColumns,
            IReadOnlyList<UpdateColumn> WhereClauseColumns)
        {
            /// <summary>
            /// Every column's NAME positioned by its one-based number, for
            /// <c>DataWindowBufferStore.SetColumnNames</c>.
            /// </summary>
            /// <remarks>
            /// POSITIONED BY <see cref="UpdateColumn.Number"/> RATHER THAN BY ENUMERATION ORDER, even though
            /// <see cref="AllColumns"/> is documented as being in one-based order. The two agree today; if a
            /// gap ever appeared, indexing by position would shift every name after it by one and hand
            /// consumers a payload in which the name and the ordinal on the same column disagreed - which is
            /// worse than an absent name, and is precisely the one-based translation hazard this port keeps
            /// naming. A number outside the model is skipped rather than trusted.
            /// </remarks>
            internal IReadOnlyList<string> ColumnNames
            {
                get
                {
                    string[] names = new string[AllColumns.Count];
                    Array.Fill(names, string.Empty);

                    foreach (UpdateColumn column in AllColumns)
                    {
                        if (column.Number >= 1 && column.Number <= names.Length)
                        {
                            names[column.Number - 1] = column.Name;
                        }
                    }

                    return names;
                }
            }

            /// <summary>
            /// The columns the conflict payload reports: every KEY column and every column marked for the
            /// concurrency predicate, in one-based model order, with no duplicate.
            /// </summary>
            /// <remarks>
            /// <para>
            /// 🔴 <b>THE UNION, NOT EITHER HALF, AND NOT THE UPDATABLE SET.</b> Under
            /// <c>updatewhere=1</c> the failed statement's predicate was the key column PLUS the original
            /// value of every marked column, so those are exactly the columns whose comparison decided the
            /// conflict - and a caller rebasing a retry needs all of them. Reporting the UPDATABLE set
            /// instead would omit a key-only or marked-only column, which is the column most likely to be
            /// the one that moved; reporting only the keys would omit the values that actually mismatched.
            /// </para>
            /// <para>
            /// MODEL ORDER IS PRESERVED because a consumer comparing two value lists position by position
            /// needs a stable order, and the model's own one-based column order is the only order both
            /// halves of the union share.
            /// </para>
            /// </remarks>
            internal IReadOnlyList<UpdateColumn> ConflictColumns =>
                [.. AllColumns.Where(column =>
                    KeyColumns.Contains(column) || WhereClauseColumns.Contains(column))];
        }
    }
}
