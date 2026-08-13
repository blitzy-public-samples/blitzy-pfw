// ======================================================================================================
//  ONE AUTHORITATIVE PERMISSION MODEL, AND A HOST THAT REFUSES TO START CARRYING A SECOND ONE.
//
//  The issuance decision is taken entirely against the deployment-wide audience roster and the grant matrix
//  folded from Security:Callers and Security:CallerAuthorizations. A SECOND surface describing the same
//  decision - Security:Clients[n]:Audiences and :Scopes - is the shape this file exists to keep out. Bound
//  and frozen onto RegisteredIssuanceClient it would be consulted by NOTHING, able neither to grant nor to
//  withhold, while reading to an operator as a permission: a directory advertising the audience
//  powerframework-security and the scope ping for a caller the matrix grants neither would have that operator
//  conclude a caller may address audiences it will in fact be refused for, and editing those lists to fix an
//  authorization problem would change nothing at all (CWE-16, CWE-863).
//
//  SO IT IS REFUSED, NOT ENFORCED. Enforcing it would create a second permission gate able to refuse what
//  the matrix grants, which is the divided authority TokenIssuer.cs rejects in terms. And its PRESENCE IS
//  FATAL: a binder silently drops a key no property matches, so a settings file carrying either member would
//  read as working authorization configuration and do nothing - invisible from the bound instance, because
//  the value never arrives. That is the whole fatal contract, and it is deliberately no wider: a duplicated
//  REPRESENTATION of the permission model refuses the host.
//
//  THE ROSTER/MATRIX CROSS-REFERENCE IS A DIFFERENT QUESTION AND IS REPORTED, NOT REFUSED. Security:Clients
//  says who may authenticate BY SHARED SECRET; the matrix says what an authenticated identity may REQUEST.
//  A grant naming a caller no credential entry names is usually a caller that authenticates BY CLIENT
//  CERTIFICATE, whose identity TokenEndpoints.ResolvePresentedIdentity reads from the certificate's common
//  name without consulting the roster at all - a shape SecurityOptions documents by making an entry's
//  secret-key name optional. Refusing it would make a supported topology unstartable, so it is stated at
//  Warning: the severity it held before this checkpoint, and the severity it earns.
//
//  WHAT THESE ROWS COVER. The pure report in both configuration shapes, the ordinal untrimmed comparison
//  that matches the enforcement point, the retired-key refusal in both spellings and past the end of the
//  bound roster, the one-message-per-startup contract, the deliberate NON-refusal of a certificate-only
//  grant, and - the row that cannot be faked - the REAL composition root booting on the service's OWN
//  shipped settings and reporting nothing.
// ======================================================================================================

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PowerFramework.Security.Configuration;
using PowerFramework.Security.Tokens;

namespace PowerFramework.Security.Tests;

/// <summary>
/// Covers the issuance-configuration authority check: what refuses a host, what is only reported, and that
/// the shipped configuration triggers neither.
/// </summary>
public sealed class IssuanceRosterAuthorityTests
{
    /// <summary>A caller the shipped credential directory carries.</summary>
    private const string ShippedCaller = "powerframework-gateway";

    /// <summary>
    /// A coherent deployment is accepted, and nothing is reported.
    /// </summary>
    /// <remarks>
    /// THE POSITIVE ARM, AND IT IS LOAD BEARING. Without it every row below would pass against a check that
    /// refused unconditionally, which would make every deployment unstartable - a far worse failure than the
    /// one being fixed.
    /// </remarks>
    [Fact]
    public void ACoherentDeploymentIsAccepted()
    {
        SecurityOptions options = new();
        AddClient(options, "caller-a");
        AddAuthorization(options, "caller-a", "aud-1", ["scope.read"]);

        Assert.Empty(IssuanceRosterAuthority.Describe(options));

        IssuanceRosterAuthority.Require(options, EmptyConfiguration());
    }

    /// <summary>
    /// A credential entry no grant names is accepted, because it is a legitimate state.
    /// </summary>
    /// <remarks>
    /// THE ASYMMETRY IS DELIBERATE AND IT MATTERS. An empty matrix is an accepted posture - a host serving
    /// the published key set, the discovery document, health and the whole cryptographic surface while
    /// minting nothing - so a credential entry with no grant cannot be fatal without making that posture
    /// unstartable. It is the OPPOSITE direction that is unreachable configuration: a grant nobody can
    /// authenticate as.
    /// </remarks>
    [Fact]
    public void ACredentialEntryWithNoGrantIsAccepted()
    {
        SecurityOptions options = new();
        AddClient(options, "caller-a");

        Assert.Empty(IssuanceRosterAuthority.Describe(options));
    }

    /// <summary>
    /// A grant for a caller the credential directory does not name is reported, in either shape.
    /// </summary>
    /// <param name="shape">Which configuration shape states the unreachable grant.</param>
    /// <remarks>
    /// BOTH SHAPES, because the issuer folds both into one matrix and a check that read only one would
    /// report a deployment as coherent purely because it expressed its grant in the other spelling.
    /// </remarks>
    [Theory]
    [InlineData("flat")]
    [InlineData("nested")]
    public void AGrantForACallerWithNoCredentialEntryIsReported(string shape)
    {
        SecurityOptions options = new();
        AddClient(options, "caller-a");
        AddAuthorization(options, "caller-a", "aud-1", ["scope.read"]);

        if (string.Equals(shape, "flat", StringComparison.Ordinal))
        {
            AddAuthorization(options, "a-caller-no-credential-names", "aud-1", ["scope.read"]);
        }
        else
        {
            AddNestedGrant(options, "a-caller-no-credential-names", "aud-1", ["scope.read"]);
        }

        string reported = Assert.Single(IssuanceRosterAuthority.Describe(options));

        Assert.Contains("a-caller-no-credential-names", reported, StringComparison.Ordinal);
        Assert.Contains("Security:Clients", reported, StringComparison.Ordinal);
        Assert.Contains("unreachable", reported, StringComparison.Ordinal);
    }

    /// <summary>
    /// A certificate-authenticated grant does NOT refuse the host.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ROW THAT KEEPS THE FATAL GATE HONEST, and it records a design decision taken deliberately rather
    /// than an omission. An intermediate revision of this check refused the host on the cross-reference too,
    /// on the reasoning that a grant nobody can authenticate as is dead configuration. It is not: the token
    /// endpoint's certificate arm reads the caller identity from the presented certificate's common name and
    /// never consults <c>Security:Clients</c>, and <c>SecurityOptions</c> documents that shape by making an
    /// entry's secret-key name optional. Refusing it would have made a supported topology unstartable - a
    /// diagnostic promoted into an outage, which is the mirror image of the defect being fixed.
    /// </para>
    /// <para>
    /// So the assertion is the ABSENCE of a throw, paired with the presence of a report: the deployment is
    /// flagged for an operator and allowed to run.
    /// </para>
    /// </remarks>
    [Fact]
    public void ACertificateAuthenticatedGrantIsReportedWithoutRefusingTheHost()
    {
        SecurityOptions options = new();
        AddClient(options, "caller-a");
        AddAuthorization(options, "a-caller-authenticating-by-certificate", "aud-1", ["scope.read"]);

        Assert.NotEmpty(IssuanceRosterAuthority.Describe(options));

        IssuanceRosterAuthority.Require(options, EmptyConfiguration());
    }

    /// <summary>
    /// The subject is compared exactly as the enforcement point compares it: ordinally and untrimmed.
    /// </summary>
    /// <remarks>
    /// THE OPTIONS TYPE DOCUMENTS THAT A SUBJECT IS NEITHER TRIMMED NOR REPAIRED - it reaches the token's
    /// subject claim verbatim, and <c>IssuanceClientRegistry</c> keys the roster on it under an ordinal
    /// comparer - while the matrix is keyed on the trimmed identity. So a directory entry carrying a leading
    /// space and a grant naming the trimmed form describe two different callers to the issuer, and this
    /// check must say so rather than reporting agreement authentication will not honour. Trimming BOTH sides
    /// would report the pair as agreeing, which is the one reading it must never produce.
    /// </remarks>
    [Fact]
    public void ASubjectIsComparedAsTheEnforcementPointComparesIt()
    {
        SecurityOptions options = new();
        AddClient(options, " caller-a");
        AddAuthorization(options, "caller-a", "aud-1", ["scope.read"]);

        string reported = Assert.Single(IssuanceRosterAuthority.Describe(options));

        Assert.Contains("caller-a", reported, StringComparison.Ordinal);
    }

    /// <summary>
    /// A retired per-client permission key refuses the host, naming the key and the matrix that replaced it.
    /// </summary>
    /// <param name="member">The retired member spelling.</param>
    /// <remarks>
    /// <para>
    /// A BINDER SILENTLY DROPS A KEY NO PROPERTY MATCHES, which is why this screen exists at all: a
    /// deployment carrying the retired member forward from an older settings file would look configured and
    /// do nothing - the original defect in a new dress, and undetectable from the options instance because
    /// the value never reaches it.
    /// </para>
    /// <para>
    /// The message must name the SURVIVING surface as well as the offending key, because the fix is to state
    /// the permission somewhere else rather than merely to delete a line.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("Audiences")]
    [InlineData("Scopes")]
    public void ARetiredPerClientPermissionKeyRefusesTheHost(string member)
    {
        SecurityOptions options = new();
        AddClient(options, "caller-a");
        AddAuthorization(options, "caller-a", "aud-1", ["scope.read"]);

        IConfiguration configuration = Configuration(
            [new($"{SecurityOptions.SectionName}:Clients:0:{member}:0", "a-value-nothing-reads")]);

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => IssuanceRosterAuthority.Require(options, configuration));

        Assert.Contains(
            $"{SecurityOptions.SectionName}:Clients:0:{member}",
            refused.Message,
            StringComparison.Ordinal);

        Assert.Contains(
            $"{SecurityOptions.SectionName}:{nameof(SecurityOptions.CallerAuthorizations)}",
            refused.Message,
            StringComparison.Ordinal);

        // AND THE VALUE IS NOT ECHOED. The key is a locator; the value is the deployment's own text.
        Assert.DoesNotContain("a-value-nothing-reads", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A retired key on an element PAST the bound roster is still caught.
    /// </summary>
    /// <remarks>
    /// THE CASE THAT NEEDS THE PROBE TO REACH ONE PAST THE END. An element whose only members were the
    /// retired ones binds nothing at all, so the bound collection is shorter than the configuration - and a
    /// probe bounded by the bound count would miss precisely the deployment that carried nothing but the
    /// retired keys.
    /// </remarks>
    [Fact]
    public void ARetiredKeyBeyondTheBoundRosterIsStillCaught()
    {
        SecurityOptions options = new();
        AddClient(options, "caller-a");

        IConfiguration configuration = Configuration(
            [new($"{SecurityOptions.SectionName}:Clients:1:Audiences:0", "an-audience")]);

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => IssuanceRosterAuthority.Require(options, configuration));

        Assert.Contains(
            $"{SecurityOptions.SectionName}:Clients:1:Audiences",
            refused.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Every offending key is named in one message, so an operator fixes them in one pass.
    /// </summary>
    /// <remarks>
    /// A HOST THAT REFUSES ONE FAULT AT A TIME COSTS ONE RESTART PER FAULT, and each restart looks like a
    /// new problem. The check therefore accumulates rather than short-circuiting - across both retired
    /// members and across every roster position, which is the case a settings file edited by hand produces.
    /// </remarks>
    [Fact]
    public void EveryOffendingKeyIsNamedInOneMessage()
    {
        SecurityOptions options = new();
        AddClient(options, "caller-a");
        AddClient(options, "caller-b");
        AddAuthorization(options, "caller-a", "aud-1", ["scope.read"]);

        IConfiguration configuration = Configuration(
        [
            new($"{SecurityOptions.SectionName}:Clients:0:Audiences:0", "an-audience"),
            new($"{SecurityOptions.SectionName}:Clients:0:Scopes:0", "a-scope"),
            new($"{SecurityOptions.SectionName}:Clients:1:Scopes:0", "another-scope"),
        ]);

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => IssuanceRosterAuthority.Require(options, configuration));

        Assert.Contains("Clients:0:Audiences", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Clients:0:Scopes", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Clients:1:Scopes", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>Both arguments are guarded.</summary>
    [Fact]
    public void BothArgumentsAreGuarded()
    {
        Assert.Throws<ArgumentNullException>(() => IssuanceRosterAuthority.Describe(null!));
        Assert.Throws<ArgumentNullException>(
            () => IssuanceRosterAuthority.Require(null!, EmptyConfiguration()));
        Assert.Throws<ArgumentNullException>(
            () => IssuanceRosterAuthority.Require(new SecurityOptions(), null!));
    }

    /// <summary>
    /// The REAL composition root starts on the service's own shipped settings, and reports nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ROW THAT CANNOT BE FAKED. Divergence between a credential directory advertising an audience and a
    /// scope and a matrix withholding both is the failure this whole file is about, and a composition root
    /// that merely logged a warning and started anyway would leave it in place. Booting the real host on the
    /// service's own files proves three things at once: the settings state their permissions in ONE place,
    /// they carry neither permission key on a directory entry, and the startup gate that would refuse a host
    /// carrying either is actually wired in.
    /// </para>
    /// <para>
    /// A HOST FAILING THE GATE WOULD NOT START, so resolving the options at all is itself half the
    /// evidence; the recorded log is the other half, because a report that is silenced is indistinguishable
    /// from one that found nothing to say.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheShippedConfigurationStartsAndReportsNothing()
    {
        RecordingLoggerProvider recorder = new();

        using ReportingHost host = new(recorder);

        // TOUCHING THE PROVIDER STARTS THE HOST, so the composition root's whole startup validation - the
        // check under test included - has already run and passed by the time the next line reads anything.
        SecurityOptions shipped = host.Services.GetRequiredService<IOptions<SecurityOptions>>().Value;

        Assert.NotEmpty(shipped.Clients);
        Assert.Empty(IssuanceRosterAuthority.Describe(shipped));

        // EVERY SHIPPED CREDENTIAL IDENTITY IS ONE THE MATRIX GRANTS SOMETHING, which is the direction this
        // check deliberately does not refuse but the shipped configuration nevertheless satisfies.
        HashSet<string> granted =
        [
            .. IssuanceFixture
                .EffectiveGrants(shipped)
                .Where(static grant => grant.Scopes.Count > 0)
                .Select(static grant => grant.Caller),
        ];

        Assert.Contains(ShippedCaller, granted);

        Assert.DoesNotContain(
            recorder.Records,
            record => record.Level >= LogLevel.Warning
                && record.Message.Contains("Security:Clients", StringComparison.Ordinal));
    }

    /// <summary>Adds one credential-directory entry.</summary>
    /// <param name="options">The options to add to.</param>
    /// <param name="subject">The subject the entry names.</param>
    private static void AddClient(SecurityOptions options, string subject) =>
        options.Clients.Add(new SecurityClientOptions { Subject = subject });

    /// <summary>Adds one flat authorization row to the grant matrix.</summary>
    /// <param name="options">The options to add to.</param>
    /// <param name="caller">The caller the row grants to.</param>
    /// <param name="audience">The audience the row grants.</param>
    /// <param name="scopes">The scopes the row grants.</param>
    private static void AddAuthorization(
        SecurityOptions options,
        string caller,
        string audience,
        IEnumerable<string> scopes)
    {
        CallerAuthorizationOptions row = new() { Caller = caller, Audience = audience };

        foreach (string scope in scopes)
        {
            row.Scopes.Add(scope);
        }

        options.CallerAuthorizations.Add(row);
    }

    /// <summary>Adds one nested caller grant to the grant matrix.</summary>
    /// <param name="options">The options to add to.</param>
    /// <param name="caller">The caller identity.</param>
    /// <param name="audience">The audience granted.</param>
    /// <param name="scopes">The scopes granted.</param>
    private static void AddNestedGrant(
        SecurityOptions options,
        string caller,
        string audience,
        IEnumerable<string> scopes)
    {
        SecurityCallerGrantOptions grant = new() { Audience = audience };

        foreach (string scope in scopes)
        {
            grant.Scopes.Add(scope);
        }

        SecurityCallerOptions declared = new() { Identity = caller };
        declared.Grants.Add(grant);

        options.Callers.Add(declared);
    }

    /// <summary>A configuration root carrying nothing.</summary>
    /// <returns>The empty root.</returns>
    private static IConfiguration EmptyConfiguration() => Configuration([]);

    /// <summary>A configuration root carrying exactly the supplied entries.</summary>
    /// <param name="entries">The entries.</param>
    /// <returns>The root.</returns>
    private static IConfiguration Configuration(KeyValuePair<string, string?>[] entries) =>
        new ConfigurationBuilder().AddInMemoryCollection(entries).Build();

    /// <summary>
    /// A host of the REAL composition root carrying the service's own settings and a recording logger.
    /// </summary>
    /// <remarks>
    /// DELIBERATELY NOT <c>SecurityAppFactory</c>: that factory installs its own roster and grant matrix, so
    /// what it proved would be a census of the harness rather than of the shipped configuration. This host
    /// supplies the ONE thing the settings files deliberately do not carry - signing material (constraint
    /// C-F) - and takes issuer, audiences, credential directory and matrix from the service's own files.
    /// </remarks>
    private sealed class ReportingHost : WebApplicationFactory<Program>
    {
        /// <summary>The provider every record is written through.</summary>
        private readonly RecordingLoggerProvider _recorder;

        /// <summary>Builds a host that records what its composition root logs.</summary>
        /// <param name="recorder">The recording provider.</param>
        internal ReportingHost(RecordingLoggerProvider recorder) => _recorder = recorder;

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

            // EVERY SECRET THE SHIPPED ROSTER NAMES HAS TO RESOLVE OR THIS HOST DOES NOT START, and that
            // refusal is a deliberate one: a roster entry naming a configuration key the orchestration
            // secret layer never populated is a misconfiguration the composition root reports by roster
            // position rather than tolerating. This row's subject is the ROSTER-versus-MATRIX coherence
            // check, not secret injection, so the same generated per-process values every other host in
            // this assembly uses are merged in from one declaration - which is also what keeps this host
            // from drifting when a roster entry is added to either settings file.
            foreach (KeyValuePair<string, string?> secret in SecurityAppFactory.RosterSecretOverrides())
            {
                settings[secret.Key] = secret.Value;
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

                // The filter is raised so nothing this row reads is silenced by the shipped
                // Warning-and-above filters this host inherits.
                _ = logging.AddProvider(_recorder).SetMinimumLevel(LogLevel.Warning);
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
    /// format argument, and a complaint whose single argument went missing would log an empty warning.
    /// </remarks>
    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        /// <summary>Every record written through this provider, in order.</summary>
        private readonly List<LogRecord> _records = [];

        /// <summary>A snapshot of every record, in arrival order.</summary>
        /// <remarks>
        /// THE APPEND WAS ALREADY LOCKED AND THE READ WAS NOT, which leaves an enumeration racing a writer -
        /// the half of the hazard that actually throws. This provider is installed into a running host, so
        /// the read copies under the same monitor the append takes.
        /// </remarks>
        internal IReadOnlyList<LogRecord> Records
        {
            get
            {
                lock (_records)
                {
                    return [.. _records];
                }
            }
        }

        /// <summary>Appends one record.</summary>
        /// <param name="record">The record to append.</param>
        internal void Add(LogRecord record)
        {
            lock (_records)
            {
                _records.Add(record);
            }
        }

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

                owner.Add(new LogRecord(category, logLevel, formatter(state, exception)));
            }
        }
    }
}
