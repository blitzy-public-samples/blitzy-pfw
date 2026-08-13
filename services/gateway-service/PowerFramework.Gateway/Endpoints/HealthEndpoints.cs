// ======================================================================================================
// HealthEndpoints.cs
// The Gateway service's ANONYMOUS readiness probe: GET /health, contract C-10.
// ======================================================================================================
//
// WHAT THIS FILE IS
//   One route, and the contact point the container orchestration probes. `/health` composes Gateway's
//   own readiness with the individually reported readiness of its three upstreams - Persistence,
//   DataServices and Security - and answers 200 only when every one of them is ready
//   [shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml:L480-L547].
//
//   OpenApi/gateway.v1.yaml is AUTHORITATIVE FOR THE WIRE and was read in full before a single route,
//   status code or payload member was written here. Where it and docs/CONTRACTS.md disagree the
//   document wins; on this operation they agree - CONTRACTS.md 12.2 states the aggregation property and
//   the anonymous posture, ARCHITECTURE.md 4.2 states the status-code mapping - so nothing below had to
//   be adjudicated.
//
// LEGACY REFERENCE, AND WHY THERE IS NO LEGACY ANALOGUE TO PORT (constraint C-C)
//   ws_objects/pfw.pbl.src/pfw.sra is the composition-root reference for this service. TWO distinct
//   files in this repository are named pfw.sra; the other, ws_objects/pfw.pack.pbl.src/pfw.sra, is the
//   PowerBuilder packager and is irrelevant here, which is why the full path is always written out.
//
//       :L88-L106  event open          pfwInitialize(Enums.INIT_FLAG_ENABLE_ALL) is the FIRST statement
//                                      [:L91]; the locale is hardcoded to "en" [:L94].
//       :L108      event close         pfwFinalize() is the ONLY statement.
//       :L111-L144 event systemerror   the seven-field assert-unpacking protocol, ending in
//                                      HALT CLOSE [:L143] - process termination.
//
//   That lifecycle is reproduced under Composition/ and Diagnostics/, not here. There is NO legacy
//   `/health`, and there could not be: PowerFramework is a LIBRARY - 39 PowerBuilder libraries loaded
//   into one process, with no process of its own, no listener, no route table and no server tier. It
//   opens no listening socket and receives no unsolicited request, so this route translates no existing
//   wire format. It is net new, like every route in C-09 and C-10.
//
//   The legacy tree - ws_objects/**, the compiled libraries and targets, oldversion/125/**, pack/**,
//   res/**, samples/**, sciter_control/**, tests/blink|sciter|webview/** and the five Chinese documents
//   under docs/ - is READ ONLY and is the behavioural oracle. This file reads it and changes nothing.
//
// -----------------------------------------------------------------------------------------------------
// C-K DECISION 1 - WHY REST AT THIS BOUNDARY AND NOT gRPC
// -----------------------------------------------------------------------------------------------------
//   Transport was decided per service from the shape of that service's CURRENT interface, not by
//   blanket policy. Gateway's legacy shape is the coarse open / close / systemerror application
//   lifecycle plus a navigation surface: request/response and nothing more - no ordered chain, no veto,
//   nothing to stream.
//
//   Being the SOLE INGRESS then decides it outright, because an ingress needs three properties:
//
//     1. Reach                  a browser or third-party client calls it directly, with no
//                               intermediary of ours in the path;
//     2. Alignment with HTTP    standard caching semantics, standard proxying and standard status
//        itself                 codes that an operator's existing tooling already understands - and
//                               for THIS route in particular, a status code a container orchestrator
//                               and a load balancer already know how to read;
//     3. Mature description     so a consumer generates a client from the published document instead
//        tooling                of hand-writing one.
//
//   gRPC-Web needs a translating proxy in front of it and supports SERVER streaming only, so the reach
//   an ingress exists to provide would depend on an extra hop, and the richer streaming the internal
//   contracts rely on would not survive the translation anyway. The internal east-west edges ARE gRPC
//   precisely because the reasoning inverts there: both ends are owned and the contracts are strongly
//   typed and stable. docs/ARCHITECTURE.md 5 records the decision for all four services.
//
// -----------------------------------------------------------------------------------------------------
// C-K DECISION 2 - THE AGGREGATION GATE, AND THE REJECTED .NET ASPIRE ALTERNATIVE
// -----------------------------------------------------------------------------------------------------
//   "Gateway reports healthy only after its three upstreams do" is expressed in the orchestration
//   manifest as `depends_on:` with `condition: service_healthy` against each upstream, and THIS
//   ENDPOINT is what that condition probes. It is the single most important readiness property in the
//   orchestration, and it is a STATUS-CODE property: what the orchestrator observes is the code, not
//   the body.
//
//   THAT REQUIREMENT IS EXACTLY WHAT RULED OUT .NET ASPIRE'S DOCKER COMPOSE PUBLISHING, and the reason
//   is written down here because a future reader will otherwise "improve" this project by reaching for
//   it. Aspire's Compose environment does generate a compose file and a `.env` from an AppHost model,
//   so it was a genuine candidate. It was rejected on two findings [docs/ARCHITECTURE.md 10.4]:
//
//     1. its Dockerfile-builder APIs remain EXPERIMENTAL and require suppressing an experimental-API
//        diagnostic to use at all; and
//     2. `depends_on` with a `service_healthy` CONDITION was not expressible through the generated
//        model.
//
//   The second is decisive: an orchestration path that cannot express the condition would break the
//   very gate it exists to enforce. Hand-authored orchestration/docker-compose.yml is therefore the
//   SINGLE built orchestration path, with Aspire documented as a considered and rejected alternative
//   rather than quietly omitted.
//
// -----------------------------------------------------------------------------------------------------
// C-K DECISION 3 - THE PERSISTENCE ENTRY: HEALTH OBSERVATION, NOT A CALL PATH
// -----------------------------------------------------------------------------------------------------
//   Gateway REPORTS ON Persistence while NEVER CALLING Persistence. Both halves are contract: C-10
//   requires the aggregate to name Persistence with its own state, and the topology is layered and
//   acyclic - Gateway calls DataServices and Security; DataServices calls Persistence; nothing but
//   DataServices calls Persistence [docs/ARCHITECTURE.md 3.2].
//
//   The two are reconciled IN THE TYPE SYSTEM rather than by a comment, and the reconciliation was
//   already made by Configuration/GatewayOptions.cs before this file existed:
//
//     * `Gateway:Upstreams`    (GatewayOptions.UpstreamAddresses) carries the TWO services Gateway
//                              INVOKES - DataServices and Security - and deliberately has no
//                              Persistence member;
//     * `Gateway:HealthProbes` (GatewayOptions.HealthProbeAddresses) carries THREE addresses that
//                              authorise exactly ONE anonymous endpoint each, `GET /health`, and
//                              nothing else.
//
//   So the resolution this file implements is the FIRST-CHOICE one: the Persistence probe address is
//   read from the group that already declares it. Nothing was invented to make it work -
//   `Gateway:HealthProbes:Persistence` is declared by the options type, is present in both
//   appsettings.json and appsettings.Development.json, and is documented in orchestration/.env.example
//   as `Gateway__HealthProbes__Persistence`. A configuration key invented here instead would have bound
//   to NOTHING and failed SILENTLY, which is the worst possible outcome for a readiness probe: it would
//   have reported one verdict forever with no error anywhere.
//
//   WHAT IS DELIBERATELY ABSENT, and a reviewer can grep for each:
//     * NO Persistence client of any kind - no such type, field, instance or registration exists in
//       this project, and the identifier appears nowhere in this file. Gateway has exactly TWO outbound
//       edges, Clients/DataServicesClient.cs and Clients/SecurityClient.cs. A third client would turn
//       a health report into a functional call path and put SQL generation one hop from the ingress.
//     * NO gRPC channel, generated stub or contract call against Persistence, so nothing beyond the
//       anonymous readiness endpoint is reachable on that address even by accident.
//     * NO probe routed through the two typed clients. Neither exposes a readiness operation -
//       SecurityClient's public surface is token issuance and DataServicesClient's is the C-03/C-04
//       gRPC methods - and routing an OBSERVATION through an INVOCATION client would erase the exact
//       distinction the two configuration groups exist to keep.
//
//   BELT AND BRACES, worth stating because it is independent of this file: the ordering property is
//   enforced end to end by the compose `depends_on: condition: service_healthy` chain regardless of how
//   this endpoint sources any entry. This aggregate is the readable, machine-checkable statement of the
//   same property, not the only mechanism holding it up.
//
// -----------------------------------------------------------------------------------------------------
// BOUNDARY 1 - AGGREGATION MUST NOT GATE PROCESS STARTUP
// -----------------------------------------------------------------------------------------------------
//   The application MUST start with every upstream unreachable and simply report unhealthy. The compose
//   dependency condition is ORCHESTRATION, not startup. So: no blocking upstream call during host
//   startup, no reachability check at startup, no throw from a constructor or a hosted service because
//   an upstream is down, and no retry loop that delays Run. Every probe here happens INSIDE a request,
//   under a bounded timeout, and a failure to reach an upstream is REPORTED rather than thrown.
//
//   THIS DOES NOT CONTRADICT THE SERVICE'S FAIL-FAST POSTURE, and the two must not be conflated.
//   Fail-fast applies to STRUCTURAL faults: the legacy terminates on a decoded assertion failure,
//   unpacking a seven-field payload split on CRLF and then executing HALT CLOSE
//   [ws_objects/pfw.pbl.src/pfw.sra:L143], and Composition/FrameworkInitializer.cs plus
//   Diagnostics/SystemErrorHandler.cs reproduce that for startup validation and for an unhandled
//   structural fault respectively. AN UNREACHABLE UPSTREAM IS NOT A STRUCTURAL FAULT - it is precisely
//   what this endpoint exists to report. Nothing here softens the structural fail-fast, and nothing
//   here escalates a transient network condition into one.
//
// -----------------------------------------------------------------------------------------------------
// BOUNDARY 2 - THE ANONYMOUS BODY DISCLOSES NOTHING (constraint C-G)
// -----------------------------------------------------------------------------------------------------
//   `/health` is anonymous, so its body is world-readable by anything that can reach the port. It
//   therefore carries a CLOSED VOCABULARY declared in this file and nothing else: three logical
//   upstream names, four status tokens, three component-check names and a fixed set of authored
//   sentences. It carries no host name, no IP address, no port, no URL, no connection string, no
//   authority or key-set address, no token, no credential, no environment value, no exception message,
//   no stack trace and no probe address - not even the one it just used.
//
//   Detail belongs on the OPERATOR CHANNEL, which is the convention Diagnostics/SystemErrorHandler.cs
//   already establishes for this service: full detail to the log, a redacted body to the caller. The
//   audience for a log record is authenticated by having access to the service's logs rather than by
//   being able to reach a port, which is why the two get different amounts of information.
//
//   One consequence, stated because it is easy to miss: an upstream's own `detail` text is NOT echoed
//   here. Each upstream promises the same discipline for its own body, but "the upstream promised" is
//   not a control this file can enforce, and a forwarded string is exactly how a provider name, a host
//   or - in the data path - an unredacted SQL statement would reach an anonymous reader. The entry
//   states which upstream and in what coarse state, in this file's own words.
//
// WHAT THIS FILE DELIBERATELY DOES NOT DO
//   * No health-check or URI-probe PACKAGE reference. Health-check registration ships inside the
//     Microsoft.AspNetCore.App shared framework, and Directory.Packages.props declares no such package
//     at all - under central package management a reference without a matching PackageVersion fails
//     restore outright, so the shared framework is the only available route rather than merely the
//     preferred one.
//   * No entry, probe, address, placeholder or "unknown" row for DesignSystem, Documents, Integration
//     or ScriptBridge, and no occurrence of the reserved Phase-2 port anywhere in this file. The
//     aggregate is bounded at exactly three upstreams by the contract itself (constraint C-D).
//   * No database probe. No connection string, no DbContext, no `SELECT 1`. Gateway holds no storage
//     provider; database health belongs to Persistence, which owns the only one in the system (C-E).
//   * No metric, uptime counter, build or version banner, dependency roll-up or latency figure. The
//     repository publishes no service-level agreement, no latency budget, no throughput target and no
//     availability commitment anywhere, so NO PERFORMANCE, LATENCY, THROUGHPUT OR AVAILABILITY CLAIM IS
//     MADE OR IMPLIED by anything below - a health payload must not imply one (C-B).
//   * No authorization requirement. `/health` is the ONE deliberately unauthenticated route in the
//     whole Gateway surface, and `/v1/ping` - the authenticated probe - is Endpoints/PingEndpoints.cs's
//     job. The two are not merged, because their opposite postures are what makes each testable.
//   * No SCREAMING_SNAKE constant is DECLARED here. The repository .editorconfig scopes its
//     naming-analyzer suppressions to a fixed list of files carrying preserved legacy identifiers and
//     no Gateway file is on it, while Directory.Build.props sets TreatWarningsAsErrors, so such a
//     declaration would be a BUILD ERROR. The preserved identifiers are CONSUMED from
//     PowerFramework.Shared.Kernel instead - here, RetCode.E_RETRY.
//
// TESTABILITY (constraint C-H: 80% line coverage per in-scope service)
//   Nothing here needs a live upstream, a database, a container or a real clock. Every collaborator is
//   resolved from the request's services: IUpstreamReadinessProbe (substituted wholesale by a test),
//   TimeProvider (the determinism seam), HealthCheckService, FrameworkInitializer and IHttpClientFactory
//   - the last five OPTIONALLY, so the probe answers on a host that registered none. That is what lets
//   one suite reach every branch: all three healthy, each one down individually, all three down, a
//   degraded upstream, an unreachable one, a hung one that trips the bounded timeout, and a faulted
//   evaluation.
// ======================================================================================================

using System.Globalization;
using System.Net;
using System.Net.Mime;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using PowerFramework.Gateway.Clients;
using PowerFramework.Gateway.Composition;
using PowerFramework.Gateway.Configuration;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Gateway.Endpoints;

/// <summary>
/// Maps <c>GET /health</c>, the anonymous aggregated readiness probe of contract C-10.
/// </summary>
/// <remarks>
/// <para>
/// Gateway reports <c>Healthy</c> only after Persistence, DataServices and Security all report healthy,
/// and only <c>Healthy</c> is answered with <c>200</c>. <c>Degraded</c> and <c>Unhealthy</c> are both
/// answered with <c>503</c> and remain distinguishable in the response, because what a container
/// orchestrator observes is the status <b>code</b>: a not-ready service answering <c>200</c> would
/// satisfy a <c>service_healthy</c> dependency condition and open the gate early, which is the one
/// ordering property that gate exists to enforce.
/// </para>
/// <para>
/// The class is static and holds no state. It registers no authentication scheme, no options type and
/// no client, opens no connection at startup and reads no configuration outside the request path -
/// everything it needs is resolved from the request's own service provider so that a test can substitute
/// any of it, and so that a host which registered none of it still answers.
/// </para>
/// <para>
/// Read the decision record at the head of this file - in particular C-K decision 3 and both boundary
/// notes - before changing anything here. Each of the three is a resolved tension rather than an
/// accident, and the resolutions are not locally obvious.
/// </para>
/// </remarks>
public static class HealthEndpoints
{
    /// <summary>
    /// The logical name of the <see cref="HttpClient"/> the default upstream probe resolves from
    /// <see cref="IHttpClientFactory"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PUBLIC DELIBERATELY, AND THE ONLY PUBLIC CONSTANT THIS TYPE DECLARES. It is the seam through
    /// which a deployment configures the probe channel - a resilience handler, a certificate-trust
    /// arrangement, a proxy - and through which a test substitutes a message handler, without either
    /// having to restate the name and risk a silent typo that would leave the default client in place.
    /// </para>
    /// <para>
    /// Registration is OPTIONAL. With no matching registration the factory returns a default client,
    /// which is sufficient because every probe is issued against an absolute address and is bounded by
    /// <see cref="ProbeTimeout"/> here rather than by the client's own timeout.
    /// </para>
    /// </remarks>
    public const string ProbeHttpClientName = "gateway-health-probe";

    /// <summary>
    /// The route template, spelled exactly as the contract spells it.
    /// </summary>
    /// <remarks>
    /// <b><c>/health</c> and NOT <c>/v1/health</c>.</b> Every other route in this service is versioned;
    /// this one is not, and the contract says why: a readiness probe is spelled by convention rather
    /// than negotiated, the thing that probes it is an orchestrator or a load balancer rather than a
    /// generated client, and versioning it would break the health gate for no benefit - a probe has
    /// nothing to negotiate. One route and one method: no <c>HEAD</c> companion, no trailing-slash
    /// variant and no alias, because an ingress with an undocumented surface is what C-G forbids.
    /// </remarks>
    private const string RoutePattern = "/health";

    /// <summary>
    /// The contract's <c>operationId</c>. Also the endpoint name, which is what the OpenAPI document
    /// generator projects into <c>operationId</c>.
    /// </summary>
    private const string OperationId = "getHealth";

    /// <summary>
    /// The contract's tag for this operation.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT the diagnostics tag that <c>/v1/ping</c> carries, even though both operations
    /// belong to C-10: the two halves of that contract have opposite authentication postures, and the
    /// contract separates them so a reader scanning the groupings sees that rather than inferring it.
    /// </remarks>
    private const string HealthTag = "Health";

    /// <summary>
    /// The specification extension the contract carries on every operation to name its contract.
    /// </summary>
    private const string ContractIdExtensionName = "x-contract-id";

    /// <summary>
    /// The contract this operation belongs to: the health and readiness contract.
    /// </summary>
    private const string ContractId = "C-10";

    /// <summary>
    /// The status key of the ready response in an OpenAPI responses map.
    /// </summary>
    private const string ReadyStatusKey = "200";

    /// <summary>
    /// The status key of the not-ready response in an OpenAPI responses map.
    /// </summary>
    private const string NotReadyStatusKey = "503";

    /// <summary>
    /// The Persistence upstream's name on the wire, from the contract's closed enumeration.
    /// </summary>
    /// <remarks>
    /// Gateway reports on Persistence and never calls it. See C-K decision 3 in the file header: the
    /// probe address comes from <c>Gateway:HealthProbes</c>, which authorises one anonymous endpoint and
    /// nothing else, and this project holds no Persistence client of any kind.
    /// </remarks>
    private const string PersistenceParticipant = "persistence";

    /// <summary>
    /// The DataServices upstream's name on the wire, from the contract's closed enumeration.
    /// </summary>
    private const string DataServicesParticipant = "dataservices";

    /// <summary>
    /// The Security upstream's name on the wire, from the contract's closed enumeration.
    /// </summary>
    private const string SecurityParticipant = "security";

    /// <summary>
    /// Fully ready. The only verdict answered with <c>200</c>.
    /// </summary>
    private const string StatusHealthy = "Healthy";

    /// <summary>
    /// Not ready, but not failed - Gateway's own startup validation has not completed, or an upstream
    /// reports the same of itself. Answered with <c>503</c>.
    /// </summary>
    private const string StatusDegraded = "Degraded";

    /// <summary>
    /// Failed. Answered with <c>503</c>.
    /// </summary>
    private const string StatusUnhealthy = "Unhealthy";

    /// <summary>
    /// An upstream-only verdict: no readiness verdict could be obtained from it at all.
    /// </summary>
    /// <remarks>
    /// Kept distinct from <see cref="StatusUnhealthy"/> in the per-upstream entry because the two call
    /// for different operator action - a network or startup-ordering problem rather than a failed
    /// dependency inside a running service - even though both collapse to <c>Unhealthy</c> in the
    /// aggregate, whose vocabulary the contract closes at three tokens.
    /// </remarks>
    private const string StatusUnreachable = "Unreachable";

    /// <summary>
    /// The component entry standing for this process answering at all.
    /// </summary>
    private const string SelfCheckName = "self";

    /// <summary>
    /// The component entry standing for the framework lifecycle's startup validation.
    /// </summary>
    private const string FrameworkCheckName = "framework";

    /// <summary>
    /// The component entry naming whether Gateway holds the credential material its token bootstrap
    /// requires.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY IT IS A COMPONENT OF READINESS AND NOT MERELY CONFIGURATION. Gateway forwards every proxied
    /// operation to DataServices with a bearer token, and the only way to obtain one is Security's
    /// issuance edge - which contract C-01 protects with EITHER OF TWO CREDENTIALS, an HTTP Basic client
    /// credential or a client certificate, and requires one of them, because a caller cannot present a
    /// bearer token in order to obtain its first bearer token. A deployment presenting NEITHER is a state
    /// in which every proxied operation is refused for want of a caller identity, so reporting ready in it
    /// is the readiness endpoint answering its own question wrongly. Which of the two schemes satisfies
    /// this component, and why requiring the certificate pair alone was an outage rather than strictness,
    /// is recorded on <see cref="EvaluateCredentialReadiness"/>.
    /// </para>
    /// <para>
    /// Named individually rather than folded into <see cref="ComponentsCheckName"/> for the same reason
    /// <see cref="FrameworkCheckName"/> is: an operator polling this route needs to know WHICH
    /// precondition is unmet, and the aggregated entry deliberately says only that one is.
    /// </para>
    /// </remarks>
    private const string CredentialsCheckName = "credentials";

    /// <summary>
    /// The single aggregated component entry standing for every registered health check.
    /// </summary>
    /// <remarks>
    /// ONE ENTRY FOR ALL OF THEM, NOT ONE PER CHECK. A per-check entry would publish the shape of this
    /// service's dependency graph, and its name and description are supplied by whatever registered the
    /// check rather than authored for disclosure - so a name like <c>npgsql-primary-eu-west-1</c> would
    /// reach an anonymous reader. The registration-supplied names go to the operator channel instead.
    /// </remarks>
    private const string ComponentsCheckName = "components";


    /// <summary>The authored note on the self entry when the evaluation completed.</summary>
    private const string SelfReadyDetail = "The process is running and answering requests.";

    /// <summary>The authored note on the self entry when the evaluation itself faulted.</summary>
    private const string SelfFaultedDetail = "Readiness evaluation did not complete.";

    /// <summary>The authored note on the framework entry when initialization has completed.</summary>
    private const string FrameworkReadyDetail = "Framework initialization completed.";

    /// <summary>The authored note on the framework entry when it has not.</summary>
    private const string FrameworkNotReadyDetail =
        "The framework lifecycle is not in its initialized state.";

    /// <summary>The detail for a Gateway holding the credential material it needs.</summary>
    private const string CredentialsReadyDetail =
        "The credential material needed to obtain a service token is configured.";

    /// <summary>
    /// The detail for a Gateway that cannot obtain a token. It names WHAT is missing in capability terms
    /// and never a path, a certificate subject or a configuration key: this route is anonymous, and a path
    /// names where private key material is mounted. The setting names go to the operator channel.
    /// </summary>
    private const string CredentialsNotReadyDetail =
        "The credential material needed to obtain a service token is not configured, so every proxied "
        + "operation would be refused.";

    /// <summary>The detail for the aggregated component entry when every registered check is ready.</summary>
    private const string ComponentsReadyDetail = "Every registered component check reports ready.";

    /// <summary>The authored note on the aggregated component entry when any check is not.</summary>
    private const string ComponentsNotReadyDetail =
        "At least one registered component check does not report ready.";

    /// <summary>The authored note on an upstream entry whose own verdict is ready.</summary>
    private const string UpstreamReadyDetail = "The upstream reports that it is ready.";

    /// <summary>The authored note on an upstream entry whose own verdict is not-ready-but-not-failed.</summary>
    private const string UpstreamDegradedDetail = "The upstream reports that it is not ready.";

    /// <summary>The authored note on an upstream entry whose own verdict is failure.</summary>
    private const string UpstreamUnhealthyDetail = "The upstream reports that it has failed.";

    /// <summary>The authored note on an upstream from which no verdict could be obtained.</summary>
    /// <remarks>
    /// Says nothing about WHY, deliberately: the cause - a refused connection, an unresolved name, a
    /// rejected certificate, an exhausted timeout - is exactly the kind of topology detail an anonymous
    /// reader must not receive. The cause is written to the operator channel instead.
    /// </remarks>
    private const string UpstreamUnreachableDetail =
        "No readiness verdict could be obtained from the upstream.";

    /// <summary>
    /// The problem document's title on the not-ready response. Stable across occurrences, as RFC 9457
    /// requires of a title.
    /// </summary>
    private const string NotReadyProblemTitle = "Service Unavailable";

    /// <summary>
    /// The one extension member the contract's <c>ProblemDetails</c> declares for the legacy return
    /// code.
    /// </summary>
    private const string RetCodeExtensionMember = "retCode";

    /// <summary>The reporting-service member, carried onto the not-ready problem document.</summary>
    private const string ServiceExtensionMember = "service";

    /// <summary>The per-upstream array member, carried onto the not-ready problem document.</summary>
    private const string UpstreamsExtensionMember = "upstreams";

    /// <summary>The component-check array member, carried onto the not-ready problem document.</summary>
    private const string ChecksExtensionMember = "checks";

    /// <summary>The evaluation-time member, carried onto the not-ready problem document.</summary>
    private const string CheckedAtExtensionMember = "checkedAt";

    /// <summary>
    /// The readiness-verdict member, carried onto the not-ready problem document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A NAME OF ITS OWN, BECAUSE <c>status</c> IS ALREADY TAKEN AND IS A DIFFERENT TYPE. RFC 9457 uses
    /// <c>status</c> for the integer HTTP status and the contract's schema types it that way, so the
    /// verdict token cannot occupy that name on a problem document. Without a member of its own the
    /// token would exist only inside the prose of <c>detail</c>, where the sole machine-readable member
    /// left is <c>retCode</c> - and that carries <see cref="RetCode.E_RETRY"/> for
    /// <c>Degraded</c> and for <c>Unhealthy</c> alike, so an aggregator could not tell them apart.
    /// </para>
    /// <para>
    /// The same spelling is emitted by all four services, because C-10 publishes one readiness shape,
    /// and <see cref="HttpUpstreamReadinessProbe"/> reads it off the three upstreams' not-ready bodies.
    /// </para>
    /// </remarks>
    private const string ServiceStatusExtensionMember = "serviceStatus";

    /// <summary>
    /// The member an upstream's own readiness body carries its verdict in.
    /// </summary>
    /// <remarks>
    /// The same spelling on all four services, because C-10 publishes one readiness shape. It is read
    /// only when it is a JSON STRING: RFC 9457 uses the same member name for the integer HTTP status, so
    /// a not-ready upstream answering a problem document would otherwise be misread as carrying a token.
    /// </remarks>
    private const string ReportedStatusMember = "status";

    /// <summary>
    /// The logger category for the operator channel.
    /// </summary>
    /// <remarks>
    /// A category NAME rather than <c>ILogger&lt;T&gt;</c>, because a generic logger cannot name a static
    /// class and this class is deliberately static.
    /// </remarks>
    private const string LoggerCategoryName = "PowerFramework.Gateway.Endpoints.HealthEndpoints";

    /// <summary>
    /// The largest readiness body this endpoint will read from an upstream, in bytes.
    /// </summary>
    /// <remarks>
    /// A readiness body is a handful of members, so this is generous by two orders of magnitude while
    /// still bounding the read. It exists so that a misbehaving or compromised upstream cannot make a
    /// probe allocate without limit - the probe is the one operation in this service that a caller
    /// holding no credential can cause to happen.
    /// </remarks>
    private const int MaxProbeBodyBytes = 4096;

    /// <summary>
    /// The operation summary, verbatim from the contract.
    /// </summary>
    private const string OperationSummary =
        "Report aggregated readiness across Gateway and its three upstreams. Anonymous.";

    /// <summary>
    /// The ready response's description, verbatim from the contract.
    /// </summary>
    private const string ReadyResponseDescription =
        "Gateway is `Healthy`, which is the only verdict answered with 200: Gateway itself and all "
        + "three upstreams are ready. The body names each upstream with its own state.";

    /// <summary>
    /// The not-ready response's description, carried across from the contract.
    /// </summary>
    private const string NotReadyResponseDescription =
        "Gateway is **not ready** - `Degraded` or `Unhealthy`. One of its three upstreams is not ready "
        + "or has failed, or Gateway's own startup validation has not completed. The participant "
        + "responsible is named in the problem `detail` and `retCode` carries the corresponding legacy "
        + "return code.";

    /// <summary>
    /// The operation description, carried across from the contract so that the generated document and
    /// the hand-authored one say the same thing to a consumer.
    /// </summary>
    private const string OperationDescription = """
        Reports Gateway's readiness **and that of each of its three upstreams individually**.

        **Gateway reports healthy only after Persistence, DataServices and Security all report
        healthy.** The ordering is expressed in the orchestration manifest as a dependency condition
        on upstream health, and it is the readiness property that ruled out the alternative
        orchestration approach recorded in `docs/ARCHITECTURE.md` 10.4 - the generated model could not
        express it.

        **The aggregate names each upstream and its own state rather than returning one opaque
        verdict.** An operator reading a failed aggregate needs to know *which* upstream is not ready.

        **This endpoint is anonymous, on all four services.** The thing that probes it holds no token,
        and it probes precisely during the window in which the service is still starting.

        **`Degraded` and `Unhealthy` are different states, and the difference is carried in the body
        rather than in the status code.** Only `Healthy` is answered with 200; `Degraded` means *not
        ready* and is answered with 503 alongside `Unhealthy`, because what an orchestrator observes is
        the status code.
        """;

    /// <summary>
    /// The budget for the whole upstream-probe round, applied to all three probes together.
    /// </summary>
    /// <remarks>
    /// <para>
    /// BOUNDED SO THAT A HUNG UPSTREAM CANNOT HANG THE PROBE. The three probes run concurrently under
    /// one linked token, so the round costs one budget rather than three, and an upstream that accepts a
    /// connection and then never answers is reported <c>Unreachable</c> when the budget expires instead
    /// of holding this request open until the orchestrator's own probe times out - which would make the
    /// gate read a timeout rather than a verdict.
    /// </para>
    /// <para>
    /// It is a fixed value in this file rather than a configuration key, deliberately. No such key is
    /// declared by <see cref="GatewayOptions"/>, by either settings file or by the orchestration
    /// environment file, and a key invented here would bind to nothing and fail silently. This is also
    /// NOT a retry policy: retry belongs to the resilience handler a deployment attaches to the named
    /// probe client, and retrying hard enough to outlive the orchestrator's probe interval would defeat
    /// the gate.
    /// </para>
    /// </remarks>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);


    /// <summary>
    /// Registers <c>GET /health</c> together with its anonymous posture and the OpenAPI metadata that
    /// makes the generated document agree with <c>OpenApi/gateway.v1.yaml</c>.
    /// </summary>
    /// <param name="endpoints">The route builder the composition root is populating.</param>
    /// <returns>
    /// The same <paramref name="endpoints"/> instance, so the composition root can chain its endpoint
    /// registrations.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// The route definition lives here rather than in the composition root, so that the whole of this
    /// operation's contract - its path, its method, its anonymous posture, its response surface and its
    /// published metadata - is reviewable in one place.
    /// </para>
    /// <para>
    /// <b>The return type is deliberately the route builder and not the route handler builder.</b>
    /// Returning the handler builder would let a caller in another file append
    /// <c>RequireAuthorization</c> to the one route that must never require a token, which would break
    /// every readiness gate in the orchestration from a file no reader of this one would think to check.
    /// Withholding it makes that impossible.
    /// </para>
    /// </remarks>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(RoutePattern, ReportAggregateReadinessAsync)

            // C-G's ONE deliberate, contract-mandated exception, and EXPLICIT rather than implied. The
            // composition root installs a fallback authorization policy so that no route is ever
            // anonymous by omission; this call is what keeps THIS route anonymous under that policy, and
            // it is what keeps it anonymous if the default policy is later tightened again. Its safety
            // comes from disclosing nothing, not from authentication - see boundary note 2 in the header.
            .AllowAnonymous()

            // Published metadata. WithName supplies the contract's operationId as well as the endpoint
            // name; the two Produces calls state the response surface where a reviewer diffing against
            // the contract will look for it.
            .WithName(OperationId)
            .WithTags(HealthTag)
            .WithSummary(OperationSummary)
            .WithDescription(OperationDescription)
            .Produces<AggregateHealthReport>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)
            .ProducesProblem(
                StatusCodes.Status503ServiceUnavailable,
                MediaTypeNames.Application.ProblemJson)

            // The parts of the contract that endpoint metadata alone cannot express: the specification
            // extension, the two response descriptions, and the empty security requirement list that
            // declares the anonymous posture in the document as well as in the pipeline.
            .AddOpenApiOperationTransformer(ApplyContractMetadataAsync);

        return endpoints;
    }

    /// <summary>
    /// Composes Gateway's own readiness with the individually reported readiness of its three upstreams
    /// and answers the contract's ready or not-ready response.
    /// </summary>
    /// <param name="httpContext">
    /// The current request, used only to resolve collaborators from its service provider. Nothing is
    /// read from the request itself: this operation takes no parameter, no header and no body, so no
    /// caller-supplied value can influence what it probes.
    /// </param>
    /// <param name="cancellationToken">
    /// The request's cancellation token. Linked into the probe budget so that a caller who disconnects
    /// stops the outbound probes rather than leaving them to run to completion unobserved.
    /// </param>
    /// <returns>
    /// <c>200</c> carrying an <see cref="AggregateHealthReport"/> when Gateway and all three upstreams
    /// are ready, and <c>503</c> carrying a problem document otherwise - <c>Degraded</c> included,
    /// because not ready is not ready and the orchestration gate reads the status code.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="httpContext"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// THIS METHOD REPORTS AND DOES NOT THROW. A probe that faulted would take the container with it
    /// through the host's unhandled-exception path and the readiness gate could never recover, so every
    /// failure mode below resolves to a verdict. That is not a softening of the service's fail-fast
    /// posture: fail-fast governs STRUCTURAL faults, and an unreachable upstream is not one - it is the
    /// thing this endpoint exists to report.
    /// </para>
    /// <para>
    /// Every collaborator is resolved from <see cref="HttpContext.RequestServices"/> rather than taken as
    /// a handler parameter. On a <c>GET</c>, a complex parameter that the framework cannot resolve from
    /// the container is inferred as a request body and fails while the endpoint is being built, so a host
    /// that had not registered one of the optional collaborators would fail to START rather than answer
    /// the probe - the exact regression boundary note 1 forbids.
    /// </para>
    /// </remarks>
    private static async Task<IResult> ReportAggregateReadinessAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        IServiceProvider services = httpContext.RequestServices;
        ILogger logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerCategoryName);

        // The determinism seam. Resolved optionally so that a test can substitute a deterministic clock
        // and assert an exact value, while a deployment that registers none still works. The
        // characterization model requires every clock read to be maskable from both the master and the
        // candidate recording, which is why no ambient DateTimeOffset.UtcNow appears anywhere here.
        TimeProvider clock = services.GetService<TimeProvider>() ?? TimeProvider.System;

        (HealthStatus ownStatus, IReadOnlyList<ComponentHealthCheck> checks) =
            await EvaluateOwnReadinessAsync(services, logger, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<UpstreamHealth> upstreams =
            await ProbeUpstreamsAsync(services, logger, cancellationToken).ConfigureAwait(false);

        HealthStatus aggregate = ownStatus;

        foreach (UpstreamHealth upstream in upstreams)
        {
            aggregate = Worse(aggregate, SeverityOf(upstream.Status));
        }

        string wireStatus = ToWireStatus(aggregate);
        DateTimeOffset checkedAt = clock.GetUtcNow();

        WriteOperatorRecord(logger, wireStatus, checks, upstreams);

        // FAILS CLOSED: only Healthy is reported ready. Testing FOR the single ready state rather than
        // against the failed ones is what makes an unrecognised status fail closed too.
        if (aggregate is not HealthStatus.Healthy)
        {
            return BuildNotReadyProblem(wireStatus, checks, upstreams, checkedAt);
        }

        return TypedResults.Ok(new AggregateHealthReport
        {
            Status = wireStatus,
            CheckedAt = checkedAt,
            Upstreams = upstreams,
            Checks = checks,
        });
    }


    /// <summary>
    /// Evaluates Gateway's OWN contribution to the verdict: that the process is answering, that the
    /// framework lifecycle completed its startup validation, and that every registered component check
    /// reports ready.
    /// </summary>
    /// <param name="services">The request's service provider.</param>
    /// <param name="logger">The operator channel.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>
    /// Gateway's own status and the component entries behind it, named ONLY from this file's closed
    /// vocabulary.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The framework entry is what makes the contract's <c>Degraded</c> verdict reachable rather than
    /// theoretical: the contract names "Gateway's own startup validation has not completed" as a cause of
    /// not-ready, and <see cref="FrameworkInitializer"/> IS that validation - it reproduces the mandatory
    /// initialize/finalize pairing the legacy documentation requires. The initializer is resolved
    /// OPTIONALLY, so a host that registered none simply has no framework entry rather than a fabricated
    /// verdict about one.
    /// </para>
    /// <para>
    /// A finalized lifecycle is reported not-ready as well as an uninitialized one. During graceful
    /// shutdown the process still answers while it drains, and a draining instance that reported ready
    /// would keep receiving traffic it is about to stop serving.
    /// </para>
    /// <para>
    /// The credential entry is the second reachable cause of <c>Degraded</c>, and it is a readiness
    /// question rather than a configuration one: every proxied operation is forwarded with a bearer token,
    /// and a Gateway with no client certificate mounted cannot obtain one at all. It too is evaluated only
    /// when there is something to evaluate - see <see cref="EvaluateCredentialReadiness"/>.
    /// </para>
    /// <para>
    /// <see cref="HealthCheckService"/> is likewise optional. The composition root registers it, but this
    /// probe must answer whether or not it did: requiring it would let a missing registration turn a
    /// readiness probe into a startup gate, which boundary note 1 forbids.
    /// </para>
    /// </remarks>
    private static async Task<(HealthStatus Status, IReadOnlyList<ComponentHealthCheck> Checks)>
        EvaluateOwnReadinessAsync(
            IServiceProvider services,
            ILogger logger,
            CancellationToken cancellationToken)
    {
        List<ComponentHealthCheck> checks = [];
        HealthStatus status = HealthStatus.Healthy;

        try
        {
            // Truthful rather than assumed: the execution of this method IS the demonstration that the
            // process is running and answering requests.
            checks.Add(new ComponentHealthCheck
            {
                Name = SelfCheckName,
                Status = StatusHealthy,
                Detail = SelfReadyDetail,
            });

            FrameworkInitializer? initializer = services.GetService<FrameworkInitializer>();

            if (initializer is not null)
            {
                bool frameworkReady = initializer.IsInitialized && !initializer.IsFinalized;
                HealthStatus frameworkStatus =
                    frameworkReady ? HealthStatus.Healthy : HealthStatus.Degraded;

                status = Worse(status, frameworkStatus);

                checks.Add(new ComponentHealthCheck
                {
                    Name = FrameworkCheckName,
                    Status = ToWireStatus(frameworkStatus),
                    Detail = frameworkReady ? FrameworkReadyDetail : FrameworkNotReadyDetail,
                });
            }

            if (EvaluateCredentialReadiness(services, logger) is (HealthStatus, ComponentHealthCheck)
                credentials)
            {
                status = Worse(status, credentials.Item1);
                checks.Add(credentials.Item2);
            }

            HealthCheckService? healthChecks = services.GetService<HealthCheckService>();

            if (healthChecks is not null)
            {
                HealthReport report =
                    await healthChecks.CheckHealthAsync(cancellationToken).ConfigureAwait(false);

                // An empty report contributes no entry. The composition root registers the health-check
                // service without registering a check, so the empty case is the ordinary one, and an
                // entry standing for nothing would be noise in a body that is read by a machine.
                if (report.Entries.Count > 0)
                {
                    status = Worse(status, report.Status);

                    checks.Add(new ComponentHealthCheck
                    {
                        Name = ComponentsCheckName,
                        Status = ToWireStatus(report.Status),
                        Detail = report.Status is HealthStatus.Healthy
                            ? ComponentsReadyDetail
                            : ComponentsNotReadyDetail,
                    });

                    WriteComponentRecord(logger, report);
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The exception is logged HERE, where the audience is an operator with access to the logs,
            // and is deliberately NOT projected onto the anonymous response. OperationCanceledException
            // is excluded on purpose: it means the caller disconnected, so there is no longer a response
            // worth composing and the framework's own handling is correct.
            logger.LogError(
                exception,
                "Gateway readiness evaluation faulted; reporting {Status}.",
                StatusUnhealthy);

            return (
                HealthStatus.Unhealthy,
                [
                    new ComponentHealthCheck
                    {
                        Name = SelfCheckName,
                        Status = StatusUnhealthy,
                        Detail = SelfFaultedDetail,
                    },
                ]);
        }

        return (status, checks);
    }

    /// <summary>
    /// Establishes whether Gateway holds the credential material its token bootstrap requires, without
    /// requesting a token or performing any I/O at all.
    /// </summary>
    /// <param name="services">The request's service provider.</param>
    /// <param name="logger">The operator channel, where the unmet setting names are recorded.</param>
    /// <returns>
    /// The entry and the status it contributes, or <see langword="null"/> when this file has nothing to
    /// say - which is the case when a host supplied its own bootstrap or bound no options.
    /// </returns>
    /// <remarks>
    /// <para>
    /// NO I/O, AT ALL. No token is minted, no handshake is attempted, no metadata is fetched and no file
    /// is opened. It reads two bound configuration values. A probe that minted a token would mutate the
    /// client's credential cache and put load on the issuance edge from an anonymous, unauthenticated
    /// route - and this endpoint is the one operation a caller holding no credential can cause to happen.
    /// </para>
    /// <para>
    /// IT DOES NOT ASK WHETHER SECURITY IS REACHABLE. That is an upstream question, and the three upstream
    /// entries already answer it; duplicating it here would make one outage produce two not-ready reasons.
    /// This asks the purely local question: is the material here.
    /// </para>
    /// <para>
    /// NOTHING IS REPORTED WHEN A HOST SUPPLIED ITS OWN <see cref="IServiceTokenProvider"/>, on exactly the
    /// same terms as the framework entry when no initializer is registered. Such a host obtains credentials
    /// by a mechanism whose preconditions this file cannot know, so a verdict about them would be a
    /// fabrication - and an absent entry says "nothing to report" where a fabricated one would say
    /// something false. Both halves - a bound options group AND the shipped client - are required before an
    /// entry appears.
    /// </para>
    /// <para>
    /// <b>EITHER ACCEPTED SCHEME MAKES THIS COMPONENT READY, AND REQUIRING THE CERTIFICATE PAIR WAS AN
    /// OUTAGE RATHER THAN STRICTNESS.</b> Contract C-01 accepts TWO caller credentials on
    /// <c>POST /v1/tokens</c> - a shared secret presented as an HTTP <c>Basic</c> credential naming a
    /// subject on Security's issuance roster, or a client certificate - as ALTERNATIVES, and
    /// <see cref="GatewayOptions.SecurityClientSecret"/> is the one the documented bring-up supplies while
    /// both certificate paths stay deliberately EMPTY. This component used to report degraded on exactly
    /// that configuration, and degraded is answered <c>503</c>: Gateway would never have reported ready
    /// under the configuration the orchestration layer documents, which is a stack that does not come up
    /// rather than a misleading warning.
    /// </para>
    /// <para>
    /// THE CERTIFICATE PAIR IS STILL TESTED AS A PAIR, because half a pair cannot complete a handshake: a
    /// certificate without its key cannot be loaded and a key with no certificate has nothing to present.
    /// So a certificate-only deployment needs both halves while a secret-configured deployment needs
    /// neither - which is what the options group's own <c>HasIssuanceCredential</c> cannot express, since
    /// it is an OR over "did the operator INTEND a scheme" and exists so the startup validator can refuse
    /// a deployment that intended nothing. The composition root already refuses to START on a half-set
    /// pair, so this test and the validator can only disagree in a host that bypassed both the validator
    /// and the identity loader.
    /// </para>
    /// </remarks>
    private static (HealthStatus Status, ComponentHealthCheck Entry)? EvaluateCredentialReadiness(
        IServiceProvider services,
        ILogger logger)
    {
        if (services.GetService<IServiceTokenProvider>() is not SecurityClient)
        {
            return null;
        }

        if (services.GetService<IOptions<GatewayOptions>>()?.Value is not GatewayOptions options)
        {
            return null;
        }

        GatewayOptions.MutualTlsClientOptions identity = options.MutualTls;

        bool presentsSecret = !string.IsNullOrWhiteSpace(options.SecurityClientSecret);

        bool presentsCertificate = !string.IsNullOrWhiteSpace(identity.CertificatePath)
            && !string.IsNullOrWhiteSpace(identity.CertificateKeyPath);

        if (presentsSecret || presentsCertificate)
        {
            return (
                HealthStatus.Healthy,
                new ComponentHealthCheck
                {
                    Name = CredentialsCheckName,
                    Status = StatusHealthy,
                    Detail = CredentialsReadyDetail,
                });
        }

        // NO VALUE AND NO PATH IS REPRODUCED, not even on the operator channel. A path names where private
        // key material is mounted and a secret is a credential; a log is exempt from neither concern. The
        // SETTING NAMES are what an operator needs in order to act. Warning rather than error: the material
        // has not failed, it has not arrived, and supplying it is an orchestration act.
        logger.LogWarning(
            "Gateway presents no caller credential, so it cannot obtain a service token: the issuance "
                + "operation accepts a shared secret as an HTTP Basic credential or a client certificate, "
                + "and a request presenting neither can only be refused. Set '{Secret}' - the documented "
                + "bring-up path - or both '{Certificate}' and '{Key}' to the material this deployment "
                + "mounts. Reporting the {Check} component {Status}.",
            GatewayOptions.SecurityClientSecretConfigurationKey,
            $"{GatewayOptions.SectionName}:{nameof(GatewayOptions.MutualTls)}"
                + $":{nameof(GatewayOptions.MutualTlsClientOptions.CertificatePath)}",
            $"{GatewayOptions.SectionName}:{nameof(GatewayOptions.MutualTls)}"
                + $":{nameof(GatewayOptions.MutualTlsClientOptions.CertificateKeyPath)}",
            CredentialsCheckName,
            StatusDegraded);

        // DEGRADED RATHER THAN UNHEALTHY: the material has not failed, it has not arrived, and the
        // orchestration layer owns the mount. Both verdicts are answered 503, so the readiness gate behaves
        // identically - the distinction is what an operator is told to do about it.
        return (
            HealthStatus.Degraded,
            new ComponentHealthCheck
            {
                Name = CredentialsCheckName,
                Status = StatusDegraded,
                Detail = CredentialsNotReadyDetail,
            });
    }

    /// <summary>
    /// Observes the anonymous readiness endpoint of each of the three upstreams and returns one entry per
    /// upstream, in the contract's order.
    /// </summary>
    /// <param name="services">The request's service provider.</param>
    /// <param name="logger">The operator channel.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    /// <returns>
    /// Exactly three entries - Persistence, DataServices and Security - which is what the contract bounds
    /// the array at. No entry exists for any deferred service, because no deferred service exists.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The three probes run CONCURRENTLY under one linked, time-bounded token, so the round costs one
    /// budget rather than three and no upstream's latency is charged to another. The budget is linked to
    /// the request's own token so a disconnected caller stops the probes.
    /// </para>
    /// <para>
    /// The addresses come from <c>Gateway:HealthProbes</c> - the group that exists precisely so that
    /// observing a published readiness verdict is expressed differently from invoking a capability. See
    /// C-K decision 3 in the file header for why the Persistence entry is not a layering breach and why
    /// no Persistence client exists to read it.
    /// </para>
    /// </remarks>
    private static async Task<IReadOnlyList<UpstreamHealth>> ProbeUpstreamsAsync(
        IServiceProvider services,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        GatewayOptions options = services.GetRequiredService<IOptions<GatewayOptions>>().Value;
        GatewayOptions.HealthProbeAddresses addresses = options.HealthProbes;
        IUpstreamReadinessProbe probe = ResolveProbe(services, logger);

        using CancellationTokenSource budget =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        budget.CancelAfter(ProbeTimeout);

        Task<UpstreamReadiness> persistence = ObserveAsync(
            probe, PersistenceParticipant, addresses.Persistence, logger, budget.Token);
        Task<UpstreamReadiness> dataServices = ObserveAsync(
            probe, DataServicesParticipant, addresses.DataServices, logger, budget.Token);
        Task<UpstreamReadiness> security = ObserveAsync(
            probe, SecurityParticipant, addresses.Security, logger, budget.Token);

        UpstreamReadiness[] observed = await Task
            .WhenAll(persistence, dataServices, security)
            .ConfigureAwait(false);

        return
        [
            BuildUpstreamEntry(PersistenceParticipant, observed[0]),
            BuildUpstreamEntry(DataServicesParticipant, observed[1]),
            BuildUpstreamEntry(SecurityParticipant, observed[2]),
        ];
    }


    /// <summary>
    /// Observes one upstream, converting every failure mode into a verdict.
    /// </summary>
    /// <param name="probe">The probe to observe through.</param>
    /// <param name="participant">The upstream's logical name, from the contract's closed enumeration.</param>
    /// <param name="configuredAddress">The upstream's base address from <c>Gateway:HealthProbes</c>.</param>
    /// <param name="logger">The operator channel.</param>
    /// <param name="cancellationToken">The time-bounded probe budget.</param>
    /// <returns>The upstream's verdict, or <see cref="UpstreamReadiness.Unreachable"/>.</returns>
    /// <remarks>
    /// <para>
    /// A SUBSTITUTED probe is not trusted to be well behaved: the default implementation converts its own
    /// failures into a verdict, and this wrapper does the same for anything a substitute throws, so a
    /// faulty double in a test - or a faulty resilience handler in a deployment - degrades one entry
    /// rather than the whole probe.
    /// </para>
    /// <para>
    /// An unusable configured address is reported <c>Unreachable</c> rather than thrown. The options type
    /// validates all three addresses on start, so this arm is defence rather than routine; reporting it
    /// keeps the failure diagnosable from the probe instead of turning a probe into a 500.
    /// </para>
    /// </remarks>
    private static async Task<UpstreamReadiness> ObserveAsync(
        IUpstreamReadinessProbe probe,
        string participant,
        string configuredAddress,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(configuredAddress, UriKind.Absolute, out Uri? baseAddress)
            || !Uri.TryCreate(baseAddress, RoutePattern, out Uri? probeAddress))
        {
            // The address itself is NOT written to the log. It is not a credential, but it is topology,
            // and the configuration key is what an operator needs in order to correct it.
            logger.LogError(
                "'{ConfigurationKey}:HealthProbes:{Participant}' is not a usable absolute address, so no "
                    + "readiness verdict can be obtained for that upstream. Reporting {Status}.",
                GatewayOptions.SectionName,
                participant,
                StatusUnreachable);

            return UpstreamReadiness.Unreachable;
        }

        try
        {
            return await probe
                .ProbeAsync(participant, probeAddress, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "The readiness probe for the {Participant} upstream faulted; reporting {Status}.",
                participant,
                StatusUnreachable);

            return UpstreamReadiness.Unreachable;
        }
    }

    /// <summary>
    /// Resolves the probe implementation, preferring one the host registered.
    /// </summary>
    /// <param name="services">The request's service provider.</param>
    /// <param name="logger">The operator channel.</param>
    /// <returns>The probe to observe the three upstreams through.</returns>
    /// <remarks>
    /// <para>
    /// THE SUBSTITUTION SEAM (constraint C-H). A registered <see cref="IUpstreamReadinessProbe"/> wins, so
    /// the whole suite of readiness branches - all healthy, each down individually, all down, degraded,
    /// unreachable, timed out - is reachable in process with no live upstream and no network at all.
    /// </para>
    /// <para>
    /// With no registration the default HTTP implementation is used, and with no
    /// <see cref="IHttpClientFactory"/> either there is genuinely no way to obtain a verdict, so every
    /// upstream is reported <c>Unreachable</c>. That arm fails CLOSED and says so on the operator channel
    /// rather than throwing: a probe that threw would fault the endpoint the readiness gate depends on.
    /// </para>
    /// </remarks>
    private static IUpstreamReadinessProbe ResolveProbe(IServiceProvider services, ILogger logger)
    {
        IUpstreamReadinessProbe? registered = services.GetService<IUpstreamReadinessProbe>();

        if (registered is not null)
        {
            return registered;
        }

        IHttpClientFactory? httpClientFactory = services.GetService<IHttpClientFactory>();

        if (httpClientFactory is null)
        {
            logger.LogError(
                "No {Factory} and no {Probe} is registered, so no upstream readiness verdict can be "
                    + "obtained. Reporting every upstream {Status}.",
                nameof(IHttpClientFactory),
                nameof(IUpstreamReadinessProbe),
                StatusUnreachable);

            return UnobtainableReadinessProbe.Instance;
        }

        return new HttpUpstreamReadinessProbe(httpClientFactory, logger);
    }

    /// <summary>
    /// Builds one upstream entry from an observed verdict, using only this file's closed vocabulary.
    /// </summary>
    /// <param name="participant">The upstream's logical name.</param>
    /// <param name="readiness">The observed verdict.</param>
    /// <returns>The wire entry for that upstream.</returns>
    /// <remarks>
    /// The NAME is supplied by the caller from the contract's enumeration rather than by the probe, so a
    /// substituted probe cannot introduce an entry the contract does not declare, and the DETAIL is one of
    /// four authored sentences rather than anything the upstream said - see boundary note 2.
    /// </remarks>
    private static UpstreamHealth BuildUpstreamEntry(string participant, UpstreamReadiness readiness) =>
        new()
        {
            Service = participant,
            Status = ToWireStatus(readiness),
            Detail = readiness switch
            {
                UpstreamReadiness.Healthy => UpstreamReadyDetail,
                UpstreamReadiness.Degraded => UpstreamDegradedDetail,
                UpstreamReadiness.Unhealthy => UpstreamUnhealthyDetail,
                _ => UpstreamUnreachableDetail,
            },
        };

    /// <summary>
    /// Builds the contract's not-ready response: <c>503</c> carrying a problem document.
    /// </summary>
    /// <param name="wireStatus">The aggregate verdict, <c>Degraded</c> or <c>Unhealthy</c>.</param>
    /// <param name="checks">Gateway's own component entries.</param>
    /// <param name="upstreams">The three upstream entries.</param>
    /// <param name="checkedAt">When the evaluation was performed, read from the injected clock.</param>
    /// <returns><c>503</c> with an <c>application/problem+json</c> body.</returns>
    /// <remarks>
    /// <para>
    /// DEGRADED AND UNHEALTHY BOTH ARRIVE HERE AND ARE DISTINGUISHED IN THE BODY RATHER THAN IN THE
    /// STATUS. Both mean not ready, and the orchestration gate reads the status code.
    /// </para>
    /// <para>
    /// <b>Why the aggregate verdict travels in a <c>serviceStatus</c> member and not in <c>status</c>.</b>
    /// RFC 9457 - which is the shape the contract publishes for every error in this document - reserves
    /// <c>status</c> for the integer HTTP status code, and the contract's own schema types it as an
    /// integer. The verdict token therefore cannot occupy that name here, so it travels in the
    /// <see cref="ServiceStatusExtensionMember"/> extension member the contract declares for exactly this
    /// purpose, and is ALSO stated in <c>detail</c> together with the participants responsible. Both
    /// halves are needed and neither substitutes for the other: <c>detail</c> is prose for a human, and
    /// prose is not something an aggregator can branch on. Stating the verdict only in <c>detail</c> is
    /// what leaves <c>retCode</c> as the sole machine-readable member of this body - and
    /// <c>retCode</c> is <see cref="RetCode.E_RETRY"/> for <c>Degraded</c> and <c>Unhealthy</c> alike.
    /// </para>
    /// <para>
    /// The per-upstream array, the component entries, the reporting service and the evaluation time ride
    /// along as EXTENSION MEMBERS. RFC 9457 explicitly permits extension members and the contract's
    /// <c>ProblemDetails</c> sets <c>additionalProperties: true</c> for that reason, instructing consumers
    /// to ignore any member they do not recognise. They are carried because C-10's whole purpose is that
    /// an operator can tell WHICH participant is responsible, and dropping the machine-readable half of
    /// that precisely when something is down would defeat the aggregate. Every value is drawn from the
    /// same closed vocabulary as the ready response, so the anonymous disclosure surface is unchanged.
    /// </para>
    /// <para>
    /// <c>retCode</c> carries the legacy framework's own retry code rather than an invented one, so a
    /// consumer that already branches on <c>retCode</c> handles a not-ready gateway with a value it knows
    /// [ws_objects/pfw.shared.pbl.src/retcode.sru:L76].
    /// </para>
    /// </remarks>
    private static IResult BuildNotReadyProblem(
        string wireStatus,
        IReadOnlyList<ComponentHealthCheck> checks,
        IReadOnlyList<UpstreamHealth> upstreams,
        DateTimeOffset checkedAt)
    {
        List<string> responsible = [];

        foreach (UpstreamHealth upstream in upstreams)
        {
            if (!string.Equals(upstream.Status, StatusHealthy, StringComparison.Ordinal))
            {
                responsible.Add($"{upstream.Service} ({upstream.Status})");
            }
        }

        foreach (ComponentHealthCheck check in checks)
        {
            if (!string.Equals(check.Status, StatusHealthy, StringComparison.Ordinal))
            {
                responsible.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} component '{1}' ({2})",
                    AggregateHealthReport.ReportingService,
                    check.Name,
                    check.Status));
            }
        }

        // The empty case is reachable and is handled rather than assumed away: an aggregate can be
        // not-ready while every individual entry reports ready if a future contributor degrades the whole
        // on a condition no single entry reports.
        string detail = responsible.Count == 0
            ? string.Format(
                CultureInfo.InvariantCulture,
                "Gateway is not ready ({0}).",
                wireStatus)
            : string.Format(
                CultureInfo.InvariantCulture,
                "Gateway is not ready ({0}). Not reporting {1}: {2}.",
                wireStatus,
                StatusHealthy,
                string.Join(", ", responsible));

        return TypedResults.Problem(
            detail: detail,
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: NotReadyProblemTitle,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ServiceStatusExtensionMember] = wireStatus,
                [RetCodeExtensionMember] = RetCode.E_RETRY,
                [ServiceExtensionMember] = AggregateHealthReport.ReportingService,
                [CheckedAtExtensionMember] = checkedAt,
                [UpstreamsExtensionMember] = upstreams,
                [ChecksExtensionMember] = checks,
            });
    }


    /// <summary>
    /// Writes the evaluation to the operator channel, where the audience is authenticated by having access
    /// to the service's logs rather than by being able to reach a port.
    /// </summary>
    /// <param name="logger">The operator channel.</param>
    /// <param name="wireStatus">The aggregate verdict.</param>
    /// <param name="checks">Gateway's own component entries.</param>
    /// <param name="upstreams">The three upstream entries.</param>
    /// <remarks>
    /// <para>
    /// A not-ready evaluation is recorded at warning level and a ready one at debug level behind an
    /// <see cref="ILogger.IsEnabled(LogLevel)"/> guard, because this endpoint is polled continuously by an
    /// orchestrator and an unguarded record per evaluation would drown the channel in records that say
    /// nothing happened.
    /// </para>
    /// <para>
    /// The record carries the same closed vocabulary the response does. It deliberately does NOT carry any
    /// probe address: a log is not exempt from the concern that an address is topology, and the
    /// configuration key is what an operator needs in order to act.
    /// </para>
    /// </remarks>
    private static void WriteOperatorRecord(
        ILogger logger,
        string wireStatus,
        IReadOnlyList<ComponentHealthCheck> checks,
        IReadOnlyList<UpstreamHealth> upstreams)
    {
        bool ready = string.Equals(wireStatus, StatusHealthy, StringComparison.Ordinal);
        LogLevel level = ready ? LogLevel.Debug : LogLevel.Warning;

        if (!logger.IsEnabled(level))
        {
            return;
        }

        List<string> upstreamStates = new(upstreams.Count);

        foreach (UpstreamHealth upstream in upstreams)
        {
            upstreamStates.Add($"{upstream.Service}={upstream.Status}");
        }

        List<string> componentStates = new(checks.Count);

        foreach (ComponentHealthCheck check in checks)
        {
            componentStates.Add($"{check.Name}={check.Status}");
        }

        logger.Log(
            level,
            "Gateway readiness is {Status}. Upstreams: {Upstreams}. Own components: {Components}.",
            wireStatus,
            string.Join(", ", upstreamStates),
            string.Join(", ", componentStates));
    }

    /// <summary>
    /// Records the registration-supplied names behind a not-ready component report, on the operator
    /// channel only.
    /// </summary>
    /// <param name="logger">The operator channel.</param>
    /// <param name="report">The evaluated component report.</param>
    /// <remarks>
    /// THIS IS THE INFORMATION THE ANONYMOUS BODY WITHHOLDS. A registered check's own name and description
    /// are authored by whatever registered it rather than for disclosure, so they are recorded here - where
    /// they are exactly what an operator needs in order to find the failing component - and aggregated into
    /// a single unnamed entry in the response.
    /// </remarks>
    private static void WriteComponentRecord(ILogger logger, HealthReport report)
    {
        if (report.Status is HealthStatus.Healthy || !logger.IsEnabled(LogLevel.Warning))
        {
            return;
        }

        List<string> notReady = [];

        foreach (KeyValuePair<string, HealthReportEntry> entry in report.Entries)
        {
            if (entry.Value.Status is not HealthStatus.Healthy)
            {
                notReady.Add($"{entry.Key}={ToWireStatus(entry.Value.Status)}");
            }
        }

        logger.LogWarning(
            "Registered component check(s) not reporting {Status}: {Components}.",
            StatusHealthy,
            notReady.Count == 0 ? "(none individually)" : string.Join(", ", notReady));
    }

    /// <summary>
    /// Combines two statuses, keeping the worse of the two.
    /// </summary>
    /// <param name="current">The status accumulated so far.</param>
    /// <param name="candidate">The status to fold in.</param>
    /// <returns>The worse of the two.</returns>
    /// <remarks>
    /// <b>The framework enumeration is ordered by ASCENDING health, not ascending severity</b> -
    /// <see cref="HealthStatus.Unhealthy"/> is 0, <see cref="HealthStatus.Degraded"/> is 1 and
    /// <see cref="HealthStatus.Healthy"/> is 2 - so the worse of two statuses is the MINIMUM, which is the
    /// same rule the framework's own report aggregation applies. Written as an explicit minimum with this
    /// note rather than as a comparison, because a reader who assumed the opposite ordering would invert
    /// the gate and Gateway would report ready while an upstream was failing.
    /// </remarks>
    private static HealthStatus Worse(HealthStatus current, HealthStatus candidate) =>
        (HealthStatus)Math.Min((int)current, (int)candidate);

    /// <summary>
    /// Maps one upstream's wire token onto its contribution to the aggregate verdict.
    /// </summary>
    /// <param name="wireStatus">The upstream entry's status token.</param>
    /// <returns>The severity that token contributes.</returns>
    /// <remarks>
    /// <para>
    /// <c>Unreachable</c> collapses to <see cref="HealthStatus.Unhealthy"/>, because the aggregate's
    /// vocabulary is closed at three tokens and an upstream from which no verdict could be obtained at all
    /// is not a weaker signal than one that reported a failure. The distinction the operator needs survives
    /// in the per-upstream entry, which keeps <c>Unreachable</c> as its own token; the status code is 503
    /// either way, so the readiness gate is unaffected by the collapse.
    /// </para>
    /// <para>
    /// An unrecognised token also maps to <see cref="HealthStatus.Unhealthy"/> - the same fail-closed choice
    /// the status-code mapping makes - so that a token this file does not know cannot open the gate.
    /// </para>
    /// </remarks>
    private static HealthStatus SeverityOf(string wireStatus) => wireStatus switch
    {
        StatusHealthy => HealthStatus.Healthy,
        StatusDegraded => HealthStatus.Degraded,
        _ => HealthStatus.Unhealthy,
    };

    /// <summary>
    /// Maps a framework status onto the closed three-token vocabulary the contract publishes.
    /// </summary>
    /// <param name="status">The status to translate.</param>
    /// <returns><c>Healthy</c>, <c>Degraded</c> or <c>Unhealthy</c>.</returns>
    /// <remarks>
    /// Translated through an explicit switch rather than by calling the enumeration's own string
    /// conversion, so the published vocabulary is stated in this file and cannot drift silently if a future
    /// framework version renames or adds a member. An unrecognised status maps to <c>Unhealthy</c>.
    /// </remarks>
    private static string ToWireStatus(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => StatusHealthy,
        HealthStatus.Degraded => StatusDegraded,
        HealthStatus.Unhealthy => StatusUnhealthy,
        _ => StatusUnhealthy,
    };

    /// <summary>
    /// Maps an observed upstream verdict onto the closed four-token vocabulary the contract publishes for a
    /// per-upstream entry.
    /// </summary>
    /// <param name="readiness">The observed verdict.</param>
    /// <returns><c>Healthy</c>, <c>Degraded</c>, <c>Unhealthy</c> or <c>Unreachable</c>.</returns>
    /// <remarks>
    /// An unrecognised value maps to <c>Unreachable</c>, which is the truthful reading of a verdict this
    /// file cannot interpret: nothing usable was obtained. It still fails closed, because
    /// <see cref="SeverityOf"/> treats that token as a failure in the aggregate.
    /// </remarks>
    private static string ToWireStatus(UpstreamReadiness readiness) => readiness switch
    {
        UpstreamReadiness.Healthy => StatusHealthy,
        UpstreamReadiness.Degraded => StatusDegraded,
        UpstreamReadiness.Unhealthy => StatusUnhealthy,
        _ => StatusUnreachable,
    };


    /// <summary>
    /// Applies the parts of the contract that endpoint metadata cannot express to the generated operation.
    /// </summary>
    /// <param name="operation">The operation the document generator produced for this endpoint.</param>
    /// <param name="context">The transformation context, which carries the document being built.</param>
    /// <param name="cancellationToken">
    /// Cancellation for the document generation this transformer participates in. The transformer is pure
    /// in-memory editing with no asynchronous work of its own, so it has nothing to observe the token with
    /// and completes synchronously.
    /// </param>
    /// <returns>A completed task.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="operation"/> or <paramref name="context"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// Endpoint-scoped, so it can never affect another operation, and applied through
    /// <c>AddOpenApiOperationTransformer</c> rather than <c>WithOpenApi</c> because the latter is documented
    /// as not integrating with the built-in document generation this service uses.
    /// </remarks>
    private static Task ApplyContractMetadataAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        // Stated explicitly as well as through WithName. The endpoint name and the operation id are two
        // different concepts that happen to share a value here, and the contract pins the operation id.
        operation.OperationId = OperationId;

        ApplyContractIdExtension(operation);
        ApplyResponseDescriptions(operation);
        ApplyAnonymousSecurity(operation);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Carries the contract's <c>x-contract-id</c> specification extension onto the operation.
    /// </summary>
    /// <param name="operation">The operation to annotate.</param>
    /// <remarks>
    /// RFC-permitted specification extensions are how the contract records which of its ten contracts an
    /// operation belongs to, and an operation that omits it is harder to audit against the inventory in
    /// <c>docs/CONTRACTS.md</c>.
    /// </remarks>
    private static void ApplyContractIdExtension(OpenApiOperation operation)
    {
        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);
        operation.Extensions[ContractIdExtensionName] = new JsonNodeExtension(ContractId);
    }

    /// <summary>
    /// Replaces the generator's default response descriptions with the contract's own wording.
    /// </summary>
    /// <param name="operation">The operation whose responses are being described.</param>
    /// <remarks>
    /// Each assignment is guarded: a response the generator did not produce is left alone rather than
    /// created here, because inventing a response would publish a status this endpoint cannot return. The
    /// cast is required because the responses map is typed by the read-only response interface while the
    /// description is settable on the concrete type.
    /// </remarks>
    private static void ApplyResponseDescriptions(OpenApiOperation operation)
    {
        if (operation.Responses is null)
        {
            return;
        }

        if (operation.Responses.TryGetValue(ReadyStatusKey, out IOpenApiResponse? ready)
            && ready is OpenApiResponse readyResponse)
        {
            readyResponse.Description = ReadyResponseDescription;
        }

        if (operation.Responses.TryGetValue(NotReadyStatusKey, out IOpenApiResponse? notReady)
            && notReady is OpenApiResponse notReadyResponse)
        {
            notReadyResponse.Description = NotReadyResponseDescription;
        }
    }

    /// <summary>
    /// Declares the anonymous posture in the published document as well as in the pipeline.
    /// </summary>
    /// <param name="operation">The operation to declare anonymous.</param>
    /// <remarks>
    /// <para>
    /// An EMPTY requirement list, which is what the hand-authored contract carries on this one operation.
    /// <c>[]</c> is not interchangeable with <c>[{}]</c>: the empty list says "no requirement applies",
    /// whereas a list containing an empty requirement says "one requirement applies and is satisfied by
    /// nothing". Both permit anonymous access, but only the empty list says so in the shape every generator
    /// and policy checker reads.
    /// </para>
    /// <para>
    /// Assigned rather than added to, deliberately and unlike the sibling authenticated operations: any
    /// requirement a document-level convention contributed to THIS operation would be wrong, because a
    /// document that advertised a credential requirement on the readiness probe would tell an orchestrator
    /// to present one - and the probe holds none, precisely during the window in which the token issuer may
    /// not yet be reachable.
    /// </para>
    /// </remarks>
    private static void ApplyAnonymousSecurity(OpenApiOperation operation) => operation.Security = [];

    /// <summary>
    /// The probe used when neither a substituted probe nor an <see cref="IHttpClientFactory"/> is available:
    /// it reports that no verdict could be obtained.
    /// </summary>
    /// <remarks>
    /// A NULL OBJECT WITH A TRUTHFUL ANSWER, not a placeholder. There is genuinely no channel on which to
    /// observe an upstream in that configuration, and <c>Unreachable</c> is the token the contract provides
    /// for exactly that case, so this type reports it rather than throwing - a throw here would fault the
    /// endpoint the readiness gate depends on. <see cref="ResolveProbe"/> records the cause on the operator
    /// channel so the configuration gap is diagnosable.
    /// </remarks>
    private sealed class UnobtainableReadinessProbe : IUpstreamReadinessProbe
    {
        /// <summary>
        /// The single shared instance. The type is stateless, so one instance is sufficient.
        /// </summary>
        internal static readonly UnobtainableReadinessProbe Instance = new();

        /// <summary>
        /// Prevents construction from anywhere but <see cref="Instance"/>.
        /// </summary>
        private UnobtainableReadinessProbe()
        {
        }

        /// <inheritdoc />
        public ValueTask<UpstreamReadiness> ProbeAsync(
            string participant,
            Uri probeAddress,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(UpstreamReadiness.Unreachable);
    }


    /// <summary>
    /// The default probe: one anonymous <c>GET</c> against an upstream's readiness endpoint, with every
    /// failure mode converted into a verdict.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PRIVATE AND NESTED, so the only way to replace the probe channel from outside this file is to
    /// register an <see cref="IUpstreamReadinessProbe"/> - which is the seam this file publishes - rather
    /// than to subclass or re-use an implementation detail.
    /// </para>
    /// <para>
    /// IT SENDS NO CREDENTIAL. <c>/health</c> is anonymous on all four services, and presenting a token
    /// would make readiness depend on token issuance already being live: during a cold start that is a
    /// circular dependency that cannot resolve, and the aggregate would report every upstream unreachable
    /// exactly when the gate needs a verdict. No <c>Authorization</c> header, no client certificate
    /// selection and no key material appears anywhere in this type.
    /// </para>
    /// <para>
    /// IT VALIDATES SERVER CERTIFICATES BY DEFAULT AND OVERRIDES NOTHING. The deployed probe addresses are
    /// <c>https</c>, and a forged readiness verdict opens the dependency gate early, so trust is an
    /// orchestration concern - the material a deployment mounts - and never a validation callback here.
    /// </para>
    /// </remarks>
    private sealed class HttpUpstreamReadinessProbe : IUpstreamReadinessProbe
    {
        /// <summary>
        /// The factory the probe channel is resolved from.
        /// </summary>
        private readonly IHttpClientFactory _httpClientFactory;

        /// <summary>
        /// The operator channel, which is where a failure's cause is recorded.
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// Creates the default probe.
        /// </summary>
        /// <param name="httpClientFactory">The factory the probe channel is resolved from.</param>
        /// <param name="logger">The operator channel.</param>
        /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
        internal HttpUpstreamReadinessProbe(IHttpClientFactory httpClientFactory, ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(httpClientFactory);
            ArgumentNullException.ThrowIfNull(logger);

            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        /// <inheritdoc />
        /// <remarks>
        /// <para>
        /// THE STATUS CODE SELECTS WHICH BODY SHAPE IS EXPECTED, AND THE BODY SUPPLIES THE VERDICT. The
        /// contract declares exactly two responses on <c>GET /health</c> and a different schema for each,
        /// so reading the body without first branching on the code would be reading an unknown shape.
        /// </para>
        /// <list type="bullet">
        /// <item>
        /// <c>200</c> - the body must be a <c>ServiceHealthReport</c>, and its <c>status</c> member
        /// supplies the verdict. A recognised token yields that verdict, so a <c>Degraded</c> upstream is
        /// reported as degraded rather than flattened into ready. Anything else - an absent, empty,
        /// non-object, unparseable or truncated body, a missing or non-string member, or a token outside
        /// the published vocabulary - yields <c>Unhealthy</c>. FAILING CLOSED IS THE POINT: a body that
        /// cannot state its verdict is not a body whose verdict can be trusted, and reading such a
        /// response as ready is what would let the readiness gate open onto an unusable service.
        /// </item>
        /// <item>
        /// <c>503</c> - the body must be a problem document, and its <see cref="ServiceStatusExtensionMember"/>
        /// extension member supplies the verdict. This is the arm that PRESERVES <c>Degraded</c>: both
        /// not-ready verdicts are answered 503 by every service in the estate, so the code alone cannot
        /// separate them and the only other machine-readable member of that body is <c>retCode</c>, which
        /// is <see cref="RetCode.E_RETRY"/> for both. A <c>Healthy</c> token on a 503 is a contradiction
        /// and is read as <c>Unhealthy</c> - the code is the statement the estate's own readiness gate
        /// acts on, so it wins. Anything unreadable yields <c>Unhealthy</c>, as above.
        /// </item>
        /// <item>
        /// any OTHER status yields <c>Unhealthy</c> without the body being read at all. The contract
        /// declares no third response on this path, so whatever answered is not answering this contract,
        /// and a shape this file cannot name is not a shape it should parse.
        /// </item>
        /// <item>
        /// a transport failure, an expired budget or a cancellation yields <c>Unreachable</c>, which is the
        /// contract's token for "no verdict could be obtained at all".
        /// </item>
        /// </list>
        /// <para>
        /// NOTHING HERE READS THE PLAIN-TEXT SHAPE THE SHARED FRAMEWORK'S OWN <c>MapHealthChecks</c>
        /// EMITS, and that is deliberate rather than an omission. This probe only ever addresses the three
        /// upstreams named in <c>Gateway:HealthProbes</c>, all three of which publish C-10's JSON shapes
        /// from their own hand-written endpoint; treating a bare <c>Healthy</c> string as ready would mean
        /// treating any 200 with any body as ready, which is exactly the flattening this arm exists to
        /// remove.
        /// </para>
        /// </remarks>
        public async ValueTask<UpstreamReadiness> ProbeAsync(
            string participant,
            Uri probeAddress,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(participant);
            ArgumentNullException.ThrowIfNull(probeAddress);

            try
            {
                HttpClient client = _httpClientFactory.CreateClient(ProbeHttpClientName);

                using HttpRequestMessage request = new(HttpMethod.Get, probeAddress);
                using HttpResponseMessage response = await client
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (response.StatusCode is not HttpStatusCode.OK
                    and not HttpStatusCode.ServiceUnavailable)
                {
                    // Recorded at debug level: a not-ready upstream during a cold start is the ORDINARY
                    // observation, and the aggregate's own warning already names the participant.
                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug(
                            "The {Participant} upstream answered its readiness probe with HTTP "
                                + "{StatusCode}, which contract C-10 does not declare on this path; "
                                + "reporting {Status}.",
                            participant,
                            (int)response.StatusCode,
                            StatusUnhealthy);
                    }

                    return UpstreamReadiness.Unhealthy;
                }

                bool ready = response.StatusCode is HttpStatusCode.OK;

                UpstreamReadiness verdict = await ReadReportedVerdictAsync(
                        response,
                        ready ? ReportedStatusMember : ServiceStatusExtensionMember,
                        cancellationToken)
                    .ConfigureAwait(false);

                // A 503 whose body claims Healthy is self-contradictory. The CODE is what the estate's
                // own readiness gate acts on, so it decides: the response says not-ready, and a token
                // saying otherwise is evidence the body cannot be trusted rather than evidence of health.
                if (!ready && verdict is UpstreamReadiness.Healthy)
                {
                    _logger.LogWarning(
                        "The {Participant} upstream answered its readiness probe with HTTP 503 while its "
                            + "body reported {Reported}; the status code decides, so reporting {Status}.",
                        participant,
                        StatusHealthy,
                        StatusUnhealthy);

                    return UpstreamReadiness.Unhealthy;
                }

                if (verdict is not UpstreamReadiness.Healthy && _logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "The {Participant} upstream answered its readiness probe with HTTP {StatusCode}; "
                            + "reporting {Status}.",
                        participant,
                        (int)response.StatusCode,
                        ToWireStatus(verdict));
                }

                return verdict;
            }
            catch (OperationCanceledException)
            {
                // Covers BOTH the expired probe budget and a disconnected caller, and deliberately does not
                // distinguish them: the consequence is identical - no verdict was obtained - and the
                // distinction is not worth a second code path in the one endpoint that must never throw.
                _logger.LogWarning(
                    "The readiness probe for the {Participant} upstream did not complete within its "
                        + "budget or was cancelled; reporting {Status}.",
                    participant,
                    StatusUnreachable);

                return UpstreamReadiness.Unreachable;
            }
            catch (HttpRequestException exception)
            {
                // The cause goes to the operator, never to the anonymous body: the message can name a host,
                // a port or a certificate subject, all of which are topology.
                _logger.LogWarning(
                    exception,
                    "The readiness probe for the {Participant} upstream could not be completed; reporting "
                        + "{Status}.",
                    participant,
                    StatusUnreachable);

                return UpstreamReadiness.Unreachable;
            }
            catch (InvalidOperationException exception)
            {
                // A misconfigured probe channel - a handler configured with an incompatible base address,
                // for instance - surfaces here rather than as a transport failure. Still not a fault of the
                // probe: no verdict was obtained.
                _logger.LogError(
                    exception,
                    "The readiness probe channel for the {Participant} upstream is not usable; reporting "
                        + "{Status}.",
                    participant,
                    StatusUnreachable);

                return UpstreamReadiness.Unreachable;
            }
        }

        /// <summary>
        /// Reads an upstream's own verdict token out of a readiness response whose shape the status code
        /// has already established.
        /// </summary>
        /// <param name="response">The response, either the contract's <c>200</c> or its <c>503</c>.</param>
        /// <param name="verdictMember">
        /// Which member carries the token in the shape this status code declares:
        /// <see cref="ReportedStatusMember"/> on the <c>200</c>'s report, or
        /// <see cref="ServiceStatusExtensionMember"/> on the <c>503</c>'s problem document.
        /// </param>
        /// <param name="cancellationToken">The probe budget.</param>
        /// <returns>
        /// The reported verdict, or <see cref="UpstreamReadiness.Unhealthy"/> when no token could be read.
        /// </returns>
        /// <remarks>
        /// <para>
        /// EVERY UNREADABLE OUTCOME IS <c>Unhealthy</c>, AND THERE IS NO ARM THAT ANSWERS <c>Healthy</c> BY
        /// DEFAULT. An absent body, an empty body, a body that is not a JSON object, a missing member, a
        /// member that is not a string, a token outside the published vocabulary, and a body that does not
        /// parse at all are six distinct ways for a response to fail to state its verdict, and all six mean
        /// the same thing: nothing was read. Reading any of them as ready is what makes a health aggregate
        /// worse than no aggregate, because it converts silence into a positive assertion.
        /// </para>
        /// <para>
        /// THE READ IS BOUNDED. At most <see cref="MaxProbeBodyBytes"/> are read, so a misbehaving or
        /// compromised upstream cannot make the one operation an unauthenticated caller can trigger allocate
        /// without limit. A readiness body is far smaller than that bound, so a truncated read means the
        /// body was not a readiness body - and a truncated buffer fails to parse, which now fails closed
        /// rather than being read as ready.
        /// </para>
        /// <para>
        /// The token is read only when it is a JSON STRING. On the <c>503</c> that is what keeps RFC 9457's
        /// own integer <c>status</c> member from being mistaken for a verdict, which is the same reason the
        /// verdict needed an extension member of its own in the first place.
        /// </para>
        /// </remarks>
        private static async ValueTask<UpstreamReadiness> ReadReportedVerdictAsync(
            HttpResponseMessage response,
            string verdictMember,
            CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[MaxProbeBodyBytes];

            Stream content = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            await using (content.ConfigureAwait(false))
            {
                int read = await content
                    .ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken)
                    .ConfigureAwait(false);

                if (read == 0)
                {
                    // No body at all. The contract declares a body on both of its responses, so this is a
                    // response that cannot state its verdict.
                    return UpstreamReadiness.Unhealthy;
                }

                try
                {
                    using JsonDocument document = JsonDocument.Parse(buffer.AsMemory(0, read));

                    if (document.RootElement.ValueKind is not JsonValueKind.Object
                        || !document.RootElement.TryGetProperty(verdictMember, out JsonElement reported)
                        || reported.ValueKind is not JsonValueKind.String)
                    {
                        return UpstreamReadiness.Unhealthy;
                    }

                    return reported.GetString() switch
                    {
                        StatusHealthy => UpstreamReadiness.Healthy,
                        StatusDegraded => UpstreamReadiness.Degraded,
                        StatusUnhealthy => UpstreamReadiness.Unhealthy,

                        // A token from outside the published vocabulary is not trusted to mean ready.
                        // Failing closed here is the same choice every other arm of this method makes.
                        _ => UpstreamReadiness.Unhealthy,
                    };
                }
                catch (JsonException)
                {
                    // Not JSON, or truncated by the bound above. Either way no verdict was stated, and an
                    // unstated verdict is not a ready one.
                    return UpstreamReadiness.Unhealthy;
                }
            }
        }
    }
}


/// <summary>
/// One upstream's readiness as Gateway observed it, before it is projected onto the contract's wire
/// vocabulary.
/// </summary>
/// <remarks>
/// <para>
/// FOUR VALUES, WHICH IS THE PER-UPSTREAM VOCABULARY THE CONTRACT PUBLISHES - one wider than the
/// aggregate's, because an upstream can be <see cref="Unreachable"/> whereas the aggregate has only three
/// tokens and expresses that as a failure.
/// </para>
/// <para>
/// The member names are the wire tokens, but they are translated through an explicit switch rather than by
/// name, so a rename here could not silently change the published vocabulary.
/// </para>
/// </remarks>
public enum UpstreamReadiness
{
    /// <summary>
    /// The upstream reported that it is ready.
    /// </summary>
    Healthy,

    /// <summary>
    /// The upstream reported that it is not ready but has not failed - for instance, it is still
    /// completing its own startup validation.
    /// </summary>
    Degraded,

    /// <summary>
    /// The upstream answered and reported a failure, or answered with a status or vocabulary that cannot be
    /// read as ready.
    /// </summary>
    Unhealthy,

    /// <summary>
    /// No verdict could be obtained from the upstream at all - it could not be reached, it did not answer
    /// within the probe budget, or there was no channel on which to ask.
    /// </summary>
    /// <remarks>
    /// Distinguished from <see cref="Unhealthy"/> because the two call for different operator action: a
    /// network or startup-ordering problem rather than a failed dependency inside a running service.
    /// </remarks>
    Unreachable,
}

/// <summary>
/// Observes one upstream's anonymous readiness endpoint on Gateway's behalf.
/// </summary>
/// <remarks>
/// <para>
/// THE ONE SEAM THROUGH WHICH THE READINESS AGGREGATE CAN BE EXERCISED WITHOUT A NETWORK (constraint C-H).
/// Registering an implementation replaces the default HTTP probe wholesale, which is what lets a suite
/// reach every branch of the aggregate - all three upstreams ready, each one down individually, all three
/// down, a degraded upstream, an unreachable one and an expired probe budget - in process, with no live
/// upstream, no container and no certificate.
/// </para>
/// <para>
/// AN IMPLEMENTATION IS AN OBSERVER AND NOTHING MORE. It is handed the address of exactly one anonymous
/// endpoint and returns a verdict; it is not a client for the service behind that address, and Gateway
/// holds no such client for the upstream it does not call. An implementation must therefore present no
/// credential, invoke no capability, and return a verdict rather than throw - the aggregate is the endpoint
/// a container orchestrator's readiness gate probes, and a probe that faults takes the container with it.
/// </para>
/// </remarks>
public interface IUpstreamReadinessProbe
{
    /// <summary>
    /// Observes one upstream's readiness endpoint.
    /// </summary>
    /// <param name="participant">
    /// The upstream's logical name from the contract's closed enumeration - <c>persistence</c>,
    /// <c>dataservices</c> or <c>security</c>. Supplied for the operator record only: the caller builds the
    /// published entry from its own copy of the name, so an implementation cannot introduce a participant
    /// the contract does not declare.
    /// </param>
    /// <param name="probeAddress">
    /// The absolute address of that upstream's anonymous readiness endpoint, already composed from the
    /// configured base address.
    /// </param>
    /// <param name="cancellationToken">
    /// The probe budget, linked to the request. An implementation is expected to observe it and to report
    /// <see cref="UpstreamReadiness.Unreachable"/> when it expires rather than to propagate a cancellation.
    /// </param>
    /// <returns>The observed verdict.</returns>
    ValueTask<UpstreamReadiness> ProbeAsync(
        string participant,
        Uri probeAddress,
        CancellationToken cancellationToken);
}

/// <summary>
/// Gateway's readiness and that of each of its three upstreams individually: the body of
/// <c>200 GET /health</c>.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the contract's <c>AggregateHealthReport</c> schema member for member. Gateway reports
/// <c>Healthy</c> only after Persistence, DataServices and Security all do, and only <c>Healthy</c> is
/// answered with <c>200</c> - so this body is returned exclusively on the ready path, while the not-ready
/// path answers <c>503</c> with a problem document carrying the same information.
/// </para>
/// <para>
/// EVERYTHING IN THIS RECORD IS PUBLIC, because <c>GET /health</c> is anonymous. It therefore carries no
/// address, no port, no URL, no connection string, no credential, no configuration value, no exception
/// message and no stack trace, and no member of it can be used to enumerate internal topology. The
/// vocabulary is closed by the endpoint that builds it.
/// </para>
/// <para>
/// Declared in this file rather than in a file of its own: this folder permits exactly five files, and the
/// shape is small enough that inlining it keeps the whole operation reviewable in one place.
/// </para>
/// </remarks>
public sealed record AggregateHealthReport
{
    /// <summary>
    /// The value the contract pins for the reporting service.
    /// </summary>
    /// <remarks>
    /// Public so that the not-ready problem document can carry the same value without restating the
    /// literal. One spelling, one source.
    /// </remarks>
    public const string ReportingService = "gateway";

    /// <summary>
    /// The aggregate verdict: <c>Healthy</c>, <c>Degraded</c> or <c>Unhealthy</c>.
    /// </summary>
    /// <remarks>
    /// <c>Degraded</c> and <c>Unhealthy</c> are both answered with <c>503</c> - both mean not ready, and the
    /// orchestration gate observes the status code - so this member is what keeps the two distinguishable
    /// for an operator.
    /// </remarks>
    [JsonPropertyName("status")]
    [JsonRequired]
    public required string Status { get; init; }

    /// <summary>
    /// The reporting service, which the contract pins to a constant so a consumer can tell which listener
    /// answered.
    /// </summary>
    [JsonPropertyName("service")]
    [JsonRequired]
    public string Service { get; init; } = ReportingService;

    /// <summary>
    /// When this report was produced, so a consumer can detect a stale cached report.
    /// </summary>
    /// <remarks>
    /// Read from the injected <see cref="TimeProvider"/> rather than from the ambient system clock, so a
    /// test can substitute a deterministic clock and a characterization recording can mask the value.
    /// </remarks>
    [JsonPropertyName("checkedAt")]
    public DateTimeOffset CheckedAt { get; init; }

    /// <summary>
    /// The three upstreams Gateway aggregates, each named with its own state.
    /// </summary>
    /// <remarks>
    /// EXACTLY THREE: Persistence, DataServices and Security, which is what the contract bounds the array
    /// at. No entry exists for any deferred service, because no deferred service exists - and the reserved
    /// slot in the port band is a commented placeholder rather than a fourth participant.
    /// </remarks>
    [JsonPropertyName("upstreams")]
    [JsonRequired]
    public required IReadOnlyList<UpstreamHealth> Upstreams { get; init; }

    /// <summary>
    /// Gateway's own component checks, behind its own contribution to the verdict.
    /// </summary>
    /// <remarks>
    /// Named from a closed vocabulary the endpoint declares. Carries no configuration value, no connection
    /// string and no key material.
    /// </remarks>
    [JsonPropertyName("checks")]
    public IReadOnlyList<ComponentHealthCheck> Checks { get; init; } = [];
}

/// <summary>
/// One upstream's individually reported state within Gateway's aggregate.
/// </summary>
/// <remarks>
/// The aggregate names each upstream rather than returning one opaque verdict, because an operator reading
/// a failed aggregate needs to know WHICH upstream is responsible. Naming Persistence here is health
/// observation and not a call path: Gateway reads that service's anonymous readiness endpoint and holds no
/// client, channel or generated stub for it.
/// </remarks>
public sealed record UpstreamHealth
{
    /// <summary>
    /// Which upstream this entry describes: <c>persistence</c>, <c>dataservices</c> or <c>security</c>.
    /// </summary>
    /// <remarks>
    /// The enumeration is closed at three because the composition root depends on exactly these three.
    /// </remarks>
    [JsonPropertyName("service")]
    [JsonRequired]
    public required string Service { get; init; }

    /// <summary>
    /// This upstream's own verdict, or <c>Unreachable</c> when no verdict could be obtained at all.
    /// </summary>
    [JsonPropertyName("status")]
    [JsonRequired]
    public required string Status { get; init; }

    /// <summary>
    /// A human-readable note about this upstream's state, authored by Gateway from a closed vocabulary.
    /// </summary>
    /// <remarks>
    /// NEVER the upstream's own words and never a cause. It carries no connection string, no key, no
    /// configuration value, no address and no exception text; the cause of an unreachable upstream is
    /// written to the operator channel instead.
    /// </remarks>
    [JsonPropertyName("detail")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Detail { get; init; }
}

/// <summary>
/// One component check behind Gateway's own contribution to the verdict.
/// </summary>
/// <remarks>
/// Present so an operator can tell which component is not yet satisfied rather than only that something is
/// not. Named <c>ComponentHealthCheck</c> rather than after the contract's <c>HealthCheckResult</c> schema
/// because that name is taken by the shared framework's own result type, which this file consumes; the
/// member names on the wire are what the contract fixes, and they match it exactly.
/// </remarks>
public sealed record ComponentHealthCheck
{
    /// <summary>
    /// The check's identifier, from the closed vocabulary the endpoint declares.
    /// </summary>
    /// <remarks>
    /// NEVER a registration-supplied name. Those are authored by whatever registered the check rather than
    /// for disclosure, and this body is anonymous, so the registered names go to the operator channel and
    /// every registered check is represented here by one aggregated entry.
    /// </remarks>
    [JsonPropertyName("name")]
    [JsonRequired]
    public required string Name { get; init; }

    /// <summary>
    /// This check's own verdict: <c>Healthy</c>, <c>Degraded</c> or <c>Unhealthy</c>.
    /// </summary>
    [JsonPropertyName("status")]
    [JsonRequired]
    public required string Status { get; init; }

    /// <summary>
    /// A human-readable note, authored by Gateway from a closed vocabulary.
    /// </summary>
    /// <remarks>
    /// Carries no configuration value, no connection string and no key material - a health endpoint is
    /// anonymous, so anything it reports is public.
    /// </remarks>
    [JsonPropertyName("detail")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Detail { get; init; }
}
