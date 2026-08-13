// ==================================================================================================
//  PublishedDocumentAuthorizationTests - THE GENERATED CONTRACT DOCUMENT IS NOT AN ANONYMOUS ROUTE
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS FILE GUARDS
//
//  The generated OpenAPI document used to be mapped with an explicit anonymous exemption, in every
//  environment, on the argument that a description of a surface is not part of the surface. That
//  argument does not survive the Agent Action Plan, which ENUMERATES the anonymous exceptions rather
//  than describing a principle: `/health` on all four services under contract C-10, and Security's key
//  set and discovery document under contract C-01. The generated document is not among them, so an
//  exemption for it exceeded the enumeration.
//
//  Nor is anything withheld by protecting it. A consumer learns this service's shape from the AUTHORED
//  contracts under shared/PowerFramework.Contracts - files in the repository, handed over out of band -
//  and this route serves a generated projection of them. The circularity objection that a consumer must
//  read the document in order to learn how to authenticate does not apply either: that objection is real
//  for a KEY SET and a DISCOVERY DOCUMENT, which a bearer handler must fetch before it holds anything,
//  and both of those are anonymous on Security for exactly that reason. Security - the token issuer
//  itself - already publishes its own generated document behind its own boundary.
//
//  WHY IT IS A FILE OF ITS OWN
//  ------------------------------------------------------------------------------------------------
//  The exemption was one call. Restoring it is one call, and nothing else in this suite would notice: no
//  other row requests the document, so its posture could revert silently. A named file is what makes the
//  posture assertable and findable - a reader looking for "is the document protected?" finds it here.
//
//  BOTH ARMS ARE ASSERTED, WHICH IS THE POINT
//  ------------------------------------------------------------------------------------------------
//  A row proving the anonymous caller is refused says nothing about whether the document still works. A
//  route that had been protected by breaking it would pass that row alone. So the authenticated arm
//  asserts a success AND that the body is the document rather than an error shape.
//
//  CONSTRAINT COMPLIANCE
//  ------------------------------------------------------------------------------------------------
//  C-G   EVERY NEW BOUNDARY AUTHENTICATED, with the anonymous exceptions being exactly the enumerated
//        ones and no others.
//  C-H   80 PER CENT LINE COVERAGE PER SERVICE. Named share: the document mapping in Program.cs.
//  C-I   EACH SERVICE BUILDS AND TESTS INDEPENDENTLY. The host is in-memory; no port is bound.
//  NO CREDENTIAL APPEARS IN THIS FILE. The fixture establishes a principal through a header whose
//        PRESENCE selects a test authentication scheme; no key, token or signature value appears here.
// ==================================================================================================

using System.Net;
using System.Text.Json;
using Xunit;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// Proves that the generated contract document is served behind this service's authentication boundary,
/// and is still served.
/// </summary>
/// <param name="host">The shared in-memory host.</param>
public sealed class PublishedDocumentAuthorizationTests(DataServicesTestHostFactory host)
    : IClassFixture<DataServicesTestHostFactory>
{
    /// <summary>The address the generated document is served at.</summary>
    /// <remarks>
    /// The framework's default for a single unnamed document. Written out rather than derived, because
    /// this is the address a consumer is given and a change to it is a change a consumer sees.
    /// </remarks>
    private const string DocumentRoute = "/openapi/v1.json";

    /// <summary>The document member that names the specification version.</summary>
    private const string SpecificationVersionMember = "openapi";

    /// <summary>
    /// An anonymous caller is refused the generated document.
    /// </summary>
    /// <remarks>
    /// 401 SPECIFICALLY, not merely "not 200". The document route carries no scope requirement of its own,
    /// so the refusal comes from the fallback policy and must be the authentication refusal - a 403 here
    /// would mean an unauthenticated caller was told its permissions were the problem.
    /// </remarks>
    [Fact]
    public async Task AnAnonymousCallerIsRefusedTheGeneratedDocument()
    {
        using HttpClient client = host.CreateAnonymousClient();

        using HttpResponseMessage refused = await client.GetAsync(
            new Uri(DocumentRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    /// <summary>
    /// An authenticated caller receives the generated document.
    /// </summary>
    /// <remarks>
    /// THE ARM THAT KEEPS THE FIRST ONE HONEST. Protecting a route by breaking it would satisfy the
    /// refusal row on its own, so this row requires both a success status and a body that is recognisably
    /// the document - the specification-version member is asserted because it is present in every
    /// conforming document and absent from every error shape this service can produce.
    /// </remarks>
    [Fact]
    public async Task AnAuthenticatedCallerReceivesTheGeneratedDocument()
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage served = await client.GetAsync(
            new Uri(DocumentRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, served.StatusCode);

        string body = await served.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using JsonDocument document = JsonDocument.Parse(body);

        Assert.True(
            document.RootElement.TryGetProperty(SpecificationVersionMember, out JsonElement version),
            "The authenticated response must be the generated contract document. Without the "
            + "specification-version member it is some other payload, which would mean the route was "
            + "protected by breaking it rather than by requiring a credential.");

        Assert.False(string.IsNullOrWhiteSpace(version.GetString()));
    }

    /// <summary>
    /// Every projected operation in the served document declares the four statuses this service's failure
    /// map can produce on any of them.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE DOCUMENT A CONSUMER FETCHES IS GENERATED FROM ROUTE METADATA, NOT FROM THE AUTHORED CONTRACT, so
    /// a declaration is only real once it is on the route. The projection's failure map translated
    /// <c>ResourceExhausted</c>, <c>Unavailable</c> and <c>DeadlineExceeded</c> into 429, 503 and 504 while
    /// no operation declared any of the three, which leaves a generated client with no branch for a status
    /// it will receive.
    /// </para>
    /// <para>
    /// None of the four depends on which method is projected, which is what makes the assertion uniform: a
    /// capacity ceiling can refuse any call that needs a handle, an upstream can decline any call, a
    /// deadline is set on every call, and any call can fail before a gRPC response arrives at all.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task EveryProjectedOperationDeclaresTheStatusesItsFailureMapProduces()
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage served = await client.GetAsync(
            new Uri(DocumentRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, served.StatusCode);

        using JsonDocument document = JsonDocument.Parse(
            await served.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        string[] required = ["429", "500", "502", "503", "504"];
        int projected = 0;

        foreach (JsonProperty path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            if (!path.Name.StartsWith("/v1/datawindow", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (JsonProperty operation in path.Value.EnumerateObject())
            {
                if (!operation.Value.TryGetProperty("responses", out JsonElement responses))
                {
                    continue;
                }

                projected++;

                foreach (string status in required)
                {
                    Assert.True(
                        responses.TryGetProperty(status, out _),
                        $"{operation.Name.ToUpperInvariant()} {path.Name} must declare {status}: the "
                            + "failure map can produce it on any projected operation.");
                }
            }
        }

        // Asserted so that a projection removed from the route table cannot make this pass by finding
        // nothing to check. Thirty-nine is the contract's own figure: fifteen of C-03's sixteen methods and
        // twenty-four of C-04's twenty-six - every unary and every server-streaming one, and none of the
        // three bidirectional ones.
        Assert.Equal(39, projected);
    }
}
