// ==================================================================================================
//  ErrorContractTests.cs - EVERY REFUSAL CARRIES THE BODY THE PROJECTION PUBLISHES
//  ------------------------------------------------------------------------------------------------
//  WHAT THESE ROWS PROTECT
//  The REST projection declares `application/problem+json` for BOTH refusals on every route it maps -
//  a 401 when no usable token is presented and a 403 when the token carries the wrong scope. The
//  framework's challenge and its forbid each write a bare status with NO BODY, so until the two
//  diagnostics middlewares were installed in the composition root this service advertised a problem
//  document on every refusal and returned nothing at all. A caller written against the generated
//  document had nothing to parse, and the two refusals were indistinguishable from each other by body
//  because neither had one.
//
//  WHY THIS SERVICE NEEDED ITS OWN ROWS
//  Gateway's sibling suite pins the same property at the ingress, and it would be easy to assume the
//  two are the same test written twice. They are not: each service composes its own pipeline, and the
//  middleware that produces these bodies is registered per service. The ingress had one of the two
//  diagnostics components and was missing the other; THIS service had NEITHER, and its pipeline comment
//  named their absence as a deliberate omission - so nothing here would have failed if the fix had been
//  applied only at the ingress, which is exactly the gap a per-service row closes.
//
//  THE gRPC EDGE IS NOT IN SCOPE HERE, AND THAT IS A PROPERTY RATHER THAN AN OMISSION
//  A gRPC refusal travels in trailers with its own status code, not as a status-code body, so neither
//  middleware rewrites anything a gRPC client reads. The rows below therefore address the REST
//  projection only - the surface Gateway consumes and the only surface on this service that publishes a
//  problem schema at all.
//
//  RULES
//  review_rules reports that no user rules were provided, so no user-specified rule governs this file.
// ==================================================================================================

using System.Net;
using System.Net.Mime;
using System.Text.Json;

using Xunit;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// The published refusal bodies are produced, the generated document is not anonymous, and the routes
/// that write their own bodies are untouched by the middleware that supplies the missing ones.
/// </summary>
public sealed class ErrorContractTests
{
    /// <summary>The generated contract document.</summary>
    private const string DocumentRoute = "/openapi/v1.json";

    /// <summary>
    /// A projected operation, reached with the verb the projection maps rather than the verb the ingress
    /// publishes.
    /// </summary>
    /// <remarks>
    /// THE PROJECTION MAPS EVERY OPERATION AS A POST, including the handful the ingress publishes as a
    /// GET. A row that assumed the ingress verb would be answered <c>405 Method Not Allowed</c>, which is
    /// neither of the two refusals these rows tell apart - and worse, a 405 would satisfy an assertion
    /// written as "not a 403", making a positive row pass for the wrong reason.
    /// </remarks>
    private const string ProjectedRoute = "/v1/datawindow/update";

    /// <summary>The anonymous readiness probe, which writes its own body.</summary>
    private const string ReadinessRoute = "/health";

    // ==============================================================================================
    //  1. THE TWO REFUSALS CARRY THE PUBLISHED BODY.
    // ==============================================================================================

    /// <summary>An untokened request is refused with the published problem body.</summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The standing C-G proof for this service, now carrying the content the contract promises. The
    /// status is read out of the body as well as off the response, because a body disagreeing with its
    /// own status line would be worse than no body at all.
    /// </remarks>
    [Fact]
    public async Task AnUntokenedRequestCarriesThePublishedProblemBodyAsync()
    {
        await using DataServicesTestHostFactory host = new();
        using HttpClient client = host.CreateAnonymousClient();

        using HttpResponseMessage response = await client.PostAsync(
            new Uri(ProjectedRoute, UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertProblemBodyAsync(response, HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// A token that authenticates but carries no scope is refused with the published problem body.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE SECOND REFUSAL, AND IT IS A DIFFERENT MIDDLEWARE DECISION FROM THE FIRST. A challenge and a
    /// forbid are produced on different paths - one because no principal was established, the other
    /// because an established principal failed a policy - and a fix that supplied a body for only one of
    /// them would leave this row failing. Asserting both is what makes the pair complete, and it is also
    /// what lets a caller tell a missing credential from an insufficient one, which is the distinction
    /// the published contract says the two statuses exist to draw.
    /// </remarks>
    [Fact]
    public async Task AnUnscopedTokenCarriesThePublishedProblemBodyAsync()
    {
        await using DataServicesTestHostFactory host = new();
        using HttpClient client = host.CreateScopedClient([]);

        using HttpResponseMessage response = await client.PostAsync(
            new Uri(ProjectedRoute, UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertProblemBodyAsync(response, HttpStatusCode.Forbidden);
    }

    /// <summary>Neither refusal body echoes the identity material the request carried.</summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// The rows above establish that a body now exists; this one establishes that supplying it did not
    /// disclose anything. The middleware writes a generic document from the status code, so there is no
    /// mechanism by which it could echo the caller's identity - which is exactly why it is worth pinning:
    /// the property holds by construction today, and a later hand-written handler that "improved" the
    /// message by naming what was wrong with the caller is how it would stop holding.
    /// </para>
    /// <para>
    /// THE MATERIAL CHECKED IS THE PRINCIPAL AND SCOPE HEADERS, NOT A BEARER TOKEN, and that is a property
    /// of the harness rather than a weaker assertion. This fixture MINTS NOTHING AND HOLDS NO KEY: the
    /// principal header's presence routes the request to a test scheme whose handler returns a principal
    /// directly, which is what keeps the sole-issuer invariant structurally true - Security holds the only
    /// signing secret in the system and this assembly adds no second one. The token-echo property
    /// therefore belongs to the suites that have a real token to echo, and the equivalent disclosure
    /// question here is whether the refusal repeats the identity it was handed.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task NoRefusalBodyEchoesThePresentedIdentityAsync()
    {
        await using DataServicesTestHostFactory host = new();
        using HttpClient client = host.CreateScopedClient(["dataservices.wrong-scope"]);

        using HttpResponseMessage response = await client.PostAsync(
            new Uri(ProjectedRoute, UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(body);

        Assert.DoesNotContain(
            DataServicesTestHostFactory.TestPrincipalHeaderValue,
            body,
            StringComparison.Ordinal);

        Assert.DoesNotContain("dataservices.wrong-scope", body, StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  2. THE GENERATED DOCUMENT IS NOT ANONYMOUS.
    // ==============================================================================================

    /// <summary>The generated contract document requires a credential.</summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// IT WAS EXPLICITLY ANONYMOUS, on the reasoning that a description of the surface is not part of the
    /// surface it describes. Constraint C-G does not leave that to judgement: it ENUMERATES the anonymous
    /// exceptions as readiness plus the sole issuer's key-set and discovery documents, which a stock
    /// bearer handler must fetch before it holds any credential at all. A contract description is neither.
    /// </para>
    /// <para>
    /// On THIS service the case is stronger than at the ingress, because nothing outside the estate has
    /// any reason to read this document: Gateway is the only caller, it arrives with a Security-issued
    /// token, and the authored specification is what a consumer is handed.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheGeneratedDocumentRequiresACredentialAsync()
    {
        await using DataServicesTestHostFactory host = new();
        using HttpClient client = host.CreateAnonymousClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(DocumentRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// THE POSITIVE ARM: the document is still served to a caller that presents a credential.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// Without this row the one above would pass against a service that had stopped generating the
    /// document altogether, or that had removed the route - both of which are refusals of a sort and
    /// neither of which is the intended behaviour.
    /// </remarks>
    [Fact]
    public async Task TheGeneratedDocumentIsServedToACredentialedCallerAsync()
    {
        await using DataServicesTestHostFactory host = new();
        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(DocumentRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.True(
            document.RootElement.TryGetProperty("paths", out JsonElement paths),
            "The generated document declares a paths object.");

        Assert.NotEmpty(paths.EnumerateObject());
    }

    // ==============================================================================================
    //  3. THE ROUTES THAT WRITE THEIR OWN BODY ARE UNTOUCHED.
    // ==============================================================================================

    /// <summary>
    /// Readiness still answers anonymously with its own body, so the added middleware did not reach a
    /// route that was already complete.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE BLAST-RADIUS ROW, and the answer to the objection that installing a status-code page changes
    /// the service on every status it answers. It writes a body only where the response has none and the
    /// status is a failure, so a route that already wrote one is untouched - and readiness is the route
    /// where a regression would be most expensive, because three services gate their own startup on it
    /// and a broken body would take a deployment down while every service behaved as written.
    /// </remarks>
    [Fact]
    public async Task ReadinessStillAnswersAnonymouslyWithItsOwnBodyAsync()
    {
        await using DataServicesTestHostFactory host = new();
        using HttpClient client = host.CreateAnonymousClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(ReadinessRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.NotEmpty(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Asserts the published problem shape and media type on a refusal.</summary>
    /// <param name="response">The refusal.</param>
    /// <param name="expected">The status the body must agree with.</param>
    /// <returns>A task representing the assertion.</returns>
    private static async Task AssertProblemBodyAsync(
        HttpResponseMessage response,
        HttpStatusCode expected)
    {
        Assert.Equal(
            MediaTypeNames.Application.ProblemJson,
            response.Content.Headers.ContentType?.MediaType);

        using JsonDocument problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal((int)expected, problem.RootElement.GetProperty("status").GetInt32());

        Assert.False(
            string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("title").GetString()),
            "The published problem schema declares a title, so an empty one is a contract breach.");
    }
}
