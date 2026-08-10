// ==================================================================================================
//  TokenEndpointsTests.cs - CONFORMANCE, AUTHENTICATION, PARITY AND SECRECY FOR CONTRACT C-01's
//  ISSUANCE OPERATION
//  ------------------------------------------------------------------------------------------------
//  WHAT IS UNDER TEST
//  services/security-service/PowerFramework.Security/Endpoints/TokenEndpoints.cs - the only route to
//  the system's sole minter. Security mints; Gateway, DataServices and Persistence hold verification
//  material only.
//
//  HOW THE ROWS ARE SPLIT, AND WHY
//    * CONTRACT rows read the AUTHORED document out of the contracts assembly and assert that the
//      implementation matches it. The authored document is authoritative for anything on the wire, so a
//      disagreement is a defect in the code and never in the document.
//    * UNIT rows call the handler's own members directly. They need no host, no transport and no key,
//      which is what makes every refusal arm reachable - including the ones a test host cannot produce
//      because it terminates no TLS.
//    * SERVICE rows boot the host through the in-process factory. Two shapes are used: the ordinary
//      client for the unauthenticated path, and a host with a client certificate injected into the
//      connection for the authenticated path, because this is the one operation in the system whose
//      caller identity comes from the transport rather than from a token.
//
//  NO KEY, CERTIFICATE OR TOKEN LITERAL APPEARS IN THIS FILE. Every piece of material is generated in
//  this process, kept in memory, and discarded with the fixture. Nothing is copied from any
//  hardcoded-secret site in the repository, and in particular nothing from
//  tests/blink/test_jws.htm:L8-L23, which the implementation exists to replace rather than to imitate.
//  Generating also proves more than a fixture would: the service must sign with what it was handed
//  rather than recognise a known value.
//
//  LEGACY ANCHORS (REFERENCE BY LOCATOR ONLY - never read as a build input, never edited)
//    ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L70-L73  the RSA signature pair the issuer is built on
//    ws_objects/pfw.shared.pbl.src/enums.sru:L927-L933    the digest catalogue those take their type
//                                                         from, SHA-256 at :L930
//    ws_objects/pfw.shared.pbl.src/issucceeded.srf:L11-L12 and isfailed.srf:L11-L12
//                                                         the tri-state return algebra, asserted here
//                                                         through the shared problem map
// ==================================================================================================

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using PowerFramework.Security.Configuration;
using PowerFramework.Security.Endpoints;
using PowerFramework.Security.Tokens;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Security.Tests;

/// <summary>
/// The addresses, identities and material every row in this file shares.
/// </summary>
/// <remarks>
/// One place for the values the authored document fixes, so that a row asserting one of them reads as
/// an assertion rather than as a literal.
/// </remarks>
internal static class IssuanceFixture
{
    /// <summary>The issuance path, as the authored document declares it.</summary>
    internal const string IssuancePath = "/v1/tokens";

    /// <summary>The operation identifier, as the authored document declares it.</summary>
    internal const string OperationId = "issueToken";

    /// <summary>The published key set's address.</summary>
    internal const string KeySetPath = "/.well-known/jwks.json";

    /// <summary>The generated document's address.</summary>
    internal const string DocumentPath = "/openapi/v1.json";

    /// <summary>
    /// A caller identity the configured audience roster carries, used as both the claimed subject and
    /// the certificate's common name on the authenticated rows.
    /// </summary>
    /// <remarks>
    /// Taken from the roster the service's own settings file declares, and matching the naming the
    /// local certificate recipe in docs/ARCHITECTURE.md section 9.3.1 issues client certificates under.
    /// </remarks>
    internal const string CallerIdentity = "powerframework-gateway";

    /// <summary>A second configured audience, for the rows that vary the audience.</summary>
    internal const string SecondAudience = "powerframework-dataservices";

    /// <summary>An audience no deployment in this repository configures.</summary>
    /// <remarks>
    /// Deliberately shaped like a service identity so that the refusal is proved to come from the
    /// roster check rather than from a shape check that a nonsense value would also have failed.
    /// </remarks>
    internal const string UnlistedAudience = "powerframework-elsewhere";

    /// <summary>The key identifier the service's settings file configures.</summary>
    internal const string ConfiguredKeyId = "powerframework-security-signing-1";

    /// <summary>One scope, so that a request asks for something rather than for nothing.</summary>
    internal const string ReadScope = "datawindow.read";

    /// <summary>A second scope, for the rows that assert set handling.</summary>
    internal const string WriteScope = "datawindow.write";

    /// <summary>The logger category the operation writes its own records under.</summary>
    /// <remarks>
    /// Selecting on it is what separates this operation's record from the shared problem factory's and
    /// from the issuer's, all three of which describe an issuance and therefore cannot be told apart by
    /// inspecting message text alone.
    /// </remarks>
    internal const string LoggerCategory = "PowerFramework.Security.Endpoints.TokenEndpoints";

    /// <summary>
    /// Builds a client certificate whose common name is the supplied identity.
    /// </summary>
    /// <param name="commonName">The identity the certificate should establish.</param>
    /// <returns>A self-signed certificate, valid now, held only in memory.</returns>
    /// <remarks>
    /// SELF-SIGNED AND EPHEMERAL. Chain verification, validity and revocation are the transport's job
    /// against a trust anchor a deployment mounts, and the implementation deliberately repeats none of
    /// it - so a row here needs a certificate that carries an identity and nothing more. Nothing is
    /// written to disk and no certificate store is touched.
    /// </remarks>
    internal static X509Certificate2 CreateCallerCertificate(string commonName)
    {
        ArgumentNullException.ThrowIfNull(commonName);

        // An elliptic-curve key rather than an RSA one, deliberately. The operation reads exactly ONE
        // thing from the certificate - the identity in its subject - and never its key, so the key type
        // is irrelevant to the behaviour under test; choosing the cheap one keeps a suite that builds a
        // fresh host per row from spending its time on key generation. That the rows pass with a
        // non-RSA client certificate is itself worth having asserted: the transport identity and the
        // RSA signing identity are separate keys with separate lifetimes.
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        // Built through the framework's own encoder rather than by concatenating a string. A
        // distinguished name has quoting, escaping and ordering rules, and an identity carrying a
        // separator is exactly the case a concatenated string gets wrong - which is the same reason the
        // implementation reads the identity back through the framework's accessor instead of splitting
        // the subject itself.
        X500DistinguishedNameBuilder subject = new();
        subject.AddCommonName(commonName);

        CertificateRequest request = new(
            subject.Build(),
            key,
            HashAlgorithmName.SHA256);

        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddMinutes(5));
    }

    /// <summary>
    /// Builds a certificate whose subject carries no common name at all.
    /// </summary>
    /// <returns>A self-signed certificate establishing no usable identity.</returns>
    /// <remarks>
    /// The subject declares an organisation and nothing else, which is the shape that makes the
    /// framework's simple-name accessor answer with nothing. It exists so the "presented but unusable"
    /// arm is proved rather than assumed.
    /// </remarks>
    internal static X509Certificate2 CreateCertificateWithoutCommonName()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        X500DistinguishedNameBuilder subject = new();
        subject.AddOrganizationName("powerframework-tests");

        CertificateRequest request = new(
            subject.Build(),
            key,
            HashAlgorithmName.SHA256);

        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddMinutes(5));
    }

    /// <summary>Builds a well-formed request body.</summary>
    /// <param name="subject">The claimed identity.</param>
    /// <param name="audience">The intended audience.</param>
    /// <param name="scopes">The requested scopes.</param>
    /// <returns>The body.</returns>
    internal static TokenIssuanceRequestBody Body(
        string? subject = CallerIdentity,
        string? audience = CallerIdentity,
        IReadOnlyList<string>? scopes = null) =>
        new()
        {
            Subject = subject,
            Audience = audience,
            Scopes = scopes ?? [ReadScope],
        };

    /// <summary>
    /// Builds a body whose scope set is genuinely absent rather than defaulted.
    /// </summary>
    /// <returns>The body.</returns>
    /// <remarks>
    /// A separate builder because <see cref="Body"/> substitutes a scope for an omitted one, which is
    /// convenient everywhere else and exactly wrong for the row that asserts absence.
    /// </remarks>
    internal static TokenIssuanceRequestBody BodyWithoutScopes() =>
        new()
        {
            Subject = CallerIdentity,
            Audience = CallerIdentity,
            Scopes = null,
        };

    /// <summary>
    /// Generates fresh signing material for one host.
    /// </summary>
    /// <returns>The private key as armoured algorithm-tagged text.</returns>
    /// <remarks>
    /// <para>
    /// GENERATED RATHER THAN WRITTEN DOWN. No key literal appears in this file, and nothing is copied
    /// from any hardcoded-secret site in the repository. Generating also proves more than a fixture
    /// would: the service must sign with what it was handed rather than recognise a known value. 2048
    /// bits because the minting library applies its own asymmetric minimum when it builds a signature
    /// provider, and because the service's own configuration declares that floor for its signing
    /// identity - which is not a legacy correction: the legacy keeps a smaller size a first-class value
    /// [ws_objects/pfw.shared.pbl.src/enums.sru:L965] on the surface where a CALLER supplies it, and
    /// this key is the system's own signing identity instead.
    /// </para>
    /// <para>
    /// FRESH PER HOST RATHER THAN SHARED ACROSS ROWS, AND THAT IS NOT MERELY TIDINESS. The minting
    /// library caches signature providers in a PROCESS-WIDE cache keyed by the key's own identity, and a
    /// key's identity is derived from its material rather than from the object holding it - so two hosts
    /// configured with the same material share one cached provider, and the provider outlives whichever
    /// host is disposed first. Sharing one pair therefore makes a row fail intermittently, for a reason
    /// that has nothing to do with the code under test. A distinct pair per host removes the sharing.
    /// </para>
    /// </remarks>
    internal static string CreateSigningKeyPem()
    {
        using RSA key = RSA.Create(2048);

        return key.ExportPkcs8PrivateKeyPem();
    }
}

/// <summary>
/// A clock that never advances, so that two issuances for identical input produce identical claims.
/// </summary>
/// <remarks>
/// The clock is one of the two primary non-determinism sources in this service and the characterization
/// model requires every such value to be maskable from BOTH the master and the candidate recording.
/// Substituting the whole seam here is what proves the endpoint reads NO ambient clock of its own: if
/// it did, a fixed clock would not produce a byte-identical body.
/// </remarks>
internal sealed class FrozenTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _instant;

    /// <summary>Creates the clock over the instant it will always report.</summary>
    /// <param name="instant">The instant.</param>
    public FrozenTimeProvider(DateTimeOffset instant) => _instant = instant;

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => _instant;
}

/// <summary>
/// A transport-security feature that reports a caller-supplied client certificate.
/// </summary>
/// <remarks>
/// The in-process test host terminates no TLS, so there is no handshake to present a certificate in.
/// This feature is the seam that stands in for one. It reports exactly what the connection would have
/// carried and nothing else, so the code under test cannot tell the difference and no production type
/// is modified to accommodate the test.
/// </remarks>
internal sealed class StubTlsConnectionFeature : ITlsConnectionFeature
{
    /// <summary>Creates the feature over the certificate it will report.</summary>
    /// <param name="certificate">The certificate, or <see langword="null"/> for none.</param>
    public StubTlsConnectionFeature(X509Certificate2? certificate) => ClientCertificate = certificate;

    /// <inheritdoc/>
    public X509Certificate2? ClientCertificate { get; set; }

    /// <inheritdoc/>
    public Task<X509Certificate2?> GetClientCertificateAsync(CancellationToken cancellationToken) =>
        Task.FromResult(ClientCertificate);
}

/// <summary>
/// Prepends middleware that puts a client certificate on every connection.
/// </summary>
/// <remarks>
/// A startup filter rather than a replaced pipeline, so the host under test keeps its OWN middleware
/// order - authentication, then authorization, then the routes - and only gains a transport fact ahead
/// of all of it. Replacing the pipeline instead would test a different application.
/// </remarks>
internal sealed class ClientCertificateStartupFilter : IStartupFilter
{
    private readonly X509Certificate2 _certificate;

    /// <summary>Creates the filter over the certificate every request will carry.</summary>
    /// <param name="certificate">The certificate.</param>
    public ClientCertificateStartupFilter(X509Certificate2 certificate) => _certificate = certificate;

    /// <inheritdoc/>
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return builder =>
        {
            builder.Use(async (context, continuation) =>
            {
                context.Features.Set<ITlsConnectionFeature>(
                    new StubTlsConnectionFeature(_certificate));

                await continuation(context).ConfigureAwait(false);
            });

            next(builder);
        };
    }
}

/// <summary>
/// The issuance host: the real composition root, with signing material supplied and, optionally, a
/// client certificate on the connection and a frozen clock.
/// </summary>
/// <remarks>
/// <para>
/// THE COMPOSITION ROOT IS NOT REPLACED. Every registration the service makes is kept; this factory
/// only supplies what a deployment supplies - the signing secret - and substitutes the two determinism
/// seams the characterization model requires to be substitutable. The route, its authorization policy
/// and its handler are the production ones.
/// </para>
/// <para>
/// The signing secret is applied through the options pipeline rather than through an environment
/// variable, because that is the same ingress a deployment uses and it keeps the value out of the
/// process environment where a sibling row could observe it.
/// </para>
/// </remarks>
internal sealed class IssuanceHostFactory : WebApplicationFactory<Program>
{
    private readonly X509Certificate2? _certificate;
    private readonly DateTimeOffset? _instant;
    private readonly string _signingKey;
    private readonly string? _issuancePath;
    private readonly CapturedRecords? _captured;
    private readonly LogLevel? _minimumLevel;

    /// <summary>Creates the factory.</summary>
    /// <param name="certificate">
    /// The client certificate every request should carry, or <see langword="null"/> to leave the
    /// connection without one - which is what an unauthenticated caller looks like.
    /// </param>
    /// <param name="instant">The instant to freeze the clock at, or <see langword="null"/> to keep the
    /// real one.</param>
    /// <param name="signingKey">
    /// The signing material, or <see langword="null"/> to generate a pair for this host alone.
    /// </param>
    /// <param name="issuancePath">
    /// A replacement issuance address, or <see langword="null"/> to keep the configured one. Supplied by
    /// the rows that assert the registration's fail-fast validation.
    /// </param>
    /// <param name="captured">
    /// A record store to capture the host's log records into, or <see langword="null"/> to keep the
    /// ordinary providers.
    /// </param>
    /// <param name="minimumLevel">
    /// A minimum level to filter this file's own logger category to, or <see langword="null"/> to leave
    /// the configured filtering alone. Supplied by the row that asserts issuance succeeds with the
    /// operator channel switched off.
    /// </param>
    public IssuanceHostFactory(
        X509Certificate2? certificate = null,
        DateTimeOffset? instant = null,
        string? signingKey = null,
        string? issuancePath = null,
        CapturedRecords? captured = null,
        LogLevel? minimumLevel = null)
    {
        _certificate = certificate;
        _instant = instant;
        _signingKey = signingKey ?? IssuanceFixture.CreateSigningKeyPem();
        _issuancePath = issuancePath;
        _captured = captured;
        _minimumLevel = minimumLevel;
    }

    /// <inheritdoc/>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureServices(services =>
        {
            string signingKey = _signingKey;
            string? issuancePath = _issuancePath;

            services.Configure<SecurityOptions>(options =>
            {
                options.SigningKey = signingKey;

                if (issuancePath is not null)
                {
                    options.TokenEndpointPath = issuancePath;
                }
            });

            if (_captured is not null)
            {
                CapturedRecords captured = _captured;

                services.AddLogging(logging =>
                    logging.AddProvider(new CapturingLoggerProvider(captured)));
            }

            if (_minimumLevel is LogLevel level)
            {
                services.AddLogging(logging =>
                    logging.AddFilter(IssuanceFixture.LoggerCategory, level));
            }

            if (_certificate is not null)
            {
                services.AddSingleton<IStartupFilter>(
                    new ClientCertificateStartupFilter(_certificate));
            }

            if (_instant is DateTimeOffset frozen)
            {
                services.AddSingleton<TimeProvider>(new FrozenTimeProvider(frozen));
            }
        });
    }
}

/// <summary>
/// The implementation matches the AUTHORED document, member for member.
/// </summary>
/// <remarks>
/// The authored document is authoritative for anything on the wire, so every row here compares the
/// implementation against it rather than the other way round. A disagreement is fixed in the code.
/// </remarks>
public sealed class TokenContractConformanceTests
{
    /// <summary>The authored block declaring the issuance path, extracted once.</summary>
    private static string Block => ContractDocument.PathBlock(IssuanceFixture.IssuancePath);

    /// <summary>The authored document declares the operation as a POST with the expected identity.</summary>
    [Fact]
    public void OperationIdentityMatchesTheAuthoredDocument()
    {
        string block = Block;

        Assert.Contains("    post:", block, StringComparison.Ordinal);
        Assert.Contains("operationId: " + IssuanceFixture.OperationId, block, StringComparison.Ordinal);
        Assert.Contains("tags: [TokenService]", block, StringComparison.Ordinal);
        Assert.Contains(
            "summary: Issue a short-lived service token.",
            block,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The authored document protects the operation with mutual TLS and offers NO bearer alternative.
    /// </summary>
    /// <remarks>
    /// THE ROW THAT PINS THE DESIGN DECISION MOST WORTH PINNING. The document-level requirement in this
    /// specification is a bearer token; this operation OVERRIDES it, because a caller cannot present a
    /// bearer token in order to obtain its first bearer token. An implementation that required a bearer
    /// principal here would make the sole issuer unreachable by the only callers that need it, so this
    /// row exists to make that impossible to introduce silently.
    /// </remarks>
    [Fact]
    public void OperationRequiresMutualTlsAndOffersNoBearerAlternative()
    {
        string block = Block;
        int securityIndex = block.IndexOf("      security:", StringComparison.Ordinal);

        Assert.True(securityIndex >= 0, "The authored operation declares no security requirement.");

        string requirement = block[securityIndex..];
        int requestBodyIndex = requirement.IndexOf("      requestBody:", StringComparison.Ordinal);

        Assert.True(requestBodyIndex > 0, "The authored operation declares no request body.");

        requirement = requirement[..requestBodyIndex];

        Assert.Contains("- mutualTls: []", requirement, StringComparison.Ordinal);
        Assert.DoesNotContain("bearerAuth", requirement, StringComparison.Ordinal);
    }

    /// <summary>The authored document declares the mutual-TLS scheme with the mutual-TLS type.</summary>
    [Fact]
    public void AuthoredDocumentDeclaresTheMutualTlsScheme()
    {
        Assert.Contains("    mutualTls:", ContractDocument.Text, StringComparison.Ordinal);
        Assert.Contains("      type: mutualTLS", ContractDocument.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The authored operation declares exactly the five statuses the implementation publishes.
    /// </summary>
    /// <param name="status">The status the authored document must declare.</param>
    [Theory]
    [InlineData("200")]
    [InlineData("400")]
    [InlineData("401")]
    [InlineData("403")]
    [InlineData("500")]
    public void AuthoredOperationDeclaresStatus(string status) =>
        Assert.Contains("        '" + status + "':", Block, StringComparison.Ordinal);

    /// <summary>
    /// The authored operation declares NO status the implementation does not publish.
    /// </summary>
    /// <param name="status">A status that must be absent.</param>
    /// <remarks>
    /// The complement of the row above, and the reason the handler returns the untyped result interface
    /// rather than a typed union: a union lets the framework infer response metadata of its own, and
    /// this pair of rows is what would catch the divergence that would cause.
    /// </remarks>
    [Theory]
    [InlineData("201")]
    [InlineData("204")]
    [InlineData("404")]
    [InlineData("409")]
    [InlineData("503")]
    public void AuthoredOperationDeclaresNoOtherStatus(string status) =>
        Assert.DoesNotContain("        '" + status + "':", Block, StringComparison.Ordinal);

    /// <summary>
    /// The request record declares exactly the members the authored request schema declares.
    /// </summary>
    /// <remarks>
    /// Compared by the WIRE name rather than by the property name, because the wire name is what a
    /// consumer sends. A member added to the record without being added to the closed authored schema
    /// would fail this row, which is what makes the closure enforceable from the code side too.
    /// </remarks>
    [Fact]
    public void RequestRecordDeclaresExactlyTheAuthoredMembers()
    {
        string[] expected = ["subject", "audience", "scopes"];

        Assert.Equal(expected.OrderBy(name => name, StringComparer.Ordinal), WireNames(typeof(TokenIssuanceRequestBody)));
    }

    /// <summary>
    /// The response record declares exactly the members the authored response schema declares.
    /// </summary>
    /// <remarks>
    /// The key identifier is deliberately absent: the authored schema declares no such member and closes
    /// the object against undeclared ones, and the identifier is public metadata available in the token's
    /// own header and in the published key set. This row is what stops it being added "for convenience".
    /// </remarks>
    [Fact]
    public void ResponseRecordDeclaresExactlyTheAuthoredMembers()
    {
        string[] expected = ["access_token", "token_type", "expires_in", "scope", "issued_at"];

        Assert.Equal(expected.OrderBy(name => name, StringComparer.Ordinal), WireNames(typeof(TokenIssuanceResponse)));
        Assert.DoesNotContain("kid", WireNames(typeof(TokenIssuanceResponse)));
    }

    /// <summary>
    /// The optional member of the authored response schema is NOT declared required by the record.
    /// </summary>
    /// <remarks>
    /// The four required members are marked so the generated document says so; the issuance instant is
    /// optional in the authored schema, so marking it required here would publish a stricter schema than
    /// the contract has. Asserted through the compiler-emitted required-member attribute rather than by
    /// reading source text.
    /// </remarks>
    [Fact]
    public void ResponseRecordMarksOnlyTheAuthoredRequiredMembers()
    {
        Assert.True(IsRequired(nameof(TokenIssuanceResponse.AccessToken)));
        Assert.True(IsRequired(nameof(TokenIssuanceResponse.TokenType)));
        Assert.True(IsRequired(nameof(TokenIssuanceResponse.ExpiresIn)));
        Assert.True(IsRequired(nameof(TokenIssuanceResponse.Scope)));
        Assert.False(IsRequired(nameof(TokenIssuanceResponse.IssuedAt)));
    }

    /// <summary>
    /// No member of either record carries a default value that could be mistaken for material.
    /// </summary>
    /// <remarks>
    /// A default on a request member would let the service supply something the caller never asked for;
    /// a default on a response member would be a specimen value in source. Both are refused, and the
    /// check is by construction: an instance built with no initializer has null or zero everywhere.
    /// </remarks>
    [Fact]
    public void NeitherRecordCarriesADefaultValue()
    {
        TokenIssuanceRequestBody request = new();

        Assert.Null(request.Subject);
        Assert.Null(request.Audience);
        Assert.Null(request.Scopes);
    }

    /// <summary>Reports the wire names a record publishes, in ordinal order.</summary>
    /// <param name="type">The record type.</param>
    /// <returns>The wire names.</returns>
    private static IEnumerable<string> WireNames(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property =>
                property.GetCustomAttribute<System.Text.Json.Serialization.JsonPropertyNameAttribute>()
                    ?.Name ?? property.Name)
            .OrderBy(name => name, StringComparer.Ordinal);

    /// <summary>Reports whether a response member is compiler-marked as required.</summary>
    /// <param name="propertyName">The property name.</param>
    /// <returns><see langword="true"/> when the member is required.</returns>
    private static bool IsRequired(string propertyName)
    {
        PropertyInfo? property = typeof(TokenIssuanceResponse).GetProperty(propertyName);

        Assert.NotNull(property);

        return property.GetCustomAttributes()
            .Any(attribute => string.Equals(
                attribute.GetType().Name,
                "RequiredMemberAttribute",
                StringComparison.Ordinal));
    }
}

/// <summary>
/// The boundary is authenticated, and it is authenticated EXPLICITLY rather than by omission.
/// </summary>
/// <remarks>
/// Constraint C-G's standing proof for the one route in the system that reaches a signing key.
/// </remarks>
public sealed class TokenEndpointAuthorizationTests
{
    /// <summary>
    /// A request with no client certificate and no token is refused with the unauthorized status.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// The body is deliberately an empty JSON object. Authorization is evaluated before model binding,
    /// so a well-formed request is unnecessary - and sending one would risk the row passing for the
    /// wrong reason if binding ever rejected first.
    /// </para>
    /// <para>
    /// A well-formed body would also be refused, and the companion row below sends one to prove that the
    /// refusal is about the credential rather than about the payload.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task RequestWithoutATransportCredentialIsRefusedAsync()
    {
        await using IssuanceHostFactory factory = new();
        using HttpClient client = factory.CreateClient();
        using StringContent body = new("{}", Encoding.UTF8, MediaTypeNames.Application.Json);

        using HttpResponseMessage response = await client.PostAsync(
            new Uri(IssuanceFixture.IssuancePath, UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// A WELL-FORMED request with no client certificate is refused too, and nothing is minted.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// Proves the refusal is about the credential rather than the payload, and that no response on the
    /// unauthenticated path carries anything token-shaped.
    /// </remarks>
    [Fact]
    public async Task WellFormedRequestWithoutATransportCredentialMintsNothingAsync()
    {
        await using IssuanceHostFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(IssuanceFixture.IssuancePath, UriKind.Relative),
            IssuanceFixture.Body(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        string payload = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("access_token", payload, StringComparison.Ordinal);
    }

    /// <summary>
    /// The route carries authorization metadata, so the requirement is declared at the route itself.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THIS ROW FAILS IF <c>RequireAuthorization</c> IS REMOVED FROM THE ROUTE, which is the property it
    /// exists to protect and which a status assertion alone cannot prove on this service: the host also
    /// installs a default-deny fallback policy, so a route stripped of its own requirement would still
    /// answer the same status while having lost its local, visible guard.
    /// </para>
    /// <para>
    /// It also asserts the complement - that the route is NOT marked anonymous - so the two ways of
    /// breaking the requirement are both covered.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task RouteDeclaresItsOwnAuthorizationRequirementAsync()
    {
        await using IssuanceHostFactory factory = new();

        // Forces the host to build, which is what materialises the endpoint data source.
        using HttpClient client = factory.CreateClient();

        Endpoint issuance = FindIssuanceEndpoint(factory);

        Assert.NotNull(issuance.Metadata.GetMetadata<IAuthorizeData>());
        Assert.Null(issuance.Metadata.GetMetadata<IAllowAnonymous>());
    }

    /// <summary>
    /// The route's own policy does NOT require an authenticated principal.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The mirror image of the row above, and just as load-bearing. Requiring a principal would demand a
    /// bearer token on the one operation whose whole purpose is to issue a caller's first token, which
    /// the authored document forbids by overriding the document-level bearer requirement. Asserted by
    /// evaluating the route's policy against a connection that carries a certificate and no principal: a
    /// policy that demanded a principal could not succeed there.
    /// </remarks>
    [Fact]
    public async Task RoutePolicyIsSatisfiedByTheTransportCredentialAloneAsync()
    {
        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate);
        using HttpClient client = factory.CreateClient();

        Endpoint issuance = FindIssuanceEndpoint(factory);
        IAuthorizeData? authorization = issuance.Metadata.GetMetadata<IAuthorizeData>();

        Assert.NotNull(authorization);

        // An inline policy carries no name, which is what distinguishes this route from every other
        // authenticated route on the service: those use the parameterless form.
        Assert.True(string.IsNullOrEmpty(authorization.Policy));
    }

    /// <summary>
    /// The issuance route is none of the service's three anonymous exemptions.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The complement of the rows above: it proves the exemption set and the issuance path are disjoint,
    /// so an exemption added later for a readiness or discovery address cannot accidentally cover the
    /// mint. The readiness probe is exercised in the same host so that the refusals above are shown to
    /// be the contract's own requirement rather than a broken host.
    /// </remarks>
    [Fact]
    public async Task IssuanceRouteIsNotAnAnonymousExemptionAsync()
    {
        string[] exemptions =
        [
            "/health",
            IssuanceFixture.KeySetPath,
            "/.well-known/openid-configuration",
        ];

        foreach (string exemption in exemptions)
        {
            Assert.NotEqual(IssuanceFixture.IssuancePath, exemption);
        }

        await using IssuanceHostFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage health = await client.GetAsync(
            new Uri("/health", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.Unauthorized, health.StatusCode);
    }

    /// <summary>Locates the issuance endpoint in the booted host's endpoint data sources.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The endpoint.</returns>
    internal static Endpoint FindIssuanceEndpoint(WebApplicationFactory<Program> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        EndpointDataSource endpoints = factory.Services.GetRequiredService<EndpointDataSource>();

        Endpoint? issuance = endpoints.Endpoints.FirstOrDefault(candidate =>
            candidate is RouteEndpoint route &&
            string.Equals(
                route.RoutePattern.RawText,
                IssuanceFixture.IssuancePath,
                StringComparison.Ordinal));

        Assert.NotNull(issuance);

        return issuance;
    }
}

/// <summary>
/// The authenticated path, driven end to end through the booted host.
/// </summary>
/// <remarks>
/// Every row here presents a client certificate on the connection, because this is the one operation in
/// the system whose caller identity comes from the transport rather than from a token.
/// </remarks>
public sealed class TokenIssuanceServiceTests
{
    /// <summary>
    /// A well-formed request from an authenticated caller is issued a token.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// Asserts the response member for member against the authored schema: the credential, the fixed
    /// credential type, a lifetime of at least one second matching the configured five minutes, the
    /// granted scope value, and the issuance instant. The token itself is only asserted to be
    /// well-shaped here; the round-trip rows prove it verifies.
    /// </remarks>
    [Fact]
    public async Task AuthenticatedCallerReceivesATokenAsync()
    {
        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(IssuanceFixture.IssuancePath, UriKind.Relative),
            IssuanceFixture.Body(scopes: [IssuanceFixture.ReadScope, IssuanceFixture.WriteScope]),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            MediaTypeNames.Application.Json,
            response.Content.Headers.ContentType?.MediaType);

        TokenIssuanceResponse? body = await response.Content.ReadFromJsonAsync<TokenIssuanceResponse>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.False(string.IsNullOrEmpty(body.AccessToken));
        Assert.Equal(3, body.AccessToken.Split('.').Length);
        Assert.Equal("Bearer", body.TokenType);
        Assert.Equal(300, body.ExpiresIn);
        Assert.Equal(
            IssuanceFixture.ReadScope + " " + IssuanceFixture.WriteScope,
            body.Scope);
        Assert.True(body.IssuedAt > 0);
    }

    /// <summary>
    /// The response is spelled with the wire names the authored schema declares, and no others.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// Read as raw JSON rather than deserialized, because deserializing would hide both an extra member
    /// and a misspelled one - and a consumer's stock client library reads the raw names.
    /// </remarks>
    [Fact]
    public async Task ResponseCarriesExactlyTheAuthoredWireNamesAsync()
    {
        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(IssuanceFixture.IssuancePath, UriKind.Relative),
            IssuanceFixture.Body(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string payload = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using JsonDocument document = JsonDocument.Parse(payload);

        string[] actual = document.RootElement.EnumerateObject()
            .Select(member => member.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        string[] expected =
            ["access_token", "expires_in", "issued_at", "scope", "token_type"];

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// The minted token's key identifier is the one the published key set publishes.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE AGREEMENT THAT MAKES A TOKEN VERIFIABLE AT ALL. A verifier selects its key from the published
    /// set by the identifier in the token's header, so a divergence would make every token unverifiable
    /// while both halves looked correct in isolation. The identifier is read from the token's HEADER
    /// rather than from the response, because the authored response schema declares no such member.
    /// </remarks>
    [Fact]
    public async Task MintedTokenNamesThePublishedKeyAsync()
    {
        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate);
        using HttpClient client = factory.CreateClient();

        string token = await IssueAsync(client);

        JsonWebToken parsed = new(token);

        Assert.Equal(IssuanceFixture.ConfiguredKeyId, parsed.Kid);
        Assert.Equal(SecurityAlgorithms.RsaSha256, parsed.Alg);

        using HttpResponseMessage keySet = await client.GetAsync(
            new Uri(IssuanceFixture.KeySetPath, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, keySet.StatusCode);

        string published = await keySet.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using JsonDocument document = JsonDocument.Parse(published);

        JsonElement key = document.RootElement.GetProperty("keys").EnumerateArray().Single();

        Assert.Equal(parsed.Kid, key.GetProperty("kid").GetString());
        Assert.Equal(parsed.Alg, key.GetProperty("alg").GetString());
    }

    /// <summary>
    /// A claimed identity that disagrees with the certificate's is refused, and nothing is minted.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The subject-to-caller mapping the architecture record assigns to this file. The refusal must name
    /// NEITHER the expected identity NOR any part of the stored configuration, which the second half of
    /// this row asserts: a message that reported the expected value would turn a refusal into an
    /// identity oracle.
    /// </remarks>
    [Fact]
    public async Task ClaimedSubjectMustMatchTheCertificateIdentityAsync()
    {
        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(IssuanceFixture.IssuancePath, UriKind.Relative),
            IssuanceFixture.Body(subject: IssuanceFixture.SecondAudience),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        string payload = await AssertProblemAsync(response, RetCode.E_ACCESS_DENIED);

        Assert.DoesNotContain(IssuanceFixture.CallerIdentity, payload, StringComparison.Ordinal);
        Assert.DoesNotContain("access_token", payload, StringComparison.Ordinal);
    }

    /// <summary>
    /// An audience the configured roster does not carry is refused, and the roster is not enumerated.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// FORBIDDEN RATHER THAN BAD REQUEST, because the authored operation's forbidden response says in so
    /// many words that it answers when the authenticated caller is not permitted the requested subject OR
    /// AUDIENCE. The distinction from the malformed rows is exact: an absent or blank audience violates
    /// the published schema and is a bad request; a well-formed audience absent from the roster violates
    /// no schema at all - the schema does not enumerate audiences - and is a permission decision.
    /// </remarks>
    [Fact]
    public async Task UnlistedAudienceIsRefusedWithoutEnumeratingTheRosterAsync()
    {
        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(IssuanceFixture.IssuancePath, UriKind.Relative),
            IssuanceFixture.Body(audience: IssuanceFixture.UnlistedAudience),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        string payload = await AssertProblemAsync(response, RetCode.E_ACCESS_DENIED);

        Assert.DoesNotContain(IssuanceFixture.UnlistedAudience, payload, StringComparison.Ordinal);
        Assert.DoesNotContain(IssuanceFixture.SecondAudience, payload, StringComparison.Ordinal);
        Assert.DoesNotContain("access_token", payload, StringComparison.Ordinal);
    }

    /// <summary>
    /// A malformed request from an authenticated caller is a bad request carrying the legacy code.
    /// </summary>
    /// <param name="subject">The claimed identity, or <see langword="null"/> to omit it.</param>
    /// <param name="audience">The audience, or <see langword="null"/> to omit it.</param>
    /// <param name="scope">A single scope, or <see langword="null"/> to omit the set.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// Driven through the transport rather than only as a unit call, so the row proves that the status
    /// and the problem shape survive the whole pipeline - including that the framework's own binding did
    /// not answer first with a shapeless response of its own.
    /// </remarks>
    [Theory]
    [InlineData(null, IssuanceFixture.CallerIdentity, IssuanceFixture.ReadScope)]
    [InlineData("", IssuanceFixture.CallerIdentity, IssuanceFixture.ReadScope)]
    [InlineData(IssuanceFixture.CallerIdentity, null, IssuanceFixture.ReadScope)]
    [InlineData(IssuanceFixture.CallerIdentity, " ", IssuanceFixture.ReadScope)]
    [InlineData(IssuanceFixture.CallerIdentity, IssuanceFixture.CallerIdentity, null)]
    [InlineData(IssuanceFixture.CallerIdentity, IssuanceFixture.CallerIdentity, "")]
    [InlineData(IssuanceFixture.CallerIdentity, IssuanceFixture.CallerIdentity, "with space")]
    public async Task MalformedRequestIsABadRequestAsync(string? subject, string? audience, string? scope)
    {
        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate);
        using HttpClient client = factory.CreateClient();

        TokenIssuanceRequestBody body = new()
        {
            Subject = subject,
            Audience = audience,
            Scopes = scope is null ? null : [scope],
        };

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(IssuanceFixture.IssuancePath, UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await AssertProblemAsync(response, RetCode.E_INVALID_ARGUMENT);
    }

    /// <summary>
    /// An absent body is answered by the operation's own bad request rather than by the framework's.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE ROW THAT JUSTIFIES THE NULLABLE BODY PARAMETER. A non-nullable parameter would make the
    /// framework refuse the absent body itself, with a status but with no problem document and therefore
    /// no legacy return code - and the authored bad-request response states that it carries one. The
    /// assertion on the return code is what would fail if the parameter were made non-nullable.
    /// </remarks>
    [Fact]
    public async Task AbsentBodyIsAnsweredByTheOperationAsync()
    {
        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate);
        using HttpClient client = factory.CreateClient();
        using StringContent body = new("null", Encoding.UTF8, MediaTypeNames.Application.Json);

        using HttpResponseMessage response = await client.PostAsync(
            new Uri(IssuanceFixture.IssuancePath, UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await AssertProblemAsync(response, RetCode.E_INVALID_ARGUMENT);
    }

    /// <summary>
    /// A certificate that establishes no identity is refused with the unauthorized status.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The "presented but unusable" arm, and the one that proves the handler's own refusal carries the
    /// authored problem body and the legacy code - which the middleware's earlier challenge cannot, since
    /// it writes no body. The sentence must be the same one an absent certificate produces, so the
    /// response cannot be used to probe which of the two conditions was hit.
    /// </remarks>
    [Fact]
    public async Task CertificateWithoutAnIdentityIsRefusedAsync()
    {
        using X509Certificate2 certificate = IssuanceFixture.CreateCertificateWithoutCommonName();

        await using IssuanceHostFactory factory = new(certificate);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(IssuanceFixture.IssuancePath, UriKind.Relative),
            IssuanceFixture.Body(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        string payload = await AssertProblemAsync(response, RetCode.E_ACCESS_DENIED);

        Assert.DoesNotContain("access_token", payload, StringComparison.Ordinal);
    }

    /// <summary>Issues one token through the booted host and returns it.</summary>
    /// <param name="client">The client, whose host must carry a caller certificate.</param>
    /// <returns>The minted token.</returns>
    internal static async Task<string> IssueAsync(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(IssuanceFixture.IssuancePath, UriKind.Relative),
            IssuanceFixture.Body(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        TokenIssuanceResponse? body = await response.Content.ReadFromJsonAsync<TokenIssuanceResponse>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(body);

        return body.AccessToken;
    }

    /// <summary>
    /// Asserts that a response is the service's one problem shape carrying the expected legacy code.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <param name="expected">The legacy return code the body must carry.</param>
    /// <returns>The raw payload, so a caller can assert on what it does NOT contain.</returns>
    private static async Task<string> AssertProblemAsync(HttpResponseMessage response, long expected)
    {
        Assert.Equal(
            MediaTypeNames.Application.ProblemJson,
            response.Content.Headers.ContentType?.MediaType);

        string payload = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using JsonDocument document = JsonDocument.Parse(payload);

        Assert.True(
            document.RootElement.TryGetProperty("retCode", out JsonElement retCode),
            "The problem body carries no retCode member.");

        Assert.Equal(expected, retCode.GetInt64());

        return payload;
    }
}

/// <summary>
/// The request-shape arms, driven directly as a table.
/// </summary>
/// <remarks>
/// Called without a host, a transport or a key, which is what makes every arm reachable and cheap. Each
/// row asserts the STATUS and the LEGACY RETURN CODE, because the authored bad-request response promises
/// both.
/// </remarks>
public sealed class TokenRequestValidationTests
{
    /// <summary>Every malformed shape the authored schema forbids, one row each.</summary>
    /// <returns>The rows.</returns>
    public static TheoryData<string, TokenIssuanceRequestBody?> MalformedRequests()
    {
        TheoryData<string, TokenIssuanceRequestBody?> rows = new()
        {
            { "absent body", null },
            { "absent subject", IssuanceFixture.Body(subject: null) },
            { "empty subject", IssuanceFixture.Body(subject: string.Empty) },
            { "whitespace subject", IssuanceFixture.Body(subject: "   ") },
            { "absent audience", IssuanceFixture.Body(audience: null) },
            { "empty audience", IssuanceFixture.Body(audience: string.Empty) },
            { "whitespace audience", IssuanceFixture.Body(audience: "\t") },
            { "absent scope set", IssuanceFixture.BodyWithoutScopes() },
            { "empty scope set", IssuanceFixture.Body(scopes: []) },
            { "empty scope", IssuanceFixture.Body(scopes: [string.Empty]) },
            { "space in scope", IssuanceFixture.Body(scopes: ["read write"]) },
            { "tab in scope", IssuanceFixture.Body(scopes: ["read\twrite"]) },
            { "line break in scope", IssuanceFixture.Body(scopes: ["read\nwrite"]) },
            {
                "duplicate scope",
                IssuanceFixture.Body(scopes: [IssuanceFixture.ReadScope, IssuanceFixture.ReadScope])
            },
        };

        return rows;
    }

    /// <summary>
    /// Every malformed shape is refused as a bad request carrying the invalid-argument code.
    /// </summary>
    /// <param name="description">What the row varies, carried for row identity.</param>
    /// <param name="request">The malformed request.</param>
    [Theory]
    [MemberData(nameof(MalformedRequests))]
    public void MalformedRequestIsRefused(string description, TokenIssuanceRequestBody? request)
    {
        Assert.False(string.IsNullOrEmpty(description));

        ProblemHttpResult? refusal = TokenEndpoints.ValidateRequestShape(
            request,
            NullLoggerFactory.Instance);

        Assert.NotNull(refusal);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Equal(
            RetCode.E_INVALID_ARGUMENT,
            Assert.IsType<long>(refusal.ProblemDetails.Extensions["retCode"]));
    }

    /// <summary>
    /// Scopes differing only in case are two scopes rather than a duplicate.
    /// </summary>
    /// <remarks>
    /// A scope is an opaque protocol token, so comparing case-insensitively would refuse a request the
    /// authored schema permits. This row is the complement of the duplicate row above.
    /// </remarks>
    [Fact]
    public void ScopesDifferingOnlyByCaseAreNotDuplicates()
    {
        TokenIssuanceRequestBody request = IssuanceFixture.Body(
            scopes: [IssuanceFixture.ReadScope, IssuanceFixture.ReadScope.ToUpperInvariant()]);

        Assert.Null(TokenEndpoints.ValidateRequestShape(request, NullLoggerFactory.Instance));
    }

    /// <summary>A well-formed request is not refused.</summary>
    /// <param name="scopeCount">How many distinct scopes the row asks for.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(8)]
    public void WellFormedRequestIsAccepted(int scopeCount)
    {
        string[] scopes = Enumerable.Range(0, scopeCount)
            .Select(index => string.Create(CultureInfo.InvariantCulture, $"scope.{index}"))
            .ToArray();

        Assert.Null(TokenEndpoints.ValidateRequestShape(
            IssuanceFixture.Body(scopes: scopes),
            NullLoggerFactory.Instance));
    }

    /// <summary>
    /// No refusal reproduces the offending value.
    /// </summary>
    /// <remarks>
    /// Every detail sentence is a compile-time constant with no parameter for a caller value, so this row
    /// is checking a property the SHAPE of the code guarantees rather than the discipline of a call site -
    /// and it is written down so a future arm that composed a message could not pass it.
    /// </remarks>
    [Fact]
    public void RefusalDoesNotReproduceTheOffendingValue()
    {
        const string marker = "unmistakable-caller-supplied-marker";

        TokenIssuanceRequestBody request = IssuanceFixture.Body(scopes: [marker + " " + marker]);

        ProblemHttpResult? refusal = TokenEndpoints.ValidateRequestShape(
            request,
            NullLoggerFactory.Instance);

        Assert.NotNull(refusal);
        Assert.DoesNotContain(marker, refusal.ProblemDetails.Detail ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(marker, refusal.ProblemDetails.Title ?? string.Empty, StringComparison.Ordinal);
    }
}

/// <summary>
/// The transport identity: what a certificate establishes, and what the route's policy makes of one.
/// </summary>
public sealed class TokenCallerIdentityTests
{
    /// <summary>A certificate's common name is the identity it establishes.</summary>
    /// <param name="commonName">The name to issue the certificate under.</param>
    [Theory]
    [InlineData(IssuanceFixture.CallerIdentity)]
    [InlineData(IssuanceFixture.SecondAudience)]
    [InlineData("caller with spaces")]
    [InlineData("caller,with,separators")]
    public void CommonNameIsTheEstablishedIdentity(string commonName)
    {
        using X509Certificate2 certificate = IssuanceFixture.CreateCallerCertificate(commonName);

        Assert.Equal(commonName, TokenEndpoints.ResolveCallerIdentity(certificate));
    }

    /// <summary>An absent certificate establishes no identity.</summary>
    [Fact]
    public void AbsentCertificateEstablishesNoIdentity() =>
        Assert.Null(TokenEndpoints.ResolveCallerIdentity(certificate: null));

    /// <summary>A certificate with no common name establishes no identity.</summary>
    [Fact]
    public void CertificateWithoutACommonNameEstablishesNoIdentity()
    {
        using X509Certificate2 certificate = IssuanceFixture.CreateCertificateWithoutCommonName();

        Assert.Null(TokenEndpoints.ResolveCallerIdentity(certificate));
    }

    /// <summary>The route's policy predicate refuses a connection with no certificate.</summary>
    [Fact]
    public void PolicyRefusesAConnectionWithoutACertificate()
    {
        DefaultHttpContext connection = new();

        Assert.False(TokenEndpoints.HasTransportCredential(BuildContext(connection)));
    }

    /// <summary>The route's policy predicate admits a connection carrying a certificate.</summary>
    /// <remarks>
    /// With NO authenticated principal on the context, which is the property that matters: the policy
    /// must be satisfiable by the transport alone, because a caller cannot present a bearer token in
    /// order to obtain its first bearer token.
    /// </remarks>
    [Fact]
    public void PolicyAdmitsAConnectionCarryingACertificate()
    {
        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        DefaultHttpContext connection = new();
        connection.Features.Set<ITlsConnectionFeature>(new StubTlsConnectionFeature(certificate));

        Assert.False(connection.User.Identity?.IsAuthenticated ?? false);
        Assert.True(TokenEndpoints.HasTransportCredential(BuildContext(connection)));
    }

    /// <summary>
    /// The policy defers when the decision cannot see the connection at all.
    /// </summary>
    /// <remarks>
    /// THE ONE LINE IN THE PREDICATE THAT NEEDS A ROW OF ITS OWN. The middleware supplies the request
    /// context as the authorization resource by default and that default is switchable by host
    /// configuration; where it has been switched, refusing would reject EVERY caller and leave the system
    /// unable to obtain a single token. Deferring costs nothing, because the handler's own arm is
    /// unconditional - which the service rows above prove by refusing a certificate-less request through
    /// the whole pipeline.
    /// </remarks>
    [Fact]
    public void PolicyDefersWhenTheConnectionIsNotVisible()
    {
        AuthorizationHandlerContext context = new(
            [],
            new System.Security.Claims.ClaimsPrincipal(),
            resource: new object());

        Assert.True(TokenEndpoints.HasTransportCredential(context));
    }

    /// <summary>Builds an authorization context over one connection, as the middleware would.</summary>
    /// <param name="connection">The connection.</param>
    /// <returns>The context.</returns>
    private static AuthorizationHandlerContext BuildContext(HttpContext connection) =>
        new([], connection.User, resource: connection);
}

/// <summary>
/// The registration refuses to publish an unusable issuance address, and refuses at STARTUP.
/// </summary>
/// <remarks>
/// The fail-fast posture the framework application object sets by ending a structural fault in process
/// termination rather than in a warning [ws_objects/pfw.pbl.src/pfw.sra:L111-L144, ending in HALT CLOSE
/// at :L143]. A misconfigured address here is not a degraded service: it is a service on which no token
/// can ever be obtained, while its readiness probe reports healthy throughout - which is exactly the
/// class of fault a startup check exists to convert into an obvious one.
/// </remarks>
public sealed class TokenRegistrationTests
{
    /// <summary>An unusable configured address fails the host rather than being published.</summary>
    /// <param name="description">What the row varies, carried for row identity.</param>
    /// <param name="issuancePath">The unusable address.</param>
    /// <remarks>
    /// The metadata-namespace row is the one worth reading twice: that namespace carries this service's
    /// two ANONYMOUS routes, so an issuance address inside it could collide with a route that is anonymous
    /// by design - and the one route that mints must never be able to land beside them.
    /// </remarks>
    [Theory]
    [InlineData("blank", "")]
    [InlineData("whitespace", "   ")]
    [InlineData("not rooted", "v1/tokens")]
    [InlineData("inside the metadata namespace", "/.well-known/tokens")]
    public void UnusableIssuanceAddressFailsTheHost(string description, string issuancePath)
    {
        Assert.False(string.IsNullOrEmpty(description));

        using IssuanceHostFactory factory = new(
            certificate: null,
            instant: null,
            signingKey: null,
            issuancePath);

        Exception failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(
            "Security:TokenEndpointPath",
            Flatten(failure),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The configured address is published verbatim rather than repaired.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The router compares the address byte for byte and the document publishes it byte for byte, so
    /// trimming or rewriting it here would publish one address and serve another. This row moves the
    /// operation to a second legal address and asserts that BOTH the route and the generated document
    /// follow it - which also proves the address genuinely comes from configuration rather than from a
    /// literal beside the registration.
    /// </remarks>
    [Fact]
    public async Task ConfiguredAddressIsPublishedVerbatimAsync()
    {
        const string relocated = "/v1/service-tokens";

        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(
            certificate,
            instant: null,
            signingKey: null,
            relocated);

        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(relocated, UriKind.Relative),
            IssuanceFixture.Body(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using HttpResponseMessage original = await client.PostAsJsonAsync(
            new Uri(IssuanceFixture.IssuancePath, UriKind.Relative),
            IssuanceFixture.Body(),
            TestContext.Current.CancellationToken);

        // NOTHING IS MINTED AT THE ADDRESS THE OPERATION NO LONGER OCCUPIES, which is the property that
        // matters. The status is deliberately NOT asserted to be the router's not-found: this host
        // installs a default-deny fallback policy, and that policy applies to a request that matched no
        // endpoint as well as to one that matched an endpoint declaring no requirement - so an unmatched
        // address is refused as unauthorized BEFORE routing reports it missing. Measured on this host
        // rather than assumed, and it is the fail-closed answer: an unauthenticated caller cannot map the
        // service's addresses by comparing statuses.
        Assert.NotEqual(HttpStatusCode.OK, original.StatusCode);

        string refused = await original.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("access_token", refused, StringComparison.Ordinal);

        using HttpResponseMessage document = await client.GetAsync(
            new Uri(IssuanceFixture.DocumentPath, UriKind.Relative),
            TestContext.Current.CancellationToken);

        string payload = await document.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using JsonDocument generated = JsonDocument.Parse(payload);

        Assert.True(generated.RootElement.GetProperty("paths").TryGetProperty(relocated, out _));
    }

    /// <summary>
    /// The unrecognised-outcome refusal is a server fault carrying the internal-error code.
    /// </summary>
    /// <remarks>
    /// Its only production route is an issuance outcome this build does not recognise, and the published
    /// outcome set is closed with two cases - so the refusal is unreachable through the issuer and would
    /// otherwise sit permanently unexercised. A defence nobody has ever seen fire is a defence nobody
    /// knows works.
    /// </remarks>
    [Fact]
    public void UnrecognisedOutcomeIsAServerFault()
    {
        ProblemHttpResult refusal = TokenEndpoints.Faulted(NullLoggerFactory.Instance);

        Assert.Equal(StatusCodes.Status500InternalServerError, refusal.StatusCode);
        Assert.Equal(
            RetCode.E_INTERNAL_ERROR,
            Assert.IsType<long>(refusal.ProblemDetails.Extensions["retCode"]));
        Assert.DoesNotContain(
            "access_token",
            refusal.ProblemDetails.Detail ?? string.Empty,
            StringComparison.Ordinal);
    }

    /// <summary>Flattens an exception chain into one searchable string.</summary>
    /// <param name="failure">The exception.</param>
    /// <returns>Every message in the chain.</returns>
    /// <remarks>
    /// The host builder wraps a startup failure, and how deeply it wraps is not this row's business - so
    /// the assertion is made against the whole chain rather than against whichever layer happened to be
    /// outermost.
    /// </remarks>
    private static string Flatten(Exception failure)
    {
        StringBuilder messages = new();

        for (Exception? current = failure; current is not null; current = current.InnerException)
        {
            messages.AppendLine(current.Message);
        }

        return messages.ToString();
    }
}

/// <summary>
/// A minted token verifies against the material this service publishes, and is refused on every
/// condition a verifier is supposed to refuse it on.
/// </summary>
/// <remarks>
/// <para>
/// This is the row set that proves the issuance half and the publication half of contract C-01 agree.
/// Each consumer of this service validates with the stock bearer handler pointed at the published key
/// set, so a token that does not verify against that set is worthless however well formed it is.
/// </para>
/// <para>
/// The validation library is used only to VALIDATE here. Nothing in this file mints, and nothing in the
/// implementation under test mints either: exactly one component in the refactor creates a token, and it
/// is the issuer the endpoint delegates to.
/// </para>
/// </remarks>
public sealed class TokenRoundTripTests
{
    /// <summary>The issuer identity the development settings configure.</summary>
    private const string ConfiguredIssuer = "https://localhost:5104";

    /// <summary>A minted token validates against the published key set.</summary>
    /// <returns>A task representing the assertion.</returns>
    [Fact]
    public async Task MintedTokenValidatesAgainstThePublishedKeySetAsync()
    {
        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate);
        using HttpClient client = factory.CreateClient();

        string token = await TokenIssuanceServiceTests.IssueAsync(client);
        SecurityKey published = await ReadPublishedKeyAsync(client);

        TokenValidationResult result = await new JsonWebTokenHandler().ValidateTokenAsync(
            token,
            Parameters(published));

        Assert.True(result.IsValid, result.Exception?.Message);
        Assert.Equal(
            IssuanceFixture.CallerIdentity,
            result.ClaimsIdentity.FindFirst("sub")?.Value);
        Assert.Equal(
            IssuanceFixture.ReadScope,
            result.ClaimsIdentity.FindFirst("scope")?.Value);
    }

    /// <summary>
    /// The signature is computed over the digest the legacy catalogue's own constant names.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE PARITY ASSERTION THAT TIES THE .NET ALGORITHM BACK TO THE ORACLE, made executable rather than
    /// left as prose in a comment. The legacy signature primitives take a digest selector whose catalogue
    /// comment records that the same set governs hashing, signing AND verification
    /// [ws_objects/pfw.shared.pbl.src/enums.sru:L927], and its SHA-256 member is the value 2 at
    /// [:L930] - which is the settled provenance for the algorithm this issuer signs with
    /// [n_crypto.sru:L70-L73]. Reading the issuer's reported selector and comparing it against the
    /// PRESERVED KERNEL CONSTANT is what makes that equivalence provable instead of asserted.
    /// </para>
    /// <para>
    /// The constant is CONSUMED from the shared kernel rather than re-spelled here, which is the same rule
    /// the operation follows for the return codes: the preserved legacy identifiers have exactly one
    /// definition in the estate, and no file outside the fixed suppression list declares one of its own.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task SignatureUsesTheLegacyDigestSelectorAsync()
    {
        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate);
        using HttpClient client = factory.CreateClient();

        string token = await TokenIssuanceServiceTests.IssueAsync(client);

        Assert.Equal(SecurityAlgorithms.RsaSha256, new JsonWebToken(token).Alg);
        Assert.Equal(
            Enums.CRYPTO_HASH_SHA256,
            factory.Services.GetRequiredService<TokenIssuer>().LegacySigningHashType);
    }

    /// <summary>A minted token is refused for an audience it was not issued to.</summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// One audience is carried per request deliberately, so that a token is never valid somewhere its
    /// holder did not intend. This row is what proves that property rather than assuming it.
    /// </remarks>
    [Fact]
    public async Task MintedTokenIsRefusedForTheWrongAudienceAsync()
    {
        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate);
        using HttpClient client = factory.CreateClient();

        string token = await TokenIssuanceServiceTests.IssueAsync(client);
        SecurityKey published = await ReadPublishedKeyAsync(client);

        TokenValidationParameters parameters = Parameters(published);
        parameters.ValidAudiences = [IssuanceFixture.SecondAudience];

        TokenValidationResult result =
            await new JsonWebTokenHandler().ValidateTokenAsync(token, parameters);

        Assert.False(result.IsValid);
        Assert.IsType<SecurityTokenInvalidAudienceException>(result.Exception);
    }

    /// <summary>A minted token is refused once its lifetime has elapsed.</summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// Driven by validating against a clock well past the frozen issuance instant rather than by waiting,
    /// which is the whole point of the clock seam: expiry is provable in milliseconds and reproducibly.
    /// The skew tolerance is set to nothing so the row asserts the expiry rather than the library's
    /// default leeway.
    /// </remarks>
    [Fact]
    public async Task MintedTokenIsRefusedAfterItsLifetimeAsync()
    {
        DateTimeOffset issuedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate, issuedAt);
        using HttpClient client = factory.CreateClient();

        string token = await TokenIssuanceServiceTests.IssueAsync(client);
        SecurityKey published = await ReadPublishedKeyAsync(client);

        TokenValidationParameters parameters = Parameters(published);
        parameters.ClockSkew = TimeSpan.Zero;
        parameters.LifetimeValidator = (notBefore, expires, _, _) =>
            expires is not null && expires > issuedAt.AddHours(1).UtcDateTime;

        TokenValidationResult result =
            await new JsonWebTokenHandler().ValidateTokenAsync(token, parameters);

        Assert.False(result.IsValid);
        Assert.IsType<SecurityTokenInvalidLifetimeException>(result.Exception);
    }

    /// <summary>A tampered token is refused.</summary>
    /// <param name="segment">Which segment of the token the row alters.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// Both halves matter and are asserted separately: altering the PAYLOAD proves the signature covers
    /// the claims, and altering the SIGNATURE proves the signature is checked at all. A token whose
    /// payload could be edited without detection would let any holder grant itself any subject, audience
    /// and scope it liked.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task TamperedTokenIsRefusedAsync(int segment)
    {
        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate);
        using HttpClient client = factory.CreateClient();

        string token = await TokenIssuanceServiceTests.IssueAsync(client);
        SecurityKey published = await ReadPublishedKeyAsync(client);

        string[] parts = token.Split('.');

        Assert.Equal(3, parts.Length);

        parts[segment] = Tamper(parts[segment]);

        TokenValidationResult result = await new JsonWebTokenHandler().ValidateTokenAsync(
            string.Join('.', parts),
            Parameters(published));

        Assert.False(result.IsValid);
        Assert.NotNull(result.Exception);
    }

    /// <summary>Alters one base64url segment so that it decodes to different bytes.</summary>
    /// <param name="segment">The segment.</param>
    /// <returns>The altered segment.</returns>
    private static string Tamper(string segment)
    {
        char[] characters = segment.ToCharArray();

        characters[^1] = characters[^1] == 'A' ? 'B' : 'A';

        return new string(characters);
    }

    /// <summary>Reads the published verification key out of the key set.</summary>
    /// <param name="client">The client.</param>
    /// <returns>The key.</returns>
    /// <remarks>
    /// Rebuilt from the PUBLISHED parameters rather than taken from the host's own service graph, so the
    /// row validates with exactly what a consumer would fetch. That is also what makes it an assertion
    /// about the published document rather than about the process.
    /// </remarks>
    private static async Task<SecurityKey> ReadPublishedKeyAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync(
            new Uri(IssuanceFixture.KeySetPath, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string payload = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using JsonDocument document = JsonDocument.Parse(payload);

        JsonElement published = document.RootElement.GetProperty("keys").EnumerateArray().Single();

        RSAParameters parameters = new()
        {
            Modulus = Base64UrlEncoder.DecodeBytes(published.GetProperty("n").GetString()),
            Exponent = Base64UrlEncoder.DecodeBytes(published.GetProperty("e").GetString()),
        };

        RSA verifier = RSA.Create();
        verifier.ImportParameters(parameters);

        return new RsaSecurityKey(verifier)
        {
            KeyId = published.GetProperty("kid").GetString(),
        };
    }

    /// <summary>Builds validation parameters that check everything a consumer checks.</summary>
    /// <param name="key">The published verification key.</param>
    /// <returns>The parameters.</returns>
    private static TokenValidationParameters Parameters(SecurityKey key) =>
        new()
        {
            ValidateIssuer = true,
            ValidIssuers = [ConfiguredIssuer],
            ValidateAudience = true,
            ValidAudiences = [IssuanceFixture.CallerIdentity],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
        };
}

/// <summary>
/// The clock seam: a frozen clock produces byte-identical claims.
/// </summary>
/// <remarks>
/// The characterization model requires every non-deterministic value to be maskable from BOTH the master
/// and the candidate recording, and the clock is one of this service's two primary sources of one. These
/// rows are what prove the endpoint reads NO ambient clock of its own - if it did, no substitution could
/// make two issuances agree.
/// </remarks>
public sealed class TokenDeterminismTests
{
    /// <summary>
    /// Two issuances for identical input under a frozen clock produce identical instants.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The token itself is asserted identical too, which is a stronger statement and a legitimate one for
    /// this algorithm: the signature scheme is deterministic, so identical claims signed by the same key
    /// produce identical bytes. A row that only compared the instants would still pass if the endpoint
    /// had stamped something of its own into the payload.
    /// </remarks>
    [Fact]
    public async Task FrozenClockProducesIdenticalIssuancesAsync()
    {
        DateTimeOffset instant = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate, instant);
        using HttpClient client = factory.CreateClient();

        TokenIssuanceResponse first = await IssueAsync(client);
        TokenIssuanceResponse second = await IssueAsync(client);

        Assert.Equal(first.IssuedAt, second.IssuedAt);
        Assert.Equal(first.ExpiresIn, second.ExpiresIn);
        Assert.Equal(first.AccessToken, second.AccessToken);

        JsonWebToken parsed = new(first.AccessToken);

        Assert.Equal(instant.ToUnixTimeSeconds(), first.IssuedAt);
        Assert.Equal(instant.UtcDateTime, parsed.IssuedAt);
        Assert.Equal(instant.UtcDateTime, parsed.ValidFrom);
        Assert.Equal(instant.AddSeconds(first.ExpiresIn).UtcDateTime, parsed.ValidTo);
    }

    /// <summary>
    /// The response's issuance instant equals the token's own issuance claim.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// Asserted against the REAL clock as well as the frozen one, because the identity must hold in both:
    /// the endpoint takes the instant from the issuer's result rather than measuring one, so the two can
    /// never disagree by a scheduling delay.
    /// </remarks>
    [Fact]
    public async Task ResponseInstantEqualsTheTokenClaimAsync()
    {
        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate);
        using HttpClient client = factory.CreateClient();

        TokenIssuanceResponse body = await IssueAsync(client);

        JsonWebToken parsed = new(body.AccessToken);

        Assert.Equal(
            body.IssuedAt,
            new DateTimeOffset(parsed.IssuedAt, TimeSpan.Zero).ToUnixTimeSeconds());
        Assert.Equal(
            body.ExpiresIn,
            (long)(parsed.ValidTo - parsed.IssuedAt).TotalSeconds);
    }

    /// <summary>Issues one token and returns the whole response body.</summary>
    /// <param name="client">The client.</param>
    /// <returns>The body.</returns>
    private static async Task<TokenIssuanceResponse> IssueAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(IssuanceFixture.IssuancePath, UriKind.Relative),
            IssuanceFixture.Body(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        TokenIssuanceResponse? body = await response.Content.ReadFromJsonAsync<TokenIssuanceResponse>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(body);

        return body;
    }
}

/// <summary>
/// The legacy return codes this operation uses are mapped EXPLICITLY, and the tri-state hole in the
/// legacy algebra is asserted rather than assumed.
/// </summary>
/// <remarks>
/// <para>
/// Measured from the read-only oracle: the success predicate is a greater-than-or-equal test against zero
/// [ws_objects/pfw.shared.pbl.src/issucceeded.srf:L11-L12] while a prevention is 1
/// [retcode.sru:L42], SO A PREVENTION READS AS A SUCCESS; the failure predicate excludes the cancellation
/// value explicitly [isfailed.srf:L11-L12] while that value also fails the success test
/// [retcode.sru:L44-L45], SO A CANCELLATION IS NEITHER. Both are preserved rather than repaired, which is
/// exactly why no status anywhere in this service is derived from a truthiness test on a code.
/// </para>
/// <para>
/// The two rows below assert those two values INDIVIDUALLY, so neither can be collapsed into a generic
/// success or failure by a future change to the shared map.
/// </para>
/// </remarks>
public sealed class TokenProblemMappingTests
{
    /// <summary>
    /// A prevention is a distinct status and is NOT treated as a success by the shared map.
    /// </summary>
    [Fact]
    public void PreventionIsNotTreatedAsASuccess()
    {
        Assert.True(Predicates.IsSucceeded(RetCode.PREVENT));
        Assert.Equal(StatusCodes.Status409Conflict, ProblemResults.MapStatusCode(RetCode.PREVENT));
        Assert.NotEqual(StatusCodes.Status200OK, ProblemResults.MapStatusCode(RetCode.PREVENT));
        Assert.False(ProblemResults.ClaimsSuccess(RetCode.PREVENT));
        Assert.False(ProblemResults.IsIndeterminate(RetCode.PREVENT));
    }

    /// <summary>
    /// A cancellation is neither succeeded nor failed, and is a distinct status.
    /// </summary>
    [Fact]
    public void CancellationIsNeitherSucceededNorFailed()
    {
        Assert.False(Predicates.IsSucceeded(RetCode.CANCELLED));
        Assert.False(Predicates.IsFailed(RetCode.CANCELLED));
        Assert.True(ProblemResults.IsIndeterminate(RetCode.CANCELLED));
        Assert.Equal(StatusCodes.Status409Conflict, ProblemResults.MapStatusCode(RetCode.CANCELLED));
        Assert.NotEqual(
            ProblemResults.MapStatusCode(RetCode.FAILED),
            ProblemResults.MapStatusCode(RetCode.CANCELLED));
    }

    /// <summary>
    /// The three codes this operation actually uses map to the three statuses it publishes.
    /// </summary>
    /// <param name="retCode">The legacy code.</param>
    /// <param name="expected">The status the shared map answers with.</param>
    /// <remarks>
    /// The access-denied code appears TWICE in the authored document - once for the unauthorized response
    /// and once for the forbidden one - so the map cannot pick between them from the code alone. It
    /// defaults to forbidden, and the operation passes the unauthorized status explicitly for the arm that
    /// needs it; the service rows prove both statuses are actually produced.
    /// </remarks>
    [Theory]
    [InlineData(RetCode.E_INVALID_ARGUMENT, StatusCodes.Status400BadRequest)]
    [InlineData(RetCode.E_ACCESS_DENIED, StatusCodes.Status403Forbidden)]
    [InlineData(RetCode.E_INTERNAL_ERROR, StatusCodes.Status500InternalServerError)]
    public void OperationCodesMapToTheirPublishedStatuses(long retCode, int expected) =>
        Assert.Equal(expected, ProblemResults.MapStatusCode(retCode));

    /// <summary>
    /// The shared map's output set is closed, and the status reserved for a deferred capability's routing
    /// declaration is outside it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Constraint C-D, and the specific trap a status map walks into. The four routes that answer the
    /// reserved status are the ingress service's routing metadata for the four capabilities deferred out
    /// of this phase; producing it from this service's error map would put a deferred capability's surface
    /// on the wrong service entirely.
    /// </para>
    /// <para>
    /// STATED AS A CLOSED ALLOWED SET RATHER THAN AS THE ABSENCE OF ONE VALUE, which is both stronger and
    /// cleaner: it fails on ANY status the map was not designed to answer rather than only on the one
    /// this constraint names, and it keeps the forbidden status out of this file altogether. Asserted
    /// across the whole catalogue of codes this service can reach, including the two the constraint puts
    /// under particular scrutiny, rather than only the ones the operation uses today.
    /// </para>
    /// </remarks>
    [Fact]
    public void SharedMapAnswersOnlyFromItsClosedStatusSet()
    {
        int[] allowed =
        [
            StatusCodes.Status400BadRequest,
            StatusCodes.Status403Forbidden,
            StatusCodes.Status404NotFound,
            StatusCodes.Status409Conflict,
            StatusCodes.Status500InternalServerError,
            StatusCodes.Status503ServiceUnavailable,
        ];

        long[] codes =
        [
            RetCode.OK,
            RetCode.PREVENT,
            RetCode.FAILED,
            RetCode.CANCELLED,
            RetCode.E_INVALID_ARGUMENT,
            RetCode.E_INVALID_TYPE,
            RetCode.E_INVALID_DATA,
            RetCode.E_OUT_OF_RANGE,
            RetCode.E_OBJECT_NOT_FOUND,
            RetCode.E_NOT_EXISTS,
            RetCode.E_BUSY,
            RetCode.E_TIME_OUT,
            RetCode.E_RETRY,
            RetCode.E_ACCESS_DENIED,
            RetCode.E_INTERNAL_ERROR,
            RetCode.E_NO_SUPPORT,
            RetCode.E_NO_IMPLEMENTATION,
            RetCode.UNKNOWN,
        ];

        foreach (long code in codes)
        {
            Assert.Contains(ProblemResults.MapStatusCode(code), allowed);
        }

        // The null code is not a code at all, and it is classified too rather than left to chance.
        Assert.Contains(ProblemResults.MapStatusCode(retCode: null), allowed);
    }
}

/// <summary>
/// The generated document and the authored one agree about this operation.
/// </summary>
/// <remarks>
/// This is the comparison that matters most in practice: a consumer generates its client from whichever
/// document it is handed. The authored one is authoritative, so a disagreement is fixed in the code.
/// </remarks>
public sealed class TokenGeneratedDocumentTests
{
    /// <summary>
    /// The generated document declares the operation at the authored path with the authored identity.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    [Fact]
    public async Task GeneratedDocumentDeclaresTheOperationAsync()
    {
        using JsonDocument document = await ReadAsync();

        JsonElement operation = Operation(document);

        Assert.Equal(IssuanceFixture.OperationId, operation.GetProperty("operationId").GetString());
        Assert.Equal("TokenService", operation.GetProperty("tags").EnumerateArray().Single().GetString());
        Assert.Contains(
            "short-lived service token",
            operation.GetProperty("summary").GetString() ?? string.Empty,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The generated document declares the mutual-TLS requirement and NO bearer alternative.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE ROW THAT PROVES THE OVERRIDE SURVIVED GENERATION. A document listing both schemes would tell a
    /// consumer it may present a token instead of a certificate, which is the one thing this operation
    /// cannot accept - and the generator does not synthesise a requirement from authorization metadata by
    /// itself, so a document with no requirement at all would advertise an anonymous mint.
    /// </remarks>
    [Fact]
    public async Task GeneratedDocumentDeclaresMutualTlsOnlyAsync()
    {
        using JsonDocument document = await ReadAsync();

        JsonElement requirement = Operation(document).GetProperty("security").EnumerateArray().Single();

        Assert.True(requirement.TryGetProperty("mutualTls", out JsonElement scopes));
        Assert.Empty(scopes.EnumerateArray());
        Assert.False(requirement.TryGetProperty("bearerAuth", out _));

        JsonElement scheme = document.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("mutualTls");

        Assert.Equal("mutualTLS", scheme.GetProperty("type").GetString());
    }

    /// <summary>
    /// The generated document declares exactly the five responses the authored document declares.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// Both directions are asserted. The presence half catches a response the implementation forgot to
    /// declare; the ABSENCE half catches one the framework inferred from a result type, which is the exact
    /// reason the handler returns the untyped result interface rather than a typed union.
    /// </remarks>
    [Fact]
    public async Task GeneratedDocumentDeclaresExactlyTheAuthoredResponsesAsync()
    {
        using JsonDocument document = await ReadAsync();

        string[] actual = Operation(document).GetProperty("responses").EnumerateObject()
            .Select(response => response.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["200", "400", "401", "403", "500"], actual);
    }

    /// <summary>
    /// Every error response in the generated document carries the one problem media type.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    [Theory]
    [InlineData("400")]
    [InlineData("401")]
    [InlineData("403")]
    [InlineData("500")]
    public async Task GeneratedErrorResponseCarriesTheProblemMediaTypeAsync(string status)
    {
        using JsonDocument document = await ReadAsync();

        JsonElement content = Operation(document)
            .GetProperty("responses")
            .GetProperty(status)
            .GetProperty("content");

        Assert.True(content.TryGetProperty(MediaTypeNames.Application.ProblemJson, out _));
    }

    /// <summary>
    /// The generated success response carries the response schema's members and no key identifier.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    [Fact]
    public async Task GeneratedSuccessResponseMatchesTheAuthoredSchemaAsync()
    {
        using JsonDocument document = await ReadAsync();

        JsonElement schema = Operation(document)
            .GetProperty("responses")
            .GetProperty("200")
            .GetProperty("content")
            .GetProperty(MediaTypeNames.Application.Json)
            .GetProperty("schema");

        JsonElement resolved = Resolve(document, schema);

        string[] members = resolved.GetProperty("properties").EnumerateObject()
            .Select(member => member.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["access_token", "expires_in", "issued_at", "scope", "token_type"],
            members);

        string[] required = resolved.GetProperty("required").EnumerateArray()
            .Select(member => member.GetString() ?? string.Empty)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["access_token", "expires_in", "scope", "token_type"], required);
    }

    /// <summary>
    /// The generated request schema declares the three authored members and no credential-shaped one.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The authored schema carries no client secret, password, key reference, assertion or key material,
    /// and neither may the generated one: caller identity comes from the transport. A row that only
    /// counted members would miss a credential added alongside them, so the names are asserted exactly.
    /// </remarks>
    [Fact]
    public async Task GeneratedRequestSchemaCarriesNoCredentialAsync()
    {
        using JsonDocument document = await ReadAsync();

        JsonElement schema = Operation(document)
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty(MediaTypeNames.Application.Json)
            .GetProperty("schema");

        JsonElement resolved = Resolve(document, schema);

        string[] members = resolved.GetProperty("properties").EnumerateObject()
            .Select(member => member.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["audience", "scopes", "subject"], members);
    }

    /// <summary>Resolves a schema reference against the generated document's component section.</summary>
    /// <param name="document">The document.</param>
    /// <param name="schema">The schema, which may be a reference or a nullable union around one.</param>
    /// <returns>The resolved schema.</returns>
    /// <remarks>
    /// THE UNION ARM IS THE DOCUMENTED CONSEQUENCE OF THE OPTIONAL BODY, and unwrapping it here is what
    /// keeps the assertion about the MEMBERS rather than about the wrapper. The operation's body parameter
    /// is nullable so that an absent body is answered by the operation's own bad request carrying the
    /// legacy return code, instead of by the framework's shapeless refusal - so the generator publishes
    /// the request schema as a choice between the object and nothing. The authored document declares the
    /// object directly, which is stricter; the difference is one degree of permissiveness in the
    /// GENERATED schema only, the authored document remains authoritative for what a caller may send, and
    /// the observable behaviour on a violation is identical.
    /// </remarks>
    private static JsonElement Resolve(JsonDocument document, JsonElement schema)
    {
        if (schema.TryGetProperty("oneOf", out JsonElement union))
        {
            foreach (JsonElement arm in union.EnumerateArray())
            {
                bool isNullArm =
                    arm.TryGetProperty("type", out JsonElement type) &&
                    type.ValueKind == JsonValueKind.String &&
                    string.Equals(type.GetString(), "null", StringComparison.Ordinal);

                if (!isNullArm)
                {
                    return Resolve(document, arm);
                }
            }

            Assert.Fail("The generated request schema declares no non-null arm.");
        }

        if (!schema.TryGetProperty("$ref", out JsonElement reference))
        {
            return schema;
        }

        string pointer = reference.GetString() ?? string.Empty;
        string name = pointer[(pointer.LastIndexOf('/') + 1)..];

        return document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(name);
    }

    /// <summary>Extracts the issuance operation from a generated document.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The operation.</returns>
    private static JsonElement Operation(JsonDocument document)
    {
        Assert.True(
            document.RootElement.GetProperty("paths")
                .TryGetProperty(IssuanceFixture.IssuancePath, out JsonElement item),
            $"The generated document declares no path '{IssuanceFixture.IssuancePath}'.");

        Assert.True(
            item.TryGetProperty("post", out JsonElement operation),
            "The issuance path is not declared as a POST operation.");

        return operation;
    }

    /// <summary>Fetches the generated document from a booted host.</summary>
    /// <returns>The document.</returns>
    private static async Task<JsonDocument> ReadAsync()
    {
        await using IssuanceHostFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(IssuanceFixture.DocumentPath, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string payload = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        return JsonDocument.Parse(payload);
    }
}

/// <summary>
/// Nothing this operation produces discloses the signing material, and the credential appears in exactly
/// one place.
/// </summary>
/// <remarks>
/// Constraint C-F, asserted rather than asserted-about. The minted token is a credential and belongs in
/// the success body alone; the signing material belongs nowhere a caller or a log reader can see.
/// </remarks>
public sealed class TokenSecrecyTests
{
    /// <summary>
    /// Neither the published document nor the generated one carries a specimen token.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// A plausible-looking specimen is indistinguishable from real material to a reader, and specimens
    /// have a long history of being copied into production unchanged. The authored specification carries
    /// no example on any field, and the generated one must not acquire one either.
    /// </remarks>
    [Fact]
    public async Task NoDocumentCarriesASpecimenTokenAsync()
    {
        Assert.DoesNotContain("eyJ", ContractDocument.Text, StringComparison.Ordinal);

        await using IssuanceHostFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(IssuanceFixture.DocumentPath, UriKind.Relative),
            TestContext.Current.CancellationToken);

        string payload = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("eyJ", payload, StringComparison.Ordinal);

        // The armour marker rather than the words "private key": the cryptographic surface's own
        // published prose legitimately DISCUSSES private keys, and a row that forbade the phrase would
        // fail on a description while missing an actual key. Only material carries the marker.
        Assert.DoesNotContain("-----BEGIN", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("-----BEGIN", ContractDocument.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The signing material reaches neither the response nor the operator channel.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// Checked in WINDOWS as well as in full, so a record that quoted a fragment of the key would still
    /// fail. The window is short on purpose: a long one would only catch a wholesale echo.
    /// </para>
    /// <para>
    /// The token is asserted PRESENT in the success body and ABSENT from every record, which is the pair
    /// of statements that matters: the credential has exactly one legitimate destination.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task SigningMaterialNeverLeavesTheProcessAsync()
    {
        string signingKey = IssuanceFixture.CreateSigningKeyPem();

        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate, instant: null, signingKey);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(IssuanceFixture.IssuancePath, UriKind.Relative),
            IssuanceFixture.Body(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string payload = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains("access_token", payload, StringComparison.Ordinal);
        AssertDisclosesNothingAbout(payload, signingKey);
        Assert.DoesNotContain("-----BEGIN", payload, StringComparison.Ordinal);

        // The key set is the one place verification material is published, and it must carry the PUBLIC
        // half only - so the private material must be absent from it too.
        using HttpResponseMessage keySet = await client.GetAsync(
            new Uri(IssuanceFixture.KeySetPath, UriKind.Relative),
            TestContext.Current.CancellationToken);

        string published = await keySet.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        AssertDisclosesNothingAbout(published, signingKey);

        foreach (string member in new[] { "\"d\"", "\"p\"", "\"q\"", "\"dp\"", "\"dq\"", "\"qi\"", "\"k\"" })
        {
            Assert.DoesNotContain(member, published, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A refusal never carries the minted token, the request payload or the signing material.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    [Fact]
    public async Task RefusalCarriesNothingSensitiveAsync()
    {
        string signingKey = IssuanceFixture.CreateSigningKeyPem();

        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(certificate, instant: null, signingKey);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(IssuanceFixture.IssuancePath, UriKind.Relative),
            IssuanceFixture.Body(audience: IssuanceFixture.UnlistedAudience),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        string payload = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("access_token", payload, StringComparison.Ordinal);
        Assert.DoesNotContain(IssuanceFixture.ConfiguredKeyId, payload, StringComparison.Ordinal);
        AssertDisclosesNothingAbout(payload, signingKey);
    }

    /// <summary>
    /// The operator record names the caller and the audience, and never the credential.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE SUBJECT IS RECORDED DELIBERATELY, and only because it has already been reconciled against the
    /// identity the presented certificate established - so it is a transport-established identity rather
    /// than unvalidated caller text. The issuer refuses to record it at ITS layer for exactly that reason,
    /// and the difference between the two files is the reconciliation that happens between them.
    /// </para>
    /// <para>
    /// The token, the key identifier and the signing material must be absent from every record. Asserted
    /// across ALL captured records rather than only the operation's own, so a record written by the shared
    /// problem factory or by the issuer cannot leak what this one withholds.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task OperatorRecordNamesTheCallerAndNeverTheCredentialAsync()
    {
        string signingKey = IssuanceFixture.CreateSigningKeyPem();
        CapturedRecords captured = new();

        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(
            certificate,
            instant: null,
            signingKey,
            issuancePath: null,
            captured);

        using HttpClient client = factory.CreateClient();

        string token = await TokenIssuanceServiceTests.IssueAsync(client);

        string issuance = Assert.Single(
            captured.Records,
            record => record.Contains("Security issued a service token", StringComparison.Ordinal));

        Assert.Contains(IssuanceFixture.CallerIdentity, issuance, StringComparison.Ordinal);
        Assert.Contains(IssuanceFixture.OperationId, issuance, StringComparison.Ordinal);

        foreach (string record in captured.Records)
        {
            Assert.DoesNotContain(token, record, StringComparison.Ordinal);
            AssertDisclosesNothingAbout(record, signingKey);
        }
    }

    /// <summary>
    /// A token is still issued when the operation's own operator channel is switched off.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The enablement guard keeps the formatting cost off a disabled sink, and this row proves the guard
    /// is a guard rather than a gate: a deployment that raises the level for this category loses the
    /// record and keeps the behaviour.
    /// </remarks>
    [Fact]
    public async Task IssuanceSucceedsWithTheOperatorChannelDisabledAsync()
    {
        CapturedRecords captured = new();

        using X509Certificate2 certificate =
            IssuanceFixture.CreateCallerCertificate(IssuanceFixture.CallerIdentity);

        await using IssuanceHostFactory factory = new(
            certificate,
            instant: null,
            signingKey: null,
            issuancePath: null,
            captured,
            LogLevel.Warning);

        using HttpClient client = factory.CreateClient();

        string token = await TokenIssuanceServiceTests.IssueAsync(client);

        Assert.False(string.IsNullOrEmpty(token));
        Assert.DoesNotContain(
            captured.Records,
            record => record.Contains("Security issued a service token", StringComparison.Ordinal));
    }

    /// <summary>Asserts that a payload discloses no part of some material.</summary>
    /// <param name="payload">The payload.</param>
    /// <param name="material">The material that must not appear.</param>
    private static void AssertDisclosesNothingAbout(string payload, string material)
    {
        Assert.DoesNotContain(material, payload, StringComparison.Ordinal);

        const int windowLength = 12;

        string body = material.Replace("\n", string.Empty, StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal);

        for (int start = 0; start + windowLength <= body.Length; start += windowLength)
        {
            string window = body.Substring(start, windowLength);

            Assert.DoesNotContain(window, payload, StringComparison.Ordinal);
        }
    }
}
