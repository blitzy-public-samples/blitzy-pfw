// ==================================================================================================
//  ScopeAuthorizationTests.cs - THE SCOPE CLAIM IS CHECKED, AND THAT IS OBSERVABLE
//  ------------------------------------------------------------------------------------------------
//  WHAT THESE ROWS PROTECT
//  Security mints a `scope` claim on every token and security.v1.yaml declares a `403` on all eighteen
//  operations of the C-02 contract whose description says the token may be valid and still not carry
//  the scope the operation requires. Until the policy these rows exercise existed, nothing in the
//  service read that claim: every authenticated route was reachable by any token addressed to this
//  service, whatever it was scoped to, so a caller granted the cryptographic surface for one purpose
//  held all of it and the published 403 was unreachable.
//
//  THE THREE STATUSES THAT MUST STAY DISTINCT, AND WHY EACH ROW IS NEEDED
//    401 - no usable token at all. The standing C-G proof, and it must NOT become 403: telling a caller
//          with no credential that its credential is insufficient sends it to fix the wrong thing.
//    403 - a usable token that is not scoped for this surface. The row below called
//          AnUnscopedTokenIsRefused is the only place this is observable, because it needs a token that
//          is valid in every respect except its scope - which nothing else in the suite mints.
//    200 - a usable token carrying the scope. The positive arm, and load-bearing: every refusal row
//          here would also pass against a service that refused everything.
//
//  WHAT IS DELIBERATELY NOT SCOPE-GATED
//  `/health` and the two `/.well-known/` publications are anonymous, so they reach no scope check
//  because they reach no authentication - and a row below asserts that adding the scope gate did not
//  accidentally close them, which would break the readiness chain three services wait on. Those three
//  are also the ONLY operations security.v1.yaml declares without a 403; every operation that reaches
//  authentication declares one.
//  `POST /v1/tokens` carries no bearer token - it is authenticated by a presented Basic credential or a
//  trusted client certificate - so it has no scope claim to check; its authorization is the
//  caller-and-audience matrix, exercised in CallerAuthorizationTests.cs.
//
//  AND `/v1/ping` IS SCOPE-GATED, WHICH IS EASY TO ASSUME IT IS NOT. It performs no work and reaches no
//  capability, so a permission over it reads like a permission over nothing - but Endpoints/PingEndpoints.cs
//  names a scope policy on the route rather than taking the parameterless requirement, and the published
//  document declares the matching 403. An unscoped token reaches it with 403, not 200.
//
//  RULES
//  review_rules reports that no user rules were provided, so no user-specified rule governs this file.
// ==================================================================================================

using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

using PowerFramework.Security.Authorization;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Security.Tests;

/// <summary>
/// The C-02 surface requires its scope, the anonymous routes are unaffected, and the three statuses
/// stay distinct.
/// </summary>
public sealed class ScopeAuthorizationTests
{
    /// <summary>A digest operation, chosen because it resolves no reference and takes no key.</summary>
    /// <remarks>
    /// DELIBERATELY THE KEYLESS ONE. It is the operation whose 403 could ONLY come from the scope gate:
    /// a keyRef-taking operation can answer 403 for an unpermitted reference too, so a row driving one of
    /// those could pass for the wrong reason. This route had no 403 at all before the scope requirement
    /// existed, which makes it the sharpest probe in the contract.
    /// </remarks>
    private const string KeylessRoute = "/v1/crypto/hash";

    /// <summary>A body the digest operation accepts, so a refusal cannot be a binding failure.</summary>
    /// <remarks>
    /// WELL FORMED ON PURPOSE. Authorization is evaluated before model binding, so an empty body would
    /// also produce the refusal - and the row would then pass against a service that had no scope gate
    /// and merely rejected the body. A body the operation would happily process makes the refusal
    /// attributable to authorization and nothing else, which the positive arm then confirms by receiving
    /// a 200 for the very same body.
    /// </remarks>
    private static object Body => new
    {
        data = "payload",
        payloadForm = "STRING",
        hashType = 2L,
    };

    /// <summary>
    /// THE POSITIVE ARM: a token carrying the scope reaches the surface.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// First, because every refusal row below would also pass against a service that refused every
    /// request. The same body is used as in the refusal rows, so the two differ in the token alone.
    /// </remarks>
    [Fact]
    public async Task AScopedTokenReachesTheCryptographicSurfaceAsync()
    {
        await using SecurityAppFactory factory = new();
        using HttpClient client = factory.CreateAuthenticatedClient(
            SecurityAppFactory.DefaultTokenSubject,
            factory.ResolveInboundAudience(),
            [SecurityScopes.Crypto]);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(KeylessRoute, UriKind.Relative),
            Body,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// A token that is valid in every respect EXCEPT its scope is refused with the published 403.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE ROW THE WHOLE FINDING REDUCES TO. The token is minted by this host's own issuer, addressed to
    /// the audience this host's inbound handler accepts, signed by the key it publishes, and unexpired -
    /// so nothing about it can be refused except the one scope it does not carry. Before the policy
    /// existed this request answered 200.
    /// </para>
    /// <para>
    /// FORBIDDEN AND NOT UNAUTHORIZED, and the distinction is the contract's: 401 means no usable token,
    /// 403 means a usable token that is not scoped for this surface. The return code is asserted as well
    /// as the status, because the published body carries it and a status without it is a response this
    /// service would not be producing.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnUnscopedTokenIsRefusedWithTheForbiddenStatusAsync()
    {
        await using SecurityAppFactory factory = new();
        using HttpClient client = factory.CreateAuthenticatedClient(
            SecurityAppFactory.DefaultTokenSubject,
            factory.ResolveInboundAudience(),
            [SecurityAppFactory.DefaultTokenScope]);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(KeylessRoute, UriKind.Relative),
            Body,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains(
            $"\"retCode\":{RetCode.E_ACCESS_DENIED}",
            body.Replace(" ", string.Empty, StringComparison.Ordinal),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// NO TOKEN IS STILL 401, NOT 403. The scope gate did not collapse the two statuses.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The regression this row guards is specific and easy to introduce: a named policy REPLACES the
    /// fallback policy for the route that names it, so a scope policy registered WITHOUT
    /// <c>RequireAuthenticatedUser</c> would leave this route as the only one in the service not
    /// demanding authentication - and it would still appear to work, because an anonymous principal
    /// carries no scope claim and would be refused anyway. It would be refused with 403, telling a caller
    /// with no credential at all that its credential was merely insufficient.
    /// </remarks>
    [Fact]
    public async Task NoTokenIsStillRefusedAsUnauthorizedAsync()
    {
        await using SecurityAppFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(KeylessRoute, UriKind.Relative),
            Body,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// A MULTI-SCOPE TOKEN IS ACCEPTED, which the framework's own claim requirement would refuse.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE REASON A CUSTOM REQUIREMENT EXISTS AT ALL, pinned as a behaviour. The issuer stamps ONE claim
    /// carrying a SPACE-DELIMITED set, so a token granted two scopes carries the single value
    /// "&lt;a&gt; &lt;b&gt;". <c>RequireClaim("scope", "security.crypto")</c> compares the claim's WHOLE
    /// value and would refuse it - a failure in the safe direction, but one that makes a correctly scoped
    /// multi-scope caller unable to call anything.
    /// </para>
    /// <para>
    /// The required scope is placed SECOND in the set, so a handler that only examined the first entry -
    /// or that compared a prefix - fails this row. That ordering is the whole point of the row.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AMultiScopeTokenIsAcceptedAsync()
    {
        await using SecurityAppFactory factory = new();
        using HttpClient client = factory.CreateAuthenticatedClient(
            SecurityAppFactory.DefaultTokenSubject,
            factory.ResolveInboundAudience(),
            [SecurityAppFactory.DefaultTokenScope, SecurityScopes.Crypto]);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(KeylessRoute, UriKind.Relative),
            Body,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// The scope comparison is ORDINAL: a differently cased scope does not satisfy the requirement.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// Consistent with every other identity comparison in the system, and specifically with the issuer's
    /// own authorization matrix - which compares ordinally, so a case-insensitive check here would let a
    /// route accept a scope spelling the issuer never granted. The two layers would then disagree about
    /// what a token carries, and the disagreement would only ever be visible as an operation succeeding
    /// that should not have.
    /// </remarks>
    [Fact]
    public async Task TheScopeComparisonIsOrdinalAsync()
    {
        await using SecurityAppFactory factory = new()
        {
            // The issuer refuses to grant a scope its matrix does not permit, so the mis-cased spelling
            // has to be permitted BEFORE it can be granted - otherwise this row would observe the
            // issuer's refusal rather than the route's, and would pass for the wrong reason.
            ShapeOptions = options =>
            {
                foreach (CallerAuthorizationOptionsShim row in CallerRows(options))
                {
                    row.Add(SecurityScopes.Crypto.ToUpperInvariant());
                }
            },
        };

        using HttpClient client = factory.CreateAuthenticatedClient(
            SecurityAppFactory.DefaultTokenSubject,
            factory.ResolveInboundAudience(),
            [SecurityScopes.Crypto.ToUpperInvariant()]);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(KeylessRoute, UriKind.Relative),
            Body,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// EVERY operation of the C-02 contract requires the scope, not merely the one probed above.
    /// </summary>
    /// <param name="path">The route path.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE GROUP-WIDE GUARANTEE, ASSERTED OPERATION BY OPERATION. The requirement is declared once at the
    /// group precisely so that no operation can be forgotten, and this row is what proves the declaration
    /// actually reaches all of them - a property that a single probe cannot establish and that a future
    /// per-operation override would silently break.
    /// </para>
    /// <para>
    /// The body is deliberately EMPTY for these rows. Authorization runs before model binding, so a
    /// refusal here is attributable to the policy whatever the body is - and the row above already
    /// proves, with a body the operation accepts, that the refusal is not a binding failure in disguise.
    /// Building eighteen valid bodies would restate the request-shape suite for no additional coverage.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(CryptoRoutes))]
    public async Task EveryCryptographicOperationRequiresTheScopeAsync(string path)
    {
        await using SecurityAppFactory factory = new();
        using HttpClient client = factory.CreateAuthenticatedClient(
            SecurityAppFactory.DefaultTokenSubject,
            factory.ResolveInboundAudience(),
            [SecurityAppFactory.DefaultTokenScope]);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(path, UriKind.Relative),
            new { },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// The anonymous routes are untouched by the scope gate.
    /// </summary>
    /// <param name="path">The anonymous route.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The orchestration depends on this: three services gate their own readiness on <c>/health</c>, and
    /// the same three fetch their verification material from the two <c>/.well-known/</c> publications
    /// with no credential at all. A scope requirement that leaked onto any of them would take the whole
    /// deployment down while every service behaved exactly as written.
    /// </remarks>
    [Theory]
    [InlineData("/health")]
    [InlineData("/.well-known/jwks.json")]
    [InlineData("/.well-known/openid-configuration")]
    public async Task TheAnonymousRoutesRemainAnonymousAsync(string path)
    {
        await using SecurityAppFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(path, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// <c>/v1/ping</c> requires authentication AND the probe scope, and all three answers are pinned.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// A DECISION ASSERTED, NOT AN OMISSION LEFT UNTESTED - AND THE OPPOSING ARGUMENT IS RECORDED BECAUSE
    /// IT IS A GOOD ONE. It runs: the probe requires NO scope, because it performs no work and reaches no
    /// capability, so a scope for it is a permission over nothing - and, decisively, if the published
    /// document declared no 403 on it then requiring one would make the service answer a status its own
    /// contract did not describe.
    /// </para>
    /// <para>
    /// THAT LAST PREMISE DOES NOT HOLD HERE, AND THE DOCUMENT IS THE ARBITER FOR ANYTHING ON THE WIRE.
    /// <c>shared/PowerFramework.Contracts/OpenApi/security.v1.yaml</c> declares
    /// <c>403 ScopeForbidden</c> on <c>GET /v1/ping</c> alongside its 200 and 401, the shipped issuance
    /// roster grants each caller that is expected to prove a boundary the <c>ping</c> scope, and
    /// <c>orchestration/.env.example</c> grants it to the end-to-end suite. A probe left scopeless while
    /// every one of those says otherwise would be the one authenticated route on this service whose
    /// credential requirement is weaker than the deployment states - and the argument against a scope
    /// ("a permission over nothing") cuts the other way once the estate provisions one: an unenforced
    /// scope in a roster reads exactly like an enforced one.
    /// </para>
    /// <para>
    /// ALL THREE ANSWERS ARE ASSERTED, because any two of them are satisfiable by a wrong implementation. A
    /// token carrying the scope is 200; a token carrying an unrelated scope is the published 403 - the arm
    /// that separates "authenticated is enough" from "authorised"; and no token at all is 401, which is the
    /// environment's own documented access note for this endpoint.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ThePingProbeRequiresAuthenticationAndTheProbeScopeAsync()
    {
        await using SecurityAppFactory factory = new();

        using HttpClient scoped = factory.CreateAuthenticatedClient(
            SecurityAppFactory.DefaultTokenSubject,
            factory.ResolveInboundAudience(),
            [SecurityScopes.Ping]);

        using HttpResponseMessage authenticated = await scoped.GetAsync(
            new Uri("/v1/ping", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);

        using HttpClient unrelated = factory.CreateAuthenticatedClient(
            SecurityAppFactory.DefaultTokenSubject,
            factory.ResolveInboundAudience(),
            [SecurityAppFactory.DefaultTokenScope]);

        using HttpResponseMessage forbidden = await unrelated.GetAsync(
            new Uri("/v1/ping", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using HttpClient anonymous = factory.CreateClient();

        using HttpResponseMessage refused = await anonymous.GetAsync(
            new Uri("/v1/ping", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    /// <summary>
    /// Every scope the service declares is registered as a policy, and each demands that scope.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE REGISTRATION IS READ BACK FROM THE PROVIDER rather than inferred from a route's behaviour, so
    /// a scope added to the declared list without a registration fails here with a clear cause instead of
    /// surfacing as an unexplained 500 at whichever route named the missing policy - which is what the
    /// framework answers for an unregistered policy name.
    /// </para>
    /// <para>
    /// It also asserts the authentication requirement is present on each policy, which is the property
    /// the 401-versus-403 row above depends on and the one a hand-written policy is most likely to omit.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task EveryDeclaredScopeIsRegisteredAsAPolicyAsync()
    {
        await using SecurityAppFactory factory = new();

        IAuthorizationPolicyProvider provider = factory.Services
            .GetRequiredService<IAuthorizationPolicyProvider>();

        Assert.NotEmpty(SecurityScopes.All);

        foreach (string scope in SecurityScopes.All)
        {
            // THROUGH THE SAME COMPOSITION THE REGISTRATION USES. Reading back under the BARE scope string
            // is what let a registration keyed one way and a route requiring another coexist unnoticed:
            // the policy existed, so this row passed, and the route still answered 500.
            AuthorizationPolicy? policy = await provider.GetPolicyAsync(
                SecurityScopes.PolicyNameFor(scope));

            Assert.NotNull(policy);

            Assert.Contains(
                policy.Requirements,
                requirement => requirement is DenyAnonymousAuthorizationRequirement);

            Assert.Contains(
                policy.Requirements,
                requirement => requirement
                    .GetType()
                    .GetProperty("Scope")
                    ?.GetValue(requirement) as string == scope);
        }
    }

    /// <summary>Every POST route of the C-02 contract.</summary>
    /// <returns>The routes.</returns>
    /// <remarks>
    /// Read from the shared operation table the contract suite already maintains, so a route added there
    /// is covered here without a second list to remember. The table is itself checked against the
    /// authored document, so it cannot drift into agreeing only with the implementation.
    /// </remarks>
    public static TheoryData<string> CryptoRoutes()
    {
        TheoryData<string> rows = [];

        foreach (CryptoOperation operation in CryptoFixture.Operations)
        {
            rows.Add(operation.Path);
        }

        return rows;
    }

    /// <summary>
    /// The authorization rows of an options instance, exposed as a minimal scope-adding surface.
    /// </summary>
    /// <param name="options">The options being shaped.</param>
    /// <returns>One shim per row.</returns>
    /// <remarks>
    /// A shim rather than the option type directly, so the one row that needs to widen a permitted scope
    /// set says exactly that and cannot accidentally rewrite a caller or an audience.
    /// </remarks>
    private static IEnumerable<CallerAuthorizationOptionsShim> CallerRows(
        Security.Configuration.SecurityOptions options) =>
        options.CallerAuthorizations.Select(row => new CallerAuthorizationOptionsShim(row));

    /// <summary>Adds a permitted scope to one authorization row.</summary>
    /// <param name="row">The row.</param>
    private sealed class CallerAuthorizationOptionsShim(
        Security.Configuration.CallerAuthorizationOptions row)
    {
        /// <summary>Permits one further scope on the wrapped row.</summary>
        /// <param name="scope">The scope to permit.</param>
        public void Add(string scope) => row.Scopes.Add(scope);
    }
}
