// ==================================================================================================
//  ScopeAuthorizationTests.cs - THE INGRESS CHECKS THE SCOPE IT PUBLISHES A 403 FOR
//  ------------------------------------------------------------------------------------------------
//  WHAT THESE ROWS PROTECT
//  gateway.v1.yaml declares a `403` on forty-one of this service's fifty operations, and its shared
//  `Forbidden` component says what the status means: the token is valid but does not carry the scope the
//  operation requires, "deliberately distinguished from `401` so a caller can tell a missing credential
//  from an insufficient one". Until the policies these rows exercise existed nothing here read the scope
//  claim. Every authenticated route was reachable by any token addressed to this service, whatever it was
//  scoped to - at the one boundary in the whole system an external client can reach - and the published
//  403 was unreachable, because the only other thing that could have produced it was a projected
//  `PermissionDenied` from an upstream that was not producing one either.
//
//  THE THREE STATUSES THAT MUST STAY DISTINCT
//    401 - no usable token. The standing C-G proof. It must NOT become 403, or a caller with no
//          credential is told its credential was merely insufficient.
//    403 - a usable token that is not scoped for the operation. Only observable with a token that is
//          valid in every OTHER respect, which is what the fixture's scoped issuer exists to mint.
//    2xx - a usable token carrying the scope. The positive arm, and load-bearing: every refusal row here
//          would also pass against a service that refused everything.
//
//  WHAT IS DELIBERATELY NOT SCOPE-GATED, ASSERTED RATHER THAN OMITTED
//  `/health` is anonymous - C-10 requires it and three services gate their readiness on it - so a scope
//  requirement leaking onto it would take a deployment down while every service behaved as written. THE
//  EIGHT RESERVED DEFERRED OPERATIONS are authenticated and carry NO scope: they reach no capability by
//  construction, each answering `501` and naming the service that will eventually serve it, so a scope
//  there would be a permission over a feature that does not exist - and requiring one would turn the
//  published `501` into a `403`, hiding the shape of the eventual system that those routes exist to
//  publish.
//
//  RULES
//  review_rules reports that no user rules were provided, so no user-specified rule governs this file.
// ==================================================================================================

using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using PowerFramework.Gateway.Authorization;

using Xunit;

namespace PowerFramework.Gateway.Tests;

/// <summary>
/// Each ingress surface requires its own scope, the two exempt surfaces stay exempt, and the three
/// statuses stay distinct.
/// </summary>
public sealed class ScopeAuthorizationTests
{
    /// <summary>The authenticated probe.</summary>
    private const string PingRoute = "/v1/ping";

    /// <summary>The capability projection.</summary>
    private const string CapabilitiesRoute = "/v1/capabilities";

    /// <summary>
    /// A projected DataWindow operation. The contract publishes this one as a <c>GET</c> taking the
    /// session through the query string, and the ingress maps it with that verb.
    /// </summary>
    private const string DataWindowRoute = "/v1/datawindow/event-gate?sessionId=probe";

    /// <summary>
    /// A projected expression operation, under the same prefix and therefore the same scope.
    /// </summary>
    /// <remarks>
    /// A <c>POST</c>, and not by preference. Of the twenty-four operations the contract publishes under
    /// <c>/v1/datawindow/expression/</c>, twenty-three are <c>POST</c> and one is a <c>DELETE</c>; there is
    /// no <c>GET</c> anywhere under the prefix. A probe that assumed the verb from the neighbouring
    /// DataWindow route would be answered <c>405 Method Not Allowed</c> - which is neither the refusal nor
    /// the acceptance these rows distinguish, and which would make the positive arm pass for the wrong
    /// reason, since a 405 is also not a 403. <see cref="VerbFor"/> is why that cannot happen quietly.
    /// </remarks>
    private const string ExpressionRoute = "/v1/datawindow/expression/state";

    // ==============================================================================================
    //  1. EACH SURFACE REQUIRES ITS OWN SCOPE.
    // ==============================================================================================

    /// <summary>
    /// A token carrying every scope EXCEPT the one a surface requires is refused by that surface.
    /// </summary>
    /// <param name="route">The route.</param>
    /// <param name="withheld">The single scope withheld from the token.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// WITHHOLDING ONE SCOPE RATHER THAN PRESENTING ONE. Presenting a single scope would also refuse a
    /// surface that required nothing in particular but happened to reject the token for another reason;
    /// withholding exactly one from an otherwise complete set makes the missing scope the only possible
    /// cause, and it is also how the failure would actually arise in a deployment - a caller granted most
    /// of what it needs.
    /// </para>
    /// <para>
    /// The two DataWindow rows share a scope on purpose: the expression sub-surface is under the same path
    /// prefix and the ingress gates it with the same name, which is the deliberate difference from
    /// DataServices - where the two projected contracts have independent scopes because the two gRPC
    /// services version independently.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(PingRoute, GatewayScopes.Ping)]
    [InlineData(CapabilitiesRoute, GatewayScopes.Capabilities)]
    [InlineData(DataWindowRoute, GatewayScopes.DataWindow)]
    [InlineData(ExpressionRoute, GatewayScopes.DataWindow)]
    public async Task ASurfaceIsRefusedWhenItsOwnScopeIsWithheldAsync(string route, string withheld)
    {
        await using GatewayTestHostFixture host = new();

        string[] granted =
        [
            .. GatewayScopes.All.Where(scope => !string.Equals(scope, withheld, StringComparison.Ordinal)),
        ];

        Assert.Equal(
            HttpStatusCode.Forbidden,
            await GetAsync(host, route, granted));
    }

    /// <summary>
    /// THE POSITIVE ARM: the same route with the same token plus its one scope is not refused.
    /// </summary>
    /// <param name="route">The route.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// Every refusal row above would pass against a service that refused everything, so this is the row
    /// that makes them mean something. It asserts NOT-403 rather than a particular success status because
    /// the projected routes reach an upstream that is not running in this fixture - so their answer past
    /// the gate is an upstream failure, and the distinction that matters is authorization versus
    /// everything else.
    /// </para>
    /// <para>
    /// The full declared set is presented, which is also the set the end-to-end suite requests - so this
    /// row doubles as the assertion that the vocabulary the suite sends is the vocabulary the service
    /// accepts. A divergence between them would otherwise surface only in a container bring-up.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(PingRoute)]
    [InlineData(CapabilitiesRoute)]
    [InlineData(DataWindowRoute)]
    [InlineData(ExpressionRoute)]
    public async Task TheSameRouteIsNotRefusedWithTheFullScopeSetAsync(string route)
    {
        await using GatewayTestHostFixture host = new();

        Assert.NotEqual(
            HttpStatusCode.Forbidden,
            await GetAsync(host, route, GatewayScopes.All));
    }

    /// <summary>
    /// A token carrying NO scope at all is refused by all three scope-gated surfaces.
    /// </summary>
    /// <param name="route">The route.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The simplest statement of the fix, and the one that most directly contradicts the pre-fix
    /// behaviour: this token is valid in every respect the credential rows in the sibling authorization
    /// suite check - trusted key, accepted audience, expected issuer, live window - and before the
    /// policies existed it reached all three surfaces.
    /// </remarks>
    [Theory]
    [InlineData(PingRoute)]
    [InlineData(CapabilitiesRoute)]
    [InlineData(DataWindowRoute)]
    public async Task AnUnscopedTokenIsRefusedByEveryGatedSurfaceAsync(string route)
    {
        await using GatewayTestHostFixture host = new();

        Assert.Equal(HttpStatusCode.Forbidden, await GetAsync(host, route, []));
    }

    /// <summary>
    /// The scope names are not interchangeable: one surface's scope does not open another's.
    /// </summary>
    /// <param name="route">The route.</param>
    /// <param name="foreign">A scope the route does not require.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The rows above prove each surface requires SOMETHING; these prove it requires ITS OWN thing. An
    /// implementation that registered one policy and named it three times would pass every row above and
    /// fail every row here - and it is a plausible mistake, because all three policies are built by the
    /// same loop over the same declared list.
    /// </remarks>
    [Theory]
    [InlineData(PingRoute, GatewayScopes.DataWindow)]
    [InlineData(CapabilitiesRoute, GatewayScopes.Ping)]
    [InlineData(DataWindowRoute, GatewayScopes.Capabilities)]
    public async Task OneSurfacesScopeDoesNotOpenAnothersAsync(string route, string foreign)
    {
        await using GatewayTestHostFixture host = new();

        Assert.Equal(HttpStatusCode.Forbidden, await GetAsync(host, route, [foreign]));
    }

    // ==============================================================================================
    //  2. THE SURFACES THAT ARE DELIBERATELY NOT GATED.
    // ==============================================================================================

    /// <summary>Readiness stays anonymous, and therefore ungated.</summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// C-10 requires it and three services gate their own readiness on it, so a scope requirement leaking
    /// onto this route would take a deployment down while every service behaved exactly as written. The
    /// request carries no credential at all, which is how the readiness chain actually probes it.
    /// </remarks>
    [Fact]
    public async Task ReadinessRemainsAnonymousAsync()
    {
        await using GatewayTestHostFixture host = new();
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/health", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// The reserved deferred routes are authenticated, carry NO scope requirement, and still answer 501.
    /// </summary>
    /// <param name="route">The reserved route.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// A DECISION ASSERTED, NOT AN OMISSION LEFT UNTESTED. These routes reach no capability by
    /// construction - each answers 501 and names the deferred service that will eventually serve it - so a
    /// scope for them would be a permission over a feature that does not exist. Requiring one would
    /// replace the published 501 with a 403 and hide the shape of the eventual system that these routes
    /// exist to publish, which is the one thing C-D permits them to do.
    /// </para>
    /// <para>
    /// The token carries NO scope, so the row is specifically the assertion that no scope is required; and
    /// the 501 is asserted rather than merely NOT-403, because a route that answered 404 would also pass a
    /// negative check while meaning the reservation had been lost.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("/v1/design/anything")]
    [InlineData("/v1/documents/anything")]
    [InlineData("/v1/integration/anything")]
    [InlineData("/v1/scripting/anything")]
    public async Task TheReservedRoutesRequireNoScopeAndStillAnswerNotImplementedAsync(string route)
    {
        await using GatewayTestHostFixture host = new();

        using HttpClient client = host.CreateClient();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            GatewayTestHostFixture.BearerScheme,
            host.IssueTokenWithScopes([]));

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(route, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
    }

    /// <summary>
    /// A reserved route with NO credential is still Unauthorized rather than 501.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The complement of the row above: "no scope required" must not have become "no credential required".
    /// A reserved route that answered 501 anonymously would be an unauthenticated surface, which C-G
    /// forbids, and the 501 would make it look intentional.
    /// </remarks>
    [Fact]
    public async Task AReservedRouteWithNoCredentialIsUnauthorizedAsync()
    {
        await using GatewayTestHostFixture host = new();
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/v1/design/anything", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// A gated surface with NO credential is Unauthorized, not Forbidden.
    /// </summary>
    /// <param name="route">The route.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// A named policy REPLACES the fallback for the route naming it, so a scope policy registered without
    /// <c>RequireAuthenticatedUser</c> would answer 403 here - which is exactly the confusion the published
    /// contract says the two statuses exist to prevent, produced by the code that publishes it.
    /// </remarks>
    [Theory]
    [InlineData(PingRoute)]
    [InlineData(CapabilitiesRoute)]
    [InlineData(DataWindowRoute)]
    public async Task AGatedSurfaceWithNoCredentialIsUnauthorizedAsync(string route)
    {
        await using GatewayTestHostFixture host = new();
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(route, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ==============================================================================================
    //  3. THE ROUTE TABLE, THE REGISTRATION AND THE HANDLER.
    // ==============================================================================================

    /// <summary>
    /// Every projected DataWindow route names the DataWindow policy, the expression ones included.
    /// </summary>
    /// <remarks>
    /// The boundary rows probe two routes; this asserts the requirement reaches all of them, which is the
    /// property the group-level placement exists to guarantee and which a per-route override would
    /// silently break. Here the nesting of the expression group is WANTED - both sub-surfaces take one
    /// ingress scope - which is the deliberate opposite of DataServices, where the same nesting had to be
    /// undone because its two contracts take independent scopes.
    /// </remarks>
    [Fact]
    public void EveryProjectedRouteNamesTheDataWindowPolicy()
    {
        List<RouteEndpoint> mapped = EndpointsUnder("/v1/datawindow");

        Assert.NotEmpty(mapped);
        Assert.Contains(
            mapped,
            endpoint => endpoint.RoutePattern.RawText!.StartsWith(
                "/v1/datawindow/expression",
                StringComparison.Ordinal));

        Assert.All(mapped, endpoint =>
            Assert.Contains(
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                data => string.Equals(
                    data.Policy,
                    GatewayScopes.DataWindow,
                    StringComparison.Ordinal)));
    }

    /// <summary>
    /// No reserved route names a scope policy, and every one of them still requires authorization.
    /// </summary>
    /// <param name="prefix">The reserved route prefix.</param>
    /// <remarks>
    /// Both halves in one row, because either alone is misleading: authorization without a scope policy is
    /// the intended posture, no authorization at all would be a C-G violation, and a scope policy would be
    /// a permission over nothing.
    /// </remarks>
    [Theory]
    [InlineData("/v1/design")]
    [InlineData("/v1/documents")]
    [InlineData("/v1/integration")]
    [InlineData("/v1/scripting")]
    public void NoReservedRouteNamesAScopePolicy(string prefix)
    {
        List<RouteEndpoint> mapped = EndpointsUnder(prefix);

        Assert.NotEmpty(mapped);

        Assert.All(mapped, endpoint =>
        {
            IReadOnlyList<IAuthorizeData> authorization =
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();

            Assert.NotEmpty(authorization);

            Assert.DoesNotContain(
                authorization,
                data => data.Policy is string policy
                    && GatewayScopes.All.Contains(policy, StringComparer.Ordinal));
        });
    }

    /// <summary>
    /// Every declared scope is registered as a policy demanding that scope AND authentication.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// Read back from the provider, so a scope named by a route but never registered fails here with a
    /// clear cause rather than as an unexplained internal error at request time. The authentication
    /// requirement is asserted too, because it is the part the 401-versus-403 rows depend on.
    /// </remarks>
    [Fact]
    public async Task EveryDeclaredScopeIsRegisteredAsAPolicyAsync()
    {
        await using GatewayTestHostFixture host = new();

        IAuthorizationPolicyProvider provider = host.Services
            .GetRequiredService<IAuthorizationPolicyProvider>();

        Assert.Equal(3, GatewayScopes.All.Count);

        foreach (string scope in GatewayScopes.All)
        {
            AuthorizationPolicy? policy = await provider.GetPolicyAsync(scope);

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

    /// <summary>The handler grants a scope carried inside a space-delimited claim, at any position.</summary>
    /// <param name="claim">The claim value.</param>
    /// <param name="scope">The scope required.</param>
    /// <param name="expected">Whether the requirement should be satisfied.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The end-to-end suite sends all three ingress scopes in one credential, so the multi-entry rows are
    /// the ordinary case rather than an edge one - a service using the framework's own whole-value claim
    /// requirement would refuse every real caller. The prefix rows matter because `ping` is a short name
    /// that could be found inside a longer one by a substring comparison.
    /// </remarks>
    [Theory]
    [InlineData("ping", GatewayScopes.Ping, true)]
    [InlineData("ping capabilities datawindow", GatewayScopes.Ping, true)]
    [InlineData("ping capabilities datawindow", GatewayScopes.Capabilities, true)]
    [InlineData("ping capabilities datawindow", GatewayScopes.DataWindow, true)]
    [InlineData("datawindow capabilities ping", GatewayScopes.Ping, true)]
    [InlineData("  ping   datawindow  ", GatewayScopes.DataWindow, true)]
    [InlineData("capabilities", GatewayScopes.Ping, false)]
    [InlineData("pinging", GatewayScopes.Ping, false)]
    [InlineData("shipping", GatewayScopes.Ping, false)]
    [InlineData("PING", GatewayScopes.Ping, false)]
    [InlineData("", GatewayScopes.Ping, false)]
    public async Task TheHandlerSplitsTheDelimitedClaim(string claim, string scope, bool expected)
    {
        Assert.Equal(expected, await EvaluateAsync(scope, claim));
    }

    /// <summary>A principal carrying no scope claim at all satisfies nothing.</summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// Asserted separately from the empty-value row, because "the claim is there and says nothing" and
    /// "the claim is not there" reach different branches, and only one is a plausible place to write a
    /// permissive default.
    /// </remarks>
    [Fact]
    public async Task APrincipalWithNoScopeClaimSatisfiesNothingAsync()
    {
        foreach (string scope in GatewayScopes.All)
        {
            Assert.False(await EvaluateAsync(scope, claim: null));
        }
    }

    /// <summary>
    /// The verb each probe route is published with, so that no row can reach a route with the wrong one.
    /// </summary>
    /// <param name="route">One of the four probe routes.</param>
    /// <returns>The verb the ingress maps that route with.</returns>
    /// <remarks>
    /// <para>
    /// A DELIBERATE REFUSAL TO GUESS. Every value here was read off the published contract and the mapping
    /// site rather than inferred from a neighbouring route, because the four probes do not share a verb:
    /// two are <c>GET</c>, one is a body-bound <c>POST</c>, and the prefix the fourth sits under publishes
    /// no <c>GET</c> at all. A helper that defaulted to <c>GET</c> would answer <c>405</c> on the mismatched
    /// route, and a 405 is neither of the two statuses these rows exist to tell apart - it is not the
    /// <c>403</c> a refusal row asserts, and it is also not the <c>403</c> a positive row asserts the
    /// absence of, so BOTH rows would report success while testing nothing.
    /// </para>
    /// <para>
    /// Hence the throw rather than a fallback: a route added to a theory row without a verb decision fails
    /// the row loudly, at the one moment somebody is in a position to look the verb up.
    /// </para>
    /// </remarks>
    private static HttpMethod VerbFor(string route) => route switch
    {
        PingRoute or CapabilitiesRoute or DataWindowRoute => HttpMethod.Get,
        ExpressionRoute => HttpMethod.Post,
        _ => throw new InvalidOperationException(
            $"No verb is declared for probe route '{route}'. Read the verb off gateway.v1.yaml and the "
            + "mapping site and add it here; defaulting to GET would answer 405 and make the row pass "
            + "for the wrong reason."),
    };

    /// <summary>Issues a scoped credential, sends the route's own verb, and returns the status.</summary>
    /// <param name="host">The booted host.</param>
    /// <param name="route">The route, query string included.</param>
    /// <param name="scopes">The scopes to stamp.</param>
    /// <returns>The status the pipeline answered.</returns>
    /// <remarks>
    /// The body-bound probe is sent with no body on purpose. Authorization runs BEFORE model binding, so a
    /// refused request never reaches the binder and the missing body cannot mask the <c>403</c>; a request
    /// that passes the gate fails afterwards on the payload, which is still NOT a <c>403</c> and so is
    /// exactly what the positive arm asserts.
    /// </remarks>
    private static async Task<HttpStatusCode> GetAsync(
        GatewayTestHostFixture host,
        string route,
        IReadOnlyList<string> scopes)
    {
        using HttpClient client = host.CreateClient();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            GatewayTestHostFixture.BearerScheme,
            host.IssueTokenWithScopes(scopes));

        using HttpRequestMessage request = new(VerbFor(route), new Uri(route, UriKind.Relative));

        using HttpResponseMessage response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        return response.StatusCode;
    }

    /// <summary>Evaluates the scope handler for one required scope and one claim value.</summary>
    /// <param name="scope">The required scope.</param>
    /// <param name="claim">The claim value, or <see langword="null"/> for no claim at all.</param>
    /// <returns><see langword="true"/> when the requirement was satisfied.</returns>
    /// <remarks>
    /// Drives the real handler through the framework's own context type, so the row asserts what the
    /// framework would conclude rather than what a reimplementation of the comparison would.
    /// </remarks>
    private static async Task<bool> EvaluateAsync(string scope, string? claim)
    {
        ScopeRequirement requirement = new(scope);

        List<Claim> claims = claim is null ? [] : [new Claim("scope", claim)];

        ClaimsPrincipal principal = new(new ClaimsIdentity(claims, authenticationType: "Test"));

        AuthorizationHandlerContext context = new([requirement], principal, resource: null);

        await new ScopeHandler().HandleAsync(context);

        return context.HasSucceeded;
    }

    /// <summary>Reads the booted host's route table for the endpoints under one prefix.</summary>
    /// <param name="prefix">The route prefix, leading slash included.</param>
    /// <returns>The matching endpoints.</returns>
    private static List<RouteEndpoint> EndpointsUnder(string prefix)
    {
        using GatewayTestHostFixture host = new();

        return
        [
            .. host.Services
                .GetRequiredService<EndpointDataSource>()
                .Endpoints
                .OfType<RouteEndpoint>()
                .Where(endpoint => endpoint.RoutePattern.RawText is string text
                    && text.StartsWith(prefix, StringComparison.Ordinal)),
        ];
    }
}
