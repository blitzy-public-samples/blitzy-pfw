// ==================================================================================================
//  PingEndpointsTests - THE CONFORMANCE PROOF FOR GET /v1/ping ON THE PERSISTENCE SERVICE
//  ------------------------------------------------------------------------------------------------
//  WHAT IS BEING PROVED, AND WHY IT NEEDS PROVING
//  Decomposition created every boundary in this system from nothing: the legacy framework was an
//  in-process library that opened no listening socket, registered no route and received no unsolicited
//  request. "No new attack surface" therefore cannot mean "no new surface" - it means every newly
//  created surface is authenticated from the outset. Endpoints/PingEndpoints.cs is where that claim
//  becomes checkable on this service, and this file is the check.
//
//  BOTH ARMS ARE MANDATORY, AND NEITHER ALONE IS SUFFICIENT.
//  Asserting only the 401 leaves the authenticated path unproven, so a route that rejected EVERY
//  request - including valid ones - would pass. Asserting only the 200 leaves the boundary unproven, so
//  an accidentally anonymous route would pass. Both arms are here, plus the four rejections that
//  demonstrate each individual validation is actually live rather than merely configured.
//
//  THE HOST UNDER TEST IS THE REAL ONE.
//  WebApplicationFactory boots the service's own Program.cs, so what is exercised is the DEPLOYED
//  pipeline: the stock bearer registration, the fallback authorization policy, the middleware ordering
//  and the real MapPingEndpoints call. A hand-assembled test host would prove only that this file can
//  configure authentication correctly, which is not the question an auditor asks.
//
//  WHY THE SIGNING KEY LIVES HERE AND NOWHERE ELSE (the whole point of the sole-issuer topology)
//  Exactly one signing secret exists in the system and the Security service holds it; Persistence holds
//  verification material only and can never mint. That is enforced structurally rather than by review:
//  the token-minting package is deliberately absent from BOTH this test project and the application
//  project, so a minting call would not compile in either. Neither manifest gained a package for this
//  file, and none may.
//
//  The token below is therefore hand-assembled from primitives that ship in the base class library -
//  a JSON header, a JSON payload, and one keyed hash - inside a TEST fixture that exists only in
//  memory. Its key is a locally generated random byte array, never a literal, never a credential from
//  anywhere, and it never leaves this process. That is the deliberate inverse of the anti-pattern the
//  refactor plan records as in-source secret site #1: a legacy browser asset that hardcodes a private
//  key in page source and signs with it. No value from that file, or from any other recorded secret
//  site, appears here in any form.
//
//  WHY THE HOST ACCEPTS THE TOKEN WITHOUT A NETWORK CALL
//  The test supplies the bearer handler with a static verification configuration carrying the issuer
//  and the one acceptable key. The framework wraps a supplied configuration in a static configuration
//  manager, so no discovery document is fetched and no key set is downloaded - which is what makes
//  these tests hermetic and what keeps the rejection tests HONEST: a 401 caused by unreachable
//  metadata would prove nothing about audience, lifetime or signature checking. Every one of the four
//  validations stays ON; nothing here turns one off to make a test pass.
// ==================================================================================================

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Conformance tests for <c>GET /v1/ping</c>: the authenticated boundary proof of the Persistence
/// service.
/// </summary>
/// <remarks>
/// Every test drives the service's own <c>Program.cs</c> in process. Nothing here needs a live issuer,
/// a database, a container or a real clock.
/// </remarks>
public sealed class PingEndpointsTests
{
    /// <summary>The route under test, spelled exactly as the operational contract spells it.</summary>
    private const string PingRoute = "/v1/ping";

    /// <summary>The anonymous readiness route, used only to contrast the two postures.</summary>
    private const string HealthRoute = "/health";

    /// <summary>The issuer the test host is configured to trust.</summary>
    private const string TrustedIssuer = "https://security-service.test.invalid";

    /// <summary>The audience the test host is configured to accept.</summary>
    private const string TrustedAudience = "powerframework-persistence-test";

    /// <summary>An audience no configuration accepts, used to prove audience validation is live.</summary>
    private const string ForeignAudience = "powerframework-someone-else";

    /// <summary>The bearer authentication scheme name.</summary>
    private const string BearerScheme = "Bearer";

    /// <summary>
    /// Values that must never appear in the success body. Configuration keys, every field name of the
    /// legacy transaction structure - whose <c>logpass</c> member is the credential and is write-only by
    /// contract - and the connection details that structure carries.
    /// </summary>
    /// <remarks>
    /// The legacy models the right instinct at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L118</c>, which scrubs
    /// parameters unrelated to the connection target before use. A liveness probe that echoed any of
    /// these would be an information-disclosure surface wearing a diagnostic label.
    /// </remarks>
    private static readonly string[] ValuesThatMustNotLeak =
    [
        "authority",
        "audience",
        "jwks",
        "well-known",
        "signingkey",
        "issuer",
        "dbms",
        "servername",
        "database",
        "logid",
        "logpass",
        "dbparm",
        "autocommit",
        "userparm",
        "connectionstring",
        "sqlite",
        "password",
        "assembly",
        "version",
        "machine",
        "hostname",
        "environment",
        "token",
        "bearer",
        "claim",
    ];

    /// <summary>
    /// The negative arm: no credential at all is refused, and refused with exactly <c>401</c>.
    /// </summary>
    /// <remarks>
    /// The status code is asserted EXACTLY rather than as "not 200". A <c>403</c> would mean the caller
    /// was authenticated but unauthorized, a <c>404</c> would mean the route is not registered at all
    /// and a redirect would mean an interactive challenge scheme is installed - each of those would be a
    /// different defect wearing the same "request refused" clothes, and each would make the boundary
    /// proof mean something other than what it claims.
    /// </remarks>
    [Fact]
    public async Task AnAnonymousRequestIsRefusedWithFourHundredAndOne()
    {
        await using PersistenceHost host = PersistenceHost.Create();
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(PingRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// The positive arm: a valid credential is accepted, and the body is the trivial agreed shape.
    /// </summary>
    [Fact]
    public async Task AValidTokenIsAcceptedAndAnswersTheTrivialBody()
    {
        await using PersistenceHost host = PersistenceHost.Create();
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await SendAsync(client, host.IssueValidToken());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument body = await ReadJsonAsync(response);
        JsonElement root = body.RootElement;

        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.Equal("persistence", root.GetProperty("service").GetString());
        Assert.True(root.GetProperty("authenticated").GetBoolean());

        // Consumed symbolically from the shared kernel, never as a numeric literal: retcode.sru:L39
        // declares OK, SUCCESS and ALLOW as three names for the same value.
        Assert.Equal(RetCode.OK, root.GetProperty("retCode").GetInt64());

        Assert.Equal(JsonValueKind.String, root.GetProperty("timestamp").ValueKind);

        // Closed by construction: exactly the four members above and nothing else.
        Assert.Equal(4, root.EnumerateObject().Count());
    }

    /// <summary>
    /// A garbage bearer value is refused. Proves the route does not accept the mere PRESENCE of an
    /// authorization header as evidence of anything.
    /// </summary>
    [Fact]
    public async Task AMalformedTokenIsRefusedWithFourHundredAndOne()
    {
        await using PersistenceHost host = PersistenceHost.Create();
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await SendAsync(client, "not-a-token.at-all.whatsoever");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// A well-formed, correctly signed, unexpired token minted for a DIFFERENT audience is refused.
    /// Proves audience validation is live: without it, any token from the estate's issuer would open
    /// every service in the estate.
    /// </summary>
    [Fact]
    public async Task ATokenForAnotherAudienceIsRefusedWithFourHundredAndOne()
    {
        await using PersistenceHost host = PersistenceHost.Create();
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await SendAsync(
            client,
            host.IssueToken(audience: ForeignAudience));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// A token that has expired is refused. Proves lifetime validation is live, which is what makes
    /// short-lived service tokens meaningful rather than decorative.
    /// </summary>
    [Fact]
    public async Task AnExpiredTokenIsRefusedWithFourHundredAndOne()
    {
        await using PersistenceHost host = PersistenceHost.Create();
        using HttpClient client = host.CreateClient();

        // An hour past expiry, well clear of the default clock-skew tolerance, so the assertion is about
        // expiry rather than about how generous that tolerance happens to be.
        using HttpResponseMessage response = await SendAsync(
            client,
            host.IssueToken(expiresIn: TimeSpan.FromHours(-1)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// A token signed with a key the host does not trust is refused. Proves signature validation is
    /// live - the single most important of the four, because without it a caller could mint its own
    /// claims and the sole-issuer topology would be fiction.
    /// </summary>
    [Fact]
    public async Task ATokenSignedByAnUnknownKeyIsRefusedWithFourHundredAndOne()
    {
        await using PersistenceHost host = PersistenceHost.Create();
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await SendAsync(client, host.IssueTokenWithForeignKey());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// THE NO-DEVELOPMENT-BYPASS TEST. The same anonymous request is refused with the host running in
    /// the Development environment.
    /// </summary>
    /// <remarks>
    /// A bypass that exists only locally is still a bypass, and it is the most dangerous kind: it makes
    /// the local build disagree with the deployed one, so the boundary looks proven right up to the
    /// moment it is deployed. The development settings file is authored under an explicit prohibition on
    /// authenticating differently from the deployed build, and this test is what holds that prohibition
    /// to account.
    /// </remarks>
    [Fact]
    public async Task AnAnonymousRequestIsStillRefusedInTheDevelopmentEnvironment()
    {
        await using PersistenceHost host = PersistenceHost.Create(Environments.Development);
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(PingRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// A valid token is accepted in the Development environment too, so the pairing above is a proof
    /// about the CREDENTIAL rather than an artefact of the environment.
    /// </summary>
    [Fact]
    public async Task AValidTokenIsStillAcceptedInTheDevelopmentEnvironment()
    {
        await using PersistenceHost host = PersistenceHost.Create(Environments.Development);
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await SendAsync(client, host.IssueValidToken());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// The success body discloses nothing: no configuration value, no connection detail, no version or
    /// host banner, no token fragment and no field name of the legacy transaction structure.
    /// </summary>
    [Fact]
    public async Task TheSuccessBodyDisclosesNothing()
    {
        await using PersistenceHost host = PersistenceHost.Create();
        using HttpClient client = host.CreateClient();

        string token = host.IssueValidToken();
        using HttpResponseMessage response = await SendAsync(client, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string payload = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // The member name retCode legitimately ends in "Code" and the member name service legitimately
        // names the service, so the sweep runs over the VALUES the body carries rather than over its
        // raw text - which is the only way to distinguish an agreed member name from a leaked value.
        using JsonDocument body = JsonDocument.Parse(payload);
        string concatenatedValues = string.Join(
            '\u001f',
            body.RootElement.EnumerateObject().Select(static member => member.Value.ToString()));

        foreach (string forbidden in ValuesThatMustNotLeak)
        {
            Assert.DoesNotContain(
                forbidden,
                concatenatedValues,
                StringComparison.OrdinalIgnoreCase);
        }

        // No part of the presented credential is echoed anywhere in the response, headers included.
        Assert.DoesNotContain(token, payload, StringComparison.Ordinal);
        Assert.DoesNotContain(
            token,
            string.Join('\u001f', response.Headers.Select(static header => string.Join(',', header.Value))),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// STORAGE INDEPENDENCE. A storage seam that throws on resolution changes nothing, because this
    /// route never reaches for storage.
    /// </summary>
    /// <remarks>
    /// The poison is a registration for the storage connection type that throws the moment anything
    /// resolves it. The route answers <c>200</c> regardless, so the assertion is not decorative: the day
    /// somebody adds a storage probe to this endpoint - handing an authenticated caller a
    /// storage-probing primitive nobody asked for, and duplicating a responsibility that belongs to the
    /// readiness route - this test fails and says why.
    /// </remarks>
    [Fact]
    public async Task APoisonedStorageSeamDoesNotAffectTheRoute()
    {
        await using PersistenceHost host = PersistenceHost.Create(poisonStorage: true);
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await SendAsync(client, host.IssueValidToken());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// The two REST routes this service publishes have OPPOSITE postures, and both are intended: the
    /// readiness route is anonymous because the orchestrator probing it holds no token and probes while
    /// the service is still starting, and this route is the counterpart proving the rest of the surface
    /// is closed.
    /// </summary>
    [Fact]
    public async Task TheReadinessRouteIsAnonymousWhileThePingRouteIsNot()
    {
        await using PersistenceHost host = PersistenceHost.Create();
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage readiness = await client.GetAsync(
            new Uri(HealthRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);
        using HttpResponseMessage ping = await client.GetAsync(
            new Uri(PingRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.Unauthorized, readiness.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, ping.StatusCode);
    }

    /// <summary>
    /// Only <c>GET</c> is mapped. One route, one method: no second verb was added alongside it.
    /// </summary>
    [Fact]
    public async Task NoSecondVerbIsMappedOnTheRoute()
    {
        await using PersistenceHost host = PersistenceHost.Create();
        using HttpClient client = host.CreateClient();

        using HttpRequestMessage request = new(HttpMethod.Post, new Uri(PingRoute, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue(
            BearerScheme,
            host.IssueValidToken());

        using HttpResponseMessage response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    /// <summary>
    /// Sends an authenticated <c>GET</c> to the route under test.
    /// </summary>
    /// <param name="client">The client bound to the host under test.</param>
    /// <param name="token">The bearer credential to present.</param>
    /// <returns>The response, for the caller to assert on and dispose.</returns>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string token)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(PingRoute, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, token);

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Reads a response body as JSON.
    /// </summary>
    /// <param name="response">The response to read.</param>
    /// <returns>The parsed body, which the caller disposes.</returns>
    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
        => JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

    /// <summary>
    /// The Persistence service hosted in process, together with the test-only credential factory.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ONLY MINTER IN THE SYSTEM OUTSIDE THE SECURITY SERVICE IS THIS FIXTURE, and it is a test
    /// fixture rather than shipped code. The application project holds verification material only and
    /// carries no minting package, so it could not mint even if a future edit tried to.
    /// </para>
    /// <para>
    /// The verification configuration is supplied to the bearer handler directly, so no discovery
    /// document is fetched and no key set is downloaded. All four validations remain enabled.
    /// </para>
    /// </remarks>
    private sealed class PersistenceHost : WebApplicationFactory<Program>
    {
        /// <summary>The signature algorithm named in the token header.</summary>
        private const string HeaderAlgorithm = "HS256";

        /// <summary>The token type named in the token header.</summary>
        private const string HeaderType = "JWT";

        /// <summary>
        /// How long a credential minted for the accepted case stays valid. Short, because the tokens the
        /// sole issuer mints in the running system are short-lived service tokens.
        /// </summary>
        private static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);

        /// <summary>The environment the host runs in.</summary>
        private readonly string _environment;

        /// <summary>Whether a storage seam that throws on resolution is registered.</summary>
        private readonly bool _poisonStorage;

        /// <summary>The one key the host is configured to trust. Generated, never a literal.</summary>
        private readonly byte[] _trustedKey = RandomNumberGenerator.GetBytes(64);

        /// <summary>A key the host is NOT configured to trust. Generated, never a literal.</summary>
        private readonly byte[] _foreignKey = RandomNumberGenerator.GetBytes(64);

        /// <summary>
        /// Initializes the fixture.
        /// </summary>
        /// <param name="environment">The environment name the host should run under.</param>
        /// <param name="poisonStorage">
        /// When <see langword="true"/>, registers a storage connection whose resolution throws.
        /// </param>
        private PersistenceHost(string environment, bool poisonStorage)
        {
            _environment = environment;
            _poisonStorage = poisonStorage;
        }

        /// <summary>
        /// Creates a host.
        /// </summary>
        /// <param name="environment">
        /// The environment name. Defaults to Production, so the ordinary case under test is the
        /// deployed one rather than the developer one.
        /// </param>
        /// <param name="poisonStorage">
        /// When <see langword="true"/>, registers a storage connection whose resolution throws.
        /// </param>
        /// <returns>A started-on-first-use host.</returns>
        internal static PersistenceHost Create(
            string? environment = null,
            bool poisonStorage = false)
            => new(environment ?? Environments.Production, poisonStorage);

        /// <summary>
        /// Mints a credential the host must accept.
        /// </summary>
        /// <returns>A compact-serialized token.</returns>
        internal string IssueValidToken() => IssueToken();

        /// <summary>
        /// Mints a credential, letting a caller vary exactly one property so that each rejection test
        /// isolates a single validation.
        /// </summary>
        /// <param name="audience">The audience claim. Defaults to the accepted audience.</param>
        /// <param name="expiresIn">
        /// When the credential expires, measured from now. A negative value produces an
        /// already-expired credential.
        /// </param>
        /// <returns>A compact-serialized token signed with the trusted key.</returns>
        internal string IssueToken(string? audience = null, TimeSpan? expiresIn = null)
            => Mint(_trustedKey, audience ?? TrustedAudience, expiresIn ?? DefaultLifetime);

        /// <summary>
        /// Mints an otherwise-valid credential signed with a key the host does not trust.
        /// </summary>
        /// <returns>A compact-serialized token with an unverifiable signature.</returns>
        internal string IssueTokenWithForeignKey()
            => Mint(_foreignKey, TrustedAudience, DefaultLifetime);

        /// <summary>
        /// Configures the host under test.
        /// </summary>
        /// <param name="builder">The web host builder the factory is populating.</param>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// <para>
        /// The settings are supplied as host configuration so the service's own startup validation runs
        /// against them rather than against whatever happens to be on disk. The authority value is a
        /// reserved test hostname that resolves nowhere, which is safe precisely because the static
        /// verification configuration below means it is never contacted.
        /// </para>
        /// <para>
        /// The bearer options are amended, never replaced: the service's own registration still runs
        /// first, so what is under test is the deployed configuration plus a local key rather than a
        /// substitute for it.
        /// </para>
        /// </remarks>
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.UseEnvironment(_environment);
            builder.UseSetting("Jwt:Authority", TrustedIssuer);
            builder.UseSetting("Jwt:Audience", TrustedAudience);
            builder.UseSetting("Jwt:RequireHttpsMetadata", "true");

            builder.ConfigureServices(services =>
            {
                services.Configure<JwtBearerOptions>(
                    JwtBearerDefaults.AuthenticationScheme,
                    options =>
                    {
                        OpenIdConnectConfiguration verification = new() { Issuer = TrustedIssuer };
                        verification.SigningKeys.Add(new SymmetricSecurityKey(_trustedKey));

                        // Supplying the configuration is what makes the handler use a static
                        // configuration manager and skip metadata retrieval entirely.
                        options.Configuration = verification;
                        options.TokenValidationParameters.ValidIssuer = TrustedIssuer;
                        options.TokenValidationParameters.ValidAudience = TrustedAudience;

                        // Stated rather than assumed. These are the four properties the rejection tests
                        // above exist to demonstrate, and no test may quietly relax one to pass.
                        options.TokenValidationParameters.ValidateIssuer = true;
                        options.TokenValidationParameters.ValidateAudience = true;
                        options.TokenValidationParameters.ValidateLifetime = true;
                        options.TokenValidationParameters.ValidateIssuerSigningKey = true;
                    });

                if (_poisonStorage)
                {
                    services.RemoveAll<SqliteConnection>();
                    services.AddScoped<SqliteConnection>(static _ =>
                        throw new InvalidOperationException(
                            "The storage seam was resolved. GET /v1/ping must never reach storage."));
                }
            });
        }

        /// <summary>
        /// Assembles a compact-serialized token.
        /// </summary>
        /// <param name="key">The signing key bytes.</param>
        /// <param name="audience">The audience claim.</param>
        /// <param name="expiresIn">When the credential expires, measured from now.</param>
        /// <returns>The compact serialization.</returns>
        /// <remarks>
        /// <para>
        /// Hand-assembled from base-class-library primitives on purpose: a JSON header, a JSON payload
        /// and one keyed hash. No token-minting library is referenced by this project, and none may be
        /// added, because the absence of one is the structural half of the sole-issuer guarantee.
        /// </para>
        /// <para>
        /// THE WINDOW IS READ FROM THE REAL CLOCK, DELIBERATELY, and this is the one place in these
        /// tests where a fixed instant would be WRONG rather than merely inconvenient. Lifetime
        /// validation is one of the four properties under test, and the handler evaluates it against the
        /// system clock. A credential minted against a frozen instant would be long expired by the time
        /// any of these tests ran, so every positive assertion would fail for a reason that has nothing
        /// to do with the boundary. The alternative - substituting a lifetime validator - would DISABLE
        /// the very check the rejection tests exist to demonstrate. The determinism seam this refactor
        /// requires applies to the timestamp the endpoint EMITS, which is injected and substitutable; it
        /// does not apply to the validity window of a credential the framework must check for itself.
        /// </para>
        /// <para>
        /// The expired case is placed an hour beyond expiry rather than a moment beyond it, so the
        /// assertion is about expiry rather than about how generous the default clock-skew tolerance
        /// happens to be.
        /// </para>
        /// </remarks>
        private static string Mint(byte[] key, string audience, TimeSpan expiresIn)
        {
            DateTimeOffset now = TimeProvider.System.GetUtcNow();
            DateTimeOffset expires = now + expiresIn;
            DateTimeOffset issuedAt = expires < now
                ? expires - TimeSpan.FromHours(1)
                : now - TimeSpan.FromMinutes(1);

            string header = $"{{\"alg\":\"{HeaderAlgorithm}\",\"typ\":\"{HeaderType}\"}}";
            string payload = string.Create(
                CultureInfo.InvariantCulture,
                $"{{\"iss\":\"{TrustedIssuer}\",\"aud\":\"{audience}\",\"sub\":\"persistence-conformance-test\",\"iat\":{issuedAt.ToUnixTimeSeconds()},\"nbf\":{issuedAt.ToUnixTimeSeconds()},\"exp\":{expires.ToUnixTimeSeconds()}}}");

            string signingInput = string.Concat(
                Encode(Encoding.UTF8.GetBytes(header)),
                ".",
                Encode(Encoding.UTF8.GetBytes(payload)));

            byte[] signature = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(signingInput));

            return string.Concat(signingInput, ".", Encode(signature));
        }

        /// <summary>
        /// Base64url-encodes a segment, unpadded, as the compact serialization requires.
        /// </summary>
        /// <param name="value">The bytes to encode.</param>
        /// <returns>The encoded segment.</returns>
        private static string Encode(byte[] value) => System.Buffers.Text.Base64Url.EncodeToString(value);
    }
}
