// ==================================================================================================
//  SchemaProvisioner - THE CONFIGURATION-GATED, ADDITIVE-ONLY SCHEMA STEP THAT MAKES THE DOCUMENTED
//  ONE-COMMAND BRING-UP REACH A HEALTHY STACK
//  ------------------------------------------------------------------------------------------------
//  WHAT WAS WRONG BEFORE THIS TYPE EXISTED, STATED AS THE OPERATOR EXPERIENCED IT. This service
//  creates no schema of its own, so against a FRESH `persistence-db` volume its readiness probe found
//  the COMPANY table absent, reported not-ready, and kept reporting not-ready for ever. The Compose
//  health condition then correctly held DataServices and Gateway behind it - so the documented single
//  command, `docker compose --env-file .env up --build -d`, brought up a stack in which three of four
//  services never became healthy and nothing in the manifest could fix it. Reaching a healthy stack
//  required `dotnet ef database update` run out of band, from a checkout, with the SDK and the
//  `dotnet-ef` tool installed - none of which the runtime image carries and none of which an operator
//  following the documented bring-up has any reason to have.
//
//  That is a shortfall against constraint C-J (one local orchestration path bringing all four up
//  together) and constraint C-L (the attached environment's bring-up command and its readiness gates
//  are binding). This type closes it.
//
//  THE FOUR THINGS THIS TYPE MAY NOT BE, EACH FOR A NAMED REASON
//  ------------------------------------------------------------------------------------------------
//   1. IT MAY NOT BE A FIFTH CONTAINER. An init container running the EF tool would be a service built
//      from a different base image, and the manifest is exactly four services by requirement (C-D).
//      So the step lives inside the service that owns the storage.
//
//   2. IT MAY NOT BE DESTRUCTIVE, IN ANY DEGREE. No EnsureCreated, no EnsureDeleted, no DROP, no
//      DELETE, no seed, no file removal - not on this path and not anywhere it reaches. The
//      characterization model requires that for one workflow identifier the legacy-side and
//      target-side recordings run against the SAME volume state, with the volume neither recreated
//      nor reseeded between them, or the paired recordings are not comparable at all (AAP 0.6.7).
//      Database.Migrate is ADDITIVE and IDEMPOTENT - it applies what the history table does not
//      already record and does nothing when there is nothing to apply - which is the only shape
//      compatible with that rule. The sibling suite asserts the absence of every destructive
//      construct by SCANNING THIS FILE'S SOURCE, so the property is mechanical rather than reviewed.
//
//   3. IT MAY NOT BE ON BY DEFAULT. `Schema:ApplyMigrationsOnStartup` defaults to false, so a parity
//      run can rely on the volume being untouched, every existing deployment behaves exactly as it did
//      before this file existed, and every service-level test - all of which boot this same
//      composition root - is unaffected. The orchestration manifest turns it on explicitly, in one
//      place an operator reading the bring-up can see.
//
//   4. IT MAY NOT DEGRADE GRACEFULLY. When the switch is ON and provisioning fails, the process
//      terminates with a named cause. A service that started anyway would answer every retrieval and
//      every update with a storage error, which is the failure shape the fail-fast posture exists to
//      prevent - and the oracle's own posture is unconditional: its application object decodes a
//      seven-field assert payload and then executes HALT CLOSE
//      [ws_objects/pfw.pbl.src/pfw.sra:L111-L144, the halt at :L143].
//
//  WHY THE FILE LOCK, AND WHAT IT HONESTLY GUARANTEES
//  ------------------------------------------------------------------------------------------------
//  This service is required to be independently SCALABLE (C-J), so two replicas can start at once
//  against one mounted volume and both can find the same migration pending. Two concurrent
//  Database.Migrate calls on one SQLite file is a genuine hazard: the loser meets a busy database or a
//  duplicate history row, and the resulting failure looks like a broken migration rather than a race.
//
//  So the step is serialized by an exclusive handle on `<DataDirectory>/.schema-provision.lock`,
//  opened with FileShare.None, held for the duration and released after. What that guarantees, stated
//  precisely rather than optimistically: on a local filesystem the runtime backs FileShare with an
//  advisory `flock`, so replicas sharing one host and one volume serialize. On a network filesystem
//  whose locking is unreliable it may not serialize, and this file does not pretend otherwise - the
//  second line of defence is that a loser FAILS rather than corrupts, because EF's own migration
//  history table and SQLite's own locking both refuse a duplicate application, and a failure here
//  terminates the process, which is what an orchestrator's restart policy is for. The retry loop
//  bounds the wait so a stale lock cannot hang a start for ever.
//
//  WHAT THIS TYPE DELIBERATELY DOES NOT DO
//  ------------------------------------------------------------------------------------------------
//    * It does not create the data directory. The startup gate has already proven that directory
//      present and writable by the time this runs, and it names `Sqlite:DataDirectory` when it is not,
//      so a second creation attempt here would only duplicate an established diagnosis. A failure to
//      open the lock file is therefore described by Data/DataDirectoryFault, which names the
//      configuration key and never the path.
//    * It does not read or write application data. It applies migrations; it does not seed, verify or
//      inspect a row.
//    * It logs no path and no connection string. A startup record must not publish a container's mount
//      layout - the same rule DataDirectoryFault, InternalTlsTrust and PersistenceOptions all apply.
//    * It makes no performance claim and measures no duration, here or anywhere: the repository
//      publishes no such budget (AAP 0.8.5).
// ==================================================================================================

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PowerFramework.Persistence.Configuration;

namespace PowerFramework.Persistence.Data
{
    /// <summary>
    /// What the provisioning step did, so a caller and a test can tell the three outcomes apart.
    /// </summary>
    /// <remarks>
    /// THREE MEMBERS RATHER THAN A BOOLEAN, because "the switch is off" and "the switch is on and there
    /// was nothing to apply" are different facts about a deployment and an operator reading a startup
    /// record needs to know which one happened. Collapsing them would make a service that silently never
    /// provisions indistinguishable from one whose schema is already current.
    /// </remarks>
    internal enum SchemaProvisioningOutcome
    {
        /// <summary>The switch is off, so nothing was opened, created or applied.</summary>
        Skipped = 0,

        /// <summary>The switch is on and the schema already carried every migration.</summary>
        AlreadyCurrent = 1,

        /// <summary>The switch is on and at least one pending migration was applied.</summary>
        Applied = 2,
    }

    /// <summary>
    /// Applies the pending Entity Framework migrations at startup when
    /// <see cref="SchemaOptions.ApplyMigrationsOnStartup"/> is set, serialized across replicas by an
    /// exclusive lock file in the configured data directory.
    /// </summary>
    /// <remarks>
    /// See the file banner for why this exists, what it may not do, and exactly what the lock
    /// guarantees. It is resolved once from the composition root immediately after the startup gate, so
    /// no request is served before the schema it needs exists.
    /// </remarks>
    internal sealed class SchemaProvisioner
    {
        /// <summary>
        /// The configuration key that enables this step, spelled once so every message names the same
        /// setting an operator has to edit.
        /// </summary>
        internal const string EnablingConfigurationKey = "Schema:ApplyMigrationsOnStartup";

        /// <summary>
        /// The lock file's name, created beside the database inside the configured data directory.
        /// </summary>
        /// <remarks>
        /// A LEADING DOT AND A FIXED NAME. It sits on the mounted volume rather than in a temporary
        /// directory precisely because the volume is the thing replicas share - a lock in a container's
        /// own writable layer would be private to each replica and would serialize nothing. It is never
        /// deleted: an empty zero-byte file beside the database is harmless, whereas deleting it would
        /// introduce the one destructive file operation this type is not permitted to have, and would
        /// open a window in which two replicas hold locks on two different inodes of the same name.
        /// </remarks>
        internal const string LockFileName = ".schema-provision.lock";

        /// <summary>
        /// How many times the lock is attempted before the step fails.
        /// </summary>
        /// <remarks>
        /// BOUNDED SO A STALE LOCK CANNOT HANG A START FOR EVER. Thirty attempts at
        /// <see cref="LockRetryDelay"/> is a ceiling comfortably above the time one migration of the one
        /// evidenced table takes and far below any orchestrator's patience. Exhausting it is a failure
        /// with a named cause, not a silent skip: skipping would leave the schema absent while the
        /// process reported that it had provisioned.
        /// </remarks>
        internal const int DefaultMaxLockAttempts = 30;

        /// <summary>The wait between lock attempts.</summary>
        internal static readonly TimeSpan LockRetryDelay = TimeSpan.FromSeconds(1);

        /// <summary>Creates the scope the context is resolved in.</summary>
        private readonly IServiceScopeFactory _scopes;

        /// <summary>Supplies the resolved, validated data directory the lock file lives in.</summary>
        private readonly SqliteConnectionFactory _storage;

        /// <summary>The bound options, read once for the switch.</summary>
        private readonly IOptions<PersistenceOptions> _options;

        /// <summary>Records what the step did.</summary>
        private readonly ILogger<SchemaProvisioner> _logger;

        /// <summary>How many lock attempts this instance makes.</summary>
        private readonly int _maxLockAttempts;

        /// <summary>The injectable wait between lock attempts.</summary>
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;

        /// <summary>Initializes the provisioner.</summary>
        /// <param name="scopes">The scope factory the database context is resolved through.</param>
        /// <param name="storage">
        /// The connection factory, which owns the resolved and validated data directory. Taking it rather
        /// than re-reading <c>Sqlite:DataDirectory</c> is deliberate: the factory has already trimmed the
        /// value, made it absolute and refused a path inside the read-only legacy tree, and a second
        /// resolution here could disagree with the one the database actually opens through.
        /// </param>
        /// <param name="options">The bound options graph, read for the enabling switch.</param>
        /// <param name="logger">The logger the outcome is recorded through.</param>
        /// <param name="maxLockAttempts">
        /// How many times the lock is attempted, defaulting to <see cref="DefaultMaxLockAttempts"/>. A
        /// test lowers it so the exhaustion branch is reachable without waiting out the production
        /// ceiling.
        /// </param>
        /// <param name="delay">
        /// The wait between lock attempts, defaulting to <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.
        /// A test substitutes a completed task so the retry loop runs at full speed, which is what keeps
        /// the contention branch measurable rather than slow.
        /// </param>
        /// <exception cref="ArgumentNullException">A required dependency is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="maxLockAttempts"/> is below one. Zero attempts would mean the step never tried
        /// and never reported that it had not, which is the one outcome this type must not have.
        /// </exception>
        public SchemaProvisioner(
            IServiceScopeFactory scopes,
            SqliteConnectionFactory storage,
            IOptions<PersistenceOptions> options,
            ILogger<SchemaProvisioner> logger,
            int maxLockAttempts = DefaultMaxLockAttempts,
            Func<TimeSpan, CancellationToken, Task>? delay = null)
        {
            ArgumentNullException.ThrowIfNull(scopes);
            ArgumentNullException.ThrowIfNull(storage);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentOutOfRangeException.ThrowIfLessThan(maxLockAttempts, 1);

            _scopes = scopes;
            _storage = storage;
            _options = options;
            _logger = logger;
            _maxLockAttempts = maxLockAttempts;
            _delay = delay ?? Task.Delay;
        }

        /// <summary>
        /// Applies the pending migrations when the switch is set, and does nothing at all when it is not.
        /// </summary>
        /// <param name="cancellationToken">Abandons the wait for the lock and the migration itself.</param>
        /// <returns>Which of the three outcomes occurred.</returns>
        /// <exception cref="InvalidOperationException">
        /// The lock could not be acquired within the bounded attempt count, or the data directory could
        /// not be reached. Escaping the composition root, this terminates the process with a non-zero
        /// exit code, which is the managed equivalent of the oracle's <c>HALT CLOSE</c> and what an
        /// orchestrator's restart policy reads.
        /// </exception>
        /// <remarks>
        /// <para>
        /// THE ORDER OF THE THREE STEPS IS THE CONTRACT. The switch is read FIRST, so a deployment that
        /// has not opted in performs no file operation of any kind - not even opening the lock. The lock
        /// is taken SECOND, before anything reads the database, so a replica that loses the race waits
        /// rather than discovering the conflict inside a migration. The pending set is read THIRD, inside
        /// the lock, so the answer cannot be invalidated by a sibling between the read and the apply.
        /// </para>
        /// <para>
        /// A CANCELLED REQUEST IS PROPAGATED RATHER THAN SWALLOWED. Cancellation here means the host is
        /// shutting down, and reporting a provisioning success for a migration that was abandoned
        /// half-way is the one lie this method must not tell.
        /// </para>
        /// </remarks>
        internal async Task<SchemaProvisioningOutcome> ProvisionAsync(CancellationToken cancellationToken)
        {
            if (!_options.Value.Schema.ApplyMigrationsOnStartup)
            {
                // NO FILE OPERATION ON THE OPT-OUT PATH. The message names the key rather than describing
                // the state, because the only actionable fact for a reader who expected provisioning is
                // which setting turns it on.
                _logger.LogInformation(
                    "Schema provisioning is disabled, so no migration was applied and no storage file was "
                    + "opened. Set '{ConfigurationKey}' to true to apply the pending migrations at "
                    + "startup, or apply them out of band with the migration tool.",
                    EnablingConfigurationKey);

                return SchemaProvisioningOutcome.Skipped;
            }

            using FileStream guard = await AcquireLockAsync(cancellationToken).ConfigureAwait(false);

            using IServiceScope scope = _scopes.CreateScope();

            PowerFrameworkDbContext context =
                scope.ServiceProvider.GetRequiredService<PowerFrameworkDbContext>();

            string[] pending =
            [
                .. await context.Database
                    .GetPendingMigrationsAsync(cancellationToken)
                    .ConfigureAwait(false),
            ];

            if (pending.Length == 0)
            {
                // ALREADY CURRENT, AND NO WRITE IS ATTEMPTED. Migrate would itself be a no-op here, and
                // not calling it keeps the common restart path from opening a write transaction on a
                // volume a paired capture may be mid-way through reading.
                _logger.LogInformation(
                    "Schema provisioning found no pending migration, so the schema was left exactly as it "
                    + "was. Nothing was created, dropped or seeded.");

                return SchemaProvisioningOutcome.AlreadyCurrent;
            }

            _logger.LogInformation(
                "Schema provisioning is applying {PendingCount} pending migration(s): {PendingMigrations}. "
                + "The step is additive and idempotent - it creates what the migration history does not "
                + "already record and removes nothing.",
                pending.Length,
                string.Join(", ", pending));

            // THE ONLY SCHEMA CALL IN THIS SERVICE, AND IT IS THE ADDITIVE ONE. EnsureCreated would
            // bypass the migration history and leave a database no later migration could be applied to;
            // EnsureDeleted and a DROP would destroy the volume a paired capture depends on. Neither
            // appears here or anywhere this path reaches, and the sibling suite proves it by scanning
            // this file.
            await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Schema provisioning applied {AppliedCount} migration(s). The readiness probe reports ready "
                + "once the storage engine answers, which is what opens the orchestration dependency gate.",
                pending.Length);

            return SchemaProvisioningOutcome.Applied;
        }

        /// <summary>
        /// Takes the exclusive lock, retrying a bounded number of times while a sibling holds it.
        /// </summary>
        /// <param name="cancellationToken">Abandons the wait.</param>
        /// <returns>The held handle, released by the caller's <c>using</c>.</returns>
        /// <exception cref="InvalidOperationException">
        /// The attempts were exhausted, or the directory could not be reached at all.
        /// </exception>
        /// <remarks>
        /// THE TWO FAILURE CLASSES ARE DISCRIMINATED, AND THAT IS THE POINT OF THE SPLIT. A directory
        /// that cannot be reached is a deployment fault - an unmounted volume, a path occupied by a file,
        /// an ownership mismatch - and <see cref="DataDirectoryFault"/> already establishes which and
        /// names the configuration key without publishing the path. A lock that is HELD is a live sibling
        /// replica, which is a wait rather than a fault until the attempts run out. Reporting the first as
        /// contention would send an operator hunting for a replica that does not exist, and reporting the
        /// second as a directory fault would send them to check permissions that are already correct.
        /// </remarks>
        private async Task<FileStream> AcquireLockAsync(CancellationToken cancellationToken)
        {
            string directory = _storage.DataDirectory;
            string lockPath = Path.Combine(directory, LockFileName);

            for (int attempt = 1; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    return new FileStream(
                        lockPath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None);
                }
                catch (IOException error) when (attempt < _maxLockAttempts && Directory.Exists(directory))
                {
                    // A SIBLING HOLDS IT. The directory-existence condition is what keeps this arm from
                    // swallowing a genuine directory fault: an unmounted volume also raises IOException,
                    // and retrying that thirty times would delay a fatal diagnosis by half a minute and
                    // then report it as contention.
                    _logger.LogInformation(
                        "Schema provisioning is waiting for another replica to finish; attempt {Attempt} "
                        + "of {MaxAttempts}. Cause type {CauseType}.",
                        attempt,
                        _maxLockAttempts,
                        error.GetType().Name);

                    await _delay(LockRetryDelay, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    // EXHAUSTED, OR A DIRECTORY FAULT. Either way the description is built by
                    // DataDirectoryFault, which classifies the directory and names the configuration key
                    // rather than the path; the cause is described BY TYPE and not attached, because the
                    // file APIs put the path into their own message.
                    throw new InvalidOperationException(
                        "Schema provisioning could not take its exclusive lock, so the pending migrations "
                        + "were not applied and this service cannot serve a retrieval or an update. "
                        + $"Attempted {attempt} time(s) of {_maxLockAttempts}. "
                        + DataDirectoryFault.Describe(directory, error)
                        + $" If no other replica is starting, the lock file named '{LockFileName}' in that "
                        + "directory is held by a process that did not release it. Provisioning is enabled "
                        + $"by '{EnablingConfigurationKey}'.");
                }
            }
        }
    }
}
