// ==================================================================================================
//  SqliteTransactionEngine.cs - THE PROVISIONED ITransactionEngine
//  ------------------------------------------------------------------------------------------------
//  ROLE
//  The connection-owning half of the legacy transaction object, over the ONE storage engine this
//  repository evidences. `Transactions/TransactionPool.cs` owns the pooling, the reference counting,
//  the idle expiry, the hooks and the SQLCode algebra; this type owns nothing but the connection and
//  the four verbs that move it, so the pool's ported behaviour is untouched by what is bound here.
//
//  WHY THIS EXISTS, STATED AGAINST THE CONSTRAINT IT COULD BE MISREAD AS BREAKING (C-E)
//  An earlier revision shipped an engine that refused to connect, on the argument that the legacy
//  transaction object enumerates exactly two database types - SQL Server as 0 and Oracle as 1
//  [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L60-L61] - and that neither has a schema,
//  a connection string or one line of DDL anywhere in the repository, so connecting would require
//  inventing a target. The first half of that is true and is still respected below. The conclusion did
//  not follow: it silently equated "the DIALECT enumeration omits SQLite" with "there is no evidenced
//  target at all", and the repository plainly contradicts the second clause. The evidenced target is
//  SQLite - `ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L456` opens a database through a URI whose
//  grammar `Data/SqliteConnectionFactory.cs` reproduces in full, and `:L463-L469` is the ONLY DDL in
//  the estate. So this engine fabricates nothing: it connects to the one storage engine the repository
//  actually documents, through the factory that already owns that URI grammar, and no SQL Server or
//  Oracle provider, connection or credential appears anywhere in this file.
//
//  WHAT IS *NOT* CHANGED BY PROVISIONING IT - AND THIS IS THE PART A READER MUST NOT UNDO
//  The dialect discriminator is still the CALLER'S, echoed back untouched through `Dbms`. The
//  classification over it stays exactly as the legacy wrote it: anything whose DBMS string does not
//  contain ORACLE is the SQL Server type [PooledTransaction.GetDbType], there is no third arm, and
//  specifically none for SQLite. So a caller that names SQL Server still gets the SQL Server paging
//  rewriter - a pure string transform held to byte-exact parity - while the statement it produces runs
//  against the evidenced store. Adding a SQLite arm to that dispatch would be the behavioural change;
//  connecting to SQLite is not.
//
//  LEGACY REFERENCE (read only - never edited, never built, never shipped: constraint C-C)
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru
//          :L115-L142  of_Connect      the handle test, the hooks, the SQLCode verdict
//          :L145-L161  of_Disconnect   the no-op fast path that still reports success
//          :L185-L191  of_Rollback     rollback under auto-commit REPORTS FAILURE
//          :L233       of_Exec         SQLCode <> 0 and SQLCode <> 100 is the failure test, so the
//                                      no-data code 100 READS AS SUCCESS
//          :L337-L340  of_IsFailed / of_IsSucceeded, the sign tests over SQLCode
//      ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L450-L469
//                                      the URI grammar and the estate's only DDL
//      ws_objects/pfw.utility.sqlite.pbl.src/n_sqlite.sru:L12-L19
//                                      the connection-level surface the factory already ports
//
//  NOTHING IN THIS FILE READS THE LEGACY TREE. Every ws_objects/** path above appears in a comment.
//  The root .dockerignore excludes ws_objects/ from the build context, so a read would fail in a
//  container and in CI even where it happened to work locally.
//
//  WHAT THIS FILE DELIBERATELY DOES NOT DO
//    * It never deletes, recreates or reseeds the database file, and it issues no DROP of any kind.
//      The legacy test window deletes the file immediately before opening it [:L450], but that is the
//      HARNESS's setup rather than framework behaviour, and reproducing it here would destroy the
//      paired-capture rule under which a legacy-side and a target-side recording for one workflow are
//      comparable only against the same unrecreated persistence-db volume state.
//    * It reads no clock. Every liveness tick and idle-expiry comparison belongs to the pool, which
//      takes the injected TimeProvider; a DateTime.UtcNow here would be a determinism defect.
//    * It never copies the descriptor's credential anywhere. `ApplyConnectionFields` retains the
//      dialect and the auto-commit choice and NOTHING else, so the log password cannot reach this
//      object's state at all (constraint C-F).
//    * It throws no exception across its own boundary. Every provider fault becomes a failed SqlState,
//      because the pool's ported arms are written against SQLCode rather than against exceptions.
// ==================================================================================================

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using PowerFramework.Persistence.Errors;
using PowerFramework.Persistence.Transactions;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Persistence.Data
{
    /// <summary>
    /// The optional engine capability that lets the result-carrier runtime run its own statements on the
    /// connection a pooled transaction already owns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THIS INTERFACE IS WHY <c>Transactions/</c> STAYS PROVIDER-NEUTRAL.</b> The legacy fuses
    /// connection and carrier - a datastore reaches the driver through the transaction object it was
    /// given - so retrieval and update statements must run on the caller's own connection or an
    /// uncommitted write becomes invisible to a read in the same session. Declaring the command factory
    /// on <c>ITransactionEngine</c> would put SQLite into the pooling abstraction; declaring it here, in
    /// the namespace that already owns the provider, and reaching it through
    /// <see cref="IPooledTransaction.TryGetEngineCapability{TCapability}"/> keeps the provider on one
    /// side of the seam.
    /// </para>
    /// <para>
    /// An engine that does not implement it is not broken - it simply cannot serve a carrier-filling
    /// retrieval, and the runtime reports that as the datastore failure value its own signature already
    /// documents rather than as an exception.
    /// </para>
    /// </remarks>
    internal interface ISqliteCommandSource
    {
        /// <summary>
        /// Creates a command on the open connection, enlisted in any open explicit transaction.
        /// </summary>
        /// <returns>The command, which the caller owns and disposes.</returns>
        /// <exception cref="InvalidOperationException">The engine is not connected.</exception>
        SqliteCommand CreateCommand();

        /// <summary>
        /// Whether <see cref="CreateCommand"/> would succeed.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>THE COMPANION THAT TURNS AN EXCEPTION INTO A VERDICT.</b> An engine is a CAPABILITY of a
        /// pooled transaction, so a consumer that resolves one has established that the transaction is
        /// backed by SQLite - and nothing more. The transaction may still be un-connected, broken or
        /// already released, and on every one of those the command source exists while
        /// <see cref="CreateCommand"/> refuses. Each of this seam's consumers answers in the datastore
        /// alphabet or in a return code, so it needs to ASK rather than to catch: letting the refusal
        /// escape as an exception crosses a boundary whose signature declares a numeric answer, and
        /// catching <see cref="InvalidOperationException"/> instead would silently swallow a genuine
        /// programming error alongside the condition it meant to report.
        /// </para>
        /// <para>
        /// IT IS CHEAP AND SIDE-EFFECT FREE, DELIBERATELY, and that is why it is not
        /// <see cref="IPooledTransaction.IsConnected()"/>. That member is the published C-08 liveness
        /// question: it raises the check and test hooks and may send a probe statement down the
        /// connection. Asking it before every retrieval would add hook invocations and a round trip that
        /// the oracle's own retrieval path does not make. This answers only "is there an open connection
        /// behind this command source", which is exactly the precondition
        /// <see cref="CreateCommand"/> enforces.
        /// </para>
        /// </remarks>
        bool CanCreateCommand { get; }
    }

    /// <summary>
    /// The optional engine capability that reports the state of the prepared-statement store the
    /// leading-<c>@</c> execution mode populates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THIS EXISTS SO THE MODE IS VERIFIABLE, NOT SO IT IS TUNABLE.</b> The legacy selects statement
    /// caching by writing <c>@</c> in front of the statement
    /// [<c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L397-L398</c>], and the effect it describes -
    /// avoiding a re-parse - is invisible in the result of any one call. A contract that promises the
    /// mode and cannot demonstrate it is indistinguishable from one that silently drops it, which is the
    /// exact defect this capability closes. Every member here is a COUNT of something that happened, so
    /// a test asserts the mode structurally instead of asserting a duration.
    /// </para>
    /// <para>
    /// <b>NOT ON <c>ITransactionEngine</c>, FOR THE SAME REASON <see cref="ISqliteCommandSource"/> IS
    /// NOT.</b> A prepared statement is a provider concept; putting its accounting on the pooling
    /// abstraction would put SQLite into <c>Transactions/</c>. It is reached through
    /// <see cref="IPooledTransaction.TryGetEngineCapability{TCapability}"/>, so an engine that keeps no
    /// prepared form is not broken - it simply does not publish this, and a consumer that asks gets a
    /// defined negative rather than a fabricated zero.
    /// </para>
    /// <para>
    /// <b>NO MEMBER HERE IS A PERFORMANCE STATEMENT (AAP §0.8.5).</b> The repository publishes no
    /// latency budget, no throughput target and no availability commitment, so these counts describe
    /// only how many times a statement was retained, matched or newly prepared. Reading a rate or a
    /// saving out of them would be inventing a requirement the plan forbids asserting.
    /// </para>
    /// </remarks>
    internal interface IPreparedStatementStore
    {
        /// <summary>The number of statements currently retained in prepared form.</summary>
        /// <remarks>
        /// Never exceeds <see cref="PreparedStatementCapacity"/>, and drops to zero when the connection
        /// closes, because a prepared statement is prepared against a connection and cannot outlive it.
        /// </remarks>
        int PreparedStatementCount { get; }

        /// <summary>
        /// The number of executions that MATCHED an already-retained statement, so the provider was not
        /// asked to parse the text again.
        /// </summary>
        /// <remarks>
        /// This is the counter that makes the mode observable: the legacy demonstration executes one
        /// prefixed statement ten times in a loop [<c>w_test_sqlite.srw:L396-L406</c>], which is nine
        /// matches after the first retention.
        /// </remarks>
        long PreparedStatementHits { get; }

        /// <summary>
        /// The number of executions that asked for the retained form and did not find one, so a new
        /// statement was prepared.
        /// </summary>
        /// <remarks>
        /// Counts only executions that REQUESTED the mode. An ordinary immediate execution is neither a
        /// hit nor a miss, because it never consulted the store - folding it in would make the two
        /// counters describe the connection's whole traffic rather than the mode.
        /// </remarks>
        long PreparedStatementMisses { get; }

        /// <summary>The most statements this store will retain at once.</summary>
        /// <remarks>
        /// <b>A BOUND IS NOT OPTIONAL HERE.</b> The legacy comment calls the mode 空间换时间 - trading
        /// space for time - and a store with no bound trades away unbounded space on a connection that
        /// may live for the whole session. Past the bound the least-recently-matched entry is released,
        /// so the mode keeps working on the statements a caller actually re-executes.
        /// </remarks>
        int PreparedStatementCapacity { get; }
    }

    /// <summary>
    /// The provisioned <see cref="ITransactionEngine"/>: one SQLite connection, composed through
    /// <see cref="SqliteConnectionFactory"/>, plus the four verbs that move it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>ONE ENGINE PER POOLED TRANSACTION, AND THE LIFETIME IS LOAD BEARING.</b>
    /// <c>PooledTransactionActivator</c> takes a DELEGATE producing an engine rather than an engine
    /// instance, precisely because the pool creates one engine per pooled transaction and disposes it on
    /// collection. This type is therefore registered TRANSIENT: a singleton registration would hand the
    /// same connection to every pooled transaction and then dispose it when the first one was collected,
    /// which is a use-after-dispose that only shows up under concurrent load.
    /// </para>
    /// <para>
    /// <b>NOT THREAD SAFE, BY DESIGN AND BY CONTRACT.</b> <see cref="SqliteConnection"/> is not thread
    /// safe and neither is this type. It does not need to be: a pooled transaction is checked out to one
    /// task at a time, and the legacy encodes exactly that as an affinity contract rather than as a
    /// convention. Adding a lock here would hide a caller that had violated the checkout discipline.
    /// </para>
    /// </remarks>
    internal sealed class SqliteTransactionEngine : ITransactionEngine, ISqliteCommandSource, IPreparedStatementStore
    {
        /// <summary>
        /// The handle a connected engine reports.
        /// </summary>
        /// <remarks>
        /// The pool tests the handle three ways - <c>!= 0</c> before connecting [<c>:L115</c>],
        /// <c>&lt;= 0</c> before disconnecting [<c>:L145</c>], and truthiness elsewhere - so the value
        /// only has to be a stable positive integer. One is used rather than a captured provider handle
        /// because <see cref="SqliteConnection"/> exposes none, and inventing a numeric identity that
        /// looked like a provider handle would invite a reader to attach meaning to it.
        /// </remarks>
        internal const int ConnectedHandle = 1;

        /// <summary>
        /// The row count a successful connect reports.
        /// </summary>
        /// <remarks>
        /// 🔴 LOAD BEARING AND EASILY MISSED. The pool's liveness probe judges its verdict on
        /// <c>SQLNRows &gt; 0</c> rather than on the code [<c>n_cst_thread_trans.sru:L207</c>], so a
        /// connect that reported zero rows would read as NOT connected and every probe would then
        /// reconnect a perfectly live connection.
        /// </remarks>
        internal const long ConnectedRowCount = 1L;

        /// <summary>The message a verb reports when it is called on an unconnected engine.</summary>
        internal const string NotConnectedText =
            "The transaction object is not connected, so this operation has nothing to act on. Connect "
            + "the transaction before committing, rolling back or executing a statement.";

        /// <summary>The message a rollback or commit reports when no transaction is open.</summary>
        /// <summary>The diagnostic a cancelled call reports.</summary>
        /// <remarks>
        /// NAMES THE CANCELLATION AND NOTHING ELSE. It carries no statement text, no connection string and
        /// no parameter value, because a cancellation is not a fault report and this text reaches the same
        /// channels a fault report does.
        /// </remarks>
        internal const string CancelledText =
            "The call was cancelled before it reached the database.";

        /// <summary>The diagnostic a connect reports when the descriptor supplied a credential.</summary>
        /// <remarks>
        /// <para>
        /// <b>REFUSED, NOT IGNORED, AND THE DIFFERENCE IS THE WHOLE POINT.</b> SQLite has no password on
        /// the unencrypted path, and the encrypted one is out of Phase-1 scope because the shipped
        /// cipher library's key derivation and per-page integrity options are not reachable through any
        /// framework API (AAP 0.6.4, R3). Dropping the credential on the floor would open the connection
        /// WITHOUT the protection the caller asked for and report success, which is the worst of the
        /// three possible outcomes - worse than refusing and worse than failing loudly.
        /// </para>
        /// <para>
        /// The companion refusal lives in <c>SqliteConnectionFactory</c>, which will not START while a
        /// password is CONFIGURED. This one covers the other arrival route: a credential carried on a
        /// per-session descriptor, which configuration validation never sees.
        /// </para>
        /// <para>
        /// NAMES THE REFUSAL AND NOTHING ELSE - no credential, no length, no connection string.
        /// </para>
        /// </remarks>
        internal const string CredentialRefusedText =
            "The transaction descriptor supplied a login password, but encrypted SQLite is out of scope "
            + "for this phase and this connection was refused rather than opened without the protection "
            + "the caller asked for. Remove the password from the descriptor to connect.";

        internal const string NoOpenTransactionText =
            "No explicit transaction is open on this connection, so there is nothing to commit or roll "
            + "back. A connection opened with auto-commit on applies each statement as it executes.";

        /// <summary>The most statements the prepared-statement store retains at once.</summary>
        /// <remarks>
        /// <para>
        /// <b>A CONSTANT RATHER THAN A SETTING, DELIBERATELY.</b> The legacy exposes no knob for the
        /// mode at all - the only control it has is whether the statement carries the prefix - so adding
        /// a configurable bound would be adding a capability the oracle does not have (constraint C-B).
        /// The value only has to be large enough that a caller re-executing a handful of statements in a
        /// session keeps hitting, and small enough that the store cannot become the reason a long-lived
        /// connection grows without limit.
        /// </para>
        /// <para>
        /// It is internal rather than private so the suite can assert the eviction arm at the bound
        /// instead of hard-coding a number that would silently stop testing eviction if the bound moved.
        /// </para>
        /// </remarks>
        internal const int PreparedStatementCapacityLimit = 64;

        /// <summary>Composes the connection string from the ported URI grammar.</summary>
        private readonly SqliteConnectionFactory _connections;

        /// <summary>Records connection-level faults that the SqlState alone would not explain.</summary>
        private readonly ILogger<SqliteTransactionEngine> _logger;

        /// <summary>The open connection, or <see langword="null"/> when this engine is not connected.</summary>
        private SqliteConnection? _connection;

        /// <summary>The open explicit transaction, or <see langword="null"/> under auto-commit.</summary>
        private SqliteTransaction? _transaction;

        /// <summary>Whether this engine has been disposed.</summary>
        private bool _disposed;

        /// <summary>The auto-commit choice, honoured on the next connect and by the two unwind verbs.</summary>
        private bool _autoCommit;

        /// <summary>
        /// Whether the applied descriptor supplied a login password.
        /// </summary>
        /// <remarks>
        /// A BOOLEAN AND NEVER THE VALUE, which is what keeps this refusal compatible with the
        /// credential-hygiene rule the rest of this class observes (constraint C-F): the descriptor
        /// answers the presence question through <c>TransactionData.HasCredential</c> and the value itself
        /// is never read, never copied and never reachable from this object.
        /// </remarks>
        private bool _credentialSupplied;

        /// <summary>
        /// The statements retained in prepared form, keyed by the CANONICAL text that was prepared.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>KEYED ON THE CANONICAL TEXT AND NEVER ON THE RENDERED TEXT.</b> The canonical form is the
        /// one with <c>@pN</c> placeholders, so ten executions of one statement with ten different
        /// parameter sets are ten matches on one entry - which is exactly the legacy demonstration's
        /// shape [<c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L396-L406</c>]. Keying on the
        /// rendered text would produce a miss every time a value changed, so the mode would retain
        /// everything and match nothing.
        /// </para>
        /// <para>
        /// <b>C-F:</b> the key is the PLACEHOLDER form, which carries no caller value by construction.
        /// The rendered, literal-bearing form is never stored here, never logged from here, and is not
        /// reachable from this object once the execution that carried it has returned.
        /// </para>
        /// <para>
        /// Created lazily so an engine that is never asked for the mode allocates nothing, and cleared
        /// with the connection because a prepared statement cannot outlive what it was prepared against.
        /// </para>
        /// </remarks>
        private Dictionary<string, RetainedStatement>? _preparedStatements;

        /// <summary>The monotonic stamp the eviction arm orders entries by.</summary>
        /// <remarks>
        /// A COUNTER RATHER THAN A CLOCK, so eviction order is deterministic and a test does not have to
        /// arrange for time to pass. It is stamped on retention and re-stamped on every match, which is
        /// what makes the victim the least recently MATCHED entry rather than the oldest one.
        /// </remarks>
        private long _preparedStatementStamp;

        /// <summary>Executions that requested the mode and matched a retained statement.</summary>
        private long _preparedStatementHits;

        /// <summary>Executions that requested the mode and had to prepare a new statement.</summary>
        private long _preparedStatementMisses;

        /// <summary>Initializes the engine.</summary>
        /// <param name="connections">The factory the connection string is composed by.</param>
        /// <param name="logger">The logger connection-level faults are recorded through.</param>
        /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
        public SqliteTransactionEngine(
            SqliteConnectionFactory connections,
            ILogger<SqliteTransactionEngine> logger)
        {
            _connections = connections ?? throw new ArgumentNullException(nameof(connections));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc/>
        public int DbHandle => _connection is not null ? ConnectedHandle : 0;

        /// <inheritdoc/>
        /// <remarks>
        /// THE CALLER'S OWN DISCRIMINATOR, ECHOED BACK UNCHANGED. See this file's header: the paging
        /// dispatcher classifies this string exactly as the legacy does, and provisioning a real
        /// connection must not alter what a caller is told about its own dialect.
        /// </remarks>
        public string Dbms { get; private set; } = string.Empty;

        /// <inheritdoc/>
        /// <remarks>
        /// <para>
        /// Read back as configured whether or not a connection is open, because the descriptor's
        /// auto-commit choice is part of the session state a caller can read back and losing it would
        /// make the descriptor round trip lossily.
        /// </para>
        /// <para>
        /// Setting it WHILE CONNECTED takes effect immediately, because that is what the legacy's own
        /// property assignment does: switching auto-commit ON commits and releases any open explicit
        /// transaction, and switching it OFF opens one. A setter that only took effect on the next
        /// connect would leave a caller's <c>Commit</c> silently applying to nothing.
        /// </para>
        /// </remarks>
        public bool AutoCommit
        {
            get => _autoCommit;

            set
            {
                if (_autoCommit == value)
                {
                    return;
                }

                _autoCommit = value;

                if (_connection is null)
                {
                    return;
                }

                if (value)
                {
                    // Switching auto-commit on retires the explicit transaction. Committing rather than
                    // rolling back is the faithful choice: the legacy's own auto-commit checkpoint
                    // commits accumulated work rather than discarding it, and discarding a caller's
                    // writes because it changed a mode would be the more damaging of the two failures.
                    CommitAndRelease();
                }
                else
                {
                    // The outcome is deliberately DISCARDED on this re-begin: the caller's own mode
                    // change has already taken effect, so its answer must stand. An absent transaction
                    // stays visible to the next commit or rollback, which refuses rather than claiming
                    // success - see TryBeginTransaction.
                    _ = TryBeginTransaction(out _);
                }
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// ONLY THE DIALECT AND THE AUTO-COMMIT CHOICE ARE RETAINED, AND DELIBERATELY ONLY THOSE. The
        /// descriptor also carries the server, the database, the log identity, the log password, the
        /// connection parameters, the lock level and the user parameters. None of them has any meaning
        /// for a file-backed store whose path is owned by <see cref="SqliteConnectionFactory"/> and
        /// composed from configuration, so none is copied - which keeps the credential out of this
        /// object's state entirely rather than merely unused (constraint C-F). The two DBParm flags the
        /// task layer needs, bind-disabling and N-char binding, are read by the task layer from the
        /// descriptor itself and are not this engine's to interpret.
        /// </remarks>
        public void ApplyConnectionFields(in TransactionData descriptor)
        {
            Dbms = descriptor.Dbms;
            AutoCommit = descriptor.AutoCommit;

            // THE PRESENCE BIT, NOT THE CREDENTIAL. See CredentialRefusedText: a supplied password is
            // refused at connect rather than silently discarded, and answering the presence question
            // here is what lets the refusal happen without this object ever holding the value.
            _credentialSupplied = descriptor.HasCredential;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The pool has already disconnected any prior connection and fired its before-connect hook by
        /// the time this runs [<c>n_cst_thread_trans.sru:L115-L142</c>], so this member's whole job is
        /// to open the connection, apply the two URI extensions the legacy grammar carries, and report
        /// the outcome as a SqlState.
        /// </remarks>
        public SqlState Connect(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (TryObserveCancellation(cancellationToken, out SqlState cancelled))
            {
                return cancelled;
            }

            if (_credentialSupplied)
            {
                // BEFORE the already-connected arm and before anything is opened, so a credentialed
                // descriptor never reaches the provider on any path. Reported as an argument fault
                // because the descriptor is the argument that is wrong, and the caller can fix it.
                _logger.LogError(
                    "A transaction descriptor supplied a login password. Encrypted SQLite is out of "
                        + "scope for this phase, so the connection was refused rather than opened "
                        + "unprotected. No credential value is recorded.");

                return SqlState.Failed(RetCode.E_INVALID_ARGUMENT, CredentialRefusedText);
            }

            if (_connection is not null)
            {
                // Already connected. Reported as success with the connected row count rather than as a
                // fault, because the pool's own connect path treats an existing handle as a state to
                // unwind rather than as an error, and a failure here would turn a benign double connect
                // into a broken transaction.
                return SqlState.Succeeded(ConnectedRowCount);
            }

            SqliteConnection connection = new(_connections.ConnectionString);

            try
            {
                connection.Open();

                // The journal mode is part of the legacy URI grammar and defaults to DELETE
                // [w_test_sqlite.srw:L452-L455], so it is applied on every connection rather than once
                // per file - a journal pragma is per-connection for every mode except WAL.
                //
                // CONDITIONAL AND NON-FATAL. See ApplyJournalMode: the mode is read first and the pragma
                // is issued only when it differs, and a conversion the engine refuses is recorded rather
                // than allowed to fail the connect. Issuing it unconditionally and letting SQLITE_BUSY
                // propagate is what made a WAL-provisioned file plus one concurrent reader - which is the
                // ordinary state under a health probe - render the entire data plane unavailable.
                ApplyJournalMode(connection);

                // The integrity check is the grammar's optional third extension. It runs only when
                // configured, and its verdict is checked rather than discarded: a check that does not
                // answer `ok` is exactly the fault the option exists to surface.
                if (_connections.IntegrityCheck is { } check)
                {
                    string verdict = ScalarText(
                        connection,
                        SqliteConnectionFactory.IntegrityCheckStatementFor(check));

                    if (!string.Equals(verdict, "ok", StringComparison.OrdinalIgnoreCase))
                    {
                        connection.Dispose();

                        _logger.LogError(
                            "The configured SQLite integrity check did not pass, so the transaction "
                                + "object refused to connect. Check mode: {CheckMode}.",
                            check);

                        return SqlState.Failed(
                            RetCode.SQLITE_CORRUPT,
                            "The configured SQLite integrity check did not answer ok, so this "
                                + "connection was refused. Restore the database file from a known good "
                                + "copy; the verdict text is not reproduced here because it can quote "
                                + "page contents.");
                    }
                }
            }
            catch (SqliteException failure)
            {
                connection.Dispose();

                return Failed(failure, "connect");
            }
            catch (InvalidOperationException failure)
            {
                connection.Dispose();

                // 🔴 THE EXCEPTION OBJECT USED TO BE HERE, AND THIS IS THE WORST SITE IN THE SERVICE FOR
                // IT. A rejected connection string is the one fault whose message QUOTES THE CONNECTION
                // STRING - which on this service's own URI grammar carries the database path and, when
                // configured, the password [Data/SqliteConnectionFactory.cs]. Attaching the exception made
                // every provider render that message, its whole inner chain and the stack, so constraint
                // C-F was satisfied by the template and defeated by the argument beside it.
                _logger.LogError(
                    "The transaction object could not open its SQLite connection because the composed "
                        + "connection string was rejected. FaultTypes={FaultTypes} "
                        + "RedactedMessage={RedactedMessage}",
                    FaultRecord.Types(failure),
                    FaultRecord.RedactedMessages(failure));

                return SqlState.Failed(RetCode.SQLITE_CANTOPEN, failure.Message);
            }

            _connection = connection;

            if (!_autoCommit && !TryBeginTransaction(out SqlState beginFailure))
            {
                // THE CONNECT FAILS, AND THE CONNECTION IS RELEASED RATHER THAN LEFT HALF-OPEN. A caller
                // that asked for a non-auto-commit connection asked for a transaction; answering success
                // without one hands back a session whose first commit refuses for a reason that has
                // nothing to do with the statement the caller ran. The nearest legacy behaviour is a
                // CONNECT that left a non-zero status, which is E_DB_ERROR
                // [n_cst_thread_trans.sru:L129-L133] - the explicit BEGIN is a detail of this port, because
                // PowerBuilder's transaction is implicit after CONNECT and cannot fail separately.
                _connection = null;
                connection.Dispose();

                return beginFailure;
            }

            return SqlState.Succeeded(ConnectedRowCount);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// An open explicit transaction is ROLLED BACK rather than committed, which is the opposite of
        /// the auto-commit setter above and is right for the same reason stated from the other side: a
        /// disconnect is not a caller asking for its work to be kept, and committing uncommitted work on
        /// the way out would apply writes nobody asked to apply. Success is reported unconditionally,
        /// matching the legacy's tolerance of disconnecting something that never connected.
        /// </remarks>
        public SqlState Disconnect()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            RollbackAndRelease();

            // BEFORE the connection goes, not after: a retained command holds prepared statements over
            // this connection, and disposing the connection first would leave them dangling.
            ReleaseRetainedStatements();

            _connection?.Dispose();
            _connection = null;

            return SqlState.Succeeded();
        }

        /// <inheritdoc/>
        public SqlState Commit()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_connection is null)
            {
                // E_INVALID_TRANSACTION AND NOT SQLITE_MISUSE. The provider code vocabulary describes
                // what an ENGINE did wrong; this is a caller reaching a transaction that was never
                // connected, which is the legacy's own E_INVALID_TRANSACTION condition
                // [n_cst_thread_trans.sru:L113] and the same code the acquisition path answers for it
                // [n_cst_thread_task_sqlbase.sru:L177]. Answering a provider code would additionally be a
                // fabrication: no provider was reached, so no provider said anything.
                return SqlState.Failed(RetCode.E_INVALID_TRANSACTION, NotConnectedText);
            }

            if (_transaction is null)
            {
                // Under auto-commit every statement has already been applied, so there is nothing to
                // commit. Reported as a FAULT rather than as a no-op: the pool's own rollback arm
                // reports failure under auto-commit [:L185], and a commit that silently succeeded
                // without committing anything is the more dangerous of the two possible lies. The
                // provider code is right HERE and wrong above: a connection IS open, so the misuse is
                // the caller's use of it.
                return SqlState.Failed(RetCode.SQLITE_MISUSE, NoOpenTransactionText);
            }

            try
            {
                _transaction.Commit();
            }
            catch (SqliteException failure)
            {
                return Failed(failure, "commit");
            }
            finally
            {
                _transaction.Dispose();
                _transaction = null;
            }

            // A committed transaction object stays usable, so the next statement needs a fresh
            // transaction rather than an implicit one. Without this the object would silently drift into
            // auto-commit behaviour after its first commit.
            // The outcome is deliberately DISCARDED here: the COMMIT succeeded, and that is what this
            // method answers. See TryBeginTransaction for why the connect path treats it differently.
            _ = TryBeginTransaction(out _);

            return SqlState.Succeeded();
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Symmetric with <see cref="Commit"/>, including the auto-commit refusal, because the legacy is
        /// explicit that rollback under auto-commit REPORTS FAILURE and is not a harmless no-op
        /// [<c>n_cst_thread_trans.sru:L185</c>].
        /// </remarks>
        public SqlState Rollback()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_connection is null)
            {
                // AN IDEMPOTENT SUCCESS, NOT A FAULT, AND THE ASYMMETRY WITH COMMIT IS DELIBERATE. A
                // rollback is an UNWIND, and there is genuinely nothing to unwind on a transaction that
                // never connected - the state the caller asked for is the state it is already in. The
                // sibling disconnect answers the same way for the same reason, and the legacy tolerates
                // disconnecting an unconnected transaction. A commit is the opposite: it asks for work to
                // be MADE durable, and a success that made nothing durable is the more dangerous of the
                // two possible lies.
                return SqlState.Succeeded();
            }

            if (_transaction is null)
            {
                return SqlState.Failed(RetCode.SQLITE_MISUSE, NoOpenTransactionText);
            }

            try
            {
                _transaction.Rollback();
            }
            catch (SqliteException failure)
            {
                return Failed(failure, "rollback");
            }
            finally
            {
                _transaction.Dispose();
                _transaction = null;
            }

            // Discarded for the same reason as the commit path's - the ROLLBACK succeeded.
            _ = TryBeginTransaction(out _);

            return SqlState.Succeeded();
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <para>
        /// The statement is executed as supplied. That is the contract: the caller composed it, the
        /// caller owns its shape, and rewriting it here would change the observable generated statement
        /// the parity criterion is measured on.
        /// </para>
        /// <para>
        /// THE STATEMENT IS NEITHER STORED NOR LOGGED. It may carry interpolated literal values whenever
        /// the connection disabled bind variables, and this engine has no reason to retain it
        /// (constraint C-F). The row count travels back on the SqlState, which is what the ported
        /// command path reads.
        /// </para>
        /// </remarks>
        public SqlState Execute(string sqlCommand, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(sqlCommand);

            // FORWARDED RATHER THAN IMPLEMENTED TWICE. The legacy call shape is a single statement string,
            // and it stays supported - but it is expressed as a command whose canonical and rendered texts
            // are the same and which carries no parameter, so there is exactly ONE execution path and it is
            // the parameter-binding one. A second body here is how an implementation opts back into the
            // splice the bound overload exists to eliminate (AAP 0.6.4).
            return Execute(SqlCommandText.FromRenderedStatement(sqlCommand), cancellationToken);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <para>
        /// TYPED PROVIDER PARAMETERS, NOT SPLICED LITERALS. The canonical text carries the placeholders and
        /// the ordered values are bound to the provider command, so no caller-supplied value is ever
        /// concatenated into a statement. The rendered text travels with the command as a PARITY ARTEFACT
        /// for the SQL-preview and diagnostic channels only and is never executed (AAP 0.6.4, 0.7.2).
        /// </para>
        /// <para>
        /// MULTI-STATEMENT BATCHES ARE A REAL C-07 CAPABILITY, so the text is executed as the provider
        /// receives it and the affected-row total is the last statement's, which is what the legacy row
        /// count carries.
        /// </para>
        /// <para>
        /// THE RENDERED STATEMENT IS NEITHER STORED NOR LOGGED. It may carry interpolated literal values
        /// whenever the connection disabled bind variables, and this engine has no reason to retain it
        /// (constraint C-F). The CANONICAL text is retained when - and only when - the caller selected
        /// the prepared-form mode, and it is the placeholder form, so it carries no caller value.
        /// </para>
        /// <para>
        /// <b>THE PREPARED-FORM MODE IS HONOURED HERE, AND THIS IS THE ONLY PLACE IT IS.</b>
        /// <see cref="SqlCommandText.CacheStatement"/> is the port of the leading-<c>@</c> execution mode
        /// [<c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L397-L398</c>]. When it is set the command
        /// object is kept alive across executions rather than disposed, so its prepared statements are
        /// reused and the provider is not asked to parse the text again; the values are re-bound each
        /// time, which is what makes one retained entry serve the legacy demonstration's ten iterations.
        /// The OUTCOME is identical either way - the mode changes the parse count and nothing a caller
        /// observes in the result - which is exactly why the ordinary path is left untouched below.
        /// </para>
        /// </remarks>
        public SqlState Execute(in SqlCommandText command, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (TryObserveCancellation(cancellationToken, out SqlState cancelled))
            {
                return cancelled;
            }

            if (_connection is null)
            {
                // E_INVALID_TRANSACTION AND NOT SQLITE_MISUSE. The provider code vocabulary describes
                // what an ENGINE did wrong; this is a caller reaching a transaction that was never
                // connected, which is the legacy's own E_INVALID_TRANSACTION condition
                // [n_cst_thread_trans.sru:L113] and the same code the acquisition path answers for it
                // [n_cst_thread_task_sqlbase.sru:L177]. Answering a provider code would additionally be a
                // fabrication: no provider was reached, so no provider said anything.
                return SqlState.Failed(RetCode.E_INVALID_TRANSACTION, NotConnectedText);
            }

            return command.CacheStatement
                ? ExecuteRetained(in command)
                : ExecuteImmediate(in command);
        }

        /// <summary>
        /// Executes a statement and disposes the command afterwards - the ordinary mode.
        /// </summary>
        /// <param name="command">The command. Its canonical text is neither empty nor null.</param>
        /// <returns>The five state values the statement left behind.</returns>
        /// <remarks>
        /// Unchanged from the single path this class had before the prepared-form mode existed, and
        /// extracted rather than rewritten precisely so that a reader can see the mode added an arm and
        /// altered nothing on this one.
        /// </remarks>
        private SqlState ExecuteImmediate(in SqlCommandText command)
        {
            try
            {
                using SqliteCommand statement = _connection!.CreateCommand();
                statement.CommandText = command.CanonicalText;
                statement.Transaction = _transaction;

                command.BindTo(statement);

                return SqlState.Succeeded(AffectedRows(statement.ExecuteNonQuery()));
            }
            catch (SqliteException failure)
            {
                return Failed(failure, "statement execution");
            }
            catch (InvalidOperationException failure)
            {
                return BindFaulted(failure, "statement execution");
            }
        }

        /// <summary>
        /// Executes a statement through the retained prepared form, retaining it if it is new.
        /// </summary>
        /// <param name="command">The command. Its canonical text is neither empty nor null.</param>
        /// <returns>The five state values the statement left behind.</returns>
        /// <remarks>
        /// <para>
        /// <b>THE AMBIENT TRANSACTION IS RE-ASSIGNED ON EVERY EXECUTION, AND THAT IS NOT DEFENSIVE
        /// CODING.</b> A retained command outlives commits and rollbacks, and each of those DISPOSES the
        /// explicit transaction and opens a fresh one - so the reference the command was created with
        /// goes stale. <see cref="SqliteCommand"/> refuses to execute when its transaction is not the
        /// connection's current one, so a command retained across a commit would start failing with a
        /// transaction-mismatch fault that has nothing to do with the caller's statement.
        /// </para>
        /// <para>
        /// <b>THE VALUES ARE CLEARED AND RE-BOUND RATHER THAN OVERWRITTEN IN PLACE.</b> Re-binding is
        /// what lets one retained entry serve every iteration of the legacy's loop, and clearing first
        /// means the entry cannot accumulate a parameter from a previous execution - which would bind a
        /// stale value into a statement whose placeholder count happened to be smaller.
        /// </para>
        /// <para>
        /// <b>A FAILURE RELEASES THE ENTRY INSTEAD OF RETAINING IT.</b> A statement the provider refused
        /// has no useful prepared form, and retaining one would mean a malformed statement occupied a
        /// slot in a bounded store for the life of the connection. A statement that already matched an
        /// entry keeps it: the failure there is about the values, not the text.
        /// </para>
        /// </remarks>
        private SqlState ExecuteRetained(in SqlCommandText command)
        {
            Dictionary<string, RetainedStatement> retained =
                _preparedStatements ??= new Dictionary<string, RetainedStatement>(StringComparer.Ordinal);

            bool matched = retained.TryGetValue(command.CanonicalText, out RetainedStatement? entry);

            if (matched)
            {
                _preparedStatementHits++;
            }
            else
            {
                _preparedStatementMisses++;

                // Released BEFORE the new entry is built, so the store is never momentarily over its
                // bound and the victim is chosen without the newcomer competing to be it.
                EvictLeastRecentlyMatchedStatement(retained);

                SqliteCommand created = _connection!.CreateCommand();
                created.CommandText = command.CanonicalText;

                entry = new RetainedStatement(created);
            }

            SqliteCommand statement = entry!.Command;

            try
            {
                statement.Transaction = _transaction;

                statement.Parameters.Clear();
                command.BindTo(statement);

                SqlState state = SqlState.Succeeded(AffectedRows(statement.ExecuteNonQuery()));

                entry.Stamp = ++_preparedStatementStamp;
                retained[command.CanonicalText] = entry;

                return state;
            }
            catch (SqliteException failure)
            {
                if (!matched)
                {
                    // Never entered the store, so there is nothing to remove - only the command this
                    // method created and the caller will never see.
                    statement.Dispose();
                }

                return Failed(failure, "statement execution");
            }
            catch (InvalidOperationException failure)
            {
                if (!matched)
                {
                    statement.Dispose();
                }

                return BindFaulted(failure, "statement execution");
            }
        }

        /// <summary>
        /// Releases the least recently matched retained statement when the store is at its bound.
        /// </summary>
        /// <param name="retained">The store.</param>
        /// <remarks>
        /// <para>
        /// O(n) over a population bounded by <see cref="PreparedStatementCapacityLimit"/>, which is the
        /// right trade at this size: a linked list threaded through the dictionary would make eviction
        /// constant-time and make every other member of this class harder to read, for a saving on a
        /// scan of a few dozen entries that happens only when the store is full and the statement is new.
        /// </para>
        /// <para>
        /// <b>LEAST RECENTLY MATCHED, NOT OLDEST.</b> The stamp is refreshed on every match, so a
        /// statement a caller keeps re-executing is never the victim however long ago it was first
        /// retained - which is the population the mode exists to serve.
        /// </para>
        /// </remarks>
        private static void EvictLeastRecentlyMatchedStatement(Dictionary<string, RetainedStatement> retained)
        {
            if (retained.Count < PreparedStatementCapacityLimit)
            {
                return;
            }

            string? victim = null;
            long oldest = long.MaxValue;

            foreach (KeyValuePair<string, RetainedStatement> candidate in retained)
            {
                if (candidate.Value.Stamp < oldest)
                {
                    oldest = candidate.Value.Stamp;
                    victim = candidate.Key;
                }
            }

            if (victim is null)
            {
                return;
            }

            retained[victim].Command.Dispose();
            _ = retained.Remove(victim);
        }

        /// <summary>
        /// Releases every retained statement and forgets the store.
        /// </summary>
        /// <remarks>
        /// <b>CALLED FROM BOTH CONNECTION-CLOSING PATHS, AND IT HAS TO BE.</b> A prepared statement is
        /// prepared against a connection; disposing the connection while a command still holds prepared
        /// statements over it is a use-after-free at the provider layer. The counters are deliberately
        /// NOT reset - they describe what this engine did over its whole life, and zeroing them on a
        /// reconnect would hide the mode's history from the only observer of it.
        /// </remarks>
        private void ReleaseRetainedStatements()
        {
            if (_preparedStatements is null)
            {
                return;
            }

            foreach (RetainedStatement entry in _preparedStatements.Values)
            {
                entry.Command.Dispose();
            }

            _preparedStatements = null;
        }

        /// <inheritdoc/>
        public int PreparedStatementCount => _preparedStatements?.Count ?? 0;

        /// <inheritdoc/>
        public long PreparedStatementHits => _preparedStatementHits;

        /// <inheritdoc/>
        public long PreparedStatementMisses => _preparedStatementMisses;

        /// <inheritdoc/>
        public int PreparedStatementCapacity => PreparedStatementCapacityLimit;

        /// <summary>
        /// One statement held in prepared form, with the stamp the eviction arm orders by.
        /// </summary>
        /// <param name="command">The command whose prepared statements are being retained.</param>
        /// <remarks>
        /// A CLASS RATHER THAN A RECORD STRUCT, because <see cref="Stamp"/> is mutated in place on every
        /// match and a struct in a dictionary would have to be read out, copied, and written back - three
        /// steps where one of them can be forgotten.
        /// </remarks>
        private sealed class RetainedStatement(SqliteCommand command)
        {
            /// <summary>The retained command. Disposed by the store, never by a caller.</summary>
            internal SqliteCommand Command { get; } = command;

            /// <summary>When this entry was last matched or first retained.</summary>
            internal long Stamp { get; set; }
        }

        /// <summary>
        /// Reports the caller's cancellation as a state value rather than as an exception.
        /// </summary>
        /// <param name="cancellationToken">The caller's cancellation.</param>
        /// <param name="cancelled">The cancelled state, when it was requested.</param>
        /// <returns><see langword="true"/> when cancellation was requested.</returns>
        /// <remarks>
        /// A STATE VALUE AND NOT A THROW, because every member of this surface reports through
        /// <see cref="SqlState"/> and the legacy return algebra distinguishes cancelled from failed - a
        /// cancellation is neither succeeded nor failed [<c>isfailed.srf:L11-L13</c>]. Throwing here would
        /// collapse that distinction at the one boundary that has to preserve it.
        /// </remarks>
        private static bool TryObserveCancellation(
            CancellationToken cancellationToken,
            out SqlState cancelled)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = SqlState.Failed(RetCode.CANCELLED, CancelledText);

                return true;
            }

            cancelled = default;

            return false;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// THE AMBIENT TRANSACTION IS ASSIGNED, AND THAT IS THE WHOLE POINT OF THE CAPABILITY. A command
        /// created without it would run outside the caller's transaction, so an uncommitted write would be
        /// invisible to a read in the same session and a rollback would not undo it.
        /// </remarks>
        /// <inheritdoc/>
        /// <remarks>
        /// EXACTLY THE PRECONDITION <see cref="CreateCommand"/> ENFORCES, restated as a question rather
        /// than duplicated as a rule: a disposed engine and an engine that never connected both answer
        /// <see langword="false"/>, and nothing else here can make <see cref="CreateCommand"/> refuse. No
        /// statement is sent and no hook is raised, so a consumer may ask on every call.
        /// </remarks>
        public bool CanCreateCommand => !_disposed && _connection is not null;

        /// <inheritdoc/>
        public SqliteCommand CreateCommand()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_connection is null)
            {
                throw new InvalidOperationException(NotConnectedText);
            }

            SqliteCommand command = _connection.CreateCommand();
            command.Transaction = _transaction;

            return command;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Disconnects first, so the connection is released deterministically rather than at the mercy
        /// of a finaliser. The pool disposes every engine it creates, so this is the normal path rather
        /// than an exceptional one.
        /// </remarks>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            RollbackAndRelease();

            // BEFORE the connection goes, for the reason stated on Disconnect.
            ReleaseRetainedStatements();

            _connection?.Dispose();
            _connection = null;
            _disposed = true;
        }

        /// <summary>
        /// Runs a statement that returns nothing, and lets a provider fault propagate to the caller.
        /// </summary>
        /// <param name="connection">The open connection.</param>
        /// <param name="statement">The statement text.</param>
        private static void Execute(SqliteConnection connection, string statement)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = statement;
            _ = command.ExecuteNonQuery();
        }

        /// <summary>
        /// Applies the configured journal mode to a freshly opened connection, skipping the pragma when
        /// the file is already in that mode and tolerating a conversion the engine refuses.
        /// </summary>
        /// <param name="connection">The open connection.</param>
        /// <remarks>
        /// <para>
        /// <b>THREE STEPS, AND THE FIRST ONE IS WHY THIS METHOD EXISTS.</b> The mode in force is READ
        /// first, so the setter is issued only when it genuinely differs - which is never, on the
        /// overwhelmingly common path where every connection to a file agrees about its journal mode. The
        /// setter's own answer is then CHECKED, because the pragma returns the mode actually in force and
        /// therefore reports a refusal by answering the old value rather than by failing. Finally a
        /// provider fault on the conversion is caught and recorded.
        /// </para>
        /// <para>
        /// <b>WHY A REFUSAL IS NOT FATAL HERE.</b> Converting out of WAL needs an EXCLUSIVE lock, so the
        /// pragma answers <c>SQLITE_BUSY</c> whenever another connection is open on the file - and a
        /// service with an anonymous readiness probe and a pooling provider routinely has one. Letting
        /// that fail the connect made every session on a WAL-provisioned database answer
        /// <see cref="RetCode.E_INVALID_TRANSACTION"/> permanently, which is a total outage caused by a
        /// setting no caller can observe: the journal mode changes how the engine journals, not the result
        /// of any statement. The connection is therefore kept and the shortfall is logged with the
        /// operator's remedy. An integrity check, by contrast, exists precisely to refuse a damaged file
        /// and stays fatal - see <see cref="Connect"/>.
        /// </para>
        /// </remarks>
        private void ApplyJournalMode(SqliteConnection connection)
        {
            string configured = _connections.JournalMode;

            string inForce = ScalarText(connection, SqliteConnectionFactory.JournalModeProbeStatement);

            if (string.Equals(inForce, configured, StringComparison.OrdinalIgnoreCase))
            {
                // Nothing to do, and issuing the pragma anyway is exactly the operation that can fail.
                return;
            }

            string resulting;

            try
            {
                // The pragma ANSWERS the resulting mode, so the setter is run as a scalar read rather than
                // as a non-query - that answer is the only way a silent refusal becomes visible.
                resulting = ScalarText(
                    connection,
                    SqliteConnectionFactory.JournalStatementFor(configured));
            }
            catch (SqliteException failure)
            {
                // The exception object is not attached: the pragma is a generated statement and a
                // provider fault on one carries the statement in its own message.
                _logger.LogWarning(
                    "The SQLite journal mode stayed {JournalModeInForce} instead of the configured "
                        + "{ConfiguredJournalMode}. {Explanation} FaultTypes={FaultTypes} "
                        + "RedactedMessage={RedactedMessage}",
                    inForce,
                    configured,
                    SqliteConnectionFactory.JournalModeNotConvertedText,
                    FaultRecord.Types(failure),
                    FaultRecord.RedactedMessages(failure));

                return;
            }

            if (string.Equals(resulting, configured, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _logger.LogWarning(
                "The SQLite journal mode stayed {JournalModeInForce} instead of the configured "
                    + "{ConfiguredJournalMode}. {Explanation}",
                resulting.Length == 0 ? inForce : resulting,
                configured,
                SqliteConnectionFactory.JournalModeNotConvertedText);
        }

        /// <summary>
        /// Runs a statement and returns its first column of its first row as text.
        /// </summary>
        /// <param name="connection">The open connection.</param>
        /// <param name="statement">The statement text.</param>
        /// <returns>The scalar as text, or the empty string when the statement returned nothing.</returns>
        private static string ScalarText(SqliteConnection connection, string statement)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = statement;

            return command.ExecuteScalar()?.ToString() ?? string.Empty;
        }

        /// <summary>Opens an explicit transaction on the connection, if one is not already open.</summary>
        /// <param name="failure">Receives the provider's own fault when the begin could not be issued.</param>
        /// <returns><see langword="true"/> when a transaction is open on return.</returns>
        /// <remarks>
        /// <para>
        /// <b>DEFERRED, AND THAT IS A FIDELITY DECISION RATHER THAN A TUNING ONE.</b> The provider's
        /// parameterless <c>BeginTransaction</c> issues <c>BEGIN IMMEDIATE</c> for its default
        /// <see cref="System.Data.IsolationLevel.Serializable"/> level, which takes SQLite's single WRITE
        /// LOCK at begin time - so merely CONNECTING a second session would fail with "database is locked"
        /// while any other session held an open write transaction. PowerBuilder's transaction is implicit
        /// after <c>CONNECT</c> and takes no lock until a statement writes, so an eager write lock here
        /// would invent a failure the oracle does not have: two transaction objects on one database can
        /// both connect, and only the second WRITE conflicts.
        /// </para>
        /// <para>
        /// Measured against the shipped provider in both DELETE and WAL journal modes, with another
        /// connection holding the write slot: the immediate form fails at BEGIN; the deferred form begins,
        /// READS normally, and fails at the WRITE - which is the legacy's own shape. The isolation level is
        /// unchanged, because SQLite takes its read lock at the first read under either form.
        /// </para>
        /// </remarks>
        private bool TryBeginTransaction(out SqlState failure)
        {
            failure = SqlState.Succeeded();

            if (_connection is null || _transaction is not null)
            {
                return true;
            }

            try
            {
                _transaction = _connection.BeginTransaction(
                    System.Data.IsolationLevel.Serializable,
                    deferred: true);

                return true;
            }
            catch (SqliteException failure_)
            {
                // Recorded rather than thrown, because every caller of this helper is on a path whose
                // outcome is already being reported as a SqlState and an exception here would escape
                // that channel.
                //
                // ⚠ THE OUTCOME IS NOW REPORTED AS WELL AS LOGGED, AND THE TWO CALLERS TREAT IT
                // DIFFERENTLY ON PURPOSE. On the CONNECT path a caller is asking for a usable
                // transaction, so a failure there must fail the connect: reporting success for a session
                // that holds no transaction told the caller it had something it did not have, and the
                // fault only surfaced later at a commit that refused for a reason the caller could not
                // relate to its own request. On the RE-BEGIN paths - after a commit, after a rollback -
                // the caller's own operation has already SUCCEEDED, so its answer must stand; there the
                // absent transaction stays visible to the NEXT commit or rollback, which refuses with
                // NoOpenTransactionText rather than claiming success.
                // Described rather than attached - see Errors/FaultRecord.cs. A BEGIN that the provider
                // refuses reports its own statement text, and the redactor is the only route by which any
                // statement reaches a record in this service.
                _logger.LogError(
                    "The transaction object could not open an explicit SQLite transaction. "
                        + "FaultTypes={FaultTypes} RedactedMessage={RedactedMessage}",
                    FaultRecord.Types(failure_),
                    FaultRecord.RedactedMessages(failure_));

                failure = Failed(failure_, "begin transaction");

                return false;
            }
        }

        /// <summary>Commits and releases the explicit transaction, ignoring the absence of one.</summary>
        private void CommitAndRelease()
        {
            if (_transaction is null)
            {
                return;
            }

            try
            {
                _transaction.Commit();
            }
            catch (SqliteException failure)
            {
                _logger.LogError(
                    "The transaction object could not commit its explicit SQLite transaction while "
                        + "switching to auto-commit, so the accumulated work was not applied. "
                        + "FaultTypes={FaultTypes} RedactedMessage={RedactedMessage}",
                    FaultRecord.Types(failure),
                    FaultRecord.RedactedMessages(failure));
            }
            finally
            {
                _transaction.Dispose();
                _transaction = null;
            }
        }

        /// <summary>Rolls back and releases the explicit transaction, ignoring the absence of one.</summary>
        private void RollbackAndRelease()
        {
            if (_transaction is null)
            {
                return;
            }

            try
            {
                _transaction.Rollback();
            }
            catch (SqliteException failure)
            {
                // An unwind that cannot unwind is logged and swallowed: every caller is already on a
                // teardown path, and throwing would turn a released connection into a leaked one.
                _logger.LogWarning(
                    "The transaction object could not roll back its explicit SQLite transaction while "
                        + "releasing the connection. FaultTypes={FaultTypes} "
                        + "RedactedMessage={RedactedMessage}",
                    FaultRecord.Types(failure),
                    FaultRecord.RedactedMessages(failure));
            }
            finally
            {
                _transaction.Dispose();
                _transaction = null;
            }
        }

        /// <summary>
        /// Normalises the provider's affected-row answer onto the legacy <c>SQLNRows</c> domain.
        /// </summary>
        /// <param name="reported">Whatever the provider's non-query execution answered.</param>
        /// <returns>The count, with the provider's negative non-DML sentinel folded to zero.</returns>
        /// <remarks>
        /// <para>
        /// <b>ADO.NET's <c>ExecuteNonQuery</c> ANSWERS <c>-1</c> FOR A STATEMENT THAT IS NOT DML</b> - a
        /// <c>SELECT</c>, a statement made only of comments, a no-op. That is an ADO.NET convention, not a
        /// SQLite one and not a legacy one: <c>SQLNRows</c> is "the number of rows affected", it is never
        /// negative in PowerBuilder, and a caller branching on it has no arm for a negative value.
        /// Publishing <c>-1</c> would put a provider artefact on a legacy observable.
        /// </para>
        /// <para>
        /// <b>WHY THIS WAS PREVIOUSLY INVISIBLE, WHICH IS THE INTERESTING PART.</b> A commit used to
        /// replace the whole state and zero the count, so on the <c>AC_ON</c> arm the <c>-1</c> was erased
        /// along with every legitimate count. Preserving the count across a commit - which is the correct
        /// behaviour and is asserted separately - exposed the sentinel that erasure had been hiding. Both
        /// halves are needed: the count must survive the commit, and it must be a count.
        /// </para>
        /// <para>
        /// No legitimate DML answer is affected. A provider reports zero rows affected as <c>0</c>, so the
        /// only value folded here is the sentinel.
        /// </para>
        /// </remarks>
        private static int AffectedRows(int reported) => reported < 0 ? 0 : reported;

        /// <summary>
        /// Projects a provider fault onto a failed <see cref="SqlState"/>.
        /// </summary>
        /// <param name="failure">The provider fault.</param>
        /// <param name="operation">The verb that faulted, for the log record only.</param>
        /// <returns>The failed state, carrying the provider's own mapped code and message.</returns>
        /// <remarks>
        /// The code goes through <see cref="SqliteConnectionFactory.MapSqliteResultCode"/> so that the
        /// extended result code wins when the provider supplies one, which is the same projection the
        /// connection factory applies - one mapping for the whole service rather than two that could
        /// disagree about the same fault.
        /// </remarks>
        private SqlState Failed(SqliteException failure, string operation)
        {
            long code = SqliteConnectionFactory.MapSqliteResultCode(
                failure.SqliteErrorCode,
                failure.SqliteExtendedErrorCode);

            // 🔴 THE HIGHEST-TRAFFIC SITE OF THE SIX, because every connect and every statement fault
            // in this engine funnels through here. The provider's message is the driver envelope around
            // its own diagnosis - `SQLite Error 19: '<text>'.` - and on a statement fault the interior is
            // the statement with its literal values interpolated. The mapped code is published in the
            // clear because it is already on the wire as DbError.sqldbcode, so preserving it discloses
            // nothing the caller does not already receive.
            _logger.LogError(
                "The transaction object's SQLite {Operation} failed with mapped code {ResultCode}. "
                    + "FaultTypes={FaultTypes} RedactedMessage={RedactedMessage}",
                operation,
                code,
                FaultRecord.Types(failure),
                FaultRecord.RedactedMessages(failure));

            return SqlState.Failed(code, failure.Message);
        }

        /// <summary>
        /// Projects a provider BIND fault onto a failed <see cref="SqlState"/>.
        /// </summary>
        /// <param name="failure">The provider fault.</param>
        /// <param name="operation">The verb that faulted, for the log record only.</param>
        /// <returns>
        /// The failed state, carrying <see cref="RetCode.SQLITE_MISUSE"/> and the provider's message.
        /// </returns>
        /// <remarks>
        /// <para>
        /// <b>WHY A SECOND PROJECTION EXISTS.</b> <c>Microsoft.Data.Sqlite</c> raises
        /// <see cref="InvalidOperationException"/> - NOT <see cref="SqliteException"/> - when a statement
        /// carries a parameter marker for which no value was supplied, because the refusal happens in the
        /// provider before SQLite is asked to step anything. That exception type was previously uncaught
        /// on both execution paths, so it escaped the whole task layer and surfaced as an UNHANDLED gRPC
        /// fault carrying no defined code - the one outcome AAP §0.1.5 forbids, since a contract must be
        /// "narrowed with a defined error, never widened with a guess".
        /// </para>
        /// <para>
        /// <b>WHY <see cref="RetCode.SQLITE_MISUSE"/> AND NOT A SENTINEL.</b> Twenty-one is SQLite's own
        /// result code for "library used incorrectly", which is exactly what executing a statement with an
        /// unsupplied parameter is; it is already declared in the shared kernel's <c>SQLITE_*</c> set, so
        /// the value is a real code from the same vocabulary every other <c>SqlDbCode</c> on this engine
        /// comes from rather than a private marker a reader would have to look up.
        /// </para>
        /// <para>
        /// <b>THIS IS THE BACKSTOP, NOT THE FIX.</b> The statement forms C-07 publishes are refused with
        /// the contract's own binding code BEFORE execution - see
        /// <c>Tasks/SqlCommandTask.OnDoTask</c>'s unbound-marker guard and
        /// <c>SqlTaskBase.BindParams</c>'s unmatched-placeholder check. This projection exists so that any
        /// shape those two do not anticipate still produces a defined status instead of a fault.
        /// </para>
        /// </remarks>
        private SqlState BindFaulted(InvalidOperationException failure, string operation)
        {
            // C-F: the exception message may name a parameter, never a value, and the STATEMENT is not
            // included in this record - the only route a statement text ever takes to a log is the
            // sanctioned redactor, which the task layer applies.
            _logger.LogError(
                "The transaction object's SQLite {Operation} was refused by the provider before reaching "
                    + "the engine, which is the shape an unsupplied statement parameter takes. "
                    + "FaultTypes={FaultTypes} RedactedMessage={RedactedMessage}",
                operation,
                FaultRecord.Types(failure),
                FaultRecord.RedactedMessages(failure));

            return SqlState.Failed(RetCode.SQLITE_MISUSE, failure.Message);
        }
    }
}
