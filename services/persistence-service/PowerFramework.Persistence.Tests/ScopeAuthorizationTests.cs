// ==================================================================================================
//  ScopeAuthorizationTests.cs - THE READ/WRITE SPLIT IS A BOUNDARY, NOT A COMMENT
//  ------------------------------------------------------------------------------------------------
//  WHAT THESE ROWS PROTECT
//  This service's only caller requests two distinct scopes and documents the split it intends -
//  `persistence.read` for "C-05 and the C-08 reads", `persistence.write` for "C-06, C-07 and the C-08
//  state changes" [DataServices/Clients/PersistenceClient.cs:438,441]. Until the policies these rows
//  exercise existed, nothing in this service read the scope claim: all four contracts were reachable by
//  any token addressed to it, so a credential obtained to RETRIEVE rows could equally update them, run
//  caller-supplied SQL through C-07, and commit or roll back a transaction. The caller's declared split
//  was a comment; these rows are what make it a boundary.
//
//  THE TWO LEVELS THESE ROWS WORK AT, AND WHY BOTH ARE NEEDED
//    1. THE ROUTE TABLE, read from a booted host. Which policy each contract's methods actually name is
//       a property of the composition root, and it is the only level at which a FORGOTTEN attribute is
//       visible - C-08 carries its policy per method, so a method added without one degrades silently to
//       authentication-only and would answer every request it used to refuse.
//    2. THE HANDLER, driven directly. Whether a policy is satisfied by a given claim is a property of
//       the handler, and the case that matters most cannot be reached from the route table at all: a
//       SINGLE claim carrying a SPACE-DELIMITED set, which is exactly what this service's caller sends
//       because it requests both scopes in one token.
//
//  WHY THERE IS NO END-TO-END gRPC ROW HERE
//  A gRPC call over the loopback host would need a full channel, a generated client and a minted token,
//  and it would assert the same two facts these rows already establish separately - which policy the
//  endpoint names, and whether the handler grants it. Persistence has no token issuer of its own (it
//  holds verification material only), so a boundary row would additionally have to mint with material
//  this service is not allowed to hold. The cross-service path is covered where it belongs, in the
//  end-to-end suite.
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

using PowerFramework.Persistence.Authorization;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Each contract names the scope policy its caller's declared split assigns it, and the handler decides
/// that policy the way a space-delimited claim requires.
/// </summary>
public sealed class ScopeAuthorizationTests
{
    /// <summary>
    /// Each of the three single-scope contracts routes every method under its own policy.
    /// </summary>
    /// <param name="prefix">The contract's route prefix, leading slash included.</param>
    /// <param name="expectedPolicy">The policy every method of that contract must name.</param>
    /// <remarks>
    /// <para>
    /// STRONGER THAN "SOME AUTHORIZATION IS PRESENT", WHICH IS THE TEMPTING ASSERTION HERE. A row that
    /// only checks for authorization metadata passes equally against the parameterless requirement that
    /// lets a read-scoped token commit, so it cannot distinguish the two. Naming the
    /// expected policy is what makes the assignment itself the subject.
    /// </para>
    /// <para>
    /// EVERY METHOD IS CHECKED RATHER THAN THE SERVICE, because the requirement is applied at the
    /// mapping site and propagates to the generated per-method endpoints - so "the mapping names the
    /// policy" and "every method carries it" are different claims, and it is the second one that
    /// matters.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("/persistence.v1.QueryService/", PersistenceScopes.Read)]
    [InlineData("/persistence.v1.UpdateService/", PersistenceScopes.Write)]
    [InlineData("/persistence.v1.CommandService/", PersistenceScopes.Write)]
    public void EverySingleScopeContractNamesItsPolicy(string prefix, string expectedPolicy)
    {
        List<RouteEndpoint> mapped = EndpointsUnder(prefix);

        Assert.NotEmpty(mapped);

        Assert.All(mapped, endpoint =>
            Assert.Contains(
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                data => string.Equals(data.Policy, expectedPolicy, StringComparison.Ordinal)));
    }

    /// <summary>
    /// EVERY method of the mixed contract names one of the two policies, and none names neither.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE GUARD FOR THE ONE CONTRACT WHOSE POLICY IS PER METHOD. C-08 divides into observers and
    /// mutators, so it takes no service-wide scope; each method carries its own attribute instead. That
    /// design has exactly one failure mode - a method added or edited without an attribute - and the
    /// failure is SILENT: the method stays authenticated, keeps working for the only caller (which holds
    /// both scopes), and quietly accepts a read-only credential for an operation that commits.
    /// </para>
    /// <para>
    /// The row asserts both halves: that every method names a KNOWN policy, and that the count of
    /// distinct methods is what the contract declares, so a method that vanished from the route table
    /// cannot make this row pass by being absent.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryMethodOfTheMixedContractNamesAKnownPolicy()
    {
        List<RouteEndpoint> mapped = EndpointsUnder("/persistence.v1.TransactionService/");

        // The contract declares thirteen methods; the route table must carry all of them.
        Assert.Equal(13, mapped.Count);

        Assert.All(mapped, endpoint =>
        {
            string[] policies =
            [
                .. endpoint.Metadata
                    .GetOrderedMetadata<IAuthorizeData>()
                    .Select(data => data.Policy)
                    .Where(policy => !string.IsNullOrEmpty(policy))!,
            ];

            Assert.Contains(
                policies,
                policy => PersistenceScopes.All.Contains(policy, StringComparer.Ordinal));
        });
    }

    /// <summary>
    /// The observing methods of the mixed contract are readable, and the mutating ones are not.
    /// </summary>
    /// <param name="method">The gRPC method name.</param>
    /// <param name="expectedPolicy">The policy it must name.</param>
    /// <remarks>
    /// <para>
    /// THE ASSIGNMENT ITSELF, METHOD BY METHOD, because the row above only proves each method names A
    /// known policy - it would pass with every method marked write, which would deny a read-only caller
    /// the ability to ask whether it is connected, and it would pass with every method marked read, which
    /// would let that caller commit.
    /// </para>
    /// <para>
    /// <c>AutoCommit</c> is the row worth reading twice. It REPORTS the commit mode and changes nothing,
    /// so it looks like an observer - and it is deliberately write-side anyway, because it is the paired
    /// accessor of the setter beside it and a caller with no ability to change the commit mode has no
    /// commit mode to consult. Classifying it by its return type rather than its role would be the
    /// plausible mistake, which is why it is pinned.
    /// </para>
    /// <para>
    /// <b><c>BeginSession</c> AND <c>EndSession</c> ARE READ-SIDE, WHICH IS THE OTHER ROW WORTH READING
    /// TWICE.</b> Both change state - a session is leased from the pool, connected, and later released -
    /// so on an observer-versus-mutator reading they would be write. They are read-side because the
    /// CALLER'S credential decides it, and the caller obtains ONE scope per operation: it requests the
    /// read scope alone for session begin and end
    /// [<c>DataServices/Clients/PersistenceClient.cs</c>, <c>BeginSessionAsync</c> and
    /// <c>EndSessionAsync</c>], and write does not imply read anywhere in this system. Requiring write
    /// here would therefore refuse the first call of EVERY retrieval - C-05 has no session to run a query
    /// against until C-08 opens one - which would leave the read scope granting access to nothing
    /// reachable and would take the whole deployment down in the safe direction. The state they change is
    /// also not durable: neither writes a row, neither commits, and a read-scoped credential still cannot
    /// commit, roll back, change the commit mode, clear state or mark the transaction broken.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("GetTransactionData", PersistenceScopes.Read)]
    [InlineData("IsConnected", PersistenceScopes.Read)]
    [InlineData("GetDatabaseType", PersistenceScopes.Read)]
    [InlineData("GetSessionState", PersistenceScopes.Read)]
    [InlineData("GridSyntaxFromSql", PersistenceScopes.Read)]
    [InlineData("BeginSession", PersistenceScopes.Read)]
    [InlineData("EndSession", PersistenceScopes.Read)]
    [InlineData("SetAutoCommit", PersistenceScopes.Write)]
    [InlineData("AutoCommit", PersistenceScopes.Write)]
    [InlineData("Commit", PersistenceScopes.Write)]
    [InlineData("Rollback", PersistenceScopes.Write)]
    [InlineData("ClearState", PersistenceScopes.Write)]
    [InlineData("SetBroken", PersistenceScopes.Write)]
    public void EachMixedContractMethodNamesTheSideItBelongsTo(string method, string expectedPolicy)
    {
        RouteEndpoint endpoint = Assert.Single(
            EndpointsUnder("/persistence.v1.TransactionService/"),
            candidate => candidate.RoutePattern.RawText!.EndsWith(
                "/" + method,
                StringComparison.Ordinal));

        Assert.Contains(
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            data => string.Equals(data.Policy, expectedPolicy, StringComparison.Ordinal));

        // And it does NOT carry the other side's policy. Attributes combine, so a method carrying both
        // would demand both - which for an observer means a read-only caller is refused after all.
        string other = string.Equals(expectedPolicy, PersistenceScopes.Read, StringComparison.Ordinal)
            ? PersistenceScopes.Write
            : PersistenceScopes.Read;

        Assert.DoesNotContain(
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            data => string.Equals(data.Policy, other, StringComparison.Ordinal));
    }

    /// <summary>
    /// Every declared scope is registered as a policy that demands that scope AND authentication.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// The registration is read back from the provider rather than inferred from a call's behaviour, so a
    /// scope named by an endpoint but never registered fails here with a clear cause instead of surfacing
    /// as an unexplained internal error at call time, which is what the framework answers for an
    /// unregistered policy name.
    /// </para>
    /// <para>
    /// THE AUTHENTICATION REQUIREMENT IS ASSERTED TOO, and it is the part a hand-written policy is most
    /// likely to omit. A named policy REPLACES the fallback for the endpoint naming it, so a scope policy
    /// without it would make that method the only one in the service not demanding authentication - and
    /// it would still appear to work, because an anonymous principal carries no scope claim. It would be
    /// refused as PermissionDenied rather than Unauthenticated, which Gateway projects as 403 rather than
    /// the published 401.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task EveryDeclaredScopeIsRegisteredAsAPolicyAsync()
    {
        using CompositionHost host = CompositionHost.Create();

        IAuthorizationPolicyProvider provider = host.Services
            .GetRequiredService<IAuthorizationPolicyProvider>();

        Assert.NotEmpty(PersistenceScopes.All);

        foreach (string scope in PersistenceScopes.All)
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

    /// <summary>
    /// The handler grants a scope carried inside a SPACE-DELIMITED claim, at any position.
    /// </summary>
    /// <param name="claim">The claim value.</param>
    /// <param name="scope">The scope required.</param>
    /// <param name="expected">Whether the requirement should be satisfied.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE REASON A CUSTOM HANDLER EXISTS, AS A MATRIX. The framework's own claim requirement compares
    /// the claim's WHOLE value, so it would refuse every row here whose claim carries more than one
    /// scope - and this service's only caller sends exactly that, because it requests both scopes in one
    /// token and attaches that one credential to every call. The failure would be in the safe direction
    /// and would still take the whole service down.
    /// </para>
    /// <para>
    /// The rows cover both positions, a prefix that must NOT match, a differently cased spelling that
    /// must not match, doubled and trailing delimiters, and an absent claim. The prefix row is the
    /// sharpest: `persistence.read` is a prefix of nothing here, but `persistence.rea` matching would
    /// mean the comparison was not whole-entry, and a scope vocabulary that ever gained a longer name
    /// sharing a prefix would then grant the wrong one.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("persistence.read", PersistenceScopes.Read, true)]
    [InlineData("persistence.read persistence.write", PersistenceScopes.Read, true)]
    [InlineData("persistence.read persistence.write", PersistenceScopes.Write, true)]
    [InlineData("persistence.write persistence.read", PersistenceScopes.Read, true)]
    [InlineData("  persistence.read   persistence.write  ", PersistenceScopes.Write, true)]
    [InlineData("persistence.read", PersistenceScopes.Write, false)]
    [InlineData("persistence.rea", PersistenceScopes.Read, false)]
    [InlineData("persistence.reads", PersistenceScopes.Read, false)]
    [InlineData("PERSISTENCE.READ", PersistenceScopes.Read, false)]
    [InlineData("", PersistenceScopes.Read, false)]
    public async Task TheHandlerSplitsTheDelimitedClaim(string claim, string scope, bool expected)
    {
        Assert.Equal(expected, await EvaluateAsync(scope, claim));
    }

    /// <summary>A principal carrying NO scope claim at all satisfies nothing.</summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// Every token this system issues carries the claim, so an absent one means a token from another
    /// issuer or a claim dropped in transit - neither a reason to grant. Asserted separately from the
    /// empty-value row above, because "the claim is there and says nothing" and "the claim is not there"
    /// reach different branches and only one of them is a plausible place to write a permissive default.
    /// </remarks>
    [Fact]
    public async Task APrincipalWithNoScopeClaimSatisfiesNothingAsync()
    {
        Assert.False(await EvaluateAsync(PersistenceScopes.Read, claim: null));
        Assert.False(await EvaluateAsync(PersistenceScopes.Write, claim: null));
    }

    /// <summary>
    /// The two scopes are independent: neither implies the other.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// A write-implies-read hierarchy is the obvious convenience and it is deliberately absent: it would
    /// be a policy of this service's own invention, silently widening every token that carries the write
    /// scope, and the caller already requests both explicitly so nothing needs it. Asserted in both
    /// directions so the absence is a decision on the record rather than an oversight.
    /// </remarks>
    [Fact]
    public async Task NeitherScopeImpliesTheOtherAsync()
    {
        Assert.False(await EvaluateAsync(PersistenceScopes.Read, PersistenceScopes.Write));
        Assert.False(await EvaluateAsync(PersistenceScopes.Write, PersistenceScopes.Read));
    }

    // ==============================================================================================
    //  THE BOUNDARY ITSELF. A REAL CALL, A REAL TOKEN, THE REAL PIPELINE.
    // ==============================================================================================

    /// <summary>
    /// A read-scoped credential reaches C-05 and is refused by C-06, C-07 and the write half of C-08.
    /// </summary>
    /// <param name="path">The gRPC method path.</param>
    /// <param name="expected">The status the read-only credential should receive.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE ROW THE WHOLE FINDING REDUCES TO, AND IT IS DELIBERATELY NOT A METADATA ASSERTION. The
    /// metadata rows above prove which policy each endpoint NAMES; this proves what the running pipeline
    /// DOES with a credential that carries one scope and not the other - which is a different claim, and
    /// the only one a caller can observe. Before the policies existed every row here answered something
    /// other than a permission refusal.
    /// </para>
    /// <para>
    /// THE STATUS IS READ FROM THE HTTP RESPONSE RATHER THAN FROM A gRPC CLIENT, and that is what makes
    /// the row cheap enough to run per method. Authorization is evaluated by the routing pipeline before
    /// any gRPC handler is entered, so a refusal is an HTTP status - 403 for an insufficient credential -
    /// and no channel, generated client or valid protobuf payload is needed to observe it. A request that
    /// PASSES authorization is a different matter: it reaches the handler, which then rejects the empty
    /// body, so the accepted arm asserts only that the answer is NOT the refusal. That asymmetry is the
    /// point - it is what proves the 403 came from the policy rather than from the payload.
    /// </para>
    /// <para>
    /// The C-08 rows are the sharpest: one method of the same service is expected to accept the
    /// credential and another to refuse it, so no service-wide explanation can account for both.
    /// </para>
    /// <para>
    /// <c>BeginSession</c> AND <c>EndSession</c> ARE ACCEPTING ROWS, and they are the two the read scope
    /// would be useless without: C-05 has no session to run a query against until C-08 opens one, and the
    /// caller obtains the read scope ALONE for both of those calls. A deployment that required write there
    /// would refuse the first call of every retrieval, in the safe direction and completely. Their sibling
    /// <c>SetBroken</c> refuses, so this pair is not evidence of a service-wide relaxation.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("/persistence.v1.QueryService/Count", false)]
    [InlineData("/persistence.v1.QueryService/CreateQueryTask", false)]
    [InlineData("/persistence.v1.UpdateService/Update", true)]
    [InlineData("/persistence.v1.CommandService/Exec", true)]
    [InlineData("/persistence.v1.TransactionService/IsConnected", false)]
    [InlineData("/persistence.v1.TransactionService/GetSessionState", false)]
    [InlineData("/persistence.v1.TransactionService/Commit", true)]
    [InlineData("/persistence.v1.TransactionService/SetBroken", true)]
    [InlineData("/persistence.v1.TransactionService/BeginSession", false)]
    [InlineData("/persistence.v1.TransactionService/EndSession", false)]
    public async Task AReadScopedCredentialIsRefusedByEveryWriteSideMethodAsync(string path, bool expected)
    {
        using CompositionHost host = CompositionHost.Create();

        HttpStatusCode status = await CallAsync(host, path, [PersistenceScopes.Read]);

        Assert.Equal(expected, status == HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// A write-scoped credential is refused by the read-side methods, which is the other direction.
    /// </summary>
    /// <param name="path">The gRPC method path.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE ROW THAT PROVES THERE IS NO IMPLICIT HIERARCHY. A write-implies-read convenience is the obvious
    /// thing to add and is deliberately absent, so a credential carrying only the write scope cannot
    /// retrieve. Without this row an implementation that quietly granted read to every write-scoped token
    /// would pass every other row in this file, and the scope split would be half a boundary.
    /// </remarks>
    [Theory]
    [InlineData("/persistence.v1.QueryService/Count")]
    [InlineData("/persistence.v1.TransactionService/IsConnected")]
    public async Task AWriteScopedCredentialIsRefusedByTheReadSideAsync(string path)
    {
        using CompositionHost host = CompositionHost.Create();

        Assert.Equal(
            HttpStatusCode.Forbidden,
            await CallAsync(host, path, [PersistenceScopes.Write]));
    }

    /// <summary>
    /// The credential the only real caller sends - both scopes in one claim - is refused nowhere.
    /// </summary>
    /// <param name="path">The gRPC method path.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE POSITIVE ARM, AND THE ONE THAT WOULD HAVE CAUGHT THE WHOLE-VALUE COMPARISON. DataServices
    /// requests both scopes together and attaches that one credential to every call, so its token's claim
    /// is a single space-delimited value. A service using the framework's own claim requirement would
    /// refuse this token at every method - the failure would be in the safe direction and would take the
    /// entire deployment down - and this row is where that shows up as a test failure rather than as a
    /// bring-up that never serves a request.
    /// </remarks>
    [Theory]
    [InlineData("/persistence.v1.QueryService/Count")]
    [InlineData("/persistence.v1.UpdateService/Update")]
    [InlineData("/persistence.v1.CommandService/Exec")]
    [InlineData("/persistence.v1.TransactionService/IsConnected")]
    [InlineData("/persistence.v1.TransactionService/Commit")]
    public async Task TheCredentialTheRealCallerSendsIsAcceptedEverywhereAsync(string path)
    {
        using CompositionHost host = CompositionHost.Create();

        Assert.NotEqual(
            HttpStatusCode.Forbidden,
            await CallAsync(host, path, scopes: null));
    }

    /// <summary>
    /// A call with NO credential is still Unauthorized, not Forbidden.
    /// </summary>
    /// <param name="path">The gRPC method path.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The regression this guards is specific: a named policy REPLACES the fallback for the endpoint that
    /// names it, so a scope policy registered without <c>RequireAuthenticatedUser</c> would answer 403
    /// here - telling a caller with no credential at all that its credential was merely insufficient, and
    /// leaving the published 401 unreachable on the four contracts this service exists to serve.
    /// </remarks>
    [Theory]
    [InlineData("/persistence.v1.QueryService/Count")]
    [InlineData("/persistence.v1.UpdateService/Update")]
    [InlineData("/persistence.v1.TransactionService/Commit")]
    public async Task ACallWithNoCredentialIsStillUnauthorizedAsync(string path)
    {
        using CompositionHost host = CompositionHost.Create();
        using HttpClient client = host.CreateClient();

        using HttpRequestMessage request = new(HttpMethod.Post, path)
        {
            Content = new ByteArrayContent([]),
        };

        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");

        using HttpResponseMessage response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Posts an empty gRPC frame to one method with a credential carrying given scopes.</summary>
    /// <param name="host">The booted host.</param>
    /// <param name="path">The gRPC method path.</param>
    /// <param name="scopes">The scopes to stamp, or <see langword="null"/> for both.</param>
    /// <returns>The HTTP status the pipeline answered.</returns>
    /// <remarks>
    /// The body is empty on purpose: authorization runs before the gRPC handler, so a refusal is decided
    /// without one - and an accepted call reaching the handler then fails on the payload, which is exactly
    /// the asymmetry the accepted arm relies on.
    /// </remarks>
    private static async Task<HttpStatusCode> CallAsync(
        CompositionHost host,
        string path,
        IReadOnlyList<string>? scopes)
    {
        using HttpClient client = host.CreateClient();

        using HttpRequestMessage request = new(HttpMethod.Post, path)
        {
            Content = new ByteArrayContent([]),
        };

        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            host.MintToken(scopes));

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
        using CompositionHost host = CompositionHost.Create();

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
