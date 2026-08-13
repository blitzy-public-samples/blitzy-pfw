// ==================================================================================================
//  MutualTlsClientIdentityTests.cs
//  ------------------------------------------------------------------------------------------------
//  SUBJECT   PowerFramework.DataServices.DataServicesComposition.LoadSecurityClientIdentity
//            the X509Certificate2Collection singleton and its eager resolve in Program.cs
//            the primary message handler the SecurityClient registration composes
//
//  WHY THIS EDGE IS WORTH A FILE OF ITS OWN
//  ------------------------------------------------------------------------------------------------
//  `POST /v1/tokens` is protected by mutual TLS and by NOTHING ELSE, because a caller cannot present a
//  bearer credential in order to obtain its first bearer credential. DataServices requests tokens for
//  two purposes - its own C-02 cryptographic calls and the audience beneath it on C-05..C-08 - so a
//  handler that presents no client certificate obtains no token, and every authenticated call this
//  service would make becomes unreachable. That failure is silent at composition time and total at run
//  time, which is the worst combination a configuration surface can have.
//
//  THE PROPERTY THAT MATTERS IS INSTALLATION, NOT BINDING. `DataServicesOptionsTests.cs` already
//  asserts that the pair binds, that it is both-or-neither, and that the group has no member capable of
//  holding key material. None of that is evidence that the certificate reaches a socket: an options
//  group can bind perfectly while the handler that would present it is never told about it. The
//  container test below is therefore the load-bearing one - it walks the handler chain the deployed
//  registration composes and reads the identity off the primary handler.
//
//  NO REAL CREDENTIAL APPEARS ANYWHERE IN THIS FILE. Every certificate is generated in-process, per
//  test, on an ephemeral elliptic-curve key, and written to a per-test temporary directory that the
//  test deletes. Nothing is committed, nothing is copied from the repository, and in particular nothing
//  is taken from the read-only legacy asset `tests/blink/test_jws.htm` - the hardcoded-private-key
//  anti-pattern this whole edge replaces (constraint C-F).
//
//  WHY THE PATHS BELOW ARE ASSERTED *ABSENT* FROM EVERY MESSAGE. A path is not itself a credential, but
//  it names the location of one, and a startup log is exactly the wrong place to publish where a private
//  key is mounted. The refusals name the two CONFIGURATION KEYS instead, which is what an operator needs
//  in order to find the setting. The PRESERVED INNER CAUSE is a deliberate exception: the platform's own
//  file-system error names the path, and discarding it to keep the chain clean would trade a diagnosable
//  startup failure for a mysterious one. It is safe for the same reason the eager resolve is - a startup
//  exception reaches the operator channel and never a caller.
//
//  WHY THE SUBJECT OF THIS FILE CHANGED, AND WHAT WAS WRONG BEFORE. These tests used to call a helper
//  named `LoadMutualTlsClientIdentity`, which had NO production call site: the singleton registration in
//  `Program.cs` has always used `LoadSecurityClientIdentity`. Two near-identical loaders coexisted, and
//  the tested one was the dead one - so every assertion here about refusal wording described diagnostics
//  no deployment could ever emit. The two genuinely differed on that point: the dead helper quoted the
//  GROUP key plus bare property names, while the live one quotes both FULLY-QUALIFIED configuration keys,
//  which is why the two constants below are spelled out in full rather than reduced to the group. A test
//  that passes against an unreachable implementation is worse than no test, because it reports confidence
//  it has not earned. The duplicate is deleted and every assertion below now runs against the loader the
//  deployed registration calls.
// ==================================================================================================

using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PowerFramework.DataServices.Clients;
using PowerFramework.DataServices.Configuration;
using Xunit;

namespace PowerFramework.DataServices.Tests;

public sealed class MutualTlsClientIdentityTests
{
    /// <summary>The configuration key the certificate path binds from.</summary>
    private const string CertificatePathKey =
        $"{DataServicesOptions.SectionName}:{nameof(DataServicesOptions.Security)}:"
        + $"{nameof(SecurityClientOptions.MutualTls)}:"
        + nameof(MutualTlsClientOptions.CertificatePath);

    /// <summary>The configuration key the private-key path binds from.</summary>
    private const string CertificateKeyPathKey =
        $"{DataServicesOptions.SectionName}:{nameof(DataServicesOptions.Security)}:"
        + $"{nameof(SecurityClientOptions.MutualTls)}:"
        + nameof(MutualTlsClientOptions.CertificateKeyPath);

    /// <summary>The group prefix both refusals quote, so an operator can find the section.</summary>
    private const string GroupKey =
        $"{DataServicesOptions.SectionName}:{nameof(DataServicesOptions.Security)}:"
        + nameof(SecurityClientOptions.MutualTls);

    /// <summary>The name the client factory registers <see cref="SecurityClient"/>'s channel under.</summary>
    /// <remarks>
    /// ⚠ READ FROM THE CLIENT ITSELF, NEVER SPELLED HERE ⚠
    ///
    /// This was <c>nameof(SecurityClient)</c>, which is not the registered name: the registration uses the
    /// client's own published constant, "security-rest". Asking the factory for an unregistered name does
    /// not fail - it hands back a DEFAULT-configured handler - so the assertion below read a handler that
    /// had never been given a certificate and reported the installation missing when it was present. Reading
    /// the constant is what makes the test observe the registration rather than a coincidence of spelling.
    /// </remarks>
    private const string SecurityClientName = SecurityClient.HttpClientName;

    // ==============================================================================================
    //  THE LOADER - THE THREE STATES OF A CONFIGURED PAIR
    // ==============================================================================================

    /// <summary>
    /// An unset pair is a legitimate deployment rather than a fault, and it presents nothing.
    /// </summary>
    /// <remarks>
    /// THE STATE EVERY OTHER TEST IN THIS PROJECT DEPENDS ON. The shared fixture configures no client
    /// identity, so if an unset pair were a fault the whole suite would fail at host construction. It is
    /// asserted here explicitly rather than left as an inference from the suite starting: an unset pair
    /// means this deployment presents no client certificate and does not reach the issuance edge, which
    /// is a documented configuration and not a partial one.
    /// </remarks>
    [Fact]
    public void AnUnsetPairPresentsNoIdentityAndIsNotAFault()
    {
        MutualTlsClientOptions unset = new();

        Assert.False(unset.IsConfigured);

        X509Certificate2Collection identity =
            DataServicesComposition.LoadSecurityClientIdentity(unset);

        // EMPTY RATHER THAN NULL, deliberately: the handler registration then has one shape to handle
        // instead of two, and the eager resolve can assert that the singleton composed without asserting
        // that a certificate exists.
        Assert.Empty(identity);
    }

    /// <summary>
    /// Half a pair is refused by the loader, naming both keys and neither path.
    /// </summary>
    /// <param name="certificateConfigured">Whether the certificate path is set.</param>
    /// <param name="keyConfigured">Whether the private-key path is set.</param>
    /// <remarks>
    /// <para>
    /// DEFENCE IN DEPTH, ASSERTED AT THE DEPTH IT LIVES. The options validator refuses this pair on
    /// start, so in a composed host this loader never sees half a pair - which is precisely why the
    /// guard is worth a direct test: an unreachable guard that has never been executed is a guard
    /// nobody knows the behaviour of. Calling the loader directly is the only way to reach it.
    /// </para>
    /// <para>
    /// A certificate cannot complete a handshake without its key and a key has nothing to present
    /// without its certificate, so half a pair is unusable rather than merely weaker. Continuing with
    /// it would produce an outbound edge that looks configured and is not, which is the graceful
    /// degradation constraint C-B forbids - and on a security boundary the softening would be worst of
    /// all.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void HalfAPairIsRefusedByTheLoaderNamingBothKeysAndNeitherPath(
        bool certificateConfigured,
        bool keyConfigured)
    {
        const string certificatePath = "/nonexistent/dataservices-tests/half-configured-certificate.pem";
        const string keyPath = "/nonexistent/dataservices-tests/half-configured-key.pem";

        MutualTlsClientOptions half = new()
        {
            CertificatePath = certificateConfigured ? certificatePath : string.Empty,
            CertificateKeyPath = keyConfigured ? keyPath : string.Empty,
        };

        // IsConfigured is an OR over the two, so half a pair reaches the loader's own guard rather than
        // being answered as "unset" before it.
        Assert.True(half.IsConfigured);

        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(
            () => DataServicesComposition.LoadSecurityClientIdentity(half));

        // BOTH KEYS, FULLY QUALIFIED, AND THAT PRECISION IS THE POINT RATHER THAN PEDANTRY. An operator
        // has to be able to tell which half is missing, and a bare `CertificatePath` does not locate a
        // setting in a file with several sections. It is also the assertion that DISCRIMINATES this loader
        // from the dead duplicate this file used to exercise: that one quoted the group key plus the bare
        // property names, so it would satisfy a `Contains(GroupKey)` and a `Contains(nameof(...))` check
        // while failing these two. Asserting the group alone is what let the tests pass against an
        // implementation no deployment could reach.
        Assert.Contains(CertificatePathKey, refusal.Message, StringComparison.Ordinal);
        Assert.Contains(CertificateKeyPathKey, refusal.Message, StringComparison.Ordinal);

        // The group prefix follows from the two above, and is asserted separately so a future message that
        // dropped the section prefix from both keys still fails loudly.
        Assert.Contains(GroupKey, refusal.Message, StringComparison.Ordinal);

        Assert.DoesNotContain(certificatePath, refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(keyPath, refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A pair that cannot be read is refused with its cause preserved and neither path echoed.
    /// </summary>
    [Fact]
    public void AnUnreadablePairIsRefusedWithItsCausePreservedAndNeitherPathEchoed()
    {
        const string certificatePath = "/nonexistent/dataservices-tests/mtls-client-certificate.pem";
        const string keyPath = "/nonexistent/dataservices-tests/mtls-client-key.pem";

        MutualTlsClientOptions unreadable = new()
        {
            CertificatePath = certificatePath,
            CertificateKeyPath = keyPath,
        };

        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(
            () => DataServicesComposition.LoadSecurityClientIdentity(unreadable));

        // Fully qualified, for the reason the half-configured theory above records: it is what tells this
        // loader's diagnostics apart from the deleted duplicate's.
        Assert.Contains(CertificatePathKey, refusal.Message, StringComparison.Ordinal);
        Assert.Contains(CertificateKeyPathKey, refusal.Message, StringComparison.Ordinal);
        Assert.Contains(GroupKey, refusal.Message, StringComparison.Ordinal);

        Assert.DoesNotContain(certificatePath, refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(keyPath, refusal.Message, StringComparison.Ordinal);

        // The cause is preserved rather than swallowed, which is what makes the failure diagnosable. The
        // platform's own error names the path - see this file's header for why that is safe and
        // deliberate here while the composition root's own message is not permitted to.
        Assert.NotNull(refusal.InnerException);
    }

    /// <summary>
    /// A whitespace-only pair is treated as configured and refused, not silently read as unset.
    /// </summary>
    /// <remarks>
    /// THE DIRECTION THAT CATCHES A REAL MISTAKE. An operator who mounted a secret that resolved to
    /// whitespace has stated an intention to authenticate; reading that as "presents no certificate" and
    /// starting anyway would produce exactly the silent outage this file exists to prevent.
    /// <c>IsConfigured</c> is whitespace-aware in both directions, so the value never reaches
    /// <see cref="X509Certificate2.CreateFromPemFile"/> as an empty path.
    /// </remarks>
    [Fact]
    public void AWhitespaceOnlyPairIsNotMistakenForAnUnsetOne()
    {
        MutualTlsClientOptions whitespace = new()
        {
            CertificatePath = "   ",
            CertificateKeyPath = "\t",
        };

        Assert.False(whitespace.IsConfigured);

        // Consistent with the options group's own reading: whitespace is absence, and absence is not a
        // fault. Asserted so the pairing between IsConfigured and the loader cannot drift apart.
        Assert.Empty(DataServicesComposition.LoadSecurityClientIdentity(whitespace));
    }

    /// <summary>
    /// A real PEM pair loads as one identity that carries its private key.
    /// </summary>
    /// <remarks>
    /// THE PRIVATE KEY IS THE ASSERTION. A certificate without its key completes no handshake, so
    /// loading the certificate alone would satisfy a naive count assertion while presenting nothing
    /// usable. <c>CreateFromPemFile</c> is what pairs them, and this is the test that proves the pairing
    /// survived rather than that a file parsed.
    /// </remarks>
    [Fact]
    public void ARealPairLoadsAsOneIdentityCarryingItsPrivateKey()
    {
        using TemporaryClientIdentity material = TemporaryClientIdentity.Create();

        X509Certificate2Collection identity =
            DataServicesComposition.LoadSecurityClientIdentity(material.Options);

        X509Certificate2 loaded = Assert.Single(identity);

        try
        {
            Assert.True(loaded.HasPrivateKey);
            Assert.Equal(material.Thumbprint, loaded.Thumbprint, StringComparer.Ordinal);
        }
        finally
        {
            loaded.Dispose();
        }
    }

    // ==============================================================================================
    //  THE INSTALLATION - THE PROPERTY BINDING ALONE DOES NOT ESTABLISH
    // ==============================================================================================

    /// <summary>
    /// The loaded identity is installed on the primary handler the SecurityClient registration composes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE LOAD-BEARING TEST IN THIS FILE. Everything else here proves the material can be READ; this
    /// proves it is PRESENTED. The two are independent: the singleton can compose, the eager resolve can
    /// pass and the options can validate while the handler that would carry the certificate was never
    /// told about it - in which case the pair is dead configuration and the issuance edge is unreachable.
    /// </para>
    /// <para>
    /// BUILT FROM THE DEPLOYED REGISTRATION RATHER THAN THROUGH THE TEST HOST, and that is required
    /// rather than preferred: the shared fixture replaces every client's primary handler so that no
    /// request leaves the process, which would make an assertion about the primary handler vacuous. So
    /// this test composes the real container from the same two registration groups the composition root
    /// calls, and then asks the factory for the handler chain it would actually use.
    /// </para>
    /// <para>
    /// The chain is WALKED rather than assumed to be one deep, because the registration also attaches a
    /// standard resilience handler and the factory adds its own lifetime-tracking wrapper. Walking to the
    /// innermost non-delegating handler is what makes the assertion independent of how many delegating
    /// handlers sit in front of it.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheLoadedIdentityIsInstalledOnTheSecurityClientPrimaryHandler()
    {
        using TemporaryClientIdentity material = TemporaryClientIdentity.Create();

        using ServiceProvider provider = BuildClientContainer(material);

        using HttpMessageHandler chain = provider
            .GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(SecurityClientName);

        SocketsHttpHandler primary = Assert.IsType<SocketsHttpHandler>(InnermostHandler(chain));

        Assert.NotNull(primary.SslOptions.ClientCertificates);

        X509Certificate2 presented = Assert.IsType<X509Certificate2>(
            Assert.Single(primary.SslOptions.ClientCertificates!));

        Assert.Equal(material.Thumbprint, presented.Thumbprint, StringComparer.Ordinal);
        Assert.True(presented.HasPrivateKey);
    }

    /// <summary>
    /// Nothing about server-certificate validation is relaxed on the token-issuance edge.
    /// </summary>
    /// <remarks>
    /// CONSTRAINT C-G, ASSERTED AT THE ONE PLACE A HANDLER IS CONSTRUCTED BY HAND. A forged Security
    /// service would be a forged token issuer for the whole system, so trust is an orchestration concern
    /// - the CA a deployment mounts - and never a validation callback in code. Installing a client
    /// certificate is exactly the edit during which a validation callback gets added "to make the
    /// handshake work locally", so the absence is pinned by a test rather than by a comment.
    /// <para>
    /// EVERY TRUST SETTING IS COMPARED AGAINST A PRISTINE HANDLER RATHER THAN AGAINST A LITERAL, which is
    /// the difference between asserting non-relaxation and endorsing a particular value. A test that
    /// asserted, say, a specific revocation mode would encode this platform's default as a requirement
    /// this refactor never stated - and would then fail on a platform that defaults differently, for a
    /// reason having nothing to do with the code. What is asserted is the property that actually matters:
    /// the composition writes <c>ClientCertificates</c> and NOTHING else on the TLS surface.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoServerCertificateValidationIsRelaxedOnTheIssuanceEdge()
    {
        using TemporaryClientIdentity material = TemporaryClientIdentity.Create();

        using ServiceProvider provider = BuildClientContainer(material);

        using HttpMessageHandler chain = provider
            .GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(SecurityClientName);

        SocketsHttpHandler primary = Assert.IsType<SocketsHttpHandler>(InnermostHandler(chain));

        using SocketsHttpHandler pristine = new();

        Assert.Null(primary.SslOptions.RemoteCertificateValidationCallback);
        Assert.Null(primary.SslOptions.LocalCertificateSelectionCallback);

        Assert.Equal(
            pristine.SslOptions.CertificateRevocationCheckMode,
            primary.SslOptions.CertificateRevocationCheckMode);
        Assert.Equal(
            pristine.SslOptions.EnabledSslProtocols,
            primary.SslOptions.EnabledSslProtocols);
        Assert.Equal(
            pristine.SslOptions.EncryptionPolicy,
            primary.SslOptions.EncryptionPolicy);
        Assert.Equal(
            pristine.SslOptions.AllowRenegotiation,
            primary.SslOptions.AllowRenegotiation);
    }

    /// <summary>
    /// An unconfigured deployment composes a handler that presents no client certificate.
    /// </summary>
    /// <remarks>
    /// THE COMPLEMENT OF THE INSTALLATION TEST, AND IT ASSERTS AN ABSENCE THAT MATTERS. The property is
    /// left UNWRITTEN rather than assigned an empty collection: assigning one would be harmless but says
    /// less than leaving it alone, and a reader who found an empty collection there could not tell
    /// whether the deployment configured nothing or configured something that failed to load.
    /// </remarks>
    [Fact]
    public void AnUnconfiguredDeploymentPresentsNoClientCertificate()
    {
        using ServiceProvider provider = BuildClientContainer(material: null);

        using HttpMessageHandler chain = provider
            .GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(SecurityClientName);

        SocketsHttpHandler primary = Assert.IsType<SocketsHttpHandler>(InnermostHandler(chain));

        Assert.Null(primary.SslOptions.ClientCertificates);
    }

    // ==============================================================================================
    //  THE HOST - FAIL FAST RATHER THAN FAIL AT FIRST TOKEN REQUEST
    // ==============================================================================================

    /// <summary>
    /// A configured but unreadable client identity stops the HOST, not the first token request.
    /// </summary>
    /// <remarks>
    /// A deployment that meant to authenticate to the issuer and cannot has already lost every
    /// authenticated call it would make, so discovering that at startup is a failure to launch while
    /// discovering it at the first token request is an outage that looks like an upstream problem. AAP
    /// 0.1.4 requires the legacy's fail-fast posture survive as fail-fast rather than soften into
    /// warning-and-continue, and the eager resolve after <c>Build()</c> is what implements that. This
    /// test is what proves the resolve is actually reached.
    /// </remarks>
    [Fact]
    public async Task AnUnreadableClientIdentityStopsTheHostWithoutEchoingItsPath()
    {
        const string missingCertificate = "/nonexistent/dataservices-tests/host-certificate.pem";
        const string missingKey = "/nonexistent/dataservices-tests/host-key.pem";

        await using DataServicesTestHostFactory faultedHost = new();

        faultedHost.AdditionalSettings[CertificatePathKey] = missingCertificate;
        faultedHost.AdditionalSettings[CertificateKeyPathKey] = missingKey;

        Exception failure = Assert.ThrowsAny<Exception>(faultedHost.CreateAnonymousClient);

        Exception compositionRootFrame = FrameNaming(failure, GroupKey);

        Assert.Contains(
            nameof(MutualTlsClientOptions.CertificatePath),
            compositionRootFrame.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            nameof(MutualTlsClientOptions.CertificateKeyPath),
            compositionRootFrame.Message,
            StringComparison.Ordinal);

        Assert.DoesNotContain(missingCertificate, compositionRootFrame.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(missingKey, compositionRootFrame.Message, StringComparison.Ordinal);

        Assert.NotNull(compositionRootFrame.InnerException);
    }

    /// <summary>
    /// A half-configured client identity stops the host rather than presenting nothing and continuing.
    /// </summary>
    /// <param name="certificateConfigured">Whether the certificate path is set.</param>
    /// <param name="keyConfigured">Whether the private-key path is set.</param>
    /// <remarks>
    /// WHICH GUARD REFUSES IT IS ITSELF WORTH RECORDING. The options surface validates the pair on start,
    /// so that is the frame this test finds; the composition root carries an equivalent guard behind it,
    /// which is therefore defence in depth that no configuration can reach - and which
    /// <see cref="HalfAPairIsRefusedByTheLoaderNamingBothKeysAndNeitherPath"/> reaches directly. Asserting
    /// the reachable guard here rather than the shadowed one is deliberate: a test written against the
    /// unreachable frame would fail for a reason unrelated to the property, and the property is that the
    /// host does not start.
    /// </remarks>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task AHalfConfiguredClientIdentityStopsTheHost(
        bool certificateConfigured,
        bool keyConfigured)
    {
        const string certificatePath = "/nonexistent/dataservices-tests/host-half-certificate.pem";
        const string keyPath = "/nonexistent/dataservices-tests/host-half-key.pem";

        await using DataServicesTestHostFactory halfConfigured = new();

        halfConfigured.AdditionalSettings[CertificatePathKey] =
            certificateConfigured ? certificatePath : string.Empty;
        halfConfigured.AdditionalSettings[CertificateKeyPathKey] =
            keyConfigured ? keyPath : string.Empty;

        Exception failure = Assert.ThrowsAny<Exception>(halfConfigured.CreateAnonymousClient);

        Exception refusingFrame = FrameNaming(
            failure,
            nameof(MutualTlsClientOptions.CertificatePath));

        Assert.Contains(
            nameof(MutualTlsClientOptions.CertificateKeyPath),
            refusingFrame.Message,
            StringComparison.Ordinal);

        Assert.DoesNotContain(certificatePath, refusingFrame.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(keyPath, refusingFrame.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A readable pair starts the host and the identity singleton resolves to it.
    /// </summary>
    /// <remarks>
    /// THE POSITIVE ARM, WITHOUT WHICH THE NEGATIVE ARMS PROVE LESS THAN THEY APPEAR TO. A startup guard
    /// that refuses everything would pass every refusal test in this file. This one starts a real host
    /// over real material and reads the composed singleton back out, so the guard is shown to admit a
    /// correct configuration as well as to refuse the three wrong ones.
    /// </remarks>
    [Fact]
    public async Task AReadablePairStartsTheHostAndResolvesToTheConfiguredIdentity()
    {
        using TemporaryClientIdentity material = TemporaryClientIdentity.Create();

        await using DataServicesTestHostFactory configuredHost = new();

        configuredHost.AdditionalSettings[CertificatePathKey] = material.CertificatePath;
        configuredHost.AdditionalSettings[CertificateKeyPathKey] = material.CertificateKeyPath;

        // Constructing a client is what forces the host to build and run its startup guards, including
        // the eager resolve of the identity singleton.
        using HttpClient client = configuredHost.CreateAnonymousClient();

        X509Certificate2Collection identity =
            configuredHost.Services.GetRequiredService<X509Certificate2Collection>();

        X509Certificate2 composed = Assert.Single(identity);

        Assert.Equal(material.Thumbprint, composed.Thumbprint, StringComparer.Ordinal);
        Assert.True(composed.HasPrivateKey);
    }

    // ==============================================================================================
    //  HELPERS
    // ==============================================================================================

    /// <summary>
    /// Composes the real container from the two deployed registration groups the client edge needs.
    /// </summary>
    /// <param name="material">The generated identity, or <see langword="null"/> for an unset pair.</param>
    /// <returns>A provider the caller owns and must dispose.</returns>
    /// <remarks>
    /// Only the groups this edge reads are called - options, the clock seam and the clients - because
    /// calling the whole composition root would pull in the published gRPC surface and the domain
    /// registrations, none of which participates in composing a message handler. Both upstream addresses
    /// are supplied because both carry <c>[Required]</c>; the identity paths are the only variable.
    /// </remarks>
    private static ServiceProvider BuildClientContainer(TemporaryClientIdentity? material)
    {
        Dictionary<string, string?> settings = new(StringComparer.Ordinal)
        {
            [$"{DataServicesOptions.SectionName}:{nameof(DataServicesOptions.Persistence)}:"
                + nameof(PersistenceClientOptions.Address)] = "http://persistence:5101",
            [$"{DataServicesOptions.SectionName}:{nameof(DataServicesOptions.Security)}:"
                + nameof(SecurityClientOptions.BaseAddress)] = "https://security:5104",
        };

        if (material is not null)
        {
            settings[CertificatePathKey] = material.CertificatePath;
            settings[CertificateKeyPathKey] = material.CertificateKeyPath;
        }
        else
        {
            // EXACTLY ONE ISSUANCE CREDENTIAL IS ALWAYS SUPPLIED, AND WHICH ONE IS THE POINT OF THE ROW.
            //
            // C-01 publishes two schemes for POST /v1/tokens as a disjunction - an HTTP Basic client
            // credential or this client certificate - because a caller cannot present a bearer token in
            // order to obtain its first bearer token. A deployment that can present NEITHER obtains no token
            // at all, so the options validator refuses it at startup rather than letting it run and fail on
            // first use. A container built with no certificate AND no secret is therefore not "the
            // unconfigured posture", it is the refused one, and a row about presenting no CERTIFICATE has to
            // be a legal deployment that simply chose the other scheme.
            //
            // A FIXTURE-SHAPED VALUE, NEVER A REAL ONE: nothing here is presented to anything, and the key
            // is flat rather than sectioned because the environment-variable provider maps only a double
            // underscore onto ':' - which is why the composition root reads this one with an explicit
            // post-configure step instead of binding it (constraint C-F).
            settings[SecurityClientOptions.ClientSecretConfigurationKey] =
                "an-issuance-secret-shaped-value";
        }

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        ServiceCollection services = new();

        _ = services.AddLogging(static logging => logging.ClearProviders());

        return services
            .AddDataServicesOptions(configuration)
            .AddDataServicesDeterminismSeam()
            .AddDataServicesClients()
            .BuildServiceProvider();
    }

    /// <summary>Walks a handler chain to the innermost handler that delegates to nothing.</summary>
    /// <param name="handler">The outermost handler the factory produced.</param>
    /// <returns>The primary handler.</returns>
    private static HttpMessageHandler InnermostHandler(HttpMessageHandler handler)
    {
        HttpMessageHandler current = handler;

        while (current is DelegatingHandler delegating && delegating.InnerHandler is not null)
        {
            current = delegating.InnerHandler;
        }

        return current;
    }

    /// <summary>
    /// Finds the frame in an exception chain whose message names the given marker.
    /// </summary>
    /// <param name="failure">The exception the host surfaced.</param>
    /// <param name="marker">The text the refusing frame must contain.</param>
    /// <returns>The frame that made the claim.</returns>
    /// <remarks>
    /// The WHOLE chain is searched, because the test host surfaces a startup fault through however many
    /// layers the host builder wrapped it in, and asserting on the outermost message would assert on the
    /// wrapper rather than on the guard.
    /// </remarks>
    private static Exception FrameNaming(Exception failure, string marker)
    {
        for (Exception? current = failure; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains(marker, StringComparison.Ordinal))
            {
                return current;
            }
        }

        Assert.Fail(
            $"No frame in the exception chain named '{marker}'. Outermost message: {failure.Message}");

        // Unreachable: Assert.Fail always throws. Present because the compiler cannot know that.
        throw new InvalidOperationException();
    }

    /// <summary>
    /// A generated PEM certificate and key pair on disk, deleted with the instance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// GENERATED PER TEST AND NEVER COMMITTED. An elliptic-curve key rather than an RSA one, deliberately:
    /// nothing under test reads the key type - the loader pairs a certificate with a key and the handler
    /// presents the pair - so choosing the cheap one keeps a suite that starts real hosts from spending
    /// its time on key generation.
    /// </para>
    /// <para>
    /// The certificate is written as PEM and the key as PKCS#8 PEM in a separate file, which is the shape
    /// <c>docs/ARCHITECTURE.md</c>'s generation recipe produces and the shape the <c>*_MTLS_CERT_PATH</c>
    /// and <c>*_MTLS_KEY_PATH</c> variables name.
    /// </para>
    /// </remarks>
    private sealed class TemporaryClientIdentity : IDisposable
    {
        private readonly string _directory;

        private TemporaryClientIdentity(string directory, string thumbprint)
        {
            _directory = directory;
            Thumbprint = thumbprint;
            CertificatePath = Path.Combine(directory, "client.crt");
            CertificateKeyPath = Path.Combine(directory, "client.key");
        }

        /// <summary>The path the certificate was written to.</summary>
        internal string CertificatePath { get; }

        /// <summary>The path the private key was written to.</summary>
        internal string CertificateKeyPath { get; }

        /// <summary>The generated certificate's thumbprint, for identity assertions.</summary>
        internal string Thumbprint { get; }

        /// <summary>The two paths as the options group the loader reads.</summary>
        internal MutualTlsClientOptions Options => new()
        {
            CertificatePath = CertificatePath,
            CertificateKeyPath = CertificateKeyPath,
        };

        /// <summary>Generates a fresh pair and writes both halves to a new temporary directory.</summary>
        /// <returns>The material, which the caller owns and must dispose.</returns>
        internal static TemporaryClientIdentity Create()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "pfw-dataservices-mtls-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));

            _ = Directory.CreateDirectory(directory);

            using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            X500DistinguishedNameBuilder subject = new();
            subject.AddCommonName("powerframework-dataservices-tests");

            CertificateRequest request = new(subject.Build(), key, HashAlgorithmName.SHA256);

            using X509Certificate2 certificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-5),
                DateTimeOffset.UtcNow.AddMinutes(5));

            TemporaryClientIdentity material = new(directory, certificate.Thumbprint);

            File.WriteAllText(material.CertificatePath, certificate.ExportCertificatePem());
            File.WriteAllText(material.CertificateKeyPath, key.ExportPkcs8PrivateKeyPem());

            return material;
        }

        /// <summary>Deletes the generated material.</summary>
        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
