// ======================================================================================================
// HealthAggregationTests.cs
// Contract C-10 on the Gateway service: the health and readiness aggregate.
// ======================================================================================================
//
// WHAT THIS FILE PROVES, AND WHY IT MATTERS MORE THAN IT LOOKS
//   One property: GATEWAY REPORTS HEALTHY ONLY AFTER PERSISTENCE, DATASERVICES AND SECURITY ALL REPORT
//   HEALTHY, AND IF ANY ONE OF THEM IS NOT HEALTHY, GATEWAY IS NOT HEALTHY
//   [shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml, GET /health, x-contract-id C-10].
//
//   That property is what orchestration/docker-compose.yml expresses as `depends_on:` with
//   `condition: service_healthy` against each upstream, and THIS ENDPOINT is what the condition probes.
//   It is also the single requirement that RULED OUT the .NET Aspire Docker-Compose publisher as an
//   orchestration path: the generated model could not express a `service_healthy` condition
//   [docs/ARCHITECTURE.md 10.4], so the manifest is hand-authored instead. A regression here therefore
//   does not merely fail a test - it silently opens the orchestration readiness gate early, and Gateway
//   starts in front of upstreams that are not yet usable. Treat a break in this file as an
//   ORCHESTRATION break (constraint C-J).
//
//   It is a STATUS-CODE property. What a container orchestrator observes is the code, not the body, so
//   only `Healthy` may answer 200; `Degraded` and `Unhealthy` both answer 503 and stay distinguishable
//   in the payload. Every row of the matrix below asserts the code as well as the verdict for exactly
//   that reason.
//
// -----------------------------------------------------------------------------------------------------
// C-K DECISION - WHY THE MATRIX IS DRIVEN BY REGISTERED NAME AND NOT BY A CONFIGURATION KEY
// -----------------------------------------------------------------------------------------------------
//   The aggregate covers THREE upstreams, but Gateway CALLS only two of them. The topology is layered
//   and acyclic: Gateway calls DataServices and Security; DataServices calls Persistence; nothing but
//   DataServices calls Persistence. Configuration/GatewayOptions.cs encodes that in the type system -
//   `Gateway:Upstreams` carries DataServices and Security and has NO Persistence member, while
//   `Gateway:HealthProbes` carries three addresses that authorise exactly one anonymous endpoint each.
//
//   So this file drives every row through the ONE published seam, Endpoints/HealthEndpoints.cs's
//   IUpstreamReadinessProbe, ADDRESSING EACH UPSTREAM BY ITS REGISTERED PARTICIPANT NAME - the closed
//   enumeration `persistence`, `dataservices`, `security` that the interface documents and that the
//   endpoint passes on every observation. Driving it by name rather than by address is what makes the
//   matrix address-agnostic and configuration-key-agnostic, so it stays correct when a deployment moves
//   an address and it cannot be satisfied by a probe that answered for the wrong participant.
//
//   TWO THINGS THIS FILE DELIBERATELY DOES NOT REQUIRE TO EXIST, because the system under test forbids
//   both and a test demanding either would force a change to another folder's file:
//     * no `Gateway:Upstreams:Persistence` configuration key. GatewayOptions and appsettings.json do
//       not declare one, and a key invented here would bind to NOTHING and fail SILENTLY - the worst
//       outcome available to a readiness probe, which would then report one verdict forever.
//     * no Persistence client of any kind. Gateway has exactly two outbound edges,
//       Clients/DataServicesClient.cs and Clients/SecurityClient.cs. A third would turn a health
//       OBSERVATION into a functional CALL PATH and put SQL generation one hop from the ingress.
//
//   A DISCREPANCY, RECORDED RATHER THAN SMOOTHED OVER. The registered surface in the finished
//   HealthEndpoints.cs is two distinct things, not one: (a) the three upstream verdicts, addressed by
//   participant name through the probe seam, and (b) the shared framework's own HealthCheckService
//   registrations, which the anonymous body deliberately collapses into ONE unnamed `components` entry
//   so that a registration-supplied name such as `npgsql-primary-eu-west-1` cannot reach an anonymous
//   reader. There is no per-upstream HealthCheckRegistration and there is not meant to be. Both
//   surfaces are covered below, on their own terms, and the C-10 contract is asserted AS THE PLAN
//   STATES IT rather than weakened to match whichever surface was convenient. HealthEndpoints.cs is not
//   edited: it belongs to another folder.
//
//   NO PACKAGE IS ADDED. Health-check registration and the named-check builder both ship inside the
//   Microsoft.AspNetCore.App shared framework. Directory.Packages.props declares no health-check or
//   URI-probe package at all, and under central package management a PackageReference with no matching
//   PackageVersion FAILS RESTORE (NU1010), so the shared framework is the only available route rather
//   than merely the preferred one.
//
// -----------------------------------------------------------------------------------------------------
// THE SHARED TEST HOST, CONSUMED AND NOT REBUILT
// -----------------------------------------------------------------------------------------------------
//   GatewayTestHostFixture is declared publicly in the sibling AuthorizationTests.cs and is consumed
//   here through IClassFixture<GatewayTestHostFixture>. A CLASS fixture, deliberately, not a collection
//   fixture: a collection fixture serialises every test in every class that joins it, and these must
//   stay parallel-safe across classes. No second fixture and no helper file is introduced - this folder
//   permits a small fixed set of files and a helper would be one more than that.
//
//   THE SUBSTITUTION THAT MAKES ANY OF THIS RUN. Composition/FrameworkInitializer.cs publishes
//   IFrameworkRuntime over the framework's native lifecycle entry points, and both legacy declarations
//   are `from function_object native "pfw.dll"`
//   [ws_objects/pfw.base.pbl.src/pfwinitialize.srf:L3, ws_objects/pfw.base.pbl.src/pfwfinalize.srf:L3] -
//   a closed-source Win32 binary with no source in this repository and no existence inside the Linux
//   container this service ships as. The fixture substitutes that seam. Without it the host cannot
//   start and every test in this project fails at once, so it is a prerequisite rather than a
//   convenience.
//
//   NO LIVE UPSTREAM, NO DATABASE, NO CONTAINER, NO NETWORK. The readiness probe is substituted per
//   participant by the double below; the fixture additionally refuses outbound tokens and gives every
//   factory-created client a handler that throws on any outbound request, so a test that accidentally
//   reached the network fails loudly instead of passing slowly. Gateway holds no storage provider, so
//   no database is registered here - not even an in-memory one (constraint C-E).
//
// -----------------------------------------------------------------------------------------------------
// FAIL FAST STAYS FAIL FAST, AND THE TRAP IN THIS FILE IS CONFLATING IT WITH REPORTING (C-B, C-K)
// -----------------------------------------------------------------------------------------------------
//   Two failure modes look similar from a distance and behave oppositely. They are asserted separately
//   and on purpose:
//
//     AN UNREACHABLE UPSTREAM IS REPORTED AS UNHEALTHY AND THE PROCESS KEEPS RUNNING. Readiness
//     aggregation must not gate process startup: the host starts with every upstream unreachable and
//     simply reports itself unready. That is not a softening of anything - it is the property that makes
//     the orchestration chain possible at all, because a service that refused to start until its
//     upstreams answered could never be the thing they are gated behind.
//
//     A STRUCTURAL FAULT IS FATAL. The legacy reports a decoded assertion failure and then executes
//     HALT CLOSE [ws_objects/pfw.pbl.src/pfw.sra:L143]. The scenario that produces one is an UNCAUGHT
//     assertion: ws_objects/pfw.tests.pbl.src/w_test_assert.srw:L39 declares `wf_testassert(num)` over
//     `Assert(num > 0)` and :L137 invokes it with -1 catching nothing, while :L42 declares
//     `wf_testassert2(num)` over `Assert(num > 0,"Invalid Number!")` and :L119 invokes it the same way;
//     the failure reaches the application system-error event, which decodes it across
//     ws_objects/pfw.pbl.src/pfw.sra:L111-L144 and halts. Diagnostics/SystemErrorHandler.cs reproduces
//     that as host shutdown plus a non-zero process exit code, through a SUBSTITUTABLE termination
//     callback - so the request is OBSERVED here and the test runner is never terminated.
//
//   A test that expected warning-and-continue on a structural fault would silently license exactly the
//   softening C-B forbids: "graceful degradation" is a behavioural change wearing the costume of
//   robustness. The expectation is therefore written the other way round, and this note exists so a
//   future reader cannot mistake it for missing robustness (constraint C-K).
//
// -----------------------------------------------------------------------------------------------------
// LEGACY SOURCES ARE REFERENCE ONLY (constraint C-C)
// -----------------------------------------------------------------------------------------------------
//   ws_objects/** is READ ONLY and is the behavioural oracle. Its 47 w_test_*.srw windows are read for
//   SCENARIOS; not one line of any of them is translated into a test here, and not one of them is
//   edited, moved or reformatted. The same holds for ws_objects/pfw.pbl.src/pfw.sra, the two lifecycle
//   .srf files and the five Chinese documents under docs/.
//
//   ws_objects/pfw.pbl.src/pfw.sra IS ALWAYS WRITTEN OUT IN FULL. Two distinct files in this repository
//   are named pfw.sra, and the other - ws_objects/pfw.pack.pbl.src/pfw.sra - is the PowerBuilder
//   packager. A relative reference to either would be ambiguous.
//
//   Lifecycle references that bear on the `framework` component entry asserted below:
//   docs/README.md:L15 warns that pfwFinalize MUST be paired with pfwInitialize;
//   ws_objects/pfw.pbl.src/pfw.sra:L91 initializes as the first statement of the open event and :L108
//   finalizes as the only statement of close. docs/README.md:L21 warns that the auto-instantiate route
//   is unstable, which is why FrameworkInitializer is an INJECTED dependency rather than a global.
//
//   DESTINATION NAMES IN CODE, LEGACY NAMES ONLY IN CITATIONS. The legacy exception type is spelled
//   AssertionFailed and the destination type is AssertionFailure; the legacy global function Assert
//   lives on the destination static class Assertions, because a static class cannot contain a member of
//   its own name (CS0542); and the illegal `#` member prefix is dropped.
//
// -----------------------------------------------------------------------------------------------------
// WHAT IS DELIBERATELY NOT ASSERTED
// -----------------------------------------------------------------------------------------------------
//   NO PERFORMANCE, LATENCY, THROUGHPUT, AVAILABILITY, TIMEOUT-BUDGET OR STARTUP-DURATION ASSERTION OF
//   ANY KIND, and the prohibition is sharpest in this file precisely because health and readiness are
//   where such a claim would feel most natural. The repository publishes no service-level agreement, no
//   latency budget, no throughput target and no availability commitment anywhere, so there is no
//   baseline to compare against and inventing one would be a fabricated requirement. The only
//   quantitative non-functional requirement in the whole brief is the per-service line-coverage gate.
//   Every verdict below is DRIVEN DIRECTLY through the probe seam rather than produced by waiting, so
//   no test here depends on a clock, a timeout or a delay.
//
//   NO ENTRY, PROBE OR PLACEHOLDER FOR A DEFERRED CAPABILITY (constraint C-D). DesignSystem, Documents,
//   Integration and ScriptBridge have no project, no container and no health check, so there is nothing
//   to assert about one. The reserved 5103 slot is a commented placeholder in the orchestration manifest
//   and no probe in this file targets it.
//
//   NO USER INTERFACE, NO COMPONENT LIBRARY, NO DESIGN SYSTEM. None is specified, none exists, and the
//   capability area that would own one is a deferred service.
//
//   NO KEY MATERIAL, IN ANY FORM (constraints C-F and C-G). Nothing here holds or constructs a
//   credential; the one route under test is anonymous, and every assertion about credentials below is
//   an assertion of ABSENCE.
//
// USER RULES
//   review_rules returns exactly one line - "No user rules provided." That is the whole document, not a
//   loading failure. No rule is invented to fill the gap and its absence is not treated as licence to
//   lower the bar: the binding constraints are the plan's own non-rule constraints C-A to C-L and its
//   enterprise-standard baseline, and this file names the ones it honours where it honours them.
// ======================================================================================================

using System.Globalization;
using System.Net;
using System.Net.Mime;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PowerFramework.Gateway.Clients;
using PowerFramework.Gateway.Configuration;
using PowerFramework.Gateway.Diagnostics;
using PowerFramework.Gateway.Endpoints;
using PowerFramework.Shared.Diagnostics;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.Gateway.Tests;

// ------------------------------------------------------------------------------------------------------
//  1. THE ONE SUBSTITUTION SEAM THIS FILE ADDS
// ------------------------------------------------------------------------------------------------------

/// <summary>
/// A readiness probe scripted PER PARTICIPANT NAME, so each of the three upstreams can be given its own
/// verdict independently of the others and without an address, a socket or a configuration key.
/// </summary>
/// <remarks>
/// <para>
/// WHY A SECOND PROBE DOUBLE EXISTS AT ALL. The shared fixture's own substitute answers ONE verdict for
/// every participant, which is exactly right for "no upstream is reachable" but cannot express "these two
/// are ready and that one is not" - and the whole of contract C-10 lives in that difference. This double
/// keys its script by the participant name the endpoint supplies on every observation, which is the
/// closed enumeration <c>persistence</c>, <c>dataservices</c>, <c>security</c> that
/// <see cref="IUpstreamReadinessProbe"/> documents.
/// </para>
/// <para>
/// IT IS DECLARED HERE RATHER THAN IN A FILE OF ITS OWN, for the same reason the fixture is declared in
/// its own consumer: this folder permits a small fixed set of files, so a stub belongs beside the tests
/// that need it. Its name is distinct from every other double already declared in this folder, because
/// the whole folder compiles into one namespace and a repeated name is a build error rather than an
/// override.
/// </para>
/// <para>
/// AN UNSCRIPTED PARTICIPANT IS RECORDED RATHER THAN THROWN. Throwing would be invisible: the endpoint
/// deliberately converts anything a substituted probe throws into one unreachable entry, so that a faulty
/// double degrades a single entry instead of faulting the endpoint an orchestrator's readiness gate polls.
/// A throw here would therefore be swallowed and read as a legitimate verdict. Recording the name instead
/// lets the test assert that the script covered every participant the endpoint actually asked about.
/// </para>
/// <para>
/// IT PRESENTS NO CREDENTIAL AND INVOKES NOTHING, which is the contract the seam states: an
/// implementation is an observer of one anonymous endpoint and is not a client for the service behind it.
/// </para>
/// <para>
/// The three observations run concurrently under one linked token, so both records are guarded by a lock
/// and projected as snapshots.
/// </para>
/// </remarks>
internal sealed class ParticipantKeyedReadinessProbe : IUpstreamReadinessProbe
{
    /// <summary>Guards both records against the three concurrent observations.</summary>
    private readonly Lock _gate = new();

    /// <summary>Every participant name observed, in arrival order.</summary>
    private readonly List<string> _observed = [];

    /// <summary>Every participant name the script did not cover.</summary>
    private readonly List<string> _unscripted = [];

    /// <summary>The verdict each participant name answers with.</summary>
    private readonly Dictionary<string, UpstreamReadiness> _verdicts;

    /// <summary>Creates a probe scripted by participant name.</summary>
    /// <param name="verdicts">The verdict to answer for each participant name.</param>
    /// <exception cref="ArgumentNullException"><paramref name="verdicts"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The script is COPIED rather than captured, and compared with ordinal case-sensitive equality
    /// because the participant enumeration is a closed set of exact lower-case tokens.
    /// </remarks>
    internal ParticipantKeyedReadinessProbe(IReadOnlyDictionary<string, UpstreamReadiness> verdicts)
    {
        ArgumentNullException.ThrowIfNull(verdicts);

        _verdicts = new Dictionary<string, UpstreamReadiness>(verdicts, StringComparer.Ordinal);
    }

    /// <summary>A snapshot of the participant names observed.</summary>
    internal IReadOnlyList<string> ObservedParticipants
    {
        get
        {
            lock (_gate)
            {
                return [.. _observed];
            }
        }
    }

    /// <summary>A snapshot of the participant names the script did not cover. Must stay empty.</summary>
    internal IReadOnlyList<string> UnscriptedParticipants
    {
        get
        {
            lock (_gate)
            {
                return [.. _unscripted];
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Returns a verdict and never throws, which is the seam's stated contract: the aggregate is what an
    /// orchestrator's readiness gate reads, and a probe that faulted would take the container with it.
    /// </remarks>
    public ValueTask<UpstreamReadiness> ProbeAsync(
        string participant,
        Uri probeAddress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(probeAddress);

        cancellationToken.ThrowIfCancellationRequested();

        bool scripted = _verdicts.TryGetValue(participant, out UpstreamReadiness verdict);

        lock (_gate)
        {
            _observed.Add(participant);

            if (!scripted)
            {
                _unscripted.Add(participant);
            }
        }

        // The honest answer for a participant nobody scripted: nothing usable was obtained. The recorded
        // name is what turns the omission into a test failure rather than a silent default.
        return ValueTask.FromResult(scripted ? verdict : UpstreamReadiness.Unreachable);
    }
}


// ------------------------------------------------------------------------------------------------------
//  2. THE CONTRACT C-10 ASSERTIONS
// ------------------------------------------------------------------------------------------------------

/// <summary>
/// Contract C-10 on the Gateway service: the aggregate reports healthy only after all three upstreams do,
/// it answers anonymously, it never gates process startup, and it discloses nothing.
/// </summary>
/// <param name="host">The shared in-process host, supplied as an xUnit class fixture.</param>
/// <remarks>
/// <para>
/// EVERY ASSERTION IS AGAINST THE DEPLOYED COMPOSITION ROOT rather than a hand-assembled pipeline. The
/// options binding and its start-time validation, the framework lifecycle service, the fallback
/// authorization policy, the route's own anonymous posture and the aggregate itself all interact, and only
/// the real host exercises the interaction. A suite that built its own pipeline could pass while the
/// shipped service reported ready with an upstream down.
/// </para>
/// <para>
/// THE SHARED FIXTURE IS USED READ-ONLY. Its substituted probe answers <c>Unreachable</c> for every
/// participant, which is the honest state for a suite with no upstream present, and no test in this class
/// mutates it. Every row that needs a DIFFERENT verdict owns its own host instead, so nothing here depends
/// on execution order and every test is parallel-safe across classes.
/// </para>
/// </remarks>
public sealed class HealthAggregationTests(GatewayTestHostFixture host) : IClassFixture<GatewayTestHostFixture>
{
    /// <summary>
    /// The anonymous readiness route, spelled exactly as the contract spells it.
    /// </summary>
    /// <remarks>
    /// <c>/health</c> and NOT <c>/v1/health</c>. It is the one unversioned path in the whole document: a
    /// readiness probe is spelled by convention rather than negotiated, and versioning it would break the
    /// orchestration health gate for no benefit.
    /// </remarks>
    private const string ReadinessRoute = "/health";

    /// <summary>The AUTHENTICATED diagnostic probe, asserted here only as readiness's counterpart.</summary>
    private const string AuthenticatedProbeRoute = "/v1/ping";

    /// <summary>The Persistence upstream's registered participant name.</summary>
    /// <remarks>
    /// Gateway REPORTS ON Persistence and never CALLS it. This name is how the matrix addresses that
    /// upstream, which is precisely why no configuration key and no client is needed to reach it.
    /// </remarks>
    private const string PersistenceParticipant = "persistence";

    /// <summary>The DataServices upstream's registered participant name.</summary>
    private const string DataServicesParticipant = "dataservices";

    /// <summary>The Security upstream's registered participant name.</summary>
    private const string SecurityParticipant = "security";

    /// <summary>The only verdict answered with <c>200</c>.</summary>
    private const string HealthyToken = "Healthy";

    /// <summary>Not ready but not failed. Answered with <c>503</c>.</summary>
    private const string DegradedToken = "Degraded";

    /// <summary>Failed. Answered with <c>503</c>.</summary>
    private const string UnhealthyToken = "Unhealthy";

    /// <summary>
    /// The fourth token, which only a PER-UPSTREAM entry may carry.
    /// </summary>
    /// <remarks>
    /// The aggregate's vocabulary is closed at three, so an upstream from which no verdict could be
    /// obtained collapses to <see cref="UnhealthyToken"/> in the aggregate while keeping its own token in
    /// its own entry. Both halves of that are asserted, because the collapse is what makes the gate fail
    /// closed and the surviving distinction is what an operator needs.
    /// </remarks>
    private const string UnreachableToken = "Unreachable";

    /// <summary>The component entry standing for this process answering at all.</summary>
    private const string SelfCheckName = "self";

    /// <summary>The component entry standing for the framework lifecycle's startup validation.</summary>
    private const string FrameworkCheckName = "framework";

    /// <summary>The single aggregated entry standing for EVERY registered component check.</summary>
    private const string ComponentsCheckName = "components";

    /// <summary>The component entry naming whether the token bootstrap's credential material is present.</summary>
    private const string CredentialsCheckName = "credentials";

    /// <summary>The aggregate verdict member of the ready body, and the integer status of a problem body.</summary>
    private const string StatusMember = "status";

    /// <summary>The reporting-service member, present on both branches.</summary>
    private const string ServiceMember = "service";

    /// <summary>The human-readable member, which is where the not-ready verdict travels.</summary>
    private const string DetailMember = "detail";

    /// <summary>The evaluation-time member, read from the injected clock on both branches.</summary>
    private const string CheckedAtMember = "checkedAt";

    /// <summary>The per-upstream array member.</summary>
    private const string UpstreamsMember = "upstreams";

    /// <summary>The component-entry array member.</summary>
    private const string ChecksMember = "checks";

    /// <summary>The component entry's identifier member.</summary>
    private const string NameMember = "name";

    /// <summary>The legacy return-code extension member the contract publishes on its problem body.</summary>
    private const string RetCodeMember = "retCode";

    /// <summary>
    /// The readiness-verdict extension member the contract publishes on its problem body.
    /// </summary>
    /// <remarks>
    /// It exists because the not-ready response is a problem document rather than a report, and RFC 9457
    /// already uses <see cref="StatusMember"/> there for the integer HTTP status - so the verdict token
    /// needs a name of its own or it exists only inside the prose of <see cref="DetailMember"/>. It is a
    /// closed three-token vocabulary and therefore disclosure-safe by construction: it can carry nothing
    /// but <c>Healthy</c>, <c>Degraded</c> or <c>Unhealthy</c>.
    /// </remarks>
    private const string ServiceStatusMember = "serviceStatus";

    /// <summary>
    /// The framework-generated correlation identifier the problem shape carries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// IT IS NOT DISCLOSURE, AND IT IS THE ONE NON-DETERMINISTIC VALUE IN THE BODY. It is the handle an
    /// operator uses to find the full diagnostic record on the operator channel - which is the whole reason
    /// the anonymous body can afford to withhold the detail - and it carries no configuration value, no
    /// address and no credential, only opaque hexadecimal.
    /// </para>
    /// <para>
    /// IT IS ALSO A TRAP FOR A NAIVE DISCLOSURE SCAN, discovered by running the suite twice rather than
    /// reasoned about in advance. A W3C trace identifier is roughly fifty hexadecimal characters, so a
    /// four-digit port number appears inside one by chance often enough to fail a run: the first observed
    /// occurrence was a trace identifier containing the literal digits of an upstream port. Scanning the
    /// body for bare port digits is therefore only sound once this value is excluded, which is what
    /// <see cref="WithoutCorrelationIdentifier(string)"/> does - and the value's own OPACITY is then
    /// asserted separately, so excluding it does not create a blind spot.
    /// </para>
    /// </remarks>
    private const string TraceIdMember = "traceId";

    /// <summary>What the correlation identifier is replaced by before the body is scanned.</summary>
    private const string CorrelationPlaceholder = "[correlation-identifier]";

    /// <summary>
    /// A registration-supplied check name shaped exactly like the one the endpoint warns about.
    /// </summary>
    /// <remarks>
    /// Chosen to look like a real deployment's check name - a provider, a role and a region - because the
    /// disclosure risk it stands for is not hypothetical: a registered check's name is authored by whatever
    /// registered it rather than for publication, and <c>/health</c> is anonymous. The assertion is that
    /// this string NEVER appears in the response body (constraint C-G).
    /// </remarks>
    private const string RegistrationSuppliedCheckName = "npgsql-primary-eu-west-1";

    /// <summary>
    /// A description attached to the registered component check, which must not be echoed either.
    /// </summary>
    private const string RegistrationSuppliedDescription =
        "gateway-tests-registration-supplied-description";

    /// <summary>
    /// A configured probe address that is not an absolute URI, so no probe address can be composed from it.
    /// </summary>
    /// <remarks>
    /// Deliberately RELATIVE rather than merely wrong: the endpoint's defensive arm is reached by the address
    /// failing to parse as absolute, not by it pointing somewhere unhelpful. It carries no host, no port and
    /// no scheme, so it is not itself topology - and the assertion is that it never reaches the body anyway.
    /// </remarks>
    private const string UnusableProbeAddress = "an-unusable-relative-address";

    /// <summary>
    /// Vocabulary that must never appear in the anonymous body, matched case-insensitively.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CONSTRAINT C-G APPLIED TO AN UNAUTHENTICATED ENDPOINT. <c>/health</c> is world-readable by anything
    /// that can reach the port, so its body carries a closed vocabulary of status tokens and authored
    /// sentences and nothing else. This list is the negative half of that: host names, the whole 5101-5105
    /// port band including the reserved 5103 slot, connection-string markers and credential words.
    /// </para>
    /// <para>
    /// The problem body's <c>type</c> member is a specification-defined URI and is deliberately not caught
    /// by any entry here - it names a public RFC section rather than this system's topology. Every entry
    /// below is something only an internal detail could put in the body.
    /// </para>
    /// </remarks>
    private static readonly string[] ForbiddenBodyVocabulary =
    [
        "localhost",
        "127.0.0.1",
        "5101",
        "5102",
        "5103",
        "5104",
        "5105",
        "Data Source=",
        "Server=",
        "password",
        "passphrase",
        "secret",
        "credential",
        "apikey",
        "api_key",
        "bearer",
        "authorization",
        "signingkey",
        "jwks",
        "private key",
        "BEGIN RSA",
        "npgsql",
    ];

    /// <summary>
    /// The C-10 aggregation matrix: one row per combination of upstream verdicts that the contract's
    /// vocabulary can express, with the aggregate verdict and the status code each must produce.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TABLE-DRIVEN WITH MEMBER DATA, which is the mandated test shape for a parity matrix: the rows are
    /// data rather than code, so adding a case is a line rather than a method and a reviewer can read the
    /// whole contract in one place.
    /// </para>
    /// <para>
    /// THE COMBINING RULE THE ROWS ENCODE. The worse of two verdicts wins, and the framework enumeration is
    /// ordered by ASCENDING HEALTH rather than ascending severity, so "worse" is the MINIMUM. Hence
    /// <c>Degraded</c> beside <c>Unhealthy</c> yields <c>Unhealthy</c>, and a single not-ready upstream is
    /// enough to make the whole aggregate not ready however healthy the other two are. A reader who assumed
    /// the opposite ordering would invert the gate, which is why the rows state it exhaustively rather than
    /// by sampling.
    /// </para>
    /// <para>
    /// The first four rows are the contract's own statement of the property - all three ready, then each one
    /// down on its own. The rest exist because they are free once the matrix exists and because they are
    /// where an inverted or short-circuiting fold would actually show up: the degraded band, the unreachable
    /// band, every pair, all three at once, and the mixed row where two different not-ready verdicts must
    /// resolve to the worse of the two.
    /// </para>
    /// </remarks>
    public static TheoryData<string, UpstreamReadiness, UpstreamReadiness, UpstreamReadiness, string, int>
        ReadinessAggregationMatrix =>
        new()
        {
            // The one ready row. Every upstream healthy, and only this row may answer 200.
            {
                "all three upstreams ready",
                UpstreamReadiness.Healthy,
                UpstreamReadiness.Healthy,
                UpstreamReadiness.Healthy,
                HealthyToken,
                StatusCodes.Status200OK
            },

            // Each upstream failed on its own. These three rows ARE contract C-10.
            {
                "persistence failed alone",
                UpstreamReadiness.Unhealthy,
                UpstreamReadiness.Healthy,
                UpstreamReadiness.Healthy,
                UnhealthyToken,
                StatusCodes.Status503ServiceUnavailable
            },
            {
                "dataservices failed alone",
                UpstreamReadiness.Healthy,
                UpstreamReadiness.Unhealthy,
                UpstreamReadiness.Healthy,
                UnhealthyToken,
                StatusCodes.Status503ServiceUnavailable
            },
            {
                "security failed alone",
                UpstreamReadiness.Healthy,
                UpstreamReadiness.Healthy,
                UpstreamReadiness.Unhealthy,
                UnhealthyToken,
                StatusCodes.Status503ServiceUnavailable
            },

            // The degraded band. Not ready is not ready: 503, and the verdict stays distinguishable.
            {
                "persistence degraded alone",
                UpstreamReadiness.Degraded,
                UpstreamReadiness.Healthy,
                UpstreamReadiness.Healthy,
                DegradedToken,
                StatusCodes.Status503ServiceUnavailable
            },
            {
                "dataservices degraded alone",
                UpstreamReadiness.Healthy,
                UpstreamReadiness.Degraded,
                UpstreamReadiness.Healthy,
                DegradedToken,
                StatusCodes.Status503ServiceUnavailable
            },
            {
                "security degraded alone",
                UpstreamReadiness.Healthy,
                UpstreamReadiness.Healthy,
                UpstreamReadiness.Degraded,
                DegradedToken,
                StatusCodes.Status503ServiceUnavailable
            },

            // The unreachable band. The per-upstream entry keeps its own fourth token while the aggregate
            // collapses it to Unhealthy, which is how the gate fails closed on a verdict it never got.
            {
                "persistence unreachable alone",
                UpstreamReadiness.Unreachable,
                UpstreamReadiness.Healthy,
                UpstreamReadiness.Healthy,
                UnhealthyToken,
                StatusCodes.Status503ServiceUnavailable
            },
            {
                "dataservices unreachable alone",
                UpstreamReadiness.Healthy,
                UpstreamReadiness.Unreachable,
                UpstreamReadiness.Healthy,
                UnhealthyToken,
                StatusCodes.Status503ServiceUnavailable
            },
            {
                "security unreachable alone",
                UpstreamReadiness.Healthy,
                UpstreamReadiness.Healthy,
                UpstreamReadiness.Unreachable,
                UnhealthyToken,
                StatusCodes.Status503ServiceUnavailable
            },

            // Every pair, and then all three.
            {
                "persistence and dataservices failed",
                UpstreamReadiness.Unhealthy,
                UpstreamReadiness.Unhealthy,
                UpstreamReadiness.Healthy,
                UnhealthyToken,
                StatusCodes.Status503ServiceUnavailable
            },
            {
                "persistence and security failed",
                UpstreamReadiness.Unhealthy,
                UpstreamReadiness.Healthy,
                UpstreamReadiness.Unhealthy,
                UnhealthyToken,
                StatusCodes.Status503ServiceUnavailable
            },
            {
                "dataservices and security failed",
                UpstreamReadiness.Healthy,
                UpstreamReadiness.Unhealthy,
                UpstreamReadiness.Unhealthy,
                UnhealthyToken,
                StatusCodes.Status503ServiceUnavailable
            },
            {
                "all three failed",
                UpstreamReadiness.Unhealthy,
                UpstreamReadiness.Unhealthy,
                UpstreamReadiness.Unhealthy,
                UnhealthyToken,
                StatusCodes.Status503ServiceUnavailable
            },

            // All three degraded stays Degraded: the fold keeps the worse verdict, it does not escalate.
            {
                "all three degraded",
                UpstreamReadiness.Degraded,
                UpstreamReadiness.Degraded,
                UpstreamReadiness.Degraded,
                DegradedToken,
                StatusCodes.Status503ServiceUnavailable
            },

            // All three unreachable: the state a cold start actually produces, and the one the shared
            // fixture defaults to.
            {
                "all three unreachable",
                UpstreamReadiness.Unreachable,
                UpstreamReadiness.Unreachable,
                UpstreamReadiness.Unreachable,
                UnhealthyToken,
                StatusCodes.Status503ServiceUnavailable
            },

            // The mixed row, where a degraded upstream and a failed one must resolve to the worse of the
            // two rather than to whichever was folded in last.
            {
                "one degraded and one failed",
                UpstreamReadiness.Degraded,
                UpstreamReadiness.Unhealthy,
                UpstreamReadiness.Healthy,
                UnhealthyToken,
                StatusCodes.Status503ServiceUnavailable
            },
            {
                "one unreachable and one degraded",
                UpstreamReadiness.Unreachable,
                UpstreamReadiness.Degraded,
                UpstreamReadiness.Healthy,
                UnhealthyToken,
                StatusCodes.Status503ServiceUnavailable
            },
        };

    /// <summary>
    /// The registered-component-check matrix: one row per verdict a check registered BY NAME can report,
    /// with the aggregate verdict and status code it must produce while all three upstreams are ready.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE OTHER REGISTERED SURFACE. Gateway's own contribution to the verdict folds in every registered
    /// component check through the shared framework's health-check service, and these rows drive that
    /// surface by the check's REGISTERED NAME. The upstreams are all ready in every row on purpose, so the
    /// only thing that can move the aggregate is the component check - which is what isolates the property.
    /// </para>
    /// <para>
    /// The disclosure half rides along in the same test rather than in a second one, because the two are
    /// one property: the check's verdict must reach the aggregate while the check's NAME must not reach the
    /// body.
    /// </para>
    /// </remarks>
    public static TheoryData<HealthStatus, string, int> RegisteredComponentCheckMatrix =>
        new()
        {
            { HealthStatus.Healthy, HealthyToken, StatusCodes.Status200OK },
            { HealthStatus.Degraded, DegradedToken, StatusCodes.Status503ServiceUnavailable },
            { HealthStatus.Unhealthy, UnhealthyToken, StatusCodes.Status503ServiceUnavailable },
        };


    // --------------------------------------------------------------------------------------------------
    //  2.1  THE AGGREGATION MATRIX - CONTRACT C-10 ITSELF
    // --------------------------------------------------------------------------------------------------

    /// <summary>
    /// Gateway reports healthy only after Persistence, DataServices and Security all report healthy, and
    /// any one of them being not healthy makes Gateway not healthy.
    /// </summary>
    /// <param name="scenario">
    /// The row's description, carried so that a failure names the combination rather than an index.
    /// </param>
    /// <param name="persistence">The verdict the Persistence participant answers with.</param>
    /// <param name="dataServices">The verdict the DataServices participant answers with.</param>
    /// <param name="security">The verdict the Security participant answers with.</param>
    /// <param name="expectedAggregate">The aggregate verdict the row must produce.</param>
    /// <param name="expectedStatusCode">The status code the row must produce.</param>
    /// <remarks>
    /// <para>
    /// THIS IS THE ORCHESTRATION READINESS GATE, EXPRESSED AS A TEST. The compose manifest's
    /// <c>depends_on: condition: service_healthy</c> chain reads the STATUS CODE of this route, so the code
    /// is asserted on every row alongside the verdict - a not-ready service answering 200 would satisfy the
    /// condition and open the gate early, which is the one ordering property the gate exists to enforce.
    /// </para>
    /// <para>
    /// EACH ROW OWNS ITS HOST. Reaching a per-participant verdict means registering a scripted probe, and
    /// mutating the shared class fixture would make every other test in this class depend on execution
    /// order. Owning the host keeps each row independent, and the fixture is disposed asynchronously so the
    /// registered shutdown path - which is where the framework finalize step lives, the pairing
    /// <c>docs/README.md</c>:L15 requires - actually runs.
    /// </para>
    /// <para>
    /// NOTHING IS TIMED. Every verdict is DRIVEN through the probe seam, so no row waits for a socket, a
    /// timeout or a clock, and no row asserts anything about how long the probe took. The repository
    /// publishes no latency or availability commitment, so there is nothing to assert.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ReadinessAggregationMatrix))]
    public async Task GatewayReportsHealthyOnlyAfterAllThreeUpstreamsDo(
        string scenario,
        UpstreamReadiness persistence,
        UpstreamReadiness dataServices,
        UpstreamReadiness security,
        string expectedAggregate,
        int expectedStatusCode)
    {
        await using GatewayTestHostFixture aggregate =
            CreateHostScriptedByParticipantName(persistence, dataServices, security);

        using HttpClient client = aggregate.CreateAnonymousClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(ReadinessRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        // The gate's own signal, asserted first because it is the only thing an orchestrator observes.
        Assert.Equal(expectedStatusCode, (int)response.StatusCode);

        bool ready = expectedStatusCode == StatusCodes.Status200OK;

        Assert.Equal(
            ready ? MediaTypeNames.Application.Json : MediaTypeNames.Application.ProblemJson,
            response.Content.Headers.ContentType?.MediaType);

        string raw = await ReadBodyAsync(response, TestContext.Current.CancellationToken);

        using JsonDocument body = JsonDocument.Parse(raw);
        JsonElement root = body.RootElement;

        if (ready)
        {
            // The ready branch carries the verdict in its own status member.
            Assert.Equal(expectedAggregate, root.GetProperty(StatusMember).GetString());
        }
        else
        {
            // THE NOT-READY BRANCH IS RFC 9457, which reserves `status` for the INTEGER HTTP status code -
            // the contract's own schema types it as an integer - so the verdict token cannot occupy that
            // name and travels in `detail` instead. Asserting the integer as well as the token is what
            // stops a future edit from quietly moving the token into `status` and breaking every consumer
            // that reads the problem shape.
            Assert.Equal(expectedStatusCode, root.GetProperty(StatusMember).GetInt32());
            Assert.Contains(
                expectedAggregate,
                root.GetProperty(DetailMember).GetString(),
                StringComparison.Ordinal);

            // The legacy return code, read through the published symbol rather than as a bare integer so
            // the two cannot drift apart [ws_objects/pfw.shared.pbl.src/retcode.sru:L76].
            Assert.Equal(RetCode.E_RETRY, root.GetProperty(RetCodeMember).GetInt64());
        }

        // Both branches name the reporting service and carry the evaluation time from the INJECTED clock,
        // which is why the instant can be asserted for exact equality rather than for being about now.
        Assert.Equal(AggregateHealthReport.ReportingService, root.GetProperty(ServiceMember).GetString());
        Assert.Equal(
            GatewayTestHostFixture.FrozenNow,
            root.GetProperty(CheckedAtMember).GetDateTimeOffset());

        // The aggregate names each upstream individually, IN THE CONTRACT'S ORDER, so an operator reading a
        // failed aggregate can tell WHICH upstream is responsible rather than only that something is.
        List<KeyValuePair<string, string>> reported = ReadUpstreamStates(root);

        Assert.Equal(
            (string[])[PersistenceParticipant, DataServicesParticipant, SecurityParticipant],
            NamesOf(reported));

        // The per-upstream vocabulary is one token WIDER than the aggregate's: an upstream that answered
        // nothing keeps `Unreachable` here while the aggregate above collapses it to `Unhealthy`. Both
        // halves matter - the collapse is what makes the gate fail closed, and the surviving distinction is
        // what tells an operator to look at the network rather than at the upstream's own dependencies.
        Assert.Equal(ExpectedUpstreamToken(persistence), StateOf(reported, PersistenceParticipant));
        Assert.Equal(ExpectedUpstreamToken(dataServices), StateOf(reported, DataServicesParticipant));
        Assert.Equal(ExpectedUpstreamToken(security), StateOf(reported, SecurityParticipant));

        // Every upstream that is not reporting Healthy is named in the human-readable member too, so the
        // failing participant is identifiable without parsing the extension array.
        AssertNotReadyParticipantsAreNamed(root, ready, persistence, dataServices, security);

        // Gateway's own contribution is ready in every row, so each row is testing the UPSTREAMS and
        // nothing else. Stated rather than assumed: without it a row could pass because Gateway itself
        // happened to be degraded for an unrelated reason.
        List<KeyValuePair<string, string>> ownChecks = ReadComponentStates(root);

        Assert.Equal(HealthyToken, StateOf(ownChecks, SelfCheckName));
        Assert.Equal(HealthyToken, StateOf(ownChecks, FrameworkCheckName));

        // The composition root registers the health-check service WITHOUT registering a check, so the
        // aggregated entry is absent rather than present-and-empty. An entry standing for nothing would be
        // noise in a body that is read by a machine.
        Assert.DoesNotContain(ComponentsCheckName, NamesOf(ownChecks), StringComparer.Ordinal);

        // The script covered every participant the endpoint actually asked about, and it asked about all
        // three exactly once. This is what makes the row's verdicts provably the ones under test: a
        // mis-stated participant name would otherwise be swallowed by the endpoint's own fail-closed
        // wrapper and read as a legitimate Unreachable verdict.
        ParticipantKeyedReadinessProbe probe = ResolveScriptedProbe(aggregate);
        List<string> observed = [.. probe.ObservedParticipants];

        Assert.Empty(probe.UnscriptedParticipants);
        Assert.Equal(3, observed.Count);
        Assert.Contains(PersistenceParticipant, observed, StringComparer.Ordinal);
        Assert.Contains(DataServicesParticipant, observed, StringComparer.Ordinal);
        Assert.Contains(SecurityParticipant, observed, StringComparer.Ordinal);

        // Constraint C-G on every row rather than only on the dedicated disclosure test: the body an
        // unauthenticated caller receives names no host, no port, no connection string and no credential,
        // in either the ready or the not-ready shape.
        AssertBodyDisclosesNothing(raw, aggregate);

        // The row description is load-bearing for diagnosis rather than decoration, so its presence is
        // asserted. A row added with a blank description would report as an opaque index on failure.
        Assert.False(string.IsNullOrWhiteSpace(scenario));
    }

    /// <summary>
    /// A component check registered BY NAME moves the aggregate, and its registered name never reaches the
    /// anonymous body.
    /// </summary>
    /// <param name="registered">The verdict the registered check reports.</param>
    /// <param name="expectedAggregate">The aggregate verdict that must result.</param>
    /// <param name="expectedStatusCode">The status code that must result.</param>
    /// <remarks>
    /// <para>
    /// THE SECOND REGISTERED SURFACE, AND THE REASON IT IS ASSERTED SEPARATELY. The three upstream verdicts
    /// arrive through the probe seam; every registered component check arrives through the shared
    /// framework's health-check service. Both fold into the same aggregate, and a change that folded one in
    /// and not the other would pass a matrix that only exercised the other. Here all three upstreams are
    /// ready, so the registered check is the only thing that can move the verdict.
    /// </para>
    /// <para>
    /// AND THE DISCLOSURE HALF, WHICH IS THE SAME PROPERTY SEEN FROM THE OTHER SIDE (constraint C-G). The
    /// check's VERDICT must reach the aggregate; the check's NAME and DESCRIPTION must not reach the body,
    /// because both are authored by whatever registered the check rather than for publication and
    /// <c>/health</c> is anonymous. The body carries one aggregated <c>components</c> entry instead, and the
    /// registration-supplied names go to the operator channel - an audience authenticated by having access
    /// to the service's logs rather than by being able to reach a port.
    /// </para>
    /// <para>
    /// The check is registered through the shared framework's own named-check builder. No package is added:
    /// there is none to add, because <c>Directory.Packages.props</c> declares no health-check package and a
    /// reference without a central version fails restore.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(RegisteredComponentCheckMatrix))]
    public async Task ARegisteredComponentCheckMovesTheAggregateWithoutDisclosingItsName(
        HealthStatus registered,
        string expectedAggregate,
        int expectedStatusCode)
    {
        await using GatewayTestHostFixture componentHost = CreateHostScriptedByParticipantName(
            UpstreamReadiness.Healthy,
            UpstreamReadiness.Healthy,
            UpstreamReadiness.Healthy);

        componentHost.AdditionalServiceConfiguration.Add(services => services
            .AddHealthChecks()
            .AddCheck(
                RegistrationSuppliedCheckName,
                () => new HealthCheckResult(registered, RegistrationSuppliedDescription)));

        using HttpClient client = componentHost.CreateAnonymousClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(ReadinessRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatusCode, (int)response.StatusCode);

        string raw = await ReadBodyAsync(response, TestContext.Current.CancellationToken);

        using JsonDocument body = JsonDocument.Parse(raw);
        JsonElement root = body.RootElement;

        // The registered verdict reached the aggregate through the one aggregated entry.
        List<KeyValuePair<string, string>> ownChecks = ReadComponentStates(root);

        Assert.Equal(expectedAggregate, StateOf(ownChecks, ComponentsCheckName));
        Assert.Equal(HealthyToken, StateOf(ownChecks, SelfCheckName));
        Assert.Equal(HealthyToken, StateOf(ownChecks, FrameworkCheckName));

        // Every upstream stayed ready, so the verdict above is the component check's own contribution.
        List<KeyValuePair<string, string>> reported = ReadUpstreamStates(root);

        Assert.Equal(3, reported.Count);
        Assert.All(reported, entry => Assert.Equal(HealthyToken, entry.Value));

        // And the aggregate verdict itself, on whichever branch the row lands.
        if (expectedStatusCode == StatusCodes.Status200OK)
        {
            Assert.Equal(expectedAggregate, root.GetProperty(StatusMember).GetString());
        }
        else
        {
            Assert.Contains(
                expectedAggregate,
                root.GetProperty(DetailMember).GetString(),
                StringComparison.Ordinal);
        }

        // C-G: neither the registration-supplied name nor its description is anywhere in the body, on
        // either branch. This is the assertion the aggregated entry exists to make possible.
        Assert.DoesNotContain(RegistrationSuppliedCheckName, raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(RegistrationSuppliedDescription, raw, StringComparison.OrdinalIgnoreCase);
    }


    // --------------------------------------------------------------------------------------------------
    //  2.2  THE THREE PROPERTIES BEYOND THE AGGREGATE
    // --------------------------------------------------------------------------------------------------

    /// <summary>
    /// Readiness answers a request that carries NO credential, and it is never challenged - while the
    /// authenticated probe beside it refuses the identical anonymous request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE COMPOSE HEALTH PROBE IS THE CALLER THIS EXISTS FOR. It holds no token, and it polls precisely
    /// during the window in which the service is still starting - which is also the window in which
    /// Security's token issuance may not yet be reachable. Requiring a credential here would make readiness
    /// depend on the very thing whose readiness is being gated, a circular dependency that cannot resolve
    /// during a cold start.
    /// </para>
    /// <para>
    /// ASSERTED TOGETHER WITH ITS COUNTERPART ON PURPOSE. Anonymity is only meaningful as one half of a
    /// pair: the surrounding ingress is authenticated, and <c>/v1/ping</c> is the standing proof of that.
    /// Sending the SAME credential-free client at both routes in one test is what shows the anonymous
    /// posture is a deliberate, route-scoped exception rather than a hole in the pipeline (constraint C-G).
    /// </para>
    /// <para>
    /// The absence of a challenge header is asserted as well as the status code, because a 503 carrying a
    /// <c>WWW-Authenticate</c> header would tell an orchestrator to go and find a token.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ReadinessAnswersWithoutACredentialWhileTheAuthenticatedProbeRefuses()
    {
        using HttpClient client = host.CreateAnonymousClient();

        // Stated rather than assumed: the client this request is issued from carries no default credential.
        Assert.Null(client.DefaultRequestHeaders.Authorization);

        using HttpRequestMessage probe = new(HttpMethod.Get, new Uri(ReadinessRoute, UriKind.Relative));

        // And neither does the request itself.
        Assert.Null(probe.Headers.Authorization);

        using HttpResponseMessage readiness = await client.SendAsync(
            probe,
            TestContext.Current.CancellationToken);

        // Answered, not challenged. The 503 is the aggregate's verdict with no upstream present; what is
        // under test is that a verdict was produced at all for a caller holding nothing.
        Assert.NotEqual(HttpStatusCode.Unauthorized, readiness.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, readiness.StatusCode);
        Assert.Empty(readiness.Headers.WwwAuthenticate);

        // The counterpart, from the same credential-free client: the authenticated probe refuses.
        using HttpResponseMessage refused = await client.GetAsync(
            new Uri(AuthenticatedProbeRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    /// <summary>
    /// The host boots and keeps serving with every upstream unreachable, reporting unhealthy rather than
    /// failing to start, hanging, or stopping itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// AGGREGATION MUST NOT GATE PROCESS STARTUP, and this is both a real behavioural requirement and the
    /// prerequisite every other test in this project depends on. The compose dependency condition is
    /// ORCHESTRATION, not startup: a service that refused to start until its upstreams answered could never
    /// be the thing they are gated behind, so the aggregate makes no blocking upstream call during startup,
    /// performs no reachability check there, throws from no constructor or hosted service because an
    /// upstream is down, and runs no retry loop that delays the host.
    /// </para>
    /// <para>
    /// PROBED TWICE, DELIBERATELY. One answer proves the host started; a second answer after it proves the
    /// host is still running, which is the half that distinguishes "reported unhealthy" from "reported
    /// unhealthy and then died". The host lifetime is inspected directly for the same reason.
    /// </para>
    /// <para>
    /// The framework lifecycle is asserted to be in its initialized-and-not-finalized state, because that is
    /// what makes the <c>framework</c> component entry ready and therefore what proves the mandatory
    /// initialize/finalize pairing [<c>docs/README.md</c>:L15,
    /// <c>ws_objects/pfw.pbl.src/pfw.sra</c>:L91 and :L108] completed its first half at startup - with the
    /// native <c>pfw.dll</c> seam substituted, since that binary does not exist in this container.
    /// </para>
    /// <para>
    /// A locally owned host, so that the two probes are the ONLY requests this host has served and the
    /// observation counts below cannot pick up another test's traffic.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheHostBootsAndKeepsServingWithEveryUpstreamUnreachable()
    {
        await using GatewayTestHostFixture unreachable = CreateHostScriptedByParticipantName(
            UpstreamReadiness.Unreachable,
            UpstreamReadiness.Unreachable,
            UpstreamReadiness.Unreachable);

        using HttpClient client = unreachable.CreateAnonymousClient();

        using HttpResponseMessage first = await client.GetAsync(
            new Uri(ReadinessRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Not ready, because nothing answered - and ANSWERED rather than faulted, which is the point. A
        // probe that threw would fault the endpoint the readiness gate polls and the gate could never
        // recover.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);
        Assert.Equal(
            MediaTypeNames.Application.ProblemJson,
            first.Content.Headers.ContentType?.MediaType);

        string raw = await ReadBodyAsync(first, TestContext.Current.CancellationToken);

        using JsonDocument body = JsonDocument.Parse(raw);

        List<KeyValuePair<string, string>> reported = ReadUpstreamStates(body.RootElement);

        Assert.Equal(3, reported.Count);
        Assert.All(reported, entry => Assert.Equal(UnreachableToken, entry.Value));

        // The process is running and answering, which is what the `self` entry states truthfully rather
        // than assumes - its own evaluation is the demonstration.
        Assert.Equal(HealthyToken, StateOf(ReadComponentStates(body.RootElement), SelfCheckName));

        // Still serving afterwards. The second answer is what separates "reported unhealthy" from
        // "reported unhealthy and then stopped".
        using HttpResponseMessage second = await client.GetAsync(
            new Uri(ReadinessRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);

        // And nothing asked the host to stop. This is the fail-fast boundary in one assertion: an
        // unreachable upstream is REPORTED, never escalated into a structural fault.
        IHostApplicationLifetime lifetime =
            unreachable.Services.GetRequiredService<IHostApplicationLifetime>();

        Assert.False(lifetime.ApplicationStopping.IsCancellationRequested);
        Assert.False(lifetime.ApplicationStopped.IsCancellationRequested);

        // The framework lifecycle completed its startup half, through the substituted native seam.
        Assert.True(unreachable.Initializer.IsInitialized);
        Assert.False(unreachable.Initializer.IsFinalized);

        // Six observations for two requests: three per evaluation, none cached, none skipped.
        ParticipantKeyedReadinessProbe probe = ResolveScriptedProbe(unreachable);

        Assert.Empty(probe.UnscriptedParticipants);
        Assert.Equal(6, probe.ObservedParticipants.Count);
    }

    /// <summary>
    /// The anonymous body discloses no address, port, connection string, credential or internal topology -
    /// in its ready shape as well as in its not-ready shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CONSTRAINT C-G APPLIED TO AN UNAUTHENTICATED ENDPOINT, and the reason it needs applying: because
    /// <c>/health</c> is anonymous, everything it says is world-readable by anything that can reach the
    /// port. Its safety comes from disclosing nothing rather than from authentication, so the disclosure
    /// surface is the security control and is asserted as one.
    /// </para>
    /// <para>
    /// BOTH SHAPES ARE SCANNED. A body that leaked only while the service was failing would leak at exactly
    /// the moment an attacker was most interested, and the not-ready shape is the richer of the two - it
    /// carries the participant list, the component list and a composed sentence. Scanning only the ready
    /// shape would miss all three.
    /// </para>
    /// <para>
    /// The configured probe addresses are read FROM THE RUNNING HOST rather than restated, so the assertion
    /// keeps holding when a deployment changes an address. Their host and port are then required to be
    /// absent - including the whole 5101-5105 band, so that the reserved Phase-2 slot cannot appear either
    /// (constraint C-D).
    /// </para>
    /// <para>
    /// One consequence worth stating because it is easy to mistake for a gap: an upstream's OWN detail text
    /// is not echoed. Each upstream promises the same discipline for its own body, but "the upstream
    /// promised" is not a control this service can enforce, and a forwarded string is exactly how a provider
    /// name, a host, or - in the data path - an unredacted statement would reach an anonymous reader.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheAnonymousBodyDisclosesNoAddressCredentialOrTopology()
    {
        // The not-ready shape, from a host whose upstreams answer nothing.
        await using GatewayTestHostFixture notReady = CreateHostScriptedByParticipantName(
            UpstreamReadiness.Unreachable,
            UpstreamReadiness.Degraded,
            UpstreamReadiness.Unhealthy);

        using HttpClient notReadyClient = notReady.CreateAnonymousClient();

        using HttpResponseMessage notReadyResponse = await notReadyClient.GetAsync(
            new Uri(ReadinessRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, notReadyResponse.StatusCode);

        string notReadyBody = await ReadBodyAsync(notReadyResponse, TestContext.Current.CancellationToken);

        AssertBodyDisclosesNothing(notReadyBody, notReady);

        // The ready shape, from a host whose upstreams all answer ready.
        await using GatewayTestHostFixture ready = CreateHostScriptedByParticipantName(
            UpstreamReadiness.Healthy,
            UpstreamReadiness.Healthy,
            UpstreamReadiness.Healthy);

        using HttpClient readyClient = ready.CreateAnonymousClient();

        using HttpResponseMessage readyResponse = await readyClient.GetAsync(
            new Uri(ReadinessRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, readyResponse.StatusCode);

        string readyBody = await ReadBodyAsync(readyResponse, TestContext.Current.CancellationToken);

        AssertBodyDisclosesNothing(readyBody, ready);

        // THE MEMBER SET OF EACH SHAPE IS CLOSED, and pinning it is what makes the scan above COMPLETE
        // rather than best-effort: a future member carrying an address, a configuration value or another
        // random identifier could not slip past a scan that also fixes the shape. It is equally how the one
        // non-deterministic member stays accounted for instead of merely tolerated.
        Assert.Equal(
            (string[])[CheckedAtMember, ChecksMember, ServiceMember, StatusMember, UpstreamsMember],
            SortedMemberNames(readyBody));

        Assert.Equal(
            (string[])
            [
                CheckedAtMember,
                ChecksMember,
                DetailMember,
                RetCodeMember,
                ServiceMember,
                ServiceStatusMember,
                StatusMember,
                "title",
                TraceIdMember,
                "type",
                UpstreamsMember,
            ],
            SortedMemberNames(notReadyBody));

        // AND THE VERDICT MEMBER CARRIES A TOKEN FROM THE CLOSED VOCABULARY, not merely a member name. The
        // set above pins the SHAPE; this pins the one member whose whole purpose is to be branched on, and
        // the upstreams scripted above make Unhealthy the only correct aggregate - one of them answered
        // Unhealthy, and the aggregate takes the worst of its participants.
        using JsonDocument notReadyDocument = JsonDocument.Parse(notReadyBody);

        Assert.Equal(
            "Unhealthy",
            notReadyDocument.RootElement.GetProperty(ServiceStatusMember).GetString());

        // The environment name is configuration and is withheld too, on both shapes.
        Assert.DoesNotContain(notReady.EnvironmentName, notReadyBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ready.EnvironmentName, readyBody, StringComparison.OrdinalIgnoreCase);

        // The issuer the host is configured to trust is an authority address, so it is topology as much as
        // any upstream address is. Neither shape carries it.
        Assert.DoesNotContain(
            GatewayTestHostFixture.ExpectedIssuer,
            notReadyBody,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            GatewayTestHostFixture.ExpectedIssuer,
            readyBody,
            StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>
    /// With the SHIPPED token bootstrap in place, the credential material's presence is reported as its own
    /// component, and EITHER accepted scheme satisfies it.
    /// </summary>
    /// <param name="mounted">Whether a client-certificate pair is configured as well as the secret.</param>
    /// <param name="expectedStatus">The status the route must answer with.</param>
    /// <param name="expectedEntry">The verdict the credential entry must carry.</param>
    /// <remarks>
    /// <para>
    /// <b>BOTH ROWS ARE READY, AND THE SECOND ONE IS THE CORRECTION.</b> Contract C-01 accepts TWO caller
    /// credentials on <c>POST /v1/tokens</c> - a shared secret presented as an HTTP <c>Basic</c> credential
    /// naming a subject on Security's issuance roster, or a client certificate - as ALTERNATIVES, and its
    /// own <c>security</c> block declares them so. <c>SECURITY_CLIENT_SECRET_GATEWAY</c> is the one the
    /// documented bring-up supplies, with both certificate paths deliberately EMPTY:
    /// <c>orchestration/.env.example</c> section 6.3 records that as a SUPPORTED state and names the
    /// <c>Basic</c> scheme the primary one.
    /// </para>
    /// <para>
    /// This row used to assert that the unmounted case was DEGRADED, which is answered <c>503</c> - so it
    /// pinned a Gateway that could never report ready under the configuration the orchestration layer
    /// documents. That is a stack which does not come up rather than a misleading warning, and the test
    /// froze it. The verdict now follows the contract: a deployment presenting either scheme is ready.
    /// </para>
    /// <para>
    /// PRESENTING NEITHER IS NOT A READINESS STATE AT ALL, which is why there is no third row here. The
    /// composition root refuses to START a deployment that could present neither scheme, and
    /// <see cref="ADeploymentPresentingNeitherAcceptedSchemeIsRefusedBeforeItCanReportAnything"/> pins that
    /// - a fail-fast refusal rather than an instance that runs and reports itself unready.
    /// </para>
    /// <para>
    /// The fixture normally substitutes the bootstrap, so this row RESTORES the shipped client - which is
    /// what makes it a test of the production path rather than of a double. Nothing is sent: the check
    /// performs no I/O at all, and the fixture's own transport reaches no socket.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(true, HttpStatusCode.OK, HealthyToken)]
    [InlineData(false, HttpStatusCode.OK, HealthyToken)]
    public async Task TheCredentialMaterialForTheTokenBootstrapIsItsOwnReportedComponent(
        bool mounted,
        HttpStatusCode expectedStatus,
        string expectedEntry)
    {
        await using GatewayTestHostFixture host = CreateHostScriptedByParticipantName(
            UpstreamReadiness.Healthy,
            UpstreamReadiness.Healthy,
            UpstreamReadiness.Healthy);

        string certificate = string.Empty;
        string key = string.Empty;

        if (mounted)
        {
            // REAL MATERIAL ON DISK, because the composition root LOADS the pair eagerly at startup and
            // refuses to start on one it cannot read. A path pointing at nothing would therefore fail the
            // host build rather than exercise the readiness verdict - which is itself the correct
            // behaviour, and is asserted by the options and startup suites rather than here.
            (certificate, key) = WriteClientIdentityPem();

            host.AdditionalSettings[
                $"{GatewayOptions.SectionName}:{nameof(GatewayOptions.MutualTls)}"
                + $":{nameof(GatewayOptions.MutualTlsClientOptions.CertificatePath)}"] = certificate;
            host.AdditionalSettings[
                $"{GatewayOptions.SectionName}:{nameof(GatewayOptions.MutualTls)}"
                + $":{nameof(GatewayOptions.MutualTlsClientOptions.CertificateKeyPath)}"] = key;
        }

        RestoreShippedTokenBootstrap(host);

        using HttpClient client = host.CreateAnonymousClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(ReadinessRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatus, response.StatusCode);

        string raw = await ReadBodyAsync(response, TestContext.Current.CancellationToken);

        using JsonDocument body = JsonDocument.Parse(raw);

        Assert.Equal(
            expectedEntry,
            StateOf(ReadComponentStates(body.RootElement), CredentialsCheckName));

        // Every upstream answered healthy, so the credential component is the ONLY thing that could have
        // moved the aggregate - which is what makes a ready aggregate here attributable rather than
        // incidental.
        Assert.All(ReadUpstreamStates(body.RootElement), entry => Assert.Equal(HealthyToken, entry.Value));

        // `status` and not `serviceStatus`: the READY shape carries the aggregate verdict under the former,
        // and the latter belongs to the problem shape a not-ready answer uses.
        Assert.Equal(HealthyToken, body.RootElement.GetProperty(StatusMember).GetString());

        // NEITHER PATH NOR THE SECRET REACHES THE ANONYMOUS BODY. A path names where private key material is
        // mounted and a secret is a credential; this route is world-readable by anything that can reach the
        // port.
        Assert.DoesNotContain(nameof(GatewayOptions.MutualTls), raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            GatewayOptions.SecurityClientSecretConfigurationKey,
            raw,
            StringComparison.OrdinalIgnoreCase);

        if (!mounted)
        {
            return;
        }

        Assert.DoesNotContain(certificate, raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(key, raw, StringComparison.OrdinalIgnoreCase);

        File.Delete(certificate);
        File.Delete(key);
    }

    /// <summary>
    /// A deployment that could present NEITHER accepted issuance scheme is refused before it can report
    /// anything at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS WHAT REPLACED THE DEGRADED READINESS VERDICT, and it is the stronger posture. An instance
    /// that starts holding no credential can obtain no token and can therefore serve no proxied operation,
    /// so reporting itself unready would leave an operator watching a process that will never become
    /// useful. The composition root ends it instead - the fail-fast posture the legacy application object
    /// sets by terminating on a structural fault rather than warning
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L111-L144</c>].
    /// </para>
    /// <para>
    /// The secret is cleared through the SAME flat configuration key the deployment supplies it on, applied
    /// after the fixture's own, so what is exercised is the production resolution path rather than a
    /// test-only door. No certificate pair is configured either, so neither scheme is available.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ADeploymentPresentingNeitherAcceptedSchemeIsRefusedBeforeItCanReportAnything()
    {
        await using GatewayTestHostFixture host =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        host.AdditionalSettings[GatewayOptions.SecurityClientSecretConfigurationKey] = string.Empty;

        OptionsValidationException refused = Assert.Throws<OptionsValidationException>(
            () => host.CreateAnonymousClient().Dispose());

        // The refusal names the SETTING an operator supplies and quotes no value.
        Assert.Contains(
            GatewayOptions.SecurityClientSecretConfigurationKey,
            string.Join(' ', refused.Failures),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Writes a freshly generated client certificate and its private key to two temporary PEM files.
    /// </summary>
    /// <returns>The certificate path and the key path, both of which the caller deletes.</returns>
    /// <remarks>
    /// GENERATED RATHER THAN COMMITTED. No certificate and no private key is committed to this repository
    /// or embedded in any image, and a test fixture is not an exception to that: a key on disk in a
    /// repository is a key, whatever it is labelled. The pair is produced fresh, used by one host, and
    /// deleted.
    /// </remarks>
    private static (string Certificate, string Key) WriteClientIdentityPem()
    {
        using RSA key = RSA.Create(2048);

        CertificateRequest request = new(
            "CN=powerframework-gateway-readiness-test",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        using X509Certificate2 identity = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddMinutes(30));

        string certificatePath = Path.Combine(
            Path.GetTempPath(),
            $"blitzy_gateway_client_{Guid.NewGuid():n}.crt");
        string keyPath = Path.Combine(
            Path.GetTempPath(),
            $"blitzy_gateway_client_{Guid.NewGuid():n}.key");

        File.WriteAllText(certificatePath, identity.ExportCertificatePem());
        File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());

        return (certificatePath, keyPath);
    }

    /// <summary>
    /// A host that supplies its own token bootstrap gets NO credential entry, rather than a fabricated
    /// verdict about a mechanism this service does not own.
    /// </summary>
    /// <remarks>
    /// THE SAME RULE THE FRAMEWORK ENTRY FOLLOWS, and for the same reason: a host that registered no
    /// initializer simply has no framework entry. A bootstrap supplied by the host obtains credentials by
    /// some other means, whose preconditions this file cannot know - so an absent entry says "nothing to
    /// report" where a present one would say something false. The fixture's default IS that host, so this
    /// row also pins down that every other test in this file is unaffected by the entry's existence.
    /// </remarks>
    [Fact]
    public async Task AHostSuppliedTokenBootstrapReportsNoCredentialComponentAtAll()
    {
        await using GatewayTestHostFixture host = CreateHostScriptedByParticipantName(
            UpstreamReadiness.Healthy,
            UpstreamReadiness.Healthy,
            UpstreamReadiness.Healthy);

        using HttpClient client = host.CreateAnonymousClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(ReadinessRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument body = JsonDocument.Parse(
            await ReadBodyAsync(response, TestContext.Current.CancellationToken));

        List<KeyValuePair<string, string>> checks = ReadComponentStates(body.RootElement);

        Assert.DoesNotContain(CredentialsCheckName, checks.Select(static entry => entry.Key));

        // The two entries that DO belong are still there, so the row is asserting an absence rather than an
        // empty list.
        Assert.Equal(HealthyToken, StateOf(checks, SelfCheckName));
        Assert.Equal(HealthyToken, StateOf(checks, FrameworkCheckName));
    }


    // --------------------------------------------------------------------------------------------------
    //  2.3  FAIL FAST STAYS FAIL FAST, AND THE LINE BETWEEN IT AND REPORTING
    //
    //  Host-free from here down except where the contrast requires a host. Diagnostics/SystemErrorHandler.cs
    //  publishes a pure decoder, a pure formatter and a SUBSTITUTABLE termination request precisely so that
    //  the legacy protocol is reachable without a web host and without terminating the test runner - which
    //  is what makes a separate file for it unnecessary.
    // --------------------------------------------------------------------------------------------------

    /// <summary>
    /// A structural fault requests termination, while an unreachable upstream is merely reported and the
    /// process keeps running.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE TRAP THIS FILE EXISTS TO AVOID IS CONFLATING THESE TWO, so they are asserted side by side in one
    /// test rather than in two that could drift apart. They are different behaviours with opposite outcomes,
    /// and each is only meaningful against the other.
    /// </para>
    /// <para>
    /// FAIL FAST IS ASSERTED AS FAIL FAST, ON PURPOSE (constraint C-B). The legacy reports a decoded
    /// assertion failure and then executes <c>HALT CLOSE</c>
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra</c>:L141-L143]. The scenario that produces one is an UNCAUGHT
    /// assertion - <c>ws_objects/pfw.tests.pbl.src/w_test_assert.srw</c>:L39 declares the one-argument form
    /// and :L137 invokes it with <c>-1</c> catching nothing, :L42 declares the two-argument form and :L119
    /// invokes it the same way - so the failure reaches the application system-error event. A test that
    /// expected warning-and-continue here would silently license exactly the softening C-B forbids:
    /// "graceful degradation" is a behavioural change wearing the costume of robustness. This note is
    /// constraint C-K's requirement that the choice be documented, so that a future reader cannot mistake
    /// the expectation for missing robustness.
    /// </para>
    /// <para>
    /// AN UNREACHABLE UPSTREAM IS NOT A STRUCTURAL FAULT. It is the thing the readiness endpoint exists to
    /// report, and escalating a transient network condition into a halt would let any upstream's outage take
    /// this container down - which would also make the orchestration gate unusable, since the gate polls
    /// this service precisely while its upstreams are still coming up.
    /// </para>
    /// <para>
    /// NOTHING IS TERMINATED. The termination effect is a substitutable callback, so the request is OBSERVED
    /// and the runner survives; observing it is a stronger assertion than watching a process die, because it
    /// also pins down that the request is made exactly once and with a non-zero code. The exact code is the
    /// handler's own private constant and is deliberately not restated - what matters is that it is non-zero,
    /// because a zero exit code would report SUCCESS from a process that halted on a structural fault.
    /// </para>
    /// <para>
    /// The destination reproduces host SHUTDOWN plus an exit code rather than an immediate abort, because an
    /// abort would bypass the registered shutdown path - and that path is where the framework finalize step
    /// lives, the pairing <c>docs/README.md</c>:L15 requires and
    /// <c>ws_objects/pfw.pbl.src/pfw.sra</c>:L108 performs in its close event.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AStructuralFaultRequestsTerminationWhileAnUnreachableUpstreamOnlyReportsUnhealthy()
    {
        // ---- HALF ONE: the structural fault. Termination requested, exactly once, non-zero.
        TerminationRecordingLifetime structuralLifetime = new();
        List<int> requestedExitCodes = [];

        SystemErrorHandler structural = new(
            NullLogger<SystemErrorHandler>.Instance,
            structuralLifetime,
            problemDetailsService: null,
            requestProcessTermination: requestedExitCodes.Add);

        bool structuralHandled = await structural.TryHandleAsync(
            CreateFaultContext(),
            new AssertionFailure(BuildAssertPayload(CompleteFieldCount)),
            TestContext.Current.CancellationToken);

        Assert.True(structuralHandled);
        Assert.Single(requestedExitCodes);
        Assert.NotEqual(0, requestedExitCodes[0]);

        // The substituted callback replaces the whole host-backed request, so nothing reached the lifetime.
        // Stated rather than left implicit: it is what guarantees this test cannot stop the test host.
        Assert.Equal(0, structuralLifetime.StopRequestCount);

        // ---- HALF TWO: the fault shape an unreachable upstream produces. Handled, and NOT terminal.
        TerminationRecordingLifetime transportLifetime = new();
        List<int> notRequested = [];

        SystemErrorHandler transport = new(
            NullLogger<SystemErrorHandler>.Instance,
            transportLifetime,
            problemDetailsService: null,
            requestProcessTermination: notRequested.Add);

        bool transportHandled = await transport.TryHandleAsync(
            CreateFaultContext(),
            new HttpRequestException("the upstream refused the connection"),
            TestContext.Current.CancellationToken);

        Assert.True(transportHandled);
        Assert.Empty(notRequested);
        Assert.Equal(0, transportLifetime.StopRequestCount);

        // ---- HALF THREE: and end to end, on the shared host whose upstreams answer nothing at all. The
        // aggregate reports not-ready and the host is never asked to stop.
        using HttpClient client = host.CreateAnonymousClient();

        using HttpResponseMessage reported = await client.GetAsync(
            new Uri(ReadinessRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, reported.StatusCode);

        IHostApplicationLifetime live = host.Services.GetRequiredService<IHostApplicationLifetime>();

        Assert.False(live.ApplicationStopping.IsCancellationRequested);
        Assert.False(live.ApplicationStopped.IsCancellationRequested);
    }

    /// <summary>
    /// An unusable configured probe address is REPORTED as unreachable rather than thrown, and the
    /// unusable value is not echoed to the anonymous caller.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ONE ENDPOINT THAT MUST NEVER FAULT. Every other route in this service may answer a problem
    /// document, but this one is polled by the orchestration readiness gate: a probe that threw would take
    /// the container with it through the host's unhandled-exception path, and the gate could never recover.
    /// So every failure mode has to resolve to a VERDICT, including a configuration fault, and this row is
    /// the configuration-shaped one.
    /// </para>
    /// <para>
    /// WHY THE OPTIONS INSTANCE IS SUBSTITUTED RATHER THAN THE SETTING CHANGED, stated because it looks
    /// like the harder route and is in fact the only one. <c>GatewayOptions</c> validates all three probe
    /// addresses on start and the host is registered to validate on start, so a settings value this arm
    /// could react to would prevent the host from STARTING - which is the fail-fast posture working as
    /// intended, and a different property from the one under test. Supplying the options instance directly
    /// puts an unusable value in front of the endpoint while leaving the real start-time validation intact,
    /// which is what makes this arm reachable at all. It is DEFENCE rather than routine, and it is asserted
    /// precisely because a defensive arm nobody exercises is a defensive arm nobody knows is broken.
    /// </para>
    /// <para>
    /// The other two participants stay ready, so the row also shows that one unusable address degrades ONE
    /// entry rather than the whole evaluation - and that the probe is never even asked about the participant
    /// whose address could not be composed, since there is no address to ask it with.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnUnusableConfiguredProbeAddressIsReportedRatherThanThrown()
    {
        await using GatewayTestHostFixture misconfigured =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        ParticipantKeyedReadinessProbe probe = new(
            new Dictionary<string, UpstreamReadiness>(StringComparer.Ordinal)
            {
                [PersistenceParticipant] = UpstreamReadiness.Healthy,
                [DataServicesParticipant] = UpstreamReadiness.Healthy,
                [SecurityParticipant] = UpstreamReadiness.Healthy,
            });

        misconfigured.AdditionalServiceConfiguration.Add(services =>
        {
            services.RemoveAll<IUpstreamReadinessProbe>();
            services.AddSingleton<IUpstreamReadinessProbe>(probe);

            // Every other member keeps its declared default, which is the same value the deployed settings
            // file carries, so the ONLY difference from the running configuration is the one address.
            GatewayOptions unusable = new();
            unusable.HealthProbes.Persistence = UnusableProbeAddress;

            services.AddSingleton<IOptions<GatewayOptions>>(Options.Create(unusable));
        });

        using HttpClient client = misconfigured.CreateAnonymousClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(ReadinessRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Answered, not faulted. A 500 here would be the failure this test exists to prevent.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(
            MediaTypeNames.Application.ProblemJson,
            response.Content.Headers.ContentType?.MediaType);

        string raw = await ReadBodyAsync(response, TestContext.Current.CancellationToken);

        using JsonDocument body = JsonDocument.Parse(raw);
        List<KeyValuePair<string, string>> reported = ReadUpstreamStates(body.RootElement);

        // One entry degraded, the other two untouched.
        Assert.Equal(UnreachableToken, StateOf(reported, PersistenceParticipant));
        Assert.Equal(HealthyToken, StateOf(reported, DataServicesParticipant));
        Assert.Equal(HealthyToken, StateOf(reported, SecurityParticipant));

        // The probe was never asked about the participant whose address could not be composed, because
        // there was no address to ask it with.
        List<string> observed = [.. probe.ObservedParticipants];

        Assert.Equal(2, observed.Count);
        Assert.DoesNotContain(PersistenceParticipant, observed, StringComparer.Ordinal);
        Assert.Empty(probe.UnscriptedParticipants);

        // C-G: the offending VALUE is not echoed. The operator channel names the configuration KEY, which
        // is what an operator needs in order to correct it, and the anonymous caller learns neither.
        Assert.DoesNotContain(UnusableProbeAddress, raw, StringComparison.OrdinalIgnoreCase);
        AssertBodyDisclosesNothing(raw, misconfigured);
    }

    /// <summary>
    /// The decoder, the formatter and the exception projection are TOTAL: none of them throws for any
    /// input, including no input at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// REQUIRED BEHAVIOUR RATHER THAN DEFENSIVENESS, and the oracle is the reason. The legacy is a
    /// PowerScript event with no exception surface whatsoever
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra</c>:L111-L144], and its long conversion yields zero from
    /// unparseable text rather than failing, so a managed counterpart that threw on a malformed payload
    /// would have introduced a failure mode the legacy could not have had. That is a regression created by
    /// the migration, which is exactly what constraint C-B forbids.
    /// </para>
    /// <para>
    /// It matters most on the terminal path. A structural fault is already the worst moment for a second
    /// failure, and a decoder that threw while unpacking one would replace a diagnosable halt with an
    /// opaque one.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDecoderAndFormatterAreTotalForEveryInputIncludingNone()
    {
        // No exception at all: an empty record rather than a throw.
        SystemErrorInfo fromNothing = SystemErrorHandler.FromException(null);

        Assert.Equal(0L, fromNothing.Number);
        Assert.Equal(string.Empty, fromNothing.Text);
        Assert.Equal(string.Empty, fromNothing.Object);
        Assert.Equal(string.Empty, fromNothing.WindowMenu);
        Assert.Equal(string.Empty, fromNothing.ObjectEvent);
        Assert.Equal(0L, fromNothing.Line);
        Assert.Equal(string.Empty, fromNothing.StackTrace);

        // No record at all: treated as an empty one, on both pure entry points.
        Assert.Equal(fromNothing, SystemErrorHandler.Decode(null));
        Assert.Equal(SystemErrorHandler.Format(fromNothing).Body, SystemErrorHandler.Format(null).Body);

        // A malformed payload under the sentinel: the number falls to zero from unparseable text, exactly
        // as the legacy long conversion does, and nothing is signalled.
        SystemErrorInfo malformed = SystemErrorHandler.Decode(new SystemErrorInfo
        {
            Object = SystemErrorHandler.AssertErrorObject,
            Text = string.Join(PayloadFieldDelimiter, "not a number", AssertPayloadTextField),
        });

        Assert.Equal(0L, malformed.Number);
        Assert.Equal(AssertPayloadTextField, malformed.Text);

        // And the empty record still formats into the fixed five-line report, so the terminal path always
        // has something to write.
        List<string> lines = [.. SystemErrorHandler.Format(null).Body.Split(ReportLineSeparator)];

        Assert.Equal(TypeLine, lines[0]);
        Assert.Equal(TextLabel, lines[1]);
        Assert.DoesNotContain(WindowMenuLabel, lines, StringComparer.Ordinal);
        Assert.DoesNotContain(StackTraceLabel, lines, StringComparer.Ordinal);
    }

    /// <summary>
    /// The assert payload's five remaining fields are decoded at EXACTLY seven fields and at no other arity.
    /// </summary>
    /// <param name="fieldCount">How many CRLF-delimited fields the payload carries.</param>
    /// <param name="fullyDecoded">Whether that arity sets the five remaining fields.</param>
    /// <remarks>
    /// <para>
    /// PRESERVED VERBATIM, NOT "IMPROVED" (constraint C-B). <c>ws_objects/pfw.pbl.src/pfw.sra</c>:L116 tests
    /// for AT LEAST two fields before setting the failure number and the text, and :L119 tests for EXACTLY
    /// seven - <c>if nCount = 7</c>, not <c>&gt;= 7</c> - before setting the window-or-menu, the object, the
    /// object event, the line and the call stack. So a SIX-field payload and an EIGHT-field payload behave
    /// identically: both keep only the number and the text and discard the rest. Widening that to an
    /// at-least test would be a silent behavioural change, so the rows pin both sides of the boundary.
    /// </para>
    /// <para>
    /// A consequence that falls out of the same reading and is asserted here because it is easy to miss: at
    /// any arity other than seven the error object KEEPS THE SENTINEL it was discriminated on, because
    /// :L121 - the only statement that overwrites it - sits inside the exactly-seven branch.
    /// </para>
    /// <para>
    /// Field one is the fixed literal the legacy producer emits, and it is decoded into the failure number
    /// [:L117] and then NEVER RENDERED in the report [:L129-L139]. The decoder is pure and host-free, which
    /// is what lets this be a plain unit assertion.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(AssertPayloadArities))]
    public void TheRemainingFieldsAreDecodedAtExactlySevenFieldsAndNoOther(int fieldCount, bool fullyDecoded)
    {
        SystemErrorInfo decoded = SystemErrorHandler.Decode(new SystemErrorInfo
        {
            Object = SystemErrorHandler.AssertErrorObject,
            Text = BuildAssertPayload(fieldCount),
        });

        if (fieldCount < MinimumDecodableFieldCount)
        {
            // Below two fields nothing at all is decoded, so the payload survives as the text.
            Assert.Equal(0L, decoded.Number);
            Assert.Equal(AssertPayloadNumberField, decoded.Text);
        }
        else
        {
            Assert.Equal(-10000L, decoded.Number);
            Assert.Equal(AssertPayloadTextField, decoded.Text);
        }

        if (fullyDecoded)
        {
            Assert.Equal(AssertPayloadWindowMenuField, decoded.WindowMenu);
            Assert.Equal(AssertPayloadObjectField, decoded.Object);
            Assert.Equal(AssertPayloadObjectEventField, decoded.ObjectEvent);
            Assert.Equal(AssertPayloadLineNumber, decoded.Line);
            Assert.Equal(AssertPayloadStackTraceField, decoded.StackTrace);
        }
        else
        {
            Assert.Equal(string.Empty, decoded.WindowMenu);

            // The sentinel survives, because the statement that overwrites it is inside the seven-field
            // branch that did not run.
            Assert.Equal(SystemErrorHandler.AssertErrorObject, decoded.Object);
            Assert.Equal(string.Empty, decoded.ObjectEvent);
            Assert.Equal(0L, decoded.Line);
            Assert.Equal(string.Empty, decoded.StackTrace);
        }
    }

    /// <summary>
    /// The window-or-menu line appears only when the window-or-menu value DIFFERS from the object value,
    /// and inequality is the only guard.
    /// </summary>
    /// <param name="windowMenu">The window-or-menu value.</param>
    /// <param name="objectName">The object value.</param>
    /// <param name="expectWindowLine">Whether the line must appear.</param>
    /// <remarks>
    /// <para>
    /// PRESERVED VERBATIM (constraint C-B). <c>ws_objects/pfw.pbl.src/pfw.sra</c>:L131-L133 guards the line
    /// with <c>if Error.WindowMenu &lt;&gt; Error.Object</c> and with NOTHING ELSE. There is deliberately no
    /// non-empty test, so an EMPTY window-or-menu against a non-empty object still emits the label followed
    /// by nothing - the third row below. Adding the non-empty test a reader would instinctively reach for
    /// would change the report a human sees, which is exactly the kind of tidy-up C-B forbids.
    /// </para>
    /// <para>
    /// The five other lines are unconditional and are asserted present on every row, so that the absence
    /// above reads as a deliberate omission rather than as an empty report.
    /// </para>
    /// <para>
    /// The labels are restated from the ORACLE - <c>ws_objects/pfw.pbl.src/pfw.sra</c>:L129-L139 - and not
    /// from the implementation, whose corresponding constants are private. That is the right direction for a
    /// parity assertion: the test agrees with the legacy, and the implementation has to agree with the test.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(WindowMenuLineCases))]
    public void TheWindowLineAppearsOnlyWhenTheWindowAndObjectValuesDiffer(
        string windowMenu,
        string objectName,
        bool expectWindowLine)
    {
        SystemErrorReport report = SystemErrorHandler.Format(new SystemErrorInfo
        {
            Number = -10000L,
            Text = AssertPayloadTextField,
            WindowMenu = windowMenu,
            Object = objectName,
            ObjectEvent = AssertPayloadObjectEventField,
            Line = AssertPayloadLineNumber,
        });

        List<string> lines = [.. report.Body.Split(ReportLineSeparator)];
        List<string> windowLines = LinesStartingWith(lines, WindowMenuLabel);

        if (expectWindowLine)
        {
            Assert.Equal(WindowMenuLabel + windowMenu, Assert.Single(windowLines));
        }
        else
        {
            Assert.Empty(windowLines);
        }

        // The unconditional lines, on every row.
        Assert.Equal(TypeLine, lines[0]);
        Assert.Equal(TextLabel + AssertPayloadTextField, lines[1]);
        Assert.Contains(ObjectLabel + objectName, lines, StringComparer.Ordinal);
        Assert.Contains(ObjectEventLabel + AssertPayloadObjectEventField, lines, StringComparer.Ordinal);
        Assert.Contains(
            LineLabel + AssertPayloadLineNumber.ToString(CultureInfo.InvariantCulture),
            lines,
            StringComparer.Ordinal);
    }

    /// <summary>
    /// The call-stack section appears only when the call-stack value is non-empty, the failure number is
    /// never rendered, the type label is always the literal system word, and the report is joined with bare
    /// line feeds although the payload arrives CRLF-delimited.
    /// </summary>
    /// <remarks>
    /// <para>
    /// FOUR QUIRKS THAT TRAVEL TOGETHER, ALL PRESERVED (constraints C-B and C-K).
    /// </para>
    /// <list type="number">
    /// <item>
    /// <c>ws_objects/pfw.pbl.src/pfw.sra</c>:L137-L139 emits the call-stack section only when the stack
    /// string is non-empty, and its label carries a colon and then a separator with NO trailing space -
    /// unlike the six value labels, which each carry a colon and exactly one space. That inconsistency is
    /// legacy behaviour and is reproduced rather than harmonised.
    /// </item>
    /// <item>
    /// The failure number is decoded at :L117 and then never appears in the composed report, so the report a
    /// human reads omits the very value the decode recovered.
    /// </item>
    /// <item>
    /// The type label at :L129 is the hardcoded literal system word EVEN ON THE FULLY DECODED ASSERTION
    /// path, so the first line of a decoded assertion report is indistinguishable from the first line of any
    /// other system error.
    /// </item>
    /// <item>
    /// The payload is split on CRLF at :L115 while the report is joined with bare line feeds at
    /// :L129-L139, so the delimiter CHANGES across the boundary.
    /// </item>
    /// </list>
    /// <para>
    /// The empty-stack case is reached through the pure formatter rather than through a payload, and that is
    /// forced rather than chosen: the legacy split helper appends its tail only when the tail is non-empty
    /// [:L64-L66], so a seven-field payload whose last field is empty splits into SIX fields and never
    /// reaches the seven-field branch at all. That behaviour is asserted separately below.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheCallStackSectionAppearsOnlyWhenTheStackValueIsNonEmpty()
    {
        SystemErrorInfo withStack = SystemErrorHandler.Decode(new SystemErrorInfo
        {
            Object = SystemErrorHandler.AssertErrorObject,
            Text = BuildAssertPayload(CompleteFieldCount),
        });

        SystemErrorReport carried = SystemErrorHandler.Format(withStack);
        List<string> carriedLines = [.. carried.Body.Split(ReportLineSeparator)];

        // The label line, then the value on the NEXT line - separator, label, separator, value.
        int labelIndex = carriedLines.IndexOf(StackTraceLabel);

        Assert.True(labelIndex >= 0);
        Assert.Equal(AssertPayloadStackTraceField, carriedLines[labelIndex + 1]);

        // The same record with the stack emptied: the whole section disappears, and nothing else does.
        SystemErrorReport withoutStack = SystemErrorHandler.Format(withStack with { StackTrace = string.Empty });
        List<string> withoutLines = [.. withoutStack.Body.Split(ReportLineSeparator)];

        Assert.DoesNotContain(StackTraceLabel, withoutLines, StringComparer.Ordinal);
        Assert.Equal(carriedLines.Count - 2, withoutLines.Count);

        // QUIRK: the failure number was decoded and is rendered nowhere.
        Assert.Equal(-10000L, withStack.Number);
        Assert.DoesNotContain(
            withStack.Number.ToString(CultureInfo.InvariantCulture),
            carried.Body,
            StringComparison.Ordinal);

        // QUIRK: the type line is the literal system word even for a fully decoded assertion, so the first
        // line of this report equals the first line of an empty one.
        Assert.Equal(TypeLine, carriedLines[0]);
        Assert.Equal(
            SystemErrorHandler.Format(new SystemErrorInfo()).Body.Split(ReportLineSeparator)[0],
            carriedLines[0]);

        // QUIRK: CRLF on the way in, bare line feeds on the way out.
        Assert.Contains(PayloadFieldDelimiter, BuildAssertPayload(CompleteFieldCount), StringComparison.Ordinal);
        Assert.DoesNotContain(PayloadFieldDelimiter, carried.Body, StringComparison.Ordinal);

        // And the two dialog arguments the legacy passed alongside the text, carried as data instead of
        // displayed [ws_objects/pfw.pbl.src/pfw.sra:L141].
        Assert.Equal(SystemErrorSeverity.Stop, carried.Severity);
        Assert.False(string.IsNullOrWhiteSpace(carried.Title));
    }

    /// <summary>
    /// A payload written with seven fields whose last one is empty decodes as SIX fields, so the remaining
    /// five are never set and the report carries no call-stack section.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A PRESERVED CONSEQUENCE OF THE SPLIT HELPER, not a defect in the decoder. The legacy helper appends
    /// its tail ONLY when the tail is non-empty [<c>ws_objects/pfw.pbl.src/pfw.sra</c>:L64-L66], so a
    /// trailing delimiter yields no empty final field and the arity the seven-field branch tests against
    /// [:L119] is one lower than the author of the payload intended. The observable result is that an
    /// assertion whose stack trace happened to be empty loses its window-or-menu, its object, its object
    /// event and its line number as well.
    /// </para>
    /// <para>
    /// Asserted rather than fixed. It is the legacy's behaviour, and it is the reason the empty-stack case in
    /// the formatter test above cannot be reached through a payload at all.
    /// </para>
    /// </remarks>
    [Fact]
    public void ASevenFieldPayloadWhoseLastFieldIsEmptyDecodesAsSixFields()
    {
        string payload = string.Join(
            PayloadFieldDelimiter,
            AssertPayloadNumberField,
            AssertPayloadTextField,
            AssertPayloadWindowMenuField,
            AssertPayloadObjectField,
            AssertPayloadObjectEventField,
            AssertPayloadLineNumber.ToString(CultureInfo.InvariantCulture),
            string.Empty);

        SystemErrorInfo decoded = SystemErrorHandler.Decode(new SystemErrorInfo
        {
            Object = SystemErrorHandler.AssertErrorObject,
            Text = payload,
        });

        // The at-least-two branch ran.
        Assert.Equal(-10000L, decoded.Number);
        Assert.Equal(AssertPayloadTextField, decoded.Text);

        // The exactly-seven branch did not, so the sentinel survives and the other four stay at their
        // pre-decode values.
        Assert.Equal(SystemErrorHandler.AssertErrorObject, decoded.Object);
        Assert.Equal(string.Empty, decoded.WindowMenu);
        Assert.Equal(string.Empty, decoded.ObjectEvent);
        Assert.Equal(0L, decoded.Line);
        Assert.Equal(string.Empty, decoded.StackTrace);

        // And with no stack value there is no call-stack section.
        List<string> lines = [.. SystemErrorHandler.Format(decoded).Body.Split(ReportLineSeparator)];

        Assert.DoesNotContain(StackTraceLabel, lines, StringComparer.Ordinal);
    }


    // --------------------------------------------------------------------------------------------------
    //  2.4  THE ORACLE-DERIVED FIXTURE VALUES
    //
    //  Every value below is restated FROM ws_objects/pfw.pbl.src/pfw.sra rather than read from the
    //  implementation, whose corresponding constants are private. That direction is deliberate for a parity
    //  suite: the test agrees with the behavioural oracle, and the implementation has to agree with the
    //  test. Nothing here is copied out of the legacy tree as CODE - these are the observable literals the
    //  protocol emits, which is data (constraint C-C).
    // --------------------------------------------------------------------------------------------------

    /// <summary>
    /// The field count below which nothing at all is decoded
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra</c>:L116]. The legacy test is AT LEAST, not exactly.
    /// </summary>
    private const int MinimumDecodableFieldCount = 2;

    /// <summary>
    /// The field count that sets the remaining five fields
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra</c>:L119]. The legacy test is EXACT equality, not at-least.
    /// </summary>
    private const int CompleteFieldCount = 7;

    /// <summary>
    /// The payload delimiter [<c>ws_objects/pfw.pbl.src/pfw.sra</c>:L115]: <c>~r~n</c>, that is CRLF.
    /// </summary>
    private const string PayloadFieldDelimiter = "\r\n";

    /// <summary>
    /// The report's line separator [<c>ws_objects/pfw.pbl.src/pfw.sra</c>:L129-L139]: <c>~n</c>, a BARE
    /// line feed. Different from the delimiter above, which is the point of the CRLF-in/LF-out quirk.
    /// </summary>
    private const char ReportLineSeparator = '\n';

    /// <summary>
    /// The report's first line [<c>ws_objects/pfw.pbl.src/pfw.sra</c>:L129], whose type word is a hardcoded
    /// literal even on the fully decoded assertion path.
    /// </summary>
    private const string TypeLine = "类型: SYSTEM";

    /// <summary>The text label [<c>ws_objects/pfw.pbl.src/pfw.sra</c>:L130].</summary>
    private const string TextLabel = "信息: ";

    /// <summary>The conditional window-or-menu label [<c>ws_objects/pfw.pbl.src/pfw.sra</c>:L132].</summary>
    private const string WindowMenuLabel = "窗口: ";

    /// <summary>The object label [<c>ws_objects/pfw.pbl.src/pfw.sra</c>:L134].</summary>
    private const string ObjectLabel = "对象: ";

    /// <summary>The object-event label [<c>ws_objects/pfw.pbl.src/pfw.sra</c>:L135].</summary>
    private const string ObjectEventLabel = "函数: ";

    /// <summary>The line-number label [<c>ws_objects/pfw.pbl.src/pfw.sra</c>:L136].</summary>
    private const string LineLabel = "行号: ";

    /// <summary>
    /// The conditional call-stack label [<c>ws_objects/pfw.pbl.src/pfw.sra</c>:L138]. It carries a colon and
    /// then the separator with NO trailing space, unlike the six value labels above - an inconsistency that
    /// is preserved rather than harmonised.
    /// </summary>
    private const string StackTraceLabel = "调用栈:";

    /// <summary>
    /// Field one of the assert payload: the fixed literal the legacy producer emits.
    /// </summary>
    /// <remarks>
    /// Decoded into the failure number [<c>ws_objects/pfw.pbl.src/pfw.sra</c>:L117] and then never rendered.
    /// It is not a member of the closed return-code set the contract publishes, which is why the wire code
    /// narrows to the catalogue's unknown value rather than widening the contract.
    /// </remarks>
    private const string AssertPayloadNumberField = "-10000";

    /// <summary>Field two: the assertion text.</summary>
    /// <remarks>
    /// Spelled as the oracle's own two-argument assertion helper spells it
    /// [<c>ws_objects/pfw.tests.pbl.src/w_test_assert.srw</c>:L42].
    /// </remarks>
    private const string AssertPayloadTextField = "Invalid Number!";

    /// <summary>Field three: the window-or-menu.</summary>
    /// <remarks>
    /// The oracle's own window name [<c>ws_objects/pfw.tests.pbl.src/w_test_assert.srw</c>], carried as a
    /// scenario label. Deliberately DIFFERENT from field four, so the conditional line is emitted.
    /// </remarks>
    private const string AssertPayloadWindowMenuField = "w_test_assert";

    /// <summary>Field four: the object, which overwrites the sentinel it was discriminated on.</summary>
    private const string AssertPayloadObjectField = "cb_2";

    /// <summary>Field five: the object event.</summary>
    private const string AssertPayloadObjectEventField = "clicked";

    /// <summary>Field six: the line number, which the legacy converts with its own long conversion.</summary>
    private const long AssertPayloadLineNumber = 119L;

    /// <summary>Field seven: the call stack, the only field that gates the call-stack section.</summary>
    private const string AssertPayloadStackTraceField = "wf_testassert2 -> Assertions.Assert";

    /// <summary>
    /// An eighth field, which an EXACT seven-field test must cause to be discarded along with fields three
    /// through seven.
    /// </summary>
    private const string AssertPayloadEighthField = "a field the legacy discards";

    /// <summary>
    /// Every arity worth testing around the exactly-seven boundary, with whether it decodes the remaining
    /// five fields.
    /// </summary>
    /// <remarks>
    /// One below the at-least-two threshold, the threshold itself, one above it, one BELOW seven, seven, and
    /// one ABOVE seven. Six and eight are the two rows that would pass under an at-least-seven test and must
    /// fail under the exact test the legacy actually performs.
    /// </remarks>
    public static TheoryData<int, bool> AssertPayloadArities =>
        new()
        {
            { 1, false },
            { 2, false },
            { 3, false },
            { 6, false },
            { 7, true },
            { 8, false },
        };

    /// <summary>
    /// The window-or-menu and object pairings that decide whether the conditional line is emitted.
    /// </summary>
    /// <remarks>
    /// The third row is the one that matters most: an EMPTY window-or-menu against a non-empty object still
    /// emits the label, because inequality is the only guard. The fourth row is its counterpart - both empty
    /// is still equal, so the line is omitted.
    /// </remarks>
    public static TheoryData<string, string, bool> WindowMenuLineCases =>
        new()
        {
            { AssertPayloadWindowMenuField, AssertPayloadWindowMenuField, false },
            { AssertPayloadWindowMenuField, AssertPayloadObjectField, true },
            { "", AssertPayloadObjectField, true },
            { "", "", false },
        };

    // --------------------------------------------------------------------------------------------------
    //  2.5  PRIVATE HELPERS. No assertion logic is hidden in these beyond what their names state.
    // --------------------------------------------------------------------------------------------------

    /// <summary>
    /// Builds a host whose readiness probe is scripted PER PARTICIPANT NAME.
    /// </summary>
    /// <param name="persistence">The verdict the Persistence participant answers with.</param>
    /// <param name="dataServices">The verdict the DataServices participant answers with.</param>
    /// <param name="security">The verdict the Security participant answers with.</param>
    /// <returns>A fixture the caller owns and must dispose asynchronously.</returns>
    /// <remarks>
    /// <para>
    /// LOCALLY OWNED, NEVER THE SHARED FIXTURE. Reaching a per-participant verdict means registering a
    /// probe, and mutating the shared class fixture would make every other test in this class depend on
    /// execution order. Disposing it asynchronously matters as well: the host's registered shutdown path is
    /// where the framework finalize step lives.
    /// </para>
    /// <para>
    /// The registration is added to the fixture's extension point, which the fixture applies LAST, so it
    /// wins over the fixture's own single-verdict substitute. The prior registration is removed explicitly
    /// rather than relied upon to be overridden, because a container that resolved the first of two
    /// registrations would leave the script silently unused.
    /// </para>
    /// <para>
    /// PRODUCTION IS THE ENVIRONMENT, deliberately: the deployed configuration is the case that matters, and
    /// the readiness gate the orchestration relies on runs there.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Puts the SHIPPED token bootstrap back in place, undoing the fixture's own substitution.
    /// </summary>
    /// <param name="fixture">The host to configure.</param>
    /// <remarks>
    /// The fixture registers a refusing double for the bootstrap so that no authorisation outcome can
    /// depend on the outbound edge. The credential component is decided from the registered provider's
    /// IDENTITY, so the double suppresses it entirely - which is correct behaviour and is asserted in its
    /// own row. Restoring the real client is what makes the production decision observable. It never sends:
    /// the check performs no I/O, and the fixture's transport reaches no socket.
    /// </remarks>
    private static void RestoreShippedTokenBootstrap(GatewayTestHostFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        fixture.AdditionalServiceConfiguration.Add(static services =>
        {
            services.RemoveAll<IServiceTokenProvider>();
            services.AddSingleton<IServiceTokenProvider>(static _ => new SecurityClient(
                new HttpClient { BaseAddress = new Uri("https://security.invalid/", UriKind.Absolute) },
                NullLogger<SecurityClient>.Instance));
        });
    }

    private static GatewayTestHostFixture CreateHostScriptedByParticipantName(
        UpstreamReadiness persistence,
        UpstreamReadiness dataServices,
        UpstreamReadiness security)
    {
        GatewayTestHostFixture fixture = GatewayTestHostFixture.ForEnvironment(Environments.Production);

        ParticipantKeyedReadinessProbe probe = new(
            new Dictionary<string, UpstreamReadiness>(StringComparer.Ordinal)
            {
                [PersistenceParticipant] = persistence,
                [DataServicesParticipant] = dataServices,
                [SecurityParticipant] = security,
            });

        fixture.AdditionalServiceConfiguration.Add(services =>
        {
            services.RemoveAll<IUpstreamReadinessProbe>();
            services.AddSingleton<IUpstreamReadinessProbe>(probe);
        });

        return fixture;
    }

    /// <summary>
    /// Resolves the scripted probe back out of a running host, so its records can be asserted.
    /// </summary>
    /// <param name="fixture">The host to resolve from.</param>
    /// <returns>The scripted probe the host is using.</returns>
    /// <remarks>
    /// The type assertion is load-bearing rather than a cast for convenience: if the registration above ever
    /// stopped winning, this would fail here with a clear cause instead of every verdict quietly reverting to
    /// the shared fixture's single default.
    /// </remarks>
    private static ParticipantKeyedReadinessProbe ResolveScriptedProbe(GatewayTestHostFixture fixture) =>
        Assert.IsType<ParticipantKeyedReadinessProbe>(
            fixture.Services.GetRequiredService<IUpstreamReadinessProbe>());

    /// <summary>Reads a response body as text, once, so the same read serves both parsing and scanning.</summary>
    /// <param name="response">The response to read.</param>
    /// <param name="cancellationToken">The test's cancellation token.</param>
    /// <returns>The body as text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="response"/> is <see langword="null"/>.</exception>
    private static Task<string> ReadBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        return response.Content.ReadAsStringAsync(cancellationToken);
    }

    /// <summary>Reads the per-upstream entries in wire order.</summary>
    /// <param name="root">The parsed body.</param>
    /// <returns>Each upstream's name paired with its status token, in the order the body carried them.</returns>
    private static List<KeyValuePair<string, string>> ReadUpstreamStates(JsonElement root) =>
        ReadNamedStates(root, UpstreamsMember, ServiceMember);

    /// <summary>Reads Gateway's own component entries in wire order.</summary>
    /// <param name="root">The parsed body.</param>
    /// <returns>Each component's name paired with its status token, in the order the body carried them.</returns>
    private static List<KeyValuePair<string, string>> ReadComponentStates(JsonElement root) =>
        ReadNamedStates(root, ChecksMember, NameMember);

    /// <summary>
    /// Reads a named-and-statused array out of the body, preserving ORDER.
    /// </summary>
    /// <param name="root">The parsed body.</param>
    /// <param name="arrayMember">The array member to read.</param>
    /// <param name="nameMember">The member carrying each entry's name.</param>
    /// <returns>The entries, in the order the body carried them.</returns>
    /// <remarks>
    /// An ordered list rather than a dictionary, deliberately: the contract fixes the ORDER of the upstream
    /// array, so a structure whose enumeration order is an implementation detail could not assert it.
    /// </remarks>
    private static List<KeyValuePair<string, string>> ReadNamedStates(
        JsonElement root,
        string arrayMember,
        string nameMember)
    {
        List<KeyValuePair<string, string>> entries = [];

        foreach (JsonElement entry in root.GetProperty(arrayMember).EnumerateArray())
        {
            entries.Add(new KeyValuePair<string, string>(
                entry.GetProperty(nameMember).GetString() ?? string.Empty,
                entry.GetProperty(StatusMember).GetString() ?? string.Empty));
        }

        return entries;
    }

    /// <summary>Reads the body's top-level member names, sorted so the assertion is order-independent.</summary>
    /// <param name="body">The response body as text.</param>
    /// <returns>Every top-level member name, in ordinal order.</returns>
    /// <remarks>
    /// Sorted rather than compared in wire order deliberately: the contract fixes WHICH members a body
    /// carries, not the order a serializer happens to emit them in, so an order-sensitive assertion here
    /// would be a false constraint that a serializer change could break for no reason.
    /// </remarks>
    private static List<string> SortedMemberNames(string body)
    {
        using JsonDocument document = JsonDocument.Parse(body);

        List<string> names = [];

        foreach (JsonProperty member in document.RootElement.EnumerateObject())
        {
            names.Add(member.Name);
        }

        names.Sort(StringComparer.Ordinal);

        return names;
    }

    /// <summary>Projects the names out of a read array, in order.</summary>
    /// <param name="entries">The entries to project.</param>
    /// <returns>The names, in order.</returns>
    private static List<string> NamesOf(List<KeyValuePair<string, string>> entries)
    {
        List<string> names = new(entries.Count);

        foreach (KeyValuePair<string, string> entry in entries)
        {
            names.Add(entry.Key);
        }

        return names;
    }

    /// <summary>Reads the status of exactly one named entry, failing if it is absent or duplicated.</summary>
    /// <param name="entries">The entries to search.</param>
    /// <param name="name">The name to find.</param>
    /// <returns>That entry's status token.</returns>
    /// <remarks>
    /// Requiring exactly one match is deliberate: a duplicated entry would let a body report two different
    /// verdicts for the same participant, and a lookup that took the first would hide it.
    /// </remarks>
    private static string StateOf(List<KeyValuePair<string, string>> entries, string name)
    {
        List<string> matched = [];

        foreach (KeyValuePair<string, string> entry in entries)
        {
            if (string.Equals(entry.Key, name, StringComparison.Ordinal))
            {
                matched.Add(entry.Value);
            }
        }

        return Assert.Single(matched);
    }

    /// <summary>
    /// The per-upstream token a verdict must be published as.
    /// </summary>
    /// <param name="readiness">The observed verdict.</param>
    /// <returns>The published token.</returns>
    /// <remarks>
    /// FOUR TOKENS, one more than the aggregate's three. An upstream from which no verdict could be obtained
    /// keeps its own token here while collapsing to a failure in the aggregate, because the two call for
    /// different operator action - a network or startup-ordering problem rather than a failed dependency
    /// inside a running service.
    /// </remarks>
    private static string ExpectedUpstreamToken(UpstreamReadiness readiness) => readiness switch
    {
        UpstreamReadiness.Healthy => HealthyToken,
        UpstreamReadiness.Degraded => DegradedToken,
        UpstreamReadiness.Unhealthy => UnhealthyToken,
        _ => UnreachableToken,
    };

    /// <summary>
    /// Asserts that every upstream not reporting ready is named in the human-readable member, and that no
    /// ready one is.
    /// </summary>
    /// <param name="root">The parsed body.</param>
    /// <param name="ready">Whether the aggregate reported ready.</param>
    /// <param name="persistence">The Persistence verdict.</param>
    /// <param name="dataServices">The DataServices verdict.</param>
    /// <param name="security">The Security verdict.</param>
    /// <remarks>
    /// THE POINT OF THE AGGREGATE IS DIAGNOSABILITY. An operator reading a failed aggregate needs to know
    /// WHICH upstream is responsible, so the responsible participants are named in prose as well as carried
    /// in the machine-readable array. Asserting that a READY participant is absent is the other half: a
    /// message that named all three whatever their state would not identify anything.
    /// </remarks>
    private static void AssertNotReadyParticipantsAreNamed(
        JsonElement root,
        bool ready,
        UpstreamReadiness persistence,
        UpstreamReadiness dataServices,
        UpstreamReadiness security)
    {
        if (ready)
        {
            // Nothing to name: every participant reported ready, which is the only case that answers 200.
            Assert.Equal(HealthyToken, root.GetProperty(StatusMember).GetString());

            return;
        }

        string detail = root.GetProperty(DetailMember).GetString() ?? string.Empty;

        AssertParticipantNamedOnlyWhenNotReady(detail, PersistenceParticipant, persistence);
        AssertParticipantNamedOnlyWhenNotReady(detail, DataServicesParticipant, dataServices);
        AssertParticipantNamedOnlyWhenNotReady(detail, SecurityParticipant, security);
    }

    /// <summary>Asserts one participant's presence in, or absence from, the prose.</summary>
    /// <param name="detail">The human-readable member.</param>
    /// <param name="participant">The participant's registered name.</param>
    /// <param name="verdict">That participant's verdict.</param>
    private static void AssertParticipantNamedOnlyWhenNotReady(
        string detail,
        string participant,
        UpstreamReadiness verdict)
    {
        if (verdict is UpstreamReadiness.Healthy)
        {
            Assert.DoesNotContain(participant, detail, StringComparison.Ordinal);

            return;
        }

        Assert.Contains(participant, detail, StringComparison.Ordinal);
        Assert.Contains(ExpectedUpstreamToken(verdict), detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// Asserts constraint C-G on the anonymous body: no address, no port, no connection string, no
    /// credential and no internal topology.
    /// </summary>
    /// <param name="body">The response body as text.</param>
    /// <param name="fixture">The host that produced it, read for the addresses it was configured with.</param>
    /// <remarks>
    /// <para>
    /// THE CONFIGURED ADDRESSES ARE READ FROM THE RUNNING HOST rather than restated here, so this keeps
    /// holding when a deployment moves one. Both configuration groups are checked - the two Gateway INVOKES
    /// and the three it OBSERVES - because either would be topology in an anonymous body.
    /// </para>
    /// <para>
    /// The probe channel's logical client name is checked as well. It is not a secret, but it is an internal
    /// detail of how the observation is made, and the closed vocabulary the body publishes does not include
    /// it.
    /// </para>
    /// </remarks>
    private static void AssertBodyDisclosesNothing(string body, GatewayTestHostFixture fixture)
    {
        GatewayOptions options = fixture.Services.GetRequiredService<IOptions<GatewayOptions>>().Value;

        // The correlation identifier is excluded from the scan and asserted OPAQUE instead. See the remarks
        // on TraceIdMember: it is a random hexadecimal value, so a scan for bare port digits over the raw
        // text is not sound while it is present, and it is not disclosure in the first place.
        AssertCorrelationIdentifierIsOpaque(body);

        string scannable = WithoutCorrelationIdentifier(body);

        foreach (string address in (string[])
        [
            options.HealthProbes.Persistence,
            options.HealthProbes.DataServices,
            options.HealthProbes.Security,
            options.Upstreams.DataServices,
            options.Upstreams.Security,
        ])
        {
            Assert.DoesNotContain(address, scannable, StringComparison.OrdinalIgnoreCase);
        }

        foreach (string fragment in ForbiddenBodyVocabulary)
        {
            Assert.DoesNotContain(fragment, scannable, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain(
            HealthEndpoints.ProbeHttpClientName,
            scannable,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Asserts that the correlation identifier, where the body carries one, is opaque: hexadecimal digits
    /// and hyphens and nothing else.
    /// </summary>
    /// <param name="body">The response body as text.</param>
    /// <remarks>
    /// THIS IS WHY EXCLUDING IT FROM THE SCAN IS SAFE. A value restricted to hexadecimal digits and hyphens
    /// cannot carry a host name, a scheme, a path, a connection string or a credential - none of those is
    /// expressible in that alphabet - so the exclusion removes a source of false positives without removing
    /// any source of disclosure. Asserting the alphabet rather than trusting it is what keeps that reasoning
    /// checkable.
    /// </remarks>
    private static void AssertCorrelationIdentifierIsOpaque(string body)
    {
        using JsonDocument document = JsonDocument.Parse(body);

        if (document.RootElement.ValueKind is not JsonValueKind.Object
            || !document.RootElement.TryGetProperty(TraceIdMember, out JsonElement traceId)
            || traceId.ValueKind is not JsonValueKind.String)
        {
            // The ready shape carries no correlation identifier at all, which is equally acceptable: there
            // is no diagnostic record for a successful evaluation to correlate to.
            return;
        }

        string value = traceId.GetString() ?? string.Empty;

        Assert.NotEmpty(value);

        foreach (char character in value)
        {
            bool opaque = character is '-'
                || (character >= '0' && character <= '9')
                || (character >= 'a' && character <= 'f')
                || (character >= 'A' && character <= 'F');

            Assert.True(
                opaque,
                $"The correlation identifier carried a character outside the hexadecimal-and-hyphen "
                    + $"alphabet, so it can no longer be assumed to be opaque: '{character}'.");
        }
    }

    /// <summary>
    /// Returns the body with the correlation identifier's value replaced by a fixed placeholder.
    /// </summary>
    /// <param name="body">The response body as text.</param>
    /// <returns>The body, safe to scan for bare port digits.</returns>
    /// <remarks>
    /// A value replacement rather than a member removal, so the scan still sees the whole of the rest of the
    /// body verbatim - including any member a future edit adds - instead of a reconstructed projection that
    /// could quietly omit one.
    /// </remarks>
    private static string WithoutCorrelationIdentifier(string body)
    {
        using JsonDocument document = JsonDocument.Parse(body);

        if (document.RootElement.ValueKind is not JsonValueKind.Object
            || !document.RootElement.TryGetProperty(TraceIdMember, out JsonElement traceId)
            || traceId.ValueKind is not JsonValueKind.String)
        {
            return body;
        }

        string value = traceId.GetString() ?? string.Empty;

        return value.Length == 0
            ? body
            : body.Replace(value, CorrelationPlaceholder, StringComparison.Ordinal);
    }

    /// <summary>Builds an assert payload carrying the first <paramref name="fieldCount"/> fields.</summary>
    /// <param name="fieldCount">How many fields to emit.</param>
    /// <returns>The CRLF-delimited payload.</returns>
    /// <remarks>
    /// The fields are the seven the legacy producer emits, plus one more so that the exactly-seven test can
    /// be probed from above as well as from below.
    /// </remarks>
    private static string BuildAssertPayload(int fieldCount)
    {
        string[] fields =
        [
            AssertPayloadNumberField,
            AssertPayloadTextField,
            AssertPayloadWindowMenuField,
            AssertPayloadObjectField,
            AssertPayloadObjectEventField,
            AssertPayloadLineNumber.ToString(CultureInfo.InvariantCulture),
            AssertPayloadStackTraceField,
            AssertPayloadEighthField,
        ];

        List<string> emitted = new(fieldCount);

        for (int index = 0; index < fieldCount; index++)
        {
            emitted.Add(fields[index]);
        }

        return string.Join(PayloadFieldDelimiter, emitted);
    }

    /// <summary>Selects the report lines that begin with a given label.</summary>
    /// <param name="lines">The report's lines.</param>
    /// <param name="label">The label to select on.</param>
    /// <returns>Every matching line, in order.</returns>
    /// <remarks>
    /// Written as a loop rather than a query so that the assertion helpers it feeds are the only place a
    /// collection assertion is made - a query inside an assertion is the shape the test analyzers flag.
    /// </remarks>
    private static List<string> LinesStartingWith(List<string> lines, string label)
    {
        List<string> matched = [];

        foreach (string line in lines)
        {
            if (line.StartsWith(label, StringComparison.Ordinal))
            {
                matched.Add(line);
            }
        }

        return matched;
    }

    /// <summary>
    /// Builds a minimal request context for the pure fail-fast assertions.
    /// </summary>
    /// <returns>A context whose response body can be written to.</returns>
    /// <remarks>
    /// NO HOST IS INVOLVED. The exception handler writes a redacted body and, for a structural fault, asks
    /// for termination through its substitutable callback, so a bare context is all it needs - which is
    /// exactly why the fail-fast protocol does not require a web host to assert.
    /// </remarks>
    private static HttpContext CreateFaultContext()
    {
        DefaultHttpContext context = new();

        context.Response.Body = new MemoryStream();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = ReadinessRoute;

        return context;
    }
}
