// ==================================================================================================
//  THE STARTUP RECORD ABOUT THE ISSUANCE CREDENTIAL, AND ITS AGREEMENT WITH READINESS
//
//  WHAT THIS FILE EXISTS FOR. Contract C-01 accepts TWO caller credentials on `POST /v1/tokens` as
//  alternatives: a shared secret presented as an HTTP Basic credential, or a client certificate. The
//  documented bring-up uses the SECRET and leaves both certificate paths deliberately empty -
//  `orchestration/.env.example` section 6.3 states in as many words that presenting no client
//  certificate is a supported state.
//
//  THE DEFECT THESE ROWS PIN CLOSED. The composition root's startup record was gated on the resolved
//  certificate collection being EMPTY, and the message it wrote announced that the host "cannot obtain
//  a credential" and that "readiness reports the bootstrap as unavailable". On the documented bring-up
//  both halves were false: the host held a working secret, and `/health` reported the credentials check
//  HEALTHY at the same moment. An operator reading the two together had to decide which of their own
//  service's two statements to believe, and the false one was the one that reads as urgent.
//
//  AND REWORDING ALONE WOULD NOT HAVE BEEN ENOUGH, WHICH IS THE PART WORTH RECORDING. The condition was
//  UNREACHABLE in its intended meaning: `DataServicesOptionsValidator` refuses to start a host with no
//  issuance credential at all, and separately refuses a HALF-configured certificate pair, so by the time
//  that line ran the host provably held either a non-blank secret or a complete, loadable pair. The old
//  warning could therefore fire ONLY on the false positive - it had no true positive to report.
//
//  WHAT IS ASSERTED, AND WHY IT IS ASSERTED THIS WAY.
//
//    * The ABSENCE rows are the finding. They assert that no record at Warning or above claims a missing
//      credential on a host configured the way the orchestration layer documents. Absence is asserted
//      over the WHOLE operator channel rather than over one category, because a record's category is not
//      what makes it contradict readiness - its content is.
//
//    * The PRESENCE row is the paired positive control. Asserting only absence would pass on a host that
//      had stopped saying anything at all, which would lose a genuinely useful startup statement: which
//      of the two schemes is in force is the first thing an operator wants when the issuance edge answers
//      401.
//
//    * The AGREEMENT row is the one that would have caught the original defect on its own. It reads the
//      readiness verdict and the startup record from the SAME host and requires them to agree, so a
//      future edit that moves one without the other fails here rather than in production.
//
//    * The C-F row asserts the secret itself never reaches the channel.
//
//  CONSTRAINT COMPLIANCE
//  ------------------------------------------------------------------------------------------------
//  C-F   NOTHING HARDCODED AND NOTHING ECHOED. The secret is generated per test process by the factory;
//        this file names it only in order to assert that no record contains it.
//  C-G   EVERY NEW BOUNDARY AUTHENTICATED. A startup record that tells an operator their credential is
//        missing when it is present invites them to "fix" a working credential path, so an accurate
//        record is part of keeping the boundary authenticated rather than cosmetic.
//  C-I   EACH SERVICE BUILDS AND TESTS INDEPENDENTLY. Every row is an in-memory host; no port is bound
//        and no sibling service is required.
// ==================================================================================================

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Microsoft.Extensions.Options;

using PowerFramework.DataServices.Configuration;

using Xunit;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// The startup record about the issuance credential says what is true, and agrees with readiness.
/// </summary>
public sealed class IssuanceCredentialStartupRecordTests
{
    /// <summary>
    /// Message fragments that would only appear in a record claiming the credential is missing.
    /// </summary>
    /// <remarks>
    /// THE FIRST TWO ARE VERBATIM FROM THE RECORD THIS REPLACED, so a revert would fail these rows rather
    /// than pass them. The third is a fragment of the REPLACEMENT's own neither-scheme branch, which is
    /// correct when it fires and is nonetheless wrong on this configuration - so it is listed too, and the
    /// rows below are about the CONFIGURATION rather than about any particular wording.
    /// </remarks>
    private static readonly string[] MissingCredentialClaims =
    [
        "cannot obtain a credential",
        "presents\nno certificate".Replace("\n", " ", StringComparison.Ordinal),
        "can present neither issuance credential",
    ];

    /// <summary>A fragment of the accurate record the composition root now writes.</summary>
    private const string ConfiguredClaim = "The token-issuance credential is configured";

    // ==============================================================================================
    //  THE FINDING: NO RECORD CLAIMS A MISSING CREDENTIAL ON THE DOCUMENTED BRING-UP.
    // ==============================================================================================

    /// <summary>
    /// A host holding a secret and no certificate writes no record claiming it has no credential.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THIS IS THE DOCUMENTED BRING-UP, NOT AN EDGE CASE. The factory supplies the issuance secret through
    /// the same flat configuration key a deployment uses and configures no certificate pair at all, which
    /// is precisely what <c>orchestration/.env.example</c> section 6.3 describes. A warning on this
    /// configuration is a warning on the supported path.
    /// </remarks>
    [Fact]
    public async Task ASecretOnlyHostWritesNoRecordClaimingItHasNoCredentialAsync()
    {
        StartupChannel channel = new();

        await using DataServicesTestHostFactory factory = Boot(channel);

        using HttpClient client = factory.CreateClient();

        // The premise: this host really is secret-only. Asserted rather than assumed, because a factory
        // that started mounting a certificate would make every row below vacuous.
        SecurityClientOptions security = ResolveSecurity(factory);

        Assert.False(string.IsNullOrWhiteSpace(security.ClientSecret));
        Assert.True(string.IsNullOrWhiteSpace(security.MutualTls.CertificatePath));
        Assert.True(string.IsNullOrWhiteSpace(security.MutualTls.CertificateKeyPath));

        foreach (string claim in MissingCredentialClaims)
        {
            Assert.DoesNotContain(
                channel.Records,
                record => record.Message.Contains(claim, StringComparison.OrdinalIgnoreCase));
        }

        // AND NOTHING AT WARNING OR ABOVE MENTIONS THE CREDENTIAL AT ALL on this configuration. Stated as
        // a level rule as well as a content rule, because a record that said the same thing at a calmer
        // level would still be telling an operator their working configuration is broken.
        Assert.DoesNotContain(
            channel.Records,
            record => record.Level >= LogLevel.Warning
                && record.Message.Contains("credential", StringComparison.OrdinalIgnoreCase));
    }

    // ==============================================================================================
    //  THE PAIRED POSITIVE CONTROL: IT STILL SAYS SOMETHING, AND WHAT IT SAYS IS ACCURATE.
    // ==============================================================================================

    /// <summary>
    /// The record names which of the two schemes is in force, at information level.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// WITHOUT THIS ROW THE ONE ABOVE WOULD PASS ON SILENCE, and silence is a worse answer than the
    /// warning was for the operator debugging a 401 from the issuance edge: the first question is always
    /// which credential the host believes it holds. Information rather than Trace, unlike the readiness
    /// path's healthy record, because a host starts once whereas the Compose health condition polls
    /// continuously.
    /// </remarks>
    [Fact]
    public async Task TheRecordNamesWhichSchemeIsInForceAsync()
    {
        StartupChannel channel = new();

        await using DataServicesTestHostFactory factory = Boot(channel);

        using HttpClient client = factory.CreateClient();

        (LogLevel Level, string Message) record = Assert.Single(
            channel.Records,
            entry => entry.Message.Contains(ConfiguredClaim, StringComparison.Ordinal));

        Assert.Equal(LogLevel.Information, record.Level);

        // The two flags, spelled as the record renders them: secret in force, certificate not.
        Assert.Contains("shared secret = True", record.Message, StringComparison.Ordinal);
        Assert.Contains("client certificate = False", record.Message, StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  THE AGREEMENT ROW: THE RECORD AND THE READINESS VERDICT COME FROM ONE HOST AND MATCH.
    // ==============================================================================================

    /// <summary>
    /// The startup record and the credentials readiness verdict agree on the same host.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THIS IS THE ROW THAT WOULD HAVE CAUGHT THE DEFECT ON ITS OWN, and it is written as a relation
    /// rather than as two independent expectations. The original fault was not that either statement was
    /// individually implausible - it was that the two disagreed, and nothing anywhere required them to
    /// agree. A row that asserted "readiness is healthy" and a separate row that asserted "the log says
    /// X" could both pass while the pair remained contradictory.
    /// </para>
    /// <para>
    /// The verdict is read from the REGISTERED health check rather than from the probe body, because the
    /// body deliberately publishes no per-check detail to an anonymous caller - so the projection cannot
    /// be the source for a claim about one named check.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheStartupRecordAgreesWithTheCredentialsReadinessVerdictAsync()
    {
        StartupChannel channel = new();

        await using DataServicesTestHostFactory factory = Boot(channel);

        using HttpClient client = factory.CreateClient();

        HealthReport report = await factory.Services
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(
                registration => string.Equals(
                    registration.Name,
                    "credentials",
                    StringComparison.OrdinalIgnoreCase),
                TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Healthy, report.Status);

        // Readiness says the credential is available, so the startup record must say the same - and must
        // not additionally carry the contrary claim.
        Assert.Contains(
            channel.Records,
            record => record.Message.Contains(ConfiguredClaim, StringComparison.Ordinal));

        foreach (string claim in MissingCredentialClaims)
        {
            Assert.DoesNotContain(
                channel.Records,
                record => record.Message.Contains(claim, StringComparison.OrdinalIgnoreCase));
        }
    }

    // ==============================================================================================
    //  C-F: THE SECRET ITSELF NEVER REACHES THE CHANNEL.
    // ==============================================================================================

    /// <summary>
    /// No startup record carries the issuance secret.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE RECORD REPORTS A BOOLEAN, NOT A VALUE, and this row is what keeps that true as the message
    /// changes. Asserted as a boolean rather than with <c>Assert.DoesNotContain</c> over the message,
    /// because that overload renders both operands and would therefore print the secret at exactly the
    /// moment the defect it guards against is present.
    /// </remarks>
    [Fact]
    public async Task NoStartupRecordCarriesTheIssuanceSecretAsync()
    {
        StartupChannel channel = new();

        await using DataServicesTestHostFactory factory = Boot(channel);

        using HttpClient client = factory.CreateClient();

        string secret = DataServicesTestHostFactory.ConfiguredIssuanceSecret;

        Assert.False(string.IsNullOrWhiteSpace(secret));

        foreach ((LogLevel level, string message) in channel.Records)
        {
            Assert.False(
                message.Contains(secret, StringComparison.Ordinal),
                $"A startup record written at {level} carries the issuance secret.");
        }
    }

    // ==============================================================================================
    //  BUILDERS.
    // ==============================================================================================

    /// <summary>Boots a host whose operator channel is captured into <paramref name="channel"/>.</summary>
    /// <param name="channel">The channel every startup record is appended to.</param>
    /// <returns>The factory. The caller disposes it.</returns>
    /// <remarks>
    /// THE PROVIDER IS REGISTERED AS A SERVICE rather than through a logging-builder call, because that is
    /// the only seam this factory exposes - and it is equivalent: <c>AddProvider</c> is itself a singleton
    /// <see cref="ILoggerProvider"/> registration. The filter rules the copied settings file installs are
    /// left alone: both records this file cares about are written at Information or above, so nothing has
    /// to be relaxed in order to observe them, and relaxing it would make the rows depend on a filter
    /// state no deployment has.
    /// </remarks>
    private static DataServicesTestHostFactory Boot(StartupChannel channel)
    {
        DataServicesTestHostFactory factory = new();

        factory.AdditionalServiceConfiguration.Add(services =>
            services.AddSingleton<ILoggerProvider>(new StartupRecordingLoggerProvider(channel)));

        return factory;
    }

    /// <summary>Reads the bound issuance-credential group from a booted host.</summary>
    /// <param name="factory">The booted factory.</param>
    /// <returns>The group.</returns>
    private static SecurityClientOptions ResolveSecurity(DataServicesTestHostFactory factory) =>
        factory.Services
            .GetRequiredService<IOptions<DataServicesOptions>>()
            .Value
            .Security;
}

/// <summary>
/// The operator records one host wrote during startup, with the level each was written at.
/// </summary>
/// <remarks>
/// Held per host rather than in static state, so two rows can never observe each other's records.
/// </remarks>
internal sealed class StartupChannel
{
    /// <summary>Every record this host wrote, in order.</summary>
    public List<(LogLevel Level, string Message)> Records { get; } = [];
}

/// <summary>
/// A logging provider that appends every record from every category to a <see cref="StartupChannel"/>.
/// </summary>
/// <remarks>
/// EVERY CATEGORY, DELIBERATELY, unlike the readiness suite's provider which selects one. What makes a
/// startup record contradict readiness is its CONTENT rather than the category it happens to be written
/// under, and the composition root's records are written through the application logger whose category is
/// an implementation detail of the host builder. Selecting a category here would let a record move and the
/// rows keep passing on having observed nothing.
/// </remarks>
internal sealed class StartupRecordingLoggerProvider : ILoggerProvider
{
    private readonly StartupChannel _channel;

    /// <summary>Creates the provider over the caller's channel.</summary>
    /// <param name="channel">The channel every record is appended to.</param>
    public StartupRecordingLoggerProvider(StartupChannel channel) => _channel = channel;

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => new StartupRecordingLogger(_channel);

    /// <inheritdoc/>
    public void Dispose()
    {
    }

    private sealed class StartupRecordingLogger : ILogger
    {
        private readonly StartupChannel _channel;

        public StartupRecordingLogger(StartupChannel channel) => _channel = channel;

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
            => NullLogger.Instance.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            lock (_channel)
            {
                _channel.Records.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
