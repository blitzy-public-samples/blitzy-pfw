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
    internal sealed class SqliteTransactionEngine : ITransactionEngine, ISqliteCommandSource
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
                    BeginTransaction();
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
                Execute(connection, SqliteConnectionFactory.JournalStatementFor(_connections.JournalMode));

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

                _logger.LogError(
                    failure,
                    "The transaction object could not open its SQLite connection because the composed "
                        + "connection string was rejected.");

                return SqlState.Failed(RetCode.SQLITE_CANTOPEN, failure.Message);
            }

            _connection = connection;

            if (!_autoCommit)
            {
                BeginTransaction();
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
            BeginTransaction();

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

            BeginTransaction();

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
        /// THE STATEMENT IS NEITHER STORED NOR LOGGED. It may carry interpolated literal values whenever
        /// the connection disabled bind variables, and this engine has no reason to retain it
        /// (constraint C-F).
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

            try
            {
                using SqliteCommand statement = _connection.CreateCommand();
                statement.CommandText = command.CanonicalText;
                statement.Transaction = _transaction;

                command.BindTo(statement);

                return SqlState.Succeeded(statement.ExecuteNonQuery());
            }
            catch (SqliteException failure)
            {
                return Failed(failure, "statement execution");
            }
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
        private void BeginTransaction()
        {
            if (_connection is null || _transaction is not null)
            {
                return;
            }

            try
            {
                _transaction = _connection.BeginTransaction();
            }
            catch (SqliteException failure)
            {
                // Recorded rather than thrown, because every caller of this helper is on a path whose
                // outcome is already being reported as a SqlState and an exception here would escape
                // that channel. The absent transaction is then visible to the next commit or rollback,
                // which refuses with NoOpenTransactionText rather than claiming success.
                _logger.LogError(
                    failure,
                    "The transaction object could not open an explicit SQLite transaction, so the next "
                        + "commit or rollback will refuse rather than report success.");
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
                    failure,
                    "The transaction object could not commit its explicit SQLite transaction while "
                        + "switching to auto-commit, so the accumulated work was not applied.");
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
                    failure,
                    "The transaction object could not roll back its explicit SQLite transaction while "
                        + "releasing the connection.");
            }
            finally
            {
                _transaction.Dispose();
                _transaction = null;
            }
        }

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

            _logger.LogError(
                failure,
                "The transaction object's SQLite {Operation} failed with mapped code {ResultCode}.",
                operation,
                code);

            return SqlState.Failed(code, failure.Message);
        }
    }
}
