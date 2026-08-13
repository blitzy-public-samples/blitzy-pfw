// =================================================================================================
//  HealthAndPingEndpointTests - the two OPERATIONAL routes of the DataServices service, proved
//  together because they are each other's control.
//
//  Units under test: Endpoints/HealthEndpoints.cs and Endpoints/PingEndpoints.cs, exercised through
//  the deployed composition root over the in-process host published by DataServicesTestHostFactory.
//
//  WHY THE TWO ROUTES BELONG IN ONE SUITE
//  ------------------------------------------------------------------------------------------------
//  They are the only two routes on this service whose authentication postures are OPPOSITE, and each
//  one is the reason the other's assertion means anything:
//
//    * /health is ANONYMOUS, and it has to be. The thing that polls it - the orchestrator's health
//      condition, and Gateway's own readiness aggregate - holds no token, so requiring one would make
//      readiness depend on the very service being probed. A suite that only proved /health answers 200
//      would pass equally against a host that had made every route anonymous.
//    * /v1/ping REQUIRES A TOKEN and answers 401 without one, in every environment. A suite that only
//      proved the 401 would pass equally against a host that had broken authentication so thoroughly
//      that nothing could get in - including the probe.
//
//  Asserting both from one place is what distinguishes "correctly configured" from either failure, and
//  it is why the anonymous exception is asserted as an ENUMERATED exception rather than as a default.
//
//  WHAT THIS FILE EXISTS TO PROTECT, STATED AS THE REQUIREMENT RATHER THAN AS A ROUTE
//  ------------------------------------------------------------------------------------------------
//  The legacy PowerFramework opens no listening socket, registers no route and receives no unsolicited
//  request: it is an in-process library loaded into one PowerBuilder application object, whose whole
//  lifecycle is an open event, a close event and a system-error event
//  [ws_objects/pfw.pbl.src/pfw.sra:L88-L144]. Decomposition therefore creates every boundary in this
//  system from nothing, so "no new attack surface" cannot mean "no new surface" literally - it means
//  every newly created surface is AUTHENTICATED FROM THE OUTSET (constraint C-G). /v1/ping answering
//  401 without a credential is the reference implementation of that posture for this service, and the
//  requirement is that it be VERIFIED rather than assumed. This file is that verification.
//
//  It is verified on an INTERNAL edge, not a public one, and that is deliberate: DataServices sits
//  behind Gateway, and an internal edge that trusted its caller "because it is internal" would itself
//  be a new unauthenticated surface. Nothing about C-G is weaker one hop in.
//
//  SECURITY IS THE SOLE ISSUER, AND THIS SUITE MINTS NOTHING
//  ------------------------------------------------------------------------------------------------
//  Exactly one signing credential exists in the whole system and the Security service holds it
//  (AAP 0.6.6.3). DataServices holds VERIFICATION MATERIAL ONLY: it validates inbound tokens with the
//  stock Microsoft.AspNetCore.Authentication.JwtBearer handler pointed at Security's published key set
//  and discovery document, and it cannot mint a token. This file preserves that invariant
//  STRUCTURALLY rather than by review - it reaches the authenticated path through the fixture's test
//  authentication scheme, so no key, no token literal, no PEM block and no base64 key blob appears
//  here in any form, not in code and not in a comment (constraints C-F and C-G).
//
//  The anti-pattern is named in the plan and is worth naming again, because this is exactly the file
//  where somebody would reach for it: tests/blink/test_jws.htm embeds a plaintext RSA private key in a
//  legacy browser asset and signs a JWS with it. It is one of the eight in-source credential sites
//  recorded in docs/SECRETS.md, the posture toward every one of them is "never replicate, document and
//  rotate", and nothing resembling it may appear anywhere in the .NET tree. Section 4 below asserts
//  the absence rather than trusting it.
//
//  AGGREGATION IS GATEWAY'S, SO THIS PROBE IS LEAF-ONLY
//  ------------------------------------------------------------------------------------------------
//  Contract C-10 gives the AGGREGATE to Gateway alone: Gateway reports healthy only once Persistence,
//  DataServices and Security do. This service's probe therefore answers for ITSELF, makes no outbound
//  call, and must stay answerable when every upstream is unreachable - a leaf that reported itself
//  unready because an upstream was slow would make the Compose `depends_on: condition:
//  service_healthy` chain oscillate rather than settle. Section 1 asserts the absence of the outbound
//  call rather than inferring it from the status code.
//
//  NOTHING HERE TOUCHES A SOCKET, A DAEMON OR A DATABASE
//  ------------------------------------------------------------------------------------------------
//  Every host is in-process, every outbound edge is intercepted by the fixture's recording transport,
//  and no port is bound - which is what lets the whole file run with networking unavailable and with
//  no container runtime at all (constraints C-I and C-J; the plan records at AAP 0.6.7 R4 that Docker
//  was unavailable in the authoring environment, so container correctness is asserted by review and CI
//  while THIS level is asserted by execution). No test here measures anything: the repository
//  publishes no service-level agreement, no latency budget, no throughput target and no availability
//  commitment, so no timing assertion is written and none may be (AAP 0.8.5).
//
//  ONE NAMING CONSTRAINT, HONOURED BY REFERENCE ONLY
//  ------------------------------------------------------------------------------------------------
//  The legacy return-code identifiers keep their original screaming-snake spelling in C# because those
//  identifiers appear in serialized payloads, log records and characterization recordings (AAP
//  0.4.5.3), and .editorconfig suppresses the naming analyzers only on the files that CARRY them.
//  This file therefore REFERENCES RetCode members and DECLARES no such identifier of its own.
// =================================================================================================

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Endpoints;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// Proves the two operational routes of the DataServices service together: that <c>GET /health</c> is
/// anonymous, leaf-only and discloses nothing, and that <c>GET /v1/ping</c> is authenticated in every
/// environment with no bypass of any kind.
/// </summary>
/// <param name="host">
/// The shared in-process host. A CLASS fixture, so this suite's hosts are isolated from every other
/// class's and no scriptable double is shared across classes.
/// </param>
/// <remarks>
/// <para>
/// The two authentication postures both come from the fixture and are never re-implemented here.
/// <b>Posture A</b> - <c>CreateAnonymousClient</c> - presents no credential and travels the completely
/// untouched stock bearer pipeline, so a refusal it observes is the DEPLOYED refusal.
/// <b>Posture B</b> - <c>CreateAuthenticatedClient</c> - supplies an authenticated principal from an
/// explicit test scheme selected by the PRESENCE of a header, so the accepted path is reached without
/// minting, signing or holding anything.
/// </para>
/// <para>
/// Every awaited call that accepts a cancellation token is given
/// <c>TestContext.Current.CancellationToken</c>, because the repository treats warnings as errors and
/// the xunit analyzer raises the omission as one. That is not a formality here: a suite whose calls
/// could not be cancelled is a suite that can hang the run rather than fail it.
/// </para>
/// </remarks>
public sealed class HealthAndPingEndpointTests(DataServicesTestHostFactory host)
    : IClassFixture<DataServicesTestHostFactory>
{
    /// <summary>
    /// The anonymous readiness route, fixed by the attached environment's acceptance criteria
    /// (constraint C-L) as the per-service health path on the 5101-5105 band.
    /// </summary>
    /// <remarks>
    /// Written out rather than derived from the endpoint file's private constant, deliberately: this is
    /// the address the orchestration probe and Gateway's aggregate are given, so a change to it is a
    /// change a consumer sees, and a test that derived it from the implementation could not see it.
    /// </remarks>
    private const string HealthRoute = "/health";

    /// <summary>The authenticated liveness route, fixed by the same acceptance criteria.</summary>
    private const string PingRoute = "/v1/ping";

    /// <summary>
    /// Contract C-01's token-issuance operation, which belongs to the Security service ALONE.
    /// </summary>
    /// <remarks>
    /// Declared here so that its ABSENCE from this service can be asserted by name. The path is the one
    /// published in <c>shared/PowerFramework.Contracts/OpenApi/security.v1.yaml</c>.
    /// </remarks>
    private const string TokenIssuanceRoute = "/v1/tokens";

    /// <summary>The standard key-set publication path, which is Security's and not this service's.</summary>
    private const string KeySetRoute = "/.well-known/jwks.json";

    /// <summary>The standard discovery path, which is Security's and not this service's.</summary>
    private const string DiscoveryRoute = "/.well-known/openid-configuration";

    /// <summary>The address the generated contract document is served at.</summary>
    /// <remarks>
    /// The framework's default for a single unnamed document, and AUTHENTICATED on this service: the
    /// enumerated anonymous exceptions are <c>/health</c> on all four services and Security's two
    /// publication paths, and a generated description of this service's surface is not among them. Every
    /// row below that reads it therefore uses Posture B.
    /// </remarks>
    private const string DocumentRoute = "/openapi/v1.json";

    /// <summary>The media type every problem document in this estate is served as.</summary>
    private const string ProblemMediaType = "application/problem+json";

    /// <summary>The media type the two success bodies are served as.</summary>
    private const string JsonMediaType = "application/json";

    /// <summary>
    /// This service's own identifier on the wire, as both bodies report it.
    /// </summary>
    /// <remarks>
    /// Fixed by the authored contract rather than chosen: <c>OpenApi/gateway.v1.yaml</c> closes the
    /// reporting-service enumeration at <c>persistence</c>, <c>dataservices</c> and <c>security</c>.
    /// </remarks>
    private const string ServiceIdentifier = "dataservices";

    /// <summary>The configuration path of this service's REST listener.</summary>
    private const string RestEndpointUrlKey = "Kestrel:Endpoints:Rest:Url";

    /// <summary>
    /// The REST port the plan's port map gives this service, as it appears in a listener address.
    /// </summary>
    /// <remarks>
    /// AAP 0.3.2.2 assigns 5102 to DataServices, and the deployed manifest, the container definition and
    /// the documented probe all name it. No socket is bound under test - that is the point of an
    /// in-process host - so what is observable here is the CONFIGURED listener, which is the thing a
    /// deployment acts on.
    /// </remarks>
    private const string RestPortSuffix = ":5102";

    /// <summary>The name of the standard authorization header.</summary>
    private const string AuthorizationHeaderName = "Authorization";

    /// <summary>The bearer scheme name declared by the published contracts.</summary>
    private const string BearerSchemeName = "bearerAuth";

    /// <summary>
    /// The repository-root markers a source-file locator anchors on, so a walk cannot latch onto a
    /// same-named directory elsewhere on the machine.
    /// </summary>
    private const string SolutionMarkerFileName = "PowerFramework.slnx";

    /// <summary>The second root marker, and the file the package-pin assertions read.</summary>
    private const string PackageVersionMarkerFileName = "Directory.Packages.props";

    /// <summary>The application project's directory, relative to the repository root.</summary>
    private const string ApplicationProjectDirectory =
        "services/dataservices-service/PowerFramework.DataServices";

    /// <summary>This test project's directory, relative to the repository root.</summary>
    private const string TestProjectDirectory =
        "services/dataservices-service/PowerFramework.DataServices.Tests";

    /// <summary>
    /// The health-check package the plan forbids adding, because the abstractions ship inside the
    /// <c>Microsoft.AspNetCore.App</c> shared framework (AAP 0.5.3).
    /// </summary>
    private const string ForbiddenHealthCheckPackage = "Microsoft.Extensions.Diagnostics.HealthChecks";

    /// <summary>The token-MINTING library, which neither project in this service may reference.</summary>
    /// <remarks>
    /// Its sibling <c>Microsoft.IdentityModel.Tokens</c> is a different assembly and is legitimately
    /// present: the stock bearer handler's configuration manager types live there, and configuring
    /// VALIDATION is the opposite of acquiring the ability to MINT. Only the minting library is named
    /// here, so the assertion cannot be satisfied or broken by the validation stack.
    /// </remarks>
    private const string MintingLibraryName = "Microsoft.IdentityModel.JsonWebTokens";

    /// <summary>The wire token a fully ready service reports, and the only one answered with 200.</summary>
    private const string ReadyStatusToken = "Healthy";

    /// <summary>
    /// The complete public vocabulary the anonymous report may name a component with.
    /// </summary>
    /// <remarks>
    /// A CLOSED set authored by the endpoint file, never a registration's own check name and never one
    /// entry per registration: an anonymous body must disclose neither what a registration called itself
    /// nor how many registrations exist. Restated here rather than read from the implementation, because
    /// a test that derived the permitted set from the code under test could not detect the set widening.
    /// </remarks>
    private static readonly string[] PermittedCheckNames = ["self", "credentials", "components"];

    /// <summary>The complete member set of the readiness report, as the contract declares it.</summary>
    private static readonly string[] ReportMembers = ["status", "service", "checkedAt", "checks"];

    /// <summary>The complete member set of one component entry.</summary>
    private static readonly string[] ReportCheckMembers = ["name", "status", "description"];

    /// <summary>The complete member set of the ping body.</summary>
    private static readonly string[] PingMembers = ["service", "authenticated", "retCode", "timestamp"];

    // ==============================================================================================
    //  SECTION 1 - GET /health: ANONYMOUS, LEAF-ONLY, AND SILENT ABOUT EVERYTHING ELSE
    // ==============================================================================================

    /// <summary>
    /// A caller presenting no credential at all is answered, and answered with the readiness body.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE ACCEPTANCE CRITERION THE ATTACHED ENVIRONMENT STATES (constraint C-L), and the property the
    /// whole orchestration readiness chain rests on: the thing that polls this route holds no token, so a
    /// probe that required one would make readiness depend on the service being probed and the Compose
    /// dependency gate could never open. The absence of the header is asserted on the CLIENT as well as
    /// the outcome on the response, because a client that had silently acquired a default credential
    /// would make this row prove nothing.
    /// </remarks>
    [Fact]
    public async Task TheReadinessProbeAnswersACallerThatPresentsNoCredential()
    {
        using HttpClient anonymous = host.CreateAnonymousClient();

        Assert.Null(anonymous.DefaultRequestHeaders.Authorization);

        Assert.DoesNotContain(
            AuthorizationHeaderName,
            anonymous.DefaultRequestHeaders.Select(static header => header.Key),
            StringComparer.OrdinalIgnoreCase);

        using HttpResponseMessage response = await anonymous.GetAsync(
            Relative(HealthRoute),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonMediaType, response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// The report names THIS service, reports it ready, and timestamps itself from the substitutable
    /// clock rather than from the wall clock.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE STATUS CODE ALONE IS NOT THE CONTRACT. Gateway's aggregate names each upstream individually,
    /// so the reporting service has to be identifiable from the body; and only <c>Healthy</c> is answered
    /// with 200, so the token is asserted alongside the code rather than assumed to follow from it.
    /// </para>
    /// <para>
    /// THE INSTANT IS ASSERTED AGAINST THE FIXTURE'S FROZEN CLOCK, which is a parity assertion rather
    /// than a cosmetic one: the characterization model requires every clock read to be maskable from
    /// both the master and the candidate recording (AAP 0.6.7), so the handler must resolve
    /// <c>TimeProvider</c> from request services instead of reading the system clock. A handler that read
    /// the system clock would answer an instant that moved, and this row is what detects it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheReadinessReportNamesThisServiceAndTimestampsItselfFromTheSubstitutedClock()
    {
        using HttpClient anonymous = host.CreateAnonymousClient();

        ServiceHealthReport? report = await anonymous.GetFromJsonAsync<ServiceHealthReport>(
            Relative(HealthRoute),
            TestContext.Current.CancellationToken);

        Assert.NotNull(report);
        Assert.Equal(ReadyStatusToken, report.Status);
        Assert.Equal(ServiceIdentifier, report.Service);
        Assert.Equal(DeterministicTimeProvider.DefaultInstant, report.CheckedAt);
        Assert.NotEmpty(report.Checks);

        Assert.All(
            report.Checks,
            check => Assert.Contains(check.Name, PermittedCheckNames, StringComparer.Ordinal));
    }

    /// <summary>
    /// The probe answers without making a single outbound call, so it can never be gated on an upstream.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// CONTRACT C-10 GIVES THE AGGREGATE TO GATEWAY ALONE, and this is the row that holds the line. A
    /// leaf service that probed Persistence or Security in order to answer for itself would report itself
    /// unready whenever an upstream was merely slow, which makes the Compose
    /// <c>depends_on: condition: service_healthy</c> chain oscillate instead of settle - and it would do
    /// so while every status code still looked plausible. Asserting the ABSENCE of the call is the only
    /// way to see that; a status-code row cannot.
    /// </para>
    /// <para>
    /// The count is compared as a DELTA rather than against zero. Tests within a class run sequentially
    /// against one shared host, so a row that demanded an absolute zero would be asserting something
    /// about its neighbours instead of about the probe. The three named paths are additionally asserted
    /// empty over the whole run, because those three - the key set, the discovery document and the
    /// issuance operation - are the calls whose appearance here would mean something structural had gone
    /// wrong rather than merely unexpected.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheReadinessProbeMakesNoOutboundCallOfItsOwn()
    {
        using HttpClient anonymous = host.CreateAnonymousClient();

        int before = host.Transport.RequestCount;

        using HttpResponseMessage response = await anonymous.GetAsync(
            Relative(HealthRoute),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(before, host.Transport.RequestCount);

        Assert.Empty(host.Transport.ExchangesFor(KeySetRoute));
        Assert.Empty(host.Transport.ExchangesFor(DiscoveryRoute));
        Assert.Empty(host.Transport.ExchangesFor(TokenIssuanceRoute));
    }

    /// <summary>
    /// A host whose every upstream is unreachable still boots, still serves, and still answers the probe.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// READINESS MUST NOT GATE STARTUP, which is a different property from the row above and fails
    /// differently. This one constructs its own host rather than borrowing the class fixture, so
    /// "constructs with unreachable upstreams and boots anyway" is literally what is exercised: the
    /// authority is an address in the reserved <c>.invalid</c> top-level domain that resolves nowhere, the
    /// two upstream addresses are loopback addresses nothing is listening on, and the fixture's transport
    /// has no scripted route, so any outbound call would THROW rather than answer.
    /// </para>
    /// <para>
    /// THE ADDRESSES ARE READ FROM BOUND OPTIONS RATHER THAN RESTATED. A row that hardcoded them would
    /// still pass if the fixture were later pointed at something real, which is exactly the change that
    /// would invalidate the claim.
    /// </para>
    /// <para>
    /// THE FAIL-FAST POSTURE IS NOT WEAKENED BY THIS, AND THE DISTINCTION MATTERS. The legacy terminates
    /// on a STRUCTURAL fault - its system-error event unpacks a seven-field assert payload and then halts
    /// [ws_objects/pfw.pbl.src/pfw.sra:L111-L144] - and the target reproduces that as fail-fast startup
    /// validation, which this fixture never suppresses. An unreachable upstream is not a structural
    /// fault: it is a runtime condition the orchestration layer resolves by starting the other container.
    /// Refusing to start on it would deadlock the dependency chain rather than protect anything.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ReadinessNeverGatesStartupEvenWithEveryUpstreamUnreachable()
    {
        await using DataServicesTestHostFactory isolated = new();

        DataServicesOptions options = isolated.Services
            .GetRequiredService<IOptions<DataServicesOptions>>()
            .Value;

        JwtAuthenticationOptions authentication = isolated.Services
            .GetRequiredService<IOptions<JwtAuthenticationOptions>>()
            .Value;

        Assert.True(
            new Uri(options.Persistence.Address, UriKind.Absolute).IsLoopback,
            "The Persistence address must be one nothing is listening on, or this row proves nothing.");

        Assert.True(
            new Uri(options.Security.BaseAddress, UriKind.Absolute).IsLoopback,
            "The Security address must be one nothing is listening on, or this row proves nothing.");

        Assert.Equal(DataServicesTestHostFactory.ConfiguredAuthority, authentication.Authority);

        Assert.EndsWith(
            ".invalid",
            new Uri(authentication.Authority, UriKind.Absolute).Host,
            StringComparison.Ordinal);

        using HttpClient anonymous = isolated.CreateAnonymousClient();

        int before = isolated.Transport.RequestCount;

        using HttpResponseMessage response = await anonymous.GetAsync(
            Relative(HealthRoute),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before, isolated.Transport.RequestCount);
    }

    /// <summary>
    /// The probe stays anonymous under the test authentication scheme as well, and answers a caller that
    /// presents a principal identically.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// TWO FAILURES THIS CATCHES, IN OPPOSITE DIRECTIONS. A scheme registration that made the probe
    /// require credentials would break the readiness gate while every ping row still passed; and a probe
    /// that varied its answer by who asked would be reporting something about the CALLER on an anonymous
    /// route. Both statuses are asserted, and so is the equality of the two verdicts.
    /// </remarks>
    [Fact]
    public async Task TheReadinessProbeStaysAnonymousUnderThePrincipalSchemeToo()
    {
        using HttpClient anonymous = host.CreateAnonymousClient();
        using HttpClient authenticated = host.CreateAuthenticatedClient();

        using HttpResponseMessage withoutPrincipal = await anonymous.GetAsync(
            Relative(HealthRoute),
            TestContext.Current.CancellationToken);

        using HttpResponseMessage withPrincipal = await authenticated.GetAsync(
            Relative(HealthRoute),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, withoutPrincipal.StatusCode);
        Assert.Equal(withoutPrincipal.StatusCode, withPrincipal.StatusCode);
    }

    /// <summary>
    /// The route the service actually maps is the one <c>MapHealthEndpoints</c> declares, and its
    /// anonymity is a DECLARATION rather than an accident of policy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A STATUS-CODE ROW CANNOT SEE THIS. An anonymous 200 would also be produced by a host that had
    /// removed its fallback policy altogether, and that host would answer every other route anonymously
    /// too. What makes the probe's anonymity safe is that it is the ONE endpoint carrying explicit
    /// allow-anonymous metadata, which survives a later global tightening rather than depending on the
    /// absence of one.
    /// </para>
    /// <para>
    /// The operation identifier and the tag are asserted because the generated contract document is built
    /// from this metadata: they are what a consumer reads, so a rename is a consumer-visible change.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheProbeIsTheRouteMapHealthEndpointsDeclaredAndIsAnonymousByDeclaration()
    {
        RouteEndpoint probe = RequireEndpoint(HealthRoute);

        Assert.NotNull(probe.Metadata.GetMetadata<IAllowAnonymous>());
        Assert.Empty(probe.Metadata.GetOrderedMetadata<IAuthorizeData>());

        Assert.Equal(
            "getHealth",
            probe.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);

        Assert.Contains(
            "Health",
            probe.Metadata.GetMetadata<ITagsMetadata>()?.Tags ?? [],
            StringComparer.Ordinal);

        Assert.Contains(
            HttpMethods.Get,
            probe.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [],
            StringComparer.Ordinal);
    }

    /// <summary>
    /// The readiness evaluator this service uses comes from the shared framework, and no health-check
    /// package is referenced or pinned anywhere.
    /// </summary>
    /// <remarks>
    /// <para>
    /// BOTH HALVES ARE NEEDED. The runtime half proves the abstractions are genuinely available without a
    /// package: the evaluator type resolves, and its assembly sits in the same directory as the framework
    /// assembly that carries the host itself rather than next to this test assembly, which is where a
    /// package asset would have been copied. The declaration half proves nobody added one anyway, and it
    /// reads the actual XML elements rather than searching the text - both project files DISCUSS the
    /// omission in comments, so a substring search would report the explanation as the violation.
    /// </para>
    /// <para>
    /// AAP 0.5.3 lists the health-check packages among the deliberate exclusions precisely because
    /// registration and evaluation ship in <c>Microsoft.AspNetCore.App</c>. Adding one would be
    /// unrequested dependency surface for capability the platform already provides.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheReadinessEvaluatorComesFromTheSharedFrameworkRatherThanFromAPackage()
    {
        Assert.NotNull(host.Services.GetService<HealthCheckService>());

        string? evaluatorDirectory = Path.GetDirectoryName(typeof(HealthCheckService).Assembly.Location);
        string? hostingDirectory = Path.GetDirectoryName(typeof(IHost).Assembly.Location);

        Assert.False(string.IsNullOrEmpty(evaluatorDirectory));
        Assert.Equal(hostingDirectory, evaluatorDirectory);

        Assert.NotEqual(
            Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory),
            Path.TrimEndingDirectorySeparator(evaluatorDirectory!));

        IReadOnlyList<string> rootPins = DeclaredPackageIdentities(
            RequireRepositoryFileText(PackageVersionMarkerFileName),
            "PackageVersion");

        IReadOnlyList<string> applicationPackages = DeclaredPackageIdentities(
            RequireRepositoryFileText(
                $"{ApplicationProjectDirectory}/PowerFramework.DataServices.csproj"),
            "PackageReference");

        IReadOnlyList<string> testPackages = DeclaredPackageIdentities(
            RequireRepositoryFileText(
                $"{TestProjectDirectory}/PowerFramework.DataServices.Tests.csproj"),
            "PackageReference");

        Assert.DoesNotContain(ForbiddenHealthCheckPackage, rootPins, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            ForbiddenHealthCheckPackage,
            applicationPackages,
            StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(ForbiddenHealthCheckPackage, testPackages, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The anonymous body carries none of the fragments an anonymous body must never carry.
    /// </summary>
    /// <param name="fragment">The fragment that must be absent.</param>
    /// <param name="why">Why its presence would be a disclosure, surfaced in the failure message.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THIS ROUTE IS WORLD-READABLE BY ANYTHING THAT CAN REACH THE PORT, so every member of its body is
    /// published to all of it. The sweep is expressed as rows rather than as one long assertion because
    /// each row is a separate claim with a separate reason, and a single combined assertion would report
    /// only the first violation it found.
    /// </para>
    /// <para>
    /// The rows are compile-time constants ON PURPOSE. Values the fixture generates at run time - the
    /// issuance credential above all - are asserted in a Fact of their own instead, because theory data is
    /// pre-enumerated and a row whose identity changed between discovery and execution would fail for a
    /// reason that has nothing to do with the code under test.
    /// </para>
    /// <para>
    /// The comparison is case-insensitive, since a disclosure spelled differently is still a disclosure.
    /// Words that legitimately appear in the endpoint's own FIXED PROSE - the component named
    /// <c>credentials</c>, and its note about obtaining a service token - are deliberately not among the
    /// rows: they are authored English describing a capability, and forbidding them would be asserting
    /// against the contract rather than against a leak.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(FragmentsTheAnonymousReportMayNotCarry))]
    public async Task TheAnonymousReadinessReportDisclosesNothingSensitive(string fragment, string why)
    {
        using HttpClient anonymous = host.CreateAnonymousClient();

        using HttpResponseMessage response = await anonymous.GetAsync(
            Relative(HealthRoute),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.False(
            body.Contains(fragment, StringComparison.OrdinalIgnoreCase),
            $"The anonymous readiness body carries '{fragment}', which is {why}. The route is "
            + "world-readable by anything that can reach the port, so the member set is a publication "
            + "decision rather than a formatting one.");
    }

    /// <summary>
    /// The anonymous body carries none of the values this deployment was configured with.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE STRONGEST SECRET ASSERTION AVAILABLE HERE, and the fixture publishes the credential for
    /// exactly this purpose: the value is generated once per test process rather than written down, so
    /// asserting its absence by NAME is possible while asserting anything about its content is not. A
    /// weaker sweep for "anything base64-shaped" could not distinguish a passing run from a leak of a
    /// differently-shaped secret.
    /// </para>
    /// <para>
    /// The addresses are read from BOUND OPTIONS rather than restated, so the claim tracks whatever this
    /// host was actually pointed at. The audience is included because it names a deployment, and an
    /// anonymous route that echoed it would let a caller enumerate the token topology from outside.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheAnonymousReadinessReportEchoesNoConfiguredValue()
    {
        using HttpClient anonymous = host.CreateAnonymousClient();

        using HttpResponseMessage response = await anonymous.GetAsync(
            Relative(HealthRoute),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        DataServicesOptions options = host.Services
            .GetRequiredService<IOptions<DataServicesOptions>>()
            .Value;

        Assert.DoesNotContain(
            DataServicesTestHostFactory.ConfiguredIssuanceSecret,
            body,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            DataServicesTestHostFactory.ConfiguredAuthority,
            body,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            DataServicesTestHostFactory.ConfiguredAudience,
            body,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(options.Persistence.Address, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(options.Security.BaseAddress, body, StringComparison.OrdinalIgnoreCase);

        AssertCarriesNoKeyMaterialMarker(body, HealthRoute);
    }

    /// <summary>
    /// The anonymous body carries EXACTLY the members the contract declares, and no others.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE COMPLEMENT OF THE SWEEP ABOVE, AND THE HALF THAT SCALES. A forbidden-fragment list can only
    /// forbid what somebody thought of; a closed member set forbids everything else at once, which is
    /// what catches a member added later carrying a value nobody classified. It is asserted on the raw
    /// JSON rather than on the deserialized record, because deserialization would silently discard an
    /// unexpected member and report a clean shape.
    /// </remarks>
    [Fact]
    public async Task TheAnonymousReadinessReportCarriesNoMemberBeyondTheContract()
    {
        using HttpClient anonymous = host.CreateAnonymousClient();

        using HttpResponseMessage response = await anonymous.GetAsync(
            Relative(HealthRoute),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.All(
            document.RootElement.EnumerateObject(),
            member => Assert.Contains(member.Name, ReportMembers, StringComparer.Ordinal));

        Assert.All(
            document.RootElement.GetProperty("checks").EnumerateArray(),
            check => Assert.All(
                check.EnumerateObject(),
                member => Assert.Contains(member.Name, ReportCheckMembers, StringComparer.Ordinal)));
    }

    // ==============================================================================================
    //  SECTION 2 - GET /v1/ping WITHOUT A TOKEN: 401, EVERY TIME, IN EVERY ENVIRONMENT
    // ==============================================================================================

    /// <summary>
    /// An anonymous caller is refused with EXACTLY 401, and the challenge comes from the stock handler.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE REFERENCE IMPLEMENTATION OF CONSTRAINT C-G ON THIS SERVICE, and the acceptance criterion the
    /// attached environment states verbatim (constraint C-L). The status is asserted as an EQUALITY rather
    /// than as "not 200", because each near miss means something different and all three are wrong: 403
    /// would tell an unauthenticated caller its permissions were the problem, 404 would mean the route was
    /// never mapped so nothing was proved, and 500 would mean the pipeline faulted while refusing - which
    /// is a denial-of-service surface rather than an authentication boundary.
    /// </para>
    /// <para>
    /// THE CHALLENGE HEADER IS THE PROVENANCE ASSERTION. A 401 could be produced by anything in the
    /// pipeline; a <c>WWW-Authenticate: Bearer</c> challenge is produced by the stock bearer handler
    /// registered in <c>Program.cs</c>. Asserting it is how this row shows the refusal came from the
    /// DEPLOYED pipeline rather than from anything in the test fixture - with the test header absent, the
    /// fixture's scheme selector forwards to the bearer handler and touches nothing.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnAnonymousPingIsRefusedWithExactlyFourHundredAndOne()
    {
        using HttpClient anonymous = host.CreateAnonymousClient();

        using HttpResponseMessage response = await anonymous.GetAsync(
            Relative(PingRoute),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        Assert.Contains(
            response.Headers.WwwAuthenticate,
            challenge => string.Equals(
                challenge.Scheme,
                JwtBearerDefaults.AuthenticationScheme,
                StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The refusal is produced without fetching key-set or discovery metadata.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// TWO INDEPENDENT PROOFS, BECAUSE NEITHER IS SUFFICIENT ALONE. The recorded transport shows no
    /// outbound request was made through the channel every client registration resolves - but the bearer
    /// handler's metadata backchannel is built by the handler itself and does not travel that channel, so
    /// the count alone could not see a key-set fetch.
    /// </para>
    /// <para>
    /// The second proof is the STATUS ITSELF, and it is the decisive one: this host's authority is an
    /// address in the reserved <c>.invalid</c> top-level domain that resolves nowhere, so a handler that
    /// tried to resolve metadata in order to refuse an anonymous request could not have answered 401 at
    /// all. The stock handler resolves metadata LAZILY, on the first request that actually needs a key,
    /// and a request carrying no token needs none - so it short-circuits before any address is contacted.
    /// That is what makes the mandated 401 hold with Security unreachable and with networking
    /// unavailable, which is the whole basis of constraints C-I and C-J.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheRefusalCostsNoKeySetOrDiscoveryFetch()
    {
        using HttpClient anonymous = host.CreateAnonymousClient();

        int before = host.Transport.RequestCount;

        using HttpResponseMessage response = await anonymous.GetAsync(
            Relative(PingRoute),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        Assert.Equal(before, host.Transport.RequestCount);

        Assert.Empty(host.Transport.ExchangesFor(KeySetRoute));
        Assert.Empty(host.Transport.ExchangesFor(DiscoveryRoute));
    }

    /// <summary>
    /// A malformed authorization header is REFUSED rather than faulted.
    /// </summary>
    /// <param name="header">The raw header value to present.</param>
    /// <param name="shape">What that value is, surfaced in the failure message.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE DISTINCTION BETWEEN 401 AND 500 IS THE WHOLE POINT. A boundary that answered 500 to a
    /// syntactically broken header would turn a rejected request into an unhandled fault, which is
    /// reachable by anyone who can send a header and is therefore a surface rather than a refusal. Every
    /// row here is a value the stock handler must recognise as "no usable credential present" and refuse
    /// without touching an issuer.
    /// </para>
    /// <para>
    /// EVERY ROW IS DELIBERATELY A SHAPE THAT CARRIES NO BEARER VALUE - no scheme at all, the scheme with
    /// nothing after it, the scheme with only whitespace after it, or a different scheme entirely. A row
    /// carrying a fabricated bearer VALUE would exercise the token VALIDATION path, which needs the
    /// issuer's key set and is therefore a different property with a different owner; it is asserted
    /// separately below, where the claim is only that such a value never gets IN.
    /// </para>
    /// <para>
    /// The values are added WITHOUT validation on purpose: the message pipeline would refuse to attach
    /// several of them otherwise, and a row that could not be sent could not be a row. None of them is a
    /// credential - each is a syntactic shape, and no key, token or signature value appears here.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(MalformedAuthorizationHeaders))]
    public async Task AMalformedAuthorizationHeaderIsRefusedRatherThanFaulted(string header, string shape)
    {
        using HttpClient anonymous = host.CreateAnonymousClient();

        using HttpRequestMessage request = new(HttpMethod.Get, Relative(PingRoute));

        Assert.True(
            request.Headers.TryAddWithoutValidation(AuthorizationHeaderName, header),
            $"The row for {shape} could not be sent at all, so it would have proved nothing.");

        using HttpResponseMessage response = await anonymous.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// A fabricated bearer value is refused rather than faulted, even with the issuer unreachable.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE ONE ROW THAT REACHES THE VALIDATION PATH, AND THE ONE WHERE A FAULT WOULD HAVE BEEN EASY. Unlike
    /// the malformed shapes above, this value IS a bearer value, so the stock handler tries to validate it -
    /// and validating it requires the issuer's key set, which on this host is behind an address that
    /// resolves nowhere. An implementation that let an unresolvable issuer surface as an exception would
    /// answer 500, which would hand any caller a fault path reachable with one header. It answers 401
    /// instead: the handler hands its configuration manager to the validator, which reports a failure
    /// rather than throwing, so an unreachable issuer means "cannot admit this token" rather than "this
    /// request broke the service". That distinction is asserted here because it is the difference between
    /// a boundary and a denial-of-service surface.
    /// </para>
    /// <para>
    /// WHAT IS NOT ASSERTED is WHICH check failed. Signature, issuer, audience and lifetime validation all
    /// belong to the stock handler pointed at Security, whose published key set is the only thing that
    /// could ever admit a token - and Security is the sole issuer in the system (AAP 0.6.6.3). Nothing in
    /// this assembly can produce a value that would satisfy it, and that is the invariant rather than a
    /// limitation of the test.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AFabricatedBearerValueIsRefusedRatherThanFaulted()
    {
        using HttpClient anonymous = host.CreateAnonymousClient();

        using HttpRequestMessage request = new(HttpMethod.Get, Relative(PingRoute));

        // Three dot-separated words. Not a token, not derived from one, and not capable of validating
        // against anything: it is the SHAPE a caller would present, which is all this row needs.
        request.Headers.TryAddWithoutValidation(
            AuthorizationHeaderName,
            $"{JwtBearerDefaults.AuthenticationScheme} not.a.token");

        using HttpResponseMessage response = await anonymous.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.IsSuccessStatusCode);
    }

    /// <summary>
    /// The refusal holds in the Development environment exactly as in Production.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE SINGLE EASIEST WAY FOR THIS POSTURE TO REGRESS is a development convenience, so it is the one
    /// asserted with its own host. <c>Endpoints/PingEndpoints.cs</c> applies its authorization requirement
    /// unconditionally - no environment test, no anonymous fallback, no configuration switch, no header
    /// escape - and <c>appsettings.Development.json</c> overrides only addresses and idle timeouts while
    /// relaxing no validation switch. A bypass that exists only in Development is still a code path that
    /// ships, and one that is only ever exercised where nobody is watching.
    /// </para>
    /// <para>
    /// The environment is asserted BEFORE the status, because a row that silently booted Production would
    /// pass while proving nothing about Development at all.
    /// </para>
    /// <para>
    /// The probe is asserted in the same host, because the two properties are each other's control: the
    /// Development overlay must relax the ping route no more than the Production one, and must not tighten
    /// the anonymous probe either.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheRefusalHoldsInTheDevelopmentEnvironmentToo()
    {
        await using DataServicesTestHostFactory development = DataServicesTestHostFactory.ForDevelopment();

        Assert.Equal(Environments.Development, development.EnvironmentName);

        using HttpClient anonymous = development.CreateAnonymousClient();

        using HttpResponseMessage refused = await anonymous.GetAsync(
            Relative(PingRoute),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

        Assert.Equal(
            Environments.Development,
            development.Services
                .GetRequiredService<IHostEnvironment>()
                .EnvironmentName);

        using HttpResponseMessage probe = await anonymous.GetAsync(
            Relative(HealthRoute),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, probe.StatusCode);
    }

    /// <summary>
    /// The ping route carries authorization metadata and no anonymous escape, in either environment.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE STRUCTURAL COMPANION TO THE STATUS ROWS, and it catches what they cannot: a status row proves
    /// what happened on one request, whereas absent allow-anonymous metadata proves there is no path by
    /// which the requirement could be skipped. Both environments are inspected because a
    /// conditionally-applied requirement would be invisible in the one where it was applied.
    /// </para>
    /// <para>
    /// THE DECLARED AUTHORIZATION NAMES NO POLICY, AND THAT IS DELIBERATE RATHER THAN INCOMPLETE. The
    /// parameterless requirement resolves to the application's DEFAULT policy, which is exactly "an
    /// authenticated principal" - the property this route exists to prove. Naming a policy here would
    /// couple the route to a definition it does not own, and the transformation plan publishes no scope
    /// for ping precisely because it performs no work and reaches no capability, so inventing one would be
    /// a new requirement rather than a preserved one (constraint C-B).
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ThePingRouteCarriesNoAnonymousEscapeInEitherEnvironment()
    {
        AssertPingIsAuthorizedByDeclaration(RequireEndpoint(PingRoute));

        await using DataServicesTestHostFactory development = DataServicesTestHostFactory.ForDevelopment();

        AssertPingIsAuthorizedByDeclaration(RequireEndpoint(PingRoute, development));
    }

    /// <summary>
    /// The route's declared policy is exactly "an authenticated principal", and the caller that fails it
    /// is refused.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE REQUIREMENT SET IS READ FROM THE POLICY PROVIDER RATHER THAN INFERRED. The default policy this
    /// route resolves to must carry the deny-anonymous requirement and nothing else: an additional
    /// requirement would mean the route silently demanded something the contract never published, and a
    /// missing one would mean it demanded nothing at all while still looking authorized.
    /// </para>
    /// <para>
    /// AND THE PRINCIPAL THAT FAILS IT IS REFUSED, which is the same row's other half. The requirement
    /// deny-anonymous is failed by exactly one caller - the one presenting no principal - so that caller
    /// is the negative control, and it is answered 401 rather than reaching the body.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheDeclaredPolicyIsAuthenticationAloneAndTheCallerFailingItIsRefused()
    {
        AuthorizationPolicy? declared = await host.Services
            .GetRequiredService<IAuthorizationPolicyProvider>()
            .GetDefaultPolicyAsync();

        Assert.NotNull(declared);

        Assert.Single(declared.Requirements);
        Assert.IsType<DenyAnonymousAuthorizationRequirement>(Assert.Single(declared.Requirements));

        using HttpClient anonymous = host.CreateAnonymousClient();

        using HttpResponseMessage refused = await anonymous.GetAsync(
            Relative(PingRoute),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    /// <summary>
    /// The refusal's body is a problem document whose return code comes from the legacy vocabulary.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE STATUS WAS ALWAYS RIGHT AND A BODY IS WHAT MAKES IT ACTIONABLE. The challenge is produced
    /// BENEATH any of this service's own code - by the authorization middleware - so the body that
    /// accompanies it is the host's, and the host is configured to write one. Where it does, the shape is
    /// asserted rather than assumed.
    /// </para>
    /// <para>
    /// THE RETURN CODE MUST COME FROM THE PORTED ALGEBRA, NOT FROM AN AD-HOC NUMBER. The legacy declares
    /// exactly one access code and draws no distinction between "no credential" and "credential without
    /// permission" [ws_objects/pfw.shared.pbl.src/retcode.sru], so the refusal carries that code and
    /// acquires no member the oracle never had. Membership of the vocabulary is asserted by REFLECTING the
    /// kernel's constants, which is what makes the claim "a RetCode value" rather than "this particular
    /// integer" - and the specific code is asserted too, because membership alone would accept any of them.
    /// </para>
    /// <para>
    /// The body is asserted CONDITIONALLY on being present, and that is faithful rather than lax: the
    /// route declares its 401 without a response schema precisely because the body belongs to the host, so
    /// a row demanding one would be asserting a promise this route does not make. What it may never be is
    /// something OTHER than the published problem shape.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheRefusalBodyIsAProblemDocumentUsingTheLegacyVocabulary()
    {
        using HttpClient anonymous = host.CreateAnonymousClient();

        using HttpResponseMessage response = await anonymous.GetAsync(
            Relative(PingRoute),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        if (body.Length == 0)
        {
            return;
        }

        Assert.Equal(ProblemMediaType, response.Content.Headers.ContentType?.MediaType);

        using JsonDocument document = JsonDocument.Parse(body);

        Assert.Equal(
            (int)HttpStatusCode.Unauthorized,
            document.RootElement.GetProperty("status").GetInt32());

        if (!document.RootElement.TryGetProperty("retCode", out JsonElement retCode))
        {
            return;
        }

        Assert.Contains(retCode.GetInt64(), RetCodeVocabulary());
        Assert.Equal(RetCode.E_ACCESS_DENIED, retCode.GetInt64());
    }

    // ==============================================================================================
    //  SECTION 3 - GET /v1/ping WITH AN AUTHENTICATED PRINCIPAL: THE OTHER HALF OF THE PROOF
    // ==============================================================================================

    /// <summary>
    /// An authenticated principal reaches the liveness body, and the body is the published shape.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE ROW THAT KEEPS EVERY REFUSAL ROW HONEST. Protecting a route by breaking it would satisfy all of
    /// section 2 on its own, so the accepted path is asserted with the same rigour: a success status, the
    /// published media type, and every member of the body.
    /// </para>
    /// <para>
    /// THE <c>authenticated</c> MEMBER IS ASSERTED EXPLICITLY rather than inferred from the status,
    /// because that is what it exists for - it gives a conformance test something to assert on other than
    /// a status code. The return code is asserted from the shared kernel's symbol rather than as a
    /// literal: the value crosses the wire and appears in characterization recordings, so the assertion
    /// names the same symbol the handler does.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnAuthenticatedPrincipalReachesThePingBody()
    {
        using HttpClient authenticated = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await authenticated.GetAsync(
            Relative(PingRoute),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonMediaType, response.Content.Headers.ContentType?.MediaType);

        PingEndpoints.PingResponse? body = await response.Content
            .ReadFromJsonAsync<PingEndpoints.PingResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.Equal(ServiceIdentifier, body.Service);
        Assert.True(body.Authenticated);
        Assert.Equal(RetCode.OK, body.RetCode);
        Assert.Equal(DeterministicTimeProvider.DefaultInstant, body.Timestamp);
    }

    /// <summary>
    /// The accepted path is reached WITHOUT presenting a token, and without any outbound call.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THIS IS THE SOLE-ISSUER INVARIANT, ASSERTED AT THE POINT WHERE IT WOULD BE BROKEN. Reaching an
    /// authenticated route is exactly the moment a test suite is tempted to mint a credential, and minting
    /// one would put a signing key in this assembly - which is precisely what Security holding the only
    /// one in the system means (AAP 0.6.6.3, constraints C-F and C-G). The fixture's Posture B supplies a
    /// PRINCIPAL instead of a credential, so the assertion here is that the successful request carried no
    /// authorization header at all.
    /// </para>
    /// <para>
    /// The absence of an outbound call is asserted too, because a suite that had reached the accepted path
    /// by making this service fetch something would have proved the opposite of what it claimed.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheAcceptedPathIsReachedWithoutPresentingATokenAtAll()
    {
        using HttpClient authenticated = host.CreateAuthenticatedClient();

        Assert.Null(authenticated.DefaultRequestHeaders.Authorization);

        Assert.DoesNotContain(
            AuthorizationHeaderName,
            authenticated.DefaultRequestHeaders.Select(static header => header.Key),
            StringComparer.OrdinalIgnoreCase);

        Assert.Contains(
            DataServicesTestHostFactory.TestPrincipalHeaderName,
            authenticated.DefaultRequestHeaders.Select(static header => header.Key),
            StringComparer.OrdinalIgnoreCase);

        int before = host.Transport.RequestCount;

        using HttpResponseMessage response = await authenticated.GetAsync(
            Relative(PingRoute),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before, host.Transport.RequestCount);
    }

    /// <summary>
    /// Neither project in this service acquires the ability to mint a token, and this assembly uses none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// STRUCTURAL RATHER THAN TEXTUAL, DELIBERATELY. Both project files and several source files DISCUSS
    /// the minting library by name in order to record its absence as a decision, so a search of the source
    /// text would report the explanation as the violation. What cannot be faked is the assembly reference
    /// table and the declared package identities: an assembly that used a minting type would reference the
    /// assembly carrying it, and a project that could use one would declare the package.
    /// </para>
    /// <para>
    /// THE VALIDATION STACK IS DELIBERATELY NOT IMPLICATED. The stock bearer handler brings the whole
    /// identity-model family with it transitively, and that is how inbound validation is supposed to work;
    /// the root pin file legitimately carries a version for the minting library because the SECURITY
    /// service - the sole issuer - references it. What matters here is that neither of THIS service's two
    /// projects does.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoTokenMintingCapabilityIsReferencedByThisServiceOrThisAssembly()
    {
        Assert.DoesNotContain(
            MintingLibraryName,
            typeof(HealthAndPingEndpointTests).Assembly
                .GetReferencedAssemblies()
                .Select(static reference => reference.Name ?? string.Empty),
            StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            MintingLibraryName,
            typeof(PingEndpoints).Assembly
                .GetReferencedAssemblies()
                .Select(static reference => reference.Name ?? string.Empty),
            StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<string> applicationPackages = DeclaredPackageIdentities(
            RequireRepositoryFileText(
                $"{ApplicationProjectDirectory}/PowerFramework.DataServices.csproj"),
            "PackageReference");

        IReadOnlyList<string> testPackages = DeclaredPackageIdentities(
            RequireRepositoryFileText(
                $"{TestProjectDirectory}/PowerFramework.DataServices.Tests.csproj"),
            "PackageReference");

        Assert.DoesNotContain(MintingLibraryName, applicationPackages, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(MintingLibraryName, testPackages, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The liveness body echoes no credential, no claim of the principal and no internal topology.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// A PING IS A LIVENESS SIGNAL, NOT A TOKEN-INTROSPECTION OR DIAGNOSTICS ENDPOINT. The tempting
    /// implementation reports back what it saw - the subject, the granted scopes, the audience it matched -
    /// and every one of those turns a trivial success into a way to enumerate the token topology from
    /// outside. The subject and the scope set are named here from the fixture's own published constants,
    /// so this row asserts against the exact values the request DID carry rather than against a guess.
    /// </remarks>
    [Fact]
    public async Task ThePingBodyEchoesNoCredentialClaimOrTopology()
    {
        using HttpClient authenticated = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await authenticated.GetAsync(
            Relative(PingRoute),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        DataServicesOptions options = host.Services
            .GetRequiredService<IOptions<DataServicesOptions>>()
            .Value;

        Assert.DoesNotContain(
            DataServicesTestHostFactory.TestPrincipalSubject,
            body,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            DataServicesTestHostFactory.TestPrincipalScopes,
            body,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            DataServicesTestHostFactory.TestPrincipalHeaderValue,
            body,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            DataServicesTestHostFactory.ConfiguredIssuanceSecret,
            body,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            DataServicesTestHostFactory.ConfiguredAuthority,
            body,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            DataServicesTestHostFactory.ConfiguredAudience,
            body,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(options.Persistence.Address, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(options.Security.BaseAddress, body, StringComparison.OrdinalIgnoreCase);

        AssertCarriesNoKeyMaterialMarker(body, PingRoute);

        using JsonDocument document = JsonDocument.Parse(body);

        Assert.All(
            document.RootElement.EnumerateObject(),
            member => Assert.Contains(member.Name, PingMembers, StringComparer.Ordinal));
    }

    /// <summary>
    /// A principal holding NO permission still reaches the route, because the route declares none.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE COMPLEMENT OF THE POLICY ROW IN SECTION 2, AND IT IS AN ASSERTION ABOUT AN ABSENCE. The
    /// transformation plan publishes no scope for this route: it performs no work and reaches no
    /// capability, so a permission over it would be a permission over nothing, and demanding one would be
    /// a new requirement rather than a preserved behaviour (constraint C-B). The unscoped principal is
    /// therefore the posture that proves the requirement is authentication ALONE - a scope requirement
    /// that leaked onto this route from a group convention would refuse this caller, and nothing else in
    /// this file would notice.
    /// </para>
    /// <para>
    /// It is not a relaxation. The same caller presenting NO principal is refused - that is section 2 - so
    /// what this row shows is where the line is drawn, not that it moved.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task APrincipalHoldingNoScopeStillReachesTheRouteBecauseItDeclaresNone()
    {
        using HttpClient unscoped = host.CreateScopedClient([]);

        using HttpResponseMessage response = await unscoped.GetAsync(
            Relative(PingRoute),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ==============================================================================================
    //  SECTION 4 - THE VERIFICATION-ONLY POSTURE, ASSERTED AS THE ABSENCES IT CONSISTS OF
    // ==============================================================================================

    /// <summary>
    /// This service serves no token-issuance operation and publishes no verification material.
    /// </summary>
    /// <param name="method">The method to request with.</param>
    /// <param name="route">The path that belongs to the Security service alone.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE THREE PATHS ARE SECURITY'S, AND ONLY SECURITY'S. Contract C-01 places token issuance and both
    /// publication paths on the Security service, which is the sole issuer in the system; a second service
    /// answering any of them would either be minting tokens or publishing verification material it does
    /// not own. 404 is the assertion that the route is ABSENT rather than merely protected.
    /// </para>
    /// <para>
    /// THE PRINCIPAL IS PRESENT ON PURPOSE, AND WITHOUT IT THE ROW WOULD BE AMBIGUOUS. This host applies
    /// an authenticated-user FALLBACK policy, which also governs requests that match no endpoint at all,
    /// so an anonymous caller is answered 401 for an absent path exactly as for a protected one. Only an
    /// authenticated caller can tell the two apart, and that distinction is the whole content of this row.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("POST", TokenIssuanceRoute)]
    [InlineData("GET", KeySetRoute)]
    [InlineData("GET", DiscoveryRoute)]
    public async Task NeitherTokenIssuanceNorKeyPublicationIsServedHere(string method, string route)
    {
        using HttpClient authenticated = host.CreateAuthenticatedClient();

        using HttpRequestMessage request = new(new HttpMethod(method), Relative(route));

        using HttpResponseMessage response = await authenticated.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// An anonymous caller is never handed anything on those three paths either.
    /// </summary>
    /// <param name="method">The method to request with.</param>
    /// <param name="route">The path that belongs to the Security service alone.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE WEAKER CLAIM, ASSERTED SEPARATELY BECAUSE IT IS THE ONE THAT MATTERS TO AN ATTACKER. Whether
    /// the answer is 401 from the fallback policy or 404 from routing is an implementation detail of a path
    /// that does not exist; what may never happen is a success. Asserting the exact status would tie the
    /// row to which middleware answers first, and asserting only success-or-not is precisely the property.
    /// </remarks>
    [Theory]
    [InlineData("POST", TokenIssuanceRoute)]
    [InlineData("GET", KeySetRoute)]
    [InlineData("GET", DiscoveryRoute)]
    public async Task NoAnonymousCallerIsHandedIssuanceOrPublicationMaterial(string method, string route)
    {
        using HttpClient anonymous = host.CreateAnonymousClient();

        using HttpRequestMessage request = new(new HttpMethod(method), Relative(route));

        using HttpResponseMessage response = await anonymous.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.False(
            response.IsSuccessStatusCode,
            $"An anonymous {method} {route} was answered {(int)response.StatusCode}, which is a success. "
            + "Token issuance and key publication belong to the Security service alone.");
    }

    /// <summary>
    /// The three paths this suite asserts absent are exactly the ones the authored Security contract
    /// publishes.
    /// </summary>
    /// <remarks>
    /// THE ROW THAT STOPS THE THREE ABOVE FROM BEING VACUOUS. A 404 is trivially obtainable from any
    /// misspelled path, so the paths are cross-checked against
    /// <c>shared/PowerFramework.Contracts/OpenApi/security.v1.yaml</c> - the authored contract that
    /// declares them. If Security renamed one, this row fails and the three above are corrected rather
    /// than left asserting the absence of something nobody was ever going to serve.
    /// </remarks>
    [Fact]
    public void TheAbsentPathsAreTheOnesTheAuthoredSecurityContractPublishes()
    {
        string contract = RequireRepositoryFileText(
            "shared/PowerFramework.Contracts/OpenApi/security.v1.yaml");

        Assert.Contains($"  {TokenIssuanceRoute}:", contract, StringComparison.Ordinal);
        Assert.Contains($"  {KeySetRoute}:", contract, StringComparison.Ordinal);
        Assert.Contains($"  {DiscoveryRoute}:", contract, StringComparison.Ordinal);
    }

    /// <summary>
    /// No member anywhere in this service's options graph could carry signing material.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE WHOLE GRAPH IS WALKED, not just the authentication group, because a signing property added to
    /// any nested group would be just as reachable. The predicate looks for a member that could carry key
    /// MATERIAL - a string or a byte array whose name names a signing key - which is why
    /// <c>ValidateIssuerSigningKey</c> is not a match: it is a boolean SWITCH selecting whether the
    /// signature is checked, and switching validation on is the opposite of holding a key.
    /// </para>
    /// <para>
    /// THE ONE CREDENTIAL PROPERTY THAT DOES EXIST IS ASSERTED TO BE EMPTY IN SOURCE, and it is not a
    /// signing key: it is the password half of the caller identity this service presents in order to
    /// OBTAIN its first token, which contract C-01 requires because a caller cannot present a bearer token
    /// in order to be issued one. Its value arrives through a flat configuration key at run time, so the
    /// declaration is in the code and the material is not - the only arrangement that lets the value be
    /// required while leaving nothing to leak in a committed file (constraint C-F).
    /// </para>
    /// <para>
    /// AND THE AUTHENTICATION SECTION READS NOTHING BEYOND ITS OWN OPTION TYPE. Every configuration child
    /// under the section must correspond to a declared property, which is how "no signing key is read
    /// here" becomes an assertion rather than a claim: a key added to that section under ANY name - the
    /// orchestration signing secret's name included - would appear as a child that no property matches.
    /// Expressed as a subset test rather than as a list of forbidden names, so it holds against names
    /// nobody thought of.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoOptionMemberInThisServiceCouldCarrySigningMaterial()
    {
        Assert.Empty(CredentialMaterialShapedProperties());

        Assert.Empty(new SecurityClientOptions().ClientSecret);
        Assert.Empty(new MutualTlsClientOptions().CertificatePath);
        Assert.Empty(new MutualTlsClientOptions().CertificateKeyPath);

        IReadOnlyList<string> declared =
        [
            .. typeof(JwtAuthenticationOptions)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(static property => property.Name),
        ];

        IReadOnlyList<string> bound =
        [
            .. host.Services
                .GetRequiredService<IConfiguration>()
                .GetSection(JwtAuthenticationOptions.SectionName)
                .GetChildren()
                .Select(static child => child.Key),
        ];

        Assert.NotEmpty(bound);

        Assert.All(
            bound,
            key => Assert.Contains(key, declared, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The application registers exactly one authentication scheme, and it is the stock bearer handler
    /// pointed at Security with every inbound check on and no key of its own.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE FIXTURE'S TWO SCHEMES ARE EXCLUDED BY THEIR PUBLISHED NAMES, which is what makes the remainder
    /// the APPLICATION's set. Both are named for this test assembly precisely so they can never be
    /// mistaken for something the service registered, and the fixture adds to the set rather than
    /// replacing anything in it - so what is left after removing them is what a deployment runs.
    /// </para>
    /// <para>
    /// EVERY PART OF "VERIFICATION ONLY" IS ASSERTED SEPARATELY, because each fails differently: the
    /// handler type proves the security-critical path is framework code rather than hand-written code; the
    /// authority and audience prove it is pointed at the issuer; the four switches prove no inbound check
    /// was traded away; the absent keys and absent key resolver prove no verification material is embedded
    /// here; and a configuration manager whose type does not come from this service's own assembly proves
    /// the key set and the discovery document are fetched by the library rather than by a hand-rolled
    /// fetcher. A bespoke fetcher would be a net INCREASE in hand-written security code, which is the
    /// opposite of the reason Security speaks REST at all.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task OnlyTheStockBearerSchemeIsRegisteredAndItHoldsNoKeyOfItsOwn()
    {
        IReadOnlyList<AuthenticationScheme> registered =
        [
            .. await host.Services
                .GetRequiredService<IAuthenticationSchemeProvider>()
                .GetAllSchemesAsync(),
        ];

        AuthenticationScheme application = Assert.Single(
            registered,
            static scheme =>
                !string.Equals(
                    scheme.Name,
                    DataServicesTestHostFactory.SchemeSelectorName,
                    StringComparison.Ordinal)
                && !string.Equals(
                    scheme.Name,
                    DataServicesTestHostFactory.TestPrincipalSchemeName,
                    StringComparison.Ordinal));

        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, application.Name);
        Assert.Equal(typeof(JwtBearerHandler), application.HandlerType);

        JwtBearerOptions bearer = host.Services
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Equal(DataServicesTestHostFactory.ConfiguredAuthority, bearer.Authority);
        Assert.Equal(DataServicesTestHostFactory.ConfiguredAudience, bearer.Audience);

        Assert.True(bearer.TokenValidationParameters.ValidateIssuer);
        Assert.True(bearer.TokenValidationParameters.ValidateAudience);
        Assert.True(bearer.TokenValidationParameters.ValidateLifetime);
        Assert.True(bearer.TokenValidationParameters.ValidateIssuerSigningKey);

        Assert.Null(bearer.TokenValidationParameters.IssuerSigningKey);
        Assert.Null(bearer.TokenValidationParameters.IssuerSigningKeys);
        Assert.Null(bearer.TokenValidationParameters.IssuerSigningKeyResolver);

        Assert.Null(bearer.Configuration);
        Assert.NotNull(bearer.ConfigurationManager);

        Assert.NotEqual(
            typeof(PingEndpoints).Assembly,
            bearer.ConfigurationManager.GetType().Assembly);
    }

    /// <summary>
    /// This service authors no key-set, discovery-document or token-minting type of its own.
    /// </summary>
    /// <remarks>
    /// THE NAME-SHAPED COMPANION TO THE ROW ABOVE. The configuration-manager assertion proves the type in
    /// USE is the library's; this one proves no such type EXISTS to be swapped in later. It scans the
    /// application assembly's own types rather than its source text, so a comment recording the decision
    /// cannot satisfy or break it.
    /// </remarks>
    [Fact]
    public void ThisServiceAuthorsNoKeySetOrDiscoveryTypeOfItsOwn()
    {
        IReadOnlyList<string> authored =
        [
            .. typeof(PingEndpoints).Assembly
                .GetTypes()
                .Select(static type => type.Name),
        ];

        Assert.All(
            HandRolledRetrievalNameFragments,
            fragment => Assert.DoesNotContain(
                authored,
                name => name.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// The document this service publishes describes the same posture the service enforces.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// A CONTRACT THAT DISAGREED WITH THE RUNTIME WOULD BE WORSE THAN NO CONTRACT. The generator does not
    /// synthesise a security requirement from authorization metadata by itself, so a route can require a
    /// token while its published description claims to be anonymous - a generated client would then have no
    /// branch for the 401 it is certain to receive. Both halves are asserted from the DOCUMENT THE SERVICE
    /// ACTUALLY SERVES rather than from the authored contract files, because the served document is what a
    /// consumer reads.
    /// </para>
    /// <para>
    /// It is fetched with a principal, since the document route is authenticated on this service: the
    /// enumerated anonymous exceptions are the four services' health probes and Security's two publication
    /// paths, and a generated description of this surface is not among them.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ThePublishedDocumentDescribesTheSamePostureTheServiceEnforces()
    {
        using HttpClient authenticated = host.CreateAuthenticatedClient();

        using HttpResponseMessage served = await authenticated.GetAsync(
            Relative(DocumentRoute),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, served.StatusCode);

        using JsonDocument document = JsonDocument.Parse(
            await served.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        JsonElement paths = document.RootElement.GetProperty("paths");

        JsonElement probe = paths.GetProperty(HealthRoute).GetProperty("get");

        Assert.False(
            probe.TryGetProperty("security", out JsonElement probeSecurity)
                && probeSecurity.GetArrayLength() > 0,
            "The published document declares a security requirement on the readiness probe, which the "
            + "running service answers anonymously. The probe's anonymity is an enumerated exception and "
            + "the document must say so.");

        JsonElement ping = paths.GetProperty(PingRoute).GetProperty("get");

        Assert.True(
            ping.TryGetProperty("security", out JsonElement pingSecurity),
            "The published document declares no security requirement on the authenticated route, which "
            + "the running service answers 401 without a token.");

        Assert.Contains(
            BearerSchemeName,
            pingSecurity.EnumerateArray()
                .SelectMany(static requirement => requirement.EnumerateObject())
                .Select(static scheme => scheme.Name),
            StringComparer.Ordinal);

        Assert.True(
            ping.GetProperty("responses")
                .TryGetProperty(
                    ((int)HttpStatusCode.Unauthorized).ToString(CultureInfo.InvariantCulture),
                    out _),
            "The refusal is half of what this operation promises, so it is a DECLARED response rather "
            + "than an implementation detail.");

        JsonElement scheme = document.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty(BearerSchemeName);

        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
        Assert.Equal("JWT", scheme.GetProperty("bearerFormat").GetString());
    }

    // ==============================================================================================
    //  SECTION 5 - THE PORT, AND THIS FILE'S OWN DISCIPLINE
    // ==============================================================================================

    /// <summary>
    /// The service is configured on the REST port the plan's port map gives it, and names no other
    /// service's port there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHAT IS OBSERVABLE HERE IS THE CONFIGURED LISTENER, NOT A BOUND SOCKET, and that is the point: the
    /// host is in-process, so nothing is bound (constraints C-I and C-J) while the address a deployment
    /// acts on is still asserted. AAP 0.3.2.2 gives DataServices 5102, and the deployed manifest, the
    /// container definition and the documented probe all name it - a silent change here would break all
    /// three at once while every test that used a relative address kept passing.
    /// </para>
    /// <para>
    /// The two upstream ports are asserted ABSENT from this service's own listener address, because a
    /// listener accidentally configured on an upstream's port is the one form of this mistake that would
    /// still start and still answer.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheServiceIsConfiguredOnItsPublishedRestPort()
    {
        string? listener = host.Services.GetRequiredService<IConfiguration>()[RestEndpointUrlKey];

        Assert.False(
            string.IsNullOrWhiteSpace(listener),
            $"'{RestEndpointUrlKey}' is unset, so this service declares no REST listener at all.");

        Assert.EndsWith(RestPortSuffix, listener, StringComparison.Ordinal);

        Assert.DoesNotContain(":5101", listener, StringComparison.Ordinal);
        Assert.DoesNotContain(":5104", listener, StringComparison.Ordinal);
    }

    /// <summary>
    /// This suite declares no legacy constant identifier and carries no credential-shaped literal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A SUITE THAT POLICES A SECRETS RULE HAS TO BE HELD TO IT, so this row reads THIS FILE. Two
    /// properties are asserted. First, the legacy screaming-snake identifiers are REFERENCED here and
    /// never DECLARED: .editorconfig suppresses the naming analyzers only on the files that carry those
    /// identifiers, so a declaration here would be both a lint failure elsewhere and a second home for a
    /// value that must have exactly one (AAP 0.4.5.3). Second, no key material in any form - no
    /// certificate block header, no private-key header, no padded key-shaped blob.
    /// </para>
    /// <para>
    /// THE MARKERS ARE COMPOSED AT RUN TIME RATHER THAN WRITTEN AS LITERALS, because a literal marker
    /// would be the very text this row forbids and the assertion would fail on its own source. The
    /// comparisons are ORDINAL for the same reason: this file discusses private keys in prose, in lower
    /// case, and prose about a credential is not a credential.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThisSuiteDeclaresNoLegacyConstantAndCarriesNoCredentialShapedLiteral()
    {
        string source = RequireRepositoryFileText(
            $"{TestProjectDirectory}/HealthAndPingEndpointTests.cs");

        string certificateBlockMarker = string.Concat(new string('-', 5), "BEGIN");
        string privateKeyMarker = string.Concat("PRIVATE", " ", "KEY");

        Assert.DoesNotContain(certificateBlockMarker, source, StringComparison.Ordinal);
        Assert.DoesNotContain(privateKeyMarker, source, StringComparison.Ordinal);

        Assert.Empty(DeclaredLegacyConstantIdentifiers(source));
        Assert.Empty(EncodedKeyShapedRuns(source));
    }

    // ==============================================================================================
    //  THE ROWS
    // ==============================================================================================

    /// <summary>
    /// The fragments the anonymous readiness body may never carry, each with the reason its presence
    /// would be a disclosure.
    /// </summary>
    /// <returns>The rows.</returns>
    /// <remarks>
    /// <para>
    /// EVERY ROW IS A VALUE OR A TOPOLOGY DETAIL, NEVER A WORD. The endpoint's own fixed prose legitimately
    /// says "credential" and "service token" while describing a capability, so forbidding those words would
    /// assert against the contract instead of against a leak. What is forbidden is anything from which a
    /// reader could learn WHERE this service's collaborators are, WHAT it authenticates against, or WHAT
    /// went wrong inside it.
    /// </para>
    /// <para>
    /// The diagnostic rows at the end are the ones that catch the plausible regression rather than the
    /// obvious one: a probe that reported an exception message or a stack frame would look helpful in
    /// development and would publish this service's internals to anything that can reach the port for the
    /// rest of its life.
    /// </para>
    /// </remarks>
    public static TheoryData<string, string> FragmentsTheAnonymousReportMayNotCarry() => new()
    {
        { "127.0.0.1", "the loopback host this deployment's upstreams are addressed on" },
        { "localhost", "an upstream host name" },
        { "security-service", "the Security service's deployed host name" },
        { "persistence-service", "the Persistence service's deployed host name" },
        { ".invalid", "part of the configured issuer address" },
        { ":5101", "the Persistence port, which carries its gRPC contracts and its probe alike" },
        { ":5104", "the Security port" },
        { ":5102", "this service's own port, which a readiness report has no reason to name" },
        { "Authorization", "the name of the header a caller presents a credential in" },
        { "Basic ", "an HTTP Basic credential prefix" },
        { "Bearer ", "a bearer credential prefix" },
        { "Data Source", "a connection-string keyword" },
        { "Password=", "a connection-string credential keyword" },
        { "User Id=", "a connection-string identity keyword" },
        { "sqlite", "a storage engine this service does not even hold a provider for" },
        { "PowerFramework.DataServices", "an internal namespace or type name" },
        { "System.", "a framework type name, which only a leaked diagnostic would carry" },
        { "Microsoft.", "a framework type name, which only a leaked diagnostic would carry" },
        { "Exception", "an exception type or message" },
        { "StackTrace", "a stack trace" },
        { "   at ", "a stack frame" },
        { ".cs:line", "a source file and line number" },
        { "appsettings", "a configuration file name" },
        { "ClientSecret", "the name of the one credential property this service binds" },
    };

    /// <summary>
    /// The malformed authorization header shapes that must be refused rather than faulted.
    /// </summary>
    /// <returns>The rows.</returns>
    /// <remarks>
    /// NONE OF THESE IS A CREDENTIAL. Each is a syntactic shape carrying no value that could authenticate
    /// anything - no key, no token, no signature - which is what makes them safe to write down and what
    /// makes them exercise the "no usable credential present" path rather than the validation path.
    /// </remarks>
    public static TheoryData<string, string> MalformedAuthorizationHeaders() => new()
    {
        { "garbage", "a value with no scheme at all" },
        { "Bearer", "the bearer scheme with nothing after it" },
        { "Bearer ", "the bearer scheme with an empty value" },
        { "Bearer    ", "the bearer scheme followed only by whitespace" },
        { "bearer", "the bearer scheme in lower case with nothing after it" },
        { "Basic abc", "a different scheme entirely" },
        { "Negotiate abc", "a scheme this service does not register" },
    };

    // ==============================================================================================
    //  THE HELPERS
    // ==============================================================================================

    /// <summary>
    /// The member-name fragments that would mean an options member could carry signing material.
    /// </summary>
    private static readonly string[] SigningMemberNameFragments =
        ["SigningKey", "SigningCredential", "PrivateKey", "IssuerKey", "Jwks", "KeySet"];

    /// <summary>
    /// The type-name fragments that would mean this service had hand-rolled its own metadata retrieval.
    /// </summary>
    private static readonly string[] HandRolledRetrievalNameFragments =
        ["Jwks", "JsonWebKey", "OpenIdConfiguration", "DiscoveryDocument", "TokenMint", "TokenIssuer"];

    /// <summary>The characters an encoded key blob is composed of.</summary>
    private static readonly char[] EncodedBlobAlphabet =
    [
        'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'S',
        'T', 'U', 'V', 'W', 'X', 'Y', 'Z',
        'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm', 'n', 'o', 'p', 'q', 'r', 's',
        't', 'u', 'v', 'w', 'x', 'y', 'z',
        '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '+', '/', '=',
    ];

    /// <summary>Builds a relative request address.</summary>
    /// <param name="route">The route, leading slash included.</param>
    /// <returns>The relative address.</returns>
    /// <remarks>
    /// RELATIVE ON PURPOSE. The client's base address is the in-process test server's, so no absolute
    /// address is written anywhere in this file and no row can accidentally leave the process.
    /// </remarks>
    private static Uri Relative(string route) => new(route, UriKind.Relative);

    /// <summary>Finds the single route endpoint the shared host maps at a pattern.</summary>
    /// <param name="rawText">The exact route pattern.</param>
    /// <returns>The endpoint.</returns>
    private RouteEndpoint RequireEndpoint(string rawText) => RequireEndpoint(rawText, host);

    /// <summary>Finds the single route endpoint a given host maps at a pattern.</summary>
    /// <param name="rawText">The exact route pattern.</param>
    /// <param name="factory">The host to inspect.</param>
    /// <returns>The endpoint.</returns>
    /// <remarks>
    /// SINGLE rather than first: two endpoints at one pattern would make every metadata assertion depend
    /// on which one was picked, and the pattern is compared for EQUALITY rather than as a prefix so a
    /// longer route beginning with the same text cannot answer for it.
    /// </remarks>
    private static RouteEndpoint RequireEndpoint(string rawText, DataServicesTestHostFactory factory)
    {
        IReadOnlyList<RouteEndpoint> mapped =
        [
            .. factory.Services
                .GetRequiredService<EndpointDataSource>()
                .Endpoints
                .OfType<RouteEndpoint>(),
        ];

        return Assert.Single(
            mapped,
            endpoint => string.Equals(endpoint.RoutePattern.RawText, rawText, StringComparison.Ordinal));
    }

    /// <summary>
    /// Asserts that a ping endpoint requires authorization by declaration, names no policy of its own, and
    /// carries no anonymous escape.
    /// </summary>
    /// <param name="ping">The endpoint to inspect.</param>
    /// <remarks>
    /// FOUR SEPARATE CLAIMS, EACH OF WHICH FAILS DIFFERENTLY. Absent allow-anonymous metadata is what makes
    /// the requirement unskippable; present authorization metadata is what makes it a requirement at all;
    /// an unnamed policy is what keeps the route on the application's default rather than on a definition it
    /// does not own; and an unnamed authentication scheme is what keeps it on the registered default
    /// instead of pinning it to one that could later be removed.
    /// </remarks>
    private static void AssertPingIsAuthorizedByDeclaration(RouteEndpoint ping)
    {
        Assert.Null(ping.Metadata.GetMetadata<IAllowAnonymous>());

        IReadOnlyList<IAuthorizeData> authorization = ping.Metadata.GetOrderedMetadata<IAuthorizeData>();

        Assert.NotEmpty(authorization);

        Assert.All(authorization, data =>
        {
            Assert.True(
                string.IsNullOrEmpty(data.Policy),
                $"The ping route names the policy '{data.Policy}'. The parameterless requirement resolves "
                + "to the application's default policy, which is exactly an authenticated principal; "
                + "naming one couples the route to a definition it does not own.");

            Assert.True(
                string.IsNullOrEmpty(data.AuthenticationSchemes),
                "The ping route pins an authentication scheme rather than using the registered default.");

            Assert.True(string.IsNullOrEmpty(data.Roles), "The ping route demands a role it never published.");
        });

        Assert.Equal("ping", ping.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);

        Assert.Contains(
            HttpMethods.Get,
            ping.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [],
            StringComparer.Ordinal);
    }

    /// <summary>The complete set of return codes the ported legacy algebra declares.</summary>
    /// <returns>Every value.</returns>
    /// <remarks>
    /// READ BY REFLECTION FROM THE SHARED KERNEL rather than listed here, which is what turns "the body
    /// carries a RetCode value" into an assertion instead of a restatement: a code invented at the boundary
    /// would be absent from this set however plausible it looked. Duplicates are kept, because several
    /// spellings deliberately share a value - the three names for success and both spellings of cancelled
    /// among them - and de-duplicating would quietly assert something about the algebra's shape.
    /// </remarks>
    private static IReadOnlyList<long> RetCodeVocabulary() =>
    [
        .. typeof(RetCode)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(static field => field.IsLiteral && field.FieldType == typeof(long))
            .Select(static field => (long)field.GetRawConstantValue()!),
    ];

    /// <summary>Reads the package identities a project or property file declares.</summary>
    /// <param name="projectXml">The file's text.</param>
    /// <param name="elementName">The element to read - a reference or a version pin.</param>
    /// <returns>The declared identities.</returns>
    /// <remarks>
    /// PARSED AS XML RATHER THAN SEARCHED AS TEXT, and that is the whole reason this helper exists: both
    /// project files and the root pin file DISCUSS packages they deliberately omit, so a substring search
    /// would report each explanation as a violation. Only a real element with a real include attribute is
    /// a declaration.
    /// </remarks>
    private static IReadOnlyList<string> DeclaredPackageIdentities(string projectXml, string elementName) =>
    [
        .. XDocument.Parse(projectXml)
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, elementName, StringComparison.Ordinal))
            .Select(static element => element.Attribute("Include")?.Value ?? string.Empty)
            .Where(static identity => identity.Length > 0),
    ];

    /// <summary>
    /// Every member of this service's options graph that could carry key material.
    /// </summary>
    /// <returns>The offending members, which must be none.</returns>
    /// <remarks>
    /// THE WALK IS BREADTH-FIRST OVER THE WHOLE GRAPH and follows only types in this service's own
    /// configuration namespace, so it covers every nested group without wandering into the framework. A
    /// visited set makes it terminate whatever shape the graph takes.
    /// </remarks>
    private static IReadOnlyList<PropertyInfo> CredentialMaterialShapedProperties()
    {
        List<PropertyInfo> offending = [];
        HashSet<Type> visited = [];
        Queue<Type> pending = new([typeof(DataServicesOptions), typeof(JwtAuthenticationOptions)]);

        while (pending.Count > 0)
        {
            Type current = pending.Dequeue();

            if (!visited.Add(current))
            {
                continue;
            }

            foreach (PropertyInfo property in
                current.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (IsCredentialMaterialShaped(property))
                {
                    offending.Add(property);
                }

                if (string.Equals(
                        property.PropertyType.Namespace,
                        typeof(DataServicesOptions).Namespace,
                        StringComparison.Ordinal))
                {
                    pending.Enqueue(property.PropertyType);
                }
            }
        }

        return offending;
    }

    /// <summary>Whether a property could carry key material rather than a switch or an address.</summary>
    /// <param name="property">The property to classify.</param>
    /// <returns><see langword="true"/> when it could carry key material.</returns>
    /// <remarks>
    /// THE TYPE TEST IS AS LOAD-BEARING AS THE NAME TEST. <c>ValidateIssuerSigningKey</c> names a signing
    /// key and is a BOOLEAN: it selects whether the signature is checked, and switching validation on is
    /// the opposite of holding a key. Only a string or a byte array can carry material, so only those are
    /// classified.
    /// </remarks>
    private static bool IsCredentialMaterialShaped(PropertyInfo property) =>
        (property.PropertyType == typeof(string) || property.PropertyType == typeof(byte[]))
        && SigningMemberNameFragments.Any(fragment =>
            property.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    /// <summary>Every legacy screaming-snake identifier a source text DECLARES.</summary>
    /// <param name="source">The source text.</param>
    /// <returns>The declared identifiers, which must be none in this file.</returns>
    /// <remarks>
    /// ONLY THE DECLARATION HALF OF A DECLARATION LINE IS EXAMINED - the text before the assignment - so a
    /// REFERENCE to a kernel constant in an initializer or an assertion is not mistaken for a declaration.
    /// That distinction is the whole point: this file must reference those identifiers and must not declare
    /// them.
    /// </remarks>
    private static IReadOnlyList<string> DeclaredLegacyConstantIdentifiers(string source)
    {
        List<string> declared = [];

        foreach (string line in source.Split('\n'))
        {
            if (!line.Contains(" const ", StringComparison.Ordinal)
                && !line.Contains("static readonly", StringComparison.Ordinal))
            {
                continue;
            }

            int assignment = line.IndexOf('=', StringComparison.Ordinal);
            string declaration = assignment < 0 ? line : line[..assignment];

            foreach (string token in declaration.Split(
                [' ', '\t', '\r', '<', '>', '[', ']', '(', ')', ',', ';', '.'],
                StringSplitOptions.RemoveEmptyEntries))
            {
                if (LooksLikeLegacyConstantIdentifier(token))
                {
                    declared.Add(token);
                }
            }
        }

        return declared;
    }

    /// <summary>Whether a token is spelled as a legacy screaming-snake constant identifier.</summary>
    /// <param name="token">The token.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private static bool LooksLikeLegacyConstantIdentifier(string token) =>
        token.Length >= 3
        && char.IsAsciiLetterUpper(token[0])
        && token.Contains('_', StringComparison.Ordinal)
        && token.All(static character =>
            char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character) || character == '_');

    /// <summary>Every run of text in a document that is shaped like an encoded key blob.</summary>
    /// <param name="text">The text to scan.</param>
    /// <returns>The offending runs, which must be none.</returns>
    /// <remarks>
    /// <para>
    /// THREE CONDITIONS TOGETHER, BECAUSE ANY TWO OF THEM PRODUCE FALSE POSITIVES. A long run of the
    /// encoding alphabet alone matches a comment rule made of equals signs and matches this file's own
    /// long method names; requiring at least one encoding-only character excludes the method names;
    /// requiring both a letter and a digit excludes the comment rules. What is left is the shape of an
    /// encoded key: long, mixed, and carrying padding or a character no identifier may contain.
    /// </para>
    /// <para>
    /// It is a heuristic and it is deliberately a narrow one. Its job is to catch the specific mistake this
    /// refactor has already found eight times in the legacy tree - a key pasted into a file that a scanner
    /// cannot tell from a real one - and not to classify text in general.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<string> EncodedKeyShapedRuns(string text)
    {
        List<string> offending = [];
        int index = 0;

        while (index < text.Length)
        {
            if (!EncodedBlobAlphabet.Contains(text[index]))
            {
                index++;
                continue;
            }

            int start = index;

            while (index < text.Length && EncodedBlobAlphabet.Contains(text[index]))
            {
                index++;
            }

            string run = text[start..index];

            if (run.Length >= 40
                && run.Any(static character => character is '+' or '/' or '=')
                && run.Any(char.IsAsciiLetter)
                && run.Any(char.IsAsciiDigit))
            {
                offending.Add(run);
            }
        }

        return offending;
    }

    /// <summary>Reads a repository file by its path relative to the repository root.</summary>
    /// <param name="repositoryRelativePath">The forward-slashed path.</param>
    /// <returns>The file's text.</returns>
    /// <remarks>
    /// <para>
    /// AN UPWARD SEARCH ANCHORED ON BOTH ROOT MARKERS rather than a fixed number of parent hops, because
    /// the distance from a test assembly to the repository root is a function of the configuration and the
    /// target framework in the output path. It starts from the anchor the build embeds, so it still works
    /// when the test output is placed outside the checkout, and it keeps the walk as the fallback. Nothing
    /// is created, written or moved.
    /// </para>
    /// <para>
    /// An absent file is the FINDING rather than a reason to stand down: every caller reads a file whose
    /// content is the subject of an assertion, so silently skipping would turn a real violation into a
    /// passing run.
    /// </para>
    /// </remarks>
    private static string RequireRepositoryFileText(string repositoryRelativePath)
    {
        string[] segments = repositoryRelativePath.Split('/');

        DirectoryInfo? candidate = new(TestRepositoryRoot.SearchStart);

        while (candidate is not null)
        {
            if (File.Exists(Path.Combine(candidate.FullName, SolutionMarkerFileName))
                && File.Exists(Path.Combine(candidate.FullName, PackageVersionMarkerFileName)))
            {
                string file = Path.Combine([candidate.FullName, .. segments]);

                Assert.True(
                    File.Exists(file),
                    $"'{repositoryRelativePath}' was not found at '{file}'. It is the subject of a "
                    + "source-level assertion, so its absence is the finding rather than a reason to "
                    + "stand down.");

                return File.ReadAllText(file);
            }

            // Parent is null at the filesystem root, which is what terminates the walk.
            candidate = candidate.Parent;
        }

        Assert.Fail(
            $"No directory from '{TestRepositoryRoot.SearchStart}' up to the filesystem root holds both "
            + $"'{SolutionMarkerFileName}' and '{PackageVersionMarkerFileName}', so "
            + $"'{repositoryRelativePath}' could not be located. The probe is read-only; nothing was "
            + "created.");

        return string.Empty;
    }

    /// <summary>
    /// Asserts that a response body carries no marker of key material in any form.
    /// </summary>
    /// <param name="body">The body to scan.</param>
    /// <param name="route">The route that produced it, for the failure message.</param>
    /// <remarks>
    /// THE MARKERS ARE COMPOSED AT RUN TIME rather than written as literals, because this file also scans
    /// ITSELF for them and a literal here would be the very text that scan forbids. The comparisons are
    /// ordinal, so this file's prose about private keys - which is lower case - is not mistaken for one.
    /// </remarks>
    private static void AssertCarriesNoKeyMaterialMarker(string body, string route)
    {
        string certificateBlockMarker = string.Concat(new string('-', 5), "BEGIN");
        string privateKeyMarker = string.Concat("PRIVATE", " ", "KEY");

        Assert.False(
            body.Contains(certificateBlockMarker, StringComparison.Ordinal),
            $"The body of {route} opens a certificate or key block, which no response on this service "
            + "may carry.");

        Assert.False(
            body.Contains(privateKeyMarker, StringComparison.Ordinal),
            $"The body of {route} names private key material, which no response on this service may "
            + "carry.");

        Assert.Empty(EncodedKeyShapedRuns(body));
    }
}
