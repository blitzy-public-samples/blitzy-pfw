// ==================================================================================================
//  ScopeAuthorizationTests.cs - GATEWAY'S PUBLISHED 403 IS A PROJECTION OF A STATUS THIS SERVICE
//  MUST ACTUALLY PRODUCE
//  ------------------------------------------------------------------------------------------------
//  WHAT THESE ROWS PROTECT
//  Gateway's published contract declares a `403` on all thirty-nine of its `/v1/datawindow/**`
//  operations, and its shared response component says what that status means: "The projected gRPC method
//  returned `PermissionDenied`. The token is valid but does not carry the scope this operation requires."
//  That is a promise about THIS service. Until the policies these rows exercise existed, nothing here
//  read the scope claim - C-03 and C-04 were both reachable by any token addressed to this service, this
//  service never returned `PermissionDenied`, and the 403 Gateway publishes was unreachable through the
//  entire chain. The claim was minted, projected and never checked.
//
//  WHY TWO SCOPES RATHER THAN ONE
//  C-04 is a SEPARATE gRPC service from C-03 so the expansion engine can version independently of the
//  event chain [AAP 0.4.3], and the caller requests a separate scope for each
//  [Gateway/Clients/DataServicesClient.cs:1286,1289]. One scope covering both would undo that
//  independence at the authorization layer: a deployment could not grant the event chain without also
//  granting the expression engine. Several rows below exist only to pin that the two are independent in
//  BOTH directions.
//
//  THE STRUCTURAL CHANGE THESE ROWS GUARD
//  The REST projection's expression group used to be declared ON the DataWindow group. That produced the
//  right route prefix and the wrong authorization: group conventions are INHERITED AND CANNOT BE REMOVED,
//  so a nested expression group would demand the C-03 scope in addition to its own, and a caller holding
//  only the C-04 scope would be refused the very contract it was granted. The two groups are now siblings
//  with an explicitly composed prefix - identical URLs, independent policies - and the rows below assert
//  both halves of that: the URLs did not move, and the policies are separate.
//
//  RULES
//  review_rules reports that no user rules were provided, so no user-specified rule governs this file.
// ==================================================================================================

using System.Net;
using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using PowerFramework.DataServices.Authorization;

using Xunit;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// Each contract names its own scope policy, the two scopes are independent, and the projection's URLs
/// are unchanged by the regrouping that made them independent.
/// </summary>
public sealed class ScopeAuthorizationTests
{
    /// <summary>A projected C-03 operation, used as the DataWindow-side probe.</summary>
    /// <remarks>
    /// CHOSEN BECAUSE THE PROJECTION MAPS IT AS A POST, which every projected operation is: the projection
    /// is a thin internal surface consumed only by Gateway, so it renders every unary and server-streaming
    /// method as a POST regardless of how Gateway's own published document exposes the same capability -
    /// <c>event-gate</c>, for instance, is a GET at the ingress and a POST here. A probe route whose verb
    /// this file guessed would answer 405, which is neither the refusal nor the acceptance these rows
    /// distinguish, and every one of them would have passed or failed for the wrong reason.
    /// </remarks>
    private const string DataWindowRoute = "/v1/datawindow/update";

    /// <summary>A projected C-04 operation, used as the expression-side probe.</summary>
    /// <remarks>
    /// UNDER THE COMPOSED PREFIX, so this constant is also the assertion that the regrouping preserved the
    /// route: if the expression group's prefix had changed, every row naming this path would answer 404
    /// rather than a permission verdict.
    /// </remarks>
    private const string ExpressionRoute = "/v1/datawindow/expression/state";

    // ==============================================================================================
    //  1. THE ROUTE TABLE. WHICH POLICY EACH SURFACE NAMES.
    // ==============================================================================================

    /// <summary>Each gRPC contract routes every one of its methods under its own scope policy.</summary>
    /// <param name="prefix">The contract's route prefix, leading slash included.</param>
    /// <param name="expectedPolicy">The policy every method of that contract must name.</param>
    /// <remarks>
    /// STRONGER THAN "SOME AUTHORIZATION IS PRESENT". A row that only checked for authorization metadata
    /// passed equally against the parameterless requirement that let an expression-scoped token drive the
    /// whole event chain, so it could not distinguish the defect from the fix.
    /// </remarks>
    [Theory]
    [InlineData("/dataservices.v1.DataWindowService/", DataServicesScopes.DataWindow)]
    [InlineData("/dataservices.v1.ColumnExpressionService/", DataServicesScopes.ColumnExpression)]
    public void EveryGrpcContractNamesItsOwnPolicy(string prefix, string expectedPolicy)
    {
        List<RouteEndpoint> mapped = EndpointsUnder(prefix);

        Assert.NotEmpty(mapped);

        Assert.All(mapped, endpoint =>
            Assert.Contains(
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                data => string.Equals(data.Policy, expectedPolicy, StringComparison.Ordinal)));
    }

    /// <summary>
    /// A gRPC contract's methods do NOT carry the other contract's policy.
    /// </summary>
    /// <param name="prefix">The contract's route prefix.</param>
    /// <param name="foreignPolicy">The policy the contract must NOT name.</param>
    /// <remarks>
    /// The complement of the row above, and it is the one that catches the plausible mistake rather than
    /// the obvious one: authorization metadata COMBINES, so an endpoint carrying both policies demands
    /// both - which would mean a caller granted exactly one contract is refused it. The positive row
    /// cannot see that, because the expected policy is present either way.
    /// </remarks>
    [Theory]
    [InlineData("/dataservices.v1.DataWindowService/", DataServicesScopes.ColumnExpression)]
    [InlineData("/dataservices.v1.ColumnExpressionService/", DataServicesScopes.DataWindow)]
    public void NoGrpcContractCarriesTheOtherContractsPolicy(string prefix, string foreignPolicy)
    {
        Assert.All(EndpointsUnder(prefix), endpoint =>
            Assert.DoesNotContain(
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                data => string.Equals(data.Policy, foreignPolicy, StringComparison.Ordinal)));
    }

    /// <summary>
    /// The projected expression operations name the C-04 policy and NOT the C-03 one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE REGRESSION ROW FOR THE REGROUPING, AND THE REASON IT WAS NEEDED. While the expression group was
    /// declared ON the DataWindow group it inherited that group's policy, and a group convention cannot be
    /// removed further down - so every projected expression operation demanded the C-03 scope as well as
    /// its own, and a caller granted only C-04 could not reach the contract it held. Re-nesting it would
    /// reintroduce that silently: the URLs would be identical and only the permission would change.
    /// </para>
    /// <para>
    /// It asserts the route set is non-empty first, so a regrouping that lost the operations altogether
    /// cannot make the row pass by having nothing to check.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheProjectedExpressionOperationsNameOnlyTheExpressionPolicy()
    {
        List<RouteEndpoint> mapped = EndpointsUnder("/v1/datawindow/expression");

        Assert.NotEmpty(mapped);

        Assert.All(mapped, endpoint =>
        {
            IReadOnlyList<IAuthorizeData> authorization =
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();

            Assert.Contains(
                authorization,
                data => string.Equals(
                    data.Policy,
                    DataServicesScopes.ColumnExpression,
                    StringComparison.Ordinal));

            Assert.DoesNotContain(
                authorization,
                data => string.Equals(
                    data.Policy,
                    DataServicesScopes.DataWindow,
                    StringComparison.Ordinal));
        });
    }

    /// <summary>
    /// The projected DataWindow operations OUTSIDE the expression prefix name the C-03 policy.
    /// </summary>
    /// <remarks>
    /// The expression routes are excluded by prefix rather than by count, so the row keeps working when
    /// either group gains an operation. Without the exclusion the row would be asserting the opposite of
    /// the one above for the same endpoints.
    /// </remarks>
    [Fact]
    public void TheProjectedDataWindowOperationsNameTheDataWindowPolicy()
    {
        List<RouteEndpoint> mapped =
        [
            .. EndpointsUnder("/v1/datawindow")
                .Where(endpoint => !endpoint.RoutePattern.RawText!.StartsWith(
                    "/v1/datawindow/expression",
                    StringComparison.Ordinal)),
        ];

        Assert.NotEmpty(mapped);

        Assert.All(mapped, endpoint =>
            Assert.Contains(
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                data => string.Equals(
                    data.Policy,
                    DataServicesScopes.DataWindow,
                    StringComparison.Ordinal)));
    }

    /// <summary>
    /// The two probe routes this file uses are really routed, so a permission row cannot pass on a 404.
    /// </summary>
    /// <remarks>
    /// Cheap, and it removes the one way every boundary row below could be vacuous: an unrouted path
    /// answers 404, which is neither 403 nor 200, so a row asserting "not forbidden" would pass against a
    /// route that does not exist. It is also the standing assertion that the regrouping did not move a URL.
    /// </remarks>
    [Fact]
    public void BothProbeRoutesAreRouted()
    {
        string[] routed =
        [
            .. EndpointsUnder("/v1/datawindow").Select(endpoint => endpoint.RoutePattern.RawText!),
        ];

        Assert.Contains(DataWindowRoute, routed, StringComparer.Ordinal);
        Assert.Contains(ExpressionRoute, routed, StringComparer.Ordinal);
    }

    // ==============================================================================================
    //  2. THE BOUNDARY. A REAL REQUEST THROUGH THE REAL PIPELINE.
    // ==============================================================================================

    /// <summary>
    /// An authenticated principal with NO scope is refused by both projected surfaces.
    /// </summary>
    /// <param name="route">The projected route.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The principal is authenticated - the fixture's own Posture B - so the refusal is attributable to
    /// the scope and to nothing else. Before the policies existed this request reached the handler.
    /// </remarks>
    [Theory]
    [InlineData(DataWindowRoute)]
    [InlineData(ExpressionRoute)]
    public async Task AnUnscopedPrincipalIsRefusedByBothProjectionsAsync(string route)
    {
        await using DataServicesTestHostFactory factory = new();

        // AN EMPTY SCOPE SET IS ASKED FOR RATHER THAN INHERITED, and that is a correction. The fixture's
        // plain authenticated client is deliberately SUFFICIENT: it claims Gateway's identity - the one
        // entry in the shipped permitted-caller roster - and carries both published scopes, because that is
        // the credential Gateway really sends and every row that needs to get PAST the gate needs it. A row
        // whose subject is an unscoped principal must therefore declare one, or it is asserting a fixture
        // default rather than a policy.
        using HttpClient client = factory.CreateScopedClient([]);

        using HttpResponseMessage response = await client.PostAsync(
            new Uri(route, UriKind.Relative),
            EmptyJson(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Each scope reaches its OWN projected surface and is refused by the other.
    /// </summary>
    /// <param name="scope">The single scope the principal holds.</param>
    /// <param name="reachable">The route that scope should reach.</param>
    /// <param name="refused">The route it should be refused.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE INDEPENDENCE OF THE TWO CONTRACTS, OBSERVED FROM OUTSIDE. Each row asserts a pair of outcomes
    /// from ONE principal, so no single-scope explanation accounts for both: an implementation that
    /// granted everything fails the refused half, and one that granted nothing fails the reachable half.
    /// </para>
    /// <para>
    /// The reachable half asserts NOT-403 rather than 200, deliberately. Authorization runs before model
    /// binding, so a request that PASSES the gate reaches the handler and is then rejected on its empty
    /// body - and that asymmetry is exactly what proves the 403 in the other half came from the policy
    /// rather than from the payload.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(DataServicesScopes.DataWindow, DataWindowRoute, ExpressionRoute)]
    [InlineData(DataServicesScopes.ColumnExpression, ExpressionRoute, DataWindowRoute)]
    public async Task EachScopeReachesItsOwnSurfaceAndIsRefusedTheOtherAsync(
        string scope,
        string reachable,
        string refused)
    {
        await using DataServicesTestHostFactory factory = new();
        using HttpClient client = factory.CreateScopedClient([scope]);

        using HttpResponseMessage allowed = await client.PostAsync(
            new Uri(reachable, UriKind.Relative),
            EmptyJson(),
            TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.Forbidden, allowed.StatusCode);

        using HttpResponseMessage denied = await client.PostAsync(
            new Uri(refused, UriKind.Relative),
            EmptyJson(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    /// <summary>
    /// The credential Gateway actually sends - both scopes in ONE claim - is refused nowhere.
    /// </summary>
    /// <param name="route">The projected route.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE ROW THAT WOULD HAVE CAUGHT THE WHOLE-VALUE COMPARISON. Gateway requests both scopes together
    /// and attaches that one credential to every call, so its token's claim is a single space-delimited
    /// value. A service using the framework's own claim requirement would refuse this at every operation -
    /// a failure in the safe direction that would nonetheless mean this service served nothing at all -
    /// and this is where that shows up as a test failure rather than as a bring-up that never works.
    /// </remarks>
    [Theory]
    [InlineData(DataWindowRoute)]
    [InlineData(ExpressionRoute)]
    public async Task TheCredentialGatewaySendsIsAcceptedEverywhereAsync(string route)
    {
        await using DataServicesTestHostFactory factory = new();
        using HttpClient client = factory.CreateScopedClient(
            [DataServicesScopes.DataWindow, DataServicesScopes.ColumnExpression]);

        using HttpResponseMessage response = await client.PostAsync(
            new Uri(route, UriKind.Relative),
            EmptyJson(),
            TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// A request with no principal at all is still Unauthorized, not Forbidden.
    /// </summary>
    /// <param name="route">The projected route.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// A named policy REPLACES the fallback for the endpoint naming it, so a scope policy registered
    /// without <c>RequireAuthenticatedUser</c> would answer 403 here - telling a caller with no credential
    /// that its credential was merely insufficient, and leaving Gateway to project that as a 403 where its
    /// own contract publishes a 401.
    /// </remarks>
    [Theory]
    [InlineData(DataWindowRoute)]
    [InlineData(ExpressionRoute)]
    public async Task ARequestWithNoPrincipalIsStillUnauthorizedAsync(string route)
    {
        await using DataServicesTestHostFactory factory = new();
        using HttpClient client = factory.CreateAnonymousClient();

        using HttpResponseMessage response = await client.PostAsync(
            new Uri(route, UriKind.Relative),
            EmptyJson(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// The anonymous readiness probe and the authenticated ping are unaffected by the scope gate.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// Two decisions asserted rather than left untested. <c>/health</c> must stay anonymous because
    /// Gateway gates its own readiness on it, and a scope requirement leaking onto it would take the
    /// deployment down while every service behaved exactly as written. <c>/v1/ping</c> is authenticated
    /// and deliberately requires NO scope: it performs no work and reaches no capability, so a scope for
    /// it would be a permission over nothing.
    /// </para>
    /// <para>
    /// The ping arm uses the UNSCOPED principal on purpose - that is what makes it an assertion about the
    /// absence of a scope requirement rather than about the presence of one.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ReadinessStaysAnonymousAndPingRequiresNoScopeAsync()
    {
        await using DataServicesTestHostFactory factory = new();

        using HttpClient anonymous = factory.CreateAnonymousClient();

        using HttpResponseMessage health = await anonymous.GetAsync(
            new Uri("/health", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        using HttpClient unscoped = factory.CreateAuthenticatedClient();

        using HttpResponseMessage ping = await unscoped.GetAsync(
            new Uri("/v1/ping", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, ping.StatusCode);
    }

    // ==============================================================================================
    //  3. THE REGISTRATION AND THE HANDLER.
    // ==============================================================================================

    /// <summary>
    /// Every declared scope is registered as a policy demanding that scope AND authentication.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The registration is read back from the provider, so a scope named by an endpoint but never
    /// registered fails here with a clear cause instead of surfacing as an unexplained internal error at
    /// call time - which is what the framework answers for an unregistered policy name. The authentication
    /// requirement is asserted too, because it is the part a hand-written policy is most likely to omit
    /// and the part the 401-versus-403 rows above depend on.
    /// </remarks>
    [Fact]
    public async Task EveryDeclaredScopeIsRegisteredAsAPolicyAsync()
    {
        await using DataServicesTestHostFactory factory = new();

        IAuthorizationPolicyProvider provider = factory.Services
            .GetRequiredService<IAuthorizationPolicyProvider>();

        Assert.NotEmpty(DataServicesScopes.All);

        foreach (string scope in DataServicesScopes.All)
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
    /// The rows cover both positions, a prefix that must not match, a longer name sharing a prefix, a
    /// differently cased spelling, doubled and trailing delimiters, and an empty value. The prefix rows
    /// matter here more than elsewhere because these two scope names share the whole
    /// <c>dataservices.</c> stem, so a comparison that was not whole-entry would grant the wrong contract.
    /// </remarks>
    [Theory]
    [InlineData("dataservices.datawindow", DataServicesScopes.DataWindow, true)]
    [InlineData("dataservices.datawindow dataservices.columnexpression", DataServicesScopes.DataWindow, true)]
    [InlineData("dataservices.datawindow dataservices.columnexpression", DataServicesScopes.ColumnExpression, true)]
    [InlineData("dataservices.columnexpression dataservices.datawindow", DataServicesScopes.DataWindow, true)]
    [InlineData("  dataservices.datawindow   dataservices.columnexpression  ", DataServicesScopes.ColumnExpression, true)]
    [InlineData("dataservices.datawindow", DataServicesScopes.ColumnExpression, false)]
    [InlineData("dataservices.", DataServicesScopes.DataWindow, false)]
    [InlineData("dataservices.datawindows", DataServicesScopes.DataWindow, false)]
    [InlineData("DATASERVICES.DATAWINDOW", DataServicesScopes.DataWindow, false)]
    [InlineData("", DataServicesScopes.DataWindow, false)]
    public async Task TheHandlerSplitsTheDelimitedClaim(string claim, string scope, bool expected)
    {
        Assert.Equal(expected, await EvaluateAsync(scope, claim));
    }

    /// <summary>A principal carrying no scope claim at all satisfies nothing.</summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// Asserted separately from the empty-value row, because "the claim is there and says nothing" and
    /// "the claim is not there" reach different branches, and only one of them is a plausible place to
    /// write a permissive default.
    /// </remarks>
    [Fact]
    public async Task APrincipalWithNoScopeClaimSatisfiesNothingAsync()
    {
        Assert.False(await EvaluateAsync(DataServicesScopes.DataWindow, claim: null));
        Assert.False(await EvaluateAsync(DataServicesScopes.ColumnExpression, claim: null));
    }

    /// <summary>Neither scope implies the other.</summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The contracts are separate so they can version independently, and an implication either way would
    /// undo that at the authorization layer while leaving them looking independent. Asserted in both
    /// directions so the absence is a decision on the record rather than an oversight.
    /// </remarks>
    [Fact]
    public async Task NeitherScopeImpliesTheOtherAsync()
    {
        Assert.False(await EvaluateAsync(
            DataServicesScopes.DataWindow,
            DataServicesScopes.ColumnExpression));

        Assert.False(await EvaluateAsync(
            DataServicesScopes.ColumnExpression,
            DataServicesScopes.DataWindow));
    }

    /// <summary>A body the projected operations will bind but that carries no members.</summary>
    /// <returns>An empty JSON object as content.</returns>
    /// <remarks>
    /// Empty on purpose: authorization is evaluated before model binding, so a refusal is decided without
    /// a valid body - and a request that PASSES the gate reaches the handler and fails there, which is the
    /// asymmetry the accepted arms rely on.
    /// </remarks>
    private static StringContent EmptyJson() => new("{}", System.Text.Encoding.UTF8, "application/json");

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
        using DataServicesTestHostFactory factory = new();

        return
        [
            .. factory.Services
                .GetRequiredService<EndpointDataSource>()
                .Endpoints
                .OfType<RouteEndpoint>()
                .Where(endpoint => endpoint.RoutePattern.RawText is string text
                    && text.StartsWith(prefix, StringComparison.Ordinal)),
        ];
    }
}
