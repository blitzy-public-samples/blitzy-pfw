// ==================================================================================================
//  SqliteConnectionFactory.cs - THE ONLY PLACE IN THE SYSTEM THAT OPENS A STORAGE CONNECTION
//  ------------------------------------------------------------------------------------------------
//  ROLE
//  The managed substitute for the closed-source PBNI connection facade
//  ws_objects/pfw.utility.sqlite.pbl.src/n_sqlite.sru, declared there as
//  `global type n_sqlite from nonvisualobject native "pfw.dll"` [:L8]. Persistence is the only
//  service that generates or executes SQL and the only one holding a storage provider; this file is
//  the single seam through which that provider is reached. No other file in the repository opens a
//  connection, composes a connection string or names a data file.
//
//  LEGACY SOURCES - READ AS SPECIFICATION, NEVER EDITED (constraint C-C)
//    ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw            L448-L474, the primary specification
//    ws_objects/pfw.utility.sqlite.pbl.src/n_sqlite.sru        the facade surface substituted here
//    ws_objects/pfw.utility.sqlite.pbl.src/n_sqliterecordset.sru  REFERENCE ONLY - see the omissions
//    ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru  L60-L61, the two database types
//
//  THE SPECIFICATION, LINE BY LINE, AT w_test_sqlite.srw
//    L448  if sqlitedb.IsOpened() then return        the idempotent-open guard, reproduced
//    L450  FileDelete("test.db")                     TEST-HARNESS SETUP - DELIBERATELY NOT PORTED
//    L452  //URI protocol reference sqlite.org/uri.html
//    L453  //extension parameters:                   the legacy's OWN word for them - see below
//    L454  //- check[=quick]                         PRAGMA integrity_check, `quick` selects quick_check
//    L455  //- journal[=DELETE|TRUNCATE|PERSIST|MEMORY|WAL|OFF]   journal mode, DEFAULT `DELETE`
//    L456  Open("test.db?mode=rwc"/*[,password]*/)   the grammar, and the SECOND-ARGUMENT marker
//    L461  SetAutoCommit(true)                       immediately after a successful open
//    L463  CREATE TABLE IF NOT EXISTS COMPANY(...)   the only DDL in the repository
//    L473  SetAutoCommit(false)                      after the DDL
//  and the window's own teardown at L115, `event close;sqlitedb.Close()`, plus the DISCONNECT
//  button at L430 which is the second call site of the same verb.
//
//  ================================================================================================
//  TWO RULINGS THAT REVERSE WHAT AN EXPERIENCED .NET DEVELOPER WOULD DO BY DEFAULT
//  ================================================================================================
//
//  RULING 1 - THE PASSWORD IS A SEPARATE ARGUMENT, NOT URI SYNTAX, AND A SUPPLIED ONE FAILS FAST.
//  n_sqlite declares TWO distinct overloads, `Open(readonly string uri)` [:L17] and
//  `Open(readonly string uri, readonly string password)` [:L18]. The inline `/*[,password]*/` at
//  w_test_sqlite.srw:L456 is a PowerScript block comment marking the position of that second
//  argument - it is NOT a URI grammar. So `,password` is never emitted into a URI here, `password=`
//  is never emitted as a URI query parameter, and SqliteConnectionStringBuilder.Password is never
//  set. A DIRECT AND VALUABLE CONSEQUENCE: because no credential ever enters either string, the
//  composed URI and the composed connection string are both SAFE TO LOG and safe to carry in a
//  diagnostic. That property is what lets this file report what it opened at all.
//
//  And when a password IS configured, this file refuses to start rather than ignoring it. The
//  pinned native is SQLitePCLRaw.bundle_e_sqlite3 3.0.5 - the PLAIN, NON-CIPHER bundle - and
//  encrypted SQLite is explicitly out of Phase-1 scope: the cipher-enabled library shipped in the
//  repository is materially older than the plain one and its key-derivation and per-page integrity
//  options are not reachable through any framework API, so there is no evidence of the settings an
//  existing encrypted file was created with and the current provider cannot reproduce that page
//  format. Silently ignoring a supplied password would therefore be a SECURITY-RELEVANT SILENT
//  FAILURE: an operator would believe the database is encrypted when it is plaintext on disk. The
//  migration's own transformation rule applies exactly here - where a legacy behaviour cannot be
//  reproduced across the boundary the contract is NARROWED WITH A DEFINED ERROR, never widened with
//  a guess - joined to the framework's fail-fast posture, where a structural fault ends the process
//  rather than degrading past it [ws_objects/pfw.pbl.src/pfw.sra:L111-L144]. The refusal names the
//  configuration key and the reason and NEVER echoes the value.
//
//  RULING 2 - `check` AND `journal` ARE NOT SQLITE URI PARAMETERS. THEY BECOME PRAGMAS.
//  This is the subtler of the two and the easier to get silently wrong. The legacy comment at
//  w_test_sqlite.srw:L453 calls them "extension parameters", and that is literally what they are:
//  pfw.dll's own additions, parsed by the closed binary and never by SQLite. SQLite SILENTLY
//  IGNORES query parameters it does not recognise. Pass `journal=DELETE` or `check=quick` through
//  to the engine inside a data source and they are quietly dropped - the connection opens perfectly,
//  and it has neither the journal mode nor the integrity check the configuration asked for. Nothing
//  errors and no test would obviously catch it.
//
//  So the two artefacts are deliberately kept SEPARATE and are NOT the same thing:
//    * The composed legacy-form URI is the PARITY AND DIAGNOSTIC ARTEFACT. It is what
//      n_sqlite.Open() received, it is what the matrix tests assert on, and it is what this file
//      logs and reports. It is NEVER handed to SQLite verbatim.
//    * The real connection string is built with SqliteConnectionStringBuilder, and the two
//      extension parameters are applied AFTER opening as PRAGMAs, whose results are then VERIFIED.
//  `PRAGMA journal_mode` returns the resulting mode and returns the OLD mode when the requested one
//  cannot be set, so its return value is checked rather than assumed; a mismatch is surfaced as a
//  structured error. `PRAGMA integrity_check` / `quick_check` exist precisely to verify the data
//  file is not corrupt, so a result other than `ok` is surfaced as a structured failure and is
//  never logged and shrugged off.
//
//  ================================================================================================
//  NEVER DELETE, NEVER RESEED - THE MOST CONSEQUENTIAL RULE IN THIS FOLDER (constraints C-L, C-B)
//  ================================================================================================
//  w_test_sqlite.srw:L450 performs `FileDelete("test.db")` three lines above the URI grammar ported
//  here, inside the same event script, where it reads like part of the open sequence. IT IS THE TEST
//  HARNESS'S SETUP, NOT FRAMEWORK BEHAVIOUR, and reproducing it in a service would be catastrophic.
//
//  The reason is the parity model, not tidiness. For a given workflow identifier the legacy-side and
//  target-side characterization recordings must be captured against the SAME `persistence-db` volume
//  state, with the volume neither recreated nor reseeded between them, or the paired recordings are
//  not comparable at all. A delete on startup would destroy that guarantee on every single restart,
//  silently, while every unit test still passed.
//
//  Therefore, nowhere in this file: no File.Delete, no Database.EnsureDeleted, no EnsureCreated, no
//  drop-and-recreate, no DROP TABLE, no TRUNCATE, no reseed, no schema mutation of any kind. The
//  legacy `test.db` at the repository root is never targeted, never opened and never deleted; the
//  file this factory opens lives under the configured data directory and nowhere else.
//
//  EXACTLY ONE FILESYSTEM MUTATION IS PERMITTED, and it is Directory.CreateDirectory on the
//  configured data directory: idempotent, additive, non-destructive, and genuinely required because
//  `mode=rwc` creates the database FILE but not a missing parent DIRECTORY. It lives in the impure
//  open path and never inside the pure composition function.
//
//  ================================================================================================
//  WHAT THIS FILE DELIBERATELY DOES NOT CONTAIN - each omission a constraint discharged (C-K)
//  ================================================================================================
//   * NO SQL SERVER AND NO ORACLE - no provider, no client package, no connection, no dialect
//     branch, not even a nullable field (C-E). The legacy transaction layer declares exactly two
//     database types, DBT_MSSQL = 0 and DBT_ORACLE = 1
//     [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L60-L61], resolved at [:L356-L361]
//     by a substring test on the DBMS string - and SQLITE IS ABSENT FROM THAT ENUMERATION ENTIRELY.
//     Neither dialect has a schema, a connection string or any DDL anywhere in the repository, so
//     provisioning either would fabricate a database the legacy never had. Their two behaviours
//     survive only in Sql/Paging/ as pure string transforms that need no instance of either engine.
//   * NO SQLCIPHER - no PRAGMA key, no rekey, no cipher setting, no key-derivation pragma, and no
//     cipher-enabled provider. See RULING 1 for the reason and for what happens instead.
//   * NO SHIPPED NATIVE BINARY. The repository-root sqlite3 and cipher-enabled libraries are
//     read-only legacy artefacts and are never loaded (C-C). The managed native arrives exclusively
//     through the SQLitePCLRaw.bundle_e_sqlite3 3.0.5 pin the project file already carries.
//   * NO EXTENSION LOADING. n_sqlite declares LoadExtension in two arities [:L20-L21] and neither is
//     reproduced. This is a DECISION, not an oversight (C-K): the member has no named consumer
//     anywhere in this refactor, and loading a native extension out of the repository is precisely
//     what C-C forbids. Should a consumer ever appear, it arrives with its own evidence.
//   * NO RECORDSET TYPE. n_sqliterecordset's 33 forward-only accessors are reference only; that
//     progressive-delivery shape is realised by the streaming query contract and its chunking, not
//     by a row cursor here.
//   * NO TRANSACTION VERBS. n_sqlite's AutoCommit() with its "SQLCode = 0 then COMMIT else ROLLBACK,
//     and a failed COMMIT also auto-ROLLBACKs" semantics [:L22], Commit([autorollback]) [:L23-L24]
//     and Rollback() [:L25] belong to Transactions/ and the transaction contract. This file owns the
//     connection-level STATE and its toggle, and stops there. See SetAutoCommitAsync for how the
//     boundary is held without either duplicating a verb or stranding a transaction.
//   * NO Exec, NO Query AND NO Update. n_sqlite's positional-binding command surface [:L32-L43] and
//     its datastore and DataWindow retrieval and update overloads [:L44-L87] are the command, query
//     and update contracts, implemented in Tasks/ and Grpc/. The only statements this file executes
//     are its own connection-level ones, every one of them a compile-time constant.
//   * NO ENDPOINT, ROUTE, LISTENER OR HANDLER (C-G). This file creates no boundary; it is in-process
//     only. The authenticated surfaces live in Endpoints/ and Grpc/.
//   * NOTHING FROM A DEFERRED CAPABILITY AREA (C-D). No XML or JSON handling, no HttpClient, no UI,
//     DPI or theming concern, no scripting and no dynamic invocation.
//   * NO Win32 HANDLE MARSHALLING. n_sqlite's SetCancelEvent takes an unsigned long that is a Win32
//     event HANDLE [:L11], which has no portable equivalent on a Linux container. Cancellation is
//     expressed as a CancellationToken threaded through every awaited member instead, which is the
//     migration's own mapping for the legacy concurrency primitives.
//   * NO RAW STATEMENT LOGGING AND NO STATEMENT IN AN ERROR PAYLOAD. Outbound statement masking
//     belongs to Errors/SqlRedactor.cs. Every DbErrorData this file raises is built through
//     DbErrorData.FromTransaction, which leaves SqlSyntax empty by construction, so no statement
//     this file executes can reach a log record, an error payload or a characterization recording.
//   * NO NEW PACKAGE REFERENCE (C-I). Microsoft.Data.Sqlite, the shared framework's options and
//     logging abstractions, and the BCL. Directory.Packages.props is untouched.
//   * NO STATIC MUTABLE STATE, NO SINGLETON FIELD AND NO STATIC CACHED CONNECTION. n_sqlite.sru:L90
//     declares `global n_sqlite n_sqlite`, a global auto-instance shadowing its own type name; the
//     collision-resolution rule keeps the descriptive .NET type name and turns the INSTANCE into an
//     injected dependency. Every static member below is a pure function.
//   * NO CLOCK READ OF ITS OWN. No DateTime.UtcNow, no DateTime.Now, no DateTimeOffset.Now and no
//     Stopwatch. Every time value comes from the injected TimeProvider, which the host registers as
//     the single determinism seam this service shares with the transaction pool's idle expiry.
//   * NO SCREAMING_SNAKE IDENTIFIER IS DECLARED. The repository .editorconfig scopes its CA1707 and
//     IDE1006 relaxations to individually named files and is the SOLE ROSTER of them - the count is
//     deliberately not restated here, because a copied number is a second place for the roster to be
//     wrong and it silently became wrong as files were added. In this project the named files are
//     under Sql/; Data/ is outside every one of those sections, and warnings are errors. The journal-mode
//     tokens and the pragma texts below are STRING LITERALS, not C# identifiers, so preserving their
//     legacy spellings raises nothing.
//
//  RULES POSITION
//  No user rules were provided for this project: the rules document contains exactly one line saying
//  so, and re-reading it returns the same. Nothing is invented or back-filled from convention in
//  their place. The binding constraints are the enterprise-standard baseline - nullable enabled,
//  warnings as errors, no secret in source, parameterized SQL, structured logging with no credential
//  and no statement - plus the named non-rule constraints C-B, C-C, C-D, C-E, C-F, C-G, C-H, C-I,
//  C-J, C-K and C-L, each cited above and below at the point it applies, as C-K requires.
// ==================================================================================================

using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Errors;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Persistence.Data
{
    /// <summary>
    /// Opens and owns this service's single SQLite connection, reproducing the legacy
    /// <c>n_sqlite</c> facade's connection-level surface over
    /// <see cref="Microsoft.Data.Sqlite"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The name carries both halves of what the type does, and both are deliberate. It is a
    /// FACTORY - <see cref="CreateOpenConnectionAsync"/> hands a caller its own freshly opened and
    /// fully configured connection - and it is a FACADE, holding one ambient connection whose
    /// open, close, timeout and auto-commit state reproduce the legacy object's
    /// [<c>ws_objects/pfw.utility.sqlite.pbl.src/n_sqlite.sru:L12-L19</c>].
    /// </para>
    /// <para>
    /// THREAD SAFETY. <see cref="SqliteConnection"/> is not thread safe, and this type is intended
    /// to be resolved once for the process lifetime the way the legacy global auto-instance was, so
    /// every operation that touches the ambient connection is serialised through an asynchronous
    /// gate. <see cref="CreateOpenConnectionAsync"/> is the exception and needs no gate: it touches
    /// no shared connection state and returns an object the caller owns outright.
    /// </para>
    /// <para>
    /// FAIL FAST, PRESERVED AS FAIL FAST. A structural fault throws: a configured password, a data
    /// directory resolving inside the read-only legacy tree, an unrecognised URI mode token, an
    /// illegal journal mode, or a data directory that cannot be created. An OPERATIONAL fault -
    /// the provider refusing to open, a journal mode the engine will not accept, an integrity check
    /// that does not answer <c>ok</c> - is reported the way the legacy reported it, as a negative
    /// return code with <see cref="LastError"/> populated, so a caller can apply the ported
    /// predicates to it. Softening the first kind into the second would be a behavioural change
    /// dressed up as robustness.
    /// </para>
    /// <para>
    /// INTERNAL, AND FOR THREE CONVERGING REASONS RATHER THAN BY HABIT. Nothing outside this
    /// assembly consumes it - the composition root, the readiness route, the task layer and the
    /// gRPC services are all in this project - so a public surface would widen the API for no
    /// consumer, which is the trade the migration explicitly declines. It matches the surrounding
    /// convention, under which this project's domain types are internal and the sibling test project
    /// reaches them through the <c>InternalsVisibleTo</c> the project file already grants, so no
    /// interface and no widened surface is needed to make the pure functions below testable.
    /// And it keeps something true that is worth keeping true: the PUBLIC surface of the
    /// <c>Data</c> namespace remains exactly the one entity the evidenced schema supports, which is
    /// the no-fabricated-database constraint expressed as a census rather than as an aspiration -
    /// and which <c>PowerFramework.Persistence.Tests.CompanyEntityTests</c> asserts by reflection.
    /// A connection factory is not an entity, and it does not appear among them.
    /// </para>
    /// </remarks>
    internal sealed class SqliteConnectionFactory : IAsyncDisposable, IDisposable
    {
        // ------------------------------------------------------------------------------------------
        //  THE SIX JOURNAL MODES, AND THE PRAGMA TEXTS - ALL COMPILE-TIME CONSTANT
        //
        //  The tokens are exactly those documented at w_test_sqlite.srw:L455, in the order the legacy
        //  comment lists them, spelled the way the legacy comment spells them. They are string
        //  literals rather than an enumeration for two reasons: SqliteOptions.Journal is a string, and
        //  Configuration/ owns the only enumeration in this area - SqliteIntegrityCheckMode - so
        //  declaring a second one here would duplicate a type another folder owns.
        //
        //  Every pragma statement this file executes is selected from these constants by a switch over
        //  an already-validated token. NOTHING is interpolated into SQL anywhere in this file. That is
        //  what makes the one place SQL cannot be parameterized - a pragma keyword is a keyword, never
        //  a bindable value - safe by construction rather than safe by review.
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// The canonical spellings of the six legal journal modes, in the order
        /// <c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L455</c> lists them.
        /// </summary>
        /// <remarks>
        /// The first entry is also the legacy default, which is why
        /// <see cref="NormalizeJournalMode"/> can resolve an absent value by taking element zero
        /// rather than by repeating a literal.
        /// </remarks>
        private static readonly string[] JournalModes =
            ["DELETE", "TRUNCATE", "PERSIST", "MEMORY", "WAL", "OFF"];

        private const string JournalModeDeleteStatement = "PRAGMA journal_mode = DELETE;";
        private const string JournalModeTruncateStatement = "PRAGMA journal_mode = TRUNCATE;";
        private const string JournalModePersistStatement = "PRAGMA journal_mode = PERSIST;";
        private const string JournalModeMemoryStatement = "PRAGMA journal_mode = MEMORY;";
        private const string JournalModeWalStatement = "PRAGMA journal_mode = WAL;";
        private const string JournalModeOffStatement = "PRAGMA journal_mode = OFF;";

        /// <summary>
        /// The complete check, selected by a bare <c>check</c> parameter
        /// [<c>w_test_sqlite.srw:L454</c>].
        /// </summary>
        private const string IntegrityCheckStatement = "PRAGMA integrity_check;";

        /// <summary>
        /// The cheaper check, selected by <c>check=quick</c> [<c>w_test_sqlite.srw:L454</c>].
        /// </summary>
        private const string QuickCheckStatement = "PRAGMA quick_check;";

        /// <summary>
        /// The single answer both integrity pragmas give when the data file is sound.
        /// </summary>
        private const string IntegrityCheckOk = "ok";

        /// <summary>
        /// The reachability probe. Parameterless, constant, and the cheapest statement that proves
        /// the engine answered rather than merely that a file handle opened.
        /// </summary>
        private const string ReachabilityProbeStatement = "SELECT 1;";

        /// <summary>
        /// Existence of a table in a named schema, with BOTH the schema and the table name
        /// parameterized.
        /// </summary>
        /// <remarks>
        /// <para>
        /// SQLite's own catalogue table-valued function is used rather than a schema-qualified
        /// <c>sqlite_master</c> query, and that is a security decision rather than a stylistic one.
        /// A schema name is an IDENTIFIER, and an identifier cannot be a bound parameter - a
        /// <c>sqlite_master</c> form would therefore have to splice the caller's schema string into
        /// the statement text and rely on quoting to make it safe. <c>pragma_table_list</c> exposes
        /// the schema as a COLUMN, so both inputs bind as values and this file interpolates nothing.
        /// </para>
        /// <para>
        /// <c>type = 'table'</c> matches the legacy member's name
        /// [<c>ws_objects/pfw.utility.sqlite.pbl.src/n_sqlite.sru:L30-L31</c>]: a view is not a
        /// table, and widening this to include views would answer a question that was not asked.
        /// </para>
        /// </remarks>
        private const string TableExistsStatement =
            "SELECT COUNT(*) FROM pragma_table_list "
            + "WHERE \"schema\" = $schema AND name = $name AND type = 'table';";

        /// <summary>
        /// The one evidenced application table, which a readiness verdict requires to be present.
        /// </summary>
        /// <remarks>
        /// THE ONLY DDL IN THE ENTIRE LEGACY REPOSITORY creates this table
        /// [<c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L463-L469</c>], and the sole updatable
        /// DataWindow retrieves and updates it by this name
        /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14</c>]. The managed model maps to the same
        /// name [<c>Data/PowerFrameworkDbContext.cs</c> - <c>company.ToTable("COMPANY")</c>], so this
        /// spelling is a constant of this codebase rather than a configured value, and naming it in a
        /// diagnostic discloses nothing (constraint C-F).
        /// </remarks>
        private const string RequiredApplicationTable = "COMPANY";

        /// <summary>
        /// EF Core's migration bookkeeping table, which a readiness verdict also requires.
        /// </summary>
        /// <remarks>
        /// <para>
        /// REQUIRED BECAUSE IT IS THE ONLY PROVISIONING PATH THIS SERVICE HAS. Nothing in this service
        /// calls <c>EnsureCreated</c> or <c>Migrate</c> - deliberately, and both files say so at
        /// length - so the schema arrives exclusively through <c>dotnet ef database update</c>, which
        /// writes a row here for every migration it applies. A database holding the application table
        /// but NOT this one is therefore not a hand-provisioned equivalent: it is a database the
        /// migration tool would try to re-apply migration one against, and fail. Reporting it not-ready
        /// is the actionable answer.
        /// </para>
        /// <para>
        /// The name is EF Core's default and is not configured anywhere in this service, so like the
        /// application table it is a constant rather than a value.
        /// </para>
        /// </remarks>
        private const string RequiredMigrationHistoryTable = "__EFMigrationsHistory";

        /// <summary>
        /// The schema name SQLite gives the database opened by the connection itself.
        /// </summary>
        /// <remarks>
        /// This is what makes the one-argument
        /// <see cref="IsTableExistsAsync(string, CancellationToken)"/> overload
        /// [<c>n_sqlite.sru:L30</c>] a special case of the two-argument one [<c>:L31</c>] rather
        /// than a second implementation of the same question.
        /// </remarks>
        private const string MainSchemaName = "main";

        /// <summary>
        /// The one path segment the resolved data directory may never contain.
        /// </summary>
        /// <remarks>
        /// The legacy export tree is the behavioural oracle and is read-only (C-C). A data directory
        /// resolving inside it would put a writable database file among the oracle's own objects,
        /// where the very next characterization capture would read it as legacy source. That is a
        /// structural fault, so it is refused at construction rather than detected later.
        /// </remarks>
        private const string ReadOnlyLegacyTreeSegment = "ws_objects";

        // ------------------------------------------------------------------------------------------
        //  INSTANCE STATE - all of it instance state, none of it static (see the header)
        // ------------------------------------------------------------------------------------------

        private readonly ILogger<SqliteConnectionFactory> _logger;
        private readonly TimeProvider _timeProvider;

        /// <summary>
        /// Serialises every operation that touches <see cref="_connection"/>.
        /// </summary>
        private readonly SemaphoreSlim _gate = new(1, 1);

        private readonly SqliteIntegrityCheckMode? _check;
        private readonly string _journalMode;
        private readonly string _mode;
        private readonly SqliteOpenMode _openMode;

        private SqliteConnection? _connection;
        private SqliteTransaction? _ambientTransaction;
        private int _timeoutSeconds;
        private bool _disposed;

        private long _sqlCode;
        private long _sqlDbCode;
        private long _sqlNRows;
        private string _sqlErrText = string.Empty;
        private DbErrorData _lastError = DbErrorData.Empty;

        /// <summary>
        /// The cached POSITIVE reachability answer, or <see langword="null"/> when there is nothing
        /// cached. A failure is never cached, so this is never <see langword="false"/>.
        /// </summary>
        private bool? _lastReachability;

        /// <summary>
        /// When the reachability probe last ran, on the injected clock, or <see langword="null"/>
        /// when it has not run. Stamped on every probe including a failed one, which is what keeps
        /// it an honest record of activity rather than a mirror of the cache.
        /// </summary>
        /// <remarks>
        /// OBSERVABILITY ONLY. The cache WINDOW is decided on
        /// <see cref="_lastReachabilityTimestamp"/> instead, for the reason recorded there. This member
        /// answers "when did a probe last run", which is a wall-clock question an operator asks; it must
        /// not be used to answer "how long ago", which is an elapsed-time question.
        /// </remarks>
        private DateTimeOffset? _lastReachabilityAt;

        /// <summary>
        /// The MONOTONIC timestamp of the last probe, or <see langword="null"/> when none has run.
        /// </summary>
        /// <remarks>
        /// <para>
        /// SEPARATE FROM THE WALL-CLOCK STAMP BECAUSE SUBTRACTING TWO WALL-CLOCK READS DOES NOT MEASURE
        /// ELAPSED TIME. A wall clock can step in either direction - an NTP correction, a container
        /// resuming from a suspended host, a manual change - and each direction breaks the window a
        /// different way. A step FORWARD expires a window early, which merely costs an extra probe. A
        /// step BACKWARD is the one that matters: the computed age goes negative, so the comparison keeps
        /// reporting the cached positive answer, and a cached success can then outlive its window
        /// indefinitely while the engine behind it is already unreachable.
        /// </para>
        /// <para>
        /// <see cref="TimeProvider.GetTimestamp"/> and <see cref="TimeProvider.GetElapsedTime(long)"/>
        /// are monotonic by contract and are still INJECTED, so this remains a determinism seam a
        /// characterization run can mask from both the master and the candidate recording (AAP 0.6.7) -
        /// which a raw <see cref="System.Diagnostics.Stopwatch"/> would not have been.
        /// </para>
        /// </remarks>
        private long? _lastReachabilityTimestamp;

        /// <summary>
        /// Why the last readiness probe answered as it did.
        /// </summary>
        /// <remarks>
        /// Present because a boolean cannot carry the distinction the operator most needs: "the engine
        /// did not answer" and "the engine answered but there is no schema" call for completely
        /// different actions - fix the mount or the provider in the first case, run the migrations in the
        /// second - and a single false conflates them. It is a small closed enumeration rather than text,
        /// so nothing configured and nothing from the provider can travel on it.
        /// </remarks>
        private StorageReadiness _lastReadiness = StorageReadiness.NotProbed;

        // ==========================================================================================
        //  CONSTRUCTION - WHERE EVERY STRUCTURAL FAULT IS REFUSED
        // ==========================================================================================

        /// <summary>
        /// Resolves and validates the SQLite connection settings, refusing outright any
        /// configuration this phase cannot honour.
        /// </summary>
        /// <param name="options">
        /// The bound service configuration. Only its <see cref="PersistenceOptions.Sqlite"/> group
        /// is read; the host binds it with validation on start, so the annotations on that group
        /// have already run by the time this constructor sees it. The additional checks here are the
        /// ones the annotations cannot express - a token that must map onto a provider enumeration,
        /// a path that must not resolve inside the read-only legacy tree, and a credential that must
        /// not be present at all.
        /// </param>
        /// <param name="logger">The service's logger. Never receives a credential or a statement.</param>
        /// <param name="timeProvider">
        /// The single determinism seam the host registers. Every time value this type reports comes
        /// from here, so a characterization run can substitute a deterministic double for the whole
        /// host rather than for one call site.
        /// </param>
        /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// A password is configured - encrypted SQLite is out of Phase-1 scope and is refused rather
        /// than ignored; the data directory or database file name is absent or malformed; the data
        /// directory resolves inside the read-only legacy tree; or the URI mode or journal token is
        /// not one the provider can honour. Each message names the configuration key and the reason,
        /// and none of them echoes a configured value.
        /// </exception>
        public SqliteConnectionFactory(
            IOptions<PersistenceOptions> options,
            ILogger<SqliteConnectionFactory> logger,
            TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(timeProvider);

            _logger = logger;
            _timeProvider = timeProvider;

            SqliteOptions sqlite = options.Value?.Sqlite
                ?? throw new InvalidOperationException(
                    "The 'Sqlite' configuration group did not bind, so this service has no storage "
                    + "settings at all and could serve no request. Declare the section and restart.");

            // ------------------------------------------------------------------------------------
            //  THE PASSWORD - REFUSED, NOT IGNORED. See RULING 1 in the header for the full reason.
            //  Note what is NOT in the message: the value. A rejected credential is still a
            //  credential, and an exception message is a log record waiting to happen (C-F).
            // ------------------------------------------------------------------------------------
            if (!string.IsNullOrEmpty(sqlite.Password))
            {
                throw new InvalidOperationException(
                    "'Sqlite:Password' is configured, but encrypted SQLite is out of scope for this "
                    + "phase and this service will not start while a password is supplied. The "
                    + "provisioned native library is the PLAIN SQLite bundle, not a cipher-enabled "
                    + "one: the cipher library shipped in the legacy tree is materially older, its "
                    + "key-derivation and per-page integrity settings are not reachable through any "
                    + "framework interface, and the current provider therefore cannot reproduce the "
                    + "page format an existing encrypted file was created with. Accepting the "
                    + "setting and ignoring it would leave an operator believing the database is "
                    + "encrypted while it is plaintext on disk, so the setting is refused instead. "
                    + "Remove 'Sqlite__Password' from the environment to start. The configured value "
                    + "is deliberately not quoted here.");
            }

            // ------------------------------------------------------------------------------------
            //  THE URI MODE. The composed URI carries the configured token VERBATIM, so no token
            //  rule is invented for the grammar - Configuration/ deliberately validates this
            //  setting for presence only, on the grounds that the oracle constrains it to nothing
            //  beyond the one spelling it uses. The provider is a different matter: an open mode has
            //  to become a member of a four-valued enumeration, so an unrecognised token is a
            //  structural fault here even though it is not a grammar violation there.
            // ------------------------------------------------------------------------------------
            _mode = (sqlite.Mode ?? string.Empty).Trim();
            if (_mode.Length == 0)
            {
                throw new InvalidOperationException(
                    "'Sqlite:Mode' is empty. The legacy value is 'rwc' - read, write, create - from "
                    + "ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L456. An empty mode would "
                    + "compose the meaningless URI parameter 'mode=' and leave the open behaviour to "
                    + "whatever the engine then chose.");
            }

            _openMode = MapOpenMode(_mode);
            _journalMode = NormalizeJournalMode(sqlite.Journal);
            _check = ValidateIntegrityCheckMode(sqlite.Check);

            // ------------------------------------------------------------------------------------
            //  THE FILE NAME. It is a NAME, which is why a separator in it is refused rather than
            //  combined: `Path.Combine` treats a rooted second argument as the whole path and would
            //  silently discard the configured directory, and a traversal segment would walk out of
            //  it. Refusing both is also what makes the read-only-tree check below unbypassable.
            // ------------------------------------------------------------------------------------
            string fileName = (sqlite.DatabaseFileName ?? string.Empty).Trim();
            if (fileName.Length == 0)
            {
                throw new InvalidOperationException(
                    "'Sqlite:DatabaseFileName' is empty, so this service has no database file to "
                    + "open. The legacy name is retained deliberately - see "
                    + "ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L456 - so that stored "
                    + "characterization comparisons keep resolving.");
            }

            if (fileName != Path.GetFileName(fileName))
            {
                throw new InvalidOperationException(
                    "'Sqlite:DatabaseFileName' must be a bare file name and must contain no "
                    + "directory separator and no relative segment. The directory is configured "
                    + "separately as 'Sqlite:DataDirectory', which is the mount point of the "
                    + "persistence-db volume; a path here would silently escape it.");
            }

            string dataDirectory = (sqlite.DataDirectory ?? string.Empty).Trim();
            if (dataDirectory.Length == 0)
            {
                throw new InvalidOperationException(
                    "'Sqlite:DataDirectory' is empty. It names the directory holding the SQLite "
                    + "database file, it is the mount point of the persistence-db volume, and it "
                    + "must be writable by the container image's non-root user. There is no "
                    + "fallback: a service that cannot locate its only storage engine can serve no "
                    + "request.");
            }

            // Resolved once, here, so that every later member reads one settled absolute path
            // rather than re-resolving a relative one against a working directory that may since
            // have changed. This reads the process working directory for a relative input and
            // touches the filesystem for nothing - directory CREATION belongs to the open path.
            DataDirectory = Path.GetFullPath(dataDirectory);

            // ------------------------------------------------------------------------------------
            //  THE READ-ONLY LEGACY TREE (C-C). Checked on the RESOLVED path and by whole path
            //  segment, so neither a relative input nor a traversal can slip past a prefix test.
            // ------------------------------------------------------------------------------------
            if (ResolvesInsideReadOnlyLegacyTree(DataDirectory))
            {
                throw new InvalidOperationException(
                    "'Sqlite:DataDirectory' resolves inside the read-only legacy export tree, whose "
                    + "path contains a '" + ReadOnlyLegacyTreeSegment + "' segment. That tree is the "
                    + "behavioural oracle for parity testing and is never written to. Point the "
                    + "setting at the persistence-db volume mount instead.");
            }

            DatabaseFileName = fileName;
            DatabasePath = Path.Combine(DataDirectory, fileName);

            // ------------------------------------------------------------------------------------
            //  THE TWO PARITY ARTEFACTS. Both come from the same pure function, and neither can carry a
            //  credential (RULING 1) - but only the first is written to a log. The RESOLVED form embeds
            //  the data directory, and a log record that publishes where this deployment mounts its
            //  storage discloses layout to every reader of the log without helping any of them; it is
            //  kept as a property for a caller that genuinely needs the deployment form.
            //
            //    LegacyUri          `<DatabaseFileName>?mode=...` - the exact form n_sqlite.Open()
            //                       received at w_test_sqlite.srw:L456, which for the legacy defaults
            //                       is byte-for-byte `test.db?mode=rwc`. This is the form the matrix
            //                       tests assert and the form a characterization recording carries.
            //    LegacyResolvedUri  `<DataDirectory>/<DatabaseFileName>?mode=...` - the deployment
            //                       form, which is the shape Configuration/PersistenceOptions.cs
            //                       documents this factory as assembling.
            //
            //  Both are kept because they answer different questions - what the oracle was asked, and
            //  what this deployment opened - and collapsing them would lose one of the two.
            // ------------------------------------------------------------------------------------
            LegacyUri = ComposeLegacyUri(fileName, _mode, _check, _journalMode);
            LegacyResolvedUri = ComposeLegacyUri(DatabasePath, _mode, _check, _journalMode);

            // The provider's OWN default, read from the provider rather than restated as a literal.
            // n_sqlite exposes GetTimeout/SetTimeout [n_sqlite.sru:L12-L13] but the legacy publishes
            // no initial value anywhere, so inventing one would be a fabricated default; deferring to
            // the provider is the only choice that adds no behaviour. Both sides count SECONDS.
            _timeoutSeconds = new SqliteConnectionStringBuilder().DefaultTimeout;
        }

        // ==========================================================================================
        //  THE RESOLVED ARTEFACTS - every one settled at construction, every one safe to log
        // ==========================================================================================

        /// <summary>
        /// The resolved absolute directory holding the database file.
        /// </summary>
        /// <remarks>
        /// The mount point of the named <c>persistence-db</c> volume, attached to this service
        /// alone. It must be writable by the container image's non-root user, and it is created if
        /// absent - see <see cref="OpenAsync"/> - but its contents are never removed, replaced or
        /// reseeded by anything in this file.
        /// </remarks>
        public string DataDirectory { get; }

        /// <summary>
        /// The database file name, without a directory component.
        /// </summary>
        /// <remarks>
        /// The legacy name is retained deliberately
        /// [<c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L456</c>]: it reads like a test
        /// artefact, and a tidier one would invalidate every stored characterization comparison that
        /// resolves against it.
        /// </remarks>
        public string DatabaseFileName { get; }

        /// <summary>
        /// The resolved absolute path of the database file - the data source the provider is given.
        /// </summary>
        /// <remarks>
        /// This is never the legacy <c>test.db</c> at the repository root. That file is part of the
        /// read-only oracle and is neither opened nor written nor deleted by this service; this path
        /// always resolves under <see cref="DataDirectory"/>.
        /// </remarks>
        public string DatabasePath { get; }

        /// <summary>
        /// The legacy-form connection URI in its PARITY shape - the bare database file name in the
        /// path position, exactly as <c>n_sqlite.Open()</c> received it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// For the preserved legacy defaults this is <c>test.db?mode=rwc&amp;journal=DELETE</c>,
        /// reproducing <c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L456</c> together with the
        /// journal extension its own comment declares defaulted at <c>:L455</c>.
        /// </para>
        /// <para>
        /// DELIBERATELY NOT THE STRING HANDED TO SQLITE. <c>check</c> and <c>journal</c> are the
        /// framework's extension parameters, not SQLite's, and SQLite silently ignores query
        /// parameters it does not recognise - so passing this through would open a connection that
        /// quietly had neither the journal mode nor the integrity check configured. This value is the
        /// parity and diagnostic artefact; <see cref="ConnectionString"/> plus the pragmas applied at
        /// open are the effect. See RULING 2 in the file header.
        /// </para>
        /// <para>
        /// Safe to log, safe to return in a diagnostic and safe to capture into a recording: no
        /// credential can appear in it, because the legacy password is the second argument of the
        /// open call rather than URI syntax.
        /// </para>
        /// </remarks>
        public string LegacyUri { get; }

        /// <summary>
        /// The legacy-form connection URI in its DEPLOYMENT shape - the resolved absolute path in
        /// the path position.
        /// </summary>
        /// <remarks>
        /// The form <c>Configuration/PersistenceOptions.cs</c> documents this factory as assembling,
        /// <c>&lt;DataDirectory&gt;/&lt;DatabaseFileName&gt;?mode=…</c>. It answers "what did THIS
        /// deployment open", where <see cref="LegacyUri"/> answers "what was the oracle asked". Both
        /// are composed by the same pure function and both are safe to log for the same reason.
        /// </remarks>
        public string LegacyResolvedUri { get; }

        /// <summary>
        /// The provider connection string, rebuilt from the current settings on every read.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Carries the resolved data source, the mapped open mode and the current command timeout in
        /// seconds, and NOTHING ELSE. In particular it never carries a password, because a supplied
        /// one is refused at construction rather than applied, so this value is safe to log.
        /// </para>
        /// <para>
        /// Recomputed rather than cached so that a <see cref="SetTimeout"/> made after construction
        /// is visible here, and so the value can never be a stale copy of a setting that has since
        /// changed. It carries neither <c>check</c> nor <c>journal</c>: those are the framework's
        /// extension parameters and become pragmas.
        /// </para>
        /// </remarks>
        public string ConnectionString
        {
            get
            {
                return BuildConnectionString(_timeoutSeconds);
            }
        }

        /// <summary>
        /// The canonical journal mode that will be applied, as a pragma, immediately after opening.
        /// </summary>
        /// <remarks>
        /// One of the six tokens documented at
        /// <c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L455</c>, in the canonical upper-case
        /// spelling. Defaults to <c>DELETE</c>, which is the legacy default and is not "improved" to
        /// write-ahead logging.
        /// </remarks>
        public string JournalMode
        {
            get
            {
                return _journalMode;
            }
        }

        /// <summary>
        /// The integrity check that will run immediately after opening, or <see langword="null"/>
        /// when none will.
        /// </summary>
        /// <remarks>
        /// Three states, and the <see langword="null"/> is a genuine third rather than a stand-in
        /// for "off": absent means no verification statement is executed at all.
        /// </remarks>
        public SqliteIntegrityCheckMode? IntegrityCheck
        {
            get
            {
                return _check;
            }
        }

        /// <summary>
        /// Whether the ambient connection is currently open.
        /// </summary>
        /// <remarks>
        /// The substitute for <c>n_sqlite.IsOpened()</c>
        /// [<c>ws_objects/pfw.utility.sqlite.pbl.src/n_sqlite.sru:L16</c>], and the value the
        /// fixture's own idempotent-open guard tests before doing anything else
        /// [<c>w_test_sqlite.srw:L448</c>]. Read from the provider's connection state rather than
        /// from a flag of its own, so it cannot drift from the truth.
        /// </remarks>
        public bool IsOpened
        {
            get
            {
                return _connection is { State: ConnectionState.Open };
            }
        }

        /// <summary>
        /// Whether the connection is in auto-commit mode - that is, whether NO ambient transaction
        /// is being held.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The substitute for <c>n_sqlite.IsAutoCommit()</c> [<c>n_sqlite.sru:L14</c>]. ADO.NET has
        /// no auto-commit switch, so the state is expressed the only way it can be: auto-commit on
        /// means no ambient transaction, auto-commit off means one is held.
        /// </para>
        /// <para>
        /// DERIVED, NOT STORED, and that is what keeps this file free of a fabricated default. The
        /// legacy publishes no initial value - the fixture sets it explicitly immediately after
        /// opening [<c>w_test_sqlite.srw:L461</c>] and again after the schema statement
        /// [<c>:L473</c>] - so rather than inventing one, this reports the observable fact about the
        /// connection. A connection with no transaction on it IS in auto-commit, so a freshly opened
        /// one reads <see langword="true"/> without anything having chosen that.
        /// </para>
        /// </remarks>
        public bool IsAutoCommit
        {
            get
            {
                return _ambientTransaction is null;
            }
        }

        /// <summary>
        /// The ambient transaction held while auto-commit is off, or <see langword="null"/> while it
        /// is on.
        /// </summary>
        /// <remarks>
        /// EXPOSED SO THAT SOMEONE ELSE CAN RESOLVE IT, which is the whole point. This file owns the
        /// connection-level STATE; the transaction VERBS - the legacy's <c>AutoCommit()</c>,
        /// <c>Commit([autorollback])</c> and <c>Rollback()</c> [<c>n_sqlite.sru:L22-L25</c>] - belong
        /// to <c>Transactions/</c> and the transaction contract. Were this held privately with no
        /// accessor, a transaction opened here could never be committed by the component that owns
        /// committing, so the boundary would strand data instead of dividing responsibility.
        /// </remarks>
        public SqliteTransaction? AmbientTransaction
        {
            get
            {
                return _ambientTransaction;
            }
        }

        /// <summary>
        /// The open connection, or <see langword="null"/> when this factory holds none.
        /// </summary>
        /// <remarks>
        /// <para>
        /// EXPOSED FOR THE SAME REASON <see cref="AmbientTransaction"/> IS, AND WITH THE SAME
        /// BOUNDARY. This file owns opening, closing and the connection-level state; the SQL layer
        /// owns statements. A statement layer that opened its OWN connection would be a correctness
        /// defect rather than a style choice: SQLite isolates connections, so work written inside this
        /// factory's ambient transaction is INVISIBLE to any other connection until that transaction
        /// commits. A retrieval issued on a second connection during an open transaction would
        /// therefore answer the pre-transaction rows and report success - the failure mode contract
        /// C-08 exists to prevent, and one no row-count assertion would catch.
        /// </para>
        /// <para>
        /// IT IS DELIBERATELY NOT A "GET OR OPEN" ACCESSOR. Answering null while closed keeps opening
        /// in exactly one place - <see cref="OpenAsync"/>, reached through the transaction contract's
        /// own connect verb - so a caller that finds null has genuinely not connected and learns that
        /// rather than silently acquiring a connection whose URI, pragmas and journal mode nobody
        /// applied.
        /// </para>
        /// </remarks>
        internal SqliteConnection? AmbientConnection
        {
            get
            {
                return IsOpened ? _connection : null;
            }
        }

        /// <summary>
        /// Detaches the ambient transaction and hands it to the caller, leaving this factory holding
        /// none.
        /// </summary>
        /// <returns>The detached transaction, or <see langword="null"/> when none was held.</returns>
        /// <remarks>
        /// <para>
        /// THE OTHER HALF OF THE BOUNDARY <see cref="AmbientTransaction"/> DESCRIBES. That property
        /// lets the transaction layer REACH the transaction; this one lets it TAKE ownership, which is
        /// what committing actually requires. Without it the field would go stale the instant someone
        /// committed: the provider clears the transaction from the CONNECTION on commit - measured -
        /// but nothing clears it from here, so <see cref="IsAutoCommit"/> would keep reporting false
        /// for a transaction that no longer exists and <see cref="SetAutoCommitAsync"/> would refuse to
        /// turn auto-commit on, citing a transaction that had already been resolved.
        /// </para>
        /// <para>
        /// THE CALLER OWNS DISPOSAL AFTERWARDS, and that is the point of "take" rather than "resolve":
        /// committing and rolling back are different verbs with different failure handling, and
        /// deciding between them here would put the decision back in the file whose whole design is
        /// that it does not make it.
        /// </para>
        /// <para>
        /// The exchange is atomic so that two callers cannot both believe they hold the transaction -
        /// the loser observes <see langword="null"/> and reports "nothing to commit", which is true.
        /// </para>
        /// </remarks>
        internal SqliteTransaction? TakeAmbientTransaction()
        {
            return Interlocked.Exchange(ref _ambientTransaction, null);
        }

        /// <summary>
        /// The row count the last connection-level statement reported, reproducing
        /// <c>n_sqlite.SQLNRows()</c> [<c>n_sqlite.sru:L26</c>].
        /// </summary>
        /// <remarks>
        /// This facade executes only its own connection-level statements - the two integrity
        /// pragmas, the journal pragma, the reachability probe and the catalogue lookup - so this
        /// reports those. Retrieval and update row counts belong to the query and update contracts.
        /// </remarks>
        public long SqlNRows
        {
            get
            {
                return _sqlNRows;
            }
        }

        /// <summary>
        /// The legacy status indicator for the last connection-level operation, reproducing
        /// <c>n_sqlite.SQLCode()</c> [<c>n_sqlite.sru:L27</c>].
        /// </summary>
        /// <remarks>
        /// <see cref="RetCode.OK"/> after a success and <see cref="RetCode.FAILED"/> after a
        /// failure, which are the legacy convention's own zero and minus one taken from the ported
        /// catalogue rather than written as bare numbers. The convention's third value, the
        /// hundred that means "no rows found", is unreachable from here because nothing in this file
        /// retrieves a result set on a caller's behalf.
        /// </remarks>
        public long SqlCode
        {
            get
            {
                return _sqlCode;
            }
        }

        /// <summary>
        /// The provider's own result code for the last connection-level operation, reproducing
        /// <c>n_sqlite.SQLDBCode()</c> [<c>n_sqlite.sru:L28</c>].
        /// </summary>
        /// <remarks>
        /// Always one of the preserved <c>SQLITE_*</c> constants where the code is a documented one;
        /// see <see cref="MapSqliteResultCode"/> for how the correspondence is established.
        /// </remarks>
        public long SqlDbCode
        {
            get
            {
                return _sqlDbCode;
            }
        }

        /// <summary>
        /// The provider's message for the last connection-level failure, or an empty string,
        /// reproducing <c>n_sqlite.SQLErrText()</c> [<c>n_sqlite.sru:L29</c>].
        /// </summary>
        /// <remarks>
        /// This is the value the fixture displays when an open fails
        /// [<c>w_test_sqlite.srw:L457</c>]. It carries a provider message and never a statement.
        /// </remarks>
        public string SqlErrText
        {
            get
            {
                return _sqlErrText;
            }
        }

        /// <summary>
        /// The structured payload for the last connection-level failure.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The sanctioned shape, taken from <c>Errors/DbErrorData.cs</c> rather than declared here:
        /// the legacy binding raises <c>OnDBError(long code, string sqlErrorText, string sqlSyntax)</c>
        /// [<c>n_sqlite.sru:L88</c>] and exposes the same three values as accessors on the connection
        /// object itself [<c>:L26-L29</c>], so a structured open or connect failure is genuinely part
        /// of this facade. <c>Data</c> referencing <c>Errors</c> is one-way and creates no cycle,
        /// because <c>Errors/</c> takes no intra-project dependency at all.
        /// </para>
        /// <para>
        /// Internal because the payload type is, and it is built EXCLUSIVELY through
        /// <c>DbErrorData.FromTransaction</c>, which leaves the statement field empty by
        /// construction. That is deliberate rather than incidental: it means no statement this file
        /// executes can reach an error payload, a log record or a recording, so redaction never has
        /// anything to do here.
        /// </para>
        /// </remarks>
        internal DbErrorData LastError
        {
            get
            {
                return _lastError;
            }
        }

        /// <summary>
        /// When the reachability probe last ran, measured on the injected clock, or
        /// <see langword="null"/> if it has not run.
        /// </summary>
        /// <remarks>
        /// Present so that the liveness cache in <see cref="IsReachableAsync"/> is observable rather
        /// than opaque, and so a test driving a fake clock can assert the caching decision instead of
        /// inferring it. This type reads no clock of its own anywhere.
        /// </remarks>
        public DateTimeOffset? LastReachabilityProbedAt
        {
            get
            {
                return _lastReachabilityAt;
            }
        }

        /// <summary>
        /// Why the last readiness probe answered as it did, or
        /// <see cref="StorageReadiness.NotProbed"/> before the first probe.
        /// </summary>
        /// <remarks>
        /// Read by the readiness check to choose between two fixed descriptions. It is a closed
        /// enumeration by design: an anonymous response may carry it without carrying a path, a provider
        /// message or a statement (constraint C-F). It is NOT reset when the connection closes, because
        /// it records what the last probe found rather than the current state of a handle.
        /// </remarks>
        public StorageReadiness LastReadiness
        {
            get
            {
                return _lastReadiness;
            }
        }

        // ==========================================================================================
        //  THE PURE FUNCTIONS
        //
        //  Deterministic, side-effect free, and internal so the sibling test project can drive them
        //  directly - the application project already grants it InternalsVisibleTo, so no interface
        //  and no widened public surface is needed to make them testable (C-H). None of them reads a
        //  clock, touches the filesystem, opens a connection or logs. Given the same inputs each
        //  returns the same string, always, which is what lets the whole 3-by-6 check-and-journal
        //  matrix plus the mode permutations run with no I/O at all.
        // ==========================================================================================

        /// <summary>
        /// Composes the legacy connection URI from a database identity and the three URI settings.
        /// </summary>
        /// <param name="databaseIdentity">
        /// What stands in the URI's path position: the bare database file name for the parity form,
        /// or the resolved absolute path for the deployment form. Emitted verbatim.
        /// </param>
        /// <param name="mode">
        /// The <c>mode</c> parameter value, emitted verbatim after trimming. Never validated against
        /// a token set here - see the constructor for why the grammar and the provider differ on
        /// this point.
        /// </param>
        /// <param name="check">
        /// The three-state integrity check. <see langword="null"/> omits the parameter entirely,
        /// <see cref="SqliteIntegrityCheckMode.Full"/> emits the BARE parameter name with no
        /// <c>=</c> and no value, and <see cref="SqliteIntegrityCheckMode.Quick"/> emits
        /// <c>check=quick</c>.
        /// </param>
        /// <param name="journal">
        /// The journal mode. <see langword="null"/> or blank resolves to the legacy default; any
        /// other value must be one of the six documented tokens, matched case-insensitively.
        /// </param>
        /// <returns>
        /// The composed URI, for example <c>test.db?mode=rwc&amp;journal=DELETE</c>, or
        /// <c>test.db?mode=rwc&amp;check&amp;journal=WAL</c> with the full check requested.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// <paramref name="databaseIdentity"/> or <paramref name="mode"/> is blank, or
        /// <paramref name="journal"/> is not one of the six documented tokens.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="check"/> holds a value outside the declared enumeration.
        /// </exception>
        /// <remarks>
        /// <para>
        /// THE PARAMETER ORDER IS PART OF THE ARTEFACT. <c>mode</c>, then <c>check</c>, then
        /// <c>journal</c> - the order the legacy comments introduce them in
        /// [<c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L454-L456</c>] and the order
        /// <c>Configuration/PersistenceOptions.cs</c> documents. A stored comparison is a string
        /// comparison, so reordering the parameters would invalidate every recording even though the
        /// settings they express are identical.
        /// </para>
        /// <para>
        /// NO PASSWORD APPEARS HERE IN ANY FORM, and there is no parameter through which one could.
        /// The credential is the second argument of the legacy open call
        /// [<c>ws_objects/pfw.utility.sqlite.pbl.src/n_sqlite.sru:L18</c>], never URI syntax, so
        /// neither <c>,password</c> nor <c>password=</c> is emitted - which is precisely what makes
        /// the returned string safe to log.
        /// </para>
        /// </remarks>
        internal static string ComposeLegacyUri(
            string databaseIdentity,
            string mode,
            SqliteIntegrityCheckMode? check,
            string? journal)
        {
            if (string.IsNullOrWhiteSpace(databaseIdentity))
            {
                throw new ArgumentException(
                    "A database identity is required: it occupies the path position of the URI, "
                    + "which the legacy fills with the database file name at "
                    + "ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L456.",
                    nameof(databaseIdentity));
            }

            if (string.IsNullOrWhiteSpace(mode))
            {
                throw new ArgumentException(
                    "A URI mode is required. The legacy value is 'rwc' - read, write, create - from "
                    + "ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L456. Emitting 'mode=' with no "
                    + "value would state nothing and leave the open behaviour to the engine.",
                    nameof(mode));
            }

            // Validated before anything is written, so a rejected journal token can never produce a
            // partially composed string that a caller might mistake for a usable URI.
            string journalMode = NormalizeJournalMode(journal);
            SqliteIntegrityCheckMode? integrityCheck = ValidateIntegrityCheckMode(check);

            // Capacity is a hint only; the four fragments below are the whole of the result.
            StringBuilder uri = new(
                databaseIdentity.Length + mode.Length + journalMode.Length + 32);

            uri.Append(databaseIdentity.Trim());
            uri.Append("?mode=");
            uri.Append(mode.Trim());

            // THREE STATES, AND THE MIDDLE ONE IS THE ONE THAT IS EASY TO GET WRONG. `Full` emits the
            // BARE parameter name - no '=', no value - because "present without a value" is exactly
            // what the legacy grammar `check[=quick]` means by the unqualified form
            // [w_test_sqlite.srw:L454]. Emitting `check=full` or `check=true` would not reproduce it.
            // A null omits the parameter altogether, which is the third state a boolean could not
            // express and which must never be collapsed into "false".
            switch (integrityCheck)
            {
                case SqliteIntegrityCheckMode.Full:
                    uri.Append("&check");
                    break;

                case SqliteIntegrityCheckMode.Quick:
                    uri.Append("&check=quick");
                    break;

                default:
                    // Absent. Nothing is emitted, deliberately.
                    break;
            }

            uri.Append("&journal=");
            uri.Append(journalMode);

            return uri.ToString();
        }

        /// <summary>
        /// Composes the legacy parity form of the URI - the bare database file name in the path
        /// position - straight from a bound options group.
        /// </summary>
        /// <param name="sqlite">The bound SQLite settings group.</param>
        /// <returns>
        /// The composed URI. For the preserved legacy defaults this is
        /// <c>test.db?mode=rwc&amp;journal=DELETE</c>.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="sqlite"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// The convenience overload the matrix tests drive, so that a test states a settings shape
        /// rather than four positional arguments. It reads <see cref="SqliteOptions.Password"/>
        /// nowhere, which is what keeps it a pure string function over non-credential values.
        /// </remarks>
        internal static string ComposeLegacyUri(SqliteOptions sqlite)
        {
            ArgumentNullException.ThrowIfNull(sqlite);

            return ComposeLegacyUri(
                sqlite.DatabaseFileName,
                sqlite.Mode,
                sqlite.Check,
                sqlite.Journal);
        }

        /// <summary>
        /// Resolves a configured journal mode to its canonical spelling, refusing anything outside
        /// the six documented tokens.
        /// </summary>
        /// <param name="journal">
        /// The configured value. <see langword="null"/>, empty or whitespace resolves to the legacy
        /// default; anything else is trimmed and matched case-insensitively.
        /// </param>
        /// <returns>
        /// One of <c>DELETE</c>, <c>TRUNCATE</c>, <c>PERSIST</c>, <c>MEMORY</c>, <c>WAL</c> or
        /// <c>OFF</c>, in the canonical upper-case spelling.
        /// </returns>
        /// <exception cref="ArgumentException">The value is none of the six tokens.</exception>
        /// <remarks>
        /// <para>
        /// THE DEFAULT IS <c>DELETE</c> AND MUST STAY <c>DELETE</c>. The legacy states it inline at
        /// <c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L455</c>. A modern reader reaches for
        /// write-ahead logging, and choosing it here because it is "better" would be exactly the
        /// silent behavioural improvement this migration forbids: journal mode changes
        /// crash-recovery semantics, on-disk file layout and reader/writer concurrency, every one of
        /// which is observable. A deployment that wants write-ahead logging asks for it by name.
        /// </para>
        /// <para>
        /// THE CANONICAL SPELLING IS EMITTED RATHER THAN THE CONFIGURED ONE, so <c>wal</c> and
        /// <c>" WAL "</c> both compose <c>journal=WAL</c>. Upper case is the spelling the grammar
        /// itself uses at <c>:L455</c>, and pinning one form is what keeps a stored comparison
        /// stable across deployments that spell a setting differently. It also guarantees the
        /// composed URI and the pragma actually executed can never disagree, because both are
        /// derived from this one token.
        /// </para>
        /// <para>
        /// A blank value resolving to the default rather than failing is deliberate DEFENCE IN
        /// DEPTH and not a disagreement with <c>Configuration/</c>: the options validator refuses an
        /// explicitly empty <c>Sqlite:Journal</c> at startup, so a blank never reaches this function
        /// from configuration at all. It can reach it from a direct call, and answering that with
        /// the documented default is more useful than answering it with an exception.
        /// </para>
        /// </remarks>
        internal static string NormalizeJournalMode(string? journal)
        {
            if (string.IsNullOrWhiteSpace(journal))
            {
                // Element zero IS the legacy default, so the default is expressed once, in the
                // token list, rather than repeated as a second literal that could drift from it.
                return JournalModes[0];
            }

            string candidate = journal.Trim();

            foreach (string mode in JournalModes)
            {
                if (string.Equals(mode, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return mode;
                }
            }

            throw new ArgumentException(
                "'"
                + candidate
                + "' is not a legal journal mode. It must be one of "
                + string.Join(", ", JournalModes)
                + ", matched case-insensitively - see "
                + "ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L455. The legacy default is "
                + JournalModes[0]
                + ".",
                nameof(journal));
        }

        /// <summary>
        /// Confirms that a three-state integrity-check setting holds either nothing or a declared
        /// enumeration member.
        /// </summary>
        /// <param name="check">The configured value.</param>
        /// <returns>The value unchanged, once confirmed.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The value is present but outside the declared enumeration.
        /// </exception>
        /// <remarks>
        /// <see cref="SqliteIntegrityCheckMode"/> is declared by
        /// <c>Configuration/PersistenceOptions.cs</c> and is CONSUMED here, never re-declared: a
        /// second type of the same name in this namespace would be ambiguous wherever both were in
        /// scope. Because a C# enumeration will hold any value of its underlying type, an undeclared
        /// one has to be rejected explicitly - otherwise it would fall through the composition
        /// switch and silently omit the parameter, which is the behaviour of a DIFFERENT state.
        /// </remarks>
        internal static SqliteIntegrityCheckMode? ValidateIntegrityCheckMode(
            SqliteIntegrityCheckMode? check)
        {
            if (check is null)
            {
                return null;
            }

            if (check is not (SqliteIntegrityCheckMode.Full or SqliteIntegrityCheckMode.Quick))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(check),
                    check,
                    "The integrity-check setting must be absent, "
                    + nameof(SqliteIntegrityCheckMode.Full)
                    + " for a bare 'check' parameter, or "
                    + nameof(SqliteIntegrityCheckMode.Quick)
                    + " for 'check=quick' - see "
                    + "ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L454. Absence is the third "
                    + "state and means no integrity verification runs at all.");
            }

            return check;
        }

        /// <summary>
        /// Maps a URI <c>mode</c> token onto the provider's open mode.
        /// </summary>
        /// <param name="mode">The configured token, trimmed and matched case-insensitively.</param>
        /// <returns>The corresponding <see cref="SqliteOpenMode"/>.</returns>
        /// <exception cref="ArgumentException">
        /// The token is not one of the four the SQLite URI specification defines.
        /// </exception>
        /// <remarks>
        /// <para>
        /// The four tokens are those the URI specification the legacy cites at
        /// <c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L452</c> defines, and the legacy uses
        /// exactly one of them, <c>rwc</c> [<c>:L456</c>].
        /// </para>
        /// <para>
        /// AN UNRECOGNISED TOKEN FAILS RATHER THAN FALLING BACK. Defaulting a misspelling to
        /// read-write-create would turn a typo into a database this deployment never asked for -
        /// created, on disk, and indistinguishable afterwards from one that was intended. That is
        /// the fabricated-database failure mode in miniature, so the token is refused.
        /// </para>
        /// </remarks>
        internal static SqliteOpenMode MapOpenMode(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode))
            {
                throw new ArgumentException(
                    "A URI mode token is required. The legacy value is 'rwc' from "
                    + "ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L456.",
                    nameof(mode));
            }

            string candidate = mode.Trim();

            if (string.Equals(candidate, "rwc", StringComparison.OrdinalIgnoreCase))
            {
                return SqliteOpenMode.ReadWriteCreate;
            }

            if (string.Equals(candidate, "rw", StringComparison.OrdinalIgnoreCase))
            {
                return SqliteOpenMode.ReadWrite;
            }

            if (string.Equals(candidate, "ro", StringComparison.OrdinalIgnoreCase))
            {
                return SqliteOpenMode.ReadOnly;
            }

            if (string.Equals(candidate, "memory", StringComparison.OrdinalIgnoreCase))
            {
                return SqliteOpenMode.Memory;
            }

            throw new ArgumentException(
                "'"
                + candidate
                + "' is not a URI mode this provider can open. It must be one of ro, rw, rwc or "
                + "memory. The legacy value is rwc - read, write, create - from "
                + "ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L456.",
                nameof(mode));
        }

        /// <summary>
        /// Reports whether a resolved absolute path lies inside the read-only legacy export tree.
        /// </summary>
        /// <param name="resolvedPath">An already absolute path.</param>
        /// <returns>
        /// <see langword="true"/> when any whole segment of the path is the legacy export directory.
        /// </returns>
        /// <remarks>
        /// Compared segment by segment rather than by substring, so a directory legitimately named
        /// with the segment as a prefix or suffix is not caught, and a traversal that walks into the
        /// tree still is. Both directory separators are honoured so the check does not depend on the
        /// host platform.
        /// </remarks>
        internal static bool ResolvesInsideReadOnlyLegacyTree(string resolvedPath)
        {
            if (string.IsNullOrEmpty(resolvedPath))
            {
                return false;
            }

            string[] segments = resolvedPath.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);

            foreach (string segment in segments)
            {
                if (string.Equals(segment, ReadOnlyLegacyTreeSegment, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Maps a provider result code onto the preserved <c>SQLITE_*</c> catalogue in the shared
        /// kernel.
        /// </summary>
        /// <param name="errorCode">The primary result code the provider reported.</param>
        /// <param name="extendedErrorCode">
        /// The extended result code. When it differs from <paramref name="errorCode"/> it is the
        /// more specific answer and is returned as-is.
        /// </param>
        /// <returns>
        /// The matching <see cref="RetCode"/> <c>SQLITE_*</c> constant, or the extended code
        /// unchanged when the primary code is not one of the documented base results.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The values in <see cref="RetCode"/> ARE SQLite's own numbers - the catalogue preserves
        /// them verbatim, extended codes included, each written there as its base plus a multiple of
        /// 256 - so this function invents no number and translates no value. What it does is make
        /// the correspondence EXPLICIT and assertable, so that a code arriving from the provider is
        /// recognised against the ported catalogue rather than merely passed through as an integer
        /// nobody has checked.
        /// </para>
        /// <para>
        /// An extended code is preferred when the provider supplies a distinct one, because it
        /// carries strictly more information and the catalogue models it. The base-code arm is what
        /// answers when the two are equal, which is the common case.
        /// </para>
        /// </remarks>
        internal static long MapSqliteResultCode(int errorCode, int extendedErrorCode)
        {
            if (extendedErrorCode != errorCode && extendedErrorCode != 0)
            {
                return extendedErrorCode;
            }

            return errorCode switch
            {
                0 => RetCode.SQLITE_OK,
                1 => RetCode.SQLITE_ERROR,
                2 => RetCode.SQLITE_INTERNAL,
                3 => RetCode.SQLITE_PERM,
                4 => RetCode.SQLITE_ABORT,
                5 => RetCode.SQLITE_BUSY,
                6 => RetCode.SQLITE_LOCKED,
                7 => RetCode.SQLITE_NOMEM,
                8 => RetCode.SQLITE_READONLY,
                9 => RetCode.SQLITE_INTERRUPT,
                10 => RetCode.SQLITE_IOERR,
                11 => RetCode.SQLITE_CORRUPT,
                12 => RetCode.SQLITE_NOTFOUND,
                13 => RetCode.SQLITE_FULL,
                14 => RetCode.SQLITE_CANTOPEN,
                15 => RetCode.SQLITE_PROTOCOL,
                16 => RetCode.SQLITE_EMPTY,
                17 => RetCode.SQLITE_SCHEMA,
                18 => RetCode.SQLITE_TOOBIG,
                19 => RetCode.SQLITE_CONSTRAINT,
                20 => RetCode.SQLITE_MISMATCH,
                21 => RetCode.SQLITE_MISUSE,
                22 => RetCode.SQLITE_NOLFS,
                23 => RetCode.SQLITE_AUTH,
                24 => RetCode.SQLITE_FORMAT,
                25 => RetCode.SQLITE_RANGE,
                26 => RetCode.SQLITE_NOTADB,
                27 => RetCode.SQLITE_NOTICE,
                28 => RetCode.SQLITE_WARNING,
                100 => RetCode.SQLITE_ROW,
                101 => RetCode.SQLITE_DONE,
                _ => extendedErrorCode,
            };
        }

        /// <summary>
        /// Selects the pragma statement for a canonical journal mode.
        /// </summary>
        /// <param name="canonicalJournalMode">
        /// A token already resolved by <see cref="NormalizeJournalMode"/>.
        /// </param>
        /// <returns>The corresponding compile-time constant statement.</returns>
        /// <exception cref="ArgumentException">
        /// The token is not canonical, which can only happen if a caller bypasses normalisation.
        /// </exception>
        /// <remarks>
        /// THIS FUNCTION IS WHY NOTHING IS INTERPOLATED INTO SQL ANYWHERE IN THIS FILE. A pragma
        /// keyword is a keyword and cannot be a bound parameter, so the naive implementation
        /// concatenates the configured token into the statement text. Selecting a whole constant
        /// statement from a closed set instead means the executed SQL is fixed at compile time and no
        /// configured string ever reaches the parser - which is a stronger guarantee than validating
        /// the token and then splicing it, and it is what makes the enterprise baseline's
        /// parameterized-SQL requirement satisfiable in a place where parameters do not exist.
        /// </remarks>
        internal static string JournalStatementFor(string canonicalJournalMode)
        {
            return canonicalJournalMode switch
            {
                "DELETE" => JournalModeDeleteStatement,
                "TRUNCATE" => JournalModeTruncateStatement,
                "PERSIST" => JournalModePersistStatement,
                "MEMORY" => JournalModeMemoryStatement,
                "WAL" => JournalModeWalStatement,
                "OFF" => JournalModeOffStatement,
                _ => throw new ArgumentException(
                    "A canonical journal mode is required - one of "
                    + "DELETE, TRUNCATE, PERSIST, MEMORY, WAL or OFF, upper case. Resolve the "
                    + "configured value through "
                    + nameof(NormalizeJournalMode)
                    + " before selecting a statement for it.",
                    nameof(canonicalJournalMode)),
            };
        }

        /// <summary>
        /// Selects the pragma statement for an integrity-check mode.
        /// </summary>
        /// <param name="check">A mode already confirmed by <see cref="ValidateIntegrityCheckMode"/>.</param>
        /// <returns>The corresponding compile-time constant statement.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The mode is outside the enumeration.</exception>
        /// <remarks>
        /// The mapping the legacy comment states at
        /// <c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L454</c>: the bare parameter selects the
        /// complete check, and the <c>quick</c> value selects the cheaper one. Absence is handled by
        /// the caller, which executes no statement at all rather than asking this function for one.
        /// </remarks>
        internal static string IntegrityCheckStatementFor(SqliteIntegrityCheckMode check)
        {
            return check switch
            {
                SqliteIntegrityCheckMode.Full => IntegrityCheckStatement,
                SqliteIntegrityCheckMode.Quick => QuickCheckStatement,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(check),
                    check,
                    "The integrity-check mode must be "
                    + nameof(SqliteIntegrityCheckMode.Full)
                    + " or "
                    + nameof(SqliteIntegrityCheckMode.Quick)
                    + ". Absence is handled by executing no statement, not by selecting one."),
            };
        }

        // ==========================================================================================
        //  THE CONNECTION LIFECYCLE
        //
        //  Every member here serialises on the asynchronous gate, because SqliteConnection is not
        //  thread safe and this type is resolved once for the process the way the legacy global
        //  auto-instance was. The `*Core` helpers assume the gate is ALREADY held and are private for
        //  that reason; nothing outside this region calls one.
        // ==========================================================================================

        /// <summary>
        /// Opens the ambient connection if it is not already open, then applies the two framework
        /// extension parameters as pragmas.
        /// </summary>
        /// <param name="cancellationToken">
        /// Cancels the open and the pragmas. This is the portable stand-in for the legacy
        /// <c>SetCancelEvent(readonly ulong hevent)</c> [<c>n_sqlite.sru:L11</c>], whose argument is a
        /// Win32 event handle with no equivalent on a Linux container.
        /// </param>
        /// <returns>
        /// <see cref="RetCode.OK"/> on success, including when the connection was already open;
        /// <see cref="RetCode.E_DB_ERROR"/> when the provider refuses, when the engine will not accept
        /// the configured journal mode, or when the integrity check does not answer <c>ok</c>. On
        /// failure <see cref="SqlCode"/>, <see cref="SqlDbCode"/>, <see cref="SqlErrText"/> and
        /// <see cref="LastError"/> carry the detail, exactly as the legacy accessors did
        /// [<c>n_sqlite.sru:L26-L29</c>], and the fixture's own error path reads
        /// [<c>w_test_sqlite.srw:L456-L459</c>].
        /// </returns>
        /// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
        /// <exception cref="InvalidOperationException">
        /// The configured data directory cannot be created. That is a structural fault - the service
        /// has no storage at all - so it throws rather than returning a code.
        /// </exception>
        /// <remarks>
        /// <para>
        /// IDEMPOTENT, reproducing the guard the fixture applies before it does anything else:
        /// <c>if sqlitedb.IsOpened() then return</c> [<c>w_test_sqlite.srw:L448</c>]. A second call
        /// on an open connection is a success and does nothing, so a caller need not track state.
        /// </para>
        /// <para>
        /// WHAT THIS METHOD DOES NOT DO, and the reason it is stated here as well as in the file
        /// header: it does not delete, truncate, drop, recreate or reseed anything. The fixture's
        /// <c>FileDelete("test.db")</c> [<c>:L450</c>] sits three lines above the grammar ported here
        /// and is the harness's setup, not framework behaviour. Reproducing it would break the
        /// paired-capture parity rule on every restart, because the legacy-side and target-side
        /// recordings for one workflow identifier must be taken against the same volume state with
        /// the volume neither recreated nor reseeded between them.
        /// </para>
        /// <para>
        /// The single filesystem mutation it DOES perform is <c>Directory.CreateDirectory</c> on the
        /// configured data directory: idempotent, additive, non-destructive, and necessary because
        /// the create mode creates a missing FILE but not a missing parent DIRECTORY.
        /// </para>
        /// </remarks>
        public async ValueTask<long> OpenAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (IsOpened)
                {
                    // The idempotent-open guard of w_test_sqlite.srw:L448, reproduced. Nothing is
                    // re-applied: re-running the integrity check on every call would turn a cheap
                    // guard into an expensive one, and re-setting the journal mode inside a
                    // transaction the caller may since have opened would fail for no reason.
                    RecordSuccess(0);
                    return RetCode.OK;
                }

                return await OpenCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Closes the ambient connection, discarding any unresolved ambient transaction.
        /// </summary>
        /// <param name="cancellationToken">Cancels only the wait for the gate.</param>
        /// <returns><see cref="RetCode.OK"/>. Closing something already closed is a success.</returns>
        /// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
        /// <remarks>
        /// <para>
        /// The substitute for <c>n_sqlite.Close()</c> [<c>n_sqlite.sru:L19</c>], whose two call sites
        /// in the fixture are the window's own teardown [<c>w_test_sqlite.srw:L115</c>] and its
        /// DISCONNECT button [<c>:L430</c>]. It is asynchronous where the legacy is synchronous
        /// because everything it disposes exposes an asynchronous release and because a synchronous
        /// wait on this type's asynchronous gate would be a deadlock hazard for no benefit.
        /// </para>
        /// <para>
        /// AN UNRESOLVED AMBIENT TRANSACTION IS ROLLED BACK, NEVER COMMITTED. Disposing a
        /// transaction that has not been committed rolls it back, which is the safe direction and the
        /// only one this file may take: committing on close would be a data-affecting decision, and
        /// the decision of whether to commit or roll back belongs to the transaction contract, whose
        /// legacy rule is documented on <c>AutoCommit()</c> [<c>n_sqlite.sru:L22</c>]. No partial
        /// work is silently persisted here. And nothing is deleted: closing a connection removes no
        /// data and no file.
        /// </para>
        /// </remarks>
        public async ValueTask<long> CloseAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await CloseCoreAsync().ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Opens and fully configures a NEW connection that the caller owns and must dispose.
        /// </summary>
        /// <param name="cancellationToken">Cancels the open and the pragmas.</param>
        /// <returns>An open connection with the configured journal mode and integrity check applied.</returns>
        /// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
        /// <exception cref="InvalidOperationException">
        /// The data directory cannot be created, the engine will not accept the configured journal
        /// mode, or the integrity check did not answer <c>ok</c>.
        /// </exception>
        /// <exception cref="SqliteException">The provider refused to open the database.</exception>
        /// <remarks>
        /// <para>
        /// The factory half of this type, for the components that need a connection of their own -
        /// the worker-affine task layer, whose legacy classes carry mandatory thread affinity in
        /// their own source comments, and any unit of work that must not share a transaction with
        /// the ambient one.
        /// </para>
        /// <para>
        /// THE ONE MEMBER THAT TAKES NO GATE, deliberately: it mutates no shared state, records no
        /// error into this instance and returns an object nobody else can see. That is also why it
        /// reports failure by THROWING rather than by returning a code - its return value is a
        /// connection, so there is no code channel, and handing back a half-configured connection
        /// would be exactly the silent degradation the fail-fast posture forbids.
        /// </para>
        /// </remarks>
        public async ValueTask<SqliteConnection> CreateOpenConnectionAsync(
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            EnsureDataDirectoryExists();

            SqliteConnection connection = new(BuildConnectionString(GetTimeoutSeconds()));
            try
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                ExtensionParameterOutcome outcome = await ApplyExtensionParametersAsync(
                    connection,
                    _journalMode,
                    _check,
                    cancellationToken).ConfigureAwait(false);

                if (outcome.Code != RetCode.OK)
                {
                    throw new InvalidOperationException(outcome.Message);
                }
            }
            catch
            {
                // The caller never receives a connection it did not ask for, so a failure anywhere
                // above releases the one opened here before the fault leaves this method.
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            return connection;
        }

        // ==========================================================================================
        //  THE CONNECTION-LEVEL SETTINGS - reproduced where they are meaningful, and no further
        // ==========================================================================================

        /// <summary>
        /// The command timeout in SECONDS, reproducing <c>n_sqlite.GetTimeout()</c>
        /// [<c>n_sqlite.sru:L12</c>].
        /// </summary>
        /// <returns>The current timeout, in seconds.</returns>
        /// <remarks>
        /// SECONDS ON BOTH SIDES, WITH NO CONVERSION. The legacy signature is
        /// <c>SetTimeout(readonly long sec)</c> [<c>:L13</c>] and the provider's own default-timeout
        /// setting is likewise in seconds, so the two map one to one; treating either as
        /// milliseconds would silently change every command's patience by three orders of magnitude.
        /// The initial value is the provider's own default rather than a number invented here,
        /// because the legacy publishes no initial timeout anywhere.
        /// </remarks>
        public long GetTimeout()
        {
            // An aligned 32-bit read is atomic, so this needs no gate; the volatile read is what
            // guarantees a value written by another thread is observed rather than a cached one.
            return Volatile.Read(ref _timeoutSeconds);
        }

        /// <summary>
        /// Sets the command timeout in SECONDS, reproducing <c>n_sqlite.SetTimeout(readonly long sec)</c>
        /// [<c>n_sqlite.sru:L13</c>].
        /// </summary>
        /// <param name="sec">
        /// The timeout in seconds. Zero means no timeout, which is the provider's own convention.
        /// </param>
        /// <returns>
        /// <see cref="RetCode.OK"/>, or <see cref="RetCode.E_INVALID_ARGUMENT"/> when the value is
        /// negative or larger than the provider's setting can hold.
        /// </returns>
        /// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
        /// <remarks>
        /// Applied to the ambient connection immediately when one is open, and carried into every
        /// connection opened afterwards - including those from
        /// <see cref="CreateOpenConnectionAsync"/> - through <see cref="ConnectionString"/>. The
        /// parameter keeps the legacy's own name so the correspondence is unambiguous.
        /// </remarks>
        public long SetTimeout(long sec)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (sec < 0 || sec > int.MaxValue)
            {
                return RetCode.E_INVALID_ARGUMENT;
            }

            // Gated because it touches the ambient connection as well as the field. This is a setup
            // operation - the legacy calls it while wiring a connection up, not per statement - so
            // the synchronous wait costs nothing on the request path. No member of this type calls
            // it while already holding the gate, which is what keeps the wait deadlock-free.
            _gate.Wait();
            try
            {
                int seconds = (int)sec;
                Volatile.Write(ref _timeoutSeconds, seconds);

                if (_connection is not null)
                {
                    _connection.DefaultTimeout = seconds;
                }

                return RetCode.OK;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Turns auto-commit on or off, reproducing
        /// <c>n_sqlite.SetAutoCommit(readonly boolean enabled)</c> [<c>n_sqlite.sru:L15</c>].
        /// </summary>
        /// <param name="enabled">
        /// <see langword="true"/> for auto-commit - no ambient transaction held;
        /// <see langword="false"/> to hold one.
        /// </param>
        /// <param name="cancellationToken">Cancels the gate wait and the transaction start.</param>
        /// <returns>
        /// <see cref="RetCode.OK"/> on success, or <see cref="RetCode.E_INVALID_TRANSACTION"/> when
        /// the connection is not open or when turning auto-commit ON while an ambient transaction is
        /// still held.
        /// </returns>
        /// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
        /// <remarks>
        /// <para>
        /// The sequence this reproduces is the fixture's: on immediately after a successful open
        /// [<c>w_test_sqlite.srw:L461</c>], and off again after the schema statement [<c>:L473</c>].
        /// Turning it off twice is a success and starts nothing new.
        /// </para>
        /// <para>
        /// TURNING IT ON WHILE A TRANSACTION IS HELD IS REFUSED, AND THAT IS THE INTERESTING
        /// DECISION. Doing so would require resolving the open transaction, and whether an
        /// unresolved transaction commits or rolls back is precisely the legacy's <c>AutoCommit()</c>
        /// rule - commit when the status code is zero, otherwise roll back, and roll back too when
        /// the commit itself fails [<c>n_sqlite.sru:L22</c>] - which belongs to
        /// <c>Transactions/</c> and the transaction contract, not here. Guessing either direction
        /// would make a data-affecting decision in the wrong component; committing would be worse
        /// than rolling back, because it would persist work nobody asked to persist. So this refuses
        /// with a defined code and the caller resolves the transaction through the component that
        /// owns resolving it - reachable from here as <see cref="AmbientTransaction"/>. That is the
        /// migration's own rule for a behaviour that cannot cross the boundary intact: narrow the
        /// contract with a defined error rather than widen it with a guess.
        /// </para>
        /// </remarks>
        public async ValueTask<long> SetAutoCommitAsync(
            bool enabled,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_connection is not { State: ConnectionState.Open } connection)
                {
                    return RecordFailure(
                        RetCode.E_INVALID_TRANSACTION,
                        RetCode.SQLITE_MISUSE,
                        "The auto-commit state cannot be changed before the connection is opened. "
                        + "Open it first; the legacy sequence sets auto-commit immediately after a "
                        + "successful open.");
                }

                if (enabled)
                {
                    if (_ambientTransaction is not null)
                    {
                        return RecordFailure(
                            RetCode.E_INVALID_TRANSACTION,
                            RetCode.SQLITE_MISUSE,
                            "Auto-commit cannot be turned on while an ambient transaction is still "
                            + "held, because that would require committing or rolling it back and "
                            + "that decision belongs to the transaction contract rather than to the "
                            + "connection. Resolve the transaction first, then turn auto-commit on.");
                    }

                    RecordSuccess(0);
                    return RetCode.OK;
                }

                if (_ambientTransaction is null)
                {
                    _ambientTransaction = (SqliteTransaction)await connection
                        .BeginTransactionAsync(cancellationToken)
                        .ConfigureAwait(false);
                }

                RecordSuccess(0);
                return RetCode.OK;
            }
            catch (SqliteException exception)
            {
                return RecordProviderFailure(exception);
            }
            finally
            {
                _gate.Release();
            }
        }

        // ==========================================================================================
        //  REACHABILITY AND CATALOGUE LOOKUP - what the readiness probe is built from
        // ==========================================================================================

        /// <summary>
        /// Reports whether a table exists in the database this connection opened, reproducing
        /// <c>n_sqlite.IsTableExists(readonly string table)</c> [<c>n_sqlite.sru:L30</c>].
        /// </summary>
        /// <param name="table">The table name. Compared as a value, never spliced into the statement.</param>
        /// <param name="cancellationToken">Cancels the lookup.</param>
        /// <returns><see langword="true"/> when the table exists.</returns>
        /// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
        /// <exception cref="ArgumentException"><paramref name="table"/> is blank.</exception>
        /// <exception cref="InvalidOperationException">The connection is not open.</exception>
        /// <remarks>
        /// A special case of the two-argument overload rather than a second implementation, asking
        /// about the connection's own schema.
        /// </remarks>
        public ValueTask<bool> IsTableExistsAsync(
            string table,
            CancellationToken cancellationToken = default)
        {
            return IsTableExistsAsync(MainSchemaName, table, cancellationToken);
        }

        /// <summary>
        /// Reports whether a table exists in a named schema, reproducing
        /// <c>n_sqlite.IsTableExists(readonly string db, readonly string table)</c>
        /// [<c>n_sqlite.sru:L31</c>].
        /// </summary>
        /// <param name="db">
        /// The schema name, keeping the legacy parameter's own spelling. Compared as a VALUE against
        /// SQLite's catalogue rather than spliced into the statement as an identifier.
        /// </param>
        /// <param name="table">The table name, likewise compared as a value.</param>
        /// <param name="cancellationToken">Cancels the lookup.</param>
        /// <returns><see langword="true"/> when the table exists in that schema.</returns>
        /// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
        /// <exception cref="ArgumentException">Either name is blank.</exception>
        /// <exception cref="InvalidOperationException">The connection is not open.</exception>
        /// <remarks>
        /// <para>
        /// FULLY PARAMETERIZED, WHICH IS WHY THE CATALOGUE FUNCTION IS USED INSTEAD OF A
        /// SCHEMA-QUALIFIED CATALOGUE TABLE. A schema is an identifier and an identifier cannot be
        /// bound, so the obvious query would have to concatenate the caller's schema string into the
        /// statement and lean on quoting. Filtering the catalogue function on a schema COLUMN keeps
        /// both inputs values, so this file interpolates nothing into SQL.
        /// </para>
        /// <para>
        /// A PROVIDER FAILURE ANSWERS FALSE AND IS RECORDED. The legacy member returns a boolean and
        /// therefore cannot itself distinguish "the table is absent" from "the question could not be
        /// asked"; the return shape is preserved rather than widened, and the discrimination is added
        /// through <see cref="SqlCode"/> and <see cref="LastError"/>, which a caller that cares can
        /// read. An unknown schema is not a failure at all - it simply matches nothing.
        /// </para>
        /// </remarks>
        public async ValueTask<bool> IsTableExistsAsync(
            string db,
            string table,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentException.ThrowIfNullOrWhiteSpace(db);
            ArgumentException.ThrowIfNullOrWhiteSpace(table);

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_connection is not { State: ConnectionState.Open } connection)
                {
                    throw new InvalidOperationException(
                        "The connection is not open, so the catalogue cannot be queried. Call "
                        + nameof(OpenAsync)
                        + " first - it is idempotent, so calling it when the connection is already "
                        + "open costs nothing.");
                }

                // Through the SHARED helper, so this member and the readiness probe ask the catalogue
                // through one statement and one parameter binding rather than two that could drift. The
                // trimming stays HERE because it belongs to this member's published contract, not to the
                // helper - the probe's own inputs are compile-time constants with nothing to trim.
                bool exists = await TableExistsOnAsync(
                    connection,
                    db.Trim(),
                    table.Trim(),
                    cancellationToken).ConfigureAwait(false);

                RecordSuccess(exists ? 1L : 0L);
                return exists;
            }
            catch (SqliteException exception)
            {
                RecordProviderFailure(exception);
                return false;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Reports whether the storage engine is reachable, opening the connection first if
        /// necessary.
        /// </summary>
        /// <param name="maximumCacheAge">
        /// How stale a previous answer may be before it is re-measured.
        /// <see cref="TimeSpan.Zero"/> disables caching and probes every time. Age is measured on
        /// the injected clock.
        /// </param>
        /// <param name="cancellationToken">Cancels the probe.</param>
        /// <returns>
        /// <see langword="true"/> when the engine answered. On <see langword="false"/>,
        /// <see cref="SqlDbCode"/>, <see cref="SqlErrText"/> and <see cref="LastError"/> carry the
        /// reason.
        /// </returns>
        /// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="maximumCacheAge"/> is negative.</exception>
        /// <remarks>
        /// <para>
        /// The shape the anonymous readiness route needs: a single boolean that means "this service
        /// can reach its only storage engine", with the detail available separately for a log record.
        /// It executes a constant, parameterless statement, so it proves the ENGINE answered rather
        /// than merely that a file handle opened - and it reads no schema, so it works against an
        /// empty database as well as a populated one.
        /// </para>
        /// <para>
        /// THE CACHE WINDOW IS AN ARGUMENT, NOT A CONSTANT, and that is deliberate. A hardcoded
        /// duration here would be a number with no evidence behind it anywhere in the legacy, and a
        /// readiness gate's tolerance for staleness is the caller's business rather than this file's.
        /// The window is measured with the injected clock and never with an ambient one, because
        /// every clock read is a determinism seam that a characterization run has to be able to mask
        /// from both the master and the candidate recording.
        /// </para>
        /// <para>
        /// A FAILED PROBE INVALIDATES THE CACHE rather than being remembered for the window: a
        /// service that has just recovered should be reported as reachable at the next ask, not at
        /// the end of someone else's staleness budget.
        /// </para>
        /// </remarks>
        public async ValueTask<bool> IsReachableAsync(
            TimeSpan maximumCacheAge,
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (maximumCacheAge < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumCacheAge),
                    maximumCacheAge,
                    "A cache window cannot be negative. Pass TimeSpan.Zero to probe every time.");
            }

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // THE WINDOW IS DECIDED ON ELAPSED TIME, THE RECORD IS KEPT ON WALL TIME. Both reads
                // happen here, once, so the two can never disagree about which probe they describe.
                // The comparison boundary is unchanged - still `age <= maximumCacheAge`, so an age
                // exactly equal to the window still serves the cached answer - and only the quantity
                // being compared changed, from a difference of two wall-clock reads to a genuinely
                // monotonic elapsed time.
                DateTimeOffset now = _timeProvider.GetUtcNow();
                long timestamp = _timeProvider.GetTimestamp();

                if (maximumCacheAge > TimeSpan.Zero
                    && _lastReachability is true
                    && _lastReachabilityTimestamp is long probedAtTimestamp
                    && _timeProvider.GetElapsedTime(probedAtTimestamp, timestamp) <= maximumCacheAge)
                {
                    return true;
                }

                bool reachable = await ProbeReadinessCoreAsync(cancellationToken).ConfigureAwait(false);

                // Only a success is remembered. A failure is deliberately not cached, so a service
                // that has just recovered is reported healthy at the very next ask rather than at
                // the end of somebody else's staleness budget.
                _lastReachability = reachable ? true : null;
                _lastReachabilityAt = now;
                _lastReachabilityTimestamp = timestamp;

                return reachable;
            }
            finally
            {
                _gate.Release();
            }
        }

        // ==========================================================================================
        //  THE PRIVATE MECHANICS
        //
        //  Every `*Core` member below assumes the gate is ALREADY HELD by its caller and mutates this
        //  instance's recorded status, which is exactly why none of them is reachable from outside.
        //  ApplyExtensionParametersAsync is the deliberate exception: it is STATIC and returns its
        //  outcome instead of recording it, so the un-gated CreateOpenConnectionAsync can share it
        //  without touching shared state.
        // ==========================================================================================

        /// <summary>
        /// The result of applying the two framework extension parameters to a connection.
        /// </summary>
        /// <param name="Code">
        /// <see cref="RetCode.OK"/> on success, otherwise <see cref="RetCode.E_DB_ERROR"/>.
        /// </param>
        /// <param name="ProviderCode">
        /// The preserved <c>SQLITE_*</c> constant that best names the cause: the generic error for a
        /// journal mode the engine refused, and the malformed-image code for a failed integrity
        /// check, which is precisely what that check exists to detect.
        /// </param>
        /// <param name="Message">
        /// The diagnostic text on failure, or an empty string. It names journal modes and check
        /// results only - never a statement, and never a credential.
        /// </param>
        /// <remarks>
        /// Nested and private on purpose. The sibling test project imports this whole namespace as a
        /// global using, so every type declared in it becomes ambient across every test file; a
        /// nested private type adds nothing to the namespace and so can collide with nothing.
        /// </remarks>
        private readonly record struct ExtensionParameterOutcome(
            long Code,
            long ProviderCode,
            string Message);

        /// <summary>
        /// Opens the ambient connection and applies the extension parameters. The gate must be held.
        /// </summary>
        private async ValueTask<long> OpenCoreAsync(CancellationToken cancellationToken)
        {
            // THE ONE PERMITTED FILESYSTEM MUTATION IN THIS FILE. Idempotent, additive and
            // non-destructive: the create mode brings the database FILE into being but not a missing
            // parent DIRECTORY, so without this an otherwise correct deployment fails on first run.
            // Nothing here removes, replaces, truncates or reseeds anything.
            EnsureDataDirectoryExists();

            SqliteConnection connection = new(BuildConnectionString(GetTimeoutSeconds()));

            try
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                ExtensionParameterOutcome outcome = await ApplyExtensionParametersAsync(
                    connection,
                    _journalMode,
                    _check,
                    cancellationToken).ConfigureAwait(false);

                if (outcome.Code != RetCode.OK)
                {
                    await connection.DisposeAsync().ConfigureAwait(false);

                    long failed = RecordFailure(
                        outcome.Code,
                        outcome.ProviderCode,
                        outcome.Message);

                    // C-F. NEITHER THE PATH NOR THE FILE NAME ON A FAILURE ARM, and the provider's reason
                    // through the redactor. The success record beside this one DOES name the database,
                    // because "which database opened" is what an operator reads it for and the name is
                    // already in the settings file that chose it. A FAILURE record is different in who
                    // ends up reading it: it is the record that gets quoted into a ticket, pasted into a
                    // chat and shipped to a vendor, and against the provider code it adds nothing an
                    // operator did not configure while adding storage layout for every other reader. This
                    // deployment opens exactly ONE database, so omitting the name loses no ability to
                    // tell two failures apart. The reason is composed BY the provider FROM the statement
                    // or pragma it was executing, so it quotes values back; masking is
                    // content-preserving for the diagnostic itself, so the code and the pragma name
                    // survive while a quoted value does not. The full text stays available in process on
                    // SqlErrText, so an authenticated surface is as diagnosable as it ever was.
                    _logger.LogError(
                        "The SQLite database opened but could not be configured: {Reason}",
                        SqlRedactor.Instance.Redact(outcome.Message));

                    return failed;
                }
            }
            catch (SqliteException exception)
            {
                await connection.DisposeAsync().ConfigureAwait(false);

                long failed = RecordProviderFailure(exception);

                // C-F, on the same terms as the record above and for the same reasons. A failed open is
                // also the record most likely to carry a path INSIDE the provider's own message -
                // "unable to open database file" arrives with the data source attached - which the
                // redactor masks along with any quoted value. What locates the cause is the PRESERVED
                // SQLITE_* constant, which distinguishes a missing file from a permission refusal from a
                // corrupt header without naming any of them.
                _logger.LogError(
                    "The SQLite database could not be opened. Provider code {SqlDbCode}: {SqlErrText}",
                    _sqlDbCode,
                    SqlRedactor.Instance.Redact(_sqlErrText));

                return failed;
            }
            catch
            {
                // Cancellation and every other fault release the half-open connection before
                // leaving, so a failed open can never strand a handle on the data file.
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            _connection = connection;
            RecordSuccess(0);

            // The composed legacy URI is logged rather than the connection string, because the URI is the
            // parity artefact and is the value a recording carries. Neither it nor the file name can carry
            // a credential - the legacy password is the second argument of the open call rather than URI
            // syntax [ws_objects/pfw.utility.sqlite.pbl.src/n_sqlite.sru:L18], and ComposeLegacyUri emits
            // no password parameter in any form - and neither is a statement.
            //
            // THE RESOLVED PATH IS DELIBERATELY NOT HERE. It is the one value in this record that is about
            // the deployment rather than about the database, and a startup log that publishes where storage
            // is mounted tells a reader of the log something they did not need in order to read it. The
            // PARITY form of the URI already carries the bare file name, so the identity survives; the
            // deployment form, which does embed the directory, is available on the factory for a caller
            // that genuinely needs it and is not written here.
            _logger.LogInformation(
                "Opened SQLite database {Database} for legacy URI {LegacyUri}. Journal mode "
                + "{JournalMode}, integrity check {IntegrityCheck}, command timeout {TimeoutSeconds}s.",
                DatabaseFileName,
                LegacyUri,
                _journalMode,
                _check?.ToString() ?? "absent",
                GetTimeoutSeconds());

            return RetCode.OK;
        }

        /// <summary>
        /// Releases the ambient transaction and connection. The gate must be held.
        /// </summary>
        private async ValueTask<long> CloseCoreAsync()
        {
            SqliteTransaction? transaction = _ambientTransaction;
            _ambientTransaction = null;

            if (transaction is not null)
            {
                // Disposing an uncommitted transaction ROLLS IT BACK. That is the only direction this
                // file may take: committing would persist work nobody asked to persist, and the
                // commit-or-roll-back decision belongs to the transaction contract.
                await transaction.DisposeAsync().ConfigureAwait(false);
            }

            SqliteConnection? connection = _connection;
            _connection = null;

            if (connection is not null)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }

            // A closed connection tells us nothing about reachability any more, so the cached
            // positive answer is dropped. The WALL-CLOCK stamp is left alone - it records when a probe
            // last ran, which remains true - but the MONOTONIC stamp goes with the answer it gates,
            // because leaving a live stamp beside a cleared answer would be a pair that no longer
            // describes the same thing.
            _lastReachability = null;
            _lastReachabilityTimestamp = null;

            RecordSuccess(0);

            _logger.LogDebug("Closed the SQLite connection to {Database}.", DatabaseFileName);

            return RetCode.OK;
        }

        /// <summary>
        /// Establishes readiness WITHOUT ALTERING ANYTHING. The gate must be held.
        /// </summary>
        /// <param name="cancellationToken">The caller's token, already budget-bounded by the check.</param>
        /// <returns>
        /// <see langword="true"/> only when the engine answered AND the required schema is present.
        /// </returns>
        /// <remarks>
        /// <para>
        /// <b>THE READINESS PATH IS NOT THE RUNTIME PATH, AND THAT SEPARATION IS THE WHOLE POINT OF THIS
        /// MEMBER.</b> The runtime open is deliberately creative: it brings the data directory into being
        /// if it is absent, opens with the legacy <c>mode=rwc</c> grammar so the database FILE is created
        /// on first run, sets the journal mode, and can run an integrity check - each of which is correct
        /// for a service starting up, and none of which an ANONYMOUS caller may provoke. Reaching that
        /// path from <c>/health</c> meant an unauthenticated request could create a directory, create a
        /// database file and write a journal, which is a data mutation driven from an unauthenticated
        /// surface. So readiness gets its own path that creates nothing.
        /// </para>
        /// <para>
        /// <b>IT NEVER OPENS THE AMBIENT CONNECTION, AND IT NEVER CLOSES ONE EITHER.</b> When the runtime
        /// has already opened, the probe borrows that connection: it is the handle requests actually run
        /// on, so proving IT answers is a truer readiness statement than proving some other handle does,
        /// and borrowing costs no file descriptor. When the runtime has NOT opened, the probe opens a
        /// short-lived handle of its own in <see cref="SqliteOpenMode.ReadOnly"/> and disposes it before
        /// returning - it is never stored in <c>_connection</c>, so a probe can never leave the ambient
        /// connection in a state the runtime path did not choose. A read-only open of a database that
        /// does not exist FAILS rather than creating it, which is exactly the answer wanted: no storage
        /// means not ready, and nothing is brought into being by asking.
        /// </para>
        /// <para>
        /// <b>WHY A CONSTANT SCALAR IS NOT ENOUGH ON ITS OWN.</b> <c>SELECT 1</c> proves the ENGINE
        /// answered rather than merely that a file handle opened, which is why it is still issued first.
        /// But it passes just as happily against an empty database, so a service whose volume mounted
        /// correctly and whose migrations never ran would report READY and then fail every request. The
        /// schema check is what closes that gap, and it is a read of SQLite's own catalogue through
        /// <c>pragma_table_list</c> with both inputs BOUND, so it writes nothing and interpolates
        /// nothing.
        /// </para>
        /// <para>
        /// Every failure arm records its cause on <see cref="SqlCode"/>, <see cref="SqlDbCode"/> and
        /// <see cref="LastError"/> for the in-process readers, and sets
        /// <see cref="LastReadiness"/> so the caller can distinguish an unreachable engine from a
        /// reachable one with no schema. Nothing is logged here: this path is anonymous, so what may be
        /// recorded is decided by the arms in <c>OpenCoreAsync</c> and by the health check itself
        /// (constraint C-F).
        /// </para>
        /// </remarks>
        private async ValueTask<bool> ProbeReadinessCoreAsync(CancellationToken cancellationToken)
        {
            SqliteConnection? borrowed = _connection is { State: ConnectionState.Open } ambient
                ? ambient
                : null;

            SqliteConnection? opened = null;

            try
            {
                if (borrowed is null)
                {
                    // READ-ONLY, AND NOT THROUGH BuildConnectionString - that composer applies the
                    // CONFIGURED open mode, which is the creative one. This is the single place in this
                    // file that composes a connection string with a mode of its own, and it is confined
                    // to this member so no other path can acquire a read-only handle by accident.
                    SqliteConnectionStringBuilder builder = new()
                    {
                        DataSource = DatabasePath,
                        Mode = SqliteOpenMode.ReadOnly,
                        DefaultTimeout = GetTimeoutSeconds(),
                    };

                    opened = new SqliteConnection(builder.ConnectionString);

                    await opened.OpenAsync(cancellationToken).ConfigureAwait(false);
                }

                SqliteConnection connection = borrowed ?? opened!;

                string? answer = await ExecuteScalarTextAsync(
                    connection,
                    ReachabilityProbeStatement,
                    cancellationToken).ConfigureAwait(false);

                if (answer is null)
                {
                    RecordFailure(
                        RetCode.E_DB_ERROR,
                        RetCode.SQLITE_ERROR,
                        "The reachability probe returned no value, so the engine did not answer.");

                    _lastReadiness = StorageReadiness.Unreachable;

                    return false;
                }

                // BOTH TABLES, AND THE APPLICATION ONE IS TESTED FIRST because it is the one an operator
                // is most likely to be missing and therefore the more useful thing to name.
                if (!await TableExistsOnAsync(
                        connection,
                        MainSchemaName,
                        RequiredApplicationTable,
                        cancellationToken).ConfigureAwait(false))
                {
                    RecordFailure(
                        RetCode.E_DB_ERROR,
                        RetCode.SQLITE_ERROR,
                        "The storage engine answered but the "
                        + RequiredApplicationTable
                        + " table is absent, so the schema has not been provisioned. Apply the migrations "
                        + "with `dotnet ef database update`.");

                    _lastReadiness = StorageReadiness.SchemaIncomplete;

                    return false;
                }

                if (!await TableExistsOnAsync(
                        connection,
                        MainSchemaName,
                        RequiredMigrationHistoryTable,
                        cancellationToken).ConfigureAwait(false))
                {
                    RecordFailure(
                        RetCode.E_DB_ERROR,
                        RetCode.SQLITE_ERROR,
                        "The storage engine answered and the "
                        + RequiredApplicationTable
                        + " table is present, but the migration history table is absent, so the schema "
                        + "was not applied by the migration tool and its version cannot be established.");

                    _lastReadiness = StorageReadiness.SchemaIncomplete;

                    return false;
                }

                RecordSuccess(1);

                _lastReadiness = StorageReadiness.Ready;

                return true;
            }
            catch (SqliteException exception)
            {
                RecordProviderFailure(exception);

                _lastReadiness = StorageReadiness.Unreachable;

                return false;
            }
            finally
            {
                // ONLY EVER THE HANDLE THIS MEMBER OPENED. `borrowed` is the runtime's and is left
                // exactly as it was found - disposing it here would close the connection every request
                // runs on, from an anonymous route.
                if (opened is not null)
                {
                    await opened.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Reports whether a table exists, on a caller-supplied open connection.
        /// </summary>
        /// <param name="connection">An open connection. Not disposed here.</param>
        /// <param name="schema">The schema name. Bound as a VALUE, never interpolated.</param>
        /// <param name="table">The table name. Bound as a VALUE, never interpolated.</param>
        /// <param name="cancellationToken">The caller's token.</param>
        /// <returns><see langword="true"/> when exactly the named table exists in that schema.</returns>
        /// <remarks>
        /// Extracted so the public catalogue member and the readiness probe ask the question through ONE
        /// statement and one parameter-binding, rather than through two that could drift. It takes the
        /// connection as an argument precisely because the two callers hold different ones: the public
        /// member uses the ambient connection, the probe may be using a short-lived read-only handle. It
        /// acquires no gate and records nothing - both are the caller's business.
        /// </remarks>
        private static async ValueTask<bool> TableExistsOnAsync(
            SqliteConnection connection,
            string schema,
            string table,
            CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = TableExistsStatement;
            command.Parameters.AddWithValue("$schema", schema);
            command.Parameters.AddWithValue("$name", table);

            object? scalar = await command
                .ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false);

            long matches = scalar is null or DBNull
                ? 0L
                : Convert.ToInt64(scalar, CultureInfo.InvariantCulture);

            return matches > 0;
        }

        /// <summary>
        /// Applies the framework's two extension parameters as pragmas and verifies both results.
        /// </summary>
        /// <param name="connection">An open connection.</param>
        /// <param name="canonicalJournalMode">The canonical journal-mode token.</param>
        /// <param name="check">The integrity check to run, or <see langword="null"/> to run none.</param>
        /// <param name="cancellationToken">Cancels either pragma.</param>
        /// <returns>The outcome, which the caller records or throws as suits its own contract.</returns>
        /// <remarks>
        /// <para>
        /// THE HEART OF RULING 2. <c>check</c> and <c>journal</c> are the framework's own extension
        /// parameters [<c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L453-L455</c>] and are
        /// meaningless to SQLite, which silently ignores query parameters it does not recognise -
        /// so they are applied here, as pragmas, after the connection is open. Passing them through
        /// inside the data source instead would open a connection that quietly had neither.
        /// </para>
        /// <para>
        /// BOTH RESULTS ARE VERIFIED RATHER THAN ASSUMED. The journal pragma RETURNS the resulting
        /// mode and returns the OLD one when the requested mode cannot be set, so a mismatch is a
        /// silent failure unless the answer is read - it is read, and a mismatch is reported. The
        /// integrity pragma exists precisely to state whether the data file is sound
        /// [<c>:L454</c>], so any answer other than the single word <c>ok</c> is reported too rather
        /// than logged and shrugged off. Its output is bounded by the engine's own row limit, so no
        /// cap of ours is needed or invented.
        /// </para>
        /// <para>
        /// Static, and it records nothing on the instance, which is what lets the un-gated
        /// <see cref="CreateOpenConnectionAsync"/> use it safely.
        /// </para>
        /// </remarks>
        private static async ValueTask<ExtensionParameterOutcome> ApplyExtensionParametersAsync(
            SqliteConnection connection,
            string canonicalJournalMode,
            SqliteIntegrityCheckMode? check,
            CancellationToken cancellationToken)
        {
            string? resultingMode = await ExecuteScalarTextAsync(
                connection,
                JournalStatementFor(canonicalJournalMode),
                cancellationToken).ConfigureAwait(false);

            if (!string.Equals(resultingMode, canonicalJournalMode, StringComparison.OrdinalIgnoreCase))
            {
                return new ExtensionParameterOutcome(
                    RetCode.E_DB_ERROR,
                    RetCode.SQLITE_ERROR,
                    "The engine would not set journal mode '"
                    + canonicalJournalMode
                    + "' and reported '"
                    + (resultingMode ?? "no value")
                    + "' instead. The pragma returns the mode actually in force, so this is a "
                    + "refusal rather than a success - it happens when the requested mode is "
                    + "incompatible with how the database was opened. Configure 'Sqlite:Journal' to "
                    + "a mode this database supports.");
            }

            if (check is null)
            {
                // The third state: no verification statement is executed at all. This is NOT the
                // same as a check that passed, and the two must never be conflated.
                return new ExtensionParameterOutcome(RetCode.OK, RetCode.SQLITE_OK, string.Empty);
            }

            IReadOnlyList<string> findings = await ExecuteTextRowsAsync(
                connection,
                IntegrityCheckStatementFor(check.Value),
                cancellationToken).ConfigureAwait(false);

            bool sound = findings.Count == 1
                && string.Equals(findings[0], IntegrityCheckOk, StringComparison.OrdinalIgnoreCase);

            if (sound)
            {
                return new ExtensionParameterOutcome(RetCode.OK, RetCode.SQLITE_OK, string.Empty);
            }

            return new ExtensionParameterOutcome(
                RetCode.E_DB_ERROR,
                RetCode.SQLITE_CORRUPT,
                "The configured integrity check reported that the data file is not sound: "
                + (findings.Count == 0
                    ? "the check returned no result at all"
                    : string.Join("; ", findings))
                + ". The check exists to detect exactly this, so the open is refused rather than "
                + "continuing against a damaged file.");
        }

        /// <summary>
        /// Creates the configured data directory if it is absent.
        /// </summary>
        /// <exception cref="InvalidOperationException">The directory cannot be created.</exception>
        /// <remarks>
        /// FAIL FAST. A service that cannot create its only data directory has no storage at all, and
        /// the framework's posture for a structural fault is to stop rather than to continue degraded
        /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L111-L144</c>]. The most likely cause in a container is
        /// a mount the image's non-root user cannot write, so the message says so.
        /// </remarks>
        /// <summary>
        /// Brings the configured data directory into being if it is absent.
        /// </summary>
        /// <remarks>
        /// INTERNAL RATHER THAN PRIVATE so the transaction engine can call it on its own RUNTIME connect
        /// path. The engine opens its own connection - <c>IPooledTransaction</c> deliberately publishes no
        /// provider handle for it to borrow - and an otherwise correct deployment would fail on first run
        /// without this, exactly as the factory's own open path would. It stays out of the READINESS path
        /// on purpose: an anonymous probe must not create storage.
        /// </remarks>
        internal void EnsureDataDirectoryExistsForRuntimeConnect() => EnsureDataDirectoryExists();

        private void EnsureDataDirectoryExists()
        {
            try
            {
                Directory.CreateDirectory(DataDirectory);
            }
            catch (Exception exception) when (exception
                is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or ArgumentException)
            {
                throw new InvalidOperationException(
                    "The configured SQLite data directory '"
                    + DataDirectory
                    + "' does not exist and could not be created, so this service has no storage. It "
                    + "is expected to be the mount point of the persistence-db volume and it must be "
                    + "writable by the container image's non-root user. Note that nothing here "
                    + "removes or replaces existing contents - only a missing directory is created.",
                    exception);
            }
        }

        /// <summary>
        /// Builds the provider connection string for a given timeout in seconds.
        /// </summary>
        /// <remarks>
        /// <para>
        /// THREE SETTINGS, AND DELIBERATELY NO FOURTH. The data source, the mapped open mode and the
        /// command timeout in seconds. <c>Password</c> IS NEVER ASSIGNED: a configured password is
        /// refused at construction because encrypted SQLite is out of scope for this phase, so this
        /// builder has nothing to put there - which is what makes the returned string safe to log.
        /// </para>
        /// <para>
        /// Neither <c>check</c> nor <c>journal</c> appears either. They are the framework's extension
        /// parameters, they mean nothing to SQLite, and they are applied as pragmas after the open.
        /// </para>
        /// </remarks>
        private string BuildConnectionString(int timeoutSeconds)
        {
            SqliteConnectionStringBuilder builder = new()
            {
                DataSource = DatabasePath,
                Mode = _openMode,
                DefaultTimeout = timeoutSeconds,
            };

            return builder.ConnectionString;
        }

        /// <summary>
        /// The current command timeout in seconds, read atomically.
        /// </summary>
        private int GetTimeoutSeconds()
        {
            return Volatile.Read(ref _timeoutSeconds);
        }

        /// <summary>
        /// Records a successful connection-level operation. The gate must be held.
        /// </summary>
        private void RecordSuccess(long rowCount)
        {
            _sqlCode = RetCode.OK;
            _sqlDbCode = RetCode.SQLITE_OK;
            _sqlNRows = rowCount;
            _sqlErrText = string.Empty;
            _lastError = DbErrorData.Empty;
        }

        /// <summary>
        /// Records a provider exception and returns the code the caller should return.
        /// </summary>
        /// <remarks>
        /// The provider's numeric result is translated through the preserved <c>SQLITE_*</c>
        /// catalogue rather than passed through unexamined, so the value a caller sees on
        /// <see cref="SqlDbCode"/> is a constant the ported algebra recognises.
        /// </remarks>
        private long RecordProviderFailure(SqliteException exception)
        {
            return RecordFailure(
                RetCode.E_DB_ERROR,
                MapSqliteResultCode(exception.SqliteErrorCode, exception.SqliteExtendedErrorCode),
                exception.Message);
        }

        /// <summary>
        /// Records a failed connection-level operation. The gate must be held.
        /// </summary>
        /// <param name="returnCode">The negative code the caller returns.</param>
        /// <param name="providerCode">The preserved <c>SQLITE_*</c> constant for the cause.</param>
        /// <param name="message">The diagnostic text. Never a statement and never a credential.</param>
        /// <returns><paramref name="returnCode"/>, so a caller can record and return in one line.</returns>
        /// <remarks>
        /// <para>
        /// The return code is NEGATIVE by construction, and that matters more than it looks. The
        /// ported predicates read a prevention of 1 as a success and treat a value of at least zero
        /// as succeeded, so returning a positive <c>SQLITE_*</c> code from a failure would make the
        /// failure test as a SUCCESS at every call site. The provider's code therefore travels on
        /// <see cref="SqlDbCode"/>, exactly as the legacy carried it on its own accessor
        /// [<c>n_sqlite.sru:L28</c>], while the return value stays in the negative <c>E_*</c> band.
        /// </para>
        /// <para>
        /// The payload is built through <c>DbErrorData.FromTransaction</c>, which mirrors the legacy
        /// connect-failure site that populates only the code and the text and leaves the statement,
        /// buffer and row cleared. That is also what guarantees no statement can leave this file.
        /// </para>
        /// </remarks>
        private long RecordFailure(long returnCode, long providerCode, string message)
        {
            _sqlCode = RetCode.FAILED;
            _sqlDbCode = providerCode;
            _sqlNRows = 0;
            _sqlErrText = message;
            _lastError = DbErrorData.FromTransaction(providerCode, message);

            return returnCode;
        }

        /// <summary>
        /// Executes a constant statement and returns its single value as text, or
        /// <see langword="null"/> when there is none.
        /// </summary>
        private static async ValueTask<string?> ExecuteScalarTextAsync(
            SqliteConnection connection,
            string statement,
            CancellationToken cancellationToken)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = statement;

            object? value = await command
                .ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false);

            return value is null or DBNull
                ? null
                : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Executes a constant statement and returns its first column, row by row.
        /// </summary>
        /// <remarks>
        /// Used only by the integrity check, which answers with a single <c>ok</c> row when the file
        /// is sound and with one row per finding when it is not. The row count is bounded by the
        /// engine's own limit on that pragma, so no cap is imposed here - imposing one would discard
        /// findings and invent a number the legacy does not have.
        /// </remarks>
        private static async ValueTask<IReadOnlyList<string>> ExecuteTextRowsAsync(
            SqliteConnection connection,
            string statement,
            CancellationToken cancellationToken)
        {
            List<string> rows = [];

            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = statement;

            await using DbDataReader reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(await reader.IsDBNullAsync(0, cancellationToken).ConfigureAwait(false)
                    ? string.Empty
                    : reader.GetString(0));
            }

            return rows;
        }

        // ==========================================================================================
        //  TEARDOWN
        // ==========================================================================================

        /// <summary>
        /// Releases the ambient transaction, the connection and the gate.
        /// </summary>
        /// <remarks>
        /// <see cref="DisposeAsync"/> is the preferred form because everything held here exposes an
        /// asynchronous release; this synchronous path exists for the container and for a
        /// <c>using</c> statement that cannot await. An unresolved ambient transaction is rolled
        /// back, never committed, for the reason given on <see cref="CloseAsync"/>. Nothing is
        /// deleted: disposing a connection removes no data and no file.
        /// </remarks>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            _ambientTransaction?.Dispose();
            _ambientTransaction = null;

            _connection?.Dispose();
            _connection = null;

            _gate.Dispose();

            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Asynchronously releases the ambient transaction, the connection and the gate.
        /// </summary>
        /// <returns>A task that completes when everything held here has been released.</returns>
        /// <remarks>
        /// The preferred teardown. As with <see cref="Dispose"/>, an unresolved ambient transaction
        /// is rolled back rather than committed, and nothing is deleted.
        /// </remarks>
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_ambientTransaction is not null)
            {
                await _ambientTransaction.DisposeAsync().ConfigureAwait(false);
                _ambientTransaction = null;
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
                _connection = null;
            }

            _gate.Dispose();

            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// What the last readiness probe established about the storage engine.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A CLOSED ENUMERATION RATHER THAN TEXT, because its whole purpose is to travel to a surface that
    /// may disclose nothing: the readiness route is anonymous, so the caller-visible reason must be a
    /// value this codebase authored, never a provider message and never a configured path (constraint
    /// C-F). The provider's own detail stays on <c>SqlErrText</c> and <c>LastError</c> for the
    /// in-process readers.
    /// </para>
    /// <para>
    /// The two failure members are separate because they call for different actions and conflating them
    /// wastes an operator's time: an unreachable engine means the mount or the provider is wrong, while
    /// an incomplete schema means the storage is fine and the migrations have not been applied.
    /// </para>
    /// </remarks>
    public enum StorageReadiness
    {
        /// <summary>No readiness probe has run yet.</summary>
        NotProbed = 0,

        /// <summary>The engine answered and the required schema is present.</summary>
        Ready = 1,

        /// <summary>
        /// The engine could not be reached, or could be reached but did not answer. Includes the case
        /// where no database exists at the configured location, because the readiness probe opens
        /// read-only and therefore refuses rather than creating one.
        /// </summary>
        Unreachable = 2,

        /// <summary>
        /// The engine answered, but a required table is absent - so the volume is mounted and the
        /// provider works, and the migrations have not been applied.
        /// </summary>
        SchemaIncomplete = 3,
    }
}
