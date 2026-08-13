// =================================================================================================
//  HealthEndpointsTests - the Security service's anonymous readiness probe, exercised over a real
//  HTTP pipeline.
//
//  WHY A TEST HOST AND NOT A DIRECT CALL. Endpoints/HealthEndpoints.cs exposes exactly one public
//  member, MapHealthEndpoints, and everything this suite needs to observe is a property of the
//  RESPONSE rather than of a method's return value: the STATUS CODE the orchestration readiness gate
//  reads, the anonymity that gate depends on, and the exact member set an anonymous body is allowed
//  to carry. A direct invocation of the handler could observe none of the three, because the handler
//  is private by design and its IResult has to be executed to become a status and a body.
//
//  TWO HOSTS, EACH FOR A DIFFERENT QUESTION, AND THE SPLIT IS DELIBERATE.
//
//    * THE REAL COMPOSITION ROOT, through WebApplicationFactory<Program>. This is the only way to
//      exercise the property that matters most: Program.cs installs a FALLBACK AUTHORIZATION POLICY
//      requiring an authenticated user, so on this service omitting a policy on a route CLOSES it.
//      The rows below therefore prove the 200 is earned by the explicit AllowAnonymous() call and not
//      by an inert policy - each anonymity row is paired with a refutation showing the fallback
//      policy really is active on the same host. These rows also exercise the published OpenAPI
//      document, which only the real composition produces, and the disclosure boundary against the
//      service's REAL appsettings.json values rather than against invented ones.
//
//    * A LOCALLY BUILT HOST, through WebApplication.CreateSlimBuilder plus UseTestServer. The
//      endpoint distinguishes THREE registration states - no health check registry at all, a
//      registry with no component check, and a registry with component checks - and the real
//      composition root can only ever produce one of them. Building the host here is what makes all
//      three reachable, and it is also what lets a row register a deliberately hostile check name and
//      description in order to prove neither is echoed.
//
//  WHAT EACH GROUP OF ROWS EXISTS TO PROTECT.
//    * ONLY Healthy IS 200. Degraded means NOT READY, and what an orchestrator observes is the
//      status code, so a 200 for Degraded would satisfy `depends_on: condition: service_healthy` on a
//      service that had just said it was not usable - Gateway would start behind an upstream that was
//      not ready while every document still claimed the gate held. That is the single most important
//      readiness property in the orchestration, and it is a status-code property.
//    * THE PROBE IS ANONYMOUS, AND UNAVOIDABLY SO ON THIS SERVICE. Security is the sole token issuer,
//      so a probe requiring a token could not answer until issuance was live - and issuance going
//      live is the very thing the probe gates.
//    * THE PROBE TOUCHES NO CRYPTOGRAPHIC MATERIAL. Readiness is gated on this answer, so the handler
//      must not reach the signing or randomness path. Proved by booting the real host with every
//      cryptographic registration replaced by a factory that throws, and showing the probe still
//      answers 200.
//    * THE ANONYMOUS BODY DISCLOSES NOTHING. This is the one route an unauthenticated caller can
//      always reach on the service that holds the system's single signing secret, so a leak here
//      would be both unconditional and maximally costly. Nothing a registration supplied and nothing
//      configuration supplied may appear in it.
//    * THE COUNT OF REGISTERED CHECKS IS ITSELF DISCLOSURE. One entry per check, even anonymised,
//      publishes the shape of this service's dependency graph, so the aggregation is asserted rather
//      than assumed.
// =================================================================================================

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PowerFramework.Security.Authorization;
using PowerFramework.Security.Configuration;
using PowerFramework.Security.Crypto;
using PowerFramework.Security.Endpoints;
using PowerFramework.Security.Tokens;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Security.Tests;

/// <summary>
/// Conformance rows for <see cref="HealthEndpoints"/>: the status-code mapping the readiness gate
/// depends on, the anonymity it depends on, the cheapness guarantee, and the disclosure boundary.
/// </summary>
public sealed class HealthEndpointsTests
{
    /// <summary>The route under test, spelled as the authored contract spells it.</summary>
    private static readonly Uri HealthRoute = new("/health", UriKind.Relative);

    /// <summary>The authenticated route used as the refutation for every anonymity row.</summary>
    private static readonly Uri PingRoute = new("/v1/ping", UriKind.Relative);

    /// <summary>The published OpenAPI document, which the real composition root maps.</summary>
    private static readonly Uri OpenApiDocumentRoute = new("/openapi/v1.json", UriKind.Relative);

    /// <summary>The two component identifiers the anonymous body is permitted to name.</summary>
    private static readonly string[] PermittedComponentNames = ["self", "components"];

    // ==============================================================================================
    //  GROUP 1 - THE REAL COMPOSITION ROOT
    //  These rows run against Program.cs itself, which is the only host that carries the default-deny
    //  fallback authorization policy, the published OpenAPI document and the real configuration.
    // ==============================================================================================

    /// <summary>
    /// THE SINGLE MOST IMPORTANT ASSERTION IN THIS SUITE: the probe answers 200 with no credential at
    /// all, against the service's real composition root.
    /// </summary>
    /// <remarks>
    /// The paired refutation is what gives the row its force. <c>Program.cs</c> installs a fallback
    /// policy requiring an authenticated user, so if that policy were inert the 200 would prove
    /// nothing; the second half of this row shows the identical client is refused on
    /// <c>/v1/ping</c>. Together they establish that the probe is open BECAUSE
    /// <c>AllowAnonymous()</c> is called explicitly - remove that call and this row fails with 401.
    /// </remarks>
    [Fact]
    public async Task TheProbeAnswersTwoHundredWithoutATokenUnderTheRealDefaultDenyFallbackPolicy()
    {
        await using SecurityHostFactory factory = new();
        using HttpClient client = factory.CreateClient();

        Assert.Null(client.DefaultRequestHeaders.Authorization);

        using HttpResponseMessage probe = await client.GetAsync(
            HealthRoute,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, probe.StatusCode);

        using JsonDocument body = JsonDocument.Parse(
            await probe.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal("Healthy", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("security", body.RootElement.GetProperty("service").GetString());

        // THE REFUTATION. Without this, a fallback policy that had silently stopped applying would
        // make the assertion above pass for the wrong reason.
        using HttpResponseMessage refused = await client.GetAsync(
            PingRoute,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    /// <summary>
    /// The probe answers even when every cryptographic registration in the service is poisoned, which
    /// is what "the probe does no cryptographic work" means operationally.
    /// </summary>
    /// <remarks>
    /// Each of the seven cryptographic registrations - the entropy source behind random blobs, strings
    /// and identifiers, and the six providers composed over it - is replaced by a factory that throws
    /// on resolution. Every one is a singleton, so it is constructed lazily on first resolution: if the
    /// readiness handler reached any of them, directly or transitively, this row would observe a 500
    /// rather than a 200. Readiness is gated on this endpoint, so a probe that touched the signing or
    /// randomness path would make the orchestration's readiness gate depend on the most sensitive path
    /// in the system.
    /// </remarks>
    [Fact]
    public async Task TheProbeAnswersWithoutResolvingAnyCryptographicOrRandomnessService()
    {
        await using SecurityHostFactory factory = new(PoisonEveryCryptographicRegistration);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage probe = await client.GetAsync(
            HealthRoute,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, probe.StatusCode);

        using JsonDocument body = JsonDocument.Parse(
            await probe.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal("Healthy", body.RootElement.GetProperty("status").GetString());

        // THE REFUTATION FOR THE POISONING ITSELF: the doubles are installed and would in fact throw,
        // so the 200 above is a statement about which services the handler resolves and not about a
        // replacement that never took effect.
        using IServiceScope scope = factory.Services.CreateScope();

        Assert.Throws<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<RandomProvider>());
    }

    /// <summary>
    /// The anonymous body echoes none of the service's configured identity, key or metadata values.
    /// </summary>
    /// <remarks>
    /// Every string below is a REAL value from the service's own <c>appsettings.json</c> rather than an
    /// invented one, so the row fails if the report ever grows a member that projects configuration.
    /// The generic secret shapes are asserted alongside them: a key reference member, the two PEM block
    /// markers, and any run of base64 long enough to be key material.
    /// </remarks>
    [Fact]
    public async Task TheAnonymousBodyEchoesNoConfiguredIdentityKeyOrMetadataValue()
    {
        await using SecurityHostFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage probe = await client.GetAsync(
            HealthRoute,
            TestContext.Current.CancellationToken);

        string payload = await probe.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, probe.StatusCode);

        IConfiguration configuration = factory.Services.GetRequiredService<IConfiguration>();

        string[] forbidden = [.. ForbiddenBodyFragments(configuration)];

        // The row is only meaningful if the host really has identity and metadata values to leak, so the
        // list itself is asserted non-trivial before it is used.
        Assert.True(forbidden.Length > 5);

        foreach (string fragment in forbidden)
        {
            Assert.DoesNotContain(fragment, payload, StringComparison.OrdinalIgnoreCase);
        }

        // No run of base64 long enough to be a key, a token or a certificate body. The report's only
        // long strings are fixed English prose, which contains spaces and full stops and so cannot
        // form such a run.
        Assert.DoesNotMatch("[A-Za-z0-9+/]{24,}={0,2}", payload);
    }

    /// <summary>
    /// The published document declares exactly the operation the authored contract fixes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the generated-document half of the contract check:
    /// <c>shared/PowerFramework.Contracts/OpenApi/security.v1.yaml</c> is packaged as content rather
    /// than compiled, so nothing links the two at build time and the agreement has to be asserted.
    /// The YAML is authoritative - where it and this file's expectations could ever disagree, the
    /// endpoint changes, not the contract.
    /// </para>
    /// <para>
    /// The absent-or-empty security requirement is the structural form of the anonymity decision: the
    /// document declares a bearer scheme at the document level, and this operation must not inherit a
    /// NON-EMPTY requirement from it. The response set is asserted to be exactly two so that a
    /// carelessly typed result union, which would have the framework infer an extra 500 from the
    /// problem result's own metadata, is caught here rather than in a consumer.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ThePublishedDocumentDeclaresTheOperationTheAuthoredContractFixes()
    {
        await using SecurityHostFactory factory = new();

        // THE DOCUMENT IS NOT ONE OF THE SERVICE'S THREE ANONYMOUS ROUTES. The refutation comes first
        // so that the assertions below cannot pass for the wrong reason: an anonymous caller is refused
        // by the default-deny fallback policy, exactly as on every other non-exempt route.
        using HttpClient anonymous = factory.CreateClient();

        using HttpResponseMessage refused = await anonymous.GetAsync(
            OpenApiDocumentRoute,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

        using HttpClient client = factory.CreateAuthenticatedClient();

        using HttpResponseMessage document = await client.GetAsync(
            OpenApiDocumentRoute,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, document.StatusCode);

        using JsonDocument parsed = JsonDocument.Parse(
            await document.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        JsonElement operation = parsed.RootElement
            .GetProperty("paths")
            .GetProperty("/health")
            .GetProperty("get");

        Assert.Equal("getHealth", operation.GetProperty("operationId").GetString());
        Assert.Equal(
            "Report this service's own readiness. Anonymous.",
            operation.GetProperty("summary").GetString());

        JsonElement tag = Assert.Single(operation.GetProperty("tags").EnumerateArray().ToArray());
        Assert.Equal("Health", tag.GetString());

        // Anonymous: either no requirement at all, or an empty requirement list. A NON-EMPTY
        // requirement would mean the operation had inherited the document-level bearer scheme.
        if (operation.TryGetProperty("security", out JsonElement security))
        {
            foreach (JsonElement requirement in security.EnumerateArray())
            {
                Assert.Empty(requirement.EnumerateObject().ToArray());
            }
        }

        JsonElement responses = operation.GetProperty("responses");

        string[] declared = [.. responses.EnumerateObject().Select(static member => member.Name)];

        Assert.Equal(2, declared.Length);
        Assert.Contains("200", declared);
        Assert.Contains("503", declared);

        Assert.Equal(
            "#/components/schemas/ServiceHealthReport",
            responses.GetProperty("200").GetProperty("content")
                .GetProperty("application/json").GetProperty("schema")
                .GetProperty("$ref").GetString());

        // The 503 reuses the document's single error shape rather than defining a second one for this
        // one response, which is what makes a consumer able to write exactly one error handler.
        Assert.Equal(
            "#/components/schemas/ProblemDetails",
            responses.GetProperty("503").GetProperty("content")
                .GetProperty("application/problem+json").GetProperty("schema")
                .GetProperty("$ref").GetString());

        JsonElement schemas = parsed.RootElement.GetProperty("components").GetProperty("schemas");

        // THE MEMBER SET IS WHAT THE CONTRACT FIXES, and the generated names are camel-cased exactly as
        // the authored schema spells them. A renamed or added member would be a wire change.
        AssertProperties(
            schemas.GetProperty("ServiceHealthReport"),
            ["status", "service", "checkedAt", "checks"]);

        AssertProperties(
            schemas.GetProperty("ServiceHealthCheck"),
            ["name", "status", "description"]);

        // The check result's required set matches the authored schema EXACTLY: the note is optional, and
        // the omit-when-null rule is what makes "optional" mean absent on the wire rather than null.
        AssertRequired(schemas.GetProperty("ServiceHealthCheck"), ["name", "status"]);

        // The report declares all four members required, which is a deliberate STRENGTHENING over the
        // authored schema's {status, service}: this service always populates all four, so the generated
        // document states what it actually sends. A stricter guarantee on an existing member cannot
        // break a consumer written against the looser family schema.
        AssertRequired(
            schemas.GetProperty("ServiceHealthReport"),
            ["status", "service", "checkedAt", "checks"]);
    }

    /// <summary>Asserts a generated schema declares exactly the expected property names.</summary>
    /// <param name="schema">The generated schema.</param>
    /// <param name="expected">The property names, in the order the authored schema lists them.</param>
    private static void AssertProperties(JsonElement schema, string[] expected)
    {
        string[] declared =
            [.. schema.GetProperty("properties").EnumerateObject().Select(static m => m.Name)];

        Assert.Equal<IEnumerable<string>>(expected, declared);
    }

    /// <summary>Asserts a generated schema declares exactly the expected required members.</summary>
    /// <param name="schema">The generated schema.</param>
    /// <param name="expected">The required member names.</param>
    private static void AssertRequired(JsonElement schema, string[] expected)
    {
        string[] wanted = [.. expected.Order(StringComparer.Ordinal)];

        string[] declared =
            [.. schema.GetProperty("required").EnumerateArray()
                .Select(static member => member.GetString()!)
                .Order(StringComparer.Ordinal)];

        Assert.Equal<IEnumerable<string>>(wanted, declared);
    }

    /// <summary>
    /// A null route builder is rejected at composition time.
    /// </summary>
    /// <remarks>
    /// The only throw in the endpoint file, and it is a programming error at composition time rather
    /// than a request-time failure - the request handler itself never throws, because a probe that
    /// faulted would take the container with it and the readiness gate could never recover.
    /// </remarks>
    [Fact]
    public void MappingOntoANullRouteBuilderIsRejected()
    {
        Assert.Throws<ArgumentNullException>(
            static () => HealthEndpoints.MapHealthEndpoints(null!));
    }

    // ==============================================================================================
    //  GROUP 2 - THE STATUS-CODE MAPPING
    //  The readiness gate's whole contract with this endpoint, and the one property whose regression
    //  would be invisible to every document while silently defeating the orchestration ordering.
    // ==============================================================================================

    /// <summary>A fully ready service answers 200.</summary>
    [Fact]
    public async Task AFullyReadyServiceAnswersTwoHundred()
    {
        (IHost host, HttpClient client) = await StartAsync(
            ("signing-key-material", HealthStatus.Healthy, "loaded"));

        using (host)
        using (client)
        {
            (HttpStatusCode status, JsonDocument body) = await ProbeAsync(client);

            using (body)
            {
                Assert.Equal(HttpStatusCode.OK, status);
                Assert.Equal("Healthy", body.RootElement.GetProperty("status").GetString());
                Assert.Equal("security", body.RootElement.GetProperty("service").GetString());
            }
        }
    }

    /// <summary>
    /// BOTH not-ready verdicts answer 503, and both stay distinguishable in the body.
    /// </summary>
    /// <param name="registered">The component verdict to register.</param>
    /// <param name="expectedWireStatus">The wire token that verdict must produce.</param>
    /// <remarks>
    /// THE ROWS THAT MATTER MOST IN THIS FILE. <c>Degraded</c> answering 200 would let
    /// <c>depends_on: condition: service_healthy</c> open on a service that had just reported itself
    /// not ready, so Gateway would start behind an unusable upstream while the readiness gate appeared
    /// to be working. Collapsing <c>Degraded</c> into <c>Unhealthy</c> would lose the distinction
    /// contract C-10 requires between a service still completing startup validation and one whose
    /// component has failed - so the verdict token is asserted in the problem detail as well as the
    /// status code.
    /// </remarks>
    [Theory]
    [InlineData(HealthStatus.Degraded, "Degraded")]
    [InlineData(HealthStatus.Unhealthy, "Unhealthy")]
    public async Task ANotReadyServiceAnswersFiveOhThreeForBothNotReadyVerdicts(
        HealthStatus registered,
        string expectedWireStatus)
    {
        (IHost host, HttpClient client) = await StartAsync(
            ("signing-key-material", registered, "still loading"));

        using (host)
        using (client)
        {
            (HttpStatusCode status, JsonDocument body) = await ProbeAsync(client);

            using (body)
            {
                Assert.Equal(HttpStatusCode.ServiceUnavailable, status);

                string? detail = body.RootElement.GetProperty("detail").GetString();

                Assert.NotNull(detail);
                Assert.Contains(expectedWireStatus, detail!, StringComparison.Ordinal);
                Assert.Contains("components", detail!, StringComparison.Ordinal);

                // The legacy return code the authored contract requires in the single permitted
                // extension member. E_RETRY is "the operation should be retried", the legacy
                // vocabulary's match for Service Unavailable.
                Assert.Equal(
                    RetCode.E_RETRY,
                    body.RootElement.GetProperty("retCode").GetInt64());

                // AND THE VERDICT IS MACHINE-READABLE, WHICH IS THE HALF THAT ACTUALLY REACHES GATEWAY.
                // The detail above is prose for a human and retCode is identical for both verdicts, so
                // neither separates the two states contract C-10 promises to keep apart. The token cannot
                // live in `status` - RFC 9457 uses that name here for the integer HTTP status - so it has a
                // member of its own, and Gateway's aggregator reads exactly this one.
                Assert.Equal(
                    expectedWireStatus,
                    body.RootElement.GetProperty("serviceStatus").GetString());
                Assert.Equal(503, body.RootElement.GetProperty("status").GetInt32());

                Assert.Equal(
                    "Service Unavailable",
                    body.RootElement.GetProperty("title").GetString());
            }
        }
    }

    /// <summary>
    /// A status token this build does not recognise fails CLOSED.
    /// </summary>
    /// <remarks>
    /// The endpoint tests FOR the single ready state rather than against the failed ones, which is
    /// precisely what makes an unrecognised value answer 503 instead of leaking through as ready. The
    /// aggregate is supplied directly here because no registration can produce an out-of-range verdict.
    /// </remarks>
    [Fact]
    public async Task AnUnrecognisedAggregateVerdictFailsClosed()
    {
        (IHost host, HttpClient client) = await StartWithReportAsync(
            new HealthReport(
                new Dictionary<string, HealthReportEntry>(StringComparer.Ordinal),
                (HealthStatus)42,
                TimeSpan.Zero));

        using (host)
        using (client)
        {
            (HttpStatusCode status, JsonDocument body) = await ProbeAsync(client);

            using (body)
            {
                Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
                Assert.Contains(
                    "Unhealthy",
                    body.RootElement.GetProperty("detail").GetString()!,
                    StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// An aggregate that is not ready while every individual entry is ready still answers 503, and the
    /// problem detail names no component.
    /// </summary>
    /// <remarks>
    /// Reachable rather than theoretical: a registration whose own result predicate degrades the whole
    /// on a condition no single entry reports produces exactly this shape. The endpoint handles it
    /// instead of assuming it away, and the detail says the service is not ready without naming a
    /// component that did not report one.
    /// </remarks>
    [Fact]
    public async Task AnAggregateOnlyFailureAnswersFiveOhThreeAndNamesNoComponent()
    {
        (IHost host, HttpClient client) = await StartWithReportAsync(
            new HealthReport(
                new Dictionary<string, HealthReportEntry>(StringComparer.Ordinal)
                {
                    ["all-good"] = new HealthReportEntry(
                        HealthStatus.Healthy,
                        "ready",
                        TimeSpan.Zero,
                        exception: null,
                        data: null),
                },
                HealthStatus.Degraded,
                TimeSpan.Zero));

        using (host)
        using (client)
        {
            (HttpStatusCode status, JsonDocument body) = await ProbeAsync(client);

            using (body)
            {
                Assert.Equal(HttpStatusCode.ServiceUnavailable, status);

                string detail = body.RootElement.GetProperty("detail").GetString()!;

                Assert.Equal("The Security service is not ready (Degraded).", detail);
                Assert.DoesNotContain("all-good", detail, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>
    /// A readiness evaluation that throws is REPORTED as unhealthy, never propagated.
    /// </summary>
    /// <remarks>
    /// A probe that faulted would take the container with it and the readiness gate could never
    /// recover, so the handler catches, records the reason on the operator channel, and answers 503.
    /// The anonymous body must carry the fact of the failure and never its reason: the reason is an
    /// exception, and on the service holding the system's single signing secret an exception raised
    /// while evaluating readiness could name a key store or a key identifier.
    /// </remarks>
    [Fact]
    public async Task AReadinessEvaluationThatThrowsIsReportedRatherThanPropagated()
    {
        const string LeakyFailure =
            "key store /var/lib/powerframework/keys/signing.pem is unreadable for account svc_security";

        OperatorChannel channel = new();

        (IHost host, HttpClient client) = await StartCoreAsync(
            registerHealthCheckService: false,
            channel,
            healthCheckService: new ThrowingHealthCheckService(LeakyFailure));

        using (host)
        using (client)
        {
            (HttpStatusCode status, JsonDocument body) = await ProbeAsync(client);

            using (body)
            {
                Assert.Equal(HttpStatusCode.ServiceUnavailable, status);

                string payload = body.RootElement.ToString();

                Assert.Contains("self", payload, StringComparison.Ordinal);
                Assert.DoesNotContain(LeakyFailure, payload, StringComparison.Ordinal);
                Assert.DoesNotContain("signing.pem", payload, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("svc_security", payload, StringComparison.OrdinalIgnoreCase);
            }
        }

        // The reason reaches the OPERATOR, whose audience is authenticated by having access to this
        // service's logs rather than by being able to reach a port.
        (LogLevel level, string record) = Assert.Single(channel.Records);

        Assert.Equal(LogLevel.Error, level);
        Assert.Contains("security", record, StringComparison.Ordinal);
        Assert.Contains("Unhealthy", record, StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  GROUP 3 - THE THREE REGISTRATION STATES THE ENDPOINT DISTINGUISHES
    // ==============================================================================================

    /// <summary>
    /// With no health check registry at all, the service is ready on the strength of answering.
    /// </summary>
    /// <remarks>
    /// The probe must answer whether or not the composition root registered anything, because binding
    /// this endpoint's liveness to a registration it does not own would turn a probe into a startup
    /// gate. With nothing registered, the whole of a leaf service's readiness is that its process is
    /// running and answering - which executing this request has just demonstrated.
    /// </remarks>
    [Fact]
    public async Task AServiceWithNoHealthCheckRegistryIsReadyOnTheStrengthOfAnsweringAtAll()
    {
        (IHost host, HttpClient client) = await StartAsync();

        using (host)
        using (client)
        {
            (HttpStatusCode status, JsonDocument body) = await ProbeAsync(client);

            using (body)
            {
                Assert.Equal(HttpStatusCode.OK, status);
                Assert.Equal("Healthy", body.RootElement.GetProperty("status").GetString());

                JsonElement only = Assert.Single(
                    body.RootElement.GetProperty("checks").EnumerateArray().ToArray());

                Assert.Equal("self", only.GetProperty("name").GetString());
            }
        }
    }

    /// <summary>
    /// A registry with no registered component check reports this service's own liveness alone.
    /// </summary>
    /// <remarks>
    /// The state a composition root produces by enabling the health check registry before any
    /// component check exists - which is exactly what this service's <c>Program.cs</c> does today. The
    /// aggregate must then be liveness rather than an empty component summary: a <c>components</c>
    /// entry over zero checks would claim a verdict about a dependency graph that has no members.
    /// </remarks>
    [Fact]
    public async Task ARegisteredRegistryWithNoRegisteredCheckReportsLivenessAlone()
    {
        (IHost host, HttpClient client) = await StartCoreAsync(
            registerHealthCheckService: true,
            channel: null);

        using (host)
        using (client)
        {
            (HttpStatusCode status, JsonDocument body) = await ProbeAsync(client);

            using (body)
            {
                Assert.Equal(HttpStatusCode.OK, status);
                Assert.Equal("Healthy", body.RootElement.GetProperty("status").GetString());

                JsonElement only = Assert.Single(
                    body.RootElement.GetProperty("checks").EnumerateArray().ToArray());

                Assert.Equal("self", only.GetProperty("name").GetString());
                Assert.Equal(
                    "The service process is running and answering requests.",
                    only.GetProperty("description").GetString());
            }
        }
    }

    // ==============================================================================================
    //  GROUP 4 - THE DISCLOSURE BOUNDARY
    //  What an anonymous body on the sole token issuer may and may not carry.
    // ==============================================================================================

    /// <summary>
    /// No registration-supplied name or description reaches the anonymous body.
    /// </summary>
    /// <remarks>
    /// BOTH STRINGS BELOW ARE THE FAILURE MODE, WRITTEN OUT. A registration names its check and
    /// describes it for its own operator, not for an anonymous reader, and the contract's request that
    /// authors keep those free of internal detail is a rule on THEM - not a control this service can
    /// enforce. So the check name identifies a key vault and a region, and the description carries a
    /// path, an account and an identifier. If either is echoed, this row fails. The literals are
    /// deliberately shaped like the thing they stand for while containing no real credential.
    /// </remarks>
    [Fact]
    public async Task NoRegistrationSuppliedNameOrDescriptionReachesTheAnonymousBody()
    {
        const string LeakyName = "keyvault-signing.security.internal.eu-west-1";
        const string LeakyDescription =
            "signing key powerframework-security-signing-1 unreadable at "
            + "/var/lib/powerframework/keys/signing.pem for account svc_security";

        (IHost host, HttpClient client) = await StartAsync(
            (LeakyName, HealthStatus.Unhealthy, LeakyDescription));

        using (host)
        using (client)
        {
            using HttpResponseMessage response = await client.GetAsync(
                HealthRoute,
                TestContext.Current.CancellationToken);

            string payload = await response.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

            foreach (string forbidden in (string[])
                [
                    LeakyName,
                    LeakyDescription,
                    "keyvault",
                    "security.internal",
                    "eu-west-1",
                    "signing.pem",
                    "svc_security",
                    "/var/lib",
                ])
            {
                Assert.DoesNotContain(forbidden, payload, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>
    /// However many checks are registered, the body names only the closed public vocabulary.
    /// </summary>
    [Fact]
    public async Task TheBodyNamesOnlyTheClosedPublicVocabularyHoweverManyChecksAreRegistered()
    {
        (IHost host, HttpClient client) = await StartAsync(
            ("keystore-a", HealthStatus.Healthy, "ok"),
            ("keystore-b", HealthStatus.Healthy, "ok"),
            ("metadata-cache", HealthStatus.Degraded, "warming"));

        using (host)
        using (client)
        {
            (HttpStatusCode status, JsonDocument body) = await ProbeAsync(client);

            using (body)
            {
                Assert.Equal(HttpStatusCode.ServiceUnavailable, status);

                string payload = body.RootElement.ToString();

                foreach (string leaked in (string[])["keystore-a", "keystore-b", "metadata-cache"])
                {
                    Assert.DoesNotContain(leaked, payload, StringComparison.OrdinalIgnoreCase);
                }

                Assert.Contains("components", payload, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// Three registered checks become ONE aggregated entry rather than three.
    /// </summary>
    /// <remarks>
    /// THE COUNT IS ITSELF DISCLOSURE, which is why aggregation is asserted rather than left to read as
    /// a formatting choice. One anonymised entry per registered check would publish how many components
    /// this service depends on to any unauthenticated caller - the same disclosure as a name, expressed
    /// as a number.
    /// </remarks>
    [Fact]
    public async Task ThreeRegisteredChecksBecomeOneAggregatedEntryRatherThanThree()
    {
        (IHost host, HttpClient client) = await StartAsync(
            ("keystore-a", HealthStatus.Healthy, "ok"),
            ("keystore-b", HealthStatus.Healthy, "ok"),
            ("metadata-cache", HealthStatus.Healthy, "ok"));

        using (host)
        using (client)
        {
            (HttpStatusCode status, JsonDocument body) = await ProbeAsync(client);

            using (body)
            {
                Assert.Equal(HttpStatusCode.OK, status);

                JsonElement[] checks = [.. body.RootElement.GetProperty("checks").EnumerateArray()];

                Assert.Equal(2, checks.Length);

                foreach (JsonElement check in checks)
                {
                    Assert.Contains(check.GetProperty("name").GetString(), PermittedComponentNames);
                }

                JsonElement components = checks[1];

                Assert.Equal("components", components.GetProperty("name").GetString());
                Assert.Equal("Healthy", components.GetProperty("status").GetString());
                Assert.Equal(
                    "Every registered component readiness check reports ready.",
                    components.GetProperty("description").GetString());
            }
        }
    }

    /// <summary>
    /// The aggregated entry reports the WORST component verdict, not the first or the last.
    /// </summary>
    /// <remarks>
    /// Computed from the entries themselves rather than taken from the report's own aggregate, because
    /// the report's aggregate can be shaped by a registration's result predicate while this entry stands
    /// for what the checks actually said. The fixed not-ready note states THAT a component is not ready
    /// and never WHICH.
    /// </remarks>
    [Fact]
    public async Task TheAggregatedEntryReportsTheWorstComponentVerdict()
    {
        (IHost host, HttpClient client) = await StartAsync(
            ("keystore-a", HealthStatus.Healthy, "ok"),
            ("metadata-cache", HealthStatus.Degraded, "warming"),
            ("keystore-b", HealthStatus.Healthy, "ok"));

        using (host)
        using (client)
        {
            (HttpStatusCode status, JsonDocument body) = await ProbeAsync(client);

            using (body)
            {
                Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
                Assert.Contains(
                    "Degraded",
                    body.RootElement.GetProperty("detail").GetString()!,
                    StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// The report carries exactly the four members the contract declares, and nothing else.
    /// </summary>
    /// <remarks>
    /// Deserialized into the endpoint's own wire types, which is what makes the member set - rather
    /// than merely the values - the subject of the row.
    /// </remarks>
    [Fact]
    public async Task TheReportCarriesExactlyTheFourMembersTheContractDeclares()
    {
        (IHost host, HttpClient client) = await StartAsync(
            ("keystore-a", HealthStatus.Healthy, "ok"));

        using (host)
        using (client)
        {
            ServiceHealthReport? report = await client.GetFromJsonAsync<ServiceHealthReport>(
                HealthRoute,
                TestContext.Current.CancellationToken);

            Assert.NotNull(report);
            Assert.Equal("Healthy", report!.Status);
            Assert.Equal("security", report.Service);
            Assert.NotEqual(default, report.CheckedAt);
            Assert.NotEmpty(report.Checks);

            foreach (ServiceHealthCheck check in report.Checks)
            {
                Assert.Contains(check.Name, PermittedComponentNames);
                Assert.Contains(check.Status, (string[])["Healthy", "Degraded", "Unhealthy"]);
            }
        }
    }

    /// <summary>
    /// A check entry with no note omits the member rather than serializing an explicit null.
    /// </summary>
    /// <remarks>
    /// The authored contract lists only the name and the status as required on a check result and closes
    /// the object, so an explicit null would not validate against a schema whose type for the note is a
    /// plain string. The omit-when-null rule on the property is what makes "optional" mean absent.
    /// </remarks>
    [Fact]
    public void ACheckWithNoNoteSerializesWithoutTheDescriptionMember()
    {
        string json = JsonSerializer.Serialize(new ServiceHealthCheck("self", "Healthy"));

        Assert.DoesNotContain("description", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("null", json, StringComparison.OrdinalIgnoreCase);
    }

    // ==============================================================================================
    //  GROUP 5 - THE OPERATOR CHANNEL
    //  Where the per-check detail the anonymous body withholds actually goes.
    // ==============================================================================================

    /// <summary>
    /// The healthy operator record is trace level and names every check without its description.
    /// </summary>
    [Fact]
    public async Task TheHealthyOperatorRecordIsTraceLevelAndNamesEveryCheckWithoutItsDescription()
    {
        OperatorChannel channel = new();

        (IHost host, HttpClient client) = await StartCoreAsync(
            registerHealthCheckService: true,
            channel,
            healthCheckService: null,
            ("keystore-a", HealthStatus.Healthy, "path=/var/lib/powerframework/keys MUST-NOT-BE-LOGGED"),
            ("metadata-cache", HealthStatus.Healthy, "endpoint=https://cache.security.internal:6379"));

        using (host)
        using (client)
        {
            (HttpStatusCode status, JsonDocument body) = await ProbeAsync(client);
            body.Dispose();

            Assert.Equal(HttpStatusCode.OK, status);
        }

        (LogLevel level, string record) = Assert.Single(
            channel.Records,
            static r => r.Message.Contains("keystore-a", StringComparison.Ordinal));

        // BOTH names travel, with their verdicts, because WHICH component is which is the one thing the
        // anonymous response deliberately cannot say.
        Assert.Contains("keystore-a=Healthy", record, StringComparison.Ordinal);
        Assert.Contains("metadata-cache=Healthy", record, StringComparison.Ordinal);

        // NEITHER DESCRIPTION travels. A description is free text a registration authored, and a log
        // record is not exempt from the secrets discipline merely because its reader is trusted.
        Assert.DoesNotContain("MUST-NOT-BE-LOGGED", record, StringComparison.Ordinal);
        Assert.DoesNotContain("cache.security.internal", record, StringComparison.Ordinal);
        Assert.DoesNotContain("/var/lib", record, StringComparison.Ordinal);

        // The level is chosen from the verdict, so continuous probing of a healthy service does not fill
        // a log with records that say nothing happened.
        Assert.Equal(LogLevel.Trace, level);
    }

    /// <summary>
    /// The not-ready operator record is warning level. The refutation for the row above: the level is
    /// not a constant.
    /// </summary>
    [Fact]
    public async Task TheNotReadyOperatorRecordIsWarningLevel()
    {
        OperatorChannel channel = new();

        (IHost host, HttpClient client) = await StartCoreAsync(
            registerHealthCheckService: true,
            channel,
            healthCheckService: null,
            ("keystore-a", HealthStatus.Degraded, "warming"));

        using (host)
        using (client)
        {
            (HttpStatusCode status, JsonDocument body) = await ProbeAsync(client);
            body.Dispose();

            Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        }

        (LogLevel level, _) = Assert.Single(
            channel.Records,
            static r => r.Message.Contains("keystore-a=Degraded", StringComparison.Ordinal));

        Assert.Equal(LogLevel.Warning, level);
    }

    /// <summary>
    /// With the operator channel disabled the probe still answers, and writes nothing.
    /// </summary>
    /// <remarks>
    /// The endpoint tests the channel's level BEFORE joining the check names, so a disabled channel
    /// costs no allocation. This row is what keeps that early return reachable: no recording provider
    /// is installed, so the trace-level healthy record is suppressed by the copied configuration's
    /// default of Information.
    /// </remarks>
    [Fact]
    public async Task TheProbeAnswersWithTheOperatorChannelSuppressed()
    {
        (IHost host, HttpClient client) = await StartAsync(
            ("keystore-a", HealthStatus.Healthy, "ok"));

        using (host)
        using (client)
        {
            (HttpStatusCode status, JsonDocument body) = await ProbeAsync(client);

            using (body)
            {
                Assert.Equal(HttpStatusCode.OK, status);
            }
        }
    }


    // ==============================================================================================
    //  HOST HELPERS
    // ==============================================================================================

    /// <summary>
    /// Builds a host that maps the probe and registers the supplied component checks, then returns a
    /// client for it.
    /// </summary>
    /// <param name="checks">
    /// One entry per component check to register: its name, its verdict, and the description it
    /// reports. Several rows pass deliberately hostile values here - the kind of internal detail a real
    /// registration routinely leaks - so that a projection which echoed either would fail those rows
    /// rather than pass them.
    /// </param>
    /// <returns>The host and a client bound to it. The caller disposes both.</returns>
    private static async Task<(IHost Host, HttpClient Client)> StartAsync(
        params (string Name, HealthStatus Status, string Description)[] checks)
        => await StartCoreAsync(
            registerHealthCheckService: checks.Length > 0,
            channel: null,
            healthCheckService: null,
            checks);

    /// <summary>
    /// Builds a host whose readiness evaluation returns the supplied report verbatim.
    /// </summary>
    /// <param name="report">The report the endpoint will observe.</param>
    /// <returns>The host and a client bound to it. The caller disposes both.</returns>
    /// <remarks>
    /// Supplying the report directly is the only way to reach two states no registration can produce:
    /// an aggregate verdict outside the declared enumeration, and an aggregate that is not ready while
    /// every individual entry is.
    /// </remarks>
    private static async Task<(IHost Host, HttpClient Client)> StartWithReportAsync(HealthReport report)
        => await StartCoreAsync(
            registerHealthCheckService: false,
            channel: null,
            healthCheckService: new StubHealthCheckService(report));

    /// <summary>
    /// Builds the host, with the three axes the rows above need to vary independently.
    /// </summary>
    /// <param name="registerHealthCheckService">
    /// Whether the health check registry is enabled at all. This is a SEPARATE axis from the check
    /// list, because enabling the registry and registering a check are separate acts and the endpoint
    /// distinguishes them: with no registry it reports its own liveness, and with a registry but no
    /// check it aggregates an empty report. Collapsing the two would leave the empty-report arm
    /// unreachable from any row.
    /// </param>
    /// <param name="channel">
    /// When supplied, receives every record written to the operator channel together with the level
    /// each was written at, and forces the level filter low enough that the trace-level healthy record
    /// is actually emitted. Both halves are needed: the endpoint guards that record behind
    /// <c>IsEnabled</c>, and this project's copied <c>appsettings.json</c> pins the default category to
    /// Information - a configuration RULE, which beats <c>SetMinimumLevel</c>, so the rule set is
    /// cleared rather than merely lowered.
    /// </param>
    /// <param name="healthCheckService">
    /// When supplied, replaces the registry outright so a row can dictate the evaluated report or make
    /// the evaluation throw. Registered last, so it wins the single-service resolution the endpoint
    /// performs.
    /// </param>
    /// <param name="checks">The component checks to register, if any.</param>
    /// <returns>The host and a client bound to it. The caller disposes both.</returns>
    private static async Task<(IHost Host, HttpClient Client)> StartCoreAsync(
        bool registerHealthCheckService,
        OperatorChannel? channel,
        HealthCheckService? healthCheckService = null,
        params (string Name, HealthStatus Status, string Description)[] checks)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();

        if (channel is not null)
        {
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(new RecordingLoggerProvider(channel));
            builder.Logging.SetMinimumLevel(LogLevel.Trace);

            // The configuration-bound rules are CLEARED rather than overridden. A rule sourced from
            // appsettings wins over SetMinimumLevel, and this project's copy pins the default category
            // to Information - which would suppress the very record these rows exist to observe and
            // would make them pass by seeing nothing.
            builder.Logging.Services.Configure<LoggerFilterOptions>(static options =>
            {
                options.Rules.Clear();
                options.MinLevel = LogLevel.Trace;
            });
        }

        if (registerHealthCheckService)
        {
            IHealthChecksBuilder health = builder.Services.AddHealthChecks();

            foreach ((string name, HealthStatus status, string description) in checks)
            {
                health.AddCheck(name, () => new HealthCheckResult(status, description));
            }
        }

        if (healthCheckService is not null)
        {
            builder.Services.RemoveAll<HealthCheckService>();
            builder.Services.AddSingleton(healthCheckService);
        }

        WebApplication app = builder.Build();
        app.MapHealthEndpoints();

        await app.StartAsync(TestContext.Current.CancellationToken);

        return (app, app.GetTestClient());
    }

    /// <summary>Issues the probe and returns its status and parsed body.</summary>
    /// <param name="client">The client bound to the host under test.</param>
    /// <returns>The status code and the parsed body. The caller disposes the body.</returns>
    private static async Task<(HttpStatusCode Status, JsonDocument Body)> ProbeAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync(
            HealthRoute,
            TestContext.Current.CancellationToken);

        JsonDocument body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        return (response.StatusCode, body);
    }

    /// <summary>
    /// Replaces every cryptographic registration with a factory that throws on resolution.
    /// </summary>
    /// <param name="services">The service collection to poison.</param>
    /// <remarks>
    /// Each provider is a singleton and is therefore constructed lazily on first resolution, so a
    /// throwing factory turns "the handler resolved this" into an observable 500. The entropy source is
    /// included because it is the seam every randomness path composes over.
    /// </remarks>
    private static void PoisonEveryCryptographicRegistration(IServiceCollection services)
    {
        Poison<IEntropySource>(services);
        Poison<EncodingProvider>(services);
        Poison<HashProvider>(services);
        Poison<HmacProvider>(services);
        Poison<SymmetricCipherProvider>(services);
        Poison<RsaProvider>(services);
        Poison<RandomProvider>(services);
    }

    /// <summary>Replaces one registration with a factory that throws.</summary>
    /// <typeparam name="TService">The service type to poison.</typeparam>
    /// <param name="services">The service collection to poison.</param>
    private static void Poison<TService>(IServiceCollection services)
        where TService : class
    {
        services.RemoveAll<TService>();
        services.AddSingleton<TService>(static _ => throw new InvalidOperationException(
            "The readiness probe must not resolve any cryptographic service."));
    }

    /// <summary>
    /// The values the anonymous body may never carry: whatever the running host actually has configured,
    /// plus the generic secret shapes.
    /// </summary>
    /// <param name="configuration">The running host's own configuration.</param>
    /// <returns>Each forbidden fragment, skipping any that is not configured.</returns>
    /// <remarks>
    /// <para>
    /// READ FROM THE LIVE CONFIGURATION RATHER THAN TRANSCRIBED, for two reasons. A transcribed literal
    /// records what <c>appsettings.json</c> said on the day the row was written and stops testing
    /// anything once that file changes, whereas reading the host makes the row track the deployment. And
    /// the section and member names come from <see cref="SecurityOptions"/> itself, so nothing here
    /// re-spells a constant that type already owns.
    /// </para>
    /// <para>
    /// NONE OF THESE IS A CREDENTIAL, and that is deliberate: the signing key is supplied only through
    /// the environment and is absent from this list, because a row asserting on a real key would have to
    /// contain one. What is asserted is that the report projects no identity, metadata or key-reference
    /// value at all - the class of leak, not one instance of it.
    /// </para>
    /// </remarks>
    private static IEnumerable<string> ForbiddenBodyFragments(IConfiguration configuration)
    {
        IConfigurationSection security = configuration.GetSection(SecurityOptions.SectionName);

        string[] configured =
        [
            security[nameof(SecurityOptions.Issuer)] ?? string.Empty,
            security[nameof(SecurityOptions.SigningKeyId)] ?? string.Empty,
            security[nameof(SecurityOptions.SigningAlgorithm)] ?? string.Empty,
            security[nameof(SecurityOptions.JwksPath)] ?? string.Empty,
            security[nameof(SecurityOptions.OpenIdConfigurationPath)] ?? string.Empty,
            security[nameof(SecurityOptions.TokenEndpointPath)] ?? string.Empty,
            security.GetSection(nameof(SecurityOptions.KeyStore))[
                nameof(SecurityKeyStoreOptions.ConfigurationKeyPrefix)] ?? string.Empty,
        ];

        foreach (string value in configured)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }
        }

        // The environment variable the signing secret arrives through. Its NAME, never its value.
        yield return SecurityOptions.SigningKeyEnvironmentVariableName;

        // Generic secret shapes, so a future member that carried one is caught even if it is not a
        // configured value.
        yield return "keyRef";
        yield return "signingKey";
        yield return "BEGIN RSA";
        yield return "BEGIN PRIVATE";
        yield return "BEGIN CERTIFICATE";
    }
}

/// <summary>
/// A <see cref="WebApplicationFactory{TEntryPoint}"/> over the service's own composition root.
/// </summary>
/// <remarks>
/// The real host is what carries the default-deny fallback authorization policy, the published OpenAPI
/// document and the real configuration, so it is the only host on which the anonymity, document and
/// disclosure rows mean anything. Registration overrides run AFTER <c>Program.cs</c>, which is what lets
/// a row poison a registration the composition root installed.
/// </remarks>
internal sealed class SecurityHostFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// The signing material this host runs on. A DISTINCT PAIR PER HOST, generated in the constructor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SUPPLYING IT IS MANDATORY, NOT CONVENIENCE. <c>Program.cs</c> validates the configuration
    /// contract on start and REFUSES THE HOST when the signing material is absent, which is the legacy
    /// fail-fast posture [<c>ws_objects/pfw.pbl.src/pfw.sra:L111-L144</c>, ending in <c>HALT CLOSE</c> at
    /// <c>:L143</c>]. A factory that supplied none would therefore not boot at all, and every row below
    /// would fail on startup rather than on the property it exists to assert.
    /// </para>
    /// <para>
    /// It is applied through the OPTIONS PIPELINE rather than through the process environment, because
    /// that is the same ingress a deployment uses and it keeps the value out of an environment a sibling
    /// row could observe. The composition root's own resolution step reads the flat configuration key
    /// only when that key is PRESENT, so it never displaces what is supplied here.
    /// </para>
    /// </remarks>
    private readonly string _signingKey;

    private readonly Action<IServiceCollection>? _configure;

    /// <summary>Creates the factory over the unmodified composition root.</summary>
    public SecurityHostFactory()
        : this(configure: null)
    {
    }

    /// <summary>Creates the factory and applies a registration override.</summary>
    /// <param name="configure">The override to apply after the composition root has registered.</param>
    public SecurityHostFactory(Action<IServiceCollection>? configure)
    {
        _configure = configure;

        using RSA key = RSA.Create(2048);

        _signingKey = key.ExportPkcs8PrivateKeyPem();
    }

    /// <inheritdoc/>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        string signingKey = _signingKey;

        // THE ROSTER SECRETS ARE PART OF THE MINIMUM A HOST NEEDS IN ORDER TO START, not an extra. The
        // issuance registry resolves every secret the shipped roster names EAGERLY and refuses to construct
        // when one resolves to nothing, so a host that omitted them would fail during startup rather than on
        // the readiness or contract property a row is asserting. They come from
        // SecurityAppFactory.RosterSecretOverrides so this host and the four others in the assembly cannot
        // drift, and they are supplied HERE - in this host's own in-memory configuration - rather than in
        // the process environment, so nothing this host configures is observable by another test class.
        builder.ConfigureAppConfiguration(configuration =>
        {
            ArgumentNullException.ThrowIfNull(configuration);

            _ = configuration.AddInMemoryCollection(SecurityAppFactory.RosterSecretOverrides());
        });

        builder.ConfigureServices(services =>
            services.Configure<SecurityOptions>(options =>
            {
                options.SigningKey = signingKey;

                // THIS HOST MINTS A TOKEN FOR ITS OWN IDENTITY, SO IT MUST PERMIT ITSELF. The issuer
                // consults a permission matrix that decides which caller may address which audience with
                // which scopes, and the shipped settings file grants only the service-to-service call
                // graph - Gateway to DataServices, DataServices to Persistence and Security - because an
                // unused permission is still a permission. A deployment that wants Security's own identity
                // to reach Security's authenticated routes adds exactly the grant this installs.
                //
                // THE SUITE'S OWN MATRIX IS INSTALLED RATHER THAN A HAND-WRITTEN GRANT, so this host and
                // every other host in the assembly authorise the same callers for the same scopes. A
                // bespoke grant here would have to be revisited every time a row in this file asked for a
                // scope it did not anticipate, and the failure would read as a permission defect rather
                // than as a gap in the host's setup. It REPLACES both configured shapes, so no production
                // grant leaks into a row that was not meant to be using one.
                IssuanceFixture.PermitTestCallers(options);
            }));

        // Applied AFTER the signing material so that a row poisoning a registration still observes a
        // bootable host, and after the composition root so that a row can poison what it installed.
        if (_configure is not null)
        {
            builder.ConfigureServices(_configure);
        }
    }

    /// <summary>
    /// Creates a client carrying a bearer token this host itself minted.
    /// </summary>
    /// <returns>A client whose every request presents a valid token for this service.</returns>
    /// <remarks>
    /// <para>
    /// NEEDED BECAUSE THE DEFAULT-DENY FALLBACK POLICY GOVERNS EVERY ROUTE THAT DID NOT EXPLICITLY OPT
    /// OUT, and this service has exactly THREE anonymous routes - <c>/health</c> and the two
    /// <c>/.well-known/</c> publications. The generated contract document is not among them, so a row
    /// that reads it authenticates like any other caller.
    /// </para>
    /// <para>
    /// THE TOKEN IS MINTED THROUGH THE HOST'S OWN ISSUER rather than hand-assembled, so the issuer, the
    /// audience, the key identifier and the algorithm are by construction the ones the composition root
    /// configured its inbound handler to require. A hand-built token would be asserting the test's
    /// beliefs about that configuration instead of the configuration itself.
    /// </para>
    /// <para>
    /// The audience is this service's own identity, because that is the single audience the inbound
    /// handler accepts - a token addressed to another service in the roster is deliberately NOT
    /// replayable here.
    /// </para>
    /// </remarks>
    public HttpClient CreateAuthenticatedClient()
    {
        // TWO SCOPES, AND THE SECOND IS LOAD-BEARING. The C-02 group's authorization policy demands
        // `security.crypto`, so a client carrying only the document-reading scope reaches every
        // authenticated route in the service EXCEPT the cryptographic surface - where it is refused with
        // the published 403. The request-binding rows in JsonContractStrictnessTests post to a digest
        // operation through this helper, so it carries what they need. It is still not a wildcard: two
        // named scopes, both members of the suite's declared set, so a row asserting a scope refusal
        // mints its own narrower token rather than accidentally passing here.
        TokenIssuanceResult issued = Services
            .GetRequiredService<TokenIssuer>()
            .Issue(new TokenIssuanceRequest(
                subject: SelfAudience,
                audience: SelfAudience,
                scopes: [DocumentScope, SecurityScopes.Crypto]));

        Assert.Equal(TokenIssuanceOutcome.Issued, issued.Outcome);
        Assert.NotNull(issued.Token);

        HttpClient client = CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", issued.Token.AccessToken);

        return client;
    }

    /// <summary>This service's own identity, as its settings file declares it in both rosters.</summary>
    private const string SelfAudience = "powerframework-security";

    /// <summary>One scope, so that a request asks for something rather than for nothing.</summary>
    /// <remarks>
    /// A MEMBER OF THE SUITE'S OWN DECLARED SCOPE SET, so the harness matrix this host installs grants it.
    /// The routes that require a scope declare it themselves, and this one is the document-reading scope
    /// the contract rows use.
    /// </remarks>
    private const string DocumentScope = "contract.read";
}

/// <summary>
/// A readiness evaluation that returns a caller-supplied report verbatim.
/// </summary>
/// <remarks>
/// Two states no registration can produce are reachable only this way: an aggregate verdict outside the
/// declared enumeration, and an aggregate that is not ready while every individual entry is ready.
/// </remarks>
internal sealed class StubHealthCheckService : HealthCheckService
{
    private readonly HealthReport _report;

    /// <summary>Creates the double over the report it will return.</summary>
    /// <param name="report">The report every evaluation returns.</param>
    public StubHealthCheckService(HealthReport report) => _report = report;

    /// <inheritdoc/>
    public override Task<HealthReport> CheckHealthAsync(
        Func<HealthCheckRegistration, bool>? predicate,
        CancellationToken cancellationToken = default)
        => Task.FromResult(_report);
}

/// <summary>
/// A readiness evaluation that throws, so that the endpoint's report-rather-than-throw posture is
/// exercised rather than assumed.
/// </summary>
/// <remarks>
/// The message is deliberately shaped like the kind of internal detail such an exception really carries -
/// a key store path and a service account - so that a row asserting it never reaches the anonymous body
/// is testing the real hazard.
/// </remarks>
internal sealed class ThrowingHealthCheckService : HealthCheckService
{
    private readonly string _message;

    /// <summary>Creates the double over the failure message it will raise.</summary>
    /// <param name="message">The exception message.</param>
    public ThrowingHealthCheckService(string message) => _message = message;

    /// <inheritdoc/>
    public override Task<HealthReport> CheckHealthAsync(
        Func<HealthCheckRegistration, bool>? predicate,
        CancellationToken cancellationToken = default)
        => throw new InvalidOperationException(_message);
}

/// <summary>
/// The operator-channel records one host wrote, with the level each was written at.
/// </summary>
/// <remarks>
/// The level is captured because the endpoint chooses it from the verdict, and a record written at the
/// wrong level is a real defect that inspecting the message alone cannot detect: a healthy probe polled
/// once a second at warning level would bury the warnings that matter. Held per host rather than in
/// static state, so two rows can never observe each other's records.
/// </remarks>
internal sealed class OperatorChannel
{
    /// <summary>Every readiness record this host wrote, in order.</summary>
    public List<(LogLevel Level, string Message)> Records { get; } = [];
}

/// <summary>
/// A logging provider that appends every readiness record to an <see cref="OperatorChannel"/>.
/// </summary>
internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    /// <summary>
    /// The endpoint's own logger category. Selecting on it is what separates the endpoint's operator
    /// record from the framework's own health-check record, which names a check and its verdict too and
    /// therefore cannot be excluded by inspecting message text.
    /// </summary>
    private const string ReadinessCategory = "PowerFramework.Security.Endpoints.HealthEndpoints";

    private readonly OperatorChannel _channel;

    /// <summary>Creates the provider over the caller's channel.</summary>
    /// <param name="channel">The channel every record is appended to.</param>
    public RecordingLoggerProvider(OperatorChannel channel) => _channel = channel;

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) =>
        string.Equals(categoryName, ReadinessCategory, StringComparison.Ordinal)
            ? new RecordingLogger(_channel)
            : NullLogger.Instance;

    /// <inheritdoc/>
    public void Dispose()
    {
    }

    private sealed class RecordingLogger : ILogger
    {
        private readonly OperatorChannel _channel;

        public RecordingLogger(OperatorChannel channel) => _channel = channel;

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
            => NullLogger.Instance.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            _channel.Records.Add((logLevel, formatter(state, exception)));
        }
    }
}
