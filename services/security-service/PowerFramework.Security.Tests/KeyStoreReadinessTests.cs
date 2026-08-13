// ======================================================================================================
//  KeyStoreReadinessTests.cs - THE STORE SAYS WHETHER IT CAN RESOLVE ANYTHING, ONCE, AT STARTUP
//  ------------------------------------------------------------------------------------------------------
//  WHAT THESE ROWS PROTECT
//  Contract C-02 resolves a caller's keyRef only if the reference appears in
//  Security:KeyStore:PermittedKeyRefs, and the shipped settings file leaves that set EMPTY while
//  configuring a prefix beside it. The default is correct - an unconfigured store must resolve nothing
//  rather than read whatever configuration key a caller names - but it is SILENT. A deployment that meant
//  to publish references and did not learns nothing until a caller is refused, and that refusal is
//  deliberately indistinguishable from an unknown reference, so it cannot say the store is merely empty.
//  One startup record closes that gap.
//
//  WHY WARNING FOR THE EMPTY STORE, AND WHY THAT IS NOT NOISE
//  ClientCertificateTrust already records an unset trust anchor at Warning on the shipped configuration,
//  qualified to the credential it affects and closed with the note that the state is fail-closed rather
//  than a fault. An empty key store is the identical shape one edge over: a capability is unavailable, an
//  adjacent one is untouched, and the remedy is a configuration key. These rows pin that the two records
//  agree - same level, same qualification, same closing - because an operator reads them together.
//
//  WHAT IS DELIBERATELY NOT HERE
//  No row asserts that an empty store REFUSES the host. It must not: the empty set is the shipped posture
//  and a host refusing it could not be brought up from a clean checkout (constraints C-A and C-I). The
//  validator already refuses the one shape that cannot work - a permitted reference with no prefix - and
//  that rule is covered where the validator is covered. Nothing here changes what resolves.
// ======================================================================================================

using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PowerFramework.Security.Configuration;

namespace PowerFramework.Security.Tests;

/// <summary>
/// Covers the key-store readiness record: both store states, what it never echoes, and the composition root
/// actually emitting it at the level each state earns.
/// </summary>
public sealed class KeyStoreReadinessTests
{
    /// <summary>The category the composition root writes the readiness record under.</summary>
    private const string ReadinessCategory = "PowerFramework.Security.KeyStoreReadiness";

    /// <summary>The configuration key a deployment lists its published references under.</summary>
    private const string PermittedKeyRefsKey = "Security:KeyStore:PermittedKeyRefs";

    /// <summary>The configuration key the prefix is bound from.</summary>
    private const string ConfigurationKeyPrefixKey = "Security:KeyStore:ConfigurationKeyPrefix";

    /// <summary>
    /// An empty store reports the capability as unavailable, names the minted-reference path as unaffected,
    /// and closes as a fail-closed state rather than a fault.
    /// </summary>
    /// <remarks>
    /// THE QUALIFICATION IS THE LOAD-BEARING PART, and it is the same correction
    /// <c>ClientCertificateTrust</c> carries. Key material this service generated and retained for the
    /// calling principal is reached by a reference it minted, which the permitted set does not gate at all -
    /// so an unqualified "keyed crypto is unavailable" would send an operator hunting an outage that is not
    /// happening.
    /// </remarks>
    [Fact]
    public void AnEmptyStoreReportsTheCapabilityUnavailableAndTheMintedPathUnaffected()
    {
        SecurityOptions options = new();

        Assert.Empty(options.KeyStore.PermittedKeyRefs);
        Assert.Empty(KeyStoreReadiness.DescribeAvailable(options));

        string record = Assert.Single(KeyStoreReadiness.DescribeUnavailable(options));

        Assert.Contains("No key reference is permitted", record, StringComparison.Ordinal);
        Assert.Contains("GENERATED", record, StringComparison.Ordinal);
        Assert.Contains("remains usable by the reference it minted", record, StringComparison.Ordinal);
        Assert.Contains("fail-closed state, not a fault", record, StringComparison.Ordinal);
        Assert.Contains(PermittedKeyRefsKey, record, StringComparison.Ordinal);
    }

    /// <summary>
    /// The unavailable record's remedy names BOTH missing halves when neither is configured, and names the
    /// reference list as the only missing half when a prefix is already configured.
    /// </summary>
    /// <remarks>
    /// TWO REMEDIES BECAUSE THERE ARE TWO SHAPES, and the shipped configuration is the second one: it
    /// carries a prefix and no references, so a message telling an operator to configure the prefix would
    /// point at the half that is already done. The prefix-less arm has to name both, or the deployment
    /// applies one edit and is still refused.
    /// </remarks>
    [Fact]
    public void TheUnavailableRecordsRemedyMatchesWhichHalfIsMissing()
    {
        SecurityOptions unconfigured = new();

        string both = Assert.Single(KeyStoreReadiness.DescribeUnavailable(unconfigured));

        Assert.Contains($"Configure '{ConfigurationKeyPrefixKey}'", both, StringComparison.Ordinal);

        SecurityOptions halfConfigured = new();
        halfConfigured.KeyStore.ConfigurationKeyPrefix = "SECURITY_KEYSTORE_";

        string listOnly = Assert.Single(KeyStoreReadiness.DescribeUnavailable(halfConfigured));

        Assert.Contains("is configured and resolves nothing on its own", listOnly, StringComparison.Ordinal);
        Assert.Contains("the missing half is the reference list", listOnly, StringComparison.Ordinal);
    }

    /// <summary>
    /// A whitespace prefix is treated as absent, which is how the validator's own prefix rule reads it.
    /// </summary>
    /// <remarks>
    /// A BLANK PREFIX CANNOT RESOLVE ANYTHING, so a remedy claiming it is "configured and resolves nothing
    /// on its own" would be telling an operator that a value they never set is half the answer.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankPrefixIsTreatedAsAbsentByTheRemedy(string prefix)
    {
        SecurityOptions options = new();
        options.KeyStore.ConfigurationKeyPrefix = prefix;

        string record = Assert.Single(KeyStoreReadiness.DescribeUnavailable(options));

        Assert.Contains($"Configure '{ConfigurationKeyPrefixKey}'", record, StringComparison.Ordinal);
        Assert.DoesNotContain("the missing half is the reference list", record, StringComparison.Ordinal);
    }

    /// <summary>
    /// A configured store is reported at the other level, with its count, every reference it published, and
    /// what it still refuses.
    /// </summary>
    /// <param name="references">The references the deployment published.</param>
    /// <remarks>
    /// THE IDENTIFIERS ARE SAFE TO NAME AND THE COUNT IS WHAT AN OPERATOR COMPARES. A reference is an
    /// identifier already written in a settings file in plain text; the MATERIAL it names is reached only
    /// through configuration injection and is never read here. The count is asserted separately from the
    /// identifiers because it is the value that catches a list one entry shorter than intended.
    /// </remarks>
    [Theory]
    [InlineData("signing-a")]
    [InlineData("signing-a", "signing-b")]
    [InlineData("signing-a", "signing-b", "signing-c")]
    public void AConfiguredStoreIsReportedWithItsCountItsReferencesAndWhatItStillRefuses(
        params string[] references)
    {
        ArgumentNullException.ThrowIfNull(references);

        SecurityOptions options = new();
        options.KeyStore.ConfigurationKeyPrefix = "SECURITY_KEYSTORE_";

        foreach (string reference in references)
        {
            options.KeyStore.PermittedKeyRefs.Add(reference);
        }

        Assert.Empty(KeyStoreReadiness.DescribeUnavailable(options));

        string record = Assert.Single(KeyStoreReadiness.DescribeAvailable(options));

        Assert.Contains(
            references.Length.ToString(CultureInfo.InvariantCulture),
            record,
            StringComparison.Ordinal);

        foreach (string reference in references)
        {
            Assert.Contains(reference, record, StringComparison.Ordinal);
        }

        // A configured store still refuses two things, and saying so is what stops the first refusal after a
        // successful start from reading as a regression.
        Assert.Contains("any other reference is refused", record, StringComparison.Ordinal);
        Assert.Contains("material the deployment did not supply", record, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two members are mutually exclusive in every configuration shape, so exactly one record exists.
    /// </summary>
    /// <param name="prefix">The prefix value to bind.</param>
    /// <param name="referenceCount">How many references to publish.</param>
    /// <remarks>
    /// THE EXCLUSIVITY IS THE CONTRACT THE CALL SITE RELIES ON. The composition root runs both loops
    /// unconditionally, so a shape satisfying both members would log two records saying the opposite of each
    /// other, and a shape satisfying neither would log nothing at all - the silence the finding is about.
    /// The prefix-less-with-references row is included even though the validator refuses that shape, because
    /// this member must not be the thing that decides it: two reports of one fault at two severities is
    /// worse than one.
    /// </remarks>
    [Theory]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("SECURITY_KEYSTORE_", 0)]
    [InlineData("SECURITY_KEYSTORE_", 1)]
    [InlineData("SECURITY_KEYSTORE_", 4)]
    [InlineData("", 1)]
    public void ExactlyOneRecordDescribesAnyConfigurationShape(string prefix, int referenceCount)
    {
        SecurityOptions options = new();
        options.KeyStore.ConfigurationKeyPrefix = prefix;

        for (int index = 0; index < referenceCount; index++)
        {
            options.KeyStore.PermittedKeyRefs.Add(
                string.Create(CultureInfo.InvariantCulture, $"signing-{index}"));
        }

        int total = KeyStoreReadiness.DescribeAvailable(options).Count
            + KeyStoreReadiness.DescribeUnavailable(options).Count;

        Assert.Equal(1, total);
    }

    /// <summary>
    /// Neither member echoes the prefix VALUE, which composes the configuration key material arrives under.
    /// </summary>
    /// <remarks>
    /// A PREFIX IS NOT A SECRET AND IS STILL NOT ECHOED. It names where material lives, and the
    /// repository-wide convention is that a diagnostic names a configuration KEY and never a configured
    /// value. Both store states are driven, because the available record is the one that would most
    /// naturally quote it.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoRecordEchoesTheConfiguredPrefixValue(int referenceCount)
    {
        const string prefix = "PFW_SENTINEL_PREFIX_VALUE_";

        SecurityOptions options = new();
        options.KeyStore.ConfigurationKeyPrefix = prefix;

        for (int index = 0; index < referenceCount; index++)
        {
            options.KeyStore.PermittedKeyRefs.Add(
                string.Create(CultureInfo.InvariantCulture, $"signing-{index}"));
        }

        foreach (string record in KeyStoreReadiness.DescribeAvailable(options))
        {
            Assert.DoesNotContain(prefix, record, StringComparison.Ordinal);
        }

        foreach (string record in KeyStoreReadiness.DescribeUnavailable(options))
        {
            Assert.DoesNotContain(prefix, record, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Both members refuse a null options instance rather than reporting a readiness they cannot know.
    /// </summary>
    [Fact]
    public void BothMembersRefuseANullOptionsInstance()
    {
        _ = Assert.Throws<ArgumentNullException>(() => KeyStoreReadiness.DescribeAvailable(null!));
        _ = Assert.Throws<ArgumentNullException>(() => KeyStoreReadiness.DescribeUnavailable(null!));
    }

    /// <summary>
    /// The REAL composition root emits the readiness record for the SHIPPED configuration, at Warning, under
    /// the category named after the type that produces it - and emits exactly one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ROW THAT CANNOT BE FAKED. Every row above tests a pure function; a composition root that never
    /// called either would pass all of them and ship the silence the finding is about. This one boots the
    /// service's own entry point on its own settings files and reads what it actually wrote.
    /// </para>
    /// <para>
    /// AND IT PINS THE SHIPPED STATE ITSELF, which is the substantive discovery behind this suite: the
    /// shipped settings file configures a key-store prefix with no permitted reference, so the store a
    /// default deployment runs is the half-configured one. The record therefore has to be the WARNING arm on
    /// a stock bring-up, not the informational one.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheCompositionRootWarnsOnceForTheShippedConfiguration()
    {
        RecordingLoggerProvider recorder = new();

        using ReadinessHost host = new(recorder, referenceOverride: null);

        _ = host.CreateClient();

        LogRecord record = Assert.Single(
            recorder.Records,
            entry => string.Equals(entry.Category, ReadinessCategory, StringComparison.Ordinal));

        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Contains("No key reference is permitted", record.Message, StringComparison.Ordinal);
        Assert.Contains("fail-closed state, not a fault", record.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The REAL composition root drops to Information, and emits only that, once the deployment publishes a
    /// reference.
    /// </summary>
    /// <remarks>
    /// THE SILENCING ARM, AND IT IS WHY THE WARNING IS NOT NOISE. A warning a deployment cannot clear is
    /// noise; this row proves one configuration edit clears it and replaces it with the readiness statement,
    /// which is the whole difference between a signal and a standing complaint.
    /// </remarks>
    [Fact]
    public void TheCompositionRootDropsToInformationOnceAReferenceIsPublished()
    {
        RecordingLoggerProvider recorder = new();

        using ReadinessHost host = new(recorder, referenceOverride: "signing-a");

        _ = host.CreateClient();

        LogRecord record = Assert.Single(
            recorder.Records,
            entry => string.Equals(entry.Category, ReadinessCategory, StringComparison.Ordinal));

        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Contains("signing-a", record.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A host of the REAL composition root carrying the service's own settings and a recording logger.
    /// </summary>
    /// <param name="recorder">The recording provider every record is written through.</param>
    /// <param name="referenceOverride">
    /// A key reference to publish, or <see langword="null"/> to leave the shipped empty set in place.
    /// </param>
    /// <remarks>
    /// DELIBERATELY NOT <c>SecurityAppFactory</c>, for the reason the sibling reporting host in
    /// <c>IssuanceRosterAuthorityTests</c> gives: that factory installs its own roster and grant matrix, so
    /// what it proved would be a census of the harness rather than of the shipped configuration. This host
    /// supplies only what the settings files deliberately do not carry - signing material and the secrets
    /// the shipped roster names (constraint C-F) - plus the one override a row under test needs.
    /// </remarks>
    private sealed class ReadinessHost(RecordingLoggerProvider recorder, string? referenceOverride)
        : WebApplicationFactory<Program>
    {
        /// <inheritdoc />
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            Dictionary<string, string?> settings = new(StringComparer.Ordinal)
            {
                [SecurityOptions.SigningKeyEnvironmentVariableName] =
                    SecurityAppFactory.CreateSigningKeyMaterial(
                        SecurityAppFactory.DefaultSigningKeySizeInBits),
            };

            // Every secret the shipped roster names has to resolve or this host does not start - the same
            // refusal, and the same one-declaration merge, the sibling reporting host relies on.
            foreach (KeyValuePair<string, string?> secret in SecurityAppFactory.RosterSecretOverrides())
            {
                settings[secret.Key] = secret.Value;
            }

            if (referenceOverride is not null)
            {
                // The FIRST element of the shipped empty array, which is how a configuration provider adds
                // to a collection: the key path carries the index.
                settings[$"{PermittedKeyRefsKey}:0"] = referenceOverride;
            }

            foreach (KeyValuePair<string, string?> setting in settings)
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.ConfigureAppConfiguration(configuration =>
            {
                ArgumentNullException.ThrowIfNull(configuration);

                _ = configuration.AddInMemoryCollection(settings);
            });

            builder.ConfigureLogging(logging =>
            {
                ArgumentNullException.ThrowIfNull(logging);

                // Lowered to Information because one arm of the record is written at that level and the
                // shipped filters this host inherits are Warning-and-above.
                _ = logging.AddProvider(recorder).SetMinimumLevel(LogLevel.Information);
            });
        }
    }

    /// <summary>One recorded log entry: its category, level and rendered text.</summary>
    /// <param name="Category">The category the record was written under.</param>
    /// <param name="Level">The level it was written at.</param>
    /// <param name="Message">The rendered message.</param>
    private sealed record LogRecord(string Category, LogLevel Level, string Message);

    /// <summary>A logger provider that keeps the rendered text of every record written through it.</summary>
    /// <remarks>
    /// The RENDERED message is the assertable artifact: a structured-state assertion cannot see a lost
    /// format argument, and a record whose single argument went missing would log an empty line.
    /// </remarks>
    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        /// <summary>Every record written through this provider, in order.</summary>
        internal List<LogRecord> Records { get; } = [];

        /// <inheritdoc />
        public ILogger CreateLogger(string categoryName) => new RecordingLogger(this, categoryName);

        /// <inheritdoc />
        public void Dispose()
        {
            // Nothing to release: the records are held in memory for the life of the test.
        }

        /// <summary>The logger the provider hands out.</summary>
        /// <param name="owner">The provider that keeps the records.</param>
        /// <param name="category">The category this logger writes under.</param>
        private sealed class RecordingLogger(RecordingLoggerProvider owner, string category) : ILogger
        {
            /// <inheritdoc />
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            /// <inheritdoc />
            public bool IsEnabled(LogLevel logLevel) => true;

            /// <inheritdoc />
            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                ArgumentNullException.ThrowIfNull(formatter);

                lock (owner.Records)
                {
                    owner.Records.Add(new LogRecord(category, logLevel, formatter(state, exception)));
                }
            }
        }
    }
}
