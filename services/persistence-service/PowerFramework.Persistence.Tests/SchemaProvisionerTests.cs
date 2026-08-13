// ==================================================================================================
//  SchemaProvisionerTests - THE SUITE OVER THE CONFIGURATION-GATED, ADDITIVE-ONLY SCHEMA STEP
//  ------------------------------------------------------------------------------------------------
//  WHAT IS UNDER TEST, AND WHY EVERY ROW HERE RUNS AGAINST A REAL SQLITE FILE. The subject is the one
//  place in this service that mutates a schema, so a test double for the database would assert nothing
//  about the property that matters: that Migrate applied the real migrations under Data/Migrations/ to a
//  real file, additively, and that a second run neither reapplies nor destroys anything. Every row
//  therefore builds a genuine SqliteConnectionFactory over a freshly created, GUID-named temporary
//  directory this file owns, runs the provisioner against it, and deletes the directory afterwards. No
//  row touches a configured data directory, and no row needs a host.
//
//  THE FOUR CLAIMS THE SUITE HAS TO ESTABLISH, IN THE ORDER THEY MATTER
//   1. OFF MEANS NOTHING HAPPENS. Not "nothing important" - nothing: no database file, no lock file,
//      no directory entry of any kind. That is what makes the false default safe for every existing
//      deployment and for every service-level test, all of which boot the same composition root.
//   2. ON MEANS THE SCHEMA EXISTS. The COMPANY table and the migration history table both, because the
//      readiness probe requires both and a database with the first and not the second is one no later
//      migration can be applied to.
//   3. RE-RUNNING IS IDEMPOTENT AND NON-DESTRUCTIVE. This is the claim the paired-capture rule depends
//      on (AAP 0.6.7), so it is asserted the only way that means anything: a row is inserted between
//      the two runs and is still there, unchanged, afterwards.
//   4. CONCURRENT REPLICAS SERIALIZE, AND AN UNAVAILABLE LOCK IS A FAILURE RATHER THAN A SKIP. A
//      provisioner that gave up quietly would report success while leaving the schema absent, which is
//      the one outcome worse than failing to start.
//
//  WHAT THIS SUITE DELIBERATELY DOES NOT DO
//    * It measures no duration and asserts nothing about how long anything takes: the repository
//      publishes no performance budget (AAP 0.8.5). The retry rows inject a completed delay so the
//      contention branch is reachable without waiting, which is a determinism seam and not a speed
//      claim.
//    * It writes no credential of any kind, and the connection it builds carries no password: the
//      encrypted path is out of Phase-1 scope and a credential-shaped literal in a test file is a
//      credential-shaped string in version control (constraint C-F).
//    * It names no other service (constraint C-A) and no deferred capability area (constraint C-D).
//    * It does not edit, read or depend on the read-only legacy tree (constraint C-C). The only legacy
//      facts it relies on are the evidenced table name and file name, both already constants of the
//      application project's own settings.
// ==================================================================================================

using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PowerFramework.Persistence.Configuration;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Asserts that startup schema provisioning is off unless configured, additive and idempotent when on,
/// serialized across replicas, and fatal rather than silent when it cannot proceed.
/// </summary>
public sealed class SchemaProvisionerTests
{
    /// <summary>The only database file name evidenced anywhere in the repository.</summary>
    private const string LegacyDatabaseFileName = "test.db";

    /// <summary>The legacy open mode: read, write, create.</summary>
    private const string LegacyMode = "rwc";

    /// <summary>The legacy default journal mode, which is deliberately not write-ahead logging.</summary>
    private const string LegacyDefaultJournal = "DELETE";

    /// <summary>The one table the sole DDL statement in the repository creates.</summary>
    private const string EvidencedTableName = "COMPANY";

    /// <summary>Entity Framework's own migration history table.</summary>
    private const string MigrationHistoryTableName = "__EFMigrationsHistory";

    /// <summary>
    /// The switch being off performs no work and, decisively, touches the filesystem not at all.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE FILESYSTEM ASSERTIONS ARE THE POINT OF THIS ROW, NOT THE OUTCOME VALUE. A provisioner that
    /// returned <see cref="SchemaProvisioningOutcome.Skipped"/> while still opening its lock file, or
    /// while letting a context materialise the database, would be indistinguishable here from one that
    /// does nothing - and it would break the two things the false default exists to protect: an existing
    /// deployment's behaviour, and a parity operator's volume. So the directory is asserted EMPTY.
    /// </remarks>
    [Fact]
    public async Task TheSwitchBeingOffDoesNothingAtAllAndCreatesNoFile()
    {
        string directory = CreateTestOwnedDirectory("skipped");

        try
        {
            await using Harness harness = Harness.Create(directory, applyMigrations: false);

            SchemaProvisioningOutcome outcome =
                await harness.Provisioner.ProvisionAsync(TestContext.Current.CancellationToken);

            Assert.Equal(SchemaProvisioningOutcome.Skipped, outcome);

            // NOT ONE ENTRY. No database, no lock file, no journal, nothing.
            Assert.Empty(Directory.GetFileSystemEntries(directory));
        }
        finally
        {
            DeleteTestOwnedDirectory(directory);
        }
    }

    /// <summary>
    /// The switch being on applies the pending migrations, producing both tables the readiness probe
    /// requires.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// BOTH TABLES ARE ASSERTED, AND THE HISTORY TABLE IS NOT A DETAIL. The readiness probe checks for
    /// the application table AND the migration history table, and it reports the schema incomplete
    /// without either - because a database carrying the first but not the second was produced by
    /// something other than the migration tool and its version cannot be established, which is exactly
    /// what <c>EnsureCreated</c> would leave behind. Asserting both is therefore what distinguishes the
    /// additive path this type is permitted to take from the one it is not.
    /// </remarks>
    [Fact]
    public async Task TheSwitchBeingOnAppliesThePendingMigrationsAndProducesBothRequiredTables()
    {
        string directory = CreateTestOwnedDirectory("applied");

        try
        {
            await using Harness harness = Harness.Create(directory, applyMigrations: true);

            SchemaProvisioningOutcome outcome =
                await harness.Provisioner.ProvisionAsync(TestContext.Current.CancellationToken);

            Assert.Equal(SchemaProvisioningOutcome.Applied, outcome);

            Assert.True(await TableExistsAsync(harness, EvidencedTableName));
            Assert.True(await TableExistsAsync(harness, MigrationHistoryTableName));

            // The lock file is left in place rather than deleted, which is the documented choice: an
            // empty zero-byte file is harmless, and deleting it would be the one destructive file
            // operation this type is not permitted to have.
            Assert.True(File.Exists(Path.Combine(directory, SchemaProvisioner.LockFileName)));
        }
        finally
        {
            DeleteTestOwnedDirectory(directory);
        }
    }

    /// <summary>
    /// A second run reports the schema already current, writes nothing, and leaves data written between
    /// the two runs exactly as it was.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THIS IS THE ROW THE PAIRED-CAPTURE RULE RESTS ON, WHICH IS WHY IT ASSERTS DATA AND NOT JUST AN
    /// OUTCOME. The characterization model requires that for one workflow identifier the legacy-side and
    /// target-side recordings run against the SAME <c>persistence-db</c> volume state, with the volume
    /// neither recreated nor reseeded between them (AAP 0.6.7). A provisioner that ran on every restart
    /// would violate that the moment it dropped, recreated or reseeded anything - so a row is inserted
    /// between the two runs and read back afterwards, value for value.
    /// </para>
    /// <para>
    /// The second outcome being <see cref="SchemaProvisioningOutcome.AlreadyCurrent"/> rather than
    /// <see cref="SchemaProvisioningOutcome.Applied"/> is asserted as well, because the distinction is
    /// what an operator reads in a startup record: a service that reported "applied" on every restart
    /// would give a reader no way to tell a genuine migration from a no-op.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ASecondRunReportsTheSchemaAlreadyCurrentAndPreservesEveryRow()
    {
        string directory = CreateTestOwnedDirectory("idempotent");

        try
        {
            await using Harness harness = Harness.Create(directory, applyMigrations: true);

            Assert.Equal(
                SchemaProvisioningOutcome.Applied,
                await harness.Provisioner.ProvisionAsync(TestContext.Current.CancellationToken));

            await ExecuteAsync(
                harness,
                "INSERT INTO COMPANY (NAME, AGE, ADDRESS, SALARY, BIRTH) "
                + "VALUES ('a-not-real-person', 41, 'a-not-real-street', 1.5, '2001-02-03');");

            Assert.Equal(1L, await CountRowsAsync(harness));

            Assert.Equal(
                SchemaProvisioningOutcome.AlreadyCurrent,
                await harness.Provisioner.ProvisionAsync(TestContext.Current.CancellationToken));

            // NOTHING WAS DROPPED, RECREATED OR RESEEDED. The row is still there and still itself.
            Assert.Equal(1L, await CountRowsAsync(harness));
            Assert.Equal("a-not-real-person", await ScalarTextAsync(harness, "SELECT NAME FROM COMPANY;"));
            Assert.True(await TableExistsAsync(harness, EvidencedTableName));
            Assert.True(await TableExistsAsync(harness, MigrationHistoryTableName));
        }
        finally
        {
            DeleteTestOwnedDirectory(directory);
        }
    }

    /// <summary>
    /// A provisioner that finds the lock held waits for it and proceeds once it is released.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE WAIT IS PROVEN BY THE SEAM RATHER THAN BY THE CLOCK. The injected delay records that it was
    /// called and releases the held handle on its first invocation, so the row establishes both halves of
    /// the property without measuring any duration: the provisioner did NOT proceed while the lock was
    /// held, and it DID proceed once it was free. A row that merely slept and then checked the outcome
    /// would pass just as well against a provisioner that ignored the lock entirely.
    /// </remarks>
    [Fact]
    public async Task AHeldLockIsWaitedForAndProvisioningProceedsOnceItIsReleased()
    {
        string directory = CreateTestOwnedDirectory("contended");

        try
        {
            FileStream held = new(
                Path.Combine(directory, SchemaProvisioner.LockFileName),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);

            int waits = 0;

            await using Harness harness = Harness.Create(
                directory,
                applyMigrations: true,
                delay: (_, _) =>
                {
                    waits++;
                    held.Dispose();
                    return Task.CompletedTask;
                });

            SchemaProvisioningOutcome outcome =
                await harness.Provisioner.ProvisionAsync(TestContext.Current.CancellationToken);

            Assert.Equal(1, waits);
            Assert.Equal(SchemaProvisioningOutcome.Applied, outcome);
            Assert.True(await TableExistsAsync(harness, EvidencedTableName));
        }
        finally
        {
            DeleteTestOwnedDirectory(directory);
        }
    }

    /// <summary>
    /// A lock that is never released exhausts the bounded attempts and FAILS, naming the setting, the
    /// lock file and the attempt count.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// FAILING IS THE REQUIREMENT HERE, AND SKIPPING WOULD BE THE DEFECT. A provisioner that gave up
    /// quietly would report that startup had completed while leaving the schema absent, so the service
    /// would answer every retrieval and every update with a storage error - the exact failure shape the
    /// fail-fast posture exists to prevent, and the one the oracle's own unconditional halt establishes
    /// [ws_objects/pfw.pbl.src/pfw.sra:L143].
    /// </para>
    /// <para>
    /// THE MESSAGE IS ASSERTED, NOT JUST THE TYPE. A refusal that named no setting and no file would be
    /// fail-fast and useless: the two things an operator needs are which key turns the step off and which
    /// file to look at when no replica is starting. The attempts are lowered and the delay is completed
    /// immediately so the branch is reachable without waiting out the production ceiling.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ALockThatIsNeverReleasedExhaustsTheAttemptsAndFailsWithAnActionableMessage()
    {
        string directory = CreateTestOwnedDirectory("exhausted");

        try
        {
            using FileStream held = new(
                Path.Combine(directory, SchemaProvisioner.LockFileName),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);

            int waits = 0;

            await using Harness harness = Harness.Create(
                directory,
                applyMigrations: true,
                maxLockAttempts: 3,
                delay: (_, _) =>
                {
                    waits++;
                    return Task.CompletedTask;
                });

            InvalidOperationException failure =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await harness.Provisioner.ProvisionAsync(
                        TestContext.Current.CancellationToken));

            // Two waits for three attempts: the third attempt reports rather than waiting again.
            Assert.Equal(2, waits);

            Assert.Contains(
                SchemaProvisioner.EnablingConfigurationKey,
                failure.Message,
                StringComparison.Ordinal);
            Assert.Contains(SchemaProvisioner.LockFileName, failure.Message, StringComparison.Ordinal);
            Assert.Contains("3", failure.Message, StringComparison.Ordinal);

            // NO PATH IS PUBLISHED. A startup record must not disclose a container's mount layout, so the
            // description names the configuration key and never the directory - the same rule
            // DataDirectoryFault applies at its own call sites.
            Assert.DoesNotContain(directory, failure.Message, StringComparison.Ordinal);

            // AND THE SCHEMA WAS NOT TOUCHED. Failing before the lock means nothing was opened.
            Assert.False(File.Exists(Path.Combine(directory, LegacyDatabaseFileName)));
        }
        finally
        {
            DeleteTestOwnedDirectory(directory);
        }
    }

    /// <summary>
    /// The lock is requested EXCLUSIVELY, so a handle that merely permits readers still blocks it.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THIS ROW EXISTS BECAUSE THE TWO CONTENTION ROWS ABOVE DO NOT DISCRIMINATE THE SHARING MODE, AND
    /// THAT WAS ESTABLISHED BY BREAKING THE PRODUCTION CODE RATHER THAN BY INSPECTION. Both of those rows
    /// hold the file with <see cref="FileShare.None"/>, which is an EXCLUSIVE hold - it blocks any second
    /// opener regardless of what sharing that opener asks for. So relaxing the provisioner's own request
    /// from <see cref="FileShare.None"/> to <see cref="FileShare.ReadWrite"/> - which would abandon
    /// serialization entirely and let two replicas migrate at once - left both of them passing.
    /// </para>
    /// <para>
    /// A HOLDER THAT PERMITS READERS IS WHAT SEPARATES THE TWO, MEASURED ON THIS RUNTIME AND PLATFORM. With
    /// the file held as <see cref="FileShare.Read"/>: a second open asking <see cref="FileShare.None"/> is
    /// BLOCKED, because an exclusive request conflicts with an existing shared hold, while one asking
    /// <see cref="FileShare.ReadWrite"/> ACQUIRES. This row therefore fails if and only if the provisioner
    /// stops asking for exclusivity, which is the property the replica-serialization claim rests on.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheLockIsRequestedExclusivelySoASharedHolderStillBlocksIt()
    {
        string directory = CreateTestOwnedDirectory("exclusive");

        try
        {
            // A SHARING HOLD, NOT AN EXCLUSIVE ONE. FileShare.Read permits other readers, so only a
            // request that insists on exclusivity is refused by it.
            using FileStream sharedHolder = new(
                Path.Combine(directory, SchemaProvisioner.LockFileName),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.Read);

            int waits = 0;

            await using Harness harness = Harness.Create(
                directory,
                applyMigrations: true,
                maxLockAttempts: 2,
                delay: (_, _) =>
                {
                    waits++;
                    return Task.CompletedTask;
                });

            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await harness.Provisioner.ProvisionAsync(
                    TestContext.Current.CancellationToken));

            Assert.Equal(1, waits);

            // AND NOTHING WAS MIGRATED. A provisioner that had acquired the lock would have created the
            // database file, which is the observable difference between serializing and not.
            Assert.False(File.Exists(Path.Combine(directory, LegacyDatabaseFileName)));
        }
        finally
        {
            DeleteTestOwnedDirectory(directory);
        }
    }

    /// <summary>
    /// A cancelled request is propagated rather than reported as a completed outcome.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// CANCELLATION HERE MEANS THE HOST IS SHUTTING DOWN, and reporting a provisioning outcome for a
    /// migration that was abandoned is the one lie this method must not tell: the next start would find
    /// the schema absent while the previous run's record said it had been applied. The switch is ON for
    /// this row precisely so the cancellation is observed on the path that does work rather than on the
    /// one that returns immediately.
    /// </remarks>
    [Fact]
    public async Task ACancelledRequestIsPropagatedRatherThanReportedAsAnOutcome()
    {
        string directory = CreateTestOwnedDirectory("cancelled");

        try
        {
            await using Harness harness = Harness.Create(directory, applyMigrations: true);

            using CancellationTokenSource cancelled = new();
            await cancelled.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await harness.Provisioner.ProvisionAsync(cancelled.Token));

            Assert.False(File.Exists(Path.Combine(directory, LegacyDatabaseFileName)));
        }
        finally
        {
            DeleteTestOwnedDirectory(directory);
        }
    }

    /// <summary>
    /// Every constructor dependency is required, and a non-positive attempt count is refused.
    /// </summary>
    /// <remarks>
    /// ZERO ATTEMPTS IS THE INTERESTING ONE. It would mean the step never tried and never reported that
    /// it had not tried, which is the single outcome this type must not have - so it is refused at
    /// construction rather than producing a provisioner that silently does nothing.
    /// </remarks>
    [Fact]
    public void EveryDependencyIsRequiredAndANonPositiveAttemptCountIsRefused()
    {
        string directory = CreateTestOwnedDirectory("guards");

        try
        {
            PersistenceOptions options = OptionsFor(directory, applyMigrations: true);
            IOptions<PersistenceOptions> wrapped = Options.Create(options);

            using SqliteConnectionFactory storage = new(
                wrapped,
                NullLogger<SqliteConnectionFactory>.Instance,
                TimeProvider.System);

            ServiceCollection services = new();
            using ServiceProvider provider = services.BuildServiceProvider();
            IServiceScopeFactory scopes = provider.GetRequiredService<IServiceScopeFactory>();

            Assert.Throws<ArgumentNullException>(() => new SchemaProvisioner(
                null!,
                storage,
                wrapped,
                NullLogger<SchemaProvisioner>.Instance));

            Assert.Throws<ArgumentNullException>(() => new SchemaProvisioner(
                scopes,
                null!,
                wrapped,
                NullLogger<SchemaProvisioner>.Instance));

            Assert.Throws<ArgumentNullException>(() => new SchemaProvisioner(
                scopes,
                storage,
                null!,
                NullLogger<SchemaProvisioner>.Instance));

            Assert.Throws<ArgumentNullException>(() => new SchemaProvisioner(
                scopes,
                storage,
                wrapped,
                null!));

            Assert.Throws<ArgumentOutOfRangeException>(() => new SchemaProvisioner(
                scopes,
                storage,
                wrapped,
                NullLogger<SchemaProvisioner>.Instance,
                maxLockAttempts: 0));

            Assert.Throws<ArgumentOutOfRangeException>(() => new SchemaProvisioner(
                scopes,
                storage,
                wrapped,
                NullLogger<SchemaProvisioner>.Instance,
                maxLockAttempts: -1));
        }
        finally
        {
            DeleteTestOwnedDirectory(directory);
        }
    }

    /// <summary>
    /// The provisioner's source reaches no destructive construct, which is asserted by scanning it.
    /// </summary>
    /// <param name="construct">The construct that must not appear.</param>
    /// <remarks>
    /// <para>
    /// A SOURCE SCAN IS THE ONLY ASSERTION THAT COVERS THE BRANCH NOBODY TAKES. A behavioural row can
    /// only prove that the paths it exercises destroy nothing; it cannot prove that no path anywhere in
    /// the type does. Since the whole justification for turning provisioning on by default in the
    /// orchestration manifest is that the step CANNOT destroy or reseed a volume, the absence has to be
    /// mechanical - and this is the same technique the sibling suites already use over the connection
    /// factory and the database context.
    /// </para>
    /// <para>
    /// The scan runs over the file as authored rather than over a compiled form, because that is where a
    /// future edit would introduce one of these and where a reviewer would look for it.
    /// </para>
    /// <para>
    /// COMMENTS ARE STRIPPED FIRST, AND THAT IS NOT A CONVENIENCE. The provisioner's own banner documents
    /// the absence of each construct BY NAME - "no EnsureCreated, no EnsureDeleted and no DROP" - because
    /// naming them is what makes the guarantee legible to a reader. A scan of the raw text would find them
    /// there and fail on good documentation, which would leave only two ways out: delete the sentences
    /// that explain the property, or delete the test. Stripping comments is what makes the assertion about
    /// the CODE, and it is the same technique the sibling connection-factory suite already applies to the
    /// same class of claim.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("EnsureCreated")]
    [InlineData("EnsureDeleted")]
    [InlineData("ExecuteSqlRaw")]
    [InlineData("ExecuteSqlInterpolated")]
    [InlineData("DROP ")]
    [InlineData("DELETE FROM")]
    [InlineData("TRUNCATE")]
    [InlineData("File.Delete")]
    [InlineData("Directory.Delete")]
    [InlineData("RemoveRange")]
    public void TheProvisionerSourceReachesNoDestructiveConstruct(string construct)
    {
        string source = ReadProvisionerSource();

        Assert.DoesNotContain(construct, source, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The provisioner's source applies the schema through the additive migration call and no other.
    /// </summary>
    /// <remarks>
    /// THE POSITIVE HALF OF THE ROW ABOVE, AND IT IS WORTH HAVING SEPARATELY. A file that reached no
    /// destructive construct because it had stopped applying anything at all would satisfy every row
    /// above while leaving a fresh volume unprovisioned for ever. Asserting the presence of
    /// <c>MigrateAsync</c> is what keeps the absence assertions from being vacuously satisfiable.
    /// </remarks>
    [Fact]
    public void TheProvisionerSourceAppliesTheSchemaThroughTheAdditiveMigrationCallAlone()
    {
        string source = ReadProvisionerSource();

        Assert.Contains("MigrateAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetPendingMigrationsAsync", source, StringComparison.Ordinal);
    }

    /// <summary>Reads the provisioner's authored source, with line comments removed.</summary>
    /// <returns>The file's code, comments stripped.</returns>
    /// <remarks>
    /// The repository root comes from <see cref="TestRepositoryRoot"/>, which the build embeds and which
    /// falls back to a walk, so the row works from whatever working directory a runner chooses. A missing
    /// file is a hard failure rather than a skip: the whole point of these rows is that the property is
    /// asserted, and a row that quietly passed when it could not find its subject would assert nothing.
    /// </remarks>
    private static string ReadProvisionerSource()
    {
        string root = TestRepositoryRoot.Embedded
            ?? throw new InvalidOperationException(
                "The repository root was not resolved, so the provisioner's source could not be read.");

        string path = Path.Combine(
            root,
            "services",
            "persistence-service",
            "PowerFramework.Persistence",
            "Data",
            "SchemaProvisioner.cs");

        Assert.True(File.Exists(path), $"The provisioner source was not found at '{path}'.");

        return StripLineComments(File.ReadAllText(path));
    }

    /// <summary>Removes line comments while leaving string and character literals intact.</summary>
    /// <param name="source">The source text.</param>
    /// <returns>The same text with every line comment removed.</returns>
    /// <remarks>
    /// LINE COMMENTS ONLY, WHICH IS SUFFICIENT AND IS STATED RATHER THAN ASSUMED. The subject file
    /// contains no block comment, no verbatim string and no raw string literal, so a full C# parser would
    /// be a much larger thing to get right for no gain - and getting one subtly wrong is how a
    /// source-shape assertion turns into a false negative that reports a destructive construct as absent.
    /// The scanner tracks literals with backslash escaping so a <c>//</c> inside one is preserved.
    /// </remarks>
    private static string StripLineComments(string source)
    {
        System.Text.StringBuilder stripped = new(source.Length);

        foreach (string line in source.Split('\n'))
        {
            bool inString = false;
            bool inChar = false;
            int index = 0;

            while (index < line.Length)
            {
                char current = line[index];

                if (inString || inChar)
                {
                    if (current == '\\')
                    {
                        index += 2;
                        continue;
                    }

                    if (inString && current == '"')
                    {
                        inString = false;
                    }
                    else if (inChar && current == '\'')
                    {
                        inChar = false;
                    }

                    index++;
                    continue;
                }

                if (current == '"')
                {
                    inString = true;
                    index++;
                    continue;
                }

                if (current == '\'')
                {
                    inChar = true;
                    index++;
                    continue;
                }

                if (current == '/' && index + 1 < line.Length && line[index + 1] == '/')
                {
                    // A line comment outside any literal: the rest of the line is commentary.
                    break;
                }

                index++;
            }

            stripped.Append(line, 0, index).Append('\n');
        }

        return stripped.ToString();
    }

    /// <summary>Builds the option graph a row runs against.</summary>
    /// <param name="directory">The test-owned data directory.</param>
    /// <param name="applyMigrations">Whether the switch is on.</param>
    /// <returns>The bound options.</returns>
    private static PersistenceOptions OptionsFor(string directory, bool applyMigrations)
    {
        PersistenceOptions options = new PersistenceOptionsBuilder()
            .WithDataDirectory(directory)
            .WithDatabaseFileName(LegacyDatabaseFileName)
            .WithMode(LegacyMode)
            .WithJournal(LegacyDefaultJournal)
            .Build();

        options.Schema.ApplyMigrationsOnStartup = applyMigrations;

        return options;
    }

    /// <summary>Whether a table exists in the row's database.</summary>
    /// <param name="harness">The row's harness.</param>
    /// <param name="table">The table name.</param>
    /// <returns><see langword="true"/> when the table exists.</returns>
    private static async Task<bool> TableExistsAsync(Harness harness, string table)
    {
        string? answer = await ScalarTextAsync(
            harness,
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name = '" + table + "';");

        return answer is not null;
    }

    /// <summary>Counts the rows of the one evidenced table.</summary>
    /// <param name="harness">The row's harness.</param>
    /// <returns>The row count.</returns>
    private static async Task<long> CountRowsAsync(Harness harness)
    {
        string? answer = await ScalarTextAsync(harness, "SELECT COUNT(*) FROM COMPANY;");

        return answer is null ? 0L : long.Parse(answer, CultureInfo.InvariantCulture);
    }

    /// <summary>Reads one scalar as text, or null when the query returned no row.</summary>
    /// <param name="harness">The row's harness.</param>
    /// <param name="statement">The statement to run.</param>
    /// <returns>The scalar as text, or <see langword="null"/>.</returns>
    private static async Task<string?> ScalarTextAsync(Harness harness, string statement)
    {
        await using SqliteConnection connection = new(harness.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = statement;

        object? answer = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        return answer is null or DBNull
            ? null
            : Convert.ToString(answer, CultureInfo.InvariantCulture);
    }

    /// <summary>Runs one statement against the row's database.</summary>
    /// <param name="harness">The row's harness.</param>
    /// <param name="statement">The statement to run.</param>
    /// <returns>A task that completes when the statement has run.</returns>
    private static async Task ExecuteAsync(Harness harness, string statement)
    {
        await using SqliteConnection connection = new(harness.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = statement;

        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Creates a freshly named temporary directory this file owns.</summary>
    /// <param name="purpose">A short label folded into the name so a leftover is attributable.</param>
    /// <returns>The created directory's full path.</returns>
    private static string CreateTestOwnedDirectory(string purpose)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            string.Create(CultureInfo.InvariantCulture, $"pfw-schemaprov-{purpose}-{Guid.NewGuid():n}"));

        Directory.CreateDirectory(directory);

        return directory;
    }

    /// <summary>
    /// Removes a directory this file created, tolerating one that is already gone.
    /// </summary>
    /// <param name="directory">The directory to remove.</param>
    /// <remarks>
    /// THE ONE PLACE IN THIS FILE THAT DELETES ANYTHING, AND IT DELETES ONLY WHAT THIS FILE CREATED. The
    /// path is always a freshly created GUID-named temporary directory returned by
    /// <see cref="CreateTestOwnedDirectory"/>, so it can never reach a configured data directory. SQLite
    /// pools connections, so a failure to remove is tolerated rather than propagated: a retained handle
    /// would otherwise turn a passing row into a failing teardown.
    /// </remarks>
    private static void DeleteTestOwnedDirectory(string directory)
    {
        SqliteConnection.ClearAllPools();

        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary directory is not a test failure, and turning one into a failure would
            // report a teardown detail as a defect in the subject.
        }
    }

    /// <summary>
    /// One row's provisioner, over a real connection factory and a real database context, against a
    /// test-owned directory.
    /// </summary>
    /// <remarks>
    /// THE CONTEXT IS REGISTERED THE WAY THE COMPOSITION ROOT REGISTERS IT - scoped, over the connection
    /// string the factory composes - so a row exercises the same graph the service builds rather than a
    /// convenient approximation. That is what makes the migration assertions meaningful: the migrations
    /// applied are the ones under <c>Data/Migrations/</c>, resolved from the application assembly exactly
    /// as they are at startup.
    /// </remarks>
    private sealed class Harness : IAsyncDisposable
    {
        /// <summary>Owns the scoped context registration.</summary>
        private readonly ServiceProvider _provider;

        /// <summary>Owns the composed connection string.</summary>
        private readonly SqliteConnectionFactory _storage;

        /// <summary>Initializes the harness.</summary>
        /// <param name="provider">The built provider.</param>
        /// <param name="storage">The connection factory.</param>
        /// <param name="provisioner">The subject under test.</param>
        private Harness(
            ServiceProvider provider,
            SqliteConnectionFactory storage,
            SchemaProvisioner provisioner)
        {
            _provider = provider;
            _storage = storage;
            Provisioner = provisioner;
        }

        /// <summary>The subject under test.</summary>
        internal SchemaProvisioner Provisioner { get; }

        /// <summary>The connection string a row reads its assertions through.</summary>
        internal string ConnectionString => _storage.ConnectionString;

        /// <summary>Builds a harness.</summary>
        /// <param name="directory">The test-owned data directory.</param>
        /// <param name="applyMigrations">Whether the switch is on.</param>
        /// <param name="maxLockAttempts">The bounded attempt count.</param>
        /// <param name="delay">The injected wait between lock attempts.</param>
        /// <returns>The harness.</returns>
        internal static Harness Create(
            string directory,
            bool applyMigrations,
            int maxLockAttempts = SchemaProvisioner.DefaultMaxLockAttempts,
            Func<TimeSpan, CancellationToken, Task>? delay = null)
        {
            IOptions<PersistenceOptions> options =
                Options.Create(OptionsFor(directory, applyMigrations));

            SqliteConnectionFactory storage = new(
                options,
                NullLogger<SqliteConnectionFactory>.Instance,
                TimeProvider.System);

            ServiceCollection services = new();
            services.AddDbContext<PowerFrameworkDbContext>(builder =>
                builder.UseSqlite(storage.ConnectionString));

            ServiceProvider provider = services.BuildServiceProvider();

            SchemaProvisioner provisioner = new(
                provider.GetRequiredService<IServiceScopeFactory>(),
                storage,
                options,
                NullLogger<SchemaProvisioner>.Instance,
                maxLockAttempts,
                delay);

            return new Harness(provider, storage, provisioner);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            await _storage.DisposeAsync();
        }
    }
}
