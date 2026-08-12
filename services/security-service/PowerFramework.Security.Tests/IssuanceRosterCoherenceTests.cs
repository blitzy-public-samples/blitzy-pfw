// ======================================================================================================
//  THE ADVERTISED ROSTER AND THE EFFECTIVE GRANT MATRIX ARE TWO SURFACES, AND ONLY ONE OF THEM DECIDES.
//
//  Security:Clients[n]:Audiences and :Scopes read in a settings file exactly like permissions. They are
//  not. They are bound onto RegisteredIssuanceClient.PermittedAudiences / PermittedScopes and then read by
//  NOTHING in production - the issuance decision is taken entirely against the matrix folded from
//  Security:Callers and Security:CallerAuthorizations. A value in the advertised roster can therefore
//  neither grant nor withhold anything, which is the shape TokenIssuer.cs itself condemns in terms: a shape
//  that binds but is never consulted is unreachable configuration that reads like working configuration.
//
//  AND THE SHIPPED CONFIGURATION DIVERGES. powerframework-gateway advertises the audience
//  powerframework-security and the scope ping, and is granted neither; in Development pfw-e2e-suite
//  advertises three audiences the matrix does not grant. So the divergence is not hypothetical, and that is
//  also why the composition root REPORTS rather than REFUSES: refusing would turn a documentation defect
//  into an outage, and enforcing the advertised roster would create a second permission gate able to
//  withhold what the matrix grants - divided authority over one decision, which is the very defect the
//  report exists to surface.
//
//  WHAT THESE ROWS COVER. The pure function, in both directions - advertised-but-not-granted, and granted
//  to a caller no credential roster names - plus the fold order, the ordinal comparison that matches the
//  enforcement point, the blank and untrimmed shapes, and the deliberate leniency on scopes. The last row
//  is the one that cannot be faked: it boots the REAL composition root on the service's OWN shipped
//  settings and asserts the warning actually reaches a logger, because a pure function nothing calls
//  reports nothing.
// ======================================================================================================

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PowerFramework.Security.Configuration;
using PowerFramework.Security.Tokens;

namespace PowerFramework.Security.Tests;

/// <summary>
/// Covers the issuance-roster coherence report: what it reports, what it deliberately does not, and that
/// the composition root actually emits it.
/// </summary>
public sealed class IssuanceRosterCoherenceTests
{
    /// <summary>The log category the composition root writes the report under.</summary>
    private const string ReportCategory = "PowerFramework.Security.IssuanceRosterCoherence";

    /// <summary>A caller the shipped roster carries, used by the host row.</summary>
    private const string ShippedCaller = "powerframework-gateway";

    /// <summary>An audience that caller advertises and the shipped matrix does not grant.</summary>
    private const string ShippedUngrantedAudience = "powerframework-security";

    /// <summary>
    /// A roster that advertises exactly what the matrix grants reports nothing at all.
    /// </summary>
    /// <remarks>
    /// THE POSITIVE ARM, AND IT IS LOAD BEARING. Without it every row below would pass against a report
    /// that fired unconditionally, and an operator with a coherent configuration would be told it was
    /// incoherent - which is how a diagnostic becomes noise and then becomes ignored.
    /// </remarks>
    [Fact]
    public void AnAgreeingRosterReportsNothing()
    {
        SecurityOptions options = new();
        AddClient(options, "caller-a", ["aud-1"], ["scope.read"]);
        AddAuthorization(options, "caller-a", "aud-1", ["scope.read"]);

        Assert.Empty(IssuanceRosterCoherence.Describe(options));
    }

    /// <summary>
    /// An advertised audience the matrix does not grant is reported, naming caller, audience and both
    /// surfaces.
    /// </summary>
    /// <remarks>
    /// BOTH CONFIGURATION SURFACES ARE NAMED ON PURPOSE. The reader has to know which of the two to change,
    /// and the answer depends on which they meant: widen the matrix, or stop advertising. A message naming
    /// only the divergence would leave them to guess where it lives.
    /// </remarks>
    [Fact]
    public void AnAdvertisedAudienceTheMatrixDoesNotGrantIsReported()
    {
        SecurityOptions options = new();
        AddClient(options, "caller-a", ["aud-1", "aud-2"], ["scope.read"]);
        AddAuthorization(options, "caller-a", "aud-1", ["scope.read"]);

        string message = Assert.Single(IssuanceRosterCoherence.Describe(options));

        Assert.Contains("aud-2", message, StringComparison.Ordinal);
        Assert.Contains("caller-a", message, StringComparison.Ordinal);
        Assert.Contains("Security:Clients", message, StringComparison.Ordinal);
        Assert.Contains("Security:CallerAuthorizations", message, StringComparison.Ordinal);

        // The granted audience is NOT named: a message that listed both would read as though the working
        // one were also a problem.
        Assert.DoesNotContain("aud-1", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An advertised scope that no grant for the caller includes is reported.
    /// </summary>
    /// <remarks>
    /// THE MESSAGE STATES THE CONSEQUENCE RATHER THAN ONLY THE MISMATCH, because the two possible outcomes
    /// differ: a request carrying only ungranted scopes is refused, while one carrying them alongside
    /// granted scopes is minted with the granted subset. An operator who read "mismatch" alone would not
    /// know which of those their callers are meeting.
    /// </remarks>
    [Fact]
    public void AnAdvertisedScopeNoGrantIncludesIsReported()
    {
        SecurityOptions options = new();
        AddClient(options, "caller-a", ["aud-1"], ["scope.read", "scope.write"]);
        AddAuthorization(options, "caller-a", "aud-1", ["scope.read"]);

        string message = Assert.Single(IssuanceRosterCoherence.Describe(options));

        Assert.Contains("scope.write", message, StringComparison.Ordinal);
        Assert.DoesNotContain("scope.read", message, StringComparison.Ordinal);
        Assert.Contains("minted with the granted subset only", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A scope granted under a DIFFERENT audience of the same caller is not reported.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>DELIBERATE LENIENCY, AND THIS ROW IS WHERE IT IS PINNED.</b> The advertised scope list is per
    /// CLIENT and carries no audience, while a grant is per caller-and-audience pair - so the two surfaces
    /// are not the same shape and an exact comparison is not available. Reporting a scope only when NO grant
    /// for that caller includes it is the conservative reading: it never claims a divergence the
    /// configuration does not have. A stricter per-audience comparison would report every caller with more
    /// than one audience, which is the normal shape, and a diagnostic that fires on the normal shape is one
    /// nobody reads.
    /// </remarks>
    [Fact]
    public void AScopeGrantedUnderADifferentAudienceOfTheSameCallerIsNotReported()
    {
        SecurityOptions options = new();
        AddClient(options, "caller-a", ["aud-1", "aud-2"], ["scope.read", "scope.write"]);
        AddAuthorization(options, "caller-a", "aud-1", ["scope.read"]);
        AddAuthorization(options, "caller-a", "aud-2", ["scope.write"]);

        Assert.Empty(IssuanceRosterCoherence.Describe(options));
    }

    /// <summary>
    /// A grant for a caller the credential roster does not name is reported as unreachable.
    /// </summary>
    /// <remarks>
    /// THE OTHER DIRECTION, AND IT IS A DIFFERENT DEFECT. This one is a permission nobody can exercise: no
    /// request can authenticate under a subject with no credential-roster entry, so the grant is dead
    /// configuration that reads as a live permission - which is exactly as misleading as the first
    /// direction and considerably more dangerous to reason about during a security review.
    /// </remarks>
    [Fact]
    public void AGrantForACallerTheCredentialRosterDoesNotNameIsReported()
    {
        SecurityOptions options = new();
        AddClient(options, "caller-a", ["aud-1"], ["scope.read"]);
        AddAuthorization(options, "caller-a", "aud-1", ["scope.read"]);
        AddAuthorization(options, "ghost-caller", "aud-1", ["scope.read"]);

        string message = Assert.Single(IssuanceRosterCoherence.Describe(options));

        Assert.Contains("ghost-caller", message, StringComparison.Ordinal);
        Assert.Contains("unreachable", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A case difference IS a divergence, because the enforcement point compares ordinally.
    /// </summary>
    /// <remarks>
    /// A CASE-INSENSITIVE COMPARISON HERE WOULD REPORT AGREEMENT WHERE THE ISSUER SEES NONE. The issuance
    /// decision compares an identity, an audience and a scope ordinally, so <c>Aud-1</c> and <c>aud-1</c>
    /// are two different audiences to it. A diagnostic that folded case would go quiet on precisely the
    /// configuration a reader is least likely to spot by eye.
    /// </remarks>
    [Fact]
    public void ACaseDifferenceIsReportedBecauseTheEnforcementPointComparesOrdinally()
    {
        SecurityOptions options = new();
        AddClient(options, "caller-a", ["Aud-1"], ["Scope.Read"]);
        AddAuthorization(options, "caller-a", "aud-1", ["scope.read"]);

        Assert.Equal(2, IssuanceRosterCoherence.Describe(options).Count);
    }

    /// <summary>
    /// A case difference in the CALLER IDENTITY is a divergence in both directions at once.
    /// </summary>
    /// <remarks>
    /// A SEPARATE ROW FROM THE AUDIENCE AND SCOPE CASE ROW, AND IT HAD TO BE. Those two compare inside the
    /// folded matrix; this one compares the matrix's own keys, so a comparer changed on one and not the other
    /// is invisible to the row that only exercises the other - which is exactly what a mutation of the outer
    /// comparer alone proved.
    /// <para>
    /// THREE MESSAGES, AND THE ARITHMETIC IS THE ASSERTION. To the issuer, <c>Caller-A</c> and
    /// <c>caller-a</c> are two different callers: the first advertises an audience and a scope nothing
    /// grants it, and the second holds a grant no credential-roster entry can be authenticated under. So a
    /// single mistyped letter produces one caller that cannot use what it advertises AND one grant nobody
    /// can reach - the two directions of the same defect, reported together.
    /// </para>
    /// </remarks>
    [Fact]
    public void ACaseDifferenceInTheCallerIdentityDivergesInBothDirections()
    {
        SecurityOptions options = new();
        AddClient(options, "Caller-A", ["aud-1"], ["scope.read"]);
        AddAuthorization(options, "caller-a", "aud-1", ["scope.read"]);

        IReadOnlyList<string> messages = IssuanceRosterCoherence.Describe(options);

        Assert.Equal(3, messages.Count);
        Assert.Contains(
            messages,
            message => message.Contains("Caller-A", StringComparison.Ordinal)
                && message.Contains("aud-1", StringComparison.Ordinal));
        Assert.Contains(
            messages,
            message => message.Contains("caller-a", StringComparison.Ordinal)
                && message.Contains("unreachable", StringComparison.Ordinal));
    }

    /// <summary>
    /// A nested caller grant counts as a grant, so the fold reaches both surfaces.
    /// </summary>
    /// <remarks>
    /// THE FOLD ORDER IS THE ENFORCEMENT POINT'S OWN: nested grants first, flat rows added on top. A
    /// diagnostic that read only the flat rows would report every deployment that expressed its grants
    /// nested - a false alarm on a configuration shape the options type explicitly supports.
    /// </remarks>
    [Fact]
    public void ANestedCallerGrantCountsAsAGrant()
    {
        SecurityOptions options = new();
        AddClient(options, "caller-a", ["aud-1"], ["scope.read"]);

        SecurityCallerOptions caller = new() { Identity = "caller-a" };
        SecurityCallerGrantOptions grant = new() { Audience = "aud-1" };
        grant.Scopes.Add("scope.read");
        caller.Grants.Add(grant);
        options.Callers.Add(caller);

        Assert.Empty(IssuanceRosterCoherence.Describe(options));
    }

    /// <summary>
    /// Two spellings of one scope that differ only in case are BOTH granted, so neither is reported.
    /// </summary>
    /// <remarks>
    /// THE ROW THAT PINS THE FOLD'S OWN SCOPE COMPARER, which no other row reaches. The nested surface and
    /// the flat row contribute to the SAME scope set, and that set is ordinal - so both spellings survive the
    /// fold and a client advertising both is coherent. Folded case-insensitively the second spelling would be
    /// swallowed on the way in and then reported as ungranted on the way out: a divergence manufactured by
    /// the diagnostic rather than found by it, which is the worst failure a diagnostic has available.
    /// <para>
    /// The shape is deliberately exotic and is not a claim that the host accepts it - <c>Describe</c>
    /// reproduces the fold and none of the startup refusals, so a configuration this suite folds may still be
    /// one the options validator declines. What is under test is the comparer, nothing else.
    /// </para>
    /// </remarks>
    [Fact]
    public void TwoScopeSpellingsDifferingOnlyInCaseAreBothGranted()
    {
        SecurityOptions options = new();
        AddClient(options, "caller-a", ["aud-1"], ["scope.read", "Scope.Read"]);

        SecurityCallerOptions caller = new() { Identity = "caller-a" };
        SecurityCallerGrantOptions grant = new() { Audience = "aud-1" };
        grant.Scopes.Add("scope.read");
        caller.Grants.Add(grant);
        options.Callers.Add(caller);

        AddAuthorization(options, "caller-a", "aud-1", ["Scope.Read"]);

        Assert.Empty(IssuanceRosterCoherence.Describe(options));
    }

    /// <summary>
    /// Blank and white-space entries are skipped rather than reported as divergences.
    /// </summary>
    /// <remarks>
    /// THESE SHAPES BELONG TO THE OPTIONS VALIDATOR, NOT TO THIS REPORT. A blank subject or a blank audience
    /// stops the host by name long before a diagnostic runs, so reporting them here would produce a second,
    /// vaguer message about a fault the reader has already been told about precisely.
    /// </remarks>
    [Fact]
    public void BlankEntriesAreSkippedRatherThanReported()
    {
        SecurityOptions options = new();
        AddClient(options, "caller-a", ["aud-1", "   ", ""], ["scope.read", " "]);
        AddAuthorization(options, "caller-a", "aud-1", ["scope.read"]);
        AddAuthorization(options, "   ", "aud-1", ["scope.read"]);
        AddClient(options, "  ", ["aud-9"], ["scope.nine"]);

        Assert.Empty(IssuanceRosterCoherence.Describe(options));
    }

    /// <summary>
    /// Surrounding white space is trimmed on both surfaces before they are compared.
    /// </summary>
    /// <remarks>
    /// A PADDED VALUE IS THE SAME VALUE, and it is the shape an environment-variable substitution produces
    /// most often. Comparing untrimmed would report a divergence between two spellings the enforcement point
    /// treats as one.
    /// </remarks>
    [Fact]
    public void SurroundingWhiteSpaceIsTrimmedBeforeComparison()
    {
        SecurityOptions options = new();
        AddClient(options, " caller-a ", [" aud-1 "], [" scope.read "]);
        AddAuthorization(options, "caller-a", "aud-1", ["scope.read"]);

        Assert.Empty(IssuanceRosterCoherence.Describe(options));
    }

    /// <summary>
    /// Both directions are reported together when both are present, one message each.
    /// </summary>
    /// <remarks>
    /// A REPORT THAT STOPPED AT THE FIRST DIVERGENCE WOULD MAKE AN OPERATOR ITERATE. Every divergence is
    /// stated in one startup, so one pass over the configuration can close all of them.
    /// </remarks>
    [Fact]
    public void EveryDivergencePresentIsReportedInOnePass()
    {
        SecurityOptions options = new();
        AddClient(options, "caller-a", ["aud-1", "aud-2"], ["scope.read", "scope.write"]);
        AddAuthorization(options, "caller-a", "aud-1", ["scope.read"]);
        AddAuthorization(options, "ghost-caller", "aud-1", ["scope.read"]);

        IReadOnlyList<string> messages = IssuanceRosterCoherence.Describe(options);

        Assert.Equal(3, messages.Count);
        Assert.Contains(messages, message => message.Contains("aud-2", StringComparison.Ordinal));
        Assert.Contains(messages, message => message.Contains("scope.write", StringComparison.Ordinal));
        Assert.Contains(messages, message => message.Contains("ghost-caller", StringComparison.Ordinal));
    }

    /// <summary>
    /// A null argument is refused rather than silently reporting nothing.
    /// </summary>
    [Fact]
    public void ANullOptionsArgumentIsRefused() =>
        Assert.Throws<ArgumentNullException>(() => IssuanceRosterCoherence.Describe(null!));

    /// <summary>
    /// The real composition root emits the report for the service's OWN shipped configuration.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>THE ROW A PURE-FUNCTION SUITE CANNOT REPLACE.</b> Every row above would keep passing if nothing
    /// called <c>Describe</c>, which is the state this whole item is about: a computed diagnostic nobody
    /// emits is exactly as useless as configuration nobody reads. This one boots the real host, on the
    /// service's own settings files, and asserts a Warning under the report's category actually reaches a
    /// logger.
    /// <para>
    /// IT IS ALSO A CENSUS OF THE SHIPPED CONFIGURATION. <c>powerframework-gateway</c> advertises the
    /// audience <c>powerframework-security</c> and the scope <c>ping</c>, and the matrix grants it neither.
    /// If a later change aligns the roster, this row fails - and that failure is the reminder to delete it
    /// and record the alignment, not a defect to work around.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheCompositionRootReportsTheShippedConfigurationsDivergence()
    {
        RecordingLoggerProvider recorder = new();

        using ReportingHost host = new(recorder);
        using HttpClient client = host.CreateClient();

        List<string> report =
        [
            .. recorder.Records
                .Where(static record =>
                    string.Equals(record.Category, ReportCategory, StringComparison.Ordinal)
                    && record.Level == LogLevel.Warning)
                .Select(static record => record.Message)
        ];

        Assert.NotEmpty(report);

        Assert.Contains(
            report,
            message => message.Contains(ShippedCaller, StringComparison.Ordinal)
                && message.Contains(ShippedUngrantedAudience, StringComparison.Ordinal));

        Assert.All(
            report,
            message => Assert.Contains("Security:Clients", message, StringComparison.Ordinal));
    }

    /// <summary>Adds one advertised client to the roster.</summary>
    /// <param name="options">The options to add to.</param>
    /// <param name="subject">The client's subject.</param>
    /// <param name="audiences">The audiences it advertises.</param>
    /// <param name="scopes">The scopes it advertises.</param>
    private static void AddClient(
        SecurityOptions options,
        string subject,
        IEnumerable<string> audiences,
        IEnumerable<string> scopes)
    {
        SecurityClientOptions client = new() { Subject = subject };

        foreach (string audience in audiences)
        {
            client.Audiences.Add(audience);
        }

        foreach (string scope in scopes)
        {
            client.Scopes.Add(scope);
        }

        options.Clients.Add(client);
    }

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

    /// <summary>
    /// A host of the REAL composition root carrying the service's own settings and a recording logger.
    /// </summary>
    /// <remarks>
    /// DELIBERATELY NOT <c>SecurityAppFactory</c>: that factory installs its own roster and grant matrix, so
    /// the report it produced would be a census of the harness rather than of the shipped configuration.
    /// This host supplies the ONE thing the settings files deliberately do not carry - signing material
    /// (constraint C-F) - and takes issuer, audiences, roster and matrix from the service's own files.
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

                // The filter is raised so the report is not silenced by the shipped Warning-and-above
                // filters this host inherits; the report is written at Warning, so this only guarantees it
                // is not suppressed rather than changing what is written.
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
    /// format argument, and a report whose single argument went missing would log an empty warning.
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
