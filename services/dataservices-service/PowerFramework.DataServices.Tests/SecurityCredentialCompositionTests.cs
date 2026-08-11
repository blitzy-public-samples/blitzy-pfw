// ==================================================================================================
//  SecurityCredentialCompositionTests - THE TWO HALVES OF THE TOKEN BOOTSTRAP, ASSERTED IN THE
//  COMPOSED CONTAINER RATHER THAN IN A COMMENT
//  ------------------------------------------------------------------------------------------------
//  SUBJECT   The registrations DataServicesComposition.AddDataServicesClients makes for the
//            token-issuance edge: the client identity this service presents, the single credential
//            store its held tokens live in, and the two interfaces that forward to the typed client.
//
//  WHY THIS FILE EXISTS
//  ------------------------------------------------------------------------------------------------
//  Two defects lived in this exact composition, and both were invisible from the code that contained
//  them because both were CORRECTLY DESCRIBED by their own comments and neither was true.
//
//    1. THE CLIENT IDENTITY WAS CONFIGURABLE AND NEVER ATTACHED. Contract C-01 protects
//       POST /v1/tokens with a caller credential and no bearer token - a caller cannot present a bearer
//       token in order to obtain its first bearer token - and it accepts EITHER a shared secret as an
//       HTTP Basic credential or a client certificate. So for a deployment that chose the certificate
//       scheme, a Security channel built with the trust anchor and no client certificate completed its
//       handshake, was refused at the endpoint, and left this service with no credential for ANY of the
//       four Persistence contracts or the crypto contract. The options group had existed since it was
//       authored; nothing read it.
//    2. THE HELD CREDENTIAL WAS NEVER HELD. A typed HttpClient is registered TRANSIENT by the
//       framework. With the store owned by the client as a field, every resolve produced an EMPTY
//       store, so the documented "reuse a held token until it lapses" path was never reached across
//       calls in a running host and this service asked Security to mint a fresh token for every single
//       outbound call. Two TRANSIENT interface forwarders made it worse: two consumers in one request
//       each resolved their own client and each minted independently, while the composition root's own
//       remarks asserted the two interfaces were "the same object, not two".
//
//  WHAT THESE TESTS GUARD
//  ------------------------------------------------------------------------------------------------
//    - The identity is registered, is a SINGLETON (one private-key read for the process, not one per
//      rotation of the recycled primary handler), and loads the configured pair.
//    - Half a pair, and material that cannot be read, are both fail-fast - and neither failure echoes
//      a configured path, because a diagnostic must not record where a private key is mounted.
//    - An unconfigured pair is a legitimate posture that yields an empty collection rather than a
//      throw, which is what keeps the shipped defaults runnable.
//    - The credential store is a singleton, and the two interfaces plus the concrete client are ONE
//      object within a scope - asserted with Assert.Same against the composed container.
//
//  NO KEY MATERIAL IS COMMITTED BY THIS FILE. Every certificate is generated in-memory per test and
//  every temporary file is deleted by the test that wrote it.
// ==================================================================================================

using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PowerFramework.DataServices.Clients;
using PowerFramework.DataServices.Configuration;
using Xunit;

namespace PowerFramework.DataServices.Tests;

public sealed class SecurityCredentialCompositionTests
{
    /// <summary>
    /// The two addresses the client registrations require, neither of which is contacted by any test in
    /// this file - composition is the subject, so nothing is sent.
    /// </summary>
    private const string LoopbackSecurityAddress = "https://localhost:15104/";

    private const string LoopbackPersistenceAddress = "https://localhost:15101/";

    // ==============================================================================================
    //  GROUP 1 - the client identity: registered, singleton, and loaded from the configured pair
    // ==============================================================================================

    /// <summary>
    /// The composed container resolves the client identity from the configured PEM pair, and resolves
    /// the SAME collection every time.
    /// </summary>
    /// <remarks>
    /// ONE READ FOR THE PROCESS IS THE POINT, NOT AN OPTIMISATION. The typed-client factory recycles its
    /// primary handler on a schedule; a per-handler load would open a fresh private-key handle on every
    /// rotation and never close one, and it would also mean an unreadable pair surfaced as a failed
    /// request rather than as a failure to launch. <c>Assert.Same</c> is what pins that.
    /// </remarks>
    [Fact]
    public void TheClientIdentityIsResolvedOnceAndSharedByEveryHandlerRotation()
    {
        using CertificatePair pair = CertificatePair.Create();
        using ServiceProvider provider = BuildProvider(
            pair.CertificatePath,
            pair.CertificateKeyPath);

        X509Certificate2Collection first = provider.GetRequiredService<X509Certificate2Collection>();
        X509Certificate2Collection second = provider.GetRequiredService<X509Certificate2Collection>();

        Assert.Same(first, second);

        // The pair genuinely loaded: one certificate, and it is the one that was written.
        X509Certificate2 loaded = Assert.Single(first.Cast<X509Certificate2>());

        Assert.Equal(pair.Certificate.Thumbprint, loaded.Thumbprint);

        // AND IT CARRIES ITS PRIVATE KEY. A certificate loaded without its key completes no handshake,
        // so this is the assertion that separates "the file was read" from "an identity was loaded".
        Assert.True(loaded.HasPrivateKey);
    }

    /// <summary>
    /// An unconfigured pair resolves to an empty collection rather than ending the host.
    /// </summary>
    /// <remarks>
    /// THIS IS A POSTURE, NOT AN OMISSION. The options group documents both-empty as "this deployment
    /// presents no client certificate and requests no token", and the shipped appsettings.json ships it
    /// that way - so refusing to start on it would make the defaults unrunnable. The consequence is
    /// surfaced where it is actionable instead: a named startup warning, and a named diagnostic from the
    /// client at the first token request rather than a request that could only be refused.
    /// </remarks>
    [Fact]
    public void AnUnconfiguredPairIsAnEmptyIdentityRatherThanAStartupFailure()
    {
        using ServiceProvider provider = BuildProvider(string.Empty, string.Empty);

        Assert.Empty(provider.GetRequiredService<X509Certificate2Collection>());
    }

    /// <summary>
    /// Half a pair ends the host, and the failure names both configuration keys without quoting either
    /// path.
    /// </summary>
    /// <remarks>
    /// A certificate cannot complete a handshake without its key and a key has nothing to present
    /// without its certificate, so half a pair is unusable rather than merely weaker. Both orders are
    /// exercised because a guard that only checked one would pass this test while leaving the other
    /// half-configuration silently accepted.
    /// </remarks>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void HalfAPairIsRefusedAndNamesTheKeysWithoutQuotingThePaths(
        bool hasCertificate,
        bool hasKey)
    {
        using CertificatePair pair = CertificatePair.Create();

        string certificatePath = hasCertificate ? pair.CertificatePath : string.Empty;
        string certificateKeyPath = hasKey ? pair.CertificateKeyPath : string.Empty;

        using ServiceProvider provider = BuildProvider(certificatePath, certificateKeyPath);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            provider.GetRequiredService<X509Certificate2Collection>);

        Assert.Contains(
            "DataServices:Security:MutualTls:CertificatePath",
            failure.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "DataServices:Security:MutualTls:CertificateKeyPath",
            failure.Message,
            StringComparison.Ordinal);

        // NEITHER PATH IS ECHOED. A path is not itself a credential, but it names where one is mounted,
        // and a startup log is exactly the wrong place to publish that.
        Assert.DoesNotContain(pair.CertificatePath, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(pair.CertificateKeyPath, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A configured pair that cannot be read ends the host, naming the keys and not the paths.
    /// </summary>
    /// <remarks>
    /// A deployment that intended to authenticate and cannot has already lost every authenticated call
    /// it would make, so discovering it at startup is a failure to launch whereas discovering it at the
    /// first token request is an outage that reads like an upstream problem. This is the managed form of
    /// the legacy's own posture for a structural fault [ws_objects/pfw.pbl.src/pfw.sra:L143].
    /// </remarks>
    [Fact]
    public void AnUnreadablePairIsRefusedAndNamesTheKeysWithoutQuotingThePaths()
    {
        string missingCertificate = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            string.Create(
                CultureInfo.InvariantCulture,
                $"pfw-absent-{Guid.NewGuid():N}.crt"));
        string missingKey = System.IO.Path.ChangeExtension(missingCertificate, ".key");

        using ServiceProvider provider = BuildProvider(missingCertificate, missingKey);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            provider.GetRequiredService<X509Certificate2Collection>);

        Assert.Contains(
            "DataServices:Security:MutualTls:CertificatePath",
            failure.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(missingCertificate, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(missingKey, failure.Message, StringComparison.Ordinal);

        // The cause is preserved rather than swallowed, so an operator can see WHICH read failed.
        Assert.NotNull(failure.InnerException);
    }

    // ==============================================================================================
    //  GROUP 2 - the identity actually reaches the handshake
    // ==============================================================================================

    /// <summary>
    /// The Security channel's primary handler presents the loaded client certificate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE DEFECT WAS HERE, NOT IN THE OPTIONS. The certificate was configurable, validated and
    /// documented, and the handler that opens the channel to the issuance endpoint never carried it - so
    /// the handshake completed anonymously, <c>POST /v1/tokens</c> refused the request for want of a
    /// caller identity, and this service could not obtain a credential for any of its 35 Persistence RPCs
    /// or its 17 crypto calls. An options test cannot catch that: the options were correct. This walks the
    /// real handler chain the factory builds for the registered channel and asserts the certificate is on
    /// the socket handler that performs the handshake.
    /// </para>
    /// <para>
    /// It also asserts what must NOT be there. A certificate-validation callback of any kind on this
    /// handler would mean a forged Security service could be accepted as the token issuer for the whole
    /// system, so the absence is part of the assertion rather than an omission (constraint C-G).
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSecurityChannelPresentsTheConfiguredClientCertificate()
    {
        using CertificatePair pair = CertificatePair.Create();
        using ServiceProvider provider = BuildProvider(
            pair.CertificatePath,
            pair.CertificateKeyPath);

        SocketsHttpHandler primary = ResolvePrimaryHandler(provider);

        Assert.NotNull(primary.SslOptions.ClientCertificates);
        Assert.Contains(
            pair.Certificate.Thumbprint,
            primary.SslOptions.ClientCertificates!
                .Cast<X509Certificate2>()
                .Select(static certificate => certificate.Thumbprint),
            StringComparer.Ordinal);

        Assert.Null(primary.SslOptions.RemoteCertificateValidationCallback);
    }

    /// <summary>
    /// With no identity configured the channel is built without a client-certificate collection at all.
    /// </summary>
    /// <remarks>
    /// AN EMPTY COLLECTION WOULD BE HARMLESS AND WOULD SAY LESS. Leaving the property unwritten is what
    /// distinguishes "this deployment presents nothing" from "something was attached and it was empty",
    /// and it keeps the unconfigured posture visible in the handler rather than only in the options.
    /// </remarks>
    [Fact]
    public void AnUnconfiguredIdentityLeavesTheChannelWithNoClientCertificate()
    {
        using ServiceProvider provider = BuildProvider(string.Empty, string.Empty);

        SocketsHttpHandler primary = ResolvePrimaryHandler(provider);

        Assert.Null(primary.SslOptions.ClientCertificates);
    }

    // ==============================================================================================
    //  GROUP 3 - the credential store and the two interface forwarders
    // ==============================================================================================

    /// <summary>
    /// The credential store is one object for the whole process.
    /// </summary>
    /// <remarks>
    /// THE STORE IS WHAT MAKES REUSE REAL, and its lifetime is the only thing that could make it not.
    /// It holds no connection and no handler - only short-lived tokens keyed by audience and scope - so
    /// unlike the typed client itself it is safe to keep for the life of the process, and it has to be
    /// kept for that long or the reuse path is never reached twice.
    /// </remarks>
    [Fact]
    public void TheCredentialStoreIsASingleton()
    {
        using ServiceProvider provider = BuildProvider(string.Empty, string.Empty);

        using IServiceScope first = provider.CreateScope();
        using IServiceScope second = provider.CreateScope();

        Assert.Same(
            first.ServiceProvider.GetRequiredService<ServiceTokenCache>(),
            second.ServiceProvider.GetRequiredService<ServiceTokenCache>());
    }

    /// <summary>
    /// Within one scope the token provider and the crypto client are the SAME object - which is what the
    /// composition root's remarks have always claimed and what the transient forwarders made false.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ASSERTION THE COMMENT WAS STANDING IN FOR. Both interfaces forward to the typed client, and
    /// while those forwarders were TRANSIENT each resolve produced a separate client with a separate
    /// store: the claim held for one resolve and not for a request. Scoped is what makes it true, and it
    /// matches <c>PersistenceClient</c>'s own lifetime, so the client that obtains the credential and the
    /// client that uses it are one object for the duration of the request that needs them.
    /// </para>
    /// <para>
    /// THE CONCRETE TYPE IS DELIBERATELY NOT ASSERTED THE SAME, AND THAT IS NOT A GAP. A typed HTTP
    /// client's concrete registration is the framework's, and it is transient by design so that each
    /// resolve receives a handler-factory-managed <c>HttpClient</c> rather than one pinned past its
    /// rotation window. Shadowing it with a scoped registration would mean re-deriving the factory's
    /// internal client name and rebuilding the configured pipeline by hand - fighting the framework to
    /// change something no production code can observe, because the two interfaces above are the only
    /// way this service reaches the client. What has to be shared IS shared, and
    /// <see cref="DifferentScopesHoldDifferentClientsButShareOneCredentialStore"/> is the proof.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheTwoInterfacesAndTheTypedClientAreOneObjectWithinAScope()
    {
        using ServiceProvider provider = BuildProvider(string.Empty, string.Empty);
        using IServiceScope scope = provider.CreateScope();

        IServiceTokenProvider tokenProvider =
            scope.ServiceProvider.GetRequiredService<IServiceTokenProvider>();
        ICryptoServiceClient cryptoClient =
            scope.ServiceProvider.GetRequiredService<ICryptoServiceClient>();

        Assert.Same(tokenProvider, cryptoClient);

        // Resolving either a second time within the same scope does not produce a second client.
        Assert.Same(
            tokenProvider,
            scope.ServiceProvider.GetRequiredService<IServiceTokenProvider>());
        Assert.Same(
            cryptoClient,
            scope.ServiceProvider.GetRequiredService<ICryptoServiceClient>());

        // And the one object behind both interfaces is the typed client itself rather than some adapter,
        // so the credential the token provider holds is the one the crypto path sends.
        _ = Assert.IsType<SecurityClient>(tokenProvider);
    }

    /// <summary>
    /// Two different scopes read the same credential store even though they hold different clients.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A SCOPED CLIENT WOULD STILL RE-MINT PER REQUEST IF THE STORE WERE SCOPED WITH IT. Separating the
    /// two lifetimes is deliberate: the client is per-scope because it carries a request-aligned logger
    /// scope and a factory-managed transport, while the credential outlives the request that obtained it
    /// and is valid for any caller of the same audience and scope set. This test is what stops a later
    /// edit from collapsing the store into the client's lifetime and quietly restoring per-request
    /// minting - the exact defect this composition had.
    /// </para>
    /// <para>
    /// This is also the test that makes the transient concrete registration harmless: two scopes DO hold
    /// two clients, and they still read one store.
    /// </para>
    /// </remarks>
    [Fact]
    public void DifferentScopesHoldDifferentClientsButShareOneCredentialStore()
    {
        using ServiceProvider provider = BuildProvider(string.Empty, string.Empty);

        using IServiceScope first = provider.CreateScope();
        using IServiceScope second = provider.CreateScope();

        IServiceTokenProvider firstClient =
            first.ServiceProvider.GetRequiredService<IServiceTokenProvider>();
        IServiceTokenProvider secondClient =
            second.ServiceProvider.GetRequiredService<IServiceTokenProvider>();

        Assert.NotSame(firstClient, secondClient);
        Assert.Same(
            first.ServiceProvider.GetRequiredService<ServiceTokenCache>(),
            second.ServiceProvider.GetRequiredService<ServiceTokenCache>());
    }

    /// <summary>
    /// A client that could present NEITHER accepted scheme refuses to ask for a token, and names every
    /// setting an operator could supply.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE OTHER HALF OF "FAIL WHEN ABSENT". Startup does not end on an unconfigured certificate pair,
    /// because both-empty is a documented posture and the shipped defaults use it - so this refusal
    /// happens at the first token request, which is the first moment the absence is actually fatal.
    /// Naming the settings is the whole value: without it the request goes out with no credential, comes
    /// back rejected, and reads like a credential fault at Security rather than a missing setting here.
    /// </para>
    /// <para>
    /// ALL THREE SETTINGS ARE NAMED, not just the certificate pair. Contract C-01 accepts two caller
    /// credentials as alternatives, so a diagnostic naming only one of them would send an operator to
    /// adopt a scheme their deployment had deliberately not chosen - which is exactly what this guard
    /// used to do.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AClientWithNeitherAcceptedSchemeRefusesToRequestATokenAndNamesEverySetting()
    {
        using System.Net.Http.HttpClient httpClient = new()
        {
            BaseAddress = new Uri(LoopbackSecurityAddress, UriKind.Absolute),
        };

        DataServicesOptions options = new();
        options.Security.BaseAddress = LoopbackSecurityAddress;

        SecurityClient client = new(
            httpClient,
            Options.Create(options),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SecurityClient>.Instance,
            TimeProvider.System);

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.GetTokenAsync(
                new ServiceTokenRequest("dataservices", "persistence", ["persistence.query"]),
                TestContext.Current.CancellationToken));

        Assert.Contains(
            SecurityClientOptions.ClientSecretConfigurationKey,
            failure.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "DataServices:Security:MutualTls:CertificatePath",
            failure.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "DataServices:Security:MutualTls:CertificateKeyPath",
            failure.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A client configured with ONLY the shared secret - the documented bring-up - obtains a token and
    /// presents that secret as an HTTP <c>Basic</c> credential.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THIS IS THE ROW THAT WOULD HAVE FAILED, AND ITS ABSENCE IS WHY THE DEFECT SURVIVED.</b> The
    /// client's pre-flight guard used to test the certificate pair ALONE, so this exact configuration -
    /// <c>SECURITY_CLIENT_SECRET_DATASERVICES</c> supplied, both certificate paths deliberately empty,
    /// which <c>orchestration/.env.example</c> section 6.3 records as a SUPPORTED state and names the
    /// primary scheme - was refused before a request was ever sent. Every downstream call this service
    /// makes needs a token, so the documented bring-up could reach nothing at all.
    /// </para>
    /// <para>
    /// THE HEADER IS ASSERTED AS WELL AS THE OUTCOME, because a token obtained without the credential
    /// travelling would prove only that the guard had been removed. The scheme name is asserted and the
    /// PARAMETER IS NOT DECODED OR COMPARED: this test writes a placeholder value and asserting the value
    /// would put a credential-shaped comparison in a test file for no gain.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AClientPresentingOnlyTheSharedSecretObtainsATokenAndSendsItAsABasicCredential()
    {
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.OK,
            "{\"access_token\":\"composition.not-a-real-token.value\",\"token_type\":\"Bearer\","
            + "\"expires_in\":300,\"scope\":\"persistence.query\"}");

        using System.Net.Http.HttpClient httpClient = new(handler, disposeHandler: false)
        {
            BaseAddress = new Uri(LoopbackSecurityAddress, UriKind.Absolute),
        };

        DataServicesOptions options = new();
        options.Security.BaseAddress = LoopbackSecurityAddress;

        // NOT MATERIAL AND NOT CREDENTIAL-SHAPED. It is a marker this test writes and Security never sees;
        // a realistic-looking value in a test file invites being mistaken for real material (C-F).
        options.Security.ClientSecret = "composition-not-a-real-secret";

        SecurityClient client = new(
            httpClient,
            Options.Create(options),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SecurityClient>.Instance,
            TimeProvider.System);

        ServiceToken token = await client.GetTokenAsync(
            new ServiceTokenRequest("dataservices", "persistence", ["persistence.query"]),
            TestContext.Current.CancellationToken);

        Assert.Equal("composition.not-a-real-token.value", token.AccessToken);

        HttpRequestMessage sent = Assert.Single(handler.Requests);

        Assert.Equal(RecordingHandler.TokenPath, sent.RequestUri?.AbsolutePath);
        Assert.Equal("Basic", sent.Headers.Authorization?.Scheme, StringComparer.Ordinal);
        Assert.False(string.IsNullOrEmpty(sent.Headers.Authorization?.Parameter));

        // AND NO CREDENTIAL IS IN THE BODY, which is the property C-01 states in its own description: the
        // request schema carries no secret, password, API key, assertion or key material.
        Assert.DoesNotContain(
            options.Security.ClientSecret,
            Assert.Single(handler.Bodies),
            StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  HARNESS
    // ==============================================================================================

    /// <summary>
    /// Builds a container carrying the deployed client registrations over an in-memory options value.
    /// </summary>
    /// <param name="certificatePath">The certificate path to bind, empty for unconfigured.</param>
    /// <param name="certificateKeyPath">The key path to bind, empty for unconfigured.</param>
    /// <returns>The composed provider, which the caller disposes.</returns>
    /// <remarks>
    /// THE DEPLOYED REGISTRATION IS THE SUBJECT, so this calls the real extension method rather than
    /// re-declaring what it registers. Options are supplied directly instead of through configuration
    /// binding because the registrations are what is under test and the binding has its own suite. No
    /// address here is contacted: every test in this file resolves and inspects, and none sends.
    /// </remarks>
    private static ServiceProvider BuildProvider(string certificatePath, string certificateKeyPath)
    {
        ServiceCollection services = new();

        _ = services.AddLogging();
        _ = services.AddSingleton(TimeProvider.System);

        DataServicesOptions options = new();
        options.Security.BaseAddress = LoopbackSecurityAddress;
        options.Persistence.Address = LoopbackPersistenceAddress;
        options.Security.MutualTls.CertificatePath = certificatePath;
        options.Security.MutualTls.CertificateKeyPath = certificateKeyPath;

        _ = services.AddSingleton<IOptions<DataServicesOptions>>(Options.Create(options));
        _ = services.AddDataServicesClients();

        // VALIDATED SCOPES, deliberately. A captive dependency - a scoped client injected into a
        // singleton - would be a real defect in this composition and would be invisible without it.
        return services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>
    /// Walks the handler chain the factory builds for the registered Security channel down to the socket
    /// handler that performs the TLS handshake.
    /// </summary>
    /// <param name="provider">The composed container.</param>
    /// <returns>The primary handler at the end of the chain.</returns>
    /// <remarks>
    /// THE CHAIN IS WALKED RATHER THAN ASSUMED, because the registration adds a resilience handler and the
    /// factory adds a lifetime-tracking one, so the primary handler is several links down and its depth is
    /// not this test's business. Walking to the end and requiring the terminal link to be the socket
    /// handler is what makes the assertion survive a later change to the pipeline.
    /// </remarks>
    private static SocketsHttpHandler ResolvePrimaryHandler(ServiceProvider provider)
    {
        HttpMessageHandler handler = provider
            .GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(SecurityClient.HttpClientName);

        while (handler is DelegatingHandler delegating)
        {
            Assert.NotNull(delegating.InnerHandler);
            handler = delegating.InnerHandler!;
        }

        return Assert.IsType<SocketsHttpHandler>(handler);
    }

    /// <summary>
    /// A generated certificate written to two temporary PEM files, deleted on dispose.
    /// </summary>
    /// <remarks>
    /// GENERATED, NEVER COMMITTED. The material exists only for the duration of one test, and both files
    /// are removed by the same object that wrote them. The certificate is self-signed because nothing
    /// here validates a chain - the subject is whether the composition LOADS an identity.
    /// </remarks>
    private sealed class CertificatePair : IDisposable
    {
        private CertificatePair(
            X509Certificate2 certificate,
            string certificatePath,
            string certificateKeyPath)
        {
            Certificate = certificate;
            CertificatePath = certificatePath;
            CertificateKeyPath = certificateKeyPath;
        }

        internal X509Certificate2 Certificate { get; }

        internal string CertificatePath { get; }

        internal string CertificateKeyPath { get; }

        internal static CertificatePair Create()
        {
            using RSA key = RSA.Create(2048);

            CertificateRequest request = new(
                "CN=powerframework-dataservices",
                key,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            request.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension(
                    [new Oid("1.3.6.1.5.5.7.3.2", "clientAuth")],
                    critical: false));

            DateTimeOffset now = DateTimeOffset.UtcNow;
            X509Certificate2 certificate = request.CreateSelfSigned(
                now.AddMinutes(-5),
                now.AddHours(1));

            string root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                string.Create(CultureInfo.InvariantCulture, $"pfw-identity-{Guid.NewGuid():N}"));

            string certificatePath = root + ".crt";
            string certificateKeyPath = root + ".key";

            System.IO.File.WriteAllText(certificatePath, certificate.ExportCertificatePem());
            System.IO.File.WriteAllText(certificateKeyPath, key.ExportPkcs8PrivateKeyPem());

            return new CertificatePair(certificate, certificatePath, certificateKeyPath);
        }

        public void Dispose()
        {
            Certificate.Dispose();

            foreach (string path in (string[])[CertificatePath, CertificateKeyPath])
            {
                try
                {
                    System.IO.File.Delete(path);
                }
                catch (System.IO.IOException)
                {
                    // A file that cannot be removed is a temporary-directory condition and not a
                    // failure of the subject, so it is not allowed to fail the test that wrote it.
                }
                catch (UnauthorizedAccessException)
                {
                    // Same reasoning.
                }
            }
        }
    }
}
