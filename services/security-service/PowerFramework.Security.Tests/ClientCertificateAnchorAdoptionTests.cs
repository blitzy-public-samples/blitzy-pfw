// ======================================================================================================
//  ONE PUBLISHED VARIABLE, TWO ANCHORS - AND THE ONE AN OPERATOR IS TOLD TO SET WAS NOT THE ONE ISSUANCE
//  READ.
//
//  Security decides TWICE about a caller certificate, and the two decisions belong to different layers:
//
//    * Security:MutualTls:ClientCaPath   the LISTENER's anchor. Read by the composition root and
//                                        installed as Kestrel's ClientCertificateValidation callback, so
//                                        the handshake completes at all. This is the ONLY one of the two
//                                        the orchestration layer publishes a variable for -
//                                        SECURITY_MTLS_CLIENT_CA_PATH.
//    * Security:ClientCertificateAuthorityPath   the ISSUANCE anchor. Read by Tokens/ClientCertificateTrust,
//                                        so a certificate that already completed a handshake may establish
//                                        an identity on POST /v1/tokens.
//
//  A deployment that followed the documented bootstrap therefore completed the handshake and was then
//  refused 401 on every certificate, while its HTTP Basic callers kept minting normally - measured, not
//  theorised. The composition root now ADOPTS the listener's anchor as the issuance anchor when the
//  issuance key is unset, and this file is the assertion that it does, that an explicit issuance anchor
//  still wins, that neither-configured stays fail-closed, and that a white-space issuance path is still
//  refused by name rather than silently repaired.
//
//  The second half of this file pins the WORDING of the fail-closed startup record. It used to say token
//  issuance "will refuse every request", which is false: C-01 accepts either an HTTP Basic credential from
//  the roster or a client certificate, and the Basic half is untouched by a missing anchor. An operator
//  reading the old record would go looking for a total outage that was not happening.
//
//  ASSERTED ON THE REAL HOST rather than on a restatement of it, because the adoption lives in the
//  composition root: a test that called the PostConfigure itself would pass while the host was wired
//  differently. Every row resolves IOptions<SecurityOptions> out of a started host.
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
/// Covers the issuance anchor's adoption of the listener anchor, and the wording of the fail-closed
/// startup record that reports having neither.
/// </summary>
public sealed class ClientCertificateAnchorAdoptionTests
{
    /// <summary>The configuration key the orchestration layer's published variable maps onto.</summary>
    private const string ListenerAnchorKey = "Security:MutualTls:ClientCaPath";

    /// <summary>The configuration key issuance reads, for which no variable is published.</summary>
    private const string IssuanceAnchorKey = "Security:ClientCertificateAuthorityPath";

    /// <summary>
    /// The documented bootstrap works: with only the published variable's key configured, the issuance
    /// anchor is the listener's.
    /// </summary>
    /// <remarks>
    /// THE ROW THE FINDING ASKED FOR. Before the adoption this resolved to an empty string, so
    /// <see cref="ClientCertificateTrust"/> established no identity and every certificate was refused
    /// <c>401</c> on a deployment that had configured exactly what its documentation told it to.
    /// </remarks>
    [Fact]
    public void TheIssuanceAnchorAdoptsTheListenerAnchorWhenItIsNotConfiguredItself()
    {
        string anchor = IssuanceFixture.ClientCertificateAuthorityPath;

        using AnchorHost host = new() { [ListenerAnchorKey] = anchor };

        SecurityOptions resolved = Resolve(host);

        Assert.Equal(anchor, resolved.ClientCertificateAuthorityPath);
        Assert.Equal(anchor, resolved.MutualTls.ClientCaPath);
        Assert.True(resolved.MutualTls.IsConfigured);
    }

    /// <summary>
    /// An explicitly configured issuance anchor is authoritative and is never replaced by the listener's.
    /// </summary>
    /// <remarks>
    /// THE TWO ANCHORS ARE ALLOWED TO DIFFER, which is why this is a fallback rather than a merge or a
    /// rename: a deployment may let its listener complete handshakes for a broader authority than issuance
    /// will honour identities from. A merge would remove that distinction silently, and this row is what
    /// stops one being introduced later.
    /// </remarks>
    [Fact]
    public void AnExplicitlyConfiguredIssuanceAnchorSurvivesADifferentListenerAnchor()
    {
        string issuance = IssuanceFixture.ClientCertificateAuthorityPath;
        string listener = Path.Combine(
            Path.GetDirectoryName(issuance) ?? Path.GetTempPath(),
            "listener-only-authority.pem");

        File.WriteAllText(listener, File.ReadAllText(issuance));

        try
        {
            using AnchorHost host = new()
            {
                [IssuanceAnchorKey] = issuance,
                [ListenerAnchorKey] = listener,
            };

            SecurityOptions resolved = Resolve(host);

            Assert.Equal(issuance, resolved.ClientCertificateAuthorityPath);
            Assert.NotEqual(resolved.MutualTls.ClientCaPath, resolved.ClientCertificateAuthorityPath);
        }
        finally
        {
            File.Delete(listener);
        }
    }

    /// <summary>
    /// With NEITHER anchor configured the issuance anchor stays empty, which is the fail-closed state.
    /// </summary>
    /// <remarks>
    /// Without this row the adoption could have been implemented as "point at something plausible", and a
    /// deployment that deliberately configures no client trust would have acquired one it never named.
    /// Unset is a legitimate state: the certificate credential is unavailable and the roster's Basic
    /// credential still mints.
    /// </remarks>
    [Fact]
    public void NeitherAnchorConfiguredLeavesTheIssuanceAnchorEmpty()
    {
        using AnchorHost host = new();

        SecurityOptions resolved = Resolve(host);

        Assert.Equal(string.Empty, resolved.ClientCertificateAuthorityPath);
        Assert.False(resolved.MutualTls.IsConfigured);
    }

    /// <summary>
    /// A white-space issuance path is NOT adopted over: the host refuses to start and names the key.
    /// </summary>
    /// <remarks>
    /// THE GUARD IS ON EMPTY, NOT ON WHITE SPACE, AND THIS IS THE ROW THAT PINS THE DIFFERENCE. A
    /// white-space path is a populated setting carrying nothing usable - the shape a substitution that
    /// expanded to nothing produces - and the options validator refuses the host for it by name. Falling
    /// back for it would silently repair a deployment that stated an intent it cannot meet, which is the
    /// one case where being helpful hides a fault.
    /// </remarks>
    [Fact]
    public void AWhiteSpaceIssuanceAnchorIsRefusedRatherThanAdoptedOver()
    {
        using AnchorHost host = new()
        {
            [IssuanceAnchorKey] = "   ",
            [ListenerAnchorKey] = IssuanceFixture.ClientCertificateAuthorityPath,
        };

        OptionsValidationException refusal =
            Assert.Throws<OptionsValidationException>(() => Resolve(host));

        Assert.Contains(
            refusal.Failures,
            failure => failure.Contains(IssuanceAnchorKey, StringComparison.Ordinal));
    }

    /// <summary>
    /// The fail-closed startup record names the CERTIFICATE credential and says the Basic one keeps
    /// minting.
    /// </summary>
    /// <remarks>
    /// ASSERTED ON THE RENDERED MESSAGE rather than on structured state, because the defect was the
    /// SENTENCE: a claim that issuance "will refuse every request" would send an operator after an outage
    /// that is not happening, and might have them restart or roll back a service whose primary credential
    /// path was working. Both configuration keys are named too, because either can now supply the anchor -
    /// naming only one would send an operator to the key their deployment does not use.
    /// </remarks>
    [Fact]
    public void TheFailClosedStartupRecordScopesItselfToTheCertificateCredential()
    {
        RecordingLogger<ClientCertificateTrust> log = new();

        using ClientCertificateTrust trust = new(
            Options.Create(new SecurityOptions()),
            TimeProvider.System,
            log);

        string record = Assert.Single(log.Records, entry => entry.Level == LogLevel.Warning).Message;

        Assert.Contains("CERTIFICATE-BASED token issuance is unavailable", record, StringComparison.Ordinal);
        Assert.Contains("HTTP Basic credential", record, StringComparison.Ordinal);
        Assert.Contains("continues to mint", record, StringComparison.Ordinal);
        Assert.Contains(IssuanceAnchorKey, record, StringComparison.Ordinal);
        Assert.Contains("Security:MutualTls:ClientCaPath", record, StringComparison.Ordinal);
        Assert.Contains("fail-closed state, not a fault", record, StringComparison.Ordinal);

        // THE CLAIM THAT WAS FALSE, ASSERTED ABSENT. Its removal is the finding, so a row that only
        // checked for the new wording would pass on a record that carried both.
        Assert.DoesNotContain("refuse every request", record, StringComparison.Ordinal);
    }

    /// <summary>
    /// A CONFIGURED anchor raises no fail-closed record at all.
    /// </summary>
    /// <remarks>
    /// The positive arm: without it the row above would pass against a trust layer that warned
    /// unconditionally, and an operator who had configured an anchor correctly would still be told the
    /// certificate credential was unavailable.
    /// </remarks>
    [Fact]
    public void AConfiguredAnchorRaisesNoFailClosedRecord()
    {
        RecordingLogger<ClientCertificateTrust> log = new();

        SecurityOptions options = new()
        {
            ClientCertificateAuthorityPath = IssuanceFixture.ClientCertificateAuthorityPath,
        };

        using ClientCertificateTrust trust = new(Options.Create(options), TimeProvider.System, log);

        Assert.DoesNotContain(log.Records, entry => entry.Level == LogLevel.Warning);
    }

    /// <summary>Starts the host and returns its resolved options.</summary>
    /// <param name="host">The host to start.</param>
    /// <returns>The bound and post-configured options the host actually runs on.</returns>
    /// <remarks>
    /// Creating a client is what forces the host to build, so the value returned here has been through the
    /// composition root's binding, post-configuration and validation exactly as a deployed process would.
    /// </remarks>
    private static SecurityOptions Resolve(AnchorHost host)
    {
        using HttpClient client = host.CreateClient();

        return host.Services.GetRequiredService<IOptions<SecurityOptions>>().Value;
    }

    /// <summary>
    /// A host of the REAL composition root, configured only through configuration keys.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>DELIBERATELY NOT <c>SecurityAppFactory</c>, AND THE REASON IS THE SUBJECT OF THESE ROWS.</b>
    /// That factory post-configures <c>ClientCertificateAuthorityPath</c> to its own fixture authority
    /// UNCONDITIONALLY and LAST, so every host it builds has an explicitly set issuance anchor - which is
    /// exactly the state in which the adoption must NOT happen. A row asserting the adoption through it
    /// would be asserting the harness's assignment, and a row asserting the fail-closed empty state would
    /// see the fixture path instead. This host therefore supplies nothing but configuration and leaves the
    /// composition root's own post-configure step as the last word, which is what a deployed process does.
    /// <para>
    /// The signing material is the one thing configuration must carry that the settings files deliberately
    /// do not (constraint C-F), so it is generated per host and passed under the same flat key the
    /// orchestration layer uses. Everything else - issuer, audiences, the issuance roster and the grant
    /// matrix - comes from the service's own shipped settings, which is what makes these rows assertions
    /// about the deployed configuration rather than about a fabricated one.
    /// </para>
    /// </remarks>
    private sealed class AnchorHost : WebApplicationFactory<Program>
    {
        /// <summary>The configuration overrides this host is built with.</summary>
        private readonly Dictionary<string, string?> _settings = new(StringComparer.Ordinal)
        {
            [SecurityOptions.SigningKeyEnvironmentVariableName] =
                SecurityAppFactory.CreateSigningKeyMaterial(
                    SecurityAppFactory.DefaultSigningKeySizeInBits),
        };

        /// <summary>Records one configuration override, for the collection initializer.</summary>
        /// <param name="key">The configuration key.</param>
        /// <returns>Never read; the setter is the whole point.</returns>
        internal string? this[string key]
        {
            get => _settings.TryGetValue(key, out string? value) ? value : null;
            set => _settings[key] = value;
        }

        /// <inheritdoc />
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            foreach (KeyValuePair<string, string?> setting in _settings)
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.ConfigureAppConfiguration(configuration =>
            {
                ArgumentNullException.ThrowIfNull(configuration);

                _ = configuration.AddInMemoryCollection(_settings);
            });
        }
    }

    /// <summary>A logger that keeps the RENDERED text of every record written through it.</summary>
    /// <typeparam name="TCategory">The category the logger is resolved for.</typeparam>
    /// <remarks>
    /// The rendered message is the assertable artifact: a structured-state assertion cannot see a lost
    /// format argument or a sentence that says more than it should, and both are what these rows are about.
    /// </remarks>
    private sealed class RecordingLogger<TCategory> : ILogger<TCategory>
    {
        /// <summary>The records written, in order.</summary>
        internal List<(LogLevel Level, string Message)> Records { get; } = [];

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

            Records.Add((logLevel, formatter(state, exception)));
        }
    }
}
