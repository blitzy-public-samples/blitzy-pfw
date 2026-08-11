// ==================================================================================================
//  ReceiverAuthorizationTests - AUTHENTICATION IS NOT AUTHORIZATION, PROVED AT THE RECEIVER
//  ------------------------------------------------------------------------------------------------
//  SUBJECT   The two named authorization policies this service serves its contracts under -
//            CallerAuthorization in Program.cs - as they are actually applied at the gRPC mapping
//            sites and at the two projected REST groups.
//
//  WHY THIS FILE EXISTS
//  ------------------------------------------------------------------------------------------------
//  Every receiver in this service used to be protected by the PARAMETERLESS RequireAuthorization,
//  which requires an authenticated user and nothing else. Combined with an issuer that granted every
//  requested scope to any caller whose certificate chained to the configured authority, that meant:
//
//    - a credential obtained to read a DataWindow could drive the column-expression engine, and
//    - a credential minted for a caller with no business here at all could drive either.
//
//  That is CWE-862 (missing authorization) sitting behind a boundary that looked authenticated, and it
//  is exactly the shape a test suite fails to notice: every existing row passed, because every
//  existing row presented a principal that was merely authenticated and asked for nothing more.
//
//  WHAT THESE ROWS GUARD
//  ------------------------------------------------------------------------------------------------
//    1. ANONYMOUS IS STILL UNAUTHENTICATED, not forbidden. The distinction is published - 401 for a
//       missing credential, 403 for a valid-but-insufficient one - and collapsing it would tell a
//       caller to fix the wrong thing.
//    2. AN AUTHENTICATED CALLER HOLDING NO SCOPE IS REFUSED. This is the row that would have caught
//       the original defect: the principal is genuine and the request is refused anyway.
//    3. THE WRONG SCOPE IS AS INSUFFICIENT AS NONE. A DataWindow credential does not reach the
//       expression surface.
//    4. AN UNPERMITTED CALLER IS REFUSED EVEN HOLDING THE RIGHT SCOPE. Scope without subject would
//       admit any caller the issuer serves; this is the subject half.
//    5. THE PERMITTED CALLER WITH THE RIGHT SCOPE GETS THROUGH. Asserted deliberately alongside the
//       refusals, because a suite that only proved refusals would leave open that everything is
//       refused.
//
//  NO CREDENTIAL APPEARS IN THIS FILE. The fixture establishes a principal through a header whose
//  PRESENCE selects a test authentication scheme; the subject and scope values are permission NAMES,
//  not secrets, and no key, token or signature value appears anywhere here.
// ==================================================================================================

using System.Net;
using System.Net.Http;
using System.Text.Json;
using Grpc.Core;
using PowerFramework.Shared.Kernel;
using Xunit;

// The generated client is reached through an ALIAS rather than a namespace import, on the same reasoning the
// fixture records: PowerFramework.Contracts.DataServices.V1 publishes a `DataWindowService` whose bare name
// collides with this service's own domain vocabulary, so naming exactly the types used keeps the collision
// surface at zero rather than relying on which namespaces this file happens not to import.
using DataWindowContractClient =
    PowerFramework.Contracts.DataServices.V1.DataWindowService.DataWindowServiceClient;
using GetEventGateRequest = PowerFramework.Contracts.DataServices.V1.GetEventGateRequest;

namespace PowerFramework.DataServices.Tests;

public sealed class ReceiverAuthorizationTests(DataServicesTestHostFactory host)
    : IClassFixture<DataServicesTestHostFactory>
{
    /// <summary>
    /// A projected DataWindow route, chosen because it is the shallowest one that reaches the policy.
    /// </summary>
    /// <remarks>
    /// THE POLICY RUNS BEFORE THE HANDLER, so a row asserting a refusal never reaches the upstream and
    /// needs no scripted Persistence answer. The positive row does reach the handler, which is why it
    /// asserts only that the response is NOT a refusal rather than asserting a body: what the handler then
    /// does with an unscripted upstream is a different suite's subject.
    /// </remarks>
    private const string DataWindowRoute = "/v1/datawindow/retrieve";

    /// <summary>A projected expression route, under the nested group carrying the second policy.</summary>
    private const string ExpressionRoute = "/v1/datawindow/expression/enabled";

    // ==============================================================================================
    //  GROUP 1 - the anonymous / insufficient distinction
    // ==============================================================================================

    /// <summary>
    /// An anonymous caller is answered 401 rather than 403 on a projected route.
    /// </summary>
    /// <remarks>
    /// THE TWO REFUSALS MEAN DIFFERENT THINGS AND THE CONTRACT PUBLISHES BOTH. 401 says "you presented no
    /// credential"; 403 says "your credential is valid and insufficient". Collapsing them would send a
    /// caller to fix the wrong thing - obtaining a fresh token when the roster is what needs changing, or
    /// the reverse. This row is what keeps the named policies from turning every refusal into a 403.
    /// </remarks>
    [Fact]
    public async Task AnAnonymousCallerIsUnauthenticatedRatherThanForbidden()
    {
        using HttpClient client = host.CreateAnonymousClient();

        using HttpResponseMessage response = await client.PostAsync(
            new Uri(DataWindowRoute, UriKind.Relative),
            EmptyJson(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// An authenticated caller holding none of this service's scopes is forbidden.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS THE ROW THAT WOULD HAVE CAUGHT THE ORIGINAL DEFECT. The principal is genuine - the same
    /// scheme, the same permitted identity, the same audience - and its scope claim carries nothing this
    /// service serves. Under the parameterless requirement this request succeeded; under the named policy it
    /// is refused, and the refusal is 403 rather than 401 because the credential was established and found
    /// wanting.
    /// </para>
    /// <para>
    /// THE SCOPES PRESENTED ARE REAL RFC 6749 SCOPES THAT THIS SERVICE PUBLISHES NOTHING FOR, rather than an
    /// empty claim. A genuinely empty claim cannot be produced over HTTP - a header whose value is empty is
    /// not transmitted, so the fixture would fall back to its default and the row would silently assert
    /// nothing - and a credential carrying somebody else's scopes is in any case the realistic form of this
    /// fault.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnAuthenticatedCallerHoldingNoneOfThisServicesScopesIsForbidden()
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        client.DefaultRequestHeaders.Add(
            DataServicesTestHostFactory.TestPrincipalScopeHeaderName,
            "openid profile");

        using HttpResponseMessage response = await client.PostAsync(
            new Uri(DataWindowRoute, UriKind.Relative),
            EmptyJson(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ==============================================================================================
    //  GROUP 2 - the scope half
    // ==============================================================================================

    /// <summary>
    /// The DataWindow scope does not reach the expression surface.
    /// </summary>
    /// <remarks>
    /// THE TWO CONTRACTS ARE SEPARATE BY DESIGN AND MUST BE SEPARATE IN PRACTICE. C-04 is kept apart from
    /// C-03 precisely so the expansion engine can version independently, and serving both under one
    /// requirement would make that separation cosmetic. The nested group's policy is ADDITIVE, so a caller
    /// reaching an expression route must satisfy both - which is why the credential Gateway obtains carries
    /// both scopes, and why a credential carrying only one is refused here.
    /// </remarks>
    [Fact]
    public async Task TheDataWindowScopeAloneDoesNotReachTheExpressionSurface()
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        client.DefaultRequestHeaders.Add(
            DataServicesTestHostFactory.TestPrincipalScopeHeaderName,
            "dataservices.datawindow");

        using HttpResponseMessage response = await client.PostAsync(
            new Uri(ExpressionRoute, UriKind.Relative),
            EmptyJson(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// A scope that resembles a published one but is not one is insufficient.
    /// </summary>
    /// <remarks>
    /// COMPARISON IS ORDINAL, WHICH RFC 6749 REQUIRES - scope tokens are case-sensitive. A case-folded
    /// comparison would accept a spelling the issuer never granted, and the difference is invisible in a
    /// log. The value here differs only in case, so the row fails if the comparison is ever relaxed.
    /// </remarks>
    [Fact]
    public async Task AScopeDifferingOnlyInCaseIsInsufficient()
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        client.DefaultRequestHeaders.Add(
            DataServicesTestHostFactory.TestPrincipalScopeHeaderName,
            "DataServices.DataWindow");

        using HttpResponseMessage response = await client.PostAsync(
            new Uri(DataWindowRoute, UriKind.Relative),
            EmptyJson(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// The scope claim is read as a space-delimited SET, not as one opaque value.
    /// </summary>
    /// <remarks>
    /// THE REASON THE POLICY IS AN ASSERTION AND NOT <c>RequireClaim</c>. RFC 6749 carries the granted set
    /// as one claim holding a space-delimited list, so a claim-equality requirement would demand a token
    /// whose ENTIRE scope claim is the single value it wants - and would refuse the very credential Gateway
    /// obtains, which carries both scopes. This row presents both, in the order Gateway requests them, and
    /// requires the second one to be found.
    /// </remarks>
    [Fact]
    public async Task ASpaceDelimitedScopeSetIsSplitRatherThanComparedWhole()
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        client.DefaultRequestHeaders.Add(
            DataServicesTestHostFactory.TestPrincipalScopeHeaderName,
            "dataservices.datawindow dataservices.columnexpression");

        using HttpResponseMessage response = await client.PostAsync(
            new Uri(ExpressionRoute, UriKind.Relative),
            EmptyJson(),
            TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ==============================================================================================
    //  GROUP 3 - the subject half
    // ==============================================================================================

    /// <summary>
    /// A caller that is not on the permitted roster is refused even holding the right scope.
    /// </summary>
    /// <remarks>
    /// SCOPE WITHOUT SUBJECT WOULD ADMIT ANY CALLER THE ISSUER SERVES. The AAP fixes the call graph as
    /// layered and acyclic - nothing but Gateway calls DataServices - and the shipped roster says so. This
    /// row presents a subject that is a real service identity elsewhere in the system, so the refusal is
    /// proved to come from the roster rather than from a shape check a nonsense value would also have
    /// failed.
    /// </remarks>
    [Fact]
    public async Task AnUnpermittedCallerIsForbiddenEvenHoldingTheScope()
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        client.DefaultRequestHeaders.Add(
            DataServicesTestHostFactory.TestPrincipalSubjectHeaderName,
            "powerframework-persistence");

        using HttpResponseMessage response = await client.PostAsync(
            new Uri(DataWindowRoute, UriKind.Relative),
            EmptyJson(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// A subject differing only in case is not the permitted caller.
    /// </summary>
    /// <remarks>
    /// ORDINAL, MATCHING HOW THE ISSUER COMPARES AN IDENTITY EVERYWHERE ELSE - the issuance endpoint
    /// reconciles the declared subject against the certificate's common name ordinally, and the issuer
    /// matches its audience roster the same way. Folding case at this one point would honour an identity no
    /// other part of the system considers established.
    /// </remarks>
    [Fact]
    public async Task ASubjectDifferingOnlyInCaseIsNotThePermittedCaller()
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        client.DefaultRequestHeaders.Add(
            DataServicesTestHostFactory.TestPrincipalSubjectHeaderName,
            "PowerFramework-Gateway");

        using HttpResponseMessage response = await client.PostAsync(
            new Uri(DataWindowRoute, UriKind.Relative),
            EmptyJson(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ==============================================================================================
    //  GROUP 4 - the positive arm
    // ==============================================================================================

    /// <summary>
    /// The permitted caller holding the operation's scope passes the policy.
    /// </summary>
    /// <remarks>
    /// ASSERTED DELIBERATELY ALONGSIDE THE REFUSALS. A suite proving only refusals would leave open the
    /// possibility that the policy refuses everything, which would be a far worse defect than the one it
    /// replaced - a boundary nobody can cross is an outage, and it would present as one only in production.
    /// The default principal is exactly Gateway's credential: its identity, and both published scopes.
    /// </remarks>
    [Fact]
    public async Task ThePermittedCallerHoldingTheScopePassesThePolicy()
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await client.PostAsync(
            new Uri(DataWindowRoute, UriKind.Relative),
            EmptyJson(),
            TestContext.Current.CancellationToken);

        // NOT an assertion about the BODY. The policy runs before the handler, so passing it is what this
        // row exists to prove; what the handler then makes of the request is another suite's subject.
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// The ping route stays authenticated-only, unchanged by the two contract policies.
    /// </summary>
    /// <remarks>
    /// A NEGATIVE THAT MATTERS. The AAP fixes <c>/v1/ping</c> as requiring a token and answering 401
    /// without one, and it publishes no scope for it - so adding a scope requirement there would be
    /// inventing a requirement the contract does not state. This row asserts the named policies were
    /// applied to the two contracts and not to the whole host, which is precisely the kind of over-reach a
    /// fallback-policy edit could introduce silently.
    /// </remarks>
    [Fact]
    public async Task ThePingRouteRemainsAuthenticatedOnly()
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        client.DefaultRequestHeaders.Add(
            DataServicesTestHostFactory.TestPrincipalScopeHeaderName,
            "openid profile");

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/v1/ping", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ==============================================================================================
    //  GROUP 4 - the refusal's BODY, which the published routes declare and the framework does not write
    // ==============================================================================================

    /// <summary>
    /// Every framework-generated failure status carries the published problem body, with both extension
    /// members filled.
    /// </summary>
    /// <param name="anonymous">Whether the request is made without a principal.</param>
    /// <param name="route">The route to request.</param>
    /// <param name="method">The method to request it with.</param>
    /// <param name="expected">The status the framework answers.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE STATUS WAS ALWAYS RIGHT AND THE BODY WAS ALWAYS ABSENT, which is why every row above passed while
    /// the promise went unkept. Each projected route in <c>Endpoints/RestProjectionEndpoints.cs</c> declares
    /// <c>ProducesProblem</c> for 401, 403 and 404 and the host registers the problem-details service to
    /// write them, but nothing in the pipeline asked for a body on a response produced BENEATH this
    /// service's own code - and all four statuses below are produced there: the challenge by authorization,
    /// the refusal by the named policy, an unmatched route and a rejected method by routing.
    /// </para>
    /// <para>
    /// BOTH EXTENSION MEMBERS ARE ASSERTED, because status-code pages alone would produce a body without
    /// them. <c>retCode</c> comes from the composition root's classification of the framework status into the
    /// legacy vocabulary, and <c>traceId</c> is the member through which an operator record for the
    /// occurrence can be found - a body with the right media type and neither member would satisfy a
    /// content-type assertion while telling a caller nothing.
    /// </para>
    /// <para>
    /// The 401 and the 403 keep the same access code, because the legacy algebra declares exactly one and
    /// draws no distinction between "no credential" and "credential without permission". The HTTP statuses
    /// still differ, which is what rows one and two above assert; this row asserts the code does not acquire
    /// a member the oracle never had.
    /// </para>
    /// <para>
    /// THE LAST TWO ROWS ARE ABOUT THE SHARED PORT, and they are the reason the gRPC catch-all route is
    /// switched off in the composition root. That route is two parameter segments wide, so it matched the
    /// two-segment REST paths - <c>/v1/ping</c> and <c>/health</c> - and answered them from the gRPC handler
    /// with a gRPC content type: no problem body, and a rejected method reported as 404 rather than 405. The
    /// method row is what fails if the catch-all comes back, and the unknown-service row prices what
    /// switching it off costs: an unknown service now falls through to routing and answers 404, which every
    /// conforming gRPC client maps onto UNIMPLEMENTED, so the status a gRPC caller surfaces is unchanged.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(true, DataWindowRoute, "POST", HttpStatusCode.Unauthorized, RetCode.E_ACCESS_DENIED)]
    [InlineData(false, DataWindowRoute, "POST", HttpStatusCode.Forbidden, RetCode.E_ACCESS_DENIED)]
    [InlineData(false, "/v1/datawindow/no-such-projection", "POST", HttpStatusCode.NotFound, RetCode.E_OBJECT_NOT_FOUND)]
    [InlineData(false, "/v1/ping", "DELETE", HttpStatusCode.MethodNotAllowed, RetCode.E_NO_SUPPORT)]
    [InlineData(false, "/no.such.Service/Method", "POST", HttpStatusCode.NotFound, RetCode.E_OBJECT_NOT_FOUND)]
    public async Task EveryFrameworkGeneratedRefusalCarriesThePublishedProblemBody(
        bool anonymous,
        string route,
        string method,
        HttpStatusCode expected,
        long expectedRetCode)
    {
        using HttpClient client = anonymous
            ? host.CreateAnonymousClient()
            : host.CreateAuthenticatedClient();

        if (expected == HttpStatusCode.Forbidden)
        {
            // A genuine principal carrying somebody else's scopes, which is the realistic shape of an
            // insufficient credential and the one the row above already relies on.
            client.DefaultRequestHeaders.Add(
                DataServicesTestHostFactory.TestPrincipalScopeHeaderName,
                "openid profile");
        }

        using HttpRequestMessage request = new(new HttpMethod(method), new Uri(route, UriKind.Relative))
        {
            Content = EmptyJson(),
        };

        using HttpResponseMessage response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using JsonDocument body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // The RFC 9457 member, which is the HTTP status as an integer rather than a status word.
        Assert.Equal((int)expected, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(expectedRetCode, body.RootElement.GetProperty("retCode").GetInt64());
        Assert.False(string.IsNullOrEmpty(body.RootElement.GetProperty("traceId").GetString()));
    }

    /// <summary>
    /// An unauthenticated gRPC call is still refused as <c>Unauthenticated</c>, unaffected by the body the
    /// status-code middleware now writes onto its 401.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE ONE WAY THE REST FIX COULD HAVE BROKEN THE PRIMARY TRANSPORT, ASSERTED DIRECTLY. Status-code pages
    /// write a body onto any response that has neither one nor a content type, and an unauthenticated gRPC
    /// call's 401 is exactly such a response - so the refusal a gRPC client sees now arrives carrying
    /// <c>application/problem+json</c>. A client maps the HTTP status before it inspects the content type, so
    /// the observable status is unchanged; that reasoning is worth an assertion rather than a comment,
    /// because if it were wrong every authenticated call in the system would fail at the same moment.
    /// </remarks>
    [Fact]
    public async Task AnUnauthenticatedGrpcCallIsStillUnauthenticated()
    {
        DataWindowContractClient client = new(host.CreateAnonymousGrpcChannel());

        RpcException refused = await Assert.ThrowsAsync<RpcException>(() =>
            client.GetEventGateAsync(
                new GetEventGateRequest(),
                cancellationToken: TestContext.Current.CancellationToken).ResponseAsync);

        Assert.Equal(StatusCode.Unauthenticated, refused.StatusCode);
    }

    /// <summary>
    /// An empty JSON object body, so a POST carries something well-formed rather than nothing.
    /// </summary>
    /// <returns>The request content.</returns>
    /// <remarks>
    /// THE BODY IS DELIBERATELY MINIMAL AND IS NEVER THE SUBJECT. Authorization runs before model binding,
    /// so every refusal row is decided before this content is read at all; it exists so the request is a
    /// well-formed POST rather than one the transport could reject for its own reasons.
    /// </remarks>
    private static StringContent EmptyJson() =>
        new("{}", System.Text.Encoding.UTF8, "application/json");
}
